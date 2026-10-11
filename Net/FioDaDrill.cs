using Jandirus.Core.Tech;
using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>
/// O ESTADO DE UMA DRILL NO FIO -- o corpo do `S2C.Drill`, escrito e lido NUM LUGAR SO.
///
/// O servidor escreve por aqui (`GameServer.MandarDrill`) e o cliente le por aqui
/// (`GameClient.AoReceberADrill`): as duas pontas nao tem como discordar da ordem dos campos, e a bancada
/// (`--drillteste`) da a volta inteira -- escreve, le e compara.
///
/// Layout, depois do opcode: `int id` da obra (0 = nenhuma Drill ao alcance) + `byte nivel` +
/// `double estoque` + `byte situacao` (<see cref="SituacaoDaDrill"/>) + `float fracaoDaReserva` +
/// `bool minha` + `string dono` + `double zeni` + `double recursos` (os dois saldos de QUEM RECEBE).
/// </summary>
public static class FioDaDrill
{
	/// <summary>O teto de letras do nome do dono no fio -- o mesmo do pacote de construcoes.</summary>
	public const int MaxDono = 32;

	public static void Escrever(NetDataWriter w, in EstadoDeDrill e)
	{
		w.Put(e.Id);
		w.Put((byte)Math.Clamp(e.Nivel, 0, byte.MaxValue));
		w.Put(e.Estoque);
		w.Put((byte)e.Situacao);
		w.Put((float)e.FracaoDaReserva);
		w.Put(e.Minha);
		w.Put(e.Dono.Length > MaxDono ? e.Dono[..MaxDono] : e.Dono);
		w.Put(e.Zeni);
		w.Put(e.Recursos);
	}

	public static EstadoDeDrill Ler(NetDataReader r) => new(
		Id: r.GetInt(),
		Nivel: r.GetByte(),
		Estoque: r.GetDouble(),
		Situacao: (SituacaoDaDrill)r.GetByte(),
		FracaoDaReserva: r.GetFloat(),
		Minha: r.GetBool(),
		Dono: r.GetString(MaxDono),
		Zeni: r.GetDouble(),
		Recursos: r.GetDouble());
}
