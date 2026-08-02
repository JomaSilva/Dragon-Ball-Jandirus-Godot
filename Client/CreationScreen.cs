using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Races;

namespace Jandirus.Client;

/// <summary>
/// CRIACAO DE PERSONAGEM com PREVIA AO VIVO.
///
/// O boneco fica na tela e muda NA HORA a cada escolha -- cabelo, cor, pele, roupa. Tudo
/// acontece ANTES de conectar: so no fim o cliente abre a conexao e manda a ficha inteira,
/// e o servidor confere. Isso significa que esta tela nao depende de rede nenhuma, e que o
/// jogador nunca ocupa uma vaga no servidor enquanto decide o penteado.
///
/// A tela e so COLETA e DESENHO. Quem decide stat, BP e classe e o servidor, com o mesmo
/// RaceCatalog -- a classe continua sendo sorteio cego, por isso ela nao aparece aqui.
/// </summary>
public partial class CreationScreen : CanvasLayer
{
	public event Action<CharacterDraft, Appearance>? Pronto;
	public event Action? Cancelado;

	/// <summary>Nome ja digitado na tela de login -- nao faz o jogador digitar duas vezes.</summary>
	public string NomeInicial = "";

	private readonly CharacterDraft _ficha = new();
	private readonly Appearance _visual = new();
	private RaceCatalog? _racas;
	private VisualCatalog? _cat;

	private OptionButton _planeta = null!, _raca = null!, _linhagem = null!, _genero = null!;
	private OptionButton _corpo = null!;
	private GridContainer _gradeCabelo = null!, _gradeRoupa = null!;
	private LineEdit _nome = null!;
	private SpinBox _idade = null!;
	private Label _descricao = null!, _erro = null!;
	private HBoxContainer _linhaLinhagem = null!, _linhaGenero = null!, _linhaCorpo = null!;
	private VBoxContainer _blocoCabelo = null!;
	private HBoxContainer _linhaCorCabelo = null!, _linhaCorPele = null!;
	private ColorPickerButton _corCabelo = null!, _corOlho = null!, _corPele = null!;
	private CheckBox _tingirCabelo = null!, _tingirOlho = null!, _tingirPele = null!;
	private CharacterVisual _boneco = null!;
	private VBoxContainer _listaRoupa = null!;

	public override void _Ready()
	{
		// acima da tela de login (que fica na camada 1). O Boot ainda esconde a de login,
		// mas duas telas empilhadas na MESMA camada dependem da ordem de criacao pra decidir
		// quem fica na frente -- e isso e o tipo de coisa que volta a quebrar sozinha.
		Layer = 2;

		_racas = Carregar("res://Assets/Data/races.json", RaceCatalog.Parse);
		_cat = Carregar("res://Assets/Data/visual.json", VisualCatalog.Parse);
		if (_racas == null) GD.PushWarning("[criacao] sem races.json -- rode o AssetPipeline (comando 'races')");
		if (_cat == null) GD.PushWarning("[criacao] sem visual.json -- rode o AssetPipeline (comando 'visual')");

		Montar();
		AoTrocarPlaneta(0);
	}

	private static T? Carregar<T>(string caminho, Func<string, T> parse) where T : class =>
		Godot.FileAccess.FileExists(caminho) ? parse(Godot.FileAccess.GetFileAsString(caminho)) : null;

	// =====================================================================
	// LAYOUT
	// =====================================================================
	private void Montar()
	{
		var fundo = new ColorRect { Color = new Color("11131c"), AnchorRight = 1, AnchorBottom = 1 };
		AddChild(fundo);

		var centro = new CenterContainer
		{
			AnchorRight = 1, AnchorBottom = 1,
			GrowHorizontal = Control.GrowDirection.Both,
			GrowVertical = Control.GrowDirection.Both,
		};
		AddChild(centro);

		var colunas = new HBoxContainer();
		colunas.AddThemeConstantOverride("separation", 24);
		centro.AddChild(colunas);

		colunas.AddChild(MontarPalco());
		colunas.AddChild(MontarFormulario());
	}

	/// <summary>O palco da previa: o boneco grande, girando com as setas.</summary>
	private Control MontarPalco()
	{
		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(230, 0) };

		var moldura = new PanelContainer { CustomMinimumSize = new Vector2(230, 260) };
		caixa.AddChild(moldura);

		// RECORTA: o boneco e um Node2D e ignora o layout do container -- sem isto ele escorre
		// por cima dos botoes de girar quando o sprite e alto
		var palco = new Control { ClipContents = true };
		moldura.AddChild(palco);

		// escala 5x: o sprite tem 32 px e a ideia e ver o penteado, nao adivinhar
		_boneco = new CharacterVisual
		{
			Name = "Previa",
			Position = new Vector2(115, 170),
			Scale = new Vector2(5, 5),
		};
		palco.AddChild(_boneco);

		var giro = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		foreach ((string txt, Jandirus.Core.World.Facing dir) in new[]
				 {
					 ("◀", Jandirus.Core.World.Facing.West), ("▼", Jandirus.Core.World.Facing.South),
					 ("▲", Jandirus.Core.World.Facing.North), ("▶", Jandirus.Core.World.Facing.East),
				 })
		{
			var b = new Button { Text = txt, CustomMinimumSize = new Vector2(46, 0) };
			Jandirus.Core.World.Facing d = dir;
			b.Pressed += () => _boneco.SetMotion(d, false);
			giro.AddChild(b);
		}
		caixa.AddChild(giro);

		var andar = new CheckBox { Text = "andando", ButtonPressed = false };
		andar.Toggled += on => _boneco.SetMotion(_facingAtual, on);
		caixa.AddChild(andar);

		return caixa;
	}

	private Jandirus.Core.World.Facing _facingAtual = Jandirus.Core.World.Facing.South;

	private Control MontarFormulario()
	{
		var painel = new PanelContainer();
		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
		painel.AddChild(caixa);

		var titulo = new Label { Text = "NOVO GUERREIRO", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 22);
		caixa.AddChild(titulo);
		caixa.AddChild(new HSeparator());

		_nome = new LineEdit { PlaceholderText = "nome do personagem", MaxLength = 24, Text = NomeInicial };
		caixa.AddChild(Linha("Nome", _nome));

		_planeta = new OptionButton();
		foreach (string p in CharacterDraft.Planetas) _planeta.AddItem(NomeBonito(p));
		_planeta.ItemSelected += AoTrocarPlaneta;
		caixa.AddChild(Linha("Planeta natal", _planeta));

		_raca = new OptionButton();
		_raca.ItemSelected += _ => AoTrocarRaca();
		caixa.AddChild(Linha("Raca", _raca));

		_linhagem = new OptionButton();
		_linhaLinhagem = Linha("Linhagem", _linhagem);
		caixa.AddChild(_linhaLinhagem);

		_genero = new OptionButton();
		_genero.AddItem("Masculino");
		_genero.AddItem("Feminino");
		_genero.ItemSelected += _ => { AoTrocarGenero(); };
		_linhaGenero = Linha("Genero", _genero);
		caixa.AddChild(_linhaGenero);

		_idade = new SpinBox { MinValue = 1, MaxValue = 200, Value = 18 };
		caixa.AddChild(Linha("Idade", _idade));

		caixa.AddChild(new HSeparator());

		_corpo = new OptionButton();
		_corpo.ItemSelected += i => { _visual.Corpo = (int)i; _visual.Tom = (int)i; Repintar(); };
		_linhaCorpo = Linha("Corpo", _corpo);
		caixa.AddChild(_linhaCorpo);

		// COR OPCIONAL. O padrao e NAO tingir: os sprites ja vem coloridos, com realce e
		// sombra desenhados. Quando o jogador liga a cor, ela e SOMADA por cima (o ICON_ADD
		// do jogo) -- multiplicar apagaria o desenho e o cabelo viraria uma silhueta.
		(_linhaCorPele, _tingirPele, _corPele) = LinhaDeCor("Cor do corpo", new Color("ff9ab5"),
			() => { _visual.CorPele = _tingirPele.ButtonPressed ? DeCor(_corPele.Color) : null; Repintar(); });
		caixa.AddChild(_linhaCorPele);

		// CABELO: grade de icones. Ver o penteado antes de escolher e o ponto todo -- um
		// dropdown com 59 nomes ("Kylin 2"?) nao diz nada.
		_blocoCabelo = new VBoxContainer();
		_blocoCabelo.AddChild(new Label { Text = "Cabelo" });
		_gradeCabelo = new GridContainer { Columns = 8 };
		_blocoCabelo.AddChild(Rolagem(_gradeCabelo, 150));
		caixa.AddChild(_blocoCabelo);

		(_linhaCorCabelo, _tingirCabelo, _corCabelo) = LinhaDeCor("Colorir o cabelo", new Color("8a4b2a"),
			() => { _visual.CorCabelo = _tingirCabelo.ButtonPressed ? DeCor(_corCabelo.Color) : null; Repintar(); });
		caixa.AddChild(_linhaCorCabelo);

		(HBoxContainer linhaOlho, _tingirOlho, _corOlho) = LinhaDeCor("Colorir os olhos", new Color("2857c8"),
			() => { _visual.CorOlho = _tingirOlho.ButtonPressed ? DeCor(_corOlho.Color) : null; Repintar(); });
		caixa.AddChild(linhaOlho);

		caixa.AddChild(new Label { Text = $"Roupa (ate {Appearance.MaxRoupa} pecas -- clique pra vestir e tirar)" });
		_gradeRoupa = new GridContainer { Columns = 8 };
		caixa.AddChild(Rolagem(_gradeRoupa, 150));
		_listaRoupa = new VBoxContainer();
		caixa.AddChild(_listaRoupa);

		MontarGrades();

		_descricao = new Label { AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(0, 48) };
		_descricao.AddThemeColorOverride("font_color", new Color("9fb4d8"));
		caixa.AddChild(_descricao);

		_erro = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_erro.AddThemeColorOverride("font_color", new Color("d88a8a"));
		caixa.AddChild(_erro);

		var botoes = new HBoxContainer();
		var voltar = new Button { Text = "Voltar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		voltar.Pressed += () => { Cancelado?.Invoke(); QueueFree(); };
		botoes.AddChild(voltar);

		var criar = new Button { Text = "Entrar no mundo", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		criar.Pressed += Confirmar;
		botoes.AddChild(criar);
		caixa.AddChild(botoes);

		return painel;
	}

	private void RedesenharRoupa()
	{
		foreach (Node n in _listaRoupa.GetChildren()) n.QueueFree();
		foreach (string peca in _visual.Roupa.ToArray())
		{
			var h = new HBoxContainer();
			h.AddChild(new Label { Text = "  " + NomeDeArquivo(peca), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
			var x = new Button { Text = "tirar" };
			string alvo = peca;
			x.Pressed += () => { _visual.Roupa.Remove(alvo); Repintar(); };
			h.AddChild(x);
			_listaRoupa.AddChild(h);
		}
	}

	// =====================================================================
	// GRADES DE ICONE
	// =====================================================================
	/// <summary>Uma area rolavel com altura fixa -- 59 cabelos nao cabem numa tela.</summary>
	private static ScrollContainer Rolagem(Control dentro, int altura)
	{
		var sc = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(0, altura),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		dentro.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		sc.AddChild(dentro);
		return sc;
	}

	/// <summary>
	/// O primeiro quadro de um SpriteFrames, virado textura -- e o icone do botao.
	/// Barato: o AtlasTexture aponta pro mesmo PNG ja carregado, nao copia pixel nenhum.
	/// </summary>
	private static Texture2D? Miniatura(string caminho)
	{
		var f = ResourceLoader.Load<SpriteFrames>(caminho);
		if (f == null) return null;

		// A ordem importa: a pose PARADA de frente e o melhor retrato. Mas nem toda peca tem
		// uma -- depois que a caminhada virou um estado separado (`walk_*`), varias so tem
		// essa. Procurar so por `default_south` deixava a grade quase toda sem icone.
		foreach (string anim in new[] { "default_south", "walk_south", "default_east", "walk_east" })
			if (f.HasAnimation(anim) && f.GetFrameCount(anim) > 0)
				return f.GetFrameTexture(anim, 0);

		// ultimo recurso: o primeiro quadro de qualquer animacao que exista
		foreach (StringName anim in f.GetAnimationNames())
			if (f.GetFrameCount(anim) > 0)
				return f.GetFrameTexture(anim, 0);

		return null;
	}

	private static Button BotaoIcone(Texture2D? icone, string dica)
	{
		var b = new Button
		{
			CustomMinimumSize = new Vector2(48, 48),
			TooltipText = dica,
			IconAlignment = HorizontalAlignment.Center,
			ExpandIcon = true,
			ToggleMode = true,
		};
		if (icone != null) b.Icon = icone;
		else b.Text = dica.Length > 4 ? dica[..4] : dica;
		return b;
	}

	/// <summary>Monta as duas grades UMA VEZ. Trocar de raca so muda quais ficam visiveis.</summary>
	private void MontarGrades()
	{
		if (_cat == null) return;

		foreach ((string nome, string? sprite) in _cat.Cabelos)
		{
			Button b = BotaoIcone(sprite == null ? null : Miniatura(sprite), nome);
			string estilo = nome;
			b.Pressed += () =>
			{
				_visual.Cabelo = estilo;
				MarcarSelecionado(_gradeCabelo, b);
				Repintar();
			};
			_gradeCabelo.AddChild(b);
		}

		foreach (string peca in _cat.Roupas)
		{
			Button b = BotaoIcone(Miniatura(peca), NomeDeArquivo(peca));
			string alvo = peca;
			b.Pressed += () =>
			{
				if (_visual.Roupa.Remove(alvo)) { b.ButtonPressed = false; Repintar(); return; }
				if (_visual.Roupa.Count >= Appearance.MaxRoupa)
				{
					b.ButtonPressed = false;
					_erro.Text = $"o guarda-roupa leva no maximo {Appearance.MaxRoupa} pecas";
					return;
				}
				_visual.Roupa.Add(alvo);
				b.ButtonPressed = true;
				_erro.Text = "";
				Repintar();
			};
			_gradeRoupa.AddChild(b);
		}
	}

	/// <summary>Grade de escolha unica: so um botao fica apertado.</summary>
	private static void MarcarSelecionado(Node grade, Button escolhido)
	{
		foreach (Node n in grade.GetChildren())
			if (n is Button b) b.ButtonPressed = ReferenceEquals(b, escolhido);
	}

	/// <summary>
	/// Uma linha de "colorir isto?": a caixa liga a cor, o seletor escolhe qual. Desligada
	/// (o padrao) o sprite fica com a cor que o artista desenhou.
	/// </summary>
	private static (HBoxContainer, CheckBox, ColorPickerButton) LinhaDeCor(string rotulo, Color inicial, Action aoMudar)
	{
		var h = new HBoxContainer();
		var caixa = new CheckBox { Text = rotulo, CustomMinimumSize = new Vector2(190, 0) };
		var cor = new ColorPickerButton
		{
			Color = inicial,
			CustomMinimumSize = new Vector2(0, 26),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			Disabled = true,
			EditAlpha = false,
		};
		caixa.Toggled += on => { cor.Disabled = !on; aoMudar(); };
		cor.ColorChanged += _ => aoMudar();
		h.AddChild(caixa);
		h.AddChild(cor);
		return (h, caixa, cor);
	}

	private static HBoxContainer Linha(string rotulo, Control campo)
	{
		var h = new HBoxContainer();
		h.AddChild(new Label { Text = rotulo, CustomMinimumSize = new Vector2(130, 0) });
		campo.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		h.AddChild(campo);
		return h;
	}

	private static string NomeDeArquivo(string res) =>
		res.Substring(res.LastIndexOf('/') + 1).Replace(".tres", "").Replace('_', ' ');

	private static string NomeBonito(string s) => s switch
	{
		"Earth" => "Terra", "Vegeta" => "Vegeta", "Namek" => "Namek",
		"Heaven" => "Outro Mundo", "Hell" => "Inferno",
		"Icer" => "Frost Demon", "Saibaman" => "Saibamen",
		"Kanassa" => "Kanassa-Jin", "BioAndroid" => "Bio-Android",
		"SpiritDoll" => "Spirit Doll", "Halfbreed" => "Half-Saiyan",
		_ => s,
	};

	private static Rgb DeCor(Color c) => new((byte)(c.R * 255), (byte)(c.G * 255), (byte)(c.B * 255));

	// =====================================================================
	// REACAO AS ESCOLHAS
	// =====================================================================
	private void AoTrocarPlaneta(long idx)
	{
		string planeta = CharacterDraft.Planetas[Math.Clamp((int)idx, 0, CharacterDraft.Planetas.Length - 1)];
		_ficha.Planet = planeta;

		_raca.Clear();
		foreach (string r in CharacterDraft.RacasDoPlaneta(planeta)) _raca.AddItem(NomeBonito(r));
		if (_raca.ItemCount > 0) _raca.Selected = 0;
		AoTrocarRaca();
	}

	private void AoTrocarRaca()
	{
		string[] racas = CharacterDraft.RacasDoPlaneta(_ficha.Planet);
		int i = Math.Clamp(_raca.Selected, 0, Math.Max(racas.Length - 1, 0));
		_ficha.Race = racas.Length > 0 ? racas[i] : "";

		// linhagem so nas 3 racas em que o jogador escolhe de verdade
		string[] opcoes = CharacterDraft.EscolhasDeClasse(_ficha.Race);
		_linhaLinhagem.Visible = opcoes.Length > 0;
		_linhagem.Clear();
		foreach (string o in opcoes) _linhagem.AddItem(o);
		if (opcoes.Length > 0) _linhagem.Selected = 0;

		// cinco racas nao passam pelo passo de genero (o BYOND cravava Male)
		_linhaGenero.Visible = _ficha.TemGenero;

		AoTrocarGenero();
		_descricao.Text = Descrever(_ficha.Race);
	}

	/// <summary>Trocar de raca ou de sexo troca a lista de corpos -- e o que a tela oferece.</summary>
	private void AoTrocarGenero()
	{
		string genero = Genero();
		BodyOptions b = _cat?.CorposDe(_ficha.Race) ?? new BodyOptions();

		// quando a raca tem TONS (pele clara/morena/negra, verdes do Namekuseijin) o rotulo e
		// o tom; senao e o proprio corpo, que ai e escolha de silhueta
		List<string> rotulos = b.Tons.Count > 0
			? b.Tons
			: [.. b.Para(genero).Select(NomeDeArquivo)];

		_corpo.Clear();
		foreach (string r in rotulos) _corpo.AddItem(r);
		if (_corpo.ItemCount > 0) _corpo.Selected = 0;
		_visual.Corpo = 0;
		_visual.Tom = 0;
		_linhaCorpo.Visible = _corpo.ItemCount > 1;

		_linhaCorPele.Visible = b.CorLivre;
		if (!b.CorLivre) { _tingirPele.ButtonPressed = false; _visual.CorPele = null; }

		// cabelo: seis racas nao tem, e o Saiyajin nao colore (o sprite dele JA e preto,
		// com realce -- tingir de preto apagaria o desenho)
		bool temCabelo = VisualCatalog.TemCabelo(_ficha.Race);
		_blocoCabelo.Visible = temCabelo;
		_linhaCorCabelo.Visible = temCabelo && !VisualCatalog.CabeloNatural(_ficha.Race);
		if (!temCabelo) _visual.Cabelo = "Bald";
		if (VisualCatalog.CabeloNatural(_ficha.Race))
		{
			_tingirCabelo.ButtonPressed = false;
			_visual.CorCabelo = null;
		}

		Repintar();
	}

	private string Genero() => !_ficha.TemGenero ? "Male" : (_genero.Selected == 1 ? "Female" : "Male");

	/// <summary>A PREVIA AO VIVO: remonta as camadas do boneco com a ficha atual.</summary>
	private void Repintar()
	{
		RedesenharRoupa();
		if (_cat == null) return;
		_boneco.Vestir(_cat, _visual, _ficha.Race, Genero());
		_boneco.SetMotion(_facingAtual, false);
	}

	/// <summary>O que da pra dizer sem entregar a classe (que e sorteio cego).</summary>
	private string Descrever(string raca)
	{
		RaceProto? p = _racas?.Get(raca);
		if (p == null) return "";

		double bp = p.MiscStat("Starting BP");
		string classes = p.ClassSpread.Count > 0
			? $"Linhagens possiveis: {string.Join(", ", p.ClassSpread.Select(c => c.Classe))}."
			: "Sem variacao de linhagem.";
		string escolhe = CharacterDraft.EscolhasDeClasse(raca).Length > 0
			? " Voce escolhe a sua."
			: " A sua e revelada quando o combate mostrar do que voce e feito.";

		return $"Poder inicial base: {bp:0}. {classes}{escolhe}";
	}

	private void Confirmar()
	{
		_ficha.Name = _nome.Text.Trim();
		_ficha.Age = (int)_idade.Value;
		_ficha.Gender = Genero();
		_ficha.ChosenClass = _linhaLinhagem.Visible && _linhagem.Selected >= 0
			? _linhagem.GetItemText(_linhagem.Selected) : "";

		string erro = _ficha.Validar();
		if (erro.Length > 0) { _erro.Text = erro; return; }

		// sanear ANTES de mandar: o servidor vai rodar a mesma conferencia, e e melhor o
		// jogador ver o ajuste aqui do que descobrir depois que a roupa nao veio
		_cat?.Sanear(_visual, _ficha.Race, _ficha.Gender);

		Pronto?.Invoke(_ficha, _visual);
		QueueFree();
	}
}
