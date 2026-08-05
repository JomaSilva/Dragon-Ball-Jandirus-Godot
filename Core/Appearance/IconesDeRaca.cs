namespace Jandirus.Core.Appearance;

/// <summary>
/// O RETRATO DE CADA RAÇA -- o card que a criação de personagem mostra.
///
/// ============================ POR QUE ELE NAO SAI DO CATALOGO DE CORPOS ============================
/// A primeira versao perguntava ao `VisualCatalog` qual o corpo masculino da raça e usava aquilo. O
/// resultado esta na foto que o dono mandou: Saiyan, Tsujin, Saibamen, Heran, Meta e Alien
/// apareceram todos como o MESMO humano pálido.
///
/// E o catálogo estava certo -- ele só conhece as raças que têm passo de escolha de corpo, e essas
/// seis não têm. O que faltava era outra pergunta: "com que cara esta raça se apresenta?", que no
/// original é um campo próprio (`/obj/race`, com `icon` por raça) e não o corpo customizável.
///
/// A tabela abaixo é a transcrição desse campo (`CreationUI.dm:328-338` monta os cards a partir
/// dele, com dois overrides declarados no próprio DM).
/// ====================================================================================================
/// </summary>
public static class IconesDeRaca
{
	private const string Base = "res://Assets/Sprites/Character Icons/";

	/// <summary>
	/// O sprite do card, ou nulo pra cair no corpo padrão.
	///
	/// OS NOMES SÃO OS DO PORT ("Icer", "Saibaman", "Kanassa"), e não os do DM ("Frost Demon",
	/// "Saibamen", "Kanassa-Jin") -- as duas grafias circulam, e quem casa aqui é a do `races.json`.
	/// </summary>
	public static string? De(string raca) => raca switch
	{
		// O DM sobrescreve o card do Saiyajin pro corpo-base padrão de propósito: o `/obj/race`
		// dele tem o ícone de um NPC específico, que não representa a raça.
		"Saiyan" or "Human" or "Halfbreed" or "Demigod" => Base + "NewPaleMale.tres",

		"Icer" => Base + "Frost Demons/Changling - Form 1.tres",
		"Namekian" => Base + "Namekians/Namek Young.tres",
		"Majin" => Base + "Majins/Majin1.tres",
		"BioAndroid" => Base + "Bioandroids/Bio Android 1.tres",
		"Arlian" => Base + "Misc/Arlian.tres",
		"Meta" => Base + "MetaM.tres",
		"Kanassa" => Base + "Kanassan.tres",
		"Makyo" => Base + "Makyojin.tres",
		"Yardrat" => Base + "Yardrat.tres",
		"Heran" => Base + "spacepirate.tres",
		"Gray" => Base + "Jiren.tres",
		"Shapeshifter" => Base + "cat.tres",
		"SpiritDoll" => Base + "Spirit Doll.tres",
		"Demon" => Base + "Demons/Demon - Form 1.tres",

		// Kai e Alien dividem o mesmo corpo no original -- os dois usam o Konatsu.
		"Kai" or "Alien" => Base + "Aliens/Konatsu.tres",

		"Saibaman" => "res://Assets/Sprites/NPCs/Saibaman - Form 1.tres",
		"Android" => "res://Assets/Sprites/NPCs/GochekAndroid.tres",

		// Tsujin não tem ícone próprio no DM: cai no corpo padrão, como lá.
		_ => null,
	};
}
