namespace Jandirus.Core.Items;

/// <summary>
/// A SEMENTE QUE SOBRA DA FRUTA -- `if(planttype&&prob(25))` (`Stamina/Food.dm:140-143`).
///
/// O dono (2026-10-10): *"faça com quem coma maça tenha uma chance de spawnar uma semente de arvore de
/// maça no inventario q serve pra plantar uma nova arvore de maça onde quiser (pra n passar fome em
/// planetas procedurais)"*.
///
/// ============================ E O QUE O ORIGINAL JA FAZIA ============================
/// No DM toda comida com `planttype` deixa uma semente com um quarto de chance ao ser comida
/// (`Food.dm:140`: `new/obj/items/food/Seed`, `A.planttype = planttype`, e ela vai direto pro `contents`
/// de quem comeu), e o `Plant()` da semente de maca ergue um `obj/Trees/AppleTree` que vai pro save
/// (`SaveItem = 1`, `Turfs/Plants.dm:949-953`). A Maca e a unica fruta de plantar que este port tem
/// (`planttype="Apple"`, `Plants.dm:800`); Laranja, Opuntia e a fruta do Enma entram aqui quando a
/// comida delas entrar no catalogo.
///
/// O PLANTIO em si nao mora aqui: a semente e um item que se ASSENTA, e quem cuida de lugar, alcance,
/// agua e parede e o mesmo funil de toda construcao (`GameServer.Posicionar`, com a previa do
/// `TelaDeConstrucao`). O que a semente vira no chao esta em <see cref="CatalogoDeItens.OQueAssenta"/>.
/// ====================================================================================
/// </summary>
public static class Plantio
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--macieirateste`): a fruta nunca deixa semente -- o estado de antes de
	/// 2026-10-10. Lido pela propria regra, em <see cref="SaiSemente"/>.
	/// </summary>
	public static bool FrutaSemSementeDeTeste;

	/// <summary>A chance de a fruta comida deixar uma semente: o `prob(25)` de `Food.dm:140`.</summary>
	public const double ChanceDeSemente = 0.25;

	/// <summary>
	/// A semente que ESTA comida deixa (nulo = nao e fruta de plantar). E o `planttype` do original: a Maca
	/// tem "Apple", e a propria Semente CARREGA o tipo da fruta de onde saiu (`A.planttype = planttype`) --
	/// por isso comer a semente tambem pode devolver outra, como la.
	/// </summary>
	public static string? SementeDe(string idDaComida) =>
		string.Equals(idDaComida, CatalogoDeItens.Maca, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(idDaComida, CatalogoDeItens.SementeDeMaca, StringComparison.OrdinalIgnoreCase)
			? CatalogoDeItens.SementeDeMaca
			: null;

	/// <summary>
	/// Comer ISTO, com ESTE sorteio (0 a 1), deixa semente? Devolve o id dela, ou nulo.
	///
	/// O sorteio e ARGUMENTO, e nao um `Random` daqui de dentro: quem sorteia e o servidor, e a bancada
	/// precisa poder perguntar a regra nos dois lados da fronteira (0,2499 da; 0,25 nao da).
	/// </summary>
	public static string? SaiSemente(string idDaComida, double sorteio) =>
		!FrutaSemSementeDeTeste && sorteio < ChanceDeSemente ? SementeDe(idDaComida) : null;
}
