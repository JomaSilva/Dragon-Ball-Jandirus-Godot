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
		UniForca = "forca";

	private Sprite2D? _quad;
	private ShaderMaterial? _mat;
	private FolhaDeAura _simbolo = FolhaDeAura.Base;
	private int? _estilo;
	private bool _aceso;
	private Color _cor = Colors.White;
	private float _forca = 1f;

	/// <summary>O que o material ja recebeu -- a paleta so e reescrita quando o simbolo ou a cor mudam.</summary>
	private (FolhaDeAura Simbolo, Color Cor)? _paletaPintada;

	public override void _Ready()
	{
		// ATRAS DO CORPO PELA ORDEM DE IRMAO, como o `SpriteDeAura` (ver o `_Ready` dele): z proprio enterraria a chama
		// debaixo do chao da zona.
		ZIndex = 0;
		Visible = false;
		SetProcess(false);
	}

	/// <summary>
	/// QUAL DOS TRES DESENHOS O SIMBOLO PEDE -- 0 o fogo do Deus, 1 a chama do Blue, 2 a do Rose -- ou NULO: "esta chama
	/// nao e minha" (as mortais sao do <see cref="SpriteDeAura"/>, a nebulosa e da `NebulosaDaForma`).
	/// </summary>
	public static int? Estilo(FolhaDeAura f) => DesenhaTudoDeTeste && f != FolhaDeAura.Nebulosa ? EstiloDivino(f) ?? 1 : EstiloDivino(f);

	private static int? EstiloDivino(FolhaDeAura f) => f switch
	{
		FolhaDeAura.DeusQuente => 0,
		FolhaDeAura.DeusFrio => 1,
		FolhaDeAura.DeusRosa => 2,
		_ => null,
	};

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
	public void DefinirFolha(FolhaDeAura f)
	{
		if (f == _simbolo && _estilo == Estilo(f)) return;
		_simbolo = f;
		_estilo = Estilo(f);
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
		_mat.SetShaderParameter("largura_px", LarguraDoQuad);
		_mat.SetShaderParameter("altura_px", AlturaDoQuad);
		_mat.SetShaderParameter("pes_px", PesNoQuad);
		_quad = new Sprite2D
		{
			Name = "Quad",
			Texture = Branco(),
			Centered = false,
			Material = _mat,
			Scale = new Vector2(LarguraDoQuad, AlturaDoQuad),
			// os pes do quad na LINHA DOS PES do corpo (a origem deste node e o centro do quadro de 32)
			Position = new Vector2(-LarguraDoQuad * 0.5f, SpriteDeAura.LinhaDosPes - PesNoQuad),
		};
		AddChild(_quad);
	}

	/// <summary>
	/// A FORCA VAI SEMPRE (ela pulsa a cada quadro na carga); a PALETA e o desenho so quando o simbolo ou a cor mudam.
	/// </summary>
	private void Pintar()
	{
		if (_mat == null || _estilo is not { } estilo) return;
		_mat.SetShaderParameter(UniForca, _forca);
		if (_paletaPintada is { } p && p.Simbolo == _simbolo && p.Cor == _cor) return;
		_paletaPintada = (_simbolo, _cor);
		(Color miolo, Color meio, Color borda, Color aro) = PaletaDe(_simbolo, _cor);
		_mat.SetShaderParameter(UniEstilo, estilo);
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
	public static (Color Miolo, Color Meio, Color Borda, Color Aro) PaletaDe(FolhaDeAura f, Color cor) => f switch
	{
		FolhaDeAura.DeusQuente => (new Color("fff6c8"), new Color("ffae2e"), new Color("e8331a"), new Color("e8331a")),
		FolhaDeAura.DeusRosa => (new Color("e8aaff"), new Color("9e28ff"), new Color("6e0012"), new Color("dc1046")),
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
