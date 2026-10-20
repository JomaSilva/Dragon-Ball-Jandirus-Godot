namespace Jandirus.Core.Npc;

/// <summary>
/// O GRANDE PATRIARCA DE NAMEK (dono, 2026-10-19: *"adicione o patriarca em namek assim como tinha no Finale-master"*) -- o
/// `mob/npc/Citizen/Guru` do original (`PlanetPopulation.dm:213-235`, com a fabrica `make_namek_guru` em `:444-452` e o
/// `populate_namek` em `:454-460`).
///
/// ============================ O QUE ELE E NO DM, E ONDE CADA PEDACO MORA AQUI ============================
///   * UM HABITANTE UNICO DE NAMEK -- Namekuseijin do Cla do Dragao, BP de 9 a 12 mil, o nome "Guru", a folha
///     `NamekOld - Guru Style`: o molde <see cref="Molde"/> do `npcs.json` e uma linha do povoamento com `quantos: 1` (o
///     `count_citizens("Namek", "guru") < 1`, `:459`). Morto, a manutencao de 5 min poe outro, como o Rei de Vegeta. Como todo
///     habitante ele nao ataca ninguem e revida quem bate nele ("PEACEFUL but TRAINABLE", `:8-9`);
///   * QUE NAO SAI VAGANDO (`idle_wandering = 0`, `:219`) -- o `"vagueia": false` do molde (`MoldeDeNpc.Vagueia`);
///   * QUE NASCE PERTO DO BERCO DE NAMEK (o `planet_spawn_turf("Namek")`, `:279-294`: ate 10 tiles do ponto de nascer) -- o
///     `"pertoDoBerco": 10` da linha do povoamento (`LinhaDePovoamento.PertoDoBerco`);
///   * E QUE DESPERTA O POTENCIAL OCULTO de quem fala com ele, UMA VEZ NA VIDA, se o povo de Namek nao o ve mal (o `Click()` a ate
///     2 tiles, `:220-234`). Aqui o clique e o menu E -- ele e um CORPO, como o Bills e o Whis, e o servidor o aponta pelo mesmo
///     `S2C.DivinoPerto` --, e o despertar e o mesmo do verbo `Unlock_Potential` e do desejo ao dragao
///     (`GameServer.DespertarPotencialG11`, o `UnlockPotential.dm`).
/// ==========================================================================================================
/// </summary>
public static class Patriarca
{
	/// <summary>O molde dele no `npcs.json`.</summary>
	public const string Molde = "patriarca";

	/// <summary>A chave do menu E dele (`Tech.Interacoes.De`): o que o servidor aponta pelo `S2C.DivinoPerto`.</summary>
	public const string Tipo = "Grande_Patriarca";

	/// <summary>O botao do menu: falar com ele.</summary>
	public const string VerboFalar = "patriarca_falar";

	/// <summary>
	/// O POVO DE NAMEK O VE MAL? -- `if(planet_rep_get("Namek", usr) <= -10)` (`:226`), com o comentario do proprio original:
	/// *"pelo menos neutro": Malvisto pra baixo e rejeitado*. O -10 e o piso da faixa "Malvisto" (`Social.Reputacao`).
	/// </summary>
	public static bool Recusa(double reputacaoComOPovoDele) => reputacaoComOPovoDele <= Social.Reputacao.LimiarDeMalvisto;

	/// <summary>
	/// ============================ A FOLHA DELE VIAJA NO `Appearance.Corpo`, NUM INDICE RESERVADO ============================
	/// O DM troca o `icon` na fabrica (`M.icon = 'NamekOld - Guru Style.dmi'`, `:449`). Aqui o corpo de um Namekuseijin sai do TOM
	/// (`VisualCatalog.CorpoSprite`: os tres verdes e o albino) e o `Corpo` dele nao e lido -- e por esse campo que a folha do Guru
	/// viaja, pelo caminho do corpo da saga do Majin (`Races.SagaDoMajin.PrimeiroCorpo`, 101 a 104): o pacote de aparencia ja leva o
	/// indice (num byte, `Protocol`), e nao ha canal novo.
	///
	/// NINGUEM ESCOLHE ESTE CORPO NA CRIACAO: ele nao esta no catalogo, e o `VisualCatalog.Sanear` zera todo indice fora da lista da
	/// raca. Quem o escreve e a aparencia do NPC (`GameServer.AparenciaDeNpc`), DEPOIS do saneamento.
	///
	/// 202, E NAO 201: o 201 e o do Cell Jr (`TorneioDoCell.CorpoDoCellJr`, so pra bio-androide). O ramo do bio responde antes e os dois
	/// nunca se cruzariam, mas um indice reservado com dois sentidos -- um por raca -- e a armadilha de quem acrescentar o proximo.
	/// =====================================================================================================================
	/// </summary>
	public const int Corpo = 202;

	/// <summary>A folha do Grande Patriarca: o `NamekOld - Guru Style.dmi` do DM, ja convertido.</summary>
	public const string Folha = "res://Assets/Sprites/Character Icons/Namekians/NamekOld - Guru Style.tres";

	/// <summary>A folha deste indice de corpo, ou nula quando ele nao e o do Guru.</summary>
	public static string? FolhaDoCorpo(int corpo) => corpo == Corpo ? Folha : null;
}
