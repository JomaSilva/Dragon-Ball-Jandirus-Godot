using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// OS INTERIORES, UM POR INSTANCIA -- a nave, a mente, o bolso da fusao, o selo, o interior do Majin, a Sala do Tempo
/// de cada mundo, e todo interior que vier depois.
///
/// ============================ A REGRA (dono, 2026-10-11) ============================
/// **ESTADO QUE MUDA DURANTE O JOGO NUNCA E DIVIDIDO ENTRE DUAS INSTANCIAS DE UM INTERIOR.**
///
/// Um interior e `ZoneKey.Interior(nome, id)`: o NOME diz a PLANTA (que mapa e) e o `id` diz QUAL instancia (a nave
/// #7, a mente do jogador #12, a Sala do mundo de seed X). A planta e uma so e IMUTAVEL -- a funcao do Core
/// (`NaveGrande.Planta`, `DimensaoMental.Planta`, `InteriorDoMajin.Planta`) ou o mapa de arquivo do catalogo (o z13
/// da Sala). TUDO o que muda em jogo e da INSTANCIA: o mapa que barra, o mapa que cega, as celulas caidas, as portas
/// abertas.
///
/// ============================ O QUE ACONTECIA (medido pela `--interioresteste`) ============================
/// O funil do mapa (`MapaDaZonaOuCatalogo`, `MapaDaVista`) devolvia a PROPRIA planta pra toda instancia, e o registro
/// do que caiu (`_cenarioCaido`) e do que abriu (`_portasAbertas`) era por NOME de zona -- e o nome e o mesmo pra
/// todas. Entao:
///   * a parede derrubada na nave #7 abria na #8 e em todas, e aparecia caida na tela de quem entrasse em qualquer uma;
///   * o chao rachado numa mente rachava em todas (e o do bolso de uma fusao, em todos os bolsos e no selo);
///   * a parede derrubada na Sala do Tempo de um mundo caia na da TERRA e nas de todos os outros mundos -- o z13 tem
///     168 celulas que barram, e nenhuma delas e dura;
///   * a maquina assentada num interior (`AplicarColisaoDasObras`) virava parede em todos os do mesmo nome, e a
///     seguinte apagava a dela (a camada e refeita inteira a cada vez).
/// O cabecalho de `Core/Tech/NaveGrande.cs` dizia que partilhar era seguro e listava as tres escritas que nao
/// alcancavam a planta. A quarta -- a celula que cai (`AbrirACelulaCaida`) -- alcancava.
///
/// ============================ COMO FICOU ============================
///   * UM FUNIL SO: <see cref="TiposDeInterior"/> diz de que planta sai cada tipo, e <see cref="InstanciaDoInterior"/>
///     entrega a COPIA da instancia (`ZoneCollision.Instanciar`: os planos do arquivo sao os mesmos, as camadas que
///     mudam em jogo sao so dela). `MapaDaZonaOuCatalogo` e `MapaDaVista` passam por aqui pra todo `KindInterior`.
///   * O REGISTRO E POR INSTANCIA: <see cref="ChaveDeEstado"/> no lugar de `zona.Name` em toda colecao que guarda o
///     que mudou.
///   * A COPIA NASCE na primeira pergunta, ja com o que e dela (o que caiu, o que esta aberto, as maquinas de pe).
///     MORRE quando a instancia fica sem ninguem (<see cref="TickDosInteriores"/>) e renasce igual na pergunta
///     seguinte: o que e da instancia mora nas LISTAS, e a copia e so o mapa delas. As listas duram ate o servidor
///     reiniciar, como o estrago de toda zona do jogo.
///   * CUSTO: dois objetos pequenos por instancia viva -- os bitsets nao sao copiados. O numero medido sai no placar
///     da bancada.
///
/// ============================ INTERIOR NOVO? ============================
/// Planta do Core: uma linha em <see cref="TiposDeInterior"/>. Mapa de ARQUIVO: nada a fazer aqui (o ultimo tipo
/// resolve pelo nome no catalogo, e a copia separa as instancias). Nos dois casos, uma linha na vigia
/// (`GameServer.InterioresTeste.cs`) -- ela roda duas instancias de cada tipo, muda uma e confere a outra, e tipo
/// sem linha la e VERMELHO. NUNCA devolva a planta direto a quem vai escrever, e NUNCA guarde por `zona.Name` o que
/// muda em jogo.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada --interioresteste): toda instancia volta a receber a PROPRIA planta, e o que caiu e o
	/// que abriu voltam a ser guardados pelo NOME da zona -- o jogo de antes do conserto. Falso em jogo, sempre.
	/// </summary>
	public static bool EstadoDeZonaPorNomeDeTeste;

	/// <summary>UM TIPO DE INTERIOR: quem e, e de que PLANTA saem os dois mapas dele -- o que barra e o que cega.</summary>
	private sealed record TipoDeInterior(
		string Nome,
		Func<ZoneKey, bool> Eh,
		Func<ZoneKey, (ZoneCollision? Colisao, ZoneCollision? Vista)> Planta);

	/// <summary>O tipo que resolve pelo NOME no catalogo de mapas: o ultimo da lista, e o que serve a todo mapa de arquivo.</summary>
	private const string TipoDeArquivo = "arquivo";

	private TipoDeInterior[]? _tiposDeInterior;

	/// <summary>
	/// OS TIPOS DE INTERIOR, NA ORDEM EM QUE SE PERGUNTA. A ordem importa: o ultimo resolve pelo NOME no catalogo, e
	/// `Interior("Interdimension", id)` casaria la com o z24 do BYOND -- a mente responde antes (ver
	/// `DimensaoMental.Planta`).
	///
	/// OS DOIS MAPAS VEM DA MESMA PLANTA, e e a que o cliente desenha: `Colisao` barra o corpo, `QueEsconde` barra a
	/// vista e a voz. A vista da mente vinha do catalogo PELO NOME -- o `.vis` do z24, 596 celulas que cegam em
	/// x 130..212, y 248..292, num quarto branco que o cliente desenha vazio e sem fim -- e a do Majin e a do selo
	/// nao vinham de lugar nenhum. Agora as quatro plantas do Core respondem como a da nave ja respondia.
	/// </summary>
	private TipoDeInterior[] TiposDeInterior => _tiposDeInterior ??=
	[
		// A MENTE de quem medita: o quarto branco. Com `--menteantiga` ela volta ao z24, pelo ultimo tipo.
		new("mente", z => !MenteAntiga && DimensaoMental.EhAMente(z), _ => DaPlanta(DimensaoMental.Planta())),
		// O BOLSO DA FUSAO E O SELO: o mesmo quarto branco vazio, e por isso a mesma planta. "Selado" nao tem entrada
		// no catalogo -- sem esta linha o passageiro cairia num bolso sem parede nenhuma.
		new("selo", EhOSelo, _ => DaPlanta(DimensaoMental.Planta())),
		// O INTERIOR DE UM MAJIN: o bolsao de carne, inteiro indestrutivel e sob teto (`InteriorDoMajin.Planta`).
		new("majin", InteriorDoMajin.EhOInterior, _ => DaPlanta(InteriorDoMajin.Planta())),
		// O INTERIOR DA NAVE GRANDE (`NaveGrande.Planta`). Sem esta linha ele seria uma zona sem colisao: dava pra sair
		// da nave andando pelo vazio.
		new("nave", z => NaveGrande.EhInterior(z, out _), _ => DaPlanta(NaveGrande.Planta())),
		// O MAPA DE ARQUIVO usado como interior: a Sala do Tempo de um mundo sorteado (o z13) -- e todo interior que
		// nascer de um mapa do catalogo. O catalogo resolve pelo NOME; quem separa as instancias e a copia.
		new(TipoDeArquivo, _ => true, z => (_catalogo?.Get(z)?.Mapa, _catalogo?.Get(z)?.Vista)),
	];

	private static (ZoneCollision? Colisao, ZoneCollision? Vista) DaPlanta(TerrenoGerado planta) =>
		(planta.Colisao, planta.QueEsconde);

	private TipoDeInterior TipoDoInterior(ZoneKey zona)
	{
		TipoDeInterior[] tipos = TiposDeInterior;
		foreach (TipoDeInterior t in tipos) if (t.Eh(zona)) return t;
		return tipos[^1];
	}

	/// <summary>UMA INSTANCIA VIVA de um interior: os dois mapas DELA, e a chave do que ela guarda nas listas.</summary>
	private sealed class InteriorVivo
	{
		public required ZoneKey Zona { get; init; }
		public required string Tipo { get; init; }
		public required string Chave { get; init; }
		public ZoneCollision? Colisao { get; init; }
		public ZoneCollision? Vista { get; init; }
	}

	private readonly Dictionary<ulong, InteriorVivo> _interiores = [];
	private readonly Dictionary<string, InteriorVivo> _interioresPorChave = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// A INSTANCIA DESTE INTERIOR, criada na primeira pergunta: a copia dos dois mapas da planta, ja vestida com o que
	/// as listas guardam dela. E registrada ANTES de ser vestida -- vestir pergunta pelo mapa (o
	/// `AplicarColisaoDasObras` passa pelo funil), e a pergunta tem de achar esta copia, nao fazer outra.
	/// </summary>
	private InteriorVivo InstanciaDoInterior(ZoneKey zona)
	{
		ulong hash = zona.Hash;
		if (_interiores.TryGetValue(hash, out InteriorVivo? viva)) return viva;

		TipoDeInterior tipo = TipoDoInterior(zona);
		(ZoneCollision? colisao, ZoneCollision? vista) = tipo.Planta(zona);
		viva = new InteriorVivo
		{
			Zona = zona,
			Tipo = tipo.Nome,
			Chave = zona.ToString(),   // `nome#id`
			Colisao = colisao?.Instanciar(),
			Vista = vista?.Instanciar(),
		};
		_interiores[hash] = viva;
		_interioresPorChave[viva.Chave] = viva;

		if (_cenarioCaido.TryGetValue(viva.Chave, out HashSet<(int X, int Y)>? caidas))
			foreach ((int x, int y) in caidas) { viva.Colisao?.Abrir(x, y); viva.Vista?.Abrir(x, y); }
		if (_portasAbertas.TryGetValue(viva.Chave, out Dictionary<(int X, int Y), long>? abertas))
			foreach ((int x, int y) in abertas.Keys) { viva.Colisao?.Abrir(x, y); viva.Vista?.Abrir(x, y); }
		AplicarColisaoDasObras(zona);
		return viva;
	}

	/// <summary>O MAPA QUE BARRA nesta instancia. Chamado pelo `MapaDaZonaOuCatalogo`, o funil de "onde ha parede".</summary>
	private ZoneCollision? ColisaoDoInterior(ZoneKey zona) =>
		EstadoDeZonaPorNomeDeTeste ? TipoDoInterior(zona).Planta(zona).Colisao : InstanciaDoInterior(zona).Colisao;

	/// <summary>O MAPA QUE CEGA nesta instancia. Chamado pelo `MapaDaVista`.</summary>
	private ZoneCollision? VistaDoInterior(ZoneKey zona) =>
		EstadoDeZonaPorNomeDeTeste ? TipoDoInterior(zona).Planta(zona).Vista : InstanciaDoInterior(zona).Vista;

	// =====================================================================
	// A CHAVE DO QUE MUDA EM JOGO
	// =====================================================================

	/// <summary>As chaves ja montadas dos mundos sorteados: a pergunta vem do tique, e a string nao e refeita a cada uma.</summary>
	private readonly Dictionary<ulong, string> _chavesDeMundoSorteado = [];

	/// <summary>
	/// A CHAVE DO QUE MUDA EM JOGO NUMA ZONA -- e o que vai em toda colecao que guarda estado por zona
	/// (`_cenarioCaido`, `_portasAbertas`), no lugar de `zona.Name`.
	///
	///   * PLANETA DE ARQUIVO: o NOME, de proposito -- la ele e unico (uma zona por nome) e legivel no log.
	///   * INTERIOR: `nome#id`. O nome e de TODAS as instancias.
	///   * MUNDO SORTEADO: `nome#seed`. O nome sai de `{bioma}-{|Sx| % 1000}{|Sy| % 1000}{k}` e NAO e unico (os
	///     setores espelhados repetem o numero, e a concatenacao e ambigua -- ver `PlanetasMortos`): por nome, o
	///     buraco cavado num mundo abria as mesmas celulas no homonimo dele.
	/// </summary>
	private string ChaveDeEstado(ZoneKey zona)
	{
		if (zona.Kind == ZoneKey.KindPremade || EstadoDeZonaPorNomeDeTeste) return zona.Name;
		if (zona.Kind == ZoneKey.KindInterior) return InstanciaDoInterior(zona).Chave;

		ulong hash = zona.Hash;
		if (!_chavesDeMundoSorteado.TryGetValue(hash, out string? chave))
			_chavesDeMundoSorteado[hash] = chave = zona.ToString();
		return chave;
	}

	/// <summary>
	/// OS DOIS MAPAS DE QUEM TEM ESTA CHAVE DE ESTADO -- pra quem so tem a chave na mao (o relogio das portas guarda
	/// as abertas por ela). Interior vivo: os da instancia. Senao, o catalogo: a chave de um planeta de arquivo e o
	/// nome dele. A instancia que morreu (ficou vazia) nao tem mapa pra mexer, e nao precisa: ela renasce vestida
	/// com o que as listas dizem naquela hora.
	/// </summary>
	private (ZoneCollision? Mapa, ZoneCollision? Vista) MapasDoEstado(string chave) =>
		_interioresPorChave.TryGetValue(chave, out InteriorVivo? viva) ? (viva.Colisao, viva.Vista)
		: _catalogo?.Get(chave) is { } e ? (e.Mapa, e.Vista)
		: (null, null);

	// =====================================================================
	// A COPIA MORRE COM A INSTANCIA VAZIA
	// =====================================================================

	/// <summary>De quanto em quanto tempo as instancias vazias sao varridas.</summary>
	private const long VarreInterioresACadaMs = 60_000;

	private long _interioresVarridosEm;

	/// <summary>
	/// A COPIA DE UM INTERIOR SEM NINGUEM DENTRO E SOLTA. Ela e so o mapa do que as listas guardam
	/// (<see cref="InstanciaDoInterior"/> a refaz igual), e interior e o que mais nasce neste servidor -- uma mente por
	/// jogador que medita, um bolso por fusao, um interior por Majin que absorve.
	/// </summary>
	private void TickDosInteriores()
	{
		long agora = NowMs();
		if (agora - _interioresVarridosEm < VarreInterioresACadaMs) return;
		_interioresVarridosEm = agora;
		SoltarInterioresVazios();
	}

	/// <summary>Solta a copia de toda instancia sem ninguem dentro. Devolve quantas soltou.</summary>
	private int SoltarInterioresVazios()
	{
		List<ulong>? vazias = null;
		foreach ((ulong hash, InteriorVivo _) in _interiores)
			if (!_zones.TryGetValue(hash, out List<ServerPlayer>? gente) || gente.Count == 0) (vazias ??= []).Add(hash);
		if (vazias == null) return 0;

		foreach (ulong hash in vazias)
		{
			_interioresPorChave.Remove(_interiores[hash].Chave);
			_interiores.Remove(hash);
		}
		return vazias.Count;
	}
}
