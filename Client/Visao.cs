using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A SOMBRA DO QUE ESTA NA FRENTE. Nao ha alcance de visao: de dia voce enxerga a tela
/// inteira, e a unica coisa que esconde alguma coisa e o que uma parede (ou uma arvore) tapa.
///
/// POR QUE ISTO NAO E UMA LUZ. A primeira versao era um <see cref="PointLight2D"/> preso no
/// personagem, e o dono cortou: "essa luz de visao nao deveria ser uma luz em si". Ele estava
/// certo por um motivo mecanico -- uma luz SOMA brilho em tudo que alcanca, inclusive no
/// sprite de quem a carrega, e o personagem ficava mais claro que o mundo em volta.
///
/// POR QUE NAO E MAIS UM CIRCULO. A segunda versao tinha alcance (12 tiles) e um leque de 128
/// raios em angulos uniformes. Dois problemas, e o dono viu os dois: o circulo nao devia
/// existir de dia, e a borda saia SERRILHADA. O serrilhado tem causa exata: 128 raios dao um
/// passo de 2,81 graus, e um tile de 32 px visto a 384 px ocupa ~4,8 graus -- uma parede
/// isolada era amostrada por um ou dois raios e a sombra dela PULAVA um tile inteiro a cada
/// passo do jogador. Aumentar o numero de raios nao conserta isso; so muda a frequencia.
///
/// O QUE CONSERTA e mirar nas QUINAS. Sombra de geometria reta e delimitada por retas que
/// passam pelos CANTOS do obstaculo -- entao os raios sao lancados exatamente ali, um de cada
/// lado de cada quina. Entre duas quinas vizinhas nao ha nada pra amostrar, e a aresta que
/// liga os dois pontos e a propria face da parede. Borda reta por construcao.
///
/// O LIMITE E A TELA, e isso e o corte de desempenho: so entram no calculo as celulas dentro
/// do retangulo da camera. Uma parede do outro lado do planeta nao projeta nada que alguem
/// possa ver, entao nao custa nada.
///
/// ============================ A PAREDE E ILUMINADA POR FORA, NAO ATRAVESSADA (2026-09-07) ============================
/// Houve tres regras pra "onde o raio para", e as duas ultimas eram tentativas de fazer o muro
/// aparecer CLARO na tela (o raio parando na face de ENTRADA punha o proprio muro dentro da
/// sombra -- um bloco preto no lugar da parede que o jogador devia estar enxergando):
///
///   * parar na SAIDA da primeira celula cega: muro comprido visto de raso ficava em dente de
///     serra, cada tile projetando sombra no vizinho;
///   * ATRAVESSAR o bloco inteiro (parando so quando o tile mudava): consertou o dente e
///     produziu o que o dono fotografou em 2026-09-07 -- "sombra que sai do meio da parede".
///     A causa e exata: um raio raso que corre por dentro da fileira sai dela por uma face
///     LATERAL (o vao de um portao, a ponta do muro) e o raio vizinho sai pela face NORTE,
///     longe dali; a aresta do leque entre os dois corta os tiles do muro em diagonal. O
///     repinte de parede so cobria a celula cujo CENTRO caia na sombra, entao sobrava a cunha
///     num tile e o tile do lado saia inteiro claro -- "tem sombra que fica sobre tile e
///     outras nao". E o interior de uma casa ficava visivel de fora quando a cerca encostava
///     no muro, o que trouxe o plano de identidade de tile (o byte por celula no `.vis`) so
///     pra separar os dois. Tudo isso era sintoma de UMA escolha: mover o ponto de parada pra
///     dentro do obstaculo.
///
/// A regra agora separa as duas perguntas, que sao perguntas diferentes:
///
///   1. O QUE SE VE e geometria exata: o raio para na face de ENTRADA da primeira celula cega.
///      Dois raios que batem na mesma parede param em pontos colineares, a sombra comeca na
///      face que encara o jogador, e nao existe mais parada "no meio do muro" -- toda parada e
///      uma fronteira livre/cega (`ParadasForaDeFace` conta o contrario, e e zero).
///   2. QUE PAREDE ESTA CLARA e uma pergunta por CELULA: a parede cuja face da pra algum ponto
///      visivel (tres amostras por face, meio pixel pra fora, ver `FaceVisivel`). Essas celulas
///      viram FUROS na malha da sombra -- o shader descarta o fragmento que cai nelas -- em vez
///      de serem redesenhadas por cima. Sem repinte nao ha tile alto espremido em 32 px, nao ha
///      parede desenhada por cima de quem voa sobre ela, e a porta (que nao e tile) nao precisa
///      de caso proprio.
///
/// O que fica: a face do muro que encara o jogador clara, TUDO atras dela escuro (inclusive o
/// muro de tras -- "clara, a nao ser que a sombra de outra parede a cubra", regra do dono de
/// 2026-08-03), e o vao de um portao abrindo um cone de luz pro outro lado. O plano de identidade
/// morreu junto com a travessia; o `.vis` voltou a ser so o bitset.
/// =====================================================================================================================
///
/// ============================ O OLHO PODE SER EMPRESTADO (2026-09-07) ============================
/// Assistindo ao torneio, a camera vai pra arena e o corpo fica na area de espera. O olho do veu
/// continuava no CORPO, e o dono viu "uma sombra na tela" e os lutadores invisiveis: com o olho
/// fora do retangulo da camera, todo raio apontado pro lado oposto morria no `AteABorda` (limite
/// negativo) e o leque, que so fecha com 360 graus de raios, DOBRAVA -- o quadrilatero entre o
/// ultimo raio e o primeiro cobria a tela em diagonal, duas vezes num pedaco. Ver
/// <see cref="OlhoEmprestado"/> (o `EYE_PERSPECTIVE` do DM) e <see cref="Tela"/>, que agora
/// sempre CONTEM o olho, pra que o leque nunca mais dobre por conta de onde a camera esta.
/// ================================================================================================
///
/// ============================ O INTERIOR QUE NAO SE VE E BREU (2026-10-08) ============================
/// Pedido do dono: "a sombra deveria ser totalmente escura vendo de fora pra dentro de uma casa/base
/// sem janelas, assim ninguem consegue ver oq tem dentro da sua base".
///
/// A geometria ja estava certa -- um comodo fechado cai inteiro dentro da malha da sombra. O que
/// vazava era o resto, e eram tres coisas:
///
///   1. A SOMBRA TEM ALFA 0,85 (<see cref="Sombra"/>): 15% do que esta embaixo passa, e de dia isso e
///      piso, movel e a silhueta de quem esta la dentro.
///   2. PAIRAR APAGAVA O VEU INTEIRO. A altura abria a sombra pelo `Modulate` do no, e so de ligar o
///      voo o corpo ja passa do limiar (`Voo.AlturaDePairar` e maior que `Voo.AlturaQueAtravessa`):
///      bastava a tecla V do lado de fora pra ver dentro de qualquer casa.
///   3. O CORPO ENCOSTADO NA PAREDE APARECIA PELO FURO DELA: o furo mostra a celula da parede
///      inteira, e um sprite de 32 px com os pes na celula de baixo invade ate 19 px dela.
///
/// A REGRA: a celula SOB TETO (<see cref="SobTeto"/> -- a area `Inside` do original, ver
/// `CelulaInterna`) que NAO e do comodo do olho (<see cref="Escondida"/>) sai com alfa 1, e nao abre
/// com a altura. Quem esta do lado de fora ve a fachada clara (o furo continua vencendo) e um bloco
/// preto atras dela; quem esta DENTRO ve o proprio comodo com a sombra de sempre -- a sombra do
/// balcao nao vira breu --, e o comodo do lado, atras de porta fechada, no escuro. E o corpo e a
/// maquina que estao numa celula dessas, fora do leque, nao sao desenhados (<see cref="Esconde"/>).
///
/// "SEM JANELAS" NAO PRECISA DE CASO PROPRIO: o que nao cega nao para o raio. O vidro
/// (`/turf/decor/Glass`, que bloqueia e nao cega) e a porta aberta deixam o leque entrar, e o
/// pedaco do comodo que ele alcanca nao e coberto pela malha -- so o que o olho NAO ve vira breu.
///
/// NO DM QUEM FAZ ISTO E O TELHADO: `opacity = 1` no `turf/Roof` (`Turfs.dm:1006-1009`), com a parede
/// comum transparente (`CastleWall`, `Turfs.dm:452-456`); e o original escreveu a intencao com as
/// mesmas palavras ao carimbar a cidade de Vegeta -- "blocks vision so you can't see the interior
/// from outside" (`VegetaCity.dm:47`). La o que fica atras de um turf opaco nao e desenhado. O port
/// punha uma penumbra no lugar, e essa divergencia era nossa.
///
/// DIVERGENCIAS DECLARADAS:
///   * e por CELULA SOB TETO, e nao por turf opaco: o `Roof` do DM virou parede comum no port (nao
///     ha camada de telhado), e "sob teto" e o plano que ja existe pro clima;
///   * o DM nao tem altura, entao nao ha o que copiar pra "quem voa ve dentro?": aqui NAO ve;
///   * a cortina da CEGUEIRA (`CegoAte`) tambem sumia com a altura, pelo mesmo `Modulate` -- o Solar
///     Flare nao cegava quem estava pairando. Saiu junto, que era o mesmo defeito.
///
/// O QUE ISTO NAO FAZ: nao filtra o que o servidor manda (o retrato da zona e um buffer so, e um
/// cliente adulterado continua recebendo a posicao de todo mundo); nao esconde fumaca, luz de ki
/// nem som; e nao muda a sombra do campo aberto -- atras da arvore continua a penumbra.
/// ====================================================================================================
/// </summary>
public partial class Visao : Node2D
{
	/// <summary>Quem enxerga.</summary>
	public Node2D? Alvo;

	private ZoneCollision? _mapa;

	/// <summary>
	/// O QUE CEGA: parede, porta e arvore -- tudo que e denso (ver MapConverter). E um bitset
	/// proprio (`.vis`), separado do de colisao, porque os dois divergem: a porta cega e nao
	/// bloqueia, a borda do mundo bloqueia e nao interessa cegar.
	///
	/// TROCAR DE MAPA E TROCAR DE MUNDO: o leque anterior e da zona anterior. Sem o `Invalidar`
	/// daqui, quem chega numa zona nova SEM ANDAR (teleporte; o robo da bancada) ficava com a
	/// sombra do lugar de onde saiu ate o primeiro passo -- o recalculo so olha o olho e a tela, e
	/// nenhum dos dois muda num teleporte de mesma coordenada. Foi o primeiro numero errado que a
	/// bancada com janela deu: zero paredes claras num portao inteiro.
	/// </summary>
	public ZoneCollision? Mapa
	{
		get => _mapa;
		set { _mapa = value; InvalidarOComodo(); }
	}

	/// <summary>
	/// O mapa de COLISAO da mesma zona. Usado so pelo guarda de sanidade do <see cref="_Draw"/> --
	/// ver o comentario la. Nulo = sem guarda, que e melhor que um guarda que dispara errado.
	/// </summary>
	public ZoneCollision? Colisao;

	/// <summary>
	/// QUEM RESPONDE "esta celula esta SOB TETO agora?" -- no jogo, o `World.CelulaSobTeto` (o plano
	/// `.dentro` da zona, menos o que caiu). Nulo = zona sem interior marcado, e a regra do breu
	/// simplesmente nao tem onde agir.
	///
	/// E UM DELEGADO, e nao uma chamada ao `World`, porque a bancada sem cena (`--diagvisao`) monta
	/// este veu sem mundo nenhum -- e ela precisa provar a regra com o mesmo codigo que o jogo roda.
	/// </summary>
	public Func<int, int, bool>? SobTeto
	{
		get => _sobTeto;
		set
		{
			// O MESMO TETO DE NOVO (uma celula interna caiu, e o dono do teto avisou): a mascara muda, o
			// comodo do olho nao -- ver <see cref="InvalidarOComodo"/>. Dois delegados do mesmo metodo
			// do mesmo objeto sao IGUAIS pro `==`, e por isso a comparacao serve.
			bool outro = _sobTeto != value;
			_sobTeto = value;
			if (outro) InvalidarOComodo(); else Invalidar();
		}
	}
	private Func<int, int, bool>? _sobTeto;

	/// <summary>
	/// QUANTO A ALTURA ABRIU O VEU: 0 no chao, 1 de `Voo.AlturaQueAtravessa` pra cima. Quem esta por
	/// cima das paredes nao recebe a sombra delas -- MENOS a do interior que nao se ve, que continua
	/// breu (ver o cabecalho da classe).
	///
	/// E UM UNIFORM, e nao o `Modulate` do no, exatamente por causa desse "menos": o `Modulate`
	/// multiplica a malha inteira, e era ele que deixava ver dentro de casa pairando.
	/// </summary>
	public float Abertura
	{
		get => _abertura;
		set
		{
			value = Mathf.Clamp(value, 0f, 1f);
			if (Mathf.IsEqualApprox(_abertura, value)) return;
			_abertura = value;
			_tinta.SetShaderParameter("abertura", value);
		}
	}
	private float _abertura;

	/// <summary>
	/// DEFEITO INJETADO (bancada): o interior que nao se ve NAO e breu -- nenhuma celula e
	/// <see cref="Escondida"/>. E o veu de ANTES de 2026-10-08, no mesmo binario: penumbra de 85% sobre
	/// o comodo fechado, veu inteiro aberto pra quem paira, e o corpo encostado na parede aparecendo
	/// pelo furo dela. Falso em jogo, sempre.
	/// </summary>
	public static bool InteriorSemBreuDeTeste;

	/// <summary>
	/// DE ONDE SE OLHA QUANDO NAO E DO CORPO -- o `client.perspective = EYE_PERSPECTIVE` do BYOND
	/// (`Tournament.dm:824`: quem assiste ao torneio enxerga de onde o olho esta, nao de onde o
	/// corpo ficou). Quem escreve e o `World`, a cada quadro, enquanto a camera estiver longe do
	/// corpo (ver `World.OlhoDoEspectador`); nulo = os pes do corpo, como sempre.
	/// </summary>
	public Vector2? OlhoEmprestado;

	/// <summary>
	/// ATE QUANDO ESTOU CEGO (o `blindT` do Solar Flare), em ms do relogio do cliente.
	///
	/// A CEGUEIRA MORA AQUI e nao numa cortina por cima da tela porque cegar E o campo de
	/// visao indo a zero -- e o mesmo sistema, com o leque vazio. Uma cortina separada seria
	/// um segundo jeito de escurecer a tela, e os dois teriam que combinar pra sempre.
	/// </summary>
	public ulong CegoAte;

	public bool Cego => Time.GetTicksMsec() < CegoAte;

	/// <summary>Estava cego no quadro passado? E o que deixa o <see cref="_Process"/> ver a cegueira ACABAR.</summary>
	private bool _estavaCego;

	/// <summary>
	/// DEFEITO INJETADO (bancada): acabada a cegueira ninguem manda redesenhar -- a cortina preta fica na
	/// tela ate o corpo dar um passo, como ficava ate 2026-10-08. Falso em jogo, sempre.
	/// </summary>
	public static bool CortinaDaCegueiraFicaDeTeste;

	/// <summary>
	/// A COR DA SOMBRA. O alfa e a unica coisa que se costuma querer mexer aqui: mais alto
	/// esconde melhor o interior das casas, mais baixo faz a sombra da arvore pesar menos no
	/// campo aberto.
	/// </summary>
	private static readonly Color Sombra = new(0.02f, 0.025f, 0.05f, 0.85f);

	/// <summary>
	/// Quanto o raio passa AO LADO da quina, em pixels.
	///
	/// Sao dois raios por quina: um que raspa por dentro (bate na parede) e um que raspa por
	/// fora (segue adiante). E o par deles que produz a descontinuidade -- a reta da sombra.
	///
	/// O desvio e em PIXELS, nao em angulo, e essa e a diferenca que importa: um epsilon
	/// angular fixo vira um desvio grande numa quina distante e some abaixo da precisao do
	/// float numa quina perto (a 20 px, 1e-4 rad da 0,002 px, e o vertice comeca a piscar
	/// entre uma face e outra conforme o jogador anda). Em pixels, a folga e sempre a mesma.
	/// </summary>
	private const float Folga = 0.3f;

	/// <summary>
	/// Raios soltos em angulos regulares, alem dos das quinas.
	///
	/// Nao servem pra desenhar sombra -- servem de PISO: garantem que nenhum setor entre dois
	/// vertices vizinhos chegue perto de 180 graus, que e a condicao pro leque de triangulos
	/// nao se dobrar sobre si mesmo. Custam nada e tiram uma classe inteira de caso-limite.
	/// </summary>
	private static int RaiosSoltos => Boot.Config.Grafico switch
	{
		Settings.GraficoBaixo => 8,
		Settings.GraficoMedio => 16,
		_ => 40,
	};

	/// <summary>Margem alem da tela, em pixels. Cobre a folga de um quadro de camera.</summary>
	private const float Margem = 96f;

	/// <summary>
	/// Quanto a amostra de face fica PRA FORA da parede, em pixels. A sombra comeca exatamente
	/// na face; perguntar em cima dela e perguntar num empate de float. Meio pixel pra dentro
	/// da celula livre esta do lado de ca da fronteira sem exceção.
	/// </summary>
	private const float Recuo = 0.5f;

	// =====================================================================
	// OS DEFEITOS INJETAVEIS DA BANCADA (`--diagvisao`)
	// =====================================================================
	/// <summary>DEFEITO INJETADO: o raio para na SAIDA da celula cega (a regra do dente de serra e da cunha).</summary>
	public static bool ParaNaSaidaDeTeste;

	/// <summary>DEFEITO INJETADO: nenhuma parede ganha furo -- a face que encara o jogador fica escura.</summary>
	public static bool SemFacesDeTeste;

	/// <summary>DEFEITO INJETADO: a tela NAO e alargada ate conter o olho (o leque dobra com o olho fora dela).</summary>
	public static bool TelaSemOlhoDeTeste;

	private readonly List<Raio> _raios = [];
	private Vector2[] _pontos = [];
	private Color[] _cores = [];
	private int[] _indices = [];

	private Vector2 _ultimoOlho = new(float.NaN, float.NaN);
	private Rect2 _ultimaTela;

	/// <summary>De onde o leque atual foi lancado. Guardado porque <see cref="Ve"/> precisa.</summary>
	private Vector2 _olho;

	/// <summary>O retangulo do ultimo leque (ja contendo o olho). Diagnostico.</summary>
	private Rect2 _tela;

	// =====================================================================
	// OS FUROS: as paredes claras, por celula
	// =====================================================================
	/// <summary>
	/// O shader que abre os furos. A malha e um leque de quadrilateros e nao da pra recortar uma
	/// celula dele; o fragmento que cai numa celula marcada e simplesmente descartado. A mascara e
	/// uma textura de um byte por celula cobrindo o retangulo da tela (uns 3 KB), refeita junto com
	/// o leque.
	///
	/// A MASCARA TEM TRES VALORES (ver <see cref="ValorDoFuro"/> e <see cref="ValorDoBreu"/>): a parede
	/// clara e descartada, o interior que nao se ve sai PRETO OPACO, e todo o resto e a sombra comum --
	/// a cor do vertice, que e a unica coisa que a altura abre (`abertura`).
	///
	/// A CEGUEIRA TEM RAMO PROPRIO (`cego`): ela e preto puro na tela inteira, sem furo e sem
	/// abertura. Antes ela era so um retangulo preto passando por este mesmo fragmento -- com os
	/// furos do ultimo leque ainda na mascara, e sumindo com a altura junto do resto.
	/// </summary>
	private const string CodigoDosFuros = """
		shader_type canvas_item;
		render_mode unshaded, blend_mix;
		uniform sampler2D mascara : filter_nearest, repeat_disable;
		uniform vec2 origem;    // celula do canto da mascara
		uniform vec2 tamanho;   // celulas cobertas
		uniform float abertura = 0.0;   // 0 = veu fechado; 1 = aberto pela altura (so a sombra comum)
		uniform float cego = 0.0;       // 1 = a cortina da cegueira: preto em tudo
		varying vec2 mundo;
		void vertex() { mundo = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy; }
		void fragment() {
			if (cego > 0.5) {
				COLOR = vec4(0.0, 0.0, 0.0, 1.0);
			} else {
				vec2 c = floor(mundo / 32.0) - origem;
				float m = 0.0;
				if (c.x >= 0.0 && c.y >= 0.0 && c.x < tamanho.x && c.y < tamanho.y)
					m = texture(mascara, (c + 0.5) / tamanho).r;
				if (m > 0.75) discard;                               // a parede clara
				if (m > 0.25) COLOR = vec4(0.0, 0.0, 0.0, 1.0);      // o interior que nao se ve: breu
				else COLOR.a *= 1.0 - abertura;                      // a sombra comum, que a altura abre
			}
		}
		""";

	/// <summary>O byte da mascara que vira FURO: a parede clara. O shader descarta acima de 0,75.</summary>
	private const byte ValorDoFuro = 255;

	/// <summary>
	/// O byte da mascara que vira BREU: a celula <see cref="Escondida"/>. Fica no meio da escala (0,5
	/// lido como L8) de proposito -- o shader separa os tres valores por dois cortes, 0,25 e 0,75, e
	/// nenhum arredondamento de textura de 8 bits chega perto de um deles.
	/// </summary>
	private const byte ValorDoBreu = 128;

	private readonly ShaderMaterial _tinta = new() { Shader = new Shader { Code = CodigoDosFuros } };
	private byte[] _furos = [];
	private int _furosLarg, _furosAlt, _furosX0, _furosY0;
	private ImageTexture? _mascara;

	/// <summary>Quantas paredes estao claras no ultimo leque. Diagnostico.</summary>
	public int Furos { get; private set; }

	/// <summary>Quantas celulas da tela sao breu no ultimo leque. Diagnostico.</summary>
	public int Breus { get; private set; }

	/// <summary>
	/// Quantos raios pararam em algo que NAO e a fronteira livre/cega. E zero por construcao com a
	/// regra da face de entrada, e e o numero que separa esta regra das duas anteriores (que
	/// paravam na saida de uma celula cega -- na fronteira cega/livre, do lado errado).
	/// </summary>
	public int ParadasForaDeFace { get; private set; }

	/// <summary>
	/// O maior setor entre dois raios vizinhos, em graus. Acima de 180 o leque DOBRA: o
	/// quadrilatero entre os dois raios passa por cima do olho e cobre a tela do lado de ca.
	/// </summary>
	public float MaiorSetorGraus { get; private set; }

	public bool Dobrado => MaiorSetorGraus >= 180f;
	public Vector2 OlhoDeTeste => _olho;
	public Rect2 TelaDeTeste => _tela;

	/// <summary>Um raio resolvido: pra onde apontou, ate onde foi, e em que angulo saiu.</summary>
	private readonly struct Raio(Vector2 dir, float dist, float angulo, int ordem)
	{
		public readonly Vector2 Dir = dir;
		public readonly float Dist = dist;
		public readonly float Angulo = angulo;

		/// <summary>Ordem de criacao. E o desempate FINAL da ordenacao -- ver Ordenar().</summary>
		public readonly int Ordem = ordem;
	}

	public override void _Ready()
	{
		// POR CIMA DE TUDO que e mundo. z_index vence a ordenacao por Y, entao a sombra cobre
		// tiles e personagens. O HUD nao entra nisso: e CanvasLayer, camada de tela.
		ZIndex = 90;

		// desenha em coordenadas de MUNDO: a malha e montada com posicoes globais
		TopLevel = true;

		// NENHUMA LUZ TOCA A SOMBRA. Sem isto, a fogueira do cenario iluminaria a propria
		// sombra -- uma PointLight2D aditiva bate em todo CanvasItem que compartilhe a
		// mascara, e sairia um clarao laranja no meio do escuro. (O shader diz `unshaded`
		// pelo mesmo motivo; as duas linhas dizem a mesma coisa pra dois caminhos do motor.)
		LightMask = 0;
		Material = _tinta;
	}

	// =====================================================================
	// QUANDO REFAZER
	// =====================================================================
	/// <summary>
	/// O MUNDO MUDOU: refaz o leque no proximo quadro mesmo que ninguem tenha se mexido.
	///
	/// ============================ SOMBRA E DO MAPA, NAO SO DO OLHO ============================
	/// O recalculo e pulado quando o olho e a tela estao no mesmo lugar -- e essa e a otimizacao
	/// que faz a sombra caber no orcamento. Mas ela assume que o MAPA nao muda, e ele muda: uma
	/// parede que cai com o jogador parado deixava a sombra dela projetada no chao ate ele andar.
	/// ========================================================================================
	/// </summary>
	public void Invalidar() => _ultimoOlho = new Vector2(float.NaN, float.NaN);

	/// <summary>
	/// O MUNDO MUDOU DE UM JEITO QUE LIGA OU SEPARA COMODOS: outra zona, ou uma PORTA que abriu ou
	/// fechou. Refaz o leque e tambem o comodo do olho (<see cref="AcharOComodo"/>).
	///
	/// PAREDE QUE CAI NAO PASSA POR AQUI, e nao e economia as cegas: a celula que cai deixa de estar
	/// sob teto (no DM o turf destruido volta pra area de fora, `NewTurfs.dm:13-17`), e o comodo so se
	/// espalha por celula sob teto -- o buraco na parede nao liga dois comodos, ele vira um pedaco de
	/// ar livre entre os dois. O que se ve por ele e o que o leque alcanca, como numa porta aberta. E
	/// e isso que deixa a varredura (na caverna, o mapa inteiro) fora do caminho de uma briga que
	/// derruba parede atras de parede.
	/// </summary>
	public void InvalidarOComodo()
	{
		_comodoVelho = true;
		Invalidar();
	}

	public override void _Process(double delta)
	{
		if (Alvo == null || Mapa == null) { Visible = false; return; }
		Visible = true;

		// cego: nao ha leque pra recalcular, e a cada quadro a cortina tem que continuar de pe
		if (Cego) { _estavaCego = true; QueueRedraw(); return; }

		// A CEGUEIRA ACABOU, E A CORTINA FOI O ULTIMO DESENHO. O recalculo logo abaixo so acontece quando o olho
		// ou a tela se mexem -- entao quem ficava PARADO enquanto estava cego continuava com a tela preta depois
		// do prazo, ate dar um passo. Medido na `--diagsombra` (2026-10-08): o chao a 156 de 255 antes, 0 com a
		// cegueira ligada, e 0 ainda sete decimos de segundo depois de ela acabar.
		if (_estavaCego)
		{
			_estavaCego = false;
			if (!CortinaDaCegueiraFicaDeTeste) Invalidar();
		}

		Vector2 olho = Olho();
		Rect2 tela = Tela(olho);
		if (olho.IsEqualApprox(_ultimoOlho) && tela.Position.IsEqualApprox(_ultimaTela.Position)
			&& tela.Size.IsEqualApprox(_ultimaTela.Size)) return;

		_ultimoOlho = olho;
		_ultimaTela = tela;
		QueueRedraw();
	}

	/// <summary>
	/// DE ONDE SE OLHA -- e nao e o centro do sprite.
	///
	/// A caixa de colisao do personagem sao os PES (<see cref="MoveRules.FeetOffsetY"/> abaixo
	/// do centro), entao encostar de frente numa parede horizontal e um estado NORMAL em que o
	/// centro do corpo esta DENTRO da celula bloqueante. Olhando dali, todo raio lateral bate
	/// na parede vizinha no primeiro passo e a tela inteira apaga -- e isso aconteceria toda
	/// vez que alguem encostasse num muro.
	///
	/// O centro da caixa dos pes, ao contrario, e o unico ponto que a regra de movimento
	/// GARANTE estar fora de parede (uma celula tem 32 px e a caixa 16x10: nao ha como o
	/// centro cair dentro de um bloco com os quatro cantos livres).
	///
	/// O OLHO EMPRESTADO vence, quando existe: quem assiste ao torneio olha de onde a camera esta.
	/// </summary>
	private Vector2 Olho() => OlhoEmprestado ?? Alvo!.GlobalPosition + new Vector2(0, MoveRules.FeetOffsetY);

	/// <summary>
	/// O retangulo em que a sombra e calculada: a camera mais uma margem -- e SEMPRE contendo o
	/// olho. Um olho fora do retangulo nao tem raio pro lado oposto (o `AteABorda` da limite
	/// negativo e o raio e descartado), e um leque que nao fecha os 360 graus dobra sobre si
	/// mesmo. Era o que a camera do espectador fazia com o corpo parado na area de espera.
	/// </summary>
	private Rect2 Tela(Vector2 olho)
	{
		Camera2D? cam = GetViewport()?.GetCamera2D();
		Vector2 tam = GetViewportRect().Size;
		if (cam != null) tam /= cam.Zoom;
		Vector2 centro = cam?.GetScreenCenterPosition() ?? olho;
		var tela = new Rect2(centro - tam * 0.5f - new Vector2(Margem, Margem),
							 tam + new Vector2(Margem, Margem) * 2f);
		return ComOOlhoDentro(tela, olho);
	}

	/// <summary>A tela alargada ate conter o olho com folga. Publico porque a bancada monta a tela na mao.</summary>
	public static Rect2 ComOOlhoDentro(Rect2 tela, Vector2 olho)
	{
		if (TelaSemOlhoDeTeste) return tela;
		var emVolta = new Rect2(olho - new Vector2(Margem, Margem), new Vector2(Margem, Margem) * 2f);
		return tela.Merge(emVolta);
	}

	// =====================================================================
	// O DESENHO
	// =====================================================================
	public override void _Draw()
	{
		if (Alvo == null || Mapa == null) return;

		// CEGO: preto puro, sem furo nenhum. Nem o proprio corpo aparece -- e o que separa
		// "estou no escuro" (da pra se orientar pelo que se lembra) de "nao enxergo".
		//
		// QUEM FAZ O "SEM FURO NENHUM" E O RAMO `cego` DO SHADER, e nao este retangulo: o retangulo
		// passa pelo mesmo fragmento da malha, e sem o ramo ele saia com os furos do ultimo leque (as
		// paredes claras continuavam aparecendo pra quem estava cego) e sumia com a altura.
		bool cego = Cego;
		if (cego != _cegoNoShader)
		{
			_cegoNoShader = cego;
			_tinta.SetShaderParameter("cego", cego ? 1f : 0f);
		}
		if (cego)
		{
			Rect2 t = Tela(Olho());
			DrawRect(new Rect2(t.Position - t.Size, t.Size * 3f), Colors.Black);
			return;
		}

		Vector2 p = Olho();
		Rect2 tela = Tela(p);

		// ============================ O GUARDA OLHAVA PRO MAPA ERRADO ============================
		// Ele testava o mapa de VISAO, e o comentario dizia "nao deveria acontecer". Acontecia, e
		// num lugar exato: a PORTA. Ela e a unica celula em que os dois mapas discordam -- cega e
		// nao bloqueia (ver o comentario do MapConverter). Pisar nela apagava a sombra inteira.
		//
		// Medido: 72 celulas assim nos 10 mapas, TODAS portas. O dono viu como "dentro de portas as
		// sombras somem".
		//
		// O guarda continua existindo, porque teleporte pra dentro de rocha e um estado do qual nao
		// da pra sair; so que ele agora pergunta pro mapa que responde isso -- o de COLISAO. Estar
		// numa celula CEGA e legitimo, e o DDA lida bem: ele so olha as celulas em que ENTRA, nunca
		// a de origem.
		//
		// SEM LEQUE NAO HA SOMBRA COMUM -- MAS O BREU CONTINUA (2026-10-08). O guarda dispara num lugar
		// facil de alcancar: o vao de uma porta que fechou em cima de quem parou nele. Com o `return`
		// seco bastava isso pra ver dentro de todas as casas da tela. A malha, ai, e o retangulo da
		// tela com a cor do vertice TRANSPARENTE: fora das celulas de breu o fragmento nao pinta nada,
		// que e a "tela sem veu" que o guarda sempre deu.
		if (!Preparar(p, tela))
		{
			if (Breus > 0) DrawRect(tela, new Color(0f, 0f, 0f, 0f));
			return;
		}

		if (_raios.Count < 3) return;
		Montar(p, tela);
	}

	/// <summary>
	/// TUDO QUE O DESENHO VAI USAR, SEM DESENHAR: o leque, os furos e o breu deste ponto de vista --
	/// ou so o breu, se o olho esta dentro de parede (o guarda do <see cref="_Draw"/>, que le
	/// <see cref="Colisao"/>). Publico pelo mesmo motivo do <see cref="Recalcular"/>: a bancada sem
	/// cena precisa passar pelo guarda, e nao so pelo caminho feliz.
	/// </summary>
	/// <returns>Verdadeiro se ha leque; falso se o olho esta dentro de parede.</returns>
	public bool Preparar(Vector2 olho, Rect2 tela)
	{
		if (Colisao?.BlockedAt(new Vec2(olho.X, olho.Y)) == true) { SoOBreu(olho, tela); return false; }
		Recalcular(olho, tela);
		return true;
	}

	// =====================================================================
	// O LEQUE, DISPONIVEL PRA QUEM PRECISAR SABER "DA PRA VER?"
	// =====================================================================
	/// <summary>
	/// Refaz o leque (e os furos) de um ponto de vista. Publico porque o diagnostico (`--diagvisao`)
	/// precisa rodar isto SEM cena, sem camera e sem janela.
	/// </summary>
	public void Recalcular(Vector2 olho, Rect2 tela)
	{
		_olho = olho;
		_tela = tela;
		_semLeque = false;
		ParadasForaDeFace = 0;
		Mirar(olho, tela);
		Ordenar();
		MedirSetores();
		ConferirOComodo(olho);
		Furar(tela);
	}

	/// <summary>
	/// O OLHO ESTA DENTRO DE PAREDE: nao ha leque -- e sem leque nao ha furo nem sombra comum. O que
	/// sobra e SO o breu, marcado na mascara pra quem for desenhar.
	/// </summary>
	private void SoOBreu(Vector2 olho, Rect2 tela)
	{
		_olho = olho;
		_tela = tela;
		_raios.Clear();
		_semLeque = true;
		ConferirOComodo(olho);
		Furar(tela);
	}

	/// <summary>O ultimo desenho foi feito SEM leque (o olho dentro de parede)? Ver <see cref="Esconde"/>.</summary>
	private bool _semLeque;

	/// <summary>O que o uniform `cego` do shader vale agora, pra nao reescreve-lo a cada desenho.</summary>
	private bool _cegoNoShader;

	/// <summary>Quantos raios o ultimo leque usou. Diagnostico.</summary>
	public int QuantosRaios => _raios.Count;

	/// <summary>
	/// ESTE PONTO E VISIVEL do ultimo ponto de vista calculado?
	///
	/// O leque ja e a resposta: acha-se o par de raios que abraca o angulo do ponto e
	/// pergunta-se de que lado da aresta entre eles o ponto caiu. Do mesmo lado que o
	/// observador, ve; do outro, esta na sombra.
	///
	/// E O(log n) e nao lanca raio nenhum -- da pra perguntar por dezenas de alvos no mesmo
	/// quadro sem custo. (E e por isso que a pergunta "que parede esta clara?" cabe no
	/// orcamento: sao ate doze destas por parede da tela.)
	/// </summary>
	public bool Ve(Vector2 ponto)
	{
		int n = _raios.Count;
		if (n < 3) return true;

		Vector2 v = ponto - _olho;
		if (v.LengthSquared() < 1f) return true;
		float a = Angulo(v);

		// primeiro raio com angulo > a; o par e (esse-1, esse), com volta no fim
		int lo = 0, hi = n;
		while (lo < hi)
		{
			int meio = (lo + hi) >> 1;
			if (_raios[meio].Angulo <= a) lo = meio + 1; else hi = meio;
		}
		int j = lo % n;
		int i = (j - 1 + n) % n;

		Vector2 A = _olho + _raios[i].Dir * _raios[i].Dist;
		Vector2 B = _olho + _raios[j].Dir * _raios[j].Dist;
		Vector2 e = B - A;
		if (e.LengthSquared() < 1e-6f) return v.Length() <= _raios[i].Dist;

		float ladoPonto = e.X * (ponto.Y - A.Y) - e.Y * (ponto.X - A.X);
		float ladoOlho = e.X * (_olho.Y - A.Y) - e.Y * (_olho.X - A.X);
		return ladoPonto * ladoOlho >= 0f;
	}

	/// <summary>
	/// ESTA PAREDE ESTA CLARA no ultimo leque? (Fora do retangulo calculado: nao.) E o que o
	/// shader le, exposto pra bancada e pra quem mais precisar.
	/// </summary>
	public bool ParedeIluminada(int cx, int cy)
	{
		int x = cx - _furosX0, y = cy - _furosY0;
		return x >= 0 && y >= 0 && x < _furosLarg && y < _furosAlt && _furos[y * _furosLarg + x] == ValorDoFuro;
	}

	/// <summary>
	/// ESTA CELULA SAIU COMO BREU no ultimo leque? (Fora do retangulo calculado: nao.) E o que o
	/// shader le -- o byte da mascara --, exposto pra bancada: <see cref="Escondida"/> diz a REGRA, isto
	/// diz o que foi entregue ao desenho (a parede clara vence o breu, e so aqui isso aparece).
	/// </summary>
	public bool Breu(int cx, int cy)
	{
		int x = cx - _furosX0, y = cy - _furosY0;
		return x >= 0 && y >= 0 && x < _furosLarg && y < _furosAlt && _furos[y * _furosLarg + x] == ValorDoBreu;
	}

	// =====================================================================
	// O COMODO DO OLHO, E O QUE E BREU
	// =====================================================================
	/// <summary>
	/// O COMODO DO OLHO: o chao sob teto ligado ao do olho de lado em lado, e a massa de parede que o
	/// cerca. E o que separa "estou DENTRO" de "estou olhando de fora": nele a sombra continua sendo a
	/// de sempre.
	///
	/// POR CONEXAO, e nao por "o olho esta sob teto?", por dois motivos:
	///   * do vao da porta da MINHA casa eu veria o interior da casa do vizinho na penumbra -- bastava
	///     estar debaixo de um teto qualquer;
	///   * a caverna e interna INTEIRA (53.878 celulas numa delas, contadas pelo `DentroBench`). Sem a
	///     conexao ela viraria breu atras de cada pedra; com ela, o corredor em que se anda e um comodo so.
	///
	/// AS DUAS REGRAS DE ESPALHAR, e a assimetria entre elas e a regra inteira:
	///   * o CHAO so se alcanca de LADO, e so a partir de chao -- duas celulas de chao que so se tocam
	///     pela quina entre duas paredes nao se veem (o leque sela essa quina, ver <see cref="Marchar"/>),
	///     e parede nenhuma "abre" pro chao do outro lado dela: e isso que deixa o comodo vizinho de fora;
	///   * a PAREDE entra pelos oito lados, e de parede em parede -- o canto do comodo e a parede grossa
	///     (o Banco tem duas fileiras no fundo; a rocha da caverna tem dezenas) sao a massa do MEU
	///     predio. Sem isto a rocha atras da primeira camada sairia preta onde sempre foi penumbra.
	///
	/// UM BYTE POR CELULA DO MAPA, e nao um conjunto de chaves: na caverna a varredura cobre o mapa
	/// inteiro. Medido pela `--diagvisao` na da Terra: 32.079 celulas em 4,75 ms -- um terco de quadro,
	/// e por isso ela so roda quando o comodo pode ter mudado (<see cref="InvalidarOComodo"/>).
	/// </summary>
	private byte[] _comodo = [];
	private int _comodoLarg, _comodoAlt;
	private bool _comodoVazio = true;
	private readonly Stack<int> _pilha = new();
	private (int Cx, int Cy) _celulaDoComodo;
	private bool _comodoVelho = true;

	private const byte ChaoDoComodo = 1, ParedeDoComodo = 2;

	/// <summary>Quantas celulas (chao e parede) o comodo do olho tem agora. Diagnostico.</summary>
	public int CelulasDoComodo { get; private set; }

	private byte NoComodo(int cx, int cy)
		=> _comodoVazio || cx < 0 || cy < 0 || cx >= _comodoLarg || cy >= _comodoAlt ? (byte)0 : _comodo[cy * _comodoLarg + cx];

	/// <summary>
	/// Refaz o comodo SE PRECISA: o mapa mudou, ou o olho saiu do chao dele. Andar dentro do mesmo
	/// comodo nao refaz nada.
	/// </summary>
	private void ConferirOComodo(Vector2 olho)
	{
		const int T = ZoneCollision.TileSize;
		int ox = (int)MathF.Floor(olho.X / T), oy = (int)MathF.Floor(olho.Y / T);
		if (!_comodoVelho)
		{
			if ((ox, oy) == _celulaDoComodo) return;
			// outra celula de CHAO do mesmo comodo (de uma parede dele o conjunto nasce de novo)
			if (NoComodo(ox, oy) == ChaoDoComodo) { _celulaDoComodo = (ox, oy); return; }
		}
		AcharOComodo(ox, oy);
	}

	private void AcharOComodo(int ox, int oy)
	{
		_comodoVelho = false;
		_celulaDoComodo = (ox, oy);
		CelulasDoComodo = 0;
		if (!_comodoVazio) { Array.Clear(_comodo); _comodoVazio = true; }
		if (_sobTeto == null || _mapa == null || !_sobTeto(ox, oy)) return;   // olho ao ar livre: comodo nenhum

		int w = _mapa.Width, h = _mapa.Height;
		if (ox < 0 || oy < 0 || ox >= w || oy >= h) return;
		if (_comodo.Length != w * h) _comodo = new byte[w * h];
		_comodoLarg = w;
		_comodoAlt = h;
		_comodoVazio = false;

		// A CELULA DO OLHO ESPALHA COMO CHAO MESMO CEGA: e o vao de uma porta que fechou em cima de quem
		// estava nele.
		_pilha.Clear();
		_comodo[oy * w + ox] = ChaoDoComodo;
		_pilha.Push(oy * w + ox);
		while (_pilha.Count > 0)
		{
			int i = _pilha.Pop();
			CelulasDoComodo++;
			int cx = i % w, cy = i / w;
			bool deChao = _comodo[i] == ChaoDoComodo;
			for (int dy = -1; dy <= 1; dy++)
			{
				int ny = cy + dy;
				if (ny < 0 || ny >= h) continue;
				for (int dx = -1; dx <= 1; dx++)
				{
					int nx = cx + dx;
					if ((dx == 0 && dy == 0) || nx < 0 || nx >= w) continue;
					int j = ny * w + nx;
					if (_comodo[j] != 0 || !_sobTeto(nx, ny)) continue;
					bool cega = Cega(nx, ny);
					if (!cega && (!deChao || (dx != 0 && dy != 0))) continue;   // chao: so de lado, e so de chao
					_comodo[j] = cega ? ParedeDoComodo : ChaoDoComodo;
					_pilha.Push(j);
				}
			}
		}
	}

	/// <summary>
	/// ESTA CELULA E BREU PRA QUEM NAO A VE? Sob teto, e fora do comodo do olho. A pergunta e da
	/// REGRA e vale pra qualquer celula do mapa; o que de fato vai pro desenho e <see cref="Breu"/>.
	/// </summary>
	public bool Escondida(int cx, int cy)
		=> !InteriorSemBreuDeTeste && _sobTeto != null && _sobTeto(cx, cy) && NoComodo(cx, cy) == 0;

	/// <summary>
	/// QUEM TEM OS PES AQUI NAO E DESENHADO? -- numa celula <see cref="Escondida"/>, e fora do leque.
	///
	/// POR QUE O VEU NAO BASTA: ele cobre a CELULA, e o que esta nela nem sempre cabe nela. O corpo
	/// encostado na parede de dentro invade ate 19 px da celula da parede, e essa parede, vista de
	/// fora, e um FURO; a bancada de pesquisa tem 96 px de largura. Entao quem decide e o pe: se o pe
	/// esta no breu, nao se desenha nada de quem e dono dele (e o balao, a aura e o anel vao junto, que
	/// sao filhos do mesmo no).
	///
	/// SEM LEQUE (o olho dentro de parede) nada e visto: a celula escondida esconde, e pronto.
	/// </summary>
	public bool Esconde(Vector2 pes)
	{
		const int T = ZoneCollision.TileSize;
		if (!Escondida((int)MathF.Floor(pes.X / T), (int)MathF.Floor(pes.Y / T))) return false;
		return _semLeque || !Ve(pes);
	}

	/// <summary>
	/// Monta a lista de raios: dois por quina de parede, quatro nos cantos da tela e alguns
	/// soltos de reserva. Cada um ja sai resolvido (marchado ate bater ou sair da tela).
	/// </summary>
	private void Mirar(Vector2 p, Rect2 tela)
	{
		_raios.Clear();
		const int T = ZoneCollision.TileSize;

		int cx0 = (int)MathF.Floor(tela.Position.X / T);
		int cy0 = (int)MathF.Floor(tela.Position.Y / T);
		int cx1 = (int)MathF.Floor(tela.End.X / T);
		int cy1 = (int)MathF.Floor(tela.End.Y / T);

		// QUINAS. Um ponto da grade e quina quando as quatro celulas em volta dele NAO sao
		// todas iguais -- ou seja, ali a parede comeca, acaba ou dobra. Onde as quatro sao
		// iguais nao ha nada que projete borda, e lancar raio seria trabalho jogado fora.
		for (int gy = cy0; gy <= cy1 + 1; gy++)
			for (int gx = cx0; gx <= cx1 + 1; gx++)
			{
				bool a = Cega(gx - 1, gy - 1), b = Cega(gx, gy - 1);
				bool c = Cega(gx - 1, gy), d = Cega(gx, gy);
				if (a == b && b == c && c == d) continue;
				Quina(p, tela, new Vector2(gx * T, gy * T));
			}

		// OS CANTOS DA TELA fecham o leque. Sem eles, dois raios vizinhos podem terminar em
		// bordas DIFERENTES do retangulo e a aresta entre os dois cortaria o canto da tela --
		// apareceria um triangulo escuro na quina do monitor, do nada.
		foreach (Vector2 canto in new[] { tela.Position, new Vector2(tela.End.X, tela.Position.Y), tela.End, new Vector2(tela.Position.X, tela.End.Y) })
			Lancar(p, tela, canto - p);

		for (int i = 0; i < RaiosSoltos; i++)
		{
			float ang = Mathf.Tau * i / RaiosSoltos;
			Lancar(p, tela, new Vector2(MathF.Cos(ang), MathF.Sin(ang)));
		}
	}

	/// <summary>Os dois raios de uma quina: um raspando de cada lado.</summary>
	private void Quina(Vector2 p, Rect2 tela, Vector2 quina)
	{
		Vector2 v = quina - p;
		float d = v.Length();
		if (d < 1f) return;                       // quina embaixo do pe: nao ha lado de ca nem de la
		Vector2 perp = new Vector2(-v.Y, v.X) / d;
		Lancar(p, tela, v + perp * Folga);
		Lancar(p, tela, v - perp * Folga);
	}

	private void Lancar(Vector2 p, Rect2 tela, Vector2 rumo)
	{
		float n = rumo.Length();
		if (n < 1e-6f) return;
		Vector2 d = rumo / n;

		float limite = AteABorda(p, d, tela);
		if (limite <= 0.5f) return;

		float dist = Marchar(p, d, limite);
		if (dist < limite - 0.01f && !_paradaNaFace) ParadasForaDeFace++;
		_raios.Add(new Raio(d, dist, Angulo(d), _raios.Count));
	}

	/// <summary>
	/// A ultima parada de <see cref="Marchar"/> foi numa fronteira livre/cega? Escrito pelo DDA, que
	/// sabe de onde o raio veio; um teste geometrico depois da parada (meio pixel antes, meio pixel
	/// depois) classificava errado o raio que raspa uma quina e sairia da celula pela face vizinha.
	/// </summary>
	private bool _paradaNaFace;

	/// <summary>Onde o raio sai do retangulo da tela. E o "infinito" util deste sistema.</summary>
	private static float AteABorda(Vector2 p, Vector2 d, Rect2 r)
	{
		float t = float.MaxValue;
		if (MathF.Abs(d.X) > 1e-6f)
			t = MathF.Min(t, ((d.X > 0 ? r.End.X : r.Position.X) - p.X) / d.X);
		if (MathF.Abs(d.Y) > 1e-6f)
			t = MathF.Min(t, ((d.Y > 0 ? r.End.Y : r.Position.Y) - p.Y) / d.Y);
		return t == float.MaxValue ? 0f : t;
	}

	/// <summary>
	/// ATE ONDE O RAIO VAI. Caminha CELULA A CELULA pela grade (Amanatides-Woo) em vez de
	/// amostrar de N em N pixels.
	///
	/// A diferenca nao e so de desempenho: o passo fixo ERRA por construcao (ele para no
	/// primeiro ponto amostrado que ja esta dentro da parede, nunca na face dela), e e esse
	/// erro que fazia a borda tremer. Aqui a parada e exatamente a face da celula, entao dois
	/// raios que batem na MESMA parede devolvem pontos exatamente colineares -- a borda sai
	/// reta sozinha, sem precisar juntar arestas nem fundir nada.
	///
	/// A PARADA E A FACE DE ENTRADA da primeira celula cega. So isso. As regras que levavam o
	/// raio pra dentro do bloco (saida da celula, travessia ate o tile mudar) estao contadas no
	/// cabecalho da classe, com o que cada uma custou; quem ilumina a parede agora e
	/// <see cref="FaceVisivel"/>, celula a celula, e nao o ponto de parada.
	/// </summary>
	public float Marchar(Vector2 p, Vector2 d, float limite)
	{
		const int T = ZoneCollision.TileSize;
		int cx = (int)MathF.Floor(p.X / T);
		int cy = (int)MathF.Floor(p.Y / T);

		int sx = d.X > 0 ? 1 : d.X < 0 ? -1 : 0;
		int sy = d.Y > 0 ? 1 : d.Y < 0 ? -1 : 0;

		float tdx = sx != 0 ? MathF.Abs(T / d.X) : float.MaxValue;
		float tdy = sy != 0 ? MathF.Abs(T / d.Y) : float.MaxValue;

		float tmx = sx > 0 ? ((cx + 1) * T - p.X) / d.X
				  : sx < 0 ? (cx * T - p.X) / d.X
				  : float.MaxValue;
		float tmy = sy > 0 ? ((cy + 1) * T - p.Y) / d.Y
				  : sy < 0 ? (cy * T - p.Y) / d.Y
				  : float.MaxValue;

		// DE ONDE O RAIO VEM: parada "numa face" e a entrada numa celula cega vindo de uma LIVRE. A
		// celula de origem pode ser cega (o olho dentro de uma porta) -- dali o raio sai, nao para.
		bool veioDeLivre = !Cega(cx, cy);
		_paradaNaFace = false;

		// teto de passos: a diagonal da tela tem ~40 celulas, e o `limite` ja para o laco --
		// isto e so cinto de seguranca contra um raio degenerado
		for (int passo = 0; passo < 256; passo++)
		{
			bool porX = tmx < tmy;
			float t = porX ? tmx : tmy;
			if (t >= limite) return limite;

			// CANTO EXATO: o raio cruza um vertice da grade, os dois eixos avancam juntos.
			// Duas paredes que so se tocam pela diagonal nao tem fresta de verdade -- se as
			// duas laterais bloqueiam, o vertice conta como muro. Sem esta regra nasce uma
			// agulha de luz de 1 px que pisca a cada passo do jogador.
			if (MathF.Abs(tmx - tmy) < 0.01f)
			{
				bool selado = Cega(cx + sx, cy) && Cega(cx, cy + sy);
				cx += sx; cy += sy;
				tmx += tdx; tmy += tdy;
				if (selado && !ParaNaSaidaDeTeste) { _paradaNaFace = veioDeLivre; return MathF.Min(t, limite); }
			}
			else if (porX) { cx += sx; tmx += tdx; }
			else { cy += sy; tmy += tdy; }

			if (!Cega(cx, cy)) { veioDeLivre = true; continue; }

			// `t` e onde o raio ENTRA na celula cega em que acabou de pisar: a sombra comeca aqui.
			if (!ParaNaSaidaDeTeste) { _paradaNaFace = veioDeLivre; return MathF.Min(t, limite); }

			// DEFEITO INJETADO (a regra de 2026-08-03): atravessa a celula e para na SAIDA dela --
			// no meio de um muro grosso, ou de raso, a saida e uma face lateral e a sombra nasce
			// dentro do muro. E o que a bancada precisa VER em numero pra provar que mede.
			return MathF.Min(MathF.Min(tmx, tmy), limite);
		}
		return limite;
	}

	// =====================================================================
	// OS FUROS
	// =====================================================================
	/// <summary>
	/// Marca as paredes claras da tela e sobe a mascara pro shader.
	///
	/// So as celulas cegas com algum vizinho livre entram na pergunta: uma parede cercada de
	/// parede nao tem face nenhuma pra mostrar. E so as da TELA -- o retangulo ja veio recortado.
	/// </summary>
	private void Furar(Rect2 tela)
	{
		const int T = ZoneCollision.TileSize;
		_furosX0 = (int)MathF.Floor(tela.Position.X / T);
		_furosY0 = (int)MathF.Floor(tela.Position.Y / T);
		int cx1 = (int)MathF.Floor(tela.End.X / T);
		int cy1 = (int)MathF.Floor(tela.End.Y / T);
		_furosLarg = cx1 - _furosX0 + 1;
		_furosAlt = cy1 - _furosY0 + 1;

		int n = _furosLarg * _furosAlt;
		if (_furos.Length != n) _furos = new byte[n];
		else Array.Clear(_furos);
		Furos = 0;

		if (!SemFacesDeTeste && _raios.Count >= 3)
			for (int cy = _furosY0; cy <= cy1; cy++)
				for (int cx = _furosX0; cx <= cx1; cx++)
				{
					if (!Cega(cx, cy) || !FaceVisivel(cx, cy)) continue;
					_furos[(cy - _furosY0) * _furosLarg + (cx - _furosX0)] = ValorDoFuro;
					Furos++;
				}

		// O BREU, DEPOIS DOS FUROS e so onde nao ha furo: a fachada com face visivel continua clara.
		// Nao depende de haver leque -- com o olho dentro de parede (`SoOBreu`) nao ha sombra comum, e
		// o interior dos outros continua escondido.
		Breus = 0;
		if (_sobTeto != null)
			for (int cy = _furosY0; cy <= cy1; cy++)
				for (int cx = _furosX0; cx <= cx1; cx++)
				{
					int i = (cy - _furosY0) * _furosLarg + (cx - _furosX0);
					if (_furos[i] != 0 || !Escondida(cx, cy)) continue;
					_furos[i] = ValorDoBreu;
					Breus++;
				}

		var img = Image.CreateFromData(_furosLarg, _furosAlt, false, Image.Format.L8, _furos);
		if (_mascara == null || _mascara.GetWidth() != _furosLarg || _mascara.GetHeight() != _furosAlt)
		{
			_mascara = ImageTexture.CreateFromImage(img);
			_tinta.SetShaderParameter("mascara", _mascara);
		}
		else _mascara.Update(img);
		_tinta.SetShaderParameter("origem", new Vector2(_furosX0, _furosY0));
		_tinta.SetShaderParameter("tamanho", new Vector2(_furosLarg, _furosAlt));
	}

	/// <summary>
	/// ALGUMA FACE DESTA PAREDE DA PRA UM PONTO VISIVEL? Tres amostras por face (a 1/6, 1/2 e 5/6
	/// da aresta), meio pixel pra fora, so nas faces que dao pra celula LIVRE -- a face entre duas
	/// paredes e interna e ninguem a ve.
	///
	/// Tres e nao uma: a parede parcialmente coberta por um pilar continua clara enquanto um
	/// pedaco dela aparecer. Um vao mais estreito que um terco de tile passa despercebido, e
	/// fica escuro -- limite aceito, e conhecido.
	/// </summary>
	private bool FaceVisivel(int cx, int cy)
	{
		const int T = ZoneCollision.TileSize;
		float x0 = cx * T, y0 = cy * T;
		return (!Cega(cx, cy - 1) && Aresta(x0, y0 - Recuo, T, 0))
			|| (!Cega(cx, cy + 1) && Aresta(x0, y0 + T + Recuo, T, 0))
			|| (!Cega(cx - 1, cy) && Aresta(x0 - Recuo, y0, 0, T))
			|| (!Cega(cx + 1, cy) && Aresta(x0 + T + Recuo, y0, 0, T));
	}

	/// <summary>As tres amostras de uma aresta que parte de (x, y) e mede (dx, dy).</summary>
	private bool Aresta(float x, float y, float dx, float dy)
		=> Ve(new Vector2(x + dx / 6f, y + dy / 6f))
		|| Ve(new Vector2(x + dx / 2f, y + dy / 2f))
		|| Ve(new Vector2(x + dx * 5f / 6f, y + dy * 5f / 6f));

	/// <summary>
	/// O QUE TAPA A VISTA. E o `BlockedCell` do mapa de visao, e por isso a Sala do Tempo passa por
	/// aqui sem uma linha propria: o `SemBorda` (ver `ZoneCollision`) faz o vazio branco em volta do
	/// quarto responder "nao cega", e o veu simplesmente nao acha nada pra sombrear.
	///
	/// SEM ESSE BIT o custo seria escondido e permanente -- medido em `--diagvazio`: 984 celulas de
	/// tela cegas e todo raio gastando o teto de 256 passos do DDA, por uma parede que nem chega a
	/// projetar sombra.
	/// </summary>
	private bool Cega(int cx, int cy) => _mapa!.BlockedCell(cx, cy);

	private static float Angulo(Vector2 d)
	{
		float a = MathF.Atan2(d.Y, d.X);
		if (a < 0f) a += Mathf.Tau;
		// a soma acima pode devolver Tau exato por arredondamento, e ai o raio sairia do
		// intervalo [0, Tau) e iria parar no fim do array em vez do comeco -- invertendo o par
		if (a >= Mathf.Tau) a -= Mathf.Tau;
		return a;
	}

	/// <summary>
	/// Ordena por angulo. O comparador NUNCA devolve 0, e isso e proposital.
	///
	/// Numa grade alinhada aos eixos, duas quinas na mesma horizontal do jogador dao angulos
	/// exatamente iguais o tempo todo. O Array.Sort do .NET e INSTAVEL: com empate, a ordem
	/// entre elas mudaria de quadro pra quadro sem o jogador andar um pixel, e o par de
	/// triangulos correspondente inverteria -- cintilacao. Desempatar por distancia e, por
	/// fim, por ordem de criacao torna a saida deterministica.
	/// </summary>
	private void Ordenar() => _raios.Sort((x, y) =>
	{
		int c = x.Angulo.CompareTo(y.Angulo);
		if (c != 0) return c;
		c = x.Dist.CompareTo(y.Dist);
		return c != 0 ? c : x.Ordem.CompareTo(y.Ordem);
	});

	/// <summary>O maior setor entre raios vizinhos, contando a volta do ultimo pro primeiro.</summary>
	private void MedirSetores()
	{
		int n = _raios.Count;
		MaiorSetorGraus = n == 0 ? 360f : 0f;
		for (int i = 0; i < n; i++)
		{
			float a = _raios[i].Angulo;
			float b = i + 1 < n ? _raios[i + 1].Angulo : _raios[0].Angulo + Mathf.Tau;
			MaiorSetorGraus = MathF.Max(MaiorSetorGraus, Mathf.RadToDeg(b - a));
		}
	}

	/// <summary>
	/// Monta e entrega a malha da sombra.
	///
	/// O que se desenha e o COMPLEMENTO do que se ve: pra cada par de raios vizinhos, o
	/// quadrilatero que vai dos dois pontos de parada pra fora, ate passar da tela. Onde os
	/// dois raios morreram na borda do retangulo, esse quadrilatero cai todo fora da tela e
	/// nao pinta nada -- que e o certo, ali nao ha sombra.
	///
	/// Uma chamada so de RenderingServer com os arrays prontos, e nao um vertice por vez: o
	/// caminho por vertice do ImmediateMesh custa duas passagens C#->motor CADA, e no pior
	/// caso (uma cidade cheia de quinas) isso sozinho passaria de mil chamadas por quadro.
	/// Os buffers sao reaproveitados entre quadros pela mesma razao.
	/// </summary>
	private void Montar(Vector2 p, Rect2 tela)
	{
		int n = _raios.Count;

		// alem do canto mais distante da tela: o que passar disso o monitor corta
		float longe = 0f;
		foreach (Vector2 c in new[] { tela.Position, new Vector2(tela.End.X, tela.Position.Y), tela.End, new Vector2(tela.Position.X, tela.End.Y) })
			longe = MathF.Max(longe, p.DistanceTo(c));
		longe += 64f;

		// ============================ POR QUE ESTA SOMBRA NAO TEM FILTRO ============================
		// O dono pediu que a qualidade grafica mudasse o filtro dela ("no godot shadow tem como mudar
		// o filter"). Ele esta certo sobre o Godot -- `Light2D` tem PCF5 e PCF13 --, mas o filtro e
		// do SHADOW MAP de uma luz, e isto aqui nao e uma luz: e uma malha de triangulos que nos
		// desenhamos (ver o cabecalho da classe). Nao ha filtro pra ligar.
		//
		// TENTEI SUAVIZAR NA MALHA e o resultado foi pior: um degrade de alfa a partir da borda da
		// visao cai EM CIMA do muro (a borda da visao, junto de uma parede, e a face dela), e o que
		// aparecia era um vao claro no fim do tile -- exatamente o que o dono fotografou. A rampa
		// radial nao serve porque a suavizacao que o olho pede e LATERAL, na silhueta, e nesta
		// topologia (quads radiais) a silhueta nao e uma aresta: e a descontinuidade entre dois
		// raios vizinhos.
		//
		// Fica o corte seco, que e correto por construcao, e a qualidade grafica segue mexendo no
		// que ela consegue mexer de verdade: os raios do leque e o PCF das luzes de fogueira.
		// ===========================================================================================
		if (_pontos.Length != n * 2)
		{
			_pontos = new Vector2[n * 2];
			_cores = new Color[n * 2];
			for (int i = 0; i < _cores.Length; i++) _cores[i] = Sombra;
			_indices = new int[n * 6];
		}

		for (int i = 0; i < n; i++)
		{
			Raio r = _raios[i];
			_pontos[i * 2] = p + r.Dir * r.Dist;      // onde a vista termina
			_pontos[i * 2 + 1] = p + r.Dir * longe;   // e o mesmo rumo, fora da tela
		}

		for (int i = 0; i < n; i++)
		{
			int j = (i + 1) % n;
			int k = i * 6;
			_indices[k] = i * 2; _indices[k + 1] = j * 2; _indices[k + 2] = j * 2 + 1;
			_indices[k + 3] = i * 2; _indices[k + 4] = j * 2 + 1; _indices[k + 5] = i * 2 + 1;
		}

		RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), _indices, _pontos, _cores);
	}
}
