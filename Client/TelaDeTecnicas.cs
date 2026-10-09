using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// A MESA DE ATAQUES DE KI -- o `CreateAttackWindow` do DM (`customattacks.dm:605-1400`, uma janela
/// `.dmf` com trinta widgets nomeados a mao) mais o `Blast_Color()` do menu de Settings
/// (`CharacterCreation.dm:152-164`), que la moravam em dois lugares e aqui sao uma tela so.
///
/// ============================ O QUE ELA MOSTRA (dono, 2026-09-15) ============================
/// *"faca a tela de customizacao de ataques de ki (beam, blast etc)"*. Tres coisas, em duas colunas:
///
///   ESQUERDA -- o que se VE:
///     * a PREVIA VIVA do tiro: um `ProjetilDesenhado` de producao (a mesma classe que desenha o tiro
///       no mundo, sem luz), com a arte escolhida e a cor do ki de quem esta olhando. Nao e um quadro
///       parecido: e o trem de raio inteiro (cabeca, corpo, mao) ou a bola, animando;
///     * a GRADE DE ARTES em miniatura, recortada pelo tipo (`ArteDeProjetil.PermitidasPara`, o
///       `custom_icon_folders` do DM), cada miniatura tingida com a cor do ki -- o dono ja pediu uma
///       vez "lista com icone" no lugar de dropdown, e a lista antiga daqui era um dropdown de nomes
///       de arquivo ("Blasts: 12.dmi");
///     * a COR DO SEU KI: o seletor, "Aplicar" e "Sortear" (o `rand(0,255)` da criacao). Vale pra TODO
///       ataque de ki -- Ki Wave, bola, as inventadas -- porque e o `blastR/G/B` do mob, nao da tecnica.
///   DIREITA -- os NUMEROS: tipo, nome, gritos, as compras de ponto, o folego. Nada mudou neles.
///
/// ============================ ELA NAO SABE UM PRECO SEQUER ============================
/// Nenhum botao daqui calcula, confere ou desconta ponto. Cada um manda `ca_comprar &lt;compra&gt;`
/// e espera o pacote de volta; o numero de pontos que aparece na tela e o `Gasto` que o SERVIDOR
/// devolveu, calculado pelo `Core.Skills.TecnicaCustomizada`. A cor tambem: o seletor so TINGE a
/// previa ao vivo; a cor de verdade e a que volta no `PeerLook` depois do `ca_cor`.
/// ==================================================================================
///
/// ============================ UMA TELA, DOIS ESTADOS ============================
/// SEM MESA ABERTA: a lista das tecnicas de pe (com a miniatura de cada uma), "ajustar" e "esquecer" em
/// cada, e o botao de criar. COM MESA ABERTA: o rascunho. A troca de estado e o servidor quem decide (o
/// `bool` da mesa no pacote), e nao um clique local. A coluna da esquerda existe nos dois: a cor do ki
/// nao depende de haver tecnica inventada.
/// ============================================================================
/// </summary>
public partial class TelaDeTecnicas : CanvasLayer
{
	public static TelaDeTecnicas? Instancia { get; private set; }

	private Control _raiz = null!;
	private Label _titulo = null!;

	/// <summary>A coluna do que se VE: previa, grade de artes, cor do ki.</summary>
	private VBoxContainer _esquerda = null!;

	/// <summary>A coluna dos NUMEROS (ou a lista de tecnicas), rolavel.</summary>
	private VBoxContainer _corpo = null!;

	/// <summary>O campo de texto aberto agora (nome/descricao/grito), ou nulo.</summary>
	private LineEdit? _campo;

	/// <summary>
	/// ESTOU ESCREVENDO NUM CAMPO DESTA TELA? Lido pelo <see cref="Foco"/>, que e a pergunta unica
	/// que todo leitor de teclado do jogo faz antes de agir. ESTATICO como o `Chat.Digitando`.
	/// </summary>
	public static bool Digitando { get; private set; }

	/// <summary>A previa viva -- um `ProjetilDesenhado` de producao, sem luz, dentro de um palco recortado.</summary>
	private ProjetilDesenhado? _previa;

	private ColorPickerButton? _seletorDeCor;

	/// <summary>
	/// TODA MINIATURA QUE ESTA NA TELA -- as da grade e as da lista. Cada uma tem o proprio material (o
	/// estilo e de cada arte), entao mover o seletor de cor passa por todas e retinge uma a uma.
	/// </summary>
	private readonly List<AmostraDeKi> _amostras = [];

	private readonly List<Button> _botoesDeArte = [];

	// A COLUNA DA ESQUERDA TEM QUE CABER EM 720 DE ALTURA junto do titulo e do botao de fechar: 8 miniaturas
	// de 44 px por linha (Beams 9 + Techniques 15 = 4 linhas; Blasts 18 = 3) e o palco de 120. Com 6 de 52
	// px e palco de 150, a segunda foto da `--diagmesa` saiu com a cor do ki cortada no pe da tela.
	private const int LadoDaMiniatura = 44;
	private const int ColunasDaGrade = 8;
	private static readonly Vector2 Palco = new(340, 110);

	// =====================================================================
	// AS PORTAS DA BANCADA (`--diagmesa`)
	// =====================================================================
	/// <summary>
	/// O DEFEITO INJETADO: a grade sem o recorte por tipo -- toda arte do catalogo em qualquer tipo.
	/// **Falso em jogo, sempre.** Existe pra provar que a regra <see cref="GradeRespeitaOTipo"/> fica
	/// vermelha quando o filtro some, em vez de ficar verde olhando pra uma grade cheia de bolas com
	/// cauda de raio.
	/// </summary>
	public static bool GradeSemFiltroDeTeste;

	public bool Aberta => _raiz.Visible;
	public string ModoDeTeste => GameClient.Instance?.Mesa != null ? "mesa" : "lista";
	public ProjetilDesenhado? Previa => _previa;
	public void ForcarRedesenho() => Redesenhar();

	/// <summary>As artes que a grade oferece agora, na ordem em que estao na tela.</summary>
	public List<(ArteDeKi Arte, bool TemMiniatura, bool Marcada)> ArtesNaGrade() =>
		[.. _botoesDeArte.Select(b => ((ArteDeKi)b.GetMeta("arte").AsInt32(), b.GetMeta("mini").AsBool(), b.ButtonPressed))];

	/// <summary>Aperta a miniatura desta arte, pelo mesmo sinal que o dedo dispara.</summary>
	public bool ClicarArte(ArteDeKi arte)
	{
		Button? b = _botoesDeArte.Find(x => (ArteDeKi)x.GetMeta("arte").AsInt32() == arte);
		if (b == null) return false;
		b.EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}

	/// <summary>Aperta o botao com este texto (o primeiro visivel), pelo mesmo sinal que o dedo dispara.</summary>
	public bool ClicarBotao(string texto)
	{
		Button? b = Todos(_raiz).OfType<Button>().FirstOrDefault(x => x.Text == texto && x.IsVisibleInTree() && !x.Disabled);
		if (b == null) return false;
		b.EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}

	/// <summary>A cor no seletor. Escrever dispara o mesmo `ColorChanged` que arrastar o mouse dispara.</summary>
	public Color CorNoSeletor
	{
		get => _seletorDeCor?.Color ?? Aura.CorDoKiCru;
		set
		{
			if (_seletorDeCor == null) return;
			_seletorDeCor.Color = value;
			_seletorDeCor.EmitSignal(ColorPickerButton.SignalName.ColorChanged, value);
		}
	}

	/// <summary>
	/// A REGRA DA GRADE: toda arte oferecida (fora o "padrao", que e `Nenhuma`) e permitida pro tipo --
	/// o `custom_icon_folders` do DM. Pura, pra bancada injetar uma grade suja e ve-la reprovar.
	/// </summary>
	public static bool GradeRespeitaOTipo(IEnumerable<ArteDeKi> artes, TipoDeProjetil tipo)
	{
		HashSet<ArteDeKi> ok = [.. ArteDeProjetil.PermitidasPara(tipo)];
		return artes.All(a => a == ArteDeKi.Nenhuma || ok.Contains(a));
	}

	// =====================================================================
	// O CICLO DE VIDA
	// =====================================================================
	public override void _Ready()
	{
		Instancia = this;
		Layer = 4;   // a mesma da mochila e do menu de interacao: sao as telas "do mundo"
		Montar();

		if (GameClient.Instance is { } cli)
		{
			cli.CustomizadasMudaram += AoMudar;
			cli.PeerLooked += AoVerFicha;
		}
	}

	/// <summary>
	/// Solta as assinaturas. O `GameClient` sobrevive ao logout e esta tela nao -- ver o registro
	/// `dbclimax-port-assinaturas-vazadas`. Por isso os metodos tem NOME.
	/// </summary>
	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli)
		{
			cli.CustomizadasMudaram -= AoMudar;
			cli.PeerLooked -= AoVerFicha;
		}
		if (Instancia == this) Instancia = null;
	}

	private void AoMudar() { if (_raiz.Visible) Redesenhar(); }

	/// <summary>
	/// A MINHA FICHA CHEGOU DE NOVO -- e o que acontece depois do `ca_cor`: o servidor reapresenta a
	/// aparencia e o `World` ja escreveu a cor nova. Redesenhar aqui e o que faz a previa, as miniaturas
	/// e o seletor mostrarem a cor que VALE, e nao a que o mouse largou.
	/// </summary>
	private void AoVerFicha(int quem, string nome, string raca, string genero,
							Jandirus.Core.Appearance.Appearance ap, Jandirus.Core.Social.TipoDeFusao? fusao)
	{
		if (!_raiz.Visible || GameClient.Instance is not { } cli || quem != cli.LocalId) return;
		Redesenhar();
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

		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(940, 0) };
		caixa.AddThemeConstantOverride("separation", 8);
		painel.AddChild(caixa);

		_titulo = new Label { Text = "ATAQUES DE KI", HorizontalAlignment = HorizontalAlignment.Center };
		_titulo.AddThemeFontSizeOverride("font_size", 22);
		caixa.AddChild(_titulo);
		caixa.AddChild(new HSeparator());

		var colunas = new HBoxContainer();
		colunas.AddThemeConstantOverride("separation", 14);
		caixa.AddChild(colunas);

		// A ESQUERDA TEM LARGURA FIXA (380) e nao expande; a direita fica com o resto. E toda legenda das
		// duas colunas QUEBRA LINHA (`Nota`): a primeira foto da `--diagmesa` saiu com a previa de 1070 px
		// e os numeros fora da tela, porque uma `Label` sem autowrap alarga a coluna ate caber o texto.
		_esquerda = new VBoxContainer
		{
			CustomMinimumSize = new Vector2(380, 0),
			SizeFlagsHorizontal = Control.SizeFlags.Fill,
		};
		_esquerda.AddThemeConstantOverride("separation", 8);
		colunas.AddChild(_esquerda);

		// ROLAGEM SO NA DIREITA: dez tecnicas com duas linhas cada, ou a mesa com quinze compras, passam
		// da altura de uma tela de 720. A esquerda cabe sempre (previa + grade + cor = ~560 px).
		var rolagem = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(530, 560),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		colunas.AddChild(rolagem);

		_corpo = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_corpo.AddThemeConstantOverride("separation", 6);
		rolagem.AddChild(_corpo);

		caixa.AddChild(new HSeparator());
		var fechar = new Button { Text = "Fechar (Esc)" };
		fechar.Pressed += Fechar;
		caixa.AddChild(fechar);
	}

	public override void _UnhandledInput(InputEvent evento)
	{
		if (Foco.Digitando) return;
		if (!_raiz.Visible) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k) return;
		if (k.Keycode != Key.Escape) return;

		Fechar();
		GetViewport().SetInputAsHandled();
	}

	public void Abrir()
	{
		_raiz.Visible = true;
		// PEDE A LISTA AO ABRIR em vez de confiar na que chegou no login: entre o login e agora o
		// jogador pode ter esquecido uma tecnica em outra tela, e uma lista velha oferece "editar"
		// uma coisa que nao existe mais.
		GameClient.Instance?.SendVerbo("ca_listar");
		Redesenhar();
	}

	private void Fechar()
	{
		FecharCampo();
		_raiz.Visible = false;
	}

	// =====================================================================
	// O DESENHO
	// =====================================================================
	private void Redesenhar()
	{
		FecharCampo();
		foreach (Node n in _corpo.GetChildren()) n.QueueFree();
		foreach (Node n in _esquerda.GetChildren()) n.QueueFree();
		_botoesDeArte.Clear();
		_previa = null;
		_seletorDeCor = null;
		_amostras.Clear();

		GameClient? cli = GameClient.Instance;
		if (cli == null) return;

		if (cli.Mesa is { } mesa)
		{
			_titulo.Text = mesa.Criada ? $"AJUSTANDO — {mesa.Nome}" : "INVENTANDO UMA TÉCNICA";
			DesenharPrevia(mesa.Tipo, mesa.Arte, $"Assim sai {(mesa.Criada ? mesa.Nome : "a técnica")} da sua mão.");
			DesenharGradeDeArtes(mesa);
			DesenharCorDoKi();
			DesenharMesa(mesa);
			return;
		}

		_titulo.Text = "ATAQUES DE KI";
		DesenharPrevia(TipoDeProjetil.Beam, ArteDeKi.Nenhuma, "Seu ki, na arte padrão de raio (a do Ki Wave).");
		DesenharCorDoKi();
		DesenharLista(cli);
	}

	// ---------------------------------------------------------------- a previa
	/// <summary>
	/// A PREVIA VIVA. E um <see cref="ProjetilDesenhado"/> de PRODUCAO -- a mesma classe, o mesmo estilo
	/// e o mesmo shader que o mundo desenha --, so que sem luz e dentro de um palco recortado. Um desenho
	/// proprio da tela seria a "segunda resposta" pra "como e este tiro", e o dia em que o desenho do
	/// mundo mudasse a previa mentiria calada.
	/// </summary>
	private void DesenharPrevia(TipoDeProjetil tipo, ArteDeKi arte, string legenda)
	{
		_esquerda.AddChild(Tema.Rotulo("Prévia"));

		var moldura = new PanelContainer();
		moldura.AddThemeStyleboxOverride("panel", Tema.Caixa(new Color("0b0d14"), Tema.Borda, 4));
		_esquerda.AddChild(moldura);

		var palco = new Control { CustomMinimumSize = Palco, ClipContents = true };
		moldura.AddChild(palco);

		ArteDeKi efetiva = arte == ArteDeKi.Nenhuma ? ArteDeProjetil.PadraoDoCustom(tipo) : arte;
		// EM DOBRO, como a camera do jogo (o mundo roda com zoom 2): em 1x a bola padrao e um pingo num
		// palco de 340. A escala e do NODE, entao o raio continua medido em pixel de mundo por dentro --
		// so o desenho sai maior.
		// `SempreVoando`: a bola da vitrine nao anda, e sem isto ela sairia sem o rastro que tem em voo.
		var p = new ProjetilDesenhado
		{
			Tipo = tipo, Cor = MinhaCor(), SemLuz = true, SempreVoando = true, Scale = new Vector2(2, 2),
		};
		p.Vestir(efetiva, 1f);
		// O RAIO ATRAVESSA O PALCO da mao (esquerda) a cabeca (direita); a bola fica no meio. As duas
		// chamadas dao rumo LESTE ao node (`Mirar` tira o rumo da subtracao), que e o lado com que a
		// miniatura da grade tambem e desenhada. Os 140 px locais viram 280 na tela.
		if (tipo == TipoDeProjetil.Beam)
			p.Mirar(new Vector2(Palco.X - 24, Palco.Y / 2), new Vector2(Palco.X - 24 - 140, Palco.Y / 2));
		else
			p.Mirar(new Vector2(Palco.X / 2, Palco.Y / 2), new Vector2(Palco.X / 2 - 2, Palco.Y / 2));
		palco.AddChild(p);
		_previa = p;

		_esquerda.AddChild(Nota(legenda));
	}

	// ---------------------------------------------------------------- a grade de artes
	/// <summary>
	/// A GRADE. `pick_game_icon` (`customattacks.dm:543-568`) listava os `.dmi` da pasta por NOME; aqui
	/// cada arte vira uma miniatura VIVA (a ponta do raio ou a bola, desenhada pela <see cref="AmostraDeKi"/>
	/// com o mesmo shader do tiro).
	///
	/// ============================ ELA NAO CUSTA PONTO, E POR ISSO FICA LONGE DAS COMPRAS ============================
	/// O botao de icone do DM (`:1135-1148`) fica fora do orcamento de cinco pontos: ele nao toca o
	/// `custompoints_spent`. Desenha-la entre as compras diria ao jogador que ele esta gastando alguma
	/// coisa pra escolher uma arte -- por isso ela mora na coluna do que se VE.
	///
	/// A LISTA VEM DO `Core` E E RECORTADA PELO TIPO (`custom_icon_folders`, `:558-562`): raio ve
	/// `Beams` + `Techniques`, bola so `Blasts`, teleguiado so `Techniques`. E a MESMA funcao que o
	/// servidor usa pra recusar (`GameServer.ArteDaTecnica`); aqui ela so evita oferecer o que seria negado.
	/// ================================================================================================================
	/// </summary>
	private void DesenharGradeDeArtes(TecnicaCustomizada m)
	{
		_esquerda.AddChild(Tema.Rotulo("Arte do tiro (não custa ponto)"));

		IEnumerable<ArteDeKi> oferta = GradeSemFiltroDeTeste ? ArteDeProjetil.Todas : ArteDeProjetil.PermitidasPara(m.Tipo);
		List<ArteDeKi> artes = [.. oferta];

		// O PADRAO PRIMEIRO, sozinho na linha: e o `"Default"` do menu do DM (`:1146`, `attackicon = null`),
		// e e o que a tecnica usa quando o jogador nunca escolheu.
		var linhaDoPadrao = new HBoxContainer();
		linhaDoPadrao.AddThemeConstantOverride("separation", 8);
		_esquerda.AddChild(linhaDoPadrao);
		linhaDoPadrao.AddChild(BotaoDeArte(m.Tipo, ArteDeKi.Nenhuma, m.Arte == ArteDeKi.Nenhuma,
										   "Padrão — " + ArteDeKiNoCliente.Rotulo(ArteDeProjetil.PadraoDoCustom(m.Tipo))));
		linhaDoPadrao.AddChild(Nota("Padrão: a arte que a técnica usa se você não escolher nenhuma."));

		foreach (string pasta in new[] { "Beams", "Techniques", "Blasts" })
		{
			List<ArteDeKi> daPasta = [.. artes.Where(a => ArteDeProjetil.Folha(a).Pasta == pasta)];
			if (daPasta.Count == 0) continue;

			var cabeca = new Label { Text = pasta };
			cabeca.AddThemeFontSizeOverride("font_size", 11);
			cabeca.AddThemeColorOverride("font_color", Tema.TextoFraco);
			_esquerda.AddChild(cabeca);

			var grade = new GridContainer { Columns = ColunasDaGrade };
			grade.AddThemeConstantOverride("h_separation", 4);
			grade.AddThemeConstantOverride("v_separation", 4);
			_esquerda.AddChild(grade);
			foreach (ArteDeKi a in daPasta)
				grade.AddChild(BotaoDeArte(m.Tipo, a, m.Arte == a, ArteDeKiNoCliente.Rotulo(a)));
		}

		_esquerda.AddChild(Nota(
			"O feitio de cada arte do original + a cor do SEU ki por cima. Raio: Beams e Techniques; bola: Blasts; teleguiado: Techniques."));
	}

	/// <summary>
	/// UM BOTAO DA GRADE: a miniatura viva dentro de um botao de alternar. A miniatura e um node FILHO
	/// (e nao o `Icon` do botao) porque o material dela tem que valer so pra ela -- no botao inteiro, o
	/// shader pintaria tambem a moldura.
	/// </summary>
	private Button BotaoDeArte(TipoDeProjetil tipo, ArteDeKi arte, bool marcado, string dica)
	{
		ArteDeKi efetiva = arte == ArteDeKi.Nenhuma ? ArteDeProjetil.PadraoDoCustom(tipo) : arte;

		var b = new Button
		{
			CustomMinimumSize = new Vector2(LadoDaMiniatura, LadoDaMiniatura),
			TooltipText = dica,
			ToggleMode = true,
			ButtonPressed = marcado,
		};
		AmostraDeKi amostra = NovaAmostra(tipo, efetiva);
		amostra.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		amostra.OffsetLeft = 3; amostra.OffsetTop = 3; amostra.OffsetRight = -3; amostra.OffsetBottom = -3;
		b.AddChild(amostra);

		// SEM SHADER NAO HA MINIATURA: o nome no lugar, e nao um botao vazio -- o jogador ainda pode
		// escolher a arte. So acontece se o shader nao carregar.
		if (!amostra.Vestida) b.Text = dica.Length > 6 ? dica[..6] : dica;

		b.SetMeta("arte", (int)arte);
		b.SetMeta("mini", amostra.Vestida);
		// `ItemSelected` do dropdown antigo dava o INDICE da linha; aqui o botao ja sabe qual arte e,
		// entao o que vai pro fio e o id dela -- o que o servidor entende (`ca_arte`).
		b.Pressed += () => GameClient.Instance?.SendVerbo("ca_arte", ((int)arte).ToString());
		_botoesDeArte.Add(b);
		return b;
	}

	/// <summary>Uma miniatura desta arte, na cor do meu ki de agora, ja na lista das que o seletor de cor retinge.</summary>
	private AmostraDeKi NovaAmostra(TipoDeProjetil tipo, ArteDeKi arte)
	{
		var a = new AmostraDeKi { Tipo = tipo, Arte = arte, Cor = MinhaCor() };
		// VESTIDA JA, e nao no `_Ready`: o botao que a recebe ainda nao esta na arvore, e quem monta a
		// grade pergunta "tem miniatura?" antes de po-lo la.
		a.Vestir();
		_amostras.Add(a);
		return a;
	}

	// ---------------------------------------------------------------- a cor do ki
	/// <summary>
	/// A COR DO SEU KI -- `Blast_Color()` (`CharacterCreation.dm:152-164`), que no DM ficava escondido no
	/// menu de Settings ("Aura and Blast Color"). O seletor TINGE a previa e as miniaturas ao vivo;
	/// "Aplicar" manda `ca_cor R,G,B` e a cor que vale e a que volta no `PeerLook`. "Sortear" e o
	/// `rand(0,255)` por canal da criacao. A aura e OUTRA cor, sorteada, e nao muda aqui (decisao do dono).
	/// </summary>
	private void DesenharCorDoKi()
	{
		_esquerda.AddChild(new HSeparator());
		_esquerda.AddChild(Tema.Rotulo("Cor do seu ki"));

		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 6);
		_esquerda.AddChild(linha);

		Color atual = MinhaCor();
		var seletor = new ColorPickerButton
		{
			Color = atual,
			EditAlpha = false,
			CustomMinimumSize = new Vector2(150, 28),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		// AO VIVO SO NA TELA: nada vai pro fio enquanto o mouse arrasta. O redesenho tambem nao roda
		// (ele recriaria o seletor e fecharia o picker no meio do arrasto) -- so as cores dos shaders mudam.
		seletor.ColorChanged += c =>
		{
			_previa?.Tingir(c);
			foreach (AmostraDeKi a in _amostras) if (IsInstanceValid(a)) a.Tingir(c);
		};
		linha.AddChild(seletor);
		_seletorDeCor = seletor;

		var aplicar = new Button { Text = "Aplicar" };
		aplicar.Pressed += () =>
		{
			Color c = seletor.Color;
			GameClient.Instance?.SendVerbo("ca_cor",
				$"{Mathf.RoundToInt(c.R * 255)},{Mathf.RoundToInt(c.G * 255)},{Mathf.RoundToInt(c.B * 255)}");
		};
		linha.AddChild(aplicar);

		var sortear = new Button { Text = "Sortear" };
		sortear.Pressed += () => GameClient.Instance?.SendVerbo("ca_cor", "sortear");
		linha.AddChild(sortear);

		_esquerda.AddChild(Nota(
			"Vale pra TODO ataque de ki que sai da sua mão (Ki Wave, bolas, as inventadas). "
			+ "Não muda carregando, voando ou escudado."));
	}

	/// <summary>
	/// UMA LEGENDA QUE QUEBRA LINHA. Uma `Label` sem autowrap tem largura minima = o texto inteiro, e uma
	/// frase de duas linhas alargava a coluna ate 1070 px (a primeira foto da `--diagmesa`: a previa
	/// enorme e os numeros fora da tela). Toda legenda longa desta tela passa por aqui.
	/// </summary>
	private static Label Nota(string texto, Color? cor = null, int tamanho = 11)
	{
		Label l = Tema.Legenda(texto, cor ?? Tema.TextoFraco, tamanho);
		l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		return l;
	}

	/// <summary>A cor do MEU ki, como o mundo a desenha agora (`World.CorDoKiDe`). Sem mundo, o ki cru.</summary>
	private static Color MinhaCor() =>
		GameClient.Instance is { } cli && World.Instancia is { } mundo ? mundo.CorDoKiDe(cli.LocalId) : Aura.CorDoKiCru;

	// ---------------------------------------------------------------- a lista
	private void DesenharLista(GameClient cli)
	{
		_corpo.AddChild(Nota(
			$"{cli.Customizadas.Count} de {TecnicaCustomizada.Maximo} técnicas inventadas.",
			Tema.TextoFraco));

		foreach (TecnicaCustomizada t in cli.Customizadas)
		{
			PanelContainer p = Tema.Painel1(8);
			var linha = new HBoxContainer();
			linha.AddThemeConstantOverride("separation", 10);
			p.AddChild(linha);

			// A MINIATURA DA TECNICA ao lado do nome: a mesma da grade, na cor do ki de agora.
			ArteDeKi efetiva = t.Arte == ArteDeKi.Nenhuma ? ArteDeProjetil.PadraoDoCustom(t.Tipo) : t.Arte;
			AmostraDeKi quadro = NovaAmostra(t.Tipo, efetiva);
			quadro.CustomMinimumSize = new Vector2(LadoDaMiniatura, LadoDaMiniatura);
			linha.AddChild(quadro);

			var coluna = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			linha.AddChild(coluna);

			var nome = new Label { Text = $"{t.Nome}  —  {NomeDoTipo(t.Tipo)}" };
			nome.AddThemeColorOverride("font_color", Tema.Destaque);
			coluna.AddChild(nome);
			coluna.AddChild(Nota(Resumo(t), Tema.TextoFraco, 12));

			var botoes = new HBoxContainer();
			coluna.AddChild(botoes);

			int id = t.Id;
			var editar = new Button { Text = "Ajustar" };
			editar.Pressed += () => GameClient.Instance?.SendVerbo("ca_editar", id.ToString());
			botoes.AddChild(editar);

			// ESQUECER PEDE UM SEGUNDO CLIQUE, e o alerta do DM diz por que: *"This decision is
			// irreversable!"*. O botao troca de texto em vez de abrir uma caixa -- a confirmacao
			// mora onde o dedo ja esta, e um "tem certeza?" que aparece embaixo do cursor e um
			// "tem certeza?" que se clica sem ler.
			var esquecer = new Button { Text = "Esquecer" };
			bool armado = false;
			esquecer.Pressed += () =>
			{
				if (!armado)
				{
					armado = true;
					esquecer.Text = "Esquecer — tem certeza?";
					esquecer.AddThemeColorOverride("font_color", Tema.Perigo);
					return;
				}
				GameClient.Instance?.SendVerbo("ca_esquecer", id.ToString());
			};
			botoes.AddChild(esquecer);

			_corpo.AddChild(p);
		}

		bool cabe = cli.Customizadas.Count < TecnicaCustomizada.Maximo;
		var criar = new Button { Text = "Inventar uma técnica nova", Disabled = !cabe };
		criar.Pressed += () => GameClient.Instance?.SendVerbo("ca_criar");
		_corpo.AddChild(criar);

		// O TETO DIZ POR QUE, e nao so apaga o botao. Um botao cinza sem explicacao e a mesma coisa
		// que o `switch` sem `else` do DM: o jogador nao descobre que ja tem dez.
		if (!cabe)
			_corpo.AddChild(Nota(
				$"Cabem {TecnicaCustomizada.Maximo} técnicas na sua cabeça. Esqueça uma para abrir espaço.",
				Tema.Perigo, 12));
	}

	// ---------------------------------------------------------------- a mesa (os numeros)
	private void DesenharMesa(TecnicaCustomizada m)
	{
		// OS PONTOS PRIMEIRO, e grandes: e o unico numero que muda a cada clique, e e por ele que o
		// jogador decide o proximo.
		var pontos = new Label
		{
			Text = $"{m.Restantes} pontos livres",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		pontos.AddThemeFontSizeOverride("font_size", 18);
		pontos.AddThemeColorOverride("font_color", m.Restantes >= 0 ? Tema.Destaque : Tema.Perigo);
		_corpo.AddChild(pontos);
		_corpo.AddChild(Nota(
			$"O orçamento é {TecnicaCustomizada.PontosTotais}, e ele é um TETO. Rebaixar potência, encarecer a "
			+ "energia, alongar a carga ou deixar o tiro mais lento DEVOLVE pontos — mas só devolve o que já foi "
			+ $"gasto: não dá para juntar mais de {TecnicaCustomizada.PontosTotais}.",
			Tema.TextoFraco, 11));

		// ============================ O PISO TEM QUE APARECER ANTES DO CLIQUE ============================
		// Com o orcamento intacto toda desvantagem e RECUSADA (nao ha o que estornar), e uma recusa que so
		// chega DEPOIS do clique, pelo chat, e uma regra que o jogador aprende por tentativa e erro. Esta
		// linha e a mesma regra dita antes -- e e a UNICA coisa que esta tela deduz sobre pontos: nao e um
		// preco, e o zero.
		// ============================================================================================
		if (m.Gasto == 0)
			_corpo.AddChild(Nota(
				"Você ainda não gastou nada. Desvantagens (menos potência, energia mais cara, carga mais longa, "
				+ "tiro mais lento, gastar fôlego) pagam com pontos de volta — e com o orçamento inteiro não há o "
				+ "que devolver, então elas são RECUSADAS em vez de sair de graça. Compre alguma vantagem primeiro.",
				Tema.Perigo, 11));

		// ---------------------------------------------------------------- tipo
		if (!m.Criada)
		{
			_corpo.AddChild(new HSeparator());
			_corpo.AddChild(Tema.Rotulo("Tipo (só se escolhe uma vez)"));
			var tipos = new HBoxContainer();
			foreach ((TipoDeProjetil t, string rotulo) in new[]
			{
				(TipoDeProjetil.Beam, "Raio"),
				(TipoDeProjetil.Blast, "Bola"),
				(TipoDeProjetil.Guided, "Bola teleguiada"),
			})
			{
				TipoDeProjetil alvo = t;
				var b = new Button { Text = rotulo, Disabled = m.Tipo == t };
				b.Pressed += () => GameClient.Instance?.SendVerbo("ca_tipo", alvo.ToString().ToLowerInvariant());
				tipos.AddChild(b);
			}
			_corpo.AddChild(tipos);
			_corpo.AddChild(Nota(DescricaoDoTipo(m.Tipo), Tema.TextoFraco, 11));
		}
		else
		{
			_corpo.AddChild(Nota($"Tipo: {NomeDoTipo(m.Tipo)} (não muda depois de pronta).",
										 Tema.TextoFraco, 12));
		}

		// O RAIO TELEGUIADO (dono, 2026-10-09), JUNTO DO TIPO: e ali que se procura "este ataque e teleguiado?" -- a
		// bola teleguiada e um dos tres botoes de cima. La embaixo, com as outras compras de raio, ele caia fora da
		// tela numa janela de 720. A regra inteira vai escrita porque e ela que o jogador esta comprando: todo ataque
		// ja sai NO marcado; o que este botao vende e a curva depois. E compra, nao tipo: muda tambem depois de pronta.
		if (m.Tipo == TipoDeProjetil.Beam)
		{
			Interruptor($"Teleguiado: o raio acompanha quem você marcou ({TecnicaCustomizada.PrecoDoTeleguiado} pontos)",
						m.Teleguiado, null,
						() => GameClient.Instance?.SendVerbo("ca_comprar",
							m.Teleguiado ? nameof(Compra.TeleguiadoDesligar) : nameof(Compra.TeleguiadoLigar)));
			_corpo.AddChild(Nota(
				"Todo ataque de ki já sai na direção de quem está marcado — e depois NÃO faz curva. O teleguiado "
				+ $"gira de leve atrás do alvo, e só até {Teleguiado.DesvioMaximoEmGraus:0}° do rumo em que saiu: "
				+ "quem sai muito da frente escapa. Sem ninguém marcado, ele sai reto.",
				Tema.TextoFraco, 11));
		}

		// ---------------------------------------------------------------- textos
		_corpo.AddChild(new HSeparator());
		Texto("Nome", m.Nome, "nome");
		Texto("Descrição", m.Desc, "desc");

		Interruptor($"Gritar ao disparar: \"{m.Grito}\"", m.DizGrito, "grito");
		if (m.DizGrito) Texto("Grito do disparo", m.Grito, "grito");
		if (m.Tipo == TipoDeProjetil.Beam)
		{
			Interruptor($"Gritar ao começar a carregar: \"{m.GritoDeCarga}\"", m.DizGritoDeCarga, "gritocarga");
			if (m.DizGritoDeCarga) Texto("Grito da carga", m.GritoDeCarga, "gritocarga");
		}

		// ---------------------------------------------------------------- as compras
		_corpo.AddChild(new HSeparator());
		Degrau("Potência", $"{m.BaseDano:0.0}", Compra.DanoMais, Compra.DanoMenos,
			   "Multiplica o dano inteiro. +0,1 por ponto.");
		Degrau("Custo de energia", $"{m.CustoKi:0}", Compra.KiMenos, Compra.KiMais,
			   "Baratear custa 1 ponto; encarecer devolve 1. Passo de 40, mínimo 20 — o padrão já é o mínimo.");
		Degrau("Velocidade", $"{m.Velocidade:0.#}", Compra.VelocidadeMais, Compra.VelocidadeMenos,
			   "De 1 pra cima anda de 1 em 1 até 5; de 1 pra baixo, de 0,2 em 0,2 até 0,2.");

		if (m.Tipo == TipoDeProjetil.Beam)
		{
			Degrau("Tempo de carga", $"{m.CargaMinima:0.#}s", Compra.CargaMenos, Compra.CargaMais,
				   "Encurtar custa 1 ponto; alongar devolve 1. Passo de 0,4s, mínimo 0,2s.");

			Interruptor($"Sai sozinho quando termina de carregar ({TecnicaCustomizada.PrecoDoInstantaneo} pontos)",
						m.Instantaneo, null,
						() => GameClient.Instance?.SendVerbo("ca_comprar",
							m.Instantaneo ? nameof(Compra.InstantaneoDesligar) : nameof(Compra.InstantaneoLigar)));

			Numero($"Alcance: {m.Alcance:0} tiles", (int)m.Alcance, TecnicaCustomizada.AlcancePiso, 60,
				   "1 ponto por tile, nos dois sentidos. Mínimo 5, padrão 20.",
				   v => GameClient.Instance?.SendVerbo("ca_comprar", $"{nameof(Compra.Alcance)}/{v}"));

			Numero($"Força pela distância: {m.DistanciaMod:0.0}× por tile",
				   (int)Math.Round(m.DistanciaMod * 10), (int)(TecnicaCustomizada.DistModPiso * 10), 20,
				   "Abaixo de 1,0 o raio morre andando; acima, engrossa. UM ponto a cada 0,1 — "
				   + "o texto do jogo antigo dizia 2, mas a conta dele sempre foi essa. (valor × 10)",
				   v => GameClient.Instance?.SendVerbo("ca_comprar",
						$"{nameof(Compra.DistanciaMod)}/{(v / 10.0).ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
		}

		// ---------------------------------------------------------------- folego
		_corpo.AddChild(new HSeparator());
		// O UNICO ESTORNO DE DOIS PONTOS: ele exige DOIS ja gastos, e nao um.
		Interruptor($"Gasta fôlego além da energia (devolve {TecnicaCustomizada.PrecoDaEstamina} pontos — "
					+ $"precisa ter {TecnicaCustomizada.PrecoDaEstamina} gastos)",
					m.UsaStamina, null,
					() => GameClient.Instance?.SendVerbo("ca_comprar",
						m.UsaStamina ? nameof(Compra.StaminaDesligar) : nameof(Compra.StaminaLigar)));
		if (m.UsaStamina)
			Degrau("Fôlego gasto", $"{m.CustoStamina:0}", Compra.StaminaMenos, Compra.StaminaMais,
				   "Cada ponto de fôlego a mais devolve 1 ponto de criação. Mínimo 1.");

		// ---------------------------------------------------------------- fechar
		_corpo.AddChild(new HSeparator());
		var acoes = new HBoxContainer();
		_corpo.AddChild(acoes);

		// SALVAR SO COM O ORCAMENTO FECHADO. Desde o piso do dono, `Gasto` vive preso em 0..5 no
		// `Core` -- entao `Restantes` negativo aqui e um estado que NAO EXISTE mais nem por dentro. O
		// botao apagado cobre um pacote adulterado que tenha escapado do grampo do `CustomWire`.
		var salvar = new Button { Text = m.Criada ? "Confirmar mudanças" : "Criar a técnica",
								  Disabled = m.Restantes < 0 };
		salvar.Pressed += () => GameClient.Instance?.SendVerbo("ca_salvar");
		acoes.AddChild(salvar);

		var cancelar = new Button { Text = "Descartar" };
		cancelar.Pressed += () => GameClient.Instance?.SendVerbo("ca_cancelar");
		acoes.AddChild(cancelar);
	}

	// =====================================================================
	// OS WIDGETS
	// =====================================================================
	/// <summary>Um par de botoes "mais / menos" em volta de um valor. O `+`/`-` do painel do DM.</summary>
	private void Degrau(string rotulo, string valor, Compra sobe, Compra desce, string dica)
	{
		var linha = new HBoxContainer { TooltipText = dica };
		_corpo.AddChild(linha);

		var nome = new Label { Text = rotulo, CustomMinimumSize = new Vector2(190, 0) };
		linha.AddChild(nome);

		var menos = new Button { Text = "−", CustomMinimumSize = new Vector2(34, 0) };
		menos.Pressed += () => GameClient.Instance?.SendVerbo("ca_comprar", desce.ToString());
		linha.AddChild(menos);

		var v = new Label
		{
			Text = valor,
			CustomMinimumSize = new Vector2(70, 0),
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		v.AddThemeColorOverride("font_color", Tema.Destaque);
		linha.AddChild(v);

		var mais = new Button { Text = "+", CustomMinimumSize = new Vector2(34, 0) };
		mais.Pressed += () => GameClient.Instance?.SendVerbo("ca_comprar", sobe.ToString());
		linha.AddChild(mais);

		linha.AddChild(Nota(dica, Tema.TextoFraco, 11));
	}

	/// <summary>Uma caixinha de liga/desliga. `acao` nula = alternar um grito (que e livre).</summary>
	private void Interruptor(string rotulo, bool ligado, string? grito, Action? acao = null)
	{
		var b = new CheckBox { Text = rotulo, ButtonPressed = ligado };
		b.Pressed += () =>
		{
			if (acao != null) { acao(); return; }
			GameClient.Instance?.SendVerbo("ca_grito", grito ?? "");
		};
		_corpo.AddChild(b);
	}

	/// <summary>
	/// UM CAMPO DE TEXTO que so manda ao confirmar (Enter ou perder o foco). Nao manda a cada letra:
	/// cada verbo e uma mensagem confiavel, e o redesenho recria o campo, o que arrancaria o cursor da
	/// mao do jogador na segunda letra.
	/// </summary>
	private void Texto(string rotulo, string valor, string campo)
	{
		var linha = new HBoxContainer();
		_corpo.AddChild(linha);
		linha.AddChild(new Label { Text = rotulo, CustomMinimumSize = new Vector2(190, 0) });

		var e = new LineEdit
		{
			Text = valor,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MaxLength = campo == "desc" ? Jandirus.Net.CustomWire.MaxDesc : Jandirus.Net.CustomWire.MaxGrito,
		};
		// `Foco.Digitando` E O QUE IMPEDE AS TECLAS DO JOGO DE DISPARAREM enquanto se escreve. Sem
		// ele, escrever "Kamehameha" manda o personagem meditar (M) e carregar Ki (C) no meio da
		// palavra -- e a tecla Esc fecharia a tela em vez de sair do campo.
		e.FocusEntered += () => Digitando = true;
		e.FocusExited += () => { Digitando = false; Enviar(); };
		e.TextSubmitted += _ => Enviar();
		linha.AddChild(e);
		_campo = e;

		void Enviar()
		{
			if (e.Text == valor) return;   // nada mudou: nao gasta pacote nem redesenho
			GameClient.Instance?.SendVerbo("ca_texto", $"{campo}/{e.Text}");
		}
	}

	/// <summary>Um valor que se digita (alcance, modificador de distancia) -- o `input ... as num` do DM.</summary>
	private void Numero(string rotulo, int valor, int minimo, int maximo, string dica, Action<int> aplicar)
	{
		var linha = new HBoxContainer { TooltipText = dica };
		_corpo.AddChild(linha);
		linha.AddChild(new Label { Text = rotulo, CustomMinimumSize = new Vector2(230, 0) });

		var s = new SpinBox { MinValue = minimo, MaxValue = maximo, Value = valor, Step = 1 };
		// SO NO `value_changed` DO USUARIO: a `SpinBox` dispara o sinal ao ser construida com um
		// valor fora da faixa, e isso mandaria uma compra que ninguem pediu no meio do desenho.
		s.ValueChanged += v => { if ((int)v != valor) aplicar((int)v); };
		linha.AddChild(s);

		_corpo.AddChild(Nota(dica, Tema.TextoFraco, 11));
	}

	private void FecharCampo()
	{
		if (_campo == null) return;
		// SOLTA O `Digitando` NA MARRA. O `FocusExited` nao dispara quando o no e destruido no
		// redesenho, e um `Digitando` preso deixa o jogo inteiro sem teclado ate o proximo campo.
		Digitando = false;
		_campo = null;
	}

	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node n in raiz.GetChildren())
		{
			yield return n;
			foreach (Node m in Todos(n)) yield return m;
		}
	}

	private static string NomeDoTipo(TipoDeProjetil t) => t switch
	{
		TipoDeProjetil.Beam => "raio canalizado",
		TipoDeProjetil.Blast => "bola solta",
		_ => "esfera teleguiada",
	};

	private static string DescricaoDoTipo(TipoDeProjetil t) => t switch
	{
		TipoDeProjetil.Beam =>
			"Carrega antes de sair e prende você no lugar enquanto dura — em troca, encosta e mói. "
			+ "É o único tipo com alcance e força-pela-distância ajustáveis.",
		TipoDeProjetil.Blast =>
			"Sai na hora, não prende você, e morre em quem acertar. É o tiro de todo dia.",
		_ =>
			"Sai na hora e faz curva atrás do alvo marcado — de leve, e só até "
			+ $"{Teleguiado.DesvioMaximoEmGraus:0}° do rumo em que saiu. Quem sai muito da frente escapa.",
	};

	private static string Resumo(TecnicaCustomizada t)
	{
		string s = $"potência {t.BaseDano:0.#} · energia {t.CustoKi:0} · velocidade {t.Velocidade:0.#}"
				 + $" · alcance {t.Alcance:0}";
		if (t.Tipo == TipoDeProjetil.Beam)
		{
			s += $" · carga {t.CargaMinima:0.#}s";
			if (t.Instantaneo) s += " · sai sozinho";
			if (t.Teleguiado) s += " · teleguiado";
			if (Math.Abs(t.DistanciaMod - 1) > 1e-6) s += $" · {t.DistanciaMod:0.0}×/tile";
		}
		if (t.UsaStamina) s += $" · {t.CustoStamina:0} de fôlego";
		return s;
	}
}
