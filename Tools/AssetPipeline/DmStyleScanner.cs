using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>Um estilo de luta, como o DM o declara.</summary>
public sealed class EstiloDef
{
	public string Id = "";        // "SaiyanStyle"
	public string Nome = "";
	public string Desc = "";

	/// <summary>
	/// OS `default*` DO DM -- e eles NAO SAO MULTIPLICADORES. Sao "pontos" na faixa 1..6, e o
	/// multiplicador de verdade so nasce em `UpdateStyle()`: `1 + (default-1) * mudanca`.
	/// Ler `defaultphysoff = 6` como 6x e a armadilha numero um deste subsistema -- daria um
	/// Saiyan Style com SEIS VEZES o ataque em vez de dez por cento a mais.
	/// </summary>
	public Dictionary<string, double> Defaults = new(StringComparer.Ordinal);

	public double LearnCost;
	public double AllocatedPoints;
	public string Arquivo = "";
}

/// <summary>
/// Le os estilos de luta (`/datum/style/...`) e quem os concede.
///
/// Sao oito estilos vivos e nove skills que os penduram -- pouco o bastante pra transcrever a mao,
/// e e exatamente por isso que vale extrair: numa tabela de nove linhas o erro de digitacao nao
/// chama atencao nenhuma, e um `defaultspeed` trocado entre God e Namek nao aparece em teste, so
/// numa reclamacao de balanceamento seis meses depois.
///
/// ARMADILHA DO DM, e esta esta no proprio nome dos campos: e `defaultechnique` (sem o "t" de
/// "technique") e `defaultsstaminamod` (com dois "s"). Nao sao erros de leitura -- e assim que
/// esta escrito nos oito arquivos, e um scanner que "corrigisse" os nomes leria zero.
/// </summary>
public static class DmStyleScanner
{
	private static readonly Regex RxEstilo = new(@"^/?datum/style/(?<id>[A-Za-z0-9_]+)\s*$", RegexOptions.Compiled);
	private static readonly Regex RxProp = new(@"^\s*(?<k>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<v>.+?)\s*$", RegexOptions.Compiled);
	private static readonly Regex RxStr = new("\"(?<s>[^\"]*)\"", RegexOptions.Compiled);

	/// <summary>`attachedstyle = new /datum/style/KameStyle` dentro de um after_learn.</summary>
	public static readonly Regex RxAtacha = new(
		@"attachedstyle\s*=\s*new\s*(?:/?datum/style/(?<id>[A-Za-z0-9_]+))?", RegexOptions.Compiled);

	public static List<EstiloDef> Scan(string pastaCode)
	{
		var todos = new List<EstiloDef>();
		foreach (string arq in Directory.GetFiles(pastaCode, "*.dm", SearchOption.AllDirectories))
			Ler(arq, todos);
		return todos;
	}

	private static void Ler(string arq, List<EstiloDef> saida)
	{
		string[] linhas = File.ReadAllLines(arq);
		EstiloDef? atual = null;
		bool comentado = false;

		for (int i = 0; i < linhas.Length; i++)
		{
			string linha = linhas[i];
			string t = linha.Trim();

			// BLOCO COMENTADO: `styletemplate.dm` inteiro vive dentro de um /* */, e o estilo
			// declarado la NAO EXISTE em runtime. Ler o arquivo sem respeitar isso traria um
			// nono estilo fantasma pro catalogo do jogo.
			if (t.StartsWith("/*", StringComparison.Ordinal)) comentado = true;
			if (comentado)
			{
				if (t.Contains("*/", StringComparison.Ordinal)) comentado = false;
				continue;
			}
			if (t.StartsWith("//", StringComparison.Ordinal)) continue;

			Match m = RxEstilo.Match(t);
			if (m.Success)
			{
				// `/datum/style/proc/...` nao e um estilo, e um proc
				string id = m.Groups["id"].Value;
				if (id is "proc" or "New" or "Del") { atual = null; continue; }
				atual = new EstiloDef { Id = id, Nome = id, Arquivo = Path.GetFileName(arq) };
				saida.Add(atual);
				continue;
			}
			// linha sem indentacao e que nao e estilo: fechou o bloco
			if (atual != null && linha.Length > 0 && linha[0] is not ('\t' or ' ')) { atual = null; continue; }
			if (atual == null) continue;

			Match p = RxProp.Match(linha);
			if (!p.Success) continue;
			string chave = p.Groups["k"].Value;
			string valor = p.Groups["v"].Value;
			int c = valor.IndexOf("//", StringComparison.Ordinal);
			if (c >= 0) valor = valor[..c].Trim();

			if (chave == "name") { if (RxStr.Match(valor) is { Success: true } mn) atual.Nome = mn.Groups["s"].Value; }
			else if (chave == "desc") { if (RxStr.Match(valor) is { Success: true } md) atual.Desc = md.Groups["s"].Value; }
			else if (double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
			{
				if (chave == "learncost") atual.LearnCost = v;
				else if (chave == "allocatedpoints") atual.AllocatedPoints = v;
				else if (chave.StartsWith("default", StringComparison.Ordinal)) atual.Defaults[chave] = v;
			}
		}
	}

	public static string ParaJson(IEnumerable<EstiloDef> defs)
	{
		var sb = new StringBuilder("[\n");
		bool primeiro = true;
		foreach (EstiloDef d in defs.OrderBy(e => e.LearnCost).ThenBy(e => e.Id))
		{
			if (!primeiro) sb.Append(",\n");
			primeiro = false;
			sb.Append("  { ");
			sb.Append($"\"id\": {J(d.Id)}, \"nome\": {J(d.Nome)}, \"desc\": {J(d.Desc)}, ");
			sb.Append($"\"custo\": {d.LearnCost.ToString("0.##", CultureInfo.InvariantCulture)}, ");
			sb.Append($"\"pontos\": {d.AllocatedPoints.ToString("0.##", CultureInfo.InvariantCulture)}, ");
			sb.Append($"\"defaults\": [{string.Join(", ", d.Defaults.Select(kv =>
				J($"{kv.Key}={kv.Value.ToString("0.####", CultureInfo.InvariantCulture)}")))}]");
			sb.Append(" }");
		}
		return sb.Append("\n]\n").ToString();
	}

	private static string J(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
