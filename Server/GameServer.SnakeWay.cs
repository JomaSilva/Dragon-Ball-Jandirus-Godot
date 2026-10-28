using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ MORRER DE PROPOSITO E A SNAKE WAY (2026-10-27, o pedido 124d) ============================
/// Do documento de um jogador que o dono mandou: *"Add an easy way to kill your own character, and in otherworld have a dialog option for
/// king yemma to let you run snake way"*. As escolhas do dono: "Botao com confirmacao" e "Corrida + exigencia".
///
///   * MORRER (`morrer`, o verbo do menu P > Other, depois da confirmacao da `TelaDeMorrer`): a morte COMUM, pelo `Combate.Morrer()` de
///     producao -- o funil de toda morte (o cadaver, a viagem pro Outro Mundo). Sem algoz: nao ha Zenkai (que e do `AoPerderALuta`) nem
///     karma. O seguro de morte (`NegarMorte`: a Aura of Destruction, as raciais) vale como pra qualquer morte. Dentro da mente nao: la a
///     morte nao e morte (`AMorteAconteceu`).
///   * A SNAKE WAY: o Enma ("Correr a Snake Way") deixa correr quem esta morto, no Outro Mundo, com `SnakeWay.BpMinimo` de BP e a porta da
///     alma aberta (`APortaDoOutroMundoAbrePara`). A corrida e A PE: voar a cancela, a cada tique. Chegar ao FIM DA ESTRADA (a cabeca da
///     serpente, `SnakeWay.NaChegada`; ate 2026-10-27 era o alcance do Sr. Kaioh, que a pe ninguem alcancava -- o pedido 142) a fecha --
///     `SnakeWay.BonusDeBp` de BP uma vez na vida, e a marca `Fighter.SnakeWayFeita`, que o Sr. Kaioh pede pra ensinar (`SrKaiohFalar`);
///     da cabeca o corredor VOA ate a ilha dele.
/// =========================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// Quem esta correndo a Snake Way: id do corpo -> assinatura (o id se recicla; a assinatura confere). So memoria: quem sai do jogo no
	/// meio larga a corrida.
	/// </summary>
	private readonly Dictionary<int, string> _correndoASnakeWay = [];

	/// <summary>A bancada (`--snakewayteste`) le se este corpo esta correndo.</summary>
	internal bool CorrendoASnakeWayDeTeste(int id) => _correndoASnakeWay.ContainsKey(id);

	/// <summary>O robo do botao Morrer (`--diagmorrer`) le se o corpo morreu.</summary>
	internal bool MortoDeTeste(int id) => _players.TryGetValue(id, out ServerPlayer? p) && p.Ficha.dead;

	private void MorrerDeProposito(ServerPlayer pl)
	{
		if (pl.Ficha.dead) { Avisar(pl, "você já está morto."); return; }
		if (NaMente(pl)) { Avisar(pl, "dentro da mente não se morre -- acorde primeiro."); return; }
		if (!pl.Combate.Morrer())
		{
			MandarFicha(pl);
			Avisar(pl, "a sua morte foi negada: alguma coisa segura a sua vida agora.");
			return;
		}
		MandarFicha(pl);
		Avisar(pl, "você deixa a vida ir.");
		GD.Print($"[server] {pl.Name} morreu de proposito (o verbo Morrer)");
	}

	private void EnmaSnakeWay(ServerPlayer pl)
	{
		if (ObraQueAceita(pl, SnakeWay.Verbo) == null) { Avisar(pl, "o Enma não está ao alcance -- fale com ele na mesa dele."); return; }
		if (!pl.Ficha.dead || !Alem.EhOAlem(pl.Zone)) { Avisar(pl, "a Snake Way é caminho de morto -- e começa na mesa do Enma."); return; }
		if (pl.Ficha.SnakeWayFeita) { Avisar(pl, "Enma Daioh: \"Você já correu a Snake Way! O Sr. Kaioh te conhece.\""); return; }
		if (pl.Ficha.BP < SnakeWay.BpMinimo)
		{
			Avisar(pl, $"Enma Daioh: \"Fraco demais pra Snake Way! Volte com {SnakeWay.BpMinimo:N0} de BP (você tem {pl.Ficha.BP:N0}).\"");
			return;
		}
		if (!APortaDoOutroMundoAbrePara(pl, NowMs())) return;   // a barreira da alma ja disse por que
		_correndoASnakeWay[pl.Id] = pl.Assinatura;
		Avisar(pl, "Enma Daioh aponta o Caminho da Serpente: \"Corra até o fim dele, a cabeça da serpente -- A PÉ! Quem voa não chegou.\" "
				   + "(voar antes do fim cancela a corrida; de lá, voe até o planeta do Sr. Kaioh)");
	}

	/// <summary>A CORRIDA, a cada tique (o voo de um instante tambem cancela): a saida do Outro Mundo, o voo e a chegada ao Sr. Kaioh.</summary>
	private void TickDaSnakeWay()
	{
		if (_correndoASnakeWay.Count == 0) return;
		foreach ((int id, string sig) in _correndoASnakeWay.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Assinatura != sig) { _correndoASnakeWay.Remove(id); continue; }
			if (!pl.Ficha.dead || !Alem.EhOAlem(pl.Zone))
			{
				_correndoASnakeWay.Remove(id);
				Avisar(pl, "você saiu do Outro Mundo: a corrida da Snake Way fica pra trás.");
				continue;
			}
			if (pl.Voando)
			{
				_correndoASnakeWay.Remove(id);
				Avisar(pl, "você VOOU: a corrida da Snake Way foi cancelada -- volte ao Enma e corra de novo, a pé.");
				continue;
			}
			// (2026-10-27, o pedido 142) a chegada e o FIM DA ESTRADA, e nao o Sr. Kaioh: a ilha dele fica a 11 tiles de nuvem da cabeca da
			// serpente, e a pe ninguem chegava -- ver `SnakeWay.NaChegada`
			if (pl.Zone.Name != Alem.ZonaDoOutroMundo || !SnakeWay.NaChegada(pl.Pos)) continue;
			_correndoASnakeWay.Remove(id);
			pl.Ficha.SnakeWayFeita = true;
			// O BP FIXO, e nao pelo `CapCheck`: ele multiplica pelo folego (`StamBPGainMod`) e mexe nos relogios do treino -- o pedido e
			// "um BP fixo pequeno", e o numero e o `SnakeWay.BonusDeBp`.
			double ganho = SnakeWay.BonusDeBp;
			pl.Ficha.BP += ganho;
			MandarFicha(pl);
			Avisar(pl, $"você correu a Snake Way inteira, a pé! (+{ganho:N0} de BP) O planeta do Sr. Kaioh fica logo ao norte: voe até ele, que ele te ensina.");
			GD.Print($"[server] {pl.Name} correu a Snake Way (+{ganho:0} de BP)");
		}
	}
}
