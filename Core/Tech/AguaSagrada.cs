namespace Jandirus.Core.Tech;

/// <summary>
/// A AGUA ULTRA DIVINA DO MIRANTE -- o `/obj/Sacred_Water` do original (`Code/Modules/Ranks/ordered/
/// EarthRanks.dm:181-231`), que o item 11 do playtest de 2026-10-10 pediu pelo nome do anime: *"Ultra Divine
/// Water no Lookout: existia no BYOND e falta aqui. Porte o comportamento original (efeito, requisitos, limite
/// de uso, riscos)."*
///
/// NO DM ELA SE CHAMA "SACRED WATER", e e a mesma coisa: nao ha "Ultra Divine Water" em nenhum `.dm`, e o
/// unico jarro do Mirante (`Maps/1to26.dmm`, celula 142,62 do z 12, area `Lookout/Inside`) e este -- a agua
/// que mata quem nao aguenta e desperta poder em quem aguenta, que e a Chōshinsui do anime.
///
/// ============================ (2026-10-26) O QUE ELA FAZ AGORA: O BANCO DO POTENCIAL, COMO NO DU ============================
/// O dono (o pedido 95): ela SOLTA o banco do potencial 1x (`GainKnobs.LiberaAAgua`) e POE nele 10% do BP base, minguando a cada gole
/// (`GainKnobs.AguaPoeNoBanco`, o `Skills.dm:2770-2773` do DU) -- e se bebe de novo, de hora em hora (*"repetiveis, 1h de recarga e a
/// agua pode matar"*): o risco de morte vale a cada gole. O vigor maximo sobe so na PRIMEIRA que se aguenta. O que esta abaixo e o DM,
/// que ela era ate 2026-10-25 (uma vez na vida, e um quinto do teto pessoal em BP).
/// ==================================================================================================================================
///
/// ============================ O QUE O VERB `Drink()` FAZ, LINHA A LINHA ============================
///   * UMA VEZ POR PERSONAGEM: `mob/var/sacredwater`. Quem ja bebeu e sobreviveu so sente gosto de agua.
///   * O RISCO: `if(prob(50) || usr.Ephysdef >= 1)`. Quem tem a resistencia fisica em 1 ou mais SEMPRE
///     aguenta; abaixo disso e meio a meio -- e quem perde a moeda MORRE (`usr.Death()`).
///   * A MARCA SO E GASTA POR QUEM AGUENTA (`usr.sacredwater = 1` e a primeira linha do ramo do sucesso):
///     quem morreu pode voltar e tentar de novo.
///   * O QUE ELA DA: tres falas com 3 s entre elas, o corpo desaba (`KO`), 30 s no chao, acorda, e 3 s depois
///     `maxstamina *= 1.2` e -- so se o BP estiver abaixo do proprio teto (`BP &lt; relBPmax`) -- `BP +=
///     capcheck(BPMod * relBPmax) / 5`. Quem ja esta no teto ouve que e forte demais pra tirar algo da agua.
///   * O QUE O VERB `Description()` DIZ: o que ela da e o que ela cobra, antes de alguem beber.
/// ===================================================================================================
///
/// Os numeros ficam aqui, com nome, pra quem quiser afinar; quem conduz as falas e o relogio e o servidor
/// (`GameServer.AguaSagrada.cs`).
/// </summary>
public static class AguaSagrada
{
	/// <summary>O id da obra no catalogo (`construcoes.json`) e no `.objetos` do Mirante.</summary>
	public const string Tipo = "Sacred_Water";

	/// <summary>Os dois verbs do jarro: `Drink()` e `Description()`.</summary>
	public const string VerboBeber = "agua_beber", VerboSobre = "agua_sobre";

	/// <summary>
	/// DEFEITO INJETADO (bancada `--aguateste`): a resistencia nao garante nada -- todo mundo depende da moeda.
	/// Lido pela propria <see cref="Aguenta"/>.
	/// </summary>
	public static bool SemGarantiaDeTeste;

	/// <summary>A moeda de quem nao tem a resistencia: `prob(50)`.</summary>
	public const double ChanceDeAguentar = 0.5;

	/// <summary>A resistencia fisica (`Ephysdef`) que dispensa a moeda: `usr.Ephysdef >= 1`.</summary>
	public const double ResistenciaQueGarante = 1;

	/// <summary>Os `sleep(30)` entre uma fala e a seguinte: 3 s.</summary>
	public const long MsEntreFalas = 3000;

	/// <summary>O `sleep(300)` com o corpo no chao: 30 s.</summary>
	public const double SegundosNoChao = 30;

	/// <summary>`maxstamina *= 1.2`.</summary>
	public const double GanhoDeVigor = 1.2;

	/// <summary>
	/// ESTE CORPO AGUENTA A AGUA? <paramref name="sorteio"/> e um numero em [0,1): abaixo de
	/// <see cref="ChanceDeAguentar"/> a moeda caiu a favor.
	/// </summary>
	public static bool Aguenta(double resistencia, double sorteio) =>
		sorteio < ChanceDeAguentar || (!SemGarantiaDeTeste && resistencia >= ResistenciaQueGarante);
}
