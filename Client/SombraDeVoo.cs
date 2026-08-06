using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A SOMBRA DE QUEM ESTA NO AR.
///
/// ============================ POR QUE ELA E NECESSARIA, E NAO ENFEITE ============================
/// Num jogo visto de cima, "subiu" e "andou pra cima" desenham a MESMA coisa: o sprite mais alto na
/// tela. Sem um segundo sinal nao ha como o jogador distinguir os dois -- e como o corpo que voa e
/// deslocado pra cima (<see cref="Voo.EscalaNaTela"/>) enquanto a POSICAO real continua no chao, a
/// pergunta "onde eu estou de verdade?" fica sem resposta na tela.
///
/// A sombra responde: ela fica na posicao real, no chao, e o vao entre ela e o corpo E a altura.
/// Quanto mais alto, menor e mais apagada -- que e o que a luz de cima faz.
///
/// NAO SE CONFUNDE COM A OUTRA "SOMBRA" DO PROJETO. O que `Client/Visao.cs` chama de sombra e
/// oclusao de VISAO (o que o personagem enxerga). Isto aqui e a mancha embaixo do corpo. Sao dois
/// sistemas sem nada em comum alem da palavra.
/// ============================================================================================
/// </summary>
public partial class SombraDeVoo : Node2D
{
	/// <summary>O raio da mancha no chao (colado), em pixels.</summary>
	private const float RaioNoChao = 9f;

	/// <summary>Quanto ela encolhe no teto -- longe da luz, a sombra se fecha.</summary>
	private const float MenorEscala = 0.35f;

	/// <summary>Opacidade colado no chao. Nunca chega a preto: e sombra, nao buraco.</summary>
	private const float OpacidadeNoChao = 0.45f;

	private float _altura;

	/// <summary>Altura do corpo em pixels. Zero apaga a sombra.</summary>
	public float Altura
	{
		set
		{
			if (Mathf.IsEqualApprox(_altura, value)) return;
			_altura = value;
			Visible = value > 0.5f;
			QueueRedraw();
		}
	}

	public override void _Ready()
	{
		// ATRAS DO CORPO E DE QUALQUER COISA DO ATOR. A sombra e chao; se ela desenhasse por cima,
		// o personagem apareceria com uma mancha escura na barriga.
		ZIndex = -1;
		ZAsRelative = true;
		Visible = false;
	}

	public override void _Draw()
	{
		if (_altura <= 0.5f) return;

		float f = Voo.Fracao(_altura);
		float escala = Mathf.Lerp(1f, MenorEscala, f);
		float alfa = Mathf.Lerp(OpacidadeNoChao, 0.10f, f);

		// A MANCHA FICA NOS PES, e nao no centro do sprite -- e a mesma ancora que a colisao usa
		// (`MoveRules.FeetOffsetY`). Desenhar no centro deixaria a sombra flutuando na cintura.
		var centro = new Vector2(0, MoveRules.FeetOffsetY);

		// Elipse achatada: circulo perfeito no chao le como bola, nao como sombra de corpo em pe.
		DrawSetTransform(centro, 0f, new Vector2(escala, escala * 0.45f));
		DrawCircle(Vector2.Zero, RaioNoChao, new Color(0, 0, 0, alfa));
		DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
	}
}
