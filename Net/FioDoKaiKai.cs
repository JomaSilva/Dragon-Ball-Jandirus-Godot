using Jandirus.Core.Skills;
using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>
/// (2026-10-23) A LISTA DO KAI KAI NO FIO -- o corpo do <see cref="Protocol.S2C.DestinosDoKaiKai"/>, escrito e lido NUM LUGAR SO (o
/// molde do `FioDaDrill`): o servidor escreve por aqui (`GameServer.MandarATelaDoKaiKai`), o cliente le por aqui
/// (`TelaDoKaiKai.Abrir`), e a bancada `--kaikaiteste` da a volta inteira -- escreve, le e compara.
///
/// Layout, depois do opcode: `ushort n` + n x (`string chave` + `string nome` + `byte bits`). Bits: 1 = mundo gerado, 2 = e onde
/// quem recebe esta agora.
/// </summary>
public static class FioDoKaiKai
{
	/// <summary>A chave mais comprida e a de mundo gerado: `#` e ate 20 digitos. O mapa mais comprido tem 19 letras.</summary>
	public const int MaxChave = 32;

	/// <summary>O nome: o batismo vai ate 24 letras (`Batismo.MaxLetras`), o nome de mundo gerado tem uns 15. Folga de sobra.</summary>
	public const int MaxNome = 64;

	private const byte BitGerado = 1, BitAqui = 2;

	public static void Escrever(NetDataWriter w, IReadOnlyList<DestinoDoKaiKai> lista)
	{
		int n = Math.Min(lista.Count, ushort.MaxValue);
		w.Put((ushort)n);
		for (int i = 0; i < n; i++)
		{
			DestinoDoKaiKai d = lista[i];
			w.Put(Cortar(d.Chave, MaxChave));
			w.Put(Cortar(d.Nome, MaxNome));
			w.Put((byte)((d.Gerado ? BitGerado : 0) | (d.Aqui ? BitAqui : 0)));
		}
	}

	public static List<DestinoDoKaiKai> Ler(NetDataReader r)
	{
		int n = r.GetUShort();
		var lista = new List<DestinoDoKaiKai>(n);
		for (int i = 0; i < n; i++)
		{
			string chave = r.GetString(MaxChave);
			string nome = r.GetString(MaxNome);
			byte bits = r.GetByte();
			lista.Add(new DestinoDoKaiKai(chave, nome, (bits & BitGerado) != 0, (bits & BitAqui) != 0));
		}
		return lista;
	}

	/// <summary>
	/// O TEXTO COMO ELE VIAJA: inteiro ate o teto, cortado nele acima disso -- sem partir um par substituto no meio. O leitor do
	/// LiteNetLib devolve VAZIO pra texto acima do teto (`GetString(max)` nao corta), entao quem escreve corta antes.
	/// </summary>
	private static string Cortar(string s, int max)
	{
		if (s.Length <= max) return s;
		int fim = char.IsHighSurrogate(s[max - 1]) ? max - 1 : max;
		return s[..fim];
	}
}
