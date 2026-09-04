using Concentus;
using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ A VOZ DOS OUTROS: AMORTECER, DESCOMPRIMIR, ABAFAR, TOCAR NO LUGAR ============================
/// Chega um quadro de 20 ms de alguem que o SERVIDOR ja decidiu que eu posso ouvir (ver
/// `GameServer.Voz.cs` -- quem esta longe nao chega aqui, nao chega baixinho). O que sobra pra este
/// arquivo e a segunda metade da frase do dono: *"com EFEITO DE DISTANCIA caso se afaste, e tb se
/// tiver ATRAS DE PAREDE da uma ABAFADA"* -- e a frase seguinte dele, *"o audio fica picotando e nao
/// fica fluido"*, que e sobre o que ha entre o pacote e o alto-falante.
///
///   | o que          | como                                                                 |
///   |----------------|----------------------------------------------------------------------|
///   | picote         | <see cref="FilaDeJitter"/>: reordena, pre-carrega, remenda, guiada pelo gerador |
///   | posicao        | um `AudioStreamPlayer2D` posto EM CIMA do corpo de quem fala, suavizado |
///   | distancia      | a atenuacao do proprio motor (nao linear -- ver abaixo)               |
///   | parede         | filtro passa-BAIXA no PCM, com rampa (<see cref="FiltroDeParede"/>)  |
///   | nao brigar     | barramento `Voz`, proprio, com controle proprio                      |
///
/// ============================ O PACOTE NAO TOCA NO GERADOR ============================
/// Antes, `Receber` fazia `Play()` com a fila vazia e empurrava o quadro na chegada: a folga na fila
/// do gerador era UM quadro, e qualquer pacote 20 ms atrasado virava zeros. Agora `Receber` so entrega
/// o pacote a <see cref="FilaDeJitter"/>; quem empurra e o `_Process`, lendo do GERADOR quantas
/// amostras ele ainda tem e completando ate o alvo. O relogio da reproducao e o do gerador, e nao o
/// da rede. O desenho inteiro esta no cabecalho daquela classe.
/// ======================================================================================
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
	/// 10 s. Era 1,5 s, e isso desmontava a fonte entre duas frases da mesma conversa: cada retomada
	/// nascia com o gerador recriado e a fila em zero, e o primeiro pacote da frase nova tocava sem
	/// folga nenhuma atras dele -- um picote garantido no comeco de cada frase. Com 10 s a fonte
	/// sobrevive as pausas de uma conversa; um `AudioStreamPlayer2D` tocando silencio custa nada, e a
	/// retomada de fala re-prima a pre-carga dentro da <see cref="FilaDeJitter"/> sem recriar coisa
	/// nenhuma.
	/// </summary>
	private const ulong MsAteDesmontar = 10_000;

	/// <summary>
	/// A CONSTANTE DE TEMPO DA SUAVIZACAO de volume e posicao, em segundos.
	///
	/// 0,08 s. Volume e posicao eram escritos por PACOTE, e o pacote de quem some da tela (voa alto) e
	/// volta trocava a fonte entre "em cima do corpo, 0 dB" e "em cima de mim, -X dB" num quadro so --
	/// um salto audivel. Com 80 ms a troca vira um deslizamento; e o corpo anda a 160 px/s, entao a
	/// fonte fica no maximo 13 px (menos de meio tile) atras dele, que nao se ouve.
	/// </summary>
	private const float SegundosDeSuavizacao = 0.08f;

	/// <summary>
	/// A FILA DO GERADOR, em segundos. E CAPACIDADE, nao pre-carga: quanto pode estar esperando pra
	/// tocar. Precisa caber o alvo maximo da <see cref="FilaDeJitter"/> (10 quadros = 200 ms) mais o
	/// quadro que entra por cima dele. O motor arredonda pra potencia de dois (0,3 s a 16 kHz vira
	/// 8192 amostras) -- e por isso a capacidade de verdade e LIDA do gerador, nunca calculada.
	/// </summary>
	private const float SegundosDeFila = 0.3f;

	/// <summary>Uma pessoa que esta falando comigo -- a fila dela, o tocador dela, o filtro dela.</summary>
	private sealed class Fonte
	{
		public required int Id;
		public required AudioStreamPlayer2D Tocador;
		public FilaDeJitter Fila = null!;
		public AudioStreamGeneratorPlayback? Gerador;

		/// <summary>Quantas amostras cabem no gerador VAZIO -- lida dele, ver <see cref="SegundosDeFila"/>.</summary>
		public int Capacidade;

		/// <summary>A abafada desta voz: a rampa, o filtro e a memoria dele. Ver <see cref="FiltroDeParede"/>.</summary>
		public readonly FiltroDeParede Filtro = new();

		/// <summary>Quando chegou o ultimo PACOTE -- e o que decide o desmonte.</summary>
		public ulong UltimoPacoteMs;

		/// <summary>Quando o ultimo quadro de VOZ foi pro gerador -- e o que acende o sinal sobre a cabeca.</summary>
		public ulong UltimoQuadroMs;

		/// <summary>O boneco de quem fala (pode nao existir nesta tela) e onde EU estava no ultimo pacote.</summary>
		public Node2D? Corpo;
		public Vector2 Ouvinte;

		/// <summary>Volume linear atual e alvo (a suavizacao anda entre os dois).</summary>
		public float Volume = 1f, VolumeAlvo = 1f;
		public bool Posicionada;
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
	/// no pacote, se veio marcado com parede) de cada quadro DE VOZ entregue ao gerador -- decodificado,
	/// FEC, PLC ou esticado. A pre-carga de silencio nao passa aqui: ela nao e voz de ninguem.
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

	/// <summary>O quadro no formato que o gerador do Godot quer. Reusado, 50 quadros por segundo por falante.</summary>
	private readonly Vector2[] _saida = new Vector2[VozLocal.AmostrasPorQuadro];

	/// <summary>A pre-carga, ja no formato do gerador. Nunca e escrito.</summary>
	private readonly Vector2[] _saidaMuda = new Vector2[VozLocal.AmostrasPorQuadro];

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
	/// A tentacao era um `S2C.Falando(id, bool)`. Nao precisa: **tocar quadro E a evidencia**. E a
	/// evidencia certa, ainda por cima -- o sinal acende exatamente pra quem esta ouvindo aquela voz, e
	/// nao pra quem esta longe demais pra ouvi-la. Um pacote proprio teria que reimplementar o corte de
	/// alcance inteiro, e no dia em que os dois divergissem apareceria uma boca mexendo em silencio.
	///
	/// PELO QUADRO TOCADO E NAO PELO PACOTE CHEGADO: entre um e outro ha a pre-carga da fila (60 a
	/// 200 ms). Acender no pacote apagaria o sinal enquanto a ultima palavra ainda esta saindo.
	///
	/// A CONSEQUENCIA ESTA ESCRITA: a quinta pessoa falando (a que nao coube nas quatro mais proximas)
	/// nao acende sinal. E o certo -- a voz dela nao esta acontecendo pra mim.
	/// ================================================================================================
	/// </summary>
	public bool Falando(int id) =>
		_fontes.TryGetValue(id, out Fonte? f)
		&& Time.GetTicksMsec() - f.UltimoQuadroMs <= VozLocal.MsAteCalar;

	// =====================================================================
	// CHEGOU UM QUADRO
	// =====================================================================
	/// <param name="corpo">O boneco de quem fala, ou nulo se ele ainda nao existe nesta tela.</param>
	/// <param name="ouvinte">Onde EU estou -- e de onde a voz sai quando nao ha corpo. Ver <see cref="Suavizar"/>.</param>
	public void Receber(int id, ushort seq, byte distancia, bool parede,
						byte[] dados, int n, Node2D? corpo, Vector2 ouvinte)
	{
		if (n <= 0 || n > VozLocal.MaxBytesDeQuadro) return;

		Fonte f = _fontes.TryGetValue(id, out Fonte? achada) ? achada : Nascer(id);
		ulong agora = Time.GetTicksMsec();
		f.UltimoPacoteMs = agora;
		f.Corpo = corpo;
		f.Ouvinte = ouvinte;

		// O TOCADOR FICA TOCANDO ENQUANTO A FONTE VIVER -- inclusive silencio entre duas frases. Parar
		// e recomecar e o que zerava a fila a cada retomada. O gerador so existe depois do `Play`.
		if (!f.Tocador.Playing)
		{
			f.Tocador.Play();
			f.Gerador = f.Tocador.GetStreamPlayback() as AudioStreamGeneratorPlayback;
			f.Capacidade = f.Gerador?.GetFramesAvailable() ?? 0;
		}
		if (f.Gerador == null) return;

		f.Fila.Receber(seq, dados.AsSpan(0, n), distancia, parede, (long)agora);
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
				BufferLength = SegundosDeFila,
			},
		};
		AddChild(tocador);

		var f = new Fonte { Id = id, Tocador = tocador };
		// O DELEGATE E DA FONTE E MORRE COM ELA: nao e assinatura em evento estatico, entao nao ha o
		// que cancelar (a licao das assinaturas vazadas nao se aplica aqui).
		f.Fila = new FilaDeJitter(OpusCodecFactory.CreateDecoder(VozLocal.TaxaDeAmostragem, 1),
			(pcm, tipo, _, dist, parede) => Entregar(f, pcm, tipo, dist, parede));
		_fontes[id] = f;
		return f;
	}

	// =====================================================================
	// UM QUADRO PRONTO VAI PRO GERADOR
	// =====================================================================
	/// <summary>
	/// A FILA DECIDIU QUE E A HORA DESTE QUADRO. Aqui ele vira som: a parede que veio carimbada NELE
	/// (e nao no pacote mais recente -- entre um e outro ha a pre-carga) alimenta a rampa, o filtro
	/// modifica o PCM no lugar, e o resultado vai pro gerador inteiro. Nunca meio quadro: a fila so
	/// entrega quando cabe.
	/// </summary>
	private void Entregar(Fonte f, short[] pcm, FilaDeJitter.Tipo tipo, byte distancia, bool parede)
	{
		if (f.Gerador == null) return;

		if (tipo == FilaDeJitter.Tipo.Silencio)
		{
			f.Gerador.PushBuffer(_saidaMuda);
			return;
		}

		f.Filtro.Parede = parede;
		f.Filtro.Aplicar(pcm, VozLocal.AmostrasPorQuadro);

		// ============================ DE ONDE O SOM SAI ============================
		// COM CORPO: em cima dele, e o motor cuida da queda e do lado (esquerda/direita). E o pedido
		// literal -- "voz posicional, saindo de onde a pessoa esta".
		//
		// SEM CORPO: em cima de MIM, com o volume que o servidor mandou. Nao e um caso teorico -- quem
		// voa alto some da tela de quem esta no chao (`Voo.Enxerga`), e ai eu tenho a voz e nao tenho
		// o corpo. Posto em cima de mim ele nao ganha lado nenhum (correto: eu nao sei de onde vem) e
		// o volume vem do unico que sabe a distancia, que e quem cortou o alcance.
		//
		// Aqui so se escreve o ALVO; quem anda ate ele e a suavizacao no `_Process`.
		// ==========================================================================
		f.VolumeAlvo = f.Corpo != null && IsInstanceValid(f.Corpo)
			? 1f
			: VozLocal.VolumePelaDistancia(VozLocal.FracaoDaDistancia(distancia));

		for (int i = 0; i < VozLocal.AmostrasPorQuadro; i++)
		{
			float v = pcm[i] / (float)short.MaxValue;
			_saida[i] = new Vector2(v, v);   // mono nos dois canais; quem panoramiza e o `2D`
		}
		f.Gerador.PushBuffer(_saida);
		f.UltimoQuadroMs = Time.GetTicksMsec();

		// DEPOIS do filtro, porque e ele quem modifica o `pcm` no lugar: chamar antes entregaria a
		// bancada o sinal LIMPO e a abafada da parede -- que e metade do pedido do dono -- passaria
		// verde com o filtro desligado. Ver `Espiao`.
		Espiao?.Invoke(f.Id, pcm, VozLocal.AmostrasPorQuadro, distancia, parede);
	}

	// =====================================================================
	// O RELOGIO DO GERADOR, A SUAVIZACAO E A LIMPEZA
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_fontes.Count == 0) return;

		ulong agora = Time.GetTicksMsec();
		List<int>? mortas = null;
		foreach ((int id, Fonte f) in _fontes)
		{
			if (f.Gerador != null)
			{
				// O RELOGIO E O DO GERADOR: o que ele ja consumiu e o que a fila repoe, em ordem.
				int livres = f.Gerador.GetFramesAvailable();
				f.Fila.Puxar(f.Capacidade - livres, livres, (long)agora);
			}
			Suavizar(f, (float)delta);
			if (agora - f.UltimoPacoteMs > MsAteDesmontar) (mortas ??= []).Add(id);
		}
		if (mortas == null) return;

		foreach (int id in mortas)
		{
			if (!_fontes.Remove(id, out Fonte? f)) continue;
			f.Tocador.Stop();
			f.Tocador.QueueFree();
		}
	}

	/// <summary>Anda posicao e volume ate o alvo. Ver <see cref="SegundosDeSuavizacao"/>.</summary>
	private static void Suavizar(Fonte f, float delta)
	{
		Vector2 alvo = f.Corpo != null && IsInstanceValid(f.Corpo) ? f.Corpo.GlobalPosition : f.Ouvinte;

		// A PRIMEIRA POSICAO E DIRETA: deslizar da origem do mundo ate o corpo seria uma voz vinda de
		// lugar nenhum no primeiro decimo de segundo de toda conversa.
		if (!f.Posicionada)
		{
			f.Tocador.GlobalPosition = alvo;
			f.Volume = f.VolumeAlvo;
			f.Posicionada = true;
		}
		else
		{
			float k = 1f - MathF.Exp(-delta / SegundosDeSuavizacao);
			f.Tocador.GlobalPosition = f.Tocador.GlobalPosition.Lerp(alvo, k);
			f.Volume = Mathf.Lerp(f.Volume, f.VolumeAlvo, k);
		}
		f.Tocador.VolumeDb = Mathf.LinearToDb(Mathf.Max(f.Volume, 0.0001f));
	}
}
