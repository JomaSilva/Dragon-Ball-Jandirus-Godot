using Godot;

namespace Jandirus.Client;

/// <summary>
/// A NUVEM DE GALAXIA QUE ENVOLVE QUEM ESTA EM ULTRA INSTINTO.
///
/// Irmao do <see cref="RaiosDaForma"/> em tudo: um node que existe sempre, nasce desligado, e que o
/// <see cref="Transformacao"/> (com cena) e o `World.AcenderFormaNoCorpo` (sem cena) acendem pelo
/// mesmo par de chamadas. Quem decide SE ele acende e o Core --
/// <see cref="Jandirus.Core.Forms.Catalogo.TemNebulosa"/>, derivado da linha da forma --, e o
/// cabecalho de la explica por que a paleta ficou no shader em vez de vir junto.
///
/// ============================ ELE E O PEDACO DA REFERENCIA QUE NAO TEM ARTE ============================
/// A imagem que o dono mandou tem cinco camadas, e tres delas ja tinham dono antes deste arquivo:
///
///   * o AZUL-LAVANDA encostado na pele e as MICROPARTICULAS brancas subindo sao as duas folhas
///     coladas que o DM veste (`UltraInstinct.dm:479-480`) -- passam pelo canal de
///     <see cref="Jandirus.Core.Forms.Colada"/>;
///   * o FIO PRATEADO na silhueta e o contorno de forma que ja existe, escrito no
///     `Personagem.gdshader` pelo uniform `aura` (a cor sai de
///     `Catalogo.CorDoContorno` -> `BrancoDoInstinto`, `f0f6ff`).
///
/// O que sobrava eram as camadas 1 e 2: o fundo indigo quase preto e as volutas violeta-azuladas
/// com filamento. Elas ocupam TRES corpos de largura na referencia -- uma celula de 32 px nao
/// envolve ninguem --, entao nao ha .dmi que sirva e o desenho e procedural. Ver o cabecalho do
/// `NebulosaDaForma.gdshader`.
/// ====================================================================================================
/// </summary>
public partial class NebulosaDaForma : Node2D
{
	private const string CaminhoDoShader = "res://Assets/Shaders/NebulosaDaForma.gdshader";

	/// <summary>
	/// A FOLGA EM VOLTA DA SILHUETA, em pixels de mundo. Palavra do dono, literal:
	///
	///   "a aura do ultra instinto, e do perfected ultra instinct estao mt grandes, era pra estar
	///    perto do corpo, no MAXIMO 5 pixels de distancia do corpo"
	///
	/// ============================ O QUE ELA SUBSTITUIU ============================
	/// Aqui havia `Lado = 96` -- um quad de tres tiles, lido da referencia "em corpos". Ele nao era
	/// arbitrario, era ERRADO por leitura: a foto no jogo mostrou um halo redondo bem maior que o
	/// boneco, com o personagem inteiro dentro dele. Medido naquele quad, contra a silhueta util de
	/// 16 px, a nuvem sobrava **26 px de lado, 19 acima da cabeca e 29 abaixo dos pes** -- entre 3,7x
	/// e 6x o que o dono pediu, e assimetrica de quebra (o `centro.y = 0.55` do shader empurrava a
	/// elipse pra baixo enquanto os pes ficam em +16).
	///
	/// Agora nao ha lado nenhum escrito: ele SAI da silhueta medida mais esta folga (ver
	/// <see cref="Modelar"/>). O corpo de 32 produz um quad de 42; o Oozaru, de folha 96,
	/// produziria um de ~106 -- com os mesmos 5 px em volta, sem ninguem recalcular nada. E a mesma
	/// escolha do <see cref="SpriteDeAura.AncoraPara"/>, que e uma REGRA (`LinhaDosPes - altura/2`) e
	/// nao um `-16` cravado que so estaria certo pra um tamanho de folha.
	/// ============================================================================
	/// </summary>
	private const float Folga = 5f;

	/// <summary>
	/// A LARGURA DA SILHUETA QUANDO ELA NAO PODE SER MEDIDA, em fracao da largura do quadro.
	///
	/// So vale enquanto o boneco ainda nao tem folha (o node nasce antes de `VestirCorpoInteiro`) ou
	/// se a textura do quadro nao devolver imagem. Metade e o numero que este projeto ja assumia: o
	/// cabecalho do shader dizia "a silhueta em pe ocupa ~16 de largura util" num quadro de 32, e a
	/// medida do alfa confirma. Como fallback ele erra pra ESTREITO, que e o lado seguro: a nuvem sai
	/// mais colada, nunca mais larga que o pedido.
	/// </summary>
	private const float FracaoDaSilhuetaSemMedida = 0.5f;

	/// <summary>
	/// POR CIMA DO CORPO, e o `ZIndex` NAO e quem decide isso -- e a ORDEM DE IRMAO. Por isso ele
	/// continua ZERO.
	///
	/// A regra ja esta escrita no `World.AoEntrar` pra <see cref="Aura"/>: com todos os desenhos do
	/// corpo em z 0, quem desenha por cima e o irmao mais NOVO. Este node entra DEPOIS do
	/// `CharacterVisual` -- entao a pilha do corpo fica
	/// aura &lt; carga &lt; boneco &lt; nebulosa &lt; raios (z 6).
	///
	/// ============================ ERA ATRAS, E O DONO PEDIU NA FRENTE ============================
	/// "o efeito deveria ficar sobre o corpo e nao atras". A nuvem nascia como o irmao MAIS VELHO,
	/// justamente pra ficar por tras -- e a primeira versao tinha RECUSADO a passada por cima com um
	/// argumento que continua valendo: nuvem desenhada sobre o sprite tapa o rosto e a roupa.
	///
	/// O que muda e a resposta. Nao se volta pra tras: baixa-se o ALFA por cima da silhueta (o
	/// `veu_no_corpo` do shader, ~0,2 em cima do rosto), que e o que a referencia mostra -- da pra ver
	/// o rosto do Goku ATRAVES do efeito. O anel colado no corpo continua cheio.
	/// ====================================================================================
	///
	/// ============================ POR QUE NAO UM Z ALTO ============================
	/// Z vence Y-sort, e e isso que o torna perigoso aqui: um z positivo tiraria a nuvem da ordem do
	/// mundo inteiro e ela passaria a desenhar por CIMA das arvores e paredes do cenario -- que e o
	/// tombo que o `_cabelo` ja levou neste projeto. O `SpriteDeAura` pagou o mesmo erro pelo outro
	/// lado (era `ZIndex = -4` e a aura sumia debaixo do chao; o conserto foi voltar pra 0).
	///
	/// Com z 0 a nuvem herda o Y-sort do corpo -- ela anda com ele na profundidade da cena, que e o
	/// certo: um lutador atras de uma arvore tem a aura atras da arvore tambem.
	/// ==================================================================================
	/// </summary>
	private const int Camada = 0;

	/// <summary>O lado do quad em pixels de mundo, ja derivado da caixa dos quadros. Ver <see cref="Modelar"/>.</summary>
	private int _lado = 42;

	/// <summary>
	/// O CANTO SUPERIOR ESQUERDO do quad no espaco do personagem, em pixel INTEIRO.
	///
	/// Inteiro por obrigacao e nao por gosto: este projeto liga `snap_2d_vertices_to_pixel`
	/// (`project.godot:42`) e meio pixel aqui sai multiplicado pela escala do quad -- e o tombo do
	/// `Centered` que este arquivo ja pagou uma vez (ver o `_Ready`). E ele e tambem a ORIGEM do campo
	/// de distancia: o texel (0,0) da textura E este pixel do personagem, e um deslocamento fracionario
	/// entre os dois faria a mascara nascer meio pixel fora do boneco -- justamente o erro que uma
	/// mascara colada na silhueta denuncia e uma elipse folgada escondia.
	/// </summary>
	private Vector2I _canto;

	private Sprite2D _quad = null!;
	private ShaderMaterial _tinta = null!;

	/// <summary>
	/// O BONECO, achado uma vez. E irmao deste node (`World.AoEntrar` cria os dois lado a lado), e
	/// pode ainda nao existir quando este `_Ready` roda -- dai o `??=` a cada consulta em vez de uma
	/// captura no nascimento.
	/// </summary>
	private CharacterVisual? _visual;

	/// <summary>
	/// AS CAMADAS DE SILHUETA no quadro que cada uma esta desenhando AGORA, reusada quadro a quadro.
	/// Ela e recheada pelo <see cref="CharacterVisual.SilhuetaDesenhada"/>; a lista e campo (e nao
	/// retorno) porque ela e lida a cada quadro pra saber se a pose mudou, e alocar seis tuplas por
	/// quadro por corpo pra quase sempre concluir "nao mudou" seria lixo puro.
	/// </summary>
	private readonly List<(Texture2D Quadro, Vector2 Centro)> _camadas = [];

	/// <summary>A chave da pose que o <see cref="_campo"/> desenha. Ver <see cref="ChaveDaPose"/>.</summary>
	private long _pose = -1;

	private ImageTexture? _campo;

	/// <summary>
	/// A IMAGEM do campo de distancia, guardada ao lado da textura. So as bancadas leem -- e elas
	/// precisam: `ImageTexture.GetImage()` faz leitura de volta da GPU e devolve o que o motor
	/// guardou, nao o que este arquivo construiu. Guardar a imagem e o que separa "o que eu quis
	/// mandar" de "o que a textura tem".
	/// </summary>
	private Image? _imagemDoCampo;

	/// <summary>
	/// A NUVEM ESTA LIGADA? Lida do UNIFORM e nao de um campo guardado -- mesmo motivo do
	/// <see cref="RaiosDaForma.CorDeTeste"/>: escrever num uniform que nao existe e SILENCIOSO no
	/// Godot, entao um campo responderia o que <see cref="Definir"/> pediu e nao o que o shader
	/// recebeu. Pra bancada.
	/// </summary>
	public float ForcaDeTeste => (float)_tinta.GetShaderParameter("forca");

	/// <inheritdoc cref="Carga"/>
	public float CargaDeTeste => (float)_tinta.GetShaderParameter("carga");

	/// <summary>
	/// UMA DAS QUATRO CORES, LIDA DO MATERIAL. Pra bancada, e pelo mesmo motivo da
	/// <see cref="ForcaDeTeste"/>, que aqui pesa mais: a paleta e o UNICO efeito de todo o pedido do
	/// Ultra Ego, entao "escreveu a paleta certa" e "a paleta chegou no shader" sao a mesma pergunta
	/// duas vezes se a bancada so ler o Core. Um nome de uniform trocado (`cor_dos_pontos` ->
	/// `cor_do_ponto`, digamos) e silencioso no Godot: a escrita vai pro vazio e a nuvem sai indigo.
	///
	/// Devolve NULO quando o uniform nao existe, e isso reprova em vez de estourar -- ler um Variant
	/// nulo como <c>Color</c> daria PRETO, que e uma cor plausivel e passaria por uma comparacao
	/// distraida.
	/// </summary>
	public Color? CorDeTeste(string uniform)
	{
		Variant v = _tinta.GetShaderParameter(uniform);
		return v.VariantType == Variant.Type.Nil ? null : v.AsColor();
	}

	/// <summary>O lado do quad em pixels de mundo, ja com a escala aplicada. Pra bancada.</summary>
	public float LadoDeTeste => _quad.Scale.X;

	/// <summary>
	/// A CHAVE DA POSE que o campo desenha agora. Pra bancada, e ela existe pra um requisito que nenhuma
	/// medida de tamanho alcanca: **a mascara acompanha a animacao**.
	///
	/// Ler a folga ou o lado nao responde isso -- os dois ficam iguais o tempo todo, de proposito (o quad
	/// e estavel e a folga e sempre 5). O que muda a cada passo do boneco e o DESENHO dentro do quad, e a
	/// chave e a unica coisa que distingue um quadro de caminhada do seguinte sem comparar bitmaps.
	/// </summary>
	public long PoseDeTeste => _pose;

	/// <summary>
	/// O `ZIndex` DO QUE DESENHA, lido do <c>Sprite2D</c> e nao do <see cref="Camada"/>. Pra bancada.
	///
	/// Ela e a OUTRA PONTA do pedido do dono: a nuvem por cima do corpo (que e ordem de irmao) e ainda
	/// assim ATRAS do cenario (que e este zero -- z vence Y-sort). Ler o const responderia o que eu quis;
	/// ler o node responde o que esta desenhando, que e o que a arvore pode ter mudado desde o `_Ready`.
	/// </summary>
	public int CamadaDeTeste => _quad.ZIndex;

	/// <summary>
	/// A FOLGA DESENHADA, em pixels de mundo: a MAIOR distancia entre um pixel de nuvem e o boneco. Pra
	/// bancada, e ela e a unica linha que responde a pergunta que o dono fez -- "no MAXIMO 5 pixels de
	/// distancia do corpo".
	///
	/// ============================ ELA DEIXOU DE SER UM PAR DE EIXOS, E ISSO E O PEDIDO ============================
	/// Ela era `(lateral, vertical)`, e com elipse tinha que ser: elipse tem dois raios, e a folga dela
	/// depende da direcao (5 px nos quatro pontos onde ela encosta no boneco, e mais nas diagonais). Um
	/// numero por eixo era o jeito de mostrar as duas.
	///
	/// A distancia PERPENDICULAR nao tem eixo. E a primeira versao desta propriedade tentou fingir que
	/// tinha -- ela andava pra direita na linha do meio e contava texels ate sair da nuvem --, e a bancada
	/// pegou na primeira rodada: **8 px de lado contra 5 de cima**, num campo correto. A causa nao e o
	/// campo, e o instrumento: na linha da cintura o boneco e estreito, mas o BRACO fica tres linhas acima
	/// e mais pra fora -- os texels a direita da cintura estao a 5 px do BRACO, na diagonal, e o raio
	/// horizontal os conta como se estivessem a 8 do tronco. Andar em linha reta mede a hipotenusa e chama
	/// de cateto.
	///
	/// ============================ E A CONTA E REFEITA POR FORCA BRUTA, DE PROPOSITO ============================
	/// A resposta certa e a maior distancia EUCLIDIANA entre um pixel que ainda tem nuvem e o pixel de
	/// corpo mais proximo dele. Perguntar isso ao proprio campo seria circular (o campo E essa distancia,
	/// por construcao -- ele responderia "5" ate com a transformada quebrada). Entao aqui ela e recalculada
	/// do zero, por um algoritmo DIFERENTE: cada texel de nuvem contra todos os de corpo, distancia exata.
	///
	/// E e isso que da valor a linha: se o chanfro estiver errado, se a conversao de unidade se perder, ou
	/// se o campo nascer meio pixel fora do boneco, este numero discorda daquele. Sao ~600 texels de nuvem
	/// contra ~350 de corpo -- 200 mil contas numa propriedade que so a bancada le.
	/// ==========================================================================================================
	/// </summary>
	public float FolgaDeTeste
	{
		get
		{
			if (_imagemDoCampo is not { } img) return 0f;

			int n = img.GetWidth();
			var corpo = new List<Vector2I>();
			var nuvem = new List<Vector2I>();

			for (int y = 0; y < n; y++)
				for (int x = 0; x < n; x++)
				{
					float r = img.GetPixel(x, y).R;
					if (r <= 0.5f) corpo.Add(new Vector2I(x, y));
					else if (r < 0.999f) nuvem.Add(new Vector2I(x, y));
				}

			if (corpo.Count == 0 || nuvem.Count == 0) return 0f;

			float maior = 0f;
			foreach (Vector2I p in nuvem)
			{
				int perto = int.MaxValue;
				foreach (Vector2I c in corpo)
				{
					int dx = p.X - c.X, dy = p.Y - c.Y, d2 = dx * dx + dy * dy;
					if (d2 < perto) perto = d2;
				}
				maior = Mathf.Max(maior, Mathf.Sqrt(perto));
			}

			// MENOS MEIO PIXEL, pela mesma razao do <see cref="MeioPixel"/>: a conta acima e de CENTRO a
			// centro, e a pele mora na fronteira entre o ultimo pixel de corpo e o primeiro de fora.
			return Mathf.Max(maior - MeioPixel, 0f);
		}
	}

	/// <summary>
	/// A SILHUETA QUE O CAMPO GUARDA, em pixels de mundo -- (meia-largura, meia-altura). Pra bancada, e
	/// ela e o que permite conferir que o tamanho da nuvem foi DERIVADO do boneco: a bancada remede a
	/// caixa do alfa da folha por conta propria e cobra que esta caixa a contenha. Um lado cravado no
	/// `.cs` reprova ali.
	///
	/// E ela e MAIOR que a caixa da folha do corpo de proposito: o que entra aqui e a uniao das camadas
	/// VIVAS -- cabelo espetado, roupa e rabo inclusos --, que e o personagem que o dono ve na captura
	/// dele. A folha do corpo sozinha nao tem nem topete nem cauda.
	/// </summary>
	public Vector2 SilhuetaDeTeste
	{
		get
		{
			if (_imagemDoCampo is not { } img) return Vector2.Zero;
			(Vector2I min, Vector2I max) = CaixaDoAlfaDoCampo(img);
			return max.X < min.X ? Vector2.Zero
								 : new Vector2((max.X - min.X + 1) * 0.5f, (max.Y - min.Y + 1) * 0.5f);
		}
	}

	/// <summary>
	/// QUANTO DA CAIXA DA SILHUETA ESTA MESMO PREENCHIDO, de 0 a 1. Pra bancada, e ela existe pra UMA
	/// pergunta -- **isto ainda e um circulo?**.
	///
	/// ============================ POR QUE ESTA E A LINHA QUE COBRE O PEDIDO DO DONO ============================
	/// A queixa nao foi de tamanho, foi de FORMA: *"ela deveria n ser um circulo e sim contornar o
	/// corpo"*. Todas as outras medidas desta classe (lado, folga, silhueta) davam VERDE com a elipse --
	/// ela tinha o tamanho certo e a folga certa. O que separa as duas geometrias e o quanto elas
	/// enchem a propria caixa: uma elipse enche `pi/4` = 78,5% dela, sempre, por definicao; um lutador
	/// em pe enche ~50% (o vao entre as pernas, o ar entre o braco e o tronco, os cantos vazios).
	///
	/// E por isso o numero e a fracao e nao a area: area cresce com a folha (o Oozaru enche 9 mil px) e
	/// nao distingue forma nenhuma.
	/// ======================================================================================================
	/// </summary>
	public float PreenchimentoDeTeste
	{
		get
		{
			if (_imagemDoCampo is not { } img) return float.NaN;
			(Vector2I min, Vector2I max) = CaixaDoAlfaDoCampo(img);
			if (max.X < min.X) return float.NaN;

			int dentro = 0;
			for (int y = min.Y; y <= max.Y; y++)
				for (int x = min.X; x <= max.X; x++)
					if (img.GetPixel(x, y).R <= 0.5f) dentro++;

			return dentro / (float)((max.X - min.X + 1) * (max.Y - min.Y + 1));
		}
	}

	/// <summary>A caixa dos texels que estao DENTRO do boneco (s &lt;= 0, ou seja meio tom pra baixo).</summary>
	private static (Vector2I Min, Vector2I Max) CaixaDoAlfaDoCampo(Image img)
	{
		var min = new Vector2I(int.MaxValue, int.MaxValue);
		var max = new Vector2I(int.MinValue, int.MinValue);
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
			{
				if (img.GetPixel(x, y).R > 0.5f) continue;
				if (x < min.X) min.X = x;
				if (y < min.Y) min.Y = y;
				if (x > max.X) max.X = x;
				if (y > max.Y) max.Y = y;
			}
		return (min, max);
	}

	/// <summary>
	/// Onde o quad esta DE VERDADE no mundo. Pra bancada, e ela nasceu de uma foto: a nuvem apareceu
	/// desencostada do corpo e nao havia como saber, olhando, se quem estava fora do lugar era o node,
	/// o quad ou a mascara do shader. Esta linha separa os tres.
	/// </summary>
	/// <summary>
	/// O CENTRO do quad no mundo -- e centro, nao `GlobalPosition`, desde que a ancora foi pro canto
	/// (ver `_Ready`). Ler o `GlobalPosition` aqui devolveria o canto superior esquerdo e a bancada
	/// acusaria "o quad esta 48 px fora do corpo" justamente depois do conserto que o centrou.
	///
	/// Sai da transformada aplicada ao meio do retangulo local (0..1), entao acompanha escala e
	/// rotacao sozinha em vez de somar meio lado na mao.
	/// </summary>
	public Vector2 PosicaoDoQuadDeTeste => _quad.GetGlobalTransform() * new Vector2(0.5f, 0.5f);

	/// <summary>
	/// O RETANGULO DO QUAD EM PIXEL DE TELA, ja com camera e zoom aplicados. Pra bancada, e ela
	/// nasceu de uma conta minha que nao fechava: eu deduzia esse retangulo multiplicando o lado pelo
	/// zoom que eu ACHAVA que estava valendo, e a foto nao batia com a deducao de jeito nenhum.
	/// Perguntar pra a transformada e a unica resposta que nao depende do que eu acho.
	/// </summary>
	public Rect2 RetanguloNaTelaDeTeste
	{
		get
		{
			Transform2D t = _quad.GetGlobalTransformWithCanvas();
			// O retangulo local vai de 0 a 1 (ancora no canto -- ver `_Ready`), entao o meio dele e
			// (0,5, 0,5) passado pela transformada. Esta conta ASSUMIA o quad centrado e por isso
			// respondia "192x192 com centro no corpo" enquanto a foto mostrava a nuvem 51 px fora --
			// era a bancada confirmando a suposicao de quem a escreveu em vez de medir o desenho.
			Vector2 meio = t * new Vector2(0.5f, 0.5f);
			Vector2 meiaExtensao = (t.X.Abs() + t.Y.Abs()) * 0.5f;
			return new Rect2(meio - meiaExtensao, meiaExtensao * 2f);
		}
	}

	/// <summary>
	/// MODO DIAGNOSTICO: pinta a mascara CHAPADA, sem rampa de alfa e sem particula. Nao e enfeite de
	/// bancada -- e o unico jeito de a foto responder "onde este quad esta desenhando?", que foi
	/// justamente o que eu nao consegui responder olhando: sobre grama, a nuvem de verdade muda a cor
	/// media do anel em 2 unidades de 255, e um efeito assim nao tem contorno pra se localizar.
	///
	/// Ele escreve UNIFORMS e nao troca o shader: o que se ve chapado e exatamente a mesma mascara, no
	/// mesmo lugar, que o modo normal desenha translucida.
	/// </summary>
	public void DiagnosticoChapado(bool ligado)
	{
		Afinar("opacidade", ligado, 1.0f);
		Afinar("atenua_escuro", ligado, 1.0f);
		Afinar("curva_alfa", ligado, 0.001f);
		Afinar("pontos_brilho", ligado, 0.0f);

		// O VEU ENTROU NA LISTA QUANDO A MASCARA VIROU SILHUETA, e sem ele o modo chapado deixou de
		// ser chapado: `veu_no_corpo` derruba o alfa pra 0,22 EM CIMA DO BONECO, e com a mascara
		// colada no contorno isso e mais da metade do desenho. A foto sairia com o miolo palido e a
		// bancada leria "a nuvem nao cobre o corpo" -- lendo o veu, que e arte, como se fosse
		// geometria. Chapado tem que pintar a mascara e nada mais.
		Afinar("veu_no_corpo", ligado, 1.0f);
	}

	/// <summary>
	/// Escreve um uniform enquanto o diagnostico esta ligado e o DEVOLVE AO PADRAO DO SHADER quando
	/// sai -- passar `default` remove a sobrescrita, e a paleta volta a valer a do arquivo.
	///
	/// ============================ POR QUE NAO SE ESCREVE O VALOR DE VOLTA ============================
	/// A primeira versao destes metodos repunha os padroes a mao ("opacidade de volta pra 0,85"), e
	/// isso e uma copia dos numeros do `.gdshader` morando num `.cs`. Na recalibragem seguinte os
	/// padroes mudaram -- `opacidade` foi a 0,95, `curva_alfa` a 0,85 -- e estas linhas teriam
	/// silenciosamente REBAIXADO a nuvem de volta pros valores velhos toda vez que a bancada saisse do
	/// modo diagnostico. O jogo ficaria certo, e a foto da bancada (a unica que alguem olha) sairia
	/// com a arte antiga.
	///
	/// Pior ainda: o dono afina esses numeros editando o `.gdshader` SEM recompilar (e a propriedade
	/// que a bancada do `--diagforma` protege de proposito). Um padrao copiado aqui desfaria o ajuste
	/// dele sem aviso.
	/// ============================================================================================
	/// </summary>
	private void Afinar(string uniform, bool ligado, float valor)
	{
		if (ligado) _tinta.SetShaderParameter(uniform, valor);
		else _tinta.SetShaderParameter(uniform, default);
	}

	/// <summary>
	/// MODO MOLDURA: pinta o QUAD INTEIRO, mascara e tudo. Serve pra uma pergunta so, e ela apareceu
	/// quando a elipse chapada saiu fora da caixa que o C# dizia que o quad tinha: "onde este
	/// retangulo esta, afinal?".
	///
	/// Uma mascara cheia em cada pixel do quad faz a foto mostrar a borda do RETANGULO -- e comparar essa
	/// borda com a silhueta do modo chapado diz, sem conta nenhuma, se o problema e o quad estar fora do
	/// lugar ou a mascara estar fora do quad.
	/// </summary>
	public void DiagnosticoMoldura(bool ligado)
	{
		DiagnosticoChapado(ligado);

		// ============================ E AQUI A MOLDURA FICOU MAIS SIMPLES DO QUE ERA ============================
		// Antes eram DUAS elipses infladas a mao (`raio` e `raio_do_corpo` empurrados pra alem do quad),
		// e o comentario de la avisava que sair do modo NAO podia devolver o padrao do shader -- porque
		// aqueles dois uniforms nao eram arte, eram a medida deste corpo.
		//
		// Com o campo de distancia sobra UM canal, e "mascara cheia em todo lugar" e um campo de UM
		// TEXEL valendo zero (`s = -1`, ou seja: este pixel esta no fundo do boneco). Nao ha o que
		// inflar e nao ha dois numeros pra manter em par. Sair do modo REMODELA -- e remodelar reconstroi
		// o campo do corpo de verdade, nao repoe um padrao copiado.
		// ==================================================================================================
		// ============================ E O MODO PRECISA DE TRANCA, PORQUE A MASCARA ANDA ============================
		// A elipse ficava parada, entao inflar os uniforms bastava. O campo e RECONSTRUIDO quando a pose
		// muda (ver `_Process`), e sem esta tranca acontecem os dois estragos opostos:
		//
		//   * DENTRO do modo, um passo do boneco reescreveria o `campo_do_corpo` com o campo de verdade e
		//     a foto da moldura sairia com a silhueta -- ou seja, a foto que existe pra localizar o
		//     RETANGULO mostraria justamente o que ela nao esta medindo;
		//   * SAINDO do modo, `Modelar()` sozinho nao devolve nada: ele so escreve o uniform quando a
		//     CHAVE muda, e a chave nao mudou -- o corpo esta parado. A nuvem ficaria desenhando o quad
		//     cheio pelo resto da rodada, e as fotos seguintes seriam de um efeito que nao existe.
		//
		// Zerar a chave e o que forca a reescrita: e a mesma linha que o `Modelar` usa quando o quad anda.
		// =====================================================================================================
		_moldura = ligado;
		if (ligado) _tinta.SetShaderParameter("campo_do_corpo", TexelUnico(0f));
		else
		{
			_pose = -1;
			Modelar();
		}
	}

	/// <inheritdoc cref="DiagnosticoMoldura"/>
	private bool _moldura;

	/// <summary>Uma textura de 1x1 com este valor em R -- a moldura do diagnostico. Ver acima.</summary>
	private static ImageTexture TexelUnico(float valor)
	{
		Image img = Image.CreateEmpty(1, 1, false, Image.Format.R8);
		img.SetPixel(0, 0, new Color(valor, valor, valor));
		return ImageTexture.CreateFromImage(img);
	}

	/// <summary>
	/// ESCREVE UM UNIFORM QUALQUER; `null` DEVOLVE O PADRAO DO ARQUIVO. Pra bancada, e o publico do
	/// <see cref="Afinar"/> -- as mesmas duas linhas, com o mesmo cuidado de nunca copiar um padrao do
	/// `.gdshader` pra dentro de um `.cs` (o bloco do `Afinar` explica por que isso custou caro).
	///
	/// ============================ POR QUE A BANCADA PRECISA DISTO ============================
	/// "A densidade das microparticulas e AFINAVEL" e um requisito que nao da pra provar lendo o
	/// arquivo: `code.Contains(" pontos_densidade ")` prova que a PALAVRA existe, e nao que girar o
	/// botao muda alguma coisa na tela. Um uniform declarado e nunca lido passaria nessa leitura --
	/// que e a mesma familia de defeito dos 35 atlas escritos e nunca importados.
	///
	/// Com esta porta a bancada gira o botao pros DOIS extremos e conta os pixels brancos nas duas
	/// fotos. Se a contagem nao se mexer, o botao nao existe de verdade.
	/// ======================================================================================
	/// </summary>
	public void AfinarDeTeste(string uniform, float? valor)
	{
		if (valor is { } v) _tinta.SetShaderParameter(uniform, v);
		else _tinta.SetShaderParameter(uniform, default);
	}

	/// <summary>
	/// A ASSINATURA DO MATERIAL: TODO uniform do shader com o valor que ESTE corpo esta usando agora,
	/// em ordem alfabetica. Pra bancada, e ela existe pra uma pergunta so -- **os dois estagios do
	/// Ultra Instinto mostram o MESMO overlay?**
	///
	/// ============================ POR QUE NAO BASTA COMPARAR O `forca` ============================
	/// `forca == 1` nos dois estagios prova que a nuvem ACENDE nos dois, e nao que ela e a mesma. No
	/// dia em que alguem quiser "o Perfected um pouco mais azul" o caminho mais curto e escrever
	/// `cor_perto` (ou `opacidade`, ou `pontos_densidade`) do lado do cliente conforme a `Ordem` -- e
	/// nenhuma checagem de `forca` veria isso. A palavra do dono e do DM (`UltraInstinct.dm:479`, que
	/// veste os dois overlays sem olhar o estagio) e que os dois sao IGUAIS.
	///
	/// A assinatura pega qualquer ramificacao dessas, porque ela nao sabe quais uniforms existem: ela
	/// pergunta ao SHADER a lista e le todos. Um uniform novo entra na comparacao sozinho.
	///
	/// O `carga` FICA DENTRO, e isso e deliberado apesar de ele ser estado do instante: o roteiro captura
	/// os dois estagios com o C SOLTO (passos 11 e 18, e o 15 ja cobrou que ele voltou a zero), entao os
	/// dois valem zero por construcao. Deixa-lo entrar e o que pegaria alguem que resolvesse "o Perfected
	/// adensa mais ao carregar" -- que e exatamente a familia de ramificacao que esta assinatura existe
	/// pra proibir.
	///
	/// A `semente` fica de fora, e ela e a unica: ela e por CORPO de proposito (ver `_Ready`) --
	/// compara-la seria cobrar que dois lutadores desenhem a mesma voluta no mesmo quadro, que e
	/// exatamente o estroboscopio sincronizado que ela existe pra evitar.
	///
	/// ============================ E AGORA O `campo_do_corpo` TAMBEM ============================
	/// Ele e a segunda -- e a razao e a mesma da `semente` de cabeca pra baixo: ele nao e ARTE, e a
	/// GEOMETRIA da pose deste instante. Compara-lo cobraria que os dois estagios do Ultra Instinto
	/// tivessem sido fotografados no mesmo quadro de caminhada, o que nao e um requisito de ninguem --
	/// e a bancada reprovaria por um pe estar meio passo a frente.
	///
	/// O que o campo tem que provar (que ele cola no boneco, que a folga cabe nos 5 px, que ele nao e
	/// um circulo) e cobrado onde ele pode ser: <see cref="FolgaDeTeste"/>,
	/// <see cref="SilhuetaDeTeste"/> e <see cref="PreenchimentoDeTeste"/>, medidos no campo construido.
	/// ======================================================================================
	/// </summary>
	public string AssinaturaDeTeste()
	{
		var partes = new List<string>();
		foreach (Variant item in _tinta.Shader.GetShaderUniformList())
		{
			string nome = item.AsGodotDictionary()["name"].AsString();
			if (nome is "semente" or "campo_do_corpo") continue;
			partes.Add($"{nome}={_tinta.GetShaderParameter(nome)}");
		}
		partes.Sort(StringComparer.Ordinal);
		return string.Join(";", partes);
	}

	public override void _Ready()
	{
		_tinta = new ShaderMaterial { Shader = GD.Load<Shader>(CaminhoDoShader) };

		_quad = new Sprite2D
		{
			Name = "Quad",
			// ============================ UM PIXEL BRANCO ESTICADO ============================
			// O shader nao amostra `TEXTURE` uma vez sequer: a nuvem inteira e gerada. A textura
			// existe so pra o `Sprite2D` ter um QUAD com UV de 0 a 1 -- e 1x1 esticado e a forma
			// mais barata de conseguir isso (nao ocupa VRAM e nao passa por importacao, entao nao
			// pode repetir o tombo dos 35 atlas que estavam no disco e o Godot nunca importou).
			//
			// ============================ E O `Centered` NAO SERVE PRA UMA TEXTURA DE 1x1 ============================
			// Aqui estava o padrao (`Centered = true`) e a FOTO mostrou o estrago: a nuvem desenhava
			// inteira PRA BAIXO E PRA DIREITA do corpo, desencostada dele. Medido na foto do modo
			// chapado: a elipse saiu com o tamanho exato que devia (76x88 px de mundo, contra 77x90 de
			// projeto) e com o centro deslocado em +51,+47 -- ou seja, MEIO QUAD, e nao um erro de
			// mascara. O corpo ficava fora da propria aura.
			//
			// A causa e a soma de duas coisas certas. `Centered` desloca a arte em
			// `-tamanho_da_textura / 2`, e numa textura de 1x1 isso e MEIO PIXEL. E este projeto liga
			// `snap_2d_vertices_to_pixel` (project.godot:42), como todo jogo de pixel art liga. Meio
			// pixel nao sobrevive a um arredondamento pra pixel inteiro: o deslocamento some e o quad
			// passa a ocupar 0..+96 em vez de -48..+48. O erro nasce de meio pixel e chega na tela
			// multiplicado pela escala de 96, que e aplicada depois.
			//
			// O conserto nao e desligar o snap (ele e do jogo inteiro) nem inchar a textura pra 96x96
			// (36 KB de VRAM pra nao amostrar nem um texel). E parar de pedir o centro em TEXEL, que e
			// fracionario, e pedi-lo em PIXEL DE MUNDO: ancora no canto e o node em `-lado/2`, que e
			// INTEIRO (o `Modelar` arredonda de proposito) e atravessa qualquer arredondamento
			// intacto.
			// ================================================================================================
			Texture = QuadBranco(),
			Centered = false,
			Material = _tinta,
			ZIndex = Camada,
			Visible = false,
		};
		AddChild(_quad);

		// ============================ CADA CORPO COM A PROPRIA NUVEM ============================
		// `TIME` e global no shader. Sem semente, dois lutadores em Ultra Instinto lado a lado
		// desenhariam a MESMA voluta no mesmo quadro -- o estroboscopio sincronizado que o
		// `RaiosDaForma` inteiro existe pra evitar, do outro lado do mesmo problema.
		//
		// A semente sai do id da INSTANCIA e nao de um sorteio: e estavel enquanto o corpo viver
		// (a nuvem nao "pula" quando alguem troca de degrau) e nao gasta um gerador por corpo.
		_tinta.SetShaderParameter("semente", GetInstanceId() % 997 * 0.113f);

		// A GEOMETRIA JA SAI DAQUI COM VALOR, e nao so quando alguem acender: a bancada le
		// <see cref="LadoDeTeste"/> e <see cref="FolgaDeTeste"/> num corpo que nunca se transformou, e
		// um quad de escala zero devolveria "folga 0 px" -- um verde vazio. Aqui o boneco ainda nao tem
		// folha (este node nasce antes do `VestirCorpoInteiro`), entao vale o fallback; o
		// <see cref="Definir"/> remodela quando a forma acende.
		Modelar();

		// A CARGA NASCE ESCRITA, e nao herdada do padrao do `.gdshader`: a `CargaDeTeste` le do MATERIAL
		// (ver `ForcaDeTeste` pro porque), e um uniform que ninguem escreveu devolve Variant nulo. A
		// bancada leria zero por "nao ha valor" em vez de por "esta em repouso" -- duas coisas
		// diferentes com a mesma cara, que e como um sensor morto passa verde.
		_tinta.SetShaderParameter("carga", 0f);

		// APAGADO NAO PROCESSA. Ver o fim do `Definir`.
		SetProcess(false);
	}

	/// <summary>
	/// ============================ MASCARA DE POSE PARADA DESCOLA ANDANDO ============================
	/// A elipse podia ser medida UMA VEZ POR TRANSFORMACAO, e era: ela era grande, mole e centrada, e um
	/// braco a mais pra fora cabia na folga. Uma mascara colada no contorno nao perdoa isso -- na pose de
	/// soco o punho avanca ~6 px alem da caixa da pose parada, ou seja pra fora da nuvem INTEIRA, e o que
	/// se veria e o braco saindo do efeito a cada golpe. O mesmo vale pro voo (o corpo deita), pro nocaute
	/// (o desenho ja vem deitado) e pra qualquer troca de roupa no meio da forma.
	///
	/// Entao a pose e conferida POR QUADRO -- e conferir e barato de proposito: o
	/// <see cref="CharacterVisual.SilhuetaDesenhada"/> recolhe meia duzia de ponteiros de textura e seus
	/// deslocamentos numa lista que ja existe, e a chave deles e um `long`. RECONSTRUIR e que custa, e
	/// reconstruir so acontece quando a chave muda, que e a taxa da ANIMACAO (5 a 10 Hz) e nao a do
	/// render. E o mesmo racional do `CharacterVisual.AtualizarCaixa`, que reenvia a caixa do quadro pelo
	/// sinal de troca e nao a 60 Hz.
	///
	/// ============================ E O NODE COPIA A TRANSFORMADA DO BONECO ============================
	/// O `CharacterVisual` GIRA (90 graus por direcao no voo, e no arremesso -- ver `GirarPara`) e ENCOLHE
	/// (o tween do `Crescer`, quando alguem vira Oozaru). Esta nuvem e IRMA dele, nao filha, entao nao
	/// herda nada disso -- e com elipse ninguem reparava, porque um ovo girado continua um ovo. Silhueta
	/// em pe por cima de um corpo deitado seria o defeito mais visivel desta tarefa.
	///
	/// Copiar a `Transform` inteira resolve rotacao, escala e posicao numa linha e sem espelhar regra
	/// nenhuma: no dia em que o boneco ganhar outro efeito de transformada, a nuvem vai junto sozinha.
	/// ================================================================================================
	/// </summary>
	public override void _Process(double delta) => SeguirOBoneco();

	/// <inheritdoc cref="_Process"/>
	private void SeguirOBoneco()
	{
		if (Boneco() is { } vis && Transform != vis.Transform) Transform = vis.Transform;
		Modelar();
	}

	/// <summary>
	/// O BONECO, achado sob demanda. Ele e IRMAO deste node e pode nascer depois dele (a ordem do
	/// `World.AoEntrar` nao e contrato deste arquivo), entao nao da pra captura-lo no `_Ready` -- e
	/// capturar nulo pra sempre e como este projeto ja perdeu efeito antes.
	/// </summary>
	private CharacterVisual? Boneco()
	{
		if (_visual != null && IsInstanceValid(_visual)) return _visual;
		_visual = GetParent()?.GetNodeOrNull<CharacterVisual>("Visual");
		return _visual;
	}

	/// <summary>
	/// ============================ A NUVEM VESTE O CORPO, E NAO O ENQUADRA ============================
	/// O pedido do dono tem duas metades, e elas moram em lugares diferentes:
	///
	///   * "no MAXIMO 5 pixels de distancia do corpo" -- e uma DISTANCIA, e ela e o
	///     <see cref="ConstruirCampo"/>: a transformada de distancia mede os 5 px PERPENDICULARES a
	///     silhueta, iguais na quina do ombro e no vao entre as pernas;
	///   * "ela deveria n ser um circulo e sim CONTORNAR O CORPO" -- e a FORMA, e ela e a uniao do alfa
	///     das camadas vivas, tambem no `ConstruirCampo`.
	///
	/// O que sobra aqui e o QUAD -- a folha de papel em que o campo e desenhado --, e ele nao mede nada:
	/// e a caixa dos QUADROS das camadas mais a folga. Isso e de proposito, e e o achado que separa esta
	/// versao da anterior: **o tamanho do quad deixou de ser a fonte da folga**. Antes ele ERA a elipse,
	/// entao um quad medido na pose errada virava uma folga errada -- e por isso a versao velha media a
	/// pose PARADA (os quatro quadros de `walk_*`) pra o tamanho nao pular com a animacao. Agora a folga
	/// sai do campo, e o quad so precisa ser grande o bastante e ESTAVEL: caixa do quadro (32, ou 96 no
	/// Oozaru) mais 5 px em volta. Ele nao muda enquanto a folha nao mudar, entao nada pulsa; e o braco
	/// esticado do soco cabe nele porque a arte inteira cabe no proprio quadro dela, por construcao.
	///
	/// ============================ TRES CUIDADOS QUE PARECEM DETALHE E NAO SAO ============================
	///   1. TUDO INTEIRO. O `Position` do quad precisa atravessar o `snap_2d_vertices_to_pixel`
	///      (`project.godot:42`) sem virar meio pixel -- e meio pixel aqui sai multiplicado pela escala do
	///      quad, que e o tombo do `Centered` que este arquivo ja pagou uma vez. E o canto e tambem a
	///      ORIGEM do campo: o texel (0,0) E este pixel do personagem.
	///   2. O QUAD E CENTRADO NA CAIXA DOS QUADROS, e nao na origem do node. Os pes ficam em +16 num
	///      quadro de 32, mas o macaco desenha num de 96 ancorado pela base (o `CharacterVisual` poe
	///      `Offset.Y = (32 - 96)/2`): centrar no node deixaria a nuvem dele 32 px enterrada.
	///   3. SO SE ESCREVE O QUE MUDOU. Este metodo roda por quadro; reescrever `Scale`, `Position` e
	///      `lado_do_quad` sempre marcaria o `Sprite2D` como sujo 60 vezes por segundo pra repetir o
	///      mesmo numero.
	/// ================================================================================================
	/// </summary>
	private void Modelar()
	{
		if (Boneco() is { } vis) vis.SilhuetaDesenhada(_camadas);
		else _camadas.Clear();

		(Vector2I canto, int lado) = CaixaDoQuad();
		if (canto != _canto || lado != _lado)
		{
			_canto = canto;
			_lado = lado;
			_quad.Position = canto;
			_quad.Scale = new Vector2(lado, lado);

			// O LADO VAI PRO SHADER E NAO E DUPLICADO LA: as microparticulas tem tamanho em PIXELS
			// ("~1 px", da referencia) e o shader trabalha em UV. Cravado la seriam dois numeros iguais
			// em arquivos diferentes, e no dia em que o quad mudasse de tamanho os pontinhos encolheriam
			// sozinhos sem ninguem entender por que.
			_tinta.SetShaderParameter("lado_do_quad", (float)lado);

			// O CAMPO VELHO ESTA NA GRADE VELHA. Ele e indexado pelo canto e pelo lado; reaproveita-lo
			// depois de o quad andar poria a silhueta deslocada dentro do quad novo -- e o deslocamento
			// seria pequeno, ou seja invisivel na foto e errado na tela.
			_pose = -1;
		}

		long chave = ChaveDaPose();
		if (chave == _pose && _campo != null) return;

		_pose = chave;
		(_campo, _imagemDoCampo) = CampoDaPose(chave);

		// O MODO MOLDURA E DONO DO UNIFORM ENQUANTO DURA -- ver `DiagnosticoMoldura`. O campo continua
		// sendo construido e guardado (as medidas da bancada leem a IMAGEM, nao a tela), so nao vai pra o
		// shader.
		if (!_moldura) _tinta.SetShaderParameter("campo_do_corpo", _campo);
	}

	/// <summary>
	/// O QUADRO QUE VALE QUANDO NAO HA CAMADA NENHUMA -- o lado do tile deste jogo. So serve entre o
	/// nascimento deste node e o `Vestir` do boneco; ver <see cref="FracaoDaSilhuetaSemMedida"/>.
	/// </summary>
	private const float QuadroSemMedida = 32f;

	/// <inheritdoc cref="Modelar"/>
	private (Vector2I Canto, int Lado) CaixaDoQuad()
	{
		float x0 = -QuadroSemMedida * 0.5f, y0 = x0, x1 = -x0, y1 = -x0;
		bool achou = false;

		foreach ((Texture2D quadro, Vector2 centro) in _camadas)
		{
			Vector2 meia = quadro.GetSize() * 0.5f;
			if (!achou)
			{
				(x0, y0, x1, y1) =
					(centro.X - meia.X, centro.Y - meia.Y, centro.X + meia.X, centro.Y + meia.Y);
				achou = true;
				continue;
			}
			x0 = Mathf.Min(x0, centro.X - meia.X);
			y0 = Mathf.Min(y0, centro.Y - meia.Y);
			x1 = Mathf.Max(x1, centro.X + meia.X);
			y1 = Mathf.Max(y1, centro.Y + meia.Y);
		}

		// QUADRADO, e por razao de shader e nao de geometria: o `escala` (quantas volutas cabem no quad) e
		// o `lado_do_quad` (que traduz UV em pixel pra a particula) sao NUMEROS UNICOS. Um quad retangular
		// esticaria as volutas num eixo e deixaria os pontinhos ovais.
		int meio = Mathf.Max(Mathf.CeilToInt(Mathf.Max(x1 - x0, y1 - y0) * 0.5f + Folga), 2);
		return (new Vector2I(Mathf.RoundToInt((x0 + x1) * 0.5f) - meio,
							 Mathf.RoundToInt((y0 + y1) * 0.5f) - meio), 2 * meio);
	}

	/// <summary>
	/// A CHAVE DA POSE: o quad, mais o quadro que cada camada esta desenhando e onde ele cai. Duas poses
	/// com a mesma chave desenham a MESMA silhueta -- e por isso ela e tambem a chave do cache.
	///
	/// FNV-1a e nao `h * 31 + x`: o que entra sao inteiros pequenos e vizinhos (deslocamentos de -32 a
	/// +32), e a multiplicacao por 31 mal os espalha. Duas poses diferentes com a mesma chave nao dao
	/// erro -- dao a MASCARA ERRADA, que e o defeito que ninguem consegue ler numa foto.
	///
	/// O id de INSTANCIA da textura basta como identidade do quadro: as folhas vem do `ResourceLoader`,
	/// que as cacheia -- dois lutadores com a mesma roupa apontam pro mesmo `AtlasTexture`, e e isso que
	/// faz o cache do campo ser compartilhado entre corpos em vez de um por boneco.
	/// </summary>
	private long ChaveDaPose()
	{
		ulong h = 14695981039346656037UL;

		void Comer(long v)
		{
			for (int i = 0; i < 8; i++)
			{
				h ^= (byte)(v >> (i * 8));
				h *= 1099511628211UL;
			}
		}

		Comer(_lado);
		Comer(_canto.X);
		Comer(_canto.Y);
		foreach ((Texture2D quadro, Vector2 centro) in _camadas)
		{
			Comer((long)quadro.GetInstanceId());
			Comer(Mathf.RoundToInt(centro.X));
			Comer(Mathf.RoundToInt(centro.Y));
		}
		return (long)h;
	}

	/// <summary>
	/// ============================ O CACHE E POR POSE, E E ESTATICO ============================
	/// Construir o campo custa uma composicao de alfa mais duas varreduras de chanfro. Isso e barato pra
	/// uma pose e caro pra sessenta por segundo -- e a maioria das poses REPETE: o ciclo de caminhada tem
	/// 4 quadros por direcao, o de soco 4, e o jogo volta pra eles o dia inteiro.
	///
	/// ESTATICO porque a chave ja carrega tudo o que distingue um corpo do outro (o canto do quad, o lado
	/// e as texturas de cada camada). Dois Saiyajins com a mesma roupa no mesmo passo tem a MESMA
	/// silhueta -- guardar uma copia por boneco seria pagar o mesmo desenho duas vezes.
	///
	/// E O TETO EXISTE porque as chaves nao morrem sozinhas: guarda-roupa, forma e direcao multiplicam as
	/// poses possiveis, e um dicionario sem teto num jogo que roda horas e um vazamento devagar. Limpar
	/// tudo (em vez de expulsar o mais velho) e a escolha barata: o custo e reconstruir as poses vivas uma
	/// vez, e elas sao poucas.
	/// </summary>
	private static readonly Dictionary<long, (ImageTexture Textura, Image Imagem)> Campos = [];

	/// <inheritdoc cref="Campos"/>
	private const int TetoDoCache = 96;

	/// <inheritdoc cref="Campos"/>
	private (ImageTexture, Image) CampoDaPose(long chave)
	{
		if (Campos.TryGetValue(chave, out (ImageTexture Textura, Image Imagem) pronto)) return pronto;

		if (Campos.Count >= TetoDoCache) Campos.Clear();
		(ImageTexture, Image) novo = ConstruirCampo();
		Campos[chave] = novo;
		return novo;
	}

	/// <summary>
	/// PIXEL ART TEM ALFA QUASE BINARIO -- o corte baixo pega tambem a franja antialiasada que alguma
	/// folha convertida do `.dmi` trouxe, e franja tambem e silhueta.
	/// </summary>
	private const float CorteDoAlfa = 0.1f;

	/// <summary>
	/// ONDE FICA A PELE ENTRE DOIS PIXELS. A transformada de chanfro mede de CENTRO a CENTRO: o pixel de
	/// corpo encostado no vazio da 0,96 e nao zero. Descontar meio pixel dos dois lados poe o zero
	/// exatamente na fronteira entre o ultimo pixel aceso e o primeiro apagado -- que e onde o olho ve a
	/// borda do desenho, e o que faz a folga medir 5 px inteiros em vez de 4 ou 6.
	/// </summary>
	private const float MeioPixel = 0.5f;

	/// <summary>
	/// ============================ O CAMPO DE DISTANCIA, QUE E A TAREFA INTEIRA ============================
	/// Tres passos, e cada um responde uma metade do pedido:
	///
	///   1. COMPOR. A uniao do alfa das camadas VIVAS -- corpo, corpo-da-forma, rabo, roupa e cabelo, no
	///      quadro que cada uma esta desenhando agora. Quem escolhe quem entra e o `CharacterVisual`
	///      (`EhSilhueta`) e nao este arquivo: la a MESMA pergunta ja decide quem recebe contorno.
	///      E ISSO E O QUE A ELIPSE NAO SABIA FAZER: o topete do Super Saiyajin, a ponta do rabo e o vao
	///      entre as pernas so existem aqui.
	///
	///   2. MEDIR. Duas transformadas de chanfro: uma semeada nos pixels de CORPO (que devolve, pra cada
	///      pixel de fora, a distancia ate o boneco) e outra semeada nos de FORA (que devolve, pra cada
	///      pixel de dentro, a profundidade). Chanfro e nao euclidiana exata porque o erro dele e de ~2%
	///      -- em cinco pixels, um decimo de pixel -- e ele custa duas varreduras lineares em vez de uma
	///      fila de propagacao.
	///
	///   3. NORMALIZAR, e aqui esta a decisao que salva o rosto do personagem. Os dois lados NAO usam a
	///      mesma unidade:
	///
	///        fora   -> dividido pela FOLGA (5 px). E o pedido do dono, e ele e igual em toda direcao.
	///        dentro -> dividido pela ESPESSURA DESTA POSE (a maior profundidade encontrada).
	///
	///      Dividir o lado de dentro tambem pela folga seria o erro obvio e caro: um braco tem ~3 px de
	///      largura e um peito de lutador tem ~10, entao TUDO ficaria a menos de 5 px da pele, o
	///      `veu_no_corpo` sairia cheio no boneco inteiro e a nuvem apagaria o personagem -- que e
	///      exatamente por que a primeira tentativa de por este efeito na FRENTE do corpo foi recusada.
	///      Com a espessura como unidade, o miolo do peito e do rosto continua valendo -1 (veu ralo, o
	///      rosto aparece por baixo) e so o que e fino de verdade -- o braco, a mecha de cabelo -- afunda
	///      na nuvem, que e o que a referencia mostra.
	///
	/// O resultado sai em UM canal de 8 bits, um texel por pixel de mundo: 0 = fundo do corpo, 128 = a
	/// pele, 255 = o fim da nuvem. Um corpo de 32 gasta 42x42 = 1,7 KB.
	/// ====================================================================================================
	/// </summary>
	private (ImageTexture, Image) ConstruirCampo()
	{
		int n = _lado;
		var dentro = new bool[n * n];
		bool algum = false;

		foreach ((Texture2D quadro, Vector2 centro) in _camadas)
		{
			(Image? img, Rect2I regiao) = Recortar(quadro);
			if (img == null) continue;

			int ox = Mathf.RoundToInt(centro.X - regiao.Size.X * 0.5f) - _canto.X;
			int oy = Mathf.RoundToInt(centro.Y - regiao.Size.Y * 0.5f) - _canto.Y;

			for (int y = 0; y < regiao.Size.Y; y++)
			{
				int gy = oy + y;
				if (gy < 0 || gy >= n) continue;
				for (int x = 0; x < regiao.Size.X; x++)
				{
					int gx = ox + x;
					if (gx < 0 || gx >= n) continue;
					if (img.GetPixel(regiao.Position.X + x, regiao.Position.Y + y).A < CorteDoAlfa) continue;
					dentro[gy * n + gx] = true;
					algum = true;
				}
			}
		}

		if (!algum) SilhuetaSemMedida(dentro, n);

		float[] fora = Chanfro(dentro, n, semearNoCorpo: true);
		float[] fundo = Chanfro(dentro, n, semearNoCorpo: false);

		// A ESPESSURA DESTA POSE -- ver o passo 3 do cabecalho. O `Max(1)` e a guarda de uma silhueta de
		// um pixel de largura (uma fagulha, um rabo sozinho): sem ele a divisao estoura e o campo inteiro
		// sai saturado.
		float espessura = 0f;
		for (int i = 0; i < dentro.Length; i++)
			if (dentro[i] && fundo[i] > espessura) espessura = fundo[i];
		espessura = Mathf.Max(espessura - MeioPixel, 1f);

		var bytes = new byte[n * n];
		for (int i = 0; i < bytes.Length; i++)
		{
			// ============================ A FOLGA TEM QUE VIRAR UNIDADE DE CHANFRO ============================
			// `Folga` esta em PIXELS e `fora[i]` esta em unidades de chanfro, e as duas nao sao a mesma
			// coisa: um passo reto vale `PesoOrto` (0,9619) e nao 1. Dividir por `Folga` cru esticaria a
			// banda em 1/0,9619 -- e a bancada mediu isso na primeira rodada: 5,2 px onde o dono pediu 5.
			//
			// Multiplicar a folga pelo peso e a traducao, nao um ajuste: sao os mesmos 5 px, ditos na
			// lingua da transformada. (E vale igual na diagonal: 1,3604/raiz(2) = 0,962, o mesmo fator --
			// e por isso que uma unica constante corrige a volta inteira.)
			//
			// O LADO DE DENTRO NAO PRECISA, e nao pode levar: la o divisor e a `espessura`, que TAMBEM
			// esta em unidades de chanfro. A razao entre duas medidas da mesma regua ja e adimensional --
			// corrigir de novo seria aplicar o fator duas vezes.
			// ============================================================================================
			float s = dentro[i]
				? -Mathf.Min(Mathf.Max(fundo[i] - MeioPixel, 0f) / espessura, 1f)
				: Mathf.Min(Mathf.Max(fora[i] - MeioPixel, 0f) / (Folga * PesoOrto), 1f);
			bytes[i] = (byte)Mathf.RoundToInt((s * 0.5f + 0.5f) * 255f);
		}

		Image mapa = Image.CreateFromData(n, n, false, Image.Format.R8, bytes);
		return (ImageTexture.CreateFromImage(mapa), mapa);
	}

	/// <summary>
	/// A SILHUETA DE QUEM AINDA NAO TEM FOLHA. Ver <see cref="FracaoDaSilhuetaSemMedida"/>: um retangulo
	/// centrado, com metade da largura do quadro e a altura inteira dele. Ela existe pra a bancada nao ler
	/// "folga zero" num corpo recem-nascido e chamar isso de verde.
	/// </summary>
	private static void SilhuetaSemMedida(bool[] dentro, int n)
	{
		int quadro = Mathf.Max(n - 2 * Mathf.RoundToInt(Folga), 4);
		int larg = Mathf.Max(Mathf.RoundToInt(quadro * FracaoDaSilhuetaSemMedida), 2);
		int x0 = (n - larg) / 2, y0 = (n - quadro) / 2;

		for (int y = Mathf.Max(y0, 0); y < y0 + quadro && y < n; y++)
			for (int x = Mathf.Max(x0, 0); x < x0 + larg && x < n; x++)
				dentro[y * n + x] = true;
	}

	/// <summary>Os pesos de chanfro de Verwer -- os que menos erram contra a euclidiana num 3x3.</summary>
	private const float PesoOrto = 0.9619f, PesoDiag = 1.3604f;

	/// <summary>
	/// A TRANSFORMADA DE DISTANCIA, em duas varreduras. `semearNoCorpo` escolhe QUEM e o zero: semeando
	/// no corpo mede-se o lado de FORA (a folga), semeando no vazio mede-se a PROFUNDIDADE de dentro.
	///
	/// A varredura pra frente propaga dos vizinhos de cima e da esquerda, a de tras dos de baixo e da
	/// direita -- juntas elas alcancam qualquer caminho. E o `Longe` nao e `float.MaxValue` de proposito:
	/// somar peso a ele tem que continuar cabendo no `float` sem virar infinito.
	/// </summary>
	private static float[] Chanfro(bool[] dentro, int n, bool semearNoCorpo)
	{
		const float Longe = 1e9f;
		var d = new float[n * n];
		for (int i = 0; i < d.Length; i++) d[i] = dentro[i] == semearNoCorpo ? 0f : Longe;

		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				int i = y * n + x;
				float v = d[i];
				if (v == 0f) continue;
				if (y > 0)
				{
					if (x > 0) v = Mathf.Min(v, d[i - n - 1] + PesoDiag);
					v = Mathf.Min(v, d[i - n] + PesoOrto);
					if (x < n - 1) v = Mathf.Min(v, d[i - n + 1] + PesoDiag);
				}
				if (x > 0) v = Mathf.Min(v, d[i - 1] + PesoOrto);
				d[i] = v;
			}

		for (int y = n - 1; y >= 0; y--)
			for (int x = n - 1; x >= 0; x--)
			{
				int i = y * n + x;
				float v = d[i];
				if (v == 0f) continue;
				if (y < n - 1)
				{
					if (x < n - 1) v = Mathf.Min(v, d[i + n + 1] + PesoDiag);
					v = Mathf.Min(v, d[i + n] + PesoOrto);
					if (x > 0) v = Mathf.Min(v, d[i + n - 1] + PesoDiag);
				}
				if (x < n - 1) v = Mathf.Min(v, d[i + 1] + PesoOrto);
				d[i] = v;
			}

		return d;
	}

	/// <summary>
	/// UMA IMAGEM POR FOLHA, e nao uma por quadro: os quadros moram todos na MESMA folha, e
	/// `AtlasTexture.GetImage()` recorta a regiao DECODIFICANDO O ATLAS INTEIRO toda vez. Sem o cache,
	/// montar uma pose custaria meia duzia de decodificacoes de folha pra ler 6 KB delas -- e isso agora
	/// acontece a cada troca de quadro da animacao, e nao mais uma vez por transformacao.
	///
	/// ============================ A CHAVE E O ID, E O DONO VIAJA JUNTO ============================
	/// Sao dois riscos opostos, e guardar os dois resolve os dois:
	///
	///   * chavear pelo OBJETO depende de o Godot devolver sempre o mesmo inv&#243;lucro em C# pra o mesmo
	///     recurso nativo. Ele devolve -- mas o dia em que nao devolvesse, o dicionario nao acertaria uma
	///     unica vez, encheria ate o teto e voltaria a decodificar a folha inteira a cada troca de quadro.
	///     Nao daria erro nenhum: daria LENTIDAO, que e o que menos se enxerga numa foto.
	///   * chavear pelo ID sozinho e pior: id de instancia e RECICLADO pelo motor, e uma folha liberada
	///     mais outra criada devolveriam a mesma chave apontando pra imagem errada -- na tela, "a mascara
	///     do cabelo de outra pessoa".
	///
	/// Entao a chave e o id (estavel por construcao) e o VALOR carrega a folha, que a mantem viva -- e
	/// folha viva nao devolve o id pra ninguem.
	/// ========================================================================================
	/// </summary>
	private static readonly Dictionary<ulong, (Texture2D Dono, Image Img)> Folhas = [];

	/// <inheritdoc cref="Folhas"/>
	private const int TetoDeFolhas = 32;

	/// <inheritdoc cref="Folhas"/>
	private static (Image? Img, Rect2I Regiao) Recortar(Texture2D quadro)
	{
		Texture2D dono = quadro is AtlasTexture at && at.Atlas != null ? at.Atlas : quadro;

		if (!Folhas.TryGetValue(dono.GetInstanceId(), out (Texture2D Dono, Image Img) achada))
		{
			if (dono.GetImage() is not { } lida) return (null, default);
			if (Folhas.Count >= TetoDeFolhas) Folhas.Clear();
			achada = (dono, lida);
			Folhas[dono.GetInstanceId()] = achada;
		}

		return (achada.Img, quadro is AtlasTexture a2 && a2.Atlas != null
			? new Rect2I((Vector2I)a2.Region.Position, (Vector2I)a2.Region.Size)
			: new Rect2I(0, 0, achada.Img.GetWidth(), achada.Img.GetHeight()));
	}

	/// <summary>
	/// Acende ou apaga a nebulosa. Chamado dos dois caminhos que vestem uma forma -- o
	/// `Transformacao.Vestir` (com cinematica) e o `World.AcenderFormaNoCorpo` (sem) --, sempre com
	/// a resposta do <see cref="Jandirus.Core.Forms.Catalogo.PaletaDaNebulosa"/>.
	///
	/// ============================ E ELE RECEBE A PALETA, NAO UM `bool` ============================
	/// Isto era `Definir(bool ligado)` enquanto a nuvem tinha um dono so. O Ultra Ego trouxe a segunda
	/// paleta, e a escolha de passar as CORES (nulo = apagar) em vez de acrescentar um segundo metodo
	/// e a mesma que o `Aura.CorDaChamaDe` fez quando a cor de aura virou pessoal: uma sobrecarga
	/// `Definir(bool)` compilaria nos dois chamadores antigos e acenderia a nuvem INDIGO num Ultra Ego,
	/// calada. Trocando o tipo, foi o compilador quem listou os dois pontos do jogo.
	///
	/// AS QUATRO CORES SAO ESCRITAS TODA VEZ QUE ACENDE, e nao uma vez no `_Ready`: o mesmo corpo troca
	/// de forma sem trocar de node (`ultra_ego` -> `base` -> `ui_sign` e o caminho de quem tem as duas
	/// disciplinas na mesma conta em teste), e uma paleta escrita so no nascimento deixaria o segundo
	/// dono com a cor do primeiro.
	///
	/// O `Visible` DESLIGA O DESENHO DE VERDADE, e nao so o alfa: com `forca = 0` o shader ainda
	/// seria chamado pra cada um dos ~1800 pixels do quad, por corpo, por quadro -- pra devolver
	/// transparente. Quase ninguem no jogo esta em Ultra Instinto; o custo tem que ser zero pra
	/// esse "quase todo mundo".
	///
	/// ============================ E O `SetProcess` E A OUTRA METADE DESSE ZERO ============================
	/// A mascara acompanha a animacao (ver o `_Process`), e isso custa uma leitura das camadas por quadro.
	/// Pagar isso em todo corpo da zona pra descobrir que ninguem esta em Ultra Instinto seria trocar um
	/// custo de GPU por um de CPU e chamar de conserto. Quem nao acende nao processa.
	/// ==================================================================================================
	/// </summary>
	public void Definir(Jandirus.Core.Forms.PaletaDeNebulosa? paleta)
	{
		bool ligado = paleta is not null;
		if (paleta is { } p)
		{
			// `new Color(hexa)` aceita o hexa SEM `#` (e com), que e o formato em que o Core guarda cor
			// -- ver `Catalogo.CorDoContorno` e as irmas. O Core nao conhece o Godot; a traducao e aqui.
			_tinta.SetShaderParameter("cor_borda", new Color(p.Borda));
			_tinta.SetShaderParameter("cor_meio", new Color(p.Meio));
			_tinta.SetShaderParameter("cor_perto", new Color(p.Perto));
			_tinta.SetShaderParameter("cor_dos_pontos", new Color(p.Pontos));
		}

		if (ligado) SeguirOBoneco();
		_tinta.SetShaderParameter("forca", ligado ? 1f : 0f);
		_quad.Visible = ligado;
		SetProcess(ligado);
		_ligada = ligado;

		// A CARGA NAO ATRAVESSA A TROCA DE FORMA. Sem esta linha, quem largasse o Ultra Instinto com o
		// C na mao voltaria a vestir a forma, quadros depois, com o adensamento do ultimo instante
		// congelado -- `Carga` sai cedo quando a nuvem esta apagada (ver la), entao ninguem zeraria.
		// Um valor que sobrevive ao proprio estado e a definicao de estado velho.
		if (!ligado && _carga != 0f) { _carga = 0f; _tinta.SetShaderParameter("carga", 0f); }
	}

	/// <summary>
	/// ============================ O SEGUNDO PAPEL: ESTE CORPO ESTA CARREGANDO ============================
	/// Em Ultra Instinto a nuvem TOMA O LUGAR da folha de chama -- ordem do dono: *"a aura/carga do
	/// ultra instinto deveria ser essa aura em shaders, e nao o icone de carga atual"*. Quem desenhava a
	/// chama (a <see cref="CargaVisual"/>) fica muda (ver `SpriteDeAura.DefinirFolha`) e manda a mesma
	/// <paramref name="forca"/> pra ca.
	///
	/// A REGRA DO KI NAO SE MOVE UM CENTIMETRO: quem chama isto e a `CargaVisual.Pintar`, que obedece ao
	/// par `carregando`/`sobrecarregado` do snapshot -- tecla C ou Ki acima de 100%, e nada mais. O que
	/// muda e O QUE acende, nao QUANDO.
	///
	/// ============================ E POR ISSO A GUARDA E AQUI, E NAO NA `CargaVisual` ============================
	/// A `CargaVisual` chama isto sem perguntar se o corpo esta em Ultra Instinto -- e tem que ser
	/// assim: perguntar la seria um `if` por forma no cliente, que e exatamente o que a
	/// <see cref="Jandirus.Core.Forms.FolhaDeAura.Nebulosa"/> existe pra nao precisar. Quem sabe se ha
	/// nuvem e a nuvem. Num corpo sem Ultra Instinto isto custa uma comparacao de bool e sai.
	/// ========================================================================================================
	/// </summary>
	public void Carga(bool ativa, float forca)
	{
		if (!_ligada) return;

		float v = ativa ? Mathf.Max(forca, 0f) : 0f;
		// A `CargaVisual` repinta TODO QUADRO enquanto o C esta segurado (a chama dela pulsa), e o valor
		// muda a cada um -- entao a comparacao aqui nao economiza o caso comum. Ela existe pro caso
		// contrario: o corpo parado, que recebe `Carga(false, 0)` a cada snapshot e nao pode pagar uma
		// escrita de uniform por isso.
		if (Mathf.IsEqualApprox(v, _carga)) return;
		_carga = v;
		_tinta.SetShaderParameter("carga", v);
	}

	/// <inheritdoc cref="Carga"/>
	private float _carga;

	/// <inheritdoc cref="Carga"/>
	private bool _ligada;

	/// <inheritdoc cref="_Ready"/>
	private static ImageTexture QuadBranco()
	{
		Image img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		img.Fill(Colors.White);
		return ImageTexture.CreateFromImage(img);
	}
}
