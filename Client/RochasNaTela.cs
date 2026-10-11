using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// AS ROCHAS DE MINERIO, NA TELA -- o desenho, a colisao do lado de ca e o alvo da tecla E.
///
/// ============================ UM TILE POR ROCHA, NUMA CAMADA SO ============================
/// Sao centenas por planeta (ver `GameServer.Rochas.cs` pro porque de nao serem `Obra`), e a tela nao pode
/// pagar um node por rocha. Aqui cada uma e uma celula de um `TileMapLayer` com um tileset de UM tile -- a
/// pedra escura de cristais vermelhos do original (<see cref="RochaDeMinerio.Arte"/>). Nascer e `SetCell`,
/// ser colhida e `EraseCell`.
///
/// A CAMADA ORDENA POR Y e e filha direta do `World`, que tambem ordena: cada rocha entra na ordem dos
/// corpos pela celula dela, como a arvore e a pedra do cenario -- quem esta abaixo passa na frente, quem
/// esta acima fica atras.
/// ===========================================================================================
///
/// ============================ ELA NAO MORA NA CENA DA ZONA ============================
/// Pelo motivo dos blocos erguidos (`World._blocosTiles`): a cena do planeta e cacheada entre visitas e
/// pintada aos pedacos, e a rocha tem estado -- quem diz quais estao de pe e sempre o servidor. E o mapa
/// de colisao dos planetas desenhados a mao e cacheado por sessao: a camada de rochas que esta classe
/// escreve nele (<see cref="ZoneCollision.PorRocha"/>) e LIMPA quando o chao muda, antes de valer a lista
/// da zona nova.
///
/// UM NODE SO, que vive com o mundo e se resolve sozinho: ele ouve a lista do cliente (dois eventos, com o
/// `-=` do par) e confere a cada quadro se o chao montado ainda e o que ele pintou. Nenhum dos cinco ramos
/// do `World.CarregarZona` precisa saber que rochas existem.
/// ======================================================================================
/// </summary>
public partial class RochasNaTela : TileMapLayer
{
	/// <summary>A camada viva, ou nula antes do mundo. Quem pergunta e o menu da tecla E.</summary>
	public static RochasNaTela? Instancia { get; private set; }

	private static TileSet? _tiles;
	private static int _fonte = -1;

	/// <summary>O mapa de colisao em que as rochas pintadas estao assentadas. Nulo = nenhum.</summary>
	private ZoneCollision? _mapa;

	/// <summary>De que zona sao as rochas pintadas agora. Zero = nenhuma (a lista nao e deste chao).</summary>
	private ulong _zona;

	/// <summary>Quantas rochas estao pintadas agora. So pra bancada (`--diagrochas`).</summary>
	public int PintadasDeTeste => GetUsedCells().Count;

	/// <summary>
	/// O TILESET DE UM TILE, montado uma vez por processo. A folha e a mesma `Red Rock.png` que o tileset dos
	/// mapas ja carrega -- nao ha arte nova nem import novo.
	/// </summary>
	private static TileSet? Tiles()
	{
		if (_tiles != null) return _tiles;
		if (!ResourceLoader.Exists(RochaDeMinerio.Arte) || ResourceLoader.Load<Texture2D>(RochaDeMinerio.Arte) is not { } folha)
		{
			// A ROCHA BARRA MESMO SEM DESENHO (quem decide e o servidor), e parede invisivel nao pode ser muda.
			GD.PushError($"[rochas] sem a arte '{RochaDeMinerio.Arte}' -- as rochas de minerio vao BARRAR sem aparecer.");
			return null;
		}

		const int t = ZoneCollision.TileSize;
		var atlas = new TileSetAtlasSource { Texture = folha, TextureRegionSize = new Vector2I(t, t) };
		atlas.CreateTile(Vector2I.Zero);
		_tiles = new TileSet { TileSize = new Vector2I(t, t) };
		_fonte = _tiles.AddSource(atlas);
		return _tiles;
	}

	public override void _Ready()
	{
		Instancia = this;
		YSortEnabled = true;
		CollisionEnabled = false;
		NavigationEnabled = false;
		TileSet = Tiles();

		if (GameClient.Instance is { } cli)
		{
			cli.RochasChegaram += Remontar;
			cli.RochaMudou += AoMudar;
		}
		Remontar();   // o retrato pode ter chegado antes de o mundo existir (o login)
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
		// O `-=` DO PAR: o `GameClient` sobrevive ao relog e este node nao.
		if (GameClient.Instance is { } cli)
		{
			cli.RochasChegaram -= Remontar;
			cli.RochaMudou -= AoMudar;
		}
		// E O MAPA TAMBEM SOBREVIVE (o cache dos planetas desenhados a mao e da sessao): a camada sai com a tela.
		_mapa?.LimparRochas();
		_mapa = null;
	}

	/// <summary>
	/// O CHAO MUDOU DEBAIXO DA LISTA? Outra zona foi montada, ou o mesmo planeta voltou com outro mapa. E uma
	/// comparacao de referencia por quadro; quem refaz e o <see cref="Remontar"/>.
	/// </summary>
	public override void _Process(double delta)
	{
		if (!ReferenceEquals(World.Instancia?.Colisao, _mapa)) Remontar();
	}

	/// <summary>
	/// REFAZ TUDO da lista do cliente: tira as rochas do mapa em que estavam, limpa a camada e -- se a lista
	/// e do chao montado -- pinta e assenta cada uma. Roda no retrato e na troca de chao; a rocha que nasce
	/// ou some sozinha passa pelo <see cref="AoMudar"/>.
	///
	/// A LISTA E DESTE CHAO quando ela e da zona em que o cliente esta. Entre o `ZoneChanged` e a montagem do
	/// planeta novo o chao na tela ainda e o velho (a troca espera a tela de carregamento): se o retrato
	/// novo chega nesse intervalo, ele e pintado ali por baixo da cortina e refeito no mapa certo assim que
	/// o chao troca -- o `_Process` acima ve o mapa mudar.
	/// </summary>
	private void Remontar()
	{
		_mapa?.LimparRochas();
		Clear();
		_mapa = World.Instancia?.Colisao;
		_zona = 0;

		if (GameClient.Instance is not { } cli || _mapa == null || cli.RochasDaZona != cli.Zone.Hash) return;

		_zona = cli.RochasDaZona;
		foreach ((int x, int y) in cli.Rochas) Por(x, y);
	}

	private void Por(int cx, int cy)
	{
		if (_fonte >= 0) SetCell(new Vector2I(cx, cy), _fonte, Vector2I.Zero);
		// A MESMA LINHA DO SERVIDOR: a previsao do passo para na rocha exatamente onde ele para.
		if (_mapa != null) RochaDeMinerio.Assentar(_mapa, cx, cy);
	}

	/// <summary>O servidor disse que uma rocha nasceu ou foi colhida.</summary>
	private void AoMudar(int cx, int cy, bool nasceu)
	{
		// SO SE O QUE ESTA PINTADO E DESSA LISTA: senao a montagem aplica a lista inteira, ja com a mudanca.
		if (_zona == 0 || GameClient.Instance is not { } cli || cli.RochasDaZona != _zona) return;

		if (nasceu) { Por(cx, cy); return; }
		EraseCell(new Vector2I(cx, cy));
		_mapa?.TirarRocha(cx, cy);
	}

	/// <summary>
	/// A ROCHA AO ALCANCE DISPUTA A TECLA E com a mobilia por perto, pela regra de sempre do menu: a mais
	/// perto ganha. Ela entra como uma `ObraInfo` so com tipo, nome e posicao -- e o que o menu le --, e dali
	/// pra frente o caminho e o de qualquer objeto: a dica "[E]", o titulo e os botoes saem do `Interacoes`.
	///
	/// O ALCANCE E O DO CORE (<see cref="RochaDeMinerio.AoAlcance"/>), a conta com que o servidor aceita o verbo.
	/// </summary>
	public static void Disputar(Vector2 eu, ref GameClient.ObraInfo? melhor, ref float melhorDist)
	{
		if (Instancia is not { _zona: not 0 } || GameClient.Instance is not { } cli) return;

		const int t = ZoneCollision.TileSize;
		var corpo = new Vec2(eu.X, eu.Y);
		int raio = Mathf.CeilToInt(Jandirus.Core.Tech.Interacoes.Alcance / t) + 1;
		int cx0 = Mathf.FloorToInt(eu.X / t), cy0 = Mathf.FloorToInt(eu.Y / t);

		for (int dy = -raio; dy <= raio; dy++)
			for (int dx = -raio; dx <= raio; dx++)
			{
				(int X, int Y) c = (cx0 + dx, cy0 + dy);
				if (!cli.Rochas.Contains(c) || !RochaDeMinerio.AoAlcance(corpo, c.X, c.Y)) continue;

				Vec2 p = RochaDeMinerio.Ponto(c.X, c.Y);
				var ponto = new Vector2(p.X, p.Y);
				float d = eu.DistanceSquaredTo(ponto);
				if (d >= melhorDist) continue;
				melhorDist = d;
				melhor = new GameClient.ObraInfo(0, RochaDeMinerio.Tipo, RochaDeMinerio.Nome, ponto,
												 Aparafusada: true, Lab: 0, Dono: "", Arte: "", Estado: "",
												 Pixel: Vector2.Zero, Densa: true);
			}
	}
}
