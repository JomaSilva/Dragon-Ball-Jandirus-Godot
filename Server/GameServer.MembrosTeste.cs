using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--membrosteste` -- O QUE UM MEMBRO PERDIDO CUSTA, MEDIDO NO PONTO DE PRODUCAO.
///
/// O dono (playtest de 2026-10-10): *"ser desmembrado n tava dando debuffs reais como eram o DM, da uma olhada
/// la e faca os debuffs de la serem aplicados aqui"*. As regras sao as do commit `601a30b` do DM ("limb loss
/// effects") mais a lentidao do ferido que ja morava em `movement handler.dm:55`; a tabela inteira esta no
/// cabecalho de <see cref="DebuffsDeMembro"/>.
///
///     Godot --headless --path . --server --port 8310 --membrosteste
///
/// ============================ O QUE CADA FAMILIA MEDE, E POR ONDE ============================
///  1. A TABELA PURA: as cinco funcoes do Core contra os numeros LITERAIS do DM (0,75 / 0,50 / piso 0,10 /
///     meio ritmo / uma perna / duas pernas / HP sobre cem / Androide e Majin).
///  2. BRACO PERDIDO ERRA MAIS: milhares de golpes pelo `MeleeResolver.Resolver` (o mesmo que o `Atacar`
///     chama) com a pontaria base em 80% -- a fracao que acerta cai pra 60% e pra 40%.
///  3. SEM OS DOIS BRACOS O SOCO DEMORA: o `Atacar` de producao escreve a recarga e o `CombatState.Tick` a
///     escorre -- conta-se quanto tempo ate o corpo poder socar de novo, e o `SocoMs` que vai pro cliente.
///  4. SEM UMA PERNA NAO CORRE: pacotes de input pelo `AplicarInput` (o que o `Input` do fio chama) pedindo
///     corrida -- mede-se o DESLOCAMENTO e o Ki cobrado.
///  5. SEM AS DUAS SO VOANDO: os mesmos pacotes, a pe (zero pixel) e no ar (anda).
///  6. FERIDO ANDA DEVAGAR: o `RecalcularVelocidade` de producao e o deslocamento que ele paga.
///  7. A FRASE NO CHAT no instante em que o membro cai (`CombatState.Arrancar`, o `LopLimb`).
///  8. OS TEXTOS DA ABA BODY dizem o que as familias 2-6 mediram.
///  9. QUEM A IA DIRIGE NAO TRAVA, o bicho nao perde membro, e o corpo sem membro nenhum nao divide por zero.
/// 10. O DEFEITO INJETADO (`DebuffsDeMembro.DesligadosDeTeste`): com ele as cinco reguas de producao tem que
///     voltar ao valor do corpo inteiro -- e o estado do port de antes, o que o dono sentiu no playtest.
/// 11. SSJ4 SEM RABO SE DESFAZ (`supersaiyanbuff.dm:135-162`), pelo `TickDaForma` -- e o corpo que nunca teve
///     rabo nao e derrubado por uma regra que nao e dele.
/// 12. SEM BRACO NEM PERNA INTEIRO NAO SE ERGUE A GUARDA (o `holdblock()`, `CombatMovement.dm:77-79`), pela
///     porta do `C2S.Guard`.
///
/// EM CADA FAMILIA, "curou -> o debuff some" e medido pela mesma regua, por um caminho de cura de producao
/// diferente: `Body.Regenerar` (a primitiva do tanque e da raca), o verb `regenerar` do Namekuseijin, o
/// `Reviver` (morte e retorno), o `Curar` (Heal). E o avesso: curar a VIDA sem devolver o membro nao tira nada.
/// ===========================================================================================
/// </summary>
public sealed partial class GameServer
{
	private int _mbOk, _mbFalhou;

	private void AfirmarMb(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _mbOk++; GD.Print($"[membros]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _mbFalhou++; GD.PrintErr($"[membros]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDosMembros()
	{
		_mbOk = _mbFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[membros] ================ O QUE UM MEMBRO PERDIDO CUSTA (pedido do dono, playtest de 2026-10-10) ================");
		List<string>? escutaDeAntes = EscutaDeAvisos;
		try
		{
			AfirmarMb("PRECONDICAO: o mapa da zona da bancada esta carregado (os corredores saem dele)", _pjMapa != null);
			MbATabelaPura();
			MbBracoPerdidoErraMais();
			MbSemOsDoisBracosOSocoDemora();
			MbSemUmaPernaNaoCorre();
			MbSemAsDuasPernasSoVoando();
			MbFeridoAndaDevagar();
			MbAFraseDaPerda();
			MbOsTextosDizemOQueSeAplica();
			MbQuemAIaDirigeNaoTrava();
			MbODefeitoInjetado();
			MbSsj4SemRabo();
			MbSemMembroNaoHaGuarda();
		}
		catch (Exception e)
		{
			_mbFalhou++;
			GD.PrintErr($"[membros]   FALHA a bancada estourou no meio: {e}");
		}
		finally
		{
			DebuffsDeMembro.DesligadosDeTeste = false;
			EscutaDeAvisos = escutaDeAntes;
			LimparTudoDaBancada();
			GD.Print($"[membros] ================ {_mbOk} OK, {_mbFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// OS CORPOS E AS REGUAS
	// =====================================================================
	/// <summary>Um corpo de bancada num corredor seco, olhando pro leste, com o Ki cheio.</summary>
	private ServerPlayer MbCorpo(string nome, int tiles = 16)
	{
		ServerPlayer pl = Forjar(nome, CorredorSeco(tiles), 5_000);
		pl.Facing = Facing.East;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		return pl;
	}

	/// <summary>
	/// ARRANCA pelo `LopLimb` de producao (<see cref="CombatState.Arrancar"/>): a cascata (o braco leva a mao), os
	/// 20% de Ki e o gancho `AoDecepar` rodam como numa luta. Devolve o Ki cheio depois -- o que se mede e o membro.
	/// </summary>
	private static void MbArrancar(ServerPlayer pl, params string[] membros)
	{
		foreach (string nome in membros)
			if (pl.Combate.Corpo.Achar(nome) is { } m) pl.Combate.Arrancar(m);
		pl.Combate.SincronizarVida();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
	}

	/// <summary>
	/// A FRACAO DE GOLPES QUE ACERTA em <paramref name="n"/> resolucoes de producao. Pontaria base de 80% (tecnica
	/// 4 contra velocidade 5, poderes iguais), alvo inteiro e de pe a cada golpe -- nocauteado ele nao esquiva nunca.
	/// </summary>
	private static double MbTaxaDeAcerto(ServerPlayer a, ServerPlayer d, int n, int semente)
	{
		var rng = new Random(semente);
		a.Ficha.Etechnique = 4;
		d.Ficha.Espeed = 5;
		a.Ficha.expressedBP = d.Ficha.expressedBP = 5_000;
		a.Combate.Precisao = 0;
		d.Combate.Deflexao = 0;
		d.Combate.ChanceEsquiva = 0;
		int acertos = 0;
		for (int i = 0; i < n; i++)
		{
			d.Combate.Curar();
			d.Combate.Guardar(false);
			GolpeResultado r = MeleeResolver.Resolver(a.Combate, d.Combate, 0, rng);
			if (r.Desfecho != Desfecho.Esquivou) acertos++;
		}
		d.Combate.Curar();
		return (double)acertos / n;
	}

	/// <summary>
	/// QUANTO TEMPO ATE PODER SOCAR DE NOVO. O soco e no ar e sai pelo <see cref="Atacar"/> de producao (e ele que
	/// escreve a recarga); o tempo escorre pelo <see cref="CombatState.Tick"/>, que e o que o `TickCombate` chama.
	/// </summary>
	private (double Segundos, int SocoMs) MbEsperaDoSoco(ServerPlayer a)
	{
		a.Combate.Recarga = 0;
		a.Combate.Stun = 0;
		a.Ficha.Ki = a.Ficha.MaxKi;
		Atacar(a, Protocol.Golpe.Leve);
		const double dt = 1.0 / 30;
		double t = 0;
		for (int i = 0; i < 900 && !a.Combate.PodeAtacar(); i++) { a.Combate.Tick(dt); t += dt; }
		return (t, a.Sheet().SocoMs);
	}

	/// <summary>
	/// DEZ PACOTES DE INPUT, um a cada 100 ms, cada um pedindo o passo CHEIO da marcha pedida a partir de onde o
	/// servidor diz que o corpo esta. Devolve quanto o corpo andou de verdade -- e o poe de volta onde estava (o
	/// corredor da bancada e finito: a primeira rodada andou 38 tiles num corredor de 30 e parou na pedra).
	/// </summary>
	private float MbAndar(ServerPlayer pl, bool correndo)
	{
		byte flags = (byte)((byte)Facing.East | Protocol.InputAndando | (correndo ? Protocol.InputCorrendo : 0));
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.OrcamentoPx = 0;
		pl.CorrecaoEsperadaAte = 0;
		Vec2 partida = pl.Pos;
		uint seq = pl.SeqInput;
		float porPacote = MoveRules.SpeedPx(pl.SpeedStat, correndo) * 0.1f;
		for (int i = 0; i < 10; i++) PacoteEmVoo(pl, ++seq, pl.Pos + new Vec2(porPacote, 0), flags);
		float andou = pl.Pos.X - partida.X;
		pl.Pos = partida;
		pl.OrcamentoPx = 0;
		return andou;
	}

	private static bool MbPerto(double a, double b, double folga) => Math.Abs(a - b) <= folga;

	/// <summary>A mesma pergunta com o corpo DEVOLVIDO AO DONO (sem cerebro) por um instante -- o controle do "a IA nao trava".</summary>
	private static bool MbDoDono(ServerPlayer npc, Func<bool> pergunta)
	{
		Jandirus.Core.Ai.Cerebro? cerebro = npc.Cerebro;
		npc.Cerebro = null;
		try { return pergunta(); }
		finally { npc.Cerebro = cerebro; }
	}

	// =====================================================================
	// 1) A TABELA PURA
	// =====================================================================
	private void MbATabelaPura()
	{
		GD.Print("[membros] --- 1) a tabela do DM, funcao por funcao (numeros LITERAIS do original) ---");
		var inteiro = new DebuffsDeMembro.Perdas(0, 0, 0);
		var umBraco = new DebuffsDeMembro.Perdas(1, 0, 1);
		var doisBracos = new DebuffsDeMembro.Perdas(2, 0, 2);
		var umaPerna = new DebuffsDeMembro.Perdas(0, 1, 1);
		var duasPernas = new DebuffsDeMembro.Perdas(0, 2, 2);

		AfirmarMb("pontaria: inteiro 1,00 | um braco 0,75 | os dois 0,50 (`CombatMovement.dm:189`)",
			DebuffsDeMembro.FatorDePontaria(inteiro) == 1 && MbPerto(DebuffsDeMembro.FatorDePontaria(umBraco), 0.75, 1e-9)
			&& MbPerto(DebuffsDeMembro.FatorDePontaria(doisBracos), 0.50, 1e-9),
			$"{DebuffsDeMembro.FatorDePontaria(inteiro)} | {DebuffsDeMembro.FatorDePontaria(umBraco)} | {DebuffsDeMembro.FatorDePontaria(doisBracos)}");
		AfirmarMb("...e o piso de 0,10 do `max(..., 0.1)` (so um corpo de quatro bracos chegaria nele)",
			MbPerto(DebuffsDeMembro.FatorDePontaria(new DebuffsDeMembro.Perdas(4, 0, 4)), 0.10, 1e-9));
		AfirmarMb("recarga: so com os DOIS bracos perdidos ela escorre a meio ritmo (`movement handler.dm:167`)",
			DebuffsDeMembro.RitmoDaRecarga(inteiro) == 1 && DebuffsDeMembro.RitmoDaRecarga(umBraco) == 1
			&& MbPerto(DebuffsDeMembro.RitmoDaRecarga(doisBracos), 0.5, 1e-9));
		AfirmarMb("corrida: UMA perna perdida ja a tira (`speedy.dm:36`); bracos nao tem nada com isso",
			DebuffsDeMembro.Corre(inteiro) && DebuffsDeMembro.Corre(doisBracos)
			&& !DebuffsDeMembro.Corre(umaPerna) && !DebuffsDeMembro.Corre(duasPernas));
		AfirmarMb("chao: so sem as DUAS pernas o corpo nao sai do lugar a pe (`movement handler.dm:133`)",
			DebuffsDeMembro.AndaNoChao(inteiro) && DebuffsDeMembro.AndaNoChao(umaPerna) && !DebuffsDeMembro.AndaNoChao(duasPernas));
		AfirmarMb("passo do ferido: HP sobre cem (`movement handler.dm:55`) -- 100 -> 1,0 | 50 -> 0,5 | 20 -> 0,2",
			DebuffsDeMembro.FatorDoPasso(100, "Human") == 1 && MbPerto(DebuffsDeMembro.FatorDoPasso(50, "Human"), 0.5, 1e-9)
			&& MbPerto(DebuffsDeMembro.FatorDoPasso(20, "Saiyan"), 0.2, 1e-9));
		AfirmarMb("...menos o Androide e o Majin (`undelayed = 1`, `statandroid.dm:2` e `statmajin.dm:14`), e HP zero nao freia (`if(HP>0...)`)",
			DebuffsDeMembro.FatorDoPasso(50, "Android") == 1 && DebuffsDeMembro.FatorDoPasso(50, "Majin") == 1
			&& DebuffsDeMembro.FatorDoPasso(0, "Human") == 1 && DebuffsDeMembro.FatorDoPasso(50, "BioAndroid") < 1);

		// A CONTA DO CORPO DE VERDADE: o braco leva a mao, e a mao nao conta em dobro.
		LimparTudoDaBancada();
		ServerPlayer c = MbCorpo("mbConta");
		MbArrancar(c, "Braco direito");
		DebuffsDeMembro.Perdas p = DebuffsDeMembro.De(c.Combate.Corpo);
		AfirmarMb("um braco arrancado pelo `LopLimb` conta UM braco (a mao foi junto e nao conta em dobro, `Injuries.dm:229`)",
			p is { Bracos: 1, Pernas: 0, ForaDaGuarda: 1 } && c.Combate.Corpo.Achar("Mao direita") is { Decepado: true }, $"{p}");
		if (c.Combate.Corpo.Achar("Perna esquerda") is { } quebrada) quebrada.Vida = quebrada.VidaMax * 0.10;
		p = DebuffsDeMembro.De(c.Combate.Corpo);
		AfirmarMb("perna QUEBRADA (10%) sai da guarda mas nao e perna perdida: continua correndo e andando",
			p is { Bracos: 1, Pernas: 0, ForaDaGuarda: 2 } && DebuffsDeMembro.Corre(p) && DebuffsDeMembro.AndaNoChao(p), $"{p}");

		// A MESMA CONTA PELO LADO DO CLIENTE (nome + vida em porcento + arrancado, como chega no `S2C.Corpo`).
		var doFio = new DebuffsDeMembro.Perdas(0, 0, 0);
		foreach (BodyPart m in c.Combate.Corpo.Partes)
			doFio = DebuffsDeMembro.Somar(doFio, m.Nome, Math.Round(m.Fracao * 100), m.Decepado);
		AfirmarMb("a conta montada so com o que vai no fio (nome, vida %, arrancado) e a MESMA do servidor", doFio == p, $"fio {doFio} x corpo {p}");
	}

	// =====================================================================
	// 2) BRACO PERDIDO ERRA MAIS
	// =====================================================================
	private void MbBracoPerdidoErraMais()
	{
		GD.Print("[membros] --- 2) BRACO PERDIDO ERRA MAIS: 4000 golpes pelo `MeleeResolver.Resolver`, pontaria base de 80% ---");
		LimparTudoDaBancada();
		const int N = 4000;
		const double Folga = 0.03;
		ServerPlayer a = MbCorpo("mbBate");
		ServerPlayer d = Forjar("mbLeva", a.Pos + new Vec2(ZoneCollision.TileSize, 0), 5_000);

		double inteiro = MbTaxaDeAcerto(a, d, N, 11);
		AfirmarMb("CONTROLE: com os dois bracos, 80% dos golpes acertam", MbPerto(inteiro, 0.80, Folga), $"{inteiro:P1}");

		MbArrancar(a, "Braco direito");
		double um = MbTaxaDeAcerto(a, d, N, 12);
		AfirmarMb("sem UM braco a pontaria cai 25%: 60% dos golpes acertam (0,80 x 0,75)", MbPerto(um, 0.80 * 0.75, Folga), $"{um:P1} (inteiro {inteiro:P1})");

		MbArrancar(a, "Braco esquerdo");
		double dois = MbTaxaDeAcerto(a, d, N, 13);
		AfirmarMb("sem os DOIS cai 50%: 40% dos golpes acertam (0,80 x 0,50)", MbPerto(dois, 0.80 * 0.50, Folga), $"{dois:P1}");

		// O EXPOSTO DO NOCAUTE: alvo caido leva tudo, com ou sem braco de quem bate (`if(M.KO) hit = 2`, `CombatMovement.dm:211`).
		var rng = new Random(14);
		int noChao = 0;
		for (int i = 0; i < 200; i++)
		{
			d.Combate.Curar();
			d.Ficha.KO = true;
			if (MeleeResolver.Resolver(a.Combate, d.Combate, 0, rng).Desfecho != Desfecho.Esquivou) noChao++;
		}
		d.Combate.Curar();
		AfirmarMb("...mas o alvo NOCAUTEADO continua exposto: 200 de 200 golpes entram, mesmo sem braco nenhum", noChao == 200, $"{noChao}/200");

		// CURAR A VIDA NAO DEVOLVE O BRACO: o debuff fica.
		a.Combate.Corpo.Curar(1000);
		a.Combate.SincronizarVida();
		double curadoSemBraco = MbTaxaDeAcerto(a, d, N, 15);
		AfirmarMb("curar a VIDA dos membros que sobraram nao tira o debuff (o braco continua no chao): segue em 40%",
			MbPerto(curadoSemBraco, 0.40, Folga), $"{curadoSemBraco:P1}, vida {a.Ficha.HP:0}%");

		// O MEMBRO QUE VOLTA TIRA O DEBUFF -- pela primitiva do tanque e da regeneracao de raca.
		a.Combate.Corpo.Regenerar(a.Combate.Corpo.Achar("Braco direito")!);
		double umDeVolta = MbTaxaDeAcerto(a, d, N, 16);
		AfirmarMb("`Body.Regenerar` devolve UM braco (a primitiva do tanque e da raca): a pontaria volta pra 60%", MbPerto(umDeVolta, 0.60, Folga), $"{umDeVolta:P1}");
		a.Combate.Corpo.Regenerar(a.Combate.Corpo.Achar("Braco esquerdo")!);
		double deVolta = MbTaxaDeAcerto(a, d, N, 17);
		AfirmarMb("...e com os dois de volta ela e a do corpo inteiro (80%)", MbPerto(deVolta, 0.80, Folga), $"{deVolta:P1}");
	}

	// =====================================================================
	// 3) SEM OS DOIS BRACOS O SOCO DEMORA
	// =====================================================================
	private void MbSemOsDoisBracosOSocoDemora()
	{
		GD.Print("[membros] --- 3) SEM OS DOIS BRACOS O SOCO DEMORA O DOBRO: `Atacar` escreve a recarga, `CombatState.Tick` a escorre ---");
		LimparTudoDaBancada();
		ServerPlayer a = MbCorpo("mbRitmo");
		(double t0, int ms0) = MbEsperaDoSoco(a);
		AfirmarMb("CONTROLE: o corpo inteiro espera a cadencia do soco (e o `SocoMs` da ficha diz o mesmo)",
			t0 > 0.1 && MbPerto(t0 * 1000, ms0, 40), $"{t0 * 1000:0} ms medidos, SocoMs {ms0}");

		MbArrancar(a, "Braco direito");
		(double t1, int ms1) = MbEsperaDoSoco(a);
		AfirmarMb("sem UM braco a recarga nao muda (`armsLost>=2`)", MbPerto(t1, t0, 0.04) && ms1 == ms0, $"{t1 * 1000:0} ms, SocoMs {ms1}");

		MbArrancar(a, "Braco esquerdo");
		(double t2, int ms2) = MbEsperaDoSoco(a);
		AfirmarMb("sem os DOIS a espera DOBRA (a recarga escorre a meio ritmo)", MbPerto(t2 / t0, 2, 0.15), $"{t2 * 1000:0} ms contra {t0 * 1000:0} ms (x{t2 / t0:0.00})");
		AfirmarMb("...e o `SocoMs` que vai pro cliente dobra junto (ele nao tenta um golpe que o servidor recusaria)", MbPerto(ms2, ms0 * 2, 2), $"SocoMs {ms2} contra {ms0}");

		// O VERB DO NAMEKUSEIJIN devolve um braco: um so de volta ja tira ESTE debuff.
		a.Race = "Namekian";
		a.RegenLivreEm = 0;
		a.Ficha.Ki = a.Ficha.MaxKi;
		Regenerar(a);
		DebuffsDeMembro.Perdas p = DebuffsDeMembro.De(a.Combate.Corpo);
		(double t3, int ms3) = MbEsperaDoSoco(a);
		AfirmarMb("o `regenerar` do Namekuseijin devolveu um braco: a recarga volta ao normal no mesmo instante",
			p.Bracos == 1 && MbPerto(t3, t0, 0.04) && ms3 == ms0, $"bracos perdidos {p.Bracos}, {t3 * 1000:0} ms, SocoMs {ms3}");
	}

	// =====================================================================
	// 4) SEM UMA PERNA NAO CORRE
	// =====================================================================
	private void MbSemUmaPernaNaoCorre()
	{
		GD.Print("[membros] --- 4) SEM UMA PERNA NAO CORRE: dez pacotes de input pedindo corrida, pelo `AplicarInput` ---");
		LimparTudoDaBancada();
		ServerPlayer pl = MbCorpo("mbCorre", 30);
		pl.SpeedStat = 1f;
		float passoCheio = MoveRules.SpeedPx(1f, true);   // px por segundo correndo
		float andando = MoveRules.SpeedPx(1f, false);

		double kiAntes = pl.Ficha.MaxKi;
		float correu = MbAndar(pl, correndo: true);
		double cobrado = kiAntes - pl.Ficha.Ki;
		AfirmarMb("CONTROLE: com as duas pernas o servidor CONCEDE a corrida (anda o passo de corrida inteiro, cobra Ki)",
			pl.Correndo && MbPerto(correu, passoCheio, 2) && cobrado > 0, $"{correu:0.0} px em 1 s (corrida = {passoCheio:0.0}), Ki cobrado {cobrado:0.###}");

		MbArrancar(pl, "Perna esquerda");
		float manco = MbAndar(pl, correndo: true);
		double cobradoManco = pl.Ficha.MaxKi - pl.Ficha.Ki;
		AfirmarMb("sem UMA perna o pedido de corrida e RECUSADO: o corpo nao sai correndo",
			!pl.Correndo && manco < passoCheio * 0.75f, $"{manco:0.0} px em 1 s (corrida seria {passoCheio:0.0})");
		AfirmarMb("...e a corrida que nao houve nao e cobrada em Ki", cobradoManco <= 1e-9, $"Ki cobrado {cobradoManco:0.###}");
		float andou = MbAndar(pl, correndo: false);
		AfirmarMb("...mas ANDAR ele anda, no passo normal (uma perna so nao prende ninguem)", MbPerto(andou, andando, 2), $"{andou:0.0} px em 1 s (passo = {andando:0.0})");

		// NO AR O SHIFT E O SUPERFLIGHT, e o DM nao pergunta pelas pernas la (`movement handler.dm:52-53`).
		pl.Voando = true;
		pl.Altitude = Voo.AlturaDePairar;
		float voou = MbAndar(pl, correndo: true);
		AfirmarMb("voando, o SHIFT continua valendo sem perna (e o superflight: o DM so corta o dash do chao)",
			pl.Correndo && MbPerto(voou, passoCheio, 2), $"{voou:0.0} px em 1 s no ar");
		pl.Voando = false;
		pl.Altitude = 0f;

		// A MORTE E O RETORNO devolvem o corpo inteiro (`Death.dm:87-88`): a corrida volta.
		pl.Combate.Reviver(1, 0);
		float deVolta = MbAndar(pl, correndo: true);
		AfirmarMb("morte e retorno (`Reviver`) devolvem a perna: a corrida volta", pl.Correndo && MbPerto(deVolta, passoCheio, 2), $"{deVolta:0.0} px em 1 s");
	}

	// =====================================================================
	// 5) SEM AS DUAS PERNAS SO VOANDO
	// =====================================================================
	private void MbSemAsDuasPernasSoVoando()
	{
		GD.Print("[membros] --- 5) SEM AS DUAS PERNAS, NO CHAO O CORPO NAO SAI DO LUGAR -- so voando ---");
		LimparTudoDaBancada();
		ServerPlayer pl = MbCorpo("mbChao", 30);
		pl.SpeedStat = 1f;
		float andando = MoveRules.SpeedPx(1f, false);

		MbArrancar(pl, "Perna esquerda", "Perna direita");
		float noChao = MbAndar(pl, correndo: false);
		AfirmarMb("sem as DUAS pernas, dez pacotes de passo a pe nao movem o corpo um pixel", Math.Abs(noChao) < 0.01f, $"{noChao:0.00} px em 1 s (o passo seria {andando:0.0})");
		AfirmarMb("...e o funil de vetor diz que este corpo nao sai do lugar (`PodeMexerOCorpo`)", !PodeMexerOCorpo(pl));

		pl.Voando = true;
		pl.Altitude = Voo.AlturaDePairar;
		float noAr = MbAndar(pl, correndo: false);
		AfirmarMb("VOANDO ele se move como sempre (`legsLost >= 2 && !flight`)", MbPerto(noAr, andando, 2) && PodeMexerOCorpo(pl), $"{noAr:0.0} px em 1 s no ar");
		pl.Voando = false;
		pl.Altitude = 0f;
		AfirmarMb("...e ao pousar volta a ficar preso", !PodeMexerOCorpo(pl) && Math.Abs(MbAndar(pl, correndo: false)) < 0.01f);

		// UMA PERNA DE VOLTA BASTA PRA ANDAR (mas nao pra correr).
		pl.Combate.Corpo.Regenerar(pl.Combate.Corpo.Achar("Perna direita")!);
		float comUma = MbAndar(pl, correndo: false);
		AfirmarMb("uma perna de volta (`Body.Regenerar`): anda de novo a pe", MbPerto(comUma, andando, 2) && PodeMexerOCorpo(pl), $"{comUma:0.0} px em 1 s");
		MbAndar(pl, correndo: true);
		AfirmarMb("...e ainda nao corre, porque a outra continua no chao", !pl.Correndo);

		// O HEAL (`World_Heal`, `CombatState.Curar`) devolve tudo.
		pl.Combate.Curar();
		MbAndar(pl, correndo: true);
		AfirmarMb("o Heal (`CombatState.Curar`) devolve as duas: corre de novo", pl.Correndo && PodeMexerOCorpo(pl));
	}

	// =====================================================================
	// 6) FERIDO ANDA DEVAGAR
	// =====================================================================
	private void MbFeridoAndaDevagar()
	{
		GD.Print("[membros] --- 6) FERIDO ANDA DEVAGAR: `mobTime *= HP/100` (`movement handler.dm:55`), pelo `RecalcularVelocidade` ---");
		LimparTudoDaBancada();
		ServerPlayer pl = MbCorpo("mbFerido", 30);
		RecalcularVelocidade(pl);
		float inteiro = pl.SpeedStat;
		float andouInteiro = MbAndar(pl, correndo: false);

		void Machucar(ServerPlayer quem, double fracao)
		{
			foreach (BodyPart m in quem.Combate.Corpo.Partes) if (!m.Decepado) m.Vida = m.VidaMax * fracao;
			quem.Combate.SincronizarVida();
			RecalcularVelocidade(quem);
		}

		Machucar(pl, 0.5);
		float andouFerido = MbAndar(pl, correndo: false);
		AfirmarMb("com a vida em 50% o passo e a METADE (o `SpeedStat` que as duas pontas obedecem)",
			MbPerto(pl.SpeedStat / inteiro, 0.5, 0.01), $"SpeedStat {pl.SpeedStat:0.###} contra {inteiro:0.###} (vida {pl.Ficha.HP:0}%)");
		AfirmarMb("...medido pelo DESLOCAMENTO: em 1 s anda a metade", MbPerto(andouFerido / andouInteiro, 0.5, 0.03), $"{andouFerido:0.0} px contra {andouInteiro:0.0} px");

		Machucar(pl, 0.8);
		AfirmarMb("com a vida em 80%, 80% do passo", MbPerto(pl.SpeedStat / inteiro, 0.8, 0.01), $"x{pl.SpeedStat / inteiro:0.###}");

		// PERDER O MEMBRO NAO FREIA POR SI: o arrancado sai da MEDIA da vida (`Injuries.dm:263-265`).
		Machucar(pl, 1.0);
		MbArrancar(pl, "Braco direito");
		RecalcularVelocidade(pl);
		AfirmarMb("um braco arrancado com o resto do corpo inteiro NAO freia o passo (o arrancado sai da media da vida)",
			MbPerto(pl.SpeedStat / inteiro, 1, 0.01) && pl.Ficha.HP > 99.9, $"x{pl.SpeedStat / inteiro:0.###}, vida {pl.Ficha.HP:0.#}%");

		// ANDROIDE E MAJIN NAO MANCAM.
		foreach (string raca in new[] { "Android", "Majin" })
		{
			ServerPlayer imune = MbCorpo($"mb{raca}");
			imune.Race = raca;
			RecalcularVelocidade(imune);
			float antes = imune.SpeedStat;
			Machucar(imune, 0.5);
			AfirmarMb($"{raca} com a vida em 50% anda no passo cheio (`undelayed = 1`)", MbPerto(imune.SpeedStat / antes, 1, 0.01), $"x{imune.SpeedStat / antes:0.###}");
		}

		// CURADO, O PASSO VOLTA.
		pl.Combate.Curar();
		Machucar(pl, 0.5);
		pl.Combate.Corpo.Curar(1000);
		pl.Combate.SincronizarVida();
		RecalcularVelocidade(pl);
		AfirmarMb("curado (a vida de volta em 100%), o passo e o de antes", MbPerto(pl.SpeedStat / inteiro, 1, 0.01), $"x{pl.SpeedStat / inteiro:0.###}");

		// O TEXTO DO PODER (`hpratio`) E DE OUTRA CASA: a bancada cobra que os dois concordem.
		Machucar(pl, 0.7);
		pl.Ficha.Tick(agoraMs: NowMs());
		AfirmarMb("o `hpratio` do `Fighter.PowerLevel` e o `DebuffsDeMembro.FatorDoPoder` (o que a aba Body escreve) sao o mesmo numero",
			MbPerto(pl.Ficha.hpratio, DebuffsDeMembro.FatorDoPoder(pl.Ficha.HP), 1e-9), $"hpratio {pl.Ficha.hpratio:0.###}, texto {DebuffsDeMembro.FatorDoPoder(pl.Ficha.HP):0.###}");
		Machucar(pl, 0.3);
		pl.Ficha.Tick(agoraMs: NowMs());
		AfirmarMb("...inclusive no piso (vida em 30% -> 0,6)", MbPerto(pl.Ficha.hpratio, 0.6, 1e-9) && MbPerto(DebuffsDeMembro.FatorDoPoder(pl.Ficha.HP), 0.6, 1e-9));
	}

	// =====================================================================
	// 7) A FRASE NO CHAT NA HORA DA PERDA
	// =====================================================================
	private void MbAFraseDaPerda()
	{
		GD.Print("[membros] --- 7) A FRASE NO CHAT no instante em que o membro cai (pelo `CombatState.Arrancar`, o `LopLimb`) ---");
		LimparTudoDaBancada();
		ServerPlayer v = MbCorpo("mbVitima");
		Forjar("mbPlateia", v.Pos + new Vec2(3 * ZoneCollision.TileSize, 0), 5_000);

		EscutaDeAvisos = [];
		v.Combate.Arrancar(v.Combate.Corpo.Achar("Braco direito")!);
		List<string> ouvido = [.. EscutaDeAvisos];
		AfirmarMb("quem PERDEU le o que caiu e o que passa a valer (\"seu braço direito foi ARRANCADO! ... 25% ...\")",
			ouvido.Count(t => t.StartsWith("seu braço direito foi ARRANCADO!", StringComparison.Ordinal) && t.Contains("25%")) == 1,
			string.Join(" | ", ouvido));
		AfirmarMb("...quem ASSISTE le so o fato (\"o braço direito de mbVitima foi ARRANCADO!\"), sem numero nenhum",
			ouvido.Any(t => t == "o braço direito de mbVitima foi ARRANCADO!"), string.Join(" | ", ouvido));
		AfirmarMb("...e a mao, que foi de carona, nao ganha frase propria", !ouvido.Any(t => t.Contains("mão")), string.Join(" | ", ouvido));

		EscutaDeAvisos = [];
		v.Combate.Arrancar(v.Combate.Corpo.Achar("Braco esquerdo")!);
		AfirmarMb("o SEGUNDO braco diz a conta de depois dele (50% e a recarga 2×)",
			EscutaDeAvisos.Any(t => t.StartsWith("seu braço esquerdo foi ARRANCADO!", StringComparison.Ordinal) && t.Contains("50%") && t.Contains("2×")),
			string.Join(" | ", EscutaDeAvisos));

		EscutaDeAvisos = [];
		v.Combate.Arrancar(v.Combate.Corpo.Achar("Perna esquerda")!);
		AfirmarMb("a perna vai no feminino e diz que acabou a corrida",
			EscutaDeAvisos.Any(t => t.StartsWith("sua perna esquerda foi ARRANCADA!", StringComparison.Ordinal) && t.Contains("não corre")),
			string.Join(" | ", EscutaDeAvisos));

		EscutaDeAvisos = [];
		v.Combate.Arrancar(v.Combate.Corpo.Achar("Perna direita")!);
		AfirmarMb("a segunda perna diz que agora e so voando",
			EscutaDeAvisos.Any(t => t.StartsWith("sua perna direita foi ARRANCADA!", StringComparison.Ordinal) && t.Contains("só voando")),
			string.Join(" | ", EscutaDeAvisos));

		EscutaDeAvisos = [];
		v.Combate.Arrancar(v.Combate.Corpo.Achar("Perna direita")!);
		AfirmarMb("CONTRA-EXEMPLO: o membro que ja caiu nao cai de novo -- nenhuma frase", !EscutaDeAvisos.Any(t => t.Contains("ARRANCAD")), string.Join(" | ", EscutaDeAvisos));
		EscutaDeAvisos = null;
	}

	// =====================================================================
	// 8) OS TEXTOS DIZEM O QUE SE APLICA
	// =====================================================================
	private void MbOsTextosDizemOQueSeAplica()
	{
		GD.Print("[membros] --- 8) OS TEXTOS DA ABA BODY (as mesmas funcoes do Core) dizem os numeros que as familias 2-6 mediram ---");
		var umBraco = new DebuffsDeMembro.Perdas(1, 0, 1);
		var semBracos = new DebuffsDeMembro.Perdas(2, 0, 2);
		var umaPerna = new DebuffsDeMembro.Perdas(0, 1, 1);
		var semPernas = new DebuffsDeMembro.Perdas(0, 2, 2);
		var semNada = new DebuffsDeMembro.Perdas(2, 2, 4);

		List<string> t = DebuffsDeMembro.EmVigor(new DebuffsDeMembro.Perdas(0, 0, 0), 100, "Human");
		AfirmarMb("corpo inteiro e com a vida cheia: nenhuma linha de efeito", t.Count == 0, string.Join(" | ", t));

		t = DebuffsDeMembro.EmVigor(umBraco, 100, "Human");
		AfirmarMb("um braco perdido: \"25% menos chance de acertar\" (a familia 2 mediu 60% de 80%) e a guarda com 25% a mais",
			t.Any(l => l.Contains("25% menos chance de acertar") && l.Contains("1 braço perdido")) && t.Any(l => l.Contains("guarda") && l.Contains("25%"))
			&& !t.Any(l => l.Contains("recarga")), string.Join(" | ", t));

		t = DebuffsDeMembro.EmVigor(semBracos, 100, "Human");
		AfirmarMb("os dois bracos: \"50% menos chance\" e \"recarga do soco 2× mais lenta\" (a familia 3 mediu o dobro)",
			t.Any(l => l.Contains("50% menos chance de acertar") && l.Contains("2 braços perdidos")) && t.Any(l => l.Contains("recarga do soco") && l.Contains("2×")),
			string.Join(" | ", t));

		t = DebuffsDeMembro.EmVigor(umaPerna, 100, "Human");
		AfirmarMb("uma perna: \"Você não corre\" -- e nada de \"não sai do lugar\"", t.Any(l => l.Contains("não corre")) && !t.Any(l => l.Contains("só voando")), string.Join(" | ", t));

		t = DebuffsDeMembro.EmVigor(semPernas, 100, "Human");
		AfirmarMb("as duas pernas: \"No chão você não sai do lugar: só voando\"", t.Any(l => l.Contains("não sai do lugar") && l.Contains("só voando")), string.Join(" | ", t));

		t = DebuffsDeMembro.EmVigor(semNada, 100, "Human");
		AfirmarMb("sem braco nem perna: \"Você não ergue a guarda\" (o `EscolherGuarda` nao acha membro)",
			t.Any(l => l.Contains("não ergue a guarda")), string.Join(" | ", t));

		t = DebuffsDeMembro.EmVigor(new DebuffsDeMembro.Perdas(0, 0, 0), 50, "Human");
		AfirmarMb("vida em 50%: \"passo 50% mais lento\" (a familia 6 mediu a metade) e \"poder rende 40% a menos\" (o piso do hpratio)",
			t.Any(l => l.Contains("passo") && l.Contains("50% mais lento")) && t.Any(l => l.Contains("poder") && l.Contains("40%")), string.Join(" | ", t));
		t = DebuffsDeMembro.EmVigor(new DebuffsDeMembro.Perdas(0, 0, 0), 50, "Majin");
		AfirmarMb("...e pro Majin a linha do passo NAO aparece (ele nao manca), so a do poder",
			!t.Any(l => l.Contains("passo")) && t.Any(l => l.Contains("poder")), string.Join(" | ", t));

		// A LINHA DE CADA MEMBRO
		string? n = DebuffsDeMembro.NotaDoMembro("Braco direito", 0, true);
		AfirmarMb("a linha do braco ARRANCADO diz os 25% e que nao apara mais com ele", n != null && n.Contains("25%") && n.Contains("não apara"), n ?? "(nula)");
		n = DebuffsDeMembro.NotaDoMembro("Perna esquerda", 0, true);
		AfirmarMb("a da perna ARRANCADA diz que nao corre", n != null && n.Contains("não corre"), n ?? "(nula)");
		n = DebuffsDeMembro.NotaDoMembro("Braco esquerdo", 12, false);
		AfirmarMb("a do braco QUEBRADO (12%) fala so da guarda (ele ainda soca)", n != null && n.Contains("quebrado") && n.Contains("guarda") && !n.Contains("acertar"), n ?? "(nula)");
		n = DebuffsDeMembro.NotaDoMembro("Cabeca", 15, false);
		AfirmarMb("a do nucleo quebrado explica o nocaute (20%)", n != null && n.Contains("20%") && n.Contains("nocaute"), n ?? "(nula)");
		AfirmarMb("membro inteiro, e membro ferido acima da linha de quebra, nao ganham linha",
			DebuffsDeMembro.NotaDoMembro("Braco direito", 100, false) == null && DebuffsDeMembro.NotaDoMembro("Perna direita", 45, false) == null
			&& DebuffsDeMembro.NotaDoMembro("Torso", 60, false) == null);
		AfirmarMb("nenhum texto traz nome cru do fio (\"Braco\", \"Mao\", \"Cabeca\" sem acento)",
			!DebuffsDeMembro.FraseDaPerda("Braco direito", umBraco).Contains("Braco") && !DebuffsDeMembro.FraseDaPerdaAlheia("X", "Mao direita").Contains("Mao")
			&& DebuffsDeMembro.NomeNaTela("Cabeca") == "Cabeça");

		// A GUARDA: o numero do texto e o que o `GuardaAguenta` cobra.
		AfirmarMb("os 25% da guarda no texto sao a constante que o `GuardaAguenta` soma por membro fora de combate", MeleeResolver.FalhaDaGuardaPorMembro == 25);
	}

	// =====================================================================
	// 9) QUEM A IA DIRIGE NAO TRAVA
	// =====================================================================
	private void MbQuemAIaDirigeNaoTrava()
	{
		GD.Print("[membros] --- 9) QUEM A IA DIRIGE NAO TRAVA (`NPCAI.dm:855-892` nao le pernas, bracos nem HP), o bicho nao perde membro, e nada divide por zero ---");
		LimparTudoDaBancada();
		ServerPlayer npc = MbCorpo("mbNpc", 30);
		npc.Cerebro = new Jandirus.Core.Ai.Cerebro();
		RecalcularVelocidade(npc);
		float passo = npc.SpeedStat;
		(double t0, _) = MbEsperaDoSoco(npc);

		MbArrancar(npc, "Perna esquerda", "Perna direita", "Braco direito", "Braco esquerdo");
		foreach (BodyPart m in npc.Combate.Corpo.Partes) if (!m.Decepado) m.Vida = m.VidaMax * 0.5;
		npc.Combate.SincronizarVida();
		RecalcularVelocidade(npc);

		AfirmarMb("NPC sem as duas pernas: o funil de vetor continua deixando a IA anda-lo (ele nao vira estatua)", PodeMexerOCorpo(npc));
		AfirmarMb("NPC sem perna: a corrida continua concedida (a recusa da perna nao vale pra ele, e o `PodeCorrer` concede)",
			!SemPernaPraCorrer(npc) && PodeCorrer(npc, 0.1f));
		AfirmarMb("...e nao e por falta de perna que a pergunta responde assim: o MESMO corpo, devolvido ao dono, nao corre nem anda",
			MbDoDono(npc, () => SemPernaPraCorrer(npc) && SemPernasNoChao(npc) && !PodeMexerOCorpo(npc)));
		AfirmarMb("NPC ferido (vida em 50%): o passo e o mesmo", MbPerto(npc.SpeedStat / passo, 1, 0.01), $"x{npc.SpeedStat / passo:0.###}");
		(double t1, _) = MbEsperaDoSoco(npc);
		AfirmarMb("NPC sem os dois bracos: a recarga do soco e a mesma (`NPCAI.dm:892`, `attacking-1` cru)", MbPerto(t1, t0, 0.04), $"{t1 * 1000:0} ms contra {t0 * 1000:0} ms");

		ServerPlayer alvo = Forjar("mbAlvoDoNpc", npc.Pos + new Vec2(ZoneCollision.TileSize, 0), 5_000);
		double taxa = MbTaxaDeAcerto(npc, alvo, 4000, 21);
		AfirmarMb("...mas a PONTARIA cai pra ele tambem: o `hitProc` e um so pros dois (40% de 80%)", MbPerto(taxa, 0.40, 0.03), $"{taxa:P1}");

		// AO LEME: a nave nao anda com as pernas do piloto.
		// (v0.2.1) A NAVE POUSADA NAO ANDA, DE NINGUEM (`NavePousada`): quem a move e quem a decolou. Entao a medida das
		// pernas e com ela NO AR, decolada pela porta de producao (a tecla de subir, `DecolarANavePousada`) -- ate a
		// v0.2.0 esta linha media a nave no chao, e passou a reprovar por causa da regra nova, e nao das pernas.
		ServerPlayer piloto = MbCorpo("mbPiloto");
		MbArrancar(piloto, "Perna esquerda", "Perna direita");
		var nave = new Nave { Id = -9_911, Tipo = "bancada", PilotoId = piloto.Id };
		nave.PorZona(piloto.Zone);
		bool presoAPe = !PodeMexerOCorpo(piloto);
		_naves.Add(nave);
		bool pousadaParada, livreAoLeme;
		try
		{
			pousadaParada = !PodeMexerOCorpo(piloto);
			DecolarANavePousada(piloto);
			livreAoLeme = piloto.Voando && PodeMexerOCorpo(piloto) && !SemPernaPraCorrer(piloto);
		}
		finally
		{
			_naves.Remove(nave);
			piloto.Voando = false;
			piloto.Altitude = 0;
		}
		AfirmarMb("o piloto sem as duas pernas esta preso A PE; AO LEME, a nave pousada nao anda (de ninguem) e, DECOLADA, ele a dirige normalmente (inclusive no SHIFT)",
			presoAPe && pousadaParada && livreAoLeme && !PodeMexerOCorpo(piloto),
			$"a pe preso={presoAPe}, pousada parada={pousadaParada}, ao leme livre={livreAoLeme}");

		// O ZENKAI DE QUEM FOI DESPEDACADO: `if(lopcount >= 2) return TRUE` (`Injuries.dm:191`).
		ServerPlayer saiyajin = MbCorpo("mbZenkai");
		bool inteiro = saiyajin.Combate.Corpo.MuitoFerido();
		MbArrancar(saiyajin, "Braco direito");
		bool comUm = saiyajin.Combate.Corpo.MuitoFerido();
		MbArrancar(saiyajin, "Perna esquerda");
		bool comDois = saiyajin.Combate.Corpo.MuitoFerido();
		AfirmarMb("\"extremamente ferido\" (o gatilho do Zenkai maior): UM braco arrancado -- a mao de carona nao conta -- ainda nao; DOIS membros arrancados, sim",
			!inteiro && !comUm && comDois, $"inteiro={inteiro}, um={comUm}, dois={comDois}");

		// O BICHO: `MembrosFirmes` -- o membro zera e nao sai, entao nao ha o que cobrar.
		ServerPlayer bicho = MbCorpo("mbBicho");
		bicho.Combate.MembrosFirmes = true;
		BodyPart pata = bicho.Combate.Corpo.Achar("Perna esquerda")!;
		bicho.Combate.Ferir(pata, pata.VidaMax * 10, letal: true);
		DebuffsDeMembro.Perdas doBicho = DebuffsDeMembro.De(bicho.Combate.Corpo);
		AfirmarMb("o bicho (`MembrosFirmes`) leva o golpe letal que zera a pata e ela NAO cai: nenhuma perna perdida",
			!pata.Decepado && doBicho.Pernas == 0, $"{doBicho}");

		// TODO MEMBRO EM ZERO / ARRANCADO: nenhuma conta estoura.
		ServerPlayer resto = MbCorpo("mbResto");
		foreach (BodyPart m in resto.Combate.Corpo.Partes) { m.Decepado = true; m.Vida = 0; }
		resto.Combate.SincronizarVida();
		RecalcularVelocidade(resto);
		List<string> linhas = DebuffsDeMembro.EmVigor(DebuffsDeMembro.De(resto.Combate.Corpo), resto.Ficha.HP, resto.Race);
		int ms = resto.Sheet().SocoMs;
		AfirmarMb("corpo com TODO membro arrancado: velocidade, cadencia e textos saem finitos (nada divide por zero)",
			float.IsFinite(resto.SpeedStat) && resto.SpeedStat > 0 && ms > 0 && linhas.Count > 0 && !linhas.Any(l => l.Contains("NaN") || l.Contains('∞')),
			$"SpeedStat {resto.SpeedStat:0.###}, SocoMs {ms}, vida {resto.Ficha.HP:0.#}, {linhas.Count} linhas");
	}

	// =====================================================================
	// 11) SSJ4 SEM RABO SE DESFAZ
	// =====================================================================
	private void MbSsj4SemRabo()
	{
		GD.Print("[membros] --- 11) SSJ4 SEM RABO SE DESFAZ (`supersaiyanbuff.dm:135-162`), pelo `TickDaForma` de producao ---");
		LimparTudoDaBancada();
		const double dt = 1.0 / 30;
		try
		{
			// UM SAIYAJIN (o corpo dele tem o membro `Rabo`) posto em SSJ4.
			ServerPlayer s = MbCorpo("mbSsj4");
			s.Race = "Saiyan";
			s.Ficha.Race = "Saiyan";
			PrepararCombate(s, null);
			BodyPart? rabo = s.Combate.Corpo.Achar("Rabo");
			AfirmarMb("PREMISSA: o Saiyajin da bancada tem o membro Rabo", rabo != null);
			if (rabo == null) return;
			s.Forma.Entrar("ssj4");
			s.Ficha.Ki = s.Ficha.MaxKi;
			TickDaForma(s, dt);
			AfirmarMb("CONTROLE: com o rabo no lugar, o tique da forma mantem o SSJ4", s.Forma.Atual == "ssj4", s.Forma.Atual);

			bool desfeitoComDefeito;
			s.Combate.Arrancar(rabo);
			s.Ficha.Ki = s.Ficha.MaxKi;
			try
			{
				DebuffsDeMembro.DesligadosDeTeste = true;
				TickDaForma(s, dt);
				desfeitoComDefeito = s.Forma.Atual != "ssj4";
			}
			finally { DebuffsDeMembro.DesligadosDeTeste = false; }
			AfirmarMb("(defeito injetado: a forma nao le o rabo) sem rabo o SSJ4 continua de pe -- o port de antes", !desfeitoComDefeito, s.Forma.Atual);

			EscutaDeAvisos = [];
			TickDaForma(s, dt);
			AfirmarMb("o rabo ARRANCADO desfaz o SSJ4 no tique seguinte (a forma volta ao piso da escada)",
				s.Forma.Atual != "ssj4" && s.Forma.Atual == Jandirus.Core.Forms.Catalogo.IdDoPiso(Perfil(s)), s.Forma.Atual);
			AfirmarMb("...e o dono le o porque", EscutaDeAvisos.Any(t => t.Contains("rabo") && t.Contains("Super Saiyajin 4")), string.Join(" | ", EscutaDeAvisos));
			EscutaDeAvisos = null;

			// O CORPO QUE NUNCA TEVE RABO (um humano que o roteiro pos em SSJ4) nao e derrubado pela regra.
			ServerPlayer h = MbCorpo("mbSsj4SemMembro");
			h.Forma.Entrar("ssj4");
			h.Ficha.Ki = h.Ficha.MaxKi;
			TickDaForma(h, dt);
			AfirmarMb("um corpo SEM o membro Rabo (nunca teve o que perder) fica no SSJ4: a regra e de quem PERDEU o rabo",
				h.Combate.Corpo.Achar("Rabo") == null && h.Forma.Atual == "ssj4", h.Forma.Atual);
		}
		catch (Exception e)
		{
			AfirmarMb("a familia do SSJ4 correu ate o fim sem estourar", false, $"{e.GetType().Name}: {e.Message}");
		}
		finally { EscutaDeAvisos = null; }
	}

	// =====================================================================
	// 12) SEM BRACO NEM PERNA INTEIRO NAO SE ERGUE A GUARDA
	// =====================================================================
	private void MbSemMembroNaoHaGuarda()
	{
		GD.Print("[membros] --- 12) SEM BRACO NEM PERNA INTEIRO NAO SE ERGUE A GUARDA (o `holdblock()`, `CombatMovement.dm:77-79`), pela porta do `C2S.Guard` ---");
		LimparTudoDaBancada();
		ServerPlayer pl = MbCorpo("mbGuarda");

		ErguerAGuarda(pl, true);
		AfirmarMb("CONTROLE: com o corpo inteiro o ALT ergue a guarda", pl.Combate.Bloqueando);
		ErguerAGuarda(pl, false);

		// TRES FORA (dois bracos arrancados, uma perna quebrada): ainda ha uma perna pra aparar.
		MbArrancar(pl, "Braco direito", "Braco esquerdo");
		BodyPart pernaE = pl.Combate.Corpo.Achar("Perna esquerda")!;
		pernaE.Vida = pernaE.VidaMax * 0.10;
		EscutaDeAvisos = [];
		ErguerAGuarda(pl, true);
		AfirmarMb("com TRES dos quatro fora de combate ainda se ergue a guarda (sobra uma perna), sem aviso",
			pl.Combate.Bloqueando && EscutaDeAvisos.Count == 0, $"fora da guarda: {DebuffsDeMembro.De(pl.Combate.Corpo).ForaDaGuarda}");
		ErguerAGuarda(pl, false);

		// OS QUATRO FORA: a guarda nem sobe, e o dono le o porque.
		BodyPart pernaD = pl.Combate.Corpo.Achar("Perna direita")!;
		pernaD.Vida = pernaD.VidaMax * 0.10;
		EscutaDeAvisos = [];
		ErguerAGuarda(pl, true);
		AfirmarMb("com os QUATRO fora (dois arrancados, dois quebrados) a guarda NAO sobe",
			!pl.Combate.Bloqueando, $"fora da guarda: {DebuffsDeMembro.De(pl.Combate.Corpo).ForaDaGuarda}");
		AfirmarMb("...e o dono le o porque, uma frase por aperto", EscutaDeAvisos.Count(t => t == DebuffsDeMembro.AvisoDeGuarda) == 1, string.Join(" | ", EscutaDeAvisos));
		AfirmarMb("...que e o que a aba Body escreve pra esse corpo (\"Você não ergue a guarda\")",
			DebuffsDeMembro.EmVigor(DebuffsDeMembro.De(pl.Combate.Corpo), pl.Ficha.HP, pl.Race).Any(l => l.Contains("não ergue a guarda")));

		try
		{
			DebuffsDeMembro.DesligadosDeTeste = true;
			ErguerAGuarda(pl, true);
			AfirmarMb("(defeito injetado: a porta do ALT nao le os membros) a guarda SOBE num corpo que nao tem com o que aparar -- o port de antes",
				pl.Combate.Bloqueando);
		}
		finally { DebuffsDeMembro.DesligadosDeTeste = false; pl.Combate.Guardar(false); EscutaDeAvisos = null; }

		// UM MEMBRO SARADO ACIMA DA LINHA DE QUEBRA: a guarda volta.
		pernaD.Vida = pernaD.VidaMax * 0.50;
		ErguerAGuarda(pl, true);
		AfirmarMb("uma perna de volta acima dos 20%: a guarda sobe de novo", pl.Combate.Bloqueando);
		pl.Combate.Guardar(false);

		// ============================ E A IA PASSA PELA MESMA PORTA (`NPCAI.dm:191` chama o `holdblock()`) ============================
		// Pelo ATUADOR de producao (`AplicarComando`, o que o tique da IA chama), com o comando que o cerebro manda
		// quando quer aparar. Na primeira versao o atuador escrevia a guarda no `CombatState` direto, e o NPC sem os
		// quatro membros erguia a guarda que o jogador nao ergue (a `--kideponta` reprovou a diferenca no FONTE; isto
		// aqui mede o COMPORTAMENTO).
		const double dt = 1.0 / 30;
		var querAparar = new Jandirus.Core.Ai.Comando { Guardar = true };
		var soltaAGuarda = new Jandirus.Core.Ai.Comando { Guardar = false };

		ServerPlayer npc = MbCorpo("mbGuardaIa");
		npc.Cerebro = new Jandirus.Core.Ai.Cerebro();
		AplicarComando(npc, querAparar, dt);
		AfirmarMb("CONTROLE: o NPC inteiro ergue a guarda pelo atuador da IA (`AplicarComando`)", npc.Combate.Bloqueando);
		AplicarComando(npc, soltaAGuarda, dt);
		AfirmarMb("...e a baixa pelo mesmo caminho", !npc.Combate.Bloqueando);

		MbArrancar(npc, "Braco direito", "Braco esquerdo", "Perna esquerda", "Perna direita");
		EscutaDeAvisos = [];
		for (int i = 0; i < 30; i++) AplicarComando(npc, querAparar, dt);
		AfirmarMb("o NPC SEM os quatro membros nao ergue a guarda, por mais que o cerebro insista (30 tiques pedindo)",
			!npc.Combate.Bloqueando, $"fora da guarda: {DebuffsDeMembro.De(npc.Combate.Corpo).ForaDaGuarda}");
		AfirmarMb("...e nenhuma frase sai pra ele (a recusa da IA e calada: senao o jogador possuido leria trinta por segundo)",
			!EscutaDeAvisos.Contains(DebuffsDeMembro.AvisoDeGuarda), $"{EscutaDeAvisos.Count} aviso(s)");
		EscutaDeAvisos = null;
		AfirmarMb("...e a capacidade que o cerebro le (`TemComQueAparar`) diz o mesmo: ele para de insistir na proxima leitura",
			!LerCapacidades(npc).TemComQueAparar);

		// A PARIDADE, dita de uma vez: o MESMO corpo mutilado, pelas duas portas.
		bool pelaIa = npc.Combate.Bloqueando;
		bool peloDono = MbDoDono(npc, () =>
		{
			ErguerAGuarda(npc, true);
			bool subiu = npc.Combate.Bloqueando;
			npc.Combate.Guardar(false);
			return subiu;
		});
		AfirmarMb("PARIDADE: o mesmo corpo sem os quatro membros recebe a MESMA resposta pela porta da IA e pela do dono (nao sobe)",
			!pelaIa && !peloDono, $"IA={pelaIa}, dono={peloDono}");

		try
		{
			DebuffsDeMembro.DesligadosDeTeste = true;
			AplicarComando(npc, querAparar, dt);
			AfirmarMb("(defeito injetado: a porta nao le os membros) o NPC sem membro nenhum ERGUE a guarda -- a regua da paridade REPROVA",
				npc.Combate.Bloqueando);
		}
		finally { DebuffsDeMembro.DesligadosDeTeste = false; npc.Combate.Guardar(false); }
	}

	// =====================================================================
	// 10) O DEFEITO INJETADO
	// =====================================================================
	private void MbODefeitoInjetado()
	{
		GD.Print("[membros] --- 10) O DEFEITO INJETADO (`DebuffsDeMembro.DesligadosDeTeste`): o port de antes -- as cinco reguas tem que voltar ao corpo inteiro ---");
		LimparTudoDaBancada();
		ServerPlayer a = MbCorpo("mbDefBate");
		ServerPlayer d = Forjar("mbDefLeva", a.Pos + new Vec2(ZoneCollision.TileSize, 0), 5_000);
		(double t0, int ms0) = MbEsperaDoSoco(a);
		MbArrancar(a, "Braco direito", "Braco esquerdo");

		ServerPlayer manco = MbCorpo("mbDefManco", 30);
		manco.SpeedStat = 1f;
		MbArrancar(manco, "Perna esquerda");
		ServerPlayer semPernas = MbCorpo("mbDefChao", 30);
		semPernas.SpeedStat = 1f;
		MbArrancar(semPernas, "Perna esquerda", "Perna direita");
		ServerPlayer ferido = MbCorpo("mbDefFerido", 30);
		RecalcularVelocidade(ferido);
		float inteiro = ferido.SpeedStat;
		foreach (BodyPart m in ferido.Combate.Corpo.Partes) m.Vida = m.VidaMax * 0.5;
		ferido.Combate.SincronizarVida();

		try
		{
			DebuffsDeMembro.DesligadosDeTeste = true;

			double taxa = MbTaxaDeAcerto(a, d, 4000, 31);
			AfirmarMb("(defeito injetado: a pontaria nao le os bracos) sem os dois bracos o corpo acerta os 80% de quem esta inteiro -- a regua da familia 2 REPROVA",
				!MbPerto(taxa, 0.40, 0.03) && MbPerto(taxa, 0.80, 0.03), $"{taxa:P1}");

			(double t, int ms) = MbEsperaDoSoco(a);
			AfirmarMb("(defeito injetado: a recarga nao le os bracos) a espera do soco e a do corpo inteiro -- a regua da familia 3 REPROVA",
				!MbPerto(t / t0, 2, 0.15) && ms == ms0, $"{t * 1000:0} ms contra {t0 * 1000:0} ms, SocoMs {ms}");

			float correu = MbAndar(manco, correndo: true);
			AfirmarMb("(defeito injetado: a corrida nao le as pernas) sem uma perna o corpo CORRE -- a regua da familia 4 REPROVA",
				manco.Correndo && MbPerto(correu, MoveRules.SpeedPx(1f, true), 2), $"{correu:0.0} px em 1 s");

			float andou = MbAndar(semPernas, correndo: false);
			AfirmarMb("(defeito injetado: o funil nao le as pernas) sem as duas pernas o corpo ANDA a pe -- a regua da familia 5 REPROVA",
				PodeMexerOCorpo(semPernas) && MbPerto(andou, MoveRules.SpeedPx(1f, false), 2), $"{andou:0.0} px em 1 s");

			RecalcularVelocidade(ferido);
			AfirmarMb("(defeito injetado: o passo nao le a vida) com a vida em 50% o passo e o cheio -- a regua da familia 6 REPROVA",
				MbPerto(ferido.SpeedStat / inteiro, 1, 0.01), $"x{ferido.SpeedStat / inteiro:0.###}");
		}
		finally { DebuffsDeMembro.DesligadosDeTeste = false; }

		// E DE VOLTA: desligado o defeito, as mesmas reguas nos mesmos corpos voltam a aprovar.
		double deVolta = MbTaxaDeAcerto(a, d, 4000, 32);
		RecalcularVelocidade(ferido);
		AfirmarMb("desligado o defeito, os MESMOS corpos voltam a pagar: pontaria em 40%, passo na metade, preso no chao",
			MbPerto(deVolta, 0.40, 0.03) && MbPerto(ferido.SpeedStat / inteiro, 0.5, 0.01) && !PodeMexerOCorpo(semPernas),
			$"{deVolta:P1}, x{ferido.SpeedStat / inteiro:0.###}");
	}
}
