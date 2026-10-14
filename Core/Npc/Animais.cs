using System.Text.Json;
using Jandirus.Core.World;

namespace Jandirus.Core.Npc;

/// <summary>
/// A FICHA DE UM BICHO -- o bloco <c>"animal": { ... }</c> de um molde do tipo <see cref="TipoDeNpc.Animal"/> no
/// `npcs.json`. O resto do molde (id, nome, `bpMin`/`bpMax`, coragem, furia) continua onde sempre esteve; aqui
/// fica so o que um bicho tem e gente nao tem.
/// </summary>
public sealed class FichaDeAnimal
{
	/// <summary>O nome da folha de sprite, um dos <see cref="Animais.Folhas"/> ("Wolf", "Animal Cow"...).</summary>
	public string Folha = "";

	/// <summary>
	/// BRAVO ataca quem chega perto (lobo, leao, dinossauro); MANSO nunca ataca -- pasta, e corre de quem bate
	/// nele (vaca, galinha, sapo).
	/// </summary>
	public bool Bravo;

	/// <summary>Quantos pedacos de carne a carcaca deste bicho da.</summary>
	public int Carne = 1;

	/// <summary>O peso desta especie no sorteio de quem nasce (3 = tres vezes mais comum que 1).</summary>
	public double Peso = 1;

	/// <summary>Le o bloco `animal` de um molde. Nulo quando o molde nao tem o bloco.</summary>
	public static FichaDeAnimal? Ler(JsonElement molde)
	{
		if (!molde.TryGetProperty("animal", out JsonElement a) || a.ValueKind != JsonValueKind.Object) return null;
		return new FichaDeAnimal
		{
			Folha = a.TryGetProperty("folha", out JsonElement f) && f.ValueKind == JsonValueKind.String ? f.GetString() ?? "" : "",
			Bravo = a.TryGetProperty("bravo", out JsonElement b) && b.ValueKind == JsonValueKind.True,
			Carne = a.TryGetProperty("carne", out JsonElement c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 1,
			Peso = a.TryGetProperty("peso", out JsonElement p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 1,
		};
	}
}

/// <summary>
/// OS ANIMAIS -- pedido do dono em 2026-10-10:
///
///   *"adicione animais npcs (como lobos etc) (ja tem sprite deles nos assets e tinha uns animais no jogo do
///   byond mas la era diferente do q planejo aqui a ideia deles). a forca deles e fixa (normalmente em torno de
///   10 de bp a 100 de bp), eles n teleportam nem nada, sao animais comuns q as vezes batem um pouco forte (100
///   de bp). ao matar eles vc pode lootear eles e pegar carne (tb ja tem sprite de carne e tb tinha no DM) ao
///   qual vc pode comer e recuperar bastante nutricao quando n tem frutas etc."*
///
/// AQUI FICAM OS NUMEROS, todos com nome, e as contas puras. Quem poe o bicho no mundo, o guia, o abate e monta
/// a carcaca e o servidor (`GameServer.Animais.cs`).
///
/// ============================ O QUE E DAQUI E O QUE NAO E ============================
///   * A ESPECIE E DADO: cada bicho e um molde do tipo `animal` no `npcs.json` (forca, se e bravo, quanta carne,
///     quao comum, que folha de sprite). Especie nova com arte que ja esta na lista <see cref="Folhas"/> e uma
///     entrada de arquivo, sem recompilar.
///   * A FORCA E FIXA: `bpMin` = `bpMax` no molde, e ela nao acompanha a media dos jogadores (`bpRelativo` e
///     recusado no boot). O servidor ainda reancora o numero a cada segundo -- brigar nao deixa bicho mais forte.
///   * NO DM ERA DIFERENTE, e o dono avisou: la o lobo (`mob/npc/Enemy/Wolf`, `NPClist.dm:144`, BP 10) crescia
///     com a media do servidor (`NPCTicker`, `:72`) e os mansos eram a familia `mob/npc/shy` (`:375-490`, BP 1).
///     O que veio de la e a lista de bichos e a carne; a regra e a do pedido.
///   * NAO E O INIMIGO COMUM. O tipo <see cref="TipoDeNpc.Inimigo"/> continua desligado
///     (`Povoamento.InimigoComumLigado`); bicho e outro tipo, com outra regra.
/// ======================================================================================
/// </summary>
public static class Animais
{
	/// <summary>
	/// A chave geral. Desligada, nenhum bicho novo nasce (os que estao de pe ficam ate sumirem sozinhos). Quem a
	/// desliga de fora e a linha de comando `--semfauna` do servidor -- pra bancada de janela que deixa um
	/// jogador andando por um planeta e nao pode ter um lobo de passagem na medida.
	/// </summary>
	public static bool Ligado = true;

	// =====================================================================
	// 1. O CORPO -- a "raca" e as folhas de sprite
	// =====================================================================

	/// <summary>
	/// A "RACA" QUE TODO BICHO CARREGA. Nao existe no `races.json` (bicho nao tem proto, classe nem arvore de
	/// skill) e e por ela que as duas pontas reconhecem um animal: o desenho (`VisualCatalog.CorpoSprite` troca
	/// o boneco em camadas por UMA folha) e os cortes de regra (<see cref="SemBonusDeLuta"/>).
	/// </summary>
	public const string Raca = "Animal";

	public static bool EhAnimal(string? raca) => string.Equals(raca, Raca, StringComparison.Ordinal);

	private const string PastaDasFolhas = "res://Assets/Sprites/DU/Mobs/";

	/// <summary>
	/// AS FOLHAS DE BICHO QUE O JOGO JA TEM (`Assets/Sprites/DU/Mobs/`, convertidas dos `.dmi`). O INDICE desta
	/// lista e o que viaja na aparencia (`Appearance.Corpo`), exatamente como o degrau do bio-androide: o
	/// servidor escreve o numero e o cliente abre a mesma lista. Por isso folha nova entra NO FIM.
	///
	/// Todas sao 32x32 com `walk_*` nas quatro direcoes; as que nao tem pose de ataque nem de nocaute caem na
	/// caminhada (a escada do `CharacterVisual.Escolher`), e o bicho abatido deita pela rotacao do corpo.
	/// </summary>
	public static readonly string[] Folhas =
	[
		"Wolf", "Animal Lion", "Animal DinoBird", "Animal Bat",
		"Animal Cow", "Animal Chicken", "Animal, Frog", "NPC Sheep", "Turtle",
	];

	/// <summary>O indice de uma folha pelo nome, ou -1 quando ela nao esta na lista.</summary>
	public static int IndiceDaFolha(string nome) =>
		Array.FindIndex(Folhas, f => string.Equals(f, nome, StringComparison.OrdinalIgnoreCase));

	/// <summary>O `res://` da folha deste indice. Indice fora da lista cai na primeira: desenhar o bicho errado e melhor que nao desenhar nada.</summary>
	public static string CaminhoDaFolha(int indice) =>
		PastaDasFolhas + Folhas[indice >= 0 && indice < Folhas.Length ? indice : 0] + ".tres";

	// =====================================================================
	// 2. A FORCA
	// =====================================================================

	/// <summary>A forca FIXA desta especie: o `bpMax` do molde (que tem de ser igual ao `bpMin` -- ver <see cref="Problemas"/>).</summary>
	public static double BpDe(MoldeDeNpc molde) => Math.Max(molde.BpMax, 1);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--animaisteste`): o BP do bicho deixa de ser reancorado -- ele passa a subir
	/// com a luta, como o de qualquer corpo. Lido pelo proprio pino, no `ManterOAnimal` (`GameServer.Animais.cs`).
	/// </summary>
	public static bool BpSoltoDeTeste;

	/// <summary>
	/// CONTRA BICHO NAO HA "LUTAR COM QUEM E MAIS FORTE". O multiplicador de ganho por gap de poder
	/// (`Fighter.FightGainMult`, ate 2x) vale 1 quando um dos dois e animal: socar um leao rende o mesmo que
	/// socar o ar. Sem isto um novato de BP 2 teria num leao de BP 100 um parceiro de treino de 2x que nunca
	/// cansa e nunca fica mais forte.
	/// </summary>
	public static bool SemBonusDeLuta(string minhaRaca, string racaDoOutro) =>
		!BonusDeLutaDeTeste && (EhAnimal(minhaRaca) || EhAnimal(racaDoOutro));

	/// <summary>DEFEITO INJETADO (bancada): o gap de poder contra um bicho volta a multiplicar o ganho. Lido por <see cref="SemBonusDeLuta"/>.</summary>
	public static bool BonusDeLutaDeTeste;

	// =====================================================================
	// 3. QUANTOS, E ONDE
	// =====================================================================

	/// <summary>
	/// OS PLANETAS DO CATALOGO QUE NAO TEM FAUNA NENHUMA, pelo nome da zona (o do mapa), sem ligar pra caixa.
	/// Decisao do dono em 2026-10-10: *"esses planetas so vao ter animais, makyo star e o unico q n tem nada"*.
	///
	///   * MAKYO STAR nao tem nada: nem bicho, nem fruta.
	///   * NAMEK, ICER e ARLIA nao tem (nem vao ter) arvore de fruta: la a UNICA comida e a caca, entao eles
	///     NAO PODEM entrar nesta lista -- a bancada `--animaisteste` cobra bicho nascendo nos tres.
	///   * Todo o resto (a Terra, Vegeta, Arconia e todo mundo gerado) tem fauna pela regra geral.
	///
	/// Pra tirar a fauna de um planeta, escreva o nome dele aqui; pra devolver, apague.
	/// </summary>
	public static readonly string[] PlanetasSemFauna = ["Makyo_Star"];

	/// <summary>Este planeta do catalogo tem fauna? Ver <see cref="PlanetasSemFauna"/>.</summary>
	public static bool PlanetaTemFauna(string nomeDaZona)
	{
		if (FaunaEmTodoPlanetaDeTeste) return true;
		foreach (string semNada in PlanetasSemFauna)
			if (string.Equals(semNada, nomeDaZona, StringComparison.OrdinalIgnoreCase)) return false;
		return true;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--animaisteste`): a lista de planetas sem fauna deixa de valer -- Makyo Star
	/// passa a ter bicho como qualquer outro. Lido por <see cref="PlanetaTemFauna"/>.
	/// </summary>
	public static bool FaunaEmTodoPlanetaDeTeste;

	/// <summary>
	/// UM BICHO A CADA TANTAS CELULAS DE MAPA: a fauna de um planeta e proporcional ao TAMANHO dele. A Terra
	/// (500x500) da 21 e bate no teto; um mundo gerado pequeno (192x192) da 3 e sobe pro piso.
	/// </summary>
	public const int CelulasPorAnimal = 12_000;

	/// <summary>O piso e o teto da fauna de UM planeta, seja qual for o tamanho.</summary>
	public const int MinPorPlaneta = 6, MaxPorPlaneta = 16;

	/// <summary>
	/// O teto do servidor inteiro. Bicho so existe em planeta COM JOGADOR (ver o cabecalho do
	/// `GameServer.Animais.cs`), entao isto e quantos planetas habitados ao mesmo tempo tem fauna cheia: seis.
	/// Somado ao plano de cidadaos (148) fica abaixo do `Povoamento.MaxNoServidor` (300) com folga pras sagas.
	/// </summary>
	public const int MaxNoServidor = 96;

	/// <summary>A fauna CHEIA de um planeta deste tamanho.</summary>
	public static int AlvoDoPlaneta(int largura, int altura) =>
		Math.Clamp((int)Math.Round((double)largura * altura / CelulasPorAnimal), MinPorPlaneta, MaxPorPlaneta);

	/// <summary>
	/// A REPOSICAO: cada bicho abatido volta ao estoque do planeta depois de tantos segundos. O estoque e por
	/// planeta e anda com o relogio mesmo sem ninguem la -- sair e voltar nao enche a fauna de novo.
	/// </summary>
	public const double SegundosPorReposicao = 90;

	/// <summary>O estoque depois de <paramref name="segundos"/>: sobe devagar e para no <paramref name="alvo"/>.</summary>
	public static double Repor(double estoque, double segundos, int alvo) =>
		Math.Min(alvo, Math.Max(estoque, 0) + Math.Max(segundos, 0) / SegundosPorReposicao);

	/// <summary>
	/// O ANEL EM QUE UM BICHO APARECE, em tiles a partir de um jogador: fora da tela dele (a tela mostra uns 20
	/// tiles pra cada lado) e perto o bastante pra ser achado andando. E nunca a menos do minimo de NENHUM
	/// jogador -- bicho nao brota na frente de ninguem.
	/// </summary>
	public const float DistMinDeGenteTiles = 24, DistMaxDeGenteTiles = 64;

	/// <summary>Quantos pontos o servidor sorteia por segundo, por planeta, atras de um lugar que sirva.</summary>
	public const int TentativasPorNascimento = 24;

	/// <summary>
	/// O bicho que ficou a mais disto de TODO jogador por <see cref="SegundosAteSumir"/> volta pro estoque (some
	/// sem morrer) e reaparece no anel de alguem. E o que mantem a fauna onde ha quem a encontre.
	/// </summary>
	public const float DistQueSomeTiles = 100;

	/// <inheritdoc cref="DistQueSomeTiles"/>
	public const double SegundosAteSumir = 20;

	/// <summary>
	/// LONGE DE CIDADE E DE BASE: nenhuma celula sob teto (predio do mapa ou teto erguido por jogador) nem
	/// parede erguida por jogador neste raio da TOCA, em tiles. Maior que o pasto mais o faro
	/// (<see cref="PastoTiles"/> + <see cref="FaroTiles"/>) de proposito: nem pastando o bravo chega a notar
	/// quem esta encostado na parede de casa.
	/// </summary>
	public const int TilesLongeDeConstrucao = 20;

	/// <summary>Longe do ponto de chegada do planeta, em tiles: quem acabou de nascer ou de pousar nao e recebido por um leao.</summary>
	public const int TilesLongeDoBerco = 28;

	/// <summary>
	/// O bicho precisa de CHAO: partindo do ponto, tem de dar pra alcancar a pe pelo menos estas celulas. Corta
	/// o patio murado, a ilhota e o buraco entre montanhas -- lugares de onde ele nunca sairia.
	/// </summary>
	public const int CelulasDeChaoLivre = 150;

	/// <summary>DEFEITO INJETADO (bancada): o lugar deixa de ser conferido -- vale qualquer celula. Lido por <see cref="LugarServe"/>.</summary>
	public static bool QualquerLugarDeTeste;

	/// <summary>
	/// ESTE PONTO SERVE PRA UM BICHO NASCER? Chao de verdade (nem parede, nem agua, nem beirada), longe de
	/// construcao e com espaco pra andar. A distancia a jogadores e ao berco e do servidor, que e quem sabe
	/// onde eles estao.
	/// </summary>
	public static bool LugarServe(ZoneCollision mapa, int cx, int cy)
	{
		if (QualquerLugarDeTeste) return true;
		return mapa.ServeDeChao(cx, cy) && !PertoDeConstrucao(mapa, cx, cy) && TemChaoLivre(mapa, cx, cy);
	}

	/// <summary>Ha celula sob teto ou parede erguida por jogador a menos de <see cref="TilesLongeDeConstrucao"/> deste ponto?</summary>
	public static bool PertoDeConstrucao(ZoneCollision mapa, int cx, int cy)
	{
		const int r = TilesLongeDeConstrucao;
		for (int y = cy - r; y <= cy + r; y++)
			for (int x = cx - r; x <= cx + r; x++)
				if (mapa.NasceuDentro(x, y) || mapa.Coberta(x, y) || mapa.Erguida(x, y)) return true;
		return false;
	}

	/// <summary>
	/// Da pra alcancar a pe <see cref="CelulasDeChaoLivre"/> celulas a partir daqui? Uma enchente curta (para
	/// assim que chega no numero), pelos quatro vizinhos, com a mesma pergunta de chao do nascimento.
	/// </summary>
	public static bool TemChaoLivre(ZoneCollision mapa, int cx, int cy)
	{
		if (!mapa.ServeDeChao(cx, cy)) return false;
		var vistas = new HashSet<int> { cy * mapa.Width + cx };
		var fila = new Queue<(int X, int Y)>();
		fila.Enqueue((cx, cy));
		while (fila.Count > 0)
		{
			if (vistas.Count >= CelulasDeChaoLivre) return true;
			(int x, int y) = fila.Dequeue();
			Espiar(x + 1, y); Espiar(x - 1, y); Espiar(x, y + 1); Espiar(x, y - 1);
		}
		return vistas.Count >= CelulasDeChaoLivre;

		void Espiar(int nx, int ny)
		{
			if (!mapa.ServeDeChao(nx, ny) || !vistas.Add(ny * mapa.Width + nx)) return;
			fila.Enqueue((nx, ny));
		}
	}

	/// <summary>
	/// QUAL ESPECIE NASCE: sorteio pelo <see cref="FichaDeAnimal.Peso"/>. <paramref name="sorteio"/> e um numero
	/// em [0,1). Nulo so com a lista vazia.
	/// </summary>
	public static MoldeDeNpc? Escolher(IReadOnlyList<MoldeDeNpc> especies, double sorteio)
	{
		double total = 0;
		foreach (MoldeDeNpc m in especies) total += Math.Max(m.Animal?.Peso ?? 0, 0);
		if (especies.Count == 0 || total <= 0) return especies.Count > 0 ? especies[0] : null;

		double alvo = Math.Clamp(sorteio, 0, 0.999999) * total;
		foreach (MoldeDeNpc m in especies)
		{
			alvo -= Math.Max(m.Animal?.Peso ?? 0, 0);
			if (alvo < 0) return m;
		}
		return especies[^1];
	}

	// =====================================================================
	// 4. O COMPORTAMENTO
	// =====================================================================

	/// <summary>
	/// O PASTO: o bicho anda de um ponto a outro dentro deste raio (em tiles) da TOCA, que e onde ele nasceu.
	/// E o que mantem a fauna onde ela apareceu -- no mato, e nao dentro da cidade.
	/// </summary>
	public const float PastoTiles = 8;

	/// <summary>Quanto tempo ele fica parado entre um passeio e outro, em segundos (sorteado entre os dois).</summary>
	public const double PastoParadoMin = 3, PastoParadoMax = 9;

	/// <summary>O teto de UM passeio, em segundos: nao chegou (um lago no caminho, a volta de uma perseguicao comprida), para e sorteia outro.</summary>
	public const double SegundosDePasseio = 8;

	/// <summary>A que distancia (px) do destino o passeio conta como chegado. A mesma folga do passeio dos NPCs (`Rotina.ChegouEmPx`).</summary>
	public const float ChegouNoPastoPx = 12f;

	/// <summary>
	/// O PASSO DE UM BICHO, em vezes o passo de gente (`MoveRules.BaseSpeedPx`): pastando ele anda a pouco mais
	/// da metade do passo de um jogador. A CORRIDA e a de todo corpo (`MoveRules.MultiplicadorCorrida`, 2,2x, e
	/// gasta Ki como a de todo corpo): correndo -- o bravo atras da presa, o manso fugindo -- ele passa de quem
	/// ANDA e perde de quem CORRE. Quem quer fugir de um lobo corre; quem quer alcancar uma vaca, tambem.
	///
	/// Escrito aqui, e nao herdado da ficha: a ficha neutra do bicho sairia com um passo parecido por acidente
	/// da conta de atributos, e mudaria calada com ela.
	/// </summary>
	public const float VelocidadeDoPasso = 0.55f;

	/// <summary>A que distancia o bravo NOTA um jogador, em tiles. Curto: bicho defende o pedaco dele, nao caca pelo mapa.</summary>
	public const float FaroTiles = 6;

	/// <summary>Ate onde ele persegue quem ja notou, em tiles. Maior que o faro pra briga nao piscar na fronteira.</summary>
	public const float PerseguicaoTiles = 12;

	/// <summary>
	/// O TERRITORIO: a mais disto (em tiles) da TOCA, o bravo larga a presa e volta. E o que impede alguem de
	/// puxar um leao ate dentro da cidade andando de costas (a toca fica a `TilesLongeDeConstrucao` de qualquer
	/// predio: a perseguicao acaba na beira da cidade).
	/// </summary>
	public const float TerritorioTiles = 22;

	/// <summary>Depois de largar a presa na borda do territorio, ele ignora todo mundo por estes segundos enquanto volta.</summary>
	public const double SegundosDeVolta = 6;

	/// <summary>
	/// A TREGUA: derrubou a presa, o bravo da a briga por ganha e ignora todo mundo por estes segundos. O golpe de
	/// bicho nao mata (e o nao-letal de todo NPC), e sem a tregua quem caisse ao lado de um leao levantaria pra
	/// cair de novo, sem nunca conseguir sair de perto: o nocaute dura ate 12 s, e sobra tempo pra ir embora.
	/// </summary>
	public const double SegundosDeTregua = 30;

	/// <summary>DEFEITO INJETADO (bancada): sem tregua -- o bravo volta a morder quem acabou de levantar. Lido pelo `PresaDoAnimal`.</summary>
	public static bool SemTreguaDeTeste;

	/// <summary>
	/// O bicho que apanha e nao tem em quem morder corre de quem bateu por estes segundos (renovados a cada
	/// golpe): o manso sempre, o bravo quando quem bate esta fora do alcance dele (voando, atirando de longe).
	/// </summary>
	public const double SegundosDeFuga = 5;

	/// <summary>
	/// O DESVIO: barrado no meio da fuga (uma arvore, uma pedra, a beira d'agua), o bicho corre DE LADO por estes
	/// segundos e retoma o rumo -- e assim que ele contorna o que estava na frente. Se o lado tambem estiver
	/// fechado, troca de lado.
	/// </summary>
	public const double SegundosDeDesvio = 0.6;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--animaisteste`): sem desvio -- o bicho que foge e da de cara com um obstaculo
	/// fica empurrando ele, parado, de costas pra quem bate. Lido pelo ramo da fuga, no `GuiarOAnimal`.
	/// </summary>
	public static bool SemDesvioDeTeste;

	/// <summary>
	/// A DISTANCIA DE BOTE, em pixels: o bicho encosta ate aqui antes de morder. Menor que a de gente (34) de
	/// proposito -- gente fecha o resto do caminho com a investida do golpe, e bicho NAO INVESTE (ver
	/// <see cref="InvesteDeTeste"/>): ele tem de chegar andando ate dentro do alcance do soco (40 px).
	/// </summary>
	public const float DistanciaDoBote = 25f;

	/// <summary>
	/// DEFEITO INJETADO (bancada): o bicho volta a ter a INVESTIDA de todo corpo -- o salto de ate 15 tiles que o
	/// golpe da em quem esta marcado. E o "teleporte" que o dono mandou nao existir. Lido pela propria guarda,
	/// no `Aproximar` (`GameServer.Combat.cs`).
	/// </summary>
	public static bool InvesteDeTeste;

	/// <summary>
	/// A MENTE DE UM BICHO: o cerebro de combate de todo NPC (`Ai.Cerebro`), com tres manivelas viradas.
	/// Chamado pelo <see cref="Temperamento.Montar"/> pra todo molde do tipo animal -- entao vale em qualquer
	/// caminho que monte (ou remonte) a mente de um bicho.
	///
	///   * SEM INVESTIDA: o cerebro nem pede o golpe de longe (quem RECUSA o salto de verdade e o servidor, no
	///     `Aproximar`; isto so poupa o bicho de socar o ar tentando). Ele fecha a distancia CORRENDO -- ver
	///     <see cref="VelocidadeDoPasso"/>;
	///   * A DISTANCIA DE BOTE no lugar da de gente -- ver <see cref="DistanciaDoBote"/>;
	///   * A VOZ E A DA ESPECIE: bicho nao grita "Toma essa!" no chat -- rosna, uiva, muge. Ver <see cref="VozDe"/>.
	///
	/// O resto sai do molde como em todo NPC: `inteligencia` 0 (nunca apara, nunca para pra reunir ki),
	/// `coragem` (quando recua) e `furia` (o quanto o golpe sai pesado).
	/// </summary>
	public static void MenteDeBicho(Ai.Cerebro c, string folha)
	{
		c.AlcanceDaInvestida = 0;
		c.ChanceDeInvestir = 0;
		c.DistanciaIdeal = DistanciaDoBote;
		c.Boca.Bicho = VozDe(folha);
	}

	// =====================================================================
	// A VOZ -- o som de cada especie
	// =====================================================================

	/// <summary>
	/// ============================ O SOM DE CADA BICHO, PELA FOLHA DE SPRITE DELE ============================
	/// O dono, 2026-10-13: *"os animais quando estao lutando, eles falam igual um npc normal durante combate, eles
	/// deveriam fazer o som de seus respectivos sprites/animais"*.
	///
	/// E TEXTO, E NAO AUDIO: o jogo nao tem arquivo de som de bicho nenhum (o unico rugido da pasta, `Roar.wav`, e o do
	/// Oozaru), e o original tambem nao tinha. O que o bicho "diz" sai pelo MESMO funil da fala de todo corpo
	/// (`GameServer.Falar`): aparece no balao sobre ele e no chat de quem esta perto, e o som que termina em "!" e
	/// grito -- o rugido do leao se ouve de mais longe que o coaxar do sapo.
	///
	/// DUAS LISTAS POR ESPECIE: a de BRAVO (atacando, vencendo) e a de FERIDO (apanhando, no limite). Quem escolhe
	/// entre as duas e o cerebro, pela ocasiao (`Ai.Falatorio.Bicho`). Nenhum som se repete entre especies -- a
	/// bancada `--animaisteste` cobra --, entao da pra saber quem rosnou sem olhar.
	///
	/// A CHAVE E A FOLHA (`FichaDeAnimal.Folha`), e nao o id do molde: e o SPRITE que diz que bicho e aquele, e duas
	/// especies do `npcs.json` desenhadas com a mesma folha tem de soar igual.
	/// ==========================================================================================================
	/// </summary>
	private static readonly Dictionary<string, Ai.VozDeBicho> Vozes = new(StringComparer.OrdinalIgnoreCase)
	{
		["Wolf"] = new(["Grrrrr...", "Auuuuuuu!", "Grrrau! Grrrau!"], ["Cain! Cain!", "Uuuuh..."]),
		["Animal Lion"] = new(["ROOOAAAR!", "Rrrrrrrr...", "RAAAUR!"], ["Grruuuh...", "Rrruh..."]),
		["Animal DinoBird"] = new(["SKREEEEE!", "KRAAAA!", "Graaak! Graaak!"], ["Skriiih...", "Kruuu..."]),
		["Animal Bat"] = new(["Skriii! Skriii!", "Iiiic! Iiiic!", "Tsiii!"], ["Iiic...", "Tsii..."]),
		["Animal Cow"] = new(["Muuuuuu!", "MUUU!"], ["Muuuh...", "Mmmuu..."]),
		["Animal Chicken"] = new(["Có! Có có có!", "Cocoricóóó!"], ["Pó pó pó...", "Cóóó..."]),
		["Animal, Frog"] = new(["Croac! Croac!", "Rebbit!"], ["Croooac...", "Coax..."]),
		["NPC Sheep"] = new(["Bééééé!", "Mééé!"], ["Bééé...", "Mééh..."]),
		["Turtle"] = new(["Hsssss!", "Fssss!"], ["Hsss...", "Fff..."]),
	};

	/// <summary>A voz de um bicho cuja folha nao esta na tabela (especie nova no `npcs.json`): bicho nenhum fala como gente.</summary>
	private static readonly Ai.VozDeBicho VozQualquer = new(["Rrrrr!", "Grah!"], ["Nhh..."]);

	/// <summary>A voz da especie desta folha de sprite. Nunca nula: folha sem voz propria ganha a <see cref="VozQualquer"/>.</summary>
	public static Ai.VozDeBicho VozDe(string folha) => Vozes.GetValueOrDefault(folha) ?? VozQualquer;

	/// <summary>Esta folha tem voz PROPRIA na tabela? A bancada cobra as nove de <see cref="Folhas"/>.</summary>
	public static bool TemVozPropria(string folha) => Vozes.ContainsKey(folha);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--animaisteste`): o funil da fala deixa passar o que quer que chegue na boca de um
	/// bicho. Lido pela propria <see cref="NaBocaDoBicho"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool BichoFalaLinguaDeGenteDeTeste;

	/// <summary>
	/// O QUE SAI DA BOCA DE UM BICHO, venha a frase de onde vier: ela mesma, se ja e um som da especie; senao, um som de
	/// bravo no lugar. E A SEGUNDA TRANCA -- quem escolhe o som certo pela ocasiao e o cerebro (`Ai.Falatorio.Bicho`), e
	/// esta fica no funil da fala do servidor (`GameServer.Falar`), pro caminho de fala que nasca amanha sem saber que
	/// bicho existe.
	/// </summary>
	public static string NaBocaDoBicho(string folha, string texto, Random rng)
	{
		if (BichoFalaLinguaDeGenteDeTeste) return texto;
		Ai.VozDeBicho voz = VozDe(folha);
		return Array.IndexOf(voz.Bravo, texto) >= 0 || Array.IndexOf(voz.Ferido, texto) >= 0
			? texto
			: voz.Bravo[rng.Next(voz.Bravo.Length)];
	}

	/// <summary>DEFEITO INJETADO (bancada): o manso passa a cacar como o bravo. Lido pelo `GuiarOAnimal`.</summary>
	public static bool MansoAtacaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): bicho nocauteado deixa de morrer -- fica no chao e levanta, como gente. Lido
	/// pelo `GuiarOAnimal` e pelo `TickDosAnimais`.
	/// </summary>
	public static bool NocauteNaoMataDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): a derrota com bicho no meio volta a ter as consequencias de gente (Zenkai de
	/// quem apanhou, luto de quem viu). Lido pelo corte, no `AoPerderALuta`.
	/// </summary>
	public static bool DerrotaDeGenteDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): apanhar de bicho volta a render ganho de luta e a ligar a "luta de verdade" de
	/// quem so apanha. Lido pelo `GolpeDeBichoNaoTreina` (`GameServer.Animais.cs`), que o `Atacar` consulta.
	/// </summary>
	public static bool ApanharTreinaDeTeste;

	// =====================================================================
	// 5. A CARCACA E A CARNE
	// =====================================================================

	/// <summary>
	/// QUANTO UM PEDACO DE CARNE ALIMENTA. O tanque cheio e 50 e a maca da 6 (`CatalogoDeItens`): um pedaco
	/// enche mais de meio estomago.
	///
	/// DIVERGENCIA DECLARADA DO DM: la a carne do bicho nasce CRUA (`obj/items/food/corpse`, `Food.dm:96-102`,
	/// nutricao 10) e so a cozida vale 30 (`Cooked_Meat`, `:104-108`). O port nao tem cozinha e o dono pediu
	/// "bastante nutricao": a carne daqui ja vale a cozida.
	/// </summary>
	public const double NutricaoDaCarne = 30;

	/// <summary>Quanto tempo a carcaca fica no chao esperando alguem. Depois disso ela some (com a carne que sobrou).</summary>
	public const double SegundosDaCarcaca = 240;

	/// <summary>Quantas carcacas cabem num planeta. Passou disso, a mais antiga some.</summary>
	public const int MaxCarcacasPorZona = 12;

	// =====================================================================
	// 6. A CONFERENCIA DO ARQUIVO
	// =====================================================================

	/// <summary>
	/// O QUE HA DE ERRADO NA PARTE DE BICHO DESTE MOLDE. Vazio = coerente. Chamado pelo
	/// <see cref="MoldeDeNpc.Problemas"/>, que grita no boot com o id do molde.
	/// </summary>
	public static List<string> Problemas(MoldeDeNpc m)
	{
		var p = new List<string>();
		if (m.Tipo != TipoDeNpc.Animal)
		{
			if (m.Animal != null) p.Add($"'{m.Id}': tem o bloco 'animal' mas o tipo e '{m.Tipo}' -- bicho e tipo 'animal'.");
			return p;
		}

		if (m.Animal is not { } a)
		{
			p.Add($"'{m.Id}': tipo 'animal' sem o bloco 'animal' (folha, bravo, carne, peso).");
			return p;
		}

		if (IndiceDaFolha(a.Folha) < 0)
			p.Add($"'{m.Id}': a folha '{a.Folha}' nao esta em `Animais.Folhas` -- use uma de: {string.Join(", ", Folhas)}.");
		if (m.BpRelativo > 0)
			p.Add($"'{m.Id}': bicho com 'bpRelativo' -- a forca de animal e FIXA, nao acompanha os jogadores.");
		if (m.BpMin != m.BpMax)
			p.Add($"'{m.Id}': bpMin ({m.BpMin}) diferente de bpMax ({m.BpMax}) -- a forca de animal e FIXA por especie.");
		if (a.Carne < 0) p.Add($"'{m.Id}': animal.carne = {a.Carne}, negativo.");
		if (a.Peso <= 0) p.Add($"'{m.Id}': animal.peso = {a.Peso} -- com zero ou menos a especie nunca nasce.");
		return p;
	}
}
