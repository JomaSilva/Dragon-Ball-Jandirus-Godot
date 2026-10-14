namespace Jandirus.Core.Skills;

/// <summary>
/// ============================ OS ACENDEDORES QUE O PORT ESCREVEU ============================
/// O dono, 2026-10-13: *"basic ki effusion etc estao sem "acendedor" verifique quais skills estao sem "acendedor"
/// adicione o pré req delas com as mecanicas incluidas"*.
///
/// Uma folha que nasce `enabled = 0` precisa de quem a acenda: um pre-requisito, uma regra da arvore
/// (`growbranches()`), o degrau de outra skill. Onze folhas PENDURADAS EM ARVORE nao tinham nenhum dos tres neste
/// port -- a loja as mostrava com "sem acendedor", pra sempre. Tres delas eram legado do DM e sairam do jogo
/// (<see cref="SkillsAposentadas"/>); as outras oito estao aqui, cada uma com o porque:
///
///   * BASIC KI EFFUSION -- a skill de ENTRADA da arvore Effusive Mastery (tier 1). No DM NADA a acende
///     (`Effusion.dm:17-24` so acende as tres maestrias do tier 2): a arvore abre com `kieffusionskill>=1` e a
///     primeira casa dela fica apagada. Defeito do original. Aqui ela acende quando a arvore abre.
///   * BASIC BUFF / DEBUFF / DEFENSE MASTERY -- as tres de entrada da Ki Buff Mastery. O DM as acende sem condicao,
///     so que o `growbranches()` foi escrito no tipo ERRADO (`BuffMastery.dm:15` declara
///     `/datum/skill/tree/effusionmas/growbranches()`), e o `enableskill` so alcanca os galhos da propria arvore
///     (`trees.dm:172-176`). O extrator copiou o engano fielmente; as tres regras voltam pra arvore delas.
///   * BRAIN POWER -- `if(savant.Class=="Hermano") enableskill(...)` (`gray.dm:17-18`). Esta no `effector()` da
///     arvore racial, que o extrator nao le (ele le o `growbranches()`).
///   * FULL POWER -- `if(S.level==3) enableskill(/datum/skill/fullpower)` com `S` = a Meditate Power
///     (`gray.dm:13-16`, o mesmo `effector()`). E um degrau de OUTRA skill: entra pelo canal que ja acende as
///     Advanced/Perfect da Mente (<see cref="PorDegrau"/>).
///   * MAKE DRAGON STATUE -- `Class == "Dragon clan" && (signature == Earth_Guardian || conq_owns_any(signature))`
///     (`namekian.dm:24-27`, tambem no `effector()`). As duas perguntas de territorio sao do servidor, e chegam a
///     regra por um identificador que so ele responde (<see cref="TemOndeErguer"/>).
///   * RAPID MOVEMENT -- nasce `enabled=0` com `prereqs = list()` e o comentario *"You must learn a skill before
///     enabling this to be learned"* (`speedy.dm:94-107`): e o molde de skill do DM, e ninguem preencheu a lista.
///     O pre-requisito e DECISAO DESTE PORT (o dono pediu um): Speed Drills, o treino de velocidade da mesma arvore.
///
/// POR QUE AQUI, E NAO NO `skills.json`: o JSON e o que o extrator LE do DM, defeitos inclusive, e uma re-extracao o
/// reescreve. O que o port corrige por cima mora em codigo, com o motivo ao lado -- a mesma escolha do
/// <see cref="DegrausDoPort"/>.
/// ==============================================================================================
/// </summary>
public static class AcendedoresDoPort
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o catalogo e as regras de nivel carregam sem correcao nenhuma -- as
	/// oito folhas voltam a ficar "sem acendedor", a v0.2.0. Lido na carga (`SkillCatalog.Parse`,
	/// `RegrasDoDisco.Carregar`). Falso em jogo, sempre.
	/// </summary>
	public static bool SemCorrecaoDeTeste;

	public const string ArvoreDeEfusao = "/datum/skill/tree/effusionmas";
	public const string ArvoreDeBuff = "/datum/skill/tree/kibuffmas";
	public const string ArvoreDoGray = "/datum/skill/tree/gray";
	public const string ArvoreDoNamek = "/datum/skill/tree/namek";

	public const string BasicKiEffusion = "/datum/skill/mind/Basic_Ki_Effusion";
	public const string BrainPower = "/datum/skill/gray/brainpower";
	public const string FullPower = "/datum/skill/fullpower";
	public const string MeditatePower = "/datum/skill/gray/meditatepower";
	public const string EstatuaDoDragao = "/datum/skill/rank/MakeDragonballs";
	public const string RapidMovement = "/datum/skill/rapidmovement";
	public const string SpeedDrills = "/datum/skill/drills";

	/// <summary>As tres de entrada da Ki Buff Mastery -- ver o cabecalho.</summary>
	public static readonly string[] BasicasDeBuff =
	[
		"/datum/skill/mind/Basic_Buff_Mastery", "/datum/skill/mind/Basic_Debuff_Mastery", "/datum/skill/mind/Basic_Defense_Mastery",
	];

	// =====================================================================
	// O QUE SO O SERVIDOR SABE RESPONDER
	// =====================================================================
	/// <summary>
	/// "TEM ONDE ERGUER A ESTATUA?" -- e Guardiao da Terra ou domina um planeta (`namekian.dm:24`). A regra da arvore
	/// pergunta por este nome, e quem responde e o contador do servidor (`GameServer.ContadorDoServidor`).
	/// </summary>
	public const string TemOndeErguer = "temOndeErguerAEstatua";

	/// <summary>O port responde por este identificador de regra, mesmo ele nao sendo campo da ficha?</summary>
	public static bool EDoServidor(string ident) => ident == TemOndeErguer;

	// =====================================================================
	// AS TABELAS
	// =====================================================================
	/// <summary>Regras que SAEM de uma arvore: o galho que o DM pendurou no lugar errado.</summary>
	public static readonly (string Arvore, string Regra)[] ForaDeLugar =
		[.. BasicasDeBuff.Select(p => (ArvoreDeEfusao, $"acende;{p};"))];

	/// <summary>Regras que ENTRAM numa arvore, no formato do extrator (`tipo;alvo;condicao`; condicao vazia = sempre).</summary>
	public static readonly (string Arvore, string Regra)[] Regras =
	[
		(ArvoreDeEfusao, $"acende;{BasicKiEffusion};"),
		.. BasicasDeBuff.Select(p => (ArvoreDeBuff, $"acende;{p};")),
		(ArvoreDoGray, $"acende;{BrainPower};Class=='Hermano'"),
		(ArvoreDoNamek, $"acende;{EstatuaDoDragao};Class=='Dragon clan'&&{TemOndeErguer}"),
	];

	/// <summary>Pre-requisitos que o port deu a uma folha.</summary>
	public static readonly (string Skill, string PreRequisito)[] PreRequisitos = [(RapidMovement, SpeedDrills)];

	/// <summary>O degrau de uma skill que acende outra: (a dona, o nivel exato, a que acende).</summary>
	public static readonly (string Dona, int Nivel, string Acende)[] PorDegrau = [(MeditatePower, 3, FullPower)];

	// =====================================================================
	// A APLICACAO
	// =====================================================================
	/// <summary>
	/// Aplica as tabelas de arvore e de pre-requisito ao catalogo recem-lido. Devolve quantas mudancas pegaram --
	/// regra que ja estava la (uma re-extracao que passe a trazer o `effector()`) nao entra duas vezes.
	/// </summary>
	public static int Aplicar(SkillCatalog cat)
	{
		if (SemCorrecaoDeTeste) return 0;
		int n = 0;

		foreach ((string arvore, string regra) in ForaDeLugar)
		{
			if (cat.Get(arvore) is not { Arvore: true } a || !a.Regras.Contains(regra, StringComparer.Ordinal)) continue;
			a.Regras = [.. a.Regras.Where(r => r != regra)];
			a.RegrasDeGalho = [.. a.Regras.Select(RegraDeArvore.Parse).Where(r => r != null)!];
			n++;
		}
		foreach ((string arvore, string regra) in Regras)
		{
			if (cat.Get(arvore) is not { Arvore: true } a || a.Regras.Contains(regra, StringComparer.Ordinal)) continue;
			a.Regras = [.. a.Regras, regra];
			a.RegrasDeGalho = [.. a.Regras.Select(RegraDeArvore.Parse).Where(r => r != null)!];
			n++;
		}
		foreach ((string skill, string pre) in PreRequisitos)
		{
			if (cat.Get(skill) is not { Arvore: false } s || cat.Get(pre) == null
				|| s.PreReqs.Contains(pre, StringComparer.OrdinalIgnoreCase)) continue;
			s.PreReqs = [.. s.PreReqs, pre];
			n++;
		}
		return n;
	}

	/// <summary>
	/// O mesmo pros degraus recem-lidos do `niveis.json` (path -> degraus): o degrau EXATO da dona passa a destravar
	/// a outra. Devolve quantos pegaram.
	/// </summary>
	public static int AplicarAosDegraus(IReadOnlyDictionary<string, List<Degrau>> degraus)
	{
		if (SemCorrecaoDeTeste) return 0;
		int n = 0;
		foreach ((string dona, int nivel, string acende) in PorDegrau)
		{
			if (!degraus.TryGetValue(dona, out List<Degrau>? l)) continue;
			if (l.Find(d => d.Periodo == 0 && d.Nivel == nivel) is not { } degrau
				|| degrau.Destrava.Contains(acende, StringComparer.OrdinalIgnoreCase)) continue;
			degrau.Destrava = [.. degrau.Destrava, acende];
			n++;
		}
		return n;
	}
}
