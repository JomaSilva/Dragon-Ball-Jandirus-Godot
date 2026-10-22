using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Magic;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// **O DISCO DO DRAGON RADAR, DO LADO DE QUEM SABE** -- o que o servidor conta a quem tem o radar.
///
/// O dono (2026-10-10): *"quando tiver radar do dragao no canto inferior direito vai aparecer um circulo
/// igual um radar com as bolinhas amarelas a onde as esferas estao em relacao a voce"*.
///
/// ============================ O SERVIDOR DECIDE, E SO CONTA A QUEM TEM O APARELHO ============================
/// O disco e desenhado SO com o que chega no `S2C.RadarDeEsferas`, e esse pacote so sai pra quem tem o
/// Dragon Radar na mochila (<see cref="TemDragonRadar"/>). Sem o aparelho nao sai byte nenhum -- nem um
/// "escondido": o cliente nao tem o que desenhar e nem o que vazar. Perdeu o radar, sai UM pacote dizendo
/// "desligado" e o disco some.
///
/// A REGRA DO QUE APARECE E A DO VERB DE RADAR (<see cref="UsarORadar"/>, `Tier 1.5.dm:234-243`): so esfera
/// ACORDADA, e so deste mundo. A esfera na MAO de alguem anda com a pessoa, nos dois: o verb media do ponto
/// em que ela foi pega, e passou a perguntar ao mesmo <see cref="OndeEstaAEsfera"/> que o disco.
///
/// ============================ ESTADO, E NAO EVENTO: SO SAI QUANDO O DISCO MUDARIA ============================
/// Uma vez por segundo, pra cada jogador com o radar, o servidor monta o que o disco dele mostraria e
/// compara com o que ja mandou (<see cref="_radarMandado"/>). Igual, nao manda. Isso cobre de uma vez
/// todas as portas que mexem numa esfera -- pegar, largar, espalhar, acordar, a policia de planeta, trocar
/// de zona, ganhar e perder o radar -- sem um gancho em cada uma, que e a regra que este projeto ja pagou
/// por esquecer num chamador. Com as sete paradas no chao e um pacote ao chegar e mais nenhum; com uma
/// esfera na mao de alguem que anda, um por segundo (a posicao dela viaja por TILE:
/// <see cref="RadarDeEsferas.PassoDaCarregada"/>), de uns 70 bytes, so pra quem tem radar naquele mundo.
///
/// AS SUPER ESFERAS FICAM DE FORA, e e escolha: elas tem instrumento proprio (o sinal dourado, em CELULAS
/// de sistema e sem direcao, `GameServer.SuperEsferas.MandarSupers`), e a posicao delas e o segredo que
/// aquele desenho protege -- um ponto com rumo no disco seria o mapa do tesouro que ele nao entrega.
/// ================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>DEFEITO INJETADO (bancada `--radarteste`): o servidor manda os sinais a quem NAO tem o Dragon Radar. Falso em jogo, sempre.</summary>
	internal bool RadarSemPortaoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--radarteste`): esfera APAGADA (set que ainda se refaz) tambem vira sinal. Falso em jogo, sempre.</summary>
	internal bool RadarVeApagadaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--radarteste`): esfera de OUTRO mundo tambem vira sinal. Falso em jogo, sempre.</summary>
	internal bool RadarVeOutroMundoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--radarteste`): o pacote sai todo segundo, mudando ou nao o que o disco mostra. Falso em jogo, sempre.</summary>
	internal bool RadarSemComparaDeTeste;

	/// <summary>
	/// O QUE CADA TELA ESTA MOSTRANDO: pra cada conexao com o disco LIGADO, o corpo e a assinatura do ultimo
	/// pacote. Ausente = aquela tela nao tem disco.
	///
	/// POR CONEXAO, e nao por id de corpo: o que se guarda e o que a TELA de alguem recebeu, e a tela e da
	/// conexao -- o id de sessao se reusa, e a mesma conexao troca de corpo (outro personagem, a mente). O
	/// CORPO vai junto porque corpo novo na mesma tela e disco novo: manda sempre, sem comparar.
	/// </summary>
	private readonly Dictionary<NetPeer, (ServerPlayer Corpo, ulong Assinatura)> _radarMandado = [];

	/// <summary>O `S2C.RadarDeEsferas` que saiu, por destinatario: pra quem e o fio. Nulo fora da bancada (o molde do `EscutaDoFioDasEsferas`).</summary>
	internal static List<(int Para, byte[] Fio)>? EscutaDoRadar;

	/// <summary>
	/// ESTE CORPO TEM O DRAGON RADAR? A pergunta do disco -- e a mesma que o verb faz (`UsarORadar`): estar
	/// com o item, sem liga/desliga (a mochila vai pro save; um bit de "ligado" nao iria).
	/// </summary>
	private static bool TemDragonRadar(ServerPlayer pl) => pl.Mochila.Quantos(CatalogoDeItens.Radar) > 0;

	/// <summary>DEFEITO INJETADO (bancada `--radarteste`): o verb de radar mede a esfera CARREGADA do ponto em que ela foi pega, e nao de onde esta quem a carrega -- o que este port fez ate 2026-10-10. Falso em jogo, sempre.</summary>
	internal bool RadarMedeDeOndeFoiPegaDeTeste;

	/// <summary>
	/// ONDE UMA ESFERA ESTA AGORA: no chao, onde caiu; na mao de alguem, onde a PESSOA esta (o `X`/`Y` dela
	/// param no ponto em que foi pega ate cair de novo). Uma resposta so pro verb de radar e pro disco.
	/// </summary>
	private Vec2 OndeEstaAEsfera(Esfera e) =>
		e.Portador != 0 && !RadarMedeDeOndeFoiPegaDeTeste && _players.TryGetValue(e.Portador, out ServerPlayer? quem)
			? quem.Pos
			: new Vec2(e.X, e.Y);

	/// <summary>
	/// O QUE O RADAR DESTE JOGADOR SENTE AGORA: as esferas ACORDADAS do mundo em que ele esta.
	///
	/// A esfera na mao de alguem vale a posicao de QUEM A CARREGA (no centro do tile dele); a que esta na
	/// mao do proprio jogador leva a marca `Comigo`, e o disco a desenha no centro.
	/// </summary>
	private List<SinalDeEsfera> SinaisDoRadar(ServerPlayer pl)
	{
		var sinais = new List<SinalDeEsfera>();
		foreach (Esfera e in _esferas)
		{
			if (e.Zona.Hash != pl.Zone.Hash && !RadarVeOutroMundoDeTeste) continue;
			if (SetPorId(e.Set) is not { } s || (!SetAtivo(s) && !RadarVeApagadaDeTeste)) continue;
			if (sinais.Count >= RadarDeEsferas.TetoDeSinais) break;

			if (e.Portador != 0 && _players.TryGetValue(e.Portador, out ServerPlayer? quem))
			{
				Vec2 onde = RadarDeEsferas.PosicaoDaCarregada(quem.Pos);
				sinais.Add(new SinalDeEsfera(onde.X, onde.Y, Carregada: true, Comigo: quem.Id == pl.Id));
			}
			else sinais.Add(new SinalDeEsfera(e.X, e.Y, Carregada: false, Comigo: false));
		}
		AcrescentarSinaisNegros(pl, sinais);
		return sinais;
	}

	/// <summary>
	/// UMA VEZ POR SEGUNDO, no fim do <see cref="TickDasEsferas"/> (depois de a policia e as quedas terem
	/// posto cada esfera no lugar dela neste segundo). Ver o cabecalho: estado comparado, e nao evento.
	/// </summary>
	private void TickDoRadarDeEsferas()
	{
		if (RadarDeTeste && !_radarTestou && Jogadores.FirstOrDefault() is { } testador)
		{
			_radarTestou = true;
			RodarBancadaDoRadar(testador);
		}

		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.Peer is not { } tela) continue;   // corpo sem dono na tela nao tem disco

			bool aceso = _radarMandado.TryGetValue(tela, out (ServerPlayer Corpo, ulong Assinatura) antes);
			if (!TemDragonRadar(pl) && !RadarSemPortaoDeTeste)
			{
				// SEM O RADAR (largou, foi saqueado, a tela passou pra um corpo que nao tem): se o disco
				// estava aceso naquela tela, UM pacote dizendo "desligado". Quem nunca teve nao recebe nada.
				if (aceso)
				{
					MandarRadar(pl, tela, ligado: false, []);
					_radarMandado.Remove(tela);
				}
				continue;
			}

			List<SinalDeEsfera> sinais = SinaisDoRadar(pl);
			ulong assinatura = AssinaturaDoRadar(pl.Zone.Hash, sinais);
			if (aceso && antes.Corpo == pl && antes.Assinatura == assinatura && !RadarSemComparaDeTeste) continue;

			// TIRA ANTES DE POR: a chave e a conexao, e uma reconexao do mesmo endereco e "igual" a antiga pro
			// dicionario -- escrever por cima deixaria o objeto da conexao MORTA como chave.
			_radarMandado.Remove(tela);
			_radarMandado[tela] = (pl, assinatura);
			MandarRadar(pl, tela, ligado: true, sinais);
		}

		// CONEXAO QUE CAIU SAI DA CONTA. Raro (so ha entrada aqui pra quem tem o disco aceso).
		if (_radarMandado.Count > 0)
			foreach (NetPeer tela in _radarMandado.Keys.ToList())
				if (tela.ConnectionState != ConnectionState.Connected) _radarMandado.Remove(tela);
	}

	/// <summary>
	/// A assinatura do que o disco mostraria: a zona e os sinais, na ordem em que saem. FNV-1a, como o
	/// `ZoneKey.Hash` -- estavel, barato, e sem alocar. A zona entra porque trocar de mundo com os mesmos
	/// zero sinais ainda e um disco novo (o cliente esquece os sinais ao trocar de zona).
	/// </summary>
	private static ulong AssinaturaDoRadar(ulong zona, List<SinalDeEsfera> sinais)
	{
		const ulong primo = 1099511628211UL;
		ulong h = 14695981039346656037UL ^ zona;
		h *= primo;
		foreach (SinalDeEsfera s in sinais)
		{
			h = (h ^ BitConverter.SingleToUInt32Bits(s.X)) * primo;
			h = (h ^ BitConverter.SingleToUInt32Bits(s.Y)) * primo;
			h = (h ^ (uint)((s.Carregada ? 1 : 0) | (s.Comigo ? 2 : 0))) * primo;
		}
		return (h ^ (uint)sinais.Count) * primo;
	}

	/// <summary>O pacote. O corpo dele mora em <see cref="FioDoRadarDeEsferas"/>, que o `GameClient` le.</summary>
	private static void MandarRadar(ServerPlayer pl, NetPeer tela, bool ligado, List<SinalDeEsfera> sinais)
	{
		NetDataWriter w = Protocol.Begin(Protocol.S2C.RadarDeEsferas);
		FioDoRadarDeEsferas.Escrever(w, ligado, sinais);

		EscutaDoRadar?.Add((pl.Id, w.CopyData()));
		tela.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
