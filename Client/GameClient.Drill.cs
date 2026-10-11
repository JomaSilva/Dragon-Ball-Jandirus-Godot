using Jandirus.Core.Tech;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// A DRILL NO CLIENTE: o ultimo estado que o servidor mandou (`S2C.Drill`), pra quem o desenha -- o painel
/// do menu da tecla E (<see cref="EstadoDaDrillNoMenu"/>). O cliente nao decide nada daqui: ele pergunta
/// (`drill_ver`), mostra, e manda os verbos de coletar e melhorar; a conta e do servidor.
/// </summary>
public partial class GameClient
{
	/// <summary>
	/// A DRILL SOBRE A QUAL O SERVIDOR RESPONDEU POR ULTIMO -- nulo antes da primeira resposta, e `Id` zero
	/// quando ele disse que nao ha nenhuma ao alcance. Uma so: e a que esta ao alcance da mao.
	/// </summary>
	public EstadoDeDrill? DrillVista { get; private set; }

	/// <summary>Quando ela chegou, no relogio do processo (ms): o painel anda o estoque a partir daqui.</summary>
	public ulong DrillVistaEm { get; private set; }

	public event Action? DrillMudou;

	private void AoReceberADrill(NetDataReader leitor)
	{
		DrillVista = FioDaDrill.Ler(leitor);
		DrillVistaEm = Godot.Time.GetTicksMsec();
		DrillMudou?.Invoke();
	}
}
