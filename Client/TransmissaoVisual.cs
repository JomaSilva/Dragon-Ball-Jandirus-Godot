using Godot;

namespace Jandirus.Client;

/// <summary>
/// O piscar do Instant Transmission, visto por quem esta na zona. E o mesmo efeito de desviar um golpe
/// (<see cref="EsquivaZanzoken"/>): o corpo vira as listras. O som e o <c>instanttransmission.wav</c>, no lugar
/// do assobio do desvio, e sai posicionado -- quem esta longe na zona ve, e nao ouve.
/// </summary>
public partial class World
{
	/// <summary>Quanto o som alcanca, em pixel de mundo. 480 e o mesmo alcance dos golpes (<c>EfeitoNoLugar</c>).</summary>
	public const float AlcanceDoSomDaTransmissao = 480f;

	public void PiscarDaTransmissao(int id, float x, float y)
	{
		EsquivaZanzoken.Trocar(Corpo(id));
		var marca = new Node2D { Position = new Vector2(x, y), Name = "SomDaTransmissao" };
		AddChild(marca);
		AudioDirector.EfeitoNoLugar(marca, Trilha.TransmissaoInstantanea, 1f, AlcanceDoSomDaTransmissao);
		if (marca.GetChildCount() > 0 && marca.GetChild(0) is AudioStreamPlayer2D som)
			som.Finished += () => { if (GodotObject.IsInstanceValid(marca)) marca.QueueFree(); };
		else
			marca.QueueFree();
	}
}
