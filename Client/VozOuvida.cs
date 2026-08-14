using Concentus;
using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ A VOZ DOS OUTROS: DESCOMPRIMIR, ABAFAR, TOCAR NO LUGAR ============================
/// Chega um quadro de 20 ms de alguem que o SERVIDOR ja decidiu que eu posso ouvir (ver
/// `GameServer.Voz.cs` -- quem esta longe nao chega aqui, nao chega baixinho). O que sobra pra este
/// arquivo e a segunda metade da frase do dono: *"com EFEITO DE DISTANCIA caso se afaste, e tb se
/// tiver ATRAS DE PAREDE da uma ABAFADA"*.
///
///   | o que          | como                                                       |
///   |----------------|------------------------------------------------------------|
///   | posicao        | um `AudioStreamPlayer2D` posto EM CIMA do corpo de quem fala|
///   | distancia      | a atenuacao do proprio motor (nao linear -- ver abaixo)     |
///   | parede         | filtro passa-BAIXA no PCM, com rampa (<see cref="FiltroDeParede"/>)|
///   | nao brigar     | barramento `Voz`, proprio, com controle proprio            |
///
/// ============================ A ABAFADA E FILTRO NO SINAL, E NAO EFEITO DE BARRAMENTO ============================
/// A saida obvia no Godot seria um `AudioEffectLowPassFilter` -- so que efeito e do BARRAMENTO, e um
/// barramento e global. Com quatro vozes simultaneas eu precisaria de quatro barramentos, criados e
/// destruidos conforme as pessoas falam, e o dia em que sobrasse um a voz de alguem sairia abafada sem
/// parede nenhuma.
///
/// O PCM ja esta na minha mao depois de descomprimir: um passa-baixa de um polo (duas linhas) e POR
/// FONTE por construcao, custa quase nada e ainda me da a RAMPA de graca -- que e o que impede a
/// abafada de piscar quando alguem anda no vao de uma porta.
/// ============================================================================================================
///
/// ============================ POR QUE A QUEDA DE VOLUME NAO E LINEAR ============================
/// Som real cai com o inverso da distancia e o ouvido le volume em escala logaritmica: uma queda
/// linear soa como se nada acontecesse ate a metade do caminho e depois sumisse de uma vez. Quem faz a
/// curva aqui e o `Attenuation` do `AudioStreamPlayer2D` -- o MESMO que os efeitos do jogo ja usam
/// (`AudioDirector.EfeitoNoLugar`), pra a voz e o soco cairem com a mesma fisica.
/// ==========================================================================================
/// </summary>
/// <remarks>
/// `Node2D` E NAO `Node`, e isso nao e cosmetico: o `AudioStreamPlayer2D` e um `CanvasItem`, e o
/// `CanvasItem` procura o ANCESTRAL de canvas mais proximo pra montar a transformacao global. Pendurado
/// num `Node` puro ele nao acha nenhum e passa a viver no espaco do canvas raiz -- hoje daria no mesmo
/// (o `World` esta na origem), e no dia em que o mundo se deslocar por qualquer motivo as vozes ficariam
/// paradas onde o mundo estava. Um defeito que so aparece muito depois da linha que o causou.
/// </remarks>
public partial class VozOuvida : Node2D
{
	public static VozOuvida? Instancia { get; private set; }

	/// <summary>
	/// O ALCANCE DA VOZ NO DESENHO -- os mesmos 22 tiles que o servidor usa pra cortar.
	///
	/// Ele **nao decide quem ouve** (isso ja aconteceu, no servidor): ele e so o denominador da curva
	/// de atenuacao. Mas tem que ser o MESMO numero, senao a voz chegaria ainda audivel e cortaria de
	/// uma vez no limite -- ou sumiria antes de o servidor parar de mandar, e o jogador ouviria a
	/// propria conversa "falhando" a meio caminho.
	/// </summary>
	private const float Alcance = 22 * ZoneCollision.TileSize;

	/// <summary>
	/// A CURVA DA QUEDA. 1,5, o mesmo do `AudioDirector.EfeitoNoLugar` -- voz e soco caem igual porque
	/// e o mesmo ar entre as duas pessoas.
	/// </summary>
	private const float Curva = 1.5f;

	/// <summary>
	/// EM QUANTO TEMPO A ABAFADA ENTRA E SAI, em segundos.
	///
	/// 0,15 s. Ela NAO pode ser instantanea: o servidor responde "ha parede?" com validade de 100 ms
	/// (ver `MsDeValidadeDaParede`), e alguem parado exatamente no vao de uma porta faria a resposta
	/// oscilar. Sem rampa isso vira um chiado ligando e desligando; com 0,15 s vira o que soa como e --
	/// alguem passando por uma porta.
	///
	/// E ela tambem nao pode ser lenta: 1 s deixaria a voz abafada por meio corredor depois de a pessoa
	/// aparecer na sua frente.
	/// </summary>
	private const float SegundosDeRampa = 0.15f;

	/// <summary>
	/// O CORTE DA PAREDE, em Hz.
	///
	/// Parede baixa agudo e deixa grave passar -- e por isso que se ouve o baixo do vizinho e nao a
	/// letra. 700 Hz mata a inteligibilidade das consoantes (que vivem de 2 a 6 kHz) e mantem a
	/// entonacao: da pra saber que alguem esta falando ali, e nao da pra ler o que foi dito. Que e
	/// exatamente o que uma parede faz.
	/// </summary>
	private const float CorteDaParede = 700f;

	/// <summary>
	/// QUANTO A PAREDE ABAIXA, alem de cortar o agudo. 0,55 -- uma parede tambem atenua, e so filtrar
	/// deixaria a voz abafada com o mesmo volume da voz limpa, que soa como um defeito de equalizador
	/// e nao como um muro.
	/// </summary>
	private const float GanhoAtrasDaParede = 0.55f;

	/// <summary>
	/// DEPOIS DE QUANTO SILENCIO A FONTE E DESMONTADA, em ms.
	///
	/// 1,5 s -- seis vezes o <see cref="VozLocal.MsAteCalar"/>. Nao e o mesmo prazo de proposito: o
	/// sinal sobre a cabeca tem que apagar rapido (250 ms) e o TOCADOR tem que sobreviver as pausas
	/// entre as frases. Desmontar junto com o sinal recriaria o gerador a cada respirada de quem fala,
	/// e cada recriacao e um estalo.
	/// </summary>
	private const ulong MsAteDesmontar = 1500;

	/// <summary>Uma pessoa que esta falando comigo -- o decodificador dela, o tocador dela, o filtro dela.</summary>
	private sealed class Fonte
	{
		public required IOpusDecoder Decodificador;
		public required AudioStreamPlayer2D Tocador;
		public AudioStreamGeneratorPlayback? Fila;

		/// <summary>A ultima sequencia recebida. Buraco aqui = quadro perdido no caminho.</summary>
		public ushort Seq;
		public bool TemSeq;

		/// <summary>A abafada desta voz: a rampa, o filtro e a memoria dele. Ver <see cref="FiltroDeParede"/>.</summary>
		public readonly FiltroDeParede Filtro = new();

		public ulong UltimoMs;
	}

	/// <summary>
	/// ============================ A ABAFADA, EM UM OBJETO ============================
	/// A rampa, o passa-baixa e a memoria dele -- tudo o que "estar atras de uma parede" significa pro
	/// SINAL, e nada mais.
	///
	/// ============================ POR QUE ELE E UMA CLASSE E NAO TRES CAMPOS SOLTOS ============================
	/// Porque so assim a bancada consegue medir o que sai, e nao o que eu escrevi. Com o filtro
	/// dissolvido dentro do `VozOuvida`, a unica coisa testavel seria o coeficiente -- ou seja, eu
	/// conferindo a minha propria constante, que e exatamente o cego que este projeto ja pagou caro
	/// ("uniform escrito nao e pixel desenhado"). Aqui a bancada joga um sinal de duas frequencias e
	/// MEDE quanta energia sobrou em cada uma.
	///
	/// E ele nao sabe o que e voz, rede ou Godot: recebe um buffer de PCM e o modifica.
	/// ======================================================================================================
	/// </summary>
	public sealed class FiltroDeParede
	{
		/// <summary>Quanto da abafada esta aplicada AGORA (0 a 1). E o estado da rampa.</summary>
		public float Abafada { get; private set; }

		/// <summary>Ha parede agora, segundo o servidor. E o ALVO da rampa.</summary>
		public bool Parede { get; set; }

		/// <summary>A memoria dos dois polos.</summary>
		private float _f1, _f2;

		/// <summary>
		/// ANDA A RAMPA E FILTRA O QUADRO. Um quadro = <see cref="VozLocal.MsPorQuadro"/> ms de rampa.
		///
		/// Dois polos em cascata e nao um: um polo so cai 6 dB por oitava, e a 700 Hz isso ainda deixa
		/// a consoante passar -- soa como voz mal gravada, e nao como voz atras de um muro. Dois caem
		/// 12 dB/oitava, que e a ordem de grandeza de uma parede de verdade.
		///
		/// A RAMPA MEXE NO CORTE **E** NO GANHO ao mesmo tempo, e por isso a voz "entra na parede" em
		/// vez de so abaixar.
		/// </summary>
		public void Aplicar(short[] pcm, int amostras)
		{
			float passo = VozLocal.MsPorQuadro / 1000f / SegundosDeRampa;
			Abafada = Mathf.MoveToward(Abafada, Parede ? 1f : 0f, passo);

			// SEM PAREDE, NADA E FEITO. E o caminho comum (a maior parte das conversas e em campo
			// aberto) e ele nao paga nada. A memoria do filtro fica onde esta: zerar aqui daria um
			// estalo na primeira amostra da proxima parede.
			if (Abafada <= 0.001f) return;

			// O CORTE VAI DE "TUDO PASSA" (metade da taxa) ATE `CorteDaParede`, conforme a rampa.
			float livre = VozLocal.TaxaDeAmostragem * 0.5f;
			float corte = Mathf.Lerp(livre, CorteDaParede, Abafada);
			float a = 1f - MathF.Exp(-2f * MathF.PI * corte / VozLocal.TaxaDeAmostragem);
			float ganho = Mathf.Lerp(1f, GanhoAtrasDaParede, Abafada);

			for (int i = 0; i < amostras; i++)
			{
				float x = pcm[i] / (float)short.MaxValue;
				_f1 += a * (x - _f1);        // primeiro polo
				_f2 += a * (_f1 - _f2);      // segundo, em cascata
				pcm[i] = (short)Mathf.Clamp((int)(_f2 * ganho * short.MaxValue),
											short.MinValue, short.MaxValue);
			}
		}
	}

	/// <summary>
	/// ============================ O QUE SAIU DO FILTRO -- SO BANCADA ============================
	/// Nula em jogo. Recebe (quem falou, o PCM **ja filtrado**, quantas amostras, a distancia que veio
	/// no pacote, se veio marcado com parede) de cada quadro entregue ao alto-falante.
	///
	/// ELA E DEPOIS DA ABAFADA E ANTES DO MOTOR, e esse ponto e o unico util: antes do filtro so daria
	/// pra medir o que o codec devolveu (o que a `--diagvoz` ja mede sem rede nenhuma), e depois do
	/// motor nao ha nada pra ler -- o Godot nao devolve o sinal que ele mandou pra placa. Aqui esta o
	/// sinal exato que a pessoa vai ouvir, menos o volume que o `AudioStreamPlayer2D` aplica pela
	/// distancia (esse a bancada mede no BARRAMENTO, ver `Client/RoboDeVozViva.cs`).
	///
	/// Mesma razao do <see cref="Microfone.Espiao"/>: voz nao se fotografa.
	/// ==========================================================================================
	/// </summary>
	public static Action<int, short[], int, byte, bool>? Espiao;

	private readonly Dictionary<int, Fonte> _fontes = [];

	/// <summary>PCM descomprimido -- reusado, 50 quadros por segundo por falante.</summary>
	private readonly short[] _pcm = new short[VozLocal.AmostrasPorQuadro];

	/// <summary>O mesmo quadro no formato que o gerador do Godot quer. Reusado pelo mesmo motivo.</summary>
	private readonly Vector2[] _saida = new Vector2[VozLocal.AmostrasPorQuadro];

	public override void _Ready()
	{
		Instancia = this;
		ProcessMode = ProcessModeEnum.Always;
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
		_fontes.Clear();
	}

	/// <summary>
	/// ESTA PESSOA ESTA FALANDO AGORA? E o que acende o sinal sobre a cabeca (ver <see cref="SinalDeVoz"/>).
	///
	/// ============================ SEM PACOTE PROPRIO, E ISSO E DE PROPOSITO ============================
	/// A tentacao era um `S2C.Falando(id, bool)`. Nao precisa: **receber quadro E a evidencia**. E a
	/// evidencia certa, ainda por cima -- o sinal acende exatamente pra quem esta ouvindo aquela voz, e
	/// nao pra quem esta longe demais pra ouvi-la. Um pacote proprio teria que reimplementar o corte de
	/// alcance inteiro, e no dia em que os dois divergissem apareceria uma boca mexendo em silencio.
	///
	/// A CONSEQUENCIA ESTA ESCRITA: a quinta pessoa falando (a que nao coube nas quatro mais proximas)
	/// nao acende sinal. E o certo -- a voz dela nao esta acontecendo pra mim.
	/// ================================================================================================
	/// </summary>
	public bool Falando(int id) =>
		_fontes.TryGetValue(id, out Fonte? f)
		&& Time.GetTicksMsec() - f.UltimoMs <= VozLocal.MsAteCalar;

	// =====================================================================
	// CHEGOU UM QUADRO
	// =====================================================================
	/// <param name="corpo">O boneco de quem fala, ou nulo se ele ainda nao existe nesta tela.</param>
	/// <param name="ouvinte">Onde EU estou -- e de onde a voz sai quando nao ha corpo. Ver abaixo.</param>
	public void Receber(int id, ushort seq, byte distancia, bool parede,
						byte[] dados, int n, Node2D? corpo, Vector2 ouvinte)
	{
		if (n <= 0 || n > VozLocal.MaxBytesDeQuadro) return;

		Fonte f = _fontes.TryGetValue(id, out Fonte? achada) ? achada : Nascer(id);
		f.UltimoMs = Time.GetTicksMsec();
		f.Filtro.Parede = parede;

		// ============================ DE ONDE O SOM SAI ============================
		// COM CORPO: em cima dele, e o motor cuida da queda e do lado (esquerda/direita). E o pedido
		// literal -- "voz posicional, saindo de onde a pessoa esta".
		//
		// SEM CORPO: em cima de MIM, com o volume que o servidor mandou. Nao e um caso teorico -- quem
		// voa alto some da tela de quem esta no chao (`Voo.Enxerga`), e ai eu tenho a voz e nao tenho
		// o corpo. Posto em cima de mim ele nao ganha lado nenhum (correto: eu nao sei de onde vem) e
		// o volume vem do unico que sabe a distancia, que e quem cortou o alcance.
		// ==========================================================================
		if (corpo != null && IsInstanceValid(corpo))
		{
			f.Tocador.GlobalPosition = corpo.GlobalPosition;
			f.Tocador.VolumeDb = 0;
		}
		else
		{
			f.Tocador.GlobalPosition = ouvinte;
			f.Tocador.VolumeDb = Mathf.LinearToDb(Mathf.Max(
				VozLocal.VolumePelaDistancia(VozLocal.FracaoDaDistancia(distancia)), 0.0001f));
		}

		if (!f.Tocador.Playing) f.Tocador.Play();
		f.Fila ??= f.Tocador.GetStreamPlayback() as AudioStreamGeneratorPlayback;
		if (f.Fila == null) return;

		// ============================ O BURACO NA SEQUENCIA VIRA UM QUADRO RECUPERADO ============================
		// O canal e nao confiavel de proposito, entao perder quadro e normal e nao e erro. O que NAO
		// pode acontecer e emendar duas metades de silabas diferentes como se fossem seguidas -- e o
		// estalo que se ouve em toda voz de jogo mal feita.
		//
		// O Opus resolve isso com FEC: o quadro que CHEGOU carrega uma versao grosseira do anterior. Um
		// so, e nao a fila inteira: reconstruir tres quadros perdidos custaria mais latencia do que o
		// silencio que eles deixam, e a versao FEC ja e grosseira -- tres delas em fila soariam pior
		// que o buraco.
		// ====================================================================================================
		if (f.TemSeq && (ushort)(seq - f.Seq) == 2)
		{
			int rec = f.Decodificador.Decode(dados.AsSpan(0, n), _pcm, VozLocal.AmostrasPorQuadro,
											 decode_fec: true);
			if (rec > 0) Empurrar(f, rec);
		}
		f.Seq = seq;
		f.TemSeq = true;

		int amostras = f.Decodificador.Decode(dados.AsSpan(0, n), _pcm, VozLocal.AmostrasPorQuadro,
											  decode_fec: false);
		if (amostras <= 0) return;

		Empurrar(f, amostras);
		// DEPOIS do `Empurrar`, porque e ele quem filtra o `_pcm` no lugar: chamar antes entregaria a
		// bancada o sinal LIMPO e a abafada da parede -- que e metade do pedido do dono -- passaria
		// verde com o filtro desligado. Ver `Espiao`.
		Espiao?.Invoke(id, _pcm, amostras, distancia, parede);
	}

	private Fonte Nascer(int id)
	{
		// SEM BIBLIOTECA NATIVA -- ver o mesmo bloco em `Microfone.CriarCodificador`. Ligado aqui
		// TAMBEM porque quem ouve pode nunca ter falado: um jogador com o microfone desligado nunca
		// passa pelo codificador, e o padrao do Concentus vale por processo.
		OpusCodecFactory.AttemptToUseNativeLibrary = false;

		var tocador = new AudioStreamPlayer2D
		{
			Name = $"Voz{id}",
			Bus = AudioDirector.BusVoz,
			MaxDistance = Alcance,
			Attenuation = Curva,
			// O GERADOR RODA A 16 kHz e o motor reamostra pra taxa da placa. Fazer o contrario --
			// gerar ja na taxa do motor -- exigiria um reamostrador aqui, e o Godot ja tem um melhor.
			Stream = new AudioStreamGenerator
			{
				MixRate = VozLocal.TaxaDeAmostragem,
				// 0,25 s de folga. Menos que isso e um engasgo de quadro esvazia a fila e a voz corta;
				// muito mais e o atraso da conversa comeca a se ouvir (a fila e latencia pura).
				BufferLength = 0.25f,
			},
		};
		AddChild(tocador);

		var f = new Fonte
		{
			Decodificador = OpusCodecFactory.CreateDecoder(VozLocal.TaxaDeAmostragem, 1),
			Tocador = tocador,
		};
		_fontes[id] = f;
		return f;
	}

	// =====================================================================
	// A ABAFADA
	// =====================================================================
	private void Empurrar(Fonte f, int amostras)
	{
		if (f.Fila == null) return;

		f.Filtro.Aplicar(_pcm, amostras);

		int cabe = Math.Min(amostras, f.Fila.GetFramesAvailable());
		for (int i = 0; i < cabe; i++)
		{
			float v = _pcm[i] / (float)short.MaxValue;
			_saida[i] = new Vector2(v, v);   // mono nos dois canais; quem panoramiza e o `2D`
		}
		// O CAMINHO COMUM NAO ALOCA: a fila quase sempre tem os 320 lugares e o buffer reusado vai
		// inteiro. So o caso raro (fila quase cheia, quando o jogo engasgou) paga uma copia -- e ali
		// a alternativa seria descartar o quadro, que e um estalo audivel pra economizar 320 floats.
		if (cabe == _saida.Length) f.Fila.PushBuffer(_saida);
		else if (cabe > 0) f.Fila.PushBuffer(_saida[..cabe]);

	}

	// =====================================================================
	// LIMPEZA
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_fontes.Count == 0) return;

		ulong agora = Time.GetTicksMsec();
		List<int>? mortas = null;
		foreach ((int id, Fonte f) in _fontes)
			if (agora - f.UltimoMs > MsAteDesmontar) (mortas ??= []).Add(id);
		if (mortas == null) return;

		foreach (int id in mortas)
		{
			if (!_fontes.Remove(id, out Fonte? f)) continue;
			f.Tocador.Stop();
			f.Tocador.QueueFree();
		}
	}
}
