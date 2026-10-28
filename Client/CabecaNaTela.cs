using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ A CABECA NA TELA (dono, 2026-10-27, o pedido 119) ============================
/// *"Se vc sofrer mt dano na cabeça sua visão no jogo começaria a ficar turva, escurecendo, se vc for nocauteado por dano na cabeça,
/// vc desmaia e n ve nada alem do seu personagem (fundo preto)"*.
///
/// DUAS COISAS, as duas so do dono do corpo e as duas pelo que o cliente JA recebe -- a cabeca no `S2C.Corpo` (0-100) e o nocaute na
/// ficha (`SheetState.KO`); nenhum pacote novo:
///   * A VISAO TURVA: da cabeca "Gravemente ferida" (abaixo de 60%) ate a linha do nocaute (20%), o mundo borra e escurece das bordas
///     pra dentro. O borrao e o MESMO shader da nevoa de altitude (`Altitude.gdshader`, com a cor da distancia trocada por preto) -- a
///     pipeline dele ja e ensaiada no `Aquecimento`, e nenhum shader novo paga o primeiro uso no meio da luta. O escuro e uma vinheta de
///     textura, como a da loucura (`LoucuraNaTela`).
///   * O DESMAIO PELA CABECA: caido com a cabeca abaixo da linha -- na queda ou depois dela, o soco na cabeca de quem caiu pelo tronco --
///     a tela fica PRETA e so o proprio corpo aparece. E a cortina da cegueira do veu (`Visao.Desmaiado`, preto puro), com o corpo do
///     jogador posto acima dela no z ate levantar. Fica ate o corpo levantar, como no servidor (`CombatState.DesmaioPelaCabeca`).
///
/// ATRAS DA INTERFACE, NA FRENTE DO MUNDO: camada 0, como a nevoa (ver o mapa de camadas no `NevoaDeAltitude`) -- o HUD e o chat ficam
/// por cima e intactos.
/// =============================================================================================================
/// </summary>
public partial class CabecaNaTela : CanvasLayer
{
	/// <summary>A camada do mundo -- ver o mapa no `NevoaDeAltitude`.</summary>
	private const int Camada = 0;

	/// <summary>A cabeca abaixo disto (a faixa "Gravemente ferido" da aba Corpo) comeca a turvar a visao.</summary>
	public const int ComecaATurvar = 60;

	/// <summary>A linha do nocaute (`Regras.LimiarQuebra`, 20%): turva no maximo, e caido abaixo dela e desmaio pela cabeca.</summary>
	public const int LinhaDoNocaute = 20;

	/// <summary>
	/// O Z DO DESMAIO: o veu sobe pra ca e o corpo do jogador pra um acima -- tudo que e mundo (o maior z dele e o rastro, 300) fica
	/// atras do preto. Abaixo do teto de z do Godot (4096).
	/// </summary>
	public const int ZDoDesmaio = 3000;

	/// <summary>Quanto o que se ve persegue o alvo, por segundo.</summary>
	private const float Perseguicao = 2.5f;

	/// <summary>O veu do jogador (a cortina preta do desmaio). Quem poe e o `World`.</summary>
	public Visao? Veu;

	/// <summary>O corpo do jogador (o que sobe acima da cortina). Quem poe e o `World`.</summary>
	public Node2D? Corpo;

	private ColorRect? _turva;
	private ShaderMaterial? _mat;
	private TextureRect? _escuro;
	private float _agora;
	private bool _desmaioPelaCabeca, _desmaiadoNaTela;
	private int _zDoVeu, _zDoCorpo;

	/// <summary>A visao turva que a tela mostra AGORA, de 0 a 1 (suavizada). Pra bancada e pro robo de fotos.</summary>
	public float TurvaNaTela => _agora;

	/// <summary>A tela esta preta do desmaio pela cabeca agora? Pra bancada e pro robo de fotos.</summary>
	public bool DesmaioNaTela => _desmaiadoNaTela;

	public override void _Ready()
	{
		Layer = Camada;

		_mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Assets/Shaders/Altitude.gdshader") };
		// a "distancia" que lava a cor e o ESCURO: o mesmo borrao da altitude, puxando pro preto em vez do azul
		_mat.SetShaderParameter("ceu", new Color(0.03f, 0.01f, 0.01f));
		_mat.SetShaderParameter("altura", 0f);
		_turva = new ColorRect
		{
			Name = "Turva", Material = _mat, Color = Colors.White,
			MouseFilter = Control.MouseFilterEnum.Ignore, LightMask = 0, Visible = false,
		};
		_turva.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_turva);

		// O ESCURO: transparente no meio, quase preto na beirada (o RGB do trecho transparente ja e preto -- nao ha anel sujo).
		var gradiente = new Gradient
		{
			Offsets = [0f, 0.35f, 0.75f, 1f],
			Colors = [new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.75f), new Color(0f, 0f, 0f, 0.95f)],
		};
		_escuro = new TextureRect
		{
			Name = "Escuro",
			Texture = new GradientTexture2D
			{
				Gradient = gradiente, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
				Width = 128, Height = 128,
			},
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			TextureFilter = CanvasItem.TextureFilterEnum.Linear,
			LightMask = 0,
			Visible = false,
		};
		_escuro.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_escuro);
	}

	/// <summary>A vida da cabeca do meu corpo, 0-100 (a perdida conta como inteira: perder a cabeca e outra historia -- morte ou coma).</summary>
	public static int VidaDaCabeca(List<Protocol.ParteState>? partes)
	{
		if (partes == null) return 100;
		foreach (Protocol.ParteState p in partes)
			if (p.Nome == "Cabeca") return p.Decepado ? 100 : p.Vida;
		return 100;
	}

	/// <summary>Quanto a visao turva com a cabeca nesta vida: 0 de 60% pra cima, 1 na linha do nocaute pra baixo.</summary>
	public static float TurvaDaCabeca(int vida) => Mathf.Clamp((ComecaATurvar - vida) / (float)(ComecaATurvar - LinhaDoNocaute), 0f, 1f);

	public override void _Process(double delta)
	{
		GameClient? cli = GameClient.Instance;
		bool caido = cli?.Sheet.KO == true;
		int cabeca = VidaDaCabeca(cli?.Corpo);

		// O DESMAIO PELA CABECA E A MESMA PERGUNTA DO SERVIDOR: caido, com a cabeca abaixo da linha em algum momento deste nocaute.
		if (!caido) _desmaioPelaCabeca = false;
		else if (cabeca < LinhaDoNocaute) _desmaioPelaCabeca = true;
		Desmaiar(caido && _desmaioPelaCabeca);

		// com a tela preta nao ha o que turvar; de pe (ou caido pelo tronco), a cabeca diz quanto
		float alvo = _desmaiadoNaTela ? 0f : TurvaDaCabeca(cabeca);
		_agora = Mathf.MoveToward(_agora, alvo, Perseguicao * (float)delta);
		bool ve = _agora > 0.001f;
		if (_turva != null) _turva.Visible = ve;
		if (_escuro != null)
		{
			_escuro.Visible = ve;
			_escuro.Modulate = new Color(1f, 1f, 1f, _agora * 0.9f);
		}
		// a raiz: o shader eleva a altura ao quadrado, e aqui a turva tem que crescer por igual com o ferimento
		if (ve) _mat?.SetShaderParameter("altura", Mathf.Sqrt(_agora));
	}

	/// <summary>
	/// A TELA PRETA DO DESMAIO: a cortina do veu (`Visao.Desmaiado`) e o corpo do jogador acima dela. O z de antes de cada um volta
	/// quando o corpo levanta.
	/// </summary>
	private void Desmaiar(bool desmaiado)
	{
		if (desmaiado == _desmaiadoNaTela) return;
		_desmaiadoNaTela = desmaiado;
		if (Veu != null && IsInstanceValid(Veu))
		{
			if (desmaiado) _zDoVeu = Veu.ZIndex;
			Veu.Desmaiado = desmaiado;
			Veu.ZIndex = desmaiado ? ZDoDesmaio : _zDoVeu;
		}
		if (Corpo != null && IsInstanceValid(Corpo))
		{
			if (desmaiado) _zDoCorpo = Corpo.ZIndex;
			Corpo.ZIndex = desmaiado ? ZDoDesmaio + 1 : _zDoCorpo;
		}
	}
}
