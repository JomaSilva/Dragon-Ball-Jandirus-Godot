using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O FUNDO DO ESPACO -- e ele e o TILE do jogo antigo, nao um desenho meu.
///
/// ============================ POR QUE ISTO FOI REESCRITO ============================
/// A primeira versao inventava um campo de estrelas por codigo: pontos sorteados em tres camadas
/// de paralaxe. Ficava com cara de protetor de tela, e -- pior -- NAO ERA O JOGO. O BYOND ja tem
/// `spacebck.dmi`, com 26 variantes de 32x32 desenhadas a mao, e o espaco inteiro do original e
/// esse tile repetido. Inventar arte quando a arte existe troca uma coisa reconhecivel por uma
/// generica, e joga fora o trabalho de quem desenhou.
/// ===================================================================================
///
/// SO PINTA O QUE A CAMERA VE, que era o pedido explicito. O espaco nao tem fim: pintar "o mapa"
/// e impossivel por definicao. A cada quadro isto olha o retangulo visivel, converte em celulas e
/// pinta so essa faixa, apagando o que saiu. Num monitor comum sao ~1.400 celulas vivas de cada
/// vez, e o custo NAO CRESCE nunca -- da pra voar por milhoes de pixels e continuam 1.400.
///
/// QUAL VARIANTE CAI EM CADA CELULA E FUNCAO PURA de (seed do universo, x, y). Nao ha sorteio em
/// runtime e nao ha memoria: sair voando e voltar mostra o mesmo pedaco de ceu. Sorteado na hora
/// de pintar, o espaco se reembaralharia a cada volta de camera -- e a sensacao de LUGAR, que e o
/// unico ponto de referencia que existe no vazio, iria junto.
/// </summary>
public partial class CeuDoEspaco : Node2D
{
	/// <summary>A folha do original: 26 variantes numeradas de 32x32.</summary>
	private const string Folha = "res://Assets/Sprites/Turfs/spacebck.tres";

	private const int Lado = 32;

	/// <summary>
	/// Quantas variantes usar. A folha tem 26 numeradas mais `speedspace_*`, `damaged` e
	/// `bluespace` -- essas sao de CENARIO (corredor de nave, dano), nao de fundo, e entrariam
	/// como remendo no meio do ceu.
	/// </summary>
	private const int Variantes = 26;

	/// <summary>Folga em celulas: a borda chega pintada em vez de aparecer entrando na tela.</summary>
	private const int Folga = 2;

	public ulong Seed;

	private TileMapLayer _camada = null!;
	private int _colunas = 1;
	private Rect2I _pintado;
	private ulong _seedPintada;

	public override void _Ready()
	{
		// ATRAS DE TUDO: o ceu e fundo, e ate os planetas passam por cima dele.
		ZIndex = -100;
		_camada = new TileMapLayer { Name = "Fundo", TileSet = MontarTileSet(out _colunas) };
		AddChild(_camada);
	}

	/// <summary>
	/// Monta um TileSet de uma fonte so com as 26 variantes.
	///
	/// FEITO POR CODIGO e nao lido de um `.tres` de TileSet porque o `spacebck.tres` do projeto e
	/// um <see cref="SpriteFrames"/> -- o pipeline converte todo .dmi assim, pensando em animacao
	/// de personagem. O que interessa dele e a TEXTURA; a estrutura de tile e outra coisa, e sai
	/// daqui.
	/// </summary>
	private static TileSet MontarTileSet(out int colunas)
	{
		colunas = 1;
		var ts = new TileSet { TileSize = new Vector2I(Lado, Lado) };

		var frames = ResourceLoader.Load<SpriteFrames>(Folha);
		Texture2D? tex = frames != null && frames.HasAnimation("0") && frames.GetFrameCount("0") > 0
						 && frames.GetFrameTexture("0", 0) is AtlasTexture at
			? at.Atlas
			: null;

		if (tex == null)
		{
			GD.PushWarning("[ceu] nao achei a textura do spacebck -- o espaco fica preto");
			return ts;
		}

		var fonte = new TileSetAtlasSource { Texture = tex, TextureRegionSize = new Vector2I(Lado, Lado) };
		colunas = Mathf.Max(1, tex.GetWidth() / Lado);
		for (int i = 0; i < Variantes; i++)
		{
			var c = new Vector2I(i % colunas, i / colunas);
			if (!fonte.HasTile(c)) fonte.CreateTile(c);
		}
		ts.AddSource(fonte, 0);
		return ts;
	}

	public override void _Process(double delta)
	{
		if (!Visible) return;

		Rect2I quer = CelulasVisiveis();
		if (Seed != _seedPintada)
		{
			_camada.Clear();
			_pintado = new Rect2I();
			_seedPintada = Seed;
		}
		else if (quer == _pintado) return;

		// SO O QUE MUDOU. Repintar a tela inteira a cada passo seria refazer 1.400 celulas por
		// quadro pra trocar uma coluna -- e o custo apareceria exatamente quando o jogador esta
		// se movendo, que e quando ele menos pode engasgar.
		for (int y = _pintado.Position.Y; y < _pintado.End.Y; y++)
			for (int x = _pintado.Position.X; x < _pintado.End.X; x++)
				if (!Dentro(quer, x, y)) _camada.EraseCell(new Vector2I(x, y));

		for (int y = quer.Position.Y; y < quer.End.Y; y++)
			for (int x = quer.Position.X; x < quer.End.X; x++)
			{
				if (Dentro(_pintado, x, y)) continue;
				// CAST EXPLICITO e nao acidental: `x` e `y` sao coordenadas de celula e podem ser
				// NEGATIVAS (o espaco tem origem no meio). O cast de `int` negativo pra `ulong`
				// preserva os bits, que e o que o hash quer -- e o mesmo valor nas duas pontas.
				int v = (int)(Espaco.Misturar(Seed, (ulong)(long)x, (ulong)(long)y) % Variantes);
				_camada.SetCell(new Vector2I(x, y), 0, new Vector2I(v % _colunas, v / _colunas));
			}

		_pintado = quer;
	}

	private static bool Dentro(Rect2I r, int x, int y) =>
		x >= r.Position.X && x < r.End.X && y >= r.Position.Y && y < r.End.Y;

	/// <summary>O retangulo de celulas que a camera alcanca, com folga.</summary>
	private Rect2I CelulasVisiveis()
	{
		Camera2D? cam = GetViewport()?.GetCamera2D();
		Vector2 tam = GetViewportRect().Size;
		if (cam != null) tam /= cam.Zoom;
		Vector2 centro = cam?.GetScreenCenterPosition() ?? GlobalPosition;

		var canto = new Vector2I(
			Mathf.FloorToInt((centro.X - tam.X * 0.5f) / Lado) - Folga,
			Mathf.FloorToInt((centro.Y - tam.Y * 0.5f) / Lado) - Folga);
		var lados = new Vector2I(
			Mathf.CeilToInt(tam.X / Lado) + Folga * 2,
			Mathf.CeilToInt(tam.Y / Lado) + Folga * 2);
		return new Rect2I(canto, lados);
	}

	/// <summary>Quantas celulas estao pintadas -- pro diagnostico provar que nao cresce.</summary>
	public int CelulasVivas => Mathf.Max(0, _pintado.Size.X * _pintado.Size.Y);
}

/// <summary>
/// UM PLANETA VISTO DO ESPACO -- com o icone do jogo antigo.
///
/// ============================ POR QUE ISTO FOI REESCRITO ============================
/// A primeira versao desenhava um circulo colorido com halo e crescente de sombra, e o comentario
/// dela justificava a escolha por causa do raio variavel (110 a 200 px). O argumento era fraco:
/// um sprite escala. E `Misc/Planets.dmi` existe desde sempre, com DEZOITO planetas desenhados de
/// 128x128 -- `earth`, `namek`, `vegeta`, `icer_planet`, `arlia`, `hell`, `heaven`,
/// `big_gete_star` e outros. Escolher o icone pelo nome e o que faz a Terra parecer a Terra em
/// vez de um disco verde-agua indistinguivel de qualquer outro mundo verde.
/// ===================================================================================
///
/// PLANETA GERADO CAI NO ICONE DO BIOMA: `jungle`, `desert`, `icer_planet`. A folha nao tem um
/// icone por seed -- nem poderia -- entao o que da pra fazer e escolher o parente mais proximo
/// pelo tipo. Um Jardim sorteado se parece com selva porque ele E uma selva.
/// </summary>
public partial class PlanetaDesenhado : Node2D
{
	private const string Folha = "res://Assets/Sprites/Misc/Planets.tres";

	/// <summary>O lado do quadro na folha. Serve pra converter Raio (px) em escala.</summary>
	private const float LadoDoIcone = 128f;

	public string Nome = "";
	public float Raio = 120;
	public ulong Seed;
	public bool Premade;

	/// <summary>O tipo, quando gerado ("Jardim", "Deserto", "Gelado"...). Vazio nos pre-feitos.</summary>
	public string Tipo = "";

	private Label _rotulo = null!;

	public override void _Ready()
	{
		ZIndex = -60;   // atras dos corpos, na frente do ceu

		AddChild(new Sprite2D
		{
			Name = "Icone",
			Texture = Quadro(EstadoDoIcone()),
			// ESCALA PELO DIAMETRO. O raio vem do servidor em pixels de mundo e e ele que decide a
			// que distancia o pouso acontece; se o desenho nao casar com esse raio, o jogador
			// pousa "no vazio" ao lado de um planeta que parecia estar longe.
			Scale = Vector2.One * (Raio * 2f / LadoDoIcone),
			TextureFilter = TextureFilterEnum.Nearest,
		});

		_rotulo = Tema.Legenda(Nome, Premade ? Tema.Destaque : Tema.TextoFraco, 13);
		_rotulo.Position = new Vector2(-90, -Raio - 34);
		_rotulo.Size = new Vector2(180, 20);
		_rotulo.HorizontalAlignment = HorizontalAlignment.Center;
		AddChild(_rotulo);
	}

	/// <summary>
	/// O estado da folha pra este planeta.
	///
	/// PRE-FEITO CASA PELO NOME (a Terra usa `earth`); GERADO cai no icone do BIOMA. Sem
	/// correspondencia sobra um mundo generico -- nunca um quadrado vazio.
	/// </summary>
	private string EstadoDoIcone()
	{
		if (Premade)
			return Nome.Trim().ToLowerInvariant().Replace(' ', '_') switch
			{
				"earth" => "earth",
				"namek" => "namek",
				"vegeta" => "vegeta",
				"icer_planet" or "icer" => "icer_planet",
				"arlia" => "arlia",
				"arconia" or "acronia" => "arconia",
				"hell" => "hell",
				"heaven" => "heaven",
				"big_gete_star" => "big_gete_star",
				"vampa" => "vampa",
				_ => "jungle",
			};

		return Tipo.Trim().ToLowerInvariant() switch
		{
			"jardim" => "jungle",
			"deserto" => "desert",
			"gelado" => "icer_planet",
			"vulcanico" => "hell",
			"morto" => "big_gete_star",
			_ => "vegeta",   // Rochoso: o mundo de pedra do original
		};
	}

	private static Texture2D? Quadro(string estado)
	{
		var frames = ResourceLoader.Load<SpriteFrames>(Folha);
		if (frames == null) { GD.PushWarning("[planeta] sem Planets.tres"); return null; }
		if (frames.HasAnimation(estado) && frames.GetFrameCount(estado) > 0)
			return frames.GetFrameTexture(estado, 0);

		GD.PushWarning($"[planeta] a folha nao tem o estado '{estado}'");
		return frames.HasAnimation("jungle") && frames.GetFrameCount("jungle") > 0
			? frames.GetFrameTexture("jungle", 0) : null;
	}
}
