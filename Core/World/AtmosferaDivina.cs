namespace Jandirus.Core.World;

/// <summary>
/// OS TRES BOTOES DO SORTEIO DA ATMOSFERA DIVINA -- e o jeito de a bancada medir a GARANTIA.
///
/// O jogo so conhece <see cref="Hoje"/>. Os botoes ficam a mostra pelo motivo da `RegraDaGrade` do
/// <see cref="Sistemas"/>: a garantia ("todo universo tem pelo menos um mundo divino ao alcance") quase
/// nunca dispara com a raridade de hoje -- e guarda que nunca roda e guarda que ninguem sabe se funciona.
/// Com a MESMA funcao e uma raridade que nao sorteia mundo nenhum, a bancada ve a garantia trabalhar em
/// todo universo, em vez de procurar entre vinte mil sementes a unica que precisaria dela.
/// </summary>
/// <param name="UmEm">Um mundo sorteado em cada tantos tem atmosfera divina. Zero ou menos = o sorteio nao da nenhum.</param>
/// <param name="RaioGarantido">A regiao garantida: as celulas de sistema a ate tantas da celula da Terra.</param>
/// <param name="MinimoGarantido">Quantos mundos divinos a regiao garantida tem no minimo.</param>
public readonly record struct RegraDaAtmosfera(int UmEm, int RaioGarantido, int MinimoGarantido)
{
	/// <summary>O universo que esta no ar. E o padrao de todas as sobrecargas curtas.</summary>
	public static readonly RegraDaAtmosfera Hoje =
		new(AtmosferaDivina.UmEm, AtmosferaDivina.RaioGarantidoEmCelulas, AtmosferaDivina.MinimoGarantido);

	/// <summary>O mesmo universo com outra raridade -- e por onde a bancada desliga o sorteio pra ver a garantia.</summary>
	public RegraDaAtmosfera ComRaridade(int umEm) => this with { UmEm = umEm };
}

/// <summary>Um mundo de atmosfera divina na carta: o corpo, o endereco dele e por que ele e divino.</summary>
/// <param name="PorGarantia">Falso = saiu no sorteio; verdadeiro = ganhou pela garantia da regiao.</param>
public readonly record struct MundoDivino(PlanetaNoEspaco Corpo, int Sx, int Sy, int K, bool PorGarantia);

/// <summary>
/// A ATMOSFERA DIVINA -- quais mundos a tem. Funcao pura da seed, como o resto do universo.
///
/// ============================ O PEDIDO DO DONO (2026-10-10) ============================
/// *"o god ki alguns planetas raros vao possuir uma atmosfera divina ao qual quando o jogador tiver um bp
/// base maior q o requisito minimo dele de despertar o god ki, ao ficar um tempo nessa atmosfera o ki
/// divino e liberado"*.
///
/// E DESENHO NOVO, e o parente dele no DM e a FENDA divina (`Godki/Rifts.dm:94-110`, o `GodKi_Rift`):
/// um objeto raro que so nasce no Reino dos Deuses (`Areas.dm:362-372`, `rift_type` + `rift_rarity`) e
/// da energia divina a quem medita dentro dele. Aqui a fenda virou o AR de um mundo inteiro -- e o
/// Reino dos Deuses continua divino, por natureza (<see cref="DivinosPorNatureza"/>).
/// ======================================================================================
///
/// ============================ E ATRIBUTO DO MUNDO, NAO E TILE ============================
/// Nada aqui toca o gerador de terreno: o chao de um mundo divino e o mesmo que a seed dele sempre
/// gerou. A atmosfera e uma pergunta a mais sobre a MESMA seed (<see cref="Sorteada"/>), com sal
/// proprio -- senao "ser divino" sairia correlacionado com o bioma ou com a gravidade.
///
/// POR SER FUNCAO PURA ELA NAO VIAJA: o servidor pergunta aqui pra decidir quem absorve, e o cliente
/// pergunta aqui pra desenhar a marca na carta estelar e a linha da aba World. Uma funcao, duas
/// pontas, nenhum byte -- o desenho do <see cref="Espaco"/>.
/// =========================================================================================
///
/// ============================ A GARANTIA, E POR QUE ELA EXISTE ============================
/// O segundo caminho do despertar e lutar com quem JA tem ki divino. Num servidor novo ninguem tem:
/// se o sorteio nao desse nenhum mundo divino perto de onde se nasce, ninguem jamais despertaria.
/// Entao a regiao em volta da Terra (<see cref="RaioGarantidoEmCelulas"/>) tem pelo menos
/// <see cref="MinimoGarantido"/> mundos divinos POR CONSTRUCAO: se o sorteio der menos que isso, os de
/// menor <see cref="Peso"/> da regiao completam a conta.
///
/// O QUE ELA NAO SABE: planeta destruido. A garantia e sobre a seed, e destruir planeta e estado do
/// servidor -- se alguem explodir todos os mundos divinos da regiao, sobra o `Restore Planet` do admin.
/// =========================================================================================
/// </summary>
public static class AtmosferaDivina
{
	/// <summary>
	/// A RARIDADE: um mundo sorteado em cada cem. *"Planetas raros"*, nas palavras do dono.
	///
	/// O QUE O NUMERO DA NA CARTA, medido em 400 universos (62,4% das celulas tem sistema, 5,5 mundos
	/// por sistema): um mundo divino a cada ~30 celulas, e o mais perto da Terra a 2,7 celulas em media
	/// -- 18,6 minutos de voo a pe, meio minuto numa Spacepod no limite (39x) --, no pior caso a 6.
	/// 85% deles puxam ate 15x; 2,7% passam de 40x. Dobrar este numero poe o mais perto a ~3,8 celulas;
	/// cortar pela metade, a ~1,9.
	/// </summary>
	public const int UmEm = 100;

	/// <summary>
	/// A REGIAO GARANTIDA, em celulas de sistema a partir da celula (0,0) -- a da Terra, que e o zero
	/// do universo (<see cref="Espaco.PreFeitos"/>). Raio 8 = 17x17 celulas, ~990 mundos sorteados, e o
	/// canto mais longe fica a ~11 celulas da Terra. Com a raridade de hoje ela tem ~10 mundos divinos em
	/// media (medido: 9,7, de 2 a 22), ficaria com menos de dois uma vez em cada ~2.000 universos e sem
	/// nenhum uma vez em ~21.000 -- e pra esses que a garantia existe.
	/// </summary>
	public const int RaioGarantidoEmCelulas = 8;

	/// <summary>Quantos mundos divinos a regiao garantida tem NO MINIMO. Dois: um so viraria fila (e alvo).</summary>
	public const int MinimoGarantido = 2;

	/// <summary>
	/// OS LUGARES DO CATALOGO QUE SAO DIVINOS POR NATUREZA -- pelo nome da zona no manifesto dos mapas.
	///
	/// So o Reino dos Deuses (`God_Realm`, o z31): e nele que o DM poe as fendas divinas
	/// (`Areas.dm:362-372`: `rift_type = /obj/Rift/GodKi_Rift`). O Paraiso, o Mirante e o Outro Mundo
	/// NAO entram -- o original nao poe ki divino em nenhum deles, e "lugar sagrado" nao e "ar divino".
	/// </summary>
	public static readonly string[] DivinosPorNatureza = ["God_Realm"];

	/// <summary>
	/// Sal do sorteio. Nao pode ser `0x9E3779B9...`: o <see cref="Espaco.Misturar"/> ja faz
	/// `seed ^ 0x9E37...` na primeira linha, e repetir a constante devolveria a seed crua.
	/// </summary>
	private const ulong Sal = 0x6A09E667F3BCC909UL;

	/// <summary>
	/// DEFEITO INJETADO (bancada --godkiteste): a garantia nao completa nada -- o universo cujo sorteio
	/// nao deu mundo divino nenhum fica sem nenhum, e ninguem nele jamais despertaria.
	/// </summary>
	public static bool GarantiaDesligadaDeTeste;

	/// <summary>
	/// O PESO DE UM MUNDO NO SORTEIO -- o hash da seed dele com o sal daqui. Dele saem as duas
	/// respostas: o sorteio (<see cref="Sorteada"/>) e a ordem da garantia (o menor ganha).
	/// </summary>
	public static ulong Peso(ulong seedDoMundo) => Espaco.Misturar(seedDoMundo ^ Sal, 0, 0);

	/// <summary>O sorteio puro: este mundo saiu com atmosfera divina? So a seed dele entra.</summary>
	public static bool Sorteada(ulong seedDoMundo, int umEm = UmEm) =>
		umEm > 0 && Peso(seedDoMundo) % (ulong)umEm == 0;

	public static bool DivinaPorNatureza(string zona)
	{
		foreach (string nome in DivinosPorNatureza)
			if (string.Equals(nome, zona, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	// =====================================================================
	// A PERGUNTA -- as duas pontas chamam estas
	// =====================================================================
	/// <summary>
	/// ESTA ZONA TEM ATMOSFERA DIVINA?
	///
	///   * mapa feito a mao: so os da lista (<see cref="DivinosPorNatureza"/>);
	///   * mundo sorteado: o sorteio da seed dele, ou a garantia da regiao da Terra;
	///   * o espaco e os interiores (nave, Sala do Tempo, a mente, o bolso de um Majin): nunca. O ESPACO
	///     FICA DE FORA DE PROPOSITO -- ele tambem e `KindProcedural`, e a seed dele e a do universo.
	/// </summary>
	public static bool Tem(ulong seedDoUniverso, ZoneKey zona) => Tem(seedDoUniverso, zona, RegraDaAtmosfera.Hoje);

	/// <summary>A mesma pergunta com os botoes a mostra. Nenhum caminho do jogo chama esta: e da bancada.</summary>
	public static bool Tem(ulong seedDoUniverso, ZoneKey zona, RegraDaAtmosfera regra)
	{
		if (zona.Kind == ZoneKey.KindPremade) return DivinaPorNatureza(zona.Name);
		if (zona.Kind != ZoneKey.KindProcedural || Espaco.EhEspaco(zona)) return false;
		return DoMundo(seedDoUniverso, zona.Seed, regra);
	}

	/// <summary>O mesmo, a partir do corpo da carta estelar (que e o que o mapa e o pouso tem na mao).</summary>
	public static bool Tem(ulong seedDoUniverso, PlanetaNoEspaco p) =>
		p.Premade ? DivinaPorNatureza(p.Nome) : DoMundo(seedDoUniverso, p.Seed, RegraDaAtmosfera.Hoje);

	/// <summary>Sorteio OU garantia, pela seed do mundo.</summary>
	public static bool DoMundo(ulong seedDoUniverso, ulong seedDoMundo, RegraDaAtmosfera regra) =>
		Sorteada(seedDoMundo, regra.UmEm)
		|| (!GarantiaDesligadaDeTeste && Garantidos(seedDoUniverso, regra).Contains(seedDoMundo));

	// =====================================================================
	// A GARANTIA
	// =====================================================================
	/// <summary>A ultima garantia calculada. Imutavel depois de publicada: quem le nao precisa de trava.</summary>
	private sealed record Garantia(ulong Universo, RegraDaAtmosfera Regra, HashSet<ulong> Mundos);

	/// <summary>
	/// UMA ENTRADA SO, e basta: em jogo a pergunta e sempre (o universo deste servidor, a regra de hoje).
	/// `volatile` porque o servidor e o cliente de quem hospeda moram no mesmo processo -- a troca e de
	/// referencia inteira, entao nenhum dos dois ve um conjunto pela metade.
	/// </summary>
	private static volatile Garantia? _ultima;

	/// <summary>
	/// AS SEEDS DOS MUNDOS QUE GANHAM PELA GARANTIA neste universo -- vazio quando o sorteio ja deu o
	/// minimo na regiao (o caso de quase todo universo).
	///
	/// A ORDEM E O <see cref="Peso"/> (e a seed como desempate): deterministica, e independente da ordem
	/// em que a regiao e varrida. Custo: as 289 celulas da regiao, uma vez por universo (~1 ms); depois
	/// e uma comparacao de referencia.
	/// </summary>
	public static IReadOnlySet<ulong> Garantidos(ulong seedDoUniverso, RegraDaAtmosfera regra)
	{
		Garantia? g = _ultima;
		if (g != null && g.Universo == seedDoUniverso && g.Regra == regra) return g.Mundos;

		int naturais = 0;
		var candidatos = new List<(ulong Peso, ulong Seed)>();
		foreach ((PlanetaNoEspaco p, _, _, _) in Gerados(seedDoUniverso, 0, 0, regra.RaioGarantido))
		{
			if (Sorteada(p.Seed, regra.UmEm)) naturais++;
			else candidatos.Add((Peso(p.Seed), p.Seed));
		}

		var mundos = new HashSet<ulong>();
		int faltam = regra.MinimoGarantido - naturais;
		if (faltam > 0)
		{
			candidatos.Sort();
			for (int i = 0; i < candidatos.Count && i < faltam; i++) mundos.Add(candidatos[i].Seed);
		}

		_ultima = new Garantia(seedDoUniverso, regra, mundos);
		return mundos;
	}

	// =====================================================================
	// A VARREDURA -- pra carta, pro admin e pra bancada
	// =====================================================================
	/// <summary>
	/// Os mundos SORTEADOS das celulas em volta de (<paramref name="sx0"/>, <paramref name="sy0"/>), com o
	/// endereco de cada um. Os pre-feitos ficam de fora: eles nao sao sorteados.
	/// </summary>
	private static IEnumerable<(PlanetaNoEspaco Corpo, int Sx, int Sy, int K)> Gerados(
		ulong seedDoUniverso, int sx0, int sy0, int raio)
	{
		for (int sy = sy0 - raio; sy <= sy0 + raio; sy++)
			for (int sx = sx0 - raio; sx <= sx0 + raio; sx++)
			{
				if (Sistemas.Do(seedDoUniverso, sx, sy) is not { } s) continue;
				for (int k = 0; k < s.Orbitas; k++)
				{
					if (k == s.OrbitaPreFeita) continue;
					yield return (s.Planeta(k), sx, sy, k);
				}
			}
	}

	/// <summary>
	/// OS MUNDOS DIVINOS das celulas a ate <paramref name="raio"/> de (<paramref name="sx0"/>,
	/// <paramref name="sy0"/>). Monta um planeta por mundo varrido (uma string de nome cada): e
	/// consulta de admin e de bancada, nao de tique.
	/// </summary>
	public static List<MundoDivino> NaRegiao(ulong seedDoUniverso, int sx0, int sy0, int raio) =>
		NaRegiao(seedDoUniverso, sx0, sy0, raio, RegraDaAtmosfera.Hoje);

	public static List<MundoDivino> NaRegiao(ulong seedDoUniverso, int sx0, int sy0, int raio, RegraDaAtmosfera regra)
	{
		var l = new List<MundoDivino>();
		IReadOnlySet<ulong> garantidos = GarantiaDesligadaDeTeste ? new HashSet<ulong>() : Garantidos(seedDoUniverso, regra);
		foreach ((PlanetaNoEspaco p, int sx, int sy, int k) in Gerados(seedDoUniverso, sx0, sy0, raio))
		{
			bool sorteada = Sorteada(p.Seed, regra.UmEm);
			if (sorteada || garantidos.Contains(p.Seed)) l.Add(new MundoDivino(p, sx, sy, k, !sorteada));
		}
		return l;
	}

	/// <summary>
	/// O MUNDO DIVINO MAIS PERTO DE UM PONTO DA GALAXIA, ou nulo se nao ha nenhum nas celulas em volta.
	/// O raio padrao cobre a regiao garantida inteira a partir de qualquer ponto dela.
	/// </summary>
	public static MundoDivino? MaisProximo(ulong seedDoUniverso, Vec2 de, int raioEmCelulas = RaioGarantidoEmCelulas * 2)
	{
		SistemaId c = SistemaId.De(de);
		MundoDivino? melhor = null;
		double menor = double.MaxValue;
		foreach (MundoDivino m in NaRegiao(seedDoUniverso, c.Sx, c.Sy, raioEmCelulas))
		{
			double dx = m.Corpo.Pos.X - (double)de.X, dy = m.Corpo.Pos.Y - (double)de.Y;
			double d2 = dx * dx + dy * dy;
			if (d2 >= menor) continue;
			menor = d2;
			melhor = m;
		}
		return melhor;
	}
}
