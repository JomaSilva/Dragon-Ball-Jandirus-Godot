using Jandirus.Core.Items;

namespace Jandirus.Core.Tech;

/// <summary>
/// A BANCADA DE CRIACAO -- `obj/items/Crafting_Bench` (`Code/Modules/Tech/Crafting.dm`): *"A crafting bench lets you
/// make various items. It also lets you make clothes manually out of Zenni. (This is the only way to make custom
/// clothes!)"*
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// Aparafusada (*"You need to bolt [name] before you can use it!"*), o `Craft()` oferece:
///   * "Clothes" -- a vitrine de roupas (`ClothingChoice()`, `ClothesChoose.dm`): escolhe-se a peca, *"This will cost
///     [N] zenni, accept?"*, escolhe-se a cor, e a peca tingida vai pro inventario (`usr.contents+=objtype`);
///   * "Items", cem zeni cada: o pente (`Comb`: `Change_Hair` -- a cor e depois o penteado), a tinta de olhos
///     (`Eye_Dye`: `Change_Eye_Color`), o tear (`Loom`: uma roupa com um ICONE ENVIADO pelo jogador) e a lixeira
///     (`Trash_Bin`: destroi um item do inventario).
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * A VITRINE E A GRADE DO GUARDA-ROUPA (a mesma tela da arte da armadura e do Materializar), so com as pecas que
///     a vitrine do original vende, pelo preco de la (`pickclothes()`). Quatro pecas da vitrine de la nao tem preco
///     no codigo (Gloves, Bandana, Belt Buckle e Horns: clicar nelas nao faz nada) e ficam de fora; a "Ninja Mask2"
///     entra pelo preco da "Ninja Mask 2" -- um erro de digitacao de la, que a deixava sem preco tambem.
///   * A COR E OPCIONAL: sem tingir, a peca sai na cor do desenho. La a cor e sempre perguntada.
///   * SO EM ZENI. A regra das duas moedas (dono, 2026-10-10) e das MELHORIAS de maquina; a propria bancada diz que
///     faz roupa *"out of Zenni"*.
///   * O PENTE E A TINTA VAO PRA MOCHILA (la nascem no chao, aos pes de quem os faz), e nao se gastam -- como la.
///   * O SAIYAJIN NAO TINGE O CABELO (*"Saiyan hair created from here will always be black."*): e a regra que a
///     criacao de personagem ja tem (`VisualCatalog.CabeloNatural`).
///   * O TEAR nao veio: a unica coisa que ele faz alem da vitrine e aceitar um icone ENVIADO, e este port nao tem
///     canal de arquivo do cliente pro servidor.
///   * A LIXEIRA nao veio: neste port o que se larga no chao some sozinho (`Chao.SegundosNoChao`), que e o que a
///     lixeira fazia de proposito.
///   * A "RESEARCH STATION" da lista de itens de la nao faz nada (a opcao existe, o ramo nao): nao veio.
/// ==================================================================================
/// </summary>
public static class BancadaDeCriacao
{
	public const string Tipo = "Crafting_Bench";

	public const string VerboRoupa = "bancada_roupa", VerboItem = "bancada_item";

	/// <summary>Os dois itens que a bancada faz (`obj/items/Comb`, `obj/items/Eye_Dye`).</summary>
	public const string Pente = "Comb", TintaDeOlhos = "Eye_Dye";

	/// <summary>As acoes de mochila dos dois (`Change_Hair`, `Change_Eye_Color`).</summary>
	public const string AcaoPentear = "pentear", AcaoTingirOsOlhos = "tingir_olhos";

	/// <summary>`if(usr.zenni>=100) usr.zenni-=100`: o preco de cada item.</summary>
	public const double CustoDoItem = 100;

	private const string ArteDoPente = "res://Assets/Sprites/Misc/Objects/Items/Item - Comb.tres";
	private const string ArteDaTinta = "res://Assets/Sprites/Misc/Objects/Items/Item, Needle.tres";

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X10): a bancada trabalha solta, sem o parafuso.</summary>
	public static bool TrabalhaSoltaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X10): a bancada faz qualquer folha do guarda-roupa, e de graca.</summary>
	public static bool FazQualquerPecaDeGracaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X10): o pente tinge o cabelo do Saiyajin.</summary>
	public static bool SaiyajinTingeDeTeste;

	/// <summary>
	/// O PRECO DE CADA PECA, pela folha dela no guarda-roupa -- a tabela do `pickclothes()` (`ClothesChoose.dm:477-786`),
	/// casada com o icone de cada tipo da vitrine (`ClothingChoice()`, `:33-462`). O comentario de cada linha e o nome da
	/// peca na vitrine de la.
	/// </summary>
	private static readonly Dictionary<string, int> Precos = new(StringComparer.OrdinalIgnoreCase)
	{
		["Angel Wings"] = 777,                    // "Angel Wings"
		["BobaFett"] = 50,                        // "Boba Helmet"
		["BrolyWaistrobe"] = 10,                  // "Broly Waistrobe"
		["Clothes Arm Pads"] = 5,                 // "Arm Pads"
		["Clothes Backpack"] = 5,                 // "Backpack"
		["Clothes Daimaou"] = 10,                 // "Daimaou"
		["Clothes Guardian"] = 10,                // "Guardian"
		["Clothes Helmet"] = 100,                 // "Tech Helmet"
		["Clothes Kung Fu Shirt"] = 5,            // "Kung Fu Shirt"
		["Clothes, Demon Arm"] = 5,               // "Demon Arm"
		["Clothes, Kimono"] = 5,                  // "Kimono"
		["Clothes, Neko"] = 5,                    // "Neko"
		["Clothes, Ninja Mask"] = 5,              // "Ninja Mask"
		["Clothes, Saiyan Gloves"] = 10,          // "Saiyan Gloves"
		["Clothes, Saiyan Shoes"] = 10,           // "Saiyan Shoes"
		["Clothes_ArmBands"] = 10,                // "Arm Bands"
		["Clothes_Belt"] = 10,                    // "Belt"
		["Clothes_Book"] = 10,                    // "Book"
		["Clothes_Boots"] = 5,                    // "Boots"
		["Clothes_Bustier"] = 25,                 // "Bustier"
		["Clothes_Cape"] = 100,                   // "Cape"
		["Clothes_Cape2"] = 10,                   // "Small Cloak"
		["Clothes_CelesRobe"] = 55,               // "Celes Robe"
		["Clothes_CloudSOLDIER"] = 100,           // "SOLDIER Suit"
		["Clothes_GiBottom"] = 5,                 // "Gi Bottom"
		["Clothes_GiCustom"] = 25,                // "Cross Gi"
		["Clothes_GiTop"] = 5,                    // "Gi Top"
		["Clothes_GokuJacketandPants"] = 10,      // "Jacket and Pants"
		["Clothes_Headband"] = 5,                 // "Headband"
		["Clothes_Headband2"] = 10,               // "Windblown Headband"
		["Clothes_Hit"] = 10,                     // "Hit Robe"
		["Clothes_Hood"] = 5,                     // "Hood"
		["Clothes_Hooded Cloak 2"] = 100,         // "Hooded Cloak"
		["Clothes_HoodedCloak"] = 1300,           // "XIII Hooded Cloak"
		["Clothes_HoodlessCloak"] = 1300,         // "XIII Cloak"
		["Clothes_Jacket"] = 50,                  // "Jacket"
		["Clothes_KaioSuit"] = 30,                // "Kai Suit"
		["Clothes_KingVegetaCape"] = 100000,      // "Vegeta Cape"
		["Clothes_LongSleeveShirt"] = 20,         // "Long Sleeve Shirt"
		["Clothes_NamekianScarf"] = 5,            // "Namekian Scarf"
		["Clothes_Pants"] = 10,                   // "Pants"
		["Clothes_Ronin"] = 25,                   // "Ronin"
		["Clothes_SaiyanMask"] = 10,              // "Red and Blue Mask"
		["Clothes_SaiyanSuit"] = 10,              // "Saiyan Suit"
		["Clothes_Sash"] = 5,                     // "Sash"
		["Clothes_Scarf"] = 10,                   // "Long Scarf"
		["Clothes_Shades"] = 50,                  // "Shades"
		["Clothes_ShadesCool"] = 10,              // "Large Sunglasses"
		["Clothes_Shoes"] = 5,                    // "Shoes"
		["Clothes_ShortSleeveShirt"] = 10,        // "Short Sleeve Shirt"
		["Clothes_TankTop"] = 5,                  // "Tank Top"
		["Clothes_TankTopandPants"] = 10,         // "Tank Top and Pants"
		["Clothes_TimePatrolCoat"] = 10,          // "Time Patrol Coat"
		["Clothes_Turban"] = 5,                   // "Turban"
		["Clothes_TurtleSuit"] = 10,              // "Turtle Suit"
		["Clothes_WaistRobe"] = 5,                // "Waist Robe"
		["Clothes_WarriorPants"] = 10,            // "Warrior Pants"
		["Clothes_Wristband"] = 5,                // "Wristband"
		["ClothesAssassin"] = 10,                 // "Assassin Suit"
		["ClothesAssassinHoodless"] = 10,         // "Assassin Hoodless"
		["ClothesDaimaouCape"] = 10,              // "Daimaou Cape"
		["ClothesFemaleShirt"] = 10,              // "Short Shirt"
		["ClothesFemaleShorts"] = 10,             // "Short Shorts"
		["ClothesFullYardrat"] = 10,              // "Yardrat"
		["ClothesGiFemale"] = 10,                 // "Female Gi"
		["ClothesGoggles"] = 10,                  // "Goggles"
		["ClothesKaioShirt"] = 10,                // "Kai Shirt"
		["ClothesNamekJacket"] = 10,              // "Namek Jacket"
		["ClothesNeroJacket"] = 100,              // "Long Coat"
		["ClothesNinjaMask2"] = 10,               // "Ninja Mask2"
		["ClothesPimpHat"] = 10,                  // "Big Hat"
		["ClothesTsurusennin"] = 35,              // "Crane Outfit"
		["ClothesTuxedo"] = 500,                  // "Tuxedo"
		["ClothesUnderwear"] = 10,                // "Underwear"
		["ClothesWolfHermit"] = 10,               // "Wolf Hermit"
		["Crane Hermit suit"] = 10,               // "Drab Wolf Hermit"
		["DarkJango"] = 10,                       // "Dark Jango"
		["El Hermano Suit"] = 10,                 // "El Hermano"
		["EyePatchL"] = 10,                       // "Left Eye Patch"
		["EyePatchR"] = 10,                       // "Right Eye Patch"
		["FlowingCape"] = 25,                     // "Flowing Cape"
		["Gray suit"] = 200,                      // "Gray Ninja Suit"
		["Hat"] = 10,                             // "Hat"
		["ItemPiccoloCape"] = 20,                 // "Piccolo Cape"
		["ItemSunGlassess"] = 50,                 // "Sunshades"
		["Jumpsuit"] = 20,                        // "Jumpsuit"
		["NekoCollar"] = 10,                      // "Collar"
		["PhoenixFull"] = 100,                    // "Phoenix Full"
		["PhoenixFullMakyo"] = 100,               // "Phoenix Full Makyo"
		["PhoenixFullMoonlight"] = 100,           // "Phoenix Full Moon"
		["PhoenixFullNegative"] = 100,            // "Phoenix Full Negative"
		["PhoenixFullNegativeMakyo"] = 100,       // "Phoenix Full Nega-makyo"
		["Pride Trooper Suit"] = 1337,            // "Pride Trooper"
		["Side Cape"] = 10,                       // "Side Cape"
		["Tien Clothes"] = 20,                    // "Tien Clothes"
		["ToSWingsBlack"] = 550,                  // "Energy Wings"
		["TuffleTux"] = 30,                       // "Tuffle"
		["Tunic"] = 10,                           // "Tunic"
		["VVGauntletBlack"] = 10,                 // "Gauntlet"
	};

	/// <summary>Quanto custa esta peca na bancada, ou nulo se ela nao a faz.</summary>
	public static int? PrecoDaRoupa(string folha) => Precos.TryGetValue(folha, out int preco) ? preco : null;

	/// <summary>Quantas pecas a vitrine tem.</summary>
	public static int Pecas => Precos.Count;

	/// <summary>A ficha de mochila do pente e da tinta de olhos -- eles nao estao no catalogo de construcoes (nao se fabricam na aba Tech).</summary>
	public static ItemDef? Ficha(string id) => id switch
	{
		Pente => new ItemDef(Pente, "Pente", "Muda o seu penteado, e a cor do cabelo (menos a do Saiyajin). Não se gasta.",
							 ArteDoPente, "", Empilhavel: false, Acoes: [AcaoPentear]),
		TintaDeOlhos => new ItemDef(TintaDeOlhos, "Tinta de olhos", "Muda a cor dos seus olhos. Não se gasta.",
									ArteDaTinta, "", Empilhavel: false, Acoes: [AcaoTingirOsOlhos]),
		_ => null,
	};

	/// <summary>Le uma cor `RRGGBB`. Falso se nao sao seis digitos de base dezesseis.</summary>
	public static bool LerCor(string? hex, out Appearance.Rgb cor)
	{
		cor = default;
		if (hex == null || hex.Length != 6
			|| !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint rgb))
			return false;
		cor = new Appearance.Rgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
		return true;
	}
}
