using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>O que a aba Allies desenha do estado de um aliado, em bits.</summary>
[Flags]
public enum EstadoDeAliado : byte
{
	Nenhum = 0,
	/// <summary>Esta no mundo, ao lado do dono (o motor do companheiro o dirige); sem este bit, esta "em casa".</summary>
	Solto = 1,
	/// <summary>Mandado ficar onde esta (sem ele, segue o dono).</summary>
	Ficando = 2,
	/// <summary>Esta atras de alguem (uma ordem de atacar, ou um reflexo).</summary>
	Lutando = 4,
	/// <summary>Nocauteado.</summary>
	Caido = 8,
	QuandoAtaco = 16,
	QuandoApanho = 32,
	/// <summary>O golpe dele esta letal (a ordem de matar).</summary>
	Matando = 64,
}

/// <summary>
/// UM ALIADO NA ABA. `Poder` vem ESCRITO pelo servidor: em numero so pra quem tem medidor, e senao contra o poder de quem le (a regra
/// principal do BP -- `GameServer.PoderEscrito`); o cliente nunca ve o numero cru.
/// </summary>
public readonly record struct AliadoNaAba(int Id, string Nome, string Raca, int Nivel, int Exp, int ExpDoNivel, string Poder,
										  int Relacao, int Inteligencia, int Sobras, EstadoDeAliado Estado, string Planeta);

/// <summary>Uma amizade em andamento: com quem, o medidor (0 a 100) e onde a pessoa esta.</summary>
public readonly record struct AmizadeNaAba(string Nome, string Raca, int Pontos, string Onde);

/// <summary>
/// A ABA ALLIES INTEIRA (2026-10-21, ver `Core/Npc/Amizade.cs`): os tetos, os aliados e as amizades em andamento. VAZIA (sem aliado
/// nem amizade) = a aba nao existe -- quem decide se ela aparece e o servidor, pelo que manda.
/// </summary>
public readonly record struct AliadosState(int Teto, int TetoDeSoltos, List<AliadoNaAba> Aliados, List<AmizadeNaAba> Amizades)
{
	public bool Vazio => Aliados.Count == 0 && Amizades.Count == 0;

	public static AliadosState Nenhum => new(0, 0, [], []);
}

/// <summary>
/// O CORPO DO `S2C.Aliados` -- escrito e lido NUM LUGAR SO, pra as duas pontas (e a bancada, que le o que saiu no fio pelo leitor de
/// producao) nao terem como divergir num campo.
///
///     `byte teto` + `byte tetoDeSoltos` + `byte n` + n x (`int id`, `string nome`, `string raca`, `byte nivel`, `ushort exp`,
///     `ushort expDoNivel`, `string poder`, `ushort relacao`, `byte inteligencia`, `byte sobras`, `byte estado`, `string planeta`)
///     + `byte m` + m x (`string nome`, `string raca`, `byte pontos`, `string onde`)
/// </summary>
public static class FioDosAliados
{
	public static void Escrever(NetDataWriter w, AliadosState s)
	{
		w.Put((byte)Math.Clamp(s.Teto, 0, byte.MaxValue));
		w.Put((byte)Math.Clamp(s.TetoDeSoltos, 0, byte.MaxValue));
		int n = Math.Min(s.Aliados.Count, byte.MaxValue);
		w.Put((byte)n);
		for (int i = 0; i < n; i++)
		{
			AliadoNaAba a = s.Aliados[i];
			w.Put(a.Id);
			w.Put(a.Nome);
			w.Put(a.Raca);
			w.Put((byte)Math.Clamp(a.Nivel, 0, byte.MaxValue));
			w.Put((ushort)Math.Clamp(a.Exp, 0, ushort.MaxValue));
			w.Put((ushort)Math.Clamp(a.ExpDoNivel, 0, ushort.MaxValue));
			w.Put(a.Poder);
			w.Put((ushort)Math.Clamp(a.Relacao, 0, ushort.MaxValue));
			w.Put((byte)Math.Clamp(a.Inteligencia, 0, byte.MaxValue));
			w.Put((byte)Math.Clamp(a.Sobras, 0, byte.MaxValue));
			w.Put((byte)a.Estado);
			w.Put(a.Planeta);
		}
		int m = Math.Min(s.Amizades.Count, byte.MaxValue);
		w.Put((byte)m);
		for (int i = 0; i < m; i++)
		{
			AmizadeNaAba z = s.Amizades[i];
			w.Put(z.Nome);
			w.Put(z.Raca);
			w.Put((byte)Math.Clamp(z.Pontos, 0, byte.MaxValue));
			w.Put(z.Onde);
		}
	}

	public static AliadosState Ler(NetDataReader r)
	{
		int teto = r.GetByte(), tetoDeSoltos = r.GetByte();
		int n = r.GetByte();
		var aliados = new List<AliadoNaAba>(n);
		for (int i = 0; i < n; i++)
			aliados.Add(new AliadoNaAba(r.GetInt(), r.GetString(), r.GetString(), r.GetByte(), r.GetUShort(), r.GetUShort(), r.GetString(),
										r.GetUShort(), r.GetByte(), r.GetByte(), (EstadoDeAliado)r.GetByte(), r.GetString()));
		int m = r.GetByte();
		var amizades = new List<AmizadeNaAba>(m);
		for (int i = 0; i < m; i++) amizades.Add(new AmizadeNaAba(r.GetString(), r.GetString(), r.GetByte(), r.GetString()));
		return new AliadosState(teto, tetoDeSoltos, aliados, amizades);
	}

	/// <summary>O que a aba DESENHA, numa string: o pacote so sai quando ela muda, e e ela que a pagina da aba assina.</summary>
	public static string Assinatura(AliadosState s) =>
		$"{s.Teto}/{s.TetoDeSoltos}|"
		+ string.Join(";", s.Aliados.Select(a => $"{a.Id},{a.Nome},{a.Raca},{a.Nivel},{a.Exp},{a.ExpDoNivel},{a.Poder},{a.Relacao},{a.Inteligencia},{a.Sobras},{(byte)a.Estado},{a.Planeta}"))
		+ "|" + string.Join(";", s.Amizades.Select(z => $"{z.Nome},{z.Raca},{z.Pontos},{z.Onde}"));
}
