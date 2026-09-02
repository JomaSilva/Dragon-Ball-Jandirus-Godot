using System.Text;
using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>
/// O `growbranches()` DE CADA ARVORE, TRADUZIDO EM DADO.
///
/// ============================ POR QUE DADO E NAO 46 `if` EM C# ============================
/// Cada arvore do DM declara a PROPRIA regra de crescimento num corpo de proc (`Skill Trees/trees.dm:136`,
/// "place any enabling in here"). Sao 46 arvores e tres coisas que um `growbranches()` faz:
///
///   1. sobe o `allowedtier` conforme o que foi INVESTIDO nela   (`Body.dm:20-21`: `invested>=4 -> 2`);
///   2. abre OUTRA arvore                                         (`Body.dm:25`: `enabletree(Bodybuilding)`);
///   3. acende ou apaga uma skill dela mesma                      (`Mind.dm:15`: `enableskill(Basic_Ki_Awareness)`).
///
/// Escrever isso a mao no port eram 46 blocos que envelhecem no primeiro dia em que alguem mexe num
/// `.dm` -- e ja envelheceram: o `RecalcularDestravadas` do Core tinha SO as quatro portas do Body e
/// nenhum tier, e o Afterimage (tier 2, sem pre-requisito) saia de graca no primeiro milissegundo.
///
/// Aqui o corpo do proc vira uma lista de REGRAS, uma por linha de efeito, todas com a mesma cara:
///
///     tipo;alvo;condicao
///
///     tier;2;invested>=4                                        -- o allowedtier vira 2
///     arvore;/datum/skill/tree/Bodybuilding;bodyreadiness>2     -- abre a arvore
///     acende;/datum/skill/mind/Basic_Ki_Awareness;kiawarenessskill>=1
///     apaga;*;invested>=3||(invested&&Class!='None')            -- `*` = todos os galhos desta arvore
///
/// O separador e `;` porque a condicao carrega `||`. A condicao e a EXPRESSAO DO DM normalizada: sem
/// espacos, sem o prefixo `savant.`, string entre aspas simples (o leitor de json do Core fatia por
/// aspas duplas). Quem AVALIA e o Core (`RegrasDeArvore`), com um contexto que sabe `invested`, os
/// contadores do lutador (`bodyskill`, `kieffusionskill`...) e `Class`/`Race`. Identificador que o
/// port nao tem vira "desconhecido", e regra desconhecida NAO dispara -- e o censo diz qual.
/// ==========================================================================================
///
/// ============================ O QUE E TRADUZIDO, E O QUE NAO E ============================
/// LIDO: `if`/`else if`/`else`, `switch` + `if("caso")`, `for(... in constituentskills)` (vira `*`),
/// `enableskill`/`disableskill`/`enabletree`, e `allowedtier = N | min(invested+1,C) | max(invested+1,1)`
/// (as formulas viram degraus: `tier;k+1;invested>=k`).
///
/// NEUTRALIZADO DE PROPOSITO (vira `1`):
///   * `savant.didbodychange` e `savant.gravitatecheck` sao flags de "algo mudou, reavalie": existem
///     pra poupar CPU no BYOND, e o port reavalia do zero a cada compra -- entao "mudou" e "sempre";
///   * `!gotlegendchoice`, `!acquiredFormMastery`, `!acquiredSSJtrees`, `!dewitonce` sao flags de
///     "primeira vez", pela mesma razao: num recalculo sem estado a primeira vez e toda vez;
///   * `savant` sozinho e guarda de nulo.
///
/// `switch(invested)` com caso numerico vira `invested>=N`, e nao `==N`: no DM o `enabled` e uma flag
/// que FICA acesa (a Magia acende `materialization` em 5 e so a apaga em `invested<5`,
/// `Magic_Tree.dm:12-19`); num recalculo do zero, `==5` apagaria a skill no sexto marco.
///
/// FORA (vai pro diario <see cref="NaoLidas"/>, impresso pelo comando `skills`): `whitelist`/`blacklist`
/// (o `override`), `getTree`/`saiyantreeget` (concessao de arvore de forma -- a escada de formas do
/// port ja cobre), `mastery_enable`, e o `prunebranches()` inteiro -- ele so DESFAZ o que o
/// `growbranches()` fez, e um recalculo do zero nao precisa de desfazer.
/// ==========================================================================================
/// </summary>
public static class DmGalhosScanner
{
	/// <summary>As linhas de `growbranches()` que este tradutor NAO entende: (arvore, linha).</summary>
	public static readonly List<(string Arvore, string Linha)> NaoLidas = [];

	private static readonly Regex RxIf = new(@"^(?<else>else\s+)?if\s*\(", RegexOptions.Compiled);
	private static readonly Regex RxSwitch = new(@"^switch\s*\(", RegexOptions.Compiled);
	private static readonly Regex RxForTodos = new(
		@"^for\s*\(\s*var/datum/skill/\w+\s+in\s+constituentskills\s*\)", RegexOptions.Compiled);
	private static readonly Regex RxFor = new(@"^for\s*\(", RegexOptions.Compiled);
	private static readonly Regex RxChamada = new(
		@"^(?<f>enableskill|disableskill|enabletree|disabletree)\s*\(\s*(?<a>[^)]*)\)", RegexOptions.Compiled);
	private static readonly Regex RxTier = new(@"^allowedtier\s*=\s*(?<e>.+)$", RegexOptions.Compiled);
	private static readonly Regex RxMinMax = new(
		@"^(?<f>min|max)\(invested\+1,(?<c>\d+)\)$", RegexOptions.Compiled);
	private static readonly Regex RxTypePath = new(@"^/?(?:new\s*/)?datum/skill(?:/[A-Za-z0-9_]+)+$", RegexOptions.Compiled);

	/// <summary>Flags de "algo mudou" e de "primeira vez" -- ver o cabecalho.</summary>
	private static readonly string[] FlagsSempreVerdadeiras =
		["didbodychange", "gravitatecheck"];
	private static readonly string[] FlagsDePrimeiraVez =
		["gotlegendchoice", "acquiredFormMastery", "acquiredSSJtrees", "dewitonce"];

	/// <summary>Um bloco aberto (`if`, `else`, `switch`, `for`) e o que ele impoe as linhas de dentro.</summary>
	private sealed class Quadro
	{
		public int Ind;
		public string Cond = "";         // a condicao ACUMULADA (ja com as dos pais)
		public string? Switch;           // `switch(expr)`: as `if("caso")` de dentro comparam com isto
		public bool Todos;               // `for(... in constituentskills)`: alvo vira `*`
		public string Cadeia = "";       // OR das condicoes dos `if`/`else if` irmaos ja vistos (pro `else`)
	}

	/// <summary>
	/// Traduz o corpo de um `growbranches()` (linhas com indentacao) nas regras da arvore.
	/// <paramref name="tierMax"/> e o teto pra expandir `max(invested+1,1)`.
	/// </summary>
	public static List<string> Traduzir(string arvore, List<(int Ind, string Txt)> corpo, int tierMax)
	{
		var regras = new List<string>();
		var pilha = new List<Quadro>();

		foreach ((int ind, string cru) in corpo)
		{
			string txt = cru.Trim();
			if (txt.Length == 0) continue;
			while (pilha.Count > 0 && pilha[^1].Ind >= ind) pilha.RemoveAt(pilha.Count - 1);

			Quadro? pai = pilha.Count > 0 ? pilha[^1] : null;
			string condPai = pai?.Cond ?? "";
			bool todos = pilha.Exists(q => q.Todos);

			// ---- if / else if / else ----
			Match mi = RxIf.Match(txt);
			if (mi.Success)
			{
				int abre = txt.IndexOf('(');
				int fecha = FechaDe(txt, abre);
				if (fecha < 0) { NaoLidas.Add((arvore, cru)); continue; }
				string dentro = txt[(abre + 1)..fecha];
				string resto = txt[(fecha + 1)..].Trim();

				string cond = pai?.Switch != null ? Caso(pai.Switch, dentro) : Normalizar(dentro);
				string minha = cond;
				if (mi.Groups["else"].Success && pai != null && pai.Cadeia.Length > 0)
					minha = E(Nao(pai.Cadeia), cond);
				if (pai != null) pai.Cadeia = pai.Cadeia.Length == 0 ? cond : Ou(pai.Cadeia, cond);

				string total = E(condPai, minha);
				if (resto.Length > 0) { Linha(arvore, resto, total, todos, tierMax, regras, cru); continue; }
				pilha.Add(new Quadro { Ind = ind, Cond = total });
				continue;
			}
			if (txt == "else" || txt.StartsWith("else ", StringComparison.Ordinal))
			{
				string cadeia = pai?.Cadeia ?? "";
				string total = cadeia.Length > 0 ? E(condPai, Nao(cadeia)) : E(condPai, "?");
				string resto = txt.Length > 4 ? txt[4..].Trim() : "";
				if (resto.Length > 0) { Linha(arvore, resto, total, todos, tierMax, regras, cru); continue; }
				pilha.Add(new Quadro { Ind = ind, Cond = total });
				continue;
			}

			// ---- switch ----
			if (RxSwitch.IsMatch(txt))
			{
				int abre = txt.IndexOf('(');
				int fecha = FechaDe(txt, abre);
				if (fecha < 0) { NaoLidas.Add((arvore, cru)); continue; }
				pilha.Add(new Quadro { Ind = ind, Cond = condPai, Switch = Normalizar(txt[(abre + 1)..fecha]) });
				continue;
			}

			// ---- for ----
			if (RxForTodos.IsMatch(txt))
			{
				pilha.Add(new Quadro { Ind = ind, Cond = condPai, Todos = true });
				continue;
			}
			if (RxFor.IsMatch(txt))
			{
				// laco sobre algo que o port nao enxerga (skills aprendidas, o kit de um cargo): o
				// que estiver dentro fica com condicao DESCONHECIDA -- existe como regra, nunca dispara
				pilha.Add(new Quadro { Ind = ind, Cond = E(condPai, "?") });
				continue;
			}

			Linha(arvore, txt, condPai, todos, tierMax, regras, cru);
		}
		return regras;
	}

	/// <summary>Uma linha de EFEITO (chamada ou `allowedtier =`) sob uma condicao.</summary>
	private static void Linha(string arvore, string txt, string cond, bool todos, int tierMax,
							  List<string> regras, string cru)
	{
		Match mc = RxChamada.Match(txt);
		if (mc.Success)
		{
			string f = mc.Groups["f"].Value;
			string a = mc.Groups["a"].Value.Trim();
			string? alvo = a == "S.type" && todos ? "*"
						 : RxTypePath.IsMatch(a) ? DmSkillScannerPath(a)
						 : null;
			if (alvo == null) { NaoLidas.Add((arvore, cru)); return; }
			string tipo = f switch
			{
				"enableskill" => "acende",
				"disableskill" => "apaga",
				"enabletree" => "arvore",
				_ => "fecha",
			};
			// `disabletree` so aparece em `prunebranches()`, que nao e lido; se um dia aparecer no
			// `growbranches()`, sai no diario em vez de virar regra que o Core nao conhece
			if (tipo == "fecha") { NaoLidas.Add((arvore, cru)); return; }
			regras.Add($"{tipo};{alvo};{cond}");
			return;
		}

		Match mt = RxTier.Match(txt);
		if (mt.Success)
		{
			string e = Normalizar(mt.Groups["e"].Value);
			if (int.TryParse(e, out int n)) { regras.Add($"tier;{n};{cond}"); return; }
			Match mm = RxMinMax.Match(e);
			if (mm.Success)
			{
				// `min(invested+1, C)` = degraus 1..C-1; `max(invested+1, 1)` nao tem teto -- o teto e
				// o `maxtier` da arvore, e degrau acima do maxtier e vitrine vazia
				int teto = mm.Groups["f"].Value == "min" ? int.Parse(mm.Groups["c"].Value) : Math.Max(tierMax, 1);
				for (int k = 1; k < teto; k++) regras.Add($"tier;{k + 1};{E(cond, $"invested>={k}")}");
				return;
			}
			NaoLidas.Add((arvore, cru));
			return;
		}

		// assignments de contabilidade do proprio datum, `..()`, `return`, `. = ..()`: ruido do DM,
		// nao efeito -- ficam de fora do diario pra ele nao virar lista de `..()`
		string semEspaco = txt.Replace(" ", "");
		if (semEspaco is "..()" or "return" or ".=..()") return;
		if (!semEspaco.Contains('(') && semEspaco.Contains('=')) return;
		NaoLidas.Add((arvore, cru));
	}

	// =====================================================================
	// A CONDICAO
	// =====================================================================
	/// <summary>`switch(expr)` + `if("A","B")` ou `if(5)` -> a comparacao que o caso significa.</summary>
	private static string Caso(string expr, string casos)
	{
		var partes = new List<string>();
		foreach (string c in Dividir(casos))
		{
			string v = c.Trim();
			if (v.Length == 0) continue;
			if (v.StartsWith('"') && v.EndsWith('"') && v.Length >= 2)
				partes.Add($"{expr}=='{v[1..^1]}'");
			else if (expr == "invested" && int.TryParse(v, out int n))
				partes.Add($"invested>={n}");   // ver o cabecalho: o `enabled` do DM FICA aceso
			else
				partes.Add($"{expr}=={Normalizar(v)}");
		}
		return partes.Count == 1 ? partes[0] : "(" + string.Join("||", partes) + ")";
	}

	/// <summary>Separa por virgula FORA de aspas.</summary>
	private static IEnumerable<string> Dividir(string s)
	{
		var sb = new StringBuilder();
		bool aspas = false;
		foreach (char ch in s)
		{
			if (ch == '"') aspas = !aspas;
			if (ch == ',' && !aspas) { yield return sb.ToString(); sb.Clear(); continue; }
			sb.Append(ch);
		}
		yield return sb.ToString();
	}

	/// <summary>
	/// A expressao do DM na forma que o Core le: sem espacos, sem `savant.`, strings entre aspas
	/// simples, e as flags do cabecalho neutralizadas.
	/// </summary>
	public static string Normalizar(string expr)
	{
		var sb = new StringBuilder();
		bool aspas = false;
		foreach (char ch in expr)
		{
			if (ch == '"') { aspas = !aspas; sb.Append('\''); continue; }
			if (!aspas && char.IsWhiteSpace(ch)) continue;
			sb.Append(ch);
		}
		string s = sb.ToString();
		s = s.Replace("savant.", "");

		foreach (string f in FlagsDePrimeiraVez) s = SubstituirIdent(s, "!" + f, "1");
		foreach (string f in FlagsSempreVerdadeiras) s = SubstituirIdent(s, f, "1");
		s = SubstituirIdent(s, "savant", "1");
		// `X&&1` e `1&&X` sao `X`: a flag neutralizada nao deixa rastro na regra que o censo imprime.
		// SO quando o `1` e um OPERANDO INTEIRO (comeco, `(`, `&&`, `||` de um lado e fim, `)`, `&&`,
		// `||` do outro): a primeira versao disto casava o `1` de `invested>=1&&` e deixava
		// `invested>=invested<4` no json -- o Spirit nunca subia de tier.
		s = Regex.Replace(s, @"(?<=^|\(|&&|\|\|)1&&", "");
		s = Regex.Replace(s, @"&&1(?=$|\)|&&|\|\|)", "");
		return s;
	}

	/// <summary>Troca um identificador INTEIRO (nao um pedaco de outro).</summary>
	private static string SubstituirIdent(string s, string de, string por)
	{
		string padrao = (de.StartsWith('!') ? @"(?<![A-Za-z0-9_])!" + Regex.Escape(de[1..]) : @"(?<![A-Za-z0-9_.])" + Regex.Escape(de))
					  + @"(?![A-Za-z0-9_])";
		return Regex.Replace(s, padrao, por);
	}

	// As flags neutralizadas viram `1`, e `1&&X` e so `X`: a regra sai legivel no json e no censo.
	private static string E(string a, string b) =>
		a.Length == 0 || a == "1" ? b : b.Length == 0 || b == "1" ? a : $"{Par(a)}&&{Par(b)}";
	private static string Ou(string a, string b) =>
		a.Length == 0 ? b : b.Length == 0 ? a : $"{Par(a)}||{Par(b)}";
	private static string Nao(string a) => a.Length == 0 ? "" : a == "1" ? "0" : $"!{Par(a)}";

	/// <summary>Poe parenteses so quando a expressao tem operador logico solto.</summary>
	private static string Par(string a) =>
		a.Contains("&&", StringComparison.Ordinal) || a.Contains("||", StringComparison.Ordinal) ? $"({a})" : a;

	private static int FechaDe(string s, int abre)
	{
		int prof = 0;
		bool aspas = false;
		for (int i = abre; i < s.Length; i++)
		{
			char ch = s[i];
			if (ch == '"') aspas = !aspas;
			if (aspas) continue;
			if (ch == '(') prof++;
			else if (ch == ')' && --prof == 0) return i;
		}
		return -1;
	}

	private static string DmSkillScannerPath(string a)
	{
		string p = a.Replace("new", "").Replace(" ", "").Trim();
		return p.StartsWith('/') ? p : "/" + p;
	}
}
