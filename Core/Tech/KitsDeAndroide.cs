namespace Jandirus.Core.Tech;

/// <summary>
/// O KIT DE REPARO E O KIT MECANICO -- `obj/items/Repair_Kit` e `obj/items/Mechanical_Kit` (`Code/Modules/Tech/Cyborgs.dm:147-180`).
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// O KIT DE REPARO conserta os membros ARTIFICIAIS de quem o usa: *"This will take a moment, don't move or take
/// further damage!"*, dez segundos parado e sem apanhar, e cada membro artificial ferido ganha dez de vida
/// (`C.health += 10`); o kit se gasta. O KIT MECANICO instala e tira os MODULOS de ciborgue (`obj/Modules`) do corpo
/// de alguem.
///
/// ============================ A REGRA DO DONO (2026-10-13), QUE E A QUE VALE ============================
/// *"Nao existe artificial body parts nessa versao, os itens q reparavam essas partes agr servem pra curar a vida e
/// as partes dos androides q absorvem energia (os q custam 1 milhao de zeni) pq eles n tem como regenerar em maquina
/// de regeneração (ainda n botei isso no jogo entao faça isso q androides q absorvem energia nao curam na maquina de
/// regeneração, androides de energia infinita q custa 2 milhoes regeneram normalmente)"*.
///
/// Entao, neste port:
///   * O ANDROIDE DE ABSORCAO NAO SE CURA NO REGENERADOR (`GameServer.PulsoDosTanques`): o tanque nao faz nada por
///     ele -- nem vida, nem Ki, nem membro --, e nao gasta bateria com ele. O de energia infinita se cura la como
///     todo mundo.
///   * O KIT DE REPARO E A CURA DELE: o mesmo gesto do original (dez segundos parado e sem apanhar), e cada parte
///     ferida do corpo ganha dez de vida. So serve a androide de absorcao.
///   * O KIT MECANICO REFAZ UM MEMBRO PERDIDO DELE -- o que o tanque faria com a melhoria de ferimentos, e que ele nao
///     pode usar. O dele mesmo, ou o de um androide de absorcao caido a frente (o `Install_Module(mob/T in view(1))`
///     de la tambem mexia no corpo de outro). **Esta divisao entre os dois kits e leitura minha da frase do dono**
///     (*"curar a vida e as partes"*): o kit de reparo cura, o mecanico devolve o que caiu. O membro perdido de vez
///     (`BodyPart.Perdido`) nao volta nem por ele.
///   * OS MODULOS DE CIBORGUE nao existem nesta versao, e o kit mecanico nao instala nada.
/// ==================================================================================
/// </summary>
public static class KitsDeAndroide
{
	public const string Reparo = "Repair_Kit", Mecanico = "Mechanical_Kit";

	public const string AcaoReparar = "kit_reparar", AcaoRefazer = "kit_refazer";

	/// <summary>`C.health += 10`: o que cada parte ferida ganha de um kit de reparo.</summary>
	public const double VidaPorKit = 10;

	/// <summary>`sleep(100)`: os dez segundos parado e sem apanhar.</summary>
	public const double SegundosParado = 10;

	/// <summary>De quanto em quanto tempo o tanque explica ao androide de absorcao que nao o cura.</summary>
	public const double SegundosEntreAvisosDoTanque = 30;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X8): o androide de absorcao se cura no regenerador, como antes da regra do dono.</summary>
	public static bool AbsorcaoCuraNoTanqueDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X8): os kits servem a qualquer corpo.</summary>
	public static bool QualquerUmUsaDeTeste;

	// ---- (2026-10-26) O UPGRADE -- o pedido 95c do dono ----
	/// <summary>
	/// O ANDROID UPGRADE: o que solta o banco de potencial do androide de absorcao -- o dono: *"os androides de absorçao eles tem uma pool de
	/// hidden q so libera com tech q da um 'upgrade' neles liberando esse potencial, nao pelos metodos tradicionais"*; e as escolhas dele
	/// (2026-10-26): um ITEM DA ABA TECH (100 mil zeni, tecnologia 40) que o androide usa em si -- dez segundos parado e sem apanhar, como os
	/// kits --, soltando 1x o banco (`Stats.GainKnobs.LiberaOUpgrade`), com a hora de recarga dos outros liberadores. O Guru, a Agua, o
	/// desejo e o Unlock de um jogador continuam recusando o androide de absorcao; o de energia infinita os usa como todo mundo, e o upgrade
	/// nao e dele.
	/// </summary>
	public const string Upgrade = "Android_Upgrade";
	public const string AcaoUpgrade = "kit_upgrade";
}
