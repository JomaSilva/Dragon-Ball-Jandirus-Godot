using Jandirus.Core.Social;
using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>
/// ============================ OS TRES PACOTES DO RADIO DO SCOUTER, ESCRITOS E LIDOS NUM LUGAR SO ============================
/// <see cref="Protocol.C2S.VozDeRadio"/>, <see cref="Protocol.S2C.VozDeRadio"/> e <see cref="Protocol.S2C.Radio"/>.
///
/// O ESCRITOR E O LEITOR DE CADA UM MORAM LADO A LADO de proposito. O leitor do LiteNetLib nao confere o fim do
/// pacote: um campo a mais num dos lados ESTOURA ou LE LIXO conforme o buffer do pool (medido em 2026-10-08, com
/// o `PeerLook`). Aqui as duas pontas chamam o par deste arquivo, e a bancada `--radioteste` faz a ida e volta
/// em memoria cobrando que nao sobre nem falte byte.
///
/// O CORPO, SEM O OPCODE: quem escreve poe o byte do opcode antes (o relay do servidor reusa um escritor e o
/// escreve na mao, como a voz de proximidade faz); quem le ja consumiu o opcode no despacho.
/// ==========================================================================================================================
/// </summary>
public static class FioDoRadio
{
	// =====================================================================
	// C2S.VozDeRadio -- `ushort seq` + `byte n` + n bytes de Opus
	// =====================================================================
	/// <summary>Um quadro saindo da boca de quem transmite. O mesmo corpo do `C2S.Voz`: nao ha "pra quem" nem "em que frequencia".</summary>
	public static void EscreverQuadroDeQuemFala(NetDataWriter w, ushort seq, byte[] dados, int tam)
	{
		w.Put(seq);
		w.Put((byte)tam);
		w.Put(dados, 0, tam);
	}

	/// <summary>
	/// O inverso. Falso = pacote mentiroso (tamanho zero, maior que o teto, ou maior do que o que chegou) --
	/// descartado ANTES de ler o audio: o `GetBytes` estouraria, e a excecao viraria uma linha de log por quadro.
	/// </summary>
	public static bool LerQuadroDeQuemFala(NetDataReader r, byte[] destino, out ushort seq, out int tam)
	{
		seq = 0;
		tam = 0;
		if (r.AvailableBytes < 3) return false;
		seq = r.GetUShort();
		int n = r.GetByte();
		if (n == 0 || n > VozLocal.MaxBytesDeQuadro || n > destino.Length || r.AvailableBytes < n) return false;
		r.GetBytes(destino, n);
		tam = n;
		return true;
	}

	// =====================================================================
	// S2C.VozDeRadio -- `int falante` + `ushort seq` + `byte n` + n bytes de Opus
	// =====================================================================
	/// <summary>
	/// Um quadro repassado a quem tem direito de ouvir. SEM distancia e SEM parede (os dois bytes que o
	/// `S2C.Voz` carrega por ouvinte): radio nao tem perto nem longe, entao o pacote e IGUAL pra todo
	/// ouvinte e o servidor o monta uma vez so.
	/// </summary>
	public static void EscreverQuadroPraQuemOuve(NetDataWriter w, int falante, ushort seq, byte[] dados, int tam)
	{
		w.Put(falante);
		w.Put(seq);
		w.Put((byte)tam);
		w.Put(dados, 0, tam);
	}

	/// <summary>O inverso. Falso = pacote mentiroso, pelo mesmo criterio de <see cref="LerQuadroDeQuemFala"/>.</summary>
	public static bool LerQuadroPraQuemOuve(NetDataReader r, byte[] destino, out int falante, out ushort seq, out int tam)
	{
		falante = 0;
		seq = 0;
		tam = 0;
		if (r.AvailableBytes < 7) return false;
		falante = r.GetInt();
		seq = r.GetUShort();
		int n = r.GetByte();
		if (n == 0 || n > VozLocal.MaxBytesDeQuadro || n > destino.Length || r.AvailableBytes < n) return false;
		r.GetBytes(destino, n);
		tam = n;
		return true;
	}

	// =====================================================================
	// S2C.Radio -- `ushort frequencia`
	// =====================================================================
	/// <summary>O estado do MEU radio: a frequencia que o servidor guarda na ficha.</summary>
	public static void EscreverEstado(NetDataWriter w, int frequencia) => w.Put((ushort)RadioDoScouter.Sanear(frequencia));

	public static int LerEstado(NetDataReader r) => RadioDoScouter.Sanear(r.GetUShort());
}
