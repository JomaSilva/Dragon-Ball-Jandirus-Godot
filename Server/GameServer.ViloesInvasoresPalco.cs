using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS MAOS DO ROBO `--diaginvasao` (o pedido 227) do lado do servidor. Nenhuma regra mora aqui: o vilao solto, a cena da explosao e o tedio
/// do Kid sao os de producao (`GameServer.ViloesInvasores.cs`); o palco so poe as pecas no lugar e encurta o relogio.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// AS FOTOS: o vilao solto deixa o jogador de fora da caca (a foto mostra o POVO apanhando, e nao o fotografo). Falso em jogo, sempre.
	/// </summary>
	internal static bool SoltoSoCacaOPovoDeTeste;

	/// <summary>
	/// UM VILAO SOLTO DESTE ELO JA NUM PLANETA (sem a vitoria da saga): o molde do ultimo chefe do elo, o degrau pedido, os BPs da ficha dele, e
	/// os dois dias acabando em <paramref name="segundos"/> s. O jogador do robo nao se fere (a cena da explosao e pra ser vista). Falso sem elo.
	/// </summary>
	internal bool PalcoInv_Soltar(int eu, string eloId, ZoneKey zona, int degrau, double segundos)
	{
		GarantirOsSoltos();
		if (Array.Find(_cadeia, e => e.Id == eloId) is not { } elo || elo.Chefes.Length == 0) return false;
		string molde = elo.Chefes[^1].Molde;
		if (_moldes?.Get(molde) is not { } m) return false;
		foreach (VilaoSolto velho in _soltos.Where(x => x.Elo == eloId).ToList()) RecolherOSolto(velho);
		_soltos.RemoveAll(x => x.Elo == eloId);
		var v = new VilaoSolto
		{
			Elo = eloId, Molde = molde, Nome = m.Nome, Jeito = ViloesSoltos.Jeito.Invasor, ComExercito = false,
			Degrau = Math.Clamp(degrau, 0, Math.Max(m.Estagios.Length - 1, 0)), Bps = [.. m.Estagios.Select(e => e.Bp)],
		};
		v.PorZona(zona);
		v.SaiEm = NowMs() + (long)(segundos * 1000);
		_soltos.Add(v);
		LigarPulso();
		if (_players.TryGetValue(eu, out ServerPlayer? pl)) pl.Combate.NaoSeFere = true;
		return true;
	}

	/// <summary>O corpo de pe do vilao solto deste elo (0 = nenhum), a presa dele, se ele esta sumido e se a cena da explosao esta no ar.</summary>
	internal (int Corpo, int Presa, bool PresaDoPovo, bool Sumido, bool EmCena) PalcoInv_Solto(string eloId)
	{
		if (_soltos.Find(x => x.Elo == eloId) is not { } v) return (0, 0, false, false, false);
		int presa = _players.TryGetValue(v.Corpo, out ServerPlayer? c) ? c.PresaEngajada : 0;
		bool doPovo = _players.TryGetValue(presa, out ServerPlayer? p) && p.Peer == null && p.Papel?.Tipo == TipoDeNpc.Cidadao;
		return (v.Corpo, presa, doPovo, v.Sumido, v.ExplodeEm != 0);
	}

	/// <summary>
	/// O KID NO CEU, ENTEDIADO NA FRENTE DO JOGADOR: o jogador vai pro Ceu, o Kid nasce a tres tiles dele (o molde e o degrau da saga) e o tedio
	/// de producao (`KidEntediado`) o leva -- o clarao do IT e a frase. O elo e o estado sao de mentira (a saga de verdade nao e mexida).
	/// </summary>
	internal bool PalcoInv_KidEntediado(int eu)
	{
		if (!_players.TryGetValue(eu, out ServerPlayer? pl)) return false;
		if (Array.Find(_cadeia, e => e.Id == "boo") is not { } elo || elo.Chefes.Length == 0) return false;
		var ceu = ZoneKey.Premade(SagaDoBuu.ZonaDoKid);
		MoveToZone(eu, ceu, PontoDeNascimento(ceu));
		string molde = elo.Chefes[^1].Molde;
		// o jogador que nao entrou no Ceu (uma regra do alem o barrou) ve a cena onde esta: e o mesmo IT e a mesma frase
		ZoneKey onde = pl.Zone.Equals(ceu) ? ceu : pl.Zone;
		if (NascerNpc(molde, onde, pl.Pos + new Vec2(3 * ZoneCollision.TileSize, 0), 0x1227_0001UL) is not { Papel: { } papel } kid) return false;
		papel.Estagio = SagaDoBuu.DegrauKid;
		EntrarNoDegrau(kid, cura: 0, anunciar: false);
		kid.Combate.Letal = false;
		_kidDoPalcoInv = (kid, elo, new EstadoDoElo { Id = "boo-palco" }, new EstadoDoChefe { Molde = molde, Fase = FaseDoChefe.NoMundo, Corpo = kid.Id, NoCeu = true });
		return true;
	}

	/// <summary>O Kid do palco, esperando o robo olhar antes de se entediar.</summary>
	private (ServerPlayer Kid, EloDaSaga Elo, EstadoDoElo Est, EstadoDoChefe Ec)? _kidDoPalcoInv;

	/// <summary>O Kid do palco se entedia agora (o mesmo `KidEntediado` do jogo). Falso sem o Kid.</summary>
	internal bool PalcoInv_Entediar()
	{
		if (_kidDoPalcoInv is not { } k || !_players.ContainsKey(k.Kid.Id)) return false;
		_kidDoPalcoInv = null;
		KidEntediado(k.Kid, k.Elo, k.Est, k.Ec);
		return true;
	}

	/// <summary>Arruma a casa: os soltos do palco somem, os planetas que explodiram voltam, e o jogador volta a se ferir.</summary>
	internal void PalcoInv_Limpar(int eu, params ZoneKey[] planetas)
	{
		SoltoSoCacaOPovoDeTeste = false;
		foreach (VilaoSolto v in _soltos.ToList()) RecolherOSolto(v);
		_soltos.Clear();
		SalvarOsSoltos();
		if (_kamehamehaDoCell is { DoSolto: true }) _kamehamehaDoCell = null;
		foreach (ZoneKey z in planetas)
			if (ZonaCondenada(z) || ZonaMorta(z)) RessuscitarPlaneta(z);
		if (_players.TryGetValue(eu, out ServerPlayer? pl)) pl.Combate.NaoSeFere = false;
	}

	/// <summary>
	/// O FOTOGRAFO NAO ESTA NUMA LUTA: o povo apanhando do vilao encosta nele (a etiqueta de briga sobe mesmo sem dano), e no meio de uma luta o
	/// prazo do vilao se estende (o pedido 206) -- a cena dos dois dias podia nunca chegar (a r2 dos invasores reprovou uma vez assim). So o
	/// robo chama, a cada passo da espera.
	/// </summary>
	internal void PalcoInv_SemBriga(int eu)
	{
		if (!_players.TryGetValue(eu, out ServerPlayer? pl)) return;
		pl.Combate.EmCombate = 0;
		pl.Combate.BrigandoComAlguem = 0;
	}

	/// <summary>
	/// O FOTOGRAFO VAI PRA PERTO DO VILAO deste elo (<paramref name="tilesAoSul"/> tiles ao sul dele -- negativo e ao norte --, no chao livre
	/// mais perto): a cena da explosao acontece em cima do corpo dele, e a praca pode estar longe. Falso sem o corpo.
	/// </summary>
	internal bool PalcoInv_PertoDoSolto(int eu, string eloId, int tilesAoSul)
	{
		if (!_players.TryGetValue(eu, out ServerPlayer? pl) || _soltos.Find(x => x.Elo == eloId) is not { } v
			|| !_players.TryGetValue(v.Corpo, out ServerPlayer? corpo) || MapaDaZonaOuCatalogo(corpo.Zone) is not { } mapa) return false;
		CravarPosicao(pl, mapa.PontoLivrePerto(corpo.Pos + new Vec2(0, tilesAoSul * ZoneCollision.TileSize)), 2000);
		return true;
	}

	/// <summary>O planeta esta condenado ou morto?</summary>
	internal bool PalcoInv_Explodindo(ZoneKey z) => ZonaCondenada(z) || ZonaMorta(z);
}
