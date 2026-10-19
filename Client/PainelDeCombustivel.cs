using Godot;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-18) A BARRA DO COMBUSTIVEL -- o pedido do dono: *"ao pilotar qualquer tipo de nave, deve aparecer no canto uma barrinha de
/// combustivel q mostra o nivel dele, comecando verde (quase cheio ou cheio, amarelo quando ta na metade e vermelho quanto ta acabando) a
/// barra tb mostra o consumo de combustivel por segundo (igual mineirar mostra recursos por segundo)"*.
///
/// E A IRMA DA FAIXA DA MINERACAO (`PainelDeMineracao`), e de proposito: o mesmo canto (logo acima do chat, alinhada com ele), o mesmo
/// desenho (titulo e taxa em cima, a barra embaixo) e a mesma leitura -- os numeros chegam prontos do servidor (`S2C.Combustivel`, uma vez
/// por segundo; o "por segundo" e o que a ultima volta gastou, como a mineracao mostra o que o ultimo segundo rendeu). As duas nunca estao na
/// tela juntas: ao leme ou observando nao se minera.
///
/// APARECE AO LEME DE QUALQUER NAVE COM TANQUE (o pod e a Capital Ship; o foguete nao tem) e tambem pra quem OBSERVA a nave de fora -- quem
/// assiste a viagem quer saber se ela chega.
///
/// SEM ASSINATURA NENHUMA: le o cliente por quadro e so reescreve quando algum numero mudou -- o desenho da faixa da mineracao, pelo mesmo
/// motivo (um node que vive o tempo todo nao tem o que vazar).
/// </summary>
public partial class PainelDeCombustivel : CanvasLayer
{
	public static PainelDeCombustivel? Instancia { get; private set; }

	private PanelContainer _faixa = null!;
	private Label _titulo = null!, _taxa = null!, _numeros = null!;
	private ProgressBar _barra = null!;
	private StyleBoxFlat _cheio = null!;

	/// <summary>O que esta escrito agora, numa chave so: os rotulos so sao refeitos quando ela muda.</summary>
	private string _desenhado = "";

	public override void _EnterTree() => Instancia = this;
	public override void _ExitTree() { if (Instancia == this) Instancia = null; }

	/// <summary>Os numeros de quem nasce sem chat (uma bancada sem ele) -- os mesmos da faixa da mineracao.</summary>
	private const float MargemEsquerda = 12, PeAcimaDoChat = 300;

	// =====================================================================
	// AS TRES CORES DO DONO
	// =====================================================================
	/// <summary>Cheio ou quase: verde (o `Bom` do tema).</summary>
	public static readonly Color Verde = Tema.Bom;

	/// <summary>Na metade: amarelo. O `Destaque` do tema e laranja (o gi), e o dono pediu amarelo -- este e o amarelo da barra.</summary>
	public static readonly Color Amarelo = new("e6cf3f");

	/// <summary>Acabando: vermelho (o `Perigo` do tema).</summary>
	public static readonly Color Vermelho = Tema.Perigo;

	/// <summary>
	/// A COR DO TANQUE PELA FRACAO -- as tres palavras do dono como pontos de uma rampa: VERDE do cheio ate <see cref="QuaseCheio"/>
	/// ("quase cheio ou cheio"), AMARELO na metade, VERMELHO de <see cref="Acabando"/> pra baixo ("quando ta acabando"); entre eles a cor
	/// passa de uma pra outra, sem degrau -- quem olha ve o tanque baixando, e nao tres quadros.
	/// </summary>
	public static Color CorDoTanque(float fracao)
	{
		fracao = Math.Clamp(fracao, 0f, 1f);
		if (fracao >= QuaseCheio) return Verde;
		if (fracao >= 0.5f) return Amarelo.Lerp(Verde, (fracao - 0.5f) / (QuaseCheio - 0.5f));
		if (fracao >= Acabando) return Vermelho.Lerp(Amarelo, (fracao - Acabando) / (0.5f - Acabando));
		return Vermelho;
	}

	/// <summary>Daqui pra cima e "quase cheio": verde puro.</summary>
	public const float QuaseCheio = 0.8f;

	/// <summary>Daqui pra baixo e "acabando": vermelho puro.</summary>
	public const float Acabando = 0.15f;

	/// <summary>
	/// O LUGAR DA FAIXA, PELO RETANGULO REAL DO CHAT -- o mesmo `Janelas.JuntoDoChat` da faixa da mineracao: em cima dele, alinhada pela
	/// esquerda, ou a direita dele se ali ela bateria no painel de status. So escreve quando o lugar muda.
	/// </summary>
	private void Assentar()
	{
		if (Chat.RetanguloNaTela is not { } chat)
		{
			Por(MargemEsquerda, -PeAcimaDoChat);
			return;
		}

		float tela = GetViewport().GetVisibleRect().Size.Y;
		Vector2 faixa = _faixa.Size.Max(_faixa.GetCombinedMinimumSize());
		Vector2 canto = Janelas.JuntoDoChat(chat, faixa, 0f, Hud.RetanguloDoStatus?.End.Y ?? 0f);
		Por(canto.X, canto.Y - tela);   // (ancora de baixo: o deslocamento e negativo)
	}

	private void Por(float esquerda, float pe)
	{
		if (_faixa.OffsetLeft != esquerda) _faixa.OffsetLeft = esquerda;
		if (_faixa.OffsetTop != pe || _faixa.OffsetBottom != pe) { _faixa.OffsetTop = pe; _faixa.OffsetBottom = pe; }
	}

	public override void _Ready()
	{
		Layer = 5;   // a mesma camada da faixa da mineracao: por cima do HUD e do chat

		var raiz = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
		Tema.Aplicar(raiz);
		AddChild(raiz);

		_faixa = Tema.Painel1(8);
		_faixa.AnchorTop = 1; _faixa.AnchorBottom = 1;
		_faixa.OffsetLeft = MargemEsquerda;
		_faixa.OffsetTop = -PeAcimaDoChat; _faixa.OffsetBottom = -PeAcimaDoChat;
		_faixa.GrowVertical = Control.GrowDirection.Begin;   // o pe fica onde esta; o que crescer, cresce pra cima
		_faixa.CustomMinimumSize = new Vector2(360, 0);
		_faixa.MouseFilter = Control.MouseFilterEnum.Ignore;
		_faixa.Visible = false;
		raiz.AddChild(_faixa);

		var linhas = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		linhas.AddThemeConstantOverride("separation", 4);
		_faixa.AddChild(linhas);

		// EM CIMA: o que e, de que nave, e quanto gasta por segundo.
		var cima = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		cima.AddThemeConstantOverride("separation", 12);
		linhas.AddChild(cima);
		_titulo = Rotulo(cima, Tema.Destaque, 13);
		cima.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
		_taxa = Rotulo(cima, Tema.Texto, 13);

		// EMBAIXO: a barra na cor do nivel, e o numero ao lado.
		var baixo = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		baixo.AddThemeConstantOverride("separation", 10);
		linhas.AddChild(baixo);
		_cheio = new StyleBoxFlat { BgColor = Verde };
		_cheio.SetCornerRadiusAll(3);
		_barra = new ProgressBar
		{
			CustomMinimumSize = new Vector2(200, 10),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			ShowPercentage = false,
			MaxValue = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_barra.AddThemeStyleboxOverride("fill", _cheio);
		baixo.AddChild(_barra);
		_numeros = Rotulo(baixo, Tema.TextoFraco, 12);
	}

	private static Label Rotulo(Control pai, Color cor, int tamanho)
	{
		var l = new Label { VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		l.AddThemeFontSizeOverride("font_size", tamanho);
		l.AddThemeColorOverride("font_color", cor);
		pai.AddChild(l);
		return l;
	}

	public override void _Process(double delta)
	{
		GameClient? cli = GameClient.Instance;
		bool naTela = cli is { Connected: true } && cli.Tanque.Modo != 0 && cli.Tanque.Capacidade > 0;
		if (naTela) Assentar();   // ANTES de aparecer: ela nao pode nascer num lugar e pular pra outro
		if (_faixa.Visible != naTela) _faixa.Visible = naTela;
		if (!naTela || cli == null) { _desenhado = ""; return; }

		GameClient.TanqueVisto t = cli.Tanque;
		string chave = $"{t.Modo}|{t.Nave}|{t.Nivel}|{t.Capacidade}|{t.PorSegundo}";
		if (chave == _desenhado) return;
		_desenhado = chave;

		float fracao = t.Nivel / t.Capacidade;
		_titulo.Text = t.Modo == 2 ? $"COMBUSTÍVEL · {t.Nave} (observando)" : $"COMBUSTÍVEL · {t.Nave}";
		_taxa.Text = t.Nivel <= 0 ? "VAZIO — abasteça (E)"
			: t.PorSegundo > 0 ? $"-{t.PorSegundo:0.##}/s"
			: "parada: 0/s";
		_taxa.AddThemeColorOverride("font_color", t.Nivel <= 0 ? Vermelho : t.PorSegundo > 0 ? Tema.Texto : Tema.TextoFraco);
		_numeros.Text = $"{Math.Floor(t.Nivel):N0} / {t.Capacidade:N0}";
		_barra.Value = Math.Clamp(fracao, 0f, 1f);
		_cheio.BgColor = CorUnicaDeTeste ? Verde : CorDoTanque(fracao);
	}

	/// <summary>
	/// DEFEITO INJETADO (`--diagcapital`, C9): a barra fica sempre verde, qualquer que seja o nivel -- uma barra so com o tamanho dizendo o
	/// tanque, sem as tres cores que o dono pediu. Lido pela propria linha da cor. Falso em jogo, sempre.
	/// </summary>
	public static bool CorUnicaDeTeste;

	// =====================================================================
	// O QUE A BANCADA (`--diagcapital`) PERGUNTA
	// =====================================================================
	/// <summary>A faixa esta na tela?</summary>
	internal bool NaTela => _faixa.Visible;

	/// <summary>O que a faixa diz, como o jogador le.</summary>
	internal string TextoDeTeste => $"{_titulo.Text} | {_taxa.Text} | {_numeros.Text}";

	/// <summary>A cor da barra agora.</summary>
	internal Color CorDeTeste => _cheio.BgColor;

	/// <summary>Onde a faixa esta desenhada, em pixels de tela.</summary>
	internal Rect2 RetanguloDeTeste => _faixa.GetGlobalRect();
}
