using Godot;
using Jandirus.Core.Races;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-19) O `Icer_Form_Settings` DO DM EM JOGO -- "Re-escolher corpos das formas" (`icer.dm:77-83`, aba Other): a mesma tela de
/// formas da criacao, aberta de novo, que aplica os corpos escolhidos e devolve o corpo a forma inicial.
///
/// ============================ POR QUE ELA EXISTE AGORA ============================
/// A criacao prometia (*"pode re-escolher em jogo depois"*) e o menu nao existia. E ele e a saida de quem ja nasceu com os corpos
/// errados: o Mutante criado antes desta leva ganhou os tres corpos do normal nas supressoes (ver `FormasDeFrost.Sanear`).
///
/// ============================ ELA NAO DECIDE NADA ============================
/// O Salvar manda `formas_frost` com os corpos em INDICES do `FormasDeFrost.Catalogo`, e quem confere a raca, a lista e quantos slots
/// ha e o servidor (`GameServer.Frost.ComandoDasFormasDoFrost`). A CLASSE SAI DO TAMANHO DA LISTA que o servidor saneou (sete =
/// Mutante), pelo motivo do `FormasDeFrost.SlotDoDegrau`: o cliente nao recebe classe nenhuma, e a lista ja diz.
/// ===================================================================================
/// </summary>
public partial class TelaDeFormasDoFrost : CanvasLayer
{
	public static TelaDeFormasDoFrost? Instancia { get; private set; }

	private Control _raiz = null!;
	private EscolhaDeFormasDoFrost _escolha = null!;
	private CharacterVisual _boneco = null!;

	/// <summary>Os corpos que a tela esta editando -- uma copia saneada dos que o corpo tem; so viajam no Salvar.</summary>
	private List<string> _corpos = [];

	/// <summary>A ficha visual de quem abriu a tela, pra previa vestir o resto do corpo (a roupa, a pele) por cima dos corpos novos.</summary>
	private (string Raca, string Genero, Jandirus.Core.Appearance.Appearance Ap)? _eu;

	public override void _Ready()
	{
		Instancia = this;
		Layer = 5;   // a altura da tela da armadura: por cima do menu P, de onde ela abre
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

		PanelContainer painel = Tema.Painel1(16);
		centro.AddChild(painel);

		var colunas = new HBoxContainer();
		colunas.AddThemeConstantOverride("separation", 16);
		painel.AddChild(colunas);

		// A PREVIA, como a da criacao: o corpo do degrau aberto, com o resto da aparencia por cima. Ver `Previa`.
		var moldura = new PanelContainer { CustomMinimumSize = new Vector2(180, 230) };
		moldura.AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.PainelClaro, Tema.BordaViva, 10));
		colunas.AddChild(moldura);
		var palco = new Control { ClipContents = true };
		moldura.AddChild(palco);
		_boneco = new CharacterVisual { Name = "Previa", Scale = new Vector2(4, 4) };
		palco.AddChild(_boneco);
		palco.Resized += () => _boneco.Position = new Vector2(palco.Size.X * 0.5f, palco.Size.Y * 0.62f);

		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(470, 0) };
		caixa.AddThemeConstantOverride("separation", 8);
		colunas.AddChild(caixa);

		var titulo = new Label { Text = "AS SUAS FORMAS", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 22);
		caixa.AddChild(titulo);
		caixa.AddChild(new HSeparator());

		_escolha = new EscolhaDeFormasDoFrost();
		_escolha.Mudou += Previa;
		caixa.AddChild(_escolha);

		var botoes = new HBoxContainer();
		botoes.AddThemeConstantOverride("separation", 8);
		var salvar = new Button { Text = "Salvar os corpos", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		salvar.Pressed += Salvar;
		botoes.AddChild(salvar);
		var cancelar = new Button { Text = "Cancelar (Esc)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		cancelar.Pressed += Fechar;
		botoes.AddChild(cancelar);
		caixa.AddChild(botoes);
	}

	/// <summary>
	/// ABRE A TELA com os corpos que este Frost Demon tem agora. Quem nao e Frost Demon ouve por que (o botao nem aparece pra ele -- a
	/// tabela de requisitos esconde --, mas a busca do menu e a tecla de verb sao outras portas).
	/// </summary>
	public void Abrir()
	{
		if (World.Instancia?.MeuLook is not { } eu || !FormasDeFrost.EhFrost(eu.Raca))
		{
			Chat.Sistema("só um Frost Demon escolhe os corpos das formas.");
			return;
		}

		_eu = eu;
		string classe = eu.Ap.FormasDeFrost.Count == FormasDeFrost.Total ? FormasDeFrost.ClasseMutante : FormasDeFrost.ClasseNormal;
		_corpos = FormasDeFrost.Sanear(classe, eu.Ap.FormasDeFrost);
		_escolha.Abrir(classe, _corpos, emJogo: true);
		_raiz.Visible = true;
	}

	/// <summary>O boneco com o corpo do degrau aberto -- a mesma troca de slot da previa da criacao (`CreationScreen.Previa`).</summary>
	private void Previa()
	{
		if (_eu is not { } eu || VistosDeGente.Catalogo is not { } cat) return;
		Jandirus.Core.Appearance.Appearance mostrar = eu.Ap.Copiar();
		mostrar.FormasDeFrost = [.. _corpos];
		int slot = _escolha.SlotAberto;
		if (slot > 0 && slot < mostrar.FormasDeFrost.Count) mostrar.FormasDeFrost[0] = _corpos[slot];
		_boneco.Vestir(cat, mostrar, eu.Raca, eu.Genero);
		_boneco.SetMotion(Jandirus.Core.World.Facing.South, false);
	}

	/// <summary>Manda os corpos pro servidor (em indices do catalogo) e fecha. O servidor responde no chat.</summary>
	private void Salvar()
	{
		GameClient.Instance?.SendVerbo("formas_frost",
			string.Join("|", _corpos.Select(c => Array.IndexOf(FormasDeFrost.Catalogo, c))));
		Fechar();
	}

	public void Fechar() => _raiz.Visible = false;

	/// <summary>
	/// O ESC COM A TELA ABERTA E DELA -- no `_Input`, pelo motivo escrito na `TelaDeArmadura`: o menu de pausa escuta a mesma tecla no
	/// `_UnhandledInput` e e o ultimo node da arvore, entao la ele a receberia primeiro e abriria por cima.
	/// </summary>
	public override void _Input(InputEvent evento)
	{
		if (!_raiz.Visible || Foco.Digitando) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
		if (PauseMenu.Instancia is { Aberto: true }) return;
		Fechar();
		GetViewport().SetInputAsHandled();
	}

	// =====================================================================
	// AS PORTAS DE BANCADA (`--diagmutante`)
	// =====================================================================
	/// <summary>A tela esta aberta? Pra bancada.</summary>
	public bool AbertaDeTeste => IsInstanceValid(_raiz) && _raiz.Visible;

	/// <summary>A escolha da tela. Pra bancada.</summary>
	public EscolhaDeFormasDoFrost EscolhaDeTeste => _escolha;

	/// <summary>O Salvar, como o clique o apertaria. Pra bancada.</summary>
	public void SalvarDeTeste() => Salvar();
}
