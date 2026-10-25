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
/// E NAO ALCANCA PORTA QUE ABRE POR ENCOSTAR: trocar isso por um menu de duas opcoes seria piorar um
/// gesto que ja esta bom. A excecao e a porta ERGUIDA com senha (e a porta de quem a ergueu) -- ver
/// `Interacoes.DaPorta`.
/// ===========================================================================
/// </summary>
public partial class MenuDeInteracao : CanvasLayer
{
	/// <summary>
	/// A QUE DISTANCIA A COISA RESPONDE, em pixels -- e o numero E DO CORE.
	///
	/// Havia uma copia de 64 aqui e outra no servidor; elas concordavam por acaso, e uma TERCEIRA (a
	/// do console da ponte) divergia em 16 px -- um menu que abria e um verbo que falhava, que e
	/// exatamente o que este comentario dizia que nao podia acontecer. Hoje ha um numero so:
	/// <see cref="Interacoes.Alcance"/>, e a historia inteira esta escrita la.
	/// </summary>
	private const float Alcance = Interacoes.Alcance;

	private Control _raiz = null!;
	private VBoxContainer _lista = null!;
	private Label _titulo = null!;
	private Label _dica = null!;

	// O `_alvo` (o ID da obra aberta) FOI DELETADO: ele so servia pra reachar o nome do alvo na lista
	// de construcoes, e o alvo passou a poder ser um VEICULO, que nao esta nessa lista. Quem responde
	// pelo titulo agora e `_tipoDoAlvo`, que vale pros dois casos -- e o id nao tinha segundo uso.

	/// <summary>O menu vivo, ou nulo antes do mundo. SO PRA BANCADA (`--diagembarque`).</summary>
	public static MenuDeInteracao? Instancia { get; private set; }

	public override void _Ready()
	{
		Layer = 4;   // acima do menu do jogo (3), abaixo do quick time event (5)
		Instancia = this;
		Montar();
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
		// O `-=` DO PAR: o menu pode morrer (a volta ao menu principal) e o `GameClient` nao.
		if (_ouvindoEscolhas is { } cli) cli.EscolhasChegaram -= AoChegaremEscolhas;
		_ouvindoEscolhas = null;
	}

	/// <summary>(v0.3.2) A pergunta de `Forma.Lista` aberta agora -- nula fora da pagina de escolhas.</summary>
	private Interacoes.Acao? _perguntando;

	/// <summary>(v0.3.2) Quando a pergunta saiu: lista de ANTES dela e de outra pergunta, e a pagina segue consultando.</summary>
	private ulong _perguntouEm;

	/// <summary>(v0.3.2) O cliente cujas escolhas este menu ouve (a inscricao e preguicosa: o menu nasce antes da conexao).</summary>
	private GameClient? _ouvindoEscolhas;

	private void OuvirEscolhas()
	{
		if (GameClient.Instance is not { } cli || ReferenceEquals(cli, _ouvindoEscolhas)) return;
		if (_ouvindoEscolhas != null) _ouvindoEscolhas.EscolhasChegaram -= AoChegaremEscolhas;
		cli.EscolhasChegaram += AoChegaremEscolhas;
		_ouvindoEscolhas = cli;
	}

	/// <summary>(v0.3.2) As escolhas chegaram: se a pergunta delas e a pagina aberta, ela se redesenha com os botoes.</summary>
	private void AoChegaremEscolhas(string verbo)
	{
		if (NaTela && _perguntando is { } p && p.Verbo == verbo && _pagina == verbo) Desenhar(_tipoDoAlvo);
	}

	// =====================================================================
	// O CAIDO -- a QUINTA fonte de alvo da tecla E (o saque, item 9b do playtest de 2026-10-10)
	// =====================================================================
	// (2026-10-13) O MENU DE BOTOES DO SAQUE SAIU DAQUI: o dono pediu a mochila do caido no lugar dele -- *"deveria
	// abrir o inventario dela e nao um menu onde vc seleciona por botoes oq vc quer"*. Este menu continua sendo quem
	// ACHA o caido e manda a pergunta (o verbo `saquear`); quem desenha a resposta e a `TelaDeInventario.AbrirSaque`.

	/// <summary>
	/// O NOCAUTEADO AO ALCANCE, se ele for o alvo: o cadaver apontado pelo servidor fica de fora (ele tem o
	/// menu dele) e uma obra MAIS PERTO ganha, como ganha do cadaver. Quem sabe quem esta caido e o `World`,
	/// pelo snapshot -- nao ha pacote pra isso.
	/// </summary>
	private static (int Id, string Nome)? CaidoGanha(GameClient.ObraInfo? obra)
	{
		if (GameClient.Instance is not { } cli || World.Instancia is not { } mundo) return null;
		if (mundo.PosicaoDesenhadaDe(cli.LocalId) is not { } eu) return null;
		if (mundo.CaidoPerto(eu, Alcance, cli.CadaverPerto) is not { } caido) return null;
		if (obra is { } o && eu.DistanceSquaredTo(o.Pos) <= eu.DistanceSquaredTo(caido.Onde)) return null;
		return (caido.Id, caido.Nome);
	}

	// =====================================================================
	// O QUE ESTA LARGADO NO CHAO -- a SEXTA fonte de alvo da tecla E (dono, 2026-10-13)
	// =====================================================================
	/// <summary>
	/// A COISA LARGADA MAIS PERTO, ao alcance, se ela for o alvo: como o cadaver e o caido, ela nao esta em
	/// `cli.Obras` -- vem no `S2C.Chao` (`cli.Largados`) -- e disputa com a mobilia por perto pela regra de sempre, a
	/// distancia. O CORTE E POR EIXO, como o do servidor (`GameServer.AoAlcanceDoChao`).
	///
	/// ELA NAO ABRE MENU: o E PEGA (`chao_pegar #id`). E o `Get()` do original, que e um gesto so -- e com tres
	/// coisas no chao sao tres toques, cada um levando a mais perto.
	/// </summary>
	private static GameClient.LargadoInfo? NoChaoGanha(GameClient.ObraInfo? obra)
	{
		if (PegarDoChaoSoPeloVerboDeTeste) return null;
		if (GameClient.Instance is not { } cli || World.Instancia?.PosicaoDesenhadaDe(cli.LocalId) is not { } eu) return null;

		GameClient.LargadoInfo? melhor = null;
		float melhorDist = float.MaxValue;
		foreach (GameClient.LargadoInfo l in cli.Largados)
		{
			if (Math.Abs(l.Pos.X - eu.X) > Alcance || Math.Abs(l.Pos.Y - eu.Y) > Alcance) continue;
			float d = eu.DistanceSquaredTo(l.Pos);
			if (d >= melhorDist) continue;
			melhorDist = d;
			melhor = l;
		}
		if (melhor == null) return null;
		return obra is { } o && eu.DistanceSquaredTo(o.Pos) <= melhorDist ? null : melhor;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagv021`): a tecla E nao enxerga o que esta largado no chao -- nem a dica, nem o
	/// gesto. O item cai e fica la, sem porta de volta. Lido pela propria <see cref="NoChaoGanha"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool PegarDoChaoSoPeloVerboDeTeste;

	// =====================================================================
	// A ESFERA DO DRAGAO NO CHAO -- a SETIMA fonte de alvo da tecla E (dono, 2026-10-18)
	// =====================================================================
	/// <summary>
	/// A ESFERA DO DRAGAO MAIS PERTO, ao alcance de pegar, se ela for o alvo. O dono: *"n to conseguindo pegar a esfera do dragao"* e *"seria
	/// bom aparecer a mensagem de pegar a esfera assim como pega um item dropado"* -- ela so se pegava pelo botao "Pegar esfera" da aba World,
	/// e a tecla E, que pega tudo o que esta largado, nao a enxergava. Como a coisa largada, ela nao esta em `cli.Obras` -- vem no
	/// `S2C.Esferas` (`cli.Esferas`) -- e disputa pela regra de sempre, a distancia: a mobilia e a coisa largada MAIS PERTO ganham dela.
	///
	/// O CORTE E O DO SERVIDOR: a caixa de `Esferas.AlcanceDePegar` (um tile e meio, o `oview(1)` do `Get()`) em volta do corpo -- a
	/// pergunta do `GameServer.EsferaPerto`. O E PEGA, sem menu: o `db_pegar` de producao, que leva a mais perto.
	/// </summary>
	private static GameClient.EsferaInfo? EsferaGanha(GameClient.ObraInfo? obra, GameClient.LargadoInfo? largado)
	{
		if (EsferaSoPeloMenuDeTeste) return null;
		if (GameClient.Instance is not { } cli || World.Instancia?.PosicaoDesenhadaDe(cli.LocalId) is not { } eu) return null;

		const float alcance = Jandirus.Core.Magic.Esferas.AlcanceDePegar;
		GameClient.EsferaInfo? melhor = null;
		float melhorDist = float.MaxValue;
		foreach (GameClient.EsferaInfo e in cli.Esferas)
		{
			if (e.Tipo != Jandirus.Net.Protocol.CoisaDeEsfera.Esfera) continue;
			if (Math.Abs(e.Pos.X - eu.X) > alcance || Math.Abs(e.Pos.Y - eu.Y) > alcance) continue;
			float d = eu.DistanceSquaredTo(e.Pos);
			if (d >= melhorDist) continue;
			melhorDist = d;
			melhor = e;
		}
		if (melhor == null) return null;
		if (obra is { } o && eu.DistanceSquaredTo(o.Pos) <= melhorDist) return null;
		if (largado is { } l && eu.DistanceSquaredTo(l.Pos) < melhorDist) return null;
		return melhor;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagesferae`): a tecla E nao enxerga a esfera -- o jogo de antes de 2026-10-18, em que ela so se pegava
	/// pelo botao da aba World. Lido pela propria <see cref="EsferaGanha"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool EsferaSoPeloMenuDeTeste;

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

	/// <summary>DEFEITO INJETADO (bancada --diagconstruir): a dica "[E] ..." volta a aparecer no modo de construir.</summary>
	public static bool DicaNoModoDeConstruirDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --diagjanelas): a dica "[E] ..." volta a ser desenhada por cima do menu P embutido.</summary>
	public static bool DicaPorCimaDoMenuDeTeste;

	/// <summary>
	/// O MENU P EMBUTIDO ESTA EM CIMA DE ONDE A DICA APARECE? Esta camada desenha por cima da dele, e a dica caia em
	/// cima do rodape do menu ("P FECHA · ESC FECHA · [E] Bank"). E pelo RETANGULO do menu na tela, e nao por "o
	/// menu esta aberto": ele e uma janela que o jogador arrasta e encolhe -- fora do caminho, a dica continua; e
	/// DESTACADO noutra janela do sistema ele nao tapa nada (`MenuJogo.RetanguloNaTela` e nulo).
	/// </summary>
	private bool OMenuCobreADica() =>
		!DicaPorCimaDoMenuDeTeste && MenuJogo.RetanguloNaTela is { } menu && menu.Intersects(_dica.GetGlobalRect());

	/// <summary>Onde a dica "[E] ..." e desenhada, em px da tela (esteja ela na tela ou nao). So a bancada le.</summary>
	public Rect2 RetanguloDaDicaDeTeste => _dica.GetGlobalRect();

	public override void _Process(double delta)
	{
		// (2026-10-24) A COPIA DO MENU ABERTO SE DESFEZ (o prazo de 100 s, um golpe, o Destroy, o dono que mudou de zona): o menu dela fecha.
		if (_naCopia != 0 && (GameClient.Instance is not { } dono || !dono.MinhasCopias.Contains(_naCopia))) Fechar();

		// A DICA SEGUE O QUE ESTA POR PERTO, e some quando o menu abre (o menu ja e a resposta).
		// SOME TAMBEM NO MODO DE CONSTRUIR: ali a tecla E abre a paleta de blocos, entao "[E] Sua porta" mentiria
		// -- e a dica da porta ou da maquina vizinha caia em cima da linha de motivo da barra de construir.
		// E SOME EMBAIXO DO MENU P: ver `OMenuCobreADica`.
		if (_raiz.Visible || (ModoDeConstruir.Ligado && !DicaNoModoDeConstruirDeTeste) || OMenuCobreADica()) { _dica.Visible = false; return; }

		// O VEICULO VEM PRIMEIRO -- ver `Abrir`.
		if (VeiculoMontado().Length > 0)
		{
			_dica.Visible = true;
			_dica.Text = $"[E] {NomeDoVeiculo()}";
			return;
		}

		GameClient.ObraInfo? perto = MaisPerto();

		// O CORPO NO CHAO DISPUTA COM O QUE ESTA POR PERTO, e ganha pela REGRA QUE JA EXISTIA -- o mais
		// perto. Ver `Abrir`.
		if (CadaverGanha(perto))
		{
			_dica.Visible = true;
			_dica.Text = $"[E] {NomeDoCadaver()}";
			return;
		}

		if (CaidoGanha(perto) is { } caido)
		{
			_dica.Visible = true;
			_dica.Text = $"[E] revistar {caido.Nome}";
			return;
		}

		// A PORTA ERGUIDA disputa pela mesma regra: a mais perto ganha. Ver `PortaGanha`.
		if (PortaGanha(perto) is { } porta)
		{
			_dica.Visible = true;
			_dica.Text = $"[E] {NomeDaPorta(porta)}";
			return;
		}

		// A ESFERA DO DRAGAO NO CHAO, pela mesma regra (e a coisa largada mais perto ganha dela). Ver `EsferaGanha`.
		if (EsferaGanha(perto, NoChaoGanha(perto)) is { } esfera)
		{
			_dica.Visible = true;
			_dica.Text = $"[E] pegar a {Jandirus.Core.Magic.Esferas.NomeDaEsfera(esfera.Numero)}";
			return;
		}

		// O QUE ESTA LARGADO NO CHAO, pela mesma regra. Ver `NoChaoGanha`.
		if (NoChaoGanha(perto) is { } noChao)
		{
			_dica.Visible = true;
			_dica.Text = $"[E] pegar {Jandirus.Core.Items.Chao.Nome(noChao.Tipo, noChao.Item, noChao.Quantidade)}";
			return;
		}

		_dica.Visible = perto != null;
		if (perto is { } o) _dica.Text = $"[E] {NomeDe(o)}";
	}

	// =====================================================================
	// A PORTA ERGUIDA -- o quarto tipo de alvo da tecla E
	// =====================================================================
	/// <summary>
	/// A PORTA ERGUIDA MAIS PERTO QUE TEM ALGO A OFERECER a este jogador, com a distancia ao quadrado.
	///
	/// Entra pela mesma porta do veiculo e do cadaver, e pelo mesmo motivo: ela nao esta em `cli.Obras`.
	/// E um bloco (`cli.Blocos`), e o `World` sabe quais deles sao porta. O que ela oferece depende de
	/// quem olha (`Interacoes.DaPorta`): pra quem ela abre por encostar, ela nem e alvo.
	///
	/// O CORTE E POR EIXO, contra o centro da celula -- a conta do servidor (`PortaErguidaPerto`).
	/// </summary>
	private static (GameClient.BlocoInfo Bloco, float Dist2)? PortaPerto()
	{
		if (GameClient.Instance is not { } cli || World.Instancia is not { } mundo) return null;
		if (mundo.PosicaoDesenhadaDe(cli.LocalId) is not { } eu) return null;

		const float T = Jandirus.Core.World.ZoneCollision.TileSize;
		(GameClient.BlocoInfo Bloco, float Dist2)? melhor = null;
		foreach ((int x, int y) in mundo.PortasErguidas)
		{
			if (!cli.Blocos.TryGetValue((x, y), out GameClient.BlocoInfo b)) continue;
			if (Interacoes.DaPorta(b.Meu, b.Trancado, b.SeiASenha).Length == 0) continue;

			var centro = new Vector2(x * T + T / 2f, y * T + T / 2f);
			if (Math.Abs(centro.X - eu.X) > Alcance || Math.Abs(centro.Y - eu.Y) > Alcance) continue;

			float d = eu.DistanceSquaredTo(centro);
			if (melhor is { } m && d >= m.Dist2) continue;
			melhor = (b, d);
		}
		return melhor;
	}

	/// <summary>A porta erguida ganha da mobilia por perto? Pela distancia, como o cadaver (`CadaverGanha`).</summary>
	private static GameClient.BlocoInfo? PortaGanha(GameClient.ObraInfo? obra)
	{
		if (PortaPerto() is not { } p) return null;
		if (obra is { } o && GameClient.Instance is { } cli
			&& World.Instancia?.PosicaoDesenhadaDe(cli.LocalId) is { } eu && eu.DistanceSquaredTo(o.Pos) <= p.Dist2)
			return null;
		return p.Bloco;
	}

	private static string NomeDaPorta(GameClient.BlocoInfo b) => b.Meu ? "Sua porta" : "Porta trancada";

	/// <summary>O menu aberto e o de uma PORTA erguida, e estas sao as acoes dela (fixadas ao abrir).</summary>
	private bool _naPorta;
	private Interacoes.Acao[] _acoesDaPorta = [];

	/// <summary>
	/// ============================ O CORPO AOS MEUS PES -- o terceiro tipo de alvo da tecla E ============================
	/// *"basta apertar E perto do corpo pra enterrar"*. Ele entra pela mesma porta do VEICULO e pelo
	/// mesmo motivo, ja escrito no `VeiculoMontado`: **todo o resto deste menu procura alvo em
	/// `cli.Obras`**, que e a lista de construcoes da zona, e um cadaver nao esta la -- ele e um CORPO,
	/// e corpo viaja pelo snapshot, que nao diz qual deles esta morto.
	///
	/// Entao o servidor DIZ qual esta ao alcance (`Protocol.S2C.Cadaver`) e o menu o trata como alvo.
	/// Zero = nenhum.
	/// ================================================================================================================
	/// </summary>
	private static int Cadaver()
	{
		if (GameClient.Instance is not { } cli) return 0;
		return cli.CadaverPerto;
	}

	/// <summary>O nome que vai na tela ("corpo de Fulano") -- vem NO PACOTE, como o do veiculo.</summary>
	private static string NomeDoCadaver()
	{
		if (GameClient.Instance is not { } cli) return "corpo";
		return cli.NomeDoCadaver.Length > 0 ? cli.NomeDoCadaver : "corpo";
	}

	/// <summary>
	/// ============================ O CORPO GANHA DA MOBILIA? PELA DISTANCIA, COMO SEMPRE ============================
	/// **Nao ha prioridade inventada aqui**, e nao pode haver: "o mais perto ganha" ja era a regra deste
	/// menu (e o que faz o veiculo ganhar de tudo -- ele esta a distancia ZERO, embaixo de voce). Um
	/// corpo caido ao lado de uma macieira precisa da mesma resposta.
	///
	/// A POSICAO DELE SAI DO MUNDO DESENHADO e nao do pacote: o pacote diz QUAL corpo, e o corpo ja
	/// esta na tela (ele veio no snapshot como qualquer outro). Mandar a coordenada junto seria uma
	/// segunda verdade sobre onde ele esta -- e ela ficaria velha no instante em que alguem o
	/// arremessasse, que e uma coisa que se faz com cadaver o tempo todo neste jogo.
	///
	/// SEM O CORPO NA TELA (ele saiu de vista, o pacote chegou antes do snapshot), a resposta e NAO:
	/// oferecer "enterrar" apontando pra nada seria o botao que existe e o comando que falha.
	/// ==========================================================================================================
	/// </summary>
	private static bool CadaverGanha(GameClient.ObraInfo? obra)
	{
		if (Cadaver() == 0) return false;
		if (GameClient.Instance is not { } cli) return false;
		if (World.Instancia?.PosicaoDesenhadaDe(cli.LocalId) is not { } eu) return false;
		if (World.Instancia?.PosicaoDesenhadaDe(Cadaver()) is not { } corpo) return false;

		// O MESMO CORTE POR EIXO do `MaisPerto`, e pelo mesmo motivo escrito la: o servidor mede assim
		// (`Math.Abs` em X e Y separados), e usar raio aqui abriria o menu em pontos que ele recusaria.
		if (Math.Abs(corpo.X - eu.X) > Alcance || Math.Abs(corpo.Y - eu.Y) > Alcance) return false;

		return obra is not { } o || eu.DistanceSquaredTo(corpo) < eu.DistanceSquaredTo(o.Pos);
	}

	/// <summary>
	/// A NAVE QUE ESTA EMBAIXO DE MIM, ou "" -- o alvo que nao esta no chao.
	///
	/// ============================ POR QUE ELE NAO PODE SAIR DA LISTA DE OBRAS ============================
	/// Todo o resto deste menu procura alvo em `cli.Obras`, que e a lista de construcoes da ZONA. Uma
	/// nave pilotada nao esta la de proposito: ela deixou de estar no chao e passou a andar com o
	/// corpo (`NavesParadasEm` filtra `PilotoId == 0`), e mandar a lista inteira 30x/s pra acompanhar
	/// um veiculo seria o canal errado -- aquele pacote e confiavel e so sai quando algo MUDA.
	///
	/// Entao o servidor DIZ qual e (`Protocol.S2C.Veiculo`), e ele vira alvo por outro caminho. Sem
	/// isto o piloto era a unica pessoa do servidor que nao alcancava os comandos do proprio veiculo,
	/// nem o de descer dele -- o buraco que a aba Nav tapava com uma fileira de botoes.
	/// ==================================================================================================
	/// </summary>
	private static string VeiculoMontado()
	{
		if (GameClient.Instance is not { } cli) return "";
		// (2026-10-18) OBSERVANDO UMA NAVE DE FORA, a pagina e a do olho (`Interacoes.TipoDoOlho`): o console esta a um mundo de distancia,
		// e por ela se volta ao corpo -- pela mesma porta do veiculo montado, a "distancia zero" de sempre.
		if (cli.Observador) return Interacoes.TipoDoOlho;
		return Interacoes.DoVeiculo(cli.VeiculoMontado).Length > 0 ? cli.VeiculoMontado : "";
	}

	/// <summary>O nome da nave montada -- ele vem no pacote, pelo mesmo motivo do nome das obras.</summary>
	private static string NomeDoVeiculo()
	{
		if (GameClient.Instance is not { } cli) return "";
		if (cli.Observador) return "Observando a nave";
		return cli.NomeDoVeiculo.Length > 0 ? cli.NomeDoVeiculo : cli.VeiculoMontado.Replace('_', ' ');
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagbills): o Esc do menu aberto deixa de ser ouvido no `_Input` -- ele segue pro
	/// `_UnhandledInput`, onde o menu de pausa o recebe ANTES deste node e abre por cima do menu. Lido pela propria
	/// linha do <see cref="_Input"/>.
	/// </summary>
	public static bool EscSoDepoisDaPausaDeTeste;

	/// <summary>
	/// ============================ O ESC COM O MENU ABERTO E DO MENU ============================
	/// Ele e ouvido AQUI, no `_Input`, e nao no `_UnhandledInput` junto da tecla E. Morava la, e la o menu de pausa
	/// escuta a mesma tecla (`PauseMenu._UnhandledInput`) e a recebia ANTES deste node: com o menu de um objeto
	/// aberto, o Esc abria a PAUSA por cima dele e o menu continuava aberto atras -- o botao "Fechar (Esc)" mentia.
	/// MEDIDO na `--diagbills` (2026-10-11), que fotografou a pausa em cima do menu do Bills.
	///
	/// E o conserto que as outras telas ja tinham, pelo motivo que elas escrevem (`MenuDeDesejos`,
	/// `ModoDeConstruir`, o menu P): o `_Input` roda antes, entao a coisa mais na frente da tela responde primeiro,
	/// e a pausa so abre quando nao ha mais nada pra fechar.
	///
	/// UMA CAMADA POR VEZ: com o teclado numerico por cima, o Esc fecha o TECLADO e o menu fica (ele nao ouvia o
	/// Esc, e a tecla caia na pausa).
	///
	/// QUEM ESTA DIGITANDO E DONO DO ESC (`Foco.Digitando`, a mesma porta do `MenuDeDesejos`): a frase do chat
	/// desiste por ele, e a caixa de texto deste proprio menu (a lapide) se cancela sozinha -- ver `CaixaDeTexto`.
	///
	/// A PAUSA ABERTA E DELA: se ela ja esta na tela, e ela que esta na frente -- o Esc segue e a fecha.
	///
	/// SEM O PORTAO DO EMBATE (`EmClash`), de proposito: aquele portao cala o que ABRE coisa por cima da disputa.
	/// Fechar nunca e bloqueado (a mesma regra escrita no `PauseMenu`).
	/// ===========================================================================================
	/// </summary>
	public override void _Input(InputEvent evento)
	{
		// (2026-10-24) O DUPLO CLIQUE CUJO PRIMEIRO CLIQUE ABRIU O MENU DE UMA COPIA (`World.CliqueNaCopia`): o gesto era o duplo -- marcar o
		// alvo, a piscada do Zanzoken. O menu fecha AQUI, antes da interface ver o clique, e ele segue pro mundo -- em vez de cair num botao
		// do menu que acabou de abrir em cima dele (o menu tapa a tela inteira).
		if (_naCopia != 0 && !DuploDeixaOMenuDaCopiaDeTeste && evento is InputEventMouseButton { Pressed: true, DoubleClick: true, ButtonIndex: MouseButton.Left }
			&& Time.GetTicksMsec() - _copiaAbertaEm < JanelaDoDuplo)
		{
			Fechar();
			return;
		}

		if (EscSoDepoisDaPausaDeTeste || !NaTela || Foco.Digitando) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
		if (PauseMenu.Instancia is { Aberto: true }) return;

		if (_teclado != null && IsInstanceValid(_teclado)) FecharTeclado();
		else Fechar();
		GetViewport().SetInputAsHandled();
	}

	public override void _UnhandledInput(InputEvent evento)
	{
		// NO MEIO DE UM EMBATE O "E" NAO ABRE NADA (ate 2026-10-19 ele era uma das letras sorteadas) -- ver `Foco.AtalhosMudos`.
		if (Foco.AtalhosMudos) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k) return;

		// (O ESC do menu aberto nao e daqui: ele e ouvido no `_Input`, antes do menu de pausa. Ver acima.)

		// PELO REGISTRO, e nao por `Key.E` escrito aqui. Duas coisas dependem disso: a tecla e
		// religavel como qualquer outra, e a pergunta "essa tecla esta livre?" enxerga esta linha --
		// sem o registro ela responderia "sim" sobre o E e entregaria o menu de interacao junto com
		// o que o jogador ligou. Ver o cabecalho do `Teclas`.
		if (!Teclas.Bate("ui_interagir", k)) return;

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

		// A ROCHA DE MINERIO ao alcance disputa pela mesma regra -- ela nao esta em `cli.Obras` (ver `RochasNaTela`).
		RochasNaTela.Disputar(eu, ref melhor, ref melhorDist);
		// O BILLS OU O WHIS ao alcance tambem: sao corpos, e quem diz qual deles tem menu e o servidor (ver `DivinosNaTela`).
		DivinosNaTela.Disputar(eu, ref melhor, ref melhorDist);
		return melhor;
	}

	/// <summary>
	/// O NOME QUE VAI NA TELA. Ele vem NO PACOTE, junto do objeto (`ObraInfo.Nome`), e nao do
	/// catalogo local: o catalogo do cliente so tem o que ele pode COMPRAR, e mobilia de mapa (banco,
	/// macieira, porta da Sala do Tempo, as duas pecas da ponte) nunca entra nele -- essas caiam no
	/// ultimo recurso e apareciam em ingles. Ver `ObraInfo`.
	///
	/// O `Replace('_', ' ')` continua como ultimo recurso pra um tipo que o servidor mandou sem nome
	/// (linha faltando no `construcoes.json`): nome feio e melhor que nome vazio.
	/// </summary>
	private static string NomeDe(GameClient.ObraInfo o) =>
		o.Nome.Length > 0 ? o.Nome : o.Tipo.Replace('_', ' ');

	/// <summary>Que pagina do menu esta aberta. Vazio = a raiz do objeto.</summary>
	private string _pagina = "";

	/// <summary>
	/// O TIPO DO ALVO ABERTO. Guardado, e nao relido da lista de obras a cada quadro: o alvo pode
	/// ser um VEICULO, que nao esta na lista -- e uma nave em movimento sairia do alcance no meio do
	/// menu se ele fosse reperguntado.
	/// </summary>
	private string _tipoDoAlvo = "";

	/// <summary>O menu aberto e o do veiculo montado, e nao o de um objeto do chao.</summary>
	private bool _noVeiculo;

	// =====================================================================
	// A COPIA DO SPLIT FORM -- o menu que o CLIQUE abre (dono, 2026-10-24)
	// =====================================================================
	/// <summary>A copia do Split Form cujo menu esta aberto (0 = nenhuma): as ordens saem com o id dela. Ver <see cref="AbrirNaCopia"/>.</summary>
	private int _naCopia;

	/// <summary>Quando o menu da copia abriu (ms do relogio do motor): o duplo clique cujo primeiro clique o abriu o fecha. Ver `_Input`.</summary>
	private ulong _copiaAbertaEm;

	/// <summary>A janela do duplo clique, em ms: a do Windows e de 500 por padrao e vai ate 900 no painel -- um pouco acima, por folga.</summary>
	private const ulong JanelaDoDuplo = 1000;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagverbs`, F10): o duplo clique deixa aberto o menu que o primeiro clique abriu numa copia -- e o
	/// segundo clique cai no menu (num botao, se houver um ali). Lido pelo `_Input`. Falso em jogo, sempre.
	/// </summary>
	public static bool DuploDeixaOMenuDaCopiaDeTeste;

	/// <summary>
	/// (2026-10-24) O MENU DE ORDENS DE UMA COPIA MINHA DO SPLIT FORM -- o `Click()` do `/mob/npc/Splitform` (`Split Forms.dm:28-68`), que
	/// o dono procurou: *"Eu clicava na copia como no DM e nao aparecia nada; nao achei onde dar as ordens"*. Quem chama e o clique no
	/// mundo (`World.CliqueNaCopia`), de qualquer distancia, como no DM. As acoes sao a tabela (`Interacoes.De(Interacoes.TipoDaCopia)`), e
	/// cada ordem sai com o id DESTA copia (`Escolher`): so ela obedece -- o Destroy Splitforms desfaz todas, como o do DM.
	/// </summary>
	public void AbrirNaCopia(int id, string nome)
	{
		FecharTeclado();
		_pagina = "";
		_perguntando = null;
		_noVeiculo = false;
		_noCadaver = false;
		_naPorta = false;
		_naCopia = id;
		_copiaAbertaEm = Time.GetTicksMsec();
		_tipoDoAlvo = Interacoes.TipoDaCopia;
		_nomeDoAlvo = nome.Length > 0 ? nome : "Split Form";
		Desenhar(Interacoes.TipoDaCopia);
		_raiz.Visible = true;
	}

	/// <summary>
	/// ABRE O MENU DO QUE ESTA AO ALCANCE.
	///
	/// ============================ O VEICULO GANHA DE QUALQUER COISA NO CHAO ============================
	/// Nao por prioridade inventada: porque ele nao esta A DOIS TILES, ele esta EMBAIXO de voce -- a
	/// distancia e zero, e "o mais perto ganha" ja era a regra deste menu. Quem pilota um pod por
	/// cima de uma bancada quer o pod; a bancada volta a responder assim que ele descer.
	///
	/// E e o pedido do dono na ponte: *"ao apertar E DNV vc volta pra dentro da nave"*. Do leme da
	/// Capital Ship o console esta noutra ZONA -- nao ha objeto nenhum a alcance --, entao sem esta
	/// porta nao haveria como largar o leme.
	/// ================================================================================================
	/// </summary>
	private void Abrir()
	{
		_pagina = "";
		_perguntando = null;

		_naPorta = false;
		_naCopia = 0;

		if (VeiculoMontado() is { Length: > 0 } v)
		{
			_noVeiculo = true;
			_tipoDoAlvo = v;
			_nomeDoAlvo = NomeDoVeiculo();
			Desenhar(v);
			_raiz.Visible = true;
			return;
		}

		GameClient.ObraInfo? perto = MaisPerto();

		// ============================ O CORPO NO CHAO, PELA MESMA REGRA DE SEMPRE ============================
		// Ele nao "tem prioridade": ele ganha quando esta MAIS PERTO, que e a unica regra que este menu
		// sempre teve. Ver `CadaverGanha`.
		if (CadaverGanha(perto))
		{
			_noVeiculo = false;
			_noCadaver = true;
			_tipoDoAlvo = "";
			_nomeDoAlvo = NomeDoCadaver();
			Desenhar("");
			_raiz.Visible = true;
			return;
		}

		// O CAIDO: o menu NAO abre aqui. Quem sabe o que ele carrega e o servidor -- o E PERGUNTA (o verbo
		// `saquear` sem argumento), e o menu abre quando a lista chegar (`AoAbrirSaque`).
		if (CaidoGanha(perto) is not null)
		{
			GameClient.Instance?.SendVerbo(Jandirus.Core.Items.Saque.Verbo);
			return;
		}

		if (PortaGanha(perto) is { } porta)
		{
			_noVeiculo = false;
			_noCadaver = false;
			_naPorta = true;
			_acoesDaPorta = Interacoes.DaPorta(porta.Meu, porta.Trancado, porta.SeiASenha);
			_tipoDoAlvo = "";
			_nomeDoAlvo = NomeDaPorta(porta);
			Desenhar("");
			_raiz.Visible = true;
			return;
		}

		// A ESFERA DO DRAGAO NO CHAO: o E PEGA, sem menu -- o `db_pegar` de producao. Ver `EsferaGanha`.
		if (EsferaGanha(perto, NoChaoGanha(perto)) is not null)
		{
			GameClient.Instance?.SendVerbo("db_pegar", "");
			return;
		}

		// O QUE ESTA LARGADO NO CHAO: o E PEGA, sem menu nenhum -- ver `NoChaoGanha`.
		if (NoChaoGanha(perto) is { } noChao)
		{
			GameClient.Instance?.SendVerbo(Jandirus.Core.Items.Chao.VerboPegar, $"#{noChao.Id}");
			return;
		}

		if (perto is not { } o) return;
		_noVeiculo = false;
		_noCadaver = false;
		_tipoDoAlvo = o.Tipo;
		_nomeDoAlvo = NomeDe(o);
		Desenhar(o.Tipo);
		_raiz.Visible = true;
	}

	/// <summary>O menu aberto e o de um CORPO no chao, e nao o de um objeto nem o de um veiculo.</summary>
	private bool _noCadaver;

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagmaquinas): o "Voltar" de antes de 2026-10-10 -- sempre a raiz, mesmo de uma
	/// pagina aberta DENTRO de outra (a das duas formas de pagar uma melhoria). Lido pela propria linha do botao.
	/// </summary>
	public static bool VoltarVaiPraRaizDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagmaquinas): a lista de melhorias abre CALADA, sem pedir os precos ao
	/// servidor -- o menu de antes de 2026-10-10. Lido pela propria linha que pergunta, em <see cref="Escolher"/>.
	/// </summary>
	public static bool AbreSemPerguntarDeTeste;

	/// <summary>
	/// Desenha a pagina atual do objeto. A raiz e `tipo`; um submenu e `tipo/chave`.
	/// </summary>
	private void Desenhar(string tipo)
	{
		string chave = _pagina.Length == 0 ? tipo : $"{tipo}/{_pagina}";
		// (v0.3.2) a pagina de escolhas so vale enquanto e ela que esta aberta: o Voltar a deixa pra tras
		if (_perguntando is { } p0 && _pagina != p0.Verbo) _perguntando = null;
		_titulo.Text = _pagina.Length == 0 ? NomeDoAlvo()
			: _perguntando is { } p1 ? $"{NomeDoAlvo()} — {p1.Rotulo}"
			: NomeDoAlvo() + " — melhorias";

		foreach (Node n in _lista.GetChildren()) n.QueueFree();

		// A DRILL DIZ COMO ESTA em cima dos botoes (nivel, taxa, estoque, a reserva do planeta e o preco da
		// proxima melhoria): o painel pergunta ao servidor e se atualiza sozinho. Ver `EstadoDaDrillNoMenu`.
		if (tipo == Drill.Tipo) _lista.AddChild(new EstadoDaDrillNoMenu());

		// VOLTAR SO EXISTE DENTRO DE UM SUBMENU. Na raiz ele seria o mesmo que Fechar, e dois
		// botoes pra sair da mesma tela e uma escolha que o jogador nao devia ter que fazer.
		if (_pagina.Length > 0)
		{
			var voltar = new Button { Text = "< Voltar" };
			// UMA PAGINA PRA CIMA, e nao a raiz: a melhoria de uma maquina abre a pagina das duas formas de pagar
			// ("upgrades/forca"), e de la o Voltar tem de cair na lista de melhorias.
			voltar.Pressed += () =>
			{
				_pagina = !VoltarVaiPraRaizDeTeste && _pagina.Contains('/') ? _pagina[.._pagina.LastIndexOf('/')] : "";
				Desenhar(tipo);
			};
			_lista.AddChild(voltar);
		}

		// (v0.3.2) A PAGINA DE ESCOLHAS DO SERVIDOR (`Forma.Lista`): um botao por escolha, e o clique manda o MESMO verbo com ela. Ate a
		// resposta chegar (`S2C.Escolhas`, `GameClient.Escolhas`), a pagina diz que esta consultando.
		if (_perguntando is { } pergunta)
		{
			if (GameClient.Instance?.Escolhas is { } e && e.Verbo == pergunta.Verbo && e.ChegouEm >= _perguntouEm)
			{
				if (e.Itens.Count == 0) _lista.AddChild(new Label { Text = "nenhuma escolha agora." });
				foreach (string escolha in e.Itens)
				{
					string arg = escolha;
					var b = new Button { Text = escolha, TooltipText = pergunta.Dica };
					b.Pressed += () => { GameClient.Instance?.SendVerbo(pergunta.Verbo, arg); Fechar(); };
					_lista.AddChild(b);
				}
			}
			else _lista.AddChild(new Label { Text = "consultando..." });
			return;
		}

		// A MESMA TELA, OUTRA TABELA: montado num veiculo o que se pode fazer com ele e outra coisa
		// (sair, largar o leme, lancar) e quase nada do que se faz com ele parado (entrar, pegar).
		// Ver `Interacoes.DoVeiculo` pro porque de nao ser uma lista so.
		//
		// VEICULO NAO TEM SUBMENU hoje, e por isso ele le sempre a raiz: nenhuma das tres naves
		// declara `Forma.Submenu`. Se um dia declarar, a chave composta ja esta montada acima.
		// O CADAVER E A TERCEIRA TABELA, e ele nao tem submenu nem tipo: as acoes de um corpo no chao
		// sao as mesmas para todo corpo (ver `Interacoes.DoCadaver`, que explica por que e uma so).
		// A PORTA ERGUIDA E A QUARTA, e a tabela dela depende de quem olha (ver `Interacoes.DaPorta`).
		// O QUE ESTA MAQUINA JA TEM de melhoria de uma vez so (o byte que veio com ela no pacote de construcoes): a
		// opcao sai da lista em vez de ficar ali pra recusar depois do clique. Ver `Interacoes.BitQueEsconde`.
		byte feitas = _naPorta || _noCadaver || _noVeiculo ? (byte)0 : MaisPerto()?.Feitas ?? 0;

		foreach (Interacoes.Acao a in _naPorta ? _acoesDaPorta
									 : _noCadaver ? Interacoes.DoCadaver()
									 : _noVeiculo ? Interacoes.DoVeiculo(tipo)
									 : Interacoes.De(chave))
		{
			// O MESMO CRIVO DO MENU P (`RequisitosDosVerbos`): acao que este personagem nao tem como usar -- o
			// laboratorio e de humano -- nao aparece. Acao fora da tabela nao tem requisito, e aparece.
			if (!Jandirus.Core.Verbos.RequisitosDosVerbos.DoVerbo(a.Verbo, a.Arg).Atende(PosseDoCliente.Atual)) continue;
			if (Interacoes.SaiDoMenu(tipo, a, feitas)) continue;

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
				// A LISTA DE MELHORIAS PEDE OS PRECOS (nas duas moedas) e o saldo: eles sao do estado desta maquina,
				// e a tabela dos botoes nao os tem. A resposta vem pelo chat. Ver `Interacoes.PerguntaAoAbrir`.
				if (!AbreSemPerguntarDeTeste && Interacoes.PerguntaAoAbrir(tipo, _pagina) is { Length: > 0 } pergunta)
					GameClient.Instance?.SendVerbo(pergunta);
				Desenhar(tipo);
				break;

			case Interacoes.Forma.Numero:
				AbrirTeclado(acao);
				break;

			case Interacoes.Forma.Texto:
				AbrirCaixaDeTexto(acao);
				break;

			case Interacoes.Forma.Lista:
				// (v0.3.2) A PERGUNTA VAI AO SERVIDOR: o verbo com o argumento vazio, e a resposta redesenha esta pagina (`AoChegaremEscolhas`).
				_perguntando = acao;
				_perguntouEm = Time.GetTicksMsec();
				_pagina = acao.Verbo;
				OuvirEscolhas();
				GameClient.Instance?.SendVerbo(acao.Verbo, "");
				Desenhar(tipo);
				break;

			case Interacoes.Forma.Roupa:
				// A VITRINE DA BANCADA DE CRIACAO: a grade do guarda-roupa abre por cima, e o clique LA manda o verbo, com
				// a peca e a cor. Ver `TelaDeArmadura.AbrirPraVitrine`.
				TelaDeArmadura.Instancia?.AbrirPraVitrine(acao.Verbo);
				Fechar();
				break;

			default:
				// TUDO PELO CANAL DE VERBOS. O `abrir_tech` abria a grade da bancada de pesquisa por
				// aqui; a grade morreu (fabricar e na aba Tech do menu P) e com ela o unico desvio que
				// este `default` tinha.
				if (RumoQueVaiComOLeme(acao) is { } rumo)
				{
					// (2026-10-18) LARGAR O LEME DA NAVE GRANDE COM O NAV SYSTEM LIGADO: o destino vai junto e a nave segue sozinha
					// (`GameServer.NaveRumo.cs`). O piloto automatico DESTE cliente sai calado -- quem conta da viagem agora e o servidor.
					GameClient.Instance?.SendVerbo(acao.Verbo, rumo);
					World.Instancia?.SoltarPiloto();
				}
				// (2026-10-24) no menu de uma copia do Split Form a ordem leva o id DELA: so ela obedece (`AbrirNaCopia`)
				else GameClient.Instance?.SendVerbo(acao.Verbo, _naCopia != 0 ? _naCopia.ToString() : acao.Arg);
				Fechar();
				break;
		}
	}

	/// <summary>
	/// O RUMO QUE VAI COM O LEME (dono, 2026-10-18: *"permita q na capital ship ao colocar pra ela ir pra um planeta com nav system e o jogador
	/// sair do piloto, ela ainda continua"*): ao leme da NAVE GRANDE, com o piloto automatico ligado, o "voltar a ponte" leva o destino do nav
	/// system em coordenada de galaxia ("x/y"). O piloto automatico mora neste cliente (`LocalPlayer.Destino`, e o `RumoDepoisDeDecolar` de quem
	/// ainda esta pousado), e por isso e daqui que o destino tem de sair. Nulo = nada a levar: o verbo sai como a tabela o escreveu.
	/// </summary>
	internal static string? RumoQueVaiComOLeme(Interacoes.Acao acao) =>
		acao.Verbo == "nave_pilotar" && GameClient.Instance?.VeiculoMontado == NaveGrande.Tipo && World.Instancia?.DestinoDoPiloto is { } d
			? $"{d.X:0}/{d.Y:0}"
			: null;

	/// <summary>
	/// O NOME NO ALTO DO MENU. Guardado ao abrir, e nao relido: o alvo pode ser um veiculo, que nao
	/// esta na lista de obras, e uma obra pode sumir da lista com o menu dela aberto (alguem a
	/// recolheu) -- e um titulo que fica vazio no meio da tela e pior que um titulo velho.
	/// </summary>
	private string _nomeDoAlvo = "";

	private string NomeDoAlvo() => _nomeDoAlvo;

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

	/// <summary>
	/// A CAIXA DE TEXTO -- a terceira pergunta que o menu aprendeu (a lapide: *"deveria ter a opcao de
	/// escrever manualmente na lapide, abrindo uma caixa de texto"*, 2026-09-04). Mesmo lugar e mesmo
	/// ciclo de vida do teclado numerico; o valor inicial vem da tabela das acoes (`Interacoes.TextoInicial`).
	/// </summary>
	private void AbrirCaixaDeTexto(Interacoes.Acao acao)
	{
		FecharTeclado();

		int teto = acao.Max > 0 ? (int)acao.Max : Jandirus.Net.Protocol.MaxArgDeVerbo;
		var c = new CaixaDeTexto(acao.Rotulo, Interacoes.TextoInicial(acao, _nomeDoAlvo), teto,
			texto => { GameClient.Instance?.SendVerbo(acao.Verbo, texto); Fechar(); },
			FecharTeclado);

		_teclado = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_teclado.AddChild(c);
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
		_pagina = "";
		_perguntando = null;
		_tipoDoAlvo = "";
		_nomeDoAlvo = "";
		_noVeiculo = false;
		_noCadaver = false;
		_naPorta = false;
		_naCopia = 0;
	}

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--diagembarque`)
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ELA PRECISA EXISTIR ============================
	/// "O menu abriu com a opcao de embarcar" e uma afirmacao que, sem isto, ninguem consegue
	/// conferir: o menu e um `CanvasLayer` com `Button`s dentro, e um `Button` nao devolve nada. A
	/// bancada de servidor pode provar que `Interacoes.De("Capital_Ship")` tem cinco linhas -- e o
	/// arquivo `dbclimax-port-bancada-mede-intencao` conta o que essa leitura vale: quatro defeitos
	/// visuais passaram por quatro mil checagens verdes porque a bancada media a TABELA, e o defeito
	/// morava no widget que a desenha.
	///
	/// Entao aqui nao se le tabela nenhuma: le-se o `_lista`, que e o node que estava na tela, e
	/// aperta-se o `Button` que estava nele. O que a bancada afirma e o que o jogador veria.
	/// =====================================================================================
	///
	/// TUDO SO-LEITURA MENOS UM: o <see cref="ApertarDesenhado"/> emite o MESMO sinal que o dedo
	/// emite -- ele nao chama `Escolher` por dentro, senao a bancada pularia justamente a ligacao
	/// (`b.Pressed += ...`) que pode estar faltando.
	/// </summary>
	public bool NaTela => IsInstanceValid(_raiz) && _raiz.Visible;

	/// <summary>(2026-10-24) A copia do Split Form cujo menu esta na tela (0 = nenhuma, ou o menu aberto e de outra coisa).</summary>
	public int CopiaAbertaDeTeste => NaTela ? _naCopia : 0;

	/// <summary>O titulo desenhado no alto do menu aberto -- vazio quando nao ha menu.</summary>
	public string TituloDesenhado => NaTela && IsInstanceValid(_titulo) ? _titulo.Text : "";

	/// <summary>
	/// A DICA FLUTUANTE do pe da tela ("[E] Console da Ponte"), ou vazio quando ela nao esta acesa.
	///
	/// Ela e a metade que ENSINA a tecla, e e a que reprova o caso "longe": um menu que nao abre
	/// prova pouco se a dica continua acesa dizendo que ha alguma coisa ali.
	/// </summary>
	public string DicaNaTela => IsInstanceValid(_dica) && _dica.Visible ? _dica.Text : "";

	/// <summary>Os rotulos dos botoes que o menu aberto DESENHOU, de cima pra baixo.</summary>
	public List<string> BotoesDesenhados()
	{
		var r = new List<string>();
		if (!NaTela || !IsInstanceValid(_lista)) return r;
		foreach (Node n in _lista.GetChildren())
			if (n is Button b && IsInstanceValid(b)) r.Add(b.Text);
		return r;
	}

	/// <summary>Aperta um botao do menu aberto pelo rotulo. Falso quando ele nao esta na tela.</summary>
	public bool ApertarDesenhado(string rotulo)
	{
		if (!NaTela || !IsInstanceValid(_lista)) return false;
		foreach (Node n in _lista.GetChildren())
		{
			if (n is not Button b || !IsInstanceValid(b) || b.Text != rotulo) continue;
			b.EmitSignal(BaseButton.SignalName.Pressed);
			return true;
		}
		return false;
	}

	/// <summary>O teclado numerico esta aberto? (a segunda pagina do menu, ver <see cref="Forma"/>).</summary>
	public bool TecladoNaTela => _teclado != null && IsInstanceValid(_teclado);

	/// <summary>A caixa de texto (`Forma.Texto`) esta na tela? Mora no mesmo lugar do teclado numerico.</summary>
	public bool CaixaDeTextoNaTela =>
		TecladoNaTela && _teclado!.GetChildCount() > 0 && _teclado.GetChild(0) is CaixaDeTexto;

	/// <summary>
	/// DIGITA NA CAIXA DE TEXTO como o jogador digitaria -- o campo inteiro, e nao tecla a tecla: o
	/// `LineEdit` e do Godot e nao tem regra propria a provar -- e confirma pelo mesmo "Gravar". Devolve
	/// o que o campo MOSTRAVA antes: o valor inicial (`Interacoes.TextoInicial`), pra bancada comparar
	/// com a resposta pronta do `input()` do DM.
	/// </summary>
	public string DigitarNaCaixa(string texto, bool confirmar = true)
	{
		if (!CaixaDeTextoNaTela || _teclado!.GetChild(0) is not CaixaDeTexto c) return "";
		string antes = c.DigitarDeTeste(texto);
		if (confirmar) c.ConfirmarDeTeste();
		return antes;
	}

	/// <summary>
	/// DIGITA UM NUMERO NO TECLADO ABERTO e confirma -- tecla a tecla, pelos mesmos botoes.
	///
	/// NAO chama o `aoConfirmar` por dentro de proposito: o teto de algarismos do teclado
	/// (`TecladoNumerico`) e justamente uma das coisas que podem nao caber no `Max` que a acao
	/// declarou, e um atalho que "digita" o numero inteiro passaria por cima disso. Devolve o que o
	/// VISOR ficou mostrando, pra a bancada poder comparar com o que se pediu.
	/// </summary>
	public string DigitarNoTeclado(string numero, bool confirmar = true)
	{
		if (!TecladoNaTela || _teclado!.GetChildCount() == 0) return "";
		if (_teclado.GetChild(0) is not TecladoNumerico t) return "";

		foreach (char c in numero) t.ApertarTecla(c.ToString());
		string visor = t.VisorDeTeste;
		if (confirmar) t.ApertarTecla("Confirmar");
		return visor;
	}
}
