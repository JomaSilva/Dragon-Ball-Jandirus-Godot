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
///     a vitoria acertando tudo; em 6x, nao da).
///  4. O FIM E ASSIMETRICO: quem perde LEVA o feixe, pelo funil de dano de producao.
///  5. DA PRA SAIR, e sair e perder (soltar o canal / abaixar a guarda).
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
			OFimEAssimetrico();
			SairDoEmbateEPerder();
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
		// NUNCA SAI DE PERTO DO MEIO: se uma das escalas estivesse errada por pouco que fosse, 22
		// segundos de empurrao a levariam a uma das pontas.
		DisputarComLetras(1.0, euApertando: true, oOutroEBot: true, out double desvio, out _);
		AfirmarEk("o jogador que acerta TUDO fica lado a lado com o NPC mediano (as escalas batem)",
				  desvio < 20, $"o medidor se afastou {desvio:0.#} pontos do meio em 22 s");

		// ============================ O QUE O TECLADO COMPRA, MEDIDO ============================
		// Nao ha um numero escolhido aqui. A bancada roda o MESMO encontro com desniveis crescentes e
		// imprime onde a vitoria vira -- e o que se afirma sao so as duas pontas, que sao promessas de
		// desenho: em forcas parelhas o teclado decide, e no teto da vantagem o poder decide.
		//
		// O ponto de virada CAI das constantes do DM (empurrao por aperto, deriva, teto, e os 22 s de
		// prazo); ele nao esta escrito em lugar nenhum, e por isso e uma medicao e nao uma repeticao.
		// ==================================================================================
		// A COLUNA DO MEIO E A QUE MANDA. O `bp` do rival e multiplicado pelo numero da esquerda, mas
		// o poder de um feixe (`BP * mods * basedamage`) sobe MAIS que o BP -- os atributos de ki
		// crescem junto --, entao o que decide a disputa e a vantagem MEDIDA, e nao o desnivel de BP.
		// Imprimir so a esquerda seria imprimir um numero que nao e o que o jogo usa.
		GD.Print("[embateki]       bp do rival | vantagem dele | desfecho de quem acerta TODAS as letras:");
		foreach (double v in new[] { 1.0, 1.5, 1.75, 2.0, 3.0, 6.0 })
		{
			DesfechoDeTeste fim = DisputarComLetras(v, true, false, out _, out double vant);
			GD.Print($"[embateki]        {v,10:0.##}x | {vant,13:0.##}x | {fim}");
		}

		AfirmarEk("em forcas parelhas o teclado decide", DisputarComLetras(1.0) == DesfechoDeTeste.Venci);
		AfirmarEk("...e no TETO da vantagem (6x) o poder decide, por mais rapido que voce seja",
				  DisputarComLetras(6.0) != DesfechoDeTeste.Venci);

		SerRapidoPaga();
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
	private (ServerPlayer, ServerPlayer, DisputaDeKi?) DoisRaiosDeFrente(
		int tiles, double bpDoSegundo = 50_000, bool viradoParaOOutro = true, bool tecladoNoSegundo = true)
	{
		Vec2 chao = CorredorLivre(tiles + 6);
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
