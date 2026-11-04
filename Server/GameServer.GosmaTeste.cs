using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Races;
using Jandirus.Core.Verbos;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA GOSMA DO MAJIN (`--gosmateste`, 2026-11-01, o pedido 219). O jogador entra pela porta (a gosma so pega JOGADOR: e o `m.client`
/// do DU); o Majin e um corpo da foto (`MajinDaFoto`) que aperta o verbo pela porta de producao (`UsarHabilidade`); os passos andam pelo
/// `TickDasGosmas` de verdade, um tique de 30 Hz por vez:
///   A. O VERBO E O PRECO: a linha `hab:gosma` pela raca; o humano e recusado; o Majin larga -- escondida, oculta, intocavel, fora da grade,
///      com 70% do poder dele -- e fica com 73%; apertar de novo recolhe e devolve o poder;
///   B. A CACA: antes dos 4 s ela nao olha; aos 4 s, o jogador mais FORTE que ela nao e presa (nem o habitante, nem o dono); o mais fraco e --
///      ela aparece (a vista, tocavel) e vai atras;
///   C. GRUDA, E QUEM SOBE O PODER SE SOLTA: ela volta pro lugar e se esconde de novo;
///   D. ENGOLE E VOA: nove conferidas perdidas e o jogador vai DENTRO -- oculto, preso, sem comando, fora da grade, com o `LevadoPor` da
///      gosma e a ocupacao `NaGosma` --, e ela voa rapido pro Majin, com ele junto;
///   E. ENCOSTA E O MAJIN ABSORVE: o jogador no interior dele, registrado, a vista de novo -- a absorcao de sempre;
///   F. SAIR DA ZONA DESFAZ: o Majin muda de zona com o jogador dentro da gosma -- ela some, o jogador cai a vista e livre, o Majin volta ao poder;
///   G. O GOLPE: escondida ela e intocavel; a vista, um golpe a desfaz;
///   H. O SUPER BOO (o adendo 219h): o Fat nao larga; o Super larga sozinho (sem o preco); o Kid nao tem -- a do Super se desfaz na virada.
/// </summary>
public partial class GameServer
{
	private const string ContaDaBancadaDaGosma = "bancada_gosma_ag";

	private bool _gosmaRodando, _gosmaJaRodou;
	private int _gosmaOk, _gosmaFalhou;

	private void AfirmarGosma(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _gosmaOk++; GD.Print($"[gosma]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _gosmaFalhou++; GD.PrintErr($"[gosma]   FALHA {nome}   [{detalhe}]"); }
	}

	/// <summary>Anda o relogio das gosmas por <paramref name="segundos"/>, um tique de 30 Hz por vez.</summary>
	private void TiquesDaGosma(double segundos)
	{
		// ARREDONDADO, e nao pra cima: o tique e um `float`, e `3 x TickSeconds / TickSeconds` da 3,0000001 -- a r1 andou QUATRO tiques
		// onde pediu tres, e mediu o voo a 24 tiles/s.
		int n = (int)Math.Round(segundos / Protocol.TickSeconds);
		for (int i = 0; i < n; i++) TickDasGosmas();
	}

	/// <summary>Exatamente <paramref name="n"/> tiques de 30 Hz.</summary>
	private void TiquesDaGosmaN(int n)
	{
		for (int i = 0; i < n; i++) TickDasGosmas();
	}

	/// <summary>Anda o relogio ate a condicao (ou o teto), e diz se chegou.</summary>
	private bool TiquesDaGosmaAte(Func<bool> cond, double teto)
	{
		int n = (int)Math.Ceiling(teto / Protocol.TickSeconds);
		for (int i = 0; i < n; i++)
		{
			if (cond()) return true;
			TickDasGosmas();
		}
		return cond();
	}

	private void PoderDaBancadaDaGosma(ServerPlayer pl, double bp)
	{
		pl.Ficha.BP = bp;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
	}

	public void RodarBancadaDaGosma(int porta)
	{
		if (_gosmaRodando || _gosmaJaRodou) return;
		_gosmaRodando = _gosmaJaRodou = true;
		_gosmaOk = _gosmaFalhou = 0;
		GD.Print("[gosma] ================ A GOSMA DO MAJIN (dono, 2026-11-01, o pedido 219) ================");

		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		ServerPlayer? pl = null;
		double bpGuardado = 0;
		ZoneKey zonaGuardada = default;
		Vec2 posGuardada = default;
		try
		{
			pl = mesa.EntraNoMundo(cli, ContaDaBancadaDaGosma, "Goku");
			AfirmarGosma("(montagem) o cliente entra pela porta e e jogador", pl != null && EhJogador(pl));
			if (pl == null) return;
			bpGuardado = pl.Ficha.BP;
			zonaGuardada = pl.Zone;
			posGuardada = pl.Pos;

			int idDoMajin = MajinDaFoto(pl.Id, new Vec2(-3 * Tile, 0), 200_000);
			int idDoHabitante = ForjarCorpoDeFoto(pl.Id, new Vec2(1 * Tile, 1 * Tile), "Foto: o habitante", 100, comEscada: false);
			ServerPlayer? majin = _players.GetValueOrDefault(idDoMajin), habitante = _players.GetValueOrDefault(idDoHabitante);
			AfirmarGosma("(montagem) um Majin da foto (200 mil) e um habitante fraco ao lado do jogador", majin != null && habitante != null);
			if (majin == null || habitante == null) return;

			OVerboEOPrecoDaGosma(pl, majin);
			if (!ACacaDaGosma(pl, majin, habitante)) return;
			GrudaEQuemSobeSeSolta(pl, majin);
			if (!EngoleEVoa(pl, majin)) return;
			EncostaEOMajinAbsorve(pl, majin);
			SairDaZonaDesfazAGosma(pl, majin);
			OGolpeNaGosma(pl, majin);
			LimparAFotoDoMajin();
			ASuperBooEAGosma(pl);
		}
		catch (Exception e)
		{
			AfirmarGosma("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			foreach (GosmaPosta g in _gosmas.Values.ToArray()) DesfazerAGosma(g, _players.GetValueOrDefault(g.Dono), "");
			foreach (int id in _dentroDoMajin.Keys.ToList()) CuspirTodosDoMajin(id, "");
			LimparAFotoDoMajin();
			LimparAFoto();
			if (pl != null)
			{
				PalcoBoo_Desfazer(pl.Id);
				pl.Ficha.BP = bpGuardado;
				if (pl.Ficha.KO) pl.Combate.Curar();
				if (!pl.Zone.Equals(zonaGuardada)) MoveToZone(pl.Id, zonaGuardada, posGuardada);
			}
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaDaGosma + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch { }
			_gosmaRodando = false;
			GD.Print($"[gosma] ================ {_gosmaOk} OK, {_gosmaFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// A. O VERBO E O PRECO
	// =====================================================================
	private void OVerboEOPrecoDaGosma(ServerPlayer pl, ServerPlayer majin)
	{
		GD.Print("[gosma] --- A) o verbo e o preco ---");
		VerbDeclarado? linha = RequisitosDosVerbos.Linha("hab:gosma");
		AfirmarGosma("**A LINHA DO BOTAO** (`hab:gosma`) existe -- e o botao e de todo Majin, pela raca", linha != null, linha?.ToString() ?? "sem linha");

		List<string> doHumano = Ouvir(() => UsarHabilidade(pl, GosmaDoMajin.Verbo));
		AfirmarGosma("o HUMANO aperta e e recusado: so os Majins poem gosma", !TemGosma(pl.Id) && Disse(doHumano, GosmaDoMajin.RecusaNaoMajin),
					 string.Join(" | ", doHumano));

		majin.Ficha.PowerLevel();
		double antes = majin.Ficha.expressedBP;
		List<string> doMajin = Ouvir(() => UsarHabilidade(majin, GosmaDoMajin.Verbo));
		GosmaPosta? g = GosmaDoPalco(majin.Id);
		ServerPlayer? corpo = g != null ? _players.GetValueOrDefault(g.Corpo) : null;
		AfirmarGosma("**O MAJIN LARGA A GOSMA** onde esta: ESCONDIDA, oculta, intocavel, fora da grade de corpos, com a ocupacao `Gosma` -- e 70% do poder dele",
					 g != null && corpo != null && g.Passo == PassoDaGosma.Escondida && EstaOculto(corpo.Id) && corpo.Combate.Intocavel
					 && !EntraNaGrade(corpo) && OcupacaoDe(corpo) == Ocupacao.Gosma && (corpo.Pos - majin.Pos).Length < 1
					 && Math.Abs(g.Poder - antes * GosmaDoMajin.FracaoDoPoder) < 1 && Disse(doMajin, "gosma espreita"),
					 g == null ? "sem gosma" : $"passo {g.Passo}, oculta {EstaOculto(g.Corpo)}, poder {g.Poder:N0} de {antes:N0}");
		AfirmarGosma("**O PRECO**: com ela no mapa o Majin fica com 73% do poder",
					 Math.Abs(majin.Ficha.gosmaNoMapaMod - GosmaDoMajin.PoderDoMajinComAGosmaNoMapa) < 1e-9
					 && Math.Abs(majin.Ficha.expressedBP - antes * GosmaDoMajin.PoderDoMajinComAGosmaNoMapa) <= antes * 0.002,
					 $"{antes:N0} -> {majin.Ficha.expressedBP:N0}");

		List<string> recolhe = Ouvir(() => UsarHabilidade(majin, GosmaDoMajin.Verbo));
		AfirmarGosma("apertar DE NOVO recolhe: a gosma sai do mapa e o Majin volta ao poder inteiro",
					 !TemGosma(majin.Id) && (corpo == null || !_players.ContainsKey(corpo.Id)) && majin.Ficha.gosmaNoMapaMod == 1
					 && Math.Abs(majin.Ficha.expressedBP - antes) <= antes * 0.002 && Disse(recolhe, GosmaDoMajin.AoRecolher),
					 $"poder {majin.Ficha.expressedBP:N0}");
	}

	// =====================================================================
	// B. A CACA
	// =====================================================================
	private bool ACacaDaGosma(ServerPlayer pl, ServerPlayer majin, ServerPlayer habitante)
	{
		GD.Print("[gosma] --- B) a caca ---");
		UsarHabilidade(majin, GosmaDoMajin.Verbo);
		GosmaPosta? g = GosmaDoPalco(majin.Id);
		if (g == null) { AfirmarGosma("(montagem) a gosma de novo", false); return false; }
		Transportar(pl, pl.Zone, g.Lugar + new Vec2(3 * Tile, 0));
		Transportar(habitante, habitante.Zone, g.Lugar + new Vec2(2 * Tile, 0));

		PoderDaBancadaDaGosma(pl, g.Poder * 3);   // mais FORTE que ela
		TiquesDaGosma(GosmaDoMajin.SegundosAtePrimeiraOlhada - 0.2);
		bool aindaNaoOlhou = g.Passo == PassoDaGosma.Escondida && g.Presa == 0;
		TiquesDaGosma(0.4);
		AfirmarGosma("**ANTES DOS 4 s ELA NAO OLHA**; aos 4 s, o jogador MAIS FORTE que ela nao e presa -- nem o habitante (nao e jogador), nem o dono",
					 aindaNaoOlhou && g.Passo == PassoDaGosma.Escondida && g.Presa == 0, $"passo {g.Passo}, presa #{g.Presa}");

		PoderDaBancadaDaGosma(pl, 2_000);   // agora mais fraco
		g.Relogio = 0;
		TiquesDaGosmaN(1);
		ServerPlayer? corpo = _players.GetValueOrDefault(g.Corpo);
		AfirmarGosma("**O MAIS FRACO E PRESA**: ela o ve e APARECE -- a vista e tocavel -- e vai atras dele",
					 g.Passo == PassoDaGosma.Cacando && g.Presa == pl.Id && corpo != null && !EstaOculto(corpo.Id) && !corpo.Combate.Intocavel,
					 $"passo {g.Passo}, presa #{g.Presa} (jogador #{pl.Id})");
		return g.Passo == PassoDaGosma.Cacando;
	}

	// =====================================================================
	// C. GRUDA, E QUEM SOBE O PODER SE SOLTA
	// =====================================================================
	private void GrudaEQuemSobeSeSolta(ServerPlayer pl, ServerPlayer majin)
	{
		GD.Print("[gosma] --- C) gruda, e quem sobe o poder se solta ---");
		GosmaPosta g = GosmaDoPalco(majin.Id)!;
		List<string> grude = [];
		bool grudou = false;
		List<string> ouviu = Ouvir(() => grudou = TiquesDaGosmaAte(() => g.Passo == PassoDaGosma.Grudada, 3));
		grude.AddRange(ouviu);
		ServerPlayer? corpo = _players.GetValueOrDefault(g.Corpo);
		AfirmarGosma("**GRUDA**: chegando nele, ela fica em cima dele, e ele ouve que tem de subir o poder",
					 grudou && corpo != null && (corpo.Pos - pl.Pos).Length < 1 && Disse(grude, "grudou em você"), string.Join(" | ", grude));

		TiquesDaGosma(2.2);   // duas conferidas perdidas
		PoderDaBancadaDaGosma(pl, g.Poder * 2);
		List<string> solta = Ouvir(() => TiquesDaGosma(1.1));
		AfirmarGosma("**QUEM SOBE O PODER SE SOLTA** na conferida seguinte: ela o larga e volta pro lugar",
					 g.Passo is PassoDaGosma.Voltando or PassoDaGosma.Escondida && !NaGosma(pl.Id) && Disse(solta, "se livrou"),
					 $"passo {g.Passo}; {string.Join(" | ", solta)}");
		bool escondida = TiquesDaGosmaAte(() => g.Passo == PassoDaGosma.Escondida, 4);
		AfirmarGosma("...e la ela SE ESCONDE de novo (oculta e intocavel)",
					 escondida && corpo != null && EstaOculto(corpo.Id) && corpo.Combate.Intocavel && (corpo.Pos - g.Lugar).Length < 1);
		PoderDaBancadaDaGosma(pl, 2_000);
	}

	// =====================================================================
	// D. ENGOLE E VOA
	// =====================================================================
	private bool EngoleEVoa(ServerPlayer pl, ServerPlayer majin)
	{
		GD.Print("[gosma] --- D) engole e voa ---");
		GosmaPosta g = GosmaDoPalco(majin.Id)!;
		ServerPlayer corpo = _players[g.Corpo];
		// O MAJIN LONGE DO LUGAR DELA: o voo tem caminho.
		Transportar(majin, majin.Zone, g.Lugar + new Vec2(-10 * Tile, 0));
		Transportar(pl, pl.Zone, g.Lugar + new Vec2(2 * Tile, 0));
		g.Relogio = 0;
		bool grudou = TiquesDaGosmaAte(() => g.Passo == PassoDaGosma.Grudada, 3);
		TiquesDaGosma(GosmaDoMajin.Conferidas * GosmaDoMajin.SegundosPorConferida - 0.5);
		bool aindaGrudada = g.Passo == PassoDaGosma.Grudada;
		bool engoliu = TiquesDaGosmaAte(() => g.Passo == PassoDaGosma.Levando, 1.5);
		Vec2 antes = corpo.Pos;
		TiquesDaGosmaN(3);
		double tilesPorSegundo = (corpo.Pos - antes).Length / Tile / (Protocol.TickSeconds * 3);
		EntityState e = EstadoDe(pl, NowMs());
		AfirmarGosma("**NOVE CONFERIDAS PERDIDAS E ELA O ENGOLE** (nao antes): ele vai DENTRO -- oculto, sem andar, sem comando, fora da grade, com a "
					 + "ocupacao `NaGosma` e o `LevadoPor` da gosma (a camera dele vai junto)",
					 grudou && aindaGrudada && engoliu && NaGosma(pl.Id) && EstaOculto(pl.Id) && !PodeMexerOCorpo(pl) && RecusaOComandoDeCorpo(pl)
					 && !EntraNaGrade(pl) && OcupacaoDe(pl) == Ocupacao.NaGosma && e.LevadoPor == g.Corpo,
					 $"passo {g.Passo}, levado por #{e.LevadoPor} (gosma #{g.Corpo}), ocupacao {OcupacaoDe(pl)}");
		AfirmarGosma("**E VOA RAPIDO PRO MAJIN**, com ele junto (o corpo dele onde ela esta)",
					 Math.Abs(tilesPorSegundo - GosmaDoMajin.TilesPorSegundoVoando) < 0.5 && (pl.Pos - corpo.Pos).Length < 1
					 && (majin.Pos - corpo.Pos).Length < (majin.Pos - antes).Length,
					 $"{tilesPorSegundo:0.0} tiles/s (a caca anda {GosmaDoMajin.TilesPorSegundoCacando})");
		return engoliu;
	}

	// =====================================================================
	// E. ENCOSTA E O MAJIN ABSORVE
	// =====================================================================
	private void EncostaEOMajinAbsorve(ServerPlayer pl, ServerPlayer majin)
	{
		GD.Print("[gosma] --- E) encosta, e o Majin absorve ---");
		GosmaPosta g = GosmaDoPalco(majin.Id)!;
		bool entregou = TiquesDaGosmaAte(() => !NaGosma(pl.Id), 3);
		bool registrado = AbsorvidosPor(majin.Id).Any(r => r.Quem == pl.Id);
		AfirmarGosma("**AO ENCOSTAR, O MAJIN O ABSORVE** -- a absorcao de sempre: ele no interior do Majin, registrado, a vista de novo",
					 entregou && DentroDeUmMajin(pl) && InteriorDoMajin.Anfitriao(pl.Zone) == majin.Id && registrado && !EstaOculto(pl.Id)
					 && OcupacaoDe(pl) != Ocupacao.NaGosma,
					 $"zona {pl.Zone.Name}, registrado {registrado}");
		AfirmarGosma("...e ela volta pro lugar dela, e nao vai atras dele de novo em seguida (`goo_last_absorbed_key`)",
					 g.Passo is PassoDaGosma.Voltando or PassoDaGosma.Escondida && g.UltimoLevado == pl.Id, $"passo {g.Passo}");
		CuspirTodosDoMajin(majin.Id, "");
		TiquesDaGosmaAte(() => g.Passo == PassoDaGosma.Escondida, 4);
		g.UltimoLevado = 0;
	}

	// =====================================================================
	// F. SAIR DA ZONA DESFAZ
	// =====================================================================
	private void SairDaZonaDesfazAGosma(ServerPlayer pl, ServerPlayer majin)
	{
		GD.Print("[gosma] --- F) sair da zona desfaz ---");
		GosmaPosta g = GosmaDoPalco(majin.Id)!;
		int corpo = g.Corpo;
		if (pl.Ficha.KO) pl.Combate.Curar();
		Transportar(pl, pl.Zone, g.Lugar + new Vec2(2 * Tile, 0));
		g.Relogio = 0;
		TiquesDaGosmaAte(() => g.Passo == PassoDaGosma.Grudada, 3);
		g.Conferidas = GosmaDoMajin.Conferidas - 1;
		g.Relogio = 0;
		bool engoliu = TiquesDaGosmaAte(() => NaGosma(pl.Id), 1);
		ZoneKey terra = majin.Zone;
		var namek = ZoneKey.Premade("Namek");
		List<string> ouviu = Ouvir(() => MoveToZone(majin.Id, namek, PontoDeNascimento(namek)));
		AfirmarGosma("**O MAJIN SAI DA ZONA E A GOSMA SE DESFAZ**: ela some do mapa, quem ia dentro cai a vista e livre, e o Majin volta ao poder inteiro",
					 engoliu && !TemGosma(majin.Id) && !_players.ContainsKey(corpo) && !NaGosma(pl.Id) && !EstaOculto(pl.Id) && PodeMexerOCorpo(pl)
					 && pl.Zone.Equals(terra) && majin.Ficha.gosmaNoMapaMod == 1 && Disse(ouviu, "se desfez"),
					 $"engoliu {engoliu}, gosma {TemGosma(majin.Id)}, jogador oculto {EstaOculto(pl.Id)}; {string.Join(" | ", ouviu)}");
		MoveToZone(majin.Id, terra, pl.Pos + new Vec2(-3 * Tile, 0));
	}

	// =====================================================================
	// G. O GOLPE
	// =====================================================================
	private void OGolpeNaGosma(ServerPlayer pl, ServerPlayer majin)
	{
		GD.Print("[gosma] --- G) o golpe ---");
		UsarHabilidade(majin, GosmaDoMajin.Verbo);
		GosmaPosta? g = GosmaDoPalco(majin.Id);
		if (g == null || !_players.TryGetValue(g.Corpo, out ServerPlayer? corpo)) { AfirmarGosma("(montagem) a gosma de novo", false); return; }
		bool intocavelEscondida = corpo.Combate.Intocavel;
		Transportar(pl, pl.Zone, g.Lugar + new Vec2(3 * Tile, 0));
		g.Relogio = 0;
		TiquesDaGosmaN(1);
		bool aVistaTocavel = g.Passo == PassoDaGosma.Cacando && !corpo.Combate.Intocavel;
		// (2026-11-01) O DONO NAO A DESFAZ: o golpe dele passa por ela, o do jogador nao -- a pergunta que o soco, o tiro e o feixe fazem
		bool donoPassa = corpo.Combate.IntocavelPara(majin.Combate), outroBate = !corpo.Combate.IntocavelPara(pl.Combate);
		AfirmarGosma("**O DONO NAO DESFAZ A PROPRIA GOSMA**: a vista, o golpe DELE passa por ela (`IntocavelPara`), e o do jogador nao",
					 aVistaTocavel && donoPassa && outroBate, $"tocavel a vista {aVistaTocavel}, o dono passa {donoPassa}, o jogador bate {outroBate}");
		corpo.Ficha.HP -= 5;   // o golpe -- a vida da gosma a vista e a do `Health = 1`: qualquer dano
		List<string> ouviu = Ouvir(() => TiquesDaGosmaN(1));
		AfirmarGosma("**ESCONDIDA E INTOCAVEL; A VISTA, UM GOLPE A DESFAZ** -- e o Majin ouve e volta ao poder inteiro",
					 intocavelEscondida && aVistaTocavel && !TemGosma(majin.Id) && !_players.ContainsKey(corpo.Id) && majin.Ficha.gosmaNoMapaMod == 1
					 && Disse(ouviu, "destruída"),
					 $"intocavel escondida {intocavelEscondida}, tocavel a vista {aVistaTocavel}; {string.Join(" | ", ouviu)}");
	}

	// =====================================================================
	// H. O SUPER BOO
	// =====================================================================
	private void ASuperBooEAGosma(ServerPlayer pl)
	{
		GD.Print("[gosma] --- H) o Super Boo (o adendo 219h) ---");
		int idDoBoo = PalcoBoo_Nascer(pl.Id);
		if (!_players.TryGetValue(idDoBoo, out ServerPlayer? boo)) { AfirmarGosma("(montagem) o Boo nasceu pela cadeia", false); return; }
		for (int i = 0; i < 30; i++) TickDoBoo();
		bool gordoSemGosma = !TemGosma(boo.Id);

		PalcoBoo_Dividir();
		TickDoBoo();
		bool virouSuper = boo.Papel?.Estagio == SagaDoBuu.DegrauSuper;
		boo.CenaSegundos = 0;   // a cena da divisao nao e desta bancada
		for (int i = 0; i < (int)GosmaDoMajin.SegundosAteAGosmaDoSuperBoo + 2 && !TemGosma(boo.Id); i++) TickDoBoo();
		GosmaPosta? g = GosmaDoPalco(boo.Id);
		AfirmarGosma("**O FAT NAO LARGA GOSMA; O SUPER LARGA SOZINHO** -- e sem o preco: o poder dele e o da saga",
					 gordoSemGosma && virouSuper && g != null && !g.CobraDoDono && boo.Ficha.gosmaNoMapaMod == 1,
					 $"gordo sem gosma {gordoSemGosma}, super {virouSuper}, gosma {(g == null ? "nenhuma" : $"#{g.Corpo}")}");

		_booViraKid.Add(boo.Id);
		TickDoBoo();
		AfirmarGosma("**O KID NAO TEM GOSMA**: a do Super se desfaz na virada",
					 boo.Papel?.Estagio == SagaDoBuu.DegrauKid && !TemGosma(boo.Id) && (g == null || !_players.ContainsKey(g.Corpo)),
					 $"degrau {boo.Papel?.Estagio}");
		_fugaDoKid = null;
		PalcoBoo_Desfazer(pl.Id);
	}
}
