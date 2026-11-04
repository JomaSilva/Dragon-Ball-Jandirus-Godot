using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// (2026-11-01, o pedido 216) O AVISO URGENTE NA TELA -- o dono: *"o player deus da destruição recebe um aviso urgente q alguem ta matando um
/// kaioshin"*. Um cartao VERMELHO na pilha dos pedidos (o canto inferior direito, a mesma do aviso do jornal), aceso por toda fala do servidor
/// no tom `Fala.AvisoUrgente`; a linha sai no chat tambem, pintada (`Chat`). O aviso mais novo troca o texto (o chat guarda todos), e o cartao
/// some no "Ciente" ou em <see cref="SegundosNaTela"/> s. Quem fala nesse tom e o servidor (`GameServer.VidaLigada.cs`).
/// </summary>
public partial class AvisoUrgenteNaTela : PanelContainer
{
	public static AvisoUrgenteNaTela? Instancia { get; private set; }

	/// <summary>Quanto o cartao fica na tela sem ninguem tocar -- o dobro do aviso do jornal: este e o aviso que nao se pode perder.</summary>
	public const double SegundosNaTela = 90;

	/// <summary>O vermelho do aviso -- o mesmo da linha do chat (`Chat.CorDoUrgente`).</summary>
	public static readonly Color Vermelho = new(Chat.CorDoUrgente);

	private Label _texto = null!;
	private double _restante;

	public bool NaTela => Visible;
	public string TextoDesenhado => _texto.Text;

	public override void _Ready()
	{
		Instancia = this;
		CustomMinimumSize = new Vector2(300, 0);
		var caixa = new StyleBoxFlat
		{
			BgColor = new Color(0.22f, 0.03f, 0.08f, 0.94f),
			BorderColor = Vermelho,
			ShadowColor = new Color(1f, 0.23f, 0.42f, 0.35f),
			ShadowSize = 10,
		};
		caixa.SetBorderWidthAll(2);
		caixa.SetCornerRadiusAll(5);
		caixa.SetContentMarginAll(10);
		AddThemeStyleboxOverride("panel", caixa);
		MouseFilter = MouseFilterEnum.Stop;

		var v = new VBoxContainer();
		v.AddThemeConstantOverride("separation", 6);
		AddChild(v);
		var titulo = new Label { Text = "AVISO URGENTE" };
		titulo.AddThemeFontSizeOverride("font_size", 15);
		titulo.AddThemeColorOverride("font_color", Vermelho);
		v.AddChild(titulo);
		_texto = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(276, 0) };
		_texto.AddThemeFontSizeOverride("font_size", 13);
		_texto.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.92f));
		v.AddChild(_texto);
		var botoes = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		v.AddChild(botoes);
		var ciente = new Button { Text = "Ciente" };
		ciente.Pressed += Esconder;
		botoes.AddChild(ciente);

		Visible = false;
		SetProcess(false);
		if (GameClient.Instance is { } cli) cli.Falou += AoOuvir;
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.Falou -= AoOuvir;
		if (Instancia == this) Instancia = null;
	}

	/// <summary>So o tom urgente acende o cartao; o texto ja chega na lingua desta tela (`GameClient`, `Idioma.DoServidor`).</summary>
	private void AoOuvir(Protocol.Fala canal, string quem, string texto)
	{
		if (canal != Protocol.Fala.AvisoUrgente) return;
		_texto.Text = texto;
		_restante = SegundosNaTela;
		Visible = true;
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		_restante -= delta;
		if (_restante <= 0) Esconder();
	}

	public void Esconder()
	{
		Visible = false;
		SetProcess(false);
	}
}
