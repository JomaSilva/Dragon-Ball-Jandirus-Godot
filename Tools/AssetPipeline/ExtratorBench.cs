using System.Text;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DO EXTRATOR DE SKILLS -- `dotnet run --project Tools/AssetPipeline -- extrator &lt;pastaCode&gt; [skills.json]`.
///
/// ============================ POR QUE ELA EXISTE ============================
/// O extrator ja perdeu efeito de skill DUAS vezes, e as duas do mesmo jeito: ele lia UMA forma de
/// declarar `after_learn()`, a outra caia no chao **calada**, e a skill saia do `skills.json` como se
/// o DM tambem nao fizesse nada. Da primeira vez foram 116 skills (o `after_learn` com o typepath na
/// propria linha); da segunda foram 3 (Mafuba, Open Dead Zone, Superior Seal), cujo corpo mora em
/// `Modules/Magic/Sealing.dm` -- 175 arquivos ANTES do arquivo onde os typepaths sao declarados.
///
/// Nas duas vezes o remedio foi ler mais uma forma. Nas duas vezes **ninguem pos uma bancada**, e e
/// por isso que houve a segunda. Esta e a bancada.
/// ============================================================
///
/// ============================ O QUE ELA MEDE, E COMO ELA SABE FICAR VERMELHA ============================
/// Ela nao pergunta "o extrator rodou": ela monta uma ARVORE DM SINTETICA com o defeito exato dentro
/// (o efeito num arquivo que vem ANTES do arquivo do dono) e exige que o verb chegue na skill. E
/// entao **liga o defeito de volta** (<see cref="DmSkillScanner.ComoAntesDoConserto"/>, o extrator de
/// antes do conserto) e exige que as MESMAS linhas fiquem vermelhas.
///
/// Uma bancada que so soubesse olhar pro estado de hoje nunca provaria que ela reprovaria -- e este
/// projeto ja teve bancada de PORTAO verde com o efeito quebrado do outro lado.
///
/// AS QUATRO FAMILIAS:
///   1. SINTETICA, ORDEM RUIM  -- o efeito antes do dono: o verb, o buff e a DELEGACAO tem que chegar.
///   2. SINTETICA, ORDEM BOA   -- o dono antes do efeito: o caminho que sempre funcionou. E o
///      controle que prova que o eixo medido e a ORDEM DE LEITURA, e nao "o extrator le after_learn".
///   3. O ALARME               -- um `after_learn` de dono INEXISTENTE tem que sair nomeado, e o
///      `/datum/skill/proc/after_learn()` do motor NAO pode virar falso positivo.
///   4. A ARVORE DE VERDADE    -- as tres skills de selo do DM do dono, e o artefato no disco.
/// ====================================================================================================
/// </summary>
public static class ExtratorBench
{
	private static int _ok, _falhou;

	private static void Conferir(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _ok++; Console.WriteLine($"  ok     {oque}"); return; }
		_falhou++;
		Console.WriteLine($"  FALHA  {oque}   {detalhe}");
	}

	/// <summary>
	/// A skill de teste que carrega o defeito. O nome e proposital: ela NAO existe no DM do dono, entao
	/// nenhuma medicao desta bancada pode passar por acidente de o jogo ja ter uma skill parecida.
	/// </summary>
	private const string SkillDoTeste = "/datum/skill/rank/SeloDeBancada";

	private const string SkillQueDelega = "/datum/skill/rank/DelegaDeBancada";
	private const string SkillSemDono = "/datum/skill/rank/DonoQueNaoExiste";
	private const string VerboDoTeste = "Selo_De_Bancada";

	/// <summary>
	/// O ARQUIVO DO EFEITO -- as tres formas que importam, num arquivo so.
	///
	/// A ULTIMA DECLARACAO E A DO MOTOR (`Skills Master/skill.dm:97`), e ela esta aqui de proposito:
	/// a `RxLearnSolto` casa com ela porque `proc` parece um segmento de typepath, e sem o filtro o
	/// diario dos orfaos nasceria com um falso alarme dentro. Diario com falso positivo e diario que
	/// ninguem le.
	/// </summary>
	private const string DmDoEfeito = """
		/datum/skill/rank/SeloDeBancada/after_learn()
			assignverb(/mob/keyable/verb/Selo_De_Bancada)
			savant.physoffBuff += 0.5

		/datum/skill/rank/DelegaDeBancada/after_learn()
			escolherDeBancada()

		/datum/skill/rank/DelegaDeBancada/proc/escolherDeBancada()
			savant.kioffBuff += 0.25

		/datum/skill/rank/DonoQueNaoExiste/after_learn()
			assignverb(/mob/keyable/verb/Verbo_Sem_Dono)

		/datum/skill/proc/after_learn()
			return

		""";

	/// <summary>Os typepaths. Sem `name` a skill nem entra no json -- e o `name` que a torna real.</summary>
	private const string DmDosDonos = """
		/datum/skill/rank/SeloDeBancada
			name = "Selo de Bancada"
			desc = "so existe nesta bancada"

		/datum/skill/rank/DelegaDeBancada
			name = "Delega de Bancada"

		""";

	public static int Run(string pastaCode, string? artefato)
	{
		Console.WriteLine("=== O EXTRATOR DE SKILLS: o after_learn que caia no chao ===\n");
		_ok = _falhou = 0;

		string raiz = Path.Combine(Path.GetTempPath(), "jandirus-bancada-extrator");
		try
		{
			ASinteticaComOrdemRuim(raiz);
			ASinteticaComOrdemBoa(raiz);
			OAlarme(raiz);
			AArvoreDeVerdade(pastaCode, artefato);
		}
		catch (Exception e)
		{
			_falhou++;
			Console.WriteLine($"  FALHA  a bancada rodou inteira   {e}");
		}
		finally
		{
			DmSkillScanner.ComoAntesDoConserto = false;
			if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
		}

		Console.WriteLine($"\n=== EXTRATOR: {_ok} OK, {_falhou} FALHA ===");
		return _falhou;
	}

	// =====================================================================
	// A ARVORE SINTETICA
	// =====================================================================
	/// <summary>
	/// Escreve a arvore de teste e devolve a pasta. Os nomes de arquivo mandam na ORDEM em que o
	/// `Directory.GetFiles` os entrega, e a ordem e o objeto do teste inteiro.
	/// </summary>
	private static string Montar(string raiz, string sufixo, string nomeDoEfeito, string nomeDosDonos)
	{
		string pasta = Path.Combine(raiz, sufixo);
		if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true);
		Directory.CreateDirectory(pasta);
		File.WriteAllText(Path.Combine(pasta, nomeDoEfeito), DmDoEfeito, new UTF8Encoding(false));
		File.WriteAllText(Path.Combine(pasta, nomeDosDonos), DmDosDonos, new UTF8Encoding(false));
		return pasta;
	}

	/// <summary>
	/// A ORDEM DE LEITURA E DE VERDADE A QUE EU QUIS?
	///
	/// Sem esta pergunta a familia 1 poderia passar por um motivo que nao tem nada a ver com o
	/// conserto: se o sistema de arquivos devolvesse o dono primeiro, o corpo seria aplicado no
	/// caminho direto e o adiamento nunca seria exercitado -- verde, e medindo outra coisa.
	/// </summary>
	private static void ConferirAOrdem(string pasta, string primeiroEsperado)
	{
		string[] arqs = Directory.GetFiles(pasta, "*.dm", SearchOption.AllDirectories);
		Conferir($"a leitura comeca por `{primeiroEsperado}` (a ordem E o objeto deste teste)",
				 arqs.Length == 2 && Path.GetFileName(arqs[0]) == primeiroEsperado,
				 string.Join(", ", arqs.Select(Path.GetFileName)));
	}

	// =====================================================================
	// 1) O EFEITO CHEGA ANTES DO DONO -- o defeito exato do selo
	// =====================================================================
	private static void ASinteticaComOrdemRuim(string raiz)
	{
		Console.WriteLine("-- 1) ARVORE SINTETICA, ORDEM RUIM: o efeito num arquivo ANTES do dono --");

		string pasta = Montar(raiz, "ordem-ruim", "aaa_efeito.dm", "zzz_donos.dm");
		ConferirAOrdem(pasta, "aaa_efeito.dm");

		DmSkillScanner.ComoAntesDoConserto = false;
		Dictionary<string, SkillDef> hoje = DmSkillScanner.Scan(pasta);

		Conferir("a skill existe no catalogo", hoje.ContainsKey(SkillDoTeste));
		Conferir("o VERB do `after_learn` chegou na skill (o corpo adiado foi resolvido)",
				 Verbos(hoje, SkillDoTeste).Contains(VerboDoTeste),
				 string.Join(",", Verbos(hoje, SkillDoTeste)));
		Conferir("...e o BUFF da mesma linha tambem (nao e so o verb que se perdia)",
				 Perto(Buff(hoje, SkillDoTeste, "physoffBuff"), 0.5),
				 $"{Buff(hoje, SkillDoTeste, "physoffBuff")}");

		// A DELEGACAO ADIADA -- e a razao de os dois lacos de resolucao terem a ordem que tem.
		Conferir("o `after_learn` adiado que DELEGA pra um proc proprio tambem resolve "
				 + "(por isso o laco dos adiados vem ANTES do laco dos chamados)",
				 Perto(Buff(hoje, SkillQueDelega, "kioffBuff"), 0.25),
				 $"{Buff(hoje, SkillQueDelega, "kioffBuff")}");

		// ============================ O CONTROLE NEGATIVO ============================
		// Aqui a bancada liga o extrator de ANTES do conserto e exige que as tres linhas acima virem
		// as tres linhas de baixo. E o que separa "esta verde" de "sabe ficar vermelha".
		// ============================================================================
		DmSkillScanner.ComoAntesDoConserto = true;
		Dictionary<string, SkillDef> antes = DmSkillScanner.Scan(pasta);
		DmSkillScanner.ComoAntesDoConserto = false;

		Conferir("CONTROLE NEGATIVO: com a ordem de leitura ANTIGA o verb SOME "
				 + "(e esta a linha que fica vermelha se o conserto for desfeito)",
				 !Verbos(antes, SkillDoTeste).Contains(VerboDoTeste),
				 string.Join(",", Verbos(antes, SkillDoTeste)));
		Conferir("...e o buff some junto", Buff(antes, SkillDoTeste, "physoffBuff") == 0);
		Conferir("...e a delegacao some junto", Buff(antes, SkillQueDelega, "kioffBuff") == 0);
		Conferir("...e o extrator antigo nao dizia UMA PALAVRA sobre isso (era ausencia, nao erro)",
				 DmSkillScanner.AprendizadosSemDono.Count == 0,
				 string.Join(", ", DmSkillScanner.AprendizadosSemDono.Select(x => x.Skill)));
	}

	// =====================================================================
	// 2) O DONO CHEGA ANTES -- o caminho que sempre funcionou
	// =====================================================================
	/// <summary>
	/// O CONTROLE QUE IMPEDE A CONCLUSAO ERRADA. Sem ele, a familia 1 provaria "o extrator novo le e o
	/// velho nao" -- e alguem poderia concluir que o velho nao lia `after_learn` NENHUM, que e falso e
	/// levaria o proximo conserto pro lugar errado. O que muda entre os dois nao e a leitura: e a
	/// ORDEM em que o dono aparece.
	/// </summary>
	private static void ASinteticaComOrdemBoa(string raiz)
	{
		Console.WriteLine("\n-- 2) ARVORE SINTETICA, ORDEM BOA: o dono ANTES do efeito --");

		string pasta = Montar(raiz, "ordem-boa", "zzz_efeito.dm", "aaa_donos.dm");
		ConferirAOrdem(pasta, "aaa_donos.dm");

		foreach (bool antigo in new[] { false, true })
		{
			DmSkillScanner.ComoAntesDoConserto = antigo;
			Dictionary<string, SkillDef> d = DmSkillScanner.Scan(pasta);
			Conferir($"com o dono lido primeiro, o verb chega {(antigo ? "TAMBEM no extrator ANTIGO" : "no extrator de hoje")}",
					 Verbos(d, SkillDoTeste).Contains(VerboDoTeste),
					 string.Join(",", Verbos(d, SkillDoTeste)));
		}
		DmSkillScanner.ComoAntesDoConserto = false;
	}

	// =====================================================================
	// 3) O ALARME
	// =====================================================================
	private static void OAlarme(string raiz)
	{
		Console.WriteLine("\n-- 3) O ALARME: quem nao coube em skill nenhuma sai NOMEADO --");

		string pasta = Montar(raiz, "alarme", "aaa_efeito.dm", "zzz_donos.dm");
		DmSkillScanner.ComoAntesDoConserto = false;
		DmSkillScanner.Scan(pasta);

		var semDono = DmSkillScanner.AprendizadosSemDono;
		Conferir("o `after_learn` de typepath que NAO EXISTE sai no alarme, e so ele",
				 semDono.Count == 1 && semDono[0].Skill.Equals(SkillSemDono, StringComparison.OrdinalIgnoreCase),
				 string.Join(", ", semDono.Select(x => $"{x.Skill} {x.Onde}")));
		// ARQUIVO E LINHA, e a LINHA CERTA -- conferida contra o proprio arquivo em vez de um numero
		// digitado aqui. Um alarme que aponta pra linha errada e pior que um alarme sem linha: manda
		// quem for consertar procurar no lugar errado.
		string[] linhas = File.ReadAllLines(Path.Combine(pasta, "aaa_efeito.dm"));
		int esperada = Array.FindIndex(
			linhas, l => l.Contains("DonoQueNaoExiste/after_learn", StringComparison.Ordinal)) + 1;
		Conferir($"...apontando o arquivo e a LINHA de verdade (aaa_efeito.dm:{esperada})",
				 semDono.Count == 1 && semDono[0].Onde == $"aaa_efeito.dm:{esperada}",
				 semDono.Count == 1 ? semDono[0].Onde : "");

		// O FALSO POSITIVO QUE O FILTRO MATA -- `/datum/skill/proc/after_learn()` e a declaracao BASE
		// do proc no motor do DM (`Skills Master/skill.dm:97`), nao uma skill chamada "proc".
		Conferir("a declaracao base `/datum/skill/proc/after_learn()` NAO vira alarme",
				 !semDono.Exists(x => x.Skill.Contains("/proc", StringComparison.OrdinalIgnoreCase)));
		Conferir("...e nao vira skill",
				 !DmSkillScanner.Scan(pasta).Keys.Any(k => k.EndsWith("/proc", StringComparison.OrdinalIgnoreCase)));
	}

	// =====================================================================
	// 4) A ARVORE DE VERDADE
	// =====================================================================
	/// <summary>
	/// AS TRES SKILLS DO SELO, no DM do dono -- e no artefato que o jogo LE.
	///
	/// As duas pontas, e nao uma: o extrator pode estar certo e o `Assets/Data/skills.json` no disco
	/// ser de antes do conserto. O jogo nao roda o extrator; ele le o arquivo.
	/// </summary>
	private static void AArvoreDeVerdade(string pastaCode, string? artefato)
	{
		Console.WriteLine("\n-- 4) A ARVORE DE VERDADE: as tres skills de selo do DM --");

		if (!Directory.Exists(pastaCode))
		{
			Conferir($"a pasta do DM existe ({pastaCode})", false);
			return;
		}

		(string Path, string Verbo)[] tres =
		[
			("/datum/skill/rank/Mafuba", "Mafuba"),
			("/datum/skill/rank/DeadZone", "Open_Dead_Zone"),
			("/datum/skill/rank/SuperiorSeal", "Seal_Mob"),
		];

		DmSkillScanner.ComoAntesDoConserto = false;
		Dictionary<string, SkillDef> hoje = DmSkillScanner.Scan(pastaCode);
		int comNome = hoje.Values.Count(s => s.Nome.Length > 0);

		Conferir($"o DM inteiro foi lido ({comNome} entradas com nome)", comNome > 300, $"{comNome}");
		Conferir("NENHUM `after_learn` de caminho absoluto ficou sem dono -- o alarme esta calado "
				 + "porque nao ha o que dizer",
				 DmSkillScanner.AprendizadosSemDono.Count == 0,
				 string.Join(", ", DmSkillScanner.AprendizadosSemDono.Select(x => $"{x.Skill} {x.Onde}")));

		foreach ((string path, string verbo) in tres)
			Conferir($"`{path.Split('/')[^1]}` concede `{verbo}` (o after_learn mora em Magic/Sealing.dm, "
					 + "175 arquivos antes do typepath)",
					 Verbos(hoje, path).Contains(verbo), string.Join(",", Verbos(hoje, path)));

		// O CONTROLE NEGATIVO NA ARVORE DE VERDADE: e aqui que se ve o tamanho do buraco original.
		DmSkillScanner.ComoAntesDoConserto = true;
		Dictionary<string, SkillDef> antes = DmSkillScanner.Scan(pastaCode);
		DmSkillScanner.ComoAntesDoConserto = false;

		var perdidos = tres.Where(t => !Verbos(antes, t.Path).Contains(t.Verbo)).Select(t => t.Verbo).ToList();
		Conferir("CONTROLE NEGATIVO: com a ordem de leitura ANTIGA as TRES perdem o verb "
				 + "(era este o estado em que o painel do cargo anunciava o Mafuba)",
				 perdidos.Count == 3, $"perderam: {string.Join(", ", perdidos)}");

		Conferir("...e o catalogo continuava do mesmo TAMANHO, o que e o pior detalhe: "
				 + "nada no relatorio mudava",
				 antes.Values.Count(s => s.Nome.Length > 0) == comNome);

		if (artefato == null || !File.Exists(artefato))
		{
			Console.WriteLine("  (sem `skills.json` na linha de comando -- o artefato nao foi conferido)");
			return;
		}

		// O ARTEFATO NO DISCO -- o que o jogo carrega de verdade.
		string json = File.ReadAllText(artefato);
		var cat = Jandirus.Core.Skills.SkillCatalog.Parse(json, "{}");
		foreach ((string path, string verbo) in tres)
		{
			string[] vs = cat.Get(path)?.Verbos ?? [];
			Conferir($"e o `skills.json` NO DISCO ja tem `{verbo}` -- o jogo nao roda o extrator, "
					 + "ele le o arquivo",
					 vs.Contains(verbo, StringComparer.OrdinalIgnoreCase), string.Join(",", vs));
		}
	}

	// =====================================================================
	private static List<string> Verbos(Dictionary<string, SkillDef> d, string path) =>
		d.TryGetValue(path, out SkillDef? s) ? s.Verbos : [];

	private static double Buff(Dictionary<string, SkillDef> d, string path, string campo) =>
		d.TryGetValue(path, out SkillDef? s) ? s.Buffs.GetValueOrDefault(campo) : 0;

	private static bool Perto(double a, double b) => Math.Abs(a - b) < 1e-9;
}
