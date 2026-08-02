using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// COMBATE, LADO DO SERVIDOR.
///
/// Aqui e a excecao declarada ao "cliente calcula, servidor valida": o golpe e resolvido
/// SO AQUI. A razao e que a resolucao sorteia (pontaria, membro atingido, critico) e duas
/// pontas sorteando nunca chegam ao mesmo resultado. O cliente pede "socar" e recebe o
/// relato pronto -- ele nem sabe o BP de quem apanhou.
///
/// A CADENCIA E A TRAVA ANTI-CHEAT. Mandar o pacote de soco mil vezes por segundo nao adianta:
/// o servidor so aceita quando a recarga do proprio lutador zerou, e a recarga sai do
/// `Eactspeed` dele. O cliente recebe o mesmo numero na ficha (`SheetState.SocoMs`) pra nao
/// tentar o que vai ser recusado.
/// </summary>
public partial class GameServer
{
	/// <summary>Quanto tempo o corpo fica no chao antes de renascer, em milissegundos.</summary>
	private const long MsAteRenascer = 15_000;

	/// <summary>Segundos de carencia de quem acabou de renascer (ver CombatState.Carencia).</summary>
	private const double SegundosDeCarencia = 6;

	/// <summary>
	/// Racas que REGENERAM membro perdido. Nelas, perder um nucleo poe em coma em vez de
	/// matar -- e o `canheallopped` do original.
	/// </summary>
	private static bool Regenera(string raca) =>
		raca is "Namekian" or "Majin" or "BioAndroid" or "Shapeshifter";

	/// <summary>Quem nasce com rabo (e portanto tem um membro a mais pra perder).</summary>
	private static bool TemRabo(string raca) => raca is "Saiyan" or "Halfbreed";

	/// <summary>Monta o corpo de quem acabou de entrar, e devolve a vida ao estado salvo.</summary>
	private static void PrepararCombate(ServerPlayer pl, CharacterSave? save)
	{
		pl.Combate = new CombatState(pl.Ficha, TemRabo(pl.Race), Regenera(pl.Race));

		// A vida dos membros PERSISTE entre sessoes: deslogar com o braco quebrado nao cura.
		// (Deslogar com o corpo DECEPADO tambem nao -- isso e coisa de regeneracao ou de morte.)
		if (save?.Membros is { Count: > 0 })
			foreach (BodyPart p in pl.Combate.Corpo.Partes)
				if (save.Membros.TryGetValue(p.Nome, out double[]? v) && v.Length >= 2)
				{
					p.Vida = Math.Clamp(v[0], 0, p.VidaMax);
					p.Decepado = v[1] > 0.5;
				}

		pl.Combate.SincronizarVida();
		if (pl.Ficha.dead) { pl.RenasceEm = NowMs() + MsAteRenascer; return; }

		// NOCAUTE NAO ATRAVESSA O LOGOUT DE GRACA. O `KO` mora na ficha (que e salva) mas o
		// cronometro que faz levantar mora no estado de combate (que nao e) -- entrar com
		// KO=true e cronometro zerado deixaria o personagem no chao PARA SEMPRE. Quem volta
		// caido recomeca a contagem; quem volta com um nucleo abaixo do limiar cai de novo.
		if (pl.Ficha.KO || pl.Combate.Corpo.DeveNocautear())
			pl.Combate.Nocautear(MeleeResolver.SegundosDeNocaute);
	}

	/// <summary>Fotografa o corpo pro savefile: vida e "esta decepado" por membro.</summary>
	public static Dictionary<string, double[]> FotografarCorpo(CombatState c)
	{
		var d = new Dictionary<string, double[]>(c.Corpo.Partes.Count);
		foreach (BodyPart p in c.Corpo.Partes) d[p.Nome] = [p.Vida, p.Decepado ? 1 : 0];
		return d;
	}

	// =====================================================================
	// O GOLPE
	// =====================================================================
	private void Atacar(ServerPlayer a, Protocol.Golpe golpe)
	{
		CombatState ca = a.Combate;
		if (!ca.PodeAtacar()) return;   // morto, caido, atordoado ou ainda em recarga

		double tipo = Protocol.PesoDoGolpe(golpe);
		double espera = CombatMath.Cadencia(a.Ficha, tipo);
		ca.Recarga = espera;
		a.AtaqueAte = NowMs() + (long)(espera * 1000);

		// bater com a guarda erguida nao existe: o braco que soca e o que estava aparando
		ca.Guardar(false);

		ServerPlayer? alvo = AlvoNaFrente(a);
		if (alvo == null)
		{
			// SOCAR O AR AINDA TREINA. E o que o BYOND fazia e o que faz o novato progredir
			// sozinho num canto do mapa -- so que sem o multiplicador de lutar contra alguem.
			//
			// E nao se anuncia nada: a pose de soco ja vai no snapshot, e mandar um pacote
			// confiavel pra zona inteira tres vezes por segundo por pessoa treinando seria
			// gastar banda pra contar que ninguem foi atingido.
			a.Ficha.AttackGain(_rng);
			return;
		}

		CombatState cd = alvo.Combate;
		double angulo = MeleeArea.AnguloDeChegada(alvo.Pos, alvo.Facing, a.Pos);

		// O NIVEL DO BAQUE sai daqui, ANTES de resolver: `min(3, tipo + min(1, combo))` do
		// original. Um soco leve isolado soa pequeno; o mesmo soco no meio de uma sequencia
		// soa medio; o pesado soa grande. E o que faz a briga esquentar no ouvido.
		int nivel = (int)Math.Min(3, tipo + Math.Min(1, ca.Combo));

		GolpeResultado r = MeleeResolver.Resolver(ca, cd, angulo, _rng, tipo);
		alvo.UltimoAgressor = a.Id;

		if (r.Encostou) ca.SomarCombo();
		else ca.ZerarCombo();          // errou, foi aparado de longe ou tomou contra: recomeca

		// LUTAR ENSINA OS DOIS. Quem bate ganha pela troca, quem apanha ganha pelo gap de
		// poder -- encarar alguem mais forte e o ganho mais rapido do jogo.
		a.Ficha.AttackGain(_rng, a.Ficha.FightGainMult(alvo.Ficha));
		if (r.Encostou) alvo.Ficha.AttackGain(_rng, alvo.Ficha.FightGainMult(a.Ficha));

		// O contra-ataque devolve o golpe: quem bloqueou na hora certa acerta de volta.
		if (r.Desfecho == Desfecho.Contra)
		{
			GolpeResultado devolta = MeleeResolver.Resolver(
				cd, ca, MeleeArea.AnguloDeChegada(a.Pos, a.Facing, alvo.Pos), _rng, tipo);
			a.UltimoAgressor = alvo.Id;
			ResolverDesfecho(alvo, a, devolta);
			AnunciarGolpe(alvo, a, devolta, 2);
		}

		ResolverDesfecho(a, alvo, r);
		AnunciarGolpe(a, alvo, r, nivel);
	}

	/// <summary>As consequencias fora do corpo: nocaute, morte, Zenkai.</summary>
	private void ResolverDesfecho(ServerPlayer a, ServerPlayer d, GolpeResultado r)
	{
		if (r.RaboArrancado)
			GD.Print($"[server] {a.Name} ARRANCOU O RABO de {d.Name}");

		if (r.Nocauteou)
			GD.Print($"[server] {a.Name} NOCAUTEOU {d.Name} ({r.Membro})");

		if (!r.Morreu) return;

		d.RenasceEm = NowMs() + MsAteRenascer;
		GD.Print($"[server] {a.Name} MATOU {d.Name} ({r.Membro})");

		// ZENKAI: perder pra alguem mais forte arranca poder do corpo. E pago na hora, direto
		// no BP base -- e recompensa, nao treino, entao nao passa pelo CapCheck.
		Fighter.ZenkaiResult z = d.Ficha.GainZenkai(
			Math.Max(a.Ficha.expressedBP, a.Ficha.BP), NowMs(),
			d.Combate.Corpo.MuitoFerido());

		if (z == Fighter.ZenkaiResult.Concedido)
			GD.Print($"[server] Zenkai de {d.Name}: +{d.Ficha.UltimoZenkai:0} de BP" +
					 (d.Ficha.UltimoZenkaiNoTeto ? " (no teto)" : ""));
	}

	/// <summary>
	/// Quem esta no cone do golpe. O MAIS PROXIMO leva -- socar nao acerta dois de uma vez.
	/// Morto e ignorado: o corpo esta no chao, nao no caminho.
	/// </summary>
	private ServerPlayer? AlvoNaFrente(ServerPlayer a)
	{
		ServerPlayer? melhor = null;
		float melhorDist = float.MaxValue;

		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			if (o == a || o.Ficha.dead || o.Combate.Intocavel) continue;
			if (!MeleeArea.NoAlcance(a.Pos, a.Facing, o.Pos)) continue;

			float dist = (o.Pos - a.Pos).LengthSquared;
			if (dist >= melhorDist) continue;
			melhorDist = dist;
			melhor = o;
		}
		return melhor;
	}

	/// <summary>
	/// Conta o golpe pra zona. Os DOIS ENVOLVIDOS recebem o dano; quem so assistiu recebe o
	/// evento sem numero -- ve o impacto e ouve o som, mas nao le a ficha alheia.
	/// </summary>
	private void AnunciarGolpe(ServerPlayer a, ServerPlayer d, GolpeResultado r, int nivel)
	{
		var cheio = new Protocol.HitEvent
		{
			Atacante = a.Id, Alvo = d.Id, Desfecho = (byte)r.Desfecho,
			Nivel = (byte)Math.Clamp(nivel, 1, 3),
			TemDano = true, Dano = (float)r.Dano, Membro = r.Membro,
			Quebrou = r.Quebrou, Decepou = r.Decepou, Nocauteou = r.Nocauteou, Morreu = r.Morreu,
			Rabo = r.RaboArrancado,
		};
		Protocol.HitEvent magro = cheio;
		magro.TemDano = false;

		var wCheio = Protocol.Begin(Protocol.S2C.Hit); cheio.Write(wCheio);
		var wMagro = Protocol.Begin(Protocol.S2C.Hit); magro.Write(wMagro);

		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			// Pros DOIS envolvidos o relato e confiavel: perder o pacote que diz "voce perdeu
			// o braco" nao e opcao. Pra quem so assiste vai sem garantia -- e uma piscada e um
			// som, e reenviar isso pra uma zona cheia de gente e o tipo de trafego que derruba
			// servidor.
			if (o == a || o == d)
				o.Peer.Send(wCheio, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			else
				o.Peer.Send(wMagro, Protocol.ChannelState, DeliveryMethod.Unreliable);
		}
	}

	// =====================================================================
	// PASSAGEM DE TEMPO
	// =====================================================================
	/// <summary>
	/// Roda a cada tick do servidor (30 Hz): cronometros de recarga, atordoamento, guarda,
	/// saida do nocaute e o renascimento de quem morreu.
	/// </summary>
	private void TickCombate(double dt)
	{
		long agora = NowMs();
		foreach (ServerPlayer pl in _players.Values)
		{
			CombatState c = pl.Combate;
			if (c == null) continue;

			bool eraKO = pl.Ficha.KO;
			c.Tick(dt);
			if (eraKO && !pl.Ficha.KO)
			{
				c.SincronizarVida();
				GD.Print($"[server] {pl.Name} levantou");
			}

			// REGENERACAO PASSIVA: so fora de combate, e so pra quem nao esta morto. Enquanto
			// a tag de luta esta no ar o corpo nao se recupera -- senao ninguem perde nunca.
			if (!pl.Ficha.dead && !pl.Ficha.KO && c.EmCombate <= 0 && pl.Ficha.HP < 99.99)
			{
				c.Corpo.Curar(RegenPorSegundo * dt);
				c.SincronizarVida();
			}

			if (pl.Ficha.dead && agora >= pl.RenasceEm) Renascer(pl);
		}
	}

	/// <summary>
	/// Vida por segundo que o corpo recupera fora de combate. Um corpo inteiro leva ~1 minuto
	/// pra sair de zero -- lento o bastante pra derrota doer, rapido o bastante pra nao
	/// obrigar ninguem a ficar sentado esperando.
	/// </summary>
	private const double RegenPorSegundo = 100.0 / 60.0;

	/// <summary>
	/// Morrer devolve o personagem inteiro no ponto de spawn, com metade da vida.
	///
	/// O JULGAMENTO DO ENMA (karma, o alem, o revive por Zeni) e outra etapa -- ate la, morrer
	/// custa a luta, o tempo no chao e mais nada. Fica marcado aqui pra nao virar "esqueci".
	/// </summary>
	private void Renascer(ServerPlayer pl)
	{
		pl.Combate.Reviver(0.5, SegundosDeCarencia);
		pl.Ficha.Ki = pl.Ficha.MaxKi * 0.5;
		pl.RenasceEm = 0;

		if (pl.Zone.Hash != SpawnZone.Hash) MoveToZone(pl.Id, SpawnZone, SpawnPos);
		else
		{
			pl.Pos = SpawnPos;
			var w = Protocol.Begin(Protocol.S2C.Correction);
			w.PutVec(pl.Pos);
			pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}

		GD.Print($"[server] {pl.Name} renasceu ({SegundosDeCarencia:0}s de carencia)");
	}
}
