using Godot;

namespace Jandirus.Client;

/// <summary>
/// O ANEL QUE MARCA O ALVO. Fica no chao, aos pes de quem voce escolheu.
///
/// NO CHAO, e nao em cima da cabeca, por uma razao pratica: num amontoado de gente uma marca em
/// cima some no meio das cabecas e dos baloes de fala. Aos pes ela fica no unico lugar que so
/// pertence a um personagem.
///
/// (Este comentario dizia "a cabeca ja tem a barra de vida e o nome". Nome nunca foi desenhado no
/// mundo, e a barra de vida foi deletada a pedido do dono -- ver `EntityState`.)
///
/// GIRA DEVAGAR. Uma marca parada some do olho depois de dois segundos; o giro lento e o que
/// faz o olho voltar nela sozinho no meio da briga sem que ela precise piscar ou gritar.
/// </summary>
/// <remarks>
/// <see cref="IFicaNoChao"/>, e este e o caso que quase se perdeu nos dois sentidos.
///
/// Ela e filha do corpo alheio (`World.MarcarNaCena`) e NUNCA esteve na lista de "quem sobe com o
/// voo" -- por acidente, porque aquela lista era escrita a mao e ninguem a revisitou quando a marca
/// nasceu. O acidente calhou de dar o resultado CERTO: ela e um decalque de piso (achatado, `ZIndex`
/// -3, atras de todo mundo) e o lugar dela e o chao, ao lado da sombra, dizendo onde o sujeito esta
/// de verdade. Anel achatado boiando a 160 px do chao nao le como mira, le como defeito.
///
/// Quando a regra virou "todo filho visual sobe", ela passou a subir sozinha -- e junto levaria uma
/// segunda quebra: o `Position = (0, 14)` que a poe AOS PES seria apagado pela escrita da subida.
/// Por isso a declaracao. E o exemplo de que a inversao nao e "mover tudo": e "mover tudo menos
/// quem tiver motivo, dito onde da pra ver o motivo".
/// </remarks>
/// <remarks>
/// <see cref="INaoSomeComOCorpo"/> pela mesma leitura que a poe no chao: ela nao e o sujeito, e a
/// MIRA em cima dele. Some-la junto com o corpo durante a esquiva
/// (<see cref="EsquivaZanzoken"/>) faria a mira piscar a cada soco desviado -- e desviar e o que
/// mais acontece contra alguem mais forte.
/// </remarks>
public partial class MarcaDeAlvo : Node2D, IFicaNoChao, INaoSomeComOCorpo
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
