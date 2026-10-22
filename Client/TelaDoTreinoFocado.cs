using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O anel do treino focado e a barra da meditacao focada. Nao julga: a letra, o prazo, a fase e o
/// bonus vem do servidor (<see cref="GameClient.FocoMudou"/>). A tecla que sai daqui e crua.
///
/// NAO cala os atalhos. O silencio do embate (<see cref="Foco.AtalhosMudos"/>) cala o T e o M e e a
/// origem das duas oscilacoes da <c>--diagmudez</c>. Este anel so come o WASD enquanto esta na tela,
/// e devolve a tecla inteira quando um embate de verdade esta de pe -- a letra do embate nao pode
/// ser engolida aqui. O corpo parado e o <see cref="TreinoFocado.WasdNaoAnda"/>, no vetor de andar,
/// porque <c>IsActionPressed</c> nao ve <c>SetInputAsHandled</c>.
/// </summary>
public partial class TelaDoTreinoFocado : CanvasLayer
{
	public static TelaDoTreinoFocado? Instancia { get; private set; }

	private Control _raiz = null!;
	private Label _titulo = null!, _letra = null!, _bonus = null!, _dica = null!;
	private AnelDoFoco _anel = null!;
	private BarraDaRespiracao _barra = null!;
	private int _geracaoVista = -1;
	private int _respVista = -1;
	private double _pisca;
	private bool _piscaVerde;
	private double _resta, _total;

	public override void _Ready()
	{
		Layer = 5;
		Instancia = this;
		Montar();
		if (GameClient.Instance is { } cli) cli.FocoMudou += AoMudar;
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.FocoMudou -= AoMudar;
		if (Instancia == this) Instancia = null;
	}

	private void Montar()
	{
		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
		AddChild(_raiz);

		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
		_raiz.AddChild(centro);

		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
		caixa.AddThemeConstantOverride("separation", 8);
		centro.AddChild(caixa);

		_titulo = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		_titulo.AddThemeFontSizeOverride("font_size", 22);
		caixa.AddChild(_titulo);

		_anel = new AnelDoFoco { CustomMinimumSize = new Vector2(180, 180), MouseFilter = Control.MouseFilterEnum.Ignore };
		caixa.AddChild(_anel);

		_letra = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		_letra.AddThemeFontSizeOverride("font_size", 64);
		caixa.AddChild(_letra);

		_barra = new BarraDaRespiracao { CustomMinimumSize = new Vector2(280, 28), MouseFilter = Control.MouseFilterEnum.Ignore };
		caixa.AddChild(_barra);

		_bonus = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		_bonus.AddThemeFontSizeOverride("font_size", 18);
		caixa.AddChild(_bonus);

		_dica = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		caixa.AddChild(_dica);
	}

	private void AoMudar()
	{
		if (GameClient.Instance is not { } cli) return;
		byte v = cli.FocoModo == TreinoFocado.ModoTreino ? cli.FocoVeredito : cli.RespiracaoVeredito;
		if (v is 1 or 2)
		{
			_pisca = 0.35;
			_piscaVerde = v == 1;
		}
		if (cli.FocoGeracao != _geracaoVista && cli.FocoModo == TreinoFocado.ModoTreino)
		{
			_geracaoVista = cli.FocoGeracao;
			_total = Math.Max(1, cli.FocoPrazoMs) / 1000.0;
			_resta = _total;
		}
	}

	public override void _Process(double delta)
	{
		GameClient? cli = GameClient.Instance;
		bool treino = cli is { FocoModo: TreinoFocado.ModoTreino, FocoPausado: false, Atividade: Protocol.Activity.Treinando };
		bool med = cli is { FocoModo: TreinoFocado.ModoMeditacao, FocoPausado: false, Atividade: Protocol.Activity.Meditando };
		_raiz.Visible = treino || med;
		if (!_raiz.Visible || cli == null) return;

		if (_pisca > 0) _pisca -= delta;
		_resta = Math.Max(0, _resta - delta);

		_titulo.Text = treino ? "Treino focado" : "Meditação focada";
		_bonus.Text = $"bônus x{cli.FocoBonus:0.0}";
		_bonus.AddThemeColorOverride("font_color", cli.FocoBonus > TreinoFocado.PisoDoBonus + 0.05 ? new Color(0.5f, 1f, 0.55f) : Colors.White);
		_anel.Visible = treino;
		_letra.Visible = treino;
		_barra.Visible = med;
		_dica.Text = treino ? "WASD  ·  T para parar" : "espaço na faixa  ·  M para parar";

		if (treino)
		{
			_letra.Text = cli.FocoLetra == '\0' ? "" : cli.FocoLetra.ToString();
			_letra.AddThemeColorOverride("font_color", CorDoPisca());
			_anel.Fracao = _total <= 0 ? 0 : (float)(_resta / _total);
			_anel.Cor = CorDoPisca();
			_anel.QueueRedraw();
		}
		else
		{
			long agora = cli.AgoraDaRespiracao();
			_barra.Posicao = (float)TreinoFocado.Posicao(agora, cli.RespiracaoInicio, cli.RespiracaoPeriodo);
			_barra.CorDoMarcador = CorDoPisca();
			_barra.QueueRedraw();
		}
	}

	private Color CorDoPisca()
	{
		if (_pisca <= 0) return Colors.White;
		return _piscaVerde ? new Color(0.45f, 1f, 0.5f) : new Color(1f, 0.35f, 0.3f);
	}

	public override void _UnhandledInput(InputEvent evento)
	{
		if (!_raiz.Visible || Foco.Digitando) return;
		if (GameClient.Instance is not { } cli) return;
		// O embate e dono do WASD. Engolir a letra aqui faria o jogador perder o QTE calado.
		if (cli.EmClash || cli.QteMudo || cli.FocoPausado) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k) return;

		if (cli.FocoModo == TreinoFocado.ModoTreino && cli.Atividade == Protocol.Activity.Treinando)
		{
			Key t = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
			if (t < Key.A || t > Key.Z) return;
			char letra = (char)('A' + (t - Key.A));
			if (!LetrasDoEmbate.EhLetra(letra)) return;
			if (cli.FocoGeracao == _respVista) { GetViewport().SetInputAsHandled(); return; }
			_respVista = cli.FocoGeracao;
			cli.SendTreinoTecla(letra);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (cli.FocoModo == TreinoFocado.ModoMeditacao && cli.Atividade == Protocol.Activity.Meditando)
		{
			Key t = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
			if (t != Key.Space) return;
			cli.SendRespiracaoTecla();
			GetViewport().SetInputAsHandled();
		}
	}

	public bool NaTela => IsInstanceValid(_raiz) && _raiz.Visible;
	public string TituloNaTela => NaTela && IsInstanceValid(_titulo) ? _titulo.Text : "";
	public string LetraNaTela => NaTela && IsInstanceValid(_letra) && _letra.Visible ? _letra.Text : "";
	public string BonusNaTela => NaTela && IsInstanceValid(_bonus) ? _bonus.Text : "";
	public bool BarraNaTela => NaTela && IsInstanceValid(_barra) && _barra.Visible;
	public bool AnelNaTela => NaTela && IsInstanceValid(_anel) && _anel.Visible;

	/// <summary>A cor da letra agora. A foto do acerto e do erro le isto, nao um bool da bancada.</summary>
	public Color CorDaLetra => IsInstanceValid(_letra) ? _letra.GetThemeColor("font_color") : Colors.White;
}

/// <summary>O anel que fecha. A fracao e o que AINDA resta do prazo que o servidor mandou.</summary>
public partial class AnelDoFoco : Control
{
	public float Fracao = 1;
	public Color Cor = Colors.White;

	public override void _Draw()
	{
		Vector2 c = Size / 2;
		float raio = Math.Min(Size.X, Size.Y) * 0.42f;
		DrawArc(c, raio, 0, Mathf.Tau, 48, new Color(1, 1, 1, 0.18f), 5);
		float fim = -Mathf.Pi / 2 + Mathf.Tau * Mathf.Clamp(Fracao, 0, 1);
		if (Fracao > 0.01f) DrawArc(c, raio, -Mathf.Pi / 2, fim, 48, Cor, 7);
	}
}

/// <summary>A barra da respiracao: a faixa no meio e o marcador que vai e volta.</summary>
public partial class BarraDaRespiracao : Control
{
	public float Posicao;
	public Color CorDoMarcador = Colors.White;

	public override void _Draw()
	{
		var fundo = new Rect2(0, Size.Y * 0.25f, Size.X, Size.Y * 0.5f);
		DrawRect(fundo, new Color(1, 1, 1, 0.12f));
		float x0 = (float)(TreinoFocado.CentroDaFaixa - TreinoFocado.MeiaFaixa) * Size.X;
		float x1 = (float)(TreinoFocado.CentroDaFaixa + TreinoFocado.MeiaFaixa) * Size.X;
		DrawRect(new Rect2(x0, 0, x1 - x0, Size.Y), new Color(0.45f, 0.9f, 0.5f, 0.45f));
		float x = Mathf.Clamp(Posicao, 0, 1) * Size.X;
		DrawRect(new Rect2(x - 3, 0, 6, Size.Y), CorDoMarcador);
	}
}
