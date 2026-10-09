using Godot;

namespace Jandirus.Client;

/// <summary>
/// O AQUECIMENTO DO PROCESSO -- o trabalho de UMA VEZ POR SESSAO, feito enquanto o jogador ainda
/// esta no lobby em vez de no pior instante possivel.
///
/// ============================ O NUMERO QUE JUSTIFICA ESTA CLASSE ============================
/// Medido com janela, quadro a quadro, do clique em "Entrar no mundo" ate o corpo aparecer -- as
/// duas colunas sao o MESMO binario, separadas so pelo `--semaquecimento` (ver `RoboDeCarga`):
///
///                                 sem aquecimento     com aquecimento
///     1a entrada do processo ....... 1586 ms  ......... 866 ms
///       -- rede + montagem .......... 899 ms  ......... 486 ms
///       -- o quadro que desenhou .... 687 ms  ......... 381 ms
///     2a entrada, mesmo processo .... 575 ms  ......... 457 ms
///
/// (866 ms e o jogador que gastou alguns segundos no lobby. Quem loga em ~1 s pega o aquecimento
/// pela metade e entra em 922 ms -- ver <see cref="Concluir"/>. Nunca fica PIOR que sem aquecimento:
/// e o mesmo trabalho, feito antes do clique em vez de depois dele.)
///
/// **720 ms saem da frente do jogador.** Nao eram carga de zona (essa custa ~75 ms, medidos pelo
/// proprio `[perf] TOTAL` do `World.CarregarZona`) nem servidor. Eram trabalho de UMA VEZ POR
/// PROCESSO, e e por isso que a segunda entrada sempre foi barata -- ela ja encontrava tudo pronto:
///
///   * `res://Assets/Maps/tileset.tres` -- ~630 ms. Nao e parse (274 ms em texto contra 275 em
///     binario, ja medido no `World._tilesetVivo`): e montar 125 fontes de atlas, 163 PNG e
///     ~7.800 tiles. Ele e o passageiro escondido do `World._Ready`.
///   * os SHADERS do mundo -- cada um paga um "Shader cache miss" na primeira vez. (Medido: o
///     cache de shader EM DISCO nao muda o quadro caro -- 502 ms com ele frio, 502 ms com ele
///     quente. Ou seja compilar shader NAO era o gargalo, e vale registrar pra ninguem gastar uma
///     sessao ali.)
///   * as CHAPAS DO BONECO DE VIDA do HUD -- 10 folhas que so aparecem quando o HUD monta, isto
///     e, dentro do mesmo quadro em que o corpo nasce.
///   * as FOLHAS DE APARENCIA dos NPC -- ver <see cref="PedirAsAparencias"/>. Sao elas que sobraram
///     depois do tileset, e sozinhas valeram outros ~130 ms do quadro caro (502 -> 346 ms).
///
/// ============================ POR QUE ISTO E CONSERTO E NAO MAQUIAGEM ============================
/// Nada disto depende do `JoinAccepted`: o tileset e o mesmo pra todo mundo, os shaders sao os
/// mesmos, as chapas do HUD sao as mesmas. Nao ha uma unica informacao do personagem em nada que
/// esta lista carrega -- e por isso pode ser carregado ANTES de existir personagem.
///
/// O padrao ja existia no projeto e so nao alcancava o peso de verdade: o `Boot.PrepararCriacao`
/// monta a tela de criacao escondida por `CallDeferred`, com o comentario *"o jogador esta lendo a
/// lista de slots -- e o momento em que uns milissegundos nao custam nada"*. Aqui e o mesmo
/// momento, com os milissegundos que custam.
/// =================================================================================================
///
/// ============================ E O QUE CARREGAR NAO AQUECE: O ENSAIO ============================
/// A fila abaixo poe ARQUIVOS na memoria. Um efeito desenhado por shader tem dois custos de primeiro
/// uso que nenhum arquivo carregado paga -- compilar o shader e montar a pipeline -- e eles caiam no
/// primeiro tiro de ki que acertava: um terco de segundo com a tela parada. Entao, depois da fila, o
/// aquecimento ENSAIA: monta e desenha cada efeito de ki uma vez, num palco fora da tela. A medicao e
/// o porque de cada escolha estao em <see cref="Atos"/>; a bancada e a `--diagestouro`.
///
/// A CINEMATICA DE TRANSFORMACAO ENTROU NO MESMO PALCO, pelo mesmo motivo: o quadro em que a primeira do
/// processo nascia parava a tela esperando o shader da chama. Ver <see cref="AtosDaCena"/>; a bancada e a
/// rodada `--temas` da mesma `--diagestouro`.
///
/// E O QUE A CENA USA DEPOIS DE NASCER TAMBEM PASSA POR AQUI: as crateras, a fumaca e os sons dos beats entram na
/// fila (<see cref="ArquivosDaCena"/>), e o chao que o beat que assume abre e ensaiado (<see cref="AtosDoChao"/>).
///
/// E OS RAIOS DA FORMA, que estavam na lista pelo mesmo engano dos do ki: o shader deles era carregado aqui, e as
/// pipelines dele so nasciam no quadro do primeiro feixe de chao e no do primeiro corpo que crepita. Ver
/// <see cref="AtosDosRaios"/>.
///
/// E O BORRAO DO RASTRO, que nem na lista estava: o shader dele era lido do disco, compilado e desenhado no quadro da
/// primeira investida do processo. Ver <see cref="AtosDoBorrao"/>.
///
/// E A MIRAGEM DO ZANZOKEN, que estava de fora do mesmo jeito: o shader do vulto de quem tem a Afterimage era lido,
/// compilado e desenhado no quadro do primeiro Zanzoken do processo. Ver <see cref="AtosDaMiragem"/>.
///
/// E O RELAMPAGO, A CHUVA E O BONECO, que nao sao efeito de ki nem de forma e tinham a mesma conta: a pipeline de cada
/// um so nascia no primeiro raio que caia na tela, no primeiro pingo e na primeira luz que alcancava um corpo. Ver
/// <see cref="AtosDoRelampago"/>.
///
/// E OS SEIS QUE NENHUMA BANCADA PUNHA NA TELA -- a gota do transe, a nevoa de altitude, a nebulosa do Ultra Instinto, a
/// tela do embate, o planeta visto do espaco e o estouro dele: quatro nem na lista estavam, como o borrao, e os outros
/// dois estavam nela pelo engano dos do ki. Ver <see cref="AtosDaGota"/>.
/// ================================================================================================
///
/// ============================ EM OUTRA THREAD, E ISSO NAO E ENFEITE ============================
/// `ResourceLoader.Load` cru aqui trocaria um congelamento por outro: o lobby ficaria 600 ms sem
/// responder bem quando o jogador esta clicando nele. O `LoadThreadedRequest` poe o trabalho numa
/// thread de carga e devolve o quadro na hora; o `_Process` daqui so RECOLHE o que ficou pronto.
/// ===============================================================================================
///
/// ============================ E SE O JOGADOR FOR MAIS RAPIDO QUE A THREAD? ============================
/// A primeira versao desta classe respondia "nada quebra, o `ResourceLoader` espera a carga que ja
/// comecou". **Era mentira, e travava o jogo.** Um cliente automatico (`--host`, o modo das outras
/// quarenta bancadas) entra no mundo no primeiro segundo, e o `ResourceLoader.Load` do
/// `World._Ready` batia de frente com o `LoadThreadedRequest` do MESMO tileset ainda no ar: a thread
/// principal parava ali e nunca mais saia. Medido: 240 s sem uma linha de log depois de
/// `[client] entrei como id 10`, e a mesma rodada com `--semaquecimento` terminando em 40 s.
///
/// O conserto e <see cref="Concluir"/>: antes de qualquer um poder pedir estes arquivos, a fila
/// pendente e RECOLHIDA com `LoadThreadedGet` -- que e a porta feita pra esperar uma carga em
/// andamento, e nao competir com ela. Depois disso nao ha nada no ar pra colidir, e todo
/// `ResourceLoader.Load` do jogo cai no cache.
///
/// A licao vale mais que o conserto: **"provavelmente nao acontece" nao e uma corrida resolvida.**
/// Aqui a corrida so nao aparecia na janela porque o jogador humano gasta segundos no lobby -- e as
/// bancadas, que entram em milissegundos, caiam nela todas as vezes.
/// =======================================================================================================
///
/// ============================ SEGURAR A REFERENCIA E O QUE MANTEM O CACHE ============================
/// O cache do Godot e por contagem de referencia: ele solta o recurso no instante em que a ultima
/// some. Carregar e jogar fora seria aquecer o forno e abrir a porta. Por isso <see cref="_presos"/>
/// e **estatica** -- o custo e do PROCESSO, e relogar nao deve repaga-lo (a mesma razao pela qual o
/// `World._tilesetVivo` e estatico).
///
/// OS SONS DE EFEITO TEM MAIS UM DONO, a porta `SonsPresos`: ela segura todo som que o jogo toca, venha ele desta
/// fila ou do disco. A fila continua valendo pelo que so ela faz -- ler o arquivo ANTES da hora, numa thread.
///
/// E O SCRIPT DE UMA CLASSE C# DE NODE TAMBEM E UM RECURSO, com a mesma regra: so vive enquanto alguem o segura, e quem
/// o segura sao as instancias da classe. Os das classes que nascem e morrem no meio do jogo ganham dono aqui -- ver
/// <see cref="NodesDeVidaCurta"/>.
/// ====================================================================================================
/// </summary>
public partial class Aquecimento : Node
{
	/// <summary>
	/// O QUE VALE A PENA AQUECER, em ordem de custo. So entra aqui o que **nao depende do
	/// personagem**: se um item desta lista precisar saber quem entrou, ele esta no lugar errado.
	/// </summary>
	private static readonly string[] Lista =
	[
		// O MONSTRO. Sozinho ele e ~630 ms dos ~1253 -- ver o cabecalho da classe.
		"res://Assets/Maps/tileset.tres",

		// OS SHADERS DO MUNDO, na ordem em que a entrada os pede. Cada um paga a compilacao uma
		// vez por processo, e aqui ela sai da frente do jogador.
		"res://Assets/Shaders/Clima.gdshader",            // Iluminacao/ClimaNaTela, no MontarCenario
		"res://Assets/Shaders/Raio.gdshader",             // idem
		"res://Assets/Shaders/Queda.gdshader",            // idem: o material do que cai (recorta o teto)
		"res://Assets/Shaders/Personagem.gdshader",       // CharacterVisual: o corpo
		"res://Assets/Shaders/Aura.gdshader",             // SpriteDeAura
		"res://Assets/Shaders/NebulosaDaForma.gdshader",  // NebulosaDaForma
		"res://Assets/Shaders/RaioDaForma.gdshader",      // RaiosDaForma
		"res://Assets/Shaders/Embate.gdshader",           // ClashQte, montado junto com o resto

		// OS QUATRO DO KI. Nao sao da entrada -- sao do PRIMEIRO TIRO, que e pior: o travamento aparece no
		// meio de uma luta. ESTAR AQUI TIRA DO QUADRO DO TIRO A LEITURA DO ARQUIVO, E SO ELA: o shader so e
		// compilado quando o primeiro material dele nasce, e a pipeline quando ele e desenhado. Quem faz as
		// duas coisas antes da luta e o ensaio -- ver `Atos`, mais abaixo, onde esta a medicao.
		PintorDeKi.ShaderDoFeixe,                         // ProjetilDesenhado: todo raio
		PintorDeKi.ShaderDaEsfera,                        // ProjetilDesenhado: toda bola; CargaDeRaioVisual
		PintorDeKi.ShaderDoChoque,                        // ChoqueDeKi: a estrela do embate
		PintorDeKi.ShaderDoEstouro,                       // EstouroDeKi: todo tiro que acerta

		// OS DOIS QUE ESTAVAM DE FORA e caiam no mesmo quadro do primeiro tiro que acerta. O anel de choque
		// (`CombatFx.Onda`) era carregado na hora, na thread principal. A folha da faisca do golpe
		// (`CombatFx.Impacto`) pior: ninguem a segurava, entao ela saia do cache junto com a ultima faisca
		// e voltava do disco no golpe seguinte.
		"res://Assets/Shaders/Impacto.gdshader",
		"res://Assets/Sprites/Misc/effects/attackspark.tres",

		// AS CHAPAS DO BONECO DE VIDA. Sao 10 folhas, e todas caem no mesmo quadro em que o `Hud`
		// e montado -- ou seja, no quadro do corpo. Ver `BodyDoll.Quadro`.
		"res://Assets/Sprites/Misc/HUD/health_hud.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_base.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_head.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_torso.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_abdomen.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_reproductive_organs.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_leftarm.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_rightarm.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_leftleg.tres",
		"res://Assets/Sprites/Misc/HUD/health_hud_rightleg.tres",

		// as barras do HUD, pelo mesmo motivo das chapas
		"res://Assets/Sprites/Misc/HUD/KiBar.tres",
		"res://Assets/Sprites/Misc/HUD/StaBar.tres",
		"res://Assets/Sprites/Misc/HUD/GKiBar.tres",
	];

	/// <summary>O que ja veio. Estatica de proposito -- ver o cabecalho.</summary>
	private static readonly List<Resource> _presos = [];

	/// <summary>Quem esta aquecendo agora. Nulo depois que a fila esvazia.</summary>
	private static Aquecimento? _vivo;

	/// <summary>Ja pedimos nesta sessao? Relogar nao repete o pedido: o cache continua quente.</summary>
	private static bool _pedido;

	/// <summary>Verdadeiro quando a fila esvaziou (com ou sem falha em algum item).</summary>
	public static bool Terminou { get; private set; }

	/// <summary>Quantos itens ja foram recolhidos -- pra bancada e pro log.</summary>
	public static int Prontos => _presos.Count;

	/// <summary>
	/// Quantos itens foram PEDIDOS ao todo -- publico pra bancada dizer "x de y".
	///
	/// E o pedido e nao o tamanho da <see cref="Lista"/> porque ha DUAS filas: a fixa e as folhas de
	/// aparencia, que saem do `visual.json` e por isso so sao contadas em tempo de execucao.
	/// </summary>
	public static int Total { get; private set; }

	/// <summary>Quanto tempo o aquecimento levou de ponta a ponta, em ms. Zero enquanto nao acabou.</summary>
	public static double Ms { get; private set; }

	private static ulong _comecou;

	/// <summary>Quantos itens a PRIMEIRA fila pediu: a <see cref="Lista"/> e as amostras de golpe. O resto do <see cref="Total"/> sao as folhas de aparencia.</summary>
	private static int _fixos;

	/// <summary>O que ainda nao voltou. Esvazia conforme o `_Process` recolhe.</summary>
	private readonly List<string> _faltando = [];

	private readonly List<string> _falharam = [];

	/// <summary>A segunda fila ja foi pedida? Ver <see cref="PedirAsAparencias"/>.</summary>
	private bool _aparenciasPedidas;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro`, rodada `--semensaio`): o aquecimento carrega os recursos e
	/// NAO ensaia os efeitos de ki -- o jogo de antes: o shader de cada efeito e compilado quando o primeiro
	/// material dele nasce e a pipeline quando ele e desenhado pela primeira vez, ou seja no quadro do
	/// primeiro tiro que acerta. Sempre falso em jogo.
	/// </summary>
	public static bool SemEnsaioDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --temas`, rodada `--semensaiodacena`): o aquecimento NAO carrega as
	/// folhas de chama nem ensaia a cinematica de transformacao -- o jogo de antes: a primeira cena do processo le
	/// do disco a folha da chama, roda pela primeira vez o codigo dela, e nasce e e desenhada no mesmo quadro em que
	/// o shader da aura comeca a ser compilado, com a thread principal esperando por ele. Tudo no quadro em que a
	/// transformacao comeca. Sempre falso em jogo.
	/// </summary>
	public static bool SemEnsaioDaCenaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --temas`, rodada `--semadiantaracena`): nada do que a cinematica de
	/// transformacao usa DEPOIS de nascer chega antes da hora -- o jogo de antes: a cratera, a fumaca e os sons dos
	/// beats sao lidos do disco pela thread principal no quadro do beat que os usa, a fumaca nao tem dono (volta do
	/// disco depois de sumir e de o coletor do .NET passar) e o penteado da forma e lido no quadro em que o cabelo
	/// pisca pela primeira vez. Sempre falso em jogo.
	///
	/// QUEM O LE: esta classe (a fila do <see cref="ArquivosDaCena"/> e o ato do <see cref="AtosDoChao"/>), o
	/// `Decalques.TexturaSolta` (o dono da fumaca) e o `Transformacao.AdiantarAsFolhasDaForma` (as folhas da forma).
	/// </summary>
	public static bool SemAdiantarACenaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --pecas`, rodada `--semensaiodoborrao`): o aquecimento NAO carrega o
	/// shader do borrao nem o ensaia -- o jogo de antes: o primeiro borrao do processo (a primeira investida de uma luta,
	/// ou o primeiro passo de uma corrida) le o `Borrao.gdshader` do disco pela thread principal, cria o primeiro
	/// material dele e o desenha no mesmo quadro, com a tela esperando o shader compilar e a pipeline ser montada.
	/// Sempre falso em jogo.
	/// </summary>
	public static bool SemEnsaioDoBorraoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --pecas`, rodada `--semensaiodamiragem`): o aquecimento NAO carrega o
	/// shader da miragem do Zanzoken nem a ensaia -- o jogo de antes: a primeira miragem do processo (o primeiro Zanzoken,
	/// ou o primeiro arranque de quem tem a Afterimage) le o `Zanzoken.gdshader` do disco pela thread principal, cria os
	/// primeiros materiais dele e os desenha no mesmo quadro, com a tela esperando o shader compilar e a pipeline ser
	/// montada. Sempre falso em jogo.
	/// </summary>
	public static bool SemEnsaioDaMiragemDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --temas`, rodada `--semensaiodosraios`): o aquecimento NAO ensaia os
	/// raios da forma -- o jogo de antes: ninguem desenha um raio de forma antes da hora, e a pipeline do
	/// `RaioDaForma.gdshader` e montada no quadro em que os feixes do chao de uma cinematica aparecem pela primeira
	/// vez no processo, e de novo (e outra pipeline) no do primeiro corpo que crepita. Sempre falso em jogo.
	/// </summary>
	public static bool SemEnsaioDosRaiosDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --temas`, a rodada de NOITE com `--estrelacomluz`): a
	/// estrela do embate do ensaio nasce com a luz propria dela -- o jogo de antes: quando o MUNDO esta escuro na hora
	/// do ensaio (o login mais rapido que ele, numa noite ou numa tempestade), essa luz acende o lado ESCURO do palco,
	/// e os atos que vem depois dela nao montam ali a pipeline do item sem luz -- a que o jogo usa de dia. Sempre falso
	/// em jogo.
	/// </summary>
	public static bool EstrelaComLuzDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --temas`, rodada `--semensaiodorelampago`): o aquecimento NAO ensaia o
	/// relampago -- o jogo de antes: o risco do raio nasce invisivel com o mundo, e a pipeline do `Raio.gdshader` e
	/// montada no quadro em que o primeiro raio do processo cai dentro da tela. Sempre falso em jogo.
	/// </summary>
	public static bool SemEnsaioDoRelampagoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --temas --ceu`, rodada `--semensaiodachuva`): o aquecimento NAO ensaia a
	/// chuva -- o jogo de antes: a pipeline do `Queda.gdshader` so e montada no quadro em que o primeiro clima que
	/// chove (ou neva, ou venta areia) comeca, no meio do jogo. Sempre falso em jogo.
	/// </summary>
	public static bool SemEnsaioDaChuvaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro`, a bola de NOITE e a `--temas --raioscomluz`, com `--semensaiodocorpo`):
	/// o aquecimento NAO ensaia o corpo -- o jogo de antes: a pipeline do boneco COM luz em cima e montada no quadro em
	/// que a primeira luz alcanca um corpo (de noite, a luz de ki do primeiro tiro). Sempre falso em jogo.
	/// </summary>
	public static bool SemEnsaioDoCorpoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --avulsos`, rodada `--semensaiodagota`): o aquecimento NAO carrega o shader
	/// da gota nem a ensaia -- o jogo de antes: a primeira onda do processo (a entrada na meditacao profunda) le o
	/// `Gota.gdshader` do disco pela thread principal, cria o primeiro material dele e o desenha no mesmo quadro, com a
	/// tela esperando o shader compilar e a pipeline ser montada; e a gemea iluminada da pipeline nasce na primeira onda
	/// com uma luz na tela. Sempre falso em jogo.
	/// </summary>
	public static bool SemEnsaioDaGotaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --avulsos`, rodada `--semensaiodanevoa`): o aquecimento NAO carrega o
	/// shader da nevoa de altitude nem a ensaia -- o jogo de antes: o primeiro voo do processo le o `Altitude.gdshader` do
	/// disco pela thread principal, cria o primeiro material dele e o desenha no mesmo quadro; e a gemea iluminada da
	/// pipeline nasce no primeiro voo com uma luz na tela. Sempre falso em jogo.
	/// </summary>
	public static bool SemEnsaioDaNevoaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --avulsos`, rodada `--semensaiodanebulosa`): o aquecimento NAO ensaia a
	/// nebulosa -- o jogo de antes: o quad dela nasce invisivel com cada corpo, e a pipeline do `NebulosaDaForma.gdshader`
	/// e montada no quadro em que o primeiro Ultra Instinto do processo acende. Sempre falso em jogo.
	/// </summary>
	public static bool SemEnsaioDaNebulosaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --avulsos`, rodada `--semensaiodoembate`): o aquecimento NAO ensaia a tela
	/// do embate -- o jogo de antes: ela nasce invisivel com o mundo, e a pipeline do `Embate.gdshader` e montada no quadro
	/// em que o primeiro embate do processo comeca. Sempre falso em jogo.
	/// </summary>
	public static bool SemEnsaioDoEmbateDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --avulsos`, rodada `--semensaiodoplaneta`): o aquecimento NAO carrega os
	/// dois shaders do planeta nem os ensaia -- o jogo de antes: o `PlanetaMorrendo.gdshader` e lido do disco pela thread
	/// principal quando o primeiro disco de planeta do processo nasce (a primeira chegada no espaco), e o
	/// `EstouroDePlaneta.gdshader` no primeiro estouro (a Final Explosion, a Aura da Destruicao, um mundo que morre), cada
	/// um com o primeiro material e a pipeline no mesmo quadro; e a gemea iluminada de cada um nasce na primeira luz que
	/// o alcanca. Sempre falso em jogo.
	/// </summary>
	public static bool SemEnsaioDoPlanetaDeTeste;

	/// <summary>O ensaio dos efeitos de ki ja rodou inteiro neste processo? Pra bancada e pro log.</summary>
	public static bool Ensaiou { get; private set; }

	/// <summary>
	/// ESPIA DE BANCADA: um ato do ensaio acabou de ser montado -- o nome dele e os ms das duas chamadas (o lado escuro e
	/// o iluminado do palco). E como a `--diagestouro --pecas` da nome ao quadro de cada ato e diz quanto ele pesa onde
	/// e pago. Ninguem assina em jogo.
	/// </summary>
	public static event Action<string, double>? EspiaoDeAto;

	// =====================================================================
	// O ENSAIO -- os efeitos de ki montados e desenhados uma vez, fora da tela
	// =====================================================================
	/// <summary>
	/// ============================ CARREGAR O SHADER NAO COMPILA NADA ============================
	/// Os quatro shaders do ki estao na <see cref="Lista"/> desde que nasceram, com a nota "sem isto a
	/// compilacao cairia no quadro em que alguem dispara". Caia do mesmo jeito: o primeiro tiro de ki
	/// que acertava travava a tela por um terco de segundo. Medido com janela pela `--diagestouro`
	/// (2026-10-08), o custo do primeiro uso de um efeito tem TRES donos, e o arquivo carregado nao e
	/// nenhum deles:
	///
	///   * o shader e COMPILADO quando nasce o primeiro MATERIAL que o usa (~11 ms cada, dentro da chamada);
	///   * a PIPELINE e montada quando ele e DESENHADO pela primeira vez (12 a 30 ms, no desenho do quadro);
	///   * o shader de PARTICULA e compilado dentro da chamada que cria o `ParticleProcessMaterial` -- os
	///     tres da `PoeiraDeEstrago` somavam 321 ms, e eram eles a travada.
	///
	/// (Numeros do cache de shader em disco VAZIO, `user://shader_cache`: e o de quem abre o jogo pela
	/// primeira vez, e o de toda bancada e toda tomada do trailer, que rodam com o APPDATA desviado. Com o
	/// cache cheio o primeiro estouro custava 46 ms em vez de 370 -- menos, e ainda cinco quadros.)
	///
	/// Entao o que aquece e USAR. Cada efeito e montado pela MESMA porta que o jogo usa e desenhado por
	/// alguns quadros num palco que ninguem ve; quando o primeiro tiro de verdade sair, shader, pipeline
	/// e particula ja existem.
	///
	/// ============================ O PALCO E UM `SubViewport`, E NAO UM CANTO DA TELA ============================
	/// A pipeline e montada por FORMATO do alvo de desenho, e um `SubViewport` com o `UseHdr2D` e o
	/// `Msaa2D` da janela tem o formato dela: a pipeline montada aqui e a que o mundo acha pronta. A
	/// bancada confere pela contagem do proprio Godot -- com o ensaio feito, a primeira bola do dia nao
	/// monta pipeline nenhuma (sem ele, uma) e o primeiro estouro monta a mesma uma de qualquer estouro
	/// (sem ele, tres). E por ser outro viewport ele nao depende do que esta na janela: serve igual no
	/// lobby e nos primeiros quadros do mundo, que e onde o ensaio cai quando o login foi mais rapido que
	/// ele (os robos).
	///
	/// ============================ CADA EFEITO NASCE DUAS VEZES, E UM DELES DEBAIXO DE UMA LUZ ============================
	/// O Godot monta uma pipeline pro item que nenhuma luz alcanca e OUTRA pro item iluminado -- mesmo com
	/// o shader `unshaded`. De dia o tiro e do primeiro tipo; de noite, com a luz dele mesmo e das auras em
	/// volta, e do segundo. O lado esquerdo do palco fica no escuro e o direito debaixo de uma
	/// `PointLight2D`: as duas pipelines de cada efeito saem do mesmo ensaio. Medido tirando a luz do
	/// palco: ele monta 8 pipelines em vez de 15, e a primeira bola da noite volta a pagar 4 ms de desenho.
	/// (Sao so 4 porque o caro e o shader, que a esta altura ja esta compilado -- e ainda assim e meio
	/// quadro, e de graca.)
	///
	/// ============================ O LADO ESCURO TEM QUE CONTINUAR ESCURO ============================
	/// NENHUM ATO PODE TRAZER LUZ PROPRIA PRO PALCO. A luz de um efeito de ki (`LuzDeKi`) so nasce quando o MUNDO esta
	/// escuro (`Iluminacao.ForcaDaNoite`, que e do processo e nao do palco), e o ensaio cai nos primeiros quadros do
	/// mundo quando o login foi mais rapido que ele. O tiro ja nascia aqui sem luz (`SemLuz`); a estrela do embate
	/// nao -- e a dela tem 121 px de raio e fica de pe ate o palco sair. Num mundo que comeca de noite ou em
	/// tempestade ela acendia o lado escuro inteiro, e os atos seguintes (o estouro, o anel, o escudo, os raios da
	/// forma) so montavam a pipeline do item ILUMINADO: a "de dia" de cada um ficava pra hora do primeiro uso.
	///
	/// MEDIDO em 2026-10-09, pela contagem do proprio motor: o ensaio com o login de robo montava 25 pipelines com o
	/// mundo claro e 20 com ele escuro, e as que faltavam nasciam em jogo, no primeiro uso. Achado pela regua dos raios
	/// da forma (`--diagestouro --temas`), que reprovou sozinha durante os 24 minutos em que o clima natural do
	/// meio-dia foi tempestade. Quem joga quase nunca cai nisso -- o ensaio acaba no lobby em menos de dois segundos, e
	/// no lobby nao ha mundo escuro --, mas toda bancada e toda tomada do trailer entram no mundo antes dele. A regua e
	/// a rodada dos temas feita de NOITE (26 pipelines no ensaio, nenhuma nos tres quadros dela); o defeito injetado e
	/// o <see cref="EstrelaComLuzDeTeste"/> (20, e uma em cada quadro).
	///
	/// ============================ UM ATO POR QUADRO ============================
	/// O ensaio custa o que o primeiro tiro custava, e a diferenca e ONDE: no lobby, em vez de no meio de
	/// uma luta. Medido: com o cache de shader em disco vazio, meio segundo de montagem ao todo, e 0,4 a
	/// 0,47 s dele sao o ato da poeira -- um quadro so, uma vez por instalacao, um segundo depois de a
	/// janela abrir. Com o cache cheio (toda vez que se abre o jogo depois da primeira) sao 86 ms ao todo
	/// e 33 no pior ato. Um ato por quadro reparte o resto; a poeira nao se reparte porque e uma chamada so.
	///
	/// A ORDEM NAO E DECORATIVA: a poeira vem primeiro porque o quadro dela e o comprido, e um efeito montado
	/// antes envelheceria um terco de segundo nele; os que vivem menos (a faisca dura 0,2 s) ficam pro fim.
	/// ====================================================================================================================
	/// </summary>
	private static readonly (string Nome, Action<Node2D, Vector2> Monta)[] Atos =
	[
		("a poeira do estouro", PoeiraDeEstrago.Ensaiar),
		("a bola", (pai, onde) => PorOTiro(pai, onde, Jandirus.Core.Combat.TipoDeProjetil.Blast)),
		("o raio", (pai, onde) => PorOTiro(pai, onde, Jandirus.Core.Combat.TipoDeProjetil.Beam)),
		("a estrela do embate", PorAEstrela),
		("o estouro", (pai, onde) => EstouroDeKi.Soltar(pai, onde, 14f, CorDoEnsaio)),
		("o anel de choque", (pai, onde) => CombatFx.Onda(pai, onde, 60f, CorDoEnsaio)),
		("a faisca e o escudo", (pai, onde) =>
		{
			CombatFx.Impacto(pai, onde, 1.3f, CorDoEnsaio);
			CombatFx.Escudo(pai, onde, CorDoEnsaio);
		}),
	];

	/// <summary>
	/// ============================ E A CINEMATICA DE TRANSFORMACAO, QUE TAMBEM SO SE AQUECE USANDO ============================
	/// O quadro em que a primeira cinematica de transformacao do processo nascia custava 41 a 43 ms de trabalho
	/// contra 4 das seguintes: a tela parava no instante em que a transformacao comeca. A conta inteira esta no
	/// `Transformacao.Ensaiar`; o grosso dela (21 a 24 ms) era a thread principal esperando o shader da chama, que o
	/// Godot so comeca a compilar quando o primeiro material dele nasce -- o mesmo defeito dos efeitos de ki, num
	/// shader que ja estava na <see cref="Lista"/> pelo mesmo engano.
	///
	/// O ATO NASCE UMA CENA DE VERDADE, pela porta de producao, e por isso paga junto o que nao e shader: o codigo
	/// da cena rodando pela primeira vez, que o .NET compila ali (9 dos 42 ms). Vem ANTES dos atos de ki porque nada
	/// nele envelhece (a cena do ensaio nao roda) e o quadro dele e comprido, como o da poeira.
	///
	/// E UMA LISTA A PARTE pra a rodada de injecao poder tira-la sozinha: ver <see cref="SemEnsaioDaCenaDeTeste"/>.
	/// ====================================================================================================================
	/// </summary>
	private static readonly (string Nome, Action<Node2D, Vector2> Monta)[] AtosDaCena =
	[
		("a cinematica que nasce", Transformacao.Ensaiar),
	];

	/// <summary>
	/// ============================ E O CHAO QUE O BEAT QUE ASSUME ABRE ============================
	/// O instante mais visto da cinematica -- a forma fica, o chao abre, a tela estoura em clarao -- planta uma cratera
	/// e a fumaca dela, e a primeira cratera do processo roda pela primeira vez o caminho inteiro do `Decalques`.
	/// Medido em 2026-10-08 pela `--diagestouro --temas --cenas` (ver o `Decalques.Ensaiar`): com os dois arquivos ja
	/// na memoria, sobravam 3 a 4 ms de codigo de primeira vez naquele quadro. O ato planta as duas coisas pela porta
	/// de producao, num `Decalques` so do palco.
	///
	/// DEPOIS DOS ATOS DA CENA e antes dos de ki, pela mesma razao deles: nada nele envelhece mal durante o quadro
	/// comprido da poeira. E UMA LISTA A PARTE pra a rodada de injecao dele (<see cref="SemAdiantarACenaDeTeste"/>)
	/// poder tira-lo sem levar o ensaio da cena junto.
	/// ==============================================================================================
	/// </summary>
	private static readonly (string Nome, Action<Node2D, Vector2> Monta)[] AtosDoChao =
	[
		("a cratera e a fumaca", Decalques.Ensaiar),
	];

	/// <summary>
	/// ============================ E O BORRAO DO RASTRO, QUE NEM NA LISTA ESTAVA ============================
	/// Toda investida e toda corrida largam copias borradas do corpo (`RastroDeCorrida`), e o shader do borrao era o
	/// unico efeito de luta que o aquecimento nao conhecia: nem o arquivo vinha do lobby. O primeiro borrao do processo
	/// lia o `Borrao.gdshader` do disco, criava o primeiro material dele e o desenhava -- tudo no quadro da primeira
	/// investida, com a thread principal esperando o shader compilar. MEDIDO em 2026-10-09 pela `--diagestouro --pecas`
	/// (a conta partida esta no `BorraoDirecional.Ensaiar`): 31 a 40 ms de trabalho nesse quadro, contra 3 a 5 de um
	/// borrao qualquer. DEPOIS: 5,5 a 8,4, e nenhuma pipeline nasce nele.
	///
	/// O ATO E UM SPRITE PARADO, borrado pela mesma chamada que o rastro faz em cada camada de cada foto: e ela que cria
	/// o primeiro material (o shader compila ali) e, desenhada dos dois lados do palco, monta as duas pipelines dele --
	/// a do sprite sem luz e a do iluminado.
	///
	/// O SHADER ENTRA NA PRIMEIRA FILA por uma linha propria do <see cref="_Ready"/>, e nao pela <see cref="Lista"/>: e o
	/// que deixa a rodada de injecao tira-lo junto com o ato (<see cref="SemEnsaioDoBorraoDeTeste"/>).
	///
	/// ANTES DOS EFEITOS DE KI: e um sprite parado, e nada nele envelhece no quadro comprido da poeira.
	///
	/// O PRECO: um arquivo de 700 bytes a mais na fila, duas pipelines e um quadro do lobby com 37 ms em vez de 8 -- os
	/// mesmos 27 de espera pelo shader, pagos aqui. (Com o cache de shader do Godot cheio, 7 ms; com o driver de video
	/// frio, 127: as duas pipelines montadas do zero.) O ensaio inteiro, com o login de gente, foi de 871 pra 896 ms -- uma
	/// corrida de cada.
	///
	/// O QUE O ATO NAO PAGA: o codigo do rastro rodando pela primeira vez (2 a 3 ms no primeiro borrao do processo). O
	/// palco tem um sprite, e nao um corpo correndo.
	/// ================================================================================================================
	/// </summary>
	private static readonly (string Nome, Action<Node2D, Vector2> Monta)[] AtosDoBorrao =
	[
		("o borrao do rastro", BorraoDirecional.Ensaiar),
	];

	/// <summary>
	/// ============================ E OS RAIOS DA FORMA, QUE TEM DOIS DESENHOS ============================
	/// O `RaioDaForma.gdshader` esta na <see cref="Lista"/> desde que nasceu, e isso so tirava do caminho a leitura do
	/// arquivo: a PIPELINE dele era montada no primeiro desenho. E ele e desenhado de dois jeitos, cada um com a sua:
	///
	///   * por PARTICULA -- a faisca que corre no corpo de quem esta transformado (`RaiosDaForma`);
	///   * por SPRITE -- os oito feixes de chao de uma cinematica (`Transformacao.MontarOsFeixes`).
	///
	/// O motor monta uma pipeline por VARIANTE do shader (retangulo de sprite e malha de particula sao variantes
	/// diferentes) e, como nos efeitos de ki, outra pro item que tem luz em cima. Dois desenhos, dois lados do palco:
	/// quatro pipelines, e o mundo de dia so pagava duas delas -- uma no quadro do primeiro feixe de chao do processo,
	/// outra no do primeiro corpo que crepita.
	///
	/// QUANTO CUSTAVA depende de o DRIVER de video ja ter visto o shader, e por isso a bancada so pegava as vezes (a
	/// conta inteira esta no cabecalho do `RoboDoPrimeiroEstouro`, bloco "E OS RAIOS DA FORMA"). MEDIDO em 2026-10-09
	/// pela `--diagestouro --temas`, o desenho de cada um dos dois quadros: 1 a 2 ms com o cache do driver quente, 19 a
	/// 23 com ele frio -- o de quem abre o jogo pela primeira vez, ou trocou de driver. DEPOIS: nenhuma pipeline nasce
	/// neles, e o desenho custa 0,3 a 0,6 ms nos dois casos.
	///
	/// O PRECO: quatro pipelines a mais no ensaio (22 em vez de 18 quando ele cabe inteiro no lobby, o login de gente)
	/// e, nesse caso, uns 50 ms a mais de montagem num quadro dele -- sem corpo nenhum no mundo, os primeiros materiais
	/// da faisca nascem aqui.
	///
	/// POR ULTIMO, depois dos efeitos de ki: a faisca e particula, e a nota do <see cref="QuadrosDePalco"/> vale pra
	/// ela -- pode ser desenhada so no quadro seguinte ao em que e solta. A rajada vive 0,9 s; montada antes da
	/// poeira, atravessaria o quadro comprido dela (um terco de segundo com o cache de shader vazio, mais numa maquina
	/// lenta) e podia morrer sem nunca ter sido desenhada. A faisca antes dos feixes pelo mesmo motivo: o quadro do
	/// ato seguinte e o que garante o desenho dela.
	///
	/// E UMA LISTA A PARTE pra a rodada de injecao dela (<see cref="SemEnsaioDosRaiosDeTeste"/>) poder tira-la sozinha.
	/// ================================================================================================================
	/// </summary>
	private static readonly (string Nome, Action<Node2D, Vector2> Monta)[] AtosDosRaios =
	[
		("a faisca da forma", RaiosDaForma.Ensaiar),
		("os feixes de chao", Transformacao.EnsaiarOsFeixes),
	];

	/// <summary>
	/// ============================ E TRES DESENHOS QUE NAO SAO EFEITO DE NINGUEM ============================
	/// Depois dos raios da forma, a propria `--diagestouro` continuou listando pipeline 2D montada em jogo
	/// (`ListarAsPipelinesNascidasEmJogo`), e nem toda era de efeito:
	///
	///   * o RELAMPAGO -- o risco do `ClimaNaTela`, um sprite com o `Raio.gdshader` que nasce invisivel com o mundo e
	///     so e desenhado quando um raio cai dentro da tela (`ClimaNaTela.Ensaiar`);
	///   * a CHUVA -- o que cai do ceu e particula com o `Queda.gdshader`, num emissor que nasce desligado com o mundo;
	///     o motor nao desenha particula inativa, e a pipeline dele so nascia no primeiro pingo
	///     (`ClimaNaTela.EnsaiarAChuva`). Esta nao estava no inventario -- as rodadas de dia cravam o ceu aberto --: saiu
	///     da leitura do motor, e ganhou rodada propria, a do ceu (`--diagestouro --temas --ceu`), que faz uma tempestade
	///     COMECAR no meio do jogo;
	///   * o CORPO -- o boneco, que tem pipeline propria (`Personagem.gdshader`) e, como todo item, a gemea "com luz":
	///     a primeira luz que alcancava um corpo montava a gemea ali (`CharacterVisual.Ensaiar`).
	///
	/// MEDIDO em 2026-10-09, o desenho do quadro em que cada um aparecia pela primeira vez, com o cache de shader do
	/// driver de video quente e com ele frio (o sal do `--driverfrio`, ver o `RoboDoPrimeiroEstouro.Raios.cs`): o
	/// relampago, 1,0 e 25 a 41 ms; a chuva, 1,1 a 1,9 e 28 a 38 ms; o corpo iluminado, 1,2 e 101 a 106 ms. DEPOIS:
	/// nenhuma pipeline nasce em nenhum dos tres quadros, e o desenho deles custa 0,3 a 0,8 ms, quente ou frio.
	///
	/// O PRECO FICA NO LOBBY. Medido com o login de gente (o ensaio inteiro antes de haver mundo: os primeiros
	/// materiais de cada um nascem ali), os tres quadros somam 150 ms com o driver quente -- 35 o do risco, 56 o da
	/// chuva, 60 o do boneco -- e 500 com ele frio pros tres shaders (148, 142 e 213). E ali que quem acabou de instalar
	/// passa a pagar a pipeline de cada um, atras da tela de login, e nao no meio do jogo. O ensaio inteiro, que tinha
	/// 11 atos e levava 0,76 s, tem 15 (um e o do borrao do rastro) e leva 0,9 a 1,0 s.
	///
	/// (O BALAO DE FALA TAMBEM ESTAVA NO INVENTARIO, com duas pipelines, e ficou como estava: e desenho por malha do
	/// shader PADRAO do motor, e medido com o cache do driver novo de verdade o quadro dele custa 0,7 ms. A conta esta
	/// no cabecalho do `RoboDoPrimeiroEstouro.Nascidas.cs`.)
	///
	/// ANTES DOS EFEITOS DE KI, e a ordem aqui tem motivo: nenhum dos tres traz luz propria, e os tres precisam do lado
	/// escuro do palco ESCURO pra montar a pipeline do item sem luz. Vindo antes da estrela do embate eles nao dependem
	/// de ela nascer sem luz (ver "O LADO ESCURO TEM QUE CONTINUAR ESCURO", nos <see cref="Atos"/>). E nenhum envelhece
	/// mal no quadro comprido da poeira: o risco e o boneco estao parados, e a chuva nao para de emitir.
	///
	/// TRES LISTAS, pra a rodada de injecao de cada um poder tirar so o dela: <see cref="SemEnsaioDoRelampagoDeTeste"/>,
	/// <see cref="SemEnsaioDaChuvaDeTeste"/>, <see cref="SemEnsaioDoCorpoDeTeste"/>.
	/// ========================================================================================================
	/// </summary>
	private static readonly (string Nome, Action<Node2D, Vector2> Monta)[] AtosDoRelampago =
	[
		("o risco do raio", ClimaNaTela.Ensaiar),
	];

	/// <inheritdoc cref="AtosDoRelampago"/>
	private static readonly (string Nome, Action<Node2D, Vector2> Monta)[] AtosDaChuva =
	[
		("a chuva", ClimaNaTela.EnsaiarAChuva),
	];

	/// <inheritdoc cref="AtosDoRelampago"/>
	private static readonly (string Nome, Action<Node2D, Vector2> Monta)[] AtosDoCorpo =
	[
		("o boneco", CharacterVisual.Ensaiar),
	];

	/// <summary>
	/// ============================ E A MIRAGEM DO ZANZOKEN, QUE ESTAVA DE FORA COMO O BORRAO ============================
	/// Quem tem a `Afterimage Technique` larga uma miragem a cada Zanzoken e a cada arranque (`Zanzoken.Deixar`, pelo
	/// `World.AoPiscar`): a foto da pilha do corpo, com um material do `Zanzoken.gdshader` em cada camada. O shader era
	/// carregado na hora do primeiro uso, e o aquecimento nao o conhecia -- nem o arquivo vinha do lobby. A primeira
	/// miragem do processo lia o `Zanzoken.gdshader` do disco, criava os primeiros materiais dele e os desenhava, tudo
	/// no quadro do primeiro Zanzoken, com a thread principal esperando o shader compilar. MEDIDO em 2026-10-09 pela
	/// `--diagestouro --pecas` (a conta partida esta no `Zanzoken.Ensaiar`): 32,8 a 34,8 ms de trabalho nesse quadro,
	/// contra 2,3 a 3,9 de uma miragem qualquer; com o driver de video frio, um quadro de 56 a 57 ms -- e de 101 a 108 se
	/// ha uma luz em cima da miragem. DEPOIS: 3,2 a 4,4, e nenhuma pipeline nasce nele.
	///
	/// O ATO E UMA MIRAGEM DE VERDADE, pela porta de producao (`Zanzoken.Ensaiar` chama a mesma `Deixar`), de um boneco
	/// escondido: e ela que cria os primeiros materiais (o shader compila ali) e, desenhada dos dois lados do palco,
	/// monta as duas pipelines dele -- a do sprite sem luz e a do iluminado. E por ser a chamada do jogo, paga junto o
	/// codigo dela rodando pela primeira vez.
	///
	/// O SHADER ENTRA NA PRIMEIRA FILA por uma linha propria do <see cref="_Ready"/>, e nao pela <see cref="Lista"/>: e o
	/// que deixa a rodada de injecao tira-lo junto com o ato (<see cref="SemEnsaioDaMiragemDeTeste"/>).
	///
	/// DEPOIS DO BONECO, E ANTES DOS EFEITOS DE KI. Depois do boneco porque o ato faz um, escondido, pra fotografar: vindo
	/// antes, seria ele o primeiro a criar um material do `Personagem.gdshader`, e a espera por esse shader -- que e do
	/// ato do corpo -- cairia no quadro deste. Antes dos efeitos de ki pela razao do relampago, da chuva e do boneco: a
	/// miragem nao traz luz propria e precisa do lado escuro do palco ESCURO pra montar a pipeline do sprite sem luz, e
	/// vindo antes da estrela do embate nao depende de ela nascer sem luz. E nada nela envelhece mal no quadro comprido
	/// da poeira: a pipeline nasce no quadro em que ela e desenhada pela primeira vez, que e o do proprio ato.
	///
	/// O PRECO: um arquivo de 4,7 KB a mais na fila, um ato, duas pipelines e um quadro do lobby com 39,5 ms em vez de 8
	/// -- a espera pelo shader, paga aqui. (Com o cache de shader do Godot cheio, 13,7 ms; com o driver de video frio,
	/// 118,6: as duas pipelines montadas do zero, e a do sprite iluminado e a cara.) O ensaio inteiro, com o login de
	/// gente, foi de 896 a 902 ms pra 945 -- duas corridas sem o ato e uma com ele, antes de os atos de baixo entrarem.
	/// ================================================================================================================
	/// </summary>
	private static readonly (string Nome, Action<Node2D, Vector2> Monta)[] AtosDaMiragem =
	[
		("a miragem do Zanzoken", Zanzoken.Ensaiar),
	];

	/// <summary>
	/// ============================ E OS SEIS QUE NENHUMA BANCADA PUNHA NA TELA ============================
	/// Depois do relampago, da chuva e do boneco sobravam seis shaders do projeto que ato nenhum desenhava e que rodada
	/// nenhuma da `--diagestouro` usava em jogo. Ganharam rodada propria (`--diagestouro --avulsos`, o arquivo
	/// `RoboDoPrimeiroEstouro.Avulsos.cs`), que faz o primeiro uso de cada um sozinho num quadro, e foram MEDIDOS com o
	/// driver de video frio antes de qualquer conserto. Os seis custavam:
	///
	///   * a GOTA do transe (`GotaNaTela`) e a NEVOA de altitude (`NevoaDeAltitude`) -- retangulos de tela cheia que o
	///     `World` so cria no primeiro uso: a primeira meditacao profunda, o primeiro voo. O shader de cada uma nem na
	///     fila estava: era lido do disco, compilado e desenhado no mesmo quadro, como o do borrao;
	///   * o PLANETA visto do espaco e o ESTOURO de planeta (`PlanetaDesenhado`) -- o disco de cada mundo, na primeira
	///     chegada ao espaco, e o quad da Final Explosion, da Aura da Destruicao e de um mundo que morre. Lidos na hora
	///     tambem;
	///   * a NEBULOSA do Ultra Instinto (`NebulosaDaForma`) e a tela do EMBATE (`ClashQte`) -- estes estavam na
	///     <see cref="Lista"/> pelo engano dos do ki: o material de cada um nasce com o mundo, invisivel, e a pipeline so
	///     era montada no primeiro DESENHO -- o instante em que a forma acende, o comeco do primeiro ZanzoClash.
	///
	/// E A GEMEA ILUMINADA CUSTAVA MAIS QUE O PRIMEIRO USO. O motor desenha o item que tem luz em cima com outra pipeline
	/// do mesmo shader, e a COM luz montada depois da SEM luz e a cara: a gota, a nevoa, o estouro e o planeta pagavam o
	/// primeiro uso de dia e, de noite, a primeira vez com uma luz na tela, um quadro ainda maior. (A gota e a nevoa sao
	/// retangulos de tela cheia numa camada 0: QUALQUER luz do mundo que esteja na tela as alcanca -- a aura de uma forma,
	/// um tiro de ki, uma fogueira.) So a gemea da nebulosa saia de graca: o shader dela e `unshaded`, e as duas pipelines
	/// sao o mesmo codigo. A tela do embate mora numa camada de interface, onde luz nenhuma do mundo chega.
	///
	/// MEDIDO em 2026-10-09 pela `--diagestouro --avulsos`, o quadro do primeiro uso de cada um com o driver de video frio
	/// (o sal do `--driverfrio`, e uma corrida em que o driver nunca tinha visto os shaders de verdade): a gota 52 a 59
	/// ms, o estouro 54 a 57, a nebulosa 74 a 81, a nevoa 50 a 55, a tela do embate 23 a 31, e o planeta 17 a 21 a mais no
	/// quadro da chegada ao espaco. A gemea iluminada, montada depois: 58 a 79 ms de tela na gota, na nevoa, no estouro
	/// e no planeta. Com o driver QUENTE os quatro lidos na hora ainda custavam 33 a 39 ms -- a espera pelo shader, em
	/// todo jogo aberto pela primeira vez. DEPOIS: nenhuma pipeline nasce em nenhum dos onze quadros da rodada, com o
	/// driver quente ou frio, e o do primeiro uso custa 8 a 10 ms (9 a 15 o do embate). A conta inteira, o que so a medida
	/// disse e a regua estao no cabecalho do `RoboDoPrimeiroEstouro`, no bloco "E OS SEIS QUE NENHUMA RODADA PUNHA NA
	/// TELA".
	///
	/// O PRECO FICA NO LOBBY. Medido com o login de gente (a tabela "O ENSAIO, ATO A ATO" da rodada `--pecas`), os seis
	/// quadros somam 196 ms com o driver quente -- 30 a 38 cada, quase tudo a espera do shader de cada um -- e 615 com ele
	/// frio pros seis shaders (49 a 138 cada: as duas pipelines de cada um montadas do zero). O ensaio inteiro, que tinha
	/// 16 atos e levava 0,95 s, tem 22 e leva 1,15 s (1,55 com o driver frio) -- uma corrida de cada, no mesmo binario.
	///
	/// OS QUATRO SHADERS LIDOS NA HORA ENTRAM NA PRIMEIRA FILA por linhas proprias do <see cref="_Ready"/>, e nao pela
	/// <see cref="Lista"/>: e o que deixa a rodada de injecao de cada um tira-lo junto com o ato.
	///
	/// DEPOIS DA MIRAGEM E ANTES DOS EFEITOS DE KI, pela razao do relampago, da chuva e do boneco: nenhum dos seis traz
	/// luz propria, e todos precisam do lado escuro do palco ESCURO pra montar a pipeline do item sem luz. E nenhum
	/// envelhece mal no quadro comprido da poeira: sao todos parados.
	///
	/// A TELA DO EMBATE SO DO LADO ESCURO DO PALCO: a gemea iluminada dela nunca e usada em jogo (ver acima), e monta-la
	/// seria pagar no lobby, com o driver frio, uma pipeline que ninguem desenha.
	///
	/// CINCO LISTAS, pra a rodada de injecao de cada um poder tirar so a dela: <see cref="SemEnsaioDaGotaDeTeste"/>,
	/// <see cref="SemEnsaioDoPlanetaDeTeste"/> (o planeta e o estouro dele), <see cref="SemEnsaioDaNebulosaDeTeste"/>,
	/// <see cref="SemEnsaioDaNevoaDeTeste"/>, <see cref="SemEnsaioDoEmbateDeTeste"/>.
	/// ======================================================================================================
	/// </summary>
	private static readonly (string Nome, Action<Node2D, Vector2> Monta)[] AtosDaGota =
	[
		("a gota do transe", GotaNaTela.Ensaiar),
	];

	/// <inheritdoc cref="AtosDaGota"/>
	private static readonly (string Nome, Action<Node2D, Vector2> Monta)[] AtosDoPlaneta =
	[
		("o planeta visto do espaco", PlanetaDesenhado.Ensaiar),
		("o estouro de planeta", PlanetaDesenhado.EnsaiarOEstouro),
	];

	/// <inheritdoc cref="AtosDaGota"/>
	private static readonly (string Nome, Action<Node2D, Vector2> Monta)[] AtosDaNebulosa =
	[
		("a nebulosa", NebulosaDaForma.Ensaiar),
	];

	/// <inheritdoc cref="AtosDaGota"/>
	private static readonly (string Nome, Action<Node2D, Vector2> Monta)[] AtosDaNevoa =
	[
		("a nevoa de altitude", NevoaDeAltitude.Ensaiar),
	];

	/// <inheritdoc cref="AtosDaGota"/>
	private static readonly (string Nome, Action<Node2D, Vector2> Monta)[] AtosDoEmbate =
	[
		("a tela do embate", (pai, onde) => { if (onde == CentroEscuro) ClashQte.Ensaiar(pai, onde); }),
	];

	/// <summary>
	/// AS FOLHAS DE CHAMA: a aura de cada LINHA de forma, que a cinematica, a carga de ki e o node `Aura` desenham
	/// pela mesma `SpriteDeAura`. Entram na primeira fila, com a <see cref="Lista"/>.
	///
	/// LIDA DO DISCO NA HORA, cada uma custava 2 a 4 ms a thread principal no quadro em que a primeira chama
	/// daquela linha acendia -- a primeira cena, ou a primeira carga de ki. Medido em 2026-10-08, no script do quadro
	/// em que a cena nasce: a `AuraSSjBig` sozinha, 2,9 a 3,8 ms; a cena do `blue` 6,0 a 6,3 ms e a do `ssg` 11,4 (uma
	/// corrida), contra 3,6 a 5,1 da do `ssj2`, que divide a folha com o `ssj1`. Com as folhas na fila, as tres nascem
	/// em 3,2 a 3,6 ms.
	///
	/// VAO TODAS, e a conta e curta: a chama e uma folha por LINHA e nao por forma (`Catalogo.Folha`), entao sao
	/// sete arquivos e 1,7 MB de textura ao todo. Escolher "so as da raca do personagem" pouparia memoria que nao
	/// pesa e exigiria saber quem entrou, que e justamente o que esta classe nao sabe.
	///
	/// A LISTA SAI DO SIMBOLO, como a das aparencias sai do catalogo: folha nova no `FolhaDeAura` entra aqui sozinha.
	/// (As pedras da cena nao estao aqui porque ja tem dono: o `World` monta as `PedrasDaAgonia` na entrada, e
	/// elas carregam a mesma folha.)
	/// </summary>
	private static IEnumerable<string> FolhasDeChama()
	{
		foreach (Jandirus.Core.Forms.FolhaDeAura folha in Enum.GetValues<Jandirus.Core.Forms.FolhaDeAura>())
			if (SpriteDeAura.CaminhoDa(folha) is { } caminho) yield return caminho;
	}

	/// <summary>
	/// O QUE A CINEMATICA USA DEPOIS DE NASCER E NAO DEPENDE DE QUEM SE TRANSFORMA: as duas crateras e a fumaca que o
	/// beat que ASSUME planta, o som de cada beat, os estalos da faisca e os trovoes da tempestade. Entram na primeira
	/// fila, com a <see cref="Lista"/>.
	///
	/// LIDO NA HORA, cada arquivo destes parava a thread principal no quadro do beat que o usava. MEDIDO em 2026-10-08
	/// pela `--diagestouro --temas --cenas`, que parte cada quadro da cena em pedacos e da nome ao arquivo lido: a
	/// fumaca (um PNG de 1280x720) 13,6 a 15,4 ms, a folha de uma cratera 5,9, cada som 2,5 a 5,6. E NINGUEM OS
	/// SEGURAVA: com o coletor do .NET passando entre duas cenas, a segunda lia tudo de novo -- a fumaca em seis cenas
	/// de seis, e sozinha ela era mais da metade do quadro do beat que assume (18 a 38 ms). Aqui eles sao lidos uma
	/// vez, numa thread, e ficam.
	///
	/// NAO SAO SO DA CENA, e e por isso que valem a memoria (uns 4 MB, quase tudo a fumaca): toda cratera de combate
	/// planta a mesma fumaca, toda rajada de faisca de um Super Saiyajin 2 toca um dos quatro estalos, todo raio de
	/// chuva toca um dos dois trovoes.
	///
	/// A LISTA SAI DE QUEM USA -- o `Decalques`, o `RaiosDaForma`, os roteiros do Core pelo `Transformacao.SonsDasCenas`
	/// --, menos os dois trovoes, que estao escritos dentro do `ClimaNaTela`.
	///
	/// (O que DEPENDE de quem se transforma -- o penteado, o corpo proprio e as coladas da forma -- nao cabe nesta
	/// classe: quem o traz e a propria cena, quando comeca. Ver `Transformacao.AdiantarAsFolhasDaForma`.)
	/// </summary>
	private static IEnumerable<string> ArquivosDaCena()
	{
		yield return Decalques.CaminhoDaArte(Jandirus.Net.Protocol.Decal.CrateraGrande);
		yield return Decalques.CaminhoDaArte(Jandirus.Net.Protocol.Decal.Cratera);
		yield return Decalques.CaminhoDaArte(Jandirus.Net.Protocol.Decal.Fumaca);
		foreach (string som in Transformacao.SonsDasCenas()) yield return som;
		foreach (string estalo in RaiosDaForma.Estalos) yield return estalo;
		yield return "res://Assets/Sounds/Effects/thunderclap.ogg";    // `ClimaNaTela`: o trovao de cada raio
		yield return "res://Assets/Sounds/Effects/thunderclap2.ogg";
	}

	/// <summary>
	/// OS SONS DE GESTO: todo som de CAMINHO FIXO do `Trilha` que as listas de cima ainda nao trazem -- o rasgo de todo
	/// comeco de corrida e de toda investida, o Zanzoken, a decolagem e o pouso, o Kiai e a lamina de ar dele, o
	/// Kaio-ken acendendo, o membro arrancado. Entram na primeira fila, com a <see cref="Lista"/>.
	///
	/// QUEM OS SEGURA DEPOIS E A PORTA (`SonsPresos`), como a todo som de efeito; o que a fila faz e tirar da thread
	/// principal a PRIMEIRA leitura de cada um, que sem ela cai no primeiro gesto do processo que o toca.
	///
	/// MEDIDO em 2026-10-09 pela `--diagestouro --sons`: lido na hora, cada um custava 1,2 a 1,7 ms a thread principal
	/// (os oito que os gestos tocam, 9,8 a 10,7 ms ao todo); com eles na fila, 0,1 ms pelos oito. E PESAM POUCO: nove
	/// arquivos -- os oito, e o `Trilha.NovaHabilidade`, que ainda nao tem quem o toque --, 146 KB na memoria, e o tempo
	/// da fila nao muda (no mesmo binario, 103 arquivos em 911 ms com eles e 94 em 911 sem: quem a fecha e o tileset).
	///
	/// A LISTA SAI DO SIMBOLO (`Trilha.EfeitosFixos`: cada constante de caminho daquela classe), menos os sons de beat,
	/// que tem lista e rodada de injecao proprias (<see cref="ArquivosDaCena"/>): som novo de caminho fixo ja entra
	/// aqui. Os da pasta dos golpes repetem os de `Trilha.AmostrasDeGolpe`, e o <see cref="Pedir"/> nao pede duas vezes.
	/// </summary>
	private static IEnumerable<string> SonsDeGesto()
	{
		var deBeat = new HashSet<string>(Transformacao.SonsDasCenas(), StringComparer.Ordinal);
		foreach (string caminho in Trilha.EfeitosFixos())
			if (!deBeat.Contains(caminho)) yield return caminho;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --scripts`, rodada `--semsegurarosscripts`, ligado pelo `Boot`): o
	/// aquecimento NAO segura o script das classes de node que nascem e morrem no meio do jogo -- o jogo de antes: o motor
	/// solta o script de uma classe C# quando a ultima instancia dela morre, e o carrega de novo, pela thread principal,
	/// no nascimento seguinte. Sempre falso em jogo.
	///
	/// QUEM O LE: o <see cref="SegurarOsScripts"/>.
	/// </summary>
	public static bool SemSegurarOsScriptsDeTeste;

	/// <summary>
	/// ============================ OS SCRIPTS DAS CLASSES QUE NASCEM E MORREM NO MEIO DO JOGO ============================
	/// Pro Godot, uma classe C# de node e um RECURSO de script (`res://Client/X.cs`), e quem segura esse recurso sao as
	/// instancias da classe -- e mais ninguem. Morta a ultima, o motor destroi o script; o `new` seguinte, sem achar
	/// script pra classe, o carrega de novo ali mesmo, dentro do construtor, pela thread principal: a maquina do
	/// `ResourceLoader`, o fonte do `.cs` lido inteiro (so nos binarios de depuracao: o do editor, e o template de
	/// depuracao -- o do `DBJ.exe` que o dono exportou em 2026-08-16) e a ficha da classe refeita metodo a metodo. NAO
	/// DEPENDE DO COLETOR DO .NET, e e a diferenca pros sons: acontece toda vez que a classe vai a zero instancias e
	/// volta. Um tiro, a carga dele, a estrela de um embate, a poeira -- cada um carregava o script de novo quando
	/// aparecia sozinho.
	/// (O mecanismo, lido no fonte do motor, e a medida inteira estao no `RoboDoPrimeiroEstouro.Scripts.cs`.)
	///
	/// MEDIDO em 2026-10-09 pela `--diagestouro --scripts`, sem `--verbose`: cada carga custava a thread principal 0,5 a
	/// 0,9 ms no construtor (a cinematica, 1,2 a 1,3) e 0,7 a 1,1 pela porta de producao (1,4 a 1,5); no jogo exportado,
	/// 0,25 a 0,5 no construtor com o template de depuracao (a cinematica, 0,8) e 0,13 a 0,35 com o de lancamento (0,6).
	/// Um duelo de dois NPCs de 90 s fazia 16 a 26 cargas -- ate tres no mesmo quadro, o do encontro de dois raios.
	/// DEPOIS: nenhuma. O PRECO e do lobby: 20 a 24 ms no quadro em que ele nasce, uma vez por processo (13 a 17 no jogo
	/// exportado) -- as dez cargas e o codigo delas rodando pela primeira vez, que sem isto caia no primeiro uso de cada
	/// classe.
	///
	/// O REMEDIO E O DESTA CLASSE INTEIRA -- segurar a referencia: um `Script` de cada classe da lista, pedido no lobby e
	/// guardado numa lista estatica. Dali em diante a classe nunca mais fica sem dono, e o `new` dela so faz o node.
	///
	/// NESTA THREAD, E NAO NA FILA DE CARGA: quem pede o script de uma classe e o CONSTRUTOR dela, por dentro do motor, e
	/// um `new` que caisse enquanto o script esta numa thread de carga pediria ao `ResourceLoader`, pela thread
	/// principal, um caminho que esta no ar -- a corrida do cabecalho desta classe, e sem um `LoadThreadedGet` no caminho
	/// pra resolve-la. (Carregar script numa thread FUNCIONA -- conferido num projeto minimo; o que nao da pra garantir
	/// e que ninguem faca `new` no meio. Essa corrida, com script, ninguem provocou: e pra nao ter de provocar.)
	///
	/// QUEM ENTRA: a classe que o jogo nasce e mata no meio de uma luta e que pode ficar sem NENHUMA instancia viva. Nao
	/// entra a que tem sempre uma de pe enquanto ha mundo (a aura, a carga de ki, o rastro, o balao: o corpo do proprio
	/// jogador as carrega), nem a tela que so nasce num clique de menu, nem a coisa do mapa que entra e sai de vista
	/// com a zona. O QUE FICA DE FORA NAO SE ESCONDE: o gravador da `--diagestouro` (`--passivo` com `--scripts`) vigia
	/// TODA classe de script de producao e lista cada carga, com o quadro em que caiu.
	///
	/// A LISTA SAI DO SIMBOLO: o caminho do script vem do `ScriptPath` que o gerador do Godot escreve em cada classe (o
	/// mesmo que o motor le pra achar o script dela), e nao de um nome de arquivo escrito aqui.
	/// ====================================================================================================================
	/// </summary>
	private static readonly Type[] NodesDeVidaCurta =
	[
		typeof(ProjetilDesenhado),     // todo tiro de ki, bola ou raio
		typeof(CargaDeRaioVisual),     // o brilho na mao de quem carrega um raio
		typeof(ChoqueDeKi),            // a estrela do embate
		typeof(EstouroDeKi),           // o estouro de todo tiro que acerta
		typeof(PoeiraDeEstrago),       // a poeira de todo estrago no chao
		typeof(LuzDeKi),               // a luz de cada efeito de ki, num mundo escuro
		typeof(Transformacao),         // a cinematica de transformacao
		typeof(Zanzoken),              // o vulto que o Zanzoken deixa pra tras
		typeof(EsquivaZanzoken),       // o corpo trocado da esquiva por Zanzoken
		typeof(MarcaDeAlvo),           // o anel que marca o alvo
	];

	/// <summary>Os scripts presos. Estatica pelo mesmo motivo do <see cref="_presos"/>: o custo e do PROCESSO, e relogar nao deve repaga-lo.</summary>
	private static readonly List<Script> _scriptsPresos = [];

	/// <summary>CARREGA E SEGURA o script de cada classe do <see cref="NodesDeVidaCurta"/>. Uma vez por processo, chamado no `_Ready`.</summary>
	private static void SegurarOsScripts()
	{
		// (o jogo de antes, pra bancada: ver `SemSegurarOsScriptsDeTeste`)
		if (SemSegurarOsScriptsDeTeste) return;

		ulong t0 = Time.GetTicksUsec();
		var deFora = new List<string>();
		foreach (Type tipo in NodesDeVidaCurta)
		{
			// So tem caminho de script a classe que mora num arquivo com o nome dela. A que nao tem nem passa pelo
			// `ResourceLoader` -- e nao da pra segura-la por aqui: sai dita, em vez de calada.
			if (Attribute.GetCustomAttributes(tipo, typeof(ScriptPathAttribute), false) is not [ScriptPathAttribute atributo, ..])
			{
				deFora.Add(tipo.Name + " (sem caminho de script)");
				continue;
			}
			if (ResourceLoader.Load<Script>(atributo.Path) is { } script) _scriptsPresos.Add(script);
			else deFora.Add(tipo.Name + " (voltou nulo)");
		}
		GD.Print($"[aquece] {_scriptsPresos.Count} script(s) de node presos em {(Time.GetTicksUsec() - t0) / 1000.0:0.0} ms"
				 + (deFora.Count > 0 ? $" -- {deFora.Count} de fora: {string.Join(", ", deFora)}" : ""));
	}

	/// <summary>A cor dos efeitos do ensaio. Nao importa qual: cor e VALOR, e nao muda shader nem pipeline.</summary>
	private static readonly Color CorDoEnsaio = new(0.3f, 0.6f, 1f);

	/// <summary>
	/// O PALCO: 512 x 256, com um centro escuro a esquerda e um iluminado a direita, a 256 px um do outro.
	///
	/// ============================ O LADO ESCURO SE MEDE EM RETANGULO, E A ESTRELA ENCOSTA NA LUZ ============================
	/// O motor decide se um item "tem luz em cima" pelo RETANGULO dele contra o RETANGULO da luz (Godot 4.7,
	/// `renderer_canvas_render_rd.cpp`, `_record_item_commands`: `global_rect_cache.intersects(light->rect_cache)`), e
	/// nao pelo que a luz de fato clareia. A luz do lado iluminado ocupa de x = 326,4 a 441,6: um efeito do lado escuro
	/// continua "sem luz" enquanto nao passa de 198 px do proprio centro. A ESTRELA DO EMBATE PASSA, e so ela: o quad dela
	/// tem 199 px de meio lado (as pontas compridas: `ChoqueDeKi.MeioQuadro`) e chega a x = 327,3 -- nove decimos de
	/// pixel dentro do retangulo da luz. As duas estrelas do ensaio montam a pipeline COM luz, e a do item sem luz -- a
	/// que o jogo usa de dia -- nasce no primeiro embate de verdade. Era a segunda das "duas pipelines que a primeira
	/// estrela traz com o ensaio e sem ele" (a outra e a das faiscas: material gerado, que volta).
	///
	/// FICOU ASSIM PORQUE NAO CUSTA. MEDIDO em 2026-10-09 pela `--diagestouro --pecas --estrelapartida` (a conta inteira
	/// esta no cabecalho do `RoboDoPrimeiroEstouro.Nascidas.cs`): a gemea sem luz do quad, montada em jogo depois da
	/// iluminada, custa 0,5 a 0,6 ms de desenho com o driver de video frio pro shader dela. Com o palco alargado pra 1024
	/// px ela e montada no ensaio (33 pipelines em vez de 32) e o quadro do quad sai sem nenhuma -- a causa e essa --,
	/// e ninguem ve a diferenca. QUEM PUSER AQUI UM EFEITO MAIOR QUE A ESTRELA, OU UM QUE CUSTE, ALARGA O PALCO.
	/// (A gemea cara e a outra, a COM luz montada depois da sem luz -- a do boneco, mais de 100 ms --, e essa o lado
	/// iluminado garante.)
	/// ====================================================================================================================
	/// </summary>
	private static readonly Vector2I TamanhoDoPalco = new(512, 256);
	private static readonly Vector2 CentroEscuro = new(128, 128), CentroIluminado = new(384, 128);

	/// <summary>
	/// Quantos quadros DESENHADOS o palco fica de pe depois do ultimo ato: a particula so e emitida no quadro
	/// seguinte ao do nascimento, e so e desenhada no outro. O prazo em segundos e pra janela que nao desenha
	/// (minimizada): o palco nao pode ficar pendurado pra sempre esperando um quadro que nao vem.
	/// </summary>
	private const int QuadrosDePalco = 6;
	private const double PrazoDoPalco = 3.0;

	private enum FaseDoEnsaio { Esperando, Atos, Palco, Feito }

	private FaseDoEnsaio _fase = FaseDoEnsaio.Esperando;
	private SubViewport? _palco;
	private Node2D? _cena;
	private int _ato;

	/// <summary>Os atos que ESTE ensaio faz, na ordem: os da cena e os de ki. Montado quando o ensaio comeca.</summary>
	private (string Nome, Action<Node2D, Vector2> Monta)[] _atos = Atos;
	private ulong _desenhadosNoUltimoAto, _comecoDoEnsaio, _pipelinesNoComeco;
	private double _noPalco, _msDosAtos, _msDoPiorAto;
	private string _piorAto = "";

	/// <summary>
	/// UM TIRO PARADO NO PALCO, pelo node de producao -- a mesma receita da previa da mesa de tecnicas: sem
	/// luz propria (a do palco e a que conta) e com o rastro inteiro mesmo sem andar.
	/// </summary>
	private static void PorOTiro(Node2D pai, Vector2 onde, Jandirus.Core.Combat.TipoDeProjetil tipo)
	{
		var tiro = new ProjetilDesenhado { Tipo = tipo, Cor = CorDoEnsaio, SemLuz = true, SempreVoando = true, Position = onde };
		tiro.Vestir(Jandirus.Core.Combat.ArteDeKi.Nenhuma, 1f);
		// a cauda 48 px atras da cabeca: e o que da RUMO ao raio, e sem rumo ele nao desenha nada
		tiro.Mirar(onde, onde - new Vector2(48, 0));
		pai.AddChild(tiro);
	}

	/// <summary>A estrela do embate, definida antes de entrar na arvore como o `World.TickDosChoquesDeKi` faz.</summary>
	private static void PorAEstrela(Node2D pai, Vector2 onde)
	{
		// sem pedras: o palco nao tem chao, e depois de o mundo montar o `ChaoSoltoEm` responderia por um
		// ponto do mapa que nao tem nada a ver com ele
		//
		// E SEM LUZ PROPRIA, como o tiro: a luz de ki so nasce com o MUNDO escuro, e a da estrela (121 px de raio,
		// viva ate o palco sair) acendia o lado escuro inteiro -- ver "O LADO ESCURO TEM QUE CONTINUAR ESCURO", nos `Atos`.
		var estrela = new ChoqueDeKi { NoChao = false, SemLuz = !EstrelaComLuzDeTeste };
		estrela.Definir(onde, Vector2.Right, 30f, 18f, CorDoEnsaio, new Color(1f, 0.3f, 0.8f));
		pai.AddChild(estrela);
	}

	/// <summary>
	/// ANDA O ENSAIO UM PASSO. Chamado todo quadro pelo `_Process`, depois da fila.
	/// </summary>
	private void AndarNoEnsaio(double delta)
	{
		switch (_fase)
		{
			case FaseDoEnsaio.Esperando:
				// SO DEPOIS DA PRIMEIRA FILA: os atos pedem os shaders do ki ao `ResourceLoader`, e pedir um
				// arquivo que ainda esta na thread de carga e o travamento do cabecalho da classe.
				if (!_aparenciasPedidas && !Terminou) return;

				// SEM JANELA NAO HA O QUE ENSAIAR: no headless nada e desenhado, e a travada nao existe.
				if (SemEnsaioDeTeste || DisplayServer.GetName() == "headless") { _fase = FaseDoEnsaio.Feito; return; }

				// A CENA PRIMEIRO, o chao que ela abre em seguida, os efeitos de ki depois, na ordem que eles ja
				// tinham, e os raios da forma por ultimo -- ver `AtosDaCena`, `AtosDoChao` e `AtosDosRaios`. Cada
				// rodada de injecao tira so a lista dela.
				var atos = new List<(string Nome, Action<Node2D, Vector2> Monta)>();
				if (!SemEnsaioDaCenaDeTeste) atos.AddRange(AtosDaCena);
				if (!SemAdiantarACenaDeTeste) atos.AddRange(AtosDoChao);
				// (o borrao do rastro logo depois do chao: ver `AtosDoBorrao`)
				if (!SemEnsaioDoBorraoDeTeste) atos.AddRange(AtosDoBorrao);
				// (o relampago, a chuva e o boneco ANTES dos efeitos de ki: ver `AtosDoRelampago`)
				if (!SemEnsaioDoRelampagoDeTeste) atos.AddRange(AtosDoRelampago);
				if (!SemEnsaioDaChuvaDeTeste) atos.AddRange(AtosDaChuva);
				if (!SemEnsaioDoCorpoDeTeste) atos.AddRange(AtosDoCorpo);
				// (a miragem do Zanzoken DEPOIS do boneco, e antes dos efeitos de ki: ver `AtosDaMiragem`)
				if (!SemEnsaioDaMiragemDeTeste) atos.AddRange(AtosDaMiragem);
				// (os seis que nenhuma bancada punha na tela, depois da miragem e ANTES dos efeitos de ki: ver `AtosDaGota`)
				if (!SemEnsaioDaGotaDeTeste) atos.AddRange(AtosDaGota);
				if (!SemEnsaioDoPlanetaDeTeste) atos.AddRange(AtosDoPlaneta);
				if (!SemEnsaioDaNebulosaDeTeste) atos.AddRange(AtosDaNebulosa);
				if (!SemEnsaioDaNevoaDeTeste) atos.AddRange(AtosDaNevoa);
				if (!SemEnsaioDoEmbateDeTeste) atos.AddRange(AtosDoEmbate);
				atos.AddRange(Atos);
				if (!SemEnsaioDosRaiosDeTeste) atos.AddRange(AtosDosRaios);
				_atos = [.. atos];

				MontarOPalco();
				_comecoDoEnsaio = Time.GetTicksUsec();
				_pipelinesNoComeco = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsCanvas);
				_fase = FaseDoEnsaio.Atos;
				return;

			case FaseDoEnsaio.Atos:
				FazerUmAto();
				if (_ato < _atos.Length) return;
				_desenhadosNoUltimoAto = (ulong)Engine.GetFramesDrawn();
				_noPalco = 0;
				_fase = FaseDoEnsaio.Palco;
				return;

			case FaseDoEnsaio.Palco:
				_noPalco += delta;
				if ((ulong)Engine.GetFramesDrawn() - _desenhadosNoUltimoAto < QuadrosDePalco && _noPalco < PrazoDoPalco) return;

				_palco?.QueueFree();
				_palco = null;
				_cena = null;
				_fase = FaseDoEnsaio.Feito;
				Ensaiou = true;
				ulong pipelines = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsCanvas) - _pipelinesNoComeco;
				GD.Print($"[aquece] ensaio {(_atos.Length > Atos.Length ? "da cinematica e dos efeitos de ki" : "dos efeitos de ki")}:"
						 + $" {_atos.Length} atos em {(Time.GetTicksUsec() - _comecoDoEnsaio) / 1000.0:0} ms"
						 + $" ({_msDosAtos:0} ms montando; o mais caro, {_piorAto}, {_msDoPiorAto:0} ms; {pipelines} pipelines 2D montadas no caminho)");
				return;
		}
	}

	private void MontarOPalco()
	{
		Viewport janela = GetViewport();
		_palco = new SubViewport
		{
			Name = "PalcoDoEnsaio",
			Size = TamanhoDoPalco,
			// O MESMO FORMATO DA JANELA -- e o que faz a pipeline montada aqui ser a do mundo. Ver o cabecalho.
			UseHdr2D = janela.UseHdr2D,
			Msaa2D = janela.Msaa2D,
			Disable3D = true,
			GuiDisableInput = true,
			// ninguem mostra a textura dele, entao "quando visivel" nunca desenharia
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		_cena = new Node2D { Name = "Cena" };
		_palco.AddChild(_cena);

		// A LUZ DO LADO DIREITO. A textura e a mesma radial das luzes de ki (`Fogo.Radial` a guarda por raio:
		// a primeira `LuzDeKi` da noite ja a encontra feita). 96 px de raio x 0,6 = um disco de 58 px em
		// volta do centro iluminado -- e, pro motor, um RETANGULO de 115 px de lado: e nele que o efeito do
		// lado escuro nao pode encostar (ver `TamanhoDoPalco`: a estrela do embate encosta).
		_palco.AddChild(new PointLight2D
		{
			Name = "Luz",
			Position = CentroIluminado,
			Texture = Fogo.Radial(LuzDeKi.RaioDaTextura),
			TextureScale = 0.6f,
			Energy = 0.5f,
		});
		AddChild(_palco);
	}

	private void FazerUmAto()
	{
		if (_cena == null || _ato >= _atos.Length) return;

		(string nome, Action<Node2D, Vector2> monta) = _atos[_ato++];
		ulong t0 = Time.GetTicksUsec();
		monta(_cena, CentroEscuro);
		monta(_cena, CentroIluminado);
		double ms = (Time.GetTicksUsec() - t0) / 1000.0;

		_msDosAtos += ms;
		if (ms > _msDoPiorAto) { _msDoPiorAto = ms; _piorAto = nome; }
		EspiaoDeAto?.Invoke(nome, ms);
	}

	/// <summary>
	/// ============================ A SEGUNDA FILA: AS FOLHAS DE APARENCIA ============================
	/// COMO SE SABE QUE SAO ELAS: medido com `--verbose`, na janela entre o `JoinAccepted` e o
	/// `[perf] PRIMEIRO QUADRO`, das 267 linhas de recurso **31 sao folhas de CABELO** e 6 de roupa.
	/// Elas nao vem do personagem do jogador -- vem da rajada de aparencias dos NPC do primeiro
	/// snapshot, e caem todas dentro do unico quadro que o jogador fica esperando. A prova do custo
	/// esta no proprio processo: a MESMA entrada feita uma segunda vez, com as folhas ja em memoria,
	/// desenha em 278 ms contra 502 ms.
	///
	/// A LISTA SAI DO CATALOGO E NAO DE UM PALPITE: os caminhos vem do `visual.json`, o MESMO arquivo
	/// que o `World._Ready` e a criacao de personagem leem. Escrever nomes de cabelo aqui seria criar
	/// uma segunda lista pra sair de sincronia com a primeira no dia em que alguem puser um penteado.
	///
	/// FILA SEPARADA E PEDIDA DEPOIS: sao ~250 arquivos pequenos contra 21 grandes. Pedidos juntos,
	/// os pequenos entrariam na frente do tileset na fila do carregador e atrasariam justamente o
	/// item que sozinho vale ~630 ms.
	///
	/// O QUE CUSTA: as folhas ficam em memoria desde o lobby. Nao e memoria NOVA -- e a mesma que
	/// qualquer planeta povoado ia pedir de qualquer jeito, uns segundos depois e no pior momento.
	/// ================================================================================================
	/// </summary>
	private void PedirAsAparencias()
	{
		_aparenciasPedidas = true;
		const string dados = "res://Assets/Data/visual.json";
		if (!Godot.FileAccess.FileExists(dados)) return;

		Jandirus.Core.Appearance.VisualCatalog cat;
		try { cat = Jandirus.Core.Appearance.VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados)); }
		catch (Exception e) { _falharam.Add("visual.json (" + e.Message + ")"); return; }

		var caminhos = new List<string>();
		foreach ((string _, string? sprite) in cat.Cabelos) if (sprite is { Length: > 0 }) caminhos.Add(sprite);
		caminhos.AddRange(cat.Roupas);
		caminhos.AddRange(cat.Armaduras);
		if (cat.Olhos is { Length: > 0 }) caminhos.Add(cat.Olhos);
		// OS CORPOS, os dois generos. `Tons` fica de fora de proposito: aquilo sao nomes de cor, e
		// nao caminhos de arquivo -- pedi-los ao `ResourceLoader` so encheria a lista de falhas.
		foreach (Jandirus.Core.Appearance.BodyOptions b in cat.Corpos.Values)
		{
			caminhos.AddRange(b.Masculino);
			caminhos.AddRange(b.Feminino);
		}
		caminhos.Add(Jandirus.Core.Appearance.VisualCatalog.CorpoPadraoM);
		caminhos.Add(Jandirus.Core.Appearance.VisualCatalog.CorpoPadraoF);

		Pedir(caminhos);
	}

	/// <summary>Poe na fila o que existir, sem nunca deixar um caminho ruim derrubar o jogo.</summary>
	private void Pedir(IEnumerable<string> caminhos)
	{
		foreach (string caminho in caminhos)
		{
			// ARQUIVO QUE NAO EXISTE NAO E MOTIVO DE PARAR O JOGO. Isto aqui e uma OTIMIZACAO: se
			// alguem renomear um shader, o caminho de producao continua carregando pelo nome novo e
			// o unico prejuizo e voltar ao tempo de antes. Um erro fatal aqui seria trocar um
			// engasgo por uma tela preta.
			if (_faltando.Contains(caminho)) continue;
			if (!ResourceLoader.Exists(caminho)) { _falharam.Add(caminho + " (nao existe)"); continue; }
			if (ResourceLoader.LoadThreadedRequest(caminho) != Error.Ok) { _falharam.Add(caminho + " (recusado)"); continue; }
			_faltando.Add(caminho);
			Total++;
		}
	}

	public override void _Ready()
	{
		// UMA VEZ POR PROCESSO. O `Boot.VoltarAoLogin` derruba e remonta o lobby, e sem esta guarda
		// cada relog dispararia a fila de novo -- pedindo ao `ResourceLoader` cargas de coisas que ja
		// estao na memoria.
		if (_pedido) { Terminou = true; SetProcess(false); return; }
		_pedido = true;
		_vivo = this;
		_comecou = Time.GetTicksUsec();

		// OS SCRIPTS DAS CLASSES DE VIDA CURTA, antes de a fila existir: sao cargas DESTA thread (ver `NodesDeVidaCurta`),
		// e assim nao dividem o `ResourceLoader` com os cem arquivos dela. (E limpeza, nao tempo: medido, custam os mesmos
		// 20 a 24 ms antes ou depois de a fila ser pedida.)
		SegurarOsScripts();

		Pedir(Lista);
		// AS AMOSTRAS DE GOLPE, na mesma fila: o baque de todo soco e de todo tiro de ki sai desta pasta, e
		// ninguem segurava os arquivos -- ver `Trilha.AmostrasDeGolpe`. A lista vem da PASTA, como as da
		// trilha: por um som novo la nao pede uma linha aqui.
		Pedir(Trilha.AmostrasDeGolpe());
		// AS FOLHAS DE CHAMA, na mesma fila: sem elas a primeira aura de cada linha de forma lia a folha do disco no
		// quadro em que acendia -- ver `FolhasDeChama`. (Na rodada de injecao ficam de fora, com o ato da cena.)
		if (!SemEnsaioDaCenaDeTeste) Pedir(FolhasDeChama());
		// E O QUE A CENA USA DEPOIS DE NASCER, na mesma fila: as crateras, a fumaca e os sons -- ver `ArquivosDaCena`.
		// (Na rodada de injecao deles ficam de fora, como as folhas de chama na dela.)
		if (!SemAdiantarACenaDeTeste) Pedir(ArquivosDaCena());
		// E O SHADER DO BORRAO DO RASTRO, na mesma fila: sem ele o primeiro borrao do processo -- a primeira investida de
		// uma luta, o primeiro passo de uma corrida -- lia o arquivo do disco na hora. Ver `AtosDoBorrao`. (Na rodada de
		// injecao dele fica de fora, com o ato.)
		if (!SemEnsaioDoBorraoDeTeste) Pedir([BorraoDirecional.CaminhoDoShader]);
		// E O SHADER DA MIRAGEM DO ZANZOKEN, na mesma fila e pelo mesmo motivo: sem ele a primeira miragem do processo -- o
		// primeiro Zanzoken, o primeiro arranque de quem tem a Afterimage -- lia o arquivo do disco na hora. Ver
		// `AtosDaMiragem`. (Na rodada de injecao dela fica de fora, com o ato.)
		if (!SemEnsaioDaMiragemDeTeste) Pedir([Zanzoken.CaminhoDoShader]);
		// E OS QUATRO SHADERS QUE O JOGO SO LIA NA HORA DO PRIMEIRO USO, na mesma fila e pelo mesmo motivo: o da gota do
		// transe (a primeira meditacao profunda), o da nevoa de altitude (o primeiro voo) e os dois do planeta (o disco
		// visto do espaco, e o estouro). Ver `AtosDaGota`. (Na rodada de injecao de cada um ficam de fora, com o ato.)
		if (!SemEnsaioDaGotaDeTeste) Pedir([GotaNaTela.CaminhoDoShader]);
		if (!SemEnsaioDaNevoaDeTeste) Pedir([NevoaDeAltitude.CaminhoDoShader]);
		if (!SemEnsaioDoPlanetaDeTeste) Pedir([PlanetaDesenhado.CaminhoDaAgonia, PlanetaDesenhado.CaminhoDoEstouro]);
		// E OS SONS DE GESTO, na mesma fila: o rasgo da corrida, o Zanzoken, a decolagem e o pouso, o Kiai -- ver
		// `SonsDeGesto`. (Na rodada de injecao deles ficam de fora, como os de cima nas delas.)
		if (!SonsPresos.SemSegurarDeTeste) Pedir(SonsDeGesto());
		_fixos = Total;
		if (_faltando.Count == 0) { PedirAsAparencias(); if (_faltando.Count == 0) Encerrar(); }
	}

	/// <summary>
	/// FECHA A FILA AGORA, ESPERANDO O QUE FALTA -- e a porta de saida obrigatoria do aquecimento.
	///
	/// ============================ CHAME ISTO ANTES DE QUALQUER UM PEDIR OS ARQUIVOS ============================
	/// Enquanto houver `LoadThreadedRequest` no ar, um `ResourceLoader.Load` do mesmo caminho vindo do
	/// jogo TRAVA a thread principal (ver o cabecalho da classe -- foi medido, com 240 s de log mudo).
	/// Este metodo recolhe o pendente com `LoadThreadedGet`, que e a porta feita pra ESPERAR uma carga
	/// em andamento. Depois dele nao ha mais nada no ar, e o jogo pode pedir o que quiser.
	///
	/// ELE PODE BLOQUEAR, e isso e o certo: o pior caso e esperar o que faltava do aquecimento, que e
	/// trabalho que teria de ser feito de qualquer jeito e um pouco mais adiante. No caso normal -- o
	/// jogador digitou conta e senha, gastou alguns segundos -- a fila ja esta vazia e isto custa zero.
	///
	/// O PIOR CASO, MEDIDO: um cliente que loga em ~1 s espera 377 ms aqui, no LOBBY, e a segunda fila
	/// (as folhas de aparencia) nem chega a ser pedida. Ou seja quem entra correndo fica com o
	/// aquecimento pela METADE -- e mesmo assim a entrada cai de 1586 ms pra 922 ms. Quem entra no
	/// ritmo de gente pega as duas filas e 866 ms. Em nenhum dos dois casos se paga MAIS do que sem
	/// aquecimento nenhum: e o mesmo trabalho, so que antes do clique em vez de depois dele.
	/// ============================================================================================================
	/// </summary>
	public static void Concluir()
	{
		if (_vivo is not { } eu || eu._faltando.Count == 0) return;

		ulong t0 = Time.GetTicksUsec();
		int esperados = eu._faltando.Count;

		foreach (string caminho in eu._faltando)
		{
			// `LoadThreadedGet` de um item ainda em andamento ESPERA a thread de carga terminar --
			// e exatamente o que se quer aqui, e e o que o `Load` cru nao faz.
			if (ResourceLoader.LoadThreadedGet(caminho) is { } r) _presos.Add(r);
			else eu._falharam.Add(caminho + " (voltou nulo no fecho)");
		}
		eu._faltando.Clear();

		GD.Print($"[aquece] fecho antecipado: esperei {esperados} recurso(s) por"
				 + $" {(Time.GetTicksUsec() - t0) / 1000.0:0} ms -- o jogador foi mais rapido que a thread");
		eu.Encerrar();
	}

	public override void _Process(double delta)
	{
		if (!Terminou) RecolherAFila();
		AndarNoEnsaio(delta);

		// SO PARA QUANDO OS DOIS ACABARAM, a fila e o ensaio: quem fecha a fila no `Concluir` (o login mais
		// rapido que a thread) ainda tem o ensaio inteiro pela frente.
		if (Terminou && _fase == FaseDoEnsaio.Feito) SetProcess(false);
	}

	private void RecolherAFila()
	{
		// RECOLHE O QUE ESTIVER PRONTO, e so isso. Nada aqui bloqueia: `LoadThreadedGetStatus` e uma
		// consulta, e o `LoadThreadedGet` de um item que ja voltou e uma entrega, nao uma carga.
		for (int i = _faltando.Count - 1; i >= 0; i--)
		{
			string caminho = _faltando[i];
			ResourceLoader.ThreadLoadStatus estado = ResourceLoader.LoadThreadedGetStatus(caminho);
			if (estado == ResourceLoader.ThreadLoadStatus.InProgress) continue;

			_faltando.RemoveAt(i);
			if (estado != ResourceLoader.ThreadLoadStatus.Loaded) { _falharam.Add(caminho + $" ({estado})"); continue; }

			if (ResourceLoader.LoadThreadedGet(caminho) is { } r) _presos.Add(r);
			else _falharam.Add(caminho + " (voltou nulo)");
		}

		if (_faltando.Count > 0) return;

		// A PRIMEIRA FILA ACABOU: agora sim as folhas de aparencia -- ver `PedirAsAparencias`.
		if (!_aparenciasPedidas) { PedirAsAparencias(); if (_faltando.Count > 0) return; }
		Encerrar();
	}

	/// <summary>A FILA fechou. O `_Process` continua ate o ensaio acabar tambem -- ver <see cref="AndarNoEnsaio"/>.</summary>
	private void Encerrar()
	{
		Terminou = true;
		_vivo = null;   // nao ha mais nada no ar: o `Concluir` vira um retorno imediato
		Ms = (Time.GetTicksUsec() - _comecou) / 1000.0;
		GD.Print($"[aquece] {_presos.Count} de {Total} recurso(s) quentes em {Ms:0} ms"
				 + $" ({_fixos} do mundo e do combate + {Total - _fixos} folhas de aparencia)"
				 + (_falharam.Count > 0 ? $" -- {_falharam.Count} de fora: {string.Join(", ", _falharam)}" : ""));
	}
}
