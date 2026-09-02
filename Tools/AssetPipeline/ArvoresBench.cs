using Jandirus.Core.Skills;
using Jandirus.Core.Stats;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DAS ARVORES DE SKILL -- `dotnet run --project Tools/AssetPipeline -- arvores [Assets/Data]`.
///
/// ============================ O QUE ELA PROVA, E POR QUE SEM GODOT ============================
/// Tudo aqui e Core de producao: o `SkillCatalog` lido do MESMO `skills.json` que o jogo carrega, o
/// `SkillBook` que o servidor e o cliente usam, o `EfeitosDeSkill` que escreve os contadores na ficha
/// e o `RegraDeArvore` que le o `growbranches()` extraido. Nao ha copia de regra nesta bancada: ela
/// compra pelo `Aprender`, aplica pelo `Aplicar`, recalcula pelo `Recalcular` e pergunta pelo
/// `Avaliar`. O que ela NAO cobre e o funil do servidor (pacote, `AplicarEfeitos`), e isso e da
/// irma dela, a `--arvoreteste`.
///
/// AS DUAS RAIZES QUE ELA GUARDA (medidas por dois agentes, convergentes):
///   R1. `enabled = 0` no DM e "trancada ATE o pre-requisito entrar" (`skill.dm:26`), e o port lia
///       como tranca permanente: 43 folhas com pre-requisito nunca abriam, os contadores de corpo
///       tinham teto 1, e as quatro arvores do `Body.dm:24-32` nunca abriam.
///   R2. NAO EXISTIA TIER DE ARVORE: a vitrine do DM so mostra `tier <= allowedtier`
///       (`HtmlUI.dm:820`), e o tier cresce por marco INVESTIDO (`Body.dm:20-21`). O Afterimage
///       (tier 2, sem pre-requisito) saia de graca no primeiro milissegundo.
///   As duas vao JUNTAS: portar o tier sem consertar R1 tranca o Afterimage PRA SEMPRE (so a Basic
///   Training e compravel, o investimento para em 1). A familia 3 e exatamente essa prova: ela fica
///   vermelha se R1 for desfeita.
/// ==========================================================================================
///
/// AS SEIS FAMILIAS:
///   1. O DADO        -- o `skills.json` no disco tem o que o extrator passou a extrair (galhos que a
///                       continuacao por parentese perdia, `Tree_Mastery` fora das arvores, as regras).
///   2. O `enabled`   -- pre-requisito acende; sem acendedor e Desligada; acendedor de arvore e
///                       AguardaAcendedor com a condicao como DADO.
///   3. O TIER        -- Afterimage: trancado ao nascer (falta investir 4), compravel depois de investir
///                       4 no Body; e o tier RECUA e REEMBOLSA ao esquecer.
///   4. AS PORTAS     -- uma prova por arvore de R4 (Wrestling, Assassain, Effusive Mastery, Effusive
///                       Specialty, Ki Buff Mastery, Magic) mais as quatro do Body.
///   5. O VEREDITO    -- os numeros que a tela vai ler (FaltaInvestir, PreReqsFaltando, Acendedor).
///   6. O CENSO       -- as trancadas classificadas, e a Ki Buff Mastery nomeada como sem acendedor.
///
/// CODIGO DE SAIDA = numero de falhas.
/// </summary>
public static class ArvoresBench
{
	private static int _ok, _falhou;

	private static void Conferir(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _ok++; Console.WriteLine($"  ok     {oque}"); return; }
		_falhou++;
		Console.WriteLine($"  FALHA  {oque}   {detalhe}");
	}

	private const string Body = "/datum/skill/tree/Body";
	private const string Training = "/datum/skill/training";
	private const string Evasive = "/datum/skill/evasive";
	private const string Afterimage = "/datum/skill/ki/Afterimage";
	private const string RapidMovement = "/datum/skill/rapidmovement";
	private const string KiAwareness = "/datum/skill/mind/Basic_Ki_Awareness";
	private const string BuffMastery = "/datum/skill/mind/Basic_Buff_Mastery";
	private const string MartialArts = "/datum/skill/MartialSkill/MartialArts";

	public static int Run(string pastaDados)
	{
		Console.WriteLine("=== AS ARVORES DE SKILL: o tier de vitrine e o `enabled` lido como o DM le ===\n");
		_ok = _falhou = 0;

		string cs = Path.Combine(pastaDados, "skills.json"), ct = Path.Combine(pastaDados, "skilltrees.json");
		if (!File.Exists(cs) || !File.Exists(ct))
		{
			Conferir($"ha `skills.json` e `skilltrees.json` em {pastaDados}", false);
			return _falhou;
		}
		SkillCatalog cat = SkillCatalog.Parse(File.ReadAllText(cs), File.ReadAllText(ct));

		try
		{
			OAvaliador();
			ODado(cat);
			OEnabled(cat);
			OTier(cat);
			AsPortas(cat);
			OVeredito(cat);
			OCenso(cat);
		}
		catch (Exception e)
		{
			_falhou++;
			Console.WriteLine($"  FALHA  a bancada rodou inteira   {e}");
		}

		Console.WriteLine($"\n=== ARVORES: {_ok} OK, {_falhou} FALHA ===");
		return _falhou;
	}

	// =====================================================================
	// 0) O AVALIADOR DE CONDICOES -- as formas que o DM usa, com valores na mao
	// =====================================================================
	private static void OAvaliador()
	{
		Console.WriteLine("-- 0) O AVALIADOR: as condicoes do DM, com contadores na mao --");

		static ContextoDeRegra Ctx(int invested, string classe, params (string, double)[] contadores)
		{
			var d = contadores.ToDictionary(c => c.Item1, c => c.Item2, StringComparer.Ordinal);
			return new ContextoDeRegra
			{
				Invested = invested, Classe = classe, Raca = "Alien",
				Contador = nome => d.TryGetValue(nome, out double v) ? v : null,
			};
		}
		static bool? Vale(string regra, ContextoDeRegra ctx) => RegraDeArvore.Parse(regra)!.Vale(ctx);

		Conferir("`invested>=4` com 4: verdadeiro", Vale("tier;2;invested>=4", Ctx(4, "None")) == true);
		Conferir("`invested>=4` com 3: falso", Vale("tier;2;invested>=4", Ctx(3, "None")) == false);
		Conferir("`invested>=1&&invested<4` com 2: verdadeiro (Spirit.dm:10-11)",
				 Vale("tier;3;invested>=1&&invested<4", Ctx(2, "None")) == true,
				 $"{Vale("tier;3;invested>=1&&invested<4", Ctx(2, "None"))}");
		Conferir("`invested>=1&&invested<4` com 4: falso", Vale("tier;3;invested>=1&&invested<4", Ctx(4, "None")) == false);
		Conferir("`invested>=3||(invested&&Class!='None')` com 3/None: verdadeiro (alien.dm:15)",
				 Vale("apaga;*;invested>=3||(invested&&Class!='None')", Ctx(3, "None")) == true,
				 $"{Vale("apaga;*;invested>=3||(invested&&Class!='None')", Ctx(3, "None"))}");
		Conferir("...com 1/None: falso", Vale("apaga;*;invested>=3||(invested&&Class!='None')", Ctx(1, "None")) == false);
		Conferir("...com 1/Legendary: verdadeiro (classe escolhida + qualquer investimento)",
				 Vale("apaga;*;invested>=3||(invested&&Class!='None')", Ctx(1, "Legendary")) == true);
		Conferir("`bodyskill>2&&weaponeq` com bodyskill 3 e weaponeq DESCONHECIDO: desconhecido (nao dispara)",
				 Vale("arvore;x;bodyskill>2&&weaponeq", Ctx(0, "None", ("bodyskill", 3))) == null);
		Conferir("...e com bodyskill 1: FALSO mesmo sem saber o weaponeq (curto-circuito)",
				 Vale("arvore;x;bodyskill>2&&weaponeq", Ctx(0, "None", ("bodyskill", 1))) == false);
		Conferir("`KOcount>=100&&Age>10&&BP>=godki_at*0.9` sem os campos: desconhecido, e o contexto diz quais",
				 Vale("acende;x;KOcount>=100&&Age>10&&BP>=godki_at*0.9", Ctx(0, "None")) == null);
		Conferir("`effspec==0` com effspec 0: verdadeiro; com 2: falso",
				 Vale("acende;x;effspec==0", Ctx(0, "None", ("effspec", 0))) == true
				 && Vale("acende;x;effspec==0", Ctx(0, "None", ("effspec", 2))) == false);
		Conferir("`gravitate` sozinho e verdade do DM: 1 e verdadeiro, 0 e falso",
				 Vale("acende;x;gravitate", Ctx(0, "None", ("gravitate", 1))) == true
				 && Vale("acende;x;gravitate", Ctx(0, "None", ("gravitate", 0))) == false);
		Conferir("`Rank=='Turtle'` sem cargo no contexto: desconhecido",
				 Vale("acende;x;Rank=='Turtle'", Ctx(0, "None")) == null);
		Conferir("`?` (laco que o port nao enxerga): desconhecido", Vale("acende;x;?", Ctx(0, "None")) == null);
		Conferir("condicao vazia: sempre", Vale("acende;x;", Ctx(0, "None")) == true);
		Conferir("`invested>=4` tem investido minimo 4; `invested>4` tem 5; `lssj==3` nao tem",
				 RegraDeArvore.Parse("tier;2;invested>=4")!.InvestidoMinimo == 4
				 && RegraDeArvore.Parse("arvore;x;invested>4")!.InvestidoMinimo == 5
				 && RegraDeArvore.Parse("tier;4;lssj==3")!.InvestidoMinimo == null);
	}

	// =====================================================================
	// 1) O DADO NO DISCO
	// =====================================================================
	private static void ODado(SkillCatalog cat)
	{
		Console.WriteLine("-- 1) O DADO: o skills.json que o jogo le --");

		Conferir("Internal Cultivation tem os 7 galhos do DM (a continuacao por parentese perdia 3)",
				 cat.Get("/datum/skill/tree/Cultivation")?.Galhos.Length == 7,
				 $"{cat.Get("/datum/skill/tree/Cultivation")?.Galhos.Length}");
		Conferir("Spirit Doll tem os 6 (perdia 3)",
				 cat.Get("/datum/skill/tree/spiritdoll")?.Galhos.Length == 6,
				 $"{cat.Get("/datum/skill/tree/spiritdoll")?.Galhos.Length}");
		Conferir("Custom Attacks tem os 10 distintos (perdia 8)",
				 cat.Get("/datum/skill/tree/Custom_Attacks")?.Galhos.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 10,
				 $"{cat.Get("/datum/skill/tree/Custom_Attacks")?.Galhos.Distinct().Count()}");
		Conferir("`/datum/skill/Tree_Mastery` e FOLHA, nao arvore (casava com o prefixo `/datum/skill/tree`)",
				 cat.Get("/datum/skill/Tree_Mastery") is { Arvore: false });
		Conferir("...e ela continua pendurada na Weapons Expert",
				 cat.Get("/datum/skill/tree/WeaponsExpert")?.Galhos.Contains("/datum/skill/Tree_Mastery") == true);
		Conferir("`Body Change` NAO pende da arvore Alien (esta COMENTADA em alien.dm:12 -- o extrator lia o comentario)",
				 cat.Get("/datum/skill/tree/alien")?.Galhos.Contains("/datum/skill/Body_Change") == false);

		Skill? body = cat.Get(Body);
		Conferir("Body nasce com allowedtier 1 e maxtier 10 (Body.dm:4-5)",
				 body is { TierInicial: 1, TierMax: 10 }, $"{body?.TierInicial}/{body?.TierMax}");
		Conferir("Body tem `tier;2;invested>=4` e `tier;3;invested>=7` (Body.dm:20-21)",
				 body != null && body.Regras.Contains("tier;2;invested>=4") && body.Regras.Contains("tier;3;invested>=7"),
				 string.Join(" | ", body?.Regras ?? []));
		Conferir("...e as quatro portas do Body.dm:24-32, com a flag `didbodychange` NEUTRALIZADA",
				 body != null
				 && body.Regras.Contains("arvore;/datum/skill/tree/Bodybuilding;bodyreadiness>2")
				 && body.Regras.Contains("arvore;/datum/skill/tree/MartialSkill;bodyskill>2")
				 && body.Regras.Contains("arvore;/datum/skill/tree/WeaponsExpert;bodyskill>2&&weaponeq")
				 && body.Regras.Contains("arvore;/datum/skill/tree/Cultivation;bodyskill>=2&&bodyreadiness>=2"));

		(string Arvore, string Regra, string Dm)[] r4 =
		[
			("/datum/skill/tree/Bodybuilding", "arvore;/datum/skill/tree/Wrestling;invested>=4", "Bodybuilding.dm:15-17"),
			("/datum/skill/tree/MartialSkill", "arvore;/datum/skill/tree/Assassain;invested>4", "Martial Skill.dm:19-20"),
			("/datum/skill/tree/Mind", "arvore;/datum/skill/tree/effusionmas;kieffusionskill>=1", "Mind.dm:17-18"),
			("/datum/skill/tree/Mind", "arvore;/datum/skill/tree/kibuffmas;kibuffskill>=1", "Mind.dm:28-29"),
			("/datum/skill/tree/effusionmas", "arvore;/datum/skill/tree/effusionspec;effusionspecial==1", "Effusion.dm:22-23"),
			("/datum/skill/tree/Spirit", "arvore;/datum/skill/tree/Magic;invested>=4", "Spirit.dm:14-15"),
		];
		foreach ((string arv, string regra, string dm) in r4)
			Conferir($"{arv.Split('/')[^1]} traz `{regra}` ({dm})",
					 cat.Get(arv)?.Regras.Contains(regra) == true, string.Join(" | ", cat.Get(arv)?.Regras ?? []));

		Conferir("Assassain e Wrestling: `min(invested+1,6)` virou degraus 1..5 (Assassain.dm:13, Wrestling.dm:11)",
				 cat.Get("/datum/skill/tree/Assassain")?.Regras.Contains("tier;5;invested>=4") == true
				 && cat.Get("/datum/skill/tree/Wrestling")?.Regras.Contains("tier;2;invested>=1") == true);
		Conferir("a exclusividade das especialidades de Ki, que so mora no PRUNE, foi lida "
				 + "(Effusion Specialization.dm:37-47)",
				 cat.Get("/datum/skill/tree/effusionspec")?.Regras.Contains("apaga;/datum/skill/effusionspec/Interference;effspec==1") == true,
				 string.Join(" | ", cat.Get("/datum/skill/tree/effusionspec")?.Regras.Where(r => r.StartsWith("apaga")) ?? []));
		Conferir("toda regra do catalogo e legivel pelo Core (nenhuma caiu no parse)",
				 cat.Arvores.All(a => a.RegrasDeGalho.Length == a.Regras.Length),
				 string.Join(", ", cat.Arvores.Where(a => a.RegrasDeGalho.Length != a.Regras.Length).Select(a => a.Path)));
		Conferir("`can_forget` chegou: Spirit Unleashed nao se esquece, Basic Training sim",
				 cat.Get("/datum/skill/SpiritUnleashed") is { Esquecivel: false } && cat.Get(Training) is { Esquecivel: true });
	}

	// =====================================================================
	// 2) O `enabled` LIDO COMO O DM LE
	// =====================================================================
	private static void OEnabled(SkillCatalog cat)
	{
		Console.WriteLine("\n-- 2) O `enabled = 0`: pre-requisito acende, acendedor de arvore acende, o resto e Desligada --");

		(SkillBook livro, Fighter f) = Nascer(3);
		ContextoDeRegra ctx = ContextoDeRegra.De(f, "Saiyan", "None");
		livro.Recalcular(cat, ctx, "Saiyan", "None");

		Veredito ev = livro.Avaliar(cat, Evasive, "Saiyan", "None", false);
		Conferir("Evasion Training nasce `enabled = 0` COM pre-requisito -> a recusa e FaltaPreRequisito, nao Desligada",
				 ev.Motivo == Recusa.FaltaPreRequisito, ev.Motivo.ToString());
		Conferir("...e o veredito diz QUAL: Basic Training",
				 ev.PreReqsFaltando.Length == 1 && ev.PreReqsFaltando[0] == Training, string.Join(",", ev.PreReqsFaltando));

		Conferir("comprar Basic Training passa", Comprar(livro, cat, f, Training) == Recusa.Pode);
		Conferir("...e Evasion Training ABRE (o testskillprereqs do trees.dm:28-36)",
				 livro.PodeAprender(cat, Evasive, "Saiyan", "None", false) == Recusa.Pode,
				 livro.PodeAprender(cat, Evasive, "Saiyan", "None", false).ToString());

		Veredito rm = livro.Avaliar(cat, RapidMovement, "Saiyan", "None", false);
		Conferir("Rapid Movement nasce `enabled = 0` SEM pre-requisito e SEM regra que a acenda -> Desligada de verdade",
				 rm.Motivo == Recusa.Desligada, rm.Motivo.ToString());

		Veredito ka = livro.Avaliar(cat, KiAwareness, "Saiyan", "None", false);
		Conferir("Basic Ki Awareness nasce apagada e tem acendedor na Mind (Mind.dm:15) -> AguardaAcendedor",
				 ka.Motivo == Recusa.AguardaAcendedor, ka.Motivo.ToString());
		Conferir("...com a condicao como DADO: `kiawarenessskill>=1`",
				 ka.Acendedor == "kiawarenessskill>=1", ka.Acendedor);
		f.kiawarenessskill = 1;
		livro.Recalcular(cat, ctx, "Saiyan", "None");
		Conferir("...e com o contador em 1 ela acende",
				 livro.PodeAprender(cat, KiAwareness, "Saiyan", "None", false) == Recusa.Pode,
				 livro.PodeAprender(cat, KiAwareness, "Saiyan", "None", false).ToString());

		// A KI BUFF MASTERY: a arvore abre por `kibuffskill >= 1`, mas o growbranches que acende as
		// tres basicas dela esta escrito na arvore ERRADA (BuffMastery.dm:15 declara o proc da
		// effusionmas). Fiel ao DM: a folha fica sem acendedor.
		f.kibuffskill = 1;
		livro.Recalcular(cat, ctx, "Saiyan", "None");
		Conferir("kibuffskill=1 abre a Ki Buff Mastery (Mind.dm:28-29)",
				 livro.Destravadas.Contains("/datum/skill/tree/kibuffmas"), string.Join(",", livro.Destravadas));
		Veredito bm = livro.Avaliar(cat, BuffMastery, "Saiyan", "None", false);
		Conferir("...mas a Basic Buff Mastery e Desligada: o acendedor do DM esta na arvore errada (BuffMastery.dm:15)",
				 bm.Motivo == Recusa.Desligada, bm.Motivo.ToString());
	}

	// =====================================================================
	// 3) O TIER DE VITRINE -- o Afterimage
	// =====================================================================
	private static void OTier(SkillCatalog cat)
	{
		Console.WriteLine("\n-- 3) O TIER: o Afterimage NAO sai de graca, sai depois de investir 4 no Body; e o tier recua --");

		(SkillBook livro, Fighter f) = Nascer(10);
		ContextoDeRegra ctx = ContextoDeRegra.De(f, "Saiyan", "None");
		livro.Recalcular(cat, ctx, "Saiyan", "None");

		Veredito a0 = livro.Avaliar(cat, Afterimage, "Saiyan", "None", false);
		Conferir("ao nascer o Afterimage (tier 2, sem pre-requisito) e TierTrancado -- e nao Pode",
				 a0.Motivo == Recusa.TierTrancado, a0.Motivo.ToString());
		Conferir("...pela arvore Body, que mostra ate o tier 1", a0.Arvore == Body && a0.TierDaArvore == 1,
				 $"{a0.Arvore} t{a0.TierDaArvore}");
		Conferir("...e faltam investir 4 (Body.dm:20)", a0.FaltaInvestir == 4, $"{a0.FaltaInvestir}");
		Conferir("Body Expansion (tier 2) tambem esta trancada pra um Saiyan (ele nao tem a racial do Alien)",
				 livro.PodeAprender(cat, "/datum/skill/expand", "Saiyan", "None", false) == Recusa.TierTrancado);
		Conferir("o proximo degrau do Body diz: invista ate 4 pra chegar no tier 2",
				 livro.Arvore(Body) is { ProximoInvestir: 4, ProximoTier: 2 },
				 $"{livro.Arvore(Body)?.ProximoInvestir}/{livro.Arvore(Body)?.ProximoTier}");

		// INVESTE 4 pelo balcao: o que estiver a venda no Body, tier 1, um marco de cada vez.
		var compradas = new List<string>();
		for (int i = 0; i < 4; i++)
		{
			string? p = OfertaDoBody(livro, cat);
			if (p == null) break;
			if (Comprar(livro, cat, f, p) == Recusa.Pode) compradas.Add(p);
		}
		Conferir($"quatro compras de tier 1 no Body passaram ({string.Join(", ", compradas.Select(p => cat.Get(p)?.Nome))})",
				 compradas.Count == 4 && livro.Arvore(Body)?.Investido == 4,
				 $"{compradas.Count} compras, investido {livro.Arvore(Body)?.Investido}");
		Conferir("...o Body passou pro tier 2", livro.Arvore(Body)?.Tier == 2, $"{livro.Arvore(Body)?.Tier}");
		Conferir("...o proximo degrau agora e 7 -> tier 3",
				 livro.Arvore(Body) is { ProximoInvestir: 7, ProximoTier: 3 });
		Recusa a4 = livro.PodeAprender(cat, Afterimage, "Saiyan", "None", false);
		Conferir("...e o Afterimage ficou COMPRAVEL (esta e a linha que fica vermelha se R1 for desfeita: "
				 + "com `enabled = 0` permanente so a Basic Training se compra e o Body para em 1)",
				 a4 == Recusa.Pode, a4.ToString());
		Conferir("comprar o Afterimage passa e custa 2 (tier 2, custo normalizado)",
				 Comprar(livro, cat, f, Afterimage) == Recusa.Pode && livro.MarcosLivres == 4, $"{livro.MarcosLivres} marcos");
		Conferir("...e o investimento do Body soma o custo dele: 6", livro.Arvore(Body)?.Investido == 6);

		// O TIER RECUA E REEMBOLSA: esquece tres de tier 1 (a que nao e pre-requisito de ninguem
		// primeiro) ate o investimento cair abaixo de 4 -- o Afterimage tem que cair junto.
		int marcosAntes = livro.MarcosLivres;
		var cascatas = new List<string>();
		foreach (string p in Enumerable.Reverse(compradas))
		{
			if (p == Training) continue;
			cascatas.AddRange(livro.EsquecerEReembolsar(cat, p, "Saiyan", "None"));
			EfeitosDeSkill.Aplicar(f, cat, livro.Aprendidas);
		}
		Conferir("esquecer as tres de tier 1 devolve 1 marco cada (o refund() do trees.dm:93)",
				 livro.MarcosLivres >= marcosAntes + 3, $"{marcosAntes} -> {livro.MarcosLivres}");
		Conferir("...o Body recuou pro tier 1", livro.Arvore(Body)?.Tier == 1, $"{livro.Arvore(Body)?.Tier}");
		Conferir("...e o Afterimage CAIU na cascata (treeshrink, trees.dm:119-125) e foi reembolsado",
				 cascatas.Contains(Afterimage) && !livro.Sabe(Afterimage) && livro.MarcosLivres == 9,
				 $"cascata: {string.Join(",", cascatas)} | {livro.MarcosLivres} marcos");
		Conferir("...a Basic Training ficou (so ela e tier 1 e nao perdeu pre-requisito)",
				 livro.Sabe(Training) && livro.Aprendidas.Count == 1, string.Join(",", livro.Aprendidas));
	}

	// =====================================================================
	// 4) AS PORTAS -- uma por arvore de R4, e as do Body
	// =====================================================================
	private static void AsPortas(SkillCatalog cat)
	{
		Console.WriteLine("\n-- 4) AS PORTAS: cada arvore que o growbranches abre, fechada antes e aberta depois --");

		// Martial Arts pelo caminho REAL: os contadores vem das skills compradas (EfeitosDeSkill).
		{
			(SkillBook livro, Fighter f) = Nascer(10);
			ContextoDeRegra ctx = ContextoDeRegra.De(f, "Saiyan", "None");
			livro.Recalcular(cat, ctx, "Saiyan", "None");
			Conferir("Martial Arts NAO aparece ao nascer (a arvore nao e sua)",
					 livro.PodeAprender(cat, MartialArts, "Saiyan", "None", false) == Recusa.SemArvore);
			// Basic Training, Evasion, Light Skill: +1 bodyskill cada -> 3
			foreach (string p in new[] { Training, Evasive, "/datum/skill/qingqong" }) Comprar(livro, cat, f, p);
			Conferir("tres skills de corpo levam bodyskill a 3", f.bodyskill > 2, $"{f.bodyskill}");
			Conferir("...e a Martial Skill ABRE (Body.dm:26)", livro.Destravadas.Contains("/datum/skill/tree/MartialSkill"),
					 string.Join(",", livro.Destravadas));
			Recusa ma = livro.PodeAprender(cat, MartialArts, "Saiyan", "None", false);
			Conferir("...e Martial Arts aparece e se compra", ma == Recusa.Pode, ma.ToString());

			// Assassain: MartialSkill invested > 4 (Martial Skill.dm:19-20)
			Conferir("Assassain fechada antes de investir na Martial Skill", !livro.Destravadas.Contains("/datum/skill/tree/Assassain"));
			livro.Conceder(20);
			int voltas = 0;
			while ((livro.Arvore("/datum/skill/tree/MartialSkill")?.Investido ?? 0) <= 4 && voltas++ < 12)
			{
				string? p = livro.Ofertas(cat, "Saiyan", "None", false)
					.Where(o => o.Estado == Recusa.Pode && cat.Get("/datum/skill/tree/MartialSkill")!.Galhos.Contains(o.Skill.Path))
					.OrderBy(o => o.Skill.Tier).Select(o => o.Skill.Path).FirstOrDefault();
				if (p == null) break;
				Comprar(livro, cat, f, p);
			}
			Conferir($"...investir mais de 4 na Martial Skill abre a Assassain ({livro.Arvore("/datum/skill/tree/MartialSkill")?.Investido} investidos)",
					 livro.Destravadas.Contains("/datum/skill/tree/Assassain"), string.Join(",", livro.Destravadas));
		}

		// Bodybuilding -> Wrestling, pelo investimento (Bodybuilding.dm:15-17)
		{
			(SkillBook livro, Fighter f) = Nascer(30);
			ContextoDeRegra ctx = ContextoDeRegra.De(f, "Saiyan", "None");
			livro.Recalcular(cat, ctx, "Saiyan", "None");
			Conferir("Bodybuilding fechada ao nascer", !livro.Destravadas.Contains("/datum/skill/tree/Bodybuilding"));
			f.bodyreadiness = 3;
			livro.Recalcular(cat, ctx, "Saiyan", "None");
			Conferir("bodyreadiness > 2 abre a Bodybuilding (Body.dm:24-25)", livro.Destravadas.Contains("/datum/skill/tree/Bodybuilding"));
			Conferir("...e a Wrestling continua fechada", !livro.Destravadas.Contains("/datum/skill/tree/Wrestling"));
			int voltas = 0;
			while ((livro.Arvore("/datum/skill/tree/Bodybuilding")?.Investido ?? 0) < 4 && voltas++ < 12)
			{
				string? p = livro.Ofertas(cat, "Saiyan", "None", false)
					.Where(o => o.Estado == Recusa.Pode && cat.Get("/datum/skill/tree/Bodybuilding")!.Galhos.Contains(o.Skill.Path))
					.OrderBy(o => o.Skill.Tier).Select(o => o.Skill.Path).FirstOrDefault();
				if (p == null) break;
				Comprar(livro, cat, f, p);
			}
			Conferir($"...investir 4 na Bodybuilding abre a Wrestling ({livro.Arvore("/datum/skill/tree/Bodybuilding")?.Investido} investidos)",
					 livro.Destravadas.Contains("/datum/skill/tree/Wrestling"), string.Join(",", livro.Destravadas));
			Conferir("...e o tier da Bodybuilding chegou a 3 (Bodybuilding.dm:13-14)",
					 livro.Arvore("/datum/skill/tree/Bodybuilding")?.Tier == 3, $"{livro.Arvore("/datum/skill/tree/Bodybuilding")?.Tier}");
			Conferir("Grabber (Wrestling, tier 1) ficou compravel",
					 livro.PodeAprender(cat, "/datum/skill/Wrestling/Grabber", "Saiyan", "None", false) == Recusa.Pode,
					 livro.PodeAprender(cat, "/datum/skill/Wrestling/Grabber", "Saiyan", "None", false).ToString());
		}

		// As tres de Ki, por contador (Mind.dm:17, :28; Effusion.dm:22)
		{
			(SkillBook livro, Fighter f) = Nascer(3, "Human");
			ContextoDeRegra ctx = ContextoDeRegra.De(f, "Human", "None");
			livro.Recalcular(cat, ctx, "Human", "None");
			Conferir("Effusive Mastery, Effusive Specialty e Ki Buff Mastery fechadas ao nascer",
					 !livro.Destravadas.Contains("/datum/skill/tree/effusionmas")
					 && !livro.Destravadas.Contains("/datum/skill/tree/effusionspec")
					 && !livro.Destravadas.Contains("/datum/skill/tree/kibuffmas"));
			f.kieffusionskill = 1;
			livro.Recalcular(cat, ctx, "Human", "None");
			Conferir("kieffusionskill >= 1 abre a Effusive Mastery", livro.Destravadas.Contains("/datum/skill/tree/effusionmas"));
			Conferir("...e a Specialty ainda nao (effusionspecial == 0)", !livro.Destravadas.Contains("/datum/skill/tree/effusionspec"));
			f.effusionspecial = 1;
			livro.Recalcular(cat, ctx, "Human", "None");
			Conferir("effusionspecial == 1 abre a Effusive Specialty -- em CADEIA (Mind -> effusionmas -> effusionspec)",
					 livro.Destravadas.Contains("/datum/skill/tree/effusionspec"), string.Join(",", livro.Destravadas));
			f.kibuffskill = 1;
			livro.Recalcular(cat, ctx, "Human", "None");
			Conferir("kibuffskill >= 1 abre a Ki Buff Mastery", livro.Destravadas.Contains("/datum/skill/tree/kibuffmas"));

			// a exclusividade das especialidades (prune): escolher Ki Shock apaga Interference
			livro.Conceder(5);
			Conferir("Interference esta a venda antes de escolher uma especialidade",
					 livro.PodeAprender(cat, "/datum/skill/effusionspec/Interference", "Human", "None", false) == Recusa.Pode,
					 livro.PodeAprender(cat, "/datum/skill/effusionspec/Interference", "Human", "None", false).ToString());
			Comprar(livro, cat, f, "/datum/skill/effusionspec/Ki_Shock");
			Veredito inter = livro.Avaliar(cat, "/datum/skill/effusionspec/Interference", "Human", "None", false);
			Conferir("...escolhida a Ki Shock (effspec=2), a Interference esta APAGADA (Effusion Specialization.dm:41-43)",
					 inter.Motivo == Recusa.Apagada && inter.Acendedor.Contains("effspec==2"),
					 $"{inter.Motivo} {inter.Acendedor}");
		}

		// Magic: Spirit invested >= 4 (Spirit.dm:14-15)
		{
			(SkillBook livro, Fighter f) = Nascer(10, "Human");
			ContextoDeRegra ctx = ContextoDeRegra.De(f, "Human", "None");
			livro.Recalcular(cat, ctx, "Human", "None");
			Conferir("Magic fechada ao nascer", !livro.Destravadas.Contains("/datum/skill/tree/Magic"));
			int voltas = 0;
			while ((livro.Arvore("/datum/skill/tree/Spirit")?.Investido ?? 0) < 4 && voltas++ < 12)
			{
				string? p = livro.Ofertas(cat, "Human", "None", false)
					.Where(o => o.Estado == Recusa.Pode && cat.Get("/datum/skill/tree/Spirit")!.Galhos.Contains(o.Skill.Path))
					.OrderBy(o => o.Skill.Tier).Select(o => o.Skill.Path).FirstOrDefault();
				if (p == null) break;
				Comprar(livro, cat, f, p);
			}
			Conferir($"investir 4 no Spirit abre a Magic ({livro.Arvore("/datum/skill/tree/Spirit")?.Investido} investidos: "
					 + $"{string.Join(", ", livro.Aprendidas.Select(p => cat.Get(p)?.Nome))})",
					 livro.Destravadas.Contains("/datum/skill/tree/Magic"), string.Join(",", livro.Destravadas));
			Conferir("...e o Spirit esta no tier 4 (Spirit.dm:12-13)", livro.Arvore("/datum/skill/tree/Spirit")?.Tier == 4,
					 $"{livro.Arvore("/datum/skill/tree/Spirit")?.Tier}");
			Veredito mat = livro.Avaliar(cat, "/datum/skill/general/materialization", "Human", "None", false);
			Conferir("Materialize (Magic) nasce APAGADA ate investir 5 na Magic (Magic_Tree.dm:12)",
					 mat.Motivo == Recusa.Apagada && mat.Acendedor.Contains("invested<5"), $"{mat.Motivo} {mat.Acendedor}");
		}

		// Cultivation e Weapons Expert, por contador (Body.dm:27-31)
		{
			(SkillBook livro, Fighter f) = Nascer(3);
			ContextoDeRegra ctx = ContextoDeRegra.De(f, "Saiyan", "None");
			f.bodyskill = 2; f.bodyreadiness = 2;
			livro.Recalcular(cat, ctx, "Saiyan", "None");
			Conferir("bodyskill 2 E bodyreadiness 2 abrem a Cultivation, e nao a Martial Skill (que pede > 2)",
					 livro.Destravadas.Contains("/datum/skill/tree/Cultivation") && !livro.Destravadas.Contains("/datum/skill/tree/MartialSkill"));
			f.bodyskill = 3;
			livro.Recalcular(cat, ctx, "Saiyan", "None");
			Conferir("bodyskill 3 sem arma: Martial Skill sim, Weapons Expert NAO",
					 livro.Destravadas.Contains("/datum/skill/tree/MartialSkill") && !livro.Destravadas.Contains("/datum/skill/tree/WeaponsExpert"));
			f.weaponeq = 1;
			livro.Recalcular(cat, ctx, "Saiyan", "None");
			Conferir("...com arma equipada a Weapons Expert abre", livro.Destravadas.Contains("/datum/skill/tree/WeaponsExpert"));
		}

		// O Alien tranca os raciais depois de 3 marcos (alien.dm:14-17)
		{
			(SkillBook livro, Fighter f) = Nascer(10, "Alien");
			ContextoDeRegra ctx = ContextoDeRegra.De(f, "Alien", "None");
			livro.Recalcular(cat, ctx, "Alien", "None");
			Conferir("um Alien compra Body Expansion ao nascer pela RACIAL (allowedtier 2), ao contrario do Saiyan",
					 livro.PodeAprender(cat, "/datum/skill/expand", "Alien", "None", false) == Recusa.Pode,
					 livro.PodeAprender(cat, "/datum/skill/expand", "Alien", "None", false).ToString());
			// O GROW apaga em `invested >= 3` (alien.dm:15) e o PRUNE reacende em `invested <= 3`
			// (alien.dm:20): no DM os dois rodam em sequencia, entao em 3 a racial ainda esta ABERTA
			// e em 4 tranca -- exatamente o que a `desc` da arvore promete ("After 4 Milestones").
			foreach (string p in new[] { "/datum/skill/general/Hardened_Body", "/datum/skill/general/LankyLegs", "/datum/skill/general/Willed" })
				Comprar(livro, cat, f, p);
			Conferir("...com 3 marcos investidos na racial ela AINDA esta aberta (o prune reacende em invested<=3, alien.dm:20)",
					 livro.PodeAprender(cat, "/datum/skill/general/imitation", "Alien", "None", false) == Recusa.Pode,
					 livro.PodeAprender(cat, "/datum/skill/general/imitation", "Alien", "None", false).ToString());
			Comprar(livro, cat, f, "/datum/skill/general/imitation");
			Veredito st = livro.Avaliar(cat, "/datum/skill/general/stoptime", "Alien", "None", false);
			Conferir("...e com 4 o resto dela esta APAGADO (alien.dm:14-17: 'After 4 Milestones ... locked')",
					 st.Motivo == Recusa.Apagada && st.Acendedor.Contains("invested>=3"), $"{st.Motivo} {st.Acendedor}");
		}
	}

	// =====================================================================
	// 5) O VEREDITO E DADO
	// =====================================================================
	private static void OVeredito(SkillCatalog cat)
	{
		Console.WriteLine("\n-- 5) O VEREDITO: numeros, nao frases --");
		(SkillBook livro, Fighter f) = Nascer(0);
		livro.Recalcular(cat, ContextoDeRegra.De(f, "Saiyan", "None"), "Saiyan", "None");

		Veredito v = livro.Avaliar(cat, Training, "Saiyan", "None", false);
		Conferir("sem marco nenhum: SemMarcos com custo 1 e faltam 1",
				 v.Motivo == Recusa.SemMarcos && v.Custo == 1 && v.FaltamMarcos == 1, $"{v.Motivo} {v.Custo} {v.FaltamMarcos}");
		Conferir("Planet Destroy sem o bit de vilao: SoVilao",
				 livro.PodeAprender(cat, "/datum/skill/Ki_Control/Planet_Destroy", "Saiyan", "None", false) == Recusa.SoVilao);
		Conferir("a regeneracao namekuseijin pra um Saiyan: SemArvore (a arvore e o gate)",
				 livro.PodeAprender(cat, "/datum/skill/general/regenerate", "Saiyan", "None", false) == Recusa.SemArvore,
				 livro.PodeAprender(cat, "/datum/skill/general/regenerate", "Saiyan", "None", false).ToString());
		Conferir("uma arvore inteira: PodeAprender(arvore) e NaoExiste (arvore nao se compra)",
				 livro.PodeAprender(cat, Body, "Saiyan", "None", false) == Recusa.NaoExiste);

		// O estado do SERVIDOR mandado pro cliente: um livro sem contexto nenhum, so com o estado
		// carregado, tem que dar o MESMO veredito.
		(SkillBook servidor, Fighter fs) = Nascer(10);
		fs.bodyskill = 3;
		servidor.Recalcular(cat, ContextoDeRegra.De(fs, "Saiyan", "None"), "Saiyan", "None");
		var cliente = new SkillBook { MarcosLivres = servidor.MarcosLivres, MarcosTotais = servidor.MarcosTotais };
		cliente.Carregar(servidor.Aprendidas);
		cliente.CarregarEstado(servidor.Destravadas, servidor.Arvores);
		bool iguais = true;
		foreach (Skill s in cat.Todas.Where(s => !s.Arvore))
			if (cliente.PodeAprender(cat, s.Path, "Saiyan", "None", false) != servidor.PodeAprender(cat, s.Path, "Saiyan", "None", false))
			{ iguais = false; Console.WriteLine($"         diverge: {s.Path}"); }
		Conferir("um livro que so RECEBEU o estado (o cliente) da o mesmo veredito que o que CALCULOU (o servidor), "
				 + "pras 317 folhas -- inclusive a Martial Arts, que so abre por contador",
				 iguais && cliente.PodeAprender(cat, MartialArts, "Saiyan", "None", false) == Recusa.Pode);
	}

	// =====================================================================
	// 6) O CENSO
	// =====================================================================
	private static void OCenso(SkillCatalog cat)
	{
		Console.WriteLine("\n-- 6) O CENSO das trancadas --");
		CensoDeSkills.Relatorio r = CensoDeSkills.Levantar(cat);
		int trancadas = r.TrancadasPorPreReq + r.TrancadasSoVilao + r.TrancadasComAcendedor
					  + r.AcendedorForaDoPort.Count + r.TrancadasSoPorEnsino + r.SemAcendedor.Count;
		int ligada0 = cat.Todas.Count(s => !s.Arvore && !s.Ligada);
		Conferir($"o censo classifica TODAS as {ligada0} folhas `ligada: 0` (nenhuma some)", trancadas == ligada0, $"{trancadas}");
		Conferir("43 delas um pre-requisito acende", r.TrancadasPorPreReq == 43, $"{r.TrancadasPorPreReq}");
		Conferir("a Basic Buff Mastery esta nomeada como SEM ACENDEDOR", r.SemAcendedor.Contains("Basic Buff Mastery"),
				 string.Join(", ", r.SemAcendedor.Take(12)));
		Conferir("a Golden Form esta como acendedor FORA DO ALCANCE (le `godki_at`, `KOcount`, `Age`)",
				 r.AcendedorForaDoPort.Any(x => x.Nome == "Golden Form"),
				 string.Join(", ", r.AcendedorForaDoPort.Select(x => x.Nome).Take(12)));
		foreach (string linha in CensoDeSkills.Texto(r).SkipWhile(l => !l.Contains("`enabled = 0`")).Take(8))
			Console.WriteLine("         " + linha);
	}

	// =====================================================================
	/// <summary>Um livro com N marcos e uma ficha crua da raca -- o que um personagem novo e.</summary>
	private static (SkillBook, Fighter) Nascer(int marcos, string raca = "Saiyan")
	{
		var livro = new SkillBook();
		livro.Conceder(marcos);
		return (livro, new Fighter { Race = raca, Class = "None" });
	}

	/// <summary>
	/// Compra pelo Core e aplica os efeitos na ficha -- o que o `AplicarEfeitos` do servidor faz,
	/// na mesma ordem: efeitos ANTES do recalculo, porque sao os contadores que as regras leem.
	/// </summary>
	private static Recusa Comprar(SkillBook livro, SkillCatalog cat, Fighter f, string path)
	{
		Recusa r = livro.Aprender(cat, path, f.Race, f.Class, false);
		if (r != Recusa.Pode) return r;
		EfeitosDeSkill.Aplicar(f, cat, livro.Aprendidas, livro.Escolhas);
		livro.Recalcular(cat, ContextoDeRegra.De(f, f.Race, f.Class), f.Race, f.Class);
		return r;
	}

	private static string? OfertaDoBody(SkillBook livro, SkillCatalog cat) =>
		livro.Ofertas(cat, "Saiyan", "None", false)
			 .Where(o => o.Estado == Recusa.Pode && o.Skill.Tier == 1 && cat.Get(Body)!.Galhos.Contains(o.Skill.Path))
			 .Select(o => o.Skill.Path).FirstOrDefault();
}
