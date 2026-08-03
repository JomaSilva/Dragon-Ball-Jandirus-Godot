using Jandirus.Core.Stats;

namespace Jandirus.Core.Tech;

/// <summary>Uma coisa que da pra construir no mundo.</summary>
public sealed class Construcao
{
	public string Id = "";
	public string Nome = "";
	public string Desc = "";
	public double Custo;
	public double Tech;
	public string[] Racas = [];
}

/// <summary>Por que uma construcao foi recusada. Tipado pelo mesmo motivo das skills.</summary>
public enum RecusaObra
{
	Pode,
	NaoExiste,
	SemTech,
	SemZeni,
	RacaErrada,
	LugarOcupado,
	ZonaProibida,
}

/// <summary>
/// O CATALOGO DE CONSTRUCOES, extraido dos `obj/Creatables` do DM (comando `tech` do pipeline).
/// 99 entradas com preco em zeni e requisito de tecnologia.
///
/// E A SEGUNDA VIA DE PODER DO JOGO, e por isso ela tem uma moeda propria: o Saiyajin sobe socando
/// pedra, o humano sobe ESTUDANDO, e o que o estudo compra sao coisas no mundo. As duas escalas
/// tem que ser lidas juntas -- o mainframe que transforma um humano em androide pede tech 50 e
/// meio milhao de zeni, que e o equivalente, em horas, a subir uma transformacao.
/// </summary>
public sealed class CatalogoDeObras
{
	private readonly Dictionary<string, Construcao> _porId = new(StringComparer.OrdinalIgnoreCase);

	public int Total => _porId.Count;
	public IEnumerable<Construcao> Todas => _porId.Values;
	public Construcao? Get(string id) => _porId.GetValueOrDefault(id);

	/// <summary>
	/// O QUE ESTE PERSONAGEM CONSEGUE COMPRAR AGORA, e o motivo de cada nao.
	///
	/// Devolve TUDO que a raca permite, inclusive o que nao cabe no bolso -- ver a lista inteira
	/// com "faltam 40 de tecnologia" ao lado e o que transforma tecnologia numa meta. Uma lista
	/// que so mostra o comprável esconde o jogo.
	/// </summary>
	public List<(Construcao Obra, RecusaObra Motivo)> Ofertas(Fighter f, string raca)
	{
		var l = new List<(Construcao, RecusaObra)>();
		foreach (Construcao c in _porId.Values.OrderBy(c2 => c2.Tech).ThenBy(c2 => c2.Custo))
		{
			RecusaObra r = Permitida(c, f, raca);
			if (r == RecusaObra.RacaErrada) continue;   // nem existe pra voce: nao vira linha
			l.Add((c, r));
		}
		return l;
	}

	public static RecusaObra Permitida(Construcao c, Fighter f, string raca)
	{
		if (c.Racas.Length > 0 && !c.Racas.Contains(raca, StringComparer.OrdinalIgnoreCase))
			return RecusaObra.RacaErrada;
		if (f.techskill < c.Tech) return RecusaObra.SemTech;
		if (f.Zeni < c.Custo) return RecusaObra.SemZeni;
		return RecusaObra.Pode;
	}

	public static CatalogoDeObras Parse(string json)
	{
		var cat = new CatalogoDeObras();
		foreach (string bloco in Blocos(json))
		{
			var c = new Construcao
			{
				Id = Str(bloco, "id"),
				Nome = Str(bloco, "nome"),
				Desc = Str(bloco, "desc"),
				Custo = Num(bloco, "custo"),
				Tech = Num(bloco, "tech"),
				Racas = Lista(bloco, "racas"),
			};
			if (c.Id.Length > 0) cat._porId[c.Id] = c;
		}
		return cat;
	}

	// ---- o mesmo leitor de uma pagina do catalogo de skills ----
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
		var sb = new System.Text.StringBuilder();
		for (int k = a + 1; k < bloco.Length; k++)
		{
			if (bloco[k] == '\\' && k + 1 < bloco.Length) { sb.Append(bloco[++k]); continue; }
			if (bloco[k] == '"') break;
			sb.Append(bloco[k]);
		}
		return sb.ToString();
	}

	private static double Num(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return 0;
		int a = bloco.IndexOf(':', i) + 1;
		int b = a;
		while (b < bloco.Length && (char.IsDigit(bloco[b]) || bloco[b] is '.' or '-' or ' ')) b++;
		return double.TryParse(bloco[a..b].Trim(), System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
	}

	private static string[] Lista(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return [];
		int a = bloco.IndexOf('[', i);
		int b = bloco.IndexOf(']', a + 1);
		if (a < 0 || b < 0) return [];
		var l = new List<string>();
		string dentro = bloco[(a + 1)..b];
		int k = 0;
		while (true)
		{
			int q1 = dentro.IndexOf('"', k);
			if (q1 < 0) break;
			int q2 = dentro.IndexOf('"', q1 + 1);
			if (q2 < 0) break;
			l.Add(dentro[(q1 + 1)..q2]);
			k = q2 + 1;
		}
		return [.. l];
	}
}
