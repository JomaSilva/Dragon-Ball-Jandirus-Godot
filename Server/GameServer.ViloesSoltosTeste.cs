using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ BANCADA DOS VILOES SOLTOS (`--soltosteste`, 2026-10-31, o pedido 198g) ============================
/// Headless e com `--host` (o host e o "mais forte" e quem pisa no planeta invadido). Roda inteira no primeiro login, sem tique do mundo no
/// meio: o relogio anda pelo `AdiantoDoRelogioDeTeste` e o tique dos soltos e chamado a mao. A vitoria do chefe e o `GanchoDeDestruicao`
/// de verdade (num mundo de bancada: a destruicao acende e ninguem espera o pavio). Familias:
///   S1. o Freeza vence em Namek e fica SOLTO: invasor, com o exercito, num planeta vivo que nao e Namek -- e so o jornal do dia conta
///       (o pedido 200: o chat de ninguem);
///   S2. o Freeza do planeta Vegeta, nao (o pedido fala de Namek e da Terra);
///   S3. sem gente no planeta invadido, nao ha corpo; S4. o host chega e ele nasce -- o degrau, o BP pinado, chefe de saga, letal -- e o
///       exercito desembarca; S5. o soldado do imperio ataca o povo de qualquer raca (e nao o chefe); S6. a reposicao, um a cada 3 s;
///   S7. sem ninguem por um minuto, o corpo e a tropa somem (ele continua solto); S8. ele fica os DOIS dias do jogo no planeta (o pedido
///       200); S8b. (o pedido 206) no fim deles, NO MEIO DE UMA LUTA com o host, o prazo se estende; S8c. a luta acaba e ele SAI: o corpo e
///       a tropa somem, sumido, e so o jornal conta; S8d. tres dias do jogo depois (e nao antes), ataca outro planeta;
///   S9. o Cell vence e vai pro planeta do mais forte; S10. ele tambem fica dois dias, some, e dias depois vai atras do mais forte; S11. a
///       revanche: derrotado, sai do jogo de vez, e o
///       heroi ganha o karma; S12. o disco guarda os soltos.
///   (2026-11-02, o pedido 227) S13. o planeta da invasao e um PLANETA COM POVO (os pre-feitos povoados e os mundos com civilizacao), e
///       o mais forte num planeta sem povo nao atrai o Cell; S14. o corpo do vilao solto nasce com a maestria de gravidade de chefe (300x);
///       S15. o vilao solto caca o POVO do planeta quando nao ha jogador; S16. o Freeza so vai embora (o planeta fica); S17. o Cell, no fim
///       dos dois dias, EXPLODE o planeta que invadia (sem ninguem la, sem cena) -- e sem a regra (o jogo de antes) so iria embora.
///       (S9 e S10 mudaram de planeta: o mais forte agora esta em planetas com povo, Icer e Arlia.)
///   (2026-11-02, o pedido 228d) S18. o Freeza e o Cell soltos nascem na FORMA EXATA em que venceram a saga (o corpo do degrau da vitoria);
///       o Boo, na familia K da `--buuteste`.
/// ===============================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	private bool _soltosDeTeste;

	private void RodarBancadaDosSoltos(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA DOS VILOES SOLTOS =====");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		long adiantoAntes = AdiantoDoRelogioDeTeste;
		void Avancar(double segundos) => AdiantoDoRelogioDeTeste += (long)(segundos * 1000);
		bool Ouviu(string trecho) => EscutaDeAvisos?.Any(a => a.Contains(trecho, StringComparison.Ordinal)) ?? false;
		string Ultimos() => string.Join(" | ", (EscutaDeAvisos ?? []).TakeLast(3));
		ZoneKey Pre(string nome) => ZoneKey.Premade(nome);
		void Ir(ZoneKey z) => MoveToZone(pl.Id, z, PontoDeNascimento(z) + new Vec2(25 * ZoneCollision.TileSize, 15 * ZoneCollision.TileSize));

		// (2026-11-02, o pedido 228d) o corpo de cada chefe na hora da vitoria: o solto nasce NA FORMA EXATA em que venceu a saga (o S18)
		var formaDaVitoria = new Dictionary<string, (int Corpo, string Frost)>();
		bool NaFormaDaVitoria(string eloId, ServerPlayer? c) =>
			c != null && formaDaVitoria.TryGetValue(eloId, out var f) && c.Visual.Corpo == f.Corpo && (c.Visual.FormasDeFrost.FirstOrDefault() ?? "") == f.Frost;
		string FormaDiag(string eloId, ServerPlayer? c) =>
			$"{eloId}: {(c == null ? "sem corpo" : $"corpo {c.Visual.Corpo}, frost '{c.Visual.FormasDeFrost.FirstOrDefault()}'")}, na vitoria "
			+ (formaDaVitoria.TryGetValue(eloId, out var f) ? $"corpo {f.Corpo}, frost '{f.Frost}'" : "?");

		/// <summary>A vitoria do chefe deste elo, pela porta de producao: o corpo nasce no planeta da saga, no degrau e no BP pedidos, e o gancho roda.</summary>
		VilaoSolto? Vencer(string eloId, int degrau, double[] bps)
		{
			if (Array.Find(_cadeia, e => e.Id == eloId) is not { } elo || elo.Chefes.Length == 0) return null;
			string molde = elo.Chefes[^1].Molde;
			ZoneKey z = Pre(elo.Planeta);
			if (NascerNpc(molde, z, PontoDeNascimento(z), 0x5017_9000UL + (ulong)_soltos.Count) is not { Papel: { } papel } corpo) return null;
			papel.BpsPinados = bps;
			papel.Estagio = Math.Clamp(degrau, 0, Math.Max(papel.Molde.Estagios.Length - 1, 0));
			EntrarNoDegrau(corpo, cura: 0, anunciar: false);
			formaDaVitoria[eloId] = (corpo.Visual.Corpo, corpo.Visual.FormasDeFrost.FirstOrDefault() ?? "");
			EstadoDoElo est = _sagas.Do(elo.Id) ?? new EstadoDoElo { Id = elo.Id };
			var ec = new EstadoDoChefe { Molde = molde, Fase = FaseDoChefe.NoMundo, Corpo = corpo.Id };
			GanchoDeDestruicao(elo, est, ec, corpo);
			return _soltos.Find(v => v.Elo == eloId);
		}

		EscutaDeAvisos = [];
		SorteioDoPlanetaDeTeste = 0;
		ZoneKey casa = pl.Zone;
		try
		{
			GarantirOsSoltos();
			GarantirOJornal();
			// O host fica no planeta prisao (fora da carta e de qualquer sorteio) enquanto os chefes vencem: nem o "mais forte" o acha la.
			Ir(Pre(PatrulhaGalactica.ZonaDaPrisao));

			// ============================ S1. o Freeza de Namek fica solto
			double[] bpsDoFreeza = [2_000_000, 8_000_000, 30_000_000, 90_000_000];
			long chegouOFreeza = NowMs();   // a regua dos dois dias do S8 (e nao o prazo que o proprio codigo escreveu)
			VilaoSolto? freeza = Vencer("freeza_namek", 2, bpsDoFreeza);
			// (2026-10-31, o pedido 200) pra onde ele foi NAO vai pro chat de ninguem -- so pro jornal do dia (e pra missao da Patrulha)
			bool noJornal = NoticiasDoDiaDeTeste.Any(n => n.Tipo == TipoDeNoticia.VilaoAtacando && n.Texto.Contains("o império dele invade", StringComparison.Ordinal));
			Checa("(o pedido 198g) S1. **O FREEZA VENCE EM NAMEK E FICA SOLTO**: invasor, com o exercito, num planeta vivo que nao e Namek -- e pra "
				  + "onde ele foi sai no jornal do dia, e nao no chat de ninguem (o pedido 200)", freeza is { Jeito: ViloesSoltos.Jeito.Invasor, ComExercito: true, Degrau: 2 }
					 && !freeza.Zona.Equals(Pre("Namek")) && Espaco.EhPlaneta(freeza.Zona) && !ZonaMorta(freeza.Zona)
					 && freeza.Bps.SequenceEqual(bpsDoFreeza) && noJornal && !Ouviu("invade"),
				  $"{freeza?.Jeito} exercito={freeza?.ComExercito} degrau={freeza?.Degrau} em {freeza?.ZonaNome}; jornal={noJornal}; {Ultimos()}");

			// ============================ S0. o pulso desligado volta a andar (a licao da Patrulha: sem ele, nada anda sozinho no jogo vivo)
			_pulsoLigado = false;
			TickDaTropa();
			Checa("(o pedido 198g) S0. **O PULSO DOS SOLTOS SE REARMA**: desligado (o servidor ocioso), o tique da tropa o religa enquanto ha vilao solto",
				  _pulsoLigado);

			// ============================ S2. o de Vegeta nao
			int antes = _soltos.Count;
			Vencer("freeza_vegeta", 1, [1_000_000, 4_000_000]);
			Checa("(o pedido 198g) S2. **O FREEZA DO PLANETA VEGETA NAO FICA SOLTO** (o pedido fala de Namek e da Terra)",
				  _soltos.Count == antes && !_soltos.Any(v => v.Elo == "freeza_vegeta"), $"{_soltos.Count} solto(s)");
			if (freeza == null) return;

			// ============================ S3. sem gente, sem corpo
			TickDosSoltos();
			Checa("(o pedido 198g) S3. **SEM GENTE NO PLANETA INVADIDO, NAO HA CORPO**", freeza.Corpo == 0 && !_tropa.Values.Any(s => s.Elo == freeza.MarcaDaTropa));

			// ============================ S4. o host chega: o corpo e o desembarque
			EscutaDeAvisos.Clear();
			Ir(freeza.Zona);
			TickDosSoltos();
			ServerPlayer? corpo = _players.GetValueOrDefault(freeza.Corpo);
			List<ServerPlayer> soldados = [.. _tropa.Where(x => x.Value.Elo == freeza.MarcaDaTropa).Select(x => _players.GetValueOrDefault(x.Key)).OfType<ServerPlayer>()];
			Checa("(o pedido 198g) S4. **O HOST CHEGA E O FREEZA NASCE**: no degrau e no BP pinado em que venceu, chefe de saga (caca o planeta, Ki de "
				  + "chefe) e letal -- e o exercito desembarca", corpo is { Papel: { DaSaga: true } p } && p.Molde.Id == freeza.Molde && p.Estagio == 2
					 && p.BpsPinados.SequenceEqual(bpsDoFreeza) && corpo.Ficha.KiDeChefe && corpo.Combate.Letal && corpo.Zone.Equals(freeza.Zona)
					 && soldados.Count == ViloesSoltos.SoldadosNoDesembarque && soldados.TrueForAll(s => s.Zone.Equals(freeza.Zona))
					 && Ouviu("os soldados atacam a população"),
				  $"corpo={(corpo == null ? "nenhum" : $"{corpo.Papel?.Molde.Id} degrau {corpo.Papel?.Estagio}")}, {soldados.Count} soldado(s); {Ultimos()}");
			bool freezaNaForma = NaFormaDaVitoria("freeza_namek", corpo);   // o S18
			string freezaForma = FormaDiag("freeza_namek", corpo);

			// ============================ S5. o soldado ataca o povo de qualquer raca
			const int T = ZoneCollision.TileSize;
			ServerPlayer? povo = null;
			if (soldados.FirstOrDefault() is { } soldado)
			{
				povo = NascerNpc("cidadao", soldado.Zone, soldado.Pos + new Vec2(3 * T, 0), 0x5017_9900UL);
				if (corpo != null) corpo.Pos = soldado.Pos + new Vec2(-2 * T, 0);   // o chefe mais perto que o povo: ele nao e presa
				if (povo != null) { povo.Race = "Arlian"; }
				_tropa[soldado.Id].ProximaBusca = 0;
				_tropa[soldado.Id].Presa = 0;
				EscolherOAlvoDoSoldado(soldado, _tropa[soldado.Id]);
			}
			int presa = soldados.Count > 0 ? _tropa[soldados[0].Id].Presa : -1;
			// a presa e o povo -- o habitante de outra raca que a bancada pos ali, ou outro da Terra mais perto --, nunca o chefe nem um soldado
			ServerPlayer? alvoDoSoldado = _players.GetValueOrDefault(presa);
			bool doPovo = povo != null && alvoDoSoldado != null && alvoDoSoldado != corpo && !EhSoldado(alvoDoSoldado) && alvoDoSoldado.Papel?.DaSaga != true;
			Checa("(o pedido 198g) S5. **O SOLDADO DO IMPERIO ATACA O POVO DE QUALQUER RACA** (e nao o chefe dele)",
				  doPovo, $"presa #{presa} ({alvoDoSoldado?.Race}), povo #{povo?.Id}, chefe #{corpo?.Id}");

			// ============================ S14. (o pedido 227n) a maestria de gravidade de chefe
			Checa("(o pedido 227n) S14. **O VILAO SOLTO NASCE COM A MAESTRIA DE GRAVIDADE DE CHEFE** (300x: a invasao de um mundo pesado nao o esmaga)",
				  corpo != null && corpo.Ficha.GravMastered >= ViloesSoltos.MaestriaDeGravidadeDoChefe, $"{corpo?.Ficha.GravMastered}");

			// ============================ S14b. (o pedido 227n) todo chefe que nasce, pela porta de todo nascimento
			string? moldeDoChefe = _moldes?.Todos.FirstOrDefault(m => m.EhChefe)?.Id;
			ServerPlayer? chefeNovo = moldeDoChefe == null ? null : NascerNpc(moldeDoChefe, Pre(PatrulhaGalactica.ZonaDaPrisao), new Vec2(0, 0), 0x5017_9A00UL);
			double maestria = chefeNovo?.Ficha.GravMastered ?? 0;
			if (chefeNovo != null) RemoverNpc(chefeNovo);
			ChefeSemMaestriaDeTeste = true;
			ServerPlayer? semMaestria = moldeDoChefe == null ? null : NascerNpc(moldeDoChefe, Pre(PatrulhaGalactica.ZonaDaPrisao), new Vec2(0, 0), 0x5017_9A01UL);
			ChefeSemMaestriaDeTeste = false;
			double maestriaSem = semMaestria?.Ficha.GravMastered ?? 0;
			if (semMaestria != null) RemoverNpc(semMaestria);
			Checa("(o pedido 227n) S14b. **TODO CHEFE DE SAGA NASCE COM A MAESTRIA DE 300x** (a porta de todo nascimento: a saga, o Inferno, o wipe) -- "
				  + "e sem a regra (o jogo de antes) a de fabrica", maestria >= ViloesSoltos.MaestriaDeGravidadeDoChefe && maestriaSem < ViloesSoltos.MaestriaDeGravidadeDoChefe,
				  $"{moldeDoChefe}: {maestria}; sem a regra {maestriaSem}");

			// ============================ S15. (o pedido 227) o vilao solto caca o povo
			// o host sai do planeta por um instante (sem tique no meio: o corpo fica): sem jogador ali, a presa do chefe e o povo
			ZoneKey ondeOFreeza = freeza.Zona;
			Ir(Pre(PatrulhaGalactica.ZonaDaPrisao));
			ServerPlayer? doChefe = corpo == null ? null : PresaDoNpc(corpo);
			SoltoNaoCacaOPovoDeTeste = true;
			ServerPlayer? semARegra = corpo == null ? null : PresaDoNpc(corpo);
			SoltoNaoCacaOPovoDeTeste = false;
			Ir(ondeOFreeza);
			Checa("(o pedido 227) S15. **O VILAO SOLTO CACA O POVO DO PLANETA** (o dono: vai atacar os npcs nativos): sem jogador pra ele, a presa e o "
				  + "habitante mais perto -- e sem a regra (o jogo de antes, so atras de gente) nenhuma", doChefe is { Papel.Tipo: TipoDeNpc.Cidadao } && semARegra == null,
				  $"presa {doChefe?.Name} ({doChefe?.Papel?.Tipo}); sem a regra {semARegra?.Name}");
			if (povo != null) RemoverNpc(povo);

			// ============================ S6. a reposicao
			Avancar(ViloesSoltos.SegundosEntreSoldados + 0.5);
			TickDosSoltos();
			int depois = _tropa.Count(x => x.Value.Elo == freeza.MarcaDaTropa && _players.ContainsKey(x.Key));
			Checa("(o pedido 198g) S6. **O RESTO DO EXERCITO CHEGA UM A UM** (ate os 40)", depois == ViloesSoltos.SoldadosNoDesembarque + 1, $"{depois} soldado(s)");

			// ============================ S7. sem ninguem por um minuto, somem
			Ir(Pre(PatrulhaGalactica.ZonaDaPrisao));
			TickDosSoltos();
			bool aindaLa = freeza.Corpo != 0;
			Avancar(ViloesSoltos.SegundosSemNinguemPraSumir + 1);
			TickDosSoltos();
			Checa("(o pedido 198g) S7. **SEM NINGUEM NO PLANETA POR UM MINUTO, O CORPO E A TROPA SOMEM** -- e ele continua solto",
				  aindaLa && freeza.Corpo == 0 && corpo != null && !_players.ContainsKey(corpo.Id) && !_tropa.Values.Any(s => s.Elo == freeza.MarcaDaTropa)
				  && _soltos.Contains(freeza), $"ainda la no 1o segundo={aindaLa}, corpo={freeza.Corpo}");

			// ============================ S8. os dois dias do jogo no planeta (o pedido 200)
			// Medido da CHEGADA (um dia do jogo = 24 min reais): com 47 min ele ainda esta la -- o prazo velho, de um dia, ja o teria levado.
			ZoneKey antigo = freeza.Zona;
			EscutaDeAvisos.Clear();
			SorteioDoPlanetaDeTeste = 1;
			Avancar((chegouOFreeza + (2 * 24 - 1) * 60_000L - NowMs()) / 1000.0);
			TickDosSoltos();
			Checa("(o pedido 200) S8. **O INVASOR FICA DOIS DIAS DO JOGO NO PLANETA** (aos 47 min da chegada, ainda la)",
				  freeza.Zona.Equals(antigo) && !freeza.Sumido, $"em {freeza.ZonaNome}, sumido={freeza.Sumido}");

			// ============================ S8b. (o pedido 206) no fim dos dois dias, no meio de uma luta: o prazo se estende
			// O host volta ao planeta (o corpo nasce) e os dois estao brigando (a tag de briga dos dois, como um golpe trocado a arma).
			Ir(antigo);
			TickDosSoltos();
			ServerPlayer? doFreeza = _players.GetValueOrDefault(freeza.Corpo);
			if (doFreeza != null)
			{
				doFreeza.Pos = pl.Pos + new Vec2(3 * T, 0);
				doFreeza.Combate.EntrarEmCombate();
				pl.Combate?.EntrarEmCombate();
			}
			Avancar(60 + 5);   // os 48 min passaram
			TickDosSoltos();
			Checa("(o pedido 206) S8b. **NO FIM DOS DOIS DIAS, NO MEIO DE UMA LUTA, O PRAZO SE ESTENDE** (ele continua no planeta, de corpo e tudo)",
				  doFreeza != null && freeza.Zona.Equals(antigo) && !freeza.Sumido && freeza.Corpo == doFreeza.Id && _players.ContainsKey(doFreeza.Id),
				  $"corpo #{doFreeza?.Id}, sumido={freeza.Sumido}, em {freeza.ZonaNome}");

			// ============================ S8c. (o pedido 206) a luta acaba: ele SAI -- o corpo e a tropa somem, e so o jornal conta
			if (doFreeza != null) { doFreeza.Combate.EmCombate = 0; doFreeza.Combate.BrigandoComAlguem = 0; }
			EscutaDeAvisos.Clear();
			TickDosSoltos();
			long saiu = NowMs();
			bool saiuNoJornal = NoticiasDoDiaDeTeste.Any(n => n.Tipo == TipoDeNoticia.VilaoAtacando && n.Texto.Contains("ninguém sabe onde", StringComparison.Ordinal));
			Checa("(o pedido 206) S8c. **A LUTA ACABA E ELE SAI DO PLANETA**: o corpo e a tropa somem, ele fica sumido -- e so o jornal do dia conta",
				  freeza.Sumido && freeza.Corpo == 0 && doFreeza != null && !_players.ContainsKey(doFreeza.Id)
				  && !_tropa.Values.Any(s => s.Elo == freeza.MarcaDaTropa) && saiuNoJornal && !Ouviu("deixaram"),
				  $"sumido={freeza.Sumido}, corpo={freeza.Corpo}; jornal={saiuNoJornal}; {Ultimos()}");

			// ============================ S16. (o pedido 227o) o Freeza so vai embora
			Checa("(o pedido 227o) S16. **O FREEZA SO VAI EMBORA**: o planeta que ele invadia fica (quem explode e o Cell e o Boo)",
				  !ZonaMorta(antigo) && !ZonaCondenada(antigo), antigo.Name);

			// ============================ S8d. (o pedido 206) tres dias do jogo sumido (e nao antes), e ataca outro planeta
			// Medido da SAIDA: com 71 min ele ainda esta sumido; passados os 72, invade outro planeta (nunca o de onde saiu).
			Avancar((saiu + (3 * 24 - 1) * 60_000L - NowMs()) / 1000.0);
			TickDosSoltos();
			bool aindaSumido = freeza.Sumido;
			EscutaDeAvisos.Clear();
			Avancar(60 + 5);
			TickDosSoltos();
			bool voltouNoJornal = NoticiasDoDiaDeTeste.Any(n => n.Tipo == TipoDeNoticia.VilaoAtacando && n.Texto.Contains("voltaram: agora invadem", StringComparison.Ordinal));
			Checa("(o pedido 206) S8d. **TRES DIAS DO JOGO SUMIDO, E ELE ATACA OUTRO PLANETA** (aos 71 min, ainda sumido) -- e a volta sai no jornal, nao no chat",
				  aindaSumido && !freeza.Sumido && !freeza.Zona.Equals(antigo) && Espaco.EhPlaneta(freeza.Zona) && voltouNoJornal && !Ouviu("voltaram"),
				  $"sumido aos 71 min={aindaSumido}; {antigo.Name} -> {freeza.ZonaNome}; jornal={voltouNoJornal}; {Ultimos()}");

			// ============================ S9. o Cell vai pro planeta do mais forte
			// (2026-11-02, o pedido 227) o mais forte num planeta COM POVO: Icer (o Cell so fica com o planeta do mais forte se ele tem povo)
			ZoneKey doForte = Pre("Icer");
			Ir(doForte);
			long chegouOCell = NowMs();
			VilaoSolto? cell = Vencer("cell", 1, [5_000_000, 20_000_000]);
			Checa("(o pedido 198g) S9. **O CELL VENCE E VAI PRO PLANETA DO MAIS FORTE** (o host, em Icer: um planeta com povo)",
				  cell is { Jeito: ViloesSoltos.Jeito.Cacador, ComExercito: false } && cell.Zona.Equals(doForte), $"{cell?.Jeito} em {cell?.ZonaNome}");

			// ============================ S10. o Cell tambem fica dois dias (o pedido 200), e so entao vai atras do mais forte
			// O mais forte vai pra Icer logo; o Cell espera os 47 min em Arconia (antes do pedido 200 ele ia atras em dois minutos), sai nos 48
			// (o pedido 206: sem luta, o host esta em Icer) e, passados os dias sumido, vai atras dele em Icer.
			ZoneKey outro = Pre("Arlia");
			Ir(outro);
			SorteioDoPlanetaDeTeste = 0;
			TickDosSoltos();
			Avancar((chegouOCell + (2 * 24 - 1) * 60_000L - NowMs()) / 1000.0);
			TickDosSoltos();
			bool esperou = cell != null && cell.Zona.Equals(doForte) && !cell.Sumido;
			Avancar(60 + 5);
			TickDosSoltos();
			esperou &= cell is { Sumido: true };
			if (cell != null) Avancar((cell.VoltaEm - NowMs()) / 1000.0 + 1);
			TickDosSoltos();
			Checa("(o pedido 200) S10. **O CELL TAMBEM FICA DOIS DIAS NO PLANETA, SOME, E DIAS DEPOIS VAI ATRAS DO MAIS FORTE** (o host, agora em Arlia)",
				  esperou && cell != null && cell.Zona.Equals(outro), $"esperou={esperou}, Cell em {cell?.ZonaNome}");

			// ============================ S11. a revanche
			TickDosSoltos();
			ServerPlayer? doCell = cell == null ? null : _players.GetValueOrDefault(cell.Corpo);
			int karmaAntes = pl.Karma;
			bool cellNaForma = NaFormaDaVitoria("cell", doCell);   // o S18
			string cellForma = FormaDiag("cell", doCell);
			EscutaDeAvisos.Clear();
			if (doCell != null)
			{
				doCell.Ficha.KO = true;
				AoPerderALuta(doCell, pl, morreu: false);
			}
			Checa("(o pedido 198g) S11. **A REVANCHE**: o Cell derrotado sai do jogo de vez, o heroi ganha o karma de chefe derrotado, e o mundo ouve",
				  doCell != null && cell != null && !_soltos.Contains(cell) && !_players.ContainsKey(doCell.Id) && pl.Karma > karmaAntes
				  && Ouviu($"{pl.Name} derrotou {cell.Nome}") && NoticiasDoDiaDeTeste.Any(n => n.Tipo == TipoDeNoticia.VilaoDerrotado && n.Texto.Contains(cell.Nome)),
				  $"corpo={doCell?.Id}, karma {karmaAntes} -> {pl.Karma}; {Ultimos()}");

			// ============================ S12. o disco
			int quantos = _soltos.Count;
			SalvarOsSoltos();
			_soltos = [];
			_soltosCarregados = false;
			GarantirOsSoltos();
			Checa("(o pedido 198g) S12. **O DISCO GUARDA OS SOLTOS**: relido o `viloes-soltos.json`, o Freeza volta com o planeta, o degrau e o BP",
				  _soltos.Count == quantos && _soltos.Find(v => v.Elo == "freeza_namek") is { } relido && relido.Degrau == 2 && relido.Bps.SequenceEqual(bpsDoFreeza)
				  && relido.ZonaNome == freeza.ZonaNome && relido.VoltaEm == freeza.VoltaEm, $"{_soltos.Count}/{quantos}");

			// ============================ S18. (o pedido 228d) a forma exata da vitoria
			Checa("(o pedido 228d) S18. **O FREEZA E O CELL SOLTOS NASCEM NA FORMA EXATA EM QUE VENCERAM A SAGA** (*\"Freeza, cell e majin buu devem "
				  + "manter os corpos da exata forma q eles estavam quando venceram a saga\"*; o Boo, na familia K da `--buuteste`)",
				  freezaNaForma && cellNaForma, $"{freezaForma}; {cellForma}");

			// ============================ S13. (o pedido 227) o planeta da invasao e um planeta com povo
			List<ZoneKey> comPovo = PlanetasComPovo(null);
			var sorteados = new List<ZoneKey>();
			for (int i = 0; i < Math.Max(comPovo.Count, 1); i++)
			{
				SorteioDoPlanetaDeTeste = i;
				if (SortearPlanetaDeInvasao(null) is { } z) sorteados.Add(z);
			}
			SoltoSemPrioridadeDePovoDeTeste = true;
			var semPrioridade = new List<ZoneKey>();
			for (int i = 0; i < 40; i++)
			{
				SorteioDoPlanetaDeTeste = i;
				if (SortearPlanetaDeInvasao(null) is { } z) semPrioridade.Add(z);
			}
			SoltoSemPrioridadeDePovoDeTeste = false;
			Ir(Pre("Arconia"));
			ZoneKey? doMaisForte = PlanetaDoMaisForteComPovo(null);
			SorteioDoPlanetaDeTeste = 0;
			int civilizados = sorteados.Count(Civilizacao.Tem), preFeitos = sorteados.Count(z => z.Kind != ZoneKey.KindProcedural);
			Checa("(o pedido 227) S13. **O PLANETA DA INVASAO E UM PLANETA COM POVO** (o dono: dao prioridade pra planetas q tenham npcs): todo sorteio "
				  + "cai num pre-feito povoado ou num mundo com civilizacao (os dois aparecem), e o mais forte num planeta SEM povo (Arconia) nao atrai o "
				  + "Cell -- e sem a regra (o jogo de antes) o sorteio cai em planeta sem povo",
				  sorteados.Count > 0 && sorteados.All(TemPovo) && civilizados > 0 && preFeitos > 0
				  && doMaisForte is { } dm && !dm.Equals(Pre("Arconia")) && TemPovo(dm) && semPrioridade.Any(z => !TemPovo(z)),
				  $"{sorteados.Count} sorteio(s): {preFeitos} pre-feito(s), {civilizados} mundo(s) com civilizacao ({string.Join(", ", sorteados.Take(6).Select(z => z.Name))}); "
				  + $"o mais forte em Arconia -> {doMaisForte?.Name}; sem a regra, sem povo: {semPrioridade.Count(z => !TemPovo(z))}");

			// ============================ S17. (o pedido 227o) o Cell explode o planeta no fim dos dois dias
			(bool Explodiu, string Onde, bool Sumiu) CellNoFim(bool semARegraDeExplodir)
			{
				_soltos.RemoveAll(v => v.Elo == "cell");
				Ir(Pre(PatrulhaGalactica.ZonaDaPrisao));
				SorteioDoPlanetaDeTeste = semARegraDeExplodir ? 1 : 0;
				VilaoSolto? c = Vencer("cell", 1, [5_000_000, 20_000_000]);
				if (c == null) return (false, "nenhum", false);
				ZoneKey onde = c.Zona;
				// (a r1) o planeta do Cell tem de estar INTEIRO antes: a bancada ja explodiu Namek e Vegeta la em cima
				if (ZonaCondenada(onde) || ZonaMorta(onde)) return (false, onde.Name + " (ja explodindo antes!)", c.Sumido);
				SoltoNaoExplodeDeTeste = semARegraDeExplodir;
				try
				{
					Avancar((c.SaiEm - NowMs()) / 1000.0 + 61);
					TickDosSoltos();
				}
				finally { SoltoNaoExplodeDeTeste = false; }
				return (ZonaCondenada(onde) || ZonaMorta(onde), onde.Name, c.Sumido);
			}
			(bool explodiu, string ondeExplodiu, bool sumiu) = CellNoFim(false);
			(bool explodiuSemRegra, string ondeSemRegra, bool sumiuSemRegra) = CellNoFim(true);
			Checa("(o pedido 227o) S17. **O CELL EXPLODE O PLANETA NO FIM DOS DOIS DIAS** (o dono: ao passarem os 2 dias eles vao explodir o planeta "
				  + "q estao atacando): sem ninguem la, sem cena, o planeta entra na destruicao e ele some -- e sem a regra (o jogo de antes) so vai embora",
				  explodiu && sumiu && !explodiuSemRegra && sumiuSemRegra,
				  $"{ondeExplodiu}: explodiu {explodiu}, sumiu {sumiu}; sem a regra, {ondeSemRegra}: explodiu {explodiuSemRegra}, sumiu {sumiuSemRegra}");
		}
		catch (Exception e)
		{
			Checa("a bancada correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
		}
		finally
		{
			ZerarOsSoltos();
			SalvarOsSoltos();
			SoltoSemPrioridadeDePovoDeTeste = SoltoNaoExplodeDeTeste = SoltoNaoCacaOPovoDeTeste = false;
			SorteioDoPlanetaDeTeste = null;
			AdiantoDoRelogioDeTeste = adiantoAntes;
			MoveToZone(pl.Id, casa, PontoDeNascimento(casa));
			EscutaDeAvisos = null;
			// O PLACAR SAI DAQUI, e nao depois do `finally`: a familia que sai cedo (o Freeza que nao ficou solto) pulava a linha, e a fila
			// esperava o prazo inteiro por ela (a r3 do mutante b1).
			GD.Print($"===== BANCADA DOS SOLTOS: {ok} OK, {falhou} FALHA =====\n");
		}
	}
}
