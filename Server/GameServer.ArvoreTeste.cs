using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--arvoreteste` -- O TIER DE VITRINE E O `enabled = 0` PELO FUNIL DO SERVIDOR.
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// A irma de mesa (`dotnet run --project Tools/AssetPipeline -- arvores`) prova as regras no Core:
/// o `SkillBook.Avaliar`, o `Recalcular`, o `EsquecerEReembolsar`. O que ela NAO prova e o que so
/// existe com o servidor de pe:
///
///   * que a compra do JOGADOR (`Aprender(pl, path)`, o mesmo que o `C2S.Aprender` chama) passa pelo
///     veredito novo -- e que o Afterimage e recusado com a frase de TIER, nao de "desativada";
///   * que os contadores escritos pelo `AplicarEfeitos` (`bodyskill` etc.) chegam ao recalculo NA
///     MESMA compra (a ordem "efeitos, depois arvores" que o comentario de la promete);
///   * que o pacote `S2C.Skills` LEVA o estado das arvores -- montado pelo `MontarPacoteDeSkills` de
///     producao e desmontado com o `Protocol.LerEstadoDeSkills` que o CLIENTE usa -- e que um livro
///     que so recebeu esse pacote da o mesmo veredito que o do servidor;
///   * que o verbo `skill_esquecer` reembolsa e encolhe a arvore em cascata, com aviso por skill.
/// ==============================================================================
///
///     Godot --headless --path . --host --arvoreteste
/// </summary>
public partial class GameServer
{
	private int _arvOk, _arvFalhou;

	private void AfirmarArv(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _arvOk++; GD.Print($"[arvores]   OK    {oque}"); return; }
		_arvFalhou++;
		GD.PrintErr($"[arvores]   FALHA {oque}   {detalhe}");
	}

	private const string ArvBody = "/datum/skill/tree/Body";
	private const string ArvTraining = "/datum/skill/training";
	private const string ArvEvasive = "/datum/skill/evasive";
	private const string ArvDrills = "/datum/skill/drills";
	private const string ArvQingqong = "/datum/skill/qingqong";
	private const string ArvAfterimage = "/datum/skill/ki/Afterimage";
	private const string ArvMartialArts = "/datum/skill/MartialSkill/MartialArts";

	public void RodarBancadaDasArvores()
	{
		_arvOk = _arvFalhou = 0;
		GD.Print("[arvores] ================ O TIER DE VITRINE E O `enabled` PELO FUNIL ================");
		if (_skills == null) { AfirmarArv("o catalogo de skills carregou", false); return; }

		List<string>? escutaAnterior = EscutaDeAvisos;
		try
		{
			ServerPlayer pl = Forjar("Aprendiz das Arvores", CorredorLivre(4), bp: 1000);
			pl.Livro.Conceder(10);
			AplicarEfeitos(pl);   // o login faz isto: e o que calcula o estado das arvores pela primeira vez

			OFunilDaCompra(pl);
			OPacote(pl);
			OEsquecimento(pl);
			ODegrauQueAcendeEConcede();
			OsMarcosDoBp();
			AMeditacaoEAsSkillsDeKi();
		}
		catch (Exception e)
		{
			_arvFalhou++;
			GD.PrintErr($"[arvores]   FALHA a bancada rodou inteira   {e}");
		}
		finally
		{
			EscutaDeAvisos = escutaAnterior;
			MarcosDeBp.BpNaoDaMarcoDeTeste = false;
			Jandirus.Core.Stats.GainKnobs.MeditacaoNasSkillsDeKi = 1;
			LimparTudoDaBancada();
		}

		GD.Print($"[arvores] ================ {_arvOk} passaram, {_arvFalhou} falharam ================");
	}

	// =====================================================================
	// 1) A COMPRA PELO FUNIL
	// =====================================================================
	private void OFunilDaCompra(ServerPlayer pl)
	{
		GD.Print("[arvores] -- 1) A COMPRA PELO FUNIL: Aprender -> efeitos -> contadores -> arvores");

		AfirmarArv("ao nascer o livro tem estado de arvores (Body/Mind/Spirit + a racial)",
				   pl.Livro.Arvores.Count >= 3 && pl.Livro.Arvore(ArvBody) != null,
				   $"{pl.Livro.Arvores.Count}");
		AfirmarArv("o Body nasce no tier 1 com 0 investido",
				   pl.Livro.Arvore(ArvBody) is { Tier: 1, Investido: 0 });

		EscutaDeAvisos = [];
		Aprender(pl, ArvAfterimage);
		AfirmarArv("o Afterimage e RECUSADO pelo funil ao nascer", !pl.Livro.Sabe(ArvAfterimage));
		AfirmarArv("...e a recusa fala de TIER e de investir (nao de 'desativada')",
				   EscutaDeAvisos.Exists(a => a.Contains("tier", StringComparison.OrdinalIgnoreCase)
											&& a.Contains("invista", StringComparison.OrdinalIgnoreCase)),
				   string.Join(" | ", EscutaDeAvisos));

		EscutaDeAvisos = [];
		Aprender(pl, ArvEvasive);
		AfirmarArv("Evasion Training e recusada por PRE-REQUISITO, nomeando a Basic Training",
				   !pl.Livro.Sabe(ArvEvasive)
				   && EscutaDeAvisos.Exists(a => a.Contains("pre-requisito") && a.Contains("Basic Training")),
				   string.Join(" | ", EscutaDeAvisos));

		Aprender(pl, ArvTraining);
		AfirmarArv("comprar Basic Training pelo funil passa", pl.Livro.Sabe(ArvTraining));
		AfirmarArv("...e o contador bodyskill ja vale 1 na MESMA chamada (efeitos antes do recalculo)",
				   pl.Ficha.bodyskill >= 1, $"{pl.Ficha.bodyskill}");
		AfirmarArv("...e o Body registra 1 investido", pl.Livro.Arvore(ArvBody)?.Investido == 1);

		Aprender(pl, ArvEvasive);
		AfirmarArv("Evasion Training agora passa (o pre-requisito entrou)", pl.Livro.Sabe(ArvEvasive));
		Aprender(pl, ArvDrills);
		Aprender(pl, ArvQingqong);
		AfirmarArv("quatro compras de tier 1: Body com 4 investidos e no tier 2",
				   pl.Livro.Arvore(ArvBody) is { Investido: 4, Tier: 2 },
				   $"{pl.Livro.Arvore(ArvBody)?.Investido}/{pl.Livro.Arvore(ArvBody)?.Tier}");

		EscutaDeAvisos = [];
		Aprender(pl, ArvAfterimage);
		AfirmarArv("...e o Afterimage AGORA e comprado pelo funil", pl.Livro.Sabe(ArvAfterimage),
				   string.Join(" | ", EscutaDeAvisos));
		AfirmarArv("...custou 2 (10 - 4 - 2 = 4 marcos livres)", pl.Livro.MarcosLivres == 4, $"{pl.Livro.MarcosLivres}");

		// A MARTIAL SKILL: tres das quatro compradas somam bodyskill (Training, Evasion, Light Skill).
		AfirmarArv("bodyskill passou de 2 com as skills de corpo", pl.Ficha.bodyskill > 2, $"{pl.Ficha.bodyskill}");
		AfirmarArv("...e a Martial Skill foi aberta pelo growbranches do Body (extraido do DM)",
				   pl.Livro.Destravadas.Contains("/datum/skill/tree/MartialSkill"),
				   string.Join(",", pl.Livro.Destravadas));
		AfirmarArv("...e Martial Arts ficou compravel pelo veredito do servidor",
				   pl.Livro.PodeAprender(_skills!, ArvMartialArts, pl.Race, pl.Class, false) == Recusa.Pode,
				   pl.Livro.PodeAprender(_skills!, ArvMartialArts, pl.Race, pl.Class, false).ToString());
	}

	// =====================================================================
	// 2) O PACOTE
	// =====================================================================
	private void OPacote(ServerPlayer pl)
	{
		GD.Print("[arvores] -- 2) O PACOTE: o que sai no fio, lido com o leitor do cliente");

		NetDataWriter w = MontarPacoteDeSkills(pl);
		var r = new NetDataReader(w.CopyData());
		byte opcode = r.GetByte();
		int totais = r.GetInt(), livres = r.GetInt();
		bool vilao = r.GetBool();
		int n = r.GetUShort();
		var aprendidas = new List<string>();
		for (int i = 0; i < n; i++) aprendidas.Add(r.GetString(96));
		(List<string> destravadas, List<EstadoDeArvore> arvores, List<string> verbosNoFio, List<string> semReembolso) = Protocol.LerEstadoDeSkills(r);

		AfirmarArv("o opcode e S2C.Skills e a cabeca antiga (marcos, vilao, lista) continua no lugar",
				   opcode == (byte)Protocol.S2C.Skills && totais == pl.Livro.MarcosTotais && livres == pl.Livro.MarcosLivres
				   && !vilao && aprendidas.Count == pl.Livro.Aprendidas.Count);
		AfirmarArv("o pacote acabou exatamente onde o leitor parou (nenhum byte sobrando ou faltando)",
				   r.EndOfData, $"{r.AvailableBytes} bytes sobrando");
		AfirmarArv("a cauda fecha com as que NAO devolvem marco: nenhuma aqui, as cinco foram compradas pelo funil",
				   semReembolso.Count == 0 && aprendidas.TrueForAll(pl.Livro.FoiComprada), string.Join(",", semReembolso));
		AfirmarArv("a cauda traz as arvores destravadas (Martial Skill)",
				   destravadas.Contains("/datum/skill/tree/MartialSkill"), string.Join(",", destravadas));
		AfirmarArv("...e o estado de cada arvore possuida, com tier, investido e proximo degrau",
				   arvores.Count == pl.Livro.Arvores.Count
				   && arvores.Find(e => e.Path == ArvBody) is { Tier: 2, Investido: 6, ProximoInvestir: 7, ProximoTier: 3 },
				   string.Join(" | ", arvores.Select(e => $"{e.Path.Split('/')[^1]} t{e.Tier} inv{e.Investido} ->{e.ProximoInvestir}/{e.ProximoTier}")));

		// O CLIENTE: um livro que so tem o que veio no fio tem que dar o MESMO veredito.
		var cliente = new SkillBook { MarcosTotais = totais, MarcosLivres = livres };
		cliente.Carregar(aprendidas);
		cliente.CarregarEstado(destravadas, arvores);
		int divergentes = 0;
		foreach (Skill s in _skills!.Todas)
		{
			if (s.Arvore) continue;
			if (cliente.PodeAprender(_skills, s.Path, pl.Race, pl.Class, vilao)
				!= pl.Livro.PodeAprender(_skills, s.Path, pl.Race, pl.Class, vilao)) divergentes++;
		}
		AfirmarArv("um livro montado SO do pacote da o mesmo veredito que o do servidor pras 317 folhas",
				   divergentes == 0, $"{divergentes} divergem");
		AfirmarArv("...inclusive Martial Arts = Pode, que so abre por contador que o cliente NAO tem",
				   cliente.PodeAprender(_skills, ArvMartialArts, pl.Race, pl.Class, vilao) == Recusa.Pode);

		// A ASSINATURA: um contador que sobe por NIVEL (sem compra) muda o pacote.
		string sigAntes = pl.SigSkills;
		MandarSkills(pl);
		AfirmarArv("sem nada mudar, a assinatura do pacote fica a mesma", pl.SigSkills == sigAntes);
		pl.Ficha.kieffusionskill = 1;   // o degrau 35 da Basic Ki Circulation faz isto (Mind.dm:114)
		AplicarEfeitos(pl);
		AfirmarArv("um contador que subiu por nivel abre a Effusive Mastery e MUDA a assinatura (o pacote sai sem compra nenhuma)",
				   pl.SigSkills != sigAntes && pl.Livro.Destravadas.Contains("/datum/skill/tree/effusionmas"),
				   string.Join(",", pl.Livro.Destravadas));
	}

	// =====================================================================
	// 3) O ESQUECIMENTO
	// =====================================================================
	private void OEsquecimento(ServerPlayer pl)
	{
		GD.Print("[arvores] -- 3) O ESQUECIMENTO: skill_esquecer reembolsa e a arvore encolhe em cascata");

		int marcos = pl.Livro.MarcosLivres;
		EscutaDeAvisos = [];
		VerboEsquecerSkill(pl, ArvQingqong);
		AfirmarArv("esquecer Light Skill devolve 1 marco", pl.Livro.MarcosLivres == marcos + 1 && !pl.Livro.Sabe(ArvQingqong),
				   $"{marcos} -> {pl.Livro.MarcosLivres}");
		AfirmarArv("...o Body caiu pra 5 investidos e continua no tier 2 (o Afterimage conta 2)",
				   pl.Livro.Arvore(ArvBody) is { Investido: 5, Tier: 2 },
				   $"{pl.Livro.Arvore(ArvBody)?.Investido}/{pl.Livro.Arvore(ArvBody)?.Tier}");
		AfirmarArv("...e o bodyskill desceu junto (os efeitos foram reaplicados sem ela)",
				   pl.Ficha.bodyskill < 3, $"{pl.Ficha.bodyskill}");

		VerboEsquecerSkill(pl, ArvDrills);
		EscutaDeAvisos = [];
		VerboEsquecerSkill(pl, ArvEvasive);
		AfirmarArv("esquecer Evasion Training leva o Body a 3 investidos -> tier 1 -> o Afterimage CAI na cascata",
				   !pl.Livro.Sabe(ArvAfterimage) && pl.Livro.Arvore(ArvBody) is { Tier: 1 },
				   $"{pl.Livro.Arvore(ArvBody)?.Investido}/{pl.Livro.Arvore(ArvBody)?.Tier} sabe={pl.Livro.Sabe(ArvAfterimage)}");
		AfirmarArv("...com aviso nomeando o Afterimage ('nao sustenta')",
				   EscutaDeAvisos.Exists(a => a.Contains("Afterimage") && a.Contains("sustenta")),
				   string.Join(" | ", EscutaDeAvisos));
		AfirmarArv("...e os marcos voltaram todos menos o da Basic Training (10 - 1 = 9)",
				   pl.Livro.MarcosLivres == 9, $"{pl.Livro.MarcosLivres}");

		EscutaDeAvisos = [];
		VerboEsquecerSkill(pl, ArvAfterimage);
		AfirmarArv("esquecer o que nao se sabe e recusado sem mexer em marco",
				   pl.Livro.MarcosLivres == 9 && EscutaDeAvisos.Exists(a => a.Contains("nao sabe")),
				   string.Join(" | ", EscutaDeAvisos));
	}

	// =====================================================================
	// 4) O DEGRAU QUE ACENDE E O DEGRAU QUE CONCEDE -- pelo tique de verdade
	// =====================================================================
	private const string ArvKiUnlocked = "/datum/skill/mind/Ki_Unlocked";
	private const string ArvBasicAwareness = "/datum/skill/mind/Basic_Ki_Awareness";
	private const string ArvAdvancedAwareness = "/datum/skill/mind/Advanced_Ki_Awareness";
	private const string ArvSense = "/datum/skill/sense";

	/// <summary>
	/// O `enableskill(Advanced_Ki_Awareness)` do nivel 100 (Mind.dm:186) e o `learn(sense, 1)` do nivel
	/// 5 (Mind.dm:103-104), pelo FUNIL: o nivel sobe no `TicarNiveisDe` de producao (o corpo do laco do
	/// `TickDosNiveis` -- o corpo forjado nao passa por `EhJogador`), o `DepoisDaSubida` recalcula as
	/// arvores e concede, e a loja/os poderes respondem. A irma de mesa (`niveis`) prova o Core; aqui e
	/// o que so existe com o servidor de pe -- o tique, o bit `Poder.Sense`, o aviso.
	/// </summary>
	private void ODegrauQueAcendeEConcede()
	{
		GD.Print("[arvores] -- 4) O DEGRAU QUE ACENDE (destrava) E O QUE CONCEDE, pelo tique do servidor");
		if (RegrasDeNivel.Get(ArvBasicAwareness) is not { } regra || RegrasDeNivel.Get(ArvKiUnlocked) is not { } regraKi)
		{
			AfirmarArv("o niveis.json esta carregado (Basic Ki Awareness e Ki Unlocked tem regra)", false);
			return;
		}

		ServerPlayer pl = Forjar("Aprendiz dos Degraus", CorredorLivre(5), bp: 1000);
		pl.Livro.Conceder(30);
		pl.Livro.Dar(ArvKiUnlocked);
		pl.Livro.Dar(ArvBasicAwareness);
		pl.Niveis.Por(ArvBasicAwareness, 99);
		AplicarEfeitos(pl);

		Veredito v99 = pl.Livro.Avaliar(_skills!, ArvAdvancedAwareness, pl.Race, pl.Class, false);
		AfirmarArv("Basic no 99: a Advanced Ki Awareness AGUARDA o acendedor (nao esta 'morta')",
				   v99.Motivo == Recusa.AguardaAcendedor, v99.Motivo.ToString());
		EscutaDeAvisos = [];
		Aprender(pl, ArvAdvancedAwareness);
		AfirmarArv("...e a compra e recusada com a frase 'acende quando: Basic Ki Awareness chega ao nivel 100'",
				   !pl.Livro.Sabe(ArvAdvancedAwareness) && EscutaDeAvisos.Exists(a => a.Contains("nivel 100")),
				   string.Join(" | ", EscutaDeAvisos));

		// A SUBIDA DE VERDADE: exp na barreira, e o tique de producao sobe o nivel
		pl.Niveis.Por(ArvBasicAwareness, 99, regra.BarreiraEm(99));
		EscutaDeAvisos = [];
		TicarNiveisDe(pl);
		AfirmarArv("o TickDosNiveis levou a Basic ao 100", pl.Niveis.Nivel(ArvBasicAwareness) == 100, $"{pl.Niveis.Nivel(ArvBasicAwareness)}");
		AfirmarArv("...avisou 'voce agora pode aprender Advanced Ki Awareness' (o texto do Mind.dm:54)",
				   EscutaDeAvisos.Exists(a => a.Contains("pode aprender") && a.Contains("Advanced Ki Awareness")),
				   string.Join(" | ", EscutaDeAvisos));
		AfirmarArv("...e a Advanced esta ACESA no estado da arvore Mind (o pacote leva isto)",
				   pl.Livro.Arvore("/datum/skill/tree/Mind")?.Acesas.Contains(ArvAdvancedAwareness) == true);
		Aprender(pl, ArvAdvancedAwareness);
		AfirmarArv("...e a compra pelo funil PASSA", pl.Livro.Sabe(ArvAdvancedAwareness));
		AfirmarArv("os periodicos do 100 chegaram na ficha: kiawarenessskill 20 (20 x %5)",
				   Math.Abs(pl.Ficha.kiawarenessskill - 20) < 1e-9, $"{pl.Ficha.kiawarenessskill}");

		// O CONCEDE: Ki Unlocked do 4 pro 5 entrega o Sense no nivel 1, e o bit de poder acende
		AfirmarArv("antes do nivel 5 o Sense nao esta no livro nem no bit de poder",
				   !pl.Livro.Sabe(ArvSense) && (pl.Poderes & Protocol.Poder.Sense) == 0);
		pl.Niveis.Por(ArvKiUnlocked, 4, regraKi.BarreiraEm(4));
		EscutaDeAvisos = [];
		TicarNiveisDe(pl);
		AfirmarArv("Ki Unlocked chegou ao 5", pl.Niveis.Nivel(ArvKiUnlocked) == 5, $"{pl.Niveis.Nivel(ArvKiUnlocked)}");
		// O NIVEL 1 do `learn(savant, 1)` so tem onde morar em skill COM regra de nivel; o Sense nao tem
		// (o effector dele e quarentena inteira), entao o que se afirma e o livro e o aviso -- e, se um
		// dia ele ganhar regra, o 1 tem que aparecer.
		AfirmarArv("...e o Sense entrou no livro (no nivel 1 se tiver regra de nivel), com aviso",
				   pl.Livro.Sabe(ArvSense) && (RegrasDeNivel.Get(ArvSense) == null || pl.Niveis.Nivel(ArvSense) == 1)
				   && EscutaDeAvisos.Exists(a => a.Contains("Sense")),
				   $"sabe={pl.Livro.Sabe(ArvSense)} nivel={pl.Niveis.Nivel(ArvSense)} | {string.Join(" | ", EscutaDeAvisos)}");
		AfirmarArv("...e o bit `Poder.Sense` acendeu (a aba Sense existe agora)", (pl.Poderes & Protocol.Poder.Sense) != 0);

		// O VOO DO NIVEL 30 NAO E CONCEDIDO -- decisao do dono (Voo.cs: libera sozinho no 50)
		pl.Niveis.Por(ArvKiUnlocked, 29, regraKi.BarreiraEm(29));
		TicarNiveisDe(pl);
		AfirmarArv("Ki Unlocked no 30: a skill `flying` NAO entra no livro (o dado a traz; o dono cortou -- Voo.cs)",
				   pl.Niveis.Nivel(ArvKiUnlocked) == 30 && !pl.Livro.Sabe(SkillDoVoo), $"{pl.Niveis.Nivel(ArvKiUnlocked)} sabe={pl.Livro.Sabe(SkillDoVoo)}");
	}

	// =====================================================================
	// 5) OS MARCOS DO BP -- o `processpoints()` de `Stats/Level/scaling.dm`
	// =====================================================================
	/// <summary>
	/// O item 3 do playtest (dono, 2026-10-10): *"jogadores foram de 10 a 1000 de BP sem ganhar nenhum marco"*.
	/// A regra pura (os limiares do DM, a raca, o teto), o funil de producao (`TicarNiveisDe`, o mesmo tique que
	/// o laco dos jogadores chama), o salto, o retroativo, a contraprova e o disco.
	/// </summary>
	private void OsMarcosDoBp()
	{
		GD.Print("[arvores] -- 5) os marcos do BP (o `processpoints` de scaling.dm) --");

		int[] doDm = [3, 5, 7, 10, 15, 21, 31, 46, 67, 99, 144, 212, 310, 455, 667, 978];
		int[] daqui = [.. Enumerable.Range(MarcosDeBp.JaNoNascimento, doDm.Length).Select(n => (int)MarcosDeBp.Limiar(n, 1))];
		AfirmarArv("OS LIMIARES SAO OS DO ORIGINAL (1,466^n, arredondado): 3, 5, 7, 10, 15, 21, 31, 46, 67, 99, 144, 212, 310, 455, 667, 978",
				   daqui.SequenceEqual(doDm), string.Join(",", daqui));
		AfirmarArv("...a RACA divide o limiar (o `skillpointMod`): com 100 de BP o Humano (1,5) ja cruzou mais limiares que o mod 1, e o Gray (0,5) menos",
				   MarcosDeBp.Limiar(10, 1.5) < MarcosDeBp.Limiar(10, 1) && MarcosDeBp.Limiar(10, 0.5) > MarcosDeBp.Limiar(10, 1)
				   && MarcosDeBp.Devidos(100, 0, 1.5) > MarcosDeBp.Devidos(100, 0, 1)
				   && MarcosDeBp.Devidos(100, 0, 0.5) < MarcosDeBp.Devidos(100, 0, 1),
				   $"1,5: {MarcosDeBp.Devidos(100, 0, 1.5)}; 1: {MarcosDeBp.Devidos(100, 0, 1)}; 0,5: {MarcosDeBp.Devidos(100, 0, 0.5)}");
		AfirmarArv("...e o teto e o `spcap` (40 + 10 x mod): 50 marcos naturais pra mod 1, 55 pro Humano",
				   MarcosDeBp.Teto(1) == 50 && MarcosDeBp.Teto(1.5) == 55);
		int aos10 = MarcosDeBp.Devidos(10, 0, 1);
		AfirmarArv("A CONTA DO PLAYTEST: de 10 a 1000 de BP sao DOZE marcos (7 no total aos 10 e 19 aos 1000 -- os numeros do DM)",
				   aos10 == 4 && MarcosDeBp.Devidos(1000, aos10, 1) == 12
				   && MarcosDeBp.JaNoNascimento + MarcosDeBp.Devidos(1000, 0, 1) == 19,
				   $"aos 10: {aos10}; de 10 a 1000: {MarcosDeBp.Devidos(1000, aos10, 1)}");

		// ---- PELO TIQUE DE PRODUCAO ----
		ServerPlayer pl = Forjar("Marcado", CorredorLivre(4), bp: 10);
		pl.Livro.Conceder(MarcosIniciais);   // um recem-nascido: os tres do berco
		EscutaDeAvisos = [];
		TicarNiveisDe(pl);
		AfirmarArv("**O BP DA MARCO**: um recem-nascido com 10 de BP recebe os 4 limiares que ja cruzou (3 + 4 = 7, todos livres)",
				   pl.Livro.MarcosTotais == 7 && pl.Livro.MarcosLivres == 7 && pl.Livro.MarcosPorBp == 4,
				   $"{pl.Livro.MarcosLivres} livres / {pl.Livro.MarcosTotais} na vida / {pl.Livro.MarcosPorBp} por BP");
		AfirmarArv("...e ele e AVISADO, numa frase so pros quatro (e sem o numero do poder dele)",
				   EscutaDeAvisos!.Count(f => f.Contains("MARCO", StringComparison.Ordinal)) == 1
				   && EscutaDeAvisos.Contains("MARCOS! O seu poder cruzou 4 limiares: +4 marcos pra gastar (menu P > Learning)."),
				   string.Join(" | ", EscutaDeAvisos));

		EscutaDeAvisos = [];
		for (int i = 0; i < 20; i++) TicarNiveisDe(pl);
		AfirmarArv("...UMA VEZ: vinte tiques depois, com o mesmo BP, nenhum marco a mais e nenhum aviso",
				   pl.Livro.MarcosTotais == 7 && !EscutaDeAvisos!.Exists(f => f.Contains("MARCO", StringComparison.Ordinal)));

		pl.Ficha.BP = 14.9;
		TicarNiveisDe(pl);
		bool antesDoLimiar = pl.Livro.MarcosTotais == 7;
		pl.Ficha.BP = 15;
		EscutaDeAvisos = [];
		TicarNiveisDe(pl);
		AfirmarArv("O LIMIAR E `>=`: com 14,9 de BP nada; com 15 exatos, +1 marco",
				   antesDoLimiar && pl.Livro.MarcosTotais == 8 && EscutaDeAvisos!.Exists(f => f.Contains("+1 marco pra gastar")),
				   $"{pl.Livro.MarcosTotais} na vida | {string.Join(" | ", EscutaDeAvisos!)}");

		pl.Ficha.BP = 1000;
		TicarNiveisDe(pl);
		AfirmarArv("**O SALTO**: de 15 pra 1000 de BP num tique so, TODOS os limiares pulados sao pagos (19 na vida, como no DM)",
				   pl.Livro.MarcosTotais == 19 && pl.Livro.MarcosLivres == 19, $"{pl.Livro.MarcosTotais} na vida");

		// ---- O RETROATIVO ----
		ServerPlayer velho = Forjar("Veterano", CorredorLivre(4), bp: 50_000);
		velho.Livro.MarcosTotais = 3;   // save de antes de 2026-10-10: os tres do berco ja gastos, e o cursor em zero
		velho.Livro.MarcosLivres = 0;
		int esperados = MarcosDeBp.Devidos(50_000, 0, 1);
		TicarNiveisDe(velho);
		AfirmarArv("**RETROATIVO**: o personagem antigo, ja com 50.000 de BP e o cursor zerado, recebe de uma vez o que o BP dele ja valia",
				   esperados > 20 && velho.Livro.MarcosLivres == esperados && velho.Livro.MarcosTotais == 3 + esperados,
				   $"esperados {esperados}; {velho.Livro.MarcosLivres} livres / {velho.Livro.MarcosTotais} na vida");

		// ---- O TETO ----
		velho.Ficha.BP = 1e15;
		TicarNiveisDe(velho);
		int noTeto = velho.Livro.MarcosTotais;
		velho.Ficha.BP = 1e18;
		TicarNiveisDe(velho);
		AfirmarArv("O TETO: os naturais param no `spcap` (50 pra mod 1), por mais que o BP suba",
				   noTeto == MarcosDeBp.Teto(1) && velho.Livro.MarcosTotais == noTeto, $"{noTeto} e depois {velho.Livro.MarcosTotais}");

		// ---- A RACA, PELO FUNIL ----
		ServerPlayer humano = Forjar("Humano de Bancada", CorredorLivre(4), bp: 100);
		humano.Livro.Conceder(MarcosIniciais);
		humano.ModDeMarcos = 1.5;
		ServerPlayer gray = Forjar("Gray de Bancada", CorredorLivre(4), bp: 100);
		gray.Livro.Conceder(MarcosIniciais);
		gray.ModDeMarcos = 0.5;
		TicarNiveisDe(humano);
		TicarNiveisDe(gray);
		AfirmarArv("com os MESMOS 100 de BP o Humano (mod 1,5) tem mais marcos que o Gray (mod 0,5): o mod do CORPO e lido pelo funil",
				   humano.Livro.MarcosTotais > gray.Livro.MarcosTotais
				   && humano.Livro.MarcosPorBp == MarcosDeBp.Devidos(100, 0, 1.5) && gray.Livro.MarcosPorBp == MarcosDeBp.Devidos(100, 0, 0.5),
				   $"humano {humano.Livro.MarcosTotais}, gray {gray.Livro.MarcosTotais}");

		// ---- A CONTRAPROVA ----
		ServerPlayer mudo = Forjar("Sem Marco", CorredorLivre(4), bp: 1000);
		mudo.Livro.Conceder(MarcosIniciais);
		MarcosDeBp.BpNaoDaMarcoDeTeste = true;
		try { TicarNiveisDe(mudo); }
		finally { MarcosDeBp.BpNaoDaMarcoDeTeste = false; }
		AfirmarArv("(defeito injetado: o BP nao da marco) o mesmo corpo de 1000 de BP fica com os 3 do berco -- o relato do playtest, reproduzido",
				   mudo.Livro.MarcosTotais == 3 && mudo.Livro.MarcosPorBp == 0, $"{mudo.Livro.MarcosTotais} na vida");

		// ---- O DISCO: o cursor vai e volta, e o relog nao paga de novo ----
		CharacterSave save = AccountStore.DeJogador(pl, NowMs());
		ServerPlayer voltou = Forjar("Marcado de Volta", CorredorLivre(4), bp: 1000);
		PrepararSkills(voltou, save);
		int totalNaVolta = voltou.Livro.MarcosTotais;
		TicarNiveisDe(voltou);
		AfirmarArv("O RELOG NAO PAGA DE NOVO: o cursor vai pro save e volta, e o mesmo BP nao rende os mesmos marcos outra vez",
				   save.MarcosPorBp == 16 && voltou.Livro.MarcosPorBp == 16 && totalNaVolta == 19 && voltou.Livro.MarcosTotais == 19,
				   $"save {save.MarcosPorBp}; de volta {voltou.Livro.MarcosPorBp} por BP, {voltou.Livro.MarcosTotais} na vida");
	}

	// =====================================================================
	// 6) MEDITAR E AS SKILLS DE KI -- a MEDICAO do item 4 do playtest
	// =====================================================================
	/// <summary>
	/// *"Meditar deveria fazer Ki Unlocked e outras skills de ki subirem mais rapido; medir XP/min meditando vs
	/// nao."* Mede pelo tique de producao (`TicarNiveisDe`), um minuto de jogo por medida (300 tiques a 5 Hz),
	/// nos dois regimes da curva do original: o recem-nascido (Ki maximo 100, onde o ganho e multiplicado por
	/// Ki maximo / 3000) e o corpo ja formado (Ki maximo 1000).
	/// </summary>
	private void AMeditacaoEAsSkillsDeKi()
	{
		GD.Print("[arvores] -- 6) meditar e as skills de Ki: a medicao --");
		if (RegrasDeNivel.Get(ArvKiUnlocked) is not { } regra) { AfirmarArv("o Ki Unlocked tem regra de nivel", false); return; }

		double Medir(double kiMaximo, bool meditando, double peso = 1, int tiques = 300)
		{
			ServerPlayer c = Forjar("Medido", CorredorLivre(4), bp: 1000);
			c.Livro.Dar(ArvKiUnlocked);
			c.Niveis.Por(ArvKiUnlocked, 1);
			c.Ficha.MaxKi = kiMaximo;
			c.Ficha.med = meditando;
			Jandirus.Core.Stats.GainKnobs.MeditacaoNasSkillsDeKi = peso;
			try { for (int i = 0; i < tiques; i++) TicarNiveisDe(c); }
			finally { Jandirus.Core.Stats.GainKnobs.MeditacaoNasSkillsDeKi = 1; }
			return c.Niveis.Nivel(ArvKiUnlocked) == 1 ? c.Niveis.Exp(ArvKiUnlocked) : double.NaN;
		}

		double barreira = regra.BarreiraEm(1);
		double paradoNovo = Medir(100, false), meditaNovo = Medir(100, true);
		double paradoFormado = Medir(1000, false), meditaFormado = Medir(1000, true);
		GD.Print($"[arvores]         MEDIDO (Ki Unlocked nivel 1, barreira {barreira:N0} de exp, 1 minuto de jogo):");
		GD.Print($"[arvores]           recem-nascido (Ki max 100):  parado {paradoNovo:0.#} exp/min | meditando {meditaNovo:0.#} exp/min "
			   + $"({meditaNovo / Math.Max(paradoNovo, 1e-9):0.#}x) -> {barreira / Math.Max(meditaNovo, 1e-9):0} min meditando ate o nivel 2");
		GD.Print($"[arvores]           corpo formado  (Ki max 1000): parado {paradoFormado:0.#} exp/min | meditando {meditaFormado:0.#} exp/min "
			   + $"({meditaFormado / Math.Max(paradoFormado, 1e-9):0.#}x) -> {barreira / Math.Max(meditaFormado, 1e-9):0.#} min meditando ate o nivel 2");

		AfirmarArv("**MEDITAR RENDE MAIS EXP DE KI QUE FICAR PARADO**, nos dois regimes: pelo menos o dobro (os 2 contra 1 do `Mind.dm:126-131`, mais o exp adiantado de quem medita)",
				   meditaNovo >= paradoNovo * 1.9 && meditaFormado >= paradoFormado * 1.9 && paradoNovo > 0,
				   $"novo {paradoNovo:0.#} -> {meditaNovo:0.#}; formado {paradoFormado:0.#} -> {meditaFormado:0.#}");
		AfirmarArv("...e o que faz o comeco parecer parado e a CURVA do original, nao a falta do bonus: com Ki maximo 100 o mesmo minuto rende perto de um trigesimo (Ki max / 3000)",
				   paradoFormado / paradoNovo is > 20 and < 45, $"formado/novo parado = {paradoFormado / paradoNovo:0.#}x");

		// 100 TIQUES (20 s) E NAO O MINUTO: com o peso em 3 o corpo formado PASSA DA BARREIRA dentro do minuto
		// (2.428 x 3 contra 5.150), e a regua de cima so sabe ler exp dentro de um nivel -- ela devolvia NaN.
		double semPeso = Medir(1000, true, tiques: 100), comPeso = Medir(1000, true, peso: 3, tiques: 100);
		AfirmarArv("**O PESO E AJUSTAVEL** (`GainKnobs.MeditacaoNasSkillsDeKi`): em 3, meditar rende perto do triplo -- e parado nao muda nada",
				   comPeso / semPeso is > 2.2 and < 3.8 && Math.Abs(Medir(1000, false, peso: 3) - paradoFormado) < paradoFormado * 0.25,
				   $"20 s meditando: {semPeso:0.#} de exp com peso 1, {comPeso:0.#} com peso 3 ({comPeso / semPeso:0.##}x)");

		// ---- O PROGRESSO, A VISTA ----
		ServerPlayer leitor = Forjar("Leitor", CorredorLivre(4), bp: 1000);
		EscutaDeAvisos = [];
		VerProgressoDasSkills(leitor);
		bool semSkill = EscutaDeAvisos!.Exists(f => f.Contains("nenhuma skill sua sobe de nível", StringComparison.Ordinal));
		leitor.Livro.Dar(ArvKiUnlocked);
		leitor.Niveis.Por(ArvKiUnlocked, 3, 1234);
		EscutaDeAvisos = [];
		Verbo(leitor, "skills_progresso", "");
		AfirmarArv("**O PROGRESSO APARECE** (o `Check_Skill_Stats` do original, pelo verbo do menu): nivel, exp e barreira do Ki Unlocked -- e quem nao tem skill que sobe ouve qual e a primeira",
				   semSkill && EscutaDeAvisos!.Exists(f => f.Contains("Ki Unlocked: nível 3/100", StringComparison.Ordinal) && f.Contains("1.234 de", StringComparison.Ordinal)),
				   string.Join(" | ", EscutaDeAvisos!));
	}
}
