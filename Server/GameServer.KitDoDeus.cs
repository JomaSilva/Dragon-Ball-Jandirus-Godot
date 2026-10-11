using Godot;
using Jandirus.Core;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Ranks;
using Jandirus.Core.Social;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// O KIT DO DEUS DA DESTRUICAO, LADO DO SERVIDOR -- porte de `Code/Modules/Ranks/GodOfDestruction.dm`.
///
/// Os numeros e as contas puras moram em <see cref="KitDoDeus"/> (Core). Aqui mora o que e do servidor: QUEM
/// carrega o titulo, o que entra e o que sai com ele, e os verbs do kit.
///
/// ============================ O TITULO NAO E DO PERSONAGEM ============================
/// *"Perdeu o titulo = perde TUDO na hora (verbs removidos, Fury cai)"* (`GodOfDestruction.dm:10`). Por isso nada
/// daqui e skill de livro: cada gesto pergunta <see cref="EhDeusDaDestruicao"/> na hora de usar, e a
/// <see cref="ReconciliarOKitDoDeus"/> -- chamada no login, a cada segundo e em toda troca de trono, de dentro da
/// `ReconciliarDadiva` -- desfaz o que nao pode sobreviver ao trono.
///
/// O que ja tinha casa continua onde estava: o duelo, as tarefas e o painel (`GameServer.CargoDuelo.cs`), o folego
/// no vacuo (`GameServer.Vacuo.cs`) e o 1,25 das formas do Poder da Destruicao (`GameServer.Disciplinas.cs`).
/// ======================================================================================
///
/// ============================ O CORPO SEM DONO TAMBEM PODE SER O DEUS ============================
/// O cargo mora na CONTA (`_tronos`), e um NPC nao tem conta. Pra um corpo do mundo carregar o kit (o NPC que
/// guarda o trono enquanto nenhum jogador o tomou) ha a <see cref="ConsagrarDeusSemDono"/>: uma lista de corpos,
/// na memoria, que respondem "sim" a <see cref="EhDeusDaDestruicao"/>. Ela NAO escreve no `_tronos` -- o trono
/// de verdade continua vago pra quem for disputa-lo. Todo verb daqui serve a esse corpo: quem o dirige chama o
/// metodo do verb (ou o <see cref="UsarKitDoDeus"/>, com o id), e as recusas simplesmente nao tem tela pra onde ir.
/// =================================================================================================
///
/// ============================ O QUE ESTE PORT COBRA A MAIS QUE O DM ============================
/// No DM cinco dos seis verbs abrem so com `if(!god_is_god(usr)) return`: nocauteado, morto ou meditando, o Deus
/// executa, peteleca e anula. Aqui vale a regra da casa (`GameServer.Validacoes.cs`: "o corpo pode?") -- o Hakai
/// e o Power Flick passam pela guarda dos verbs de agarrao (<see cref="ProntoPraGolpeG3"/>, sem o atordoamento), os
/// dois tiros pela dos tiros (`PodeAtirar`, que e a do DM mais a morte e o atordoamento) e a anulacao recusa o
/// corpo caido. A Fury continua a do DM: ela nao pergunta estado nenhum.
/// ================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>A recusa de todo verb do kit pra quem nao carrega o titulo (`if(!god_is_god(usr)) return`).</summary>
	private const string SoODeusDaDestruicao = "isso é do Deus da Destruição: só quem carrega o título.";

	/// <summary>Os corpos SEM DONO consagrados (ids de corpo). Ver o cabecalho.</summary>
	private readonly HashSet<int> _deusesSemDono = [];

	/// <summary>
	/// ESTE CORPO E O DEUS DA DESTRUICAO AGORA? -- o `god_is_god(M)` do DM (`GodOfDestruction.dm:69-70`).
	///
	/// Pela CONTA, como todo cargo, ou pela consagracao de um corpo sem dono. E a pergunta que todo verb do kit faz
	/// antes de qualquer outra coisa -- e a que o funil de dano faz a cada golpe (o God's Temper), por isso ela le o
	/// trono DESTE cargo direto, sem varrer o livro inteiro como o `CargoDe`.
	/// </summary>
	internal bool EhDeusDaDestruicao(ServerPlayer pl) =>
		_deusesSemDono.Contains(pl.Id)
		|| (pl.Conta.Length > 0 && _tronos.TryGetValue(KitDoDeus.Cargo, out string? dono)
			&& string.Equals(dono, pl.Conta, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// FAZ DE UM CORPO SEM DONO UM DEUS DA DESTRUICAO (ou desfaz). So vale pra corpo sem `Peer`: jogador ganha o
	/// titulo pelas portas de cargo (`Outorgar`, `Entronizar`), que gravam, anunciam e entregam o kit.
	///
	/// Desconsagrar derruba a Fury na hora e esquece o estado do kit, pela mesma regra do titulo que cai.
	/// </summary>
	internal void ConsagrarDeusSemDono(ServerPlayer npc, bool consagrado = true)
	{
		// (ids de corpo que ja sairam do mundo nao ficam na lista: ela e pequena, e a faxina e aqui)
		_deusesSemDono.RemoveWhere(id => !_players.ContainsKey(id));
		if (npc.Peer != null) return;

		if (consagrado) { _deusesSemDono.Add(npc.Id); return; }
		_deusesSemDono.Remove(npc.Id);
		_kitDoDeus.Remove(npc.Id);
		if (EstaNaFury(npc)) Reverter(npc, "sem o título, a Fúria morre na hora.");
	}

	/// <summary>QUEM SAIU LEVA O ESTADO DELE JUNTO -- inscrito no `EsquecerTecnicas`.</summary>
	private void EsquecerOKitDoDeus(int id)
	{
		_deusesSemDono.Remove(id);
		_kitDoDeus.Remove(id);
		_deusesLembradosDaTarefa.Remove(id);
		_disciplinasVivas.Remove(id);   // os avisos e as pilhas de esquiva das disciplinas (`GameServer.Disciplinas.cs`)
	}

	// =====================================================================
	// O ESTADO DE SESSAO -- os `mob/var/tmp` do DM (`:60-67`)
	// =====================================================================
	/// <summary>
	/// O QUE O KIT GUARDA DE UM CORPO ENQUANTO ELE ESTA NO MUNDO. Nada disto vai pro save, como la (`mob/var/tmp`):
	/// relogar zera o contador do surto e a recarga da anulacao.
	/// </summary>
	private sealed class EstadoDoKit
	{
		/// <summary>`god_temper_hits`: danos seguidos sem guarda.</summary>
		public int GolpesDoTemper;

		/// <summary>`god_temper_cd`: ate quando o surto nao dispara de novo.</summary>
		public long TemperRecargaAteMs;

		/// <summary>`god_nullify_next`: quando a Energy Nullification volta.</summary>
		public long AnulacaoProntaEmMs;

		/// <summary>O `sleep` dos dois tiros: quando a carga acaba (0 = nao ha tiro na mao) e qual deles e.</summary>
		public long CargaAteMs;
		public DomDeProjetil CargaDe;
	}

	private readonly Dictionary<int, EstadoDoKit> _kitDoDeus = [];

	private EstadoDoKit KitDe(ServerPlayer pl)
	{
		if (!_kitDoDeus.TryGetValue(pl.Id, out EstadoDoKit? e)) _kitDoDeus[pl.Id] = e = new EstadoDoKit();
		return e;
	}

	// =====================================================================
	// O QUE ENTRA E O QUE SAI COM O TITULO
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): a Fury sobrevive ao titulo -- quem foi destituido transformado
	/// continua em 72x. Lido pela <see cref="ReconciliarOKitDoDeus"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool FuryFicaSemOTituloDeTeste;

	/// <summary>
	/// PoE ESTE CORPO DE ACORDO COM O TRONO DE AGORA. Idempotente e derivada, pelo mesmo argumento da
	/// `ReconciliarDadiva` (de onde ela e chamada): um cargo troca de dono por seis caminhos, e um deles -- perder o
	/// titulo OFFLINE -- nao e evento nenhum.
	///
	/// A FURY CAI COM O TITULO: e o `Loop()` do buff (`if(!god_is_god(container)) DeBuff()`,
	/// `GodOfDestruction.dm:402-403`) e o `stopbuff` do `god_strip_powers` (`:144`). O contador do surto, a recarga
	/// da anulacao e a carga de um tiro saem junto (`god_temper_armed = 0`, `:142`); o surto que ja estava de pe
	/// acaba no prazo dele, como la (o `spawn` do `:376` nao e cancelado).
	/// </summary>
	private void ReconciliarOKitDoDeus(ServerPlayer pl)
	{
		// OS CAMINHOS (`god_gain_title`, `:93-102`; `ue_login_check` e `ui_login_check`): quem ja trilha um ouve o
		// que o trono faz com ele; quem nao trilha nenhum recebe a disciplina do cargo. Nesta ordem -- o que acabou
		// de receber ouve so a frase do despertar, como la (o `if / else if` de `:95-102`).
		SaudarOCaminhoDoDeus(pl);
		ConcederADisciplinaDoCargo(pl);
		RecolherOKitEmprestado(pl);

		if (EhDeusDaDestruicao(pl))
		{
			LembrarATarefaNoLogin(pl);
			return;
		}

		_deusesLembradosDaTarefa.Remove(pl.Id);
		_kitDoDeus.Remove(pl.Id);
		if (!FuryFicaSemOTituloDeTeste && EstaNaFury(pl)) Reverter(pl, "sem o título, a Fúria morre na hora.");
	}

	/// <summary>
	/// Os corpos que ja ouviram, NESTA ENTRADA no mundo, o que o trono espera deles. O corpo sai da lista quando sai
	/// do mundo (<see cref="EsquecerOKitDoDeus"/>) e quando perde o titulo.
	/// </summary>
	private readonly HashSet<int> _deusesLembradosDaTarefa = [];

	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): o Deus entra no mundo e ninguem lhe diz da tarefa pendente. Lido
	/// pela <see cref="LembrarATarefaNoLogin"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool LoginCalaATarefaDeTeste;

	/// <summary>
	/// O LEMBRETE DO LOGIN -- `if(god_task_type) to_chat(src, "GoD: tarefa pendente -- [god_task_desc()]. Prazo:
	/// [god_time_left(god_task_deadline)].")` (`god_login_check`, `GodOfDestruction.dm:168-171`). O resto daquela proc
	/// (re-armar os verbs do portador, limpar o ex-portador) e a propria reconciliacao: aqui o kit e DERIVADO do
	/// trono, e nao ha o que re-armar.
	///
	/// A reconciliacao roda a cada segundo, entao a borda e do CORPO: a primeira vez que ela encontra este corpo
	/// portando o titulo -- o login de quem ja era o Deus, e tambem a coroacao de quem herda um trono com a tarefa de
	/// pe. Sem tarefa nao ha o que dizer, e o corpo fica anotado do mesmo jeito: a tarefa sorteada DEPOIS ja e
	/// anunciada na hora pelo `SortearTarefaDoDeus`.
	///
	/// So pra quem porta o titulo PELA CONTA: a tarefa e do trono, e o corpo sem dono consagrado
	/// (<see cref="ConsagrarDeusSemDono"/>) nao responde por ela.
	/// </summary>
	private void LembrarATarefaNoLogin(ServerPlayer pl)
	{
		if (LoginCalaATarefaDeTeste || _deusesSemDono.Contains(pl.Id)) return;
		if (!_deusesLembradosDaTarefa.Add(pl.Id)) return;

		// (`god_task_desc`, `:523-526` -- os mesmos dois textos do verb de status, `VerboStatusDoTitulo`)
		string tarefa = _tarefaAlvo.Length > 0 ? $"ELIMINAR a ameaça {_tarefaNomeDoAlvo}"
					  : _tarefaPlanetaChave.Length > 0 ? $"DESTRUIR o mundo {_tarefaPlanetaNome}"
					  : "";
		if (tarefa.Length == 0) return;

		Avisar(pl, $"Deus da Destruição: tarefa pendente -- {tarefa}. Prazo: {Duelo.TempoQueFalta(_tarefaPrazo, NowMs())}.");
	}

	/// <summary>
	/// A conta que este arquivo viu no trono por ultimo (nulo = ainda nao olhou desde que o servidor subiu). E o que
	/// separa COROAR de RELOGAR: as frases da coroacao saem quando o trono troca de mao, e nao a cada login do Deus.
	/// </summary>
	private string? _tronoVistoPeloKit;

	/// <summary>
	/// O QUE O NOVO DEUS OUVE AO SER COROADO, pelo caminho que ele ja trilha -- as duas frases de `:99-102` e a de
	/// `:105`. Quem nao trilha nenhum ouve a do despertar, do <see cref="ConcederADisciplinaDoCargo"/>.
	///
	/// A reconciliacao roda pra todo mundo a cada segundo, entao a borda e do TRONO e nao do corpo: so fala quando a
	/// conta que esta la deixou de ser a que este arquivo tinha visto. A primeira olhada depois do boot so anota.
	/// </summary>
	private void SaudarOCaminhoDoDeus(ServerPlayer pl)
	{
		string dono = _tronos.TryGetValue(KitDoDeus.Cargo, out string? conta) ? conta : "";
		if (dono.Length == 0 || !string.Equals(dono, pl.Conta, StringComparison.OrdinalIgnoreCase))
		{
			// (este corpo nao e o dono: so se anota o trono VAGO, ou o dono que estava la na primeira olhada)
			if (_tronoVistoPeloKit == null || dono.Length == 0) _tronoVistoPeloKit = dono;
			return;
		}

		bool coroadoAgora = _tronoVistoPeloKit != null
							&& !string.Equals(_tronoVistoPeloKit, dono, StringComparison.OrdinalIgnoreCase);
		_tronoVistoPeloKit = dono;
		if (!coroadoAgora) return;

		if (pl.PoderDaDestruicao.Aprendida)
			Avisar(pl, "o trono reconhece o seu caminho: todas as habilidades do Poder da Destruição são AMPLIFICADAS em "
					 + $"{Disciplinas.AmpliacaoDoCargo * 100:0}% enquanto você portar o título.");
		else if (pl.UltraInstinct.Aprendida)
			Avisar(pl, "o seu Instinto agora canaliza a Destruição: você ganha a Aura of Destruction e a Hakai Infusion a "
					 + $"{KitDoDeus.EmprestimoDoCaminho * 100:0}% de eficiência, movidas pela sua PRECISÃO (os verbs estão na "
					 + "aba Skills, enquanto você portar o título).");

		Avisar(pl, "você agora é o DEUS DA DESTRUIÇÃO. Mantenha o equilíbrio do universo: cumpra as tarefas (verb Title "
				 + "Status) ou será destituído. Você não envelhece e respira no vácuo enquanto portar o título.");
	}

	/// <summary>
	/// OS DOIS VERBS QUE O TITULO EMPRESTA (`god_apply_powers`, `:134-138`): a Aura of Destruction e a Hakai Infusion,
	/// pro Deus que trilha o INSTINTO. As regras de cada um sao as da disciplina (`AlternarDisciplina`,
	/// `HakaiInfusion`), que ja sabem do kit emprestado; aqui so se confere de quem e o botao.
	/// </summary>
	private void UsarOKitEmprestado(ServerPlayer pl, bool aura)
	{
		if (!EhDeusDaDestruicao(pl)) { Avisar(pl, SoODeusDaDestruicao); return; }
		if (pl.PoderDaDestruicao.Aprendida || !pl.UltraInstinct.Aprendida)
		{
			Avisar(pl, "o título só empresta a Destruição a quem trilha o Instinto.");
			return;
		}

		if (aura) AlternarDisciplina(pl, TipoDeDisciplina.PoderDaDestruicao);
		else HakaiInfusion(pl);
	}

	// =====================================================================
	// A FURY -- `obj/buff/GodFury` e o verb `God_Fury` (`GodOfDestruction.dm:388-419`)
	// =====================================================================
	/// <summary>Este corpo esta na Fury agora?</summary>
	private static bool EstaNaFury(ServerPlayer pl) => pl.Forma.Atual == KitDoDeus.IdDaFury;

	/// <summary>
	/// HA OUTRA TRANSFORMACAO DE PE NESTE CORPO? -- qualquer forma que nao seja o repouso dele, ou a fera.
	///
	/// No DM a Fury ocupa o slot de forma (`slot = sFORM`, `:395`) e o `startbuff` recusa o segundo buff do mesmo
	/// slot (`buffs.dm:110-113`); o verb ainda recusa em voz alta as duas que nao sao buff de slot antes dele
	/// (`if(usr.ue_form || usr.ui_form)`, `:415`). Aqui toda forma e uma entrada do catalogo, entao a pergunta e uma:
	/// o corpo esta no piso da escada dele (`Catalogo.IdDoPiso` -- a base, ou a forma de repouso do Frost Demon)?
	/// O Oozaru entra pela outra metade porque neste port ele e estado paralelo (`ServerPlayer.Oozaru`).
	/// </summary>
	private static bool OutraFormaDePe(ServerPlayer pl) =>
		pl.Oozaru != FormaOozaru.Nao
		|| (!EstaNaFury(pl) && pl.Forma.Atual != Catalogo.IdDoPiso(Perfil(pl)));

	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): a Fury entra por cima de outra transformacao -- o Super Saiyajin
	/// some sem ser recolhido. Lido pela <see cref="EntrarNaFury"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool FuryAtropelaOutraFormaDeTeste;

	/// <summary>
	/// O VERB `Fury` -- liga e desliga (`if(isBuffed) stopbuff else startbuff`, `:418-419`).
	///
	/// As guardas de GESTO sao as que todo pedido de forma de jogador ja tem neste port (`TransformarPara`): quem
	/// perdeu as redeas do corpo nao muda de forma pela propria mao, e a fusao estragada nao se transforma
	/// (`AscendePorDecisao`). DESLIGAR SEMPRE PODE. O DM nao pergunta nocaute nem morte neste verb, e aqui tambem nao.
	/// </summary>
	private void AlternarFury(ServerPlayer pl)
	{
		if (!EhDeusDaDestruicao(pl)) { Avisar(pl, SoODeusDaDestruicao); return; }
		if (EstaNaFury(pl)) { SairDaFury(pl); return; }
		if (RecusarFormaSemAsRedeas(pl) || !AscendePorDecisao(pl)) return;
		EntrarNaFury(pl);
	}

	/// <summary>
	/// ENTRAR NA FURY -- **a unica porta**, pro verb do jogador e pra quem dirige um corpo sem dono. Devolve se o
	/// corpo esta na Fury ao sair daqui.
	///
	/// OS DOIS PORTOES SAO OS DO DM: carregar o titulo (`:414`) e nao haver outra forma de pe
	/// (<see cref="OutraFormaDePe"/>). NAO HA custo de Ki, dreno nem maestria (`:393-409`), e o Ki nao e mexido --
	/// por isso este metodo nao passa pelo `EntrarNaForma` da escada, que ENCHE o tanque na estreia. O que ele faz e
	/// o resto do que aquele faz, pelas mesmas chamadas: `Entrar` (libera e marca a estreia), `AplicarForma` (o
	/// multiplicador, o teto de Ki, o poder na hora) e `AnunciarForma` (a zona veste o corpo; a cena, na estreia).
	///
	/// DIVERGENCIA DECLARADA: com uma forma RACIAL de pe o DM falha CALADO (o `startbuff` devolve FALSE,
	/// `buffs.dm:112-113`); aqui a recusa fala, com a frase que o verb ja tinha pra Ultra Instinto e Ultra Ego.
	/// </summary>
	/// <param name="semCena">
	/// Entra sem a cena da estreia (2,5 s com o corpo preso). Pra quem liga a forma num corpo sem dono no meio de
	/// uma luta; o verb do jogador nao passa `true`.
	/// </param>
	internal bool EntrarNaFury(ServerPlayer pl, bool semCena = false)
	{
		if (Catalogo.Def(KitDoDeus.IdDaFury) is not { } fury) return false;
		if (!EhDeusDaDestruicao(pl)) { Avisar(pl, SoODeusDaDestruicao); return false; }
		if (EstaNaFury(pl)) return true;

		if (!FuryAtropelaOutraFormaDeTeste && OutraFormaDePe(pl))
		{
			Avisar(pl, "recolha a outra forma antes de despertar a Fúria.");   // `:416`
			return false;
		}

		string antes = pl.Forma.Atual;
		bool estreia = pl.Forma.Entrar(fury.Id);
		AplicarForma(pl);

		// `to_chat(view(container), "[container] desperta a FURIA da Destruicao! Uma aura violeta densa envolve seu
		// corpo!")` (`:401`) -- pra quem ve, e nao pra zona inteira.
		Avisar(pl, "você desperta a FÚRIA da Destruição! Uma aura violeta densa envolve o seu corpo.");
		AvisarPertoG3(pl, RaioDaVista,
			$"{NomeVisivel(pl)} desperta a FÚRIA da Destruição! Uma aura violeta densa envolve o corpo.",
			excetoCentro: true);
		GD.Print($"[server] {pl.Name}: {antes} -> {fury.Id} (a Fury do Deus da Destruicao)"
				 + (estreia ? "  <- PRIMEIRA VEZ" : ""));

		AnunciarForma(pl, antes, fury.Id, estreia, semCena);
		return true;
	}

	/// <summary>SAIR DA FURY por vontade propria -- o `DeBuff()` (`:405-409`): *"A Furia se recolhe."*</summary>
	internal void SairDaFury(ServerPlayer pl)
	{
		if (EstaNaFury(pl)) Reverter(pl, "a Fúria se recolhe.");
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): a Fury volta a cair com o corpo -- nocauteado, morto ou com o
	/// Ki zerado, como toda forma da escada. Lido pela <see cref="FuryAtravessaAQueda"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool FuryCaiComOCorpoDeTeste;

	/// <summary>
	/// ESTE CORPO ESTA NUMA FORMA QUE NAO CAI COM ELE? -- a Fury, e so ela. Lida pelo `TickDaForma`, nos dois pontos
	/// em que uma forma da escada se desfaz sozinha (o corpo caido, o Ki zerado).
	///
	/// E DO DM, e nao gentileza: quem desfaz forma no nocaute e na morte e o `Revert()` (`KO.dm:77-79`,
	/// `Death.dm:17`), e a lista dele (`Stats/BP/Revert.dm:12-23`) tem o Super Saiyajin, o Ultra Instinto, o Ultra
	/// Ego e o Beast -- e NAO tem o `GodFury`. E a Fury nao cobra Ki nenhum (`:393-409`), entao nao ha "o Ki acabou"
	/// pra ela. So duas coisas a tiram: o verb e a perda do titulo.
	/// </summary>
	private bool FuryAtravessaAQueda(ServerPlayer pl) =>
		!FuryCaiComOCorpoDeTeste && Catalogo.SoPeloTitulo(pl.Forma.Def);

	// =====================================================================
	// O HAKAI -- `god_hakai_try` / `god_hakai_execute` / o verb `God_Hakai` (`:182-222`)
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): o Hakai nao confere criterio nenhum -- apaga quem estiver na
	/// frente, de torso inteiro e mais forte que o Deus. Lido pelo <see cref="TentarHakai"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool HakaiSemCriterioDeTeste;

	/// <summary>
	/// O VERB `Hakai` -- *"movimento de AGARRAO: executa quem voce esta agarrando (ou o alvo colado a frente)"* (`:212`).
	///
	///     var/mob/M = usr.grabbee
	///     if(!M) for(var/mob/P in get_step(usr, usr.dir)) { M = P; break }
	///
	/// O "colado a frente" e o alvo do GOLPE deste port (<see cref="AlvoNaFrente"/>: o cone e o alcance do soco, a
	/// altura, e nunca quem esta intocavel -- o `!M.attackable` do `:184`).
	/// </summary>
	internal void HakaiDoDeus(ServerPlayer pl)
	{
		if (!EhDeusDaDestruicao(pl)) { Avisar(pl, SoODeusDaDestruicao); return; }
		if (!ProntoPraGolpeG3(pl, out string porque, exigirCanfight: false)) { Avisar(pl, porque); return; }

		ServerPlayer? alvo = QuemEuSeguro(pl) ?? AlvoNaFrente(pl);
		if (alvo == null) { Avisar(pl, "agarre (ou encoste em) a sua vítima primeiro."); return; }   // `:219`

		if (!TentarHakai(pl, alvo))
			Avisar(pl, $"{NomeVisivel(alvo)} ainda resiste à Destruição: o torso precisa estar abaixo de "
					 + $"{KitDoDeus.HakaiTorsoPct:0}% e o poder ao menos {(1 - KitDoDeus.HakaiFracaoDeBp) * 100:0}% abaixo do seu.");   // `:222`
	}

	/// <summary>
	/// O NUCLEO DO HAKAI, dividido pelo verb de agarrao e pela Destroyer Sphere -- o `god_hakai_try(M, via)`
	/// (`:182-194`). Devolve se o alvo foi APAGADO... ou se o golpe fatal foi dado e negado (ver <see cref="ExecutarHakai"/>):
	/// nos dois casos os criterios passaram, que e o que o `return 1` do DM quer dizer.
	///
	/// Os criterios sao os do <see cref="KitDoDeus.HakaiPega"/>, com o BP EXPRESSO dos dois NESTE instante
	/// (*"pelo menos 25% mais fraco que voce AGORA"*, `:191`).
	/// </summary>
	internal bool TentarHakai(ServerPlayer deus, ServerPlayer alvo)
	{
		if (!EhDeusDaDestruicao(deus)) return false;                                                   // `:183`
		if (alvo == deus || alvo.Ficha.dead || alvo.Combate is not { Intocavel: false } c) return false;   // `:184`

		if (!HakaiSemCriterioDeTeste)
		{
			if (c.Corpo.Achar("Torso") is not { Decepado: false } torso) return false;                 // `:186-189`
			if (!KitDoDeus.HakaiPega(torso.Vida, torso.VidaMax, alvo.Ficha.expressedBP, deus.Ficha.expressedBP))
				return false;                                                                              // `:190-192`
		}

		ExecutarHakai(deus, alvo);
		return true;
	}

	/// <summary>
	/// O ALVO SE DESFAZ -- `god_hakai_execute` (`:196-210`).
	///
	/// A MORTE PASSA PELA PORTA UNICA (`CombatState.Morrer`), como o `spawn M.Death()` de la passa pelo `Death()`:
	/// quem tem o seguro da Aura of Destruction (`ue_deathsave_try`, `Death.dm:13`) ou o despertar do bio-androide
	/// (`:10`) sobrevive ao Hakai, la e aqui.
	///
	/// DIVERGENCIA DECLARADA: o DM escreve `hakai_mark = 1` ANTES de chamar o `Death()` (`:203`), entao a alma que
	/// teve o golpe negado ficava marcada sem ter sido apagada -- e pagaria o dobro no Enma numa morte qualquer,
	/// depois. Aqui a marca e de quem MORREU do Hakai.
	/// </summary>
	private void ExecutarHakai(ServerPlayer deus, ServerPlayer alvo)
	{
		// `to_chat(view(M), ...)` -- pra quem ve a vitima (ela e o Deus inclusos).
		AvisarPertoG3(alvo, RaioDaVista,
			$"{NomeVisivel(deus)}: HAKAI! O corpo de {NomeVisivel(alvo)} se desfaz em partículas de luz violeta...");
		BaqueDoKit(alvo);   // `createShockwavemisc(M.loc, 2)` + `createDustmisc(M.loc, 2)` + `spawn Quake()` (`:198-201`)
		GestoDoKit(deus);   // `emit_Sound('scouterexplode.ogg')` (`:200`)

		alvo.UltimoAgressor = deus.Id;   // `M.lastDamager = src` (`:204`)
		MarcarAgressao(alvo, deus);
		if (!alvo.Combate.Morrer()) return;

		if (EhPessoa(alvo)) alvo.Ficha.hakai_mark = true;   // `if(M.client) M.hakai_mark = 1` (`:202-203`)
		AoPerderALuta(alvo, deus, morreu: true);
		GD.Print($"[server] {deus.Name} apagou {alvo.Name} com o HAKAI");
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): o Enma cobra de quem foi apagado por Hakai o preco de todo mundo.
	/// Lido pelo <see cref="PrecoDoEnmaPara"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool HakaiNaoEncareceDeTeste;

	/// <summary>
	/// O PRECO DA VOLTA A VIDA PRA ESTA ALMA, no Enma: `ZENI_REVIVE_COST * (hakai_mark ? GOD_HAKAI_REVIVE_MULT : 1)`
	/// (`NPCs/SkyNPCs.dm:189`) -- *"apagado por HAKAI: o Enma cobra o dobro pra reconstruir a alma"*.
	/// </summary>
	private static double PrecoDoEnmaPara(ServerPlayer pl) =>
		Alem.PrecoDoReviveDoEnma * (pl.Ficha.hakai_mark && !HakaiNaoEncareceDeTeste ? KitDoDeus.HakaiPrecoDoRevive : 1);

	/// <summary>
	/// O que o Enma acrescenta a fala do preco pra quem foi apagado (`SkyNPCs.dm:191`: *"A God of Destruction erased
	/// you -- rebuilding THAT soul costs extra!"*). Vazio pra todo o resto.
	/// </summary>
	private static string FraseDoHakaiNoEnma(ServerPlayer pl) =>
		pl.Ficha.hakai_mark ? " \"Um Deus da Destruição apagou você -- reconstruir ESSA alma custa mais caro!\"" : "";

	// =====================================================================
	// O POWER FLICK -- o verb `God_Power_Flick` (`:225-245`)
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): o peteleco vale UM golpe pesado em vez de dois. Lido pelo
	/// <see cref="PowerFlickDoDeus"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool FlickValeUmGolpeDeTeste;

	/// <summary>
	/// O VERB `Power Flick` -- *"agarra e peteleca pra longe (2x o dano de um golpe pesado sem carga)"*: solta quem
	/// esta nos bracos (`:233-237`), fere o torso com `NormDamageCalc(M) * 2` (`:238-241`) e arremessa no rumo do
	/// olhar por nove tiques, com o poder do Deus como forca (`:242-245`).
	///
	/// O DANO E DIRETO (`damage_m`: sem pontaria, sem guarda, sem esquiva) e NAO passa pelo gap de poder -- o
	/// `NormDamageCalc` e so ataque contra defesa (`calcs.dm:9-22`), e o `damage_m` nao tem `BPModulus`. O
	/// `ArmorCalc` e a mitigacao do Ultra Ego do `damage_m` nao entram, pela regra que o `FerirUmMembroG10` declara
	/// pra todo dano direto deste port.
	/// </summary>
	internal void PowerFlickDoDeus(ServerPlayer pl)
	{
		if (!EhDeusDaDestruicao(pl)) { Avisar(pl, SoODeusDaDestruicao); return; }
		if (!ProntoPraGolpeG3(pl, out string porque, exigirCanfight: false)) { Avisar(pl, porque); return; }
		if (QuemEuSeguro(pl) is not { } preso)
		{
			Avisar(pl, "você precisa estar AGARRANDO alguém para o peteleco.");   // `:230`
			return;
		}

		// SOLTA ANTES DE BATER E DE JOGAR (`:233-237`), pelos mesmos campos do `ArremessarComOAgarrao`: o `Arremessar`
		// manda a ficha do corpo jogado, e ela tem de sair ja com o aperto desfeito.
		pl.AgarrandoId = 0;
		pl.ModoDoAgarrao = ModoDeAgarrao.Nenhum;
		pl.Estrangulando = false;
		LimparPreso(preso);

		double dano = CombatMath.DanoBase(pl.Ficha, preso.Ficha) * (FlickValeUmGolpeDeTeste ? 1 : KitDoDeus.FlickGolpes);
		AvisarPertoG3(preso, RaioDaVista,
			$"{NomeVisivel(pl)} dá um PETELECO em {NomeVisivel(preso)} -- o impacto atravessa o corpo inteiro!");   // `:239`
		GestoDoKit(pl);   // `emit_Sound('scouterexplode.ogg')` (`:240`)

		FerirUmMembroG10(preso, pl, dano, pl.Combate?.Letal ?? false, zona: "torso");   // `damage_m(M, dmg, "torso", usr.murderToggle, 2)`
		Arremessar(preso, MeleeArea.Frente(pl.Facing), pl.Ficha.expressedBP, KitDoDeus.FlickTiquesDeVoo);
		GD.Print($"[server] {pl.Name} deu um Power Flick em {preso.Name} (dano {dano:0.##})");
	}

	// =====================================================================
	// OS DOIS TIROS -- `God_Fury_Beam` (`:251-288`) e `God_Destroyer_Sphere` (`:294-331`)
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): os dois tiros saem na hora, sem a carga
	/// (`sleep(max(round(usr.Eactspeed / 3), 1))`). Lido pelo <see cref="AtirarDoKit"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool TiroDoKitSemCargaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): os dois tiros sao bolas comuns -- o feixe nao derruba a guarda e a
	/// esfera nao tenta o Hakai. Lido pelo <see cref="AoFerirComTiroDoKit"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool TiroDoKitSemDomDeTeste;

	/// <summary>ESTE CORPO ESTA COM UM TIRO DO KIT NA MAO? -- o `usr.blasting = 1` dos dois verbs (`:266`, `:309`). Lido pelo `PodeAtirar`.</summary>
	private bool CarregandoTiroDoKit(int id) => _kitDoDeus.TryGetValue(id, out EstadoDoKit? e) && e.CargaAteMs != 0;

	/// <summary>
	/// OS VERBS `Destruction God's Fury` E `Destroyer Sphere`. Os dois sao o mesmo verb com numeros diferentes, no DM
	/// tambem: recusa, cobra, faz a pose, ESPERA um terco da velocidade de acao e so entao a bola nasce.
	///
	///     if(usr.KO || usr.med || usr.train || usr.blasting) return
	///     if(usr.Ki &lt; GOD_FURYBEAM_KI * usr.BaseDrain) ...
	///     usr.blasting = 1 ; usr.Ki -= ... ; flick("Blast", usr)
	///     sleep(max(round(usr.Eactspeed / 3), 1))
	///
	/// As recusas sao as do `PodeAtirar` (a porta de todo tiro deste port: as quatro do DM, mais a morte, o
	/// atordoamento, o raio na mao e os tetos de projetil). A carga corre no pulso de 10 Hz
	/// (<see cref="PulsoDoKitDoDeus"/>), que e quem solta o tiro.
	/// </summary>
	internal void AtirarDoKit(ServerPlayer pl, DomDeProjetil qual)
	{
		if (!EhDeusDaDestruicao(pl)) { Avisar(pl, SoODeusDaDestruicao); return; }

		bool esfera = qual == DomDeProjetil.Hakai;
		double custo = (esfera ? KitDoDeus.EsferaKi : KitDoDeus.FuriaKi) * pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		pl.Ficha.Ki -= custo;
		long agora = NowMs();
		long carga = (long)(KitDoDeus.TiquesDeCarga(pl.Ficha.Eactspeed) * TempoDoDm.MsPorTique);
		pl.GestoAte = agora + Math.Max(GestoDeSoproMs, carga);   // `flick("Blast", usr)`

		if (esfera)   // `to_chat(view(usr), "[usr] condensa uma esfera de pura DESTRUICAO...")` (`:312`)
			AvisarPertoG3(pl, RaioDaVista, $"{NomeVisivel(pl)} condensa uma esfera de pura DESTRUIÇÃO...");

		EstadoDoKit e = KitDe(pl);
		e.CargaDe = qual;
		if (TiroDoKitSemCargaDeTeste) { SoltarOTiroDoKit(pl, e); return; }

		e.CargaAteMs = agora + carga;
		if (!_pulsoDoKitInscrito)
		{
			_pulsoDoKitInscrito = true;
			InscreverNoPulso(HaCargaNoKit, PulsoDoKitDoDeus);
		}
		LigarPulso();
	}

	private bool _pulsoDoKitInscrito;

	private bool HaCargaNoKit()
	{
		foreach (EstadoDoKit e in _kitDoDeus.Values)
			if (e.CargaAteMs != 0) return true;
		return false;
	}

	/// <summary>A AGENDA DO KIT NO PULSO DE 10 Hz: solta os tiros cuja carga venceu. Chamada tambem pela bancada, que avanca o relogio na mao.</summary>
	private void PulsoDoKitDoDeus()
	{
		long agora = NowMs();
		foreach ((int id, EstadoDoKit e) in _kitDoDeus.ToList())
		{
			if (e.CargaAteMs == 0 || agora < e.CargaAteMs) continue;
			if (_players.TryGetValue(id, out ServerPlayer? pl)) SoltarOTiroDoKit(pl, e);
			else e.CargaAteMs = 0;
		}
	}

	/// <summary>
	/// A BOLA NASCE (`:270-288`, `:314-331`): `12.dmi`, `deflectable = 0`, `basedamage` 1,5 (ou 2), o poder e os
	/// `mods` de quem atira, `walk(A, A.dir, 2)` e o alcance e o prazo de todo `obj/attack` (30 tiles, `Burnout()` de
	/// 5 s). Ela NAO empurra: o `Bump` so arremessa com raio ou com bola `kiforceful` (`objects.dm:450-460`), e
	/// nenhum dos dois verbs a escreve.
	///
	/// O FEIXE mira o TORSO pelo proprio objeto (`A.selectzone = "torso"`, `:279`) e, onde fere, a guarda cai; a
	/// ESFERA tenta o Hakai em quem fere. As duas coisas sao o <see cref="DomDeProjetil"/> da receita, executado pelo
	/// <see cref="AoFerirComTiroDoKit"/>.
	///
	/// DIVERGENCIA DECLARADA: no DM o tiro nasce depois do `sleep` mesmo que o corpo tenha caido no meio dele (o verb
	/// nao pergunta de novo). Aqui a carga se perde com o corpo nocauteado ou morto -- o Ki ja foi cobrado, como la.
	/// </summary>
	private void SoltarOTiroDoKit(ServerPlayer pl, EstadoDoKit e)
	{
		DomDeProjetil qual = e.CargaDe;
		e.CargaAteMs = 0;
		e.CargaDe = DomDeProjetil.Nenhum;
		if (pl.Combate == null || Caido(pl)) return;

		bool esfera = qual == DomDeProjetil.Hakai;
		Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = esfera ? KitDoDeus.EsferaBaseDano : KitDoDeus.FuriaBaseDano,
			Velocidade = KitDoDeus.VelocidadeDosTiros,
			AlcanceTiles = KitDoDeus.AlcanceDosTiros,
			Deflectivel = false,                       // `deflectable = 0` (`:252`, `:295`)
			Empurra = false,
			ZonaCerta = esfera ? "" : "torso",         // `A.selectzone = "torso"` (`:279`)
			Dom = qual,
			Nome = esfera ? "Destroyer Sphere" : "Destruction God's Fury",
		}, verbo: esfera ? KitDoDeus.TiroDaEsfera : KitDoDeus.TiroDaFuria);
		pl.Ficha.BlastGain(_rng);   // `usr.Blast_Gain()` (`:287`, `:330`)
	}

	/// <summary>
	/// O QUE O TIRO DO KIT FAZ A MAIS EM QUEM ELE FERIU -- os dois `OnSucCollWMob(mob/M)` do arquivo, chamados pelo
	/// `Acertar` logo depois do dano do impacto (`objects.dm:439-440`):
	///
	///   * `godfury` (`:253-256`): `if(M.blocking) { M.blocking = 0; ... }` -- a guarda CAI. Ela ainda dividiu o dano
	///     deste tiro (a conta do `Bump` vem antes, `objects.dm:342-343`); o que ela nao faz e continuar de pe.
	///   * `goddestroyer` (`:296-299`): `P.god_hakai_try(M, "Destroyer Sphere")` -- o Hakai, pelos mesmos dois
	///     criterios do agarrao, medidos DEPOIS do dano da esfera.
	/// </summary>
	private void AoFerirComTiroDoKit(Projetil p, ServerPlayer dono, ServerPlayer alvo)
	{
		if (TiroDoKitSemDomDeTeste) return;

		if (p.Dom == DomDeProjetil.DerrubaAGuarda)
		{
			if (alvo.Combate is not { Bloqueando: true } guarda) return;
			guarda.Guardar(false);
			Avisar(alvo, "o feixe do Deus atravessa a sua guarda como se ela não existisse!");   // `:256`
		}
		else if (p.Dom == DomDeProjetil.Hakai) TentarHakai(dono, alvo);
	}

	// =====================================================================
	// A ENERGY NULLIFICATION -- o verb `God_Energy_Nullification` (`:336-357`)
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): a anulacao apaga todo ataque por perto, mais forte que o Deus ou
	/// nao. Lido pelo <see cref="AnularEnergiaDoDeus"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool AnulacaoApagaOsFortesDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): a recarga de cinco minutos arma mesmo sem nada anulado. Lido pelo
	/// <see cref="AnularEnergiaDoDeus"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool AnulacaoSempreCobraDeTeste;

	/// <summary>
	/// O VERB `Energy Nullification` -- apaga os ataques de ki alheios, a ate oito tiles, que carregam MENOS poder
	/// que o Deus tem agora (`if(A.BP &lt; usr.expressedBP)`, `:346`). A recarga de cinco minutos REAIS so arma se algo
	/// foi anulado (`if(n)`, `:351`).
	///
	/// O ALCANCE: o quadrado do `view(GOD_NULLIFY_RANGE, usr)` (a distancia de tabuleiro do BYOND), pela CABECA do
	/// tiro -- e, como em toda regra de alcance contra tiro deste port desde 2026-10-08, tambem o tiro que vem pra
	/// cima do Deus dentro do aviso que oito tiles davam na velocidade do DM (`Projetil.VemPraCimaDe`). Linha de
	/// visao nao entra (ver `AlvoAoAlcance`).
	///
	/// O alvo de treino da meditacao nao e ataque (`/obj/training_obj`, e nao `/obj/attack`) e fica de fora: e a
	/// bola de dano zero.
	/// </summary>
	internal void AnularEnergiaDoDeus(ServerPlayer pl)
	{
		if (!EhDeusDaDestruicao(pl)) { Avisar(pl, SoODeusDaDestruicao); return; }
		if (RecusarCaido(pl)) return;

		EstadoDoKit e = KitDe(pl);
		long agora = NowMs();
		if (agora < e.AnulacaoProntaEmMs)
		{
			Avisar(pl, $"a anulação retorna em {Duelo.TempoQueFalta(e.AnulacaoProntaEmMs, agora)}.");   // `:341`
			return;
		}

		float alcance = KitDoDeus.AnulacaoTiles * ZoneCollision.TileSize;
		int anulados = 0;
		foreach (Projetil p in ProjeteisDaZona(pl.Zone.Hash).ToList())
		{
			if (!p.Vivo || p.Dono == pl.Id || p.BaseDano <= 0) continue;   // `if(A.proprietor == usr) continue` (`:345`)
			if (Fusao.DistanciaEmTilesDoDm(pl.Pos, p.Pos, ZoneCollision.TileSize) > KitDoDeus.AnulacaoTiles
				&& !p.VemPraCimaDe(pl.Pos, alcance)) continue;
			if (!AnulacaoApagaOsFortesDeTeste && !KitDoDeus.Anula(p.Bp, pl.Ficha.expressedBP)) continue;

			Matar(p, FimDeProjetil.Apagou);   // `A.loc = null` (`:347-349`): some, sem estouro
			anulados++;
		}

		if (anulados == 0)
		{
			if (AnulacaoSempreCobraDeTeste) e.AnulacaoProntaEmMs = agora + (long)(KitDoDeus.AnulacaoRecargaSegundos * 1000);
			Avisar(pl, "nenhum ataque anulável por perto (só consome o poder se anular algo).");   // `:357`
			return;
		}

		e.AnulacaoProntaEmMs = agora + (long)(KitDoDeus.AnulacaoRecargaSegundos * 1000);
		BaqueDoKit(pl);   // `createShockwavemisc(usr.loc, 3)` (`:353`)
		GestoDoKit(pl);   // `usr.emit_Sound('scouterexplode.ogg')` (`:354`)
		AvisarPertoG3(pl, RaioDaVista,
			$"{NomeVisivel(pl)} ergue a mão -- {anulados} ataque(s) de ki simplesmente DEIXAM DE EXISTIR.");   // `:355`
	}

	// =====================================================================
	// O GOD'S TEMPER -- `god_temper_hit` (`:363-383`)
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada `--godkitteste`): a guarda nao zera o contador do surto -- quem apanha bloqueando
	/// tambem transborda. Lido pelo <see cref="ContarGolpeNoTemper"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool TemperContaComGuardaDeTeste;

	/// <summary>
	/// UM DANO ENCOSTOU NESTE CORPO -- o `if(M.god_temper_armed) M.god_temper_hit()` do funil de dano
	/// (`calcs.dm:181`), e o `god_temper_hit` inteiro (`:363-375`): com a guarda erguida o contador ZERA; sem ela
	/// sobe, e no decimo (fora do surto e da recarga) o Deus ganha, por dez segundos, 10% do ataque e da defesa
	/// fisica EFETIVOS de agora somados nos temporarios (`Tphysoff += Ephysoff * GOD_TEMPER_PCT`).
	///
	/// O SURTO E UM BUFF DA CASA (`LigarBuff`, com prazo): e ela que guarda o que foi somado e o tira exato -- o
	/// `god_temper_off`/`god_temper_def` do DM --, e que impede o numero de ir pro save com quem desloga no meio.
	/// Dai saem as duas diferencas de prazo, declaradas: o tique dos buffs e de 1 Hz (o surto dura de 10 a 11 s) e
	/// todo buff cai com o corpo nocauteado ou morto (la o `spawn(GOD_TEMPER_TICKS)` roda ate o fim).
	///
	/// ============================ DIVERGENCIA DECLARADA: QUAIS DANOS CONTAM ============================
	/// No DM o gancho so existe no `damage_m` (`calcs.dm:178-183`), que e o funil de OITO chamadas (o
	/// estrangulamento, o Godly Display, a Destruction Explosion, o recuo da aura, o proprio Power Flick, um ritual,
	/// `objects.dm:575`). O soco comum passa pelo `damage_mob` (`:168-176`) e o tiro de ki fere direto no `Bump`
	/// (`objects.dm:439`) -- nenhum dos dois conta. Mas o proprio arquivo descreve a passiva como *"10 danos seguidos
	/// SEM bloquear"* (`:22`, `:360`) e *"surto ao apanhar sem bloquear"* (`:6`), e o pedido do dono fala de
	/// "apanhar". Aqui conta todo dano de OUTRO corpo que encosta: soco, tiro e tecnica (as duas caudas de dano
	/// deste port, `ResolverDesfecho` e `ConsequenciaDoDano`).
	/// ====================================================================================================
	///
	/// A aura violeta dos dez segundos (`updateOverlay(god_fury_aura)`, `:374`) nao e desenhada fora da Fury: neste
	/// port a aura colada sai da FORMA. O surto avisa por texto, como la.
	/// </summary>
	private void ContarGolpeNoTemper(ServerPlayer d)
	{
		if (d.Combate is not { } c || !EhDeusDaDestruicao(d)) return;

		EstadoDoKit e = KitDe(d);
		if (c.Bloqueando && !TemperContaComGuardaDeTeste) { e.GolpesDoTemper = 0; return; }   // `:364`

		e.GolpesDoTemper++;
		long agora = NowMs();
		if (!KitDoDeus.TemperDispara(e.GolpesDoTemper, TemBuff(d, KitDoDeus.IdDoTemper), agora < e.TemperRecargaAteMs))
			return;

		e.GolpesDoTemper = 0;
		e.TemperRecargaAteMs = agora + (long)(KitDoDeus.TemperRecargaSegundos * 1000);
		LigarBuff(d, KitDoDeus.IdDoTemper, "a ira do Deus", new Dictionary<string, double>
		{
			["Tphysoff"] = d.Ficha.Ephysoff * KitDoDeus.TemperPct,
			["Tphysdef"] = d.Ficha.Ephysdef * KitDoDeus.TemperPct,
		}, duracaoMs: (long)(KitDoDeus.TemperSegundos * 1000));

		AvisarPertoG3(d, RaioDaVista, $"A ira do Deus transborda! {NomeVisivel(d)} pulsa com poder redobrado!");   // `:375`
		GD.Print($"[server] {d.Name}: God's Temper (+{KitDoDeus.TemperPct:P0} de ataque e defesa fisica)");
	}

	// =====================================================================
	// O QUE A ZONA VE E OUVE
	// =====================================================================
	/// <summary>
	/// A ONDA DE CHOQUE, A POEIRA E O TREMOR num ponto -- os `createShockwavemisc`/`createDustmisc`/`Quake()` do
	/// Hakai e da anulacao. Nao ha efeito novo: e o `ClashSub.Baque` que o embate de ki e o ZanzoClash ja mandam, e
	/// que o cliente ja desenha com faisca, poeira e tremor (`World.AoBaqueDeEmbate`).
	/// </summary>
	private void BaqueDoKit(ServerPlayer centro)
	{
		EscutaDeBaquesDoKit?.Add(centro.Id);
		var w = Protocol.Begin(Protocol.S2C.Clash);
		w.Put((byte)Protocol.ClashSub.Baque);
		w.PutVec(centro.Pos);
		w.Put(Voo.ParaByte(centro.Altitude));
		foreach (ServerPlayer o in ZoneList(centro.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>A bancada ouve os baques do kit (em volta de quem). Nula em producao.</summary>
	internal static List<int>? EscutaDeBaquesDoKit;

	/// <summary>
	/// O `emit_Sound('scouterexplode.ogg')` do Hakai, do peteleco e da anulacao, com a mao erguida: e o gesto do
	/// sopro (`GestoDoCorpo.Kiai` -- a pose de tiro por meio segundo e exatamente esse som), que a zona ja sabe ouvir.
	/// </summary>
	private void GestoDoKit(ServerPlayer pl)
	{
		pl.GestoAte = NowMs() + GestoDeSoproMs;
		AnunciarGesto(pl, Protocol.GestoDoCorpo.Kiai);
	}

	// =====================================================================
	// O CANAL DE HABILIDADE
	// =====================================================================
	/// <summary>Os verbs do kit. Entram pelo mesmo cano do `UsarHabilidade`; devolve se o id era daqui.</summary>
	private bool UsarKitDoDeus(ServerPlayer pl, string id)
	{
		switch (id)
		{
			case KitDoDeus.IdDaFury: AlternarFury(pl); return true;
			case KitDoDeus.VerbHakai: HakaiDoDeus(pl); return true;
			case KitDoDeus.VerbFlick: PowerFlickDoDeus(pl); return true;
			case KitDoDeus.VerbFuria: AtirarDoKit(pl, DomDeProjetil.DerrubaAGuarda); return true;
			case KitDoDeus.VerbEsfera: AtirarDoKit(pl, DomDeProjetil.Hakai); return true;
			case KitDoDeus.VerbAnular: AnularEnergiaDoDeus(pl); return true;
			case KitDoDeus.VerbAuraEmprestada: UsarOKitEmprestado(pl, aura: true); return true;
			case KitDoDeus.VerbInfusaoEmprestada: UsarOKitEmprestado(pl, aura: false); return true;
			default: return false;
		}
	}
}
