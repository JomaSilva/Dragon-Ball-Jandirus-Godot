using System.Text.Json;
using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA DRILL (`--drillteste`) -- o pedido do dono de 2026-10-10: *"a maquina "Drill" de tech 30 fica
/// pegando recursos de forma automatica no planeta q foi instalada, vc pode investir mais recurso ou zenni no
/// menu de interaçao pra deixar ela melhor e pegar mais recursos por segundo"*, e a consequencia que ele
/// escreveu das rochas: *"se alguem usar uma drill e zerar o planeta..."*.
///
/// No boot, como a `--maquinasteste`: precisa das zonas com colisao e de ninguem logado. Os corpos sao
/// forjados (sem `Peer`) e TUDO passa pelo funil de producao -- a Drill e assentada pelo `posicionar`,
/// coletada, melhorada e recolhida pelos verbos do menu da tecla E (`ComandoDeInteracao`), e quem conta a
/// producao e o `PorTodasAsDrillsEmDia` que o tique chama. A reserva do planeta e a de VERDADE (a da Terra,
/// pelo contrato da economia: `ReservaDe`), posta no valor de cada cena so pelas duas portas dela
/// (`Regenerar` e `Tirar`).
///
/// O TEMPO DA BANCADA E EXATO: `DrMarcar` zera o cronometro de todas as Drills num instante conhecido e
/// `DrPassar` adianta o relogio e entrega a conta de producao AQUELE instante -- "100 s" sao cem segundos, e
/// nao cem segundos mais o que a maquina demorou. O tique com o relogio de parede tem a sua propria cena (a 12).
///
/// DEZ DEFEITOS INJETADOS, um por regra que so existe por uma linha: a conta que so enxerga um tique
/// (`Drill.ContaSoOTiqueDeTeste`), o carimbo que nao anda (`CarimboParadoDeTeste`), o estoque sem teto
/// (`EstoqueSemTetoDeTeste`), o teto que corta o que ja estava (`TetoCortaOQueJaEstavaDeTeste`), a coleta que nao esvazia (`ColetaNaoEsvaziaDeTeste`), o recurso que vem do nada
/// (`TiraDoNadaDeTeste`), a primeira que leva tudo (`PrimeiraLevaTudoDeTeste`), a Drill de qualquer um
/// (`DrillDeQualquerUmDeTeste`), a melhoria sem pagar (`DrillMelhoraSemPagarDeTeste`) e a recolhida que perde
/// tudo (`DrillRecolhidaPerdeTudoDeTeste`).
///
///     Godot --headless -- --server --port 8120 --drillteste
/// </summary>
public sealed partial class GameServer
{
	private int _drOk, _drFalhou;

	/// <summary>O relogio da bancada (ms): o instante que a ultima conta de producao recebeu.</summary>
	private long _drRelogio;

	private void AfirmarDr(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _drOk++; GD.Print($"[drill]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _drFalhou++; GD.PrintErr($"[drill]   FALHA {nome}   [{detalhe}]"); }
	}

	private static bool DrPerto(double a, double b) => Math.Abs(a - b) < 1e-6;

	private static Vec2 DrNo(int cx, int cy) =>
		new(cx * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f,
			cy * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f - MoveRules.FeetOffsetY);

	/// <summary>ZERA O CRONOMETRO: toda Drill passa a estar contada ate AGORA, e o relogio da bancada marca esse instante.</summary>
	private void DrMarcar()
	{
		_drRelogio = NowMs();
		foreach (Obra o in _noChao)
			if (o.Drill is { } d) d.ContadoAteMs = _drRelogio;
	}

	/// <summary>
	/// PASSAM-SE N SEGUNDOS, exatos: o relogio de parede adianta junto (os verbos do menu leem `NowMs`), e a
	/// conta de PRODUCAO -- a mesma que o tique chama -- recebe o instante exato.
	/// </summary>
	private void DrPassar(double segundos)
	{
		long ms = (long)Math.Round(segundos * 1000);
		AdiantoDoRelogioDeTeste += ms;
		_drRelogio += ms;
		PorTodasAsDrillsEmDia(_drRelogio);
	}

	/// <summary>
	/// A RESERVA NUM VALOR EXATO, so pelas duas portas do contrato: enche (`Regenerar`) e tira a diferenca
	/// (`Tirar`). A bancada nao mexe por dentro do que e da economia.
	/// </summary>
	private static void DrPorAReservaEm(ReservaDePlaneta r, double atual)
	{
		r.Regenerar(Recurso.HorasPraEncher * 3600.0 * 2);
		r.Tirar(r.Maximo - Math.Clamp(atual, 0, r.Maximo));
	}

	public void RodarBancadaDaDrill()
	{
		_drOk = _drFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[drill] ================ A DRILL: extrai sozinha, guarda, e se melhora (dono, 2026-10-10) ================");

		ZoneKey terra = ZonaDaBancadaDeProjetil;
		ZoneKey mirante = ZoneKey.Premade("Lookout");
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(terra);
		ReservaDePlaneta? reserva = ReservaDe(terra);

		// A FOTO DO MUNDO DE VERDADE: as obras, o proximo id, o relogio e a reserva voltam no `finally`.
		List<Obra> obrasDeAntes = [.. _noChao];
		int idDeAntes = _proximaObraId;
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		double reservaDeAntes = reserva?.Atual ?? 0;
		string arquivo = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jandirus-drillteste.json");

		try
		{
			AfirmarDr("(montagem) a Terra tem mapa, o catalogo tem a Drill, e o planeta tem RESERVA (o contrato da economia)",
					  mapa != null && _obras?.Get(Drill.Tipo) != null && reserva is { Maximo: > 0 },
					  $"reserva {reserva?.Atual:0} de {reserva?.Maximo:0}");
			if (mapa == null || _obras?.Get(Drill.Tipo) == null || reserva is not { Maximo: > 0 }) return;

			DrPorAReservaEm(reserva, reserva.Maximo);   // cheia: toda cena mede do mesmo ponto
			(int px, int py) = PracaDaBancada(mapa);

			DrDoMenuEDaCurva();
			(Obra? drill, ServerPlayer dona) = DrDaInstalacao(px, py);
			if (drill?.Drill == null) return;

			DrDaExtracao(drill, reserva);
			DrDoTeto(drill, reserva);
			ServerPlayer vizinho = DrDaColeta(drill, dona, px, py);
			DrDasMelhorias(drill, dona);
			DrDoPlanetaSeco(drill, dona, reserva);
			DrDasDuas(drill, vizinho, px, py, reserva);
			DrDoFio(drill, dona, reserva);
			drill = DrDeRecolher(drill, dona, px, py, reserva);
			DrDoLugarSemReserva(mirante);
			if (drill?.Drill != null) DrDoTempoSemNinguem(drill, reserva, arquivo);
		}
		finally
		{
			Drill.ContaSoOTiqueDeTeste = Drill.CarimboParadoDeTeste = Drill.EstoqueSemTetoDeTeste = false;
			Drill.ColetaNaoEsvaziaDeTeste = Drill.TiraDoNadaDeTeste = Drill.PrimeiraLevaTudoDeTeste = false;
			Drill.TetoCortaOQueJaEstavaDeTeste = false;
			DrillDeQualquerUmDeTeste = DrillMelhoraSemPagarDeTeste = DrillRecolhidaPerdeTudoDeTeste = false;
			EscutaDeDrill = null;

			// O RELOGIO VOLTA, e os dois prazos medidos contra o relogio adiantado voltam com ele.
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			_proximaLeituraDasDrills = _proximaGravacaoDasDrills = 0;

			// O MUNDO VOLTA: as obras de antes da bancada (as MESMAS instancias), e nada dela no disco.
			_noChao.Clear();
			_noChao.AddRange(obrasDeAntes);
			_proximaObraId = idDeAntes;
			_obrasNaUltimaVoltaDasDrills = -1;
			GravarMundo();
			AplicarColisaoDasObras(terra);
			AplicarColisaoDasObras(mirante);
			if (reserva != null) DrPorAReservaEm(reserva, reservaDeAntes);
			LimparTudoDaBancada();   // os corpos forjados saem do mundo
			try { System.IO.File.Delete(arquivo); } catch (Exception) { /* o temporario fica: nao e do jogo */ }

			GD.Print($"[drill] ================ {_drOk} OK, {_drFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// 1) O MENU DA TECLA E, E A CURVA
	// =====================================================================
	private void DrDoMenuEDaCurva()
	{
		GD.Print("[drill] -- 1) o menu da tecla E e a curva (nivel -> taxa -> estoque -> preco) --");

		Interacoes.Acao[] raiz = Interacoes.De(Drill.Tipo);
		string[] verbos = [.. raiz.Select(a => a.Verbo)];
		AfirmarDr("o menu da Drill oferece COLETAR, MELHORAR (uma pagina), ver estado e pegar",
				  verbos.Contains(Drill.VerboColetar) && verbos.Contains(Drill.VerboInfo) && verbos.Contains("pegar")
				  && raiz.Any(a => a.Forma == Interacoes.Forma.Submenu && a.Arg == Drill.PaginaDeMelhorias),
				  string.Join(",", verbos));
		AfirmarDr("a pagina de melhorias tem as DUAS formas de pagar, e so elas: zeni e recursos",
				  Interacoes.De(Drill.ChaveDasMelhorias).Select(a => (a.Verbo, a.Arg))
							.SequenceEqual([(Drill.VerboMelhorar, Drill.PagarComZeni), (Drill.VerboMelhorar, Drill.PagarComRecursos)]));
		AfirmarDr("o servidor aceita os verbos dela (o de melhorar so existe dentro da pagina) -- e so NELA",
				  Interacoes.Aceita(Drill.Tipo, Drill.VerboMelhorar) && Interacoes.Aceita(Drill.Tipo, Drill.VerboColetar)
				  && !Interacoes.Aceita(Regenerador.Tipo, Drill.VerboColetar) && !Interacoes.Aceita("Bank", Drill.VerboMelhorar));
		AfirmarDr("ela NAO pede parafuso (o `Power_Drill` do DM nao tem `Bolt`): o menu nem o oferece",
				  !verbos.Contains("aparafusar"));

		// A TABELA, impressa: e ela que vai pro relatorio do dono.
		bool sobe = true;
		for (int n = 1; n <= Drill.NivelMaximo; n++)
		{
			double custo = Drill.CustoDaMelhoria(n);
			GD.Print($"[drill]      nivel {n,2}: {Drill.Taxa(n),4:0.#} rec/s | estoque {Drill.Teto(n),9:0} | "
					 + (Drill.NoMaximo(n) ? "(maximo)" : $"subir custa {custo,7:0} zeni ou {Recurso.CustoEmRecursos(custo),7:0} recursos"));
			if (n > 1)
				sobe &= Drill.Taxa(n) > Drill.Taxa(n - 1) && Drill.Teto(n) > Drill.Teto(n - 1) && custo > Drill.CustoDaMelhoria(n - 1);
		}
		AfirmarDr($"a curva SOBE a cada nivel, ate o {Drill.NivelMaximo}: mais taxa, mais estoque, e a melhoria seguinte mais cara", sobe);
		AfirmarDr("em recursos a melhoria custa o DOBRO do zeni (a conta e a do contrato, a de toda obra)",
				  Recurso.CustoEmRecursos(Drill.CustoDaMelhoria(1)) == 2 * Drill.CustoDaMelhoria(1));
	}

	// =====================================================================
	// 2) A INSTALACAO
	// =====================================================================
	private (Obra? Drill, ServerPlayer Dona) DrDaInstalacao(int px, int py)
	{
		GD.Print("[drill] -- 2) a instalacao: pelo fluxo normal de tecnologia, e ela ja nasce trabalhando --");

		ServerPlayer dona = Forjar("dr: a dona", DrNo(px + 2, py + 2), 1_000);
		dona.Conta = "bancada_drill_dona";

		Obra? drill = AssentarDeTeste(dona, Drill.Tipo, px + 2, py + 1);
		AfirmarDr("(montagem) a Drill assentou pelo funil de producao (`posicionar`), e e da dona",
				  drill is { Tipo: Drill.Tipo, DonoConta: "bancada_drill_dona" });
		if (drill == null) return (null, dona);

		AfirmarDr("recem-assentada ela ainda nao tem estado (o `posicionar` nao sabe o que e uma Drill)", drill.Drill == null);
		List<string> falas = Ouvir(TickDasDrills);
		AfirmarDr("no tique SEGUINTE ela ganha o estado: nivel 1, estoque vazio, contada a partir de agora",
				  drill.Drill is { Nivel: 1 } d0 && d0.Estoque < 1 && Math.Abs(d0.ContadoAteMs - NowMs()) < 5_000,
				  $"nivel {drill.Drill?.Nivel}, estoque {drill.Drill?.Estoque:0.###}");
		AfirmarDr("...e a dona ouve que ela comecou a extrair, com a taxa e a reserva do planeta",
				  Disse(falas, "começa a extrair") && Disse(falas, "reserva em"), Ultimos(falas));
		AfirmarDr("SOLTA, sem parafuso, ela ja trabalha", !drill.Aparafusada);
		return (drill, dona);
	}

	// =====================================================================
	// 3) A EXTRACAO
	// =====================================================================
	private void DrDaExtracao(Obra drill, ReservaDePlaneta reserva)
	{
		GD.Print("[drill] -- 3) a extracao: a taxa do nivel, pelo TEMPO QUE PASSOU, tirada da reserva do planeta --");
		DrillDaObra d = drill.Drill!;

		d.Estoque = 0;
		DrMarcar();
		double antes = reserva.Atual;
		DrPassar(100);
		// (v0.3.0) A TAXA E DEZ VEZES A DE ANTES (dono, 2026-10-15: *"aumente em 10x os ganhos de recursos da drill"*): as contas
		// daqui leem a constante (`Drill.TaxaPorNivel`), e nao o 1 por segundo que estava cravado nelas.
		double cem = 100 * Drill.TaxaPorNivel, umaHora = 3600 * Drill.TaxaPorNivel;
		AfirmarDr($"em 100 s a Drill de nivel 1 junta {cem:0} recursos ({Drill.TaxaPorNivel:0} por segundo)", DrPerto(d.Estoque, cem), $"{d.Estoque:0.######}");
		AfirmarDr("...e a reserva do planeta caiu EXATAMENTE isso: o que entra no estoque e o que saiu dela",
				  DrPerto(antes - reserva.Atual, cem), $"saiu {antes - reserva.Atual:0.######}");

		DrPassar(0);
		AfirmarDr("consultar de novo no MESMO instante nao paga nada (o carimbo andou com a conta)",
				  DrPerto(d.Estoque, cem) && d.ContadoAteMs == _drRelogio, $"{d.Estoque:0.######}");
		try
		{
			Drill.CarimboParadoDeTeste = true;
			d.Estoque = 0;
			DrMarcar();
			DrPassar(100);
			DrPassar(0);
			AfirmarDr("(defeito injetado: o carimbo nao anda) a mesma regua REPROVA -- a segunda consulta paga os 100 s de novo",
					  !DrPerto(d.Estoque, cem), $"{d.Estoque:0.###}");
		}
		finally { Drill.CarimboParadoDeTeste = false; }

		// UMA HORA SEM NINGUEM CONTAR -- o planeta vazio, o servidor desligado: UMA conta so, uma hora depois.
		d.Estoque = 0;
		DrMarcar();
		DrPassar(3600);
		AfirmarDr($"uma HORA sem ninguem contar: a conta seguinte paga a hora inteira ({umaHora:0}), e nao um tique",
				  DrPerto(d.Estoque, umaHora), $"{d.Estoque:0.###}");
		try
		{
			Drill.ContaSoOTiqueDeTeste = true;
			d.Estoque = 0;
			DrMarcar();
			DrPassar(3600);
			AfirmarDr("(defeito injetado: a conta so enxerga um tique) a mesma regua REPROVA -- a hora vira cinco segundos",
					  !DrPerto(d.Estoque, umaHora), $"{d.Estoque:0.###}");
		}
		finally { Drill.ContaSoOTiqueDeTeste = false; }

		// O RELOGIO QUE ANDOU PRA TRAS (acerto de hora da maquina): pede zero, e o carimbo volta com ele.
		d.Estoque = 0;
		DrMarcar();
		d.ContadoAteMs = _drRelogio + 60_000;
		DrPassar(0);
		AfirmarDr("relogio que andou pra tras nao rende nada e nao trava a Drill: o carimbo volta pro instante da conta",
				  DrPerto(d.Estoque, 0) && d.ContadoAteMs == _drRelogio);
	}

	// =====================================================================
	// 4) O TETO DO ESTOQUE
	// =====================================================================
	private void DrDoTeto(Obra drill, ReservaDePlaneta reserva)
	{
		GD.Print("[drill] -- 4) o estoque tem TETO: cheia, ela para de tirar do planeta --");
		DrillDaObra d = drill.Drill!;
		double teto = Drill.Teto(d.Nivel), umaHoraAlem = Drill.HorasDeEstoque * 3600 + 3600;

		d.Estoque = 0;
		DrMarcar();
		double antes = reserva.Atual;
		DrPassar(umaHoraAlem);
		AfirmarDr($"o estoque PARA no teto do nivel ({teto:0}): uma hora alem do que cabe nao rende um recurso a mais",
				  DrPerto(d.Estoque, teto), $"{d.Estoque:0.###}");
		AfirmarDr("...e a reserva so perdeu o que COUBE", DrPerto(antes - reserva.Atual, teto), $"saiu {antes - reserva.Atual:0.###}");
		AfirmarDr("...a situacao dela vira CHEIA", Drill.Situacao(d, reserva) == SituacaoDaDrill.Cheia, $"{Drill.Situacao(d, reserva)}");

		antes = reserva.Atual;
		DrPassar(7200);
		AfirmarDr("mais duas horas cheia: nada sai do planeta, e o tempo parada nao fica devido",
				  DrPerto(antes - reserva.Atual, 0) && DrPerto(d.Estoque, teto) && d.ContadoAteMs == _drRelogio);

		try
		{
			Drill.EstoqueSemTetoDeTeste = true;
			d.Estoque = 0;
			DrMarcar();
			DrPassar(umaHoraAlem);
			AfirmarDr("(defeito injetado: estoque sem teto) a mesma regua REPROVA -- a hora a mais entra",
					  !DrPerto(d.Estoque, teto), $"{d.Estoque:0.###} > {teto:0}");
		}
		finally { Drill.EstoqueSemTetoDeTeste = false; }

		// O TETO QUE ENCOLHEU (o dono baixou a constante com Drills ja cheias no mundo): o que ja estava la fica.
		d.Estoque = teto + 500;
		DrMarcar();
		antes = reserva.Atual;
		DrPassar(3600);
		AfirmarDr("um estoque ACIMA do teto de hoje (a constante baixou) nao e cortado: fica inteiro ate a coleta, e ela nao tira mais nada",
				  DrPerto(d.Estoque, teto + 500) && DrPerto(antes - reserva.Atual, 0) && Drill.Situacao(d, reserva) == SituacaoDaDrill.Cheia,
				  $"{d.Estoque:0.###}");
		try
		{
			Drill.TetoCortaOQueJaEstavaDeTeste = true;
			DrPassar(3600);
			AfirmarDr("(defeito injetado: o teto corta o que ja estava) a mesma regua REPROVA -- os 500 a mais somem",
					  !DrPerto(d.Estoque, teto + 500), $"{d.Estoque:0.###}");
		}
		finally { Drill.TetoCortaOQueJaEstavaDeTeste = false; }

		// ...e ela fica CHEIA pra cena da coleta.
		d.Estoque = 0;
		DrMarcar();
		DrPassar(umaHoraAlem);
		DrPorAReservaEm(reserva, reserva.Maximo);
	}

	// =====================================================================
	// 5) A COLETA, E DE QUEM ELA E
	// =====================================================================
	private ServerPlayer DrDaColeta(Obra drill, ServerPlayer dona, int px, int py)
	{
		GD.Print("[drill] -- 5) a coleta: o estoque vira saldo -- e so o dono coleta --");
		DrillDaObra d = drill.Drill!;
		double teto = Drill.Teto(d.Nivel);

		double saldo = dona.Ficha.Recursos;
		List<string> falas = Ouvir(() => ComandoDeInteracao(dona, Drill.VerboColetar, ""));
		AfirmarDr($"COLETAR passa o estoque inteiro pro saldo da dona: +{teto:0} recursos",
				  DrPerto(dona.Ficha.Recursos - saldo, teto) && Disse(falas, "você coleta"),
				  $"+{dona.Ficha.Recursos - saldo:0.###} | {Ultimos(falas)}");
		AfirmarDr("...a maquina fica vazia (so a fracao de um recurso fica nela)", d.Estoque < 1, $"{d.Estoque:0.######}");
		AfirmarDr("...e, esvaziada, ela VOLTA a extrair",
				  Drill.Situacao(d, ReservaDe(drill.Zona)) == SituacaoDaDrill.Extraindo);

		saldo = dona.Ficha.Recursos;
		falas = Ouvir(() => ComandoDeInteracao(dona, Drill.VerboColetar, ""));
		AfirmarDr("coletar de novo em seguida nao da nada: o que foi coletado SAIU da maquina",
				  dona.Ficha.Recursos - saldo <= 2, $"+{dona.Ficha.Recursos - saldo:0.###} | {Ultimos(falas)}");
		try
		{
			Drill.ColetaNaoEsvaziaDeTeste = true;
			d.Estoque = 500;
			saldo = dona.Ficha.Recursos;
			ComandoDeInteracao(dona, Drill.VerboColetar, "");
			ComandoDeInteracao(dona, Drill.VerboColetar, "");
			AfirmarDr("(defeito injetado: coletar nao esvazia) a mesma regua REPROVA -- a segunda coleta paga o estoque de novo",
					  dona.Ficha.Recursos - saldo > 500 + 2, $"+{dona.Ficha.Recursos - saldo:0.###}");
		}
		finally { Drill.ColetaNaoEsvaziaDeTeste = false; }

		// ------------------------------------------------------------ O VIZINHO
		ServerPlayer vizinho = Forjar("dr: o vizinho", DrNo(px + 3, py + 2), 1_000);
		vizinho.Conta = "bancada_drill_vizinho";
		vizinho.Ficha.Zeni = 10_000_000;
		d.Estoque = 300;
		double saldoDoVizinho = vizinho.Ficha.Recursos;

		falas = Ouvir(() => ComandoDeInteracao(vizinho, Drill.VerboColetar, ""));
		AfirmarDr("QUEM NAO E DONO NAO COLETA: o estoque fica na maquina, o saldo dele nao anda, e ele ouve de quem ela e",
				  DrPerto(vizinho.Ficha.Recursos, saldoDoVizinho) && d.Estoque >= 300 && Disse(falas, "só o dono") && Disse(falas, dona.Name),
				  Ultimos(falas));
		falas = Ouvir(() => ComandoDeInteracao(vizinho, Drill.VerboMelhorar, Drill.PagarComZeni));
		AfirmarDr("...nem melhora, com zeni sobrando: o nivel dela e o zeni dele ficam como estavam",
				  d.Nivel == 1 && vizinho.Ficha.Zeni == 10_000_000 && Disse(falas, "só o dono"), Ultimos(falas));

		EstadoDeDrill? visto = null;
		EscutaDeDrill = (quem, e) => { if (quem == vizinho.Id) visto = e; };
		ComandoDeInteracao(vizinho, Drill.VerboVer, "");
		AfirmarDr("...mas VE o estado dela, marcado como alheio e com o nome da dona",
				  visto is { Minha: false } v && v.Id == drill.Id && v.Dono == dona.Name);
		AfirmarDr("...e o painel dele comeca dizendo de quem e, e nao mostra preco nem saldo",
				  visto is { } ve && Drill.Descrever(ve) is { } linhas && linhas[0].Contains("só o dono")
				  && !linhas.Exists(l => l.Contains("zeni")));
		EscutaDeDrill = null;

		try
		{
			DrillDeQualquerUmDeTeste = true;
			ComandoDeInteracao(vizinho, Drill.VerboColetar, "");
			AfirmarDr("(defeito injetado: a Drill e de qualquer um) a mesma regua REPROVA -- o vizinho leva o estoque",
					  vizinho.Ficha.Recursos > saldoDoVizinho, $"+{vizinho.Ficha.Recursos - saldoDoVizinho:0.###}");
		}
		finally { DrillDeQualquerUmDeTeste = false; vizinho.Ficha.Recursos = saldoDoVizinho; }
		return vizinho;
	}

	// =====================================================================
	// 6) AS MELHORIAS
	// =====================================================================
	private void DrDasMelhorias(Obra drill, ServerPlayer dona)
	{
		GD.Print("[drill] -- 6) melhorar: zeni, ou o dobro em recursos -- e sem saldo naquela moeda, recusa --");
		DrillDaObra d = drill.Drill!;
		double c1 = Drill.CustoDaMelhoria(1), c2 = Drill.CustoDaMelhoria(2), c3 = Drill.CustoDaMelhoria(3);
		const double Cheio = 1_000_000, Farto = 10_000_000;

		d.Nivel = 1;
		dona.Ficha.Zeni = dona.Ficha.Recursos = Cheio;
		List<string> falas = Ouvir(() => ComandoDeInteracao(dona, Drill.VerboMelhorar, Drill.PagarComZeni));
		AfirmarDr($"MELHORAR COM ZENI cobra {c1:0} zeni, nao toca nos recursos, e leva a Drill ao nivel 2",
				  d.Nivel == 2 && dona.Ficha.Zeni == Cheio - c1 && dona.Ficha.Recursos == Cheio && Disse(falas, "você melhora"),
				  $"nivel {d.Nivel}, zeni {dona.Ficha.Zeni:0}, recursos {dona.Ficha.Recursos:0} | {Ultimos(falas)}");

		falas = Ouvir(() => ComandoDeInteracao(dona, Drill.VerboMelhorar, Drill.PagarComRecursos));
		AfirmarDr($"MELHORAR COM RECURSOS cobra o DOBRO ({2 * c2:0} recursos pelos {c2:0} zeni do nivel), nao toca no zeni, e leva ao nivel 3",
				  d.Nivel == 3 && dona.Ficha.Recursos == Cheio - 2 * c2 && dona.Ficha.Zeni == Cheio - c1,
				  $"nivel {d.Nivel}, zeni {dona.Ficha.Zeni:0}, recursos {dona.Ficha.Recursos:0} | {Ultimos(falas)}");

		d.Estoque = 0;
		DrMarcar();
		DrPassar(100);
		AfirmarDr($"no nivel 3 ela tira {Drill.Taxa(3):0} por segundo: {100 * Drill.Taxa(3):0} em 100 s (a de nivel 1 juntou {100 * Drill.Taxa(1):0} no mesmo tempo)",
				  DrPerto(d.Estoque, 100 * Drill.Taxa(3)), $"{d.Estoque:0.######}");
		AfirmarDr("...e o teto do estoque subiu junto com a taxa", DrPerto(Drill.Teto(d.Nivel), 3 * Drill.Teto(1)));

		// ------------------------------------------------------------ SEM SALDO
		dona.Ficha.Zeni = c3 - 1;
		dona.Ficha.Recursos = Farto;
		falas = Ouvir(() => ComandoDeInteracao(dona, Drill.VerboMelhorar, Drill.PagarComZeni));
		AfirmarDr("SEM ZENI BASTANTE (falta 1) recusa: o nivel fica, nada e cobrado -- nem dos recursos que sobravam --, e ela diz o preco",
				  d.Nivel == 3 && dona.Ficha.Zeni == c3 - 1 && dona.Ficha.Recursos == Farto && Disse(falas, "custa") && Disse(falas, "zeni"),
				  Ultimos(falas));

		dona.Ficha.Zeni = Farto;
		dona.Ficha.Recursos = 2 * c3 - 1;
		falas = Ouvir(() => ComandoDeInteracao(dona, Drill.VerboMelhorar, Drill.PagarComRecursos));
		AfirmarDr("SEM RECURSOS BASTANTES (falta 1) recusa -- e NAO completa com o zeni que sobrava",
				  d.Nivel == 3 && dona.Ficha.Zeni == Farto && dona.Ficha.Recursos == 2 * c3 - 1 && Disse(falas, "custa") && Disse(falas, "recursos"),
				  Ultimos(falas));

		falas = Ouvir(() => ComandoDeInteracao(dona, Drill.VerboMelhorar, "fiado"));
		AfirmarDr("moeda que nao existe e recusada", d.Nivel == 3 && dona.Ficha.Zeni == Farto && Disse(falas, "zeni ou recursos"), Ultimos(falas));

		try
		{
			DrillMelhoraSemPagarDeTeste = true;
			dona.Ficha.Zeni = 0;
			ComandoDeInteracao(dona, Drill.VerboMelhorar, Drill.PagarComZeni);
			AfirmarDr("(defeito injetado: melhora sem pagar) a mesma regua REPROVA -- sem um zeni, o nivel sobe", d.Nivel != 3, $"nivel {d.Nivel}");
		}
		finally { DrillMelhoraSemPagarDeTeste = false; d.Nivel = 3; }

		// ------------------------------------------------------------ O FIM DA CURVA
		dona.Ficha.Zeni = Farto;
		d.Nivel = Drill.NivelMaximo;
		falas = Ouvir(() => ComandoDeInteracao(dona, Drill.VerboMelhorar, Drill.PagarComZeni));
		AfirmarDr($"no nivel maximo ({Drill.NivelMaximo}) nao ha o que comprar: recusa, e nao cobra",
				  d.Nivel == Drill.NivelMaximo && dona.Ficha.Zeni == Farto && Disse(falas, "máximo"), Ultimos(falas));
		d.Nivel = 3;
	}

	// =====================================================================
	// 7) O PLANETA SECO
	// =====================================================================
	private void DrDoPlanetaSeco(Obra drill, ServerPlayer dona, ReservaDePlaneta reserva)
	{
		GD.Print("[drill] -- 7) reserva seca: ela para -- e volta sozinha quando a reserva regenera --");
		DrillDaObra d = drill.Drill!;

		DrPorAReservaEm(reserva, 0);
		d.Estoque = 0;
		DrMarcar();
		DrPassar(100);
		AfirmarDr("PLANETA SECO: em 100 s a Drill (nivel 3) nao junta nada", DrPerto(d.Estoque, 0), $"{d.Estoque:0.######}");
		AfirmarDr("...a situacao dela vira PLANETA ESGOTADO", Drill.Situacao(d, reserva) == SituacaoDaDrill.PlanetaEsgotado,
				  $"{Drill.Situacao(d, reserva)}");
		List<string> falas = Ouvir(() => ComandoDeInteracao(dona, Drill.VerboInfo, ""));
		AfirmarDr("...\"Ver estado\" diz isso com todas as letras", Disse(falas, "PLANETA ESGOTADO"), Ultimos(falas));
		falas = Ouvir(() => ComandoDeInteracao(dona, Drill.VerboColetar, ""));
		AfirmarDr("...e coletar explica por que ela esta vazia", Disse(falas, "esgotado"), Ultimos(falas));

		try
		{
			Drill.TiraDoNadaDeTeste = true;
			d.Estoque = 0;
			DrMarcar();
			DrPassar(100);
			AfirmarDr($"(defeito injetado: a Drill credita o que pediu) a mesma regua REPROVA -- com a reserva em zero ela junta {100 * Drill.Taxa(d.Nivel):0}",
					  !DrPerto(d.Estoque, 0), $"{d.Estoque:0.###}");
		}
		finally { Drill.TiraDoNadaDeTeste = false; }

		// A RESERVA REGENERA (seis horas de relogio, pela conta do contrato), e ninguem religa nada.
		reserva.Regenerar(6 * 3600);
		double voltou = reserva.Atual, cem = 100 * Drill.Taxa(d.Nivel);
		d.Estoque = 0;
		DrMarcar();
		DrPassar(100);
		AfirmarDr($"a reserva REGENERA e ela volta sozinha, sem ninguem religar: {cem:0} em 100 s, tirados do que regenerou",
				  voltou > cem && DrPerto(d.Estoque, cem) && DrPerto(voltou - reserva.Atual, cem),
				  $"regenerou {voltou:0}, estoque {d.Estoque:0.###}");

		DrPorAReservaEm(reserva, 40);
		d.Estoque = 0;
		DrMarcar();
		DrPassar(100);
		AfirmarDr("com a reserva no fim (40) ela leva so o que ha: 40 -- e e a Drill que ZERA o planeta",
				  DrPerto(d.Estoque, 40) && DrPerto(reserva.Atual, 0) && DrPerto(reserva.Fracao, 0), $"estoque {d.Estoque:0.###}, reserva {reserva.Atual:0.###}");
		DrPorAReservaEm(reserva, reserva.Maximo);
	}

	// =====================================================================
	// 8) DUAS DRILLS, UMA RESERVA
	// =====================================================================
	private void DrDasDuas(Obra drill, ServerPlayer vizinho, int px, int py, ReservaDePlaneta reserva)
	{
		GD.Print("[drill] -- 8) duas Drills no mesmo planeta dividem a MESMA reserva --");
		vizinho.Pos = DrNo(px + 6, py + 2);
		Obra? segunda = AssentarDeTeste(vizinho, Drill.Tipo, px + 6, py + 1);
		TickDasDrills();
		AfirmarDr("(montagem) uma SEGUNDA Drill, de outro dono, no mesmo planeta",
				  segunda is { Drill: not null } && segunda.DonoConta == vizinho.Conta);
		if (segunda?.Drill is not { } d2) return;
		DrillDaObra d1 = drill.Drill!;

		void Cena(double reservaDaCena, int nivelDaPrimeira)
		{
			d1.Nivel = nivelDaPrimeira;
			d2.Nivel = 1;
			DrPorAReservaEm(reserva, reservaDaCena);
			d1.Estoque = d2.Estoque = 0;
			DrMarcar();
			DrPassar(100);
		}

		Cena(reserva.Maximo, 1);
		double cada = 100 * Drill.Taxa(1);
		AfirmarDr($"com a reserva folgada cada uma junta o seu ({cada:0} e {cada:0}), e o planeta perde a SOMA: e uma reserva so",
				  DrPerto(d1.Estoque, cada) && DrPerto(d2.Estoque, cada) && DrPerto(reserva.Maximo - reserva.Atual, 2 * cada),
				  $"{d1.Estoque:0.###} / {d2.Estoque:0.###}, planeta -{reserva.Maximo - reserva.Atual:0.###}");

		Cena(60, 1);
		AfirmarDr("reserva de 60 pra duas que pedem 100 cada: saem 60 ao todo -- nunca mais do que o planeta tinha --, 30 pra cada",
				  DrPerto(d1.Estoque, 30) && DrPerto(d2.Estoque, 30) && DrPerto(reserva.Atual, 0),
				  $"{d1.Estoque:0.###} / {d2.Estoque:0.###}, reserva {reserva.Atual:0.###}");
		AfirmarDr("...e as DUAS ficam em \"planeta esgotado\"",
				  Drill.Situacao(d1, reserva) == SituacaoDaDrill.PlanetaEsgotado && Drill.Situacao(d2, reserva) == SituacaoDaDrill.PlanetaEsgotado);

		Cena(80, 3);
		AfirmarDr("o rateio e pelo que cada uma PEDIU: a de nivel 3 e a de nivel 1 dividem 80 em 60 e 20",
				  DrPerto(d1.Estoque, 60) && DrPerto(d2.Estoque, 20), $"{d1.Estoque:0.###} / {d2.Estoque:0.###}");

		try
		{
			Drill.PrimeiraLevaTudoDeTeste = true;
			Cena(60, 1);
			AfirmarDr("(defeito injetado: cada uma vai a reserva por conta propria) a regua do rateio REPROVA -- a primeira leva os 60 e a segunda fica sem nada",
					  !(DrPerto(d1.Estoque, 30) && DrPerto(d2.Estoque, 30)), $"{d1.Estoque:0.###} / {d2.Estoque:0.###}");
		}
		finally { Drill.PrimeiraLevaTudoDeTeste = false; }

		try
		{
			Drill.TiraDoNadaDeTeste = true;
			Cena(60, 1);
			AfirmarDr("(defeito injetado: a Drill credita o que pediu) a regua da soma REPROVA -- as duas juntam 200 de um planeta que tinha 60",
					  d1.Estoque + d2.Estoque > 61, $"{d1.Estoque + d2.Estoque:0.###}");
		}
		finally { Drill.TiraDoNadaDeTeste = false; }

		d1.Nivel = 3;
		DrPorAReservaEm(reserva, reserva.Maximo);
	}

	// =====================================================================
	// 9) O FIO E O PAINEL
	// =====================================================================
	private void DrDoFio(Obra drill, ServerPlayer dona, ReservaDePlaneta reserva)
	{
		GD.Print("[drill] -- 9) o fio (`S2C.Drill`) e as linhas do painel do menu --");
		DrillDaObra d = drill.Drill!;
		dona.Ficha.Zeni = 123_456;
		dona.Ficha.Recursos = 7_890;
		d.Estoque = 1234;

		EstadoDeDrill? visto = null;
		EscutaDeDrill = (quem, e) => { if (quem == dona.Id) visto = e; };
		List<string> falas = Ouvir(() => ComandoDeInteracao(dona, Drill.VerboVer, ""));
		AfirmarDr("\"como esta a Drill?\" (o painel pergunta ao abrir) responde pelo fio, CALADO: nenhuma linha no chat",
				  visto != null && falas.Count == 0, Ultimos(falas));
		if (visto is not { } e0) return;

		AfirmarDr("o estado leva a obra, o nivel, o estoque, a situacao, a reserva, que ela e MINHA, e os meus dois saldos",
				  e0.Id == drill.Id && e0.Nivel == d.Nivel && DrPerto(e0.Estoque, d.Estoque) && e0.Situacao == SituacaoDaDrill.Extraindo
				  && DrPerto(e0.FracaoDaReserva, reserva.Fracao) && e0.Minha && e0.Dono == dona.Name
				  && e0.Zeni == 123_456 && e0.Recursos == 7_890, $"{e0}");

		NetDataWriter w = Protocol.Begin(Protocol.S2C.Drill);
		FioDaDrill.Escrever(w, e0);
		var r = new NetDataReader(w.CopyData());
		bool opcode = r.GetByte() == (byte)Protocol.S2C.Drill;
		EstadoDeDrill lido = FioDaDrill.Ler(r);
		AfirmarDr("o pacote da a volta inteira: o leitor do cliente devolve o que o servidor escreveu, e nao sobra byte",
				  opcode && r.AvailableBytes == 0 && lido with { FracaoDaReserva = e0.FracaoDaReserva } == e0
				  && Math.Abs(lido.FracaoDaReserva - e0.FracaoDaReserva) < 1e-6, $"{lido}");

		List<string> linhas = Drill.Descrever(e0);
		AfirmarDr("as linhas do painel: nivel e taxa, estoque com o teto e o tempo pra encher, reserva em %, o preco nas duas moedas e o saldo",
				  linhas.Exists(l => l.StartsWith($"Nível {d.Nivel} de {Drill.NivelMaximo}") && l.Contains("por segundo"))
				  && linhas.Exists(l => l.StartsWith("Estoque:") && l.Contains("cheio em"))
				  && linhas.Exists(l => l.StartsWith("Reserva do planeta:") && l.EndsWith('%'))
				  && linhas.Exists(l => l.StartsWith($"Nível {d.Nivel + 1}") && l.Contains("zeni ou") && l.EndsWith("recursos"))
				  && linhas.Exists(l => l.StartsWith("Você tem")), string.Join(" | ", linhas));

		falas = Ouvir(() => ComandoDeInteracao(dona, Drill.VerboInfo, ""));
		AfirmarDr("\"Ver estado\" escreve no chat o titulo e AS MESMAS linhas (uma casa so pras duas pontas)",
				  falas.Count == linhas.Count + 1 && falas.Exists(f => f.Trim() == linhas[0]) && falas.Exists(f => f.Trim() == linhas[^1]),
				  Ultimos(falas));

		EstadoDeDrill adiante = Drill.Adiante(e0, 10);
		AfirmarDr("entre duas respostas o painel anda sozinho: 10 s depois, +10 x a taxa -- e ele para no teto, ja como CHEIA",
				  DrPerto(adiante.Estoque, e0.Estoque + 10 * Drill.Taxa(e0.Nivel))
				  && Drill.Adiante(e0, 1e9) is { Situacao: SituacaoDaDrill.Cheia } cheia && DrPerto(cheia.Estoque, Drill.Teto(e0.Nivel)));

		// ------------------------------------------------------------ DE LONGE
		Vec2 onde = dona.Pos;
		dona.Pos = onde + new Vec2(20 * ZoneCollision.TileSize, 0);
		visto = null;
		ComandoDeInteracao(dona, Drill.VerboVer, "");
		double saldo = dona.Ficha.Recursos;
		falas = Ouvir(() => ComandoDeInteracao(dona, Drill.VerboColetar, ""));
		AfirmarDr("de LONGE a resposta e \"nenhuma\" (id 0), e coletar e recusado: o menu nao e permissao",
				  visto is { Id: 0 } && dona.Ficha.Recursos == saldo && Disse(falas, "não há Drill"), Ultimos(falas));
		dona.Pos = onde;
		EscutaDeDrill = null;
	}

	// =====================================================================
	// 10) RECOLHER
	// =====================================================================
	private Obra? DrDeRecolher(Obra drill, ServerPlayer dona, int px, int py, ReservaDePlaneta reserva)
	{
		GD.Print("[drill] -- 10) recolher pra mochila: o estoque vem junto, e o nivel volta na proxima que ela assentar --");
		DrillDaObra d = drill.Drill!;

		// O PLANETA SECO durante a cena: o estoque e o que a bancada pos, e nao o que pingou enquanto ela media.
		DrPorAReservaEm(reserva, 0);
		d.Nivel = 3;
		d.Estoque = 77;
		double saldo = dona.Ficha.Recursos;
		int naMochila = dona.Mochila.Quantos(Drill.Tipo);

		List<string> falas = Ouvir(() => ComandoDeInteracao(dona, "pegar", ""));
		AfirmarDr("a dona RECOLHE a Drill pelo `pegar` de toda obra: ela sai do chao e entra na mochila",
				  !_noChao.Contains(drill) && dona.Mochila.Quantos(Drill.Tipo) == naMochila + 1, Ultimos(falas));
		AfirmarDr("...o ESTOQUE vem junto pro saldo dela (77)", DrPerto(dona.Ficha.Recursos - saldo, 77) && Disse(falas, "vem junto"),
				  $"+{dona.Ficha.Recursos - saldo:0.###}");
		AfirmarDr("...e o NIVEL fica guardado na ficha dela", dona.Ficha.DrillsRecolhidas.SequenceEqual([3]) && Disse(falas, "melhorias ficam"),
				  string.Join(",", dona.Ficha.DrillsRecolhidas));

		Obra? volta = AssentarDeTeste(dona, Drill.Tipo, px + 2, py + 1);
		falas = Ouvir(TickDasDrills);
		AfirmarDr("assentada de novo ela VOLTA NO NIVEL 3 -- e a ficha esquece o que devolveu",
				  volta?.Drill is { Nivel: 3 } && dona.Ficha.DrillsRecolhidas.Count == 0 && Disse(falas, "voltam com ela"), Ultimos(falas));

		Obra? nova = AssentarDeTeste(dona, Drill.Tipo, px + 2, py + 3);
		TickDasDrills();
		AfirmarDr("(contraprova) a Drill SEGUINTE que ela assenta nasce de fabrica: o nivel guardado vale uma vez",
				  nova?.Drill is { Nivel: 1 });
		if (nova != null) _noChao.Remove(nova);
		if (volta?.Drill == null) return volta;

		try
		{
			DrillRecolhidaPerdeTudoDeTeste = true;
			volta.Drill.Estoque = 50;
			saldo = dona.Ficha.Recursos;
			ComandoDeInteracao(dona, "pegar", "");
			AfirmarDr("(defeito injetado: recolher joga tudo fora) as mesmas reguas REPROVAM -- nem estoque no saldo, nem nivel na ficha",
					  !_noChao.Contains(volta) && dona.Ficha.Recursos == saldo && dona.Ficha.DrillsRecolhidas.Count == 0);
		}
		finally { DrillRecolhidaPerdeTudoDeTeste = false; }

		// ...e ela volta pro chao, no nivel 3, pras cenas seguintes.
		volta = AssentarDeTeste(dona, Drill.Tipo, px + 2, py + 1);
		TickDasDrills();
		if (volta?.Drill is { } d3) d3.Nivel = 3;
		DrPorAReservaEm(reserva, reserva.Maximo);
		return volta;
	}

	// =====================================================================
	// 11) ONDE NAO HA RESERVA
	// =====================================================================
	private void DrDoLugarSemReserva(ZoneKey mirante)
	{
		GD.Print("[drill] -- 11) onde NAO ha reserva (o Lookout): instala, avisa, e nao junta nada --");
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(mirante);
		AfirmarDr("(montagem) o Lookout tem mapa e NAO tem reserva (nao e planeta: o contrato devolve nulo)",
				  mapa != null && ReservaDe(mirante) == null);
		if (mapa == null || ReservaDe(mirante) != null) return;

		ServerPlayer visita = Forjar("dr: no mirante", DrNo(0, 0), 1_000);
		visita.Conta = "bancada_drill_mirante";
		ZoneList(ZonaDaBancadaDeProjetil.Hash).Remove(visita);
		visita.Zone = mirante;
		ZoneList(mirante.Hash).Add(visita);

		Obra? drill = null;
		if (DrChaoLivre(mirante, mapa) is { } c)
		{
			visita.Pos = DrNo(c.X, c.Y + 1);
			drill = AssentarDeTeste(visita, Drill.Tipo, c.X, c.Y);
		}
		AfirmarDr("a Drill ASSENTA no Lookout pelo mesmo funil: lugar sem reserva nao impede a instalacao",
				  drill != null && drill.Zona.Equals(mirante));
		if (drill == null) return;

		List<string> falas = Ouvir(TickDasDrills);
		AfirmarDr("...e o dono e AVISADO, ao instalar, de que ali nao ha o que extrair",
				  drill.Drill != null && Disse(falas, "NÃO HÁ O QUE EXTRAIR"), Ultimos(falas));
		if (drill.Drill is not { } d) return;

		DrMarcar();
		DrPassar(3600);
		AfirmarDr("uma hora depois o estoque continua vazio -- e o carimbo ANDOU: ela nao acumula um tempo que nunca vai valer",
				  DrPerto(d.Estoque, 0) && d.ContadoAteMs == _drRelogio, $"{d.Estoque:0.###}");

		EstadoDeDrill? visto = null;
		EscutaDeDrill = (quem, e) => { if (quem == visita.Id) visto = e; };
		ComandoDeInteracao(visita, Drill.VerboVer, "");
		falas = Ouvir(() => ComandoDeInteracao(visita, Drill.VerboInfo, ""));
		AfirmarDr("o painel e o \"Ver estado\" dizem SEM RESERVA, com todas as letras",
				  visto is { Situacao: SituacaoDaDrill.SemReserva } && Disse(falas, "AQUI NÃO HÁ O QUE EXTRAIR"), Ultimos(falas));
		EscutaDeDrill = null;
	}

	/// <summary>O que as Drills de uma zona tem no estoque, somado -- o outro lado da conta da reserva.</summary>
	private double DrEstoqueDaZona(ZoneKey zona)
	{
		double soma = 0;
		foreach (Obra o in _noChao)
			if (o.Drill is { } d && o.Zona.Equals(zona)) soma += d.Estoque;
		return soma;
	}

	/// <summary>Uma celula de chao comum, com dois tiles livres pra cada lado, procurada do meio do mapa pra fora.</summary>
	private (int X, int Y)? DrChaoLivre(ZoneKey zona, ZoneCollision mapa)
	{
		int cx = mapa.Width / 2, cy = mapa.Height / 2;
		for (int r = 0; r < Math.Max(mapa.Width, mapa.Height) / 2; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					if (r > 0 && Math.Abs(dx) != r && Math.Abs(dy) != r) continue;   // so a casca: o miolo ja foi visto
					if (QuadradoDeChaoComum(zona, mapa, cx + dx, cy + dy, 2)) return (cx + dx, cy + dy);
				}
		return null;
	}

	// =====================================================================
	// 12) SEM NINGUEM POR PERTO, E COM O SERVIDOR DESLIGADO
	// =====================================================================
	private void DrDoTempoSemNinguem(Obra drill, ReservaDePlaneta reserva, string arquivo)
	{
		GD.Print("[drill] -- 12) planeta vazio, dono fora, servidor desligado: nem perde, nem duplica --");
		DrillDaObra d = drill.Drill!;
		double taxa = Drill.Taxa(d.Nivel);

		// ------------------------------------------------------------ (a) O TIQUE DE PRODUCAO, SEM NINGUEM NO PLANETA
		LimparTudoDaBancada();
		bool ninguem = !_zones.TryGetValue(drill.Zona.Hash, out List<ServerPlayer>? naZona) || !naZona.Exists(p => p.Conta.Length > 0);
		d.Estoque = 0;
		DrMarcar();
		double antes = reserva.Atual, juntoAntes = DrEstoqueDaZona(drill.Zona);
		AdiantoDoRelogioDeTeste += 3_600_000;
		_proximaLeituraDasDrills = 0;
		TickDasDrills();
		AfirmarDr("com o planeta SEM JOGADOR NENHUM (a dona saiu do jogo), o tique de producao poe a Drill em dia: a hora rende a hora inteira",
				  ninguem && d.Estoque >= 3600 * taxa - Drill.Folga && d.Estoque <= 3605 * taxa,
				  $"ninguem {ninguem}, estoque {d.Estoque:0.###} (esperado {3600 * taxa:0})");
		AfirmarDr("...e a reserva do planeta caiu o que as Drills dele juntaram: ela seca com o dono fora",
				  DrPerto(antes - reserva.Atual, DrEstoqueDaZona(drill.Zona) - juntoAntes) && antes - reserva.Atual >= 3600 * taxa - Drill.Folga,
				  $"planeta -{antes - reserva.Atual:0.###}");

		long carimbo = d.ContadoAteMs;
		TickDasDrills();
		AfirmarDr("entre duas leituras o tique nao refaz a conta (o prazo de cinco segundos)", d.ContadoAteMs == carimbo);

		_proximaLeituraDasDrills = NowMs() + 36_000_000;
		AdiantoDoRelogioDeTeste += 10_000;
		TickDasDrills();
		AfirmarDr("um prazo perdido dez horas no futuro (o relogio andou pra tras) nao para as Drills: a leitura acontece",
				  d.ContadoAteMs > carimbo);

		// ------------------------------------------------------------ (b) O DISCO, SOZINHO
		d.Estoque = 0;
		DrMarcar();
		AdiantoDoRelogioDeTeste += 120_000;
		_proximaLeituraDasDrills = _proximaGravacaoDasDrills = 0;
		TickDasDrills();
		DrillDaObra? noDisco = null;
		try
		{
			noDisco = JsonSerializer.Deserialize<List<Obra>>(System.IO.File.ReadAllText(CaminhoDoMundo),
				new JsonSerializerOptions { IncludeFields = true })?.FirstOrDefault(o => o.Id == drill.Id)?.Drill;
		}
		catch (Exception e) { GD.PrintErr($"[drill]   (nao li o mundo.json: {e.Message})"); }
		AfirmarDr("o estoque que mudou vai pro DISCO sozinho, sem ninguem mexer na maquina -- com o nivel e o carimbo junto",
				  noDisco != null && noDisco.Nivel == d.Nivel && DrPerto(noDisco.Estoque, d.Estoque) && noDisco.ContadoAteMs == d.ContadoAteMs
				  && d.Estoque >= 120 * taxa - Drill.Folga, $"no disco: {noDisco?.Estoque:0.###}; na memoria: {d.Estoque:0.###}");

		// ------------------------------------------------------------ (c) O REINICIO
		const int MeiaHora = 1800;
		d.Estoque = 1000;
		DrMarcar();
		(int Nivel, double Estoque, long Carimbo) foto = (d.Nivel, d.Estoque, d.ContadoAteMs);
		GravarMundoEm(arquivo);
		double devido = foto.Estoque + MeiaHora * taxa;

		// O SERVIDOR "DESLIGA" (nada na memoria), fica meia hora fora do ar, e "liga" lendo o disco.
		DrillDaObra? Religar()
		{
			_noChao.Clear();
			CarregarMundoDeDisco(arquivo);
			return _noChao.FirstOrDefault(o => o.Id == drill.Id)?.Drill;
		}
		AdiantoDoRelogioDeTeste += MeiaHora * 1000L;
		_drRelogio += MeiaHora * 1000L;
		DrPorAReservaEm(reserva, reserva.Maximo);

		DrillDaObra? volta = Religar();
		AfirmarDr("REINICIO: a Drill volta do `mundo.json` com o nivel, o estoque e o carimbo que tinha",
				  volta != null && !ReferenceEquals(volta, d) && volta.Nivel == foto.Nivel && DrPerto(volta.Estoque, foto.Estoque)
				  && volta.ContadoAteMs == foto.Carimbo, $"nivel {volta?.Nivel}, estoque {volta?.Estoque:0.###}");
		if (volta == null) return;

		PorTodasAsDrillsEmDia(_drRelogio);
		AfirmarDr("a primeira conta depois do boot paga a meia hora em que o servidor ficou desligado: NAO PERDE",
				  DrPerto(volta.Estoque, devido), $"{volta.Estoque:0.###} (esperado {devido:0})");
		PorTodasAsDrillsEmDia(_drRelogio);
		AfirmarDr("...e a segunda, no mesmo instante, nao paga de novo: NAO DUPLICA", DrPerto(volta.Estoque, devido), $"{volta.Estoque:0.###}");

		try
		{
			Drill.ContaSoOTiqueDeTeste = true;
			volta = Religar();
			PorTodasAsDrillsEmDia(_drRelogio);
			AfirmarDr("(defeito injetado: a conta so enxerga um tique) a regua do reinicio REPROVA -- a meia hora desligada some",
					  volta != null && !DrPerto(volta.Estoque, devido), $"{volta?.Estoque:0.###}");
		}
		finally { Drill.ContaSoOTiqueDeTeste = false; }

		try
		{
			Drill.CarimboParadoDeTeste = true;
			volta = Religar();
			PorTodasAsDrillsEmDia(_drRelogio);
			PorTodasAsDrillsEmDia(_drRelogio);
			AfirmarDr("(defeito injetado: o carimbo nao anda) a regua do reinicio REPROVA -- a segunda conta paga a meia hora de novo",
					  volta != null && !DrPerto(volta.Estoque, devido), $"{volta?.Estoque:0.###}");
		}
		finally { Drill.CarimboParadoDeTeste = false; }

		// ------------------------------------------------------------ (d) O PLANETA QUE SAIU DA MEMORIA
		// Um mundo sorteado que ninguem carregou depois do reinicio: e planeta, e ninguem sabe a reserva dele agora.
		ZoneKey longe = ZoneKey.Procedural("Mundo-da-Bancada-da-Drill", 0xD1D1UL);
		var daOrfa = new DrillDaObra { Nivel = 2, ContadoAteMs = _drRelogio };
		var orfa = new Obra { Id = _proximaObraId++, Tipo = Drill.Tipo, DonoConta = "bancada_drill_dona", DonoNome = "dr: a dona", Drill = daOrfa };
		orfa.PorZona(longe);
		_noChao.Add(orfa);
		long carimboDaOrfa = daOrfa.ContadoAteMs;
		bool semLeitura = ReservaDe(longe) == null;
		AdiantoDoRelogioDeTeste += 3_600_000;
		_drRelogio += 3_600_000;
		PorTodasAsDrillsEmDia(_drRelogio);
		if (semLeitura)
			AfirmarDr("PLANETA SORTEADO FORA DA MEMORIA (ninguem sabe a reserva dele agora): a conta ESPERA -- o carimbo fica, e a hora continua devida",
					  DrPerto(daOrfa.Estoque, 0) && daOrfa.ContadoAteMs == carimboDaOrfa
					  && DrPerto(Drill.Pedido(daOrfa, _drRelogio), 3600 * Drill.Taxa(2)));
		else
			AfirmarDr("(o contrato ja sabe a reserva de um mundo sorteado sem carrega-lo: a conta nao espera) a hora foi paga",
					  DrPerto(daOrfa.Estoque, 3600 * Drill.Taxa(2)), $"{daOrfa.Estoque:0.###}");
	}
}
