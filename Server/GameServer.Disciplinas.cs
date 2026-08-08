using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS DISCIPLINAS DIVINAS, LADO DO SERVIDOR -- porte de `UltraInstinct.dm` e `UltraEgo.dm`.
///
/// As regras puras moram em <see cref="Disciplinas"/> (Core). Aqui mora o que e ESTADO e DECISAO:
/// quem ensina quem, o que o toggle faz no corpo, quando a maestria sobe e o que cada faixa liga.
///
/// ============================ UM MOTOR PRAS DUAS ============================
/// O `UltraEgo.dm` abre dizendo "o espelho do Ultra Instinct, mesma logica, disciplina oposta", e
/// isso e literal: as duas tem duas energias, cinco faixas, um toggle, duas formas e uma ativa. A
/// tentacao e escrever dois arquivos parecidos -- e o resultado conhecido e a correcao que entra
/// num e nao no outro. Aqui ha UM motor e duas fichas de dado.
///
/// O que e proprio de cada uma (a esquiva do UI, a aura do UE) mora nos ganchos de combate, que
/// sao poucos e nomeados.
/// ==========================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// A CADEIA DE ENSINO
	// =====================================================================
	/// <summary>
	/// ENSINAR A DISCIPLINA A QUEM ESTA DO LADO.
	///
	/// ============================ POR QUE NAO SE COMPRA ============================
	/// Nao ha marco de skill, nem loja, nem quest: o unico caminho e alguem que JA SABE te ensinar,
	/// e a raiz da cadeia e o cargo (o Anjo, o Deus da Destruicao). E a escolha de desenho do
	/// original, e ela e o que faz as duas disciplinas serem raras sem depender de numero nenhum --
	/// elas se espalham na velocidade em que as pessoas se encontram.
	/// ============================================================================
	/// </summary>
	private void EnsinarDisciplina(ServerPlayer mestre, TipoDeDisciplina tipo)
	{
		DisciplinaDef def = Disciplinas.Def(tipo)!;

		// 1. O MESTRE SABE? Ou sabe por ter aprendido, ou sabe por CARREGAR o cargo -- o portador do
		//    titulo sempre possui a disciplina, e perder o titulo nao desaprende (`UltraEgo.dm`).
		if (!SabeDisciplina(mestre, tipo))
		{
			Avisar(mestre, $"voce nao domina o {def.Nome}.");
			return;
		}

		ServerPlayer? aluno = AlvoNaFrente(mestre);
		if (aluno == null || aluno == mestre) { Avisar(mestre, "ninguem por perto pra ensinar."); return; }

		RecusaDeDisciplina r = PodeAprender(aluno, tipo);
		if (r != RecusaDeDisciplina.Pode)
		{
			Avisar(mestre, PorQueNaoAprende(r, def, aluno));
			return;
		}

		EstadoDeDisciplina est = EstadoDe(aluno, tipo);
		est.Aprendida = true;
		aluno.Disciplina = tipo;
		AplicarDisciplina(aluno);

		GD.Print($"[server] {mestre.Name} ensinou {def.Nome} a {aluno.Name}");
		Avisar(mestre, $"voce ensina o {def.Nome} a {aluno.Name}.");
		Avisar(aluno, $"{mestre.Name} te ensina o {def.Nome}. "
					+ $"Comece por '{def.NomeDoToggle}' -- o resto vem lutando com ela ligada.");
		aluno.SigAtributos = "";   // a aba precisa refletir a disciplina nova
	}

	/// <summary>
	/// Sabe a disciplina? APRENDEU **ou** carrega o cargo que sempre a possui.
	///
	/// A segunda metade importa: sem ela o primeiro Anjo do servidor nao teria de quem aprender, e a
	/// cadeia inteira nunca comecaria -- a disciplina existiria no codigo e em lugar nenhum no jogo.
	/// </summary>
	private bool SabeDisciplina(ServerPlayer pl, TipoDeDisciplina tipo)
	{
		if (EstadoDe(pl, tipo).Aprendida) return true;
		DisciplinaDef def = Disciplinas.Def(tipo)!;
		return string.Equals(CargoDe(pl.Conta), def.RankQueEnsina, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// O FATOR DO CARGO NA FORMA ATUAL -- 1,25 pro Deus da Destruicao que trilhou a destruicao e esta
	/// numa das duas formas dela; 1 pra todo o resto do jogo.
	///
	/// Ver <see cref="Disciplinas.AmpliacaoDoCargo"/>: a regra e o numero moram no Core, e este metodo
	/// so responde as duas perguntas que o Core nao pode fazer -- **o cargo mora na CONTA**, e o
	/// catalogo de formas nao conhece conta nenhuma.
	///
	/// A LINHA E A GUARDA, e nao o id: `ue_form_mult()` (`UltraEgo.dm:544`) so existe pras formas do
	/// Ultra Ego. Perguntar pela linha faz um degrau novo da destruicao ja nascer amplificado, e impede
	/// o fator de vazar pro Ultra Instinct -- que no DM nao tem `ui_form_mult` nenhum.
	/// </summary>
	private double FatorDoCargoNaForma(ServerPlayer pl)
	{
		if (pl.Forma.Def?.Linha != LinhaDeForma.UltraEgo) return 1;
		DisciplinaDef def = Disciplinas.PoderDaDestruicao;
		return Disciplinas.FatorDoCargo(
			string.Equals(CargoDe(pl.Conta), def.RankQueEnsina, StringComparison.OrdinalIgnoreCase),
			pl.PoderDaDestruicao.Aprendida);
	}

	/// <summary>
	/// O MULTIPLICADOR QUE O CORPO REALMENTE RECEBE: o da forma vezes o
	/// <see cref="FatorDoCargoNaForma"/>.
	///
	/// Existe pra o `ssjBuff` e as mensagens de tela sairem da MESMA conta. Quando o fator do cargo
	/// entrou, o `ssjBuff` sozinho passaria a valer 75x enquanto o chat continuaria anunciando 60x --
	/// e o jogador leria como bug do numero, nao como titulo.
	/// </summary>
	private double MultiplicadorDaForma(ServerPlayer pl) =>
		// O PERFIL INTEIRO E NAO SO O `SangueDiluido`: a curva do Mistico le a linhagem e a maestria
		// de ki divino de quem esta na forma (ver `FormaDef.EscalaComGodKi`), e o `Perfil` e o funil
		// unico que ja responde as duas. O booleano que estava aqui era um pedaco dele solto.
		pl.Forma.Multiplicador(Perfil(pl)) * FatorDoCargoNaForma(pl);

	private enum RecusaDeDisciplina
	{
		Pode = 0,
		JaSabe,
		SemGodKi,
		OutraDisciplina,   // as duas se excluem
		Caido,
	}

	private RecusaDeDisciplina PodeAprender(ServerPlayer pl, TipoDeDisciplina tipo)
	{
		if (pl.Ficha.KO || pl.Ficha.dead) return RecusaDeDisciplina.Caido;
		if (EstadoDe(pl, tipo).Aprendida) return RecusaDeDisciplina.JaSabe;

		// AS DUAS SE EXCLUEM. Ja valia pras FORMAS (`Catalogo.LinhasAbertas`); aqui vale pra skill,
		// que e onde a escolha de verdade acontece -- quem aprendeu uma nunca mais aprende a outra.
		if (EstadoDe(pl, Disciplinas.Oposta(tipo)).Aprendida) return RecusaDeDisciplina.OutraDisciplina;

		// `GODKI_UIUE_LEARN_PCT`: o corpo precisa aguentar o ki divino antes de aprender o que se faz
		// com ele.
		double godki = pl.Ficha.godki is { awakened: true } g ? g.mastery : -1;
		if (godki < Disciplinas.GodKiParaAprender) return RecusaDeDisciplina.SemGodKi;

		return RecusaDeDisciplina.Pode;
	}

	private static string PorQueNaoAprende(RecusaDeDisciplina r, DisciplinaDef def, ServerPlayer aluno) => r switch
	{
		RecusaDeDisciplina.JaSabe => $"{aluno.Name} ja domina o {def.Nome}.",
		RecusaDeDisciplina.Caido => $"{aluno.Name} nao esta em condicoes de aprender nada.",
		// A MENSAGEM DIZ O QUE ACONTECEU. "Nao pode aprender" manda a pessoa procurar um requisito
		// que ela nunca vai achar -- as duas disciplinas se excluem e isso e definitivo.
		RecusaDeDisciplina.OutraDisciplina =>
			$"{aluno.Name} ja trilhou a outra disciplina, e as duas se excluem. Nao ha volta.",
		RecusaDeDisciplina.SemGodKi =>
			$"{aluno.Name} ainda nao domina o ki divino o bastante "
			+ $"({Disciplinas.GodKiParaAprender:0}% de maestria divina).",
		_ => "agora nao.",
	};

	// =====================================================================
	// O ESTADO
	// =====================================================================
	private static EstadoDeDisciplina EstadoDe(ServerPlayer pl, TipoDeDisciplina tipo) =>
		tipo == TipoDeDisciplina.UltraInstinct ? pl.UltraInstinct : pl.PoderDaDestruicao;

	/// <summary>A disciplina que este corpo trilhou, se alguma.</summary>
	private static (DisciplinaDef Def, EstadoDeDisciplina Est)? DisciplinaAtiva(ServerPlayer pl)
	{
		if (pl.UltraInstinct.Aprendida) return (Disciplinas.UltraInstinct, pl.UltraInstinct);
		if (pl.PoderDaDestruicao.Aprendida) return (Disciplinas.PoderDaDestruicao, pl.PoderDaDestruicao);
		return null;
	}

	/// <summary>
	/// LIGA E DESLIGA O TOGGLE da faixa 0% -- Autonomous Evasion ou Aura of Destruction.
	///
	/// Ele custa: a energia ATUAL cai enquanto ele esta ligado. Deixar ligado o tempo todo NAO e a
	/// jogada certa, e e isso que faz a disciplina ser uma decisao e nao um bonus permanente.
	/// </summary>
	private void AlternarDisciplina(ServerPlayer pl, TipoDeDisciplina tipo)
	{
		DisciplinaDef def = Disciplinas.Def(tipo)!;
		EstadoDeDisciplina est = EstadoDe(pl, tipo);

		if (!est.Aprendida) { Avisar(pl, $"voce nao domina o {def.Nome}."); return; }

		est.Ligada = !est.Ligada;
		AplicarDisciplina(pl);

		Avisar(pl, est.Ligada
			? $"{def.NomeDoToggle}: LIGADA. (precisao {est.Atual:0}%, e ela cai enquanto estiver ligada)"
			: $"{def.NomeDoToggle}: desligada.");
	}

	/// <summary>
	/// ESCREVE O EFEITO DO TOGGLE NO CORPO. E o unico lugar que mexe na esquiva e na aura.
	///
	/// Chamado de TODO ponto que muda o estado (aprender, ligar, tique, forma), pelo mesmo motivo do
	/// `AplicarForma`: um efeito que so e reescrito na hora do clique fica congelado no valor velho
	/// enquanto a precisao cai, e a esquiva mediria a energia de dez minutos atras.
	/// </summary>
	private static void AplicarDisciplina(ServerPlayer pl)
	{
		if (pl.Combate == null) return;

		// A ESQUIVA AUTONOMA -- o campo ja existia no CombatState esperando alguem escrever nele.
		pl.Combate.ChanceEsquiva = pl.UltraInstinct is { Aprendida: true, Ligada: true } ui
			? Disciplinas.ChanceDeEsquiva(ui.Atual, contraKi: false)
			: 0;

		// A REDUCAO DA AURA -- mesmo desenho: o Core le, este metodo escreve.
		pl.Combate.ReducaoDeDano = pl.PoderDaDestruicao is { Aprendida: true, Ligada: true } ue
			? Disciplinas.ReducaoDeDano(ue.Atual)
			: 0;

		pl.SigAtributos = "";   // a aba mostra as duas energias
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>
	/// O TIQUE DAS DISCIPLINAS: drena, regenera, e faz a maestria REAL crescer em combate.
	///
	/// TRES REGRAS, e as tres sao do DM:
	///   1. a energia ATUAL drena com o toggle ligado e drena (menos) dentro da forma;
	///   2. ela NAO recupera em combate, e o teto da recuperacao e a maestria REAL;
	///   3. a maestria REAL so cresce LUTANDO com a disciplina em uso -- treinar nao adianta.
	/// </summary>
	private void TickDasDisciplinas(ServerPlayer pl, double dt)
	{
		if (DisciplinaAtiva(pl) is not var (def, est)) return;

		bool emCombate = pl.Combate is { EmCombate: > 0 };
		bool emForma = FormaDaDisciplina(pl, def) != null;
		bool emUso = est.Ligada || emForma;

		ResetarPorFimDeLuta(pl);
		est.TickAtual(def, dt, emForma, emCombate);

		// A MAESTRIA REAL: so LUTANDO, e so com a disciplina em uso. Quem deixa o toggle ligado
		// treinando num canto nao aprende nada -- e a diferenca entre a maestria de forma (que se
		// paga sustentando) e esta (que se paga apanhando).
		if (emCombate && emUso && SubirReal(pl, def, est, def.RealPorSegundo * dt)) { /* anunciado dentro */ }

		AplicarDisciplina(pl);
	}

	/// <summary>
	/// SOBE A MAESTRIA REAL e ANUNCIA a faixa cruzada. Devolve true quando cruzou.
	///
	/// O anuncio nao e enfeite: uma passiva que o jogador nao sabe que ganhou e indistinguivel de
	/// uma passiva que nao funciona. Este projeto ja teve exatamente esse defeito com as regras de
	/// exp condicionais.
	/// </summary>
	private bool SubirReal(ServerPlayer pl, DisciplinaDef def, EstadoDeDisciplina est, double quanto)
	{
		if (est.SubirReal(def, quanto) is not { } degrau) return false;

		Avisar(pl, $"** {def.Nome} -- {degrau.Pct:0}%: {degrau.Nome} **");
		Avisar(pl, degrau.Desc);
		GD.Print($"[server] {pl.Name}: {def.Nome} {degrau.Pct:0}% -> {degrau.Nome}");

		// A FAIXA QUE ABRE UMA FORMA marca a forma como alcancavel na aba -- o catalogo ja cobra a
		// maestria pelo `PedeProficienciaUi`/`PedeEnergiaUe`, entao nao ha nada a escrever ali; o
		// que muda e o jogador PODER ver.
		pl.SigAtributos = "";
		return true;
	}

	/// <summary>
	/// A forma desta disciplina em que o corpo esta, se alguma.
	///
	/// O laco que estava escrito aqui virou <see cref="Disciplinas.DaForma"/> (Core) quando a maestria
	/// das quatro formas divinas passou a ser da SKILL: era a MESMA pergunta -- "de que disciplina e
	/// esta forma?" -- respondida em dois lugares, e a segunda copia envelheceria calada no dia em que
	/// um degrau novo entrasse. Aqui sobrou o recorte: **desta** disciplina, e nao de qualquer uma.
	/// </summary>
	private static Degrau? FormaDaDisciplina(ServerPlayer pl, DisciplinaDef def) =>
		Disciplinas.DaForma(pl.Forma.Atual) is { } par && par.Def.Tipo == def.Tipo
			? par.Faixa
			: null;

	/// <summary>
	/// TRANSFORMAR RENOVA A ENERGIA ATUAL. Sign/Destroyer devolvem 80%, Perfected/Ultra Ego 100%.
	///
	/// ============================ E ESTA E A UNICA RENOVACAO EM COMBATE ============================
	/// A energia nao volta lutando -- de proposito. Entao a forma nao e so poder: ela e o botao de
	/// "recomecar o instinto" no meio da briga, e o custo e o dreno de Ki dela. Sem esta funcao a
	/// disciplina inteira viraria uma barra que so desce.
	/// ==========================================================================================
	/// </summary>
	private void RenovarEnergiaNaForma(ServerPlayer pl, string idDaForma)
	{
		if (DisciplinaAtiva(pl) is not var (def, est)) return;

		for (int i = 0; i < def.Degraus.Length; i++)
		{
			if (def.Degraus[i].Forma != idDaForma) continue;

			// a PRIMEIRA forma da linha devolve 80, a segunda 100 -- e a ordem no array e a da escada
			bool primeira = !def.Degraus.Take(i).Any(d => d.Forma.Length > 0);
			double novo = primeira ? def.AtualAoEntrarNaPrimeiraForma : def.AtualAoEntrarNaSegundaForma;

			// O TETO CONTINUA SENDO A MAESTRIA REAL. Quem domina 60% entra no Perfected com 60% de
			// precisao, nao com 100 -- senao a forma daria de graca o que a maestria cobra caro.
			est.Atual = Math.Min(Math.Max(est.Atual, novo), est.Real);
			AplicarDisciplina(pl);
			Avisar(pl, $"a energia se renova: {est.Atual:0}%.");
			return;
		}
	}

	// =====================================================================
	// OS GANCHOS DE COMBATE
	// =====================================================================
	/// <summary>
	/// O CORPO DESVIOU SOZINHO. Chamado quando o <see cref="Jandirus.Core.Combat.MeleeResolver"/>
	/// devolve <c>Esquivou</c> e a esquiva era a autonoma.
	///
	/// Paga a maestria REAL (0,2 por esquiva -- muito mais rapido que os 0,04/s de luta: o instinto
	/// aprende ESQUIVANDO, nao apanhando) e empilha o bonus de +5%.
	/// </summary>
	private void AoEsquivarPorInstinto(ServerPlayer pl)
	{
		if (!pl.UltraInstinct.Aprendida || !pl.UltraInstinct.Ligada) return;
		DisciplinaDef def = Disciplinas.UltraInstinct;

		SubirReal(pl, def, pl.UltraInstinct, def.RealPorGatilho);

		if (pl.PilhasDeEsquiva < Disciplinas.EsquivaPilhaMax)
		{
			pl.PilhasDeEsquiva++;
			pl.Ficha.Tphysoff += Disciplinas.EsquivaBonus;
			pl.Ficha.Tspeed += Disciplinas.EsquivaBonus;
			pl.Ficha.Statify();
		}
		pl.EsquivaAte = NowMs() + (long)(Disciplinas.EsquivaBonusSegundos * 1000);
	}

	/// <summary>
	/// AS PILHAS DE ESQUIVA CAEM JUNTAS quando a ultima expira.
	///
	/// Desfaz pelo VALOR QUE FOI APLICADO (o numero de pilhas x o bonus), nunca recalculando -- e a
	/// regra de todo buff deste projeto, e a que impede o atributo de derivar pra cima a cada ciclo.
	/// </summary>
	private void TickDasPilhasDeEsquiva(ServerPlayer pl)
	{
		if (pl.PilhasDeEsquiva <= 0 || NowMs() < pl.EsquivaAte) return;

		pl.Ficha.Tphysoff -= Disciplinas.EsquivaBonus * pl.PilhasDeEsquiva;
		pl.Ficha.Tspeed -= Disciplinas.EsquivaBonus * pl.PilhasDeEsquiva;
		pl.PilhasDeEsquiva = 0;
		pl.Ficha.Statify();
		pl.SigAtributos = "";
	}

	/// <summary>
	/// A AURA OF DESTRUCTION FILTRA O DANO QUE CHEGA. Devolve o dano JA reduzido.
	///
	/// `UE_DR_BASE 15` + `UE_DR_PER_EN 0.10` x energia: 15% parado, 25% com a energia cheia.
	/// Tambem guarda o golpe pra Destruction Explosion e devolve o RECUO pro atacante.
	/// </summary>
	private void AplicarAuraDepoisDoGolpe(ServerPlayer alvo, ServerPlayer? atacante, double dano, bool ehKi)
	{
		if (!alvo.PoderDaDestruicao.Aprendida || !alvo.PoderDaDestruicao.Ligada) return;
		double energia = alvo.PoderDaDestruicao.Atual;

		// A REDUCAO DE DANO NAO ESTA AQUI de proposito: ela mora no `CombatState.ReducaoDeDano`, que
		// o resolvedor le ANTES de ferir o corpo. Descontar depois seria curar, e um golpe fatal
		// continuaria fatal. Aqui ficam so os efeitos que acontecem DEPOIS do golpe.

		// 1. O RECUO no atacante MELEE. So melee: quem atira de longe nao encosta na aura.
		if (!ehKi && atacante != null && atacante != alvo)
		{
			double recuo = dano * Disciplinas.Recuo(energia) / 100.0;
			// O RECUO BATE NO TORSO, nao no "primeiro membro da lista" -- a lista comeca por onde o
			// corpo foi montado, e um aninhado (cerebro, orgao) levaria um dano que nao e dele.
			if (recuo > 0 && atacante.Combate?.Corpo.Achar("torso") is { } torso)
			{
				atacante.Combate.Corpo.Ferir(torso, recuo, letal: false);
				atacante.Combate.SincronizarVida();
			}
			SubirReal(alvo, Disciplinas.PoderDaDestruicao, alvo.PoderDaDestruicao,
					  Disciplinas.PoderDaDestruicao.RealPorGatilho);
		}

		alvo.UltimoGolpeRecebido = dano;
	}

	/// <summary>
	/// A AURA DEVORA ATAQUES DE KI -- `UE_KIEAT_BASE 20` + `UE_KIEAT_PER_EN 0.15` x energia.
	///
	/// Este e o unico efeito da aura que os funis de KI precisam chamar (o dano do soco ja passa
	/// pelo `CombatState.ReducaoDeDano`). Devolve o dano JA reduzido.
	/// </summary>
	private double AuraDevoraKi(ServerPlayer alvo, double dano)
	{
		if (!alvo.PoderDaDestruicao.Aprendida || !alvo.PoderDaDestruicao.Ligada) return dano;

		double energia = alvo.PoderDaDestruicao.Atual;
		double sobrou = dano * (1 - Disciplinas.KiDevorado(energia) / 100.0);

		SubirReal(alvo, Disciplinas.PoderDaDestruicao, alvo.PoderDaDestruicao,
				  Disciplinas.PoderDaDestruicao.RealPorGatilho);
		alvo.UltimoGolpeRecebido = sobrou;
		return sobrou;
	}

	/// <summary>
	/// O UNBOUND EGO: cada membro FERIDO vira BP. Faixa de 40%.
	///
	/// Recalculado por tique porque o corpo muda por tique -- e o unico buff do jogo cujo valor
	/// depende de quanto voce esta quebrado.
	/// </summary>
	private void TickDoUnboundEgo(ServerPlayer pl)
	{
		EstadoDeDisciplina est = pl.PoderDaDestruicao;
		DisciplinaDef def = Disciplinas.PoderDaDestruicao;

		bool ligado = est.Aprendida && (est.Ligada || FormaDaDisciplina(pl, def) != null)
					  && def.Destravou(est.Real, "Unbound Ego") && pl.Combate != null;

		if (!ligado)
		{
			if (pl.Ficha.ue_ego_mult != 1)
			{
				pl.Ficha.ue_ego_mult = 1;
				pl.Ficha.Statify();
			}
			return;
		}

		var fracoes = new List<double>();
		var quebrados = new List<bool>();
		foreach (Jandirus.Core.Combat.BodyPart p in pl.Combate!.Corpo.Partes)
		{
			fracoes.Add(p.Fracao);
			quebrados.Add(p.Decepado || p.Quebrado);
		}

		double mult = Disciplinas.UnboundEgo(fracoes, quebrados, out bool todosNoPico);

		// CAMPO PROPRIO, E ESCRITA ABSOLUTA. O DM tem `ue_ego_mult` separado justamente porque o
		// valor e recalculado do zero a cada tique (o corpo muda o tempo todo) -- somar num campo
		// compartilhado como o `MysticPcnt` faria o bonus se empilhar consigo mesmo e colidir com
		// quem tambem usa aquele campo.
		if (Math.Abs(mult - pl.Ficha.ue_ego_mult) > 1e-6)
		{
			pl.Ficha.ue_ego_mult = mult;
			pl.Ficha.Statify();
			pl.SigAtributos = "";
		}

		// TODOS OS MEMBROS NO PICO: uma vez por luta, devolve 25% de Ki e vigor.
		if (todosNoPico && !pl.EgoRestauracaoUsada)
		{
			pl.EgoRestauracaoUsada = true;
			pl.Ficha.Ki = Math.Min(pl.Ficha.MaxKi, pl.Ficha.Ki + pl.Ficha.MaxKi * Disciplinas.EgoRestaura / 100.0);
			Avisar(pl, "TODO o seu corpo grita, e o ego responde: a dor vira poder.");
		}
	}

	// =====================================================================
	// O CANAL DE HABILIDADE
	// =====================================================================
	// =====================================================================
	// O GOLPE FATAL NEGADO -- e a DESTRUCTION EXPLOSION
	// =====================================================================
	/// <summary>
	/// A AURA NEGA A MORTE, uma vez por luta. Porte de `ue_deathsave_try()` (UltraEgo.dm:261).
	///
	/// Pendurado no <see cref="Jandirus.Core.Combat.CombatState.NegarMorte"/>, que e a porta UNICA
	/// da morte -- entao ele vale contra soco, contra ki, contra explosao em area e contra a propria
	/// Final Explosion, sem uma linha em cada um.
	///
	/// ============================ TRES DETALHES QUE O DM DEIXA EXPLICITOS ============================
	///   1. **So o TOGGLE segura a morte.** `if(!ue_active ...) return 0` -- estar na Destroyer Form
	///      ou no Ultra Ego NAO da o seguro. Quem quer a rede tem que pagar o dreno da aura.
	///   2. **A aura e forcosamente encerrada.** Ela nao "gastou uma carga": ela ESTOUROU. Religar
	///      custa a energia de novo, e o seguro nao volta nesta luta.
	///   3. **Nao e cura.** Cada membro vai pra `max(vida, 5% da vida maxima)` -- fica de pe por um
	///      fio. Um golpe fraco depois disso mata. Curar aqui transformaria a aura num segundo
	///      corpo, que e outra mecanica.
	/// =============================================================================================
	/// </summary>
	private bool TentarNegarMorte(ServerPlayer pl)
	{
		EstadoDeDisciplina est = pl.PoderDaDestruicao;
		if (!est.Aprendida || !est.Ligada || pl.AuraSalvouNestaLuta) return false;
		if (pl.Combate == null || pl.Ficha.dead) return false;

		pl.AuraSalvouNestaLuta = true;
		est.Ligada = false;                    // a aura ESTOURA -- nao "gasta uma carga"

		// DE PE POR UM FIO. `B.health = max(B.health, B.maxhealth * 0.05)`.
		foreach (Jandirus.Core.Combat.BodyPart p in pl.Combate.Corpo.Partes)
		{
			if (p.Decepado) continue;
			double piso = p.VidaMax * PisoDoSeguroDaAura;
			if (p.Vida < piso) p.Vida = piso;
		}
		pl.Combate.SincronizarVida();
		AplicarDisciplina(pl);                 // a reducao e a esquiva caem junto com o toggle

		Avisar(pl, "a AURA OF DESTRUCTION NEGA o golpe fatal! Voce fica de pe por um fio -- e ela ESTOURA.");
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (o != pl) Avisar(o, $"a aura de {pl.Name} estoura numa DESTRUCTION EXPLOSION!");

		DestructionExplosion(pl);
		GD.Print($"[server] {pl.Name}: a aura negou a morte (energia {est.Atual:0}%)");
		return true;
	}

	/// <summary>Vida minima de cada membro depois do seguro. `B.maxhealth * 0.05`.</summary>
	private const double PisoDoSeguroDaAura = 0.05;

	/// <summary>
	/// A DESTRUCTION EXPLOSION -- `ue_destruction_explosion()` (UltraEgo.dm:276).
	///
	/// `ue_last_hit * (50 + energia * 0.5) / 100` num raio de 3 tiles. Ela NAO e uma tecnica que se
	/// usa: e o que sobra da aura quando ela estoura, e por isso o dano vem do golpe que quase te
	/// matou -- quanto mais forte quem te derrubou, maior o troco.
	/// </summary>
	private void DestructionExplosion(ServerPlayer pl)
	{
		double dano = pl.UltimoGolpeRecebido * Disciplinas.Explosao(pl.PoderDaDestruicao.Atual) / 100.0;
		if (dano <= 0) return;

		float raio = (float)(Disciplinas.ExplosaoRaioTiles * ZoneCollision.TileSize);
		int pegos = 0;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash).ToList())
		{
			if (o == pl || o.Ficha.dead || o.Combate is not { Intocavel: false }) continue;
			if (Vec2.Distance(o.Pos, pl.Pos) > raio) continue;

			// `murderToggle` no DM: a explosao mata se a luta for letal. Aqui a letalidade e do
			// atacante, e quem estourou a aura e o atacante desta explosao.
			EspalharDanoG3(o, pl, dano, pl.Combate?.Letal ?? false);
			MandarEfeito(o, "explosao_final", 500);
			Avisar(o, $"a aura de {pl.Name} estoura em cima de voce.");
			pegos++;
		}
		MandarEfeito(pl, "explosao_final", 500);
		GD.Print($"[server] Destruction Explosion de {pl.Name}: dano {dano:0}, {pegos} atingidos");
	}

	/// <summary>
	/// O SEGURO E A RESTAURACAO VOLTAM QUANDO A LUTA ACABA -- "1x por luta", e a luta e a TAG.
	///
	/// Sem isto os dois seriam "1x por vida do processo": quem gastou o seguro numa briga nunca
	/// mais o teria, e "uma vez por luta" viraria "uma vez, e pronto".
	/// </summary>
	private static void ResetarPorFimDeLuta(ServerPlayer pl)
	{
		if (pl.Combate is { EmCombate: > 0 }) return;
		pl.AuraSalvouNestaLuta = false;
		pl.EgoRestauracaoUsada = false;
	}

	/// <summary>Os verbs das duas disciplinas. Entram pelo mesmo cano de `UsarHabilidade`.</summary>
	private bool UsarDisciplina(ServerPlayer pl, string id)
	{
		switch (id)
		{
			case "ui_toggle": AlternarDisciplina(pl, TipoDeDisciplina.UltraInstinct); return true;
			case "ui_ensinar": EnsinarDisciplina(pl, TipoDeDisciplina.UltraInstinct); return true;
			case "ue_toggle": AlternarDisciplina(pl, TipoDeDisciplina.PoderDaDestruicao); return true;
			case "ue_ensinar": EnsinarDisciplina(pl, TipoDeDisciplina.PoderDaDestruicao); return true;
			case "ui_godlydisplay": GodlyDisplay(pl); return true;
			case "ue_hakai": HakaiInfusion(pl); return true;
			default: return false;
		}
	}

	/// <summary>
	/// GODLY DISPLAY (faixa 80% do Ultra Instinct): dois toques.
	///
	/// O primeiro AVANCA marcando todo mundo no caminho; o segundo, dentro de 5s, despeja dez golpes
	/// leves divididos entre os marcados. Errar o segundo toque nao e de graca: a janela expira e a
	/// recarga de um minuto corre igual -- e o que faz a ativa ser uma aposta e nao um botao.
	/// </summary>
	private void GodlyDisplay(ServerPlayer pl)
	{
		EstadoDeDisciplina est = pl.UltraInstinct;
		DisciplinaDef def = Disciplinas.UltraInstinct;

		if (!est.Aprendida || !def.Destravou(est.Real, "Godly Display"))
		{
			Avisar(pl, $"o Godly Display pede {Disciplinas.UiGd:0}% de maestria no {def.Nome}.");
			return;
		}
		long agora = NowMs();
		if (agora < pl.GdRecargaAte)
		{
			Avisar(pl, $"o instinto ainda se recompõe ({(pl.GdRecargaAte - agora) / 1000 + 1}s).");
			return;
		}

		// --- SEGUNDO TOQUE: a rajada -------------------------------------
		if (agora < pl.GdJanelaAte && pl.GdMarcados.Count > 0)
		{
			double custo = pl.Ficha.MaxKi * Disciplinas.GdCustoKiPct / 100.0;
			if (pl.Ficha.Ki < custo) { Avisar(pl, "Ki insuficiente pra fechar o Godly Display."); return; }

			pl.Ficha.Ki -= custo;
			est.Atual = Math.Max(0, est.Atual - Disciplinas.GdCustoPrecisao);

			// A FORMULA E A DA CASA (`power / defesa * 10`, misc.dm:311), com o 0,5 do
			// `UI_GD_HIT_MULT` por golpe. O dano do soco propriamente dito mora dentro do
			// MeleeResolver e nao e visivel daqui -- e uma tecnica nao deveria mesmo chamar o
			// resolvedor de socos, que sorteia membro e consulta guarda.
			int porAlvo = Math.Max(1, Disciplinas.GdGolpes / pl.GdMarcados.Count);
			foreach (int idAlvo in pl.GdMarcados)
			{
				if (!_players.TryGetValue(idAlvo, out ServerPlayer? alvo) || alvo.Combate == null) continue;
				if (alvo.Ficha.dead || alvo.Combate.Intocavel) continue;

				double baixo = Math.Max(alvo.Ficha.expressedBP * alvo.Ficha.Ephysdef, 1);
				double dano = pl.Ficha.expressedBP * pl.Ficha.Ephysoff / baixo * 10
							* Disciplinas.GdDanoPorGolpe;
				for (int i = 0; i < porAlvo; i++) EspalharDanoG3(alvo, pl, dano, pl.Combate?.Letal ?? false);
				Avisar(alvo, $"{pl.Name} esta em todo lugar de uma vez.");
			}

			Avisar(pl, $"voce reaparece entre eles: {Disciplinas.GdGolpes} golpes em {pl.GdMarcados.Count} alvo(s).");
			pl.GdMarcados.Clear();
			pl.GdJanelaAte = 0;
			pl.GdRecargaAte = agora + (long)(Disciplinas.GdRecargaSegundos * 1000);
			AplicarDisciplina(pl);
			return;
		}

		// --- PRIMEIRO TOQUE: o avanco que marca --------------------------
		pl.GdMarcados.Clear();
		float alcance = (float)(Disciplinas.GdAlcanceTiles * ZoneCollision.TileSize);
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
		{
			if (o == pl || o.Ficha.dead) continue;
			if (Vec2.Distance(o.Pos, pl.Pos) > alcance) continue;
			pl.GdMarcados.Add(o.Id);
			Avisar(o, "algo passa por voce rapido demais pra ver.");
		}

		if (pl.GdMarcados.Count == 0) { Avisar(pl, "voce avanca, e nao ha ninguem no caminho."); return; }

		pl.GdJanelaAte = agora + (long)(Disciplinas.GdJanelaSegundos * 1000);
		Avisar(pl, $"voce marca {pl.GdMarcados.Count} alvo(s). Toque de novo em "
				 + $"{Disciplinas.GdJanelaSegundos:0}s pra fechar.");
	}

	/// <summary>
	/// HAKAI INFUSION (faixa 80% do Poder da Destruicao): um minuto de ataques de ki infundidos.
	///
	/// O efeito nao e dano direto -- e penetracao, defesa cortada pela metade no alvo, e, num embate
	/// de beams, o seu DEVORANDO o do rival. Ver <see cref="HakaiAtivo"/>, que e quem os funis leem.
	/// </summary>
	private void HakaiInfusion(ServerPlayer pl)
	{
		EstadoDeDisciplina est = pl.PoderDaDestruicao;
		DisciplinaDef def = Disciplinas.PoderDaDestruicao;

		if (!est.Aprendida || !def.Destravou(est.Real, "Hakai Infusion"))
		{
			Avisar(pl, $"a Hakai Infusion pede {Disciplinas.UeHakai:0}% de maestria no {def.Nome}.");
			return;
		}
		long agora = NowMs();
		if (agora < pl.HakaiRecargaAte)
		{
			Avisar(pl, $"a destruicao ainda se recolhe ({(pl.HakaiRecargaAte - agora) / 1000 + 1}s).");
			return;
		}
		if (agora < pl.HakaiAte) { Avisar(pl, "seus ataques ja carregam o Hakai."); return; }

		double custo = pl.Ficha.MaxKi * Disciplinas.HakaiCustoKiPct / 100.0;
		if (pl.Ficha.Ki < custo) { Avisar(pl, "Ki insuficiente."); return; }

		pl.Ficha.Ki -= custo;
		pl.HakaiAte = agora + (long)(Disciplinas.HakaiDuracaoSegundos * 1000);
		// A RECARGA CONTA DO FIM da infusao (`UE_HKI_CD` "contada do FIM"), nao do inicio.
		pl.HakaiRecargaAte = pl.HakaiAte + (long)(Disciplinas.HakaiRecargaSegundos * 1000);

		Avisar(pl, $"seus ataques de ki passam a carregar o Hakai por {Disciplinas.HakaiDuracaoSegundos:0}s.");
		GD.Print($"[server] {pl.Name}: Hakai Infusion");
	}

	/// <summary>Os ataques de ki deste corpo estao infundidos AGORA? Lido pelos funis de dano.</summary>
	private bool HakaiAtivo(ServerPlayer pl) =>
		pl.PoderDaDestruicao.Aprendida && NowMs() < pl.HakaiAte;
}
