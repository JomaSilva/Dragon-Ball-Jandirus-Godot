using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>Uma coisa construivel, do jeito que o jogo precisa saber dela pra vender.</summary>
public sealed class ConstrucaoDef
{
	public string Id = "";          // "Research_Station"
	public string Nome = "";
	public string Desc = "";
	public double Custo = 0;        // `cost`, em zeni
	public double TechNecessario;   // `neededtech`
	public List<string> Racas = []; // `allowedRaces`: o mainframe de androide e so pra Humano
	public string Arquivo = "";     // de onde veio, pra conferir
}

/// <summary>
/// Le as construcoes (`obj/Creatables`) direto do DM.
///
/// MESMO ARGUMENTO DAS SKILLS: sao 110 entradas com custo, requisito de tecnologia e restricao de
/// raca. Transcrever isso a mao erra um zero em algum custo e ninguem descobre ate um jogador
/// comprar um mainframe de 500.000 por 50.000.
///
/// A ARMADILHA AQUI E OUTRA, e vale anotar: em `obj/Creatables` o nome do bloco E o id do item, e
/// os blocos sao aninhados por indentacao SEM typepath (`Research_Station` solto, com as
/// propriedades embaixo). Nao da pra procurar por `/obj/Creatables/...`; o que marca o comeco de
/// uma entrada e "identificador sozinho na linha, um nivel dentro de um bloco Creatables".
/// </summary>
public static class DmTechScanner
{
	private static readonly Regex RxNum = new(@"^-?[0-9.]+$", RegexOptions.Compiled);
	private static readonly Regex RxStr = new("\"(?<s>[^\"]*)\"", RegexOptions.Compiled);

	public static List<ConstrucaoDef> Scan(string pastaCode)
	{
		var todas = new List<ConstrucaoDef>();
		foreach (string arq in Directory.GetFiles(pastaCode, "*.dm", SearchOption.AllDirectories))
			Ler(arq, todas);

		// SO INTERESSA O QUE TEM PRECO OU REQUISITO. `obj/Creatables` tambem e usado como base
		// abstrata e por blocos que so declaram icone; sem preco nem requisito nao ha o que vender.
		return [.. todas.Where(c => c.Custo > 0 || c.TechNecessario > 0)];
	}

	private static void Ler(string arq, List<ConstrucaoDef> saida)
	{
		string[] linhas = File.ReadAllLines(arq);
		int indRaiz = -1;                 // indentacao da linha `obj/Creatables`
		ConstrucaoDef? atual = null;
		int indAtual = -1;

		for (int i = 0; i < linhas.Length; i++)
		{
			string linha = linhas[i].TrimEnd();
			if (linha.Length == 0) continue;

			int ind = 0;
			while (ind < linha.Length && linha[ind] == '\t') ind++;
			string corpo = linha[ind..];
			if (corpo.StartsWith("//", StringComparison.Ordinal)) continue;

			// abriu um bloco de construiveis?
			if (corpo is "obj/Creatables" or "/obj/Creatables") { indRaiz = ind; atual = null; continue; }
			if (indRaiz < 0) continue;
			if (ind <= indRaiz) { indRaiz = -1; atual = null; continue; }

			// uma entrada nova: identificador sozinho, um nivel dentro do bloco
			if (ind == indRaiz + 1 && corpo.All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
			{
				atual = new ConstrucaoDef { Id = corpo, Nome = corpo.Replace('_', ' '), Arquivo = Path.GetFileName(arq) };
				saida.Add(atual);
				indAtual = ind;
				continue;
			}

			if (atual == null || ind <= indAtual) continue;

			int igual = corpo.IndexOf('=');
			if (igual <= 0) continue;
			string chave = corpo[..igual].Trim();
			string valor = corpo[(igual + 1)..].Trim();
			int comentario = valor.IndexOf("//", StringComparison.Ordinal);
			if (comentario >= 0) valor = valor[..comentario].Trim();

			switch (chave)
			{
				case "cost" when RxNum.IsMatch(valor):
					atual.Custo = double.Parse(valor, CultureInfo.InvariantCulture); break;
				case "neededtech" when RxNum.IsMatch(valor):
					atual.TechNecessario = double.Parse(valor, CultureInfo.InvariantCulture); break;
				case "name":
					if (RxStr.Match(valor) is { Success: true } mn) atual.Nome = mn.Groups["s"].Value; break;
				case "desc":
					if (RxStr.Match(valor) is { Success: true } md) atual.Desc = md.Groups["s"].Value; break;
				case "allowedRaces":
					foreach (Match m in RxStr.Matches(valor)) atual.Racas.Add(m.Groups["s"].Value); break;
			}
		}
	}

	public static string ParaJson(IEnumerable<ConstrucaoDef> defs)
	{
		var sb = new StringBuilder("[\n");
		bool primeiro = true;
		foreach (ConstrucaoDef d in defs.OrderBy(c => c.TechNecessario).ThenBy(c => c.Custo))
		{
			if (!primeiro) sb.Append(",\n");
			primeiro = false;
			sb.Append("  { ");
			sb.Append($"\"id\": {J(d.Id)}, \"nome\": {J(d.Nome)}, \"desc\": {J(d.Desc)}, ");
			sb.Append($"\"custo\": {d.Custo.ToString("0.##", CultureInfo.InvariantCulture)}, ");
			sb.Append($"\"tech\": {d.TechNecessario.ToString("0.##", CultureInfo.InvariantCulture)}, ");
			sb.Append($"\"racas\": [{string.Join(", ", d.Racas.Select(J))}]");
			sb.Append(" }");
		}
		return sb.Append("\n]\n").ToString();
	}

	private static string J(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
