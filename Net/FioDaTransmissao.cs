using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>
/// O INSTANT TRANSMISSION DE QUEM JOGA, pra aba Sense (2026-10-23): o nivel (0 = sem a skill), a maestria (0 a 100) e o que
/// ela rende AGORA -- o custo de Ki em % do tanque e a concentracao de longe e no campo de visao, em ms. As contas sao do
/// servidor (as mesmas que o verbo usa); a aba so escreve. <see cref="Maestria"/> -1 = o servidor e de antes da maestria.
/// </summary>
public readonly record struct TransmissaoState(byte Nivel, int Maestria, int CustoDeKi, int MsLonge, int MsPerto)
{
	/// <summary>A maestria veio no fio (servidor de antes de 2026-10-23 so mandava o nivel).</summary>
	public bool TemMaestria => Maestria >= 0;
}

/// <summary>
/// O CORPO DO `S2C.NivelDaTransmissao` (165) -- escrito e lido NUM LUGAR SO, pras duas pontas (e a bancada, que le o que sai
/// pelo leitor de producao) nao terem como divergir num campo.
///
///     `byte nivel` + `byte maestria` + `byte custoDeKi` + `ushort msLonge` + `ushort msPerto`
///
/// O NIVEL CONTINUA SENDO O PRIMEIRO BYTE, como era: o cliente de antes le so ele e deixa o resto (ler de menos nao e erro --
/// ver `GameClient.PacotesMalLidos`), e o cliente novo diante de um servidor de antes acha o pacote acabado no nivel e fica sem
/// o cartao da maestria, em vez de ler alem do fim.
/// </summary>
public static class FioDaTransmissao
{
	/// <summary>Quantos bytes vem depois do nivel. Menos que isto = servidor de antes da maestria.</summary>
	public const int BytesDaMaestria = 1 + 1 + 2 + 2;

	public static void Escrever(NetDataWriter w, TransmissaoState s)
	{
		w.Put(s.Nivel);
		w.Put((byte)Math.Clamp(s.Maestria, 0, 100));
		w.Put((byte)Math.Clamp(s.CustoDeKi, 0, 100));
		w.Put((ushort)Math.Clamp(s.MsLonge, 0, ushort.MaxValue));
		w.Put((ushort)Math.Clamp(s.MsPerto, 0, ushort.MaxValue));
	}

	public static TransmissaoState Ler(NetDataReader r)
	{
		byte nivel = r.GetByte();
		if (r.AvailableBytes < BytesDaMaestria) return new TransmissaoState(nivel, -1, -1, -1, -1);
		return new TransmissaoState(nivel, r.GetByte(), r.GetByte(), r.GetUShort(), r.GetUShort());
	}
}
