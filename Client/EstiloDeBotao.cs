using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A CHAPA DE BOTAO DO JOGO (2026-11-01, o pedido 215) ============================
/// O dono mandou duas fotos de menu (`pedido/215/` no rascunho) e pediu: *"Faca os botoes dos menus do jogo nesse estilo, botoes de escolher
/// gravidade, % do peso, etc, manter um padrao visual no jogo"*. O estilo das fotos: um PARALELOGRAMO inclinado, com DEGRADE vertical (claro
/// em cima, mais fundo embaixo), uma BORDA grossa escura e um BRILHO fino logo abaixo da borda de cima -- azul no botao comum, amarelo-laranja
/// no que esta em destaque.
///
/// O `StyleBoxFlat` do Godot inclina (`Skew`) mas nao faz degrade, entao esta chapa se desenha sozinha (`_Draw`): o poligono de fora na cor
/// da borda, o de dentro (recuado a largura da borda, com a MESMA inclinacao) com uma cor por canto -- o degrade --, e o brilho por cima.
/// Sem preenchimento (o foco do teclado), so o contorno. Quem escolhe as cores de cada estado e o `Tema` (os quatro moldes la embaixo).
/// ==================================================================================================================
/// </summary>
public partial class EstiloDeBotao : StyleBox
{
	/// <summary>A cor do alto da chapa (o degrade vai dela ate <see cref="CorDeBaixo"/>).</summary>
	public Color CorDeCima = new("cfe8fb");

	/// <summary>A cor do pe da chapa.</summary>
	public Color CorDeBaixo = new("5f9cd3");

	/// <summary>A borda grossa em volta.</summary>
	public Color CorDaBorda = new("3a1f4e");

	/// <summary>A largura da borda, em px.</summary>
	public float LarguraDaBorda = 2.5f;

	/// <summary>Quanto a chapa inclina, em fracao da ALTURA: o topo anda isso pra direita e o pe, pra esquerda.</summary>
	public float Inclinacao = 0.22f;

	/// <summary>A linha clara logo abaixo da borda de cima (o "vidro" dos botoes da foto). Transparente = sem brilho.</summary>
	public Color CorDoBrilho = new(1, 1, 1, 0.55f);

	/// <summary>As margens de dentro (onde o texto mora): a folga da inclinacao dos lados entra na da esquerda e na da direita.</summary>
	public EstiloDeBotao()
	{
		ContentMarginLeft = ContentMarginRight = 15;
		ContentMarginTop = ContentMarginBottom = 8;
	}

	/// <summary>Os quatro cantos do paralelogramo de um retangulo, com o desvio horizontal dado (o do topo pra direita, o do pe pra esquerda).</summary>
	private static Vector2[] Paralelogramo(Rect2 r, float desvio) =>
	[
		new(r.Position.X + desvio, r.Position.Y),
		new(r.End.X, r.Position.Y),
		new(r.End.X - desvio, r.End.Y),
		new(r.Position.X, r.End.Y),
	];

	public override void _Draw(Rid toCanvasItem, Rect2 rect)
	{
		if (rect.Size.X <= 0 || rect.Size.Y <= 0) return;
		// A INCLINACAO E PELA ALTURA, com teto num quarto da largura: um botao baixo e largo inclina pouco, e um quadradinho nao vira losango.
		float desvio = Mathf.Min(rect.Size.Y * Inclinacao, rect.Size.X * 0.25f);
		Vector2[] fora = Paralelogramo(rect, desvio);
		bool temChapa = CorDeCima.A > 0 || CorDeBaixo.A > 0;
		bool temBorda = LarguraDaBorda > 0 && CorDaBorda.A > 0;

		if (!temChapa)
		{
			// SO O CONTORNO (o foco do teclado, por cima do estado de verdade).
			if (temBorda)
				RenderingServer.CanvasItemAddPolyline(toCanvasItem, [.. fora, fora[0]], [CorDaBorda], LarguraDaBorda, true);
			return;
		}

		// A BORDA E O POLIGONO DE FORA INTEIRO, e a chapa cobre o miolo -- assim a quina fica cheia, sem a fresta que uma linha grossa deixa no
		// canto. Um fio de 1 px por cima alisa o serrilhado da diagonal (o poligono nao tem antisserrilhado).
		Rect2 dentro = rect;
		if (temBorda)
		{
			RenderingServer.CanvasItemAddPolygon(toCanvasItem, fora, [CorDaBorda, CorDaBorda, CorDaBorda, CorDaBorda]);
			RenderingServer.CanvasItemAddPolyline(toCanvasItem, [.. fora, fora[0]], [CorDaBorda], 1f, true);
			dentro = rect.Grow(-LarguraDaBorda);
		}
		if (dentro.Size.X <= 0 || dentro.Size.Y <= 0) return;

		// O MIOLO COM A MESMA INCLINACAO (o desvio na proporcao da altura de dentro): as bordas dos lados ficam paralelas.
		Vector2[] miolo = Paralelogramo(dentro, desvio * dentro.Size.Y / rect.Size.Y);
		RenderingServer.CanvasItemAddPolygon(toCanvasItem, miolo, [CorDeCima, CorDeCima, CorDeBaixo, CorDeBaixo]);

		// O BRILHO: um fio claro um pouco abaixo da borda de cima, de lado a lado do miolo naquela altura.
		if (CorDoBrilho.A > 0 && dentro.Size.Y > 8)
		{
			float y = dentro.Position.Y + 1.5f;
			float t = (y - dentro.Position.Y) / dentro.Size.Y;
			float d = desvio * dentro.Size.Y / rect.Size.Y;
			var brilho = new Vector2[] { new(dentro.Position.X + d * (1 - t) + 3, y), new(dentro.End.X - d * t - 3, y) };
			RenderingServer.CanvasItemAddPolyline(toCanvasItem, brilho, new[] { CorDoBrilho }, 1.5f, true);
		}
	}

	// =====================================================================
	// OS QUATRO MOLDES -- os estados do botao do tema
	// =====================================================================
	/// <summary>A chapa AZUL do botao em repouso (a dos botoes comuns da foto).</summary>
	public static EstiloDeBotao Azul() => new() { CorDeCima = new("d2eafc"), CorDeBaixo = new("5e9bd2"), CorDaBorda = new("3a1f4e") };

	/// <summary>A chapa AMARELO-LARANJA do botao em destaque (o mouse em cima, o marcado): a do "Host a Room" e do "Open Room" da foto.</summary>
	public static EstiloDeBotao Amarelo() => new() { CorDeCima = new("fff1a8"), CorDeBaixo = new("f2a238"), CorDaBorda = new("fff3c4") };

	/// <summary>O amarelo AFUNDADO do botao apertado: o mesmo, um tom abaixo -- o clique se ve.</summary>
	public static EstiloDeBotao AmareloFundo() => new() { CorDeCima = new("f9d36a"), CorDeBaixo = new("d9831f"), CorDaBorda = new("fff3c4") };

	/// <summary>O amarelo do MARCADO COM O MOUSE EM CIMA: entre o aceso e o afundado -- os quatro estados pintam quatro chapas diferentes.</summary>
	public static EstiloDeBotao AmareloMarcado() => new() { CorDeCima = new("ffe48a"), CorDeBaixo = new("e8922c"), CorDaBorda = new("fff3c4") };

	/// <summary>A chapa CINZA-AZULADA do botao desligado: a mesma forma, sem cor -- "existe, mas nao e com voce agora".</summary>
	public static EstiloDeBotao Apagado() =>
		new() { CorDeCima = new("a7b1c0"), CorDeBaixo = new("66717f"), CorDaBorda = new("2c2838"), CorDoBrilho = new(1, 1, 1, 0.25f) };

	/// <summary>So o contorno creme: o foco do teclado, por cima do estado de verdade.</summary>
	public static EstiloDeBotao SoOContorno() =>
		new() { CorDeCima = new(0, 0, 0, 0), CorDeBaixo = new(0, 0, 0, 0), CorDaBorda = new("fff3c4"), CorDoBrilho = new(0, 0, 0, 0) };
}
