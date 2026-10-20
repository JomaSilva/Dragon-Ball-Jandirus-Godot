namespace Jandirus.Server;

/// <summary>
/// A PORTA DO ROBO DO MENU LEVE (`--diagmenuleve`, `Client/RoboDoMenuLeve.cs`). O robo e de cliente e roda hospedando
/// (`--host`): o servidor mora no mesmo processo, e e por aqui que ele le o que o servidor decidiu sobre o passo. (O Ki
/// ele poe pela porta que ja existia, o `KiDeTeste` da `GameServer.CondicaoTeste.cs`.)
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// SO PRA BANCADA: quantas vezes o servidor ja PUXOU este jogador de volta (o `Corrections`, contado no
	/// `AplicarInput` fora da janela de correcao esperada). -1 = nao esta no mundo.
	/// </summary>
	internal int CorrecoesDeTeste(int idDoJogador) =>
		_players.TryGetValue(idDoJogador, out ServerPlayer? pl) ? pl.Corrections : -1;
}
