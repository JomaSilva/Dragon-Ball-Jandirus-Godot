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
		set { _mapa = value; Invalidar(); }
	}

	/// <summary>
	/// O mapa de COLISAO da mesma zona. Usado so pelo guarda de sanidade do <see cref="_Draw"/> --
	/// ver o comentario la. Nulo = sem guarda, que e melhor que um guarda que dispara errado.
	/// </summary>
	public ZoneCollision? Colisao;

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
	/// </summary>
	private const string CodigoDosFuros = """
		shader_type canvas_item;
		render_mode unshaded, blend_mix;
		uniform sampler2D mascara : filter_nearest, repeat_disable;
		uniform vec2 origem;    // celula do canto da mascara
		uniform vec2 tamanho;   // celulas cobertas
		varying vec2 mundo;
		void vertex() { mundo = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy; }
		void fragment() {
			vec2 c = floor(mundo / 32.0) - origem;
			if (c.x >= 0.0 && c.y >= 0.0 && c.x < tamanho.x && c.y < tamanho.y
				&& texture(mascara, (c + 0.5) / tamanho).r > 0.5) discard;
		}
		""";

	private readonly ShaderMaterial _tinta = new() { Shader = new Shader { Code = CodigoDosFuros } };
	private byte[] _furos = [];
	private int _furosLarg, _furosAlt, _furosX0, _furosY0;
	private ImageTexture? _mascara;

	/// <summary>Quantas paredes estao claras no ultimo leque. Diagnostico.</summary>
	public int Furos { get; private set; }

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

	public override void _Process(double delta)
	{
		if (Alvo == null || Mapa == null) { Visible = false; return; }
		Visible = true;

		// cego: nao ha leque pra recalcular, e a cada quadro a cortina tem que continuar de pe
		if (Cego) { QueueRedraw(); return; }

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
		if (Cego)
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
		if (Colisao?.BlockedAt(new Vec2(p.X, p.Y)) == true) return;

		Recalcular(p, tela);
		if (_raios.Count < 3) return;
		Montar(p, tela);
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
		ParadasForaDeFace = 0;
		Mirar(olho, tela);
		Ordenar();
		MedirSetores();
		Furar(tela);
	}

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
		return x >= 0 && y >= 0 && x < _furosLarg && y < _furosAlt && _furos[y * _furosLarg + x] != 0;
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
					_furos[(cy - _furosY0) * _furosLarg + (cx - _furosX0)] = 255;
					Furos++;
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
