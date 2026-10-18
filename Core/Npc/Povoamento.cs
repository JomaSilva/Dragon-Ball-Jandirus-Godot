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

	/// <summary>
	/// (v0.3.0) SO COM ESTE TRONO VAGO: a chave de um cargo (`"kov"`, o Rei de Vegeta) -- a linha so nasce enquanto nenhum
	/// jogador o carrega. Vazio = sempre. E o `if(!King_of_Vegeta ...)` do `populate_vegeta` (PlanetPopulation.dm:435); ver
	/// <see cref="Povoamento.LinhaVale"/> e `GameServer.ReiDeVegeta.cs`.
	/// </summary>
	public string TronoVago = "";

	/// <summary>
	/// (2026-10-16) CADA DONO DESTE CARGO TIRA UM: a chave de um cargo de varias vagas (`"kaioshin"`) -- o alvo da linha cai um por
	/// jogador que o carrega, e quem sobra do alvo SOME (`GameServer.AparaOPovoDoCargo`). O dono: *"cada player no cargo faz 1 npc
	/// sumir desses 5"*. Vazio = o alvo e o <see cref="Quantos"/>. Ver <see cref="Povoamento.AlvoDaLinha"/>.
	/// </summary>
	public string CadaDonoTiraUm = "";
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

	/// <summary>
	/// (v0.3.0) ESTA LINHA VALE AGORA? A que pede um trono vago (<see cref="LinhaDePovoamento.TronoVago"/>) so vale enquanto
	/// `tronoVago(cargo)` responde sim -- quem responde e o servidor, que sabe quem esta em cada trono. As outras valem
	/// sempre. O Rei Vegeta e o Principe sao as duas linhas de hoje (`GameServer.ReiDeVegeta.cs`).
	/// </summary>
	public static bool LinhaVale(LinhaDePovoamento linha, Func<string, bool> tronoVago) =>
		linha.TronoVago.Length == 0 || TronoIgnoradoDeTeste || tronoVago(linha.TronoVago);

	/// <summary>DEFEITO INJETADO (`--v030teste`): o trono e ignorado -- o Rei NPC nasceria com um jogador no trono (dois reis).</summary>
	public static bool TronoIgnoradoDeTeste;

	/// <summary>
	/// (2026-10-16) O ALVO DESTA LINHA AGORA: o `Quantos`, menos um por dono do cargo de <see cref="LinhaDePovoamento.CadaDonoTiraUm"/>
	/// -- quem conta os donos e o servidor (`donos`). Nunca abaixo de zero. Os cinco Supremos Kaiohs do Ceu sao a linha de hoje.
	/// </summary>
	public static int AlvoDaLinha(LinhaDePovoamento linha, Func<string, int> donos) =>
		linha.CadaDonoTiraUm.Length == 0 || DonosIgnoradosDeTeste ? linha.Quantos
		: Math.Max(0, linha.Quantos - donos(linha.CadaDonoTiraUm));

	/// <summary>DEFEITO INJETADO (`--kaioshinteste`): os donos do cargo nao tiram ninguem -- os cinco ficam com o cargo cheio.</summary>
	public static bool DonosIgnoradosDeTeste;

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
	/// (v0.3.0) E FOI O FIO QUE MUDOU -- e nao o teto que foi esticado no chute. O dono pediu *"aumentar a quantidade
	/// de npcs consideravelmente, mas cuidado com a performance do server"*, e o habitante longe de todo jogador (o
	/// mesmo `LongeDeTodos` que ja faz a mente dele pensar um tique a cada dez) passou a ir no snapshot SO no tique em
	/// que pensa (`GameServer.CorposDoSnapshot`): o fio paga a vizinhanca de quem esta jogando, e nao o planeta
	/// inteiro. O plano da Terra foi a 100; 150 deixa a fauna e as ondas de invasao cabendo em cima dele.
	///
	/// 150 e o numero, e ele REJEITA DE VERDADE: a bancada aperta o teto contra o codigo de PRODUCAO (o parametro
	/// existe por isso, como o `teto` do <see cref="Races.Bercos.ServeDeBerco"/>) pra provar que ele dispara.
	/// Corolario 0.7: um teto que nunca e atingido e indistinguivel de teto nenhum.
	/// ================================================================================
	/// </summary>
	public const int MaxPorZona = 150;

	/// <summary>
	/// QUANTOS NO SERVIDOR INTEIRO. `TickCombate` e `TickDaForma` varrem `_players` a 30 Hz, e essa
	/// varredura NAO e por zona: um planeta cheio custa nos tiques de todo mundo, inclusive de quem
	/// esta em outro mapa.
	///
	/// (v0.3.0) 600: o plano de producao foi de 150 a 372 corpos (o pedido do dono de mais gente nos planetas
	/// civilizados), e o teto continua com folga pra fauna, invasao e torneio -- e continua sendo um teto que a bancada
	/// faz disparar. O preco e linear e e medido: os dez relogios do corpo custavam 0,717 ms por tique com 148 corpos e
	/// ninguem online (`GameServer.RelogiosDoCorpo.cs`), e a `--embaralhoteste` (familias 5 e 13) mede o tique do mundo
	/// cheio de hoje e reprova acima de 25% do orcamento.
	/// </summary>
	public const int MaxNoServidor = 600;

	/// <summary>
	/// QUANTOS NASCEM POR TIQUE. Sortear uma ficha custa 0,16 a 0,29 ms (medido no mapeamento:
	/// `soldado_saiyajin` 0,199 ms, `guerreiro_namekuseijin` 0,294 ms) -- e isso ANTES do
	/// `PorNoMundo`, que faz `Statify` e `PrepararCombate`.
	///
	/// Povoar os 372 corpos do plano de uma vez seriam ~80 ms num tique so: o servidor inteiro parado por
	/// alguns quadros por causa de uma manutencao que ninguem pediu. E exatamente o que o `sleep(1)` entre
	/// cada NPC do original evita (PlanetPopulation.dm:431), e a regra 0.4 diz a mesma coisa com
	/// outras palavras.
	///
	/// UM por tique = 30 por segundo = o mundo inteiro povoado em ~12 s, ao preco de 0,3 ms num
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
	/// QUANTOS TILES UM HABITANTE "ANDA" POR MINUTO DE PLANETA VAZIO -- o raio do embaralho cresce com a ausencia.
	///
	/// ============================ O PEDIDO VIROU UMA PROPORCAO (v0.3.0) ============================
	/// O primeiro pedido era um PRAZO: *"ao SAIR de um planeta e passar UNS 5 MINUTOS, todos os npcs do mapa sao
	/// colocados em POSICOES ALEATORIAS PROXIMA DA ONDE ELES ESTAVAM, pra parecer q eles andaram"*. O de 2026-10-15 o
	/// trocou por uma conta: *"o embaralhamento vai ser baseado no tempo q o planeta ficou sem player, pro cara n sair
	/// e voltar em 1 segundo e o npc parar no outro lado do planeta"*. Entao o quanto cada um andou e o TEMPO VAZIO
	/// vezes esta velocidade (<see cref="RaioDoEmbaralho"/>), e os 5 minutos de antes viraram um ponto da reta: dois
	/// tiles por minuto dao, aos 5 minutos, os mesmos 10 tiles que o embaralho antigo dava.
	///
	/// POR QUE DOIS: a caminhada da vida diaria anda bem mais que isso em TRAJETO, e trajeto nao e deslocamento --
	/// quem passeia a esmo volta sobre os proprios passos. Dois tiles por minuto e o deslocamento LIQUIDO de quem
	/// passeou, e nao o odometro dele.
	/// ======================================================================================
	/// </summary>
	public const double TilesPorMinutoDoEmbaralho = 2;

	/// <summary>
	/// O MENOR PASSO DO EMBARALHO, em tiles -- o anel morto do sorteio --, e por isso ele tambem decide o prazo.
	///
	/// ============================ O PISO E O QUE FAZ A COISA APARECER ============================
	/// Sem anel morto no meio, metade dos sorteios cai a um tile de onde o corpo ja estava -- e um
	/// tile e indistinguivel de nao ter mexido. O piso e o mesmo gesto do `PontoPertoDaBandeira`
	/// (o `get_dist >= 2` do `conq_spawn_turf_near`), pela mesma razao: o sorteio precisa RECUSAR o
	/// meio, senao o comportamento existe no codigo e nao no jogo.
	///
	/// E UM RAIO MENOR QUE O PISO NAO TEM ONDE POR NINGUEM: abaixo dele ninguem anda, e o tempo em que o raio o
	/// alcanca e o <see cref="SegundosAteOEmbaralho"/>.
	/// ======================================================================================
	/// </summary>
	public const int TilesMinDoEmbaralho = 3;

	/// <summary>
	/// O TETO DO RAIO, em tiles: o quanto uma ausencia LONGA leva -- 64, alcancados aos 32 minutos.
	///
	/// ============================ O TETO ANTIGO (10) PROTEGIA UM VILAREJO QUE NAO EXISTE MAIS ============================
	/// Ele nasceu quando o povoamento espalhava os habitantes num quadrado de 24 tiles, e mais que 10 era "o vilarejo se
	/// dissolvendo no mapa a cada visita". Hoje o povoamento espalha por AREA no planeta inteiro (`Core/Npc/Habitat.cs`,
	/// regioes de 32 tiles): 64 tiles sao duas regioes -- trocar de bairro, e nao de planeta.
	///
	/// E O TETO CONTINUA EXISTINDO: sem ele uma ausencia de um dia viraria um sorteio de lugar novo no planeta inteiro, e
	/// apagaria a continuidade ("ele estava perto do lago, agora esta do outro lado da montanha") que e o motivo do
	/// embaralho existir.
	/// =================================================================================================================
	/// </summary>
	public const int TilesMaxDoEmbaralho = 64;

	/// <summary>
	/// O PRAZO MINIMO: quanto tempo o planeta precisa ficar vazio pra alguem ter andado. DERIVADO, e nao escolhido: e o
	/// tempo em que o raio alcanca o anel morto (3 tiles a 2 por minuto = 90 s). Quem sai e volta antes disso encontra
	/// todo mundo onde deixou -- o *"sair e voltar em 1 segundo"* do dono.
	/// </summary>
	public const double SegundosAteOEmbaralho = TilesMinDoEmbaralho * 60.0 / TilesPorMinutoDoEmbaralho;

	/// <summary>
	/// O RAIO DO EMBARALHO, em tiles por eixo, pra um planeta que ficou <paramref name="segundosVazio"/> sem jogador:
	/// zero abaixo do prazo (ninguem anda), o tempo vezes a velocidade acima dele, e nunca mais que o teto. Funcao pura
	/// -- o servidor e as bancadas perguntam a mesma.
	/// </summary>
	public static int RaioDoEmbaralho(double segundosVazio)
	{
		if (segundosVazio < SegundosAteOEmbaralho) return 0;
		if (RaioFixoDeTeste) return TilesMaxDoEmbaralho;
		return (int)Math.Min(TilesMaxDoEmbaralho, Math.Floor(segundosVazio / 60.0 * TilesPorMinutoDoEmbaralho));
	}

	/// <summary>
	/// DEFEITO INJETADO (`--embaralhoteste`, `--v030teste`): o raio ignora o tempo -- vencido o prazo, toda ausencia leva
	/// o teto, e quem saiu por dois minutos acha a cidade espalhada pelo bairro todo. Falso em jogo, sempre.
	/// </summary>
	public static bool RaioFixoDeTeste;

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
	/// OITO, e o numero tem uma conta atras dele: o anel util (3..R tiles nos dois eixos, com sinal)
	/// tem 4x(R-2)^2 celulas -- 256 com o raio dos 5 minutos (R = 10), e mais com raio maior. Com metade
	/// delas livres, oito tentativas erram todas com chance 1/256; com so um QUARTO livre, 10%. Abaixo
	/// disso -- um corpo praticamente emparedado -- ficar onde estava e o comportamento desejado e nao uma falha.
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
				TronoVago = e.TryGetProperty("tronoVago", out JsonElement tv) ? tv.GetString() ?? "" : "",
				CadaDonoTiraUm = e.TryGetProperty("cadaDonoTiraUm", out JsonElement cd) ? cd.GetString() ?? "" : "",
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
