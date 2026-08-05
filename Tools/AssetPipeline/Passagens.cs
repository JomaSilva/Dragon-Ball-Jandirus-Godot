namespace Jandirus.Tools;

/// <summary>
/// AS PASSAGENS ENTRE MAPAS -- as celulas que teleportam o jogador de um z-level pra outro.
///
/// ============================ POR QUE ISTO NAO E UMA PORTA ============================
/// O port ja extrai PORTAS (`.portas`): turfs densos que abrem ao encostar e continuam no mesmo
/// mapa. A caverna da Terra, a escada do Templo e a Sala do Tempo sao outra coisa -- elas MUDAM DE
/// MAPA. No BYOND as duas usam o mesmo gancho (`Enter`), e por isso e facil confundi-las; a
/// diferenca e que uma escreve `M.loc = locate(x, y, OUTRO_Z)`.
/// ======================================================================================
///
/// ============================ O DESTINO VEM DE DOIS LUGARES ============================
/// Metade das passagens tem o destino CRAVADO no .dm (`CaveEntrance1` sempre leva a 169,25,10), e
/// a outra metade e um `Special/Teleporter` cujo destino vem nas PROPRIEDADES DA INSTANCIA no
/// .dmm (`{gotox = 354; gotoy = 2; gotoz = 12}`). Um extrator que so olhasse o codigo perderia 21
/// passagens; um que so olhasse o mapa perderia 18.
/// =======================================================================================
/// </summary>
public static class Passagens
{
	/// <summary>Um destino em coordenadas BYOND, como o DM as escreve.</summary>
	public readonly record struct Destino(int X, int Y, int Z, string Nome);

	/// <summary>
	/// AS PASSAGENS DE DESTINO FIXO, transcritas do `Turfs.dm`.
	///
	/// O NOME E O QUE O JOGADOR LE. Tres deles vem do proprio DM (`name = "To Heaven"`); os outros
	/// nao tinham nome nenhum la -- eram celulas mudas que o jogador descobria pisando. Aqui todas
	/// dizem pra onde vao, que era o pedido do dono ("no byond tinha a descriçao pra qual dimensao
	/// cada porta levava").
	/// </summary>
	private static readonly Dictionary<string, Destino> Fixas = new(StringComparer.Ordinal)
	{
		// Outro Mundo -> Céu / Inferno, e a volta
		["/turf/Teleporters/CaveEntrance1"] = new(169, 25, 10, "Céu"),
		["/turf/Teleporters/CaveEntrance2"] = new(64, 297, 9, "Inferno"),
		["/turf/Teleporters/CaveEntrance3"] = new(221, 235, 6, "Outro Mundo"),
		["/turf/Teleporters/CaveEntrance4"] = new(146, 222, 6, "Outro Mundo"),

		// Terra <-> Templo (a torre de Karin sobe pro Lookout)
		["/turf/Teleporters/toeg"] = new(142, 2, 12, "Templo Sagrado"),
		["/turf/Teleporters/fromeg"] = new(128, 162, 1, "Terra"),

		// Sala do Tempo. A ENTRADA fica de fora de proposito -- ver `SoIda`.
		["/turf/Teleporters/fromhbtc"] = new(125, 420, 12, "Templo Sagrado"),

		// Cavernas da Terra
		["/turf/UndergroundCaves/Underground_E_entrance"] = new(156, 299, 23, "Caverna da Terra"),
		["/turf/UndergroundCaves/Underground_E_entrance2"] = new(17, 206, 23, "Caverna da Terra"),
		["/turf/UndergroundCaves/Underground_E_entrance3"] = new(366, 248, 23, "Caverna da Terra"),
		["/turf/UndergroundCaves/Underground_E_Exit"] = new(267, 388, 1, "Terra"),
		["/turf/UndergroundCaves/Underground_E_Exit2"] = new(46, 353, 1, "Terra"),
		["/turf/UndergroundCaves/Underground_E_Exit3"] = new(476, 346, 1, "Terra"),

		// Cavernas de Vegeta
		["/turf/UndergroundCaves/Underground_V_Entrance"] = new(157, 299, 22, "Caverna de Vegeta"),
		["/turf/UndergroundCaves/Underground_V_Exit"] = new(142, 285, 3, "Vegeta"),
	};

	/// <summary>
	/// A PORTA DA SALA DO TEMPO NAO E UMA PASSAGEM COMUM, e por isso ela nao esta na tabela.
	///
	/// No DM ela chama `htc_try_enter()`, que confere permissao do Guardiao e um descanso de 24
	/// horas REAIS antes de deixar entrar -- e a sessao la dentro tem regras proprias (dois anos de
	/// treino, gravidade 10x, saida cronometrada). Porta-la como "pisou, foi" entregaria de graca a
	/// coisa mais cara do jogo.
	///
	/// Ela fica anotada aqui pra a ausencia ser uma DECISAO e nao um esquecimento.
	/// </summary>
	public const string PortaDaSalaDoTempo = "/turf/Teleporters/tohbtc";

	/// <summary>
	/// O NOME CANONICO DE CADA Z-LEVEL -- a tabela do `area_z_num_to_string` (`datatranslators.dm`).
	///
	/// ============================ ELA CONSERTA UMA COLISAO DE NOME ============================
	/// O conversor batiza cada andar pela AREA dominante, e nove andares diferentes tem `/area/Outside`
	/// como area dominante: z11, z12, z15..z18, z22, z23 e z29. Todos viravam a zona "Outside".
	///
	/// E o `ZoneCatalog` guarda UM por nome, o de menor z -- entao z12 (o Templo Sagrado), z22 (a
	/// caverna de Vegeta) e z23 (a caverna da Terra) simplesmente NAO EXISTIAM no catalogo. Eram
	/// mapas convertidos, no disco, inalcancaveis. E sao exatamente os destinos das passagens: sem
	/// isto, a boca da caverna apontaria pra um lugar que o servidor nao sabe carregar.
	///
	/// Por isso o nome generico cede lugar ao canonico. Onde a area ja diz algo ("Earth", "Namek"),
	/// nada muda.
	/// ==========================================================================================
	/// </summary>
	public static string? NomeDoZ(int z) => z switch
	{
		1 => "Earth",
		2 => "Namek",
		3 => "Vegeta",
		4 => "Icer_Planet",
		5 => "Arconia",
		6 => "Afterlife",
		7 => "Sealed_Zone",
		8 => "Desert",
		9 => "Hell",
		10 => "Heaven",
		11 => "Hera",
		12 => "Lookout",
		13 => "Time_Chamber",
		14 => "Makyo_Star",
		19 => "Big_Geti_Star",
		20 => "Negative_Earth",
		21 => "Arlia",
		22 => "Vegeta_Cave",
		23 => "Earth_Cave",
		24 => "Interdimension",
		25 => "Inbetween_Realm",
		26 => "Space",
		27 => "Small_Space_Station",
		28 => "Large_Space_Station",
		_ => null,
	};

	/// <summary>
	/// AREAS QUE NAO NOMEIAM NADA. "Outside" e "Inside" dizem em que lado da parede voce esta, e
	/// nao em que mundo -- e sao as duas que colidem.
	/// </summary>
	public static bool NomeGenerico(string nome) =>
		nome is "Outside" or "Inside" or "Area" or "Unknown";

	/// <summary>Esta celula e uma passagem? Serve pro conversor tira-la do tilemap comum.</summary>
	public static bool Eh(string basePath) =>
		Fixas.ContainsKey(basePath) || basePath == "/turf/Special/Teleporter";

	/// <summary>
	/// O DESTINO DESTA CELULA, ou nulo se ela nao for passagem.
	///
	/// `typepathCompleto` traz as propriedades da instancia (`{gotox = 354; ...}`), que e de onde
	/// sai o destino dos teleportadores parametrizados.
	/// </summary>
	public static Destino? De(string typepathCompleto)
	{
		string bp = DmmMap.BasePath(typepathCompleto);
		if (Fixas.TryGetValue(bp, out Destino fixa)) return fixa;
		if (bp != "/turf/Special/Teleporter") return null;

		int? x = Propriedade(typepathCompleto, "gotox");
		int? y = Propriedade(typepathCompleto, "gotoy");
		int? z = Propriedade(typepathCompleto, "gotoz");

		// SEM DESTINO NAO E PASSAGEM. Ha instancias no mapa sem os tres campos, e teleportar pra
		// (0,0,0) poria o jogador fora do mundo -- pior que a celula nao fazer nada.
		if (x is null || y is null || z is null) return null;

		return new Destino(x.Value, y.Value, z.Value, Rotulo(typepathCompleto));
	}

	/// <summary>
	/// O NOME QUE O MAPA DEU A ESTA INSTANCIA (`{name = "VCastleEntrance1"}`), quando ha um.
	///
	/// Sao nomes de EDITOR ("ECave3.2"), e nao texto de jogador -- mas eles dizem pra onde a coisa
	/// vai melhor que nada, e o servidor os melhora com o nome da zona de destino.
	/// </summary>
	private static string Rotulo(string tp)
	{
		int i = tp.IndexOf("name = \"", StringComparison.Ordinal);
		if (i < 0) return "";
		int ini = i + 8;
		int fim = tp.IndexOf('"', ini);
		return fim < 0 ? "" : tp[ini..fim];
	}

	private static int? Propriedade(string tp, string campo)
	{
		int i = tp.IndexOf(campo + " = ", StringComparison.Ordinal);
		if (i < 0) return null;

		int ini = i + campo.Length + 3;
		int fim = ini;
		while (fim < tp.Length && (char.IsDigit(tp[fim]) || tp[fim] == '-')) fim++;
		return fim > ini && int.TryParse(tp[ini..fim], out int v) ? v : null;
	}
}
