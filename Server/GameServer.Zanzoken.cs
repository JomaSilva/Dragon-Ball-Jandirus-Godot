using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// O ZANZOKEN COMO DESLOCAMENTO -- piscar pra um ponto do chao, deixando a miragem pra tras.
///
/// ============================ DE ONDE VEM ============================
/// E o `Zanzoken_Dodge` do original (`misc.dm:66-68`, nivel 3 da Afterimage Technique): "use the
/// skill to immediately teleport to a free tile around you". O dono pediu a versao com mira --
/// "faça com quem tenha after image ao dar double click no chao ele de o teleporte e deixando a
/// miragem a onde estava" -- que e a mesma mecanica com destino escolhido em vez de sorteado.
/// =====================================================================
///
/// TUDO QUE IMPORTA E DECIDIDO AQUI. O cliente manda um PONTO e mais nada: se ele decidisse, a
/// tecnica seria teleporte livre pra qualquer cliente modificado -- que e a definicao de
/// atravessar parede de graca.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// ALCANCE DA PISCADA, em pixels. Seis tiles.
	///
	/// O original teleporta pra um tile LIVRE em volta -- alcance de 1. Aqui vale mais porque o
	/// port tem movimento livre e mira do mouse: um tile de distancia num jogo sem grade nao e
	/// escapar de nada, e o gesto (duplo clique num ponto) so faz sentido se der pra apontar
	/// pra algum lugar que valha a pena.
	/// </summary>
	private const float AlcanceDoZanzo = 192f;

	/// <summary>Fracao do Ki maximo por piscada. Metade do que custa uma investida de soco.</summary>
	private const double CustoZanzoKi = 0.025;

	/// <summary>Espera entre duas piscadas, em ms. Sem ela a tecnica vira voo.</summary>
	private const long RecargaZanzoMs = 900;

	/// <summary>
	/// O jogador pediu pra piscar ate um ponto.
	///
	/// AS RECUSAS SAO MUDAS por escolha: elas acontecem no meio de uma luta, e encher o chat de
	/// "sem Ki" / "muito longe" a cada duplo clique tira a atencao de onde ela precisa estar. A
	/// unica que FALA e a de nao ter a skill -- essa e a que o jogador nao tem como deduzir.
	/// </summary>
	private void Zanzoken(ServerPlayer pl, Vec2 destino)
	{
		if (pl.Livro?.Sabe(PathDoZanzoken) != true)
		{
			Avisar(pl, "você não sabe se mover assim -- é o que a Afterimage Technique ensina.");
			return;
		}

		if (pl.Ficha.KO || pl.Ficha.dead) return;

		long agora = NowMs();
		if (agora < pl.ZanzoLivreEm) return;

		Vec2 de = pl.Pos;
		Vec2 d = destino - de;
		float dist = d.Length;
		if (dist < 8f) return;   // clicou nos proprios pes

		// LONGE DEMAIS ENCURTA, nao recusa. Recusar por meio pixel obrigaria o jogador a medir
		// distancia com o mouse no meio de uma briga; encurtar entrega o gesto que ele fez, ate
		// onde a tecnica alcanca.
		if (dist > AlcanceDoZanzo) destino = de + d.Normalized() * AlcanceDoZanzo;

		double custo = pl.Ficha.MaxKi * CustoZanzoKi;
		if (pl.Ficha.Ki < custo) return;

		// PAREDE MANDA MAIS QUE A TECNICA -- a mesma regra da investida do soco. Sem isto o
		// Zanzoken vira a forma barata de entrar em qualquer casa fechada.
		ZoneCollision? mapa = _catalogo?.Get(pl.Zone)?.Mapa;
		if (mapa != null && (MoveRules.Occupied(mapa, destino) || MoveRules.PathOccupied(mapa, de, destino)))
			return;

		pl.Ficha.Ki -= custo;
		pl.Pos = destino;
		pl.Facing = MoveRules.FacingFrom(d, pl.Facing);
		pl.ZanzoLivreEm = agora + RecargaZanzoMs;

		// O MESMO CUIDADO DA INVESTIDA: os pacotes que o cliente ja mandou falam da posicao velha,
		// e sem o carimbo de sequencia o servidor os trataria como cliente errado e puxaria o
		// corpo de volta (ver `GameServer.Input`).
		pl.LastInputMs = agora;
		pl.CorrecaoEsperadaAte = agora + 500;
		pl.SeqDoTeleporte = pl.SeqInput;
		pl.OrcamentoPx = 0;

		var corr = Protocol.Begin(Protocol.S2C.Correction);
		corr.Put(pl.SeqInput);
		corr.PutVec(pl.Pos);
		pl.Peer?.Send(corr, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		AnunciarZanzo(pl, de);
	}

	/// <summary>
	/// Conta a piscada pra ZONA INTEIRA, com o ponto de PARTIDA.
	///
	/// A origem viaja no pacote porque e onde a miragem nasce, e quando ele chega o corpo ja esta
	/// no destino -- quem recebesse so o id desenharia o vulto em cima do jogador, que e o oposto
	/// de "ficou pra tras". Vale pra quem piscou tambem: e o mesmo pacote, e assim as duas pontas
	/// desenham a mesma coisa no mesmo lugar.
	///
	/// Canal NAO confiavel: perder um vulto custa meio segundo de efeito, e a piscada e uma coisa
	/// de combate, onde o trafego ja e o que mais aperta.
	/// </summary>
	private void AnunciarZanzo(ServerPlayer pl, Vec2 de)
	{
		// QUEM ESTA INVISIVEL NAO DEIXA VULTO.
		//
		// A miragem e uma FOTO do corpo, opaca, parada num ponto: um jogador escondido que piscasse
		// (ou investisse) entregava a propria posicao com ela. O sigilo do corpo nao pode depender
		// de o jogador lembrar de nao usar a tecnica.
		//
		// O corte e aqui e nao no cliente: mandar o pacote e depois pedir pra ele nao desenhar seria
		// entregar a posicao pra qualquer cliente modificado, que e a mesma regra do BP escondido.
		if (EstaOculto(pl.Id)) return;

		var w = Protocol.Begin(Protocol.S2C.Zanzo);
		w.Put(pl.Id);
		w.PutVec(de);
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelState, DeliveryMethod.Unreliable);
	}
}
