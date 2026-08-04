using Godot;

namespace Jandirus.Client;

/// <summary>
/// O DESENHO DA AURA -- e ele e o sprite do jogo antigo, colorido, nao um efeito inventado.
///
/// ============================ UM SPRITE SO, TINGIDO ============================
/// Procurando "a aura de cada forma" no DM nao se acha uma por forma: acha-se UMA,
/// `colorablebigaura.dmi` (`Power Control.dm:9`, e escolhivel em `Settings.dm:410`). O que muda
/// de forma pra forma e a COR -- `centerAura()` faz
/// `icolor = rgb(container.AuraR, container.AuraG, container.AuraB)`.
///
/// Isso casa com o port sem esforco: `FormaDef.Aura` ja e uma cor em hexa. Entao ha um desenho e
/// uma paleta, e nao dezoito arquivos pra manter em sincronia.
/// (`Aurabigcombined.dmi`, que aparece em doze lugares, e o FLASH das cinematicas -- outra coisa.)
/// ==============================================================================
///
/// ============================ SPRITE NAO E LUZ ============================
/// O dono foi explicito duas vezes: "o brilho causado por aura e so quando ESTA TRANSFORMADO, na
/// base aura nao gera brilho". Entao as duas camadas sao INDEPENDENTES:
///
///   reunindo energia na base  ->  SO este sprite
///   transformado (qualquer)   ->  este sprite + a <see cref="Aura"/> (PointLight2D)
///
/// Manter as duas separadas e o que permite obedecer isso sem `if` espalhado: quem acende luz e
/// so quem trata de forma.
/// ==========================================================================
/// </summary>
public partial class SpriteDeAura : Node2D
{
	/// <summary>A folha do original: 4 quadros, desenhados pra serem coloridos por cima.</summary>
	private const string Folha = "res://Assets/Sprites/Auras/colorablebigaura.tres";

	/// <summary>
	/// Segundos de um ciclo da aura. O .dmi nao traz duracao util (o BYOND animava no proprio
	/// relogio), entao o valor e escolhido: rapido o bastante pra parecer energia agitada, lento
	/// o bastante pra nao virar estroboscopio atras do personagem.
	/// </summary>
	private const double Ciclo = 0.32;

	/// <summary>
	/// O `ICON_ADD` de novo, e pelo mesmo motivo do corpo: `modulate` MULTIPLICA, e a aura ja vem
	/// desenhada em tons claros -- multiplicar por dourado daria um borrao escuro sem os fachos.
	/// Somar clareia e preserva o desenho. E o `blend_mode = BLEND_MODE_ADD` do original.
	/// </summary>
	/// <summary>
	/// O CODIGO DESTE EFEITO mora num `.gdshader` de verdade -- ver o comentario de
	/// <see cref="CharacterVisual"/>: efeito procedural nao se acerta lendo codigo, se acerta
	/// arrastando o valor e OLHANDO, e pra isso ele precisa abrir no editor do Godot.
	/// </summary>
	private const string CaminhoDoShader = "res://Assets/Shaders/Aura.gdshader";

	private static Shader? _shader;
	private static Shader Sh => _shader ??= ResourceLoader.Load<Shader>(CaminhoDoShader);

	private AnimatedSprite2D? _s;
	private ShaderMaterial? _mat;
	private double _relogio;
	private int _quadros;

	private bool _aceso;
	private Color _cor = Colors.White;
	private float _forca = 1f;

	public override void _Ready()
	{
		// ATRAS DO CORPO. Por cima, a aura cobriria o rosto e o personagem viraria uma mancha --
		// e no original ela tambem fica em `UNDERAURA_LAYER` quando `container.Over` e falso.
		ZIndex = -4;
		Visible = false;
		SetProcess(false);
	}

	/// <summary>
	/// Acende (ou apaga) o desenho. <paramref name="forca"/> 1 e a aura comum; acima disso ela
	/// fica mais densa, o que separa visualmente o esforco de carregar de uma transformacao.
	/// </summary>
	public void Definir(bool aceso, Color cor, float forca = 1f)
	{
		_cor = cor;
		_forca = forca;

		if (aceso == _aceso) { Pintar(); return; }
		_aceso = aceso;

		if (!aceso)
		{
			Visible = false;
			SetProcess(false);
			return;
		}

		Montar();
		_relogio = 0;
		Visible = true;
		SetProcess(true);
		Pintar();
	}

	/// <summary>
	/// Monta o sprite na PRIMEIRA vez que a aura acende, e nao no `_Ready`.
	///
	/// A grande maioria dos corpos da zona nunca acende aura nenhuma; criar o node e carregar a
	/// folha pra todos custaria em cada personagem que entra no campo de visao. Depois de montado
	/// ele fica -- acender de novo e so trocar `Visible`.
	/// </summary>
	private void Montar()
	{
		if (_s != null) return;

		var frames = ResourceLoader.Load<SpriteFrames>(Folha);
		if (frames == null) { GD.PushWarning("[aura] sem colorablebigaura.tres"); return; }

		string anim = frames.HasAnimation("default") ? "default"
					: frames.GetAnimationNames() is { Length: > 0 } n ? n[0] : "";
		if (anim.Length == 0) return;

		_quadros = Mathf.Max(1, frames.GetFrameCount(anim));
		_mat = new ShaderMaterial { Shader = Sh };
		_s = new AnimatedSprite2D
		{
			Name = "Desenho",
			SpriteFrames = frames,
			Animation = anim,
			Centered = true,
			Material = _mat,
			TextureFilter = TextureFilterEnum.Nearest,
		};
		AddChild(_s);
	}

	public override void _Process(double delta)
	{
		if (_s == null || _quadros <= 1) return;
		// RELOGIO PROPRIO em vez de `Play()`: o mesmo motivo das camadas do corpo -- assim a
		// cadencia e nossa e nao depende do que o conversor de .dmi tiver escrito na folha.
		_relogio = (_relogio + delta) % Ciclo;
		int q = Mathf.Clamp((int)(_relogio / Ciclo * _quadros), 0, _quadros - 1);
		if (_s.Frame != q) _s.Frame = q;
	}

	private void Pintar()
	{
		if (_mat == null) return;
		_mat.SetShaderParameter("cor", new Vector3(_cor.R, _cor.G, _cor.B));
		_mat.SetShaderParameter("forca", _forca);
	}
}
