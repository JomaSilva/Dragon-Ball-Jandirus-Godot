using Godot;

namespace Jandirus.Client;

/// <summary>
/// OS RAIOZINHOS QUE CORREM NO CORPO DE QUEM ESTA TRANSFORMADO.
///
/// Porte do `updateOverlay(/obj/overlay/effects/electrictyeffects)` do BYOND
/// (`supersaiyanbuff.dm:221-257`).
///
/// SAO SETE FORMAS NO JOGO INTEIRO -- `ssj2`, `ssj3`, `primal_legendary2`, `primal_legendary3`,
/// `ssj4_limit_breaker` e, desde o pedido do dono, os dois da linha do Mistico (`mistico` e
/// `beast`, que no original vestem OUTRA folha -- `Electric_Mystic.dmi`, `Mystic.dm:37` e `:112`).
/// Quem manda e o `FormaDef.Raios`, e o cabecalho dele explica quais degraus do DM ficaram de fora.
/// A COR nao esta aqui: quatro sao azuis, o Limit Breaker e VERMELHO e os dois do Mistico sao
/// BRANCOS, e quem responde isso e o `Catalogo.CorDosRaios` -- este node so recebe a cor pronta em
/// `Definir`.
///
/// ============================ POR QUE PARTICULA E NAO OVERLAY ANIMADO ============================
/// La e uma folha de quadros ciclando sobre o sprite. Aqui sao particulas com um shader que DESENHA
/// cada raio a partir da posicao em que ele nasceu. Tres coisas mudam, e as tres importam:
///
///   1. **Ninguem pisca junto.** Dez Super Saiyajin numa luta com o overlay do original viram um
///      estroboscopio sincronizado -- todos os corpos rodam os mesmos quadros na mesma ordem. Com a
///      posicao como semente, dois raios nunca saem iguais.
///   2. **A cor e um parametro.** O raio do Blue e azul sem pedir arte nova; o do Rose e rosa. No
///      original, cada cor exigiria outra folha .dmi (e o SSJ3 de la ja precisou de duas:
///      `ElecAura1.dmi` e `Electric_Blue.dmi`).
///   3. **O volume escala com a forma.** `Intensidade` muda quantos raios existem e o quanto eles
///      serpenteiam, entao a diferenca entre o SSJ2 (uma folha no DM) e o SSJ3 (tres) se ve sem
///      contar tiers.
/// ==========================================================================================
///
/// O NODE EXISTE SEMPRE E NASCE DESLIGADO -- a mesma regra da <see cref="Aura"/>: montar
/// GpuParticles2D no meio de uma transformacao custaria um engasgo bem no quadro em que o jogador
/// esta olhando pro proprio personagem.
/// </summary>
public partial class RaiosDaForma : Node2D
{
	private const string CaminhoDoShader = "res://Assets/Shaders/RaioDaForma.gdshader";

	/// <summary>
	/// ACIMA DO SPRITE. Foi pedido explicitamente ("raiozinhos perto do personagem por cima do
	/// sprite dele"), e e o certo: o raio corre POR FORA do corpo. Abaixo, o desenho o esconderia
	/// justamente onde ele mais aparece.
	///
	/// Fica abaixo do balao de fala (21) e acima da aura, que e o fundo da cena do corpo.
	/// </summary>
	private const int Camada = 6;

	/// <summary>
	/// QUANTOS RAIOS PODEM SAIR NA MESMA RAJADA, no maximo.
	///
	/// ============================ O RITMO E RAJADA, NAO GOTEJO ============================
	/// Duas versoes antes desta erraram por motivos opostos, e vale registrar as duas:
	///
	///   1. **25 particulas com vida de 0,34 s.** No Godot a taxa de emissao continua e
	///      `Amount / Lifetime` -- eram SETENTA raios por segundo. O corpo virava uma bola.
	///   2. **1 particula com vida de 2 s e o shader descartando 85%.** Rareou certo, mas o raio
	///      saia sempre SOZINHO, e o ritmo ficou escondido dentro do shader.
	///
	/// O que uma descarga parece de verdade e uma RAJADA: um punhado de raios ao mesmo tempo, e
	/// depois silencio. Entao o emissor virou `OneShot` e quem conta os segundos e o relogio deste
	/// node -- que a cada disparo sorteia **de 1 a 4** raios e um intervalo com folga.
	/// ==================================================================================
	///
	/// ERA 5, E CAIU JUNTO COM A INTENSIDADE 3. O pool tem que ser exatamente o teto da intensidade
	/// mais alta, e a mais alta hoje e 2. O Limit Breaker VOLTOU a soltar faisca depois disso e nao
	/// devolveu o quinto lugar: ele voltou em 2, porque no DM a folha propria dele sao DUAS
	/// (`ssj4lb_sparks` + `ssj4lb_lightning`) contra as tres do SSJ3 -- ver `FormaDef.Raios`.
	/// Deixar o 5 aqui seria alocar na GPU um raio que o catalogo nao pode pedir -- e, pior, faria o
	/// `AmountRatio` nunca chegar a 1 nem na forma mais cheia.
	///
	/// O pool e alocado UMA vez, no maximo -- `Amount` nao pode mudar em runtime sem recriar o
	/// buffer, e recriar no meio da luta e exatamente o engasgo que este node existe pra evitar.
	/// O sorteio mexe no `AmountRatio`, que so muda quantos daquele pool saem na rajada.
	/// </summary>
	private const int Maximo = 4;

	/// <summary>
	/// QUANTO TEMPO UM RAIO FICA NA TELA. E a vida da particula, e mais nada.
	///
	/// Quase um segundo, e nao os 0,35 s de antes: o raio agora tem uma HISTORIA -- nasce, se
	/// contorce, estroba e se parte em cacos que sobem. Com um terco de segundo nada disso dava
	/// tempo de ser visto, e o efeito virava um lampejo.
	/// </summary>
	private const double DuracaoDoRaio = 0.9;

	/// <summary>
	/// O INTERVALO ENTRE RAJADAS, em segundos -- sorteado dentro desta faixa a cada disparo.
	///
	/// A folga e o que evita o metronomo: com intervalo fixo o olho aprende o compasso em tres
	/// ciclos e a eletricidade vira pisca-pisca. Dois segundos e o centro, que foi o pedido.
	/// </summary>
	private const double IntervaloMin = 1.3, IntervaloMax = 2.7;

	/// <summary>
	/// TAMANHO DO QUAD DE UM RAIO, em pixels. Um corpo tem 32 px de largura.
	///
	/// MAIS LARGO QUE A PRIMEIRA VERSAO (era 10) porque agora alguns raios saem em ARCO: o "C"
	/// precisa de espaco lateral pra a barriga dele caber. Num quad estreito o arco encosta na
	/// borda e sai cortado, que e pior do que nao ter arco.
	/// </summary>
	private static readonly Vector2 TamanhoDoRaio = new(16, 24);

	/// <summary>
	/// A caixa em que os raios nascem: em volta do TRONCO, nao do chao. (Meias-extensoes.)
	///
	/// ============================ APERTADA DUAS VEZES ============================
	/// 20x26 na primeira versao: os raios nasciam a meio corpo de distancia e pareciam do CENARIO.
	/// 11x17 na segunda: melhor, mas o dono ainda viu raio longe demais -- e a conta explica por
	/// que. O que afasta o raio nao e so a caixa; e a caixa MAIS a deriva da subida MAIS meia
	/// altura do quad. Com 17 de caixa, 23 px de deriva e um quad de 24, um raio podia terminar a
	/// mais de 50 px do centro -- um corpo e meio de distancia.
	///
	/// Agora os tres termos foram cortados juntos (ver <see cref="AlcanceMaximoDeTeste"/>, que soma
	/// os tres e a bancada confere). Mexer so num deles deixaria o problema pela metade.
	/// =========================================================================
	/// </summary>
	private static readonly Vector3 CaixaDeEmissao = new(7, 11, 1);

	private GpuParticles2D _fogo = null!;
	private ShaderMaterial _tinta = null!;
	private ParticleProcessMaterial _fisica = null!;

	private bool _ligado;
	private int _intensidade;

	/// <summary>Quanto falta pra a proxima rajada. Ver <see cref="_Process"/>.</summary>
	private double _ateARajada;

	/// <summary>
	/// UM SORTEADOR POR CORPO, com semente propria.
	///
	/// Nao e o `randi()` global de proposito: dez corpos transformados na tela sorteando do mesmo
	/// gerador em quadros consecutivos e o caminho mais curto pra eles caírem em sincronia -- que e
	/// exatamente o estroboscopio que este arquivo inteiro existe pra evitar.
	/// </summary>
	private readonly RandomNumberGenerator _sorte = new();

	/// <summary>
	/// O QUAO LONGE DO CENTRO DO CORPO um raio pode chegar, em pixels.
	///
	/// Soma os TRES termos que afastam: meia-caixa de emissao, a deriva da subida ao longo da vida,
	/// e meia altura do quad ja escalado. Existe porque foi exatamente essa soma que me escapou --
	/// eu apertei a caixa e achei que tinha resolvido, e a deriva e o tamanho do quad continuavam
	/// jogando o raio pra longe.
	/// </summary>
	public float AlcanceMaximoDeTeste =>
		CaixaDeEmissao.Y
		+ (float)(_fisica.InitialVelocityMax * DuracaoDoRaio)
		+ TamanhoDoRaio.Y * 0.5f * _fisica.ScaleMax;

	/// <summary>O emissor esta mesmo emitindo? Pra bancada -- `OneShot` desliga sozinho ao acabar.</summary>
	public bool EmitindoDeTeste => _fogo.Emitting;

	/// <summary>Solta uma rajada AGORA. So pra bancada: em jogo quem dispara e o relogio.</summary>
	public void DispararDeTeste() => Disparar();

	/// <summary>Quantos raios sairam na ULTIMA rajada. Pra bancada -- ver `--diagforma`.</summary>
	public int UltimaRajadaDeTeste { get; private set; }

	/// <summary>Quantas rajadas ja sairam. Pra a bancada saber que o relogio esta correndo.</summary>
	public int RajadasDeTeste { get; private set; }

	/// <summary>Teto de raios simultaneos nesta intensidade. Pra bancada.</summary>
	public int VivosDeTeste => _ligado ? TetoDaIntensidade() : 0;

	/// <summary>
	/// DE QUE COR A FAISCA ESTA SAINDO AGORA -- lida do UNIFORM, e nao de um campo guardado.
	///
	/// Derivada de proposito: um `_cor` guardado aqui responderia o que <see cref="Definir"/> PEDIU,
	/// e um dos jeitos de este efeito quebrar e a cor nao CHEGAR ao shader -- escrever num uniform
	/// que nao existe e silencioso no Godot (e o motivo de o bloco `Shaders()` da bancada conferir os
	/// nomes um a um). Quem desenha e o uniform, entao e ele que responde.
	///
	/// SO VALE COM O NODE LIGADO: `Definir(false, ...)` sai antes de pintar, entao com a faisca
	/// apagada isto devolve a cor da ULTIMA forma acesa. Quem le pergunta <see cref="VivosDeTeste"/>
	/// antes.
	/// </summary>
	public Color CorDeTeste => _tinta.GetShaderParameter("cor").AsColor();

	/// <summary>Com que intensidade a faisca foi ligada. 0 = apagada. Pra bancada.</summary>
	public int IntensidadeDeTeste => _ligado ? _intensidade : 0;

	/// <summary>
	/// A GROSSURA BASE QUE CHEGOU AO SHADER -- lida do uniform pelo mesmo motivo de <see cref="CorDeTeste"/>.
	/// NAO e a grossura na tela: o shader ainda multiplica por `afinar` e pelo sorteio por particula.
	/// </summary>
	public float GrossuraBaseDeTeste => _tinta.GetShaderParameter("grossura").AsSingle();

	/// <summary>
	/// ESTE NODE ESCREVEU ALGUM DOS DOIS BOTOES DA GROSSURA?
	///
	/// Tem que ser NAO. Os dois (`afinar` e `variacao_grossura`) existem pra o dono afinar de olho
	/// mexendo no .gdshader, sem recompilar -- e um `SetShaderParameter` aqui apagaria o padrao do
	/// arquivo em silencio: o efeito continuaria desenhando, so que o botao pararia de ter efeito.
	/// E o mesmo defeito que a rampa da nebulosa ja guarda, e ele nao aparece em foto nenhuma.
	///
	/// Um uniform que o material nunca escreveu volta como `Nil` -- e dai que o valor vem do shader.
	/// </summary>
	public bool BotoesDaGrossuraIntactosDeTeste =>
		_tinta.GetShaderParameter("afinar").VariantType == Variant.Type.Nil
		&& _tinta.GetShaderParameter("variacao_grossura").VariantType == Variant.Type.Nil;

	/// <summary>
	/// A FAIXA DE ESCALA QUE O EMISSOR SORTEIA POR PARTICULA -- (min, max). Pra bancada.
	///
	/// Ela e a SEGUNDA fonte de variacao de grossura, e a unica que existia antes dos dois botoes: a
	/// escala multiplica o quad inteiro, entao um raio maior sai grosso E comprido. Sem este par a
	/// bancada nao consegue medir a grossura em pixel de mundo -- so a fracao de UV, que nao e o que
	/// o dono ve --, e nao consegue separar a variacao NOVA (por raio) da que ja vinha do tamanho.
	/// </summary>
	public Vector2 EscalaDaParticulaDeTeste => new((float)_fisica.ScaleMin, (float)_fisica.ScaleMax);

	/// <summary>
	/// A LARGURA DO QUAD DE UM RAIO em pixels de mundo, ANTES da escala da particula. Pra bancada.
	///
	/// E o que converte a `grossura` (que e fracao de UV) em pixel: a meia-largura do nucleo na tela e
	/// `grossura x esta largura x escala`. Exposta em vez de repetida na bancada porque repetir um 16
	/// la faria a medida continuar dizendo 1,09 px no dia em que o quad mudasse de tamanho.
	/// </summary>
	public static float LarguraDoQuadDeTeste => TamanhoDoRaio.X;

	/// <summary>
	/// RAIOS POR SEGUNDO, em media -- o numero que responde ao pedido ("um a cada 2 segundos").
	///
	/// E a media do sorteio dividida pela media do intervalo. Foi a conta que faltava na primeira
	/// versao: eu media "quantas particulas existem" e nao "com que frequencia elas saem".
	/// </summary>
	public float PorSegundoDeTeste =>
		_ligado ? (float)((1 + TetoDaIntensidade()) / 2.0 / ((IntervaloMin + IntervaloMax) / 2.0)) : 0;

	/// <summary>
	/// Quantos raios no MAXIMO por rajada nesta intensidade. O minimo e sempre 1: uma rajada vazia
	/// seria indistinguivel do efeito desligado.
	///
	/// SAO DOIS DEGRAUS, e nao tres: o `3` morreu quando o Limit Breaker perdeu a faisca, e nao voltou
	/// com ela (ele reentrou em 2 -- ver `FormaDef.Raios`). O `1` continua valendo 2 raios (o SSJ2,
	/// sozinho, e ele e quem responde pelo "um raio a cada 2 segundos" que o dono pediu) e o `2` leva
	/// o pool inteiro (os outros quatro).
	/// </summary>
	private int TetoDaIntensidade() => _intensidade == 1 ? 2 : Maximo;

	public override void _Ready()
	{
		_tinta = new ShaderMaterial { Shader = GD.Load<Shader>(CaminhoDoShader) };

		_fisica = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = CaixaDeEmissao,
			Gravity = Vector3.Zero,          // raio nao cai: ele estala no lugar
			ParticleFlagDisableZ = true,

			// QUASE PARADO. O raio nao viaja -- ele aparece, estala e some. Um resto de deriva
			// pra cima da a sensacao de energia subindo, que e o que a aura tambem faz.
			// OS CACOS SOBEM -- mas pouco. A subida so importa depois que o raio comeca a se
			// partir, pra os pedacos derivarem em vez de apagarem no lugar. Com 26, ao longo dos
			// 0,9 s de vida o caco andava 23 px e terminava longe do corpo; com 14, anda 12.
			InitialVelocityMin = 4,
			InitialVelocityMax = 14,
			Direction = new Vector3(0, -1, 0),
			Spread = 55,

			// O GIRO E O QUE FAZ O RAIO NAO SER SEMPRE VERTICAL. Sem ele, todos correriam de cima
			// pra baixo e a leitura viraria "chuva", nao "descarga".
			AngleMin = -70,
			AngleMax = 70,

			ScaleMin = 0.55f,
			ScaleMax = 1.1f,   // reescrito por intensidade em `Definir`
		};

		_fogo = new GpuParticles2D
		{
			Name = "Fogo",
			Amount = Maximo,
			ProcessMaterial = _fisica,
			Material = _tinta,
			Texture = Quad(),
			Lifetime = DuracaoDoRaio,
			// ============================ RAJADA, E SO QUANDO MANDAREM ============================
			// `OneShot` = o emissor nao roda sozinho: ele dispara UMA leva quando recebe `Restart()`
			// e para. `Explosiveness = 1` = a leva inteira sai no MESMO instante, que e o que faz
			// dela uma descarga e nao um gotejo.
			//
			// O `Randomness` continua alto pra a vida e a escala variarem DENTRO da rajada -- os
			// raios saem juntos, mas nao morrem juntos.
			// ==================================================================================
			OneShot = true,
			Explosiveness = 1,
			Randomness = 0.7f,
			Emitting = false,
			LocalCoords = true,    // os raios ANDAM COM O CORPO -- eles sao do corpo, nao do mundo
			ZIndex = Camada,
			ZAsRelative = false,   // ver o cabecalho: sair da camada do pai e o ponto
			Visible = false,
		};
		AddChild(_fogo);
	}

	/// <summary>
	/// LIGA, DESLIGA E PINTA. <paramref name="intensidade"/> 0 apaga; 1 e o crepitar do SSJ2 e 2 o das
	/// outras quatro. Nao ha mais nada acima -- ver `FormaDef.Raios`.
	/// </summary>
	public void Definir(bool ligado, Color cor, int intensidade)
	{
		_ligado = ligado && intensidade > 0;
		_intensidade = Mathf.Clamp(intensidade, 0, 2);

		if (!_ligado)
		{
			// PARA DE EMITIR, MAS NAO SOME NA HORA: os raios que ja estao no ar terminam de
			// piscar. Cortar o node inteiro faria a eletricidade sumir num quadro, e sair de uma
			// forma tem que parecer a energia se dissipando.
			_fogo.Emitting = false;
			GetTree()?.CreateTimer(_fogo.Lifetime).Timeout += () =>
			{
				if (!_ligado && IsInstanceValid(_fogo)) _fogo.Visible = false;
			};
			return;
		}

		_tinta.SetShaderParameter("cor", cor);
		_tinta.SetShaderParameter("zigue", 0.22f + _intensidade * 0.09f);
		// A GROSSURA ESCRITA AQUI E SO A BASE DA FORMA. Quem afina a media e quem espalha um raio do
		// outro sao dois uniforms que moram no `RaioDaForma.gdshader` (`afinar` e
		// `variacao_grossura`) e que este arquivo NAO ESCREVE de proposito: escrever apagaria o
		// padrao do shader e o dono perderia o unico jeito de afinar isso sem um build inteiro pelo
		// meio. Ver o bloco de comentario do shader pras medidas de antes e depois.
		_tinta.SetShaderParameter("grossura", 0.055f - _intensidade * 0.006f);
		_tinta.SetShaderParameter("halo", 0.7f + _intensidade * 0.35f);

		// `AmountRatio` E O QUE ESCALA O VOLUME. Mexer no `Amount` realocaria o buffer de
		// particulas na GPU -- e realocar no meio da luta e o engasgo que este node evita.
		// A QUANTIDADE NAO E ESCRITA AQUI: ela e sorteada a cada rajada, em `Disparar`. Escrever
		// um valor fixo neste ponto foi o que fez a versao anterior sair sempre com o mesmo numero
		// de raios -- o campo do catalogo virava um teto, nao um sorteio.

		// O RAIO DO SSJ3 E MAIOR QUE O DO SSJ2, e nao so mais numeroso: no DM o degrau de cima soma
		// duas folhas de eletricidade por cima da primeira (`ElecAura1.dmi` e `Electric_Blue.dmi`),
		// entao a diferenca la tambem e de TAMANHO na tela, e nao so de contagem.
		//
		// A CONTA NAO MUDOU quando a intensidade 3 morreu, de proposito: o SSJ3 esta em 2 antes e
		// depois, entao ele continua com o mesmo 1,19 de sempre. Quem afinou isso no olho foi o
		// dono, e mexer no coeficiente pra "reaproveitar o topo" mudaria a forma que ele aprovou.
		//
		// O TETO ja tinha descido antes (era 1,1 + 0,2 x intensidade = ate 1,7). Um quad de 24 px
		// escalado 1,7 da 41 px de raio num corpo de 32 -- o raio ficava maior que a pessoa.
		_fisica.ScaleMax = 0.95f + _intensidade * 0.12f;

		_fogo.Visible = true;

		// A PRIMEIRA RAJADA SAI NA HORA. Esperar o intervalo aqui daria dois segundos de forma
		// acesa sem eletricidade nenhuma -- justo no instante em que o jogador esta olhando pro
		// proprio personagem depois de transformar.
		Disparar();
	}

	/// <summary>
	/// O RELOGIO DAS RAJADAS. Cada disparo sorteia quantos raios e quanto esperar pro proximo.
	/// </summary>
	public override void _Process(double delta)
	{
		if (!_ligado) return;
		_ateARajada -= delta;
		if (_ateARajada <= 0) Disparar();
	}

	/// <summary>
	/// SOLTA UMA RAJADA DE 1 A <see cref="Maximo"/> RAIOS e marca a proxima.
	///
	/// O sorteio e do TAMANHO da rajada, e o minimo e 1 de proposito: uma rajada de zero raios
	/// seria indistinguivel do efeito desligado, e o jogador leria como "quebrou".
	/// </summary>
	/// <summary>
	/// ============================ O ESTALO DA FAISCA ============================
	/// Pedido do dono: toda rajada toca um dos quatro `sparks` de
	/// `Assets/Sounds/Effects/sparks`. Sorteado a cada rajada de proposito -- o mesmo estalo
	/// repetido a cada 1,3..2,7 s vira metronomo, e faisca de energia nao tem compasso.
	///
	/// Posicional (`EfeitoNoLugar`), e nao global: quem esta longe do Super Saiyajin nao ouve o
	/// crepitar dele. O alcance e menor que o padrao porque isto e um detalhe do corpo, nao um
	/// acontecimento da zona.
	/// ========================================================================
	/// </summary>
	private static readonly string[] Estalos =
	[
		"res://Assets/Sounds/Effects/sparks/sparks1.mp3",
		"res://Assets/Sounds/Effects/sparks/sparks2.mp3",
		"res://Assets/Sounds/Effects/sparks/sparks3.mp3",
		"res://Assets/Sounds/Effects/sparks/sparks4.mp3",
	];

	/// <summary>Quantos estalos ja tocaram. Pra bancada: som que nao toca nao se ve num teste.</summary>
	public int EstalosDeTeste { get; private set; }

	private void Disparar()
	{
		AudioDirector.EfeitoNoLugar(this, Estalos[GD.Randi() % (uint)Estalos.Length], 0.55f, 320f);
		EstalosDeTeste++;

		int quantos = _sorte.RandiRange(1, TetoDaIntensidade());
		UltimaRajadaDeTeste = quantos;
		RajadasDeTeste++;

		_fogo.AmountRatio = quantos / (float)Maximo;

		// `Restart()` num emissor OneShot e o que dispara a leva. `Emitting = true` sozinho nao
		// basta: um OneShot que ja disparou fica com `Emitting` falso e ignora ser religado.
		_fogo.Restart();

		_ateARajada = _sorte.RandfRange((float)IntervaloMin, (float)IntervaloMax);
	}

	/// <summary>
	/// O QUAD EM QUE CADA RAIO E DESENHADO -- um retangulo branco de <see cref="TamanhoDoRaio"/>.
	///
	/// A textura e BRANCA E OPACA de proposito: quem recorta o raio e o shader, a partir do UV.
	/// Uma textura com desenho voltaria a amarrar a forma do raio a um arquivo.
	/// </summary>
	private static ImageTexture Quad()
	{
		Image img = Image.CreateEmpty((int)TamanhoDoRaio.X, (int)TamanhoDoRaio.Y, false, Image.Format.Rgba8);
		img.Fill(Colors.White);
		return ImageTexture.CreateFromImage(img);
	}
}
