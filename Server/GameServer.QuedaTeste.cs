namespace Jandirus.Server;

public partial class GameServer
{
	/// <summary>
	/// PALCO DA `--diagqueda` (`Client/RoboDeQueda.cs`): derruba a conexao de UM jogador pelo fio, do jeito que
	/// uma expulsao faz (`AdminExpulsar`: grava e `Peer.Disconnect()`).
	///
	/// NAO PASSA PELO VERB porque ele recusa o admin que expulsa a si mesmo, e a bancada e um processo so: o
	/// robo e o unico jogador. O que o cliente recebe e a MESMA despedida de um kick, de um ban ou de um
	/// servidor que fechou -- `DisconnectReason.RemoteConnectionClose` --, e e essa chegada que se mede.
	/// </summary>
	internal bool DerrubarDeBancada(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Peer == null) return false;
		Persistir(pl);
		pl.Peer.Disconnect();
		return true;
	}
}
