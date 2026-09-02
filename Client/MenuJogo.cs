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
			case Verbos.Outros: ListaDeVerbos(_aba); break;
			case Verbos.Admin: AbaAdmin(); break;
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

	/// <summary>Uma linha "rotulo .... valor", com a cor de qualidade do original (hi/av/lo).</summary>
	private void Linha(string rotulo, string valor, Color? cor = null)
	{
		var h = new HBoxContainer();
		var a = new Label { Text = rotulo, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		a.AddThemeColorOverride("font_color", Tema.TextoFraco);
		a.AddThemeFontSizeOverride("font_size", 13);
		h.AddChild(a);
		var b = new Label { Text = valor, HorizontalAlignment = HorizontalAlignment.Right };
		b.AddThemeColorOverride("font_color", cor ?? Tema.Texto);
		b.AddThemeFontSizeOverride("font_size", 13);
		h.AddChild(b);
		_conteudo.AddChild(h);
	}

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

		string? porPrefixo = null;
		foreach (Node n in pg.GetChildren())
		{
			if (n is not HBoxContainer h || h.GetChildCount() < 2) continue;
			if (h.GetChild(0) is not Label r || h.GetChild(1) is not Label v) continue;
			if (r.Text == rotulo) return v.Text;
			// o prefixo e o plano B: "Maestria desta forma" x "Proficiencia em X" trocam de nome
			// conforme a forma, e cravar o nome exato faria a bancada reprovar a aba certa.
			porPrefixo ??= r.Text.StartsWith(rotulo, StringComparison.Ordinal) ? v.Text : null;
		}
		return porPrefixo;
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
	// STATS -- o ui_tab_stats do original
	// =====================================================================
	private void Stats(SheetState f)
	{
		Secao("Poder");

		// "???" SEM SCOUTER. E a regra do original e ela e do JOGO, nao da interface: ninguem
		// le o proprio poder de luta em numero sem um aparelho que meca.
		//
		// COM scouter sai o expresso E o base, e as duas metades sao literais do DM
		// (HtmlUI.dm:178-181: `[FullNum(round(expressedBP))] (base [FullNum(round(BP))])` no ramo
		// `if(scouteron)`, `??? (no scouter)` no outro). Esta e a UNICA linha do painel inteiro que
		// tem permissao de imprimir BP -- a aba Forms nao imprime nem o limiar (ver AbaFormas).
		Linha("Battle Power", _atributos.Tem(Protocol.Poder.Scouter)
			? $"{f.ExpressedBP:N0}   (base {f.BP:N0})"
			: "???   (sem scouter)");

		// ============================ QUANTO DISSO ESTA SAINDO ============================
		// A MESMA leitura que o HUD poe ao lado do BP, do MESMO campo -- e por isso as duas telas nao
		// tem como divergir. Aparece com scouter ou sem: e razao, nao poder (ver `GameServer.Sigilo`).
		//
		// A frase explica o que ela nao e: transformar multiplica os dois lados da fracao, entao a
		// forma nao mexe nela. Quem mexe e Ki, ferimento, peso, gravidade e idade.
		Linha("Poder efetivo", $"{f.Inteireza * 100:0.#}%   (do seu pico sem desgaste)",
			f.Inteireza >= 0.9 ? Tema.Bom : f.Inteireza <= 0.5 ? Tema.Perigo : Tema.Texto);

		Linha("Vida", $"{f.HP:0}%", f.HP >= 66 ? Tema.Bom : f.HP <= 33 ? Tema.Perigo : Tema.Texto);

		// A RAZAO SAI DO STRUCT, e nao de um `Ki / MaxKi` escrito aqui: era essa copia -- tres delas,
		// nesta aba, na aba Ki e no HUD -- que deixava o corte de uma passar despercebido nas outras.
		Linha("Ki", $"{f.Ki:N0} / {f.MaxKi:N0}   ({f.RazaoDeKi * 100:0}%)",
			f.RazaoDeKi > 1 ? Tema.Destaque : Tema.Texto);
		Linha("Vigor", $"{_atributos.Stamina * 100:0}%");

		// ============================ A NUTRICAO FALTAVA, E ELA EXPLICA O VIGOR ============================
		// O vigor cai sozinho e so sobe as custas do tanque de comida. Sem este numero na tela, um
		// jogador com o folego minguando nao tem como saber que o problema e FOME -- ele ve uma
		// barra caindo e nenhuma causa. Ver `Core.Stats.Nutricao`.
		//
		// A COR AVISA ANTES DE DOER: o aviso de fome do servidor bate em 25% de vigor, mas quem fica
		// sem tanque para de recuperar MUITO antes disso.
		//
		// A BARRA DE NUTRICAO AGORA EXISTE NO HUD, e ela le esta mesma razao (`RazaoDeNutricao`).
		double pct = f.RazaoDeNutricao * 100;
		Linha("Nutrição", $"{f.Nutricao:0} / {f.NutricaoMax:0}   ({pct:0}%)",
			pct >= 50 ? Tema.Bom : pct <= 15 ? Tema.Perigo : Tema.Texto);

		Treino();

		Secao("Atributos");
		(string Nome, float Valor)[] atts =
		[
			("Ofensiva Fisica", _atributos.PhysOff), ("Defesa Fisica", _atributos.PhysDef),
			("Ofensiva de Ki", _atributos.KiOff),    ("Defesa de Ki", _atributos.KiDef),
			("Tecnica", _atributos.Technique),       ("Pericia de Ki", _atributos.KiSkill),
			("Velocidade", _atributos.Speed),        ("Esoterico", _atributos.Esoteric),
		];

		// A COR SAI DA COMPARACAO COM A PROPRIA MEDIA, como no `ui_qual()`: um atributo nao e
		// alto ou baixo em absoluto, e alto ou baixo PRA ESTE personagem. E o que deixa a
		// vocacao de cada um visivel de relance.
		float media = 0;
		foreach ((string _, float v) in atts) media += v;
		media /= Math.Max(atts.Length, 1);

		foreach ((string nome, float v) in atts)
		{
			Color cor = v >= media * 1.2f ? Tema.Bom : v <= media * 0.8f ? Tema.Perigo : Tema.Texto;
			string rotulo = v >= media * 1.2f ? "alto" : v <= media * 0.8f ? "baixo" : "medio";
			Linha(nome, $"{v * 10:0}   ({rotulo})", cor);
		}
		Linha("Forca de Vontade", $"{_atributos.Willpower:0.##}");

		Secao("Estado");

		// A CLASSE NAO APARECE, NUNCA. Ela e sorteio cego na criacao (por isso a tela de criacao
		// so da uma dica indireta, CreationScreen.cs:500) e o painel do original tambem nunca a
		// imprimiu: `ui_tab_stats()` lista poder, atributos, emocao e estilo -- classe nao esta la
		// (HtmlUI.dm:175-229). Escrever "Legendary" numa linha entrega de graca o que o jogo inteiro
		// trata como descoberta, e ainda vaza pra quem olha a tela de outro.
		Linha("Raca", _atributos.Raca ?? "");
		Linha("Idade", $"{_atributos.Idade}");
		Linha("Condicao", f.Morto ? "MORTO" : f.KO ? "NOCAUTEADO" : "de pe",
			f.Morto || f.KO ? Tema.Perigo : Tema.Bom);
		Linha("Golpe", f.Letal ? "LETAL" : "nao-letal", f.Letal ? Tema.Perigo : Tema.Texto);
		Linha("Cadencia do soco", $"{f.SocoMs} ms");
	}

	/// <summary>
	/// QUANTO O TREINO ESTA RENDENDO -- a linha "BP GAIN" do painel do original (`HtmlUI.dm:182-187`).
	///
	/// ============================ ESTA SECAO E O SISTEMA ============================
	/// Peso, gravidade e Sala do Tempo mudam quanto BP entra por tique. BP por tique nao se ve: o
	/// numero da tela e o mesmo antes e depois, e so muda mais rapido -- o que, num jogo onde o BP
	/// sobe a vida inteira, e indistinguivel de nada acontecendo. A queixa que abriu esta camada e
	/// literalmente essa ("o sistema e invisivel e parece nao existir").
	///
	/// Por isso a linha nao mostra so o produto: ela mostra as PARTES. "2800x (10x grav · 1,4x pesos ·
	/// Sala 280x)" e uma frase acionavel -- diz o que aumentar. "2800x" sozinho e um numero magico.
	///
	/// O NUMERO VEM PRONTO DO SERVIDOR (`Fighter.MultiplicadorDeGanho`, no Core). O cliente nao refaz
	/// a conta: ele nao tem `Egains`, nem `GravMastered`, nem `zoneGainMult`, e uma segunda copia da
	/// formula seria a que envelhece calada.
	/// =============================================================================
	/// </summary>
	private void Treino()
	{
		Secao("Treino");

		var partes = new List<string>();
		if (_atributos.Gravidade > 0) partes.Add($"{_atributos.Gravidade:0.#}x grav");
		// A ACLIMATACAO SO APARECE QUANDO EXISTE: quem domina mais gravidade do que sente treina com
		// parte da folga (`GravAccustomWeight`), e sem esta pista o jogador nao entenderia por que
		// rende 3x num chao de 1x.
		if (_atributos.GravEfetiva > _atributos.Gravidade + 0.05f)
			partes.Add($"aclimatado → {_atributos.GravEfetiva:0.#}");
		if (_atributos.PesoMult > 1.001f) partes.Add($"{_atributos.PesoMult:0.##}x pesos");
		if (_atributos.ZonaMult > 1.001f) partes.Add($"Sala {_atributos.ZonaMult:0}x");
		else if (_atributos.ZonaMult < 0.999f) partes.Add($"zona {_atributos.ZonaMult:0.##}x");

		double g = _atributos.GanhoDeTreino;
		Linha("Ganho de BP", $"{g:0.##}x   ({string.Join(" · ", partes)})",
			g >= 1.5 ? Tema.Bom : g < 0.9 ? Tema.Perigo : Tema.Texto);

		// ============================ A SESSAO DA SALA DO TEMPO ============================
		// A sala e a unica coisa deste jogo que tem PRAZO com castigo no fim (a porta tranca aos 50
		// minutos e so o Guardiao solta). Um prazo cujo unico sinal e uma frase que ja rolou pra
		// cima no chat e uma armadilha -- e esta linha e onde se olha pra saber quanto falta.
		//
		// O NUMERO VEM PRONTO, como o de cima: quem conta o tempo (em DIAS in-game, por tique) e o
		// servidor. O cliente nao tem o relogio do mundo dele nem sabe quando a janela foi armada.
		// ================================================================================
		switch (_atributos.SalaFase)
		{
			case 1:
				Linha("Sessão na Sala", $"~{_atributos.SalaMinutos:0} min de treino restantes",
					_atributos.SalaMinutos <= 5 ? Tema.Perigo : Tema.Bom);
				break;
			case 2:
				Linha("Sessão na Sala", $"ACABOU -- {_atributos.SalaMinutos:0.#} min pra sair pela porta",
					Tema.Perigo);
				break;
			case 3:
				Linha("Sessão na Sala", "PRESO -- só o Guardião da Terra (ou um admin) solta", Tema.Perigo);
				break;
		}

		// ============================ O ESMAGAMENTO E O OUTRO LADO DA MESMA MOEDA ============================
		// A razao acima de 1 e o que a gravidade alta COBRA: dano por segundo, folego drenado e passo
		// mais lento. Ela fica logo abaixo do ganho de proposito -- as duas linhas juntas sao a
		// decisao inteira ("rende 10x e me machuca 1,8x do que aguento").
		//
		// A partir de `RazaoQuePrende` o corpo nao anda. Sem esta linha, um jogador preso no chao teria
		// as teclas mortas e nenhuma explicacao na tela.
		// ==================================================================================================
		double r = _atributos.Esmagamento;
		if (r <= 1.0001f) return;

		bool preso = r >= Jandirus.Core.Stats.Esmagamento.RazaoQuePrende;
		Linha("Esmagamento", preso
				? $"{r:0.##}x do seu limite   (PRESO NO CHÃO)"
				: $"{r:0.##}x do seu limite   (perdendo vida e velocidade)",
			Tema.Perigo);
	}

	// =====================================================================
	// BODY -- os membros, os mesmos que o boneco do HUD desenha
	// =====================================================================
	private void Corpo()
	{
		List<Protocol.ParteState> partes = GameClient.Instance?.Corpo ?? [];
		if (partes.Count == 0) { Aviso("O corpo ainda nao chegou do servidor."); return; }

		Secao("Membros");
		foreach (Protocol.ParteState p in partes)
		{
			if (p.Decepado) { Linha(p.Nome, "DECEPADO", Tema.Perigo); continue; }
			Color cor = p.Vida >= 66 ? Tema.Bom : p.Vida <= 33 ? Tema.Perigo : Tema.Texto;
			Linha(p.Nome, $"{p.Vida}%", cor);
		}
	}

	private void Ki(SheetState f)
	{
		Secao("Energia");
		Linha("Ki atual", $"{f.Ki:N0}");
		Linha("Ki maximo", $"{f.MaxKi:N0}");

		// MESMA RAZAO DO HUD E DA ABA STATS -- ver `SheetState.RazaoDeKi`.
		Linha("Percentual", $"{f.RazaoDeKi * 100:0.#}%",
			f.RazaoDeKi > 1 ? Tema.Destaque : Tema.Texto);

		// O TETO DE CARGA E O FIM DO TRILHO DA BARRA, e mostra-lo nao e invencao do port: o original
		// imprimia este mesmo numero com esta mesma cara (`Statistics.dm:349`, `Ki Capacity`). Ele so
		// sobe com as skills de power-up, e sem ele o jogador nao tem como saber quanto ainda cabe.
		Linha("Teto de carga", $"{f.TrilhoDeKi * 100:0}% do tanque");

		Aviso("\nSegure C pra reunir energia. Acima de 100% o Ki NAO para de contar: ele entra "
			+ "linear no poder -- 118% de Ki é 1,18x de BP -- e a barra do HUD desenha esse "
			+ "excedente até o teto de carga.");
	}

	// =====================================================================
	// PEOPLE -- o "Known People" do original
	// =====================================================================
	/// <summary>
	/// ============================ A ABA PEOPLE -- O KNOWN-PEOPLE DO ORIGINAL ============================
	/// Ela tinha SO a lista de quem estava na tela, que e o que o `NomesVisiveis` sabia responder.
	/// Agora ela e o que o `ui_tab_people()` do DM (`HtmlUI.dm:545-560`) mostrava: a lista de quem
	/// voce CONHECE, com a ficha "como visto da ultima vez" e o degrau de proximidade.
	///
	/// AS DUAS SECOES CONVIVEM porque respondem coisas diferentes: "quem esta aqui agora" (que e o
	/// que voce usa pra marcar alguem) e "quem eu conheco" (que e o que sobrevive ao logout). No
	/// original as duas tambem existiam, em telas separadas.
	///
	/// O RETRATO NAO VEIO. La ele e o icone achatado do corpo (`contact_portrait`), servido por
	/// `browse_rsc`; aqui a aparencia e montada em camadas pelo cliente e "a foto de como ele
	/// estava" seria um SEGUNDO caminho de vestir alguem. Ficaram os campos de texto, que sao o que
	/// a aba de la realmente le.
	/// ======================================================================================================
	/// </summary>
	private void Gente()
	{
		if (GameClient.Instance is not { } cli) return;

		// ---- o pedido de amizade pendente vem PRIMEIRO: e o unico item aqui que expira ----
		if (cli.PedidoDeAmizade.Length > 0)
		{
			Secao($"{cli.PedidoDeAmizade} quer ser seu amigo");
			var linha = new HBoxContainer();
			var sim = new Button { Text = "Aceitar" };
			sim.Pressed += () => cli.SendVerbo("amizade_aceitar");
			var nao = new Button { Text = "Recusar" };
			nao.Pressed += () => cli.SendVerbo("amizade_recusar");
			linha.AddChild(sim);
			linha.AddChild(nao);
			_conteudo.AddChild(linha);
		}

		Secao("Quem voce conhece");
		if (cli.Conhecidos.Count == 0)
			Aviso("Voce ainda nao conviveu com ninguem de quem valha a pena lembrar.");

		foreach (GameClient.ConhecidoInfo c in cli.Conhecidos)
		{
			// SEM FICHA = so vinculo, sem rosto. `??? (assinatura)` e a forma do original.
			string nome = c.Nome.Length > 0 ? c.Nome : $"??? ({c.Assinatura})";
			string grau = Convivio.RotuloDeProximidade(c.Amizade);
			string odio = Convivio.RotuloDeInimizade(c.Inimizade);
			var rel = (Relacao)c.Relacao;

			Linha(nome, $"{grau} ({c.Amizade:0})");
			Aviso($"      {c.Raca} / {c.Classe}  ·  como visto da ultima vez"
				  + $"  ·  convivio {c.Familiaridade}"
				  + (rel != Relacao.Nenhuma ? $"  ·  {Convivio.NomeDaRelacao(rel)}" : "")
				  + (c.Rival ? "  ·  RIVAL" : "")
				  + (odio.Length > 0 ? $"  ·  {odio} ({c.Inimizade:0})" : ""));

			BotoesDeRelacao(cli, c);
		}

		Secao("Quem esta por perto");
		List<string> nomes = World.Instancia?.NomesVisiveis() ?? [];
		if (nomes.Count == 0) { Aviso("Ninguem no seu campo de visao."); return; }
		foreach (string n in nomes) Linha(n, "");
	}

	/// <summary>
	/// AS DECLARACOES QUE ESTA FAMILIARIDADE JA PAGA -- o verb `Relation()` do original, que la era
	/// um `input()` com a lista inteira e a explicacao dos custos no titulo.
	///
	/// O BOTAO APARECE APAGADO em vez de sumir, que e a regra do menu deste port: saber que "Amor"
	/// existe e que custa 200 de convivio e informacao; esconder e esconder o jogo do jogador.
	///
	/// **QUEM DECIDE E O SERVIDOR.** Isto aqui e conveniencia -- o `VerboRelacao` confere a
	/// familiaridade de novo, porque apagar botao nunca foi permissao.
	/// </summary>
	private void BotoesDeRelacao(GameClient cli, GameClient.ConhecidoInfo c)
	{
		if (c.Nome.Length == 0) return;   // sem ficha nao ha o que declarar

		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 2);
		foreach (Relacao r in Convivio.Declaraveis)
		{
			int pede = Convivio.FamiliaridadeExigida(r);
			bool pode = c.Familiaridade >= pede;
			var b = new Button
			{
				Text = Convivio.NomeDaRelacao(r),
				Disabled = !pode || (Relacao)c.Relacao == r,
				TooltipText = pode ? "declarar" : $"exige {pede} de convivio (voce tem {c.Familiaridade})",
			};
			b.AddThemeFontSizeOverride("font_size", 11);
			string sig = c.Assinatura, nome = Convivio.NomeDaRelacao(r);
			b.Pressed += () => cli.SendVerbo("relacao", $"{sig}|{nome}");
			linha.AddChild(b);
		}
		_conteudo.AddChild(linha);
	}

	private void Mundo()
	{
		Secao("Onde voce esta");
		Linha("Zona", GameClient.Instance?.Zone.Name ?? "");

		Jandirus.Core.World.RelogioDoPlaneta r = World.Instancia?.RelogioDoLugar
											 ?? Jandirus.Core.World.RelogioDoPlaneta.Padrao;
		Linha("Ciclo", Jandirus.Core.World.Ceu.NomeDoCiclo(r));

		if (World.Instancia?.Ceu is { } c)
		{
			Linha("Hora", $"{Jandirus.Core.World.Ceu.NomeDaHora(c.Hora)} ({c.Hora * 24:00.0}h)");

			if (!r.Lua.Existe) Linha("Lua", "este mundo nao tem lua");
			else
			{
				Linha("Lua", Jandirus.Core.World.Ceu.NomeDaFase(c.Fase)
							 + (c.LuaNoCeu ? " (no ceu)" : " (abaixo do horizonte)"));

				// QUANTO FALTA PRA CHEIA. E a informacao com que se PLANEJA -- sem ela o ciclo
				// da lua e uma surpresa que cai na cabeca de quem tem rabo.
				double faltam = Jandirus.Core.World.Ceu.SegundosAteACheia(r, World.Instancia.TempoDoMundo);
				Linha("Lua cheia", faltam < 0 ? "nunca (a lua nao chega a nascer aqui)"
							 : faltam < 1 ? "AGORA"
							 : faltam < 60 ? $"em {faltam:0} s"
							 : $"em {faltam / 60:0} min");
			}
		}

		Jandirus.Core.World.ClimaDoPlaneta cl = World.Instancia?.ClimaDoLugar
											?? Jandirus.Core.World.ClimaDoPlaneta.Nenhum;
		if (World.Instancia?.TempoQueFaz is { } tq)
		{
			Linha("Clima", tq.Ativo
				? $"{Jandirus.Core.World.Clima.Nome(tq.Tipo)} ({tq.Forca:P0})"
					+ (tq.Forcado ? " -- forcado" : "")
				: "ceu limpo");

			// O QUE PODE CAIR AQUI vem do `allowedWeatherTypes` do DM, e e informacao de LUGAR:
			// saber que em Vegeta chove sangue e nao agua e parte de conhecer Vegeta.
			if (cl.Existe && cl.Permitidos.Length > 0)
				Linha("Pode cair", string.Join(", ",
					cl.Permitidos.Select(Jandirus.Core.World.Clima.Nome)));
			else Linha("Pode cair", "nada -- o ceu daqui nao muda");
		}

		Aviso("\nCada planeta corre o proprio dia e o proprio tempo: a hora e o ceu daqui");
		Aviso("nao sao os da Terra.");

		// ============================ A CONQUISTA E AS ESFERAS MORAM AQUI, E NAO NA NAV ============================
		// Elas estavam na aba Nav -- e o comentario que terminava a linha acima ("a conquista do planeta
		// entra aqui com o sistema dela") ja dizia onde era a casa delas.
		//
		// A MUDANCA E CONSEQUENCIA DO PORTAO, e nao gosto: a aba Nav passou a depender do item Nav System
		// (pedido do dono). Deixar dominio e esferas la dentro trancaria fincar bandeira, cobrar tributo,
		// invocar Shenron e erguer estatua atras de um aparelho de 550.000 zeni -- nada disso tem a ver com
		// navegar, e o Namekuseijin do Cla do Dragao ficaria sem caminho nenhum pras esferas dele. Este e o
		// UNICO lugar do cliente por onde esses verbos saem (nao ha segundo caminho), entao esconder a aba
		// sem move-los seria apagar duas features que ninguem pediu pra apagar.
		//
		// A ABA WORLD SEMPRE EXISTE (esta em `Fixas`) e e sobre o LUGAR -- e as duas sao sobre o lugar. O
		// preco e que World precisou de assinatura: ver `Assinatura`, que explica por que.
		// ==========================================================================================================
		if (GameClient.Instance is { } cli)
		{
			// O REFUGIO VEM PRIMEIRO, e sem crivo de zona: ver `BlocoDoRefugio`. Ele so aparece quando
			// o planeta natal desta pessoa deixou de existir, e nesse dia e a coisa mais urgente desta
			// aba -- o dominio e as esferas continuam podendo esperar.
			BlocoDoRefugio(cli);
			BlocoDeDominio(cli);
			BlocoDasEsferas(cli);
		}
	}

	// =====================================================================
	// NAV -- o mapa do espaco
	// =====================================================================
	/// <summary>
	/// O MAPA ESTELAR: a galaxia desenhada, com zoom, arrasto e clique.
	///
	/// ============================ ERA UMA LISTA, E LISTA NAO E MAPA ============================
	/// Esta aba mostrava os corpos celestes das tres chunks em volta, em texto, ordenados por
	/// distancia. Isso responde "o que ha por perto" -- e nao responde a pergunta que faz um mapa
	/// estelar existir, que e "PRA ONDE EU VOU". Sem posicao relativa nao da pra escolher rota, nao
	/// da pra saber que Namek fica do lado oposto de Vegeta, e o universo inteiro cabe em cinco
	/// linhas de texto que mudam quando voce anda.
	///
	/// O pedido do dono: "faca um mapa do espaco com todos os planetas que o servidor sabe onde
	/// estao... voce clica nos planetas e seleciona viajar... e voce pode dar zoom e zoom out".
	/// O desenho, o zoom e o clique moram no <see cref="MapaEstelar"/>; esta aba e a moldura.
	/// ===========================================================================================
	///
	/// O TEMPO CONTINUA SENDO O QUE IMPORTA no texto: "1.608.035 px" nao diz nada a ninguem, "7
	/// dias" diz tudo -- e a mesma unidade em que o anime mede a viagem pra Namek. A conta sai do
	/// Core (velocidade de voo x ciclo do dia), entao ela nao pode divergir do que a viagem custa.
	/// </summary>
	private void AbaNav()
	{
		if (GameClient.Instance is not { } cli) return;

		// ============================ A SEGUNDA METADE DO PORTAO ============================
		// A aba so entra na barra com o bit `Poder.Nav` (ver `Abas()`), e o bit so acende com o item na
		// mochila (`GameServer.Sigilo.PoderesVisiveis`). Entao esta recusa e, em jogo normal, inalcancavel
		// -- e ela existe pelo mesmo motivo que existe no original: `ui_tab_nav` (`HtmlUI.dm:336-344`)
		// tambem imprime "O sistema de navegacao esta desligado" mesmo com a barra ja tendo escondido a
		// aba em `HtmlUI.dm:138`. Duas metades, e nenhuma confiando na outra.
		//
		// SEJAMOS HONESTOS SOBRE O QUE ELA VALE: nao e permissao. A carta estelar nao vem por rede
		// nenhuma (`MapaEstelar` e funcao pura da seed do mundo) e o piloto e do cliente
		// (`World.Pilotar` so escreve `LocalPlayer.Destino`, e andar ate la o servidor ja valida como
		// andar). Um cliente mexido continua enxergando a galaxia e continua conseguindo pilotar. O que
		// esta linha entrega e o que ela promete: a aba nao monta sem o item.
		// ==================================================================================================
		if (!_atributos.Tem(Protocol.Poder.Nav))
		{
			Secao("NAV SYSTEM");
			Aviso("O sistema de navegação está desligado: você não tem um Nav System na mochila. "
				+ "Ele se fabrica na bancada de pesquisa (aba Tech).");
			return;
		}

		bool noEspaco = Jandirus.Core.World.Espaco.EhEspaco(cli.Zone);

		// O MAPA APARECE EM TERRA FIRME TAMBEM -- so o botao de viajar e que nao.
		//
		// Ele e uma CARTA: consultar antes de decolar e metade do uso de uma carta estelar, e
		// esconder o mapa de quem esta no chao obrigaria a subir pra descobrir aonde subir.
		Secao(noEspaco ? "Carta estelar" : "Carta estelar (em terra: so leitura)");

		_mapa = new MapaEstelar { Name = "MapaEstelar" };

		// A BARRA VEM ANTES DO MAPA na arvore, e isso e proposital.
		//
		// O mapa e alto (a carta so serve grande) e a aba mora num ScrollContainer: com a barra
		// EMBAIXO ela caia fora da area visivel e so aparecia depois de rolar -- os controles de
		// zoom ficavam escondidos justamente na tela que existe pra dar zoom. Em cima, eles sao a
		// primeira coisa que se ve.
		var barra = new HBoxContainer();
		foreach ((string rotulo, Action acao, string dica) in new (string, Action, string)[]
		{
			("+", () => _mapa.Zoom(1.6f), "aproximar (roda do mouse pra cima)"),
			("-", () => _mapa.Zoom(1f / 1.6f), "afastar (roda do mouse pra baixo)"),
			("centralizar em mim", () => _mapa.VerMim(), "poe voce no meio, num zoom de vizinhanca"),
			("ver tudo", () => _mapa.VerTudo(), "enquadra os mundos com mapa proprio"),
		})
		{
			var b = new Button { Text = rotulo, TooltipText = dica };
			Action fazer = acao;
			b.Pressed += () => fazer();
			barra.AddChild(b);
		}
		_conteudo.AddChild(barra);
		_conteudo.AddChild(_mapa);

		// ============================ A TELA DO SISTEMA MORA AQUI DO LADO ============================
		// Ela e IRMA do mapa e nasce escondida, em vez de ser criada no primeiro duplo clique. Duas
		// razoes, e as duas ja custaram caro neste projeto:
		//
		//   * criar node dentro do tratador do clique remonta o layout do `ScrollContainer` no meio
		//     do evento, e o mapa perde o zoom e o arrasto -- o mesmo motivo que ja mantem o painel
		//     do destino fora da remontagem da aba;
		//   * as duas telas compartilham o painel do destino embaixo. Trocar VISIBILIDADE deixa o
		//     painel intacto; trocar NODE obrigaria a religar os eventos dele toda vez.
		// ==========================================================================================
		_sistema = new TelaDoSistema { Name = "TelaDoSistema", Visible = false };
		_conteudo.AddChild(_sistema);

		// O PAINEL DO DESTINO fica FORA da remontagem da pagina: ele muda a cada clique no mapa, e
		// remontar a aba inteira a cada clique jogaria fora o zoom e o arrasto que o jogador acabou
		// de ajustar. Por isso o mapa avisa por evento e so este pedaco se refaz.
		var painel = new VBoxContainer();
		_conteudo.AddChild(painel);

		void Repintar() => DesenharDestino(painel, noEspaco);

		_mapa.SelecaoMudou += Repintar;
		_mapa.PediuViagem += p => Viajar(p, noEspaco);
		_mapa.PediuSistema += s =>
		{
			_sistema.Mostrar(s);
			_mapa.Visible = false;
			barra.Visible = false;   // "+/-/ver tudo" sao da carta; a tela do sistema tem os dela
			_sistema.Visible = true;
			Repintar();
		};

		_sistema.SelecaoMudou += Repintar;
		_sistema.PediuViagem += p => Viajar(p, noEspaco);
		_sistema.PediuPorto += p => ViajarAoPonto(p, noEspaco);
		_sistema.PediuVoltar += () =>
		{
			_sistema.Visible = false;
			_mapa.Visible = true;
			barra.Visible = true;
			Repintar();
		};

		Repintar();

		Aviso("\nCada pontinho e um SISTEMA. Clique pra selecionar, DUPLO CLIQUE pra abrir o mapa do "
			+ "sistema -- estrela no centro, os mundos nos aneis de orbita. Arraste pra mover, roda pra zoom.");
		Aviso("A cor do ponto e a classe da estrela, medida na propria arte dela. Aproximando, os mundos "
			+ "aparecem: o laranja tem mapa proprio, o azul-acinzentado e gerado. Duplo clique num MUNDO viaja.");
		Aviso("A viagem leva o tempo que diz: o piloto anda no passo normal, nao teleporta. "
			+ "Terra a Namek sao 7 dias in-game, como no anime.");

		// ============================ AS DUAS LEGENDAS DA CARTA ============================
		// Elas sao o que sobrou do bloco de botoes da nave, e sobraram porque NAO SAO ACOES: sao
		// legenda desta carta, e agora moram embaixo dela, que e onde legenda mora.
		//
		// A PRIMEIRA explica por que a bolinha do mapa mudou de dono -- sem ela, quem pediu Observar
		// no console da ponte fica olhando pra um ponto e achando que e ele mesmo. A SEGUNDA e a
		// ESCALA da carta: "1.608.035 px" nao diz nada, "7 dias" diz tudo, e o numero da nave ao lado
		// e o que transforma comprar velocidade numa decisao.
		// ================================================================================
		if (cli.NaveVista is { } nv)
			Aviso($"\nA carta está centrada na NAVE (em {nv.Zona}, casco {nv.CascoPct:0}%), e não em você. "
				+ "Ela volta a mostrar você quando desembarcar.");

		double dTN = Jandirus.Core.World.Espaco.DistanciaTerraNamek;
		Aviso($"A pé, Terra→Namek leva {Jandirus.Core.World.Espaco.SegundosDeViagem(dTN) / 60:0} min "
			+ $"({Jandirus.Core.World.Espaco.DiasInGame(dTN):0.#} dias in-game). "
			+ $"Numa Spacepod no limite ({Jandirus.Core.Tech.Naves.VelocidadeMaxima}x), "
			+ $"{Jandirus.Core.Tech.Naves.SegundosDeViagem(dTN, Jandirus.Core.Tech.Naves.VelocidadeMaxima) / 60:0.#} min.");
	}

	// ============================ O BLOCO DE BOTOES DA NAVE FOI DELETADO DAQUI ============================
	// Eram OITO botoes (entrar/sair, lancar, melhorar, ver estado, largar o leme, observar, desembarcar,
	// recondicionar) e os oito eram INTERACAO com um objeto, e nao decisao de menu. O dono disse isso com
	// todas as letras: *"a MAIORIA deles nem eram pra ser verbs do menu, e sim INTERACAO com as naves (ao
	// apertar E perto delas...)"*. Todos os oito ja viviam tambem no `Interacoes`, ou seja: eram a segunda
	// porta pra mesma coisa, que e o defeito que este repo mais paga.
	//
	// ELES ESTAVAM AQUI POR UM MOTIVO REAL, e o motivo foi RESOLVIDO em vez de ignorado: a nave PILOTADA
	// sai da lista de construcoes da zona, entao a tecla E nao tinha alvo nenhum pra oferecer ao piloto --
	// nem pra descer. Hoje o servidor diz qual veiculo esta embaixo de voce (`Protocol.S2C.Veiculo`) e o
	// menu da tecla E o alcanca como alcanca a macieira. Ver `Interacoes.DoVeiculo` e `MenuDeInteracao`.
	//
	// O QUE SOBROU sao as duas LEGENDAS (a nave observada e a escala Terra->Namek): elas nao sao acoes, e
	// por isso desceram pra debaixo da carta, no fim da `AbaNav`, que e onde legenda de mapa mora.
	// ====================================================================================================

	/// <summary>
	/// ============================ A CONQUISTA MORA NA ABA NAV, E NAO NUMA ABA PROPRIA ============================
	/// Ela e sobre PLANETAS, e a aba Nav ja e a tela dos planetas -- a carta estelar, o sistema, a
	/// viagem. Uma aba nova custaria uma entrada permanente na barra pra uma coisa que so faz sentido
	/// quando voce esta pisando num mundo.
	///
	/// ============================ E SAO BOTOES, NAO UM PAINEL DE DADOS ============================
	/// Nenhum destes botoes desenha estado: eles mandam VERBO e o servidor responde no chat. Isso e
	/// deliberado e nao preguica -- no DM a conquista inteira e `alert()`, `input()` e `to_chat()`,
	/// e um painel de dominios exigiria um opcode novo (dominios, lealdade, tributo) pra ser sempre
	/// verdadeiro. "Ver este planeta" e "Meus domínios" respondem tudo isso em texto, e o servidor
	/// continua sendo a unica fonte.
	///
	/// QUEM AUTORIZA E O SERVIDOR: esconder botao nunca foi permissao neste projeto, e por isso os
	/// botoes aparecem sempre que se esta num planeta -- a recusa ("você é fraco demais", "chegue
	/// mais perto da bandeira") e a resposta do verbo, com o motivo.
	/// ======================================================================================================
	/// </summary>
	private void BlocoDeDominio(GameClient cli)
	{
		// EM TERRA FIRME SO. O crivo e o `Espaco.EhPlaneta` do Core -- a MESMA definicao de "planeta"
		// que o servidor usa pra recusar (e a mesma que a destruicao de planeta ja consulta). Duas
		// nocoes de planeta envelheceriam separadas, e a primeira a errar seria a do cliente.
		if (!Jandirus.Core.World.Espaco.EhPlaneta(cli.Zone)) return;

		Secao("Domínio planetário");

		var linha1 = new HBoxContainer();
		var linha2 = new HBoxContainer();

		foreach ((string rotulo, string cmd, string arg, string dica, bool segunda) in
			new (string, string, string, string, bool)[]
			{
				("Ver este planeta", "conq_ver", "",
					"quem manda aqui, quanto vale de tributo e quanto poder a invasão exige", false),
				("Meus domínios", "conq_dominios", "",
					"seus planetas, a lealdade de cada um e o tributo acumulado", false),
				("Conquistar planeta", "conq_invadir", "",
					"finca a bandeira. Herói de um povo sem dono reivindica em PAZ", false),
				("...à força", "conq_invadir", "forca",
					"invade mesmo sendo herói do povo daqui -- matar defensores mancha sua reputação", false),

				("Coletar tributo", "conq_tributo", "", "junto da sua bandeira", true),
				("Renascer aqui", "conq_spawn", "", "liga/desliga este domínio como ponto de renascimento", true),
				("Abandonar domínio", "conq_abandonar", "sim", "a posse se perde na hora", true),
				("Arrancar bandeira", "conq_arrancar", "",
					"8s parado e sem levar dano frustram a invasão de outra pessoa", true),
			})
		{
			string c = cmd, a = arg;
			var b = new Button { Text = rotulo, TooltipText = dica };
			b.AddThemeFontSizeOverride("font_size", 12);
			b.Pressed += () => cli.SendVerbo(c, a);
			(segunda ? linha2 : linha1).AddChild(b);
		}

		_conteudo.AddChild(linha1);
		_conteudo.AddChild(linha2);
		Aviso("Domínio sem soberano por perto se perde: o tributo mingua, a guarnição afrouxa e o povo "
			+ "acaba derrubando a bandeira. Apareça.");
	}

	/// <summary>
	/// **A PORTA PERMANENTE DO REFUGIO** -- pra onde voce volta quando o seu planeta natal acabou.
	///
	/// ============================ POR QUE ELE NAO MORA DENTRO DO BLOCO DE DOMINIO ============================
	/// O <see cref="BlocoDeDominio"/> desiste na primeira linha quando o jogador nao esta na
	/// superficie de um planeta -- e o refugio e exatamente a tela de quem **nao esta**: quem precisa
	/// dela costuma estar morto no Outro Mundo (que nao e um planeta da carta) ou em orbita de um
	/// mundo que acabou de nascer sob ele. Dentro daquele bloco, o botao ficaria escondido justamente
	/// de quem precisa dele.
	/// ====================================================================================================
	///
	/// A tela e EMPURRADA uma vez por sessao (na chegada ao Outro Mundo, ver `GameServer.Refugio.cs`),
	/// e uma tela que so aparece sozinha e uma tela que se perde: quem a fechar sem ler nunca mais
	/// acha o caminho de volta. **Ele so existe com o berco destruido**, e a condicao e a do SERVIDOR
	/// (`RefugioPrecisa` vem de la, e nao de uma conta feita aqui).
	/// </summary>
	private void BlocoDoRefugio(GameClient cli)
	{
		if (!cli.RefugioPrecisa) return;

		Secao("Refúgio");
		Aviso($"{cli.RefugioNatal} foi destruída. Da próxima vez que você morrer, o seu corpo "
			+ "precisa de outro lugar para voltar.");

		var refugio = new Button
		{
			Text = "Escolher para onde eu volto",
			TooltipText = "um planeta que você conquistou, ou o mundo vivo mais perto de onde era casa",
		};
		refugio.AddThemeColorOverride("font_color", Tema.Destaque);
		refugio.Pressed += () => TelaDeRefugio.Instancia?.Abrir();
		_conteudo.AddChild(refugio);
	}

	/// <summary>
	/// ============================ AS ESFERAS MORAM NA ABA NAV, AO LADO DO DOMINIO ============================
	/// Pelo mesmo argumento que trouxe a conquista pra ca: elas sao sobre PLANETAS (a esfera comum
	/// pertence a um mundo e nunca sai dele) e sobre a GALAXIA (as Super se espalham por celulas de
	/// sistema, e o radar dourado e um instrumento de navegacao). Uma aba propria custaria uma entrada
	/// permanente na barra pra uma coisa que so faz sentido em dois lugares -- e os dois ja estao aqui.
	///
	/// ============================ E SAO BOTOES, NAO UM PAINEL ============================
	/// Nenhum destes botoes desenha estado: eles mandam VERBO e o servidor responde no chat. Mesma
	/// escolha (e mesma razao) do `BlocoDeDominio` -- no original a coisa toda e `alert()`, `input()` e
	/// `to_chat()`. As DUAS excecoes sao dados que o servidor ja empurra por conta propria e que
	/// mentiriam se fossem calculados aqui: o **placar das Super** e a **frase do radar dourado**.
	///
	/// QUEM AUTORIZA E O SERVIDOR: esconder botao nunca foi permissao neste projeto. "Erguer estátua"
	/// aparece pra todo mundo, e a recusa ("apenas Namekuseijins do Clã do Dragão...") e a resposta do
	/// verbo, com o motivo.
	/// ====================================================================================================
	/// </summary>
	private void BlocoDasEsferas(GameClient cli)
	{
		bool noEspaco = Jandirus.Core.World.Espaco.EhEspaco(cli.Zone);
		bool emPlaneta = Jandirus.Core.World.Espaco.EhPlaneta(cli.Zone);
		if (!noEspaco && !emPlaneta) return;

		Secao("Esferas do Dragão");

		// O SINAL DOURADO VEM PRONTO DO SERVIDOR (ver `Protocol.S2C.SuperEsferas`): o cliente nao
		// sabe onde as sete estao, e e assim que o mapa do tesouro nao viaja.
		if (cli.SinalDourado.Length > 0)
		{
			var l = new Label { Text = cli.SinalDourado, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			l.AddThemeColorOverride("font_color", new Color(1f, 0.82f, 0.25f));
			l.AddThemeFontSizeOverride("font_size", 13);
			_conteudo.AddChild(l);
		}

		Linha("Super Esferas suas", $"{cli.MinhasSupers}/{Jandirus.Core.Magic.SuperEsferas.Total}");

		var linha1 = new HBoxContainer();
		var linha2 = new HBoxContainer();

		foreach ((string rotulo, string cmd, string arg, string dica, bool espaco, bool segunda) in
			new (string, string, string, string, bool, bool)[]
			{
				("Ver as esferas daqui", "db_ver", "",
					"que estátua manda neste mundo, quantos pedidos ela dá e se as sete estão acordadas",
					false, false),
				("Radar", "db_radar", "",
					"precisa do Dragon Radar na mochila. Só acha esfera ACORDADA, e só deste mundo",
					false, false),
				("Pegar esfera", "db_pegar", "", "a que estiver ao seu alcance", false, false),
				("Largar tudo", "db_largar", "", "põe no chão as que você carrega", false, false),
				("INVOCAR", "db_invocar", "",
					"com as sete reunidas: no chão ao seu redor ou com você", false, true),

				("Erguer Estátua do Dragão", "db_estatua", "",
					"só Namekuseijin do Clã do Dragão, e só num mundo que ele domine (ou na Terra, se "
					+ "for o Guardião)", false, true),

				// ============================ ELE E A UNICA PORTA DO DESEJO SUPREMO ============================
				// `namekian.dm:101-109`: o "Strongest in the Universe" nao sai da escada de poder -- ele e
				// COMPRADO por 2.000.000 de zeni **na hora de erguer a estatua**, e nunca depois. No
				// original isso e um `alert()` que aparece sozinho; aqui virou argumento, e um argumento
				// sem botao seria um desejo que so existe pra quem digita verbo. Segundo botao, e nao um
				// campo de texto, porque a escolha e binaria.
				// ========================================================================================
				("Erguer + gravar o SUPREMO (2.000.000 zeni)", "db_estatua", "supremo",
					"grava nestas esferas o desejo O MAIS FORTE DO UNIVERSO -- a única forma de ele "
					+ "existir num set de jogador. Cobra na hora", false, true),
				("Despertar as esferas", "db_reviver", "",
					"só quem ergueu a estátua, e só vivo -- é a única volta de um set inerte", false, true),

				("Super Esferas: placar", "sdb_status", "",
					"quem tem quantas, e quantas ainda estão livres (sem dizer ONDE)", true, false),
				("Reivindicar Super Esfera", "sdb_reivindicar", "",
					"chegue perto dela no espaço. 10s se estiver livre, 5 MINUTOS se for de alguém -- "
					+ "e o dono é avisado", true, false),
				("Chamar o Super Shenron", "sdb_invocar", "",
					"com as sete Super Esferas. A Língua dos Deuses é a Fase 2", true, false),
			})
		{
			// O RECORTE E POR LUGAR e nao por permissao: reivindicar uma Super so acontece no espaco,
			// e erguer estatua so em terra. Botao que nunca poderia funcionar dali e ruido.
			if (espaco != noEspaco) continue;

			string c = cmd, a = arg;
			var b = new Button { Text = rotulo, TooltipText = dica };
			b.AddThemeFontSizeOverride("font_size", 12);
			b.Pressed += () => cli.SendVerbo(c, a);
			(segunda ? linha2 : linha1).AddChild(b);
		}

		_conteudo.AddChild(linha1);
		if (linha2.GetChildCount() > 0) _conteudo.AddChild(linha2);

		BlocoDoPedido(cli, noEspaco);

		Aviso(noEspaco
			? "As Super Esferas têm o tamanho de um planetoide: não se carregam. Quem defende não "
			+ "precisa vencer o ladrão -- basta derrubá-lo ou afastá-lo da esfera."
			: "As sete esferas de um mundo nunca saem dele: largue uma em outro planeta e ela volta "
			+ "sozinha. Depois de um pedido, elas apagam e se espalham de novo.");
	}

	/// <summary>O que o jogador escreveu na linha do desejo: o id, e o alvo quando o desejo pede um.</summary>
	private string _textoDoDesejo = "";

	/// <summary>
	/// **O PEDIDO** -- a linha de texto do desejo e os botoes que a usam.
	///
	/// ============================ POR QUE UMA CAIXA DE TEXTO, E NAO UMA LISTA DE BOTOES ============================
	/// A lista de desejos **nao e fixa**: ela depende do poder do set, de quantos pedidos ele da, de o
	/// criador ter comprado o supremo, do genoma Saiyajin de quem pede e do cargo dele. Desenhar isso
	/// aqui exigiria que o cliente refizesse a escada inteira do `WishTable.dm` -- uma SEGUNDA casa pra
	/// a mesma formula, que e o defeito que a regra da casa proibe por nome.
	///
	/// Entao quem manda a lista e o servidor, no chat, quando o verbo chega sem argumento -- e a caixa
	/// serve pra devolver a escolha. E o mesmo desenho do painel de admin ("escreva a skill ao lado") e
	/// pelo mesmo motivo: o conteudo e do servidor, o teclado e do jogador.
	/// ========================================================================================================
	/// </summary>
	private void BlocoDoPedido(GameClient cli, bool noEspaco)
	{
		Secao(noEspaco ? "O pedido ao Super Shenron" : "O pedido ao dragão");

		var campo = new LineEdit
		{
			PlaceholderText = noEspaco ? "id do desejo (e o alvo, se pedir)" : "id do desejo (e o alvo, se pedir)",
			Text = _textoDoDesejo,
			MaxLength = Jandirus.Net.Protocol.MaxArgDeVerbo - 4,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		campo.TextChanged += t => _textoDoDesejo = t;
		_conteudo.AddChild(campo);

		var linha = new HBoxContainer();
		foreach ((string rotulo, string cmd, string dica, bool precisaTexto) in noEspaco
			? new (string, string, string, bool)[]
			{
				("Ver os desejos", "sdb_invocar", "com as sete, lista o que o Super Shenron atende", false),
				("PRONUNCIAR", "sdb_invocar",
					"pede o desejo escrito ao lado. Se você carrega uma PROCURAÇÃO, o que vale é o "
					+ "pedido de quem emprestou -- você não pode trocá-lo", true),
				("Emprestar as sete", "sdb_transferir",
					"escreva ao lado o nome de quem vai falar por você. Ele precisa estar do seu lado, "
					+ "e precisa aceitar. O desejo continua sendo SEU", true),
				("Meu pedido", "sdb_pedido",
					"só quem emprestou escreve aqui: é o desejo que o porta-voz vai pronunciar", false),
				("Retomar a guarda", "sdb_revogar",
					"toma de volta as sete que você emprestou -- a qualquer momento", false),
				("Aceitar a guarda", "sdb_guarda_aceitar", "aceita falar em nome de quem te ofereceu", false),
				("Recusar", "sdb_guarda_recusar", "recusa a guarda das sete", false),
				("ACEITO O PREÇO", "sdb_aceito",
					"só para o desejo que cobra a VIDA: escreva ACEITO O PREÇO ao lado", true),
			}
			: new (string, string, string, bool)[]
			{
				("Ver os desejos", "db_desejar", "com o dragão de pé, lista o que ele atende", false),
				("PEDIR", "db_desejar", "pede o desejo escrito ao lado", true),

				// ============================ ESTES DOIS SO PASSARAM A IMPORTAR AGORA ============================
				// `db_refazer` e `db_derrubar` existem desde a Fase 1 e **nunca tiveram botao**. Enquanto o
				// dragao nao concedia nada, "quantos pedidos por invocacao" era um numero sem consequencia
				// e ninguem sentia falta. Com a tabela ligada ele e a diferenca entre um pedido e tres --
				// e e o gate que faz "Ressuscitar TODOS" e "Matar" existirem ou nao no set.
				//
				// Sem este botao, um Namekuseijin do Cla do Dragao ficaria preso em UM pedido pra sempre:
				// a estatua nasce com `Desejos = 1` e so o `Redo` do original a muda.
				// ============================================================================================
				("Pedidos por invocação", "db_refazer",
					"escreva 1, 2 ou 3 ao lado. MAIS pedidos deixa cada um mais fraco -- e com 3 o set "
					+ "perde \"Ressuscitar TODOS\" e \"Matar\", que é o que separa Shenron de Porunga", true),
				("Derrubar a estátua", "db_derrubar",
					"apaga as sete deste mundo, para sempre. Escreva 'sim' ao lado pra confirmar", true),
			})
		{
			string c = cmd;
			bool pede = precisaTexto;
			var b = new Button { Text = rotulo, TooltipText = dica };
			b.AddThemeFontSizeOverride("font_size", 12);
			b.Pressed += () =>
			{
				if (pede && _textoDoDesejo.Trim().Length == 0)
				{ Chat.Sistema("escreva o pedido (ou o nome) ao lado antes."); return; }
				cli.SendVerbo(c, pede ? _textoDoDesejo.Trim() : "");
			};
			linha.AddChild(b);
		}
		_conteudo.AddChild(linha);

		Aviso(noEspaco
			? "Só quem fala a LÍNGUA DOS DEUSES desperta o Super Shenron -- cargos divinos (atuais ou "
			+ "passados) a conhecem, e o sangue Kai ou Demigod nasce com ela. Quem não fala empresta "
			+ "as sete a quem fale: o desejo continua sendo de quem emprestou, e o porta-voz não "
			+ "escolhe qual é."
			: "Aperte \"Ver os desejos\" com o dragão de pé: a lista depende do poder do set e de "
			+ "quantos pedidos ele dá. Desejo que ainda não foi portado aparece dizendo isso, e não "
			+ "gasta pedido nenhum.");
	}

	/// <summary>O mapa da aba Nav. Guardado pra os botoes da barra alcancarem a camera dele.</summary>
	private MapaEstelar _mapa = null!;

	/// <summary>A tela do sistema, irma do mapa. Ver `AbaNav`.</summary>
	private TelaDoSistema _sistema = null!;

	/// <summary>O mapa vivo, ou nulo se a aba Nav ainda nao foi montada. SO PRA BANCADA (`--diagnav`).</summary>
	public MapaEstelar? MapaDeTeste => IsInstanceValid(_mapa) ? _mapa : null;

	/// <summary>A tela do sistema viva, ou nulo. SO PRA BANCADA (`--diagnav`).</summary>
	public TelaDoSistema? SistemaDeTeste => IsInstanceValid(_sistema) ? _sistema : null;

	/// <summary>
	/// O QUE ESTA SELECIONADO: nome, distancia, tempo de viagem, e o botao que liga o piloto.
	///
	/// Refeito sozinho a cada clique no mapa -- e so ele, nao a aba.
	/// </summary>
	private void DesenharDestino(VBoxContainer painel, bool noEspaco)
	{
		foreach (Node n in painel.GetChildren()) { painel.RemoveChild(n); n.QueueFree(); }

		// DE QUAL TELA VEM O DESTINO: a que esta visivel. Ler sempre do mapa faria o painel mostrar
		// o ultimo planeta clicado NA GALAXIA enquanto o jogador clica dentro de um sistema -- e o
		// botao "Viajar" mandaria pro corpo errado, calado.
		bool dentroDoSistema = IsInstanceValid(_sistema) && _sistema.Visible;
		Jandirus.Core.World.PlanetaNoEspaco? alvo =
			dentroDoSistema ? _sistema.Selecionado : _mapa.Selecionado;

		if (alvo is not { } p)
		{
			var vazio = new Label
			{
				Text = dentroDoSistema
					? "clique num mundo do sistema pra ver a ficha dele."
					: _mapa.VendoProcedurais
						? "nenhum destino selecionado."
						: "nenhum destino selecionado. Aproxime pra os mundos gerados aparecerem.",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			vazio.AddThemeColorOverride("font_color", Tema.TextoFraco);
			vazio.AddThemeFontSizeOverride("font_size", 12);
			painel.AddChild(vazio);
			return;
		}

		// A POSICAO DE GALAXIA, e nao a do corpo -- ver `MapaEstelar.MinhaPosicaoNaGalaxia`.
		// Pousado, a coordenada do corpo e de superficie e o "X dias daqui" sairia de um ponto
		// que nao existe no mapa.
		Vector2? eu = MapaEstelar.MinhaPosicaoNaGalaxia();
		var titulo = new Label { Text = p.Nome };
		titulo.AddThemeColorOverride("font_color", Tema.Destaque);
		painel.AddChild(titulo);

		var info = new Label
		{
			Text = FichaDoPlaneta(p)
				 + (eu is { } meu
					? $"   \u00b7   {Jandirus.Core.World.Espaco.DiasInGame((new Vector2(p.Pos.X, p.Pos.Y) - meu).Length()):0.0} dias in-game"
					+ $" ({Jandirus.Core.World.Espaco.SegundosDeViagem((new Vector2(p.Pos.X, p.Pos.Y) - meu).Length()) / 60:0} min reais)"
					: ""),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		info.AddThemeColorOverride("font_color", Tema.TextoFraco);
		info.AddThemeFontSizeOverride("font_size", 12);
		painel.AddChild(info);

		var linha = new HBoxContainer();
		var viajar = new Button
		{
			Text = $"Viajar para {p.Nome}",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			// SO NO ESPACO. Viajar e voar entre mundos: em terra firme o piloto automatico andaria
			// contra a parede do planeta. Continua APARECENDO, apagado, porque saber que o destino
			// existe e que falta decolar e informacao -- sumir com o botao seria esconder o jogo.
			Disabled = !noEspaco,
			TooltipText = noEspaco
				? "liga o piloto automatico. Qualquer tecla de movimento desliga."
				: "so no espaco. Use 'Decolar' (aba Other) pra subir.",
		};
		Jandirus.Core.World.PlanetaNoEspaco destino = p;
		viajar.Pressed += () => Viajar(destino, noEspaco);
		linha.AddChild(viajar);

		if (World.Instancia?.DestinoDoPiloto != null)
		{
			var parar = new Button { Text = "Parar", TooltipText = "desliga o piloto automatico" };
			parar.Pressed += () =>
			{
				World.Instancia?.SoltarPiloto();
				Chat.Sistema("piloto automatico desligado.");
			};
			linha.AddChild(parar);
		}
		painel.AddChild(linha);
	}

	/// <summary>
	/// O QUE SE SABE DO MUNDO ANTES DE IR: superficie, bioma e GRAVIDADE.
	///
	/// ============================ A GRAVIDADE NAO E ENFEITE ============================
	/// O `Chronology.dm` e explicito: "O Nav System mostra a gravidade dos planetas antes de
	/// pousar" -- e o motivo e que acima da sua maestria de gravidade o planeta te ESMAGA (anda
	/// devagar, o corpo todo toma dano, muito acima desmaia, absurdamente acima explode). Um
	/// planeta gerado pode ter ate 80x.
	///
	/// Sem este numero na tela, escolher destino no mapa e apostar. Com ele, a carta estelar faz o
	/// que uma carta faz: avisa onde nao se atracar.
	/// ==================================================================================
	///
	/// Os dois lados saem de funcao PURA -- a tabela dos pre-feitos (`planetas.json`, o mesmo
	/// arquivo que o servidor le) e a seed do gerado. Nenhum pacote de rede.
	/// </summary>
	private static string FichaDoPlaneta(Jandirus.Core.World.PlanetaNoEspaco p)
	{
		var zona = p.Premade
			? Jandirus.Core.World.ZoneKey.Premade(p.Nome)
			: Jandirus.Core.World.ZoneKey.Procedural(p.Nome, p.Seed);

		// O CEU DE LA, DAQUI. A hora de cada planeta e funcao pura do relogio do mundo mais a
		// ficha dele, entao a carta consegue dizer "em Namek e dia" sem pedir nada ao servidor --
		// e saber que o destino esta em plena noite de lua cheia e informacao de viagem.
		Jandirus.Core.World.RelogioDoPlaneta r = Planetas.Relogio(zona);
		string ceu = Ceu(r);

		if (!p.Premade)
		{
			Jandirus.Core.World.MundoProcedural m = Jandirus.Core.World.MundoProcedural.DaSeed(p.Seed, p.Nome);
			return $"mundo gerado · {m.Bioma} · gravidade {m.Gravidade:0.##}x · {ceu}";
		}
		double g = Planetas.Catalogo?.De(p.Nome).Gravidade ?? 1;
		return $"mundo com superficie -- da pra pousar · gravidade {g:0.##}x · {ceu}";
	}

	/// <summary>Que horas sao naquele mundo agora, e em que fase a lua dele esta.</summary>
	private static string Ceu(Jandirus.Core.World.RelogioDoPlaneta r)
	{
		if (GameClient.Instance is not { TempoChegou: true } cli)
			return Jandirus.Core.World.Ceu.NomeDoCiclo(r);

		Jandirus.Core.World.EstadoDoCeu e = Jandirus.Core.World.Ceu.De(r, cli.TempoDoMundo);
		string hora = Jandirus.Core.World.Ceu.NomeDaHora(e.Hora);
		return e.LuaNoCeu ? $"{hora}, {Jandirus.Core.World.Ceu.NomeDaFase(e.Fase)}" : hora;
	}

	private void Viajar(Jandirus.Core.World.PlanetaNoEspaco p, bool noEspaco)
	{
		if (!noEspaco) { Chat.Sistema("voce precisa estar no espaco pra viajar."); return; }
		World.Instancia?.Pilotar(new Vector2(p.Pos.X, p.Pos.Y));
		Chat.Sistema($"rumo a {p.Nome}. Qualquer tecla de movimento desliga o piloto.");
		Fechar();
	}

	/// <summary>
	/// VIAJAR PRO PORTO DE UM SISTEMA -- um ponto, e nao um corpo.
	///
	/// O `SistemaSolar.PortoDeEntrada` fica na orbita interna, do lado OPOSTO ao do primeiro mundo,
	/// justamente pra nao coincidir com nada. Ele existe pra quem quer "ir ate ali" antes de
	/// escolher em qual mundo pousar.
	///
	/// AVISO QUE E HONESTO DAR: a chegada e um lugar VAZIO. O corpo mais proximo fica a 900 px ou
	/// mais e a janela de mundo mostra 384x216 px -- quem chegar la vai ver espaco preto e achar
	/// que a viagem falhou. Por isso a mensagem diz o que fazer em seguida.
	/// </summary>
	private void ViajarAoPonto(Vector2 destino, bool noEspaco)
	{
		if (!noEspaco) { Chat.Sistema("voce precisa estar no espaco pra viajar."); return; }
		World.Instancia?.Pilotar(destino);
		Chat.Sistema("rumo ao porto do sistema. A chegada e um ponto vazio -- abra o mapa do sistema "
					 + "(aba Nav) pra escolher o mundo. Qualquer tecla de movimento desliga o piloto.");
		Fechar();
	}

	/// <summary>Distancia em TEMPO, que e como o anime mede: dias in-game e minutos reais.</summary>
	private static string Tempo(double dias, double min) =>
		dias >= 1 ? $"{dias:0.0} dias in-game ({min:0} min reais)"
		: $"{dias * 24:0.0} h in-game ({min:0.0} min reais)";

	// =====================================================================
	// CARGOS -- quem manda no mundo
	// =====================================================================
	/// <summary>
	/// A lista de cargos do MUNDO, com quem ocupa cada um e o que falta pra voce.
	///
	/// Mostra os OCUPADOS tambem: saber quem e o Guardiao da Terra e metade do valor de um
	/// sistema de cargos -- a outra metade e poder disputar quando vagar.
	/// </summary>
	private void AbaCargos()
	{
		if (GameClient.Instance is not { } cli) return;

		if (cli.Cargos.Count == 0)
		{
			Aviso("pedindo a lista ao servidor...");
			cli.SendCargo();   // a lista chega e o painel se redesenha sozinho
			return;
		}

		Secao("Cargos do mundo");
		foreach (GameClient.CargoInfo c in cli.Cargos)
		{
			bool vago = c.Dono.Length == 0;
			bool apto = c.Falta.Length == 0;

			// ============================ O QUE O CARGO E, E O QUE ELE DA ============================
			// As duas linhas que faltavam. O painel mostrava trinta cargos e o jogador nao tinha como
			// saber o que nenhum deles entrega -- nem antes de disputar, nem depois de perder. A
			// descricao vem da `RankDef.Desc` e a dadiva da tabela que o servidor executa de verdade,
			// **com o que ainda e botao mudo marcado** (ver `OQueOCargoEntrega`).
			//
			// ELAS SAIEM PRO CARGO OCUPADO TAMBEM, e nao so pro vago: metade do valor de um sistema de
			// cargos e saber o que o dono atual ganhou com ele.
			void Ficha()
			{
				if (c.Desc.Length > 0) Aviso("      " + c.Desc);
				if (c.Da.Length > 0) Aviso("      dá: " + c.Da);
			}

			if (!vago) { Linha(NomeDoCargo(c.Chave), c.Dono, Tema.Texto); Ficha(); continue; }

			var b = new Button
			{
				Text = $"{NomeDoCargo(c.Chave)}   ·   VAGO" + (apto ? "   ·   reivindicar" : ""),
				Alignment = HorizontalAlignment.Left,
				Disabled = !apto,
				TooltipText = apto ? "voce cumpre os requisitos" : c.Falta,
			};
			string chave = c.Chave;
			b.Pressed += () => cli.SendCargo(chave);
			_conteudo.AddChild(b);
			Ficha();
			if (!apto) Aviso("      exige: " + c.Falta);
		}

		Aviso("\nUm cargo tem UM dono no mundo, e uma alma carrega UM cargo. "
			+ "A escada dos Kaios e a excecao: subir larga o cargo anterior.");
	}

	/// <summary>
	/// O MULTIPLICADOR COMO SE LE. A escala deste jogo vai de 1x a milhoes, e "x345191,1" ocupa a
	/// linha inteira sem dizer mais do que "x345 mil".
	///
	/// ELE TAMBEM E A ASSINATURA DA ABA FORMS (ver <see cref="Assinatura"/>), e isso e de proposito:
	/// a pagina remonta exatamente quando o TEXTO muda, nem antes nem depois. Comparar o double cru
	/// remontaria a aba cinco vezes por segundo enquanto o Ki oscila, sem um pixel mudar.
	/// </summary>
	private static string MultTexto(double m) => m switch
	{
		>= 1e9 => $"×{m / 1e9:0.##} B",
		>= 1e6 => $"×{m / 1e6:0.##} M",
		>= 1000 => $"×{m / 1000:0.##} mil",
		>= 100 => $"×{m:0}",
		>= 10 => $"×{m:0.#}",
		_ => $"×{m:0.##}",
	};

	private static string NomeDoCargo(string chave) =>
		Jandirus.Core.Ranks.Cargos.Get(chave)?.Nome ?? chave;

	// =====================================================================
	// FORMS -- a escada de transformacao
	// =====================================================================
	/// <summary>
	/// O QUE VOCE TEM: a forma de agora e as que ja despertaram. Mais nada.
	///
	/// ============================ A ESCADA FOI EMBORA ============================
	/// Esta aba listava TODOS os degraus, inclusive os travados, com uma faixa de distancia
	/// ("muito longe", "perto", "quase la", "no limiar"). O dono mandou tirar, e as duas razoes
	/// batem com o corte de sigilo que ele pediu na mensagem anterior:
	///
	///   1. A FAIXA ERA O BP DE VOLTA. Ela nascia de `BP / PortaBp`. Cinco faixas contra uma
	///      escada de degraus conhecidos deixam qualquer um binarizar o proprio poder em poucas
	///      sessoes de treino -- e o jogo inteiro acabou de ser arrumado pra que BP so vire
	///      numero com scouter. Esconder o digito e publicar a razao dele e esconder pela metade.
	///   2. LISTA DE DEGRAUS FUTUROS E TABELA DE PROGRESSAO. Saber de antemao que existem sete
	///      degraus acima transforma despertar -- que no anime e um acontecimento -- em barra de
	///      carregamento. O que o personagem sabe das proprias formas e o que ele ja viveu.
	/// =============================================================================
	///
	/// Quem quiser subir aperta C: a tentativa falhando E a informacao, como no original.
	/// </summary>
	private void AbaFormas(SheetState f)
	{
		Jandirus.Core.Forms.FormaDef? defAtual = Jandirus.Core.Forms.Catalogo.PorRede(_atributos.FormaAtual);
		if (defAtual is { Id: "base" }) defAtual = null;
		string atual = defAtual?.Id ?? Jandirus.Core.Forms.Catalogo.IdBase;

		// O LIVRO UMA VEZ SO, e nao um por linha: o `Livro()` remonta o dicionario inteiro a cada
		// chamada, e esta aba redesenha a cada quadro em que algo muda. Ele serve as duas leituras de
		// nome daqui de baixo.
		Jandirus.Core.Forms.Maestrias livro = Livro();

		Secao("Agora");

		// ============================ O NOME SAI DO CATALOGO E NAO DA ENTRADA ============================
		// `Catalogo.NomeDe` e nao `defAtual.Nome`: o Super Saiyajin a 100% de maestria se chama
		// "Super Saiyajin Grade 4" e continua sendo a MESMA forma (ver `Catalogo.DominouOSuperSaiyajin`).
		// Aqui o cliente tem o livro de maestrias do proprio dono da ficha, entao a pergunta se responde
		// sem pedir nada ao servidor.
		// ============================================================================================
		Linha("Forma", defAtual != null ? Jandirus.Core.Forms.Catalogo.NomeDe(defAtual, livro) : "normal",
			  defAtual != null ? Tema.Destaque : Tema.Texto);
		if (defAtual != null)
		{
			// A FORMA DE DISCIPLINA MOSTRA A PROFICIENCIA DA SKILL, e diz o nome dela. Sem isto esta
			// linha marcaria 0,0% pra sempre em quem esta vivendo dentro do Ultra Instinto -- ver
			// `ProficienciaDaForma`.
			if (ProficienciaDaForma(atual) is { } prof)
				Linha($"Proficiência em {prof.Nome}", $"{prof.Pct:0.#}%");
			else
				Linha("Maestria desta forma", $"{Maestria(atual):0.#}%");
			Linha("Dreno de Ki", $"{Jandirus.Core.Forms.Catalogo.DrenoPorSegundo(atual, Livro()) * 100:0.##}% do Ki por segundo");
		}

		// ============================ O MULTIPLICADOR TOTAL ============================
		// DEPOIS do nome da forma de proposito: ele e a CONSEQUENCIA dela, e lido antes viraria um
		// numero solto no topo da aba. As duas linhas juntas contam a historia inteira -- quanto voce
		// esta multiplicado, e quao inteiro voce esta.
		//
		// O TOTAL SAI DA RAZAO `expressedBP / BP`, calculada no servidor, e NAO de multiplicar os
		// fatores um a um. Isso nao e preferencia de estilo: neste jogo os fatores tem tres FAMILIAS
		// (ver o cabecalho de `Fighter.Power.cs`). Medido: Kaio-ken 2x com Mistico 2x da 3x de
		// verdade, porque os dois SOMAM na base -- o produto ingenuo diria 4x, 33% a mais. Com forma,
		// raiva, Kaio-ken, Mistico e gravidade juntos o erro passa de 126%, e o corte de 25% do revive
		// por Zeni nao teria nem onde caber num produto de fatores.
		//
		// POR ISSO NAO HA QUEBRA POR FATOR AQUI. Uma lista "forma 56x · raiva 1,3x · ..." e ilustracao
		// legitima, mas ela so fecha com o total se for desenhada em DOIS blocos (o que soma na base e
		// o que multiplica depois) -- e uma fila de "x" que nao bate com o numero de cima ensina que a
		// tela mente. Enquanto os fatores nao viajarem no pacote, fica o total, que e o honesto.
		//
		// SEM SCOUTER ELES APARECEM DO MESMO JEITO: "x345" nao diz de QUE numero, e sem aparelho o BP e
		// o BP expresso nem chegam ao cliente. E o oposto da faixa de distancia que esta aba tinha, que
		// nascia de `BP / PortaBp` e por isso ENTREGAVA o absoluto contra a escada de degraus conhecida.
		Linha("Multiplicador total", MultTexto(f.MultTotal),
			f.MultTotal > 1.01 ? Tema.Destaque : Tema.Texto);
		Linha("Poder efetivo", $"{f.Inteireza * 100:0.#}%",
			f.Inteireza >= 0.9 ? Tema.Bom : f.Inteireza <= 0.5 ? Tema.Perigo : Tema.Texto);
		Aviso("O multiplicador é o que o seu BP BASE virou agora; o poder efetivo é quanto dele o "
			+ "corpo está conseguindo botar pra fora. Transformar mexe no primeiro e NÃO no segundo "
			+ "-- a forma multiplica os dois lados dessa conta. Quem mexe no segundo é Ki, ferimento, "
			+ "peso, gravidade e idade.");

		Aviso("\nSegure C pra reunir energia  ·  toque C duas vezes pra tentar subir  ·  X volta ao normal.\n"
			+ "Maestria SÓ cresce dentro da forma, gastando Ki -- é o único eixo do jogo que não se compra.\n"
			+ "As formas de uma disciplina divina são exceção: elas não têm maestria própria -- usá-las "
			+ "sobe a proficiência da SKILL, e essa só cresce LUTANDO.");

		// SO O QUE JA DESPERTOU. Maestria > 0 quer dizer que este corpo ja esteve nessa forma
		// alguma vez -- e o unico registro honesto de "eu sei fazer isto".
		//
		// NAS FORMAS DE DISCIPLINA O REGISTRO HONESTO E OUTRO: elas nao guardam maestria nenhuma, e
		// o que prova que o corpo as conhece e a FAIXA de proficiencia que as concedeu ter sido
		// cruzada (`Degrau.Pct`). Sem esta metade elas sumiriam da aba no instante em que o jogador
		// voltasse pra base -- a regra existiria e ninguem veria.
		var minhas = new List<Jandirus.Core.Forms.FormaDef>();
		foreach (Jandirus.Core.Forms.FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
			if (d.Id != Jandirus.Core.Forms.Catalogo.IdBase
				&& (d.Id == atual || Maestria(d.Id) > 0 || DespertouPelaDisciplina(d.Id)))
				minhas.Add(d);

		Secao("Formas que você desperta");

		if (minhas.Count == 0)
		{
			Aviso("Nenhuma, ainda. Nada garante que exista alguma -- e se existir, ela não vem por "
				+ "treino marcado: vem na hora em que vier.");
			return;
		}

		foreach (Jandirus.Core.Forms.FormaDef d in minhas)
		{
			// A MESMA TROCA DA LINHA DE CIMA: forma de disciplina relata a proficiencia da SKILL, e
			// diz que e da skill -- "maestria 0,0%" ao lado de uma forma que o jogador acabou de usar
			// leria como progresso perdido.
			string ficha = ProficienciaDaForma(d.Id) is { } p
				? $"proficiência em {p.Nome} {p.Pct:0.#}%"
				: $"maestria {Maestria(d.Id):0.#}%";

			// MESMO FUNIL DA LINHA "Forma" LA DE CIMA. Sao os dois lugares desta tela que escrevem o
			// nome de uma forma, e escrever `d.Nome` num e `NomeDe` no outro faria a aba dizer
			// "Grade 4" em cima e "Super Saiyajin" tres linhas abaixo, sobre a mesma forma.
			Linha(Jandirus.Core.Forms.Catalogo.NomeDe(d, livro),
				d.Id == atual ? $"EM USO  ·  {ficha}" : ficha,
				d.Id == atual ? Tema.Destaque : Tema.Bom);
		}

		Aviso("\nO que vem depois -- se vier -- você descobre tentando.");
	}

	/// <summary>
	/// A PROFICIENCIA QUE ESTA FORMA RELATA NO LUGAR DA MAESTRIA. Nulo = ela tem maestria propria.
	///
	/// Quem responde "esta forma e de uma disciplina?" e o <see cref="Jandirus.Core.Forms.Disciplinas.DaForma"/>,
	/// o mesmo funil que o servidor usa -- ver o cabecalho dele. Aqui so falta cruzar com a disciplina
	/// que ESTE corpo trilhou, que chega em byte na ficha lenta; os dois caminhos se excluem, entao
	/// uma forma da outra escola nunca casa e mostra 0 (que e a verdade: ela nao e alcancavel).
	/// </summary>
	private (string Nome, double Pct)? ProficienciaDaForma(string forma)
	{
		if (Jandirus.Core.Forms.Disciplinas.DaForma(forma) is not { } par) return null;
		bool minha = _atributos.Disciplina == Jandirus.Core.Forms.Disciplinas.Rede(par.Def.Tipo);
		return (par.Def.Nome, minha ? _atributos.DiscReal : 0);
	}

	/// <summary>
	/// ESTE CORPO JA DESPERTOU ESTA FORMA DE DISCIPLINA? A faixa que a concede foi cruzada.
	///
	/// E o substituto do "maestria > 0" pras quatro formas divinas: elas nao acumulam maestria, entao
	/// o registro de "eu sei fazer isto" e a proficiencia REAL ter passado do <see cref="Jandirus.Core.Forms.Degrau.Pct"/>
	/// da faixa que anuncia a forma (20% pro Sign/Destroyer, 60% pro Perfected/Ultra Ego).
	/// </summary>
	private bool DespertouPelaDisciplina(string forma) =>
		Jandirus.Core.Forms.Disciplinas.DaForma(forma) is { } par
		&& _atributos.Disciplina == Jandirus.Core.Forms.Disciplinas.Rede(par.Def.Tipo)
		&& _atributos.DiscReal >= par.Faixa.Pct;

	private double Maestria(string forma)
	{
		ushort alvo = Jandirus.Core.Forms.Catalogo.Rede(forma);
		foreach ((ushort id, float pct) in _atributos.Maestrias ?? [])
			if (id == alvo) return pct;
		return 0;
	}

	/// <summary>As maestrias num formato que o Core entende, pra calcular dreno e multiplicador.</summary>
	private Jandirus.Core.Forms.Maestrias Livro()
	{
		var m = new Jandirus.Core.Forms.Maestrias();
		foreach ((ushort id, float pct) in _atributos.Maestrias ?? [])
			if (Jandirus.Core.Forms.Catalogo.PorRede(id) is { } d) m.Por(d.Id, pct);
		return m;
	}

	// =====================================================================
	// SKILLS -- a aba inteira (Learning, Skills e a ficha) mora em MenuJogo.Skills.cs
	// =====================================================================

	// =====================================================================
	// VERBS
	// =====================================================================
	private void ListaDeVerbos(string categoria)
	{
		Secao(categoria);
		var lista = Verbos.Da(categoria).ToList();
		if (lista.Count == 0)
		{
			// a mesma frase do original quando a categoria estava vazia
			Aviso("Nenhuma acao aqui ainda.");
			return;
		}
		foreach (Verbo v in lista) Botao(v);
	}

	// =====================================================================
	// ADMIN -- os verbs, mais o que eles nao dao conta de fazer
	// =====================================================================
	/// <summary>
	/// O QUE O ADMIN DIGITOU, guardado FORA da pagina.
	///
	/// A pagina inteira e destruida e remontada quando a assinatura muda (ver
	/// <see cref="Assinatura"/>), e uma <c>LineEdit</c> destruida leva o texto junto. Guardando
	/// aqui, a remontagem devolve o que estava escrito -- que e o que qualquer um espera de um
	/// campo de texto, e o que faz a diferenca entre "digitei e sumiu" e um painel usavel.
	/// </summary>
	private string _avisoDigitado = "", _contaDigitada = "", _textoDoAlvo = "";

	/// <summary>
	/// O CODIGO DA LIMPEZA TOTAL, digitado. Fora da pagina pelo mesmo motivo dos tres acima -- e com
	/// um agravante proprio: aqui o campo tem PRAZO de um minuto, e perder o que foi digitado a cada
	/// remontagem transformaria a confirmacao numa corrida contra o relogio do menu.
	///
	/// Zerado quando chega uma previa nova (ver <see cref="AoLimpeza"/>): codigo de outra lista nao
	/// pode ficar de bobeira num campo que confirma o apagamento do servidor.
	/// </summary>
	private string _codigoDaLimpeza = "";

	/// <summary>Ja pedi a lista de contas nesta sessao? Ver o comentario em <see cref="AbaAdmin"/>.</summary>
	private bool _pediContas;

	/// <summary>O clima escolhido no painel de admin, e a forca dele. Sobrevivem ao redesenho.</summary>
	// Nasce em `Limpo`: quem abre esta aba quase sempre quer TIRAR o clima (ver o Oozaru, que so
	// aparece com a lua livre), nao por chuva. Por chuva e a excecao, e a excecao pode dar um clique.
	private Jandirus.Core.World.TipoDeClima _climaEscolhido = Jandirus.Core.World.TipoDeClima.Limpo;
	private float _forcaEscolhida = 1f;

	/// <summary>
	/// O PAINEL DE CLIMA DA ABA ADMIN -- forcar o ceu pra poder OLHAR o efeito.
	///
	/// ============================ POR QUE ISTO NAO E UM VERB DA LISTA ============================
	/// Os verbs da lista sao botoes sem argumento. Clima pede DOIS (qual e quao forte), e a
	/// alternativa seria um verb por tipo -- onze botoes chamados "Force Rain", "Force Snow"... que
	/// e o tipo de lista que ninguem le. Um seletor com um botao diz a mesma coisa em uma linha, e
	/// e o mesmo caminho que o painel de contas ja usa pra promover por nome.
	/// =============================================================================================
	///
	/// A LISTA MOSTRA O QUE CAI AQUI PRIMEIRO. Todo tipo continua escolhivel (forcar neve em Vampa
	/// e exatamente o tipo de coisa que se quer poder fazer pra conferir o desenho da neve), mas o
	/// que pertence ao planeta vem em cima e sem marca -- e o resto vem marcado com um ponto.
	/// </summary>
	private void PainelDoClima(GameClient cli)
	{
		Secao("Clima deste planeta");

		Jandirus.Core.World.ClimaDoPlaneta daqui = World.Instancia?.ClimaDoLugar
											   ?? Jandirus.Core.World.ClimaDoPlaneta.Nenhum;
		Jandirus.Core.World.EstadoDoClima agora = World.Instancia?.TempoQueFaz ?? default;

		// SEM A PORCENTAGEM AQUI. Ela muda a cada quadro durante a transicao e esta pagina so se
		// remonta quando o TIPO muda -- o numero ficaria congelado mentindo. Quem quer o valor
		// vivo usa o verb "Weather Report", que imprime no chat na hora em que se pede.
		Linha("Agora", agora.Ativo
			? Jandirus.Core.World.Clima.Nome(agora.Tipo) + (agora.Forcado ? " (forcado)" : " (natural)")
			: "ceu limpo");

		if (!daqui.Existe)
			Aviso("Este lugar nao tem clima proprio (o `HasWeather=0` do DM) -- mas forcar funciona.");

		// ---- o seletor ----
		var escolha = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		var ordem = new List<Jandirus.Core.World.TipoDeClima>();
		foreach (Jandirus.Core.World.TipoDeClima t in Enum.GetValues<Jandirus.Core.World.TipoDeClima>())
		{
			// ============================ "LIMPO" E UM CLIMA, NAO O BOTAO DE SOLTAR ============================
			// Isto pulava o `Limpo` com o argumento de que "limpo e o botao de soltar". Nao e: soltar
			// devolve o ceu ao SORTEIO -- ele pode cair em chuva no quadro seguinte. Forcar limpo e
			// outra coisa: e cravar que nao ha clima nenhum e ele FICA assim.
			//
			// O dono precisou disso e nao tinha: "kd a opçao q pedi de clima limpo, limpar qualquer
			// clima e deixar sem efeito de clima pra deixar a lua livre". Sem forcar limpo, testar o
			// Oozaru dependia de o sorteio colaborar -- e o sorteio nao colabora sob demanda.
			// ================================================================================================
			ordem.Add(t);
		}
		ordem.Sort((a, b) =>
		{
			bool na = Array.IndexOf(daqui.Permitidos, a) >= 0, nb = Array.IndexOf(daqui.Permitidos, b) >= 0;
			return na == nb ? string.CompareOrdinal(a.ToString(), b.ToString()) : na ? -1 : 1;
		});

		for (int i = 0; i < ordem.Count; i++)
		{
			bool nativo = Array.IndexOf(daqui.Permitidos, ordem[i]) >= 0;
			escolha.AddItem(Jandirus.Core.World.Clima.Nome(ordem[i]) + (nativo ? "" : "  ·"), i);
			if (ordem[i] == _climaEscolhido) escolha.Selected = i;
		}
		escolha.TooltipText = "os marcados com · nao caem aqui naturalmente -- forcar continua valendo";
		escolha.ItemSelected += i => _climaEscolhido = ordem[(int)i];

		var linha = new HBoxContainer();
		linha.AddChild(escolha);

		// ---- a forca ----
		var forca = new HSlider
		{
			MinValue = 10, MaxValue = 100, Step = 5, Value = _forcaEscolhida * 100,
			CustomMinimumSize = new Vector2(120, 0),
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			TooltipText = "quao forte: garoa a temporal",
		};
		var rotuloForca = new Label { Text = $"{_forcaEscolhida:P0}", CustomMinimumSize = new Vector2(46, 0) };
		forca.ValueChanged += v =>
		{
			_forcaEscolhida = (float)v / 100f;
			rotuloForca.Text = $"{_forcaEscolhida:P0}";
		};
		linha.AddChild(forca);
		linha.AddChild(rotuloForca);

		var bForcar = new Button
		{
			Text = "Forcar",
			TooltipText = "poe este clima nesta zona por 20 min. Vale pra todo mundo que esta aqui.",
		};
		// A FORCA VAI NO MESMO ARGUMENTO, depois de uma barra -- o canal de verb leva UMA string, e
		// e o mesmo formato que `admin_marco` ja usa ("id|quantos").
		bForcar.Pressed += () => cli.SendVerbo("admin_clima",
			$"{_climaEscolhido}|{_forcaEscolhida.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
		linha.AddChild(bForcar);

		var bSoltar = new Button
		{
			Text = "Voltar ao natural",
			Disabled = !agora.Forcado,
			TooltipText = "solta o ceu: o clima volta a ser sorteado pelo relogio do mundo",
		};
		bSoltar.Pressed += () => cli.SendVerbo("admin_clima_natural");
		linha.AddChild(bSoltar);

		// ============================ O CEU DO OOZARU ============================
		// Fica NESTA linha, ao lado do clima, porque as duas coisas sao a mesma pergunta pro
		// testador: "por que o botao de olhar pra lua nao aparece?". A resposta e sempre uma das
		// duas -- ou nao e lua cheia, ou o ceu esta encoberto -- e ter os dois botoes juntos
		// responde as duas sem o testador precisar saber qual era.
		// ====================================================================
		var bLua = new Button
		{
			Text = "Lua cheia agora",
			TooltipText = "adianta o relogio do mundo ate a proxima noite de lua cheia NESTA zona "
						+ "(o clima continua como esta -- se estiver encoberta, limpe ao lado)",
		};
		bLua.Pressed += () => cli.SendVerbo("admin_lua_cheia");
		linha.AddChild(bLua);

		// O IRMAO DELE, e ele fica aqui pelo mesmo argumento do bloco acima: a pergunta do testador
		// tambem e sobre o ceu, so que a mais banal ("por que nao da pra ver nada?"). Um dia inteiro
		// dura 24 minutos, entao metade das rodadas cai no escuro -- e julgar cor de cabelo no escuro
		// foi exatamente o que aconteceu na bancada `--diagolhada`. Ver `GameServer.AdminMeioDia`.
		var bDia = new Button
		{
			Text = "Meio-dia agora",
			TooltipText = "adianta o relogio do mundo ate o proximo meio-dia NESTA zona -- pra julgar "
						+ "cor (o clima continua como esta: meio-dia sob tempestade tambem e escuro)",
		};
		bDia.Pressed += () => cli.SendVerbo("admin_meio_dia");
		linha.AddChild(bDia);

		_conteudo.AddChild(linha);

		Aviso(daqui.Existe && daqui.Permitidos.Length > 0
			? "Cai aqui naturalmente: " + string.Join(", ",
				daqui.Permitidos.Select(Jandirus.Core.World.Clima.Nome))
			: "");
	}

	/// <summary>
	/// O NOME DA ESCADA em portugues, pros cabecalhos do painel de formas.
	///
	/// ============================ POR QUE O `_ =>` DEVOLVE O NOME DO ENUM ============================
	/// Porque linha nova tem que APARECER no painel no mesmo dia em que entra no Core, nem que seja
	/// com o nome cru ("LegendaryPrimal"). Um `_ => ""` -- ou pior, um dicionario que so contivesse as
	/// dez de hoje -- daria um cabecalho vazio (ou uma excecao) e as formas da linha nova ficariam
	/// penduradas em lugar nenhum. Feio e visivel vence bonito e ausente numa ferramenta de teste.
	/// ==============================================================================================
	/// </summary>
	private static string NomeDaLinha(Jandirus.Core.Forms.LinhaDeForma l) => l switch
	{
		Jandirus.Core.Forms.LinhaDeForma.Saiyajin => "Saiyajin",
		Jandirus.Core.Forms.LinhaDeForma.Futuro => "Futuro",
		Jandirus.Core.Forms.LinhaDeForma.Legendary => "Legendary",
		Jandirus.Core.Forms.LinhaDeForma.LegendaryPrimal => "Legendary Primal",
		Jandirus.Core.Forms.LinhaDeForma.GodKi => "Ki divino",
		Jandirus.Core.Forms.LinhaDeForma.GodKiRose => "Ki divino · Rose",
		Jandirus.Core.Forms.LinhaDeForma.Mistico => "Mistico",
		Jandirus.Core.Forms.LinhaDeForma.UltraInstinct => "Ultra Instinto",
		Jandirus.Core.Forms.LinhaDeForma.UltraEgo => "Ultra Ego",
		Jandirus.Core.Forms.LinhaDeForma.Oozaru => "Oozaru (fera)",
		// AS QUATRO LINHAS RACIAIS. O `_ =>` ja as mostrava com o nome cru do enum ("FrostDemon"),
		// que e o que este metodo promete no cabecalho -- mas "cru" era pra ser o estado do PRIMEIRO
		// dia da linha, e nao o definitivo.
		Jandirus.Core.Forms.LinhaDeForma.FrostDemon => "Frost Demon",
		Jandirus.Core.Forms.LinhaDeForma.Namekuseijin => "Namekuseijin",
		Jandirus.Core.Forms.LinhaDeForma.Heran => "Heran",
		Jandirus.Core.Forms.LinhaDeForma.Alien => "Alien",
		_ => l.ToString(),
	};

	/// <summary>
	/// FORCAR QUALQUER FORMA, E SOLTAR O KI -- as duas ferramentas de teste que o dono pediu
	/// ("forçar me transformar em qualquer transformaçao do jogo pra eu testar, assim como uma opçao
	/// de liberar as skills de ki pra eu ja poder usar aura etc").
	///
	/// ============================ A LISTA SAI DO CATALOGO, NUNCA DA MAO ============================
	/// Um botao por entrada de <see cref="Jandirus.Core.Forms.Catalogo.Todas"/>. E a mesma razao de o
	/// catalogo existir: "uma forma nova e uma entrada nova aqui e mais nada". Uma lista escrita a mao
	/// aqui seria a 37a coisa a lembrar de atualizar, e a que ninguem lembraria -- a forma nova
	/// simplesmente nao teria como ser testada, calada. O rotulo e o `Nome` e a dica de mouse e o
	/// `Desc`; os dois ja estao no dado.
	///
	/// ============================ POR QUE BOTAO E NAO UM SELETOR ============================
	/// O painel de clima ao lado usa <c>OptionButton</c> porque clima pede DOIS argumentos (qual e quao
	/// forte) -- escolher e so metade do gesto. Forma pede UM, e um seletor transformaria "quero ver o
	/// Blue" em abrir-rolar-escolher-fechar-clicar. Com botao e um clique. Sao 35 deles, e e por isso
	/// que vem AGRUPADOS: 35 numa fila e uma lista que ninguem le.
	///
	/// ============================ O AGRUPAMENTO E O MESMO DO SERVIDOR ============================
	/// Por <see cref="Jandirus.Core.Forms.LinhaDeForma"/>, e dentro dela por `Ordem` -- identico ao
	/// `ListarFormas` que responde ao `admin_forma` sem argumento. A pergunta que o admin faz e sempre
	/// "quais sao os degraus da escada X", e as duas respostas do jogo (a do chat e a da aba) dizerem a
	/// mesma coisa na mesma ordem e o que impede uma de parecer errada.
	///
	/// O `GroupBy` respeita a ordem de DECLARACAO do catalogo, que ja e a ordem em que as linhas fazem
	/// sentido de ler (Saiyajin primeiro, Oozaru por ultimo). Nao ha sort de linha aqui de proposito:
	/// alfabetico jogaria "Futuro" antes de "Saiyajin", e o catalogo e quem sabe a ordem boa.
	/// =============================================================================================
	/// </summary>
	private void PainelDeFormas(GameClient cli)
	{
		Secao("Forcar transformacao");

		// ---------------------------------------------------------- em QUEM cai
		// "alvoId|forma", o mesmo formato do `admin_skill_dar` -- e o `PorNome` do servidor devolve
		// nulo pro id 0, entao SEM alvo marcado o verb cai em quem clicou. Nao ha estado novo aqui:
		// a caixa de "aplicar no alvo" seria um campo a manter sincronizado com uma verdade que o
		// `cli.AlvoId` ja carrega. O rotulo abaixo so LE essa verdade -- e como `AlvoId` esta na
		// assinatura da aba (ver `Assinatura`), marcar alguem remonta a pagina e o rotulo acompanha.
		bool noAlvo = cli.AlvoId != 0;
		Aviso(noAlvo
			? "Cai no CORPO MARCADO (o do duplo clique), e nao em voce."
			: "Cai em VOCE. Marque alguem com duplo clique pra empurrar outro corpo.");

		Jandirus.Core.Forms.FormaDef? defAtual =
			Jandirus.Core.Forms.Catalogo.PorRede(_atributos.FormaAtual);
		// A MARCA SO VALE PRA MIM: `FormaAtual` e a MINHA ficha. Com alvo marcado, o "em uso" seria
		// uma mentira sobre o corpo errado -- entao ela some.
		string atual = noAlvo ? "" : defAtual?.Id ?? Jandirus.Core.Forms.Catalogo.IdBase;

		// ---------------------------------------------------------- a linha de cima
		var topo = new HBoxContainer();

		var bBase = new Button
		{
			Text = "Voltar ao normal",
			TooltipText = "forca a forma base -- desfaz TAMBEM o Oozaru, que e um estado paralelo "
						+ "a escada e nao sai com o X",
		};
		bBase.Pressed += () => cli.SendVerbo("admin_forma",
			$"{cli.AlvoId}|{Jandirus.Core.Forms.Catalogo.IdBase}");
		topo.AddChild(bBase);

		var bKi = new Button
		{
			Text = "Liberar skills de Ki",
			TooltipText = "da o Basic Ki Control e o degrau que acende o canPower: segurar C passa a "
						+ "carregar (aura), e os verbs Power Control e Conceal Power aparecem",
		};
		bKi.Pressed += () => cli.SendVerbo("admin_liberar_ki", cli.AlvoId.ToString());
		topo.AddChild(bKi);

		_conteudo.AddChild(topo);

		// ---------------------------------------------------------- uma faixa por escada
		foreach (var linha in Jandirus.Core.Forms.Catalogo.Todas
			.Where(d => d.Id != Jandirus.Core.Forms.Catalogo.IdBase)
			.GroupBy(d => d.Linha))
		{
			var faixa = new HBoxContainer();

			var rotulo = new Label
			{
				Text = NomeDaLinha(linha.Key),
				CustomMinimumSize = new Vector2(140, 0),
				VerticalAlignment = VerticalAlignment.Center,
			};
			rotulo.AddThemeColorOverride("font_color", Tema.TextoFraco);
			rotulo.AddThemeFontSizeOverride("font_size", 12);
			faixa.AddChild(rotulo);

			// HFlow E NAO HBox: a linha Futuro tem dez degraus de nome comprido ("Future Super
			// Saiyajin 10") e o painel nao rola na horizontal (`HorizontalScrollMode.Disabled`) --
			// numa HBox os ultimos botoes ficariam espremidos a zero e sem como clicar.
			var botoes = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

			foreach (Jandirus.Core.Forms.FormaDef d in linha.OrderBy(d => d.Ordem))
			{
				bool emUso = d.Id == atual;
				var b = new Button
				{
					// ============================ AQUI E `d.Nome` CRU, E DE PROPOSITO ============================
					// A aba Formas passa pelo `Catalogo.NomeDe` porque ela fala da ficha de quem esta
					// olhando. Este painel nao: cada botao age sobre `cli.AlvoId`, que pode ser OUTRO
					// jogador -- e a maestria dele nao chega neste cliente (ver `Catalogo.NomeDe`, e o
					// motivo de o `S2C.Forma` carregar um bit e nao um numero). Nomear o botao pela
					// maestria do ADMIN seria escrever na tela um fato sobre a pessoa errada.
					//
					// Entao o que este painel mostra e a ENTRADA do catalogo, que e o que ele manipula:
					// "Super Saiyajin" e a forma, e o "Grade 4" e um estado dela que so o dono da barra
					// tem. Foi por isto que a linha Legendary foi resolvida por RENOME de entrada -- ver
					// o cabecalho da forma `legendary` em `Formas.cs`.
					// ========================================================================================
					Text = (emUso ? "● " : "") + d.Nome,
					// A DICA E O `Desc` DO CATALOGO, palavra por palavra. Escrever outra aqui seria uma
					// segunda descricao da mesma forma pra envelhecer sozinha.
					TooltipText = d.Desc,
				};
				b.AddThemeFontSizeOverride("font_size", 12);
				if (emUso) b.AddThemeColorOverride("font_color", Tema.Destaque);

				// COPIA LOCAL do id: o que a lambda leva pro clique tem que ser o id DESTE botao, e nao
				// o que a variavel do laco calhar de valer depois. (Em C# moderno a variavel de
				// `foreach` ja e por iteracao -- a copia e pra deixar visivel que a captura e de uma
				// STRING e do `cli`, e de mais nada: nada aqui prende o menu vivo depois do relog.)
				string id = d.Id;
				b.Pressed += () => cli.SendVerbo("admin_forma", $"{cli.AlvoId}|{id}");
				botoes.AddChild(b);
			}

			faixa.AddChild(botoes);
			_conteudo.AddChild(faixa);
		}

		Aviso("Ignora BP, maestria, linhagem, classe, degrau anterior, raca e Ki -- as formas que "
			+ "interessa testar SAO as trancadas. O Ki entra cheio (senao o tique derrubaria a forma "
			+ "antes de voce olhar) e a cinematica toca pela regra normal: cheia na estreia deste "
			+ "corpo, encurtada ate 50% de maestria, instantanea depois.\n"
			+ "A fera nao aparece marcada mesmo quando esta ativa: o estado dela e paralelo a escada "
			+ "e nao viaja no campo de forma. Ela precisa de RABO INTEIRO pra durar mais que um tique.");
	}

	/// <summary>
	/// A ABA DE ADMIN.
	///
	/// ============================ POR QUE ELA NAO E SO A LISTA DE VERBS ============================
	/// A maioria dos verbs de admin do original age sobre alguem que esta na tela, e pra esses o
	/// alvo marcado por duplo clique resolve (e melhor que o `input()` bloqueante do BYOND: nome
	/// se repete e nome se digita errado).
	///
	/// Dois nao cabem nesse molde:
	///   * ANUNCIAR pede um TEXTO, que nenhum botao carrega.
	///   * PROMOVER pede uma CONTA, e a conta que o dono quer promover costuma estar OFFLINE --
	///     nao ha o que marcar com duplo clique. Foi exatamente o pedido: "permita o adm dar
	///     administrador a um dos perfis criados no server".
	///
	/// Por isso o painel: um campo de aviso, a lista de contas do servidor com o que cada uma e, e
	/// so entao os verbs.
	/// ==============================================================================================
	/// </summary>
	private void AbaAdmin()
	{
		if (GameClient.Instance is not { } cli) { ListaDeVerbos(Verbos.Admin); return; }

		// ------------------------------------------------- anunciar
		Secao("Aviso ao servidor");
		var aviso = new LineEdit
		{
			PlaceholderText = "escreva e aperte Enter (ou o botao)",
			Text = _avisoDigitado,
			// O TETO E O DO FIO, e nao um gosto de interface: o argumento de um verb cabe em
			// `Protocol.MaxArgDeVerbo` bytes, e acima disso o leitor do servidor devolve string
			// VAZIA em vez de truncar. Sem este limite, o aviso longo sumia e o servidor
			// respondia "escreva o aviso antes" -- acusando quem tinha escrito.
			MaxLength = Protocol.MaxArgDeVerbo,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		aviso.TextChanged += t => _avisoDigitado = t;
		void Anunciar()
		{
			if (_avisoDigitado.Trim().Length == 0) return;
			cli.SendVerbo("admin_anunciar", _avisoDigitado.Trim());
			_avisoDigitado = "";
			aviso.Text = "";
		}
		aviso.TextSubmitted += _ => Anunciar();

		var linhaAviso = new HBoxContainer();
		linhaAviso.AddChild(aviso);
		var bAviso = new Button { Text = "Anunciar", TooltipText = "o `Announce` do original: uma linha pra todo mundo" };
		bAviso.Pressed += Anunciar;
		linhaAviso.AddChild(bAviso);
		_conteudo.AddChild(linhaAviso);

		// ------------------------------------------------- clima
		PainelDoClima(cli);

		// ------------------------------------------------- formas e Ki
		// LOGO DEPOIS DO CLIMA de proposito: as tres coisas respondem a mesma pergunta de testador
		// ("por que nao consigo ver isto?"). Lua cheia e ceu limpo destravam o Oozaru; forcar forma
		// destrava o resto; liberar Ki destrava a aura. Juntas, elas sao o kit de olhar o jogo.
		PainelDeFormas(cli);

		// ------------------------------------------------- contas
		Secao("Contas deste servidor");
		if (cli.Contas.Count == 0)
		{
			Aviso(_pediContas ? "sem contas pra mostrar." : "pedindo a lista ao servidor...");
			// UMA VEZ SO, e nao a cada remontagem. Se o servidor devolvesse uma lista VAZIA (um
			// servidor sem armazenamento em disco, por exemplo), pedir dentro do desenho viraria
			// um pedido a cada pacote de ficha -- cinco varreduras da pasta de contas por segundo,
			// pra sempre. O botao "atualizar lista" continua sendo a saida manual.
			if (!_pediContas) { _pediContas = true; cli.SendVerbo("admin_contas"); }
		}
		else
		{
			foreach (GameClient.ContaInfo a in cli.Contas)
			{
				var h = new HBoxContainer();

				var nome = new Label
				{
					Text = (a.Online ? "● " : "○ ") + a.Conta
						 + (a.Admin ? "   [admin]" : "") + (a.Banida ? "   [banida]" : ""),
					SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
					TooltipText = a.Personagens.Length > 0 ? a.Personagens : "sem personagens",
				};
				nome.AddThemeColorOverride("font_color",
					a.Banida ? Tema.Perigo : a.Admin ? Tema.Destaque : Tema.Texto);
				nome.AddThemeFontSizeOverride("font_size", 13);
				h.AddChild(nome);

				string conta = a.Conta;
				bool eraAdmin = a.Admin, eraBanida = a.Banida;

				var bAdm = new Button
				{
					Text = eraAdmin ? "rebaixar" : "promover",
					TooltipText = eraAdmin
						? "tira o admin desta conta (o `AdminDemote` do original)"
						: "da admin a esta conta -- vale pros tres personagens dela",
				};
				bAdm.Pressed += () => cli.SendVerbo(eraAdmin ? "admin_rebaixar" : "admin_promover", conta);
				h.AddChild(bAdm);

				var bBan = new Button
				{
					Text = eraBanida ? "perdoar" : "banir",
					Disabled = eraAdmin,   // rebaixe antes: o servidor recusaria de qualquer jeito
					TooltipText = eraAdmin ? "rebaixe antes de banir um administrador" : "o `Ban` do original",
				};
				bBan.Pressed += () => cli.SendVerbo(eraBanida ? "admin_perdoar" : "admin_banir", conta);
				h.AddChild(bBan);

				_conteudo.AddChild(h);
				if (a.Personagens.Length > 0) Aviso("      " + a.Personagens);
			}
		}

		// PELO NOME, pra quem nao esta na lista (conta recem-criada, ou o nome de um PERSONAGEM,
		// que e o que o admin ve andando na tela e nem sempre e igual ao da conta).
		var porNome = new LineEdit
		{
			PlaceholderText = "conta ou nome de personagem",
			Text = _contaDigitada,
			MaxLength = 32,   // o mesmo teto que o `Login` do servidor aplica ao nome da conta
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		porNome.TextChanged += t => _contaDigitada = t;

		var linhaNome = new HBoxContainer();
		linhaNome.AddChild(porNome);
		foreach ((string rotulo, string cmd) in new[]
		{
			("promover", "admin_promover"), ("rebaixar", "admin_rebaixar"),
			("banir", "admin_banir"), ("perdoar", "admin_perdoar"),
		})
		{
			var b = new Button { Text = rotulo };
			string comando = cmd;
			b.Pressed += () =>
			{
				if (_contaDigitada.Trim().Length == 0) { Chat.Sistema("escreva a conta ou o personagem."); return; }
				cli.SendVerbo(comando, _contaDigitada.Trim());
			};
			linhaNome.AddChild(b);
		}
		_conteudo.AddChild(linhaNome);

		var atualizar = new Button { Text = "atualizar lista", Alignment = HorizontalAlignment.Left };
		atualizar.Pressed += () => cli.SendVerbo("admin_contas");
		_conteudo.AddChild(atualizar);

		// ------------------------------------------------- o que precisa de alvo E de texto
		// Sao os quatro verbs do original que abriam um `input()` DEPOIS de escolher o mob:
		// `Give (Skill)`, `Take_Skill`, `Give (Rank)` e `cmd_admin_pm`. O alvo vem do duplo clique;
		// o texto, daqui.
		Secao("Sobre o alvo marcado");
		bool temAlvo = cli.AlvoId != 0;
		Aviso(temAlvo
			? "o alvo esta marcado. Escreva ao lado o nome da skill, a chave do cargo, ou a mensagem."
			: "marque alguem com DUPLO CLIQUE pra usar os botoes abaixo.");

		var noAlvo = new LineEdit
		{
			PlaceholderText = "nome da skill / chave do cargo / mensagem",
			Text = _textoDoAlvo,
			// menos o "<id>|" que vai na frente, com folga pra um id de cinco digitos
			MaxLength = Protocol.MaxArgDeVerbo - 8,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		noAlvo.TextChanged += t => _textoDoAlvo = t;

		var linhaAlvo = new HBoxContainer();
		linhaAlvo.AddChild(noAlvo);
		foreach ((string rotulo, string cmd, string dica) in new[]
		{
			("dar skill", "admin_skill_dar", "o `Give (Skill)` do original: por nome ou typepath"),
			("tirar skill", "admin_skill_tirar", "o `Take_Skill`: desfaz um presente errado"),
			("dar cargo", "admin_cargo_dar", "o `Give (Rank)`: poe no trono ignorando os requisitos"),
			// DESTITUIR NAO OLHA O ALVO: destitui-se um TRONO pela chave escrita ao lado, e o dono
			// dele costuma estar offline -- que e o motivo mais comum de precisar disto. O botao mora
			// nesta linha porque e aqui que existe o campo de texto.
			("tirar cargo", "admin_cargo_tirar", "esvazia o trono da chave escrita ao lado (o alvo marcado e ignorado)"),
			("PM", "admin_pm", "mensagem particular. Os outros admins recebem copia"),
		})
		{
			var b = new Button { Text = rotulo, Disabled = !temAlvo, TooltipText = dica };
			string comando = cmd;
			b.Pressed += () =>
			{
				if (_textoDoAlvo.Trim().Length == 0) { Chat.Sistema("escreva o texto antes."); return; }
				// "alvoId|texto" -- o canal de verbs carrega UM argumento so, e este e o formato que
				// o resto do painel ja usa (ver `admin_marco`).
				cli.SendVerbo(comando, $"{cli.AlvoId}|{_textoDoAlvo.Trim()}");
			};
			linhaAlvo.AddChild(b);
		}
		_conteudo.AddChild(linhaAlvo);

		// ------------------------------------------------- os verbs
		ListaDeVerbos(Verbos.Admin);
		Aviso("\nOs verbs marcados com \"Target\" agem sobre quem voce marcou com DUPLO CLIQUE. "
			+ "A permissao e conferida de novo no servidor -- esconder botao nunca foi permissao.");

		// ------------------------------------------------- a zona de perigo
		// POR ULTIMO, e isso e a unica coisa que a posicao dela pode fazer de util: e preciso
		// ROLAR ATE O FIM da aba mais longa do jogo pra ver o botao que apaga o servidor. Nao e
		// seguranca (a seguranca sao os dois passos e o codigo), e afastar a mao.
		PainelDePerigo(cli);
	}

	/// <summary>
	/// A LIMPEZA TOTAL DO SERVIDOR -- o pedido do dono: *"um verb pra adm no menu P q LIMPA O SERVER
	/// TODO... dando uma limpa total como se tivesse acabado de rodar pela primeira vez"*.
	///
	/// ============================ UM CLIQUE NAO PODE BASTAR, E AQUI SAO DOIS PASSOS ============================
	/// Isto apaga contas, personagens, construcoes, naves, dominios, tronos, discipulados e sagas --
	/// e nao volta. A confirmacao tem que ser dificil de dar POR ACIDENTE (e nao dificil de dar):
	///
	///   1. "Preparar limpeza" nao apaga nada. Ele pede ao servidor o INVENTARIO -- quantas contas,
	///      quantas obras, quantos planetas dominados, quantos jogadores online -- e um codigo de
	///      quatro caracteres sorteado na hora, que vale por um minuto.
	///   2. So depois de LER a lista e DIGITAR esse codigo o segundo botao acende.
	///
	/// O codigo e sorteado no servidor e nunca existiu antes desta tela: nao da pra decorar, nao da
	/// pra colar de um comando de ontem, e um painel esquecido aberto vence sozinho. E como a lista
	/// e recontada a cada previa, quem confirma esta confirmando o numero que esta vendo.
	///
	/// A PERMISSAO NAO ESTA AQUI. Este painel so aparece pra quem tem a aba, e a aba e escondida de
	/// quem nao e admin -- mas os dois verbs sao conferidos de novo no servidor, pelo mesmo funil de
	/// admin de todos os outros. Esconder botao nunca foi permissao.
	/// ========================================================================================================
	/// </summary>
	private void PainelDePerigo(GameClient cli)
	{
		Secao("ZONA DE PERIGO");

		GameClient.PreviaDeLimpeza p = cli.Limpeza;

		if (p.Codigo.Length == 0)
		{
			Aviso("Limpar o servidor apaga TUDO o que foi jogado aqui: contas e personagens, "
				+ "construcoes, naves, planetas dominados, cargos, discipulados e o andamento das "
				+ "sagas. O mundo volta a ser o que era no primeiro boot. Nao ha como desfazer.\n"
				+ "O botao abaixo NAO apaga nada -- ele mostra a lista do que sumiria, com contagem.");

			var preparar = new Button
			{
				Text = "Preparar limpeza total do servidor...",
				Alignment = HorizontalAlignment.Left,
				TooltipText = "passo 1 de 2: pede ao servidor o inventario do que existe hoje. "
							+ "Nada e apagado por este botao",
			};
			preparar.AddThemeColorOverride("font_color", Tema.Perigo);
			preparar.Pressed += () => cli.SendVerbo("admin_limpar");
			_conteudo.AddChild(preparar);
			return;
		}

		// ---------------------------------------------------------- a previa chegou
		var titulo = new Label
		{
			Text = "ISTO VAI SUMIR PARA SEMPRE:",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		titulo.AddThemeColorOverride("font_color", Tema.Perigo);
		_conteudo.AddChild(titulo);

		// UMA LINHA POR SISTEMA, com o numero na frente. E o que o dono pediu em voz alta: "quem
		// confirma tem que saber o tamanho do que esta fazendo" -- e tamanho e numero, nao adjetivo.
		foreach (string l in p.Linhas)
		{
			var item = new Label { Text = "   " + l, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			item.AddThemeColorOverride("font_color", Tema.Texto);
			item.AddThemeFontSizeOverride("font_size", 13);
			_conteudo.AddChild(item);
		}

		Aviso($"\nPara confirmar, digite o codigo abaixo. Ele vale por {p.Segundos} segundos e nao "
			+ "serve pra mais nada depois disso.");

		var codigo = new Label
		{
			Text = p.Codigo,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		codigo.AddThemeColorOverride("font_color", Tema.Perigo);
		codigo.AddThemeFontSizeOverride("font_size", 28);
		_conteudo.AddChild(codigo);

		var campo = new LineEdit
		{
			PlaceholderText = "digite o codigo acima",
			Text = _codigoDaLimpeza,
			MaxLength = 16,
			Alignment = HorizontalAlignment.Center,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};

		var confirmar = new Button
		{
			Text = "APAGAR O SERVIDOR",
			// COMECA DESLIGADO e so acende com o codigo certo digitado. Nao e a guarda de verdade
			// (o servidor confere de novo, e e la que a decisao vale) -- e o que impede o clique
			// reflexo de quem nem leu a lista.
			Disabled = !string.Equals(_codigoDaLimpeza.Trim(), p.Codigo, StringComparison.OrdinalIgnoreCase),
			TooltipText = "passo 2 de 2: derruba todo mundo SEM salvar e apaga a pasta de saves",
		};
		confirmar.AddThemeColorOverride("font_color", Tema.Perigo);

		// O BOTAO SO LIGA/DESLIGA, e a pagina NAO se remonta a cada tecla: remontar recriaria a
		// `LineEdit` (e o foco, e o cursor) no meio da digitacao. Por isso o `TextChanged` mexe no
		// `Disabled` do botao na mao em vez de chamar `Redesenhar`.
		campo.TextChanged += t =>
		{
			_codigoDaLimpeza = t;
			confirmar.Disabled = !string.Equals(t.Trim(), p.Codigo, StringComparison.OrdinalIgnoreCase);
		};

		void Apagar()
		{
			if (confirmar.Disabled) return;
			cli.SendVerbo("admin_limpar_ja", _codigoDaLimpeza.Trim());
			_codigoDaLimpeza = "";
		}
		campo.TextSubmitted += _ => Apagar();
		confirmar.Pressed += Apagar;

		var linha = new HBoxContainer();
		linha.AddChild(campo);
		linha.AddChild(confirmar);
		_conteudo.AddChild(linha);

		var desistir = new Button { Text = "cancelar", Alignment = HorizontalAlignment.Left };
		// CANCELAR E PEDIR A PREVIA DE NOVO? Nao: e so esquecer o codigo daqui. O do servidor vence
		// sozinho em um minuto, e um verb novo pra "cancelar" seria um caminho a mais pra manter --
		// com o unico efeito de encurtar um prazo que ja e curto.
		desistir.Pressed += () => { _codigoDaLimpeza = ""; cli.EsquecerLimpeza(); };
		_conteudo.AddChild(desistir);
	}

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
