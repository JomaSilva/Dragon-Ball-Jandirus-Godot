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
	private const float Ganho = 1f;

	private AudioStreamPlayer? _tocador;
	private AudioEffectCapture? _captura;
	private IOpusEncoder? _codificador;

	/// <summary>O quadro sendo montado -- 320 amostras a 16 kHz. Reusado; alocar 50x/s e lixo.</summary>
	private readonly short[] _quadro = new short[VozLocal.AmostrasPorQuadro];
	private int _noQuadro;

	/// <summary>Saida do codificador. Reusado pelo mesmo motivo.</summary>
	private readonly byte[] _comprimido = new byte[VozLocal.MaxBytesDeQuadro];

	/// <summary>
	/// A POSICAO FRACIONARIA NO FLUXO DE ENTRADA -- o reamostrador.
	///
	/// O motor entrega a 48 kHz (medido) e o codificador quer 16 kHz. Hoje isso e exatamente 3 pra 1,
	/// mas **nao da pra cravar 3**: a taxa de mistura e do driver e da placa da pessoa, e ha maquina
	/// que abre em 44,1 kHz. Com um passo fracionario o mesmo codigo serve pras duas, e o unico preco
	/// e este `double` atravessar os lotes -- que e justamente o que ele precisa fazer, senao cada
	/// lote comecaria do zero e a voz sairia com um estalo por lote.
	/// </summary>
	private double _passo, _pos;

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
	/// Nula em jogo. Ligada, ela **substitui o microfone** e nada mais: o limiar, o codificador, a
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
	/// continua fazendo o que fazia -- tirar o que houver, quadro a quadro, ate acabar.
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
	/// `Foco.Digitando` NAO E DETALHE. Sem ele, escrever "voce" no chat abriria o microfone no "v" --
	/// e o unico jeito de a pessoa descobrir isso seria alguem contando pra ela. E a mesma guarda que
	/// as outras vinte teclas do jogo ja passam, pelo mesmo funil.
	/// </summary>
	private static bool QuerFalar()
	{
		if (!Boot.Config.VozLigada) return false;
		if (GameClient.Instance is not { Connected: true, LocalId: not 0 }) return false;
		if (Foco.Digitando) return false;
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
		// trocado de dispositivo desde o boot.
		_passo = AudioServer.GetMixRate() / (double)VozLocal.TaxaDeAmostragem;
		_pos = 0;
		_noQuadro = 0;

		// O QUE JA ESTAVA NO BUFFER E LIXO: sao as amostras do intervalo em que ninguem queria falar.
		// Sem esta linha, cada aperto da tecla comecaria mandando ate 200 ms do que aconteceu ANTES do
		// aperto -- ou seja, exatamente o que o aperta-pra-falar existe pra nao mandar.
		_captura.ClearBuffer();

		Falando = true;
	}

	private void Fechar()
	{
		Falando = false;
		if (_tocador == null) return;

		// STOP **E** QUEUEFREE. Parar sem liberar deixa o `AudioStreamMicrophone` preso ao no, e o
		// Godot mantem o dispositivo aberto enquanto houver um: o LED do microfone continuaria aceso
		// depois de soltar a tecla. "Para de verdade" e literal.
		_tocador.Stop();
		_tocador.QueueFree();
		_tocador = null;

		// O RESTO DO QUADRO MEIO MONTADO E JOGADO FORA, e nao completado com silencio: ele e o pedaco
		// de silaba que sobrou no instante em que a tecla subiu, e manda-lo faria a fala terminar com
		// um estalo em vez de terminar.
		_noQuadro = 0;
		_captura?.ClearBuffer();
	}

	private static IOpusEncoder CriarCodificador()
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
		// poucos bytes e e a unica defesa possivel quando retransmitir esta fora de questao.
		e.UseInbandFEC = true;
		e.PacketLossPercent = 10;

		return e;
	}

	// =====================================================================
	// DRENAR, REAMOSTRAR, CODIFICAR
	// =====================================================================
	/// <summary>
	/// TIRA O QUE O MOTOR CAPTUROU, REAMOSTRA PRA 16 kHz E MANDA OS QUADROS QUE FECHAREM.
	///
	/// ============================ ELE RODA POR QUADRO DE TELA, E NAO POR QUADRO DE AUDIO ============================
	/// O motor entrega em lotes de ate 1024 amostras (21,3 ms a 48 kHz -- medido), e a tela roda bem
	/// mais rapido que isso. Entao a maioria das visitas aqui nao encontra nada e sai; quando encontra,
	/// costuma fechar exatamente um quadro de 20 ms. O laco de dentro existe pro caso do engasgo: um
	/// quadro de tela longo acumula dois ou tres lotes, e eles saem juntos.
	/// ==========================================================================================================
	/// </summary>
	private void Drenar()
	{
		if (_captura == null || _codificador == null) return;

		int disponivel = _captura.GetFramesAvailable();
		if (disponivel <= 0) return;

		Vector2[] cru = _captura.GetBuffer(disponivel);
		if (cru.Length == 0) return;

		while (_pos < cru.Length)
		{
			int i = (int)_pos;
			Vector2 s = cru[i];

			// MONO PELA MEDIA DOS DOIS CANAIS. O microfone chega em estereo mesmo sendo mono de
			// verdade (o motor duplica), e pegar so o esquerdo perderia metade do sinal numa placa que
			// entregue o mono no direito -- caso raro e mudo, que e o pior tipo.
			float v = (s.X + s.Y) * 0.5f * Ganho;
			_quadro[_noQuadro++] = (short)Mathf.Clamp((int)(v * short.MaxValue), short.MinValue, short.MaxValue);
			_pos += _passo;

			if (_noQuadro < VozLocal.AmostrasPorQuadro) continue;
			_noQuadro = 0;
			Mandar();
		}

		// A SOBRA FRACIONARIA ATRAVESSA O LOTE. Zerar aqui somaria um erro de ate uma amostra por
		// lote -- 50 lotes por segundo --, e o efeito acumulado e a voz saindo com o tom subindo.
		_pos -= cru.Length;
	}

	/// <summary>
	/// O LACO DA BANCADA -- o mesmo formato do <see cref="Drenar"/>: tira quadro enquanto houver.
	///
	/// O `while` nao e enfeite: um quadro de tela longo (o cliente carregando uma zona, por exemplo)
	/// deixa dois ou tres quadros de audio atrasados, e a fonte os devolve juntos. Sem ele a bancada
	/// mediria menos quadros do que injetou e chamaria a diferenca de "perda de rede".
	/// </summary>
	private void DrenarDaFonteDeTeste()
	{
		_codificador ??= CriarCodificador();
		while (FonteDeTeste!(_quadro)) Mandar();
	}

	/// <summary>
	/// UM QUADRO PRONTO: mede, decide se e voz, comprime e manda.
	///
	/// ============================ SILENCIO NAO VIRA PACOTE ============================
	/// O pico do quadro tem que passar do <see cref="Settings.LimiarDeVoz"/>. Vale **tambem no modo de
	/// apertar**, e nao so no aberto: quem segura a tecla pra pensar antes de falar estaria mandando o
	/// ar do quarto dele pra ate quatro pessoas, 50 vezes por segundo, de graca. O portao e o mesmo nos
	/// dois modos porque a pergunta e a mesma -- "isto e voz?".
	/// ==============================================================================
	/// </summary>
	private void Mandar()
	{
		if (_codificador == null || GameClient.Instance is not { } cli) return;

		int pico = 0;
		foreach (short a in _quadro) pico = Math.Max(pico, Math.Abs((int)a));
		if (pico < Boot.Config.LimiarDeVoz * short.MaxValue) return;

		int n = _codificador.Encode(_quadro, VozLocal.AmostrasPorQuadro,
									_comprimido, _comprimido.Length);
		// O CODIFICADOR PODE DEVOLVER 1 BYTE (o "quadro vazio" do Opus, quando o VBR decide que nao ha
		// nada). Mandar aquilo seria pagar 33 B de cabecalho pra transmitir nada; e negativo e erro.
		if (n <= 2) return;

		Espiao?.Invoke(n, VozLocal.AmostrasPorQuadro);
		cli.MandarVoz(_seq++, _comprimido, n);
	}
}
