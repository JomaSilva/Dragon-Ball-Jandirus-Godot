using System.Text.Json;
using Jandirus.Core.Races;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;

namespace Jandirus.Tools;

/// <summary>
/// ============================ A BANCADA DO FONTE (`fonte-prova`) ============================
/// OS EXTRATORES LEEM O QUE O COMPILADOR LE -- e nao o que esta comentado, nem o arquivo que o `.dme` nao inclui.
///
/// A irma da familia A0c da `instalar-prova` (que cobra o leitor <see cref="DmFonte"/> e o catalogo de tecnologia),
/// pros tres outros `json` em que a mesma medida achou coisa que o jogo original NAO tem. O dono foi perguntado e
/// respondeu *"pode tirar"* (2026-10-11):
///
///   F1. `skills.json` -- duas skills e uma arvore do sistema VELHO de ataques customizados, de `.dm` que nenhum
///       `#include` cita (`Skills/CustomAttacks/beams`, `blasts`, `physical`);
///   F2. `cargos.json` -- o `GuidedBall` do Rei de Arconia, lido de uma linha `//enableskill(...)`
///       (`Ranks/ordered/SpaceRanks.dm:32`);
///   F3. `races.json` -- a raca `Dog`, a "Example prototype race" de dentro de um `/* */`
///       (`Races/Genetics/Genetic_Prototype.dm:65`);
///   F4. `estilos.json` -- o caso INVERSO, e ele fica como esta: o estilo vivo `Default` (`Style.dm:58`) nao sai
///       porque a linha do tipo traz um `//`. O dono nao mandou acrescentar; a familia so trava o estado de hoje.
///
/// CADA FAMILIA TEM AS DUAS PONTAS: o EXTRATOR, numa arvore DM de mentira com o formato do original e o defeito
/// dentro (e o botao que o religa: `DmFonte.SemDmeDeTeste`, `DmRankScanner.ComentarioViraCodigoDeTeste`,
/// `DmFonte.BlocoNaoEComentarioDeTeste`), e o ARQUIVO publicado, lido pelo parser que o jogo usa.
/// O que acontece com o livro de quem tivesse uma das duas skills e da `--catalogoteste` (servidor, familia 8).
/// ============================================================================================
/// </summary>
public static class FonteBench
{
	private static int _ok, _falhou;
	private static readonly List<string> Vermelhas = [];

	private static void Checa(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; Console.WriteLine($"  ok    {nome}"); }
		else { _falhou++; Vermelhas.Add(nome); Console.WriteLine($"  FALHA {nome}   {detalhe}"); }
	}

	private const string Raio = "/datum/skill/CustomAttacks/ki/beam/Beam";
	private const string Bola = "/datum/skill/CustomAttacks/ki/blast/Blast";
	private const string ArvoreVelha = "/datum/skill/tree/Custom_Attacks";

	public static int Run(string pastaAssets)
	{
		Console.WriteLine("=== FONTE: os extratores leem o que o compilador le ===\n");
		_ok = _falhou = 0;
		Vermelhas.Clear();

		string dados = Path.Combine(pastaAssets, "Data");
		foreach (string f in new[] { "skills.json", "skilltrees.json", "cargos.json", "races.json", "estilos.json" })
			if (!File.Exists(Path.Combine(dados, f))) { Console.WriteLine($"nao achei {Path.Combine(dados, f)}"); return 1; }

		Skills(dados);
		Cargos(dados);
		Racas(dados);
		Estilos(dados);

		Console.WriteLine($"\n===== {_ok} OK, {_falhou} FALHA(S) =====");
		if (_falhou > 0) Console.WriteLine("vermelhas: " + string.Join(" | ", Vermelhas));
		return _falhou > 0 ? 1 : 0;
	}

	/// <summary>Uma arvore DM de mentira numa pasta temporaria: `raiz/Code/...` e, se pedido, o `raiz/jogo.dme`.</summary>
	private static (string Raiz, string Code) Montar(string apelido, (string Arquivo, string[] Linhas)[] arquivos, string[]? includes)
	{
		string raiz = Path.Combine(Path.GetTempPath(), $"jandirus-fonteprova-{apelido}-" + Guid.NewGuid().ToString("N"));
		string code = Path.Combine(raiz, "Code");
		foreach ((string arquivo, string[] linhas) in arquivos)
		{
			string destino = Path.Combine(code, arquivo.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
			File.WriteAllText(destino, string.Join("\n", linhas) + "\n");
		}
		if (includes != null)
			File.WriteAllText(Path.Combine(raiz, "jogo.dme"),
				string.Join("\n", includes.Select(i => $"#include \"Code\\{i.Replace('/', '\\')}\"")) + "\n");
		return (raiz, code);
	}

	private static void Apagar(string raiz)
	{
		try { Directory.Delete(raiz, recursive: true); } catch { /* temporario */ }
	}

	// =====================================================================
	// F1) SKILLS -- o arquivo que o `.dme` nao inclui
	// =====================================================================
	private static void Skills(string dados)
	{
		Console.WriteLine("--- F1: skills -- o `.dm` que nenhum #include cita nao e do jogo ---");

		(string raiz, string code) = Montar("skills",
		[
			("Skills/Vivas.dm",
			[
				"/datum/skill/tree/ArvoreDeBancada",
				"\tname = \"Arvore de Bancada\"",
				"\tconstituentskills = list(new/datum/skill/rank/VivaDeBancada)",
				"",
				"/datum/skill/rank/VivaDeBancada",
				"\tname = \"Viva de Bancada\"",
				"\tdesc = \"compilada\"",
			]),
			// `Skills/CustomAttacks/beams/GenericBeam.dm` e `physical/basic.dm`: a skill e a arvore que a pendura
			("Skills/CustomAttacks/Mortas.dm",
			[
				"/datum/skill/CustomAttacks/ki/beam/RaioDeBancada",
				"\tname = \"Raio de Bancada\"",
				"\tdesc = \"arquivo fora do .dme\"",
				"",
				"/datum/skill/tree/Mortas_De_Bancada",
				"\tname = \"Mortas de Bancada\"",
				"\tconstituentskills = list(new/datum/skill/CustomAttacks/ki/beam/RaioDeBancada)",
			]),
		], includes: ["Skills/Vivas.dm"]);

		try
		{
			static string Nomes(Dictionary<string, SkillDef> d) =>
				string.Join(", ", d.Values.Where(s => s.Nome.Length > 0).Select(s => s.Path).Order(StringComparer.Ordinal));
			const string vivas = "/datum/skill/rank/VivaDeBancada, /datum/skill/tree/ArvoreDeBancada";

			string hoje = Nomes(DmSkillScanner.Scan(code));
			Checa("F1.1 extrator: so sai a skill e a arvore do arquivo INCLUIDO; as do `.dm` que o .dme nao cita ficam de fora",
				  hoje == vivas, hoje);
			Checa("F1.2 ...e o extrator dos degraus (`effector`) le a mesma lista de arquivos, sem tropecar",
				  DmEffectorScanner.Scan(code).Keys.All(k => !k.Contains("RaioDeBancada", StringComparison.Ordinal)));

			DmFonte.SemDmeDeTeste = true;
			try
			{
				string antes = Nomes(DmSkillScanner.Scan(code));
				Checa("(defeito injetado: o .dme deixa de mandar) a skill e a arvore do arquivo morto VOLTAM a sair -- a cobranca F1.1 acusa",
					  antes != vivas && antes.Contains("RaioDeBancada", StringComparison.Ordinal)
					  && antes.Contains("Mortas_De_Bancada", StringComparison.Ordinal), antes);
			}
			finally { DmFonte.SemDmeDeTeste = false; }
		}
		catch (Exception e) { Checa("F1.1 montei a arvore DM de mentira das skills", false, e.Message); }
		finally { DmFonte.SemDmeDeTeste = false; Apagar(raiz); }

		// ---- o arquivo publicado, pelo parser do jogo
		SkillCatalog cat = SkillCatalog.Parse(File.ReadAllText(Path.Combine(dados, "skills.json")),
											  File.ReadAllText(Path.Combine(dados, "skilltrees.json")));
		string[] tres = [Raio, Bola, ArvoreVelha];
		List<string> aindaLa = [.. tres.Where(p => cat.Get(p) != null)];
		Checa("F1.3 skills.json: `CustomAttacks/ki/beam/Beam`, `.../blast/Blast` e a arvore `Custom_Attacks` sairam",
			  aindaLa.Count == 0, string.Join(", ", aindaLa));
		List<string> quemCita = [.. cat.Todas.Where(s => s.Galhos.Concat(s.PreReqs).Any(p => p.Contains("CustomAttack", StringComparison.OrdinalIgnoreCase)))
											 .Select(s => s.Path)];
		Checa("F1.4 ...e nenhuma arvore nem pre-requisito cita um typepath de `CustomAttack`", quemCita.Count == 0, string.Join(", ", quemCita));

		string[] verbs = ["Create_Beam", "Remove_Beam", "Create_Blast", "Remove_Blast"];
		List<string> quemConcede = [.. cat.Todas.Where(s => s.Verbos.Intersect(verbs, StringComparer.OrdinalIgnoreCase).Any()).Select(s => s.Path)];
		List<string> naTabela = [.. verbs.Where(CensoDeSkills.PorOutroCanal.ContainsKey)];
		Checa("F1.5 os quatro verbs que so elas concediam nao tem mais quem os conceda, e sairam da tabela de canais do censo",
			  quemConcede.Count == 0 && naTabela.Count == 0, $"concedem: {string.Join(", ", quemConcede)}; na tabela: {string.Join(", ", naTabela)}");

		Checa("F1.6 as duas folhas estao entre as aposentadas (o livro de quem as tiver e limpo no login, com o marco de volta), a 1 marco cada",
			  SkillsAposentadas.Saiu(Raio) && SkillsAposentadas.Saiu(Bola)
			  && SkillsAposentadas.Todas.Count(a => (a.Path == Raio || a.Path == Bola) && a.Custo == 1) == 2);
		List<string> semDono = [.. tres.Where(p => !MarcoDoCatalogo.PerdasExplicadas.ContainsKey(p))];
		Checa("F1.7 ...e as tres sao perdas EXPLICADAS contra o marco congelado do catalogo", semDono.Count == 0, string.Join(", ", semDono));
		Console.WriteLine();
	}

	// =====================================================================
	// F2) CARGOS -- a concessao comentada
	// =====================================================================
	private static void Cargos(string dados)
	{
		Console.WriteLine("--- F2: cargos -- a concessao comentada nao e concessao (e a nota continua vindo do comentario) ---");

		(string raiz, string code) = Montar("cargos",
		[
			// `Modules/Ranks/RankAssign.dm`: quem limpa, quem nomeia, quem grava -- e o bloco `var` com a nota
			("Modules/Ranks/RankAssign.dm",
			[
				"mob/proc/CheckRank()",
				"\tif(King_Of_Acronia==signature) King_Of_Acronia=null",
				"mob/proc/Rank_Verb_Assign() //the //done checkmarks",
				"\tif(King_Of_Acronia==signature)//done",
				"\t\tRank=\"King Of Acronia\"",
				"proc/Save_Rank()",
				"\tvar/savefile/S=new(\"RANK\")",
				"\tS[\"KOA\"]<<King_Of_Acronia",
				"var",
				"\tKing_Of_Acronia //Rules Arconia.",
			]),
			// `Modules/Ranks/ordered/SpaceRanks.dm:30-33`: a linha do meio esta comentada no original
			("Modules/Ranks/ordered/SpaceRanks.dm",
			[
				"datum/skill/tree/Rank/Space",
				"\tname = \"Space Tree\"",
				"\tgrowbranches()",
				"\t\t..()",
				"\t\tswitch(savant.Rank)",
				"\t\t\tif(\"King Of Acronia\")",
				"\t\t\t\tenableskill(/datum/skill/rank/KillDriver)",
				"\t\t\t\t//enableskill(/datum/skill/rank/GuidedBall)",
				"\t\t\t\t/* enableskill(/datum/skill/rank/OutraMorta) */",
				"\t\t\t\tenableskill(/datum/skill/style/AlienStyle)",
			]),
			// uma consulta ao global com o comentario que explica -- e outra, morta
			("Modules/Aura.dm",
			[
				"mob/proc/aura_de_bancada()",
				"\tif(King_Of_Acronia == signature) // a coroa de Arconia brilha",
				"\t\treturn 1",
				"\t//if(King_Of_Acronia == key) // consulta morta",
			]),
		], includes: null);

		try
		{
			(string Skills, string NotaDm, string Notas) Extrair()
			{
				CargoDm? rei = DmRankScanner.Scan(code).Cargos.Find(c => c.Global == "King_Of_Acronia");
				return rei == null ? ("(sem o cargo)", "", "")
								   : (string.Join(", ", rei.Concede.Skills), rei.NotaDm, string.Join(" | ", rei.Concede.Notas));
			}
			const string vivas = "/datum/skill/rank/KillDriver, /datum/skill/style/AlienStyle";

			var hoje = Extrair();
			Checa("F2.1 extrator: o Rei de Arconia concede so as duas linhas VIVAS do `growbranches` -- nem a de `//`, nem a de `/* */`",
				  hoje.Skills == vivas, hoje.Skills);
			Checa("F2.2 ...e as NOTAS continuam saindo do comentario, de proposito: a do `var` (`nota_dm`) e a da consulta ao global",
				  hoje.NotaDm == "Rules Arconia." && hoje.Notas.Contains("a coroa de Arconia brilha", StringComparison.Ordinal)
				  && !hoje.Notas.Contains("consulta morta", StringComparison.Ordinal), $"nota_dm '{hoje.NotaDm}'; notas: {hoje.Notas}");

			DmRankScanner.ComentarioViraCodigoDeTeste = true;
			try
			{
				var antes = Extrair();
				Checa("(defeito injetado: comentario volta a ser codigo) o `GuidedBall` e a outra morta VOLTAM a ser concedidos, e a consulta morta vira nota -- as cobrancas F2.1 e F2.2 acusam",
					  antes.Skills != vivas && antes.Skills.Contains("/datum/skill/rank/GuidedBall", StringComparison.Ordinal)
					  && antes.Skills.Contains("OutraMorta", StringComparison.Ordinal)
					  && antes.Notas.Contains("consulta morta", StringComparison.Ordinal), $"{antes.Skills}; notas: {antes.Notas}");
			}
			finally { DmRankScanner.ComentarioViraCodigoDeTeste = false; }
		}
		catch (Exception e) { Checa("F2.1 montei a arvore DM de mentira dos cargos", false, e.Message); }
		finally { DmRankScanner.ComentarioViraCodigoDeTeste = false; Apagar(raiz); }

		// ---- o arquivo publicado, e a tabela do Core que entrega de verdade
		using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dados, "cargos.json")));
		JsonElement[] cargos = [.. doc.RootElement.GetProperty("cargos").EnumerateArray()];
		JsonElement[] reis = [.. cargos.Where(c => c.GetProperty("chave").GetString() == "kingofacronia")];
		string[] doJson = reis.Length == 1
			? [.. reis[0].GetProperty("concede").GetProperty("skills").EnumerateArray().Select(s => s.GetString() ?? "")]
			: [];
		Checa("F2.3 cargos.json: o Rei de Arconia concede `KillDriver` e `AlienStyle`, e nao o `GuidedBall` de `SpaceRanks.dm:32` (comentado)",
			  doJson.SequenceEqual(["/datum/skill/rank/KillDriver", "/datum/skill/style/AlienStyle"]), string.Join(", ", doJson));
		List<string> comGuided = [.. cargos.Where(c => c.GetProperty("concede").GetProperty("skills").EnumerateArray()
														 .Any(s => (s.GetString() ?? "").EndsWith("/GuidedBall", StringComparison.Ordinal)))
										   .Select(c => c.GetProperty("chave").GetString() ?? "")];
		Checa("F2.4 ...e nenhum outro cargo do arquivo a concede", comGuided.Count == 0, string.Join(", ", comGuided));
		Checa("F2.5 a tabela do Core que ENTREGA skill de cargo (`DadivaDeCargo`) diz o mesmo do Rei de Arconia (mais o canal que todo cargo tem) -- ela nunca deu o `GuidedBall`",
			  DadivaDeCargo.De("kingofacronia").Where(p => p != DadivaDeCargo.CanalDosCargos).SequenceEqual(doJson)
			  && !DadivaDeCargo.Todas.Any(p => p.EndsWith("/GuidedBall", StringComparison.Ordinal)),
			  string.Join(", ", DadivaDeCargo.De("kingofacronia")));
		Console.WriteLine();
	}

	// =====================================================================
	// F3) RACAS -- o prototipo de exemplo dentro de `/* */`
	// =====================================================================
	private static void Racas(string dados)
	{
		Console.WriteLine("--- F3: racas -- o prototipo de EXEMPLO dentro de `/* */` nao e raca ---");

		(string raiz, string code) = Montar("racas",
		[
			// `Modules/Races/Genetics/Genetic_Prototype.dm:65-…`: o exemplo comentado, e uma raca de verdade depois
			("Modules/Races/Genetics/Genetic_Prototype.dm",
			[
				"/datum/genetics/proto",
				"\tvar",
				"\t\tdescription",
				"",
				"/*Example prototype race.",
				"",
				"/datum/genetics/proto/Dog",
				"\tname = \"Dog\" //Name of race.",
				"\tm_stats = list(",
				"\t\t\"Physical Offense\" = 1,//stats",
				"\t\t\"Physical Defense\" = 1)",
				"\tmisc_stats = list(\"Zenkai\" = 1)",
				"*/",
				"",
				"/datum/genetics/proto/Viva",
				"\tname = \"Viva\"",
				"\tm_stats = list(\"Physical Offense\" = 2, \"Physical Defense\" = 3)",
				"\tmisc_stats = list(\"Zenkai\" = 1)",
			]),
		], includes: null);

		try
		{
			string Nomes() => string.Join(", ", DmProtoScanner.Scan(code).Select(p => p.Name).Order(StringComparer.Ordinal));

			string hoje = Nomes();
			Checa("F3.1 extrator: so sai a raca viva; a `Dog` do bloco `/*Example prototype race.` fica de fora", hoje == "Viva", hoje);

			DmFonte.BlocoNaoEComentarioDeTeste = true;
			try
			{
				string antes = Nomes();
				Checa("(defeito injetado: `/*` volta a ser texto) a `Dog` VOLTA a sair como raca -- a cobranca F3.1 acusa",
					  antes == "Dog, Viva", antes);
			}
			finally { DmFonte.BlocoNaoEComentarioDeTeste = false; }
		}
		catch (Exception e) { Checa("F3.1 montei a arvore DM de mentira das racas", false, e.Message); }
		finally { DmFonte.BlocoNaoEComentarioDeTeste = false; Apagar(raiz); }

		// ---- o arquivo publicado, pelo parser do jogo
		RaceCatalog racas = RaceCatalog.Parse(File.ReadAllText(Path.Combine(dados, "races.json")));
		Checa("F3.2 races.json: a `Dog` saiu, e ficaram as 23 racas que o original compila (da `Alien` a `Yardrat`)",
			  racas.Get("Dog") == null && racas.Protos.Count == 23 && racas.Get("Alien") != null && racas.Get("Human") != null
			  && racas.Get("Saiyan") != null && racas.Get("Yardrat") != null, $"{racas.Protos.Count} racas: {string.Join(", ", racas.Protos.Keys.Order())}");
		Console.WriteLine();
	}

	// =====================================================================
	// F4) ESTILOS -- o `Default` fica de fora (decisao pendente do dono)
	// =====================================================================
	/// <summary>
	/// O CASO INVERSO, E ELE NAO FOI MEXIDO. `/datum/style/Default` (`Style.dm:58`) e codigo vivo, e o extrator de
	/// estilos nao o emite porque a linha do tipo traz um `//`. O dono foi perguntado e nao mandou acrescentar
	/// (2026-10-11). Esta familia nao tem conserto nem botao de defeito: ela TRAVA o estado de hoje, pra que quem
	/// trocar o leitor de estilos (ou regenerar o `estilos.json`) nao ponha um nono estilo no jogo sem perguntar.
	/// </summary>
	private static void Estilos(string dados)
	{
		Console.WriteLine("--- F4: estilos -- o `Default` continua de fora (decisao pendente do dono) ---");

		(string raiz, string code) = Montar("estilos",
		[
			("Modules/CombatMechanics/Styles/Style.dm",
			[
				"/datum/style",
				"\tvar",
				"\t\tname=\"Basic Style\"",
				"",
				"/datum/style/Default //used when currentStyle = new/datum/style/Default is done.",
				"",
				"/datum/style/VivoDeBancada",
				"\tname = \"Vivo de Bancada\"",
				"\tdefaultphysoff = 2",
			]),
		], includes: null);

		try
		{
			string ids = string.Join(", ", DmStyleScanner.Scan(code).Select(e => e.Id).Order(StringComparer.Ordinal));
			Checa("F4.1 extrator: o estilo declarado normalmente sai, e o `Default` (linha do tipo com `//`) NAO -- pendente com o dono",
				  ids == "VivoDeBancada", ids);
		}
		catch (Exception e) { Checa("F4.1 montei a arvore DM de mentira dos estilos", false, e.Message); }
		finally { Apagar(raiz); }

		using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dados, "estilos.json")));
		string[] publicados = [.. doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString() ?? "")];
		Checa("F4.2 estilos.json: sao os oito estilos de sempre, sem o `Default`",
			  publicados.Length == 8 && !publicados.Contains("Default", StringComparer.OrdinalIgnoreCase), string.Join(", ", publicados));
	}
}
