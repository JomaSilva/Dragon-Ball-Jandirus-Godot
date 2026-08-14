using System.Text.Json;

namespace Jandirus.Core.Ranks;

/// <summary>O que o servidor sabe de quem esta reivindicando. Tudo que os requisitos consultam.</summary>
public readonly struct FichaDeRank
{
	public string Raca { get; init; }

	/// <summary>
	/// A raca do SANGUE (o `Parent_Race` do DM). Meio-sangue vale pelos dois: no original quase
	/// todo requisito racial pergunta `M.Race == X || M.Parent_Race == X`, e ignorar a segunda
	/// metade fecharia o Guardiao da Terra pra um meio-Namekuseijin que o DM aceita.
	/// </summary>
	public string RacaMae { get; init; }

	public string Classe { get; init; }
	public double BpBase { get; init; }
	public int Karma { get; init; }

	/// <summary>A chave do cargo que esta alma ja carrega ("" = nenhum). A escada usa isto.</summary>
	public string CargoAtual { get; init; }

	// --- os campos que o DM consulta e que o port so agora passa a receber ---
	public bool GodKiDesperto { get; init; }
	public double GodKiMaestria { get; init; }   // 0-100
	public double Zeni { get; init; }

	/// <summary>
	/// ============================ A REPUTACAO DE PLANETA, QUE QUATRO CARGOS PEDIAM EM TEXTO ============================
	/// Quatro `RankDef` carregavam a pendencia escrita *"o port nao tem reputacao de planeta"*. Ela
	/// CADUCOU: o livro-caixa entrou com a cadeia de sagas (`Core.Social.Reputacao` +
	/// `GameServer.Reputacao.cs`), persiste em disco e agora tem tres fontes (chefe derrotado,
	/// habitante morto, conquista). O que faltava era ele CHEGAR aqui.
	///
	/// E um mapa e nao um numero porque a pergunta do DM e sempre por planeta: o Guardiao quer ser
	/// heroi **da Terra** e o Grande Anciao quer ser amigavel **de Namek** -- um escalar so responderia
	/// a pergunta errada com um numero plausivel.
	///
	/// Nulo = "este servidor nao respondeu", e ai toda regra de reputacao FALHA em vez de passar. E o
	/// mesmo cuidado que o comentario dos quatro campos acima ja registra: `double` zero com
	/// comparacao `>=` abriria os cargos de graca.
	/// ============================================================================================================
	/// </summary>
	public IReadOnlyDictionary<string, double>? ReputacaoDePlaneta { get; init; }

	/// <summary>Quanto o povo deste planeta gosta desta alma. Zero pra quem nunca fez nada la.</summary>
	public double ReputacaoCom(string planeta) =>
		ReputacaoDePlaneta != null && ReputacaoDePlaneta.TryGetValue(planeta, out double v) ? v : 0;

	/// <summary>
	/// QUANTOS PLANETAS ESTA ALMA DOMINA -- o `conq_count_owned` (PlanetConquest.dm:101), que no DM
	/// e o gate de compra da criacao de Esferas do Cla do Dragao.
	///
	/// Ele entra aqui porque a especificacao das quests de cargo vai querer dizer "conquiste um
	/// planeta", e porque uma API de conquista que ninguem consulta e o defeito que este projeto ja
	/// pagou uma vez inteiro (o sigilo de BP, escrito e 100% orfao). Nenhum cargo do DM exige isto
	/// hoje -- o campo existe LIGADO e sem regra que o use, e essa e a ordem certa: a regra e uma
	/// linha, a fiacao e que e cara.
	/// </summary>
	public int PlanetasDominados { get; init; }
}

/// <summary>O que um requisito olha na ficha.</summary>
public enum CampoDeRank
{
	Karma,
	Bp,
	Raca,
	Classe,
	Cargo,
	GodKi,
	Zeni,

	/// <summary>Reputacao com UM planeta: `Valores[0]` e o planeta, `Valor` e o piso.</summary>
	Reputacao,

	/// <summary>Quantos planetas esta alma domina (`conq_count_owned`).</summary>
	Dominios,
}

/// <summary>Uma condicao unica. Varias delas ANDadas formam uma OPCAO de uma <see cref="Regra"/>.</summary>
public sealed class Exigencia
{
	public CampoDeRank Campo;

	/// <summary>"&gt;=", "&lt;=", "e", "desperto", "maestria&gt;=".</summary>
	public string Op = ">=";

	public double Valor;
	public string[] Valores = [];

	public bool Vale(in FichaDeRank f) => Campo switch
	{
		CampoDeRank.Karma => Op == ">=" ? f.Karma >= Valor : f.Karma <= Valor,
		CampoDeRank.Bp => f.BpBase >= Valor,
		CampoDeRank.Zeni => f.Zeni >= Valor,
		CampoDeRank.Raca => Valores.Contains(f.Raca, StringComparer.OrdinalIgnoreCase)
						 || Valores.Contains(f.RacaMae, StringComparer.OrdinalIgnoreCase),
		CampoDeRank.Classe => Valores.Contains(f.Classe, StringComparer.OrdinalIgnoreCase),
		CampoDeRank.Cargo => Valores.Contains(f.CargoAtual, StringComparer.OrdinalIgnoreCase),
		CampoDeRank.GodKi => Op == "desperto" ? f.GodKiDesperto : f.GodKiDesperto && f.GodKiMaestria >= Valor,

		// NULO REPROVA. Ver `FichaDeRank.ReputacaoDePlaneta`: um servidor que nao respondeu nao pode
		// abrir cargo de graca -- e o mesmo motivo pelo qual os quatro campos de God Ki foram
		// preenchidos em vez de ficarem no default.
		CampoDeRank.Reputacao => f.ReputacaoDePlaneta != null
							  && f.ReputacaoCom(Valores.Length > 0 ? Valores[0] : "") >= Valor,

		CampoDeRank.Dominios => f.PlanetasDominados >= Valor,
		_ => true,
	};
}

/// <summary>
/// UMA LINHA do `rq_requirements` do DM: um teste que, ao falhar, devolve uma frase pronta.
///
/// `Opcoes` e um OU de Es: a regra passa se QUALQUER opcao tiver TODAS as suas exigencias
/// cumpridas. E o formato minimo que aguenta o que o original escreve de verdade -- o Kaioshin
/// pede "ser o Grand Kai OU (sangue Kai E maestria de God Ki 33%)", que nem uma lista simples de
/// alternativas nem uma lista simples de requisitos consegue representar sem afrouxar ou apertar.
/// </summary>
public sealed class Regra
{
	public Exigencia[][] Opcoes = [];

	/// <summary>A frase do DM, com os acentos que o BYOND nao deixava escrever.</summary>
	public string Texto = "";

	/// <summary>`arquivo.dm:linha` de onde o numero saiu.</summary>
	public string Fonte = "";

	public bool Vale(in FichaDeRank f)
	{
		foreach (Exigencia[] opcao in Opcoes)
		{
			bool ok = true;
			foreach (Exigencia e in opcao) if (!e.Vale(f)) { ok = false; break; }
			if (ok) return true;
		}
		return Opcoes.Length == 0;
	}
}

/// <summary>Por onde o cargo ENTRA. So a primeira passa pelo verb de reivindicar.</summary>
public enum PortaDoCargo
{
	/// <summary>`RQ_CLAIMABLE`: requisitos + prova contra o Espirito do Cargo.</summary>
	Prova,

	/// <summary>So o verb `Give Rank` do admin (`Rewards.dm`). O DM nao escreveu requisito nenhum.</summary>
	Admin,

	/// <summary>Duelo formal pelo titulo (o Deus da Destruicao).</summary>
	Duelo,

	/// <summary>Sucessao violenta: matar o portador (o Rei de Vegeta, o Lorde do Gelo).</summary>
	Sucessao,

	/// <summary>Nomeado por quem ja tem o cargo de cima (os quatro Anciaos de Namek).</summary>
	Nomeacao,
}

/// <summary>Um cargo. Um dono por vez no mundo, e a alma so carrega um.</summary>
public sealed class RankDef
{
	public string Chave = "";
	public string Nome = "";
	public string Desc = "";

	/// <summary>A global do DM que guarda a signature do dono. E a identidade real do cargo.</summary>
	public string Global = "";

	public PortaDoCargo Porta = PortaDoCargo.Admin;

	/// <summary>Cargos que este cargo exige como degrau anterior. Vazio = porta de entrada.</summary>
	public string[] Degraus = [];

	public Regra[] Regras = [];

	/// <summary>`RQ_ALL`: recebe tarefas com prazo e e destituido depois de 3 falhas.</summary>
	public bool TemDeveres;

	/// <summary>`RQ_SKY`: da pra reivindicar e servir MORTO (os cargos do Outro Mundo).</summary>
	public bool DoOutroMundo;

	/// <summary>`RQ_WISDOM`: a tarefa e de SERVICO (presenca), nao de PODER.</summary>
	public bool Sabedoria;

	/// <summary>`RQ_EVIL`: cumprir tarefa NAO paga karma bom.</summary>
	public bool Maligno;

	/// <summary>O que o cargo concede no DM. Texto, porque a maior parte ainda nao existe no port.</summary>
	public string[] Concede = [];

	/// <summary>
	/// Requisitos que o DM COBRA e que este port ainda nao tem como medir. Ficam de fora do
	/// <see cref="Cargos.OqueFalta"/> -- e ficam AQUI, com a linha do DM, pra nao sumirem da conta
	/// quando o sistema chegar.
	///
	/// ============================ A LISTA ENCOLHEU, E ELA TEM QUE ENCOLHER ============================
	/// Este campo ja citou "reputacao de planeta" como exemplo do que nao se mede. **Nao cita mais**:
	/// o livro-caixa existe (`Core.Social.Reputacao` + `GameServer.Reputacao.cs`), chega aqui pelo
	/// <see cref="FichaDeRank.ReputacaoDePlaneta"/> e virou requisito que COBRA nos quatro cargos que
	/// so o pediam em texto. Sobra hoje UM exemplo de verdade: o contador de mortes do Enma
	/// (`RankQuests.dm:238`) -- ver a `Pendencias` dele mais abaixo.
	///
	/// Exemplo em comentario e afirmacao, nao ilustracao: quem le "o port nao mede reputacao" fecha a
	/// questao e nao vai conferir. Quando a proxima pendencia for paga, ELA SAI DAQUI TAMBEM.
	/// ============================================================================================
	/// </summary>
	public string[] Pendencias = [];
}

/// <summary>
/// OS CARGOS DO MUNDO, extraidos do DM por `Tools/AssetPipeline/DmRankScanner.cs`.
///
/// SAO TRINTA, e nenhuma lista do original tem os trinta. Um cargo no BYOND nao e um registro: e
/// uma variavel global solta com a signature do dono, e o que se sabe dela esta espalhado por seis
/// lugares que discordam entre si (a lista que salva, a lista que limpa, a que da nome, a que da
/// requisito, a que da skill, a que aparece pro jogador). O scanner cruza os seis; esta tabela e o
/// resultado, conferida contra `Assets/data/cargos.json` pelo <see cref="Conferir"/>.
///
/// TRES COISAS QUE DEFINEM O SISTEMA:
///
/// 1. **UM DONO POR CARGO, E UMA ALMA POR CARGO.** Nao ha "vice": reivindicar so vale com o trono
///    VAGO, e quem ja tem cargo com deveres nao acumula outro (`rq_any_rank`, RankQuests.dm:135).
/// 2. **KARMA E A PORTA MORAL.** A escola da Tartaruga pede karma alto; a do Grou pede karma ZERO
///    OU MENOS ("nao forma santos"); o Demon Lord pede -50 ou pior. E o unico sistema do jogo em
///    que ser mau ABRE porta em vez de fechar.
/// 3. **SO DEZESSEIS DOS TRINTA SE REIVINDICAM.** O resto e do admin, do duelo (Deus da
///    Destruicao), do assassinato (Rei de Vegeta, Lorde do Gelo) ou da nomeacao (Anciaos de
///    Namek). Cargo sem requisito no DM sai daqui SEM requisito inventado -- e com a porta
///    fechada pro verb de reivindicar, que e o que o original faz.
///
/// O QUE AINDA NAO ESTA AQUI: a PROVA contra o Espirito do Cargo (NPC a 1,15x do seu BP expresso,
/// RankQuests.dm:48). **As outras duas dividas foram pagas**: o motor de tarefas com prazo e
/// destituicao existe (`Core/Ranks/MissoesDeCargo.cs` + `Server/GameServer.CargoMissoes.cs`,
/// bancada `--cargomissoes`) e o RENOME de 3 tarefas e cobrado pelo `ReivindicarCargo`, entao a
/// escada dos Kaios deixou de subir so com o requisito.
/// </summary>
public static class Cargos
{
	// ---------------------------------------------------------------------------------------
	// atalhos de construcao (so pra tabela abaixo caber na tela)
	// ---------------------------------------------------------------------------------------
	private static Regra R(string texto, string fonte, params Exigencia[] tudoIsto) =>
		new() { Texto = texto, Fonte = fonte, Opcoes = [tudoIsto] };

	private static Regra Ou(string texto, string fonte, params Exigencia[][] opcoes) =>
		new() { Texto = texto, Fonte = fonte, Opcoes = opcoes };

	private static Exigencia KarmaMin(int n) => new() { Campo = CampoDeRank.Karma, Op = ">=", Valor = n };
	private static Exigencia KarmaMax(int n) => new() { Campo = CampoDeRank.Karma, Op = "<=", Valor = n };
	private static Exigencia Bp(double n) => new() { Campo = CampoDeRank.Bp, Valor = n };
	private static Exigencia Zeni(double n) => new() { Campo = CampoDeRank.Zeni, Valor = n };
	private static Exigencia Raca(params string[] r) => new() { Campo = CampoDeRank.Raca, Valores = r };
	private static Exigencia Classe(params string[] c) => new() { Campo = CampoDeRank.Classe, Valores = c };
	private static Exigencia Cargo(params string[] k) => new() { Campo = CampoDeRank.Cargo, Valores = k };
	private static Exigencia GodKiDesperto() => new() { Campo = CampoDeRank.GodKi, Op = "desperto" };
	private static Exigencia GodKiMaestria(double p) => new() { Campo = CampoDeRank.GodKi, Op = "maestria>=", Valor = p };

	/// <summary>
	/// Reputacao MINIMA com o povo de um planeta. O nome e o da ZONA ("Earth", "Namek") porque e assim
	/// que o livro-caixa indexa (`GameServer.SomarReputacao`) -- escrever "Terra" aqui daria um
	/// requisito que nunca casa e nunca reclama.
	/// </summary>
	private static Exigencia Rep(string planeta, double minimo) =>
		new() { Campo = CampoDeRank.Reputacao, Valores = [planeta], Valor = minimo };

	/// <summary>
	/// "Domine N planetas". **Nenhum cargo do DM exige isto**, e por isso ela e PUBLICA em vez de
	/// privada: quem a exercita hoje e a bancada da conquista, que monta a regra e afirma que ela
	/// reprova com zero dominios e passa com um. Sem esse uso o campo seria API escrita e nunca
	/// chamada -- o defeito exato que o sigilo de BP ja custou a este projeto uma vez.
	///
	/// Quando as quests de cargo forem construidas, "conquiste um planeta" e uma linha usando isto.
	/// </summary>
	public static Exigencia ExigirDominios(int quantos) =>
		new() { Campo = CampoDeRank.Dominios, Valor = quantos };

	/// <summary>
	/// FROST DEMON x ICER: no DM a raca se chama "Frost Demon" (staticer.dm:13) e o requisito
	/// compara com essa string (RankQuests.dm:251). No port, o `races.json` gravou "Icer" -- o
	/// extrator de protos pegou a folha do typepath (`/datum/genetics/proto/Icer`) em vez do
	/// `name` declarado. Enquanto isso nao se resolve do lado das racas, o cargo aceita os DOIS
	/// nomes: fechar o Lorde do Gelo por causa de um rotulo seria um bug invisivel.
	/// </summary>
	private const string RacaFrostDemon = "Frost Demon";
	private const string RacaFrostDemonNoPort = "Icer";

	public static RankDef[] Todos { get; private set; } =
	[
		// =========================== TERRA ===========================
		new()
		{
			Chave = "turtle", Global = "Turtle", Nome = "Turtle Hermit", Porta = PortaDoCargo.Prova,
			Desc = "A escola que forma protetores. Molda cascos de treino de até 10.000 kg, e usa e ensina o Kamehameha.",
			TemDeveres = true, Sabedoria = true,
			Regras =
			[
				R("karma 25+ (a escola da Tartaruga forma protetores)", "Modules/Ranks/RankQuests.dm:210", KarmaMin(25)),
				R("1M de BP base", "Modules/Ranks/RankQuests.dm:211", Bp(1_000_000)),
			],
			Concede = ["estilo Kame", "Kamehameha", "Mafuba (selo do pote)"],
		},
		new()
		{
			Chave = "crane", Global = "Crane", Nome = "Crane Hermit", Porta = PortaDoCargo.Prova,
			Desc = "A escola rival. Não forma santos -- forma assassinos eficientes.",
			TemDeveres = true, Sabedoria = true,
			Regras =
			[
				R("karma 0 ou menos (a escola do Grou não forma santos)", "Modules/Ranks/RankQuests.dm:213", KarmaMax(0)),
				R("1M de BP base", "Modules/Ranks/RankQuests.dm:214", Bp(1_000_000)),
			],
			Concede = ["estilo Grou", "voo do Grou", "Divisão de corpo", "Kikoho", "Dodompa"],
		},
		new()
		{
			Chave = "guardian", Global = "Earth_Guardian", Nome = "Guardião da Terra", Porta = PortaDoCargo.Prova,
			Desc = "A tradição de Kami. Faz chaves da Sala do Tempo e autoriza a entrada; do Clã do Dragão, também cria Esferas.",
			TemDeveres = true,
			Regras =
			[
				R("ser um Namekuseijin do Clã do Dragão (a tradição de Kami)", "Modules/Ranks/RankQuests.dm:216",
					Raca("Namekian"), Classe("Dragon clan")),
				R("karma 50+", "Modules/Ranks/RankQuests.dm:217", KarmaMin(50)),
				// A PENDENCIA CADUCOU: o livro-caixa de reputacao existe e agora chega na ficha.
				R("ser HERÓI do povo da Terra (reputação 50+)", "Modules/Ranks/RankQuests.dm:218",
					Rep("Earth", Social.Reputacao.LimiarDeHeroi)),
			],
			Concede =
			[
				"estilo dos Deuses", "Autorizar Sala do Tempo", "Guardar Corpo", "Ver Mortos",
				"Makankosappo", "Selo Superior", "Abrir a Zona Morta", "Observar", "Telepatia", "Cura",
			],
		},
		new()
		{
			Chave = "korin", Global = "Assistant_Guardian", Nome = "Korin", Porta = PortaDoCargo.Prova,
			Desc = "O assistente do Guardião: cultiva sementes dos deuses e abre o portal da Água Sagrada.",
			TemDeveres = true, Sabedoria = true,
			Regras =
			[
				R("karma 25+", "Modules/Ranks/RankQuests.dm:220", KarmaMin(25)),
				R("ser Amigável com o povo da Terra (reputação 15+)", "Modules/Ranks/RankQuests.dm:221",
					Rep("Earth", Social.Reputacao.LimiarDeAmigavel)),
			],
			Concede = ["Imbuir Semente (senzu)", "Autorizar Sala do Tempo", "Selo Superior", "Cura"],
		},
		new()
		{
			Chave = "president", Global = "President", Nome = "Presidente da Terra", Porta = PortaDoCargo.Prova,
			Desc = "Eleito. Taxa até 30 zeni por hora, comissiona a polícia e decreta recompensas por cabeças.",
			TemDeveres = true,
			Regras =
			[
				R("karma 0+", "Modules/Ranks/RankQuests.dm:241", KarmaMin(0)),
				// o zeni e COBRADO ao vencer a prova (RankQuests.dm:405), nao so exigido em caixa
				R("250.000 zeni de campanha (cobrados ao vencer a prova)", "Modules/Ranks/RankQuests.dm:243", Zeni(250_000)),
				R("ser Amigável com o povo da Terra (reputação 15+)", "Modules/Ranks/RankQuests.dm:242",
					Rep("Earth", Social.Reputacao.LimiarDeAmigavel)),
			],
			Concede = ["Impostos da Terra"],
		},

		// =========================== NAMEK ===========================
		new()
		{
			Chave = "elder", Global = "Namekian_Elder", Nome = "Grande Ancião de Namek", Porta = PortaDoCargo.Prova,
			Desc = "A voz do povo de Namek. Guarda 3 Esferas do Dragão e nomeia os quatro Anciãos.",
			TemDeveres = true, Sabedoria = true,
			Regras =
			[
				R("sangue Namekuseijin", "Modules/Ranks/RankQuests.dm:223", Raca("Namekian")),
				R("karma 25+", "Modules/Ranks/RankQuests.dm:224", KarmaMin(25)),
				R("ser Amigável com o povo de Namek (reputação 15+)", "Modules/Ranks/RankQuests.dm:225",
					Rep("Namek", Social.Reputacao.LimiarDeAmigavel)),
			],
			Concede = ["Nomear Ancião", "estilo Namek", "Enkumei", "Liberar Potencial", "Guardar Corpo", "Ver Mortos", "Cura"],
		},
		// OS QUATRO ANCIAOS CARDEAIS. Nao tem requisito no codigo: sao NOMEADOS pelo Grande Anciao
		// (Appoint_Elder, NamekRanks.dm:56). Nao aparecem no painel de Ranks do original, e os
		// quatro compartilham o MESMO mob.Rank ("Namekian Elder") -- o nome abaixo e o do menu de
		// admin (Rewards.dm:315-318), que e a unica coisa que os distingue por escrito.
		AnciaoCardeal("northelder", "North_Elder", "North Elder", "Modules/Admin/Rewards.dm:315"),
		AnciaoCardeal("southelder", "South_Elder", "South Elder", "Modules/Admin/Rewards.dm:316"),
		AnciaoCardeal("eastelder", "East_Elder", "East Elder", "Modules/Admin/Rewards.dm:317"),
		AnciaoCardeal("westelder", "West_Elder", "West Elder", "Modules/Admin/Rewards.dm:318"),

		// ======================= OUTRO MUNDO =========================
		// OS QUATRO KAIOS CARDEAIS: mesmo requisito, kit diferente. O `if("nkai","skai","ekai","wkai")`
		// do DM (RankQuests.dm:226) e um ramo so pros quatro -- por isso as regras sao identicas.
		Kaio("nkai", "North_Kai", "Kaio do Norte",
			"Ensina o Kaio-ken e a Genkidama (a mesma do Sr. Kaioh).",
			["estilo dos Deuses", "Kaio-ken", "Genkidama", "Guardar Corpo", "Ver Mortos", "Observar", "Reviver"]),
		Kaio("skai", "South_Kai", "Kaio do Sul",
			"Ensina a Expansão Corporal (x2 ataque físico, x1,2 resistência, /1,2 velocidade, -2% de stamina por segundo).",
			["estilo dos Deuses", "Expansão Corporal", "Guardar Corpo", "Ver Mortos", "Observar", "Reviver"]),
		Kaio("ekai", "East_Kai", "Kaio do Leste",
			"Ensina a Explosão de Ki (x2 poder de ki, -2% de stamina por segundo) e a Autodestruição.",
			["estilo dos Deuses", "Autodestruição", "Buster Shell", "Guardar Corpo", "Ver Mortos", "Observar", "Reviver"]),
		// O DM SE CONTRADIZ AQUI, e a contradicao esta viva: o comentario da global diz "West_Kai //Can
		// teach Self Destruction" (RankAssign.dm:245), mas o codigo da a Autodestruicao ao Kaio do
		// LESTE e deixa o do Oeste com a Buster Barrage (OtherworldRanks.dm:48-49, 96-110). Seguimos o
		// CODIGO -- e o que o jogador recebe em jogo; o comentario e so a intencao antiga.
		Kaio("wkai", "West_Kai", "Kaio do Oeste",
			"Ensina a Buster Barrage. (O comentário do DM promete a Autodestruição, mas o código dela é do Kaio do Leste.)",
			["estilo dos Deuses", "Buster Barrage", "Guardar Corpo", "Ver Mortos", "Observar", "Reviver"]),
		new()
		{
			Chave = "grandkai", Global = "Grand_Kai", Nome = "Grand Kai", Porta = PortaDoCargo.Prova,
			Desc = "O chefe dos quatro. Não se reivindica de fora: sobe-se.",
			TemDeveres = true, Sabedoria = true, DoOutroMundo = true,
			Degraus = ["nkai", "skai", "ekai", "wkai"],
			Regras =
			[
				R("ser um dos 4 Kaios cardeais (a escada dos deuses)", "Modules/Ranks/RankQuests.dm:231",
					Cargo("nkai", "skai", "ekai", "wkai")),
			],
			// A PENDÊNCIA CADUCOU: o RENOME é cobrado. O motor de tarefas existe
			// (`Core/Ranks/MissoesDeCargo.cs`), o Kaio cardeal só sobe com `RQ_PROMO_QUESTS` (3)
			// tarefas cumpridas NO CARGO DE AGORA, e quem confere é o `ReivindicarCargo`.
			Concede =
			[
				"estilo dos Deuses", "Liberar Potencial", "Guardar Corpo", "Ver Mortos", "Observar",
				"Reencarnar", "Restaurar Juventude", "Reviver", "Permissão do Loft dos Kaios", "Ritual do Poder",
			],
		},
		new()
		{
			Chave = "kaioshin", Global = "Supreme_Kai", Nome = "Kaioshin", Porta = PortaDoCargo.Prova,
			Desc = "O topo da escada divina. Concede o Místico sem prazo e toma aprendizes.",
			TemDeveres = true, Sabedoria = true, DoOutroMundo = true,
			// o rq_promo_targets (RankQuests.dm:167) deixa os 4 Kaios cardeais pularem direto pra ca,
			// alem do Grand Kai -- o degrau de baixo some ao subir, seja ele qual for.
			Degraus = ["nkai", "skai", "ekai", "wkai", "grandkai"],
			Regras =
			[
				Ou("ser o Grand Kai (ou sangue Kai com maestria de God Ki 33%+)", "Modules/Ranks/RankQuests.dm:234",
					[Cargo("grandkai")],
					[Raca("Kai"), GodKiMaestria(33)]),   // GODKI_BLUE_PCT, 1A Defines.dm:53
				R("karma 75+", "Modules/Ranks/RankQuests.dm:235", KarmaMin(75)),
			],
			// A PENDÊNCIA CADUCOU junto com a do Grand Kai -- ver lá.
			Concede =
			[
				"estilo dos Deuses", "Místico", "Guardar Corpo", "Ver Mortos", "Reencarnar", "Reviver",
				"Observar", "Permissão do Loft dos Kaios", "Ritual do Poder", "Cura", "Nomear Aprendiz de Kaioshin",
			],
		},
		new()
		{
			Chave = "yemma", Global = "King_Yemma", Nome = "King Yemma", Porta = PortaDoCargo.Prova,
			Desc = "O juiz do Outro Mundo: julga as almas e decide quem volta.",
			TemDeveres = true, Sabedoria = true, DoOutroMundo = true,
			Regras =
			[
				R("karma 50+", "Modules/Ranks/RankQuests.dm:237", KarmaMin(50)),
				R("5M de BP base", "Modules/Ranks/RankQuests.dm:239", Bp(5_000_000)),
			],
			Pendencias = ["já ter MORRIDO ao menos uma vez (conhecer o Outro Mundo) -- RankQuests.dm:238; o port não conta mortes"],
			Concede = ["Julgar", "Observar", "Reviver"],
		},
		new()
		{
			Chave = "demonlord", Global = "Demon_Lord", Nome = "Demon Lord", Porta = PortaDoCargo.Prova,
			Desc = "O senhor dos demônios. Pode majinizar.",
			TemDeveres = true, Maligno = true,
			Regras =
			[
				R("karma -50 ou pior (o Inferno exige um coração PODRE)", "Modules/Ranks/RankQuests.dm:245", KarmaMax(-50)),
				R("5M de BP base", "Modules/Ranks/RankQuests.dm:246", Bp(5_000_000)),
			],
			Concede =
			[
				"estilo Demônio", "Guardar Corpo", "Ver Mortos", "Observar", "Majinizar",
				"Reencarnar", "Reviver", "Liberar Potencial", "Restaurar Juventude", "Ritual do Poder",
			],
		},
		new()
		{
			Chave = "makyo", Global = "King_Of_Hell", Nome = "Makyo King", Porta = PortaDoCargo.Prova,
			Desc = "O rei do Makyo.",
			TemDeveres = true, Maligno = true,
			Regras =
			[
				Ou("sangue Makyo (ou karma -50 ou pior)", "Modules/Ranks/RankQuests.dm:248",
					[Raca("Makyo")], [KarmaMax(-50)]),
				R("2M de BP base", "Modules/Ranks/RankQuests.dm:249", Bp(2_000_000)),
			],
			// DEFEITO VIVO DO DM: o Rank_Verb_Assign nao tem ramo pro King_Of_Hell, entao o mob.Rank
			// nunca vira "King_Of_Hell" e o `if("King_Of_Hell")` do OtherworldRanks.dm:65 nunca roda.
			// O cargo se conquista e nao entrega NADA. A lista abaixo e a que o DM PRETENDIA dar.
			Concede =
			[
				"(morto no DM: o cargo não recebe kit -- ver relatório)",
				"estilo Demônio", "Guardar Corpo", "Ver Mortos", "Majinizar", "Reencarnar", "Reviver", "Observar",
			],
		},

		// =========================== ESPACO ===========================
		new()
		{
			Chave = "frostlord", Global = "Frost_Demon_Lord", Nome = "Frost Demon Lord", Porta = PortaDoCargo.Prova,
			Desc = "O mais forte da estirpe. Cobra tributo de medo destruindo mundos.",
			TemDeveres = true,
			Regras =
			[
				R("sangue Frost Demon", "Modules/Ranks/RankQuests.dm:251", Raca(RacaFrostDemon, RacaFrostDemonNoPort)),
				R("10M de BP base (o lorde é o mais forte da estirpe)", "Modules/Ranks/RankQuests.dm:252", Bp(10_000_000)),
			],
			Concede = ["(nenhum: o DM não dá kit a este cargo -- ver relatório)"],
		},
		new()
		{
			Chave = "kov", Global = "King_of_Vegeta", Nome = "Rei de Vegeta", Porta = PortaDoCargo.Sucessao,
			Desc = "O trono saiyajin. Taxa até 100 zeni por hora, decreta recompensas, observa pela Bola de Cristal e "
				 + "comanda o Exército Real. Só Saiyajins.",
			TemDeveres = true,
			// Nao se reivindica: mata-se o Rei sendo Saiyajin (Murder.dm:85-96) -- ou o Principe/
			// Princesa herda. O NPC Rei tambem entrega o trono a quem o mata (PlanetPopulation.dm:203).
			Concede = ["Impostos", "Final Flash", "estilo Saiyajin", "Galick Gun"],
			// A PENDÊNCIA CADUCOU: a sucessão está LIGADA. O gancho é o `AoPerderALuta` com
			// `morreu: true` (o funil único da derrota), a regra é `Core/Ranks/PortasDeCargo.cs`
			// (`Sucessao`) e a linha de sangue é escrita pelo próprio Rei (verb `cargo_herdeiro`,
			// o `Vegeta Managament` do DM). Matar o Rei sendo Saiyajin toma o trono; não sendo, o
			// herdeiro nomeado herda; não havendo, o trono VAGA.
		},
		SoDeAdmin("kingofacronia", "King_Of_Acronia", "King Of Acronia", "Modules/Admin/Rewards.dm:310",
			"O trono de Acronia.", ["Kill Driver", "estilo Alienígena"]),
		SoDeAdmin("arconianguardian", "Arconian_Guardian", "Arconian Guardian", "Modules/Admin/Rewards.dm:311",
			"O guardião ligado à Master Emerald.",
			["Selo Superior", "Cura", "Atalho Sagrado", "estilo Alienígena", "Detectar Fragmento"]),
		SoDeAdmin("saibamenrougeleader", "Saibamen_Rouge_Leader", "Saibamen Rouge Leader", "Modules/Admin/Rewards.dm:312",
			"O líder dos Saibamen renegados.",
			["(nenhum: ganha a árvore do Espaço, mas o DM não habilita skill nenhuma para ele)"]),
		SoDeAdmin("capt", "capt", "Captain/King of Pirates", "Modules/Admin/Rewards.dm:330",
			"O rei dos piratas do espaço.", ["Death Beam", "estilo Alienígena", "Death Ball"]),
		SoDeAdmin("geti", "Geti", "Geti Star King/Queen", "Modules/Admin/Rewards.dm:331",
			"O soberano da Big Geti Star.",
			["Divisão de corpo", "Death Ball", "Paralisia", "estilo Alienígena", "Dança da Fusão"]),
		SoDeAdmin("mutany", "mutany", "Mutany Leader", "Modules/Admin/Rewards.dm:332",
			"O líder do motim.",
			["(nenhum: ganha a árvore do Espaço, mas o DM não habilita skill nenhuma para ele)"]),
		SoDeAdmin("arlian", "Arlian", "Arlian King", "Modules/Admin/Rewards.dm:333",
			"O rei de Arlia.",
			["(nenhum: o DM nem dá nome de Rank a este cargo -- ver relatório)"]),

		// ===================== OS DOIS DO ALTO ========================
		new()
		{
			Chave = "godofdestruction", Global = "God_Of_Destruction", Nome = "God of Destruction",
			Porta = PortaDoCargo.Duelo,
			Desc = "O Deus da Destruição. Não envelhece, respira no vácuo, e carrega o Hakai. "
				 + "Perder o título tira TUDO na hora.",
			Concede =
			[
				"Hakai (agarrão executor: torso abaixo de 15% e alvo 25% mais fraco)",   // GodOfDestruction.dm:19-20
				"Power Flick (peteleco: 2x um golpe pesado sem carga)",                  // GodOfDestruction.dm:225
				"Destruction God's Fury (feixe amarelo que atravessa a guarda, direto no torso)", // :258
				"Destroyer Sphere (orbe roxo que aplica Hakai no contato)",              // :301
				"Energy Nullification (apaga ataques de ki mais fracos que você; 5 min de recarga)", // :336
				"Fury (transformação 72x, aura roxa)",                                   // :18 GOD_FURY_MULT
				"God's Temper (10 danos seguidos sem bloquear = +10% de ataque/defesa física por 10s)", // :22-24
				"não envelhece e respira no vácuo enquanto portar o título",             // :105
				"o Power of Destruction desperta (ou, no caminho do Instinto, o kit emprestado a 25%)", // :95-102
				"aura e cabelo púrpura (godki.dm:280, HairObject.dm:68) e a Língua dos Deuses (WishTable.dm:33)",
			],
			// ============================ A LISTA ZEROU, E CADA LINHA DELA MORREU POR UM MOTIVO ============================
			// Eram TRÊS. O **duelo formal** ficou ligado inteiro (`Core/Ranks/PortasDeCargo.cs` +
			// `GameServer.CargoDuelo.cs` -- carência de 7 dias, intervalo de 2, 2 adiamentos por semana
			// com o 3º custando o título, nocaute decide, 10 min de teto); o **portão do God Ki
			// desperto** é conferido no verb de desafiar; e a última, a tarefa de **DESTRUIR UM
			// PLANETA**, caiu com os deveres de cargo.
			//
			// Ela dizia: *"o DM sorteia entre os `pspace_planets`, um registro de mundos procedurais
			// vivos, e aqui o universo é função pura da seed -- só existe o livro dos MORTOS"*. O Lorde
			// do Gelo precisou da MESMA resposta (a tarefa dele é só essa) e ela apareceu: o SETOR do
			// portador na carta estelar (`SortearMundoParaDestruir`), que é mais perto do original do
			// que uma lista global, porque o `pspace_planets` do DM também só contém o que está perto de
			// alguém. Os dois ramos do `god_task_new` estão ligados, na ordem de lá -- o mundo só entra
			// quando não há ameaça online.
			// ========================================================================================================
		},
		new()
		{
			Chave = "angel", Global = "Angel_Rank", Nome = "Angel", Porta = PortaDoCargo.Admin,
			Desc = "O Anjo. Carrega o Ultra Instinto por definição -- e é dele que a corrente de ensino desce.",
			Concede =
			[
				"Ultra Instinto automático ao portar o título (UltraInstinct.dm:99)",
				"ENSINA o Ultra Instinto em cadeia: o aluno precisa estar adjacente, vivo, sem o Ultra Ego "
				+ "e com maestria de God Ki 70%+ -- e quem aprende também pode ensinar (UltraInstinct.dm:109)",
				"aura branca e cabelo branco-prata pelo RANK (godki.dm:272, HairObject.dm:63)",
				"a Língua dos Deuses, que nunca se esquece (WishTable.dm:33)",
			],
			Pendencias =
			[
				"o rank não tem requisito nenhum no DM: é dado pelo verb Give Rank do admin (Rewards.dm:294)",
				"paths EXCLUSIVOS: quem já trilha o Ultra Ego não recebe o Instinto junto com o título (Rewards.dm:296)",
			],
		},
	];

	// ---------------------------------------------------------------------------------------
	// fabricas dos cargos repetidos (a tabela acima ja e longa demais pra repetir isto 4x2 vezes)
	// ---------------------------------------------------------------------------------------
	private static RankDef Kaio(string chave, string global, string nome, string desc, string[] concede) => new()
	{
		Chave = chave, Global = global, Nome = nome, Desc = desc, Porta = PortaDoCargo.Prova,
		TemDeveres = true, Sabedoria = true, DoOutroMundo = true,
		Regras =
		[
			Ou("despertar o God Ki (ou sangue Kai)", "Modules/Ranks/RankQuests.dm:227",
				[Raca("Kai")], [GodKiDesperto()]),
			R("karma 50+", "Modules/Ranks/RankQuests.dm:228", KarmaMin(50)),
		],
		Concede = concede,
	};

	private static RankDef AnciaoCardeal(string chave, string global, string nome, string fonte) => new()
	{
		Chave = chave, Global = global, Nome = nome, Porta = PortaDoCargo.Nomeacao,
		Desc = "Guardião de 1 Esfera do Dragão de Namek.",
		Concede = ["estilo Namek", "Makankosappo", "Enkumei", "Liberar Potencial", "Telepatia", "Cura"],
		// A NOMEAÇÃO ESTÁ LIGADA (`Nomeacao`, em `Core/Ranks/PortasDeCargo.cs`): o Grande Ancião
		// oferece o assento pelo verb `cargo_nomear`, o convidado aceita, e o kit dos quatro chega
		// pela dádiva. **Duas coisas do DM não vieram junto de propósito, e as duas são bugs dele**:
		// `North_Elder=key` (NamekRanks.dm:63) grava a `key` de QUEM NOMEIA -- e o `Rank_Verb_Assign`
		// compara os quatro globais por SIGNATURE (RankAssign.dm:95) --, e `if(M.Rank=="Namekian
		// Elder")` (:58) só deixa nomear quem JÁ é Ancião, porta trancada por dentro num servidor novo.
		Pendencias =
		[
			$"o DM não dá requisito próprio ao assento -- ele também sai do verb Give Rank do admin em {fonte}",
			"no DM os quatro compartilham o mesmo mob.Rank (\"Namekian Elder\"), então a árvore de skills não os distingue",
		],
	};

	private static RankDef SoDeAdmin(string chave, string global, string nome, string fonte, string desc, string[] concede) => new()
	{
		Chave = chave, Global = global, Nome = nome, Desc = desc, Porta = PortaDoCargo.Admin,
		Concede = concede,
		Pendencias = [$"o DM não escreve requisito nenhum: o cargo sai do verb Give Rank do admin ({fonte})"],
	};

	// ---------------------------------------------------------------------------------------
	// consulta
	// ---------------------------------------------------------------------------------------
	public static RankDef? Get(string chave) =>
		Array.Find(Todos, r => string.Equals(r.Chave, chave, StringComparison.OrdinalIgnoreCase));

	/// <summary>Os que dá para tomar pelo verb de reivindicar. Os outros 14 têm dono por outra via.</summary>
	public static IEnumerable<RankDef> Reivindicaveis => Todos.Where(r => r.Porta == PortaDoCargo.Prova);

	/// <summary>
	/// O QUE FALTA pra reivindicar. Nulo = apto.
	///
	/// Devolve TEXTO e nao um booleano de proposito: "karma 25+" e "sangue Namekuseijin" mandam o
	/// jogador fazer coisas opostas, e o original ja entregava a frase pronta. Um "nao pode" seco
	/// faria a pessoa tentar de novo sem saber o que mudar.
	///
	/// A PORTA VEM ANTES DO REQUISITO. Um cargo de admin nao "falta karma": ele nao se reivindica,
	/// ponto -- e dizer isso e mais util do que listar um requisito que nao existe.
	/// </summary>
	public static string? OqueFalta(RankDef r, FichaDeRank f)
	{
		switch (r.Porta)
		{
			case PortaDoCargo.Admin:
				return "este cargo não se reivindica: no original ele é dado por um administrador";
			// AS TRES FRASES ABAIXO DEIXARAM DE SER SO UMA RECUSA. Enquanto as portas não existiam,
			// dizer "não se reivindica" era tudo o que havia de honesto; agora as três têm mecanismo
			// (ver `PortasDeCargo.cs`), então a frase diz COMO -- que é a mesma regra do resto deste
			// método: um "não pode" seco faz a pessoa tentar de novo sem saber o que mudar.
			case PortaDoCargo.Duelo:
				return "este cargo não se reivindica: toma-se em DUELO FORMAL pelo título "
					 + "(verb Challenge God of Destruction; exige God Ki desperto)";
			case PortaDoCargo.Sucessao:
				return "este cargo não se reivindica: toma-se MATANDO quem o carrega, sendo do sangue "
					 + "-- ou herda-se, sendo da linha de sucessão";
			case PortaDoCargo.Nomeacao:
				return "este cargo não se reivindica: é NOMEADO pelo Grande Ancião de Namek";
		}

		foreach (Regra g in r.Regras)
			if (!g.Vale(f)) return g.Texto;

		return null;
	}

	// ---------------------------------------------------------------------------------------
	// conferencia contra o DM
	// ---------------------------------------------------------------------------------------
	/// <summary>
	/// Compara esta tabela com o `cargos.json` que o `DmRankScanner` acabou de extrair do DM.
	///
	/// POR QUE CONFERIR EM VEZ DE CARREGAR: porque a tabela acima carrega duas coisas que o JSON
	/// nao tem como carregar. A primeira e a SEMANTICA exata -- o extrator ACHATA as alternativas
	/// aninhadas, e o requisito do Kaioshin sairia de "Grand Kai OU (sangue Kai E maestria 33%)"
	/// para "Grand Kai OU sangue Kai OU maestria 33%", que da o cargo a um Kai qualquer. A segunda
	/// e a decisao de o que fazer com o que o port ainda nao mede: isso e escolha de projeto, nao
	/// dado do DM. Entao o JSON vira GUARDA: se alguem mexer num numero no DM, esta funcao acusa
	/// no proximo build em vez de a divergencia dormir no jogo.
	/// </summary>
	public static List<string> Conferir(string json)
	{
		var achados = new List<string>();
		using JsonDocument doc = JsonDocument.Parse(json);
		if (!doc.RootElement.TryGetProperty("cargos", out JsonElement cargos)) return ["cargos.json sem a lista \"cargos\""];

		var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (JsonElement c in cargos.EnumerateArray())
		{
			string chave = c.GetProperty("chave").GetString() ?? "";
			string global = c.GetProperty("global").GetString() ?? "";
			vistos.Add(chave);

			RankDef? meu = Get(chave);
			if (meu == null) { achados.Add($"o DM tem o cargo '{chave}' ({global}) e a tabela nao"); continue; }
			if (!string.Equals(meu.Global, global, StringComparison.Ordinal))
				achados.Add($"{chave}: global '{meu.Global}' na tabela, '{global}' no DM");

			bool claim = c.GetProperty("reivindicavel").GetBoolean();
			if (claim != (meu.Porta == PortaDoCargo.Prova))
				achados.Add($"{chave}: RQ_CLAIMABLE={claim} no DM, porta={meu.Porta} na tabela");

			// os NUMEROS: karma, BP, zeni e maestria. Sao eles que quebram o jogo em silencio.
            foreach (JsonElement rq in c.GetProperty("requisitos").EnumerateArray())
			{
				string campo = rq.GetProperty("campo").GetString() ?? "";
				double valor = rq.GetProperty("valor").GetDouble();
				if (campo is not ("karma" or "bp" or "zenni" or "godki")) continue;
				if (campo == "godki" && rq.GetProperty("op").GetString() != "maestria >=") continue;

				CampoDeRank alvo = campo switch
				{
					"karma" => CampoDeRank.Karma,
					"bp" => CampoDeRank.Bp,
					"zenni" => CampoDeRank.Zeni,
					_ => CampoDeRank.GodKi,
				};
				bool tem = meu.Regras.Any(g => g.Opcoes.Any(o => o.Any(e => e.Campo == alvo && Math.Abs(e.Valor - valor) < 0.001)));
				if (!tem) achados.Add($"{chave}: o DM exige {campo} {valor:N0} ({rq.GetProperty("fonte").GetString()}) e a tabela nao tem esse numero");
			}
		}

		foreach (RankDef r in Todos)
			if (!vistos.Contains(r.Chave)) achados.Add($"a tabela tem o cargo '{r.Chave}' e o DM nao -- invencao?");

		return achados;
	}
}
