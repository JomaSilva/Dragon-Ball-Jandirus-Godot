using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>Uma linha da rede de servicos: o nome do servico e o estado dele pra este personagem ("disponível", "volta em 12 min"...).</summary>
public readonly record struct LinhaDeServico(string Nome, string Estado);

/// <summary>
/// O CARTAO DOS SERVICOS DO OUTRO MUNDO na aba World (2026-10-19) -- ver `Core/World/ServicosDoAlem.cs`. `Mostrar` falso = o cartao some
/// (o vivo nao trabalha pro Outro Mundo). Sem servico em curso o `Titulo` vem vazio e so a rede aparece.
/// </summary>
public readonly record struct ServicoDoAlemState(bool Mostrar, string Titulo, string Andamento, int Minutos, double Recompensa,
												 List<LinhaDeServico> Rede);

/// <summary>
/// O CORPO DO `S2C.ServicoDoAlem` -- escrito e lido NUM LUGAR SO, pra as duas pontas (e a bancada, que le o que saiu no fio pelo leitor de
/// producao) nao terem como divergir num campo.
///
///     `bool mostrar` + `string titulo` + `string andamento` + `ushort minutos` + `double recompensa` + `byte n` + n x (`string`, `string`)
/// </summary>
public static class FioDoServicoDoAlem
{
	public static void Escrever(NetDataWriter w, ServicoDoAlemState s)
	{
		w.Put(s.Mostrar);
		w.Put(s.Titulo);
		w.Put(s.Andamento);
		w.Put((ushort)Math.Clamp(s.Minutos, 0, ushort.MaxValue));
		w.Put(s.Recompensa);
		int n = Math.Min(s.Rede.Count, byte.MaxValue);
		w.Put((byte)n);
		for (int i = 0; i < n; i++)
		{
			w.Put(s.Rede[i].Nome);
			w.Put(s.Rede[i].Estado);
		}
	}

	public static ServicoDoAlemState Ler(NetDataReader r)
	{
		bool mostrar = r.GetBool();
		string titulo = r.GetString(), andamento = r.GetString();
		int minutos = r.GetUShort();
		double recompensa = r.GetDouble();
		int n = r.GetByte();
		var rede = new List<LinhaDeServico>(n);
		for (int i = 0; i < n; i++) rede.Add(new LinhaDeServico(r.GetString(), r.GetString()));
		return new ServicoDoAlemState(mostrar, titulo, andamento, minutos, recompensa, rede);
	}
}
