using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O CLIMA NA TELA: o que cai, a massa que fecha o céu e o raio.
///
/// ============================ TRÊS PEÇAS, E CADA UMA NA TÉCNICA CERTA ============================
///  1. O QUE CAI (chuva, neve, areia) é PARTÍCULA. Pingo é uma coisa com posição, velocidade e
///     fim -- fingir isso num shader de tela obriga a repetir, e o olho pega a repetição em dois
///     segundos.
///  2. A MASSA (nuvem, neblina, fumaça, a poeira suspensa da areia) é SHADER
///     (`Assets/Shaders/Clima.gdshader`). Névoa não tem contorno nem contagem; é campo contínuo, e
///     um quad com ruído dá isso por custo fixo que não cresce com a "quantidade" de nuvem.
///  3. O RAIO é SHADER também (`Assets/Shaders/Raio.gdshader`), porque é um caminho contínuo de
///     espessura variável -- com partículas sairia pontilhado.
/// ==================================================================================================
///
/// ============================ O CLIMA ESTÁ NO MUNDO, NÃO NO OLHO ============================
/// A primeira versão punha tudo num `CanvasLayer`, em espaço de tela. Era barato e errado: o
/// jogador andava e a chuva andava junto, sempre nos mesmos pixels -- lia como sujeira na lente,
/// não como tempo. Chuva é uma coisa que acontece NUM LUGAR.
///
/// O conserto guarda a propriedade que interessava (só existe o que a câmera vê) e devolve a que
/// faltava (o lugar), com uma técnica por peça:
///
///  * AS PARTÍCULAS vivem no MUNDO, com `LocalCoords = false`: o emissor acompanha a câmera, mas
///    o pingo já solto cai em coordenada de mundo e fica pra trás quando você anda. O emissor é
///    uma faixa acima da tela, então continua não existindo um pingo sequer fora do que se vê.
///  * O VÉU continua sendo um quad de TELA -- e tem que ser, porque névoa fica ENTRE você e tudo,
///    não num pedaço do chão. O que foi pro mundo é o CAMPO: o shader amostra o ruído em
///    coordenada de mundo (ver `origem`/`paralaxe` em `Clima.gdshader`), então a mancha de nuvem
///    fica sobre o mesmo pedaço de planeta enquanto você atravessa por baixo dela.
///  * O RAIO fica em tela de propósito. Ele é longe e dura 0,3 s: coisa distante quase não faz
///    paralaxe, e ancorá-lo no chão o faria deslizar como se estivesse a dez metros.
/// ============================================================================================
///
/// A CONTA NÃO CRESCE COM A CHUVA: a força do clima mexe no `AmountRatio` de um pool de tamanho
/// FIXO, então o pior caso é sempre o mesmo -- que é o que se quer poder medir uma vez e esquecer.
/// </summary>
public partial class ClimaNaTela : Node2D
{
	/// <summary>
	/// Quantos pingos no pool. Fixo -- a força do clima mexe na FRAÇÃO emitida, não no tamanho.
	///
	/// 900 numa tela comum dá chuva cerrada. São quads de 2x14 px num único draw call de GPU; o
	/// custo real é de preenchimento, e um pingo fino quase não preenche nada.
	/// </summary>
	private const int Pingos = 900;

	/// <summary>A camada do véu e do raio: acima do mundo, abaixo do HUD -- ver <see cref="LuaNoCeu"/>.</summary>
	private const int Camada = 1;

	/// <summary>
	/// Onde a chuva entra na pilha do mundo: acima dos corpos, abaixo da cinemática de
	/// transformação (80) e do véu de visão (90).
	///
	/// ABAIXO DO VÉU DE VISÃO de propósito: chuva caindo atrás de uma parede que você não enxerga
	/// tem que ser escondida junto com a parede, senão o contorno do que está oculto aparece
	/// desenhado em água.
	/// </summary>
	private const int CamadaDaChuva = 70;

	/// <summary>O raio vem na frente da chuva: ele e a coisa mais brilhante da cena naquele instante.</summary>
	private const int CamadaDoRaio = 75;

	private CanvasLayer _tela = null!;
	private ColorRect _veu = null!;
	private ShaderMaterial _tintaVeu = null!;
	private GpuParticles2D _queda = null!;
	private ParticleProcessMaterial _fisica = null!;

	private Sprite2D _raio = null!;
	private ShaderMaterial _tintaRaio = null!;

	private double _t;
	private double _idadeDoRaio = 1;
	private double _proximoRaio;
	private float _clarao;

	private EstadoDoClima _estado;
	private TipoDeClima _desenhado = TipoDeClima.Limpo;

	/// <summary>As duas formas do que cai. Feitas uma vez -- ver `TexturaDePingo`/`TexturaDeFloco`.</summary>
	private static GradientTexture2D? _pingoCache, _flocoCache;
	private static GradientTexture2D _pingo => _pingoCache ??= TexturaDePingo();
	private static GradientTexture2D _floco => _flocoCache ??= TexturaDeFloco();

	private static Shader? _shVeu, _shRaio;
	private static Shader ShVeu => _shVeu ??= ResourceLoader.Load<Shader>("res://Assets/Shaders/Clima.gdshader");
	private static Shader ShRaio => _shRaio ??= ResourceLoader.Load<Shader>("res://Assets/Shaders/Raio.gdshader");

	public override void _Ready()
	{
		// O NODE E DE MUNDO (as particulas), e ele CARREGA uma camada de tela (o veu e o raio).
		// Um CanvasLayer filho de um Node2D e independente da transformacao do pai, que e
		// exatamente o que se quer: o quad cobre a janela onde quer que a camera esteja.
		_tela = new CanvasLayer { Name = "Tela", Layer = Camada };
		AddChild(_tela);

		_tintaVeu = new ShaderMaterial { Shader = ShVeu };
		_veu = new ColorRect
		{
			Name = "Veu",
			Material = _tintaVeu,
			Color = Colors.White,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_veu.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_tela.AddChild(_veu);

		MontarQueda();

		// O RAIO CAI NUM LUGAR DO MAPA, e por isso ele e de MUNDO como a chuva.
		//
		// A primeira versao o deixou em tela argumentando que coisa distante nao faz paralaxe. O
		// argumento vale pra um relampago no horizonte; nao vale aqui, porque o que se ve e um
		// raio ATINGINDO o chao a alguns tiles de distancia -- e esse desliza junto com a camera
		// de um jeito que denuncia na hora que ele esta colado no olho.
		//
		// Sprite2D com uma textura de 1 pixel esticada: o UV continua indo de 0 a 1 (que e o que
		// o shader espera) e o node vive na arvore do mundo, que e o que se queria.
		_tintaRaio = new ShaderMaterial { Shader = ShRaio };
		_raio = new Sprite2D
		{
			Name = "Raio",
			Texture = UmPixel(),
			Material = _tintaRaio,
			Centered = false,
			Visible = false,
			ZIndex = CamadaDoRaio,
		};
		AddChild(_raio);
	}

	/// <summary>Um pixel branco. Esticado pelo `Scale`, ele e o quad em que o raio e desenhado.</summary>
	private static ImageTexture UmPixel()
	{
		Image img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		img.Fill(Colors.White);
		return ImageTexture.CreateFromImage(img);
	}

	private void MontarQueda()
	{
		_fisica = new ParticleProcessMaterial
		{
			// A CAIXA DE EMISSÃO ACOMPANHA A CÂMERA, e é daqui que sai a propriedade de "só
			// existe o que a câmera vê" -- ver `AjustarCaixa`.
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			Gravity = Vector3.Zero,   // a velocidade é constante: chuva não acelera na tela
			ParticleFlagDisableZ = true,
		};

		_queda = new GpuParticles2D
		{
			Name = "Queda",
			Amount = Pingos,
			ProcessMaterial = _fisica,
			Texture = _pingo,
			Emitting = false,
			// SEM `Explosiveness`: a chuva tem que estar CHEIA no primeiro quadro em que aparece.
			// Com o pool nascendo aos poucos, o começo de cada temporal seria uma garoa que
			// engrossa por cinco segundos toda vez que se troca de zona.
			Preprocess = 3,

			// ============================ A LINHA QUE PRENDE A CHUVA AO CHÃO ============================
			// `LocalCoords = false` faz o pingo, DEPOIS DE SOLTO, viver em coordenada de mundo e
			// ignorar o que acontece com o emissor. O emissor persegue a câmera (senão a chuva só
			// existiria onde o jogador entrou no planeta), mas a água já no ar fica onde estava --
			// então andar para a direita deixa gotas para trás, que é a coisa toda.
			//
			// Com `true`, todo o campo é filho do emissor e viaja com ele: era esse o defeito da
			// primeira versão, só que causado pelo `CanvasLayer` em vez desta flag.
			// ===========================================================================================
			LocalCoords = false,

			ZIndex = CamadaDaChuva,
		};
		AddChild(_queda);
	}

	/// <summary>
	/// O PINGO: uma faixa vertical que some nas duas pontas.
	///
	/// Gerada em código e não importada, como o halo da lua e as texturas de luz -- é um degradê
	/// de 4x24 px, e um asset pra isso seria um arquivo a manter e a reimportar por nada.
	///
	/// AS PONTAS DESBOTADAS são o que dá o risco de chuva: um retângulo chapado de ponta reta lê
	/// como palito caindo, não como água.
	/// </summary>
	private static GradientTexture2D TexturaDePingo()
	{
		var g = new Gradient();
		g.SetOffset(0, 0);
		g.SetColor(0, new Color(1, 1, 1, 0));
		g.AddPoint(0.5f, Colors.White);
		g.SetOffset(2, 1);
		g.SetColor(2, new Color(1, 1, 1, 0));

		return new GradientTexture2D
		{
			Gradient = g,
			Width = 4,
			Height = 24,
			FillFrom = new Vector2(0.5f, 0),
			FillTo = new Vector2(0.5f, 1),
		};
	}

	/// <summary>
	/// O FLOCO: um ponto redondo que desbota na borda.
	///
	/// ============================ POR QUE NEVE NAO PODE USAR A TEXTURA DA CHUVA ============================
	/// Usava. O resultado foi neve em forma de RISCO BRANCO -- e como o floco gira (ao contrario
	/// do pingo, que se alinha à velocidade), os riscos saíam apontando pra todo lado. A tela
	/// ficava com cara de arranhão em filme velho, não de neve.
	///
	/// A forma é o que separa os dois. Pingo é um traço porque cai rápido e a vista o borra;
	/// floco cai devagar e a vista o resolve como um ponto. Nenhum ajuste de tamanho, cor ou
	/// velocidade conserta a forma errada.
	/// =======================================================================================================
	/// </summary>
	private static GradientTexture2D TexturaDeFloco()
	{
		var g = new Gradient();
		g.SetColor(0, Colors.White);
		g.SetColor(1, new Color(1, 1, 1, 0));

		return new GradientTexture2D
		{
			Gradient = g,
			Width = 12,
			Height = 12,
			Fill = GradientTexture2D.FillEnum.Radial,
			FillFrom = new Vector2(0.5f, 0.5f),
			FillTo = new Vector2(1.0f, 0.5f),
		};
	}

	// =====================================================================
	// O QUE CADA CLIMA PARECE
	// =====================================================================
	/// <summary>
	/// A receita visual de um clima. Tudo o que muda entre chuva e nevasca está aqui.
	///
	/// `Escala` é o tamanho de uma célula da massa em PIXELS DE MUNDO (não fração de tela): é o
	/// que faz a nuvem ter o mesmo tamanho no mapa independente da janela e do zoom.
	///
	/// A ORDEM DE GRANDEZA IMPORTA MAIS DO QUE PARECE. A tela cobre ~960x544 px de mundo no zoom
	/// normal; uma célula de 1.300 px faria caber MEIA célula no quadro inteiro. O campo virava
	/// uma cor chapada -- sem estrutura de nuvem, e sem paralaxe visível ao andar, porque
	/// atravessar 300 px de um campo de 1.300 quase não muda o valor amostrado. Foi assim que o
	/// véu parecia estar preso na câmera mesmo já sendo amostrado em coordenada de mundo. O alvo
	/// é de duas a quatro células por tela: 150 px pra poeira fina, ~380 pra nuvem larga.
	///
	/// `Paralaxe` é a altura aparente: 0 gruda na tela, 1 gruda no chão. Nuvem é alta (valor
	/// baixo); neblina e poeira estão na altura do corpo (valor alto).
	/// </summary>
	private readonly record struct Receita(
		bool Cai, Color CorDoPingo, float Velocidade, float Inclinacao, Vector2 Tamanho, float Espalha,
		Color CorDaMassa, float Densidade, float Escala, Vector2 Deriva, float Manchado, float Paralaxe);

	/// <summary>
	/// AS CORES SAÍRAM DA ARTE ORIGINAL (`Assets/Sprites/Weather/Weather.png`, convertida do
	/// `Weather.dmi`). O desenho é nosso, mas a paleta é a do jogo antigo -- é o que faz a chuva
	/// de sangue de Vegeta e a chuva de Namek continuarem reconhecíveis pra quem jogava lá.
	/// </summary>
	private static Receita Do(TipoDeClima t) => t switch
	{
		TipoDeClima.Chuva => new(true, new Color(0.62f, 0.78f, 0.95f, 0.55f), 1500, 0.16f,
								 new Vector2(0.45f, 0.75f), 0.35f,
								 new Color(0.05f, 0.06f, 0.09f), 0.22f, 320, new Vector2(0.05f, 0.012f), 0.85f, 0.40f),

		TipoDeClima.Tempestade => new(true, new Color(0.58f, 0.72f, 0.92f, 0.7f), 2100, 0.34f,
									  new Vector2(0.6f, 1.0f), 0.5f,
									  new Color(0.03f, 0.04f, 0.07f), 0.34f, 340, new Vector2(0.11f, 0.02f), 0.95f, 0.35f),

		// A CHUVA DE SANGUE DE VEGETA. Gota mais gorda e mais lenta: sangue não cai como água, e
		// é a diferença de PESO que faz a cena ler como errada -- que é o ponto dela.
		TipoDeClima.ChuvaDeSangue => new(true, new Color(0.72f, 0.10f, 0.12f, 0.72f), 1150, 0.12f,
										 new Vector2(0.7f, 1.1f), 0.3f,
										 new Color(0.09f, 0.03f, 0.03f), 0.26f, 330, new Vector2(0.04f, 0.01f), 0.85f, 0.40f),

		TipoDeClima.ChuvaDeNamek => new(true, new Color(0.55f, 0.95f, 0.68f, 0.55f), 1350, 0.14f,
										new Vector2(0.45f, 0.75f), 0.35f,
										new Color(0.04f, 0.08f, 0.05f), 0.22f, 320, new Vector2(0.05f, 0.012f), 0.85f, 0.40f),

		// NEVE CAI DEVAGAR E VAGA. O `Espalha` alto é o que dá o flutuar -- floco com direção
		// firme vira chuva branca. A forma é um PONTO, não um risco: ver `TexturaDeFloco`.
		TipoDeClima.Neve => new(true, new Color(1f, 1f, 1f, 0.85f), 260, 0.30f,
								new Vector2(0.3f, 0.55f), 2.2f,
								new Color(0.10f, 0.11f, 0.13f), 0.16f, 360, new Vector2(0.03f, 0.008f), 0.7f, 0.45f),

		// A NEVASCA E CLARA NO AR (ver `Clima.CorDoAr`) e ESCURA no véu: o mundo fica lavado pelo
		// ambiente, e o véu desenha as rajadas mais densas passando por cima dele. Se o véu também
		// fosse claro, as duas coisas somariam e o personagem sumiria no branco.
		TipoDeClima.Nevasca => new(true, new Color(1f, 1f, 1f, 0.9f), 950, 1.05f,
								   new Vector2(0.35f, 0.6f), 1.6f,
								   new Color(0.11f, 0.12f, 0.14f), 0.22f, 260, new Vector2(0.35f, 0.03f), 0.45f, 0.75f),

		// AREIA CORRE DE LADO, quase na horizontal -- é a inclinação que conta a história, não a cor.
		TipoDeClima.Areia => new(true, new Color(0.85f, 0.71f, 0.44f, 0.6f), 1250, 2.6f,
								 new Vector2(0.3f, 0.55f), 0.9f,
								 new Color(0.12f, 0.10f, 0.06f), 0.24f, 150, new Vector2(0.55f, 0.05f), 0.55f, 0.85f),

		// SEM QUEDA daqui pra baixo: são massas, e massa é o shader.
		//
		// A PARALAXE SEPARA O QUE ESTA EM CIMA DO QUE ESTA AQUI. Nuvem é alta e quase não se
		// desloca quando se anda; neblina e fumaça estão na altura do corpo e acompanham o chão.
		TipoDeClima.Nublado => new(false, default, 0, 0, Vector2.One, 0,
								   new Color(0.06f, 0.07f, 0.09f), 0.26f, 380, new Vector2(0.04f, 0.012f), 1f, 0.25f),

		TipoDeClima.Neblina => new(false, default, 0, 0, Vector2.One, 0,
								   new Color(0.10f, 0.11f, 0.12f), 0.20f, 240, new Vector2(0.06f, 0.02f), 0.55f, 0.90f),

		TipoDeClima.Fumaca => new(false, default, 0, 0, Vector2.One, 0,
								  new Color(0.10f, 0.09f, 0.08f), 0.28f, 280, new Vector2(0.09f, 0.025f), 0.75f, 0.80f),

		_ => new(false, default, 0, 0, Vector2.One, 0, Colors.White, 0, 320, Vector2.Zero, 1, 0.5f),
	};

	/// <summary>
	/// O TETO DA OPACIDADE DO VÉU.
	///
	/// ============================ POR QUE ISTO É UM LIMITE E NÃO UM GOSTO ============================
	/// O véu cobre a tela inteira, e a tela inteira inclui o PERSONAGEM. Densidade alta não deixa
	/// a cena "mais fechada": ela baixa o contraste de tudo por igual, e o boneco passa a parecer
	/// meio transparente, sumindo no chão.
	/// =================================================================================================
	/// </summary>
	public const float DensidadeMaxima = 0.36f;

	/// <summary>
	/// ============================ NENHUM VÉU PODE SER CLARO ============================
	/// Isto reapareceu três vezes, sempre com o mesmo sintoma: o personagem "meio transparente",
	/// dando pra ver o chão através dele, e até os olhos do corpo aparecendo sob o sprite de olho.
	/// Nada disso era alfa no personagem -- era o véu.
	///
	/// A conta explica. Misturar a cena com uma cor `c` por `a` dá `dst*(1-a) + c*a`. Com `c`
	/// CLARO, tudo é puxado em direção a ele: o preto sobe, o branco desce, e cada pixel escuro
	/// (o cabelo, a pupila) ganha a cor do véu -- foi assim que os olhos pretos ficaram azuis.
	/// Baixar a opacidade só adia isso.
	///
	/// Com `c` perto do PRETO a mesma conta vira `dst*(1-a)`: uma MULTIPLICAÇÃO. Escurece sem
	/// achatar, e nada perde contraste nem ganha cor emprestada.
	///
	/// E O CLIMA CLARO? Nevasca e neblina de fato ESPALHAM luz, e lavar a cena é o efeito certo
	/// pra elas -- mas isso agora é feito onde é seguro: na COR DO AR (`Clima.CorDoAr`), que
	/// MULTIPLICA o ambiente e pode passar de 1. Um whiteout fica claro sem que uma única camada
	/// do personagem perca opacidade. O véu ficou só com a textura se mexendo, e pra isso ele não
	/// precisa -- nem pode -- ser claro.
	/// ===================================================================================
	/// </summary>
	public const float LimiteDeSombra = 0.18f;

	/// <summary>Este clima tem algo CAINDO? Nublado, neblina e fumaça são só massa no ar.</summary>
	public static bool Precipita(TipoDeClima t) => Do(t).Cai;

	/// <summary>Quão clara é a massa deste clima, de 0 (preta) a 1 (branca).</summary>
	public static float ClaridadeDaMassa(TipoDeClima t)
	{
		Color c = Do(t).CorDaMassa;
		return Mathf.Max(c.R, Mathf.Max(c.G, c.B));
	}

	// =====================================================================
	// APLICAR
	// =====================================================================
	/// <summary>
	/// PÕE O CLIMA NA TELA. Chamado a cada quadro pela <see cref="Iluminacao"/>, que é quem tem o
	/// relógio -- este node não conta tempo de mundo de propósito, pelo mesmo motivo da lua: dois
	/// relógios pro mesmo céu saem de sincronia sem ninguém notar.
	/// </summary>
	/// <param name="ambiente">
	/// A cor do ambiente agora. O véu é de tela e por isso escapa do <see cref="CanvasModulate"/>
	/// -- sem multiplicá-lo à mão, uma neblina branca continuaria branca à meia-noite, brilhando
	/// no escuro como se tivesse luz própria.
	/// </param>
	public void Aplicar(EstadoDoClima clima, double delta, Color ambiente)
	{
		_estado = clima;
		_t += delta;

		// O RETÂNGULO DE MUNDO QUE A CÂMERA ALCANÇA. Mesma conta do `CeuDoEspaco`: com o zoom, a
		// janela cobre mais ou menos planeta, e é isso que o clima precisa saber pra existir no
		// lugar certo em vez de no pixel certo.
		Camera2D? cam = GetViewport()?.GetCamera2D();
		Vector2 tela = GetViewportRect().Size;
		Vector2 mundo = cam != null ? tela / cam.Zoom : tela;
		Vector2 centro = cam?.GetScreenCenterPosition() ?? GlobalPosition;
		Vector2 origem = centro - mundo * 0.5f;
		OrigemDoCampo = origem;

		Receita r = Do(clima.Tipo);
		var forca = (float)clima.Forca;

		if (clima.Tipo != _desenhado)
		{
			_desenhado = clima.Tipo;
			Reconfigurar(r);
		}

		// ---- a massa ----
		_tintaVeu.SetShaderParameter("tempo", (float)_t);
		_tintaVeu.SetShaderParameter("forca", forca);
		_tintaVeu.SetShaderParameter("cor", r.CorDaMassa * ambiente);
		_tintaVeu.SetShaderParameter("densidade", r.Densidade);
		_tintaVeu.SetShaderParameter("escala", r.Escala);
		_tintaVeu.SetShaderParameter("deriva", r.Deriva);
		_tintaVeu.SetShaderParameter("manchado", r.Manchado);
		_tintaVeu.SetShaderParameter("clarao", _clarao);
		// ONDE ESTE PEDACO DE CEU ESTA NO PLANETA -- e o que prende a nuvem ao mapa
		_tintaVeu.SetShaderParameter("origem", origem);
		_tintaVeu.SetShaderParameter("tamanho", mundo);
		_tintaVeu.SetShaderParameter("paralaxe", r.Paralaxe);

		// ---- o que cai ----
		_queda.Emitting = r.Cai && forca > 0.02f;
		if (_queda.Emitting)
		{
			// A FRAÇÃO EMITIDA É O VOLUME DA CHUVA. Mexer no `Amount` recriaria o pool inteiro na
			// GPU a cada quadro do fade; `AmountRatio` só muda quantos do pool fixo estão vivos.
			_queda.AmountRatio = Mathf.Clamp(forca, 0.05f, 1f);
			_queda.Modulate = new Color(1, 1, 1, forca);
			AjustarCaixa(origem, mundo, r);
		}

		// ---- o raio ----
		// Só o RELÓGIO dele mora aqui; quem manda cair é o servidor (ver `Estourar`).
		Raio(delta);
	}

	private void Reconfigurar(Receita r)
	{
		if (!r.Cai) { _queda.Emitting = false; return; }

		_fisica.Direction = new Vector3(r.Inclinacao, 1, 0).Normalized();
		_fisica.Spread = r.Espalha * 12f;
		_fisica.InitialVelocityMin = r.Velocidade * 0.85f;
		_fisica.InitialVelocityMax = r.Velocidade * 1.15f;
		_fisica.ScaleMin = r.Tamanho.X;
		_fisica.ScaleMax = r.Tamanho.Y;
		_fisica.Color = r.CorDoPingo;

		// A NEVE GIRA e a chuva não: floco tem cara, pingo é um risco. Sem o giro, neve fica
		// parecendo confete colado no ar.
		bool floco = _desenhado is TipoDeClima.Neve or TipoDeClima.Nevasca;

		// E CADA UM TEM A PROPRIA FORMA. Ver `TexturaDeFloco` -- neve com a textura da chuva
		// virava um campo de riscos brancos apontando pra todo lado.
		_queda.Texture = floco ? _floco : _pingo;

		// ============================ A TURBULENCIA PRENDIA A NEVE NO TOPO ============================
		// A neve era o unico tipo com `TurbulenceEnabled`, e era o unico que nao atravessava a
		// tela: os flocos ficavam presos numa faixa em cima enquanto a chuva descia inteira. O
		// campo de turbulencia do Godot aplica forca continua e, num corpo lento como o floco
		// (190 px/s contra os 1.500 do pingo), ele deixa de ser um empurraozinho e vira quem manda
		// -- o floco fica orbitando a celula de ruido em vez de cair.
		//
		// O bamboleio que a turbulencia deveria dar sai mais barato do `Spread` e do giro, que ja
		// estao aqui e nao competem com a gravidade.
		// =============================================================================================
		_fisica.TurbulenceEnabled = false;

		// O PINGO SE ALINHA À PRÓPRIA VELOCIDADE (o floco não -- ele gira). É o que faz a chuva
		// inclinada e a areia lateral terem o risco APONTANDO pra onde vão, em vez de gotas
		// verticais viajando de lado. Com o emissor em coordenada de mundo isto passou a ser a
		// única forma de inclinar o traço: girar o node não serve, porque a partícula já solta
		// não é mais filha dele.
		_fisica.ParticleFlagAlignY = !floco;

		_fisica.AngularVelocityMin = floco ? -90 : 0;
		_fisica.AngularVelocityMax = floco ? 90 : 0;
	}

	/// <summary>
	/// A CAIXA DE EMISSÃO PERSEGUE A CÂMERA, em coordenada de MUNDO.
	///
	/// ============================ O EMISSOR ANDA, A CHUVA NÃO ============================
	/// Este é o par da flag `LocalCoords = false`. O emissor precisa seguir o jogador (senão só
	/// choveria no ponto em que ele entrou no planeta), mas o pingo já solto vive em coordenada de
	/// mundo e fica pra trás. O resultado é o que se quer: a água ocupa o LUGAR, e andar mostra
	/// água nova à frente e deixa a de trás para trás.
	/// =====================================================================================
	///
	/// Nasce ACIMA do topo e mais larga que a tela: um pingo que nasce dentro do quadro aparece do
	/// nada no meio da imagem, e com a chuva inclinada a lateral de onde ela vem fica vazia se a
	/// caixa terminar na borda. A folga lateral segue a inclinação -- e sobra um pouco mais, que é
	/// o que cobre a beirada seca de quem corre para o lado enquanto a gota ainda está caindo.
	/// </summary>
	private void AjustarCaixa(Vector2 origem, Vector2 mundo, Receita r)
	{
		float folga = mundo.X * 0.35f + Mathf.Abs(r.Inclinacao) * mundo.Y;

		// o topo da faixa fica acima da borda visível, deslocado contra a inclinação: é de lá que
		// a gota precisa partir pra entrar no quadro no lugar certo
		_queda.GlobalPosition = new Vector2(
			origem.X + mundo.X * 0.5f - r.Inclinacao * mundo.Y * 0.5f,
			origem.Y - AlturaDeSaida);

		// SÓ QUANDO MUDA DE VERDADE. Estes dois só dependem do tamanho da janela e do tipo de
		// clima, mas ficavam sendo reescritos a cada quadro; reatribuir propriedade de sistema de
		// partícula em laço de desenho é o tipo de coisa que às vezes reinicia o pool e sempre
		// custa à toa. A posição acima é a única que precisa andar todo quadro.
		float vida = Mathf.Max(0.35f, (mundo.Y + AlturaDeSaida * 2) / Mathf.Max(r.Velocidade, 1) * 1.4f);
		var caixa = new Vector3((mundo.X + folga) * 0.5f, 8, 0);

		if (Mathf.Abs(_queda.Lifetime - vida) > 0.01f) _queda.Lifetime = vida;
		if (!_fisica.EmissionBoxExtents.IsEqualApprox(caixa)) _fisica.EmissionBoxExtents = caixa;
	}

	/// <summary>Quanto acima da borda de cima a faixa de emissão fica, em pixels de mundo.</summary>
	private const float AlturaDeSaida = 80;

	// =====================================================================
	// O RAIO
	// =====================================================================
	/// <summary>
	/// A QUE VELOCIDADE O TROVÃO VIAJA, em pixels de mundo por segundo.
	///
	/// Não é a do som de verdade -- na escala do jogo (um tile ≈ um metro) o som real cruzaria a
	/// tela em cinco centésimos de segundo e o atraso não existiria pro ouvido. Este número é
	/// escolhido pra que o atraso seja LEGÍVEL: um raio a dez tiles estala quase junto, um a
	/// quarenta demora quase um segundo e meio. É a única pista de escala que uma tela de cima
	/// consegue dar do tamanho de uma tempestade.
	/// </summary>
	private const float VelocidadeDoTrovao = 900f;

	/// <summary>Até que distância o clarão de um raio ainda ilumina a cena, em pixels de mundo.</summary>
	private const float AlcanceDoClarao = 60 * 32;

	private void Raio(double delta)
	{
		_clarao = Mathf.Max(0, _clarao - (float)delta * 3.5f);

		if (_idadeDoRaio >= 1) return;
		_idadeDoRaio = Math.Min(1, _idadeDoRaio + delta * 3.0);
		_tintaRaio.SetShaderParameter("idade", (float)_idadeDoRaio);
		if (_idadeDoRaio >= 1) _raio.Visible = false;
	}

	/// <summary>
	/// CAIU UM RAIO EM `onde`. Chamado pelo servidor, pela zona inteira -- ver `S2C.Raio`.
	///
	/// ============================ VER O RISCO OU SÓ OUVIR O TROVÃO ============================
	/// O jogador com a câmera naquele pedaço do mapa vê a descarga cair. Quem não está olhando pra
	/// lá recebe o mesmo acontecimento pelos outros dois canais: o clarão, que lava a cena inteira
	/// e por isso não depende de enquadramento, e o trovão, que chega atrasado conforme a
	/// distância. Os três saem do MESMO pacote, então ninguém ouve um trovão que não houve.
	///
	/// A SEMENTE VEM JUNTO pra que dois jogadores olhando pro mesmo ponto vejam o MESMO risco. Sem
	/// ela cada um sortearia um zigue-zague, e a tempestade deixaria de ser uma coisa só.
	/// ==========================================================================================
	/// </summary>
	public void Estourar(Vector2 onde, float semente)
	{
		var rnd = new RandomNumberGenerator { Seed = (ulong)Mathf.Abs(semente * 1000f) };

		Camera2D? cam = GetViewport()?.GetCamera2D();
		Vector2 tela = GetViewportRect().Size;
		Vector2 mundo = cam != null ? tela / cam.Zoom : tela;
		Vector2 centro = cam?.GetScreenCenterPosition() ?? GlobalPosition;

		// ---- dá pra ver daqui? ----
		// A folga lateral existe porque o risco é largo: um raio logo fora da borda ainda tem
		// pedaço dentro do quadro, e recortá-lo no limite exato faria descargas aparecerem pela
		// metade e sumirem de repente.
		float folga = mundo.X * 0.35f;
		bool naTela = Mathf.Abs(onde.X - centro.X) < mundo.X * 0.5f + folga
				   && Mathf.Abs(onde.Y - centro.Y) < mundo.Y * 0.9f;

		float dist = onde.DistanceTo(centro);

		if (naTela)
		{
			// UM QUAD ESTREITO E ALTO. O zigue-zague precisa de espaço lateral pra virar, mas um
			// quad largo faz o risco parecer curto e gordo -- a proporção é que dá a leitura de
			// "isto veio de muito longe, lá de cima".
			float largura = mundo.X * (0.10f + rnd.Randf() * 0.09f);
			float altura = mundo.Y * (0.65f + rnd.Randf() * 0.30f);

			// O PÉ DO RISCO CAI NO PONTO, e o resto sobe de lá. É o que faz a descarga atingir o
			// lugar de que o servidor falou em vez de um ponto qualquer da coluna.
			_raio.GlobalPosition = new Vector2(onde.X - largura * 0.5f, onde.Y - altura);
			_raio.Scale = new Vector2(largura, altura);   // a textura tem 1 px: a escala É o tamanho
			_raio.Visible = true;

			_tintaRaio.SetShaderParameter("idade", 0f);
			_tintaRaio.SetShaderParameter("semente", semente);
			_tintaRaio.SetShaderParameter("desvio", 0.65f + rnd.Randf() * 0.35f);
			_tintaRaio.SetShaderParameter("alcance", 0.8f + rnd.Randf() * 0.2f);
			// A RAZÃO VAI PRO SHADER porque o UV de um quad estreito não é quadrado: sem corrigir,
			// a espessura sai achatada e os cantos do zigue-zague viram curvas esticadas.
			_tintaRaio.SetShaderParameter("razao", altura / Mathf.Max(largura, 1));
			_tintaRaio.SetShaderParameter("grossura", 0.011f + rnd.Randf() * 0.007f);
			_idadeDoRaio = 0;
		}

		// ---- o clarão, que não depende de enquadramento ----
		// Ele é a luz que a descarga joga no céu, e o céu está em cima de todo mundo. Só enfraquece
		// com a distância: um raio a sessenta tiles não acende mais nada por aqui.
		float longe = Mathf.Clamp(1f - dist / AlcanceDoClarao, 0, 1);
		_clarao = Mathf.Max(_clarao, (0.30f + 0.55f * longe) * (naTela ? 1f : 0.55f));
		_ultimoRaio = dist;

		// ---- o trovão ----
		float atraso = dist / VelocidadeDoTrovao;
		float volume = 0.14f + 0.32f * longe;
		GetTree().CreateTimer(atraso).Timeout += () =>
			AudioDirector.Instance?.Efeito(
				rnd.Randf() < 0.5f ? "res://Assets/Sounds/Effects/thunderclap.ogg"
								   : "res://Assets/Sounds/Effects/thunderclap2.ogg",
				volume);
	}

	/// <summary>A que distância caiu o último raio, em pixels. A bancada lê daqui.</summary>
	public float UltimoRaio => _ultimoRaio;

	private float _ultimoRaio = -1;

	/// <summary>O clima que está sendo desenhado. A bancada lê daqui.</summary>
	public EstadoDoClima Estado => _estado;

	/// <summary>Quantos pingos estão vivos agora -- pro diagnóstico provar que o custo é fixo.</summary>
	public int PingosVivos => _queda.Emitting ? (int)(Pingos * _queda.AmountRatio) : 0;

	/// <summary>Onde o emissor está, em coordenada de mundo. A bancada confere que ele segue a câmera.</summary>
	public Vector2 EmissorEm => _queda.GlobalPosition;

	/// <summary>
	/// A gota já solta vive no MUNDO e não no emissor? É a propriedade que faz a chuva ficar no
	/// lugar enquanto o jogador anda -- e a única que, se alguém a inverter um dia, devolve
	/// exatamente o defeito que este arquivo veio consertar.
	/// </summary>
	public bool PrendeNoMundo => !_queda.LocalCoords;

	/// <summary>O canto do mundo que o véu está amostrando. Muda quando a câmera anda.</summary>
	public Vector2 OrigemDoCampo { get; private set; }

	/// <summary>
	/// O raio vive na árvore do MUNDO e não na camada de tela?
	///
	/// Ele já esteve em tela, com o argumento de que coisa distante não faz paralaxe. Não colou:
	/// o que se vê é um raio atingindo o chão a alguns tiles, e esse desliza junto com a câmera.
	/// </summary>
	public bool RaioNoMundo => _raio.GetParent() == this;

	/// <summary>
	/// A FORMA do que cai neste clima. A bancada usa pra provar que neve e chuva não compartilham
	/// desenho -- foi assim que a neve saiu como um campo de riscos brancos.
	/// </summary>
	public static Texture2D FormaDe(TipoDeClima t) =>
		t is TipoDeClima.Neve or TipoDeClima.Nevasca ? _floco : _pingo;

	/// <summary>
	/// QUANTO DE MUNDO UMA TELA DE JOGO COBRE, em pixels -- a largura, no zoom normal.
	///
	/// É o mesmo número que o <see cref="CeuDoEspaco"/> cita ("uma tela em zoom 2 tem ~30x17
	/// tiles"), ou seja, 30 x 32 px. Vale como CONSTANTE e não como medida ao vivo porque a
	/// bancada roda em `--headless`, onde a janela tem 64 px e qualquer comparação com o viewport
	/// real passaria ou falharia por motivo nenhum.
	/// </summary>
	public const float MundoVisivelTipico = 30 * 32;

	/// <summary>
	/// A maior célula de ruído de todas as receitas, em pixels de mundo. A bancada compara com
	/// <see cref="MundoVisivelTipico"/> -- célula maior que o quadro é véu sem estrutura e sem
	/// paralaxe visível.
	/// </summary>
	public static float MaiorCelula
	{
		get
		{
			float maior = 0;
			foreach (TipoDeClima t in Enum.GetValues<TipoDeClima>()) maior = Mathf.Max(maior, Do(t).Escala);
			return maior;
		}
	}

	/// <summary>A maior opacidade de véu de todas as receitas. Ver <see cref="DensidadeMaxima"/>.</summary>
	public static float MaiorDensidade
	{
		get
		{
			float maior = 0;
			foreach (TipoDeClima t in Enum.GetValues<TipoDeClima>()) maior = Mathf.Max(maior, Do(t).Densidade);
			return maior;
		}
	}

	/// <summary>Algum clima usa turbulência? Ver o comentário em <see cref="Reconfigurar"/>.</summary>
	public bool UsaTurbulencia => _fisica.TurbulenceEnabled;

	/// <summary>
	/// Quanto o que cai neste clima percorre antes de morrer, em pixels de mundo. Tem que ser
	/// bem mais que a altura da tela, senão a precipitação morre no ar a meio caminho.
	/// </summary>
	public static float QuedaDe(TipoDeClima t)
	{
		Receita r = Do(t);
		if (!r.Cai) return float.MaxValue;
		float vida = Mathf.Max(0.35f, (MundoVisivelTipico * 0.567f + AlturaDeSaida * 2) / Mathf.Max(r.Velocidade, 1) * 1.4f);
		return r.Velocidade * vida;
	}
}
