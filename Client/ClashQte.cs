using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O QUICK TIME EVENT DO ZANZO CLASH.
///
/// ============================ POR QUE ELE EXISTE ============================
/// No BYOND o embate e uma ANIMACAO: dois lutadores piscam pelo mapa, o chao racha, e no fim
/// nao acontece nada -- nao ha vencedor, nao ha dano, nao ha nada a fazer alem de assistir.
///
/// O dono pediu outra coisa: "ambos os jogadores vao ter q fazer um quick time event que vai
/// aparecer na tela pra clicar em teclas aleatorias do teclado... e o server vendo qual cliente
/// acertou mais o quick time event, ele determina o vencedor". Esta tela e essa disputa.
/// ============================================================================
///
/// ============================ O QUE ELA NAO FAZ ============================
/// NAO decide nada. A letra pedida vem do servidor, o prazo vem do servidor, o placar vem do
/// servidor e o vencedor vem do servidor -- daqui sai so a tecla crua que o jogador apertou.
/// Fosse o cliente a julgar "acertei", acertar viraria uma linha de codigo.
/// ===========================================================================
///
/// A TELA E CHEIA de proposito. Os dois corpos estao INVISIVEIS durante o embate: sem raias de
/// velocidade, vinheta e um pulso a cada cruzamento, o jogador estaria apertando letras contra
/// um cenario parado. Ver `Assets/Shaders/Embate.gdshader`.
/// </summary>
public partial class ClashQte : CanvasLayer
{
	/// <summary>Quanto tempo o desfecho fica na tela depois do fim.</summary>
	private const double SegundosDeDesfecho = 2.2;

	/// <summary>Quanto tempo o pulso do cruzamento leva pra sumir.</summary>
	private const double SegundosDeBaque = 0.45;

	/// <summary>Quantos pixels acima do centro fica o anel da letra. Ver <see cref="MontarLetra"/>.</summary>
	private const int AlturaDaLetra = 150;

	private Control _raiz = null!;
	private ColorRect _fundo = null!;
	private ShaderMaterial _tinta = null!;
	private Label _titulo = null!, _letra = null!, _desfecho = null!, _dica = null!;
	private Panel _anel = null!;
	private ProgressBar _prazo = null!;
	private ProgressBar _cabo = null!;
	private Label _meus = null!, _dele = null!;

	private double _t;                 // relogio do shader
	private double _baque;             // quanto falta do pulso
	private double _prazoTotal, _prazoResta;
	private double _forca;             // fade de entrada/saida
	private double _desfechoResta;
	private bool _ligado;

	/// <summary>Ja mandei tecla nesta letra? O servidor so aceita uma; a tela tem que dizer isso.</summary>
	private bool _respondi;

	// =====================================================================
	// MONTAGEM
	// =====================================================================
	public override void _Ready()
	{
		Layer = 5;   // acima do menu do jogo (3) e da cinematica (4), abaixo do pause (20)

		_raiz = new Control
		{
			AnchorRight = 1, AnchorBottom = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
		};
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		MontarFundo();
		MontarPlacar();
		MontarLetra();
		MontarDesfecho();

		if (GameClient.Instance is { } cli)
		{
			cli.ClashComecou += Comecou;
			cli.ClashTeclaPedida += Pediu;
			cli.ClashPlacar += Placar;
			cli.ClashBaque += _ => _baque = SegundosDeBaque;
			cli.ClashAcabou += Acabou;
		}
	}

	private void MontarFundo()
	{
		var sh = ResourceLoader.Load<Shader>("res://Assets/Shaders/Embate.gdshader");
		_tinta = new ShaderMaterial { Shader = sh };

		_fundo = new ColorRect
		{
			AnchorRight = 1, AnchorBottom = 1,
			Color = Colors.White,
			Material = _tinta,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_raiz.AddChild(_fundo);
	}

	private void MontarPlacar()
	{
		// O CABO DE GUERRA. Uma barra so, com o meu lado crescendo da esquerda: duas barras
		// separadas obrigariam o jogador a comparar numeros no meio de um teste de reflexo.
		var caixa = new VBoxContainer
		{
			AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0,
			OffsetLeft = -220, OffsetRight = 220, OffsetTop = 42,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_raiz.AddChild(caixa);

		_titulo = new Label
		{
			Text = "ZANZO CLASH",
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_titulo.AddThemeFontSizeOverride("font_size", 26);
		_titulo.AddThemeColorOverride("font_color", Tema.Ki);
		caixa.AddChild(_titulo);

		var linha = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		caixa.AddChild(linha);

		_meus = Numero(Tema.Ki);
		linha.AddChild(_meus);

		_cabo = new ProgressBar
		{
			MinValue = 0, MaxValue = 1, Value = 0.5, ShowPercentage = false,
			CustomMinimumSize = new Vector2(340, 16),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_cabo.AddThemeStyleboxOverride("background", Chapa(Tema.Vida));
		_cabo.AddThemeStyleboxOverride("fill", Chapa(Tema.Ki));
		linha.AddChild(_cabo);

		_dele = Numero(Tema.Vida);
		linha.AddChild(_dele);

		_dica = new Label
		{
			Text = "aperte a letra antes que o anel feche",   // trocado no `Comecou` pela vantagem
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_dica.AddThemeColorOverride("font_color", Tema.TextoFraco);
		caixa.AddChild(_dica);

		static Label Numero(Color c)
		{
			var l = new Label
			{
				Text = "0", CustomMinimumSize = new Vector2(44, 0),
				HorizontalAlignment = HorizontalAlignment.Center,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			l.AddThemeFontSizeOverride("font_size", 20);
			l.AddThemeColorOverride("font_color", c);
			return l;
		}
	}

	private void MontarLetra()
	{
		// A LETRA fica dentro de um anel que ENCOLHE ate o prazo vencer: nao ha numero a ler, e a
		// distancia entre a borda e a letra E o quanto falta.
		//
		// ACIMA DO CENTRO, e nao no centro. A camera segue o corpo, e o corpo esta exatamente no
		// ponto onde os dois se cruzam -- entao a faisca, os dois aneis e a poeira de cada baque
		// estouram sempre no meio da tela. Com a letra ali, o efeito mais bonito do embate vira o
		// que atrapalha ler a letra. Subindo o anel, cada um fica com o seu espaco.
		_anel = new Panel
		{
			AnchorLeft = 0.5f, AnchorTop = 0.5f, AnchorRight = 0.5f, AnchorBottom = 0.5f,
			OffsetLeft = -84, OffsetTop = -84 - AlturaDaLetra, OffsetRight = 84,
			OffsetBottom = 84 - AlturaDaLetra,
			PivotOffset = new Vector2(84, 84),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		// DISCO ESCURO POR TRAS, e nao so a borda: a letra cai no meio da tela, em cima do cenario,
		// e sobre grama clara um contorno fino de 4 px some. O disco garante que a letra e o prazo
		// se leem em qualquer chao. `CornerDetail` alto porque num raio de 84 px o padrao (8) faz
		// um octogono.
		var moldura = new StyleBoxFlat
		{
			BgColor = new Color(0.04f, 0.05f, 0.09f, 0.62f),
			BorderColor = Tema.Ki,
			CornerDetail = 16,
		};
		moldura.SetBorderWidthAll(6);
		moldura.SetCornerRadiusAll(84);
		_anel.AddThemeStyleboxOverride("panel", moldura);
		_raiz.AddChild(_anel);

		_letra = new Label
		{
			AnchorLeft = 0.5f, AnchorTop = 0.5f, AnchorRight = 0.5f, AnchorBottom = 0.5f,
			OffsetLeft = -80, OffsetTop = -70 - AlturaDaLetra, OffsetRight = 80,
			OffsetBottom = 70 - AlturaDaLetra,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_letra.AddThemeFontSizeOverride("font_size", 96);
		_letra.AddThemeColorOverride("font_color", Tema.Texto);
		_raiz.AddChild(_letra);

		// A BARRA DO EMBATE INTEIRO, no pe da tela: quanto falta pra acabar. Sem ela o jogador nao
		// sabe se vale correr atras do placar ou se ja era.
		_prazo = new ProgressBar
		{
			AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1, AnchorBottom = 1,
			OffsetLeft = -220, OffsetRight = 220, OffsetTop = -74, OffsetBottom = -64,
			MinValue = 0, MaxValue = 1, ShowPercentage = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_prazo.AddThemeStyleboxOverride("background", Chapa(Tema.Borda));
		_prazo.AddThemeStyleboxOverride("fill", Chapa(Tema.Destaque));
		_raiz.AddChild(_prazo);
	}

	private void MontarDesfecho()
	{
		_desfecho = new Label
		{
			AnchorLeft = 0.5f, AnchorTop = 0.5f, AnchorRight = 0.5f, AnchorBottom = 0.5f,
			OffsetLeft = -320, OffsetTop = -40, OffsetRight = 320, OffsetBottom = 40,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_desfecho.AddThemeFontSizeOverride("font_size", 52);
		_raiz.AddChild(_desfecho);
	}

	private static StyleBoxFlat Chapa(Color c)
	{
		var s = new StyleBoxFlat { BgColor = c };
		s.SetCornerRadiusAll(4);
		return s;
	}

	// =====================================================================
	// OS MOMENTOS
	// =====================================================================
	private void Comecou(int a, int b, int ms, float meu, float dele)
	{
		// SO OS DOIS JOGAM. Quem esta na zona ve o chao rachando (isso e do mundo, nao daqui) mas
		// nao ganha tela de quick time event por assistir briga alheia -- e por isso o pacote nem
		// chega pra plateia.
		if (GameClient.Instance is not { } cli || (cli.LocalId != a && cli.LocalId != b)) return;

		// A VANTAGEM, dita em uma linha: o jogador tem que saber se esta correndo atras ou na
		// frente ANTES de apertar a primeira letra.
		_dica.Text = meu > dele
			? $"voce e mais forte: cada acerto seu vale {meu:0.##}"
			: dele > meu ? $"ele e mais forte: cada acerto dele vale {dele:0.##} -- seja mais rapido"
						 : "forcas parelhas: quem acertar mais, vence";

		_ligado = true;
		_raiz.Visible = true;
		_desfecho.Visible = false;
		_desfechoResta = 0;
		_letra.Text = "";
		_prazoResta = _prazoTotal = 0;
		_meus.Text = "0";
		_dele.Text = "0";
		_cabo.Value = 0.5;
		_prazo.MaxValue = Math.Max(ms / 1000.0, 0.1);
		_prazo.Value = _prazo.MaxValue;
	}

	private void Pediu(char c, int ms)
	{
		if (!_ligado) return;
		_letra.Text = c.ToString();
		_respondi = false;
		_prazoTotal = _prazoResta = Math.Max(ms / 1000.0, 0.05);
	}

	private void Placar(float meus, float dele)
	{
		if (!_ligado) return;
		_meus.Text = meus.ToString("0.#");
		_dele.Text = dele.ToString("0.#");
		double soma = meus + dele;
		_cabo.Value = soma <= 0 ? 0.5 : meus / soma;
	}

	private void Acabou(int venc, int perd)
	{
		if (!_ligado) return;
		_ligado = false;
		_letra.Text = "";
		_prazoResta = 0;

		bool ganhei = GameClient.Instance?.LocalId == venc;
		_desfecho.Text = ganhei ? "VOCE FOI MAIS RAPIDO" : "ELE FOI MAIS RAPIDO";
		_desfecho.AddThemeColorOverride("font_color", ganhei ? Tema.Destaque : Tema.Vida);
		_desfecho.Visible = true;
		_desfechoResta = SegundosDeDesfecho;
	}

	// =====================================================================
	// O QUADRO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (!_raiz.Visible) return;

		_t += delta;
		if (_baque > 0) _baque = Math.Max(0, _baque - delta);

		// A FORCA sobe rapido e desce devagar: o embate ENTRA de estouro e SAI de fade.
		double alvo = _ligado ? 1 : 0;
		_forca = Mathf.MoveToward((float)_forca, (float)alvo, (float)(delta * (_ligado ? 6 : 1.2)));

		_tinta.SetShaderParameter("tempo", (float)_t);
		_tinta.SetShaderParameter("forca", (float)_forca);
		_tinta.SetShaderParameter("baque", (float)(1 - _baque / SegundosDeBaque) * (_baque > 0 ? 1 : 0));
		Vector2 tela = _raiz.Size;
		_tinta.SetShaderParameter("razao", tela.Y > 0 ? tela.X / tela.Y : 1.777f);

		// O ANEL FECHANDO. `_prazoResta` some ate zero e leva a escala junto -- e o unico relogio
		// da letra, e ele e o mesmo prazo que o servidor esta contando do lado de la.
		if (_prazoResta > 0)
		{
			_prazoResta = Math.Max(0, _prazoResta - delta);
			float f = (float)(_prazoResta / _prazoTotal);
			_anel.Scale = new Vector2(0.42f + f * 0.58f, 0.42f + f * 0.58f);
			// A VEZ JA FOI USADA: o anel apaga em vez de sumir. Sumir seco daria a impressao de que
			// a tela travou; apagado ele diz "respondida, espere a proxima".
			_anel.Modulate = new Color(1, 1, 1, _respondi ? 0.18f : 0.35f + f * 0.65f);
			_anel.Visible = true;
		}
		else _anel.Visible = false;

		// A LETRA SE APAGA JUNTO. Sem isto ela ficava na tela cheia depois de respondida, e a foto
		// da bancada mostrou o resultado: um "N" sozinho no meio da tela, sem anel e sem prazo, como
		// se ainda estivesse valendo.
		_letra.Modulate = new Color(1, 1, 1, _respondi ? 0.22f : 1f);
		_letra.Visible = _ligado && _letra.Text.Length > 0 && _prazoResta > 0;
		_titulo.Visible = _ligado;
		_dica.Visible = _ligado;
		_cabo.Visible = _ligado;
		_meus.Visible = _ligado;
		_dele.Visible = _ligado;
		_prazo.Visible = _ligado;
		if (_ligado) _prazo.Value = Math.Max(0, _prazo.Value - delta);

		if (_desfechoResta > 0)
		{
			_desfechoResta -= delta;
			if (_desfechoResta <= 0) _desfecho.Visible = false;
		}

		// APAGA SO QUANDO NAO SOBROU NADA na tela -- o fade do fundo e o desfecho tem prazos
		// diferentes, e sumir no primeiro que acabar cortaria o outro no meio.
		if (!_ligado && _forca <= 0.01 && _desfechoResta <= 0) _raiz.Visible = false;
	}

	// =====================================================================
	// A TECLA
	// =====================================================================
	/// <summary>
	/// LE A TECLA CRUA e manda pro servidor, que julga.
	///
	/// `_UnhandledInput` e nao `IsActionJustPressed`: as letras do embate sao sorteadas em tempo de
	/// execucao, e nao ha acao registrada no `InputMap` pra cada uma das 22.
	///
	/// `Foco.Digitando` e obrigatorio, como em todo ponto de leitura de teclado do jogo -- sem ele,
	/// escrever no chat durante um embate mandaria cada letra digitada como resposta do quick time
	/// event (e ganharia o embate por acidente, ou o perderia por errar).
	/// </summary>
	public override void _UnhandledInput(InputEvent evento)
	{
		if (!_ligado || Foco.Digitando) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k) return;

		// A LETRA FISICA. `Keycode` muda com o layout do teclado; `PhysicalKeycode` e a tecla onde
		// o dedo esta -- e o que o resto do jogo usa (ver `Boot.RegistrarTeclas`).
		Key t = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
		if (t < Key.A || t > Key.Z) return;

		GameClient.Instance?.SendClashTecla((char)('A' + (t - Key.A)));
		_respondi = true;
		GetViewport().SetInputAsHandled();
	}
}
