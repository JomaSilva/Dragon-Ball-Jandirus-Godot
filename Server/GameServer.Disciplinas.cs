using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Ranks;
using Jandirus.Core.Social;
using Jandirus.Core.World;
using Jandirus.Net;

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
///
/// ============================ A PASSADA DE 2026-10-11: A LETRA DO DM ============================
/// O dono pediu a conferencia das passivas contra o DM. O que saiu dela, e esta escrito em cada metodo:
///   * a PASSIVA vale com o toggle ligado OU transformado (`ui_active || ui_form`, `ue_aura_on`) -- aqui so o
///     toggle a ligava, e quem estava no Perfected nao desviava de nada;
///   * a esquiva tem os portoes do `ui_try_evade` (meditando, treinando, carregando ki sem o Accelerated Battle) e
///     vale contra KI (30%), que nao existia;
///   * a aura devora e reduz o KI que chega, e a Hakai Infusion penetra e corta a deflexao -- as duas funcoes
///     existiam e ninguem as chamava;
///   * transformar RENOVA a energia (80 / 100) -- a funcao existia e ninguem a chamava;
///   * o FATOR DO KIT (`ue_kit_factor`) multiplica tudo o que a Destruicao faz, e o Deus que trilha o Instinto usa
///     a aura e a infusao EMPRESTADAS a 25%, movidas pela precisao dele.
/// ================================================================================================
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
	///
	/// ============================ DIVERGENCIA DECLARADA: O ALUNO NAO E PERGUNTADO ============================
	/// O DM pergunta antes (`alert(S, ...)`, `UltraInstinct.dm:131` e `UltraEgo.dm:149`), revalida a
	/// distancia depois do "Aceitar" (`:135`/`:153`) e ainda espera 5 s (`sleep(50)`, `:137`/`:155`). Aqui o
	/// ensino e NA HORA e sem aceite. Pesa mais do que parece: as duas disciplinas se EXCLUEM, entao ensinar
	/// uma sem perguntar fecha a outra no corpo de quem nao escolheu.
	/// ======================================================================================================
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

		// 1b. O MESTRE ESTA DE PE? `if(T2.dead || T2.KO) return` (`UltraInstinct.dm:114`, `UltraEgo.dm:132`), logo
		//     depois do `ui_learned`. O port conferia so o aluno: nocauteado ou morto, o mestre ensinava -- e o
		//     ensino daqui e NA HORA e tranca o aluno num dos dois caminhos.
		if (!CaidoAgeDeTeste && RecusarCaido(mestre, "nao da pra ensinar ninguem caido.")) return;

		// 2. QUEM ESTA DO LADO, com a lista do DM: `oview(1)` + `P.client` (`UltraInstinct.dm:116-117`,
		//    `UltraEgo.dm:134-135`). E um gesto e nao um golpe (ver `QuemEstaNaFrente`): o DM nao pergunta
		//    `attackable`, e o aluno no meio de uma cena de transformacao aprende. E so GENTE e discipulo --
		//    criatura do mundo, clone e corpo sem dono nunca estiveram na lista (ate 2026-09-24 o port os
		//    aceitava, pelo cone do soco). O resto do filtro do DM (`!P.dead && !P.KO && !P.ui_learned`)
		//    virou recusa com nome no `PodeAprender`: sem o menu do DM, o port escolhe um corpo so, e "ele ja
		//    sabe" diz mais que "ninguem ao seu lado".
		ServerPlayer? aluno = QuemEstaNaFrente(mestre, EhPessoa);
		if (aluno == null) { Avisar(mestre, "ninguem por perto pra ensinar."); return; }

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
	/// O CARGO ENTREGA A DISCIPLINA DELE A QUEM NAO TRILHOU NENHUMA -- os dois `login_check` do DM e a coroacao:
	///
	///     if(god_is_god(src) &amp;&amp; !ue_learned &amp;&amp; !ui_learned) ue_learned = 1     // `UltraEgo.dm:117-119`, `GodOfDestruction.dm:95-98`
	///     if(Angel_Rank == signature &amp;&amp; !ui_learned &amp;&amp; !ue_learned) ui_learned = 1   // `UltraInstinct.dm:99-101`
	///
	/// *"GoD SEM path: o titulo desperta o Power of Destruction (vira o path dele)"*; *"o Anjo SEMPRE possui o Ultra
	/// Instinto (mas paths sao exclusivos: quem ja trilha a Destruicao nao recebe)"*. E APRENDIZADO, e fica: perder o
	/// titulo nao desaprende (`UltraEgo.dm:7-8`).
	///
	/// Ate 2026-10-11 o cargo so deixava ENSINAR (<see cref="SabeDisciplina"/>): o primeiro Deus do servidor ensinava
	/// o Poder da Destruicao a todo mundo e nao tinha como liga-lo em si mesmo.
	///
	/// Idempotente, chamada da reconciliacao do cargo (login, 1 Hz, toda troca de trono): a frase sai uma vez so,
	/// porque depois dela o corpo ja aprendeu.
	/// </summary>
	private void ConcederADisciplinaDoCargo(ServerPlayer pl)
	{
		if (pl.UltraInstinct.Aprendida || pl.PoderDaDestruicao.Aprendida || pl.Conta.Length == 0) return;

		foreach (DisciplinaDef def in Disciplinas.Todas)
		{
			// (o trono DESTE cargo, direto: a reconciliacao roda pra todo mundo a cada segundo)
			if (!_tronos.TryGetValue(def.RankQueEnsina, out string? dono)
				|| !string.Equals(dono, pl.Conta, StringComparison.OrdinalIgnoreCase)) continue;

			EstadoDe(pl, def.Tipo).Aprendida = true;
			pl.Disciplina = def.Tipo;
			AplicarDisciplina(pl);
			Avisar(pl, def.Tipo == TipoDeDisciplina.PoderDaDestruicao
				? "o Poder da Destruição desperta em você: como Deus da Destruição, ele é parte de você. (os verbs estão na aba Skills)"
				: "como Anjo, o Ultra Instinto é parte de você. (os verbs estão na aba Skills)");
			GD.Print($"[server] {pl.Name}: o cargo '{def.RankQueEnsina}' entregou {def.Nome}");
			return;
		}
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
		return Disciplinas.FatorDoCargo(EhDeusDaDestruicao(pl), pl.PoderDaDestruicao.Aprendida);
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
	/// O QUE AS DISCIPLINAS GUARDAM DE UM CORPO SO ENQUANTO ELE ESTA NO MUNDO (os `mob/var/tmp` de aviso do DM, e as
	/// pilhas de esquiva de pe). Sai com o corpo -- ver `EsquecerOKitDoDeus`.
	/// </summary>
	private sealed class DisciplinaViva
	{
		/// <summary>`ue_proc_msg_cd` e `ui_evade_msg_cd`: ate quando a aura e a esquiva de ki ficam caladas.</summary>
		public long AuraCaladaAteMs, EsquivaCaladaAteMs;

		/// <summary>As pilhas do bonus pos-esquiva de pe: o buff de cada uma e quando ele sai.</summary>
		public readonly List<(string Buff, long AteMs)> Pilhas = [];
	}

	private readonly Dictionary<int, DisciplinaViva> _disciplinasVivas = [];

	private DisciplinaViva VivaDe(ServerPlayer pl)
	{
		if (!_disciplinasVivas.TryGetValue(pl.Id, out DisciplinaViva? v)) _disciplinasVivas[pl.Id] = v = new DisciplinaViva();
		return v;
	}

	// =====================================================================
	// O KIT DA DESTRUICAO -- `ue_kit_factor` e os tres vizinhos dele (`UltraEgo.dm:188-208`)
	// =====================================================================
	/// <summary>
	/// O FATOR DO KIT NESTE CORPO (`ue_kit_factor(M)`): 1,25 pro Deus que trilha a Destruicao, 1 pra quem a trilha,
	/// 0,25 pro Deus que trilha o Instinto (o kit EMPRESTADO pelo titulo), 0 pra quem nao tem o kit.
	/// </summary>
	private double FatorDoKitDaDestruicao(ServerPlayer pl) =>
		KitDoDeus.FatorDoKit(pl.PoderDaDestruicao.Aprendida, pl.UltraInstinct.Aprendida,
							 (pl.PoderDaDestruicao.Aprendida || pl.UltraInstinct.Aprendida) && EhDeusDaDestruicao(pl));

	/// <summary>
	/// A ENERGIA QUE MOVE O KIT (`ue_eff_energy`): a da Destruicao pra quem a trilhou, e a PRECISAO do Ultra Instinto
	/// pro Deus que o pega emprestado -- *"o GoD do Instinto canaliza o kit pela PROFICIENCIA do UI"* (`:197-199`).
	/// </summary>
	private static double EnergiaDoKit(ServerPlayer pl) =>
		pl.PoderDaDestruicao.Aprendida ? pl.PoderDaDestruicao.Atual : pl.UltraInstinct.Atual;

	/// <summary>A maestria REAL que destrava as faixas do kit (`ue_eff_real`, `:201-202`), pela mesma regra.</summary>
	private static double RealDoKit(ServerPlayer pl) =>
		pl.PoderDaDestruicao.Aprendida ? pl.PoderDaDestruicao.Real : pl.UltraInstinct.Real;

	/// <summary>
	/// A AURA OF DESTRUCTION ESTA VALENDO? -- o `ue_aura_on(M)` (`:205-208`): tem o kit, esta vivo, e o toggle esta
	/// ligado OU o corpo esta numa forma da destruicao. O toggle (`ue_active`) e o `PoderDaDestruicao.Ligada` nos dois
	/// casos: no kit emprestado ele mora no mesmo campo, com o `Aprendida` falso.
	/// </summary>
	private bool AuraDaDestruicaoLigada(ServerPlayer pl) =>
		!pl.Ficha.dead
		&& (pl.PoderDaDestruicao.Ligada || FormaDaDisciplina(pl, Disciplinas.PoderDaDestruicao) != null)
		&& FatorDoKitDaDestruicao(pl) > 0;

	/// <summary>
	/// O KIT EMPRESTADO MORRE COM O TITULO -- o ramo `if(!ue_learned)` do `god_strip_powers`
	/// (`GodOfDestruction.dm:145-148`): `ue_active = 0`, `ue_infuse_until = 0`, `ue_ego_mult = 1`. Quem trilha a
	/// Destruicao fica com o dele. Idempotente; chamada da reconciliacao do cargo.
	/// </summary>
	private void RecolherOKitEmprestado(ServerPlayer pl)
	{
		if (pl.PoderDaDestruicao.Aprendida || FatorDoKitDaDestruicao(pl) > 0) return;
		if (!pl.PoderDaDestruicao.Ligada && pl.HakaiAte == 0 && pl.Ficha.ue_ego_mult == 1) return;

		pl.PoderDaDestruicao.Ligada = false;
		pl.HakaiAte = 0;
		pl.HakaiRecargaAte = 0;
		pl.Ficha.ue_ego_mult = 1;
		pl.Ficha.Statify();
		AplicarDisciplina(pl);
	}

	/// <summary>
	/// LIGA E DESLIGA O TOGGLE da faixa 0% -- Autonomous Evasion ou Aura of Destruction.
	///
	/// Ele custa: a energia ATUAL cai enquanto ele esta ligado. Deixar ligado o tempo todo NAO e a
	/// jogada certa, e e isso que faz a disciplina ser uma decisao e nao um bonus permanente.
	///
	/// QUEM PODE: o Instinto, quem o aprendeu (`if(!usr.ui_learned) return`, `UltraInstinct.dm:235`); a Aura, quem
	/// TEM O KIT (`if(!ue_has_kit(usr)) return`, `UltraEgo.dm:360`) -- quem aprendeu a Destruicao, ou o Deus da
	/// Destruicao que trilha o Instinto, que a liga emprestada.
	///
	/// TRANSFORMADO NAO HA O QUE LIGAR (`:236-238` e `:361-363`): na forma a passiva ja vale inteira.
	/// </summary>
	private void AlternarDisciplina(ServerPlayer pl, TipoDeDisciplina tipo)
	{
		DisciplinaDef def = Disciplinas.Def(tipo)!;
		EstadoDeDisciplina est = EstadoDe(pl, tipo);
		bool daDestruicao = tipo == TipoDeDisciplina.PoderDaDestruicao;
		bool emprestada = daDestruicao && !est.Aprendida && FatorDoKitDaDestruicao(pl) > 0;

		if (!est.Aprendida && !emprestada) { Avisar(pl, $"voce nao domina o {def.Nome}."); return; }

		if (!ToggleLigaNaFormaDeTeste && FormaDaDisciplina(pl, def) != null)
		{
			Avisar(pl, daDestruicao
				? "transformado, a destruição já reveste o seu corpo por completo."
				: "transformado, o instinto já guia o seu corpo por completo.");
			return;
		}

		est.Ligada = !est.Ligada;
		if (!est.Ligada && daDestruicao) pl.Ficha.ue_ego_mult = 1;   // `usr.ue_ego_mult = 1` (`UltraEgo.dm:371`)
		AplicarDisciplina(pl);

		double energia = daDestruicao ? EnergiaDoKit(pl) : est.Atual;
		Avisar(pl, est.Ligada
			? $"{def.NomeDoToggle}: LIGADA. (precisao {energia:0}%, e ela cai enquanto estiver ligada)"
			  + (emprestada ? $" Canalizada pelo título a {KitDoDeus.EmprestimoDoCaminho * 100:0}% de eficiência." : "")
			: $"{def.NomeDoToggle}: desligada.");

		// O QUE QUEM ESTA VENDO LE (`UltraInstinct.dm:242`, `UltraEgo.dm:368`): ligar a passiva e um gesto a vista.
		if (est.Ligada)
			AvisarPertoG3(pl, RaioDaVista, daDestruicao
				? $"uma aura carmesim e faminta se acende em volta de {NomeVisivel(pl)}..."
				: $"os olhos de {NomeVisivel(pl)} ficam distantes e serenos...", excetoCentro: true);
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): o toggle liga e desliga dentro da forma, como ate 2026-10-11.
	/// Lido pelo <see cref="AlternarDisciplina"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool ToggleLigaNaFormaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): a passiva so vale com o TOGGLE -- transformado, o corpo nao desvia
	/// e a aura nao reduz nada. Era o jogo ate 2026-10-11. Lido pelo <see cref="InstintoDesvia"/> e pela
	/// <see cref="AplicarDisciplina"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool PassivaSoComToggleDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): a esquiva ignora os portoes do `ui_try_evade` -- o corpo desvia
	/// meditando, treinando e carregando ki sem o Accelerated Battle. Lido pelo <see cref="InstintoDesvia"/>.
	/// </summary>
	internal bool EsquivaSemPortoesDeTeste;

	/// <summary>
	/// O CORPO PODE DESVIAR SOZINHO AGORA? Os portoes do `ui_try_evade` (`UltraInstinct.dm:166-172`), respondidos com
	/// o estado deste corpo; a regra e o <see cref="Disciplinas.InstintoDesvia"/>. "Carregando ki" e o `charging ||
	/// beaming` de la: um raio na mao (carregando ou solto), uma tecnica sustentada (a Death Ball, a Genkidama, as
	/// rajadas) ou a carga de um tiro do kit do Deus.
	/// </summary>
	private bool InstintoDesvia(ServerPlayer pl)
	{
		EstadoDeDisciplina ui = pl.UltraInstinct;
		bool naForma = FormaDaDisciplina(pl, Disciplinas.UltraInstinct) != null;
		bool emUso = ui.Ligada || (naForma && !PassivaSoComToggleDeTeste);
		if (EsquivaSemPortoesDeTeste) return ui.Aprendida && emUso;

		bool carregando = _canais.ContainsKey(pl.Id) || BlastingG12(pl.Id) || CarregandoTiroDoKit(pl.Id);
		return Disciplinas.InstintoDesvia(ui.Aprendida, emUso, Caido(pl), pl.Ficha.med || pl.Ficha.train,
										  carregando, ui.Real);
	}

	/// <summary>
	/// ESCREVE O EFEITO DAS PASSIVAS NO CORPO. E o unico lugar que mexe na esquiva e na reducao da aura.
	///
	/// Chamado de TODO ponto que muda o estado (aprender, ligar, tique, forma), pelo mesmo motivo do
	/// `AplicarForma`: um efeito que so e reescrito na hora do clique fica congelado no valor velho
	/// enquanto a precisao cai, e a esquiva mediria a energia de dez minutos atras.
	///
	/// A ESQUIVA CONTRA SOCO e `ui_prof * UI_EVADE_MELEE_PCT` com os portoes do <see cref="InstintoDesvia"/>; a
	/// REDUCAO e `(UE_DR_BASE + energia * UE_DR_PER_EN) * f`, com o teto de 90% que o `max(1 - ..., 0.1)` do DM da
	/// (`UltraEgo.dm:217`) -- e as duas valem com o toggle ligado OU na forma.
	/// </summary>
	private void AplicarDisciplina(ServerPlayer pl)
	{
		if (pl.Combate == null) return;

		// A ESQUIVA AUTONOMA -- o campo ja existia no CombatState esperando alguem escrever nele.
		pl.Combate.ChanceEsquiva = InstintoDesvia(pl)
			? Disciplinas.ChanceDeEsquiva(pl.UltraInstinct.Atual, contraKi: false)
			: 0;

		// A REDUCAO DA AURA -- mesmo desenho: o Core le, este metodo escreve.
		bool aura = PassivaSoComToggleDeTeste
			? pl.PoderDaDestruicao is { Aprendida: true, Ligada: true }
			: AuraDaDestruicaoLigada(pl);
		pl.Combate.ReducaoDeDano = aura
			? (1 - Disciplinas.SobraDoDano(Disciplinas.ReducaoDeDano(EnergiaDoKit(pl)), FatorDoKitDaDestruicao(pl))) * 100
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
	///
	/// E A AURA EMPRESTADA DRENA A PRECISAO: o Deus que trilha o Instinto e liga a Aura of Destruction paga com a
	/// barra do Instinto (`ui_prof -= UE_DRAIN_ACTIVE`, `UltraEgo.dm:327-328`), por cima do que o proprio Instinto
	/// drena, e ela nao regenera enquanto a aura estiver de pe (`UltraInstinct.dm:218`).
	/// </summary>
	private void TickDasDisciplinas(ServerPlayer pl, double dt)
	{
		if (DisciplinaAtiva(pl) is not var (def, est)) return;

		bool emCombate = pl.Combate is { EmCombate: > 0 };
		bool emForma = FormaDaDisciplina(pl, def) != null;
		bool emUso = est.Ligada || emForma;
		bool auraEmprestada = def.Tipo == TipoDeDisciplina.UltraInstinct && pl.PoderDaDestruicao.Ligada
							  && FatorDoKitDaDestruicao(pl) > 0;

		ResetarPorFimDeLuta(pl);
		est.TickAtual(def, dt, emForma, emCombate,
					  drenoDeFora: auraEmprestada ? Disciplinas.PoderDaDestruicao.DrenoLigado : 0);

		// A MAESTRIA REAL: so LUTANDO, e so com a disciplina em uso. Quem deixa o toggle ligado
		// treinando num canto nao aprende nada -- e a diferenca entre a maestria de forma (que se
		// paga sustentando) e esta (que se paga apanhando).
		if (emCombate && emUso && SubirReal(pl, def, est, def.RealPorSegundo * dt)) { /* anunciado dentro */ }

		FecharAJanelaDoDisplay(pl, NowMs());
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
		// TREINAR COM O WHIS (ou com o Bills) ACELERA A MAESTRIA da disciplina deles -- pedido do dono em
		// 2026-10-11. O fator e 1 fora desse treino. Ver `GameServer.BillsTreino.AceleracaoDoTreinoDivino`.
		if (est.SubirReal(def, quanto * AceleracaoDoTreinoDivino(pl, def)) is not { } degrau) return false;

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
	/// O GATILHO DA AURA ENSINA A DESTRUICAO -- o `ue_real_gain(amount)` (`UltraEgo.dm:166-170`), com a guarda dele:
	/// *"kit EMPRESTADO (GoD do Instinto) nao acumula energia real"*.
	/// </summary>
	private void EnsinarADestruicao(ServerPlayer pl, double quanto)
	{
		if (pl.PoderDaDestruicao.Aprendida) SubirReal(pl, Disciplinas.PoderDaDestruicao, pl.PoderDaDestruicao, quanto);
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
	/// DEFEITO INJETADO (bancada `--godkitteste`): transformar nao renova a energia -- como ate 2026-10-11, quando
	/// este metodo existia e ninguem o chamava. Lido pelo <see cref="RenovarEnergiaNaForma"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool FormaNaoRenovaDeTeste;

	/// <summary>
	/// TRANSFORMAR RENOVA A ENERGIA ATUAL. Sign/Destroyer devolvem 80%, Perfected/Ultra Ego 100% -- `ui_prof = (stage
	/// == 2) ? UI_PROF_PERF_START : UI_PROF_SIGN_START` (`UltraInstinct.dm:466`; `UltraEgo.dm:540`).
	///
	/// ============================ E ESTA E A UNICA RENOVACAO EM COMBATE ============================
	/// A energia nao volta lutando -- de proposito. Entao a forma nao e so poder: ela e o botao de
	/// "recomecar o instinto" no meio da briga, e o custo e o dreno de Ki dela. Sem esta funcao a
	/// disciplina inteira viraria uma barra que so desce.
	/// ==========================================================================================
	///
	/// QUEM A CHAMA e o `EntrarNaForma` (`GameServer.Formas.cs`), pra toda forma: a que nao e de disciplina sai na
	/// primeira linha. Ate 2026-10-11 ninguem a chamava -- e, chamada, ela ainda cortaria a renovacao na maestria
	/// REAL, que o DM nao faz (o excesso escoa depois, fora de combate: `EstadoDeDisciplina.TickAtual`).
	/// </summary>
	private void RenovarEnergiaNaForma(ServerPlayer pl, string idDaForma)
	{
		if (FormaNaoRenovaDeTeste || DisciplinaAtiva(pl) is not var (def, est)) return;
		if (def.AtualAoEntrar(idDaForma) is not { } novo) return;

		est.Atual = novo;
		AplicarDisciplina(pl);
		Avisar(pl, $"a energia se renova: {est.Atual:0}%.");
	}

	// =====================================================================
	// OS GANCHOS DE COMBATE
	// =====================================================================
	/// <summary>
	/// O CORPO DESVIOU SOZINHO -- a cauda do `ui_try_evade` (`UltraInstinct.dm:175-176`) e o `ui_evade_stack`
	/// (`:180-191`). Chamado quando o <see cref="MeleeResolver"/> devolve a esquiva ATIVA e quando o tiro de ki e
	/// evitado (<see cref="EsquivouDoTiroPorInstinto"/>).
	///
	/// Paga a maestria REAL (0,2 por esquiva -- muito mais rapido que os 0,04/s de luta: o instinto
	/// aprende ESQUIVANDO, nao apanhando) e empilha o bonus:
	///
	///     if(ui_evade_stacks &gt;= UI_EVADE_STACK_MAX) return
	///     var/doff = Ephysoff * UI_EVADE_BONUS ; var/dspd = Espeed * UI_EVADE_BONUS
	///     Tphysoff += doff ; Tspeed += dspd
	///     spawn(UI_EVADE_BUFF_TICKS) ... -= ...          // CADA pilha sai sozinha, 5 s depois de entrar
	///
	/// ============================ O QUE ESTAVA DIFERENTE AQUI ============================
	/// O bonus era 0,05 CRU por pilha (e nao 5% do ataque e da velocidade EFETIVOS de agora), e as cinco caiam
	/// juntas quando a ultima vencia -- cada esquiva nova renovava o prazo de todas.
	///
	/// Cada pilha e um buff da casa (`LigarBuff`): e ele que guarda o que somou e tira exato, e que impede o numero
	/// de ir pro save com quem desloga no meio. Daí a unica diferenca: como todo buff, a pilha cai com o corpo
	/// nocauteado ou morto (no DM o `spawn` corre ate o fim).
	/// =====================================================================================
	/// </summary>
	private void AoEsquivarPorInstinto(ServerPlayer pl)
	{
		if (!pl.UltraInstinct.Aprendida) return;
		DisciplinaDef def = Disciplinas.UltraInstinct;

		DisciplinaViva viva = VivaDe(pl);
		if (viva.Pilhas.Count < Disciplinas.EsquivaPilhaMax)
		{
			string buff = "";
			for (int n = 1; n <= Disciplinas.EsquivaPilhaMax && buff.Length == 0; n++)
				if (!TemBuff(pl, $"UI_Esquiva_{n}")) buff = $"UI_Esquiva_{n}";

			if (buff.Length > 0 && LigarBuff(pl, buff, "o impulso da esquiva", new Dictionary<string, double>
				{
					["Tphysoff"] = pl.Ficha.Ephysoff * Disciplinas.EsquivaBonus,
					["Tspeed"] = pl.Ficha.Espeed * Disciplinas.EsquivaBonus,
				}))
				viva.Pilhas.Add((buff, NowMs() + (long)(Disciplinas.EsquivaBonusSegundos * 1000)));
		}
		pl.PilhasDeEsquiva = viva.Pilhas.Count;

		SubirReal(pl, def, pl.UltraInstinct, def.RealPorGatilho);
	}

	/// <summary>
	/// CADA PILHA DE ESQUIVA SAI NO PRAZO DELA -- o `spawn(UI_EVADE_BUFF_TICKS)` de cada uma. A que o nocaute ja
	/// derrubou (junto com todo buff do corpo) so sai da lista.
	/// </summary>
	private void TickDasPilhasDeEsquiva(ServerPlayer pl)
	{
		if (pl.PilhasDeEsquiva <= 0 || !_disciplinasVivas.TryGetValue(pl.Id, out DisciplinaViva? viva)) return;

		long agora = NowMs();
		for (int i = viva.Pilhas.Count - 1; i >= 0; i--)
		{
			(string buff, long ate) = viva.Pilhas[i];
			if (agora < ate && TemBuff(pl, buff)) continue;
			DesligarBuff(pl, buff);
			viva.Pilhas.RemoveAt(i);
		}
		pl.PilhasDeEsquiva = viva.Pilhas.Count;
	}

	/// <summary>
	/// A ESQUIVA AUTONOMA CONTRA KI -- `if(ui_try_evade(M, "blast")) { ui_blast_evade_fx(M, src); return 1 }`
	/// (`objects.dm:320-322`), chamada pelo `Acertar` depois do credito e do treino e ANTES do dano.
	///
	/// Os portoes sao os do soco (<see cref="InstintoDesvia"/>) mais o da guarda, que no tiro e literal
	/// (`if(M.blocking &amp;&amp; guard) return 0`, com `guard = 1`): de guarda erguida o corpo se comprometeu a aparar.
	/// A chance e `ui_prof * UI_EVADE_BLAST_PCT` (30% com a precisao cheia). Nao custa Ki.
	///
	/// O QUE A ZONA VE e o que ja se ve num raspao deste port: o corpo trocando pelas listras do Zanzoken
	/// (`flick('Zanzoken.dmi', M)`, `UltraInstinct.dm:196`). O passo de lado do DM (`:198-200`) nao acontece aqui
	/// pelo mesmo motivo do raspao (dono, 2026-09-25: a esquiva e desenhada, o corpo fica) -- e por isso o
	/// encontro se encerra pela mesma porta dele (`EncerrarOEncontroNoRaspao`). A frase sai no maximo de 3 em 3 s.
	/// </summary>
	private bool EsquivouDoTiroPorInstinto(Projetil p, ServerPlayer alvo, ServerPlayer dono)
	{
		if (alvo.Combate is not { Bloqueando: false } || !InstintoDesvia(alvo)) return false;
		if (!Sorteio(Disciplinas.ChanceDeEsquiva(alvo.UltraInstinct.Atual, contraKi: true))) return false;

		AoEsquivarPorInstinto(alvo);
		AnunciarGolpe(dono, alvo, new GolpeResultado { Desfecho = Desfecho.Esquivou, Membro = "" }, nivel: 1,
					  ponto: Feixe.PontoDoImpacto(p, alvo.Pos));

		DisciplinaViva viva = VivaDe(alvo);
		long agora = NowMs();
		if (agora >= viva.EsquivaCaladaAteMs)
		{
			viva.EsquivaCaladaAteMs = agora + (long)(Disciplinas.AvisoDaEsquivaSegundos * 1000);
			AvisarPertoG3(alvo, RaioDaVista, $"o corpo de {NomeVisivel(alvo)} se move sozinho -- o ataque corta o vazio!");
		}
		return true;
	}

	/// <summary>
	/// O QUE A AURA OF DESTRUCTION FAZ DEPOIS DE UM GOLPE DE CORPO: devolve o RECUO a quem bateu e guarda o golpe pra
	/// Destruction Explosion -- o `ue_melee_recoil` (`UltraEgo.dm:243-251`) e a segunda metade do `ue_aura_mitigate`
	/// (`:218-219`).
	///
	///     var/recoil = dmg * (UE_RECOIL_BASE + ue_eff_energy(M) * UE_RECOIL_PER_EN) * ue_kit_factor(M) / 100
	///     damage_m(K, recoil, "torso", 0, 0)        // nao-letal, na zona do torso
	///     M.ue_real_gain(UE_REAL_PER_PROC)
	///
	/// A REDUCAO DE DANO NAO ESTA AQUI de proposito: ela mora no `CombatState.ReducaoDeDano`, que
	/// o resolvedor le ANTES de ferir o corpo. Descontar depois seria curar, e um golpe fatal
	/// continuaria fatal. Aqui ficam so os efeitos que acontecem DEPOIS do golpe.
	///
	/// VALE COM A AURA VALENDO (<see cref="AuraDaDestruicaoLigada"/>: o toggle ou a forma), leva o fator do kit, e
	/// quem bateu le por que se machucou -- no maximo de 5 em 5 s (`ue_proc_msg_cd`).
	/// </summary>
	private void AplicarAuraDepoisDoGolpe(ServerPlayer alvo, ServerPlayer? atacante, double dano, bool ehKi)
	{
		if (!AuraDaDestruicaoLigada(alvo)) return;

		// `M.ue_last_hit = dmg` + *"apanhar com a aura tambem ensina (fracao)"* (`:218-219`).
		alvo.UltimoGolpeRecebido = dano;
		DisciplinaDef def = Disciplinas.PoderDaDestruicao;
		EnsinarADestruicao(alvo, def.RealPorGatilho * Disciplinas.FracaoDoGatilhoAoApanhar);

		// O RECUO no atacante MELEE. So melee: quem atira de longe nao encosta na aura.
		if (ehKi || atacante == null || atacante == alvo || atacante.Ficha.dead) return;

		double recuo = dano * Disciplinas.Recuo(EnergiaDoKit(alvo)) * FatorDoKitDaDestruicao(alvo) / 100.0;
		if (recuo <= 0) return;

		// PELO FUNIL DO DANO DIRETO (`FerirUmMembroG10`, o `damage_m` deste port): um corpo intocavel nao perde
		// vida nem por este caminho, e o atacante que cai pelo proprio golpe cai pela porta da derrota.
		FerirUmMembroG10(atacante, alvo, recuo, letal: false, zona: "torso");
		EnsinarADestruicao(alvo, def.RealPorGatilho);

		DisciplinaViva viva = VivaDe(alvo);
		long agora = NowMs();
		if (agora < viva.AuraCaladaAteMs) return;
		viva.AuraCaladaAteMs = agora + (long)(Disciplinas.AvisoDaAuraSegundos * 1000);
		Avisar(atacante, $"a aura de destruição de {NomeVisivel(alvo)} QUEIMA os seus golpes!");
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): o tiro de ki nao passa pela Destruicao -- a aura nao devora nem
	/// reduz, e a infusao nao penetra nem corta a deflexao. Era o jogo ate 2026-10-11 (as funcoes existiam, orfas).
	/// Lido pelo <see cref="DanoDeKiPelaDestruicao"/> e pelo <see cref="DeflexaoContraAInfusao"/>.
	/// </summary>
	internal bool KiIgnoraADestruicaoDeTeste;

	/// <summary>
	/// O QUE A DESTRUICAO FAZ COM UM TIRO DE KI QUE VAI FERIR -- o `ue_blast_mitigate(M, A, dmg)` (`UltraEgo.dm:223-240`),
	/// chamado pelo `Acertar` logo depois da conta do dano (`objects.dm:333`). Devolve o dano ja mexido.
	///
	///   1. A HAKAI INFUSION DE QUEM ATIROU penetra a defesa, com aura no alvo ou sem:
	///      `dmg *= 1 + (UE_HKI_PEN_MULT - 1) * ue_kit_factor(P)`.
	///   2. A AURA DE QUEM LEVA devora parte do ki E reduz o que sobra -- dois cortes, um depois do outro, cada um com
	///      o piso de 10% do DM:
	///      `dmg *= max(1 - (UE_KIEAT_BASE + en * UE_KIEAT_PER_EN) * f / 100, 0.1)`
	///      `dmg *= max(1 - (UE_DR_BASE + en * UE_DR_PER_EN) * f / 100, 0.1)`
	///      e o golpe fica guardado pra explosao, ensina a Destruicao (`UE_REAL_PER_PROC`) e a zona le que a aura
	///      devorou -- no maximo de 5 em 5 s.
	///
	/// (No DM a guarda e o gap de poder entram DEPOIS desta linha; aqui eles ja estao no numero que chega. E tudo
	/// multiplicacao: a ordem nao muda o resultado.)
	/// </summary>
	private double DanoDeKiPelaDestruicao(ServerPlayer alvo, ServerPlayer dono, double dano)
	{
		if (dano <= 0 || KiIgnoraADestruicaoDeTeste) return dano;

		if (HakaiAtivo(dono)) dano *= 1 + (Disciplinas.HakaiPenetracao - 1) * FatorDoKitDaDestruicao(dono);
		if (!AuraDaDestruicaoLigada(alvo)) return dano;

		double fator = FatorDoKitDaDestruicao(alvo), energia = EnergiaDoKit(alvo);
		dano *= Disciplinas.SobraDoDano(Disciplinas.KiDevorado(energia), fator);
		dano *= Disciplinas.SobraDoDano(Disciplinas.ReducaoDeDano(energia), fator);

		alvo.UltimoGolpeRecebido = dano;
		EnsinarADestruicao(alvo, Disciplinas.PoderDaDestruicao.RealPorGatilho);

		DisciplinaViva viva = VivaDe(alvo);
		long agora = NowMs();
		if (agora >= viva.AuraCaladaAteMs)
		{
			viva.AuraCaladaAteMs = agora + (long)(Disciplinas.AvisoDaAuraSegundos * 1000);
			AvisarPertoG3(alvo, RaioDaVista, $"a aura de destruição de {NomeVisivel(alvo)} DEVORA parte do ataque!");
		}
		return dano;
	}

	/// <summary>
	/// A CHANCE DE DEFLETIR UM TIRO INFUNDIDO -- o `ue_infuse_deflect(A, deflectchance)` (`UltraEgo.dm:254-258`):
	/// `deflectchance * max(1 - (1 - UE_HKI_DEFLECT_MULT) * ue_kit_factor(P), 0)`. Metade com o kit inteiro, 37,5%
	/// com o do Deus que trilha a Destruicao (*"corta mais fundo"*), 87,5% com o emprestado.
	/// </summary>
	private double DeflexaoContraAInfusao(ServerPlayer dono, double chance)
	{
		if (KiIgnoraADestruicaoDeTeste || !HakaiAtivo(dono)) return chance;
		return chance * Math.Max(1 - (1 - Disciplinas.HakaiDeflectDoAlvo) * FatorDoKitDaDestruicao(dono), 0);
	}

	/// <summary>
	/// O UNBOUND EGO: cada membro FERIDO vira BP. Faixa de 40% -- o `ue_ego_update()` (`UltraEgo.dm:292-316`).
	///
	/// Recalculado por tique porque o corpo muda por tique -- e o unico buff do jogo cujo valor
	/// depende de quanto voce esta quebrado.
	///
	/// VALE COM A AURA VALENDO (`ue_aura_on`), pela maestria que destrava o kit (`ue_eff_real`), e a conta e a do
	/// <see cref="Disciplinas.UnboundEgo"/>: a cauda fica de fora (`istype(B, /datum/Body/Tail)`, `:301`), o membro
	/// zerado ou decepado nao rende e derruba o "todos no pico", e o fator do kit multiplica o bonus.
	/// </summary>
	private void TickDoUnboundEgo(ServerPlayer pl)
	{
		bool ligado = pl.Combate != null && AuraDaDestruicaoLigada(pl) && RealDoKit(pl) >= Disciplinas.UeEgo;

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
		var perdidos = new List<bool>();
		foreach (BodyPart p in pl.Combate!.Corpo.Partes)
		{
			if (p.Nome == "Rabo") continue;
			fracoes.Add(p.Fracao);
			perdidos.Add(p.Decepado || p.Vida <= 0);
		}

		double fator = FatorDoKitDaDestruicao(pl);
		double mult = Disciplinas.UnboundEgo(fracoes, perdidos, fator, out bool todosNoPico);

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

		// TODOS OS MEMBROS NO PICO: uma vez por luta, devolve 25% (vezes o fator do kit) de Ki e de folego --
		// `Ki = min(Ki + MaxKi * UE_EGO_RESTORE * f / 100, MaxKi)` e a linha gemea da `stamina` (`:311-312`).
		if (todosNoPico && !pl.EgoRestauracaoUsada)
		{
			pl.EgoRestauracaoUsada = true;
			double fracao = Disciplinas.EgoRestaura * fator / 100.0;
			// O TETO LIMITA O QUE A RESTAURACAO ACRESCENTA, E NAO O QUE JA ESTAVA LA. Irmao do
			// `Nutricao.cs:134` e do `if (primeira)` do `GameServer.Formas.cs`: escrito como
			// `Math.Min(MaxKi, ...)` este PREMIO virava um CORTE -- quem chegasse ao pico do Ultra
			// Ego com o Ki comprimido a 140% pela tecla C recebia "25% de Ki" e SAIA com 100%.
			// O original nao corta (`Power Control.dm:113-134`: o excesso vaza devagar, nunca cai
			// de degrau). `Math.Max(MaxKi, Ki)` mantem a promessa dos dois lados -- a restauracao
			// enche ate o cheio e nunca tira.
			pl.Ficha.Ki = Math.Min(Math.Max(pl.Ficha.MaxKi, pl.Ficha.Ki), pl.Ficha.Ki + pl.Ficha.MaxKi * fracao);
			pl.Ficha.stamina = Math.Min(Math.Max(pl.Ficha.maxstamina, pl.Ficha.stamina),
										pl.Ficha.stamina + pl.Ficha.maxstamina * fracao);
			Avisar(pl, "UNBOUND EGO no limite! TODO o seu corpo grita, e o ego responde: a dor vira poder -- Ki e fôlego restaurados.");
			AvisarPertoG3(pl, RaioDaVista, $"{NomeVisivel(pl)} sorri em meio aos ferimentos -- a aura de destruição INCHA!",
						  excetoCentro: true);
		}
	}

	// =====================================================================
	// O GOLPE FATAL NEGADO -- e a DESTRUCTION EXPLOSION
	// =====================================================================
	/// <summary>
	/// A AURA NEGA A MORTE, uma vez por luta. Porte de `ue_deathsave_try()` (UltraEgo.dm:261).
	///
	/// Pendurado no <see cref="CombatState.NegarMorte"/>, que e a porta UNICA
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
	///
	/// QUEM TEM O KIT (`if(!ue_has_kit(src)) return 0`, `:263`): o toggle emprestado do Deus que trilha o Instinto
	/// tambem segura.
	/// </summary>
	private bool TentarNegarMorte(ServerPlayer pl)
	{
		EstadoDeDisciplina est = pl.PoderDaDestruicao;
		if (!est.Ligada || pl.AuraSalvouNestaLuta || FatorDoKitDaDestruicao(pl) <= 0) return false;
		if (pl.Combate == null || pl.Ficha.dead) return false;

		pl.AuraSalvouNestaLuta = true;
		est.Ligada = false;                    // a aura ESTOURA -- nao "gasta uma carga"

		// DE PE POR UM FIO. `B.health = max(B.health, B.maxhealth * 0.05)`.
		foreach (BodyPart p in pl.Combate.Corpo.Partes)
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
		GD.Print($"[server] {pl.Name}: a aura negou a morte (energia {EnergiaDoKit(pl):0}%)");
		return true;
	}

	/// <summary>Vida minima de cada membro depois do seguro. `B.maxhealth * 0.05`.</summary>
	private const double PisoDoSeguroDaAura = 0.05;

	/// <summary>
	/// A DESTRUCTION EXPLOSION -- `ue_destruction_explosion()` (UltraEgo.dm:276-287).
	///
	/// `ue_last_hit * (50 + energia * 0.5) * f / 100` em quem estiver a ate 3 tiles (`oview(UE_EXP_RANGE, src)`, a
	/// distancia de tabuleiro). Ela NAO e uma tecnica que se usa: e o que sobra da aura quando ela estoura, e por
	/// isso o dano vem do golpe que quase te matou -- quanto mais forte quem te derrubou, maior o troco.
	///
	/// O DANO E UM GOLPE DIRETO NUM MEMBRO, mirando o torso (`damage_m(V, dmg, "torso", murderToggle, 2)`, `:285`).
	/// Ate 2026-10-11 ele entrava em TODOS os membros de cada um (o funil do dano em area): o mesmo numero, uma
	/// dezena de vezes.
	/// </summary>
	private void DestructionExplosion(ServerPlayer pl)
	{
		double dano = pl.UltimoGolpeRecebido * Disciplinas.Explosao(EnergiaDoKit(pl))
					  * FatorDoKitDaDestruicao(pl) / 100.0;
		if (dano <= 0) return;

		int pegos = 0;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash).ToList())
		{
			if (o == pl || o.Ficha.dead || o.Combate is not { Intocavel: false }) continue;
			if (Fusao.DistanciaEmTilesDoDm(o.Pos, pl.Pos, ZoneCollision.TileSize) > Disciplinas.ExplosaoRaioTiles) continue;

			// `murderToggle` no DM: a explosao mata se a luta for letal. Aqui a letalidade e do
			// atacante, e quem estourou a aura e o atacante desta explosao.
			o.UltimoAgressor = pl.Id;   // `V.lastDamager = src` (`:284`)
			FerirUmMembroG10(o, pl, dano, pl.Combate?.Letal ?? false, zona: "torso");
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

	// =====================================================================
	// O CANAL DE HABILIDADE
	// =====================================================================
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

	// =====================================================================
	// O GODLY DISPLAY -- o verb `Godly_Display` (`UltraInstinct.dm:513-596`)
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): o primeiro toque marca todo mundo num raio de dez tiles, sem
	/// avancar -- como ate 2026-10-11. Lido pelo <see cref="AvancoDoDisplay"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool DisplayMarcaEmVoltaDeTeste;

	/// <summary>
	/// GODLY DISPLAY (faixa 80% do Ultra Instinct): dois toques.
	///
	/// O primeiro AVANCA dez tiles no rumo do olhar, atravessando e marcando quem estiver no caminho; o segundo,
	/// dentro de 5 s, despeja dez golpes leves em rodizio entre os marcados. Errar o segundo toque nao e de graca: a
	/// janela expira e a recarga de um minuto corre igual -- e o que faz a ativa ser uma aposta e nao um botao.
	///
	/// AS PORTAS, na ordem do verb (`:517-524`): saber o Instinto, a faixa de 80%, o corpo de pe e livre
	/// (`dead || KO || med || train || grabber`) e **o Instinto em uso** (*"Ative o Ultra Instinto primeiro."*) --
	/// esta ultima nao existia aqui.
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
		if (!CaidoAgeDeTeste)
		{
			if (!ProntoPraGolpeG3(pl, out string porque, exigirCanfight: false)) { Avisar(pl, porque); return; }
			if (Agarrado(pl)) { Avisar(pl, "nao da: voce esta preso nos bracos de alguem."); return; }
		}
		if (!est.Ligada && FormaDaDisciplina(pl, def) == null)
		{
			Avisar(pl, "ative o Ultra Instinto primeiro.");   // `:523`
			return;
		}

		long agora = NowMs();
		FecharAJanelaDoDisplay(pl, agora);

		// --- SEGUNDO TOQUE: a rajada (`:526-544`) -------------------------------------
		if (pl.GdJanelaAte != 0)
		{
			List<ServerPlayer> alvos = [];
			foreach (int id in pl.GdMarcados)
				if (CorpoNaMinhaZona(pl, id) is { } v && !v.Ficha.dead
					&& Fusao.DistanciaEmTilesDoDm(pl.Pos, v.Pos, ZoneCollision.TileSize) <= Disciplinas.GdAlcanceDoSegundoToque)
					alvos.Add(v);
			if (alvos.Count == 0) { Avisar(pl, "nenhum alvo marcado ao alcance."); return; }

			double custo = pl.Ficha.MaxKi * Disciplinas.GdCustoKiPct / 100.0;   // o custo SO existe no segundo toque
			if (pl.Ficha.Ki < custo) { Avisar(pl, $"Ki insuficiente pra fechar o Godly Display ({Disciplinas.GdCustoKiPct:0}% do maximo)."); return; }

			pl.Ficha.Ki -= custo;
			est.Atual = Math.Max(0, est.Atual - Disciplinas.GdCustoPrecisao);   // a exibicao divina cobra do corpo
			pl.GdMarcados.Clear();
			pl.GdJanelaAte = 0;
			pl.GdRecargaAte = agora + (long)(Disciplinas.GdRecargaSegundos * 1000);

			AvisarPertoG3(pl, RaioDaVista, $"{NomeVisivel(pl)} desaparece -- e o céu desaba sobre os seus inimigos!");
			RajadaDoDisplay(pl, alvos);
			AplicarDisciplina(pl);
			return;
		}

		// --- PRIMEIRO TOQUE: o avanco que marca (`:546-549`) --------------------------
		if (agora < pl.GdRecargaAte)
		{
			Avisar(pl, $"o instinto ainda se recompõe ({(pl.GdRecargaAte - agora) / 1000 + 1}s).");
			return;
		}
		AvancoDoDisplay(pl, agora);
	}

	/// <summary>
	/// O AVANCO -- `ui_gd_rush()` (`:551-578`): ate dez tiles em linha reta no rumo do olhar, parando na parede
	/// (`if(!T || T.density) break`), ATRAVESSANDO os corpos e marcando cada um que estava no caminho (*"bloquear NAO
	/// protege da marca"*; fora o morto e o intocavel, `:560`). A janela do segundo toque abre com ou sem marcado.
	///
	/// O "ESTAVA NO CAMINHO" e o corpo com o centro a ate meio tile da linha percorrida -- o `for(var/mob/V in T)`
	/// de cada tile pisado. No DM o avanco leva um tique por tile; aqui o corpo chega no mesmo tique (um salto de
	/// servidor, pela mesma porta das investidas: `CravarPosicao`).
	/// </summary>
	private void AvancoDoDisplay(ServerPlayer pl, long agora)
	{
		const int T = ZoneCollision.TileSize;
		AvisarPertoG3(pl, RaioDaVista, $"{NomeVisivel(pl)} avança como um raio prateado!");

		Vec2 rumo = MeleeArea.Frente(pl.Facing);
		Vec2 origem = pl.Pos, destino = pl.Pos;
		ZoneCollision? mapa = Voo.AtravessaCenario(pl.Altitude) ? null : MapaDoSalto(pl.Zone);
		for (int i = 1; i <= Disciplinas.GdAlcanceTiles; i++)
		{
			Vec2 passo = origem + rumo * (i * T);
			if (mapa != null && mapa.BlockedAt(passo)) break;
			destino = passo;
		}

		pl.GdMarcados.Clear();
		float percorrido = Vec2.Distance(origem, destino);
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
		{
			if (o == pl || o.Ficha.dead || o.Combate is not { Intocavel: false }) continue;
			if (!AlcancaPelaAltura(pl, o)) continue;

			Vec2 d = o.Pos - origem;
			bool noCaminho;
			if (DisplayMarcaEmVoltaDeTeste) noCaminho = d.Length <= Disciplinas.GdAlcanceTiles * T;
			else
			{
				float aoLongo = d.X * rumo.X + d.Y * rumo.Y;
				float deLado = MathF.Abs(d.X * rumo.Y - d.Y * rumo.X);
				noCaminho = aoLongo > 0 && aoLongo <= percorrido + T * 0.5f && deLado <= T * 0.5f;
			}
			if (!noCaminho) continue;

			pl.GdMarcados.Add(o.Id);
			Avisar(o, $"algo atravessa você como um vulto -- {NomeVisivel(pl)} marcou os seus movimentos!");
		}

		if (percorrido > 0 && !DisplayMarcaEmVoltaDeTeste) CravarPosicao(pl, destino);

		pl.GdJanelaAte = agora + (long)(Disciplinas.GdJanelaSegundos * 1000);
		Avisar(pl, pl.GdMarcados.Count > 0
			? $"{pl.GdMarcados.Count} alvo(s) marcado(s). Use o Godly Display DE NOVO em até {Disciplinas.GdJanelaSegundos:0}s "
			  + $"pra desferir os {Disciplinas.GdGolpes} golpes."
			: "ninguém no caminho do avanço.");
	}

	/// <summary>
	/// A JANELA DO SEGUNDO TOQUE FECHOU SEM ELE: expira e entra em recarga, sem custo -- o `spawn(UI_GD_MARK_TICKS +
	/// 2)` do avanco (`:573-578`). Chamado do tique das disciplinas e na porta do verb. A recarga conta do instante
	/// em que a janela fechou, e nao de quando alguem olhou.
	/// </summary>
	private void FecharAJanelaDoDisplay(ServerPlayer pl, long agora)
	{
		if (pl.GdJanelaAte == 0 || agora <= pl.GdJanelaAte) return;

		pl.GdRecargaAte = pl.GdJanelaAte + (long)(Disciplinas.GdRecargaSegundos * 1000);
		pl.GdJanelaAte = 0;
		pl.GdMarcados.Clear();
		Avisar(pl, "a janela do Godly Display se fecha.");
	}

	/// <summary>
	/// A RAJADA -- `ui_gd_flurry(targets)` (`:580-596`): DEZ golpes em rodizio entre os marcados
	/// (`targets[((h - 1) % targets.len) + 1]`), cada um `NormDamageCalc(V) * UI_GD_HIT_MULT` -- e `* UI_GD_BLOCK_MULT`
	/// se o alvo esta de guarda erguida --, direto num membro, mirando `pick("torso", "torso", "head")`. Sao golpes
	/// que nao se esquivam nem se aparam (o `damage_m` e o funil de depois da resolucao).
	///
	/// ============================ O QUE ESTAVA DIFERENTE AQUI ============================
	/// O dano era a conta "da casa" (`poder / defesa * 10`, que leva o gap de BP) aplicada em TODOS os membros do
	/// alvo, `10 / n` vezes por alvo (inteiro: com tres marcados saiam nove golpes), sem o desconto da guarda.
	/// =====================================================================================
	///
	/// No DM os dez golpes levam um segundo (um por tique, com um teleporte ao lado do alvo em cada); aqui saem no
	/// mesmo tique, e o corpo termina ao lado do ultimo alvo.
	/// </summary>
	private void RajadaDoDisplay(ServerPlayer pl, List<ServerPlayer> alvos)
	{
		bool letal = pl.Combate?.Letal ?? false;
		ServerPlayer? ultimo = null;
		for (int h = 0; h < Disciplinas.GdGolpes; h++)
		{
			ServerPlayer v = alvos[h % alvos.Count];
			if (v.Ficha.dead || v.Combate == null) continue;

			double dano = CombatMath.DanoBase(pl.Ficha, v.Ficha) * Disciplinas.GdDanoPorGolpe;
			if (v.Combate.Bloqueando) dano *= Disciplinas.GdBloqueioMult;   // bloquear protege dos golpes em si
			v.UltimoAgressor = pl.Id;
			FerirUmMembroG10(v, pl, dano, letal, zona: _rng.Next(3) == 2 ? "cabeca" : "torso");
			ultimo = v;
		}

		foreach (ServerPlayer v in alvos) Avisar(v, $"{NomeVisivel(pl)} esta em todo lugar de uma vez.");
		Avisar(pl, $"voce reaparece entre eles: {Disciplinas.GdGolpes} golpes em {alvos.Count} alvo(s).");

		// AO LADO DO ULTIMO ALVO (`loc = get_step(V, pick(NORTH, SOUTH, EAST, WEST))`, `:585-586`), se houver chao.
		if (ultimo == null) return;
		ZoneCollision? mapa = MapaDoSalto(pl.Zone);
		Vec2[] lados = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
		int primeiro = _rng.Next(lados.Length);
		for (int k = 0; k < lados.Length; k++)
		{
			Vec2 ao = ultimo.Pos + lados[(primeiro + k) % lados.Length] * ZoneCollision.TileSize;
			if (mapa != null && mapa.BlockedAt(ao)) continue;
			pl.Facing = MoveRules.FacingFrom(ultimo.Pos - ao, pl.Facing);
			CravarPosicao(pl, ao);
			break;
		}
	}

	// =====================================================================
	// A HAKAI INFUSION -- o verb `Hakai_Infusion` (`UltraEgo.dm:589-612`)
	// =====================================================================
	/// <summary>
	/// HAKAI INFUSION (faixa 80% do Poder da Destruicao): um minuto de ataques de ki infundidos.
	///
	/// O efeito nao e dano direto -- e penetracao (<see cref="DanoDeKiPelaDestruicao"/>) e a deflexao do alvo cortada
	/// (<see cref="DeflexaoContraAInfusao"/>). Ver <see cref="HakaiAtivo"/>, que e quem os dois leem.
	///
	/// QUEM PODE e quem tem o kit, pela maestria que o destrava (`if(!ue_has_kit(usr)) return` e `ue_eff_real(usr) &lt;
	/// UE_UNLOCK_HKI`, `:592-593`): o Deus que trilha o Instinto a usa emprestada, com 80% de maestria no Instinto.
	///
	/// FALTA, E ESTA ANOTADO: o `ue_clash_tick` (`:615-632`) -- num embate de raios, o lado infundido devora 5% do Ki
	/// do rival a cada 5 s e empurra o medidor. O embate deste port e outro sistema (letras), e o pulso nao veio.
	/// </summary>
	private void HakaiInfusion(ServerPlayer pl)
	{
		DisciplinaDef def = Disciplinas.PoderDaDestruicao;

		if (FatorDoKitDaDestruicao(pl) <= 0 || RealDoKit(pl) < Disciplinas.UeHakai)
		{
			Avisar(pl, $"a Hakai Infusion pede {Disciplinas.UeHakai:0}% de maestria no {def.Nome}.");
			return;
		}
		// `if(usr.dead || usr.KO || usr.med || usr.train) return` (`UltraEgo.dm:596`), depois do destrave e antes
		// de tudo o mais. O port nao perguntava estado nenhum: caido, morto ou meditando, a infusao acendia e
		// cobrava -- e o minuto dela corria com o corpo no chao.
		if (!CaidoAgeDeTeste && (Caido(pl) || pl.Ficha.med || pl.Ficha.train))
		{
			Avisar(pl, Caido(pl) ? "nao da, caido." : "nao da pra infundir o Hakai meditando ou treinando.");
			return;
		}
		// (a infusao de pe responde ANTES da recarga, como la -- `:597-602`: a recarga conta do fim dela, entao com
		// a ordem trocada quem apertava de novo no meio do minuto lia "ainda se recolhe")
		long agora = NowMs();
		if (agora < pl.HakaiAte) { Avisar(pl, "seus ataques ja carregam o Hakai."); return; }
		if (agora < pl.HakaiRecargaAte)
		{
			Avisar(pl, $"a destruicao ainda se recolhe ({(pl.HakaiRecargaAte - agora) / 1000 + 1}s).");
			return;
		}

		double custo = pl.Ficha.MaxKi * Disciplinas.HakaiCustoKiPct / 100.0;
		if (pl.Ficha.Ki < custo) { Avisar(pl, "Ki insuficiente."); return; }

		pl.Ficha.Ki -= custo;
		pl.HakaiAte = agora + (long)(Disciplinas.HakaiDuracaoSegundos * 1000);
		// A RECARGA CONTA DO FIM da infusao (`UE_HKI_CD` "contada do FIM"), nao do inicio.
		pl.HakaiRecargaAte = pl.HakaiAte + (long)(Disciplinas.HakaiRecargaSegundos * 1000);

		Avisar(pl, $"seus ataques de ki passam a carregar o Hakai por {Disciplinas.HakaiDuracaoSegundos:0}s.");
		AvisarPertoG3(pl, RaioDaVista, $"as mãos de {NomeVisivel(pl)} passam a verter uma luz púrpura e faminta...",
					  excetoCentro: true);
		GD.Print($"[server] {pl.Name}: Hakai Infusion");
	}

	/// <summary>
	/// Os ataques de ki deste corpo estao infundidos AGORA? (`P.ue_infuse_until > world.time`.) Lido pelos dois
	/// ganchos do tiro. O kit entra na pergunta porque o emprestado morre com o titulo.
	/// </summary>
	private bool HakaiAtivo(ServerPlayer pl) => NowMs() < pl.HakaiAte && FatorDoKitDaDestruicao(pl) > 0;
}
