using Concentus;
using Concentus.Enums;
using Godot;
using Jandirus.Core.Social;

namespace Jandirus.Client;

/// <summary>
/// ============================ APERTA-PRA-FALAR: A METADE QUE SAI DAQUI ============================
/// Segurar a tecla de voz (V de fabrica, remapeavel como qualquer outra) captura o microfone,
/// comprime em Opus e manda um quadro de 20 ms por vez. Soltar **para** -- e para de verdade: o
/// tocador do microfone e `Stop`ado e liberado, nao "silenciado".
///
/// ============================ O QUE ESTE ARQUIVO NAO DECIDE ============================
/// **Pra quem a voz vai.** Nao ha destinatario neste pacote e nao pode haver: quem ouve e decisao do
/// servidor (`GameServer.Voz.cs`), e um campo aqui seria a porta pra um cliente modificado escolher a
/// mesa ao lado. Este arquivo so sabe dizer *"estou falando"*.
/// ====================================================================================
///
/// ============================ TRES DESLIGAMENTOS, E OS TRES PRECISAM EXISTIR ============================
///   1. **A opcao** (`Settings.VozLigada`, FALSA de fabrica) -- com ela desligada este no nem cria o
///      tocador do microfone. Nao ha captura, nao ha codificador, nao ha pacote. O aparelho nao existe.
///   2. **A tecla** -- solta, o tocador e liberado no mesmo quadro.
///   3. **Sair do mundo** -- este no e filho do `World`, entao ele morre com ele. Um autoload teria
///      deixado o microfone vivo na tela de login.
/// ====================================================================================================
///
/// ============================ O CAMINHO DE UMA AMOSTRA ATE O FIO ============================
///   captura (48 kHz, estereo) -> <see cref="Reamostrador"/> (mono, anti-alias, 24 kHz, quadros de 20 ms)
///   -> <see cref="QuadrosProntos"/> (no maximo 5 por visita: engasgo nao vira rajada)
///   -> <see cref="PortaoDeVoz"/> (e voz? com histerese e hangover) -> Opus -> `MandarVoz`.
/// As tres pecas sao classes puras porque cada uma e uma afirmacao que a `--diagvoz` mede em
/// amostras e em contagem de quadros -- nao ha foto de som.
/// ==========================================================================================
///
/// ============================ E O DRIVER DE ENTRADA FOI MEDIDO ANTES DE LIGAR ============================
/// `audio/driver/enable_input` mexe no driver de audio INTEIRO, e a suspeita era que ligar mudasse a
/// latencia do som que ja existe. **Medido** (Godot 4.7.1 mono, WASAPI, duas execucoes, 30 amostras):
///
///   |                  | enable_input=false | enable_input=true |
///   |------------------|--------------------|-------------------|
///   | mix_rate         |      48000 Hz      |      48000 Hz     |
///   | output_latency   |     0,010000 s     |     0,010000 s    |
///   | min/max em 30    |     0,010000 s     |     0,010000 s    |
///
/// Nenhuma mudanca mensuravel. Fica ligado no `project.godot` -- e o driver ABERTO nao e o microfone
/// LIGADO: sem um `AudioStreamMicrophone` tocando, nao entra amostra nenhuma no jogo.
/// ======================================================================================================
/// </summary>
public partial class Microfone : Node
{
	/// <summary>
	/// O QUANTO O SINAL DE CAPTURA E AMPLIFICADO ANTES DE VIRAR PCM.
	///
	/// Um. Nao ha ganho automatico e nao deve haver: um AGC caseiro (medir o pico e normalizar) sobe o
	/// chiado do quarto nos silencios exatamente quando nao ha nada pra ouvir, e a pessoa que reclama
	/// disso nao tem como saber que foi o jogo que fez. Quem quiser mais volume mexe no misturador do
	/// sistema, que e onde o ganho de microfone mora no resto do computador dela.
	/// </summary>
	// =====================================================================
	// AS TRES PECAS PURAS
	// =====================================================================
	/// <summary>
	/// ============================ O PORTAO: "ISTO E VOZ?", COM MEMORIA ============================
	/// Era um `if (pico &lt; limiar) return` por quadro, sem memoria nenhuma -- e isso cortava DENTRO das
	/// palavras: uma oclusiva ("p", "t") e 20 ms de quase nada entre dois sons, uma fricativa fraca
	/// ("f", "s" sussurrado) fica abaixo do limiar inteira, e a pausa entre duas silabas idem. Cada
	/// quadro cortado era um buraco de 20 ms que o ouvinte ouvia inteiro, e o decodificador dele
	/// perdia o estado no meio da palavra.
	///
	/// Duas memorias, e as duas sao necessarias:
	///   * **HISTERESE** -- abre no <c>limiar</c>, mas so fecha abaixo da METADE dele. O que fica entre
	///     os dois (a cauda de uma vogal, uma consoante fraca) nao abre o portao sozinho, mas tambem
	///     nao o fecha se ele ja estava aberto;
	///   * **HANGOVER** -- fechado o limiar, ainda saem <see cref="QuadrosDeHangover"/> quadros (260 ms)
	///     antes de calar. E o que cobre a pausa entre duas palavras sem reiniciar o fluxo no ouvinte
	///     (a `FilaDeJitter` da como parado um fluxo com 100 ms sem pacote).
	///
	/// O custo e um punhado de quadros de silencio comprimido por frase (o Opus os deixa com uns 10 B),
	/// e o ganho e a palavra inteira.
	/// ======================================================================================
	/// </summary>
	public sealed class PortaoDeVoz
	{
		/// <summary>13 quadros = 260 ms. O bastante pra pausa entre duas palavras; curto o bastante pra nao mandar o ar do quarto por um segundo depois de a pessoa calar.</summary>
		public const int QuadrosDeHangover = 13;

		private readonly int _hangover;
		private int _baixosSeguidos;

		/// <summary>O portao esta aberto AGORA -- ou seja, quadros estao saindo.</summary>
		public bool Aberto { get; private set; }

		/// <summary>O hangover e parametro pra bancada poder zera-lo e mostrar que a medicao o enxerga. Producao usa o padrao.</summary>
		public PortaoDeVoz(int quadrosDeHangover = QuadrosDeHangover) => _hangover = quadrosDeHangover;

		/// <summary>Este quadro sai? <paramref name="limiar"/> e amplitude de pico, 0 a 1.</summary>
		public bool Passa(short[] quadro, float limiar)
		{
			int pico = 0;
			foreach (short a in quadro) pico = Math.Max(pico, Math.Abs((int)a));
			float p = pico / (float)short.MaxValue;

			if (p >= limiar) { Aberto = true; _baixosSeguidos = 0; return true; }
			if (!Aberto) return false;
			if (p >= limiar * 0.5f) { _baixosSeguidos = 0; return true; }   // a histerese
			if (++_baixosSeguidos > _hangover) { Aberto = false; _baixosSeguidos = 0; return false; }
			return true;                                                     // o hangover
		}

		/// <summary>A tecla subiu: o que sobrou de memoria e da fala que acabou.</summary>
		public void Fechar() { Aberto = false; _baixosSeguidos = 0; }
	}

	/// <summary>
	/// ============================ O REAMOSTRADOR: 48 kHz ESTEREO -> 24 kHz MONO, SEM ALIAS ============================
	/// O motor entrega a 48 kHz (medido) e o codificador quer 24 kHz (era 16). Hoje isso e exatamente 2 pra 1,
	/// mas **nao da pra cravar 3**: a taxa de mistura e do driver e da placa da pessoa, e ha maquina
	/// que abre em 44,1 kHz. O passo e fracionario e a sobra atravessa os lotes -- zerar a cada lote
	/// somaria ate uma amostra de erro 50 vezes por segundo, e a voz sairia com o tom subindo.
	///
	/// ============================ POR QUE NAO E "PEGAR UMA A CADA TRES" ============================
	/// Era. E pegar uma a cada tres e amostrar a 16 kHz um sinal que tem conteudo ate 24 kHz: tudo o
	/// que estiver entre 8 e 24 kHz (o chiado do microfone, o "s", o estalo do teclado) DOBRA pra
	/// dentro da faixa da voz, em cima das consoantes. E a voz "aspera, metalica" que se ouvia. Aqui
	/// cada amostra de saida e a MEDIA das amostras de entrada cobertas pelo passo (um filtro de caixa,
	/// que zera exatamente em 16 kHz) depois de dois polos de passa-baixa em <see cref="CorteDoAntiAlias"/>.
	/// Juntos derrubam o que dobraria de 12 kHz em mais de 20 dB, por duas multiplicacoes por amostra.
	/// ==============================================================================================
	/// </summary>
	public sealed class Reamostrador
	{
		/// <summary>
		/// 10 kHz. Era 7, quando a voz saia a 16 kHz (banda ate 8 kHz): dois polos a 7 kHz custavam 1,7 dB
		/// em 3 kHz e derrubavam 12 kHz em 13 dB. A 24 kHz a banda vai ate 12 kHz, e um corte em 7 comeria
		/// justamente o que a taxa nova veio buscar (as consoantes de 6 a 10 kHz -- o "abafado" do dono).
		/// Dois polos a 10 kHz custam 0,8 dB em 3 kHz e derrubam 20 kHz (o que dobraria em 4 kHz) em 14 dB
		/// antes ainda da media; a media de 2 amostras tira mais 12 dB de 20 kHz.
		/// </summary>
		public const float CorteDoAntiAlias = 10000f;

		private readonly double _passo;
		private readonly float _a;
		private double _ateProximo;
		private float _p1, _p2;
		private double _soma;
		private int _n;

		private readonly short[] _quadro = new short[VozLocal.AmostrasPorQuadro];
		private int _noQuadro;

		/// <param name="taxaDeEntrada">A taxa do driver, lida na hora de abrir (ela pode ter mudado desde o boot).</param>
		public Reamostrador(double taxaDeEntrada)
		{
			_passo = taxaDeEntrada / VozLocal.TaxaDeAmostragem;
			_ateProximo = _passo;
			_a = 1f - MathF.Exp(-2f * MathF.PI * CorteDoAntiAlias / (float)taxaDeEntrada);
		}

		/// <summary>
		/// ENGOLE UM LOTE DA CAPTURA e entrega cada quadro de 20 ms que fechar. Devolve quantos fecharam.
		///
		/// MONO PELA MEDIA DOS DOIS CANAIS. O microfone chega em estereo mesmo sendo mono de verdade (o
		/// motor duplica), e pegar so o esquerdo perderia metade do sinal numa placa que entregue o mono
		/// no direito -- caso raro e mudo, que e o pior tipo.
		/// </summary>
		public int Alimentar(ReadOnlySpan<Vector2> cru, Action<short[]> quadroPronto)
		{
			int fechados = 0;
			foreach (Vector2 s in cru)
			{
				float v = (s.X + s.Y) * 0.5f;
				_p1 += _a * (v - _p1);
				_p2 += _a * (_p1 - _p2);
				_soma += _p2;
				_n++;

				if (--_ateProximo > 0) continue;
				_ateProximo += _passo;

				_quadro[_noQuadro++] = (short)Mathf.Clamp((int)(_soma / _n * short.MaxValue),
														  short.MinValue, short.MaxValue);
				_soma = 0;
				_n = 0;
				if (_noQuadro < VozLocal.AmostrasPorQuadro) continue;
				_noQuadro = 0;
				quadroPronto(_quadro);
				fechados++;
			}
			return fechados;
		}

		/// <summary>
		/// O RESTO DO QUADRO MEIO MONTADO E JOGADO FORA, e nao completado com silencio: ele e o pedaco
		/// de silaba que sobrou no instante em que a tecla subiu, e manda-lo faria a fala terminar com
		/// um estalo em vez de terminar.
		/// </summary>
		public void Descartar() => _noQuadro = 0;
	}

	/// <summary>
	/// O GANHO AUTOMATICO (AGC): o que faz a voz de todo mundo chegar na MESMA altura.
	///
	/// O dono (2026-09-05): *"o audio do voice chat naturalmente ta muito baixo, poderia deixar ele
	/// mais alto"*. Um ganho fixo nao resolve: o microfone de um chega a -30 dBFS e o de outro a -6,
	/// e o numero que levanta um estoura o outro. Entao o ganho e MEDIDO por quadro: a energia (RMS)
	/// do quadro e comparada com o alvo e o ganho caminha ate la -- rapido pra descer (uma voz alta
	/// nao pode estourar nem por um quadro), devagar pra subir (senao o fim de cada frase infla o ar).
	///
	/// So age em quadro que ja passou pelo PORTAO: quem decide "e voz" e o portao no sinal cru, e o
	/// AGC nao amplifica o silencio entre frases. O piso de sinal e o teto de 8x (18 dB) sao o que
	/// impede um microfone mudo de virar chiado. Puro, sem Godot: a `--diagvoz` mede com senos.
	/// </summary>
	public sealed class ControleDeGanho
	{
		/// <summary>~-18 dBFS: alto sem encostar no teto -- o nivel de uma voz gravada "de perto".</summary>
		public const float RmsAlvo = 0.12f;
		/// <summary>+18 dB. Acima disso o que sobe e o ar do quarto.</summary>
		public const float GanhoMax = 8f;
		/// <summary>Quanto do caminho ate o alvo o ganho anda por quadro: subindo devagar, descendo rapido.</summary>
		public const float SubidaPorQuadro = 0.08f, DescidaPorQuadro = 0.5f;
		/// <summary>Abaixo disto o quadro e ar: o ganho nao mexe (nao sobe atras de silencio).</summary>
		public const float PisoDeSinal = 0.003f;

		private readonly float _teto;

		public float Ganho { get; private set; } = 1f;

		public ControleDeGanho(float ganhoMax = GanhoMax) => _teto = Math.Max(ganhoMax, 1f);

		public void Aplicar(short[] quadro)
		{
			double soma = 0;
			foreach (short a in quadro) { double v = a / (double)short.MaxValue; soma += v * v; }
			float rms = (float)Math.Sqrt(soma / Math.Max(quadro.Length, 1));
			if (rms >= PisoDeSinal)
			{
				float alvo = Math.Clamp(RmsAlvo / rms, 1f, _teto);
				Ganho += (alvo - Ganho) * (alvo < Ganho ? DescidaPorQuadro : SubidaPorQuadro);
			}
			if (Ganho <= 1.0001f) return;

			for (int i = 0; i < quadro.Length; i++)
			{
				float x = quadro[i] / (float)short.MaxValue * Ganho;
				// JOELHO MACIO acima de 0,85: o pico que passaria de 1,0 e dobrado, nao serrado.
				float m = Math.Abs(x);
				if (m > 0.85f) m = 0.85f + (m - 0.85f) / (1f + (m - 0.85f) * 4f);
				x = Math.Sign(x) * Math.Min(m, 0.999f);
				quadro[i] = (short)(x * short.MaxValue);
			}
		}
	}

	/// <summary>
	/// ============================ OS QUADROS DE UMA VISITA: ENGASGO NAO VIRA RAJADA ============================
	/// O dreno roda por quadro de TELA. Quando o jogo trava (carregar uma zona, um pico de coleta de
	/// lixo) a captura continua enchendo, e a visita seguinte encontra meio segundo acumulado: 25
	/// quadros de uma vez. Despejar os 25 e mandar 25 pacotes num milissegundo -- a torneira do servidor
	/// recusa a maior parte (em silencio, e o ouvinte ouve o buraco), e os que passam sao os mais
	/// VELHOS, que chegam com meio segundo de atraso e empurram o resto da conversa pra tras.
	///
	/// Este anel guarda no maximo <see cref="Teto"/> quadros: o que chega por cima derruba o mais velho.
	/// Depois de um travamento saem os ultimos 100 ms (o que a pessoa acabou de dizer) e o que ficou
	/// pra tras e um buraco -- que existiu mesmo, porque o jogo travou. E o mesmo remendo que as
	/// bancadas de dois clientes carregavam no relogio delas (*"um engasgo longo nao vira rajada"*),
	/// agora no caminho de producao, onde a captura de verdade acumula.
	/// ============================================================================================
	/// </summary>
	public sealed class QuadrosProntos
	{
		/// <summary>5 quadros = 100 ms. Um terco do balde do servidor (15): a rajada legitima passa inteira, com folga pra rede juntar duas.</summary>
		public const int Teto = 5;

		private readonly short[][] _anel;
		private int _inicio, _n;

		/// <summary>Quantos quadros foram derrubados por engasgo. So a bancada le.</summary>
		public int Descartados { get; private set; }

		/// <summary>O teto e parametro pela mesma razao do hangover: a bancada mostra que enxerga a queda.</summary>
		public QuadrosProntos(int teto = Teto)
		{
			_anel = new short[teto][];
			for (int i = 0; i < teto; i++) _anel[i] = new short[VozLocal.AmostrasPorQuadro];
		}

		public void Por(short[] quadro)
		{
			if (_n == _anel.Length)
			{
				_inicio = (_inicio + 1) % _anel.Length;
				_n--;
				Descartados++;
			}
			Array.Copy(quadro, _anel[(_inicio + _n) % _anel.Length], VozLocal.AmostrasPorQuadro);
			_n++;
		}

		/// <summary>Entrega o que ha, do mais velho pro mais novo, e esvazia.</summary>
		public void Despachar(Action<short[]> mandar)
		{
			for (int i = 0; i < _n; i++) mandar(_anel[(_inicio + i) % _anel.Length]);
			_inicio = 0;
			_n = 0;
		}
	}

	// =====================================================================
	// O ESTADO
	// =====================================================================
	private AudioStreamPlayer? _tocador;
	private AudioEffectCapture? _captura;
	private IOpusEncoder? _codificador;
	private Reamostrador? _reamostrador;
	private readonly QuadrosProntos _prontos = new();
	private readonly PortaoDeVoz _portao = new();
	private readonly ControleDeGanho _ganho = new();

	/// <summary>Saida do codificador. Reusado; alocar 50x/s e lixo.</summary>
	private readonly byte[] _comprimido = new byte[VozLocal.MaxBytesDeQuadro];

	/// <summary>O quadro que a fonte de teste enche. Reusado pelo mesmo motivo.</summary>
	private readonly short[] _quadroDeTeste = new short[VozLocal.AmostrasPorQuadro];

	/// <summary>A sequencia que este cliente carimba. O servidor nao a repassa -- ver `S2C.Voz`.</summary>
	private ushort _seq;

	/// <summary>Estou transmitindo AGORA? E o que acende o sinal sobre a minha propria cabeca.</summary>
	public static bool Falando { get; private set; }

	/// <summary>
	/// ESPIA DE BANCADA -- nula em jogo. Recebe (bytes do quadro, amostras que entraram nele) de cada
	/// quadro EFETIVAMENTE mandado.
	///
	/// Existe porque voz nao se fotografa. As duas afirmacoes que este arquivo faz -- *"soltar a tecla
	/// para de verdade"* e *"silencio nao vira pacote"* -- sao sobre o que **nao** acontece, e a
	/// ausencia de som nao deixa rastro em lugar nenhum. So a contagem de quadros com carimbo de tempo
	/// responde. Mesma razao do `AudioDirector.Espiao`.
	/// </summary>
	public static Action<int, int>? Espiao;

	/// <summary>
	/// ============================ A ONDA CONHECIDA, NO LUGAR DA CAPTURA -- SO BANCADA ============================
	/// Nula em jogo. Ligada, ela **substitui o microfone** e nada mais: o portao, o codificador, a
	/// sequencia e o `MandarVoz` continuam sendo os de producao, e e por isso que ela entra AQUI e nao
	/// numa copia do `Mandar` dentro do robo -- uma bancada que escreve o proprio caminho de envio prova
	/// o caminho dela, e nao o do jogo.
	///
	/// **A MAQUINA DE TESTE NAO TEM MICROFONE**, e nunca vai ter: a bancada de dois clientes roda
	/// `--headless`, onde o driver de audio e o Dummy e o `AudioStreamMicrophone` entrega zero amostra
	/// pra sempre. Sem esta porta, a unica coisa que dois processos poderiam medir da voz e que ninguem
	/// fala nada.
	///
	/// **O RITMO E DE QUEM INJETA**, e nao daqui: ela devolve falso quando ainda nao e hora do proximo
	/// quadro. Assim a politica de cadencia (50 quadros por segundo) mora na bancada, e este arquivo
	/// continua fazendo o que fazia -- tirar o que houver, quadro a quadro, ate acabar. E o que ela
	/// devolve passa pelo MESMO anel de <see cref="QuadrosProntos"/> que a captura: uma fonte que
	/// devolva 25 quadros numa visita so ve 5 saindo.
	/// ==========================================================================================================
	/// </summary>
	public static Func<short[], bool>? FonteDeTeste;

	public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

	public override void _Process(double delta)
	{
		bool quer = QuerFalar();

		// A BANCADA NAO ABRE APARELHO NENHUM: ela nao passa pelo `Abrir`, entao nao ha
		// `AudioStreamMicrophone`, nao ha dispositivo aberto e nao ha LED aceso na maquina de quem
		// roda o teste. So o codificador e o envio sao exercitados -- que e exatamente o pedaco que
		// dois processos existem pra medir.
		if (FonteDeTeste != null)
		{
			if (Falando && !quer) _portao.Fechar();
			Falando = quer;
			if (quer) DrenarDaFonteDeTeste();
			return;
		}

		if (quer && _tocador == null) Abrir();
		else if (!quer && _tocador != null) Fechar();

		if (_tocador != null) Drenar();
	}

	public override void _ExitTree() => Fechar();

	/// <summary>
	/// A PERGUNTA INTEIRA: a opcao esta ligada, estou no mundo, nao estou escrevendo, e o gesto
	/// (segurar a tecla, ou o modo aberto) esta valendo?
	///
	/// `Foco.AtalhosMudos` NAO E DETALHE. Sem ele, escrever "voce" no chat abriria o microfone no "v" --
	/// e o unico jeito de a pessoa descobrir isso seria alguem contando pra ela. E a mesma guarda que
	/// as outras vinte teclas do jogo ja passam, pelo mesmo funil.
	///
	/// E O "V" E UMA DAS 22 LETRAS DO EMBATE: com so o `Digitando` aqui, responder ao quick time event
	/// com a letra V abria o microfone do jogador no meio da briga. Ver `Foco.AtalhosMudos` -- e repare
	/// que este e um ponto de SONDA (`IsActionPressed`), o tipo que `SetInputAsHandled` nao alcanca:
	/// aqui o bloqueio so pode ser no ponto de LEITURA.
	/// </summary>
	private static bool QuerFalar()
	{
		if (!Boot.Config.VozLigada) return false;
		if (GameClient.Instance is not { Connected: true, LocalId: not 0 }) return false;
		if (Foco.AtalhosMudos) return false;
		return !Boot.Config.VozApertarParaFalar || Godot.Input.IsActionPressed("falar_voz");
	}

	// =====================================================================
	// ABRIR E FECHAR
	// =====================================================================
	private void Abrir()
	{
		_captura = AudioDirector.Captura();
		if (_captura == null)
		{
			GD.PushWarning("[voz] barramento de captura ausente -- o microfone nao vai abrir");
			return;
		}

		// O DISPOSITIVO ESCOLHIDO, se ele ainda existe. Um fone desplugado deixa o nome gravado
		// apontando pra nada, e escrever um nome invalido no `InputDevice` do Godot deixa a captura
		// MUDA sem erro nenhum -- o pior desfecho possivel, porque a pessoa fala e ninguem diz nada.
		string escolhido = Boot.Config.DispositivoDeVoz;
		if (escolhido.Length > 0 && Array.IndexOf(AudioServer.GetInputDeviceList(), escolhido) >= 0)
			AudioServer.InputDevice = escolhido;

		_codificador ??= CriarCodificador();

		_tocador = new AudioStreamPlayer
		{
			Stream = new AudioStreamMicrophone(),
			Bus = AudioDirector.BusCaptura,
		};
		AddChild(_tocador);
		_tocador.Play();

		// A TAXA DE MISTURA E LIDA AGORA, e nao no `_Ready`: ela e do driver e o driver pode ter
		// trocado de dispositivo desde o boot. O reamostrador nasce com ela, e com a memoria zerada.
		_reamostrador = new Reamostrador(AudioServer.GetMixRate());
		_portao.Fechar();

		// O QUE JA ESTAVA NO BUFFER E LIXO: sao as amostras do intervalo em que ninguem queria falar.
		// Sem esta linha, cada aperto da tecla comecaria mandando ate um segundo do que aconteceu ANTES
		// do aperto -- ou seja, exatamente o que o aperta-pra-falar existe pra nao mandar.
		_captura.ClearBuffer();

		Falando = true;
	}

	private void Fechar()
	{
		Falando = false;
		_portao.Fechar();
		if (_tocador == null) return;

		// STOP **E** QUEUEFREE. Parar sem liberar deixa o `AudioStreamMicrophone` preso ao no, e o
		// Godot mantem o dispositivo aberto enquanto houver um: o LED do microfone continuaria aceso
		// depois de soltar a tecla. "Para de verdade" e literal.
		_tocador.Stop();
		_tocador.QueueFree();
		_tocador = null;

		_reamostrador?.Descartar();
		_reamostrador = null;
		_captura?.ClearBuffer();
	}

	/// <summary>
	/// O CODIFICADOR COM OS NUMEROS DE PRODUCAO. `internal` porque a `--diagvoz` codifica com ELE os
	/// pacotes que alimenta na fila de jitter: uma copia dos parametros dentro da bancada e a
	/// primeira coisa a divergir quando alguem mexer aqui.
	/// </summary>
	internal static IOpusEncoder CriarCodificador()
	{
		// SEM BIBLIOTECA NATIVA, explicitamente. O Concentus tenta carregar um `opus.dll` do sistema
		// se achar um, e ai o comportamento do jogo passaria a depender do que esta instalado na
		// maquina de quem joga -- inclusive uma versao com outro formato de pacote. Puro C# e o motivo
		// de esta biblioteca ter sido escolhida; ligar isso e o que garante que ela cumpra a promessa.
		OpusCodecFactory.AttemptToUseNativeLibrary = false;

		IOpusEncoder e = OpusCodecFactory.CreateEncoder(
			VozLocal.TaxaDeAmostragem, 1, OpusApplication.OPUS_APPLICATION_VOIP);

		e.Bitrate = VozLocal.BitsPorSegundo;
		e.Complexity = VozLocal.Complexidade;
		e.UseVBR = true;
		e.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;

		// FEC LIGADO, com 10% de perda declarada: o canal e nao confiavel de proposito (ver
		// `Protocol.ChannelVoz`), entao o quadro perdido nao volta -- o que da pra fazer e deixar o
		// SEGUINTE carregar uma versao grosseira do anterior, que e o que o FEC do Opus faz. Custa
		// poucos bytes e e a unica defesa possivel quando retransmitir esta fora de questao. Quem a
		// usa e a `FilaDeJitter` do ouvinte, na vaga que ficou vazia.
		e.UseInbandFEC = true;
		e.PacketLossPercent = 10;

		return e;
	}

	// =====================================================================
	// DRENAR, REAMOSTRAR, CODIFICAR
	// =====================================================================
	/// <summary>
	/// TIRA O QUE O MOTOR CAPTUROU, REAMOSTRA PRA 24 kHz E MANDA OS QUADROS QUE FECHAREM.
	///
	/// ============================ ELE RODA POR QUADRO DE TELA, E NAO POR QUADRO DE AUDIO ============================
	/// O motor entrega em lotes de ate 1024 amostras (21,3 ms a 48 kHz -- medido), e a tela roda bem
	/// mais rapido que isso. Entao a maioria das visitas aqui nao encontra nada e sai; quando encontra,
	/// costuma fechar exatamente um quadro de 20 ms. Um quadro de tela longo acumula dois ou tres
	/// lotes, e eles saem juntos -- ate o teto do anel (ver <see cref="QuadrosProntos"/>): um
	/// travamento de meio segundo NAO sai como 25 pacotes de uma vez.
	/// ==========================================================================================================
	/// </summary>
	private void Drenar()
	{
		if (_captura == null || _reamostrador == null) return;

		int disponivel = _captura.GetFramesAvailable();
		if (disponivel <= 0) return;

		Vector2[] cru = _captura.GetBuffer(disponivel);
		if (cru.Length == 0) return;

		_reamostrador.Alimentar(cru, _prontos.Por);
		_prontos.Despachar(Mandar);
	}

	/// <summary>
	/// O LACO DA BANCADA -- o mesmo formato do <see cref="Drenar"/>: tira quadro enquanto houver, pelo
	/// mesmo anel. O `while` nao e enfeite: um quadro de tela longo deixa dois ou tres quadros de audio
	/// atrasados, e a fonte os devolve juntos. Sem ele a bancada mediria menos quadros do que injetou e
	/// chamaria a diferenca de "perda de rede".
	/// </summary>
	private void DrenarDaFonteDeTeste()
	{
		_codificador ??= CriarCodificador();
		while (FonteDeTeste!(_quadroDeTeste)) _prontos.Por(_quadroDeTeste);
		_prontos.Despachar(Mandar);
	}

	/// <summary>
	/// A ENTRADA DA `--diagvoz` NO DRENO: ela nao tem conexao, e o portao da tecla (`QuerFalar`) exige
	/// uma. O que fica DEPOIS desse portao -- o anel, o portao de voz, o codificador, o espiao -- e o
	/// que ela mede; o portao da tecla e provado com tecla de verdade pela `--vozdupla`.
	/// </summary>
	internal void DrenarDeTeste() => DrenarDaFonteDeTeste();

	/// <summary>Quantos quadros o anel derrubou por engasgo. So a bancada le.</summary>
	internal int QuadrosDescartadosPorEngasgo => _prontos.Descartados;

	/// <summary>
	/// UM QUADRO PRONTO: passa pelo portao, comprime e manda.
	///
	/// ============================ SILENCIO NAO VIRA PACOTE -- MAS PAUSA NAO E SILENCIO ============================
	/// O quadro tem que passar pelo <see cref="PortaoDeVoz"/> (limiar <see cref="Settings.LimiarDeVoz"/>).
	/// Vale **tambem no modo de apertar**, e nao so no aberto: quem segura a tecla pra pensar antes de
	/// falar estaria mandando o ar do quarto dele pra ate quatro pessoas, 50 vezes por segundo, de
	/// graca. O portao e o mesmo nos dois modos porque a pergunta e a mesma -- "isto e voz?". E a
	/// resposta tem memoria: a pausa entre duas palavras SAI (hangover), o quarto vazio nao.
	/// ==========================================================================================================
	/// </summary>
	private void Mandar(short[] quadro)
	{
		if (_codificador == null || GameClient.Instance is not { } cli) return;
		if (!_portao.Passa(quadro, Boot.Config.LimiarDeVoz)) return;
		_ganho.Aplicar(quadro);   // DEPOIS do portao: o portao decide no sinal cru, o AGC so levanta o que e voz

		int n = _codificador.Encode(quadro, VozLocal.AmostrasPorQuadro,
									_comprimido, _comprimido.Length);
		// O CODIFICADOR PODE DEVOLVER 1 BYTE (o "quadro vazio" do Opus, quando o VBR decide que nao ha
		// nada). Mandar aquilo seria pagar 33 B de cabecalho pra transmitir nada; e negativo e erro.
		if (n <= 2) return;

		Espiao?.Invoke(n, VozLocal.AmostrasPorQuadro);
		cli.MandarVoz(_seq++, _comprimido, n);
	}
}
