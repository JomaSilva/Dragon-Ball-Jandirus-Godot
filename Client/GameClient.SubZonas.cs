using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-21) OS PLANETAS COM CHEFE DE SAGA NO MUNDO, do lado do cliente -- a metade da trilha de perigo que so o servidor sabe. A outra
/// metade (o planeta explodindo) ja chega pelo `S2C.Mortos` (<see cref="Mortos"/>). Quem junta as duas e decide a musica e o `World`
/// (`World.SubZonas.cs`), pela regra do Core (`PlanetaEmPerigo.De`). Ver `GameServer.SubZonas.cs`.
/// </summary>
public partial class GameClient
{
	/// <summary>As chaves (`ChaveDePlaneta.Texto`) dos planetas com chefe de saga no mundo, como o servidor contou por ultimo.</summary>
	public readonly HashSet<string> PlanetasComChefe = [];

	/// <summary>A lista mudou -- um chefe chegou ou caiu. So aviso: o `World` olha a lista a cada quadro.</summary>
	public event Action? ChefesNosPlanetasMudaram;

	/// <summary>`S2C.ChefesNosPlanetas`: `byte n` + n x `string chave`. A lista INTEIRA, que substitui a anterior.</summary>
	private void LerChefesNosPlanetas(NetDataReader reader)
	{
		int n = reader.GetByte();
		PlanetasComChefe.Clear();
		for (int i = 0; i < n; i++) PlanetasComChefe.Add(reader.GetString(64));
		ChefesNosPlanetasMudaram?.Invoke();
	}
}
