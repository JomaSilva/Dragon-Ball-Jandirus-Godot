using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;

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
	private Label _titulo = null!, _nota = null!;
	private Button _sair = null!;

	/// <summary>
	/// PRA QUE A GRADE ESTA ABERTA. E a mesma tela com outra lista e outro verbo:
	///   * a ARMADURA crua, pra moldar (a tela nasceu pra isto);
	///   * os PESOS (dono, 2026-10-13: *"permita tb o jogador a mudar o icone do peso (assim como é quando cria uma
	///     armadura)"*) -- ver <see cref="AbrirPraPesos"/>;
	///   * MATERIALIZAR (dono, 2026-10-13: *"materialize criaria um item no chao (itens como peso, roupa etc)"*) --
	///     ver <see cref="AbrirPraMaterializar"/>;
	///   * A BANCADA DE CRIACAO (v0.2.1, lote T7 da tech): a VITRINE de roupas dela, o PENTE e a TINTA DE OLHOS -- ver
	///     <see cref="AbrirPraVitrine"/>, <see cref="AbrirPraPentear"/> e <see cref="AbrirPraOsOlhos"/>.
	/// </summary>
	private enum Para { Armadura, Pesos, Materializar, Vitrine, Pentear, Olhos }

	/// <summary>O verbo que a vitrine manda (o da acao do menu da tecla E que a abriu).</summary>
	private string _verboDaVitrine = "";

	/// <summary>O botao que so a tinta de olhos mostra: nao ha folha pra clicar, so a cor.</summary>
	private Button _tingirOsOlhos = null!;

	private Para _para;

	/// <summary>O que so o Materializar mostra: o botao dos pesos (que nao sao uma folha da grade) e a cor da roupa.</summary>
	private VBoxContainer _soDoMaterializar = null!;
	private Button _materializarPesos = null!;
	private CheckBox _tingir = null!;
	private ColorPickerButton _cor = null!;

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

		// A CRUA SAIU DA MOCHILA (moldada, jogada fora, roubada): nao ha mais o que moldar. (E os pesos, quando a
		// grade esta aberta pra eles. O Materializar nao depende da mochila.)
		if (_raiz.Visible && !OQueAGradePedeEstaNaMochila()) Fechar();
	}

	/// <summary>O item de que a grade aberta depende ainda esta na mochila? (O Materializar e a vitrine nao dependem de item nenhum.)</summary>
	private bool OQueAGradePedeEstaNaMochila() => _para switch
	{
		Para.Armadura => Cruas() > 0,
		Para.Pesos => Pesos() > 0,
		Para.Pentear => Tem(BancadaDeCriacao.Pente),
		Para.Olhos => Tem(BancadaDeCriacao.TintaDeOlhos),
		_ => true,
	};

	private static bool Tem(string item) => (GameClient.Instance?.Mochila.Quantos(item) ?? 0) > 0;

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

		// (os tres textos sao escritos ao ABRIR -- a grade serve a armadura e aos pesos; ver `Encher`)
		_titulo = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_titulo.AddThemeFontSizeOverride("font_size", 22);
		caixa.AddChild(_titulo);

		_nota = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_nota.AddThemeColorOverride("font_color", Tema.TextoFraco);
		_nota.AddThemeFontSizeOverride("font_size", 12);
		caixa.AddChild(_nota);
		caixa.AddChild(new HSeparator());

		// SO PRO MATERIALIZAR: os pesos (um item, e nao uma folha) e a cor com que a roupa nasce.
		_soDoMaterializar = new VBoxContainer { Visible = false };
		_soDoMaterializar.AddThemeConstantOverride("separation", 6);
		_materializarPesos = new Button { Text = "Materializar PESOS DE TREINO  (leves, 50 kg -- o peso máximo se compra)" };
		_materializarPesos.Pressed += () =>
		{
			GameClient.Instance?.SendVerbo(Materializacao.VerboDeCanal, Materializacao.DePesos);
			Fechar();
		};
		_soDoMaterializar.AddChild(_materializarPesos);
		var linhaDaCor = new HBoxContainer();
		linhaDaCor.AddThemeConstantOverride("separation", 8);
		_tingir = new CheckBox { Text = "...ou uma ROUPA (clique nela). Tingir desta cor:" };
		_cor = new ColorPickerButton { CustomMinimumSize = new Vector2(72, 28), Color = new Color(0.16f, 0.16f, 0.2f), EditAlpha = false };
		linhaDaCor.AddChild(_tingir);
		linhaDaCor.AddChild(_cor);
		_soDoMaterializar.AddChild(linhaDaCor);
		_tingirOsOlhos = new Button { Text = "Tingir os olhos desta cor", Visible = false };
		_tingirOsOlhos.Pressed += () =>
		{
			GameClient.Instance?.SendVerbo($"item_{BancadaDeCriacao.AcaoTingirOsOlhos}",
				$"{BancadaDeCriacao.TintaDeOlhos}/{_cor.Color.ToHtml(false).ToUpperInvariant()}");
			Fechar();
		};
		_soDoMaterializar.AddChild(_tingirOsOlhos);
		caixa.AddChild(_soDoMaterializar);

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

		_sair = new Button();
		_sair.Pressed += Fechar;
		caixa.AddChild(_sair);
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
		_para = Para.Armadura;
		Encher(ArmaduraDeTech.Escolhiveis(cat), "A ARTE DA ARMADURA",
			   "Escolha como a armadura vai ser. Depois de moldada ela vai pra mochila, pronta pra vestir.",
			   "Escolher depois (Esc) -- a armadura fica na mochila");
	}

	private static int Pesos() => GameClient.Instance?.Mochila.Quantos(CatalogoDeItens.Pesos) ?? 0;

	/// <summary>
	/// A MESMA GRADE, PROS PESOS -- o `verb/Icon()` deles (`Tier 1.dm:167-175`). Oferece o guarda-roupa inteiro
	/// (`PesoDeTreino.Escolhiveis`): os pesos SAO roupa no original, e a folha de fabrica e uma camisa. O clique manda
	/// `item_arte Weights/&lt;arte&gt;`; quem guarda a escolha e reapresenta o corpo e o servidor
	/// (`GameServer.EscolherAArteDosPesos`).
	/// </summary>
	public void AbrirPraPesos()
	{
		if (Pesos() == 0 || VistosDeGente.Catalogo is not { } cat) return;
		_para = Para.Pesos;
		Encher(PesoDeTreino.Escolhiveis(cat), "A ARTE DOS PESOS",
			   "Escolha como os pesos aparecem no corpo quando você os veste.",
			   "Deixar como está (Esc)");
	}

	/// <summary>
	/// A MESMA GRADE, PRA MATERIALIZAR -- a escolha que o ritual do original fazia por perguntas ("Make what?",
	/// "Custom icon?", "How much red?", `Rituals_Manipulation.dm:528-568`). Em cima, o botao dos pesos; embaixo, o
	/// guarda-roupa inteiro, e a cor com que a peca nasce (desmarcado: a cor do desenho). O clique manda
	/// `materializar pesos` ou `materializar roupa/&lt;folha&gt;[/&lt;RRGGBB&gt;]`; quem cria o item, cobra o Ki e
	/// recusa e o servidor (`GameServer.MaterializarG15`). Devolve falso se a tela nao pode abrir (sem catalogo).
	/// </summary>
	public bool AbrirPraMaterializar()
	{
		if (VistosDeGente.Catalogo is not { } cat) return false;
		_para = Para.Materializar;
		Encher(PesoDeTreino.Escolhiveis(cat), "MATERIALIZAR",
			   "O que você escolher toma forma no chão, aos seus pés. Custa uma boa parte do seu Ki -- menos pra quem tem perícia esotérica.",
			   "Deixar pra depois (Esc)");
		return true;
	}

	/// <summary>Poe as artes na grade e abre a tela, com os textos de quem a abriu.</summary>
	private void Encher(List<(string Nome, string Caminho)> artes, string titulo, string nota, string sair)
	{
		_titulo.Text = titulo;
		_nota.Text = nota;
		_sair.Text = sair;
		// A LINHA DA COR serve a quem tinge (o Materializar, a vitrine, o pente e os olhos); o botao dos pesos e so do
		// Materializar, e o de tingir os olhos e so da tinta.
		_soDoMaterializar.Visible = _para is Para.Materializar or Para.Vitrine or Para.Pentear or Para.Olhos;
		_materializarPesos.Visible = _para == Para.Materializar;
		_tingirOsOlhos.Visible = _para == Para.Olhos;
		_tingir.Visible = _para != Para.Olhos;
		_tingir.Text = _para switch
		{
			Para.Vitrine => "Tingir a peça desta cor:",
			Para.Pentear => "Tingir o cabelo desta cor:",
			_ => "...ou uma ROUPA (clique nela). Tingir desta cor:",
		};

		foreach (Node n in _grade.GetChildren()) n.QueueFree();
		foreach ((string nome, string caminho) in artes)
		{
			var b = new Button
			{
				CustomMinimumSize = new Vector2(LadoDoSlot, LadoDoSlot),
				// (na vitrine da bancada cada peca diz o preco dela)
				TooltipText = _para == Para.Vitrine && BancadaDeCriacao.PrecoDaRoupa(nome) is { } preco ? $"{nome} -- {preco:N0} zeni" : nome,
				ExpandIcon = true,
				IconAlignment = HorizontalAlignment.Center,
			};
			b.SetMeta("arte", nome);
			if (caminho.Length > 0 && Miniatura(caminho) is { } icone) b.Icon = icone;
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
		switch (_para)
		{
			case Para.Pesos:
				GameClient.Instance?.SendVerbo($"item_{PesoDeTreino.AcaoArte}", $"{CatalogoDeItens.Pesos}/{arte}");
				break;
			case Para.Materializar:
				string cor = _tingir.ButtonPressed ? "/" + _cor.Color.ToHtml(false).ToUpperInvariant() : "";
				GameClient.Instance?.SendVerbo(Materializacao.VerboDeCanal, $"{Materializacao.DeRoupa}/{arte}{cor}");
				break;
			case Para.Vitrine:
				GameClient.Instance?.SendVerbo(_verboDaVitrine, $"{arte}{CorMarcada()}");
				break;
			case Para.Pentear:
				GameClient.Instance?.SendVerbo($"item_{BancadaDeCriacao.AcaoPentear}", $"{BancadaDeCriacao.Pente}/{arte}{CorMarcada()}");
				break;
			default:
				GameClient.Instance?.SendVerbo($"item_{ArmaduraDeTech.AcaoMoldar}", $"{ArmaduraDeTech.Id}/{arte}");
				break;
		}
		Fechar();
	}

	/// <summary>A cor marcada na linha de tingir, como ela viaja no pedido (`/RRGGBB`) -- ou vazio, sem tingir.</summary>
	private string CorMarcada() => _tingir.ButtonPressed ? "/" + _cor.Color.ToHtml(false).ToUpperInvariant() : "";

	/// <summary>
	/// A MESMA GRADE, PRA VITRINE DA BANCADA DE CRIACAO -- o `ClothingChoice()` do original (`ClothesChoose.dm`), que la
	/// e uma janela com as pecas e duas perguntas (*"This will cost [N] zenni, accept?"* e a cor). So as pecas que a
	/// vitrine de la vende (`BancadaDeCriacao.PrecoDaRoupa`), com o preco na dica de cada uma. O clique manda o verbo da
	/// acao que abriu a tela, com `&lt;folha&gt;[/&lt;RRGGBB&gt;]`; quem confere a bancada, cobra e entrega e o servidor
	/// (`GameServer.FazerARoupa`). Devolve falso se a tela nao pode abrir (sem catalogo).
	/// </summary>
	public bool AbrirPraVitrine(string verbo)
	{
		if (VistosDeGente.Catalogo is not { } cat) return false;
		_para = Para.Vitrine;
		_verboDaVitrine = verbo;
		Encher([.. PesoDeTreino.Escolhiveis(cat).Where(p => BancadaDeCriacao.PrecoDaRoupa(p.Nome) != null)], "A VITRINE DA BANCADA",
			   "Clique numa peça pra fazê-la: ela vai pra mochila. O preço de cada uma aparece ao passar o mouse.",
			   "Fechar (Esc)");
		return true;
	}

	/// <summary>
	/// A MESMA GRADE, PRO PENTE -- o `Change_Hair()` do original (`Hair(1)`: a cor e depois o penteado). Os penteados sao
	/// os do catalogo; o clique manda `item_pentear Comb/&lt;penteado&gt;[/&lt;RRGGBB&gt;]`, e quem troca o cabelo e o
	/// servidor (`GameServer.Pentear`). Devolve falso se a tela nao pode abrir (sem pente, ou sem catalogo).
	/// </summary>
	public bool AbrirPraPentear()
	{
		if (!Tem(BancadaDeCriacao.Pente) || VistosDeGente.Catalogo is not { } cat) return false;
		_para = Para.Pentear;
		Encher([.. cat.Cabelos.Select(c => (c.Nome, c.Sprite ?? ""))], "O PENTE",
			   "Clique num penteado. Marque a cor pra tingir o cabelo junto (o de um Saiyajin não pega tinta).",
			   "Deixar como está (Esc)");
		return true;
	}

	/// <summary>
	/// A MESMA TELA, PRA TINTA DE OLHOS -- o `Change_Eye_Color()` do original: nao ha folha pra escolher, so a cor e o
	/// botao. Manda `item_tingir_olhos Eye_Dye/&lt;RRGGBB&gt;`. Devolve falso se a tela nao pode abrir (sem a tinta).
	/// </summary>
	public bool AbrirPraOsOlhos()
	{
		if (!Tem(BancadaDeCriacao.TintaDeOlhos)) return false;
		_para = Para.Olhos;
		Encher([], "A TINTA DE OLHOS", "Escolha a cor e confirme.", "Deixar como está (Esc)");
		return true;
	}

	/// <summary>A grade esta aberta pra vitrine da bancada? SO PRA BANCADA.</summary>
	public bool PraVitrineDeTeste => _para == Para.Vitrine && AbertaDeTeste;

	/// <summary>A grade esta aberta pro pente? SO PRA BANCADA.</summary>
	public bool PraPentearDeTeste => _para == Para.Pentear && AbertaDeTeste;

	/// <summary>A tela esta aberta pra tinta de olhos? SO PRA BANCADA.</summary>
	public bool PraOsOlhosDeTeste => _para == Para.Olhos && AbertaDeTeste;

	/// <summary>Aperta o botao de tingir os olhos -- o mesmo sinal do dedo. Falso se ele nao esta na tela. SO PRA BANCADA.</summary>
	public bool TingirOsOlhosDeTeste()
	{
		if (!PraOsOlhosDeTeste || !_tingirOsOlhos.IsVisibleInTree()) return false;
		_tingirOsOlhos.EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}

	/// <summary>A grade esta aberta pros pesos? SO PRA BANCADA.</summary>
	public bool PraPesosDeTeste => _para == Para.Pesos && AbertaDeTeste;

	/// <summary>A grade esta aberta pra materializar? SO PRA BANCADA.</summary>
	public bool PraMaterializarDeTeste => _para == Para.Materializar && AbertaDeTeste;

	/// <summary>Aperta o botao dos pesos do Materializar -- o mesmo sinal do dedo. Falso se ele nao esta na tela. SO PRA BANCADA.</summary>
	public bool MaterializarPesosDeTeste()
	{
		if (!PraMaterializarDeTeste || !_materializarPesos.IsVisibleInTree()) return false;
		_materializarPesos.EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}

	/// <summary>Marca (ou desmarca, com nulo) a cor com que a roupa materializada nasce. SO PRA BANCADA.</summary>
	public void TingirDeTeste(Color? cor)
	{
		_tingir.ButtonPressed = cor != null;
		if (cor is { } c) _cor.Color = c;
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
