using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A DIMENSAO MENTAL: meditar abre a porta pra lutar contra si mesmo.
///
/// E o primeiro NPC do port, e ele existe por uma razao de desenho: treinar sozinho socando o ar
/// funciona, mas nao ENSINA -- ninguem aprende a apara, a mira e a distancia sem alguem do outro
/// lado. O clone e o oponente que sempre esta disponivel, e como ele e uma copia exata, a luta
/// mede exatamente a sua habilidade e nao a diferenca de poder.
///
/// UM BOLSO POR PESSOA. A zona e <c>ZoneKey.Interior("Interdimension", id)</c>: o NOME resolve a
/// cena no catalogo (o mapa Interdimension ja existe), e o HASH -- que inclui o id -- e o corte
/// de interesse. Dois jogadores meditando ao mesmo tempo estao no mesmo cenario e em mundos
/// diferentes, sem nenhum codigo de instanciamento.
///
/// O CLONE JOGA PELAS MESMAS REGRAS. Ele nao teleporta, nao atravessa parede e nao bate mais
/// rapido que a cadencia: o cerebro (<see cref="Cerebro"/>) so decide o que APERTAR, e quem move
/// e quem resolve o golpe sao as mesmas funcoes do jogador. NPC que anda por fora das regras e a
/// origem do bug que ninguem reproduz.
/// </summary>
public partial class GameServer
{
	/// <summary>Onde o corpo nasce dentro da dimensao mental (centro do mapa Interdimension).</summary>
	private static readonly Vec2 PosMental = new(250 * 32 + 16, 250 * 32 + 16);

	/// <summary>A que distancia o clone aparece. Perto o bastante pra a luta comecar sozinha.</summary>
	private const float DistanciaDoClone = 96f;

	/// <summary>
	/// ENTRA NA MENTE. So meditando -- e a condicao do original, e ela faz sentido: e um lugar
	/// pra onde se vai por dentro, nao um mapa que se atravessa.
	/// </summary>
	private void EntrarNaMente(ServerPlayer pl)
	{
		if (pl.Peer == null) return;                       // clone nao medita
		if (pl.CloneId != 0) { Avisar(pl, "voce ja esta na sua mente."); return; }
		if (!pl.Ficha.med) { Avisar(pl, "e preciso estar MEDITANDO (tecla M) pra entrar na propria mente."); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "agora nao."); return; }

		pl.ZonaDeOrigem = pl.Zone;
		pl.PosDeOrigem = pl.Pos;

		ZoneKey mente = ZoneKey.Interior("Interdimension", (ulong)pl.Id);
		MoveToZone(pl.Id, mente, PosMental);

		ServerPlayer clone = CriarClone(pl, mente);
		pl.CloneId = clone.Id;

		Avisar(pl, "voce fecha os olhos. Diante de voce, VOCE -- e ele conhece cada golpe seu.");
		GD.Print($"[server] {pl.Name} entrou na mente (clone id {clone.Id}, BP {clone.Ficha.BP:N0})");
	}

	/// <summary>Sai da mente e desfaz o clone. Chamado pelo jogador, pela morte dele ou pelo logout.</summary>
	private void SairDaMente(ServerPlayer pl, string motivo)
	{
		if (pl.CloneId == 0) return;

		if (_players.TryGetValue(pl.CloneId, out ServerPlayer? clone)) RemoverClone(clone);
		pl.CloneId = 0;

		// volta pra onde estava. Se a origem se perdeu (save antigo, servidor reiniciado no
		// meio), cai na Terra em vez de ficar preso num bolso sem saida.
		ZoneKey destino = pl.ZonaDeOrigem.Name.Length > 0 ? pl.ZonaDeOrigem : SpawnZone;
		Vec2 pos = pl.ZonaDeOrigem.Name.Length > 0 ? pl.PosDeOrigem : SpawnPos;
		MoveToZone(pl.Id, destino, pos);
		Avisar(pl, motivo);
	}

	/// <summary>
	/// O CLONE. Copia a ficha inteira e o corpo -- ele E voce, com a mesma velocidade, a mesma
	/// cadencia e o mesmo poder.
	///
	/// A ficha e copiada por VALOR (uma instancia nova), nao compartilhada: se os dois
	/// apontassem pro mesmo <see cref="Fighter"/>, bater no clone tiraria a SUA vida.
	/// </summary>
	private ServerPlayer CriarClone(ServerPlayer dono, ZoneKey zona)
	{
		var clone = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,                       // e o que faz dele um NPC
			Name = dono.Name + " (mente)",
			Zone = zona,
			Pos = dono.Pos + new Vec2(DistanciaDoClone, 0),
			Race = dono.Race,
			Class = dono.Class,
			SpeedStat = dono.SpeedStat,
			Visual = dono.Visual,
			Genero = dono.Genero,
			Planeta = dono.Planeta,
			Idade = dono.Idade,
			LastInputMs = NowMs(),
			Cerebro = new Cerebro(),
			DonoDoClone = dono.Id,
			Ficha = ClonarFicha(dono.Ficha),
		};

		PrepararCombate(clone, null);
		clone.Livro = new Jandirus.Core.Skills.SkillBook();
		clone.Combate.Letal = false;   // a mente nao decepa membro nem mata: e treino

		_players[clone.Id] = clone;
		ZoneList(zona.Hash).Add(clone);

		// a APARENCIA precisa ir pro dono, senao ele ve um boneco sem roupa nem cabelo
		MandarLook(dono, clone);
		return clone;
	}

	/// <summary>
	/// Copia de ficha por serializacao dos campos que importam.
	///
	/// Nao ha `Clone()` no <see cref="Fighter"/> e escrever um a mao seria a mesma lista de
	/// campos que ja deu errado no save -- por isso o save serializa o objeto inteiro. Aqui o
	/// que interessa e o PODER e a energia; buff temporario e estado de luta o clone comeca do
	/// zero, que e o certo: ele e voce em condicoes de treino.
	/// </summary>
	private Fighter ClonarFicha(Fighter f)
	{
		var c = new Fighter
		{
			BP = f.BP,
			BPMod = f.BPMod,
			HP = 100,
			MaxKi = f.MaxKi,
			Ki = f.MaxKi,
			stamina = f.maxstamina,
			maxstamina = f.maxstamina,
			physoff = f.physoff, physdef = f.physdef, technique = f.technique,
			kioff = f.kioff, kidef = f.kidef, kiskill = f.kiskill,
			speed = f.speed, magiskill = f.magiskill,
			physoffMod = f.physoffMod, physdefMod = f.physdefMod, techniqueMod = f.techniqueMod,
			kioffMod = f.kioffMod, kidefMod = f.kidefMod, kiskillMod = f.kiskillMod,
			speedMod = f.speedMod, magiMod = f.magiMod,
		};
		c.Statify();
		return c;
	}

	private void RemoverClone(ServerPlayer clone)
	{
		ZoneList(clone.Zone.Hash).Remove(clone);
		_players.Remove(clone.Id);

		// avisa quem estava vendo -- sem isto o boneco fica congelado na tela do dono
		var w = Protocol.Begin(Protocol.S2C.PeerLeft);
		w.Put(clone.Id);
		if (_players.TryGetValue(clone.DonoDoClone, out ServerPlayer? dono))
			dono.Peer?.Send(w, Protocol.ChannelReliable, LiteNetLib.DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O TICK DOS NPCS: pensa e executa. Roda no tick CHEIO, junto com o combate, porque a
	/// decisao mexe em movimento e o movimento e por dt.
	/// </summary>
	private void TickDosClones(double dt)
	{
		// lista a parte: `Atacar` pode matar e mexer nas colecoes durante a volta
		List<ServerPlayer> npcs = _players.Values.Where(p => p.Cerebro != null).ToList();

		foreach (ServerPlayer npc in npcs)
		{
			if (!_players.TryGetValue(npc.DonoDoClone, out ServerPlayer? dono)
				|| dono.Zone.Hash != npc.Zone.Hash)
			{
				RemoverClone(npc);   // o dono sumiu: o clone nao tem por que existir
				continue;
			}

			if (npc.Ficha.dead || npc.Ficha.KO)
			{
				// O CLONE CAIU: fim do treino. Ele nao renasce -- vencer a si mesmo e o objetivo,
				// e deixar o corpo no chao pra socar de novo esvaziaria a coisa.
				SairDaMente(dono, "o seu reflexo se desfaz. Voce abre os olhos.");
				continue;
			}

			Decisao d = npc.Cerebro!.Pensar(
				npc.Pos, dono.Pos,
				npc.Ficha.HP / 100.0,
				npc.Ficha.MaxKi > 0 ? npc.Ficha.Ki / npc.Ficha.MaxKi : 1,
				dono.Ficha.KO || dono.Ficha.dead,
				dt, _rng);

			// MOVIMENTO PELAS MESMAS REGRAS do jogador -- inclusive a parede.
			if (d.Rumo.LengthSquared > 1e-6f)
			{
				ZoneCollision? mapa = _catalogo?.Get(npc.Zone)?.Mapa;
				Vec2 antes = npc.Pos;
				npc.Pos = MoveRules.Advance(npc.Pos, d.Rumo, (float)dt, npc.SpeedStat, mapa, out _);
				npc.Moving = (npc.Pos - antes).LengthSquared > 0.01f;
				npc.Facing = MoveRules.FacingFrom(d.Rumo, npc.Facing);
			}
			else npc.Moving = false;

			npc.Combate.Guardar(d.Guardar);
			if (d.Atacar) Atacar(npc, d.Pesado ? Protocol.Golpe.Pesado : Protocol.Golpe.Leve);
		}
	}

	/// <summary>Manda a aparencia de um corpo pra um jogador (o clone precisa disto pra existir na tela).</summary>
	private static void MandarLook(ServerPlayer para, ServerPlayer quem)
	{
		var w = Protocol.Begin(Protocol.S2C.PeerLook);
		w.Put(quem.Id);
		w.Put(quem.Name);
		w.Put(quem.Race);
		w.Put(quem.Genero);
		w.PutAppearance(quem.Visual);
		para.Peer?.Send(w, Protocol.ChannelReliable, LiteNetLib.DeliveryMethod.ReliableOrdered);
	}
}
