namespace Jandirus.Core.World;

/// <summary>A ficha de um planeta: o que a cena dele carrega e o que o servidor precisa saber.</summary>
public sealed class FichaDePlaneta
{
	public string Nome = "";

	/// <summary>O `Planetgrav` do original. Terra 1, Vegeta 10, Inferno 10, Reino dos Deuses 500.</summary>
	public double Gravidade = 1;

	/// <summary>Jardim, Deserto, Gelado, Rochoso, Vulcanico, Morto. Escolha nossa -- ver o extrator.</summary>
	public string Tipo = "Rochoso";

	/// <summary>
	/// ESTE MUNDO TEM LUA? E o `HasMoon` das areas do DM (`Modules/Turfs/Areas.dm`).
	///
	/// Verdadeiro por padrao porque e o padrao DE LA: a area base nasce com `HasMoon=1` e os
	/// mundos sem lua (Namek, Planeta Icer, o Outro Mundo, o espaco) e que se declaram com
	/// `HasMoon=0`. Um planeta novo sem entrada na tabela nasce com ceu de Terra, e nao mudo.
	/// </summary>
	public bool TemLua = true;

	/// <summary>O `HasDay` das areas. Falso = noite eterna.</summary>
	public bool TemDia = true;

	/// <summary>
	/// O `HasNight` das areas. Falso = sol eterno -- e o caso de NAMEK, que no anime tem tres
	/// sois e nao anoitece. Sem noite nao ha lua no ceu, e portanto nao ha Oozaru.
	/// </summary>
	public bool TemNoite = true;

	/// <summary>
	/// QUANTO DURA UMA ROTACAO DESTE MUNDO, em segundos reais. Zero = usa o dia padrao.
	///
	/// NAO VEM DO DM: la havia um relogio global (`WorldClock.dm`) e todo planeta obedecia a ele.
	/// A duracao propria de cada mundo e escolha nossa, declarada em `DmPlanetScanner.DiaPorNome`.
	/// </summary>
	public double SegundosPorDia;

	/// <summary>O `HasWeather` das areas. Falso = o ceu daqui nunca muda.</summary>
	public bool TemClima = true;

	/// <summary>
	/// OS CLIMAS QUE PODEM CAIR AQUI -- o `allowedWeatherTypes` de cada area do DM, com os nomes
	/// LITERAIS de la ("Rain", "Blood Rain", "Sandstorm"...). Quem traduz pra enum e
	/// `Clima.DoNomeDoDm`.
	///
	/// Guardado como string e nao como enum de proposito: este arquivo e a copia fiel do que foi
	/// extraido. Um nome novo aparecendo no DM tem que chegar aqui inteiro, e nao virar "Limpo"
	/// calado dentro do extrator.
	/// </summary>
	public List<string> Climas = [];
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
		_porNome.TryGetValue(zona, out FichaDePlaneta? f)
		|| _porNome.TryGetValue(Apelido(zona), out f)
			? f
			: new FichaDePlaneta { Nome = zona };

	/// <summary>
	/// O NOME DA ZONA NAO E SEMPRE O NOME DO PLANETA NO DM.
	///
	/// O mapa do universo (<see cref="Espaco.PreFeitos"/>) chama os mundos de `Icer` e
	/// `Makyo_Star`; o `switch(Planet)` do DM os chama de "Icer Planet" e "Makyo Star". A busca
	/// crua falhava calada nos dois e devolvia a ficha padrao -- ou seja, o Planeta Icer treinava
	/// com gravidade 1 em vez de 15, e nada na tela dizia isso.
	/// </summary>
	private static string Apelido(string zona) => zona.Replace('_', ' ') switch
	{
		"Icer" => "Icer Planet",

		// A AREA DO MAPA E O `switch` DO DM ESCREVEM O MESMO LUGAR DE JEITOS DIFERENTES.
		//
		// A Estrela Geti e o caso mais bobo e mais caro: a area do .dmm diz "Geti", o `switch` diz
		// "Gete". Um `i` de diferenca custava 25x de gravidade.
		//
		// A Sala do Tempo tem nome inteiro diferente: a zona sai da area (`Hyperbolic_Time_Chamber`)
		// e o `switch` a chama de "Hyperbolic Time Dimension" -- que e o nome da DIMENSAO, nao o da
		// sala. Medido: as duas caiam na ficha padrao, gravidade 1.
		"Big Geti Star" => "Big Gete Star",
		"Hyperbolic Time Chamber" => "Hyperbolic Time Dimension",

		// O ESPACO EM JOGO NAO SE CHAMA COMO O MAPA. A zona viva e `Espaco`
		// (`Espaco.NomeDoEspaco`, criada por semente e sem arquivo); a ficha do DM se chama "Space",
		// que e o nome do mapa z26. Sem isto, viajar rodava com gravidade 1 -- e o comentario do
		// proprio metodo que le a ficha diz o contrario: "no espaco a gravidade e ZERO, e por isso
		// que ninguem treina viajando".
		"Espaco" => "Space",

		var n => n,
	};

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
				TemLua = Bool(bloco, "lua", true),
				TemDia = Bool(bloco, "dia", true),
				TemNoite = Bool(bloco, "noite", true),
				SegundosPorDia = Num(bloco, "rotacao", 0),
				TemClima = Bool(bloco, "temclima", true),
				Climas = Lista(bloco, "climas"),
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

	/// <summary>
	/// Uma lista de strings: `"climas": ["Rain", "Snow"]`.
	///
	/// O separador de blocos corta por CHAVES (`{}`), e um vetor JSON usa colchetes -- entao a
	/// lista inteira continua dentro do bloco do planeta e da pra ler daqui.
	/// </summary>
	private static List<string> Lista(string bloco, string chave)
	{
		var saida = new List<string>();
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return saida;

		int a = bloco.IndexOf('[', i);
		int b = a < 0 ? -1 : bloco.IndexOf(']', a);
		if (a < 0 || b < 0) return saida;

		for (int p = a + 1; p < b;)
		{
			int ini = bloco.IndexOf('"', p);
			if (ini < 0 || ini > b) break;
			int fim = bloco.IndexOf('"', ini + 1);
			if (fim < 0 || fim > b) break;
			saida.Add(bloco[(ini + 1)..fim]);
			p = fim + 1;
		}
		return saida;
	}

	private static bool Bool(string bloco, string chave, bool padrao)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return padrao;
		int a = bloco.IndexOf(':', i) + 1;
		if (a <= 0) return padrao;
		string resto = bloco[a..].TrimStart();
		if (resto.StartsWith("true", StringComparison.OrdinalIgnoreCase)) return true;
		if (resto.StartsWith("false", StringComparison.OrdinalIgnoreCase)) return false;
		return padrao;
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
