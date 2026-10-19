using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A CAMADA 8 DA `--naveteste` (2026-10-18): O POUSO NUM LUGAR SORTEADO E A CRATERA DO POD. O dono: *"faça a pod e a nave quando chegarem no
/// planeta pousarem em algum local aleatorio com terra firme, e a pod quando pousa faz uma cratera grande"*.
///   a. (o Core, em mapas de mentira) o sorteio so devolve TERRA FIRME: num mar com uma ilha, o ponto cai na ilha com a folga inteira em chao a
///      ceu aberto -- e o sorteio cru (so "nao e parede") cai no mar, o contra-exemplo; a ilhota menor que a `RegiaoMinima`, o patio murado e a
///      ilha coberta inteira (o salao de um predio) nao servem; o ponto fica longe dos corpos; a mesma semente da o mesmo ponto;
///   b. (producao) o pod que desce do espaco na Terra pousa num lugar SORTEADO em terra firme, fora do berco, e cada descida cai num lugar
///      diferente; contra-exemplo `PousoNoPontoDeSempreDeTeste` (o berco, o jogo de antes);
///   c. ...e abre a CRATERA DO POD ali, pra zona inteira, com a poeira no meio e na borda; contra-exemplo `PodSemCrateraDeTeste`;
///   d. quem desce VOANDO sozinho continua chegando no berco, e sem cratera;
///   e. a Capital Ship ao leme pousa num lugar sorteado com a folga do casco, e SEM cratera;
///   f. a Capital Ship seguindo sozinha pousa num lugar sorteado com a folga do casco; contra-exemplo: o ponto de antes, ao lado do berco;
///   g. num planeta GERADO o pod pousa num lugar sorteado do mundo gerado (fora da chegada do gerador), e abre a cratera la.
/// Ela empresta o corpo do dono como as outras camadas, e devolve tudo no fim.
/// </summary>
public partial class GameServer
{
	private void RodarBancadaDoPousoSorteado(ServerPlayer pl, Action<string, bool, string> checa)
	{
		GD.Print("  -- 8) o pouso num lugar sorteado e a cratera do pod (dono, 2026-10-18) --");
		const int t = ZoneCollision.TileSize;

		// ---------------------------------------------------------------- a. o sorteio, em mapas de mentira
		RodarOSorteioEmMapasDeMentira(checa);

		ZoneKey zonaAntes = pl.Zone;
		Vec2 posAntes = pl.Pos;
		ZoneKey terra = ZoneKey.Premade("Earth");
		PlanetaNoEspaco terraNoEspaco = Espaco.PreFeitos().First(p => string.Equals(p.Nome, "Earth", StringComparison.OrdinalIgnoreCase));
		PlanetaNoEspaco namek = Espaco.PreFeitos().First(p => string.Equals(p.Nome, "Namek", StringComparison.OrdinalIgnoreCase));
		ZoneKey zonaDeNamek = ZoneKey.Premade(namek.Nome);
		var forjadas = new List<Nave>();

		// A CELULA DE UM PONTO, e a pergunta da regra -- a do Core, a mesma que o servidor fez.
		static (int X, int Y) Celula(Vec2 p) => ((int)MathF.Floor(p.X / t), (int)MathF.Floor(p.Y / t));
		static bool Firme(ZoneCollision? mapa, Vec2 p, int folga) =>
			mapa != null && PousoSorteado.TerraFirme(mapa, Celula(p).X, Celula(p).Y, folga) && PousoSorteado.LigadaAoMundo(mapa, Celula(p).X, Celula(p).Y);

		// DESCER DO ESPACO, pelas portas de producao: o corpo (e a nave dele, pelo `TickDasNaves`) posto sobre o disco, e o tique do espaco o
		// pousa. Devolve onde ele ficou e os decalques que o pouso mandou.
		(Vec2 Onde, List<(ulong Zona, Protocol.Decal Tipo, byte[] Fio)> Decalques) Descer(PlanetaNoEspaco p)
		{
			MoveToZone(pl.Id, ZonaDoEspaco, p.Pos);
			TickDasNaves();
			EscutaDeDecalques = [];
			try
			{
				TickDoEspaco(pl);
				TickDasNaves();
				return (pl.Pos, EscutaDeDecalques);
			}
			finally { EscutaDeDecalques = null; }
		}
		static int Quantas(List<(ulong Zona, Protocol.Decal Tipo, byte[] Fio)> d, ZoneKey z, Protocol.Decal tipo) =>
			d.Count(x => x.Zona == z.Hash && x.Tipo == tipo);

		try
		{
			ZoneCollision? mapaDaTerra = MapaDaZonaOuCatalogo(terra);
			Vec2 berco = PontoDeNascimento(terra);

			// ---------------------------------------------------------------- montagem: um pod na Terra, o dono ao leme
			MoveToZone(pl.Id, terra, berco);
			var pod = new Nave { Id = _proximaNaveId++, Tipo = "Spacepod", X = berco.X, Y = berco.Y, DonoConta = pl.Conta, DonoNome = pl.Name, ErguidaEm = NowMs() };
			pod.PorZona(terra);
			_naves.Add(pod);
			forjadas.Add(pod);
			ComandoDeInteracao(pl, "nave_usar", "");
			checa("(montagem) o dono embarca num pod na Terra, pela porta de producao", pod.PilotoId == pl.Id && mapaDaTerra != null,
				  $"piloto {pod.PilotoId}; mapa {mapaDaTerra != null}");
			if (pod.PilotoId != pl.Id || mapaDaTerra == null) return;

			// ---------------------------------------------------------------- b. o lugar sorteado
			var pousos = new List<Vec2>();
			var crateras = new List<(int Crateras, int Poeiras, bool NoPonto)>();
			for (int i = 0; i < 4; i++)
			{
				(Vec2 onde, List<(ulong Zona, Protocol.Decal Tipo, byte[] Fio)> dec) = Descer(terraNoEspaco);
				if (!pl.Zone.Equals(terra)) break;
				pousos.Add(onde);
				bool noPonto = dec.Where(x => x.Zona == terra.Hash && x.Tipo == Protocol.Decal.CrateraDoPod)
								  .All(x => (LerDecalque(x.Fio).Onde - onde).Length < 1f);
				crateras.Add((Quantas(dec, terra, Protocol.Decal.CrateraDoPod), Quantas(dec, terra, Protocol.Decal.Fumaca), noPonto));
			}
			int distintos = pousos.Select(Celula).Distinct().Count();
			checa("**O POD POUSA NUM LUGAR SORTEADO, EM TERRA FIRME** (*\"pousarem em algum local aleatorio com terra firme\"*): quatro descidas na "
				  + "Terra, as quatro em chao a ceu aberto com a folga do pod (5x5) e ligado ao resto do mapa, longe do berco -- e cada uma num lugar",
				  pousos.Count == 4 && pousos.All(p => Firme(mapaDaTerra, p, PousoSorteado.FolgaDoPod)) && pousos.All(p => (p - berco).Length >= 3 * t)
				  && distintos == 4 && pod.Zona.Equals(terra),
				  string.Join("; ", pousos.Select(p => $"({Celula(p).X},{Celula(p).Y}) firme {Firme(mapaDaTerra, p, PousoSorteado.FolgaDoPod)}"))
				  + $"; berco ({Celula(berco).X},{Celula(berco).Y}); {distintos} distintos; pod em {pod.Zona}");
			PousoNoPontoDeSempreDeTeste = true;
			Vec2 deAntes;
			try { deAntes = Descer(terraNoEspaco).Onde; }
			finally { PousoNoPontoDeSempreDeTeste = false; }
			checa("   DEFEITO INJETADO (o pouso de antes): o pod desce no berco, sempre o mesmo -- a regua de cima reprovaria",
				  (deAntes - berco).Length < 1f, $"pousou a {(deAntes - berco).Length:0} px do berco");

			// ---------------------------------------------------------------- c. a cratera
			checa("**...E O POD ABRE UMA CRATERA GRANDE** (*\"a pod quando pousa faz uma cratera grande\"*): cada pouso manda pra zona da Terra UMA "
				  + "`CrateraDoPod` no ponto do pouso, e a poeira no meio e nas quatro pontas da borda",
				  crateras.Count == 4 && crateras.All(c => c.Crateras == 1 && c.Poeiras == 5 && c.NoPonto),
				  string.Join("; ", crateras.Select(c => $"{c.Crateras} cratera(s), {c.Poeiras} poeira(s), no ponto {c.NoPonto}")));
			PodSemCrateraDeTeste = true;
			List<(ulong Zona, Protocol.Decal Tipo, byte[] Fio)> semCratera;
			try { semCratera = Descer(terraNoEspaco).Decalques; }
			finally { PodSemCrateraDeTeste = false; }
			checa("   DEFEITO INJETADO (o pod de antes): pousa sem cratera nenhuma -- a regua de cima reprovaria",
				  pl.Zone.Equals(terra) && Quantas(semCratera, terra, Protocol.Decal.CrateraDoPod) == 0,
				  $"{Quantas(semCratera, terra, Protocol.Decal.CrateraDoPod)} cratera(s)");

			// ---------------------------------------------------------------- d. quem voa sozinho
			ComandoDeInteracao(pl, "nave_usar", "");   // desce do pod (ele fica parado onde pousou)
			bool desceu = pod.PilotoId == 0;
			(Vec2 aPe, List<(ulong Zona, Protocol.Decal Tipo, byte[] Fio)> semNave) = Descer(terraNoEspaco);
			checa("...e quem desce do espaco VOANDO SOZINHO, sem nave, continua chegando no berco -- e sem cratera (o pedido e da nave)",
				  desceu && pl.Zone.Equals(terra) && (aPe - berco).Length < 1f && Quantas(semNave, terra, Protocol.Decal.CrateraDoPod) == 0,
				  $"desceu do pod {desceu}; a {(aPe - berco).Length:0} px do berco; {Quantas(semNave, terra, Protocol.Decal.CrateraDoPod)} cratera(s)");

			// ---------------------------------------------------------------- e. a Capital Ship ao leme
			var nave = new Nave
			{
				Id = _proximaNaveId++, Tipo = NaveGrande.Tipo, X = berco.X + 6 * t, Y = berco.Y, DonoConta = pl.Conta, DonoNome = pl.Name,
				ErguidaEm = NowMs(), ArmaduraMax = NaveGrande.ArmaduraMaxima(pl.Ficha.expressedBP),
			};
			nave.Armadura = nave.ArmaduraMax;
			nave.PorZona(terra);
			_naves.Add(nave);
			forjadas.Add(nave);
			void AssumirOLeme()
			{
				pl.Pos = NaveGrande.PixelDe((NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y + 1));
				ComandoDeInteracao(pl, "nave_pilotar", "");
			}
			MoveToZone(pl.Id, terra, new Vec2(nave.X - t, nave.Y));
			ComandoDeInteracao(pl, "nave_embarcar", "");
			AssumirOLeme();
			bool aoLeme = nave.PilotoId == pl.Id;
			(Vec2 daNave, List<(ulong Zona, Protocol.Decal Tipo, byte[] Fio)> decDaNave) =
				aoLeme ? Descer(terraNoEspaco) : (pl.Pos, new List<(ulong Zona, Protocol.Decal Tipo, byte[] Fio)>());
			checa("**A CAPITAL SHIP AO LEME TAMBEM POUSA NUM LUGAR SORTEADO**, com a folga do casco (7x7 em terra firme) -- e SEM cratera (o "
				  + "pedido da cratera e do pod)",
				  aoLeme && nave.Zona.Equals(terra) && Firme(mapaDaTerra, daNave, PousoSorteado.FolgaDaNaveGrande) && (daNave - berco).Length >= 3 * t
				  && Math.Abs(nave.X - daNave.X) < 1f && Quantas(decDaNave, terra, Protocol.Decal.CrateraDoPod) == 0,
				  $"ao leme {aoLeme}; nave em {nave.Zona} ({Celula(daNave).X},{Celula(daNave).Y}); firme {Firme(mapaDaTerra, daNave, PousoSorteado.FolgaDaNaveGrande)}; "
				  + $"{Quantas(decDaNave, terra, Protocol.Decal.CrateraDoPod)} cratera(s)");

			// ---------------------------------------------------------------- f. a Capital Ship seguindo sozinha
			ZoneCollision? mapaDeNamek = MapaDaZonaOuCatalogo(zonaDeNamek);
			Vec2 PontoDeAntes() => ChaoLivrePerto(zonaDeNamek, PontoDeNascimento(zonaDeNamek) + new Vec2(3 * t, 0), 0, mapaDeNamek,
				[.. _noChao.Where(o => o.Zona.Equals(zonaDeNamek)).Select(o => new Vec2(o.X, o.Y))
					.Concat(NavesParadasEm(zonaDeNamek).Select(x => new Vec2(x.X, x.Y)))], ZoneList(zonaDeNamek.Hash));
			Vec2 antes = PontoDeAntes();
			ComandoDeInteracao(pl, "nave_pilotar", $"{namek.Pos.X:0}/{namek.Pos.Y:0}");
			nave.LancaEm = NowMs() - 1;
			TickDasNaves();
			bool sozinha = Espaco.EhEspaco(nave.Zona) && nave.TemRumo && nave.PilotoId == 0;
			if (sozinha)
			{
				nave.X = namek.Pos.X;
				nave.Y = namek.Pos.Y;
				for (int i = 0; i < 5 && nave.TemRumo; i++) TickDasNaves();
			}
			var sozinhaEm = new Vec2(nave.X, nave.Y);
			// O CHAO E MEDIDO SEM A PROPRIA NAVE: parada, ela e densa (`AplicarColisaoDasObras` bloqueia a celula dela), e a celula embaixo deixa de
			// "servir de chao" -- o que se mede e o chao em que ela pousou (o job100 mediu com ela em cima e reprovou um pouso certo).
			bool firmeSozinha;
			nave.PorZona(ZonaDoEspaco);
			AplicarColisaoDasObras(zonaDeNamek);
			try { firmeSozinha = Firme(mapaDeNamek, sozinhaEm, PousoSorteado.FolgaDaNaveGrande); }
			finally { nave.PorZona(zonaDeNamek); AplicarColisaoDasObras(zonaDeNamek); }
			checa("**A CAPITAL SHIP QUE SEGUE SOZINHA POUSA NUM LUGAR SORTEADO** em Namek, com a folga do casco -- e nao mais no ponto de antes, ao "
				  + "lado do berco",
				  sozinha && nave.Zona.Equals(zonaDeNamek) && !nave.TemRumo && firmeSozinha && (sozinhaEm - antes).Length >= 2 * t,
				  $"subiu sozinha {sozinha}; nave em {nave.Zona} ({Celula(sozinhaEm).X},{Celula(sozinhaEm).Y}) firme {firmeSozinha}; "
				  + $"ponto de antes ({Celula(antes).X},{Celula(antes).Y})");
			nave.PorZona(ZonaDoEspaco);
			nave.X = namek.Pos.X;
			nave.Y = namek.Pos.Y;
			nave.TemRumo = true;
			PousoNoPontoDeSempreDeTeste = true;
			try { PousarNaveSozinha(nave, namek); }
			finally { PousoNoPontoDeSempreDeTeste = false; }
			checa("   DEFEITO INJETADO (o pouso de antes): a nave sozinha desce no ponto ao lado do berco -- a regua de cima reprovaria",
				  nave.Zona.Equals(zonaDeNamek) && (new Vec2(nave.X, nave.Y) - antes).Length < 1f,
				  $"a {(new Vec2(nave.X, nave.Y) - antes).Length:0} px do ponto de antes");

			// ---------------------------------------------------------------- g. num planeta gerado
			MoveToZone(pl.Id, terra, new Vec2(pod.X, pod.Y + t));
			ComandoDeInteracao(pl, "nave_usar", "");
			// O GERADO MAIS PERTO DA TERRA: a vizinhanca de 3x3 chunks costuma nao ter nenhum (os sistemas sao agrupados e o universo e vazio -- o
			// job100), entao a busca abre o raio ate achar.
			PlanetaNoEspaco? gerado = null;
			foreach (int raio in new[] { 4, 12, 30, 60 })
			{
				gerado = Espaco.PorPerto(SeedDoUniverso, ChunkId.De(terraNoEspaco.Pos), raio)
					.Where(p => !p.Premade && !PlanetaMorto(p)).OrderBy(p => (p.Pos - terraNoEspaco.Pos).Length).Cast<PlanetaNoEspaco?>().FirstOrDefault();
				if (gerado != null) break;
			}
			if (pod.PilotoId != pl.Id || gerado is not { } g)
			{
				checa("(montagem) o dono volta ao pod e ha um planeta gerado perto da Terra", false, $"piloto {pod.PilotoId}; gerado {gerado?.Nome}");
				return;
			}
			ZoneKey zg = ZoneKey.Procedural(g.Nome, g.Seed);
			MoveToZone(pl.Id, ZonaDoEspaco, g.Pos);
			TickDasNaves();
			TickDoEspaco(pl);   // encomenda o mundo (ele nasce fora do tique) -- o pod segura a orbita
			var relogio = System.Diagnostics.Stopwatch.StartNew();
			while (MundoDaZona(zg) == null && relogio.Elapsed.TotalSeconds < 60)
			{
				System.Threading.Thread.Sleep(20);
				TickDasGeracoes();
			}
			List<(ulong Zona, Protocol.Decal Tipo, byte[] Fio)> decDoGerado;
			EscutaDeDecalques = [];
			try
			{
				TickDoEspaco(pl);
				TickDasNaves();
				decDoGerado = EscutaDeDecalques;
			}
			finally { EscutaDeDecalques = null; }
			Vec2? chegada = ChegadaDaZonaGerada(zg);
			// O MUNDO PODE NAO TER ONDE (um oceano, uma cordilheira): ai o certo e a chegada do gerador. A regua pergunta a mesma regra antes.
			bool haLugar = MapaDaZona(zg) is { } mz && PousoSorteado.Sortear(mz, new Random(1), PousoSorteado.FolgaDoPod) != null;
			bool ondeDevia = chegada is { } ch && (haLugar
				? (pl.Pos - ch).Length >= 3 * t && Firme(MapaDaZona(zg), pl.Pos, PousoSorteado.FolgaDoPod)
				: (pl.Pos - ch).Length < 1f);
			checa("**NUM PLANETA GERADO, O MESMO**: o pod desce num lugar sorteado do mundo gerado, em terra firme e fora da chegada do gerador (num "
				  + "mundo sem lugar que sirva, na chegada) -- e abre a cratera la",
				  pl.Zone.Equals(zg) && ondeDevia && Quantas(decDoGerado, zg, Protocol.Decal.CrateraDoPod) == 1,
				  $"{g.Nome}: zona {pl.Zone}; ha lugar {haLugar}; a {(chegada is { } c2 ? (pl.Pos - c2).Length : -1):0} px da chegada; "
				  + $"{Quantas(decDoGerado, zg, Protocol.Decal.CrateraDoPod)} cratera(s); mundo em {relogio.Elapsed.TotalSeconds:0.0} s");
		}
		finally
		{
			PousoNoPontoDeSempreDeTeste = false;
			PodSemCrateraDeTeste = false;
			EscutaDeDecalques = null;
			if (NaveDoPiloto(pl) is { } aoLeme)
			{
				if (NaveGrande.EhNaveGrande(aoLeme.Tipo)) PararDePilotar(pl, aoLeme);
				else Desembarcar(pl, aoLeme);
			}
			var zonas = new HashSet<ZoneKey> { terra, zonaDeNamek };
			foreach (Nave f in forjadas) { zonas.Add(f.Zona); _naves.Remove(f); }
			GravarNaves();
			if (!pl.Zone.Equals(zonaAntes)) MoveToZone(pl.Id, zonaAntes, posAntes);
			else pl.Pos = posAntes;
			pl.Voando = false;
			pl.Altitude = 0f;
			RecalcularVelocidade(pl);
			MandarFicha(pl);
			MandarVeiculo(pl);
			foreach (ZoneKey z in zonas) MandarObras(z);
		}
	}

	/// <summary>
	/// A PARTE PURA DA CAMADA 8: a regra do Core (`PousoSorteado`) em mapas de mentira, montados aqui -- um mar com uma ilha, uma ilhota, um patio
	/// murado e uma ilha coberta inteira.
	/// </summary>
	private static void RodarOSorteioEmMapasDeMentira(Action<string, bool, string> checa)
	{
		const int lado = 80;
		bool NaIlha(int x, int y, int de, int ate) => x >= de && x <= ate && y >= de && y <= ate;

		// O MAPA DE MENTIRA: `agua`, `parede` e `dentro` por celula. O `.dentro` so entra pelo arquivo (o cabecalho JCOL), como no disco.
		static ZoneCollision Mapa(int w, int h, Func<int, int, bool> agua, Func<int, int, bool>? parede = null, Func<int, int, bool>? dentro = null)
		{
			int n = (w * h + 7) / 8;
			byte[] Plano(Func<int, int, bool>? f)
			{
				var bits = new byte[n];
				if (f == null) return bits;
				for (int y = 0; y < h; y++)
					for (int x = 0; x < w; x++)
						if (f(x, y)) { int i = y * w + x; bits[i >> 3] |= (byte)(1 << (i & 7)); }
				return bits;
			}
			ZoneCollision m = ZoneCollision.Montar(w, h, Plano(parede));
			m.DefinirAgua(Plano(agua));
			if (dentro != null)
			{
				byte[] arq = new byte[8 + n];
				arq[0] = (byte)'J'; arq[1] = (byte)'C'; arq[2] = (byte)'O'; arq[3] = (byte)'L';
				arq[4] = (byte)(w & 0xFF); arq[5] = (byte)(w >> 8); arq[6] = (byte)(h & 0xFF); arq[7] = (byte)(h >> 8);
				Array.Copy(Plano(dentro), 0, arq, 8, n);
				m.CarregarDentro(arq);
			}
			return m;
		}
		static (int X, int Y) Cel(Vec2 p) => ((int)MathF.Floor(p.X / ZoneCollision.TileSize), (int)MathF.Floor(p.Y / ZoneCollision.TileSize));

		// A ILHA: 40x40 no meio de um mar.
		ZoneCollision ilha = Mapa(lado, lado, (x, y) => !NaIlha(x, y, 20, 59));
		var pontos = new List<Vec2?>();
		for (int s = 0; s < 20; s++) pontos.Add(PousoSorteado.Sortear(ilha, new Random(s), PousoSorteado.FolgaDoPod));
		bool todosNaIlha = pontos.All(p => p is { } v && NaIlha(Cel(v).X, Cel(v).Y, 22, 57)
											&& PousoSorteado.TerraFirme(ilha, Cel(v).X, Cel(v).Y, PousoSorteado.FolgaDoPod));
		checa("(Core) **NUM MAR COM UMA ILHA, O SORTEIO SO CAI NA ILHA**: vinte sementes, vinte pontos em terra firme com a folga do pod inteira "
			  + "(nenhum na praia de um tile)", todosNaIlha,
			  string.Join(" ", pontos.Select(p => p is { } v ? $"({Cel(v).X},{Cel(v).Y})" : "nulo")));
		var cru = new Random(0);
		int noMar = 0;
		for (int i = 0; i < 50; i++)
		{
			int x = cru.Next(2, lado - 2), y = cru.Next(2, lado - 2);
			if (!ilha.BlockedCell(x, y) && ilha.EhAgua(x, y)) noMar++;
		}
		checa("   DEFEITO INJETADO (o sorteio cru, so \"nao e parede\"): ele cai no mar -- a regua de cima reprovaria", noMar > 0,
			  $"{noMar} de 50 no mar");

		ZoneCollision ilhota = Mapa(lado, lado, (x, y) => !NaIlha(x, y, 30, 38));
		checa("(Core) a ILHOTA (9x9 = 81 celulas, menos que a `RegiaoMinima`) nao serve: nulo, e quem chama pousa no ponto de sempre",
			  PousoSorteado.Sortear(ilhota, new Random(1), PousoSorteado.FolgaDoPod) == null, "");

		// O PATIO MURADO: terra firme pra todo lado, e um quadrado de 10x10 cercado de muro no meio -- chao, mas um bolsao.
		ZoneCollision patio = Mapa(lado, lado, (_, _) => false,
			parede: (x, y) => (x == 30 || x == 41) && y >= 30 && y <= 41 || (y == 30 || y == 41) && x >= 30 && x <= 41);
		checa("(Core) o PATIO MURADO (10x10 de chao cercado de muro) e um bolsao: dali a pe nao se chega a lugar nenhum -- e o lado de fora serve",
			  !PousoSorteado.LigadaAoMundo(patio, 35, 35) && PousoSorteado.LigadaAoMundo(patio, 10, 10), "");

		ZoneCollision coberta = Mapa(lado, lado, (x, y) => !NaIlha(x, y, 20, 59), dentro: (x, y) => NaIlha(x, y, 20, 59));
		checa("(Core) a ilha COBERTA INTEIRA (o salao de um predio, a estacao espacial) nao serve: nulo -- nada pousa sob teto",
			  PousoSorteado.Sortear(coberta, new Random(2), PousoSorteado.FolgaDoPod) == null, "");

		var gente = new List<Vec2> { ilha.CentroDaCelula(40, 40), ilha.CentroDaCelula(30, 30), ilha.CentroDaCelula(50, 50) };
		bool longe = true;
		for (int s = 0; s < 30; s++)
			if (PousoSorteado.Sortear(ilha, new Random(100 + s), PousoSorteado.FolgaDoPod, gente) is { } p)
				longe &= gente.All(c => (c - p).Length >= PousoSorteado.LongeDeGente);
		checa("(Core) o ponto fica LONGE DE GENTE: trinta sorteios com tres corpos na ilha, nenhum a menos de tres tiles de alguem", longe, "");

		checa("(Core) a mesma semente da o MESMO ponto (a funcao e pura: a sorte vem de fora)",
			  PousoSorteado.Sortear(ilha, new Random(123), PousoSorteado.FolgaDaNaveGrande) is { } a1
			  && PousoSorteado.Sortear(ilha, new Random(123), PousoSorteado.FolgaDaNaveGrande) is { } a2 && (a1 - a2).Length < 0.01f, "");
	}
}
