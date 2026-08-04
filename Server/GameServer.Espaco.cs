using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// O ESPACO, LADO DO SERVIDOR.
///
/// O QUE O SERVIDOR GUARDA DO UNIVERSO: nada. Nem uma tabela de planetas, nem um mapa de
/// estrelas. O que existe numa chunk e funcao pura de `seed + chunk` (ver <see cref="Espaco"/>),
/// entao ele CALCULA quando precisa e o cliente calcula a mesma coisa sozinho. O unico dado que
/// trafega e "estes planetas estao perto de voce" -- e mesmo esse e derivavel; vai pela rede so
/// pra o cliente nao precisar varrer chunk atras de planeta pre-feito distante.
///
/// INTERESSE POR CHUNK, NAO POR ZONA. O espaco inteiro e UMA zona: fatia-lo em varias traria de
/// volta o problema dos setores-por-z. Quem corta o trafego e a distancia em chunks, e o corte
/// entra no snapshot (ver a chamada a <see cref="Espaco.PertoDeMim"/> no laco de envio).
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A SEED DO UNIVERSO. Um numero, e dele sai todo o resto -- posicao de planeta, bioma,
	/// tamanho. Fixo por enquanto; quando virar por servidor, e so ler do disco aqui.
	/// </summary>
	public const ulong SeedDoUniverso = 20260802;

	public static ZoneKey ZonaDoEspaco => Espaco.Zona(SeedDoUniverso);

	/// <summary>
	/// DECOLAR. Sai da superficie do planeta em que se esta e aparece no espaco, logo acima
	/// dele.
	///
	/// So funciona de um planeta que EXISTE no mapa do universo: nao da pra decolar do Outro
	/// Mundo nem da dimensao mental, e isso e regra de mundo, nao limitacao.
	/// </summary>
	private void Decolar(ServerPlayer pl)
	{
		if (Espaco.EhEspaco(pl.Zone)) { Avisar(pl, "voce ja esta no espaco."); return; }
		if (pl.CloneId != 0) { Avisar(pl, "primeiro saia da sua mente."); return; }

		// MUNDO GERADO SOBE POR OUTRO CAMINHO -- e sem esta linha ele nao subia de jeito nenhum.
		//
		// `DecolarDeProcedural` existia, era publico, e nao era chamado por NINGUEM (o repo inteiro
		// so tinha a definicao). Como a busca abaixo so varre os sete pre-feitos, um planeta
		// sorteado -- cujo nome e "Verdejante-1042" e nao esta em lista nenhuma -- sempre caia no
		// "este lugar nao fica no mapa do universo". Quem pousasse num mundo gerado ficava preso
		// ali PARA SEMPRE, e o unico jeito de sair era morrer.
		//
		// Ficou invisivel enquanto nada convidava a pousar num gerado. A carta estelar convida: ela
		// desenha centenas deles e poe um botao "Viajar" em cada um.
		if (DecolarDeProcedural(pl)) return;

		PlanetaNoEspaco? daqui = null;
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (string.Equals(p.Nome, pl.Zone.Name, StringComparison.OrdinalIgnoreCase)) { daqui = p; break; }

		if (daqui == null)
		{
			Avisar(pl, $"nao da pra decolar de {pl.Zone.Name} -- este lugar nao fica no mapa do universo.");
			return;
		}

		pl.PlanetaDeOrigem = daqui.Value.Nome;
		MoveToZone(pl.Id, ZonaDoEspaco, Espaco.PontoDeDecolagem(daqui.Value));
		// A CHUNK DE CHEGADA JA E A ATUAL: sem isto o tick veria "mudou de chunk" no quadro
		// seguinte e mandaria a vizinhanca DE NOVO, duplicada, a cada decolagem.
		pl.ChunkAtual = ChunkId.De(pl.Pos);
		MandarVizinhanca(pl);

		Avisar(pl, $"voce deixa {daqui.Value.Nome} para tras. O silencio do espaco.");
		GD.Print($"[server] {pl.Name} decolou de {daqui.Value.Nome} -> chunk {ChunkId.De(pl.Pos)}");
	}

	/// <summary>
	/// POUSAR. Encostou num planeta, desce nele -- sem menu e sem porta, como o dono pediu.
	///
	/// Rodado no tick, e nao pedido pelo cliente: quem decide onde o corpo esta e o servidor, e
	/// deixar o cliente dizer "pousei em Namek" seria um teleporte de graca.
	/// </summary>
	private void TickDoEspaco(ServerPlayer pl)
	{
		if (!Espaco.EhEspaco(pl.Zone)) return;

		ChunkId agora = ChunkId.De(pl.Pos);
		if (agora != pl.ChunkAtual)
		{
			pl.ChunkAtual = agora;
			MandarVizinhanca(pl);
		}

		if (Espaco.PlanetaSob(SeedDoUniverso, pl.Pos) is not { } destino) return;

		// PROCEDURAL AGORA TEM CHAO. O servidor gera a MESMA superficie que o cliente, a partir
		// da mesma seed -- e o motivo de o gerador morar no Core: uma funcao, duas pontas, zero
		// byte de mapa na rede. Aqui ele so precisa da colisao (pra saber onde e parede) e do
		// ponto de chegada; quem desenha e a cena.
		if (!destino.Premade) { PousarEmProcedural(pl, destino); return; }

		MoveToZone(pl.Id, ZoneKey.Premade(destino.Nome), SpawnPos);
		Avisar(pl, $"voce pousa em {destino.Nome}.");
		GD.Print($"[server] {pl.Name} pousou em {destino.Nome}");
	}

	/// <summary>
	/// O SNAPSHOT DO ESPACO: um por jogador, recortado por CHUNK.
	///
	/// Nas zonas normais o mesmo buffer serve pra todo mundo, porque quem esta na zona ve todo
	/// mundo da zona. Aqui nao: o universo e continuo e a zona inteira e uma so, entao o que
	/// cada um enxerga depende de ONDE ele esta. Custa um buffer por jogador -- e o preco de o
	/// espaco nao ter fim.
	/// </summary>
	private void SnapshotDoEspaco(List<ServerPlayer> zona, long agora)
	{
		foreach (ServerPlayer eu in zona)
		{
			if (eu.Peer == null) continue;

			var vizinhos = new List<ServerPlayer>();
			foreach (ServerPlayer o in zona)
				if (Espaco.PertoDeMim(eu.Pos, o.Pos)) vizinhos.Add(o);

			var w = Protocol.Begin(Protocol.S2C.Snapshot);
			w.Put((ushort)vizinhos.Count);
			// A MESMA FABRICA da zona normal. Esta copia tinha ficado pra tras -- ver `EstadoDe`.
			foreach (ServerPlayer pl in vizinhos) EstadoDe(pl, agora).Write(w);

			eu.Peer.Send(w, Protocol.ChannelState, DeliveryMethod.Sequenced);
		}
	}

	/// <summary>
	/// Os planetas que o cliente precisa desenhar agora.
	///
	/// So sai quando a CHUNK muda -- e a mesma disciplina do corpo e dos atributos. Andar dentro
	/// da mesma chunk nao muda o que se ve.
	/// </summary>
	/// <remarks>
	/// TINHA UM PARAMETRO `forcar` QUE NINGUEM LIA. Ele existia pra "forcar" o envio numa decolagem
	/// que cai na mesma chunk -- mas quem decide se manda e o CHAMADOR (este metodo sempre manda),
	/// entao ele nunca teve efeito nenhum. Saiu junto com o carimbo de `ChunkAtual`, que e o que
	/// resolvia o problema de verdade.
	/// </remarks>
	private static void MandarVizinhanca(ServerPlayer pl)
	{
		if (pl.Peer == null) return;

		ChunkId c = ChunkId.De(pl.Pos);
		List<PlanetaNoEspaco> perto = Espaco.PorPerto(SeedDoUniverso, c);

		var w = Protocol.Begin(Protocol.S2C.Vizinhanca);
		w.Put(SeedDoUniverso);
		w.Put(c.X);
		w.Put(c.Y);
		w.Put((byte)Math.Min(perto.Count, 255));
		foreach (PlanetaNoEspaco p in perto.Take(255))
		{
			w.Put(p.Nome);
			w.Put(p.Pos.X);
			w.Put(p.Pos.Y);
			w.Put(p.Raio);
			w.Put(p.Seed);
			w.Put(p.Premade);
		}
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
