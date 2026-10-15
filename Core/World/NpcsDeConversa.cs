namespace Jandirus.Core.World;

/// <summary>
/// ============================ OS NPCS DE CONVERSA DO DM QUE FALTAVAM (v0.3.0) ============================
/// O Sr. Kaioh e o Korin -- dois dos tres `mob/npc/Talker` de `SkyNPCs.dm` (o terceiro, o Enma Daioh, ja estava:
/// `Alem.TipoDoEnma`). O dono, 2026-10-15: *"tem varios npcs q dava pra interagir no dm q n estao nesse port, como o
/// korin, sr kaioh etc, traga eles pro port no mesmo local q era no DM e com a mesma conversa so q claro com a
/// interaçao nova do godot, com menu de interaçao etc"*. O DM inteiro tem exatamente tres `Talker`
/// (`Build_Sky_NPCs`, `SkyNPCs.dm:57-60`) -- o "etc" acaba aqui.
///
/// O LUGAR E O DO DM, MENOS O DO SR. KAIOH, que o dono mandou pra ILHA DO NORTE (*"coloca o sr kaioh na ilha mais ao
/// norte"*): o DM o punha em (42,336,6), na ilha do OESTE. As quatro casas sao o mesmo prefabricado, e ele fica no mesmo
/// ponto em relacao a dele: doze celulas da parede da esquerda, uma linha abaixo da casa.
/// ==========================================================================================================
/// </summary>
public static class NpcsDeConversa
{
	/// <summary>O tipo de obra do Sr. Kaioh (o menu E dele mora em `Tech.Interacoes`).</summary>
	public const string TipoDoSrKaioh = "Sr_Kaioh";

	/// <summary>O tipo de obra do Korin.</summary>
	public const string TipoDoKorin = "Korin";

	/// <summary>A celula do Sr. Kaioh no mapa do Outro Mundo (celulas do port, y de cima pra baixo): na frente da casa da ilha do norte.</summary>
	public const int SrKaiohX = 176, SrKaiohY = 14;

	/// <summary>O Korin no topo da Torre -- `place_sky_talker(/mob/npc/Talker/Korin, 142, 63, 12)` (`SkyNPCs.dm:60`), em coordenadas do DM.</summary>
	public const int KorinXDoDm = 142, KorinYDoDm = 63;

	/// <summary>A zona do Korin: o z12 do DM.</summary>
	public const string ZonaDoKorin = "Lookout";

	/// <summary>`KAI_KAIOKEN_KARMA` (`SkyNPCs.dm:95`): o karma minimo pro Sr. Kaioh ensinar o Kaio-ken.</summary>
	public const int KarmaDoKaioken = 50;

	/// <summary>`KAI_KAIOKEN_BP` (`:96`): o BP base minimo pro Kaio-ken.</summary>
	public const double BpDoKaioken = 8000;

	/// <summary>`KAI_GENKI_KARMA` (`:97`): o karma no maximo -- a Genkidama.</summary>
	public const int KarmaDaGenkidama = 100;

	/// <summary>As duas licoes do Sr. Kaioh: `learnSkill(new/datum/skill/kaioken)` e `learnSkill(new/datum/skill/rank/SpiritBomb)`.</summary>
	public const string SkillDoKaioken = "/datum/skill/kaioken", SkillDaGenkidama = "/datum/skill/rank/SpiritBomb";

	/// <summary>A celula do Korin (celulas do port): a mesma conversao do Enma -- `x - 1` e `altura - y`.</summary>
	public static (int Cx, int Cy) CelulaDoKorin(int alturaDaZonaEmTiles) => (KorinXDoDm - 1, alturaDaZonaEmTiles - KorinYDoDm);
}
