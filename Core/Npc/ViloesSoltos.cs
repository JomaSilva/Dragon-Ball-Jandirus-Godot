namespace Jandirus.Core.Npc;

/// <summary>
/// ============================ OS VILOES DE SAGA SOLTOS -- OS NUMEROS (dono, 2026-10-31, o pedido 198g) ============================
/// O dono: o chefe de saga que vence e explode Namek ou a Terra continua vivo -- o Freeza e o imperio invadem planetas (qualquer um, ate os
/// pre-feitos) com o exercito de 40 e atacam a populacao; o Cell vai atras de adversarios fortes. Eles somem com a explosao e so nascem nos
/// planetas que estao invadindo (nada de viagem pelo espaco); quem for la os encontra pra revanche (o texto inteiro esta no
/// PEDIDO-2026-10-23.md, item 198g). O que acontece no mundo e do servidor: `GameServer.ViloesSoltos.cs`.
///
/// ESCOLHAS MINHAS (a confirmar com o dono):
///   * QUEM FICA SOLTO: o chefe dos elos que explodem NAMEK e a TERRA (a categoria do pedido, literal) -- o Freeza de Namek (com o exercito),
///     o Cell e o Majin Boo. O Freeza do planeta Vegeta nao (Vegeta nao esta no pedido). O Boo nao tem jeito proprio no pedido: ele invade
///     como o Freeza, sem exercito.
///   * (2026-10-31, o pedido 200) TODO VILAO SOLTO FICA DOIS DIAS DO JOGO NUM PLANETA (<see cref="MinutosNoPlaneta"/>; o dono: *"Quando um
///     vilao invade um planeta ele fica 2 dias nele"*).
///   * (2026-10-31, o pedido 206) NAO VENCIDO NESSES DOIS DIAS, ELE SAI DO PLANETA -- o dono: *"se eles estiverem no meio da luta ai o prazo
///     estende, n estao lutando eles despawnam apos o prazo"*: o corpo e a tropa somem, e ele fica SUMIDO <see cref="MinutosSumido"/>
///     minutos ("alguns dias": tres, escolha minha) antes de atacar outro planeta -- o INVASOR sorteia entre os pre-feitos vivos e os
///     mundos sorteados onde ha gente agora; o CACADOR (o Cell) vai pro planeta do jogador online mais forte. E fica nesse ciclo ate
///     alguem o vencer. NO MEIO DA LUTA (o corpo dele brigou nos ultimos 90 s e ha gente brigando a ate <see cref="TilesDaLuta"/> tiles
///     dele), o prazo se estende ate a luta acabar.
///   * (2026-10-31, o pedido 200) ONDE ELE ESTA NAO VAI PRO CHAT DE NINGUEM: so a Patrulha Galactica sabe (a missao dos membros) e o jornal
///     do dia conta. Quem pisa no planeta dele ve a cena de chegada, como sempre.
///   * O CORPO SO EXISTE COM GENTE NO PLANETA: o primeiro jogador que pisa la o encontra (e a tropa desembarca); sem ninguem por
///     <see cref="SegundosSemNinguemPraSumir"/> s, ele e a tropa somem -- e voltam quando alguem chega.
///   * DERROTA-LO (nocaute ou morte, por um jogador) e a revanche: ele sai do jogo de vez, a tropa vai embora, e quem o derrotou ganha o
///     premio de heroi de chefe de saga (o karma e a reputacao com o planeta).
///
/// (2026-11-02, o pedido 227) O DONO MUDOU O RUMO DELES -- *"viloes como cell e freeza dao prioridade, ao vencerem suas sagas e sairem
/// invadindo planetas, pra planetas q tenham npcs [...] freeza com seu exercito e ele mesmo, e cell sozinho como normalmente"*:
///   * O PLANETA DA INVASAO E UM PLANETA COM POVO: os pre-feitos povoados (a Terra, Namek, Vegeta, Icer, Arlia, a estrela Makyo) e os mundos
///     sorteados com civilizacao (`World.Civilizacao`) da vizinhanca deles e de onde ha gente; o povo de um mundo sorteado nasce quando ele
///     nasce na memoria, ou seja, na hora em que alguem chega pra ver a invasao. So sem planeta com povo nenhum vale o sorteio de antes.
///   * O CELL (o cacador) vai atras do mais forte SO SE o planeta dele tem povo; senao, num planeta com povo, como o Freeza.
///   * O CELL E O MAJIN BOO EXPLODEM O PLANETA no fim dos dois dias (o Freeza so vai embora) -- <see cref="ExplodeOPlaneta"/>.
///   * O KID BOO NO CEU, sem lutar por <see cref="MinutosDeTedioDoKid"/> minutos, fica entediado e some (o clarao da Transmissao
///     Instantanea; a frase so diz que ele se teleporta -- o pedido 228e): vira um vilao solto como os outros, sumido, e dias depois aparece
///     num planeta com povo.
///   * TODO CHEFE DE SAGA nasce com a maestria de gravidade de <see cref="MaestriaDeGravidadeDoChefe"/>x -- a invasao de um mundo pesado
///     nao o esmaga.
/// ===============================================================================================================================
/// </summary>
public static class ViloesSoltos
{
	/// <summary>O que o vilao solto faz: invade planetas, ou caca o mais forte.</summary>
	public enum Jeito : byte { Invasor = 0, Cacador = 1 }

	/// <summary>Os elos cujo chefe vencedor fica solto, e como.</summary>
	public static readonly (string Elo, Jeito Jeito, bool ComExercito)[] Elos =
	[
		("freeza_namek", Jeito.Invasor, true),
		("cell", Jeito.Cacador, false),
		("boo", Jeito.Invasor, false),
	];

	/// <summary>
	/// (2026-11-02, o pedido 227o) OS QUE EXPLODEM O PLANETA no fim dos dois dias: o Cell e o Majin Boo -- *"cell e majin boo diferente do
	/// freeza, ao passarem os 2 dias eles vao explodir o planeta q estao atacando"*. Com gente la, pela cena da Terra (o Kamehameha do Cell, a
	/// esfera rosa do Boo); sem ninguem, o planeta explode sozinho.
	/// </summary>
	public static bool ExplodeOPlaneta(string elo) =>
		string.Equals(elo, "cell", StringComparison.OrdinalIgnoreCase) || string.Equals(elo, "boo", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// (2026-11-02, o pedido 227m) Quanto o Kid Boo aguenta o Ceu sem lutar: *"se o kid buu n lutar enquanto estiver no heaven por 20 minutos,
	/// ele vai sumir com o efeito do instant transmission"*. Minutos reais.
	/// </summary>
	public const double MinutosDeTedioDoKid = 20;

	/// <summary>(o pedido 227p) O que o Cell solto diz quando carrega o Kamehameha que explode o planeta que ele invade (o da Terra fala da Terra).</summary>
	public const string FalaDoKamehamehaDoCellSolto = "Este planeta acaba num golpe só! KA... ME... HA... ME...";

	/// <summary>
	/// (o pedido 227m) A frase do mundo quando o Kid some do Ceu -- (o pedido 228e) sem falar em Transmissao Instantanea: *"diga 'majin buu
	/// se teleporta' ou algo assim"*.
	/// </summary>
	public static string FraseDoTedioDoKid(string nome) =>
		$"{nome} ficou entediado no Céu e se teleportou... foi procurar algum lugar pra destruir e fazer o caos.";

	/// <summary>
	/// (2026-11-02, o pedido 227n) A MAESTRIA DE GRAVIDADE DE TODO CHEFE DE SAGA: 300x -- *"lembrando q boss de saga devem todos ter maestria
	/// de 300x gravidade (eles n tem bonus de acllimacao) pra n acabarem morrendo ao invadir um planeta com gravidade mt alta"*. A mais pesada
	/// que o mundo sorteia e 80x.
	/// </summary>
	public const double MaestriaDeGravidadeDoChefe = 300;

	/// <summary>O jeito do vilao solto deste elo, ou nulo (o elo nao solta ninguem).</summary>
	public static (Jeito Jeito, bool ComExercito)? DoElo(string elo)
	{
		foreach ((string e, Jeito j, bool x) in Elos)
			if (string.Equals(e, elo, StringComparison.OrdinalIgnoreCase)) return (j, x);
		return null;
	}

	/// <summary>O exercito do imperio: quantos soldados, quantos descem juntos, e de quanto em quanto tempo chega o resto (e a reposicao).</summary>
	public const int SoldadosDoImperio = 40, SoldadosNoDesembarque = 12;
	public const double SegundosEntreSoldados = 3;

	/// <summary>O molde do soldado do exercito (o mesmo da tropa de Namek).</summary>
	public const string MoldeDoSoldado = "soldado_do_freeza";

	/// <summary>Quanto todo vilao solto fica num planeta antes de passar a outro: DOIS dias do jogo (o pedido 200; era um).</summary>
	public const double MinutosNoPlaneta = 2 * 24;

	/// <summary>
	/// (2026-10-31, o pedido 206) Quanto ele fica SUMIDO depois de sair de um planeta sem ser vencido, antes de atacar outro: tres dias do
	/// jogo -- o dono: *"dps de alguns dias eles atacam outro planeta"*. Escolha minha, a confirmar.
	/// </summary>
	public const double MinutosSumido = 3 * 24;

	/// <summary>(o pedido 206) A que distancia (tiles) de gente brigando o vilao conta como "no meio da luta" -- e o prazo dele se estende.</summary>
	public const float TilesDaLuta = 20;

	/// <summary>Sem ninguem no planeta por este tanto, o corpo e a tropa somem (e voltam quando alguem chega).</summary>
	public const double SegundosSemNinguemPraSumir = 60;

	/// <summary>A marca do elo da tropa de um vilao solto (o registro dos soldados): "solto:" + o elo.</summary>
	public const string PrefixoDaTropaSolta = "solto:";
}
