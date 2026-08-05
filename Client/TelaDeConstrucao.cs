using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A GRADE DA BANCADA DE PESQUISA -- e o fantasma de assentar o que saiu dela.
///
/// ============================ DUAS COISAS NUM ARQUIVO, DE PROPOSITO ============================
/// Comprar e assentar sao dois gestos do mesmo ciclo, e o segundo so existe por causa do primeiro:
/// a bancada nao ergue mais nada no chao, ela FABRICA pra mochila, e o jogador escolhe o lugar
/// depois. Separar em dois arquivos deixaria a metade de cima sem contexto pra metade de baixo.
/// ================================================================================================
/// </summary>
public partial class TelaDeConstrucao : CanvasLayer
{
	private Control _raiz = null!;
	private GridContainer _grade = null!;
	private Label _cabecalho = null!;
	private PanelContainer? _confirma;

	/// <summary>Quantas colunas a grade tem. Cinco cabem sem apertar o nome embaixo do icone.</summary>
	private const int Colunas = 5;

	public static TelaDeConstrucao? Instancia { get; private set; }

	public override void _Ready()
	{
		Instancia = this;
		Layer = 4;
		Montar();

		if (GameClient.Instance is { } cli)
			cli.TechMudou += () => { if (_raiz.Visible) Redesenhar(); };
	}

	public override void _ExitTree() { if (Instancia == this) Instancia = null; }

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

		var titulo = new Label { Text = "BANCADA DE PESQUISA", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 22);
		caixa.AddChild(titulo);

		_cabecalho = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_cabecalho.AddThemeColorOverride("font_color", Tema.TextoFraco);
		_cabecalho.AddThemeFontSizeOverride("font_size", 13);
		caixa.AddChild(_cabecalho);
		caixa.AddChild(new HSeparator());

		_grade = new GridContainer { Columns = Colunas };
		_grade.AddThemeConstantOverride("h_separation", 8);
		_grade.AddThemeConstantOverride("v_separation", 8);
		caixa.AddChild(Rolagem(_grade, 380));

		var fechar = new Button { Text = "Fechar (Esc)" };
		fechar.Pressed += Fechar;
		caixa.AddChild(fechar);
	}

	private static ScrollContainer Rolagem(Control dentro, int altura)
	{
		var sc = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(560, altura),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		dentro.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		sc.AddChild(dentro);
		return sc;
	}

	public void Abrir()
	{
		// PEDE A LISTA AO ABRIR. O catalogo do cliente pode estar velho -- o zeni muda ao lutar e o
		// nivel de tecnologia ao estudar, e as duas coisas mudam o que da pra comprar.
		GameClient.Instance?.SendVerbo("tech_lista", "");
		_raiz.Visible = true;
		Redesenhar();
	}

	private void Fechar()
	{
		FecharConfirmacao();
		_raiz.Visible = false;
	}

	public override void _UnhandledInput(InputEvent evento)
	{
		if (!_raiz.Visible || Foco.Digitando) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k || k.Keycode != Key.Escape) return;
		Fechar();
		GetViewport().SetInputAsHandled();
	}

	private void Redesenhar()
	{
		FecharConfirmacao();
		foreach (Node n in _grade.GetChildren()) n.QueueFree();
		if (GameClient.Instance is not { } cli) return;

		_cabecalho.Text = $"tecnologia {cli.TechNivel:0}   ·   {cli.Zeni:N0} zeni";

		foreach (GameClient.OfertaDeObra o in cli.Catalogo)
		{
			GameClient.OfertaDeObra oferta = o;
			_grade.AddChild(Cartao(oferta));
		}
	}

	/// <summary>
	/// UM ITEM DA GRADE: o icone em cima, o nome embaixo, e o preco.
	///
	/// O QUE NAO DA PRA COMPRAR CONTINUA NA GRADE, apagado e com o motivo no tooltip. Esconder
	/// seria mais limpo e diria menos: o jogador precisa VER que existe uma máquina de gravidade
	/// esperando cinquenta pontos de tecnologia -- e o que faz estudar ter destino.
	/// </summary>
	private Control Cartao(GameClient.OfertaDeObra o)
	{
		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(100, 0) };
		caixa.AddThemeConstantOverride("separation", 2);

		bool pode = o.Recusa == (int)RecusaObra.Pode;
		string motivo = o.Recusa switch
		{
			(int)RecusaObra.SemTech => $"pede {o.Tech:0} de tecnologia",
			(int)RecusaObra.SemZeni => $"custa {o.Custo:N0} zeni",
			(int)RecusaObra.RacaErrada => "não é coisa da sua raça",
			_ => "",
		};

		var b = new Button
		{
			CustomMinimumSize = new Vector2(100, 76),
			TooltipText = pode ? $"{o.Nome}\n{o.Custo:N0} zeni" : $"{o.Nome}\n{motivo}",
			Disabled = !pode,
			ExpandIcon = true,
			IconAlignment = HorizontalAlignment.Center,
		};
		if (Miniatura(o.Arte, o.Estado) is { } icone) b.Icon = icone;
		else b.Text = o.Nome[..Math.Min(6, o.Nome.Length)];

		b.Pressed += () => Confirmar(o);
		caixa.AddChild(b);

		var nome = new Label
		{
			Text = o.Nome,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.Word,
			CustomMinimumSize = new Vector2(100, 0),
		};
		nome.AddThemeFontSizeOverride("font_size", 11);
		nome.AddThemeColorOverride("font_color", pode ? Tema.Texto : Tema.TextoFraco);
		caixa.AddChild(nome);

		var preco = new Label
		{
			Text = $"{o.Custo:N0}z",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		preco.AddThemeFontSizeOverride("font_size", 11);
		preco.AddThemeColorOverride("font_color", pode ? Tema.Destaque : Tema.TextoFraco);
		caixa.AddChild(preco);

		return caixa;
	}

	/// <summary>A CAIXA DE "TEM CERTEZA?", com o preco escrito por extenso.</summary>
	private void Confirmar(GameClient.OfertaDeObra o)
	{
		FecharConfirmacao();

		_confirma = Tema.Painel1(12);
		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
		caixa.AddThemeConstantOverride("separation", 8);
		_confirma.AddChild(caixa);

		var t = new Label { Text = o.Nome, HorizontalAlignment = HorizontalAlignment.Center };
		t.AddThemeFontSizeOverride("font_size", 18);
		caixa.AddChild(t);

		var p = new Label
		{
			Text = $"Fabricar por {o.Custo:N0} zeni?",
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.Word,
		};
		caixa.AddChild(p);

		var resta = new Label
		{
			Text = $"você tem {GameClient.Instance?.Zeni ?? 0:N0}",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		resta.AddThemeColorOverride("font_color", Tema.TextoFraco);
		resta.AddThemeFontSizeOverride("font_size", 12);
		caixa.AddChild(resta);

		var linha = new HBoxContainer();
		var nao = new Button { Text = "Cancelar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		nao.Pressed += FecharConfirmacao;
		linha.AddChild(nao);

		var sim = new Button { Text = "Fabricar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		sim.Pressed += () =>
		{
			GameClient.Instance?.SendVerbo("tech_construir", o.Id);
			FecharConfirmacao();
		};
		linha.AddChild(sim);
		caixa.AddChild(linha);

		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		centro.AddChild(_confirma);
		_raiz.AddChild(centro);
	}

	private void FecharConfirmacao()
	{
		if (_confirma?.GetParent() is { } pai) pai.QueueFree();
		_confirma = null;
	}

	internal static Texture2D? Miniatura(string arte, string estado)
	{
		if (arte.Length == 0 || !ResourceLoader.Exists(arte)) return null;
		if (ResourceLoader.Load<SpriteFrames>(arte) is not { } f) return null;

		string anim = estado.Length > 0 ? Sanear(estado) : "default";
		if (!f.HasAnimation(anim) || f.GetFrameCount(anim) == 0)
		{
			foreach (StringName a in f.GetAnimationNames())
				if (f.GetFrameCount(a) > 0) { anim = a; break; }
		}
		return f.HasAnimation(anim) && f.GetFrameCount(anim) > 0 ? f.GetFrameTexture(anim, 0) : null;
	}

	/// <summary>O mesmo saneamento de nome que o conversor aplicou aos estados.</summary>
	private static string Sanear(string s)
	{
		var sb = new System.Text.StringBuilder(s.Length);
		foreach (char c in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
		string r = sb.ToString().Trim('_');
		while (r.Contains("__")) r = r.Replace("__", "_");
		return r.Length == 0 ? "state" : r;
	}

	// =====================================================================
	// O FANTASMA
	// =====================================================================
	/// <summary>
	/// A CONSTRUCAO QUE ESTA NA MAO, esperando um lugar.
	///
	/// ============================ POR QUE ELA SEGUE O MOUSE ============================
	/// Antes o jogo construia embaixo dos proprios pes: nao havia o que escolher, e nao havia o que
	/// mostrar. Agora o jogador aponta -- e apontar sem ver o que vai sair dali e adivinhar. O
	/// fantasma e a resposta a "cabe aqui?" ANTES do clique, que e quando a pergunta importa.
	///
	/// ELE E SO DESENHO. Quem decide se o ponto vale e o servidor (alcance, parede, coisa demais
	/// no mesmo lugar) -- o fantasma vermelho e um aviso, nao um veto.
	/// ===================================================================================
	/// </summary>
	private Sprite2D? _fantasma;
	private string _naMao = "";

	public void Segurar(string id)
	{
		Largar();
		if (Jandirus.Core.Items.CatalogoDeItens.Get(id) is not { } def) return;

		Texture2D? t = Miniatura(def.Arte, def.Estado);
		if (t == null) return;

		_naMao = id;
		_fantasma = new Sprite2D
		{
			Texture = t,
			Centered = false,
			// MEIO TRANSPARENTE, como o dono pediu: da pra ver o chao por baixo e julgar o encaixe.
			Modulate = new Color(1, 1, 1, 0.55f),
			ZIndex = 50,
		};
		World.Instancia?.AddChild(_fantasma);
		Chat.Sistema("clique onde quer assentar. Botão direito ou Esc cancela.");
	}

	public void Largar()
	{
		_fantasma?.QueueFree();
		_fantasma = null;
		_naMao = "";
	}

	public override void _Process(double delta)
	{
		if (_fantasma == null || World.Instancia is not { } mundo) return;

		// A ANCORA E A CELULA, e nao o pixel do mouse: a construcao ocupa um tile inteiro, e o
		// desenho tem que cair onde a PAREDE vai cair. Ver `CatalogoDeObras.Celula`.
		Vector2 alvo = mundo.GetGlobalMousePosition();
		(int cx, int cy) = CatalogoDeObras.Celula(alvo.X, alvo.Y);
		const int t = ZoneCollision.TileSize;

		Vector2 canto = new(cx * t, (cy + 1) * t - _fantasma.Texture.GetHeight());
		_fantasma.Position = canto;

		// VERMELHO QUANDO NAO CABE. As duas checagens baratas que o cliente consegue fazer sozinho:
		// longe demais, e dentro de parede. As outras ficam com o servidor.
		bool longe = World.Instancia?.PosicaoDesenhadaDe(GameClient.Instance?.LocalId ?? 0) is { } eu
					 && (Math.Abs(alvo.X - eu.X) > 96 || Math.Abs(alvo.Y - eu.Y) > 96);
		bool parede = mundo.Colisao?.BlockedCell(cx, cy) ?? false;

		_fantasma.Modulate = longe || parede
			? new Color(1f, 0.45f, 0.4f, 0.55f)
			: new Color(1, 1, 1, 0.55f);
	}

	public override void _Input(InputEvent evento)
	{
		if (_fantasma == null) return;

		if (evento is InputEventKey { Pressed: true, Keycode: Key.Escape })
		{
			Largar();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (evento is not InputEventMouseButton { Pressed: true } m) return;

		if (m.ButtonIndex == MouseButton.Right) { Largar(); GetViewport().SetInputAsHandled(); return; }
		if (m.ButtonIndex != MouseButton.Left) return;

		Vector2 alvo = World.Instancia?.GetGlobalMousePosition() ?? Vector2.Zero;
		(int cx, int cy) = CatalogoDeObras.Celula(alvo.X, alvo.Y);
		const int t = ZoneCollision.TileSize;

		// O PONTO QUE VAI PRO SERVIDOR E O CENTRO DA CELULA, e nao o pixel do clique: e assim que o
		// servidor guarda a obra, e mandar o pixel cru faria o desenho pular meio tile ao chegar.
		GameClient.Instance?.SendVerbo("tech_posicionar",
			$"{_naMao}/{cx * t + t / 2f:0}/{cy * t + t / 2f:0}");

		Largar();
		GetViewport().SetInputAsHandled();
	}
}
