using Godot;

namespace Jandirus.Client;

/// <summary>
/// **O MENU P COMO JANELA** -- arrastar pela barra do titulo, redimensionar pelas bordas e, pelo botao
/// "Destacar", sair da tela do jogo pra uma janela do sistema. Pedido do dono de 2026-10-10: *"podendo
/// redimensionar ou mudar de lugar o menu P como se fosse uma janela do windows, ou ate mesmo ter a opcao de
/// abrir o menu P fora da janela do jogo em outra janela assim podendo colocar o menu P no segundo monitor"*.
///
/// ============================ EMBUTIDO (o padrao) ============================
/// Quem nunca mexeu ve o menu de sempre: centrado, 760 x 580, montado pelas MESMAS ancoras do `Montar` (e nao
/// por uma conta equivalente -- as bancadas medem esse retangulo). O primeiro arrasto troca as ancoras por um
/// retangulo explicito, guardado como fracao da tela (<see cref="ArranjoDaInterface"/>) e preso nela pelo
/// <see cref="Janelas.MenuNaTela"/> toda vez que e aplicado.
///
///   * A BARRA DO TITULO (o nome do personagem, a esquerda da busca) arrasta;
///   * AS QUATRO BORDAS e os quatro cantos redimensionam -- acendem com o mouse em cima;
///   * a pagina da aba ganha rolagem HORIZONTAL quando o menu fica mais estreito que ela (de fabrica a rolagem
///     horizontal continua desligada, como sempre: ali quem manda na largura e a pagina).
///
/// ============================ DESTACADO ============================
/// O conteudo inteiro do menu (a `_raiz`: o painel, a ficha de skill, o teclado do radio) muda de pai: sai da
/// camada do jogo e entra num no `Window` NATIVO (`ForceNative`), que o Windows move, redimensiona e leva pra
/// outro monitor. A raiz do jogo continua embutindo as subjanelas dela (as dicas, os seletores e as contas de
/// "o mouse esta em cima de que" das outras telas nao mudam em nada) -- so ESTA janela e do sistema.
///
/// AS DECISOES (o dono pediu que ficassem escritas):
///   * **TECLA P**: continua sendo "abre/fecha o menu", nas duas janelas. Destacado, fechar e esconder a
///     janela; o proximo P a reabre no mesmo lugar, no mesmo monitor. Quem quer o menu SEMPRE na segunda tela
///     simplesmente nao aperta P.
///   * **O X DA JANELA**: fecha o menu, como o P (<see cref="FecharNoXEncaixa"/> troca por "encaixar de volta").
///     Voltar pra dentro do jogo e o botao "Encaixar", no mesmo lugar do "Destacar".
///   * **ESC**: na janela do MENU, desfaz uma camada dele (ficha, busca, arvore) e por fim o fecha -- o ESC de
///     sempre. Na janela do JOGO, com o menu destacado, o ESC nao e do menu: vai pra pausa.
///   * **O TECLADO**: o Windows entrega a tecla a janela que tem o foco. Com o foco na janela do menu:
///       - o que se SEGURA (andar, guarda, carregar ki) funciona igual -- o jogo le o estado das teclas, e ele e
///         um so pras duas janelas;
///       - o que se APERTA (P, TAB, E, I, B, os atalhos de tecnica, Enter do chat) e REPASSADO: a tecla e
///         oferecida a tela do jogo antes de a interface da janela a ver (<see cref="TeclaNaJanela"/>);
///       - escrevendo na busca do menu, as teclas sao da busca -- mas so enquanto a janela dele tem o teclado.
///         Clicou no jogo, o campo solta o foco: uma busca esquecida aberta noutro monitor nao deixa o
///         personagem mudo.
///   * **A JANELA LEMBRA** monitor, canto e tamanho. Se o monitor gravado sumiu, nasce no principal
///     (<see cref="Janelas.LugarDaJanela"/>).
///   * **A ESCALA** do conteudo e a mesma da tela do jogo naquele momento (em tela cheia de 1920 com base de 1280
///     o menu sai 1,5x, como o embutido), ate o que couber no monitor de destino.
///
/// Embutido continua sendo o PADRAO, e o unico modo onde nao ha janela do sistema pra abrir (`--headless`).
/// </summary>
public partial class MenuJogo
{
	/// <summary>
	/// O X da janela destacada ENCAIXA o menu de volta (verdadeiro) ou so o FECHA, como o P (falso)? Fechar: e o
	/// que um X faz, o menu reabre no mesmo monitor no proximo P, e encaixar ja tem botao proprio.
	/// </summary>
	public static readonly bool FecharNoXEncaixa = false;

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagjanelas): a tecla que cai na janela destacada morre nela -- com o foco no
	/// menu, P nao fecha, E nao interage, o atalho de tecnica nao dispara. Lido pela propria linha do repasse.
	/// </summary>
	public static bool SemRepasseDeTeclasDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagjanelas): o campo de texto da janela destacada continua contando como
	/// "tem alguem escrevendo" depois de a janela perder o teclado -- o personagem fica mudo com o foco no jogo.
	/// </summary>
	public static bool FocoPresoNaJanelaDeTeste;

	/// <summary>
	/// SO PRA BANCADA: faz de conta que a janela destacada tem (ou nao tem) o teclado. A janela de bancada nasce
	/// sem foco de proposito (`--semfoco`: o dono esta usando o PC), entao o foco de verdade nunca chega nela.
	/// </summary>
	public static bool? JanelaComOTecladoDeTeste;

	private AlcaDeJanela _puxador = null!, _moldura = null!;
	private Button _btDestacar = null!;
	private ScrollContainer _rolagemDaPagina = null!;

	/// <summary>A chapa de vidro do painel embutido, guardada enquanto ele veste a chapa da janela.</summary>
	private StyleBox? _vidro;

	/// <summary>A janela do sistema onde o menu mora quando esta destacado. Nula = embutido.</summary>
	private Window? _janela;

	/// <summary>O lugar da janela mudou desde a ultima gravacao do `config.json`?</summary>
	private bool _lugarPorGravar;

	private Rect2 _retanguloAoPegar;

	/// <summary>O menu esta morando numa janela do sistema (aberta ou escondida)?</summary>
	public bool Destacado => _janela != null;

	/// <summary>O menu esta ABERTO e DENTRO da tela do jogo -- ou seja, na frente dela? (Destacado, ele nao tapa nada.)</summary>
	public static bool NaFrenteDoJogo => Aberto && Instancia is { Destacado: false };

	/// <summary>O retangulo que o menu ocupa na tela do jogo, ou nulo quando ele nao esta nela (fechado, ou destacado).</summary>
	public static Rect2? RetanguloNaTela =>
		NaFrenteDoJogo && Instancia is { } m && IsInstanceValid(m._painel) ? m._painel.GetGlobalRect() : null;

	/// <summary>Este controle e do menu P embutido e aberto? (O modo de construir pergunta: clique no menu nao ergue bloco.)</summary>
	public static bool EDoMenuNaTela(Control c) =>
		NaFrenteDoJogo && Instancia is { } m && IsInstanceValid(m._painel) && (c == m._painel || m._painel.IsAncestorOf(c));

	// =====================================================================
	// MONTAGEM
	// =====================================================================
	/// <summary>
	/// As pecas de janela, por cima do menu ja montado: o puxador (dentro do rotulo do titulo, do tamanho dele),
	/// a moldura de redimensionar (irma do painel, por cima) e o botao "Destacar" no fim do cabecalho.
	/// </summary>
	private void MontarAJanela(HBoxContainer topo)
	{
		_rolagemDaPagina = (ScrollContainer)_pilha.GetParent();

		// OS DOIS TEXTOS QUE NAO ENCOLHIAM (o nome e a legenda dos marcos) passam a cortar: sem isto o menu nao
		// ficaria mais estreito que eles. Na largura de fabrica ha folga, e nada muda no desenho.
		_titulo.ClipText = true;
		_marcosLegenda.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;

		_puxador = new AlcaDeJanela
		{
			Name = "Puxador", Lados = AlcaDeJanela.Lado.Mover,
			AnchorRight = 1, AnchorBottom = 1,
			TooltipText = "arraste pra mudar o menu de lugar  ·  as bordas mudam o tamanho",
		};
		_puxador.AoPegar = PegarOMenu;
		_puxador.AoArrastar = ArrastarOMenu;
		_puxador.AoSoltar = Janelas.Gravar;
		_titulo.AddChild(_puxador);

		_btDestacar = new Button { Name = "Destacar", FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
		_btDestacar.AddThemeFontSizeOverride("font_size", 12);
		// MAIS BAIXO QUE A BUSCA: com a margem de fabrica do botao (10) ele passaria da altura dela e empurraria o
		// menu inteiro um pixel pra baixo.
		_btDestacar.AddThemeStyleboxOverride("normal", Tema.Caixa(Tema.PainelClaro, Tema.Borda, 6));
		_btDestacar.AddThemeStyleboxOverride("hover", Tema.Caixa(Tema.PainelAceso, Tema.BordaViva, 6));
		_btDestacar.AddThemeStyleboxOverride("pressed", Tema.Caixa(Tema.PainelAceso.Darkened(0.29f), Tema.BordaViva, 6));
		// ADIADO pro fim do quadro: o gesto tira da arvore o proprio botao que esta sendo apertado.
		_btDestacar.Pressed += () => Callable.From(AlternarDestaque).CallDeferred();
		topo.AddChild(_btDestacar);

		_moldura = new AlcaDeJanela
		{
			Name = "Moldura", Sobra = 3f,
			Lados = AlcaDeJanela.Lado.Esquerda | AlcaDeJanela.Lado.Direita | AlcaDeJanela.Lado.Cima | AlcaDeJanela.Lado.Baixo,
		};
		_moldura.AoPegar = PegarOMenu;
		_moldura.AoArrastar = ArrastarOMenu;
		_moldura.AoSoltar = Janelas.Gravar;
		_raiz.AddChild(_moldura);
		_painel.ItemRectChanged += MolduraSegueOPainel;

		// `Janelas.Mudou` E ESTATICO e o `SizeChanged` e da raiz, que nao morre com o mundo: metodos nomeados, e
		// `-=` no `SoltarAJanela`.
		Janelas.Mudou += AoMudarOArranjo;
		GetViewport().SizeChanged += AplicarOLugar;

		PintarOBotaoDeDestacar();
		AplicarOLugar();
	}

	/// <summary>O `_ExitTree` desta metade: solta as duas assinaturas de vida longa e grava o lugar da janela.</summary>
	private void SoltarAJanela()
	{
		Janelas.Mudou -= AoMudarOArranjo;
		if (GetViewport() is { } vp) vp.SizeChanged -= AplicarOLugar;
		if (_lugarPorGravar) { _lugarPorGravar = false; Janelas.Gravar(); }
	}

	// =====================================================================
	// EMBUTIDO: O LUGAR E O TAMANHO
	// =====================================================================
	/// <summary>O menor menu que cabe AGORA: o minimo da casa, ou o do proprio conteudo se ele pedir mais.</summary>
	private Vector2 MinimoDoMenu() => Janelas.MenuMinimo.Max(_painel.GetCombinedMinimumSize());

	/// <summary>
	/// POE O PAINEL ONDE O ARRANJO MANDA. De fabrica: as ancoras do centro, como o `Montar` sempre fez. Mexido:
	/// o retangulo guardado, preso nesta tela. Roda no nascimento, quando o arranjo muda e quando a tela muda.
	/// </summary>
	private void AplicarOLugar()
	{
		if (_janela != null || !IsInstanceValid(_painel)) return;   // destacado: quem manda no tamanho e a janela

		ArranjoDaInterface a = Janelas.Arranjo;
		if (!a.MenuMexido)
		{
			Vector2 meio = Janelas.MenuDeFabrica / 2f;
			_painel.SetAnchorsPreset(Control.LayoutPreset.Center);
			_painel.GrowHorizontal = Control.GrowDirection.Both;
			_painel.GrowVertical = Control.GrowDirection.Both;
			_painel.OffsetLeft = -meio.X; _painel.OffsetRight = meio.X;
			_painel.OffsetTop = -meio.Y; _painel.OffsetBottom = meio.Y;
			_rolagemDaPagina.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		}
		else
		{
			// A ROLAGEM ANTES DO MINIMO: e ela que tira a largura da pagina do minimo do painel.
			_rolagemDaPagina.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
			Vector2 tela = GetViewport().GetVisibleRect().Size;
			Rect2 r = Janelas.PrenderNaTela(Janelas.MenuNaTela(a, tela), tela, MinimoDoMenu());
			_painel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
			_painel.GrowHorizontal = Control.GrowDirection.End;
			_painel.GrowVertical = Control.GrowDirection.End;
			_painel.OffsetLeft = r.Position.X; _painel.OffsetRight = r.End.X;
			_painel.OffsetTop = r.Position.Y; _painel.OffsetBottom = r.End.Y;
		}
		MolduraSegueOPainel();
	}

	private void MolduraSegueOPainel()
	{
		if (IsInstanceValid(_moldura) && _janela == null) _moldura.Cobrir(_painel.GetRect());
	}

	private void PegarOMenu(AlcaDeJanela.Lado _) => _retanguloAoPegar = _painel.GetRect();

	private void ArrastarOMenu(AlcaDeJanela.Lado lados, Vector2 andou)
	{
		if (_janela != null) return;
		// A ROLAGEM HORIZONTAL JA NO PRIMEIRO PIXEL: senao o minimo desta conta ainda seria o da pagina.
		_rolagemDaPagina.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
		Vector2 tela = GetViewport().GetVisibleRect().Size;
		Rect2 r = Janelas.Arrastado(_retanguloAoPegar, lados, andou, tela, MinimoDoMenu());
		Janelas.GuardarMenu(Janelas.Arranjo, r, tela);
		AplicarOLugar();
	}

	/// <summary>O arranjo mudou por fora (as opcoes, o "Restaurar"): o lugar e o modo se acertam.</summary>
	private void AoMudarOArranjo()
	{
		bool quer = Janelas.Arranjo.MenuDestacado && Janelas.PodeDestacar;
		if (!quer && _janela != null) Encaixar();
		else if (quer && _janela == null && Visible) { CriarAJanela(); MostrarAJanela(); }
		AplicarOLugar();
		PintarOBotaoDeDestacar();
	}

	private void PintarOBotaoDeDestacar()
	{
		_btDestacar.Visible = Janelas.PodeDestacar;
		_btDestacar.Text = _janela != null ? "Encaixar" : "Destacar";
		_btDestacar.TooltipText = _janela != null
			? "devolve o menu pra dentro da tela do jogo"
			: "abre o menu numa janela separada -- pra por noutro monitor, por exemplo";
	}

	// =====================================================================
	// DESTACADO
	// =====================================================================
	/// <summary>O gesto do botao do cabecalho: destaca o embutido, encaixa o destacado. A escolha fica gravada.</summary>
	private void AlternarDestaque()
	{
		bool destacar = _janela == null;
		if (destacar && !Janelas.PodeDestacar) return;
		Janelas.Arranjo.MenuDestacado = destacar;
		if (destacar) { CriarAJanela(); if (Visible) MostrarAJanela(); }
		else
		{
			LembrarAJanela();   // o lugar de onde ela sai fica guardado: o proximo "Destacar" a devolve pra la
			Encaixar();
		}
		Janelas.Gravar();
	}

	/// <summary>Quanto a tela do jogo esta ampliando o desenho agora (1 = a base cabe exata na janela).</summary>
	private float EscalaDoJogo()
	{
		float s = GetViewport().GetStretchTransform().Scale.X;
		return float.IsFinite(s) && s > 0f ? s : 1f;
	}

	/// <summary>
	/// Onde o painel EMBUTIDO esta no desktop, em px de monitor: e em cima dele que a janela nasce da primeira
	/// vez -- o menu "sai" de onde estava, em vez de aparecer num canto qualquer.
	/// </summary>
	private Rect2I PainelNoDesktop()
	{
		Transform2D t = GetViewport().GetScreenTransform();
		Rect2 r = _painel.GetGlobalRect();
		Vector2 canto = t * r.Position, tamanho = r.Size * t.Scale;
		return new Rect2I(DisplayServer.WindowGetPosition() + (Vector2I)canto.Round(), (Vector2I)tamanho.Round());
	}

	/// <summary>
	/// CRIA A JANELA DO SISTEMA e muda o conteudo do menu pra dentro dela. Ela nasce ESCONDIDA: quem mostra e o
	/// <see cref="MostrarAJanela"/>, so com o menu aberto.
	/// </summary>
	private void CriarAJanela()
	{
		if (_janela != null || !Janelas.PodeDestacar) return;

		// A FICHA E O TECLADO DO RADIO moram na `_raiz` e iriam junto -- mas nasceram pra tela do jogo. Fecham.
		FecharFicha();
		FecharTecladoDoRadio();

		ArranjoDaInterface a = Janelas.Arranjo;
		List<Janelas.Monitor> monitores = Janelas.Monitores();
		Vector2I moldura = Janelas.MolduraDeJanela();
		Rect2I sobre = PainelNoDesktop();

		// A ESCALA: a do jogo, ate o que o monitor de destino aguenta. O destino sai da MESMA conta do lugar,
		// feita antes sem minimo -- so pra saber em que monitor a janela vai cair.
		int principal = DisplayServer.GetPrimaryScreen();
		(int destino, _) = Janelas.LugarDaJanela(a, monitores, principal, sobre, moldura, Vector2I.One);
		Rect2I util = destino >= 0 && destino < monitores.Count ? monitores[destino].Util : new Rect2I(Vector2I.Zero, DisplayServer.ScreenGetSize());
		float escala = EscalaPraJanela(util, moldura);
		var minimo = (Vector2I)(Janelas.JanelaMinima * escala).Round();
		(_, Rect2I lugar) = Janelas.LugarDaJanela(a, monitores, principal, sobre, moldura, minimo);

		_janela = new Window
		{
			Name = "JanelaDoMenu",
			Title = "Menu — Dragon Ball Jandirus",
			Visible = false,
			// NATIVA MESMO COM A RAIZ EMBUTINDO: so esta janela sai pro sistema; as dicas e os seletores do jogo
			// continuam nascendo dentro da tela dele.
			ForceNative = true,
			// FILHA DA JANELA DO JOGO pro sistema: fica por cima dela, some da barra de tarefas e minimiza junto.
			Transient = true,
			// E AS DICAS E OS SELETORES DELA nascem dentro DELA (senao subiriam ate a raiz e apareceriam no jogo).
			GuiEmbedSubwindows = true,
			InitialPosition = Window.WindowInitialPosition.Absolute,
			Position = lugar.Position,
			Size = lugar.Size,
			MinSize = minimo,
			ContentScaleFactor = escala,
			// SEM BOTAO DE MINIMIZAR: janela filha nao tem lugar na barra de tarefas, e minimizada vira uma tirinha
			// perdida no canto do desktop. Quem quer tira-la da frente fecha (o X, ou P) -- e P a traz de volta.
			MinimizeDisabled = true,
			// A JANELA DE BANCADA NAO PEGA O TECLADO DO DONO -- a mesma bandeira da janela do jogo.
			Unfocusable = Janelas.SemFoco,
		};
		AddChild(_janela);

		_janela.AddChild(new ColorRect
		{
			Name = "Fundo", Color = Tema.Fundo, AnchorRight = 1, AnchorBottom = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});
		_janela.AddChild(new PorteiroDaJanela { Name = "Porteiro" });

		RemoveChild(_raiz);
		_janela.AddChild(_raiz);

		// O PAINEL ENCHE A JANELA, e troca o vidro (canto redondo, 88% de opacidade: feito pra flutuar sobre o
		// mundo) por uma chapa reta e cheia -- a moldura agora e a do Windows.
		_vidro ??= _painel.GetThemeStylebox("panel");
		var chapa = Tema.Caixa(new Color(0.06f, 0.07f, 0.10f), new Color(0.06f, 0.07f, 0.10f), 12);
		chapa.SetCornerRadiusAll(0);
		_painel.AddThemeStyleboxOverride("panel", chapa);
		_painel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_painel.GrowHorizontal = Control.GrowDirection.End;
		_painel.GrowVertical = Control.GrowDirection.End;
		_painel.OffsetLeft = _painel.OffsetTop = _painel.OffsetRight = _painel.OffsetBottom = 0;
		_rolagemDaPagina.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
		_moldura.Visible = false;
		_puxador.Visible = false;

		_janela.CloseRequested += AoFecharNoX;
		_janela.FocusExited += AoPerderOTeclado;
		_janela.SizeChanged += JanelaMexeu;

		// TUDO O QUE MORAVA NAS PAGINAS SAIU E ENTROU DE NOVO NA ARVORE (mudou de pai), e ha pecas ali que so se
		// ligam ao jogo no `_Ready` -- que nao roda duas vezes. As paginas sao remontadas: a da vez agora, as
		// outras quando forem visitadas (sem assinatura guardada, nenhuma e reaproveitada).
		_assinaturas.Clear();
		if (Visible) Redesenhar();
		PintarOBotaoDeDestacar();
	}

	/// <summary>
	/// O MENU ABRIU. Se o arranjo diz "destacado" (e ha como abrir janela), e a janela do sistema que aparece --
	/// criada aqui na primeira vez desta sessao, no lugar lembrado.
	/// </summary>
	private void AbrirAJanela()
	{
		if (!Janelas.Arranjo.MenuDestacado || !Janelas.PodeDestacar) return;
		CriarAJanela();
		MostrarAJanela();
	}

	/// <summary>O MENU FECHOU: a janela do sistema some (no fim do quadro -- ver <see cref="EsconderAJanela"/>).</summary>
	private void FecharAJanela()
	{
		if (_janela != null) Callable.From(EsconderAJanela).CallDeferred();
	}

	/// <summary>
	/// A ESCALA DO CONTEUDO DA JANELA: a da tela do jogo AGORA (em tela cheia de 1920 com base de 1280 o menu sai
	/// 1,5x, como o embutido), sem passar do que deixa o menor menu caber na area util do monitor de destino.
	/// </summary>
	private float EscalaPraJanela(Rect2I util, Vector2I moldura)
	{
		float escala = Math.Min(EscalaDoJogo(), Math.Min((util.Size.X - moldura.X) / Janelas.JanelaMinima.X,
														 (util.Size.Y - moldura.Y) / Janelas.JanelaMinima.Y));
		return Math.Abs(escala - 1f) < 0.02f ? 1f : Math.Max(escala, 0.5f);
	}

	/// <summary>
	/// Mostra a janela (o menu abriu). Sem efeito se ela ja esta na tela. A ESCALA E CONFERIDA AQUI: o jogador
	/// pode ter trocado a resolucao do jogo com o menu fechado, e a janela reabre na escala de agora.
	/// </summary>
	private void MostrarAJanela()
	{
		if (_janela is not { Visible: false } j) return;

		List<Janelas.Monitor> monitores = Janelas.Monitores();
		ArranjoDaInterface a = Janelas.Arranjo;
		int tela = a.JanelaLembrada && a.JanelaTela >= 0 && a.JanelaTela < monitores.Count ? a.JanelaTela : DisplayServer.WindowGetCurrentScreen();
		if (tela >= 0 && tela < monitores.Count)
		{
			float escala = EscalaPraJanela(monitores[tela].Util, Janelas.MolduraDeJanela());
			if (Math.Abs(j.ContentScaleFactor - escala) > 0.02f)
			{
				j.ContentScaleFactor = escala;
				j.MinSize = (Vector2I)(Janelas.JanelaMinima * escala).Round();
			}
		}
		j.Show();
	}

	/// <summary>
	/// Esconde a janela (o menu fechou). ADIADO de proposito por quem chama: o P que fecha o menu pode ter caido
	/// NESTA janela, e ela nao pode ser desmontada no meio da propria tecla. Confere o estado na hora: se o menu
	/// reabriu no mesmo quadro, nao esconde.
	/// </summary>
	private void EsconderAJanela()
	{
		if (!IsInstanceValid(this) || _janela is not { } j || !IsInstanceValid(j) || !j.Visible || Visible) return;
		LembrarAJanela();
		j.Hide();
		if (_lugarPorGravar) { _lugarPorGravar = false; Janelas.Gravar(); }
	}

	/// <summary>
	/// DEVOLVE O MENU PRA TELA DO JOGO: o conteudo volta pra camada, a janela do sistema morre, o painel veste o
	/// vidro de novo e vai pro lugar embutido (o de fabrica, ou o que o jogador tinha deixado).
	///
	/// NAO GUARDA O LUGAR DA JANELA, de proposito: quem chama por causa de um GESTO (o botao "Encaixar") guarda
	/// antes; quem chama porque o arranjo mudou por fora nao pode -- o "Restaurar a interface padrao" troca o
	/// arranjo por um de fabrica e so entao manda encaixar, e guardar aqui escreveria o lugar velho no arranjo
	/// NOVO (a bancada pegou: depois do "Restaurar" o arquivo ainda tinha uma janela lembrada).
	/// </summary>
	private void Encaixar()
	{
		if (_janela is not { } j) return;

		FecharFicha();
		FecharTecladoDoRadio();
		j.CloseRequested -= AoFecharNoX;
		j.FocusExited -= AoPerderOTeclado;
		j.SizeChanged -= JanelaMexeu;

		j.RemoveChild(_raiz);
		AddChild(_raiz);
		_janela = null;
		j.QueueFree();

		if (_vidro != null) _painel.AddThemeStyleboxOverride("panel", _vidro);
		_moldura.Visible = true;
		_puxador.Visible = true;
		AplicarOLugar();

		_assinaturas.Clear();   // mudou de pai de novo: ver `CriarAJanela`
		if (Visible) Redesenhar();
		PintarOBotaoDeDestacar();
		_lugarPorGravar = false;   // quem mandou encaixar grava (o botao, as opcoes, o "Restaurar")
	}

	/// <summary>
	/// GUARDA NO ARRANJO onde a janela esta: o monitor, o canto (a partir do canto do monitor) e o tamanho. So
	/// com a janela NA TELA -- escondida, o sistema ja a desmontou e nao ha o que perguntar. Nao grava o
	/// arquivo: quem grava e quem fecha, encaixa ou perde o foco.
	/// </summary>
	private void LembrarAJanela()
	{
		if (_janela is not { Visible: true } j || !j.IsInsideTree()) return;
		int tela = j.CurrentScreen;
		if (tela < 0 || tela >= DisplayServer.GetScreenCount()) return;

		ArranjoDaInterface a = Janelas.Arranjo;
		Vector2I canto = j.Position - DisplayServer.ScreenGetPosition(tela), tamanho = j.Size;
		if (a.JanelaLembrada && a.JanelaTela == tela && a.JanelaX == canto.X && a.JanelaY == canto.Y
			&& a.JanelaLargura == tamanho.X && a.JanelaAltura == tamanho.Y) return;

		a.JanelaLembrada = true;
		a.JanelaTela = tela;
		a.JanelaX = canto.X; a.JanelaY = canto.Y;
		a.JanelaLargura = tamanho.X; a.JanelaAltura = tamanho.Y;
		_lugarPorGravar = true;
	}

	/// <summary>
	/// A janela foi movida (o porteiro avisa) ou redimensionada (o sinal dela) pelo jogador: o lugar novo fica
	/// lembrado.
	/// </summary>
	internal void JanelaMexeu() => LembrarAJanela();

	private void AoFecharNoX()
	{
		if (FecharNoXEncaixa) Callable.From(AlternarDestaque).CallDeferred();
		else Fechar();
	}

	/// <summary>
	/// A JANELA DO MENU PERDEU O TECLADO (o jogador clicou no jogo, ou noutro programa): o campo de texto que
	/// estivesse com o foco o solta -- senao a busca esquecida aberta contaria como "escrevendo" e o personagem
	/// nao andaria. E e uma boa hora de por o lugar da janela no arquivo.
	/// </summary>
	private void AoPerderOTeclado()
	{
		if (_janela is not { } j) return;
		if (!FocoPresoNaJanelaDeTeste) j.GuiReleaseFocus();
		// O LUGAR E CONFERIDO AQUI TAMBEM, e nao so no aviso de "a janela andou": quem arrasta a janela e clica no
		// jogo tem de sair com o lugar novo gravado, chegue aquele aviso ou nao.
		LembrarAJanela();
		if (_lugarPorGravar) { _lugarPorGravar = false; Janelas.Gravar(); }
	}

	/// <summary>
	/// QUEM TEM O FOCO DE TEXTO DESTE MENU. Embutido: o da tela do jogo. Destacado: o da janela dele -- e so se
	/// ELA tem o teclado. Ver <see cref="Digitando"/>.
	/// </summary>
	private Control? DonoDoFoco()
	{
		if (_janela is not { } j) return GetViewport()?.GuiGetFocusOwner();
		bool comOTeclado = JanelaComOTecladoDeTeste ?? j.HasFocus();
		return comOTeclado || FocoPresoNaJanelaDeTeste ? j.GuiGetFocusOwner() : null;
	}

	/// <summary>
	/// O ESC CAIU NA JANELA DESTACADA: e do menu que mora nela, e desfaz uma camada -- a ficha, a busca, a arvore
	/// e por fim o proprio menu. ANTES da interface da janela (o porteiro o entrega no `_Input`), como o ESC do
	/// menu embutido: senao o campo da busca ficaria com o primeiro aperto.
	/// </summary>
	internal void EscNaJanela(Viewport janela)
	{
		DesfazerUmaCamada();
		janela.SetInputAsHandled();
	}

	/// <summary>
	/// UMA TECLA CAIU NA JANELA DESTACADA. Ela e OFERECIDA AO JOGO antes de a interface da janela a ver --
	/// empurrada na tela dele como se tivesse caido la: P fecha o menu, TAB abre a lista de teclas, E interage, I
	/// abre a mochila, o atalho de tecnica dispara. (O que se SEGURA nao precisa disto: o jogo le o estado das
	/// teclas, que e um so pras duas janelas.)
	///
	///   * ESCREVENDO num campo da janela (a busca), a tecla e do campo e NAO e oferecida: "p" e a letra p;
	///   * se o jogo a USOU, ela morre aqui; se nao usou, segue pra interface da janela -- e a mesma ordem do
	///     menu embutido, onde as telas do jogo leem a tecla antes dos botoes do menu;
	///   * se ela abriu um campo de texto no jogo (o Enter do chat), o foco do sistema vai junto pra janela do
	///     jogo -- as proximas letras tem de cair la. Menos na bancada: ela nunca pede o teclado.
	/// </summary>
	internal void TeclaNaJanela(InputEventKey k, Viewport janela)
	{
		if (SemRepasseDeTeclasDeTeste) return;
		if (janela.GuiGetFocusOwner() is LineEdit or TextEdit) return;

		Viewport jogo = GetViewport();
		bool escrevia = Foco.Digitando;
		jogo.PushInput(k);
		if (jogo.IsInputHandled()) janela.SetInputAsHandled();
		if (!escrevia && Foco.Digitando && !Janelas.SemFoco) GetWindow().GrabFocus();
	}

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--diagjanelas`)
	// =====================================================================
	/// <summary>O retangulo do painel do menu, em px da tela onde ele mora (a do jogo, ou a da janela destacada).</summary>
	public Rect2 RetanguloDeTeste => _painel.GetGlobalRect();

	/// <summary>A barra que arrasta e a moldura que redimensiona.</summary>
	public AlcaDeJanela PuxadorDeTeste => _puxador;
	public AlcaDeJanela MolduraDeTeste => _moldura;

	/// <summary>O botao "Destacar" / "Encaixar" do cabecalho.</summary>
	public Button BotaoDeDestacarDeTeste => _btDestacar;

	/// <summary>A janela do sistema, ou nula com o menu embutido.</summary>
	public Window? JanelaDeTeste => _janela;

	/// <summary>A rolagem da pagina (a que ganha a barra horizontal quando o menu fica estreito) e a busca.</summary>
	public ScrollContainer RolagemDeTeste => _rolagemDaPagina;
	public LineEdit BuscaDeTeste => _busca;
}
