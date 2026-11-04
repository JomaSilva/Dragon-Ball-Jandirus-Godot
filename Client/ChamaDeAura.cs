using Godot;
using Jandirus.Core.Forms;

namespace Jandirus.Client;

/// <summary>
/// A CHAMA DAS FORMAS DIVINAS, POR SHADER (v0.3.0, adendo 13 do dono). O pedido comecou pra todas as formas
/// (*"pararmos de usar aura de sprite e comecar a usar aura de shaders do godot"*) e o dono o recortou depois das fotos:
/// *"acho q e melhor manter a aura de sprite pras formas mortais, e manter essa aura shaders pras formas divinas, e no
/// blue kaioken seria a aura shaders do blue + aura sprite do kaioken por cima"*.
///
/// ============================ QUEM DESENHA O QUE ============================
/// O SIMBOLO DO CORE (`Catalogo.Folha`) decide, e os dois desenhistas recebem o MESMO: o <see cref="SpriteDeAura"/> nao
/// tem folha pros tres divinos (<see cref="SpriteDeAura.CaminhoDa"/> devolve nulo e ele fica mudo -- o mesmo mecanismo
/// que ja calava a chama no Ultra Instinto), e esta classe so desenha os tres (<see cref="Estilo"/>). Nao ha simbolo que
/// os dois desenhem. O Kaio-ken por cima do Blue e o UNICO caso de duas chamas no corpo, e e de proposito: o `Aura` e a
/// `CargaVisual` dao o simbolo do Kaio-ken so ao sprite, e esta chama segue com o da forma, por baixo dele.
///
/// ============================ O DESENHO ============================
/// Um quad com o `ChamaDeAura.gdshader` atras do corpo -- ver o cabecalho do shader. A chama CONTORNA o corpo (a
/// silhueta do quadro de 32 em pe) e comeca no pe; por contornar o corpo ela GIRA com ele no voo (o boneco deita na
/// direcao do voo, `CharacterVisual.VoarPara`) e CRESCE com ele (`CharacterVisual.TamanhoDoCorpo`).
///
/// ============================ (2026-11-02, o pedido 223) A LUZ DO BLUE E O BLUE KAIO-KEN ============================
/// O dono, com duas fotos de um Blue de outro jogo: *"Faça a aura do blue ter esse efeito, e no blue kaioken o efeito da segunda
/// imagem, tire prints pra revisao, se ficar bom aplicamos em outras auras"*. A chama do Blue (a `DeusFrio`: o Blue, o Blue Evolution
/// e a MAX do bio -- o mesmo desenho, a diferenca e a cor, o dono la atras) virou a LUZ (o desenho 3, maior): o clarao quase branco, o
/// azul em volta, a beirada em lobulos com um contorno amarelado, e estrelinhas. Com o Kaio-ken por cima ela vira o desenho 4 -- a
/// luz por dentro e uma chama VERMELHA grande por fora --, e o sprite do Kaio-ken, que desenhava por cima do Blue (o pedido do adendo
/// 13), sai: a chama desenha as duas (`Aura.Kaioken`, `CargaVisual.Kaioken`, <see cref="LevaOKaioken"/>). As outras chamas nao mudaram
/// naquele dia -- *"se ficar bom aplicamos em outras auras"*.
///
/// ============================ (2026-11-02, o pedido 223f) A LUZ EM TODAS AS DIVINAS ============================
/// Vistas as fotos do Blue, o dono mandou aplicar (*"Sim, aplicar"*) e, perguntado em quais, *"Só as divinas"*: o fogo do SSG (e o do SSG
/// da linha Rose), o dourado da Golden Perfect e o Rose vestem a LUZ do Blue (o desenho 3), cada uma na SUA cor (<see cref="PaletaDe"/>).
/// As mortais continuam no sprite (o adendo 13), e a nuvem do Ultra Instinto continua a nuvem. O Kaio-ken por fora (o desenho 4) continua
/// so do Blue (<see cref="LevaOKaioken"/>). Os desenhos de antes (0, 1 e 2) ficam no shader, pras fotos do antes (<see cref="EstiloDeAntes"/>).
/// (o pedido 223g) E MENORES que a do Blue, vistas as fotos: *"Diminuir"* (<see cref="TamanhoDaLuz"/>).
/// </summary>
public partial class ChamaDeAura : Node2D
{
	private const string CaminhoDoShader = "res://Assets/Shaders/ChamaDeAura.gdshader";

	/// <summary>
	/// O QUAD, em pixel do corpo, com os pes a <see cref="PesNoQuad"/> do alto: cabe a chama (~7 px dos lados do corpo,
	/// ~12 acima da cabeca) e as linguas que o ruido estica. A beirada dele some em ~2 px no shader, entao uma lingua que
	/// passe nao corta reto.
	/// </summary>
	public const float LarguraDoQuad = 48f, AlturaDoQuad = 62f, PesNoQuad = 56f;

	/// <summary>
	/// (o pedido 223) Os dois desenhos da luz: a das divinas (a do Blue, e desde o 223f a de todas) e a do Blue com o Kaio-ken (a chama
	/// vermelha por fora).
	/// </summary>
	public const int EstiloDaLuz = 3, EstiloDaLuzComKaioken = 4;

	/// <summary>
	/// (o pedido 223) O QUAD DE CADA DESENHO, em pixel do corpo (largura, altura, pes a partir do alto). Os tres de antes cabem no de
	/// sempre; a LUZ e maior (~10 px dos lados e ~20 acima da cabeca, com os lobulos), e o Blue Kaio-ken, com a chama vermelha de
	/// labaredas por fora, bem maior -- as medidas das fotos do dono.
	/// </summary>
	public static (float Largura, float Altura, float Pes) QuadDo(int estilo) => estilo switch
	{
		// (r3, 1:1 com as fotos) a luz: +-19 com as bolhas e o brilho de fora, ~48 de altura; o Blue Kaio-ken: +-30 e as linguas ate ~72
		EstiloDaLuz => (64f, 68f, 62f),
		EstiloDaLuzComKaioken => (80f, 94f, 88f),
		_ => (LarguraDoQuad, AlturaDoQuad, PesNoQuad),
	};

	/// <summary>
	/// (2026-11-02, o pedido 223g) O TAMANHO DA LUZ de cada divina, em escala a partir dos pes: o Blue (e o Evolution e a MAX, o mesmo
	/// simbolo) no tamanho 1:1 da foto do dono, e as outras menores -- o dono, perguntado se o tamanho das novas do SSG e do Rose estava
	/// bom: *"Diminuir"*. A Golden Perfect vai junto com o SSG (a aura dela e *"igual a do ssj god so q dourada"*). O quad fica o da luz: a
	/// menor cabe nele com folga.
	/// </summary>
	public const float TamanhoDaLuzMenor = 0.8f;

	/// <inheritdoc cref="TamanhoDaLuzMenor"/>
	public static float TamanhoDaLuz(FolhaDeAura f) => f == FolhaDeAura.DeusFrio ? 1f : TamanhoDaLuzMenor;

	private static Shader? _shader;
	private static Shader Sh => _shader ??= ResourceLoader.Load<Shader>(CaminhoDoShader);

	private static ImageTexture? _branco;

	/// <summary>Um texel branco, esticado no tamanho do quad: o desenho e todo do shader, e a textura so da o UV.</summary>
	private static ImageTexture Branco()
	{
		if (_branco != null) return _branco;
		Image img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		img.SetPixel(0, 0, Colors.White);
		return _branco = ImageTexture.CreateFromImage(img);
	}

	// OS NOMES QUE ESTE ARQUIVO ENTREGA AO GODOT POR QUADRO -- a carga pulsa a forca a cada quadro, e a `CargaVisual` de
	// todo corpo alheio repinta a cada snapshot. Um `StringName` guardado por nome: a `string` entregue no lugar fabricaria
	// tres objetos com finalizador a cada chamada (a memoria do coletor do .NET, ver `CharacterVisual`).
	private static readonly StringName
		UniEstilo = "estilo", UniMiolo = "cor_miolo", UniMeio = "cor_meio", UniBorda = "cor_borda", UniAro = "cor_aro",
		UniForca = "forca", UniKaioken = "cor_kaioken", UniLargura = "largura_px", UniAltura = "altura_px", UniPes = "pes_px",
		UniTamanhoDaLuz = "tamanho_luz";

	private Sprite2D? _quad;
	private ShaderMaterial? _mat;
	private FolhaDeAura _simbolo = FolhaDeAura.Base;
	private int? _estilo;
	private bool _aceso;
	private Color _cor = Colors.White;
	private float _forca = 1f;

	/// <summary>O que o material ja recebeu -- a paleta so e reescrita quando o simbolo ou a cor mudam.</summary>
	private (FolhaDeAura Simbolo, Color Cor, int Estilo)? _paletaPintada;

	public override void _Ready()
	{
		// ATRAS DO CORPO PELA ORDEM DE IRMAO, como o `SpriteDeAura` (ver o `_Ready` dele): z proprio enterraria a chama
		// debaixo do chao da zona.
		ZIndex = 0;
		Visible = false;
		SetProcess(false);
	}

	/// <summary>
	/// QUAL DESENHO O SIMBOLO PEDE -- desde o pedido 223f, a LUZ (<see cref="EstiloDaLuz"/>) pras quatro divinas -- ou NULO: "esta chama
	/// nao e minha" (as mortais sao do <see cref="SpriteDeAura"/>, a nebulosa e da `NebulosaDaForma`).
	/// </summary>
	public static int? Estilo(FolhaDeAura f) => DesenhaTudoDeTeste && f != FolhaDeAura.Nebulosa ? EstiloDivino(f) ?? 1 : EstiloDivino(f);

	// (2026-11-02, o pedido 223f) A LUZ EM TODAS AS DIVINAS, cada uma na sua cor (a `PaletaDe`) -- o dono: *"Só as divinas"*. O desenho de
	// antes de cada uma e o `EstiloDeAntes`, logo abaixo.
	private static int? EstiloDivino(FolhaDeAura f) => f switch
	{
		FolhaDeAura.DeusQuente => EstiloDaLuz,   // o SSG e o SSG da linha Rose; o fogo de antes era o 0
		FolhaDeAura.DeusFrio => EstiloDaLuz,   // (2026-11-02, o pedido 223) a luz; a chama de antes era o 1
		FolhaDeAura.DeusRosa => EstiloDaLuz,   // o Rose e o Rose 2; a chama de antes era o 2
		FolhaDeAura.DeusDourado => EstiloDaLuz,   // a Golden Perfect do bio; antes era o fogo do Deus (o 0) na rampa de ouro
		_ => null,
	};

	/// <summary>
	/// (o pedido 223f) O DESENHO DE ANTES DA LUZ de cada divina: o fogo do Deus (0, tambem o da Golden), a chama do Blue (1) e a do Rose
	/// (2). So o laboratorio da aura o pede (<see cref="DesenhoDeAntesDeTeste"/>), pra por o antes ao lado do depois na mesma foto.
	/// </summary>
	public static int? EstiloDeAntes(FolhaDeAura f) => f switch
	{
		FolhaDeAura.DeusQuente or FolhaDeAura.DeusDourado => 0,
		FolhaDeAura.DeusFrio => 1,
		FolhaDeAura.DeusRosa => 2,
		_ => null,
	};

	/// <summary>
	/// (o pedido 223) O DESENHO COM O KAIO-KEN ACESO: a luz do Blue leva o Kaio-ken pra dentro dela (o desenho 4, a chama vermelha por
	/// fora); as outras chamas nao mudam com ele -- o sprite vermelho segue por cima delas.
	/// </summary>
	public static int? Estilo(FolhaDeAura f, bool kaioken) =>
		kaioken && LevaOKaioken(f) ? EstiloDaLuzComKaioken : Estilo(f);

	/// <summary>(o pedido 223) Esta chama desenha o Kaio-ken por fora quando ele acende -- e o sprite dele fica mudo por cima dela.</summary>
	/// (o pedido 223f) SO A LUZ DO BLUE: as outras divinas vestem a luz agora, mas o Kaio-ken nao se sustenta em nenhuma delas
	/// (`Kaioken.VetoDaForma`), e o desenho 4 e o do Blue Kaio-ken (o contorno da luz de dentro e o branco-ciano da foto dele).
	public static bool LevaOKaioken(FolhaDeAura f) => f == FolhaDeAura.DeusFrio && Estilo(f) == EstiloDaLuz;

	/// <summary>
	/// (o pedido 223) A CHAMA DE ANTES DA LUZ, nesta chama: o desenho de antes (<see cref="EstiloDeAntes"/>) e a paleta de antes, e o
	/// Kaio-ken fora dela -- so o laboratorio da aura a liga, pra comparar o antes e o depois na mesma foto. Falso em jogo, sempre. (Ate
	/// o pedido 223f era so a do Blue.)
	/// </summary>
	public bool DesenhoDeAntesDeTeste { get; set; }

	/// <summary>Esta chama desenha o simbolo? Ver <see cref="Estilo"/>.</summary>
	public static bool Desenha(FolhaDeAura f) => Estilo(f) != null;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagforma`): esta chama desenha TODO simbolo com folha -- a primeira versao do adendo,
	/// antes de o dono devolver as formas mortais ao sprite. Lido pelo <see cref="Estilo"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool DesenhaTudoDeTeste;

	/// <summary>
	/// O SIMBOLO DA FORMA. Fora dos tres divinos a chama fica MUDA -- e GUARDA o aceso, como o `SpriteDeAura` faz sem
	/// folha: quem passa de uma forma mortal pra uma divina com o C segurado acende no mesmo quadro em que o simbolo
	/// chega.
	/// </summary>
	/// (2026-11-02, o pedido 223) ...E SE O KAIO-KEN ESTA ACESO: a luz do Blue o desenha por fora dela (<see cref="Estilo(FolhaDeAura, bool)"/>).
	public void DefinirFolha(FolhaDeAura f, bool kaioken = false)
	{
		int? estilo = DesenhoDeAntesDeTeste && EstiloDeAntes(f) is { } antes ? antes : Estilo(f, kaioken);
		if (f == _simbolo && _estilo == estilo) return;
		_simbolo = f;
		_estilo = estilo;
		Ligar();
	}

	/// <summary>
	/// Acende (ou apaga) a chama. Chamado a cada snapshot pela `CargaVisual` de todo corpo, entao a primeira linha e a
	/// comparacao: apagada continua apagada sem tocar em nada.
	/// </summary>
	public void Definir(bool aceso, Color cor, float forca = 1f)
	{
		if (!aceso && !_aceso) { _cor = cor; _forca = forca; return; }
		_aceso = aceso;
		_cor = cor;
		_forca = forca;
		Ligar();
	}

	private void Ligar()
	{
		bool desenha = _aceso && _estilo != null;
		if (desenha) Montar();
		if (desenha != Visible)
		{
			if (desenha) AcompanharOCorpo();   // do tamanho e no giro do corpo ja no primeiro quadro aceso
			Visible = desenha;
			SetProcess(desenha);
		}
		if (desenha) Pintar();
	}

	/// <summary>
	/// MONTA O QUAD NA PRIMEIRA VEZ QUE A CHAMA ACENDE, e nao no `_Ready`: quase nenhum corpo da zona acende uma forma
	/// divina, e um material por corpo que entra no campo de visao seria pago por todos.
	/// </summary>
	private void Montar()
	{
		if (_quad != null) return;
		_mat = new ShaderMaterial { Shader = Sh };
		// a SEMENTE descasa as chamas de dois corpos lado a lado: o mesmo desenho no mesmo instante seria um carimbo
		_mat.SetShaderParameter("semente", GetInstanceId() % 997 * 0.113f);
		_quad = new Sprite2D
		{
			Name = "Quad",
			Texture = Branco(),
			Centered = false,
			Material = _mat,
		};
		AddChild(_quad);
		MedirOQuad(_estilo ?? 1);
	}

	/// <summary>
	/// (o pedido 223) O QUAD NO TAMANHO DO DESENHO (<see cref="QuadDo"/>), e o shader com as mesmas medidas: a luz e o Blue Kaio-ken
	/// sao maiores que as chamas de antes, e o mesmo corpo passa de uma pra outra quando o Kaio-ken acende.
	/// </summary>
	private void MedirOQuad(int estilo)
	{
		if (_quad == null || _mat == null) return;
		(float largura, float altura, float pes) = QuadDo(estilo);
		_mat.SetShaderParameter(UniLargura, largura);
		_mat.SetShaderParameter(UniAltura, altura);
		_mat.SetShaderParameter(UniPes, pes);
		_quad.Scale = new Vector2(largura, altura);
		// os pes do quad na LINHA DOS PES do corpo (a origem deste node e o centro do quadro de 32)
		_quad.Position = new Vector2(-largura * 0.5f, SpriteDeAura.LinhaDosPes - pes);
	}

	/// <summary>
	/// A FORCA VAI SEMPRE (ela pulsa a cada quadro na carga); a PALETA e o desenho so quando o simbolo ou a cor mudam.
	/// </summary>
	private void Pintar()
	{
		if (_mat == null || _estilo is not { } estilo) return;
		_mat.SetShaderParameter(UniForca, _forca);
		if (_paletaPintada is { } p && p.Simbolo == _simbolo && p.Cor == _cor && p.Estilo == estilo) return;
		_paletaPintada = (_simbolo, _cor, estilo);
		MedirOQuad(estilo);
		(Color miolo, Color meio, Color borda, Color aro) = PaletaDe(_simbolo, _cor, DesenhoDeAntesDeTeste);
		_mat.SetShaderParameter(UniKaioken, Aura.CorDoKaioken);
		_mat.SetShaderParameter(UniEstilo, estilo);
		_mat.SetShaderParameter(UniTamanhoDaLuz, TamanhoDaLuz(_simbolo));   // (o pedido 223g) so a luz (o desenho 3) o le
		_mat.SetShaderParameter(UniMiolo, miolo);
		_mat.SetShaderParameter(UniMeio, meio);
		_mat.SetShaderParameter(UniBorda, borda);
		_mat.SetShaderParameter(UniAro, aro);
	}

	/// <summary>
	/// A RAMPA DE QUATRO TONS desta chama: o MIOLO (quase branco, encostado no corpo), o MEIO, a BORDA (mais funda) e o
	/// ARO (a beirada acesa).
	///
	/// O DEUS E O ROSE TEM RAMPA PROPRIA, das referencias do dono: o fogo vai do amarelo ao vermelho, e o Rose e violeta
	/// no corpo e carmim fundo em volta, com a beirada em vermelho vivo. O BLUE SAI DA COR DA FORMA -- e e por isso que o
	/// Evolution e mais escuro com o mesmo desenho (*"ela tem o formato igual a do blue, a diferenca ta na cor"*): o
	/// `1c7cff` dele contra o `3ad2ff` do Blue, no catalogo.
	///
	/// (2026-10-24/25) O FOGO DO DEUS FICOU VERMELHO por um dia, a pedido do dono (*"deixe o ssj deus mais vermelho e a aura
	/// dele tb"*), e VOLTOU a este quando ele viu as fotos: *"a aura anterior tava melhor do ssj god, o cabelo atual ta top"*. O
	/// cabelo vermelho ficou (`Catalogo.VermelhoDoCabeloDivino`); o fogo e o amarelo-laranja de sempre.
	/// </summary>
	/// (2026-11-02, o pedido 223) A LUZ DO BLUE: o miolo BRANCO (o clarao da foto), a cor da forma no meio, um tom mais fundo na
	/// beirada e o contorno AMARELADO -- e o Evolution e a MAX continuam saindo da cor deles. `antes` = a paleta da chama de antes.
	/// (o pedido 223f) E AS OUTRAS DIVINAS NA LUZ, cada uma na SUA cor -- no desenho 3 a `Borda` e o brilho de FORA e o `Aro` o contorno.
	public static (Color Miolo, Color Meio, Color Borda, Color Aro) PaletaDe(FolhaDeAura f, Color cor, bool antes = false) => f switch
	{
		// (r3, 1:1 com as fotos do dono) o miolo quase branco da foto (e0e0de), a faixa na cor da forma um pouco menos saturada (o 3ad2ff
		// do Blue vira 59cbec, perto do 64cdee da foto), o brilho oliva de fora (d3d563) e o contorno amarelado (c8d890). A faixa perde
		// saturacao pro cinza da PROPRIA cor, e nao pra um cinza-azulado fixo: assim a MAX do Bio (o mesmo desenho, ff2a35) segue
		// VERMELHA (d5363e) -- com o cinza-azulado ela saia e7505c, azul demais pro vermelho que o dono pediu.
		FolhaDeAura.DeusFrio when !antes => (new Color("e0e0de"), cor.Lerp(new Color(cor.Luminance, cor.Luminance, cor.Luminance), 0.25f),
			new Color("d3d563"), new Color("c8d890")),
		// (2026-11-02, o pedido 223f) AS OUTRAS TRES NA LUZ, cada uma na sua cor (o dono: *"Só as divinas"*): o miolo quase branco puxado pro
		// tom dela, a faixa na cor dela, e o brilho de fora e o contorno tirados das cores que ela ja tinha -- o SSG laranja com o contorno
		// amarelo e o brilho vermelho do fogo; a Golden em ouro com o contorno creme e o brilho do ouro velho; o Rose violeta com a beirada
		// vermelha viva e o brilho carmim. Escolhidas num prototipo do desenho 3 (as mesmas contas, em numpy), lado a lado com o Blue.
		FolhaDeAura.DeusQuente when !antes => (new Color("ece6da"), new Color("ff9a3c"), new Color("e8331a"), new Color("ffc24a")),
		FolhaDeAura.DeusRosa when !antes => (new Color("e8dcea"), new Color("a65ae6"), new Color("6e0012"), new Color("dc1046")),
		FolhaDeAura.DeusDourado when !antes => (new Color("ebe6d6"), new Color("f5cf4a"), new Color("c98a0a"), new Color("ffe48a")),
		FolhaDeAura.DeusQuente => (new Color("fff6c8"), new Color("ffae2e"), new Color("e8331a"), new Color("e8331a")),
		FolhaDeAura.DeusRosa => (new Color("e8aaff"), new Color("9e28ff"), new Color("6e0012"), new Color("dc1046")),
		// (2026-10-26) A GOLDEN PERFECT DO BIO: o fogo do Deus *"so q dourada ao inves de laranja"* (o dono) -- a mesma rampa de quatro tons,
		// do branco-ouro encostado no corpo ao ouro velho da borda, no lugar do amarelo-laranja-vermelho do SSG.
		FolhaDeAura.DeusDourado => (new Color("fffbe6"), new Color("ffd84a"), new Color("c98a0a"), new Color("e8b11c")),
		_ => (cor.Lerp(Colors.White, 0.85f), cor, Funda(cor), cor.Lerp(Colors.White, 0.60f)),
	};

	/// <summary>A borda do Blue: mais saturada, mais escura e um pouco pro anil -- o azul fundo da beirada da foto do Evolution.</summary>
	private static Color Funda(Color c)
	{
		c.ToHsv(out float h, out float s, out float v);
		return Color.FromHsv((h + 0.03f) % 1f, Mathf.Min(1f, s * 1.15f + 0.10f), v * 0.85f);
	}

	private static readonly NodePath CaminhoDoBoneco = "Visual";
	private CharacterVisual? _boneco;
	private float _tamanho = 1f, _giro;

	/// <summary>
	/// A CHAMA NO TAMANHO E NO GIRO DO CORPO. O tamanho e a regra do <see cref="SpriteDeAura"/> (o mesmo
	/// `CharacterVisual.TamanhoDoCorpo`, em torno da LINHA DOS PES, e o mesmo defeito injetado
	/// `SpriteDeAura.ChamaDeTamanhoFixoDeTeste`); o GIRO e desta chama so: ela contorna o corpo, e um corpo deitado no voo
	/// dentro de uma chama em pe sairia pelos lados dela. O sprite das mortais nao gira, e nao precisa: a arte dele e uma
	/// labareda subindo, nao um contorno.
	///
	/// A CONTA: o boneco gira em torno da origem dele (o centro do quadro, que e a origem deste node tambem), e a chama
	/// cresce em torno dos pes -- `x' = giro(s * (x - pe) + pe)`, ou seja escala `s`, giro `g` e posicao `giro(pe * (1 - s))`.
	/// </summary>
	private void AcompanharOCorpo()
	{
		if (_boneco == null || !IsInstanceValid(_boneco))
			_boneco = GetParent()?.GetParent()?.GetNodeOrNull<CharacterVisual>(CaminhoDoBoneco);
		float tamanho = SpriteDeAura.ChamaDeTamanhoFixoDeTeste || _boneco == null ? 1f : _boneco.TamanhoDoCorpo;
		float giro = ChamaEmPeNoVooDeTeste || _boneco == null ? 0f : _boneco.Rotation;
		if (Mathf.IsEqualApprox(tamanho, _tamanho) && Mathf.IsEqualApprox(giro, _giro)) return;
		_tamanho = tamanho;
		_giro = giro;
		Scale = Vector2.One * tamanho;
		Rotation = giro;
		Position = new Vector2(0, SpriteDeAura.LinhaDosPes * (1f - tamanho)).Rotated(giro);
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagforma`): a chama fica EM PE com o corpo deitado no voo. Lido pelo
	/// <see cref="AcompanharOCorpo"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool ChamaEmPeNoVooDeTeste;

	public override void _Process(double delta) => AcompanharOCorpo();

	/// <summary>
	/// O ENSAIO DO AQUECIMENTO: uma chama do Blue acesa no palco que ninguem ve, pela mesma porta do jogo -- o shader e
	/// compilado no primeiro material, e as pipelines (a do lado escuro e a gemea iluminada) no primeiro desenho; sem isto
	/// as tres contas cairiam no quadro da primeira forma divina do processo. Ver `Aquecimento.AtosDaChamaDivina`.
	/// </summary>
	public static void Ensaiar(Node2D pai, Vector2 onde)
	{
		var chama = new ChamaDeAura { Name = "ChamaDoEnsaio", Position = onde };
		pai.AddChild(chama);
		chama.DefinirFolha(FolhaDeAura.DeusFrio);
		chama.Definir(true, new Color("3ad2ff"));
	}

	// ============================ PRA BANCADA ============================
	/// <summary>A chama esta DESENHANDO agora (acesa e com um simbolo que e dela)?</summary>
	public bool AcesaDeTeste => _aceso && _estilo != null && Visible;

	/// <summary>O desenho escolhido pelo simbolo; nulo = muda.</summary>
	public int? EstiloDeTeste => _estilo;

	/// <summary>O simbolo que ela recebeu por ultimo.</summary>
	public FolhaDeAura SimboloDeTeste => _simbolo;

	/// <summary>O tamanho e o giro em que ela esta desenhada agora.</summary>
	public float TamanhoDeTeste => _tamanho;

	/// <inheritdoc cref="TamanhoDeTeste"/>
	public float GiroDeTeste => _giro;

	/// <summary>
	/// A COR DO MEIO COMO O SHADER A VE -- lida do MATERIAL, e nao do campo (`a bancada mede INTENCAO`: o campo diz o que
	/// se pediu, o material o que chegou). Nulo enquanto a chama nunca acendeu.
	/// </summary>
	public Color? CorNoMaterialDeTeste => _mat?.GetShaderParameter("cor_meio") is { VariantType: Variant.Type.Color } v ? v.AsColor() : null;

	/// <summary>A forca como o shader a ve. Nulo enquanto a chama nunca acendeu.</summary>
	public float? ForcaNoMaterialDeTeste => _mat?.GetShaderParameter("forca") is { VariantType: Variant.Type.Float } v ? v.AsSingle() : null;
}
