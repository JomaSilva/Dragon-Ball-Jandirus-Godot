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
/// esconder a presença ou ficar mais fraco de propósito ja ta no jogo, se nao implemente."* E o retorno dele de
/// 2026-10-12, depois de jogar: *"esconder o ki e relaxar é redundante [...] relaxar e o poder cair deveria ser
/// somente pra quem sabe controlar o proprio poder, tire o botao de esconder o ki e faça com q o de relaxar faca o
/// poder do personagem cair pra 5 de poder de luta sempre, ficando invisivel pra scouters etc, mas extremamente
/// fraco fisicamente, vc mudar teu poder na barra de % n esconde o ki do scouter, ou sense, so serve pra se
/// segurar ou usar todo o poder, esconder o ki totalmente so com relaxar"*.
///
/// ============================ O QUE ELA MEDE ============================
///   1. A PORTA: a barra (`Power_Control`) e relaxar so pra quem tem o nivel 5 de Basic Ki Control
///      (`Mind.dm:279-283`); ficar pronto nao pede nada; o botao de esconder nao existe mais;
///   2. BAIXAR: o poder expresso cai NA HORA, e o Sense, o scouter e a IA de quem esta perto leem o poder
///      contido; pedir mais nao levanta; as bordas (0, 500, negativo, NaN, infinito, texto); e A BARRA NAO
///      ESCONDE -- o fraco a 1% continua em toda lista, e o forte contido continua no Sense da galaxia;
///   3. SUBIR COM C: a ordem do `Energy_Draw` (o tanque, depois o poder, depois a carga), a velocidade pela
///      pericia de ki, andando nao rende, e sem controle de ki o C nao sobe o poder;
///   4. RELAXAR / FICAR PRONTO: 5 de poder SEMPRE, fora do Sense, do scouter e da telepatia, sem custo de Ki, a
///      forma e a tecnica de sumir que nao o desfazem, a volta a 100% de qualquer ponto, a raiva e a carga que
///      acordam; e o que continua sendo de quem some por TECNICA (`isconcealed`);
///   5. ESCONDIDO NAO ENTREGA BP: relaxado, o Sense nao vira scouter e o multiplicador nao entrega o BP base;
///   6. AS VALIDACOES: caido nao mexe, e o canal dos dois verbos e fechado pra quem perdeu as redeas;
///   7. O RETRATO NO FIO: sai uma vez, so quando muda, com dois bytes e nenhum numero de poder;
///   8. O LOGIN: o poder contido nao vem do disco; e a fiacao (o tique e o login chamam o que tem de chamar).
///
/// (O VIGOR de quem se segura e de quem relaxa -- 1x a 2,5x pela barra, 3,5x relaxado -- e da `--v020teste`.)
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
		GD.Print("[poder] ================ POWER UP E POWER DOWN: baixar, subir, relaxar ================");
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
			PwEscondidoNaoEntregaBp();
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
			PoderEmUso.RelaxadoNaoEscondeDeTeste = PoderEmUso.BarraEscondeDeTeste = PoderEmUso.CargaNaoAcordaDeTeste = false;
			PoderMexeCaidoDeTeste = RelaxarSemControleDeTeste = PoderVemDoDiscoDeTeste = RetratoDoPoderCongeladoDeTeste = false;
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
	/// skill e do nivel aplicados (e dai que saem `MeditateGivesKiRegen`, `canPower` e o `Power_Control`).
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
		GD.Print("[poder] -- 1) A PORTA: conter o poder e relaxar pedem Basic Ki Control 5; ficar pronto nao pede nada");
		Vec2 c = CorredorLivre(20);
		ServerPlayer novato = PwCorpo("Novato", c, 10_000, comAPorta: false);

		List<string> f = ApertarEOuvir(novato, "Power_Control:40");
		AfirmarPw("sem o nivel 5 de Basic Ki Control, `Power_Control:40` e recusado (\"voce nao sabe\") e o poder fica inteiro",
				  Disse(f, "nao sabe") && PertoPw(novato.Ficha.powerMod, 1, 1e-9), $"powerMod {novato.Ficha.powerMod} | {Ultimos(f)}");
		f = ApertarEOuvir(novato, "prontidao");
		AfirmarPw("**RELAXAR TAMBEM PEDE A PORTA** (regra do dono; no DM o `ReadyUp` e de todo corpo): o novato e recusado, e continua com o poder inteiro e a vista",
				  Disse(f, "não sabe controlar o próprio poder") && !novato.Ficha.relaxado && PertoPw(novato.Ficha.powerMod, 1, 1e-9) && novato.Ficha.expressedBP > 5,
				  $"relaxado {novato.Ficha.relaxado}, expresso {novato.Ficha.expressedBP:0} | {Ultimos(f)}");
		RelaxarSemControleDeTeste = true;
		try
		{
			ApertarEOuvir(novato, "prontidao");
			AfirmarPw("(defeito injetado: relaxar e de todo corpo) a prova de cima REPROVA -- o novato relaxa e some com um botao",
					  novato.Ficha.relaxado && PertoPw(novato.Ficha.expressedBP, 5, 1e-9), $"relaxado {novato.Ficha.relaxado}, expresso {novato.Ficha.expressedBP:0.##}");
		}
		finally { RelaxarSemControleDeTeste = false; }
		f = ApertarEOuvir(novato, "prontidao");
		AfirmarPw("**FICAR PRONTO NAO PEDE PORTA**: quem esta relaxado sem o controle (aqui, pelo defeito) volta ao poder inteiro -- ninguem fica preso abaixo do proprio poder",
				  !novato.Ficha.relaxado && Disse(f, "pronto") && novato.Ficha.expressedBP > 5, $"relaxado {novato.Ficha.relaxado} | {Ultimos(f)}");

		ServerPlayer quase = PwCorpo("Quase", Em(c, 3, 0), 10_000, comAPorta: true, nivel: 4);
		f = ApertarEOuvir(quase, "prontidao");
		AfirmarPw("o nivel 4 ainda NAO abre: o `Power_Control` nasce no degrau 5 (`if(level == 5)`, Mind.dm:279), e relaxar e recusado igual",
				  !SabeTecnica(quase, "Power_Control") && quase.Ficha.canPower == 0 && !quase.Ficha.relaxado && Disse(f, "não sabe controlar"),
				  $"canPower {quase.Ficha.canPower} | {Ultimos(f)}");

		ServerPlayer mestre = PwCorpo("Controlado", Em(c, 6, 0), 10_000, comAPorta: true);
		AfirmarPw("no nivel 5 o verbo chega pelo DEGRAU, junto do `canPower` (o que deixa o C subir o poder)",
				  SabeTecnica(mestre, "Power_Control") && mestre.Ficha.canPower != 0 && mestre.Ficha.MeditateGivesKiRegen != 0,
				  $"canPower {mestre.Ficha.canPower}, MeditateGivesKiRegen {mestre.Ficha.MeditateGivesKiRegen}");
		f = ApertarEOuvir(mestre, "prontidao");
		AfirmarPw("...e com ele RELAXAR funciona", mestre.Ficha.relaxado && Disse(f, "relaxa"), Ultimos(f));
		ApertarEOuvir(mestre, "prontidao");

		bool antes = mestre.Ficha.relaxado, ocultoAntes = mestre.Ficha.isconcealed;
		f = ApertarEOuvir(mestre, "Conceal_Power");
		AfirmarPw("**O BOTAO DE ESCONDER SAIU**: `Conceal_Power` nao e mais tecnica viva (o censo o da por atendido pelo relaxar), e mandar o verbo antigo nao muda o estado de ninguem",
				  !Jandirus.Core.Skills.Tecnicas.Vivas.Contains("Conceal_Power") && Jandirus.Core.Skills.CensoDeSkills.AtendidoPorOutroCanal("Conceal_Power")
				  && mestre.Ficha.relaxado == antes && mestre.Ficha.isconcealed == ocultoAntes && mestre.Ficha.expressedBP > 5, Ultimos(f));
	}

	// =====================================================================
	// 2) BAIXAR -- `Power_Control`, `Power Control.dm:176-185`
	// =====================================================================
	private void PwBaixar()
	{
		GD.Print("[poder] -- 2) BAIXAR pra uma porcentagem: o expresso, o Sense, o scouter, a IA e o ganho de luta -- e a barra NAO esconde");
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

		// ---- A BARRA NAO ESCONDE (regra do dono, 2026-10-12) ----
		ServerPlayer fraco = PwCorpo("FracoContido", Em(c, 2, 0), 300, comAPorta: true);
		bool sentidoAntes = Acha(SentidosDe(vidente).Lista, fraco) != null, lidoAntes = Acha(SentidosDe(olho).Lista, fraco) != null;
		ApertarEOuvir(fraco, "Power_Control:1");
		PwTique(fraco);
		AfirmarPw("(montagem) o fraco que se segura a 1% expressa 5 ou menos -- o que no DM o tiraria do Sense e do scouter (HtmlUI.dm:370 e :411)",
				  sentidoAntes && lidoAntes && PertoPw(fraco.Ficha.powerMod, 0.01, 1e-9) && fraco.Ficha.expressedBP <= BpMinimoSentivel,
				  $"sentido {sentidoAntes}, lido {lidoAntes}, expresso {fraco.Ficha.expressedBP:0.##}");
		Protocol.PresencaState? noScouter = Acha(SentidosDe(olho).Lista, fraco);
		AfirmarPw("**A BARRA NAO ESCONDE**: a 1% ele CONTINUA no Sense do vizinho, no scouter (com o numero baixo que a barra lhe da) e ao alcance da telepatia",
				  Acha(SentidosDe(vidente).Lista, fraco) != null && noScouter != null && AchoAEnergiaG4(fraco)
				  && PertoPw(noScouter?.Bp ?? double.NaN, Math.Round(fraco.Ficha.expressedBP, MidpointRounding.AwayFromZero), 1e-6),
				  $"sense {Acha(SentidosDe(vidente).Lista, fraco) != null}, scouter {noScouter?.Bp}, telepatia {AchoAEnergiaG4(fraco)}");
		PoderEmUso.BarraEscondeDeTeste = true;
		try
		{
			AfirmarPw("(defeito injetado: o piso medido COM a barra) a prova de cima REPROVA -- o fraco que se segura some das tres listas, como no jogo de antes",
					  Acha(SentidosDe(vidente).Lista, fraco) == null && Acha(SentidosDe(olho).Lista, fraco) == null && !AchoAEnergiaG4(fraco));
		}
		finally { PoderEmUso.BarraEscondeDeTeste = false; }

		// ---- ...nem do Sense de LONGE (o universo, o nivel 5): o piso dos 5 de BP la tambem e medido sem a barra ----
		// (2026-10-18) O piso dos 5 milhoes da galaxia do DM saiu com os cinco niveis do Sense (`SenseEmNiveis`): de outro planeta o corte e o
		// mesmo de perto -- e a barra continua sem esconder ninguem.
		ServerPlayer tita = StCorpoEm("TitaContido", "Namek", 300);
		vidente.Niveis.Por(Jandirus.Core.Skills.SenseEmNiveis.Path, Jandirus.Core.Skills.SenseEmNiveis.Universo);
		bool naGalaxiaAntes = Acha(SentidosDe(vidente).Lista, tita) != null;
		tita.Ficha.powerMod = 0.01;
		PwTique(tita);
		AfirmarPw("o que se segura continua no SENSE DE LONGE (o universo): a 1% ele expressa 5 ou menos, e segue na lista de outro planeta",
				  naGalaxiaAntes && tita.Ficha.expressedBP <= BpMinimoSentivel && Acha(SentidosDe(vidente).Lista, tita) != null,
				  $"antes {naGalaxiaAntes}; expresso {tita.Ficha.expressedBP:0.##}");
		PoderEmUso.BarraEscondeDeTeste = true;
		try
		{
			AfirmarPw("(defeito injetado: o piso medido COM a barra) a prova de cima REPROVA -- contido, ele some do Sense de longe",
					  Acha(SentidosDe(vidente).Lista, tita) == null);
		}
		finally { PoderEmUso.BarraEscondeDeTeste = false; }
		vidente.Niveis.Por(Jandirus.Core.Skills.SenseEmNiveis.Path, 0);
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

		// ---- sem controle de ki o C so enche o tanque: quem esta abaixo do proprio poder volta ficando pronto ----
		ServerPlayer novato = PwCorpo("SoKiLiberado", Em(c, 4, 0), 10_000, comAPorta: false);
		novato.Ficha.MeditateGivesKiRegen = 1;   // sente o proprio ki (Ki Unlocked), e mais nada
		novato.Ficha.powerMod = 0.4;             // (posto a mao: sem a porta ele nao tem como baixar)
		Carregar(novato, true);
		for (int i = 0; i < 150; i++) TickDaCarga(novato, dt);
		PararCarga(novato);
		AfirmarPw("SEM CONTROLE DE KI (`canPower` 0) o C nao sobe o poder: cinco segundos segurando e a barra continua em 40% (o galho de :169-173 so enche o tanque)",
				  PertoPw(novato.Ficha.powerMod, 0.4, 1e-9), $"powerMod {novato.Ficha.powerMod}");
		novato.Ficha.powerMod = 1;
		novato.Ficha.relaxado = true;            // (posto a mao: sem a porta ele nao relaxa)
		Carregar(novato, true);
		PararCarga(novato);
		AfirmarPw("...e relaxado a carga dele NAO o acorda: sem `canPower` a barra nao subiria, e acordar no piso o prenderia em 1% do poder",
				  novato.Ficha.relaxado && PertoPw(novato.Ficha.powerMod, 1, 1e-9), $"relaxado {novato.Ficha.relaxado}, powerMod {novato.Ficha.powerMod}");
		ApertarEOuvir(novato, "prontidao");
		AfirmarPw("o caminho dele de volta e FICAR PRONTO, que nao pede porta: 100% na hora (`powerMod=1`, :52)",
				  !novato.Ficha.relaxado && PertoPw(novato.Ficha.powerMod, 1, 1e-9), $"relaxado {novato.Ficha.relaxado}, powerMod {novato.Ficha.powerMod}");
	}

	// =====================================================================
	// 4) RELAXAR / FICAR PRONTO -- `ReadyUp` e `RelaxCheck`, `Power Control.dm:39-52` e `:87-111`
	// =====================================================================
	private void PwRelaxar()
	{
		GD.Print("[poder] -- 4) RELAXAR e FICAR PRONTO: 5 de poder sempre, fora do Sense e do scouter, a volta, a raiva e a carga que acordam");
		const double dt = 1.0 / 30;
		Vec2 c = CorredorLivre(28);
		ServerPlayer eu = PwCorpo("Relaxador", c, 10_000, comAPorta: true);
		ServerPlayer vidente = PwCorpo("VidenteDoRelaxo", Em(c, 4, 0), 10_000, comAPorta: false, sense: true);
		ServerPlayer olho = PwOlho("OlhoDoRelaxo", Em(c, 6, 0));
		double cheio = eu.Ficha.expressedBP;
		double regenAntes = eu.Ficha.KIregen;
		AfirmarPw("(montagem) de pe e calmo: \"usando todo o poder\" (Using Full Power), e o Sense, o scouter e a telepatia do vizinho o acham",
				  PoderEmUso.Estado(eu.Ficha) == EstadoDoPoder.PoderTotal && PoderEmUso.Porcento(eu.Ficha) == 100
				  && Acha(SentidosDe(vidente).Lista, eu) != null && Acha(SentidosDe(olho).Lista, eu) != null && AchoAEnergiaG4(eu));

		List<string> f = ApertarEOuvir(eu, "prontidao");
		AfirmarPw("**RELAXAR TRAVA O PODER EXPRESSO EM 5, NA HORA** (o dono: \"cair pra 5 de poder de luta sempre\"), e diz o que fez",
				  eu.Ficha.relaxado && PertoPw(eu.Ficha.expressedBP, 5, 1e-9) && PoderEmUso.Estado(eu.Ficha) == EstadoDoPoder.Relaxado && Disse(f, "relaxa"),
				  $"relaxado {eu.Ficha.relaxado}, expresso {eu.Ficha.expressedBP:0.##} | {Ultimos(f)}");
		PwTique(eu);
		AfirmarPw("...e ele fica em 5 depois do tique de ficha, com a barra onde estava (100%)",
				  PertoPw(eu.Ficha.expressedBP, 5, 1e-9) && PertoPw(eu.Ficha.powerMod, 1, 1e-9), $"expresso {eu.Ficha.expressedBP:0.##}, powerMod {eu.Ficha.powerMod}");
		AfirmarPw("**O SENSE NAO O ACHA**", Acha(SentidosDe(vidente).Lista, eu) == null, Nomes(SentidosDe(vidente).Lista));
		AfirmarPw("**O SCOUTER TAMBEM NAO**, pelo caminho de producao: o scan pula quem nao tem energia que se sinta, e relaxado e 5",
				  Acha(SentidosDe(olho).Lista, eu) == null, Nomes(SentidosDe(olho).Lista));
		AfirmarPw("a telepatia, o observar e a Transmissao nao acham a energia dele", !AchoAEnergiaG4(eu));
		Percepcao p = LerPercepcao(vidente, eu, eu.Pos, quemAtira: false);
		AfirmarPw("**EXTREMAMENTE FRACO**: a IA de um NPC le 5 -- e e com esses 5 que ele bate e apanha (o dano le o poder expresso)",
				  PertoPw(p.PoderDoAlvo, 5, 1e-9), $"{p.PoderDoAlvo:0.##}");
		AfirmarPw("RELAXAR NAO CUSTA KI: a regeneracao de Ki e a mesma de antes (a metade do `concealeddeBuff` e so de quem some por TECNICA)",
				  !eu.Ficha.isconcealed && PertoPw(eu.Ficha.KIregen, regenAntes, regenAntes * 0.001 + 1e-12), $"{regenAntes:0.#####} -> {eu.Ficha.KIregen:0.#####}");
		PoderEmUso.RelaxadoNaoEscondeDeTeste = true;
		try
		{
			PwTique(eu);
			AfirmarPw("(defeito injetado: relaxar nao esconde) as provas de cima REPROVAM -- o \"relaxado\" expressa o poder inteiro e esta no Sense e no scouter",
					  eu.Ficha.relaxado && eu.Ficha.expressedBP > 5 && Acha(SentidosDe(vidente).Lista, eu) != null && Acha(SentidosDe(olho).Lista, eu) != null,
					  $"expresso {eu.Ficha.expressedBP:0}");
		}
		finally { PoderEmUso.RelaxadoNaoEscondeDeTeste = false; }
		PwTique(eu);

		// ---- a forma nao desfaz o relaxado ----
		eu.Ficha.ssjBuff = 50;
		PwTique(eu);
		AfirmarPw("TRANSFORMAR NAO TIRA NINGUEM DO RELAXADO: com um multiplicador de forma de 50x o expresso continua 5 (a trava vem antes da forma na conta)",
				  eu.Ficha.relaxado && PertoPw(eu.Ficha.expressedBP, 5, 1e-9), $"{eu.Ficha.expressedBP:0.##}");
		eu.Ficha.ssjBuff = 1;
		PwTique(eu);

		// ---- a barra nao vale relaxado ----
		f = ApertarEOuvir(eu, "Power_Control:40");
		AfirmarPw("RELAXADO A BARRA NAO VALE: `Power_Control:40` e recusado e manda ficar pronto primeiro",
				  PertoPw(eu.Ficha.powerMod, 1, 1e-9) && eu.Ficha.relaxado && Disse(f, "fique pronto"), $"powerMod {eu.Ficha.powerMod} | {Ultimos(f)}");

		// ---- uma tecnica de sumir que acaba nao acorda o relaxado ----
		eu.Ficha.Ki = Math.Max(eu.Ficha.Ki, Jandirus.Core.Skills.Tecnicas.InvisKiMinimo + 10);
		Invisibilidade(eu);
		bool sumiu = EstaOculto(eu.Id);
		Invisibilidade(eu);
		PwTique(eu);
		AfirmarPw("**QUEM RELAXA CONTINUA RELAXADO**: uma invisibilidade que liga e desliga (ela apaga o `isconcealed` ao acabar) nao mostra o poder de quem relaxou -- relaxar e um campo proprio da ficha",
				  sumiu && !EstaOculto(eu.Id) && !eu.Ficha.isconcealed && eu.Ficha.relaxado && PertoPw(eu.Ficha.expressedBP, 5, 1e-9)
				  && Acha(SentidosDe(vidente).Lista, eu) == null,
				  $"sumiu {sumiu}; relaxado {eu.Ficha.relaxado}; expresso {eu.Ficha.expressedBP:0.##}");

		// ---- ficar pronto ----
		f = ApertarEOuvir(eu, "prontidao");
		AfirmarPw("apertar de novo: PRONTO -- o poder expresso volta inteiro na hora (`powerMod=1`, :52), e o Sense e o scouter o acham de novo",
				  !eu.Ficha.relaxado && PertoPw(eu.Ficha.powerMod, 1, 1e-9) && PoderEmUso.Estado(eu.Ficha) == EstadoDoPoder.PoderTotal && Disse(f, "pronto")
				  && PertoPw(eu.Ficha.expressedBP, cheio, Math.Max(1, cheio * 0.001))
				  && Acha(SentidosDe(vidente).Lista, eu) != null && Acha(SentidosDe(olho).Lista, eu) != null,
				  $"relaxado {eu.Ficha.relaxado}, expresso {eu.Ficha.expressedBP:0} | {Ultimos(f)}");

		eu.Ficha.powerMod = 0.4;
		AfirmarPw("(montagem) a 40% o estado e \"usando parte do poder\" (Using some power.)",
				  PoderEmUso.Estado(eu.Ficha) == EstadoDoPoder.Contendo && PoderEmUso.Porcento(eu.Ficha) == 40);
		ApertarEOuvir(eu, "prontidao");
		AfirmarPw("de QUALQUER ponto abaixo de 100% o verbo e \"ficar pronto\": de 40% ele volta a 100%, e nao relaxa (`if(powerMod >= 1) ... else`, :42-52)",
				  PertoPw(eu.Ficha.powerMod, 1, 1e-9) && !eu.Ficha.relaxado, $"powerMod {eu.Ficha.powerMod}, relaxado {eu.Ficha.relaxado}");

		// ---- 5, sempre: o forte relaxa igual ao fraco ----
		ServerPlayer forte = PwCorpo("ForteRelaxado", Em(c, 2, 0), 5_000_000, comAPorta: true);
		bool forteLido = Acha(SentidosDe(olho).Lista, forte) != null;
		ApertarEOuvir(forte, "prontidao");
		PwTique(forte);
		AfirmarPw("**5, SEMPRE**: um corpo de 5 milhoes relaxado expressa os mesmos 5 e some do scouter (no jogo de antes relaxar era 1% do poder -- 50 mil, a vista de todos)",
				  forteLido && PertoPw(forte.Ficha.expressedBP, 5, 1e-9) && Acha(SentidosDe(olho).Lista, forte) == null,
				  $"lido antes {forteLido}; expresso {forte.Ficha.expressedBP:0.##}");
		ApertarEOuvir(forte, "prontidao");

		// ---- a raiva acorda ----
		eu.RaivaComumAte = NowMs() + 60_000;   // a janela da raiva comum (um amigo nocauteado)
		ProjetarRaiva(eu);
		AfirmarPw("(montagem) com a janela de raiva aberta, a raiva passa do limiar de `((MaxAnger-100)/1.66)+100` (:91)",
				  eu.Ficha.Anger > PoderEmUso.RaivaQueAcorda(eu.Ficha), $"raiva {eu.Ficha.Anger:0.#} contra {PoderEmUso.RaivaQueAcorda(eu.Ficha):0.#}");
		f = ApertarEOuvir(eu, "prontidao");
		AfirmarPw("**FURIOSO NAO SE RELAXA** (`RelaxCheck`, :90-95): o poder volta inteiro no mesmo aperto, com as duas frases do original",
				  !eu.Ficha.relaxado && PertoPw(eu.Ficha.powerMod, 1, 1e-9) && eu.Ficha.expressedBP > 5 && Disse(f, "furioso") && Disse(f, "briga"),
				  $"relaxado {eu.Ficha.relaxado}, expresso {eu.Ficha.expressedBP:0} | {Ultimos(f)}");
		PoderEmUso.RaivaDeixaRelaxarDeTeste = true;
		try
		{
			ApertarEOuvir(eu, "prontidao");
			AfirmarPw("(defeito injetado: a raiva deixa relaxar) a prova de cima REPROVA -- furioso, o corpo fica relaxado",
					  eu.Ficha.relaxado, $"relaxado {eu.Ficha.relaxado}");
		}
		finally { PoderEmUso.RaivaDeixaRelaxarDeTeste = false; eu.Ficha.relaxado = false; eu.Ficha.powerMod = 1; }

		eu.RaivaComumAte = 0;
		ProjetarRaiva(eu);
		ApertarEOuvir(eu, "prontidao");
		bool relaxouCalmo = eu.Ficha.relaxado;
		eu.RaivaComumAte = NowMs() + 60_000;
		ProjetarRaiva(eu);
		// (a escuta ligada e o que faz o tique considerar um corpo SEM TELA: em jogo ele so olha jogador -- NPC nao relaxa)
		List<(int, NetDataWriter)>? escutaDeAntes = EscutaDoPoderEmUso;
		EscutaDoPoderEmUso = [];
		List<string> noTique;
		try { noTique = Ouvir(() => { for (int i = 0; i < TicksEntreFatos; i++) TickDoPoderEmUso(); }); }
		finally { EscutaDoPoderEmUso = escutaDeAntes; }
		AfirmarPw("...e a raiva que chega DEPOIS acorda quem ja estava relaxado, pelo tique (5 Hz)",
				  relaxouCalmo && !eu.Ficha.relaxado && PertoPw(eu.Ficha.powerMod, 1, 1e-9) && Disse(noTique, "furioso"),
				  $"relaxou calmo {relaxouCalmo}; relaxado {eu.Ficha.relaxado} | {Ultimos(noTique)}");
		eu.RaivaComumAte = 0;
		ProjetarRaiva(eu);
		PwTique(eu);

		// ---- reunir energia acorda ----
		eu.Ficha.maxstamina = Math.Max(eu.Ficha.maxstamina, 100_000);   // a medida e o acordar e a subida, e nao o folego
		eu.Ficha.stamina = eu.Ficha.maxstamina;
		eu.Ficha.Ki = eu.Ficha.MaxKi;
		PwTique(eu);
		double cheioAgora = eu.Ficha.expressedBP;
		ApertarEOuvir(eu, "prontidao");
		bool relaxouPraCarga = eu.Ficha.relaxado;
		List<string> naCarga = Ouvir(() => Carregar(eu, true));
		AfirmarPw("**REUNIR ENERGIA ACORDA QUEM RELAXA**: ao segurar C o corpo deixa de estar relaxado, volta a aparecer, e a barra comeca do piso de 1%",
				  relaxouPraCarga && eu.Carregando && !eu.Ficha.relaxado && PertoPw(eu.Ficha.powerMod, PoderEmUso.Piso, 1e-9) && Disse(naCarga, "deixa de relaxar")
				  && PertoPw(eu.Ficha.expressedBP, cheioAgora * 0.01, Math.Max(1, cheioAgora * 0.002)),
				  $"relaxou {relaxouPraCarga}; relaxado {eu.Ficha.relaxado}; powerMod {eu.Ficha.powerMod}; expresso {eu.Ficha.expressedBP:0.##} de {cheioAgora:0} | {Ultimos(naCarga)}");
		for (int i = 0; i < 10; i++) TickDaCarga(eu, dt);
		AfirmarPw("...e dali a carga SOBE o poder aos poucos (o estagio `Retomando`): dez tiques depois a barra esta acima de 1% e abaixo de 100%",
				  eu.EstagioDaCarga == EstagioDaCarga.Retomando && eu.Ficha.powerMod > PoderEmUso.Piso + 1e-6 && eu.Ficha.powerMod < 1,
				  $"estagio {eu.EstagioDaCarga}; powerMod {eu.Ficha.powerMod:0.####}");
		PararCarga(eu);
		eu.Ficha.powerMod = 1;
		eu.Ficha.Ki = eu.Ficha.MaxKi;
		PwTique(eu);
		PoderEmUso.CargaNaoAcordaDeTeste = true;
		try
		{
			ApertarEOuvir(eu, "prontidao");
			Carregar(eu, true);
			for (int i = 0; i < 30; i++) TickDaCarga(eu, dt);
			AfirmarPw("(defeito injetado: a carga nao acorda) a prova de cima REPROVA -- o corpo passa dos 100% de Ki relaxado, com 5 de poder e fora do scouter",
					  eu.Ficha.relaxado && eu.Ficha.Ki > eu.Ficha.MaxKi && Acha(SentidosDe(olho).Lista, eu) == null,
					  $"relaxado {eu.Ficha.relaxado}; Ki {eu.Ficha.Ki / eu.Ficha.MaxKi * 100:0.##}%");
			PararCarga(eu);
		}
		finally { PoderEmUso.CargaNaoAcordaDeTeste = false; eu.Ficha.relaxado = false; eu.Ficha.powerMod = 1; eu.Ficha.Ki = eu.Ficha.MaxKi; }
		PwTique(eu);

		// ---- o que continua sendo de quem some por TECNICA (`isconcealed`, `master.dm:136-141`) ----
		regenAntes = eu.Ficha.KIregen;
		eu.Ficha.isconcealed = true;   // (o campo que a invisibilidade, o Sneak e o embate escrevem; posto a mao pra medir so ele)
		PwTique(eu);
		AfirmarPw("SUMIR POR TECNICA continua como no DM: o expresso em 5, o estado \"escondendo o poder\" (Concealing Power) e a regeneracao de Ki pela metade (`concealeddeBuff=0.5`, master.dm:137 e :184)",
				  PertoPw(eu.Ficha.expressedBP, 5, 1e-9) && PoderEmUso.Estado(eu.Ficha) == EstadoDoPoder.Escondendo && !eu.Ficha.relaxado
				  && PertoPw(eu.Ficha.KIregen, regenAntes * 0.5, regenAntes * 0.001 + 1e-12), $"{regenAntes:0.#####} -> {eu.Ficha.KIregen:0.#####}");
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
		AfirmarPw("...e o corpo gasta menos vigor parado -- o dreno passivo dividido por 1,5 (`concealedBuff`, master.dm:138, StaminaDrain.dm:30)",
				  escondido > 0 && PertoPw(semPoupar / escondido, PoderEmUso.FolgaDeVigorDoSigilo, 1e-6), $"{semPoupar:0.######} / {escondido:0.######} = {semPoupar / Math.Max(escondido, 1e-12):0.###}");
		AfirmarPw("(defeito injetado: o `concealedBuff` calculado e jogado fora) a prova de cima REPROVA -- o gasto e o mesmo de quem esta a vista",
				  !PertoPw(semPoupar, escondido, escondido * 0.01), $"{semPoupar:0.######} vs {escondido:0.######}");
		eu.Ficha.stamina = eu.Ficha.maxstamina;
		eu.Ficha.isconcealed = false;
		PwTique(eu);
	}

	// =====================================================================
	// 5) ESCONDIDO NAO ENTREGA O BP DE NINGUEM
	// =====================================================================
	private void PwEscondidoNaoEntregaBp()
	{
		GD.Print("[poder] -- 5) A REGRA PRINCIPAL DO BP: relaxado (expresso = 5, uma constante), nenhuma leitura relativa vira numero");
		Vec2 c = CorredorLivre(28);
		ServerPlayer espiao = PwCorpo("Espiao", c, 20_000, comAPorta: true, sense: true);
		ServerPlayer alvo = PwCorpo("Medido", Em(c, 3, 0), 37_000, comAPorta: false);
		double doAlvo = alvo.Ficha.expressedBP;

		float aVista = Acha(SentidosDe(espiao).Lista, alvo)?.PoderRelativo ?? float.NaN;
		AfirmarPw("(montagem) a vista, o Sense do espiao le o alvo em porcentagem do proprio poder EXPRESSO (HtmlUI.dm:375)",
				  PertoPw(aVista, Math.Round(doAlvo / espiao.Ficha.expressedBP * 100, MidpointRounding.AwayFromZero), 1e-6), $"{aVista}%");

		ApertarEOuvir(espiao, "prontidao");
		PwTique(espiao);
		float escondido = Acha(SentidosDe(espiao).Lista, alvo)?.PoderRelativo ?? float.NaN;
		double vazaria = Math.Round(doAlvo / 5 * 100, MidpointRounding.AwayFromZero);
		AfirmarPw("**RELAXADO, O SENSE NAO VIRA SCOUTER**: a leitura NAO e `expresso do outro / 5` (de onde o BP do outro sairia por uma conta de cabeca)",
				  espiao.Ficha.relaxado && PoderEmUso.Escondido(espiao.Ficha) && !float.IsNaN(escondido) && !PertoPw(escondido, vazaria, 1) && !PertoPw(escondido * 5 / 100, doAlvo, 1),
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
		AfirmarPw("**RELAXADO E SEM SCOUTER, O MULTIPLICADOR TOTAL NAO VIAJA** (vai zero): inteiro ele e `5 / BP`, e o BP base sairia por uma divisao",
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

		ApertarEOuvir(espiao, "prontidao");
		AfirmarPw("pronto de novo, o multiplicador volta a viajar (ele so e cortado enquanto o expresso e a constante)",
				  !espiao.Ficha.relaxado && FichaVisivel(espiao).MultTotal > 0.5);

		PoderEmUsoState retrato = PoderEmUsoDe(espiao);
		NetDataWriter w = Protocol.Begin(Protocol.S2C.PoderEmUso);
		retrato.Write(w);
		AfirmarPw("e o retrato do proprio estado tem DOIS bytes (a porcentagem do proprio poder em uso e os bits): nao cabe poder de ninguem",
				  w.Length == 3, $"{w.Length} bytes com o opcode");
	}

	// =====================================================================
	// 6) AS VALIDACOES
	// =====================================================================
	private void PwAsValidacoes()
	{
		GD.Print("[poder] -- 6) AS VALIDACOES: caido nao mexe no proprio poder, e o canal e fechado pra quem perdeu as redeas");
		Vec2 c = CorredorLivre(20);
		ServerPlayer eu = PwCorpo("Caido", c, 10_000, comAPorta: true);

		eu.Ficha.KO = true;
		List<string> f1 = ApertarEOuvir(eu, "Power_Control:40");
		List<string> f3 = ApertarEOuvir(eu, "prontidao");
		AfirmarPw("NOCAUTEADO: baixar e relaxar sao recusados, e nada muda",
				  PertoPw(eu.Ficha.powerMod, 1, 1e-9) && !eu.Ficha.relaxado && Disse(f1, "caído") && Disse(f3, "caído"),
				  $"powerMod {eu.Ficha.powerMod}, relaxado {eu.Ficha.relaxado} | {Ultimos(f1)} | {Ultimos(f3)}");
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
				  cadaver && PertoPw(eu.Ficha.powerMod, 1, 1e-9) && !eu.Ficha.relaxado && Disse(f1, "sem vida") && Disse(f3, "sem vida"), $"{Ultimos(f1)} | {Ultimos(f3)}");
		eu.Ficha.dead = false;

		AfirmarPw("POSSUIDO: os dois verbos viajam no canal de habilidade, que o despacho fecha pra quem perdeu as redeas (`ComandoDeCorpo`)",
				  ComandoDeCorpo(Protocol.C2S.Habilidade));
		f1 = ApertarEOuvir(eu, "Power_Control:40");
		AfirmarPw("(o caminho legitimo continua aberto: de pe, o mesmo corpo baixa pra 40%)", PertoPw(eu.Ficha.powerMod, 0.4, 1e-9), Ultimos(f1));
	}

	// =====================================================================
	// 7) O RETRATO NO FIO
	// =====================================================================
	private void PwORetratoNoFio()
	{
		GD.Print("[poder] -- 7) O RETRATO NO FIO: o que a tela de quem joga recebe, e quando");
		Vec2 c = CorredorLivre(20);
		ServerPlayer eu = PwCorpo("Retratado", c, 10_000, comAPorta: true);
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
		AfirmarPw("**BAIXAR MANDA O RETRATO NA HORA**: 40%, sem esperar o tique, e sem bit nenhum (a barra nao esconde)",
				  m.Count == 2 && m[^1].Estado.Porcento == 40 && !m[^1].Estado.Relaxado && !m[^1].Estado.Escondido, $"{m.Count} pacote(s); ultimo {(m.Count > 0 ? m[^1].Estado.ToString() : "-")}");
		UmQuintoDeSegundo();
		AfirmarPw("...e o tique seguinte nao o repete", Meus().Count == 2, $"{Meus().Count}");

		ApertarEOuvir(eu, "prontidao");   // de 40%: fica pronto
		ApertarEOuvir(eu, "prontidao");   // de 100%: relaxa
		m = Meus();
		AfirmarPw("ficar pronto (100%, nenhum bit) e relaxar (os dois bits: relaxado e escondido) mandam um retrato cada",
				  m.Count == 4 && m[^2].Estado == PoderEmUsoState.Cheio && m[^1].Estado.Relaxado && m[^1].Estado.Escondido && m[^1].Estado.Porcento == 100,
				  $"{m.Count} pacote(s); ultimo {(m.Count > 0 ? m[^1].Estado.ToString() : "-")}");

		RetratoDoPoderCongeladoDeTeste = true;
		try
		{
			ApertarEOuvir(eu, "prontidao");   // fica pronto
			UmQuintoDeSegundo();
			AfirmarPw("(defeito injetado: o retrato congelado) a prova de cima REPROVA -- o corpo ficou pronto e a tela continua lendo \"relaxado\"",
					  Meus().Count == 4 && !eu.Ficha.relaxado, $"{Meus().Count} pacote(s); relaxado {eu.Ficha.relaxado}");
		}
		finally { RetratoDoPoderCongeladoDeTeste = false; }
		UmQuintoDeSegundo();
		AfirmarPw("(desligado o defeito, o tique corrige a tela)", Meus().Count == 5 && Meus()[^1].Estado == PoderEmUsoState.Cheio, $"{Meus().Count}");

		eu.Ficha.isconcealed = true;   // (o campo de quem some por tecnica)
		UmQuintoDeSegundo();
		m = Meus();
		AfirmarPw("sumir por TECNICA manda o bit de escondido SEM o de relaxado (a tela escreve \"PODER ESCONDIDO\", e nao \"RELAXADO\")",
				  m.Count == 6 && m[^1].Estado.Escondido && !m[^1].Estado.Relaxado, $"{m.Count} pacote(s); ultimo {(m.Count > 0 ? m[^1].Estado.ToString() : "-")}");
		eu.Ficha.isconcealed = false;
		EscutaDoPoderEmUso = null;
	}

	// =====================================================================
	// 8) O LOGIN, E A FIACAO
	// =====================================================================
	private void PwOLoginEAFiacao()
	{
		GD.Print("[poder] -- 8) O LOGIN: o poder contido nao vem do disco; e a fiacao no servidor de verdade");
		Vec2 c = CorredorLivre(20);
		ServerPlayer eu = PwCorpo("Voltando", c, 10_000, comAPorta: true);

		eu.Ficha.powerMod = 0.01;
		eu.Ficha.relaxado = true;
		AcordarComOPoderInteiro(eu);
		AfirmarPw("**QUEM SAIU DO JOGO RELAXADO (OU A 1%) ENTRA PRONTO, COM O PODER INTEIRO** (`powerMod` e `tmp` no DM, base.dm:37-38; aqui a ficha vai inteira pro disco)",
				  PertoPw(eu.Ficha.powerMod, 1, 1e-9) && !eu.Ficha.relaxado, $"powerMod {eu.Ficha.powerMod}, relaxado {eu.Ficha.relaxado}");
		PoderVemDoDiscoDeTeste = true;
		try
		{
			eu.Ficha.powerMod = 0.01;
			eu.Ficha.relaxado = true;
			AcordarComOPoderInteiro(eu);
			AfirmarPw("(defeito injetado: o poder vem do disco) a prova de cima REPROVA -- ele entra relaxado e a 1%, sem nada na tela que diga",
					  PertoPw(eu.Ficha.powerMod, 0.01, 1e-9) && eu.Ficha.relaxado, $"powerMod {eu.Ficha.powerMod}, relaxado {eu.Ficha.relaxado}");
		}
		finally { PoderVemDoDiscoDeTeste = false; eu.Ficha.powerMod = 1; eu.Ficha.relaxado = false; }
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
		AfirmarPw("FIACAO: a tecla C chama `AcordarPelaCarga(pl)` (`GameServer.Carga.cs`)",
				  Chama(Fonte("Server/GameServer.Carga.cs"), "AcordarPelaCarga(pl)"));
		AfirmarPw("FIACAO: quem sai do jogo leva junto o retrato que a tela dele recebeu (`EsquecerG9` chama `EsquecerOPoderEmUso`)",
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
