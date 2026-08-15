using Godot;

namespace Jandirus.Client;

/// <summary>
/// A LUZ QUE UM ATAQUE DE KI LANCA NO MUNDO -- o pedido do dono foi *"beams e ataque de ki
/// deveriam ter LUZ PROPRIA"*.
///
/// ============================ ELA NAO E UM SISTEMA NOVO, E MAIS UMA FONTE ============================
/// Ja existe iluminacao dinamica neste port e ela tem tres camadas (ver <see cref="Iluminacao"/>): o
/// ambiente (`CanvasModulate`, a cor da hora), as fontes de cenario (<see cref="Fogo"/>: fogueira,
/// tocha, lava) e a luz de quem esta com a chama acesa (<see cref="Aura"/>). Um tiro de ki e a
/// quarta fonte pelo MESMO mecanismo -- `PointLight2D`, textura radial, blend aditivo --, e nao um
/// segundo caminho de luz. Isso importa: um sistema de iluminacao inteiro ja foi construido neste
/// projeto e o dono mandou REVERTER. O que ele quer nao e mais um sistema; e o tiro participando do
/// que ja esta na tela.
/// ================================================================================================
///
/// ============================ ELA E FILHA, E E POR ISSO QUE NAO SOBRA LUZ ORFA ============================
/// Este node e SEMPRE filho do que brilha (o <see cref="ProjetilDesenhado"/> ou a
/// <see cref="CargaDeRaioVisual"/>). Nasce com ele, ANDA com ele de graca -- posicao local zero,
/// nenhuma linha por quadro copiando coordenada -- e morre com ele, porque no Godot filho morre com
/// o pai. Nao ha um segundo objeto que alguem tenha que lembrar de apagar.
///
/// O projeto tem tres defeitos recentes exatamente desse feitio (aureola presa, relogio que nao
/// rearmava, pedido de musica eterno), e os tres nasceram de guardar o efeito NUM lugar e a vida
/// do efeito NOUTRO. Aqui a vida da luz E a vida do tiro, por construcao. O mesmo raciocinio ja
/// esta escrito na <see cref="CargaDeRaioVisual"/> ("filho morre com o pai, entao o corpo que sai
/// de vista leva o brilho junto").
/// ======================================================================================================
///
/// ============================ E POR QUE ELA NAO ACENDE DE DIA ============================
/// Pedido antigo do dono, sobre a aura: *"que a aura de transformacao so brilhe ao anoitecer, porque
/// de dia fica muito saturado e muito claro"*. A fisica da cena e a mesma pro tiro -- ao meio-dia o
/// ambiente ja esta perto do branco, entao somar uma luz por cima nao acrescenta brilho: estoura, e
/// o proprio efeito some dentro do borrao. A curva e UMA so pras duas
/// (<see cref="Iluminacao.ForcaDaNoite"/>), pra ninguem afinar uma e esquecer a outra.
///
/// E ela e lida UMA VEZ, no nascimento, e nao por quadro: o dia dura 24 minutos reais e um tiro vive
/// segundos. A <see cref="Aura"/> precisa de um relogio de 1 s porque um corpo fica aceso parado por
/// minutos e atravessa o entardecer; um Kamehameha nao atravessa nada.
///
/// CONSEQUENCIA QUE VALE DIZER: de dia (e nos 14 planetas que nao tem noite, como Namek) este node
/// nao chega a existir. Zero luz, zero node, zero custo -- e nao "uma luz de energia 0,02", que
/// nao aparece e mesmo assim custa uma passada por quadro.
/// =====================================================================================
/// </summary>
public partial class LuzDeKi : PointLight2D
{
	/// <summary>O nome do node. Uma casa so: a bancada e quem procura por ele.</summary>
	public const string NomeDoNode = "LuzDeKi";

	/// <summary>
	/// O RAIO DA TEXTURA, em pixel. Tres tiles.
	///
	/// E UM SO PRA TODO TIRO DO JOGO, de proposito: <see cref="Fogo.Radial"/> guarda a textura por
	/// raio, entao um unico valor quer dizer UMA textura gerada na vida do processo. Um raio que
	/// variasse com o tiro geraria um degrade novo por disparo -- e a medida do proprio cache diz o
	/// que isso custa: 2,15 ms POR LUZ so de montar. O tamanho de cada tiro sai do
	/// <see cref="Light2D.TextureScale"/>, que escala a mesma textura de graca.
	/// </summary>
	private const int RaioDaTextura = 96;

	// =====================================================================
	// O TETO
	// =====================================================================
	/// <summary>
	/// QUANTAS LUZES DE KI ESTAO ACESAS AGORA, no cliente inteiro.
	///
	/// ============================ POR QUE ESTE NUMERO PRECISA DE TETO ============================
	/// O teto de tiros de uma zona e 256 (`GameServer.Projeteis.MaxProjeteisPorZona`) e o do mundo e
	/// 1024, e o CLIENTE nao tem teto nenhum de desenho: todo tiro da zona vira node. Hoje o pico de
	/// luzes numa tela e ~25 de cenario (Vegeta) mais uma por corpo aceso; 256 luzes MOVEIS e outra
	/// ordem de grandeza, e quem pagaria seria a maquina do jogador numa briga cheia -- justamente o
	/// instante em que o quadro importa.
	///
	/// O teto sai da qualidade grafica (<see cref="Settings.LuzesDeKi"/>), que ja e o lugar onde o
	/// custo visual se ajusta. Estourar o teto nao apaga o tiro nem o efeito: so aquele tiro nao
	/// lanca luz -- ele continua desenhado, colorido e mortal.
	/// ========================================================================================
	/// </summary>
	public static int Acesas { get; private set; }

	/// <summary>Zera a conta. SO PRA BANCADA -- em jogo quem conta e a arvore.</summary>
	public static void ZerarContaDeTeste() => Acesas = 0;

	/// <summary>
	/// ESTA LUZ PEGOU VAGA? Guardado pra devolver EXATAMENTE o que se pegou: decrementar sem saber
	/// se incrementou e como um contador vaza pra sempre (e uma vez vazado, ninguem mais brilha).
	/// </summary>
	private bool _temVaga;

	private float _energia = 1f;

	/// <summary>
	/// PENDURA UMA LUZ DE KI em quem brilha. Nao devolve nada de proposito: quem chama nao guarda
	/// referencia nenhuma -- se guardasse, teria que lembrar de solta-la.
	/// </summary>
	/// <param name="dono">O node que brilha. A luz fica na posicao local ZERO dele.</param>
	/// <param name="cor">A cor do KI DE QUEM ATIROU -- a mesma que tinge o sprite. Ver o cabecalho.</param>
	/// <param name="tamanho">
	/// O `wavemult` do DM (`Projetil.EscalaVisual`): 1 num Ki Wave, 4 num Final Flash. O muro
	/// ilumina mais que o fio, que e a mesma relacao que a arte ja tem.
	/// </param>
	public static void Pendurar(Node dono, Color cor, float tamanho)
	{
		// DE DIA NAO NASCE NODE NENHUM. Ver o cabecalho: a curva e a mesma da aura, e ela e lida
		// uma vez porque um tiro nao atravessa o entardecer.
		float noite = Iluminacao.ForcaDaNoite();
		if (noite <= 0.01f) return;

		float t = Mathf.Clamp(tamanho, 1f, 6f);

		dono.AddChild(new LuzDeKi
		{
			Name = NomeDoNode,
			Texture = Fogo.Radial(RaioDaTextura),
			Color = cor,

			// ============================ A ENERGIA SAIU DA FOTO, E NAO DA INTUICAO ============================
			// A primeira versao usava a faixa da <see cref="Aura"/> (energia ~0,66) por analogia. A
			// familia 5 da bancada fotografou e o chao subiu 0,02 de luminancia: um roxo que so
			// aparece quando se sabe onde olhar. O motivo e que o passe de luz 2D do Godot multiplica
			// pelo que o chao ja tem -- luz nao cria cor onde nao ha nada pra refletir --, e um chao de
			// noite e escuro por definicao. E justamente onde esta luz precisa aparecer.
			//
			// A aura pode ser fraca: ela tem raio 160 e fica ACESA por minutos, entao um brilho baixo
			// espalhado ainda le. Um tiro passa em um segundo num raio de 72 px; se ele nao acender
			// forte, ele nao acende.
			//
			// O TETO CONTINUA EXISTINDO pelo motivo que a `Aura` documenta: energia solta lava a cena
			// num borrao claro e o proprio efeito some dentro da luz que deveria acompanha-lo. Um tiro
			// maior acende mais, so que a diferenca se mede tambem em ALCANCE, e nao so em brilho.
			// ============================================================================================
			_energia = Mathf.Clamp(1.5f + t * 0.35f, 1.5f, 2.6f) * noite,
			TextureScale = Mathf.Clamp(0.6f + t * 0.22f, 0.6f, 1.8f),

			// ============================ SEM SOMBRA, E ESTA E A LINHA QUE DECIDE O CUSTO ============================
			// A fogueira projeta sombra e pode: ela nao sai do lugar, entao o mapa de sombra dela e
			// calculado uma vez e fica. Uma luz que ANDA refaz os oclusores TODO QUADRO -- e um tiro
			// nao faz outra coisa senao andar. E a diferenca entre "mais um sprite" e "mais um passe
			// de oclusores por tiro por quadro".
			//
			// O PRECO: um tiro dentro de uma casa acende atraves da parede. Aceito de propos1to --
			// e um clarao de segundos que acompanha uma coisa que o jogador esta vendo voar, e nao
			// uma tocha parada num corredor, que e o caso que a sombra do `Fogo` existe pra resolver.
			// ====================================================================================================
			ShadowEnabled = false,

			BlendMode = Light2D.BlendModeEnum.Add,

			// ATRAS, como a fogueira e a aura: uma luz por cima lava o sprite que ela acompanha.
			ZIndex = -5,
		});
	}

	/// <summary>
	/// PEGA A VAGA AO ENTRAR NA ARVORE e devolve ao sair -- e o par que impede o contador de vazar.
	///
	/// `_EnterTree`/`_ExitTree` e nao `_Ready`: o `_Ready` roda UMA vez e o `_ExitTree` roda toda
	/// vez que o node sai. Um par desemparelhado aqui nao daria bug visivel no dia -- ele iria
	/// somando ate o teto e, dali em diante, nenhum tiro do jogo voltaria a brilhar.
	/// </summary>
	public override void _EnterTree()
	{
		_temVaga = Acesas < Settings.LuzesDeKi;
		if (_temVaga) Acesas++;

		// ZERO E ZERO: uma luz que nao pegou vaga fica DESLIGADA, e uma `PointLight2D` desligada nao
		// entra na passada de luz do quadro.
		Enabled = _temVaga;
		Energy = _temVaga ? _energia : 0f;
	}

	public override void _ExitTree()
	{
		if (_temVaga) Acesas--;
		_temVaga = false;
	}
}
