using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O MENU DO JOGO (tecla P). E o painel de abas do BYOND, portado.
///
/// A LISTA DE ABAS NAO E INVENTADA: sai literal de `BuildStatsHTML()` em
/// Code/Modules/User Interface/HtmlUI.dm:137 --
///
///     Stats, Items, Equip, Body, Forms, Ki, People, World, Skills, Other, Learning, Tech
///
/// mais tres que APARECEM E SOMEM conforme o personagem, e essa parte e a que importa:
///
///   * "Sense" so existe depois de aprender a skill (`register_html_tab("Sense")`);
///   * com o SCOUTER ligado ela nao ganha uma aba nova -- ela VIRA "Scan", e o BP passa a ser
///     lido em numero exato em vez de "???";
///   * "Nav" so existe com nav system a bordo;
///   * "Admin" so pra quem e.
///
/// Quem decide isso e o SERVIDOR, no campo <see cref="Protocol.Poder"/> da ficha lenta. O
/// cliente nunca decide sozinho que sabe uma habilidade -- fosse assim, bastaria mexer no
/// cliente pra ganhar a aba de Scan e ler o BP alheio.
///
/// A BUSCA vale pro jogo inteiro, nao pra aba: digitar filtra os verbs de TODAS as categorias.
/// Ninguem lembra em que aba mora a tecnica que quer usar.
/// </summary>
public partial class MenuJogo : CanvasLayer
{
	public static MenuJogo? Instancia { get; private set; }

	/// <summary>O menu esta aberto? O jogo pergunta antes de andar, como faz com o chat.</summary>
	public static bool Aberto { get; private set; }

	/// <summary>
	/// ALGUM CAMPO DE TEXTO DESTE MENU ESTA COM O FOCO? Com todos soltos da pra continuar andando e
	/// lutando enquanto se olha a ficha, que e como o painel do BYOND funcionava.
	///
	/// ============================ ERA SO A BUSCA, E ISSO ERA UM BURACO ============================
	/// A pergunta era `m._busca.HasFocus()` -- e o menu tem OUTROS CINCO `LineEdit`: o aviso pro
	/// servidor, a conta/personagem de promover e banir, o campo do alvo (que aceita 248 caracteres)
	/// e o do painel de admin. Digitando o nome de quem ia ser banido, o personagem ANDAVA (WASD),
	/// treinava (T) e trocava a mira (1-5) -- ja era assim antes das teclas configuraveis, so que
	/// ninguem tinha ligado nada as letras do meio do alfabeto.
	///
	/// Com tecla ligavel em QUALQUER tecla isso deixa de ser esquisito e vira grave: cada letra do
	/// nome digitado viraria uma tecnica disparada. Por isso a pergunta virou "algum campo MEU tem o
	/// foco" em vez de nomear um campo -- assim o sexto `LineEdit` que nascer neste arquivo ja
	/// nasce coberto, que e o defeito que este projeto mais repetiu.
	/// ========================================================================================
	/// </summary>
	public static bool Digitando =>
		Instancia is { Visible: true } m
		&& m.GetViewport()?.GuiGetFocusOwner() is LineEdit campo
		&& m.IsAncestorOf(campo);

	/// <summary>
	/// As abas fixas, na ordem do original.
	///
	/// "ITEMS" SAIU. Ela era um aviso de "vem com o sistema de itens" -- e o sistema chegou: a
	/// mochila e a tecla I, com grade, pilha e as acoes de cada item. Deixar a aba seria oferecer
	/// duas portas pra mesma coisa, e uma delas nao abre.
	/// </summary>
	private static readonly string[] Fixas =
	[
		"Stats", "Equip", "Body", "Forms", "Ki",
		"People", "World", "Cargos", "Skills", "Other", "Learning", "Tech",
	];

	private Control _raiz = null!;
	private PanelContainer _painel = null!;
	private HBoxContainer _barraAbas = null!;
	private LineEdit _busca = null!;
	private VBoxContainer _conteudo = null!;
	private Label _titulo = null!;

	private string _aba = "Stats";
	private Protocol.AtributosState _atributos;

	// A ARVORE ABERTA, O CATALOGO E O LIVRO moram em `MenuJogo.Skills.cs`, junto de toda a aba de
	// aprendizado (Learning), da aba Skills e da ficha de compra.

	public override void _Ready()
	{
		Instancia = this;
		Layer = 3;              // acima do chat (2), abaixo do menu de pause (20)
		Montar();
		Visible = false;

		// ============================ METODOS NOMEADOS, E NAO LAMBDAS ============================
		// Estas nove assinaturas eram lambdas anonimas, e por isso NAO TINHAM COMO ser canceladas --
		// `-=` precisa do mesmo delegate, e uma lambda nova nunca e igual a anterior.
		//
		// O `GameClient` SOBREVIVE ao logout (o `VoltarAoLogin` derruba os filhos do Boot, e ele nao
		// e um deles), entao cada volta ao menu deixava nove assinantes apontando pra um node ja
		// liberado. No login seguinte o pacote de ficha -- que chega a 5 Hz -- acordava os mortos, e
		// cada um estourava `ObjectDisposedException` dentro do `Handle`, virando um
		// "[client] pacote invalido" a cada 200 ms. Duas idas ao menu, dezoito erros por segundo.
		//
		// O `World` sempre fez certo (metodos nomeados + `-=` no `_ExitTree`); esta classe e as
		// outras telas e que nasceram fora do padrao.
		// =========================================================================================
		if (GameClient.Instance is { } cli)
		{
			cli.SheetUpdated += AoFicha;
			cli.AtributosRecebidos += AoAtributos;
			cli.CorpoAtualizado += AoCorpo;
			cli.SkillsMudaram += AoSkills;
			cli.CargosMudaram += AoCargos;
			cli.ConhecidosMudaram += AoConhecidos;
			cli.ContasMudaram += AoContas;
			cli.OnlineMudou += AoOnline;
			cli.LimpezaMudou += AoLimpeza;
			cli.TechMudou += AoTech;
			cli.EstilosMudaram += AoEstilos;
			cli.ChefesVistosMudaram += AoChefesVistos;
			cli.CustomizadasMudaram += AoCustomizadas;
			cli.ObrasMudaram += AoObras;
			_atributos = cli.Atributos;
			SincronizarLivro();
		}

		// O `Verbos.Mudou` E ESTATICO -- ele vive o processo inteiro. Um assinante morto aqui nao
		// some nem quando o mundo inteiro e derrubado.
		Verbos.Mudou += AoMudarVerbos;
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli)
		{
			cli.SheetUpdated -= AoFicha;
			cli.AtributosRecebidos -= AoAtributos;
			cli.CorpoAtualizado -= AoCorpo;
			cli.SkillsMudaram -= AoSkills;
			cli.CargosMudaram -= AoCargos;
			cli.ConhecidosMudaram -= AoConhecidos;
			cli.ContasMudaram -= AoContas;
			cli.OnlineMudou -= AoOnline;
			cli.LimpezaMudou -= AoLimpeza;
			cli.TechMudou -= AoTech;
			cli.EstilosMudaram -= AoEstilos;
			cli.ChefesVistosMudaram -= AoChefesVistos;
			cli.CustomizadasMudaram -= AoCustomizadas;
			cli.ObrasMudaram -= AoObras;
		}
		Verbos.Mudou -= AoMudarVerbos;

		if (Instancia == this) Instancia = null;
		Aberto = false;
	}

	// =====================================================================
	// O QUE O SERVIDOR MANDA
	// =====================================================================
	private void AoFicha(SheetState _) { if (Visible) Redesenhar(); }

	private void AoAtributos(Protocol.AtributosState a)
	{
		bool trocouRaca = _atributos.Raca != a.Raca;
		_atributos = a;
		// A BUSCA TAMBEM PRECISA SABER. Ela varre todas as categorias de proposito, entao
		// esconder a aba nao bastava pra esconder os verbs de admin de quem nao e admin.
		Verbos.DefinirAdmin(a.Tem(Protocol.Poder.Admin));
		// a RACA so chega na ficha lenta, e e ela que diz quais habilidades existem
		if (trocouRaca) Habilidades.Montar(a.Raca ?? "");
		if (Visible) Redesenhar();
	}

	private void AoCorpo(List<Protocol.ParteState> _) { if (Visible && _aba == "Body") Redesenhar(); }

	private void AoSkills()
	{
		SincronizarLivro();
		// APRENDER UMA SKILL PODE CRIAR UM BOTAO. Refaz a lista inteira em vez de
		// acrescentar so o novo: e a mesma funcao do login, entao nao ha um segundo
		// caminho que possa divergir do primeiro.
		Habilidades.Montar(_atributos.Raca ?? "");
		if (Visible) Redesenhar();
	}

	private void AoCargos() { if (Visible) Redesenhar(); }

	/// <summary>
	/// A LISTA DE CONHECIDOS MUDOU. So redesenha na aba People, como o `AoContas` faz com a de
	/// admin: um pedido de amizade chegando no meio de uma luta nao pode remontar a aba de Stats
	/// que o jogador esta lendo.
	/// </summary>
	private void AoConhecidos() { if (Visible && _aba == "People") Redesenhar(); }
	private void AoContas() { if (Visible && _aba == Verbos.Admin) Redesenhar(); }
	private void AoOnline() { if (Visible && _aba == Verbos.Admin) Redesenhar(); }

	/// <summary>
	/// A PREVIA DA LIMPEZA CHEGOU (ou foi consumida). Mesma regra do `AoContas`: so remonta na aba
	/// de admin. E o campo do codigo e ZERADO aqui, e nao no desenho -- previa nova, codigo novo;
	/// deixar o que estava digitado seria oferecer um codigo velho pra confirmar uma lista nova.
	/// </summary>
	private void AoLimpeza()
	{
		_codigoDaLimpeza = "";
		if (Visible && _aba == Verbos.Admin) Redesenhar();
	}
	private void AoTech() { if (Visible) Redesenhar(); }

	private void AoEstilos()
	{
		Habilidades.Montar(_atributos.Raca ?? "");
		if (Visible) Redesenhar();
	}

	/// <summary>
	/// VI UM CHEFE NOVO -- e cada um deles e um botao ("enfrentar na mente"). Remonta a lista
	/// inteira, pelo mesmo argumento do <see cref="AoCustomizadas"/>.
	/// </summary>
	private void AoChefesVistos()
	{
		Habilidades.Montar(_atributos.Raca ?? "");
		if (Visible) Redesenhar();
	}

	/// <summary>
	/// UMA TECNICA INVENTADA NASCEU, MUDOU DE NOME OU MORREU -- e cada uma delas e um BOTAO.
	///
	/// Remonta a lista inteira, como o `AoSkills` e o `AoEstilos`: renomear a tecnica 3 deixaria o
	/// botao velho de pe (o `Verbos.Registrar` deduplica por NOME, entao o novo entraria ao lado do
	/// antigo em vez de substitui-lo), e o antigo continuaria mandando o mesmo verbo -- dois botoes
	/// com nomes diferentes pra o mesmo tiro.
	/// </summary>
	private void AoCustomizadas()
	{
		Habilidades.Montar(_atributos.Raca ?? "");
		if (Visible) Redesenhar();
	}

	private void AoObras() { if (Visible && _aba == "Tech") Redesenhar(); }

	/// <summary>
	/// O CATALOGO DE VERBS MUDOU -- e ele muda EM RAJADA: `Habilidades.Montar` chama `Verbos.Limpar()` e
	/// re-registra o catalogo inteiro, e o `Mudou` dispara uma vez por verb. Isto era uma dezena de
	/// `Redesenhar()` num tique so, e ficou visivel no dia em que a aba de aprendizado passou a assinar
	/// os proprios verbs: a lista cresce de volta um a um, a assinatura muda a cada passo, e a pagina
	/// era remontada 17 vezes por pacote de skills -- com o botao que o jogador ia clicar morrendo a
	/// cada uma. Um redesenho ADIADO pro fim do quadro ve o catalogo ja inteiro: uma comparacao de
	/// assinatura, e nenhuma remontagem quando ele voltou igual.
	/// </summary>
	private bool _redesenhoAdiado;

	private void AoMudarVerbos()
	{
		if (!Visible || _redesenhoAdiado) return;
		_redesenhoAdiado = true;
		Callable.From(() => { _redesenhoAdiado = false; if (Visible) Redesenhar(); }).CallDeferred();
	}

	// =====================================================================
	// ABRIR E FECHAR
	// =====================================================================
	public override void _Input(InputEvent e)
	{
		if (e is not InputEventKey { Pressed: true, Echo: false } k) return;

		// ============================ O PORTAO SO VALE PRO ATALHO, E NAO PRO ESC ============================
		// "Escrevendo no chat, `p` e a letra p" -- a regra que o dono deu, e a mesma que ja vale pra
		// andar e socar. Ela era `Chat.Digitando` no topo da funcao e virou `Foco.Digitando`, que e a
		// pergunta unica da casa: escrever o nome de uma tecnica na mesa de tecnicas abria o menu por
		// cima da palavra, porque so o CHAT estava sendo consultado.
		//
		// Mas o portao desceu pra dentro do `if` da tecla do menu, e isso e obrigatorio: `Foco` inclui
		// o proprio menu (a busca dele e um campo de texto), entao no topo ele engoliria o ESC -- e o
		// ESC daqui e justamente quem LIMPA a busca. Barrar tudo teria trancado o jogador dentro do
		// campo que ele quis fechar.
		// ==============================================================================================
		if (Teclas.Bate("ui_menu", k))
		{
			// `AtalhosMudos` e nao `Digitando`: o P e uma das 22 letras que o quick time event sorteia,
			// e esta tela le em `_Input` -- ANTES do `ClashQte`. Ou seja, responder "P" ao embate abria
			// o menu E comia a letra (o `SetInputAsHandled` logo abaixo). Sair aqui sem consumir deixa
			// o evento seguir ate o embate, que e de quem a tecla e naquele momento.
			if (Foco.AtalhosMudos) return;
			Alternar();
			GetViewport().SetInputAsHandled();
			return;
		}

		// ESC fecha o menu ANTES de o menu de pause ouvir a tecla (ele escuta em
		// _UnhandledInput, que roda depois daqui)
		if (Visible && k.Keycode == Key.Escape)
		{
			// ESC DESFAZ UMA CAMADA POR VEZ, da mais interna pra mais externa: primeiro a FICHA de
			// skill (ela e um painel por cima da aba), depois a busca, depois a arvore aberta, e so
			// entao o menu. Fechar tudo de uma vez faria quem entrou numa arvore por engano perder
			// o painel inteiro pra corrigir o clique.
			if (FecharFicha()) { }
			else if (_busca.HasFocus() && _busca.Text.Length > 0) { _busca.Text = ""; Redesenhar(); }
			else if (_aba == "Learning" && _arvoreAberta.Length > 0) { FecharArvore(); Redesenhar(); }
			else Fechar();
			GetViewport().SetInputAsHandled();
		}
	}

	public void Alternar()
	{
		if (Visible) Fechar(); else Abrir();
	}

	/// <summary>
	/// As abas que a bancada percorre (`--diagmenu`).
	///
	/// AS VIVAS, e nao as fixas. Antes esta propriedade devolvia `Fixas`, e por isso a bancada
	/// nunca abriu Sense, Nav nem Admin -- justo as tres que so existem em certas condicoes, que
	/// sao as mais faceis de quebrar sem ninguem ver. A aba de admin, em particular, so aparece
	/// pro host: sem isto, ela so seria exercitada por alguem jogando.
	/// </summary>
	public string[] AbasDeTeste => [.. Abas()];

	/// <summary>Troca de aba pelo MESMO caminho que o botao usa. So pra bancada.</summary>
	public void IrPara(string aba)
	{
		if (aba == _aba) return;
		FecharArvore();
		_aba = aba;
		Redesenhar();
	}

	public void Abrir()
	{
		Visible = true;
		Aberto = true;
		Redesenhar();
	}

	public void Fechar()
	{
		FecharFicha();
		Visible = false;
		Aberto = false;
		_busca.ReleaseFocus();
	}

	// =====================================================================
	// MONTAGEM
	// =====================================================================
	private void Montar()
	{
		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		// SEMITRANSPARENTE como o chat: o dono pediu, e faz sentido -- da pra conferir a ficha
		// sem perder de vista o que esta acontecendo em volta.
		_painel = new PanelContainer
		{
			AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
			OffsetLeft = -380, OffsetRight = 380, OffsetTop = -290, OffsetBottom = 290,
			GrowHorizontal = Control.GrowDirection.Both, GrowVertical = Control.GrowDirection.Both,
		};
		var vidro = Tema.Caixa(new Color(0.06f, 0.07f, 0.10f, 0.88f), Tema.Borda, 12);
		_painel.AddThemeStyleboxOverride("panel", vidro);
		_raiz.AddChild(_painel);

		var coluna = new VBoxContainer();
		coluna.AddThemeConstantOverride("separation", 8);
		_painel.AddChild(coluna);

		// --- cabecalho: nome + busca ---
		var topo = new HBoxContainer();
		topo.AddThemeConstantOverride("separation", 10);
		coluna.AddChild(topo);

		_titulo = new Label { Text = "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_titulo.AddThemeFontSizeOverride("font_size", 18);
		_titulo.AddThemeColorOverride("font_color", Tema.Destaque);
		topo.AddChild(_titulo);

		_busca = new LineEdit
		{
			PlaceholderText = "procurar acao...",
			CustomMinimumSize = new Vector2(240, 0),
			ClearButtonEnabled = true,
		};
		_busca.TextChanged += _ => Redesenhar();
		topo.AddChild(_busca);

		// --- barra de abas ---
		var rolagemAbas = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(0, 34),
			VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		coluna.AddChild(rolagemAbas);
		_barraAbas = new HBoxContainer();
		_barraAbas.AddThemeConstantOverride("separation", 2);
		rolagemAbas.AddChild(_barraAbas);

		coluna.AddChild(new HSeparator());

		// A FAIXA DE MARCOS: fixa, FORA da rolagem, e so na aba de aprendizado -- ver
		// `MontarFaixaDeMarcos` no arquivo da aba.
		MontarFaixaDeMarcos(coluna);

		// --- conteudo ---
		var rolagem = new ScrollContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		coluna.AddChild(rolagem);

		// A PILHA DE PAGINAS. Uma VBox por aba, todas vivas na arvore e so UMA visivel -- ver
		// `PaginaDe`. O `_conteudo` aponta pra pagina da vez, entao os construtores de aba
		// continuam escrevendo em `_conteudo` sem saber que existe pilha.
		_pilha = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		rolagem.AddChild(_pilha);
		_conteudo = PaginaDe("Stats");

		var rodape = Tema.Rotulo("P fecha  ·  ESC fecha  ·  a busca vale pra todas as abas");
		rodape.HorizontalAlignment = HorizontalAlignment.Center;
		coluna.AddChild(rodape);
	}

	// =====================================================================
	// AS ABAS QUE EXISTEM AGORA
	// =====================================================================
	/// <summary>
	/// A lista viva de abas. Reproduz a logica do `BuildStatsHTML()`: fixas, mais as que o
	/// personagem destravou, com o Sense virando Scan quando o scouter esta ligado.
	/// </summary>
	private List<string> Abas()
	{
		var abas = new List<string>(Fixas);

		if (_atributos.Tem(Protocol.Poder.Sense))
			abas.Add(_atributos.Tem(Protocol.Poder.Scouter) ? "Scan" : "Sense");
		else if (_atributos.Tem(Protocol.Poder.Scouter))
			abas.Add("Scan");   // scouter sem a skill le BP igual: e aparelho, nao dom

		if (_atributos.Tem(Protocol.Poder.Nav)) abas.Add("Nav");
		if (_atributos.Tem(Protocol.Poder.Admin)) abas.Add(Verbos.Admin);

		return abas;
	}

	/// <summary>As abas que estao na tela agora, pra saber se a barra precisa ser refeita.</summary>
	private string _abasNaTela = "";
	private readonly Dictionary<string, Button> _botoes = [];

	// =====================================================================
	// A PILHA DE PAGINAS -- uma por aba, construida UMA vez
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ISTO EXISTE ============================
	/// O pedido do dono foi "deixar os menus ja instanciados so que invisiveis, e quando apertar a
	/// tecla so deixar visivel -- e mais rapido pra engine". Os DOIS menus ja nasciam com o mundo e
	/// so trocavam `Visible` (Boot.MontarMundo); o que custava era outra coisa, e era pior: o
	/// CONTEUDO era destruido e reconstruido do zero a cada `Redesenhar()`.
	///
	/// E `Redesenhar()` nao roda so ao abrir -- roda a cada pacote de ficha, 5x por segundo com o
	/// menu aberto, e uma vez inteira a cada abertura. A aba de skills sozinha monta centenas de
	/// linhas. Era ai que estava o custo que o `Visible` nao ia resolver.
	///
	/// Agora cada aba tem a PROPRIA pagina, viva na arvore e escondida. Trocar de aba e trocar
	/// `Visible`, e reconstruir so acontece quando o que a aba MOSTRA muda (ver `_assinaturas`) --
	/// e a mesma regra que ja valia pra barra de abas desde o defeito do "botao piscando".
	/// =============================================================================
	/// </summary>
	private VBoxContainer _pilha = null!;
	private readonly Dictionary<string, VBoxContainer> _paginas = [];

	/// <summary>O que cada pagina mostrava da ultima vez. Igual = nao ha o que refazer.</summary>
	private readonly Dictionary<string, string> _assinaturas = [];

	private VBoxContainer PaginaDe(string aba)
	{
		if (_paginas.TryGetValue(aba, out VBoxContainer? p) && IsInstanceValid(p)) return p;
		p = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = false };
		p.AddThemeConstantOverride("separation", 3);
		_pilha.AddChild(p);
		_paginas[aba] = p;
		return p;
	}

	/// <summary>
	/// O QUE ESTA ABA MOSTRA, resumido numa string. Se ela nao mudou, a pagina que ja esta montada
	/// continua correta e nao ha nada a refazer.
	///
	/// E DE PROPOSITO GROSSEIRA: numeros arredondados, contagens em vez de listas. Uma assinatura
	/// exata custaria quase o mesmo que remontar; o objetivo e cortar as 5 remontagens por segundo
	/// em que NADA visivel mudou, nao perseguir o ultimo quadro.
	///
	/// A BUSCA ENTRA NA ASSINATURA porque ela troca o conteudo inteiro da pagina (ver `Achados`).
	/// </summary>
	private string Assinatura(string aba, SheetState f)
	{
		// A PARTE DE CADA ABA MORA NO ARQUIVO DELA (`ExtraDaAssinaturaDe...`): o que uma aba passa a
		// desenhar entra na assinatura dela sem que este arquivo, que e de todas, precise mudar.
		string basica = AssinaturaBasica(aba, f);
		string extra = ExtraDaAssinatura(aba, f);
		// VAZIA CONTINUA VAZIA ("sem cache, remonta sempre"): um extra vazio nao pode transformar uma
		// aba sem assinatura numa aba congelada.
		if (basica.Length == 0 && extra.Length == 0) return "";
		return extra.Length == 0 ? basica : basica + "|" + extra;
	}

	/// <summary>O pedaco da assinatura que mora no arquivo de cada aba. Ver <see cref="Assinatura"/>.</summary>
	private string ExtraDaAssinatura(string aba, SheetState f) => aba switch
	{
		"Stats" => ExtraDaAssinaturaDeStats(f),
		"Equip" => ExtraDaAssinaturaDeEquip(f),
		"Body" => ExtraDaAssinaturaDeCorpo(f),
		"Forms" => ExtraDaAssinaturaDeFormas(f),
		"Ki" => ExtraDaAssinaturaDeKi(f),
		"People" => ExtraDaAssinaturaDeGente(f),
		"World" => ExtraDaAssinaturaDeMundo(f),
		"Cargos" => ExtraDaAssinaturaDeCargos(f),
		"Nav" => ExtraDaAssinaturaDeNav(f),
		"Tech" => ExtraDaAssinaturaDeTech(f),
		"Sense" or "Scan" => ExtraDaAssinaturaDeSentidos(f),
		Verbos.Outros => ExtraDaAssinaturaDeOutros(f),
		Verbos.Admin => ExtraDaAssinaturaDeAdmin(f),
		_ => "",
	};

	private string AssinaturaBasica(string aba, SheetState f)
	{
		GameClient? c = GameClient.Instance;
		string comum = $"{_busca.Text.Trim()}|{_arvoreAberta}|{f.Estado}";
		return aba switch
		{
			// A SECAO DE TREINO ENTRA AQUI, e sem isto ela seria uma linha CONGELADA: o multiplicador
			// muda ao trocar de planeta, ao mexer no peso e ao atravessar a porta da Sala, e nenhum
			// desses eventos mexe nos outros numeros desta assinatura. O painel mostraria o ganho do
			// mapa anterior ate o jogador levar um soco.
			// A INTEIREZA ENTRA AQUI, e nao e redundante com o BP expresso: gravidade e peso a mexem
			// por caminhos que passam ao largo dos outros campos desta linha.
			"Stats" => $"{comum}|{f.Inteireza:0.###}"
					 + $"|{f.ExpressedBP:0}|{f.Ki:0}|{f.MaxKi:0}|{f.HP:0}|{f.Vigor:0}|{f.Nutricao:0}"
					 + $"|{_atributos.PhysOff:0.##}|{_atributos.PhysDef:0.##}|{_atributos.KiOff:0.##}"
					 + $"|{_atributos.Speed:0.##}|{_atributos.Idade}|{f.Class}"
					 + $"|{_atributos.GanhoDeTreino:0.##}|{_atributos.Gravidade:0.#}"
					 + $"|{_atributos.GravEfetiva:0.#}|{_atributos.PesoMult:0.##}"
					 + $"|{_atributos.ZonaMult:0.#}|{_atributos.Esmagamento:0.##}",
			"Body" => $"{comum}|{string.Join(',', (c?.Corpo ?? []).Select(p => p.Nome + p.Vida + (p.Decepado ? "x" : "")))}",
			// AS DUAS ABAS DE SKILLS ASSINAM POR PECAS NOMEADAS (ver `AssinaturaDeSkills`). Foi uma
			// variavel fora desta linha que produziu a gaveta que nao abria (o clique invertia a flag,
			// a pagina era reaproveitada) -- e a bancada `--diagskills` tira uma peca por vez pra
			// provar que cada uma e necessaria. Elas NAO passam pelo `comum`: ele carrega `f.Estado`,
			// cujos bits altos sao a direcao do corpo, e virar pra esquerda com a arvore aberta
			// remontaria a pagina e jogaria a rolagem de volta pro topo.
			"Learning" or "Skills" => AssinaturaDeSkills(aba),
			// A PROFICIENCIA DA DISCIPLINA ENTRA AQUI porque a aba passou a DESENHA-LA: as quatro
			// formas divinas relatam a proficiencia da skill no lugar da maestria, e sem este pedaco
			// a barra delas ficaria congelada na tela enquanto sobe de verdade no servidor.
			// O MULTIPLICADOR TOTAL E O PODER EFETIVO ENTRAM COMO O TEXTO QUE VAO VIRAR, e nao como o
			// double: a aba passou a desenhar os dois, e comparar o numero cru remontaria a pagina
			// inteira a cada oscilacao de Ki -- cinco vezes por segundo, sem um pixel mudar. Assinar o
			// texto faz a remontagem acontecer exatamente quando a tela muda.
			"Forms" => $"{comum}|{_atributos.FormaAtual}|{string.Join(',', (_atributos.Maestrias ?? []).Select(m => $"{m.Forma}:{m.Pct:0.#}"))}"
					 + $"|{_atributos.Disciplina}:{_atributos.DiscReal:0.#}"
					 + $"|{MultTexto(f.MultTotal)}|{f.Inteireza * 100:0.#}",
			"Tech" => $"{comum}|{c?.TechNivel:0.#}|{c?.Zeni:0}|{c?.TechXp:0}|{c?.Obras.Count}|{c?.Catalogo.Count}",
			"Cargos" => $"{comum}|{string.Join(',', (c?.Cargos ?? []).Select(r => r.Chave + r.Dono + r.Falta))}",
			// A ABA ADMIN TEM CAIXAS DE TEXTO, e uma remontagem as recria vazias. Por isso a
			// assinatura dela e so o que o painel DESENHA (a lista de contas e as marcas de cada
			// uma) -- nada que mude a cada tick. O que o admin digitou fica fora da pagina, em
			// `_avisoDigitado`/`_contaDigitada`, e volta pra caixa na remontagem.
			// A ABA NAV TEM UM WIDGET COM ESTADO (zoom, arrasto, selecao), e por isso a assinatura
			// dela NAO passa pelo `comum`.
			//
			// Uma assinatura vazia mandaria remontar a pagina a cada pacote de ficha e o mapa
			// nasceria de novo, enquadrado no padrao, no meio do arrasto. Mas o `comum` tambem nao
			// serve: ele carrega `f.Estado`, cujos dois bits altos sao a DIRECAO DO CORPO -- e ela
			// e reescrita a cada pacote de input enquanto se anda. Com o menu aberto o personagem
			// continua andando (o `Foco.Digitando` so vale se a BUSCA tiver foco), entao virar pra
			// esquerda remontava a aba e jogava fora o zoom que o jogador acabou de ajustar.
			//
			// O que a aba mostra que muda de verdade: a zona (espaco ou nao, que liga o botao de
			// viajar), a seed do universo, e a busca -- que troca o conteudo da pagina inteira.
			// AS DUAS LINHAS DAS ESFERAS ENTRAM AQUI, e sao os UNICOS numeros vivos desta aba: o placar
			// das Super e a frase do radar dourado. Sem elas, quem cruzasse uma celula com sinal com o
			// menu aberto veria a linha do ontem -- e o radar so serve se ele muda quando se anda. Os
			// dois sao discretos (um contador e uma frase pronta do servidor), entao nao trazem de
			// volta o problema que este comentario descreve: eles nao mudam por quadro.
			"Nav" => $"{_busca.Text.Trim()}|{c?.Zone.Hash}|{c?.SeedDoUniverso}"
				   + $"|{c?.MinhasSupers}|{c?.SinalDourado}",
			// O CLIMA ENTRA AQUI SO PELO QUE E DISCRETO -- o tipo e "e forcado?". A FORCA fica de
			// fora de proposito: ela sobe e desce continuamente durante os 45 s de transicao, e
			// remontar a pagina a cada quadro do fade recriaria as caixas de texto vazias na mao
			// de quem esta digitando. E a mesma armadilha que o comentario acima descreve.
			// `FormaAtual` ENTRA porque o painel de formas marca com ● o degrau em uso. Sem ela a
			// marca ficaria no botao velho depois de forcar uma forma -- o painel diria que nada
			// aconteceu justamente no clique em que tudo aconteceu.
			// O CODIGO DA LIMPEZA ENTRA, e so ele: a previa chegar (ou vencer) TEM que remontar a
			// pagina, senao o painel de perigo aparece so quando outra coisa qualquer mudar. As
			// LINHAS do inventario ficam de fora porque elas nunca mudam sem o codigo mudar junto --
			// e porque somar oito strings a uma assinatura que se compara 5x por segundo e caro a toa.
			Verbos.Admin => $"{comum}|{c?.AlvoId}|{_atributos.FormaAtual}"
						  + $"|{World.Instancia?.TempoQueFaz?.Tipo}|{World.Instancia?.TempoQueFaz?.Forcado}"
						  + $"|{c?.Limpeza.Codigo}|"
						  + string.Join(',', (c?.Contas ?? []).Select(a => $"{a.Conta}{a.Admin}{a.Banida}{a.Online}")),
			// O TETO DE CARGA ENTRA porque a aba passou a imprimi-lo: ele so muda ao aprender uma skill
			// de power-up, e nenhum dos outros campos desta linha se mexe quando isso acontece.
			"Ki" => $"{comum}|{f.Ki:0}|{f.MaxKi:0}|{f.TetoKi:0.##}|{c?.SkillsAprendidas.Count}",
			// PEOPLE PRECISA DE ASSINATURA DESDE QUE ELA TEM BOTAO. Ela caia no `_ => ""` (remonta
			// sempre), que era inofensivo enquanto a aba era so texto -- agora ela tem os botoes de
			// relacao e o aceitar/recusar do pedido de amizade, e uma pagina refeita 5x por segundo
			// destroi o botao debaixo do dedo de quem clica. E a MESMA armadilha que o bloco do
			// Admin acima descreve, e ela mordeu este projeto uma vez.
			//
			// Entram: os vinculos (que so mudam a cada 3 s, e devagar), o pedido pendente, e os
			// NOMES visiveis -- que mudam quando alguem entra ou sai do campo de visao, e nao a
			// cada quadro. Os pontos vao arredondados de proposito: 0,1 por passo remontaria a
			// pagina toda vez sem nada visivel mudar.
			"People" => $"{comum}|{c?.PedidoDeAmizade}|"
					  + string.Join(',', (c?.Conhecidos ?? []).Select(
							k => $"{k.Assinatura}{k.Amizade:0}{k.Familiaridade}{k.Relacao}{k.Rival}{k.Inimizade:0}"))
					  + "|" + string.Join(',', World.Instancia?.NomesVisiveis() ?? []),
			// As abas fixas sem dado proprio (Equip, Other) sao texto parado: uma assinatura
			// so ja basta pra elas nunca mais serem remontadas.
			"Equip" or Verbos.Outros => comum,
			// WORLD PASSOU A TER BOTOES (dominio e esferas vieram da Nav), e por isso ela precisou de
			// assinatura. Ela caia no `_ => ""` -- remonta a cada pacote de ficha --, e isso era inofensivo
			// enquanto a aba era so texto: com botao, uma pagina refeita cinco vezes por segundo destroi o
			// botao debaixo do dedo de quem clica. E a MESMA armadilha que os blocos do Admin e do People
			// descrevem aqui em cima, e ela ja mordeu este projeto uma vez.
			//
			// ENTRA O QUE A ABA DESENHA, nos mesmos arredondamentos em que ela desenha -- assinar o TEXTO e
			// nao o double faz a remontagem acontecer exatamente quando a tela muda, e nao a cada oscilacao
			// invisivel. A zona cobre os dois blocos novos de uma vez (os dois so mudam de forma ao trocar
			// de lugar); as duas linhas vivas das esferas entram pelo mesmo motivo que entravam na Nav.
			"World" => $"{comum}|{c?.Zone.Hash}|{c?.MinhasSupers}|{c?.SinalDourado}|{AssinaturaDoCeu()}",
			// AS QUE DEPENDEM DE QUEM ESTA NA TELA refazem sempre: People lista corpos que entram e saem a
			// cada snapshot, e uma assinatura que os cobrisse custaria o mesmo que remontar. Devolver vazio
			// e dizer "nao ha cache pra esta".
			_ => "",
		};
	}

	/// <summary>
	/// O CEU E O CLIMA DA ABA WORLD, resumidos -- a parte VIVA da assinatura dela.
	///
	/// Mora num metodo separado porque repete os arredondamentos que o <see cref="Mundo"/> usa pra
	/// DESENHAR, e os dois tem que continuar iguais: e a igualdade que garante que a pagina se refaz
	/// quando (e so quando) o texto na tela muda.
	///
	/// A FORCA DO CLIMA ENTRA COMO PORCENTAGEM INTEIRA e o relogio como decimo de hora, que e como
	/// aparecem; o "quanto falta pra cheia" entra em segundos inteiros. O pior caso e uma remontagem
	/// por segundo no ultimo minuto antes da lua cheia -- contra as cinco por segundo de antes.
	/// </summary>
	private static string AssinaturaDoCeu()
	{
		Jandirus.Core.World.RelogioDoPlaneta r = World.Instancia?.RelogioDoLugar
											 ?? Jandirus.Core.World.RelogioDoPlaneta.Padrao;
		string ceu = World.Instancia?.Ceu is { } c
			? $"{c.Hora * 24:00.0}|{c.Fase}|{c.LuaNoCeu}|"
			  + $"{Jandirus.Core.World.Ceu.SegundosAteACheia(r, World.Instancia.TempoDoMundo):0}"
			: "-";
		string clima = World.Instancia?.TempoQueFaz is { } tq
			? $"{tq.Ativo}|{tq.Tipo}|{tq.Forca:P0}|{tq.Forcado}"
			: "-";
		return $"{Jandirus.Core.World.Ceu.NomeDoCiclo(r)}|{ceu}|{clima}";
	}

	/// <summary>
	/// `--diagmenu`: mede o menu em vez de afirmar que ele ficou rapido.
	///
	/// Sao dois numeros que importam e que nao dava pra ver antes: quantas vezes o conteudo foi
	/// REMONTADO (contra quantas o pedido foi atendido pela pagina que ja estava pronta) e quanto
	/// custa uma remontagem. O primeiro e o que diz se o cache funciona; o segundo e o que o dono
	/// sente ao apertar P.
	/// </summary>
	private static readonly bool Diag = Array.IndexOf(OS.GetCmdlineArgs(), "--diagmenu") >= 0;
	private int _remontagens, _reaproveitadas;
	private double _msRemontando;

	/// <summary>O relatorio do `--diagmenu`. Publico porque quem fecha o menu e quem o imprime.</summary>
	public string Relatorio() =>
		$"[menu] {_remontagens} remontagens ({_msRemontando:0.0} ms no total, "
		+ $"{(_remontagens > 0 ? _msRemontando / _remontagens : 0):0.00} ms cada) | "
		+ $"{_reaproveitadas} pedidos atendidos pela pagina ja montada";

	private void Redesenhar()
	{
		List<string> abas = Abas();

		// a aba aberta pode ter DEIXADO de existir (tirou o scouter com o Scan aberto -- o
		// original tratava exatamente este caso)
		if (!abas.Contains(_aba)) _aba = abas.Contains("Sense") ? "Sense" : "Stats";

		// ============================ A BARRA DE ABAS NAO SE RECONSTROI ============================
		// Ela era destruida e recriada a CADA `Redesenhar()`. E `Redesenhar` roda a cada pacote de
		// ficha -- varias vezes por segundo, ainda mais desde que vigor e estado entraram na
		// deteccao de mudanca. Um botao recem-criado nao esta sob o mouse ate o proximo quadro,
		// entao o realce acendia e apagava sem parar e clicar virava questao de sorte. Foi o que o
		// dono descreveu: "boto o mouse em cima da aba e ela fica piscando... ai pra clicar fica
		// ruim".
		//
		// O CONTEUDO continua sendo refeito (ele E o que muda). A barra so muda quando a LISTA de
		// abas muda -- tirar o scouter tira a aba Scan --, e isso e raro e detectavel.
		// =========================================================================================
		string assinatura = string.Join('', abas);
		if (assinatura != _abasNaTela)
		{
			_abasNaTela = assinatura;
			foreach (Node n in _barraAbas.GetChildren()) n.QueueFree();
			_botoes.Clear();

			foreach (string a in abas)
			{
				string qual = a;
				var b = new Button
				{
					Text = a,
					ToggleMode = true,
					FocusMode = Control.FocusModeEnum.None,
				};
				b.AddThemeFontSizeOverride("font_size", 12);
				// TROCAR DE ABA FECHA A ARVORE. O original tambem nao guardava a arvore aberta entre
				// visitas: `TreeWindowClose()` zera o CurrentTree (SkillTreesWindow.dm:287). Voltar pra
				// Learning e cair na lista de arvores, que e a casa da aba.
				b.Pressed += () => { if (qual != _aba) FecharArvore(); _aba = qual; Redesenhar(); };
				_barraAbas.AddChild(b);
				_botoes[qual] = b;
			}
		}

		// qual esta marcada e propriedade, nao node novo: da pra atualizar sem recriar nada
		foreach ((string nome, Button botao) in _botoes) botao.ButtonPressed = nome == _aba;

		SheetState f = GameClient.Instance?.Sheet ?? default;
		_titulo.Text = GameClient.Instance?.LocalName ?? "";

		// SO A PAGINA DA VEZ APARECE. As outras continuam montadas, escondidas -- voltar pra elas
		// e uma troca de `Visible`, e nao uma remontagem.
		_conteudo = PaginaDe(_aba);
		foreach ((string nome, VBoxContainer pg) in _paginas)
			if (IsInstanceValid(pg)) pg.Visible = nome == _aba;

		// A FAIXA DE MARCOS E FIXA (fora da rolagem) e so existe na aba de aprendizado sem busca. A
		// visibilidade se decide AQUI, antes do desvio de cache logo abaixo: trocar de aba com a
		// pagina ja montada nao remonta nada, e a faixa tem que sumir mesmo assim.
		_faixaDeMarcos.Visible = _aba == "Learning" && _busca.Text.Trim().Length == 0;

		// NADA MUDOU? Entao a pagina que ja esta ai continua certa. E o que corta as cinco
		// remontagens por segundo que o pacote de ficha provocava com o menu aberto.
		string sig = Assinatura(_aba, f);
		if (sig.Length > 0 && _assinaturas.TryGetValue(_aba, out string? antiga)
			&& antiga == sig && _conteudo.GetChildCount() > 0)
		{
			_reaproveitadas++;
			return;
		}
		_assinaturas[_aba] = sig;

		ulong t0 = Time.GetTicksUsec();
		_remontagens++;

		foreach (Node n in _conteudo.GetChildren()) { _conteudo.RemoveChild(n); n.QueueFree(); }
		Montar(f);
		_msRemontando += (Time.GetTicksUsec() - t0) / 1000.0;
	}

	/// <summary>Enche a pagina da aba da vez. Separado do <see cref="Redesenhar"/> so pra o
	/// cronometro ter onde fechar -- os `return` no meio de um metodo unico o perderiam.</summary>
	private void Montar(SheetState f)
	{
		// BUSCA VENCE A ABA. Ver o comentario da classe.
		string termo = _busca.Text.Trim();
		if (termo.Length > 0) { Achados(termo); return; }

		switch (_aba)
		{
			case "Stats": Stats(f); break;
			case "Body": Corpo(); break;
			case "Ki": Ki(f); break;
			case "People": Gente(); break;
			case "World": Mundo(); break;
			case "Learning": Aprendizado(); break;
			case "Forms": AbaFormas(f); break;
			case "Cargos": AbaCargos(); break;
			case "Nav": AbaNav(); break;
			case "Skills": Sabidas(); break;
			case Verbos.Outros: AbaOutros(); break;
			case Verbos.Admin: AbaAdmin(); break;
			case "Tech": AbaTech(); break;
			case "Equip": AbaEquip(f); break;
			case "Sense" or "Scan": AbaSentidos(f); break;
			default: AindaNao(_aba); break;
		}
	}

	// =====================================================================
	// PECAS
	// =====================================================================
	private void Secao(string texto)
	{
		if (_conteudo.GetChildCount() > 0) _conteudo.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
		var l = Tema.Rotulo(texto);
		l.AddThemeColorOverride("font_color", Tema.Destaque);
		_conteudo.AddChild(l);
		_conteudo.AddChild(new HSeparator());
	}

	/// <summary>Uma linha "rotulo .... valor", com a cor de qualidade do original (hi/av/lo). A linha em si
	/// mora em `LinhaSolta` (MenuJogo.Pecas.cs), pra poder cair dentro de um cartao tambem.</summary>
	private void Linha(string rotulo, string valor, Color? cor = null) => _conteudo.AddChild(LinhaSolta(rotulo, valor, cor));

	// =====================================================================
	// AS PORTAS DE BANCADA -- o que a ABA ESCREVEU
	// =====================================================================
	/// <summary>
	/// ============================ O QUE UMA LINHA DESTA ABA REALMENTE DESENHOU ============================
	/// Pra bancada, e so pra ela. E ela existe por causa da queixa exata do dono: *"ao abrir o menu do P
	/// o ki vai estar certo (...) porem na barra do jogo em si ela fica sempre em 100%"*. DUAS TELAS
	/// DISCORDANDO.
	///
	/// Uma bancada que leia `Sheet.RazaoDeKi` dos dois lados prova que a FICHA e uma so -- e isso nunca
	/// esteve em duvida: a ficha ja era uma so quando o bug estava vivo. O corte morava no widget, DEPOIS
	/// da ficha. A unica leitura que separa "as duas telas concordam" de "as duas telas leem o mesmo
	/// campo" e comparar os dois TEXTOS desenhados, e este metodo e a metade do menu dessa comparacao
	/// (a outra e <see cref="Barra.TextoDeTeste"/>).
	///
	/// ACHA PELO ROTULO e nao por indice: a ordem das linhas de uma aba muda a cada feature, e uma
	/// bancada presa em "a quarta linha da aba Stats" quebraria sem que nada tivesse quebrado.
	///
	/// DEVOLVE `null` QUANDO A PAGINA NAO EXISTE -- aba nunca visitada, ou menu que nunca abriu. Nao
	/// devolve vazio: "nao ha o que ler" e "a aba escreveu vazio" sao coisas diferentes, e a bancada
	/// precisa saber reprovar a primeira em vez de compara-la com a HUD e achar que achou um bug.
	/// =================================================================================================
	/// </summary>
	public string? ValorDesenhado(string aba, string rotulo)
	{
		if (!_paginas.TryGetValue(aba, out VBoxContainer? pg) || !IsInstanceValid(pg)) return null;

		// EM QUALQUER PROFUNDIDADE, e pelo METADADO `linha` que toda linha rotulo/valor carrega (ver
		// `LinhaSolta`): a linha desceu pra dentro de cartoes quando as abas ganharam a lingua visual da
		// Learning, e uma porta que so olhasse os filhos diretos da pagina deixaria de ver o "Ki" que a
		// `--diagbancada` compara com o HUD. O prefixo continua sendo o plano B, pelo motivo de sempre.
		string? porPrefixo = null;
		foreach (Node n in Descendentes(pg))
		{
			if (n is not HBoxContainer h || !h.HasMeta("linha") || h.GetChildCount() < 2) continue;
			if (h.GetChild(1) is not Label v) continue;
			string r = h.GetMeta("linha").AsString();
			// A FAIXA (numero grande + legenda) le como a linha inteira: "??? (sem scouter)".
			string texto = h.HasMeta("faixa")
				? string.Join("   ", h.GetChildren().Skip(1).OfType<Label>().Select(l => l.Text).Where(t => t.Length > 0))
				: v.Text;
			if (r == rotulo) return texto;
			porPrefixo ??= r.StartsWith(rotulo, StringComparison.Ordinal) ? texto : null;
		}
		return porPrefixo;
	}

	/// <summary>A arvore inteira debaixo de um node, em profundidade. So pras portas de bancada.</summary>
	private static IEnumerable<Node> Descendentes(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			yield return f;
			foreach (Node n in Descendentes(f)) yield return n;
		}
	}

	/// <summary>
	/// A PAGINA MONTADA DE UMA ABA, ou nula se ela nunca foi visitada. SO PRA BANCADA.
	///
	/// ============================ ELA EXISTE PRA UMA VARREDURA, E NAO PRA UMA LEITURA ============================
	/// O <see cref="ValorDesenhado"/> responde "quanto esta escrito nesta linha", e isso serve pra
	/// comparar numero. A pergunta desta bancada e outra e nao cabe naquele metodo: *"sobrou em
	/// ALGUM lugar deste menu um botao que mexe com a nave?"* -- ou seja, ela precisa percorrer a
	/// arvore inteira da aba sem saber de antemao o que procura.
	///
	/// Devolver a pagina (e nao uma lista de textos pronta) e o que deixa a bancada tambem APERTAR o
	/// que achou: uma varredura que so le rotulos provaria que o botao sumiu da TELA, e nao que o
	/// caminho pro verbo sumiu. Ver a familia 4 da `--diagembarque`.
	/// =========================================================================================================
	/// </summary>
	public Control? PaginaDeTeste(string aba) =>
		_paginas.TryGetValue(aba, out VBoxContainer? p) && IsInstanceValid(p) ? p : null;

	/// <summary>
	/// A ABA QUE O JOGADOR ESTA OLHANDO AGORA. SO PRA BANCADA.
	///
	/// ============================ ELA RESPONDE A OUTRA METADE DE "SUMIU" ============================
	/// `PaginaDeTeste("Nav") is not { Visible: true }` diz que a pagina da Nav saiu da tela. Isso e meia
	/// resposta: sair e nao ser substituida por NADA e uma tela morta -- o menu abre e nao ha nada
	/// escrito nele. A outra metade e "e ficou o que no lugar?", e ela mora no `_aba` depois do desvio
	/// do <see cref="Redesenhar"/> ("a aba aberta pode ter DEIXADO de existir"), que e o mesmo desvio
	/// que o original faz ao tirar o scouter com a aba Scan aberta.
	///
	/// PERGUNTAR A LISTA DE PAGINAS NAO SERVE: todas continuam vivas na arvore -- e o cache que existe
	/// pra nao remontar --, e a que o jogador ve e uma so.
	/// ============================================================================================
	/// </summary>
	public string AbaDeTeste => _aba;

	/// <summary>
	/// FORCA A ABA DA VEZ A SE REDESENHAR AGORA, ignorando a assinatura de reaproveitamento.
	///
	/// ============================ POR QUE A BANCADA PRECISA DISTO ============================
	/// A pagina so e remontada quando a `Assinatura` muda, e ela e GROSSEIRA de proposito (numeros
	/// arredondados). Entre um pacote e outro o Ki pode andar meio ponto: a HUD, que redesenha a cada
	/// ficha, ja escreve o numero novo, e a aba continua com o texto de um instante atras. Comparar os
	/// dois nesse intervalo acusaria uma discordancia que nao existe.
	///
	/// Com o redesenho forcado no MESMO quadro, as duas telas leem literalmente o mesmo `SheetState`, e
	/// ai a comparacao pode exigir IGUALDADE EXATA -- que e o que se quer afirmar.
	///
	/// E A ABA PRESA CONTINUA SENDO COBERTA, so que por outra checagem: a bancada tambem le a aba SEM
	/// forcar depois de mexer no valor, justamente pra pegar o dia em que um campo novo ficar de fora
	/// da assinatura e a aba congelar. As duas leituras respondem perguntas diferentes.
	/// =====================================================================================
	/// </summary>
	public void ForcarRedesenho()
	{
		_assinaturas.Remove(_aba);
		Redesenhar();
	}

	private void Aviso(string texto)
	{
		var l = new Label { Text = texto, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		l.AddThemeColorOverride("font_color", Tema.TextoFraco);
		l.AddThemeFontSizeOverride("font_size", 12);
		_conteudo.AddChild(l);
	}

	// =====================================================================
	// SKILLS -- a aba inteira (Learning, Skills e a ficha) mora em MenuJogo.Skills.cs
	// =====================================================================

	private void Achados(string termo)
	{
		var lista = Verbos.Buscar(termo).ToList();
		Secao($"Busca: \"{termo}\"");
		foreach (Verbo v in lista) Botao(v, mostrarCategoria: true);
		// AS SKILLS ENTRAM NA BUSCA. O comentario da classe prometia "a busca vale pro jogo inteiro"
		// e ela so varria os verbs: quem digitava "Afterimage" achava nada. Ver `AchadosDeSkills`.
		int skills = AchadosDeSkills(termo);
		if (lista.Count == 0 && skills == 0) Aviso("Nenhuma acao nem habilidade com esse nome.");
	}

	private void Botao(Verbo v, bool mostrarCategoria = false) => _conteudo.AddChild(BotaoDe(v, mostrarCategoria));

	/// <summary>O botao de um verb, sem pendura-lo em lugar nenhum: a aba de aprendizado o poe numa grade.</summary>
	private Button BotaoDe(Verbo v, bool mostrarCategoria = false)
	{
		var b = new Button
		{
			Text = mostrarCategoria ? $"{v.Nome}   [{v.Categoria}]" : v.Nome,
			TooltipText = v.Descricao,
			Alignment = HorizontalAlignment.Left,
			// SEM ACAO = APAGADO. A faixa passiva de uma disciplina entra no catalogo so pra ser
			// LIDA (ver `Verbo.Acionar`), e ate aqui ela virava botao clicavel que estourava uma
			// NullReferenceException no clique. Apagada ela continua dizendo o que diz e nao promete
			// mais nada -- a mesma regra do verb indisponivel, na linha de cima.
			Disabled = !v.PodeAgora || v.Acionar == null,
		};
		b.Pressed += () => { v.Acionar?.Invoke(); Redesenhar(); };
		return b;
	}

	/// <summary>
	/// A aba existe, o sistema por tras dela ainda nao. Dizer isso e melhor que esconder a aba:
	/// o original tambem tinha painel sem conteudo ("this panel has no detailed view yet"), e
	/// ver a aba e saber que aquilo faz parte do jogo.
	/// </summary>
	private void AindaNao(string aba)
	{
		Secao(aba);
		Aviso(aba switch
		{
			"Equip" => "O que esta vestido e equipado. Vem com o sistema de itens.",
			"Tech" => "Nivel tecnologico, construcoes e androides. Vem com o sistema de tecnologia.",
			"Sense" => "Leitura de Ki: quem esta por perto e quao forte. Vem com a skill de Sense.",
			"Scan" => "Leitura EXATA de poder de luta pelo scouter. Vem com o scouter.",
			"Nav" => "Mapa do espaco e viagem entre planetas. Vem com o nav system.",
			_ => "Ainda nao implementado.",
		});
	}
}
