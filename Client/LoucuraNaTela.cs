using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A LOUCURA NA TELA -- O MEDIDOR DE SANIDADE DO CORRUPTED MAJIN (2026-10-16) ============================
/// NAO HA BARRA. O dono: *"esquece a barra na tela kkkk eu esqueci q ele ja falou q a tela vai tendo um efeito q representa a
/// loucura"* -- e o sistema do amigo dele diz qual: *"red and black screen effect ... turning into tunnel vision when insane"*.
///
/// DUAS PECAS, e as duas sao so textura (nenhum shader novo, nenhuma pipeline nova -- ver os engasgos de primeiro uso desta casa):
///   * a VINHETA vermelha e preta, que aparece conforme a sanidade FALTA e fecha das bordas pra dentro: ela nasce maior que a
///     tela (o anel escuro fica do lado de fora) e encolhe ate o tamanho dela no zero;
///   * o TUNEL, so com a cabeca perdida: um centro claro pequeno e o resto escuro, apertando no compasso de um coracao.
///
/// ATRAS DA INTERFACE, NA FRENTE DO MUNDO: camada 0, como a nevoa de altitude (ver o mapa de camadas no `NevoaDeAltitude`) -- o
/// HUD, o chat e os menus ficam por cima e intactos. E SEM LUZ (`LightMask = 0`): a camada 0 e alcancada pelas luzes do mundo, e
/// uma vinheta acesa pela aura do proprio Majin seria a "gemea iluminada" (outra pipeline, montada no quadro em que ela aparece).
///
/// SO O DONO DO CORPO A VE (o `S2C.Loucura` so vai pra ele), e os numeros sao do servidor: aqui so se desenha, suavizado -- o
/// pacote sai quando a sanidade muda de um ponto, e o olho pegaria o degrau.
/// =================================================================================================================================
/// </summary>
public partial class LoucuraNaTela : CanvasLayer
{
	/// <summary>O valor do pacote que quer dizer "este corpo nao tem a barra" (nunca destravou a Forma Pura) -- apaga tudo.</summary>
	public const byte SemBarra = 255;

	/// <summary>A camada do mundo -- ver o mapa no `NevoaDeAltitude`.</summary>
	private const int Camada = 0;

	/// <summary>Quanto o que se ve persegue o valor do pacote, por segundo.</summary>
	private const float Perseguicao = 3f;

	/// <summary>Quanto MAIOR que a tela a vinheta e com a sanidade cheia -- ela fecha ate o tamanho da tela no zero.</summary>
	private const float FolgaDaVinheta = 0.35f;

	/// <summary>O coracao do tunel: batidas por segundo, e quanto ele aperta em cada uma.</summary>
	private const float Batidas = 1.3f, Aperto = 0.06f;

	/// <summary>O lado das texturas: o gradiente e liso e a tela o estica com filtro LINEAR, entao 128 basta.</summary>
	private const int Lado = 128;

	private TextureRect? _vinheta, _tunel;
	private float _alvoVinheta, _agoraVinheta, _alvoTunel, _agoraTunel;
	private double _relogio;

	/// <summary>A vinheta que a tela mostra AGORA, de 0 a 1 (suavizada). Pra bancada e pro robo de fotos.</summary>
	public float VinhetaNaTela => _agoraVinheta;

	/// <summary>O tunel que a tela mostra AGORA, de 0 a 1 (suavizado). Pra bancada e pro robo de fotos.</summary>
	public float TunelNaTela => _agoraTunel;

	/// <summary>O ultimo pacote: a sanidade (ou <see cref="SemBarra"/>) e a cabeca. Pra bancada.</summary>
	public byte SanidadeDeTeste { get; private set; } = SemBarra;

	/// <inheritdoc cref="SanidadeDeTeste"/>
	public bool InsanoDeTeste { get; private set; }

	public override void _Ready()
	{
		Layer = Camada;

		// A VINHETA: transparente no meio, vermelho escuro chegando, quase preto na beirada. O RGB do trecho transparente ja e
		// vermelho de proposito: a interpolacao entre um transparente PRETO e o vermelho deixaria um anel sujo no meio do caminho.
		_vinheta = Pano("Vinheta",
			[0f, 0.3f, 0.6f, 0.9f, 1f],
			[new Color(0.55f, 0f, 0f, 0f), new Color(0.55f, 0f, 0f, 0f), new Color(0.55f, 0f, 0.02f, 0.6f), new Color(0.05f, 0f, 0f, 0.95f),
			 new Color(0f, 0f, 0f, 0.97f)]);

		// O TUNEL: o centro claro e pequeno, e a escuridao chega rapido.
		_tunel = Pano("Tunel",
			[0f, 0.18f, 0.4f, 0.62f, 1f],
			[new Color(0.3f, 0f, 0f, 0f), new Color(0.3f, 0f, 0f, 0f), new Color(0.35f, 0f, 0f, 0.78f), new Color(0.03f, 0f, 0f, 0.97f), new Color(0f, 0f, 0f, 1f)]);

		AddChild(_vinheta);
		AddChild(_tunel);
	}

	/// <summary>
	/// UM PANO DE TELA CHEIA com um gradiente radial. O centro e o meio da tela e o raio e meia tela -- esticado, o circulo vira a
	/// elipse da propria tela, que e o desenho de uma vinheta. Os cantos passam do raio e ficam na ultima cor.
	/// </summary>
	private static TextureRect Pano(string nome, float[] pontos, Color[] cores)
	{
		var gradiente = new Gradient { Offsets = pontos, Colors = cores };
		var textura = new GradientTexture2D
		{
			Gradient = gradiente,
			Fill = GradientTexture2D.FillEnum.Radial,
			FillFrom = new Vector2(0.5f, 0.5f),
			FillTo = new Vector2(1f, 0.5f),
			Width = Lado,
			Height = Lado,
		};
		var pano = new TextureRect
		{
			Name = nome,
			Texture = textura,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			// O PANO NAO PODE ROUBAR O CLIQUE -- ele cobre a tela inteira (o mesmo cuidado da nevoa).
			MouseFilter = Control.MouseFilterEnum.Ignore,
			// LINEAR: o projeto e pixel-art (filtro de vizinho no padrao), e 128 texels esticados ate 1920 sem filtro seriam degraus.
			TextureFilter = CanvasItem.TextureFilterEnum.Linear,
			LightMask = 0,
			Visible = false,
		};
		pano.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		return pano;
	}

	/// <summary>
	/// O PACOTE CHEGOU: a sanidade de 0 a 100 (ou <see cref="SemBarra"/>) e se a cabeca ja se foi. A vinheta segue o que FALTA da
	/// sanidade; o tunel, so a cabeca perdida. Dormindo o servidor manda `insano` falso -- a vinheta fica cheia e o tunel apaga.
	/// </summary>
	public void Mostrar(byte sanidade, bool insano)
	{
		SanidadeDeTeste = sanidade;
		InsanoDeTeste = insano;
		if (sanidade == SemBarra)
		{
			_alvoVinheta = 0f;
			_alvoTunel = 0f;
			return;
		}
		_alvoVinheta = 1f - Mathf.Clamp(sanidade, 0, 100) / 100f;
		_alvoTunel = insano ? 1f : 0f;
	}

	public override void _Process(double delta)
	{
		if (_vinheta == null || _tunel == null) return;

		float k = Mathf.Min(1f, (float)delta * Perseguicao);
		_agoraVinheta = Mathf.Lerp(_agoraVinheta, _alvoVinheta, k);
		if (Mathf.Abs(_agoraVinheta - _alvoVinheta) < 0.002f) _agoraVinheta = _alvoVinheta;
		_agoraTunel = Mathf.Lerp(_agoraTunel, _alvoTunel, k);
		if (Mathf.Abs(_agoraTunel - _alvoTunel) < 0.002f) _agoraTunel = _alvoTunel;

		// QUEM NAO ESTA NA TELA NAO PAGA NADA: um pano invisivel nao e desenhado.
		_vinheta.Visible = _agoraVinheta > 0.002f;
		_tunel.Visible = _agoraTunel > 0.002f;

		if (_vinheta.Visible)
		{
			// FECHA DAS BORDAS PRA DENTRO: com pouca falta o pano e maior que a tela e o anel escuro fica do lado de fora.
			_vinheta.PivotOffset = _vinheta.Size * 0.5f;
			_vinheta.Scale = Vector2.One * (1f + FolgaDaVinheta * (1f - _agoraVinheta));
			_vinheta.Modulate = new Color(1f, 1f, 1f, Mathf.Pow(_agoraVinheta, 0.6f));
		}

		if (_tunel.Visible)
		{
			// O CORACAO: a batida e um pico curto (o seno elevado a quarta), e na batida o tunel APERTA -- o pano so cresce, nunca
			// fica menor que a tela (menor, ele descobriria a beirada).
			_relogio += delta;
			float seno = Mathf.Max(0f, Mathf.Sin((float)(_relogio * Batidas * Mathf.Tau)));
			float batida = seno * seno * seno * seno;
			_tunel.PivotOffset = _tunel.Size * 0.5f;
			_tunel.Scale = Vector2.One * (1f + Aperto * (1f - batida));
			_tunel.Modulate = new Color(1f, 1f, 1f, _agoraTunel);
		}
		else _relogio = 0;
	}
}
