using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DO ROBO `--diagpatrulha` (2026-10-31, o pedido 198) -- atalhos de montagem pras fotos: o que a bancada `--patrulhateste` prova
/// pelo caminho inteiro, aqui so se poe de pe pra fotografar (o procurado, a nave que pousa ja, o procurado forjado do lado, o Bills, a
/// edicao do jornal). O que a foto mostra continua sendo o jogo: a IA dos patrulheiros, o funil da derrota, a prisao, os verbos.
/// </summary>
public sealed partial class GameServer
{
	private readonly List<int> _forjadosDoPalcoPG = [];

	/// <summary>Longe do ponto de chegada da Terra: la e zona segura, e ninguem luta (o pedido 113).</summary>
	internal void PalcoPG_ForaDaZonaSegura(int id)
	{
		ZoneKey terra = ZoneKey.Premade("Earth");
		Vec2 longe = PontoDeNascimento(terra) + new Vec2(30 * ZoneCollision.TileSize, 18 * ZoneCollision.TileSize);
		MoveToZone(id, terra, MapaDaZonaOuCatalogo(terra)?.PontoLivrePerto(longe) ?? longe);
	}

	/// <summary>O corpo vira PROCURADO pelo gancho de verdade (cinco habitantes mortos por ele, ao lado dele) e, se pedir mais, pelos crimes direto.</summary>
	internal int PalcoPG_Procurado(int id, int nivel)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		for (int i = 0; i < 5 && NivelDeProcurado(pl) < 1; i++)
		{
			if (NascerNpc("cidadao", pl.Zone, pl.Pos + new Vec2((4 + i) * ZoneCollision.TileSize, 3 * ZoneCollision.TileSize), 7_100UL + (ulong)i) is not { } v)
				continue;
			v.UltimoAgressor = pl.Id;
			v.RancorAte = NowMs() + 60_000;
			v.Ficha.dead = true;
			MorreuUmCorpoSemDono(v);
			RemoverNpc(v);
		}
		while (NivelDeProcurado(pl) < nivel) CrimeContraOPovo(pl, pl.Zone.Name, false);
		return NivelDeProcurado(pl);
	}

	/// <summary>
	/// As naves comecam a descer agora: a cacada nasce e a hora dela e ja. Devolve quantas naves (uma por patrulheiro) vem -- (o pedido 202)
	/// a primeira pousa ja, e as outras chegam uma a uma pelo pulso.
	/// </summary>
	internal int PalcoPG_PousarJa(int id)
	{
		TickDaPatrulha();
		if (!_cacadas.TryGetValue(id, out Cacada? c)) return 0;
		c.ChegaEm = NowMs();
		TickDaPatrulha();
		return c.Patrulheiros.Count + c.NoCeu.Count;
	}

	/// <summary>(o pedido 202) As naves que ainda descem pousam agora -- o atalho das cenas que precisam do esquadrao inteiro no chao.</summary>
	private void PousarTodasAgora(Cacada c)
	{
		for (int i = 0; i < c.NoCeu.Count; i++) c.NoCeu[i] = (0, c.NoCeu[i].Onde, c.NoCeu[i].Poder, c.NoCeu[i].Lugar);
		PousarAsNavesDaVez();
	}

	/// <summary>As naves no chao, o esquadrao e as naves que ainda descem, da cacada deste corpo (zeros, sem cacada).</summary>
	internal (int Naves, List<int> Patrulheiros, int NoCeu) PalcoPG_Esquadrao(int id) =>
		_cacadas.TryGetValue(id, out Cacada? c) ? (c.Naves.Count, [.. c.Patrulheiros], c.NoCeu.Count) : (0, [], 0);

	/// <summary>(o pedido 202) As naves desta cacada no chao estao a pelo menos `TilesEntrePods` tiles umas das outras?</summary>
	/// <summary>
	/// (2026-10-31, o pedido 202, a foto) O host vai pra tres tiles ao sul da primeira nave pousada da cacada dele: a Spacepod no meio da
	/// cratera dela, no quadro -- o enquadramento de onde ele estava deixava os centros das crateras fora da tela ou atras do painel do canto.
	/// </summary>
	internal bool PalcoPG_PertoDeUmaNave(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !_cacadas.TryGetValue(id, out Cacada? c) || c.Naves.Count == 0) return false;
		if (_noChao.Find(o => o.Id == c.Naves[0]) is not { } nave) return false;
		Vec2 aoSul = new(nave.X, nave.Y + 3 * ZoneCollision.TileSize);
		CravarPosicao(pl, MapaDaZonaOuCatalogo(pl.Zone)?.PontoLivrePerto(aoSul) ?? aoSul);
		return true;
	}

	internal bool PalcoPG_NavesEspalhadas(int id)
	{
		if (!_cacadas.TryGetValue(id, out Cacada? c)) return false;
		List<Obra> naves = [.. _noChao.Where(o => c.Naves.Contains(o.Id))];
		for (int i = 0; i < naves.Count; i++)
			for (int j = i + 1; j < naves.Count; j++)
				if (Vec2.Distance(new Vec2(naves[i].X, naves[i].Y), new Vec2(naves[j].X, naves[j].Y)) < PatrulhaGalactica.TilesEntrePods * ZoneCollision.TileSize)
					return false;
		return naves.Count > 0;
	}

	/// <summary>O ATALHO: o primeiro patrulheiro nocauteia o corpo pelo funil de toda derrota -- so quando a IA nao o derrubou a tempo.</summary>
	internal void PalcoPG_Nocautear(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !_cacadas.TryGetValue(id, out Cacada? c)) return;
		if (c.Patrulheiros.Select(x => _players.GetValueOrDefault(x)).OfType<ServerPlayer>().FirstOrDefault() is not { } guarda) return;
		pl.Ficha.KO = true;
		AoPerderALuta(pl, guarda, morreu: false);
	}

	/// <summary>O corpo esta no planeta prisao, cumprindo pena?</summary>
	internal bool PalcoPG_Preso(int id) => _players.TryGetValue(id, out ServerPlayer? pl) && PresoNaPatrulha(pl) && NaZonaDaPrisao(pl.Zone);

	/// <summary>A pena cumprida: o tique solta, e o corpo volta pra Terra, fora da zona segura.</summary>
	internal void PalcoPG_Soltar(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || FichaOuNulo(pl) is not { } f) return;
		f.PresoAte = NowMs() - 1;
		TickDaPatrulha();
		PalcoPG_ForaDaZonaSegura(id);
	}

	/// <summary>Um procurado forjado ao lado do corpo, no nivel pedido (sem tela: a Patrulha nao o caca sozinha). Devolve o id.</summary>
	internal int PalcoPG_OutroProcurado(int id, string nome, int nivel)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		var o = new ServerPlayer
		{
			Id = _nextId++, Peer = null, Name = nome, Race = "Saiyan", Genero = "Male", Idade = 25, Zone = pl.Zone,
			Pos = pl.Pos + new Vec2(2 * ZoneCollision.TileSize, 0), Conta = "palco_pg_" + _forjadosDoPalcoPG.Count, Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Saiyan", BP = 5000 },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		o.Ficha.Class = "Normal";
		PorNoMundo(o);
		_forjadosDoPalcoPG.Add(o.Id);
		while (NivelDeProcurado(o) < nivel) CrimeContraOPovo(o, pl.Zone.Name, false);
		return o.Id;
	}

	/// <summary>O corpo derruba o outro: o nocaute e dele (o ultimo agressor).</summary>
	internal void PalcoPG_Derrubar(int idHost, int idOutro)
	{
		if (!_players.TryGetValue(idOutro, out ServerPlayer? o)) return;
		o.Ficha.KO = true;
		o.UltimoAgressor = idHost;
	}

	/// <summary>
	/// O DEUS DA DESTRUICAO: o corpo no nivel maximo, a nave pousa, o esquadrao inteiro cai pelas maos dele (o funil), e o tique chama o
	/// Bills. Devolve o id dele (0 = nao desceu).
	/// </summary>
	internal int PalcoPG_ChamarOBills(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		while (NivelDeProcurado(pl) < PatrulhaGalactica.NivelMaximo) CrimeContraOPovo(pl, pl.Zone.Name, false);
		PalcoPG_PousarJa(id);
		if (!_cacadas.TryGetValue(id, out Cacada? c)) return 0;
		PousarTodasAgora(c);   // (o pedido 202) o esquadrao inteiro no chao, antes de cair
		foreach (ServerPlayer o in c.Patrulheiros.Select(x => _players.GetValueOrDefault(x)).OfType<ServerPlayer>().ToList())
		{
			o.Ficha.KO = true;
			AoPerderALuta(o, pl, morreu: false);
		}
		TickDaPatrulha();
		return _billsDaPatrulha.FirstOrDefault();
	}

	/// <summary>Fim de cena: as cacadas acabam, os forjados saem, e a ficha do corpo zera (sem pena).</summary>
	internal void PalcoPG_EncerrarTudo(int id)
	{
		foreach (int alvo in _cacadas.Keys.ToList()) EncerrarACacada(alvo);
		foreach (int f in _forjadosDoPalcoPG)
			if (_players.TryGetValue(f, out ServerPlayer? o)) RemoverNpc(o);
		_forjadosDoPalcoPG.Clear();
		if (_players.TryGetValue(id, out ServerPlayer? pl) && FichaOuNulo(pl) is { } ficha)
		{
			ZerarOsCrimes(ficha);
			ficha.PresoAte = 0;
			SalvarAPatrulha();
		}
	}

	/// <summary>A edicao do dia, agora -- a virada do calendario sem esperar os 24 minutos.</summary>
	internal void PalcoPG_PublicarJornal() => PublicarOJornal(DiaDoCalendario, Calendario.De(DiaDoCalendario).Extenso);
}
