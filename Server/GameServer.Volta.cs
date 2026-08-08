using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A VOLTA DO PLANETA -- chegar numa borda e sair pela oposta.
///
/// ============================ DE ONDE VEM ============================
/// Do proprio original, e nao de uma invencao: `turf/Other/Stars_Exit` (`NewTurfs.dm:33-53`).
///
///     if(x &lt;= 3) nx = world.maxx - 3
///     else if(x &gt;= world.maxx - 2) nx = 4
///     if(y &lt;= 3) ny = world.maxy - 3
///     else if(y &gt;= world.maxy - 2) ny = 4
///     if(nx != x || ny != y)
///       M.loc = locate(nx, ny, z)
///       return 0   //ja atravessou pro outro lado; o passo na borda nao acontece
///
/// La esse turf so nascia na beirada de PLANETA GERADO (`ProceduralSpace.dm:1282`, com o
/// comentario "borda do planetoide: da a VOLTA (wrap)"); os mapas desenhados a mao terminavam em
/// parede. O dono pediu a regra pra todo planeta -- "ao chegar na borda deveria loopar pro outro
/// lado do planeta" -- e ela e a mesma: um planeta e redondo, tenha o mapa sido desenhado ou
/// sorteado.
/// =====================================================================
///
/// ============================ POR QUE NAO E UM TORO ============================
/// Um toro de verdade (a costura sendo continua, com o mundo desenhado dos dois lados dela)
/// quebraria tres coisas ao mesmo tempo, e a auditoria mediu as tres:
///
///   * o `MoveRules.ValidateStep` veria um passo de 16.000 px, recusaria, e o clamp devolveria uma
///     posicao NA DIRECAO CONTRARIA -- uma correcao por pacote, trinta por segundo, o corpo preso
///     tremendo na costura;
///   * o corpo remoto INTERPOLA, entao ele atravessaria o mapa inteiro desenhado em 33 ms;
///   * e o mesmo corpo entraria em pose de CORRIDA, porque a corrida e deduzida da distancia
///     percorrida entre dois snapshots.
///
/// O DM nao tinha nada disso porque a regra dele nao e um toro: e um TELEPORTE com margem, que
/// CANCELA o passo. O jogador ve um corte, nao uma emenda -- e o corte ja tem toda a maquinaria
/// pronta neste port (o carimbo de sequencia, a correcao confiavel, e o `Cravar` do corpo remoto).
/// ==============================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// A FAIXA DA VOLTA, em celulas. `x &lt;= 3` do DM, em base zero.
	///
	/// Tem que ser MAIOR que a margem indestrutivel da borda (<see cref="ZoneCollision.MargemDaBorda"/>):
	/// o jogador precisa dar a volta ANTES de chegar no chao que ninguem pode quebrar, senao ele
	/// para encostado numa parede invisivel de dois tiles e a volta nunca acontece.
	/// </summary>
	private const int FaixaDaVolta = 3;

	/// <summary>Onde o corpo REAPARECE, contado da borda oposta. `nx = 4` / `maxx - 3` do DM.</summary>
	private const int PousoDaVolta = 4;

	// ============================ O `ZonaEhPlaneta` DAQUI VIROU `Espaco.EhPlaneta` ============================
	// Ele morava nestas linhas e foi DELETADO, nao copiado: o cliente passou a precisar da mesma
	// resposta (a musica e o tremor de uma transformacao alcancam o planeta inteiro -- ver
	// `Client/Transformacao.cs`), e duas definicoes de "isto e um planeta" envelheceriam separadas.
	//
	// UM DEFEITO FOI JUNTO. A versao daqui abria com `if (Kind == KindProcedural) return true;` e so
	// DEPOIS perguntava pelo espaco -- mas a zona do espaco E procedural
	// (`Espaco.Zona` = `ZoneKey.Procedural(NomeDoEspaco, seed)`), entao ela nunca chegava naquela
	// linha e o espaco respondia "sou planeta". Ficava inofensivo por acidente: o `DarAVolta` logo
	// abaixo desiste por falta de mapa de colisao. A do Core pergunta pelo espaco DENTRO do caso
	// procedural, que e onde a pergunta cabe.
	// ====================================================================================================

	/// <summary>
	/// O CORPO CHEGOU NA BEIRADA? Entao ele sai pela outra ponta.
	///
	/// Chamado DEPOIS do passo validado: a volta nao e um passo, e o que acontece com quem terminou
	/// o passo na faixa. Devolve `true` quando teleportou -- e ai quem chamou nao deve mandar
	/// correcao propria, porque esta aqui ja mandou a certa.
	///
	/// Cada eixo e conferido sozinho, como no DM: quem chega numa QUINA da a volta nos dois de uma
	/// vez, e vai parar na quina oposta.
	/// </summary>
	private bool DarAVolta(ServerPlayer pl)
	{
		if (!Espaco.EhPlaneta(pl.Zone)) return false;

		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		if (mapa == null || mapa.Width <= FaixaDaVolta * 4 || mapa.Height <= FaixaDaVolta * 4) return false;

		const int T = ZoneCollision.TileSize;
		int cx = (int)Math.Floor(pl.Pos.X / T);
		int cy = (int)Math.Floor((pl.Pos.Y + MoveRules.FeetOffsetY) / T);

		int nx = cx, ny = cy;
		if (cx < FaixaDaVolta) nx = mapa.Width - 1 - PousoDaVolta;
		else if (cx >= mapa.Width - FaixaDaVolta) nx = PousoDaVolta;
		if (cy < FaixaDaVolta) ny = mapa.Height - 1 - PousoDaVolta;
		else if (cy >= mapa.Height - FaixaDaVolta) ny = PousoDaVolta;

		if (nx == cx && ny == cy) return false;

		// O RESTO DO TILE VIAJA JUNTO. Trocar so a celula jogaria o corpo pro canto dela, e num
		// mapa de 500 tiles a volta ficaria com um solavanco de meio tile toda vez. Guardando a
		// fracao, quem atravessa a costura andando reto continua no mesmo lugar do tile.
		float fx = pl.Pos.X - cx * T;
		float fy = pl.Pos.Y - cy * T;
		var destino = new Vec2(nx * T + fx, ny * T + fy);

		// PAREDE MANDA MAIS QUE A VOLTA. Se o outro lado for muro, empurra pra dentro ate achar
		// chao -- e se nao achar em oito tiles, a volta simplesmente nao acontece e o jogador para
		// na beirada, que e o comportamento de antes.
		if (MoveRules.Occupied(mapa, destino))
		{
			bool achou = false;
			for (int passo = 1; passo <= 8 && !achou; passo++)
			{
				int ax = nx + (nx < mapa.Width / 2 ? passo : -passo);
				int ay = ny + (ny < mapa.Height / 2 ? passo : -passo);
				foreach (Vec2 tenta in new[]
				{
					new Vec2(ax * T + fx, ny * T + fy),
					new Vec2(nx * T + fx, ay * T + fy),
					new Vec2(ax * T + fx, ay * T + fy),
				})
					if (!MoveRules.Occupied(mapa, tenta)) { destino = tenta; achou = true; break; }
			}
			if (!achou) return false;
		}

		Vec2 saiuDe = pl.Pos;
		pl.Pos = destino;

		// O MESMO CARIMBO DE TODO TELEPORTE DO SERVIDOR. Sem ele os pacotes de input que o cliente
		// ja mandou (da posicao velha, do outro lado do mundo) seriam lidos como cliente errado, e
		// a validacao puxaria o corpo de volta pra costura -- trinta vezes por segundo.
		pl.SeqDoTeleporte = pl.SeqInput;
		pl.CorrecaoEsperadaAte = NowMs() + 500;
		pl.OrcamentoPx = 0;
		pl.LastInputMs = NowMs();

		var w = Protocol.Begin(Protocol.S2C.Correction);
		w.Put(pl.SeqInput);
		w.PutVec(pl.Pos);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		// SEM MIRAGEM E SEM SOM: nao e uma tecnica, e a forma do mundo. Quem assiste ve o corpo
		// sumir de uma beirada e aparecer na outra -- e o `RemotePlayer` ja crava saltos deste
		// tamanho em vez de interpolar (ver `LimiteDeSalto`), entao ninguem atravessa o mapa
		// desenhado no caminho.
		GD.Print($"[server] {pl.Name} deu a volta em {pl.Zone.Name}: "
				 + $"({saiuDe.X:0},{saiuDe.Y:0}) -> ({pl.Pos.X:0},{pl.Pos.Y:0})");
		return true;
	}

	/// <summary>
	/// O MAPA DE COLISAO DE UMA ZONA, seja ela de arquivo ou sorteada.
	///
	/// Os 18 lugares que precisam de colisao escrevem `_catalogo?.Get(...)?.Mapa` na mao, e por
	/// isso NENHUM deles funciona em planeta gerado -- o catalogo so conhece os mapas de arquivo.
	/// A volta precisa dos dois, entao ela pergunta pelos dois.
	/// </summary>
	private ZoneCollision? MapaDaZonaOuCatalogo(ZoneKey zona) =>
		zona.Kind == ZoneKey.KindProcedural ? MapaDaZona(zona) : _catalogo?.Get(zona)?.Mapa;
}
