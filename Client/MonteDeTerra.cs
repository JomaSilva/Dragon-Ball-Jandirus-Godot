using Godot;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-17) O MONTINHO DE TERRA DA LARVA ENTERRADA -- so na tela de quem esta enterrado (`LocalPlayer.MostrarEnterrado`): os outros nao
/// veem nada (o `Oculto` do snapshot, que e o "se esconder" do adendo do dono), e o dono precisa saber onde esta e que esta la embaixo.
/// Desenhado em `_Draw` -- tres elipses de terra, sem arte importada --, embaixo do corpo.
/// </summary>
public partial class MonteDeTerra : Node2D
{
	public const string NomeDoNode = "MonteDeTerra";

	private static readonly Color Terra = new(0.36f, 0.25f, 0.15f, 0.92f);
	private static readonly Color TerraClara = new(0.55f, 0.41f, 0.26f, 0.92f);

	public override void _Ready() => ZIndex = -1;

	public override void _Draw()
	{
		Elipse(new Vector2(0, 11), new Vector2(15, 6), Terra);
		Elipse(new Vector2(-5, 9), new Vector2(6, 3), TerraClara);
		Elipse(new Vector2(6, 10), new Vector2(4, 2), TerraClara);
	}

	private void Elipse(Vector2 centro, Vector2 raio, Color cor)
	{
		var pontos = new Vector2[20];
		for (int i = 0; i < pontos.Length; i++)
		{
			float a = Mathf.Tau * i / pontos.Length;
			pontos[i] = centro + new Vector2(Mathf.Cos(a) * raio.X, Mathf.Sin(a) * raio.Y);
		}
		DrawColoredPolygon(pontos, cor);
	}
}
