using Godot;

namespace Jandirus.Client;

/// <summary>
/// A POEIRA DE UMA CELULA QUE CAIU -- terra, cascalho e fumaca subindo.
///
/// ============================ DE ONDE VEM ============================
/// O `createDust(T, 1)` do original, que o `turf/proc/Destroy` chama antes de trocar o turf
/// (`NewTurfs.dm`) e que o `Ticked` do knockback chama quando a parede cede. La e um sprite de
/// nuvem; aqui e particula, porque o pipeline nao converte aquele .dmi e porque particula responde
/// a direcao -- o cascalho pode sair pro lado em que o corpo bateu.
///
/// O dono: "ao quebrar n tem efeito nenhum (uma fumaca ou algo)... particulas 2d pra representar
/// terra voando ou pedras voando e fumaca".
/// =====================================================================
///
/// SAO DOIS SISTEMAS, e nao um com o dobro de particulas: cascalho e fumaca se movem por regras
/// diferentes. O cascalho e pesado -- sai rapido, cai por gravidade e some inteiro. A fumaca e
/// leve -- sobe devagar, cresce e desbota. Um sistema so teria que escolher entre os dois e o
/// resultado seria uma nuvem de pedra flutuante.
///
/// MORRE SOZINHO. As duas emissoes sao `OneShot`, e o node se apaga quando a mais longa termina --
/// senao cada parede derrubada deixaria um emissor parado no mapa pra sempre.
/// </summary>
public partial class PoeiraDeEstrago : Node2D
{
	/// <summary>Quanto o efeito inteiro dura, em segundos. E a vida da fumaca, que e a mais longa.</summary>
	private const double Duracao = 1.1;

	private double _resta = Duracao;

	/// <summary>
	/// Solta a poeira numa celula. <paramref name="rumo"/> e a direcao do impacto -- o cascalho
	/// voa preferencialmente pra la. Vetor zero espalha pra todo lado (chao rachando).
	/// </summary>
	public static void Soltar(Node pai, Vector2 onde, Vector2 rumo = default)
	{
		var p = new PoeiraDeEstrago { Position = onde, ZIndex = 1 };
		pai.AddChild(p);
		p.Montar(rumo);
	}

	private void Montar(Vector2 rumo)
	{
		bool dirigido = rumo.LengthSquared() > 1e-6f;
		Vector2 dir = dirigido ? rumo.Normalized() : Vector2.Up;

		// ---- CASCALHO: pesado, rapido, cai ----
		var pedras = new GpuParticles2D
		{
			Amount = 14,
			Lifetime = 0.55,
			OneShot = true,
			Explosiveness = 1f,          // tudo no mesmo instante: e um estouro, nao um vazamento
			Emitting = true,
			Texture = Quadrado(3, new Color(0.42f, 0.32f, 0.22f)),
			ProcessMaterial = new ParticleProcessMaterial
			{
				Direction = new Vector3(dir.X, dir.Y, 0),
				// ESPALHAMENTO ESTREITO quando ha rumo (o corpo bateu ALI) e total quando nao ha
				// (o chao rachou sozinho, e a pedra sai pra todo lado).
				Spread = dirigido ? 55f : 180f,
				InitialVelocityMin = 60f,
				InitialVelocityMax = 190f,
				Gravity = new Vector3(0, 420, 0),   // pedra cai
				ScaleMin = 0.6f,
				ScaleMax = 1.6f,
				AngularVelocityMin = -360f,
				AngularVelocityMax = 360f,
				Damping = new Vector2(20, 60),
			},
		};
		AddChild(pedras);

		// ---- FUMACA: leve, lenta, sobe e cresce ----
		var fumaca = new GpuParticles2D
		{
			Amount = 10,
			Lifetime = Duracao,
			OneShot = true,
			Explosiveness = 0.85f,
			Emitting = true,
			Texture = Quadrado(10, new Color(0.55f, 0.50f, 0.44f)),
			// SOMA NAO: fumaca ESCONDE o que esta atras. Em `Add` ela clarearia o chao e viraria um
			// clarao esbranquicado no meio do estrago.
			Modulate = new Color(1, 1, 1, 0.45f),
			ProcessMaterial = new ParticleProcessMaterial
			{
				Direction = new Vector3(0, -1, 0),
				Spread = 70f,
				InitialVelocityMin = 12f,
				InitialVelocityMax = 46f,
				Gravity = new Vector3(0, -22, 0),   // sobe
				ScaleMin = 1.0f,
				ScaleMax = 2.4f,
				Damping = new Vector2(8, 20),
			},
		};
		AddChild(fumaca);
	}

	/// <summary>
	/// A "textura" de cada particula: um quadrado de cor.
	///
	/// Sem arquivo de propósito. Um .png de poeira seria mais um asset pra converter, versionar e
	/// combinar com a paleta de cada planeta; num efeito de meio segundo, a diferenca entre um
	/// quadradinho e um grao desenhado nao chega ao olho -- o que chega e o movimento.
	/// </summary>
	/// <summary>As texturas ja feitas. Ver dentro do <see cref="Quadrado"/>.</summary>
	private static readonly Dictionary<string, ImageTexture> _cache = [];

	private static ImageTexture Quadrado(int lado, Color cor)
	{
		// ============================ DUAS TEXTURAS, UMA VEZ ============================
		// Sao sempre os MESMOS dois quadrados de cor chapada (3x3 e 10x10). Sem cache, cada celula
		// de cenario derrubada criava DUAS texturas de GPU novas -- e uma rajada de destruicao
		// derruba de tres a dez celulas no mesmo quadro.
		// ===============================================================================
		string chave = lado + ":" + cor.ToRgba32();
		if (_cache.TryGetValue(chave, out ImageTexture? pronta)) return pronta;

		Image img = Image.CreateEmpty(lado, lado, false, Image.Format.Rgba8);
		img.Fill(cor);
		ImageTexture nova = ImageTexture.CreateFromImage(img);
		_cache[chave] = nova;
		return nova;
	}

	public override void _Process(double delta)
	{
		_resta -= delta;
		if (_resta <= 0) QueueFree();
	}
}
