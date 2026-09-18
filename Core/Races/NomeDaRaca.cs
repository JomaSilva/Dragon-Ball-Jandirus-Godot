namespace Jandirus.Core.Races;

/// <summary>
/// O NOME DA RACA COMO O JOGADOR LE -- a ponte entre a CHAVE do port e o nome do jogo.
///
/// ============================ DUAS GRAFIAS CIRCULAM, E SO UMA E DO JOGO ============================
/// O `races.json` (e tudo que o le: `RaceCatalog`, `Fighter.Race`, o `SlotInfo` do fio, o save) chama
/// a raca pela CHAVE: "Icer", "Saibaman", "Kanassa", "BioAndroid", "SpiritDoll", "Halfbreed". Sao
/// identificadores -- sem espaco, sem hifen -- herdados dos nomes de ARQUIVO do DM (`icer.dm`,
/// `IcerTransform.dm`), e nunca foram o nome que o jogo mostra.
///
/// No DM a raca se chama "Frost Demon" nos 44 lugares em que o texto aparece (`CreationUI.dm:310`,
/// `CharacterCreation.dm:214`, `Murder.dm:97`...), e a string "Icer" como valor de raca aparece ZERO
/// vezes. Idem "Saibamen" (`CreationUI.dm:307`), "Kanassa-Jin" (`:318`), "Bio-Android" (`:288`),
/// "Spirit Doll" (`:287`) e "Half-Saiyan" (`:104`).
///
/// A tabela vivia dentro da `CreationScreen` (privada, misturada com os nomes dos planetas), e por
/// isso a tela de SLOTS mostrava "Icer" -- foi o que o dono viu (2026-09-15): *"ao ter um personagem
/// frost demon, aparece como 'icer' a raca no slot"*. A aba Stats e a aba People vazavam a mesma
/// chave. Mora no Core porque o SERVIDOR tambem fala a raca ao jogador (fusao, DNA, lista de quem
/// esta online), e uma tabela por ponta e a receita de "Frost Demon" numa tela e "Icer" na outra.
/// ==================================================================================================
/// </summary>
public static class NomeDaRaca
{
	/// <summary>A chave do `races.json` -> o nome que o jogo original mostra. Chave sem nome proprio volta como esta.</summary>
	public static string Bonito(string raca) => raca switch
	{
		"Icer" => "Frost Demon",         // `CreationUI.dm:310`
		"Saibaman" => "Saibamen",        // `CreationUI.dm:307`
		"Kanassa" => "Kanassa-Jin",      // `CreationUI.dm:318`
		"BioAndroid" => "Bio-Android",   // `CreationUI.dm:288`
		"SpiritDoll" => "Spirit Doll",   // `CreationUI.dm:287`
		"Halfbreed" => "Half-Saiyan",    // `CreationUI.dm:104`
		_ => raca,
	};

	/// <summary>
	/// ISTO E UMA CHAVE QUE TEM NOME PROPRIO? -- a pergunta que a bancada faz a cada pedaco de um
	/// rotulo desenhado: "Icer" cru numa tela e a chave vazando; "Human" cru nao e (a chave E o nome).
	/// </summary>
	public static bool EhChaveCrua(string texto) => Bonito(texto) != texto;
}
