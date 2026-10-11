using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// QUEM ESTA NUMA SALA DO TEMPO E CONTADO PELA SESSAO -- a da Terra e as de planeta, pela mesma regua.
///
/// ============================ O FURO (medido pela `--salapresencateste`, 2026-10-11) ============================
/// A vaga da Sala da Terra (`_naSala`) e, com ela, a SESSAO (quem o tique conta) saiam com a ZONA do corpo. So que
/// ha quatro jeitos de trocar de zona sem sair da Sala: mergulhar na mente (o corpo fica deitado la, e quem viaja
/// e o `ServerPlayer`), virar PASSAGEIRO de uma fusao (vai pro bolso `Selado` do dono), ser ABSORVIDO por um Majin
/// (vai pro interior dele) e ser selado. O tique via "este corpo nao esta mais no z13", soltava a vaga -- e quem
/// voltava ficava na Sala SEM sessao, enquanto o funil de ritmo (`SessaoDaSalaValendo`) so perguntava pelos dias:
///
///   * acordou do transe com meio dia gasto: 280x por TRES dias in-game de tique, a conta parada em 0,5, a idade
///     parada, a janela nunca armada, a vaga livre (a lotacao contava zero);
///   * o passageiro que se separava la dentro: igual;
///   * a CARONA -- o passageiro de quem atravessou a porta, sem chave e sem recarga: 280x sem um dia contado; e
///     relogando la dentro ganhava vaga e sessao inteira (o login dava vaga a qualquer um que acordasse no z13);
///   * fundir ou ser absorvido na janela de 2 minutos: a tranca nunca fechava nele.
/// O DM nao tem nada disto: la a Sala expulsa em vez de prender, e o transe, a fusao por cena e a absorcao selada
/// sao deste port (ou das regras do dono). E a regra escrita num lugar (a zona do corpo) e esquecida nos outros.
/// ================================================================================================================
///
/// ============================ A REGRA, PELA RAIZ ============================
///   1. A SESSAO E DO PERSONAGEM, e nao de uma lista: quem ATRAVESSOU A PORTA leva a marca dela
///      (`ServerPlayer.SalaPlaneta`: o nome do mundo numa sala de planeta, <see cref="SalaDoTempo.PortaDaTerra"/> na
///      do Templo). Vai pro save: relogar la dentro continua a sessao de quem a tem, e nao da uma a quem nao tem.
///   2. SO RENDE QUEM TEM A SESSAO ABERTA E ESTA DE PE NA SALA. Quem esta la dentro sem ter atravessado a porta
///      (a carona, o corpo posto por admin) rende 1x, nao e contado e nao tem prazo -- nas duas salas.
///   3. A SESSAO SO FECHA QUANDO O CORPO SAI DA SALA DE VERDADE (<see cref="LugarFisicoDe"/>). Num BOLSO la dentro
///      -- em transe, passageiro de quem esta la, absorvido por quem esta la -- a vaga continua dele, a conta de
///      dias e a idade PARAM (ele nao esta treinando) e continuam de onde pararam na volta.
///   4. NA TERRA A JANELA E A TRANCA NAO PARAM: elas sao relogio de parede, e correm tambem pra quem esta num
///      bolso la dentro. Fundir na janela nao escapa -- o passageiro fica PRESO dentro do outro, e quem o carrega
///      nao sai (`PresoQueEsteCorpoCarrega`, a rede que as validacoes ja tinham).
/// As regras do dono pra Sala da Terra nao mudaram: nao expulsa, prende; dois minutos de janela; o Guardiao, do
/// Templo, solta. A LOTACAO continua sendo a das sessoes abertas (`_naSala`: quem atravessou a porta e nao saiu
/// de verdade, inclusive quem caiu) -- a carona nao rende e nao ocupa vaga.
/// ============================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada --salapresencateste): a vaga e a sessao da Sala voltam a sair com a ZONA do corpo --
	/// mergulhar na propria mente, virar passageiro de uma fusao ou ser absorvido por um Majin la dentro solta a
	/// vaga e fecha a sessao. Lido pela propria regra, em <see cref="NaSalaDaTerraDeVerdade"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool VagaSaiNoBolsoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --salapresencateste): a janela de saida e a tranca nao andam pra quem esta num bolso
	/// dentro da Sala (o passageiro, o absorvido, quem esta em transe) -- fundir durante a janela escapa da prisao.
	/// Lido pela propria regra, no `TickDaSessaoDaSala`. Falso em jogo, sempre.
	/// </summary>
	internal bool TrancaNaoFechaNoBolsoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --salapresencateste): quem ENTRA NO MUNDO ja dentro da Sala da Terra ganha vaga e
	/// sessao, tenha ou nao atravessado a porta -- relogar la dentro da uma sessao inteira a quem entrou de carona.
	/// Lido pela propria regra, no `SalaDoTempoNoLogin`. Falso em jogo, sempre.
	/// </summary>
	internal bool LoginDaVagaSemPortaDeTeste;

	// =====================================================================
	// A SESSAO E A MARCA DA PORTA NO PERSONAGEM
	// =====================================================================
	/// <summary>Este personagem atravessou a porta do TEMPLO e ainda nao saiu da Sala da Terra de verdade?</summary>
	private static bool TemASessaoDaTerra(ServerPlayer pl) =>
		string.Equals(pl.SalaPlaneta, SalaDoTempo.PortaDaTerra, StringComparison.Ordinal);

	/// <summary>Este personagem atravessou a porta de um MUNDO SORTEADO e ainda nao saiu da sala dele de verdade?</summary>
	private static bool TemSessaoDePlaneta(ServerPlayer pl) => pl.SalaPlaneta.Length > 0 && !TemASessaoDaTerra(pl);

	// =====================================================================
	// ONDE O CORPO ESTA DE VERDADE
	// =====================================================================
	/// <summary>
	/// A ZONA EM QUE ESTE CORPO ESTA FISICAMENTE: a dele -- ou, quando ele esta num BOLSO, a de quem (ou do que) o
	/// carrega. Sao os quatro bolsos deste port, cada um com o seu fio de volta:
	///
	///   * a MENTE (a propria ou a de quem meditava ao lado): o corpo e o boneco largado no chao
	///     (`ServerPlayer.BonecoLargado`). Sem boneco ele foi POSTO la por fora (um admin): saiu de onde estava;
	///   * o bolso da FUSAO (`ZonaDoSelo(id do dono)`): onde o dono estiver;
	///   * o SELO (o mesmo bolso, com seed zero): pra onde o selado volta (`Selo.ZonaDeVolta`);
	///   * o interior de um MAJIN: onde o Majin estiver.
	/// O dono de uma fusao pode estar em transe, e um Majin pode ser passageiro: a pergunta desce, com teto.
	///
	/// QUEM CRIAR UM BOLSO NOVO (um interior em que o corpo entra sem sair do lugar) poe a linha dele aqui -- e a
	/// Sala, o que mais perguntar "ele saiu?" e a bancada passam a enxerga-lo.
	/// </summary>
	private ZoneKey LugarFisicoDe(ServerPlayer pl, int fundura = 0)
	{
		ZoneKey z = pl.Zone;
		if (fundura >= 4 || z.Kind != ZoneKey.KindInterior) return z;

		if (NaMente(pl)) return pl.BonecoLargado is { } boneco ? boneco.Zone : z;

		if (EhOSelo(z))
		{
			if (z.Seed == 0) return pl.Selo.Preso ? pl.Selo.ZonaDeVolta : z;
			return _players.TryGetValue((int)z.Seed, out ServerPlayer? dono) && dono != pl ? LugarFisicoDe(dono, fundura + 1) : z;
		}

		if (InteriorDoMajin.EhOInterior(z))
			return _players.TryGetValue(InteriorDoMajin.Anfitriao(z), out ServerPlayer? majin) && majin != pl
				? LugarFisicoDe(majin, fundura + 1)
				: z;

		return z;
	}

	/// <summary>
	/// ESTE CORPO ESTA NA SALA DA TERRA DE VERDADE -- de pe la dentro, ou num bolso la dentro? E a pergunta da VAGA
	/// e do fim da sessao; a de "esta treinando la" continua sendo o `NaSala` (a zona do corpo).
	/// </summary>
	private bool NaSalaDaTerraDeVerdade(ServerPlayer pl) =>
		VagaSaiNoBolsoDeTeste ? NaSala(pl) : SalaDoTempo.EhASala(LugarFisicoDe(pl));

	/// <summary>O mesmo pras salas de planeta: de pe numa, ou num bolso dentro de uma.</summary>
	private bool NumaSalaDePlanetaDeVerdade(ServerPlayer pl) => SalaDoTempo.EhDePlaneta(LugarFisicoDe(pl));
}
