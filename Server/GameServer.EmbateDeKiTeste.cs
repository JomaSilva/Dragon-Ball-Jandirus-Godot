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
///  9b. O PRAZO E DO RAIO, NAO DO CORPO: um NPC interrompido que atira de novo conta os doze segundos
///     do ZERO -- e com o prazo herdado do raio anterior (o jogo de antes) o segundo fecha na carga.
/// 11. O PODER E O DE AGORA: um aumento de poder aceso NO MEIO da disputa entra no feixe, vira a
///     vantagem e traz o encontro de volta -- e com a vantagem congelada no comeco (o jogo de antes),
///     o mesmo roteiro nao vira.
/// 13. A ESCADA DA GUARDA (`GameServer.EscadaDaGuardaTeste.cs`): as maos se medem pelo DANO -- a disputa
///     abre na razao dano/`DanoQueAsMaosEmpatam`, o raio do corte e segurado e devolvido, no teto quem
///     segura perde, e a escada so desce. Com as maos na escala da deflexao (o jogo de antes), ate o
///     raio do corte engole quem segura.
/// 14. SAIR DE LADO (`GameServer.SairDeLadoTeste.cs`): quem segura o raio com as maos da o passo ATRAVESSADO
///     a ele e a disputa fecha sem vencedor, com a guarda erguida -- o raio segue viagem e nao o acerta. O
///     passo ao longo do raio nao solta, o mais fraco nem entra na disputa, e quem para no meio volta a ser
///     alvo. Com a porta fechada (o jogo de antes), o mesmo passo nao fecha nada.
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
			AsCabecasSeTocamSemSobrepor();
			CruzarNaoEDisputar();
			NuncaSeSobrepoem();
			OTroncoLargoEncostaNaBeirada();
			OFimEAssimetrico();
			SairDoEmbateEPerder();
			OEmpateCobra();
			VencerAntesNaoExplode();
			AsBordasNaoPrendemNinguem();
			OFeixeContraAGuarda();
			OParryContraKi();
			OCorpoFicaPreso();
			OCustoDoGatilho();
			OsGanchosDaIa();
			OPrazoEDoRaioNaoDoCorpo();
			ApanharNaDisputaEDesvantagem();
			OSaltoDePoderVira();
			ORaioCresceComOPoder();
			AEscadaDaGuarda();
			ASaidaDeLado();
		}
		finally { EmbateDeKi.VantagemCongeladaDeTeste = false; LimparEmbatesDaBancada(); }

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
				  !TentarEmbateDeFeixes(p1, p1.Pos, ProjeteisDaZona(solo.Zone.Hash)));
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
				  !TentarEmbateDeFeixes(bola, bola.Pos, ProjeteisDaZona(g.Zone.Hash)));

		// (d) RAIO JA SOLTO: sem dono canalizando nao ha quem empurre. `!A.beaming`
		Projetil largado = Disparar(g, RaioDeTeste());
		largado.Canalizando = false;
		AfirmarEk("um raio ja SOLTO nao entra em disputa (nao ha quem o empurre)",
				  !TentarEmbateDeFeixes(largado, largado.Pos, ProjeteisDaZona(g.Zone.Hash)));

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

		// AS MAOS NAO MORAM MAIS AQUI. O ponto de equilibrio delas e um DANO (o raio que faria
		// `EmbateDeKi.DanoQueAsMaosEmpatam` atraves da guarda; dono, 2026-10-08), e dano pede um tiro e um corpo de
		// verdade: quem o mede e a familia 13 (`GameServer.EscadaDaGuardaTeste.cs`), com o embate de producao.
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
		// A MAO DO INIMIGO (2026-09-23), e nao mais a beirada do corpo: o encontro para quando a cabeca de quem
		// perde chega na mao dele (o feixe dele engolido ate o fim) -- senao ela passava pra tras da mao e o feixe
		// dele era desenhado ao contrario por cima do vencedor. O ponto de contato fica, entao, a MAO DELE mais a
		// frente da cabeca dele: e ai que a frente do vencedor encosta.
		LadoDeKi dele = meu == d.A ? d.B : d.A;
		float naMao = (BocaDeCano.De(outro.Pos, dele.Feixe!.Rumo) - outro.Pos).Length + Feixe.AlcanceDaCabeca(dele.Feixe);
		AfirmarEk("...e no instante da vitoria o feixe dele estava ENGOLIDO ATE A MAO: o contato na mao dele",
				  Math.Abs(chegou - naMao) <= 2, $"{nasceuA:0} px no comeco -> {chegou:0.##} px no fim (a mao + a frente = {naMao:0})");
		AfirmarEk("...e a cabeca dele NAO passou pra tras da propria mao (o feixe nunca ficou ao contrario)",
				  (dele.Feixe.Pos - dele.Feixe.Cauda).X * dele.Feixe.Rumo.X + (dele.Feixe.Pos - dele.Feixe.Cauda).Y * dele.Feixe.Rumo.Y >= -0.5f,
				  $"cabeca {dele.Feixe.Pos}, cauda {dele.Feixe.Cauda}");
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

		// ============================ O SORTEIO DE DEFLEXAO SAI DO CAMINHO ============================
		// O raio sai pelo <see cref="SemDeflexao"/>. Contra um igual, o dado apara o raio de bancada atraves da
		// guarda em menos de 1% dos impactos (a conta esta no `DanoDeKi.CorteDoFraco`) -- e esse 1%
		// trocaria "as maos o seguram" por um tiro aparado sem nada errado no jogo: a familia de defeito
		// que a varredura de 2026-09-02 tirou das cenas do EMBATE. (Desde 2026-10-12 so o raspao sai entre
		// iguais: defletir de vez pede o dobro do poder do tiro.) O que se isola aqui e o embate; a ordem
		// entre ele e o sorteio tem afirmacao propria logo abaixo.
		// =============================================================================================
		Vec2 chao = CorredorLivre(20);
		ServerPlayer atirador = Forjar("Agressor", chao, bp: 200_000);
		atirador.Facing = Facing.East;
		atirador.Ficha.Ki = atirador.Ficha.MaxKi;
		ServerPlayer guarda = Forjar("Muralha", new Vec2(chao.X + 8 * ZoneCollision.TileSize, chao.Y), bp: 200_000);
		guarda.Ficha.Ki = guarda.Ficha.MaxKi;
		SegurarAGuarda(guarda);
		_comTecladoDeTeste.Add(guarda.Id);

		// ============================ UM IGUAL SEGURA O RAIO QUE O FERE, E O DEVOLVE (dono, 2026-10-08) ============================
		// As maos se medem pelo DANO (`EmbateDeKi.PoderDeSegurar`): a razao da disputa e o que o raio faria atraves
		// da guarda sobre `EmbateDeKi.DanoQueAsMaosEmpatam`. O raio daqui e o golpe de bancada contra um IGUAL de
		// guarda erguida (`BaseQueFere`: 12 por batida, abaixo do que as maos empatam), entao as maos entram MAIS
		// FORTES que ele -- e quem acerta as letras o devolve. O ataque volta com o poder de quem o segurou e fere
		// quem o disparou, que esta de maos abaixadas.
		//
		// (O PREPARO la embaixo ainda confere que o 12 passa de `DanoDeKi.CorteDoFraco`: e o piso do antigo corte por
		// dano, que ate 2026-10-12 decidia se havia embate. Hoje quem decide e a razao de poder -- o igual segura, o
		// mais fraco e levado, a parede nem sente: `KiContraCorpo` --, e esse piso nao barra mais nada.)
		//
		// ATE 2026-10-08 ESTA CENA SO FECHAVA COM UM PAR QUE NAO EXISTE EM JOGO (20 de poder contra 5.000.000, no
		// piso do `BpModulus`): as maos valiam o numerador da chance de deflexao sobre 100, todo raio que feria
		// tinha mais de cem vezes o poder delas, e "segurar e devolver" so acontecia aqui. A escada inteira, e o
		// defeito injetado da escala antiga, moram na familia 13 (`GameServer.EscadaDaGuardaTeste.cs`).
		// ===============================================================================================================================
		ReceitaDeProjetil queFere = SemDeflexao();
		queFere.BaseDano = BaseQueFere(atirador, guarda);
		Canalizar(atirador, "Ki_Wave", 10 * atirador.Ficha.BaseDrain(), queFere);
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

		double danoNaGuarda = DanoFinalDe(d.A.Feixe!, guarda);
		AfirmarEk("PREPARO: o raio e de um IGUAL, fere atraves da guarda (passa do corte dos fracos) e fica abaixo do que as maos empatam",
				  Math.Abs(atirador.Ficha.expressedBP - guarda.Ficha.expressedBP) < 1
				  && danoNaGuarda >= DanoDeKi.CorteDoFraco && danoNaGuarda < EmbateDeKi.DanoQueAsMaosEmpatam,
				  $"poder {atirador.Ficha.expressedBP:0} x {guarda.Ficha.expressedBP:0}, dano na guarda {danoNaGuarda:0.##}");
		AfirmarEk("...e as maos entram na disputa MAIS FORTES que ele: a vantagem delas e o que elas empatam sobre o dano do raio",
				  Math.Abs(d.B.Vantagem - EmbateDeKi.DanoQueAsMaosEmpatam / danoNaGuarda) < 0.02,
				  $"vantagem {d.A.Vantagem:0.##} (feixe) x {d.B.Vantagem:0.##} (maos); o esperado das maos e {EmbateDeKi.DanoQueAsMaosEmpatam / danoNaGuarda:0.##}");

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
		// (de poder IGUAL desde 2026-10-12: quem e mais fraco que o raio nao o segura nas maos -- e levado. `KiContraCorpo`)
		ServerPlayer a2 = Forjar("Agressor2", chao2, bp: 5_000);
		a2.Facing = Facing.East;
		a2.Ficha.Ki = a2.Ficha.MaxKi;
		ServerPlayer g2 = Forjar("Covarde", new Vec2(chao2.X + 8 * ZoneCollision.TileSize, chao2.Y), bp: 5_000);
		g2.Ficha.Ki = g2.Ficha.MaxKi;
		SegurarAGuarda(g2);

		Canalizar(a2, "Ki_Wave", 10 * a2.Ficha.BaseDrain(), SemDeflexao());
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

		// ============================ A ORDEM: QUEM TEM SORTE DEFENDE DE GRACA ============================
		// O SORTEIO DE DEFLEXAO VEM ANTES DO EMBATE DE GUARDA. No `Acertar` os dois dados da deflexao rodam primeiro, e
		// so quem nao teve sorte chega ao passo 3b (`TentarEmbateDeGuarda`): contra um raio COMUM -- com o dado --, quem
		// tem a sorte sai da linha e nao ha disputa nenhuma. Sem esta cena, alguem poderia mover o gatilho pra antes do
		// sorteio e apagar a defesa de ki do jogo inteiro sem nenhum teste reclamar.
		//
		// QUEM SEGURA A GUARDA ESTA DENTRO DA ESCADA (`KiContraCorpo`, dono 2026-10-12): tres vezes o poder do tiro, o
		// degrau de quem PODE defletir (o dobro) e ainda nao e PAREDE (o quadruplo). E um corpo que o raio fere, que e
		// sorteado nos dois dados e que, sem sorte, seguraria o raio nas maos: os tres caminhos estao abertos pra ele, e
		// quem escolhe entre eles e so a ORDEM dos passos.
		//
		// E A SORTE E CERTA POR CONSTRUCAO: a chance de deflexao e cravada acima de 200% no proprio tiro, pelo `ModsBase`
		// (`CravarAChance`, o lever da `--projetilteste` 15), e com ela o primeiro dado -- o raspao, `prob(deflectchance/2)`
		// -- sai SEMPRE. O poder do tiro, que e o degrau da cena, nao e tocado; o dano que sobra e de milesimos, e nao
		// importa: nem o sorteio nem a disputa de guarda olham o dano (quem nao e sorteado hoje e quem e parede).
		//
		// O RASPAO E AFIRMADO PELO QUE SO ELE DEIXA: o relato `Esquivou` no fio, o corpo na `Projetil.ForaDaLinha`, a
		// cabeca seguindo viagem na mao de quem atirou, e os 0,5 de pericia de UMA batida (0,1 de encostar mais 0,4 de
		// sair da linha). "Nenhuma disputa abriu" sozinha nao prova a ordem -- ver o controle da fortaleza, logo abaixo.
		//
		// O QUE ELA NAO ISOLA: o SEGUNDO dado (a deflexao cheia, `prob(deflectchance)`). Com o raspao certo ele nunca roda,
		// e nao ha como faze-lo rodar sem sorte -- a chance do raspao e a metade da dele. O gatilho posto ENTRE os dois
		// dados passaria por esta cena; posto antes dos dois, nao passa.
		// ===================================================================================================
		Vec2 chao3 = CorredorLivre(20);
		MedidaDaOrdem sorte = RaioNaGuardaSegurada("Sortudo", chao3, VezesDeQuemSeguraNaOrdem);
		GD.Print($"[embateki]      (medido) QUEM PODE DEFLETIR, raio com o dado: {sorte.Relato()}");
		AfirmarEk("PREPARO: quem segura a guarda tem de duas a quatro vezes o poder do tiro (pode defletir, e nao e parede), a guarda e a SEGURADA (fora da janela do deflect), e o raspao do raio comum e CERTO -- a chance cravada acima de 200%, com o Ki de aparar",
				  sorte.Raio is { Deflectivel: true } && KiContraCorpo.PodeDefletir(sorte.Vezes) && !KiContraCorpo.EhParede(sorte.Vezes)
				  && sorte.GuardaSegurada && sorte.Chance / 2 >= 100 && sorte.Ki >= 5,
				  $"{sorte.Vezes:0.###}x o poder do tiro, guarda segurada {sorte.GuardaSegurada}, chance {sorte.Chance:0.#}%, Ki {sorte.Ki:0.#}");
		AfirmarEk("contra um raio COMUM, quem podia defletir defende de graca (o sorteio vem antes): o raio chega nele e NENHUMA disputa de guarda abre",
				  sorte.Chegou && sorte.Disputa == null,
				  $"chegou {sorte.Chegou}, disputa {(sorte.Disputa == null ? "nenhuma" : "ABRIU")}");
		AfirmarEk("...e quem resolveu foi o RASPAO: UM relato no fio, e ele e `Esquivou` -- nem `Aparou` (o golpe na guarda) nem `Rebateu` (o deflect)",
				  sorte.Relatos.Count == 1 && RaspoesEm(sorte.Relatos) == 1,
				  $"{sorte.Relatos.Count} relato(s): {sorte.Desfechos()}");
		AfirmarEk("...o corpo SAIU DA LINHA do raio (`Projetil.ForaDaLinha`) e a cabeca SEGUIU VIAGEM: passou dele sem plantar, sem leva-lo e sem sair da mao de quem atirou -- e ele nao perdeu vida nenhuma",
				  sorte.ForaDaLinhaNaBatida && sorte.ForaDaLinhaDepois && sorte.CabecaAlem > 2 * ZoneCollision.TileSize
				  && !sorte.Plantada && sorte.Levando == 0 && sorte.NaMaoDeQuemAtirou && Math.Abs(sorte.Vida) < 1e-9,
				  $"fora da linha na batida {sorte.ForaDaLinhaNaBatida} e depois {sorte.ForaDaLinhaDepois}, cabeca {sorte.CabecaAlem / ZoneCollision.TileSize:0.0} tiles alem, "
				  + $"plantada {sorte.Plantada}, levando {sorte.Levando}, na mao de quem atirou {sorte.NaMaoDeQuemAtirou}, vida {sorte.Vida:+0.###;-0.###;0}");
		AfirmarEk("...e o treino e o de UMA batida: 0,1 de encostar (`objects.dm:313`) mais 0,4 de sair da linha (`:359`), no proprio tique do encontro -- e nada mais soma depois",
				  Math.Abs(sorte.PericiaNaBatida - 0.5) < 1e-9 && Math.Abs(sorte.Pericia - 0.5) < 1e-9,
				  $"pericia de defesa de ki +{sorte.PericiaNaBatida:0.0} na batida, +{sorte.Pericia:0.0} no fim da cena");

		// O CONTROLE: O PREPARO DE ANTES. Ate 2026-10-12 quem segurava a guarda nesta cena era a "Fortaleza", com
		// cinquenta mil vezes o poder do tiro -- do tempo em que o corte era por DANO (`objects.dm:355-357`) e o raspao
		// certo so cabia no piso do `BpModulus`. Na escada isso e PAREDE, e o corte da parede (passo 3a-bis do `Acertar`)
		// vem antes dos dois dados e do embate: nao ha sorteio nem disputa possivel. A cena continuava verde -- "nenhuma
		// disputa" era verdade, e o "0,1 + 0,4 de pericia" que ela lia como raspao era o 0,1 de cada batida sem ferir,
		// somado em oito segundos de laco -- e mover o gatilho pra antes do sorteio nao a derrubava. O que separa os
		// dois mundos e o que o raspao deixa, e e isso que as linhas de cima afirmam.
		MedidaDaOrdem fortaleza = RaioNaGuardaSegurada("Fortaleza", chao3, VezesDaFortalezaDeAntes);
		GD.Print($"[embateki]      (medido) A FORTALEZA DE ANTES, o mesmo raio: {fortaleza.Relato()}");
		AfirmarEk("CONTRA-EXEMPLO (o preparo de antes): contra quem e PAREDE pro tiro, o mesmo raio de raspao certo tambem nao abre disputa -- e nao ha raspao nenhum: nenhum relato, o corpo na linha, a cabeca parada nele, e a batida treina so o 0,1 de encostar",
				  fortaleza.Raio is { Deflectivel: true } && KiContraCorpo.EhParede(fortaleza.Vezes) && fortaleza.Chance / 2 >= 100
				  && fortaleza.Chegou && fortaleza.Disputa == null && fortaleza.Relatos.Count == 0 && !fortaleza.ForaDaLinhaNaBatida
				  && fortaleza.CabecaAlem < 0 && Math.Abs(fortaleza.PericiaNaBatida - 0.1) < 1e-9,
				  $"{fortaleza.Vezes:0.#}x o poder do tiro, chance {fortaleza.Chance:0.#}%, chegou {fortaleza.Chegou}, disputa {(fortaleza.Disputa == null ? "nenhuma" : "ABRIU")}, "
				  + $"{fortaleza.Relatos.Count} relato(s), fora da linha {fortaleza.ForaDaLinhaNaBatida}, cabeca {fortaleza.CabecaAlem:0.0} px alem, pericia +{fortaleza.PericiaNaBatida:0.0} na batida");

		// O DEFEITO INJETADO: A MESMA CENA, O MESMO RAIO, SEM O DADO (`SemDeflexao`, o `deflectable = 0` do DM). A conta
		// da chance continua la em cima -- o que falta e quem a sorteie --, e ai o raio encontra as MAOS de quem segura a
		// guarda: a disputa abre. E a prova de que, na cena de cima, quem impediu a disputa foi o SORTEIO, e nao o
		// degrau, a guarda ou o raio: este corpo segura este raio. (O `TentarEmbateDeGuarda` nao le o `Deflectivel` -- o
		// gatilho posto antes do sorteio acharia, na cena de cima, exatamente o que acha aqui.)
		try
		{
			MedidaDaOrdem semDado = RaioNaGuardaSegurada("Sortudo", chao3, VezesDeQuemSeguraNaOrdem, comODado: false);
			GD.Print($"[embateki]      (medido) QUEM PODE DEFLETIR, raio SEM o dado: {semDado.Relato()}");
			AfirmarEk("(defeito injetado: o MESMO raio sai sem o dado da deflexao) a conta da chance continua acima de 200%, mas ninguem a sorteia: o raio encontra as MAOS de quem segura a guarda e a disputa de guarda ABRE -- na cena de cima, quem a impediu foi o sorteio",
					  semDado.Raio is { Deflectivel: false } && KiContraCorpo.PodeDefletir(semDado.Vezes) && !KiContraCorpo.EhParede(semDado.Vezes)
					  && semDado.GuardaSegurada && semDado.Chance / 2 >= 100 && semDado.Chegou
					  && semDado.Disputa is { Tipo: Protocol.TipoDeEmbate.FeixeContraGuarda, B.DeMaos: true },
					  $"{semDado.Vezes:0.###}x o poder do tiro, guarda segurada {semDado.GuardaSegurada}, chance da conta {semDado.Chance:0.#}%, "
					  + $"chegou {semDado.Chegou}, disputa {(semDado.Disputa == null ? "NENHUMA" : semDado.Disputa.Tipo.ToString())}");
			AfirmarEk("(defeito injetado) ...e raspao nenhum: nenhum relato no fio, o corpo continua na linha do raio, e a batida treina so o 0,1 de encostar",
					  semDado.Relatos.Count == 0 && !semDado.ForaDaLinhaNaBatida && Math.Abs(semDado.PericiaNaBatida - 0.1) < 1e-9,
					  $"{semDado.Relatos.Count} relato(s): {semDado.Desfechos()}; fora da linha {semDado.ForaDaLinhaNaBatida}, pericia +{semDado.PericiaNaBatida:0.0} na batida");
		}
		finally { LimparEmbatesDaBancada(); }   // a disputa que o defeito abre nao sobra pra familia seguinte
	}

	// ---------------------------------------------------------------------
	// AS PECAS DA CENA DA ORDEM (o fim da familia 6)
	// ---------------------------------------------------------------------
	/// <summary>O poder de quem atira na cena da ordem -- o mesmo atirador das cenas do deflect (6b).</summary>
	private const double BpDeQuemAtiraNaOrdem = 5_000;

	/// <summary>
	/// QUANTAS VEZES O PODER DO TIRO tem quem segura a guarda na cena da ordem: TRES -- o meio do degrau de quem pode
	/// defletir (`KiContraCorpo.VezesPraDefletir`, o dobro) e ainda nao e parede (`VezesDaParede`, o quadruplo). Longe
	/// das duas portas de proposito: a cena mede a ORDEM dos passos do `Acertar`, e nao a beirada de um degrau.
	/// </summary>
	private const double VezesDeQuemSeguraNaOrdem = 3;

	/// <summary>
	/// O preparo desta cena ate 2026-10-12: a "Fortaleza" tinha CINQUENTA MIL vezes o poder do tiro (100 contra
	/// 5.000.000). Fica como o controle -- na escada isso e parede, e parede nem e sorteada.
	/// </summary>
	private const double VezesDaFortalezaDeAntes = 50_000;

	/// <summary>
	/// Quantos tiques depois da batida a cena da ordem olha onde a cabeca foi parar: seis. O Ki Wave anda 20 tiles por
	/// segundo, entao a cabeca que seguiu viagem ja passou uns quatro tiles do corpo -- e ainda esta no corredor da cena.
	/// </summary>
	private const int TiquesPraCabecaPassarNaOrdem = 6;

	/// <summary>
	/// Por quantos tiques a cena da ordem continua de olho depois da batida: um segundo. Um raio que tivesse FICADO em
	/// quem raspou moeria cinco vezes nesse tempo (`Projetil.SegundosPorCicloDeBeam`), e cada moida e outro `Acertar`.
	/// </summary>
	private const int TiquesDeVigiaDaOrdem = 30;

	/// <summary>
	/// O QUE A CENA DA ORDEM MEDIU num raio contra uma guarda segurada.
	///   * O PREPARO, lido quando a cabeca nasce (nada dele muda ate ela chegar): `Vezes` = quantas vezes o poder do
	///     tiro o corpo tem (a razao que a escada le); `Chance` = a chance de deflexao que a conta de producao da pra
	///     este tiro neste corpo, ja cravada; `Dano` = o dano final que sobrou; `GuardaSegurada` e `Ki`.
	///   * A BATIDA: `Chegou` = o raio encostou nele (todo `Acertar` treina 0,1 de defesa de ki antes de qualquer
	///     desfecho); `PericiaNaBatida` e `ForaDaLinhaNaBatida` sao do proprio tique do encontro.
	///   * DEPOIS: `Disputa` = a disputa de guarda que abriu (nula = nenhuma, em nenhum tique da cena);
	///     `ForaDaLinhaDepois`, `CabecaAlem` (quanto a cabeca passou do corpo, pelo eixo do raio; negativo = parou antes
	///     dele), `Plantada`, `Levando` e `NaMaoDeQuemAtirou` sao de <see cref="TiquesPraCabecaPassarNaOrdem"/> tiques
	///     depois (ou do fim, se a cena acabou antes); `Relatos` = os relatos de golpe NELE, lidos do fio; `Pericia` e
	///     `Vida` sao a diferenca no fim da cena.
	/// </summary>
	private readonly record struct MedidaDaOrdem(
		Projetil? Raio, double Vezes, double Chance, double Dano, bool GuardaSegurada, double Ki,
		bool Chegou, double PericiaNaBatida, bool ForaDaLinhaNaBatida,
		DisputaDeKi? Disputa, bool ForaDaLinhaDepois, float CabecaAlem, bool Plantada, int Levando, bool NaMaoDeQuemAtirou,
		List<Protocol.HitEvent> Relatos, double Pericia, double Vida)
	{
		/// <summary>Os desfechos dos relatos, na ordem em que sairam.</summary>
		public string Desfechos() => Relatos.Count == 0 ? "nenhum" : string.Join(", ", Relatos.Select(h => (Desfecho)h.Desfecho));

		public string Relato()
			=> $"{Vezes:0.###}x o poder do tiro, chance da conta {Chance:0.#}% (dano final {Dano:0.####}) -> {Relatos.Count} relato(s) [{Desfechos()}], "
			   + $"disputa {(Disputa == null ? "nenhuma" : Disputa.Tipo.ToString())}, fora da linha {ForaDaLinhaNaBatida}, "
			   + $"pericia +{PericiaNaBatida:0.0} na batida e +{Pericia:0.0} no fim, cabeca {CabecaAlem / ZoneCollision.TileSize:0.0} tiles alem do corpo, "
			   + $"vida {Vida:+0.###;-0.###;0}";
	}

	/// <summary>
	/// UM KI WAVE CANALIZADO contra quem SEGURA a guarda a oito tiles, com a chance de deflexao CRAVADA no raspao certo
	/// -- ate a batida, e mais um segundo de olho (<see cref="TiquesDeVigiaDaOrdem"/>). Se uma disputa de guarda abrir,
	/// a cena para ali: o que ela faz dali em diante e assunto das cenas de cima.
	///
	/// TUDO POR PRODUCAO, como o resto da familia: `Canalizar` abre o canal, o tique fecha a carga e pare o raio, o
	/// `Acertar` decide o que a batida e. O unico toque da bancada e o `CravarAChance` (`GameServer.RaspaoTeste.cs`), no
	/// tique em que a cabeca nasce e antes do primeiro passo dela: ele escala o `ModsBase` do tiro ate a conta de
	/// producao dar a chance pedida, sem mexer no poder do tiro -- que e o degrau da cena. O raio SEM o dado e cravado
	/// igual: os dois saem identicos, menos o `Deflectivel`.
	/// </summary>
	/// <param name="vezesOPoderDoTiro">O poder de quem segura a guarda, em vezes o de quem atira -- o DEGRAU da cena.</param>
	/// <param name="comODado">Verdadeiro = o raio COMUM (<see cref="RaioDeTeste"/>, defletivel). Falso = o mesmo raio <see cref="SemDeflexao"/>.</param>
	private MedidaDaOrdem RaioNaGuardaSegurada(string nome, Vec2 chao, double vezesOPoderDoTiro, bool comODado = true)
	{
		LimparEmbatesDaBancada();
		ServerPlayer atirador = Forjar("Agressor3", chao, bp: BpDeQuemAtiraNaOrdem);
		atirador.Facing = Facing.East;
		atirador.Ficha.Ki = atirador.Ficha.MaxKi;
		ServerPlayer guarda = Forjar(nome, new Vec2(chao.X + 8 * ZoneCollision.TileSize, chao.Y), bp: BpDeQuemAtiraNaOrdem * vezesOPoderDoTiro);
		guarda.Ficha.Ki = guarda.Ficha.MaxKi;
		SegurarAGuarda(guarda);
		double periciaAntes = guarda.Ficha.kidefenseskill, vidaAntes = guarda.Combate.Corpo.Vida();

		Canalizar(atirador, "Ki_Wave", 10 * atirador.Ficha.BaseDrain(), comODado ? RaioDeTeste() : SemDeflexao());

		Projetil? raio = null;
		DisputaDeKi? disputa = null;
		double vezes = 0, chance = 0, dano = 0, ki = 0, periciaNaBatida = 0;
		bool segurada = false, foraNaBatida = false, foraDepois = false, plantada = false, naMao = false, olhou = false;
		float cabecaAlem = 0;
		int levando = 0, desdeABatida = -1;

		void OlharACabeca()
		{
			olhou = true;
			if (raio == null) return;
			foraDepois = raio.EstaForaDaLinha(guarda.Id);
			cabecaAlem = -NaFrenteDe(raio, guarda);
			plantada = raio.Encostado;
			levando = raio.Arrastando;
			naMao = raio.Vivo && raio.Canalizando && raio.Desviador == 0 && _canais.GetValueOrDefault(atirador.Id)?.Raio == raio;
		}

		EscutaDeGolpes = [];
		try
		{
			for (int i = 0; i < 30 * 8 && disputa == null && desdeABatida < TiquesDeVigiaDaOrdem; i++)
			{
				TickDosCanaisDeKi(Protocol.TickSeconds);

				// A CABECA ACABOU DE NASCER (a carga fechou neste tique) e ainda nao deu passo nenhum: a chance e
				// cravada aqui, e o preparo e lido aqui -- o poder do tiro e congelado no disparo, e ninguem tica a
				// ficha nem o relogio da guarda de quem espera.
				if (raio == null && _canais.GetValueOrDefault(atirador.Id)?.Raio is { } nascido)
				{
					raio = nascido;
					CravarAChance(raio, guarda, ChanceDoRaspaoCerto);
					vezes = VezesOPoderDoTiro(raio, guarda);
					chance = ChanceDeDeflexaoDe(raio, guarda);
					dano = DanoFinalDe(raio, guarda);
					segurada = guarda.Combate.Bloqueando && guarda.Combate.TempoDeGuarda > MeleeResolver.JanelaContra;
					ki = guarda.Ficha.Ki;
				}

				TickDosProjeteis(Protocol.TickSeconds);
				// A DISPUTA DE GUARDA SO ABRE DENTRO DO TIQUE DOS PROJETEIS (`Acertar`, passo 3b): olha-se a cada tique.
				disputa = _emEmbateDeKi.GetValueOrDefault(guarda.Id);
				TickDosEmbatesDeKi(Protocol.TickSeconds);

				if (desdeABatida >= 0) desdeABatida++;
				else if (guarda.Ficha.kidefenseskill > periciaAntes)
				{
					desdeABatida = 0;
					periciaNaBatida = guarda.Ficha.kidefenseskill - periciaAntes;
					foraNaBatida = raio?.EstaForaDaLinha(guarda.Id) == true;
				}
				if (desdeABatida == TiquesPraCabecaPassarNaOrdem) OlharACabeca();
			}
			if (!olhou) OlharACabeca();

			List<Protocol.HitEvent> relatos = EscutaDeGolpes.Where(g => g.Cheio).Select(g => LerGolpe(g.Fio))
															.Where(h => h.Alvo == guarda.Id).ToList();
			return new MedidaDaOrdem(
				Raio: raio, Vezes: vezes, Chance: chance, Dano: dano, GuardaSegurada: segurada, Ki: ki,
				Chegou: desdeABatida >= 0, PericiaNaBatida: periciaNaBatida, ForaDaLinhaNaBatida: foraNaBatida,
				Disputa: disputa, ForaDaLinhaDepois: foraDepois, CabecaAlem: cabecaAlem, Plantada: plantada, Levando: levando,
				NaMaoDeQuemAtirou: naMao,
				Relatos: relatos, Pericia: guarda.Ficha.kidefenseskill - periciaAntes, Vida: guarda.Combate.Corpo.Vida() - vidaAntes);
		}
		finally { EscutaDeGolpes = null; }
	}

	// =====================================================================
	// 6b) O PARRY CONTRA KI (dono, 2026-09-25)
	// =====================================================================
	/// <summary>
	/// O DEFLECT (dono, 2026-10-12): a guarda erguida NO TEMPO CERTO, por quem tem o DOBRO do poder do tiro, tira o
	/// ataque de quem atirou -- a mao dele fecha, e o RAIO sai DE LADO, solto, pra estourar no fim do alcance. A ESFERA
	/// volta pra quem a atirou (retorno do dono do mesmo dia: *"deflect de esferas de ki (apenas blast) voltam pro
	/// atacante original"*). Ver `GameServer.ParryDeKi.cs` e `KiContraCorpo.PodeDefletir`.
	///
	/// OS CONTROLES SAO AS PORTAS: a mesma cena com menos que o dobro do poder (nao ha deflect: o golpe segue a escada
	/// do `KiContraCorpo`), e o defeito injetado da porta de antes (bastava ser igual). A familia 6, logo acima, e a
	/// guarda VELHA contra raio: o embate de sempre. Todos os tiros saem SEM o dado da deflexao (`SemDeflexao`): o
	/// que se mede aqui e o gesto, e nao a sorte.
	/// </summary>
	private void OParryContraKi()
	{
		GD.Print("[embateki] -- 6b) O DEFLECT DE KI: O DOBRO DO PODER, E O ATAQUE SAI DAS MAOS DE QUEM ATIROU");
		LimparEmbatesDaBancada();
		static bool Defletiu(List<Protocol.HitEvent> relatos, ServerPlayer quem) =>
			relatos.Any(h => h.Alvo == quem.Id && (Desfecho)h.Desfecho == Desfecho.Rebateu);

		// ---------------------------------------------------------------- o raio, contra o dobro do poder (o atirador tem 5.000)
		// DE CIMA (`Voo.AlturaQueAtravessa`) e com o alcance curto: o pedaco defletido sai de LADO, pra fora do
		// corredor, e a cena mede o FIM DO ALCANCE dele -- nao o que o mapa tem em volta.
		var (feixe, raiador, espelho, relatos, vidaInicial) = RaioContraOParry("Espelho", bpDoDefensor: 10_000, alcanceTiles: 14, altura: Voo.AlturaQueAtravessa);
		AfirmarEk("PREPARO: a guarda subiu DENTRO da janela do parry",
				  espelho.Combate.TempoDeGuarda <= MeleeResolver.JanelaContra + 1e-9 && feixe != null,
				  $"guarda ha {espelho.Combate.TempoDeGuarda:0.###} s");
		AfirmarEk("**O DEFLECT TIRA O RAIO DE QUEM ATIROU**: a mao dele FECHA no mesmo tique (nao sobra canal alimentando nada)",
				  !_canais.ContainsKey(raiador.Id) && feixe is { Canalizando: false });
		AfirmarEk($"...e o raio sai DE LADO ({AnguloDoDeflect:0} graus), solto, marcado por quem o defletiu",
				  feixe is { Vivo: true } && feixe.Desviador == espelho.Id && Math.Abs(feixe.Rumo.X) < 0.01f && Math.Abs(Math.Abs(feixe.Rumo.Y) - 1) < 0.01f,
				  $"vivo {feixe?.Vivo}, rumo ({feixe?.Rumo.X:0.##}, {feixe?.Rumo.Y:0.##}), desviador #{feixe?.Desviador}");
		AfirmarEk("...o relato e o do deflect (`Rebateu`: o escudo na tela)", Defletiu(relatos, espelho));
		int explosoes = ExplosoesDoFimDoAlcanceDeTeste;
		for (int i = 0; i < 30 * 10 && feixe is { Vivo: true }; i++) TiqueDoParry(espelho);
		AfirmarEk("**DEFLETIDO OU NAO, O RAIO ESTOURA NO FIM DO ALCANCE** -- e quem o defletiu nao perdeu vida nenhuma no caminho",
				  feixe is { Vivo: false, Fim: FimDeProjetil.Explodiu } && ExplosoesDoFimDoAlcanceDeTeste == explosoes + 1
				  && espelho.Combate.Corpo.Vida() >= vidaInicial - 1e-9,
				  $"vivo {feixe?.Vivo}, fim {feixe?.Fim}, vida {vidaInicial:0.##} -> {espelho.Combate.Corpo.Vida():0.##}");
		LimparEmbatesDaBancada();

		// ---------------------------------------------------------------- com menos que o dobro, nao ha deflect
		var (feixeIgual, _, quase, relatosQuase, _) = RaioContraOParry("Quase", bpDoDefensor: 7_500);
		AfirmarEk("CONTRA-EXEMPLO: com uma vez e meia o poder do tiro, a MESMA guarda no tempo certo nao deflete -- o raio continua em quem atirou",
				  feixeIgual is { Vivo: true, Desviador: 0 } && !Defletiu(relatosQuase, quase) && quase.Combate.TempoDeGuarda <= MeleeResolver.JanelaContra + 1e-9,
				  $"vivo {feixeIgual?.Vivo}, desviador #{feixeIgual?.Desviador}");
		LimparEmbatesDaBancada();

		// ---------------------------------------------------------------- a esfera
		var (bola, atirador, rebatedor, relatosBola, velocidadeDaIda) = BolaContraOParry("Rebatedor", noTempo: true, bpDoDefensor: 10_000);
		double vidaDoAtirador = atirador.Combate.Corpo.Vida();
		AfirmarEk("**A ESFERA DEFLETIDA VOLTA PRA QUEM ATIROU**: ela passa a ser de quem a defletiu (dono e poder), aponta pro atirador, na velocidade em que veio e com o alcance renovado",
				  bola.Vivo && bola.Dono == rebatedor.Id && bola.Desviador == rebatedor.Id && Defletiu(relatosBola, rebatedor)
				  && bola.Rumo.X < -0.99f && Math.Abs(bola.Bp - rebatedor.Ficha.expressedBP) < 1e-6
				  && Math.Abs(bola.SegundosPorTile - velocidadeDaIda) < 1e-9 && Math.Abs(bola.Distancia - bola.MaxDistancia) < 1e-9
				  && rebatedor.Combate.Corpo.Vida() >= 100 - 1e-9,
				  $"vivo {bola.Vivo}, dono #{bola.Dono} (rebatedor #{rebatedor.Id}), rumo ({bola.Rumo.X:0.##}, {bola.Rumo.Y:0.##}), alcance {bola.Distancia:0.#}/{bola.MaxDistancia:0.#}");
		for (int i = 0; i < 30 * 6 && bola.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
		AfirmarEk("...e ACERTA quem a atirou: o atirador perde vida com a propria esfera, e quem a defletiu nao perde nada",
				  bola.Fim == FimDeProjetil.Acertou && atirador.Combate.Corpo.Vida() < vidaDoAtirador - 1e-6 && rebatedor.Combate.Corpo.Vida() >= 100 - 1e-9,
				  $"fim {bola.Fim}; atirador {vidaDoAtirador:0.##} -> {atirador.Combate.Corpo.Vida():0.##}");
		LimparEmbatesDaBancada();

		// (A FAIXA DE CORREDOR DESTA CENA E DEVOLVIDA: a bancada ja gastava o mapa ate o fim, e com uma faixa a menos a
		//  familia 11 ficava sem o corredor de 18 tiles dela. Os corpos saem na `LimparEmbatesDaBancada` logo abaixo.)
		int faixaDaCenaDoDefeito = _pjProximoCorredor;
		EsferaDefletidaSaiDeLadoDeTeste = true;
		try
		{
			var (deLado, atiradorDeLado, _, _, _) = BolaContraOParry("DeLado", noTempo: true, bpDoDefensor: 10_000);
			AfirmarEk("(defeito injetado: a esfera defletida sai de lado) ela nao troca de dono e nunca chega em quem a atirou -- a primeira entrega, que a regua de cima reprova",
					  deLado.Dono == atiradorDeLado.Id && deLado.Desviador != 0 && deLado.Fim != FimDeProjetil.Acertou
					  && atiradorDeLado.Combate.Corpo.Vida() >= 100 - 1e-9,
					  $"fim {deLado.Fim}, dono #{deLado.Dono}, desviador #{deLado.Desviador}");
		}
		finally { EsferaDefletidaSaiDeLadoDeTeste = false; }
		LimparEmbatesDaBancada();

		// (na mesma faixa devolvida) O DEFLECT E O GESTO, e nao a guarda: com o dobro do poder mas a guarda SEGURADA --
		// erguida antes, fora da janela -- a esfera so encontra a guarda.
		var (bolaNaGuarda, atiradorDaGuarda, segurador, relatosDaGuarda, _) = BolaContraOParry("Segurador", noTempo: false, bpDoDefensor: 10_000);
		AfirmarEk("CONTRA-EXEMPLO: com o dobro do poder e a guarda SEGURADA (fora da janela do gesto), a esfera NAO e defletida nem volta -- ela acaba na guarda, e continua sendo de quem atirou",
				  !Defletiu(relatosDaGuarda, segurador) && bolaNaGuarda.Dono == atiradorDaGuarda.Id && bolaNaGuarda.Desviador == 0
				  && bolaNaGuarda.Fim == FimDeProjetil.Acertou && atiradorDaGuarda.Combate.Corpo.Vida() >= 100 - 1e-9,
				  $"fim {bolaNaGuarda.Fim}, dono #{bolaNaGuarda.Dono}, desviador #{bolaNaGuarda.Desviador}");
		LimparEmbatesDaBancada();
		_pjProximoCorredor = faixaDaCenaDoDefeito;

		var (bolaIgual, atiradorIgual, parelho, relatosParelho, _) = BolaContraOParry("Parelho", noTempo: true);
		AfirmarEk("CONTRA-EXEMPLO: de poder IGUAL, a guarda no tempo certo so apara a bola (o deflect pede o dobro)",
				  !Defletiu(relatosParelho, parelho) && bolaIgual.Dono == atiradorIgual.Id && bolaIgual.Desviador == 0,
				  $"fim {bolaIgual.Fim}");
		LimparEmbatesDaBancada();

		// ---------------------------------------------------------------- o jogo de antes
		KiContraCorpo.RegraDeAntesDeTeste = true;
		try
		{
			var (_, _, deAntes, relatosDeAntes, _) = RaioContraOParry("DeAntes", bpDoDefensor: 5_000);
			AfirmarEk("(defeito injetado: a porta de antes, igual ou mais forte) de poder igual o raio e defletido -- a regua de cima reprovaria",
					  Defletiu(relatosDeAntes, deAntes));
		}
		finally { KiContraCorpo.RegraDeAntesDeTeste = false; }
		LimparEmbatesDaBancada();
	}

	/// <summary>Um tique de parry: canal, projetil, embate e o relogio da guarda de quem defende.</summary>
	private void TiqueDoParry(ServerPlayer defensor)
	{
		TickDosCanaisDeKi(Protocol.TickSeconds);
		TickDosProjeteis(Protocol.TickSeconds);
		TickDosEmbatesDeKi(Protocol.TickSeconds);
		defensor.Combate.Tick(Protocol.TickSeconds);   // o relogio da guarda anda como no jogo
	}

	/// <summary>
	/// UM RAIO CONTRA QUEM ACERTA O TEMPO DA GUARDA, ate o primeiro contato. O atirador tem 5.000; a guarda sobe
	/// na <see cref="HoraDoParry"/>, e o laco para no tique em que a cabeca encosta (plantada, desviada ou em
	/// embate). Os relatos do golpe sao lidos do fio (`EscutaDeGolpes` + `LerGolpe`), como o cliente os le.
	/// </summary>
	/// <param name="alcanceTiles">
	/// O alcance do raio. O padrao e DE SOBRA pras cenas que so vao ate o primeiro contato (o defensor esta a 8
	/// tiles); a do fim do alcance pede curto, pra o pedaco defletido estourar dentro da medida.
	/// </param>
	/// <param name="altura">
	/// A que altura os dois estao. A cena do fim do alcance roda DE CIMA (`Voo.AlturaQueAtravessa`): o pedaco
	/// defletido sai de lado, pra fora do corredor, e o que ela mede e o alcance -- nao o que o mapa tem em volta.
	/// </param>
	private (Projetil? Feixe, ServerPlayer Atirador, ServerPlayer Defensor, List<Protocol.HitEvent> Relatos, double VidaInicial)
		RaioContraOParry(string nome, double bpDoDefensor, double alcanceTiles = 100, float altura = 0f)
	{
		Vec2 chao = CorredorLivre(24);
		ServerPlayer atirador = Forjar($"Raiador{nome}", chao, bp: 5_000);
		atirador.Facing = Facing.East;
		atirador.Ficha.Ki = atirador.Ficha.MaxKi;
		ServerPlayer defensor = Forjar(nome, new Vec2(chao.X + 8 * ZoneCollision.TileSize, chao.Y), bp: bpDoDefensor);
		defensor.Ficha.Ki = defensor.Ficha.MaxKi;
		atirador.Altitude = defensor.Altitude = altura;
		double vidaInicial = defensor.Combate.Corpo.Vida();

		// O RAIO SAI COM O GOLPE DE BANCADA NUM IGUAL DE GUARDA ERGUIDA (`QueFereUmIgual`: 12 por batida). As cenas
		// daqui medem tambem o que acontece quando o deflect NAO sai (o raio pega quem ergueu a guarda sem ter o dobro),
		// e a base subiu em 2026-10-08, quando um Ki Wave de producao entre iguais, ~5,5 por batida, caia no corte dos
		// fracos (`DanoDeKi.CorteDoFraco`) e nao feria ninguem. O corte por dano saiu em 2026-10-12 -- hoje esse Ki
		// Wave fere um igual --, e o 12 ficou: le-se sem derrubar o boneco, e e golpe tambem no jogo de antes, que os
		// contra-exemplos repoem (ver `BaseQueFereUmIgual`).
		ReceitaDeProjetil receita = QueFereUmIgual(SemDeflexao(), atirador, deGuarda: true);
		receita.AlcanceTiles = alcanceTiles;
		Canalizar(atirador, "Ki_Wave", 10 * atirador.Ficha.BaseDrain(), receita);
		EscutaDeGolpes = [];
		Projetil? feixe = null;
		try
		{
			for (int i = 0; i < 30 * 8; i++)
			{
				feixe ??= ProjeteisDaZona(atirador.Zone.Hash).FirstOrDefault(q => q.Dono == atirador.Id && q.Tipo == TipoDeProjetil.Beam);
				if (feixe != null && !defensor.Combate.Bloqueando && HoraDoParry(feixe, defensor)) defensor.Combate.Guardar(true);
				TiqueDoParry(defensor);
				if (feixe != null && (feixe.Encostado || feixe.Desviador != 0 || !feixe.Vivo || _emEmbateDeKi.ContainsKey(defensor.Id))) break;
			}
			return (feixe, atirador, defensor, EscutaDeGolpes.Where(g => g.Cheio).Select(g => LerGolpe(g.Fio)).ToList(), vidaInicial);
		}
		finally { EscutaDeGolpes = null; }
	}

	/// <summary>
	/// UMA BOLA CONTRA QUEM DEFENDE, com forca igual (5.000 x 5.000). `noTempo` = a guarda sobe na
	/// <see cref="HoraDoParry"/>; falso = ela foi SEGURADA antes (<see cref="SegurarAGuarda"/>). O laco para quando a
	/// bola troca de dono ou acaba. Devolve os relatos do golpe ATE ali (lidos do fio) e a velocidade com que ela
	/// saiu da mao.
	/// </summary>
	private (Projetil Bola, ServerPlayer Atirador, ServerPlayer Defensor, List<Protocol.HitEvent> Relatos, double Velocidade)
		BolaContraOParry(string nome, bool noTempo, double bpDoDefensor = 5_000)
	{
		Vec2 chao = CorredorLivre(20);
		ServerPlayer atirador = Forjar($"Bolador{nome}", chao, bp: 5_000);
		atirador.Facing = Facing.East;
		atirador.Ficha.Ki = atirador.Ficha.MaxKi;
		ServerPlayer defensor = Forjar(nome, new Vec2(chao.X + 6 * ZoneCollision.TileSize, chao.Y), bp: bpDoDefensor);
		defensor.Ficha.Ki = defensor.Ficha.MaxKi;
		if (!noTempo) SegurarAGuarda(defensor);

		EscutaDeGolpes = [];
		try
		{
			// A BOLA SAI COM O GOLPE DE BANCADA NUM IGUAL DE GUARDA ERGUIDA (12): o controle mede a bola que ACERTA a
			// guarda segurada e sai como bloqueio, e a devolvida tem que ferir quem a atirou. A base subiu em 2026-10-08,
			// quando com a base 1 as duas caiam no corte dos fracos (`DanoDeKi.CorteDoFraco`) e nenhuma era golpe. O
			// corte por dano saiu em 2026-10-12 (entre iguais toda bola fere), e o 12 ficou: e golpe tambem no jogo de
			// antes, que os contra-exemplos desta familia repoem.
			Projetil bola = Disparar(atirador, new ReceitaDeProjetil
			{
				Tipo = TipoDeProjetil.Blast, BaseDano = BaseQueFereUmIgual(atirador, TipoDeProjetil.Blast, deGuarda: true),
				Velocidade = 1, AlcanceTiles = 20, Deflectivel = false,
				Nome = "bola de parry",
			});
			double velocidade = bola.SegundosPorTile;
			for (int i = 0; i < 30 * 6 && bola.Vivo && bola.Dono == atirador.Id; i++)
			{
				if (noTempo && !defensor.Combate.Bloqueando && HoraDoParry(bola, defensor)) defensor.Combate.Guardar(true);
				TickDosProjeteis(Protocol.TickSeconds);
				defensor.Combate.Tick(Protocol.TickSeconds);
			}
			return (bola, atirador, defensor, EscutaDeGolpes.Where(g => g.Cheio).Select(g => LerGolpe(g.Fio)).ToList(), velocidade);
		}
		finally { EscutaDeGolpes = null; }
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

		// ============================ O FARO E TEMPO DE AVISO, E ANDA COM A PRESSA DO RAIO ============================
		// Os 7 tiles do `BCL_NPC_DETECT` eram 0,7 s antes de o raio do DM chegar. Com o raio no dobro da velocidade
		// (dono, 2026-10-08) e o faro parado, o NPC so veria o raio na metade desse tempo. Um NPC a 12 tiles do
		// atirador -- alem do faro do DM, dentro do faro na pressa do raio -- tem que ve-lo (`Projetil.AvisoDe`).
		// ===============================================================================================================
		ServerPlayer deLonge = Forjar("RoboDeLonge", new Vec2(pista.X + 12 * ZoneCollision.TileSize, pista.Y), bp: 50_000);
		Projetil raioVindo = _canais[agressor.Id].Raio!;
		float ateORobo = (deLonge.Pos - raioVindo.Pos).Length;
		float faroDoDm = (float)(EmbateDeKi.TilesDeFaro * ZoneCollision.TileSize);
		AfirmarEk("(preparo) o NPC de longe esta ALEM dos 7 tiles do faro do DM e dentro do faro na pressa do raio",
				  ateORobo > faroDoDm && ateORobo <= faroDoDm * raioVindo.Pressa,
				  $"{ateORobo / ZoneCollision.TileSize:0.#} tiles; pressa {raioVindo.Pressa:0.#}");
		AfirmarEk("o NPC de longe VE o raio: o aviso em segundos e o do DM, e por isso o faro anda com a pressa do raio",
				  VemFeixeContraMim(deLonge, out _));
		Projetil.AvisoSemPressaDeTeste = true;
		try
		{
			AfirmarEk("(defeito injetado: o faro parado nos 7 tiles do DM) o mesmo raio, vindo, NAO e visto pelo NPC de longe -- "
					  + "ele so o veria com metade do aviso",
					  !VemFeixeContraMim(deLonge, out _) && VemFeixeContraMim(npc, out _));
		}
		finally { Projetil.AvisoSemPressaDeTeste = false; }

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
	// 9b) O PRAZO E DO RAIO, E NAO DO CORPO (2026-10-07)
	// =====================================================================
	/// <summary>
	/// `var/started = world.time` abre o `npc_beam_loop` (`BeamClash.dm:427`) e e LOCAL do laco: la os doze
	/// segundos (`BCL_NPC_BEAM_TIME`, `:430`) sao do RAIO, e cada raio conta os seus do zero.
	///
	/// ============================ O DEFEITO QUE ESTA FAMILIA GUARDA ============================
	/// Aqui o relogio mora num dicionario por CORPO (`_raioDaIaAte`), e a conta de um raio derrubado por
	/// outra porta que nao o prazo -- golpe, nocaute, Ki, parede, fim de disputa, troca de zona -- ficava pra
	/// tras sempre que o mundo ficava sem canal nenhum: a faxina do `TickDoPrazoDeRaioDaIa` mora DEPOIS de
	/// um `if (_canais.Count == 0) return`. O raio seguinte nascia com o tempo do anterior ja gasto; sobrando
	/// menos que a carga, o canal fechava ANTES de a cabeca nascer -- o NPC pagava o Ki e nao atirava. Foi a
	/// tomada de Namek do trailer 2: *"os dois DISPARAM"*, e so o raio do Freeza saiu.
	/// =========================================================================================
	///
	/// A CENA E A DO DEFEITO, no pior caso dele: um NPC SOZINHO (o unico canal do mundo e o dele) segura o
	/// raio ate faltar MEIA CARGA pro prazo, um golpe o derruba pelo funil de producao e ele atira de novo.
	/// Duas vezes: com um segundo de mundo sem canal nenhum no meio (o caso do trailer), e reatirando NO MESMO
	/// TIQUE -- o soco de um jogador se resolve no pacote (`C2S.Action` -> `Atacar`, antes do `Tick`) e o corpo
	/// dirigido pode reatirar no `TickDosCorposSemDono` desse mesmo tique, sem o prazo ter sido tiqueado entre
	/// os dois raios.
	///
	/// O CONTRA-EXEMPLO E O JOGO DE ANTES (`EmbateDeKi.PrazoHerdadoDeTeste`): a mesma cena, e o segundo raio
	/// fecha na carga.
	/// </summary>
	private void OPrazoEDoRaioNaoDoCorpo()
	{
		GD.Print("[embateki] -- 9b) O PRAZO DO RAIO DE NPC E POR RAIO: CADA UM COMECA DO ZERO");

		var depois = OSegundoRaioDoNpc(tiquesSemCanal: 30);
		AfirmarEk("(preparo) um NPC SOZINHO segura o raio ate faltar meia carga pro prazo -- o unico canal do mundo e o dele",
				  depois.Sozinho, $"carga {depois.Carga:0.##}s, segurou {depois.Segurou:0.##}s");
		AfirmarEk("(preparo) um golpe derruba o raio -- outra porta que nao o prazo -- e nao sobra canal NENHUM no mundo",
				  depois.OGolpeDerrubou);
		AfirmarEk($"o raio SEGUINTE comeca do zero: a cabeca nasce e ele dura os {EmbateDeKi.SegundosDeFeixeDeNpc}s inteiros",
				  depois.ACabecaNasceu && Math.Abs(depois.Durou - EmbateDeKi.SegundosDeFeixeDeNpc) < 0.5,
				  $"cabeca nasceu: {depois.ACabecaNasceu}; durou {depois.Durou:0.##}s");

		var noMesmoTique = OSegundoRaioDoNpc(tiquesSemCanal: 0);
		AfirmarEk("...e tambem reatirando no MESMO tique em que o raio caiu (sem tique do prazo no meio)",
				  noMesmoTique.Sozinho && noMesmoTique.OGolpeDerrubou && noMesmoTique.ACabecaNasceu
				  && Math.Abs(noMesmoTique.Durou - EmbateDeKi.SegundosDeFeixeDeNpc) < 0.5,
				  $"cabeca nasceu: {noMesmoTique.ACabecaNasceu}; durou {noMesmoTique.Durou:0.##}s");

		// O JOGO DE ANTES: o raio novo herda a conta do anterior. A mesma cena, o mesmo golpe.
		EmbateDeKi.PrazoHerdadoDeTeste = true;
		try
		{
			var herdado = OSegundoRaioDoNpc(tiquesSemCanal: 30);
			AfirmarEk("(defeito injetado: o raio novo herda o prazo do anterior) o segundo raio fecha NA CARGA -- o NPC paga o Ki e nao atira",
					  herdado.Sozinho && herdado.OGolpeDerrubou && herdado.PagouOKi
					  && !herdado.ACabecaNasceu && herdado.Durou < herdado.CargaDoSegundo,
					  $"pagou: {herdado.PagouOKi}; cabeca nasceu: {herdado.ACabecaNasceu}; "
					  + $"durou {herdado.Durou:0.##}s de uma carga de {herdado.CargaDoSegundo:0.##}s");
		}
		finally { EmbateDeKi.PrazoHerdadoDeTeste = false; }

		LimparEmbatesDaBancada();
	}

	/// <summary>
	/// O ROTEIRO DA FAMILIA 9b, medido: o primeiro raio seguro ate faltar meia carga pro prazo, o golpe,
	/// <paramref name="tiquesSemCanal"/> tiques de mundo vazio e o SEGUNDO raio -- a cabeca nasceu? quanto
	/// o canal durou?
	///
	/// OS TIQUES SAO OS DOIS DA MEDIDA DO PRAZO NA FAMILIA 9 (canal + prazo), e os projeteis ficam parados
	/// de proposito: com o `TickDosProjeteis` o raio morreria no alcance (30 tiles a 0,1 s cada) e levaria o
	/// canal junto em tres segundos -- o que se mediria seria o alcance, e nao o relogio.
	///
	/// O TANQUE VOLTA A CHEIO ANTES DO SEGUNDO RAIO pelo mesmo motivo: sustentar doze segundos custa Ki, e
	/// um canal que fecha por falta dele mede o tanque.
	/// </summary>
	private (bool Sozinho, bool OGolpeDerrubou, bool PagouOKi, bool ACabecaNasceu, double Durou,
			 double Carga, double Segurou, double CargaDoSegundo)
		OSegundoRaioDoNpc(int tiquesSemCanal)
	{
		LimparEmbatesDaBancada();

		ServerPlayer npc = Forjar("Sozinho", CorredorLivre(20), bp: 50_000);
		npc.Facing = Facing.East;
		DarSkillDoRaio(npc);

		// O PRIMEIRO RAIO, pelo verb de producao -- o mesmo `UsarTecnica` do contra-feixe e do cerebro.
		npc.Ficha.Ki = npc.Ficha.MaxKi;
		UsarTecnica(npc, "Ki_Wave");
		double carga = _canais.GetValueOrDefault(npc.Id)?.CargaRestante ?? 0;

		// ...SEGURO ATE FALTAR MEIA CARGA PRO PRAZO: o que sobraria de heranca nao deixa a proxima cabeca nascer.
		int tiques = (int)Math.Round((EmbateDeKi.SegundosDeFeixeDeNpc - carga / 2) / Protocol.TickSeconds);
		double segurou = 0;
		for (int i = 0; i < tiques && _canais.ContainsKey(npc.Id); i++)
		{
			UmTiqueDoCanalDeNpc();
			segurou += Protocol.TickSeconds;
		}
		bool sozinho = carga > 0 && _canais.Count == 1 && _canais.GetValueOrDefault(npc.Id)?.Raio is { Vivo: true };

		// CAI POR OUTRA PORTA QUE NAO O PRAZO: o funil de quem apanha com o raio na mao (soco, tiro, arremesso
		// e agarrao chamam este mesmo metodo). Depois o mundo anda sem canal nenhum.
		AoLevarGolpeComRaioNaMao(npc, null);
		bool derrubou = sozinho && _canais.Count == 0;
		for (int i = 0; i < tiquesSemCanal; i++) UmTiqueDoCanalDeNpc();

		// O SEGUNDO RAIO.
		npc.Ficha.Ki = npc.Ficha.MaxKi;
		UsarTecnica(npc, "Ki_Wave");
		bool pagou = npc.Ficha.Ki < npc.Ficha.MaxKi;
		double cargaDoSegundo = _canais.GetValueOrDefault(npc.Id)?.CargaRestante ?? 0;

		bool nasceu = false;
		double durou = 0;
		for (int i = 0; i < 30 * 20 && _canais.TryGetValue(npc.Id, out CanalDeKi? c); i++)
		{
			UmTiqueDoCanalDeNpc();
			durou += Protocol.TickSeconds;
			nasceu |= c.Raio != null;
		}
		return (sozinho, derrubou, pagou, nasceu, durou, carga, segurou, cargaDoSegundo);
	}

	/// <summary>Os dois tiques que abrem e fecham o canal de quem nao tem teclado, na ordem em que o jogo os chama.</summary>
	private void UmTiqueDoCanalDeNpc()
	{
		TickDosCanaisDeKi(Protocol.TickSeconds);
		TickDoPrazoDeRaioDaIa(Protocol.TickSeconds);
	}

	// =====================================================================
	// AS PECAS DA BANCADA
	// =====================================================================
	/// <summary>O `Ki_Wave` de producao, na receita que o verb monta (`beams.dm:270`).</summary>
	/// <remarks>
	/// ELE E O RAIO **COMUM**, E CONTINUA DEFLETIVEL DE PROPOSITO: a cena da ordem (o fim do
	/// `OFeixeContraAGuarda`, pelo ajudante `RaioNaGuardaSegurada`) mede
	/// justamente que *"contra um raio COMUM, quem podia defletir defende de graca"* -- desligar o
	/// sorteio aqui apagaria a unica prova de que a defesa de ki vem antes do embate. Quem precisa do
	/// dado fora do caminho vira o knob no proprio disparo: ver o <see cref="SemDeflexao"/>.
	/// </remarks>
	private static ReceitaDeProjetil RaioDeTeste() => new()
	{
		Tipo = TipoDeProjetil.Beam, BaseDano = 1, Velocidade = 1,
		AlcanceTiles = 30, CargaMinima = 1, Nome = "Onda de Ki",
	};

	/// <summary>
	/// O MESMO RAIO, SEM O DADO DA DEFLEXAO -- `deflectable = 0` do DM (ver `ReceitaDeProjetil`), um
	/// campo de producao que existe pra tecnicas em que *"nao ha o que defletir, so aguentar"*.
	///
	/// ============================ POR QUE ELE PRECISA EXISTIR ============================
	/// A chance de defletir e a razao entre as duas forcas (`objects.dm:333`), calculada em
	/// `DanoDeKi.ChanceDeDeflexao` e sorteada DUAS vezes por impacto no `Acertar` (o passo 3 dele: o
	/// raspao com a metade da chance e a deflexao com ela inteira -- esta, desde 2026-10-12, so pra quem
	/// tem o dobro do poder do tiro). MEDIDO com os corpos desta bancada -- feixe de
	/// `base_damage` 1, dois duelistas de 50.000: **1% por impacto**, e a linha *"o perdedor LEVOU o
	/// ataque"* (`:628`) depende do feixe do vencedor chegar inteiro no corpo dele. E dez vezes o
	/// sorteio de 0,0999% que derrubou a `--tecnicateste` em 2026-09-02 -- mesma familia de defeito,
	/// mesmo conserto, e o motivo e o que o `RaioDaBancada` da `--projetilteste` escreve:
	/// um sorteio no meio da medicao mede o dado.
	///
	/// E NAO DA PRA DESLIGAR PELO CORPO: `Fighter.Statify` calcula o `Rkidef` com piso
	/// (`max(kidef, 0.1)`, `Core/Stats/Fighter.Statify.cs:88`), entao `Ekidef` de um corpo vivo e
	/// SEMPRE maior que zero e a chance nunca e nula. O knob do projetil e o unico lever que existe.
	/// ==================================================================================
	/// </summary>
	private static ReceitaDeProjetil SemDeflexao()
	{
		ReceitaDeProjetil r = RaioDeTeste();
		r.Deflectivel = false;
		return r;
	}

	/// <summary>
	/// A GUARDA SEGURADA -- erguida ha MAIS tempo que a janela do parry, pelo relogio de producao
	/// (`CombatState.Tick`, o mesmo que o `TickCombate` roda).
	///
	/// ============================ POR QUE NAO BASTA `Guardar(true)` ============================
	/// O relogio da guarda (`TempoDeGuarda`) so anda no tique de combate, e os lacos desta bancada rodam canal,
	/// projetil e embate -- nunca o combate. Ate 2026-09-25 isso nao importava; com o parry de ki
	/// (`GameServer.ParryDeKi.cs`) uma guarda erguida com o relogio parado fica "acabou de subir" pra sempre, e o
	/// raio que chegasse oito tiles depois seria DESVIADO -- a cena da guarda velha viraria a cena do parry sem
	/// ninguem pedir. No jogo, quem ergue a guarda e espera o raio chegar segura por mais que 0,25 s.
	/// ==========================================================================================
	/// </summary>
	private static void SegurarAGuarda(ServerPlayer g)
	{
		g.Combate.Guardar(true);
		while (g.Combate.TempoDeGuarda <= MeleeResolver.JanelaContra) g.Combate.Tick(Protocol.TickSeconds);
	}

	/// <summary>
	/// O GESTO DO PARRY: a guarda sobe quando o ki esta a uns QUATRO TIQUES de encostar (~0,13 s, dentro da
	/// janela de 0,25 s). O contato do raio e a frente da cabeca na beirada do corpo; o da bola, o raio dela.
	/// Errar a conta por um tique ou dois nao muda nada; errar por muito deixa a guarda subir DEPOIS do contato,
	/// e ai a cena reprova alto, na linha do preparo.
	/// </summary>
	private static bool HoraDoParry(Projetil p, ServerPlayer defensor)
	{
		float porTique = (float)(ZoneCollision.TileSize / p.SegundosPorTile * Protocol.TickSeconds);
		float contato = p.Tipo == TipoDeProjetil.Beam ? Feixe.MeioCorpo + Feixe.AlcanceDaCabeca(p) : Projetil.RaioDeImpacto;
		return (p.Pos - defensor.Pos).Length <= contato + 4 * porTique;
	}

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

		// OS DOIS FEIXES DA DISPUTA SAEM SEM O DADO DA DEFLEXAO, e isso nao muda o embate: a disputa
		// nasce do encontro de dois feixes e nao consulta `Deflectivel` em lugar nenhum. O que muda e
		// o DEPOIS -- acabado o embate, o feixe do vencedor avanca no corpo do perdedor e cai no
		// `Acertar`, onde o sorteio de 1% por impacto (ver <see cref="SemDeflexao"/>) trocaria a linha
		// "o perdedor LEVOU o ataque" (`:628`) por um tiro aparado sem nada errado no jogo.
		//
		// A cena que PRECISA do raio defletivel (a ordem entre deflexao e embate, no fim do
		// `OFeixeContraAGuarda`) canaliza o dela na mao com o `RaioDeTeste` (`RaioNaGuardaSegurada`) -- e
		// por isso o knob esta aqui, e nao na receita comum.
		//
		// E SAEM COM A BASE DE UM GOLPE (2026-10-08). O Ki Wave de producao (`base` 1) entre dois iguais faz ~5,5
		// por batida, e com o corte dos fracos (`DanoDeKi.CorteDoFraco`, `objects.dm:355-357`; ate 2026-10-12) isso
		// nao era golpe: o feixe do vencedor chegava no perdedor e nao o feria -- como no DM, onde a cabeca que vence
		// a disputa acerta pelo mesmo `Bump` (`BeamClash.dm`, `push_phase`). O DEPOIS da disputa so se mede com um
		// raio que fere; a base sobe igual pros dois lados, entao a razao das forcas no embate nao muda. (O corte
		// por dano saiu, e hoje os ~5,5 ja feririam um igual. A base de 12 ficou: e golpe tambem no jogo de antes,
		// que os contra-exemplos repoem -- ver `BaseQueFereUmIgual`.)
		Canalizar(a, "Ki_Wave", 10 * a.Ficha.BaseDrain(), QueFereUmIgual(SemDeflexao(), a));
		Canalizar(b, "Ki_Wave", 10 * b.Ficha.BaseDrain(), QueFereUmIgual(SemDeflexao(), b));

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

	// =====================================================================
	// 1b) AS CABECAS SE TOCAM SEM SE SOBREPOR (dono, 2026-09-07)
	// =====================================================================
	/// <summary>
	/// *"ao colidirem a cabeca deles sempre devem ficar se empurrando na colisao"* -- e a foto do dono
	/// mostrava uma em cima da outra. Mede-se a DISTANCIA entre as duas cabecas durante a disputa (um
	/// raio de cada lado do ponto de contato = dois raios), e o defeito injetado (as duas no mesmo
	/// ponto, que era o codigo de antes) tem que ficar vermelho.
	/// </summary>
	private void AsCabecasSeTocamSemSobrepor()
	{
		GD.Print("[embateki] -- 1b) AS CABECAS SE TOCAM SEM SE SOBREPOR (frente com frente)");
		LimparEmbatesDaBancada();
		(ServerPlayer a, ServerPlayer b, DisputaDeKi? d) = DoisRaiosDeFrente(12);
		_ = b;
		if (d?.A.Feixe == null || d.B.Feixe == null)
		{
			AfirmarEk("a disputa comecou com dois feixes", false);
			LimparEmbatesDaBancada();
			return;
		}

		const float Toque = 2f * Projetil.RaioDeImpacto;
		float maior = 0, menor = float.MaxValue;
		bool pontoNoMeio = true;
		for (int i = 0; i < 15 && _emEmbateDeKi.ContainsKey(a.Id); i++)
		{
			UmTiqueDoEncontro();
			float dist = (d.A.Feixe.Pos - d.B.Feixe.Pos).Length;
			maior = Math.Max(maior, dist);
			menor = Math.Min(menor, dist);
			Vec2 meio = (d.A.Feixe.Pos + d.B.Feixe.Pos) * 0.5f;
			if ((meio - d.Ponto).Length > 0.5f) pontoNoMeio = false;
		}
		AfirmarEk("durante a disputa as duas cabecas ficam a exatamente DOIS RAIOS uma da outra (frente com frente)",
				  _emEmbateDeKi.ContainsKey(a.Id) && Math.Abs(maior - Toque) < 0.5f && Math.Abs(menor - Toque) < 0.5f,
				  $"{menor:0.0}..{maior:0.0} px, esperado {Toque}");
		AfirmarEk("...e o ponto de encontro e o MEIO delas -- o contato", pontoNoMeio);

		// O DEFEITO INJETADO: as duas cabecas no mesmo ponto -- o codigo de antes, e a foto do dono.
		EmbateDeKi.CabecasNoMesmoPontoDeTeste = true;
		UmTiqueDoEncontro();
		float sobrepostas = (d.A.Feixe.Pos - d.B.Feixe.Pos).Length;
		EmbateDeKi.CabecasNoMesmoPontoDeTeste = false;
		AfirmarEk("DEFEITO INJETADO (cabecas no mesmo ponto) fica vermelho: a medida acima o pegaria",
				  sobrepostas < 1f && _emEmbateDeKi.ContainsKey(a.Id), $"{sobrepostas:0.0} px entre as cabecas");
		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 1c) CRUZAR NAO E DISPUTAR: QUEM BATE NO TRONCO ALHEIO ESPERA
	// =====================================================================
	/// <summary>
	/// *"no caso de 2 beams se cruzarem (um vir na vertical e outro na horizontal) eles nao vao entrar
	/// em clash, e o beam que bater no tronco do outro beam vai ficar parado ate o outro sair do
	/// caminho"*. Um raio pra LESTE com o tronco ja estendido, outro pra NORTE que chega nele.
	/// </summary>
	private void CruzarNaoEDisputar()
	{
		GD.Print("[embateki] -- 1c) CRUZAR NAO E DISPUTAR: quem bate no tronco alheio ESPERA o tronco sair");
		LimparEmbatesDaBancada();
		const int T = ZoneCollision.TileSize;

		(ServerPlayer a, ServerPlayer b, Projetil? pa, Projetil? pb, float linhaDoTronco) = DoisRaiosCruzados();
		if (pa == null || pb == null)
		{
			AfirmarEk("os dois raios cruzados nasceram (o mapa tem uma praca livre)", false);
			LimparEmbatesDaBancada();
			return;
		}

		// O DE NORTE chega no tronco do de leste. `linhaDoTronco` e o Y do eixo do raio de leste.
		int t = 0;
		while (!pb.Esperando && pb.Vivo && t++ < 60) UmTiqueDoEncontro();
		AfirmarEk("os dois raios se CRUZARAM e nao houve disputa nenhuma (nao vieram de frente)",
				  _disputas.Count == 0 && !pa.EmEmbate && !pb.EmEmbate);
		AfirmarEk("o raio que bateu no tronco alheio esta ESPERANDO, parado a um raio do tronco",
				  pb.Esperando && pb.Pos.Y >= linhaDoTronco + Projetil.RaioDeImpacto - 1f
				  && pb.Pos.Y <= linhaDoTronco + 2f * Projetil.RaioDeImpacto + 1f,
				  $"cabeca em y {pb.Pos.Y:0.0}, tronco em y {linhaDoTronco:0.0}, apos {t} tiques");

		Vec2 parou = pb.Pos;
		float cabecaDeLeste = pa.Pos.X;
		for (int i = 0; i < 10; i++) UmTiqueDoEncontro();
		AfirmarEk("...e fica parado enquanto o tronco esta la (o outro raio continua avancando)",
				  (pb.Pos - parou).Length < 0.01f && pa.Pos.X > cabecaDeLeste + T && pb.Esperando,
				  $"esperou {(pb.Pos - parou).Length:0.00} px; o de leste andou {pa.Pos.X - cabecaDeLeste:0} px");

		// O DE LESTE E SOLTO: a cauda dele anda e o tronco sai do caminho; o de norte retoma.
		SoltarCanal(a, "Ki_Wave");
		t = 0;
		while (pb.Vivo && pb.Pos.Y > linhaDoTronco - Projetil.RaioDeImpacto && t++ < 90) UmTiqueDoEncontro();
		AfirmarEk("quando o tronco sai do caminho o raio que esperava RETOMA e atravessa a linha",
				  pb.Vivo && pb.Pos.Y < linhaDoTronco - Projetil.RaioDeImpacto && !pb.Esperando,
				  $"y {pb.Pos.Y:0.0} apos {t} tiques (linha {linhaDoTronco:0.0})");
		_ = b;
		LimparEmbatesDaBancada();

		// O DEFEITO INJETADO: sem a espera, a cabeca atravessa o tronco como se ele nao existisse.
		Feixe.AtravessaTroncoDeTeste = true;
		(_, _, Projetil? pa2, Projetil? pb2, float linha2) = DoisRaiosCruzados();
		bool atravessou = false;
		for (int i = 0; i < 60 && pb2 is { Vivo: true } && pa2 is { Vivo: true }; i++)
		{
			UmTiqueDoEncontro();
			if (pb2.Pos.Y < linha2 - Projetil.RaioDeImpacto) { atravessou = true; break; }
		}
		Feixe.AtravessaTroncoDeTeste = false;
		AfirmarEk("DEFEITO INJETADO (sem a espera) fica vermelho: a cabeca atravessa o tronco alheio",
				  atravessou && _disputas.Count == 0);
		LimparEmbatesDaBancada();
	}

	/// <summary>
	/// Um raio pra LESTE com o tronco ja estendido e um pra NORTE que vai bater nele. Devolve os dois
	/// donos, os dois feixes e o Y do eixo do raio de leste.
	/// </summary>
	private (ServerPlayer, ServerPlayer, Projetil?, Projetil?, float) DoisRaiosCruzados()
	{
		const int T = ZoneCollision.TileSize;
		Vec2 canto = QuadradoLivre(14);
		ServerPlayer a = Forjar("Leste", new Vec2(canto.X + T, canto.Y + 4 * T), bp: 50_000);
		ServerPlayer b = Forjar("Norte", new Vec2(canto.X + 6 * T, canto.Y + 11 * T), bp: 50_000);
		a.Facing = Facing.East;
		b.Facing = Facing.North;
		a.Ficha.Ki = a.Ficha.MaxKi;
		b.Ficha.Ki = b.Ficha.MaxKi;
		_comTecladoDeTeste.Add(a.Id);
		_comTecladoDeTeste.Add(b.Id);

		// O DE LESTE PRIMEIRO, ate ter uns 5 tiles de tronco; so entao o de norte, com a carga MINIMA (dois
		// tiques) -- e a cena volta com ele recem-nascido. ERA POR TIQUES CONTADOS (40 e depois 25), e isso so
		// servia na velocidade antiga: no dobro dela o raio de leste esgotava os 30 tiles de alcance e se apagava
		// antes de o de norte chegar na linha -- a cena "espera o tronco" ficava sem tronco. Por ESTADO, ela e a
		// mesma em qualquer pressa: o de norte leva 6 tiles ate a linha, e nesse tempo o de leste passa dos 12
		// (o tronco cobre a coluna dele com folga) sem chegar perto do fim do alcance.
		Canalizar(a, "Ki_Wave", 10 * a.Ficha.BaseDrain(), SemDeflexao());
		Projetil? pa = null;
		for (int i = 0; i < 90 && (pa == null || (pa.Pos - pa.Cauda).Length < 5 * T); i++)
		{
			UmTiqueDoEncontro();
			pa ??= _canais.GetValueOrDefault(a.Id)?.Raio;
		}

		ReceitaDeProjetil ligeiro = SemDeflexao();
		ligeiro.CargaMinima = 0.05;
		Canalizar(b, "Ki_Wave", 10 * b.Ficha.BaseDrain(), ligeiro);
		Projetil? pb = null;
		for (int i = 0; i < 30 && pb == null; i++)
		{
			UmTiqueDoEncontro();
			pb = _canais.GetValueOrDefault(b.Id)?.Raio;
		}
		return (a, b, pa, pb, pa?.Pos.Y ?? 0f);
	}

	/// <summary>
	/// UM QUADRADO LIVRE de <paramref name="lado"/> tiles no mapa da bancada -- o corredor e uma linha,
	/// a praca e 3x3, e o cruzamento precisa de duas linhas inteiras. Devolve o canto de cima/esquerda.
	/// </summary>
	private Vec2 QuadradoLivre(int lado)
	{
		ZoneCollision? mapa = _pjMapa;
		if (mapa == null) return new Vec2(_pjProximoCorredor * 64 + 32, 32);

		for (int y = _pjProximoCorredor; y < 250 - lado; y++)
			for (int x = 4; x < 250 - lado; x++)
			{
				bool livre = true;
				for (int dy = 0; dy < lado && livre; dy++)
					for (int dx = 0; dx < lado && livre; dx++)
						livre &= mapa.ServeDeChao(x + dx, y + dy);
				if (!livre) continue;

				_pjProximoCorredor = y + lado + 2;
				return new Vec2(x * ZoneCollision.TileSize + 16, y * ZoneCollision.TileSize + 16);
			}

		AfirmarEk($"achei um quadrado livre de {lado}x{lado} no mapa da bancada", false, "varredura falhou");
		return new Vec2(64, 64);
	}

	// =====================================================================
	// 10) APANHAR NA DISPUTA E DESVANTAGEM; NINGUEM AGARRA QUEM DISPUTA
	// =====================================================================
	/// <summary>
	/// *"atacar um personagem que ta usando beam em colisao faz ele sofrer desvantagem no clash dele
	/// (nao da pra agarrar quem esta em colisao, mas se nao estiver da pra agarrar o usuario do beam)"*
	/// -- e, da outra frase do pedido, *"caso o personagem seja atacado, agarrado ou qualquer coisa
	/// desse tipo, ele cancela o beam dele na hora"*.
	/// </summary>
	private void ApanharNaDisputaEDesvantagem()
	{
		GD.Print("[embateki] -- 10) APANHAR NA DISPUTA E DESVANTAGEM, E NINGUEM AGARRA QUEM DISPUTA");
		LimparEmbatesDaBancada();
		const int T = ZoneCollision.TileSize;

		(ServerPlayer a, ServerPlayer b, DisputaDeKi? d) = DoisRaiosDeFrente(12);
		_ = b;
		if (d == null) { AfirmarEk("a disputa comecou", false); LimparEmbatesDaBancada(); return; }
		bool souA = d.A.Quem == a;
		double Meu() => souA ? d.Medidor : 100 - d.Medidor;

		// FORCAS PARELHAS, NINGUEM APERTANDO: o medidor fica parado (familia 2). E o fundo neutro da medida.
		for (int i = 0; i < 10; i++) UmTiqueDoEncontro();
		double antes = Meu();

		// UM TERCEIRO BATE EM `a` PELO FUNIL (o mesmo que soco, tiro, arremesso e agarrao chamam).
		ServerPlayer c = Forjar("Intrometido", new Vec2(a.Pos.X - T, a.Pos.Y), bp: 50_000);
		c.Facing = Facing.East;
		AoLevarGolpeComRaioNaMao(a, c);
		UmTiqueDoEncontro();
		double depois = Meu();
		double esperado = EmbateDeKi.ApertosQueUmGolpeCusta * EmbateDeKi.EmpurraoPorAperto;   // vantagem 1 entre iguais
		AfirmarEk("um golpe sofrido no meio da disputa pende o medidor pro OUTRO lado",
				  depois < antes - 0.5, $"{antes:0.0} -> {depois:0.0}");
		AfirmarEk($"...e pende o que `ApertosQueUmGolpeCusta` apertos do rival valem ({esperado:0.0} pontos entre iguais)",
				  Math.Abs((antes - depois) - esperado) < 0.5, $"caiu {antes - depois:0.00}");
		AfirmarEk("...e a disputa NAO cai: apanhar inclina, nao derruba (o `side_ok` continua sendo morrer/cair/soltar)",
				  _emEmbateDeKi.ContainsKey(a.Id) && _canais.ContainsKey(a.Id));

		// E PELO SOCO DE VERDADE: `Atacar` -> `AoLevarGolpeComRaioNaMao`. Soca todo tique (a recarga
		// recusa os de mais); um que ENCOSTE basta -- e o corpo perde vida quando encosta.
		double antesDoSoco = Meu();
		double vidaA = a.Combate!.Corpo.Vida();
		int tiques = 0;
		while (tiques++ < 120 && a.Combate.Corpo.Vida() >= vidaA && _emEmbateDeKi.ContainsKey(a.Id))
		{
			Atacar(c, Protocol.Golpe.Leve);
			TickCombate(Protocol.TickSeconds);
			UmTiqueDoEncontro();
		}
		bool encostou = a.Combate.Corpo.Vida() < vidaA;
		AfirmarEk("...e o soco de verdade passa pelo mesmo funil: o golpe que encosta move o medidor",
				  encostou && _emEmbateDeKi.ContainsKey(a.Id) && Meu() < antesDoSoco - 0.5,
				  $"encostou em {tiques} tiques: {encostou}; medidor {antesDoSoco:0.0} -> {Meu():0.0}");

		// NINGUEM AGARRA QUEM DISPUTA.
		AlternarAgarrao(c);
		AfirmarEk("agarrar quem esta na disputa e RECUSADO", a.AgarradoPorId == 0 && c.AgarrandoId == 0);
		AfirmarEk("...e a disputa continua de pe", _emEmbateDeKi.ContainsKey(a.Id));
		LimparEmbatesDaBancada();

		// ...MAS QUEM SO CANALIZA (sem disputa) PODE SER AGARRADO -- e o raio dele cai na hora.
		Vec2 chao = CorredorLivre(12);
		ServerPlayer e = Forjar("Canalizador", new Vec2(chao.X + 2 * T, chao.Y), bp: 50_000);
		e.Facing = Facing.East;
		e.Ficha.Ki = e.Ficha.MaxKi;
		ServerPlayer f = Forjar("Agarrador", new Vec2(chao.X + T, chao.Y), bp: 50_000);
		f.Facing = Facing.East;
		Canalizar(e, "Ki_Wave", 10 * e.Ficha.BaseDrain(), SemDeflexao());
		for (int i = 0; i < 40; i++) UmTiqueDoEncontro();
		AfirmarEk("(um canalizador com o raio na mao, fora de disputa)",
				  _canais.GetValueOrDefault(e.Id)?.Atirando == true);
		AlternarAgarrao(f);
		AfirmarEk("agarrar quem so canaliza PEGA -- e o raio dele cai na hora",
				  e.AgarradoPorId == f.Id && !_canais.ContainsKey(e.Id),
				  $"agarrado por {e.AgarradoPorId} (agarrador {f.Id}), canal {_canais.ContainsKey(e.Id)}");
		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 11) O SALTO DE PODER NO MEIO VIRA A DISPUTA (dono, 2026-10-07)
	// =====================================================================
	/// <summary>
	/// *"em colisao o beam deve usar o bp atual do usuario, entao se o usuario se transformar ou usar
	/// powerup no meio da colisao ele pode virar por conta do salto de poder"*.
	///
	/// A cena e a do pedido: o lado fraco PERDENDO, um aumento de poder de producao aceso no meio (o
	/// `Kaioken`, o mesmo verbo do jogador) e o encontro tem que voltar. NINGUEM APERTA LETRA: os dois
	/// tem teclado e nenhum responde, entao o unico motor do medidor e a deriva do poder
	/// (`(advA - advB) * BCL_DRIFT_PER_TICK`) -- o que se mede e o BP, sem QTE no meio pra confundir.
	///
	/// A FICHA ANDA NA MAO, uma vez, depois do Kaio-ken: em jogo quem escreve o `expressedBP` e o laco
	/// de fichas do servidor (5 Hz), e a bancada de boot nao o roda.
	///
	/// O CONTRA-EXEMPLO E O JOGO DE ANTES: com a vantagem congelada no comeco
	/// (`EmbateDeKi.VantagemCongeladaDeTeste`), o mesmo roteiro NAO vira -- a derrota que o dono viu
	/// numa tomada do trailer (*"o goku perdeu"*, com o Kaio-ken x4 aceso).
	/// </summary>
	private void OSaltoDePoderVira()
	{
		GD.Print("[embateki] -- 11) O SALTO DE PODER NO MEIO VIRA A DISPUTA (o BP do feixe e o de AGORA)");

		var vivo = SaltarNoMeio();
		AfirmarEk("a disputa comecou (o rival e 1,5x mais forte)", vivo.Comecou);
		if (!vivo.Comecou) return;

		AfirmarEk("sem ninguem apertar, o lado FRACO perde terreno: a deriva do poder anda contra ele",
				  vivo.NoSalto < vivo.NoComeco - 3 && vivo.VantagemAntes < 1,
				  $"medidor {vivo.NoComeco:0.0} -> {vivo.NoSalto:0.0}, vantagem {vivo.VantagemAntes:0.00}");
		AfirmarEk("o aumento de poder aceso NO MEIO entra no feixe: o BP dele e o do dono AGORA",
				  vivo.BpDoFeixeDepois > vivo.BpDoFeixeAntes * 1.5
				  && Math.Abs(vivo.BpDoFeixeDepois - vivo.BpDoDonoDepois) < vivo.BpDoDonoDepois * 1e-6,
				  $"feixe {vivo.BpDoFeixeAntes:0} -> {vivo.BpDoFeixeDepois:0}; o dono expressa {vivo.BpDoDonoDepois:0}");
		AfirmarEk("...a vantagem VIRA de lado (de menos de 1 pra mais de 1)",
				  vivo.VantagemAntes < 1 && vivo.VantagemDepois > 1,
				  $"{vivo.VantagemAntes:0.00} -> {vivo.VantagemDepois:0.00}");
		AfirmarEk("...e o encontro VOLTA: o medidor passa a andar pro outro lado",
				  vivo.NoFim > vivo.NoSalto + 5,
				  $"medidor {vivo.NoSalto:0.0} -> {vivo.NoFim:0.0}");

		// O JOGO DE ANTES: a vantagem escrita uma vez, no comeco. O mesmo roteiro, o mesmo Kaio-ken.
		EmbateDeKi.VantagemCongeladaDeTeste = true;
		var congelado = SaltarNoMeio();
		EmbateDeKi.VantagemCongeladaDeTeste = false;
		AfirmarEk("(defeito injetado: vantagem congelada no comeco) o mesmo Kaio-ken NAO vira a disputa",
				  congelado.Comecou && congelado.NoFim < congelado.NoSalto - 3
				  && Math.Abs(congelado.VantagemDepois - congelado.VantagemAntes) < 1e-9,
				  $"medidor {congelado.NoSalto:0.0} -> {congelado.NoFim:0.0}, "
				  + $"vantagem {congelado.VantagemAntes:0.00} -> {congelado.VantagemDepois:0.00}");
	}

	/// <summary>
	/// O ROTEIRO DA FAMILIA 11, medido: tres segundos perdendo, o Kaio-ken, tres segundos depois.
	/// O medidor sai do ponto de vista de quem acendeu o Kaio-ken (100 = ele engoliu).
	/// </summary>
	private (bool Comecou, double NoComeco, double NoSalto, double NoFim, double VantagemAntes,
			 double VantagemDepois, double BpDoFeixeAntes, double BpDoFeixeDepois, double BpDoDonoDepois)
		SaltarNoMeio()
	{
		LimparEmbatesDaBancada();

		(ServerPlayer eu, ServerPlayer outro, DisputaDeKi? d) = DoisRaiosDeFrente(12, bpDoSegundo: 50_000 * 1.5);
		_ = outro;
		if (d == null) return default;
		LadoDeKi meu = d.A.Quem == eu ? d.A : d.B;
		double Meu() => meu == d.A ? d.Medidor : 100 - d.Medidor;

		double noComeco = Meu();
		for (int i = 0; i < 30 * 3; i++) UmTiqueDoEncontro();
		double noSalto = Meu(), vantagemAntes = meu.Vantagem, feixeAntes = meu.Feixe?.Bp ?? 0;

		// x100 e o teto da tecnica (4x de poder, `KaiokenMult`): contra um rival 1,5x mais forte, sobra.
		Kaioken(eu, 100);
		eu.Ficha.Tick(agoraMs: NowMs());

		for (int i = 0; i < 30 * 3 && _emEmbateDeKi.ContainsKey(eu.Id); i++) UmTiqueDoEncontro();
		var medido = (true, noComeco, noSalto, Meu(), vantagemAntes, meu.Vantagem,
					  feixeAntes, meu.Feixe?.Bp ?? 0, eu.Ficha.expressedBP);

		Kaioken(eu, 0);   // o mesmo botao desliga: o corpo sai da bancada sem buff pendurado
		LimparEmbatesDaBancada();
		return medido;
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
