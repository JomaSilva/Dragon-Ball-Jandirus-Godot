using Concentus;
using Godot;
using Jandirus.Core.Social;

namespace Jandirus.Client;

/// <summary>
/// ============================ BANCADA DO RADIO DO SCOUTER, NO CLIENTE (`--diagradio`) ============================
/// O servidor prova QUEM recebe (`--radioteste`). Esta prova o que o dono vai OUVIR e o que ele aperta:
/// *"...no ouvido ESQUERDO deles com um EFEITO DE RADIO"*.
///
/// Mede AMOSTRAS, e nao constantes (a licao da `--diagvoz`): um tom conhecido entra no efeito de producao
/// e a bancada conta a energia que sobrou em cada frequencia e em cada ouvido.
///
///  1. O OUVIDO ESQUERDO -- o canal direito e ZERO, amostra por amostra.
///  2. A BANDA -- 1 kHz passa; 100 Hz e 9,3 kHz sao cortados.
///  3. A SATURACAO -- o pico nao passa do teto, e a voz alta e comprimida.
///  4. O CHIADO -- existe e e baixo.
///  5. OS ESTALOS -- um ao abrir, um ao fechar, curtos e que somem.
///  6. A TECLA -- a do radio existe no registro, e remapeavel e nao e a da voz.
///  7. PRA ONDE VAI A VOZ -- a regra das duas teclas (<see cref="RadioDoScouter.ParaOndeVai"/>).
///  8. A FAIXA -- o que a tela escreve pra quem transmite e pra quem recebe.
///  9. O CAMINHO INTEIRO -- pacotes Opus de verdade pelo <see cref="RadioOuvido"/> de producao: fila de
///     jitter, efeito, ouvido esquerdo, estalo de fechar, "esta falando".
///
/// Cada regra tem o DEFEITO posto na frente dela (`...DeTeste`) e a checagem tem que ficar vermelha.
///
/// O QUE ELA NAO PROVA: som de verdade (em `--headless` a placa e o driver Dummy) e o fio -- o fio e da
/// `--radiovivo`. Sem mundo, sem rede, sem personagem:
///     Godot --headless --path . --diagradio
/// ===============================================================================================================
/// </summary>
public partial class RoboDeRadio : Node
{
	private readonly List<string> _falhas = [];
	private int _pegaram;
	private readonly List<string> _passaramBatido = [];

	private const int Taxa = VozLocal.TaxaDeSaida;
	private const int N = VozLocal.AmostrasPorQuadroDeSaida;

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		if (ok) { GD.Print($"[diagradio]   ok    {oque}"); return; }
		_falhas.Add(oque);
		GD.PrintErr($"[diagradio]   FALHA {oque}   {detalhe}");
	}

	/// <summary>Placar separado do das checagens, como na `--diagvoz`: uma injecao que "pegou" e uma checagem que SABE olhar.</summary>
	private void Injetar(string oque, bool ficouVermelha, string detalhe = "")
	{
		if (ficouVermelha) { _pegaram++; GD.Print($"[diagradio]   pegou (injecao) {oque}   {detalhe}"); return; }
		_passaramBatido.Add(oque);
		GD.PrintErr($"[diagradio]   PASSOU BATIDO (injecao) {oque}   {detalhe}");
	}

	public override void _Ready() => _ = Rodar();

	private async System.Threading.Tasks.Task Rodar()
	{
		GD.Print("[diagradio] ============ RADIO DO SCOUTER: o ouvido esquerdo e o efeito, medidos em amostras ============");
		try
		{
			OOuvidoEsquerdo();
			ABanda();
			ASaturacao();
			OChiado();
			OsEstalos();
			ATeclaDoRadio();
			PraOndeVaiAVoz();
			AFaixa();
			await OCaminhoInteiro();
		}
		catch (Exception e)
		{
			Conferir(false, "o roteiro correu ate o fim sem lancar excecao", $"{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
		}
		finally
		{
			EfeitoDeRadio.SemBandaDeTeste = EfeitoDeRadio.NosDoisOuvidosDeTeste = false;
			RadioDoScouter.RadioVazaPorPertoDeTeste = false;
			RadioOuvido.Espiao = null;
		}

		GD.Print($"[diagradio]   ...  injecoes: {_pegaram} pegaram, {_passaramBatido.Count} passaram batido");
		bool ok = _falhas.Count == 0 && _passaramBatido.Count == 0;
		GD.Print(ok
			? "[diagradio] ============ TUDO OK ============"
			: $"[diagradio] ============ {_falhas.Count + _passaramBatido.Count} FALHA(S): {string.Join(" | ", _falhas)}"
			  + (_passaramBatido.Count > 0 ? $" | INJECAO PASSOU BATIDO: {string.Join(" | ", _passaramBatido)}" : "")
			  + " ============");
		GetTree().Quit(ok ? 0 : 1);
	}

	// =====================================================================
	// SINAIS E MEDIDAS
	// =====================================================================
	/// <summary>Soma um seno ao quadro. `fase` em amostras: os quadros seguidos emendam sem degrau.</summary>
	private static void Somar(short[] alvo, float hz, float amp, int fase = 0, int taxa = Taxa)
	{
		for (int i = 0; i < alvo.Length; i++)
		{
			double v = alvo[i] / (double)short.MaxValue + amp * Math.Sin(2 * Math.PI * hz * (i + fase) / taxa);
			alvo[i] = (short)Math.Clamp((int)(v * short.MaxValue), short.MinValue, short.MaxValue);
		}
	}

	/// <summary>Quanta energia ha nesta frequencia (uma raia de Goertzel) -- em amplitude, 0,5 pra um seno de pico 1.</summary>
	private static double Energia(Func<int, double> amostra, int n, float hz)
	{
		double re = 0, im = 0;
		for (int i = 0; i < n; i++)
		{
			double ang = 2 * Math.PI * hz * i / Taxa, v = amostra(i);
			re += v * Math.Cos(ang);
			im += v * Math.Sin(ang);
		}
		return Math.Sqrt(re * re + im * im) / n;
	}

	private static double Energia(short[] pcm, float hz) => Energia(i => pcm[i] / (double)short.MaxValue, pcm.Length, hz);

	private static double Rms(short[] pcm, int de, int ate)
	{
		double s = 0;
		for (int i = de; i < ate; i++) { double v = pcm[i] / (double)short.MaxValue; s += v * v; }
		return Math.Sqrt(s / Math.Max(ate - de, 1));
	}

	private static double Pico(short[] pcm) => pcm.Max(a => Math.Abs(a / (double)short.MaxValue));

	/// <summary>Passa `quadros` de um sinal estacionario pelo efeito e devolve o ULTIMO (o transitorio do filtro ja assentou).</summary>
	private static short[] Assentado(EfeitoDeRadio efeito, Action<short[], int> sinal, int quadros = 10)
	{
		var q = new short[N];
		for (int k = 0; k < quadros; k++)
		{
			Array.Clear(q);
			sinal(q, k * N);
			efeito.Aplicar(q, N);
		}
		return q;
	}

	// =====================================================================
	// 1. O OUVIDO ESQUERDO
	// =====================================================================
	private void OOuvidoEsquerdo()
	{
		var mono = new short[N];
		Somar(mono, 1000f, 0.4f);
		var estereo = new Vector2[N];

		(double Esq, double MaiorDir) Medir()
		{
			EfeitoDeRadio.ParaOOuvidoEsquerdo(mono, N, estereo);
			return (Energia(i => estereo[i].X, N, 1000f), estereo.Max(v => Math.Abs(v.Y)));
		}

		(double esq, double dir) = Medir();
		Conferir(esq > 0.15, "a voz de radio sai no canal ESQUERDO", $"energia={esq:0.000}");
		Conferir(dir == 0, "**e o canal DIREITO e ZERO, amostra por amostra** (nao baixinho: zero)", $"maior amostra={dir:0.00000}");

		EfeitoDeRadio.NosDoisOuvidosDeTeste = true;
		try { Injetar("com a voz nos DOIS ouvidos, 'o direito e zero' reprova", Medir().MaiorDir > 0.1); }
		finally { EfeitoDeRadio.NosDoisOuvidosDeTeste = false; }
	}

	// =====================================================================
	// 2. A BANDA
	// =====================================================================
	/// <summary>
	/// TRES TONS DE MESMA FORCA: 100 Hz (abaixo da banda), 1 kHz (no meio dela) e 9,3 kHz (acima). Os tres
	/// fecham ciclo inteiro em 20 ms, entao a raia de cada um nao vaza na dos outros. Depois do efeito o do
	/// meio tem que sobrar e os das pontas, sumir -- as duas metades, pelo motivo da parede na `--diagvoz`:
	/// so "as pontas sumiram" passaria com um filtro que calasse tudo.
	/// </summary>
	private void ABanda()
	{
		const float grave = 100f, meio = 1000f, agudo = 9300f, amp = 0.1f;
		void TresTons(short[] q, int fase)
		{
			Somar(q, grave, amp, fase);
			Somar(q, meio, amp, fase);
			Somar(q, agudo, amp, fase);
		}

		(double G, double M, double A) Medir()
		{
			short[] q = Assentado(new EfeitoDeRadio(comChiado: false), TresTons);
			return (Energia(q, grave) / (amp / 2), Energia(q, meio) / (amp / 2), Energia(q, agudo) / (amp / 2));
		}

		(double g, double m, double a) = Medir();
		GD.Print($"[diagradio]   ...  pela banda: 100 Hz x{g:0.000}   1 kHz x{m:0.000}   9,3 kHz x{a:0.000}");
		Conferir(m > 0.5, "o MEIO da banda (1 kHz) atravessa -- e onde a fala mora", $"x{m:0.000}");
		Conferir(g < m * 0.06, "**o GRAVE (100 Hz) e cortado**: mais de 24 dB abaixo do meio", $"x{g:0.000} contra x{m:0.000}");
		Conferir(a < m * 0.06, "**o AGUDO (9,3 kHz) e cortado**: mais de 24 dB abaixo do meio", $"x{a:0.000} contra x{m:0.000}");

		// AS QUINAS: 300 Hz e 3,4 kHz ainda passam (com a quina arredondada de duas secoes), e uma oitava pra
		// fora de cada uma ja caiu -- e isso que faz "banda de telefone", e nao so "sem grave e sem agudo".
		double Ganho(float hz)
		{
			short[] q = Assentado(new EfeitoDeRadio(comChiado: false), (b, fase) => Somar(b, hz, amp, fase));
			return Energia(q, hz) / (amp / 2);
		}
		double naQuinaDeBaixo = Ganho(300f), oitavaAbaixo = Ganho(150f), naQuinaDeCima = Ganho(3400f), oitavaAcima = Ganho(6800f);
		Conferir(naQuinaDeBaixo > m * 0.3 && oitavaAbaixo < naQuinaDeBaixo * 0.2,
			$"a quina de baixo e em {EfeitoDeRadio.GraveDaBanda:0} Hz: ali ainda passa, uma oitava abaixo ja caiu",
			$"300 Hz x{naQuinaDeBaixo:0.000}, 150 Hz x{oitavaAbaixo:0.000}");
		Conferir(naQuinaDeCima > m * 0.2 && oitavaAcima < naQuinaDeCima * 0.2,
			$"a quina de cima e em {EfeitoDeRadio.AgudoDaBanda:0} Hz: ali ainda passa, uma oitava acima ja caiu",
			$"3,4 kHz x{naQuinaDeCima:0.000}, 6,8 kHz x{oitavaAcima:0.000}");

		EfeitoDeRadio.SemBandaDeTeste = true;
		try
		{
			(double g2, double m2, double a2) = Medir();
			Injetar("sem a banda (so saturado), 'o grave e o agudo sao cortados' reprova", g2 > m2 * 0.5 && a2 > m2 * 0.5,
				$"100 Hz x{g2:0.000}, 1 kHz x{m2:0.000}, 9,3 kHz x{a2:0.000}");
		}
		finally { EfeitoDeRadio.SemBandaDeTeste = false; }
	}

	// =====================================================================
	// 3. A SATURACAO
	// =====================================================================
	private void ASaturacao()
	{
		short[] Tom(float amp) => Assentado(new EfeitoDeRadio(comChiado: false), (q, fase) => Somar(q, 1000f, amp, fase));

		short[] baixo = Tom(0.09f), alto = Tom(0.9f);
		double pb = Pico(baixo), pa = Pico(alto);
		GD.Print($"[diagradio]   ...  saturacao: entrada 0,09 -> pico {pb:0.000}; entrada 0,90 -> pico {pa:0.000}");
		Conferir(pa <= 0.99 && pa > pb, "a voz ALTA nao estoura: o pico fica abaixo do fundo de escala", $"pico={pa:0.000}");
		Conferir(pa / pb < 5, "**e ela e COMPRIMIDA**: dez vezes mais entrada da menos de cinco vezes mais saida", $"{pa / pb:0.0}x");

		// O TETO MACIO CRIA HARMONICOS (e o "rachado" do radio): um tom puro e forte sai com o terceiro
		// harmonico; o fraco, que nao encosta no teto, sai limpo.
		double h3Alto = Energia(alto, 3000f) / Energia(alto, 1000f), h3Baixo = Energia(baixo, 3000f) / Energia(baixo, 1000f);
		Conferir(h3Alto > 0.05 && h3Baixo < h3Alto * 0.3, "a voz alta RACHA (ganha o terceiro harmonico) e a baixa sai limpa",
			$"3o harmonico: alta {h3Alto:0.000}, baixa {h3Baixo:0.000}");
	}

	// =====================================================================
	// 4. O CHIADO
	// =====================================================================
	private void OChiado()
	{
		short[] comChiado = Assentado(new EfeitoDeRadio(), (_, _) => { });
		short[] semChiado = Assentado(new EfeitoDeRadio(comChiado: false), (_, _) => { });
		double rms = Rms(comChiado, 0, N);
		GD.Print($"[diagradio]   ...  chiado no silencio: rms {rms:0.0000} ({20 * Math.Log10(Math.Max(rms, 1e-9)):0} dB)");
		Conferir(rms is > 0.001 and < 0.03, "no silencio de uma transmissao ha CHIADO, e ele e baixo (entre -60 e -30 dB)", $"rms={rms:0.0000}");
		Conferir(Pico(semChiado) == 0, "com o chiado desligado (so a bancada desliga), silencio entra e silencio sai");

		short[] deNovo = Assentado(new EfeitoDeRadio(), (_, _) => { });
		Conferir(comChiado.AsSpan().SequenceEqual(deNovo), "o efeito e DETERMINISTICO: a mesma entrada da a mesma saida, chiado incluido");
	}

	// =====================================================================
	// 5. OS ESTALOS
	// =====================================================================
	private void OsEstalos()
	{
		// ---- o de abrir: por cima do primeiro quadro, e some ----
		var efeito = new EfeitoDeRadio(comChiado: false);
		efeito.Abrir();
		var q1 = new short[N];
		var q2 = new short[N];
		var q3 = new short[N];
		efeito.Aplicar(q1, N);
		efeito.Aplicar(q2, N);
		efeito.Aplicar(q3, N);
		Conferir(Pico(q1) > 0.03, "ao ABRIR a transmissao ha um estalo de estatica no primeiro quadro", $"pico={Pico(q1):0.000}");
		Conferir(Rms(q3, 0, N) < Rms(q1, 0, N) * 0.05,
			$"...curto ({EfeitoDeRadio.MsDoEstaloDeAbrir} ms): dois quadros depois ja sumiu", $"rms {Rms(q1, 0, N):0.0000} -> {Rms(q3, 0, N):0.0000}");

		var semAbrir = new short[N];
		new EfeitoDeRadio(comChiado: false).Aplicar(semAbrir, N);
		Conferir(Pico(semAbrir) == 0, "sem `Abrir`, nao ha estalo nenhum (ele e da abertura, e nao de todo quadro)");

		// ---- o de fechar: comeca seco e cai ate o silencio ----
		var cauda = new short[EfeitoDeRadio.AmostrasDoEstaloDeFechar];
		efeito.Fechar(cauda, cauda.Length);
		int terco = cauda.Length / 3;
		double comeco = Rms(cauda, 0, terco), fim = Rms(cauda, cauda.Length - terco, cauda.Length);
		Conferir(comeco > 0.01, "ao FECHAR ha o estalo de quem soltou o botao", $"rms do comeco={comeco:0.0000}");
		Conferir(fim < comeco * 0.2, $"...e ele CAI ate o silencio em {EfeitoDeRadio.MsDoEstaloDeFechar} ms", $"rms {comeco:0.0000} -> {fim:0.0000}");
		// ESTATICA E RUIDO, e uma raia sozinha de ruido e sorte: compara-se a MEDIA de varias raias dentro da
		// banda com a de varias fora dela.
		double dentro = new[] { 700f, 1000f, 1500f, 2000f, 2500f }.Average(hz => Energia(cauda, hz));
		double fora = new[] { 50f, 100f, 8000f, 9300f, 11000f }.Average(hz => Energia(cauda, hz));
		Conferir(dentro > fora * 4, "o estalo tambem e de RADIO: a estatica dele passa pela mesma banda",
			$"dentro {dentro:0.00000}, fora {fora:0.00000}");
	}

	// =====================================================================
	// 6. A TECLA
	// =====================================================================
	private void ATeclaDoRadio()
	{
		Teclas.Aplicar(Boot.Config);

		AcaoDeTecla? acao = Teclas.Acao("falar_radio");
		Conferir(acao is { NoInputMap: true, Fixa: false }, "a acao de falar no radio existe no registro de teclas, no `InputMap` e remapeavel");
		Key[] dela = Teclas.Teclado("falar_radio");
		Conferir(dela.Length == 1 && Array.IndexOf(Teclas.Teclado("falar_voz"), dela[0]) < 0,
			"ela tem tecla PROPRIA de fabrica, que nao e a da voz de perto", Teclas.NomeDaAcao("falar_radio"));
		if (dela.Length == 0) return;

		Conferir(Teclas.DonoDe(dela[0])?.Rotulo == acao?.Rotulo, "a tecla pertence a ela, e a mais ninguem", Teclas.DonoDe(dela[0])?.Rotulo ?? "(livre)");
		Conferir(InputMap.HasAction("falar_radio") && InputMap.ActionGetEvents("falar_radio").Count == 1,
			"e esta projetada no `InputMap` (e de la que o microfone le)");
		Conferir(Array.IndexOf(Teclas.Teclado("falar_voz"), Key.V) >= 0, "a voz de perto continua no V, como estava");
		Conferir(Teclas.Ajuda().Any(l => l.Tecla.Contains(Teclas.Nome(dela[0]), StringComparison.Ordinal)
										 && l.Oque.Contains("rádio", StringComparison.OrdinalIgnoreCase)),
			"a ajuda do TAB ensina a tecla do radio");

		// REMAPEAVEL COMO QUALQUER OUTRA, e devolvida em seguida (a bancada nao deixa config mexida).
		bool foi = Teclas.Religar("falar_radio", Key.Kp7);
		Conferir(foi && Teclas.Teclado("falar_radio") is [Key.Kp7], "ela se religa como qualquer outra tecla", Teclas.NomeDaAcao("falar_radio"));
		Teclas.Restaurar("falar_radio");
		Conferir(Teclas.Teclado("falar_radio").SequenceEqual(dela), "e 'restaurar' a devolve pra tecla de fabrica", Teclas.NomeDaAcao("falar_radio"));
	}

	// =====================================================================
	// 7. PRA ONDE VAI A VOZ
	// =====================================================================
	private void PraOndeVaiAVoz()
	{
		const RadioDoScouter.Destino nada = RadioDoScouter.Destino.Nenhum, perto = RadioDoScouter.Destino.Perto,
									 radio = RadioDoScouter.Destino.Radio, osDois = perto | radio;
		static RadioDoScouter.Destino V(bool apertar, bool v, bool r, bool scouter) => RadioDoScouter.ParaOndeVai(apertar, v, r, scouter);

		Conferir(V(true, false, false, true) == nada && V(true, true, false, true) == perto,
			"apertar-pra-falar: sem tecla nao sai nada; a tecla de voz fala de PERTO, como sempre");
		Conferir(V(true, false, true, true) == radio,
			"**segurando SO a tecla do radio, a voz vai pelo RADIO -- e nao pela proximidade**", $"{V(true, false, true, true)}");
		Conferir(V(true, true, true, true) == osDois, "com as DUAS seguradas, vao as duas");
		Conferir(V(true, false, true, false) == nada && V(true, true, true, false) == perto,
			"**sem scouter ligado a tecla do radio nao transmite** (e a de voz continua valendo)");
		Conferir(V(false, false, false, true) == perto && V(false, false, false, false) == perto,
			"microfone ABERTO: sem tecla nenhuma a voz de perto esta no ar");
		Conferir(V(false, false, true, true) == radio && V(false, true, true, true) == osDois,
			"microfone aberto: a tecla do radio DESVIA a voz pro radio; com a de voz junto, vao as duas");
		Conferir(V(false, false, true, false) == perto, "microfone aberto e sem scouter: a tecla do radio nao muda nada");

		RadioDoScouter.RadioVazaPorPertoDeTeste = true;
		try { Injetar("com o radio vazando pela proximidade, 'so a tecla do radio = so o radio' reprova", V(true, false, true, true) == osDois); }
		finally { RadioDoScouter.RadioVazaPorPertoDeTeste = false; }
	}

	// =====================================================================
	// 8. A FAIXA
	// =====================================================================
	private void AFaixa()
	{
		Conferir(RadioOuvido.TextoDaFaixa(42, false, []) == "", "sem radio no ar a faixa nao existe");
		string tx = RadioOuvido.TextoDaFaixa(42, true, []);
		Conferir(tx.Contains("042") && tx.Contains("transmitindo"), "quem transmite ve que esta no radio, e em que frequencia", tx);
		string rx = RadioOuvido.TextoDaFaixa(7, false, ["Fulano", "Beltrano"]);
		Conferir(rx.Contains("007") && rx.Contains("Fulano") && rx.Contains("Beltrano") && !rx.Contains("transmitindo"),
			"quem recebe ve QUEM esta falando no radio", rx);
		string osDois = RadioOuvido.TextoDaFaixa(7, true, ["Fulano"]);
		Conferir(osDois.Contains("transmitindo") && osDois.Contains("Fulano"), "e as duas coisas juntas cabem na mesma faixa", osDois);
	}

	// =====================================================================
	// 9. O CAMINHO INTEIRO
	// =====================================================================
	/// <summary>
	/// PACOTES OPUS DE VERDADE (o codificador de producao, um tom de 1 kHz) entregues ao <see cref="RadioOuvido"/>
	/// de producao no ritmo da fala. O que se mede e o que ele mandou pro gerador (o `Espiao`): voz no
	/// esquerdo, zero no direito, o estalo de fechar, e o "esta falando" que acende e apaga.
	///
	/// O gerador aqui e o do driver Dummy (consome em blocos de ~100 ms): nao se mede picote -- isso e da
	/// `--diagvoz`, com gerador simulado, e a fila de jitter e a mesma classe.
	/// </summary>
	private async System.Threading.Tasks.Task OCaminhoInteiro()
	{
		const int quem = 4242;
		var radio = new RadioOuvido { Name = "RadioOuvidoDeTeste", QuemE = id => id == quem ? "Fulano" : "" };
		AddChild(radio);

		int quadros = 0, estalos = 0, quadrosComDireito = 0;
		double maiorDireito = 0, somaEsq = 0, maiorDoEstalo = 0, direitoDoEstalo = 0;
		bool falandoDurante = false;
		string faixaDurante = "";
		RadioOuvido.Espiao = (id, estereo, n, ehEstalo) =>
		{
			if (id != quem) return;
			double dir = 0, esq = 0;
			for (int i = 0; i < n; i++) { dir = Math.Max(dir, Math.Abs(estereo[i].Y)); esq = Math.Max(esq, Math.Abs(estereo[i].X)); }
			if (ehEstalo) { estalos++; maiorDoEstalo = esq; direitoDoEstalo = dir; return; }
			quadros++;
			maiorDireito = Math.Max(maiorDireito, dir);
			if (dir > 0) quadrosComDireito++;
			somaEsq += Energia(i => estereo[i].X, n, 1000f);
		};

		IOpusEncoder enc = Microfone.CriarCodificador();
		var pcm = new short[VozLocal.AmostrasPorQuadro];
		var pacote = new byte[VozLocal.MaxBytesDeQuadro];

		async System.Threading.Tasks.Task Falar(int quantos, ushort desde)
		{
			ulong t0 = Time.GetTicksMsec();
			for (int k = 0; k < quantos; k++)
			{
				while (Time.GetTicksMsec() - t0 < (ulong)(k * VozLocal.MsPorQuadro)) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				Array.Clear(pcm);
				Somar(pcm, 1000f, 0.4f, k * VozLocal.AmostrasPorQuadro, VozLocal.TaxaDeAmostragem);
				int n = enc.Encode(pcm, VozLocal.AmostrasPorQuadro, pacote, pacote.Length);
				radio.Receber(quem, (ushort)(desde + k), pacote, n);
				if (k == quantos - 5) { falandoDurante = radio.Falando(quem); faixaDurante = radio.FaixaDeTeste; }
			}
		}

		async System.Threading.Tasks.Task Esperar(double segundos)
		{
			ulong t0 = Time.GetTicksMsec();
			while (Time.GetTicksMsec() - t0 < segundos * 1000) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		await Falar(50, 100);   // um segundo de fala
		await Esperar(1.0);     // ...e o silencio depois dela

		GD.Print($"[diagradio]   ...  pelo RadioOuvido: {quadros} quadros de voz, {estalos} estalo(s) de fechar, maior amostra no direito {maiorDireito:0.00000}");
		Conferir(quadros >= 25, "os pacotes de radio viram quadros de voz no gerador (fila de jitter + decodificador de producao)", $"{quadros} de 50");
		Conferir(quadros > 0 && somaEsq / Math.Max(quadros, 1) > 0.05, "o tom de 1 kHz chega no ouvido ESQUERDO, depois do efeito", $"energia media={somaEsq / Math.Max(quadros, 1):0.000}");
		Conferir(maiorDireito == 0 && quadrosComDireito == 0, "**NENHUM quadro pos uma amostra sequer no ouvido DIREITO**", $"{quadrosComDireito} quadro(s), maior={maiorDireito:0.00000}");
		Conferir(falandoDurante, "durante a transmissao, 'esta no radio' responde SIM pra quem fala (e o que acende o sinal sobre a cabeca)");
		Conferir(faixaDurante.Contains("ouvindo Fulano"), "...e a faixa escreve o NOME de quem esta falando", faixaDurante);
		Conferir(!radio.Falando(quem) && radio.FaixaDeTeste == "", "um segundo depois do ultimo pacote, ninguem esta no radio e a faixa sumiu", radio.FaixaDeTeste);
		Conferir(estalos == 1 && maiorDoEstalo > 0.01 && direitoDoEstalo == 0,
			"a transmissao FECHOU com um estalo -- um so, e tambem so no ouvido esquerdo", $"{estalos} estalo(s), pico {maiorDoEstalo:0.000}, direito {direitoDoEstalo:0.000}");

		// ---- a injecao, pelo caminho inteiro ----
		quadros = quadrosComDireito = 0;
		EfeitoDeRadio.NosDoisOuvidosDeTeste = true;
		try
		{
			await Falar(20, 5000);
			await Esperar(0.4);
			Injetar("com a voz nos dois ouvidos, 'nenhum quadro no direito' reprova pelo caminho inteiro", quadrosComDireito > 0, $"{quadrosComDireito} de {quadros}");
		}
		finally { EfeitoDeRadio.NosDoisOuvidosDeTeste = false; }

		RadioOuvido.Espiao = null;
		radio.QueueFree();
	}
}
