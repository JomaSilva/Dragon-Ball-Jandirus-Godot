using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-19) A CORRENTE DE PODER NO CLIENTE -- o `S2C.DandoPoder`: alguem por perto esta dando poder a alguem. O pacote e um
/// FOLEGO (sai a cada dose da doacao, 0,2 s), e nao estado: quem desenha a corrente e a mantem viva entre um folego e outro e o
/// `World` (`World.CorrenteDePoder.cs`). Ver `GameServer.CorrenteDePoder.cs`.
/// </summary>
public partial class GameClient
{
	/// <summary>Um folego da corrente: (doador, alvo). Quem escuta e o `World`.</summary>
	public event Action<int, int>? DandoPoder;

	/// <summary>`S2C.DandoPoder`: so repassa.</summary>
	private void LerDandoPoder(NetDataReader reader)
	{
		int doador = reader.GetInt();
		int alvo = reader.GetInt();
		DandoPoder?.Invoke(doador, alvo);
	}
}
