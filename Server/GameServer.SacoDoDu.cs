using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ O SOCO NO SACO DE PANCADA, COMO NO DU (dono, 2026-10-27, o pedido 111) ============================
/// O pedido dos jogadores: um jeito de *"replicate the alt farming but solo"* -- e a resposta do dono: *"veja como era o ganho do saco no
/// DU e faça ele aqui"*. No DU o saco (`obj/Peebag`, `Train.dm:273`, *"Punching this only gives BP nothing else"*) rende pelo SOCO
/// (`mob/proc/Peebag`, `death.dm:1097-1115`):
///   * o saco no tile da frente de quem soca;
///   * um soco a cada 15 decimos (`peebagDelay`) -- 1,5 s, mais lento que o soco de verdade;
///   * 15 de Ki por soco (`peebagDrain = 10 * (15/10)`), e sem o Ki o soco nao rende;
///   * o ganho e o de LUTA (`Attack_Gain(N = bp_mod, skip_leech = 1, partyGains = 0)`), sem sanguessuga e sem grupo -- e LINEAR no
///     `bp_mod` (o `GetBaseBPGain` do DU nao o tem; o `N` o poe uma vez). No port o `BPMod` JA ENTRA no ganho base (`BpGainBase`), entao
///     o soco paga o `AttackGain` de um golpe e a conta sai a mesma (passar o `BPMod` de novo daria o quadrado dele);
///   * o MAJIN nao usa (`if(Race=="Majin") return`).
/// DIVERGENCIA DECLARADA: no DU o saco so rendia socado do lado oeste (o `Pee.dir=WEST` do proc); aqui qualquer lado serve. O saco do
/// Jandirus continua dobrando o TREINO de quem treina perto dele (`BonusDeTreinoPerto`) -- sao duas coisas: treinar ao lado e socar.
/// O saco na frente SEGURA o soco (o golpe nao vira soco em cenario, que quebraria o saco), rendendo ou nao.
/// ================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Um soco no saco a cada tanto (o `peebagDelay` de 15 decimos do DU).</summary>
	internal const long MsEntreSocosNoSaco = 1500;

	/// <summary>O Ki de cada soco (o `peebagDrain` do DU).</summary>
	internal const double KiDoSocoNoSaco = 15;

	/// <summary>O saco de pancada no tile da frente deste corpo, ou nulo.</summary>
	private Obra? SacoNaFrente(ServerPlayer a)
	{
		Vec2 frente = MeleeArea.Frente(a.Facing);
		double px = a.Pos.X + frente.X * ZoneCollision.TileSize, py = a.Pos.Y + frente.Y * ZoneCollision.TileSize;
		double folga = ZoneCollision.TileSize * 0.75;
		foreach (Obra o in _noChao)
			if (o.Tipo == "Punching_Bag" && o.Zona.Equals(a.Zone) && Math.Abs(o.X - px) <= folga && Math.Abs(o.Y - py) <= folga) return o;
		return null;
	}

	/// <summary>
	/// O SOCO QUE NAO ACHOU CORPO E O SACO NA FRENTE: rende o ganho de luta do DU (uma vez a cada 1,5 s, por 15 de Ki). Verdadeiro quando ha
	/// um saco na frente -- o soco e dele, rendendo ou nao.
	/// </summary>
	private bool SocarOSaco(ServerPlayer a)
	{
		if (SacoNaFrente(a) is not { } saco) return false;
		if (a.Race == "Majin") return true;   // o `if(Race=="Majin") return` do DU: o saco nao rende pro Majin
		// (2026-10-27, o pedido 136) o saco que ja estava numa Sala do Tempo segura o soco e nao paga -- ver `LugarDeObra.AumentamOGanho`
		if (LugarDeObra.NaoRendeAqui(saco.Zona, saco.Tipo)) return true;
		long agora = NowMs();
		if (agora - a.UltimoSocoNoSaco < MsEntreSocosNoSaco) return true;
		if (a.Ficha.Ki < KiDoSocoNoSaco) return true;
		a.Ficha.Ki -= KiDoSocoNoSaco;
		a.UltimoSocoNoSaco = agora;
		a.Ficha.AttackGain(_rng);   // o BPMod ja esta no ganho base -- ver o cabecalho
		return true;
	}
}
