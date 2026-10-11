using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// OS TOCOS DAS ARVORES QUE CAIRAM, NA TELA.
///
/// O dono (2026-10-10), perguntado se queria um toco no lugar da terra batida: *"sim seria bom um sprite"*. Quem
/// decide onde ha toco e o servidor (`GameServer.Lenha.cs`: a arvore que cai deixa um, e ele some quando ela
/// rebrota ou quando constroem em cima); aqui a lista dele (`GameClient.Tocos`: celula + tile DA ARVORE) vira
/// desenho. E SO DESENHO: o toco nao barra o passo -- a celula ja estava aberta, e e em cima dela que se constroi.
///
/// ============================ O DESENHO SAI DA ARVORE QUE CAIU ============================
/// Nao ha PNG de toco. Cada tipo de arvore tem o toco DELE, tirado da propria arte na primeira vez que uma cai: a
/// base do tronco (raiz, tufo de chao) com o topo do corte por cima -- a conta e a do `Core/World/ArteDoToco.cs`.
/// A branca de Icer deixa um toco de gelo, a de cristal um toco azul, o carvalho um toco largo e nodoso, a bola de
/// Namek um talo fino. O recorte da arvore vem da folha do `tileset.tres` (a mesma textura que ja esta carregada),
/// e o toco fica guardado por tipo pelo processo inteiro: sao 27 tipos em uso, uma leitura de folha por tipo.
///
/// ONDE ELE E POSTO: exatamente onde aqueles pixels estavam na arvore. O `TileMapLayer` desenha o tile com o centro
/// do recorte no centro da celula, menos a `texture_origin` dele; o toco usa a mesma conta mais o canto dele dentro
/// do recorte. A raiz do toco e a raiz da arvore, pixel por pixel -- derrubar e ver rebrotar nao "pula".
/// ==========================================================================================
///
/// FILHO DO `RebrotasNaTela`, que ordena por Y: um `Sprite2D` por toco, na ordem dos corpos (quem passa por tras do
/// toco de um carvalho some atras dele). E `Sprite2D` puro, sem script: o node de vida curta com script C# custa
/// uma recarga do script quando a ultima instancia morre.
///
/// O MESMO MOLDE DA CAMADA DAS REBROTAS: ouve a lista do cliente (tres eventos, com o `-=` do par) e confere a cada
/// quadro se o chao montado ainda e o que ele pintou.
///
/// ============================ UMA ARTE NOVA POR QUADRO, E NUNCA NO QUADRO DA QUEDA ============================
/// Tirar o toco de um tipo de arvore custa uma leitura da folha (da GPU pra memoria) mais a conta: medido na
/// `--diaglenha` com janela, de 5 a 8 ms, uma vez por tipo por processo. O quadro em que a arvore cai ja paga a
/// leitura da MESMA folha pra cor da poeira (`World.CorDoTile`) e solta as particulas; e quem chega numa zona com
/// tocos de cinco tipos pagaria as cinco leituras no quadro em que o chao monta. Entao o toco de um tipo ainda sem
/// arte ESPERA (<see cref="_aEspera"/>): o `_Process` tira uma arte por quadro e poe na tela todos os que a
/// esperavam. O toco de um tipo ja visto entra na hora.
/// ================================================================================================================
/// </summary>
public partial class TocosNaTela : Node2D
{
	/// <summary>O node vivo, ou nulo antes do mundo. So a bancada pergunta.</summary>
	public static TocosNaTela? Instancia { get; private set; }

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diaglenha`): UMA ARTE SO -- todo toco usa o desenho do primeiro tipo de arvore
	/// que caiu. E o "toco de carvalho no lugar da arvore de neve" que o pedido proibe. Falso em jogo, sempre. Lido
	/// pela propria linha que escolhe a arte, em <see cref="ArtePronta"/>.
	/// </summary>
	public static bool UmaArteSoDeTeste;

	/// <summary>
	/// O toco de um tipo de arvore, pronto pra desenhar.
	/// </summary>
	/// <param name="Canto">O canto de cima-esquerda do desenho, a partir do CENTRO da celula.</param>
	/// <param name="Media">A cor media dos pixels opacos -- a poeira nao usa; e a regua da bancada ("bate com a arvore").</param>
	private sealed record Arte(ImageTexture Textura, Vector2 Canto, Color Media, bool Sintetico, int AlturaDoCorte);

	/// <summary>O toco de cada tipo de arvore, pelo tile dela. Nulo = a arte nao deu toco (ja avisou). Do processo inteiro.</summary>
	private static readonly Dictionary<TileDeArvore, Arte?> Artes = [];

	/// <summary>Quanto custou, em ms, a derivacao mais cara ate agora (uma leitura de folha + a conta). So pra bancada.</summary>
	public static double MaiorDerivacaoMsDeTeste { get; private set; }

	private readonly Dictionary<(int X, int Y), Sprite2D> _pintados = [];

	/// <summary>Os tocos que esperam a arte do tipo deles: a celula e o tile da arvore. Ver o cabecalho.</summary>
	private readonly List<((int X, int Y) Onde, TileDeArvore Tile)> _aEspera = [];

	private TileSet? _tiles;

	/// <summary>O chao (mapa de colisao do `World`) sobre o qual os tocos estao pintados. Nulo = nenhum.</summary>
	private ZoneCollision? _chao;

	/// <summary>De que zona sao os tocos pintados agora. Zero = nenhuma (a lista nao e deste chao).</summary>
	private ulong _zona;

	/// <summary>Quantos tocos estao desenhados agora. So pra bancada (`--diaglenha`).</summary>
	public int PintadosDeTeste => _pintados.Count;

	/// <summary>
	/// O toco desenhado nesta celula, ou nulo: o tamanho do desenho, onde fica o canto dele (a partir do centro da
	/// celula), a cor media, e se e o sintetico. So pra bancada.
	/// </summary>
	public (Vector2I Tamanho, Vector2 Canto, Color Media, bool Sintetico, int AlturaDoCorte)? PintadoDeTeste(int cx, int cy)
	{
		if (!_pintados.TryGetValue((cx, cy), out Sprite2D? s) || !IsInstanceValid(s) || s.Texture is not { } t) return null;
		Arte? a = Artes.Values.FirstOrDefault(x => x != null && ReferenceEquals(x.Textura, t));
		return a == null ? null : (new Vector2I(t.GetWidth(), t.GetHeight()), s.Offset, a.Media, a.Sintetico, a.AlturaDoCorte);
	}

	/// <summary>Refaz tudo da lista do cliente (bancada): o mesmo caminho do retrato e da troca de chao.</summary>
	public void RemontarDeTeste() => Remontar();

	public override void _Ready()
	{
		Instancia = this;
		YSortEnabled = true;
		_tiles = (GetParent() as TileMapLayer)?.TileSet;

		if (GameClient.Instance is { } cli)
		{
			cli.LenhaChegou += Remontar;
			cli.TocoNasceu += AoNascer;
			cli.TocoSumiu += AoSumir;
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
			cli.TocoNasceu -= AoNascer;
			cli.TocoSumiu -= AoSumir;
		}
	}

	/// <summary>O chao mudou debaixo da lista (outra zona montada, ou o mesmo planeta com outro mapa)? Quem refaz e o <see cref="Remontar"/>.</summary>
	public override void _Process(double delta)
	{
		if (!ReferenceEquals(World.Instancia?.Colisao, _chao)) Remontar();
		else if (_aEspera.Count > 0) TirarUmaArte();
	}

	/// <summary>
	/// UMA ARTE NOVA, a do primeiro da fila -- e entram na tela todos os que esperavam esse tipo. So entra quem ainda
	/// esta na lista do cliente com o mesmo tile: o toco pode ter sumido (a arvore rebrotou) enquanto esperava.
	/// </summary>
	private void TirarUmaArte()
	{
		TileDeArvore tile = _aEspera[0].Tile;
		Arte? arte = ArteDe(tile);

		for (int i = _aEspera.Count - 1; i >= 0; i--)
		{
			if (_aEspera[i].Tile != tile) continue;
			(int x, int y) = _aEspera[i].Onde;
			_aEspera.RemoveAt(i);
			if (arte != null && GameClient.Instance is { } cli && cli.ZonaDaLenha == _zona
				&& cli.Tocos.TryGetValue((x, y), out TileDeArvore daLista) && daLista == tile)
				Pintar(x, y, arte);
		}
	}

	/// <summary>
	/// REFAZ TUDO da lista do cliente: tira os tocos pintados e -- se a lista e da zona em que o cliente esta -- pinta
	/// cada um. Roda no retrato e na troca de chao; o que nasce ou some sozinho passa pelo <see cref="AoNascer"/> e
	/// pelo <see cref="AoSumir"/>.
	/// </summary>
	private void Remontar()
	{
		foreach (Sprite2D s in _pintados.Values)
			if (IsInstanceValid(s)) s.QueueFree();
		_pintados.Clear();
		_aEspera.Clear();
		_chao = World.Instancia?.Colisao;
		_zona = 0;

		if (GameClient.Instance is not { } cli || _chao == null || cli.ZonaDaLenha != cli.Zone.Hash) return;

		_zona = cli.ZonaDaLenha;
		foreach (((int x, int y), TileDeArvore tile) in cli.Tocos) Por(x, y, tile);
	}

	/// <summary>
	/// UM TOCO NA TELA. O funil daqui: o retrato e o que nasce agora passam os dois por ele. Se a arte do tipo ja foi
	/// tirada ele entra na hora; senao espera a vez (uma arte nova por quadro -- ver o cabecalho).
	/// </summary>
	private void Por(int cx, int cy, TileDeArvore tile)
	{
		if (_pintados.Remove((cx, cy), out Sprite2D? velho) && IsInstanceValid(velho)) velho.QueueFree();

		if (!ArtePronta(tile, out Arte? arte)) { _aEspera.Add(((cx, cy), tile)); return; }
		if (arte != null) Pintar(cx, cy, arte);   // nulo = arvore sem desenho: o toco existe pro servidor e nao aparece
	}

	private void Pintar(int cx, int cy, Arte arte)
	{
		if (_pintados.Remove((cx, cy), out Sprite2D? velho) && IsInstanceValid(velho)) velho.QueueFree();

		const int t = ZoneCollision.TileSize;
		var toco = new Sprite2D
		{
			Texture = arte.Textura,
			Centered = false,
			// A POSICAO E O CENTRO DA CELULA -- o ponto pelo qual o tile da arvore entrava na ordem por Y --, e o
			// desenho e deslocado a partir dela.
			Position = new Vector2(cx * t + t / 2f, cy * t + t / 2f),
			Offset = arte.Canto,
		};
		AddChild(toco);
		_pintados[(cx, cy)] = toco;
	}

	private void AoNascer(int cx, int cy)
	{
		// SO SE O QUE ESTA PINTADO E DESSA LISTA: senao a montagem aplica a lista inteira, ja com a mudanca.
		if (_zona == 0 || GameClient.Instance is not { } cli || cli.ZonaDaLenha != _zona) return;
		if (cli.Tocos.TryGetValue((cx, cy), out TileDeArvore tile)) Por(cx, cy, tile);
	}

	private void AoSumir(int cx, int cy)
	{
		if (_pintados.Remove((cx, cy), out Sprite2D? s) && IsInstanceValid(s)) s.QueueFree();
	}

	/// <summary>A arte deste tipo ja foi tirada? (Nula inclusive: a que nao deu toco tambem fica guardada.)</summary>
	private static bool ArtePronta(TileDeArvore tile, out Arte? arte)
	{
		if (UmaArteSoDeTeste && Artes.Values.FirstOrDefault(a => a != null) is { } aPrimeira) { arte = aPrimeira; return true; }
		return Artes.TryGetValue(tile, out arte);
	}

	/// <summary>
	/// O TOCO DESTE TIPO DE ARVORE, derivado da arte dela na primeira vez e guardado. Nulo = nao ha o que desenhar
	/// (o tile nao esta no tileset, a folha nao tem leitura de CPU, o recorte e vazio) -- avisado uma vez.
	/// </summary>
	private Arte? ArteDe(TileDeArvore tile)
	{
		if (ArtePronta(tile, out Arte? pronta)) return pronta;

		Arte? arte = null;
		ulong t0 = Time.GetTicksUsec();
		try
		{
			var quadro = new Vector2I(tile.Ax, tile.Ay);
			if (tile.TemDesenho && _tiles?.GetSource(tile.Fonte) is TileSetAtlasSource fonte
				&& fonte.Texture is { } folha && fonte.HasTile(quadro))
			{
				Rect2I recorte = fonte.GetTileTextureRegion(quadro);

				// A FOLHA NAO E MINHA: nao se descarta nem se mexe nela. Com tela o `GetImage` devolve uma copia; SEM
				// tela (o renderizador de mentira) ele devolve a propria imagem que guarda, e o C# tem UM involucro
				// por objeto -- descartar aqui matava o involucro que o `World` tinha guardado pra cor da poeira
				// (`_folhaPorFonte`), e a queda seguinte de uma arvore da mesma folha estourava "Cannot access a
				// disposed object". Medido na `--diaglenha` headless. So o que NASCE aqui (o recorte, a copia
				// descomprimida, o toco pronto) e descartado aqui.
				Image? inteira = folha.GetImage();
				if (inteira != null && !inteira.IsEmpty())
				{
					using Image? descomprimida = inteira.IsCompressed() ? (Image)inteira.Duplicate() : null;
					descomprimida?.Decompress();
					using Image daArvore = (descomprimida ?? inteira).GetRegion(recorte);
					daArvore.Convert(Image.Format.Rgba8);

					if (ArteDoToco.Derivar(daArvore.GetData(), recorte.Size.X, recorte.Size.Y) is { } toco)
					{
						using Image doToco = Image.CreateFromData(toco.Largura, toco.Altura, false, Image.Format.Rgba8, toco.Rgba);
						Vector2I origem = fonte.GetTileData(quadro, 0)?.TextureOrigin ?? Vector2I.Zero;
						// O TILE E DESENHADO COM O CENTRO DO RECORTE NO CENTRO DA CELULA, MENOS A ORIGEM DELE
						// (`TileMapLayer`): o toco entra no mesmo lugar, mais o canto dele dentro do recorte.
						Vector2 canto = -(Vector2)recorte.Size / 2f - origem + new Vector2(toco.X, toco.Y);
						arte = new Arte(ImageTexture.CreateFromImage(doToco), canto, MediaOpaca(toco), toco.Sintetico, toco.AlturaDoCorte);
					}
				}
			}
		}
		catch (Exception e)
		{
			GD.PushWarning($"[toco] nao consegui tirar o toco da arte do tile {tile.Fonte}[{tile.Ax},{tile.Ay}]: {e.Message}");
		}

		double ms = (Time.GetTicksUsec() - t0) / 1000.0;
		if (ms > MaiorDerivacaoMsDeTeste) MaiorDerivacaoMsDeTeste = ms;
		// AVISA UMA VEZ POR TIPO (o resultado fica guardado, nulo inclusive). Sem tela nao ha o que avisar: o
		// renderizador de mentira do headless pode nao devolver a folha, e la ninguem ve toco nenhum.
		if (arte == null && tile.TemDesenho && DisplayServer.GetName() != "headless")
			GD.PushWarning($"[toco] a arvore do tile {tile.Fonte}[{tile.Ax},{tile.Ay}] nao deu toco: ela cai e fica so a terra batida.");

		Artes[tile] = arte;
		return arte;
	}

	/// <summary>A cor media dos pixels opacos do toco.</summary>
	private static Color MediaOpaca(TocoDerivado toco)
	{
		double r = 0, g = 0, b = 0;
		int n = 0;
		for (int i = 0; i + 3 < toco.Rgba.Length; i += 4)
		{
			if (toco.Rgba[i + 3] < ArteDoToco.Solido) continue;
			r += toco.Rgba[i];
			g += toco.Rgba[i + 1];
			b += toco.Rgba[i + 2];
			n++;
		}
		return n == 0 ? Colors.Transparent : new Color((float)(r / n / 255), (float)(g / n / 255), (float)(b / n / 255));
	}
}
