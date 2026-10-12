using Godot;
using Jandirus.Core.Items;

namespace Jandirus.Client;

/// <summary>
/// A TELA DE ESCOLHER A ARTE DA ARMADURA (dono, 2026-10-12): *"a sub pasta "Armor" sao sprites da armadura q vc pode
/// fabricar com tech, q ao fabricar uma armadura da tech, vai abrir uma tela pra vc escolher qual sprite vc quer"*.
///
/// E o `verb/Icon()` do original (`Armor.dm:67-125`), que la e uma lista de nomes ("Plating Style", "Elite Armor"...)
/// liberada por nivel de tecnologia. Aqui e uma GRADE DE DESENHOS -- pelo mesmo motivo da mochila: reconhecer um
/// desenho e instantaneo, ler cinquenta nomes nao e -- e oferece todas as folhas da pasta (`ArmaduraDeTech.Escolhiveis`).
///
/// ============================ ELA ABRE DE DOIS JEITOS, E NAO DECIDE NADA ============================
///   * SOZINHA, depois de fabricar: a aba Tech avisa que pediu a fabricacao (<see cref="EsperarAFabricada"/>), e a
///     tela so abre quando a MOCHILA chega com uma armadura crua a mais -- ou seja, quando o servidor entregou. Um
///     pedido recusado (sem zeni, mochila cheia) nao abre nada.
///   * PELA MOCHILA: a armadura crua tem a acao "Escolher a arte". E por ela que entra quem fechou a tela sem
///     escolher, e a armadura que ja estava guardada desde antes desta mudanca.
/// O clique manda `item_moldar Armor/&lt;arte&gt;`, e quem troca o item cru pela peca pronta e o servidor
/// (`GameServer.MoldarAArmadura`), que confere a arte de novo.
/// ====================================================================================================
/// </summary>
public partial class TelaDeArmadura : CanvasLayer
{
	public static TelaDeArmadura? Instancia { get; private set; }

	private const int Colunas = 9;
	private const int LadoDoSlot = 64;

	/// <summary>Quanto tempo a tela espera a mochila confirmar a fabricacao antes de desistir de abrir sozinha.</summary>
	private const ulong EsperaDaFabricadaMs = 5000;

	private Control _raiz = null!;
	private GridContainer _grade = null!;

	/// <summary>Quantas armaduras cruas havia na mochila quando a fabricacao foi pedida; -1 = nao espera nenhuma.</summary>
	private int _cruasAntes = -1;
	private ulong _esperaAte;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagv020`): fabricar nao abre a tela -- o jogo de antes, em que a armadura caia
	/// na mochila e ficava la. Lido pelo proprio gatilho, em <see cref="AoMudarMochila"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool NaoAbreAoFabricarDeTeste;

	public override void _Ready()
	{
		Instancia = this;
		Layer = 5;   // por cima da mochila (4): quem a abre pela mochila ve a grade na frente
		Montar();
		if (GameClient.Instance is { } cli) cli.MochilaMudou += AoMudarMochila;
	}

	/// <summary>Solta a assinatura -- o `GameClient` sobrevive ao logout (ver a nota no `MenuJogo._Ready`).</summary>
	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.MochilaMudou -= AoMudarMochila;
		if (Instancia == this) Instancia = null;
	}

	private static int Cruas() => GameClient.Instance?.Mochila.Quantos(ArmaduraDeTech.Id) ?? 0;

	/// <summary>
	/// A ABA TECH ACABOU DE PEDIR UMA ARMADURA. A tela nao abre aqui: abre quando a mochila chegar com uma crua a
	/// mais, que e a prova de que o servidor fabricou.
	/// </summary>
	public void EsperarAFabricada()
	{
		_cruasAntes = Cruas();
		_esperaAte = Time.GetTicksMsec() + EsperaDaFabricadaMs;
	}

	private void AoMudarMochila()
	{
		if (_cruasAntes >= 0)
		{
			bool chegou = Cruas() > _cruasAntes;
			if (chegou || Time.GetTicksMsec() > _esperaAte) _cruasAntes = -1;
			if (chegou && !NaoAbreAoFabricarDeTeste) { Abrir(); return; }
		}

		// A CRUA SAIU DA MOCHILA (moldada, jogada fora, roubada): nao ha mais o que moldar.
		if (_raiz.Visible && Cruas() == 0) Fechar();
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

		var caixa = new VBoxContainer();
		caixa.AddThemeConstantOverride("separation", 8);
		painel.AddChild(caixa);

		var titulo = new Label { Text = "A ARTE DA ARMADURA", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 22);
		caixa.AddChild(titulo);

		var nota = new Label
		{
			Text = "Escolha como a armadura vai ser. Depois de moldada ela vai pra mochila, pronta pra vestir.",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		nota.AddThemeColorOverride("font_color", Tema.TextoFraco);
		nota.AddThemeFontSizeOverride("font_size", 12);
		caixa.AddChild(nota);
		caixa.AddChild(new HSeparator());

		// A GRADE ROLA: sao mais de cinquenta artes, e a janela do jogo pode ser pequena.
		var rolagem = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(Colunas * (LadoDoSlot + 6) + 14, 5 * (LadoDoSlot + 6)),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		_grade = new GridContainer { Columns = Colunas };
		_grade.AddThemeConstantOverride("h_separation", 6);
		_grade.AddThemeConstantOverride("v_separation", 6);
		rolagem.AddChild(_grade);
		caixa.AddChild(rolagem);

		var depois = new Button { Text = "Escolher depois (Esc) -- a armadura fica na mochila" };
		depois.Pressed += Fechar;
		caixa.AddChild(depois);
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagv020`): o Esc da tela aberta e ouvido no `_UnhandledInput` -- onde o menu de
	/// pausa o recebe ANTES (ele e o ultimo da arvore) e abre por cima, com a grade aberta atras. Foi como a primeira
	/// versao desta tela nasceu, e a bancada pegou. Lido pelas duas portas de entrada. Falso em jogo, sempre.
	/// </summary>
	public static bool EscSoDepoisDaPausaDeTeste;

	/// <summary>
	/// O ESC COM A GRADE ABERTA E DA GRADE -- ouvido AQUI, no `_Input`, pelo mesmo motivo escrito no
	/// `MenuDeInteracao._Input`: o menu de pausa escuta a mesma tecla no `_UnhandledInput` e e o ultimo node da arvore,
	/// entao la ele a receberia primeiro. O `_Input` roda antes: a coisa mais na frente da tela responde primeiro, e a
	/// pausa so abre quando nao ha mais nada pra fechar. Com a pausa JA aberta o Esc e dela.
	/// </summary>
	public override void _Input(InputEvent evento)
	{
		if (EscSoDepoisDaPausaDeTeste) return;
		FecharComOEsc(evento);
	}

	public override void _UnhandledInput(InputEvent evento)
	{
		if (EscSoDepoisDaPausaDeTeste) FecharComOEsc(evento);
	}

	private void FecharComOEsc(InputEvent evento)
	{
		if (!_raiz.Visible || Foco.Digitando) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
		if (PauseMenu.Instancia is { Aberto: true }) return;
		Fechar();
		GetViewport().SetInputAsHandled();
	}

	/// <summary>Abre a grade -- se houver uma armadura crua na mochila pra moldar.</summary>
	public void Abrir()
	{
		if (Cruas() == 0 || VistosDeGente.Catalogo is not { } cat) return;

		foreach (Node n in _grade.GetChildren()) n.QueueFree();
		foreach ((string nome, string caminho) in ArmaduraDeTech.Escolhiveis(cat))
		{
			var b = new Button
			{
				CustomMinimumSize = new Vector2(LadoDoSlot, LadoDoSlot),
				TooltipText = nome,
				ExpandIcon = true,
				IconAlignment = HorizontalAlignment.Center,
			};
			b.SetMeta("arte", nome);
			if (Miniatura(caminho) is { } icone) b.Icon = icone;
			else b.Text = nome.Length > 6 ? nome[..6] : nome;
			string escolhida = nome;
			b.Pressed += () => Escolher(escolhida);
			_grade.AddChild(b);
		}
		_raiz.Visible = true;
	}

	/// <summary>O retrato de uma folha de armadura: de frente, parada -- ou andando, que e a pose que muita peca traz.</summary>
	private static Texture2D? Miniatura(string caminho) =>
		Miniaturas.De(caminho, Miniaturas.TemEstado(caminho, "default_south") ? "default_south" : "walk_south");

	private void Escolher(string arte)
	{
		GameClient.Instance?.SendVerbo($"item_{ArmaduraDeTech.AcaoMoldar}", $"{ArmaduraDeTech.Id}/{arte}");
		Fechar();
	}

	public void Fechar() => _raiz.Visible = false;

	// =====================================================================
	// AS PORTAS DE BANCADA (`--diagv020`)
	// =====================================================================
	/// <summary>A tela esta aberta? SO PRA BANCADA.</summary>
	public bool AbertaDeTeste => IsInstanceValid(_raiz) && _raiz.Visible;

	/// <summary>As artes que a grade oferece agora, na ordem dos botoes, e quantas tem desenho. SO PRA BANCADA.</summary>
	public (List<string> Artes, int ComDesenho) GradeDeTeste()
	{
		var artes = new List<string>();
		int comDesenho = 0;
		foreach (Node n in _grade.GetChildren())
		{
			if (n is not Button b || b.IsQueuedForDeletion()) continue;
			artes.Add(b.GetMeta("arte").AsString());
			if (b.Icon != null) comDesenho++;
		}
		return (artes, comDesenho);
	}

	/// <summary>Aperta o botao de uma arte -- o mesmo sinal do dedo. Falso se ela nao esta na grade. SO PRA BANCADA.</summary>
	public bool EscolherDeTeste(string arte)
	{
		foreach (Node n in _grade.GetChildren())
		{
			if (n is not Button b || b.IsQueuedForDeletion() || b.GetMeta("arte").AsString() != arte) continue;
			b.EmitSignal(BaseButton.SignalName.Pressed);
			return true;
		}
		return false;
	}
}
