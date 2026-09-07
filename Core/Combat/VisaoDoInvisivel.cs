namespace Jandirus.Core.Combat;

/// <summary>
/// QUEM ENXERGA O QUE NASCE INVISIVEL -- o `see_invisible` do BYOND, reduzido ao unico nivel que este
/// port emite: a lamina de ar do Kiai (`Code/Modules/Skills/Ki/Ki2.0/Kiai.dm:40`, `A.invisibility = 1`).
///
/// ============================ POR QUE ISTO E UMA TABELA DE RACAS ============================
/// No BYOND `invisibility` e um numero do objeto e `see_invisible` um numero do mob: o objeto some
/// pra quem tem o numero menor. O mob nasce com `see_invisible = 0`, e o jogo so sobe pra 1 em seis
/// lugares, todos de RACA (a "percepcao de ki" que a lamina de ar existe pra testar):
///
///     statnamek.dm:9          Namekian            see_invisible = 1
///     statkanassa.dm:5        Kanassa             see_invisible = 1
///     statspirit.dm:5         Spirit              see_invisible = 1
///     statshapeshifter.dm:5   Shapeshifter        see_invisible = 1
///     statyardrat.dm:14       Yardrat             see_invisible = 1
///     statdemi.dm:48          Demigod, so a linhagem "Genie"
///
/// Entao um Humano que solta o Kiai no vazio NAO VE a propria lamina -- so ouve o sopro e ve o
/// oponente cair --, e o Namekian do lado ve o `Daitoppa.dmi` tingido passar. E o que o original
/// fazia, e e o que o dono pediu de volta ("veja como era no BYOND", 2026-09-07).
///
/// O QUE FICOU DE FORA, declarado: o perk "See Invisible" do Alien (`statalien.dm:54`, uma escolha
/// de `statboosts` que este port nao tem) e o toggle de admin (`Admin.dm:337-338`, `see_invisible =
/// 50`), porque o admin do port nao carrega esse numero. Os dois entram aqui no dia em que existirem,
/// e so aqui -- este e o unico lugar que responde a pergunta.
/// ==========================================================================================
/// </summary>
public static class VisaoDoInvisivel
{
	/// <summary>
	/// ESTA PESSOA ENXERGA A LAMINA DE AR? Pura: raca e linhagem, nada de estado. A <paramref name="classe"/>
	/// e a linhagem do Demigod ("Genie"/"Ogre"/...), o mesmo campo que escolhe a bola racial dele
	/// (`ArteDeProjetil.Bola`).
	/// </summary>
	public static bool Enxerga(string? raca, string? classe) => raca switch
	{
		"Namekian" or "Kanassa" or "Spirit" or "Shapeshifter" or "Yardrat" => true,
		"Demigod" => string.Equals(classe, "Genie", System.StringComparison.Ordinal),
		_ => false,
	};
}
