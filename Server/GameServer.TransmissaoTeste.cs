using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// <c>--teleporteteste</c> -- o Instant Transmission pela aba Sense (dono, 2026-10-21).
///
/// Cada regra tem o defeito injetado na propria linha, ligado aqui dentro de um <c>try/finally</c>: a mesma
/// afirmacao que passou REPROVA com ele. Os corpos sao forjados (sem Peer), no corredor da bancada de projetil.
/// </summary>
public sealed partial class GameServer
{
	private int _itOk, _itFalhou;

	private void AfirmarIt(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _itOk++; GD.Print($"[teleporte]   OK    {oque}"); return; }
		_itFalhou++;
		GD.PrintErr($"[teleporte]   FALHA {oque}   {detalhe}");
	}

	private void DesligarDefeitosDaTransmissao()
	{
		TransmissaoSemTetoDeEsperaDeTeste = false;
		TransmissaoSemNivelDeTeste = false;
		TransmissaoLevaQuemEstaDePeDeTeste = false;
		TransmissaoAceitaNomeAmbiguoDeTeste = false;
		TransmissaoLevaAgarraAoEspacoDeTeste = false;
		TransmissaoSemPiscarDeTeste = false;
		TransmissaoRecusaMeditandoDeTeste = false;
		TransmissaoChegaAPeNoEspacoDeTeste = false;
		TransmissaoSemPassagemDeTeste = false;
		TransmissaoSemIdNoFioDeTeste = false;
		DesligarDefeitosDaMaestria();   // (2026-10-23) a familia M -- ver GameServer.MaestriaDaTransmissaoTeste.cs
		DesligarDefeitosDaSaida();      // (2026-10-24) a familia S -- ver GameServer.SaidaDaTransmissaoTeste.cs
	}

	public void RodarBancadaDaTransmissao()
	{
		_itOk = _itFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		DesligarDefeitosDaTransmissao();
		GD.Print("[teleporte] ================ INSTANT TRANSMISSION PELA ABA SENSE ================");
		try
		{
			ItAEscada();
			ItOAlcance();
			ItOHomonimo();
			ItAEspera();
			ItQuemMedita();
			ItOAgarrado();
			ItOEspaco();
			ItOMorto();
			ItOPiscar();
			ItMeditandoPode();
			ItOIdNoFio();
			ItOCusto();
			ItMexerCancela();
			// (2026-10-23) A MAESTRIA, por ultimo: os corredores das familias de cima continuam os mesmos -- ver GameServer.MaestriaDaTransmissaoTeste.cs
			ItAMaestria();
			// (2026-10-24) A SAIDA PISCANDO E O CLARAO DA CHEGADA, depois da maestria -- ver GameServer.SaidaDaTransmissaoTeste.cs
			ItASaida();
		}
		finally
		{
			DesligarDefeitosDaTransmissao();
			EscutaDaTransmissao = null;
			_transmissaoG11.Clear();
			EsquecerAsTransmissoesDeTeste();
			LimparTudoDaBancada();
			GD.Print($"[teleporte] ================ {_itOk} OK, {_itFalhou} FALHA(S) ================");
		}
	}

	private const string ItSkill = TransmissaoEmNiveis.Path;
	private const float T = ZoneCollision.TileSize;

	private ServerPlayer ItCorpo(string nome, Vec2 onde, double bp, int nivel, string conta)
	{
		ServerPlayer pl = ForjarComSkills(nome, onde, bp, skills: [ItSkill],
			degraus: [(ItSkill, nivel)], efeitosDeSkill: true);
		pl.Conta = conta;
		pl.Ficha.teleskill = 1;
		pl.Niveis.Por(ItSkill, nivel);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Ficha.Tick(agoraMs: NowMs());
		return pl;
	}

	/// <summary>
	/// O verbo, e a concentracao vencida na hora (o relogio de parede nao anda na bancada). (2026-10-24) E a viagem inteira: a saida
	/// piscando e o clarao da chegada tambem vencem aqui -- quem mede os dois tempos e a familia S.
	/// </summary>
	private (bool Comecou, List<string> Falas) ItViajar(ServerPlayer pl, string arg)
	{
		List<string> falas = Ouvir(() => TeletransporteG11(pl, arg));
		if (!_transmissaoG11.TryGetValue(pl.Id, out TransmissaoG11? t)) return (false, falas);
		t.QuandoMs = NowMs() - 1;
		falas.AddRange(Ouvir(() => { TickDaTransmissaoG11(pl, t, NowMs()); TerminarATransmissaoDeTeste(pl.Id); }));
		return (true, falas);
	}

	private static bool ItPerto(ServerPlayer a, ServerPlayer b) => Vec2.Distance(a.Pos, b.Pos) <= 2f * T;

	private void ItLimpar()
	{
		_transmissaoG11.Clear();
		EsquecerAsTransmissoesDeTeste();
		LimparTudoDaBancada();
	}

	// ---------------------------------------------------------------- a escada
	private void ItAEscada()
	{
		GD.Print("[teleporte] -- a escada: cinco niveis, o piso do teleskill, a espera");
		AfirmarIt("a skill passa a ter 5 niveis no catalogo (no DM era 1)",
			_skills?.Get(ItSkill)?.MaxNivel == TransmissaoEmNiveis.NivelMaximo, $"{_skills?.Get(ItSkill)?.MaxNivel}");
		AfirmarIt("...e tem regra de nivel, a mesma escada do Sense",
			RegrasDeNivel.Get(ItSkill)?.Degraus.Length == 5);
		AfirmarIt("teleskill 1 e skill 0 = campo de visao",
			TransmissaoEmNiveis.NivelDe(0, 1) == TransmissaoEmNiveis.CampoDeVisao);
		AfirmarIt("teleskill 70 (o Yardrat nasce assim) = planeta, e nao o universo",
			TransmissaoEmNiveis.NivelDe(0, 70) == TransmissaoEmNiveis.Planeta);
		AfirmarIt("teleskill 200 = sistema; o universo so com a skill no 5",
			TransmissaoEmNiveis.NivelDe(0, 200) == TransmissaoEmNiveis.Sistema
			&& TransmissaoEmNiveis.NivelDe(0, 500) == TransmissaoEmNiveis.Sistema
			&& TransmissaoEmNiveis.NivelDe(5, 1) == TransmissaoEmNiveis.Universo);
		AfirmarIt("T1. a concentracao do nivel 1 cabe em 5 s, e o nivel 5 e mais curta",
			TransmissaoEmNiveis.MsDaConcentracao(1, false, false) == 5000
			&& TransmissaoEmNiveis.MsDaConcentracao(5, false, false) == 1000
			&& TransmissaoEmNiveis.MsDaConcentracao(1, true, false) == 3000);
		AfirmarIt("(defeito injetado: sem o teto de 5 s) a conta devolve 60 s -- a regua de cima REPROVA",
			TransmissaoEmNiveis.MsDaConcentracao(1, false, true) == TransmissaoEmNiveis.EsperaSemTetoMs);
	}

	// ---------------------------------------------------------------- o alcance
	private void ItOAlcance()
	{
		GD.Print("[teleporte] -- o nivel limita a distancia, e o universo alcanca o Outro Mundo");
		Vec2 chao = CorredorLivre(50);
		ServerPlayer eu = ItCorpo("Vidente", chao, 5_000, 1, "it_vidente");
		ServerPlayer perto = ItCorpo("Perto", chao + new Vec2(5 * T, 0), 8_000, 1, "it_perto");
		ServerPlayer regiao = ItCorpo("Regiao", chao + new Vec2(40 * T, 0), 8_000, 1, "it_regiao");
		bool Tem(ServerPlayer alvo) => AlvosDaTransmissao(eu).Exists(a => a.Quem == alvo);

		AfirmarIt("T2. nivel 1 alcanca os 5 tiles e nao os 40", Tem(perto) && !Tem(regiao),
			$"perto {Tem(perto)} regiao {Tem(regiao)} nivel {NivelDaTransmissao(eu)}");
		TransmissaoSemNivelDeTeste = true;
		try { AfirmarIt("(defeito injetado: o nivel nao limita) os 40 tiles entram -- a mesma regua REPROVA", Tem(regiao)); }
		finally { TransmissaoSemNivelDeTeste = false; }
		AfirmarIt("...e com o defeito desligado os 40 tiles saem de novo", !Tem(regiao));

		eu.Niveis.Por(ItSkill, TransmissaoEmNiveis.Universo);
		ServerPlayer alma = ItCorpo("Alma", chao + new Vec2(T, T), 8_000, 1, "it_alma");
		MoveToZone(alma.Id, ZoneKey.Premade(Alem.ZonaDoOutroMundo), new Vec2(20 * T, 20 * T));
		alma.Ficha.dead = true;
		alma.Ficha.KO = false;
		alma.MorteJaViajou = true;
		AfirmarIt("nivel 5 alcanca o morto de pe no Outro Mundo", Tem(alma) && !EhCadaver(alma), $"zona {alma.Zone.Name}");
		eu.Niveis.Por(ItSkill, 1);
		AfirmarIt("...e o nivel 1 nao alcanca", !Tem(alma));
		ItLimpar();
	}

	// ---------------------------------------------------------------- homonimo
	private void ItOHomonimo()
	{
		GD.Print("[teleporte] -- o nome nao escolhe o corpo");
		Vec2 chao = CorredorLivre(24);
		ServerPlayer eu = ItCorpo("Eu", chao, 5_000, TransmissaoEmNiveis.Regiao, "it_eu_homo");
		ServerPlayer a = ItCorpo("GemeoIT", chao + new Vec2(4 * T, 0), 8_000, 1, "it_gemeo_a");
		ServerPlayer b = ItCorpo("GemeoIT", chao + new Vec2(10 * T, 0), 8_000, 1, "it_gemeo_b");
		Vec2 origem = eu.Pos;

		List<string> falas = Ouvir(() => TeletransporteG11(eu, "GemeoIT"));
		AfirmarIt("T4. o nome repetido nao escolhe ninguem (os dois GemeoIT ficam, e quem pediu tambem)",
			!_transmissaoG11.ContainsKey(eu.Id) && Vec2.Distance(eu.Pos, origem) < 1
			&& Disse(falas, "mais de uma pessoa"), Ultimos(falas));

		var ida = ItViajar(eu, "#" + b.Id);
		AfirmarIt("...o #id do botao leva ao GemeoIT de longe, e nao ao de perto",
			ida.Comecou && ItPerto(eu, b) && !ItPerto(eu, a),
			$"ate b {ItPerto(eu, b)} ate a {ItPerto(eu, a)}");

		ItLimpar();
		eu = ItCorpo("Eu", chao, 5_000, TransmissaoEmNiveis.Regiao, "it_eu_homo2");
		a = ItCorpo("GemeoIT", chao + new Vec2(4 * T, 0), 8_000, 1, "it_gemeo_c");
		b = ItCorpo("GemeoIT", chao + new Vec2(10 * T, 0), 8_000, 1, "it_gemeo_d");
		TransmissaoAceitaNomeAmbiguoDeTeste = true;
		try
		{
			var ruim = ItViajar(eu, "GemeoIT");
			AfirmarIt("(defeito injetado: o nome pega o primeiro) a viagem SAI -- a mesma regua REPROVA",
				ruim.Comecou && (ItPerto(eu, a) || ItPerto(eu, b)));
		}
		finally { TransmissaoAceitaNomeAmbiguoDeTeste = false; }
		ItLimpar();
	}

	// ---------------------------------------------------------------- a espera no corpo
	private void ItAEspera()
	{
		GD.Print("[teleporte] -- a espera no corpo: 5 s no nivel 1, Yardrat mais rapido");
		Vec2 chao = CorredorLivre(20);
		ServerPlayer eu = ItCorpo("Humano", chao, 5_000, 1, "it_espera");
		ServerPlayer alvo = ItCorpo("Alvo", chao + new Vec2(4 * T, 0), 8_000, 1, "it_espera_alvo");
		Ouvir(() => TeletransporteG11(eu, "#" + alvo.Id));
		long espera = _transmissaoG11.TryGetValue(eu.Id, out TransmissaoG11? t) ? t.QuandoMs - NowMs() : -1;
		AfirmarIt("T1b. no corpo, o nivel 1 marca 5 s", espera >= 4900 && espera <= 5100, $"{espera}");
		_transmissaoG11.Clear();

		eu.Race = eu.Ficha.Race = "Yardrat";
		eu.Ficha.Ki = eu.Ficha.MaxKi;
		Ouvir(() => TeletransporteG11(eu, "#" + alvo.Id));
		long doYardrat = _transmissaoG11.TryGetValue(eu.Id, out TransmissaoG11? ty) ? ty.QuandoMs - NowMs() : -1;
		AfirmarIt("...e o Yardrat, no mesmo nivel, marca 3 s", doYardrat >= 2900 && doYardrat <= 3100, $"{doYardrat}");
		_transmissaoG11.Clear();

		TransmissaoSemTetoDeEsperaDeTeste = true;
		try
		{
			eu.Race = eu.Ficha.Race = "Human";
			eu.Ficha.Ki = eu.Ficha.MaxKi;
			Ouvir(() => TeletransporteG11(eu, "#" + alvo.Id));
			long semTeto = _transmissaoG11.TryGetValue(eu.Id, out TransmissaoG11? ts) ? ts.QuandoMs - NowMs() : -1;
			AfirmarIt("(defeito injetado: sem o teto de 5 s) o corpo marca 60 s -- a regua do nivel 1 REPROVA",
				semTeto > 5000, $"{semTeto}");
		}
		finally { TransmissaoSemTetoDeEsperaDeTeste = false; }
		ItLimpar();
	}

	// ---------------------------------------------------------------- meditando
	private void ItQuemMedita()
	{
		GD.Print("[teleporte] -- meditando a 1 tile vai; de pe, nao");
		Vec2 chao = CorredorLivre(24);
		ServerPlayer eu = ItCorpo("Eu", chao, 5_000, TransmissaoEmNiveis.Regiao, "it_med_eu");
		ServerPlayer junto = ItCorpo("Junto", chao + new Vec2(T, 0), 4_000, 1, "it_med_junto");
		ServerPlayer dePe = ItCorpo("DePe", chao + new Vec2(0, T), 4_000, 1, "it_med_depe");
		ServerPlayer longe = ItCorpo("LongeMed", chao + new Vec2(-3 * T, 0), 4_000, 1, "it_med_longe");
		ServerPlayer alvo = ItCorpo("Alvo", chao + new Vec2(8 * T, 0), 8_000, 1, "it_med_alvo");
		eu.Ficha.med = junto.Ficha.med = longe.Ficha.med = true;

		var ida = ItViajar(eu, "#" + alvo.Id);
		AfirmarIt("os dois meditando a 1 tile chegam junto", ida.Comecou && ItPerto(eu, alvo) && ItPerto(junto, alvo));
		AfirmarIt("T3. de pe a 1 tile NAO vai, nem quem medita a 3 tiles",
			!ItPerto(dePe, alvo) && !ItPerto(longe, alvo),
			$"de pe {Vec2.Distance(dePe.Pos, alvo.Pos):0} longe {Vec2.Distance(longe.Pos, alvo.Pos):0}");
		ItLimpar();

		chao = CorredorLivre(24);
		eu = ItCorpo("Eu", chao, 5_000, TransmissaoEmNiveis.Regiao, "it_med_eu2");
		dePe = ItCorpo("DePe", chao + new Vec2(T, 0), 4_000, 1, "it_med_depe2");
		alvo = ItCorpo("Alvo", chao + new Vec2(8 * T, 0), 8_000, 1, "it_med_alvo2");
		TransmissaoLevaQuemEstaDePeDeTeste = true;
		try
		{
			ItViajar(eu, "#" + alvo.Id);
			AfirmarIt("(defeito injetado: de pe vai junto) o de pe CHEGA -- a mesma regua REPROVA", ItPerto(dePe, alvo));
		}
		finally { TransmissaoLevaQuemEstaDePeDeTeste = false; }
		ItLimpar();
	}

	// ---------------------------------------------------------------- agarrao
	private void ItOAgarrado()
	{
		GD.Print("[teleporte] -- segurando e carregando vao junto, de pe");
		Vec2 chao = CorredorLivre(20);
		ServerPlayer eu = ItCorpo("Eu", chao, 5_000, TransmissaoEmNiveis.Regiao, "it_ag_eu");
		ServerPlayer preso = ItCorpo("Preso", chao + new Vec2(T, 0), 4_000, 1, "it_ag_preso");
		ServerPlayer alvo = ItCorpo("Alvo", chao + new Vec2(8 * T, 0), 8_000, 1, "it_ag_alvo");
		Prender(eu, preso);
		ItViajar(eu, "#" + alvo.Id);
		AfirmarIt("quem esta SEGURANDO leva o preso, os dois de pe",
			ItPerto(eu, alvo) && ItPerto(preso, alvo) && eu.AgarrandoId == preso.Id);
		ItLimpar();

		chao = CorredorLivre(20);
		eu = ItCorpo("Eu", chao, 5_000, TransmissaoEmNiveis.Regiao, "it_ag_eu2");
		preso = ItCorpo("Preso", chao + new Vec2(T, 0), 4_000, 1, "it_ag_preso2");
		alvo = ItCorpo("Alvo", chao + new Vec2(8 * T, 0), 8_000, 1, "it_ag_alvo2");
		Prender(eu, preso);
		eu.ModoDoAgarrao = ModoDeAgarrao.Carregando;
		ItViajar(eu, "#" + alvo.Id);
		AfirmarIt("...e CARREGANDO (a tecla duas vezes) tambem",
			ItPerto(eu, alvo) && ItPerto(preso, alvo) && eu.ModoDoAgarrao == ModoDeAgarrao.Carregando);
		ItLimpar();
	}

	// ---------------------------------------------------------------- espaco
	private void ItOEspaco()
	{
		GD.Print("[teleporte] -- no espaco chega voando; o agarrado fica");
		Vec2 chao = CorredorLivre(16);
		ServerPlayer eu = ItCorpo("Eu", chao, 5_000, TransmissaoEmNiveis.Universo, "it_esp_eu");
		ServerPlayer preso = ItCorpo("Preso", chao + new Vec2(T, 0), 4_000, 1, "it_esp_preso");
		ServerPlayer farol = ItCorpo("Farol", chao + new Vec2(6 * T, 0), 8_000, 1, "it_esp_farol");
		Vec2 doPreso = preso.Pos;
		MoveToZone(farol.Id, ZonaDoEspaco, new Vec2(400, 400));
		farol.Ficha.Tick(agoraMs: NowMs());
		Prender(eu, preso);

		List<string> falas = [];
		var ida = ItViajar(eu, "#" + farol.Id);
		falas = ida.Falas;
		AfirmarIt("T9. quem chega no espaco chega voando, na altura de pairar",
			ida.Comecou && Espaco.EhEspaco(eu.Zone) && eu.Voando
			&& Math.Abs(eu.Altitude - Voo.AlturaDePairar) < 0.01f,
			$"zona {eu.Zone.Name} voo {eu.Voando} alt {eu.Altitude:0}");
		AfirmarIt("T5. o agarrado NAO vai pro espaco: fica no chao, e o aperto acaba",
			!Espaco.EhEspaco(preso.Zone) && Vec2.Distance(preso.Pos, doPreso) < 1 && eu.AgarrandoId == 0
			&& Disse(falas, "espaço"),
			$"zona {preso.Zone.Name} aperto {eu.AgarrandoId}");
		ItLimpar();

		chao = CorredorLivre(16);
		eu = ItCorpo("Eu", chao, 5_000, TransmissaoEmNiveis.Universo, "it_esp_eu2");
		preso = ItCorpo("Preso", chao + new Vec2(T, 0), 4_000, 1, "it_esp_preso2");
		farol = ItCorpo("Farol", chao + new Vec2(6 * T, 0), 8_000, 1, "it_esp_farol2");
		MoveToZone(farol.Id, ZonaDoEspaco, new Vec2(400, 400));
		farol.Ficha.Tick(agoraMs: NowMs());
		Prender(eu, preso);
		TransmissaoLevaAgarraAoEspacoDeTeste = true;
		try
		{
			ItViajar(eu, "#" + farol.Id);
			AfirmarIt("(defeito injetado: o agarrado vai pro espaco) ele CHEGA -- a mesma regua REPROVA",
				Espaco.EhEspaco(preso.Zone) && eu.AgarrandoId == preso.Id, preso.Zone.Name);
		}
		finally { TransmissaoLevaAgarraAoEspacoDeTeste = false; }

		ItLimpar();
		chao = CorredorLivre(16);
		eu = ItCorpo("Eu", chao, 5_000, TransmissaoEmNiveis.Universo, "it_esp_eu3");
		farol = ItCorpo("Farol", chao + new Vec2(6 * T, 0), 8_000, 1, "it_esp_farol3");
		MoveToZone(farol.Id, ZonaDoEspaco, new Vec2(480, 400));
		farol.Ficha.Tick(agoraMs: NowMs());
		TransmissaoChegaAPeNoEspacoDeTeste = true;
		try
		{
			ItViajar(eu, "#" + farol.Id);
			AfirmarIt("(defeito injetado: chega a pe no espaco) o voo nao liga -- a regua do pairar REPROVA",
				Espaco.EhEspaco(eu.Zone) && !eu.Voando, $"voo {eu.Voando}");
		}
		finally { TransmissaoChegaAPeNoEspacoDeTeste = false; }
		ItLimpar();
	}

	// ---------------------------------------------------------------- morto
	private void ItOMorto()
	{
		GD.Print("[teleporte] -- morto pro mundo dos vivos: a passagem que ja existia");
		Vec2 chao = CorredorLivre(16);
		ServerPlayer vivo = ItCorpo("Vivo", chao, 8_000, 1, "it_vivo");
		ServerPlayer morto = ItCorpo("Morto", chao + new Vec2(4 * T, 0), 5_000, TransmissaoEmNiveis.Universo, "it_morto");
		MoveToZone(morto.Id, ZoneKey.Premade(Alem.ZonaDoOutroMundo), new Vec2(30 * T, 30 * T));
		morto.Ficha.dead = true;
		morto.Ficha.KO = false;
		morto.MorteJaViajou = true;
		morto.Ficha.Ki = morto.Ficha.MaxKi;
		var ida = ItViajar(morto, vivo.Name);
		AfirmarIt("T10. o morto chega ao vivo e a passagem arma (de pe, por um prazo)",
			ida.Comecou && morto.Zone.Hash == vivo.Zone.Hash && ItPerto(morto, vivo)
			&& morto.Ficha.dead && morto.EmPassagem && morto.PassagemAte > NowMs(),
			$"passagem {morto.EmPassagem} prazo {morto.PassagemAte - NowMs()}");
		ItLimpar();

		vivo = ItCorpo("Vivo", chao, 8_000, 1, "it_vivo2");
		morto = ItCorpo("Morto", chao + new Vec2(4 * T, 0), 5_000, TransmissaoEmNiveis.Universo, "it_morto2");
		MoveToZone(morto.Id, ZoneKey.Premade(Alem.ZonaDoOutroMundo), new Vec2(30 * T, 30 * T));
		morto.Ficha.dead = true;
		morto.Ficha.KO = false;
		morto.MorteJaViajou = true;
		morto.Ficha.Ki = morto.Ficha.MaxKi;
		TransmissaoSemPassagemDeTeste = true;
		try
		{
			ItViajar(morto, vivo.Name);
			AfirmarIt("(defeito injetado: sem a passagem) chega sem o prazo -- a mesma regua REPROVA",
				morto.Zone.Hash == vivo.Zone.Hash && !morto.EmPassagem);
		}
		finally { TransmissaoSemPassagemDeTeste = false; }
		ItLimpar();
	}

	// ---------------------------------------------------------------- piscar
	private void ItOPiscar()
	{
		GD.Print("[teleporte] -- o piscar nas duas pontas");
		Vec2 chao = CorredorLivre(16);
		ServerPlayer eu = ItCorpo("Eu", chao, 5_000, 1, "it_pis_eu");
		ServerPlayer alvo = ItCorpo("Alvo", chao + new Vec2(4 * T, 0), 8_000, 1, "it_pis_alvo");
		EscutaDaTransmissao = [];
		ItViajar(eu, "#" + alvo.Id);
		int n = EscutaDaTransmissao.Count(p => p.Id == eu.Id);
		AfirmarIt("T6. saem os dois piscares (a saida e a chegada)", n >= 2, $"{n}");
		ItLimpar();

		eu = ItCorpo("Eu", chao, 5_000, 1, "it_pis_eu2");
		alvo = ItCorpo("Alvo", chao + new Vec2(4 * T, 0), 8_000, 1, "it_pis_alvo2");
		EscutaDaTransmissao = [];
		TransmissaoSemPiscarDeTeste = true;
		try
		{
			ItViajar(eu, "#" + alvo.Id);
			AfirmarIt("(defeito injetado: sem o piscar) nao sai pacote -- a mesma regua REPROVA",
				EscutaDaTransmissao.Count == 0 && ItPerto(eu, alvo));
		}
		finally { TransmissaoSemPiscarDeTeste = false; EscutaDaTransmissao = null; }
		ItLimpar();
	}

	// ---------------------------------------------------------------- meditar nao recusa
	private void ItMeditandoPode()
	{
		GD.Print("[teleporte] -- meditar nao recusa o verbo");
		Vec2 chao = CorredorLivre(16);
		ServerPlayer eu = ItCorpo("Eu", chao, 5_000, 1, "it_pode_eu");
		ServerPlayer alvo = ItCorpo("Alvo", chao + new Vec2(4 * T, 0), 8_000, 1, "it_pode_alvo");
		eu.Ficha.med = true;
		Ouvir(() => TeletransporteG11(eu, "#" + alvo.Id));
		AfirmarIt("T7. meditando, a concentracao comeca", _transmissaoG11.ContainsKey(eu.Id));
		_transmissaoG11.Clear();

		TransmissaoRecusaMeditandoDeTeste = true;
		try
		{
			eu.Ficha.Ki = eu.Ficha.MaxKi;
			List<string> falas = Ouvir(() => TeletransporteG11(eu, "#" + alvo.Id));
			AfirmarIt("(defeito injetado: meditar recusa) o verbo nao sai -- a mesma regua REPROVA",
				!_transmissaoG11.ContainsKey(eu.Id) && Disse(falas, "nao consegue"), Ultimos(falas));
		}
		finally { TransmissaoRecusaMeditandoDeTeste = false; }
		ItLimpar();
	}

	// ---------------------------------------------------------------- id no fio
	private void ItOIdNoFio()
	{
		GD.Print("[teleporte] -- o cartao leva o id do corpo");
		Vec2 chao = CorredorLivre(16);
		ServerPlayer eu = ItCorpo("Leitor", chao, 5_000, 1, "it_fio_eu");
		eu.Livro!.Dar(SenseEmNiveis.Path);
		eu.Niveis.Por(SenseEmNiveis.Path, SenseEmNiveis.Regiao);
		AplicarPoderes(eu);
		ServerPlayer a = ItCorpo("GemeoIT", chao + new Vec2(3 * T, 0), 8_000, 1, "it_fio_a");
		ServerPlayer b = ItCorpo("GemeoIT", chao + new Vec2(6 * T, 0), 8_000, 1, "it_fio_b");
		(bool scan, List<Protocol.PresencaState> lista) = SentidosDe(eu);
		NetDataWriter w = PacoteDeSentidos(scan, (byte)NivelDoSense(eu), lista);
		var r = new NetDataReader(w.CopyData());
		r.GetByte();
		List<Protocol.PresencaState> lido = r.GetSentidos().Lista;
		AfirmarIt("T8. o fio leva o id de cada GemeoIT, e o pacote acaba no leitor",
			lido.Exists(p => p.Corpo == a.Id) && lido.Exists(p => p.Corpo == b.Id) && r.EndOfData,
			string.Join(",", lido.Select(p => p.Corpo)));

		TransmissaoSemIdNoFioDeTeste = true;
		try
		{
			w = PacoteDeSentidos(scan, (byte)NivelDoSense(eu), SentidosDe(eu).Lista);
			r = new NetDataReader(w.CopyData());
			r.GetByte();
			lido = r.GetSentidos().Lista;
			AfirmarIt("(defeito injetado: cartao sem id) os dois ids zeram -- a mesma regua REPROVA",
				lido.Count > 0 && lido.TrueForAll(p => p.Corpo == 0));
		}
		finally { TransmissaoSemIdNoFioDeTeste = false; }
		ItLimpar();
	}

	// ---------------------------------------------------------------- custo e o mexer
	private void ItOCusto()
	{
		GD.Print("[teleporte] -- o custo de Ki continua o kireq");
		Vec2 chao = CorredorLivre(16);
		ServerPlayer eu = ItCorpo("Eu", chao, 5_000, 1, "it_custo");
		eu.Ficha.teleskill = 200;
		eu.Ficha.Ki = eu.Ficha.MaxKi;
		ServerPlayer alvo = ItCorpo("Alvo", chao + new Vec2(4 * T, 0), 8_000, 1, "it_custo_alvo");
		double antes = eu.Ficha.Ki;
		ItViajar(eu, "#" + alvo.Id);
		AfirmarIt("teleskill 200 cobra metade do tanque (kireq), e nao zera",
			ItPerto(eu, alvo) && Math.Abs(eu.Ficha.Ki - antes / 2) < 1e-3 && eu.Ficha.Ki > 0,
			$"ki {eu.Ficha.Ki:0} de {antes:0}");
		ItLimpar();
	}

	private void ItMexerCancela()
	{
		GD.Print("[teleporte] -- mexer cancela");
		Vec2 chao = CorredorLivre(20);
		ServerPlayer eu = ItCorpo("Eu", chao, 5_000, TransmissaoEmNiveis.Regiao, "it_mex_eu");
		ServerPlayer alvo = ItCorpo("Alvo", chao + new Vec2(12 * T, 0), 8_000, 1, "it_mex_alvo");
		Ouvir(() => TeletransporteG11(eu, "#" + alvo.Id));
		eu.Pos += new Vec2(10, 0);
		List<string> falas = Ouvir(() => TickDoEfetorG11());
		AfirmarIt("mexer mais de 4 px cancela, e ninguem viaja",
			!_transmissaoG11.ContainsKey(eu.Id) && Disse(falas, "mexeu") && !ItPerto(eu, alvo), Ultimos(falas));
		ItLimpar();
	}
}
