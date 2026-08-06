using Godot;

namespace Jandirus.Client;

/// <summary>
/// A POEIRA DE UMA CELULA QUE CAIU -- cascalho saltando, jato rente ao chao e fumaca subindo.
///
/// ============================ DE ONDE VEM ============================
/// O `createDust(T, 1)` do original, que o `turf/proc/Destroy` chama antes de trocar o turf
/// (`NewTurfs.dm`) e que o `Ticked` do knockback chama quando a parede cede. La e um sprite de
/// nuvem; aqui e particula, porque o pipeline nao converte aquele .dmi e porque particula responde
/// a direcao -- o cascalho pode sair pro lado em que o corpo bateu.
/// =====================================================================
///
/// ============================ POR QUE A PRIMEIRA VERSAO NAO PARECIA FUMACA ============================
/// Ela tinha os dois sistemas certos (cascalho e fumaca) e errava nos tres detalhes que decidem a
/// leitura:
///
///   * TEXTURA CHAPADA. Um quadrado de cor cheia com alfa 45% le como CONFETE, nao como fumaca --
///     a borda dura entrega o retangulo. Fumaca precisa de borda que desaparece, e o projeto ja
///     tinha o molde disso no floco de neve (`ClimaNaTela.TexturaDeFloco`).
///   * TAMANHO CONSTANTE. A nuvem nascia do tamanho em que morria. Fumaca de verdade EXPANDE
///     enquanto sobe, e e a expansao que faz o olho ler "gas" em vez de "objeto".
///   * SUMICO SECO. O alfa era fixo e a particula simplesmente acabava. Fumaca tem que ENTRAR
///     rapido e SAIR devagar -- e essa assimetria que da a sensacao de dissipar.
///
/// Os tres se consertam com dado, e nao com mais particula: uma textura radial, uma curva de escala
/// e uma rampa de cor com alfa. Custam zero por quadro (sao lidos pela GPU) e sao criados uma vez.
/// =====================================================================================================
///
/// SAO TRES SISTEMAS, e nao um com o triplo de particulas: cada um se move por regras diferentes.
/// O cascalho e pesado -- sai rapido, cai por gravidade e some inteiro. O JATO e o sopro do
/// impacto, rente ao chao, largo e curto. A NUVEM e o que fica: sobe devagar, cresce, gira com
/// turbulencia e desbota. Um sistema so teria que escolher entre os tres.
///
/// MORRE SOZINHO. As emissoes sao `OneShot`, e o node se apaga quando a mais longa termina --
/// senao cada parede derrubada deixaria um emissor parado no mapa pra sempre.
/// </summary>
public partial class PoeiraDeEstrago : Node2D
{
	/// <summary>Quanto o efeito inteiro dura, em segundos. E a vida da nuvem, que e a mais longa.</summary>
	private const double Duracao = 1.9;

	/// <summary>
	/// QUANTOS EFEITOS PODEM ESTAR VIVOS AO MESMO TEMPO.
	///
	/// ============================ ISTO NAO E PARANOIA ============================
	/// Uma rajada de destruicao nao derruba uma celula: derruba de tres a dez no mesmo quadro (o
	/// `RacharChao` sorteia as nove em volta), e um ZanzoClash faz isso seis a nove vezes em cinco
	/// segundos. Pior: quem ENTRA numa zona recebe a lista inteira do que ja caiu la, uma celula
	/// por pacote -- e o cliente trata cada uma como se tivesse acabado de cair.
	///
	/// Sem teto, voltar do espaco depois de uma sessao de briga custa a leva inteira num punhado de
	/// quadros. Com teto, o mais VELHO sai pra o mais novo entrar: quem acabou de quebrar e o que
	/// interessa ver.
	/// =============================================================================
	/// </summary>
	public const int MaxVivos = 24;

	private static readonly List<PoeiraDeEstrago> _vivos = [];

	/// <summary>
	/// QUANTO FALTA ATE O NODE PODER MORRER. Calculado a partir dos EMISSORES, e nao escrito a mao.
	///
	/// ============================ O CORTE SECO ============================
	/// Era `Duracao` (1,9 s), o mesmo numero do `Lifetime` da nuvem -- e isso esta errado por uma
	/// razao que o `Explosiveness` esconde: com `OneShot` e `Explosiveness = 0,6`, a nuvem nao sai
	/// toda no instante zero. Ela EMITE ao longo dos primeiros `Lifetime x (1 - Explosiveness)` =
	/// 0,76 s, e a ultima particula so terminaria a vida dela em 0,76 + 1,9 = 2,66 s.
	///
	/// O node se apagava aos 1,9 s. Tudo que ainda estava no ar sumia no mesmo quadro -- o "corte
	/// seco" que o dono viu. Nao era a curva de dissipacao: essa sempre esteve certa, cada particula
	/// desbota direitinho. Era o palco sendo retirado com os atores em cena.
	///
	/// DERIVADO E NAO CONSTANTE de proposito: mexer no `Lifetime` ou no `Explosiveness` de qualquer
	/// emissor volta a criar o corte se o numero for escrito a mao. Aqui ele se recalcula sozinho.
	/// =====================================================================
	/// </summary>
	private double _resta;

	/// <summary>A cor padrao, quando quem chamou nao sabe de que tile a poeira saiu.</summary>
	private static readonly Color TerraPadrao = new(0.46f, 0.36f, 0.26f);

	/// <summary>
	/// Solta a poeira numa celula.
	/// </summary>
	/// <param name="rumo">
	/// Direcao do impacto -- o cascalho voa preferencialmente pra la. Vetor zero espalha pra todo
	/// lado (chao rachando sozinho).
	/// </param>
	/// <param name="cor">
	/// A cor do que foi destruido. Nulo = terra. Poeira de pedra cinza saindo marrom entrega que o
	/// efeito e generico; e a cor que amarra o efeito ao lugar.
	/// </param>
	public static void Soltar(Node pai, Vector2 onde, Vector2 rumo = default, Color? cor = null)
	{
		// CONTA O PEDIDO ANTES DO DESPEJO. A primeira versao media `_vivos.Count` DEPOIS de despejar
		// -- ou seja, media o proprio teto e nunca podia passar dele. Um numero que nao pode falhar
		// nao prova nada; o que prova o teto e quantos efeitos foram PEDIDOS contra quantos viveram.
		PedidosDeTeste++;

		// O TETO DISPARA AQUI, e nao "quando der": o mais velho sai antes de o novo entrar.
		_vivos.RemoveAll(p => !GodotObject.IsInstanceValid(p));
		while (_vivos.Count >= MaxVivos)
		{
			PoeiraDeEstrago velho = _vivos[0];
			_vivos.RemoveAt(0);
			if (GodotObject.IsInstanceValid(velho)) velho.QueueFree();
		}

		UltimaCorDeTeste = cor;

		var p = new PoeiraDeEstrago { Position = onde, ZIndex = 1 };
		_vivos.Add(p);
		pai.AddChild(p);
		p.Montar(rumo, cor ?? TerraPadrao);
	}

	/// <summary>A cor com que a ultima poeira saiu. So pra bancada -- prova que ela veio do TILE.</summary>
	public static Color? UltimaCorDeTeste { get; private set; }

	/// <summary>Quantas vezes o `Soltar` foi chamado. So pra bancada -- prova que o teto foi ESTOURADO.</summary>
	public static int PedidosDeTeste { get; private set; }

	/// <summary>Quantos efeitos estao vivos AGORA. So pra bancada -- ver `MaxVivos`.</summary>
	public static int VivosDeTeste
	{
		get
		{
			_vivos.RemoveAll(p => !GodotObject.IsInstanceValid(p));
			return _vivos.Count;
		}
	}

	private void Montar(Vector2 rumo, Color cor)
	{
		bool dirigido = rumo.LengthSquared() > 1e-6f;
		Vector2 dir = dirigido ? rumo.Normalized() : Vector2.Up;

		// AS TRES CORES SAIEM DA MESMA: o cascalho e a propria (e materia solida do tile), o jato
		// clareia um pouco (poeira fina espalha luz) e a nuvem clareia mais e dessatura, que e o que
		// acontece com particulado no ar.
		Color pedra = cor;
		Color jato = cor.Lerp(Colors.White, 0.30f);
		Color nuvem = cor.Lerp(new Color(0.86f, 0.84f, 0.80f), 0.55f);

		// ---- CASCALHO: pesado, rapido, cai ----
		AddChild(new GpuParticles2D
		{
			Amount = 12,
			Lifetime = 0.55,
			OneShot = true,
			Explosiveness = 1f,          // tudo no mesmo instante: e um estouro, nao um vazamento
			Emitting = true,
			Texture = Quadrado(3, pedra),
			ProcessMaterial = new ParticleProcessMaterial
			{
				Direction = new Vector3(dir.X, dir.Y, 0),
				// ESPALHAMENTO ESTREITO quando ha rumo (o corpo bateu ALI) e total quando nao ha
				// (o chao rachou sozinho, e a pedra sai pra todo lado).
				Spread = dirigido ? 55f : 180f,
				InitialVelocityMin = 60f,
				InitialVelocityMax = 190f,
				Gravity = new Vector3(0, 420, 0),   // pedra cai

				// ============================ NAO NASCE TUDO NO MESMO PONTO ============================
				// Uma celula tem 32 px; emitir do centro exato faz cada estrago virar um ponto, e uma
				// rajada de celulas vizinhas virar uma GRADE de pontos identicos -- foi o que a foto da
				// bancada mostrou. Emitindo de dentro de um disco, cada nuvem nasce um pouco fora do
				// lugar da outra e o conjunto le como uma coisa so.
				// ======================================================================================
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
				EmissionSphereRadius = 7f,
				ScaleMin = 0.6f,
				ScaleMax = 1.6f,
				AngularVelocityMin = -360f,
				AngularVelocityMax = 360f,
				Damping = new Vector2(20, 60),
			},
		});

		// ---- JATO: o sopro rente ao chao ----
		//
		// Ele e o que faltava pro estrago ter FORCA. O cascalho conta o que quebrou e a nuvem conta
		// o que sobrou; o jato conta o INSTANTE -- ar empurrado pra fora, baixo e largo, morrendo em
		// meio segundo. Sai quase na horizontal de proposito (gravidade levemente negativa apenas
		// para nao afundar).
		AddChild(new GpuParticles2D
		{
			Amount = 10,
			Lifetime = 0.5,
			OneShot = true,
			Explosiveness = 1f,
			Emitting = true,
			Texture = Baforada,
			Modulate = new Color(jato.R, jato.G, jato.B, 0.38f),
			ProcessMaterial = new ParticleProcessMaterial
			{
				Direction = dirigido ? new Vector3(dir.X, dir.Y, 0) : new Vector3(0, 0, 0),
				Spread = dirigido ? 80f : 180f,
				InitialVelocityMin = 55f,
				InitialVelocityMax = 130f,
				Gravity = new Vector3(0, -8, 0),
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
				EmissionSphereRadius = 10f,
				ScaleMin = 0.30f,
				ScaleMax = 0.62f,
				ScaleCurve = Expansao,
				ColorRamp = Dissipacao,
				Damping = new Vector2(90, 160),     // freia forte: o sopro perde forca perto
				AngularVelocityMin = -60f,
				AngularVelocityMax = 60f,
			},
		});

		// ---- NUVEM: leve, lenta, sobe, cresce e desbota ----
		AddChild(new GpuParticles2D
		{
			Amount = 14,
			Lifetime = Duracao,
			OneShot = true,
			Explosiveness = 0.6f,               // nao sai tudo junto: a nuvem se forma, nao aparece
			Emitting = true,
			Texture = Baforada,
			// SOMA NAO: fumaca ESCONDE o que esta atras. Em `Add` ela clarearia o chao e viraria um
			// clarao esbranquicado no meio do estrago.
			Modulate = new Color(nuvem.R, nuvem.G, nuvem.B, 0.34f),
			ProcessMaterial = new ParticleProcessMaterial
			{
				Direction = new Vector3(0, -1, 0),
				Spread = 55f,
				InitialVelocityMin = 10f,
				InitialVelocityMax = 34f,
				Gravity = new Vector3(0, -16, 0),   // sobe
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
				EmissionSphereRadius = 12f,
				ScaleMin = 0.55f,
				ScaleMax = 1.30f,
				ScaleCurve = Expansao,
				ColorRamp = Dissipacao,
				Damping = new Vector2(6, 16),
				AngularVelocityMin = -25f,
				AngularVelocityMax = 25f,

				// TURBULENCIA: e o que separa "nuvem" de "bolha subindo em linha reta". Fraca de
				// proposito -- forte demais vira redemoinho e chama atencao pra si.
				TurbulenceEnabled = true,
				TurbulenceNoiseStrength = 1.6f,
				TurbulenceNoiseScale = 2.2f,
				TurbulenceInfluenceMin = 0.05f,
				TurbulenceInfluenceMax = 0.25f,
			},
		});

		// DEPOIS DE TODOS OS EMISSORES: quem decide quando o node morre sao eles. Ver `CalcularFim`.
		CalcularFim();
	}

	// =====================================================================
	// AS TEXTURAS E AS CURVAS -- feitas uma vez, compartilhadas por todos
	// =====================================================================
	private static readonly Dictionary<string, ImageTexture> _cache = [];

	/// <summary>
	/// O grao de cascalho: um quadrado de cor.
	///
	/// Sem arquivo de proposito. Um .png de pedrinha seria mais um asset pra converter, versionar e
	/// combinar com a paleta de cada planeta; num efeito de meio segundo, a diferenca entre um
	/// quadradinho e um grao desenhado nao chega ao olho -- o que chega e o movimento.
	///
	/// CACHEADO: sem isto, cada celula derrubada criava uma textura de GPU nova, e uma rajada
	/// derruba de tres a dez celulas no mesmo quadro.
	/// </summary>
	private static ImageTexture Quadrado(int lado, Color cor)
	{
		string chave = lado + ":" + cor.ToRgba32();
		if (_cache.TryGetValue(chave, out ImageTexture? pronta)) return pronta;

		Image img = Image.CreateEmpty(lado, lado, false, Image.Format.Rgba8);
		img.Fill(cor);
		ImageTexture nova = ImageTexture.CreateFromImage(img);
		_cache[chave] = nova;
		return nova;
	}

	private static ImageTexture? _baforadaCache;

	/// <summary>
	/// A BAFORADA: um disco BRANCO que desaparece na borda.
	///
	/// Branco, e nao colorido: a cor entra pelo `Modulate` de cada sistema, entao a mesma textura
	/// serve pra poeira de terra, de pedra e de neve sem virar uma textura por cor.
	///
	/// E o mesmo molde do floco de neve (`ClimaNaTela.TexturaDeFloco`), com o desbotamento comecando
	/// mais cedo -- floco tem contorno, fumaca nao pode ter.
	/// </summary>
	private static ImageTexture Baforada => _baforadaCache ??= MontarBaforada();

	/// <summary>
	/// ============================ POR QUE NAO E UM `GradientTexture2D` ============================
	/// Era. E saia QUADRADO: o preenchimento radial do Godot mede a distancia ao longo do vetor
	/// `FillFrom -> FillTo`, e a diagonal do quadrado e 1,41 vez o raio -- entao o alfa ainda nao
	/// tinha chegado a zero na borda e a aresta do retangulo aparecia. A foto da bancada mostrou
	/// exatamente isso: nuvens retangulares cinzentas com contorno reto, e uma arcada de gradiente
	/// visivel dentro.
	///
	/// Aqui a queda e CALCULADA por pixel e normalizada pela distancia ate a QUINA -- ou seja, o alfa
	/// chega a zero em todo o perimetro, diagonal inclusive. E a mesma quantidade de trabalho (uma
	/// imagem de 32x32, uma vez por sessao) com o resultado que a fumaca precisa.
	/// ==============================================================================================
	/// </summary>
	private static ImageTexture MontarBaforada()
	{
		const int lado = 32;
		const float meio = lado / 2f;
		Image img = Image.CreateEmpty(lado, lado, false, Image.Format.Rgba8);

		for (int y = 0; y < lado; y++)
			for (int x = 0; x < lado; x++)
			{
				// distancia ao centro, com 1 = a QUINA (e nao a aresta)
				float dx = (x + 0.5f - meio) / meio;
				float dy = (y + 0.5f - meio) / meio;
				float d = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Sqrt2;

				// QUEDA SUAVE E JA COMECANDO CEDO: fumaca nao tem miolo solido nem contorno.
				// O expoente 1,6 tira o "olho" que uma queda linear deixa no centro.
				float a = Mathf.Clamp(1f - d * 1.42f, 0f, 1f);
				img.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(a, 1.6f)));
			}

		return ImageTexture.CreateFromImage(img);
	}

	private static CurveTexture? _expansaoCache, _dissipacaoCache;

	/// <summary>
	/// A ESCALA AO LONGO DA VIDA: nasce pequena e abre.
	///
	/// Abre RAPIDO no comeco e devagar depois -- e assim que gas se expande contra o ar parado, e e
	/// o que faz a nuvem parecer empurrada por alguma coisa em vez de inflada por igual.
	/// </summary>
	private static CurveTexture Expansao => _expansaoCache ??= MontarCurva(0.35f, 1.0f, 1.35f);

	private static CurveTexture MontarCurva(float inicio, float meio, float fim)
	{
		var c = new Curve { MinValue = 0, MaxValue = Mathf.Max(fim, 1f) };
		c.AddPoint(new Vector2(0, inicio));
		c.AddPoint(new Vector2(0.35f, meio));
		c.AddPoint(new Vector2(1, fim));
		return new CurveTexture { Curve = c, Width = 64 };
	}

	private static GradientTexture1D? _dissipacaoGrad;

	/// <summary>
	/// O ALFA AO LONGO DA VIDA: entra rapido, sai devagar.
	///
	/// A assimetria E o efeito. Alfa constante faz a particula "acabar"; subir num piscar e cair ao
	/// longo de todo o resto faz ela DISSIPAR -- e dissipar e a unica coisa que fumaca faz que
	/// poeira chapada nao faz.
	/// </summary>
	private static GradientTexture1D Dissipacao => _dissipacaoGrad ??= MontarDissipacao();

	private static GradientTexture1D MontarDissipacao()
	{
		var g = new Gradient();
		g.SetColor(0, new Color(1, 1, 1, 0));
		g.AddPoint(0.12f, new Color(1, 1, 1, 1));
		g.AddPoint(0.45f, new Color(1, 1, 1, 0.75f));
		g.SetColor(1, new Color(1, 1, 1, 0));
		return new GradientTexture1D { Gradient = g, Width = 64 };
	}

	/// <summary>
	/// O INSTANTE EM QUE A ULTIMA PARTICULA DE TODOS OS EMISSORES TERMINA.
	///
	/// Pra um `GpuParticles2D` em `OneShot`: a emissao dura `Lifetime x (1 - Explosiveness)` e cada
	/// particula vive `Lifetime`, entao a ultima acaba em `Lifetime x (2 - Explosiveness)`. Com
	/// `Explosiveness = 1` (tudo de uma vez) isso da o proprio `Lifetime`, como deve ser.
	/// </summary>
	private void CalcularFim()
	{
		double fim = 0;
		foreach (Node n in GetChildren())
			if (n is GpuParticles2D g)
				fim = Math.Max(fim, g.Lifetime * (2 - g.Explosiveness));

		// A FOLGA cobre o quadro em que a ultima particula ainda esta sendo desenhada com alfa
		// baixinho. Sem ela o corte volta, so que pequeno demais pra alguem descrever e grande o
		// bastante pra incomodar.
		_resta = fim + 0.1;

		// SONDAS DA BANCADA: o que a ultima particula PRECISA e o que o node CONCEDE. Ver
		// `RoboDePoeira` -- o teste compara os dois em vez de cronometrar, que e o unico jeito de
		// ele reprovar o corte seco sem depender de o relogio cair no instante certo.
		FimNecessarioDeTeste = fim;
		FimConcedidoDeTeste = _resta;
	}

	/// <summary>Quando a ultima particula termina, e ate quando o node vive. So pras bancadas.</summary>
	public static double FimNecessarioDeTeste, FimConcedidoDeTeste;

	public override void _Process(double delta)
	{
		_resta -= delta;
		if (_resta <= 0) QueueFree();
	}

	public override void _ExitTree() => _vivos.Remove(this);
}
