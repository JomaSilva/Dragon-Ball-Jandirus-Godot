using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// O SNAPSHOT DA ZONA, EM PARTES QUE CABEM NUM PACOTE.
///
/// ============================ A ZONA CHEIA NAO SAIA -- E NINGUEM VIA (dono, 2026-09-07) ============================
/// "os npcs estao realmente invisiveis no torneio". Eram: o torneio faz nascer 31 NPCs de uma vez, a
/// Terra ja tinha uns 40 corpos, e o snapshot da zona era UM pacote com todos eles -- 71 corpos de
/// ~17 bytes passam dos 1020 bytes que o LiteNetLib aceita num pacote `Sequenced` (o MTU de 1024
/// menos o cabecalho; ver `NetPeer.GetMaxSinglePacketSize`). O `Send` lancava
/// `TooBigPacketException` trinta vezes por segundo, o resto do tique (o `TriggerUpdate`, o
/// `_quadrosInteiros`) morria junto, e NENHUM jogador na Terra recebia snapshot enquanto o torneio
/// existisse: os lutadores nunca chegavam ao cliente, os habitantes congelavam, e o dono, sem ver os
/// dois NPCs que o servidor jurava estarem lutando, concluiu que voavam alto demais ou eram
/// invisiveis. O servidor media tudo certo (a bancada `--torneioteste` ficava 100% verde) porque o
/// defeito morava na ultima linha do tique, a unica que nao deixa marca no mundo.
///
/// A REGRA: o snapshot sai em quantas partes forem precisas, cada uma abaixo do teto do menor peer da
/// zona. O leitor (`GameClient`, opcode `Snapshot`) nao muda: cada parte e um snapshot completo por
/// direito proprio -- carimbo, N corpos, M tiros -- e o cliente nunca deduziu "quem saiu" pela
/// ausencia num pacote (sair e o `Leave`, ver `MoveToZone`), entao receber a zona em dois pacotes
/// e o mesmo que recebe-la em um. O carimbo repetido tambem nao incomoda o relogio alinhado: ele
/// guarda o maximo em janela.
///
/// O ESPACO PASSA PELO MESMO FUNIL: la o recorte ja era por chunk e por jogador (o buffer nao se
/// compartilha), mas um chunk lotado estouraria igual.
/// ==================================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// Teto de bytes de uma parte quando nao ha peer nenhum pra perguntar: o menor MTU que o LiteNetLib
	/// tenta (576) menos o cabecalho do `Sequenced` (4). Sem peer nada e enviado, entao o numero so
	/// importa pra bancada e pro caso degenerado.
	/// </summary>
	public const int OrcamentoMinimoDoSnapshot = 572;

	/// <summary>
	/// O que o ultimo tique mandou: quantas partes a zona mais cheia precisou, o tamanho da maior parte e
	/// o orcamento usado. Zerado a cada tique pelo laco do snapshot. So bancada.
	/// </summary>
	internal (int Partes, int MaiorParte, int Orcamento) UltimoSnapshotDeTeste { get; private set; }

	/// <summary>
	/// O TETO DE UMA PARTE PRA ESTES DESTINATARIOS: o menor `GetMaxSinglePacketSize` entre eles. E o
	/// menor, e nao o do primeiro, porque o mesmo buffer vai pra todos -- o peer que ainda esta
	/// negociando MTU (576 no comeco da conexao) e quem manda.
	/// </summary>
	internal static int OrcamentoDoSnapshot(IReadOnlyList<ServerPlayer> destinatarios)
	{
		int orcamento = int.MaxValue;
		foreach (ServerPlayer p in destinatarios)
			if (p.Peer != null) orcamento = Math.Min(orcamento, p.Peer.GetMaxSinglePacketSize(DeliveryMethod.Sequenced));
		return orcamento == int.MaxValue ? OrcamentoMinimoDoSnapshot : orcamento;
	}

	/// <summary>
	/// MONTA AS PARTES sem enviar nada: cada uma e [opcode][carimbo][N corpos][corpos][M tiros][tiros],
	/// e nenhuma passa de <paramref name="orcamento"/> bytes -- a nao ser que um unico item ja passe
	/// sozinho, que e o caso em que nao ha o que partir. Publico pra bancada medir o corte de verdade,
	/// com os corpos de verdade, e nao uma copia da regra.
	/// </summary>
	internal List<NetDataWriter> PartesDoSnapshot(IReadOnlyList<ServerPlayer> corpos, IEnumerable<ProjetilState> tiros,
												  long agora, uint carimbo, int orcamento)
	{
		var partes = new List<NetDataWriter>();
		NetDataWriter w = Abrir(carimbo, out int posDosCorpos);
		int corposNaParte = 0;

		// OS CORPOS. Cada um e escrito e, se a parte passou do teto, DESescrito (o writer volta pra
		// posicao de antes) e vai abrir a proxima -- e mais barato que medir o tamanho antes, porque
		// o tamanho de um corpo varia (altitude, canal de Ki) e so o `Write` sabe.
		foreach (ServerPlayer pl in corpos)
		{
			int antes = w.Length;
			EstadoDe(pl, agora).Write(w);
			// +2: o contador (vazio) de tiros que fecha a parte
			if (w.Length + 2 > orcamento && corposNaParte > 0)
			{
				w.SetPosition(antes);
				Fechar(w, posDosCorpos, corposNaParte, tirosNaParte: 0);
				partes.Add(w);
				w = Abrir(carimbo, out posDosCorpos);
				corposNaParte = 0;
				EstadoDe(pl, agora).Write(w);
			}
			corposNaParte++;
		}

		// OS TIROS, na cauda da ultima parte -- e, se nao couberem, em partes proprias com zero corpos.
		// O contador dos tiros e escrito no fim (como o dos corpos), porque so no fim se sabe quantos
		// couberam.
		int posDosTiros = w.Length;
		w.Put((ushort)0);
		int tirosNaParte = 0;
		foreach (ProjetilState t in tiros)
		{
			int antes = w.Length;
			t.Write(w);
			if (w.Length > orcamento && (corposNaParte > 0 || tirosNaParte > 0))
			{
				w.SetPosition(antes);
				Fechar(w, posDosCorpos, corposNaParte, tirosNaParte, posDosTiros);
				partes.Add(w);
				w = Abrir(carimbo, out posDosCorpos);
				corposNaParte = 0;
				posDosTiros = w.Length;
				w.Put((ushort)0);
				tirosNaParte = 0;
				t.Write(w);
			}
			tirosNaParte++;
		}
		Fechar(w, posDosCorpos, corposNaParte, tirosNaParte, posDosTiros);
		partes.Add(w);
		return partes;

		static NetDataWriter Abrir(uint carimbo, out int posDosCorpos)
		{
			var w = Protocol.Begin(Protocol.S2C.Snapshot);
			w.Put(carimbo);
			posDosCorpos = w.Length;
			w.Put((ushort)0);   // preenchido no `Fechar`
			return w;
		}

		// Escreve os dois contadores nos lugares reservados. `posDosTiros < 0` = a parte fechou antes
		// do bloco de tiros existir: o contador (zero) vai no fim.
		static void Fechar(NetDataWriter w, int posDosCorpos, int corpos, int tirosNaParte, int posDosTiros = -1)
		{
			w.Data[posDosCorpos] = (byte)(corpos & 0xFF);
			w.Data[posDosCorpos + 1] = (byte)(corpos >> 8);
			if (posDosTiros < 0) { w.Put((ushort)0); return; }
			w.Data[posDosTiros] = (byte)(tirosNaParte & 0xFF);
			w.Data[posDosTiros + 1] = (byte)(tirosNaParte >> 8);
		}
	}

	/// <summary>Monta as partes pros destinatarios e manda todas, na ordem, pelo canal de estado.</summary>
	private void MandarSnapshot(IReadOnlyList<ServerPlayer> destinatarios, IReadOnlyList<ServerPlayer> corpos,
								IEnumerable<ProjetilState> tiros, long agora, uint carimbo)
	{
		int orcamento = OrcamentoDoSnapshot(destinatarios);
		List<NetDataWriter> partes = PartesDoSnapshot(corpos, tiros, agora, carimbo, orcamento);

		int maior = 0;
		foreach (NetDataWriter p in partes) maior = Math.Max(maior, p.Length);
		if (partes.Count >= UltimoSnapshotDeTeste.Partes)
			UltimoSnapshotDeTeste = (partes.Count, maior, orcamento);

		foreach (ServerPlayer pl in destinatarios)
		{
			if (pl.Peer == null) continue;
			foreach (NetDataWriter p in partes) pl.Peer.Send(p, Protocol.ChannelState, DeliveryMethod.Sequenced);
		}
	}

	/// <summary>
	/// OS TIROS DE UMA ZONA, prontos pro fio -- todos, ou so os perto de um ponto (o recorte por chunk
	/// do espaco, onde "todos os tiros da zona" seria todo tiro dado em qualquer canto da galaxia).
	/// </summary>
	private IEnumerable<ProjetilState> TirosDaZona(ulong hash, Vec2? perto = null)
	{
		if (!_projeteis.TryGetValue(hash, out List<Projetil>? l)) yield break;
		foreach (Projetil p in l)
		{
			if (perto is { } onde && !Espaco.PertoDeMim(onde, p.Pos)) continue;
			yield return new ProjetilState { Id = p.Id, Pos = p.Pos, Tipo = (byte)p.Tipo, Cauda = p.Cauda };
		}
	}
}
