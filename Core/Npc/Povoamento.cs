using System.Text.Json;

namespace Jandirus.Core.Npc;

/// <summary>
/// UMA LINHA DO PLANO DE POVOAMENTO: quantos corpos de um molde um planeta mantem de pe.
///
/// E o `POP_VEGETA_COMMON` / `POP_EARTH_HUMANS` / `POP_NAMEK_NAMEKS` do original
/// (PlanetPopulation.dm:17), com uma diferenca de desenho: la os tres sao `#define` e cada um tem
/// um proc proprio (`populate_vegeta`, `populate_earth`, `populate_namek`) que repete a mesma
/// dezena de linhas. Aqui e uma LINHA DE DADO, e acrescentar um planeta e acrescentar a linha.
///
/// **O QUE ELA NAO DIZ E A RACA.** De proposito: a raca sai do BERCO (ver
/// <see cref="Races.Bercos.RacasNascidasEm"/>), que e a mesma regra que decide onde o JOGADOR
/// nasce. Escrever a raca aqui seria a segunda tabela raca->planeta do projeto, e a primeira ja
/// tem duas linhas erradas documentadas (`CharacterDraft.RacasDoPlaneta` poe Icer em Vegeta e
/// Arlian em Namek). Duas tabelas discordam; uma so nao tem como.
/// </summary>
public sealed class LinhaDePovoamento
{
	/// <summary>A zona pre-feita ("Earth", "Vegeta", "Namek", ...). E o `pop_planet` do DM.</summary>
	public string Planeta = "";

	/// <summary>Que molde nasce ali. O tipo dele passa pelo <see cref="Povoamento.PodeNascer"/>.</summary>
	public string Molde = "";

	/// <summary>O alvo. A manutencao COMPLETA ate aqui; ela nunca mata ninguem pra chegar no numero.</summary>
	public int Quantos;
}

/// <summary>
/// O POVOAMENTO: quanta gente o mundo mantem viva, onde, e **quem ainda nao nasce**.
///
/// ============================ O RECORTE DO DONO MORA AQUI, E ELE E UMA FUNCAO ============================
/// O pedido foi literal: *"spawner de npc por enquanto spawna SO NPCS NORMAIS (humanos, saiyajins,
/// nameks, etc) em seus devidos planetas. NPCs INIMIGOS que nao sao chefe POR ENQUANTO NAO SPAWNAM
/// pois vamos rever essa parte, mas os CHEFES DE SAGA ja podem comecar a spawnar."*
///
/// Ele podia ter virado tres coisas, e duas seriam erradas:
///
///   * um `if` no spawner -- some no dia em que aparecer um segundo caminho de nascimento (e ja ha
///     tres: o povoamento, o admin e a bancada);
///   * uma lista de ids proibidos no codigo -- envelhece calada quando o dono acrescenta um molde
///     novo no `npcs.json`, que e justamente o arquivo que ele edita sem recompilar;
///   * **uma funcao de UMA linha sobre o TIPO** -- e e esta. Molde novo nasce ja submetido ao
///     recorte, e a bancada afirma o recorte inteiro perguntando por tipo e nao por nome.
/// ====================================================================================================
///
/// ============================ E O CAMINHO DO INIMIGO ESTA PRONTO, NAO APAGADO ============================
/// Nada foi removido: os quatro moldes de inimigo continuam no `npcs.json`, continuam sendo lidos,
/// continuam sorteando ficha, e o <see cref="SorteioDeNpc"/> nao sabe deste arquivo. O que este
/// recorte desliga e o NASCIMENTO deles -- uma linha, com o motivo do dono escrito ao lado.
///
/// Religar e trocar <see cref="InimigoComumLigado"/> por `true`. A bancada `--povoteste` afirma que
/// hoje ele esta desligado E que o caminho funciona quando ligado, exercitando os dois com a MESMA
/// funcao de producao -- senao "esta pronto" seria promessa.
/// ======================================================================================================
/// </summary>
public static class Povoamento
{
	/// <summary>
	/// **DESLIGADO POR PEDIDO DO DONO** -- *"NPCs INIMIGOS que nao sao chefe POR ENQUANTO NAO
	/// SPAWNAM pois vamos rever essa parte"*.
	///
	/// O original tem o mesmo interruptor e pelo mesmo tipo de motivo: `npcspawnson`
	/// (NPCspawner.dm:4), lido no `New()` de todo `mob/npc` (NPClist.dm:54), que MATA o mob se o
	/// toggle estiver desligado. Aqui a recusa e antes do nascimento, que e mais barato e mais
	/// honesto -- nao ha corpo pra matar.
	/// </summary>
	public const bool InimigoComumLigado = false;

	/// <summary>O motivo, pra quem ler o console em vez do codigo.</summary>
	public const string MotivoDoInimigoDesligado =
		"o dono vai rever esta parte antes de os inimigos comuns voltarem a nascer";

	/// <summary>
	/// **O RECORTE.** Cidadao sim, chefe de saga sim, inimigo comum nao.
	///
	/// Todo caminho de nascimento passa por aqui -- o povoamento, o admin e a bancada --, e e o que
	/// faz o recorte ser uma REGRA e nao uma disciplina de quem escreve o proximo chamador.
	/// </summary>
	public static bool PodeNascer(TipoDeNpc tipo) => tipo != TipoDeNpc.Inimigo || InimigoComumLigado;

	/// <summary>Por que este molde nao nasce. Vazio = nasce. Existe pra o console dizer o motivo.</summary>
	public static string PorQueNaoNasce(TipoDeNpc tipo) =>
		PodeNascer(tipo) ? "" : $"inimigo comum esta DESLIGADO ({MotivoDoInimigoDesligado})";

	// =====================================================================
	// OS TETOS -- medidos, e nao escolhidos
	// =====================================================================

	/// <summary>
	/// QUANTOS CORPOS SEM DONO CABEM NUMA ZONA.
	///
	/// ============================ O TETO NAO E A IA -- E O FIO ============================
	/// A medicao do mapeamento desta sessao ja tinha dito que a IA nao e o teto: `Cerebro.Pensar`
	/// custa 0,03 us por corpo (1000 corpos = 0,034 ms, 0,1% de um tique de 33,3 ms). E a varredura
	/// de alvo O(n^2) tambem deixou de ser o teto para CIDADAO, porque cidadao nao varre nada: ele
	/// so conhece quem bateu nele (ver `PresaDoCidadao` no servidor).
	///
	/// O que sobra e o SNAPSHOT, e ele e linear na lotacao da zona e paga por JOGADOR: cada corpo
	/// custa ~16 bytes por tique, 30 vezes por segundo. 80 corpos numa zona sao ~38 KB/s de NPC no
	/// canal de cada pessoa que estiver la -- e isso ANTES dos jogadores. 200 seriam ~96 KB/s, que
	/// e mais banda do que o jogo inteiro usa hoje.
	///
	/// 80 e o numero, e ele REJEITA DE VERDADE: o plano de producao pede 40 na Terra, e a bancada
	/// aperta o teto contra o codigo de PRODUCAO (o parametro existe por isso, como o `teto` do
	/// <see cref="Races.Bercos.ServeDeBerco"/>) pra provar que ele dispara. Corolario 0.7: um teto
	/// que nunca e atingido e indistinguivel de teto nenhum.
	/// ================================================================================
	/// </summary>
	public const int MaxPorZona = 80;

	/// <summary>
	/// QUANTOS NO SERVIDOR INTEIRO. `TickCombate` e `TickDaForma` varrem `_players` a 30 Hz, e essa
	/// varredura NAO e por zona: um planeta cheio custa nos tiques de todo mundo, inclusive de quem
	/// esta em outro mapa.
	///
	/// 300 cabe no plano de producao (148) com folga de dobrar, e continua sendo um teto que a
	/// bancada faz disparar.
	/// </summary>
	public const int MaxNoServidor = 300;

	/// <summary>
	/// QUANTOS NASCEM POR TIQUE. Sortear uma ficha custa 0,16 a 0,29 ms (medido no mapeamento:
	/// `soldado_saiyajin` 0,199 ms, `guerreiro_namekuseijin` 0,294 ms) -- e isso ANTES do
	/// `PorNoMundo`, que faz `Statify` e `PrepararCombate`.
	///
	/// Povoar 148 corpos de uma vez seriam ~30 ms num tique so: o servidor inteiro parado por um
	/// quadro por causa de uma manutencao que ninguem pediu. E exatamente o que o `sleep(1)` entre
	/// cada NPC do original evita (PlanetPopulation.dm:431), e a regra 0.4 diz a mesma coisa com
	/// outras palavras.
	///
	/// UM por tique = 30 por segundo = o mundo inteiro povoado em 5 s, ao preco de 0,3 ms num
	/// tique de 33,3 ms (1%).
	/// </summary>
	public const int NascimentosPorTique = 1;

	/// <summary>
	/// DE QUANTO EM QUANTO TEMPO A MANUTENCAO COMPLETA A POPULACAO, em segundos.
	/// **LITERAL**: `sleep(3000)` do `Population_Maintenance_Loop` (PlanetPopulation.dm:467) --
	/// 3000 decimos = 300 s. Ver a nota de unidade: `sleep(N)` do BYOND e N/10 s.
	/// </summary>
	public const double SegundosEntreManutencoes = 300;

	/// <summary>
	/// QUANTO TEMPO UM CIDADAO CONTINUA BRIGANDO DEPOIS DO ULTIMO GOLPE, em segundos.
	/// **LITERAL**: `combat_tag_duration = 900` decimos (UpdateFightingList.dm:8) = 90 s.
	///
	/// E o prazo que transforma "apanhei" num ESTADO: sem ele o cidadao revidaria um tique e
	/// esqueceria no seguinte, porque a agressao e um evento e o alvo precisa durar.
	/// </summary>
	public const double SegundosDeRancor = 90;

	// =====================================================================
	// O EMBARALHO: o passeio que ninguem viu acontecer
	// =====================================================================

	/// <summary>
	/// QUANTO TEMPO UM PLANETA VAZIO PRECISA FICAR VAZIO ANTES DE OS HABITANTES "TEREM ANDADO".
	/// **DADO DO DONO, literal**: *"ao SAIR de um planeta e passar UNS 5 MINUTOS, todos os npcs do
	/// mapa sao colocados em POSICOES ALEATORIAS PROXIMA DA ONDE ELES ESTAVAM, pra parecer q eles
	/// andaram"*.
	///
	/// Bate com <see cref="SegundosEntreManutencoes"/> por coincidencia util e nao por dependencia:
	/// os dois sao 300 s, e um planeta esquecido acorda com a populacao completada E com todo mundo
	/// noutro canto -- que e exatamente a impressao de "a vida seguiu sem mim".
	/// </summary>
	public const double SegundosAteOEmbaralho = 300;

	/// <summary>
	/// ATE ONDE UM CORPO "ANDOU" NO EMBARALHO, em tiles, e por que sao dois numeros.
	///
	/// ============================ O PISO E O QUE FAZ A COISA APARECER ============================
	/// Sem anel morto no meio, metade dos sorteios cai a um tile de onde o corpo ja estava -- e um
	/// tile e indistinguivel de nao ter mexido. O piso e o mesmo gesto do `PontoPertoDaBandeira`
	/// (o `get_dist >= 2` do `conq_spawn_turf_near`), pela mesma razao: o sorteio precisa RECUSAR o
	/// meio, senao o comportamento existe no codigo e nao no jogo.
	///
	/// O TETO E A CIDADE. O passeio de verdade da ~1 tile a cada 20 s (`PasseioDeHabitante`), entao
	/// 5 min de caminhada honesta sao ~15 tiles de trajeto e MUITO menos de deslocamento liquido
	/// (passeio aleatorio nao anda em linha reta). 10 tiles ficam dentro do que uma ausencia curta
	/// justifica, e -- o que importa mais -- dentro do quadrado de 24 tiles em que o povoamento
	/// espalhou os habitantes (<c>PontoDeHabitante</c>): mais que isso e o vilarejo se dissolvendo
	/// no mapa a cada visita, que e o defeito que o proprio passeio ja teve uma vez (2400 px em 20 s).
	/// ======================================================================================
	/// </summary>
	public const int TilesMinDoEmbaralho = 3;

	/// <inheritdoc cref="TilesMinDoEmbaralho"/>
	public const int TilesMaxDoEmbaralho = 10;

	/// <summary>
	/// QUANTOS PONTOS O EMBARALHO SORTEIA ANTES DE DESISTIR DE UM CORPO.
	///
	/// ============================ DESISTIR E UMA RESPOSTA, E E A CERTA ============================
	/// **Palavra do dono**: *"Se o sorteio nao achar lugar livre em N tentativas, o NPC fica onde
	/// estava -- e melhor nao ter andado que ter afundado."*
	///
	/// E ela nao e o mesmo que "empurra pro chao livre mais proximo", que e o que o
	/// <see cref="World.ZoneCollision.PontoLivrePerto"/> faz e o que este embaralho fazia. Aquele
	/// funil e o certo pra um NASCIMENTO (o corpo tem que aparecer em algum lugar, e a alternativa e
	/// nao existir); aqui ele resolve o caso facil e ERRA o dificil de dois jeitos ao mesmo tempo:
	///
	///   * ele nunca desiste -- varre 64 aneis e, num corpo cercado de rocha, devolve um ponto a
	///     TRINTA tiles. O dono pediu "perto de onde estavam" e ganharia um teleporte;
	///   * a busca por aneis e DIRECIONAL: o primeiro chao livre em volta de um bolsao de pedra e
	///     sempre pro mesmo lado, entao os habitantes daquele bolsao sairiam todos na mesma direcao.
	///
	/// Sortear N pontos e ficar com o primeiro que serve nao tem nenhum dos dois vicios: todo ponto
	/// candidato ja nasce dentro do raio que o dono pediu, e a distribuicao e a do sorteio.
	///
	/// OITO, e o numero tem uma conta atras dele: o anel util (3..10 tiles nos dois eixos, com sinal)
	/// tem 4x8x8 = 256 celulas. Com metade delas livres, oito tentativas erram todas com chance 1/256;
	/// com so um QUARTO livre, 10%. Abaixo disso -- um corpo praticamente emparedado -- ficar onde
	/// estava e o comportamento desejado e nao uma falha.
	/// ========================================================================================
	/// </summary>
	public const int TentativasDoEmbaralho = 8;

	// =====================================================================
	// O PLANO, LIDO DO `npcs.json`
	// =====================================================================
	/// <summary>
	/// LE A SECAO `povoamento` do arquivo. Ausente = plano vazio (nenhum cidadao nasce), e nao um
	/// plano embutido no codigo: um padrao escondido faria o dono apagar a secao e continuar vendo
	/// NPCs, sem entender de onde vem.
	/// </summary>
	public static LinhaDePovoamento[] Ler(JsonDocument doc)
	{
		if (!doc.RootElement.TryGetProperty("povoamento", out JsonElement lista)
			|| lista.ValueKind != JsonValueKind.Array) return [];

		var l = new List<LinhaDePovoamento>();
		foreach (JsonElement e in lista.EnumerateArray())
		{
			var linha = new LinhaDePovoamento
			{
				Planeta = e.TryGetProperty("planeta", out JsonElement p) ? p.GetString() ?? "" : "",
				Molde = e.TryGetProperty("molde", out JsonElement m) ? m.GetString() ?? "" : "",
				Quantos = e.TryGetProperty("quantos", out JsonElement q) && q.ValueKind == JsonValueKind.Number
					? q.GetInt32() : 0,
			};
			if (linha.Planeta.Length > 0 && linha.Molde.Length > 0) l.Add(linha);
		}
		return [.. l];
	}

	/// <summary>
	/// O QUE HA DE CONTRADITORIO NO PLANO. Mesma disciplina do <see cref="MoldeDeNpc.Problemas"/>:
	/// o arquivo e editado a mao e um plano incoerente falha CALADO -- o planeta simplesmente fica
	/// vazio e ninguem sabe por que.
	/// </summary>
	public static List<string> Problemas(LinhaDePovoamento[] plano, CatalogoDeMoldes moldes)
	{
		var p = new List<string>();
		foreach (LinhaDePovoamento l in plano)
		{
			MoldeDeNpc? m = moldes.Get(l.Molde);
			if (m == null) { p.Add($"povoamento de '{l.Planeta}': molde '{l.Molde}' nao existe"); continue; }

			if (l.Quantos <= 0)
				p.Add($"povoamento de '{l.Planeta}' com molde '{l.Molde}': 'quantos' e {l.Quantos}");

			if (l.Quantos > MaxPorZona)
				p.Add($"povoamento de '{l.Planeta}' pede {l.Quantos}, acima do teto por zona ({MaxPorZona})");

			// O PLANO E DE HABITANTES. Chefe de saga NAO entra aqui: quem o poe no mundo e a cadeia
			// de sagas (o gatilho por BP e a contagem em dias), e nao uma cota por planeta. Uma linha
			// de povoamento com molde de chefe faria nascer um Freeza permanente na Terra.
			if (m.Tipo != TipoDeNpc.Cidadao)
				p.Add($"povoamento de '{l.Planeta}': o molde '{l.Molde}' e do tipo '{m.Tipo}'. "
					+ "O plano por planeta e de CIDADAOS -- chefe entra pela cadeia de sagas e "
					+ "inimigo comum esta desligado.");
		}

		int total = plano.Sum(l => l.Quantos);
		if (total > MaxNoServidor)
			p.Add($"o plano inteiro pede {total} cidadaos, acima do teto do servidor ({MaxNoServidor})");

		return p;
	}
}
