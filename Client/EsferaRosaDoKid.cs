using Godot;
using Jandirus.Core.Combat;
using static Jandirus.Client.ArteDeKiNoCliente;

namespace Jandirus.Client;

/// <summary>
/// (2026-11-01, o pedido 218) A ESFERA ROSA DO KID BOO -- o dono: *"use o mesmo efeito da death ball mas rosa"* e, corrigindo a primeira versao
/// (a folha `deathball2017purple2` pintada): *"N é q death ball sprite nao é a deathball shaders"*. E a Death Ball da TECNICA -- o mesmo estilo
/// de bola (`ArteDeKi.DeathBall2017Purple2`: miolo escuro, casca acesa, descargas) pelo mesmo pintor de ki (`PintorDeKi`, o shader da esfera)
/// --, com a rampa rosa no lugar da roxa. Quem a posiciona e a cresce e a `SupernovaDesenhada`, pelos prazos do ultimato (`Diametro`).
/// </summary>
public partial class EsferaRosaDoKid : Node2D
{
	/// <summary>Os tres tons da esfera do Kid: a casca rosa-escura, o manto rosa aceso e o miolo branco (a Death Ball tem roxo nos dois primeiros).</summary>
	public static readonly Rampa TonsRosa = Rampa.De("a3105c", "ff4fae", "ffffff");

	/// <summary>O diametro desenhado agora, em px -- escrito a cada quadro pela `SupernovaDesenhada`.</summary>
	public float Diametro = 64f;

	private ShaderMaterial? _mat;
	private EstiloDeBola _estilo = EstiloDaBola(ArteDeKi.DeathBall2017Purple2);
	private double _idade;

	/// <summary>O shader da Death Ball carregou (a bancada: o robo `--diagbuu`).</summary>
	public bool Vestida => _mat != null;

	public override void _Ready()
	{
		_estilo = EstiloDaBola(ArteDeKi.DeathBall2017Purple2) with { Tons = TonsRosa };
		// a tinta e PRETA: a rampa entra como esta (a soma do ki de quem atira, `Rampa.Com`, nao clareia o rosa)
		_mat = PintorDeKi.MaterialDeBola(_estilo, _estilo.Raio, new Color(0, 0, 0));
		Material = _mat;
	}

	public override void _Process(double delta)
	{
		_idade += delta;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_mat == null || Diametro <= 1f) return;
		_mat.SetShaderParameter("tempo", (float)_idade);
		_mat.SetShaderParameter("rastro", 0f);
		DrawSetTransform(Vector2.Zero, 0f, Vector2.One * (Diametro / (2f * _estilo.Raio)));
		PintorDeKi.Quadro(this, Vector2.Zero, Vector2.Right, PintorDeKi.MeiaDaBola(_estilo, _estilo.Raio), 0f);
	}
}
