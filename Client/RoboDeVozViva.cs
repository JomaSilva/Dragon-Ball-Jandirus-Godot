using Godot;
using Jandirus.Core.Social;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ A VOZ ENTRE DOIS CLIENTES DE VERDADE -- `--vozviva &lt;a|b|c|d&gt;` ============================
/// A `--diagvoz` mede o CODEC e o FILTRO sem rede nenhuma; a `--vozteste` mede o CORTE do servidor com
/// corpos forjados. Nenhuma das duas atravessa o fio, e as duas dizem isso no proprio cabecalho. Esta e
/// a que atravessa: **o cliente `a` fala, o cliente `b` escuta, e o que o `b` mede sao amostras que
/// sairam do decodificador dele, num outro processo, com outra memoria.**
///
/// ============================ A ONDA CONHECIDA ENTRA NO LUGAR DA CAPTURA ============================
/// Nao ha microfone na maquina que roda bancada, e no `--headless` nao haveria nem driver. Entao o `a`
/// injeta uma onda que a bancada CONHECE (<see cref="OndaConhecida"/>) pela porta
/// <see cref="Microfone.FonteDeTeste"/> -- que substitui **so a captura**: limiar, codificador,
/// sequencia e `MandarVoz` continuam sendo os de producao.
///
/// **A ONDA TEM DUAS FREQUENCIAS DE PROPOSITO**: 300 Hz (a entonacao) e 3 kHz (as consoantes, onde mora
/// a inteligibilidade). Com um tom so daria pra dizer "chegou som"; com dois, a mesma gravacao responde
/// TRES perguntas diferentes -- se a onda atravessou (item 1), se ela cai com a distancia (item 2) e se
/// a parede come o agudo e deixa o grave (item 4).
///
/// **E 320 AMOSTRAS SAO 6 CICLOS EXATOS DE 300 Hz E 60 DE 3 kHz**: o quadro fecha em ciclo inteiro nas
/// duas. Sem isso haveria um degrau de fase na emenda de cada quadro -- o codificador gastaria bytes
/// nele e a correlacao entre o que entrou e o que saiu mediria o degrau em vez de medir a compressao.
/// ================================================================================================
///
/// ============================ ONDE CADA NUMERO E MEDIDO, E POR QUE ALI ============================
///   | pergunta                        | onde e lida                        | por que ali             |
///   |---------------------------------|------------------------------------|-------------------------|
///   | chegou byte? quantos?           | `GameClient.VozRecebida`           | e o pacote, cru         |
///   | o que a pessoa vai OUVIR        | `VozOuvida.Espiao` (pos-filtro)    | depois da abafada       |
///   | o volume que o MOTOR aplicou    | `AudioEffectCapture` no bus `Voz`  | depois da atenuacao 2D  |
///   | o que a autoridade ENTREGOU     | o log do `--vozviva` no servidor   | so ele sabe             |
///
/// As duas primeiras nao se substituem: o pacote diz *quanto trafego*, o PCM diz *como soa*. Uma voz
/// que chegasse e fosse decodificada como silencio passaria na primeira e reprovaria na segunda.
/// ============================================================================================
/// </summary>
public partial class RoboDeVozViva : Node
{
	/// <summary>`a` = quem fala e e medido; `b` = quem ouve e mede; `c`/`d` = falantes da fase 7.</summary>
	public string Papel = "";

	private bool Falante => Papel is "a" or "c" or "d";

	// =====================================================================
	// A ONDA
	// =====================================================================
	public const float HzGrave = 300f;
	public const float HzAgudo = 3000f;

	/// <summary>
	/// 0,35 em cada tom -- pico somado de 0,70, bem acima do <see cref="Settings.LimiarDeVoz"/> (0,02) e
	/// longe do teto. Encostar em 1,0 faria o somatorio saturar e a saturacao geraria harmonicos, que a
	/// bancada leria como "ruido da compressao" sendo culpa dela mesma.
	/// </summary>
	public const float Amplitude = 0.35f;

	/// <summary>A onda conhecida, sempre igual (ver o cabecalho: o quadro fecha em ciclo inteiro).</summary>
	public static void OndaConhecida(short[] q, int taxa = VozLocal.TaxaDeAmostragem)
	{
		for (int i = 0; i < q.Length; i++)
		{
			double v = Amplitude * Math.Sin(2 * Math.PI * HzGrave * i / taxa)
					 + Amplitude * Math.Sin(2 * Math.PI * HzAgudo * i / taxa);
			q[i] = (short)Math.Clamp((int)(v * short.MaxValue), short.MinValue, short.MaxValue);
		}
	}

	/// <summary>
	/// QUANTA ENERGIA HA NESTA FREQUENCIA -- uma raia de Goertzel, a mesma da `--diagvoz`.
	///
	/// E o instrumento que transforma "soa abafado" numa comparacao de dois numeros.
	///
	/// PUBLICA PORQUE A `--vozdupla` MEDE A MESMA COISA: duas bancadas com duas copias desta funcao
	/// poderiam divergir num decimal e discordar sobre a mesma parede. A onda (<see cref="OndaConhecida"/>)
	/// ja era compartilhada pelo mesmo motivo -- a regua e uma so.
	/// </summary>
	public static double Energia(short[] pcm, int n, float hz, int taxa = VozLocal.TaxaDeAmostragem)
	{
		double re = 0, im = 0;
		for (int i = 0; i < n; i++)
		{
			double ang = 2 * Math.PI * hz * i / taxa;
			double v = pcm[i] / (double)short.MaxValue;
			re += v * Math.Cos(ang);
			im += v * Math.Sin(ang);
		}
		return Math.Sqrt(re * re + im * im) / n;
	}

	// =====================================================================
	// O QUE CADA FASE ACUMULA
	// =====================================================================
	private sealed class Balde
	{
		public string Nome = "";
		public int Numero;
		public float DistPedida;
		public bool ParedePedida;
		public ulong ComecouMs, FechouMs;

		// --- o pacote, cru ---
		/// <summary>
		/// O QUE CHEGOU NA VIRADA, contado a parte -- e nao jogado fora nem somado ao resto.
		///
		/// ============================ POR QUE OS PRIMEIROS 250 ms NAO CONTAM ============================
		/// A fase muda porque o SERVIDOR teleportou os corpos. Os quadros que ja estavam voando quando
		/// isso aconteceu chegam depois -- carimbados com a distancia e a parede da fase ANTERIOR. Na
		/// primeira rodada isso apareceu como *"1 pacote na fase FORA"* e *"1 quadro marcado com parede
		/// na fase ABERTO"*, e os dois numeros diziam a mesma coisa: nao sao vazamento, sao o cano.
		///
		/// Somar seria estragar as duas medidas mais importantes da bancada (o zero do item 3 e o
		/// controle do item 4). **Apagar seria pior**: um retardatario que virasse dez seria um defeito
		/// de verdade -- a fase mudando e o corte demorando a acompanhar -- e ninguem veria. Entao ele
		/// sai numa coluna propria, e o placar diz o que ele e.
		/// ============================================================================================
		/// </summary>
		public int PacotesNaVirada;
		public long BytesNaVirada;

		public int Pacotes;
		public long BytesDeAudio;
		public readonly HashSet<int> Falantes = [];
		public int DistMin = 999, DistMax = -1;
		public long SomaDist;
		public int MarcadosComParede, MarcadosSemParede;

		// --- o PCM que vai pro alto-falante ---
		public int Quadros;
		public double SomaRms, SomaEGrave, SomaEAgudo, SomaEnergiaTotal;
		public double SomaCorrelacao;
		public int Correlacoes;

		// --- o que o MOTOR mixou no barramento da voz ---
		public double PicoDoBarramento;
		public double SomaQuadradoDoBarramento;
		public long AmostrasDoBarramento;

		// --- o que EU mandei (so no papel `a`) ---
		public int QuadrosMandados;
		public long BytesMandados;

		public double Segundos => (FechouMs - ComecouMs) / 1000.0;
	}

	private readonly List<Balde> _baldes = [];
	private Balde? _agora;

	// A REFERENCIA E COMPARADA COM O QUE SAI DO FILTRO -- PCM de SAIDA (960 a 48 kHz).
	private readonly short[] _referencia = new short[VozLocal.AmostrasPorQuadroDeSaida];

	/// <summary>O barramento da voz, escutado. Nulo quando o motor nao mixa (ver o placar).</summary>
	private AudioEffectCapture? _escutaDoBarramento;
	private int _busDaVoz = -1;

	// =====================================================================
	// LIGAR
	// =====================================================================
	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		OndaConhecida(_referencia, VozLocal.TaxaDeSaida);

		GD.Print($"[vozviva:{Papel}] pronto. build de audio: mix={AudioServer.GetMixRate():0} Hz, "
			   + $"latencia de saida={AudioServer.GetOutputLatency():0.000000} s, "
			   + $"entrada={ProjectSettings.GetSetting("audio/driver/enable_input")}");

		if (GameClient.Instance is { } cli) cli.Falou += AoOuvirOAnuncio;

		if (Falante) LigarAVoz();
		else LigarAEscuta();
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli)
		{
			cli.Falou -= AoOuvirOAnuncio;
			cli.VozRecebida -= AoChegarPacote;
		}
		// METODO NOMEADO E `-=`, e nao lambda: os dois espioes sao ESTATICOS e sobreviveriam a este no.
		// E a licao das assinaturas vazadas, e aqui ela seria pior que um orfao -- um espiao vivo
		// depois da bancada continuaria contando quadros de uma partida de verdade.
		Microfone.FonteDeTeste = null;
		Microfone.Espiao -= AoMandarQuadro;
		VozOuvida.Espiao -= AoSairDoFiltro;
		// AS TRES ESCUTAS SAIEM DOS BARRAMENTOS. Elas foram acrescentadas no fim de cada um, entao o
		// indice a remover e o ultimo -- e nenhuma outra coisa deste jogo acrescenta efeito em runtime.
		foreach ((AudioEffectCapture? cap, int bus) in
				 new[] { (_escutaDoBarramento, _busDaVoz), (_escutaDaMusica, _busDaMusica),
						 (_escutaDosEfeitos, _busDosEfeitos) })
			if (cap != null && bus >= 0 && AudioServer.GetBusEffectCount(bus) > 0)
				AudioServer.RemoveBusEffect(bus, AudioServer.GetBusEffectCount(bus) - 1);
	}

	/// <summary>
	/// O FALANTE: liga a opcao, APERTA A TECLA e poe a onda no lugar da captura.
	///
	/// ============================ ELE APERTA O V DE VERDADE ============================
	/// `Input.ActionPress("falar_voz")` e nao um booleano proprio: o portao do
	/// <see cref="Microfone.QuerFalar"/> e `IsActionPressed`, e uma bancada que o contornasse deixaria
	/// de exercitar justamente o aperta-pra-falar que o dono pediu. Se a tecla sair do registro, esta
	/// bancada emudece -- que e o comportamento certo.
	///
	/// E A OPCAO SO MUDA NA MEMORIA: `Settings.Salvar` nunca e chamado aqui. O `config.json` da maquina
	/// de quem roda o teste nao sai com o microfone ligado.
	/// </summary>
	private void LigarAVoz()
	{
		Boot.Config.VozLigada = true;
		Microfone.Espiao += AoMandarQuadro;
		Microfone.FonteDeTeste = Encher;
		Godot.Input.ActionPress("falar_voz");
		GD.Print($"[vozviva:{Papel}] falando: {HzGrave:0} Hz + {HzAgudo:0} Hz, "
			   + $"{VozLocal.QuadrosPorSegundo} quadros/s, tecla 'falar_voz' apertada");
	}

	private void LigarAEscuta()
	{
		// A VOZ FICA DESLIGADA NO OUVINTE, de proposito: ele nao pode ter microfone nenhum aberto, e
		// isso tambem prova de graca que ouvir nao depende de falar.
		Boot.Config.VozLigada = false;
		VozOuvida.Espiao += AoSairDoFiltro;
		if (GameClient.Instance is { } cli) cli.VozRecebida += AoChegarPacote;

		// ============================ ESCUTAR O BARRAMENTO E A UNICA FORMA DE VER A ATENUACAO ============================
		// O `VozOuvida.Espiao` entrega o PCM depois da abafada -- mas ANTES do volume que o
		// `AudioStreamPlayer2D` aplica pela distancia, que e do motor e nao do nosso codigo. Sem esta
		// escuta, "a amplitude cai com a distancia" so poderia ser afirmada relendo a nossa constante de
		// atenuacao, que e exatamente o cego que este projeto pagou caro ("uniform escrito nao e pixel
		// desenhado"). Aqui o que se mede e o que o motor MIXOU.
		//
		// PODE NAO VIR NADA: sem placa de som (`--headless`), o driver e o Dummy. Se vier zero amostra,
		// o placar diz que este numero NAO FOI MEDIDO -- em vez de imprimir um zero que parece medida.
		// ==========================================================================================================
		_busDaVoz = AudioServer.GetBusIndex(AudioDirector.BusVoz);
		if (_busDaVoz >= 0)
		{
			_escutaDoBarramento = new AudioEffectCapture { BufferLength = 0.5f };
			AudioServer.AddBusEffect(_busDaVoz, _escutaDoBarramento);
		}
		// E OS OUTROS DOIS BARRAMENTOS, pro item 5 (ver `MedirOJogoSoando`).
		_busDaMusica = AudioServer.GetBusIndex(AudioDirector.BusMusica);
		_busDosEfeitos = AudioServer.GetBusIndex(AudioDirector.BusEfeitos);
		if (_busDaMusica >= 0)
		{
			_escutaDaMusica = new AudioEffectCapture { BufferLength = 0.5f };
			AudioServer.AddBusEffect(_busDaMusica, _escutaDaMusica);
		}
		if (_busDosEfeitos >= 0)
		{
			_escutaDosEfeitos = new AudioEffectCapture { BufferLength = 0.5f };
			AudioServer.AddBusEffect(_busDosEfeitos, _escutaDosEfeitos);
		}
		// UMA FAIXA DE VERDADE, pedida pelo funil de producao.
		//
		// **E `Trilha.Menu()` E NAO `Trilha.MusicaDe(zona)`**: a primeira versao desta linha pediu a
		// musica da Terra, e `MusicaDe("Earth")` devolve `""` -- silencio, de propósito (na Terra quem
		// fala e o AMBIENTE). A bancada mediu 0,0000 de rms nas duas execucoes e teria concluido que a
		// musica "esta igual" comparando silencio com silencio. Uma faixa que existe e o unico jeito de
		// a comparacao dizer alguma coisa.
		AudioDirector.Instance?.Musica(Trilha.Menu(), AudioDirector.Camada.Lugar,
									   "bancada da voz: medir se o jogo continua soando");

		GD.Print($"[vozviva:{Papel}] escutando. bus 'Voz'={_busDaVoz}, 'Musica'={_busDaMusica}, "
			   + $"'Efeitos'={_busDosEfeitos}, volume da voz={Boot.Config.VolumeVoz:0.00}");
	}

	// =====================================================================
	// A CADENCIA DA INJECAO
	// =====================================================================
	private ulong _proximoQuadroMs;

	/// <summary>
	/// ENCHE UM QUADRO QUANDO CHEGAR A HORA -- e devolve falso quando ainda nao chegou.
	///
	/// O RITMO E DAQUI E NAO DO MICROFONE (ver <see cref="Microfone.FonteDeTeste"/>): 50 quadros por
	/// segundo, contados em relogio de parede e nao em quadros de tela. Amarrar um quadro de audio a um
	/// quadro de tela faria a taxa da voz seguir o FPS -- e a banda medida seria a da maquina de quem
	/// rodou a bancada, nao a do sistema.
	/// </summary>
	private bool Encher(short[] q)
	{
		ulong agora = Time.GetTicksMsec();
		if (_proximoQuadroMs == 0) _proximoQuadroMs = agora;

		// UM ENGASGO LONGO NAO VIRA RAJADA. Esta fonte e um RELOGIO, e nao uma captura: sem esta linha,
		// um cliente que travasse meio segundo devolveria 25 quadros de uma vez -- e o anel do
		// `Microfone` (`QuadrosProntos`, o mesmo remendo em producao) derrubaria 20 deles, deixando a
		// bancada medindo o descarte de engasgo em vez da conversa.
		if (agora > _proximoQuadroMs + 200) _proximoQuadroMs = agora;

		if (agora < _proximoQuadroMs) return false;
		_proximoQuadroMs += VozLocal.MsPorQuadro;
		OndaConhecida(q);
		return true;
	}

	// =====================================================================
	// AS TRES ESCUTAS
	// =====================================================================
	private void AoMandarQuadro(int bytes, int amostras)
	{
		if (_agora == null) return;
		_agora.QuadrosMandados++;
		_agora.BytesMandados += bytes;
	}

	/// <summary>Quanto tempo, no comeco de cada fase, o que chega ainda e da fase anterior. Ver <see cref="Balde.PacotesNaVirada"/>.</summary>
	private const ulong MsDeVirada = 250;

	private bool NaVirada => _agora != null && Time.GetTicksMsec() - _agora.ComecouMs < MsDeVirada;

	private void AoChegarPacote(int quem, ushort seq, byte dist, bool parede, byte[] dados, int n)
	{
		if (_agora == null) return;
		if (NaVirada) { _agora.PacotesNaVirada++; _agora.BytesNaVirada += n; return; }
		_agora.Pacotes++;
		_agora.BytesDeAudio += n;
		_agora.Falantes.Add(quem);
		_agora.SomaDist += dist;
		_agora.DistMin = Math.Min(_agora.DistMin, dist);
		_agora.DistMax = Math.Max(_agora.DistMax, dist);
		if (parede) _agora.MarcadosComParede++; else _agora.MarcadosSemParede++;
	}

	/// <summary>
	/// O QUADRO QUE VAI PRO ALTO-FALANTE. Aqui e onde o item 1 e o item 4 sao respondidos.
	/// </summary>
	private void AoSairDoFiltro(int quem, short[] pcm, int n, byte dist, bool parede)
	{
		if (_agora == null || n <= 0 || NaVirada) return;

		_agora.Quadros++;

		double soma = 0;
		for (int i = 0; i < n; i++)
		{
			double v = pcm[i] / (double)short.MaxValue;
			soma += v * v;
		}
		_agora.SomaRms += Math.Sqrt(soma / n);
		_agora.SomaEnergiaTotal += soma / n;
		_agora.SomaEGrave += Energia(pcm, n, HzGrave, VozLocal.TaxaDeSaida);   // o que sai do filtro e de SAIDA
		_agora.SomaEAgudo += Energia(pcm, n, HzAgudo, VozLocal.TaxaDeSaida);

		// ============================ A CORRELACAO SO EM ALGUNS QUADROS ============================
		// Ela e O(n^2) -- 102 mil operacoes por quadro. Em 150 quadros por fase isso e 15 milhoes de
		// multiplicacoes dentro do `_Process` do cliente, e um cliente que engasga perde pacote de voz:
		// a bancada estragaria a propria medida. Oito quadros por fase ja dao uma media estavel, porque
		// a onda e a mesma em todos.
		// ======================================================================================
		if (_agora.Correlacoes < 8 && _agora.Quadros % 9 == 0)
		{
			_agora.SomaCorrelacao += CorrelacaoCircular(pcm, n);
			_agora.Correlacoes++;
		}
	}

	/// <summary>
	/// O QUE ENTROU CONTRA O QUE SAIU: correlacao normalizada, no melhor alinhamento.
	///
	/// ============================ POR QUE CIRCULAR, E POR QUE PROCURA O ATRASO ============================
	/// O Opus tem atraso algoritmico e nao devolve a amostra 0 na amostra 0 -- comparar quadro com quadro
	/// no lugar daria uma correlacao baixa que nao quer dizer nada sobre fidelidade. Procurar o melhor
	/// deslocamento resolve isso.
	///
	/// E CIRCULAR e valido **so porque a onda fecha em ciclo inteiro no quadro** (ver o cabecalho): o
	/// sinal e periodico com periodo que divide 320, entao deslizar dando a volta e igual a deslizar num
	/// fluxo infinito. Num sinal qualquer isso seria uma armadilha.
	///
	/// 1,00 = identico a menos de ganho; 0 = nao ha nada do original ali.
	/// </summary>
	private double CorrelacaoCircular(short[] pcm, int n)
	{
		double normR = 0, normP = 0;
		for (int i = 0; i < n; i++)
		{
			double r = _referencia[i] / (double)short.MaxValue;
			double p = pcm[i] / (double)short.MaxValue;
			normR += r * r;
			normP += p * p;
		}
		if (normR <= 0 || normP <= 0) return 0;
		double den = Math.Sqrt(normR * normP);

		double melhor = 0;
		for (int lag = 0; lag < n; lag++)
		{
			double s = 0;
			for (int i = 0; i < n; i++)
				s += (pcm[i] / (double)short.MaxValue) * (_referencia[(i + lag) % n] / (double)short.MaxValue);
			if (s > melhor) melhor = s;
		}
		return melhor / den;
	}

	// =====================================================================
	// O BARRAMENTO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_escutaDoBarramento == null || _agora == null) return;

		int ha = _escutaDoBarramento.GetFramesAvailable();
		if (ha > 0)
		{
			Vector2[] mix = _escutaDoBarramento.GetBuffer(ha);

			// NA VIRADA, TIRA E JOGA FORA. O `AudioStreamGenerator` guarda 0,25 s de fila (ver
			// `VozOuvida.Nascer`), entao o que o motor esta mixando AGORA e o que chegou ate um quarto
			// de segundo atras -- ou seja, a fase anterior. Deixar no buffer seria pior do que
			// descartar: entraria inteiro no balde seguinte, num lote so.
			if (!NaVirada)
			{
				foreach (Vector2 s in mix)
				{
					double v = (Math.Abs(s.X) + Math.Abs(s.Y)) * 0.5;
					_agora.PicoDoBarramento = Math.Max(_agora.PicoDoBarramento, v);
					_agora.SomaQuadradoDoBarramento += v * v;
				}
				_agora.AmostrasDoBarramento += mix.Length;
			}
		}

		MedirOJogoSoando(delta);
	}

	// =====================================================================
	// ITEM 5: O JOGO CONTINUA SOANDO?
	// =====================================================================
	/// <summary>
	/// A MUSICA E OS EFEITOS DO JOGO, MEDIDOS NO BARRAMENTO -- e nao "conferidos".
	///
	/// ============================ POR QUE ISTO PRECISA DE DUAS EXECUCOES ============================
	/// A pergunta e *"ligar a entrada de audio mudou o som que ja existia?"*, e ela nao tem resposta
	/// dentro de UMA execucao: `audio/driver/enable_input` e lido na INICIALIZACAO do driver. O que esta
	/// bancada faz e produzir, em qualquer das duas, os mesmos numeros -- taxa de mistura, latencia de
	/// saida, e quanta energia a musica e os efeitos DE VERDADE puseram nos barramentos deles. Rodar a
	/// bancada com a chave ligada e depois com ela desligada e comparar as duas tabelas e o teste.
	///
	/// E A MUSICA E O EFEITO SAO OS DO JOGO, pedidos pelo `AudioDirector` de producao. Um seno tocado
	/// por um player proprio mediria o motor, e nao a trilha -- e o que pode quebrar com o driver e o
	/// caminho inteiro (arquivo, decodificador, barramento), nao a soma de amostras.
	/// ============================================================================================
	/// </summary>
	private void MedirOJogoSoando(double delta)
	{
		_ateOProximoSoco -= delta;
		if (_ateOProximoSoco <= 0)
		{
			_ateOProximoSoco = 0.4;
			AudioDirector.Instance?.Efeito(Trilha.SocoNoAr());
			_socosPedidos++;
		}

		Somar(_escutaDaMusica, ref _musicaPico, ref _musicaSoma, ref _musicaAmostras);
		Somar(_escutaDosEfeitos, ref _efeitosPico, ref _efeitosSoma, ref _efeitosAmostras);
	}

	private static void Somar(AudioEffectCapture? cap, ref double pico, ref double soma, ref long n)
	{
		if (cap == null) return;
		int ha = cap.GetFramesAvailable();
		if (ha <= 0) return;
		foreach (Vector2 s in cap.GetBuffer(ha))
		{
			double v = (Math.Abs(s.X) + Math.Abs(s.Y)) * 0.5;
			if (v > pico) pico = v;
			soma += v * v;
		}
		n += ha;
	}

	private AudioEffectCapture? _escutaDaMusica, _escutaDosEfeitos;
	private int _busDaMusica = -1, _busDosEfeitos = -1;
	private double _ateOProximoSoco;
	private int _socosPedidos;
	private double _musicaPico, _musicaSoma, _efeitosPico, _efeitosSoma;
	private long _musicaAmostras, _efeitosAmostras;

	// =====================================================================
	// AS FASES
	// =====================================================================
	/// <summary>
	/// O ANUNCIO DE FASE, PELO CANAL DE TEXTO DO SERVIDOR. Ver `GameServer.VozVivaTeste.cs`.
	///
	/// Ele e a unica sincronia entre tres processos, e ela precisa ser do SERVIDOR: se cada cliente
	/// contasse o proprio relogio, o `b` estaria medindo a fase seguinte enquanto o servidor ainda nao
	/// tinha movido ninguem -- e a diferenca apareceria como "a amplitude nao caiu".
	/// </summary>
	private void AoOuvirOAnuncio(Protocol.Fala canal, string autor, string texto)
	{
		if (canal != Protocol.Fala.Sistema || !texto.StartsWith("[vozviva] fase", StringComparison.Ordinal))
			return;

		Fechar();

		if (texto.Contains("nome=fim", StringComparison.Ordinal)) { Placar(); return; }

		var b = new Balde { ComecouMs = Time.GetTicksMsec() };
		foreach (string parte in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			string[] kv = parte.Split('=', 2);
			if (kv.Length != 2) continue;
			switch (kv[0])
			{
				case "fase": int.TryParse(kv[1], out b.Numero); break;
				case "nome": b.Nome = kv[1]; break;
				case "d": float.TryParse(kv[1], System.Globalization.NumberStyles.Float,
										 System.Globalization.CultureInfo.InvariantCulture,
										 out b.DistPedida); break;
				case "parede": b.ParedePedida = kv[1] == "1"; break;
			}
		}
		_baldes.Add(b);
		_agora = b;
		GD.Print($"[vozviva:{Papel}] --> fase {b.Numero} {b.Nome} (d={b.DistPedida:0} px, parede={b.ParedePedida})");
	}

	private void Fechar()
	{
		if (_agora == null) return;
		_agora.FechouMs = Time.GetTicksMsec();
		_agora = null;
	}

	// =====================================================================
	// O PLACAR
	// =====================================================================
	private void Placar()
	{
		GD.Print(Falante
			? $"[vozviva:{Papel}] ============ O QUE SAIU DAQUI ============"
			: $"[vozviva:{Papel}] ============ O QUE CHEGOU NO OUVIDO ============");

		if (Falante)
		{
			// O LADO DE CA DA CONTA. Ele existe pra ser comparado com a tabela do `b`: o que saiu
			// daqui, o que a autoridade entregou e o que chegou la sao tres numeros diferentes, e so
			// os tres juntos separam "o corte funcionou" de "o pacote se perdeu".
			GD.Print("[vozviva:a] fase        quadros   B audio   B/quadro   KB/s (audio+10B)");
			foreach (Balde b in _baldes)
			{
				double bs = (b.BytesMandados + b.QuadrosMandados * 10) / Math.Max(b.Segundos, 0.001);
				GD.Print($"[vozviva:a] {b.Nome,-11} {b.QuadrosMandados,7} {b.BytesMandados,9} "
					   + $"{(b.QuadrosMandados > 0 ? b.BytesMandados / (double)b.QuadrosMandados : 0),10:0.0} "
					   + $"{bs / 1024,10:0.00}");
			}
			GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(0);
			return;
		}

		// ---- 1. o trafego ----
		GD.Print("[vozviva:b] --- QUANTO CHEGOU (o pacote, cru) ---");
		GD.Print("[vozviva:b] fase        pedido  falantes  pacotes   B audio  B/pct  KB/s fio  dist         virada");
		foreach (Balde b in _baldes)
		{
			// O FIO: 10 B de cabecalho do `S2C.Voz` + 28 B de UDP/IP por pacote, a mesma conta do
			// `VozLocal.MaxFalantesPorOuvinte`. Sem ela o numero pareceria 4x menor do que e.
			double medidos = Math.Max(b.Segundos - MsDeVirada / 1000.0, 0.001);
			double fio = (b.BytesDeAudio + b.Pacotes * (10 + 28)) / medidos;
			string dist = b.Pacotes > 0 ? $"{b.SomaDist / (double)b.Pacotes:0}({b.DistMin}-{b.DistMax})" : "-";
			GD.Print($"[vozviva:b] {b.Nome,-11} {b.DistPedida,6:0}  {b.Falantes.Count,8} {b.Pacotes,8} "
				   + $"{b.BytesDeAudio,9} {(b.Pacotes > 0 ? b.BytesDeAudio / (double)b.Pacotes : 0),6:0.0} "
				   + $"{fio / 1024,8:0.00}  {dist,-12} {b.PacotesNaVirada}p/{b.BytesNaVirada}B");
		}

		// ---- 2. o som ----
		GD.Print("[vozviva:b] --- COMO SOA (o PCM que vai pro alto-falante) ---");
		GD.Print("[vozviva:b] fase        quadros    rms    E(300)   E(3k)  agudo/grave  correl  marcado");
		foreach (Balde b in _baldes)
		{
			double q = Math.Max(b.Quadros, 1);
			double eg = b.SomaEGrave / q, ea = b.SomaEAgudo / q;
			string marca = b.MarcadosComParede > 0 && b.MarcadosSemParede > 0
				? $"MISTO {b.MarcadosComParede}/{b.MarcadosSemParede}"
				: b.MarcadosComParede > 0 ? "parede" : b.Pacotes > 0 ? "limpo" : "-";
			GD.Print($"[vozviva:b] {b.Nome,-11} {b.Quadros,7} {b.SomaRms / q,6:0.0000} "
				   + $"{eg,8:0.0000} {ea,7:0.0000} {(eg > 0 ? ea / eg : 0),12:0.000} "
				   + $"{(b.Correlacoes > 0 ? b.SomaCorrelacao / b.Correlacoes : 0),7:0.000}  {marca}");
		}

		// ---- 3. o motor ----
		bool mixou = _baldes.Any(b => b.AmostrasDoBarramento > 0);
		if (mixou)
		{
			GD.Print("[vozviva:b] --- O QUE O MOTOR MIXOU (bus 'Voz': ja com a atenuacao 2D) ---");
			GD.Print("[vozviva:b] fase        amostras     pico      rms");
			foreach (Balde b in _baldes)
				GD.Print($"[vozviva:b] {b.Nome,-11} {b.AmostrasDoBarramento,9} {b.PicoDoBarramento,8:0.0000} "
					   + $"{(b.AmostrasDoBarramento > 0 ? Math.Sqrt(b.SomaQuadradoDoBarramento / b.AmostrasDoBarramento) : 0),8:0.0000}");
		}
		else
		{
			// ============================ O QUE NAO FOI MEDIDO SAI DITO, E NAO SAI ZERO ============================
			// Um zero na coluna do barramento seria lido como "o motor nao tocou nada" -- que e uma
			// afirmacao sobre o sistema. A verdade e outra: nao ha placa de som neste processo.
			// ==================================================================================================
			GD.Print("[vozviva:b] --- O MOTOR NAO MIXOU NADA: driver de audio Dummy (--headless). ---");
			GD.Print("[vozviva:b]     A atenuacao 2D (o volume pela distancia) NAO FOI MEDIDA aqui.");
			GD.Print("[vozviva:b]     O que ESTA medido e a distancia que o servidor mandou, na tabela 1.");
		}

		// ---- 4. o jogo continua soando (item 5) ----
		GD.Print("[vozviva:b] --- O JOGO CONTINUA SOANDO (compare esta tabela com a execucao de entrada DESLIGADA) ---");
		GD.Print($"[vozviva:b] entrada de audio ligada = {ProjectSettings.GetSetting("audio/driver/enable_input")}");
		GD.Print($"[vozviva:b] mix={AudioServer.GetMixRate():0} Hz | "
			   + $"latencia de saida={AudioServer.GetOutputLatency():0.000000} s | "
			   + $"barramentos={AudioServer.BusCount}");
		GD.Print($"[vozviva:b] musica : {_musicaAmostras} amostras, pico {_musicaPico:0.0000}, "
			   + $"rms {(_musicaAmostras > 0 ? Math.Sqrt(_musicaSoma / _musicaAmostras) : 0):0.0000}");
		GD.Print($"[vozviva:b] efeitos: {_efeitosAmostras} amostras, pico {_efeitosPico:0.0000}, "
			   + $"rms {(_efeitosAmostras > 0 ? Math.Sqrt(_efeitosSoma / _efeitosAmostras) : 0):0.0000} "
			   + $"({_socosPedidos} sons pedidos)");

		GD.Print($"[vozviva:b] ============ fim ({_baldes.Count} fases) ============");
		GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(0);
	}
}
