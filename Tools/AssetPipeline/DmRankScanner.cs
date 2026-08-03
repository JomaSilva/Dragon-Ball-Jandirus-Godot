using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>Um requisito de cargo, como o DM o escreve -- e o que o jogador le quando falha.</summary>
public sealed class RequisitoDm
{
	/// <summary>"karma" | "bp" | "raca" | "classe" | "reputacao:Earth" | "mortes" | "zenni" | "godki" | "cargo" | "?" </summary>
	public string Campo = "?";

	/// <summary>">=", "&lt;=", "e", "desperto"... o sentido do que se exige, ja INVERTIDO do DM (ver Classificar).</summary>
	public string Op = "";

	public double Valor;
	public List<string> Valores = [];

	/// <summary>
	/// Requisitos que sairam da MESMA linha do DM. Junto com <see cref="Alternativa"/> e o que
	/// diz se eles se somam ou se substituem.
	/// </summary>
	public int Grupo;

	/// <summary>
	/// O grupo e ALTERNATIVO: basta cumprir um. Sai de ler o operador de cima da condicao -- o
	/// `if` do DM testa a FALHA, entao `if(A &amp;&amp; B)` so falha com os dois, ou seja, exige A ou B;
	/// `if(A || B)` falha com qualquer um, ou seja, exige os dois.
	///
	/// LIMITE CONHECIDO, e ele e real: a lista de alternativas sai ACHATADA. No `kaioshin` a
	/// alternativa de verdade e "ser Grand Kai OU (sangue Kai E maestria 33%)", e aqui vira
	/// "Grand Kai OU sangue Kai OU maestria 33%" -- mais frouxo que o original. Quem for portar
	/// o requisito le o `bruto`/`texto` e escreve a mao; e por isso que os dois vem no JSON.
	/// </summary>
	public bool Alternativa;

	/// <summary>A frase EXATA que o DM devolve. E a fonte da verdade do que o jogador le.</summary>
	public string Texto = "";

	/// <summary>A condicao crua do DM, pra quem for conferir a mao.</summary>
	public string Bruto = "";

	public string Fonte = "";
}

/// <summary>O que o cargo CONCEDE: arvore de skills, skills destravadas, verbs, e o resto.</summary>
public sealed class ConcessaoDm
{
	public string Arvore = "";          // "Earth" | "Otherworld" | "Space" | "Namek" | "KaioshinApprentice"
	public List<string> Skills = [];    // typepaths destravados no growbranches
	public List<string> Verbs = [];     // verbs empurrados na mao (GoD)
	public List<string> Notas = [];     // "arquivo.dm:linha -- o comentario que estava la"
}

/// <summary>Um cargo do mundo, montado a partir das SEIS fontes espalhadas pelo DM.</summary>
public sealed class CargoDm
{
	public string Global = "";      // a var global que guarda a signature do dono: "Angel_Rank"
	public string Chave = "";       // a key do RankQuests quando existe; senao um slug NOSSO
	public bool ChaveInventada;     // true = o DM nao tem key pra este cargo (ver Slug)

	public string Nome = "";        // o melhor nome exibido que se achou
	public string NomeQuest = "";   // rq_display()
	public string NomePainel = "";  // o painel HTML do verb Ranks
	public string NomeRank = "";    // a string que vai em mob.Rank (e o que a RankTree casa)

	public string NotaDm = "";      // o comentario ao lado da declaracao do var
	public string Fonte = "";       // arquivo.dm:linha da declaracao

	public bool Unico = true;       // um global = uma signature = UM dono no mundo
	public bool TemQuest;           // RQ_ALL: recebe tarefas com prazo e pode ser destituido
	public bool Reivindicavel;      // RQ_CLAIMABLE: da pra tomar pelo verb + prova do Espirito
	public bool DoCeu;              // RQ_SKY: pode reivindicar/servir MORTO
	public bool Sabedoria;          // RQ_WISDOM: tarefa de SERVICO (presenca) em vez de PODER
	public bool Maligno;            // RQ_EVIL: cumprir tarefa NAO paga karma bom

	public List<RequisitoDm> Requisitos = [];
	public List<string> Degraus = [];       // cargos (chaves) que abrem ESTE por ascensao
	public ConcessaoDm Concede = new();

	/// <summary>Como se ganha, quando nao e pelo verb de reivindicar. Sai do proprio codigo.</summary>
	public List<string> Aquisicao = [];

	public bool SemRequisitoNoCodigo => Requisitos.Count == 0;
}

public sealed class VarreduraDeCargos
{
	public List<CargoDm> Cargos = [];
	public List<string> Avisos = [];
	public Dictionary<string, double> Defines = new(StringComparer.Ordinal);
}

/// <summary>
/// Le OS CARGOS DO MUNDO direto do DM.
///
/// POR QUE UM SCANNER PARA VINTE E CINCO LINHAS DE TABELA: porque a tabela nao existe. No BYOND um
/// cargo nao e um registro -- e uma VARIAVEL GLOBAL solta que guarda a signature do dono, e tudo
/// que se sabe sobre ela esta espalhado por seis lugares que nunca concordam entre si:
///
///   1. `RankAssign.dm`, bloco `var`      -- a lista de globais + o comentario que diz o que o cargo faz
///   2. `RankAssign.dm`, `Rank_Verb_Assign` -- global -> a string que vai em `mob.Rank`
///   3. `RankAssign.dm`, o verb `Ranks`   -- global -> o nome que o JOGADOR le no painel
///   4. `RankQuests.dm`, `rq_get_sig`/`rq_display`/`rq_requirements` -- key -> global, nome e REQUISITOS
///   5. `ordered/*.dm`, `growbranches()`  -- a string do `mob.Rank` -> as skills que o cargo destrava
///   6. `RankTree.dm`, `RankTreeAssign()` -- a string do `mob.Rank` -> qual arvore de cargo ele ganha
///
/// Os tres nomes da mesma coisa divergem de verdade: o `Assistant_Guardian` e "Earth Assistant
/// Guardian" no `mob.Rank`, "Korin" no painel e "Korin" na quest. O `Namekian_Elder` e "Namekian
/// Grand Elder" no `mob.Rank` e "Namekian Elder" no painel -- e existe OUTRO cargo (`North_Elder`)
/// cujo `mob.Rank` e justamente "Namekian Elder". Transcrever isso a mao troca os dois em algum
/// momento; por isso as tres colunas saem separadas no JSON, e nao fundidas num "nome" so.
///
/// O QUE NAO DA PRA EXTRAIR, e o scanner diz isso em vez de inventar: a maioria dos cargos NAO TEM
/// requisito nenhum no codigo. Sao dados pelo verb de admin `Give Rank` (`Rewards.dm`). Cargo sem
/// requisito sai com `requisitos` vazio e uma linha em `aquisicao` dizendo por onde ele entra --
/// nunca com um requisito plausivel que o DM nao escreveu.
/// </summary>
public static class DmRankScanner
{
	// --------------------------------------------------------------------------------------
	// regex das seis fontes
	// --------------------------------------------------------------------------------------
	private static readonly Regex RxCheck = new(@"^\s*if\((?<g>[A-Za-z_][A-Za-z0-9_]*)==signature\)", RegexOptions.Compiled);
	private static readonly Regex RxSave = new(@"^\s*S\[""[^""]+""\]\s*<<\s*(?<g>[A-Za-z_][A-Za-z0-9_]*)\s*$", RegexOptions.Compiled);
	private static readonly Regex RxPainel = new(@"^(?<nome>[^:<\[\]]+):\s*\[RankList\[(?<g>[A-Za-z_][A-Za-z0-9_]*)\]\]", RegexOptions.Compiled);
	private static readonly Regex RxSetRank = new(@"^\s*Rank\s*=\s*""(?<n>[^""]*)""", RegexOptions.Compiled);
	private static readonly Regex RxIfSig = new(@"(?<g>[A-Za-z_][A-Za-z0-9_]*)\s*==\s*signature", RegexOptions.Compiled);
	private static readonly Regex RxGetSig = new(@"^\s*if\(""(?<k>[a-z]+)""\)\s*return\s+(?<g>[A-Za-z_][A-Za-z0-9_]*)\s*$", RegexOptions.Compiled);
	private static readonly Regex RxDisplay = new(@"^\s*if\(""(?<k>[a-z]+)""\)\s*return\s+""(?<n>[^""]*)""", RegexOptions.Compiled);
	// idem RxCaseRank: RQ_SKY e RQ_EVIL tem comentario na mesma linha, e sem o `(?://.*)?` as duas
	// listas saiam VAZIAS -- o Kaio deixava de ser cargo do Outro Mundo e o Demon Lord deixava de
	// ser maligno, calados.
	private static readonly Regex RxListaRq = new(@"^var/list/(?<nome>RQ_[A-Z]+)\s*=\s*list\((?<corpo>.*?)\)\s*(?://.*)?$", RegexOptions.Compiled);
	private static readonly Regex RxCaseKeys = new(@"^\s*if\((?<keys>""[a-z]+""(?:\s*,\s*""[a-z]+"")*)\)\s*$", RegexOptions.Compiled);
	private static readonly Regex RxIfReturn = new(@"^\s*if\((?<cond>.+)\)\s*return\s+""(?<msg>.+)""\s*$", RegexOptions.Compiled);
	private static readonly Regex RxDefine = new(@"^#define\s+(?<n>[A-Z_][A-Z0-9_]*)\s+(?<v>-?[0-9]+(?:\.[0-9]+)?)\s*(?://.*)?$", RegexOptions.Compiled);
	private static readonly Regex RxEnable = new(@"enableskill\(\s*(?<t>/datum/skill/[A-Za-z0-9_/]+)\s*\)", RegexOptions.Compiled);
	// o `\s*(?://.*)?` no fim NAO e enfeite: metade dos cases do growbranches tem comentario na
	// mesma linha (`if("Demon Lord")//like the grand kai`), e sem ele o cargo saia com zero skills.
	private static readonly Regex RxCaseRank = new(@"^\s*if\(""(?<n>[^""]+)""\)\s*(?://.*)?$", RegexOptions.Compiled);
	private static readonly Regex RxTreeAssign = new(@"^\s*savant\.RankTreeAssign\((?<n>\d+)\)\s*$", RegexOptions.Compiled);
	private static readonly Regex RxTreeSlot = new(@"getTree\(new\s+/datum/skill/tree/Rank/(?<t>[A-Za-z0-9_]+)\)", RegexOptions.Compiled);
	private static readonly Regex RxVerbMais = new(@"^\s*verbs\s*\+=\s*(?<v>/mob/[A-Za-z0-9_/]+)\s*$", RegexOptions.Compiled);
	private static readonly Regex RxStr = new("\"(?<s>[^\"]*)\"", RegexOptions.Compiled);

	public static VarreduraDeCargos Scan(string pastaCode)
	{
		var v = new VarreduraDeCargos();
		string[] arquivos = Directory.GetFiles(pastaCode, "*.dm", SearchOption.AllDirectories);

		string? Achar(string nome) => arquivos.FirstOrDefault(
			a => string.Equals(Path.GetFileName(a), nome, StringComparison.OrdinalIgnoreCase));

		LerDefines(arquivos, v.Defines);

		string? assign = Achar("RankAssign.dm");
		string? quests = Achar("RankQuests.dm");
		if (assign == null) { v.Avisos.Add("nao achei RankAssign.dm -- sem ele nao ha lista de cargos"); return v; }

		var porGlobal = new Dictionary<string, CargoDm>(StringComparer.Ordinal);
		LerGlobais(assign, quests, pastaCode, porGlobal, v);
		LerPainel(assign, pastaCode, porGlobal);
		LerRankVerbAssign(assign, pastaCode, porGlobal);

		if (quests != null)
		{
			LerChavesEQuests(quests, pastaCode, porGlobal, v);
			LerRequisitos(quests, pastaCode, porGlobal, v);
			LerEscada(quests, porGlobal);
		}
		else v.Avisos.Add("nao achei RankQuests.dm -- nenhum cargo tera requisito nem escada");

		LerArvores(arquivos, pastaCode, porGlobal);
		LerKitDoDeus(arquivos, pastaCode, porGlobal);
		LerAquisicao(arquivos, pastaCode, porGlobal, v);
		LerConsultas(arquivos, pastaCode, porGlobal);

		// CHAVE: a do RankQuests quando existe; senao uma NOSSA, marcada como inventada.
		foreach (CargoDm c in porGlobal.Values)
			if (c.Chave.Length == 0) { c.Chave = Slug(c.Global); c.ChaveInventada = true; }

		v.Cargos = [.. porGlobal.Values.OrderBy(c => c.Chave, StringComparer.Ordinal)];
		Conferir(v);
		return v;
	}

	// --------------------------------------------------------------------------------------
	// 1. a LISTA de cargos: quem sao os globais de rank
	// --------------------------------------------------------------------------------------
	/// <summary>
	/// A lista autoritativa NAO e o bloco `var` (que mistura EarthTax, vegeta_army e outras coisas
	/// que nao sao cargo). Sao TRES listas cruzadas, e o valor esta justamente em cruza-las:
	///
	///   `Save_Rank()`  -- quem sobrevive ao reboot
	///   `CheckRank()`  -- quem e liberado quando a signature vira personagem novo
	///   `rq_get_sig()` -- quem o sistema de quests sabe entregar
	///
	/// Nenhuma das tres esta completa, e o buraco de cada uma e um bug de jogo: cargo fora do
	/// Save_Rank some no reboot, cargo fora do CheckRank e HERDADO por um personagem novo que
	/// caiu na mesma signature. O <see cref="Conferir"/> reporta os dois.
	/// </summary>
	private static void LerGlobais(string arq, string? quests, string raiz, Dictionary<string, CargoDm> saida, VarreduraDeCargos v)
	{
		string[] L = File.ReadAllLines(arq);
		var salvos = new List<string>();
		var limpos = new HashSet<string>(StringComparer.Ordinal);
		var chaveSave = new Dictionary<string, List<string>>(StringComparer.Ordinal); // chave do savefile -> globais
		bool emSave = false, emCheck = false;

		var rxSaveChave = new Regex(@"^\s*S\[""(?<k>[^""]+)""\]\s*<<\s*(?<g>[A-Za-z_][A-Za-z0-9_]*)\s*$", RegexOptions.None);

		for (int i = 0; i < L.Length; i++)
		{
			string t = L[i];
			if (t.StartsWith("proc/Save_Rank(", StringComparison.Ordinal)) { emSave = true; emCheck = false; continue; }
			if (t.StartsWith("mob/proc/CheckRank(", StringComparison.Ordinal)) { emCheck = true; emSave = false; continue; }
			if (t.Length > 0 && !char.IsWhiteSpace(t[0])) { emSave = false; emCheck = false; }

			if (emSave && RxSave.Match(t) is { Success: true } ms)
			{
				string g = ms.Groups["g"].Value;
				// ksap_list/RankList sao listas de apoio, nao tronos
				if (g is "ksap_list" or "RankList") continue;
				if (!salvos.Contains(g)) salvos.Add(g);

				string k = rxSaveChave.Match(t).Groups["k"].Value;
				if (!chaveSave.TryGetValue(k, out List<string>? gs)) chaveSave[k] = gs = [];
				if (!gs.Contains(g)) gs.Add(g);
			}
			if (emCheck && RxCheck.Match(t) is { Success: true } mc) limpos.Add(mc.Groups["g"].Value);
		}

		// DUAS GLOBAIS NA MESMA CHAVE DO SAVEFILE e silencioso e destrutivo: a segunda sobrescreve
		// a primeira ao gravar, e as duas leem o mesmo valor ao carregar -- os dois cargos passam a
		// ter o MESMO dono depois de um reboot.
		foreach ((string k, List<string> gs) in chaveSave)
			if (gs.Count > 1)
				v.Avisos.Add($"savefile RANK: a chave \"{k}\" e usada por {gs.Count} globais ({string.Join(", ", gs)}) "
					+ "-- a ultima sobrescreve, e no Load as duas recebem o MESMO dono");

		// os que so o RankQuests conhece (e a razao de o Frost Demon Lord existir de dia e sumir no reboot)
		var doQuests = new List<string>();
		if (quests != null)
			foreach (string t in File.ReadAllLines(quests))
				if (RxGetSig.Match(t) is { Success: true } mq) doQuests.Add(mq.Groups["g"].Value);

		var todos = new List<string>(salvos);
		foreach (string g in limpos) if (!todos.Contains(g)) todos.Add(g);
		foreach (string g in doQuests) if (!todos.Contains(g)) todos.Add(g);

		// o bloco `var` do fim do arquivo: e de la que sai o comentario que documenta o cargo
		var notas = new Dictionary<string, (string nota, int linha)>(StringComparer.Ordinal);
		bool emVar = false;
		string ultimo = "";
		for (int i = 0; i < L.Length; i++)
		{
			string t = L[i];
			if (t.TrimEnd() == "var") { emVar = true; continue; }
			if (!emVar) continue;
			if (t.Length > 0 && !char.IsWhiteSpace(t[0])) { emVar = false; continue; }

			string s = t.Trim();
			if (s.Length == 0) continue;

			// linha de comentario solta: continua a nota do ultimo global (o King_of_Vegeta tem 4 linhas)
			if (s.StartsWith("//", StringComparison.Ordinal))
			{
				if (ultimo.Length > 0 && notas.TryGetValue(ultimo, out var ant))
					notas[ultimo] = (ant.nota + " " + s[2..].Trim(), ant.linha);
				continue;
			}

			int c = s.IndexOf("//", StringComparison.Ordinal);
			string nome = (c >= 0 ? s[..c] : s).Trim();
			string nota = c >= 0 ? s[(c + 2)..].Trim() : "";
			if (nome.Contains('=') || nome.Contains('/')) { ultimo = ""; continue; } // list/..., EarthTax=1
			if (!todos.Contains(nome)) { ultimo = ""; continue; }
			notas[nome] = (nota, i + 1);
			ultimo = nome;
		}

		foreach (string g in todos)
		{
			var c = new CargoDm { Global = g };
			if (notas.TryGetValue(g, out var n)) { c.NotaDm = n.nota; c.Fonte = Fonte(raiz, arq, n.linha); }
			if (!limpos.Contains(g))
				v.Avisos.Add($"{g}: o CheckRank() NAO limpa este global -- personagem novo criado numa "
					+ "signature reciclada HERDA o cargo (RankAssign.dm)");
			if (!salvos.Contains(g))
				v.Avisos.Add($"{g}: o Save_Rank() NAO grava este global -- o cargo existe em jogo e "
					+ "SOME no proximo reboot (RankAssign.dm)");
			saida[g] = c;
		}
		if (todos.Count == 0) v.Avisos.Add("Save_Rank()/CheckRank() nao renderam nenhum global -- o formato do arquivo mudou?");
	}

	// --------------------------------------------------------------------------------------
	// 2/3. os nomes: painel HTML (o que o jogador le) e mob.Rank (o que a arvore casa)
	// --------------------------------------------------------------------------------------
	private static void LerPainel(string arq, string raiz, Dictionary<string, CargoDm> alvo)
	{
		foreach (string t in File.ReadAllLines(arq))
		{
			Match m = RxPainel.Match(t.Trim());
			if (!m.Success) continue;
			if (alvo.TryGetValue(m.Groups["g"].Value, out CargoDm? c)) c.NomePainel = m.Groups["nome"].Value.Trim();
		}
	}

	/// <summary>
	/// `Rank_Verb_Assign`: `if(GLOBAL==signature)` e, uma ou duas linhas abaixo, `Rank="..."`.
	/// A ordem IMPORTA e nao da pra ignorar: sao ifs SEM else, entao o ultimo que casar sobrescreve.
	/// Ha um caso com quatro globais numa condicao so (os elders de Namek) -- todos ganham o nome.
	/// </summary>
	private static void LerRankVerbAssign(string arq, string raiz, Dictionary<string, CargoDm> alvo)
	{
		string[] L = File.ReadAllLines(arq);
		int ini = Array.FindIndex(L, s => s.StartsWith("mob/proc/Rank_Verb_Assign(", StringComparison.Ordinal));
		if (ini < 0) return;

		for (int i = ini + 1; i < L.Length; i++)
		{
			if (L[i].Length > 0 && !char.IsWhiteSpace(L[i][0])) break;
			MatchCollection gs = RxIfSig.Matches(L[i]);
			if (gs.Count == 0) continue;

			for (int j = i + 1; j < L.Length && j <= i + 3; j++)
			{
				Match r = RxSetRank.Match(L[j]);
				if (!r.Success) continue;
				foreach (Match g in gs)
					if (alvo.TryGetValue(g.Groups["g"].Value, out CargoDm? c)) c.NomeRank = r.Groups["n"].Value;
				break;
			}
		}
	}

	// --------------------------------------------------------------------------------------
	// 4. RankQuests: key <-> global, nome da quest, e as cinco listas de classificacao
	// --------------------------------------------------------------------------------------
	private static void LerChavesEQuests(string arq, string raiz, Dictionary<string, CargoDm> alvo, VarreduraDeCargos v)
	{
		string[] L = File.ReadAllLines(arq);
		var porChave = new Dictionary<string, string>(StringComparer.Ordinal); // key -> global
		var nomes = new Dictionary<string, string>(StringComparer.Ordinal);
		var listas = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		bool emGet = false, emDisp = false;

		foreach (string t in L)
		{
			if (t.StartsWith("proc/rq_get_sig(", StringComparison.Ordinal)) { emGet = true; emDisp = false; continue; }
			if (t.StartsWith("proc/rq_display(", StringComparison.Ordinal)) { emDisp = true; emGet = false; continue; }
			if (t.Length > 0 && !char.IsWhiteSpace(t[0]) && !t.StartsWith("var/list/RQ_", StringComparison.Ordinal))
			{ emGet = false; emDisp = false; }

			if (emGet && RxGetSig.Match(t) is { Success: true } mg) porChave[mg.Groups["k"].Value] = mg.Groups["g"].Value;
			if (emDisp && RxDisplay.Match(t) is { Success: true } md) nomes[md.Groups["k"].Value] = md.Groups["n"].Value;

			Match ml = RxListaRq.Match(t);
			if (ml.Success)
				listas[ml.Groups["nome"].Value] =
					[.. RxStr.Matches(ml.Groups["corpo"].Value).Select(x => x.Groups["s"].Value)];
		}

		List<string> Lista(string n) => listas.TryGetValue(n, out List<string>? l) ? l : [];
		List<string> all = Lista("RQ_ALL"), claim = Lista("RQ_CLAIMABLE"),
					 sky = Lista("RQ_SKY"), wis = Lista("RQ_WISDOM"), evil = Lista("RQ_EVIL");

		foreach ((string key, string g) in porChave)
		{
			if (!alvo.TryGetValue(g, out CargoDm? c))
			{
				v.Avisos.Add($"rq_get_sig aponta a key '{key}' pro global '{g}', que o Save_Rank nao grava");
				continue;
			}
			c.Chave = key;
			c.NomeQuest = nomes.GetValueOrDefault(key, "");
			c.TemQuest = all.Contains(key);
			c.Reivindicavel = claim.Contains(key);
			c.DoCeu = sky.Contains(key);
			c.Sabedoria = wis.Contains(key);
			c.Maligno = evil.Contains(key);
		}
	}

	/// <summary>
	/// `rq_requirements`: a UNICA porta de entrada automatica do jogo. Cada `if(...) return "..."`
	/// vira um requisito -- e a frase do `return` vem junto, porque no original ela ja e a
	/// explicacao pronta do que fazer ("karma 0 ou menos (a escola do Grou nao forma santos)").
	/// </summary>
	private static void LerRequisitos(string arq, string raiz, Dictionary<string, CargoDm> alvo, VarreduraDeCargos v)
	{
		string[] L = File.ReadAllLines(arq);
		int ini = Array.FindIndex(L, s => s.StartsWith("proc/rq_requirements(", StringComparison.Ordinal));
		if (ini < 0) { v.Avisos.Add("rq_requirements nao encontrado -- nenhum cargo tera requisito"); return; }

		var porChave = alvo.Values.Where(c => c.Chave.Length > 0)
							.ToDictionary(c => c.Chave, c => c, StringComparer.Ordinal);
		List<string> atuais = [];

		for (int i = ini + 1; i < L.Length; i++)
		{
			string t = L[i];
			if (t.Length > 0 && !char.IsWhiteSpace(t[0])) break;
			if (t.Trim().Length == 0) continue;

			Match mk = RxCaseKeys.Match(t);
			if (mk.Success)
			{
				atuais = [.. RxStr.Matches(mk.Groups["keys"].Value).Select(x => x.Groups["s"].Value)];
				continue;
			}

			Match mr = RxIfReturn.Match(t);
			if (!mr.Success || atuais.Count == 0) continue;

			string cond = mr.Groups["cond"].Value.Trim();
			string msg = mr.Groups["msg"].Value.Trim();
			bool alt = TopoEhE(cond);
			foreach (string k in atuais)
			{
				if (!porChave.TryGetValue(k, out CargoDm? c)) continue;
				int grupo = c.Requisitos.Count == 0 ? 1 : c.Requisitos[^1].Grupo + 1;
				List<RequisitoDm> rs = Classificar(cond, msg, v.Defines, alvo.Keys);
				foreach (RequisitoDm r in rs)
				{
					r.Fonte = Fonte(raiz, arq, i + 1);
					r.Grupo = grupo;
					r.Alternativa = alt && rs.Count > 1;
					c.Requisitos.Add(r);
				}
			}
		}
	}

	/// <summary>
	/// A escada, do `rq_promo_targets`: la o switch e do cargo DE BAIXO pros de cima; aqui inverte,
	/// porque quem consulta pergunta "o que preciso ja ser pra tomar ESTE cargo".
	/// </summary>
	private static void LerEscada(string arq, Dictionary<string, CargoDm> alvo)
	{
		string[] L = File.ReadAllLines(arq);
		int ini = Array.FindIndex(L, s => s.StartsWith("proc/rq_promo_targets(", StringComparison.Ordinal));
		if (ini < 0) return;

		var porChave = alvo.Values.Where(c => c.Chave.Length > 0)
							.ToDictionary(c => c.Chave, c => c, StringComparer.Ordinal);

		for (int i = ini + 1; i < L.Length; i++)
		{
			string t = L[i];
			if (t.Length > 0 && !char.IsWhiteSpace(t[0])) break;
			int eq = t.IndexOf("alvos = list(", StringComparison.Ordinal);
			if (eq < 0) continue;

			// as keys DE BAIXO estao no `if(...)` da mesma linha, antes do `alvos =`
			List<string> baixo = [.. RxStr.Matches(t[..eq]).Select(x => x.Groups["s"].Value)];
			List<string> cima = [.. RxStr.Matches(t[eq..]).Select(x => x.Groups["s"].Value)];
			foreach (string alto in cima)
			{
				if (!porChave.TryGetValue(alto, out CargoDm? c)) continue;
				foreach (string b in baixo) if (!c.Degraus.Contains(b)) c.Degraus.Add(b);
			}
		}
	}

	// --------------------------------------------------------------------------------------
	// 5/6. o que o cargo CONCEDE: arvore + skills destravadas
	// --------------------------------------------------------------------------------------
	/// <summary>
	/// O casamento aqui e pela STRING do `mob.Rank`, nao pelo global -- e por isso que um cargo
	/// cujo `Rank_Verb_Assign` nao tem ramo (Frost Demon Lord, Makyo King, Arlian) nao pega arvore
	/// nenhuma por mais que o `growbranches` tenha um `if` com o nome dele. O scanner nao conserta
	/// isso: reporta (ver Conferir).
	/// </summary>
	private static void LerArvores(string[] arquivos, string raiz, Dictionary<string, CargoDm> alvo)
	{
		var porRank = alvo.Values.Where(c => c.NomeRank.Length > 0)
						  .GroupBy(c => c.NomeRank, StringComparer.Ordinal)
						  .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

		// slot -> arvore (RankTree.dm: RankTreeAssign) e nome do Rank -> slot (growbranches)
		var slots = new Dictionary<int, string>();
		var arvorePorRank = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (string arq in arquivos)
		{
			string nome = Path.GetFileName(arq);
			bool ehTree = string.Equals(nome, "RankTree.dm", StringComparison.OrdinalIgnoreCase);
			bool ehOrdered = arq.Replace('\\', '/').Contains("/Ranks/ordered/", StringComparison.OrdinalIgnoreCase);
			if (!ehTree && !ehOrdered) continue;

			string[] L = File.ReadAllLines(arq);
			int slotAtual = -1;
			string rankAtual = "";
			string arvoreDoArquivo = "";

			for (int i = 0; i < L.Length; i++)
			{
				string t = L[i];

				if (ehTree)
				{
					if (t.TrimStart().StartsWith("if(", StringComparison.Ordinal)
						&& int.TryParse(t.Trim()[3..].Split(')')[0], out int n)) slotAtual = n;
					Match mt = RxTreeSlot.Match(t);
					if (mt.Success && slotAtual > 0) slots[slotAtual] = mt.Groups["t"].Value;

					Match mc = RxCaseRank.Match(t);
					if (mc.Success) rankAtual = mc.Groups["n"].Value;
					Match ma = RxTreeAssign.Match(t);
					if (ma.Success && rankAtual.Length > 0) arvorePorRank[rankAtual] = ma.Groups["n"].Value;
					continue;
				}

				if (t.StartsWith("datum/skill/tree/Rank/", StringComparison.Ordinal))
					arvoreDoArquivo = t["datum/skill/tree/Rank/".Length..].Split('/')[0].Trim();

				Match mr = RxCaseRank.Match(t);
				if (mr.Success) { rankAtual = mr.Groups["n"].Value; continue; }
				if (t.Trim().StartsWith("switch(", StringComparison.Ordinal)) { rankAtual = ""; continue; }
				if (rankAtual.Length == 0) continue;

				Match me = RxEnable.Match(t);
				if (!me.Success) continue;
				if (!porRank.TryGetValue(rankAtual, out List<CargoDm>? cs)) continue;
				foreach (CargoDm c in cs)
				{
					if (c.Concede.Arvore.Length == 0) c.Concede.Arvore = arvoreDoArquivo;
					string sk = me.Groups["t"].Value;
					if (!c.Concede.Skills.Contains(sk)) c.Concede.Skills.Add(sk);
				}
			}
		}

		// o slot do RankTree confirma a arvore (e cobre o cargo que NAO tem enableskill nenhum)
		foreach ((string rank, string slot) in arvorePorRank)
		{
			if (!porRank.TryGetValue(rank, out List<CargoDm>? cs)) continue;
			if (!int.TryParse(slot, out int n) || !slots.TryGetValue(n, out string? arv)) continue;
			foreach (CargoDm c in cs) if (c.Concede.Arvore.Length == 0) c.Concede.Arvore = arv;
		}
	}

	/// <summary>
	/// O kit do Deus da Destruicao NAO passa por arvore de skill: sao verbs empurrados na mao pelo
	/// `god_apply_powers()`. E o unico cargo do jogo assim, entao ganha extrator proprio -- os
	/// numeros do kit (72x da Fury, 15% do Hakai) saem dos `#define` do mesmo arquivo.
	/// </summary>
	private static void LerKitDoDeus(string[] arquivos, string raiz, Dictionary<string, CargoDm> alvo)
	{
		string? arq = arquivos.FirstOrDefault(
			a => string.Equals(Path.GetFileName(a), "GodOfDestruction.dm", StringComparison.OrdinalIgnoreCase));
		if (arq == null || !alvo.TryGetValue("God_Of_Destruction", out CargoDm? god)) return;

		string[] L = File.ReadAllLines(arq);
		int ini = Array.FindIndex(L, s => s.StartsWith("mob/proc/god_apply_powers(", StringComparison.Ordinal));
		if (ini < 0) return;

		bool condicional = false;
		for (int i = ini + 1; i < L.Length; i++)
		{
			if (L[i].Length > 0 && !char.IsWhiteSpace(L[i][0])) break;
			// os dois ultimos verbs so entram num ramo `if` (o GoD que trilha o INSTINTO recebe o
			// kit da Destruicao emprestado). Marcar isso importa: quem portar sem ver o `if`
			// entrega o kit inteiro pra todo mundo.
			if (L[i].TrimStart().StartsWith("if(", StringComparison.Ordinal)) condicional = true;
			Match m = RxVerbMais.Match(L[i]);
			if (!m.Success) continue;
			string vb = m.Groups["v"].Value + (condicional ? "  (so no ramo condicional)" : "");
			if (!god.Concede.Verbs.Contains(vb)) god.Concede.Verbs.Add(vb);
		}
		god.Concede.Notas.Add($"{Fonte(raiz, arq, ini + 1)} -- god_apply_powers(): os verbs sao empurrados "
			+ "na mao (sem arvore) e o god_strip_powers() tira TODOS na hora que o titulo cai");
	}

	// --------------------------------------------------------------------------------------
	// como o cargo ENTRA quando nao ha requisito: admin, assassinato, duelo
	// --------------------------------------------------------------------------------------
	private static void LerAquisicao(string[] arquivos, string raiz, Dictionary<string, CargoDm> alvo, VarreduraDeCargos v)
	{
		// `X = M.signature` / `X = signature` / `X = key` / `X = A.signature` fora do RankQuests:
		// e todo lugar do jogo que ENTREGA um cargo sem passar pela prova do Espirito.
		var rx = new Regex(@"^\s*(?:if\([^)]*\)\s*)?(?<g>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<fonte>[A-Za-z_][A-Za-z0-9_.]*)\s*(?://(?<c>.*))?$",
			RegexOptions.Compiled);

		foreach (string arq in arquivos)
		{
			string nome = Path.GetFileName(arq);
			if (string.Equals(nome, "RankQuests.dm", StringComparison.OrdinalIgnoreCase)) continue;
			if (string.Equals(nome, "RankAssign.dm", StringComparison.OrdinalIgnoreCase)) continue;

			string[] L = File.ReadAllLines(arq);
			for (int i = 0; i < L.Length; i++)
			{
				Match m = rx.Match(L[i].TrimEnd());
				if (!m.Success) continue;
				string g = m.Groups["g"].Value;
				if (!alvo.TryGetValue(g, out CargoDm? c)) continue;

				string f = m.Groups["fonte"].Value;
				if (!f.EndsWith("signature", StringComparison.Ordinal) && f != "key") continue;

				string via = string.Equals(nome, "Rewards.dm", StringComparison.OrdinalIgnoreCase)
					? "dado por ADMIN (verb Give Rank)"
					: $"entregue por codigo em {nome}";
				string linha = $"{Fonte(raiz, arq, i + 1)} -- {via}"
							 + (f == "key" ? "  [USA key, e o resto do sistema compara por signature]" : "");
				if (!c.Aquisicao.Contains(linha)) c.Aquisicao.Add(linha);
			}
		}

		// o proprio verb de duelo do GoD: cargo que troca de dono sem admin nem prova
		string? god = arquivos.FirstOrDefault(a => Path.GetFileName(a) == "GodOfDestruction.dm");
		if (god != null && alvo.TryGetValue("God_Of_Destruction", out CargoDm? g2))
		{
			string[] L = File.ReadAllLines(god);
			int d = Array.FindIndex(L, s => s.StartsWith("mob/verb/Desafiar_God_of_Destruction(", StringComparison.Ordinal));
			if (d >= 0) g2.Aquisicao.Add($"{Fonte(raiz, god, d + 1)} -- DUELO FORMAL: o desafiante precisa de God Ki DESPERTO; "
				+ "vitoria por nocaute/morte transfere o titulo");
		}
	}

	/// <summary>
	/// ONDE MAIS o global e consultado. E aqui que aparecem os efeitos que nao passam por arvore
	/// nem por verb: o cabelo branco do Anjo, a aura roxa do Deus, a lingua dos deuses. O
	/// comentario da propria linha vem junto porque no DM ele costuma dizer o PORQUE.
	/// </summary>
	private static void LerConsultas(string[] arquivos, string raiz, Dictionary<string, CargoDm> alvo)
	{
		foreach (string arq in arquivos)
		{
			string nome = Path.GetFileName(arq);
			if (nome is "RankAssign.dm" or "RankQuests.dm" or "Rewards.dm") continue;

			string[] L = File.ReadAllLines(arq);
			for (int i = 0; i < L.Length; i++)
			{
				string t = L[i];
				if (!t.Contains("==", StringComparison.Ordinal)) continue;

				foreach (CargoDm c in alvo.Values)
				{
					if (!t.Contains(c.Global, StringComparison.Ordinal)) continue;
					if (!Regex.IsMatch(t, $@"\b{Regex.Escape(c.Global)}\s*==|==\s*{Regex.Escape(c.Global)}\b")) continue;
					if (c.Concede.Notas.Count >= 12) continue;

					int cm = t.IndexOf("//", StringComparison.Ordinal);
					string nota = cm >= 0 ? t[(cm + 2)..].Trim() : t.Trim();
					if (nota.Length > 150) nota = nota[..150] + "...";
					c.Concede.Notas.Add($"{Fonte(raiz, arq, i + 1)} -- {nota}");
				}
			}
		}
	}

	// --------------------------------------------------------------------------------------
	// classificacao dos requisitos
	// --------------------------------------------------------------------------------------
	/// <summary>
	/// Converte a condicao do DM no requisito EQUIVALENTE -- e o sinal inverte, porque no original
	/// o `if` testa a FALHA: `if(M.karma &lt; 25) return "..."` quer dizer "exige karma >= 25".
	/// Uma condicao composta rende varios requisitos (o Guardiao pede raca E classe numa linha so).
	/// O que nao casar com nenhum padrao sai como campo "?" com a condicao crua -- ficar sem
	/// classificacao e melhor do que ser classificado errado.
	/// </summary>
	/// <summary>
	/// O operador de MAIS ALTO NIVEL da condicao e `&amp;&amp;`? (ignorando o que esta entre parenteses).
	/// E o que separa "exige as duas coisas" de "basta uma": o `if` do DM dispara na FALHA, entao
	/// um `&amp;&amp;` la em cima vira um "ou" aqui.
	/// </summary>
	private static bool TopoEhE(string cond)
	{
		int prof = 0;
		bool temE = false, temOu = false;
		for (int i = 0; i < cond.Length - 1; i++)
		{
			if (cond[i] == '(') prof++;
			else if (cond[i] == ')') prof--;
			else if (prof == 0 && cond[i] == '&' && cond[i + 1] == '&') { temE = true; i++; }
			else if (prof == 0 && cond[i] == '|' && cond[i + 1] == '|') { temOu = true; i++; }
		}
		return temE && !temOu; // misturou os dois: nao arrisca, trata como conjuncao
	}

	private static List<RequisitoDm> Classificar(string cond, string msg, Dictionary<string, double> defs, IEnumerable<string> globais)
	{
		var saida = new List<RequisitoDm>();
		RequisitoDm Novo(string campo, string op, double val) => new()
		{ Campo = campo, Op = op, Valor = val, Texto = msg, Bruto = cond };

		double Num(string s)
		{
			s = s.Trim();
			if (defs.TryGetValue(s, out double d)) return d;
			return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : double.NaN;
		}

		// karma / BP / mortes / zenni: comparacao simples com um numero (ou um define)
		foreach (Match m in Regex.Matches(cond, @"M\.(?<c>karma|BP|deathcounter|zenni)\s*(?<op><|>)\s*(?<v>-?[A-Za-z0-9_.]+)"))
		{
			double val = Num(m.Groups["v"].Value);
			if (double.IsNaN(val)) continue;
			string campo = m.Groups["c"].Value switch
			{
				"karma" => "karma",
				"BP" => "bp",
				"deathcounter" => "mortes",
				_ => "zenni",
			};
			// `< N` no teste de falha  =>  exige `>= N`;  `> N`  =>  exige `<= N`
			saida.Add(Novo(campo, m.Groups["op"].Value == "<" ? ">=" : "<=", val));
		}

		// reputacao de planeta: planet_rep_get("Earth", M) < REP_HERO
		foreach (Match m in Regex.Matches(cond, @"planet_rep_get\(\s*""(?<p>[^""]+)""\s*,\s*M\s*\)\s*<\s*(?<v>[A-Za-z0-9_]+)"))
		{
			double val = Num(m.Groups["v"].Value);
			if (double.IsNaN(val)) continue;
			saida.Add(Novo("reputacao:" + m.Groups["p"].Value, ">=", val));
		}

		// raca: qualquer literal comparado com Race/Parent_Race
		var racas = new List<string>();
		foreach (Match m in Regex.Matches(cond, @"M\.(?:Race|Parent_Race)\s*[!=]=\s*""(?<s>[^""]+)"""))
			if (!racas.Contains(m.Groups["s"].Value)) racas.Add(m.Groups["s"].Value);
		if (racas.Count > 0)
		{
			RequisitoDm r = Novo("raca", "uma de", 0);
			r.Valores = racas;
			saida.Add(r);
		}

		foreach (Match m in Regex.Matches(cond, @"M\.Class\s*[!=]=\s*""(?<s>[^""]+)"""))
		{
			RequisitoDm r = Novo("classe", "uma de", 0);
			r.Valores = [m.Groups["s"].Value];
			saida.Add(r);
		}

		if (cond.Contains("godki", StringComparison.Ordinal))
		{
			Match mm = Regex.Match(cond, @"godki\.mastery\s*>=\s*(?<v>[A-Za-z0-9_]+)");
			if (mm.Success && !double.IsNaN(Num(mm.Groups["v"].Value)))
				saida.Add(Novo("godki", "maestria >=", Num(mm.Groups["v"].Value)));
			else if (cond.Contains("awakened", StringComparison.Ordinal))
				saida.Add(Novo("godki", "desperto", 0));
		}

		// cargo anterior: a condicao compara a signature com globais de rank
		var cargos = new List<string>();
		if (cond.Contains("signature", StringComparison.Ordinal) || Regex.IsMatch(cond, @"[!=]=\s*s\b|\bs\s*=="))
			foreach (string g in globais)
				if (Regex.IsMatch(cond, $@"\b{Regex.Escape(g)}\b")) cargos.Add(g);
		if (cargos.Count > 0)
		{
			RequisitoDm r = Novo("cargo", "uma de", 0);
			r.Valores = cargos;
			saida.Add(r);
		}

		// sangue Kai vem de uma var auxiliar declarada no topo do proc, nao da condicao
		if (cond.Contains("kai_blood", StringComparison.Ordinal))
		{
			RequisitoDm r = Novo("raca", "uma de (alternativa)", 0);
			r.Valores = ["Kai"];
			saida.Add(r);
		}

		if (saida.Count == 0) saida.Add(Novo("?", "", 0));
		return saida;
	}

	// --------------------------------------------------------------------------------------
	// conferencia: o que o proprio DM deixou pendurado
	// --------------------------------------------------------------------------------------
	private static void Conferir(VarreduraDeCargos v)
	{
		foreach (CargoDm c in v.Cargos)
		{
			if (c.NomeRank.Length == 0)
				v.Avisos.Add($"{c.Global}: sem ramo no Rank_Verb_Assign -- mob.Rank nunca recebe valor, "
					+ "entao este cargo NAO ganha arvore nem skill nenhuma em jogo");
			if (c.NomeRank.Length > 0 && c.Concede.Arvore.Length == 0 && c.Concede.Verbs.Count == 0)
				v.Avisos.Add($"{c.Global} (\"{c.NomeRank}\"): tem nome de Rank mas nenhum growbranches/"
					+ "RankTreeAssign casa com ele -- nao ha arvore de skill; o que ele concede (se concede) "
					+ "esta nas notas");
			if (c.Reivindicavel && c.Requisitos.Count == 0)
				v.Avisos.Add($"{c.Global}: esta em RQ_CLAIMABLE mas o rq_requirements nao tem ramo -- reivindicavel de graca");
			if (c.NomePainel.Length == 0)
				v.Avisos.Add($"{c.Global}: nao aparece no painel do verb Ranks -- o jogador nao tem como saber que existe");
		}

		var repetidos = v.Cargos.Where(c => c.NomeRank.Length > 0)
							.GroupBy(c => c.NomeRank, StringComparer.Ordinal).Where(g => g.Count() > 1);
		foreach (var g in repetidos)
			v.Avisos.Add($"o nome de Rank \"{g.Key}\" pertence a {g.Count()} globais ({string.Join(", ", g.Select(c => c.Global))}) "
				+ "-- a arvore de skills nao consegue distinguir os dois");
	}

	// --------------------------------------------------------------------------------------
	// utilitarios
	// --------------------------------------------------------------------------------------
	private static void LerDefines(string[] arquivos, Dictionary<string, double> saida)
	{
		foreach (string arq in arquivos)
			foreach (string t in File.ReadAllLines(arq))
			{
				Match m = RxDefine.Match(t);
				if (m.Success) saida[m.Groups["n"].Value] =
					double.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture);
			}
	}

	private static string Fonte(string raiz, string arq, int linha) =>
		Path.GetRelativePath(raiz, arq).Replace('\\', '/') + ":" + linha.ToString(CultureInfo.InvariantCulture);

	/// <summary>
	/// INVENCAO NOSSA, e o JSON marca como tal: o DM so tem key pros 17 cargos do RankQuests. Os
	/// outros oito sao referidos pelo nome da global, que nao serve de chave de rede.
	/// </summary>
	private static string Slug(string global) =>
		global.Replace("_Rank", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();

	// --------------------------------------------------------------------------------------
	// JSON
	// --------------------------------------------------------------------------------------
	public static string ParaJson(VarreduraDeCargos v)
	{
		var sb = new StringBuilder("{\n  \"cargos\": [\n");
		bool primeiro = true;
		foreach (CargoDm c in v.Cargos)
		{
			if (!primeiro) sb.Append(",\n");
			primeiro = false;
			sb.Append("    {\n");
			sb.Append($"      \"chave\": {J(c.Chave)}, \"chave_inventada\": {B(c.ChaveInventada)},\n");
			sb.Append($"      \"global\": {J(c.Global)}, \"fonte\": {J(c.Fonte)},\n");
			sb.Append($"      \"nome\": {J(Melhor(c))},\n");
			sb.Append($"      \"nome_quest\": {J(c.NomeQuest)}, \"nome_painel\": {J(c.NomePainel)}, \"nome_rank\": {J(c.NomeRank)},\n");
			sb.Append($"      \"nota_dm\": {J(c.NotaDm)},\n");
			sb.Append($"      \"unico\": {B(c.Unico)}, \"quest\": {B(c.TemQuest)}, \"reivindicavel\": {B(c.Reivindicavel)},\n");
			sb.Append($"      \"ceu\": {B(c.DoCeu)}, \"sabedoria\": {B(c.Sabedoria)}, \"maligno\": {B(c.Maligno)},\n");
			sb.Append($"      \"degraus\": [{string.Join(", ", c.Degraus.Select(J))}],\n");
			sb.Append("      \"requisitos\": [");
			sb.Append(string.Join(", ", c.Requisitos.Select(r =>
				"{ " + $"\"campo\": {J(r.Campo)}, \"op\": {J(r.Op)}, \"valor\": {r.Valor.ToString("0.####", CultureInfo.InvariantCulture)}, "
					 + $"\"grupo\": {r.Grupo}, \"alternativa\": {B(r.Alternativa)}, "
					 + $"\"valores\": [{string.Join(", ", r.Valores.Select(J))}], \"texto\": {J(r.Texto)}, "
					 + $"\"bruto\": {J(r.Bruto)}, \"fonte\": {J(r.Fonte)}" + " }")));
			sb.Append("],\n");
			sb.Append($"      \"aquisicao\": [{string.Join(", ", c.Aquisicao.Select(J))}],\n");
			sb.Append("      \"concede\": { ");
			sb.Append($"\"arvore\": {J(c.Concede.Arvore)}, ");
			sb.Append($"\"skills\": [{string.Join(", ", c.Concede.Skills.Select(J))}], ");
			sb.Append($"\"verbs\": [{string.Join(", ", c.Concede.Verbs.Select(J))}], ");
			sb.Append($"\"notas\": [{string.Join(", ", c.Concede.Notas.Select(J))}]");
			sb.Append(" }\n    }");
		}
		sb.Append("\n  ],\n");
		sb.Append($"  \"avisos\": [{string.Join(", ", v.Avisos.Select(J))}]\n}}\n");
		return sb.ToString();
	}

	private static string Melhor(CargoDm c) =>
		c.NomeQuest.Length > 0 ? c.NomeQuest
		: c.NomePainel.Length > 0 ? c.NomePainel
		: c.NomeRank.Length > 0 ? c.NomeRank
		: c.Global.Replace('_', ' ');

	private static string B(bool b) => b ? "true" : "false";

	private static string J(string s) => "\"" + s
		.Replace("\\", "\\\\", StringComparison.Ordinal)
		.Replace("\"", "\\\"", StringComparison.Ordinal)
		.Replace("\n", " ", StringComparison.Ordinal)
		.Replace("\r", "", StringComparison.Ordinal) + "\"";
}
