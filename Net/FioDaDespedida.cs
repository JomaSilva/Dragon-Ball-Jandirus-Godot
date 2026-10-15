using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>
/// A DESPEDIDA DE PROPOSITO (v0.3.0) -- a carga do `Disconnect` que o SERVIDOR da quando a conexao acaba por uma regra do
/// jogo, e nao por queda.
///
/// ============================ POR QUE NA CARGA DO DISCONNECT, E NAO NUM PACOTE ANTES ============================
/// O pacote mandado logo antes do `Disconnect` pode nao chegar: o LiteNetLib encerra o par e o que estava na fila do canal
/// confiavel morre junto. A carga do `Disconnect` e o unico recado que vai COM a despedida -- o par a repete ate o outro
/// lado confirmar (ou o prazo vencer), e o cliente a le no proprio `PeerDisconnected` (`DisconnectInfo.AdditionalData`).
/// E o mesmo caminho que a recusa por versao ja usa (`FioDaVersao`), so que na saida em vez da entrada.
/// ===============================================================================================================
///
/// HOJE HA UMA SO: a REENCARNACAO pelo Enma (`GameServer.EnmaReencarnar`). O personagem acabou, e o jogador volta pra
/// CRIACAO, na vaga que ele deixou, com a frase do Enma em cima (`Boot.AoSerDespedido`) -- e nao pro menu principal com
/// "DESCONECTADO DO SERVIDOR", que e o que uma queda faz.
///
/// O BLOCO: `uint marca` + `byte motivo` + `byte vaga` + `string frase`. Carga sem a marca, truncada ou de motivo
/// desconhecido le como "nao e despedida" (nulo), e a desconexao segue como queda comum -- o leitor nunca lanca.
/// </summary>
public static class FioDaDespedida
{
	/// <summary>Abre o bloco. No fio (little-endian) sao os bytes 4A 42 59 45 -- "JBYE". Lixo nao passa por acaso.</summary>
	public const uint Marca = 0x4559424A;

	/// <summary>
	/// O teto da frase, em caracteres. A carga de um `Disconnect` tem que caber num pacote (o LiteNetLib descarta a que
	/// passa de MTU - 8), e 240 caracteres acentuados ficam longe disso.
	/// </summary>
	public const int MaxTexto = 240;

	/// <summary>Por que o servidor despediu.</summary>
	public enum Motivo : byte
	{
		/// <summary>O personagem reencarnou: a conta volta pra criacao, na vaga que ele deixou.</summary>
		Reencarnou = 1,
	}

	/// <summary>A despedida lida: o motivo, a vaga (0 a 2) e a frase pra tela.</summary>
	public readonly record struct Despedida(Motivo Motivo, int Slot, string Texto);

	/// <summary>A carga do `Disconnect`, pronta.</summary>
	public static byte[] Escrever(Motivo motivo, int slot, string texto)
	{
		var w = new NetDataWriter();
		w.Put(Marca);
		w.Put((byte)motivo);
		w.Put((byte)Math.Clamp(slot, 0, 255));
		w.Put(texto.Length > MaxTexto ? texto[..MaxTexto] : texto, MaxTexto);
		return w.CopyData();
	}

	/// <summary>
	/// Le a despedida de uma carga -- nulo pra carga ausente, truncada, sem a marca ou de motivo desconhecido. Nunca
	/// lanca: uma carga ruim e "nao e despedida", e a desconexao segue como queda.
	/// </summary>
	public static Despedida? Ler(NetDataReader? r)
	{
		if (r == null || r.AvailableBytes < 4 + 1 + 1) return null;
		try
		{
			if (r.GetUInt() != Marca) return null;
			var motivo = (Motivo)r.GetByte();
			if (motivo != Motivo.Reencarnou) return null;
			int slot = r.GetByte();
			string texto = r.AvailableBytes > 0 ? r.GetString(MaxTexto) ?? "" : "";
			return new Despedida(motivo, slot, texto);
		}
		catch (Exception)
		{
			return null;
		}
	}
}
