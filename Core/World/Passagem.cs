namespace Jandirus.Core.World;

/// <summary>
/// UMA CELULA QUE LEVA A OUTRO MAPA -- a boca da caverna, a escada do Templo, a saída do Inferno.
///
/// ============================ ELA NAO E UMA PORTA ============================
/// A porta abre e continua no mesmo lugar; a passagem TROCA DE MUNDO. No BYOND as duas usam o mesmo
/// gancho (`Enter`), e por isso é fácil confundi-las -- a diferença está numa linha:
/// `M.loc = locate(x, y, OUTRO_Z)`.
///
/// O que ela carrega é o DESTINO, e destino não cabe num tile: por isso ela sai do mapa como lista
/// (`.passagens`), do mesmo jeito que as portas e as máquinas.
/// =============================================================================
/// </summary>
public sealed class Passagem
{
	/// <summary>A celula, em coordenadas de tile da zona de ORIGEM.</summary>
	public int X, Y;

	/// <summary>A zona de destino -- a chave do <see cref="ZoneCatalog"/>.</summary>
	public string Zona = "";

	/// <summary>Onde o corpo aparece na zona de destino, em pixels.</summary>
	public float Dx, Dy;

	/// <summary>O que o jogador lê antes de entrar ("Caverna da Terra").</summary>
	public string Nome = "";

	/// <summary>
	/// Le a lista `.passagens` de uma zona.
	///
	/// Parser a mao pelo mesmo motivo do resto do Core: nao ha dependencia externa aqui, e o
	/// formato e nosso -- uma lista plana de objetos com seis campos.
	/// </summary>
	public static List<Passagem> Parse(string json)
	{
		var saida = new List<Passagem>();
		int i = 0;
		while (true)
		{
			int a = json.IndexOf('{', i);
			if (a < 0) break;
			int b = json.IndexOf('}', a);
			if (b < 0) break;

			string bloco = json[(a + 1)..b];
			var p = new Passagem
			{
				X = (int)Num(bloco, "x"),
				Y = (int)Num(bloco, "y"),
				Zona = Str(bloco, "zona"),
				Dx = (float)Num(bloco, "dx"),
				Dy = (float)Num(bloco, "dy"),
				Nome = Str(bloco, "nome"),
			};
			if (p.Zona.Length > 0) saida.Add(p);
			i = b + 1;
		}
		return saida;
	}

	private static string Str(string bloco, string campo)
	{
		int i = bloco.IndexOf($"\"{campo}\"", StringComparison.Ordinal);
		if (i < 0) return "";
		int a = bloco.IndexOf('"', bloco.IndexOf(':', i) + 1);
		if (a < 0) return "";
		int b = bloco.IndexOf('"', a + 1);
		return b < 0 ? "" : bloco[(a + 1)..b];
	}

	private static double Num(string bloco, string campo)
	{
		int i = bloco.IndexOf($"\"{campo}\"", StringComparison.Ordinal);
		if (i < 0) return 0;
		int dp = bloco.IndexOf(':', i);
		int fim = dp + 1;
		while (fim < bloco.Length && (char.IsDigit(bloco[fim]) || bloco[fim] is ' ' or '-' or '.')) fim++;
		return double.TryParse(bloco[(dp + 1)..fim].Trim(),
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
	}
}
