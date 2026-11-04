using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS MAOS DO ROBO `--diagciv` (o pedido 227) do lado do servidor: o povo de um mundo com civilizacao, lido. Nenhuma regra mora aqui; o
/// pouso, o passo e a fala sao os palcos de sempre (`ProceduralDeFoto_Pousar`/`_Levar`, `PalcoLN_Dizer`).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O povo procedural desta zona: id, nome, raca, posicao e se esta de pe.</summary>
	internal List<(int Id, string Nome, string Raca, Vec2 Pos)> PalcoCiv_Povo(ZoneKey zona) =>
		[.. ZoneList(zona.Hash)
			.Where(p => p.Peer == null && p.Papel?.Molde.Id.StartsWith("civ_", StringComparison.Ordinal) == true && !p.Ficha.dead)
			.Select(p => (p.Id, p.Name, p.Race, p.Pos))];
}
