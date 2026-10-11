using Jandirus.Core.Social;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// O RADIO DO SCOUTER NA PONTA DO CLIENTE: os tres pacotes (`C2S.VozDeRadio`, `S2C.VozDeRadio`, `S2C.Radio`),
/// escritos e lidos pelos pares de <see cref="FioDoRadio"/>. As regras estao em
/// <see cref="RadioDoScouter"/>; quem decide quem ouve e o servidor (`GameServer.Radio.cs`).
/// </summary>
public partial class GameClient
{
	/// <summary>
	/// A FREQUENCIA DO MEU SCOUTER, como o servidor a guarda na ficha. Chega no login e a cada pedido de
	/// troca (`S2C.Radio`); ate chegar vale a de fabrica.
	/// </summary>
	public int FrequenciaDoRadio { get; private set; } = RadioDoScouter.FrequenciaPadrao;

	/// <summary>O `S2C.Radio` ja chegou nesta sessao de cliente? So a bancada le.</summary>
	public bool RadioChegouDeTeste { get; private set; }

	/// <summary>A frequencia chegou (ou mudou): a aba Equip e a faixa do radio se redesenham.</summary>
	public event Action? RadioMudou;

	/// <summary>
	/// CHEGOU UM QUADRO DE RADIO: (id de quem transmite, sequencia, buffer, tamanho). O BUFFER E O MESMO
	/// `_vozEntrando` da voz de perto, reusado: consuma agora -- ver o aviso em <see cref="VozRecebida"/>.
	/// </summary>
	public event Action<int, ushort, byte[], int>? VozDeRadioRecebida;

	/// <summary>
	/// MANDA UM QUADRO PELO RADIO. Como na voz de perto, nao ha destinatario nem frequencia no pacote: o
	/// servidor le a frequencia da MINHA ficha e decide quem ouve. Nao confiavel, pelo motivo de sempre.
	/// </summary>
	public void MandarVozDeRadio(ushort seq, byte[] dados, int tam)
	{
		if (!Connected || tam <= 0 || tam > VozLocal.MaxBytesDeQuadro) return;

		var w = Protocol.Begin(Protocol.C2S.VozDeRadio);
		FioDoRadio.EscreverQuadroDeQuemFala(w, seq, dados, tam);
		_peer!.Send(w, Protocol.ChannelVoz, DeliveryMethod.Unreliable);
	}

	/// <summary>Pede a troca de frequencia. Quem confere a faixa (e o scouter) e o servidor, e a resposta e o `S2C.Radio`.</summary>
	public void SintonizarORadio(int frequencia) =>
		SendVerbo(RadioDoScouter.VerboDeSintonizar, frequencia.ToString(System.Globalization.CultureInfo.InvariantCulture));

	/// <summary>O tratador do `S2C.VozDeRadio`. Sem filtro: o pacote so chega porque o servidor ja decidiu que eu posso ouvir.</summary>
	private void LerVozDeRadio(NetDataReader reader)
	{
		if (!FioDoRadio.LerQuadroPraQuemOuve(reader, _vozEntrando, out int quem, out ushort seq, out int tam)) return;
		VozDeRadioRecebida?.Invoke(quem, seq, _vozEntrando, tam);
	}

	/// <summary>O tratador do `S2C.Radio`.</summary>
	private void LerRadio(NetDataReader reader)
	{
		FrequenciaDoRadio = FioDoRadio.LerEstado(reader);
		RadioChegouDeTeste = true;
		RadioMudou?.Invoke();
	}
}
