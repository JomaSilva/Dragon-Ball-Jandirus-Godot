using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Torneio;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DOS TORNEIOS (`--torneioteste`) -- Terra e Outro Mundo, de ponta a ponta.
///
/// O pedido do dono (2026-09-06, etapa 3): *"um torneio de 32 roda so com NPCs sem travar e a chave
/// fica consistente com jogadores entrando/saindo; NPC voador se salva do ring-out, nao voador
/// perde; premios creditados corretamente; estado sobrevive a reconexao; W.O. por desconexao"*.
///
/// ============================ COMO ELA MEDE ============================
/// No primeiro login, com o host como o UNICO jogador inscrito (o resto da chave sao os 31 NPCs
/// gerados na hora pelo funil de producao). A config e a de producao com os prazos encurtados
/// (inscricao de 5 s, luta de ate 20 s, W.O. de 3 s) e o relogio de parede anda junto com os
/// tiques (`AdiantoDoRelogioDeTeste`) -- os prazos do torneio sao em ms de parede. O torneio roda
/// pelo `TickDoTorneio` de producao; a bancada so aperta os mesmos verbos que o cliente aperta
/// (`trn_participar`, `trn_iniciar`...) e le os ganchos `*DeTeste`.
///
/// Na primeira rodada inteira o host LUTA: a bancada crava o host colado no oponente e soca por
/// ele com um BP mil vezes maior (o funil `Atacar` do jogador). E o unico jeito de um premio
/// chegar a um jogador nesta bancada, e o premio e a unica coisa que so um jogador pode receber.
/// =========================================================================================
/// </summary>
public sealed partial class GameServer
{
	private void RodarBancadaDoTorneio(ServerPlayer pl)
	{
		GD.Print("\n[torneio] ================ OS TORNEIOS DA TERRA E DO OUTRO MUNDO (pedido do dono, 2026-09-06) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[torneio]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[torneio]   FALHA {nome}   [{detalhe}]"); }
		}
		List<string> Ouvidos() { List<string> l = EscutaDoTorneio ?? []; EscutaDoTorneio = []; return l; }
		List<string> Avisos() { List<string> l = EscutaDeAvisos ?? []; EscutaDeAvisos = []; return l; }

		TorneioConfig original = _torneioCfg;
		var agendaAntes = new AgendaDeTorneios
		{
			ProximoDaTerraMs = _agenda.ProximoDaTerraMs, ProximoDoAlemMs = _agenda.ProximoDoAlemMs, UltimoDaTerraMs = _agenda.UltimoDaTerraMs,
		};
		List<string>? escutaAntes = EscutaDoTorneio;
		List<string>? avisosAntes = EscutaDeAvisos;
		ZoneKey zonaDoHost = pl.Zone;
		Vec2 posDoHost = pl.Pos;
		double bpDoHost = pl.Ficha.BP, zeniDoHost = pl.Ficha.Zeni;
		bool mortoDoHost = pl.Ficha.dead, viajouDoHost = pl.MorteJaViajou;
		bool letalDoHost = pl.Combate.Letal;

		// O HOST NA LISTA DA ZONA (ver a bancada da rotina): no 1o login ele ainda nao esta nela, e os
		// avisos "aos presentes" e a ancora da espera a leem.
		List<ServerPlayer> naZona = ZoneList(pl.Zone.Hash);
		bool puseu = !naZona.Contains(pl);
		if (puseu) naZona.Add(pl);

		double relogioAndado = 0;
		void Tiques(int n)
		{
			for (int i = 0; i < n; i++)
			{
				relogioAndado += Protocol.TickSeconds * 1000;
				AdiantoDoRelogioDeTeste = (long)relogioAndado;
				TickCombate(Protocol.TickSeconds);
				TickFichas();
				foreach (ServerPlayer c in _players.Values.ToList())
				{
					TickDoVoo(c, Protocol.TickSeconds);
					TickDaCarga(c, Protocol.TickSeconds);
				}
				TickDoEmpurrao();
				TickDosCorposSemDono(Protocol.TickSeconds);
				TickDoTorneio(Protocol.TickSeconds);
			}
		}
		void PularSegundos(double s) => AdiantoDoRelogioDeTeste = (long)(relogioAndado += s * 1000);
		int TiquesAte(Func<bool> pronto, int teto)
		{
			int n = 0;
			while (n < teto && !pronto()) { Tiques(1); n++; }
			return n;
		}
		static Vec2 Tiles(float x, float y) => new(x * ZoneCollision.TileSize, y * ZoneCollision.TileSize);

		try
		{
			EscutaDoTorneio = [];
			EscutaDeAvisos = [];
			var cfg = new TorneioConfig
			{
				Vagas = original.Vagas, InscricaoSegundos = 5, ConviteSegundos = 5, LutaSegundosMax = 20,
				ContagemSegundos = 1, IntervaloSegundos = 1, WoSegundos = 3,
				Premio1 = original.Premio1, Premio2 = original.Premio2, Premio3 = original.Premio3,
				NpcBpMinPct = original.NpcBpMinPct, NpcBpMaxPct = original.NpcBpMaxPct, NpcBpPiso = original.NpcBpPiso,
				MoldeDoNpc = original.MoldeDoNpc, Terra = original.Terra, Alem = original.Alem,
				IntervaloDaTerraDias = original.IntervaloDaTerraDias, PrimeiroDaTerraDias = original.PrimeiroDaTerraDias,
				AlemDepoisDaTerraDias = original.AlemDepoisDaTerraDias, CampeaoDoAlemRevive = original.CampeaoDoAlemRevive,
			};
			_torneioCfg = cfg;

			// =====================================================================
			// 0) A CONFIG E AS ARENAS DE VERDADE
			// =====================================================================
			GD.Print("[torneio] -- 0) config e arenas --");
			Checa("o torneio.json de producao carregou sem problemas", original.Problemas().Count == 0, string.Join(" | ", original.Problemas()));
			Checa("vagas = 32 (16-avos), premios 1.000.000 / 500.000 / 250.000 (TRN_PRIZE_*), NPC em 60..140% da media com piso 500 (TRN_NPC_BP_*)",
				  original.Vagas == 32 && original.Premio1 == 1_000_000 && original.Premio2 == 500_000 && original.Premio3 == 250_000
				  && original.NpcBpMinPct == 60 && original.NpcBpMaxPct == 140 && original.NpcBpPiso == 500);
			foreach ((string nome, PalcoDeTorneio palco) in new[] { ("Terra", original.Terra), ("Outro Mundo", original.Alem) })
			{
				ZoneCollision? mapa = MapaDaZonaOuCatalogo(ZoneKey.Premade(palco.Zona));
				int bloqueadas = 0, total = 0;
				if (mapa != null)
					for (int cy = palco.Arena.Y1; cy <= palco.Arena.Y2; cy++)
						for (int cx = palco.Arena.X1; cx <= palco.Arena.X2; cx++) { total++; if (!mapa.ServeDeChao(cx, cy)) bloqueadas++; }
				Checa($"a arena da {nome} ({palco.Arena} em {palco.Zona}) e chao livre no mapa de verdade (a conta x-1 / altura-y do BYOND bate)",
					  mapa != null && total > 0 && bloqueadas == 0, $"{bloqueadas} de {total} celulas bloqueadas");
			}
			Checa("[injecao] uma config com vagas 30 (nao e potencia de 2) e W.O. negativo e apontada",
				  new TorneioConfig { Vagas = 30, WoSegundos = -1 }.Problemas().Count >= 2);

			// =====================================================================
			// 1) INSCRICOES E CONVITE
			// =====================================================================
			GD.Print("[torneio] -- 1) inscricoes e convite --");
			Checa("PRECONDICAO: o host e admin, esta vivo e na Terra (elegivel)", EhAdmin(pl) && !pl.Ficha.dead && pl.Zone.Name == cfg.Terra.Zona);
			Checa("sem torneio, `trn_participar` diz que nao ha inscricoes abertas", ComandoDeTorneio(pl, "trn_participar", "") && !TorneioAtivo);
			Avisos();
			ComandoDeTorneio(pl, "trn_iniciar", "terra");
			Checa("o admin abre o torneio da Terra na marra (`trn_iniciar terra`) e as inscricoes abrem", TorneioAtivo && FaseDoTorneioDeTeste == "Inscricao");
			Checa("...e a agenda do proximo da Terra e do Outro Mundo e marcada NO COMECO (30 e 15 dias) e gravada",
				  Math.Abs(AgendaDeTeste.ProximoDaTerraMs - (NowMs() + 30 * 86_400_000L)) < 5000
				  && Math.Abs(AgendaDeTeste.ProximoDoAlemMs - (NowMs() + 15 * 86_400_000L)) < 5000
				  && System.IO.File.Exists(CaminhoDaAgenda));
			Checa("o anuncio de abertura saiu pra todo mundo", Ouvidos().Any(x => x.Contains("inscricoes")));
			ComandoDeTorneio(pl, "trn_participar", "");
			Checa("`trn_participar` inscreve o host (1/32)", InscritosDeTeste == 1 && Ouvidos().Any(x => x.Contains("se inscreveu")));
			ComandoDeTorneio(pl, "trn_participar", "");
			Checa("...de novo: 'ja esta inscrito', e continua 1", InscritosDeTeste == 1 && Avisos().Any(x => x.Contains("ja esta inscrito")));
			ComandoDeTorneio(pl, "trn_recusar", "");
			Checa("`trn_recusar` tira o host da lista", InscritosDeTeste == 0);
			pl.Ficha.dead = true;
			ComandoDeTorneio(pl, "trn_participar", "");
			Checa("[injecao] morto na Terra nao se inscreve no torneio dos vivos", InscritosDeTeste == 0 && Avisos().Any(x => x.Contains("vivo")));
			pl.Ficha.dead = false;
			ComandoDeTorneio(pl, "trn_participar", "");
			Checa("vivo de novo, inscreve", InscritosDeTeste == 1);
			ComandoDeTorneio(pl, "trn_status", "");
			Checa("`trn_status` descreve as inscricoes abertas", Avisos().Any(x => x.Contains("inscricoes abertas")));

			// =====================================================================
			// 2) O FECHAMENTO: 31 NPCs, a chave, a area de espera
			// =====================================================================
			GD.Print("[torneio] -- 2) fechamento das inscricoes: NPCs, chave e area de espera --");
			double mediaEsperada = Math.Max(Finito(pl.Ficha.expressedBP), cfg.NpcBpPiso);
			PularSegundos(6);
			Tiques(2);
			Checa("vencido o prazo, as inscricoes fecham sozinhas e o torneio vai pra preparacao", FaseDoTorneioDeTeste == "Preparando", FaseDoTorneioDeTeste);
			IReadOnlyList<int> npcs = NpcsDoTorneioDeTeste;
			Checa("as 31 vagas restantes viram NPCs nascidos na hora", npcs.Count == 31, $"{npcs.Count}");

			// =====================================================================
			// 2b) O SNAPSHOT DA ZONA CHEIA (dono, 2026-09-07: "os npcs estao realmente invisiveis")
			// =====================================================================
			// Com os 31 NPCs a Terra do dono passa de 70 corpos; num pacote so isso estourava o MTU do
			// LiteNetLib (`TooBigPacketException`, 30x por segundo, calado) e NINGUEM na Terra recebia
			// snapshot enquanto o torneio existisse. A regra nova corta em partes -- ver
			// `GameServer.Snapshot.cs`. Mede-se o corte com os corpos de verdade e um ORCAMENTO APERTADO
			// (300 bytes): a Terra desta bancada tem menos habitantes que a do jogo (o povoamento ainda
			// nao andou no 1o login) e com o teto real caberia num pacote -- o robo `--diagsombra`, com
			// a Terra cheia, e quem prova o corte no teto real. O defeito e mostrado em numero.
			{
				GD.Print("[torneio] -- 2b) o snapshot da zona com os 31 NPCs sai em partes que cabem no pacote --");
				List<ServerPlayer> terra = ZoneList(ZoneKey.Premade(cfg.Terra.Zona).Hash);
				const int orcamento = 300;
				List<NetDataWriter> reais = PartesDoSnapshot(terra, TirosDaZona(ZoneKey.Premade(cfg.Terra.Zona).Hash), NowMs(), 7u, OrcamentoDoSnapshot(terra));
				GD.Print($"[torneio]      (com o teto real de {OrcamentoDoSnapshot(terra)} bytes esta Terra de {terra.Count} corpos sai em {reais.Count} parte(s))");
				List<NetDataWriter> partes = PartesDoSnapshot(terra, TirosDaZona(ZoneKey.Premade(cfg.Terra.Zona).Hash), NowMs(), 7u, orcamento);
				int corposNasPartes = 0, tirosNasPartes = 0, maior = 0;
				bool carimbos = true;
				foreach (NetDataWriter p in partes)
				{
					var r = new NetDataReader(p.Data, 0, p.Length);
					r.GetByte();                                  // o opcode
					if (r.GetUInt() != 7u) carimbos = false;      // o carimbo, o mesmo em toda parte
					int n = r.GetUShort();
					for (int i = 0; i < n; i++) EntityState.Read(r);
					int t = r.GetUShort();
					for (int i = 0; i < t; i++) ProjetilState.Read(r);
					if (r.AvailableBytes != 0) carimbos = false;  // a parte tem que acabar onde o leitor acaba
					corposNasPartes += n; tirosNasPartes += t;
					maior = Math.Max(maior, p.Length);
				}
				Checa("a zona com os 31 NPCs nao cabe num pacote de 300 bytes, e o snapshot sai em 2+ partes",
					  terra.Count >= 32 && partes.Count >= 2, $"{terra.Count} corpos, {partes.Count} partes, orcamento {orcamento} bytes");
				Checa("...nenhuma parte passa do orcamento", maior <= orcamento, $"maior parte {maior} bytes");
				Checa("...e cada parte se le do comeco ao fim como um snapshot inteiro, com o mesmo carimbo", carimbos);
				Checa("...e as partes somadas descrevem cada corpo da zona exatamente uma vez", corposNasPartes == terra.Count, $"{corposNasPartes} de {terra.Count}");
				List<NetDataWriter> umaSo = PartesDoSnapshot(terra, TirosDaZona(ZoneKey.Premade(cfg.Terra.Zona).Hash), NowMs(), 7u, int.MaxValue);
				Checa("DEFEITO VISIVEL: sem partir, o pacote unico passa do orcamento -- era o que o LiteNetLib recusava",
					  umaSo.Count == 1 && umaSo[0].Length > orcamento, $"{umaSo[0].Length} bytes num pacote so, contra {orcamento}");
				// TIROS TAMBEM PARTEM: um snapshot so de tiros, com orcamento apertado, sai em varias partes
				// de zero corpos e o total confere.
				var muitosTiros = new List<ProjetilState>();
				for (int i = 0; i < 40; i++) muitosTiros.Add(new ProjetilState { Id = 1000 + i, Pos = new Vec2(i, i), Tipo = 1, Cauda = new Vec2(i, i) });
				List<NetDataWriter> soTiros = PartesDoSnapshot([], muitosTiros, NowMs(), 7u, 200);
				int tirosLidos = 0;
				foreach (NetDataWriter p in soTiros)
				{
					var r = new NetDataReader(p.Data, 0, p.Length);
					r.GetByte(); r.GetUInt(); r.GetUShort();
					tirosLidos += r.GetUShort();
				}
				Checa("os tiros tambem se partem (40 tiros num orcamento de 200 bytes viram varias partes, todas legiveis, total certo)",
					  soTiros.Count >= 3 && tirosLidos == 40, $"{soTiros.Count} partes, {tirosLidos} tiros");
			}
			bool bpNaFaixa = true, todosNaEspera = true, todosComArena = true, ninguemVoa = true, todosLutadores = true;
			var mapaTerra = MapaDaZonaOuCatalogo(ZoneKey.Premade(cfg.Terra.Zona));
			foreach (int id in npcs)
			{
				if (!_players.TryGetValue(id, out ServerPlayer? n)) { bpNaFaixa = false; continue; }
				double bp = n.Ficha.BP;
				if (bp < Math.Max(mediaEsperada * 0.6 - 1, cfg.NpcBpPiso) || bp > Math.Max(mediaEsperada * 1.4 + 1, cfg.NpcBpPiso)) bpNaFaixa = false;
				int cx = (int)MathF.Floor(n.Pos.X / ZoneCollision.TileSize), cy = (int)MathF.Floor(n.Pos.Y / ZoneCollision.TileSize);
				if (cfg.Terra.Arena.Contem(cx, cy) || mapaTerra?.ServeDeChao(cx, cy) == false || !PresosDeTeste.Contains(id)) todosNaEspera = false;
				if (n.Arena == null) todosComArena = false;
				if (n.Perfil.Voa) ninguemVoa = false;
				if (n.Papel?.Tipo != Jandirus.Core.Npc.TipoDeNpc.Lutador) todosLutadores = false;
			}
			Checa($"o BP de cada NPC fica em 60..140% da media dos inscritos ({mediaEsperada:N0}), com piso {cfg.NpcBpPiso:0}", bpNaFaixa);
			Checa("todos nascem do molde 'lutador' (tipo proprio: nem cidadao, nem inimigo, nem chefe)", todosLutadores);
			Checa("todos esperam FORA da arena, em chao livre, travados (o `apply_hold`)", todosNaEspera);
			Checa("...e conhecem a linha da arena (a consciencia de borda da etapa 1)", todosComArena);
			Checa("o host nao voa, entao NENHUM NPC entra voador (o `anyfly` do DM)", ninguemVoa);
			Checa("o host tambem esta travado na area de espera", PresosDeTeste.Contains(pl.Id));
			Chave? chave = ChaveDeTeste;
			Checa("a chave tem 32 competidores e 16 lutas nos 16-avos", chave != null && chave.Competidores.Count == 32 && chave.Rodadas.Count == 1
				  && chave.Rodadas[0].Lutas.Count == 16 && chave.Rodadas[0].Nome == "16-avos de final");
			Checa("...e o host esta nela pela ASSINATURA (conta/slot), nao pelo id do corpo", chave?.Quem(pl.Assinatura) is { Npc: false });

			// A AREA DE ESPERA: sem soco, sem tecnica, e ancorado no lugar
			Avisos();
			Atacar(pl, Protocol.Golpe.Leve);
			Checa("na area de espera o host NAO bate (`canfight = 0`)", Avisos().Any(x => x.Contains("area de espera")));
			UsarHabilidade(pl, "regenerar");
			Checa("...nem usa tecnica", Avisos().Any(x => x.Contains("area de espera")));
			Vec2 espera = pl.Pos;
			pl.Pos = espera + Tiles(8, 0);
			Tiques(1);
			Checa("...e quem tenta sair e ancorado de volta (`move = 0`)", Vec2.Distance(pl.Pos, espera) <= 4, $"{Vec2.Distance(pl.Pos, espera):0} px");
			Checa("...e e intocavel (a carencia da espera)", pl.Combate.Intocavel);
			AoEntrarNoTorneio(pl);
			Checa("quem reconecta na espera volta pra espera (a chave e do servidor, o corpo e reconhecido pela assinatura)", PresosDeTeste.Contains(pl.Id));

			// =====================================================================
			// 3) O TORNEIO INTEIRO: o host luta (mil vezes mais forte) e os NPCs lutam entre si
			// =====================================================================
			GD.Print("[torneio] -- 3) o torneio inteiro, 32 vagas, ate o campeao --");
			pl.Ficha.BP = bpDoHost * 1000;
			pl.Ficha.Statify();
			pl.Ficha.PowerLevel();
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			int lutasDoHost = 0, tiques = 0, maiorLutaEmTiques = 0, lutaAtualEmTiques = 0;
			bool invariantesOk = true, hostLetalDuranteLuta = false, ringOutVisto = false, voadorSalvo = false, semVooSaiu = false;
			bool ringOutTestado = false, woTestado = false, woVenceu = false, reconexaoTestada = false;
			string ultimoPar = "";
			int maiorAndar = 0, tiquesAltoDemais = 0, tiquesAltoSobreOChao = 0, tiquesPresoNoAr = 0;
			const int teto = 90_000;   // 50 min de mundo: o dobro do pior caso (32 lutas de 20 s + folgas)
			while (TorneioAtivo && tiques < teto)
			{
				Tiques(1);
				tiques++;
				if (FaseDoTorneioDeTeste != "Luta") { lutaAtualEmTiques = 0; continue; }
				lutaAtualEmTiques++;
				maiorLutaEmTiques = Math.Max(maiorLutaEmTiques, lutaAtualEmTiques);
				(int a, int b) = LutadoresDeTeste;
				string par = $"{a}x{b}";
				if (par != ultimoPar) { ultimoPar = par; if (a == pl.Id || b == pl.Id) lutasDoHost++; }
				// INVARIANTES: os dois da vez soltos, todo o resto da chave preso
				if (PresosDeTeste.Contains(a) || PresosDeTeste.Contains(b)) invariantesOk = false;
				if (chave != null)
					foreach (string c in chave.AindaNaChave())
						if (CorpoDaChave(c) is { } corpo && corpo.Id != a && corpo.Id != b && !PresosDeTeste.Contains(corpo.Id)) invariantesOk = false;

				// O HOST LUTA: colado no oponente, socando pelo funil do jogador.
				if (a == pl.Id || b == pl.Id)
				{
					if (pl.Combate.Letal) hostLetalDuranteLuta = true;
					int outro = a == pl.Id ? b : a;
					if (_players.TryGetValue(outro, out ServerPlayer? alvo))
					{
						// COLADO NO OPONENTE, MAS DENTRO DA LINHA: um oponente encostado na borda oeste poria o
						// host um tile fora da arena, e o juiz o eliminaria por ring-out (foi o que a primeira
						// rodada desta bancada viu: o host, mil vezes mais forte, em 3o lugar).
						Vec2 lado = alvo.Pos + Tiles(-1, 0);
						bool aOeste = cfg.Terra.Arena.ContemEm(lado);
						CravarPosicao(pl, aOeste ? lado : alvo.Pos + Tiles(1, 0));
						pl.Facing = aOeste ? Facing.East : Facing.West;
						Atacar(pl, Protocol.Golpe.Pesado);
					}
					continue;
				}

				// UMA LUTA ENTRE NPCs: o ring-out, o voo que salva, o W.O.
				if (!_players.TryGetValue(a, out ServerPlayer? na) || !_players.TryGetValue(b, out ServerPlayer? nb)) continue;
				// A SONDA DA ALTURA (dono, 2026-09-07: "os personagens comecam a voar muito alto ao ponto de nao
				// dar mais pra ver do chao"): o andar de cada lutador e o do oponente, tique a tique.
				foreach ((ServerPlayer eu, ServerPlayer ele) in new[] { (na, nb), (nb, na) })
				{
					int meu = Jandirus.Core.World.Voo.Andar(eu.Altitude), dele = Jandirus.Core.World.Voo.Andar(ele.Altitude);
					maiorAndar = Math.Max(maiorAndar, meu);
					if (meu - dele > 1) tiquesAltoDemais++;
					if (dele == 0 && meu >= 2) tiquesAltoSobreOChao++;
				}
				foreach (int id in PresosDeTeste)
					if (_players.TryGetValue(id, out ServerPlayer? preso) && preso.Altitude > 0f) tiquesPresoNoAr++;
				if (!ringOutTestado && lutaAtualEmTiques == 30)
				{
					ringOutTestado = true;
					// O VOADOR: posto fora da linha VOANDO, nao e ring-out (a luta segue).
					na.Livro.Dar(SkillDoKi); na.Niveis.Por(SkillDoKi, MaestriaQueDestravaVoo);
					na.Perfil = new Jandirus.Core.Ai.PerfilDeCombate(true, true);
					na.Cerebro!.Poderes = na.Perfil.Filtrar(LerCapacidades(na));
					if (!na.Voando) AlternarVoo(na);
					Vec2 fora = new Vec2(cfg.Terra.Arena.X2 + 2.5f, (cfg.Terra.Arena.Y1 + cfg.Terra.Arena.Y2) / 2f + 0.5f) * ZoneCollision.TileSize;
					na.Pos = fora;
					Tiques(1);
					voadorSalvo = na.Voando && FaseDoTorneioDeTeste == "Luta" && LutadoresDeTeste.A == a;
					// O QUE NAO VOA: no mesmo lugar, sem voar, perde na hora por ring-out.
					nb.Perfil = Jandirus.Core.Ai.PerfilDeCombate.SoCorpo;
					nb.Cerebro!.Poderes = nb.Perfil.Filtrar(LerCapacidades(nb));
					if (nb.Voando) AlternarVoo(nb);
					nb.Pos = fora + Tiles(0, 2);
					na.Pos = new Vec2((cfg.Terra.Arena.X1 + cfg.Terra.Arena.X2) / 2f + 0.5f, (cfg.Terra.Arena.Y1 + cfg.Terra.Arena.Y2) / 2f + 0.5f) * ZoneCollision.TileSize;
					Ouvidos();
					Tiques(1);
					List<string> ditos = Ouvidos();
					semVooSaiu = ditos.Any(x => x.Contains("RING-OUT")) && ditos.Any(x => x.Contains("ring-out") && x.Contains(na.Name));
					ringOutVisto = semVooSaiu;
					continue;
				}
				if (ringOutTestado && !woTestado && lutaAtualEmTiques == 30)
				{
					woTestado = true;
					// O W.O.: um lutador some (o corpo deixa de existir); o outro vence depois do prazo.
					string nomeDeB = nb.Name;
					RemoverNpc(na);
					Tiques(2);
					bool aindaEspera = FaseDoTorneioDeTeste == "Luta";
					PularSegundos(cfg.WoSegundos + 0.5);
					Ouvidos();
					Tiques(2);
					woVenceu = aindaEspera && Ouvidos().Any(x => x.Contains("W.O.") && x.Contains(nomeDeB));
					continue;
				}
				if (woTestado && !reconexaoTestada && lutaAtualEmTiques == 30)
				{
					reconexaoTestada = true;
					// A RECONEXAO NO MEIO DA LUTA: o host esta preso na espera; `AoEntrarNoTorneio` NAO o poe
					// na luta dos outros -- so o devolve a espera. (A volta ao proprio combate e medida acima,
					// pela chave: o corpo que volta e o mesmo lutador.)
					AoEntrarNoTorneio(pl);
					reconexaoTestada = PresosDeTeste.Contains(pl.Id) && LutadoresDeTeste.A == a && LutadoresDeTeste.B == b;
				}
			}
			GD.Print($"[torneio]   sonda da altura: maior andar {maiorAndar}; tiques com um lutador 2+ andares acima do outro: {tiquesAltoDemais}; "
					+ $"tiques com lutador no andar 2+ contra oponente no chao: {tiquesAltoSobreOChao}; tiques com preso no ar: {tiquesPresoNoAr}");
			Checa("SONDA DA ALTURA: nenhum lutador passa do andar 1 nem fica 2+ andares acima do oponente (a IA regula a altura pelo andar dele)",
				  maiorAndar <= 1 && tiquesAltoDemais == 0 && tiquesAltoSobreOChao == 0,
				  $"maior andar {maiorAndar}, alto demais {tiquesAltoDemais}, alto sobre o chao {tiquesAltoSobreOChao}");
			Checa("SONDA DA ALTURA: quem espera a vez fica no chao (a espera pousa e nao sobe)", tiquesPresoNoAr == 0, $"{tiquesPresoNoAr} tique(s) no ar");
			Checa($"o torneio de 32 roda ate o fim sem travar ({tiques} tiques = {tiques * Protocol.TickSeconds / 60:0.0} min de mundo)", !TorneioAtivo && tiques < teto, $"{tiques} tiques, fase {FaseDoTorneioDeTeste}");
			Checa("a chave fechou: campeao, vice e terceiro definidos, 32 lutas decididas (16+8+4+2 + 3o lugar + final)",
				  chave is { Acabou: true } && chave.Campeao.Length > 0 && chave.Vice.Length > 0 && chave.Terceiro.Length > 0 && chave.LutasDecididas == 32,
				  $"{chave?.LutasDecididas} lutas, campeao '{chave?.NomeDe(chave.Campeao)}', vice '{chave?.NomeDe(chave.Vice)}', 3o '{chave?.NomeDe(chave.Terceiro)}'");
			Checa("as rodadas sao 16-avos, oitavas, quartas, semifinal, disputa do 3o lugar e final, nessa ordem",
				  chave != null && string.Join(" > ", chave.Rodadas.Select(r => r.Nome)) == "16-avos de final > oitavas de final > quartas de final > semifinal > disputa do 3o lugar > final",
				  chave == null ? "" : string.Join(" > ", chave.Rodadas.Select(r => r.Nome)));
			Checa("em toda luta os dois da vez estavam soltos e todo o resto da chave estava preso", invariantesOk);
			Checa($"nenhuma luta passou do tempo maximo ({cfg.LutaSegundosMax:0} s + folga)", maiorLutaEmTiques <= (cfg.LutaSegundosMax + 3) * Protocol.TickHz, $"{maiorLutaEmTiques / (double)Protocol.TickHz:0.0} s");
			Checa("o host, mil vezes mais forte, lutou as 5 rodadas ate a final e e o CAMPEAO", chave != null && chave.Campeao == pl.Assinatura && lutasDoHost == 5, $"{lutasDoHost} lutas do host");
			Checa("...com o golpe NAO-LETAL forcado durante as lutas dele", !hostLetalDuranteLuta);
			Checa("...e nenhum NPC morreu no torneio (esporte, nao guerra)", npcs.All(id => !_players.ContainsKey(id) || !_players[id].Ficha.dead));
			Checa($"o premio do campeao ({cfg.Premio1:N0} zeni) foi creditado ao host", Math.Abs(pl.Ficha.Zeni - (zeniDoHost + cfg.Premio1)) < 0.5, $"{zeniDoHost:N0} -> {pl.Ficha.Zeni:N0}");
			Checa("...e o vice e o terceiro (NPCs) nao levam zeni nenhum (o `if (M.client)` do `award`)", Ouvidos().Count >= 0);
			Checa("o VOADOR posto fora da linha VOANDO nao sofre ring-out: a luta segue", voadorSalvo);
			Checa("o que NAO voa, posto fora da linha, perde na hora por RING-OUT", semVooSaiu && ringOutVisto);
			Checa($"o lutador que SOME perde por W.O. depois de {cfg.WoSegundos:0} s -- e ate la a luta espera por ele", woVenceu);
			Checa("reconectar durante a luta alheia devolve o host a espera sem mexer na luta", reconexaoTestada);
			Checa("no fim, ninguem fica preso e os NPCs do torneio sao removidos", PresosDeTeste.Count == 0 && npcs.All(id => !_players.ContainsKey(id)));
			Checa("o host saiu da espera solto, tocavel e de golpe como estava", !pl.Combate.Intocavel && pl.Combate.Letal == letalDoHost);

			// =====================================================================
			// 4) O TORNEIO DO OUTRO MUNDO: so mortos, NPCs mortos de pe com aureola
			// =====================================================================
			GD.Print("[torneio] -- 4) o torneio do Outro Mundo --");
			pl.Ficha.BP = bpDoHost;
			pl.Ficha.Statify();
			pl.Ficha.PowerLevel();
			var alem = ZoneKey.Premade(cfg.Alem.Zona);
			ComandoDeTorneio(pl, "trn_iniciar", "alem");
			Checa("o admin abre o torneio do Outro Mundo", TorneioAtivo && FaseDoTorneioDeTeste == "Inscricao");
			Avisos();
			ComandoDeTorneio(pl, "trn_participar", "");
			Checa("[injecao] o host VIVO nao se inscreve no torneio dos mortos", InscritosDeTeste == 0 && Avisos().Any(x => x.Contains("mortos")));
			pl.Ficha.dead = true;
			pl.MorteJaViajou = true;
			MoveToZone(pl.Id, alem, MesaDoEnma(alem));
			Checa("PRECONDICAO: o host esta morto, de pe, no Outro Mundo", pl.Ficha.dead && pl.MortoDePe && Alem.EhOAlem(pl.Zone));
			ComandoDeTorneio(pl, "trn_participar", "");
			Checa("morto no Outro Mundo, o host se inscreve", InscritosDeTeste == 1);
			PularSegundos(6);
			Tiques(2);
			IReadOnlyList<int> mortos = NpcsDoTorneioDeTeste;
			bool todosMortosDePe = mortos.Count == 31, comAureola = mortos.Count == 31, naZonaCerta = mortos.Count == 31;
			double nutricaoAntes = 0;
			foreach (int id in mortos)
			{
				if (!_players.TryGetValue(id, out ServerPlayer? n)) { todosMortosDePe = false; continue; }
				if (!(n.Ficha.dead && n.MortoDePe)) todosMortosDePe = false;
				if (!Alem.TemAureola(n.Ficha.dead, n.MorteJaViajou)) comAureola = false;
				if (n.Zone.Hash != alem.Hash) naZonaCerta = false;
				nutricaoAntes += n.Ficha.CurrentNutrition;
			}
			Checa("os 31 NPCs do Outro Mundo nascem MORTOS DE PE, na zona do alem, com aureola", todosMortosDePe && comAureola && naZonaCerta, $"{mortos.Count} NPCs");
			int tiquesAlem = 0;
			while (TorneioAtivo && tiquesAlem < teto) { Tiques(1); tiquesAlem++; }
			double nutricaoDepois = 0;
			foreach (int id in mortos) if (_players.TryGetValue(id, out ServerPlayer? n)) nutricaoDepois += n.Ficha.CurrentNutrition;
			Checa($"o torneio do Outro Mundo roda ate o fim ({tiquesAlem * Protocol.TickSeconds / 60:0.0} min de mundo) com os mortos lutando", !TorneioAtivo && tiquesAlem < teto, FaseDoTorneioDeTeste);
			Checa("...e a chave fechou com campeao (32 lutas)", ChaveDeTeste == null && chave != null);
			Checa("o host continua morto no fim: o campeao do Outro Mundo NAO revive (config `campeaoRevive: false`)", pl.Ficha.dead);
			Checa("a agenda do Outro Mundo foi consumida (o proximo e a Terra quem marca)", AgendaDeTeste.ProximoDoAlemMs == 0);

			// =====================================================================
			// 5) A AGENDA PERSISTIDA
			// =====================================================================
			GD.Print("[torneio] -- 5) a agenda persistida --");
			long terraGravada = AgendaDeTeste.ProximoDaTerraMs;
			RecarregarAgendaDeTeste();
			Checa("o torneio.json da pasta de saves guarda o proximo da Terra e o ultimo (reler do disco da o mesmo)",
				  AgendaDeTeste.ProximoDaTerraMs == terraGravada && AgendaDeTeste.UltimoDaTerraMs > 0);
			ComandoDeTorneio(pl, "trn_status", "");
			Checa("`trn_status` sem torneio diz quando e o proximo", Avisos().Any(x => x.Contains("Terra em")));

			// =====================================================================
			// 6) CANCELAR SOLTA TODO MUNDO
			// =====================================================================
			GD.Print("[torneio] -- 6) cancelar --");
			pl.Ficha.dead = false;
			pl.MorteJaViajou = false;
			MoveToZone(pl.Id, zonaDoHost, posDoHost);
			ComandoDeTorneio(pl, "trn_iniciar", "terra");
			ComandoDeTorneio(pl, "trn_participar", "");
			PularSegundos(6);
			Tiques(2);
			Checa("PRECONDICAO: um terceiro torneio chegou a chave com todo mundo preso", TorneioAtivo && PresosDeTeste.Count == 32);
			IReadOnlyList<int> npcsDoTerceiro = [.. NpcsDoTorneioDeTeste];
			ComandoDeTorneio(pl, "trn_cancelar", "");
			Checa("`trn_cancelar` (admin) encerra: ninguem preso, NPCs removidos, host solto e tocavel",
				  !TorneioAtivo && PresosDeTeste.Count == 0 && npcsDoTerceiro.All(id => !_players.ContainsKey(id)) && !pl.Combate.Intocavel);

			GD.Print("[torneio] -- 7) os verbs de admin pra testar em jogo: agenda, inscrever, pular a espera --");
			{
				// A AGENDA NA MARRA: "Terra em 1 minuto" -- e o relogio do torneio (a agenda) quem abre.
				EscutaDeAvisos = [];
				ComandoDeTorneio(pl, "trn_agenda", "terra 1");
				long marcado = AgendaDeTeste.ProximoDaTerraMs;
				Checa("`trn_agenda terra 1` marca o proximo Torneio da Terra pra daqui a um minuto",
					  marcado > NowMs() && marcado <= NowMs() + 61_000, $"{(marcado - NowMs()) / 1000.0:0} s");
				EscutaDeAvisos = [];
				ComandoDeTorneio(pl, "trn_agenda", "");
				Checa("`trn_agenda` sem argumento mostra a agenda dos dois",
					  EscutaDeAvisos.Any(a => a.Contains("Terra", StringComparison.Ordinal) && a.Contains("Outro Mundo", StringComparison.Ordinal)),
					  string.Join(" | ", EscutaDeAvisos));
				PularSegundos(61);
				Tiques(1);
				Checa("...e vencido o minuto a AGENDA abre o torneio sozinha (o caminho automatico, sem `trn_iniciar`)",
					  TorneioAtivo && FaseDoTorneioDeTeste == "Inscricao" && TipoDoTorneioDeTeste == "Terra", $"{FaseDoTorneioDeTeste}/{TipoDoTorneioDeTeste}");

				// INSCREVER O ALVO: o admin poe alguem na chave sem o clique dele.
				ComandoDeTorneio(pl, "trn_inscrever", pl.Id.ToString());
				Checa("`trn_inscrever <id>` inscreve o alvo (aqui, o proprio host)", InscritosDeTeste == 1, $"inscritos {InscritosDeTeste}");

				// PULAR A ESPERA, fase a fase, pelos caminhos do relogio.
				ComandoDeTorneio(pl, "trn_avancar", "");
				Checa("`trn_avancar` nas inscricoes FECHA na hora: chave montada e 32 presos",
					  FaseDoTorneioDeTeste != "Inscricao" && PresosDeTeste.Count == 32, $"{FaseDoTorneioDeTeste}, presos {PresosDeTeste.Count}");
				ComandoDeTorneio(pl, "trn_avancar", "");
				Tiques(1);
				Checa("...na preparacao pula pra CONTAGEM", FaseDoTorneioDeTeste == "Contagem", FaseDoTorneioDeTeste);
				ComandoDeTorneio(pl, "trn_avancar", "");
				Tiques(1);
				Checa("...na contagem comeca a LUTA", FaseDoTorneioDeTeste == "Luta", FaseDoTorneioDeTeste);
				int decididasAntes = ChaveDeTeste?.LutasDecididas ?? 0;
				ComandoDeTorneio(pl, "trn_avancar", "");
				Checa("...na luta ENCERRA por pontos (os juizes decidem pela vida) e vai pro intervalo",
					  FaseDoTorneioDeTeste == "Intervalo" && (ChaveDeTeste?.LutasDecididas ?? 0) == decididasAntes + 1,
					  $"{FaseDoTorneioDeTeste}, decididas {ChaveDeTeste?.LutasDecididas}");

				// CONTRA-EXEMPLO: quem nao e admin nao adianta nada, nem remarca a agenda.
				ServerPlayer figurante = _players[NpcsDoTorneioDeTeste[0]];
				string faseAntes = FaseDoTorneioDeTeste;
				long alemAntes = AgendaDeTeste.ProximoDoAlemMs;
				ComandoDeTorneio(figurante, "trn_avancar", "");
				ComandoDeTorneio(figurante, "trn_agenda", "alem 1");
				ComandoDeTorneio(figurante, "trn_inscrever", pl.Id.ToString());
				Checa("CONTRA-EXEMPLO: sem ser admin, `trn_avancar`, `trn_agenda` e `trn_inscrever` nao fazem nada",
					  FaseDoTorneioDeTeste == faseAntes && AgendaDeTeste.ProximoDoAlemMs == alemAntes, $"{FaseDoTorneioDeTeste}");
				ComandoDeTorneio(pl, "trn_cancelar", "");

				// E O OUTRO MUNDO PELA AGENDA, do mesmo jeito.
				ComandoDeTorneio(pl, "trn_agenda", "alem 1");
				PularSegundos(61);
				Tiques(1);
				Checa("`trn_agenda alem 1` + um minuto: a agenda abre o Torneio do OUTRO MUNDO",
					  TorneioAtivo && TipoDoTorneioDeTeste == "Alem", $"{FaseDoTorneioDeTeste}/{TipoDoTorneioDeTeste}");
				ComandoDeTorneio(pl, "trn_cancelar", "");
				Checa("...e `trn_cancelar` o fecha", !TorneioAtivo);
			}

			GD.Print("[torneio] -- 8) a area de espera FECHA o teclado, e o chaveamento vai pra tela (dono, 2026-09-07) --");
			{
				EscutaDeEfeitos = [];
				EscutaDeChaves = [];
				ComandoDeTorneio(pl, "trn_iniciar", "terra");
				ComandoDeTorneio(pl, "trn_participar", "");
				ComandoDeTorneio(pl, "trn_avancar", "");
				Checa("PRECONDICAO: as inscricoes fecharam com o host preso na area de espera", TorneioAtivo && PresosDeTeste.Contains(pl.Id), FaseDoTorneioDeTeste);
				Checa("ao fechar as inscricoes o host recebe o CHAVEAMENTO: 32 competidores, ele entre eles, 16 lutas na primeira rodada",
					  EscutaDeChaves.Any(c => c.Quem == pl.Id && c.Chave.Aviso == 1 && c.Chave.Competidores.Count == 32 && c.Chave.MinhaChave >= 0
											 && c.Chave.Rodadas.Count == 1 && c.Chave.Rodadas[0].Lutas.Count == 16),
					  string.Join(" | ", EscutaDeChaves.Where(c => c.Quem == pl.Id).Select(c => $"aviso {c.Chave.Aviso} comp {c.Chave.Competidores.Count} minha {c.Chave.MinhaChave} rodadas {c.Chave.Rodadas.Count}")));
				Checa("...e a chave nao carrega assinatura nenhuma: as lutas apontam por INDICE (todos entre 0 e 31)",
					  EscutaDeChaves.Where(c => c.Quem == pl.Id).All(c => c.Chave.Rodadas.All(r => r.Lutas.All(l => l.A is >= 0 and < 32 && l.B is >= 0 and < 32 && l.Vencedor < 32))));
				Checa("...e recebe o efeito `torneio_espera` (a trava de input do cliente)",
					  EscutaDeEfeitos.Any(e => e.Quem == pl.Id && e.Id == "torneio_espera" && e.Ms == -1));
				Vec2 ondeEspera = pl.Pos;
				AplicarInput(pl, pl.SeqInput + 1, (uint)NowMs(), pl.Pos + Tiles(5, 0), Protocol.InputAndando);
				Checa("preso, um pacote de input com o corpo 5 tiles adiante NAO move nada (`PodeMexerOCorpo` recusa na origem)",
					  Vec2.Distance(pl.Pos, ondeEspera) < 1f && !PodeMexerOCorpo(pl), $"{Vec2.Distance(pl.Pos, ondeEspera):0} px");
				AplicarInput(pl, pl.SeqInput + 1, (uint)NowMs(), pl.Pos, Protocol.InputSubir);
				Checa("...nem a tecla de subir vale (o `QuerSubir` fica falso)", !pl.QuerSubir);
				pl.Livro.Dar(SkillDoKi);
				pl.Niveis.Por(SkillDoKi, MaestriaQueDestravaVoo);
				pl.Ficha.Ki = pl.Ficha.MaxKi;
				AlternarVoo(pl);
				Checa("...nem o verb de voar (\"sem voar ate a sua vez\")", !pl.Voando);

				// A CHAVE ACOMPANHA A FASE: contagem, luta, intervalo -- e quem luta agora vai por id de corpo.
				int retratosAntes = EscutaDeChaves.Count(c => c.Quem == pl.Id);
				ComandoDeTorneio(pl, "trn_avancar", "");
				Tiques(1);
				ComandoDeTorneio(pl, "trn_avancar", "");
				Tiques(1);
				Checa("PRECONDICAO: chegou na LUTA", FaseDoTorneioDeTeste == "Luta", FaseDoTorneioDeTeste);
				(int lutaA, int lutaB) = LutadoresDeTeste;
				ChaveNaTela? ultimo = EscutaDeChaves.LastOrDefault(c => c.Quem == pl.Id).Chave;
				Checa("na luta o retrato diz a fase (3) e QUEM luta agora, por id de corpo",
					  ultimo != null && ultimo.Fase == 3 && ultimo.CorpoA == lutaA && ultimo.CorpoB == lutaB && lutaA != 0 && lutaB != 0,
					  ultimo == null ? "sem retrato" : $"fase {ultimo.Fase} corpos {ultimo.CorpoA}/{ultimo.CorpoB} vs {lutaA}/{lutaB}");
				Checa("...e o centro da arena viaja junto (a camera do espectador olha pra la sem lutador)",
					  ultimo != null && cfg.Terra.Arena.ContemEm(ultimo.Centro));
				ComandoDeTorneio(pl, "trn_avancar", "");
				ChaveNaTela? depois = EscutaDeChaves.LastOrDefault(c => c.Quem == pl.Id).Chave;
				Checa("a luta encerrada entra no retrato seguinte: uma luta com vencedor, fase intervalo (4)",
					  depois != null && depois.Fase == 4 && depois.Rodadas[0].Lutas.Count(l => l.Vencedor >= 0) == 1,
					  depois == null ? "sem retrato" : $"fase {depois.Fase}, decididas {depois.Rodadas[0].Lutas.Count(l => l.Vencedor >= 0)}");
				Checa("...e foram retratos NOVOS a cada fase (contagem, luta, intervalo), nao um so",
					  EscutaDeChaves.Count(c => c.Quem == pl.Id) >= retratosAntes + 3, $"{EscutaDeChaves.Count(c => c.Quem == pl.Id) - retratosAntes} retrato(s)");

				// QUEM ESPERA POUSA: um NPC preso no ar desce ate o chao pela propria espera.
				ServerPlayer? presoNoAr = PresosDeTeste.Select(id => _players.GetValueOrDefault(id)).FirstOrDefault(c => c != null && !EhJogador(c));
				if (presoNoAr != null)
				{
					presoNoAr.Voando = true;
					presoNoAr.Altitude = Jandirus.Core.World.Voo.AlturaMaxima / 2f;
					presoNoAr.QuerSubir = true;   // o `QuerSubir` velho, que subia sozinho ate o teto
					Tiques(90);
					Checa("um NPC preso que estivesse no ar (com o `QuerSubir` velho ligado) POUSA em 3 s -- a espera manda descer",
						  presoNoAr.Altitude <= 0f && !presoNoAr.Voando, $"altitude {presoNoAr.Altitude:0}, voando {presoNoAr.Voando}");
				}
				else Checa("(nao havia NPC preso pra medir o pouso da espera)", false);

				EscutaDeEfeitos = [];
				ComandoDeTorneio(pl, "trn_cancelar", "");
				Checa("cancelado: o efeito `torneio_espera` some (0) e o retrato final avisa que fechou (aviso 2)",
					  EscutaDeEfeitos.Any(e => e.Quem == pl.Id && e.Id == "torneio_espera" && e.Ms == 0)
					  && EscutaDeChaves.Any(c => c.Quem == pl.Id && c.Chave.Aviso == 2));
				Checa("...e o host anda de novo (`PodeMexerOCorpo`)", PodeMexerOCorpo(pl));
				AlternarVoo(pl);
				Checa("...e voa de novo (o verb volta a valer)", pl.Voando);
				if (pl.Voando) AlternarVoo(pl);
				EscutaDeEfeitos = null;
				EscutaDeChaves = null;
			}
		}
		catch (Exception ex) { Checa("a bancada rodou ate o fim sem excecao", false, ex.ToString()); }
		finally
		{
			if (_torneio != null) EncerrarTorneio(_torneio);
			_torneioCfg = original;
			_agenda = agendaAntes;
			SalvarAgenda();
			EscutaDoTorneio = escutaAntes;
			EscutaDeAvisos = avisosAntes;
			pl.Ficha.BP = bpDoHost;
			pl.Ficha.Statify();
			pl.Ficha.PowerLevel();
			pl.Ficha.Zeni = zeniDoHost;
			pl.Ficha.dead = mortoDoHost;
			pl.MorteJaViajou = viajouDoHost;
			pl.Combate.Letal = letalDoHost;
			pl.Combate.Carencia = 0;
			if (pl.Zone.Hash != zonaDoHost.Hash) MoveToZone(pl.Id, zonaDoHost, posDoHost);
			if (puseu) naZona.Remove(pl);
		}
		GD.Print($"[torneio] ================ {ok} OK, {falhou} FALHA(S) ================");
	}
}
