using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O CATALOGO DE BLOCOS DO LADO DE CA: o `blocos.json` lido uma vez, e o desenho de cada bloco -- o tile
/// que o mundo pinta e o icone que a barra de construir mostra.
///
/// ============================ POR QUE UM TILESET PROPRIO ============================
/// O tileset do jogo (`Assets/Maps/tileset.tres`) so tem as folhas que algum MAPA usa: os ids de fonte
/// saem da ordem em que o conversor as descobre, e 59 dos tipos de `/turf/build` moram em folhas que
/// nenhum mapa pinta (`Turfs 12`, `Turfs 96`, `FloorsLAWL`...). Registra-las la pediria a conversao
/// cheia dos 40 mapas. Entao o bloco tem o TileSet dele, montado aqui de quem o catalogo aponta: uma
/// fonte por folha, um tile por bloco.
///
/// AS FOLHAS ENTRAM SOB DEMANDA. Sao 30, e carregar todas na chegada ao mundo seria pagar por uma
/// paleta que a maioria dos jogadores nao abre naquela sessao. A folha e lida quando um bloco dela
/// aparece -- no retrato da zona (dentro da carga dela), na barra (ao entrar no modo de construir)
/// ou na paleta (ao abri-la).
/// ===================================================================================
/// </summary>
public static class BlocosNoCliente
{
	private static CatalogoDeBlocos? _catalogo;

	public static CatalogoDeBlocos Catalogo
	{
		get
		{
			if (_catalogo != null) return _catalogo;
			const string cj = "res://Assets/Data/blocos.json";
			_catalogo = CatalogoDeBlocos.Parse(Godot.FileAccess.FileExists(cj) ? Godot.FileAccess.GetFileAsString(cj) : "");
			if (_catalogo.Total == 0) GD.PushWarning("[blocos] sem blocos.json -- rode o AssetPipeline (comando 'blocos')");
			return _catalogo;
		}
	}

	private static TileSet? _tiles;
	private static readonly Dictionary<string, (int Fonte, TileSetAtlasSource Atlas)> _fontes = new(StringComparer.Ordinal);
	private static readonly Dictionary<ushort, AtlasTexture?> _icones = [];

	/// <summary>O TileSet dos blocos. Um so pro processo: a camada do `World` e a de qualquer bancada usam o mesmo.</summary>
	public static TileSet Tiles => _tiles ??= new TileSet { TileSize = new Vector2I(ZoneCollision.TileSize, ZoneCollision.TileSize) };

	private static (int Fonte, TileSetAtlasSource Atlas)? FonteDe(BlocoDef d)
	{
		if (_fontes.TryGetValue(d.Folha, out (int, TileSetAtlasSource) pronta)) return pronta;
		if (!ResourceLoader.Exists(d.Folha) || ResourceLoader.Load<Texture2D>(d.Folha) is not { } tex) return null;

		var atlas = new TileSetAtlasSource { Texture = tex, TextureRegionSize = new Vector2I(d.Lado, d.Lado) };
		int id = Tiles.AddSource(atlas);
		_fontes[d.Folha] = (id, atlas);
		return (id, atlas);
	}

	/// <summary>O tile deste bloco (fonte e coordenada no TileSet dos blocos), ou nulo se a folha nao carregou.</summary>
	public static (int Fonte, Vector2I Coord)? TileDe(BlocoDef d)
	{
		if (FonteDe(d) is not { } f) return null;
		var c = new Vector2I(d.X, d.Y);
		if (!f.Atlas.HasTile(c)) f.Atlas.CreateTile(c);
		return (f.Fonte, c);
	}

	/// <summary>O quadro deste bloco como textura solta: o icone da barra e da paleta, e o fantasma no mouse.</summary>
	public static Texture2D? Icone(BlocoDef d)
	{
		if (_icones.TryGetValue(d.Numero, out AtlasTexture? pronto)) return pronto;
		AtlasTexture? ic = FonteDe(d) is { } f
			? new AtlasTexture { Atlas = f.Atlas.Texture, Region = new Rect2(d.X * d.Lado, d.Y * d.Lado, d.Lado, d.Lado) }
			: null;
		_icones[d.Numero] = ic;
		return ic;
	}
}
