using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A CORRENTE DE PODER DO DU, NA TELA -- pedido do dono, 2026-10-19 ============================
/// *"coloque um efeito q tinha no E:\Users\Joao\Desktop\DU-SOURCE-master quando da poder pro amigo"*.
///
/// E o `Give_Power` do DU (`Code/Combat/Skills.dm:743-746`): enquanto a doacao corre, a cada `sleep(1)` -- 0,1 s -- um
/// `missile()` com o `Give Power.dmi` voa do doador ate o alvo:
///
///     O.icon = 'Give Power.dmi' ; O.layer = layer+1
///     spawn while(src && Giving_Power && M)
///         Missile(O, src, M)
///         sleep(1)
///
/// A arte e a MESMA, convertida byte a byte (`Assets/Sprites/DU/VFX/Give Power.png`): uma espiral verde translucida de 32 px, cinco
/// quadros a 0,1 s, uma direcao so. Cada bolinha mira onde o alvo estava quando saiu (o `missile()` do BYOND e assim), entao a
/// corrente segue quem anda. Fica logo acima dos corpos, como o `layer+1` de la.
///
/// UM NO POR DOADOR, VIVO ENQUANTO OS FOLEGOS CHEGAM. O servidor manda um `S2C.DandoPoder` a cada dose (0,2 s); cada um empurra o
/// fim da corrente pra <see cref="SegundosDeFolego"/> adiante. Sem folego novo ela para de soltar bolinhas, espera as que estao no
/// ar chegarem, e sai da arvore sozinha.
///
/// DUAS COISAS QUE O DU NAO DIZ, e a escolha de cada uma:
///   * A VELOCIDADE: o `missile()` do BYOND anda na velocidade do motor, que o DU nunca escolhe (e o servidor sem janela nao a
///     desenha pra medir). Ficou <see cref="PxPorSegundo"/>: dez tiles por segundo, uma volta inteira da espiral a cada cinco tiles;
///   * A NOITE: a bolinha e 24% opaca, e a noite do port (`CanvasModulate`) a apagaria. Ela ganha o brilho da noite que a esfera
///     verde do Demonic ganhou a pedido do dono (`EsferaDemoniaca.BrilhoDaNoite`), sem luz propria -- dez bolinhas por segundo com
///     uma `PointLight2D` cada passariam do teto de luzes.
/// ==========================================================================================================================
/// </summary>
public partial class CorrenteDePoder : Node2D
{
	/// <summary>A espiral verde do DU (`Icons/VFX/Give Power.dmi`), ja convertida.</summary>
	public const string Folha = "res://Assets/Sprites/DU/VFX/Give Power.tres";

	/// <summary>`sleep(1)` do laco do DU: uma bolinha a cada decimo de segundo.</summary>
	public const double SegundosEntreBolinhas = 0.1;

	/// <summary>
	/// QUANTO UM FOLEGO SEGURA A CORRENTE: o servidor manda um a cada 0,2 s (a dose); a folga cobre um pacote atrasado sem a
	/// corrente piscar, e e curta o bastante pra ela parar logo depois do fim da doacao.
	/// </summary>
	public const double SegundosDeFolego = 0.35;

	/// <summary>A velocidade de cada bolinha: dez tiles por segundo (ver o cabecalho).</summary>
	public const float PxPorSegundo = 320f;

	/// <summary>O brilho que a noite soma no desenho -- o mesmo da esfera verde do Demonic.</summary>
	public const float BrilhoDaNoite = EsferaDemoniaca.BrilhoDaNoite;

	/// <summary>DEFEITO INJETADO (robo `--diagcorrente`): o folego chega e nada e desenhado -- o jogo de antes. Falso em jogo, sempre.</summary>
	public static bool ApagadaDeTeste;

	/// <summary>Quantas bolinhas ja voaram neste processo. So pra bancada.</summary>
	public static int BolinhasSoltasDeTeste;

	/// <summary>A ultima bolinha solta: de onde, pra onde e a folha. So pra bancada.</summary>
	public static (Vector2 De, Vector2 Para, string Folha)? UltimaBolinhaDeTeste;

	private readonly int _doador;
	private readonly Func<int, Vector2?> _ondeEsta;
	private SpriteFrames? _folha;
	private int _alvo;
	private double _t, _ate, _proxima;

	/// <param name="doador">Quem da o poder (a corrente sai dele).</param>
	/// <param name="ondeEsta">Onde um corpo esta DESENHADO agora (o centro do boneco, com a altura do voo), ou nulo se ele nao esta na tela.</param>
	public CorrenteDePoder(int doador, Func<int, Vector2?> ondeEsta)
	{
		_doador = doador;
		_ondeEsta = ondeEsta;
	}

	/// <summary>Quantas bolinhas desta corrente estao no ar agora. So pra bancada.</summary>
	public int BolinhasNoArDeTeste => GetChildCount();

	/// <summary>Ja pediu pra sair da arvore: um folego que chegue agora precisa de uma corrente nova (`World.AoDarPoder`).</summary>
	public bool Encerrando { get; private set; }

	public override void _Ready()
	{
		ZIndex = 5;   // logo acima dos corpos, como o `layer+1` do DU
		_folha = FolhasPresas.Carregar(Folha);
		if (_folha == null) GD.PushError($"[corrente] a folha da corrente nao carregou ({Folha})");
	}

	/// <summary>Um folego do servidor: a corrente vai (ou continua indo) pro <paramref name="alvo"/> por mais um instante.</summary>
	public void Folego(int alvo)
	{
		_alvo = alvo;
		_ate = _t + SegundosDeFolego;
	}

	public override void _Process(double delta)
	{
		_t += delta;
		float noite = Iluminacao.ForcaDaNoite();
		float brilho = 1f + BrilhoDaNoite * noite;
		Modulate = new Color(brilho, brilho, brilho, 1f);

		if (_t >= _ate)
		{
			if (GetChildCount() == 0) { Encerrando = true; QueueFree(); }
			return;
		}
		// sem rajada depois de um quadro longo: o laco do DU solta uma por volta, e nao recupera as que perdeu
		if (_proxima < _t - SegundosEntreBolinhas) _proxima = _t;
		while (_proxima <= _t)
		{
			Soltar();
			_proxima += SegundosEntreBolinhas;
		}
	}

	/// <summary>Uma bolinha: nasce no doador e voa ate onde o alvo esta AGORA; chegou, some.</summary>
	private void Soltar()
	{
		if (_folha == null || _ondeEsta(_doador) is not { } de || _ondeEsta(_alvo) is not { } para) return;
		var bolinha = new AnimatedSprite2D
		{
			SpriteFrames = _folha,
			Animation = "default",
			Position = de,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
		};
		AddChild(bolinha);
		bolinha.Play();
		float segundos = Mathf.Max(de.DistanceTo(para) / PxPorSegundo, 0.05f);
		Tween voo = bolinha.CreateTween();
		voo.TweenProperty(bolinha, "position", para, segundos);
		voo.TweenCallback(Callable.From(bolinha.QueueFree));
		BolinhasSoltasDeTeste++;
		UltimaBolinhaDeTeste = (de, para, Folha);
	}
}
