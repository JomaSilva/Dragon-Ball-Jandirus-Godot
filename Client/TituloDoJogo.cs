using Godot;

namespace Jandirus.Client;

/// <summary>
/// O TITULO DO JOGO NO ALTO DOS MENUS -- o logo "DRAGON BALL JANDIRUS" do trailer, com o brilho laranja atras.
///
/// O dono (2026-10-11): *"ao abrir o jogo ter tipo uma animaçao igual a animaçao q vc fez pro titulo do jogo no
/// trailer (...) e dps dessa animaçao o titulo sobe pro topo da tela e o menu normal do jogo aparece, e ja q vai ter
/// um titulo no topo da tela, o titulo q aparece no menu do jogo atualmente pode ser tirado"*.
///
/// A animacao e a <see cref="AberturaDoJogo"/>; ESTE node e onde ela pousa -- e o que fica na tela depois dela, e nas
/// corridas em que ela nao roda (toda bancada, e toda volta ao menu).
///
/// ============================ EM QUE TELAS ELE FICA ============================
/// Nas duas telas "de menu" que tem sobra no alto: o MENU PRINCIPAL (`Boot.MontarLogin`) e a SELECAO de personagem
/// (`CharacterSelect`). Na CRIACAO nao: o assistente ocupa 573 dos 720 px de altura da menor resolucao, e um logo ali
/// taparia o titulo de cada etapa. Cada tela tem o SEU node, filho dela: some e volta com ela, e fica por baixo do
/// veu de qualquer aviso que ela abrir (a queda de conexao, o "excluir personagem").
///
/// ============================ TUDO POR PROPORCAO DA TELA ============================
/// O jogo desenha numa base de 1280x720 que estica, alarga (`aspect = expand`) e troca de tamanho nas opcoes. O
/// titulo ocupa a faixa de cima -- <see cref="FaixaDoTopo"/> da ALTURA --, e o menu se centra no que sobra
/// (`AnchorTop = FaixaDoTopo` no container de cada tela). Nenhum numero daqui e pixel de tela.
///
/// ============================ A ARTE VEM NUMA THREAD, E SO COM JANELA ============================
/// Sao dois PNG: o logo (`Titulo.png`, 1498 x 899, a arte do trailer: `Trailer/bruto/t2/titulo2.png`) e o brilho
/// (`TituloBrilho.png`, assado por `Tools/brilho_do_titulo.py` pela conta do trailer). Lidos pela thread principal,
/// a leitura dos dois (so descompactar o logo e os mipmaps dele sao uns 16 ms, medidos fora do jogo) cairia no
/// arranque de todo processo com janela -- inclusive os das bancadas que medem tempo. Entao sao pedidos a thread de
/// carga e desenhados quando chegam (um ou dois quadros depois), e SEM JANELA nem sao pedidos: no `--headless` nao
/// ha o que desenhar. O logo tem MIPMAPS (o `.import` dele): no cabecalho ele aparece reduzido 4 a 6 vezes, e sem
/// isso o contorno serrilha.
///
/// E O PEDIDO NAO ATRASA A FILA DO `Aquecimento`, que usa a mesma thread de carga e e pedida logo atras dele.
/// MEDIDO em 2026-10-11: 12 pares de corridas de `--host` com janela, alternando a normal com a `--semartedotitulo`
/// (<see cref="SemArteDeTeste"/>). Quem loga em 1 s espera no lobby pelo resto da fila -- a linha
/// `[aquece] fecho antecipado` -- 378 ms com a arte e 377 ms sem ela (medianas; de 361 a 408 nos dois lados), e a
/// primeira fila fecha em 1034 ms contra 1039.
/// </summary>
public partial class TituloDoJogo : Control
{
	// =====================================================================
	// OS NUMEROS (todos ajustaveis)
	// =====================================================================
	/// <summary>A fracao da ALTURA da tela que o titulo ocupa no alto dos menus. O menu se centra no resto.</summary>
	public const float FaixaDoTopo = 0.25f;

	/// <summary>Quanto da altura da faixa a ARTE ocupa (ela ja traz ~6% de margem transparente em volta das letras).</summary>
	public const float AlturaNaFaixa = 0.94f;

	/// <summary>Numa tela muito estreita, ate que fracao da LARGURA da faixa a arte pode ir (ai e a largura que manda).</summary>
	public const float LarguraMaximaNaFaixa = 0.9f;

	/// <summary>O brilho laranja do titulo PARADO no alto do menu (na abertura ele pulsa de 0,44 a 1: ver `AberturaDoJogo`).</summary>
	public const float BrilhoParado = 0.6f;

	/// <summary>A proporcao da arte (`Titulo.png` tem 1498 x 899). Constante: o lugar do titulo nao espera a textura chegar.</summary>
	public const float Proporcao = 1498f / 899f;

	/// <summary>
	/// OS DOIS NUMEROS DA RECEITA DO TRAILER que o brilho carrega: ele foi assado em volta de um logo de 1180 px de
	/// largura, com 150 px de folga de cada lado (`Tools/brilho_do_titulo.py`). Mudou la, muda aqui.
	/// </summary>
	public const float LarguraDaReceita = 1180f, MargemDoBrilho = 150f;

	public const string CaminhoDoLogo = "res://Assets/UI/Titulo.png";
	public const string CaminhoDoBrilho = "res://Assets/UI/TituloBrilho.png";

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagabertura`): o titulo e posto por NUMERO FIXO de pixel -- o retangulo que ele
	/// tem numa tela de 1280x720 --, e nao pela faixa. Em qualquer outra tela ele sai do centro. Lido pela propria
	/// linha da regra, em <see cref="LugarDaArte"/>.
	/// </summary>
	public static bool PixelFixoDeTeste;

	/// <summary>
	/// RODADA DE CONTROLE (bandeira `--semartedotitulo`): a arte NAO e pedida, e o titulo fica sem desenho. E o braco
	/// "sem" da medida de quanto o pedido da arte custa a fila do `Aquecimento`, que divide a thread de carga com
	/// ele (o resultado esta no cabecalho da classe). Lido pela propria linha do pedido, em <see cref="PedirAArte"/>.
	/// </summary>
	public static bool SemArteDeTeste;

	// =====================================================================
	// A ARTE (do processo: uma leitura, numa thread)
	// =====================================================================
	private static readonly string[] Caminhos = [CaminhoDoLogo, CaminhoDoBrilho];
	private static readonly Texture2D?[] _arte = new Texture2D?[2];
	private static readonly bool[] _resolvida = new bool[2];
	private static bool _pedida;

	/// <summary>O logo, ou nulo enquanto nao chegou (e pra sempre sem janela, ou se a arte nao foi importada).</summary>
	public static Texture2D? Logo => _arte[0];

	/// <summary>O brilho de tras do logo, nas mesmas condicoes.</summary>
	public static Texture2D? Brilho => _arte[1];

	/// <summary>
	/// A ARTE ESTA NO PROJETO E IMPORTADA? Olha o `.import` e o arquivo pra onde ele aponta, sem carregar nada: um
	/// PNG recem-chegado que o Godot ainda nao importou responderia "existe" ao `ResourceLoader` e falharia na carga,
	/// com erro no log de toda corrida.
	/// </summary>
	public static bool ArteImportada => Importada(CaminhoDoLogo) && Importada(CaminhoDoBrilho);

	private static bool Importada(string caminho)
	{
		var ficha = new ConfigFile();
		if (ficha.Load(caminho + ".import") != Error.Ok) return false;
		string destino = ficha.GetValue("remap", "path", "").AsString();
		return destino.Length > 0 && Godot.FileAccess.FileExists(destino);
	}

	/// <summary>
	/// PEDE A ARTE A THREAD DE CARGA, uma vez por processo. Sem janela (ou sem a arte importada) nao pede nada, e as
	/// duas ficam nulas: o node existe, tem lugar na tela, e nao desenha.
	///
	/// SAO CAMINHOS SO DAQUI: ninguem mais os pede, entao nao ha `ResourceLoader.Load` pra bater de frente com a
	/// carga em andamento (a armadilha do cabecalho do `Aquecimento`).
	/// </summary>
	public static void PedirAArte()
	{
		if (_pedida) return;
		_pedida = true;

		// (a rodada de controle -- ver `SemArteDeTeste`. A bandeira e lida AQUI, e nao no `Boot`: o pedido sai do
		// `_Ready` do primeiro titulo, no meio do `Boot._Ready`, antes de ele ler as bandeiras da abertura.)
		SemArteDeTeste |= Array.IndexOf(OS.GetCmdlineArgs(), "--semartedotitulo") >= 0;
		bool daPraPedir = !SemArteDeTeste && DisplayServer.GetName() != "headless" && ArteImportada;
		for (int i = 0; i < Caminhos.Length; i++)
			if (!daPraPedir || ResourceLoader.LoadThreadedRequest(Caminhos[i]) != Error.Ok) _resolvida[i] = true;
	}

	/// <summary>
	/// A ARTE JA CHEGOU (ou nao vem)? Recolhe o que a thread de carga entregou; nada aqui bloqueia -- o mesmo desenho
	/// do `Aquecimento.RecolherAFila`.
	/// </summary>
	public static bool ArteResolvida
	{
		get
		{
			PedirAArte();
			bool tudo = true;
			for (int i = 0; i < Caminhos.Length; i++)
			{
				if (_resolvida[i]) continue;
				ResourceLoader.ThreadLoadStatus estado = ResourceLoader.LoadThreadedGetStatus(Caminhos[i]);
				if (estado == ResourceLoader.ThreadLoadStatus.InProgress) { tudo = false; continue; }
				_resolvida[i] = true;
				if (estado == ResourceLoader.ThreadLoadStatus.Loaded) _arte[i] = ResourceLoader.LoadThreadedGet(Caminhos[i]) as Texture2D;
			}
			return tudo;
		}
	}

	// =====================================================================
	// O LUGAR
	// =====================================================================
	/// <summary>
	/// ONDE A ARTE FICA DENTRO DE UMA FAIXA: centrada, com <see cref="AlturaNaFaixa"/> da altura dela (e nunca mais
	/// larga que <see cref="LarguraMaximaNaFaixa"/> da largura). Funcao pura: a abertura a usa pra saber onde pousar.
	/// </summary>
	public static Rect2 LugarDaArte(Rect2 faixa)
	{
		// (defeito injetado: o retangulo de uma tela de 1280x720, cravado -- ver `PixelFixoDeTeste`)
		if (PixelFixoDeTeste) faixa = new Rect2(0f, 0f, 1280f, 720f * FaixaDoTopo);

		float altura = faixa.Size.Y * AlturaNaFaixa;
		float largura = altura * Proporcao;
		if (largura > faixa.Size.X * LarguraMaximaNaFaixa)
		{
			largura = faixa.Size.X * LarguraMaximaNaFaixa;
			altura = largura / Proporcao;
		}
		return new Rect2(faixa.Position + (faixa.Size - new Vector2(largura, altura)) / 2f, new Vector2(largura, altura));
	}

	/// <summary>O retangulo do brilho em volta de um retangulo de arte: a folga da receita, na escala dele.</summary>
	public static Rect2 LugarDoBrilho(Rect2 arte) => arte.Grow(arte.Size.X * MargemDoBrilho / LarguraDaReceita);

	/// <summary>
	/// DESENHA O TITULO (o brilho e, por cima, o logo) num retangulo de arte. E a mesma chamada no cabecalho e na
	/// abertura: quando ela pousa, os dois desenhos sao o mesmo.
	/// </summary>
	public static void DesenharEm(CanvasItem tela, Rect2 arte, float brilho, float alfa)
	{
		if (Brilho is { } b && brilho > 0.003f) tela.DrawTextureRect(b, LugarDoBrilho(arte), false, new Color(1f, 1f, 1f, brilho * alfa));
		if (Logo is { } l && alfa > 0.003f) tela.DrawTextureRect(l, arte, false, new Color(1f, 1f, 1f, alfa));
	}

	// =====================================================================
	// O NODE
	// =====================================================================
	private static readonly List<TituloDoJogo> _vivos = [];

	/// <summary>
	/// QUANTOS TITULOS ESTAO DESENHADOS AGORA nos menus: os nodes visiveis na arvore e nao apagados. (O da abertura,
	/// enquanto ela corre, conta a parte: `AberturaDoJogo.LogoNaTela`.) Pra bancada cobrar "um titulo so".
	/// </summary>
	public static int NaTela => _vivos.Count(t => t.IsVisibleInTree() && !t.Apagado && !t.IsQueuedForDeletion());

	private bool _apagado;

	/// <summary>
	/// ENQUANTO A ABERTURA CORRE, QUEM DESENHA O TITULO E ELA: o do menu fica apagado ate ela pousar em cima dele --
	/// senao haveria dois na tela enquanto o fundo dela se desfaz. Apagado ele continua com o lugar dele (e pra la
	/// que a abertura sobe).
	/// </summary>
	public bool Apagado
	{
		get => _apagado;
		set
		{
			if (_apagado == value) return;
			_apagado = value;
			QueueRedraw();
		}
	}

	/// <summary>O retangulo da ARTE na tela, em px da base de desenho.</summary>
	public Rect2 ArteNaTela => LugarDaArte(GetGlobalRect());

	public TituloDoJogo()
	{
		Name = "TituloDoJogo";
		// A FAIXA DE CIMA, por ancora: acompanha a tela em qualquer tamanho.
		AnchorRight = 1f;
		AnchorBottom = FaixaDoTopo;
		// Enfeite: nunca fica entre o mouse e um botao.
		MouseFilter = MouseFilterEnum.Ignore;
		// O projeto desenha tudo com filtro "nearest" (arte de pixel); o logo e arte lisa, reduzida.
		TextureFilter = TextureFilterEnum.LinearWithMipmaps;
	}

	public override void _EnterTree() => _vivos.Add(this);

	public override void _ExitTree() => _vivos.Remove(this);

	public override void _Ready() => SetProcess(!ArteResolvida);

	/// <summary>So corre enquanto a arte nao chegou: recolhe, redesenha e para.</summary>
	public override void _Process(double delta)
	{
		if (!ArteResolvida) return;
		SetProcess(false);
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_apagado) return;
		DesenharEm(this, LugarDaArte(new Rect2(Vector2.Zero, Size)), BrilhoParado, 1f);
	}
}
