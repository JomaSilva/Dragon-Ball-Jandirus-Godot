namespace Jandirus.Core.World;

/// <summary>
/// COM QUE TILE UMA ARVORE SE DESENHA: a fonte do `tileset.tres` e o quadro do atlas -- o trio que o
/// `TileMapLayer` pede. E o que viaja no fio quando uma arvore rebrota (`S2C.Lenha`): o cliente so repete o tile.
/// </summary>
public readonly record struct TileDeArvore(ushort Fonte, ushort Ax, ushort Ay)
{
	/// <summary>
	/// A ARVORE EXISTE E NAO TEM DESENHO: a paleta do bioma pede um tile que o `tileset.tres` nao registra (o planeta
	/// sorteado ja avisa disso ao nascer, e deixa a celula vazia). A arvore continua parando o corpo; quem recebe
	/// este tile nao desenha nada.
	/// </summary>
	public static readonly TileDeArvore SemDesenho = new(ushort.MaxValue, 0, 0);

	public bool TemDesenho => Fonte != ushort.MaxValue;
}

/// <summary>
/// ONDE HA ARVORE NUMA ZONA -- a pergunta que o `.col` nao responde.
///
/// ============================ POR QUE ISTO PRECISOU EXISTIR ============================
/// Pro servidor a arvore era uma celula que bloqueia, igual a um muro: um bit. Bastava enquanto derrubar arvore
/// era so "quebrar cenario". A lenha (ver <see cref="Jandirus.Core.Economia.Lenha"/>) precisa saber QUAL celula e
/// arvore: e ela que paga recurso, que cai em poucos socos e que rebrota.
///
/// DUAS FONTES, UMA RESPOSTA:
///
///   * MAPA PRE-FEITO: a arvore e um tile de uma folha da pasta `Sprites/Trees/` (o `Icons/Trees/` do DM, onde
///     moram as artes de `/obj/Trees` e as arvores de `turf/decor`) -- ou um dos poucos quadros de arvore que o DM
///     desenhou em folha de TURF (<see cref="ArvoresEmFolhaDeTurf"/>) --, numa celula que o `.col` marca. Sai do
///     `.pedacos` -- o mesmo arquivo que o cliente pinta -- cruzado com o `tiles.json`. Medido em 2026-10-10: Terra
///     327 (21 de turf), Namek 350 (65), Icer 497 (216: a floresta congelada), Vegeta 63, Arconia 1343, Makyo 919,
///     Arlia 0. As plantacoes (trigo, cenoura, beterraba) moram na pasta das arvores e ficam de fora sozinhas: nao
///     bloqueiam.
///
///   * PLANETA SORTEADO: a cobertura `Arvore`/`ArvoreAcento` do <see cref="GeradorDeTerreno"/>. O servidor joga o
///     `TerrenoGerado` fora depois de gerar (so guarda a colisao); o que fica daqui sao dois bits por celula.
///
/// A MACIEIRA NAO ESTA AQUI: ela e uma construcao (`Obra` "AppleTree", com fruta e, quando plantada, dono), o
/// conversor a tira do tilemap, e quem a derruba e a regra de armadura das obras.
/// =====================================================================================
/// </summary>
public sealed class ArvoresDoMapa
{
	/// <summary>
	/// A pasta das folhas de arvore, como aparece no `res` do `tiles.json`. Sem diferenca de caixa: uma das artes
	/// veio de `Images/trees/`.
	/// </summary>
	public const string PastaDasArvores = "/trees/";

	/// <summary>
	/// AS ARVORES QUE O DM DESENHOU EM FOLHA DE TURF (atlas, estado), e que a pasta nao acha: `turf/decor` de UM tile,
	/// um quadro so, cada celula uma arvore inteira com tronco e copa -- a branca de Icer (`frozentree`), a azul de
	/// Namek (`smalltree`) e a verde (`treeb`). Medido em 2026-10-10 nos planetas: nenhuma tem pedaco em cima nem
	/// divide a celula com arvore da pasta. Arvore de turf nova no mapa entra AQUI.
	///
	/// Pro jogador sao arvores como as outras -- e sem esta lista quase metade das de Icer (216 de 497) seguia sendo
	/// parede: nao rendia nada, nao rebrotava, e o novato nem a derrubava.
	/// </summary>
	public static readonly (string Atlas, string Estado)[] ArvoresEmFolhaDeTurf =
		[("Turfs 1", "frozentree"), ("Turfs 1", "smalltree"), ("Turfs 2", "treeb")];

	/// <summary>
	/// DEFEITO INJETADO (bancada `--lenhateste`): so a PASTA diz o que e arvore -- as de folha de turf voltam a ser
	/// parede comum. Lido pela propria linha que as reconhece, em <see cref="DoArquivo"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool SoAPastaDeTeste;

	private readonly int _w, _h;

	// mapa pre-feito: poucas centenas de arvores, cada uma com o tile dela
	private readonly Dictionary<int, TileDeArvore>? _doArquivo;

	// planeta sorteado: ate dezenas de milhares, e so duas especies -- um bit pra "ha arvore", outro pra "e a de acento"
	private readonly byte[]? _ha, _acento;
	private readonly TileDeArvore _principal, _deAcento;

	/// <summary>Quantas arvores esta zona tem (de pe ou nao: e o que o mapa trouxe).</summary>
	public int Total { get; }

	private ArvoresDoMapa(int w, int h, Dictionary<int, TileDeArvore> doArquivo)
	{
		_w = w; _h = h;
		_doArquivo = doArquivo;
		Total = doArquivo.Count;
	}

	private ArvoresDoMapa(int w, int h, byte[] ha, byte[] acento, TileDeArvore principal, TileDeArvore deAcento, int total)
	{
		_w = w; _h = h;
		_ha = ha; _acento = acento;
		_principal = principal; _deAcento = deAcento;
		Total = total;
	}

	/// <summary>ESTA CELULA NASCEU ARVORE? Devolve tambem o tile com que ela se desenha.</summary>
	public bool Tem(int cx, int cy, out TileDeArvore tile)
	{
		tile = default;
		if (cx < 0 || cy < 0 || cx >= _w || cy >= _h) return false;
		int i = cy * _w + cx;

		if (_doArquivo != null) return _doArquivo.TryGetValue(i, out tile);

		if ((_ha![i >> 3] & (1 << (i & 7))) == 0) return false;
		tile = (_acento![i >> 3] & (1 << (i & 7))) != 0 ? _deAcento : _principal;
		return true;
	}

	/// <summary>Todas as celulas de arvore, na ordem do mapa (linha a linha). So pra bancada e pra diagnostico.</summary>
	public IEnumerable<(int X, int Y)> Todas()
	{
		if (_doArquivo != null)
		{
			foreach (int i in _doArquivo.Keys.Order()) yield return (i % _w, i / _w);
			yield break;
		}
		for (int i = 0; i < _w * _h; i++)
			if ((_ha![i >> 3] & (1 << (i & 7))) != 0) yield return (i % _w, i / _w);
	}

	/// <summary>Esta folha do tileset e TODA de arvore? A regra e a PASTA -- ver o cabecalho da classe.</summary>
	public static bool FolhaDeArvore(AtlasDeTiles atlas) =>
		atlas.ResPath.Contains(PastaDasArvores, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// AS ARVORES DE UM MAPA PRE-FEITO: toda celula que o arquivo de colisao marca e em que alguma camada desenha
	/// um tile de arvore -- de folha da pasta, ou um dos quadros de <see cref="ArvoresEmFolhaDeTurf"/>. Com duas
	/// camadas na mesma celula, vale a de cima (a ordem do arquivo e a de desenho).
	/// </summary>
	/// <param name="mapa">A colisao da MESMA zona -- so o bit cru do arquivo e lido (`BloqueadaNoArquivo`).</param>
	public static ArvoresDoMapa DoArquivo(PedacosDoMapa pedacos, CatalogoDeTiles tiles, ZoneCollision mapa)
	{
		// POR FONTE, num vetor: o chao de um mapa tem 250 mil celulas, e todas passam por esta pergunta.
		int maior = 0;
		foreach (AtlasDeTiles a in tiles.Todos) maior = Math.Max(maior, a.Fonte);
		var ehArvore = new bool[maior + 1];
		foreach (AtlasDeTiles a in tiles.Todos)
			if (a.Fonte >= 0 && FolhaDeArvore(a)) ehArvore[a.Fonte] = true;

		// AS DE FOLHA DE TURF: a folha inteira nao e de arvore, so um quadro dela. O vetor diz em que fontes vale a
		// pena perguntar pelo quadro -- o resto do mapa nao paga a consulta.
		var temQuadro = new bool[maior + 1];
		var quadros = new HashSet<(int Fonte, int Ax, int Ay)>();
		if (!SoAPastaDeTeste)
			foreach ((string atlas, string estado) in ArvoresEmFolhaDeTurf)
				if (tiles.Achar(atlas, estado) is { } q && q.Fonte >= 0 && q.Fonte <= maior)
				{
					temQuadro[q.Fonte] = true;
					quadros.Add((q.Fonte, q.X, q.Y));
				}

		var achadas = new Dictionary<int, TileDeArvore>();
		for (int c = 0; c < pedacos.Camadas.Length; c++)
			for (int py = pedacos.Cy0; py < pedacos.Cy1; py++)
				for (int px = pedacos.Cx0; px < pedacos.Cx1; px++)
				{
					if (!pedacos.Achar(px, py, c, out int inicio, out int quantas)) continue;
					for (int i = 0; i < quantas; i++)
					{
						CelulaDePedaco cel = pedacos.Celula(inicio, i);
						if (cel.Fonte >= ehArvore.Length) continue;
						if (!ehArvore[cel.Fonte]
							&& !(temQuadro[cel.Fonte] && quadros.Contains((cel.Fonte, cel.Ax, cel.Ay)))) continue;
						if (!mapa.BloqueadaNoArquivo(cel.X, cel.Y)) continue;
						achadas[cel.Y * mapa.Width + cel.X] = new TileDeArvore(cel.Fonte, cel.Ax, cel.Ay);
					}
				}

		return new ArvoresDoMapa(mapa.Width, mapa.Height, achadas);
	}

	/// <summary>
	/// AS ARVORES DE UM PLANETA SORTEADO: a cobertura que o gerador pos. Os dois tiles saem da paleta do bioma,
	/// resolvidos pelo mesmo indice que o cliente usa pra pintar (<see cref="CatalogoDeTiles.Achar(TileVisual)"/>).
	/// </summary>
	public static ArvoresDoMapa DoTerreno(TerrenoGerado t, CatalogoDeTiles tiles)
	{
		int n = t.Largura * t.Altura;
		var ha = new byte[(n + 7) / 8];
		var acento = new byte[(n + 7) / 8];
		int total = 0;

		for (int i = 0; i < n; i++)
		{
			var cob = (CoberturaDeTerreno)t.Cobertura[i];
			if (cob is not (CoberturaDeTerreno.Arvore or CoberturaDeTerreno.ArvoreAcento)) continue;
			ha[i >> 3] |= (byte)(1 << (i & 7));
			if (cob == CoberturaDeTerreno.ArvoreAcento) acento[i >> 3] |= (byte)(1 << (i & 7));
			total++;
		}

		return new ArvoresDoMapa(t.Largura, t.Altura, ha, acento,
								 Resolver(tiles, t.Paleta.Arvore), Resolver(tiles, t.Paleta.ArvoreAcento), total);
	}

	private static TileDeArvore Resolver(CatalogoDeTiles tiles, TileVisual visual) =>
		tiles.Achar(visual) is { } t ? new TileDeArvore((ushort)t.Fonte, (ushort)t.X, (ushort)t.Y) : TileDeArvore.SemDesenho;
}
