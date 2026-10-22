using Godot;
using Jandirus.Core.Stats;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// A metade DESTE lado do treino focado. O servidor manda o modo, a letra e a fase da respiracao
/// (<see cref="Protocol.S2C.Foco"/>, <see cref="Protocol.S2C.TreinoFocado"/>, <see cref="Protocol.S2C.Respiracao"/>).
/// Daqui saem so a tecla crua e o verbo <c>focado</c>. O bonus que a tela escreve e o que chegou no pacote.
/// </summary>
public partial class GameClient
{
	/// <summary>0 ninguem, 1 treino, 2 meditacao. Ver <see cref="TreinoFocado.ModoNenhum"/>.</summary>
	public byte FocoModo { get; private set; }

	/// <summary>Um embate de verdade pausou o anel. O WASD volta a ser do embate.</summary>
	public bool FocoPausado { get; private set; }

	/// <summary>O multiplicador que o servidor esta pagando. A tela so mostra.</summary>
	public double FocoBonus { get; private set; } = TreinoFocado.PisoDoBonus;

	/// <summary>A letra que o anel pede agora, ou 0.</summary>
	public char FocoLetra { get; private set; }

	/// <summary>Ms que o servidor disse que esta letra ainda tem. A tela conta daqui pra baixo; quem julga e ele.</summary>
	public int FocoPrazoMs { get; private set; }

	/// <summary>0 letra nova, 1 o aperto anterior acertou, 2 errou. So pro piscar da tela.</summary>
	public byte FocoVeredito { get; private set; }

	/// <summary>Sobe a cada letra, pra a tela saber que pode mandar outra tecla.</summary>
	public int FocoGeracao { get; private set; }

	public long RespiracaoInicio { get; private set; }
	public int RespiracaoPeriodo { get; private set; }
	public long RespiracaoAgoraServidor { get; private set; }
	public long RespiracaoLocalMs { get; private set; }
	public byte RespiracaoVeredito { get; private set; }
	public int RespiracaoGeracao { get; private set; }

	public event Action? FocoMudou;

	public void SendTreinoTecla(char c)
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.TreinoTecla);
		w.Put((byte)c);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	public void SendRespiracaoTecla()
	{
		if (!Connected) return;
		var w = Protocol.Begin(Protocol.C2S.RespiracaoTecla);
		_peer!.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	public void PedirFocado(bool meditar) => SendVerbo("focado", meditar ? "meditar" : "treino");

	public void SoltarFocado() => SendVerbo("focado", "");

	private void LerFoco(NetDataReader reader)
	{
		FocoModo = reader.GetByte();
		FocoPausado = reader.GetByte() != 0;
		FocoBonus = reader.GetDouble();
		if (FocoModo == TreinoFocado.ModoNenhum)
		{
			FocoLetra = '\0';
			FocoPausado = false;
			FocoBonus = TreinoFocado.PisoDoBonus;
		}
		FocoMudou?.Invoke();
	}

	private void LerTreinoFocado(NetDataReader reader)
	{
		FocoLetra = (char)reader.GetByte();
		FocoPrazoMs = reader.GetInt();
		FocoVeredito = reader.GetByte();
		FocoGeracao++;
		FocoMudou?.Invoke();
	}

	private void LerRespiracao(NetDataReader reader)
	{
		RespiracaoInicio = reader.GetLong();
		RespiracaoPeriodo = reader.GetInt();
		RespiracaoAgoraServidor = reader.GetLong();
		RespiracaoVeredito = reader.GetByte();
		RespiracaoLocalMs = (long)Time.GetTicksMsec();
		RespiracaoGeracao++;
		FocoMudou?.Invoke();
	}

	/// <summary>A hora do servidor agora, alinhada no pacote da respiracao. A folga cobre o atraso dele.</summary>
	public long AgoraDaRespiracao() =>
		RespiracaoAgoraServidor + ((long)Time.GetTicksMsec() - RespiracaoLocalMs);
}
