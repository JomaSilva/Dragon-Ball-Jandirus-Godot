using Jandirus.Core.World;

namespace Jandirus.Core.Combat;

/// <summary>
/// QUEM ESTA AO ALCANCE DO SOCO. Geometria pura, no Core, porque as duas pontas precisam da
/// MESMA resposta: o cliente pra desenhar quem vai levar (e nao prometer um golpe que o
/// servidor vai recusar) e o servidor pra decidir de verdade.
///
/// O BYOND resolvia isso em TILE: pegava os mobs do turf da frente. Aqui o movimento e livre
/// em pixel, entao o alcance virou um CONE -- distancia mais angulo. Sem o angulo, dar um
/// soco viraria um pulso circular que acerta quem esta atras de voce.
/// </summary>
public static class MeleeArea
{
	/// <summary>Vetor unitario da direcao pra onde o personagem esta virado.</summary>
	public static Vec2 Frente(Facing f) => f switch
	{
		Facing.North => new Vec2(0, -1),
		Facing.South => new Vec2(0, 1),
		Facing.East => new Vec2(1, 0),
		_ => new Vec2(-1, 0),
	};

	/// <summary>Angulo em GRAUS entre dois vetores. Devolve 0..180.</summary>
	public static double Angulo(Vec2 a, Vec2 b)
	{
		Vec2 ua = a.Normalized(), ub = b.Normalized();
		double dot = Math.Clamp(ua.X * ub.X + ua.Y * ub.Y, -1, 1);
		return Math.Acos(dot) * 180 / Math.PI;
	}

	/// <summary>
	/// O alvo esta no cone de golpe de quem ataca?
	///
	/// Encostado (dentro de meio tile) o angulo e IGNORADO: quem esta colado em voce leva o
	/// soco venha de onde vier, e sem isso um agarrao no corpo a corpo vira uma dancinha de
	/// mira que ninguem acerta.
	/// </summary>
	public static bool NoAlcance(Vec2 origem, Facing frente, Vec2 alvo)
	{
		Vec2 d = alvo - origem;
		float dist2 = d.LengthSquared;
		if (dist2 > CombatKnobs.Alcance * CombatKnobs.Alcance) return false;
		if (dist2 < 16 * 16) return true;
		return Angulo(Frente(frente), d) <= CombatKnobs.MeioAnguloCone;
	}

	/// <summary>
	/// De onde o golpe chega, do ponto de vista de quem apanha: 0 = de frente, 180 = nas
	/// costas. E o numero que o <see cref="MeleeResolver"/> usa pro bonus de flanco.
	/// </summary>
	public static double AnguloDeChegada(Vec2 posDefensor, Facing frenteDefensor, Vec2 posAtacante)
	{
		Vec2 d = posAtacante - posDefensor;
		if (d.LengthSquared < 1e-6f) return 0;
		return Angulo(Frente(frenteDefensor), d);
	}
}
