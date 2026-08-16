using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA COLISAO DE KI (`--embatekiteste`) -- roda no BOOT, sem ninguem em jogo.
///
///     Godot --headless --path . --server --port 7961 --embatekiteste
///
/// ============================ ELA CHAMA O CODIGO DE PRODUCAO, E SO ELE ============================
/// Os raios nascem por <c>Canalizar</c>, andam por <c>TickDosProjeteis</c>, se encontram por
/// <c>TentarEmbateDeFeixes</c> (o gatilho de verdade, dentro do tique), disputam por
/// <c>TickDosEmbatesDeKi</c>, as letras entram por <c>TeclaDeQualquerEmbate</c> -- a MESMA funcao
/// que o `case Protocol.C2S.ClashTecla` chama -- e o dano final sai pelo <c>Acertar</c>. Nao ha um
/// laco paralelo, nao ha uma fisica de teste e nao ha um caminho curto.
///
/// O UNICO PRIVILEGIO sao dois, e os dois estao escritos onde moram:
///   * a CADENCIA (o laco chama os tiques na mao, senao a bancada nao rodaria no boot);
///   * o TECLADO DE MENTIRA (<see cref="_comTecladoDeTeste"/>): os corpos forjados nao tem `Peer`, e
///     sem isso o lado do JOGADOR -- que e o que o quick time event existe pra medir -- nunca seria
///     exercitado. Ele nao muda regra nenhuma: so responde "este corpo tem teclado".
/// =============================================================================================
///
/// ============================ O QUE ELA AFIRMA ============================
///  1. O GATILHO dispara no caso certo e RECUSA nos quatro errados (mesmo dono, feixes paralelos,
///     bola, raio ja solto).
///  2. A FISICA e a do DM, decimo por decimo -- e o teto da vantagem existe nos DOIS sentidos.
///  3. QUEM VENCE: o quick time event vale, e vale um numero MEDIDO (ate ~2x de poder da pra roubar
///     a vitoria acertando tudo; em 6x, nao da) -- e a escada inteira so DESCE.
///  3b/3c. O PEDIDO DO DONO, EM PIXEL: cada acerto empurra o feixe do inimigo pra tras (medido na
///     posicao, nao no placar), e ENCOSTAR nele e VENCER -- o mesmo instante, medido no mesmo lugar.
///  4. O FIM E ASSIMETRICO: quem perde LEVA o feixe, pelo funil de dano de producao.
///  5. DA PRA SAIR, e sair e perder (soltar o canal / abaixar a guarda).
///  5b/5c/5d. O PRAZO: sem vencedor em 15 s explode, cobra dos DOIS e joga os DOIS pra tras -- e quem
///     vence ANTES nao explode. E as tres bordas (prazo, sair do jogo, ficar sem Ki) nao deixam
///     ninguem plantado nem nenhuma cabeca de feixe congelada no ar.
///  6. FEIXE CONTRA GUARDA: quem segura e VENCE devolve o ataque -- e ele acerta quem atirou.
///  7. O CORPO FICA PRESO nos dois lados, pelo funil de sempre (`PodeMexerOCorpo`).
///  8. O CUSTO, MEDIDO: com a zona lotada de tiros o gatilho continua barato.
///  9. A IA: a tabela de longe tem as tres que voam, o arsenal sai preenchido, o contra-feixe e um
///     reflexo e o prazo do raio de NPC nao corre durante a disputa.
/// =========================================================================
/// </summary>
public partial class GameServer
{
	private int _ekOk, _ekFalhou;

	private void AfirmarEk(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _ekOk++; GD.Print($"[embateki]   OK    {oque}"); return; }
		_ekFalhou++;
		GD.PrintErr($"[embateki]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDeEmbateDeKi()
	{
		_ekOk = _ekFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[embateki] ================ A COLISAO DE KI ================");
		AfirmarEk("a zona da bancada tem colisao carregada", _pjMapa != null);

		try
		{
			OGatilho();
			AFisicaDoMedidor();
			QuemVence();
			CadaAcertoEmpurraOFeixe();
			EncostarNoInimigoVence();
			OFimEAssimetrico();
			SairDoEmbateEPerder();
			OEmpateCobra();
			VencerAntesNaoExplode();
			AsBordasNaoPrendemNinguem();
			OFeixeContraAGuarda();
			OCorpoFicaPreso();
			OCustoDoGatilho();
			OsGanchosDaIa();
		}
		finally { LimparEmbatesDaBancada(); }

		GD.Print($"[embateki] ================ {_ekOk} passaram, {_ekFalhou} falharam ================");
	}

	// =====================================================================
	// 1) O GATILHO
	// =====================================================================
	/// <summary>
	/// Dois raios de frente um pro outro viram DISPUTA -- e as quatro recusas, que sao o que
	/// separa "detecta encontro" de "para qualquer coisa que passe perto".
	/// </summary>
	private void OGatilho()
	{
		GD.Print("[embateki] -- 1) O GATILHO: SO FEIXE CONTRA FEIXE, E SO DE FRENTE");

		(ServerPlayer a, ServerPlayer b, DisputaDeKi? d) = DoisRaiosDeFrente(tiles: 12);
		AfirmarEk("dois raios de frente comecam uma disputa", d != null);
		if (d != null)
		{
			AfirmarEk("...e as DUAS cabecas ficaram presas (o `in_beamclash` do DM)",
					  d.A.Feixe is { EmEmbate: true } && d.B.Feixe is { EmEmbate: true });
			AfirmarEk("...e o medidor comecou no meio", Math.Abs(d.Medidor - EmbateDeKi.MedidorInicial) < 1e-9);
			AfirmarEk("...e o encontro nasceu ENTRE os dois corpos",
					  (d.Ponto - a.Pos).Length > 32 && (d.Ponto - b.Pos).Length > 32,
					  $"ponto {d.Ponto}, a {a.Pos}, b {b.Pos}");
		}
		LimparEmbatesDaBancada();

		// (a) MESMO DONO: dois tiros meus nao disputam. `A == B` no `bcl_try_start`.
		ServerPlayer solo = Forjar("Solo", CorredorLivre(20), bp: 50_000);
		solo.Facing = Facing.East;
		solo.Ficha.Ki = solo.Ficha.MaxKi;
		Projetil p1 = Disparar(solo, RaioDeTeste());
		Projetil p2 = Disparar(solo, RaioDeTeste());
		p1.Canalizando = p2.Canalizando = true;
		p2.Rumo = new Vec2(-1, 0);
		p2.Pos = p1.Pos;
		AfirmarEk("dois tiros do MESMO dono nao disputam",
				  !TentarEmbateDeFeixes(p1, ProjeteisDaZona(solo.Zone.Hash)));
		LimparEmbatesDaBancada();

		// (b) PARALELOS: dois aliados atirando juntos. `R.dir != opp` no gatilho por proximidade.
		(ServerPlayer c, ServerPlayer e, DisputaDeKi? paralelo) = DoisRaiosDeFrente(12, viradoParaOOutro: false);
		AfirmarEk("dois raios no MESMO sentido nao disputam (aliados atirando juntos)", paralelo == null);
		_ = c; _ = e;
		LimparEmbatesDaBancada();

		// (c) BOLA: a disputa e coisa de raio canalizado -- `if(!one.WaveAttack) return 0`.
		ServerPlayer g = Forjar("Bolista", CorredorLivre(20), bp: 50_000);
		g.Facing = Facing.East;
		g.Ficha.Ki = g.Ficha.MaxKi;
		Projetil bola = Disparar(g, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast });
		AfirmarEk("uma BOLA nao entra em disputa",
				  !TentarEmbateDeFeixes(bola, ProjeteisDaZona(g.Zone.Hash)));

		// (d) RAIO JA SOLTO: sem dono canalizando nao ha quem empurre. `!A.beaming`
		Projetil largado = Disparar(g, RaioDeTeste());
		largado.Canalizando = false;
		AfirmarEk("um raio ja SOLTO nao entra em disputa (nao ha quem o empurre)",
				  !TentarEmbateDeFeixes(largado, ProjeteisDaZona(g.Zone.Hash)));

		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 2) A FISICA
	// =====================================================================
	/// <summary>
	/// A tabela do `BeamClash.dm`, conferida contra a funcao pura. Nenhum numero e digitado duas
	/// vezes: o esperado sai das MESMAS constantes que o jogo usa, e o que se afirma e a FORMA.
	/// </summary>
	private void AFisicaDoMedidor()
	{
		GD.Print("[embateki] -- 2) A FISICA DO MEDIDOR E A DO DM");

		// A VANTAGEM TEM TETO NOS DOIS SENTIDOS: `min(max(a/b, 1/CAP), CAP)`.
		AfirmarEk("forcas iguais dao vantagem 1", Math.Abs(EmbateDeKi.Vantagem(100, 100) - 1) < 1e-9);
		AfirmarEk("o dobro de poder vale o dobro por aperto",
				  Math.Abs(EmbateDeKi.Vantagem(200, 100) - 2) < 1e-9);
		AfirmarEk($"e ela para no teto ({EmbateDeKi.TetoDaVantagem}x), por maior que seja o abismo",
				  Math.Abs(EmbateDeKi.Vantagem(1e12, 1) - EmbateDeKi.TetoDaVantagem) < 1e-9);
		AfirmarEk("...e o lado fraco tem a vantagem INVERSA (senao a deriva nunca sairia de zero)",
				  Math.Abs(EmbateDeKi.Vantagem(1, 1e12) - 1 / EmbateDeKi.TetoDaVantagem) < 1e-9);

		// UM APERTO MOVE `BCL_PUSH_PER_PRESS`, vezes a vantagem.
		double m = EmbateDeKi.Empurrar(50, 1, 0, 1, 1, 0);
		AfirmarEk("um aperto move o medidor pelo `BCL_PUSH_PER_PRESS`",
				  Math.Abs(m - (50 + EmbateDeKi.EmpurraoPorAperto)) < 1e-9, $"{m:0.###}");

		// A DERIVA E POR CICLO DE 0,2 s -- e um tique de 1/30 s tem que render um SEXTO dela.
		double umTique = EmbateDeKi.Empurrar(50, 0, 0, 2, 1, Protocol.TickSeconds) - 50;
		double umCiclo = EmbateDeKi.Empurrar(50, 0, 0, 2, 1, EmbateDeKi.SegundosPorCiclo) - 50;
		AfirmarEk("a deriva e escalada pelo dt (copiar o 0,45 cru renderia 6x mais)",
				  Math.Abs(umTique - umCiclo * (Protocol.TickSeconds / EmbateDeKi.SegundosPorCiclo)) < 1e-9,
				  $"tique {umTique:0.####}, ciclo {umCiclo:0.####}");
		AfirmarEk("...e em forcas parelhas ela e EXATAMENTE zero",
				  Math.Abs(EmbateDeKi.Empurrar(50, 0, 0, 1, 1, 1) - 50) < 1e-12);

		// O DESFECHO.
		AfirmarEk("medidor cheio = A engole", EmbateDeKi.Decidir(100, 1) == FimDeEmbateDeKi.VenceuA);
		AfirmarEk("medidor zerado = B engole", EmbateDeKi.Decidir(0, 1) == FimDeEmbateDeKi.VenceuB);
		AfirmarEk("no meio, a disputa continua", EmbateDeKi.Decidir(50, 1) == FimDeEmbateDeKi.Continua);
		AfirmarEk($"e no prazo ({EmbateDeKi.SegundosMaximos}s) dentro da margem, EMPATE",
				  EmbateDeKi.Decidir(50, EmbateDeKi.SegundosMaximos) == FimDeEmbateDeKi.Empate);
		AfirmarEk("...mas fora da margem quem estava na frente leva",
				  EmbateDeKi.Decidir(50 + EmbateDeKi.MargemDoEmpate + 1, EmbateDeKi.SegundosMaximos)
					  == FimDeEmbateDeKi.VenceuA);

		// UMA LETRA VALE O QUE O MARTELAR DE UM NPC MEDIANO RENDE NUM CICLO DA CADENCIA -- a costura
		// entre o quick time event do ZanzoClash e o `side_presses` do DM.
		//
		// A CONTA E PELO PISO (`MsMinimoEntreLetras`) E NAO PELA JANELA, desde que o acerto passou a
		// ADIANTAR a proxima letra: o ritmo de quem joga no limite e o piso, e e o ritmo que a
		// promessa "jogador perfeito = NPC mediano" fala. Com a janela, quem acertasse tudo empurraria
		// tres vezes mais que a taxa do DM.
		double porLetra = EmbateDeKi.ApertosPorLetra(MsMinimoEntreLetras / 1000.0);
		double porSegundoMediano = EmbateDeKi.ApertosPorSegundo(EmbateDeKi.InteligenciaPadrao);
		AfirmarEk("uma letra vale o martelar de um NPC mediano durante um ciclo da cadencia minima",
				  Math.Abs(porLetra - porSegundoMediano * (MsMinimoEntreLetras / 1000.0)) < 1e-9,
				  $"{porLetra:0.###} apertos");

		// ...E A VAZAO DE QUEM JOGA NO LIMITE E A MESMA DA TAXA AUTOMATICA DO NPC MEDIANO. Esta e a
		// afirmacao que o adiantamento poderia ter quebrado calada: o valor da letra caiu pra um terco
		// E a cadencia triplicou, e o que tem que continuar de pe e o PRODUTO dos dois.
		AfirmarEk("quem acerta no limite da cadencia empurra o mesmo que o NPC mediano",
				  Math.Abs(porLetra / (MsMinimoEntreLetras / 1000.0) - porSegundoMediano) < 1e-9,
				  $"{porLetra / (MsMinimoEntreLetras / 1000.0):0.###} vs {porSegundoMediano:0.###} apertos/s");
		AfirmarEk("...e o NPC burro empurra menos que o esperto",
				  EmbateDeKi.ApertosPorSegundo(0) < EmbateDeKi.ApertosPorSegundo(100));

		// AS MAOS: o ponto de equilibrio e onde o corpo defletiria o tiro SEMPRE (100%).
		var f = new Fighter { Race = "Human", BP = 50_000 };
		f.Statify();
		f.Tick(agoraMs: NowMs());
		double maos = EmbateDeKi.PoderDeSegurar(f, bloqueando: false);
		double feixeQueEmpata = maos;
		double chance = DanoDeKi.ChanceDeDeflexao(f, feixeQueEmpata, 1, 1, bloqueando: false);
		AfirmarEk("o poder das MAOS empata com o feixe que o corpo defletiria 100% das vezes",
				  Math.Abs(chance - 100) < 1e-6, $"chance {chance:0.##}%");
		AfirmarEk("...e a guarda erguida DOBRA esse poder (como dobra a deflexao no DM)",
				  Math.Abs(EmbateDeKi.PoderDeSegurar(f, true) / maos - 2) < 1e-9);
	}

	// =====================================================================
	// 3) QUEM VENCE
	// =====================================================================
	/// <summary>
	/// O QUICK TIME EVENT DECIDE -- e a bancada mede QUANTO ele decide, que e a pergunta que o dono
	/// faria: *da pra ganhar de alguem mais forte?*
	///
	/// Ela roda tres encontros com a MESMA maquina de producao e so muda o poder do outro lado. O
	/// numero que sai (entre 2x e 6x o quick time event deixa de bastar) nao esta escrito em lugar
	/// nenhum do codigo: ele CAI das constantes do DM, e por isso vale medir.
	/// </summary>
	private void QuemVence()
	{
		GD.Print("[embateki] -- 3) QUEM VENCE: O QUE O QUICK TIME EVENT VALE, MEDIDO");

		AfirmarEk("em forcas parelhas, quem acerta as letras VENCE (contra quem nao reage)",
				  DisputarComLetras(poderDoOutro: 1.0) == DesfechoDeTeste.Venci);

		// O MAIS FORTE EMPURRA SOZINHO: a deriva passiva. Com os dois parados, o forte vence.
		AfirmarEk("com os dois parados, o mais forte engole sozinho (a deriva do DM)",
				  DisputarComLetras(poderDoOutro: 4.0, euApertando: false) == DesfechoDeTeste.Perdi);

		// A TAXA AUTOMATICA DA IA E DE VERDADE: um NPC no outro lado empurra sozinho, e um jogador
		// que so assiste perde pra ele mesmo em forcas parelhas. E a metade do `side_presses` que o
		// caso acima nao toca -- sem ela, um bug que zerasse a taxa da IA passaria verde.
		AfirmarEk("um NPC empurra sozinho e vence quem nao aperta nada",
				  DisputarComLetras(poderDoOutro: 1.0, euApertando: false, oOutroEBot: true) == DesfechoDeTeste.Perdi);

		// ...E O JOGADOR PERFEITO FICA LADO A LADO COM O NPC MEDIANO, que e o que `ApertosPorLetra`
		// promete: uma letra vale o que a taxa dele rende num ciclo da cadencia minima.
		//
		// A AFIRMACAO NAO E "EMPATA", e a diferenca importa. As duas escalas sao proximas mas nao
		// identicas por construcao (a letra e um degrau de 0,3 s; a taxa do NPC e continua), entao
		// exigir empate exato seria exigir que dois relogios diferentes marcassem o mesmo instante --
		// um teste que falha por arredondamento e nao por defeito. O que se afirma e que o medidor
		// NUNCA SAI DE PERTO DO MEIO: se uma das escalas estivesse errada por pouco que fosse, o
		// prazo inteiro de empurrao a levaria a uma das pontas.
		DisputarComLetras(1.0, euApertando: true, oOutroEBot: true, out double desvio, out _);
		AfirmarEk("o jogador que acerta TUDO fica lado a lado com o NPC mediano (as escalas batem)",
				  desvio < 20,
				  $"o medidor se afastou {desvio:0.#} pontos do meio em {EmbateDeKi.SegundosMaximos}s");

		// ============================ O QUE O TECLADO COMPRA, MEDIDO ============================
		// Nao ha um numero escolhido aqui. A bancada roda o MESMO encontro com desniveis crescentes e
		// imprime onde a vitoria vira -- e o que se afirma sao so as duas pontas, que sao promessas de
		// desenho: em forcas parelhas o teclado decide, e no teto da vantagem o poder decide.
		//
		// O ponto de virada CAI das constantes do DM (empurrao por aperto, deriva, teto) e do prazo de
		// 15 s; ele nao esta escrito em lugar nenhum, e por isso e uma medicao e nao uma repeticao.
		// ==================================================================================
		// A COLUNA DO MEIO E A QUE MANDA. O `bp` do rival e multiplicado pelo numero da esquerda, mas
		// o poder de um feixe (`BP * mods * basedamage`) sobe MAIS que o BP -- os atributos de ki
		// crescem junto --, entao o que decide a disputa e a vantagem MEDIDA, e nao o desnivel de BP.
		// Imprimir so a esquerda seria imprimir um numero que nao e o que o jogo usa.
		GD.Print("[embateki]       bp do rival | vantagem dele | desfecho de quem acerta TODAS as letras:");
		var escada = new List<(double Rival, DesfechoDeTeste Fim)>();
		foreach (double v in new[] { 1.0, 1.5, 1.75, 2.0, 3.0, 6.0 })
		{
			DesfechoDeTeste fim = DisputarComLetras(v, true, false, out _, out double vant);
			escada.Add((v, fim));
			GD.Print($"[embateki]        {v,10:0.##}x | {vant,13:0.##}x | {fim}");
		}

		AfirmarEk("em forcas parelhas o teclado decide", DisputarComLetras(1.0) == DesfechoDeTeste.Venci);
		AfirmarEk("...e no TETO da vantagem (6x) o poder decide, por mais rapido que voce seja",
				  DisputarComLetras(6.0) != DesfechoDeTeste.Venci);

		// ============================ A ESCADA INTEIRA, E ELA E MONOTONICA ============================
		// As duas linhas acima sao as PONTAS. O dono pediu a tabela inteira ("a tabela de 1x a 6x, com o
		// resultado de cada"), e o que se pode afirmar do MEIO dela sem cravar uma medicao no codigo e a
		// FORMA: **ficar mais forte nunca piora o desfecho do mais forte**. Venci > Empate > Perdi, e a
		// coluna so pode descer.
		//
		// Cravar "1,75x = Venci" seria escrever a medicao de hoje como se fosse regra, e a linha do meio
		// muda com qualquer recalibragem legitima (o valor da letra, o prazo, o teto da vantagem). A
		// monotonia nao muda: se ela quebrar, ha um desnivel de poder que e PIOR pro forte que um maior --
		// e isso e defeito em qualquer calibragem.
		// =============================================================================================
		bool desce = true;
		string trilha = "";
		for (int i = 1; i < escada.Count; i++)
		{
			desce &= Degrau(escada[i].Fim) <= Degrau(escada[i - 1].Fim);
			trilha += $"{escada[i - 1].Rival:0.##}x={escada[i - 1].Fim} ";
		}
		// E ELA TEM QUE DESCER DE FATO. So a monotonia nao basta, e isso foi MEDIDO: com o sinal da
		// deriva invertido a tabela sai `Venci` nas seis linhas -- monotonica, e com o poder valendo
		// zero. Exigir que a ponta de baixo seja PIOR que a de cima e o que separa "a escada existe" de
		// "a escada e uma reta".
		bool descedeVerdade = escada.Count == 6 && Degrau(escada[^1].Fim) < Degrau(escada[0].Fim);
		AfirmarEk("A ESCADA DE PODER: quanto mais forte o rival, nunca MELHOR pra quem acerta tudo "
				  + "(Venci >= Empate >= Perdi, a coluna so desce -- e desce mesmo)",
				  desce && descedeVerdade, trilha + $"{escada[^1].Rival:0.##}x={escada[^1].Fim}");

		SerRapidoPaga();
	}

	/// <summary>O desfecho como DEGRAU, pra a escada poder ser comparada. Ver a familia 3.</summary>
	private static int Degrau(DesfechoDeTeste f) => f switch
	{
		DesfechoDeTeste.Venci => 2,
		DesfechoDeTeste.Empate => 1,
		_ => 0,
	};

	/// <summary>
	/// ============================ CADA ACERTO EMPURRA O FEIXE DO INIMIGO -- EM PIXEL ============================
	/// O pedido do dono e sobre o MUNDO e nao sobre a barra: *"CADA ACERTO EMPURRA O BEAM DO INIMIGO PRA
	/// TRAS"*. Toda a familia 3 mede o DESFECHO (quem venceu), e um jogo em que o medidor decide tudo e o
	/// ponto de encontro fica parado no ar passaria verde nela inteira -- foi assim que o `MoverOEncontro`
	/// pode existir por camadas sem uma unica afirmacao apontada pra ele.
	///
	/// Entao aqui nao se olha placar nenhum. Olha-se **onde a cabeca do feixe esta**, antes e depois de
	/// UM acerto, e o que se exige e o que o dono descreveu com as maos: o meu ESTICA, o dele ENCOLHE, e
	/// o ponto anda pro lado dele.
	///
	/// O CONTROLE E O MESMO TEMPO SEM ACERTO. Em forcas parelhas a deriva e exatamente zero (afirmado na
	/// familia 2), entao dez tiques calados tem que deixar o encontro EXATAMENTE onde ele estava -- e e
	/// isso que separa "o acerto empurrou" de "o tempo empurrou".
	/// ==========================================================================================================
	/// </summary>
	private void CadaAcertoEmpurraOFeixe()
	{
		GD.Print("[embateki] -- 3b) CADA ACERTO EMPURRA O FEIXE DO INIMIGO (medido em PIXEL, nao no placar)");

		LimparEmbatesDaBancada();
		(ServerPlayer eu, ServerPlayer outro, DisputaDeKi? d) = DoisRaiosDeFrente(12);
		if (d == null) { AfirmarEk("a disputa comecou", false); return; }

		LadoDeKi meu = d.A.Quem == eu ? d.A : d.B;
		LadoDeKi dele = meu == d.A ? d.B : d.A;
		float sinal = meu == d.A ? 1 : -1;   // o `Eixo` vai de A pra B: pra mim, "pro inimigo" e este sinal

		// ---- O CONTROLE: dez tiques, nenhuma letra apertada ----
		Vec2 antesDoNada = d.Ponto;
		for (int i = 0; i < 10; i++) UmTiqueDoEncontro();
		float andouDeGraca = (d.Ponto - antesDoNada).Length;

		// ---- E AGORA UM ACERTO DE CADA VEZ ----
		float meuAntes = (meu.Feixe!.Pos - eu.Pos).Length;
		float deleAntes = (dele.Feixe!.Pos - outro.Pos).Length;

		var passos = new List<float>();
		for (int i = 0; i < 30 * 8 && passos.Count < 5 && _emEmbateDeKi.ContainsKey(eu.Id); i++)
		{
			if (meu.Letra == '\0') { UmTiqueDoEncontro(); continue; }

			Vec2 antes = d.Ponto;
			TeclaDeQualquerEmbate(eu, meu.Letra);
			UmTiqueDoEncontro();

			Vec2 andou = d.Ponto - antes;
			passos.Add((andou.X * d.Eixo.X + andou.Y * d.Eixo.Y) * sinal);
		}

		float meuDepois = (meu.Feixe.Pos - eu.Pos).Length;
		float deleDepois = (dele.Feixe.Pos - outro.Pos).Length;
		bool todosEmpurraram = passos.Count == 5 && passos.TrueForAll(p => p > 0.5f);
		double media = passos.Count > 0 ? passos.Average() : 0;

		AfirmarEk("sem acerto nenhum, o encontro NAO ANDA (a deriva em forcas parelhas e zero)",
				  andouDeGraca < 1f, $"{andouDeGraca:0.##} px em 10 tiques calados");
		AfirmarEk("...e CADA UM dos cinco acertos empurrou o encontro pro lado do inimigo",
				  todosEmpurraram,
				  passos.Count == 0 ? "nenhum acerto medido"
									: $"{string.Join(" | ", passos.ConvertAll(p => $"{p:0.#} px"))}");
		AfirmarEk($"...e o que ele empurra por acerto e da ordem do que a fisica promete ({media:0.#} px)",
				  media > 1, $"media {media:0.##} px por acerto");
		AfirmarEk("...e por isso o MEU feixe esticou e o DELE encolheu (o desenho sai da geometria)",
				  meuDepois > meuAntes + 4 && deleDepois < deleAntes - 4,
				  $"meu {meuAntes:0} -> {meuDepois:0} px | dele {deleAntes:0} -> {deleDepois:0} px");

		LimparEmbatesDaBancada();
	}

	/// <summary>Um tique do mundo, na ordem em que o jogo os chama. So pra nao repetir as tres linhas.</summary>
	private void UmTiqueDoEncontro()
	{
		TickDosCanaisDeKi(Protocol.TickSeconds);
		TickDosProjeteis(Protocol.TickSeconds);
		TickDosEmbatesDeKi(Protocol.TickSeconds);
	}

	/// <summary>
	/// ============================ ENCOSTAR NO INIMIGO **E** VENCER ============================
	/// A outra metade do pedido: *"ate o SEU BEAM ENCOSTAR NO INIMIGO, ai vc VENCE"*. Repare que ele nao
	/// descreve duas coisas em sequencia -- ele descreve UMA: encostar E vencer sao o mesmo instante.
	///
	/// E foi exatamente isso que a `folga` de um tile quebrava, sem quebrar teste nenhum: com ela, o
	/// medidor cheio parava a cabeca 32 px antes do corpo, e a vitoria era um numero chegando a 100
	/// enquanto o feixe ainda estava no ar. A familia 4 (que mede o encontro CAMINHANDO) ficava verde
	/// dos dois jeitos -- ela pergunta se ele andou, e nao se ele CHEGOU.
	///
	/// Entao aqui se mede a distancia do ponto de encontro ao CORPO do perdedor no ultimo instante da
	/// disputa, e se exige que ela seja zero. E que a vitoria tenha vindo do medidor CHEIO e nao do
	/// relogio -- senao a medida seria de outro desfecho.
	/// ======================================================================================================
	/// </summary>
	private void EncostarNoInimigoVence()
	{
		GD.Print("[embateki] -- 3c) ENCOSTAR NO INIMIGO E VENCER (o mesmo instante, medido no mesmo lugar)");

		LimparEmbatesDaBancada();
		(ServerPlayer eu, ServerPlayer outro, DisputaDeKi? d) = DoisRaiosDeFrente(12);
		if (d == null) { AfirmarEk("a disputa comecou", false); return; }

		LadoDeKi meu = d.A.Quem == eu ? d.A : d.B;
		bool souA = meu == d.A;

		float nasceuA = (d.Ponto - outro.Pos).Length;

		for (int i = 0; i < 30 * 40 && _emEmbateDeKi.ContainsKey(eu.Id); i++)
		{
			if (meu.Letra != '\0') TeclaDeQualquerEmbate(eu, meu.Letra);
			UmTiqueDoEncontro();
		}

		// ============================ A FOTO DO INSTANTE DA VITORIA MORA NO PROPRIO `d` ============================
		// A primeira versao guardava a distancia ANTES de cada tique, e por isso lia a posicao de um tique
		// ATRAS do desfecho -- ou seja o tamanho do ULTIMO PASSO, e nao a chegada. Ela passava verde por
		// motivo errado (o passo e pequeno) e ficava vermelha por motivo errado tambem: com a letra valendo
		// o triplo, o ultimo passo pulava 22 px e a linha reprovava um jogo geometricamente correto.
		//
		// O `Fechar` so tira a disputa das duas listas -- ele nao apaga o objeto. Entao `d.Ponto` DEPOIS do
		// laco e exatamente onde o `MoverOEncontro` deixou o encontro no tique que resolveu, com o medidor
		// ja cheio. E a leitura certa, e nao depende de o passo ser grande ou pequeno.
		// =========================================================================================================
		float chegou = (d.Ponto - outro.Pos).Length;
		double medidorNoFim = d.Medidor, corridos = d.Corridos;

		bool venciPeloMedidor = souA ? medidorNoFim >= 100 : medidorNoFim <= 0;
		AfirmarEk("venci pelo MEDIDOR CHEIO, e antes do prazo (senao a medida seria de outro desfecho)",
				  !_emEmbateDeKi.ContainsKey(eu.Id) && venciPeloMedidor
				  && corridos < EmbateDeKi.SegundosMaximos,
				  $"medidor {medidorNoFim:0.#} aos {corridos:0.#}s");
		AfirmarEk("...e no instante da vitoria o feixe estava ENCOSTADO no corpo do inimigo",
				  chegou <= 2, $"{nasceuA:0} px no comeco -> {chegou:0.##} px no fim");
		AfirmarEk("...e quem venceu fui eu: o canal na minha mao, o dele fechado",
				  _canais.ContainsKey(eu.Id) && !_canais.ContainsKey(outro.Id));

		LimparEmbatesDaBancada();
	}

	/// <summary>
	/// ============================ SER RAPIDO PAGA? A MEDICAO DO PEDIDO DO DONO ============================
	/// *"faca com q caso vc ACERTE o quick time event, ele JA VAI PRO PROXIMO BOTAO pra apertar, entao
	/// realmente QUANTO MAIS RAPIDO VC FOR MELHOR VAI SER"*.
	///
	/// Antes do adiantamento esta familia inteira daria a MESMA linha seis vezes: a proxima letra so
	/// nascia quando a janela de 0,9 s fechava, entao responder em 50 ms e responder em 850 ms rendia o
	/// mesmo empurrao no mesmo instante. **Um teste que so passa depois da mudanca e que so falha se
	/// alguem a desfizer** -- e por isso ele mede a REACAO e nao a mecanica.
	///
	/// O RIVAL E O NPC MEDIANO de mesmo poder, de proposito: ele e a REGUA da calibragem
	/// (`ApertosPorLetra`), entao a coluna do desfecho le direto -- em cima da regua, empata; abaixo
	/// dela, perde. Contra um alvo parado qualquer velocidade venceria e a tabela nao diria nada.
	/// ==================================================================================================
	/// </summary>
	private void SerRapidoPaga()
	{
		GD.Print("[embateki]       reacao do jogador | desfecho contra um NPC mediano de MESMO poder:");
		foreach (double ms in new[] { 0, 150.0, 300, 450, 600, 900 })
		{
			DesfechoDeTeste fim = DisputarComLetras(1.0, true, true, out double desvio, out _, ms / 1000.0);
			GD.Print($"[embateki]        {ms,10:0} ms | {fim} (o medidor se afastou {desvio:0.#} do meio)");
		}

		// AS DUAS PONTAS, que sao promessas de desenho e nao numeros escolhidos: quem joga NO LIMITE da
		// cadencia sustenta o empate (a calibragem), e quem responde ao DOBRO dela recebe metade das
		// letras e perde. O meio da tabela e o que se le, e ele nao precisa de afirmacao.
		AfirmarEk("quem responde no limite da cadencia sustenta o empate contra o NPC mediano",
				  DisputarComLetras(1.0, true, true, out _, out _, MsMinimoEntreLetras / 1000.0)
					  != DesfechoDeTeste.Perdi);
		AfirmarEk("...e quem responde ao DOBRO da cadencia PERDE -- ser rapido paga, e paga em vitoria",
				  DisputarComLetras(1.0, true, true, out _, out _, 2.0 * MsMinimoEntreLetras / 1000.0)
					  == DesfechoDeTeste.Perdi);
	}

	/// <summary>Como um encontro da bancada terminou. Ver <see cref="DisputarComLetras"/>.</summary>
	private enum DesfechoDeTeste { NaoComecou, Venci, Perdi, Empate }

	/// <summary>
	/// Um encontro completo, do disparo ao desfecho, so com chamadas de producao.
	///
	/// COMO SE LE QUEM VENCEU: pelo ESTADO do jogo, e nao por um campo que a disputa tenha deixado
	/// pra bancada. Quem venceu continua com o canal na mao; quem perdeu teve o dele fechado
	/// (`L.stopbeaming()`); no empate os dois fecham (`draw()`).
	/// </summary>
	private DesfechoDeTeste DisputarComLetras(double poderDoOutro, bool euApertando = true,
											  bool oOutroEBot = false)
		=> DisputarComLetras(poderDoOutro, euApertando, oOutroEBot, out _, out _);

	/// <summary>
	/// A mesma coisa, dizendo tambem O QUANTO o medidor se afastou do meio -- o que separa "empatou"
	/// de "empatou por sorte, depois de quase acabar".
	/// </summary>
	/// <param name="segundosDeReacao">
	/// Quanto tempo a letra fica na tela antes de eu apertar. ZERO e o jogador impossivel (responde no
	/// mesmo tique em que a letra nasce); e o que esta bancada sempre mediu. Ver <see cref="SerRapidoPaga"/>.
	/// </param>
	private DesfechoDeTeste DisputarComLetras(double poderDoOutro, bool euApertando,
											  bool oOutroEBot, out double desvio, out double vantagemDele,
											  double segundosDeReacao = 0)
	{
		desvio = 0;
		vantagemDele = 1;
		LimparEmbatesDaBancada();

		(ServerPlayer eu, ServerPlayer outro, DisputaDeKi? d) =
			DoisRaiosDeFrente(12, bpDoSegundo: 50_000 * poderDoOutro, tecladoNoSegundo: !oOutroEBot);
		if (d == null) return DesfechoDeTeste.NaoComecou;

		LadoDeKi meuLado = d.A.Quem == eu ? d.A : d.B;
		vantagemDele = (meuLado == d.A ? d.B : d.A).Vantagem;
		var fim = DesfechoDeTeste.NaoComecou;
		bool acabou = false;

		// O LACO E O DO JOGO: tique de projetil + tique de embate. A unica coisa que a bancada faz e
		// apertar a letra certa depois de olhar pra ela pelo tempo de reacao pedido -- com reacao zero,
		// que e o padrao, isso e o jogador perfeito de sempre.
		//
		// O RELOGIO ZERA QUANDO NAO HA LETRA (`Letra == '\0'`), e nao na troca de caractere: duas letras
		// seguidas podem sair iguais, e comparar o caractere faria a segunda herdar o tempo da primeira.
		double naTela = 0;
		for (int i = 0; i < 30 * 40 && !acabou; i++)
		{
			if (meuLado.Letra == '\0') naTela = 0;
			else if (euApertando && naTela >= segundosDeReacao) TeclaDeQualquerEmbate(eu, meuLado.Letra);
			else naTela += Protocol.TickSeconds;

			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
			TickDosEmbatesDeKi(Protocol.TickSeconds);

			desvio = Math.Max(desvio, Math.Abs(d.Medidor - EmbateDeKi.MedidorInicial));
			if (_emEmbateDeKi.ContainsKey(eu.Id)) continue;
			acabou = true;
			bool meuCanal = _canais.ContainsKey(eu.Id), oDele = _canais.ContainsKey(outro.Id);
			fim = meuCanal && !oDele ? DesfechoDeTeste.Venci
				: oDele && !meuCanal ? DesfechoDeTeste.Perdi
				: DesfechoDeTeste.Empate;
		}

		LimparEmbatesDaBancada();
		return acabou ? fim : DesfechoDeTeste.NaoComecou;
	}

	// =====================================================================
	// 4) O FIM E ASSIMETRICO
	// =====================================================================
	/// <summary>
	/// O pedido do dono, literal: *"o ki EMPURRANDO o outro ate atingir o inimigo ou vc"*. Entao a
	/// afirmacao nao pode ser "a disputa acabou": tem que ser **o perdedor perdeu vida**, e por
	/// dentro do funil de dano de producao.
	/// </summary>
	private void OFimEAssimetrico()
	{
		GD.Print("[embateki] -- 4) QUEM PERDE LEVA O FEIXE");

		LimparEmbatesDaBancada();
		// O OUTRO TEM TECLADO E NAO APERTA: e a unica forma de haver vencedor em forcas parelhas --
		// ver a nota em `DoisRaiosDeFrente` sobre por que dois lados que empurram igual empatam.
		(ServerPlayer eu, ServerPlayer outro, DisputaDeKi? d) = DoisRaiosDeFrente(10);
		if (d == null) { AfirmarEk("a disputa comecou", false); return; }

		double vidaAntes = outro.Combate.Corpo.Vida();
		LadoDeKi meuLado = d.A.Quem == eu ? d.A : d.B;

		// O ENCONTRO CAMINHA: guardo onde ele estava pra afirmar que ele ANDOU pro lado do perdedor.
		Vec2 nasceuEm = d.Ponto;
		float distanciaInicial = (d.Ponto - outro.Pos).Length;
		float maisPerto = distanciaInicial;

		bool morreuOFeixeDele = false;
		for (int i = 0; i < 30 * 45; i++)
		{
			if (meuLado.Letra != '\0') TeclaDeQualquerEmbate(eu, meuLado.Letra);

			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
			TickDosEmbatesDeKi(Protocol.TickSeconds);

			if (_emEmbateDeKi.ContainsKey(eu.Id))
			{
				maisPerto = Math.Min(maisPerto, (d.Ponto - outro.Pos).Length);
				continue;
			}
			morreuOFeixeDele |= d.B.Feixe is { Vivo: false } || d.A.Feixe is { Vivo: false };
			if (outro.Combate.Corpo.Vida() < vidaAntes) break;
		}

		AfirmarEk("o ponto de encontro ANDOU pro lado de quem estava perdendo",
				  maisPerto < distanciaInicial - 8,
				  $"nasceu a {distanciaInicial:0} px do perdedor, chegou a {maisPerto:0} px (de {nasceuEm})");
		AfirmarEk("o feixe do perdedor MORREU e o canal dele caiu",
				  morreuOFeixeDele && !_canais.ContainsKey(outro.Id));
		AfirmarEk("o feixe do vencedor CONTINUOU vivo e livre pra avancar",
				  d.A.Feixe is { Vivo: true, EmEmbate: false } || d.B.Feixe is { Vivo: true, EmEmbate: false });
		AfirmarEk("...e o perdedor LEVOU o ataque: perdeu vida pelo funil de dano de ki",
				  outro.Combate.Corpo.Vida() < vidaAntes,
				  $"{vidaAntes:0.##} -> {outro.Combate.Corpo.Vida():0.##}");
		AfirmarEk("...e o credito ficou com quem venceu (Zenkai/luto/raiva tem algoz)",
				  outro.UltimoAgressor == eu.Id, $"agressor {outro.UltimoAgressor} vs eu {eu.Id}");

		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 5) A SAIDA
	// =====================================================================
	/// <summary>
	/// `if(!M.beaming) return 0` do `side_ok`: soltar o raio ENCERRA o seu lado. A pergunta do dono
	/// era *"da pra sair do embate?"* -- da, e a resposta e "sim, e sair e a jogada perdedora".
	/// </summary>
	private void SairDoEmbateEPerder()
	{
		GD.Print("[embateki] -- 5) DA PRA SAIR, E SAIR E PERDER");

		LimparEmbatesDaBancada();
		(ServerPlayer eu, ServerPlayer outro, DisputaDeKi? d) = DoisRaiosDeFrente(12);
		if (d == null) { AfirmarEk("a disputa comecou", false); return; }

		// EU SOLTO O RAIO -- pelo verb de producao, o mesmo que o jogador aperta.
		SoltarCanal(eu, "Ki_Wave");
		TickDosEmbatesDeKi(Protocol.TickSeconds);

		AfirmarEk("soltar o raio ENCERRA a disputa na hora", !_emEmbateDeKi.ContainsKey(eu.Id));
		AfirmarEk("...e quem soltou PERDEU: o feixe dele morreu",
				  (d.A.Quem == eu ? d.A : d.B).Feixe is { Vivo: false });
		AfirmarEk("...e o feixe do outro ficou livre pra avancar",
				  (d.A.Quem == outro ? d.A : d.B).Feixe is { Vivo: true, EmEmbate: false });

		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 5b) O EMPATE COBRA
	// =====================================================================
	/// <summary>
	/// O EMPATE TEM QUE DOER NOS DOIS -- o pedido do dono, literal: *"se NINGUEM VENCER em 15
	/// segundos, acontece uma EXPLOSAO, e AMBOS os jogadores sofrem um DANO e sao JOGADOS PRA TRAS
	/// pela ONDA DE CHOQUE e o duelo acaba ai em EMPATE"*.
	///
	/// ============================ POR QUE ESTA FAMILIA PRECISOU EXISTIR ============================
	/// O empate ja existia e ja ESTOURAVA -- `Estourar(forte: true)` racha o chao, derruba cenario e
	/// manda o baque pra tela. Ou seja: a cena inteira ja estava certa, e era exatamente por isso que
	/// **ninguem percebia que ela nao cobrava nada**. Nenhuma afirmacao olhava pra vida nem pra
	/// posicao dos dois, entao 15 segundos de disputa parelha terminavam num efeito bonito e num
	/// placar zerado, e a bancada ficava verde.
	///
	/// As duas afirmacoes abaixo sao o contrario disso: elas NAO olham pro efeito. Olham pro que o
	/// jogador leva pra casa -- vida a menos e o corpo fora do lugar --, que sao as duas coisas que o
	/// dono pediu e as duas que o `draw()` do DM de fato faz (`spawn headA.explode()`).
	///
	/// O ARREMESSO SE MEDE PELO CONTADOR, e nao pelo `TiquesDeVoo`: o voo pode COMECAR E ACABAR no
	/// mesmo tique quando ha parede por perto, e a `--kbteste` ja perdeu uma classe inteira de
	/// arremesso por olhar o campo em vez do contador. Ver o comentario do `_arremessosFeitos`.
	/// ==============================================================================================
	/// </summary>
	private void OEmpateCobra()
	{
		GD.Print("[embateki] -- 5b) O EMPATE COBRA DOS DOIS");

		LimparEmbatesDaBancada();

		// FORCAS PARELHAS E NINGUEM APERTANDO: a deriva e exatamente zero (afirmado na familia 2), o
		// medidor nao sai de 50 e a disputa so pode acabar pelo PRAZO. E o empate de manual.
		//
		// A `folgaAtras` E DESTA FAMILIA: e a unica que mede o que sai VOANDO do encontro, e sem chao
		// livre atras dos dois o que se mediria era o `PontoLivrePerto` do pouso. Ver o parametro.
		(ServerPlayer eu, ServerPlayer outro, DisputaDeKi? d) = DoisRaiosDeFrente(10, folgaAtras: 6);
		if (d == null) { AfirmarEk("a disputa comecou", false); return; }

		double vidaMinha = eu.Combate.Corpo.Vida();
		double vidaDele = outro.Combate.Corpo.Vida();
		Vec2 euAntes = eu.Pos, eleAntes = outro.Pos;
		long arremessosAntes = _arremessosFeitos;

		// 15 s + folga, sem NINGUEM apertar letra nenhuma.
		int tiques = 0;
		for (int i = 0; i < 30 * 20 && _emEmbateDeKi.ContainsKey(eu.Id); i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
			TickDosEmbatesDeKi(Protocol.TickSeconds);
			tiques++;
		}

		double corridos = tiques * Protocol.TickSeconds;
		AfirmarEk($"sem ninguem apertar, a disputa vai ate o PRAZO e empata ({EmbateDeKi.SegundosMaximos}s)",
				  !_emEmbateDeKi.ContainsKey(eu.Id)
				  && Math.Abs(corridos - EmbateDeKi.SegundosMaximos) < 1.5
				  && Math.Abs(d.Medidor - EmbateDeKi.MedidorInicial) <= EmbateDeKi.MargemDoEmpate,
				  $"{corridos:0.#}s, medidor {d.Medidor:0.#}");

		AfirmarEk("...e OS DOIS sofrem dano (a explosao do `draw()`, que nao cobrava nada)",
				  eu.Combate.Corpo.Vida() < vidaMinha && outro.Combate.Corpo.Vida() < vidaDele,
				  $"eu {vidaMinha:0.##} -> {eu.Combate.Corpo.Vida():0.##} | "
				  + $"ele {vidaDele:0.##} -> {outro.Combate.Corpo.Vida():0.##}");

		AfirmarEk("...e OS DOIS sao jogados pra tras pela onda de choque",
				  _arremessosFeitos - arremessosAntes >= 2,
				  $"{_arremessosFeitos - arremessosAntes} arremessos | "
				  + $"eu andei {(eu.Pos - euAntes).Length:0} px, ele {(outro.Pos - eleAntes).Length:0} px");

		// ============================ O VOO SO ACONTECE SE ALGUEM TIQUEAR ============================
		// O empate cai no ULTIMO tique do laco acima -- o laco para justamente porque a disputa saiu do
		// `_emEmbateDeKi`. Nesse instante os dois ja tem `TiquesDeVoo`, e nenhum deles ANDOU ainda:
		// medir a posicao aqui devolvia "voaram 0 e 0 tiles", e essa leitura seria uma mentira sobre o
		// sistema (o arremesso estava certo; quem nao tinha rodado era o relogio dele).
		//
		// Entao o voo e ESCOADO aqui, com o mesmo `TickDoEmpurrao` que a `--kbteste` usa -- e e ele
		// que faz o corpo passar pelo `MoveRules`, ou seja pela colisao: quem tem parede atras para
		// nela em vez de atravessa-la.
		// ============================================================================================
		int giros = 0;
		while ((eu.TiquesDeVoo > 0 || outro.TiquesDeVoo > 0) && giros++ < 300) TickDoEmpurrao();

		Vec2 fugaMinha = eu.Pos - euAntes, fugaDele = outro.Pos - eleAntes;
		bool andaram = fugaMinha.LengthSquared > 1e-4f && fugaDele.LengthSquared > 1e-4f;
		double cosDasFugas = 0;
		if (andaram)
		{
			Vec2 a = fugaMinha.Normalized(), b = fugaDele.Normalized();
			cosDasFugas = a.X * b.X + a.Y * b.Y;
		}
		AfirmarEk("...e CADA UM PRO SEU LADO (a onda empurra pra fora do ponto de encontro)",
				  andaram && cosDasFugas < 0,
				  $"eu {fugaMinha.Length:0} px, ele {fugaDele.Length:0} px, cos {cosDasFugas:0.##}");

		// E O PRECO IMPRESSO, porque "passou" nao e um numero. O dono perguntou QUANTO a explosao
		// cobra, e a resposta so e conferivel se ela sair escrita ao lado das outras tabelas desta
		// bancada -- do contrario o unico jeito de saber seria instrumentar o servidor de novo.
		GD.Print($"[embateki]       o preco do empate: vida {vidaMinha:0.#} -> {eu.Combate.Corpo.Vida():0.#} "
				 + $"e {vidaDele:0.#} -> {outro.Combate.Corpo.Vida():0.#} | "
				 + $"voaram {fugaMinha.Length / ZoneCollision.TileSize:0.#} e "
				 + $"{fugaDele.Length / ZoneCollision.TileSize:0.#} tiles");

		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 5c) O CONTRA-EXEMPLO DO EMPATE
	// =====================================================================
	/// <summary>
	/// ============================ QUEM VENCE ANTES **NAO** EXPLODE ============================
	/// A familia 5b prova que o empate cobra. Sozinha, ela nao distingue *"a explosao acontece quando
	/// ninguem vence em 15 s"* de *"a explosao acontece no fim de toda disputa"* -- e a segunda seria um
	/// defeito grave e invisivel: o vencedor levaria dano e voaria pra tras junto com o perdedor, e o
	/// pedido do dono (*"AMBOS os jogadores sofrem um DANO"*) fala do EMPATE, e so dele.
	///
	/// Entao esta e a mesma cena com a mao ligada: eu acerto tudo, venco antes do prazo, e o que se exige
	/// e um placar VAZIO do meu lado -- vida intacta, contador de arremessos parado, nenhum tique de voo.
	/// O perdedor leva, mas leva do FEIXE (familia 4), que e outra coisa.
	/// ====================================================================================================
	/// </summary>
	private void VencerAntesNaoExplode()
	{
		GD.Print("[embateki] -- 5c) O CONTRA-EXEMPLO: QUEM VENCE ANTES DO PRAZO NAO EXPLODE");

		LimparEmbatesDaBancada();

		// O MESMO BERCO DA 5b (`folgaAtras`), de proposito: se o chao de tras nao fosse igual, "ninguem
		// voou" poderia ser "nao havia pra onde voar" em vez de "nao houve onda de choque".
		(ServerPlayer eu, ServerPlayer outro, DisputaDeKi? d) = DoisRaiosDeFrente(10, folgaAtras: 6);
		if (d == null) { AfirmarEk("a disputa comecou", false); return; }

		LadoDeKi meu = d.A.Quem == eu ? d.A : d.B;
		double vidaMinha = eu.Combate.Corpo.Vida();
		long arremessosAntes = _arremessosFeitos;
		double corridos = 0;

		for (int i = 0; i < 30 * 40 && _emEmbateDeKi.ContainsKey(eu.Id); i++)
		{
			if (meu.Letra != '\0') TeclaDeQualquerEmbate(eu, meu.Letra);
			corridos = d.Corridos;
			UmTiqueDoEncontro();
		}

		AfirmarEk("a disputa terminou ANTES do prazo (senao este contra-exemplo mediria um empate)",
				  !_emEmbateDeKi.ContainsKey(eu.Id) && corridos < EmbateDeKi.SegundosMaximos - 1,
				  $"{corridos:0.#}s de {EmbateDeKi.SegundosMaximos}");
		AfirmarEk("...e o VENCEDOR nao perdeu um ponto de vida (nao houve explosao pra ele)",
				  Math.Abs(eu.Combate.Corpo.Vida() - vidaMinha) < 1e-9,
				  $"{vidaMinha:0.###} -> {eu.Combate.Corpo.Vida():0.###}");
		AfirmarEk("...e NINGUEM foi jogado pra tras: a onda de choque e do empate, e so dele",
				  _arremessosFeitos == arremessosAntes && eu.TiquesDeVoo == 0 && outro.TiquesDeVoo == 0,
				  $"{_arremessosFeitos - arremessosAntes} arremesso(s), voo {eu.TiquesDeVoo}/{outro.TiquesDeVoo}");

		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 5d) AS BORDAS
	// =====================================================================
	/// <summary>
	/// ============================ NINGUEM FICA PRESO, E NENHUM FEIXE FICA ORFAO ============================
	/// A disputa PLANTA os pes dos dois (familia 7) e CONGELA as duas cabecas (`EmEmbate`, que tira do
	/// `AndarProjetil` quem manda nelas). As duas coisas sao boas enquanto ela corre e sao **defeito** um
	/// tique depois: um corpo que nao anda mais e um jogador perdido, e uma cabeca congelada e um feixe
	/// parado no ar pra sempre, que ninguem alimenta e nada mata.
	///
	/// A familia 7 ja mede a saida por SOLTAR O RAIO. Faltavam as tres bordas que ninguem escolhe:
	///   (a) o PRAZO -- o empate, que fecha os dois canais e mata as duas cabecas;
	///   (b) SAIR DO JOGO -- o `SoltarDoEmbateDeKi`, a mesma funcao que o logout e a troca de zona
	///       chamam (`GameServer.cs`), e que resolve a disputa a favor de quem ficou;
	///   (c) O KI ACABAR no meio -- o `Ki <= 1` do `LadoOk`, que e a unica saida que acontece sozinha.
	///
	/// A LEITURA DE "ORFAO" E UMA SO, e ela e do estado e nao da lista: nenhum projetil da zona pode
	/// continuar com `EmEmbate` de pe depois que a disputa morreu. Contar disputas nao acharia isso --
	/// a lista pode estar vazia com duas cabecas congeladas nela.
	/// ==================================================================================================
	/// </summary>
	private void AsBordasNaoPrendemNinguem()
	{
		GD.Print("[embateki] -- 5d) AS BORDAS: ninguem preso, nenhum feixe orfao");

		// ---- (a) O PRAZO: o empate solta os dois ----
		LimparEmbatesDaBancada();
		(ServerPlayer eu, ServerPlayer outro, DisputaDeKi? d) = DoisRaiosDeFrente(10, folgaAtras: 6);
		if (d == null) { AfirmarEk("a disputa do empate comecou", false); return; }

		for (int i = 0; i < 30 * 20 && _emEmbateDeKi.ContainsKey(eu.Id); i++) UmTiqueDoEncontro();

		// O VOO E ESCOADO ANTES DE PERGUNTAR SE ELES ANDAM: ser arremessado tambem tira o passo do
		// jogador (e tem que tirar), entao medir com o corpo no ar mediria o arremesso, nao a disputa.
		int giros = 0;
		while ((eu.TiquesDeVoo > 0 || outro.TiquesDeVoo > 0) && giros++ < 300) TickDoEmpurrao();

		AfirmarEk("depois do EMPATE os dois saem da disputa e os dois canais fecham",
				  !_emEmbateDeKi.ContainsKey(eu.Id) && !_emEmbateDeKi.ContainsKey(outro.Id)
				  && _disputas.Count == 0
				  && !_canais.ContainsKey(eu.Id) && !_canais.ContainsKey(outro.Id));
		AfirmarEk("...e os DOIS voltam a andar (o `PodeMexerOCorpo` deixou de recusar)",
				  PodeMexerOCorpo(eu) && PodeMexerOCorpo(outro));
		AfirmarEk("...e nao sobrou feixe ORFAO: nenhuma cabeca da zona continua congelada em embate",
				  CabecasCongeladas(eu) == 0, $"{CabecasCongeladas(eu)} cabeca(s) com `EmEmbate` de pe");

		// ---- (b) SAIR DO JOGO no meio: o funil do logout e da troca de zona ----
		LimparEmbatesDaBancada();
		(ServerPlayer some, ServerPlayer fica, DisputaDeKi? d2) = DoisRaiosDeFrente(12);
		if (d2 == null) { AfirmarEk("a disputa da saida comecou", false); return; }

		SoltarDoEmbateDeKi(some.Id);   // a MESMA chamada do logout (`GameServer.cs`)
		TickDosEmbatesDeKi(Protocol.TickSeconds);

		AfirmarEk("quem SAI DO JOGO no meio nao deixa a disputa de pe (nem pra ele nem pro outro)",
				  !_emEmbateDeKi.ContainsKey(some.Id) && !_emEmbateDeKi.ContainsKey(fica.Id)
				  && _disputas.Count == 0);
		AfirmarEk("...e o corpo de quem saiu esta LIVRE, e o feixe de quem ficou esta solto e vivo",
				  PodeMexerOCorpo(some)
				  && (d2.A.Quem == fica ? d2.A : d2.B).Feixe is { Vivo: true, EmEmbate: false },
				  $"livre={PodeMexerOCorpo(some)}");
		AfirmarEk("...e nenhuma cabeca ficou congelada", CabecasCongeladas(fica) == 0);

		// ---- (c) O KI ACABAR: a unica saida que acontece sozinha (`LadoOk`) ----
		LimparEmbatesDaBancada();
		(ServerPlayer seco, ServerPlayer cheio, DisputaDeKi? d3) = DoisRaiosDeFrente(12);
		if (d3 == null) { AfirmarEk("a disputa do Ki comecou", false); return; }

		seco.Ficha.Ki = 0;
		TickDosEmbatesDeKi(Protocol.TickSeconds);

		AfirmarEk("quem fica SEM KI no meio perde a firmeza, e a disputa se desfaz nos dois lados",
				  !_emEmbateDeKi.ContainsKey(seco.Id) && !_emEmbateDeKi.ContainsKey(cheio.Id)
				  && _disputas.Count == 0);
		AfirmarEk("...e ninguem fica plantado nem congelado depois disso",
				  PodeMexerOCorpo(seco) && CabecasCongeladas(cheio) == 0);

		LimparEmbatesDaBancada();
	}

	/// <summary>
	/// QUANTAS CABECAS DA ZONA DESTE CORPO CONTINUAM COM `EmEmbate` DE PE -- a leitura de "feixe orfao".
	///
	/// Ela olha o BIT e nao a lista de disputas de proposito: e o `EmEmbate` que tira a cabeca do
	/// `AndarProjetil`, entao uma cabeca viva com ele levantado e um feixe parado no ar sem dono do
	/// movimento -- exatamente o que sobra se alguem fechar a disputa esquecendo de baixa-lo.
	/// </summary>
	private int CabecasCongeladas(ServerPlayer perto)
	{
		int n = 0;
		foreach (Projetil p in ProjeteisDaZona(perto.Zone.Hash))
			if (p.Vivo && p.EmEmbate) n++;
		return n;
	}

	// =====================================================================
	// 6) FEIXE CONTRA GUARDA
	// =====================================================================
	/// <summary>
	/// O que e NOVO nesta camada. Duas afirmacoes, e a segunda e a que importa: quem segura e vence
	/// DEVOLVE o ataque -- e ele acerta quem atirou, que e a metade do pedido do dono ("...ate
	/// atingir o inimigo **ou vc**").
	/// </summary>
	private void OFeixeContraAGuarda()
	{
		GD.Print("[embateki] -- 6) O FEIXE CONTRA QUEM BLOQUEIA");

		LimparEmbatesDaBancada();

		// ============================ O SORTEIO DE DEFLEXAO TEM QUE SAIR DO CAMINHO ============================
		// A chance de defletir E a razao entre as duas forcas (`objects.dm:333`), a mesma que decide a
		// disputa. Um defensor forte o bastante pra VENCER o embate tem, por construcao, chance de
		// deflexao acima de 100% -- ele apararia o tiro antes de chegar aqui. A primeira versao desta
		// familia reprovava exatamente assim, e o motivo nao era o embate: era a defesa funcionando.
		//
		// Entao o raio sai NAO-DEFLETIVEL (`deflectable = 0` do DM -- ver `ReceitaDeProjetil`), que e
		// um campo de producao e existe justamente pra tecnicas que *"nao ha o que defletir, so
		// aguentar"*. O que se isola aqui e o EMBATE; a ordem entre ele e o sorteio tem afirmacao
		// propria logo abaixo.
		// =================================================================================================
		ReceitaDeProjetil semDeflexao = RaioDeTeste();
		semDeflexao.Deflectivel = false;

		Vec2 chao = CorredorLivre(20);
		ServerPlayer atirador = Forjar("Agressor", chao, bp: 5_000);
		atirador.Facing = Facing.East;
		atirador.Ficha.Ki = atirador.Ficha.MaxKi;
		ServerPlayer guarda = Forjar("Muralha", new Vec2(chao.X + 8 * ZoneCollision.TileSize, chao.Y), bp: 5_000_000);
		guarda.Ficha.Ki = guarda.Ficha.MaxKi;
		guarda.Combate.Guardar(true);
		_comTecladoDeTeste.Add(guarda.Id);

		Canalizar(atirador, "Ki_Wave", 10 * atirador.Ficha.BaseDrain(), semDeflexao);
		DisputaDeKi? d = null;
		for (int i = 0; i < 30 * 8 && d == null; i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
			TickDosEmbatesDeKi(Protocol.TickSeconds);
			d = _emEmbateDeKi.GetValueOrDefault(guarda.Id);
		}

		AfirmarEk("um raio contra quem esta de GUARDA vira embate", d != null);
		if (d == null) { LimparEmbatesDaBancada(); return; }

		AfirmarEk("...e o lado da guarda nao tem feixe nenhum (sao as MAOS dele)", d.B.DeMaos);
		AfirmarEk("...e ele foi PLANTADO no lugar, mesmo sem canal de ki", !PodeMexerOCorpo(guarda));

		double vidaDoAtirador = atirador.Combate.Corpo.Vida();
		Projetil feixe = d.A.Feixe!;
		bool acabou = false;
		for (int i = 0; i < 30 * 45 && !acabou; i++)
		{
			if (d.B.Letra != '\0') TeclaDeQualquerEmbate(guarda, d.B.Letra);
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
			TickDosEmbatesDeKi(Protocol.TickSeconds);
			acabou = !_emEmbateDeKi.ContainsKey(guarda.Id);
		}

		AfirmarEk("a guarda VENCEU e o ataque mudou de dono (o `avoidusr` passa a proteger ELE)",
				  feixe.Dono == guarda.Id, $"dono {feixe.Dono}, guarda {guarda.Id}");
		AfirmarEk("...e o canal de quem atirou caiu", !_canais.ContainsKey(atirador.Id));

		for (int i = 0; i < 30 * 10 && atirador.Combate.Corpo.Vida() >= vidaDoAtirador; i++)
			TickDosProjeteis(Protocol.TickSeconds);

		AfirmarEk("...e o ataque DEVOLVIDO acertou quem o disparou",
				  atirador.Combate.Corpo.Vida() < vidaDoAtirador,
				  $"{vidaDoAtirador:0.##} -> {atirador.Combate.Corpo.Vida():0.##}");

		LimparEmbatesDaBancada();

		// A OUTRA SAIDA: abaixar a guarda no meio e o equivalente a soltar o raio.
		Vec2 chao2 = CorredorLivre(20);
		ServerPlayer a2 = Forjar("Agressor2", chao2, bp: 5_000_000);
		a2.Facing = Facing.East;
		a2.Ficha.Ki = a2.Ficha.MaxKi;
		ServerPlayer g2 = Forjar("Covarde", new Vec2(chao2.X + 8 * ZoneCollision.TileSize, chao2.Y), bp: 5_000);
		g2.Ficha.Ki = g2.Ficha.MaxKi;
		g2.Combate.Guardar(true);

		Canalizar(a2, "Ki_Wave", 10 * a2.Ficha.BaseDrain(), semDeflexao);
		for (int i = 0; i < 30 * 8 && !_emEmbateDeKi.ContainsKey(g2.Id); i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
			TickDosEmbatesDeKi(Protocol.TickSeconds);
		}
		bool comecou = _emEmbateDeKi.ContainsKey(g2.Id);
		g2.Combate.Guardar(false);
		TickDosEmbatesDeKi(Protocol.TickSeconds);
		AfirmarEk("abaixar a guarda no meio encerra o embate (a saida deste lado)",
				  comecou && !_emEmbateDeKi.ContainsKey(g2.Id));

		LimparEmbatesDaBancada();

		// A ORDEM: QUEM TEM SORTE DEFENDE DE GRACA. O mesmo defensor gigante, agora contra um raio
		// COMUM: a deflexao (que compara as mesmas duas forcas) resolve antes, e nao ha embate
		// nenhum. Sem esta afirmacao, alguem poderia mover o gatilho pra antes do sorteio e apagar a
		// defesa de ki do jogo inteiro sem nenhum teste reclamar.
		Vec2 chao3 = CorredorLivre(20);
		ServerPlayer a3 = Forjar("Agressor3", chao3, bp: 5_000);
		a3.Facing = Facing.East;
		a3.Ficha.Ki = a3.Ficha.MaxKi;
		ServerPlayer g3 = Forjar("Fortaleza", new Vec2(chao3.X + 8 * ZoneCollision.TileSize, chao3.Y), bp: 5_000_000);
		g3.Ficha.Ki = g3.Ficha.MaxKi;
		g3.Combate.Guardar(true);

		Canalizar(a3, "Ki_Wave", 10 * a3.Ficha.BaseDrain(), RaioDeTeste());   // DEFLETIVEL
		bool virouEmbate = false;
		for (int i = 0; i < 30 * 8 && !virouEmbate; i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
			TickDosEmbatesDeKi(Protocol.TickSeconds);
			virouEmbate = _emEmbateDeKi.ContainsKey(g3.Id);
		}
		AfirmarEk("contra um raio COMUM, quem podia defletir defende de graca (o sorteio vem antes)",
				  !virouEmbate);

		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 7) O CORPO PRESO
	// =====================================================================
	private void OCorpoFicaPreso()
	{
		GD.Print("[embateki] -- 7) OS DOIS FICAM PLANTADOS, PELO FUNIL DE SEMPRE");

		LimparEmbatesDaBancada();
		(ServerPlayer a, ServerPlayer b, DisputaDeKi? d) = DoisRaiosDeFrente(12);
		if (d == null) { AfirmarEk("a disputa comecou", false); return; }

		AfirmarEk("os dois lados sao recusados pelo `PodeMexerOCorpo`",
				  !PodeMexerOCorpo(a) && !PodeMexerOCorpo(b));

		SoltarCanal(a, "Ki_Wave");
		TickDosEmbatesDeKi(Protocol.TickSeconds);
		AfirmarEk("...e acabada a disputa, quem soltou o raio anda de novo", PodeMexerOCorpo(a));

		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 8) O CUSTO
	// =====================================================================
	/// <summary>
	/// O GATILHO E UMA VARREDURA DENTRO DO TIQUE DOS PROJETEIS, e a regra 0.5 da casa manda medir em
	/// vez de estimar.
	///
	/// O caso caro e o que a poda promete resolver: uma zona LOTADA de tiros (o teto de 256) em que
	/// nada disso e raio canalizado. Se a poda estiver certa, o custo tem que ser indistinguivel do
	/// que a bancada dos projeteis ja mede -- porque o gatilho nem chega a varrer.
	/// </summary>
	private void OCustoDoGatilho()
	{
		GD.Print("[embateki] -- 8) O CUSTO DO GATILHO, MEDIDO");

		LimparEmbatesDaBancada();
		ServerPlayer pl = Forjar("Metralha", CorredorLivre(6), bp: 5_000);
		pl.Ficha.Ki = 1e12;
		pl.Altitude = Voo.AlturaQueAtravessa + 1;   // no ar: os tiros nao morrem no cenario da Terra

		for (int i = 0; i < MaxProjeteisPorZona; i++)
		{
			Projetil p = Disparar(pl, new ReceitaDeProjetil
			{
				Tipo = TipoDeProjetil.Blast, Velocidade = 5, AlcanceTiles = 100_000,
			});
			p.VidaRestante = 9999;
			p.Pos = new Vec2(pl.Pos.X + 400 + i % 16 * 96, pl.Pos.Y + 400 + i / 16 * 96);
			p.Rumo = new Vec2(MathF.Cos(i * 0.7f), MathF.Sin(i * 0.7f)).Normalized();
		}
		pl.Altitude = 0;

		int vivos = ProjeteisDaZona(pl.Zone.Hash).Count;
		for (int i = 0; i < 30; i++) TickDosProjeteis(Protocol.TickSeconds);   // aquece

		ulong t0 = Time.GetTicksUsec();
		const int amostras = 300;
		for (int i = 0; i < amostras; i++) TickDosProjeteis(Protocol.TickSeconds);
		double us = (Time.GetTicksUsec() - t0) / (double)amostras;

		GD.Print($"[embateki]      {vivos,4} bolas (nenhuma disputa possivel) -> {us,7:0.0} us/tique");
		AfirmarEk("a medicao rodou com a zona CHEIA", vivos == MaxProjeteisPorZona, $"{vivos} tiros");

		double orcamento = Protocol.TickSeconds * 1e6 / 10;
		AfirmarEk("com a zona lotada e zero raios, o gatilho nao cobra nada perceptivel",
				  us < orcamento, $"{us:0.0} us contra {orcamento:0.0} us de orcamento");

		LimparEmbatesDaBancada();

		// E AGORA O CASO CARO DE VERDADE: varias disputas vivas ao mesmo tempo.
		var pares = new List<(ServerPlayer, ServerPlayer)>();
		for (int i = 0; i < 4; i++)
		{
			(ServerPlayer a, ServerPlayer b, DisputaDeKi? d) = DoisRaiosDeFrente(12);
			if (d != null) pares.Add((a, b));
		}
		AfirmarEk("quatro disputas simultaneas vivas", _disputas.Count == 4, $"{_disputas.Count}");

		ulong t1 = Time.GetTicksUsec();
		for (int i = 0; i < amostras; i++)
		{
			TickDosProjeteis(Protocol.TickSeconds);
			TickDosEmbatesDeKi(Protocol.TickSeconds);
		}
		double us2 = (Time.GetTicksUsec() - t1) / (double)amostras;
		GD.Print($"[embateki]      {_disputas.Count} disputas + {ProjeteisDaZona(ZonaDaBancadaDeProjetil.Hash).Count}"
				 + $" tiros -> {us2,7:0.0} us/tique  ({us2 / (Protocol.TickSeconds * 1e6) * 100:0.00}% do tique)");
		AfirmarEk("...e elas cabem folgadas no orcamento", us2 < orcamento, $"{us2:0.0} us");
		_ = pares;

		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 9) OS GANCHOS DA IA
	// =====================================================================
	/// <summary>
	/// O pedido do dono tinha duas metades -- *"o sistema de ataques de ki, e colocar nos ganchos da
	/// IA"* --, e esta familia e a prova da segunda. Nao ha codigo novo de IA pra medir: o que se
	/// afirma e que o gancho, escrito ha tres camadas e inerte desde entao, ACORDOU.
	/// </summary>
	private void OsGanchosDaIa()
	{
		GD.Print("[embateki] -- 9) A IA: ARSENAL, CONTRA-FEIXE E O PRAZO DO CANAL");

		LimparEmbatesDaBancada();

		// ============================ ESTA LINHA JA FOI UM `== 3`, E O PROGRESSO A DERRUBOU ============================
		// Ela nasceu quando a tabela tinha exatamente tres linhas -- uma por tipo de projetil -- e
		// congelou esse numero. O lote G7 acrescentou a Bala Dispersa (a quarta) e a bancada ficou
		// VERMELHA porque a IA ganhou uma arma: uma checagem que pune o proprio trabalho que pede.
		//
		// O que a familia sempre quis afirmar e que o gancho ACORDOU -- a tabela deixou de estar
		// vazia e os TRES tipos de projetil continuam representados, que e a razao de o cabecalho
		// dela dizer "um por tipo". Isso vale com tres linhas e vale com trinta.
		// ============================================================================================================
		AfirmarEk("a tabela de tecnicas de longe deixou de estar vazia, e os tres tipos estao nela",
				  TecnicasDeLonge.Alguma && TecnicasDeLonge.Quantas >= 3
				  && TecnicasDeLonge.Get("Ki_Wave") != null        // Beam
				  && TecnicasDeLonge.Get("Basic_Blast") != null    // Blast
				  && TecnicasDeLonge.Get("Guided_Ball") != null,   // Guided
				  $"{TecnicasDeLonge.Quantas} linhas");
		AfirmarEk("...e o custo de cada linha e o da PROPRIA tecnica, nao uma copia",
				  TecnicasDeLonge.Get("Basic_Blast") is { } l
				  && Math.Abs(l.CustoDeKi(new Fighter { Race = "Human", BP = 5_000 })
							  - 10 * new Fighter { Race = "Human", BP = 5_000 }.BaseDrain()) < 1e-9);

		// ============================ OS DOIS DENTRO DO MESMO CORREDOR LIVRE ============================
		// A primeira versao punha o NPC na ponta do corredor e o agressor CINCO TILES A OESTE -- ou
		// seja, fora do trecho que a varredura garantiu livre. Ele nascia dentro de uma pedra da Terra
		// e o raio dele morria no cenario no primeiro sub-passo; a bancada reprovava tres afirmacoes
		// seguidas sobre a IA por um defeito que era da geometria do teste.
		//
		// E a mesma armadilha que a bancada dos projeteis ja tinha documentado ("a colisao funcionava
		// tao bem que quebrou o teste") -- e que eu repeti mesmo assim, escolhendo uma coordenada
		// relativa em vez de pedir chao ao mapa.
		// ==========================================================================================
		Vec2 pista = CorredorLivre(20);
		ServerPlayer npc = Forjar("Robo", new Vec2(pista.X + 5 * ZoneCollision.TileSize, pista.Y), bp: 50_000);
		npc.Ficha.Ki = npc.Ficha.MaxKi;
		AfirmarEk("quem nao comprou nenhuma das tres continua com arsenal VAZIO",
				  !ArsenalDeLonge(npc).TemAlguma);

		DarSkillDoRaio(npc);
		Arsenal arsenal = ArsenalDeLonge(npc);
		AfirmarEk("quem sabe o raio passa a ter arsenal (o gancho acordou)", arsenal.TemAlguma,
				  $"{arsenal.Quantas} opcoes");
		AfirmarEk("...e o preco que a IA le e o que a tecnica cobra",
				  arsenal.TemAlguma && Math.Abs(arsenal[0].CustoDeKi - 10 * npc.Ficha.BaseDrain()) < 1e-9);

		// O CONTRA-FEIXE: um raio inimigo vindo, e o NPC responde -- sem passar pelo cerebro.
		ServerPlayer agressor = Forjar("Atacante", pista, bp: 50_000);
		agressor.Facing = Facing.East;
		agressor.Ficha.Ki = agressor.Ficha.MaxKi;
		Canalizar(agressor, "Ki_Wave", 10 * agressor.Ficha.BaseDrain(), RaioDeTeste());
		for (int i = 0; i < 60 && _canais.GetValueOrDefault(agressor.Id)?.Raio == null; i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
		}

		AfirmarEk("o NPC PERCEBE um feixe vindo pra cima dele", VemFeixeContraMim(npc, out _));
		TickDoContraFeixe(npc, NowMs());
		AfirmarEk("...e responde com um feixe proprio, pelo verb de producao", _canais.ContainsKey(npc.Id));

		// E O PRAZO: 12 s de canal (`BCL_NPC_BEAM_TIME`), que NAO correm durante uma disputa.
		double segundos = 0;
		for (int i = 0; i < 30 * 20 && _canais.ContainsKey(npc.Id); i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDoPrazoDeRaioDaIa(Protocol.TickSeconds);
			segundos += Protocol.TickSeconds;
		}
		AfirmarEk($"o raio do NPC se fecha sozinho no prazo do DM ({EmbateDeKi.SegundosDeFeixeDeNpc}s)",
				  !_canais.ContainsKey(npc.Id)
				  && Math.Abs(segundos - EmbateDeKi.SegundosDeFeixeDeNpc) < 0.5,
				  $"durou {segundos:0.##}s");

		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// AS PECAS DA BANCADA
	// =====================================================================
	/// <summary>O `Ki_Wave` de producao, na receita que o verb monta (`beams.dm:270`).</summary>
	private static ReceitaDeProjetil RaioDeTeste() => new()
	{
		Tipo = TipoDeProjetil.Beam, BaseDano = 1, Velocidade = 1,
		AlcanceTiles = 30, CargaMinima = 1, Nome = "Onda de Ki",
	};

	/// <summary>
	/// DOIS CORPOS DE FRENTE, CADA UM COM UM RAIO, ate a disputa comecar (ou o prazo estourar).
	///
	/// Tudo por producao: `Canalizar` abre o canal, o tique fecha a carga e pare o raio, o tique do
	/// projetil anda com ele e o GATILHO de dentro dele e quem descobre o encontro. A bancada nao
	/// chama `Comecar` em lugar nenhum -- se o gatilho quebrar, isto aqui devolve nulo.
	/// </summary>
	/// <summary>
	/// UM CORREDOR EM QUE UM CORPO PODE FICAR DE PE -- e nao so um corredor sem parede.
	///
	/// ============================ POR QUE `CorredorLivre` NAO BASTAVA ============================
	/// O <see cref="CorredorLivre"/> pergunta `BlockedCell`, que e "tem muro aqui?". Faltam as outras
	/// tres recusas que o jogo faz pra POUSAR alguem, e que so o <c>ServeDeChao</c> junta: a BEIRADA
	/// do mapa, a AGUA e a NUVEM. Um corredor de agua nao tem parede nenhuma e passa nessa peneira.
	///
	/// **E ISSO CUSTOU UMA REPROVACAO INTEIRA.** A familia do empate arremessa os dois, e o pouso do
	/// arremesso e o unico lugar do jogo que chama `PontoLivrePerto` (um corpo sem `Peer` nao sabe
	/// nadar -- ver `GameServer.Empurrao.cs`). Como o corredor sorteado nao servia de chao, os DOIS
	/// eram reassentados no primeiro chao de verdade -- **o mesmo pra os dois**, porque a busca e em
	/// aneis e eles estavam a dois tiles um do outro. Lido de fora: "a onda jogou os dois pro mesmo
	/// lado, 23 tiles pra cima". O arremesso estava certo -- os dois primeiros tiques do voo apontam
	/// pra lados opostos, medidos. Errado estava o chao debaixo da bancada.
	///
	/// Ou seja e o tropeco que o cabecalho do `_pjMapa` ja registra ("a colisao funcionava tao bem que
	/// quebrou o teste"), agora com as regras de personagem no lugar das de parede.
	/// =============================================================================================
	/// </summary>
	private Vec2 CorredorDeChao(int tiles)
	{
		ZoneCollision? mapa = _pjMapa;
		if (mapa == null) return CorredorLivre(tiles);

		for (int y = _pjProximoCorredor; y < 250; y++)
			for (int x = 4; x + tiles < 250; x++)
			{
				bool serve = true;
				for (int d = 0; d <= tiles && serve; d++) serve &= mapa.ServeDeChao(x + d, y);
				if (!serve) continue;

				_pjProximoCorredor = y + 3;   // pula uma faixa, como o `CorredorLivre`
				return new Vec2(x * ZoneCollision.TileSize + 16, y * ZoneCollision.TileSize + 16);
			}

		// NAO REPROVA AQUI: quem chamou pode nao precisar de chao de verdade, e o corredor sem parede
		// e o que a bancada sempre usou. Reprovar seria trocar um teste vermelho por outro.
		return CorredorLivre(tiles);
	}

	/// <param name="folgaAtras">
	/// TILES DE CHAO ATRAS DE CADA UM, e ele existe por uma medicao.
	///
	/// O corredor da bancada so promete chao PRA DIREITA do primeiro corpo -- entao o duelista da
	/// esquerda nasce colado no fim do pedaco bom. Isso nunca incomodou ninguem enquanto a disputa so
	/// ANDAVA pra dentro do corredor, e passou a mentir no instante em que o empate comecou a
	/// ARREMESSAR os dois pra fora dele.
	///
	/// Quem pede folga tambem ganha o corredor mais exigente (ver <see cref="CorredorDeChao"/>): as
	/// duas coisas existem pela mesma razao -- medir o que sai VOANDO do encontro -- e quem so mede o
	/// que acontece ENTRE os dois nao paga por nenhuma das duas.
	/// </param>
	private (ServerPlayer, ServerPlayer, DisputaDeKi?) DoisRaiosDeFrente(
		int tiles, double bpDoSegundo = 50_000, bool viradoParaOOutro = true, bool tecladoNoSegundo = true,
		int folgaAtras = 0)
	{
		int comprido = tiles + 6 + folgaAtras * 2;
		Vec2 achado = folgaAtras > 0 ? CorredorDeChao(comprido) : CorredorLivre(comprido);
		var chao = new Vec2(achado.X + folgaAtras * ZoneCollision.TileSize, achado.Y);
		ServerPlayer a = Forjar("Duelista", chao, bp: 50_000);
		ServerPlayer b = Forjar("Rival", new Vec2(chao.X + tiles * ZoneCollision.TileSize, chao.Y), bp: bpDoSegundo);

		a.Facing = Facing.East;
		b.Facing = viradoParaOOutro ? Facing.West : Facing.East;
		a.Ficha.Ki = a.Ficha.MaxKi;
		b.Ficha.Ki = b.Ficha.MaxKi;

		// ============================ OS DOIS SAO JOGADORES, POR PADRAO ============================
		// A primeira versao dava teclado so a um deles, e o outro caia na taxa automatica da IA. Como
		// uma letra vale EXATAMENTE o que aquela taxa rende num ciclo da cadencia (`ApertosPorLetra`, e e de
		// proposito), o resultado era um empate perfeito toda vez -- e a bancada reprovava "quem
		// acerta as letras vence" medindo dois lados que empurravam igual.
		//
		// O que se quer medir e um JOGADOR contra outro que nao reage. Entao os dois recebem letra e
		// quem aperta e escolha de quem chamou. A taxa automatica tem afirmacao propria (familia 3).
		// ======================================================================================
		_comTecladoDeTeste.Add(a.Id);
		if (tecladoNoSegundo) _comTecladoDeTeste.Add(b.Id);

		Canalizar(a, "Ki_Wave", 10 * a.Ficha.BaseDrain(), RaioDeTeste());
		Canalizar(b, "Ki_Wave", 10 * b.Ficha.BaseDrain(), RaioDeTeste());

		for (int i = 0; i < 30 * 6; i++)
		{
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
			TickDosEmbatesDeKi(Protocol.TickSeconds);
			if (_emEmbateDeKi.TryGetValue(a.Id, out DisputaDeKi? d)) return (a, b, d);
		}
		return (a, b, null);
	}

	/// <summary>
	/// DA A SKILL QUE DESTRAVA O `Ki_Wave` -- pelo livro de verdade, e nao por um atalho: o
	/// `ArsenalDeLonge` pergunta ao `SabeTecnica`, que varre o `Livro.Aprendidas` contra o catalogo
	/// de skills. Aprender de mentira mediria a mentira.
	/// </summary>
	private void DarSkillDoRaio(ServerPlayer pl)
	{
		if (_skills == null) return;
		foreach (Skill s in _skills.Todas)
		{
			if (s.Arvore || !s.Verbos.Contains("Ki_Wave", StringComparer.OrdinalIgnoreCase)) continue;
			// `Dar` e nao `Aprender`: o caminho de quem foi ENSINADO. A bancada nao esta medindo a
			// loja de marcos nem a arvore racial -- ela esta medindo o que o ARSENAL faz com uma
			// skill aprendida, e `SabeTecnica` (que e quem o arsenal consulta) le o mesmo conjunto.
			pl.Livro.Dar(s.Path);
			return;
		}
	}

	/// <summary>
	/// Desfaz tudo que a bancada pos no mundo -- disputas, canais, tiros e corpos.
	///
	/// AS DISPUTAS SAEM PRIMEIRO e SEM `Resolver`: aquele solta cabeca, troca dono e manda pacote, e
	/// aqui nao ha o que resolver -- os dois lados estao sendo apagados no mesmo instante.
	/// </summary>
	private void LimparEmbatesDaBancada()
	{
		foreach (DisputaDeKi d in _disputas.ToList())
		{
			if (d.A.Feixe != null) d.A.Feixe.EmEmbate = false;
			if (d.B.Feixe != null) d.B.Feixe.EmEmbate = false;
			_emEmbateDeKi.Remove(d.A.Quem.Id);
			_emEmbateDeKi.Remove(d.B.Quem.Id);
		}
		_disputas.Clear();
		_comTecladoDeTeste.Clear();
		_raioDaIaAte.Clear();
		_contraFeixeLivreEm.Clear();

		// OS CORPOS E OS TIROS SAEM PELO MESMO `LimparTudoDaBancada` da bancada dos projeteis: esta
		// aqui reusa o `Forjar` dela, entao os ids nascem na mesma faixa e a limpeza e uma so. Duas
		// limpezas pra a mesma faixa de ids seria a segunda copia da regra de sempre.
		LimparTudoDaBancada();
	}
}
