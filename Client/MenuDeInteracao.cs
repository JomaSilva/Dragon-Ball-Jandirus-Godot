using Godot;
using Jandirus.Core.Tech;

namespace Jandirus.Client;

/// <summary>
/// A TECLA E: chegue perto de alguma coisa e veja o que da pra fazer com ela.
///
/// ============================ O GESTO QUE NAO EXISTIA ============================
/// Ate aqui, cada maquina do mundo era usada por um caminho proprio e invisivel: o banco tinha
/// tres verbos escondidos na busca do menu, a bancada de pesquisa tinha um verbo "estudar" que so
/// funcionava se voce ja soubesse que existia, e a arvore era pintura. Nada na tela dizia que
/// aquilo respondia a alguma coisa.
///
/// O pedido do dono foi uma porta so: "chegar perto do objeto e apertar E, assim vai abrir uma
/// tela q seria um menu te interaçoes possiveis daquele objeto, isso serve pra tudo".
/// =================================================================================
///
/// ============================ O QUE ELE NAO FAZ ============================
/// NAO DECIDE NADA. As acoes vem do <see cref="Interacoes"/>, que o servidor le tambem, e cada
/// botao manda o MESMO verbo que ja existia. Este arquivo e uma porta, nao um sistema: se ele
/// sumisse, os comandos continuariam funcionando pela busca do menu.
///
/// E NAO ALCANCA PORTA. Elas abrem por encostar, e trocar isso por um menu de duas opcoes seria
/// piorar um gesto que ja esta bom.
/// ===========================================================================
/// </summary>
public partial class MenuDeInteracao : CanvasLayer
{
	/// <summary>
	/// A QUE DISTANCIA A COISA RESPONDE, em pixels.
	///
	/// E o MESMO `AlcanceDeUso` do servidor (64 px, dois tiles). Tem que ser: um menu que abre a
	/// tres tiles mandaria um comando que o servidor recusa por distancia, e o jogador leria
	/// "longe demais" olhando pra um botao que o jogo acabou de oferecer.
	/// </summary>
	public const float Alcance = 64f;

	private Control _raiz = null!;
	private VBoxContainer _lista = null!;
	private Label _titulo = null!;
	private Label _dica = null!;

	/// <summary>Em qual obra o menu esta aberto. Zero = fechado.</summary>
	private int _alvo;

	public override void _Ready()
	{
		Layer = 4;   // acima do menu do jogo (3), abaixo do quick time event (5)
		Montar();
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

		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
		caixa.AddThemeConstantOverride("separation", 6);
		painel.AddChild(caixa);

		_titulo = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_titulo.AddThemeFontSizeOverride("font_size", 20);
		caixa.AddChild(_titulo);
		caixa.AddChild(new HSeparator());

		_lista = new VBoxContainer();
		_lista.AddThemeConstantOverride("separation", 4);
		caixa.AddChild(_lista);

		var fechar = new Button { Text = "Fechar (Esc)" };
		fechar.Pressed += Fechar;
		caixa.AddChild(fechar);

		// A DICA FLUTUANTE, no pe da tela: ela e o que ENSINA a tecla. Sem ela, um menu que so abre
		// com E e tao escondido quanto os verbos que ele veio substituir.
		_dica = new Label
		{
			AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1, AnchorBottom = 1,
			OffsetLeft = -160, OffsetRight = 160, OffsetTop = -104, OffsetBottom = -80,
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
		};
		_dica.AddThemeColorOverride("font_color", Tema.Destaque);
		AddChild(_dica);
	}

	public override void _Process(double delta)
	{
		// A DICA SEGUE O QUE ESTA POR PERTO, e some quando o menu abre (o menu ja e a resposta).
		if (_raiz.Visible) { _dica.Visible = false; return; }

		GameClient.ObraInfo? perto = MaisPerto();
		_dica.Visible = perto != null;
		if (perto is { } o) _dica.Text = $"[E] {NomeDe(o)}";
	}

	public override void _UnhandledInput(InputEvent evento)
	{
		if (Foco.Digitando) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k) return;

		if (_raiz.Visible && k.Keycode == Key.Escape)
		{
			Fechar();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (k.PhysicalKeycode != Key.E && k.Keycode != Key.E) return;

		if (_raiz.Visible) Fechar();
		else Abrir();
		GetViewport().SetInputAsHandled();
	}

	/// <summary>
	/// A COISA INTERATIVA MAIS PROXIMA, ou nula.
	///
	/// O CORTE E POR EIXO, e nao por raio, porque e assim que o servidor mede (`ObraPerto` compara
	/// `Math.Abs` em X e Y separados). Usar distancia euclidiana aqui abriria o menu em pontos que
	/// o servidor consideraria longe -- de novo, o botao existiria e o comando falharia.
	/// </summary>
	private static GameClient.ObraInfo? MaisPerto()
	{
		if (GameClient.Instance is not { } cli) return null;
		if (World.Instancia?.PosicaoDesenhadaDe(cli.LocalId) is not { } eu) return null;

		GameClient.ObraInfo? melhor = null;
		float melhorDist = float.MaxValue;

		foreach (GameClient.ObraInfo o in cli.Obras)
		{
			if (!Interacoes.Interativo(o.Tipo)) continue;
			if (Math.Abs(o.Pos.X - eu.X) > Alcance || Math.Abs(o.Pos.Y - eu.Y) > Alcance) continue;

			float d = eu.DistanceSquaredTo(o.Pos);
			if (d >= melhorDist) continue;
			melhorDist = d;
			melhor = o;
		}
		return melhor;
	}

	private static string NomeDe(GameClient.ObraInfo o) =>
		GameClient.Instance?.Catalogo.FirstOrDefault(c => c.Id == o.Tipo).Nome is { Length: > 0 } n
			? n : o.Tipo.Replace('_', ' ');

	/// <summary>Que pagina do menu esta aberta. Vazio = a raiz do objeto.</summary>
	private string _pagina = "";

	private void Abrir()
	{
		if (MaisPerto() is not { } o) return;
		_alvo = o.Id;
		_pagina = "";
		Desenhar(o.Tipo);
		_raiz.Visible = true;
	}

	/// <summary>
	/// Desenha a pagina atual do objeto. A raiz e `tipo`; um submenu e `tipo/chave`.
	/// </summary>
	private void Desenhar(string tipo)
	{
		string chave = _pagina.Length == 0 ? tipo : $"{tipo}/{_pagina}";
		_titulo.Text = _pagina.Length == 0 ? NomeDoAlvo() : NomeDoAlvo() + " — melhorias";

		foreach (Node n in _lista.GetChildren()) n.QueueFree();

		// VOLTAR SO EXISTE DENTRO DE UM SUBMENU. Na raiz ele seria o mesmo que Fechar, e dois
		// botoes pra sair da mesma tela e uma escolha que o jogador nao devia ter que fazer.
		if (_pagina.Length > 0)
		{
			var voltar = new Button { Text = "< Voltar" };
			voltar.Pressed += () => { _pagina = ""; Desenhar(tipo); };
			_lista.AddChild(voltar);
		}

		foreach (Interacoes.Acao a in Interacoes.De(chave))
		{
			Interacoes.Acao acao = a;
			var b = new Button { Text = a.Rotulo, TooltipText = a.Dica };
			b.Pressed += () => Escolher(tipo, acao);
			_lista.AddChild(b);
		}
	}

	private void Escolher(string tipo, Interacoes.Acao acao)
	{
		switch (acao.Forma)
		{
			case Interacoes.Forma.Submenu:
				_pagina = acao.Arg;
				Desenhar(tipo);
				break;

			case Interacoes.Forma.Numero:
				AbrirTeclado(acao);
				break;

			default:
				GameClient.Instance?.SendVerbo(acao.Verbo, acao.Arg);
				Fechar();
				break;
		}
	}

	private string NomeDoAlvo()
	{
		if (GameClient.Instance is not { } cli) return "";
		foreach (GameClient.ObraInfo o in cli.Obras)
			if (o.Id == _alvo) return NomeDe(o);
		return "";
	}

	// =====================================================================
	// O TECLADO NUMERICO
	// =====================================================================
	/// <summary>O teclado aberto agora, se houver. Ver <see cref="TecladoNumerico"/>.</summary>
	private CenterContainer? _teclado;

	private void AbrirTeclado(Interacoes.Acao acao)
	{
		FecharTeclado();

		var t = new TecladoNumerico(acao.Rotulo, acao.Min, acao.Max,
			v => { GameClient.Instance?.SendVerbo(acao.Verbo, $"{v:0}"); Fechar(); },
			FecharTeclado);

		_teclado = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_teclado.AddChild(t);
		_raiz.AddChild(_teclado);
	}

	private void FecharTeclado()
	{
		_teclado?.QueueFree();
		_teclado = null;
	}

	private void Fechar()
	{
		FecharTeclado();
		_raiz.Visible = false;
		_alvo = 0;
		_pagina = "";
	}
}
