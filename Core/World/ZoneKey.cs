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

	/// <summary>
	/// UMA INSTANCIA de um interior: o NOME diz a PLANTA (que mapa e) e o <paramref name="id"/> diz QUAL instancia --
	/// a nave #7, a mente do jogador #12, a Sala do Tempo do mundo de seed X.
	///
	/// ============================ REGRA DA CASA (dono, 2026-10-11) ============================
	/// **ESTADO QUE MUDA DURANTE O JOGO NUNCA E DIVIDIDO ENTRE DUAS INSTANCIAS.** O nome e de TODAS; o que muda
	/// (o mapa que barra, o que cega, a celula caida, a porta aberta) se guarda pela chave INTEIRA -- o
	/// <see cref="Hash"/>, ou a `ChaveDeEstado` do servidor --, nunca por <see cref="Name"/>. E o mapa de quem
	/// escreve e uma COPIA da planta (`ZoneCollision.Instanciar`), nunca a planta.
	///
	/// INTERIOR NOVO: um tipo em `GameServer.TiposDeInterior` (`Server/GameServer.Interiores.cs`, o funil unico) e
	/// uma linha na vigia `--interioresteste` (`Server/GameServer.InterioresTeste.cs`), que roda duas instancias,
	/// muda uma e confere a outra. Fabrica de interior fora da lista da vigia e VERMELHO.
	/// ==========================================================================================
	/// </summary>
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
			// `Name ?? ""`: o `default(ZoneKey)` nao passa pelo construtor e tem o nome NULO -- e e ele que o cliente
			// tem em `Zone` ate o `Welcome` chegar. Um pacote que compara hashes e chega antes disso (a limpeza de
			// cenario do admin vai pra todo peer; hoje so uma bancada de 1o login a dispara nessa janela, a
			// `--lenhateste`) estourava dentro do tratador. Agora a zona-nenhuma tem um hash, que nao e o de zona
			// alguma (nenhuma tem nome vazio), e a comparacao responde "nao e esta".
			foreach (char c in Name ?? "")
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
