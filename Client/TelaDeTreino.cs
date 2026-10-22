using Godot;

namespace Jandirus.Client;

/// <summary>
/// A TECLA T PERGUNTA: treinar normal, ou treino focado?
///
/// O normal e o treino parado de hoje (da pra deixar o personagem AFK). O focado e o anel de WASD.
/// Esta tela nao decide ganho e nao trava tecla -- o mesmo desenho da <see cref="TelaDeMeditacao"/>,
/// de proposito: ela nao entra no <see cref="Foco"/>, e um golpe com ela aberta so a fecha.
/// </summary>
public partial class TelaDeTreino : CanvasLayer
{
	public static TelaDeTreino? Instancia { get; private set; }

	private Control _raiz = null!;
	private Button _normal = null!;
	private Button _focado = null!;
	private Action<EscolhaDoTreino>? _resposta;
	private Jandirus.Core.World.ZoneKey _zonaDeAbertura;

	public override void _Ready()
	{
		Layer = 4;
		Instancia = this;
		Montar();
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
	}

	private void Montar()
	{
		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_raiz.AddChild(centro);

		PanelContainer painel = Tema.Painel1(14);
		centro.AddChild(painel);

		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
		caixa.AddThemeConstantOverride("separation", 6);
		painel.AddChild(caixa);

		var titulo = new Label { Text = "Treinar", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 20);
		caixa.AddChild(titulo);
		caixa.AddChild(new HSeparator());

		_normal = new Button
		{
			Text = "Treinar normal",
			TooltipText = "Treina onde você está, no ritmo de sempre. Dá pra deixar o personagem parado — "
						  + "qualquer movimento tira você do treino.",
		};
		_normal.Pressed += () => Responder(EscolhaDoTreino.Normal);
		caixa.AddChild(_normal);

		_focado = new Button
		{
			Text = "Treino focado",
			TooltipText = "Letras WASD num anel que fecha. O corpo fica parado: o WASD não anda. "
						  + "Cada acerto sobe o ganho, até o dobro; errar derruba o bônus e custa vigor.",
		};
		_focado.Pressed += () => Responder(EscolhaDoTreino.Focado);
		caixa.AddChild(_focado);

		caixa.AddChild(new HSeparator());
		var cancelar = new Button { Text = "Cancelar (Esc)" };
		cancelar.Pressed += Fechar;
		caixa.AddChild(cancelar);
	}

	public void Abrir(Action<EscolhaDoTreino> resposta)
	{
		// (2026-10-21, revisao) UMA TELINHA POR VEZ: o T aberto e o M apertado deixavam as duas na tela, e a barra da respiracao
		// nascia por cima das opcoes do treino (a foto `treinofocado-5-respiracao` da r1). Quem abre uma fecha a outra.
		if (!TelasJuntasDeTeste) TelaDeMeditacao.Instancia?.Fechar();
		_resposta = resposta;
		_zonaDeAbertura = GameClient.Instance?.Zone ?? default;
		_raiz.Visible = true;
	}

	/// <summary>
	/// DEFEITO INJETADO (robo <c>--diagtreinofocado</c>): as telinhas do T e do M abrem uma por cima da outra -- o jogo da r1.
	/// Lido pelos dois <c>Abrir</c>. Falso em jogo, sempre.
	/// </summary>
	public static bool TelasJuntasDeTeste;

	public void Fechar()
	{
		_resposta = null;
		_raiz.Visible = false;
	}

	private void Responder(EscolhaDoTreino escolha)
	{
		Action<EscolhaDoTreino>? quem = _resposta;
		Fechar();
		quem?.Invoke(escolha);
	}

	public override void _Process(double delta)
	{
		if (!_raiz.Visible) return;
		if (GameClient.Instance is not { } cli) { Fechar(); return; }
		if (LocalPlayer.Caido(cli.Sheet)) { Fechar(); return; }
		if (cli.Zone.Hash != _zonaDeAbertura.Hash) { Fechar(); return; }
	}

	public override void _Input(InputEvent evento)
	{
		if (!_raiz.Visible || Foco.Digitando) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k) return;
		if (k.Keycode != Key.Escape) return;
		Fechar();
		GetViewport().SetInputAsHandled();
	}

	public bool NaTela => IsInstanceValid(_raiz) && _raiz.Visible;

	public List<string> BotoesDesenhados()
	{
		var r = new List<string>();
		if (!NaTela) return r;
		foreach (Button b in new[] { _normal, _focado })
			if (IsInstanceValid(b)) r.Add(b.Disabled ? $"{b.Text} (apagado)" : b.Text);
		return r;
	}

	public bool ApertarDesenhado(EscolhaDoTreino escolha)
	{
		if (!NaTela) return false;
		Button b = escolha == EscolhaDoTreino.Focado ? _focado : _normal;
		if (!IsInstanceValid(b) || b.Disabled) return false;
		b.EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}
}
