using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// AS ARVORES QUE REBROTARAM, NA TELA -- o desenho e a colisao do lado de ca.
///
/// O dono (2026-10-10): *"derrubar arvores no soco [...] essas arvores dps de um tempo voltam ou spawnam em outro
/// local."* Quem decide e o servidor (`GameServer.Lenha.cs`); aqui a lista dele (`GameClient.Rebrotas`) vira tile
/// e parede. A arvore que CAIU nao passa por aqui: ela e estrago da cena, com a poeira e a terra batida de sempre
/// (`World.AplicarEstrago`).
///
/// ============================ O MESMO TILE, NUMA CAMADA IGUAL A DE OBJETOS DO MAPA ============================
/// A rebrota e desenhada com o tile da arvore que caiu -- a fonte e o quadro vem no pacote --, do `tileset.tres`
/// de todas as zonas. E nele que moram a ancora e o deslocamento de cada arvore (a copa pra cima do tronco, a
/// centrada das `/obj/Trees`), entao a que volta fica igual a que o mapa trazia sem nenhuma conta daqui. A
/// camada ORDENA POR Y e e filha direta do `World`, que tambem ordena -- como a `Objetos` da cena e a `Cobertura`
/// do planeta sorteado: quem passa por tras da arvore some atras da copa.
/// ==============================================================================================================
///
/// ============================ ELA NAO MORA NA CENA DA ZONA ============================
/// Pelo motivo dos blocos erguidos (`World._blocosTiles`) e das rochas de minerio (`RochasNaTela`, de que este
/// arquivo copia o molde): a cena e cacheada entre visitas e pintada aos pedacos, e um tile escrito nela teria de
/// ser reescrito a cada pedaco repintado e sobreviveria a saida do planeta -- e a arvore que rebrota nem sempre
/// volta pra celula em que o mapa a tinha. Quem diz quais estao de pe e sempre o servidor.
///
/// NO MAPA DE COLISAO ela e a camada `ZoneCollision.Rebrotar`, a mesma que o servidor escreve: o passo previsto
/// aqui para onde ele para. O mapa dos planetas desenhados a mao e cacheado por sessao, entao a camada e LIMPA
/// quando o chao muda, antes de valer a lista da zona nova.
///
/// UM NODE SO, que vive com o mundo e se resolve sozinho: ouve a lista do cliente (tres eventos, com o `-=` do
/// par) e confere a cada quadro se o chao montado ainda e o que ele pintou. Nenhum ramo do `World.CarregarZona`
/// precisa saber que arvore rebrota.
/// ======================================================================================
/// </summary>
public partial class RebrotasNaTela : TileMapLayer
{
	/// <summary>A camada viva, ou nula antes do mundo. So a bancada pergunta.</summary>
	public static RebrotasNaTela? Instancia { get; private set; }

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diaglenha`): a arvore que rebrota e so desenho -- nao entra no mapa de colisao do
	/// cliente. O corpo preveria o passo pra dentro dela e levaria correcao do servidor a cada tique. Falso em jogo,
	/// sempre. Lido pela propria linha que assenta a rebrota, em <see cref="Por"/>.
	/// </summary>
	public static bool SoDesenhaDeTeste;

	/// <summary>O tileset de todas as zonas -- o mesmo recurso que o `World` segura vivo o processo inteiro.</summary>
	private const string CaminhoDoTileSet = "res://Assets/Maps/tileset.tres";

	/// <summary>A cor da lasca que salta quando a rebrota cai: o bege da madeira, o mesmo padrao da faisca do estrago.</summary>
	private static readonly Color CorDaLasca = new Color(0.55f, 0.45f, 0.34f).Lerp(Colors.White, 0.45f);

	/// <summary>O mapa de colisao em que as rebrotas pintadas estao assentadas. Nulo = nenhum.</summary>
	private ZoneCollision? _mapa;

	/// <summary>De que zona sao as rebrotas pintadas agora. Zero = nenhuma (a lista nao e deste chao).</summary>
	private ulong _zona;

	/// <summary>Quantas rebrotas estao pintadas agora. So pra bancada (`--diaglenha`).</summary>
	public int PintadasDeTeste => GetUsedCells().Count;

	/// <summary>O tile pintado nesta celula (fonte e coordenada), ou nulo. So pra bancada.</summary>
	public (int Fonte, Vector2I Coord)? PintadaDeTeste(int cx, int cy)
	{
		var c = new Vector2I(cx, cy);
		int fonte = GetCellSourceId(c);
		return fonte < 0 ? null : (fonte, GetCellAtlasCoords(c));
	}

	/// <summary>Refaz tudo da lista do cliente (bancada): o mesmo caminho do retrato e da troca de chao.</summary>
	public void RemontarDeTeste() => Remontar();

	public override void _Ready()
	{
		Instancia = this;
		YSortEnabled = true;
		CollisionEnabled = false;
		NavigationEnabled = false;
		TileSet = ResourceLoader.Exists(CaminhoDoTileSet) ? ResourceLoader.Load<TileSet>(CaminhoDoTileSet) : null;
		// A REBROTA BARRA MESMO SEM DESENHO (quem decide e o servidor), e parede invisivel nao pode ser muda.
		if (TileSet == null)
			GD.PushError($"[lenha] sem '{CaminhoDoTileSet}' -- as arvores que rebrotarem vao BARRAR sem aparecer. Rode o Tools/AssetPipeline.");

		// OS TOCOS DAS QUE CAIRAM moram num node proprio, filho deste: sao desenho solto (um recorte por arvore, e nao
		// tile), e como esta camada ordena por Y os filhos dela entram na mesma ordem dos corpos. Ver `TocosNaTela`.
		AddChild(new TocosNaTela { Name = "Tocos" });

		if (GameClient.Instance is { } cli)
		{
			cli.LenhaChegou += Remontar;
			cli.RebrotaNasceu += AoNascer;
			cli.RebrotaCaiu += AoCair;
		}
		Remontar();   // o retrato pode ter chegado antes de o mundo existir (o login)
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
		// O `-=` DO PAR: o `GameClient` sobrevive ao relog e este node nao.
		if (GameClient.Instance is { } cli)
		{
			cli.LenhaChegou -= Remontar;
			cli.RebrotaNasceu -= AoNascer;
			cli.RebrotaCaiu -= AoCair;
		}
		// E O MAPA TAMBEM SOBREVIVE (o cache dos planetas desenhados a mao e da sessao): a camada sai com a tela.
		_mapa?.TombarTudo();
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
	/// REFAZ TUDO da lista do cliente: tira as rebrotas do mapa em que estavam, limpa a camada e -- se a lista e
	/// da zona em que o cliente esta -- pinta e assenta cada uma. Roda no retrato e na troca de chao; a que nasce
	/// ou cai sozinha passa pelo <see cref="AoNascer"/> e pelo <see cref="AoCair"/>.
	///
	/// Entre o `ZoneChanged` e a montagem do planeta novo o chao na tela ainda e o velho (a troca espera a tela
	/// de carregamento): o retrato novo que chega nesse intervalo e pintado ali, por baixo da cortina, e refeito no
	/// mapa certo assim que o chao troca -- o `_Process` ve o mapa mudar.
	/// </summary>
	private void Remontar()
	{
		_mapa?.TombarTudo();
		Clear();
		_mapa = World.Instancia?.Colisao;
		_zona = 0;

		if (GameClient.Instance is not { } cli || _mapa == null || cli.ZonaDaLenha != cli.Zone.Hash) return;

		_zona = cli.ZonaDaLenha;
		foreach (((int x, int y), TileDeArvore tile) in cli.Rebrotas) Por(x, y, tile);
	}

	/// <summary>UMA REBROTA NA TELA E NO MAPA. O funil daqui: o retrato e a que nasce agora passam os dois por ele.</summary>
	private void Por(int cx, int cy, TileDeArvore tile)
	{
		// SEM DESENHO: a paleta do bioma pede um tile que o tileset nao tem (o planeta ja avisou ao nascer). Barra igual.
		if (tile.TemDesenho && TileSet != null) SetCell(new Vector2I(cx, cy), tile.Fonte, new Vector2I(tile.Ax, tile.Ay));
		// A MESMA CAMADA DO SERVIDOR: a previsao do passo para na arvore exatamente onde ele para.
		if (!SoDesenhaDeTeste) _mapa?.Rebrotar(cx, cy);
	}

	/// <summary>O servidor disse que uma arvore voltou a ficar de pe.</summary>
	private void AoNascer(int cx, int cy)
	{
		// SO SE O QUE ESTA PINTADO E DESSA LISTA: senao a montagem aplica a lista inteira, ja com a mudanca.
		if (_zona == 0 || GameClient.Instance is not { } cli || cli.ZonaDaLenha != _zona) return;
		if (cli.Rebrotas.TryGetValue((cx, cy), out TileDeArvore tile)) Por(cx, cy, tile);
	}

	/// <summary>
	/// O servidor disse que uma rebrota saiu. Derrubada, ela solta a poeira e a lasca de toda queda de cenario; o
	/// chao debaixo nao muda -- ou ja era terra batida (a celula de origem), ou nunca foi parede.
	/// </summary>
	private void AoCair(int cx, int cy, bool derrubada)
	{
		if (_zona == 0 || GameClient.Instance is not { } cli || cli.ZonaDaLenha != _zona) return;

		EraseCell(new Vector2I(cx, cy));
		_mapa?.Tombar(cx, cy);
		if (!derrubada || GetParent() is not Node2D mundo) return;

		const int t = ZoneCollision.TileSize;
		var centro = new Vector2(cx * t + t / 2f, cy * t + t / 2f);
		Vector2 rumo = World.Instancia?.PosicaoLocal is { } eu ? (centro - eu).Normalized() : Vector2.Zero;
		PoeiraDeEstrago.Soltar(mundo, centro, rumo);
		CombatFx.Impacto(mundo, centro, 1.2f, CorDaLasca);
	}
}
