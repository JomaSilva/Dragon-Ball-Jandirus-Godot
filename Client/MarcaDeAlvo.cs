using Godot;

namespace Jandirus.Client;

/// <summary>
/// O ANEL QUE MARCA O ALVO. Fica no chao, aos pes de quem voce escolheu.
///
/// NO CHAO, e nao em cima da cabeca, por uma razao pratica: a cabeca ja tem a barra de vida e
/// o nome, e num amontoado de gente a marca em cima some no meio deles. Aos pes ela fica no
/// unico lugar que so pertence a um personagem.
///
/// GIRA DEVAGAR. Uma marca parada some do olho depois de dois segundos; o giro lento e o que
/// faz o olho voltar nela sozinho no meio da briga sem que ela precise piscar ou gritar.
/// </summary>
public partial class MarcaDeAlvo : Node2D
{
	private const float Raio = 15f;
	private const int Lados = 3;

	private double _t;

	public override void _Ready()
	{
		// ATRAS do personagem: a marca e chao, e chao nao cobre ninguem
		ZIndex = -3;
		Modulate = Tema.Destaque;
	}

	public override void _Process(double delta)
	{
		_t += delta;
		QueueRedraw();
	}

	public override void _Draw()
	{
		// ACHATADO: e uma marca no CHAO vista de cima. Um circulo redondo lia como bolha em
		// volta do personagem; achatado le como sombra marcada no piso.
		const float achatar = 0.45f;
		const int porArco = 7;
		float giro = (float)_t * 1.1f;
		var cor = new Color(1, 1, 1, 0.95f);

		// TRES ARCOS, nao um anel fechado: o anel fechado se confunde com a sombra do
		// personagem; os tres pedacos leem como mira.
		var p = new Vector2[porArco + 1];
		for (int i = 0; i < Lados; i++)
		{
			float a0 = giro + Mathf.Tau * i / Lados;
			for (int k = 0; k <= porArco; k++)
			{
				float a = a0 + 0.5f * k / porArco;
				p[k] = new Vector2(Mathf.Cos(a) * Raio, Mathf.Sin(a) * Raio * achatar);
			}
			DrawPolyline(p, cor, 2.5f, true);
		}
	}
}
