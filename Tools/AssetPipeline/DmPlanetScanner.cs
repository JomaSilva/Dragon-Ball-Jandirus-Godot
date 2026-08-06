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

	/// <summary>O `HasMoon` das areas do DM. Padrao do original e TER lua.</summary>
	public bool TemLua = true;

	/// <summary>O `HasDay` das areas. Falso = noite eterna.</summary>
	public bool TemDia = true;

	/// <summary>O `HasNight` das areas. Falso = sol eterno (Namek).</summary>
	public bool TemNoite = true;

	/// <summary>Duracao da rotacao em segundos reais. NAO vem do DM -- ver `DiaPorNome`.</summary>
	public double SegundosPorDia = Jandirus.Core.World.Ceu.SegundosPorDia;

	/// <summary>O `HasWeather` das areas.</summary>
	public bool TemClima = true;

	/// <summary>O `allowedWeatherTypes` das areas, com os nomes LITERAIS do DM.</summary>
	public List<string> Climas = [];
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

	/// <summary>`switch(z)` -- o caso que distribui gravidade por ANDAR em vez de dar um valor.</summary>
	private static readonly Regex RxAbreSwitchZ = new(
		@"^\s*switch\s*\(\s*z\s*\)\s*$", RegexOptions.Compiled);

	/// <summary>`if(13) Planetgrav=10` -- um andar dentro do `switch(z)`.</summary>
	private static readonly Regex RxAndar = new(
		@"^\s*if\(\s*\d+\s*\)\s*Planetgrav\s*=\s*(?<g>[0-9.]+)", RegexOptions.Compiled);

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

	/// <summary>
	/// QUANTO DURA O DIA DE CADA MUNDO, em minutos reais. NAO VEM DO DM -- la existia UM relogio
	/// global (`Modules/Globals/WorldClock.dm`) e todo planeta obedecia a ele, entao nao ha o que
	/// extrair. O dono pediu horario proprio por planeta, e estes numeros sao escolha nossa.
	///
	/// A TERRA E A REGUA (24 min, o dia padrao do jogo) porque e de la que todo jogador sai; os
	/// outros se afastam dela o bastante pra se notar e nao tanto que uma visita vire so noite.
	/// Quem nao esta na tabela usa o dia padrao.
	/// </summary>
	private static readonly Dictionary<string, double> DiaPorNome = new(StringComparer.OrdinalIgnoreCase)
	{
		["Earth"] = 24,
		["Vegeta"] = 17,            // mundo duro: o dia passa rapido e a noite vem cedo
		["Namek"] = 30,             // nao anoitece (HasNight=0), mas o calendario da lua ainda corre
		["Icer Planet"] = 41,       // rotacao lenta de um mundo gelado
		["Arconia"] = 21,
		["Arlia"] = 15,             // o menor dos pre-feitos: duas noites por dia terrestre
		["Kanzabar"] = 27,
		["Desert"] = 33,            // Vampa
		["Hera"] = 19,
		["Negative Earth"] = 24,    // o espelho da Terra: mesmo relogio
		["Big Gete Star"] = 12,     // maquina em rotacao rapida
		["Makyo Star"] = 48,
	};

	public static List<PlanetaDef> Scan(string pastaCode)
	{
		Dictionary<string, PlanetaDef> achados = Gravidades(pastaCode);

		// O CEU VEM DE OUTRO ARQUIVO, e por isso e um segundo passe: a gravidade mora num
		// `switch(Planet)` em `Gravity.dm` e o `HasMoon`/`HasDay`/`HasNight` moram na arvore de
		// areas de `Areas.dm`. Sao os dois lados da mesma ficha, e o que a une e o nome do planeta.
		//
		// UM PLANETA QUE SO APARECE NAS AREAS ENTRA MESMO ASSIM (Arconia, Kanzabar, Vampa,
		// Terra Negativa): o `switch` da gravidade nao os cita, e o default do proprio DM e 1 --
		// entao a ficha deles e util e nao inventa nada. Arconia, inclusive, e um dos sete
		// mundos que o mapa do universo ja mostra.
		foreach ((string nome, CeuDaArea ceu) in Ceus(pastaCode))
		{
			if (!achados.TryGetValue(nome, out PlanetaDef? d))
				achados[nome] = d = new PlanetaDef
				{
					Nome = nome,
					Gravidade = 1,
					Tipo = TipoPorNome.GetValueOrDefault(nome, "Rochoso"),
				};

			d.TemLua = ceu.Lua;
			d.TemDia = ceu.Dia;
			d.TemNoite = ceu.Noite;
			d.TemClima = ceu.Clima;
			d.Climas = ceu.Climas;
		}

		foreach (PlanetaDef d in achados.Values)
			d.SegundosPorDia = DiaPorNome.GetValueOrDefault(d.Nome,
				Jandirus.Core.World.Ceu.SegundosPorDia / 60) * 60;

		return [.. achados.Values];
	}

	private static Dictionary<string, PlanetaDef> Gravidades(string pastaCode)
	{
		var achados = new Dictionary<string, PlanetaDef>(StringComparer.OrdinalIgnoreCase);
		string arq = Path.Combine(pastaCode, "Modules", "Stats", "BP", "Gravity.dm");
		if (!File.Exists(arq)) return achados;

		string? pendente = null;   // nome visto numa linha, esperando o valor na proxima
		string? porAndar = null;   // nome cujo caso abriu um `switch(z)` -- ver abaixo
		foreach (string linha in File.ReadAllLines(arq))
		{
			// ============================ O CASO QUE ABRE OUTRO SWITCH ============================
			// A Sala do Tempo nao tem `Planetgrav` na linha do caso nem na de baixo: o caso dela abre
			// um `switch(z)` e distribui gravidade por ANDAR (`Gravity.dm:94-99`):
			//
			//     if("Hyperbolic Time Dimension")
            //         switch(z)
            //             if(13) Planetgrav=10   //Sala do Tempo
            //             if(15) Planetgrav=125  //HBTC
            //             ...
			//
			// As duas formas que o laco conhecia falhavam calado, e a Sala entrava na tabela pela
			// varredura de AREAS -- com a gravidade PADRAO, 1. O resultado era a sala mais pesada do
			// jogo rendendo como um parque, e nada acusava.
			//
			// FICA O PRIMEIRO ANDAR, e o comentario diz o que se perdeu: os andares 15-18 (125 a 425)
			// sao os "HBTC" antigos, e NENHUM deles tem mapa no port -- o manifesto so tem o z13. Um
			// dia que tiverem, isto aqui vira uma ficha por andar e o consumidor passa a perguntar
			// pelo z alem do nome.
			// =====================================================================================
			if (porAndar != null)
			{
				Match ma = RxAndar.Match(linha);
				if (ma.Success)
				{
					string alvo = porAndar;
					porAndar = null;
					if (!achados.ContainsKey(alvo))
						achados[alvo] = new PlanetaDef
						{
							Nome = alvo,
							Gravidade = double.Parse(ma.Groups["g"].Value, CultureInfo.InvariantCulture),
							Tipo = TipoPorNome.GetValueOrDefault(alvo, "Rochoso"),
						};
					continue;
				}
				// linha que nao e `switch(z)` nem `if(N) Planetgrav=` encerra a espera
				if (!RxAbreSwitchZ.IsMatch(linha)) porAndar = null;
			}

			if (pendente != null)
			{
				// o caso abriu um `switch(z)` em vez de dar valor: passa a esperar por ANDAR
				if (RxAbreSwitchZ.IsMatch(linha)) { porAndar = pendente; pendente = null; continue; }

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
		return achados;
	}

	// =====================================================================
	// A LUA
	// =====================================================================
	private static readonly Regex RxPlanet = new(
		@"^\s*Planet\s*=\s*""(?<p>[^""]+)""", RegexOptions.Compiled);

	/// <summary>`HasMoon`, `HasDay`, `HasNight`, `AlwaysDay` e `HasWeather` -- as chaves do ceu.</summary>
	private static readonly Regex RxCeu = new(
		@"^\s*(?<k>HasMoon|HasDay|HasNight|AlwaysDay|HasWeather)\s*=\s*(?<v>\d+)", RegexOptions.Compiled);

	/// <summary>`allowedWeatherTypes = list("Rain","Snow")` -- a lista de climas da area.</summary>
	private static readonly Regex RxClimas = new(
		@"^\s*allowedWeatherTypes\s*=\s*list\((?<l>[^)]*)\)", RegexOptions.Compiled);

	private static readonly Regex RxAspas = new("\"([^\"]+)\"", RegexOptions.Compiled);

	/// <summary>Uma linha que ABRE um bloco: so um identificador, sem `=` e sem `(`.</summary>
	private static readonly Regex RxBloco = new(
		@"^\s*(?<n>[A-Za-z_][A-Za-z_0-9/]*)\s*$", RegexOptions.Compiled);

	/// <summary>O ceu de uma area: tem lua, tem dia, tem noite, e que climas caem nela.</summary>
	private record struct CeuDaArea(bool Lua, bool Dia, bool Noite, bool SempreDia,
									bool Clima, List<string> Climas)
	{
		/// <summary>
		/// Os padroes da area base do DM: lua, dia, noite, `AlwaysDay=0`, `HasWeather=1` e o
		/// `allowedWeatherTypes = list("Rain","Snow","Fog")` de `Weather.dm:24`.
		/// </summary>
		public static CeuDaArea Padrao => new(true, true, true, false, true, ["Rain", "Snow", "Fog"]);

		/// <summary>
		/// `AlwaysDay=1` GANHA DE TUDO. As areas do Outro Mundo declaram `HasDay=0 HasNight=0
		/// AlwaysDay=1` -- pelas duas primeiras isso seria crepusculo eterno, o que contradiz o
		/// nome da terceira e o lugar (o Paraiso nao e um fim de tarde permanente). A flag existe
		/// justamente pra resolver esse par, e e ela que vale.
		/// </summary>
		public readonly CeuDaArea Resolvido() => SempreDia ? this with { Dia = true, Noite = false } : this;

		/// <summary>Duas areas do MESMO planeta: vale o que qualquer uma delas ve.</summary>
		public readonly CeuDaArea Ou(CeuDaArea o) =>
			new(Lua || o.Lua, Dia || o.Dia, Noite || o.Noite, SempreDia && o.SempreDia,
				Clima || o.Clima, [.. Climas.Union(o.Climas)]);
	}

	/// <summary>
	/// O CEU DE CADA PLANETA, lido da arvore de areas do DM (`Modules/Turfs/Areas.dm`).
	///
	/// ============================ A ARMADILHA E A HERANCA ============================
	/// A arvore do DM e por INDENTACAO, e as flags DESCEM pros filhos. `afterlifeareas` declara
	/// `HasMoon=0 HasNight=0 HasDay=0 AlwaysDay=1` e nao declara `Planet` nenhum; quem declara sao
	/// os filhos dela -- Outro Mundo, Paraiso e Inferno. Ler linha a linha, sem pilha, daria lua
	/// e noite aos tres.
	///
	/// A SEGUNDA ARMADILHA E A ORDEM. Em Namek o `Planet = "Namek"` vem ANTES do `HasMoon=0` e do
	/// `HasNight=0` (linhas 159, 161 e 162). Resolver no instante em que se ve o nome le o valor
	/// do pai, nao o do proprio bloco -- por isso o planeta so e gravado quando o bloco FECHA.
	///
	/// A TERCEIRA E O IRMAO. Dois blocos irmaos tem corpo na MESMA indentacao: sem zerar o nivel
	/// filho a cada bloco que abre, Vampa herdaria o `HasMoon=0` que o Espaco escreveu logo acima
	/// dela. Nada acusaria -- o deserto so ficaria sem lua pra sempre.
	/// =================================================================================
	///
	/// UM PLANETA COM VARIAS AREAS FICA COM A UNIAO DELAS. No DM cada area corre o proprio
	/// `mooncycle`; aqui um planeta e uma zona so, e a pergunta que sobra e "da pra ver a lua
	/// neste mundo?". A Terra aparece duas vezes (a area dela e o Templo), e as duas veem.
	/// </summary>
	private static Dictionary<string, CeuDaArea> Ceus(string pastaCode)
	{
		var achados = new Dictionary<string, CeuDaArea>(StringComparer.OrdinalIgnoreCase);
		string arq = Path.Combine(pastaCode, "Modules", "Turfs", "Areas.dm");
		if (!File.Exists(arq)) return achados;

		const int max = 64;
		var herdado = new CeuDaArea[max];
		var planetaDoNivel = new string?[max];
		herdado[0] = CeuDaArea.Padrao;

		void Fechar(int nivel)
		{
			if (planetaDoNivel[nivel] is { } p)
			{
				CeuDaArea meu = herdado[nivel].Resolvido();
				achados[p] = achados.TryGetValue(p, out CeuDaArea antes) ? antes.Ou(meu) : meu;
			}
			planetaDoNivel[nivel] = null;
		}

		int anterior = 0;
		foreach (string cru in File.ReadAllLines(arq))
		{
			string linha = SemComentario(cru);
			if (linha.Trim().Length == 0) continue;

			int ind = Math.Min(Indentacao(linha), max - 2);
			for (int d = anterior; d > ind; d--) Fechar(d);
			anterior = ind;

			if (RxPlanet.Match(linha) is { Success: true } mp)
			{
				planetaDoNivel[ind] = mp.Groups["p"].Value;
				continue;
			}

			if (RxCeu.Match(linha) is { Success: true } mc)
			{
				bool v = mc.Groups["v"].Value != "0";
				herdado[ind] = mc.Groups["k"].Value switch
				{
					"HasMoon" => herdado[ind] with { Lua = v },
					"HasDay" => herdado[ind] with { Dia = v },
					"HasNight" => herdado[ind] with { Noite = v },
					"HasWeather" => herdado[ind] with { Clima = v },
					_ => herdado[ind] with { SempreDia = v },
				};
				continue;
			}

			// `allowedWeatherTypes = list()` VAZIO E UMA DECLARACAO, nao um descuido: e assim que
			// a `area/Outside` diz "aqui nao cai nada". Uma lista vazia tem que SUBSTITUIR a
			// herdada, e nao ser ignorada por estar vazia.
			if (RxClimas.Match(linha) is { Success: true } ml)
			{
				var l = new List<string>();
				foreach (Match m in RxAspas.Matches(ml.Groups["l"].Value)) l.Add(m.Groups[1].Value);
				herdado[ind] = herdado[ind] with { Climas = l };
				continue;
			}

			if (RxBloco.IsMatch(linha)) { herdado[ind + 1] = herdado[ind]; planetaDoNivel[ind + 1] = null; }
		}
		for (int d = anterior; d >= 0; d--) Fechar(d);

		return achados;
	}

	private static int Indentacao(string linha)
	{
		int n = 0, i = 0;
		while (i < linha.Length)
		{
			if (linha[i] == '\t') { n++; i++; }
			else if (linha.Length - i >= 4 && linha[i..(i + 4)] == "    ") { n++; i += 4; }
			else break;
		}
		return n;
	}

	/// <summary>Corta `//...`. O `Areas.dm` comenta na ponta da linha em varias declaracoes.</summary>
	private static string SemComentario(string linha)
	{
		int i = linha.IndexOf("//", StringComparison.Ordinal);
		return i < 0 ? linha : linha[..i];
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
			sb.Append($"\"tipo\": {J(d.Tipo)}, ");
			sb.Append($"\"rotacao\": {d.SegundosPorDia.ToString("0.##", CultureInfo.InvariantCulture)}, ");
			sb.Append($"\"dia\": {B(d.TemDia)}, \"noite\": {B(d.TemNoite)}, \"lua\": {B(d.TemLua)}, ");
			sb.Append($"\"temclima\": {B(d.TemClima)}, ");
			sb.Append($"\"climas\": [{string.Join(", ", d.Climas.Select(J))}] }}");
		}
		return sb.Append("\n]\n").ToString();
	}

	private static string J(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

	private static string B(bool v) => v ? "true" : "false";
}
