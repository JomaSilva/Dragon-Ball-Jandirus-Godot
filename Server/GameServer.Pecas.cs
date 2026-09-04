using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// ============================ AS PECAS DE CORPO NO CHAO -- o `/obj/bodyparts` do servidor ============================
/// *"ao personagens perderem membros aparentemente nao esta spawnando o body part no chao como
/// deveria"* -- o relato do dono, e ele tinha DUAS causas, uma em cada metade deste arquivo:
///
///   1. **SO O SOCO ARRANCAVA.** O dano em area (`EspalharDanoG3`, o `SpreadDamage`) e o dano direto
///      (`FerirUmMembroG10`, o `damage_mob`) zeravam o membro e paravam: sem `LopLimb`, sem peca. A
///      cauda virou o `CombatState.Ferir` (o `DamageMe` inteiro), e o que ela produz chega aqui pelo
///      gancho `AoDecepar` -- <see cref="SoltarPecas"/> e o `SpawnLop()` de todo funil.
///   2. **A PECA ERA UM EVENTO.** O `S2C.Decalque` saia uma vez, pra quem estava na zona naquele
///      instante, e mais nada: quem entrava depois, relogava ou voltava do Outro Mundo nunca a via.
///      Agora o servidor GUARDA a peca pelos 600 s do DM e a reapresenta a quem chega
///      (<see cref="MandarPecas"/>), como faz com o cenario derrubado e as construcoes.
///
/// O que a peca continua NAO sendo esta escrito em <see cref="Jandirus.Core.Combat.PecaNoChao"/>: um
/// item. `Get`/`Drop`/`Eat` (`mobparts.dm:336-394`) ficam de fora, e a divergencia e declarada.
/// ==============================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// AS PECAS NO CHAO, POR ZONA (a chave e o `ZoneKey.Hash`, como no `_zones`). Ordenada por queda:
	/// a mais velha e a primeira, que e a que o teto empurra e a que o prazo vence antes.
	/// </summary>
	private readonly Dictionary<ulong, List<PecaNoChao>> _pecasNoChao = [];

	/// <summary>
	/// QUANTAS PECAS JA CAIRAM NESTA ZONA desde o boot -- o ORDINAL que semeia o espalhamento
	/// (<see cref="PecasNoChao.Espalhar"/>). E contagem e nao `lista.Count`: a lista encolhe pelo teto
	/// e pelo prazo, e duas pecas que nascessem com o mesmo ordinal no mesmo ponto cairiam no mesmo
	/// pixel.
	/// </summary>
	private readonly Dictionary<ulong, int> _pecasContadas = [];

	private List<PecaNoChao> PecasDaZona(ulong zona)
	{
		if (!_pecasNoChao.TryGetValue(zona, out List<PecaNoChao>? lista))
			_pecasNoChao[zona] = lista = [];
		return lista;
	}

	/// <summary>
	/// ============================ `SpawnLop()` + o `New()` da peca -- `mobparts_logic.dm:144-146`, `mobparts.dm:395-405` ============================
	/// Instalado como `CombatState.AoDecepar` no `PrepararCombate`, entao roda pra TODO membro que sai
	/// de TODO corpo (jogador, NPC, cadaver) por QUALQUER funil -- e a unica casa de "uma peca nasceu".
	///
	/// ============================ POR QUE A PECA NAO VEM COM O `S2C.Hit` ============================
	/// O `S2C.Hit` ja diz que houve amputacao (o bit `Decepou`, que chega ate pra quem so assiste) e
	/// e por ele que o jato de sangue dispara no cliente -- zero byte novo. O que ele NAO diz pra
	/// plateia e QUAL membro: o campo `Membro` so e escrito quando `TemDano`, e quem assiste recebe
	/// o pacote magro justamente pra nao ler ficha alheia. Alargar aquele pacote resolveria a peca e
	/// de quebra vazaria o membro atingido em TODO soco -- caro por um evento raro. E metade das
	/// amputacoes nem tem `S2C.Hit`: a explosao e o dano direto nao anunciam golpe nenhum.
	///
	/// Entao a peca vai pelo `S2C.Decalque`, confiavel e pra zona inteira -- e pelo retrato
	/// (`S2C.Pecas`) pra quem chegar depois. Dois canais, uma lista: os dois leem daqui.
	///
	/// A ORDEM NO FIO E A DO DM: o `LopLimb` avisa, joga a peca no chao (`SpawnLop`, `:110`) e SO
	/// ENTAO solta o jato (`bloodspray`, `:119`). O `S2C.Hit` do soco sai depois do resolvedor, e este
	/// gancho dispara de dentro dele -- a peca chega ANTES do relato que acende o jato, como la.
	/// ================================================================================================
	/// </summary>
	private void SoltarPecas(ServerPlayer d, List<BodyPart> caiu)
	{
		long agora = NowMs();
		ulong zona = d.Zone.Hash;
		List<PecaNoChao> lista = PecasDaZona(zona);

		foreach (BodyPart membro in caiu)
		{
			PecaDeCorpo peca = Body.PecaDe(membro.Nome);

			// ESPALHA EM VOLTA, e o sorteio e do SERVIDOR e e SEMEADO. O original faz
			// `pixel_x/pixel_y += rand(-32,32)` no `New()` de cada peca (`mobparts.dm:404-405`) -- um
			// tile em pixels, que e o mesmo `TileSize` daqui. Sorteado no cliente, cada tela veria o
			// braco num lugar; sorteado com o `_rng` do servidor, duas rodadas da mesma bancada
			// dariam lugares diferentes. Ver `PecasNoChao.Espalhar`.
			int ordinal = _pecasContadas.GetValueOrDefault(zona);
			_pecasContadas[zona] = ordinal + 1;
			Vec2 onde = d.Pos + PecasNoChao.Espalhar(zona, d.Pos, peca, ordinal);

			// O TETO DA ZONA: a 33a empurra a mais velha -- a que o DM apagaria primeiro, por ordem de
			// `spawn`. `RemoveAt(0)` porque a lista e ordenada por queda.
			while (lista.Count >= PecasNoChao.TetoPorZona) lista.RemoveAt(0);
			lista.Add(new PecaNoChao { Peca = peca, Onde = onde, CaiuEm = agora });

			// A DIRECAO NAO DIZ NADA AQUI: a folha `Body Parts Bloody` tem um recorte por peca e
			// nenhum sufixo de direcao, e o `Plantar` recebe a animacao por nome. Vai `South` pelo
			// mesmo motivo que o chao danificado vai -- e o campo do pacote, nao uma escolha.
			MandarDecalque(d.Zone, Protocol.Decal.Membro, onde, Facing.South, peca);
		}

		// O RABO NO CHAO MUDA O RITMO DE TREINO NA HORA, por qualquer funil. Antes isto so acontecia
		// no `ResolverDesfecho` do soco (`r.RaboArrancado`); um rabo zerado por explosao ficava
		// caido no chao com o Saiyajin ainda treinando na metade do ritmo, ate o proximo login.
		if (caiu.Exists(p => p.Nome == "Rabo")) AjustarGanhoDoRabo(d);
	}

	/// <summary>
	/// O `spawn(6000) src.loc = null` de cada peca (`mobparts.dm:397`), a 5 Hz -- junto do tique dos
	/// cadaveres, e pelo mesmo motivo: uma peca sumindo 200 ms depois do prazo e invisivel.
	///
	/// A LISTA E ORDENADA POR QUEDA E TODAS VIVEM O MESMO PRAZO, entao as vencidas sao sempre as
	/// primeiras. `RemoveAll` mesmo assim, por ser a pergunta e nao a suposicao.
	/// </summary>
	private void TickDasPecas()
	{
		long agora = NowMs();
		foreach (List<PecaNoChao> lista in _pecasNoChao.Values)
			lista.RemoveAll(p => PecasNoChao.Venceu(p, agora));
	}

	/// <summary>
	/// ============================ O RETRATO DAS PECAS PRA QUEM CHEGA NA ZONA -- `S2C.Pecas` ============================
	/// Sai no login e na troca de zona, ao lado do `MandarCenario` e do `MandarObras`, pelo mesmo
	/// argumento escrito neles: a peca e cenario, e cenario que so quem estava presente viu nao e
	/// cenario. Vai a LISTA INTEIRA da zona (no maximo 32 entradas de 13 bytes) e nao um delta -- o
	/// cliente acabou de limpar o chao da zona anterior e nao tem nada pra somar.
	///
	/// LEVA QUANTO FALTA DE CADA UMA: quem chega no minuto 9 de uma peca a ve sumir no minuto 10, e
	/// nao dez minutos depois de chegar. Sem isso o retrato faria a mesma peca durar tempos diferentes
	/// em telas diferentes.
	///
	/// A ZONA VIAJA JUNTO pelo mesmo motivo do `PacoteDeCenario` com `limpar`: o cliente pode receber
	/// o retrato antes de ter terminado de carregar a zona (a troca de mapa e diferida por uma tela de
	/// carregamento), e ele precisa saber de QUE chao e o retrato pra planta-lo no certo.
	///
	/// Vazio tambem sai: e ele que diz "esta zona nao tem peca nenhuma", e o cliente que guardou o
	/// retrato da zona anterior precisa ouvir isso.
	/// ==============================================================================================================
	/// </summary>
	private void MandarPecas(ServerPlayer pl)
	{
		NetDataWriter w = PacoteDePecas(pl.Zone, NowMs());

		// A BANCADA LE O FIO, pela mesma razao da `EscutaDeDecalques`: o retrato termina num
		// `Peer.Send`, e um corpo forjado nao tem `Peer`. Nula em jogo.
		EscutaDeRetratosDePecas?.Add((pl.Id, w.CopyData()));

		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	private NetDataWriter PacoteDePecas(ZoneKey zona, long agora)
	{
		List<PecaNoChao> lista = PecasDaZona(zona.Hash);
		var w = Protocol.Begin(Protocol.S2C.Pecas);
		w.Put(zona.Hash);
		w.Put((byte)Math.Min(lista.Count, PecasNoChao.TetoPorZona));
		foreach (PecaNoChao p in lista)
		{
			w.Put((byte)p.Peca);
			w.PutVec(p.Onde);
			w.Put((int)PecasNoChao.RestanteMs(p, agora));
		}
		return w;
	}
}
