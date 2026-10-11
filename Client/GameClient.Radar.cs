using Jandirus.Core.Magic;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// O QUE O DRAGON RADAR SENTE -- a metade do cliente do `S2C.RadarDeEsferas`.
///
/// Isto e TUDO o que o cliente sabe pro disco, e so existe pra quem tem o aparelho: o servidor nao manda
/// o pacote a mais ninguem (`GameServer.RadarDeEsferas.cs`). Quem desenha e o `RadarNaTela`.
/// </summary>
public partial class GameClient
{
	/// <summary>O servidor disse que eu tenho o radar: o disco aparece. Falso = sem disco.</summary>
	public bool RadarLigado { get; private set; }

	/// <summary>Os sinais do ultimo pacote, em pixels da zona <see cref="ZonaDoRadar"/>.</summary>
	public IReadOnlyList<SinalDeEsfera> SinaisDoRadar => _sinaisDoRadar;

	private List<SinalDeEsfera> _sinaisDoRadar = [];

	/// <summary>
	/// EM QUE ZONA EU ESTAVA QUANDO O PACOTE CHEGOU. Os sinais sao posicoes daquele mundo: ao trocar de zona
	/// eles deixam de valer ate o servidor mandar os do lugar novo (no maximo um segundo depois), e o disco
	/// os ignora nesse meio tempo -- senao as esferas da Terra apareceriam em Namek.
	/// </summary>
	public ulong ZonaDoRadar { get; private set; }

	/// <summary>Quantos pacotes de radar ja chegaram nesta sessao. So pras bancadas.</summary>
	public int PacotesDeRadarDeTeste { get; private set; }

	public event Action? RadarMudou;

	private void LerRadarDeEsferas(NetDataReader reader)
	{
		(RadarLigado, _sinaisDoRadar) = FioDoRadarDeEsferas.Ler(reader);
		ZonaDoRadar = Zone.Hash;
		PacotesDeRadarDeTeste++;
		RadarMudou?.Invoke();
	}

	/// <summary>
	/// SESSAO NOVA, DISCO APAGADO. O `GameClient` sobrevive ao logout, e o personagem seguinte pode nao ter
	/// radar -- caso em que o servidor nao manda nada, nem "desligado". Quem chama e a chegada do
	/// `JoinAccepted`, ao lado do resto do estado que nao atravessa a troca de personagem.
	/// </summary>
	private void EsquecerORadar()
	{
		RadarLigado = false;
		_sinaisDoRadar = [];
		ZonaDoRadar = 0;
		RadarMudou?.Invoke();
	}
}
