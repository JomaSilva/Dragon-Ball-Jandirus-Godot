using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A CONFIRMACAO DO "MORRER" (2026-10-27, o pedido 124d) ============================
/// O verbo `Morrer` do menu P > Other abre esta caixa, e so o botao "Morrer" dela manda o `morrer` ao servidor -- a escolha do dono,
/// "Botao com confirmacao". O foco nasce no "Cancelar": um Enter apertado sem ler nao mata ninguem.
///
/// O molde e o da `TelaDeRefugio` (`CanvasLayer` + `Control` ancorado + fundo escuro + `CenterContainer` + `Tema.Painel1`), e nao o
/// `ConfirmationDialog` do Godot -- a historia esta na `CharacterSelect.PerguntarSeApaga`. ESC fecha sem morrer.
/// </summary>
public partial class TelaDeMorrer : CanvasLayer
{
	/// <summary>A tela viva, ou nula antes do mundo. O verbo a abre por aqui.</summary>
	public static TelaDeMorrer? Instancia { get; private set; }

	private Control _raiz = null!;
	private Button _morrer = null!, _cancelar = null!;

	public override void _Ready()
	{
		Layer = 4;   // a mesma do menu da tecla E e da tela do refugio
		Instancia = this;

		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);
		var fundo = new ColorRect
		{
			Color = new Color(0, 0, 0, 0.72f),
			AnchorRight = 1, AnchorBottom = 1,
			GrowHorizontal = Control.GrowDirection.Both,
			GrowVertical = Control.GrowDirection.Both,
		};
		_raiz.AddChild(fundo);
		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_raiz.AddChild(centro);
		PanelContainer painel = Tema.Painel1(18);
		centro.AddChild(painel);
		var coluna = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
		coluna.AddThemeConstantOverride("separation", 10);
		painel.AddChild(coluna);

		var titulo = new Label { Text = "MORRER?", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 20);
		titulo.AddThemeColorOverride("font_color", Tema.Perigo);
		coluna.AddChild(titulo);
		var texto = new Label
		{
			Text = "O seu personagem morre agora -- uma morte comum: vai pro Outro Mundo, sem Zenkai e sem mexer no karma. "
				 + "Voltar à vida é como sempre (o Enma, as esferas, uma técnica).",
			AutowrapMode = TextServer.AutowrapMode.Word,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		texto.AddThemeColorOverride("font_color", Tema.TextoFraco);
		texto.AddThemeFontSizeOverride("font_size", 13);
		coluna.AddChild(texto);

		var botoes = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		botoes.AddThemeConstantOverride("separation", 12);
		coluna.AddChild(botoes);
		_morrer = new Button { Text = "Morrer", CustomMinimumSize = new Vector2(140, 0) };
		_morrer.AddThemeColorOverride("font_color", Tema.Perigo);
		_morrer.Pressed += Confirmar;
		botoes.AddChild(_morrer);
		_cancelar = new Button { Text = "Cancelar", CustomMinimumSize = new Vector2(140, 0) };
		_cancelar.Pressed += Fechar;
		botoes.AddChild(_cancelar);
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
	}

	public void Abrir()
	{
		_raiz.Visible = true;
		_cancelar.CallDeferred(Control.MethodName.GrabFocus);
	}

	public void Fechar() => _raiz.Visible = false;

	/// <summary>A caixa esta na tela? Le o NODE, e nao um `bool` proprio -- ver `TelaDeMeditacao.NaTela`.</summary>
	public bool NaTela => IsInstanceValid(_raiz) && _raiz.Visible;

	/// <summary>O botao "Morrer": manda o verbo e fecha. O servidor confere tudo de novo (ja morto, dentro da mente, o seguro).</summary>
	private void Confirmar()
	{
		GameClient.Instance?.SendVerbo("morrer");
		Fechar();
	}

	/// <summary>Os dois botoes pelo robo de fotos, pelo mesmo sinal do clique.</summary>
	internal void MorrerDeTeste() => _morrer.EmitSignal(BaseButton.SignalName.Pressed);
	internal void CancelarDeTeste() => _cancelar.EmitSignal(BaseButton.SignalName.Pressed);
	internal bool CancelarComFocoDeTeste => _cancelar.HasFocus();

	/// <summary>ESC fecha sem morrer -- `_Input`, e nao `_UnhandledInput`, pelo motivo da `TelaDeRefugio`.</summary>
	public override void _Input(InputEvent evento)
	{
		if (!NaTela || Foco.Digitando) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k || k.Keycode != Key.Escape) return;
		Fechar();
		GetViewport().SetInputAsHandled();
	}
}
