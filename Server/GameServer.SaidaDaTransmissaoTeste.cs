using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A FAMILIA S da <c>--teleporteteste</c>: a SAIDA PISCANDO E O CLARAO DA CHEGADA do Instant Transmission (dono, 2026-10-24: "Fazer
/// igual no IT" -- o par de atrasos do Kai Kai). Vencida a concentracao o corpo pisca e fica 0,2 s; so entao salta; e o clarao da
/// chegada sai 0,1 s depois do salto. Nesses 0,2 s: o golpe nao pega (o desvio), andar nao cancela nem prende, cair segura, e um
/// segundo pedido e recusado. O relogio anda pelas portas de bancada (`EnvelhecerATransmissaoDeTeste`), que so mexem no tempo -- quem
/// decide e o tique de producao (`TickDasSaidasDaTransmissao`). Quem VE o clarao (o cliente) e o robo `--diagitmaestria`.
/// </summary>
public sealed partial class GameServer
{
	private void DesligarDefeitosDaSaida()
	{
		TransmissaoSomeNaHoraDeTeste = false;
		TransmissaoClaraoNaHoraDeTeste = false;
		TransmissaoSemEsquivaNaSaidaDeTeste = false;
		TransmissaoCaidoViajaDeTeste = false;
	}

	private void ItASaida()
	{
		GD.Print("[teleporte] -- S) a saida pisca 0,2 s antes de sumir, e o clarao da chegada sai 0,1 s depois de o corpo entrar na zona");
		try
		{
			SaidaEntreZonas();
			SaidaComGolpe();
			SaidaAndandoCaindoEDeNovo();
		}
		finally
		{
			DesligarDefeitosDaSaida();
			EscutaDaTransmissao = null;
			ItLimpar();
		}
	}

	/// <summary>
	/// A cena ENTRE ZONAS: o Viajante aqui (nivel 5, o universo, pra alcancar outro planeta) e o Farol em Namek, no berco do planeta.
	/// Devolve tambem a zona de saida. A concentracao ja vence (o verbo de producao e o tique dela): o corpo fica PISCANDO.
	/// </summary>
	private (ServerPlayer Eu, ServerPlayer Farol, ZoneKey Origem, Vec2 DeOnde, bool Concentrou) ItSaidaEntreZonas(Vec2 chao, string conta)
	{
		ServerPlayer eu = ItMestre("Viajante", chao, TransmissaoEmNiveis.Universo, 0, conta);
		ServerPlayer farol = ItCorpo("Farol", chao + new Vec2(3 * T, 0), 8_000, 1, conta + "_farol");
		ZoneKey namek = ZoneKey.Premade("Namek");
		MoveToZone(farol.Id, namek, PontoDeNascimento(namek));
		farol.Ficha.Tick(agoraMs: NowMs());
		ZoneKey origem = eu.Zone;
		Vec2 deOnde = eu.Pos;
		Ouvir(() => TeletransporteG11(eu, "#" + farol.Id));
		bool concentrou = _transmissaoG11.TryGetValue(eu.Id, out TransmissaoG11? t);
		if (t != null) { t.QuandoMs = NowMs() - 1; Ouvir(() => TickDaTransmissaoG11(eu, t, NowMs())); }
		return (eu, farol, origem, deOnde, concentrou);
	}

	private int ClaroesDe(ServerPlayer pl) => EscutaDaTransmissao?.Count(p => p.Id == pl.Id) ?? 0;

	// ---------------------------------------------------------------- S1-S2: entre zonas
	private void SaidaEntreZonas()
	{
		GD.Print("[teleporte] -- S1) entre zonas: o corpo pisca e fica 0,2 s; o clarao da chegada sai 0,1 s depois do salto");
		Vec2 chao = CorredorLivre(8);
		ZoneKey namek = ZoneKey.Premade("Namek");

		EscutaDaTransmissao = [];
		var c = ItSaidaEntreZonas(chao, "it_s_vai");
		double kiCheio = c.Eu.Ficha.MaxKi;
		(int Id, float X, float Y) primeiro = EscutaDaTransmissao.FirstOrDefault(p => p.Id == c.Eu.Id);
		bool piscouAqui = ClaroesDe(c.Eu) == 1 && Vec2.Distance(new Vec2(primeiro.X, primeiro.Y), c.DeOnde) < 1;
		bool ficou = c.Eu.Zone.Hash == c.Origem.Hash && FaseDaTransmissaoDeTeste(c.Eu.Id) == "Saindo"
					 && Math.Abs(c.Eu.Ficha.Ki - kiCheio) < 1e-6 && c.Eu.Combate.Intocavel;
		EnvelhecerATransmissaoDeTeste(c.Eu.Id, 100);
		bool aindaAqui = c.Eu.Zone.Hash == c.Origem.Hash && FaseDaTransmissaoDeTeste(c.Eu.Id) == "Saindo";
		EnvelhecerATransmissaoDeTeste(c.Eu.Id, 100);
		bool saltou = c.Eu.Zone.Hash == namek.Hash && ItPerto(c.Eu, c.Farol) && c.Eu.Ficha.Ki < kiCheio
					  && FaseDaTransmissaoDeTeste(c.Eu.Id) == "Chegando";
		int noSalto = ClaroesDe(c.Eu);
		AfirmarIt("S1. ENTRE ZONAS, vencida a concentracao o corpo PISCA e fica 0,2 s na zona (sem cobrar e intocavel); com 0,1 s ainda esta la, com 0,2 s salta",
			c.Concentrou && piscouAqui && ficou && aindaAqui && saltou,
			$"piscou aqui {piscouAqui} ficou {ficou} 0,1 s {aindaAqui} saltou {saltou} (zona {c.Eu.Zone.Name}, ki {c.Eu.Ficha.Ki:0} de {kiCheio:0})");
		EnvelhecerATransmissaoDeTeste(c.Eu.Id, 50);
		int com50 = ClaroesDe(c.Eu);
		EnvelhecerATransmissaoDeTeste(c.Eu.Id, 50);
		(int Id, float X, float Y) ultimo = EscutaDaTransmissao.LastOrDefault(p => p.Id == c.Eu.Id);
		bool naChegada = ClaroesDe(c.Eu) == 2 && Vec2.Distance(new Vec2(ultimo.X, ultimo.Y), c.Eu.Pos) < 1
						 && FaseDaTransmissaoDeTeste(c.Eu.Id) == null;
		AfirmarIt("S2. ...e o clarao da chegada espera o corpo entrar na zona: nada no salto nem 0,05 s depois; com 0,1 s, o segundo clarao, onde ele chegou",
			noSalto == 1 && com50 == 1 && naChegada, $"no salto {noSalto} com 0,05 s {com50} depois {ClaroesDe(c.Eu)}");
		ItLimpar();

		bool someu;
		EscutaDaTransmissao = [];
		TransmissaoSomeNaHoraDeTeste = true;
		try
		{
			var r = ItSaidaEntreZonas(chao, "it_s_vai_ruim");
			someu = r.Eu.Zone.Hash == namek.Hash;
		}
		finally { TransmissaoSomeNaHoraDeTeste = false; }
		AfirmarIt("(defeito injetado: o corpo some no tique do clarao) no fim da concentracao ele ja esta em Namek -- a regua do S1 REPROVA", someu);
		ItLimpar();

		int juntoDoSalto;
		EscutaDaTransmissao = [];
		TransmissaoClaraoNaHoraDeTeste = true;
		try
		{
			var r = ItSaidaEntreZonas(chao, "it_s_vai_ruim2");
			EnvelhecerATransmissaoDeTeste(r.Eu.Id, 200);
			juntoDoSalto = ClaroesDe(r.Eu);
		}
		finally { TransmissaoClaraoNaHoraDeTeste = false; }
		AfirmarIt("(defeito injetado: o clarao da chegada no tique do salto) os dois claroes ja sairam no salto -- a regua do S2 REPROVA",
			juntoDoSalto == 2, $"{juntoDoSalto}");
		EscutaDaTransmissao = null;
		ItLimpar();
	}

	// ---------------------------------------------------------------- S3: o golpe nos 0,2 s
	/// <summary>A cena na mesma zona: quem sai, um alvo a 8 tiles, e a concentracao ja vencida (o corpo piscando).</summary>
	private (ServerPlayer Eu, ServerPlayer Alvo) ItSaindo(Vec2 chao, string conta)
	{
		ServerPlayer eu = ItMestre("Esquivo", chao, TransmissaoEmNiveis.Regiao, 0, conta);
		ServerPlayer alvo = ItCorpo("Alvo", chao + new Vec2(8 * T, 0), 8_000, 1, conta + "_alvo");
		Ouvir(() => TeletransporteG11(eu, "#" + alvo.Id));
		if (_transmissaoG11.TryGetValue(eu.Id, out TransmissaoG11? t)) { t.QuandoMs = NowMs() - 1; Ouvir(() => TickDaTransmissaoG11(eu, t, NowMs())); }
		return (eu, alvo);
	}

	private void SaidaComGolpe()
	{
		GD.Print("[teleporte] -- S3) um golpe nos 0,2 s nao pega: o corpo que pisca esta se desfazendo, como no desvio");
		Vec2 chao = CorredorLivre(12);
		var c = ItSaindo(chao, "it_s_golpe");
		// UM RIVAL FORTE, colado: o soco dele, se pegasse, doeria
		ServerPlayer rival = ItCorpo("Rival", c.Eu.Pos + new Vec2(T, 0), 500_000, 1, "it_s_golpe_rival");
		bool intocavel = c.Eu.Combate.Intocavel;
		double vida = c.Eu.Ficha.HP;
		rival.AlvoId = c.Eu.Id;
		rival.Combate.Recarga = 0;
		Atacar(rival, Protocol.Golpe.Pesado);
		bool naoPegou = Math.Abs(c.Eu.Ficha.HP - vida) < 1e-9 && !c.Eu.Ficha.KO;
		EnvelhecerATransmissaoDeTeste(c.Eu.Id, 200);
		bool foi = ItPerto(c.Eu, c.Alvo);
		AfirmarIt("S3. um golpe nos 0,2 s NAO PEGA (o corpo que pisca e intocavel, como no desvio de golpe) e a viagem sai do mesmo jeito",
			intocavel && naoPegou && foi, $"intocavel {intocavel} vida {vida:0.#} -> {c.Eu.Ficha.HP:0.#} foi {foi}");
		ItLimpar();

		bool tocavel;
		TransmissaoSemEsquivaNaSaidaDeTeste = true;
		try
		{
			var r = ItSaindo(chao, "it_s_golpe_ruim");
			tocavel = !r.Eu.Combate.Intocavel && FaseDaTransmissaoDeTeste(r.Eu.Id) == "Saindo";
		}
		finally { TransmissaoSemEsquivaNaSaidaDeTeste = false; }
		AfirmarIt("(defeito injetado: sem o desvio da saida) o corpo que pisca fica tocavel -- a regua do S3 REPROVA", tocavel);
		ItLimpar();
	}

	// ---------------------------------------------------------------- S4-S6: andar, cair e pedir de novo nos 0,2 s
	private void SaidaAndandoCaindoEDeNovo()
	{
		GD.Print("[teleporte] -- S4) nos 0,2 s: andar nao cancela nem prende, cair segura, e um segundo pedido e recusado");
		Vec2 chao = CorredorLivre(12);

		var c = ItSaindo(chao, "it_s_anda");
		bool podeAndar = PodeMexerOCorpo(c.Eu);
		c.Eu.Pos += new Vec2(10, 0);
		List<string> deNovo = Ouvir(() => TeletransporteG11(c.Eu, "#" + c.Alvo.Id));
		bool recusou = Disse(deNovo, "ja esta se concentrando") && !_transmissaoG11.ContainsKey(c.Eu.Id) && FaseDaTransmissaoDeTeste(c.Eu.Id) == "Saindo";
		EnvelhecerATransmissaoDeTeste(c.Eu.Id, 200);
		bool foi = ItPerto(c.Eu, c.Alvo);
		AfirmarIt("S4. nos 0,2 s o corpo NAO fica preso: anda, e andar nao cancela mais (a concentracao acabou) -- a viagem sai",
			podeAndar && foi, $"pode andar {podeAndar} foi {foi}");
		AfirmarIt("S6. ...e um segundo pedido nesses 0,2 s e recusado: a viagem e a de antes", recusou, Ultimos(deNovo));
		ItLimpar();

		c = ItSaindo(chao, "it_s_cai");
		double ki = c.Eu.Ficha.Ki;
		c.Eu.Ficha.KO = true;
		List<string> caiu = Ouvir(() => EnvelhecerATransmissaoDeTeste(c.Eu.Id, 200));
		bool segurou = !ItPerto(c.Eu, c.Alvo) && Math.Abs(c.Eu.Ficha.Ki - ki) < 1e-6 && FaseDaTransmissaoDeTeste(c.Eu.Id) == null
					   && Disse(caiu, "caiu antes de sumir");
		c.Eu.Ficha.KO = false;
		AfirmarIt("S5. cair nos 0,2 s SEGURA: caido nao viaja, e nada e cobrado", segurou, Ultimos(caiu));
		ItLimpar();

		bool viajouCaido;
		TransmissaoCaidoViajaDeTeste = true;
		try
		{
			var r = ItSaindo(chao, "it_s_cai_ruim");
			r.Eu.Ficha.KO = true;
			EnvelhecerATransmissaoDeTeste(r.Eu.Id, 200);
			viajouCaido = ItPerto(r.Eu, r.Alvo);
			r.Eu.Ficha.KO = false;
		}
		finally { TransmissaoCaidoViajaDeTeste = false; }
		AfirmarIt("(defeito injetado: caido viaja) o corpo caido chega do lado do alvo -- a regua do S5 REPROVA", viajouCaido);
		ItLimpar();
	}
}
