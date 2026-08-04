namespace Jandirus.Core.World;

/// <summary>Um planeta no mapa do universo: onde fica e o que é.</summary>
public readonly struct PlanetaNoEspaco
{
	/// <summary>Nome da zona. Pré-feito ("Earth") ou gerado ("Verdejante-1042").</summary>
	public string Nome { get; init; }

	/// <summary>Posição em pixels de MUNDO. O universo é um plano contínuo, não uma grade de setores.</summary>
	public Vec2 Pos { get; init; }

	/// <summary>Raio de desenho/colisão, em pixels.</summary>
	public float Raio { get; init; }

	/// <summary>Seed da superfície. É o que o cliente usa pra gerar o chão sem baixar mapa.</summary>
	public ulong Seed { get; init; }

	/// <summary>Pré-feito (tem .dmm convertido) ou procedural.</summary>
	public bool Premade { get; init; }

	public ChunkId Chunk => ChunkId.De(Pos);
}

/// <summary>Coordenada de chunk. Dois inteiros -- o universo não tem fim nem centro.</summary>
public readonly record struct ChunkId(int X, int Y)
{
	public static ChunkId De(Vec2 p) => new(
		(int)MathF.Floor(p.X / Espaco.ChunkPx),
		(int)MathF.Floor(p.Y / Espaco.ChunkPx));

	/// <summary>Distância em chunks (Chebyshev): é o que define "chunk vizinha".</summary>
	public int Longe(ChunkId o) => Math.Max(Math.Abs(X - o.X), Math.Abs(Y - o.Y));

	public Vec2 Canto => new(X * Espaco.ChunkPx, Y * Espaco.ChunkPx);
	public override string ToString() => $"({X},{Y})";
}

/// <summary>
/// O UNIVERSO, EM CHUNKS.
///
/// No BYOND cada setor era um `z` porque o motor obrigava, e o número não escalava. Aqui o espaço
/// é um PLANO CONTÍNUO cortado em chunks quadradas, no modelo do Minecraft: cliente e servidor só
/// processam a chunk em que se está e as vizinhas. Não há limite de tamanho porque não há lista
/// de setores -- há uma FUNÇÃO que, dada uma chunk, diz o que existe nela.
///
/// O QUE O SERVIDOR GUARDA é só a posição dos planetas. O fundo de estrelas o cliente gera
/// sozinho a partir do id da chunk (mesma seed, mesmas estrelas, em qualquer máquina); a
/// superfície de um planeta procedural o cliente gera com a seed que o servidor manda. O que o
/// servidor sincroniza de verdade é só o DELTA -- base construída, cenário destruído.
///
/// AS DISTÂNCIAS SAEM DO ANIME. Terra→Namek levou 7 dias, então leva 7 dias IN-GAME aqui. Os dois
/// números que fecham a conta já existem no jogo e são usados como estão:
///   * <see cref="SegundosPorDiaInGame"/> -- o mesmo ciclo do dia/noite;
///   * <see cref="MoveRules.BaseSpeedPx"/> -- nave viaja à velocidade de voo do jogador.
/// Derivar em vez de cravar é o que impede os dois de divergirem se um dia o dia mudar de duração.
/// </summary>
public static class Espaco
{
	/// <summary>
	/// Lado da chunk, em pixels. 64 tiles.
	///
	/// Grande o bastante pra uma tela caber com folga (uma tela em zoom 2 tem ~30x17 tiles),
	/// pequena o bastante pra carregar/descarregar sair barato.
	/// </summary>
	public const int ChunkTiles = 64;
	public const int ChunkPx = ChunkTiles * ZoneCollision.TileSize;

	/// <summary>Quantas chunks em volta ficam ativas. 1 = o quadrado 3x3 em volta de você.</summary>
	public const int RaioAtivo = 1;

	/// <summary>
	/// Duração de um dia in-game, em segundos reais. É o MESMO valor do ciclo dia/noite -- e
	/// agora é literalmente o mesmo, e não uma cópia: a constante mora no <see cref="Ceu"/>, e a
	/// distância Terra→Namek se ajusta sozinha se o dia mudar de duração.
	/// </summary>
	public const double SegundosPorDiaInGame = Ceu.SegundosPorDia;

	/// <summary>Terra→Namek no anime. É daqui que sai a escala do universo inteiro.</summary>
	public const double DiasTerraNamek = 7;

	/// <summary>
	/// A DISTÂNCIA TERRA→NAMEK em pixels, derivada: 7 dias in-game à velocidade de voo.
	///
	/// Com o dia de 24 min e 160 px/s dá ~1,61 milhão de px (~50.400 tiles, ~788 chunks) e 168
	/// minutos reais de viagem. O usuário estimou 140 min supondo um dia de 20 min; quem manda é
	/// a constante do dia, não o número cravado.
	/// </summary>
	public static double DistanciaTerraNamek =>
		DiasTerraNamek * SegundosPorDiaInGame * MoveRules.BaseSpeedPx;

	/// <summary>Quanto tempo real leva pra cruzar uma distância, na velocidade de voo base.</summary>
	public static double SegundosDeViagem(double distanciaPx) => distanciaPx / MoveRules.BaseSpeedPx;

	public static double DiasInGame(double distanciaPx) =>
		SegundosDeViagem(distanciaPx) / SegundosPorDiaInGame;

	// =====================================================================
	// OS PLANETAS PRÉ-FEITOS
	// =====================================================================
	/// <summary>
	/// Onde ficam os planetas que têm mapa próprio.
	///
	/// A Terra é a ORIGEM (0,0) -- não por ser o centro do universo, mas porque todo sistema de
	/// coordenadas precisa de um zero e é dela que os jogadores saem. Namek fica à distância do
	/// anime; os outros foram espalhados em volta na mesma escala, mais perto ou mais longe
	/// conforme o quanto pertencem à história da Terra.
	/// </summary>
	public static IEnumerable<PlanetaNoEspaco> PreFeitos()
	{
		double d = DistanciaTerraNamek;
		yield return Fixo("Earth", 0, 0, 220);
		yield return Fixo("Namek", d * 0.71, -d * 0.70, 200);          // 7 dias da Terra
		yield return Fixo("Vegeta", -d * 0.55, -d * 0.42, 190);        // ~4,8 dias
		yield return Fixo("Icer", d * 0.20, d * 1.35, 210);            // ~9,6 dias: longe e hostil
		yield return Fixo("Arconia", -d * 1.02, d * 0.48, 180);        // ~7,9 dias
		yield return Fixo("Arlia", d * 1.30, -d * 0.35, 150);          // ~9,4 dias
		yield return Fixo("Makyo_Star", -d * 0.30, d * 0.28, 140);     // ~2,9 dias
	}

	private static PlanetaNoEspaco Fixo(string nome, double x, double y, float raio) => new()
	{
		Nome = nome,
		Pos = new Vec2((float)x, (float)y),
		Raio = raio,
		Premade = true,
		Seed = Hash64(nome),
	};

	// =====================================================================
	// OS PLANETAS PROCEDURAIS
	// =====================================================================
	/// <summary>
	/// Um planeta procedural a cada N chunks, em média. Espalhado: o espaço tem que parecer
	/// VAZIO -- é a distância entre os mundos que dá peso a uma viagem de sete dias.
	/// </summary>
	public const int ChunksPorPlaneta = 40;

	/// <summary>
	/// O QUE EXISTE NESTA CHUNK. Função pura da seed do mundo + o id da chunk: nada é sorteado
	/// em runtime nem guardado numa tabela, então servidor e cliente chegam à MESMA resposta sem
	/// trocar um byte, e o universo é do tamanho do espaço de inteiros.
	///
	/// A chunk (0,0) e as chunks dos pré-feitos NUNCA geram planeta procedural: dois mundos no
	/// mesmo lugar seria o único jeito de o universo infinito ficar apertado.
	/// </summary>
	public static PlanetaNoEspaco? PlanetaDaChunk(ulong seedDoMundo, ChunkId c)
	{
		ulong h = Misturar(seedDoMundo, (ulong)(uint)c.X, (ulong)(uint)c.Y);
		if (h % ChunksPorPlaneta != 0) return null;

		// dentro da chunk, uma posição estável (margem pra o planeta não nascer na divisa)
		const int margem = 400;
		float px = c.X * (float)ChunkPx + margem + (h >> 8) % (uint)(ChunkPx - 2 * margem);
		float py = c.Y * (float)ChunkPx + margem + (h >> 24) % (uint)(ChunkPx - 2 * margem);

		var pos = new Vec2(px, py);
		foreach (PlanetaNoEspaco p in PreFeitos())
			if (p.Chunk == c) return null;   // a chunk já é de um mundo com mapa próprio

		return new PlanetaNoEspaco
		{
			Nome = $"{NomeDeBioma(h)}-{Math.Abs(c.X) % 1000}{Math.Abs(c.Y) % 1000}",
			Pos = pos,
			Raio = 110 + (h >> 40) % 90,
			Seed = h,
			Premade = false,
		};
	}

	/// <summary>Os biomas do `ProceduralSpace.dm`, na mesma ordem.</summary>
	private static string NomeDeBioma(ulong h) => ((h >> 16) % 6) switch
	{
		0 => "Verdejante",
		1 => "Gelido",
		2 => "Deserto",
		3 => "Vulcanico",
		4 => "Alienigena",
		_ => "Sombrio",
	};

	/// <summary>Todos os planetas visíveis a partir de uma chunk (ela e as vizinhas).</summary>
	public static List<PlanetaNoEspaco> PorPerto(ulong seedDoMundo, ChunkId centro, int raio = RaioAtivo)
	{
		var l = new List<PlanetaNoEspaco>();

		foreach (PlanetaNoEspaco p in PreFeitos())
			if (p.Chunk.Longe(centro) <= raio) l.Add(p);

		for (int dy = -raio; dy <= raio; dy++)
			for (int dx = -raio; dx <= raio; dx++)
				if (PlanetaDaChunk(seedDoMundo, new ChunkId(centro.X + dx, centro.Y + dy)) is { } p)
					l.Add(p);

		return l;
	}

	// =====================================================================
	// ENTRAR E SAIR DO ESPACO
	// =====================================================================
	/// <summary>
	/// O nome da zona do espaco. UMA zona pro universo inteiro -- o corte de interesse la nao e
	/// a zona, e a CHUNK (ver <see cref="PertoDeMim"/>). Fatiar o espaco em zonas traria de
	/// volta exatamente o problema dos setores-por-z que se quis matar.
	/// </summary>
	public const string NomeDoEspaco = "Espaco";

	public static ZoneKey Zona(ulong seedDoMundo) => ZoneKey.Procedural(NomeDoEspaco, seedDoMundo);

	public static bool EhEspaco(ZoneKey z) =>
		string.Equals(z.Name, NomeDoEspaco, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Onde o corpo aparece ao DECOLAR de um planeta: logo acima da superficie dele.
	///
	/// Fora do raio de proposito -- nascer DENTRO do planeta faria o teste de pouso disparar no
	/// mesmo quadro e o jogador voltaria pro chao sem nunca ver o espaco.
	/// </summary>
	public static Vec2 PontoDeDecolagem(PlanetaNoEspaco p) =>
		p.Pos + new Vec2(0, -(p.Raio + 90));

	/// <summary>
	/// Em que planeta este ponto esta encostando (nulo = espaco aberto).
	///
	/// E o "encostar nele = entrar" que o dono pediu: nao ha porta nem menu, o corpo toca a
	/// superficie e desce.
	/// </summary>
	public static PlanetaNoEspaco? PlanetaSob(ulong seedDoMundo, Vec2 pos)
	{
		foreach (PlanetaNoEspaco p in PorPerto(seedDoMundo, ChunkId.De(pos)))
			if ((pos - p.Pos).LengthSquared <= p.Raio * p.Raio) return p;
		return null;
	}

	/// <summary>
	/// Dois corpos se enxergam no espaco? So se estiverem em chunks vizinhas.
	///
	/// E ISTO que substitui o "mesmo z": num universo continuo, estar na mesma ZONA nao diz
	/// nada -- todo mundo esta. Quem decide o trafego e a distancia em chunks.
	/// </summary>
	public static bool PertoDeMim(Vec2 a, Vec2 b, int raio = RaioAtivo) =>
		ChunkId.De(a).Longe(ChunkId.De(b)) <= raio;

	// =====================================================================
	// MISTURA
	// =====================================================================
	/// <summary>
	/// Hash de três inteiros. Precisa ser ESTÁVEL entre execuções e entre máquinas -- o cliente
	/// gera o mesmo universo que o servidor sem sincronizar nada, e `GetHashCode()` não serve
	/// (o .NET o randomiza por processo).
	/// </summary>
	public static ulong Misturar(ulong seed, ulong a, ulong b)
	{
		ulong h = seed ^ 0x9E3779B97F4A7C15UL;
		h = (h ^ a) * 0xBF58476D1CE4E5B9UL;
		h ^= h >> 27;
		h = (h ^ b) * 0x94D049BB133111EBUL;
		h ^= h >> 31;
		return h;
	}

	public static ulong Hash64(string s)
	{
		ulong h = 1469598103934665603UL;
		foreach (char ch in s) { h ^= ch; h *= 1099511628211UL; }
		return h;
	}
}
