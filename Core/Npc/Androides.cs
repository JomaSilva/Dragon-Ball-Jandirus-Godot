namespace Jandirus.Core.Npc;

/// <summary>
/// OS ANDROIDES 17 E 18 DA SAGA DO CELL, COM A ARTE QUE O DONO TROUXE (2026-10-24): *"faça os chefes androide 17 e androide 18 usarem as
/// seguintes coisas, a 18 vai ter o corpo base da 18, e o 17 vai usar a roupa do 17"* -- e *"deve usar o cabelo q era no DM"*.
///
/// ============================ O QUE MORA ONDE ============================
///   * O 17 se veste pelo canal de todo chefe: a `roupa` do molde (`Android17 Suit`, uma peca do catalogo de roupas), o `corpo` cravado
///     no `NewPaleMale` -- o `BEV_A17_ICON` da fabrica do DM (`BossEvents.dm:81`), confirmado pelo dono -- e o cabelo `Long` preto do
///     `npc_apply_hair(M17, ...)` (`:497`, a cor `BEV_A17_HAIR`, `:85-87`). Ver `MoldeDeNpc.Corpo` e `MoldeDeNpc.CorDoCabelo`.
///   * A 18 tem uma FOLHA PROPRIA (`Android 18 Base`), ja vestida e com os olhos ciano desenhados, que ninguem escolhe na criacao: ela nao
///     esta no catalogo de corpos, e viaja num INDICE RESERVADO do `Appearance.Corpo` (<see cref="CorpoDa18"/>), pelo caminho do Guru
///     (<see cref="Patriarca.Corpo"/>). Sem roupa (a folha ja e a roupa dela) e sem a camada de olhos de gente (como o Saibaman,
///     `VisualCatalog.CorpoTemRosto`); o cabelo e o `Long` loiro "via codigo" do DM (`:506`, `BEV_A18_HAIR` 255,222,120, `:88-90`).
/// =========================================================================
/// </summary>
public static class Androides
{
	/// <summary>
	/// 203, E NAO 201 NEM 202: o 201 e o do Cell Jr (`TorneioDoCell.CorpoDoCellJr`) e o 202 o do Guru -- um indice reservado por folha,
	/// e nenhum com dois sentidos.
	/// </summary>
	public const int CorpoDa18 = 203;

	/// <summary>O nome da folha da 18, como o molde a crava (o `corpo` dela no `npcs.json`).</summary>
	public const string NomeDaFolhaDa18 = "Android 18 Base";

	/// <summary>A folha da Androide 18 -- o DMI que o dono trouxe, convertido pela esteira (`dmi`, o `SpriteFramesWriter`).</summary>
	public const string FolhaDa18 = "res://Assets/Sprites/Character Icons/Android 18 Base.tres";

	/// <summary>A folha deste indice de corpo, ou nula quando ele nao e o da 18.</summary>
	public static string? FolhaDoCorpo(int corpo) => corpo == CorpoDa18 ? FolhaDa18 : null;

	/// <summary>O indice reservado da folha propria que o molde crava pelo NOME, ou nulo quando o nome nao e o de uma.</summary>
	public static int? IndiceReservado(string nomeDaFolha) => nomeDaFolha == NomeDaFolhaDa18 ? CorpoDa18 : null;
}
