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
///
/// ============================ 2026-10-17: OS TIROS DE VERDADE E O CENSO DA ARENA ============================
/// O dono: *"o torneio do outro mundo TA TODO BUGADO"* -- a roda de espera inteira respondendo feixe com feixe. A bancada
/// passava verde porque os tiques dela nao andavam os ATAQUES DE KI: nenhum feixe nascia, e o reflexo do NPC
/// (`TickDoContraFeixe`) nunca tinha o que responder. Agora o `Tiques` anda o bloco de Ki do tique de producao, na mesma
/// ordem, e as duas chaves (Terra e Outro Mundo) passam tique a tique pelo <see cref="ContarNaArena"/>: so os dois da luta
/// dentro da arena, a espera na roda em volta, e ninguem fora da luta carregando feixe, atirando ou gritando.
/// ===========================================================================================================
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
			DiasDoJogo = _agenda.DiasDoJogo,
		};
		List<string>? escutaAntes = EscutaDoTorneio;
		List<(int Quem, Protocol.Fala Canal, string Texto)>? falasAntes = EscutaDeFalas;
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
				// OS ATAQUES DE KI, o bloco do tique de producao na mesma ordem (`GameServer.cs`: canal, G12, tiros, embates, prazo e
				// segundo aperto da IA). Sem ele nenhum feixe nascia aqui, e o caos do Outro Mundo passava verde (2026-10-17).
				TickDosCanaisDeKi(Protocol.TickSeconds);
				TickG12(Protocol.TickSeconds);
				TickDosProjeteis(Protocol.TickSeconds);
				TickDosEmbatesDeKi(Protocol.TickSeconds);
				TickDoPrazoDeRaioDaIa(Protocol.TickSeconds);
				TickDoSegundoApertoDaIa();
			}
		}
		void PularSegundos(double s) => AdiantoDoRelogioDeTeste = (long)(relogioAndado += s * 1000);
		static Vec2 Tiles(float x, float y) => new(x * ZoneCollision.TileSize, y * ZoneCollision.TileSize);

		try
		{
			EscutaDoTorneio = [];
			EscutaDeAvisos = [];
			EscutaDeFalas = [];
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

			// AS CADEIRAS DO CEU (2026-10-17): a lista do torneio.json e a do DM (`Tournament.dm:650-654`, pela conta do cabecalho do
			// `TorneioConfig`), cada uma chao livre no mapa de verdade e FORA da arena -- e o olhar que o servidor da a quem senta e o da
			// fileira do DM, escrito aqui como o DM o escreve.
			{
				List<Cadeira> doDm = TorneioConfig.CadeirasDoCeu();
				List<Cadeira> lidas = original.Alem.Cadeiras;
				ZoneCollision? mapaDoAlem = MapaDaZonaOuCatalogo(ZoneKey.Premade(original.Alem.Zona));
				int foraDoChao = lidas.Count(cad => mapaDoAlem?.ServeDeChao(cad.X, cad.Y) != true);
				int naArena = lidas.Count(cad => original.Alem.Arena.Contem(cad.X, cad.Y));
				Checa("as CADEIRAS do Outro Mundo no torneio.json sao as 32 do DM (o `seats` do ceu, na ordem), chao livre no mapa de verdade e FORA da arena",
					  doDm.Count == 32 && lidas.SequenceEqual(doDm) && foraDoChao == 0 && naArena == 0,
					  $"{lidas.Count} lida(s), {doDm.Count} do DM; {foraDoChao} fora do chao; {naArena} dentro da arena");
				Facing[] daFileira = [Facing.East, Facing.South, Facing.North, Facing.West];   // as quatro fileiras do DM, de 8 em 8
				int viradas = doDm.Where((cad, ix) => cad.OlharPara(original.Alem.Arena) == daFileira[ix / 8]).Count();
				Checa("...e quem senta olha pra arena: a direcao que o servidor da (pro centro do palco) e a de cada fileira do DM (EAST, SOUTH, NORTH, WEST)",
					  viradas == 32, $"{viradas} de 32");
				Checa("a Terra nao tem cadeiras: a espera dela e a roda sorteada (o DM deixa `seats = null`)", original.Terra.Cadeiras.Count == 0);
				List<string> torta = new TorneioConfig
				{
					Alem = new PalcoDeTorneio { Zona = "Afterlife", Arena = new Arena(110, 346, 125, 360), Cadeiras = [new(115, 350), new(108, 346), new(108, 346)] },
				}.Problemas();
				Checa("[injecao] uma cadeira DENTRO da arena e uma repetida sao apontadas",
					  torta.Any(x => x.Contains("DENTRO da arena")) && torta.Any(x => x.Contains("repetida")), string.Join(" | ", torta));
			}

			// =====================================================================
			// 1) INSCRICOES E CONVITE
			// =====================================================================
			GD.Print("[torneio] -- 1) inscricoes e convite --");
			// ============================ O HOST VAI PRA TERRA PELO FUNIL, NAO PELO DADO ============================
			// Desde o 0b0c8b2 o host acorda no `/obj/SpawnPoint` do berco DELE, e a classe e sorteada a cada
			// rodada: so o Saiyajin Low-Class (despejado) acorda na Terra; o comum acorda em Vegeta e o Lendario
			// no exilio, em orbita de um mundo gerado. A bancada presumia a Terra e passava ou caia pelo dado --
			// 95 OK com o Low-Class, 45 OK e 32 FALHAS com os outros dois, todas em cascata desta precondicao.
			//
			// A REGRA NAO MUDA: `eligible` e `!M.dead && M.Planet == "Earth"` (`Tournament.dm:296-297`), lida
			// no `Elegivel`. Muda o palco: o host vai ao marco de nascimento da Terra pelo MESMO `MoveToZone` +
			// `PontoDeNascimento` do berco (o lugar onde o Low-Class ja acordava, entao a bancada mede o mundo
			// das rodadas verdes), e o `finally` o devolve pra onde acordou. A precondicao continua conferindo:
			// agora o que ela prova e que o funil de producao pos na Terra um host vivo e admin.
			//
			// DEPOIS do "host na lista da zona" la de cima, de proposito: o `MoveToZone` tira o corpo da lista
			// de origem e o poe na da Terra, e o `finally` (que o devolve pelo mesmo funil e so entao tira a
			// entrada que a bancada pos) deixa as listas como o login as espera -- o login faz o proprio `Add`.
			// ======================================================================================================
			ZoneKey zonaDaTerra = ZoneKey.Premade(cfg.Terra.Zona);
			Vec2 chegadaNaTerra = PontoDeNascimento(zonaDaTerra);
			MoveToZone(pl.Id, zonaDaTerra, chegadaNaTerra);
			Checa("PRECONDICAO: o host e admin, esta vivo e na Terra (elegivel)", EhAdmin(pl) && !pl.Ficha.dead && pl.Zone.Name == cfg.Terra.Zona,
				  $"zona {pl.Zone.Name}, acordou em {zonaDoHost.Name}");
			Checa("sem torneio, `trn_participar` diz que nao ha inscricoes abertas", ComandoDeTorneio(pl, "trn_participar", "") && !TorneioAtivo);
			Avisos();
			ComandoDeTorneio(pl, "trn_iniciar", "terra");
			Checa("o admin abre o torneio da Terra na marra (`trn_iniciar terra`) e as inscricoes abrem", TorneioAtivo && FaseDoTorneioDeTeste == "Inscricao");
			long diaDoJogo = (long)(Espaco.SegundosPorDiaInGame * 1000);
			Checa("...e a agenda do proximo da Terra e do Outro Mundo e marcada NO COMECO (30 e 15 DIAS DO JOGO: 12 h e 6 h de relogio) e gravada",
				  Math.Abs(AgendaDeTeste.ProximoDaTerraMs - (NowMs() + 30 * diaDoJogo)) < 5000
				  && Math.Abs(AgendaDeTeste.ProximoDoAlemMs - (NowMs() + 15 * diaDoJogo)) < 5000
				  && System.IO.File.Exists(CaminhoDaAgenda),
				  $"Terra em {(AgendaDeTeste.ProximoDaTerraMs - NowMs()) / 3_600_000.0:0.##} h, Outro Mundo em {(AgendaDeTeste.ProximoDoAlemMs - NowMs()) / 3_600_000.0:0.##} h de relogio");
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
			// [injecao] A OUTRA METADE DO `eligible`: vivo, mas FORA da Terra -- o estado em que o berco sorteado
			// deixava o host, agora posto de proposito pelo mesmo funil. E o contra-exemplo da precondicao: se o
			// `Elegivel` deixasse de olhar o planeta, esta linha ficaria vermelha. Namek e nao Vegeta: a mesma
			// pergunta sem a gravidade de 10x mexendo na ficha no meio do caminho.
			var namek = ZoneKey.Premade("Namek");
			MoveToZone(pl.Id, namek, PontoDeNascimento(namek));
			Avisos();
			ComandoDeTorneio(pl, "trn_participar", "");
			Checa("[injecao] vivo mas FORA da Terra (em Namek) nao se inscreve", InscritosDeTeste == 0 && Avisos().Any(x => x.Contains("na Terra")), pl.Zone.Name);
			MoveToZone(pl.Id, zonaDaTerra, chegadaNaTerra);
			ComandoDeTorneio(pl, "trn_participar", "");
			Checa("vivo de novo e de volta a Terra, inscreve", InscritosDeTeste == 1);
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

			// (2026-10-18) OS FROST DEMONS DO TORNEIO NAO SAO MAIS UM SO -- o dono: *"e no torneio os frost demons tao sempre usando o mesmo sprite,
			// coloca mais variaçao, tem varios sprites de frost"*. O lutador sai do molde (`lutador_de_torneio`: Humano, Saiyajin, Namekuseijin, Icer), e
			// o Frost de NPC nascia com a lista PADRAO do DM -- todo um "Changeling Frieza 2". A regua pela porta de producao (`AparenciaDeNpc`, a que o
			// `NascerNpc` chama), em doze sementes de Icer; e os Frost que ESTE torneio nasceu, de verdade.
			{
				Jandirus.Core.Npc.MoldeDeNpc molde = _moldes!.Get(cfg.MoldeDoNpc)!;
				string[] Bases() => [.. Enumerable.Range(0, 12).Select(i => AparenciaDeNpc(molde, "Frost Demon", "Male", (ulong)(1000 + 37 * i)).FormasDeFrost[0])];
				string[] bases = Bases();
				Checa("OS FROST DEMONS DE NPC VARIAM (*\"tem varios sprites de frost\"*): doze sementes de Icer pela `AparenciaDeNpc` saem com corpos de "
					  + "repouso diferentes, todos do catalogo e nenhum o dourado",
					  bases.Distinct().Count() >= 6 && bases.All(c => Array.IndexOf(Jandirus.Core.Races.FormasDeFrost.Catalogo, c) >= 0 && c != "GoldIcer"),
					  $"{bases.Distinct().Count()} diferentes: {string.Join(", ", bases.Distinct().Take(6))}...");
				FrostNpcSemVariedadeDeTeste = true;
				string[] comODefeito;
				try { comODefeito = Bases(); }
				finally { FrostNpcSemVariedadeDeTeste = false; }
				Checa("(defeito injetado: a lista padrao do DM, o jogo de antes) as doze saem com o MESMO corpo -- a regua de cima REPROVA",
					  comODefeito.Distinct().Count() == 1, string.Join(", ", comODefeito.Distinct()));
				Checa("...e o CHEFE de saga (o Freeza de Vegeta) continua com a escada padrao: o roteiro crava o corpo de cada estagio dele",
					  _moldes.Get("freeza_vegeta") is { } freeza && AparenciaDeNpc(freeza, "Frost Demon", "Male", 7).FormasDeFrost
						  .SequenceEqual(Jandirus.Core.Races.FormasDeFrost.Sanear(Jandirus.Core.Races.FormasDeFrost.ClasseNormal, null)));
				string[] doTorneio = [.. npcs.Select(i => _players.GetValueOrDefault(i))
					.Where(n => n != null && Jandirus.Core.Races.FormasDeFrost.EhFrost(n.Race) && n.Visual.FormasDeFrost.Count > 0)
					.Select(n => n!.Visual.FormasDeFrost[0])];
				GD.Print($"[torneio]      os Frost Demons deste torneio: {doTorneio.Length}, com {doTorneio.Distinct().Count()} corpo(s) de repouso: "
					   + string.Join(", ", doTorneio));
				// TRES OU MAIS, e nao dois: com 53 corpos no sorteio, dois iguais por acaso e 2%; tres iguais, 0,04%.
				if (doTorneio.Length >= 3)
					Checa("...e os Frost Demons DESTE torneio nao sao todos iguais", doTorneio.Distinct().Count() > 1, string.Join(", ", doTorneio));
			}

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
			bool ringOutTestado = false, woTestado = false, woVenceu = false, reconexaoTestada = false, voadorDevolvido = false;
			string ultimoPar = "", nomeDoVoador = "-";
			int maiorAndar = 0, tiquesAltoDemais = 0, tiquesAltoSobreOChao = 0, tiquesPresoNoAr = 0;
			// (2026-10-27, o pedido 124e) a vida de entrada de cada luta: NPC contra NPC com 1/4, a do host cheia
			int lutasDeNpcMedidas = 0, lutasDoHostMedidas = 0;
			bool vidaDeNpcUmQuarto = true, vidaDoHostCheia = true;
			string vidaDeNpcDetalhe = "", vidaDoHostDetalhe = "";
			const int teto = 90_000;   // 50 min de mundo: o dobro do pior caso (32 lutas de 20 s + folgas)
			var censoTerra = new CensoDaArena();
			int[] participantesTerra = [.. npcs, pl.Id];
			while (TorneioAtivo && tiques < teto)
			{
				Tiques(1);
				tiques++;
				ContarNaArena(censoTerra, participantesTerra);
				if (FaseDoTorneioDeTeste != "Luta") { lutaAtualEmTiques = 0; continue; }
				lutaAtualEmTiques++;
				maiorLutaEmTiques = Math.Max(maiorLutaEmTiques, lutaAtualEmTiques);
				(int a, int b) = LutadoresDeTeste;
				string par = $"{a}x{b}";
				if (par != ultimoPar) { ultimoPar = par; if (a == pl.Id || b == pl.Id) lutasDoHost++; }
				// (2026-10-27, o pedido 124e) A VIDA DE ENTRADA, no primeiro tique de cada luta (antes de qualquer soco do host)
				if (lutaAtualEmTiques == 1 && _players.TryGetValue(a, out ServerPlayer? entraA) && _players.TryGetValue(b, out ServerPlayer? entraB))
				{
					string vidas = $"{entraA.Name} {entraA.Ficha.HP:0.#} / {entraB.Name} {entraB.Ficha.HP:0.#}";
					if (a == pl.Id || b == pl.Id)
					{
						lutasDoHostMedidas++;
						if (entraA.Ficha.HP < 90 || entraB.Ficha.HP < 90) { vidaDoHostCheia = false; vidaDoHostDetalhe = vidas; }
						else if (vidaDoHostDetalhe.Length == 0) vidaDoHostDetalhe = vidas;
					}
					else
					{
						lutasDeNpcMedidas++;
						if (entraA.Ficha.HP is < 10 or > 40 || entraB.Ficha.HP is < 10 or > 40) { vidaDeNpcUmQuarto = false; vidaDeNpcDetalhe = vidas; }
						else if (vidaDeNpcDetalhe.Length == 0) vidaDeNpcDetalhe = vidas;
					}
				}
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
					var perfilDeNascenca = na.Perfil;
					na.Livro.Dar(SkillDoKi); na.Niveis.Por(SkillDoKi, MaestriaQueDestravaVoo);
					na.Perfil = new Jandirus.Core.Ai.PerfilDeCombate(true, true);
					na.Cerebro!.Poderes = na.Perfil.Filtrar(LerCapacidades(na));
					if (!na.Voando) AlternarVoo(na);
					nomeDoVoador = na.Name;
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

					// ============================ O VOADOR ERA DE PALCO, E NAO LEVA AS ASAS PRA CHAVE ============================
					// Quem ganha esta luta e ele -- o outro e que pisou fora --, e ele segue na chave. Com as asas que a
					// bancada lhe deu, ele voava pra fora da linha sem perder (e a regra: voando nao e ring-out), e o
					// host, que nao voa, cravado ao lado dele pisava fora e perdia: "oitavas de final: vs Borgos ->
					// PERDEU (ring-out) | o voador da bancada: Borgos" (uma rodada em dez, 2026-09-24). O lutador de
					// torneio so nasce voando quando algum INSCRITO voa (`NascerLutador`, o `t.AlguemVoa`), e o host
					// desta secao nao voa: o voador volta ao perfil com que nasceu e o resto da chave e lutado pelos
					// corpos que o torneio de producao pos nela. O pouso ja foi feito pelo `TerminarLuta` (o
					// `tourney_standdown`).
					na.Perfil = perfilDeNascenca;
					na.Cerebro!.Poderes = na.Perfil.Filtrar(LerCapacidades(na));
					voadorDevolvido = !na.Voando && na.Perfil == perfilDeNascenca && !na.Perfil.Voa;
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
			Checa("(o pedido 124e) LUTA DE NPC CONTRA NPC comeca com 1/4 da vida (medida no primeiro tique de cada uma)",
				  lutasDeNpcMedidas > 0 && vidaDeNpcUmQuarto, $"{lutasDeNpcMedidas} luta(s); {vidaDeNpcDetalhe}");
			Checa("...e a luta do HOST (fora dos NPCs) comeca com a vida CHEIA -- o contra-exemplo",
				  lutasDoHostMedidas > 0 && vidaDoHostCheia, $"{lutasDoHostMedidas} luta(s); {vidaDoHostDetalhe}");
			Checa($"o torneio de 32 roda ate o fim sem travar ({tiques} tiques = {tiques * Protocol.TickSeconds / 60:0.0} min de mundo)", !TorneioAtivo && tiques < teto, $"{tiques} tiques, fase {FaseDoTorneioDeTeste}");
			Checa("a chave fechou: campeao, vice e terceiro definidos, 32 lutas decididas (16+8+4+2 + 3o lugar + final)",
				  chave is { Acabou: true } && chave.Campeao.Length > 0 && chave.Vice.Length > 0 && chave.Terceiro.Length > 0 && chave.LutasDecididas == 32,
				  $"{chave?.LutasDecididas} lutas, campeao '{chave?.NomeDe(chave.Campeao)}', vice '{chave?.NomeDe(chave.Vice)}', 3o '{chave?.NomeDe(chave.Terceiro)}'");
			Checa("as rodadas sao 16-avos, oitavas, quartas, semifinal, disputa do 3o lugar e final, nessa ordem",
				  chave != null && string.Join(" > ", chave.Rodadas.Select(r => r.Nome)) == "16-avos de final > oitavas de final > quartas de final > semifinal > disputa do 3o lugar > final",
				  chave == null ? "" : string.Join(" > ", chave.Rodadas.Select(r => r.Nome)));
			Checa("em toda luta os dois da vez estavam soltos e todo o resto da chave estava preso", invariantesOk);
			GD.Print($"[torneio]   censo da arena da Terra: {censoTerra}");
			Checa("CENSO (Terra): tique a tique, so os dois da luta DENTRO da arena; quem nao lutava estava preso, na roda em volta (1 a 6 celulas da linha)",
				  censoTerra.Amostras > 0 && censoTerra.DentroSemLutar == 0 && censoTerra.SoltosForaDaLuta == 0 && censoTerra.EsperaLonge == 0, censoTerra.ToString());
			Checa("CENSO (Terra): os tiros da luta andaram de verdade, e ninguem fora da luta carregou feixe, atirou ou gritou",
				  censoTerra.TirosDaLuta > 0 && censoTerra.CanaisForaDaLuta == 0 && censoTerra.TirosForaDaLuta == 0 && censoTerra.FalasForaDaLuta == 0,
				  censoTerra.ToString());
			Checa("CENSO (Terra): na CONTAGEM os NPCs da vez ficam no lugar, sem atirar nem falar (so o \"COMECEM!\" os solta um contra o outro)",
				  censoTerra.TirosNaContagem == 0 && censoTerra.FalasNaContagem == 0 && censoTerra.AndouNaContagem == 0, censoTerra.ToString());
			Checa($"nenhuma luta passou do tempo maximo ({cfg.LutaSegundosMax:0} s + folga)", maiorLutaEmTiques <= (cfg.LutaSegundosMax + 3) * Protocol.TickHz, $"{maiorLutaEmTiques / (double)Protocol.TickHz:0.0} s");
			// O DETALHE CONTA COMO CADA LUTA DO HOST ACABOU, e quem foi o voador da bancada: esta linha caiu uma vez
			// em dez so com "5 lutas do host", e o numero sozinho nao dizia se foi ring-out, tempo ou W.O.
			string lutasContadas = chave == null ? "" : string.Join("; ", chave.Rodadas.SelectMany(r => r.Lutas
				.Where(l => l.A == pl.Assinatura || l.B == pl.Assinatura)
				.Select(l => $"{r.Nome}: vs {chave.NomeDe(l.A == pl.Assinatura ? l.B : l.A)} -> "
							 + $"{(l.Vencedor == pl.Assinatura ? "venceu" : "PERDEU")} ({l.Como})")));
			Checa("o host, mil vezes mais forte, lutou as 5 rodadas ate a final e e o CAMPEAO", chave != null && chave.Campeao == pl.Assinatura && lutasDoHost == 5,
				  $"{lutasDoHost} lutas do host -- {lutasContadas} | o voador da bancada: {nomeDoVoador}");
			Checa("...com o golpe NAO-LETAL forcado durante as lutas dele", !hostLetalDuranteLuta);
			Checa("...e nenhum NPC morreu no torneio (esporte, nao guerra)", npcs.All(id => !_players.ContainsKey(id) || !_players[id].Ficha.dead));
			Checa($"o premio do campeao ({cfg.Premio1:N0} zeni) foi creditado ao host", Math.Abs(pl.Ficha.Zeni - (zeniDoHost + cfg.Premio1)) < 0.5, $"{zeniDoHost:N0} -> {pl.Ficha.Zeni:N0}");
			Checa("...e o vice e o terceiro (NPCs) nao levam zeni nenhum (o `if (M.client)` do `award`)", Ouvidos().Count >= 0);
			Checa("o VOADOR posto fora da linha VOANDO nao sofre ring-out: a luta segue", voadorSalvo);
			Checa("o que NAO voa, posto fora da linha, perde na hora por RING-OUT", semVooSaiu && ringOutVisto);
			Checa("...e o voador de palco seguiu na chave POUSADO e com o perfil com que nasceu (sem voo), como todo lutador de torneio",
				  voadorDevolvido, nomeDoVoador);
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
			// O CENSO DO OUTRO MUNDO (2026-10-17) -- o dono: *"o torneio do outro mundo TA TODO BUGADO"*, com a foto da roda de espera
			// inteira gritando "Onda de Ki!!". A arena celeste e pequena: todo feixe da luta passa pela plateia.
			var censoAlem = new CensoDaArena();
			int[] participantesAlem = [.. mortos, pl.Id];
			bool reflexoMedido = false, contagemMedida = false, povoMedido = false, cadeiraMedida = false;
			while (TorneioAtivo && tiquesAlem < teto)
			{
				Tiques(1);
				tiquesAlem++;
				ContarNaArena(censoAlem, participantesAlem);

				// ============================ O CONTRA-EXEMPLO DO POVO NO PALCO ============================
				// Uma alma do Outro Mundo (o molde do povoamento) nasce no MEIO do palco, com o torneio no ar. A regra e lida na porta dela
				// (`ForaDoPalcoDoTorneio`, o rumo que a rotina recebe): parada no meio, o rumo e pra fora; vindo de fora pra dentro, o passo
				// para. E o mundo anda: em 4 s ela esta fora do palco. Com o defeito (o povo no palco) a porta nao mexe em rumo nenhum.
				// ===========================================================================================
				if (!povoMedido && FaseDoTorneioDeTeste == "Luta")
				{
					povoMedido = true;
					Arena palco = PisoDoPalco(_torneio!.Arena);
					Vec2 meio = new Vec2((palco.X1 + palco.X2) / 2f + 0.5f, palco.Y1 + 1.5f) * ZoneCollision.TileSize;
					ServerPlayer? alma = NascerNpc("alma", alem, meio, 9_377);
					if (alma == null) Checa("PRECONDICAO: uma alma nasceu no palco pro contra-exemplo", false);
					else
					{
						var parada = new Jandirus.Core.Ai.Ordem();
						var praDentro = new Jandirus.Core.Ai.Ordem { Rumo = new Vec2(0, 1) };
						Vec2 naBorda = new Vec2(meio.X, (palco.Y1 - 0.4f) * ZoneCollision.TileSize);
						Vec2 antes = alma.Pos;
						alma.Pos = naBorda;
						Jandirus.Core.Ai.Ordem barrada = ForaDoPalcoDoTorneio(alma, praDentro);
						alma.Pos = antes;
						Jandirus.Core.Ai.Ordem saindo = ForaDoPalcoDoTorneio(alma, parada);
						PovoNoPalcoDeTeste = true;
						Jandirus.Core.Ai.Ordem saindoComDefeito;
						try { saindoComDefeito = ForaDoPalcoDoTorneio(alma, parada); }
						finally { PovoNoPalcoDeTeste = false; }
						for (int i = 0; i < 120 && palco.ContemEm(alma.Pos); i++) Tiques(1);
						bool saiu = !palco.ContemEm(alma.Pos);
						Checa("**O POVO SAI DO PALCO**: a alma no meio do palco recebe o rumo pra fora, a que vinha entrando para na borda -- e em 4 s ela esta fora",
							  saindo.Rumo.LengthSquared > 0.5f && barrada.Rumo.LengthSquared < 1e-6f && saiu,
							  $"rumo de saida {saindo.Rumo}; o passo pra dentro virou {barrada.Rumo}; fora do palco {saiu}");
						Checa("CONTRA-EXEMPLO (defeito injetado: o povo no palco): a mesma alma parada no meio fica parada -- ninguem a tira de la",
							  saindoComDefeito.Rumo.LengthSquared < 1e-6f, $"rumo {saindoComDefeito.Rumo}");
						RemoverNpc(alma);
						EsquecerAteAqui(censoAlem);
					}
				}

				// ============================ O CONTRA-EXEMPLO DA CONTAGEM ============================
				// Na primeira contagem com um NPC entre os dois da vez, um feixe do OUTRO nasce a tres celulas dele, vindo pra cima, e so
				// o tique dos corpos anda (o reflexo e o do `TicarUmCorpo`; o relogio do torneio fica parado, a contagem nao acaba no
				// meio). COM O DEFEITO (o lutador da vez solto na contagem) ele responde -- e o jogo de antes; SEM ele, nada ate o
				// "COMECEM!".
				// ======================================================================================
				if (!contagemMedida && FaseDoTorneioDeTeste == "Contagem"
					&& _players.TryGetValue(LutadoresDeTeste.A, out ServerPlayer? daVezA) && _players.TryGetValue(LutadoresDeTeste.B, out ServerPlayer? daVezB))
				{
					contagemMedida = true;
					(ServerPlayer vez, ServerPlayer outroDaVez) = EhJogador(daVezA) ? (daVezB, daVezA) : (daVezA, daVezB);
					if (!SabeTecnica(vez, "Ki_Wave")) DarSkillDoRaio(vez);
					foreach (bool comDefeito in new[] { true, false })
					{
						vez.Ficha.Ki = vez.Ficha.MaxKi;
						_contraFeixeLivreEm.Remove(vez.Id);
						Vec2 rumo = (vez.Pos - outroDaVez.Pos).Normalized();
						Projetil feixe = Disparar(outroDaVez, RaioDeTeste(), rumoDado: rumo, deOnde: vez.Pos - rumo * (3 * ZoneCollision.TileSize));
						ContagemSoltaDeTeste = comDefeito;
						bool respondeu = false;
						try
						{
							for (int i = 0; i < 4 && !respondeu; i++)
							{
								TickDosCorposSemDono(Protocol.TickSeconds);
								respondeu = _canais.ContainsKey(vez.Id);
							}
						}
						finally { ContagemSoltaDeTeste = false; }
						feixe.Vivo = false;
						SoltarDoRaio(vez.Id);
						EsquecerAteAqui(censoAlem);
						if (comDefeito)
							Checa($"CONTRA-EXEMPLO (defeito injetado: o lutador da vez solto na contagem): {vez.Name}, antes do \"COMECEM!\", ve o feixe e RESPONDE",
								  respondeu, $"fase {FaseDoTorneioDeTeste}");
						else
							Checa($"...e sem o defeito, na contagem {vez.Name} fica parado: nao responde feixe (o `tourney_lock` do DM -- sem oponente designado, nada)",
								  !respondeu, $"fase {FaseDoTorneioDeTeste}");
					}
				}

				// ============================ O CONTRA-EXEMPLO DO REFLEXO, NA PRIMEIRA LUTA ============================
				// Um feixe do lutador da vez nasce a tres celulas de um preso da roda, vindo pra cima dele, e o mundo anda pelos
				// `Tiques` da bancada -- o reflexo e o do `TicarUmCorpo`, e nao uma chamada direta. COM O DEFEITO (o reflexo de volta
				// na espera) o preso responde e o censo acusa; SEM ele o mesmo feixe passa e ninguem responde. O preso ganha o Ki
				// Wave se nao o tiver: o que se mede e a trava, e nao o sorteio das tecnicas do molde. O que a cena fez (o feixe
				// dela, o grito do preso) sai da conta do torneio de verdade (`EsquecerAteAqui`).
				// =====================================================================================================
				if (!reflexoMedido && FaseDoTorneioDeTeste == "Luta" && _players.TryGetValue(LutadoresDeTeste.A, out ServerPlayer? atirador))
				{
					reflexoMedido = true;
					ServerPlayer? alvo = participantesAlem.Select(id => _players.GetValueOrDefault(id))
						.FirstOrDefault(n => n != null && !EhJogador(n) && PresosDeTeste.Contains(n.Id));
					if (alvo == null) Checa("PRECONDICAO: ha um NPC preso na roda pro contra-exemplo do reflexo", false);
					else
					{
						if (!SabeTecnica(alvo, "Ki_Wave")) DarSkillDoRaio(alvo);
						foreach (bool comDefeito in new[] { true, false })
						{
							alvo.Ficha.Ki = alvo.Ficha.MaxKi;
							_contraFeixeLivreEm.Remove(alvo.Id);
							var vem = new Vec2(1, 0);
							Projetil feixe = Disparar(atirador, RaioDeTeste(), rumoDado: vem, deOnde: alvo.Pos - vem * (3 * ZoneCollision.TileSize));
							var censoDaCena = new CensoDaArena();
							ContarNaArena(censoDaCena, participantesAlem);
							ContraFeixeNaEsperaDeTeste = comDefeito;
							try
							{
								for (int i = 0; i < 4 && !_canais.ContainsKey(alvo.Id); i++)
								{
									Tiques(1);
									ContarNaArena(censoDaCena, participantesAlem);
								}
							}
							finally { ContraFeixeNaEsperaDeTeste = false; }
							bool respondeu = _canais.ContainsKey(alvo.Id);
							feixe.Vivo = false;
							// COM O DEFEITO, QUALQUER PRESO QUE VIU UM FEIXE RESPONDEU (nao so o alvo da cena): a roda inteira larga o raio, senao
							// o canal de um vizinho seguia aberto os 12 s do prazo e o censo do torneio de verdade o contava como dele.
							// E QUEM RESPONDEU VIROU PRO FEIXE: de volta a cadeira, olhando pro palco, pelo `Segurar` de producao -- senao o censo o
							// contava sentado de costas pra arena pelo resto do torneio.
							foreach (int id in participantesAlem)
								if (id != LutadoresDeTeste.A && id != LutadoresDeTeste.B)
								{
									SoltarDoRaio(id);
									if (PresosDeTeste.Contains(id) && _players.TryGetValue(id, out ServerPlayer? preso)) Segurar(_torneio!, preso);
								}
							EsquecerAteAqui(censoAlem);
							if (comDefeito)
								Checa($"CONTRA-EXEMPLO (defeito injetado: o reflexo de volta na espera): {alvo.Name}, preso na roda, ve o feixe de {atirador.Name} e RESPONDE -- e o censo acusa",
									  respondeu && censoDaCena.CanaisForaDaLuta > 0, censoDaCena.ToString());
							else
								Checa($"...e sem o defeito o mesmo feixe passa por {alvo.Name} e ele NAO responde: a trava da espera vale pro reflexo",
									  !respondeu && censoDaCena.CanaisForaDaLuta == 0 && censoDaCena.FalasForaDaLuta == 0, censoDaCena.ToString());
						}

						// ============================ O EMBALO QUE SOBRA DA LUTA ============================
						// O golpe que decide uma luta ainda carrega o perdedor quando o `TerminarLuta` o devolve a espera. A cena: o mesmo preso
						// e arremessado PRA FORA da roda (longe da luta, que segue) pelo `Arremessar` de producao, e o `Segurar` de producao o
						// devolve ao lugar; o mundo anda meio segundo. A regua e o MECANISMO (o voo que sobra no corpo devolvido), e o quanto
						// ele andou vai no detalhe: quanto o corpo voa depende do chao que houver do lado de fora da roda.
						// ====================================================================================
						Vec2 lugarDeEspera = alvo.Pos;
						Vec2 praFora = (alvo.Pos - _torneio!.Arena.Centro).Normalized();
						foreach (bool comDefeito in new[] { true, false })
						{
							Arremessar(alvo, praFora, 1, 8);
							TransporteLevaOEmbaloDeTeste = comDefeito;
							try { Segurar(_torneio!, alvo); }
							finally { TransporteLevaOEmbaloDeTeste = false; }
							int embalo = alvo.TiquesDeVoo;
							Tiques(15);
							float saiu = Vec2.Distance(alvo.Pos, lugarDeEspera) / ZoneCollision.TileSize;
							Segurar(_torneio!, alvo);   // de volta ao lugar, pelo transporte que para (o de producao)
							if (comDefeito)
								Checa($"CONTRA-EXEMPLO (defeito injetado: o transporte de antes): {alvo.Name}, arremessado e devolvido a espera, continua VOANDO de la",
									  embalo > 0, $"{embalo} tique(s) de voo sobrando; andou {saiu:0.##} tiles em meio segundo");
							else
								Checa($"...e sem o defeito o transporte o poe PARADO no lugar de espera: o arremesso que sobrou da luta acaba ali",
									  embalo == 0 && saiu < 0.1f, $"{embalo} tique(s) de voo; {saiu:0.##} tiles fora do lugar");
						}

						// ============================ A CADEIRA (2026-10-17) ============================
						// O dono: *"coloque os participantes do torneio do outro mundo pra esperarem onde tem os bancos ali"*. O mesmo preso devolve
						// a cadeira (o lugar sai do `LugarDeEspera`) e o `Segurar` de producao o poe de novo, virado de costas pro palco antes. COM
						// O DEFEITO (a roda sorteada de antes) ele cai fora delas -- ou na de outro -- e o censo acusa; a roda e sorteada e pode cair
						// justo na cadeira que ele devolveu (uma vez em ~250), entao sao ate 5 sorteios. SEM ele, a primeira livre e a que ele
						// acabou de devolver: senta nela, olhando pra arena.
						// ===============================================================================
						cadeiraMedida = true;
						Vec2 cadeiraDele = _torneio!.LugarDeEspera[alvo.Id];
						Cadeira? sentou = _torneio!.Palco.CadeiraEm((int)MathF.Floor(cadeiraDele.X / ZoneCollision.TileSize), (int)MathF.Floor(cadeiraDele.Y / ZoneCollision.TileSize));
						if (sentou is not { } suaCadeira) Checa($"PRECONDICAO: {alvo.Name}, preso, estava numa cadeira", false, $"lugar {cadeiraDele}");
						else
						{
							var censoComDefeito = new CensoDaArena();
							for (int sorteio = 0; sorteio < 5 && censoComDefeito.ForaDaCadeira == 0; sorteio++)
							{
								_torneio!.LugarDeEspera.Remove(alvo.Id);
								EsperaSemCadeiraDeTeste = true;
								try { Segurar(_torneio!, alvo); }
								finally { EsperaSemCadeiraDeTeste = false; }
								ContarNaArena(censoComDefeito, participantesAlem);
							}
							Checa($"CONTRA-EXEMPLO (defeito injetado: a espera sem cadeira): {alvo.Name}, posto de novo na espera, fica FORA das cadeiras do palco -- e o censo acusa",
								  censoComDefeito.ForaDaCadeira > 0, censoComDefeito.ToString());
							Facing praArena = suaCadeira.OlharPara(_torneio!.Arena);
							_torneio!.LugarDeEspera.Remove(alvo.Id);
							alvo.Facing = praArena switch { Facing.East => Facing.West, Facing.West => Facing.East, Facing.North => Facing.South, _ => Facing.North };
							Segurar(_torneio!, alvo);
							var censoSemDefeito = new CensoDaArena();
							ContarNaArena(censoSemDefeito, participantesAlem);
							Checa($"...e sem o defeito {alvo.Name} senta na cadeira que devolveu (a primeira livre), virado pra arena ({praArena}), e o censo fica limpo",
								  Vec2.Distance(_torneio!.LugarDeEspera[alvo.Id], cadeiraDele) < 0.5f && alvo.Facing == praArena && censoSemDefeito is { ForaDaCadeira: 0, Sentados: > 0 },
								  $"lugar {_torneio!.LugarDeEspera[alvo.Id]} (era {cadeiraDele}), olhando {alvo.Facing}; {censoSemDefeito}");
						}
						EsquecerAteAqui(censoAlem);
					}
				}
			}
			GD.Print($"[torneio]   censo da arena do Outro Mundo: {censoAlem}");
			double nutricaoDepois = 0;
			foreach (int id in mortos) if (_players.TryGetValue(id, out ServerPlayer? n)) nutricaoDepois += n.Ficha.CurrentNutrition;
			Checa($"o torneio do Outro Mundo roda ate o fim ({tiquesAlem * Protocol.TickSeconds / 60:0.0} min de mundo) com os mortos lutando", !TorneioAtivo && tiquesAlem < teto, FaseDoTorneioDeTeste);
			Checa("...e a chave fechou com campeao (32 lutas)", ChaveDeTeste == null && chave != null);
			Checa("CENSO (Outro Mundo): tique a tique, so os dois da luta DENTRO da arena; quem nao lutava estava preso, na roda em volta (1 a 6 celulas da linha)",
				  censoAlem.Amostras > 0 && censoAlem.DentroSemLutar == 0 && censoAlem.SoltosForaDaLuta == 0 && censoAlem.EsperaLonge == 0, censoAlem.ToString());
			Checa("CENSO (Outro Mundo): os feixes da luta andaram de verdade, e NINGUEM fora da luta carregou feixe, atirou ou gritou (o \"Onda de Ki!!\" da roda)",
				  censoAlem.TirosDaLuta > 0 && censoAlem.CanaisForaDaLuta == 0 && censoAlem.TirosForaDaLuta == 0 && censoAlem.FalasForaDaLuta == 0,
				  censoAlem.ToString());
			Checa("CENSO (Outro Mundo): na CONTAGEM os NPCs da vez ficam no lugar, sem atirar nem falar (so o \"COMECEM!\" os solta um contra o outro)",
				  censoAlem.TirosNaContagem == 0 && censoAlem.FalasNaContagem == 0 && censoAlem.AndouNaContagem == 0, censoAlem.ToString());
			Checa("CENSO (Outro Mundo): nenhuma alma do alem (ninguem de fora do torneio) de pe no palco durante o torneio",
				  censoAlem.IntrusosNaArena == 0, censoAlem.ToString());
			Checa("CENSO (Outro Mundo): quem esperava estava SENTADO numa das 32 cadeiras em volta do palco (as do DM), cada um na sua, e os NPCs virados pra arena",
				  censoAlem.Sentados > 0 && censoAlem.ForaDaCadeira == 0, censoAlem.ToString());
			Checa("o contra-exemplo da cadeira rodou", cadeiraMedida);
			Checa("o contra-exemplo do povo no palco rodou", povoMedido);
			Checa("o contra-exemplo do reflexo rodou (houve luta no Outro Mundo)", reflexoMedido);
			Checa("o contra-exemplo da contagem rodou (houve contagem com um NPC da vez)", contagemMedida);
			Checa("o host continua morto no fim: o campeao do Outro Mundo NAO revive (config `campeaoRevive: false`)", pl.Ficha.dead);
			Checa("a agenda do Outro Mundo foi consumida (o proximo e a Terra quem marca)", AgendaDeTeste.ProximoDoAlemMs == 0);

			// =====================================================================
			// 4b) QUEM CUMPRE PENA NO INFERNO NAO VAI AO TORNEIO (2026-10-19)
			// =====================================================================
			// O Inferno conta como alem, e o convite ia pro condenado; a corrente da pena (`CumprirPenaNoInferno`, no `TickFichas` que o
			// `Tiques` anda) o devolvia ao portao no fechamento e de novo na hora da luta. O dono, perguntado: *"Nao pode"*.
			GD.Print("[torneio] -- 4b) quem cumpre pena no Inferno nao vai ao torneio --");
			{
				var inferno = ZoneKey.Premade(Alem.ZonaDoInferno);
				long penaDoHost = pl.Ficha.hell_lockout_until;
				try
				{
					MoveToZone(pl.Id, inferno, PortaoDoInferno(inferno));
					pl.Ficha.hell_lockout_until = NowMs() + 3_600_000;   // uma hora de pena
					ComandoDeTorneio(pl, "trn_iniciar", "alem");
					Avisos();
					ComandoDeTorneio(pl, "trn_participar", "");
					List<string> recusa = Avisos();
					Checa("PRECONDICAO: o torneio do Outro Mundo abriu com o host morto no INFERNO, cumprindo pena",
						  TorneioAtivo && FaseDoTorneioDeTeste == "Inscricao" && pl.Zone.Hash == inferno.Hash && pl.Ficha.dead, pl.Zone.Name);
					Checa("**QUEM CUMPRE PENA NAO SE INSCREVE**: o `trn_participar` do condenado e recusado, dizendo quanto falta",
						  InscritosDeTeste == 0 && recusa.Any(x => x.Contains("pena", StringComparison.Ordinal)), string.Join(" | ", recusa));

					// O CONTRA-EXEMPLO E O CAMINHO QUE O JOGADOR VIA: a regra de antes (a pena nao barra). O condenado entra; no fechamento o
					// torneio o leva pra cadeira, e o tique de ficha seguinte o devolve ao portao do Inferno.
					bool entrou, voltou;
					PenaNaoBarraOTorneioDeTeste = true;
					try
					{
						ComandoDeTorneio(pl, "trn_participar", "");
						entrou = InscritosDeTeste == 1;
						PularSegundos(6);
						Tiques(3);
						voltou = TorneioAtivo && pl.Zone.Hash == inferno.Hash && ChaveDeTeste?.Quem(pl.Assinatura) != null;
					}
					finally { PenaNaoBarraOTorneioDeTeste = false; }
					Checa("(defeito injetado: a pena nao barra o torneio) o condenado entra na chave, e no fechamento a corrente o puxa da cadeira de volta pro portao do Inferno -- dali ele nunca chega a arena",
						  entrou && voltou, $"inscrito {entrou}; depois do fechamento em {pl.Zone.Name}, na chave {ChaveDeTeste?.Quem(pl.Assinatura) != null}");
					if (TorneioAtivo) ComandoDeTorneio(pl, "trn_cancelar", "");

					// E QUEM E JULGADO DEPOIS DE SE INSCREVER sai no fechamento, avisado.
					pl.Ficha.hell_lockout_until = 0;
					MoveToZone(pl.Id, alem, MesaDoEnma(alem));
					ComandoDeTorneio(pl, "trn_iniciar", "alem");
					ComandoDeTorneio(pl, "trn_participar", "");
					bool inscritoSolto = InscritosDeTeste == 1;
					pl.Ficha.hell_lockout_until = NowMs() + 3_600_000;
					Avisos();
					PularSegundos(6);
					Tiques(2);
					List<string> corte = Avisos();
					Checa("...e quem e julgado pro Inferno DEPOIS de se inscrever sai da chave no fechamento, avisado",
						  inscritoSolto && ChaveDeTeste?.Quem(pl.Assinatura) == null && corte.Any(x => x.Contains("te tirou da chave", StringComparison.Ordinal)),
						  $"inscrito antes {inscritoSolto}; {string.Join(" | ", corte)}");
					if (TorneioAtivo) ComandoDeTorneio(pl, "trn_cancelar", "");
				}
				finally
				{
					pl.Ficha.hell_lockout_until = penaDoHost;
					if (pl.Zone.Hash != alem.Hash) MoveToZone(pl.Id, alem, MesaDoEnma(alem));
				}
			}

			// =====================================================================
			// 4c) A LUTA DO HOST CHAMADA COM O CLIENTE ATRASADO (2026-10-19)
			// =====================================================================
			// O dono: *"perto da arena mas fora da area dela, perdendo automaticamente ao iniciar a luta"*. O torneio leva o lutador da
			// cadeira pro ponto de luta, e ate a correcao chegar o cliente dele segue dizendo "estou na cadeira". Aqui os pacotes do
			// `GameClient.SendState` sao forjados no `AplicarInput` de producao, um por tique (30 por segundo), com a posicao da cadeira a
			// contagem INTEIRA: o pior cliente, que so le a chamada no "COMECEM!". Com um atraso menor o arrasto depende do lado e da
			// cadeira sorteados -- com 3 s, o host do lado B e a cadeira do outro lado do palco pararam meia celula pra dentro da linha
			// (rodada r3 do agente `torneioalem`) --, e a contagem inteira alcanca qualquer cadeira. A contagem e a de producao.
			GD.Print("[torneio] -- 4c) a luta do host chamada com o cliente atrasado --");
			{
				double contagemDaBancada = cfg.ContagemSegundos;
				(bool Chamado, bool Dentro, bool RingOut, string Onde) LutaComClienteAtrasado()
				{
					ComandoDeTorneio(pl, "trn_iniciar", "alem");
					ComandoDeTorneio(pl, "trn_participar", "");
					PularSegundos(6);
					Tiques(2);
					Vec2 cadeira = pl.Pos;
					// A CHAVE ANDA ATE A LUTA DO HOST: cada fase da luta alheia pulada pelo verbo de admin.
					for (int i = 0; i < 400 && TorneioAtivo && LutadoresDeTeste.A != pl.Id && LutadoresDeTeste.B != pl.Id; i++)
					{
						ComandoDeTorneio(pl, "trn_avancar", "");
						Tiques(1);
					}
					bool chamado = FaseDoTorneioDeTeste == "Contagem" && (LutadoresDeTeste.A == pl.Id || LutadoresDeTeste.B == pl.Id);
					Arena arena = cfg.Alem.Arena;
					int tiques = 0;
					while (chamado && FaseDoTorneioDeTeste == "Contagem" && tiques < 600)
					{
						AplicarInput(pl, pl.SeqInput + 1, (uint)NowMs(), cadeira, 0);
						Tiques(1);
						tiques++;
					}
					bool dentro = arena.ContemEm(pl.Pos);
					string onde = $"host em ({MathF.Floor(pl.Pos.X / ZoneCollision.TileSize)},{MathF.Floor(pl.Pos.Y / ZoneCollision.TileSize)}) no COMECEM, "
								+ $"a cadeira em ({MathF.Floor(cadeira.X / ZoneCollision.TileSize)},{MathF.Floor(cadeira.Y / ZoneCollision.TileSize)}), {tiques} tiques de contagem";
					Ouvidos();
					Tiques(1);   // o primeiro tique da luta: e nele que o juiz olha o ring-out
					bool ringOut = Ouvidos().Any(x => x.Contains("RING-OUT", StringComparison.Ordinal) && x.Contains(pl.Name, StringComparison.Ordinal));
					if (TorneioAtivo) ComandoDeTorneio(pl, "trn_cancelar", "");
					return (chamado, dentro, ringOut, onde);
				}
				try
				{
					cfg.ContagemSegundos = original.ContagemSegundos;
					(bool chamado, bool dentro, bool ringOut, string onde) = LutaComClienteAtrasado();
					Checa("PRECONDICAO: a chave chamou a luta do host, na contagem de producao", chamado, onde);
					Checa("**O CLIENTE ATRASADO NAO TIRA O LUTADOR DA ARENA**: a contagem inteira de pacotes ainda dizendo \"estou na cadeira\" depois da chamada, e no \"COMECEM!\" o host esta DENTRO, sem ring-out",
						  chamado && dentro && !ringOut, onde);
					TransporteComJanelaCurtaDeTeste = true;
					// (2026-10-27, o pedido 128) e a trava da contagem desligada JUNTO: com ela o host nem anda na contagem, e o arrasto que esta
					// secao mede nao teria como acontecer -- o defeito do transporte ficaria escondido atras da trava nova (a 4d mede a trava).
					ContagemDoJogadorSoltaDeTeste = true;
					try
					{
						(bool chamado2, bool dentro2, bool ringOut2, string onde2) = LutaComClienteAtrasado();
						Checa("(defeito injetado: o transporte abre so o meio segundo de todo salto) os pacotes velhos arrastam o host de volta pra cadeira na contagem, e o \"COMECEM!\" o pega fora -- RING-OUT, o relato do dono",
							  chamado2 && !dentro2 && ringOut2, onde2);
					}
					finally { TransporteComJanelaCurtaDeTeste = false; ContagemDoJogadorSoltaDeTeste = false; }
				}
				finally { cfg.ContagemSegundos = contagemDaBancada; }
			}

			// =====================================================================
			// 4d) A CONTAGEM TRAVA O JOGADOR DA VEZ (o pedido 128) E A ARENA E SO DOS DOIS (o pedido 129) -- 2026-10-27
			// =====================================================================
			// O dono: *"faça com q player tb n consiga se mover nem nada quando tiver na contagem do torneio no inicio da luta"* e *"n permita
			// jogadores de fora entrarem na arena ou conseguir bater nos participantes da luta"*. A primeira metade e a luta do HOST chamada, na
			// contagem de producao (os pacotes de andar e de subir forjados no `AplicarInput` de producao, como na 4c); a segunda e o host DE FORA
			// de uma luta de NPC contra NPC -- inscrito, mas solto da cadeira --, na contagem dela (os dois NPCs parados: nenhum golpe alheio).
			GD.Print("[torneio] -- 4d) a contagem trava o jogador da vez, e a arena e so dos dois --");
			{
				double contagemDaBancada = cfg.ContagemSegundos;
				List<(int Quem, string Id, long Ms)>? efeitosAntes = EscutaDeEfeitos;
				var efeitos = new List<(int Quem, string Id, long Ms)>();
				try
				{
					cfg.ContagemSegundos = original.ContagemSegundos;
					EscutaDeEfeitos = efeitos;

					// ---- (o pedido 128) a luta do host chamada
					ComandoDeTorneio(pl, "trn_iniciar", "alem");
					ComandoDeTorneio(pl, "trn_participar", "");
					PularSegundos(6);
					Tiques(2);
					for (int i = 0; i < 400 && TorneioAtivo && LutadoresDeTeste.A != pl.Id && LutadoresDeTeste.B != pl.Id; i++)
					{
						ComandoDeTorneio(pl, "trn_avancar", "");
						Tiques(1);
					}
					bool chamado = FaseDoTorneioDeTeste == "Contagem" && (LutadoresDeTeste.A == pl.Id || LutadoresDeTeste.B == pl.Id);
					bool travou = efeitos.Any(e => e.Quem == pl.Id && e.Id == "torneio_contagem" && e.Ms == -1);
					bool recusaNaContagem = NaContagemDoTorneio(pl) && RecusaOComandoDeCorpo(pl);
					Vec2 noPonto = pl.Pos;
					for (int i = 0; i < 10 && FaseDoTorneioDeTeste == "Contagem"; i++)
					{
						AplicarInput(pl, pl.SeqInput + 1, (uint)NowMs(), pl.Pos + new Vec2(6, 0), (byte)(Protocol.InputAndando | Protocol.InputSubir));
						Tiques(1);
					}
					float andouNaContagem = Vec2.Distance(pl.Pos, noPonto);
					bool subiu = pl.QuerSubir;
					Checa("PRECONDICAO 4d: a chave chamou a luta do host, na contagem de producao", chamado, FaseDoTorneioDeTeste);
					Checa("**(o pedido 128) NA CONTAGEM O JOGADOR DA VEZ NAO SE MOVE NEM AGE**: o passo e recusado, a tecla de subir nao vale, os comandos de corpo (soco, guarda, carga, tecnica) morrem no despacho, e o cliente recebe a trava (`torneio_contagem`)",
						  chamado && travou && recusaNaContagem && andouNaContagem < 1 && !subiu,
						  $"efeito {travou}, recusa {recusaNaContagem}, andou {andouNaContagem:0.#} px, subir {subiu}");

					// ...e o "COMECEM!" solta
					for (int i = 0; i < 400 && FaseDoTorneioDeTeste == "Contagem"; i++) Tiques(1);
					bool soltou = efeitos.Any(e => e.Quem == pl.Id && e.Id == "torneio_contagem" && e.Ms == 0);
					Vec2 noComecem = pl.Pos;
					for (int i = 0; i < 10; i++)
					{
						AplicarInput(pl, pl.SeqInput + 1, (uint)NowMs(), pl.Pos + new Vec2(6, 0), Protocol.InputAndando);
						Tiques(1);
					}
					float andouNaLuta = Vec2.Distance(pl.Pos, noComecem);
					Checa("...e no \"COMECEM!\" a trava sai: o passo anda, os comandos passam e o cliente e solto",
						  FaseDoTorneioDeTeste == "Luta" && soltou && !NaContagemDoTorneio(pl) && !RecusaOComandoDeCorpo(pl) && andouNaLuta > 4,
						  $"fase {FaseDoTorneioDeTeste}, efeito solto {soltou}, andou {andouNaLuta:0.#} px");
					if (TorneioAtivo) ComandoDeTorneio(pl, "trn_cancelar", "");
					Tiques(1);

					// ---- (o pedido 129) o host DE FORA de uma luta de NPC contra NPC, na contagem dela
					ComandoDeTorneio(pl, "trn_iniciar", "alem");
					ComandoDeTorneio(pl, "trn_participar", "");
					PularSegundos(6);
					Tiques(2);
					for (int i = 0; i < 400 && TorneioAtivo
						 && !(FaseDoTorneioDeTeste == "Contagem" && LutadoresDeTeste.A is not 0 && LutadoresDeTeste.B is not 0
							  && LutadoresDeTeste.A != pl.Id && LutadoresDeTeste.B != pl.Id); i++)
					{
						ComandoDeTorneio(pl, "trn_avancar", "");
						Tiques(1);
					}
					(int ia, int ib) = LutadoresDeTeste;
					bool deNpc = FaseDoTorneioDeTeste == "Contagem" && ia != pl.Id && ib != pl.Id && _players.ContainsKey(ia) && _players.ContainsKey(ib);
					Checa("PRECONDICAO 4d: uma luta de NPC contra NPC na contagem, com o host inscrito e fora dela", deNpc, $"{FaseDoTorneioDeTeste} {ia} x {ib}");
					if (deNpc && _torneio is { } tv)
					{
						ServerPlayer a = _players[ia], b = _players[ib];
						Soltar(tv, pl);   // o host sai da cadeira: e um jogador DE FORA da luta, livre pra andar
						Arena arena = tv.Arena;

						// A ARENA FECHADA: o host posto no meio dela
						Avisos();
						pl.Pos = arena.Centro;
						Tiques(1);
						List<string> avisosDaArena = Avisos();
						bool posto = !arena.ContemEm(pl.Pos) && avisosDaArena.Any(x => x.Contains("A arena é só dos dois", StringComparison.Ordinal));
						Checa("**(o pedido 129) O JOGADOR DE FORA NAO FICA NA ARENA**: o host posto no meio dela, na contagem de uma luta alheia, sai pelo lado mais perto, avisado",
							  posto, $"host em ({MathF.Floor(pl.Pos.X / ZoneCollision.TileSize)},{MathF.Floor(pl.Pos.Y / ZoneCollision.TileSize)}), arena {arena}; {string.Join(" | ", avisosDaArena)}");

						// QUEM BATE: o jogador de fora nao, o adversario sim -- e o NPC de fora fica de fora da regra (a letra do dono: jogadores)
						bool deForaNao = a.Combate.IntocavelPara(pl.Combate) && b.Combate.IntocavelPara(pl.Combate);
						bool adversarioSim = !a.Combate.IntocavelPara(b.Combate) && !b.Combate.IntocavelPara(a.Combate);
						ServerPlayer? npcDeFora = PresosDeTeste.Select(id => _players.GetValueOrDefault(id)).FirstOrDefault(c => c != null && !EhJogador(c));
						bool npcSim = npcDeFora != null && !a.Combate.IntocavelPara(npcDeFora.Combate);
						GolpeResultado soco = MeleeResolver.Resolver(pl.Combate, a.Combate, 0, new Random(7));
						Checa("**(o pedido 129) O JOGADOR DE FORA NAO ACERTA OS DOIS DA VEZ**: pros dois ele e intocavel, o adversario continua acertando, e o soco dele nem se resolve (o `Resolver`); o NPC de fora nao entra na regra",
							  deForaNao && adversarioSim && npcSim && soco.Desfecho == Desfecho.Errou && soco.Dano == 0,
							  $"de fora {deForaNao}, adversario {adversarioSim}, NPC de fora {npcSim} ({npcDeFora?.Name}), soco {soco.Desfecho}/{soco.Dano:0.##}");

						// O TIRO DE KI, pelo caminho de producao (`Disparar` + o tique dos tiros): o de fora atravessa o lutador, o do adversario acerta.
						// Alcance curto (4 tiles a partir de 3 tiles ao lado): o tiro que atravessa apaga antes de chegar em mais alguem.
						var bola = new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast, BaseDano = 0.05, Velocidade = 1, AlcanceTiles = 4, Deflectivel = false };
						Projetil tiroDeFora = Disparar(pl, bola, rumoDado: new Vec2(-1, 0), deOnde: a.Pos + Tiles(3, 0));
						for (int i = 0; i < 60 && tiroDeFora.Vivo; i++) Tiques(1);
						Projetil tiroDoAdversario = Disparar(b, bola, rumoDado: new Vec2(-1, 0), deOnde: a.Pos + Tiles(3, 0));
						for (int i = 0; i < 60 && tiroDoAdversario.Vivo; i++) Tiques(1);
						Checa("...e o TIRO DE KI do jogador de fora atravessa o lutador sem acertar; o do adversario, pelo mesmo caminho, acerta",
							  !tiroDeFora.Vivo && tiroDeFora.Fim != FimDeProjetil.Acertou && tiroDoAdversario.Fim == FimDeProjetil.Acertou,
							  $"de fora: {tiroDeFora.Fim}, adversario: {tiroDoAdversario.Fim}, fase {FaseDoTorneioDeTeste}");
					}
					if (TorneioAtivo) ComandoDeTorneio(pl, "trn_cancelar", "");
					Tiques(1);
				}
				finally
				{
					cfg.ContagemSegundos = contagemDaBancada;
					EscutaDeEfeitos = efeitosAntes;
				}
			}

			// =====================================================================
			// 5) A AGENDA PERSISTIDA
			// =====================================================================
			GD.Print("[torneio] -- 5) a agenda persistida --");
			long terraGravada = AgendaDeTeste.ProximoDaTerraMs;
			RecarregarAgendaDeTeste();
			Checa("o torneio.json da pasta de saves guarda o proximo da Terra e o ultimo (reler do disco da o mesmo)",
				  AgendaDeTeste.ProximoDaTerraMs == terraGravada && AgendaDeTeste.UltimoDaTerraMs > 0);
			ComandoDeTorneio(pl, "trn_status", "");
			List<string> status = Avisos();
			Checa("`trn_status` sem torneio diz quando e o proximo EM DIAS DO JOGO, com a espera de relogio ao lado (o \"28.5d\" do dono era dia de parede)",
				  status.Any(x => x.Contains("Terra em") && x.Contains("do jogo") && x.Contains("reais")), string.Join(" | ", status));

			// A AGENDA DE ANTES (dias de PAREDE, sem a marca) PASSA PRA DIAS DO JOGO NO BOOT, uma vez -- o mundo do dono, que tinha o
			// proximo da Terra a 28,4 dias. O `CarregarAgenda` e o do boot, lendo um arquivo escrito como o antigo era.
			{
				long agora = NowMs();
				long ultimo = agora - 2 * 86_400_000L;
				System.IO.File.WriteAllText(CaminhoDaAgenda,
					$"{{ \"ProximoDaTerraMs\": {ultimo + 30 * 86_400_000L}, \"ProximoDoAlemMs\": {ultimo + 15 * 86_400_000L}, \"UltimoDaTerraMs\": {ultimo} }}");
				_agenda = new AgendaDeTorneios();
				CarregarAgenda();
				long terraLida = AgendaDeTeste.ProximoDaTerraMs, alemLido = AgendaDeTeste.ProximoDoAlemMs;
				RecarregarAgendaDeTeste();
				Checa("o torneio.json de ANTES (dias de parede, sem a marca) passa pra DIAS DO JOGO no boot: o proximo da Terra fica 30 dias do jogo depois do ultimo, o do Outro Mundo 15 -- e o arquivo volta gravado com a marca",
					  terraLida == ultimo + 30 * diaDoJogo && alemLido == ultimo + 15 * diaDoJogo
					  && AgendaDeTeste.DiasDoJogo && AgendaDeTeste.ProximoDaTerraMs == terraLida && AgendaDeTeste.ProximoDoAlemMs == alemLido,
					  $"Terra a {(terraLida - agora) / 3_600_000.0:0.##} h e Outro Mundo a {(alemLido - agora) / 3_600_000.0:0.##} h de relogio (negativo = atrasado, abre no proximo tique)");

				System.IO.File.WriteAllText(CaminhoDaAgenda, $"{{ \"ProximoDaTerraMs\": {agora + 86_400_000L}, \"ProximoDoAlemMs\": 0, \"UltimoDaTerraMs\": 0 }}");
				_agenda = new AgendaDeTorneios();
				CarregarAgenda();
				Checa("...e o mundo que nunca teve torneio (so o primeiro marcado, a 1 dia de parede) passa a te-lo a 1 dia DO JOGO (24 min)",
					  Math.Abs(AgendaDeTeste.ProximoDaTerraMs - (agora + diaDoJogo)) < 5000 && AgendaDeTeste.DiasDoJogo,
					  $"{(AgendaDeTeste.ProximoDaTerraMs - agora) / 60_000.0:0.#} min");

				System.IO.File.WriteAllText(CaminhoDaAgenda,
					$"{{ \"ProximoDaTerraMs\": {agora + 10 * 86_400_000L}, \"ProximoDoAlemMs\": 0, \"UltimoDaTerraMs\": {ultimo}, \"DiasDoJogo\": true }}");
				_agenda = new AgendaDeTorneios();
				CarregarAgenda();
				Checa("...e a conversao e UMA VEZ: o arquivo ja marcado volta como esta (uma data longe, marcada na mao, nao e mexida)",
					  AgendaDeTeste.ProximoDaTerraMs == agora + 10 * 86_400_000L && AgendaDeTeste.DiasDoJogo);

				AgendaEmDiasReaisDeTeste = true;
				try
				{
					_agenda = new AgendaDeTorneios { DiasDoJogo = true };
					Reagendar(TipoDeTorneio.Terra, agora);
					Checa("CONTRA-EXEMPLO (defeito injetado: a agenda em dias de PAREDE): o `Reagendar` poe o proximo da Terra 30 dias de calendario adiante -- o \"28.5d\" que nao andava",
						  AgendaDeTeste.ProximoDaTerraMs == agora + 30 * 86_400_000L, $"{(AgendaDeTeste.ProximoDaTerraMs - agora) / 86_400_000.0:0.##} dia(s) de parede");
				}
				finally { AgendaEmDiasReaisDeTeste = false; }
				_agenda = new AgendaDeTorneios { DiasDoJogo = true };
				Reagendar(TipoDeTorneio.Terra, agora);
				Checa("...e sem o defeito o MESMO `Reagendar` marca 30 dias do jogo (12 h de relogio) e o do Outro Mundo 15 (6 h)",
					  AgendaDeTeste.ProximoDaTerraMs == agora + 30 * diaDoJogo && AgendaDeTeste.ProximoDoAlemMs == agora + 15 * diaDoJogo);
			}

			// =====================================================================
			// 6) CANCELAR SOLTA TODO MUNDO
			// =====================================================================
			GD.Print("[torneio] -- 6) cancelar --");
			pl.Ficha.dead = false;
			pl.MorteJaViajou = false;
			// DE VOLTA A TERRA, e nao "pra onde o host acordou": era a segunda dependencia do berco sorteado. Com
			// o host de Vegeta ou do exilio, a volta o deixava fora da Terra, o `trn_participar` daqui e o
			// `trn_inscrever` da secao 7 recusavam (`Elegivel`), o torneio sem inscrito era cancelado e o
			// `NpcsDoTorneioDeTeste[0]` do contra-exemplo da secao 7 estourava num vetor vazio -- a
			// `ArgumentOutOfRangeException` do fim do log. Voltar pra onde ele acordou e trabalho do `finally`.
			MoveToZone(pl.Id, zonaDaTerra, chegadaNaTerra);
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
				// ============================ O INTERVALO DE PRODUCAO, E NAO O DA BANCADA ============================
				// A bancada encurta o intervalo pra 1 s (o torneio inteiro da secao 3 cabe em segundos), e AQUI isso
				// reprovava por sorteio: o pouso da espera, la embaixo, anda 3 s de relogio, e com 1 s de intervalo a
				// luta SEGUINTE e chamada no meio dele -- e o `ProximaLuta` solta da espera os dois que vao lutar.
				// Quando a chave punha o host nessa luta (2 chances em 32), ele ja nao estava preso na hora do
				// `trn_cancelar`, e a linha do "cancelado" caia (95 OK / 1 FALHA, uma rodada em treze, 2026-09-24).
				// Com o intervalo de producao a luta seguinte nao e chamada durante o pouso; e o host que por sorteio
				// disputar a PRIMEIRA (a que o `trn_avancar` abre e encerra) volta pra espera pelo `TerminarLuta`,
				// que `Segurar`-a vencedor e perdedor. A chave sorteada deixa de importar.
				// ======================================================================================================
				const int tiquesDoPouso = 90;
				cfg.IntervaloSegundos = original.IntervaloSegundos;
				Checa("PRECONDICAO: o intervalo cobre o pouso da espera (a luta seguinte nao e chamada no meio dele)",
					  cfg.IntervaloSegundos > tiquesDoPouso * Protocol.TickSeconds,
					  $"intervalo {cfg.IntervaloSegundos:0.#} s, pouso {tiquesDoPouso * Protocol.TickSeconds:0.#} s");
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
					Tiques(tiquesDoPouso);
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

			// =====================================================================
			// 9) (2026-10-27, o pedido 124e) SAIR DO TORNEIO -- o documento de um jogador: *"Please let me leave tournaments please I
			// tried leaving the game and everything and those bastards kept me hostage for 30 minutes"*. Dois inscritos FORJADOS (corpo
			// com conta e sem `Peer`) fazem o sair do jogo: um volta na metade do prazo do W.O., o outro nao volta; o host usa o verbo.
			// =====================================================================
			GD.Print("[torneio] -- 9) sair do torneio: o sair do jogo, o verbo, o relogar e a vez de quem saiu (o pedido 124e) --");
			{
				const int idBaseDoSairDeTeste = 992_700;
				var forjados = new List<ServerPlayer>();
				ZoneKey terra = ZoneKey.Premade(cfg.Terra.Zona);
				ServerPlayer ForjarInscrito(int i, string nome)
				{
					var p = new ServerPlayer
					{
						Id = idBaseDoSairDeTeste + i, Peer = null, Name = nome, Race = "Human", Genero = "Male", Idade = 25, Zone = terra,
						Pos = pl.Pos, Conta = $"bancada_torneio_sair_{i}", Slot = 0,
						Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 10_000 }, Livro = new Jandirus.Core.Skills.SkillBook(),
					};
					p.Ficha.Class = "Normal";
					PorNoMundo(p);
					forjados.Add(p);
					return p;
				}
				void Sumir(ServerPlayer p) { _players.Remove(p.Id); ZoneList(p.Zone.Hash).Remove(p); }
				try
				{
					EscutaDeEfeitos = [];
					ComandoDeTorneio(pl, "trn_iniciar", "terra");
					ComandoDeTorneio(pl, "trn_participar", "");
					ServerPlayer fujao = ForjarInscrito(1, "bancada: o que sai do jogo");
					ServerPlayer volta = ForjarInscrito(2, "bancada: o que volta a tempo");
					TorneioVivo vivo = _torneio!;
					vivo.Inscritos[fujao.Assinatura] = fujao.Name;
					vivo.Inscritos[volta.Assinatura] = volta.Name;
					ComandoDeTorneio(pl, "trn_avancar", "");   // fecha as inscricoes: a chave e montada, todo mundo na espera
					Chave ch = vivo.Chave;
					Checa("9. PRECONDICAO: a chave tem o host e os dois forjados (fora dos NPCs), e o host esta preso na espera",
						  new[] { pl, fujao, volta }.All(h => ch.Quem(h.Assinatura) is { Npc: false }) && PresosDeTeste.Contains(pl.Id),
						  $"fase {FaseDoTorneioDeTeste}, {ch.Competidores.Count} competidores");

					// 9a/9b) O SAIR DO JOGO: os dois somem; um volta na metade do prazo do W.O., o outro nao volta
					Sumir(fujao);
					Sumir(volta);
					Tiques(1);
					PularSegundos(cfg.WoSegundos / 2);
					Tiques(1);
					PorNoMundo(volta);
					Tiques(1);
					PularSegundos(cfg.WoSegundos / 2 + 1);
					Ouvidos();
					Tiques(1);
					List<string> ditos = Ouvidos();
					Checa("9a. quem SAI DO JOGO e nao volta no prazo do W.O. esta FORA do torneio de vez",
						  vivo.Sairam.Contains(fujao.Assinatura), string.Join(" | ", ditos.Where(x => x.Contains(fujao.Name)).Take(3)));
					Checa("9b. ...e quem volta DENTRO do prazo continua nele (o contra-exemplo)", !vivo.Sairam.Contains(volta.Assinatura));

					// 9c) O VERBO
					bool estavaPreso = PresosDeTeste.Contains(pl.Id);
					EscutaDeEfeitos = [];
					Ouvidos();
					ComandoDeTorneio(pl, "trn_sair", "");
					List<string> saida = Ouvidos();
					Checa("9c. o SAIR DO TORNEIO (`trn_sair`) solta o host: fora da espera, o corpo anda, e o anuncio sai",
						  vivo.Sairam.Contains(pl.Assinatura) && !PresosDeTeste.Contains(pl.Id) && PodeMexerOCorpo(pl)
						  && (!estavaPreso || EscutaDeEfeitos.Any(e => e.Quem == pl.Id && e.Id == "torneio_espera" && e.Ms == 0))
						  && saida.Any(x => x.Contains("saiu do torneio")),
						  $"estava preso {estavaPreso}, preso agora {PresosDeTeste.Contains(pl.Id)}, anda {PodeMexerOCorpo(pl)}; {string.Join(" | ", saida.Take(2))}");

					// 9d) O RELOGAR
					AoEntrarNoTorneio(pl);
					Tiques(1);
					Checa("9d. quem saiu do torneio e RELOGA nao volta pra espera (era o 'refem' de meia hora)",
						  !PresosDeTeste.Contains(pl.Id) && PodeMexerOCorpo(pl));

					// 9e) A VEZ DE QUEM SAIU: a organizacao pula as fases ate a primeira luta dos tres estar decidida
					Luta? DoHumano(ServerPlayer h) => ch.Rodadas.Count > 0 ? ch.Rodadas[0].Lutas.Find(l => l.A == h.Assinatura || l.B == h.Assinatura) : null;
					bool hostLutou = false;
					for (int i = 0; i < 3000 && TorneioAtivo && !new[] { pl, fujao, volta }.All(h => DoHumano(h) is { Acabou: true }); i++)
					{
						if (FaseDoTorneioDeTeste is "Preparando" or "Intervalo" or "Contagem" or "Luta") ComandoDeTorneio(pl, "trn_avancar", "");
						Tiques(1);
						if (FaseDoTorneioDeTeste is "Contagem" or "Luta" && (LutadoresDeTeste.A == pl.Id || LutadoresDeTeste.B == pl.Id)) hostLutou = true;
					}
					// (os dois que sairam na MESMA luta: um deles "vence" por W.O. -- a chave precisa de um pra seguir)
					bool WoCerto(ServerPlayer h) => DoHumano(h) is { Acabou: true } l && l.Como.StartsWith("W.O.")
													 && (l.Vencedor != h.Assinatura || vivo.Sairam.Contains(l.Perdedor));
					Checa("9e. a vez de quem SAIU e W.O. na hora: o host (pelo verbo) e o que nao voltou perdem a 1a luta por W.O., e o host nao e chamado pra arena",
						  WoCerto(pl) && WoCerto(fujao) && !hostLutou,
						  $"host: {DoHumano(pl)?.Como} (vencedor {ch.NomeDe(DoHumano(pl)?.Vencedor ?? "")}); o que saiu do jogo: {DoHumano(fujao)?.Como}; host lutou {hostLutou}");
					if (TorneioAtivo) ComandoDeTorneio(pl, "trn_cancelar", "");
				}
				finally
				{
					foreach (ServerPlayer p in forjados) Sumir(p);
					EscutaDeEfeitos = null;
				}
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
			EscutaDeFalas = falasAntes;
			pl.Ficha.BP = bpDoHost;
			pl.Ficha.Statify();
			pl.Ficha.PowerLevel();
			pl.Ficha.Zeni = zeniDoHost;
			pl.Ficha.dead = mortoDoHost;
			pl.MorteJaViajou = viajouDoHost;
			pl.Combate.Letal = letalDoHost;
			pl.Combate.Carencia = 0;
			// DEVOLVIDO PRA ONDE ACORDOU, zona E lugar. Noutra zona (Vegeta, a orbita do exilio) volta pelo
			// `MoveToZone`; na mesma (o Low-Class, que ja acordava na Terra) so o lugar -- sem esta linha ele
			// entrava no jogo parado na area de espera da arena, onde o `Soltar` do cancelamento o deixa, e
			// nao na casa do berco. Escrever o `Pos` direto basta porque isto roda no 1o login, antes do
			// `JoinAccepted` que leva o `pl.Pos` ao cliente (o mesmo da `--alemteste`, `GameServer.AlemTeste.cs:1170`).
			if (pl.Zone.Hash != zonaDoHost.Hash) MoveToZone(pl.Id, zonaDoHost, posDoHost);
			else pl.Pos = posDoHost;
			if (puseu) naZona.Remove(pl);
		}
		GD.Print($"[torneio] ================ {ok} OK, {falhou} FALHA(S) ================");
	}

	/// <summary>
	/// (2026-10-17) O HOST MORTO NO OUTRO MUNDO, pro robo `--diagtorneio` fotografar a roda de espera do torneio celeste: o mesmo
	/// preparo da secao 4 desta bancada -- morto, com a viagem ja feita, levado a mesa do Enma pelo `MoveToZone` de producao.
	/// </summary>
	internal void PorNoOutroMundoDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		var alem = ZoneKey.Premade(_torneioCfg.Alem.Zona);
		pl.Ficha.dead = true;
		pl.MorteJaViajou = true;
		MoveToZone(pl.Id, alem, MesaDoEnma(alem));
		MandarFicha(pl);
	}

	// =====================================================================
	// O CENSO DA ARENA (2026-10-17)
	// =====================================================================
	/// <summary>
	/// QUEM ESTA ONDE, E QUEM AGE FORA DA LUTA -- o pedido do dono depois do caos do Outro Mundo: *"so os dois q estao lutando
	/// estao dentro dela, e os outros participantes nao estao usando ki e lutando fora de hora"*. Uma amostra por chamada de
	/// <see cref="ContarNaArena"/>: a bancada (`--torneioteste`) chama a cada tique, o robo `--diagtorneio` a cada quadro.
	///
	/// "FORA DA LUTA" E DE QUEM NAO LUTAVA NA AMOSTRA ANTERIOR NEM NESTA: os dois da luta que acabou de comecar ou de terminar
	/// ficam de fora, e um tiro dado DENTRO da luta, que segue voando depois dela, nao conta -- conta o tiro que NASCEU com o
	/// dono fora da luta (cada tiro e olhado uma vez, quando aparece).
	/// </summary>
	internal sealed class CensoDaArena
	{
		public int Amostras, DentroSemLutar, SoltosForaDaLuta, EsperaLonge, CanaisForaDaLuta, TirosForaDaLuta, FalasForaDaLuta, TirosDaLuta;
		/// <summary>A CONTAGEM ("5... 4..."): os dois da vez ja estao soltos e na arena, e nenhum NPC deles atira, fala ou sai do lugar.</summary>
		public int TirosNaContagem, FalasNaContagem, AndouNaContagem;
		/// <summary>
		/// O POVO NO PALCO: um NPC de fora do torneio (a alma, o habitante) de pe no piso do palco -- a arena e a borda dela
		/// (`PisoDoPalco`) --, da primeira contagem em diante.
		/// </summary>
		public int IntrusosNaArena;
		/// <summary>
		/// AS CADEIRAS (o palco do ceu): quem espera fora delas, dois na mesma, ou o NPC sentado de costas pra arena; e as amostras de
		/// gente sentada certa (zero = a regua nao viu ninguem sentar, e o zero do outro nao prova nada).
		/// </summary>
		public int ForaDaCadeira, Sentados;
		public int MenorFolga = int.MaxValue, MaiorFolga = int.MinValue;
		/// <summary>O PRIMEIRO CASO DE CADA TIPO -- um so pra tudo escondia o tiro de fora atras do primeiro que pisou na arena.</summary>
		public string PrimeiroDentro = "", PrimeiroSolto = "", PrimeiroLonge = "", PrimeiroCanal = "", PrimeiroTiro = "", PrimeiraFala = "";
		public string PrimeiroNaContagem = "", PrimeiroIntruso = "", PrimeiroForaDaCadeira = "";
		internal int FaseAntes = -1;   // a `FaseDoTorneio` da amostra anterior (o enum e privado do servidor)
		internal readonly HashSet<int> ForaAntes = [];
		internal readonly HashSet<int> TirosVistos = [];
		internal int FalasLidas;

		/// <summary>Tudo zerado: ninguem dentro sem lutar, ninguem solto, a roda no lugar, e nada de Ki nem grito de fora.</summary>
		public bool Limpo => DentroSemLutar == 0 && SoltosForaDaLuta == 0 && EsperaLonge == 0
									  && CanaisForaDaLuta == 0 && TirosForaDaLuta == 0 && FalasForaDaLuta == 0
									  && TirosNaContagem == 0 && FalasNaContagem == 0 && AndouNaContagem == 0 && IntrusosNaArena == 0 && ForaDaCadeira == 0;

		internal static void Anotar(ref string primeiro, string oque) { if (primeiro.Length == 0) primeiro = oque; }

		public override string ToString() =>
			$"{Amostras} amostra(s): {DentroSemLutar} dentro da arena sem lutar, {SoltosForaDaLuta} solto(s) fora da luta, {EsperaLonge} longe da roda; "
			+ $"de quem nao lutava {CanaisForaDaLuta} feixe(s) na mao, {TirosForaDaLuta} tiro(s) e {FalasForaDaLuta} fala(s); {TirosDaLuta} tiro(s) da luta; "
			+ $"na contagem {TirosNaContagem} tiro(s), {FalasNaContagem} fala(s) e {AndouNaContagem} amostra(s) fora do lugar; "
			+ $"{IntrusosNaArena} amostra(s) de gente de fora do torneio no palco; {ForaDaCadeira} fora da cadeira ({Sentados} sentado(s) certo); "
			+ (MaiorFolga >= MenorFolga ? $"a roda de {MenorFolga} a {MaiorFolga} celula(s) da linha" : "ninguem na roda")
			+ string.Concat(new[] { PrimeiroDentro, PrimeiroSolto, PrimeiroLonge, PrimeiroCanal, PrimeiroTiro, PrimeiraFala, PrimeiroNaContagem, PrimeiroIntruso, PrimeiroForaDaCadeira }
				.Where(p => p.Length > 0).Select(p => $" || {p}"));
	}

	/// <summary>A roda de espera: de 1 a 6 celulas FORA da linha da arena (o `PontoDaRoda` sorteia de 2 a 5; o recuo dele, 3 acima; as cadeiras do ceu ficam a 2).</summary>
	internal const int FolgaMaximaDaEspera = 6;

	/// <summary>Uma amostra do <see cref="CensoDaArena"/>. Sem torneio, ou com as inscricoes abertas (ninguem foi posto ainda), nao conta.</summary>
	internal void ContarNaArena(CensoDaArena c, IReadOnlyCollection<int> participantes)
	{
		if (_torneio is not { } t || t.Fase == FaseDoTorneio.Inscricao) return;
		c.Amostras++;
		(int la, int lb) = LutadoresDeTeste;
		Arena a = t.Arena;
		var foraAgora = new HashSet<int>();
		var sentadas = new Dictionary<(int, int), string>();   // cadeira -> quem esta nela, nesta amostra
		foreach (int id in participantes)
		{
			if (id == la || id == lb || !_players.TryGetValue(id, out ServerPlayer? p) || p.Zone.Hash != t.Zona.Hash) continue;
			foraAgora.Add(id);
			int cx = (int)MathF.Floor(p.Pos.X / ZoneCollision.TileSize), cy = (int)MathF.Floor(p.Pos.Y / ZoneCollision.TileSize);
			if (a.Contem(cx, cy)) { c.DentroSemLutar++; CensoDaArena.Anotar(ref c.PrimeiroDentro, $"{p.Name} DENTRO da arena sem lutar (fase {t.Fase}, celula {cx},{cy})"); }
			if (!t.Presos.Contains(id)) { c.SoltosForaDaLuta++; CensoDaArena.Anotar(ref c.PrimeiroSolto, $"{p.Name} SOLTO fora da luta (fase {t.Fase})"); }
			else
			{
				int folga = Math.Max(Math.Max(a.X1 - cx, cx - a.X2), Math.Max(a.Y1 - cy, cy - a.Y2));   // celulas ate a linha
				c.MenorFolga = Math.Min(c.MenorFolga, folga);
				c.MaiorFolga = Math.Max(c.MaiorFolga, folga);
				if (folga < 1 || folga > FolgaMaximaDaEspera)
				{
					c.EsperaLonge++;
					CensoDaArena.Anotar(ref c.PrimeiroLonge, $"{p.Name} espera a {folga} celula(s) da linha (fase {t.Fase}; arremesso {p.TiquesDeVoo}, agarrado por {p.AgarradoPorId})");
				}
				// AS CADEIRAS (o palco do ceu): quem espera esta SENTADO numa delas, cada um na sua, e o NPC olha pra arena (o jogador vira
				// pra onde quiser, parado).
				if (t.Palco.Cadeiras.Count > 0)
				{
					if (t.Palco.CadeiraEm(cx, cy) is not { } cadeira)
					{
						c.ForaDaCadeira++;
						CensoDaArena.Anotar(ref c.PrimeiroForaDaCadeira, $"{p.Name} espera FORA das cadeiras (celula {cx},{cy}, fase {t.Fase})");
					}
					else if (!sentadas.TryAdd((cx, cy), p.Name))
					{
						c.ForaDaCadeira++;
						CensoDaArena.Anotar(ref c.PrimeiroForaDaCadeira, $"{p.Name} e {sentadas[(cx, cy)]} na MESMA cadeira ({cx},{cy})");
					}
					else if (!EhJogador(p) && p.Facing != cadeira.OlharPara(a))
					{
						c.ForaDaCadeira++;
						CensoDaArena.Anotar(ref c.PrimeiroForaDaCadeira, $"{p.Name} sentado em ({cx},{cy}) olhando {p.Facing}, e nao pra arena ({cadeira.OlharPara(a)}; fase {t.Fase})");
					}
					else c.Sentados++;
				}
			}
			if (c.ForaAntes.Contains(id) && _canais.ContainsKey(id)) { c.CanaisForaDaLuta++; CensoDaArena.Anotar(ref c.PrimeiroCanal, $"{p.Name} com um feixe NA MAO fora da luta (fase {t.Fase})"); }
		}
		// O POVO NO PALCO: NPC de pe de fora do torneio no piso do palco -- a arena e a borda clara dela (o jogador espectador e livre,
		// como no DM; o cadaver e coisa). NA PREPARACAO NAO CONTA: a alma que estava no palco quando o torneio abriu SAI ANDANDO
		// (`ForaDoPalcoDoTorneio`), e leva um ou dois segundos dos dez da preparacao -- a `--diagtorneio` pegou uma (47 quadros). Da
		// primeira contagem em diante, um pe la dentro reprova.
		Arena piso = PisoDoPalco(a);
		if (t.Fase != FaseDoTorneio.Preparando)
			foreach (ServerPlayer b in ZoneList(t.Zona.Hash))
			{
				if (b.Id == la || b.Id == lb || EhJogador(b) || EhCadaver(b) || participantes.Contains(b.Id) || !piso.ContemEm(b.Pos)) continue;
				c.IntrusosNaArena++;
				int bx = (int)MathF.Floor(b.Pos.X / ZoneCollision.TileSize), by = (int)MathF.Floor(b.Pos.Y / ZoneCollision.TileSize);
				CensoDaArena.Anotar(ref c.PrimeiroIntruso, $"{b.Name} (de fora do torneio) NO PALCO (fase {t.Fase}, celula {bx},{by})");
			}
		// NA CONTAGEM, OS DOIS DA VEZ (os NPCs: o jogador anda e bate no DM tambem, o `release_hold` vem antes do "5...") FICAM NO
		// LUGAR em que o `ProximaLuta` os pos -- frente a frente, `PontoDoLutador`.
		bool naContagem = t.Fase == FaseDoTorneio.Contagem && c.FaseAntes == (int)FaseDoTorneio.Contagem;
		if (naContagem)
			foreach ((int id, int lado) in new[] { (la, 1), (lb, 2) })
			{
				if (!_players.TryGetValue(id, out ServerPlayer? f) || EhJogador(f)) continue;
				float fora = Vec2.Distance(f.Pos, PontoDoLutador(t, lado)) / ZoneCollision.TileSize;
				if (fora <= 0.25f) continue;
				c.AndouNaContagem++;
				CensoDaArena.Anotar(ref c.PrimeiroNaContagem, $"{f.Name} saiu do lugar na CONTAGEM ({fora:0.#} tiles)");
			}
		if (_projeteis.TryGetValue(t.Zona.Hash, out List<Projetil>? tiros))
			foreach (Projetil k in tiros)
			{
				if (!c.TirosVistos.Add(k.Id)) continue;
				if (k.Dono == la || k.Dono == lb)
				{
					if (!naContagem) { c.TirosDaLuta++; continue; }
					c.TirosNaContagem++;
					CensoDaArena.Anotar(ref c.PrimeiroNaContagem, $"{_players.GetValueOrDefault(k.Dono)?.Name ?? $"#{k.Dono}"} ATIROU na CONTAGEM: '{k.Nome}' ({k.Tipo})");
					continue;
				}
				if (!c.ForaAntes.Contains(k.Dono) || !foraAgora.Contains(k.Dono)) continue;
				c.TirosForaDaLuta++;
				CensoDaArena.Anotar(ref c.PrimeiroTiro, $"{_players.GetValueOrDefault(k.Dono)?.Name ?? $"#{k.Dono}"} ATIROU fora da luta: '{k.Nome}' ({k.Tipo}, fase {t.Fase})");
			}
		if (EscutaDeFalas is { } falas)
			for (; c.FalasLidas < falas.Count; c.FalasLidas++)
			{
				(int quem, _, string texto) = falas[c.FalasLidas];
				if (naContagem && (quem == la || quem == lb) && _players.TryGetValue(quem, out ServerPlayer? falante) && !EhJogador(falante))
				{
					c.FalasNaContagem++;
					CensoDaArena.Anotar(ref c.PrimeiroNaContagem, $"{falante.Name} disse \"{texto}\" na CONTAGEM");
					continue;
				}
				if (!c.ForaAntes.Contains(quem) || !foraAgora.Contains(quem)) continue;
				c.FalasForaDaLuta++;
				CensoDaArena.Anotar(ref c.PrimeiraFala, $"{_players.GetValueOrDefault(quem)?.Name ?? $"#{quem}"} disse \"{texto}\" fora da luta (fase {t.Fase})");
			}
		c.ForaAntes.Clear();
		c.ForaAntes.UnionWith(foraAgora);
		c.FaseAntes = (int)t.Fase;
	}

	/// <summary>
	/// O que uma cena da bancada fez ate aqui (o feixe que ela disparou, o grito do preso que respondeu) sai da conta do torneio
	/// de verdade: os tiros vivos ficam como ja vistos e as falas como ja lidas.
	/// </summary>
	internal void EsquecerAteAqui(CensoDaArena c)
	{
		if (_torneio is { } t && _projeteis.TryGetValue(t.Zona.Hash, out List<Projetil>? tiros))
			foreach (Projetil k in tiros) c.TirosVistos.Add(k.Id);
		c.FalasLidas = EscutaDeFalas?.Count ?? 0;
	}
}
