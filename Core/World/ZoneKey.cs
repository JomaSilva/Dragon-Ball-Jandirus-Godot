namespace Jandirus.Core.World;

/// <summary>
/// O SUBSTITUTO DO "z" DO BYOND.
///
/// No BYOND o mundo era uma pilha de andares numerados (z1 = Terra, z2 = Namek, ...) e o
/// numero servia pra tudo: onde o jogador esta, quem enxerga quem, onde spawnar. O numero
/// nao escalava -- z procedural precisava de POOL de andares reciclados, e o codigo vivia
/// consultando tabelas pra saber "que lugar e o z 87?".
///
/// Aqui a identidade do lugar E o dado: um tipo + um nome + (pra procedural) uma seed.
/// O <see cref="Hash"/> disso e o que o servidor usa como chave de INTERESSE: so quem
/// compartilha o mesmo hash recebe pacote um do outro. Quem esta na Terra nao gasta banda
/// com quem esta em Namek.
///
/// A seed entra no hash de proposito: dois setores procedurais diferentes sao lugares
/// diferentes mesmo tendo o mesmo nome.
/// </summary>
public readonly struct ZoneKey : IEquatable<ZoneKey>
{
	/// <summary>Mapa feito a mao (convertido de .dmm): Terra, Vegeta, Namek, Outro Mundo...</summary>
	public const byte KindPremade = 0;
	/// <summary>Planeta gerado por seed no cliente.</summary>
	public const byte KindProcedural = 1;
	/// <summary>Interior volatil (nave, bolso do Majin, Sala do Tempo).</summary>
	public const byte KindInterior = 2;

	public readonly byte Kind;
	public readonly string Name;
	public readonly ulong Seed;

	public ZoneKey(byte kind, string name, ulong seed = 0)
	{
		Kind = kind;
		Name = name ?? "";
		Seed = seed;
	}

	public static ZoneKey Premade(string name) => new(KindPremade, name);
	public static ZoneKey Procedural(string name, ulong seed) => new(KindProcedural, name, seed);
	public static ZoneKey Interior(string name, ulong id) => new(KindInterior, name, id);

	/// <summary>
	/// FNV-1a de 64 bits sobre tipo+nome+seed. Escolhido por ser estavel entre execucoes e
	/// entre maquinas: o hash viaja no pacote, entao NAO pode depender de GetHashCode(), que
	/// o .NET randomiza por processo.
	/// </summary>
	public ulong Hash
	{
		get
		{
			const ulong offset = 14695981039346656037UL;
			const ulong prime = 1099511628211UL;
			ulong h = offset;
			h = (h ^ Kind) * prime;
			foreach (char c in Name)
			{
				h = (h ^ (byte)(c & 0xFF)) * prime;
				h = (h ^ (byte)(c >> 8)) * prime;
			}
			for (int i = 0; i < 8; i++)
				h = (h ^ (byte)((Seed >> (i * 8)) & 0xFF)) * prime;
			return h;
		}
	}

	public bool Equals(ZoneKey other) =>
		Kind == other.Kind && Seed == other.Seed && string.Equals(Name, other.Name, StringComparison.Ordinal);

	public override bool Equals(object? o) => o is ZoneKey z && Equals(z);
	public override int GetHashCode() => Hash.GetHashCode();
	public override string ToString() =>
		Kind == KindPremade ? Name : $"{Name}#{Seed}";
}
