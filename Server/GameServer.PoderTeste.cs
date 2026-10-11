using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--poderteste` -- POWER UP E POWER DOWN, DE PONTA A PONTA PELO FUNIL DO SERVIDOR.
///
/// O pedido do dono (2026-10-11): *"Verifique pra mim se a capacidade de dar power up e power down pra poder
/// esconder a presença ou ficar mais fraco de propósito ja ta no jogo, se nao implemente."*
///
/// ============================ O QUE ELA MEDE ============================
///   1. A PORTA: os dois verbos de controle (`Power_Control`, `Conceal_Power`) so pra quem tem o nivel 5 de
///      Basic Ki Control (`Mind.dm:279-283`); relaxar e de todo corpo (`mob/default/verb/ReadyUp`);
///   2. BAIXAR: o poder expresso cai NA HORA, e o Sense, o scouter e a IA de quem esta perto leem o poder
///      contido; pedir mais nao levanta; as bordas (0, 500, negativo, NaN, infinito, texto);
///   3. SUBIR COM C: a ordem do `Energy_Draw` (o tanque, depois o poder, depois a carga), a velocidade pela
///      pericia de ki, andando nao rende, e sem controle de ki o C nao sobe o poder;
///   4. RELAXAR / FICAR PRONTO: o piso de 1%, a volta a 100% de qualquer ponto, a raiva que acorda, e o fraco
///      relaxado que some do Sense;
///   5. ESCONDER: o expresso em 5, o Sense e o scouter que nao acham, a telepatia, a regeneracao de Ki pela
///      metade, o vigor poupado, a forma que nao derruba o sigilo, a trava de 5 s, e a tecnica de sumir que
///      acaba sem levar o sigilo junto;
///   6. O SIGILO NAO ENTREGA BP: escondido, o Sense nao vira scouter e o multiplicador nao entrega o BP base;
///   7. AS VALIDACOES: caido nao mexe, e o canal dos tres verbos e fechado pra quem perdeu as redeas;
///   8. O RETRATO NO FIO: sai uma vez, so quando muda, com dois bytes e nenhum numero de poder;
///   9. O LOGIN: o poder contido nao vem do disco; e a fiacao (o tique e o login chamam o que tem de chamar).
///
/// Cada regra nova guarda o seu defeito injetado, lido pela propria linha do conserto. Corpos forjados, sem
/// cliente, como as vizinhas (`--sentidosteste`, `--seloteste`). Roda no boot e o servidor continua de pe.
///
///     Godot --headless --path . --server --port 8360 --poderteste
/// </summary>
public partial class GameServer
{
	private int _pwOk, _pwFalhou;

	private void AfirmarPw(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _pwOk++; GD.Print($"[poder]   OK    {oque}"); return; }
		_pwFalhou++;
		GD.PrintErr($"[poder]   FALHA {oque}   {detalhe}");
	}

	private static bool PertoPw(double a, double b, double folga) => Math.Abs(a - b) <= folga;

	public void RodarBancadaDoPoder()
	{
		_pwOk = _pwFalhou = 0;
		GD.Print("[poder] ================ POWER UP E POWER DOWN: baixar, subir, relaxar, esconder ================");
		List<(int, NetDataWriter)>? escutaAnterior = EscutaDoPoderEmUso;
		try
		{
			if (_skills == null) { AfirmarPw("o catalogo de skills carregou", false); return; }
			_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);

			PwAPorta();
			LimparTudoDaBancada();
			PwBaixar();
			LimparTudoDaBancada();
			PwSubirComC();
			LimparTudoDaBancada();
			PwRelaxar();
			LimparTudoDaBancada();
			PwEsconder();
			LimparTudoDaBancada();
			PwOSigiloNaoEntregaBp();
			LimparTudoDaBancada();
			PwAsValidacoes();
			LimparTudoDaBancada();
			PwORetratoNoFio();
			LimparTudoDaBancada();
			PwOLoginEAFiacao();
		}
		catch (Exception e)
		{
			_pwFalhou++;
			GD.PrintErr($"[poder]   FALHA a bancada rodou inteira   {e}");
		}
		finally
		{
			// O PLACAR SAI DAQUI, haja o que houver -- e os botoes de defeito voltam todos ao lugar.
			EscutaDoPoderEmUso = escutaAnterior;
			PoderEmUso.RaivaDeixaRelaxarDeTeste = false;
			PoderEmUso.SigiloNaoPoupaVigorDeTeste = false;
			PoderMexeCaidoDeTeste = SigiloCaiComOutraTecnicaDeTeste = PoderVemDoDiscoDeTeste = RetratoDoPoderCongeladoDeTeste = false;
			SenseContraOPisoDeTeste = MultiplicadorEntregaOBpDeTeste = false;
			try { LimparTudoDaBancada(); } catch (Exception e) { GD.PrintErr($"[poder]   (limpeza) {e.Message}"); }
			GD.Print($"[poder] ================ {_pwOk} OK, {_pwFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// OS CORPOS
	// =====================================================================

	/// <summary>
	/// Um corpo da bancada, com a propria conta (a assinatura sai de conta+slot). `comAPorta` da o controle de ki
	/// pelo caminho de producao: Ki Unlocked e Basic Ki Control no livro, o DEGRAU 5 nos niveis, os efeitos da
	/// skill e do nivel aplicados (e dai que saem `MeditateGivesKiRegen`, `canPower` e os dois verbos).
	/// </summary>
	private ServerPlayer PwCorpo(string nome, Vec2 onde, double bp, bool comAPorta, bool sense = false, int nivel = 5)
	{
		var skills = new List<string>();
		if (comAPorta) { skills.Add(SkillKiUnlocked); skills.Add(SkillKiControl); }
		if (sense) skills.Add(StSense);
		ServerPlayer pl = ForjarComSkills(nome, onde, bp, skills: [.. skills],
										  degraus: comAPorta ? new[] { (SkillKiControl, nivel) } : null, efeitosDeSkill: true);
		pl.Conta = "bancada_poder_" + nome.ToLowerInvariant().Replace(' ', '_');
		if (comAPorta)
		{
			pl.Niveis.Aplicar(pl.Ficha);
			pl.Ficha.Statify();
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			pl.Ficha.stamina = pl.Ficha.maxstamina;
			pl.Ficha.PowerLevel(agoraMs: NowMs());
		}
		if (sense) AplicarPoderes(pl);
		return pl;
	}

	/// <summary>Um corpo com scouter ligado (o bit que a mochila acende ao equipar).</summary>
	private ServerPlayer PwOlho(string nome, Vec2 onde)
	{
		ServerPlayer pl = PwCorpo(nome, onde, 10_000, comAPorta: false);
		pl.PoderesConcedidos |= Protocol.Poder.Scouter;
		AplicarPoderes(pl);
		return pl;
	}

	private void PwTique(ServerPlayer pl) => pl.Ficha.Tick(agoraMs: NowMs());

	// =====================================================================
	// 1) A PORTA
	// =====================================================================
	private void PwAPorta()
	{
		GD.Print("[poder] -- 1) A PORTA: controlar e esconder pedem Basic Ki Control 5; relaxar e de todo corpo");
		Vec2 c = CorredorLivre(20);
		ServerPlayer novato = PwCorpo("Novato", c, 10_000, comAPorta: false);

		List<string> f = ApertarEOuvir(novato, "Power_Control:40");
		AfirmarPw("sem o nivel 5 de Basic Ki Control, `Power_Control:40` e recusado (\"voce nao sabe\") e o poder fica inteiro",
				  Disse(f, "nao sabe") && PertoPw(novato.Ficha.powerMod, 1, 1e-9), $"powerMod {novato.Ficha.powerMod} | {Ultimos(f)}");
		f = ApertarEOuvir(novato, "Conceal_Power");
		AfirmarPw("...e `Conceal_Power` tambem: ninguem esconde o poder sem a porta",
				  Disse(f, "nao sabe") && !novato.Ficha.isconcealed, Ultimos(f));

		ServerPlayer quase = PwCorpo("Quase", Em(c, 3, 0), 10_000, comAPorta: true, nivel: 4);
		AfirmarPw("o nivel 4 ainda NAO abre: os dois verbos nascem no degrau 5 (`if(level == 5)`, Mind.dm:279)",
				  !SabeTecnica(quase, "Power_Control") && !SabeTecnica(quase, "Conceal_Power") && quase.Ficha.canPower == 0,
				  $"canPower {quase.Ficha.canPower}");

		ServerPlayer mestre = PwCorpo("Controlado", Em(c, 6, 0), 10_000, comAPorta: true);
		AfirmarPw("no nivel 5 os dois verbos chegam pelo DEGRAU, junto do `canPower` (o que deixa o C subir o poder)",
				  SabeTecnica(mestre, "Power_Control") && SabeTecnica(mestre, "Conceal_Power")
				  && mestre.Ficha.canPower != 0 && mestre.Ficha.MeditateGivesKiRegen != 0,
				  $"canPower {mestre.Ficha.canPower}, MeditateGivesKiRegen {mestre.Ficha.MeditateGivesKiRegen}");

		f = ApertarEOuvir(novato, "prontidao");
		AfirmarPw("**RELAXAR E DE TODO CORPO** (`mob/default/verb/ReadyUp`): sem porta nenhuma, o novato leva o proprio poder ao piso de 1%",
				  PertoPw(novato.Ficha.powerMod, PoderEmUso.Piso, 1e-9) && Disse(f, "relaxa"), $"powerMod {novato.Ficha.powerMod} | {Ultimos(f)}");
		f = ApertarEOuvir(novato, "prontidao");
		AfirmarPw("...e fica pronto de novo, com o poder inteiro",
				  PertoPw(novato.Ficha.powerMod, 1, 1e-9) && Disse(f, "pronto"), $"powerMod {novato.Ficha.powerMod} | {Ultimos(f)}");
	}

	// =====================================================================
	// 2) BAIXAR -- `Power_Control`, `Power Control.dm:176-185`
	// =====================================================================
	private void PwBaixar()
	{
		GD.Print("[poder] -- 2) BAIXAR pra uma porcentagem: o expresso, o Sense, o scouter, a IA e o ganho de luta");
		Vec2 c = CorredorLivre(28);
		ServerPlayer eu = PwCorpo("Contido", c, 10_000, comAPorta: true);
		ServerPlayer vidente = PwCorpo("VidenteDoPoder", Em(c, 4, 0), 10_000, comAPorta: false, sense: true);
		ServerPlayer olho = PwOlho("OlhoDoPoder", Em(c, 6, 0));

		double cheio = eu.Ficha.expressedBP;
		float senseAntes = Acha(SentidosDe(vidente).Lista, eu)?.PoderRelativo ?? float.NaN;
		double scanAntes = Acha(SentidosDe(olho).Lista, eu)?.Bp ?? double.NaN;
		double ganhoAntes = eu.Ficha.FightGainMult(vidente.Ficha);
		AfirmarPw("(montagem) com o poder inteiro: o Sense do vizinho le uma porcentagem, o scouter le o numero, e lutar com um igual rende 1x",
				  cheio > 5 && senseAntes > 0 && PertoPw(scanAntes, Math.Round(cheio, MidpointRounding.AwayFromZero), 1e-6) && PertoPw(ganhoAntes, 1, 1e-9),
				  $"expresso {cheio:0}, sense {senseAntes}%, scan {scanAntes:0}, ganho {ganhoAntes:0.##}x");

		List<string> f = ApertarEOuvir(eu, "Power_Control:40");
		AfirmarPw("`Power_Control:40` segura o `powerMod` em 0,4, e diz isso",
				  PertoPw(eu.Ficha.powerMod, 0.4, 1e-9) && Disse(f, "40%"), $"powerMod {eu.Ficha.powerMod} | {Ultimos(f)}");
		AfirmarPw("**O PODER EXPRESSO CAI NA HORA** (a descida do DM nao tem `sleep`: e uma atribuicao): 40% do que era, antes de qualquer tique de ficha",
				  PertoPw(eu.Ficha.expressedBP, cheio * 0.4, Math.Max(1, cheio * 0.001)), $"{eu.Ficha.expressedBP:0} vs {cheio * 0.4:0}");
		PwTique(eu);
		AfirmarPw("...e continua em 40% depois do tique de ficha (`expressedBP = (...) * powerMod`, base.dm:135)",
				  PertoPw(eu.Ficha.expressedBP, cheio * 0.4, Math.Max(1, cheio * 0.001)), $"{eu.Ficha.expressedBP:0} vs {cheio * 0.4:0}");

		float senseDepois = Acha(SentidosDe(vidente).Lista, eu)?.PoderRelativo ?? float.NaN;
		AfirmarPw("o SENSE de quem esta perto le o poder CONTIDO: 40% da leitura de antes",
				  PertoPw(senseDepois, senseAntes * 0.4, 1.01), $"{senseAntes}% -> {senseDepois}%");
		double scanDepois = Acha(SentidosDe(olho).Lista, eu)?.Bp ?? double.NaN;
		AfirmarPw("o SCOUTER de quem esta perto le o numero contido (40% do que lia)",
				  PertoPw(scanDepois, scanAntes * 0.4, Math.Max(1, scanAntes * 0.001)), $"{scanAntes:0} -> {scanDepois:0}");
		Percepcao p = LerPercepcao(vidente, eu, eu.Pos, quemAtira: false);
		AfirmarPw("a IA de um NPC le o MESMO poder contido (`target.expressedBP`, NPCAI.dm:382/517): a razao de poder cai junto",
				  PertoPw(p.PoderDoAlvo, eu.Ficha.expressedBP, 1e-6) && PertoPw(p.RazaoDePoder, eu.Ficha.expressedBP / vidente.Ficha.expressedBP, 1e-6),
				  $"PoderDoAlvo {p.PoderDoAlvo:0}, razao {p.RazaoDePoder:0.###}");
		double ganhoDepois = eu.Ficha.FightGainMult(vidente.Ficha);
		AfirmarPw("TREINAR CONTIDO RENDE MAIS: contra o mesmo vizinho o bonus de luta sobe de 1x pro teto de 2x (`fight_gain_mult` le o expresso, combatgains.dm:93-95)",
				  PertoPw(ganhoDepois, GainKnobs.FightGainCap, 1e-9), $"{ganhoAntes:0.##}x -> {ganhoDepois:0.##}x");

		f = ApertarEOuvir(eu, "Power_Control:80");
		AfirmarPw("pedir MAIS do que se expressa nao levanta nada, e a recusa diz como subir",
				  PertoPw(eu.Ficha.powerMod, 0.4, 1e-9) && Disse(f, "tecla C"), $"powerMod {eu.Ficha.powerMod} | {Ultimos(f)}");
		ApertarEOuvir(eu, "Power_Control:0");
		AfirmarPw("zero e preso ao piso de 1% (`max(1,numb)`, :180)", PertoPw(eu.Ficha.powerMod, 0.01, 1e-9), $"{eu.Ficha.powerMod}");
		eu.Ficha.powerMod = 1;
		ApertarEOuvir(eu, "Power_Control:-30");
		AfirmarPw("...e um numero negativo tambem", PertoPw(eu.Ficha.powerMod, 0.01, 1e-9), $"{eu.Ficha.powerMod}");
		eu.Ficha.powerMod = 1;
		ApertarEOuvir(eu, "Power_Control:500");
		AfirmarPw("500 e preso a 100% (`min(numb,100)`, :179) -- e 100% nao baixa nada", PertoPw(eu.Ficha.powerMod, 1, 1e-9), $"{eu.Ficha.powerMod}");
		ApertarEOuvir(eu, "Power_Control:NaN");
		ApertarEOuvir(eu, "Power_Control:Infinity");
		ApertarEOuvir(eu, "Power_Control:-Infinity");
		AfirmarPw("NaN e infinito nao sao numero de cliente: o poder continua um numero, e inteiro",
				  double.IsFinite(eu.Ficha.powerMod) && PertoPw(eu.Ficha.powerMod, 1, 1e-9), $"{eu.Ficha.powerMod}");
		f = ApertarEOuvir(eu, "Power_Control");
		AfirmarPw("o verbo SEM numero (o botao da aba Skills) nao mexe em nada e diz ONDE se escolhe: na aba Ki do menu",
				  PertoPw(eu.Ficha.powerMod, 1, 1e-9) && Disse(f, "aba Ki") && !Disse(f, "Power_Control:"), Ultimos(f));
	}

	// =====================================================================
	// 3) SUBIR COM C -- `Energy_Draw`, `Power Control.dm:148-167`
	// =====================================================================
	private void PwSubirComC()
	{
		GD.Print("[poder] -- 3) SUBIR segurando C: o tanque, depois o poder, depois a carga");
		const double dt = 1.0 / 30;
		Vec2 c = CorredorLivre(20);
		ServerPlayer eu = PwCorpo("Carregador", c, 10_000, comAPorta: true);
		Fighter f = eu.Ficha;
		f.powerMod = 0.4;
		f.Ki = f.MaxKi * 0.5;
		f.maxstamina = Math.Max(f.maxstamina, 100_000);   // a medida e a ORDEM e a TAXA, e nao o folego acabando no meio
		f.stamina = f.maxstamina;

		Carregar(eu, true);
		AfirmarPw("(montagem) a tecla C e aceita: o corpo sabe reunir energia e tem controle de ki", eu.Carregando);

		bool subiuAntesDoTanque = false;
		int t = 0;
		while (f.Ki < f.MaxKi - 1e-6 && t++ < 3000)
		{
			TickDaCarga(eu, dt);
			if (f.powerMod > 0.4 + 1e-9 && f.Ki < f.MaxKi - 1e-6) subiuAntesDoTanque = true;
		}
		AfirmarPw("**A ORDEM, 1**: primeiro o TANQUE -- enquanto o Ki nao chega a 100%, o `powerMod` nao sai do lugar (`if(Ki<MaxKi) ... else if(powerMod<1)`, :149-153)",
				  !subiuAntesDoTanque && f.Ki >= f.MaxKi - 1e-6 && t < 3000, $"em {t * dt:0.0} s; powerMod {f.powerMod:0.###}");

		double habilidade = f.Ekiskill + (f.kicontrolskill + f.kigatheringskill) / 15;
		double esperado = (1 - f.powerMod) / (0.1 * habilidade);
		bool retomou = false, ultrapassouAntes = false;
		int passos = 0;
		while (f.powerMod < 1 - 1e-9 && passos++ < 30_000)
		{
			TickDaCarga(eu, dt);
			if (eu.EstagioDaCarga == EstagioDaCarga.Retomando) retomou = true;
			if (eu.EstagioDaCarga == EstagioDaCarga.Ultrapassando && f.powerMod < 1 - 1e-9) ultrapassouAntes = true;
		}
		double levou = passos * dt;
		AfirmarPw($"**A ORDEM, 2**: depois o PODER -- `powerMod += 0.01 * (Ekiskill + (controle + reuniao)/15)` por chamada a 10 Hz (:155): de onde o tanque encheu ate 100% em {esperado:0.00} s",
				  retomou && PertoPw(levou, esperado, 0.12 + esperado * 0.03), $"levou {levou:0.00} s; habilidade {habilidade:0.###}");
		AfirmarPw("**A ORDEM, 3**: e SO ENTAO a carga passa dos 100% -- com o poder contido o estagio de ultrapassar nao acontece (`else if(... powerupcap >= kiratio)`, :158)",
				  !ultrapassouAntes, $"powerMod {f.powerMod:0.###}");
		bool ultrapassou = false;
		for (int i = 0; i < 90 && !ultrapassou; i++)
		{
			TickDaCarga(eu, dt);
			ultrapassou = eu.EstagioDaCarga == EstagioDaCarga.Ultrapassando;
		}
		AfirmarPw("...e com o poder inteiro ela passa (o terceiro estagio chega)", ultrapassou && f.Ki > f.MaxKi, $"Ki {f.Ki / f.MaxKi * 100:0.#}%");
		PararCarga(eu);

		// ---- andando nao rende (decisao do dono, declarada em `EstagioDaCarga.Andando`; no DM rende um quarto) ----
		f.powerMod = 0.4;
		f.Ki = f.MaxKi;
		Carregar(eu, true);
		eu.Moving = true;
		for (int i = 0; i < 60; i++) TickDaCarga(eu, dt);
		AfirmarPw("ANDANDO o poder nao sobe (no DM subiria a um quarto da taxa, :154; o dono mandou plantar o pe -- `EstagioDaCarga.Andando`)",
				  PertoPw(f.powerMod, 0.4, 1e-9), $"powerMod {f.powerMod:0.###}");
		eu.Moving = false;
		PararCarga(eu);

		// ---- sem controle de ki o C so enche o tanque: quem relaxou volta ficando pronto ----
		ServerPlayer novato = PwCorpo("SoKiLiberado", Em(c, 4, 0), 10_000, comAPorta: false);
		novato.Ficha.MeditateGivesKiRegen = 1;   // sente o proprio ki (Ki Unlocked), e mais nada
		ApertarEOuvir(novato, "prontidao");
		Carregar(novato, true);
		for (int i = 0; i < 150; i++) TickDaCarga(novato, dt);
		PararCarga(novato);
		AfirmarPw("SEM CONTROLE DE KI (`canPower` 0) o C nao sobe o poder de quem relaxou: cinco segundos segurando e ele continua em 1% (o galho de :169-173 so enche o tanque)",
				  PertoPw(novato.Ficha.powerMod, PoderEmUso.Piso, 1e-9), $"powerMod {novato.Ficha.powerMod}");
		ApertarEOuvir(novato, "prontidao");
		AfirmarPw("...e o caminho dele de volta e FICAR PRONTO: 100% na hora (`powerMod=1`, :52)", PertoPw(novato.Ficha.powerMod, 1, 1e-9), $"{novato.Ficha.powerMod}");
	}

	// =====================================================================
	// 4) RELAXAR / FICAR PRONTO -- `ReadyUp` e `RelaxCheck`, `Power Control.dm:39-52` e `:87-111`
	// =====================================================================
	private void PwRelaxar()
	{
		GD.Print("[poder] -- 4) RELAXAR e FICAR PRONTO: o piso, a volta, a raiva que acorda, o fraco que some do Sense");
		Vec2 c = CorredorLivre(28);
		ServerPlayer eu = PwCorpo("Relaxador", c, 10_000, comAPorta: false);
		ServerPlayer vidente = PwCorpo("VidenteDoRelaxo", Em(c, 4, 0), 10_000, comAPorta: false, sense: true);
		double cheio = eu.Ficha.expressedBP;
		AfirmarPw("(montagem) de pe e calmo, o estado e \"usando todo o poder\" (Using Full Power)",
				  PoderEmUso.Estado(eu.Ficha) == EstadoDoPoder.PoderTotal && PoderEmUso.Porcento(eu.Ficha) == 100);

		List<string> f = ApertarEOuvir(eu, "prontidao");
		AfirmarPw("relaxar leva o `powerMod` ao piso de 0,01 (`powerMod=0.01`, :48) e o estado vira \"relaxado\"",
				  PertoPw(eu.Ficha.powerMod, 0.01, 1e-9) && PoderEmUso.Estado(eu.Ficha) == EstadoDoPoder.Relaxado && PoderEmUso.Porcento(eu.Ficha) == 1
				  && Disse(f, "relaxa"), $"powerMod {eu.Ficha.powerMod} | {Ultimos(f)}");
		AfirmarPw("...e o poder expresso cai pra 1% NA HORA", PertoPw(eu.Ficha.expressedBP, cheio * 0.01, Math.Max(1, cheio * 0.001)),
				  $"{eu.Ficha.expressedBP:0.##} vs {cheio * 0.01:0.##}");
		f = ApertarEOuvir(eu, "prontidao");
		AfirmarPw("apertar de novo: pronto, poder inteiro (`powerMod=1`, :52)",
				  PertoPw(eu.Ficha.powerMod, 1, 1e-9) && PoderEmUso.Estado(eu.Ficha) == EstadoDoPoder.PoderTotal && Disse(f, "pronto")
				  && PertoPw(eu.Ficha.expressedBP, cheio, Math.Max(1, cheio * 0.001)), $"powerMod {eu.Ficha.powerMod}, expresso {eu.Ficha.expressedBP:0}");

		eu.Ficha.powerMod = 0.4;
		AfirmarPw("(montagem) a 40% o estado e \"usando parte do poder\" (Using some power.)",
				  PoderEmUso.Estado(eu.Ficha) == EstadoDoPoder.Contendo && PoderEmUso.Porcento(eu.Ficha) == 40);
		ApertarEOuvir(eu, "prontidao");
		AfirmarPw("de QUALQUER ponto abaixo de 100% o verbo e \"ficar pronto\": de 40% ele volta a 100%, e nao relaxa (`if(powerMod >= 1) ... else`, :42-52)",
				  PertoPw(eu.Ficha.powerMod, 1, 1e-9), $"{eu.Ficha.powerMod}");

		// ---- o fraco relaxado some do Sense ----
		ServerPlayer fraco = PwCorpo("FracoRelaxado", Em(c, 2, 0), 400, comAPorta: false);
		bool sentidoAntes = Acha(SentidosDe(vidente).Lista, fraco) != null;
		ApertarEOuvir(fraco, "prontidao");
		PwTique(fraco);
		AfirmarPw("QUEM E FRACO SOME SO DE RELAXAR: 1% de um poder pequeno expressa 5 ou menos, e o Sense nao lista quem expressa 5 ou menos (HtmlUI.dm:371)",
				  sentidoAntes && fraco.Ficha.expressedBP <= BpMinimoSentivel && Acha(SentidosDe(vidente).Lista, fraco) == null,
				  $"sentido antes {sentidoAntes}; expresso {fraco.Ficha.expressedBP:0.##}");
		AfirmarPw("...e quem e forte nao: relaxado, o corpo de poder alto continua na lista, com 1% da leitura",
				  PwSenteRelaxado(eu, vidente), "");

		// ---- a raiva acorda ----
		eu.RaivaLendariaAte = NowMs() + 60_000;   // a janela da raiva comum (um amigo nocauteado)
		ProjetarRaiva(eu);
		AfirmarPw("(montagem) com a janela de raiva aberta, a raiva passa do limiar de `((MaxAnger-100)/1.66)+100` (:91)",
				  eu.Ficha.Anger > PoderEmUso.RaivaQueAcorda(eu.Ficha), $"raiva {eu.Ficha.Anger:0.#} contra {PoderEmUso.RaivaQueAcorda(eu.Ficha):0.#}");
		f = ApertarEOuvir(eu, "prontidao");
		AfirmarPw("**FURIOSO NAO SE RELAXA** (`RelaxCheck`, :90-95): o poder volta inteiro no mesmo aperto, com as duas frases do original",
				  PertoPw(eu.Ficha.powerMod, 1, 1e-9) && Disse(f, "furioso") && Disse(f, "briga"), $"powerMod {eu.Ficha.powerMod} | {Ultimos(f)}");
		PoderEmUso.RaivaDeixaRelaxarDeTeste = true;
		try
		{
			ApertarEOuvir(eu, "prontidao");
			AfirmarPw("(defeito injetado: a raiva deixa relaxar) a prova de cima REPROVA -- furioso, o corpo fica em 1% do poder",
					  PertoPw(eu.Ficha.powerMod, 0.01, 1e-9), $"{eu.Ficha.powerMod}");
		}
		finally { PoderEmUso.RaivaDeixaRelaxarDeTeste = false; eu.Ficha.powerMod = 1; }

		eu.RaivaLendariaAte = 0;
		ProjetarRaiva(eu);
		ApertarEOuvir(eu, "prontidao");
		bool relaxouCalmo = PertoPw(eu.Ficha.powerMod, 0.01, 1e-9);
		eu.RaivaLendariaAte = NowMs() + 60_000;
		ProjetarRaiva(eu);
		// (a escuta ligada e o que faz o tique considerar um corpo SEM TELA: em jogo ele so olha jogador -- NPC nao relaxa)
		List<(int, NetDataWriter)>? escutaDeAntes = EscutaDoPoderEmUso;
		EscutaDoPoderEmUso = [];
		List<string> noTique;
		try { noTique = Ouvir(() => { for (int i = 0; i < TicksEntreFatos; i++) TickDoPoderEmUso(); }); }
		finally { EscutaDoPoderEmUso = escutaDeAntes; }
		AfirmarPw("...e a raiva que chega DEPOIS acorda quem ja estava relaxado, pelo tique (5 Hz)",
				  relaxouCalmo && PertoPw(eu.Ficha.powerMod, 1, 1e-9) && Disse(noTique, "furioso"), $"relaxou calmo {relaxouCalmo}; powerMod {eu.Ficha.powerMod} | {Ultimos(noTique)}");
		eu.RaivaLendariaAte = 0;
		ProjetarRaiva(eu);
	}

	/// <summary>Relaxa `eu`, confere que `vidente` ainda o sente (com cerca de 1% da leitura) e o deixa pronto de novo.</summary>
	private bool PwSenteRelaxado(ServerPlayer eu, ServerPlayer vidente)
	{
		eu.Ficha.powerMod = 1;
		PwTique(eu);
		float antes = Acha(SentidosDe(vidente).Lista, eu)?.PoderRelativo ?? float.NaN;
		ApertarEOuvir(eu, "prontidao");
		PwTique(eu);
		float depois = Acha(SentidosDe(vidente).Lista, eu)?.PoderRelativo ?? float.NaN;
		ApertarEOuvir(eu, "prontidao");
		PwTique(eu);
		return antes > 50 && PertoPw(depois, antes * 0.01, 1.01);
	}

	// =====================================================================
	// 5) ESCONDER -- `Conceal_Power`, `Power Control.dm:71-85`
	// =====================================================================
	private void PwEsconder()
	{
		GD.Print("[poder] -- 5) ESCONDER o poder: o que trava, quem deixa de achar, o que custa, e o que nao o derruba");
		Vec2 c = CorredorLivre(28);
		ServerPlayer eu = PwCorpo("Oculto", c, 10_000, comAPorta: true);
		ServerPlayer vidente = PwCorpo("VidenteDoOculto", Em(c, 4, 0), 10_000, comAPorta: false, sense: true);
		ServerPlayer olho = PwOlho("OlhoDoOculto", Em(c, 6, 0));
		double cheio = eu.Ficha.expressedBP;
		double regenAntes = eu.Ficha.KIregen;
		AfirmarPw("(montagem) a vista: o Sense e o scouter do vizinho o listam, e a telepatia acha a energia dele",
				  Acha(SentidosDe(vidente).Lista, eu) != null && Acha(SentidosDe(olho).Lista, eu) != null && AchoAEnergiaG4(eu));

		_sigiloPronto.Remove(eu.Id);
		List<string> f = ApertarEOuvir(eu, "Conceal_Power");
		AfirmarPw("esconder liga o sigilo e trava o poder expresso em 5 NA HORA (`if(isconcealed&&expressedBP>=5) expressedBP = 5`, base.dm:114-115)",
				  eu.Ficha.isconcealed && PertoPw(eu.Ficha.expressedBP, 5, 1e-9) && Disse(f, "esconde"), $"expresso {eu.Ficha.expressedBP:0.##} | {Ultimos(f)}");
		PwTique(eu);
		AfirmarPw("...e ele fica em 5 depois do tique de ficha", PertoPw(eu.Ficha.expressedBP, 5, 1e-9), $"{eu.Ficha.expressedBP:0.##}");
		AfirmarPw("o estado vira \"escondendo o poder\" (Concealing Power), com o `powerMod` ainda em 100%",
				  PoderEmUso.Estado(eu.Ficha) == EstadoDoPoder.Escondendo && PoderEmUso.Porcento(eu.Ficha) == 100);
		AfirmarPw("**O SENSE NAO O ACHA** (`D.isconcealed`, HtmlUI.dm:370)", Acha(SentidosDe(vidente).Lista, eu) == null, Nomes(SentidosDe(vidente).Lista));
		AfirmarPw("**O SCOUTER TAMBEM NAO**, pelo caminho de producao: o scan pula quem expressa 5 ou menos (HtmlUI.dm:411), e escondido e 5",
				  Acha(SentidosDe(olho).Lista, eu) == null, Nomes(SentidosDe(olho).Lista));
		AfirmarPw("a telepatia, o observar e a Transmissao nao acham a energia dele (`M.isconcealed`, Communication.dm:53, observe.dm:9)", !AchoAEnergiaG4(eu));
		Percepcao p = LerPercepcao(vidente, eu, eu.Pos, quemAtira: false);
		AfirmarPw("a IA de um NPC le 5: pra ela o escondido e fraco (nao escala, nao recua)", PertoPw(p.PoderDoAlvo, 5, 1e-9), $"{p.PoderDoAlvo:0.##}");
		AfirmarPw("O PRECO, 1: a regeneracao de Ki cai pela metade (`concealeddeBuff=0.5`, master.dm:137 e :184)",
				  PertoPw(eu.Ficha.KIregen, regenAntes * 0.5, regenAntes * 0.001 + 1e-12), $"{regenAntes:0.#####} -> {eu.Ficha.KIregen:0.#####}");

		// ---- o vigor poupado: o dreno passivo dividido por 1,5 (`StaminaDrain.dm:30`) ----
		double Gasto()
		{
			eu.Ficha.stamina = eu.Ficha.maxstamina;
			Nutricao.QueimarVigor(eu.Ficha);
			return eu.Ficha.maxstamina - eu.Ficha.stamina;
		}
		double escondido = Gasto();
		PoderEmUso.SigiloNaoPoupaVigorDeTeste = true;
		double semPoupar;
		try { semPoupar = Gasto(); }
		finally { PoderEmUso.SigiloNaoPoupaVigorDeTeste = false; }
		AfirmarPw("O PREMIO: escondido o corpo gasta menos vigor parado -- o dreno passivo dividido por 1,5 (`concealedBuff`, master.dm:138, StaminaDrain.dm:30)",
				  escondido > 0 && PertoPw(semPoupar / escondido, PoderEmUso.FolgaDeVigorDoSigilo, 1e-6), $"{semPoupar:0.######} / {escondido:0.######} = {semPoupar / Math.Max(escondido, 1e-12):0.###}");
		AfirmarPw("(defeito injetado: o `concealedBuff` calculado e jogado fora) a prova de cima REPROVA -- o gasto e o mesmo de quem esta a vista",
				  !PertoPw(semPoupar, escondido, escondido * 0.01), $"{semPoupar:0.######} vs {escondido:0.######}");
		eu.Ficha.stamina = eu.Ficha.maxstamina;

		// ---- a forma nao derruba o sigilo ----
		eu.Ficha.ssjBuff = 50;
		PwTique(eu);
		AfirmarPw("TRANSFORMAR NAO DERRUBA O SIGILO: com um multiplicador de forma de 50x o expresso continua 5 (o sigilo vem antes da forma na conta)",
				  eu.Ficha.isconcealed && PertoPw(eu.Ficha.expressedBP, 5, 1e-9), $"{eu.Ficha.expressedBP:0.##}");
		eu.Ficha.ssjBuff = 1;
		PwTique(eu);

		// ---- a trava de 5 s ----
		f = ApertarEOuvir(eu, "Conceal_Power");
		AfirmarPw("A TRAVA: apertar de novo na hora nao muda nada, e a recusa diz quanto falta (`canconceal`, `sleep(50)` = 5 s, :78)",
				  eu.Ficha.isconcealed && Disse(f, "faltam"), Ultimos(f));

		// ---- outra tecnica de sumir que acaba nao leva o sigilo junto ----
		eu.Ficha.Ki = Math.Max(eu.Ficha.Ki, Jandirus.Core.Skills.Tecnicas.InvisKiMinimo + 10);
		Invisibilidade(eu);
		bool sumiu = EstaOculto(eu.Id);
		Invisibilidade(eu);
		TickDoPoderEmUso();
		PwTique(eu);
		AfirmarPw("**QUEM ESCONDE CONTINUA ESCONDIDO**: uma invisibilidade que liga e desliga (ela apaga o `isconcealed` ao acabar) nao leva junto o sigilo ligado de proposito",
				  sumiu && !EstaOculto(eu.Id) && eu.Ficha.isconcealed && PertoPw(eu.Ficha.expressedBP, 5, 1e-9),
				  $"sumiu {sumiu}; oculto {eu.Ficha.isconcealed}; expresso {eu.Ficha.expressedBP:0.##}");
		SigiloCaiComOutraTecnicaDeTeste = true;
		try
		{
			Invisibilidade(eu);
			Invisibilidade(eu);
			TickDoPoderEmUso();
			PwTique(eu);
			AfirmarPw("(defeito injetado: o sigilo cai com a outra tecnica) a prova de cima REPROVA -- o poder de verdade aparece sem ninguem ter mandado",
					  !eu.Ficha.isconcealed && eu.Ficha.expressedBP > 5, $"oculto {eu.Ficha.isconcealed}; expresso {eu.Ficha.expressedBP:0}");
		}
		finally { SigiloCaiComOutraTecnicaDeTeste = false; }
		TickDoPoderEmUso();
		PwTique(eu);

		// ---- mostrar de novo ----
		_sigiloPronto[eu.Id] = 0;   // o prazo venceu
		f = ApertarEOuvir(eu, "Conceal_Power");
		AfirmarPw("passados os 5 s, apertar MOSTRA: o poder expresso volta inteiro na hora e o Sense o acha de novo",
				  !eu.Ficha.isconcealed && PertoPw(eu.Ficha.expressedBP, cheio, Math.Max(1, cheio * 0.001)) && Acha(SentidosDe(vidente).Lista, eu) != null,
				  $"oculto {eu.Ficha.isconcealed}; expresso {eu.Ficha.expressedBP:0} | {Ultimos(f)}");

		// ---- ligar o sigilo estando invisivel ESCONDE (e nao mostra) ----
		eu.Ficha.Ki = Math.Max(eu.Ficha.Ki, Jandirus.Core.Skills.Tecnicas.InvisKiMinimo + 10);
		Invisibilidade(eu);
		_sigiloPronto[eu.Id] = 0;
		f = ApertarEOuvir(eu, "Conceal_Power");
		AfirmarPw("ligar o sigilo estando INVISIVEL esconde (o verbo so invertia o campo, e mostrava o poder de quem ja estava sumido)",
				  eu.Ficha.isconcealed && Disse(f, "esconde") && (PoderEmUsoDe(eu).Bits & BitsDoPoderEmUso.PorVontade) != 0, Ultimos(f));
		Invisibilidade(eu);
		_sigiloPronto[eu.Id] = 0;
		ApertarEOuvir(eu, "Conceal_Power");
	}

	// =====================================================================
	// 6) O SIGILO NAO ENTREGA O BP DE NINGUEM
	// =====================================================================
	private void PwOSigiloNaoEntregaBp()
	{
		GD.Print("[poder] -- 6) A REGRA PRINCIPAL DO BP: escondido (expresso = 5, uma constante), nenhuma leitura relativa vira numero");
		Vec2 c = CorredorLivre(28);
		ServerPlayer espiao = PwCorpo("Espiao", c, 20_000, comAPorta: true, sense: true);
		ServerPlayer alvo = PwCorpo("Medido", Em(c, 3, 0), 37_000, comAPorta: false);
		double doAlvo = alvo.Ficha.expressedBP;

		float aVista = Acha(SentidosDe(espiao).Lista, alvo)?.PoderRelativo ?? float.NaN;
		AfirmarPw("(montagem) a vista, o Sense do espiao le o alvo em porcentagem do proprio poder EXPRESSO (HtmlUI.dm:375)",
				  PertoPw(aVista, Math.Round(doAlvo / espiao.Ficha.expressedBP * 100, MidpointRounding.AwayFromZero), 1e-6), $"{aVista}%");

		_sigiloPronto.Remove(espiao.Id);
		ApertarEOuvir(espiao, "Conceal_Power");
		PwTique(espiao);
		float escondido = Acha(SentidosDe(espiao).Lista, alvo)?.PoderRelativo ?? float.NaN;
		double vazaria = Math.Round(doAlvo / 5 * 100, MidpointRounding.AwayFromZero);
		AfirmarPw("**ESCONDIDO, O SENSE NAO VIRA SCOUTER**: a leitura NAO e `expresso do outro / 5` (de onde o BP do outro sairia por uma conta de cabeca)",
				  espiao.Ficha.isconcealed && !float.IsNaN(escondido) && !PertoPw(escondido, vazaria, 1) && !PertoPw(escondido * 5 / 100, doAlvo, 1),
				  $"leu {escondido}%; contra o piso seria {vazaria}% (= {vazaria * 5 / 100:0} de BP)");
		AfirmarPw("...ela e a razao contra o BP BASE de quem le: continua dizendo \"mais forte ou mais fraco que voce\"",
				  PertoPw(escondido, Math.Round(doAlvo / espiao.Ficha.BP * 100, MidpointRounding.AwayFromZero), 1e-6), $"{escondido}% vs {doAlvo / espiao.Ficha.BP * 100:0.#}%");
		SenseContraOPisoDeTeste = true;
		try
		{
			float vazou = Acha(SentidosDe(espiao).Lista, alvo)?.PoderRelativo ?? float.NaN;
			AfirmarPw("(defeito injetado: o Sense mede contra o piso) a prova de cima REPROVA -- a porcentagem vezes 5 e o BP exato do alvo",
					  PertoPw(vazou * 5 / 100, doAlvo, 1), $"{vazou}% x 5 / 100 = {vazou * 5 / 100:0} (o alvo expressa {doAlvo:0})");
		}
		finally { SenseContraOPisoDeTeste = false; }

		SheetState ficha = FichaVisivel(espiao);
		AfirmarPw("**ESCONDIDO E SEM SCOUTER, O MULTIPLICADOR TOTAL NAO VIAJA** (vai zero): inteiro ele e `5 / BP`, e o BP base sairia por uma divisao",
				  double.IsNaN(ficha.BP) && double.IsNaN(ficha.ExpressedBP) && ficha.MultTotal == 0, $"MultTotal {ficha.MultTotal}");
		MultiplicadorEntregaOBpDeTeste = true;
		try
		{
			SheetState vazada = FichaVisivel(espiao);
			AfirmarPw("(defeito injetado: o multiplicador viaja) a prova de cima REPROVA -- `5 / MultTotal` devolve o BP base",
					  vazada.MultTotal > 0 && PertoPw(5 / vazada.MultTotal, espiao.Ficha.BP, 1), $"5 / {vazada.MultTotal:0.########} = {5 / Math.Max(vazada.MultTotal, 1e-12):0}");
		}
		finally { MultiplicadorEntregaOBpDeTeste = false; }

		espiao.PoderesConcedidos |= Protocol.Poder.Scouter;
		AplicarPoderes(espiao);
		AfirmarPw("com scouter o numero existe (e o multiplicador vai inteiro): o aparelho E a porta de leitura", FichaVisivel(espiao).MultTotal > 0);
		espiao.PoderesConcedidos &= ~Protocol.Poder.Scouter;
		AplicarPoderes(espiao);

		_sigiloPronto[espiao.Id] = 0;
		ApertarEOuvir(espiao, "Conceal_Power");
		AfirmarPw("a vista de novo, o multiplicador volta a viajar (ele so e cortado enquanto o expresso e a constante)", FichaVisivel(espiao).MultTotal > 0.5);

		PoderEmUsoState retrato = PoderEmUsoDe(espiao);
		NetDataWriter w = Protocol.Begin(Protocol.S2C.PoderEmUso);
		retrato.Write(w);
		AfirmarPw("e o retrato do proprio estado tem DOIS bytes (a porcentagem do proprio poder em uso e os bits): nao cabe poder de ninguem",
				  w.Length == 3, $"{w.Length} bytes com o opcode");
	}

	// =====================================================================
	// 7) AS VALIDACOES
	// =====================================================================
	private void PwAsValidacoes()
	{
		GD.Print("[poder] -- 7) AS VALIDACOES: caido nao mexe no proprio poder, e o canal e fechado pra quem perdeu as redeas");
		Vec2 c = CorredorLivre(20);
		ServerPlayer eu = PwCorpo("Caido", c, 10_000, comAPorta: true);
		_sigiloPronto.Remove(eu.Id);

		eu.Ficha.KO = true;
		List<string> f1 = ApertarEOuvir(eu, "Power_Control:40");
		List<string> f2 = ApertarEOuvir(eu, "Conceal_Power");
		List<string> f3 = ApertarEOuvir(eu, "prontidao");
		AfirmarPw("NOCAUTEADO: baixar, esconder e relaxar sao recusados, e nada muda (nem a trava de 5 s e gasta)",
				  PertoPw(eu.Ficha.powerMod, 1, 1e-9) && !eu.Ficha.isconcealed && !_sigiloPronto.ContainsKey(eu.Id)
				  && Disse(f1, "caído") && Disse(f2, "caído") && Disse(f3, "caído"),
				  $"powerMod {eu.Ficha.powerMod}, oculto {eu.Ficha.isconcealed} | {Ultimos(f1)} | {Ultimos(f2)} | {Ultimos(f3)}");
		PoderMexeCaidoDeTeste = true;
		try
		{
			ApertarEOuvir(eu, "Power_Control:40");
			AfirmarPw("(defeito injetado: caido mexe) a prova de cima REPROVA -- o nocauteado baixa o proprio poder do chao",
					  PertoPw(eu.Ficha.powerMod, 0.4, 1e-9), $"{eu.Ficha.powerMod}");
		}
		finally { PoderMexeCaidoDeTeste = false; eu.Ficha.powerMod = 1; }
		eu.Ficha.KO = false;

		eu.Ficha.dead = true;
		bool cadaver = EhCadaver(eu);
		f1 = ApertarEOuvir(eu, "Power_Control:40");
		f3 = ApertarEOuvir(eu, "prontidao");
		AfirmarPw("CADAVER (morto que ainda nao esta de pe no Outro Mundo): recusado tambem",
				  cadaver && PertoPw(eu.Ficha.powerMod, 1, 1e-9) && Disse(f1, "sem vida") && Disse(f3, "sem vida"), $"{Ultimos(f1)} | {Ultimos(f3)}");
		eu.Ficha.dead = false;

		AfirmarPw("POSSUIDO: os tres verbos viajam no canal de habilidade, que o despacho fecha pra quem perdeu as redeas (`ComandoDeCorpo`)",
				  ComandoDeCorpo(Protocol.C2S.Habilidade));
		f1 = ApertarEOuvir(eu, "Power_Control:40");
		AfirmarPw("(o caminho legitimo continua aberto: de pe, o mesmo corpo baixa pra 40%)", PertoPw(eu.Ficha.powerMod, 0.4, 1e-9), Ultimos(f1));
	}

	// =====================================================================
	// 8) O RETRATO NO FIO
	// =====================================================================
	private void PwORetratoNoFio()
	{
		GD.Print("[poder] -- 8) O RETRATO NO FIO: o que a tela de quem joga recebe, e quando");
		Vec2 c = CorredorLivre(20);
		ServerPlayer eu = PwCorpo("Retratado", c, 10_000, comAPorta: true);
		_sigiloPronto.Remove(eu.Id);
		EscutaDoPoderEmUso = [];

		List<(byte Opcode, PoderEmUsoState Estado, bool Fim)> Meus() =>
			[.. EscutaDoPoderEmUso!.Where(e => e.Quem == eu.Id).Select(e =>
			{
				var r = new NetDataReader(e.Pacote.CopyData());
				byte op = r.GetByte();
				PoderEmUsoState s = PoderEmUsoState.Read(r);
				return (op, s, r.EndOfData);
			})];
		void UmQuintoDeSegundo() { for (int i = 0; i < TicksEntreFatos; i++) TickDoPoderEmUso(); }

		UmQuintoDeSegundo();
		var m = Meus();
		AfirmarPw("o primeiro retrato sai sozinho pelo tique: opcode `S2C.PoderEmUso`, 100% do poder, nenhum bit, e o leitor do cliente para exatamente no fim do pacote",
				  m.Count == 1 && m[0].Opcode == (byte)Protocol.S2C.PoderEmUso && m[0].Estado == PoderEmUsoState.Cheio && m[0].Fim,
				  $"{m.Count} pacote(s)");
		UmQuintoDeSegundo();
		UmQuintoDeSegundo();
		AfirmarPw("sem nada mudar, nenhum pacote novo", Meus().Count == 1, $"{Meus().Count}");

		ApertarEOuvir(eu, "Power_Control:40");
		m = Meus();
		AfirmarPw("**BAIXAR MANDA O RETRATO NA HORA**: 40%, sem esperar o tique",
				  m.Count == 2 && m[^1].Estado.Porcento == 40 && !m[^1].Estado.Relaxado && !m[^1].Estado.Escondido, $"{m.Count} pacote(s); ultimo {(m.Count > 0 ? m[^1].Estado.ToString() : "-")}");
		UmQuintoDeSegundo();
		AfirmarPw("...e o tique seguinte nao o repete", Meus().Count == 2, $"{Meus().Count}");

		ApertarEOuvir(eu, "Conceal_Power");
		m = Meus();
		AfirmarPw("esconder manda os tres bits: escondido, por vontade propria, e a espera de 5 s correndo",
				  m.Count == 3 && m[^1].Estado.Escondido && m[^1].Estado.PorVontade && m[^1].Estado.SigiloEmEspera && m[^1].Estado.Porcento == 40,
				  $"{m.Count} pacote(s); ultimo {(m.Count > 0 ? m[^1].Estado.ToString() : "-")}");
		_sigiloPronto[eu.Id] = 0;   // o prazo venceu
		UmQuintoDeSegundo();
		m = Meus();
		AfirmarPw("vencidos os 5 s o retrato sai de novo, sem o bit da espera (e o que reacende o botao na tela)",
				  m.Count == 4 && m[^1].Estado.Escondido && !m[^1].Estado.SigiloEmEspera, $"{m.Count} pacote(s)");

		ApertarEOuvir(eu, "prontidao");
		ApertarEOuvir(eu, "prontidao");
		m = Meus();
		AfirmarPw("ficar pronto (100%) e relaxar (1%, com o bit de relaxado) mandam um retrato cada",
				  m.Count == 6 && m[^2].Estado.Porcento == 100 && !m[^2].Estado.Relaxado && m[^1].Estado.Porcento == 1 && m[^1].Estado.Relaxado,
				  $"{m.Count} pacote(s)");

		RetratoDoPoderCongeladoDeTeste = true;
		try
		{
			ApertarEOuvir(eu, "prontidao");
			UmQuintoDeSegundo();
			AfirmarPw("(defeito injetado: o retrato congelado) a prova de cima REPROVA -- o poder voltou a 100% e a tela continua lendo 1%",
					  Meus().Count == 6 && PertoPw(eu.Ficha.powerMod, 1, 1e-9), $"{Meus().Count} pacote(s); powerMod {eu.Ficha.powerMod}");
		}
		finally { RetratoDoPoderCongeladoDeTeste = false; }
		UmQuintoDeSegundo();
		AfirmarPw("(desligado o defeito, o tique corrige a tela)", Meus().Count == 7 && Meus()[^1].Estado.Porcento == 100, $"{Meus().Count}");
		EscutaDoPoderEmUso = null;
	}

	// =====================================================================
	// 9) O LOGIN, E A FIACAO
	// =====================================================================
	private void PwOLoginEAFiacao()
	{
		GD.Print("[poder] -- 9) O LOGIN: o poder contido nao vem do disco; e a fiacao no servidor de verdade");
		Vec2 c = CorredorLivre(20);
		ServerPlayer eu = PwCorpo("Voltando", c, 10_000, comAPorta: true);

		eu.Ficha.powerMod = 0.01;
		AcordarComOPoderInteiro(eu);
		AfirmarPw("**QUEM SAIU DO JOGO RELAXADO ENTRA COM O PODER INTEIRO** (`powerMod` e `tmp` no DM, base.dm:37-38; aqui a ficha vai inteira pro disco)",
				  PertoPw(eu.Ficha.powerMod, 1, 1e-9), $"{eu.Ficha.powerMod}");
		PoderVemDoDiscoDeTeste = true;
		try
		{
			eu.Ficha.powerMod = 0.01;
			AcordarComOPoderInteiro(eu);
			AfirmarPw("(defeito injetado: o poder vem do disco) a prova de cima REPROVA -- ele entra a 1%, sem nada na tela que diga",
					  PertoPw(eu.Ficha.powerMod, 0.01, 1e-9), $"{eu.Ficha.powerMod}");
		}
		finally { PoderVemDoDiscoDeTeste = false; eu.Ficha.powerMod = 1; }
		eu.Ficha.powerMod = double.NaN;
		AcordarComOPoderInteiro(eu);
		AfirmarPw("...e um valor estragado no save (NaN) tambem volta a 1", PertoPw(eu.Ficha.powerMod, 1, 1e-9), $"{eu.Ficha.powerMod}");

		// ---- a fiacao: escrever a regra nao e ligar a regra ----
		string[] servidor = Fonte("Server/GameServer.cs");
		bool Chama(string[] linhas, string chamada) => linhas.Any(l => SemTextoNemComentario(l).Contains(chamada, StringComparison.Ordinal));
		AfirmarPw("FIACAO: o login chama `AcordarComOPoderInteiro(pl)` (no `GameServer.cs`, ao lado do `AcordarVisivel`)",
				  Chama(servidor, "AcordarComOPoderInteiro(pl)"), $"{servidor.Length} linhas lidas");
		AfirmarPw("FIACAO: o `Tick()` chama `TickDoPoderEmUso()`", Chama(servidor, "TickDoPoderEmUso()"));
		AfirmarPw("FIACAO: o canal de habilidade atende `prontidao` (`GameServer.Raciais.cs`)",
				  Chama(Fonte("Server/GameServer.Raciais.cs"), "AlternarProntidao(pl)"));
		AfirmarPw("FIACAO: quem sai do jogo leva o sigilo ligado junto (`EsquecerG9` chama `EsquecerOPoderEmUso`)",
				  Chama(Fonte("Server/GameServer.Tecnicas.G9.cs"), "EsquecerOPoderEmUso(id)"));

		// ---- e o tique de verdade: doze quadros do `Tick()` inteiro mandam o primeiro retrato ----
		EscutaDoPoderEmUso = [];
		_poderEmUsoMandado.Remove(eu.Id);
		int cadenciaDeAntes = _tickCount;
		_tickCount = 0;   // (os doze quadros nao cruzam o salvamento em disco, como na `--animaisteste`)
		string estouro = "";
		try { for (int i = 0; i < 12; i++) Tick(); }
		catch (Exception e) { estouro = e.ToString(); }
		finally { _tickCount = cadenciaDeAntes; }
		AfirmarPw("**O TIQUE INTEIRO DO SERVIDOR MANDA O RETRATO**: doze quadros do `Tick()` de producao, e o corpo novo recebeu o dele",
				  estouro.Length == 0 && EscutaDoPoderEmUso.Any(e => e.Quem == eu.Id), estouro.Length > 0 ? estouro : "nenhum pacote pro corpo da bancada");
		EscutaDoPoderEmUso = null;
	}
}
