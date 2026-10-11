using Godot;
using Jandirus.Core.Magic;

namespace Jandirus.Client;

/// <summary>
/// O DISCO DO DRAGON RADAR, no canto inferior direito do HUD.
///
/// O dono (2026-10-10): *"quando tiver radar do dragao no canto inferior direito vai aparecer um circulo
/// igual um radar com as bolinhas amarelas a onde as esferas estao em relacao a voce"*.
///
/// ============================ ELE SO DESENHA O QUE O SERVIDOR MANDOU ============================
/// O disco aparece quando o `GameClient.RadarLigado` acende -- e quem acende e o pacote
/// `S2C.RadarDeEsferas`, que so sai pra quem tem o Dragon Radar na mochila. Nao ha conta de mochila aqui,
/// nem leitura das esferas que a zona desenha no chao: sem o pacote, este node nao sabe de esfera nenhuma.
///
/// VOCE E O CENTRO, O NORTE E EM CIMA. Cada bolinha amarela e uma esfera ACORDADA deste mundo, na direcao
/// e na distancia dela (`RadarDeEsferas.NoDisco`); a que esta alem do alcance fica presa na borda, menor,
/// do lado certo. A que alguem carrega ganha um anel; as que estao na SUA mao ficam em roda do centro.
/// Um clique no disco troca o alcance (`RadarDeEsferas.AlcancesEmTiles`), como o botao do radar do anime.
///
/// SEM SHADER, de proposito: tudo e `_Draw` com circulo, arco e linha -- o material padrao do canvas, que
/// ja esta compilado desde o primeiro quadro do jogo. O disco pode nascer no meio de uma luta (alguem te
/// da um radar) sem custar um engasgo de primeiro uso.
/// ================================================================================================
///
/// O LUGAR: um quadrado de <see cref="Diametro"/> px a <see cref="Margem"/> px do canto. O convite do
/// torneio, que mora no mesmo canto, encosta a esquerda dele enquanto o disco esta na tela
/// (<see cref="LarguraOcupada"/>); o seletor de pagamento do modo de construir fica ACIMA.
/// </summary>
public partial class RadarNaTela : Control
{
	/// <summary>O lado do quadrado que o disco ocupa, em px de tela.</summary>
	public const float Diametro = 170f;

	/// <summary>A folga entre o disco e as bordas direita e de baixo da tela -- a mesma dos paineis do HUD.</summary>
	public const float Margem = 14f;

	public static RadarNaTela? Instancia { get; private set; }

	/// <summary>
	/// QUANTO DO CANTO INFERIOR DIREITO O DISCO TOMA AGORA, na horizontal (0 com ele escondido). Quem divide
	/// o canto com ele (o convite do torneio) se afasta isto da borda.
	/// </summary>
	public static float LarguraOcupada => Instancia is { Visible: true } ? Diametro + Margem + 8f : 0f;

	// A PALETA DO APARELHO: o verde-escuro do radar do anime, e o amarelo das esferas.
	private static readonly Color Chapa = new("0b2414eb");
	private static readonly Color Grade = new("3fbf6a59");
	private static readonly Color Aro = new("5fe08ad9");
	private static readonly Color Feixe = new("7dffa8");
	private static readonly Color Eu = new("e9fff0");
	public static readonly Color Amarelo = new("ffd23a");
	private static readonly Color Contorno = new("3a2a00d9");

	/// <summary>O degrau do zoom. Do PROCESSO, e nao do node: relogar nao devolve o alcance de fabrica.</summary>
	private static int _degrau = RadarDeEsferas.AlcanceDeFabrica;

	private static readonly string[] Rotulos = [.. RadarDeEsferas.AlcancesEmTiles.Select(t => $"{t} tiles")];

	private readonly float[] _larguras = new float[Rotulos.Length];
	private double _varredura;
	private Font? _fonte;

	/// <summary>Um ponto do disco, ja em pixels deste node. E o que o `_Draw` pinta -- e o que a bancada le.</summary>
	public readonly record struct Ponto(Vector2 Onde, bool NaBorda, bool Carregada, bool Comigo);

	private readonly List<Ponto> _pontos = [];

	/// <summary>Os pontos que o ultimo quadro desenhou. So pras bancadas.</summary>
	public IReadOnlyList<Ponto> PontosDeTeste => _pontos;

	/// <summary>O alcance em uso, em tiles. So pras bancadas.</summary>
	public int AlcanceDeTeste => RadarDeEsferas.AlcancesEmTiles[_degrau];

	/// <summary>O centro do disco e o raio util (onde a borda prende os sinais), em pixels deste node.</summary>
	public Vector2 Centro => Size / 2f;
	public float RaioUtil => Size.X / 2f - 10f;

	public override void _Ready()
	{
		Instancia = this;
		AnchorLeft = AnchorRight = AnchorTop = AnchorBottom = 1;
		OffsetLeft = -(Diametro + Margem);
		OffsetRight = -Margem;
		OffsetTop = -(Diametro + Margem);
		OffsetBottom = -Margem;
		MouseFilter = MouseFilterEnum.Stop;   // so dentro do circulo: ver `_HasPoint`
		TooltipText = "Dragon Radar: as Esferas do Dragão ACORDADAS deste mundo. Você é o centro; o norte é em cima.\n"
					+ "Clique para trocar o alcance.";
		_fonte = ThemeDB.FallbackFont;
		for (int i = 0; i < Rotulos.Length; i++)
			_larguras[i] = _fonte.GetStringSize(Rotulos[i], HorizontalAlignment.Left, -1, 10).X;
		Visible = false;
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
	}

	/// <summary>O CLIQUE SO VALE DENTRO DO CIRCULO: os cantos do quadrado continuam sendo mundo (o duplo clique de marcar alvo passa).</summary>
	public override bool _HasPoint(Vector2 ponto) => (ponto - Centro).LengthSquared() <= Centro.X * Centro.X;

	public override void _GuiInput(InputEvent e)
	{
		if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
		TrocarOAlcance();
		AcceptEvent();
	}

	/// <summary>O proximo degrau do zoom, dando a volta. O clique no disco; publico pra bancada.</summary>
	public void TrocarOAlcance() => _degrau = (_degrau + 1) % RadarDeEsferas.AlcancesEmTiles.Length;

	/// <summary>DEFEITO INJETADO (bancada --diagmapa): o disco continua na tela por baixo da janela de desejos.</summary>
	public static bool DiscoPorBaixoDaJanelaDeTeste;

	public override void _Process(double delta)
	{
		GameClient? cli = GameClient.Instance;

		// A JANELA DE DESEJOS MORA NO MESMO CANTO, e em 1280x720 nao sobra altura pras duas: ela cobria o disco
		// pela metade. Com a janela aberta o disco sai (com o dragao de pe as sete esferas estao aos pes de quem
		// invocou: ele nao teria o que contar) e volta sozinho quando ela fecha.
		bool ligado = cli is { RadarLigado: true, Connected: true }
					  && (DiscoPorBaixoDaJanelaDeTeste || MenuDeDesejos.Instancia is not { NaTela: true });
		if (Visible != ligado) Visible = ligado;
		if (!ligado) return;

		_varredura = (_varredura + delta * Math.Tau / RadarDeEsferas.SegundosPorVolta) % Math.Tau;
		Recalcular(cli!);
		QueueRedraw();
	}

	/// <summary>
	/// Onde cada sinal cai, em pixels. Fora do `_Draw` porque a bancada precisa da MESMA lista que foi
	/// pintada -- e reusando a lista, pra nao alocar por quadro.
	/// </summary>
	private void Recalcular(GameClient cli)
	{
		_pontos.Clear();

		// OS SINAIS SAO DESTE MUNDO? Depois de trocar de zona os antigos nao valem -- ver `ZonaDoRadar`.
		if (cli.ZonaDoRadar != cli.Zone.Hash || World.Instancia?.PosicaoLocal is not { } eu) return;

		float alcance = RadarDeEsferas.AlcanceEmPx(_degrau), raio = RaioUtil;
		IReadOnlyList<SinalDeEsfera> sinais = cli.SinaisDoRadar;
		int comigo = 0;
		for (int i = 0; i < sinais.Count; i++)
		{
			SinalDeEsfera s = sinais[i];
			if (s.Comigo)
			{
				// AS QUE EU CARREGO ficam em roda do centro, e nao em cima dele: tres na mao tem que ler tres.
				float a = comigo++ * (Mathf.Tau / 7f) - Mathf.Pi / 2f;
				_pontos.Add(new Ponto(Centro + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 11f, false, true, true));
				continue;
			}

			(float x, float y, bool naBorda) = RadarDeEsferas.NoDisco(s.X - eu.X, s.Y - eu.Y, alcance);
			_pontos.Add(new Ponto(Centro + new Vector2(x, y) * raio, naBorda, s.Carregada, false));
		}
	}

	public override void _Draw()
	{
		Vector2 c = Centro;
		float r = Size.X / 2f - 3f;

		DrawCircle(c, r + 3f, new Color(0, 0, 0, 0.45f));
		DrawCircle(c, r, Chapa);

		// A GRADE: dois aneis e a cruz. O anel de fora e o alcance; os de dentro, um e dois tercos dele.
		DrawArc(c, RaioUtil / 3f, 0, Mathf.Tau, 40, Grade, 1f, true);
		DrawArc(c, RaioUtil * 2f / 3f, 0, Mathf.Tau, 56, Grade, 1f, true);
		DrawLine(c - new Vector2(r, 0), c + new Vector2(r, 0), Grade, 1f);
		DrawLine(c - new Vector2(0, r), c + new Vector2(0, r), Grade, 1f);

		// A VARREDURA: a linha da frente e o rastro que apaga atras dela.
		const int Rastro = 16;
		for (int i = Rastro; i >= 0; i--)
		{
			double ang = _varredura - i * 0.055;
			float forca = 1f - i / (float)(Rastro + 1);
			DrawLine(c, c + new Vector2((float)Math.Cos(ang), (float)Math.Sin(ang)) * r,
					 new Color(Feixe, i == 0 ? 0.75f : 0.22f * forca * forca), i == 0 ? 1.6f : 3.2f);
		}

		DrawArc(c, r, 0, Mathf.Tau, 72, Aro, 2f, true);

		// EU, no centro -- ANTES dos sinais: a esfera que esta em cima de mim tem de aparecer por cima do ponto.
		DrawCircle(c, 3.4f, Contorno);
		DrawCircle(c, 2.4f, Eu);

		// OS SINAIS. O que a varredura acabou de varrer brilha um instante -- e o "ping" do aparelho.
		for (int i = 0; i < _pontos.Count; i++)
		{
			Ponto p = _pontos[i];
			Vector2 d = p.Onde - c;
			double atras = (_varredura - Math.Atan2(d.Y, d.X) + Math.Tau * 2) % Math.Tau;
			float brilho = p.Comigo ? 0f : (float)Math.Exp(-atras * 1.6);
			float raio = p.Comigo ? 2.8f : p.NaBorda ? 3.2f : 4.4f;

			if (brilho > 0.05f) DrawCircle(p.Onde, raio + 4f, new Color(Amarelo, 0.30f * brilho));
			DrawCircle(p.Onde, raio + 1.2f, Contorno);
			DrawCircle(p.Onde, raio, p.NaBorda ? Amarelo.Darkened(0.12f) : Amarelo);
			if (p.Carregada && !p.Comigo) DrawArc(p.Onde, raio + 3.2f, 0, Mathf.Tau, 20, Amarelo, 1.2f, true);
		}

		if (_fonte == null) return;
		DrawString(_fonte, c + new Vector2(-4f, -r + 14f), "N", HorizontalAlignment.Left, -1, 11, new Color(Aro, 0.9f));
		DrawString(_fonte, c + new Vector2(-_larguras[_degrau] / 2f, r - 9f), Rotulos[_degrau], HorizontalAlignment.Left, -1, 10, new Color(Aro, 0.85f));
	}
}
