using Godot;

namespace Jandirus.Client;

/// <summary>
/// UMA CONSTRUCAO NO CHAO -- a bancada de pesquisa, o mainframe de androides.
///
/// DESENHADA POR CODIGO, e nao por sprite, e isso e uma escolha e nao preguica: os icones das 99
/// construcoes do original estao espalhados por dezenas de .dmi que o pipeline ainda nao converte,
/// e uma construcao invisivel e pior que uma construcao feia -- o jogador ergue um mainframe de
/// meio milhao de zeni e nao ve nada no chao. Uma forma geometrica com a cor do tipo diz onde a
/// coisa esta, se ela esta aparafusada e de quem e; quando os icones chegarem, troca-se o _Draw
/// e nada mais.
///
/// ENTRA NO Y-SORT como qualquer corpo (ancorada nos PES, ver <see cref="Ancora"/>): sem isso o
/// personagem passaria por tras da bancada e continuaria desenhado por cima dela.
/// </summary>
public partial class ObraDesenhada : Node2D
{
	public string Tipo = "";
	public string Dono = "";
	public bool Aparafusada;
	public int Lab;

	/// <summary>Metade da largura, em pixels. Uma construcao ocupa dois tiles de frente.</summary>
	private const float Largura = 28f;
	private const float Altura = 34f;

	/// <summary>
	/// A BASE DA CAIXA E A ANCORA. O desenho sobe a partir de (0,0), que fica nos pes -- e o mesmo
	/// contrato dos personagens, e e o que faz o Y-sort comparar coisas comparaveis.
	/// </summary>
	private const float Ancora = 0f;

	public override void _Ready()
	{
		YSortEnabled = true;
		ZIndex = 0;
	}

	/// <summary>Cor por familia de construcao -- o que o icone diria, dito por cor.</summary>
	private Color Cor => Tipo switch
	{
		"Research_Station" => new Color(0.35f, 0.62f, 0.85f),
		"Fabricator" => new Color(0.55f, 0.75f, 0.45f),
		"Android_Creation_Mainframe" => Lab switch
		{
			1 => new Color(0.85f, 0.72f, 0.30f),   // Android Lab
			2 => new Color(0.55f, 0.80f, 0.45f),   // Bio-Android Lab
			_ => new Color(0.70f, 0.70f, 0.76f),
		},
		_ => new Color(0.62f, 0.60f, 0.68f),
	};

	public override void _Draw()
	{
		Color c = Cor;
		var corpo = new Rect2(-Largura, Ancora - Altura, Largura * 2, Altura);

		// SOMBRA NO CHAO. Sem ela a construcao parece flutuar -- e num jogo com Y-sort, "parece
		// flutuar" e indistinguivel de "esta na camada errada".
		DrawCircle(new Vector2(0, Ancora - 2), Largura * 0.9f, new Color(0, 0, 0, 0.28f));

		DrawRect(corpo, c);
		DrawRect(corpo, c.Darkened(0.5f), filled: false, width: 2f);

		// o painel: a "cara" da maquina
		DrawRect(new Rect2(-Largura * 0.6f, Ancora - Altura * 0.85f, Largura * 1.2f, Altura * 0.4f),
				 c.Lightened(0.35f));

		// SOLTA PISCA. Uma construcao nao aparafusada nao funciona, e descobrir isso so ao clicar
		// e a diferenca entre um jogo que ensina e um que esconde.
		if (!Aparafusada)
		{
			float p = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 250f);
			DrawRect(corpo.Grow(3), new Color(1f, 0.85f, 0.35f, 0.25f + 0.35f * p), filled: false, width: 2f);
		}
	}

	public override void _Process(double delta)
	{
		if (!Aparafusada) QueueRedraw();   // so a que pisca precisa de quadro novo
	}
}
