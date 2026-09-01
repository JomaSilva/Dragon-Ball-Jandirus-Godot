namespace Jandirus.Core.World;

/// <summary>
/// ============================ OS PEDACOS DE UM PLANETA QUE ACABOU DE EXPLODIR ============================
/// O pedido do dono, literal: *"quando um planeta explodir e ele tiver a explosao, vao haver um clarao
/// logo onde ele estava, ele vai sumir do espaco pra todos os jogadores (server sync) e onde ficava o
/// planeta vao ter uns asteroides/rochas q vao girar lentamente e se afastar de onde era o planeta pra
/// representar os pedacos do planeta. o icone usado e Asteroid5112013.png. dps de um tempo eles
/// despawnam pro servidor n ter q ficar gastando tempo de tick pra ver a posicao de asteroides"*.
///
/// ============================ O SERVIDOR NAO PAGA **NADA** POR QUADRO, E ESSE E O DESENHO ============================
/// A razao que o dono deu pro despawn -- *"pro servidor n ter q ficar gastando tempo de tick pra ver a
/// posicao de asteroides"* -- e atendida de forma MAIS forte do que ele pediu: **nao ha posicao de
/// asteroide em lugar nenhum do servidor**, nem por um quadro. Aqui a posicao de cada pedaco e FUNCAO
/// PURA de `(semente do planeta, indice, segundos desde o estouro)`, e o unico fato que viaja pelo fio e
/// o que **ja viajava**: "este planeta morreu, e ha tantos segundos" (o `EstadoDaMorte.Faltam` do
/// `S2C.Mortos`, que ja carregava o relogio da agonia).
///
/// Foi MEDIDO antes de ser escrito, e sao dois numeros que decidiram:
///   * SINCRONIZAR pedaco a pedaco custaria, pelo formato do `EntityState` (`Net/Protocol.cs`), 14 a 18
///     bytes por corpo por quadro: **24 pedacos x 30 jogadores em orbita = 380 KB/s POR CAMPO**, e
///     1,85 MB/s com cinco campos vivos. Este e o produto (pedacos x campos x jogadores) que este
///     projeto ja recusou tres vezes -- no ceu de estrelas, na lua e no terreno;
///   * DERIVAR custa 0,7 us por quadro pros 24 pedacos, **no cliente**, e zero no servidor.
///
/// E ha um terceiro motivo, que nao e custo e e o decisivo: `World.DesenharPlanetas` **mata e recria
/// todos os orbes** a cada `VizinhancaMudou` (`Client/World.cs:4422`). Um destroco que fosse node com
/// estado (velocidade, angulo, vida acumulada) desapareceria quando o jogador cruzasse uma fronteira de
/// chunk. Uma funcao pura reconstroi o campo IDENTICO no quadro seguinte, sozinha, sem nem perceber.
///
/// E o mesmo padrao que este jogo ja usa duas vezes pela mesma razao: a carta estelar
/// (`Client/MapaEstelar.cs`, que enumera planetas sozinha) e as pedras da agonia
/// (`Client/PedrasDaAgonia.cs` -- *"dois amigos lado a lado tem que ver a MESMA pedra"*).
/// ================================================================================================================
///
/// ============================ E O "DESPAWN" VIRA UMA JANELA, QUE E MAIS FORTE ============================
/// Sem estado nao ha o que limpar: passada a <see cref="SegundosDaJanela"/>, a funcao para de ser
/// avaliada e o node do cliente se recolhe sozinho. Nao existe o modo de falha classico do despawn --
/// o objeto que ficou pendurado porque o dono dele morreu antes de mandar limpar.
/// ======================================================================================================
///
/// ============================ O QUE **NAO** ESTA AQUI ============================
///   * O CLARAO (o pedido *"vao haver um clarao logo onde ele estava"*) **ja existia e ja acende no
///     lugar**: e o `nucleo` do `Assets/Shaders/EstouroDePlaneta.gdshader:83-84`
///     (`smoothstep(raioNucleo, raioNucleo*0.35, r) * pow(1.0 - t, 3.0)`, com `cor_nucleo` branco-QUENTE
///     e nao branco puro). Nada foi construido pra ele -- ver `PlanetaDesenhado.Estourar`;
///   * O SUMICO DO PLANETA (o "server sync" que o dono grifou) tambem ja era do servidor: a lista de
///     mortos e autoridade dele (`GameServer.MandarMortos`), e o cliente so obedece
///     (`World.DesenharPlanetas` pula o morto). Nada mudou ali.
/// ================================================================================
/// </summary>
public static class DestrocosDeMundo
{
	/// <summary>
	/// ============================ QUANTO TEMPO OS PEDACOS FICAM, E POR QUE 60 SEGUNDOS ============================
	/// O numero saiu de uma medida e nao do gosto: **quem pode ver os destrocos e so quem esta dentro da
	/// vizinhanca ativa**, porque o disco de um planeta so e desenhado a partir do que o `S2C.Vizinhanca`
	/// manda, e esse pacote e `Espaco.PorPerto(..., raio: Espaco.RaioAtivo)` -- o 3x3 de chunks em volta
	/// de quem olha, ou seja **6.144 px de ponta a ponta** (<see cref="Espaco.ChunkPx"/> = 2.048).
	///
	/// Atravessar essa vizinhanca inteira na velocidade BASE de voo (`MoveRules.BaseSpeedPx` = 160 px/s)
	/// leva 38 s. Sessenta segundos cobrem, com meia janela de folga, a chegada mais lenta possivel
	/// partindo do ponto mais distante em que o planeta sequer chega a ser desenhado. Quem esta mais
	/// longe que isso nao recebe o corpo no pacote e nunca veria nem o planeta, nem a explosao, nem os
	/// destrocos -- esticar a janela pra ele seria esticar pra ninguem.
	///
	/// E o outro lado do numero: 60 s sao **27x** a mega explosao (2,2 s) e **um quinto** dos cinco
	/// minutos de agonia. O rescaldo dura bem mais que o acontecimento e bem menos que a espera -- se
	/// passasse disso, o ceu de um servidor antigo viraria um cemiterio de cacos.
	///
	/// E a terceira ponta, que amarra com o <see cref="AlcanceEmRaios"/>: e por volta dos 60 s que o
	/// caco chega na beirada do que a camera alcanca. A janela acaba quando ele ia sumir de qualquer
	/// jeito, e nao no meio do caminho.
	/// ==========================================================================================================
	/// </summary>
	public const double SegundosDaJanela = 60;

	/// <summary>
	/// QUANTOS PEDACOS UM MUNDO DEIXA, pelo raio dele -- com **teto duro escrito**, e um teto que MORDE.
	///
	/// A disciplina e a que este projeto ja aceitou em toda populacao visual: 64 pedras na
	/// `PedrasDaAgonia`, 24 nodes de poeira na `PoeiraDeEstrago`, 120 decalques com prazo. O pior caso e
	/// escrito e nao descoberto.
	///
	/// E ele nao e decorativo: o raio de um planeta no espaco vai de 110 a 220 px nos pre-feitos, e o
	/// disco de um sistema gerado pode passar disso. Pela conta crua (`raio/12`), um mundo de raio 440
	/// pediria 36 pedacos -- o teto corta em 24. Um de raio 110 pediria 9, e o piso levanta pra 12: um
	/// punhado de tres cacos nao le como *"o planeta se despedacou"*, le como lixo espacial.
	/// </summary>
	public const int MinPedacos = 12, MaxPedacos = 24;

	/// <summary>Quantos pedacos este mundo deixa. Ver <see cref="MinPedacos"/>.</summary>
	public static int Quantos(float raio) =>
		Math.Clamp((int)(raio / 12f), MinPedacos, MaxPedacos);

	/// <summary>
	/// ============================ ATE ONDE O CACO VAI, E QUEM DECIDIU ESSE NUMERO FOI A CAMERA ============================
	/// Em raios do planeta, no fim da janela. **1,3 nao e gosto: e o tamanho da tela.**
	///
	/// A camera deste jogo tem zoom INTEIRO com piso 2 (`Settings.ZoomMin`, `World.PisoDoZoom`), entao
	/// num monitor de 1280x720 o jogador enxerga **640 x 360 pixels de MUNDO** -- meia largura de 320 e
	/// meia altura de 180. E o raio de um planeta pre-feito vai de 140 (Makyo) a 220 (Terra). Ou seja:
	/// **um planeta ja nao cabe na tela**, e quem esta em orbita ve uma parede de mundo.
	///
	/// Com 1,3 raio, e o leque de 0,75x a 1,25x por caco, o campo termina a 210..360 px do centro pra a
	/// Terra -- exatamente na beirada do que a tela alcanca. Isso da o que o dono pediu nos dois
	/// sentidos: os cacos **se afastam de verdade** (saem de 33..110 px e chegam a 210..360, tres a seis
	/// vezes mais longe) e continuam **em quadro pelo minuto inteiro**, em vez de virarem pontos fora do
	/// alcance da camera nos primeiros dez segundos.
	///
	/// Ir mais longe (o primeiro valor escrito aqui foi 2,6) poe o caco a 570..800 px, que e duas telas
	/// e meia de distancia: o efeito existiria, ninguem veria, e o unico sintoma seria "as pedras somem
	/// rapido demais" -- que e o tipo de defeito que so a foto pega.
	/// ==================================================================================================================
	/// </summary>
	public const double AlcanceEmRaios = 1.3;

	/// <summary>
	/// ============================ O AFASTAMENTO **DESACELERA**, E ISSO FOI DECIDIDO ============================
	/// Expoente **abaixo de 1**: a derivada cai com o tempo, entao o caco arranca junto com a explosao e
	/// depois so vai indo. E a MESMA curva que este projeto ja usa duas vezes pela mesma razao --
	/// `pow(t, 0.65)` na frente de choque do `EstouroDePlaneta.gdshader:78` e no `Gota.gdshader`.
	///
	/// A alternativa "fisica" seria linear (no vacuo nao ha arrasto, e a rocha manteria a velocidade do
	/// impulso). Ela foi recusada com o motivo escrito: uma velocidade unica que ainda mostre
	/// espalhamento no fim de 60 s e **invisivel** nos dois primeiros segundos -- exatamente quando o
	/// jogador esta olhando, e exatamente quando o planeta se despedacou. E acelerar esta fora de
	/// cogitacao: nao ha motor numa pedra, e um caco que acelera sai da tela em segundos levando o
	/// efeito junto.
	/// ==========================================================================================================
	/// </summary>
	public const double ExpoenteDoAfastamento = 0.6;

	/// <summary>
	/// QUANTOS QUADROS TEM A FOLHA `Asteroid5112013` (`Icons/Misc/Asteroid5112013.dmi` no BYOND).
	///
	/// **A ARTE JA E UMA ROTACAO PRONTA**, e isso muda o desenho inteiro do giro. Medido quadro a quadro:
	/// a area opaca varia suave (6.956 -> 7.898 -> 6.561 px), o bbox desliza continuamente e a cor media
	/// fica travada em rocha (~65,58,51) nos dezesseis. Nao sao 16 pedras diferentes -- e **uma pedra so
	/// cambaleando**, mostrando faces diferentes. O `.dmi` confirma: `frames = 16, delay = 1`.
	///
	/// Consequencia direta: *"girar lentamente"* e **andar a folha devagar**, e nao `Node2D.Rotation`.
	/// Rotacionar por codigo por cima de um cambaleio ja desenhado gira DUAS vezes, e o que se le e um
	/// sprite girando, nao uma pedra tombando no vacuo.
	/// </summary>
	public const int QuadrosDaFolha = 16;

	/// <summary>
	/// A VELOCIDADE DA FOLHA (o `SpeedScale` do `AnimatedSprite2D`), de mais lenta a menos lenta.
	///
	/// A folha corre a `speed = 10` com 16 quadros, ou seja **1,6 s por volta** na velocidade nativa --
	/// que e um pedregulho em panico. 0,16..0,27 poem a volta em **6 a 10 segundos**, que e o
	/// *"lentamente"* do pedido.
	///
	/// E a faixa e faixa (e nao um numero) porque **os pedacos nao podem cambalear em unissono**: com
	/// todos na mesma fase e na mesma velocidade, dezesseis pedras giram como uma so e o campo denuncia
	/// que e um efeito. E a mesma razao da fase por `INSTANCE_CUSTOM` nos raios de forma.
	/// </summary>
	public const double GiroMin = 0.16, GiroMax = 0.27;

	/// <summary>
	/// O TAMANHO NA TELA, como fracao do quadro de 128 px da folha, antes do ajuste pelo raio do mundo.
	///
	/// 0,16 a 0,30 dao **20 a 38 px** num planeta de raio 200 (cujo disco tem 300 px de diametro na
	/// tela) -- caco, e nao lua. Em 1:1 o quadro de 128 px teria quase metade do diametro do planeta
	/// inteiro, o que leria como "nasceram tres luas".
	/// </summary>
	public const double EscalaMin = 0.16, EscalaMax = 0.30;

	/// <summary>
	/// O SAL DESTA FAMILIA DE SORTEIOS. Sem ele, `Misturar(seed, i, 0)` colidiria com qualquer outro
	/// consumidor que use a MESMA seed de planeta e um indice pequeno -- e ha varios (o bioma, o
	/// terreno, a semente do shader). Dois sistemas sorteando o mesmo numero da mesma semente ficam
	/// correlacionados de um jeito que ninguem ve olhando e ninguem consegue depurar depois.
	/// </summary>
	private const ulong Sal = 0xDEAD_FACE_0BAD_C0DEUL;

	/// <summary>
	/// UM PEDACO, resolvido **uma vez** e guardado por quem desenha.
	///
	/// ============================ POR QUE ISTO E CACHE E NAO UMA FUNCAO DE (i, t) ============================
	/// *"Nada pesado dentro do tique"* e requisito explicito do dono nesta tarefa. Tudo o que depende so
	/// de `(semente, i)` -- o hash, o seno, o cosseno -- e resolvido no nascimento do campo e **nunca
	/// mais**. Por quadro sobra: UM `Math.Pow` pro campo inteiro (ver <see cref="Avanco"/>) e uma
	/// multiplicacao de vetor por pedaco. Medido: 0,7 us pros 24 pedacos.
	/// ======================================================================================================
	/// </summary>
	/// <param name="Rumo">A direcao, ja normalizada -- pra onde este caco foi jogado.</param>
	/// <param name="Distancia0">A que distancia do centro ele comeca (o mundo tinha casca, e nao um ponto).</param>
	/// <param name="Distancia1">Onde ele estara no fim da janela.</param>
	/// <param name="Escala">O tamanho dele na tela, em fracao do quadro de 128 px.</param>
	/// <param name="Quadro">Em que quadro da folha ele COMECA -- ver <see cref="QuadrosDaFolha"/>.</param>
	/// <param name="Giro">O `SpeedScale` da folha dele -- ver <see cref="GiroMin"/>.</param>
	public readonly record struct Pedaco(
		Vec2 Rumo, double Distancia0, double Distancia1, double Escala, int Quadro, double Giro);

	/// <summary>
	/// ============================ UM PEDACO, DERIVADO DA SEMENTE DO MUNDO ============================
	/// <see cref="Espaco.Misturar"/> e a mesma mistura que gera o universo, as estrelas e o terreno --
	/// estavel entre execucoes e entre maquinas. `GetHashCode()` do .NET **nao serve** (ele e
	/// randomizado por processo, e duas telas discordariam), e `Random` local seria o defeito que este
	/// desenho existe pra nao ter: dois jogadores em orbita do mesmo mundo morto veriam campos de
	/// destroco diferentes, e o dono grifou *"server sync"* justamente sobre isso.
	///
	/// **E ISSO DEIXOU DE SER UMA AFIRMACAO.** Trocando esta linha por `$"{semente}:{i}".GetHashCode()`
	/// e rodando as duas bancadas: a `--diagagonia`, que tem um processo so, fechou **74 OK e 0 FALHA**
	/// -- cega, e cega por construcao, porque os dois campos que ela compara nascem na MESMA memoria.
	/// A `--destrocosvivos`, com dois processos de verdade, apontou a primeira pedra a **46 px** de
	/// distancia entre um cliente e o outro. Um sorteio estavel dentro do processo e diferente entre
	/// processos e invisivel pra qualquer bancada que nao tenha dois.
	///
	/// ============================ O ANGULO E ESTRATIFICADO, E NAO SORTEADO SOLTO ============================
	/// `i * 2pi/N` mais um empurrao dentro do proprio setor. Angulo puramente sorteado AGRUPA: com 18
	/// amostras uniformes num circulo, um quarto do ceu fica vazio e outro ganha tres cacos colados --
	/// e o olho le "isso foi jogado pra um lado" em vez de "isso se despedacou". O empurrao vai a 90% do
	/// setor pra a grade nao aparecer.
	/// ====================================================================================================
	/// </summary>
	public static Pedaco De(ulong semente, int i, float raio)
	{
		ulong h = Espaco.Misturar(semente ^ Sal, (ulong)(uint)i, 0x0A57UL);

		// QUATRO SORTEIOS INDEPENDENTES DO MESMO HASH, cada um numa janela de digitos diferente. Um
		// `Misturar` por grandeza custaria quatro hashes onde um basta, e as janelas de um SplitMix ja
		// sao independentes o bastante pra isto -- a mesma leitura por faixas que a `PedrasDaAgonia` faz.
		double f1 = h % 1000 / 1000.0;
		double f2 = h / 1000 % 1000 / 1000.0;
		double f3 = h / 1_000_000 % 1000 / 1000.0;
		double f4 = h / 1_000_000_000 % 1000 / 1000.0;

		int n = Math.Max(1, Quantos(raio));
		double setor = 2 * Math.PI / n;
		double ang = i * setor + (f1 - 0.5) * setor * 0.9;

		// A CASCA, E NAO UM PONTO: o mundo tinha um raio de largura, entao o caco comeca em algum lugar
		// entre o miolo e a superficie. Todos nascendo no centro exato leriam como fonte, e nao como
		// planeta que se partiu.
		double d0 = raio * (0.05 + f2 * 0.45);

		// ============================ O PERCURSO E SORTEADO SOZINHO, E ISSO A FOTO EXIGIU ============================
		// A primeira versao sorteava o DESTINO (`d1 = raio * alcance * (0,75..1,25)`), e o resultado na
		// foto foi um **ANEL OCO**: como todo caco terminava mais ou menos na mesma faixa e todos
		// avancam pela mesma curva, aos 6 s eles estavam todos entre 78 e 172 px do centro -- uma
		// coroa, com o miolo vazio. Escombro de um mundo nao e uma coroa.
		//
		// Sorteando o PERCURSO (quanto cada um anda, e nao onde cada um para), o caco lento fica perto
		// de onde o planeta estava e o rapido vai embora: o campo ganha profundidade e o miolo continua
		// povoado o tempo todo. E de brinde a garantia fica de graca -- `d1 = d0 + percurso` com
		// percurso positivo **nunca** fica atras de `d0`, entao sumiu o `Math.Max` que existia so pra
		// impedir um caco de andar pra DENTRO de um planeta que nao existe mais.
		// ==========================================================================================================
		double percurso = raio * AlcanceEmRaios * (0.30 + f3 * 1.00);

		return new Pedaco(
			new Vec2((float)Math.Cos(ang), (float)Math.Sin(ang)),
			d0,
			d0 + percurso,
			// O CACO DE UM MUNDO GRANDE E MAIOR: a escala acompanha o raio, ancorada nos 200 px do
			// planeta mediano. Sem isso, um gigante e um mundinho deixariam pedra do mesmo tamanho.
			(EscalaMin + f4 * (EscalaMax - EscalaMin)) * (raio / 200.0),
			(int)(h % QuadrosDaFolha),
			GiroMin + f2 * (GiroMax - GiroMin));
	}

	/// <summary>
	/// O AVANCO DA FRENTE, de 0 a 1 -- **um `Math.Pow` pro campo inteiro, por quadro**.
	///
	/// Fica fora do <see cref="Onde"/> de proposito: ele nao depende do pedaco, so do relogio. Chamado
	/// por pedaco seriam 24 `Math.Pow` por quadro pra 24 resultados identicos.
	/// </summary>
	public static double Avanco(double segundosDesdeOEstouro) =>
		Math.Pow(Math.Clamp(segundosDesdeOEstouro / SegundosDaJanela, 0, 1), ExpoenteDoAfastamento);

	/// <summary>ONDE O PEDACO ESTA, em pixels relativos ao centro de onde o planeta estava.</summary>
	public static Vec2 Onde(in Pedaco p, double avanco) =>
		p.Rumo * (float)(p.Distancia0 + (p.Distancia1 - p.Distancia0) * avanco);

	/// <summary>
	/// QUANTO O CAMPO ESTA VISIVEL, de 0 a 1.
	///
	/// ENTRA depois do clarao e SAI no fim da janela, e as duas pontas tem motivo:
	///   * a entrada e o tempo da mega explosao (<see cref="MortePlanetaria.SegundosDoEstouro"/>): nos
	///     primeiros instantes o nucleo branco-quente do `EstouroDePlaneta.gdshader` cobre este mesmo
	///     lugar, e um caco desenhado por cima do clarao seria um caco flutuando DENTRO da luz;
	///   * a saida e o ultimo quarto da janela. Sem ela o campo inteiro sumiria num quadro, que e
	///     exatamente o "pop" que denuncia despawn -- e o dono pediu que eles sumissem, nao que
	///     piscassem.
	/// </summary>
	public static double Opacidade(double segundosDesdeOEstouro)
	{
		if (segundosDesdeOEstouro <= 0 || segundosDesdeOEstouro >= SegundosDaJanela) return 0;

		double entrada = Math.Clamp(segundosDesdeOEstouro / MortePlanetaria.SegundosDoEstouro, 0, 1);

		const double ComecoDoFim = 0.75;
		double f = segundosDesdeOEstouro / SegundosDaJanela;
		double saida = f <= ComecoDoFim ? 1 : (1 - f) / (1 - ComecoDoFim);

		return Math.Clamp(Math.Min(entrada, saida), 0, 1);
	}

	/// <summary>Este planeta ainda tem destrocos no ceu? O corte unico, pros dois desenhistas.</summary>
	public static bool DentroDaJanela(double segundosDesdeOEstouro) =>
		segundosDesdeOEstouro >= 0 && segundosDesdeOEstouro < SegundosDaJanela;
}
