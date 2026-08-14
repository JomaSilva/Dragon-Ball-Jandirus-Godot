using Godot;
using Jandirus.Core.Forms;

namespace Jandirus.Client;

/// <summary>
/// O SIMBOLO DO CORE -> O ARQUIVO, pro CORPO que a forma veste. **UMA traducao, e ela mora aqui.**
///
/// Irma da <see cref="ColadasDeForma"/> e da <see cref="SpriteDeAura.CaminhoDa"/>, pelo mesmo motivo
/// escrito la: quem DECIDE e o Core (<see cref="FormaDef.Corpo"/>), e o Core nao conhece o Godot.
///
/// ============================ SO QUE ESTA TEM UM SEGUNDO ARGUMENTO, E ELE E O PONTO ============================
/// As irmas traduzem simbolo -> caminho e acabou. Esta pergunta tambem **de quem e o corpo**:
///
///   `apply_ussj_body()` (`supersaiyanbuff.dm:354-363`) escolhe a folha musculosa pelo ICONE que o
///   mob ja esta usando -- `White Male.dmi` vira `White Male Muscular 3.dmi`, `Tan Male.dmi` vira
///   `Tan Male Muscular.dmi`, `Black Male.dmi` vira `BlackMaleMuscular3.dmi`. A forma diz "incha";
///   quem diz "de que cor" e o corpo que estava ali.
///
/// Portar isso como caminho no catalogo exigiria TRES campos por forma (e um quarto no dia em que
/// entrasse outro tom). Como simbolo + resolucao aqui, o catalogo diz uma coisa so e esta funcao
/// responde a outra -- e a fonte da resposta e a folha que o boneco JA VESTE, que e literalmente o
/// `container.icon` do DM.
///
/// ============================ E POR ISSO A MULHER NAO INCHA -- SEM UM `if` PRA ELA ============================
/// **Nao existe folha musculosa feminina neste projeto.** Varrido: as unicas em `Assets/Sprites/`
/// sao `White Male Muscular`, `White Male Muscular 2`, `White Male Muscular 3`, `Tan Male Muscular`,
/// `Tan Male Muscular 3`, `Black Male Muscular 3` e `Makyo Muscular Grey` -- todas masculinas (o DM
/// tem a mesma lacuna: `apply_ussj_body()` so conhece os tres icones masculinos).
///
/// A saida nao e um `if (feminina) return null`, e a ausencia de linha: o corpo dela e
/// `NewPaleFemale.tres`, que nao esta na tabela, e sem entrada nao ha troca. A MESMA linha cobre o
/// Namekuseijin, o Majin, o Frost Demon, o Kai e qualquer NPC com folha propria -- nenhum deles tem
/// variante inchada, e nenhum deles precisou ser nomeado. Por um corpo masculino numa personagem
/// feminina seria pior que nao inchar, e inventar arte nao e opcao.
/// ==========================================================================================================
/// </summary>
public static class CorposDeForma
{
	private const string Personagens = "res://Assets/Sprites/Character Icons/";
	private const string Du = "res://Assets/Sprites/DU/Mobs/";

	/// <summary>A pelagem vermelha do SSJ4. `saiyan4body` / `saiyan5body` do DM.</summary>
	public const string Ssj4 = "res://Assets/Sprites/Clothes/SSj4_Body.tres";

	/// <summary>
	/// O CORPO DO MACACO. `Oozaru.dm:137-139` -- `targicon = 'oozaruhayate.dmi'`, e a linha seguinte
	/// e `container.icon = targicon`: no DM isto NAO e um overlay, e a TROCA do icone do mob, com
	/// `overlayList.Remove(...)` e `RemoveHair()` logo acima. O Oozaru nao veste nada.
	///
	/// Ele entra no mesmo mecanismo do SSJ4 mesmo sendo outro bicho (96x96 contra 32x32) porque o que
	/// separa os dois casos e o TAMANHO DO QUADRO, e o cliente deriva isso da folha
	/// (`CharacterVisual.EhCriatura`). Um `bool SubstituiTudo` no catalogo diria o que o `.tres` ja
	/// diz, com a chance de os dois discordarem.
	/// </summary>
	public const string Oozaru = Personagens + "Transformations/oozaruhayate.tres";

	/// <summary>
	/// O Oozaru Dourado. Mesma folha, pelagem dourada -- `goldoozaruhayate.dmi`.
	///
	/// O DM tinge o macaco COMUM com a cor do cabelo do jogador (`targicon += rgb(HairR,...)`) e NAO
	/// tinge o dourado. Aqui os dois saem sem tinta, e a diferenca esta anotada como divida.
	/// </summary>
	public const string OozaruDourado = Personagens + "Transformations/goldoozaruhayate.tres";

	// =====================================================================
	// O CORPO MUSCULOSO -- as tres folhas, e a pele que escolhe
	// =====================================================================
	/// <summary>Pele clara. Escolha do dono; o DM usa a `Muscular 3` aqui (ver <see cref="Musculoso"/>).</summary>
	public const string MusculosoClaro = Du + "White Male Muscular 2.tres";

	/// <summary>Pele morena. Escolha do dono; o DM usa `Tan Male Muscular` (sem numero).</summary>
	public const string MusculosoMoreno = Du + "Tan Male Muscular 3.tres";

	/// <summary>Pele negra. Mesma arte do `BlackMaleMuscular3.dmi` do DM, outra pasta.</summary>
	public const string MusculosoNegro = Du + "Black Male Muscular 3.tres";

	/// <summary>
	/// A FOLHA MUSCULOSA PRA ESTE CORPO BASE, ou `null` quando nao ha uma.
	///
	/// A chave e o NOME DO ARQUIVO do corpo que o boneco esta vestindo -- o `container.icon` do
	/// `apply_ussj_body()` (`supersaiyanbuff.dm:357-360`), que compara icone com icone pelo mesmo
	/// motivo: e o unico dado que ja diz a pele sem que ninguem precise carregar a ficha do jogador
	/// ate aqui. O `CharacterVisual` guarda esse caminho no meta `src` de cada camada.
	///
	/// ============================ TRES NOMES POR PELE, E OS TRES SAO REAIS ============================
	/// O `New*` e o que o catalogo de aparencia serve hoje (`Assets/Data/visual.json`: Saiyajin,
	/// Humano e Halfbreed todos apontam pra `NewPaleMale` / `NewTanMale` / `NewBlackMale`). Os outros
	/// dois vem do DM, que aceita as duas gracas: `if(icon == 'White Male.dmi' || icon ==
	/// 'BaseWhiteMale.dmi')`. Os seis `.tres` existem no projeto, entao os seis respondem -- um NPC
	/// montado com `BaseTanMale` incha igual, e nao calado.
	///
	/// O SUFIXO `Male` NAO E CONFERIDO de proposito: quem decide nao e o genero da ficha, e a FOLHA.
	/// `NewPaleFemale` simplesmente nao esta na lista. Perguntar pelo genero seria uma segunda fonte
	/// pra mesma verdade -- e erraria no dia em que existisse arte feminina inchada, porque a lista
	/// continuaria mandando.
	/// ==============================================================================================
	/// </summary>
	public static string? Musculoso(string corpoBase) => corpoBase.GetFile().GetBaseName() switch
	{
		"NewPaleMale" or "White Male" or "BaseWhiteMale" => MusculosoClaro,
		"NewTanMale" or "Tan Male" or "BaseTanMale" => MusculosoMoreno,
		"NewBlackMale" or "Black Male" or "BaseBlackMale" => MusculosoNegro,
		_ => null,
	};

	/// <summary>
	/// O ARQUIVO DESTE SIMBOLO. `null` = nao ha corpo pra vestir, e o chamador deixa o corpo base.
	///
	/// <paramref name="corpoBase"/> e o `res://` da folha que o boneco esta usando agora; so o
	/// <see cref="CorpoDeForma.Musculoso"/> o consulta. Passar vazio e legitimo (um boneco que ainda
	/// nao se vestiu) e devolve `null` -- que e o mesmo que o DM faz quando o `icon` nao bate com
	/// nenhum dos tres: `if(musc)` sai sem trocar nada.
	/// </summary>
	public static string? Caminho(CorpoDeForma simbolo, string corpoBase) =>
		Caminho(simbolo, corpoBase, null, 0);

	/// <summary>
	/// O MESMO, SABENDO QUEM E O FROST DEMON -- ver <see cref="Frost"/>.
	/// </summary>
	/// <param name="corposDoFrost">A lista `Appearance.FormasDeFrost` de quem se transformou. Nula = ninguem.</param>
	/// <param name="degrau">O `fd_form` da entrada (<see cref="Jandirus.Core.Forms.Catalogo.DegrauDoFrost"/>). 0 = nao e do Frost.</param>
	public static string? Caminho(CorpoDeForma simbolo, string corpoBase,
								  IReadOnlyList<string>? corposDoFrost, int degrau) => simbolo switch
	{
		CorpoDeForma.Musculoso => Musculoso(corpoBase),
		CorpoDeForma.Ssj4 => Ssj4,
		CorpoDeForma.Oozaru => Oozaru,
		CorpoDeForma.OozaruDourado => OozaruDourado,
		CorpoDeForma.FrostEscolhido => Frost(corposDoFrost, degrau),
		_ => null,
	};

	/// <summary>
	/// ============================ O CORPO QUE **ESTE** FROST DEMON ESCOLHEU PRA ESTE DEGRAU ============================
	/// Irmao do <see cref="Musculoso"/> logo acima, e pelo motivo que o cabecalho desta classe ja
	/// explica: a forma diz QUE corpo, o personagem diz DE QUEM. A diferenca e por onde o personagem
	/// responde -- o Musculoso le a PELE que o boneco ja veste (o `container.icon` do
	/// `apply_ussj_body()`), e este le a LISTA que o jogador montou na criacao (o
	/// `form1icon`..`form7icon` do `icer_poll_icon()`, `IcerTransform.dm:129-137`).
	///
	/// Quem traduz degrau -> slot e o Core (<see cref="Jandirus.Core.Races.FormasDeFrost.SlotDoDegrau"/>),
	/// porque a conta depende de quantos corpos a classe tem e essa e uma regra de raca, nao de
	/// desenho. Aqui so sobra abrir a lista e montar o caminho.
	///
	/// `null` de volta -- lista vazia, degrau que nao cabe nela, ou zero (nao e forma de Frost) -- cai
	/// no mesmo lugar do `Nenhum`: o corpo fica o que era. E o que acontece com todo NPC de Frost
	/// Demon montado sem lista e com todo save antigo, e ficar com o corpo base e a resposta certa
	/// pros dois.
	/// ==============================================================================================================
	/// </summary>
	public static string? Frost(IReadOnlyList<string>? corpos, int degrau)
	{
		if (degrau <= 0 || corpos == null || corpos.Count == 0) return null;
		int slot = Jandirus.Core.Races.FormasDeFrost.SlotDoDegrau(degrau, corpos.Count);
		return slot < 0 ? null : Jandirus.Core.Races.FormasDeFrost.Caminho(corpos[slot]);
	}

	/// <summary>
	/// TODAS as folhas que este arquivo sabe apontar. Pra bancada -- ela varre a lista em vez de
	/// repetir os nomes, entao arte nova entra na conferencia sozinha.
	/// </summary>
	public static readonly string[] Todas =
		[Ssj4, Oozaru, OozaruDourado, MusculosoClaro, MusculosoMoreno, MusculosoNegro];

	/// <summary>
	/// A ARTE ESTA IMPORTADA? Nao "existe na pasta" -- IMPORTADA.
	///
	/// Este projeto ja perdeu tempo tres vezes com arte que estava no disco e que o Godot nunca
	/// importou (os 35 atlas de animacao, os sons -- cujo efeito era silencio -- e a aura do Rose).
	/// `ResourceLoader` pergunta ao banco de recursos importados, que e a unica pergunta que
	/// corresponde ao que o jogo vai conseguir carregar.
	/// </summary>
	public static bool Existe(string caminho) => ResourceLoader.Exists(caminho);
}
