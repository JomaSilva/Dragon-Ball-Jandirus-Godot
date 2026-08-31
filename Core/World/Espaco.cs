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
/// de setores -- há uma FUNÇÃO que, dada uma coordenada, diz o que existe nela.
///
/// QUEM DECIDE O QUE EXISTE É O <see cref="Sistemas"/>, e não mais este arquivo: os planetas nascem
/// em ÓRBITA DE UMA ESTRELA, uma estrela por célula de 32x32 chunks. A chunk continua sendo o corte
/// de INTERESSE (quem enxerga quem, o que se carrega); ela deixou de ser o corte de CONTEÚDO.
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
	// OS PLANETAS PROCEDURAIS -- ELES ORBITAM ESTRELAS
	// =====================================================================
	/// <summary>
	/// TODOS OS PLANETAS VISIVEIS A PARTIR DE UMA CHUNK (ela e as vizinhas).
	///
	/// ============================ ELES NAO SAO MAIS SORTEADOS POR CHUNK ============================
	/// Havia aqui um `PlanetaDaChunk` -- um hash por chunk, um planeta a cada 40 delas, sem estrela e
	/// sem sistema. Ele foi DELETADO e nao substituido por um irmao: quem decide o que existe no
	/// espaco agora e <see cref="Sistemas.Do"/>, uma estrela em 62,4% das celulas de 32x32 chunks com
	/// 1 a 10 planetas em orbita (as outras 37,6% nao tem nada -- <see cref="Sistemas.VaziosPor256"/>).
	/// O universo passou a ser 7,5x mais vazio em planetas e AGRUPADO -- o que o comentario desta
	/// classe sempre prometeu ("e a distancia entre os mundos que da peso a uma viagem de sete dias")
	/// e que com um mundo a cada 42 s de voo nao acontecia.
	///
	/// A ASSINATURA DESTE METODO NAO MUDOU de proposito: o servidor (`MandarVizinhanca`), o pouso
	/// (<see cref="PlanetaSob"/>) e a carta perguntam a mesma coisa que sempre perguntaram.
	/// ==========================================================================================
	///
	/// POR QUE VARRER CELULAS E NAO CHUNKS: um sistema alcanca ate
	/// <see cref="Sistemas.RaioSistemaTeto"/> px, entao o numero de CELULAS a olhar sai da conta
	/// abaixo -- e da 1 (o 3x3) pra qualquer `raio` ate 22 chunks. Sao 9 hashes, contra os 9 hashes
	/// mais 63 voltas nos pre-feitos que a versao por chunk fazia.
	/// </summary>
	public static List<PlanetaNoEspaco> PorPerto(ulong seedDoMundo, ChunkId centro, int raio = RaioAtivo)
	{
		var l = new List<PlanetaNoEspaco>();

		// O meio da chunk central. Ancorar na quina faria a celula "certa" mudar de lado conforme o
		// sinal da coordenada, que e a classe de bug que so aparece do outro lado do zero.
		var meio = new Vec2(centro.X * (float)ChunkPx + ChunkPx / 2f,
							centro.Y * (float)ChunkPx + ChunkPx / 2f);

		// ATE ONDE UM CORPO INTERESSANTE PODE ESTAR: o canto mais longe da vizinhanca pedida, mais o
		// raio do maior sistema. Dividido pelo lado da celula, da quantas celulas ha pra olhar.
		double alcance = (raio + 1) * (double)ChunkPx * 1.5 + Sistemas.RaioSistemaTeto;
		int celulas = (int)Math.Ceiling(alcance / Sistemas.CelulaPx);

		SistemaId c0 = SistemaId.De(meio);
		for (int dy = -celulas; dy <= celulas; dy++)
			for (int dx = -celulas; dx <= celulas; dx++)
			{
				if (Sistemas.Do(seedDoMundo, c0.Sx + dx, c0.Sy + dy) is not { } s) continue;

				// PREFILTRO: quase sempre nenhum sistema alcanca, e ai nao se calcula planeta nenhum.
				double ex = s.Estrela.Pos.X - meio.X, ey = s.Estrela.Pos.Y - meio.Y;
				double limite = s.RaioDoSistema + (raio + 1) * (double)ChunkPx * 1.5;
				if (ex * ex + ey * ey > limite * limite) continue;

				for (int k = 0; k < s.Orbitas; k++)
				{
					PlanetaNoEspaco p = s.Planeta(k);
					if (p.Chunk.Longe(centro) <= raio) l.Add(p);
				}
			}

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
	/// ESTA ZONA É A SUPERFÍCIE DE UM PLANETA?
	///
	/// Os dois casos que valem:
	///   * mundo SORTEADO -- é literalmente onde o DM punha o `Stars_Exit` (`NewTurfs.dm:48`);
	///   * mundo PRÉ-FEITO que aparece na carta estelar, ou seja, um lugar onde se POUSA.
	/// O critério do segundo não é uma lista escrita: é a mesma <see cref="PreFeitos"/> que a carta
	/// estelar e o pouso já consultam. Zona que não está lá (Sala do Tempo, Inferno, Paraíso,
	/// Lookout, o interior de uma nave, a Dimensão Mental) não é planeta.
	///
	/// ============================ ELA MORAVA NO SERVIDOR, E AGORA SÃO DOIS DONOS ============================
	/// Nasceu como `GameServer.ZonaEhPlaneta` (`GameServer.Volta.cs`), pra decidir quem dá a volta na
	/// borda do mapa. O CLIENTE passou a precisar da mesma resposta -- a música e o tremor de uma
	/// transformação alcançam o PLANETA inteiro, e "planeta" não pode significar o espaço (que é UMA
	/// zona só pro universo inteiro, ver <see cref="NomeDoEspaco"/>) nem um interior.
	///
	/// Copiar as seis linhas pro cliente seria ter duas definições de planeta que envelheceriam
	/// separadas -- e a primeira a errar seria a do cliente, calada, num efeito. Aqui é uma só.
	/// ==================================================================================================
	/// </summary>
	public static bool EhPlaneta(ZoneKey z)
	{
		if (z.Kind == ZoneKey.KindProcedural) return !EhEspaco(z);
		if (z.Kind != ZoneKey.KindPremade) return false;

		foreach (PlanetaNoEspaco p in PreFeitos())
			if (string.Equals(p.Nome, z.Name, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	/// <summary>
	/// ============================ A ZONA DE UM CORPO DA CARTA ============================
	/// Do disco que se ve do espaco pra a SUPERFICIE em que se anda. Duas linhas, e as duas erram
	/// caladas se a gente escrever a de baixo pra um pre-feito:
	///   * pre-feito -- <see cref="ZoneKey.Premade"/>, seed ZERO. O `PlanetaNoEspaco.Seed` dele e
	///     `Hash64(nome)`, que serve pra sortear a arte e **nao e a seed da zona**;
	///   * gerado -- <see cref="ZoneKey.Procedural"/> com a seed da orbita, que e a identidade dele.
	///
	/// Isto e um metodo porque a conversao ja estava copiada em uma duzia de lugares (o admin
	/// `Restore Planet`, os desejos, o pouso, a carta, quatro bancadas), e a chave de um planeta e
	/// justamente a coisa deste sistema que ja custou caro por ser copiada -- ver
	/// <see cref="PlanetasMortos"/>. Quem escrever um consumidor novo entra por aqui.
	/// ================================================================================
	/// </summary>
	public static ZoneKey ZonaDe(PlanetaNoEspaco p) =>
		p.Premade ? ZoneKey.Premade(p.Nome) : ZoneKey.Procedural(p.Nome, p.Seed);

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
