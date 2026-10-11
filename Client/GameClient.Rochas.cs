using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// AS ROCHAS DE MINERIO, do lado de ca do fio -- a lista que o servidor mandou e os avisos de que ela mudou.
/// Quem as desenha, as faz barrar e as oferece a tecla E e o `RochasNaTela`; a regra esta em
/// `Core/Economia/RochaDeMinerio.cs`.
///
/// GUARDADA COM A ZONA DENTRO, pelo motivo do estrago e dos blocos (`CenarioDaZona`, `BlocosDaZona`): o
/// retrato chega quando o servidor quer, e o chao daquela zona pode ainda nao estar montado -- ou o chao
/// montado pode ser o do planeta ANTERIOR. Quem aplica confere de que zona a lista e.
/// </summary>
public partial class GameClient
{
	/// <summary>As celulas com rocha de minerio de pe na zona <see cref="RochasDaZona"/>.</summary>
	public readonly HashSet<(int X, int Y)> Rochas = [];

	/// <summary>De que zona e a lista <see cref="Rochas"/>. Zero antes do primeiro retrato.</summary>
	public ulong RochasDaZona { get; private set; }

	/// <summary>Chegou o retrato de uma zona: a lista foi SUBSTITUIDA.</summary>
	public event Action? RochasChegaram;

	/// <summary>Uma rocha da zona da lista nasceu (verdadeiro) ou foi colhida (falso): celula.</summary>
	public event Action<int, int, bool>? RochaMudou;

	private void LerRochas(NetDataReader reader)
	{
		byte modo = reader.GetByte();
		ulong zona = reader.GetULong();

		if (modo == Protocol.RochasRetrato)
		{
			int n = reader.GetUShort();
			Rochas.Clear();
			for (int i = 0; i < n; i++) Rochas.Add((reader.GetUShort(), reader.GetUShort()));
			RochasDaZona = zona;
			RochasChegaram?.Invoke();
			return;
		}

		int cx = reader.GetUShort(), cy = reader.GetUShort();

		// A MUDANCA DE OUTRA ZONA NAO E DESTA LISTA: o servidor so a manda a quem ja tem o retrato da zona,
		// mas o pacote pode cruzar com uma troca de planeta no caminho.
		if (zona != RochasDaZona) return;

		bool nasceu = modo == Protocol.RochasNasceu;
		if (nasceu ? !Rochas.Add((cx, cy)) : !Rochas.Remove((cx, cy))) return;   // ja estava assim: nada a avisar
		RochaMudou?.Invoke(cx, cy, nasceu);
	}
}
