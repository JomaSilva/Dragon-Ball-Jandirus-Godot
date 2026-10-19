using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagcapital` (2026-10-18): a nave grande com maquinas dentro, a explosao, o leme, a viagem encurtada, o pod e
/// o pod que cai do ceu.
/// Os gestos sob teste passam pelas portas de producao (o embarque, o `Posicionar`, o `EstragarNave`, o leme, o `nave_sair`, o `UsarNave`, a
/// decolagem do pod); o palco so arma o lugar e encurta a viagem.
/// </summary>
public partial class GameServer
{
	private readonly List<Nave> _navesDoPalcoCS = [];
	private readonly List<int> _obrasDoPalcoCS = [];

	/// <summary>Uma nave do jogador, parada a <paramref name="tiles"/> tiles a leste dele (a nave grande com o casco do BP dele).</summary>
	private Nave PalcoCS_NovaNave(ServerPlayer pl, string tipo, float tiles)
	{
		var n = new Nave
		{
			Id = _proximaNaveId++, Tipo = tipo, X = pl.Pos.X + tiles * ZoneCollision.TileSize, Y = pl.Pos.Y,
			DonoConta = pl.Conta, DonoNome = pl.Name, ErguidaEm = NowMs(),
		};
		if (NaveGrande.EhNaveGrande(tipo))
		{
			n.ArmaduraMax = NaveGrande.ArmaduraMaxima(pl.Ficha.expressedBP);
			n.Armadura = n.ArmaduraMax;
		}
		n.PorZona(pl.Zone);
		_naves.Add(n);
		_navesDoPalcoCS.Add(n);
		GravarNaves();
		MandarObras(pl.Zone);
		return n;
	}

	/// <summary>
	/// C1: A NAVE COM AS DUAS MAQUINAS DENTRO. O corpo embarca (`nave_embarcar`), fabrica e assenta a maquina de gravidade e a mesa de pesquisa a
	/// dois tiles da chegada (`construir` e `posicionar`); a de gravidade aparafusada e ligada em 10x, alcance 2 (o estado que o menu dela daria).
	/// Devolve o id da nave, ou 0.
	/// </summary>
	internal int PalcoCS_NaveComMaquinas(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		pl.Ficha.techskill = Math.Max(pl.Ficha.techskill, 60);
		pl.Ficha.Zeni = Math.Max(pl.Ficha.Zeni, 5_000_000);
		Nave n = PalcoCS_NovaNave(pl, NaveGrande.Tipo, 2);
		pl.Pos = new Vec2(n.X - ZoneCollision.TileSize, n.Y);
		ComandoDeInteracao(pl, "nave_embarcar", "");
		ZoneKey dentro = NaveGrande.ZonaDoInterior(n.Id);
		if (!pl.Zone.Equals(dentro)) return 0;
		(int X, int Y) ch = NaveGrande.CelDaChegada;
		foreach ((string tipo, int dx) in new[] { ("Gravity", -2), ("Research_Station", 2) })
		{
			ComandoDeTech(pl, "construir", tipo);
			Vec2 p = NaveGrande.PixelDe((ch.X + dx, ch.Y));
			ComandoDeTech(pl, "posicionar", $"{tipo}/{p.X:0}/{p.Y:0}");
		}
		foreach (Obra o in _noChao.Where(o => o.Zona.Equals(dentro)))
		{
			_obrasDoPalcoCS.Add(o.Id);
			if (o.Tipo != "Gravity") continue;
			o.Aparafusada = true;
			o.Gravidade = new GravidadeDaObra { Grav = 10, Range = 2 };
		}
		MandarObras(dentro);
		return n.Id;
	}

	/// <summary>C2: a nave explode -- o golpe que derruba, sem algoz (o mesmo da bancada).</summary>
	internal void PalcoCS_Explodir(int nave)
	{
		if (_naves.FirstOrDefault(x => x.Id == nave) is { } n) EstragarNave(n, Math.Max(n.ArmaduraMax, 1) * 5, null);
	}

	/// <summary>
	/// C3: OUTRA NAVE GRANDE, pousada a tres tiles: o corpo embarca e, do console, assume o leme -- pelas portas de producao (`nave_embarcar` e
	/// `nave_pilotar`). Devolve o id da nave, ou 0.
	/// </summary>
	internal int PalcoCS_AoLeme(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		Nave n = PalcoCS_NovaNave(pl, NaveGrande.Tipo, 3);
		pl.Pos = new Vec2(n.X - ZoneCollision.TileSize, n.Y);
		ComandoDeInteracao(pl, "nave_embarcar", "");
		if (!pl.Zone.Equals(NaveGrande.ZonaDoInterior(n.Id))) return 0;
		pl.Pos = NaveGrande.PixelDe((NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y + 1));
		// (o Ki cheio e o treino de voo que moravam aqui sairam em 2026-10-18: ao leme quem paga o voo e o tanque da nave, e o corpo sem treino
		// nenhum nao cansa mais pilotando -- ver `GameServer.Combustivel.cs`)
		ComandoDeInteracao(pl, "nave_pilotar", "");
		return n.PilotoId == pl.Id ? n.Id : 0;
	}

	/// <summary>A nave esta no espaco?</summary>
	internal bool PalcoCS_NoEspaco(int nave) => _naves.FirstOrDefault(x => x.Id == nave) is { } n && Espaco.EhEspaco(n.Zona);

	/// <summary>C5: a sequencia de lancamento armada vence agora -- o `TickDasNaves` sobe a nave no proximo tique.</summary>
	internal void PalcoCS_LancarJa(int nave)
	{
		if (_naves.FirstOrDefault(x => x.Id == nave) is { LancaEm: > 0 } n) n.LancaEm = NowMs() - 1;
	}

	/// <summary>
	/// C5: A VIAGEM ENCURTADA -- a nave que segue sozinha e posta a 40 px da borda do planeta do rumo, do lado de onde ela vem; o resto (o passo, o
	/// pouso fora do berco, o aviso) e o tique de producao.
	/// </summary>
	internal void PalcoCS_PertoDoDestino(int nave)
	{
		if (_naves.FirstOrDefault(x => x.Id == nave) is not { TemRumo: true } n || !Espaco.EhEspaco(n.Zona)) return;
		if (Espaco.PlanetaSob(SeedDoUniverso, new Vec2(n.RumoX, n.RumoY)) is not { } p) return;
		Vec2 de = (p.Pos - new Vec2(n.X, n.Y)).Normalized();
		Vec2 borda = p.Pos - de * (float)(p.Raio + 40);
		n.X = borda.X;
		n.Y = borda.Y;
	}

	/// <summary>C6: o corpo na plataforma, e o verbo de producao que desce (`nave_sair`).</summary>
	internal void PalcoCS_Sair(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !NaveGrande.EhInterior(pl.Zone, out _)) return;
		pl.Pos = NaveGrande.PixelDe(NaveGrande.CelDaPlataforma);
		ComandoDeInteracao(pl, "nave_sair", "");
	}

	/// <summary>
	/// C7: um pod a um tile e meio: o corpo embarca (`UsarNave`) e decola (a tecla de subir com a nave no chao, `DecolarANavePousada`). Devolve o
	/// id do pod, ou 0.
	/// </summary>
	internal int PalcoCS_PodVoando(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		// COLADO NO CORPO: a nave grande pousada ao lado esta a um tile e meio dele, e o `UsarNave` embarca na MAIS PERTO (o job96 embarcou nela).
		Nave pod = PalcoCS_NovaNave(pl, "Spacepod", 0.25f);
		UsarNave(pl);
		if (pod.PilotoId != pl.Id) return 0;
		DecolarANavePousada(pl);
		return pod.Id;
	}

	// =====================================================================
	// (2026-10-18) C9-C11: O COMBUSTIVEL, O CHAT AO LEME E O OBSERVAR
	// =====================================================================
	/// <summary>C9: o pod em que o corpo esta, no ar -- a tecla de subir com a nave no chao (`DecolarANavePousada`). Devolve se esta voando.</summary>
	internal bool PalcoCS_PodNoAr(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || NaveDoPiloto(pl) is not { } n || !Naves.EhPod(n.Tipo)) return false;
		if (!pl.Voando) DecolarANavePousada(pl);
		return pl.Voando;
	}

	/// <summary>
	/// C9: o tanque da nave ao leme (ou da observada) em <paramref name="fracao"/> da capacidade, e a barra sai na hora. Zero e o tanque que SECA:
	/// pela porta de producao (`AcabouOCombustivel` -- a nave no ar desce e pousa, e quem esta nela ouve).
	/// </summary>
	internal void PalcoCS_Tanque(int id, double fracao)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || (NaveDoPiloto(pl) ?? NaveQueObservo(pl)) is not { } n) return;
		n.Tanque = Combustivel.Capacidade(n.Tipo) * fracao;
		if (fracao <= 0) { AcabouOCombustivel(n); return; }
		_pilotosSemCombustivel.Remove(pl.Id);
		MandarCombustivelDaNave(n);
	}

	/// <summary>C9: recursos no bolso do corpo (pro abastecer pela tecla E), e a carteira sai pro cliente.</summary>
	internal void PalcoCS_Recursos(int id, double quanto)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.Recursos = quanto;
		MandarCarteira(pl);
	}

	/// <summary>O tripulante forjado das cenas C10 e C11 (0 = nenhum).</summary>
	private int _tripulanteDoPalcoCS;

	/// <summary>
	/// C10/C11: UMA NAVE GRANDE PARADA NO ESPACO, ao lado da decolagem da Terra: o corpo sobe ao espaco ao lado dela, embarca (`nave_embarcar`) e
	/// fica de pe ao lado do console; e um tripulante forjado fica perto da ponte. Devolve o id da nave, ou 0.
	/// </summary>
	internal int PalcoCS_NaveNoEspacoComTripulante(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		PalcoCS_Limpar(id);
		PlanetaNoEspaco terra = Espaco.PreFeitos().First(p => string.Equals(p.Nome, "Earth", StringComparison.OrdinalIgnoreCase));
		Vec2 onde = Espaco.PontoDeDecolagem(terra) + new Vec2(60 * ZoneCollision.TileSize, 0);
		MoveToZone(pl.Id, ZonaDoEspaco, new Vec2(onde.X - ZoneCollision.TileSize, onde.Y));
		pl.ChunkAtual = ChunkId.De(pl.Pos);
		MandarVizinhanca(pl);
		Nave n = PalcoCS_NovaNave(pl, NaveGrande.Tipo, 1);
		ComandoDeInteracao(pl, "nave_embarcar", "");
		ZoneKey dentro = NaveGrande.ZonaDoInterior(n.Id);
		if (!pl.Zone.Equals(dentro)) return 0;
		pl.Pos = NaveGrande.PixelDe((NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y + 1));
		ServerPlayer tripulante = Forjar("Tripulante", NaveGrande.PixelDe(NaveGrande.CelDoConsole) + new Vec2(2 * ZoneCollision.TileSize, 3 * ZoneCollision.TileSize),
										 dentro, "bancada_tripulante_capital");
		_tripulanteDoPalcoCS = tripulante.Id;
		return n.Id;
	}

	/// <summary>C11: o tripulante fala, pelo funil de producao (`Falar`).</summary>
	internal void PalcoCS_TripulanteFala(string texto)
	{
		if (!_players.TryGetValue(_tripulanteDoPalcoCS, out ServerPlayer? t)) return;
		_ultimaFala.Remove(t.Id);
		Falar(t, Jandirus.Net.Protocol.Fala.Diz, texto);
	}

	/// <summary>C10: a nave segue sozinha pra longe (o rumo do nav system, sem ninguem ao leme) -- quem a observa a ve andar.</summary>
	internal void PalcoCS_Seguir(int nave)
	{
		if (_naves.FirstOrDefault(x => x.Id == nave) is not { } n || !Espaco.EhEspaco(n.Zona)) return;
		n.TemRumo = true;
		n.RumoX = n.X + 200_000;
		n.RumoY = n.Y;
	}

	/// <summary>C10: onde o meu olho esta e onde o casco esta (zona e ponto), pro robo comparar.</summary>
	internal (bool Observando, ZoneKey Zona, Vec2 Olho, Vec2 Casco) PalcoCS_Olho(int id, int nave)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _naves.FirstOrDefault(x => x.Id == nave) is not { } n) return (false, default, default, default);
		return (pl.ObservaNave == nave, pl.Zone, pl.Pos, new Vec2(n.X, n.Y));
	}

	/// <summary>
	/// C8: O POD SOBE PRO ESPACO, ao lado do planeta e fora do disco dele (o `TickDoEspaco` ainda nao o pousa) -- o corpo pelo `MoveToZone` de
	/// producao, com o carimbo de chunk e a vizinhanca do `Decolar`; a nave copia o piloto no `TickDasNaves`. Falso se o corpo nao esta num pod.
	/// </summary>
	internal bool PalcoCS_PodNoEspaco(int id, string planeta)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || NaveDoPiloto(pl) is not { } n || !Naves.EhPod(n.Tipo)) return false;
		PlanetaNoEspaco p = Espaco.PreFeitos().First(x => string.Equals(x.Nome, planeta, StringComparison.OrdinalIgnoreCase));
		Vec2 aoLado = p.Pos + new Vec2((float)p.Raio * 3, 0);
		for (int i = 2; i < 10 && Espaco.PlanetaSob(SeedDoUniverso, aoLado) != null; i++) aoLado = p.Pos + new Vec2((float)p.Raio * 3 * i, 0);
		MoveToZone(pl.Id, ZonaDoEspaco, aoLado);
		pl.ChunkAtual = ChunkId.De(pl.Pos);
		MandarVizinhanca(pl);
		return true;
	}

	/// <summary>C8: o pod ENCOSTA no planeta -- o corpo cravado sobre o disco; o pouso (o lugar sorteado e a cratera) e o tique de producao.</summary>
	internal void PalcoCS_PodSobre(int id, string planeta)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !Espaco.EhEspaco(pl.Zone)) return;
		CravarPosicao(pl, Espaco.PreFeitos().First(x => string.Equals(x.Nome, planeta, StringComparison.OrdinalIgnoreCase)).Pos);
	}

	/// <summary>C8: ONDE O POD POUSOU -- se a celula e terra firme com a folga do pod (a regra do Core), a distancia ao berco e a celula.</summary>
	internal (bool Firme, float DoBerco, (int X, int Y) Celula) PalcoCS_OndePousou(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || MapaDaZonaOuCatalogo(pl.Zone) is not { } mapa) return (false, 0, (0, 0));
		int cx = (int)MathF.Floor(pl.Pos.X / ZoneCollision.TileSize), cy = (int)MathF.Floor(pl.Pos.Y / ZoneCollision.TileSize);
		return (PousoSorteado.TerraFirme(mapa, cx, cy, PousoSorteado.FolgaDoPod) && PousoSorteado.LigadaAoMundo(mapa, cx, cy),
			(pl.Pos - PontoDeNascimento(pl.Zone)).Length, (cx, cy));
	}

	/// <summary>Tira o corpo do leme e as naves e obras do palco do mundo.</summary>
	internal void PalcoCS_Limpar(int id)
	{
		// (2026-10-18) o olho volta pro corpo antes de a nave sair do mundo, e o tripulante forjado sai
		if (_players.TryGetValue(id, out ServerPlayer? quemOlha) && EhOlhoDeFora(quemOlha)) PararDeObservar(quemOlha, "");
		if (_tripulanteDoPalcoCS != 0 && _players.TryGetValue(_tripulanteDoPalcoCS, out ServerPlayer? tripulante)) RemoverNpc(tripulante);
		_tripulanteDoPalcoCS = 0;
		if (_players.TryGetValue(id, out ServerPlayer? pl) && NaveDoPiloto(pl) is { } aoLeme)
		{
			if (NaveGrande.EhNaveGrande(aoLeme.Tipo)) PararDePilotar(pl, aoLeme);
			else Desembarcar(pl, aoLeme);
		}
		var zonas = new List<ZoneKey>();
		foreach (Nave x in _navesDoPalcoCS)
		{
			zonas.Add(x.Zona);
			_naves.Remove(x);
		}
		foreach (int oid in _obrasDoPalcoCS)
			if (_noChao.FirstOrDefault(o => o.Id == oid) is { } o)
			{
				zonas.Add(o.Zona);
				_noChao.Remove(o);
			}
		_navesDoPalcoCS.Clear();
		_obrasDoPalcoCS.Clear();
		GravarNaves();
		GravarMundo();
		foreach (ZoneKey z in zonas.Distinct()) MandarObras(z);
	}
}
