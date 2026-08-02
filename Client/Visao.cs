using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O CAMPO DE VISAO: o que esta atras da parede nao aparece.
///
/// POR QUE ISTO NAO E UMA LUZ. A primeira versao era um <see cref="PointLight2D"/> com sombra
/// preso no personagem, e o dono cortou na hora: "essa luz de visao nao deveria ser uma luz em
/// si". Ele estava certo, e a razao e mecanica, nao de gosto -- uma luz SOMA brilho em tudo
/// que alcanca, inclusive no sprite de quem a carrega. O personagem ficava mais claro que o
/// mundo em volta e lia-se exatamente como o que era: uma lanterna colada nele.
///
/// Aqui e o contrario. O mundo fica com a luz CERTA da hora do dia, do jeito que sempre
/// esteve, e o que se desenha por cima e a ESCURIDAO -- e ela e recortada onde a vista
/// alcanca. Ninguem acende nada; o que existe e um veu que se abre.
///
/// COMO O RECORTE E FEITO. Um leque de raios sai do personagem e anda de 8 em 8 pixels no
/// bitset `.vis` (parede e porta; ver MapConverter) ate bater ou estourar o alcance. Os pontos
/// de parada viram o contorno do que se ve, e a malha desenhada e o COMPLEMENTO disso: um anel
/// de quadrilateros do contorno pra fora, cobrindo a tela inteira.
///
/// A BORDA CONTA UMA HISTORIA. Onde o raio morreu de velho (chegou ao alcance sem bater em
/// nada), a escuridao entra por um degrade -- a vista se perde na distancia. Onde o raio bateu
/// numa parede, o corte e SECO: parede tem beirada. E a mesma malha faz as duas coisas, porque
/// a faixa do degrade tem largura zero quando houve batida.
/// </summary>
public partial class Visao : Node2D
{
	/// <summary>Quem enxerga. Sem alvo o veu nao desenha nada.</summary>
	public Node2D? Alvo;

	/// <summary>
	/// O QUE CEGA: parede e porta fechada. E um bitset proprio (`.vis`), separado do de
	/// colisao -- arvore e cerca barram o passo e NAO barram a vista.
	/// </summary>
	public ZoneCollision? Mapa;

	/// <summary>
	/// Ate onde a vista chega, em pixels. Doze tiles: passa da metade da tela na horizontal,
	/// entao em campo aberto o veu quase nao aparece e o efeito fica sendo o que se pediu --
	/// parede escondendo coisa -- em vez de um circulo de tocha andando com o jogador.
	/// </summary>
	public const float Alcance = 384f;

	/// <summary>Onde comeca o degrade da distancia. Antes disso, luz do dia limpa.</summary>
	private const float Plato = 0.80f;

	/// <summary>
	/// Quantos raios. 128 da um passo de 2,8 graus: a 384 px de distancia dois raios vizinhos
	/// ficam a 9 px um do outro, o que e menos que um tile -- nenhuma quina de parede escapa
	/// pelo meio de dois raios.
	/// </summary>
	private const int Raios = 128;

	/// <summary>Passo da marcha. Um quarto de tile: erra no maximo 8 px na beirada da parede.</summary>
	private const float Passo = 8f;

	/// <summary>A cor do veu. Quase preta e com um pingo de azul, pra nao virar buraco chapado.</summary>
	private static readonly Color Veu = new(0.015f, 0.02f, 0.045f, 0.86f);

	private readonly float[] _dist = new float[Raios];
	private readonly bool[] _bateu = new bool[Raios];
	private ImmediateMesh _malha = null!;
	private Vector2 _ultimaPos = new(float.NaN, float.NaN);
	private Vector2 _ultimaTela;

	public override void _Ready()
	{
		// POR CIMA DE TUDO que e mundo. z_index vence a ordenacao por Y, entao o veu cobre
		// tiles e personagens mesmo com o mundo inteiro em y-sort. O HUD nao entra nisso:
		// e CanvasLayer, e camada de tela, nao de mundo.
		ZIndex = 90;
		// desenha em coordenadas de MUNDO: a malha e montada com posicoes globais
		TopLevel = true;

		// NENHUMA LUZ TOCA O VEU. Sem isto, a fogueira do cenario iluminaria o proprio veu --
		// uma <see cref="PointLight2D"/> aditiva bate em todo CanvasItem que compartilhe a
		// mascara, e o resultado seria um clarao laranja no meio do escuro, como se a
		// escuridao fosse feita de fumaca. O veu e ausencia de visao, nao materia.
		LightMask = 0;

		_malha = new ImmediateMesh();
	}

	public override void _Process(double delta)
	{
		if (Alvo == null || Mapa == null) { Visible = false; return; }
		Visible = true;

		// so refaz o leque quando o personagem realmente andou -- parado, e a mesma malha.
		// O ZOOM e o tamanho da janela tambem entram: os dois mudam ate onde o veu precisa ir,
		// e sem conferir isso um zoom-out deixaria uma moldura clara na beira da tela.
		Vector2 p = Alvo.GlobalPosition;
		Vector2 tela = GetViewportRect().Size / (GetViewport()?.GetCamera2D()?.Zoom ?? Vector2.One);
		if (p.DistanceSquaredTo(_ultimaPos) < 4f && tela.IsEqualApprox(_ultimaTela)) return;
		_ultimaPos = p;
		_ultimaTela = tela;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (Alvo == null || Mapa == null) return;
		Vector2 p = Alvo.GlobalPosition;

		Marchar(p);

		// ATE ONDE O VEU PRECISA IR: o canto mais distante da tela. Fora disso ninguem ve,
		// entao pintar seria desperdicio; aquem disso, apareceria uma moldura clara na beira.
		float longe = Alcance + LimiteDaTela(p);

		_malha.ClearSurfaces();
		_malha.SurfaceBegin(Mesh.PrimitiveType.Triangles);

		var transp = new Color(Veu, 0);
		for (int i = 0; i < Raios; i++)
		{
			int j = (i + 1) % Raios;
			Vector2 di = Dir(i), dj = Dir(j);

			// o ponto onde a vista termina em cada um dos dois raios
			Vector2 ai = p + di * _dist[i], aj = p + dj * _dist[j];

			// e onde o degrade COMECA. Se o raio bateu em parede nao ha degrade: os dois
			// pontos coincidem, a faixa tem area zero e o corte sai seco.
			Vector2 bi = p + di * (_bateu[i] ? _dist[i] : _dist[i] * Plato);
			Vector2 bj = p + dj * (_bateu[j] ? _dist[j] : _dist[j] * Plato);

			// faixa do degrade: limpa por dentro, cheia por fora
			Tri(bi, transp, bj, transp, aj, Veu);
			Tri(bi, transp, aj, Veu, ai, Veu);

			// e o breu daqui pra fora
			Vector2 fi = p + di * longe, fj = p + dj * longe;
			Tri(ai, Veu, aj, Veu, fj, Veu);
			Tri(ai, Veu, fj, Veu, fi, Veu);
		}

		_malha.SurfaceEnd();
		DrawMesh(_malha, null);
	}

	private void Tri(Vector2 a, Color ca, Vector2 b, Color cb, Vector2 c, Color cc)
	{
		_malha.SurfaceSetColor(ca); _malha.SurfaceAddVertex2D(a);
		_malha.SurfaceSetColor(cb); _malha.SurfaceAddVertex2D(b);
		_malha.SurfaceSetColor(cc); _malha.SurfaceAddVertex2D(c);
	}

	private static Vector2 Dir(int i)
	{
		float a = Mathf.Tau * i / Raios;
		return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
	}

	/// <summary>
	/// Anda cada raio ate bater ou cansar.
	///
	/// A CELULA DE ORIGEM E IGNORADA de proposito. Nascer dentro de uma parede acontece (spawn
	/// em cima de uma casa, empurrao de um golpe), e sem esta ressalva TODOS os raios morriam
	/// no primeiro passo: a tela inteira ficava preta e parecia bug de render.
	/// </summary>
	private void Marchar(Vector2 p)
	{
		int ox = (int)MathF.Floor(p.X / ZoneCollision.TileSize);
		int oy = (int)MathF.Floor(p.Y / ZoneCollision.TileSize);

		for (int i = 0; i < Raios; i++)
		{
			Vector2 d = Dir(i);
			float dist = Alcance;
			bool bateu = false;

			for (float t = Passo; t <= Alcance; t += Passo)
			{
				Vector2 q = p + d * t;
				int cx = (int)MathF.Floor(q.X / ZoneCollision.TileSize);
				int cy = (int)MathF.Floor(q.Y / ZoneCollision.TileSize);
				if (cx == ox && cy == oy) continue;
				if (!Mapa!.BlockedCell(cx, cy)) continue;

				// para NA PAREDE, nao antes dela: a face que da pra gente tem que aparecer,
				// senao a casa vira um vulto sem contorno.
				dist = t;
				bateu = true;
				break;
			}

			_dist[i] = dist;
			_bateu[i] = bateu;
		}
	}

	/// <summary>Distancia do personagem ao canto mais longe da tela, em pixels de mundo.</summary>
	private float LimiteDaTela(Vector2 p)
	{
		Camera2D? cam = GetViewport()?.GetCamera2D();
		Vector2 tam = GetViewportRect().Size;
		if (cam != null) tam /= cam.Zoom;
		Vector2 centro = cam?.GetScreenCenterPosition() ?? p;
		return centro.DistanceTo(p) + tam.Length() * 0.5f + 32f;
	}
}
