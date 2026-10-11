using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Economia;
using Jandirus.Core.Items;
using Jandirus.Core.Magic;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA ECONOMIA DE RECURSOS (`--recursosteste`).
///
/// O dono (2026-10-10): *"a capacidade de construir tecnologias sem precisar de zenni com um material chamado
/// resource q seria a materia prima q vc encontra nos planetas mineirando [...] cada planeta tem um resource
/// maximo ao qual e baseado em seu tamanho, e ele regenera lentamente com o tempo [...] ao estar mineirando e
/// igual meditacao vc n pode se mover nem fazer nada [...] se algo custo 1000 zennis pra fazer vc pode fazer
/// com 2000 resources"* -- e, antes de tudo, *"remover space pod de tech 999 q aparece na aba tech"*.
///
/// No boot, como as vizinhas de servidor: os corpos sao forjados (sem `Peer`) e tudo passa pelo funil de
/// producao -- minerar e o `DefinirAtividade` que o `C2S.Activity` chama, o segundo de quem minera e o
/// `TickDaMineracao`, a reserva cresce pelo `TickDosRecursos`, e fabricar e o `ComandoDeTech` da aba.
///
/// SETE PERGUNTAS:
///   1. A REGRA (Core): o dobro, o teto pelo tamanho, a ordem mao &lt; pa &lt; furadeira, a regeneracao.
///   2. A RESERVA: quem tem e quem nao tem (Lookout, espaco, interior, planeta MORTO), o tamanho, o tique que a
///      faz crescer, e o DISCO -- o que vai, o que nao vai, e o reinicio horas depois.
///   3. A CARTEIRA: dar, cobrar, recusar sem saldo, e o cliente ficar sabendo -- inclusive do zeni que muda
///      POR FORA do funil (a vigia: a missao de cargo, o premio do torneio, o desejo de riqueza).
///   4. MINERAR: comeca, rende por segundo o que SAIU da reserva, e cai como a meditacao (andar, agir, brigar)
///      -- e o ESMAGADO pela gravidade ou pelo peso nao minera (decisao do dono), ouvindo o motivo certo. O
///      "cavar" da pa e da furadeira SAIU (decisao do dono): o comando antigo e recusado e nada rende.
///   5. PAGAR TECNOLOGIA: em zeni o preco, em recursos o dobro, e nunca uma completando a outra.
///   6. O CATALOGO: nada a venda acima do teto de tecnologia (o "Space Pod de tech 999").
///   7. O QUE PASSOU A ACEITAR RECURSOS (decisao do dono): as melhorias da maquina de gravidade, do regenerador
///      e da nave, a conversao em androide e a estatua suprema -- em zeni o preco, em recursos o dobro, o
///      mesmo efeito, e nunca uma moeda completando a outra. E o Enma, que continua SO em zeni.
///
/// Cada regra tem o seu defeito injetado, lido pela propria linha dela; a cobranca tem de reprovar com ele.
///
///     Godot --headless --server --port 8110 --recursosteste
/// </summary>
public partial class GameServer
{
	private int _rcOk, _rcFalhou;

	private void AfirmarRc(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _rcOk++; GD.Print($"[recursos]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _rcFalhou++; GD.PrintErr($"[recursos]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDosRecursos()
	{
		_rcOk = _rcFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[recursos] ================ A ECONOMIA DE RECURSOS (dono, 2026-10-10) ================");

		// A PASTA E DE MENTIRA: o que esta bancada grava (o `reservas.json`) cai numa caixa temporaria, e a
		// memoria e relida da pasta de verdade no fim. O mesmo desenho do `NaCaixa` da `--wipeteste`.
		AccountStore? real = _store;
		string caixa = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
			"jandirus-recursosteste-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));

		try
		{
			System.IO.Directory.CreateDirectory(caixa);
			_store = new AccountStore(caixa);
			ZerarReservas();

			AfirmarRc("(montagem) a Terra tem mapa e o catalogo de obras esta carregado", _pjMapa != null && _obras != null);
			if (_pjMapa == null || _obras == null) return;

			RcDaRegra();
			RcDaReserva();
			RcDoDisco();
			RcDaCarteira();
			LimparTudoDaBancada();
			RcDeMinerar();
			LimparTudoDaBancada();
			RcDePagar();
			LimparTudoDaBancada();
			RcDoCatalogo();
			LimparTudoDaBancada();
			RcDoQuePassouAAceitarRecursos();
		}
		catch (Exception e) { AfirmarRc("a bancada correu ate o fim sem excecao", false, e.ToString()); }
		finally
		{
			Recurso.SemDobroDeTeste = Recurso.ReservaNaoRegeneraDeTeste = Recurso.FerramentaNaoContaDeTeste = false;
			CatalogoDeObras.AcimaDoTetoViraOfertaDeTeste = false;
			MortoTemReservaDeTeste = ReservasSoNaMemoriaDeTeste = CarteiraMudaDeTeste = CobraSemSaldoDeTeste = false;
			MineraAndandoDeTeste = MineraDoNadaDeTeste = false;
			CarteiraSoPeloFunilDeTeste = SemPortaoDoEsmagamentoDeTeste = SoZeniPagaDeTeste = false;
			CatalogoDeItens.FerramentaVoltaACavarDeTeste = false;
			LimparTudoDaBancada();

			// O MUNDO VOLTA: a pasta de verdade, e as reservas que ela diz.
			_store = real;
			CarregarReservas();
			try { System.IO.Directory.Delete(caixa, recursive: true); } catch { /* temporario */ }

			GD.Print($"[recursos] ================ {_rcOk} OK, {_rcFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// 1) A REGRA
	// =====================================================================
	private void RcDaRegra()
	{
		GD.Print("[recursos] -- 1) a regra: o dobro, o tamanho, a ferramenta, o tempo --");

		// ---- o dobro
		bool Dobro() => Recurso.CustoEmRecursos(1000) == 2000 && Recurso.CustoEmRecursos(15) == 30
						&& Recurso.CustoEmRecursos(0) == 0 && Recurso.CustoEmRecursos(-5) == 0;
		AfirmarRc("o exemplo do dono: o que custa 1000 zeni custa 2000 recursos (e 15 -> 30, 0 -> 0, negativo -> 0)", Dobro(),
				  $"1000 -> {Recurso.CustoEmRecursos(1000)}");
		Recurso.SemDobroDeTeste = true;
		try { AfirmarRc("(defeito injetado: sem o dobro) 1000 zeni viram 1000 recursos -- a cobranca acusa", !Dobro(), $"1000 -> {Recurso.CustoEmRecursos(1000)}"); }
		finally { Recurso.SemDobroDeTeste = false; }

		// ---- o teto pelo tamanho
		AfirmarRc("a reserva maxima sai da AREA: lado 192 guarda 73.728, lado 500 guarda 500.000, lado 1000 guarda 2.000.000",
				  Recurso.MaximoDoPlaneta(192) == 73_728 && Recurso.MaximoDoPlaneta(500) == 500_000 && Recurso.MaximoDoPlaneta(1000) == 2_000_000,
				  $"{Recurso.MaximoDoPlaneta(192):N0} / {Recurso.MaximoDoPlaneta(500):N0} / {Recurso.MaximoDoPlaneta(1000):N0}");
		AfirmarRc("...e planeta sem tamanho (lado 0 ou negativo) nao tem reserva", Recurso.MaximoDoPlaneta(0) == 0 && Recurso.MaximoDoPlaneta(-3) == 0);

		// ---- a ferramenta
		var vazia = new Inventario();
		var comPa = new Inventario();
		comPa.Guardar(CatalogoDeItens.Pa);
		var comAsDuas = new Inventario();
		comAsDuas.Guardar(CatalogoDeItens.Pa);
		comAsDuas.Guardar(CatalogoDeItens.Furadeira);

		bool Ordem() => Recurso.MelhorFerramenta(vazia) == Ferramenta.Mao && Recurso.MelhorFerramenta(comPa) == Ferramenta.Pa
						&& Recurso.MelhorFerramenta(comAsDuas) == Ferramenta.Furadeira
						&& Recurso.PorSegundo(Recurso.MelhorFerramenta(vazia)) < Recurso.PorSegundo(Recurso.MelhorFerramenta(comPa))
						&& Recurso.PorSegundo(Recurso.MelhorFerramenta(comPa)) < Recurso.PorSegundo(Recurso.MelhorFerramenta(comAsDuas));
		AfirmarRc("a MELHOR ferramenta da mochila manda, e a ordem e a do pedido: mao < pa < furadeira",
				  Ordem(), $"{Recurso.PorSegundoNaMao}/s < {Recurso.PorSegundoComPa}/s < {Recurso.PorSegundoComFuradeira}/s");
		Recurso.FerramentaNaoContaDeTeste = true;
		try { AfirmarRc("(defeito injetado: a ferramenta nao conta) pa e furadeira rendem o mesmo que a mao -- a cobranca acusa", !Ordem()); }
		finally { Recurso.FerramentaNaoContaDeTeste = false; }

		// ---- tirar e voltar
		var r = new ReservaDePlaneta(1000);
		double saiu1 = r.Tirar(300), saiu2 = r.Tirar(5000), saiu3 = r.Tirar(10);
		AfirmarRc("`Tirar` devolve o que SAIU: 300 de 1000, depois so os 700 que restavam, depois nada",
				  saiu1 == 300 && saiu2 == 700 && saiu3 == 0 && r.Atual == 0 && r.Fracao == 0 && r.Mexidas == 2,
				  $"{saiu1} / {saiu2} / {saiu3}, mexidas {r.Mexidas}");
		AfirmarRc("...e pedido nulo, negativo ou NaN nao tira nada", r.Tirar(0) == 0 && r.Tirar(-4) == 0 && r.Tirar(double.NaN) == 0);

		double metade = Recurso.HorasPraEncher * 3600 / 2;
		bool Volta()
		{
			var seca = new ReservaDePlaneta(1000, 0);
			seca.Regenerar(metade);
			double naMetade = seca.Atual;
			seca.Regenerar(metade);
			double cheia = seca.Atual;
			seca.Regenerar(metade * 10);
			return Math.Abs(naMetade - 500) < 0.001 && Math.Abs(cheia - 1000) < 0.001 && seca.Atual == 1000 && seca.Cheia;
		}
		AfirmarRc($"a reserva zerada volta a METADE em {Recurso.HorasPraEncher / 2:0} h, enche em {Recurso.HorasPraEncher:0} h, e nunca passa do teto", Volta());
		Recurso.ReservaNaoRegeneraDeTeste = true;
		try { AfirmarRc("(defeito injetado: a reserva nao regenera) ela fica zerada pra sempre -- a cobranca acusa", !Volta()); }
		finally { Recurso.ReservaNaoRegeneraDeTeste = false; }

		// ---- a carteira de cada moeda
		AfirmarRc("o veredito refeito com a carteira: 1000 zeni pagam o que custa 1000; 1999 recursos NAO pagam, 2000 pagam",
				  CatalogoDeObras.ComSaldo(RecusaObra.SemZeni, 1000, 1000, Moeda.Zeni) == RecusaObra.Pode
				  && CatalogoDeObras.ComSaldo(RecusaObra.Pode, 1000, 999, Moeda.Zeni) == RecusaObra.SemZeni
				  && CatalogoDeObras.ComSaldo(RecusaObra.Pode, 1000, 1999, Moeda.Recursos) == RecusaObra.SemRecursos
				  && CatalogoDeObras.ComSaldo(RecusaObra.SemZeni, 1000, 2000, Moeda.Recursos) == RecusaObra.Pode);
		AfirmarRc("...e o que o servidor recusou por TECNOLOGIA ou RACA nenhuma carteira destrava",
				  CatalogoDeObras.ComSaldo(RecusaObra.SemTech, 10, 1e9, Moeda.Zeni) == RecusaObra.SemTech
				  && CatalogoDeObras.ComSaldo(RecusaObra.SemTech, 10, 1e9, Moeda.Recursos) == RecusaObra.SemTech
				  && CatalogoDeObras.ComSaldo(RecusaObra.RacaErrada, 10, 1e9, Moeda.Recursos) == RecusaObra.RacaErrada);
	}

	// =====================================================================
	// 2) A RESERVA DE CADA PLANETA
	// =====================================================================
	/// <summary>Dois mundos GERADOS do universo desta rodada, de tamanhos diferentes: o menor e o maior da vizinhanca.</summary>
	private (ZoneKey Menor, ZoneKey Maior)? DoisMundosGerados()
	{
		var vistos = new List<(ZoneKey Zona, int Lado)>();
		for (int sy = -8; sy <= 8; sy++)
			for (int sx = -8; sx <= 8; sx++)
			{
				if (Sistemas.Do(SeedDoUniverso, sx, sy) is not { } s) continue;
				for (int k = 0; k < s.Orbitas; k++)
				{
					PlanetaNoEspaco p = s.Planeta(k);
					if (p.Premade) continue;
					ZoneKey z = Espaco.ZonaDe(p);
					vistos.Add((z, MundoProcedural.DaSeed(z.Seed, z.Name).Lado));
				}
			}
		if (vistos.Count < 2) return null;
		(ZoneKey Zona, int Lado) menor = vistos.MinBy(v => v.Lado), maior = vistos.MaxBy(v => v.Lado);
		return menor.Lado < maior.Lado ? (menor.Zona, maior.Zona) : null;
	}

	private void RcDaReserva()
	{
		GD.Print("[recursos] -- 2) a reserva: quem tem, quanto, e o tique que a devolve --");

		ZoneKey terra = ZonaDaBancadaDeProjetil;
		int ladoDaTerra = MedidasDoPlaneta(terra).Lado;
		ReservaDePlaneta? daTerra = ReservaDe(terra);
		AfirmarRc("a Terra tem reserva, CHEIA na primeira consulta, do tamanho que o lado do mapa manda",
				  daTerra is { Cheia: true } && daTerra.Maximo == Recurso.MaximoDoPlaneta(ladoDaTerra) && daTerra.Maximo > 0,
				  $"lado {ladoDaTerra}, reserva {daTerra?.Atual:N0} de {daTerra?.Maximo:N0}");
		AfirmarRc("...e perguntar de novo devolve A MESMA reserva (o que um tira, o outro ve)",
				  daTerra != null && ReferenceEquals(daTerra, ReservaDe(terra)));

		// ---- onde NAO ha o que minerar
		AfirmarRc("o Lookout nao tem reserva (o dono: \"menos em locais como lookout\")", ReservaDe(ZoneKey.Premade("Lookout")) == null);
		AfirmarRc("...nem o espaco, nem o interior de uma nave, nem o Outro Mundo",
				  ReservaDe(Espaco.Zona(SeedDoUniverso)) == null && ReservaDe(ZoneKey.Interior("Nave", 7)) == null
				  && ReservaDe(ZoneKey.Premade("Afterlife")) == null && ReservaDe(ZoneKey.Premade("Hell")) == null);
		AfirmarRc("CONTRASTE: os outros planetas de pouso tem (Namek, Vegeta)",
				  ReservaDe(ZoneKey.Premade("Namek")) != null && ReservaDe(ZoneKey.Premade("Vegeta")) != null);

		// ---- o tamanho, em mundo gerado (que nem precisa estar carregado)
		if (DoisMundosGerados() is { } par)
		{
			ReservaDePlaneta? menor = ReservaDe(par.Menor), maior = ReservaDe(par.Maior);
			int ladoMenor = MundoProcedural.DaSeed(par.Menor.Seed, par.Menor.Name).Lado,
				ladoMaior = MundoProcedural.DaSeed(par.Maior.Seed, par.Maior.Name).Lado;
			AfirmarRc("mundo GERADO tem reserva mesmo sem estar carregado, e o MAIOR guarda mais (\"baseado em seu tamanho\")",
					  menor != null && maior != null && MapaDaZona(par.Menor) == null
					  && menor.Maximo == Recurso.MaximoDoPlaneta(ladoMenor) && maior.Maximo == Recurso.MaximoDoPlaneta(ladoMaior)
					  && menor.Maximo < maior.Maximo,
					  $"{par.Menor.Name} lado {ladoMenor} = {menor?.Maximo:N0}  <  {par.Maior.Name} lado {ladoMaior} = {maior?.Maximo:N0}");
		}
		else AfirmarRc("achei dois mundos gerados de tamanhos diferentes na vizinhanca da origem", false, "a varredura do universo nao achou");

		// ---- planeta morto
		ZoneKey vegeta = ZoneKey.Premade("Vegeta");
		using (PalcoDeMortesDeBancada())
		{
			ReservaDePlaneta? antes = ReservaDe(vegeta);
			antes?.Tirar(antes.Maximo / 2);

			bool MortoSemReserva()
			{
				_mortos.Por(new EstadoDaMorte { Chave = "Vegeta", Nome = "Vegeta", Fase = FaseDaMorte.Destruido, Motivo = "bancada dos recursos" });
				bool nulo = ReservaDe(vegeta) == null;
				_mortos.Tirar(new ChaveDePlaneta(true, "Vegeta", 0));
				return nulo;
			}
			AfirmarRc("planeta DESTRUIDO nao tem reserva", antes != null && MortoSemReserva());
			ReservaDePlaneta? depois = ReservaDe(vegeta);
			AfirmarRc("...e RESTAURADO volta com ela CHEIA (um mundo novo, e nao o buraco de antes)",
					  depois is { Cheia: true } && !ReferenceEquals(antes, depois), $"{depois?.Fracao * 100:0}%");

			depois?.Tirar(100);
			MortoTemReservaDeTeste = true;
			try { AfirmarRc("(defeito injetado: o planeta morto continua com reserva) a cobranca acusa", !MortoSemReserva()); }
			finally { MortoTemReservaDeTeste = false; }

			// E O TIQUE TIRA O REGISTRO DE QUEM MORREU, mesmo sem ninguem perguntar por ele.
			_mortos.Por(new EstadoDaMorte { Chave = "Vegeta", Nome = "Vegeta", Fase = FaseDaMorte.Destruido, Motivo = "bancada dos recursos" });
			bool estava = _reservas.ContainsKey(vegeta.Hash);
			UmaVoltaDosRecursos(1);
			AfirmarRc("o tique apaga o registro do planeta que morreu (o livro nao guarda reserva de mundo que nao existe)",
					  estava && !_reservas.ContainsKey(vegeta.Hash));
			_mortos.Tirar(new ChaveDePlaneta(true, "Vegeta", 0));
		}

		// ---- o tique que devolve
		ZerarReservas();
		ReservaDePlaneta r = ReservaDe(terra)!;
		r.Tirar(r.Maximo);   // seca
		UmaVoltaDosRecursos(1);                       // a primeira volta so marca a hora
		double antesDasHoras = r.Atual;
		UmaVoltaDosRecursos(6 * 3600);
		double emSeisHoras = r.Atual - antesDasHoras, esperado = r.Maximo * 6 / Recurso.HorasPraEncher;
		AfirmarRc($"pelo TIQUE de producao, 6 h de relogio devolvem 6/{Recurso.HorasPraEncher:0} do teto",
				  Math.Abs(emSeisHoras - esperado) < r.Maximo * 0.001, $"voltaram {emSeisHoras:N0}, esperado {esperado:N0}");
		UmaVoltaDosRecursos(40 * 3600);
		AfirmarRc("...e depois de mais 40 h ela esta cheia, sem passar do teto", r.Cheia && r.Atual == r.Maximo, $"{r.Atual:N0} de {r.Maximo:N0}");

		r.Tirar(r.Maximo);
		Recurso.ReservaNaoRegeneraDeTeste = true;
		try
		{
			UmaVoltaDosRecursos(6 * 3600);
			AfirmarRc("(defeito injetado: a reserva nao regenera) 6 h depois ela continua seca -- a cobranca acusa", r.Atual == 0);
		}
		finally { Recurso.ReservaNaoRegeneraDeTeste = false; }
	}

	/// <summary>
	/// UMA VOLTA DO TIQUE DE PRODUCAO, com o relogio de parede adiantado em <paramref name="segundos"/>. O relogio
	/// de bancada so anda pra frente (ver `AdiantoDoRelogioDeTeste`); a volta e o `TickDosRecursos` de verdade.
	/// </summary>
	private void UmaVoltaDosRecursos(double segundos)
	{
		AdiantoDoRelogioDeTeste += (long)(Math.Max(segundos, 1.1) * 1000);
		TickDosRecursos();
	}

	// =====================================================================
	// 2b) O DISCO
	// =====================================================================
	private void RcDoDisco()
	{
		GD.Print("[recursos] -- 2b) o disco: o que vai, o que nao vai, e o reinicio --");

		ZoneKey terra = ZonaDaBancadaDeProjetil, namek = ZoneKey.Premade("Namek");
		(ZoneKey Menor, ZoneKey Maior)? par = DoisMundosGerados();

		ZerarReservas();
		try { System.IO.File.Delete(CaminhoDasReservas); } catch { /* ainda nao existe */ }
		_proximaGravacaoDasReservas = 0;

		ReservaDePlaneta daTerra = ReservaDe(terra)!;
		ReservaDe(namek);                                  // consultada e CHEIA: nao pode ir pro arquivo
		daTerra.Tirar(daTerra.Maximo * 0.4);
		ReservaDePlaneta? doGerado = par is { } p0 ? ReservaDe(p0.Menor) : null;
		doGerado?.Tirar(doGerado.Maximo * 0.9);

		AfirmarRc("quem TIROU deixou o livro por gravar -- e ninguem precisou avisar o servidor", ReservasPorGravar());
		UmaVoltaDosRecursos(1);
		string texto = System.IO.File.Exists(CaminhoDasReservas) ? System.IO.File.ReadAllText(CaminhoDasReservas) : "";
		AfirmarRc("o tique gravou o `reservas.json` com as MEXIDAS (a Terra e o mundo gerado) e sem a cheia (Namek)",
				  texto.Contains("\"Earth\"") && !texto.Contains("\"Namek\"") && (doGerado == null || texto.Contains(doGerado.Chave))
				  && !ReservasPorGravar(),
				  texto.Length > 0 ? texto.ReplaceLineEndings(" ")[..Math.Min(160, texto.Length)] : "sem arquivo");

		byte[] bytesAntes = System.IO.File.Exists(CaminhoDasReservas) ? System.IO.File.ReadAllBytes(CaminhoDasReservas) : [];
		UmaVoltaDosRecursos(120);
		AfirmarRc("sem ninguem tirar nada, dois minutos de tique NAO regravam o arquivo (o que cresce se deriva da hora)",
				  bytesAntes.Length > 0 && bytesAntes.AsSpan().SequenceEqual(System.IO.File.ReadAllBytes(CaminhoDasReservas)));

		// ---- o reinicio: a memoria some, o arquivo fica
		double terraNoDisco = daTerra.Atual, teto = daTerra.Maximo;
		daTerra.Tirar(1);            // garante um arquivo com a hora de AGORA
		_proximaGravacaoDasReservas = 0;
		UmaVoltaDosRecursos(1);
		terraNoDisco = daTerra.Atual;
		double geradoNoDisco = doGerado?.Atual ?? 0;
		byte[] bytesDoBoot = System.IO.File.ReadAllBytes(CaminhoDasReservas);

		(double Terra, double Gerado, bool Namek, bool Intacto) Reiniciar(double horasDepois)
		{
			AdiantoDoRelogioDeTeste += (long)(horasDepois * 3600 * 1000);
			CarregarReservas();
			bool intacto = bytesDoBoot.AsSpan().SequenceEqual(System.IO.File.ReadAllBytes(CaminhoDasReservas));
			bool namekVeio = _reservas.ContainsKey(namek.Hash);
			return (ReservaDe(terra)!.Atual, par is { } p1 ? ReservaDe(p1.Menor)!.Atual : 0, namekVeio, intacto);
		}

		var logo = Reiniciar(0);
		AfirmarRc("REINICIO na hora: a Terra e o mundo gerado voltam do disco como estavam, e o carregador NAO regrava a pasta",
				  Math.Abs(logo.Terra - terraNoDisco) < 1 && Math.Abs(logo.Gerado - geradoNoDisco) < 1 && !logo.Namek && logo.Intacto,
				  $"Terra {logo.Terra:N0} (disco {terraNoDisco:N0}), gerado {logo.Gerado:N0} (disco {geradoNoDisco:N0})");

		var seisHoras = Reiniciar(6);
		double esperada = Math.Min(teto, terraNoDisco + teto * 6 / Recurso.HorasPraEncher);
		AfirmarRc($"REINICIO 6 h depois: o que cresceu com o servidor DESLIGADO esta la (6/{Recurso.HorasPraEncher:0} do teto), e a pasta segue intacta",
				  Math.Abs(seisHoras.Terra - esperada) < teto * 0.001 && seisHoras.Terra > terraNoDisco && seisHoras.Intacto,
				  $"Terra {seisHoras.Terra:N0}, esperado {esperada:N0}");

		// ---- CONTRA-EXEMPLO: so na memoria
		ReservaDePlaneta outra = ReservaDe(namek)!;
		outra.Tirar(outra.Maximo * 0.5);
		ReservasSoNaMemoriaDeTeste = true;
		try
		{
			_proximaGravacaoDasReservas = 0;
			UmaVoltaDosRecursos(1);
			CarregarReservas();
			AfirmarRc("(defeito injetado: a reserva so existe na memoria) Namek, minerada pela metade, volta CHEIA do reinicio -- a cobranca acusa",
					  ReservaDe(namek) is { Cheia: true });
		}
		finally { ReservasSoNaMemoriaDeTeste = false; }

		// ---- a mesma cena, sem o defeito
		ZerarReservas();
		ReservaDePlaneta deNovo = ReservaDe(namek)!;
		deNovo.Tirar(deNovo.Maximo * 0.5);
		_proximaGravacaoDasReservas = 0;
		UmaVoltaDosRecursos(1);
		CarregarReservas();
		AfirmarRc("...e sem o defeito ela volta pela METADE", ReservaDe(namek) is { } n && Math.Abs(n.Fracao - 0.5) < 0.01, $"{ReservaDe(namek)?.Fracao * 100:0}%");

		// ---- a limpeza total conhece o arquivo
		SistemaDoMundo? inscrito = _sistemasDoMundo.Find(s => s.Arquivos.Contains("reservas.json"));
		int contava = inscrito?.Contar() ?? -1;
		inscrito?.Zerar();
		AfirmarRc("a limpeza total tem o `reservas.json` inscrito: conta as mexidas e as zera (todo planeta volta cheio)",
				  inscrito != null && contava >= 1 && inscrito.Contar() == 0 && ReservaDe(namek) is { Cheia: true },
				  $"contava {contava}, ficou {inscrito?.Contar()}");
	}

	// =====================================================================
	// 3) A CARTEIRA
	// =====================================================================
	private void RcDaCarteira()
	{
		GD.Print("[recursos] -- 3) a carteira: dar, cobrar, recusar -- e o cliente sabendo --");

		ServerPlayer pl = Forjar("Rico", CorredorLivre(6), 1000);
		pl.Ficha.Zeni = 5000;
		pl.Ficha.Recursos = 0;

		long antes = _carteirasMandadas;
		DarRecursos(pl, 300);
		AfirmarRc("`DarRecursos` soma no saldo e manda a carteira nova pro cliente", pl.Ficha.Recursos == 300 && _carteirasMandadas == antes + 1);
		antes = _carteirasMandadas;
		DarRecursos(pl, 0); DarRecursos(pl, -50); DarRecursos(pl, double.NaN);
		AfirmarRc("...e quantia nula, negativa ou NaN nao muda nada (nem manda pacote)", pl.Ficha.Recursos == 300 && _carteirasMandadas == antes);

		CarteiraMudaDeTeste = true;
		try
		{
			antes = _carteirasMandadas;
			DarRecursos(pl, 1);
			AfirmarRc("(defeito injetado: o saldo muda calado) o cliente nao fica sabendo -- a cobranca acusa", _carteirasMandadas == antes);
			pl.Ficha.Recursos = 300;
		}
		finally { CarteiraMudaDeTeste = false; }

		// ---- cobrar
		antes = _carteirasMandadas;
		bool curto = CobrarRecursos(pl, 301);
		AfirmarRc("`CobrarRecursos` sem saldo: FALSO, e nao cobra nem um pedaco", !curto && pl.Ficha.Recursos == 300 && _carteirasMandadas == antes);
		bool exato = CobrarRecursos(pl, 300);
		AfirmarRc("...e com o saldo exato cobra tudo e avisa o cliente", exato && pl.Ficha.Recursos == 0 && _carteirasMandadas == antes + 1);
		AfirmarRc("...e quantia negativa ou NaN e recusada", !CobrarRecursos(pl, -1) && !CobrarRecursos(pl, double.NaN) && pl.Ficha.Recursos == 0);

		CobraSemSaldoDeTeste = true;
		try
		{
			bool cobrou = CobrarRecursos(pl, 50);
			AfirmarRc("(defeito injetado: cobra de quem nao tem) o saldo fica negativo -- a cobranca acusa", cobrou && pl.Ficha.Recursos < 0);
			pl.Ficha.Recursos = 0;
		}
		finally { CobraSemSaldoDeTeste = false; }

		// ---- a moeda escolhida, e nunca a outra
		pl.Ficha.Zeni = 5000; pl.Ficha.Recursos = 5000;
		bool emZeni = Cobrar(pl, 1000, Moeda.Zeni);
		AfirmarRc("`Cobrar` em ZENI tira o preco do zeni e nao toca nos recursos", emZeni && pl.Ficha.Zeni == 4000 && pl.Ficha.Recursos == 5000);
		bool emRecursos = Cobrar(pl, 1000, Moeda.Recursos);
		AfirmarRc("`Cobrar` em RECURSOS tira o DOBRO dos recursos e nao toca no zeni", emRecursos && pl.Ficha.Zeni == 4000 && pl.Ficha.Recursos == 3000);

		pl.Ficha.Zeni = 1_000_000; pl.Ficha.Recursos = 1999;
		bool completou = Cobrar(pl, 1000, Moeda.Recursos);
		AfirmarRc("com 1.999 recursos e um MILHAO de zeni, pagar 1000 em recursos e recusado: nunca completa com zeni",
				  !completou && pl.Ficha.Zeni == 1_000_000 && pl.Ficha.Recursos == 1999);
		pl.Ficha.Zeni = 999; pl.Ficha.Recursos = 1_000_000;
		AfirmarRc("...e o avesso: 999 zeni e um milhao de recursos nao pagam 1000 em ZENI",
				  !Cobrar(pl, 1000, Moeda.Zeni) && pl.Ficha.Zeni == 999 && pl.Ficha.Recursos == 1_000_000);

		antes = _carteirasMandadas;
		MandarCatalogoDeObras(pl);
		AfirmarRc("a carteira vai atras de todo catalogo de construcoes (e por ela que os recursos chegam no login)", _carteirasMandadas == antes + 1);

		RcDaVigiaDaCarteira(pl);
	}

	/// <summary>
	/// A VIGIA DA CARTEIRA: o zeni e o recurso que mudam POR FORA do funil -- a ficha escrita direto, como fazem a
	/// missao de cargo, o premio do torneio e o desejo de riqueza -- chegam ao cliente no tique
	/// seguinte, uma vez, e sem ninguem ter de lembrar de avisar. O achado e do agente `construcao` (2026-10-10):
	/// as abas Stats e Tech ficavam com o zeni velho ate o proximo catalogo.
	/// </summary>
	private void RcDaVigiaDaCarteira(ServerPlayer pl)
	{
		VigiarAsCarteiras();   // poe em dia todo corpo forjado ate aqui (as secoes de cima escrevem direto na ficha)
		long antes = _carteirasMandadas;
		for (int i = 0; i < 3; i++) VigiarAsCarteiras();
		AfirmarRc("com a carteira em dia a vigia nao manda NADA: tres tiques, nenhum pacote", _carteirasMandadas == antes && CarteiraEmDia(pl));

		// ---- a ficha escrita direto, como a missao de cargo e o premio do torneio fazem
		pl.Ficha.Zeni += 777;
		bool ficouVelha = !CarteiraEmDia(pl);
		antes = _carteirasMandadas;
		VigiarAsCarteiras();
		bool chegou = _carteirasMandadas == antes + 1 && CarteiraEmDia(pl);
		VigiarAsCarteiras();
		AfirmarRc("**ZENI QUE MUDA POR FORA DO FUNIL CHEGA AO CLIENTE**: a ficha escrita direto sai na carteira do tique seguinte -- e uma vez so",
				  ficouVelha && chegou && _carteirasMandadas == antes + 1);

		// ---- um caminho de PRODUCAO que nunca avisou ninguem: o desejo de riqueza ao dragao (`DesejoDeDinheiro`).
		// (Ate 2026-10-10 a prova daqui era a cobranca das melhorias de maquina; elas passaram a cobrar pelo
		// contrato, que avisa na hora -- secao 7.)
		pl.Ficha.Zeni = 5000;
		VigiarAsCarteiras();
		antes = _carteirasMandadas;
		DesejoDeDinheiro(pl, new SetDeEsferas { NomeDoDragao = "SHENRON" });
		bool calado = _carteirasMandadas == antes && !CarteiraEmDia(pl);
		VigiarAsCarteiras();
		AfirmarRc("o desejo de RIQUEZA ao dragao (que soma o zeni CALADO) e contado ao cliente pela vigia",
				  pl.Ficha.Zeni == 5000 + Desejos.ZeniDoDesejo && calado && _carteirasMandadas == antes + 1 && CarteiraEmDia(pl),
				  $"zeni {pl.Ficha.Zeni:0}, pacotes +{_carteirasMandadas - antes}");

		// ---- os recursos tambem (quem escrever neles por fora do contrato)
		pl.Ficha.Recursos += 9;
		antes = _carteirasMandadas;
		VigiarAsCarteiras();
		AfirmarRc("...e o RECURSO escrito direto na ficha, do mesmo jeito", _carteirasMandadas == antes + 1 && CarteiraEmDia(pl));

		// ---- quem acabou de entrar
		ServerPlayer novo = Forjar("Recem-chegado", CorredorLivre(6), 1000);
		novo.Ficha.Zeni = 42;
		bool nuncaOuviu = !CarteiraEmDia(novo);
		antes = _carteirasMandadas;
		VigiarAsCarteiras();
		AfirmarRc("quem acabou de entrar ouve a carteira no PRIMEIRO tique (o corpo novo nasce sem registro)",
				  nuncaOuviu && _carteirasMandadas == antes + 1 && CarteiraEmDia(novo));

		// ---- o tique de producao
		pl.Ficha.Zeni += 1;
		antes = _carteirasMandadas;
		TickDosRecursos();
		AfirmarRc("a vigia mora no tique de producao (`TickDosRecursos`), ANTES do portao de um segundo: nao espera a volta",
				  _carteirasMandadas == antes + 1 && CarteiraEmDia(pl));

		// ---- contra-exemplo
		CarteiraSoPeloFunilDeTeste = true;
		try
		{
			pl.Ficha.Zeni += 100;
			antes = _carteirasMandadas;
			for (int i = 0; i < 3; i++) TickDosRecursos();
			AfirmarRc("(defeito injetado: so o funil avisa) o zeni muda por fora e, tres tiques depois, o cliente segue com o numero velho -- a cobranca acusa",
					  _carteirasMandadas == antes && !CarteiraEmDia(pl));
		}
		finally { CarteiraSoPeloFunilDeTeste = false; }
		VigiarAsCarteiras();
	}

	// =====================================================================
	// 4) MINERAR
	// =====================================================================
	private void RcDeMinerar()
	{
		GD.Print("[recursos] -- 4) minerar: rende o que SAI da reserva, e cai como a meditacao --");

		ZoneKey terra = ZonaDaBancadaDeProjetil;
		ZerarReservas();
		ReservaDePlaneta reserva = ReservaDe(terra)!;

		ServerPlayer pl = Forjar("Mineiro", CorredorLivre(6), 1000);
		pl.Ficha.Recursos = 0;
		pl.Ficha.Zeni = 123;

		// ---- comecar
		List<string> falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Minerando));
		AfirmarRc("declarar `Minerando` (o verb Minerar) comeca a mineracao: sem treino nem meditacao, e o jogador le quanto vai render",
				  pl.Mineracao != null && !pl.Ficha.train && !pl.Ficha.med && Disse(falas, $"+{Recurso.PorSegundoNaMao:0} recursos por segundo")
				  && Disse(falas, "100%"), Ultimos(falas));
		AfirmarRc("quem minera veste a pose de treino pros outros, e ocupa o chao como quem treina",
				  pl.Pose(NowMs(), false) == Protocol.Pose.Treinando && OcupacaoDe(pl) == Ocupacao.Treinando,
				  $"{pl.Pose(NowMs(), false)} / {OcupacaoDe(pl)}");

		// ---- o segundo
		double reservaAntes = reserva.Atual;
		for (int i = 0; i < 5; i++) TickDaMineracao();
		AfirmarRc($"cinco segundos na mao rendem {5 * Recurso.PorSegundoNaMao:0} recursos -- e a reserva do planeta desceu EXATAMENTE isso",
				  pl.Ficha.Recursos == 5 * Recurso.PorSegundoNaMao && reservaAntes - reserva.Atual == pl.Ficha.Recursos && pl.Ficha.Zeni == 123,
				  $"bolso {pl.Ficha.Recursos:0}, reserva -{reservaAntes - reserva.Atual:0}");

		// ---- a ferramenta, relida a cada segundo
		double Segundo() { double a = pl.Ficha.Recursos; TickDaMineracao(); return pl.Ficha.Recursos - a; }
		(double Mao, double Pa, double Furadeira, double DeVolta) Degraus()
		{
			double mao = Segundo();
			pl.Mochila.Guardar(CatalogoDeItens.Pa);
			double pa = Segundo();
			pl.Mochila.Guardar(CatalogoDeItens.Furadeira);
			double furadeira = Segundo();
			pl.Mochila.Tirar(CatalogoDeItens.Furadeira);
			pl.Mochila.Tirar(CatalogoDeItens.Pa);
			return (mao, pa, furadeira, Segundo());
		}
		var d = Degraus();
		AfirmarRc("com a PA na mochila o segundo rende mais; com a FURADEIRA, mais ainda; sem as duas volta ao da mao",
				  d.Mao == Recurso.PorSegundoNaMao && d.Pa == Recurso.PorSegundoComPa && d.Furadeira == Recurso.PorSegundoComFuradeira
				  && d.Mao < d.Pa && d.Pa < d.Furadeira && d.DeVolta == d.Mao, $"{d.Mao} < {d.Pa} < {d.Furadeira}, e de volta {d.DeVolta}");
		Recurso.FerramentaNaoContaDeTeste = true;
		try
		{
			var semFerramenta = Degraus();
			AfirmarRc("(defeito injetado: a ferramenta nao conta) pa e furadeira rendem o da mao -- a cobranca acusa",
					  semFerramenta.Pa == semFerramenta.Mao && semFerramenta.Furadeira == semFerramenta.Mao);
		}
		finally { Recurso.FerramentaNaoContaDeTeste = false; }

		RcDoCavarQueSaiu(pl);

		// ---- ANDAR derruba
		bool AndarDerruba()
		{
			if (pl.Mineracao == null) DefinirAtividade(pl, Protocol.Activity.Minerando);
			Vec2 onde = pl.Pos;
			double bolso = pl.Ficha.Recursos;
			pl.Pos = onde + new Vec2(ZoneCollision.TileSize, 0);
			List<string> f = Ouvir(TickDaMineracao);
			bool caiu = pl.Mineracao == null && pl.Ficha.Recursos == bolso && Disse(f, "para de minerar");
			pl.Pos = onde;
			return caiu;
		}
		AfirmarRc("ANDAR derruba a mineracao (como a meditacao): o segundo em que o corpo saiu do lugar nao rende, e o jogador e avisado", AndarDerruba());
		MineraAndandoDeTeste = true;
		try { AfirmarRc("(defeito injetado: minera andando) o corpo sai do lugar e segue rendendo -- a cobranca acusa", !AndarDerruba()); }
		finally { MineraAndandoDeTeste = false; DefinirAtividade(pl, Protocol.Activity.Parado); }

		// ---- declarar outra coisa encerra; treinar e meditar tomam o lugar
		DefinirAtividade(pl, Protocol.Activity.Minerando);
		for (int i = 0; i < 4; i++) TickDaMineracao();
		double naLeva = 4 * Recurso.PorSegundoNaMao;
		falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Parado));
		AfirmarRc($"declarar `Parado` (apertar Minerar de novo) encerra, e a frase diz quanto saiu na LEVA ({naLeva:N0}) e quanto ha no bolso",
				  pl.Mineracao == null && Disse(falas, "para de minerar") && Disse(falas, $"tirou {naLeva:N0} recursos")
				  && Disse(falas, $"tem {pl.Ficha.Recursos:N0}"), Ultimos(falas));
		falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Parado));
		AfirmarRc("...e declarar `Parado` SEM estar minerando nao diz nada (e o que o cliente manda ao andar, o tempo todo)",
				  pl.Mineracao == null && !Disse(falas, "minerar"), Ultimos(falas));
		DefinirAtividade(pl, Protocol.Activity.Minerando);
		DefinirAtividade(pl, Protocol.Activity.Treinando);
		bool treinoTomou = pl.Mineracao == null && pl.Ficha.train;
		DefinirAtividade(pl, Protocol.Activity.Minerando);
		bool mineracaoTomou = pl.Mineracao != null && !pl.Ficha.train;
		DefinirAtividade(pl, Protocol.Activity.Meditando);
		AfirmarRc("as tres atividades se excluem: treinar encerra a mineracao, minerar encerra o treino, meditar encerra a mineracao",
				  treinoTomou && mineracaoTomou && pl.Mineracao == null && pl.Ficha.med);
		DefinirAtividade(pl, Protocol.Activity.Parado);

		// ---- AGIR derruba
		AfirmarRc("todo comando de CORPO derruba a mineracao (soco, guarda, carga de Ki, tecnica, transformar, Zanzoken) -- e so eles",
				  new[] { Protocol.C2S.Action, Protocol.C2S.Guard, Protocol.C2S.Carregar, Protocol.C2S.Habilidade,
						  Protocol.C2S.Transformar, Protocol.C2S.Zanzoken }.All(ComandoQueInterrompeAMineracao)
				  && !new[] { Protocol.C2S.Activity, Protocol.C2S.Chat, Protocol.C2S.Tech, Protocol.C2S.Verbo,
							  Protocol.C2S.InputState, Protocol.C2S.Ping }.Any(ComandoQueInterrompeAMineracao));

		// ---- BRIGAR derruba, e nao deixa comecar
		DefinirAtividade(pl, Protocol.Activity.Minerando);
		pl.Combate.EntrarEmCombate();
		falas = Ouvir(TickDaMineracao);
		bool brigaDerrubou = pl.Mineracao == null && Disse(falas, "briga");
		falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Minerando));
		AfirmarRc("APANHAR OU BATER derruba (o relogio de luta acendeu), e no meio da briga nao se comeca",
				  brigaDerrubou && pl.Mineracao == null && Disse(falas, "briga"), Ultimos(falas));
		pl.Combate.LutandoDeVerdade = 0;
		pl.Combate.EmCombate = 0;
		pl.Combate.BrigandoComAlguem = 0;

		RcDoEsmagadoQueNaoMinera(pl);

		// ---- quem nao pode: no ar, caido
		pl.Voando = true;
		falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Minerando));
		bool noAr = pl.Mineracao == null && Disse(falas, "pouse");
		pl.Voando = false;
		pl.Ficha.KO = true;
		DefinirAtividade(pl, Protocol.Activity.Minerando);
		bool caido = pl.Mineracao == null;
		pl.Ficha.KO = false;
		DefinirAtividade(pl, Protocol.Activity.Minerando);
		pl.Ficha.KO = true;
		TickDaMineracao();
		AfirmarRc("voando nao se minera (\"pouse antes\"), nocauteado nao se comeca, e quem cai minerando para",
				  noAr && caido && pl.Mineracao == null);
		pl.Ficha.KO = false;

		// ---- onde nao ha o que minerar
		ZoneKey casa = pl.Zone;
		pl.Zone = ZoneKey.Premade("Lookout");
		falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Minerando));
		AfirmarRc("no Lookout o verb responde \"não há o que minerar aqui\" e nada comeca", pl.Mineracao == null && Disse(falas, "não há o que minerar aqui"), Ultimos(falas));
		pl.Zone = casa;

		// ---- a reserva seca
		pl.Mochila.Guardar(CatalogoDeItens.Furadeira);
		double taxa = Recurso.PorSegundoComFuradeira;
		double resto = taxa + 5;                      // um segundo inteiro, e um pedaco
		reserva.Tirar(reserva.Atual - resto);
		pl.Ficha.Recursos = 0;
		DefinirAtividade(pl, Protocol.Activity.Minerando);
		TickDaMineracao();
		bool primeiro = pl.Mineracao != null && pl.Ficha.Recursos == taxa;
		falas = Ouvir(TickDaMineracao);
		AfirmarRc("RESERVA SECA: o ultimo segundo paga so o que restava, a mineracao PARA e o jogador e avisado",
				  primeiro && pl.Mineracao == null && pl.Ficha.Recursos == resto && reserva.Atual == 0 && Disse(falas, "secou"),
				  $"bolso {pl.Ficha.Recursos:0} de {resto:0} | {Ultimos(falas)}");
		falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Minerando));
		AfirmarRc("...e num planeta esgotado nem se comeca", pl.Mineracao == null && Disse(falas, "esgotado"), Ultimos(falas));

		// ---- CONTRA-EXEMPLO: recurso do nada
		ZerarReservas();
		reserva = ReservaDe(terra)!;
		reserva.Tirar(reserva.Atual - 100);
		pl.Ficha.Recursos = 0;
		MineraDoNadaDeTeste = true;
		try
		{
			DefinirAtividade(pl, Protocol.Activity.Minerando);
			for (int i = 0; i < 20; i++) TickDaMineracao();
			AfirmarRc("(defeito injetado: a mineracao nao tira da reserva) vinte segundos rendem mais do que o planeta tinha, e ele nem desce -- a cobranca acusa",
					  pl.Ficha.Recursos > 100 && reserva.Atual == 100, $"bolso {pl.Ficha.Recursos:0}, reserva {reserva.Atual:0}");
		}
		finally { MineraDoNadaDeTeste = false; DefinirAtividade(pl, Protocol.Activity.Parado); }

		ZerarReservas();
	}

	/// <summary>
	/// O "CAVAR" SAIU -- dono, 2026-10-10: *"tire a opcao de cavar da pa e furadeira pq e redundante"*. Era a acao
	/// das duas na mochila (`item_cavar`: zeni e uma chance em cinco de XP de tecnologia por cavada). O verb
	/// Minerar tomou o lugar; elas ficaram sem acao e continuam valendo pelo que fazem agora -- ditar a taxa.
	///
	/// O que se cobra e o FIO: um cliente antigo (ou mexido) que ainda mande o comando tem de parar no portao das
	/// acoes do item, com frase e sem excecao, e nada pode render. Que elas nao declaram acao e que a frase delas
	/// mudou e da `instalar-prova` (a familia A0b); que elas aceleram o minerar, do bloco logo acima.
	/// </summary>
	private void RcDoCavarQueSaiu(ServerPlayer pl)
	{
		(string Id, string Nome)[] ferramentas = [(CatalogoDeItens.Pa, "Pá"), (CatalogoDeItens.Furadeira, "Furadeira Manual")];
		foreach ((string id, _) in ferramentas) pl.Mochila.Guardar(id);
		double zeni = pl.Ficha.Zeni, xp = pl.Ficha.techXp, nivel = pl.Ficha.techskill;
		bool Intacto() => pl.Ficha.Zeni == zeni && pl.Ficha.techXp == xp && pl.Ficha.techskill == nivel
						  && ferramentas.All(f => pl.Mochila.Quantos(f.Id) == 1);

		try
		{
			foreach ((string id, string nome) in ferramentas)
			{
				List<string> falas = Ouvir(() => ComandoDeItem(pl, "item_cavar", id));
				AfirmarRc($"**O CAVAR SAIU**: o comando antigo `item_cavar` com a {nome} na mochila e RECUSADO no portao das acoes, com frase -- e nao rende zeni, nem XP de tecnologia, nem gasta o item",
						  Disse(falas, $"não dá pra cavar {nome}.") && !Disse(falas, "zeni") && Intacto(), Ultimos(falas));
			}

			// O CAVAR DE ANTES TINHA RECARGA (6 s a pa, 2,5 s a furadeira) e uma chance em cinco de ensinar: uma
			// tentativa so poderia passar por sorte ou por estar na recarga. Cinquenta seguidas nao.
			for (int i = 0; i < 50; i++) ComandoDeItem(pl, "item_cavar", CatalogoDeItens.Furadeira);
			AfirmarRc("...e cinquenta pedidos seguidos continuam sem render nada", Intacto(),
					  $"zeni {pl.Ficha.Zeni:0} (era {zeni:0}), xp {pl.Ficha.techXp:0.##} (era {xp:0.##})");

			AfirmarRc("as duas CONTINUAM valendo pelo que fazem agora: na mochila, a melhor delas dita a taxa do Minerar",
					  Recurso.MelhorFerramenta(pl.Mochila) == Ferramenta.Furadeira
					  && Recurso.PorSegundo(Ferramenta.Furadeira) > Recurso.PorSegundo(Ferramenta.Pa)
					  && Recurso.PorSegundo(Ferramenta.Pa) > Recurso.PorSegundo(Ferramenta.Mao));

			// ---- contra-exemplo: o botao que devolve o cavar
			CatalogoDeItens.FerramentaVoltaACavarDeTeste = true;
			try
			{
				List<string> falas = Ouvir(() => ComandoDeItem(pl, "item_cavar", CatalogoDeItens.Pa));
				AfirmarRc("(defeito injetado: a pa volta a declarar 'cavar' -- o botao volta) o comando PASSA do portao das acoes, e a cobranca da recusa acusa",
						  !Disse(falas, "não dá pra cavar") && Disse(falas, "'cavar' ainda não faz nada"), Ultimos(falas));
				AfirmarRc("...e mesmo com o botao de volta nao ha o que rodar: o cavar saiu do SERVIDOR, e nada rende", Intacto());
			}
			finally { CatalogoDeItens.FerramentaVoltaACavarDeTeste = false; }
		}
		finally
		{
			CatalogoDeItens.FerramentaVoltaACavarDeTeste = false;
			foreach ((string id, _) in ferramentas) pl.Mochila.Tirar(id, pl.Mochila.Quantos(id));
		}
	}

	/// <summary>
	/// ESMAGADO NAO MINERA -- a decisao do dono de 2026-10-10: *"sim conta como combate, n da pra mineirar se vc
	/// nem consegue se mover"*. Acima do limite do corpo (pela gravidade do lugar ou pelo peso de treino) o verb e
	/// recusado e a mineracao em curso cai; e a frase diz O QUE esmaga, e nao "briga". O aclimatado minera.
	///
	/// O esmagamento e o tique de producao (`TickDoEsmagamento`, 1 Hz), que fere pelo funil de dano de sempre.
	/// </summary>
	private void RcDoEsmagadoQueNaoMinera(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		CombatState luta = pl.Combate;
		(double Planeta, double Maquina, double Dominada, double Peso) antes = (f.Planetgrav, f.gravmult, f.GravMastered, f.weight_ratio);
		void SemFeridaFresca() { luta.LutandoDeVerdade = 0; luta.EmCombate = 0; luta.BrigandoComAlguem = 0; }

		try
		{
			// ---- um planeta DUAS vezes mais pesado do que o corpo domina
			f.gravmult = 0;
			f.weight_ratio = 0;
			f.Planetgrav = 2 * Math.Max(f.GravMastered, 1);
			SemFeridaFresca();

			List<string> falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Minerando));
			AfirmarRc("**ESMAGADO NAO MINERA** (decisao do dono): num chao 2x mais pesado que o corpo o verb e recusado NA HORA -- antes da primeira ferida, com o relogio de luta apagado -- e a frase diz que e a GRAVIDADE, com os numeros",
					  Esmagamento.Esmaga(f) && pl.Mineracao == null && Disse(falas, "a gravidade daqui está te esmagando")
					  && Disse(falas, $"{Esmagamento.Gravidade(f):0.#}x contra {f.GravMastered:0.#}x") && Disse(falas, "você não consegue minerar")
					  && !Disse(falas, "briga"), Ultimos(falas));

			TickDoEsmagamento();
			falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Minerando));
			AfirmarRc("...e depois que o esmagamento FERE (o relogio de luta aceso, sem golpe trocado com ninguem) a frase continua sendo a da gravidade, e nao \"briga\"",
					  luta.LutandoDeVerdade > 0 && !luta.EmBrigaComAlguem && pl.Mineracao == null && Disse(falas, "gravidade") && !Disse(falas, "briga"),
					  Ultimos(falas));

			// ---- contra-exemplo: o portao que nao pergunta pelo esmagamento
			SemPortaoDoEsmagamentoDeTeste = true;
			try
			{
				SemFeridaFresca();   // acabou de pisar no chao pesado: ainda nao levou a primeira ferida
				DefinirAtividade(pl, Protocol.Activity.Minerando);
				bool comecouEsmagado = pl.Mineracao != null;
				TickDoEsmagamento();
				falas = Ouvir(TickDaMineracao);
				AfirmarRc("(defeito injetado: o portao nao pergunta pelo esmagamento) o esmagado COMECA a minerar, e cai um segundo depois sem ouvir da gravidade -- a cobranca acusa",
						  comecouEsmagado && pl.Mineracao == null && !Disse(falas, "gravidade"), Ultimos(falas));
			}
			finally { SemPortaoDoEsmagamentoDeTeste = false; DefinirAtividade(pl, Protocol.Activity.Parado); }

			// ---- o ACLIMATADO: o mesmo planeta, e o corpo que ja domina a gravidade dele
			f.GravMastered = f.Planetgrav;
			SemFeridaFresca();
			TickDoEsmagamento();   // nao fere quem aguenta
			DefinirAtividade(pl, Protocol.Activity.Minerando);
			double bolso = f.Recursos;
			for (int i = 0; i < 3; i++) { TickDoEsmagamento(); TickDaMineracao(); }
			AfirmarRc("o ACLIMATADO minera: no mesmo planeta pesado, o corpo que domina aquela gravidade comeca e segue rendendo, sem ferida e sem relogio de luta",
					  !Esmagamento.Esmaga(f) && luta.LutandoDeVerdade <= 0 && pl.Mineracao != null && f.Recursos == bolso + 3 * Recurso.PorSegundoNaMao,
					  $"razao {Esmagamento.Razao(f):0.##}, +{f.Recursos - bolso:0}");

			// ---- o chao fica mais pesado NO MEIO (uma maquina de gravidade ligada ao lado): a mineracao em curso cai
			f.gravmult = f.Planetgrav;   // o dobro do que ele domina, de novo
			falas = Ouvir(TickDaMineracao);
			AfirmarRc("...e se o chao fica mais pesado que o corpo NO MEIO (uma maquina de gravidade ligada ao lado), a mineracao em curso PARA no segundo seguinte, com a frase da gravidade e a conta da leva",
					  pl.Mineracao == null && f.Recursos == bolso + 3 * Recurso.PorSegundoNaMao && Disse(falas, "a gravidade daqui está te esmagando")
					  && Disse(falas, "você para de minerar") && Disse(falas, "tirou"), Ultimos(falas));
			f.gravmult = 0;

			// ---- o PESO de treino acima do limite
			f.weight_ratio = 2;
			SemFeridaFresca();
			falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Minerando));
			AfirmarRc("o PESO de treino acima do limite barra igual, e a frase e outra: manda tirar os pesos (a saida nao e a mesma)",
					  Esmagamento.Esmaga(f) && Esmagamento.PorPeso(f) && pl.Mineracao == null && Disse(falas, "o peso que você carrega está te esmagando")
					  && Disse(falas, "Tire os pesos") && !Disse(falas, "gravidade"), Ultimos(falas));
			f.weight_ratio = 0;

			// ---- fora do esmagamento, com a ferida ainda fresca: nao e "briga"
			SemFeridaFresca();
			luta.EntrarEmCombate(comAlguem: false);   // a ultima ferida do esmagamento, ha menos de dez segundos
			falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Minerando));
			AfirmarRc("fora do esmagamento com a ferida ainda fresca (o relogio de luta aceso SEM briga com ninguem), a recusa fala do ferimento e manda esperar -- \"briga\" e so pra quem trocou golpe com alguem",
					  !Esmagamento.Esmaga(f) && pl.Mineracao == null && Disse(falas, "acabou de se ferir") && !Disse(falas, "briga"), Ultimos(falas));
			luta.EntrarEmCombate();                   // e um golpe trocado com ALGUEM
			falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Minerando));
			AfirmarRc("...e quem trocou golpe com alguem continua ouvindo da briga", pl.Mineracao == null && Disse(falas, "no meio de uma briga"), Ultimos(falas));
		}
		finally
		{
			SemPortaoDoEsmagamentoDeTeste = false;
			DefinirAtividade(pl, Protocol.Activity.Parado);
			(f.Planetgrav, f.gravmult, f.GravMastered, f.weight_ratio) = antes;
			SemFeridaFresca();
			luta.Corpo.Curar(100);   // o esmagamento feriu de verdade (o funil de dano de sempre)
			luta.SincronizarVida();
		}
	}

	// =====================================================================
	// 5) PAGAR TECNOLOGIA
	// =====================================================================
	private void RcDePagar()
	{
		GD.Print("[recursos] -- 5) fabricar: em zeni o preco, em recursos o dobro, e nunca uma completando a outra --");

		const string coisa = "Scouter";
		Construcao? c = _obras!.Get(coisa);
		AfirmarRc("(montagem) o Scouter esta no catalogo, com preco e tecnologia", c is { Custo: > 0, Tech: > 0 }, $"{c?.Custo} zeni, tech {c?.Tech}");
		if (c == null) return;
		double emRecursos = c.Custo * 2;

		ServerPlayer pl = Forjar("Engenheiro", CorredorLivre(6), 1000);
		pl.Ficha.techskill = c.Tech;

		(int NaMochila, double Zeni, double Recursos, List<string> Falas) Fabricar(string comando, double zeni, double recursos)
		{
			pl.Ficha.Zeni = zeni;
			pl.Ficha.Recursos = recursos;
			List<string> falas = Ouvir(() => ComandoDeTech(pl, comando, coisa));
			return (pl.Mochila.Quantos(coisa), pl.Ficha.Zeni, pl.Ficha.Recursos, falas);
		}

		var falta1 = Fabricar("construir_recursos", zeni: 777, recursos: emRecursos - 1);
		AfirmarRc($"com {emRecursos - 1:N0} recursos (falta UM pro dobro de {c.Custo:N0}) o pedido e recusado, nada e cobrado, e a frase diz o preco em recursos",
				  falta1.NaMochila == 0 && falta1.Zeni == 777 && falta1.Recursos == emRecursos - 1 && Disse(falta1.Falas, $"{emRecursos:N0} recursos"),
				  Ultimos(falta1.Falas));

		var pagou = Fabricar("construir_recursos", zeni: 777, recursos: emRecursos);
		AfirmarRc($"com {emRecursos:N0} recursos ele fabrica: o item vai pra mochila, os recursos zeram e o ZENI NAO E TOCADO",
				  pagou.NaMochila == 1 && pagou.Zeni == 777 && pagou.Recursos == 0 && Disse(pagou.Falas, "você fabrica"), Ultimos(pagou.Falas));

		var naoCompleta = Fabricar("construir_recursos", zeni: 50_000_000, recursos: emRecursos - 1);
		AfirmarRc("com recursos curtos e CINQUENTA MILHOES de zeni, pagar em recursos continua recusado: nunca completa com zeni",
				  naoCompleta.NaMochila == 1 && naoCompleta.Zeni == 50_000_000 && naoCompleta.Recursos == emRecursos - 1);

		var soZeni = Fabricar("construir_recursos", zeni: c.Custo, recursos: 0);
		var comZeni = Fabricar("construir", zeni: c.Custo, recursos: 0);
		AfirmarRc("so com o zeni do preco: \"construir_recursos\" recusa e \"construir\" (o de sempre) fabrica, tirando o preco do zeni",
				  soZeni.NaMochila == 1 && soZeni.Zeni == c.Custo && comZeni.NaMochila == 2 && comZeni.Zeni == 0, Ultimos(comZeni.Falas));

		pl.Ficha.techskill = c.Tech - 1;
		var semTech = Fabricar("construir_recursos", zeni: 0, recursos: emRecursos * 10);
		AfirmarRc("a TECNOLOGIA vale pras duas moedas: sem o nivel, nem um monte de recursos fabrica",
				  semTech.NaMochila == 2 && semTech.Recursos == emRecursos * 10 && Disse(semTech.Falas, "tecnologia"), Ultimos(semTech.Falas));
		pl.Ficha.techskill = c.Tech;

		Recurso.SemDobroDeTeste = true;
		try
		{
			var metade = Fabricar("construir_recursos", zeni: 0, recursos: c.Custo);
			AfirmarRc("(defeito injetado: sem o dobro) a METADE dos recursos fabrica -- a cobranca acusa", metade.NaMochila == 3 && metade.Recursos == 0);
		}
		finally { Recurso.SemDobroDeTeste = false; }

		var metadeDeVerdade = Fabricar("construir_recursos", zeni: 0, recursos: c.Custo);
		AfirmarRc("...e sem o defeito a metade NAO fabrica", metadeDeVerdade.NaMochila == 3 && metadeDeVerdade.Recursos == c.Custo);
	}

	// =====================================================================
	// 6) O CATALOGO
	// =====================================================================
	private void RcDoCatalogo()
	{
		GD.Print("[recursos] -- 6) o catalogo: nada a venda acima do teto de tecnologia (o \"Space Pod de tech 999\") --");

		CatalogoDeObras cat = _obras!;
		var sabio = new Fighter { techskill = CatalogoDeObras.TechMaximoDoDm, Zeni = 0 };
		List<Construcao> ofertas = [.. cat.Ofertas(sabio, "Human").Select(o => o.Obra)];

		AfirmarRc("o `Spacepod_Placed` e o `Boat_Placed` (os balcoes de mapa do DM, tech 999) nao estao no catalogo",
				  cat.Get("Spacepod_Placed") == null && cat.Get("Boat_Placed") == null);
		AfirmarRc($"nenhuma oferta da aba Tech pede tecnologia acima de {CatalogoDeObras.TechMaximoDoDm:0}, e o servidor nao tem do que avisar no boot",
				  ofertas.Count > 50 && ofertas.All(o => o.Tech <= CatalogoDeObras.TechMaximoDoDm) && AvisarDasAcimaDoTeto(cat) == 0,
				  $"{ofertas.Count} ofertas, a maior pede {ofertas.Max(o => o.Tech):0}");
		AfirmarRc("CONTRASTE: a Spacepod (40), a Personal Spacepod (35) e o Boat (20) de verdade continuam a venda",
				  ofertas.Any(o => o.Id == "Spacepod" && o.Tech == 40) && ofertas.Any(o => o.Id == "Personal_Spacepod" && o.Tech == 35)
				  && ofertas.Any(o => o.Id == "Boat" && o.Tech == 20));
		AfirmarRc("...e a nave pousada e o barco do MAPA continuam achando a entrada deles pelo typepath",
				  cat.PorTypepath("/obj/Spacepod")?.Id == "Personal_Spacepod" && cat.PorTypepath("/obj/Boat")?.Id == "Boat");

		// ---- um arquivo que TRAGA a marca de volta
		CatalogoDeObras sujo = CatalogoDeObras.Parse(
			"[ { \"id\": \"Coisa\", \"nome\": \"Coisa\", \"custo\": 100, \"tech\": 40, \"racas\": [] }, "
			+ "{ \"id\": \"Coisa_Placed\", \"nome\": \"Coisa Placed\", \"custo\": 100, \"tech\": 999, \"racas\": [] } ]");
		List<string> Oferecidas() => [.. sujo.Ofertas(sabio, "Human").Select(o => o.Obra.Id)];
		bool SoADeVerdade() => Oferecidas().SequenceEqual(["Coisa"]);

		int quantas = AvisarDasAcimaDoTeto(sujo);
		AfirmarRc("um `construcoes.json` que traga uma de tech 999: ela NAO vira oferta, pedir pra fabrica-la e recusado, e o boot grita por ela",
				  SoADeVerdade() && quantas == 1
				  && CatalogoDeObras.Permitida(sujo.Get("Coisa_Placed")!, new Fighter { techskill = 5000, Zeni = 1e9 }, "Human") == RecusaObra.NaoExiste,
				  string.Join(", ", Oferecidas()));
		CatalogoDeObras.AcimaDoTetoViraOfertaDeTeste = true;
		try { AfirmarRc("(defeito injetado: o que pede tecnologia acima do teto vira oferta) o card de tech 999 volta -- a cobranca acusa", !SoADeVerdade(), string.Join(", ", Oferecidas())); }
		finally { CatalogoDeObras.AcimaDoTetoViraOfertaDeTeste = false; }
	}

	// =====================================================================
	// 7) O QUE PASSOU A ACEITAR RECURSOS -- e o Enma, que nao
	// =====================================================================
	/// <summary>Os ids das obras de palco desta secao (fora da faixa das de jogo e das outras bancadas), pra a faxina acha-las.</summary>
	private const int IdDoPalcoDosRecursos = 986_700;

	/// <summary>Um numero com ponto de milhar ("1.000.000"), como os rotulos do menu o escrevem -- sem depender da cultura da maquina.</summary>
	private static string Pontuado(double n)
	{
		string s = ((long)n).ToString(System.Globalization.CultureInfo.InvariantCulture);
		for (int i = s.Length - 3; i > 0; i -= 3) s = s.Insert(i, ".");
		return s;
	}

	/// <summary>
	/// O dono (2026-10-10): *"as melhorias da maquina de gravidade, do regenerador e da nave; a conversao em
	/// androide; a estatua SUPREMA. mude pra aceitarem resource, o enma mantenha so zeni"*.
	///
	/// UMA REGUA SO PROS CINCO (<see cref="RcPagaNasDuas"/>), pelo funil de verbos de producao: em zeni o preco,
	/// em recursos o dobro, o MESMO efeito nas duas, e sem saldo naquela moeda recusa sem cobrar -- com a outra
	/// sobrando no bolso. Os palcos sao obras e naves postas aos pes de um corpo forjado e tiradas no fim; o que
	/// os verbos gravam cai na caixa temporaria da bancada.
	/// </summary>
	private void RcDoQuePassouAAceitarRecursos()
	{
		GD.Print("[recursos] -- 7) melhorias de maquina, androide e estatua suprema: zeni OU o dobro em recursos -- e o Enma so em zeni --");

		ZoneKey terra = ZonaDaBancadaDeProjetil;
		var navesDoPalco = new List<Nave>();
		int proximaNaveDeAntes = _proximaNaveId;
		bool haviaGuardiao = _tronos.TryGetValue("guardian", out string? guardiaoDeAntes);

		// A TERRA DE ANTES SAI DO CAMINHO (a estatua de alguem, se houver, e as sete dela) e volta no fim.
		List<SetDeEsferas> setsDeAntes = [.. _sets.Where(s => s.Zona.Hash == terra.Hash)];
		List<Esfera> esferasDeAntes = [.. _esferas.Where(e => setsDeAntes.Exists(s => s.Id == e.Set))];
		_sets.RemoveAll(setsDeAntes.Contains);
		_esferas.RemoveAll(esferasDeAntes.Contains);

		Obra NoChao(ServerPlayer aosPesDe, string tipo, int n)
		{
			var o = new Obra
			{
				Id = IdDoPalcoDosRecursos + n, Tipo = tipo, Aparafusada = true,
				X = aosPesDe.Pos.X, Y = aosPesDe.Pos.Y, DonoConta = aosPesDe.Conta, DonoNome = aosPesDe.Name,
			};
			o.PorZona(terra);
			_noChao.Add(o);
			return o;
		}
		void TirarDoChao() => _noChao.RemoveAll(o => o.Id >= IdDoPalcoDosRecursos && o.Id < IdDoPalcoDosRecursos + 100);
		Nave NoPatio(ServerPlayer aosPesDe, string tipo)
		{
			var n = new Nave
			{
				Id = _proximaNaveId++, Tipo = tipo, X = aosPesDe.Pos.X, Y = aosPesDe.Pos.Y,
				DonoConta = aosPesDe.Conta, DonoNome = aosPesDe.Name,
			};
			n.PorZona(terra);
			_naves.Add(n);
			navesDoPalco.Add(n);
			return n;
		}
		void TirarDoPatio()
		{
			_naves.RemoveAll(navesDoPalco.Contains);
			navesDoPalco.Clear();
		}
		void TirarAsEstatuasDaTerra()
		{
			foreach (SetDeEsferas s in _sets.Where(x => x.Zona.Hash == terra.Hash).ToList())
			{
				_esferas.RemoveAll(e => e.Set == s.Id);
				_sets.Remove(s);
				FecharAInvocacao(s.Id, "");
			}
		}

		try
		{
			// ------------------------------------------------------------ a palavra da moeda no pedido
			static (string, Moeda) Lido(string? arg) => Recurso.LerPedidoPago(arg);
			AfirmarRc("A MOEDA VAI NO FIM DO ARGUMENTO: sem palavra de moeda e zeni (o que os clientes de antes mandam), \"recursos\" e recursos, e o resto e o que se pede",
					  Lido("") == ("", Moeda.Zeni) && Lido("forca") == ("forca", Moeda.Zeni) && Lido("forca recursos") == ("forca", Moeda.Recursos)
					  && Lido("recursos") == ("", Moeda.Recursos) && Lido("zeni") == ("", Moeda.Zeni) && Lido("forca zeni") == ("forca", Moeda.Zeni)
					  && Lido("  supremo   RECURSOS ") == ("supremo", Moeda.Recursos) && Lido(null) == ("", Moeda.Zeni)
					  && Recurso.PedidoPago("forca", Moeda.Zeni) == "forca" && Recurso.PedidoPago("forca", Moeda.Recursos) == "forca recursos"
					  && Recurso.PedidoPago("", Moeda.Zeni) == "" && Recurso.PedidoPago("", Moeda.Recursos) == "recursos");

			// ------------------------------------------------------------ o que o menu oferece
			static bool AsDuas(Interacoes.Acao[] acoes, string verbo, string oQue) =>
				acoes.Any(a => a.Verbo == verbo && a.Arg == oQue && a.Forma == Interacoes.Forma.Direta)
				&& acoes.Any(a => a.Verbo == verbo && a.Arg == Recurso.PedidoPago(oQue, Moeda.Recursos) && a.Forma == Interacoes.Forma.Direta);
			string[] daGravidade = ["forca", "bateria", "alcance", "estabilidade", "nanites"];
			string[] doTanque = ["recarga", "bateria", "velocidade", "ferimentos", "nanites"];
			AfirmarRc("O MENU DAS MAQUINAS: cada uma das cinco melhorias da gravidade e do regenerador abre a pagina das duas formas de pagar -- e o servidor aceita o verbo dela (duas paginas abaixo da raiz)",
					  daGravidade.All(m => AsDuas(Interacoes.De($"Gravity/upgrades/{m}"), "grav_up", m))
					  && doTanque.All(m => AsDuas(Interacoes.De($"{Regenerador.Tipo}/upgrades/{m}"), "regen_up", m))
					  && Interacoes.De("Gravity/upgrades").Select(a => (a.Forma, a.Arg)).SequenceEqual(daGravidade.Select(m => (Interacoes.Forma.Submenu, $"upgrades/{m}")))
					  && Interacoes.De($"{Regenerador.Tipo}/upgrades").Select(a => (a.Forma, a.Arg)).SequenceEqual(doTanque.Select(m => (Interacoes.Forma.Submenu, $"upgrades/{m}")))
					  && Interacoes.Aceita("Gravity", "grav_up") && Interacoes.Aceita(Regenerador.Tipo, "regen_up")
					  && !Interacoes.Aceita("Gravity", "regen_up") && !Interacoes.Aceita("Bank", "grav_up"));
			AfirmarRc("A LISTA DE MELHORIAS PERGUNTA OS PRECOS AO ABRIR -- so a das duas maquinas, cujo preco e do estado de cada uma; as outras paginas nao perguntam nada",
					  Interacoes.PerguntaAoAbrir("Gravity", "upgrades") == Interacoes.PrecosDaGravidade
					  && Interacoes.PerguntaAoAbrir(Regenerador.Tipo, "upgrades") == Interacoes.PrecosDoRegenerador
					  && Interacoes.PerguntaAoAbrir("Gravity", "upgrades/forca").Length == 0 && Interacoes.PerguntaAoAbrir("Gravity", "").Length == 0
					  && Interacoes.PerguntaAoAbrir("Android_Creation_Mainframe", "androide").Length == 0
					  && Interacoes.PerguntaAoAbrir(Drill.Tipo, Drill.PaginaDeMelhorias).Length == 0);
			AfirmarRc("O MENU DAS NAVES: \"Melhorar velocidade\" e \"Recondicionar\" tem a gemea \"(com recursos)\" em toda pagina em que aparecem -- pousada, na ponte e montada",
					  new[] { Interacoes.De("Spacepod"), Interacoes.De("Personal_Spacepod"), Interacoes.De("Ship_Control"),
							  Interacoes.DoVeiculo(NaveGrande.Tipo), Interacoes.DoVeiculo("Spacepod") }.All(pg => AsDuas(pg, "nave_melhorar", ""))
					  && new[] { Interacoes.De("Rocket_Ship"), Interacoes.DoVeiculo("Rocket_Ship") }.All(pg => AsDuas(pg, "nave_recondicionar", "")));

			Interacoes.Acao[] doMainframe = Interacoes.De("Android_Creation_Mainframe/androide");
			bool Botao(string verbo, Moeda m, double zeni) => doMainframe.Any(a =>
				a.Verbo == verbo && a.Arg == Recurso.PedidoPago("", m)
				&& a.Rotulo.Contains(m == Moeda.Recursos ? $"({Pontuado(Recurso.CustoEmRecursos(zeni))} recursos)" : $"({Pontuado(zeni)} zeni)"));
			AfirmarRc("O MENU DO MAINFRAME: as duas conversoes, cada uma em zeni e em recursos, com o preco que o SERVIDOR cobra escrito no botao",
					  doMainframe.Length == 4
					  && Botao("androide_absorcao", Moeda.Zeni, CustoAndroideAbsorcao) && Botao("androide_absorcao", Moeda.Recursos, CustoAndroideAbsorcao)
					  && Botao("androide_infinito", Moeda.Zeni, CustoAndroideInfinito) && Botao("androide_infinito", Moeda.Recursos, CustoAndroideInfinito),
					  string.Join(" | ", doMainframe.Select(a => a.Rotulo)));

			// ---- a tabela de requisitos: a gemea em recursos herda o requisito da de zeni
			static Jandirus.Core.Verbos.VerbDeclarado? Linha(string chave) => Jandirus.Core.Verbos.RequisitosDosVerbos.Linha(chave);
			(string Zeni, string Recursos)[] gemeas =
			[
				("verbo:androide_absorcao", "verbo:androide_absorcao recursos"),
				("verbo:androide_infinito", "verbo:androide_infinito recursos"),
				("verbo:db_estatua supremo", "verbo:db_estatua supremo recursos"),
			];
			AfirmarRc("A TABELA DE REQUISITOS: o botao \"com recursos\" do androide e o da estatua suprema pedem EXATAMENTE o que o de zeni pede (a raca, o Cla do Dragao), no mesmo lugar e com a mesma recusa do servidor",
					  gemeas.All(par => Linha(par.Zeni) is { } emZeni && Linha(par.Recursos) is { } emRecursos
										&& !emRecursos.Requisito.Livre && emRecursos.Requisito.Texto == emZeni.Requisito.Texto
										&& emRecursos.Recusa == emZeni.Recusa && emRecursos.Onde == emZeni.Onde),
					  string.Join(" | ", gemeas.Select(par => $"{par.Recursos}: {Linha(par.Recursos)?.Requisito.Texto ?? "(sem linha)"}")));
			AfirmarRc("...e os precos escritos nos rotulos dela sao os que o servidor cobra (a estatua: o do Core; o androide: os do laboratorio)",
					  Linha("verbo:db_estatua supremo")?.Rotulo.Contains($"({Pontuado(Desejos.PrecoDoSupremo)} zeni)") == true
					  && Linha("verbo:db_estatua supremo recursos")?.Rotulo.Contains($"({Pontuado(Recurso.CustoEmRecursos(Desejos.PrecoDoSupremo))} recursos)") == true
					  && Linha("verbo:androide_absorcao")?.Rotulo.Contains($"({Pontuado(CustoAndroideAbsorcao)} zeni)") == true
					  && Linha("verbo:androide_absorcao recursos")?.Rotulo.Contains($"({Pontuado(Recurso.CustoEmRecursos(CustoAndroideAbsorcao))} recursos)") == true
					  && Linha("verbo:androide_infinito")?.Rotulo.Contains($"({Pontuado(CustoAndroideInfinito)} zeni)") == true
					  && Linha("verbo:androide_infinito recursos")?.Rotulo.Contains($"({Pontuado(Recurso.CustoEmRecursos(CustoAndroideInfinito))} recursos)") == true);

			// ------------------------------------------------------------ 7.1 a maquina de gravidade
			ServerPlayer dono = Forjar("rc: o dono das maquinas", CorredorLivre(6), 1000);
			dono.Ficha.techskill = 99;   // os nanites das duas maquinas pedem tecnologia

			Obra maquina = NoChao(dono, "Gravity", 1);
			GravidadeDaObra g = maquina.Gravidade = new GravidadeDaObra();
			void PedirGravidade(string qual, Moeda m) => Verbo(dono, "grav_up", Recurso.PedidoPago(qual, m));

			dono.Ficha.Zeni = 1234;
			dono.Ficha.Recursos = 5678;
			string oSaldo = $"você tem {1234:N0} zeni e {5678:N0} recursos";
			List<string> precos = Ouvir(() => Verbo(dono, Interacoes.PrecosDaGravidade, ""));
			List<string> estado = Ouvir(() => Verbo(dono, "grav_info", ""));
			AfirmarRc("GRAVIDADE: a pergunta do menu (`grav_precos`) responde o preco de CADA melhoria nas duas moedas e o saldo -- e o \"Ver estado\" termina com as mesmas linhas",
					  Disse(precos, $"força do campo: {Recurso.DoisPrecos(CustoDeForca(g))}") && Disse(precos, $"bateria: {Recurso.DoisPrecos(CustoDeBateria(g))}")
					  && Disse(precos, $"alcance: {Recurso.DoisPrecos(CustoDeAlcance(g))}") && Disse(precos, $"estabilização: {Recurso.DoisPrecos(CustoDeEstabilidade)}")
					  && Disse(precos, $"nanites: {Recurso.DoisPrecos(CustoDeNanites(g))}") && Disse(precos, oSaldo)
					  && precos.Count == 7 && estado.Count > precos.Count && estado.TakeLast(precos.Count).SequenceEqual(precos),
					  Ultimos(precos));

			RcPagaNasDuas("GRAVIDADE (alcance)", dono, () => CustoDeAlcance(g), m => PedirGravidade("alcance", m), () => g.Range);

			double PrecoDaGravidade(string qual) => qual switch
			{
				"forca" => CustoDeForca(g), "bateria" => CustoDeBateria(g), "alcance" => CustoDeAlcance(g),
				"estabilidade" => CustoDeEstabilidade, _ => CustoDeNanites(g),
			};
			AfirmarRc("...e as CINCO melhorias da gravidade saem dos recursos pelo dobro, com o zeni em zero",
					  RcTodasEmRecursos(dono, daGravidade, PrecoDaGravidade, PedirGravidade, out string contaDaGravidade)
					  && g is { Estavel: true, Nanites: 1 }, contaDaGravidade);
			TirarDoChao();

			// ------------------------------------------------------------ 7.2 o regenerador
			Obra tanque = NoChao(dono, Regenerador.Tipo, 2);
			RegeneradorDaObra r = tanque.Regenerador = new RegeneradorDaObra();
			void PedirTanque(string qual, Moeda m) => Verbo(dono, "regen_up", Recurso.PedidoPago(qual, m));

			dono.Ficha.Zeni = 1234;
			dono.Ficha.Recursos = 5678;
			precos = Ouvir(() => Verbo(dono, Interacoes.PrecosDoRegenerador, ""));
			estado = Ouvir(() => Verbo(dono, "regen_info", ""));
			AfirmarRc("REGENERADOR: a pergunta do menu (`regen_precos`) responde o preco de CADA melhoria nas duas moedas e o saldo -- e o \"Ver estado\" termina com as mesmas linhas",
					  Disse(precos, $"recarregar a bateria: {Recurso.DoisPrecos(Regenerador.CustoDeRecarga(r.EnergiaMax))}")
					  && Disse(precos, $"bateria maior: {Recurso.DoisPrecos(Regenerador.CustoDeBateria(r.EnergiaMax))}")
					  && Disse(precos, $"velocidade de cura: {Recurso.DoisPrecos(Regenerador.CustoDeVelocidade(r.Eficiencia))}")
					  && Disse(precos, $"tratar ferimentos: {Recurso.DoisPrecos(Regenerador.CustoDeFerimentos)}")
					  && Disse(precos, $"nanites: {Recurso.DoisPrecos(Regenerador.CustoDeNanites(r.Nanites))}") && Disse(precos, oSaldo)
					  && precos.Count == 7 && estado.Count > precos.Count && estado.TakeLast(precos.Count).SequenceEqual(precos),
					  Ultimos(precos));

			RcPagaNasDuas("REGENERADOR (velocidade de cura)", dono, () => Regenerador.CustoDeVelocidade(r.Eficiencia),
						  m => PedirTanque("velocidade", m), () => r.Eficiencia);

			double PrecoDoTanque(string qual) => qual switch
			{
				"recarga" => Regenerador.CustoDeRecarga(r.EnergiaMax), "bateria" => Regenerador.CustoDeBateria(r.EnergiaMax),
				"velocidade" => Regenerador.CustoDeVelocidade(r.Eficiencia), "ferimentos" => Regenerador.CustoDeFerimentos,
				_ => Regenerador.CustoDeNanites(r.Nanites),
			};
			r.Energia = r.EnergiaMax / 2;   // a recarga recusa a bateria cheia ANTES de cobrar
			AfirmarRc("...e as CINCO melhorias do regenerador saem dos recursos pelo dobro, com o zeni em zero",
					  RcTodasEmRecursos(dono, doTanque, PrecoDoTanque, PedirTanque, out string contaDoTanque)
					  && r is { CuraFerimentos: true, Nanites: 1 }, contaDoTanque);
			TirarDoChao();

			// ------------------------------------------------------------ 7.3 a nave: o degrau de velocidade e o foguete
			Nave pod = NoPatio(dono, "Spacepod");
			RcPagaNasDuas("NAVE (degrau de velocidade)", dono, () => Naves.CustoDoUpgrade(pod.Velocidade),
						  m => Verbo(dono, "nave_melhorar", Recurso.PedidoPago("", m)), () => pod.Velocidade);

			// A CONTRAPROVA DA RECUSA, na nave: cobrar de quem nao tem (o defeito da secao 3) deixa o degrau passar.
			CobraSemSaldoDeTeste = true;
			try
			{
				int velocidade = pod.Velocidade;
				dono.Ficha.Zeni = 0;
				dono.Ficha.Recursos = Recurso.CustoEmRecursos(Naves.CustoDoUpgrade(pod.Velocidade)) - 1;
				Verbo(dono, "nave_melhorar", Recurso.PalavraDeRecursos);
				AfirmarRc("(defeito injetado: cobra recurso de quem nao tem) faltando UM recurso o degrau PASSA e o saldo fica negativo -- a cobranca da recusa acusa",
						  pod.Velocidade == velocidade + 1 && dono.Ficha.Recursos < 0, $"velocidade {velocidade} -> {pod.Velocidade}, recursos {dono.Ficha.Recursos:0}");
			}
			finally { CobraSemSaldoDeTeste = false; }
			TirarDoPatio();

			Nave foguete = NoPatio(dono, "Rocket_Ship");
			RcPagaNasDuas("FOGUETE (recondicionar)", dono, () => Naves.CustoDeRecondicionar,
						  m => Verbo(dono, "nave_recondicionar", Recurso.PedidoPago("", m)), () => foguete.Usada ? 0 : 1,
						  rearmar: () => foguete.Usada = true);
			TirarDoPatio();

			// ------------------------------------------------------------ 7.4 a conversao em androide
			ServerPlayer humano = Forjar("rc: o humano do laboratorio", CorredorLivre(6), 1000);
			humano.Ficha.techskill = 99;
			double bpDeHumano = humano.Ficha.BP;
			NoChao(humano, "Android_Creation_Mainframe", 3).Lab = 1;   // o Android Lab ja instalado
			void VoltarAHumano()
			{
				humano.Race = "Human";
				humano.Ficha.AndroideAbsorcao = humano.Ficha.AndroideInfinito = false;
				humano.Ficha.BP = bpDeHumano;
				humano.Ficha.Statify();
			}
			string Corpo() => $"{humano.Race} | absorcao {humano.Ficha.AndroideAbsorcao} | infinito {humano.Ficha.AndroideInfinito} | BP {humano.Ficha.BP:0}";
			RcPagaNasDuas("ANDROIDE DE ABSORCAO", humano, () => CustoAndroideAbsorcao,
						  m => Verbo(humano, "androide_absorcao", Recurso.PedidoPago("", m)),
						  () => humano.Race == "Android" && humano.Ficha.AndroideAbsorcao && !humano.Ficha.AndroideInfinito ? 1 : 0, VoltarAHumano, Corpo);
			RcPagaNasDuas("ANDROIDE DE ENERGIA INFINITA", humano, () => CustoAndroideInfinito,
						  m => Verbo(humano, "androide_infinito", Recurso.PedidoPago("", m)),
						  () => humano.Race == "Android" && humano.Ficha.AndroideInfinito && !humano.Ficha.AndroideAbsorcao ? 1 : 0, VoltarAHumano, Corpo);
			TirarDoChao();

			// ------------------------------------------------------------ 7.5 a estatua com o desejo SUPREMO
			ServerPlayer anciao = Forjar("rc: o do Cla do Dragao", CorredorLivre(6), 5000);
			anciao.Race = "Namekian";
			anciao.Class = "Dragon clan";
			_tronos["guardian"] = anciao.Conta;   // na Terra quem ergue estatua e o Guardiao
			int NaTerra() => _sets.Count(s => s.Zona.Hash == terra.Hash);
			int ComOSupremo() => _sets.Count(s => s.Zona.Hash == terra.Hash && s.TemSupremo);
			void Derrubar() => Verbo(anciao, "db_derrubar", "sim");   // o verb de producao: a estatua esta aos pes de quem a ergueu
			RcPagaNasDuas("ESTATUA SUPREMA", anciao, () => Desejos.PrecoDoSupremo,
						  m => Verbo(anciao, "db_estatua", Recurso.PedidoPago("supremo", m)),
						  () => ComOSupremo(), Derrubar,
						  () => $"{NaTerra()} estatua(s) na Terra, {ComOSupremo()} com o SUPREMO, {_esferas.Count(e => _sets.Exists(s => s.Id == e.Set && s.Zona.Hash == terra.Hash))} esferas");

			AfirmarRc("(montagem) a regua deixou a Terra sem estatua -- o verb de derrubar funcionou a cada volta", NaTerra() == 0, $"{NaTerra()}");
			anciao.Ficha.Zeni = Desejos.PrecoDoSupremo - 1;
			anciao.Ficha.Recursos = Recurso.CustoEmRecursos(Desejos.PrecoDoSupremo) - 1;
			List<string> semNada = Ouvir(() => Verbo(anciao, "db_estatua", Recurso.PedidoPago("supremo", Moeda.Recursos)));
			AfirmarRc("...e sem saldo a estatua SOBE SEM o desejo (como sempre subiu), dizendo o preco nas duas moedas",
					  NaTerra() == 1 && ComOSupremo() == 0
					  && Disse(semNada, Recurso.DoisPrecos(Desejos.PrecoDoSupremo)) && Disse(semNada, "sobe sem ele"), Ultimos(semNada));
			Derrubar();

			// ------------------------------------------------------------ a contraprova das cinco: so zeni paga
			// As maquinas ficam aos pes do DONO e o mainframe aos do HUMANO: cada verb acha a sua.
			GravidadeDaObra g2 = NoChao(dono, "Gravity", 4).Gravidade = new GravidadeDaObra();
			RegeneradorDaObra r2 = NoChao(dono, Regenerador.Tipo, 5).Regenerador = new RegeneradorDaObra();
			Nave pod2 = NoPatio(dono, "Spacepod");
			NoChao(humano, "Android_Creation_Mainframe", 6).Lab = 1;
			VoltarAHumano();
			(string Nome, ServerPlayer Quem, Func<double> Preco, Action Pedir)[] cinco =
			[
				("a melhoria da gravidade", dono, () => CustoDeAlcance(g2), () => Verbo(dono, "grav_up", "alcance recursos")),
				("a melhoria do regenerador", dono, () => Regenerador.CustoDeVelocidade(r2.Eficiencia), () => Verbo(dono, "regen_up", "velocidade recursos")),
				("o degrau da nave", dono, () => Naves.CustoDoUpgrade(pod2.Velocidade), () => Verbo(dono, "nave_melhorar", "recursos")),
				("a conversao em androide", humano, () => CustoAndroideAbsorcao, () => Verbo(humano, "androide_absorcao", "recursos")),
				("a estatua suprema", anciao, () => Desejos.PrecoDoSupremo, () => Verbo(anciao, "db_estatua", "supremo recursos")),
			];
			SoZeniPagaDeTeste = true;
			try
			{
				const double Farto = 50_000_000;
				var tirouDoZeni = new List<string>();
				foreach ((string nome, ServerPlayer quem, Func<double> preco, Action pedir) in cinco)
				{
					double custo = preco();
					quem.Ficha.Zeni = Farto;
					quem.Ficha.Recursos = Farto;
					pedir();
					if (quem.Ficha.Zeni == Farto - custo && quem.Ficha.Recursos == Farto) tirouDoZeni.Add(nome);
				}
				AfirmarRc("(defeito injetado: so zeni paga -- o estado de antes) pedidas EM RECURSOS, as cinco tiram do ZENI e deixam os recursos intactos -- a cobranca acusa as cinco",
						  tirouDoZeni.Count == cinco.Length, $"{tirouDoZeni.Count} de {cinco.Length}: {string.Join(", ", tirouDoZeni)}");
			}
			finally { SoZeniPagaDeTeste = false; }
			VoltarAHumano();
			Derrubar();
			TirarDoChao();
			TirarDoPatio();

			// ------------------------------------------------------------ 7.6 o Enma: SO zeni
			RcDoEnmaSoEmZeni(NoChao, TirarDoChao);
		}
		finally
		{
			SoZeniPagaDeTeste = CobraSemSaldoDeTeste = false;
			TirarDoChao();
			TirarDoPatio();
			_proximaNaveId = proximaNaveDeAntes;

			TirarAsEstatuasDaTerra();
			_sets.AddRange(setsDeAntes);
			_esferas.AddRange(esferasDeAntes);
			MandarEsferas(terra);

			if (haviaGuardiao) _tronos["guardian"] = guardiaoDeAntes!;
			else _tronos.Remove("guardian");
		}
	}

	/// <summary>
	/// A REGUA DE UMA COISA PAGA, pelo funil de producao: QUATRO pedidos (em zeni com o preco exato, em recursos
	/// com o dobro exato, e faltando UM em cada moeda com a outra sobrando).
	/// </summary>
	/// <param name="preco">O preco EM ZENI de agora (nas melhorias ele muda a cada compra).</param>
	/// <param name="pedir">Manda o verbo, na moeda.</param>
	/// <param name="feito">Um numero que anda EXATAMENTE um quando a compra passa (o nivel, ou 0/1).</param>
	/// <param name="rearmar">Poe a coisa de novo em estado de ser comprada (o que se compra uma vez so).</param>
	/// <param name="retrato">O resultado por extenso: o da compra em recursos tem de ser IGUAL ao da compra em zeni.</param>
	private void RcPagaNasDuas(string oQue, ServerPlayer pl, Func<double> preco, Action<Moeda> pedir, Func<double> feito,
							   Action? rearmar = null, Func<string>? retrato = null)
	{
		const double Farto = 50_000_000;

		// ---- em ZENI: o preco, e os recursos intactos
		rearmar?.Invoke();
		double p = preco();
		pl.Ficha.Zeni = p;
		pl.Ficha.Recursos = Farto;
		double antes = feito();
		List<string> falas = Ouvir(() => pedir(Moeda.Zeni));
		double passoEmZeni = feito() - antes;
		string retratoEmZeni = retrato?.Invoke() ?? "";
		AfirmarRc($"{oQue}: em ZENI custa o preco de sempre ({p:N0}) e nao toca nos recursos",
				  p > 0 && passoEmZeni == 1 && pl.Ficha.Zeni == 0 && pl.Ficha.Recursos == Farto,
				  $"zeni {pl.Ficha.Zeni:N0}, recursos {pl.Ficha.Recursos:N0}, passo {passoEmZeni} | {Ultimos(falas)}");

		// ---- em RECURSOS: o dobro, o zeni intacto, e o MESMO efeito
		rearmar?.Invoke();
		p = preco();
		pl.Ficha.Zeni = Farto;
		pl.Ficha.Recursos = Recurso.CustoEmRecursos(p);
		antes = feito();
		falas = Ouvir(() => pedir(Moeda.Recursos));
		double passoEmRecursos = feito() - antes;
		string retratoEmRecursos = retrato?.Invoke() ?? "";
		AfirmarRc($"**{oQue}: em RECURSOS custa o DOBRO ({Recurso.CustoEmRecursos(p):N0})**, nao toca no zeni, e faz o MESMO que em zeni",
				  passoEmRecursos == 1 && pl.Ficha.Recursos == 0 && pl.Ficha.Zeni == Farto && retratoEmRecursos == retratoEmZeni
				  && Recurso.CustoEmRecursos(p) == 2 * p,
				  $"zeni {pl.Ficha.Zeni:N0}, recursos {pl.Ficha.Recursos:N0}, passo {passoEmRecursos} | em zeni: {retratoEmZeni} / em recursos: {retratoEmRecursos} | {Ultimos(falas)}");

		// ---- sem saldo NAQUELA moeda: recusa, nao cobra, e a outra (farta) NAO completa
		rearmar?.Invoke();
		p = preco();
		pl.Ficha.Zeni = p - 1;
		pl.Ficha.Recursos = Farto;
		antes = feito();
		falas = Ouvir(() => pedir(Moeda.Zeni));
		bool recusouEmZeni = feito() == antes && pl.Ficha.Zeni == p - 1 && pl.Ficha.Recursos == Farto
							 && Disse(falas, $"custa {Recurso.DoisPrecos(p)}") && Disse(falas, "o zeni não chega");
		string ditoEmZeni = Ultimos(falas);

		rearmar?.Invoke();
		p = preco();
		pl.Ficha.Zeni = Farto;
		pl.Ficha.Recursos = Recurso.CustoEmRecursos(p) - 1;
		antes = feito();
		falas = Ouvir(() => pedir(Moeda.Recursos));
		bool recusouEmRecursos = feito() == antes && pl.Ficha.Recursos == Recurso.CustoEmRecursos(p) - 1 && pl.Ficha.Zeni == Farto
								 && Disse(falas, $"custa {Recurso.DoisPrecos(p)}") && Disse(falas, "os recursos não chegam");
		AfirmarRc($"{oQue}: faltando UM na moeda pedida, RECUSA -- nada acontece, nada e cobrado, a outra moeda (farta) NAO completa, e a frase diz o preco nas duas",
				  recusouEmZeni && recusouEmRecursos, $"em zeni: {ditoEmZeni} || em recursos: {Ultimos(falas)}");
		rearmar?.Invoke();
	}

	/// <summary>
	/// CADA MELHORIA DA LISTA, PAGA EM RECURSOS com o zeni em zero: o que sai e o dobro do preco de cada uma, e
	/// nada mais. Devolve se as contas bateram em todas; <paramref name="conta"/> e o extrato.
	/// </summary>
	private static bool RcTodasEmRecursos(ServerPlayer pl, string[] quais, Func<string, double> preco, Action<string, Moeda> pedir, out string conta)
	{
		const double Farto = 50_000_000;
		var linhas = new List<string>();
		bool certo = true;
		foreach (string qual in quais)
		{
			double custo = preco(qual);
			pl.Ficha.Zeni = 0;
			pl.Ficha.Recursos = Farto;
			pedir(qual, Moeda.Recursos);
			double saiu = Farto - pl.Ficha.Recursos;
			certo &= custo > 0 && saiu == Recurso.CustoEmRecursos(custo) && pl.Ficha.Zeni == 0;
			linhas.Add($"{qual} {custo:N0} zeni -> {saiu:N0} recursos");
		}
		conta = string.Join(", ", linhas);
		return certo;
	}

	/// <summary>
	/// O ENMA CONTINUA SO EM ZENI -- decisao do dono (2026-10-10): *"o enma mantenha so zeni"*. Esta aqui pra
	/// ninguem "consertar" depois: o menu dele nao tem botao de recurso, e quem manda a palavra da moeda por
	/// fora (um cliente mexido) nao e atendido -- o milhao em ZENI e a unica coisa que ele aceita.
	/// </summary>
	private void RcDoEnmaSoEmZeni(Func<ServerPlayer, string, int, Obra> noChao, Action tirarDoChao)
	{
		Interacoes.Acao[] doEnma = Interacoes.De(Alem.TipoDoEnma);
		AfirmarRc("O ENMA SO EM ZENI (decisao do dono): o menu dele tem as duas acoes de sempre, e NENHUMA carrega a palavra de uma moeda",
				  doEnma.Select(a => a.Verbo).SequenceEqual(["enma_ouvir", "enma_reviver"])
				  && doEnma.All(a => a.Arg.Length == 0 && !a.Rotulo.Contains(Recurso.PalavraDeRecursos, StringComparison.OrdinalIgnoreCase))
				  && doEnma[1].Rotulo.Contains("zeni", StringComparison.OrdinalIgnoreCase),
				  string.Join(" | ", doEnma.Select(a => a.Rotulo)));

		ServerPlayer morto = Forjar("rc: o morto na mesa do Enma", CorredorLivre(6), 1000);
		noChao(morto, Alem.TipoDoEnma, 7);   // (palco) a mesa dele, aos pes do morto -- o verb so confere o alcance
		try
		{
			double milhao = Alem.PrecoDoReviveDoEnma;
			morto.Ficha.dead = true;
			morto.Ficha.Zeni = 0;
			morto.Ficha.Recursos = 100 * milhao;   // cem vezes o preco, em recursos

			List<string> falas = Ouvir(() => Verbo(morto, "enma_reviver", Recurso.PalavraDeRecursos));
			AfirmarRc("...e um morto com CEM vezes o preco em recursos e zero de zeni, pedindo \"em recursos\", NAO volta: o Enma pede o milhao em zeni e nao toca nos recursos",
					  morto.Ficha.dead && morto.Ficha.Recursos == 100 * milhao && morto.Ficha.Zeni == 0
					  && Disse(falas, "não tem como pagar") && Disse(falas, "zeni") && !Disse(falas, Recurso.PalavraDeRecursos), Ultimos(falas));

			// A CONTRAPROVA: a recusa era da MOEDA. O mesmo morto, o mesmo pedido, com o milhao em zeni.
			morto.Ficha.Zeni = milhao;
			falas = Ouvir(() => Verbo(morto, "enma_reviver", Recurso.PalavraDeRecursos));
			AfirmarRc("(contraprova) o MESMO morto, com o milhao em ZENI, volta -- e o que sai e o zeni; os recursos seguem intactos",
					  !morto.Ficha.dead && morto.Ficha.Zeni == 0 && morto.Ficha.Recursos == 100 * milhao, Ultimos(falas));
		}
		finally { tirarDoChao(); }
	}
}
