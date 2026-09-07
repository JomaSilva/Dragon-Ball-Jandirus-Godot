using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

public sealed partial class GameServer
{
	/// <summary>`--mobiliateste`: quem loga nasce em Vegeta, dois tiles abaixo do banco do castelo.</summary>
	private bool _mobiliaDeTeste;

	/// <summary>
	/// O CENARIO DA FOTO DO ROBO `--diagmobilia`: o banco do castelo de Vegeta esta em (122,214), com uma
	/// cadeira na MESMA celula (`.dmm`: `/obj/buildables/chair, /obj/Bank, /turf/Tile/Tile5`) e a fileira
	/// de mesas logo abaixo. O corpo nasce em (122,216), olhando pra cima -- o banco inteiro na tela.
	/// </summary>
	private void NascerNoBanco(ServerPlayer pl)
	{
		const int t = ZoneCollision.TileSize;
		pl.Zone = ZoneKey.Premade("Vegeta");
		pl.Pos = new Vec2(122 * t + t / 2f, 216 * t + t / 2f - MoveRules.FeetOffsetY);
		pl.Facing = Facing.North;
		GD.Print($"[server] BANCADA: {pl.Name} nasce em Vegeta ({pl.Pos.X:0},{pl.Pos.Y:0}), o banco na celula (122,214)");
	}
}
