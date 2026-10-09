using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O MODO DE CONSTRUIR -- a tecla B: uma barra de blocos no pe da tela, um fantasma na celula do mouse,
/// clicar ergue e o botao direito desmancha.
///
/// ============================ O PEDIDO ============================
/// O dono (2026-10-09): *"adicione construcao de base (paredes, pisos e portas) no jogo, no DM ja tinha
/// isso podendo ver como era la e melhorar pra deixar mais intuitivo e atual, sendo um estilo 'minecraft'
/// de construcao q quero. ao construir uma porta o jogo sempre pergunta se ela vai ter senha"*.
/// ==================================================================
///
/// ============================ O QUE O ORIGINAL TINHA ============================
/// A tecla M abre a "Build Window" (`BuildWindow.dm:153-161`): tres colunas (Tile, Decor, Barrier), cada
/// uma com "Build" e "Change". "Change" fecha a janela e abre uma grade com os 155 tiles, sem aba nem
/// busca; escolhido um, volta-se a janela e aperta-se "Build" ("Building on! Set!"). So entao clicar ou
/// arrastar no chao, a ate 2 tiles, ergue (`click.dm:44-48`, `:68-75`) -- e so com a janela ABERTA, que
/// nao fecha no X. Nao ha previa do que vai subir, nada diz por que um clique nao ergueu, e nao ha como
/// tirar um tile sem por outro por cima.
/// ================================================================================
///
/// ============================ O QUE VIROU ============================
///   * UMA TECLA liga e desliga (B; o M deste port e meditar). Com o modo ligado o corpo anda como sempre.
///   * A BARRA: nove casas no pe da tela. A RODA do mouse troca de casa; clicar numa casa tambem.
///     (Os numeros 1-5 ja sao a mira do golpe, e por isso a barra nao os usa.)
///   * A PALETA (tecla E, ou o botao da barra): todos os blocos em tres grupos -- paredes, pisos e
///     portas. Clicar num bloco o poe na casa escolhida. A barra fica gravada no `config.json`.
///   * O FANTASMA: o bloco, meio transparente, na celula sob o mouse. Vermelho quando nao sobe, e a
///     linha acima da barra diz POR QUE -- as mesmas perguntas do servidor (`RegrasDeBloco`).
///   * CLICAR ERGUE, e segurar e arrastar pinta. BOTAO DIREITO desmancha o que e seu, do mesmo jeito.
///   * A PORTA SEMPRE PERGUNTA A SENHA antes de subir (o pedido do dono); em branco, sobe sem.
/// =====================================================================
///
/// QUEM DECIDE E O SERVIDOR (`GameServer.Blocos.cs`). Daqui so sai a celula e o numero do bloco; o
/// bloco aparece quando o servidor o anuncia, e nao no clique.
/// </summary>
public partial class ModoDeConstruir : CanvasLayer
{
	public static ModoDeConstruir? Instancia { get; private set; }

	/// <summary>O modo esta ligado agora?</summary>
	public static bool Ligado => Instancia is { _ligado: true };

	/// <summary>A pergunta da senha esta aberta com o teclado dentro dela? Lido por `Foco.Digitando` (a sexta fonte).</summary>
	public static bool Digitando { get; private set; }

	private const int Casas = 9;

	/// <summary>
	/// A BARRA DE QUEM NUNCA MEXEU NELA: o bastante pra uma casa -- quatro paredes, tres pisos, duas portas.
	/// Ids do `blocos.json` (a folha do typepath do DM); o que nao existir no catalogo fica vazio.
	/// </summary>
	private static readonly string[] BarraPadrao =
		["brickwall", "stone", "Wall_Tile_3", "Roof", "woodfloor", "StoneFloor", "Floor2", "Door/Door4", "Door/Door1"];

	private bool _ligado;
	private int _casa;
	private readonly string[] _barra = new string[Casas];

	private Control _raiz = null!;
	private readonly Button[] _botoes = new Button[Casas];
	private Label _nome = null!;
	private Label _aviso = null!;
	private PanelContainer _paleta = null!;
	private VBoxContainer _gruposDaPaleta = null!;
	private bool _paletaMontada;
	private PanelContainer? _pergunta;
	private LineEdit? _campoDaSenha;
	private (int X, int Y) _celulaDaPorta;
	private ushort _numeroDaPorta;

	private Sprite2D? _fantasma;

	/// <summary>O botao esta descido e o mouse esta pintando (erguendo) ou varrendo (desmanchando).</summary>
	private bool _erguendo, _desmanchando;
	private (int X, int Y)? _ultimaCelula;

	private static readonly Color Sobe = new(1, 1, 1, 0.6f);
	private static readonly Color NaoSobe = new(1f, 0.4f, 0.35f, 0.6f);

	public override void _Ready()
	{
		Instancia = this;
		Layer = 4;   // a altura do menu da tecla E e do fantasma de assentar
		LerABarra();
		Montar();
	}

	public override void _ExitTree()
	{
		Desligar();
		if (Instancia == this) Instancia = null;
	}

	// =====================================================================
	// A BARRA
	// =====================================================================
	private void LerABarra()
	{
		List<string> gravada = Boot.Config.BarraDeBlocos;
		for (int i = 0; i < Casas; i++)
		{
			string id = i < gravada.Count ? gravada[i] : BarraPadrao[i];
			_barra[i] = BlocosNoCliente.Catalogo.Get(id) != null ? id : "";
		}
	}

	private void GravarABarra()
	{
		Boot.Config.BarraDeBlocos = [.. _barra];
		Boot.Config.Gravar();
	}

	private BlocoDef? NaMao => BlocosNoCliente.Catalogo.Get(_barra[_casa]);

	private void Montar()
	{
		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		// ---- o pe da tela: aviso, nome do bloco e as nove casas ----
		var pe = new VBoxContainer
		{
			AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1, AnchorBottom = 1,
			OffsetLeft = -260, OffsetRight = 260, OffsetTop = -112, OffsetBottom = -8,
			Alignment = BoxContainer.AlignmentMode.End,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		pe.AddThemeConstantOverride("separation", 2);
		_raiz.AddChild(pe);

		_aviso = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		_aviso.AddThemeFontSizeOverride("font_size", 13);
		_aviso.AddThemeColorOverride("font_color", Tema.Perigo);
		pe.AddChild(_aviso);

		_nome = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		_nome.AddThemeColorOverride("font_color", Tema.Destaque);
		pe.AddChild(_nome);

		var fila = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		fila.AddThemeConstantOverride("separation", 4);
		pe.AddChild(fila);

		for (int i = 0; i < Casas; i++)
		{
			int casa = i;
			var b = new Button
			{
				CustomMinimumSize = new Vector2(46, 46),
				ExpandIcon = true,
				IconAlignment = HorizontalAlignment.Center,
				FocusMode = Control.FocusModeEnum.None,   // senao o Espaco (soco) "aperta" a casa com foco
			};
			b.Pressed += () => Escolher(casa);
			fila.AddChild(b);
			_botoes[i] = b;
		}

		var todos = new Button
		{
			Text = "todos",
			TooltipText = "todos os blocos (E)",
			CustomMinimumSize = new Vector2(58, 46),
			FocusMode = Control.FocusModeEnum.None,
		};
		todos.Pressed += AlternarPaleta;
		fila.AddChild(todos);

		// ---- a paleta: fica acima do pe, e so e montada na primeira vez que abre ----
		_paleta = Tema.Painel1(10);
		_paleta.AnchorLeft = _paleta.AnchorRight = 0.5f;
		_paleta.AnchorTop = _paleta.AnchorBottom = 1;
		_paleta.OffsetLeft = -330;
		_paleta.OffsetRight = 330;
		_paleta.OffsetTop = -470;
		_paleta.OffsetBottom = -122;
		_paleta.Visible = false;
		_raiz.AddChild(_paleta);

		var rolagem = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_paleta.AddChild(rolagem);
		_gruposDaPaleta = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_gruposDaPaleta.AddThemeConstantOverride("separation", 6);
		rolagem.AddChild(_gruposDaPaleta);

		PintarABarra();
	}

	private void PintarABarra()
	{
		for (int i = 0; i < Casas; i++)
		{
			BlocoDef? d = BlocosNoCliente.Catalogo.Get(_barra[i]);
			_botoes[i].Icon = d != null && _ligado ? BlocosNoCliente.Icone(d) : null;
			_botoes[i].TooltipText = d?.Nome ?? "casa vazia -- abra a paleta (E) e escolha um bloco";
			_botoes[i].AddThemeStyleboxOverride("normal",
				Tema.Caixa(i == _casa ? Tema.PainelAceso : Tema.Painel, i == _casa ? Tema.BordaViva : Tema.Borda, 4));
		}

		_nome.Text = NaMao is { } m
			? $"{m.Nome}  ·  {NomeDaClasse(m.Classe)}"
			: "casa vazia -- aperte E e escolha um bloco";
	}

	private static string NomeDaClasse(ClasseDeBloco c) => c switch
	{
		ClasseDeBloco.Parede => "parede",
		ClasseDeBloco.Porta => "porta",
		_ => "piso",
	};

	private void Escolher(int casa)
	{
		_casa = ((casa % Casas) + Casas) % Casas;
		PintarABarra();
		TrocarOFantasma();
	}

	// =====================================================================
	// A PALETA
	// =====================================================================
	private void AlternarPaleta()
	{
		if (!_paletaMontada) MontarAPaleta();
		_paleta.Visible = !_paleta.Visible;
	}

	/// <summary>
	/// OS 122 ICONES, na primeira abertura: e aqui que as 30 folhas de bloco sao lidas do disco. Num menu
	/// que o jogador acabou de pedir, e nao na chegada ao mundo nem no meio de uma briga.
	/// </summary>
	private void MontarAPaleta()
	{
		_paletaMontada = true;
		foreach ((ClasseDeBloco classe, string titulo) in new[]
		{
			(ClasseDeBloco.Parede, "PAREDES -- barram o corpo, a vista e quem voa"),
			(ClasseDeBloco.Piso, "PISOS -- cobrem: debaixo deles não chove, e de fora não se vê"),
			(ClasseDeBloco.Porta, "PORTAS -- ao erguer, o jogo pergunta a senha"),
		})
		{
			_gruposDaPaleta.AddChild(Tema.Legenda(titulo, Tema.TextoFraco, 12));
			var grade = new HFlowContainer();
			grade.AddThemeConstantOverride("h_separation", 3);
			grade.AddThemeConstantOverride("v_separation", 3);
			_gruposDaPaleta.AddChild(grade);

			foreach (BlocoDef d in BlocosNoCliente.Catalogo.Todos)
			{
				if (d.Classe != classe) continue;
				string id = d.Id;
				var b = new Button
				{
					CustomMinimumSize = new Vector2(38, 38),
					Icon = BlocosNoCliente.Icone(d),
					ExpandIcon = true,
					TooltipText = d.Nome,
					FocusMode = Control.FocusModeEnum.None,
				};
				b.Pressed += () => PorNaCasa(id);
				grade.AddChild(b);
			}
		}
	}

	private void PorNaCasa(string id)
	{
		_barra[_casa] = id;
		GravarABarra();
		PintarABarra();
		TrocarOFantasma();
	}

	// =====================================================================
	// LIGAR E DESLIGAR
	// =====================================================================
	private void Ligar()
	{
		if (_ligado || GameClient.Instance is not { } cli) return;
		if (BlocosNoCliente.Catalogo.Total == 0) { Chat.Sistema("este jogo está sem o catálogo de blocos."); return; }
		if (!RegrasDeBloco.ZonaAceita(cli.Zone)) { Chat.Sistema(RegrasDeBloco.Motivo(RecusaDeBloco.Zona)); return; }

		_ligado = true;
		_raiz.Visible = true;
		_aviso.Text = "";
		PintarABarra();
		TrocarOFantasma();

		// A LICAO SO NA PRIMEIRA VEZ DA SESSAO: quem liga e desliga o modo a cada parede nao precisa reler.
		if (_jaEnsinou) return;
		_jaEnsinou = true;
		Chat.Sistema($"modo de construir: clique ergue (segure e arraste pra pintar), botão direito desmancha, "
					 + $"a roda troca de bloco, E abre todos os blocos. {Teclas.Nome(TeclaDoModo())} ou Esc sai.");
	}

	private static bool _jaEnsinou;

	private void Desligar()
	{
		_ligado = false;
		_erguendo = _desmanchando = false;
		_ultimaCelula = null;
		_mouse = null;
		FecharPergunta();
		if (IsInstanceValid(_raiz)) _raiz.Visible = false;
		if (IsInstanceValid(_paleta)) _paleta.Visible = false;
		if (_fantasma != null && IsInstanceValid(_fantasma)) _fantasma.QueueFree();
		_fantasma = null;
	}

	private static Key TeclaDoModo() => Teclas.Teclado("ui_construir") is { Length: > 0 } t ? t[0] : Key.None;

	private void TrocarOFantasma()
	{
		if (!_ligado || World.Instancia is not { } mundo) return;
		if (_fantasma == null || !IsInstanceValid(_fantasma))
		{
			// FILHO DO MUNDO, e nao desta camada: ele mora na celula, e a celula e do mundo (a camera
			// anda e ele fica). So desenho, e so deste cliente -- nada viaja ate o clique.
			_fantasma = new Sprite2D { Centered = false, ZIndex = 50, Modulate = Sobe };
			mundo.AddChild(_fantasma);
		}
		_fantasma.Texture = NaMao is { } d ? BlocosNoCliente.Icone(d) : null;
	}

	// =====================================================================
	// A PERGUNTA: "PODE AQUI?"
	// =====================================================================
	/// <summary>
	/// O QUE IMPEDE ESTE BLOCO DE SUBIR NESTA CELULA, do que o cliente sabe. As mesmas perguntas do
	/// servidor (`GameServer.RecusaDeErguer`), na mesma ordem, com as listas que este cliente tem: e um
	/// palpite bem informado, e nao uma promessa -- por isso o bloco so aparece quando o servidor anuncia.
	///
	/// DUAS COISAS O CLIENTE NAO SABE e o servidor responde: o chao que nao aceita construcao (o plano
	/// `.duro` so e lido la) e os corpos dos outros em cima da celula.
	/// </summary>
	internal static RecusaDeBloco Recusa(BlocoDef def, int cx, int cy)
	{
		if (World.Instancia is not { } mundo || GameClient.Instance is not { } cli) return RecusaDeBloco.Zona;
		if (!RegrasDeBloco.ZonaAceita(cli.Zone)) return RecusaDeBloco.Zona;
		if (mundo.PosicaoDesenhadaDe(cli.LocalId) is not { } onde) return RecusaDeBloco.Pode;   // o servidor sabe onde estou
		var eu = new Vec2(onde.X, onde.Y);

		RecusaDeBloco r = RegrasDeBloco.DoTerreno(mundo.Colisao, eu, cx, cy, cli.CenarioCaido.Contains((cx, cy)));
		if (r != RecusaDeBloco.Pode) return r;
		if (cli.Blocos.TryGetValue((cx, cy), out GameClient.BlocoInfo ja) && !ja.Meu) return RecusaDeBloco.DeOutro;

		if (def.Classe != ClasseDeBloco.Piso)
		{
			foreach (GameClient.ObraInfo o in cli.Obras)
				if (Jandirus.Core.Tech.CatalogoDeObras.Celula(o.Pos.X, o.Pos.Y) == (cx, cy)) return RecusaDeBloco.Coisa;
			if (RegrasDeBloco.CorpoNaCelula(eu, cx, cy)) return RecusaDeBloco.Corpo;
		}
		return RecusaDeBloco.Pode;
	}

	/// <summary>A celula do mundo sob um ponto da tela.</summary>
	private static (int X, int Y)? CelulaEm(Vector2 naTela)
	{
		if (World.Instancia is not { } mundo) return null;
		Vector2 p = mundo.GetCanvasTransform().AffineInverse() * naTela;
		const float T = ZoneCollision.TileSize;
		return ((int)Mathf.Floor(p.X / T), (int)Mathf.Floor(p.Y / T));
	}

	/// <summary>
	/// ONDE O MOUSE ESTA NA TELA, pelo ultimo EVENTO de mouse que passou por aqui. Nulo ate o primeiro.
	///
	/// DO EVENTO, e nao do `GetMousePosition`/`GetGlobalMousePosition`: esses dois perguntam ao SISTEMA
	/// onde o cursor esta, e o clique chega com a posicao DELE (`m.Position`). No jogo as duas coincidem;
	/// numa bancada que empurra eventos sem mexer no cursor de verdade o fantasma ia pra uma celula e o
	/// clique pra outra -- e arrastar pintava a celula errada. Uma fonte so pras duas coisas.
	/// </summary>
	private Vector2? _mouse;

	/// <summary>A celula sob o mouse agora. A camera anda com o mouse parado, entao a conta e refeita a cada quadro.</summary>
	private (int X, int Y)? CelulaDoMouse() => CelulaEm(_mouse ?? GetViewport().GetMousePosition());

	/// <summary>
	/// O MOUSE ESTA EM CIMA DE ALGO QUE SE CLICA? Entao o clique e de la, e nao do mundo.
	///
	/// NAO E "HA UM CONTROLE SOB O MOUSE": o HUD tem paineis de tela inteira que nao sao botao de nada, e
	/// com essa pergunta o modo nunca ergueria um bloco. Conta o que e DESTE modo (a barra, a paleta) e o
	/// que e de clicar ou de digitar em qualquer tela (botao, campo de texto, lista, barra de rolagem).
	/// </summary>
	private bool MouseNaInterface()
	{
		if (GetViewport().GuiGetHoveredControl() is not { } c) return false;
		if (c is BaseButton or LineEdit or TextEdit or ItemList or ScrollBar or Slider) return true;
		for (Node? n = c; n != null; n = n.GetParent())
			if (n == _paleta) return true;
		return false;
	}

	public override void _Process(double delta)
	{
		if (!_ligado) return;

		// SAIU DE ONDE SE CONSTROI (embarcou, morreu, foi pro espaco): o modo se desliga sozinho.
		if (GameClient.Instance is not { } cli || !RegrasDeBloco.ZonaAceita(cli.Zone)) { Desligar(); return; }
		if (_fantasma == null || !IsInstanceValid(_fantasma)) TrocarOFantasma();
		if (_fantasma == null) return;

		bool escolhendo = _pergunta != null || MouseNaInterface();
		(int X, int Y)? alvo = _pergunta != null ? _celulaDaPorta : CelulaDoMouse();
		if (alvo is not { } c || NaMao is not { } d || (escolhendo && _pergunta == null))
		{
			_fantasma.Visible = false;
			if (_pergunta == null) _aviso.Text = "";
			return;
		}

		const int T = ZoneCollision.TileSize;
		_fantasma.Visible = true;
		_fantasma.Position = new Vector2(c.X * T, c.Y * T);

		// COM A PERGUNTA DA SENHA ABERTA o fantasma fica parado na celula da porta, e a linha de aviso e
		// da pergunta ("escreva a senha...").
		if (_pergunta != null) { _fantasma.Modulate = Sobe; return; }

		RecusaDeBloco r = Recusa(d, c.X, c.Y);
		_fantasma.Modulate = r == RecusaDeBloco.Pode ? Sobe : NaoSobe;
		_aviso.Text = r == RecusaDeBloco.Pode ? "" : RegrasDeBloco.Motivo(r);

		// SEGURAR E ARRASTAR: uma tentativa por celula em que o mouse ENTRA (o `turf/MouseDrag` do DM,
		// `click.dm:68-75`). O mouse rapido pula celulas de um quadro pro outro, e a parede sairia com
		// buracos: as celulas do meio do caminho sao visitadas em linha reta.
		if (_ultimaCelula is not { } de || de == c || !(_erguendo || _desmanchando)) return;
		_ultimaCelula = c;
		int passos = Math.Max(Math.Abs(c.X - de.X), Math.Abs(c.Y - de.Y));
		for (int i = 1; i <= passos; i++)
		{
			int x = de.X + (int)MathF.Round((c.X - de.X) * i / (float)passos);
			int y = de.Y + (int)MathF.Round((c.Y - de.Y) * i / (float)passos);
			if (_erguendo) Erguer(x, y, noArrasto: true);
			else if (_desmanchando) Desmanchar(x, y, noArrasto: true);
		}
	}

	// =====================================================================
	// ERGUER E DESMANCHAR
	// =====================================================================
	private void Erguer(int cx, int cy, bool noArrasto)
	{
		if (GameClient.Instance is not { } cli || NaMao is not { } d) return;

		RecusaDeBloco r = Recusa(d, cx, cy);
		if (r != RecusaDeBloco.Pode)
		{
			// NO CLIQUE A FRASE VAI PRO CHAT TAMBEM (a linha acima da barra some quando o mouse anda);
			// no arrasto nao, senao pintar ao longo de um rio encheria o chat de "nao da pra construir na agua".
			if (!noArrasto) Chat.Sistema(RegrasDeBloco.Motivo(r));
			return;
		}

		// JA ESTA LA, IGUAL E MEU: o arrasto passa por cima sem mandar nada.
		if (cli.Blocos.TryGetValue((cx, cy), out GameClient.BlocoInfo ja) && ja.Meu && ja.Numero == d.Numero) return;

		if (d.Classe == ClasseDeBloco.Porta)
		{
			// A PORTA NAO SE PINTA: ela pergunta a senha, uma por vez.
			_erguendo = false;
			AbrirPergunta(cx, cy, d.Numero);
			return;
		}

		cli.SendBloco(Protocol.BlocoErguer, cx, cy, d.Numero);
	}

	private void Desmanchar(int cx, int cy, bool noArrasto)
	{
		if (GameClient.Instance is not { } cli) return;
		if (!cli.Blocos.TryGetValue((cx, cy), out GameClient.BlocoInfo b))
		{
			if (!noArrasto) Chat.Sistema(RegrasDeBloco.Motivo(RecusaDeBloco.Nada));
			return;
		}

		// O DE OUTRO SO VAI NO CLIQUE: o servidor responde (o admin desmancha o de qualquer um, e o
		// cliente nao precisa saber quem e admin). No arrasto, so o que e meu.
		if (!b.Meu && noArrasto) return;
		cli.SendBloco(Protocol.BlocoDesmanchar, cx, cy, 0);
	}

	// =====================================================================
	// A PERGUNTA DA PORTA
	// =====================================================================
	/// <summary>
	/// "ESTA PORTA VAI TER SENHA?" -- sempre, antes de a porta subir (o pedido do dono). No DM a porta
	/// nasce sem senha e o dono a poe depois, com uma Key (`Tier 1.dm:233-237`).
	/// </summary>
	private void AbrirPergunta(int cx, int cy, ushort numero)
	{
		FecharPergunta();
		_celulaDaPorta = (cx, cy);
		_numeroDaPorta = numero;

		_pergunta = Tema.Painel1(14);
		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		centro.AddChild(_pergunta);
		_raiz.AddChild(centro);

		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
		caixa.AddThemeConstantOverride("separation", 8);
		_pergunta.AddChild(caixa);

		var titulo = new Label { Text = "Esta porta vai ter senha?", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 18);
		caixa.AddChild(titulo);

		caixa.AddChild(Tema.Legenda(
			"Sem senha, ela abre pra qualquer um que encostar.\nCom senha, é uma parede: só passa você e quem digitar a senha nela.",
			Tema.TextoFraco, 12));

		_campoDaSenha = new LineEdit
		{
			MaxLength = RegrasDeBloco.MaxSenha,
			PlaceholderText = $"a senha (até {RegrasDeBloco.MaxSenha} letras)",
			CustomMinimumSize = new Vector2(0, 34),
		};
		_campoDaSenha.TextSubmitted += _ => ResponderPergunta(comSenha: true);
		caixa.AddChild(_campoDaSenha);

		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 6);
		caixa.AddChild(linha);

		var cancelar = new Button { Text = "Cancelar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		cancelar.Pressed += FecharPergunta;
		linha.AddChild(cancelar);
		var sem = new Button { Text = "Sem senha", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		sem.Pressed += () => ResponderPergunta(comSenha: false);
		linha.AddChild(sem);
		var com = new Button { Text = "Com senha", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		com.Pressed += () => ResponderPergunta(comSenha: true);
		linha.AddChild(com);

		Digitando = true;
		_campoDaSenha.CallDeferred(Control.MethodName.GrabFocus);
	}

	private void ResponderPergunta(bool comSenha)
	{
		if (_pergunta == null || _campoDaSenha == null) return;

		string senha = comSenha ? RegrasDeBloco.SenhaLimpa(_campoDaSenha.Text) : "";
		if (comSenha && senha.Length == 0)
		{
			_aviso.Text = "escreva a senha, ou escolha \"Sem senha\".";
			_campoDaSenha.GrabFocus();
			return;
		}

		GameClient.Instance?.SendBloco(Protocol.BlocoErguer, _celulaDaPorta.X, _celulaDaPorta.Y, _numeroDaPorta, senha);
		FecharPergunta();
	}

	private void FecharPergunta()
	{
		Digitando = false;
		if (_pergunta?.GetParent() is { } pai && IsInstanceValid(pai)) pai.QueueFree();
		_pergunta = null;
		_campoDaSenha = null;
	}

	// =====================================================================
	// A ENTRADA
	// =====================================================================
	/// <summary>
	/// NO `_Input`, e nao no `_UnhandledInput`: com o modo ligado o Esc tem de ganhar do menu de pausa, o
	/// E do menu de interacao, e o clique do duplo clique do mundo (alvo e Zanzoken).
	/// </summary>
	public override void _Input(InputEvent evento)
	{
		if (evento is InputEventKey { Pressed: true, Echo: false } k)
		{
			if (_pergunta != null)
			{
				// SO O ESC e daqui enquanto a pergunta esta aberta: o resto e do campo de texto.
				if (k.Keycode == Key.Escape) { FecharPergunta(); GetViewport().SetInputAsHandled(); }
				return;
			}

			// O "B" E UMA DAS LETRAS QUE O EMBATE SORTEIA -- ver `Foco.AtalhosMudos`.
			if (Foco.AtalhosMudos) return;

			if (Teclas.Bate("ui_construir", k))
			{
				if (_ligado) Desligar(); else Ligar();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (!_ligado) return;

			if (k.Keycode == Key.Escape)
			{
				if (_paleta.Visible) _paleta.Visible = false; else Desligar();
				GetViewport().SetInputAsHandled();
			}
			else if (Teclas.Bate("ui_interagir", k))
			{
				AlternarPaleta();
				GetViewport().SetInputAsHandled();
			}
			return;
		}

		if (_ligado && evento is InputEventMouse onde) _mouse = onde.Position;
		if (!_ligado || _pergunta != null || evento is not InputEventMouseButton m) return;

		// SOLTAR O BOTAO PARA O ARRASTO, onde quer que o mouse esteja.
		if (!m.Pressed)
		{
			if (m.ButtonIndex == MouseButton.Left) _erguendo = false;
			if (m.ButtonIndex == MouseButton.Right) _desmanchando = false;
			return;
		}

		if (MouseNaInterface()) return;   // o clique e da barra, da paleta, do chat...

		switch (m.ButtonIndex)
		{
			case MouseButton.WheelUp: Escolher(_casa - 1); break;
			case MouseButton.WheelDown: Escolher(_casa + 1); break;

			case MouseButton.Left when CelulaEm(m.Position) is { } c:
				_erguendo = true;
				_ultimaCelula = c;
				Erguer(c.X, c.Y, noArrasto: false);
				break;

			case MouseButton.Right when CelulaEm(m.Position) is { } c:
				_desmanchando = true;
				_ultimaCelula = c;
				Desmanchar(c.X, c.Y, noArrasto: false);
				break;

			default: return;
		}

		// ENGOLIDO: senao o clique viraria o duplo clique do mundo (marcar alvo, Zanzoken).
		GetViewport().SetInputAsHandled();
	}

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--diagconstruir`)
	// =====================================================================
	/// <summary>O modo esta na tela (a barra desenhada)?</summary>
	public bool NaTela => _ligado && IsInstanceValid(_raiz) && _raiz.Visible;

	/// <summary>O id do bloco na casa escolhida ("" = casa vazia).</summary>
	public string NaMaoDeTeste => _barra[_casa];

	/// <summary>A casa escolhida (0..8) e o id de cada casa.</summary>
	public int CasaDeTeste => _casa;
	public IReadOnlyList<string> BarraDeTeste => _barra;

	/// <summary>O fantasma: o node (filho do mundo), ou nulo. A bancada mede o que esta NA TELA.</summary>
	public Sprite2D? FantasmaDeTeste => _fantasma != null && IsInstanceValid(_fantasma) ? _fantasma : null;

	/// <summary>A linha de aviso acima da barra (o motivo da recusa da celula sob o mouse).</summary>
	public string AvisoDeTeste => IsInstanceValid(_aviso) ? _aviso.Text : "";

	/// <summary>A paleta esta aberta, e quantos botoes de bloco ela desenhou.</summary>
	public bool PaletaNaTela => IsInstanceValid(_paleta) && _paleta.Visible;
	public int BlocosNaPaletaDeTeste => Botoes(_gruposDaPaleta).Count();

	/// <summary>Aperta, na paleta aberta, o botao do bloco com este nome. Pelo SINAL do botao.</summary>
	public bool ApertarNaPaleta(string nome)
	{
		foreach (Button b in Botoes(_gruposDaPaleta))
			if (b.TooltipText == nome) { b.EmitSignal(BaseButton.SignalName.Pressed); return true; }
		return false;
	}

	/// <summary>A pergunta da senha esta na tela?</summary>
	public bool PerguntaNaTela => _pergunta != null && IsInstanceValid(_pergunta);

	/// <summary>Responde a pergunta da porta pelos botoes dela: digita (se houver o que digitar) e aperta o rotulo.</summary>
	public bool ResponderDeTeste(string rotulo, string senha = "")
	{
		if (_pergunta == null || _campoDaSenha == null) return false;
		_campoDaSenha.Text = senha;
		foreach (Button b in Botoes(_pergunta))
			if (b.Text == rotulo) { b.EmitSignal(BaseButton.SignalName.Pressed); return true; }
		return false;
	}

	private static IEnumerable<Button> Botoes(Node raiz)
	{
		foreach (Node n in raiz.GetChildren())
		{
			if (n is Button b) yield return b;
			foreach (Button f in Botoes(n)) yield return f;
		}
	}
}
