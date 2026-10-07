using Godot;

namespace Jandirus.Client;

/// <summary>
/// O ESTOURO DE UM ATAQUE DE KI -- o que fica na tela no instante em que o tiro acerta.
///
/// ============================ ELE E DO TAMANHO DO QUE ESTOUROU ============================
/// Todo tiro morria com a mesma faisca: `CombatFx.Impacto`, a folha `attackspark` do soco, em escala
/// 1,3. Enquanto a bola era uma folha de 32 px ninguem via problema. Com os ataques desenhados por
/// shader no tamanho de verdade (pedido do dono, 2026-10-07 -- ver `ProjetilDesenhado`), a Death Ball
/// tem 36 px de raio e a Genkidama 96: a maior coisa que voa no jogo acabava num estalinho do tamanho
/// de um punho. E o dono citou as duas pelo nome (*"esferas como genkidama, super nova"*).
///
/// Entao o estouro recebe o RAIO do que estourou (`ProjetilDesenhado.RaioDoEstouro`) e a COR dele, e
/// e desenhado pelo `EstouroDeKi.gdshader`: clarao, bola de fogo que se desfaz, lascas e anel.
///
/// ============================ O QUE ELE NAO SUBSTITUI ============================
/// A faisca do GOLPE continua sendo a de sempre (`World.AoGolpe`, na cabeca do feixe que mói alguem):
/// aquilo e "acertei", pequeno e repetido. O anel de choque (`CombatFx.Onda`) e a poeira do chao
/// tambem continuam, por cima deste -- sao do `World.AoMorrerTiro`, que decide QUAL fim merece o que.
/// Este node so troca a faisca de soco que estava no lugar de uma explosao.
///
/// ELE SE APAGA SOZINHO: um `Tween` no proprio node anda o `t` de 0 a 1 e o recolhe. Nada o guarda.
/// ==================================================================================
/// </summary>
public partial class EstouroDeKi : Node2D
{
	/// <summary>
	/// Quantos raios do estouro cabem em meio quadro: o anel abre ate ~2,2 raios e as lascas ate ~3,2.
	/// Menos que isso e a borda reta do quad corta as lascas.
	/// </summary>
	private const float RaiosNoQuadro = 3.5f;

	/// <summary>
	/// Quanto dura, em segundos: um piso pro estalo de um tiro pequeno, e um pouco mais por px de raio --
	/// uma bola de 96 px que sumisse no tempo de uma de 5 pareceria ter sido cortada.
	/// </summary>
	private const float SegundosBase = 0.32f, SegundosPorPx = 0.0045f, SegundosMaximos = 0.9f;

	private float _raio = 16f;

	/// <summary>
	/// ESTOURA AQUI. `raio` e o do que estourou, em px de mundo; `cor` e a do manto dele.
	/// </summary>
	public static void Soltar(Node2D pai, Vector2 onde, float raio, Color cor)
	{
		if (ResourceLoader.Load<Shader>(PintorDeKi.ShaderDoEstouro) is not { } sh) return;

		var mat = new ShaderMaterial { Shader = sh };
		mat.SetShaderParameter("raio", raio);
		mat.SetShaderParameter("cor", new Vector3(cor.R, cor.G, cor.B));
		mat.SetShaderParameter("semente", GD.Randf() * 10f);
		mat.SetShaderParameter("t", 0f);

		var no = new EstouroDeKi
		{
			Position = onde,
			ZIndex = PintorDeKi.CamadaDoEstouro,
			Material = mat,
			_raio = raio,
		};
		pai.AddChild(no);

		// AS FAISCAS: as mesmas da estrela do embate -- quadradinhos claros voando pra fora, numa rajada so.
		var faiscas = new CpuParticles2D
		{
			Amount = Mathf.Clamp((int)(raio * 0.9f), 8, 48),
			Lifetime = 0.5,
			OneShot = true,
			Explosiveness = 1f,
			Direction = Vector2.Right,
			Spread = 180f,
			Gravity = Vector2.Zero,
			InitialVelocityMin = raio * 2.5f,
			InitialVelocityMax = raio * 7f,
			DampingMin = raio * 5f,
			DampingMax = raio * 9f,
			ScaleAmountMin = 1f,
			ScaleAmountMax = 2.5f,
			EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere,
			EmissionSphereRadius = raio * 0.5f,
			Color = cor.Lerp(Colors.White, 0.6f),
			Material = new CanvasItemMaterial
			{
				LightMode = CanvasItemMaterial.LightModeEnum.Unshaded,
				BlendMode = CanvasItemMaterial.BlendModeEnum.Add,
			},
			ColorRamp = new Gradient { Colors = [Colors.White, new Color(1, 1, 1, 0)], Offsets = [0.3f, 1f] },
			Emitting = true,
		};
		no.AddChild(faiscas);

		// `Tween` NO PROPRIO NODE, e nao lambda solta num timer da arvore: ele morre junto com o node (a
		// nota das assinaturas orfas em `Transformacao.TocarPedras`).
		float segundos = Mathf.Min(SegundosBase + raio * SegundosPorPx, SegundosMaximos);
		Tween tw = no.CreateTween();
		tw.TweenMethod(Callable.From<float>(v => mat.SetShaderParameter("t", v)), 0f, 1f, segundos);
		tw.TweenInterval(0.25);   // as ultimas faiscas ainda estao no ar quando a bola some
		tw.TweenCallback(Callable.From(no.QueueFree));
	}

	public override void _Draw() => PintorDeKi.Quadro(this, Vector2.Zero, Vector2.Right, _raio * RaiosNoQuadro);
}
