using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A FAMILIA M da <c>--teleporteteste</c>: a MAESTRIA DO INSTANT TRANSMISSION (dono, 2026-10-23). So sobe usando (+2%, o
/// Yardrat +3%, teto 100); o Ki e a concentracao nos dois extremos e no meio, de longe e no campo de visao (0,2 s a 100%);
/// no meio de uma luta; o relogio fino de 30 Hz; nada de vigor; o cartao da aba Sense no fio; e o save.
///
/// Os numeros das reguas sao LITERAIS, feitos a mao (e nao lidos das constantes): uma constante trocada tem de reprovar aqui.
/// Cada regra com o defeito injetado na propria linha (<c>GameServer.MaestriaDaTransmissao.cs</c>), ligado num try/finally.
/// O save nao tem botao (a ficha vai inteira pro disco, nao ha linha de regra pra ligar): o contra-exemplo dele e a mutacao.
/// </summary>
public sealed partial class GameServer
{
	private void DesligarDefeitosDaMaestria()
	{
		MaestriaDoItNaoSobeDeTeste = false;
		MaestriaDoItNaoBaixaOKiDeTeste = false;
		MaestriaDoItNaoEncurtaDeTeste = false;
		MaestriaDoItSemPressaDePertoDeTeste = false;
		TransmissaoSoNoTiqueDeFichaDeTeste = false;
		MaestriaForaDaAssinaturaDeTeste = false;
	}

	private void ItAMaestria()
	{
		GD.Print("[teleporte] -- M) a maestria: so sobe usando, menos Ki, menos espera, 0,2 s no campo de visao, na luta, sem vigor, no fio e no save");
		try
		{
			MaestriaSoSobeUsando();
			MaestriaBaixaOKi();
			MaestriaEncurtaAEspera();
			MaestriaNoMeioDaLuta();
			MaestriaNoTiqueCheio();
			MaestriaSemVigor();
			MaestriaNoCartaoDoSense();
			MaestriaNoSave();
		}
		finally
		{
			DesligarDefeitosDaMaestria();
			EscutaDeSentidos = null;
			ItLimpar();
		}
	}

	/// <summary>Um corpo de IT no nivel dado, com a maestria dada, num ponto; a conta (a assinatura) e unica por chamada.</summary>
	private ServerPlayer ItMestre(string nome, Vec2 onde, int nivel, double maestria, string conta)
	{
		ServerPlayer pl = ItCorpo(nome, onde, 5_000, nivel, conta);
		pl.Ficha.MaestriaDaTransmissao = maestria;
		return pl;
	}

	// ---------------------------------------------------------------- M1-M4: so sobe usando
	private void MaestriaSoSobeUsando()
	{
		GD.Print("[teleporte] -- M1) so sobe usando: +2% a viagem que chega, o Yardrat +3%, teto 100, o aviso a cada 10%");
		Vec2 chao = CorredorLivre(16);
		ServerPlayer eu = ItMestre("Aprendiz", chao, TransmissaoEmNiveis.Regiao, 0, "it_m_sobe");
		ServerPlayer alvo = ItCorpo("Farol", chao + new Vec2(8 * T, 0), 8_000, 1, "it_m_sobe_farol");
		bool nasceZero = eu.Ficha.MaestriaDaTransmissao == 0;

		List<string> lista = Ouvir(() => TeletransporteG11(eu, ""));
		bool aListaNaoEnsina = eu.Ficha.MaestriaDaTransmissao == 0 && Disse(lista, "maestria do Instant Transmission: 0%");

		Ouvir(() => TeletransporteG11(eu, "#" + alvo.Id));
		eu.Pos += new Vec2(10, 0);
		Ouvir(() => TickDoEfetorG11());
		bool aPerdidaNaoEnsina = !_transmissaoG11.ContainsKey(eu.Id) && !ItPerto(eu, alvo) && eu.Ficha.MaestriaDaTransmissao == 0;
		eu.Pos = chao;

		var ida = ItViajar(eu, "#" + alvo.Id);
		AfirmarIt("M1. a maestria nasce em 0 e SO SOBE USANDO: pedir a lista e perder a concentracao nao dao nada; a viagem que chega da +2%",
			nasceZero && aListaNaoEnsina && aPerdidaNaoEnsina && ida.Comecou && ItPerto(eu, alvo) && eu.Ficha.MaestriaDaTransmissao == 2,
			$"lista {aListaNaoEnsina} perdida {aPerdidaNaoEnsina} chegou {ItPerto(eu, alvo)} maestria {eu.Ficha.MaestriaDaTransmissao} | {Ultimos(lista)}");
		ItLimpar();

		eu = ItMestre("Yardratino", chao, TransmissaoEmNiveis.Regiao, 0, "it_m_sobe_yd");
		eu.Race = eu.Ficha.Race = "Yardrat";
		alvo = ItCorpo("Farol", chao + new Vec2(8 * T, 0), 8_000, 1, "it_m_sobe_yd_farol");
		ItViajar(eu, "#" + alvo.Id);
		AfirmarIt("M2. ...e o Yardrat sobe +3% por viagem", ItPerto(eu, alvo) && eu.Ficha.MaestriaDaTransmissao == 3,
			$"{eu.Ficha.MaestriaDaTransmissao}");
		ItLimpar();

		eu = ItMestre("Veterano", chao, TransmissaoEmNiveis.Regiao, 8, "it_m_sobe_vet");
		alvo = ItCorpo("Farol", chao + new Vec2(8 * T, 0), 8_000, 1, "it_m_sobe_vet_farol");
		List<string> aos10 = ItViajar(eu, "#" + alvo.Id).Falas;
		bool dez = eu.Ficha.MaestriaDaTransmissao == 10 && Disse(aos10, "maestria do Instant Transmission chegou a 10%");
		List<string> aos12 = ItViajar(eu, "#" + alvo.Id).Falas;
		bool calado = eu.Ficha.MaestriaDaTransmissao == 12 && !aos12.Exists(f => f.Contains("maestria", StringComparison.OrdinalIgnoreCase));
		eu.Ficha.MaestriaDaTransmissao = 99;
		List<string> aos100 = ItViajar(eu, "#" + alvo.Id).Falas;
		bool cheia = eu.Ficha.MaestriaDaTransmissao == 100 && Disse(aos100, "domina o Instant Transmission");
		List<string> depois = ItViajar(eu, "#" + alvo.Id).Falas;
		bool teto = eu.Ficha.MaestriaDaTransmissao == 100 && !depois.Exists(f => f.Contains("maestria", StringComparison.OrdinalIgnoreCase)
																						 || f.Contains("domina", StringComparison.OrdinalIgnoreCase));
		AfirmarIt("M3. o teto e 100: 99 + 2 para em 100, e a viagem seguinte nao passa dele", cheia && teto,
			$"cheia {cheia} teto {teto} maestria {eu.Ficha.MaestriaDaTransmissao}");
		AfirmarIt("M4. o chat avisa a cada 10% e cala no meio: 8 -> 10 avisa, 10 -> 12 nao, e a maestria cheia diz o que ela rende",
			dez && calado && cheia, $"dez {dez} calado {calado} | {Ultimos(aos10)} | {Ultimos(aos12)} | {Ultimos(aos100)}");
		ItLimpar();

		MaestriaDoItNaoSobeDeTeste = true;
		try
		{
			eu = ItMestre("Aprendiz", chao, TransmissaoEmNiveis.Regiao, 0, "it_m_sobe_ruim");
			alvo = ItCorpo("Farol", chao + new Vec2(8 * T, 0), 8_000, 1, "it_m_sobe_ruim_farol");
			ItViajar(eu, "#" + alvo.Id);
			AfirmarIt("(defeito injetado: a viagem nao ensina) chega e a maestria fica em 0 -- a regua do M1 REPROVA",
				ItPerto(eu, alvo) && eu.Ficha.MaestriaDaTransmissao == 0, $"{eu.Ficha.MaestriaDaTransmissao}");
		}
		finally { MaestriaDoItNaoSobeDeTeste = false; }
		ItLimpar();
	}

	// ---------------------------------------------------------------- M5: o Ki
	/// <summary>Quanto do tanque uma viagem levou, com teleskill 200 (o custo sem maestria e meio tanque, o `kireq` do DM).</summary>
	private double ItCustoDoTanque(Vec2 chao, double maestria, string conta)
	{
		ServerPlayer eu = ItMestre("Pagante", chao, 1, maestria, conta);
		eu.Ficha.teleskill = 200;
		eu.Ficha.Ki = eu.Ficha.MaxKi;
		ServerPlayer alvo = ItCorpo("Alvo", chao + new Vec2(4 * T, 0), 8_000, 1, conta + "_alvo");
		double antes = eu.Ficha.Ki;
		var ida = ItViajar(eu, "#" + alvo.Id);
		double fracao = ida.Comecou && ItPerto(eu, alvo) ? (antes - eu.Ficha.Ki) / eu.Ficha.MaxKi : double.NaN;
		ItLimpar();
		return fracao;
	}

	private void MaestriaBaixaOKi()
	{
		GD.Print("[teleporte] -- M5) o Ki: reto, do custo de sempre (0%) a um quarto dele (100%)");
		Vec2 chao = CorredorLivre(12);
		double c0 = ItCustoDoTanque(chao, 0, "it_m_ki_0"), c50 = ItCustoDoTanque(chao, 50, "it_m_ki_50"),
			   c100 = ItCustoDoTanque(chao, 100, "it_m_ki_100");
		AfirmarIt("M5. com teleskill 200 a viagem leva: 0% = a metade do tanque (o de sempre); 50% = 0,3125; 100% = 0,125 (um quarto da metade)",
			Math.Abs(c0 - 0.5) < 1e-6 && Math.Abs(c50 - 0.3125) < 1e-6 && Math.Abs(c100 - 0.125) < 1e-6,
			$"0%: {c0:0.#####}  50%: {c50:0.#####}  100%: {c100:0.#####}");
		double ruim;
		MaestriaDoItNaoBaixaOKiDeTeste = true;
		try { ruim = ItCustoDoTanque(chao, 100, "it_m_ki_ruim"); }
		finally { MaestriaDoItNaoBaixaOKiDeTeste = false; }
		AfirmarIt("(defeito injetado: a maestria nao baixa o Ki) com 100% a viagem leva a metade inteira -- a regua do M5 REPROVA",
			Math.Abs(ruim - 0.5) < 1e-6, $"{ruim:0.#####}");
	}

	// ---------------------------------------------------------------- M6-M7: a espera
	/// <summary>A espera que o verbo MARCA (ms), com o alvo a tantos tiles. Nivel 2 (regiao): 4 s sem maestria, 2,4 s no Yardrat.</summary>
	private long ItEsperaMarcada(Vec2 chao, double maestria, int tiles, string conta, bool yardrat = false, int nivel = TransmissaoEmNiveis.Regiao)
	{
		ServerPlayer eu = ItMestre("Concentrado", chao, nivel, maestria, conta);
		if (yardrat) eu.Race = eu.Ficha.Race = "Yardrat";
		ServerPlayer alvo = ItCorpo("Alvo", chao + new Vec2(tiles * T, 0), 8_000, 1, conta + "_alvo");
		Ouvir(() => TeletransporteG11(eu, "#" + alvo.Id));
		long ms = _transmissaoG11.TryGetValue(eu.Id, out TransmissaoG11? t) ? t.QuandoMs - NowMs() : -1;
		ItLimpar();
		return ms;
	}

	private static bool PertoDe(long medido, long esperado) => Math.Abs(medido - esperado) <= 20;

	private void MaestriaEncurtaAEspera()
	{
		GD.Print("[teleporte] -- M6) a espera: longe, um quinto a 100%; no campo de visao (15 tiles), 0,2 s a 100%");
		Vec2 chao = CorredorLivre(40);
		long l0 = ItEsperaMarcada(chao, 0, 30, "it_m_l0"), l50 = ItEsperaMarcada(chao, 50, 30, "it_m_l50"), l100 = ItEsperaMarcada(chao, 100, 30, "it_m_l100");
		AfirmarIt("M6. LONGE (30 tiles, nivel 2): 0% = 4 s (o de sempre); 50% = 2,4 s; 100% = 0,8 s (um quinto)",
			PertoDe(l0, 4000) && PertoDe(l50, 2400) && PertoDe(l100, 800), $"0%: {l0}  50%: {l50}  100%: {l100}");
		long p0 = ItEsperaMarcada(chao, 0, 5, "it_m_p0"), p50 = ItEsperaMarcada(chao, 50, 5, "it_m_p50"), p100 = ItEsperaMarcada(chao, 100, 5, "it_m_p100");
		AfirmarIt("M7. NO CAMPO DE VISAO (5 tiles): 0% = 4 s (igual a hoje); 50% = 2,1 s (a reta ate 0,2 s); 100% = 0,2 s",
			PertoDe(p0, 4000) && PertoDe(p50, 2100) && PertoDe(p100, 200), $"0%: {p0}  50%: {p50}  100%: {p100}");
		long yd0 = ItEsperaMarcada(chao, 0, 5, "it_m_yd0", yardrat: true), ydPerto = ItEsperaMarcada(chao, 100, 5, "it_m_ydp", yardrat: true),
			 ydLonge = ItEsperaMarcada(chao, 100, 30, "it_m_ydl", yardrat: true);
		long yd5 = ItEsperaMarcada(chao, 100, 5, "it_m_yd5", yardrat: true, nivel: TransmissaoEmNiveis.Universo);
		AfirmarIt("M7b. o Yardrat (nivel 2: 2,4 s sem maestria): 100% perto = 0,2 s, longe = 0,48 s; e no nivel 5 (0,6 s) o perto fica com o quinto, 0,12 s -- nunca mais lento que o longe",
			PertoDe(yd0, 2400) && PertoDe(ydPerto, 200) && PertoDe(ydLonge, 480) && PertoDe(yd5, 120),
			$"0%: {yd0}  perto: {ydPerto}  longe: {ydLonge}  nivel 5 perto: {yd5}");

		long semEncurtar, semPressa;
		MaestriaDoItNaoEncurtaDeTeste = true;
		try { semEncurtar = ItEsperaMarcada(chao, 100, 30, "it_m_ruim_l"); }
		finally { MaestriaDoItNaoEncurtaDeTeste = false; }
		AfirmarIt("(defeito injetado: a maestria nao encurta) 100% longe marca os 4 s de sempre -- a regua do M6 REPROVA",
			PertoDe(semEncurtar, 4000), $"{semEncurtar}");
		MaestriaDoItSemPressaDePertoDeTeste = true;
		try { semPressa = ItEsperaMarcada(chao, 100, 5, "it_m_ruim_p"); }
		finally { MaestriaDoItSemPressaDePertoDeTeste = false; }
		AfirmarIt("(defeito injetado: o campo de visao sem pressa propria) 100% perto marca 0,8 s, o quinto de longe -- a regua do M7 REPROVA",
			PertoDe(semPressa, 800), $"{semPressa}");
	}

	// ---------------------------------------------------------------- M8-M9: no meio da luta
	private void MaestriaNoMeioDaLuta()
	{
		GD.Print("[teleporte] -- M8) no meio da luta: 0,2 s com 100%, e mexer ou cair continua cancelando");
		Vec2 chao = CorredorLivre(14);
		ServerPlayer eu = ItMestre("Lutador", chao, TransmissaoEmNiveis.Regiao, 100, "it_m_luta");
		ServerPlayer rival = ItCorpo("Rival", chao + new Vec2(T, 0), 5_000, 1, "it_m_luta_rival");
		ServerPlayer outro = ItCorpo("Inimigo", chao + new Vec2(8 * T, 0), 5_000, 1, "it_m_luta_inimigo");
		// A LUTA DE VERDADE: os dois trocaram golpes (o resolvedor arma isto dos dois lados a cada golpe -- `MeleeResolver.Resolver`)
		MarcarAgressao(eu, rival);
		MarcarAgressao(rival, eu);
		eu.Combate.EntrarEmCombate();
		rival.Combate.EntrarEmCombate();
		bool emLuta = eu.Combate.LutandoDeVerdade > 0 && eu.Combate.EmCombate > 0;

		Ouvir(() => TeletransporteG11(eu, "#" + outro.Id));
		long ms = _transmissaoG11.TryGetValue(eu.Id, out TransmissaoG11? t) ? t.QuandoMs - NowMs() : -1;
		if (t != null) { t.QuandoMs = NowMs() - 1; Ouvir(() => { TickDaTransmissaoG11(eu, t, NowMs()); TerminarATransmissaoDeTeste(eu.Id); }); }
		AfirmarIt("M8. NO MEIO DA LUTA (a tag de combate acesa), com 100% e o inimigo a 8 tiles: a concentracao e de 0,2 s e o corpo aparece do lado dele",
			emLuta && PertoDe(ms, 200) && ItPerto(eu, outro), $"luta {emLuta} espera {ms} chegou {ItPerto(eu, outro)}");

		Ouvir(() => TeletransporteG11(eu, "#" + rival.Id));
		eu.Pos += new Vec2(10, 0);
		List<string> mexeu = Ouvir(() => TickDoEfetorG11());
		bool cancelouMexendo = !_transmissaoG11.ContainsKey(eu.Id) && Disse(mexeu, "mexeu") && !ItPerto(eu, rival);
		Ouvir(() => TeletransporteG11(eu, "#" + rival.Id));
		eu.Ficha.KO = true;
		Ouvir(() => TickDoEfetorG11());
		bool cancelouCaindo = !_transmissaoG11.ContainsKey(eu.Id) && !ItPerto(eu, rival);
		eu.Ficha.KO = false;
		AfirmarIt("M9. ...e com 100% mexer (o empurrao de um golpe inclusive) ou cair continua cancelando: ninguem viaja",
			cancelouMexendo && cancelouCaindo, $"mexendo {cancelouMexendo} caindo {cancelouCaindo} | {Ultimos(mexeu)}");
		ItLimpar();
	}

	// ---------------------------------------------------------------- M10: o relogio fino
	private void MaestriaNoTiqueCheio()
	{
		GD.Print("[teleporte] -- M10) a concentracao vence no tique cheio (30 Hz), e nao so no de 5 Hz");
		Vec2 chao = CorredorLivre(12);

		// (2026-10-24) o que a concentracao vencida faz no tique e COMECAR A SAIR (o corpo pisca, e o salto vem 0,2 s depois -- a familia S)
		(bool Saiu, bool Pendente) UmTiqueSoFora5Hz(string conta)
		{
			ServerPlayer eu = ItMestre("Relampago", chao, TransmissaoEmNiveis.Regiao, 100, conta);
			ServerPlayer alvo = ItCorpo("Alvo", chao + new Vec2(6 * T, 0), 8_000, 1, conta + "_alvo");
			Ouvir(() => TeletransporteG11(eu, "#" + alvo.Id));
			if (_transmissaoG11.TryGetValue(eu.Id, out TransmissaoG11? t)) t.QuandoMs = NowMs() - 1;
			// O TIQUE DE VERDADE, um so, FORA do bloco de ficha: com o contador em 0 o `++_tickCount % TicksPorFicha` da 1, e o
			// efetor de 5 Hz nao roda neste quadro -- so o relogio fino pode levar a viagem.
			int guardado = _tickCount;
			_tickCount = 0;
			try { Ouvir(() => Tick()); }
			finally { _tickCount = guardado; }
			(bool, bool) r = (FaseDaTransmissaoDeTeste(eu.Id) == "Saindo", _transmissaoG11.ContainsKey(eu.Id));
			ItLimpar();
			return r;
		}

		var bom = UmTiqueSoFora5Hz("it_m_tique");
		AfirmarIt("M10. a concentracao vencida sai no PRIMEIRO tique cheio, fora do de 5 Hz: o corpo ja comeca a piscar (os 0,2 s nao viram 0,4 s)",
			bom.Saiu && !bom.Pendente, $"saiu {bom.Saiu} pendente {bom.Pendente}");
		(bool Saiu, bool Pendente) ruim;
		TransmissaoSoNoTiqueDeFichaDeTeste = true;
		try { ruim = UmTiqueSoFora5Hz("it_m_tique_ruim"); }
		finally { TransmissaoSoNoTiqueDeFichaDeTeste = false; }
		AfirmarIt("(defeito injetado: so o tique de 5 Hz leva) o tique cheio passa e a viagem fica pendurada -- a regua do M10 REPROVA",
			!ruim.Saiu && ruim.Pendente, $"saiu {ruim.Saiu} pendente {ruim.Pendente}");
	}

	// ---------------------------------------------------------------- M11: vigor
	private void MaestriaSemVigor()
	{
		GD.Print("[teleporte] -- M11) o vigor: nenhuma viagem cobra, com ou sem maestria");
		Vec2 chao = CorredorLivre(12);
		ServerPlayer eu = ItMestre("Folego", chao, TransmissaoEmNiveis.Regiao, 0, "it_m_vigor");
		ServerPlayer alvo = ItCorpo("Alvo", chao + new Vec2(8 * T, 0), 8_000, 1, "it_m_vigor_alvo");
		// PELA METADE: um gasto qualquer apareceria (e a cura do tanque cheio nao o esconderia)
		eu.Ficha.stamina = eu.Ficha.maxstamina * 0.5;
		double antes = eu.Ficha.stamina;
		var ida0 = ItViajar(eu, "#" + alvo.Id);
		bool zero = ida0.Comecou && ItPerto(eu, alvo) && eu.Ficha.stamina == antes;
		eu.Ficha.MaestriaDaTransmissao = 100;
		eu.Pos = chao;
		var ida100 = ItViajar(eu, "#" + alvo.Id);
		bool cem = ida100.Comecou && ItPerto(eu, alvo) && eu.Ficha.stamina == antes;
		AfirmarIt("M11. nenhuma viagem gasta vigor, com 0% e com 100% de maestria: o folego fica onde estava (nao ha regra nova -- e o de sempre)",
			zero && cem, $"0% {zero} 100% {cem} folego {eu.Ficha.stamina:0.##} de {antes:0.##}");
		ItLimpar();
	}

	// ---------------------------------------------------------------- M12-M14: o cartao do Sense
	private void MaestriaNoCartaoDoSense()
	{
		GD.Print("[teleporte] -- M12) o cartao da aba Sense: nivel, maestria, Ki e espera no fio; o servidor de antes; e o reenvio");
		Vec2 chao = CorredorLivre(12);
		ServerPlayer eu = ItMestre("Leitor", chao, TransmissaoEmNiveis.Planeta, 40, "it_m_fio");
		eu.Ficha.teleskill = 100;   // custo sem maestria = o tanque inteiro; e o piso do planeta (nivel 3)
		TransmissaoState estado = EstadoDaTransmissao(eu, NivelItNoFio(eu));
		var w = new NetDataWriter();
		FioDaTransmissao.Escrever(w, estado);
		var r = new NetDataReader(w.CopyData());
		TransmissaoState lido = FioDaTransmissao.Ler(r);
		// nivel 3 = 3 s; 40%: longe 3000 * 0,68 = 2040, perto 3000 - 2800 * 0,4 = 1880; Ki = 100% * (1 - 0,75 * 0,4) = 70%
		AfirmarIt("M12. o fio do cartao leva nivel 3, maestria 40%, Ki 70% do tanque, 2,04 s de longe e 1,88 s no campo de visao -- e acaba no leitor",
			lido == new TransmissaoState(3, 40, 70, 2040, 1880) && r.EndOfData, lido.ToString());

		var velho = new NetDataWriter();
		velho.Put((byte)3);
		var rv = new NetDataReader(velho.CopyData());
		TransmissaoState doVelho = FioDaTransmissao.Ler(rv);
		AfirmarIt("M13. ...e o pacote de um servidor de ANTES (so o byte do nivel) le sem passar do fim: nivel 3 e SEM maestria (o cartao nao nasce)",
			doVelho.Nivel == 3 && !doVelho.TemMaestria && rv.AvailableBytes == 0, doVelho.ToString());

		eu.Livro!.Dar(SenseEmNiveis.Path);
		eu.Niveis.Por(SenseEmNiveis.Path, SenseEmNiveis.Regiao);
		AplicarPoderes(eu);
		EscutaDeSentidos = [];
		int Quantos() => EscutaDeSentidos!.Count(e => e.Quem == eu.Id);
		TickDosSentidos();
		int primeiro = Quantos();
		TickDosSentidos();
		int parado = Quantos();
		eu.Ficha.MaestriaDaTransmissao = 42;
		TickDosSentidos();
		int subiu = Quantos();
		AfirmarIt("M14. a maestria que sobe REMANDA o cartao (a assinatura do Sense a inclui): 1 pacote, nada parado, mais 1 depois de subir",
			primeiro == 1 && parado == 1 && subiu == 2, $"{primeiro} / {parado} / {subiu}");
		int assentou, naoMandou;
		MaestriaForaDaAssinaturaDeTeste = true;
		try
		{
			// A assinatura muda de FORMA com o defeito (perde o pedaco do cartao) e sai uma vez; dai em diante e a regra dele
			TickDosSentidos();
			assentou = Quantos();
			eu.Ficha.MaestriaDaTransmissao = 44;
			TickDosSentidos();
			naoMandou = Quantos();
		}
		finally { MaestriaForaDaAssinaturaDeTeste = false; EscutaDeSentidos = null; }
		AfirmarIt("(defeito injetado: o cartao fora da assinatura) a maestria sobe e nada sai -- a regua do M14 REPROVA",
			naoMandou == assentou, $"{assentou} -> {naoMandou}");
		ItLimpar();
	}

	// ---------------------------------------------------------------- M15: o save
	private void MaestriaNoSave()
	{
		GD.Print("[teleporte] -- M15) a maestria vai pro save e volta no login");
		string pasta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jandirus_itmaestria_" + Guid.NewGuid().ToString("N"));
		try
		{
			var loja = new AccountStore(pasta);
			Vec2 chao = CorredorLivre(6);
			ServerPlayer eu = ItMestre("Memorioso", chao, 1, 46, "it_m_save");
			// O QUE O `Persistir` GRAVA (a ficha sem os buffs) e o que o login le (`ParaJogador`), como a familia 9d da `--alemteste`
			CharacterSave salvo = AccountStore.DeJogador(eu, NowMs());
			salvo.Ficha = FichaSemOsBuffs(eu);
			var conta = new AccountSave { Conta = eu.Conta };
			conta.Slots[0] = salvo;
			loja.Gravar(conta);
			CharacterSave? volta = loja.Carregar(eu.Conta)?.Slots[0];
			var logado = new ServerPlayer { Id = -77_123, Peer = null, Conta = eu.Conta, Slot = 0 };
			if (volta != null) AccountStore.ParaJogador(volta, logado);
			AfirmarIt("M15. a maestria vai pro save e volta no login (46%) -- campo da ficha, que vai inteira pro disco",
				volta != null && logado.Ficha?.MaestriaDaTransmissao == 46, $"{logado.Ficha?.MaestriaDaTransmissao}");
		}
		finally
		{
			try { if (System.IO.Directory.Exists(pasta)) System.IO.Directory.Delete(pasta, recursive: true); }
			catch (Exception e) { GD.Print($"[teleporte]   (a pasta do save de teste ficou: {e.Message})"); }
			ItLimpar();
		}
	}
}
