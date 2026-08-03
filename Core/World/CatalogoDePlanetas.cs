namespace Jandirus.Core.World;

/// <summary>A ficha de um planeta: o que a cena dele carrega e o que o servidor precisa saber.</summary>
public sealed class FichaDePlaneta
{
	public string Nome = "";

	/// <summary>O `Planetgrav` do original. Terra 1, Vegeta 10, Inferno 10, Reino dos Deuses 500.</summary>
	public double Gravidade = 1;

	/// <summary>Jardim, Deserto, Gelado, Rochoso, Vulcanico, Morto. Escolha nossa -- ver o extrator.</summary>
	public string Tipo = "Rochoso";
}

/// <summary>
/// AS FICHAS DOS PLANETAS PRE-FEITOS, extraidas do `switch(Planet)` de `Gravity.dm`.
///
/// POR QUE ISTO EXISTE ALEM DO NODE DA CENA: a cena e a fonte pra quem EDITA (abrir Vegeta no
/// editor e ver "gravidade 10" ali) -- mas o SERVIDOR nao carrega cena nenhuma. Ele roda headless
/// e precisa da mesma resposta. Duas leituras do mesmo dado extraido, cada uma no lado que a usa.
///
/// A GRAVIDADE NAO E ENFEITE: ela entra em <c>Fighter.Planetgrav</c>, que multiplica o ganho de
/// treino e pesa no poder efetivo. Ficou 1 pra todo mundo desde o comeco do port -- ou seja,
/// treinar em Vegeta rendia igual a treinar na Terra, e o unico sinal disso era ninguem reclamar.
/// </summary>
public sealed class CatalogoDePlanetas
{
	private readonly Dictionary<string, FichaDePlaneta> _porNome = new(StringComparer.OrdinalIgnoreCase);

	public int Total => _porNome.Count;
	public IEnumerable<FichaDePlaneta> Todas => _porNome.Values;

	/// <summary>
	/// A ficha da zona. Zona desconhecida devolve gravidade 1 -- e o mesmo default do original
	/// (`Planetgrav = 1` antes do switch), entao um planeta novo sem entrada na tabela nasce
	/// terrestre em vez de nascer quebrado.
	/// </summary>
	public FichaDePlaneta De(string zona) =>
		_porNome.TryGetValue(zona, out FichaDePlaneta? f) ? f : new FichaDePlaneta { Nome = zona };

	public static CatalogoDePlanetas Parse(string json)
	{
		var cat = new CatalogoDePlanetas();
		foreach (string bloco in Blocos(json))
		{
			var f = new FichaDePlaneta
			{
				Nome = Str(bloco, "nome"),
				Gravidade = Num(bloco, "gravidade", 1),
				Tipo = Str(bloco, "tipo"),
			};
			if (f.Nome.Length > 0) cat._porNome[f.Nome] = f;
		}
		return cat;
	}

	private static IEnumerable<string> Blocos(string s)
	{
		int i = 0;
		while (true)
		{
			int a = s.IndexOf('{', i);
			if (a < 0) yield break;
			int b = s.IndexOf('}', a);
			if (b < 0) yield break;
			yield return s[(a + 1)..b];
			i = b + 1;
		}
	}

	private static string Str(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return "";
		int a = bloco.IndexOf('"', bloco.IndexOf(':', i) + 1);
		if (a < 0) return "";
		int b = bloco.IndexOf('"', a + 1);
		return b < 0 ? "" : bloco[(a + 1)..b];
	}

	private static double Num(string bloco, string chave, double padrao)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return padrao;
		int a = bloco.IndexOf(':', i) + 1;
		int b = a;
		while (b < bloco.Length && (char.IsDigit(bloco[b]) || bloco[b] is '.' or '-' or ' ')) b++;
		return double.TryParse(bloco[a..b].Trim(), System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : padrao;
	}
}
