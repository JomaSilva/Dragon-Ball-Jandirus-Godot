using Godot;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-18) O "Z" DE QUEM DORME O SLEEP DO MAJIN (`Ocupacao.Dormindo`). Quem dorme dorme EM PE (dono, 2026-10-19 -- a folha nao tem pose
/// de sono, e o sono nao mexe na pose), entao sao as letras subindo e sumindo sobre a cabeca que dizem que ele dorme. Quem o liga e desliga e
/// o <see cref="World.MarcarDormindo"/>, pelo snapshot -- pro dono do corpo e pra quem esta em volta, inclusive quem chega no meio do sono.
/// Filho do corpo: morre com ele.
/// </summary>
public partial class SonoDesenhado : Node2D
{
	/// <summary>O nome do node. Uma casa so: quem liga e quem desliga procuram por ele.</summary>
	public const string NomeDoNode = "SonoDesenhado";

	/// <summary>De quanto em quanto tempo nasce um "z", e quanto tempo ele vive subindo.</summary>
	private const double Intervalo = 0.55, Vida = 1.7;

	private double _proximo;
	private int _nascidos;

	public override void _Ready() => ZIndex = 5;

	public override void _Process(double delta)
	{
		_proximo -= delta;
		if (_proximo > 0) return;
		_proximo = Intervalo;
		_nascidos++;

		// UM PEQUENO, UM MEDIO, UM GRANDE -- o "z z Z" de desenho animado, cada um um pouco mais pro lado.
		var z = new Label
		{
			Text = _nascidos % 3 == 0 ? "Z" : "z",
			Position = new Vector2(4 + _nascidos % 2 * 3, -22),
		};
		z.AddThemeFontSizeOverride("font_size", 9 + _nascidos % 3 * 2);
		z.AddThemeColorOverride("font_color", new Color("e6eeff"));
		z.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.1f, 0.2f));
		z.AddThemeConstantOverride("outline_size", 3);
		AddChild(z);

		Tween t = z.CreateTween().SetParallel();
		t.TweenProperty(z, "position", z.Position + new Vector2(6, -16), Vida);
		t.TweenProperty(z, "modulate:a", 0f, Vida).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
		t.Chain().TweenCallback(Callable.From(z.QueueFree));
	}

	/// <summary>Quantos "z" estao no ar agora, pra bancada.</summary>
	public int LetrasDeTeste => GetChildren().OfType<Label>().Count(l => !l.IsQueuedForDeletion());
}
