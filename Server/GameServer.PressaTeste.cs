using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA PRESSA (`--pressateste`) -- o SEGUNDO pedido do dono, medido nos DOIS embates.
///
///     Godot --headless --path . --server --port 7918 --pressateste
///
/// ============================ O PEDIDO, LITERAL ============================
/// *"faca com q caso vc ACERTE o quick time event, ele JA VAI PRO PROXIMO BOTAO pra apertar, entao
/// realmente QUANTO MAIS RAPIDO VC FOR MELHOR VAI SER"*.
///
/// Sao DUAS frases e nao uma, e e por isso que sao duas familias por embate:
///   * **"ja vai pro proximo botao"** e uma medida de TEMPO -- o vao entre duas letras;
///   * **"quanto mais rapido melhor"** e uma medida de DESFECHO -- quem ganha.
/// A primeira pode estar certa com a segunda errada (foi o que quase aconteceu: adiantar a letra sem
/// recalibrar o que ela VALE dava mais letras a todo mundo e mudava a escada de poder, nao o
/// vencedor). Medir so a primeira seria dizer "o botao troca mais rapido" e deixar sem resposta a
/// unica pergunta que o dono fez.
/// ==========================================================================
///
/// ============================ O DEFEITO INJETADO TEM NOME: O METRONOMO ============================
/// Toda familia daqui roda DUAS vezes: como o jogo esta, e com **piso = janela**
/// (`MsMinimoEntreLetras = MsPorTecla`). Essa igualdade nao e um numero escolhido pra estragar: ela
/// e o jogo de ANTES, exato -- com os dois iguais o `-=` do `TeclaDoEmbate` subtrai zero, a letra
/// seguinte so nasce quando a janela inteira fecha, e o valor da letra volta a ser o calibrado pela
/// janela (`ApertosPorLetra`). Ou seja: o defeito injetado e **desfazer o pedido do dono**, e o que
/// se exige e que cada afirmacao fique VERMELHA quando ele esta no ar.
///
/// Sem isso, quatro checagens verdes nao provariam nada: elas ficariam verdes tambem com o
/// adiantamento desligado, porque letra continua nascendo dos dois jeitos.
/// ==============================================================================================
///
/// ============================ POR QUE ELA GASTA RELOGIO DE PAREDE ============================
/// So a metade de KI corre por `dt`. O embate de VELOCIDADE conta tudo em <see cref="NowMs"/> -- o
/// prazo da letra, o piso da cadencia, os cruzamentos --, entao um laco sincrono mediria o laco:
/// nove mil tiques passariam num piscar e o embate inteiro renderia UMA letra, com o jogo certo ou
/// quebrado, indistintamente. E a mesma licao que a `--tresteste` ja pagou: quando o sistema medido
/// conta numa moeda, a bancada paga naquela moeda.
/// =========================================================================================
/// </summary>
public partial class GameServer
{
	private int _prOk, _prFalhou;

	/// <summary>
	/// O PLACAR DA INJECAO, separado do outro DE PROPOSITO -- a mesma disciplina da `--diagtecla`.
	/// Verde na rodada real e "nao viu defeito"; vermelho com o defeito na frente e "sabe olhar".
	/// Somados no mesmo numero, a diferenca entre as duas coisas some.
	/// </summary>
	private int _prPegou, _prPassouBatido;

	private void AfirmarPr(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _prOk++; GD.Print($"[pressa]   OK    {oque}"); return; }
		_prFalhou++;
		GD.PrintErr($"[pressa]   FALHA {oque}   {detalhe}");
	}

	/// <summary>
	/// Uma regra posta na frente de um defeito injetado: ela TEM que ficar vermelha.
	///
	/// O NOME DO DEFEITO SAI NA LINHA porque sao DOIS, e opostos: o `metronomo` (piso = janela, o jogo
	/// de antes do pedido do dono) e o `piso zero` (adiantamento sem freio, a versao que este projeto ja
	/// reverteu). Uma linha que so dissesse "pegou" nao diria de qual dos dois ela sabe se defender.
	/// </summary>
	private void Injetar(string oque, bool ficouVermelha, string detalhe = "", string defeito = "metronomo")
	{
		if (ficouVermelha) { _prPegou++; GD.Print($"[pressa]   pegou   ({defeito}) {oque}   {detalhe}"); return; }
		_prPassouBatido++;
		GD.PrintErr($"[pressa]   PASSOU BATIDO ({defeito}) {oque}   {detalhe}");
	}

	/// <summary>
	/// RODA UM CRITERIO COM O METRONOMO NO AR. O piso vira a janela -- o jogo de antes --, e o
	/// `finally` devolve, porque o servidor continua de pe depois da bancada e uma bancada nao muda
	/// a regra de quem esta jogando.
	/// </summary>
	private T ComOMetronomo<T>(Func<T> criterio)
	{
		long pisoGuardado = MsMinimoEntreLetras;
		MsMinimoEntreLetras = MsPorTecla;
		try { return criterio(); }
		finally { MsMinimoEntreLetras = pisoGuardado; }
	}

	public void RodarBancadaDaPressa()
	{
		_prOk = _prFalhou = _prPegou = _prPassouBatido = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[pressa] ================ SER RAPIDO PAGA? (os dois embates) ================");
		AfirmarPr("a zona da bancada tem colisao carregada", _pjMapa != null);
		AfirmarPr($"o piso da cadencia e MENOR que a janela (sem isso nao ha o que adiantar): "
				  + $"{MsMinimoEntreLetras} ms contra {MsPorTecla} ms", MsMinimoEntreLetras < MsPorTecla);

		bool sorteioGuardado = _clashSempre;
		_clashSempre = true;
		try
		{
			ZanzoAcertarAdianta();
			ZanzoRapidoVenceDevagar();
			KiAcertarAdianta();
			KiRapidoVenceDevagar();
			OErroNaoAdianta();
			OPisoSeguraOMartelo();
		}
		finally { _clashSempre = sorteioGuardado; LimparAPressa(); }

		GD.Print($"[pressa] ================ {_prOk} passaram, {_prFalhou} falharam "
				 + $"| injecao: {_prPegou} pegou, {_prPassouBatido} passou batido ================");

		// ============================ ELA FECHA A PORTA ATRAS DE SI -- E SO NO DEDICADO ============================
		// Um servidor `--server` fica de pe pra sempre depois da bancada, e quem a roda por script
		// ficaria esperando um processo que nunca acaba (o `.bat` desta bancada travava aqui). Num
		// `--host` NAO: la ha um jogador na frente da tela, e uma bancada que fecha o jogo dele seria
		// a bancada mandando no jogo. E o mesmo criterio dos robos do cliente, que so fecham o que
		// eles mesmos subiram.
		// ====================================================================================================
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--server") >= 0 && GetTree() is { } arvore)
			arvore.CreateTimer(0.5).Timeout += () => arvore.Quit();
	}

	// =====================================================================
	// 1) VELOCIDADE -- ACERTAR ADIANTA A LETRA
	// =====================================================================
	/// <summary>
	/// A PRIMEIRA METADE DO PEDIDO, no embate de velocidade: **o vao entre duas letras**.
	///
	/// Duas reacoes, e elas sao as duas pontas do que uma mao humana faz: 40 ms (mais rapido que
	/// gente, o "cliente automatico") e 840 ms (o limite -- responder com a janela quase fechada). A
	/// promessa e `cadencia = max(piso, a sua reacao)`, entao a rapida tem que bater no piso e a lenta
	/// tem que ficar na propria reacao.
	///
	/// O NASCIMENTO DA LETRA NAO E AMOSTRADO, e isso importa: `NovaTecla` escreve `Prazo = agora +
	/// janela`, entao `Prazo - janela` **e** o carimbo do servidor pro instante em que a letra nasceu.
	/// Medir pelo meu tique poria o jitter do laco (33 ms) dentro de um numero que vale 300.
	/// </summary>
	private void ZanzoAcertarAdianta()
	{
		GD.Print("[pressa] -- 1) VELOCIDADE: acertar ADIANTA a proxima letra");

		Pressa rapido = UmZanzoDeTeste(0.04, 0.04);
		Pressa lento = UmZanzoDeTeste(0.84, 0.84);

		GD.Print($"[pressa]        MEDIDO: reacao 40 ms -> {rapido.LetrasMinhas} letras, cadencia "
				 + $"{rapido.CadenciaMinha:0} ms | reacao 840 ms -> {lento.LetrasMinhas} letras, cadencia "
				 + $"{lento.CadenciaMinha:0} ms   (piso {MsMinimoEntreLetras}, janela {MsPorTecla})");

		AfirmarPr("o embate de velocidade rodou dos dois jeitos (senao nao ha o que comparar)",
				  rapido.Comecou && lento.Comecou && rapido.LetrasMinhas >= 3 && lento.LetrasMinhas >= 2,
				  $"{rapido.LetrasMinhas} e {lento.LetrasMinhas} letras");

		// UM TIQUE DE FOLGA: o prazo vence DENTRO de um tique de 33 ms, e a letra nasce no tique
		// seguinte. A cadencia real e sempre "piso + ate um tique", e exigir 300 cravado mediria a
		// taxa do laco.
		const double Folga = 1000.0 / 30 + 12;
		AfirmarPr($"quem acerta em 40 ms recebe a proxima no PISO ({MsMinimoEntreLetras} ms), e nao na "
				  + $"janela ({MsPorTecla} ms)",
				  rapido.CadenciaMinha < MsMinimoEntreLetras + Folga
				  && rapido.CadenciaMinha >= MsMinimoEntreLetras - 1,
				  $"{rapido.CadenciaMinha:0} ms");
		AfirmarPr("...e quem acerta no LIMITE da janela recebe a proxima na PROPRIA reacao "
				  + "(a cadencia e `max(piso, reacao)`, e nao um compasso)",
				  lento.CadenciaMinha > 840 - 1 && lento.CadenciaMinha < 840 + Folga,
				  $"{lento.CadenciaMinha:0} ms");
		AfirmarPr("...e por isso o rapido recebeu MAIS QUE O DOBRO de letras no mesmo embate",
				  rapido.LetrasPorSegundo > lento.LetrasPorSegundo * 2,
				  $"{rapido.LetrasPorSegundo:0.##}/s contra {lento.LetrasPorSegundo:0.##}/s");

		// ============================ O METRONOMO ============================
		// AS DUAS PONTAS SAO REMEDIDAS DENTRO DO DEFEITO, e nao comparadas com as de fora: um
		// criterio que mistura os dois regimes ficaria vermelho pela mistura, e nao pelo defeito.
		// =====================================================================
		(Pressa mRapido, Pressa mLento) = ComOMetronomo(
			() => (UmZanzoDeTeste(0.04, 0.04), UmZanzoDeTeste(0.84, 0.84)));

		Injetar("a cadencia rapida deixa de bater no piso (o vao volta a ser a janela inteira)",
				!(mRapido.CadenciaMinha < MsMinimoEntreLetras + Folga),
				$"cadencia {mRapido.CadenciaMinha:0} ms com o piso desligado");
		Injetar("...e a vantagem de vazao do rapido some (ele recebe as letras do lento)",
				!(mRapido.LetrasPorSegundo > mLento.LetrasPorSegundo * 2),
				$"{mRapido.LetrasPorSegundo:0.##}/s contra {mLento.LetrasPorSegundo:0.##}/s");
	}

	// =====================================================================
	// 2) VELOCIDADE -- RAPIDO VENCE DEVAGAR
	// =====================================================================
	/// <summary>
	/// A SEGUNDA METADE, E A QUE PROVA O PEDIDO: **dois embates iguais, so as velocidades trocadas,
	/// desfechos diferentes**.
	///
	/// Iguais em tudo o que decide um embate: mesmo BP (vantagem 1 pros dois), mesmo alfabeto, mesmo
	/// prazo, os dois com teclado e os dois acertando SEMPRE a letra certa. A unica diferenca entre
	/// os dois lados e **quando** a tecla sai -- e entre a primeira rodada e a segunda, a unica
	/// diferenca e de que lado esta a pressa.
	///
	/// QUEM VENCEU E LIDO DO ESTADO, e nao recalculado: `Terminar` desfere o golpe de saida e o
	/// `MarcarAgressao` carimba o perdedor com o id de quem o acertou. Reescrever aqui a regra do
	/// desempate (`PtsA > PtsB || ...`) seria a segunda copia dela -- e ela concordaria com a
	/// primeira ate o dia em que uma das duas mudasse.
	/// </summary>
	private void ZanzoRapidoVenceDevagar()
	{
		GD.Print("[pressa] -- 2) VELOCIDADE: RAPIDO VENCE DEVAGAR (dois embates iguais, desfechos diferentes)");

		Pressa euRapido = UmZanzoDeTeste(0.04, 0.84);
		Pressa euLento = UmZanzoDeTeste(0.84, 0.04);

		GD.Print($"[pressa]        MEDIDO: eu rapido -> {euRapido.PtsMeus:0.#} x {euRapido.PtsDele:0.#} "
				 + $"({(euRapido.Venci ? "VENCI" : "perdi")}) | eu lento -> {euLento.PtsMeus:0.#} x "
				 + $"{euLento.PtsDele:0.#} ({(euLento.Venci ? "venci" : "PERDI")})");

		AfirmarPr("os dois embates aconteceram, e em forcas PARELHAS (vantagem 1 dos dois lados: o "
				  + "que decide e a mao, nao o BP)",
				  euRapido.Comecou && euLento.Comecou && Math.Abs(euRapido.MeuMult - 1) < 1e-9
				  && Math.Abs(euRapido.MultDele - 1) < 1e-9,
				  $"x{euRapido.MeuMult:0.##} contra x{euRapido.MultDele:0.##}");

		bool desfechosDiferentes = euRapido.Venci != euLento.Venci;
		double folgaRapido = euRapido.PtsMeus / Math.Max(euRapido.PtsDele, 0.001);

		AfirmarPr("O PEDIDO DO DONO: trocando SO a velocidade das maos, o vencedor troca de lado",
				  desfechosDiferentes && euRapido.Venci,
				  $"rapido venceu={euRapido.Venci}, lento venceu={euLento.Venci}");
		AfirmarPr("...e nao foi por um ponto: a mao rapida pontuou quase o triplo da lenta",
				  folgaRapido > 1.8, $"{folgaRapido:0.##}x");

		// ---- O METRONOMO: com a janela mandando, as duas maos entregam a mesma coisa ----
		(bool venciRapido, bool venciLento, double folga) = ComOMetronomo(() =>
		{
			Pressa a = UmZanzoDeTeste(0.04, 0.84);
			Pressa b = UmZanzoDeTeste(0.84, 0.04);
			return (a.Venci, b.Venci, a.PtsMeus / Math.Max(a.PtsDele, 0.001));
		});

		Injetar("o desfecho para de seguir a mao (ser rapido deixa de pagar em vitoria)",
				!(venciRapido != venciLento && venciRapido && folga > 1.8),
				$"rapido venceu={venciRapido}, lento venceu={venciLento}, folga {folga:0.##}x");
	}

	// =====================================================================
	// 3) KI -- ACERTAR ADIANTA A LETRA
	// =====================================================================
	/// <summary>
	/// A MESMA PRIMEIRA METADE, na colisao de ki -- e ela e OUTRA MEDIDA, nao a mesma repetida: la o
	/// prazo mora em `NowMs()` e aqui mora num CONTADOR de segundos (`LadoDeKi.Restante`), abatido
	/// por `dt`. Sao duas escritas do adiantamento (`PrazoA -= ...` e `Restante -= ...`), e uma pode
	/// ser desfeita sem a outra.
	///
	/// Por isso o vao e contado em TIQUES: aqui o relogio do embate e o `dt` que a bancada entrega, e
	/// o relogio de parede nao diz nada sobre este laco.
	/// </summary>
	private void KiAcertarAdianta()
	{
		GD.Print("[pressa] -- 3) KI: acertar ADIANTA a proxima letra (o outro relogio)");

		(int letrasR, double cadR, DesfechoDeTeste _) = UmaDisputaDeKiDeTeste(0.0);
		(int letrasL, double cadL, DesfechoDeTeste _2) = UmaDisputaDeKiDeTeste(0.84);

		GD.Print($"[pressa]        MEDIDO: reacao 0 ms -> {letrasR} letras, cadencia {cadR * 1000:0} ms | "
				 + $"reacao 840 ms -> {letrasL} letras, cadencia {cadL * 1000:0} ms");

		// UM TIQUE DE FOLGA, pelo mesmo motivo da familia 1: o contador vence DENTRO de um tique.
		double folga = Protocol.TickSeconds + 0.012;
		AfirmarPr($"na colisao de ki o acerto tambem adianta: cadencia no piso ({MsMinimoEntreLetras} ms)",
				  letrasR >= 3 && cadR < MsMinimoEntreLetras / 1000.0 + folga
				  && cadR >= MsMinimoEntreLetras / 1000.0 - Protocol.TickSeconds,
				  $"{cadR * 1000:0} ms em {letrasR} letras");
		AfirmarPr("...e quem responde no limite da janela fica na propria reacao",
				  letrasL >= 2 && cadL > 0.84 - Protocol.TickSeconds && cadL < 0.84 + folga,
				  $"{cadL * 1000:0} ms em {letrasL} letras");
		AfirmarPr("...e o rapido recebeu MAIS QUE O DOBRO de letras (a vazao, que e o que empurra o medidor)",
				  letrasR > letrasL * 2, $"{letrasR} contra {letrasL}");

		// O METRONOMO, com as duas pontas remedidas DENTRO dele -- ver a nota da familia 1.
		((int letras, double cad, DesfechoDeTeste _4) mR, (int letras, double cad, DesfechoDeTeste _5) mL) =
			ComOMetronomo(() => (UmaDisputaDeKiDeTeste(0.0), UmaDisputaDeKiDeTeste(0.84)));

		Injetar("a cadencia da colisao de ki volta pra janela",
				!(mR.cad < MsMinimoEntreLetras / 1000.0 + folga),
				$"{mR.cad * 1000:0} ms com o piso desligado");
		Injetar("...e o rapido perde a vazao (recebe as letras do lento)",
				!(mR.letras > mL.letras * 2), $"{mR.letras} contra {mL.letras}");
	}

	// =====================================================================
	// 4) KI -- RAPIDO VENCE DEVAGAR
	// =====================================================================
	/// <summary>
	/// A SEGUNDA METADE na colisao de ki, e aqui ela e mais dura do que no embate de velocidade: **o
	/// placar nao e "acertos sobre um numero fixo de letras"**, e um cabo de guerra continuo. Mais
	/// letras nao sao so mais pontos -- sao mais empurrao, e o encontro pode acabar antes dos 22 s.
	///
	/// O RIVAL E O NPC MEDIANO DE MESMO PODER porque ele e a REGUA da calibragem (`ApertosPorLetra`):
	/// em cima dela, empata; abaixo, perde. Contra um alvo parado qualquer velocidade venceria e a
	/// medida nao diria nada.
	/// </summary>
	private void KiRapidoVenceDevagar()
	{
		GD.Print("[pressa] -- 4) KI: RAPIDO VENCE DEVAGAR (duas disputas iguais, desfechos diferentes)");

		(int _, double _2, DesfechoDeTeste fimRapido) = UmaDisputaDeKiDeTeste(0.0);
		(int _3, double _4, DesfechoDeTeste fimLento) = UmaDisputaDeKiDeTeste(0.6);

		GD.Print($"[pressa]        MEDIDO: reacao 0 ms -> {fimRapido} | reacao 600 ms -> {fimLento}");

		AfirmarPr("O PEDIDO DO DONO na colisao de ki: a MESMA disputa, contra o MESMO rival, termina "
				  + "diferente so pela velocidade da mao",
				  fimRapido != fimLento && fimLento == DesfechoDeTeste.Perdi
				  && fimRapido != DesfechoDeTeste.NaoComecou,
				  $"rapido={fimRapido}, lento={fimLento}");

		(DesfechoDeTeste mRapido, DesfechoDeTeste mLento) = ComOMetronomo(() =>
			(UmaDisputaDeKiDeTeste(0.0).Fim, UmaDisputaDeKiDeTeste(0.6).Fim));

		Injetar("as duas disputas voltam a terminar igual (a mao deixa de decidir)",
				!(mRapido != mLento && mLento == DesfechoDeTeste.Perdi),
				$"rapido={mRapido}, lento={mLento}");
	}

	// =====================================================================
	// 5) O ERRO NAO ADIANTA
	// =====================================================================
	/// <summary>
	/// O CONTRA-EXEMPLO QUE IMPEDE O ADIANTAMENTO DE VIRAR PREMIO PRA QUEM MARTELA: quem ERRA espera
	/// a janela CHEIA. Sem esta linha, "responder rapido" viraria "bater em qualquer tecla rapido", e
	/// o quick time event deixaria de medir leitura pra medir dedo.
	///
	/// Nao ha metronomo a injetar aqui: o defeito seria o adiantamento no ramo do erro, e ele se
	/// mede pelo lado direito -- o vao do erro tem que ser o do metronomo, que e onde o acerto NAO
	/// esta.
	/// </summary>
	private void OErroNaoAdianta()
	{
		GD.Print("[pressa] -- 5) O ERRO NAO ADIANTA (senao adiantar premiaria quem martela)");

		Pressa errando = UmZanzoDeTeste(0.04, 0.04, erroDeProposito: true);

		GD.Print($"[pressa]        MEDIDO: errando sempre em 40 ms -> {errando.LetrasMinhas} letras, "
				 + $"cadencia {errando.CadenciaMinha:0} ms (o acerto rende {MsMinimoEntreLetras} ms)");

		AfirmarPr("quem erra em 40 ms espera a JANELA INTEIRA pela proxima -- o adiantamento e do "
				  + "ACERTO, e so dele",
				  errando.Comecou && errando.CadenciaMinha > MsPorTecla - 1,
				  $"{errando.CadenciaMinha:0} ms contra os {MsPorTecla} ms da janela");
		AfirmarPr("...e martelar sai NEGATIVO no placar (o ponto por erro continua cobrado)",
				  errando.PtsMeus < 0, $"{errando.PtsMeus:0.#} ponto(s)");
	}

	// =====================================================================
	// 6) O PISO SEGURA O MARTELO
	// =====================================================================
	/// <summary>
	/// ============================ A LINHA QUE IMPEDE O ABUSO QUE JA FEZ REVERTEREM ============================
	/// O adiantamento ja existiu neste projeto e foi **desfeito de proposito**: a versao sem piso media
	/// *55 letras em 5,4 s e 424 pontos*, e o embate deixou de medir leitura pra medir dedo. O piso e a
	/// resposta a isso, e as familias 1 e 3 o mostram por cima -- elas medem a cadencia de quem responde
	/// em 40 ms, que ja bate nele.
	///
	/// **Esta familia e a que fecha a porta**, e ela e outra pergunta: nao "o rapido ganha mais letras"
	/// (ganha) e sim *"existe algum jeito de ganhar MAIS que o piso?"*. Por isso o cliente aqui responde
	/// em **1 ms** -- mais rapido que qualquer nervo humano, e mais rapido que o tique do laco. Se a vazao
	/// dele for igual a de quem responde em 40 ms, o teto e um teto de verdade; se ela subir, existe um
	/// regime em que martelar paga, e e so questao de alguem escrever um macro.
	///
	/// ============================ E O DEFEITO INJETADO AQUI E OUTRO ============================
	/// O metronomo das outras familias (piso = janela) **desfaz o pedido do dono**, e por isso ele deixa
	/// a cadencia MAIOR -- ele nunca poderia reprovar um teto. O defeito desta familia e o oposto exato:
	/// **piso = 0**, ou seja o adiantamento sem freio, que e a versao revertida. Com ele a letra seguinte
	/// nasce no tique seguinte ao acerto, a cadencia despenca pra a taxa do laco e a vazao multiplica.
	///
	/// Sao os dois lados da mesma regra, e cada um pega o que o outro nao pode pegar: sem o metronomo,
	/// "a cadencia e 300" ficaria verde num jogo que nunca adianta; sem o piso-zero, ficaria verde num
	/// jogo que adianta sem limite. Nenhum dos dois sozinho prova `cadencia = max(piso, reacao)`.
	/// ======================================================================================================
	/// </summary>
	private void OPisoSeguraOMartelo()
	{
		GD.Print("[pressa] -- 6) O PISO SEGURA O MARTELO (um cliente que responde em 1 ms)");

		Pressa martelo = UmZanzoDeTeste(0.001, 0.84);
		Pressa mao = UmZanzoDeTeste(0.04, 0.84);
		(int letrasMartelo, double cadMartelo, DesfechoDeTeste _) = UmaDisputaDeKiDeTeste(0.0);

		double tetoPorSegundo = 1000.0 / MsMinimoEntreLetras;
		GD.Print($"[pressa]        MEDIDO: 1 ms -> {martelo.LetrasMinhas} letras, cadencia "
				 + $"{martelo.CadenciaMinha:0} ms ({martelo.LetrasPorSegundo:0.##}/s) | 40 ms -> "
				 + $"{mao.LetrasPorSegundo:0.##}/s | teto do piso {tetoPorSegundo:0.##}/s");

		// UM TIQUE DE FOLGA, como nas familias 1 e 3: o contador vence DENTRO de um tique e a letra
		// nasce no seguinte, entao a cadencia real e "piso + ate um tique".
		const double Folga = 1000.0 / 30 + 12;

		AfirmarPr("quem responde em 1 ms NAO recebe letra mais rapido que o piso (a vazao para nele)",
				  martelo.Comecou && martelo.CadenciaMinha >= MsMinimoEntreLetras - 1
				  && martelo.CadenciaMinha < MsMinimoEntreLetras + Folga,
				  $"{martelo.CadenciaMinha:0} ms contra o piso de {MsMinimoEntreLetras} ms");
		AfirmarPr("...e por isso o martelo de 1 ms nao entrega mais que a mao de 40 ms (nao ha o que "
				  + "ganhar apertando mais rapido que o piso)",
				  martelo.LetrasPorSegundo < mao.LetrasPorSegundo * 1.25 + 0.2
				  && martelo.LetrasPorSegundo <= tetoPorSegundo * 1.2,
				  $"{martelo.LetrasPorSegundo:0.##}/s contra {mao.LetrasPorSegundo:0.##}/s "
				  + $"(teto {tetoPorSegundo:0.##}/s)");
		AfirmarPr("...e na colisao de ki vale o mesmo teto, medido no outro relogio",
				  cadMartelo >= MsMinimoEntreLetras / 1000.0 - Protocol.TickSeconds
				  && letrasMartelo <= EmbateDeKi.SegundosMaximos * tetoPorSegundo + 2,
				  $"{letrasMartelo} letras, cadencia {cadMartelo * 1000:0} ms");

		// ============================ O DEFEITO: O ADIANTAMENTO SEM FREIO (piso = 0) ============================
		(Pressa semPiso, (int letras, double cad, DesfechoDeTeste _) kiSemPiso) =
			SemOPiso(() => (UmZanzoDeTeste(0.001, 0.84), UmaDisputaDeKiDeTeste(0.0)));

		Injetar("sem o piso, o martelo de 1 ms recebe letra na taxa do LACO (a cadencia despenca)",
				!(semPiso.CadenciaMinha >= MsMinimoEntreLetras - 1),
				$"cadencia {semPiso.CadenciaMinha:0} ms com o piso em zero", "piso zero");
		Injetar("...e a vazao dele multiplica (e a versao que mediu 55 letras em 5,4 s)",
				!(semPiso.LetrasPorSegundo <= tetoPorSegundo * 1.2),
				$"{semPiso.LetrasPorSegundo:0.##}/s contra o teto de {tetoPorSegundo:0.##}/s", "piso zero");
		Injetar("...e na colisao de ki a mesma porta se abre",
				!(kiSemPiso.letras <= EmbateDeKi.SegundosMaximos * tetoPorSegundo + 2),
				$"{kiSemPiso.letras} letras, cadencia {kiSemPiso.cad * 1000:0} ms", "piso zero");
	}

	/// <summary>
	/// RODA UM CRITERIO COM O PISO EM ZERO -- o adiantamento sem freio, que e a versao que este projeto
	/// ja reverteu uma vez. Irmao do <see cref="ComOMetronomo"/> e o oposto exato dele; o `finally`
	/// devolve pelo mesmo motivo.
	/// </summary>
	private T SemOPiso<T>(Func<T> criterio)
	{
		long pisoGuardado = MsMinimoEntreLetras;
		MsMinimoEntreLetras = 0;
		try { return criterio(); }
		finally { MsMinimoEntreLetras = pisoGuardado; }
	}

	// =====================================================================
	// AS PECAS DA BANCADA
	// =====================================================================
	/// <summary>O que uma rodada do embate de velocidade devolveu.</summary>
	private readonly record struct Pressa(
		bool Comecou, int LetrasMinhas, int LetrasDele, double CadenciaMinha,
		double PtsMeus, double PtsDele, bool Venci, double Segundos, double MeuMult, double MultDele)
	{
		/// <summary>A VAZAO, que e o que o pedido do dono paga -- letras por segundo de embate.</summary>
		public double LetrasPorSegundo => Segundos > 0.01 ? LetrasMinhas / Segundos : 0;
	}

	/// <summary>UM LADO da mesa, do jeito que a bancada o dirige: uma reacao e um relogio.</summary>
	private sealed class MaoDeTeste
	{
		public required ServerPlayer Quem;
		public required bool EhA;
		public double Reacao;
		public bool Erra;

		/// <summary>O maior prazo ja visto. Prazo que SOBE = letra nova (`NovaTecla` escreve `agora + janela`).</summary>
		public long PrazoVisto;

		public long UltimoNascimento;
		public bool Respondi;
		public readonly List<long> Vaos = [];
		public int Letras;
	}

	/// <summary>
	/// UM EMBATE DE VELOCIDADE INTEIRO, com as duas maos dirigidas pela bancada.
	///
	/// ============================ O QUE E PRIVILEGIO, E O QUE NAO E ============================
	/// Privilegio sao dois, os mesmos das outras bancadas de embate: a CADENCIA (o laco chama
	/// `TickDosEmbates` na mao, porque no boot ninguem esta tiquando) e o TECLADO EMPRESTADO
	/// (`_comTecladoDeTeste`) -- sem ele os dois corpos forjados cairiam na taxa automatica da IA e a
	/// bancada mediria duas maquinas, que e exatamente o que ela nao esta medindo.
	///
	/// TUDO O MAIS E PRODUCAO: o gatilho e a troca simultanea de socos (`UmaTrocaSimultanea` ->
	/// `Atacar` -> `TentarEmbate`), a letra vem do `NovaTecla`, o julgamento e do
	/// `TeclaDeQualquerEmbate` -- a MESMA funcao que o `case Protocol.C2S.ClashTecla` chama -- e o fim
	/// e o `Terminar`, com golpe de saida e tudo.
	/// =====================================================================================
	/// </summary>
	private Pressa UmZanzoDeTeste(double reacaoMinha, double reacaoDele, bool erroDeProposito = false)
	{
		LimparAPressa();

		Vec2 onde = CorredorLivre(10);
		ServerPlayer eu = Forjar("PressaEu", onde, 5_000);
		ServerPlayer ele = Forjar("PressaEle", onde + new Vec2(28, 0), 5_000);

		// A SKILL E A CONDICAO DO EMBATE (`haszanzo && M.haszanzo`), e ela nao passa pelo
		// `_clashSempre`: sem o livro dos dois nao ha encontro nenhum a medir.
		eu.Livro.Dar(PathDoZanzoken);
		ele.Livro.Dar(PathDoZanzoken);
		_comTecladoDeTeste.Add(eu.Id);
		_comTecladoDeTeste.Add(ele.Id);

		if (!UmaTrocaSimultanea(eu, ele, onde) || !_emEmbate.TryGetValue(eu.Id, out Embate? e))
			return default;

		// O CARIMBO DO GOLPE DE SAIDA E COMO SE LE O VENCEDOR (ver o cabecalho da familia 2). Os
		// socos que ABRIRAM o embate acabaram de escrever aqui -- zerar agora e a diferenca entre ler
		// o desfecho e ler o gatilho.
		eu.UltimoAgressor = 0;
		ele.UltimoAgressor = 0;

		bool souA = e.A == eu;
		var minha = new MaoDeTeste { Quem = eu, EhA = souA, Reacao = reacaoMinha, Erra = erroDeProposito };
		var dele = new MaoDeTeste { Quem = ele, EhA = !souA, Reacao = reacaoDele };

		long t0 = NowMs(), proximo = t0;
		while (_emEmbate.ContainsKey(eu.Id) && NowMs() - t0 < 15_000)
		{
			if (NowMs() < proximo) { System.Threading.Thread.Sleep(1); continue; }
			proximo = NowMs() + (long)(Protocol.TickSeconds * 1000);

			long agora = NowMs();
			// OLHAR ANTES DE RESPONDER: responder MEXE no prazo (e o adiantamento), e o nascimento
			// so se le enquanto o prazo ainda e o que o `NovaTecla` escreveu.
			Olhar(e, minha);
			Olhar(e, dele);
			Responder(e, minha, agora);
			Responder(e, dele, agora);

			TickDosEmbates();
		}

		double segundos = (NowMs() - t0) / 1000.0;
		double cadencia = minha.Vaos.Count > 0 ? minha.Vaos.Average() : 0;
		var saida = new Pressa(true, minha.Letras, dele.Letras, cadencia,
							   souA ? e.PtsA : e.PtsB, souA ? e.PtsB : e.PtsA,
							   ele.UltimoAgressor == eu.Id, segundos,
							   souA ? e.MultA : e.MultB, souA ? e.MultB : e.MultA);
		LimparAPressa();
		return saida;
	}

	/// <summary>Nasceu letra nova deste lado? Ver <see cref="MaoDeTeste.PrazoVisto"/>.</summary>
	private static void Olhar(Embate e, MaoDeTeste m)
	{
		char letra = m.EhA ? e.LetraA : e.LetraB;
		long prazo = m.EhA ? e.PrazoA : e.PrazoB;
		if (letra == '\0' || prazo <= m.PrazoVisto) return;

		long nasceu = prazo - MsPorTecla;
		if (m.Letras > 0) m.Vaos.Add(nasceu - m.UltimoNascimento);
		m.PrazoVisto = prazo;
		m.UltimoNascimento = nasceu;
		m.Respondi = false;
		m.Letras++;
	}

	/// <summary>
	/// A MAO APERTA, quando o tempo de reacao dela passou -- pelo funil do jogador de verdade
	/// (<see cref="TeclaDeQualquerEmbate"/>, o mesmo que o pacote chama).
	/// </summary>
	private void Responder(Embate e, MaoDeTeste m, long agora)
	{
		char letra = m.EhA ? e.LetraA : e.LetraB;
		if (letra == '\0' || m.Respondi || agora - m.UltimoNascimento < (long)(m.Reacao * 1000)) return;

		m.Respondi = true;
		// ERRAR E MANDAR OUTRA LETRA, e nao um caminho de erro proprio -- o mesmo que a maquina faz.
		TeclaDeQualquerEmbate(m.Quem, m.Erra ? (letra == 'Z' ? 'Y' : 'Z') : letra);
	}

	/// <summary>
	/// UMA COLISAO DE KI INTEIRA contra o NPC mediano, medindo TAMBEM o vao entre as letras.
	///
	/// E prima da `DisputarComLetras` da `--embatekiteste` e nao a mesma funcao de proposito: aquela
	/// responde "quem venceu" e esta responde "de quanto em quanto tempo a letra chega". Fundi-las
	/// daria uma funcao com dois donos -- e o laco delas e cinco linhas.
	/// </summary>
	private (int Letras, double Cadencia, DesfechoDeTeste Fim) UmaDisputaDeKiDeTeste(double segundosDeReacao)
	{
		LimparEmbatesDaBancada();

		(ServerPlayer eu, ServerPlayer outro, DisputaDeKi? d) =
			DoisRaiosDeFrente(12, bpDoSegundo: 50_000, tecladoNoSegundo: false);
		if (d == null) return (0, 0, DesfechoDeTeste.NaoComecou);

		LadoDeKi meu = d.A.Quem == eu ? d.A : d.B;
		var fim = DesfechoDeTeste.NaoComecou;
		bool acabou = false;
		double naTela = 0;
		int letras = 0, nasceuNoTique = -1;
		List<int> vaos = [];

		// ============================ O NASCIMENTO E O `Restante` SUBINDO, E NAO A LETRA APARECENDO ============================
		// **Esta linha e um achado da propria bancada.** A primeira versao olhava a letra sair de
		// `'\0'` -- e ficava CEGA justamente no caso que ela existe pra medir: com reacao alta o
		// acerto deixa o contador negativo, e o `NovaLetra` roda no MESMO tique, entao a letra nunca
		// chega a ser vista ausente. A medida saia 1167 ms onde o jogo entrega 850, e o instrumento
		// ainda PERTURBAVA o medido (o relogio da reacao nao zerava e a mao passava a apertar todo
		// tique).
		//
		// `Restante` so SOBE num lugar: dentro do `NovaLetra` (`+ MsPorTecla`). Fora dele ele so e
		// abatido por `dt`. Entao "subiu" e o carimbo exato de "nasceu letra", e ele nao depende de
		// quando a bancada olha -- o mesmo raciocinio do `Prazo` no embate de velocidade.
		// =================================================================================================================
		double restanteVisto = meu.Restante;

		for (int i = 0; i < 30 * 40 && !acabou; i++)
		{
			if (meu.Restante > restanteVisto + 1e-9)
			{
				if (nasceuNoTique >= 0) vaos.Add(i - nasceuNoTique);
				nasceuNoTique = i;
				naTela = 0;
				letras++;
			}

			if (meu.Letra != '\0')
			{
				if (naTela >= segundosDeReacao) TeclaDeQualquerEmbate(eu, meu.Letra);
				else naTela += Protocol.TickSeconds;
			}

			// O RETRATO E TIRADO ANTES DO TIQUE, e este `antes` e a metade que faltava: o `NovaLetra`
			// nasce DENTRO do tique, entao amostrar depois dele ja lia o valor bombado e chamava de
			// "visto" -- a subida acontecia entre duas leituras e nenhuma delas a via. Era 0 letra em
			// 22 segundos de disputa, com a mao apertando o tempo todo.
			restanteVisto = meu.Restante;
			TickDosCanaisDeKi(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
			TickDosEmbatesDeKi(Protocol.TickSeconds);

			if (_emEmbateDeKi.ContainsKey(eu.Id)) continue;
			acabou = true;
			bool meuCanal = _canais.ContainsKey(eu.Id), oDele = _canais.ContainsKey(outro.Id);
			fim = meuCanal && !oDele ? DesfechoDeTeste.Venci
				: oDele && !meuCanal ? DesfechoDeTeste.Perdi
				: DesfechoDeTeste.Empate;
		}

		LimparEmbatesDaBancada();
		return (letras, vaos.Count > 0 ? vaos.Average() * Protocol.TickSeconds : 0,
				acabou ? fim : DesfechoDeTeste.NaoComecou);
	}

	/// <summary>
	/// Desfaz o que esta bancada pos no mundo. O embate sai PRIMEIRO (`SoltarDoEmbate` devolve
	/// visibilidade e atordoamento pelo caminho de producao) e so entao os corpos.
	/// </summary>
	private void LimparAPressa()
	{
		foreach (int id in _emEmbate.Keys.ToList()) SoltarDoEmbate(id);
		_comTecladoDeTeste.Clear();
		_sorteioLivreEm.Clear();
		LimparTudoDaBancada();
	}
}
