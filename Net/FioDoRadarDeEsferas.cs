using Jandirus.Core.Magic;
using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>
/// O CORPO DO `S2C.RadarDeEsferas` -- escrito e lido NUM LUGAR SO, pra as duas pontas (e a bancada, que le
/// o que saiu no fio pelo leitor de producao) nao terem como divergir num campo.
///
///     `bool ligado` + `byte n` + n x (`float x`, `float y`, `byte marcas`)      marcas: 1 = carregada, 2 = comigo
///
/// `ligado = false` e o servidor dizendo "voce nao tem mais o radar": o disco some. Com ele ligado e zero
/// sinais o disco fica -- vazio, que e a resposta "nenhuma esfera acordada neste mundo".
/// </summary>
public static class FioDoRadarDeEsferas
{
	private const byte MarcaCarregada = 1, MarcaComigo = 2;

	public static void Escrever(NetDataWriter w, bool ligado, IReadOnlyList<SinalDeEsfera> sinais)
	{
		int n = Math.Min(sinais.Count, RadarDeEsferas.TetoDeSinais);
		w.Put(ligado);
		w.Put((byte)n);
		for (int i = 0; i < n; i++)
		{
			w.Put(sinais[i].X);
			w.Put(sinais[i].Y);
			w.Put((byte)((sinais[i].Carregada ? MarcaCarregada : 0) | (sinais[i].Comigo ? MarcaComigo : 0)));
		}
	}

	public static (bool Ligado, List<SinalDeEsfera> Sinais) Ler(NetDataReader r)
	{
		bool ligado = r.GetBool();
		int n = r.GetByte();
		var sinais = new List<SinalDeEsfera>(n);
		for (int i = 0; i < n; i++)
		{
			float x = r.GetFloat(), y = r.GetFloat();
			byte marcas = r.GetByte();
			sinais.Add(new SinalDeEsfera(x, y, (marcas & MarcaCarregada) != 0, (marcas & MarcaComigo) != 0));
		}
		return (ligado, sinais);
	}
}
