using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O FUNDO DE ESTRELAS, GERADO POR CHUNK.
///
/// O dono foi direto ao ponto: "o fundo pode ser o mesmo, mas ele vai gerando conforme o player
/// avança pelas chunks, pra não ter que renderizar bilhões de tiles". E e exatamente isso -- o
/// universo nao tem mapa de estrelas em lugar nenhum. Cada chunk sorteia as suas a partir do
/// PROPRIO id (mais a seed do mundo), entao:
///
///   * a mesma chunk tem sempre as mesmas estrelas, em qualquer maquina e em qualquer sessao;
///   * voltar pra tras mostra o mesmo ceu, sem nada ter sido guardado;
///   * o custo nao cresce com o tamanho do universo, so com o tamanho da TELA.
///
/// TRES CAMADAS COM PARALAXE. Estrelas todas no mesmo plano leem como papel de parede; com as
/// distantes andando devagar e as proximas rapido, o olho entende que ha PROFUNDIDADE e que o
/// corpo esta se movendo de verdade -- que e a unica coisa que da sensacao de velocidade num
/// vazio sem referencia.
/// </summary>
public partial class CeuDoEspaco : Node2D
{
	/// <summary>Quantas estrelas por chunk, por camada. Poucas: o vazio precisa parecer vazio.</summary>
	private static readonly int[] PorCamada = [26, 16, 9];

	/// <summary>O quanto cada camada acompanha a camera. 1 = fixa no mundo.</summary>
	private static readonly float[] Paralaxe = [0.35f, 0.65f, 1.0f];

	private static readonly Color[] Cores =
	[
		new(0.62f, 0.66f, 0.82f, 0.55f),   // longe: apagadas e azuladas
		new(0.82f, 0.85f, 0.95f, 0.75f),
		new(1.00f, 0.98f, 0.92f, 0.95f),   // perto: brancas e vivas
	];

	public ulong Seed;

	private Vector2 _ultimoCentro = new(float.NaN, float.NaN);

	public override void _Ready()
	{
		// FUNDO DE TUDO, e abaixo ate do chao de reserva do World (-100).
		ZIndex = -200;
		TopLevel = true;
	}

	public override void _Process(double delta)
	{
		Vector2 centro = CentroDaTela();
		// meio tile de folga: redesenhar por pixel andado nao muda nada na tela
		if (centro.DistanceSquaredTo(_ultimoCentro) < 256f) return;
		_ultimoCentro = centro;
		QueueRedraw();
	}

	public override void _Draw()
	{
		Vector2 centro = CentroDaTela();
		Vector2 tam = GetViewportRect().Size / (GetViewport()?.GetCamera2D()?.Zoom ?? Vector2.One);

		// o fundo E preto: sem isto o cinza do motor aparece entre as estrelas
		DrawRect(new Rect2(centro - tam * 0.6f, tam * 1.2f), new Color(0.02f, 0.02f, 0.05f));

		for (int camada = 0; camada < PorCamada.Length; camada++)
		{
			// PARALAXE POR DESLOCAMENTO DE AMOSTRAGEM: em vez de mover as estrelas (o que as
			// faria sair da propria chunk e o ceu deixaria de ser estavel), desloca-se QUAL
			// pedaco do ceu se le. O ceu continua preso ao mundo; muda o recorte.
			Vector2 olho = centro * Paralaxe[camada];

			int cx0 = (int)MathF.Floor((olho.X - tam.X * 0.6f) / Espaco.ChunkPx);
			int cy0 = (int)MathF.Floor((olho.Y - tam.Y * 0.6f) / Espaco.ChunkPx);
			int cx1 = (int)MathF.Floor((olho.X + tam.X * 0.6f) / Espaco.ChunkPx);
			int cy1 = (int)MathF.Floor((olho.Y + tam.Y * 0.6f) / Espaco.ChunkPx);

			for (int cy = cy0; cy <= cy1; cy++)
				for (int cx = cx0; cx <= cx1; cx++)
					DesenharChunk(cx, cy, camada, centro - olho);
		}
	}

	/// <summary>
	/// As estrelas de UMA chunk. Tudo sai do hash de (seed, chunk, camada, indice) -- nao ha
	/// estado, nao ha lista, nao ha alocacao por quadro.
	/// </summary>
	private void DesenharChunk(int cx, int cy, int camada, Vector2 desvio)
	{
		ulong baseHash = Espaco.Misturar(Seed + (ulong)camada * 7919UL, (ulong)(uint)cx, (ulong)(uint)cy);
		Vector2 canto = new(cx * (float)Espaco.ChunkPx, cy * (float)Espaco.ChunkPx);
		Color cor = Cores[camada];

		for (int i = 0; i < PorCamada[camada]; i++)
		{
			ulong h = Espaco.Misturar(baseHash, (ulong)i, 0x5bf03635UL);
			var p = new Vector2(
				canto.X + (h & 0xFFFF) / 65535f * Espaco.ChunkPx,
				canto.Y + ((h >> 16) & 0xFFFF) / 65535f * Espaco.ChunkPx) + desvio;

			// tamanho e brilho variam: um campo de pontos iguais le como ruido, nao como ceu
			float raio = 0.7f + (camada * 0.5f) + ((h >> 32) & 0xFF) / 255f * 0.9f;
			float brilho = 0.55f + ((h >> 40) & 0xFF) / 255f * 0.45f;
			DrawCircle(p, raio, new Color(cor, cor.A * brilho));
		}
	}

	private Vector2 CentroDaTela()
	{
		Camera2D? cam = GetViewport()?.GetCamera2D();
		return cam?.GetScreenCenterPosition() ?? Vector2.Zero;
	}
}

/// <summary>
/// UM PLANETA VISTO DO ESPACO. Um disco com nome, e a cor vem do bioma.
///
/// Desenhado em codigo e nao com sprite: o raio varia de planeta pra planeta (110 a 200 px) e um
/// sprite unico esticado nesse intervalo mostraria o mesmo desenho em tamanhos diferentes -- o
/// olho pega isso na hora e o universo inteiro passa a parecer feito de copias.
/// </summary>
public partial class PlanetaDesenhado : Node2D
{
	public string Nome = "";
	public float Raio = 120;
	public ulong Seed;
	public bool Premade;

	private Label _rotulo = null!;

	public override void _Ready()
	{
		ZIndex = -60;   // atras dos corpos, na frente das estrelas

		_rotulo = Tema.Legenda(Nome, Premade ? Tema.Destaque : Tema.TextoFraco, 13);
		_rotulo.Position = new Vector2(-90, -Raio - 34);
		_rotulo.Size = new Vector2(180, 20);
		_rotulo.HorizontalAlignment = HorizontalAlignment.Center;
		AddChild(_rotulo);
	}

	public override void _Draw()
	{
		Color c = Cor();

		// halo: a atmosfera. Sem ela o planeta e um adesivo colado no fundo preto.
		DrawCircle(Vector2.Zero, Raio * 1.12f, new Color(c, 0.16f));
		DrawCircle(Vector2.Zero, Raio, c);

		// LADO ESCURO. Um disco chapado nao tem volume; um crescente mais escuro por cima da
		// direita basta pra o olho ler uma esfera iluminada de lado.
		DrawCircle(new Vector2(Raio * 0.28f, -Raio * 0.10f), Raio * 0.96f, new Color(0, 0, 0, 0.28f));

		// contorno: quem tem mapa proprio ganha borda viva -- e o sinal de "da pra pousar"
		DrawArc(Vector2.Zero, Raio, 0, Mathf.Tau, 48,
				Premade ? new Color(Tema.Destaque, 0.85f) : new Color(1, 1, 1, 0.18f), 2f, true);
	}

	/// <summary>A cor sai da SEED: o mesmo planeta tem a mesma cara em toda máquina.</summary>
	private Color Cor()
	{
		float h = (Seed & 0xFFFF) / 65535f;
		float s = 0.35f + ((Seed >> 16) & 0xFF) / 255f * 0.35f;
		return Color.FromHsv(h, s, 0.55f);
	}
}
