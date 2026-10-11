using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// O PODER EM USO NO CLIENTE -- como o servidor contou por ultimo o do MEU personagem (`S2C.PoderEmUso`):
/// quanto do proprio poder o corpo esta usando, se esta relaxado e se esconde o poder. A regra mora em
/// `Core.Stats.PoderEmUso` e o servidor e quem decide (`GameServer.PoderEmUso.cs`); quem desenha e o HUD
/// (`Hud.PoderEmUso.cs`) e a faixa da aba Ki (`MenuJogo.PoderEmUso.cs`).
/// </summary>
public partial class GameClient
{
	private PoderEmUsoState? _poderEmUso;

	/// <summary>O meu poder em uso, como veio no ultimo `S2C.PoderEmUso`. Antes do primeiro pacote: o poder inteiro, a vista.</summary>
	public PoderEmUsoState PoderEmUso => _poderEmUso ?? PoderEmUsoState.Cheio;

	/// <summary>O retrato mudou. Quem escuta e o HUD e o menu.</summary>
	public event Action? PoderEmUsoMudou;

	/// <summary>`S2C.PoderEmUso`: guarda o retrato e avisa.</summary>
	private void LerPoderEmUso(NetDataReader reader)
	{
		_poderEmUso = PoderEmUsoState.Read(reader);
		PoderEmUsoMudou?.Invoke();
	}
}
