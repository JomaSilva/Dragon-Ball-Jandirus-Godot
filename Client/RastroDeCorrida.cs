using Godot;

namespace Jandirus.Client;

/// <summary>
/// O RASTRO DE QUEM CORRE -- e ele e TEMPORAL, que e o que "motion blur" quer dizer.
///
/// ============================ POR QUE A VERSAO ANTERIOR PISCAVA ============================
/// A primeira tentativa era um smear no shader: quatro amostras extras da propria textura ao longo
/// do rumo do movimento. O dono reportou a piscada duas vezes, e na segunda veio com o diagnostico
/// junto -- "n teria como o efeito ser um motion blur?".
///
/// Ele apontou a natureza do defeito, nao so o sintoma. Aquilo era um borrao ESPACIAL, calculado
/// dentro do quadro ATUAL da animacao. E a animacao de corrida roda 2,2x mais rapido: a cada troca
/// de quadro do .dmi, TODO o conteudo amostrado trocava de uma vez. O borrao dava um salto por
/// quadro, e salto periodico e exatamente o que o olho le como piscada.
///
/// Eu tinha tentado consertar suavizando a DIRECAO. Nao alcancava: a descontinuidade nao estava na
/// direcao, estava na FONTE.
///
/// Motion blur de verdade mostra onde o corpo ESTEVE. Aqui, copias do corpo largadas nas posicoes
/// passadas, esmaecendo. Copia velha guarda o quadro velho -- trocar de quadro nao mexe no que ja
/// foi desenhado, entao a piscada nao tem por onde nascer.
/// ==========================================================================================
///
/// CUSTO: uma foto a cada 22 ms com vida de 0,085 s da ~4 copias vivas. Cada uma sao 3-6 Sprite2D
/// sem processamento proprio (quem conta o tempo e este node, um so). E menos trabalho por quadro
/// do que o shader que ele substitui, que rodava 5 amostras por pixel do personagem inteiro.
/// </summary>
/// <remarks>
/// <see cref="IFicaNoChao"/>: este node NAO DESENHA NADA por si. As fotos que ele larga sao filhas
/// do palco e nascem carimbadas em `_visual.GlobalPosition` -- que ja carrega a altitude do voo, e
/// e justamente a correcao descrita mais abaixo. Mover este node nao mudaria um pixel; a declaracao
/// existe pra que a varredura de "quem sobe com o voo" saiba que ele ficar parado e a resposta
/// CERTA, e nao a quinta vez que alguem foi esquecido.
/// </remarks>
public partial class RastroDeCorrida : Node2D, IFicaNoChao
{
	/// <summary>
	/// O grupo em que TODA copia do rastro entra -- a unica alca que existe sobre elas.
	///
	/// Vale pras duas fontes (a corrida e o arranque) de proposito: a pergunta da bancada e "que pixel
	/// deste quadro e rastro?", e um rastro que ficasse de fora por ter nascido pela outra porta daria
	/// uma resposta menor que a verdade. Ver o uso em <see cref="Largar"/> e a `--diagborrao`.
	/// </summary>
	public const string GrupoDoRastro = "rastro_de_corrida";


	// ============================ POR QUE ESTES TRES NUMEROS ============================
	// A primeira calibragem (45 ms / 0,17 s / alfa 0,34) deu o que o dono fotografou: TRES bonecos
	// nitidos e espacados. Ele nomeou os dois problemas -- "o atual tem um efeito mt comprido e n
	// da impressao de velocidade alta".
	//
	// Sao a mesma causa. Copia NITIDA e ESPACADA le como copia; o olho conta "um, dois, tres". Pra
	// ler como BORRAO as copias precisam se SOBREPOR e serem fracas o bastante pra nenhuma se
	// destacar -- ai o cerebro funde as tres num rastro so.
	//
	// Entao: intervalo pela METADE (as fotos se sobrepoem em vez de enfileirar), vida pela metade
	// (rastro curto -- rastro comprido le como lentidao, nao velocidade) e alfa quase pela metade
	// (nenhuma copia compete com o corpo). Sao MAIS fotos e um efeito MENOR.
	// ==================================================================================

	/// <summary>Intervalo entre duas fotos. Menor SOBREPOE as copias, que e o que vira borrao.</summary>
	private const double Intervalo = 0.018;

	/// <summary>Quanto cada foto dura. Vezes o intervalo, e o COMPRIMENTO do rastro.</summary>
	private const double Vida = 0.055;

	/// <summary>Opacidade da foto mais nova. Baixa de proposito: rastro nao compete com o corpo.</summary>
	private const float Alfa = 0.26f;

	private CharacterVisual? _visual;
	private Node2D? _corpo;
	private double _proxima;
	private bool _ligado;

	/// <summary>Pra onde o corpo estava indo na ultima foto. Alimenta o borrao de cada copia.</summary>
	private Vector2 _ultimoRumo = Vector2.Right;
	private Vector2 _posAnterior;

	public override void _Ready()
	{
		_corpo = GetParent<Node2D>();
		_visual = _corpo?.GetNodeOrNull<CharacterVisual>("Visual");
		SetProcess(false);
	}

	/// <summary>
	/// Liga/desliga. Chamado por quadro pelo corpo, entao a primeira linha e a comparacao.
	///
	/// DESLIGAR NAO APAGA AS FOTOS QUE JA SAIRAM -- elas terminam de esmaecer sozinhas, e e isso
	/// que faz o rastro se recolher em vez de sumir num quadro quando o jogador solta o SHIFT.
	/// </summary>
	public void Definir(bool correndo)
	{
		if (correndo == _ligado) return;
		_ligado = correndo;
		// O ARRANQUE PENDENTE SEGURA O PROCESSO LIGADO. Parar de correr no mesmo quadro em que um
		// salto foi anunciado desligaria o `_Process` antes de o rastro do salto ser desenhado --
		// e a posicao de chegada ainda nem tinha chegado. Ver `ResolverArranque`.
		SetProcess(correndo || _arranqueDe != null);
		_proxima = 0;   // a primeira foto sai NO ATO de comecar a correr
		if (correndo && _corpo != null) _posAnterior = _corpo.GlobalPosition;
	}

	public override void _Process(double delta)
	{
		if (_visual == null || _corpo == null) { SetProcess(false); return; }

		ResolverArranque(delta);

		// SO O ARRANQUE PENDENTE MANTEM ESTE NODE ACORDADO quando ninguem esta correndo.
		if (!_ligado)
		{
			if (_arranqueDe == null) SetProcess(false);
			return;
		}

		// O RUMO SAI DO DESLOCAMENTO REAL entre duas fotos. Meio pixel de piso: perto de zero a
		// normalizacao vira ruido e o borrao apontaria pra qualquer lado.
		Vector2 desl = _corpo.GlobalPosition - _posAnterior;
		if (desl.LengthSquared() > 0.25f) _ultimoRumo = desl.Normalized();
		_posAnterior = _corpo.GlobalPosition;

		_proxima -= delta;
		if (_proxima > 0) return;
		_proxima = Intervalo;

		// ============================ A FOTO SAI DO DESENHO, NAO DO NO ============================
		// Voando, o corpo e DESENHADO ate 160 px acima do no (o no fica na posicao do chao -- ver
		// `LocalPlayer.AplicarAltura`). Carimbar a copia em `_corpo.GlobalPosition` deixava o rastro
		// no chao enquanto o personagem cortava o ceu: dois efeitos separados por meia tela, que foi
		// o que o dono viu como "o efeito de dash no fly ta bugado".
		//
		// O RUMO CONTINUA VINDO DO NO, e isso nao e descuido: o no so se move quando o corpo ANDA,
		// enquanto o visual tambem sobe e desce com a altitude. Tirar o rumo do visual poria uma
		// componente vertical falsa no borrao toda vez que o jogador subisse -- o rastro apontaria
		// pra cima com o personagem indo em frente.
		// =========================================================================================
		Largar(_visual.GlobalPosition, _ultimoRumo, Alfa, Vida);
	}

	// =====================================================================
	// O BORRAO DO ARRANQUE -- a MESMA receita, de uma vez so
	// =====================================================================

	/// <summary>Vao entre duas copias do borrao do arranque, em pixels. Menor SOBREPOE (ver `Intervalo`).</summary>
	private const float VaoDoArranque = 26f;

	/// <summary>
	/// TETO DE COPIAS num arranque. O salto mais longo que o servidor permite hoje sao 448 px (ver
	/// `AlcanceDoDashMarcado`), que sem teto dariam 17 fotos de uma vez. Doze ja fecham o vao com
	/// sobreposicao, e o teto e o que impede um alcance maior amanha de virar um pico de alocacao.
	/// </summary>
	private const int TetoDoArranque = 12;

	/// <summary>
	/// Deslocamento minimo pra valer um borrao. Abaixo disso o "salto" e o passinho de ajuste do
	/// `Aproximar` (`DeslocamentoMinimo = 16` no servidor) e nao ha trajeto que explicar.
	/// </summary>
	private const float MinimoDoArranque = 20f;

	/// <summary>Opacidade da copia mais NOVA do arranque. Acima da corrida: sao poucas e passageiras.</summary>
	private const float AlfaDoArranque = 0.34f;

	/// <summary>Quanto o rastro do arranque dura. Curto -- ele conta um gesto, nao um estado.</summary>
	private const double VidaDoArranque = 0.13;

	/// <summary>
	/// O CORPO SALTOU DE `de` ATE AQUI -- larga o rastro do trajeto inteiro, de uma vez.
	///
	/// ============================ POR QUE ISTO EXISTE, E POR QUE E AQUI ============================
	/// O dono: *"npcs quando usam DASH n ficam com o EFEITO DE BLUR igual os jogadores"*.
	///
	/// O <see cref="Definir"/> logo acima e o rastro de CORRER: um estado continuo, alimentado quadro a
	/// quadro pelo bit `EntityState.Correndo`. O jogador so via borrao no arranque por COINCIDENCIA --
	/// a mesma tecla SHIFT que faz o golpe ser Pesado tambem liga a corrida. O NPC nunca segurou SHIFT
	/// nenhum: o cerebro so pede `Correndo` na fuga, e medido deu **0 em 1000** comandos de perseguicao.
	///
	/// O arranque nao e um estado, e um EVENTO -- e ele acontece INTEIRO entre dois quadros. Nao ha
	/// deslocamento por quadro pra o `_Process` fotografar: o corpo estava la, e agora esta aqui. Entao
	/// o rastro dele nao pode ser amostrado, tem que ser RECONSTRUIDO a partir dos dois pontos.
	///
	/// E ELE E O UNICO DESENHO QUE EXPLICA O TRAJETO. Sem borrao, um salto de 268 px e um corpo que
	/// simplesmente aparece -- e teleporte sem rastro le como mais longe do que o mesmo salto com
	/// rastro. E parte do segundo relato do dono, nao so do primeiro.
	///
	/// QUEM CHAMA E O SERVIDOR, pelo `S2C.Zanzo`, pra TODO corpo que se deslocou -- sem `if` de tipo.
	/// Ver `World.AoPiscar`.
	/// ============================================================================================
	/// </summary>
	/// <param name="de">De onde o corpo saiu, em coordenada de CHAO (a que o servidor conhece).</param>
	public void Arranque(Vector2 de)
	{
		// O `_Ready` pode nao ter rodado: este pacote chega por rede e o corpo pode ter acabado de
		// entrar na zona. Sem visual nao ha o que fotografar, e perder um borrao custa um efeito.
		if (_visual == null || _corpo == null) return;

		// ============================ O RASTRO SO PODE SER DESENHADO NO QUADRO EM QUE O CORPO JA CHEGOU ============================
		// Este anuncio traz UMA ponta do trajeto (de onde o corpo saiu). A outra -- onde ele chegou --
		// vem por OUTRO pacote: a `S2C.Correction` pro corpo local, o snapshot pro remoto. E o
		// LiteNetLib nao garante ordem entre canais, e o snapshot pode estar ate um tique atras.
		//
		// Desenhar aqui mesmo apostaria que a chegada ja aconteceu. Quando ela nao tivesse acontecido,
		// as duas pontas seriam o MESMO ponto: `dist` daria zero e o borrao simplesmente nao sairia --
		// um efeito que some sozinho de vez em quando, que e a pior especie de defeito visual.
		//
		// (E a mesma familia de armadilha que o `LocalPlayer.DeixarVulto` ja documenta -- so que o
		// vulto e UM carimbo na origem e nao depende do destino, entao ele era imune e o borrao nao e.)
		//
		// Entao o pedido fica PENDENTE e o `_Process` o resolve no primeiro quadro em que o corpo
		// realmente saiu do lugar. Zero bytes a mais no fio.
		// ==================================================================================================================
		_arranqueDe = de;
		_arranquePrazo = PrazoDoArranque;
		SetProcess(true);
	}

	/// <summary>De onde o salto pendente saiu (chao), ou nulo se nao ha nenhum. Ver <see cref="Arranque"/>.</summary>
	private Vector2? _arranqueDe;

	/// <summary>Quanto tempo ainda se espera a posicao de CHEGADA do salto pendente.</summary>
	private double _arranquePrazo;

	/// <summary>
	/// Quanto se espera pela posicao de chegada, em segundos.
	///
	/// Um tique de mundo sao 33 ms; este prazo aguenta sete deles. Curto de proposito: passado ele, o
	/// que quer que tenha movido o corpo ja nao e este salto, e um rastro que aparecesse meio segundo
	/// depois do gesto seria pior do que rastro nenhum.
	/// </summary>
	private const double PrazoDoArranque = 0.25;

	/// <summary>
	/// O SALTO PENDENTE, resolvido no primeiro quadro em que o corpo ja esta no destino.
	///
	/// DESISTE EM SILENCIO quando o prazo vence sem deslocamento: ou o pacote de chegada se perdeu (o
	/// snapshot vai por canal nao confiavel), ou o corpo voltou pro lugar. Nos dois casos nao ha
	/// trajeto pra contar, e insistir desenharia uma faixa entre dois pontos sem relacao.
	/// </summary>
	private void ResolverArranque(double delta)
	{
		if (_arranqueDe is not { } de || _visual == null || _corpo == null) return;

		// ============================ A ALTURA DO DESENHO ENTRA NOS DOIS PONTOS ============================
		// O servidor so conhece a posicao de CHAO -- ele nao tem altitude no `Vec2`. O corpo, voando, e
		// desenhado ate 160 px acima do no. Carimbar o comeco do rastro na coordenada crua deixaria a
		// ponta do borrao no chao e a outra no ceu: uma diagonal que ninguem percorreu.
		//
		// A subida e a MESMA nos dois extremos (o corpo nao muda de altitude durante o salto), entao
		// somar o desnivel do no ao ponto de partida poe o rastro inteiro na altura em que o corpo esta.
		// =================================================================================================
		Vector2 subida = _visual.GlobalPosition - _corpo.GlobalPosition;
		Vector2 comeco = de + subida;
		Vector2 fim = _visual.GlobalPosition;

		Vector2 d = fim - comeco;
		float dist = d.Length();

		if (dist < MinimoDoArranque)
		{
			_arranquePrazo -= delta;
			if (_arranquePrazo <= 0) _arranqueDe = null;   // a chegada nunca veio
			return;
		}

		_arranqueDe = null;

		Vector2 rumo = d / dist;
		int copias = Mathf.Clamp(Mathf.RoundToInt(dist / VaoDoArranque), 3, TetoDoArranque);

		for (int i = 0; i < copias; i++)
		{
			// `t` vai de 0 (onde o corpo saiu) a quase 1 (colado nele). A ultima copia NAO cai em
			// cima do corpo de proposito: uma foto opaca exatamente sobre o boneco vivo le como
			// borda dupla, e nao como rastro.
			float t = i / (float)copias;

			// A PONTA VELHA E A MAIS FRACA. Rastro de intensidade uniforme le como fila de clones --
			// e a mesma licao que calibrou a corrida (ver os tres numeros la em cima). A rampa e o
			// que da o SENTIDO do movimento sem precisar de nenhuma seta.
			float alfa = AlfaDoArranque * Mathf.Lerp(0.3f, 1f, t);

			// E ELA TAMBEM MORRE PRIMEIRO: o rastro se RECOLHE na direcao do corpo em vez de sumir
			// inteiro num quadro, que e o que faz o olho ler "ele veio de la" e nao "piscou".
			double vida = VidaDoArranque * Mathf.Lerp(0.55f, 1f, t);

			Largar(comeco + d * t, rumo, alfa, vida);
		}
	}

	// =====================================================================
	// A FOTO -- o unico lugar que cria uma copia do corpo
	// =====================================================================
	/// <summary>
	/// Larga UMA copia congelada do corpo, borrada no <paramref name="rumo"/>, que esmaece sozinha.
	///
	/// ISTO E O RASTRO INTEIRO, e os dois donos (a corrida e o arranque) so escolhem ONDE, QUAO FORTE e
	/// POR QUANTO TEMPO. Foi extraido do <see cref="_Process"/> quando o arranque apareceu: duas copias
	/// da mesma receita significariam que a proxima correcao de borrao (e ja houve tres) acertaria uma
	/// so, e a diferenca apareceria como "o dash borra diferente da corrida".
	/// </summary>
	private void Largar(Vector2 onde, Vector2 rumo, float alfa, double vida)
	{
		if (_visual == null || _corpo == null) return;

		// A FOTO VAI NO PAI DO CORPO, nao neste node: ela precisa FICAR onde foi tirada enquanto o
		// corpo segue andando. Filha, ela viajaria junto -- e rastro que acompanha o dono e so um
		// borrao grudado, que e de novo o efeito errado.
		if (_corpo.GetParent() is not { } palco) return;

		// SEM TINTA: cada camada desta foto recebe o material de BORRAO tres linhas abaixo. Pedir a
		// tinta aqui seria criar um `ShaderMaterial` por camada pra descarta-lo no mesmo quadro --
		// 120 por segundo, por corpo correndo.
		Node2D foto = _visual.Fotografar(comTinta: false);
		foto.GlobalPosition = onde;

		// CADA COPIA VAI BORRADA no rumo do movimento. E o que faltava: copia NITIDA le como
		// copia, por mais transparente que seja. Borrada, ela nao tem borda pra o olho fixar --
		// e tres delas sobrepostas viram um rastro so, que e o que "motion blur" quer dizer.
		//
		// O borrao mora na COPIA e nao no corpo vivo de proposito: a copia congela o quadro em que
		// nasceu, entao trocar de quadro de animacao nao muda nada do que ja foi desenhado. Foi
		// exatamente isso que fez a primeira versao (borrao no corpo) estroboscopar.
		foreach (Node n in foto.GetChildren())
			if (n is Sprite2D sp) BorraoDirecional.Aplicar(sp, rumo, 1f);
		foto.ZIndex = _corpo.ZIndex - 1;   // atras do corpo: o rastro nao pode tapar o personagem
		foto.Modulate = new Color(1, 1, 1, alfa);

		// ============================ A ETIQUETA QUE A BANCADA DE FOTO LE (`--diagborrao`) ============================
		// UMA LINHA, e ela e o que separa "o borrao foi ligado" de "eu escrevi que ele foi". A bancada de
		// numero (`--borraoteste`) mede o pacote no servidor e **fecharia verde com o borrao nunca
		// desenhado**: entre o `S2C.Zanzo` e o pixel ha o fio, o `World.AoPiscar`, a escolha da origem e
		// o `ResolverArranque` esperando a chegada.
		//
		// A `--diagborrao` fotografa a tela DUAS VEZES com a arvore PAUSADA -- uma com estas copias e
		// uma com elas escondidas -- e a diferenca de pixel e o rastro, sem nenhuma outra coisa da cena
		// entrando na conta (a mesma receita da `--diagboca`). Pra esconder e preciso ACHAR, e uma copia
		// solta no palco nao tem alca nenhuma: ela nasce, esmaece por um Tween e se libera.
		//
		// GRUPO E NAO NOME porque o nome do node e do palco (dois filhos nao podem repetir), e num
		// arranque nascem ate doze de uma vez. Custa um `HashSet.Add` por copia.
		foto.AddToGroup(GrupoDoRastro);
		palco.AddChild(foto);

		// APAGA SOZINHA, sem node de controle. Um Tween por foto e mais barato que um _Process por
		// foto, e o `QueueFree` no fim evita o vazamento que um node orfao daria.
		Tween t = foto.CreateTween();
		t.TweenProperty(foto, "modulate:a", 0f, vida).SetEase(Tween.EaseType.In);
		t.TweenCallback(Callable.From(foto.QueueFree));
	}
}
