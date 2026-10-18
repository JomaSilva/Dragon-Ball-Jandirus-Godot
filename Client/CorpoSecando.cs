using Godot;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-17) A VITIMA DA EXECUCAO DO BIO-ANDROIDE, SECANDO -- o adendo do dono: *"After knocking an opponent into the death state, you
/// can perform an execution by absorbing their life essence, causing their body to dry up and vanish"*.
///
/// O DM nao desenha a absorcao do bio (o `Bio_Absorb` so toca o `absorb.wav`, `Absorption.dm:157`); o desenho e o pedido. Tres coisas, no
/// mesmo relogio -- os 5 s da execucao (`BioAndroids.SegundosDaExecucao`, o prazo do servidor):
///   * o CORPO INTEIRO vai ficando cor de palha (`CharacterVisual.Secar`: o RGB do `Modulate` da raiz, que multiplica corpo, roupa e cabelo
///     juntos);
///   * a ESSENCIA saindo dele: fiapos claros que sobem e se apagam, cada vez mais densos;
///   * e no fim ele SOME. Quem tira o corpo do mundo e o servidor (o NPC no dreno do tique, o jogador na viagem pro Outro Mundo); a tela, ao
///     ver sumir um corpo que ja estava seco, solta o PO dele no lugar (`World.AoSair` -> `PoeiraDeEstrago`, na cor <see cref="DoPo"/>).
/// Desfeita a execucao (o bio andou, a vitima levantou), a cor volta em um quarto de segundo e o node sai da arvore.
///
/// Quem o liga e desliga e o `World.MarcarSecando`, pela `Ocupacao.Secando` do snapshot: estado do corpo, entao quem esta em volta ve, e a
/// propria vitima tambem. Quem chega no meio da execucao ve o corpo secar do comeco -- o pacote nao diz ha quanto ela corre.
/// </summary>
public partial class CorpoSecando : Node2D
{
	public const string NomeDoNode = "CorpoSecando";

	/// <summary>A COR DO CORPO SECO, multiplicada no boneco inteiro: palha -- o azul e o verde vao embora primeiro, o que sobra e couro.</summary>
	public static readonly Color Seco = new(0.74f, 0.62f, 0.44f);

	/// <summary>A cor do po em que ele se desfaz: a mesma palha, mais clara (particulado espalha luz).</summary>
	public static readonly Color DoPo = new(0.8f, 0.7f, 0.52f);

	/// <summary>
	/// DAQUI PRA CIMA o corpo que some vira po. O servidor consuma no prazo e o corpo sai no mesmo tique (o jogador, no seguinte); a tela
	/// chegou la no mesmo relogio. Abaixo disto o corpo sumiu com a execucao no meio, e some sem po.
	/// </summary>
	public const float SecoOBastante = 0.85f;

	/// <summary>Em quanto tempo a cor volta quando a execucao se desfaz.</summary>
	private const double SegundosPraVoltar = 0.25;

	/// <summary>
	/// A ESPERA DO CORPO JA SECO antes de a cor voltar. A execucao consumada tambem apaga o estado (a vitima morre, e morto nao e bebido) -- e
	/// o jogador seco so sai do mundo na triagem do tique seguinte. Sem esta espera ele voltaria a cor de gente no meio caminho entre secar e
	/// virar po.
	/// </summary>
	private const double EsperaDoSeco = 0.5;

	private double _t, _espera;

	/// <summary>
	/// O SECAR ANDA NO RELOGIO DE PAREDE, e nao na soma dos `delta` dos quadros: a execucao fecha no relogio do SERVIDOR (5 s reais), e a soma
	/// dos quadros fica pra tras quando a tela engasga -- a janela sem foco roda a 4 quadros por segundo. A `--diagexecucao` pegou o corpo
	/// sumindo antes de chegar no <see cref="SecoOBastante"/>, e sem o po (job84).
	/// </summary>
	private ulong _ultimoQuadro = Time.GetTicksUsec();
	private CpuParticles2D? _essencia;

	/// <summary>A execucao se desfez (ou se consumou): a cor voltando, e depois fora da arvore.</summary>
	public bool Voltando { get; private set; }

	/// <summary>Quanto do corpo ja secou, de 0 a 1 -- a bancada `--diagbio` le, e o `World.AoSair` decide o po por ele.</summary>
	public float Quanto => (float)Math.Clamp(_t / Jandirus.Core.Races.BioAndroids.SegundosDaExecucao, 0, 1);

	/// <summary>Os fiapos da essencia -- a bancada `--diagbio` confere que estao saindo.</summary>
	internal CpuParticles2D? EssenciaDeTeste => _essencia;

	/// <summary>O desenho de um fiapo: um ponto de borda macia (a mesma receita da luz da aura), feito uma vez.</summary>
	private static Texture2D? _fiapo;

	public override void _Ready()
	{
		var rampa = new Gradient();
		rampa.SetColor(0, new Color(1f, 0.95f, 0.75f, 1f));
		rampa.SetColor(1, new Color(DoPo, 0f));
		_essencia = new CpuParticles2D
		{
			Name = "Essencia",
			Texture = _fiapo ??= Aura.Radial(3),
			Amount = 26,
			Lifetime = 0.9,
			EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
			EmissionRectExtents = new Vector2(12, 6),   // o corpo caido, deitado
			Direction = Vector2.Up,
			Spread = 30,
			Gravity = new Vector2(0, -24),
			InitialVelocityMin = 10,
			InitialVelocityMax = 26,
			ScaleAmountMin = 1.0f,
			ScaleAmountMax = 2.2f,
			ColorRamp = rampa,
			ZIndex = 2,
			// SOMADA, e nao por cima: a essencia e luz saindo do corpo, e na grama ela tem que acender (a primeira foto a perdeu no verde).
			Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
		};
		AddChild(_essencia);
		Aplicar();
	}

	public override void _Process(double _)
	{
		ulong agora = Time.GetTicksUsec();
		double delta = (agora - _ultimoQuadro) / 1e6;
		_ultimoQuadro = agora;
		double prazo = Jandirus.Core.Races.BioAndroids.SegundosDaExecucao;
		if (!Voltando) _t = Math.Min(prazo, _t + delta);
		else if (_espera > 0) _espera -= delta;
		else _t = Math.Max(0, _t - delta * prazo / SegundosPraVoltar);
		Aplicar();
		if (Voltando && _espera <= 0 && _t <= 0) QueueFree();
	}

	private void Aplicar()
	{
		float q = Quanto;
		GetParent()?.GetNodeOrNull<CharacterVisual>("Visual")?.Secar(q);
		if (_essencia == null) return;
		_essencia.Emitting = !Voltando;
		_essencia.Modulate = new Color(1, 1, 1, 0.3f + 0.7f * q);
	}

	/// <summary>A execucao acabou: a cor volta -- e, se o corpo ja estava seco, so depois da <see cref="EsperaDoSeco"/>.</summary>
	public void Desfazer()
	{
		if (Voltando) return;
		Voltando = true;
		_espera = Quanto >= SecoOBastante ? EsperaDoSeco : 0;
	}

	/// <summary>Sai JA, sem a volta (a troca de zona: o corpo chega ao destino inteiro, e nao meio seco).</summary>
	public void Sumir()
	{
		GetParent()?.GetNodeOrNull<CharacterVisual>("Visual")?.Secar(0);
		QueueFree();
	}
}
