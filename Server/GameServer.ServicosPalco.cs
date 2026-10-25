using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagservicos` (2026-10-19) -- ver `Client/RoboDosServicos.cs`. O robo e o servidor rodam no mesmo processo
/// (`--host`): o palco mata o robo (a alma ja de pe no Outro Mundo, sem a viagem de 2 s) e o leva pra perto de cada doador. O resto e
/// producao: o menu E, os verbos, o tique de 1 Hz e o cartao da aba World.
/// </summary>
public partial class GameServer
{
	/// <summary>O robo morre (a alma ja viajou) com a ficha de servicos zerada, e vai pro lado do Oni da recepcao. Falso sem jogador.</summary>
	internal bool PalcoSv_Morrer(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.Ficha.dead = true;
		pl.MorteJaViajou = true;
		pl.Ficha.ServicosNoAlem = new FichaDoAlem();
		PalcoSv_AoLado(id, DoadorDoAlem.Recepcao);
		MandarFicha(pl);
		return true;
	}

	/// <summary>Leva o robo pra celula ao sul do doador (a obra e densa), na zona dele.</summary>
	internal void PalcoSv_AoLado(int id, DoadorDoAlem doador)
	{
		(string zona, int cx, int cy) = doador switch
		{
			DoadorDoAlem.Princesa => (Alem.ZonaDoOutroMundo, ServicosDoAlem.PrincesaX, ServicosDoAlem.PrincesaY),
			DoadorDoAlem.SrKaioh => (Alem.ZonaDoOutroMundo, NpcsDeConversa.SrKaiohX, NpcsDeConversa.SrKaiohY),
			DoadorDoAlem.OniDoInferno => (Alem.ZonaDoInferno, ServicosDoAlem.OniDoInfernoX, ServicosDoAlem.OniDoInfernoY),
			_ => (Alem.ZonaDoOutroMundo, ServicosDoAlem.RecepcaoX, ServicosDoAlem.RecepcaoY),
		};
		const float t = ZoneCollision.TileSize;
		MoveToZone(id, ZoneKey.Premade(zona), new Vec2(cx * t + t / 2f, (cy + 1) * t + t / 2f));
		// (2026-10-23) ACLIMATADO AO LUGAR: o morto de pe passou a sentir o chao (dono), e no Inferno e no planeta do Sr. Kaioh (10x) o robo
		// ficaria preso e se machucando. O assunto deste robo sao os servicos, nao a gravidade (essa e da `--gravmetateste`).
		if (_players.TryGetValue(id, out ServerPlayer? pl))
			pl.Ficha.GravMastered = Math.Max(pl.Ficha.GravMastered, Jandirus.Core.Stats.Esmagamento.Gravidade(pl.Ficha));
	}

	/// <summary>Da o servico ao robo pela porta do servidor (sem o menu) -- o robo mede o cartao chegando com a aba World ja aberta.</summary>
	internal void PalcoSv_Pegar(int id, string servico)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || ServicosDoAlem.Get(servico) is not { } s) return;
		ServicosDoAlem.Pegar(FichaDoAlemDe(pl), s, NowMs());
	}

	/// <summary>O robo revive e a ficha de servicos zera.</summary>
	internal void PalcoSv_Reviver(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.dead = false;
		pl.Ficha.ServicosNoAlem = null;
		MandarFicha(pl);
	}
}
