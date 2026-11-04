using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagsaiyajin` (2026-11-01, o pedido 210): a cadeia do zero com o Freeza no Vegeta vencido, o BP do jogador,
/// onde esta cada chefe e a pod dele, o jogador levado ate la, a derrota de um chefe e os dias que passam. Os gestos sob teste passam pelas
/// portas de producao (o tique das sagas, a virada do dia, a morte do chefe, a IA); o palco so arma o lugar e encurta a espera.
/// </summary>
public partial class GameServer
{
	/// <summary>S1: a cadeia do zero com o Freeza no Vegeta VENCIDO (a saga Saiyajin vem depois dele), e o jogador com este BP.</summary>
	internal void PalcoSY_Preparar(int id, double bp)
	{
		foreach (ServerPlayer c in ChefesNoMundo()) RemoverNpc(c);
		_sagas = new EstadoDasSagas { UltimoDiaVisto = DiaDoMundo };
		foreach (EloDaSaga e in _cadeia) _sagas.Elos.Add(new EstadoDoElo { Id = e.Id });
		if (_sagas.Do("freeza_vegeta") is { } fv) { fv.Disparou = true; fv.Fase = FaseDaSaga.Vencida; }
		_sagasPorRetomar = false;
		SalvarSagas();
		PalcoSY_Bp(id, bp);
	}

	/// <summary>O BP base do jogador (e a ficha que sai dele), com o Ki cheio.</summary>
	internal void PalcoSY_Bp(int id, double bp)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.BP = bp;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		MandarFicha(pl);
	}

	/// <summary>O chefe deste molde que esta no mundo agora (o corpo), e a pod de chefe mais perto dele (nula se nao ha).</summary>
	internal (ServerPlayer Corpo, Vec2? Pod)? PalcoSY_Chefe(string molde)
	{
		foreach (EstadoDoElo est in _sagas.Elos)
			foreach (EstadoDoChefe ec in est.Chefes)
			{
				if (ec.Fase != FaseDoChefe.NoMundo || ec.Molde != molde || !_players.TryGetValue(ec.Corpo, out ServerPlayer? c)) continue;
				Obra? pod = _noChao.Where(o => o.Tipo == "Spacepod" && o.DoMapa && o.Zona.Equals(c.Zone))
					.MinBy(o => (c.Pos - new Vec2(o.X, o.Y)).Length);
				return (c, pod == null ? null : new Vec2(pod.X, pod.Y));
			}
		return null;
	}

	/// <summary>O jogador e posto num ponto livre perto deste (na mesma zona), pelo salto de posicao de sempre.</summary>
	internal void PalcoSY_PertoDe(int id, Vec2 onde)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		CravarPosicao(pl, MapaDaZonaOuCatalogo(pl.Zone)?.PontoLivrePerto(onde) ?? onde);
	}

	/// <summary>Um chefe e posto perto deste ponto (o Vegeta junto do Nappa: as pods caem longe uma da outra, e a foto e da luta).</summary>
	internal void PalcoSY_ChefePerto(string molde, Vec2 onde)
	{
		if (PalcoSY_Chefe(molde) is not { } c) return;
		Vec2 livre = MapaDaZonaOuCatalogo(c.Corpo.Zone)?.PontoLivrePerto(onde) ?? onde;
		c.Corpo.Pos = livre;
		c.Corpo.Moving = false;
	}

	/// <summary>O chefe deste molde cai pelas maos do jogador (a morte de producao: o tique das sagas a ve).</summary>
	internal void PalcoSY_Derrubar(string molde, int quem)
	{
		if (PalcoSY_Chefe(molde) is not { } c || !_players.TryGetValue(quem, out ServerPlayer? pl)) return;
		DerrubarSy(c.Corpo, pl);
	}

	/// <summary>Passam N dias do jogo, um por um, pela virada de producao.</summary>
	internal void PalcoSY_PularDias(int n)
	{
		for (int i = 0; i < n; i++) AvancarUmDia();
	}

	/// <summary>O chefe deste molde esta so observando agora?</summary>
	internal bool PalcoSY_Observando(string molde) => PalcoSY_Chefe(molde) is { } c && ObservandoDeTeste(c.Corpo.Id);

	/// <summary>Onde o corpo deste jogador esta no servidor (nulo = nao esta).</summary>
	internal Vec2? PalcoSY_PosicaoDe(int id) => _players.TryGetValue(id, out ServerPlayer? pl) ? pl.Pos : null;

	/// <summary>A fase de um elo da cadeia ("" = nao ha).</summary>
	internal string PalcoSY_Fase(string elo) => _sagas.Do(elo)?.Fase.ToString() ?? "";

	/// <summary>(2026-11-01, o adendo 210k) O indice do corpo de pele branca (`NewPaleMale`) na lista do Saiyajin macho do catalogo (-2 = nao ha).</summary>
	/// <summary>
	/// (2026-11-01, os adendos 210k e 210l) A FOTO DOS TRES, lado a lado e de frente pra camera: um corpo de cada molde pelo `NascerNpc` de
	/// producao (o corpo, o cabelo, a armadura e o scouter que o molde da), SEM CEREBRO -- estatua: o tique dos corpos sem dono so dirige quem
	/// tem um -- e olhando pro sul, dois tiles um do outro, tres acima do jogador. Devolve os ids, pra o robo os tirar depois.
	/// </summary>
	internal List<int> PalcoSY_OsTresLadoALado(int id)
	{
		var ids = new List<int>();
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return ids;
		const float T = ZoneCollision.TileSize;
		string[] moldes = ["raditz", "nappa", "vegeta_saiyajin"];
		for (int i = 0; i < moldes.Length; i++)
		{
			if (NascerNpc(moldes[i], pl.Zone, pl.Pos + new Vec2((i - 1) * 2 * T, -3 * T), (ulong)(9_100 + i)) is not { } c) continue;
			c.Cerebro = null;
			c.Facing = Facing.South;
			c.Moving = false;
			ids.Add(c.Id);
		}
		return ids;
	}

	/// <summary>Tira do mundo os corpos da foto dos tres.</summary>
	internal void PalcoSY_TirarOsTres(List<int> ids)
	{
		foreach (int i in ids)
			if (_players.TryGetValue(i, out ServerPlayer? c)) RemoverNpc(c);
	}

	internal int PalcoSY_IndiceDoCorpoBranco() =>
		_visual?.CorposDe("Saiyan").Para("Male") is { } l ? l.FindIndex(c => System.IO.Path.GetFileNameWithoutExtension(c) == CorpoDePeleBranca) : -2;
}
