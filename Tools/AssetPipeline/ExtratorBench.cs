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
			OsGalhos(raiz);
			OQueOExtratorNaoPegava(raiz);
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
	// 5) OS GALHOS: a continuacao por parentese, a falsa arvore e o growbranches como dado
	// =====================================================================
	/// <summary>
	/// UMA ARVORE SINTETICA COM OS TRES DEFEITOS DA RODADA DAS ARVORES:
	///   * a lista de galhos continua na linha seguinte SEM barra (so o `(` aberto) -- o extrator so
	///     lia a barra e perdia 13 galhos em tres arvores do DM;
	///   * um typepath `/datum/skill/Tree_Mastery_Bancada` casa com o prefixo `/datum/skill/tree` e
	///     virava arvore;
	///   * o `growbranches()` com tier por investimento, porta por contador atras de uma flag de
	///     "algo mudou", e um `switch(savant.Rank)` -- tudo isso tem que sair como REGRA.
	/// </summary>
	private const string DmDosGalhos = """
		/datum/skill/tree/GalhoDeBancada
			name = "Galho de Bancada"
			maxtier = 3
			allowedtier = 1
			constituentskills = list(new/datum/skill/rank/SeloDeBancada,
				new/datum/skill/rank/DelegaDeBancada)
			growbranches()
				if(invested>=2)allowedtier = 2
				if(savant.didbodychange)
					savant.didbodychange=0
					if(savant.bodyskill>2)
						enabletree(/datum/skill/tree/Bodybuilding)
				switch(savant.Rank)
					if("Turtle")
						enableskill(/datum/skill/rank/SeloDeBancada)
				..()
			prunebranches()
				if(invested<2)allowedtier = 1

		/datum/skill/Tree_Mastery_Bancada
			name = "Falsa arvore de bancada"
			tier = 2

		""";

	private static void OsGalhos(string raiz)
	{
		Console.WriteLine("\n-- 5) OS GALHOS: continuacao por parentese, a falsa arvore, o growbranches como dado --");

		string pasta = Montar(raiz, "galhos", "aaa_efeito.dm", "zzz_donos.dm");
		File.WriteAllText(Path.Combine(pasta, "mmm_galhos.dm"), DmDosGalhos, new UTF8Encoding(false));
		DmSkillScanner.ComoAntesDoConserto = false;
		Dictionary<string, SkillDef> d = DmSkillScanner.Scan(pasta);

		SkillDef? arv = d.GetValueOrDefault("/datum/skill/tree/GalhoDeBancada");
		Conferir("a arvore sintetica existe e e arvore", arv is { Arvore: true });
		if (arv == null) return;
		Conferir("os DOIS galhos chegaram -- a segunda linha da lista nao tem barra, so o `(` aberto",
				 arv.Constituintes.Count == 2, string.Join(",", arv.Constituintes));
		Conferir("`/datum/skill/Tree_Mastery_Bancada` NAO e arvore (o prefixo `/datum/skill/tree` sem a barra casava)",
				 d.GetValueOrDefault("/datum/skill/Tree_Mastery_Bancada") is { Arvore: false });
		Conferir("allowedtier 1 / maxtier 3 lidos", arv is { TierInicial: 1, TierMax: 3 }, $"{arv.TierInicial}/{arv.TierMax}");
		Conferir("`if(invested>=2)allowedtier = 2` virou `tier;2;invested>=2`",
				 arv.Regras.Contains("tier;2;invested>=2"), string.Join(" | ", arv.Regras));
		Conferir("a porta por contador atras do `didbodychange` saiu SEM a flag (ela e 'algo mudou', e o port reavalia sempre)",
				 arv.Regras.Contains("arvore;/datum/skill/tree/Bodybuilding;bodyskill>2"), string.Join(" | ", arv.Regras));
		Conferir("`switch(savant.Rank) if(\"Turtle\")` virou `acende;...;Rank=='Turtle'` (aspas SIMPLES, pro leitor de json)",
				 arv.Regras.Contains("acende;/datum/skill/rank/SeloDeBancada;Rank=='Turtle'"), string.Join(" | ", arv.Regras));
		Conferir("o prunebranches entra DEPOIS do growbranches (a ordem do testunlocks)",
				 arv.Regras.Count == 4 && arv.Regras[^1] == "tier;1;invested<2", string.Join(" | ", arv.Regras));
		Conferir("`..()` e o `savant.didbodychange=0` nao viram diario (sao ruido do DM, nao efeito)",
				 !DmGalhosScanner.NaoLidas.Any(x => x.Arvore.Contains("GalhoDeBancada")),
				 string.Join(" | ", DmGalhosScanner.NaoLidas.Where(x => x.Arvore.Contains("GalhoDeBancada")).Select(x => x.Linha)));
	}

	// =====================================================================
	// 6) O QUE O EXTRATOR NAO PEGAVA: a escolha na 2a forma, a que se herda, o ganho com expressao, o treegrow
	// =====================================================================
	/// <summary>
	/// UMA ARVORE SINTETICA COM OS QUATRO BURACOS DA RODADA DOS DEGRAUS:
	///   * `switch(input(...) in list(...))` DIRETO no `after_learn` (TheHolyTrinity, Bodybuilding.dm:119)
	///     -- so a forma delegada (`choose()`) era lida como escolha; a direta era SOMADA;
	///   * `switch(savant.<var>)` (Grace, :243) -- a skill que entra na casa que outra escolheu;
	///   * o ganho com expressao por um local do datum (`storedBP = max(1,savant.BP*0.01)` +
	///     `savant.BP+=storedBP`, :89-90) -- o extrator so lia constante;
	///   * o `treegrow()`/`treeshrink()` das raciais (arlian.dm:12-20), que ninguem traduzia em regra.
	/// E um CONTROLE: `switch(level)` no after_learn (kaioken.dm:88) NAO e escolha e continua somando.
	/// </summary>
	private const string DmDosBuracos = """
		/datum/skill/rank/TrindadeDeBancada
			name = "Trindade de Bancada"
			after_learn()
				to_chat(savant, "escolha")
				switch(input(savant,"Qual casa?","Trindade","Casa A") in list("Casa A","Casa B"))
					if("Casa A")
						to_chat(savant, "A")
						savant.physoffBuff += 0.3
						savant.genome.add_to_stat("Lifespan",0.1)
					if("Casa B")
						savant.physdefBuff += 0.3

		/datum/skill/rank/GracaDeBancada
			name = "Graca de Bancada"
			after_learn()
				switch(savant.trindadetipo)
					if("Casa A")
						savant.willpowerMod += 0.05
					if("Casa B")
						savant.willpowerMod += 0.1

		/datum/skill/rank/CemDeBancada
			name = "Cem de Bancada"
			var/storedBP
			var/hiddenpot
			after_learn()
				storedBP = max(1,savant.BP*0.01)
				savant.BP+=storedBP
				hiddenpot = (savant.relBPmax*2)
				savant.hiddenpotential += hiddenpot
				savant.staminagainMod += 0.1

		/datum/skill/rank/KaioDeBancada
			name = "Kaio de Bancada"
			after_learn()
				switch(level)
					if(0) to_chat(savant, "zero")
					if(1)
						assignverb(/verb/Kaioken_De_Bancada)
						savant.KaiokenMastery+=3

		/datum/skill/tree/RacialDeBancada
			name = "Racial de Bancada"
			maxtier = 2
			allowedtier = 2
			constituentskills = list(new/datum/skill/rank/TrindadeDeBancada,new/datum/skill/rank/GracaDeBancada)
			treegrow()
				if(savant.pitted==1)
					disableskill(/datum/skill/rank/GracaDeBancada)
			treeshrink()
				if(savant.pitted==0)
					enableskill(/datum/skill/rank/GracaDeBancada)

		""";

	private static void OQueOExtratorNaoPegava(string raiz)
	{
		Console.WriteLine("\n-- 6) O QUE O EXTRATOR NAO PEGAVA: escolha na 2a forma, escolha herdada, ganho com expressao, treegrow --");

		string pasta = Montar(raiz, "buracos", "aaa_efeito.dm", "zzz_donos.dm");
		File.WriteAllText(Path.Combine(pasta, "mmm_buracos.dm"), DmDosBuracos, new UTF8Encoding(false));
		DmSkillScanner.ComoAntesDoConserto = false;
		Dictionary<string, SkillDef> d = DmSkillScanner.Scan(pasta);

		SkillDef? tr = d.GetValueOrDefault("/datum/skill/rank/TrindadeDeBancada");
		Conferir("a Trindade sintetica saiu com DUAS casas e NENHUM buff somado",
				 tr is { Escolhas.Count: 2, Buffs.Count: 0, Genes.Count: 0 }, $"{tr?.Escolhas.Count} casas / {tr?.Buffs.Count} buffs");
		// guardado por `Count == 2` de proposito: com a deteccao da escolha desligada (o defeito que
		// esta familia guarda) a linha fica VERMELHA, e nao derruba a bancada inteira
		Conferir("...a casa A tem physoffBuff 0,3 e o gene Lifespan; a B tem physdefBuff 0,3",
				 tr != null && tr.Escolhas.Count == 2
				 && tr.Escolhas[0].Rotulo == "Casa A" && Perto(tr.Escolhas[0].Buffs.GetValueOrDefault("physoffBuff"), 0.3)
				 && Perto(tr.Escolhas[0].Genes.GetValueOrDefault("Lifespan"), 0.1)
				 && tr.Escolhas[1].Rotulo == "Casa B" && Perto(tr.Escolhas[1].Buffs.GetValueOrDefault("physdefBuff"), 0.3));

		SkillDef? gr = d.GetValueOrDefault("/datum/skill/rank/GracaDeBancada");
		Conferir("a Graca sintetica (`switch(savant.trindadetipo)`) tem duas casas e SEGUE a Trindade (casas de mesmos rotulos)",
				 gr is { Escolhas.Count: 2, Buffs.Count: 0 } && gr.EscolhaSegue == "/datum/skill/rank/TrindadeDeBancada" && gr.EscolhaPorVar == "trindadetipo",
				 $"{gr?.Escolhas.Count} casas, segue '{gr?.EscolhaSegue}', var '{gr?.EscolhaPorVar}'");
		Conferir("...e ninguem ficou sem lider no diario", DmSkillScanner.EscolhasSemLider.Count == 0,
				 string.Join(", ", DmSkillScanner.EscolhasSemLider));

		SkillDef? cem = d.GetValueOrDefault("/datum/skill/rank/CemDeBancada");
		Conferir("o ganho com expressao saiu como dado: `BP+=(max(1,BP*0.01))` e `hiddenpotential+=((relBPmax*2))` (o local substituido)",
				 cem != null && cem.Compra.Count == 2 && cem.Compra[0] == "BP+=(max(1,BP*0.01))" && cem.Compra[1] == "hiddenpotential+=((relBPmax*2))",
				 string.Join(" ; ", cem?.Compra ?? []));
		Conferir("...o buff CONSTANTE da mesma skill continua no canal de sempre (staminagainMod 0,1)",
				 cem != null && Perto(cem.Buffs.GetValueOrDefault("staminagainMod"), 0.1));
		Conferir("...e o Core LE as duas expressoes (a validacao e o parser de producao)",
				 cem != null && cem.Compra.All(c => Jandirus.Core.Skills.GanhoNaCompra.Parse(c) != null)
				 && DmSkillScanner.ComprasNaoLidas.Count == 0, string.Join(" | ", DmSkillScanner.ComprasNaoLidas));

		SkillDef? kaio = d.GetValueOrDefault("/datum/skill/rank/KaioDeBancada");
		Conferir("CONTROLE: `switch(level)` no after_learn NAO vira escolha -- o verb e o +3 somam como antes",
				 kaio is { Escolhas.Count: 0 } && kaio.Verbos.Contains("Kaioken_De_Bancada") && Perto(kaio.Buffs.GetValueOrDefault("KaiokenMastery"), 3),
				 $"{kaio?.Escolhas.Count} casas, verbos {string.Join(",", kaio?.Verbos ?? [])}");

		SkillDef? arv = d.GetValueOrDefault("/datum/skill/tree/RacialDeBancada");
		Conferir("o `treegrow()` virou regra: `apaga;/datum/skill/rank/GracaDeBancada;pitted==1`",
				 arv != null && arv.Regras.Contains("apaga;/datum/skill/rank/GracaDeBancada;pitted==1"), string.Join(" | ", arv?.Regras ?? []));
		Conferir("...e o `treeshrink()` DEPOIS dele: `acende;...;pitted==0`",
				 arv != null && arv.Regras.Count == 2 && arv.Regras[^1] == "acende;/datum/skill/rank/GracaDeBancada;pitted==0", string.Join(" | ", arv?.Regras ?? []));
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

		// OS QUATRO BURACOS DA RODADA DOS DEGRAUS, no DM do dono
		SkillDef? trin = hoje.GetValueOrDefault("/datum/skill/Bodybuilding/TheHolyTrinity");
		Conferir("a Holy Trinity (Bodybuilding.dm:119) sai com 3 casas e SEM os buffs somados (antes: physdefBuff 0,6)",
				 trin is { Escolhas.Count: 3, Buffs.Count: 0 }, $"{trin?.Escolhas.Count} casas / {trin?.Buffs.Count} buffs");
		Conferir("a Grace (Bodybuilding.dm:243) segue a Holy Trinity",
				 hoje.GetValueOrDefault("/datum/skill/Bodybuilding/Grace")?.EscolhaSegue == "/datum/skill/Bodybuilding/TheHolyTrinity");
		Conferir("a One Hundred (Bodybuilding.dm:89-92) traz os dois ganhos com expressao, e as tres do Bodybuilding sao as UNICAS",
				 hoje.GetValueOrDefault("/datum/skill/Bodybuilding/One_Hundred")?.Compra.Count == 2
				 && hoje.Values.Count(s => s.Compra.Count > 0) == 3,
				 string.Join(", ", hoje.Values.Where(s => s.Compra.Count > 0).Select(s => s.Nome)));
		Conferir("a arvore Arlian (arlian.dm:12-20) traz o `treegrow` como `apaga;Supa;pitted==1`",
				 hoje.GetValueOrDefault("/datum/skill/tree/arlian")?.Regras.Contains("apaga;/datum/skill/arlian/Supa;pitted==1") == true);

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
		Conferir("e o `skills.json` NO DISCO ja tem as casas da Trinity, a Grace seguindo, a compra da One Hundred e o pitted do Arlian",
				 cat.Get("/datum/skill/Bodybuilding/TheHolyTrinity") is { Escolhas.Length: 3, Buffs.Count: 0 }
				 && cat.Get("/datum/skill/Bodybuilding/Grace")?.EscolhaSegue == "/datum/skill/Bodybuilding/TheHolyTrinity"
				 && cat.Get("/datum/skill/Bodybuilding/One_Hundred")?.Compra.Length == 2
				 && cat.Get("/datum/skill/tree/arlian")?.Regras.Contains("apaga;/datum/skill/arlian/Supa;pitted==1") == true);
	}

	// =====================================================================
	private static List<string> Verbos(Dictionary<string, SkillDef> d, string path) =>
		d.TryGetValue(path, out SkillDef? s) ? s.Verbos : [];

	private static double Buff(Dictionary<string, SkillDef> d, string path, string campo) =>
		d.TryGetValue(path, out SkillDef? s) ? s.Buffs.GetValueOrDefault(campo) : 0;

	private static bool Perto(double a, double b) => Math.Abs(a - b) < 1e-9;
}
