using Jandirus.Core.World;

namespace Jandirus.Core.Races;

/// <summary>POR QUE este corpo nasceu ONDE nasceu. Vai pro chat da criacao e pra bancada.</summary>
public enum MotivoDoBerco : byte
{
	/// <summary>A regra geral: cada raca no planeta dela.</summary>
	PlanetaNatal = 0,

	/// <summary>O jogador pediu pra nascer perto de casa, e nasceu numa orbita irma.</summary>
	VizinhoDoNatal = 1,

	/// <summary>
	/// O Lendario. Nao e escolha dele nem do jogador -- ver <see cref="Bercos.Exilio"/>.
	/// </summary>
	ExilioDoLendario = 2,
}

/// <summary>
/// ONDE UM CORPO NASCE. E o resultado de <see cref="Bercos.Onde"/>, e ele e um ENDERECO.
///
/// ============================ O NOME NAO E CHAVE, E O ENDERECO E ============================
/// A especificacao (secao 11) manda a escolha guardar a SEED e nao o nome, e a medicao da fase 1
/// mostrou que nem a seed sozinha basta: <see cref="ZoneKey.Hash"/> come o NOME junto com a seed,
/// entao reconstruir a zona exige os dois. E pior -- os nomes gerados COLIDEM: o padrao
/// `$"{bioma}-{|Sx|%1000}{|Sy|%1000}{k}"` de `SistemaSolar.Planeta` PERDE O SINAL da celula, e a
/// varredura da fase 1 achou "Deserto-120" saindo de `[1:-2]k0` e de `[1:2]k0` na mesma passada.
///
/// Por isso o que manda aqui e a TRINCA <see cref="Sx"/>, <see cref="Sy"/>, <see cref="K"/>: ela
/// e a coordenada do corpo no universo, e `Sistemas.Do(seed,Sx,Sy).Planeta(K)` devolve nome, seed,
/// posicao e raio de uma vez. Nome e seed sao CACHE do que o endereco ja diz -- quem persistir isto
/// persiste o endereco, e nao o rotulo.
/// ==========================================================================================
/// </summary>
public readonly record struct Berco
{
	/// <summary>Nome da zona. DERIVADO do endereco -- ver o cabecalho.</summary>
	public string Planeta { get; init; }

	/// <summary>Seed da superficie. Tambem derivada; e o que o gerador de terreno consome.</summary>
	public ulong Seed { get; init; }

	/// <summary>Mundo com mapa feito a mao (Terra, Vegeta...) ou mundo sorteado.</summary>
	public bool PreFeito { get; init; }

	/// <summary>Celula de sistema solar. So vale quando <see cref="NoMapaDoUniverso"/>.</summary>
	public int Sx { get; init; }
	public int Sy { get; init; }

	/// <summary>
	/// Orbita dentro do sistema. **-1 marca "este lugar nao fica no mapa do universo"** -- e o
	/// caso do Paraiso e do Inferno, que no DM sao `area/afterlifeareas` e nao corpos celestes.
	/// </summary>
	public int K { get; init; }

	/// <summary>Posicao no espaco. So vale quando <see cref="NoMapaDoUniverso"/>.</summary>
	public Vec2 Pos { get; init; }

	/// <summary>Raio do disco. So vale quando <see cref="NoMapaDoUniverso"/>.</summary>
	public float Raio { get; init; }

	/// <summary>
	/// Gravidade do mundo gerado. **Zero nos pre-feitos**, e de proposito: a gravidade deles mora
	/// no `planetas.json`, e inventar um numero aqui criaria uma segunda fonte pra divergir. Mesmo
	/// contrato do `GameServer.GravidadeDaZonaGerada`, que tambem devolve 0 pra zona nao gerada.
	/// </summary>
	public double Gravidade { get; init; }

	/// <summary>
	/// O planeta natal DA RACA -- de onde este berco foi medido. Continua valendo mesmo quando o
	/// corpo nasce longe: e o que a ficha e a dica de classe usam pra contar de onde ele veio.
	/// </summary>
	public string Natal { get; init; }

	public MotivoDoBerco Motivo { get; init; }

	/// <summary>
	/// O Saiyajin de classe baixa que a sociedade despachou. O <see cref="Natal"/> dele ja vem
	/// REESCRITO pra Terra -- ver <see cref="Bercos.Onde"/>.
	/// </summary>
	public bool Despejado { get; init; }

	/// <summary>Quantas celulas o exilio teve que olhar. E o que a bancada afirma (regra 0.7).</summary>
	public int CelulasOlhadas { get; init; }

	/// <summary>
	/// O exilio caiu no ULTIMO RECURSO (ver <see cref="Bercos.Exilio"/>). Tem que ser mentira em
	/// todo sorteio normal, e a bancada precisa conseguir faze-lo virar verdade -- guarda que nunca
	/// roda e guarda que nao existe.
	/// </summary>
	public bool UltimoRecurso { get; init; }

	/// <summary>Este berco e um corpo que a carta estelar mostra e de onde da pra decolar?</summary>
	public bool NoMapaDoUniverso => K >= 0;

	/// <summary>A zona pra onde o corpo vai. Um pre-feito nao carrega seed no `ZoneKey`.</summary>
	public ZoneKey Zona => PreFeito ? ZoneKey.Premade(Planeta) : ZoneKey.Procedural(Planeta, Seed);

	/// <summary>
	/// O CORPO NO ESPACO, pronto pro `PousarEmProcedural`. Nulo quando o berco nao fica no mapa do
	/// universo -- e NULO em vez de uma posicao falsa porque devolver (0,0) ali seria devolver a
	/// Terra, e um lugar que mente sobre onde fica e pior que um lugar que nao responde.
	/// </summary>
	public PlanetaNoEspaco? NoEspaco() => K < 0 ? null : new PlanetaNoEspaco
	{
		Nome = Planeta,
		Pos = Pos,
		Raio = Raio,
		Seed = Seed,
		Premade = PreFeito,
	};
}

/// <summary>
/// A REGRA DO BERCO -- funcao PURA de (raca, classe, linhagem, seed do personagem) -> planeta.
///
/// ============================ POR QUE E PURA, E POR QUE MORA NO CORE ============================
/// Cliente e servidor tem que chegar no MESMO planeta sem trocar pacote (regra 0.2). A tela de
/// criacao precisa dizer "voce vai nascer em Gelido-14112" ANTES de existir personagem, e o servidor
/// precisa chegar no mesmo mundo depois -- inclusive no RENASCIMENTO, meses depois, com o processo
/// reiniciado. Nao ha `Random.Shared`, nao ha `DateTime`, nao ha cache: os mesmos quatro argumentos
/// devolvem sempre o mesmo endereco.
/// ==============================================================================================
///
/// ============================ AS TRES REGRAS DO DONO ============================
/// 1. Cada raca no seu devido planeta.                                     -> <see cref="PlanetaNatal"/>
/// 2. O Saiyajin de CLASSE BAIXA nasce na Terra (ou perto dela).           -> <see cref="Onde"/>
/// 3. O LENDARIO e mandado pra um planeta aleatorio, sempre.               -> <see cref="Exilio"/>
///
/// As duas ultimas CONTRADIZEM a primeira de proposito, e e nisso que esta a historia: o
/// classe-baixa nao nasce em Vegeta porque foi mandado embora, e o Lendario nao nasce em lugar
/// nenhum previsivel porque a sociedade Saiyajin tinha MEDO dele. O berco nao e so a raca -- e a
/// raca, a classe e a linhagem, e a excecao carrega o motivo.
/// ==============================================================================================
///
/// ============================ O DM JA QUIS ISTO, E ERROU POR UM DETALHE ============================
/// O mapa do BYOND tem 14 `/obj/SpawnPoint` com `spawnRace`/`spawnPlanet` (`Maps/1to26.dmm`), e o
/// motor casa spawnpoint com jogador pelo **spawnPlanet**, nao pela raca (`SpawnPoints.dm:77-85`).
/// Como a criacao grava o planeta do MENU (`CreationUI.dm:400`), o Frost Demon sai com
/// `spawnPlanet = "Vegeta"` e NUNCA alcanca o spawnpoint de "Icer Planet" que existe pra ele; o
/// mesmo vale pro Meta e a "Big Gete Star". Ou seja: a intencao "cada raca no seu planeta" esta
/// escrita no mapa do original e a criacao a atropela.
///
/// Aqui a fonte e a RACA e o planeta e CONSEQUENCIA -- e por isso este arquivo conserta os dois
/// casos de graca, sem uma linha falando de Frost Demon.
/// ==============================================================================================
/// </summary>
public static class Bercos
{
	// =====================================================================
	// 1. A TABELA: CADA RACA NO SEU PLANETA
	// =====================================================================
	/// <summary>
	/// O PLANETA NATAL DE CADA RACA.
	///
	/// Autoridade, em ordem: o `spawnRace` dos `/obj/SpawnPoint` do mapa do DM quando existe (e ele
	/// que MOVE o corpo la), o menu de criacao (`CreationUI.dm:284-329`) quando nao existe.
	///
	/// ============================ SO SAI DAQUI LUGAR QUE EXISTE ============================
	/// Todo nome devolvido aqui e uma zona do `Assets/Maps/manifest.json` -- ou seja, um mapa
	/// convertido, com colisao e chao. E todos menos dois estao tambem em `Espaco.PreFeitos()`, o
	/// que garante que da pra DECOLAR de la (`GameServer.Espaco.Decolar` recusa lugar que nao fica
	/// no mapa do universo) e que a carta estelar os desenha.
	///
	/// Os dois que ficam fora da carta sao "Heaven" e "Hell", e ficam de proposito: no DM eles sao
	/// `area/afterlifeareas`, nao corpos celestes -- de la se sai renascendo, nao de nave. Ver
	/// <see cref="Berco.K"/>.
	/// ====================================================================================
	///
	/// ============================ DUAS RACAS ESTAO NO PLANETA ERRADO, E EU SEI ============================
	/// HERAN e META. O DM diz o lar dos dois com todas as letras -- o Heran e "from the planet Hera"
	/// (`statheran.dm:2`) e o Meta tem spawnpoint proprio na "Big Gete Star" -- e os DOIS mapas
	/// existem aqui (`z11_Hera`, `z19_Big_Geti_Star`). O que falta e eles serem CORPOS: nenhum dos
	/// dois esta em `Espaco.PreFeitos()`, entao um recem-nascido posto la nao teria como sair --
	/// `Decolar` responde "este lugar nao fica no mapa do universo" e ele ficaria preso pra sempre.
	///
	/// Ficam em Vegeta (o planeta do menu de criacao do DM) ate que alguem os ponha na carta. E uma
	/// LINHA em `Espaco.PreFeitos()` pra cada, mais duas linhas aqui -- mas e uma mudanca que ancora
	/// dois sistemas novos e portanto MUDA A ASSINATURA DO UNIVERSO, o que nao cabe na regra do
	/// berco. Perde-se a fidelidade ao lar das duas racas; ganha-se que ninguem nasce numa prisao.
	/// ==================================================================================================
	/// </summary>
	public static string PlanetaNatal(string raca) => raca switch
	{
		// --- Terra: spawnpoint "(Human)" sem `spawnRace`, planeta default "Earth" (SpawnPoints.dm:48)
		"Human" or "Shapeshifter" or "Majin" => "Earth",

		// Demigod tem spawnpoint no "Afterlife", que nao e corpo celeste; o menu o poe na Terra, no
		// Paraiso e no Inferno. Semideus anda entre mortais -- fica na Terra, que e a unica das tres
		// de onde ele consegue ir a qualquer lugar.
		"Demigod" => "Earth",

		// O Alien e a raca SEM lar: os cinco spawnpoints dele no DM sao Deserto, Arlia,
		// Interdimensao e as duas estacoes -- lugares de PASSAGEM, e nenhum deles alcancavel pelo
		// menu de criacao. Ele comeca no porto mais movimentado que existe. Se ele quiser outro
		// lugar, e exatamente pra ele que a opcao "nascer perto de casa" foi construida.
		"Alien" => "Earth",

		// Meio-Saiyajin nao se escolhe na criacao (CreationUI.dm:305-309): ele nasce de gravidez ou
		// de ovo, e nesse caminho o filho HERDA o `spawnPlanet` do pai/mae (SpawnPoints.dm:141) --
		// essa heranca vence esta tabela. Aqui so respondem os moldes de NPC e o admin.
		"Halfbreed" => "Earth",

		// Android/Bio-Android/Spirit Doll nascem na COORDENADA DE QUEM OS CRIOU (SpawnPoints.dm:149-168),
		// sem planeta nenhum. Respondem Terra so pra nao devolver vazio; quem os cria manda.
		"Android" or "BioAndroid" or "SpiritDoll" => "Earth",

		// --- Vegeta: spawnpoint "(Saiyan)". Tsujin cai junto e a gravidade confirma -- o
		// `Birth.GravidadeNatal` da 10 pros dois com o comentario "nativos de Vegeta", e Vegeta pesa
		// 10 no planetas.json.
		"Saiyan" or "Tsujin" or "Saibaman" => "Vegeta",
		"Heran" or "Meta" => "Vegeta",   // ver o aviso do cabecalho

		// --- Namek: spawnpoint "(Namek)". Gray, Kanassa e Yardrat nao tem mundo convertido; ficam
		// no planeta do menu de criacao deles.
		"Namekian" or "Gray" or "Kanassa" or "Yardrat" => "Namek",

		// --- OS TRES QUE O DM QUIS E NAO CONSEGUIU ENTREGAR.
		// O spawnpoint "(Icer)" aponta pra "Icer Planet" e o "(Arlia)" pra "Arlia"; a criacao punha
		// os dois em Vegeta e Namek, entao nenhum dos dois era alcancavel. Com a raca como fonte,
		// eles passam a funcionar sem uma linha de codigo falando deles.
		// ATENCAO: "Icer" e nome de RACA e de PLANETA ao mesmo tempo. Nao e engano de digitacao.
		"Icer" => "Icer",
		"Arlian" => "Arlia",
		"Makyo" => "Makyo_Star",

		// --- Fora da carta estelar, e de proposito (ver o cabecalho da tabela).
		"Kai" => "Heaven",
		"Demon" => "Hell",

		// Raca desconhecida cai na Terra. Nao ha "planeta nenhum": um corpo sem lugar e um corpo no
		// vazio, que e o modo de falha que esta regra existe pra impedir.
		_ => "Earth",
	};

	// =====================================================================
	// 1.b A MESMA TABELA, LIDA AO CONTRARIO -- quem NASCE aqui?
	// =====================================================================
	/// <summary>
	/// ESTA RACA POVOA UM PLANETA?
	///
	/// ============================ O CRIVO EXISTE PORQUE O RECUO MENTE ============================
	/// A <see cref="PlanetaNatal"/> nunca devolve vazio -- raca que ela nao conhece cai em "Earth",
	/// e ha um comentario dizendo por que ("um corpo sem lugar e um corpo no vazio"). Isso e certo
	/// pra a pergunta dela ("onde ponho ESTE corpo?") e e VENENO pra a pergunta inversa ("quem mora
	/// aqui?"): lida ao contrario sem crivo, a tabela entrega como habitante da Terra tudo o que ela
	/// nao soube classificar.
	/// ======================================================================================
	///
	/// Duas familias saem, e as duas com o motivo escrito no proprio DM:
	///
	///   Android / BioAndroid / SpiritDoll -- nascem na COORDENADA DE QUEM OS CRIOU
	///                                        (SpawnPoints.dm:149-168), sem planeta nenhum;
	///   Halfbreed                         -- nasce de gravidez ou de ovo e HERDA o `spawnPlanet`
	///                                        do pai/mae (SpawnPoints.dm:141), e essa heranca vence
	///                                        a tabela (CreationUI.dm:305-309: nem se escolhe);
	///   Dog                               -- **nao e uma raca do jogo**: e a "Example prototype
	///                                        race" do `Genetic_Prototype.dm:66`, um bloco de
	///                                        documentacao que o extrator leu junto com as de
	///                                        verdade e despejou no `races.json`. Ela nao aparece em
	///                                        nenhuma tela e chega na Terra pelo recuo.
	///
	/// Sem estas quatro linhas, o primeiro povoamento da Terra teria sorteado androides orfaos e
	/// cachorros, e o defeito seria plausivel o bastante pra ninguem investigar.
	/// </summary>
	public static bool PovoaUmPlaneta(string raca) =>
		raca is not ("Android" or "BioAndroid" or "SpiritDoll" or "Halfbreed" or "Dog");

	/// <summary>
	/// QUEM NASCE NESTE PLANETA -- a <see cref="PlanetaNatal"/> lida ao contrario.
	///
	/// ============================ POR QUE ISTO E DERIVADO, E NAO UMA SEGUNDA TABELA ============================
	/// O port ja tem uma tabela planeta->racas: <see cref="CharacterDraft.RacasDoPlaneta"/>, que
	/// alimenta o MENU DE CRIACAO. E ela **nao e** a inversa desta -- ela tem tres divergencias que
	/// o proprio cabecalho da <see cref="PlanetaNatal"/> ja explicava:
	///
	///   `RacasDoPlaneta("Vegeta")` inclui **Icer**, mas `PlanetaNatal("Icer") = "Icer"`;
	///   `RacasDoPlaneta("Namek")`  inclui **Arlian** e **Makyo**, cujos bercos sao Arlia e Makyo_Star.
	///
	/// A divergencia e legitima la (o menu do DM oferecia os tres nesses planetas, e o berco
	/// consertou isso *pelo lado da raca*), e seria um DEFEITO aqui: o povoamento chamava
	/// `RacasDoPlaneta` e um cidadao de Vegeta podia sair Icer -- um Frost Demon morando no planeta
	/// dos Saiyajins, pela regra errada, enquanto o jogador Frost Demon nasce em Icer pela certa.
	///
	/// Entao o pool sai DAQUI: um filtro sobre a MESMA funcao que decide o berco do jogador. Nao ha
	/// segunda tabela pra envelhecer -- acrescentar uma raca a <see cref="PlanetaNatal"/> a poe no
	/// planeta dela nos dois caminhos, no mesmo commit, sem ninguem lembrar de nada.
	/// ======================================================================================================
	///
	/// ============================ A ORDEM E ESTAVEL DE PROPOSITO ============================
	/// O resultado e ordenado por nome (Ordinal). `todasAsRacas` costuma vir de um `Dictionary`, e a
	/// ordem de enumeracao de dicionario **nao e contratual**: sem a ordenacao, o mesmo servidor com
	/// a mesma semente sortearia racas diferentes depois de um reinicio, e a promessa de determinismo
	/// do <see cref="Npc.SorteioDeNpc"/> viraria mentira de um jeito quase impossivel de reproduzir.
	/// ==================================================================================
	/// </summary>
	public static string[] RacasNascidasEm(string planeta, IEnumerable<string> todasAsRacas) =>
		[.. todasAsRacas
			.Where(r => PovoaUmPlaneta(r)
					 && string.Equals(PlanetaNatal(r), planeta, StringComparison.OrdinalIgnoreCase))
			.OrderBy(r => r, StringComparer.Ordinal)];

	// =====================================================================
	// 2. O CRIVO: O PLANETA TEM QUE EXISTIR E TEM QUE DAR PRA VIVER NELE
	// =====================================================================
	/// <summary>
	/// GRAVIDADE MAXIMA DE UM BERCO GERADO. Tres motivos, e nenhum deles e gosto:
	///
	/// 1. O AUTOR DO DM ESCREVEU O LIMITE. A escada de gravidade de `ProceduralSpace.dm:448-452`
	///    (portada literal em <see cref="MundoProcedural.GravidadeDaSeed"/>) vem com o comentario
	///    dele: *"com o esmagamento, planeta 40x+ e zona de morte"*. 40 e o teto DURO.
	///
	/// 2. O TAMANHO DO MUNDO SAI DA GRAVIDADE (<see cref="MundoProcedural.LadoDaGravidade"/>), e o
	///    custo de gerar e linear na area: 15g da um mundo de 335 tiles (~25 ms), 40g da 591 (~77 ms)
	///    e 80g da 1000 (~220 ms, sete tiques). Nascer e o unico momento em que o mundo e gerado sem
	///    ninguem ter viajado ate ele -- por o caso caro no NASCIMENTO seria por o pico onde ele nao
	///    tem como ser antecipado.
	///
	/// 3. E O QUE IMPEDE UM PRESENTE DE BERCO. O DM acostuma o corpo a gravidade do proprio berco
	///    (`race.dm:130-131`: `GravMastered = max(GravMastered, PlanetGravity(spawnPlanet))`, com o
	///    comentario "so high-grav races aren't crushed/frozen at spawn"). Isso e o que SALVA o
	///    exilado de nascer esmagado -- e tambem o que faria um berco de 80g entregar `GravMastered`
	///    80 de graca a quem ainda nao treinou um dia, quando um Saiyajin comeca com 10.
	///
	/// 15 e o teto que a escada do DM ja marca: e o fim da segunda faixa (`5 + PR.next(10)`), aceita
	/// **85% dos mundos sorteados** (55% + 30%) e cabe em 1,5x a gravidade de Vegeta. Ele REJEITA de
	/// verdade -- a bancada mede quantos -- e nao e um teto que nunca dispara.
	/// </summary>
	public const double GravidadeMaximaDeBerco = 15;

	/// <summary>
	/// ESTE CORPO SERVE DE BERCO?
	///
	/// O que NAO precisa ser conferido aqui e o chao: todo mundo gerado tem terreno e um ponto de
	/// pouso que o proprio gerador garante livre (`TerrenoGerado.Spawn`), e quando nao ha campo
	/// aberto o pouso ESCAVA um (`PousarEmProcedural` -> `ClareiraEscavada`). Nao existe planeta
	/// gerado sem chao; existe planeta gerado pesado demais pra um recem-nascido.
	/// </summary>
	/// <param name="teto">
	/// O teto de gravidade. Parametro e nao constante SO por causa da bancada: a regra 0.7 diz que
	/// um teto que nunca dispara e indistinguivel de teto nenhum, e a unica forma de a bancada ver
	/// os caminhos de recuo do <see cref="Exilio"/> e apertar este numero contra o codigo de
	/// PRODUCAO, em vez de escrever um caminho paralelo que testaria o atalho e nao o jogo. Em jogo
	/// ninguem passa este argumento.
	/// </param>
	public static bool ServeDeBerco(PlanetaNoEspaco p, double teto = GravidadeMaximaDeBerco) =>
		p.Premade || MundoProcedural.GravidadeDaSeed(p.Seed) <= teto;

	// =====================================================================
	// 3. A FUNCAO
	// =====================================================================
	/// <summary>
	/// ONDE ESTE CORPO NASCE. **Esta e a funcao.** Tudo o mais neste arquivo a serve.
	///
	/// `seedDoPersonagem` tem que ser SORTEADA UMA VEZ e GUARDADA. Se ela for recalculada de um
	/// campo que muda (nome, data de login), o berco anda -- e o renascimento levaria o jogador pra
	/// um planeta que nao e o dele. Ela e a unica fonte de acaso desta regra, e e por isso que nao
	/// ha `Random` nenhum aqui dentro.
	///
	/// `pediuVizinho` e a segunda parte do pedido do dono ("caso o jogador queira spawnar num
	/// planeta ALEATORIO PROXIMO ao seu planeta natal"). O Lendario NAO e perguntado.
	/// </summary>
	public static Berco Onde(ulong seedDoUniverso, string raca, string classe, string linhagem,
							 ulong seedDoPersonagem, bool pediuVizinho)
	{
		string natal = PlanetaNatal(raca);
		bool despejado = false;

		// ---- EXCECAO 3: O LENDARIO. Vem primeiro porque ela nao aceita nem a excecao 2 nem o
		// pedido do jogador: o exilio nao e uma opcao de ninguem.
		if (EhLendario(raca, classe, linhagem))
			return Exilio(seedDoUniverso, natal, seedDoPersonagem);

		// ---- EXCECAO 2: O SAIYAJIN DE CLASSE BAIXA.
		//
		// ============================ POR QUE A TERRA, E NAO "UM PLANETA PERTO DELA" ============================
		// O dono escreveu "na TERRA ou num planeta proximo a Terra", e o "ou" e resolvido do jeito
		// mais barato possivel: a excecao REESCREVE O NATAL, e nao o destino. Dai em diante o
		// classe-baixa e tratado como qualquer um -- se ele pediu vizinho, o vizinho e da estrela da
		// TERRA; se nao pediu, ele nasce na Terra. Uma regra, nenhum caso especial a jusante, e a
		// opcao "perto de casa" passa a significar a coisa certa pra ele sozinha.
		//
		// E o padrao e a TERRA, e nao um sorteado, porque a Terra e o unico mundo com cidade, banco,
		// bancada e gente. O classe-baixa ja e o comeco mais fraco que existe (Starting BP 10,
		// PhysOff 1,2 no `class_stats` do `statsaiyan.dm:125`): despeja-lo sozinho num procedural
		// vazio seria castigar duas vezes a mesma classe. E e o que a historia diz -- o bebe de
		// classe baixa era MANDADO a um planeta fraco pra conquista-lo, e o planeta era a Terra.
		//
		// A TRAVA `raca == "Saiyan"` NAO E DECORACAO: "Low-Class" tambem e classe do HERAN
		// (`statheran.dm:76` -- 48% deles!), e o `GameServer.Sigilo.cs:187` ja teve que desempatar
		// essa string pela raca uma vez. Sem a trava, metade dos Herans seria despejada na Terra.
		// ====================================================================================================
		if (raca == "Saiyan" && classe == "Low-Class")
		{
			natal = "Earth";
			despejado = true;
		}

		Berco b = pediuVizinho
			? Vizinho(seedDoUniverso, natal, seedDoPersonagem)
			: DePreFeito(natal, MotivoDoBerco.PlanetaNatal);

		return b with { Despejado = despejado };
	}

	/// <summary>
	/// ESTE CORPO E UM LENDARIO?
	///
	/// Sao DUAS classes e nao uma -- `Legendary` (1% da linhagem Saiyan, `Birth.cs:47`) e
	/// `Legendary Primal Saiyan` (2% da linhagem Primal, `Birth.cs:43`) --, e o predicado por
	/// `Contains` e o MESMO que `GameServer.Formas.cs:46` ja usa pra abrir a escada Lendaria. Uma
	/// segunda definicao de "quem e Lendario" envelheceria separada da que decide as transformacoes,
	/// e o sintoma seria alguem exilado sem escada ou com escada sem exilio.
	///
	/// Ele NAO pega `Normal Primal Saiyan`, que e o certo.
	///
	/// A LINHAGEM E A SEGUNDA TRAVA, e ela existe pelo mesmo motivo que a trava de raca do
	/// classe-baixa: "Low-Class" ja provou que uma string de classe nao pertence a uma raca so.
	/// Exigir que a linhagem seja Saiyajin impede que uma classe futura de outra raca com a palavra
	/// "Legendary" no nome caia no exilio -- o mesmo erro, um nivel acima. Linhagem vazia conta como
	/// "Saiyan", que e o que `Birth.Nascer:93` grava.
	/// </summary>
	public static bool EhLendario(string raca, string classe, string linhagem) =>
		raca == "Saiyan"
		&& (linhagem.Length == 0 || linhagem == "Saiyan" || linhagem == "Primal Saiyan")
		&& classe.Contains("Legendary", StringComparison.OrdinalIgnoreCase);

	// =====================================================================
	// 3b. A SEMENTE DESTE CORPO
	// =====================================================================
	/// <summary>
	/// A SEMENTE DE BERCO DE UM PERSONAGEM -- o `seedDoPersonagem` do <see cref="Onde"/>.
	///
	/// ============================ DERIVADA, E POR ISSO O SAVE ANTIGO TAMBEM TEM UMA ============================
	/// O precedente vivo e a COR DA AURA (`Appearance.DeNome` -> `CorDeAura.De`): ela nao tem ramo
	/// de migracao nenhum porque e funcao pura de nome + instante de criacao, e essa dupla esta
	/// gravada em TODO save desde sempre (`CharacterSave.Nome` / `CharacterSave.CriadoEm`). Nulo
	/// deriva; ninguem inventa uma flag de "ja escolheu".
	///
	/// Aqui vale igual, e vale mais: um personagem criado ANTES desta regra nasceu na Terra e nao
	/// tem berco escrito em lugar nenhum. Derivando, ele GANHA um -- o mesmo que teria se tivesse
	/// nascido hoje --, e ele e estavel: a mesma dupla devolve o mesmo planeta pra sempre, em toda
	/// maquina e em todo reinicio (<see cref="Espaco.Hash64"/> existe justamente por isso;
	/// `string.GetHashCode()` e randomizado por processo e NAO serviria).
	/// ========================================================================================================
	///
	/// ============================ E MESMO ASSIM O CAMPO E GRAVADO ============================
	/// O <see cref="Onde"/> avisa que a semente tem que ser sorteada UMA VEZ e guardada, senao o
	/// berco anda. Nome e instante de criacao nao mudam hoje -- mas "hoje": no dia em que existir um
	/// verb de renomear, um jogador renomeado renasceria noutro planeta, calado. Gravar o numero
	/// custa oito bytes e fecha essa porta; derivar e o que faz o save de ontem funcionar.
	/// =====================================================================================
	///
	/// O SAL SEPARA ESTA REGRA DA COR DA AURA. As duas partem de `SementeDe(nome, criadoEm)`; sem
	/// sal, dois sistemas sorteariam do MESMO numero cru e ficariam correlacionados -- o mesmo
	/// cuidado que o `Appearance` ja documenta ao nao reusar a semente dos limiares.
	/// </summary>
	public static ulong SementeDoBerco(string nome, long criadoEm) =>
		Espaco.Misturar(
			Jandirus.Core.Forms.LimiaresPessoais.SementeDe(nome, criadoEm), SalDaSemente, 0);

	private const ulong SalDaSemente = 0x42_45_52_43_4F_53_00_01UL;   // "BERCOS"

	// =====================================================================
	// 4. O VIZINHO: "UM PLANETA ALEATORIO PROXIMO AO NATAL"
	// =====================================================================
	/// <summary>
	/// UMA ORBITA IRMA DA ESTRELA DO NATAL.
	///
	/// ============================ A REGRA DE VIZINHANCA NAO PRECISOU SER ESCRITA ============================
	/// A especificacao (secao 11) mandava construir "uma regra de vizinhanca que garanta pelo menos
	/// um planeta procedural a poucos chunks da Terra", porque o procedural mais proximo ficava a ~39
	/// chunks. Esse numero morreu com o remake do espaco: **todo pre-feito ancora um sistema de
	/// <see cref="Sistemas.OrbitasAncoradas"/> orbitas** (`Sistemas.Ancorar`), entao a Terra ja tem
	/// tres planetas gerados na propria estrela dela -- medidos a 4,6 / 4,6 / 4,8 chunks, ou 58 a 61
	/// segundos de voo base. Vegeta, Namek, Icer, Arconia, Arlia e Makyo idem, 23 a 57 s.
	///
	/// Ou seja: "perto do natal" ja e uma propriedade da CONSTRUCAO do universo, e escrever uma regra
	/// de densidade em cima disso seria uma segunda fonte pra divergir da primeira.
	/// ====================================================================================================
	///
	/// SEMPRE EXISTE E SEMPRE HA TRES: um sistema ancorado nunca e nulo (`Sistemas.Do` o devolve
	/// antes de qualquer sorteio) e tem exatamente 4 orbitas, uma delas ocupada pelo pre-feito.
	///
	/// MAS AS TRES NEM SEMPRE SERVEM, e isso e informacao e nao acidente. Medido nos sete sistemas:
	/// a Terra tem 2 irmas validas (a terceira pesa 30g), Vegeta tem 2 (a terceira pesa 67g -- zona
	/// de morte encostada em casa) e ICER TEM UMA SO, entao um Frost Demon que peca vizinho tem um
	/// unico destino possivel. Namek, Arconia, Arlia e Makyo_Star tem as tres.
	///
	/// DOIS DESFECHOS SEM VAZIO:
	///   * o natal nao esta na carta (Paraiso, Inferno) -> nao ha vizinho a oferecer, e o corpo nasce
	///     no proprio natal. A tela nao deve nem mostrar a opcao pra essas duas racas;
	///   * as tres irmas sao todas pesadas demais (<see cref="ServeDeBerco"/>) -> mesma coisa. Voce
	///     pediu vizinho, a vizinhanca e zona de morte, voce nasceu em casa.
	/// </summary>
	/// <param name="teto">Ver <see cref="ServeDeBerco"/> -- so a bancada mexe nele.</param>
	public static Berco Vizinho(ulong seedDoUniverso, string natal, ulong seedDoPersonagem,
								double teto = GravidadeMaximaDeBerco)
	{
		if (SistemaDoPreFeito(natal) is not { } sis)
			return DePreFeito(natal, MotivoDoBerco.PlanetaNatal);

		ulong h = Espaco.Misturar(seedDoPersonagem ^ SalDoVizinho, (ulong)(uint)sis.Id.Sx, (ulong)(uint)sis.Id.Sy);

		// ============================ DUAS PASSADAS, E NAO UMA ROTACAO ============================
		// Havia aqui uma rotacao ("comeca num k sorteado, anda ate achar um que sirva"). Ela PARECE
		// uniforme e nao e: a orbita logo depois de uma pulada -- a do pre-feito ou uma reprovada no
		// crivo -- herda o peso dela. A bancada mediu na Terra 75,3% / 24,7% entre as duas irmas
		// validas, com a pulada do pre-feito caindo inteira em cima da primeira.
		//
		// O dono pediu um planeta ALEATORIO; tres em quatro no mesmo mundo nao e aleatorio. Contar
		// primeiro e escolher a n-esima custa uma segunda volta em no maximo 10 orbitas, uma vez por
		// nascimento -- e devolve o sorteio plano que a regra promete.
		// =====================================================================================
		int servem = 0;
		for (int k = 0; k < sis.Orbitas; k++)
			if (k != sis.OrbitaPreFeita && ServeDeBerco(sis.Planeta(k), teto)) servem++;

		if (servem > 0)
		{
			int escolhida = (int)(h % (ulong)servem);
			for (int k = 0; k < sis.Orbitas; k++)
			{
				if (k == sis.OrbitaPreFeita || !ServeDeBerco(sis.Planeta(k), teto)) continue;
				if (escolhida-- == 0) return De(sis, k, natal, MotivoDoBerco.VizinhoDoNatal, 1, false);
			}
		}

		return DePreFeito(natal, MotivoDoBerco.PlanetaNatal);
	}

	/// <summary>
	/// AS IRMAS QUE A TELA DE CRIACAO PODE MOSTRAR -- os mundos que "nascer perto de casa" sorteia.
	///
	/// ============================ NAO PRECISA DA SEED DO UNIVERSO, E ISSO E O PONTO ============================
	/// Um sistema ANCORADO nasce do proprio pre-feito e de mais nada (`Sistemas.Ancorar`: tudo sai
	/// de `Hash64(nome do planeta)`), entao as tres irmas da Terra sao as mesmas em qualquer
	/// universo. A tela de criacao consegue ENUMERA-LAS antes de existir personagem, antes de haver
	/// mundo carregado e sem um byte a mais de protocolo -- e o determinismo da regra 0.2 sai de
	/// graca, porque as duas pontas leem a MESMA funcao.
	///
	/// O que a tela NAO consegue mostrar e QUAL das irmas sai: isso depende da semente do
	/// personagem, que so existe depois que o servidor o cria. E e assim que tem que ser -- a
	/// gravidade do berco vira `GravMastered` de graca (`race.dm:130-131`), entao deixar o cliente
	/// escolher o mundo seria deixar o cliente escolher um atributo. A tela mostra o SORTEIO; quem
	/// sorteia e o servidor.
	/// ======================================================================================================
	///
	/// Vazia quer dizer "esta raca nao tem essa opcao", e ha dois motivos possiveis: o natal nao fica
	/// no mapa do universo (Paraiso, Inferno) ou as irmas todas sao pesadas demais pra um
	/// recem-nascido. Nos dois casos <see cref="Vizinho"/> devolve o proprio natal, entao a tela que
	/// esconder a opcao esta contando a verdade.
	/// </summary>
	public static List<PlanetaNoEspaco> IrmasDoNatal(string natal, double teto = GravidadeMaximaDeBerco)
	{
		var l = new List<PlanetaNoEspaco>();
		if (SistemaDoPreFeito(natal) is not { } sis) return l;

		for (int k = 0; k < sis.Orbitas; k++)
			if (k != sis.OrbitaPreFeita && ServeDeBerco(sis.Planeta(k), teto)) l.Add(sis.Planeta(k));

		return l;
	}

	// =====================================================================
	// 5. O EXILIO DO LENDARIO
	// =====================================================================
	/// <summary>
	/// ANEL DE EXILIO, em celulas de sistema (Chebyshev). Uma celula mede
	/// <see cref="Sistemas.CelulaPx"/> = 65.536 px = 6,8 min de voo base.
	///
	/// A ESCALA SAI DOS PRE-FEITOS, e nao de gosto -- medida a partir de Vegeta, que e de onde o
	/// Lendario e expulso:
	///
	///     Vegeta -> Makyo_Star   18,3 celulas = 125 min de voo base
	///     Vegeta -> Earth        17,0 celulas = 116 min
	///     Vegeta -> Arconia      25,0 celulas = 171 min
	///     Vegeta -> Namek        31,8 celulas = 217 min
	///     Vegeta -> Arlia/Icer   45,6 / 47,3 celulas = 311 / 323 min
	///
	/// Um anel de [4, 24] poe o exilado entre "mais perto que a Terra" e "mais longe que Arconia".
	/// A bancada mediu o resultado em 200 mil sorteios: minimo 26 min de Vegeta, media 110 min
	/// (quase exatamente a distancia Vegeta-Terra), maximo 233 min. Ou seja, o exilio cai na MESMA
	/// escala em que os mundos conhecidos ja estao -- longe o bastante pra ninguem tropecar nele,
	/// perto o bastante pra caber na carta estelar que o jogo ja desenha.
	///
	/// O MINIMO E O QUE FAZ A REGRA SIGNIFICAR ALGUMA COISA. Sem ele, "aleatorio" poderia devolver a
	/// propria estrela de Vegeta e o exilio seria uma mudanca de bairro.
	///
	/// E EM 0,04% DOS SORTEIOS o anel cai numa celula ANCORADA e o exilado nasce num pre-feito
	/// (Namek, Arconia, Makyo_Star...). Medido, e mantido: um Lendario em cada 2.500 nascendo num
	/// mundo conhecido e uma historia, nao um defeito -- e o corpo continua sendo um mapa feito a
	/// mao, entao nao ha risco nenhum nisso. Vegeta esta fora por construcao: ela e o centro do anel.
	/// </summary>
	public const int ExilioAnelMinimo = 4;
	public const int ExilioAnelMaximo = 24;

	/// <summary>
	/// Quantas celulas o exilio pode olhar antes de desistir. Cada tentativa e um sorteio NOVO
	/// dentro do mesmo anel (e nao uma espiral saindo de um ponto), pra que uma regiao vazia nao
	/// empurre o exilado sistematicamente pra perto de casa.
	///
	/// Custo: um <see cref="Sistemas.Do"/> por tentativa, ~90 ns. O teto inteiro custa menos que um
	/// centesimo de tique, e a bancada mede quantas tentativas realmente acontecem -- eram 1 quando
	/// toda celula tinha sistema, e sao 1,6 em media desde que 37,5% delas ficaram vazias
	/// (<see cref="Sistemas.VaziosPor256"/>). A cauda e geometrica: a chance de 96 tentativas seguidas
	/// caírem no vazio e 0,375^96, ou seja o ultimo recurso continua inalcancavel em jogo.
	/// </summary>
	public const int ExilioTentativas = 96;

	private const ulong SalDoExilio = 0xB5026F5AA96619E9UL;
	private const ulong SalDoVizinho = 0x27D4EB2F165667C5UL;

	/// <summary>
	/// O LENDARIO NASCE LONGE, E NAO E ELE QUE ESCOLHE.
	///
	/// ============================ O MOTIVO, PRA QUE NINGUEM "CONSERTE" ISTO PRA VEGETA ============================
	/// A sociedade Saiyajin tinha MEDO do Lendario. Um bebe que nasce com poder de destruir o proprio
	/// mundo nao e criado em casa: e posto numa capsula e mandado pra onde nao possa ser encontrado.
	/// E por isso que ele e a UNICA classe cujo berco nao tem nada a ver com a raca dela, e por isso
	/// que o `pediuVizinho` do jogador e ignorado aqui. Quem um dia achar que "Saiyajin nasce em
	/// Vegeta" e apagar esta funcao vai estar apagando a historia junto com o codigo.
	/// ==========================================================================================================
	///
	/// ============================ ALEATORIO SIM, `Random` NAO ============================
	/// Tudo sai de `seedDoPersonagem`. Um `Random.Shared` aqui daria um planeta que o servidor nao
	/// saberia qual e (o cliente mostraria um e o servidor abriria outro) e que MUDARIA a cada login
	/// -- o jogador renasceria num mundo diferente toda vez que morresse.
	/// =====================================================================================
	///
	/// ============================ E O PLANETA TEM QUE EXISTIR ============================
	/// Sortear uma coordenada NAO basta, e este e o modo de falha desta funcao inteira: um jogador
	/// nascendo no espaco. Duas coisas podem faltar num ponto do universo sorteado:
	///   * a celula pode nao ter sistema, e HOJE ISSO E COMUM: 37,5% das celulas sao sorteadas vazias
	///     (<see cref="Sistemas.VaziosPor256"/>, o vazio que o dono pediu na carta estelar), mais as
	///     raras que <see cref="Sistemas.Do"/> ANULA por se sobreporem a um sistema ancorado. O anel
	///     precisa em media 1,6 tentativas em vez de 1, contra as 96 que ele tem;
	///   * o indice da orbita pode nao existir -- um sistema tem de 1 a 10 orbitas, sorteadas.
	///
	/// Por isso aqui nao se sorteia planeta: **sorteia-se uma celula e ENUMERA-SE o que ela tem**.
	/// `Sistemas.Do` -> `s.Orbitas` -> `s.Planeta(k)` so devolve corpos que existem, pela mesma
	/// funcao que a carta estelar desenha e que o pouso consulta. O que se sorteia e por ONDE
	/// COMECAR a leitura, nunca o resultado.
	///
	/// E ainda assim ha DOIS recuos, porque um teto que nunca dispara e indistinguivel de teto nenhum
	/// (regra 0.7) e os dois tem que ser exercitaveis:
	///   * A RESERVA -- se nenhum dos corpos do anel passar no crivo de gravidade, vale o primeiro
	///     que simplesmente EXISTE. Mundo pesado e melhor que lugar nenhum, e o `max` do
	///     `race.dm:130-131` acostuma o corpo a gravidade do berco, entao ele sobrevive;
	///   * O ULTIMO RECURSO -- se nenhuma das celulas tiver sistema, uma orbita de um dos sete
	///     sistemas ANCORADOS, que nunca sao nulos e sempre tem <see cref="Sistemas.OrbitasAncoradas"/>
	///     orbitas. Este caminho nao pode devolver vazio nem em teoria.
	///
	/// Os dois marcam <see cref="Berco.UltimoRecurso"/>, e os parametros `teto` e `tentativas`
	/// existem pra que a bancada consiga faze-los rodar apertando o CODIGO DE PRODUCAO -- `teto = 0`
	/// reprova todo mundo gerado e cai na reserva; `tentativas = 0` pula o anel e cai no ultimo
	/// recurso. Em jogo ninguem passa nenhum dos dois.
	/// ==================================================================================
	/// </summary>
	public static Berco Exilio(ulong seedDoUniverso, string natal, ulong seedDoPersonagem,
							   double teto = GravidadeMaximaDeBerco, int tentativas = ExilioTentativas)
	{
		// O anel e medido a partir da estrela do NATAL: quem o expulsou foi a sociedade dele.
		SistemaSolar? casa = SistemaDoPreFeito(natal) ?? SistemaDoPreFeito("Earth");
		int cx = casa?.Id.Sx ?? 0, cy = casa?.Id.Sy ?? 0;

		// A RESERVA: o primeiro corpo que EXISTE, mesmo reprovado no crivo de gravidade. Se o anel
		// inteiro so tiver mundos pesados, e melhor um mundo pesado que um lugar nenhum -- o DM
		// acostuma o corpo a gravidade do berco (`race.dm:130-131`), entao ele sobrevive.
		SistemaSolar reserva = default;
		int reservaK = -1;

		for (int i = 0; i < tentativas; i++)
		{
			ulong h = Espaco.Misturar(seedDoPersonagem ^ SalDoExilio, (ulong)(uint)i, (ulong)(uint)(cx * 31 + cy));
			(int dx, int dy) = CelulaNoAnel(h, ExilioAnelMinimo, ExilioAnelMaximo);

			if (Sistemas.Do(seedDoUniverso, cx + dx, cy + dy) is not { } s) continue;

			// AQUI A ROTACAO FICA, e a assimetria com o <see cref="Vizinho"/> e de proposito. La o
			// sorteio e ENTRE TRES irmas e o vies de rotacao virava 75/25 na cara do jogador; aqui a
			// uniformidade que importa e a das CELULAS, e ela e plana por construcao -- a bancada
			// mediu 2.315 celulas e 10.181 planetas distintos em 200 mil exilios, com o mais repetido
			// em 0,15%. Contar as orbitas validas antes de escolher dobraria as chamadas de
			// `s.Planeta(k)`, que MONTAM UMA STRING cada (o proprio `Sistemas.Assinatura` mediu que os
			// nomes custam mais que todo o resto junto) -- e este e o unico laco quente do arquivo.
			int inicio = (int)((h >> 40) % (ulong)s.Orbitas);
			for (int j = 0; j < s.Orbitas; j++)
			{
				int k = (inicio + j) % s.Orbitas;
				PlanetaNoEspaco p = s.Planeta(k);

				if (reservaK < 0) { reserva = s; reservaK = k; }
				if (ServeDeBerco(p, teto)) return De(s, k, natal, MotivoDoBerco.ExilioDoLendario, i + 1, false);
			}
		}

		if (reservaK >= 0)
			return De(reserva, reservaK, natal, MotivoDoBerco.ExilioDoLendario, tentativas, true);

		// ULTIMO RECURSO: uma orbita de um sistema ancorado. Nao ha como isto devolver vazio --
		// `Sistemas.ComPreFeito` tem sete entradas fixas e cada uma tem `OrbitasAncoradas` orbitas.
		IReadOnlyList<SistemaSolar> ancorados = Sistemas.ComPreFeito;
		ulong hf = Espaco.Misturar(seedDoPersonagem ^ SalDoExilio, 0xFFFF, 0xFFFF);
		SistemaSolar fim = ancorados[(int)(hf % (ulong)ancorados.Count)];
		return De(fim, (int)((hf >> 32) % (ulong)fim.Orbitas), natal,
				  MotivoDoBerco.ExilioDoLendario, tentativas, true);
	}

	/// <summary>
	/// UMA CELULA NA BORDA DE UM QUADRADO de raio Chebyshev sorteado em [min, max].
	///
	/// Anel e nao disco: um disco concentraria os sorteios perto do centro (a area cresce com o
	/// quadrado do raio), e o exilado cairia quase sempre no anel minimo -- que e justamente a
	/// distancia que a regra quer evitar.
	///
	/// Aritmetica inteira pura, sem trigonometria, pelo mesmo motivo do
	/// `Sistemas.DirecaoDaOrbita`: `Cos`/`Sin` variam de plataforma pra plataforma e o cliente pode
	/// nao rodar no mesmo sistema que o servidor.
	/// </summary>
	internal static (int dx, int dy) CelulaNoAnel(ulong h, int min, int max)
	{
		int r = min + (int)((h >> 8) % (ulong)(max - min + 1));
		int lado = 2 * r;                                   // celulas por aresta
		int t = (int)((h >> 24) % (ulong)(4 * lado));       // posicao no perimetro
		int face = t / lado, o = t % lado - r;              // aresta e deslocamento nela

		return face switch
		{
			0 => (o, -r),
			1 => (r, o),
			2 => (-o, r),
			_ => (-r, -o),
		};
	}

	// =====================================================================
	// 6. MONTAGEM
	// =====================================================================
	/// <summary>O sistema ancorado de um pre-feito, ou nulo se ele nao fica no mapa do universo.</summary>
	private static SistemaSolar? SistemaDoPreFeito(string nome)
	{
		foreach (SistemaSolar s in Sistemas.ComPreFeito)
			if (string.Equals(s.PreFeito.Nome, nome, StringComparison.OrdinalIgnoreCase)) return s;
		return null;
	}

	/// <summary>Berco numa orbita concreta de um sistema concreto -- o unico caminho pro endereco.</summary>
	private static Berco De(SistemaSolar s, int k, string natal, MotivoDoBerco motivo,
							int olhadas, bool ultimo)
	{
		PlanetaNoEspaco p = s.Planeta(k);
		return new Berco
		{
			Planeta = p.Nome,
			Seed = p.Seed,
			PreFeito = p.Premade,
			Sx = s.Id.Sx,
			Sy = s.Id.Sy,
			K = k,
			Pos = p.Pos,
			Raio = p.Raio,
			Gravidade = p.Premade ? 0 : MundoProcedural.GravidadeDaSeed(p.Seed),
			Natal = natal,
			Motivo = motivo,
			CelulasOlhadas = olhadas,
			UltimoRecurso = ultimo,
		};
	}

	/// <summary>
	/// Berco NO proprio pre-feito. Passa pelo sistema ancorado quando ele existe, pra que o endereco
	/// (Sx, Sy, K) seja preenchido tambem nos mapas feitos a mao -- assim quem consome um
	/// <see cref="Berco"/> nao precisa saber de que tipo ele e.
	///
	/// Paraiso e Inferno nao tem sistema, e ai o endereco fica em `K = -1`: eles existem como ZONA
	/// (`Assets/Maps/manifest.json`, z10 e z09) e nao existem como CORPO.
	/// </summary>
	private static Berco DePreFeito(string nome, MotivoDoBerco motivo)
	{
		if (SistemaDoPreFeito(nome) is { } s)
			return De(s, s.OrbitaPreFeita, nome, motivo, 0, false);

		return new Berco
		{
			Planeta = nome,
			Seed = Espaco.Hash64(nome),
			PreFeito = true,
			Sx = 0,
			Sy = 0,
			K = -1,
			Natal = nome,
			Motivo = motivo,
		};
	}
}
