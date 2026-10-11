using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DA `--fotoprocedural` (ver `Client/RoboDeFotoDoProcedural.cs`): as duas maos que o robo de tela
/// precisa do lado do servidor. Nenhuma regra mora aqui.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// POUSA O CORPO NUM MUNDO SORTEADO DADO PELA SEED DELE, pelo `PousarEmProcedural` de producao. Devolve
	/// falso enquanto o mundo ainda esta nascendo (a geracao e fora do tique): quem chama pede de novo.
	///
	/// O MUNDO NAO PRECISA SER DESTE UNIVERSO, e e por isso que isto e um palco e nao o pouso de sempre: a
	/// bancada fotografa o "antes" e o "depois" no MESMO mundo, e a seed do universo de uma pasta de saves
	/// vazia e sorteada a cada rodada. O corpo celeste e de mentira -- so serve pra a decolagem ter um lugar
	/// pra onde devolver o corpo, e ninguem decola numa foto.
	/// </summary>
	internal bool ProceduralDeFoto_Pousar(int id, string nome, ulong seed)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		if (pl.Zone.Equals(ZoneKey.Procedural(nome, seed))) return true;
		return PousarEmProcedural(pl, new PlanetaNoEspaco { Nome = nome, Seed = seed, Pos = pl.Pos, Raio = 150 });
	}

	/// <summary>Leva o corpo pro meio de uma celula da zona em que ele esta -- teleporte de palco, sem trocar de zona.</summary>
	internal void ProceduralDeFoto_Levar(int id, int cx, int cy)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		const int t = ZoneCollision.TileSize;
		CravarPosicao(pl, new Vec2(cx * t + t / 2f, cy * t + t / 2f - MoveRules.FeetOffsetY), 2000);
	}
}
