using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>Um planeta pre-feito: o que a cena dele precisa saber sobre si.</summary>
public sealed class PlanetaDef
{
	public string Nome = "";
	public double Gravidade = 1;
	public string Tipo = "Rochoso";
}

/// <summary>
/// Le a GRAVIDADE DE CADA PLANETA da tabela autoritativa do DM (`Modules/Stats/BP/Gravity.dm`,
/// o `switch(Planet)` que roda a cada recalculo).
///
/// POR QUE EXTRAIR UMA TABELA DE VINTE LINHAS: porque ela e a diferenca entre treinar em Vegeta
/// (10x) e treinar na Terra (1x), e um digito errado aqui nao aparece em teste -- aparece meses
/// depois como "por que esse cara subiu tao rapido". O DM e a fonte; um `switch` transcrito a mao
/// e uma segunda fonte esperando divergir da primeira.
///
/// O QUE **NAO** SAI DAQUI E O BIOMA. O original nao tem esse conceito: nao ha campo dizendo que
/// Namek e um jardim e que o Planeta Icer e gelado. O tipo vem da tabela declarada em
/// <see cref="TipoPorNome"/> -- e escolha nossa, nao extracao, e esta escrito como tal.
/// </summary>
public static class DmPlanetScanner
{
	/// <summary>`if("Vegeta") Planetgrav=10` -- o valor na MESMA linha do teste.</summary>
	private static readonly Regex RxLinha = new(
		@"^\s*if\(""(?<p>[^""]+)""\)\s*Planetgrav\s*=\s*(?<g>[0-9.]+)", RegexOptions.Compiled);

	/// <summary>
	/// `if("Hell")` sozinho, com o `Planetgrav=10` na linha DE BAIXO.
	///
	/// O mesmo `switch` usa as duas formas sem criterio -- Hera cabe numa linha e Inferno nao,
	/// e nada no arquivo explica por que. Ler so a forma de uma linha perdia Inferno (10x) e
	/// Ceu, silenciosamente: eles cairiam no default de gravidade 1, e treinar no Inferno
	/// renderia como treinar num parque.
	/// </summary>
	private static readonly Regex RxSoTeste = new(
		@"^\s*if\(""(?<p>[^""]+)""\)\s*$", RegexOptions.Compiled);

	private static readonly Regex RxSoValor = new(
		@"^\s*Planetgrav\s*=\s*(?<g>[0-9.]+)", RegexOptions.Compiled);

	/// <summary>
	/// O BIOMA de cada planeta pre-feito. NAO vem do DM -- e leitura nossa do que cada lugar e no
	/// anime e nos mapas. Serve pra que o gerador procedural saiba com o que se parecer, e pra que
	/// um planeta sorteado ao lado de Namek possa nascer parecido com Namek.
	/// </summary>
	private static readonly Dictionary<string, string> TipoPorNome = new(StringComparer.OrdinalIgnoreCase)
	{
		["Earth"] = "Jardim",
		["Namek"] = "Jardim",
		["Vegeta"] = "Rochoso",
		["Icer Planet"] = "Gelado",
		["Arlia"] = "Deserto",
		["Hera"] = "Rochoso",
		["Hell"] = "Vulcanico",
		["Heaven"] = "Jardim",
		["Afterlife"] = "Jardim",
		["Big Gete Star"] = "Morto",
		["Makyo Star"] = "Morto",
		["God Realm"] = "Jardim",
		["Void"] = "Morto",
		["Sealed"] = "Morto",
	};

	public static List<PlanetaDef> Scan(string pastaCode)
	{
		var achados = new Dictionary<string, PlanetaDef>(StringComparer.OrdinalIgnoreCase);
		string arq = Path.Combine(pastaCode, "Modules", "Stats", "BP", "Gravity.dm");
		if (!File.Exists(arq)) return [];

		string? pendente = null;   // nome visto numa linha, esperando o valor na proxima
		foreach (string linha in File.ReadAllLines(arq))
		{
			if (pendente != null)
			{
				Match mv = RxSoValor.Match(linha);
				string alvo = pendente;
				pendente = null;
				if (mv.Success && !achados.ContainsKey(alvo))
					achados[alvo] = new PlanetaDef
					{
						Nome = alvo,
						Gravidade = double.Parse(mv.Groups["g"].Value, CultureInfo.InvariantCulture),
						Tipo = TipoPorNome.GetValueOrDefault(alvo, "Rochoso"),
					};
			}
			if (RxSoTeste.Match(linha) is { Success: true } mt) { pendente = mt.Groups["p"].Value; continue; }

			Match m = RxLinha.Match(linha);
			if (!m.Success) continue;
			string nome = m.Groups["p"].Value;

			// A PRIMEIRA OCORRENCIA VENCE. O arquivo tem o `switch` de verdade e, mais abaixo,
			// trechos comentados com os mesmos nomes; ler o ultimo pegaria o comentario.
			if (achados.ContainsKey(nome)) continue;

			achados[nome] = new PlanetaDef
			{
				Nome = nome,
				Gravidade = double.Parse(m.Groups["g"].Value, CultureInfo.InvariantCulture),
				Tipo = TipoPorNome.GetValueOrDefault(nome, "Rochoso"),
			};
		}
		return [.. achados.Values];
	}

	public static string ParaJson(IEnumerable<PlanetaDef> defs)
	{
		var sb = new StringBuilder("[\n");
		bool primeiro = true;
		foreach (PlanetaDef d in defs.OrderBy(p => p.Gravidade).ThenBy(p => p.Nome))
		{
			if (!primeiro) sb.Append(",\n");
			primeiro = false;
			sb.Append($"  {{ \"nome\": {J(d.Nome)}, ");
			sb.Append($"\"gravidade\": {d.Gravidade.ToString("0.##", CultureInfo.InvariantCulture)}, ");
			sb.Append($"\"tipo\": {J(d.Tipo)} }}");
		}
		return sb.Append("\n]\n").ToString();
	}

	private static string J(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
