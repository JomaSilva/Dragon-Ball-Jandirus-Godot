using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-21) A ABA ALLIES -- a metade do cliente do `S2C.Aliados`: os aliados deste personagem e as amizades em andamento. Isto e
/// tudo o que o cliente sabe deles: quem decide e o servidor (`GameServer.Amizade.cs`, `GameServer.Aliados.cs`); quem desenha e a aba
/// (`MenuJogo.Allies.cs`). O poder de cada aliado chega ESCRITO pelo servidor (a regra do BP), nunca em numero cru.
/// </summary>
public partial class GameClient
{
	/// <summary>A ultima aba que chegou. Vazia (sem aliado nem amizade) = a aba nao existe.</summary>
	public AliadosState Aliados { get; private set; } = AliadosState.Nenhum;

	/// <summary>Quantas vezes a aba chegou nesta sessao -- e o que a pagina dela assina (o servidor so manda quando ela muda).</summary>
	public int VersaoDosAliados { get; private set; }

	/// <summary>Chegou uma aba nova.</summary>
	public event Action? AliadosMudaram;

	private void LerAliados(NetDataReader reader) => PorOsAliados(FioDosAliados.Ler(reader));

	private void PorOsAliados(AliadosState s)
	{
		Aliados = s;
		VersaoDosAliados++;
		AliadosMudaram?.Invoke();
	}
}
