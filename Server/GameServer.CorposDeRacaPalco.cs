using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagcorposderaca` (2026-10-18): filas de figurantes pra fotografar os corpos de raca e os Frost Demons do
/// torneio. O figurante e o do palco da v0.2.1 (`FigurantePalcoV21`); o que se mede e o catalogo de corpos (a fila das racas, com a aparencia
/// de nascenca -- o primeiro corpo da lista) e a porta de aparencia dos NPCs (`AparenciaDeNpc`, com o molde do torneio).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// Uma fila de figurantes, um por raca, cada um na sua CASA (em tiles a partir do jogador: quem chama escolhe, e o robo escolhe casas fora
	/// dos paineis da tela).
	/// </summary>
	internal List<int> PalcoCorpos_Fila(int id, string[] racas, (float X, float Y)[] casas)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return [];
		float t = ZoneCollision.TileSize;
		var ids = new List<int>();
		for (int i = 0; i < racas.Length && i < casas.Length; i++)
			ids.Add(FigurantePalcoV21(pl, racas[i], racas[i], new Vec2(casas[i].X * t, casas[i].Y * t), 5_000_000).Id);
		return ids;
	}

	/// <summary>
	/// Um Frost Demon por CASA (em tiles a partir do jogador), vestido pela porta dos NPCs (`AparenciaDeNpc`) com o molde do torneio -- cada
	/// um com a sua semente, como o `NascerLutador` faz.
	/// </summary>
	internal List<int> PalcoCorpos_FrostDoTorneio(int id, (float X, float Y)[] casas)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _moldes?.Get(_torneioCfg.MoldeDoNpc) is not { } molde) return [];
		float t = ZoneCollision.TileSize;
		var ids = new List<int>();
		for (int i = 0; i < casas.Length; i++)
		{
			ServerPlayer f = FigurantePalcoV21(pl, $"Frost {i + 1}", "Icer", new Vec2(casas[i].X * t, casas[i].Y * t), 5_000_000);
			f.Visual = AparenciaDeNpc(molde, "Icer", "Male", (ulong)(5_000 + 131 * i));
			TrocarAparencias(f);
			ids.Add(f.Id);
		}
		return ids;
	}

	/// <summary>O primeiro corpo masculino que o catalogo do servidor da a esta raca -- o de repouso de quem nasce dela.</summary>
	internal string PalcoCorpos_PrimeiroCorpo(string raca) => _visual?.CorposDe(raca).Para("Male").FirstOrDefault() ?? "";

	/// <summary>Tira os figurantes do palco, avisando a zona (`PalcoV21_TirarOFigurante`).</summary>
	internal void PalcoCorpos_Tirar(IEnumerable<int> ids)
	{
		foreach (int i in ids.ToList()) PalcoV21_TirarOFigurante(i);
	}
}
