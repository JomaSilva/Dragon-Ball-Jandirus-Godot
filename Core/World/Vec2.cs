using System.Globalization;

namespace Jandirus.Core.World;

/// <summary>
/// Vetor 2D do NUCLEO. Existe porque o Core nao pode referenciar o Godot (senao as regras
/// nao rodariam no servidor headless nem em teste). O cliente converte pra Vector2 na borda.
/// </summary>
public readonly struct Vec2(float x, float y) : IEquatable<Vec2>
{
	public readonly float X = x;
	public readonly float Y = y;

	public static readonly Vec2 Zero = new(0, 0);

	public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
	public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
	public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Y * s);

	public float LengthSquared => X * X + Y * Y;
	public float Length => MathF.Sqrt(LengthSquared);

	public Vec2 Normalized()
	{
		float len = Length;
		return len > 1e-6f ? new Vec2(X / len, Y / len) : Zero;
	}

	public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;

	public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
	public override bool Equals(object? o) => o is Vec2 v && Equals(v);
	public override int GetHashCode() => HashCode.Combine(X, Y);
	public override string ToString() =>
		string.Create(CultureInfo.InvariantCulture, $"({X:0.##}, {Y:0.##})");
}
