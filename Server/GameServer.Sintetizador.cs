using Godot;
using Jandirus.Core.Tech;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-26) O SINTETIZADOR DE VIRUS SAIU DO JOGO -- o pedido 98 do dono. O que saiu e o que fica esta em `Core/Tech/Sintetizador.cs`.
/// Aqui mora o que tira do MUNDO a maquina que estava aparafusada no chao; a da mochila e os produtos saem pelos `ItensAposentados`, no
/// login. (A espera parada de dez segundos, que nasceu aqui, e dos kits do androide: `GameServer.KitsDeAndroide.cs`.)
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// A MAQUINA APARAFUSADA SAI DO MUNDO, na carga do `mundo.json` (`CarregarMundoDeDisco`): ela nao se fabrica mais, o menu dela nao
	/// existe e nada do que ela fazia roda -- de pe, seria um caixote mudo no chao de alguem. A proxima gravacao do mundo ja nao a tem.
	/// Devolve quantas sairam.
	/// </summary>
	private int TirarOsSintetizadoresDoMundo()
	{
		int sairam = _noChao.RemoveAll(o => string.Equals(o.Tipo, Sintetizador.Tipo, StringComparison.OrdinalIgnoreCase));
		if (sairam > 0)
			GD.Print($"[server] mundo: {sairam} sintetizador(es) de virus sairam do chao -- a maquina saiu do jogo (o pedido 98 do dono)");
		return sairam;
	}
}
