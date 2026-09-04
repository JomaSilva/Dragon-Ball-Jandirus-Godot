using Concentus;
using Godot;
using Jandirus.Core.Social;

namespace Jandirus.Client;

/// <summary>
/// ============================ BANCADA DA VOZ, NO CLIENTE (`--diagvoz`) ============================
/// O servidor prova o CORTE (`--vozteste`: quem recebe e quem nao recebe). Esta prova a outra metade,
/// que e a unica que o dono vai ouvir: **o audio de verdade**.
///
/// ============================ POR QUE ELA MEDE AMOSTRAS E NAO CONSTANTES ============================
/// A ultima licao cara deste port: *"a bancada mede INTENCAO"* -- quatro mil checagens verdes com
/// quatro defeitos visuais em jogo, porque elas conferiam a variavel que eu tinha escrito em vez do
/// pixel que saiu. Som e ainda pior: nao ha nem foto.
///
/// Entao aqui nada le uma constante minha. A bancada:
///   * codifica um sinal CONHECIDO com o codificador de producao e o decodifica com o decodificador
///     de producao -- e mede o que voltou;
///   * joga um sinal de DUAS frequencias no filtro de producao e MEDE quanta energia sobrou em cada
///     uma. "A parede corta agudo" vira um numero, e nao uma afirmacao;
///   * acompanha a rampa quadro a quadro -- porque um valor sozinho nunca distingue rampa de degrau.
/// ================================================================================================
///
/// AS OITO SECOES (as tres ultimas em `RoboDeVoz.Fio.cs`):
///  1. O CODEC -- ida e volta com os parametros de producao, e o tamanho do quadro medido.
///  2. A PAREDE ABAFA DE VERDADE -- energia em 3 kHz contra energia em 300 Hz, antes e depois.
///  3. A RAMPA E RAMPA -- e ela volta quando a parede sai.
///  4. A TECLA -- o V, pelo registro, e remapeavel.
///  5. O MICROFONE DESLIGADO NAO ABRE NADA -- a opcao e um aparelho que nao existe, e nao um `if`.
///  6. O FIO COM JITTER -- a `FilaDeJitter` de producao com pacotes reais chegando torto, e o
///     DEFEITO INJETADO (fila desligada) que tem que picotar.
///  7. O EMISSOR -- portao com histerese e hangover, engasgo que nao vira rajada, anti-alias.
///  8. A TORNEIRA -- rajada de 10 passa, rajada de 30 perde 15.
///
/// COMO RODAR (sem mundo, sem rede, sem personagem):
///     Godot --headless --path . --diagvoz
/// </summary>
public partial class RoboDeVoz : Node
{
	private readonly List<string> _falhas = [];

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		if (ok) { GD.Print($"[diagvoz]   ok    {oque}"); return; }
		_falhas.Add(oque);
		GD.PrintErr($"[diagvoz]   FALHA {oque}   {detalhe}");
	}

	public override void _Ready()
	{
		GD.Print("[diagvoz] ============ VOZ: o audio, medido em amostras ============");

		OCodecVaiEVolta();
		OFecExisteDeVerdade();
		AParedeCortaOAgudoDeVerdade();
		ARampaEhRampa();
		ATeclaEhOV();
		ODesligadoNaoAbreNada();
		OFioComJitter();
		OEmissorNaoPicota();
		ATorneiraAceitaARajadaHonesta();

		// DOIS PLACARES, e os dois tem que fechar: as checagens (o sistema funciona) e as injecoes (a
		// bancada enxerga quando ele nao funciona). Ver `Injetar`.
		GD.Print($"[diagvoz]   ...  injecoes: {_injecoesQuePegaram} pegaram, {_injecoesQuePassaramBatido.Count} passaram batido");
		bool ok = _falhas.Count == 0 && _injecoesQuePassaramBatido.Count == 0;
		GD.Print(ok
			? "[diagvoz] ============ TUDO OK ============"
			: $"[diagvoz] ============ {_falhas.Count} FALHA(S): {string.Join(" | ", _falhas)}"
			  + (_injecoesQuePassaramBatido.Count > 0 ? $" | INJECAO PASSOU BATIDO: {string.Join(" | ", _injecoesQuePassaramBatido)}" : "")
			  + " ============");

		GetTree().Quit(ok ? 0 : 1);
	}

	// =====================================================================
	// SINAIS
	// =====================================================================
	/// <summary>Um seno de <paramref name="hz"/> Hz, amplitude <paramref name="amp"/>, num quadro.</summary>
	private static void Somar(short[] alvo, float hz, float amp, int fase = 0)
	{
		for (int i = 0; i < alvo.Length; i++)
		{
			double v = alvo[i] / (double)short.MaxValue;
			v += amp * Math.Sin(2 * Math.PI * hz * (i + fase) / VozLocal.TaxaDeAmostragem);
			alvo[i] = (short)Math.Clamp((int)(v * short.MaxValue), short.MinValue, short.MaxValue);
		}
	}

	/// <summary>
	/// QUANTA ENERGIA HA NESTA FREQUENCIA -- correlacao com seno e cosseno (uma raia de Goertzel).
	///
	/// E o instrumento inteiro desta bancada: e ele que transforma "soa abafado" numa comparacao de
	/// dois numeros. Sem ele so daria pra afirmar que o filtro FOI CHAMADO.
	/// </summary>
	private static double Energia(short[] pcm, int n, float hz)
	{
		double re = 0, im = 0;
		for (int i = 0; i < n; i++)
		{
			double ang = 2 * Math.PI * hz * i / VozLocal.TaxaDeAmostragem;
			double v = pcm[i] / (double)short.MaxValue;
			re += v * Math.Cos(ang);
			im += v * Math.Sin(ang);
		}
		return Math.Sqrt(re * re + im * im) / n;
	}

	// =====================================================================
	// 1. O CODEC
	// =====================================================================
	/// <summary>
	/// IDA E VOLTA COM OS PARAMETROS DE PRODUCAO.
	///
	/// A afirmacao que importa nao e "o Opus funciona" -- e que **os nossos numeros** funcionam: 16 kHz,
	/// 20 ms, 16 kbit/s, complexidade 3. Um erro em qualquer um deles (taxa de um lado, quadro do
	/// outro) nao da erro nenhum: da ruido branco, e ruido branco e o que um jogador chama de "a voz
	/// nao presta".
	///
	/// E ela CONFERE O TAMANHO MEDIDO: os ~65 B por quadro sao a base da conta de banda inteira
	/// (3,2 KB/s por fluxo, 10x mais barato que PCM cru). Se um dia isso virar 200 B, o teto por
	/// falante e o teto de quatro vozes passam a estar dimensionados pra outro mundo.
	///
	/// E ELA CONFERE QUE O FEC EXISTE (<see cref="OFecExisteDeVerdade"/>): a 16 kbit/s o Opus nao
	/// escrevia a copia redundante, e ninguem tinha como saber -- `decode_fec` devolvia PLC calado.
	/// </summary>
	private void OCodecVaiEVolta()
	{
		// O CODIFICADOR DE PRODUCAO, e nao uma copia dos parametros dele: uma copia e a primeira coisa
		// a divergir quando alguem mexer no `Microfone`, e a bancada seguiria verde medindo outro codec.
		IOpusEncoder enc = Microfone.CriarCodificador();

		IOpusDecoder dec = OpusCodecFactory.CreateDecoder(VozLocal.TaxaDeAmostragem, 1);

		var comprimido = new byte[VozLocal.MaxBytesDeQuadro];
		var volta = new short[VozLocal.AmostrasPorQuadro];

		// DEZ QUADROS, e nao um: o Opus tem estado (o primeiro quadro sai frio e o codificador ainda
		// esta escolhendo o modo). Medir so o primeiro daria um numero que nao e o de uma conversa.
		int somaBytes = 0, quadros = 0;
		double energiaDeVolta = 0;
		for (int q = 0; q < 10; q++)
		{
			var pcm = new short[VozLocal.AmostrasPorQuadro];
			Somar(pcm, 440f, 0.5f, q * VozLocal.AmostrasPorQuadro);

			int n = enc.Encode(pcm, VozLocal.AmostrasPorQuadro, comprimido, comprimido.Length);
			Conferir(n > 2 && n <= VozLocal.MaxBytesDeQuadro,
				$"quadro {q}: o codificador devolveu um tamanho plausivel", $"n={n}");
			somaBytes += n;
			quadros++;

			int amostras = dec.Decode(comprimido.AsSpan(0, n), volta, VozLocal.AmostrasPorQuadro, false);
			Conferir(amostras == VozLocal.AmostrasPorQuadro,
				$"quadro {q}: voltaram as {VozLocal.AmostrasPorQuadro} amostras", $"{amostras}");
			energiaDeVolta = Energia(volta, amostras, 440f);
		}

		double media = somaBytes / (double)quadros;
		GD.Print($"[diagvoz]   ...  {media:0.0} B por quadro  =  {media * VozLocal.QuadrosPorSegundo / 1024:0.00} KB/s por fluxo");

		// O NUMERO MEDIDO A 24 kbit/s COM FEC E ~65 B (um tom puro sai menor). A folga e generosa de
		// proposito: o ponto nao e cravar o decimal, e pegar a ordem de grandeza mudando -- que e o que
		// invalidaria o dimensionamento da banda.
		Conferir(media is > 25 and < 100,
			"o quadro comprimido continua na casa dos 65 B (a conta de banda toda depende disso)",
			$"{media:0.0} B");

		// E O SINAL VOLTOU: o tom de 440 Hz sobreviveu a ida e volta. Sem esta linha, um codec que
		// devolvesse silencio passaria em tudo acima (tamanho certo, contagem certa).
		Conferir(energiaDeVolta > 0.05,
			"o tom de 440 Hz SOBREVIVEU a ida e volta (nao voltou silencio)",
			$"energia={energiaDeVolta:0.0000}");
	}

	// =====================================================================
	// 2. A PAREDE
	// =====================================================================
	/// <summary>
	/// A PAREDE CORTA AGUDO E DEIXA GRAVE PASSAR -- medido, e nao afirmado.
	///
	/// E o que uma parede faz de verdade: ouve-se o baixo do vizinho e nao a letra. O sinal de teste
	/// tem 300 Hz (a entonacao) e 3 kHz (as consoantes, que e onde mora a inteligibilidade). Depois da
	/// abafada o grave tem que sobrar MUITO mais que o agudo.
	///
	/// ============================ AS DUAS METADES SAO OBRIGATORIAS ============================
	/// So medir a queda do agudo passaria com um filtro que zerasse tudo -- que soa como a voz sumindo,
	/// e nao como uma parede. So medir a sobrevivencia do grave passaria com um filtro que nao filtra.
	/// As duas juntas descrevem a unica coisa que e uma parede.
	/// ======================================================================================
	/// </summary>
	private void AParedeCortaOAgudoDeVerdade()
	{
		const float grave = 300f, agudo = 3000f;

		var limpo = new short[VozLocal.AmostrasPorQuadro];
		Somar(limpo, grave, 0.4f);
		Somar(limpo, agudo, 0.4f);

		double gAntes = Energia(limpo, limpo.Length, grave);
		double aAntes = Energia(limpo, limpo.Length, agudo);
		Conferir(gAntes > 0.1 && aAntes > 0.1,
			"o sinal de teste tem energia nas DUAS frequencias antes de filtrar",
			$"grave={gAntes:0.000} agudo={aAntes:0.000}");

		// A RAMPA PRECISA CHEGAR AO FIM: 0,15 s a 20 ms por quadro sao 8 quadros. Medir no primeiro
		// mediria a rampa, e nao a parede.
		var filtro = new VozOuvida.FiltroDeParede { Parede = true };
		short[] filtrado = [];
		for (int q = 0; q < 20; q++)
		{
			filtrado = new short[VozLocal.AmostrasPorQuadro];
			Somar(filtrado, grave, 0.4f);
			Somar(filtrado, agudo, 0.4f);
			filtro.Aplicar(filtrado, filtrado.Length);
		}

		Conferir(filtro.Abafada > 0.99, "a rampa chegou ao fim em 20 quadros (0,4 s)",
			$"{filtro.Abafada:0.000}");

		double gDepois = Energia(filtrado, filtrado.Length, grave);
		double aDepois = Energia(filtrado, filtrado.Length, agudo);

		double quedaAgudo = aDepois / aAntes;
		double quedaGrave = gDepois / gAntes;
		GD.Print($"[diagvoz]   ...  atras da parede: grave x{quedaGrave:0.000}  agudo x{quedaAgudo:0.000}");

		Conferir(quedaAgudo < 0.15,
			"atras da parede o AGUDO some (a consoante, que e onde mora a letra)",
			$"sobrou {quedaAgudo * 100:0.0}%");
		Conferir(quedaGrave > 0.25,
			"e o GRAVE atravessa (da pra saber que alguem esta falando ali)",
			$"sobrou {quedaGrave * 100:0.0}%");
		Conferir(quedaGrave > quedaAgudo * 3,
			"ou seja: e um passa-BAIXA, e nao um volume abaixado",
			$"grave/agudo = {quedaGrave / Math.Max(quedaAgudo, 1e-9):0.0}x");

		// SEM PAREDE, NADA MUDA. E a guarda contra o oposto: um filtro sempre ligado deixaria o jogo
		// inteiro abafado, e ninguem teria com o que comparar pra perceber.
		var solto = new VozOuvida.FiltroDeParede { Parede = false };
		var intacto = new short[VozLocal.AmostrasPorQuadro];
		Somar(intacto, agudo, 0.4f);
		double antes = Energia(intacto, intacto.Length, agudo);
		solto.Aplicar(intacto, intacto.Length);
		Conferir(Math.Abs(Energia(intacto, intacto.Length, agudo) - antes) < 1e-6,
			"sem parede o sinal passa INTOCADO (nem uma amostra muda)");
	}

	// =====================================================================
	// 3. A RAMPA
	// =====================================================================
	/// <summary>
	/// A ABAFADA ENTRA POR RAMPA E NAO POR DEGRAU -- e a serie de valores e a unica prova possivel.
	///
	/// Por que importa: o servidor responde "ha parede?" com validade de 100 ms, e alguem parado no vao
	/// de uma porta faz a resposta oscilar. Sem rampa isso vira um chiado ligando e desligando. Um
	/// valor sozinho (`abafada == 1`) nunca distinguiria os dois casos.
	/// </summary>
	private void ARampaEhRampa()
	{
		var filtro = new VozOuvida.FiltroDeParede { Parede = true };
		var serie = new List<float>();
		var pcm = new short[VozLocal.AmostrasPorQuadro];

		for (int q = 0; q < 5; q++)
		{
			Array.Clear(pcm);
			Somar(pcm, 1000f, 0.3f);
			filtro.Aplicar(pcm, pcm.Length);
			serie.Add(filtro.Abafada);
		}

		GD.Print($"[diagvoz]   ...  rampa: {string.Join(" -> ", serie.Select(v => v.ToString("0.00")))}");

		Conferir(serie[0] > 0 && serie[0] < 0.5f,
			"o primeiro quadro atras da parede NAO ja esta abafado por inteiro (isso seria um degrau)",
			$"{serie[0]:0.000}");
		Conferir(serie.Zip(serie.Skip(1)).All(p => p.Second > p.First),
			"e ela sobe monotonicamente, quadro a quadro");

		// E ELA VOLTA. Uma rampa que so sobe deixaria abafado pra sempre quem passasse uma vez atras de
		// um muro -- e o jogador atribuiria isso a "a voz dele estragou".
		filtro.Parede = false;
		float pico = filtro.Abafada;
		for (int q = 0; q < 20; q++) filtro.Aplicar(pcm, pcm.Length);
		Conferir(filtro.Abafada < 0.001f && pico > 0,
			"saindo de tras da parede, a abafada VOLTA a zero", $"pico={pico:0.00}");
	}

	// =====================================================================
	// 4. A TECLA
	// =====================================================================
	/// <summary>
	/// O V, PELO REGISTRO -- e nao por um `if (k.Keycode == Key.V)` dentro do microfone.
	///
	/// A afirmacao dupla: a acao existe na tabela **e** o V pertence a ela. A primeira sozinha passaria
	/// com a acao registrada e a tecla ocupada por outra coisa; a segunda sozinha nao existe sem a
	/// primeira. E a terceira -- que ela e remapeavel -- e o que separa "esta na tabela" de "esta na
	/// tabela pra valer".
	/// </summary>
	private void ATeclaEhOV()
	{
		Teclas.Aplicar(Boot.Config);

		Conferir(Teclas.Acao("falar_voz") != null, "a acao de falar por voz existe no registro de teclas");
		Conferir(Array.IndexOf(Teclas.Teclado("falar_voz"), Key.V) >= 0,
			"e ela esta no V de fabrica", Teclas.NomeDaAcao("falar_voz"));

		(string Rotulo, bool Fixa)? dono = Teclas.DonoDe(Key.V);
		Conferir(dono?.Rotulo == Teclas.Acao("falar_voz")?.Rotulo,
			"o V pertence a ela, e a mais ninguem", dono?.Rotulo ?? "(livre)");

		// E O VOO SAIU DO V (o pedido 1 do dono). Sem esta linha, as duas acoes poderiam estar no V ao
		// mesmo tempo e `DonoDe` -- que devolve o PRIMEIRO da tabela -- esconderia uma delas.
		Conferir(Array.IndexOf(Teclas.Teclado("voar"), Key.V) < 0
				 && Array.IndexOf(Teclas.Teclado("voar"), Key.F) >= 0,
			"o voo saiu do V e esta no F", Teclas.NomeDaAcao("voar"));

		// REMAPEAVEL COMO QUALQUER OUTRA. Devolvido logo em seguida: a bancada nao pode deixar o
		// `config.json` da maquina do dono com a voz num Z.
		bool foi = Teclas.Religar("falar_voz", Key.Z);
		Conferir(foi && Array.IndexOf(Teclas.Teclado("falar_voz"), Key.Z) >= 0,
			"ela se religa como qualquer outra tecla");
		Teclas.Restaurar("falar_voz");
		Conferir(Array.IndexOf(Teclas.Teclado("falar_voz"), Key.V) >= 0,
			"e 'restaurar' a devolve pro V");
	}

	// =====================================================================
	// 5. O DESLIGADO
	// =====================================================================
	/// <summary>
	/// COM A VOZ DESLIGADA, O APARELHO NAO EXISTE -- e nao "existe e e ignorado".
	///
	/// ============================ A DIFERENCA E O QUARTO DA PESSOA ============================
	/// Um `if` no envio deixaria o `AudioStreamMicrophone` tocando: o dispositivo aberto, o LED aceso, o
	/// sistema operacional dizendo "este programa esta usando o microfone" -- com o jogo jurando que a
	/// voz esta desligada. A unica versao honesta de "desligado" e nao haver captura acontecendo.
	///
	/// A bancada mede pelo NUMERO DE FILHOS do `Microfone`: o tocador e um node, e node ou existe ou
	/// nao existe. Nao ha como isso passar com um `if` no lugar errado.
	/// ======================================================================================
	/// </summary>
	private void ODesligadoNaoAbreNada()
	{
		bool antes = Boot.Config.VozLigada;
		Boot.Config.VozLigada = false;

		// O ESPIAO CONTA OS QUADROS QUE SAIRAM. Sao DUAS afirmacoes diferentes e as duas precisam ser
		// feitas: "nao ha aparelho" (o node) e "nao saiu quadro" (o espiao). Um `if` no lugar errado
		// poderia deixar o dispositivo aberto sem mandar nada -- silencioso na rede e aceso no
		// sistema operacional, que e o pior dos dois mundos.
		int quadros = 0;
		void Contar(int bytes, int amostras) => quadros++;
		Microfone.Espiao += Contar;

		var mic = new Microfone { Name = "MicrofoneDeTeste" };
		AddChild(mic);
		// dois passos do `_Process`, na mao: um pra ele decidir, outro pra provar que ele nao mudou
		// de ideia no seguinte
		mic._Process(0.016);
		mic._Process(0.016);

		Conferir(mic.GetChildCount() == 0,
			"com a voz DESLIGADA o microfone nao cria tocador nenhum", $"{mic.GetChildCount()} filho(s)");
		Conferir(!Microfone.Falando, "e o jogo nao acha que eu estou falando");
		Conferir(quadros == 0, "e nenhum quadro saiu pela rede", $"{quadros} quadro(s)");

		// LIGADA MAS SEM MUNDO tambem nao abre: a segunda guarda do `QuerFalar` e estar conectado, e
		// esta bancada roda sem rede nenhuma. Sem ela, `--diagvoz` abriria o microfone da maquina de
		// quem roda o teste.
		Boot.Config.VozLigada = true;
		mic._Process(0.016);
		Conferir(mic.GetChildCount() == 0 && quadros == 0,
			"ligada mas fora do mundo, tambem nao abre (a bancada nao liga o microfone de ninguem)");

		// METODO NOMEADO E `-=`: `Espiao` e estatico e sobrevive a este node. Ver a licao das
		// assinaturas vazadas.
		Microfone.Espiao -= Contar;
		Boot.Config.VozLigada = antes;
		mic.QueueFree();
	}
}
