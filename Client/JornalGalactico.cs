using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ O JORNAL GALACTICO NA TELA (dono, 2026-10-31, o pedido 198) ============================
/// O dono pediu o jornal diario com estilo holografico e um aviso no canto inferior direito. Duas pecas, e as duas so MOSTRAM o que o
/// servidor mandou (`S2C.Jornal`, lido no `GameClient.Jornal`):
///   * O AVISO (<see cref="AvisoDoJornal"/>): um cartao na pilha do canto inferior direito (a dos pedidos, acima do convite do torneio):
///     "JORNAL GALÁCTICO -- a edição de {data} saiu", com "Ler" e "Depois". Some sozinho em <see cref="AvisoDoJornal.SegundosNaTela"/> s.
///   * A JANELA (<see cref="JornalGalactico"/>): no meio da tela, numa camada acima do chat -- o painel em ciano translucido com a borda que
///     brilha, as linhas de varredura correndo por cima e o tremor de holograma ao abrir; o titulo, a data e as noticias, cada uma com o selo
///     do tipo (PLANETA DESTRUÍDO, DEUS DA DESTRUIÇÃO, PRISÃO, PROCURADO, HERÓI, AMEAÇA). Fecha no "Fechar" ou no Esc.
/// O TEXTO CHEGA EM PORTUGUES e o rotulo o le na lingua da tela (a traducao de todo controle, `Idioma`).
/// ======================================================================================================
/// Robo: `--diagpatrulha` (as fotos do aviso e da janela).
/// </summary>
public partial class JornalGalactico : Control
{
	public static JornalGalactico? Instancia { get; private set; }

	/// <summary>O tamanho da janela, em px de tela.</summary>
	private const float Largura = 640f, Altura = 470f;

	/// <summary>Quanto dura o tremor de holograma ao abrir.</summary>
	private const double SegundosDoTremor = 0.45;

	/// <summary>As cores do holograma.</summary>
	internal static readonly Color Ciano = new(0.55f, 1f, 1f), CianoFundo = new(0.02f, 0.14f, 0.2f, 0.88f), CianoBorda = new(0.35f, 0.95f, 1f, 0.95f);

	private Control _janela = null!;
	private Label _data = null!;
	private VBoxContainer _lista = null!;
	private double _desdeQueAbriu = SegundosDoTremor;

	/// <summary>A janela esta na tela?</summary>
	public bool NaTela => Visible;

	/// <summary>(robo) as linhas desenhadas: o selo e o texto de cada noticia, e a data.</summary>
	public List<(string Selo, string Texto)> NoticiasDesenhadas()
	{
		var linhas = new List<(string, string)>();
		foreach (Node n in _lista.GetChildren())
			if (n is HBoxContainer h && h.GetChildCount() >= 2 && h.GetChild(0) is PanelContainer { } s && s.GetChild(0) is Label selo
				&& h.GetChild(1) is Label texto)
				linhas.Add((selo.Text, texto.Text));
		return linhas;
	}

	public string DataDesenhada => _data.Text;

	/// <summary>O retangulo da janela na tela (o robo confere que ela cabe inteira).</summary>
	public Rect2 RetanguloDaJanela => _janela.GetGlobalRect();

	public override void _Ready()
	{
		Instancia = this;
		AnchorRight = 1;
		AnchorBottom = 1;
		MouseFilter = MouseFilterEnum.Ignore;
		Tema.Aplicar(this);

		_janela = new Control
		{
			AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
			OffsetLeft = -Largura / 2, OffsetRight = Largura / 2, OffsetTop = -Altura / 2, OffsetBottom = Altura / 2,
			MouseFilter = MouseFilterEnum.Stop,
		};
		AddChild(_janela);

		var painel = new PanelContainer { AnchorRight = 1, AnchorBottom = 1 };
		var caixa = new StyleBoxFlat
		{
			BgColor = CianoFundo,
			BorderColor = CianoBorda,
			ShadowColor = new Color(0.3f, 0.9f, 1f, 0.35f),
			ShadowSize = 16,
		};
		caixa.SetBorderWidthAll(2);
		caixa.SetCornerRadiusAll(6);
		caixa.SetContentMarginAll(16);
		painel.AddThemeStyleboxOverride("panel", caixa);
		_janela.AddChild(painel);

		var v = new VBoxContainer();
		v.AddThemeConstantOverride("separation", 8);
		painel.AddChild(v);

		var titulo = new Label { Text = "JORNAL GALÁCTICO", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 26);
		titulo.AddThemeColorOverride("font_color", Ciano);
		titulo.AddThemeColorOverride("font_outline_color", new Color(0.1f, 0.6f, 0.8f, 0.8f));
		titulo.AddThemeConstantOverride("outline_size", 4);
		v.AddChild(titulo);

		_data = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_data.AddThemeFontSizeOverride("font_size", 13);
		_data.AddThemeColorOverride("font_color", new Color(Ciano, 0.75f));
		v.AddChild(_data);

		var linha = new ColorRect { Color = new Color(CianoBorda, 0.6f), CustomMinimumSize = new Vector2(0, 2) };
		v.AddChild(linha);

		var rolagem = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		v.AddChild(rolagem);
		_lista = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_lista.AddThemeConstantOverride("separation", 10);
		rolagem.AddChild(_lista);

		var rodape = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		v.AddChild(rodape);
		var fechar = new Button { Text = "Fechar (Esc)" };
		fechar.Pressed += Fechar;
		rodape.AddChild(fechar);

		// AS LINHAS DE VARREDURA, por cima do painel e sem pegar o mouse: o "holografico" do pedido. Um shader de tres linhas -- as faixas
		// finas paradas e uma faixa clara que desce devagar --, no lugar de uma textura que ninguem desenhou.
		var varredura = new ColorRect { AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
		varredura.Material = new ShaderMaterial
		{
			Shader = new Shader
			{
				Code = "shader_type canvas_item;\n"
					   + "void fragment() {\n"
					   + "  float linha = step(2.0, mod(FRAGCOORD.y, 4.0));\n"
					   + "  float faixa = 1.0 - smoothstep(0.0, 0.06, abs(fract(TIME * 0.18) - UV.y));\n"
					   + "  COLOR = vec4(0.55, 1.0, 1.0, 0.035 * linha + 0.07 * faixa);\n"
					   + "}\n",
			},
		};
		_janela.AddChild(varredura);

		Visible = false;
		SetProcess(false);

		if (GameClient.Instance is { } cli) cli.JornalChegou += AoChegar;
	}

	/// <summary>Solta a assinatura -- o `GameClient` sobrevive ao logout (ver a nota no `MenuJogo._Ready`).</summary>
	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.JornalChegou -= AoChegar;
		if (Instancia == this) Instancia = null;
	}

	/// <summary>A edicao pedida (`jornal_ler`) abre a janela; a nova vai pro aviso do canto, e a janela aberta se refaz com ela.</summary>
	private void AoChegar(bool aviso)
	{
		if (!aviso || Visible) Abrir();
	}

	/// <summary>Abre (ou refaz) a janela com a ultima edicao que chegou.</summary>
	public void Abrir()
	{
		if (GameClient.Instance?.Jornal is not { } e) return;
		_data.Text = $"Edição de {e.Data}";
		foreach (Node n in _lista.GetChildren()) n.QueueFree();
		foreach (GameClient.NoticiaInfo n in e.Noticias) _lista.AddChild(Linha(n));
		if (!Visible) _desdeQueAbriu = 0;
		Visible = true;
		SetProcess(true);
		AvisoDoJornal.Instancia?.Esconder();
	}

	public void Fechar()
	{
		Visible = false;
		SetProcess(false);
		Modulate = Colors.White;
	}

	public override void _Process(double delta)
	{
		// O TREMOR DE HOLOGRAMA ao abrir: a opacidade pisca e assenta.
		_desdeQueAbriu += delta;
		float a = _desdeQueAbriu >= SegundosDoTremor ? 1f : 0.55f + 0.45f * Mathf.Abs(Mathf.Sin((float)_desdeQueAbriu * 38f));
		Modulate = new Color(1, 1, 1, a);
	}

	/// <summary>ESC FECHA, e o menu de pausa nao abre junto: `_Input` roda antes do `_UnhandledInput` dele (o motivo da janela de desejos).</summary>
	public override void _Input(InputEvent evento)
	{
		if (!Visible || Foco.Digitando) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
		Fechar();
		GetViewport().SetInputAsHandled();
	}

	/// <summary>O selo de cada tipo de noticia (a ordem e a do `GameServer.TipoDeNoticia`).</summary>
	internal static (string Selo, Color Cor) SeloDe(byte tipo) => tipo switch
	{
		1 => ("PLANETA DESTRUÍDO", new Color("ff5a4a")),
		2 => ("DEUS DA DESTRUIÇÃO", new Color("c58bff")),
		3 => ("PRISÃO", new Color("7fd4ff")),
		4 => ("PROCURADO", new Color("ffb347")),
		5 => ("HERÓI", new Color("7ee08a")),
		6 => ("AMEAÇA", new Color("ffe066")),
		_ => ("NOTÍCIA", Ciano),
	};

	private static HBoxContainer Linha(GameClient.NoticiaInfo n)
	{
		(string selo, Color cor) = SeloDe(n.Tipo);
		var h = new HBoxContainer();
		h.AddThemeConstantOverride("separation", 10);

		var caixa = new StyleBoxFlat { BgColor = new Color(cor, 0.16f), BorderColor = new Color(cor, 0.9f) };
		caixa.SetBorderWidthAll(1);
		caixa.SetCornerRadiusAll(3);
		caixa.SetContentMarginAll(4);
		var moldura = new PanelContainer { CustomMinimumSize = new Vector2(150, 0), SizeFlagsVertical = SizeFlags.ShrinkBegin };
		moldura.AddThemeStyleboxOverride("panel", caixa);
		var rotulo = new Label { Text = selo, HorizontalAlignment = HorizontalAlignment.Center };
		rotulo.AddThemeFontSizeOverride("font_size", 11);
		rotulo.AddThemeColorOverride("font_color", cor);
		moldura.AddChild(rotulo);
		h.AddChild(moldura);

		var texto = new Label
		{
			Text = n.Texto,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		texto.AddThemeFontSizeOverride("font_size", 15);
		texto.AddThemeColorOverride("font_color", new Color(0.88f, 0.98f, 1f));
		h.AddChild(texto);
		return h;
	}
}

/// <summary>
/// O AVISO DO JORNAL, no canto inferior direito -- um cartao na pilha dos pedidos (`PainelDePedidos`, acima do convite do torneio): a edicao
/// nova saiu, com "Ler" (abre a janela) e "Depois" (some). Some sozinho em <see cref="SegundosNaTela"/> s.
/// </summary>
public partial class AvisoDoJornal : PanelContainer
{
	public static AvisoDoJornal? Instancia { get; private set; }

	/// <summary>Quanto o aviso fica na tela sem ninguem tocar.</summary>
	public const double SegundosNaTela = 45;

	private Label _texto = null!;
	private double _restante;

	public bool NaTela => Visible;
	public string TextoDesenhado => _texto.Text;
	public void LerDeTeste() => Ler();

	public override void _Ready()
	{
		Instancia = this;
		CustomMinimumSize = new Vector2(300, 0);
		var caixa = new StyleBoxFlat
		{
			BgColor = JornalGalactico.CianoFundo,
			BorderColor = JornalGalactico.CianoBorda,
			ShadowColor = new Color(0.3f, 0.9f, 1f, 0.3f),
			ShadowSize = 8,
		};
		caixa.SetBorderWidthAll(2);
		caixa.SetCornerRadiusAll(5);
		caixa.SetContentMarginAll(10);
		AddThemeStyleboxOverride("panel", caixa);
		MouseFilter = MouseFilterEnum.Stop;

		var v = new VBoxContainer();
		v.AddThemeConstantOverride("separation", 6);
		AddChild(v);
		var titulo = new Label { Text = "JORNAL GALÁCTICO" };
		titulo.AddThemeFontSizeOverride("font_size", 15);
		titulo.AddThemeColorOverride("font_color", JornalGalactico.Ciano);
		v.AddChild(titulo);
		_texto = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(276, 0) };
		_texto.AddThemeFontSizeOverride("font_size", 12);
		_texto.AddThemeColorOverride("font_color", new Color(0.85f, 0.97f, 1f));
		v.AddChild(_texto);
		var botoes = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		botoes.AddThemeConstantOverride("separation", 8);
		v.AddChild(botoes);
		var ler = new Button { Text = "Ler" };
		ler.Pressed += Ler;
		botoes.AddChild(ler);
		var depois = new Button { Text = "Depois" };
		depois.Pressed += Esconder;
		botoes.AddChild(depois);

		Visible = false;
		SetProcess(false);
		if (GameClient.Instance is { } cli) cli.JornalChegou += AoChegar;
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.JornalChegou -= AoChegar;
		if (Instancia == this) Instancia = null;
	}

	/// <summary>So a edicao NOVA acende o aviso (a pedida abre a janela direto).</summary>
	private void AoChegar(bool aviso)
	{
		if (!aviso || GameClient.Instance?.Jornal is not { } e) return;
		if (JornalGalactico.Instancia is { NaTela: true }) return;
		int n = e.Noticias.Count;
		_texto.Text = n == 1 ? $"A edição de {e.Data} saiu: 1 notícia da galáxia." : $"A edição de {e.Data} saiu: {n} notícias da galáxia.";
		_restante = SegundosNaTela;
		Visible = true;
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		_restante -= delta;
		if (_restante <= 0) Esconder();
	}

	private void Ler()
	{
		Esconder();
		JornalGalactico.Instancia?.Abrir();
	}

	public void Esconder()
	{
		Visible = false;
		SetProcess(false);
	}
}
