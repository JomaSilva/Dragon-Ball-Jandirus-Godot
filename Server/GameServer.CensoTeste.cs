using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--censoteste` -- O RELATORIO DO CATALOGO E AS DEZENOVE FOLHAS DO LOTE G6.
///
/// ============================ ELA COMECA IMPRIMINDO O RELATORIO ============================
/// A especificacao pede, com todas as letras, *"um relatorio do proprio catalogo -- quantas skills
/// tem efeito, quantas so dao verbo, quantas estao mudas"*. Ele sai NA PRIMEIRA LINHA de toda
/// rodada, antes de qualquer afirmacao, porque um numero que so aparece quando alguem lembra de
/// procurar e um numero que envelhece em silencio. Depois dele vem a checagem que o torna confiavel:
/// nenhum verb pode ficar MUDO sem estar catalogado.
/// ==========================================================================================
///
/// ============================ O QUE ELA TENTA REPROVAR ============================
///  1. COBERTURA -- um verb que o jogo concede e que nao esta em nenhuma das tres listas
///     (portado / outro canal / esperando um sistema) e uma divida invisivel. A familia injeta um
///     verb inventado e exige que ele saia como `SemCobertura`.
///  2. CANAL     -- dizer "o `Fly` e atendido pelo `voar`" so vale enquanto o `voar` existir. Cada
///     canal declarado e procurado no ROTEADOR DE VERBOS de verdade, lendo o fonte.
///  2b. AS DUAS DIRECOES -- a boca que fala com o jogador (`SistemaQueFalta`) tem que separar a folha
///     que so SOMA NUMERO (pronta) da folha cujo verbo NAO TEM CORPO (muda, com o sistema nomeado).
///     Afirmar so um dos lados fica verde numa funcao que responda sempre a mesma coisa -- e as duas
///     respostas erradas ja custaram caro: `null` de mais foi o Mafuba anunciado sem botao.
///  3. GATE      -- quem nao comprou a skill nao usa, e a recusa nao cobra Ki.
///  4. RAIOS     -- o degrau `beamskill >= 100` (o defeito do `== 100`, que aqui aparece TRES
///     vezes), a escada de custo de 30x, e o Boom Wave, cujo aluguel CAI com a pericia.
///  5. BUFFS     -- o Focus tem que subir o poder E o gasto pelo MESMO fator; e o Energy Shield tem
///     que DIVIDIR o `superkiarmorMod` ao sair (o canal multiplicativo que o motor nao tinha).
///  6. SOPRO     -- o Kiai pega quem esta na frente e NAO quem esta atras; a recarga e uma so pras
///     quatro; a Onda de Choque apaga tiro; a Deflexao TROCA O DONO do tiro (sem isso ela devolve
///     um tiro que atravessa quem atirou -- o defeito do proprio DM).
///  7. PUNHOS    -- o Punho arremessa no fim e o Furacao NAO arremessa no meio.
///  8. SERVICO   -- a cura cura enquanto o alvo estiver colado e para quando ele sai.
/// ==================================================================================
/// </summary>
public partial class GameServer
{
	private int _cenOk, _cenFalhou;

	private void AfirmarCen(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _cenOk++; GD.Print($"[censo]   OK    {oque}"); return; }
		_cenFalhou++;
		GD.PrintErr($"[censo]   FALHA {oque}   {detalhe}");
	}

	/// <summary>
	/// OS DEZENOVE E DE ONDE VEM CADA UM. Escrito aqui, e nao lido do json, pelo mesmo motivo da
	/// tabela irma da `--arsenalteste`: ler o par do proprio arquivo faria a bancada concordar
	/// consigo mesma, e o dia em que o extrator parasse de escrever o degrau os dois lados sumiriam
	/// juntos com o teste verde sobre uma tecnica inalcancavel.
	/// </summary>
	private static readonly (string Verbo, string Path, int Nivel)[] PorDegrauG6 =
	[
		("Focus", "/datum/skill/mind/Basic_Ki_Circulation", 30),
		("Efficiency", "/datum/skill/mind/Basic_Ki_Efficiency", 30),
		("Energy_Shield", "/datum/skill/mind/Basic_Defense_Mastery", 10),
		("Full_Power", "/datum/skill/fullpower", 1),
		("Kiai", "/datum/skill/mind/Ki_Unlocked", 10),
		("Shockwave", "/datum/skill/mind/Basic_Kiai_Mastery", 75),
		("Deflection", "/datum/skill/mind/Basic_Kiai_Mastery", 75),
		("Explosive_Roar", "/datum/skill/mind/Basic_Kiai_Mastery", 75),
		("Assess_Ki_Skill", "/datum/skill/mind/Basic_Ki_Awareness", 50),
	];

	/// <summary>Os dez que uma SKILL concede -- SETE deles saem de um KIT DE CARGO.</summary>
	private static readonly (string Verbo, string Path)[] PorSkillG6 =
	[
		("Kamehameha", "/datum/skill/rank/Kamehameha"),            // Turtle
		("GalicGun", "/datum/skill/rank/GalicGun"),                // Rei de Vegeta
		("Death_Beam", "/datum/skill/rank/Death_Beam"),            // Capitao
		("Dodompa", "/datum/skill/rank/Dodompa"),                  // Crane
		("Kikoho", "/datum/skill/general/kikoho"),                 // Crane
		("Enkumei", "/datum/skill/Enkumei"),                       // Grande Ancia~o e os 4 cardeais
		("Heal", "/datum/skill/ki/Heal"),                          // cinco cargos
		("Boom_Wave", "/datum/skill/ki/Boom_Wave"),
		("Wolf_Fang_Fist", "/datum/skill/MartialSkill/Wolf_Fang_Fist"),
		("Wolf_Fang_Hurricane", "/datum/skill/MartialSkill/Wolf_Hurricane"),
	];

	public void RodarBancadaDoCenso()
	{
		_cenOk = _cenFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[censo] ================ O CATALOGO DE SKILLS E O LOTE G6 ================");

		try
		{
			ORelatorioDoCatalogo();
			ACoberturaNaoTemBuraco();
			OCensoRespondeNasDuasDirecoes();
			OsCanaisDeclaradosExistem();
			OCatalogoConheceOsDezenove();
			OsSeisRaiosNomeados();
			OsQuatroBuffs();
			OSopro();
			OsDoisPunhos();
			OServico();
		}
		finally
		{
			LimparTudoDaBancada();
		}

		GD.Print($"[censo] ================ {_cenOk} passaram, {_cenFalhou} falharam ================");
	}

	// =====================================================================
	// 1) O RELATORIO -- impresso a cada rodada
	// =====================================================================
	private CensoDeSkills.Relatorio? _relatorio;

	private void ORelatorioDoCatalogo()
	{
		GD.Print("[censo] -- 1) O RELATORIO DO CATALOGO (a prova que a especificacao pediu)");

		if (_skills == null) { AfirmarCen("o catalogo de skills esta carregado", false); return; }

		// OS DEGRAUS ENTRAM NAS DUAS PONTAS: os verbos que um nivel concede E as skills que um nivel
		// acende (`enableskill` no effector, Mind.dm:186). Sem a segunda, 32 folhas saiam como "sem
		// acendedor" num servidor que sabia quem as acende.
		_relatorio = CensoDeSkills.Levantar(_skills, RegrasDeNivel.VerbosDeDegrau, RegrasDeNivel.DestravadasPorDegrau);
		foreach (string linha in CensoDeSkills.Texto(_relatorio)) GD.Print($"[censo] {linha}");

		AfirmarCen("o relatorio conta folhas de verdade (mais de 200)", _relatorio.Folhas > 200,
				   $"{_relatorio.Folhas}");
		AfirmarCen("...e conta verbos dos DOIS caminhos (skill E degrau)",
				   _relatorio.Verbos.Exists(l => l.PorSkill > 0) && _relatorio.Verbos.Exists(l => l.PorDegrau > 0));

		// A CONTA TEM QUE FECHAR. Sem isto uma situacao nova (um quinto estado) sumiria da soma e o
		// relatorio ficaria bonito e errado.
		int soma = _relatorio.VerbosPortados + _relatorio.VerbosPorCanal
				 + _relatorio.VerbosEsperando + _relatorio.VerbosSemCobertura;
		AfirmarCen("as quatro situacoes somam o total de verbos", soma == _relatorio.VerbosTotal,
				   $"{soma} vs {_relatorio.VerbosTotal}");
	}

	// =====================================================================
	// 2) COBERTURA -- a checagem que impede a divida de virar invisivel
	// =====================================================================
	/// <summary>
	/// NENHUM VERB MUDO SEM CATALOGO. E a afirmacao mais importante desta bancada e a unica que vai
	/// reprovar sozinha no futuro: no dia em que o extrator trouxer uma skill nova com um verb novo,
	/// esta linha fica vermelha ate alguem escrever de que sistema ele depende.
	///
	/// O DEFEITO INJETADO E DE VERDADE, e nao um comentario: um verb inventado entra pela mesma
	/// porta dos degraus e TEM que sair como `SemCobertura`. Sem isso, um `Levantar` que classificasse
	/// tudo como "esperando" passaria neste teste com nota cheia.
	/// </summary>
	private void ACoberturaNaoTemBuraco()
	{
		GD.Print("[censo] -- 2) COBERTURA: nenhum verb mudo fica fora do catalogo");
		if (_skills == null || _relatorio == null) { AfirmarCen("ha relatorio pra conferir", false); return; }

		AfirmarCen($"ZERO verbos sem cobertura (de {_relatorio.VerbosTotal})",
				   _relatorio.VerbosSemCobertura == 0, string.Join(" | ", _relatorio.SemCobertura));

		var comIntruso = new List<string>(RegrasDeNivel.VerbosDeDegrau) { "Verb_Inventado_Da_Bancada" };
		CensoDeSkills.Relatorio sujo = CensoDeSkills.Levantar(_skills, comIntruso);
		AfirmarCen("...e um verb inventado E PEGO (o detector detecta)",
				   sujo.VerbosSemCobertura == 1 && sujo.SemCobertura[0] == "Verb_Inventado_Da_Bancada",
				   $"{sujo.VerbosSemCobertura} sem cobertura");

		// ============================ ESTA LINHA JA FOI UM `>= 20`, E O PROGRESSO A DERRUBOU ============================
		// O AGRUPAMENTO E O PRODUTO desta camada: 100+ verbos soltos nao dizem nada, "vinte e nove
		// esperam o mesmo motor de combo" diz onde esta o proximo lote.
		//
		// A versao anterior exigia `PorSistema[0].Quantos >= 20` -- um retrato da divida no dia em
		// que a bancada foi escrita, quando o maior grupo (o motor de combo) tinha 29 verbos. O lote
		// G7 fechou catorze punhos e o grupo caiu pra 15: a bancada ficou VERMELHA porque o jogo
		// melhorou, que e o pior tipo de checagem que existe -- ela pune exatamente o trabalho que
		// pediu.
		//
		// O que ela SEMPRE quis afirmar e que o relatorio AGRUPA em vez de despejar uma lista: que ha
		// varios sistemas nomeados, que o maior deles reune mais de um verbo, e que a soma dos grupos
		// bate com o total de mudos (ou seja, nenhum mudo escapa do agrupamento). Isso continua
		// valendo quando o ultimo verb for portado -- e ai o `PorSistema` fica VAZIO, que e o unico
		// estado em que esta linha deve mudar de novo.
		// ============================================================================================================
		int somaDosGrupos = 0;
		foreach ((string _, int quantos) in _relatorio.PorSistema) somaDosGrupos += quantos;

		AfirmarCen("os mudos estao AGRUPADOS por sistema, e a soma dos grupos bate com o total",
				   _relatorio.PorSistema.Count > 10
				   && _relatorio.PorSistema[0].Quantos > 1
				   && somaDosGrupos == _relatorio.VerbosEsperando,
				   $"{_relatorio.PorSistema.Count} sistemas, maior com {_relatorio.PorSistema[0].Quantos}, "
				   + $"soma {somaDosGrupos} vs {_relatorio.VerbosEsperando} mudos");

		// O RECORTE DOS CARGOS: era a queixa da camada anterior ("40 dao verbo, 8 vivos").
		AfirmarCen("os kits de cargo entregam mais verbos VIVOS que os 8 da camada anterior",
				   _relatorio.VerbosDeCargoVivos > 8,
				   $"{_relatorio.VerbosDeCargoVivos} de {_relatorio.VerbosDeCargo}");
	}

	// =====================================================================
	// 2b) O CENSO RESPONDE NAS DUAS DIRECOES
	// =====================================================================
	/// <summary>
	/// ============================ UMA DIRECAO SO FICA VERDE NUM SISTEMA MORTO ============================
	/// O `CensoDeSkills.SistemaQueFalta` e a boca que fala com o JOGADOR: e ela que decide se o painel
	/// do cargo lista uma skill entre as ENTREGUES ou entre as *"ainda mudas neste servidor"*, e e ela
	/// que escreve o recado de posse. Duas respostas, e cada uma tem um jeito proprio de estar errada:
	///
	///   * **responder `null` de mais** -- e o defeito de ontem, o que fez o painel anunciar o Mafuba
	///     enquanto nao havia botao nenhum. Uma funcao que devolvesse `null` sempre passaria em todas
	///     as afirmacoes de "esta pronta" que ja existem nesta bancada e na `--cargoportas`;
	///   * **responder um sistema de mais** -- o espelho: as 200 e tantas folhas que so somam numero
	///     (`physoffBuff += 0.1`) viveriam marcadas como quebradas, e o painel pediria desculpa por
	///     uma skill que funciona. Uma funcao que devolvesse sempre um sistema passaria em todas as
	///     afirmacoes de "esta muda".
	///
	/// Por isso esta familia afirma as DUAS pontas, e afirma nas duas escalas: uma TABELA VERDADE com
	/// folhas sinteticas (o classificador reage ao que recebe, e nao a nomes que ele conheca) e a
	/// varredura do CATALOGO INTEIRO, onde as duas populacoes tem que existir e nenhuma pode cair no
	/// lado errado. Cada metade e o defeito injetado da outra.
	/// ==================================================================================================
	/// </summary>
	private void OCensoRespondeNasDuasDirecoes()
	{
		GD.Print("[censo] -- 2b) O CENSO NAS DUAS DIRECOES: passiva continua passiva, muda continua muda");

		if (_skills == null) { AfirmarCen("o catalogo de skills esta carregado", false); return; }

		// ---- A TABELA VERDADE, com folhas que so existem aqui ----
		//
		// SINTETICAS DE PROPOSITO: com skills de verdade, uma resposta certa pode vir do nome, do
		// caminho ou de qualquer coisa que o classificador conheca por acaso. Estas seis nao existem
		// no DM, entao a unica coisa que o censo tem pra olhar e o CONTEUDO delas.
		var soBuff = new Skill { Path = "/datum/skill/bancada/SoBuff", Nome = "So Buff" };
		soBuff.Buffs["physoffBuff"] = 0.1;

		var comVerboPortado = new Skill
		{
			Path = "/datum/skill/bancada/ComCorpo", Nome = "Com Corpo", Verbos = ["Mafuba"],
		};
		var comVerboCatalogado = new Skill
		{
			Path = "/datum/skill/bancada/Catalogada", Nome = "Catalogada", Verbos = ["Buu_Absorb"],
		};
		var comVerboDesconhecido = new Skill
		{
			Path = "/datum/skill/bancada/Desconhecida", Nome = "Desconhecida",
			Verbos = ["Verbo_Que_Nunca_Existiu"],
		};
		var galho = new Skill { Path = "/datum/skill/tree/bancada", Nome = "Galho", Arvore = true, Verbos = ["Verbo_Que_Nunca_Existiu"] };
		var mudaDeNascenca = new Skill { Path = "/datum/skill/bancada/Muda", Nome = "Muda" };

		AfirmarCen("DIRECAO 1: folha que so SOMA NUMERO e anunciada como PRONTA (ela faz o que o DM manda)",
				   CensoDeSkills.SistemaQueFalta(soBuff) == null, CensoDeSkills.SistemaQueFalta(soBuff) ?? "");
		AfirmarCen("...e folha cujo verbo TEM CORPO tambem",
				   CensoDeSkills.SistemaQueFalta(comVerboPortado) == null,
				   CensoDeSkills.SistemaQueFalta(comVerboPortado) ?? "");

		AfirmarCen("DIRECAO 2: folha com verbo SEM CORPO e anunciada como muda, com o sistema NOMEADO",
				   CensoDeSkills.SistemaQueFalta(comVerboCatalogado) is { } falta1
				   && falta1.Contains("absorcao", StringComparison.OrdinalIgnoreCase),
				   CensoDeSkills.SistemaQueFalta(comVerboCatalogado) ?? "(null -- ela mentiu)");
		AfirmarCen("...e verbo que nem catalogado esta cai no generico, e nao em `null`",
				   CensoDeSkills.SistemaQueFalta(comVerboDesconhecido) == "um sistema que este port ainda nao tem",
				   CensoDeSkills.SistemaQueFalta(comVerboDesconhecido) ?? "(null -- ela mentiu)");

		AfirmarCen("ARVORE nao e folha: galho nao promete botao nenhum",
				   CensoDeSkills.SistemaQueFalta(galho) == null);
		AfirmarCen("folha MUDA DE NASCENCA (o DM tambem nao faz nada) nao acusa sistema faltando -- "
				   + "nao ha promessa a quebrar",
				   CensoDeSkills.SistemaQueFalta(mudaDeNascenca) == null,
				   CensoDeSkills.SistemaQueFalta(mudaDeNascenca) ?? "");

		// ---- E AS DUAS DIRECOES NO CATALOGO INTEIRO ----
		//
		// A tabela verdade prova o classificador; esta varredura prova que ele esta CERTO nas 300 e
		// tantas folhas de verdade -- e, principalmente, que as duas populacoes EXISTEM. Uma afirmacao
		// sobre um conjunto vazio e verde por vacuidade, que e o modo preferido deste projeto de
		// enganar a si mesmo.
		var passivas = new List<Skill>();
		var comVerboMudo = new List<Skill>();
		foreach (Skill s in _skills.Todas)
		{
			if (s.Arvore) continue;
			bool temPassivo = s.Buffs.Count > 0 || s.Mults.Count > 0 || s.Genes.Count > 0
							  || s.Flags.Count > 0 || s.Estilo.Length > 0 || s.Escolhas.Length > 0;
			if (temPassivo) { passivas.Add(s); continue; }
			if (s.Verbos.Length == 0) continue;   // muda de nascenca: nao e nem uma coisa nem outra

			bool algumVivo = s.Verbos.Any(
				v => Tecnicas.Get(v) is { Modo: not Modo.NaoPortada }
					 || CensoDeSkills.PorOutroCanal.ContainsKey(v));
			if (!algumVivo) comVerboMudo.Add(s);
		}

		AfirmarCen($"o catalogo tem as DUAS populacoes ({passivas.Count} folhas passivas, "
				   + $"{comVerboMudo.Count} com verbo sem corpo) -- nenhuma afirmacao abaixo e vazia",
				   passivas.Count > 100 && comVerboMudo.Count > 10);

		var passivasChamadasDeMudas = passivas
			.Where(s => CensoDeSkills.SistemaQueFalta(s) != null).Select(s => s.Nome).ToList();
		AfirmarCen("NENHUMA folha passiva e anunciada como quebrada (o painel nao pede desculpa por "
				   + "skill que funciona)",
				   passivasChamadasDeMudas.Count == 0,
				   string.Join(", ", passivasChamadasDeMudas.Take(8)));

		var mudasChamadasDeProntas = comVerboMudo
			.Where(s => CensoDeSkills.SistemaQueFalta(s) == null).Select(s => s.Nome).ToList();
		AfirmarCen("NENHUMA folha de verbo mudo e anunciada como pronta (era este o defeito do Mafuba)",
				   mudasChamadasDeProntas.Count == 0,
				   string.Join(", ", mudasChamadasDeProntas.Take(8)));

		// ---- E AS DUAS PONTAS EM SKILLS DE VERDADE, uma de cada lado, com o MOTIVO conferido ----
		//
		// A resposta certa pelo motivo errado e o que a tabela verdade nao pega: uma skill pode sair
		// como pronta porque o censo achou um verbo com corpo QUANDO ela nem verbo tem.
		Skill? ritual = _skills.Get("/datum/skill/rank/Ritual_of_Might");
		Skill? superior = _skills.Get("/datum/skill/rank/SuperiorSeal");
		AfirmarCen("as duas testemunhas de verdade existem no catalogo", ritual != null && superior != null);
		if (ritual == null || superior == null) return;

		AfirmarCen("`Ritual of Might` e passiva DE VERDADE (soma numero e nao da verbo nenhum)",
				   ritual.Buffs.Count > 0 && ritual.Verbos.Length == 0,
				   $"buffs={ritual.Buffs.Count} verbos={ritual.Verbos.Length}");
		AfirmarCen("...e por isso o censo a da como PRONTA", CensoDeSkills.SistemaQueFalta(ritual) == null,
				   CensoDeSkills.SistemaQueFalta(ritual) ?? "");

		AfirmarCen("`Superior Seal` da verbo E nao soma numero nenhum",
				   superior.Verbos.Length > 0 && superior.Buffs.Count == 0,
				   $"buffs={superior.Buffs.Count} verbos={string.Join(",", superior.Verbos)}");
		AfirmarCen("...e por isso o censo a da como MUDA, nomeando a magia",
				   CensoDeSkills.SistemaQueFalta(superior) is { } f2
				   && f2.Contains("magia", StringComparison.OrdinalIgnoreCase),
				   CensoDeSkills.SistemaQueFalta(superior) ?? "(null -- ela voltou a mentir)");
	}

	// =====================================================================
	// 3) OS CANAIS DECLARADOS EXISTEM MESMO
	// =====================================================================
	/// <summary>
	/// "ISSO E ATENDIDO POR OUTRO CANAL" TEM QUE SER VERIFICAVEL, senao a tabela vira uma lista de
	/// desculpas que ninguem revisa. Cada canal declarado e procurado NO FONTE dos roteadores de
	/// verbo -- o mesmo truque que a `--kideponta` usa pra provar que o funil da IA e o do jogador.
	///
	/// (E o mesmo truque tem a mesma armadilha, que ja mordeu este projeto uma vez: procurar por uma
	/// assinatura longa demais devolve zero linhas e a bancada reprova por nao achar o METODO, e nao
	/// por achar defeito nele. Por isso aqui a busca e por `"<id>"` -- o literal, curto e estavel.)
	/// </summary>
	private void OsCanaisDeclaradosExistem()
	{
		GD.Print("[censo] -- 3) OS CANAIS DECLARADOS EXISTEM NO ROTEADOR");

		string[] fontes =
		[
			"Server/GameServer.Raciais.cs", "Server/GameServer.Verbos.cs",
			"Server/GameServer.Customizadas.cs", "Server/GameServer.CargoPortas.cs",
			"Server/GameServer.SalaDoTempo.cs", "Server/GameServer.Tecnicas.G2.cs",
			"Server/GameServer.Destruicao.cs", "Server/GameServer.Zanzoken.cs",
			"Net/Protocol.cs", "Core/Forms/Formas.cs",
			// O ROTEADOR DE OPCODES mora no `GameServer.cs` (o `switch` do `Handle`), e nao num
			// arquivo de tema: e la que `Protocol.C2S.Zanzoken` e atendido.
			"Server/GameServer.cs",
		];

		string tudo = "";
		foreach (string f in fontes) tudo += LerFonteDaBancada(f);
		AfirmarCen("os fontes do roteador foram lidos", tudo.Length > 10_000, $"{tudo.Length} chars");

		var sumidos = new List<string>();
		foreach ((string verb, string canal) in CensoDeSkills.PorOutroCanal)
		{
			// `forma:xxx` aponta pro CATALOGO DE FORMAS e nao pro roteador: o que tem que existir e
			// o id da forma, porque e ele que o canal de transformacao aceita.
			string alvo = canal.StartsWith("forma:") ? canal["forma:".Length..] : canal;

			// UM CANAL COM PONTO E UM OPCODE (`C2S.Zanzoken`), e opcode nao e string: ele existe no
			// fonte como IDENTIFICADOR. Procurar `"C2S.Zanzoken"` entre aspas nunca acharia nada, e a
			// bancada reprovaria por procurar errado -- que e o mesmo engano que ja fez a
			// `--kideponta` acusar um metodo de sumir quando so a assinatura dele tinha mudado.
			bool achou = alvo.Contains('.')
				? tudo.Contains(alvo, StringComparison.Ordinal)
				: tudo.Contains($"\"{alvo}\"", StringComparison.Ordinal);
			if (!achou) sumidos.Add($"{verb} -> {canal}");
		}
		AfirmarCen($"os {CensoDeSkills.PorOutroCanal.Count} canais declarados existem no codigo",
				   sumidos.Count == 0, string.Join(" | ", sumidos));

		// O DEFEITO INJETADO: um canal que NAO existe tem que ser pego pela mesma busca.
		AfirmarCen("...e um canal inventado seria pego",
				   !tudo.Contains("\"canal_que_nunca_existiu\"", StringComparison.Ordinal));
	}

	/// <summary>Le um fonte do projeto pra bancada. Vazio se nao achar -- quem chama afirma.</summary>
	private static string LerFonteDaBancada(string relativo)
	{
		using Godot.FileAccess? f = Godot.FileAccess.Open($"res://{relativo}", Godot.FileAccess.ModeFlags.Read);
		return f?.GetAsText() ?? "";
	}

	// =====================================================================
	// 4) O CATALOGO E O GATE
	// =====================================================================
	private void OCatalogoConheceOsDezenove()
	{
		GD.Print("[censo] -- 4) O CATALOGO CONHECE OS DEZENOVE, E O GATE CONTINUA FECHADO");

		var todos = new List<string>();
		foreach ((string v, _, _) in PorDegrauG6) todos.Add(v);
		foreach ((string v, _) in PorSkillG6) todos.Add(v);

		AfirmarCen("os dezenove do lote sao exatamente os dezenove desta bancada",
				   todos.Count == 19 && todos.TrueForAll(EhDoLoteG6), $"{todos.Count} verbos");

		var mudas = todos.FindAll(v => Tecnicas.Get(v)!.Modo == Modo.NaoPortada);
		AfirmarCen("...e nenhum deles continua marcado como NAO-PORTADO",
				   mudas.Count == 0, string.Join(" | ", mudas));

		// O DEFEITO INJETADO: uma tecnica que este lote NAO portou nao pode entrar por engano.
		// ERA A `Death_Ball`, e ela foi portada pelo lote G12 (`GameServer.Tecnicas.G12.cs`) -- esta linha
		// ficou vermelha por isso. O exemplo agora e o `Reincarnate_Mob`, que depende da reencarnacao
		// (sistema ausente, `CensoDeSkills.SistAlem`) e nao esta em lote nenhum.
		AfirmarCen("...e um verbo que o lote NAO portou (Reincarnate_Mob) continua fora dele",
				   !EhDoLoteG6("Reincarnate_Mob") && Tecnicas.Get("Reincarnate_Mob")!.Modo == Modo.NaoPortada);

		Vec2 chao = CorredorLivre(24);
		ServerPlayer nu = Forjar("SemSkillG6", chao, bp: 5_000);
		nu.Facing = Facing.East;

		EscutaDeAvisos = [];
		double kiAntes = nu.Ficha.Ki;
		UsarHabilidade(nu, "Kamehameha");
		AfirmarCen("quem NAO comprou a skill ouve \"voce nao sabe\" e nao abre canal nenhum",
				   !_canais.ContainsKey(nu.Id)
				   && EscutaDeAvisos.Exists(a => a.Contains("nao sabe", StringComparison.OrdinalIgnoreCase)),
				   string.Join(" | ", EscutaDeAvisos));
		AfirmarCen("...e a recusa nao cobra Ki nenhum", Math.Abs(nu.Ficha.Ki - kiAntes) < 0.001);

		EscutaDeAvisos.Clear();
		UsarHabilidade(nu, "Kiai");
		AfirmarCen("o mesmo vale pro sopro, que vem de DEGRAU e nao de skill comprada",
				   !TemBuff(nu, "Kiai")
				   && EscutaDeAvisos.Exists(a => a.Contains("nao sabe", StringComparison.OrdinalIgnoreCase)));
		EscutaDeAvisos = null;

		// A OUTRA METADE DA PORTA: o degrau destrava, e o menu enxerga a mesma lista.
		ServerPlayer pl = ForjarArmadoG6("Estudioso6", CorredorLivre(6), bp: 5_000);
		var faltando = new List<string>();
		foreach ((string v, _, _) in PorDegrauG6) if (!SabeTecnica(pl, v)) faltando.Add(v);
		AfirmarCen("os nove verbos de DEGRAU abrem com o degrau cruzado",
				   faltando.Count == 0, string.Join(" | ", faltando));

		var semSkill = new List<string>();
		foreach ((string v, _) in PorSkillG6) if (!SabeTecnica(pl, v)) semSkill.Add(v);
		AfirmarCen("...e os dez de SKILL abrem com a skill dada (sete deles sao kit de CARGO)",
				   semSkill.Count == 0, string.Join(" | ", semSkill));

		AfirmarCen("...e o menu do cliente enxerga os mesmos dezenove (uma lista so)",
				   todos.TrueForAll(v => TecnicasDe(pl).Contains(v)));

		LimparTudoDaBancada();
	}

	/// <summary>Um corpo com os dezenove destravados -- o ponto de partida das familias 5 a 9.</summary>
	private ServerPlayer ForjarArmadoG6(string nome, Vec2 onde, double bp)
	{
		ServerPlayer pl = Forjar(nome, onde, bp);
		var save = new NivelSave();
		foreach ((_, string path, int nivel) in PorDegrauG6) save.Skills[path] = [nivel, 0];
		pl.Niveis.DoSave(save);
		foreach ((_, string path) in PorSkillG6) pl.Livro.Dar(path);

		// O KI DOS RAIOS CAROS: o Death Beam alimenta a 25x o `kireq`. Sem isto metade das familias
		// mediria a recusa por falta de energia, que ja tem bancada propria.
		pl.Ficha.MaxKi = Math.Max(pl.Ficha.MaxKi, 5_000_000);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		return pl;
	}

	// =====================================================================
	// 5) OS SEIS RAIOS
	// =====================================================================
	private void OsSeisRaiosNomeados()
	{
		GD.Print("[censo] -- 5) OS SEIS RAIOS: CANAL, A ESCADA DE PERICIA E O ALUGUEL");

		Vec2 chao = CorredorLivre(40);
		ServerPlayer pl = ForjarArmadoG6("Raiador6", chao, bp: 50_000);
		pl.Facing = Facing.East;

		foreach (string verbo in new[] { "Kamehameha", "GalicGun", "Death_Beam", "Dodompa", "Enkumei", "Boom_Wave" })
		{
			UsarHabilidade(pl, verbo);
			AfirmarCen($"{verbo}: apertar abre o canal", _canais.ContainsKey(pl.Id));
			UsarHabilidade(pl, verbo);
			AfirmarCen($"...e apertar de novo o fecha", !_canais.ContainsKey(pl.Id));
		}

		// ============================ O DEFEITO DO `== 100`, TRES VEZES ============================
		// Kamehameha, Galick Gun e Enkumei fecham a escada com igualdade exata. Acima de 100 o verb
		// do DM nao faz NADA. Aqui os tres tem que continuar abrindo canal.
		pl.Ficha.beamskill = 137.4;
		foreach (string verbo in new[] { "Kamehameha", "GalicGun", "Enkumei" })
		{
			UsarHabilidade(pl, verbo);
			AfirmarCen($"{verbo} com beamskill 137,4 (acima de 100) AINDA carrega -- o `==100` do DM",
					   _canais.ContainsKey(pl.Id));
			UsarHabilidade(pl, verbo);
		}

		// A ESCADA DE CUSTO: o mesmo verb, pericia zero contra pericia 100, tem que custar 30x mais
		// por ciclo. E o `custoPorTiro` -- o parametro que o `Canalizar` nao tinha ate o lote G5.
		pl.Ficha.beamskill = 0;
		UsarHabilidade(pl, "Kamehameha");
		double baratoPorCiclo = _canais[pl.Id].CustoPorCiclo;
		UsarHabilidade(pl, "Kamehameha");

		pl.Ficha.beamskill = 120;
		UsarHabilidade(pl, "Kamehameha");
		double caroPorCiclo = _canais[pl.Id].CustoPorCiclo;
		UsarHabilidade(pl, "Kamehameha");

		AfirmarCen("o Kamehameha do mestre custa 30x o do novato POR CICLO",
				   Math.Abs(caroPorCiclo / baratoPorCiclo - 30) < 0.01,
				   $"{caroPorCiclo / baratoPorCiclo:0.###}x");

		// O BOOM WAVE E O UNICO QUE FICA MAIS BARATO COM O TREINO. `lastbeamcost = 15/(Ekiskill*2)`.
		pl.Ficha.Ekiskill = 1;
		UsarHabilidade(pl, "Boom_Wave");
		double bwNovato = _canais[pl.Id].CustoPorCiclo / pl.Ficha.BaseDrain();
		UsarHabilidade(pl, "Boom_Wave");

		pl.Ficha.Ekiskill = 10;
		UsarHabilidade(pl, "Boom_Wave");
		double bwMestre = _canais[pl.Id].CustoPorCiclo / pl.Ficha.BaseDrain();
		UsarHabilidade(pl, "Boom_Wave");

		AfirmarCen("o Boom Wave CAI de preco com a pericia de Ki (10x de Ekiskill = 1/10 do aluguel)",
				   Math.Abs(bwNovato / bwMestre - 10) < 0.01, $"{bwNovato / bwMestre:0.##}x");

		// A CARGA PRENDE O CORPO -- o `canmove = 0` do DM, pelo funil de vetor do port.
		UsarHabilidade(pl, "Death_Beam");
		AfirmarCen("carregar um raio nomeado ENRAIZA o corpo (o mesmo funil do Ki Wave)",
				   EnraizadoPorKi(pl.Id) && !PodeMexerOCorpo(pl));
		UsarHabilidade(pl, "Death_Beam");
		AfirmarCen("...e soltar devolve o movimento", PodeMexerOCorpo(pl));

		// O RAIO NASCE MESMO, depois da carga. `Death_Beam` tem `chargedelay = 5`.
		pl.Ficha.chargedskill = 0;
		pl.Ficha.beamskill = 0;
		UsarHabilidade(pl, "Dodompa");
		for (int i = 0; i < 400 && ProjeteisDaZona(pl.Zone.Hash).Count == 0; i++)
			TickDosCanaisDeKi(Protocol.TickSeconds);
		AfirmarCen("depois da carga o raio NASCE de verdade (nao e so um canal aberto)",
				   ProjeteisDaZona(pl.Zone.Hash).Count == 1
				   && ProjeteisDaZona(pl.Zone.Hash)[0].Tipo == TipoDeProjetil.Beam,
				   $"{ProjeteisDaZona(pl.Zone.Hash).Count} tiros");

		AfirmarCen("...e ele e PIERCER, como o `bypass = 1` dos seis verbos",
				   ProjeteisDaZona(pl.Zone.Hash).Count > 0 && ProjeteisDaZona(pl.Zone.Hash)[0].Piercer);

		LimparTudoDaBancada();
		OKikoho();
	}

	/// <summary>
	/// O KIKOHO -- a unica tecnica do lote que cobra em SANGUE, e por isso a unica que precisa de
	/// familia propria: as tres silabas sao um contador com prazo, e contador com prazo e onde este
	/// projeto ja escondeu erro (o `kikohoblasts` do DM zera por `sleep(60)` dentro do verb).
	/// </summary>
	private void OKikoho()
	{
		GD.Print("[censo] -- 5b) O KIKOHO: TRES SILABAS, E CADA UMA COBRA MAIS SANGUE");

		ServerPlayer pl = ForjarArmadoG6("Tenshinhan", CorredorLivre(12), bp: 200_000);
		pl.Facing = Facing.East;

		double vida0 = pl.Combate.Corpo.Vida();
		UsarHabilidade(pl, "Kikoho");
		double depoisDoKi = pl.Combate.Corpo.Vida();
		AfirmarCen("a primeira silaba sai e cobra sangue de quem atirou",
				   ProjeteisDaZona(pl.Zone.Hash).Count == 1 && depoisDoKi < vida0,
				   $"{depoisDoKi:0.###} vs {vida0:0.###}");

		AfirmarCen("...e o tiro carrega o `basedamage = 40` fixo do DM",
				   Math.Abs(ProjeteisDaZona(pl.Zone.Hash)[0].BaseDano - 40) < 1e-9);

		// A SEGUNDA SILABA COBRA O DOBRO. A recarga de 1 s do proprio verb e o que separa as duas.
		_blastPronto.Remove(pl.Id);
		double antesDoKo = pl.Combate.Corpo.Vida();
		UsarHabilidade(pl, "Kikoho");
		double sangueDoKo = antesDoKo - pl.Combate.Corpo.Vida();
		AfirmarCen("a segunda silaba (KO) cobra o DOBRO da primeira",
				   sangueDoKo > (vida0 - depoisDoKi) * 1.5, $"{sangueDoKo:0.###} vs {vida0 - depoisDoKi:0.###}");

		AfirmarCen("...e o segundo tiro sai com o dobro de poder por tras (`mods *= n`)",
				   ProjeteisDaZona(pl.Zone.Hash)[1].Bp > ProjeteisDaZona(pl.Zone.Hash)[0].Bp * 1.9);

		// O PRAZO: passados os 6 s, a sequencia recomeca no KI.
		_blastPronto.Remove(pl.Id);
		_kikoho[pl.Id] = (2, NowMs() - 1);
		double antesDeRecomecar = pl.Combate.Corpo.Vida();
		UsarHabilidade(pl, "Kikoho");
		double sangueDoRecomeco = antesDeRecomecar - pl.Combate.Corpo.Vida();
		AfirmarCen("passado o prazo, a sequencia RECOMECA no KI (cobra o sangue de uma silaba so)",
				   Math.Abs(sangueDoRecomeco - (vida0 - depoisDoKi)) < 1e-6,
				   $"{sangueDoRecomeco:0.###} vs {vida0 - depoisDoKi:0.###}");

		// E QUEM ESTA FERIDO DEMAIS NAO PAGA: o `HP >= 10` do verb.
		_blastPronto.Remove(pl.Id);

		// ============================ POR QUE A VIDA E FORJADA AQUI, E SO AQUI ============================
		// Tentei chegar aos 10 batendo: nao da, e a razao e do MODELO DE CORPO deste port, nao do
		// teste. Golpe nao-letal tem piso por membro (`Body.Ferir`: `VidaMax*LimiarQuebra*0,99`) e
		// para em ~25 de media; golpe letal MATA quando um vital quebra, com a media ainda em 34 --
		// medi os dois. Ou seja: neste port um corpo VIVO quase nunca chega a 10 de media, porque a
		// morte vem de um membro e nao da soma.
		//
		// A GUARDA CONTINUA VALENDO (o Kikoho e a unica tecnica que cobra sangue, e um dia o corpo
		// vai poder chegar la -- regeneracao parcial, veneno, fome), e o que a bancada prova e que
		// ELA DISPARA. Forjar o campo que a guarda le e o unico jeito de exercitar um limite que o
		// resto do jogo ainda nao produz; fingir que ele foi atingido seria o oposto.
		// ============================================================================================
		pl.Ficha.HP = 5;
		EscutaDeAvisos = [];
		int antes = ProjeteisDaZona(pl.Zone.Hash).Count;
		UsarHabilidade(pl, "Kikoho");
		AfirmarCen("...e um corpo ferido demais e RECUSADO (o `HP >= 10` do verb)",
				   ProjeteisDaZona(pl.Zone.Hash).Count == antes
				   && EscutaDeAvisos.Exists(a => a.Contains("ferido demais", StringComparison.OrdinalIgnoreCase)),
				   string.Join(" | ", EscutaDeAvisos));
		EscutaDeAvisos = null;

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 6) OS QUATRO BUFFS
	// =====================================================================
	private void OsQuatroBuffs()
	{
		GD.Print("[censo] -- 6) OS QUATRO BUFFS: O MESMO FATOR NOS DOIS LADOS, E O DESFAZER");

		ServerPlayer pl = ForjarArmadoG6("Buffado", CorredorLivre(6), bp: 20_000);
		pl.Ficha.kicirculationskill = 50;
		pl.Ficha.kibuffskill = 50;
		pl.Ficha.kiefficiencyskill = 100;
		pl.Ficha.kidefenseskill = 35;

		// --- FOCUS: poder e preco pelo MESMO fator ---
		double kioff0 = pl.Ficha.Tkioff, dreno0 = pl.Ficha.DrainMod;
		UsarHabilidade(pl, "Focus");
		double subiuPoder = pl.Ficha.Tkioff - kioff0;
		double subiuPreco = pl.Ficha.DrainMod / dreno0;

		AfirmarCen("Focus: `1 + (circulacao+buff)/100` = 2,0 com as duas em 50",
				   Math.Abs(subiuPoder - 2.0) < 1e-9, $"{subiuPoder:0.###}");
		AfirmarCen("...e o gasto sobe pelo MESMO numero (o `initdrain == initbuff` do DM)",
				   Math.Abs(subiuPreco - 2.0) < 1e-9, $"{subiuPreco:0.###}");

		UsarHabilidade(pl, "Focus");
		AfirmarCen("...e desligar devolve EXATAMENTE o que somou",
				   Math.Abs(pl.Ficha.Tkioff - kioff0) < 1e-9 && Math.Abs(pl.Ficha.DrainMod - dreno0) < 1e-9,
				   $"Tkioff {pl.Ficha.Tkioff:0.####} vs {kioff0:0.####}");

		// O CLASSICO: ficar mais forte COM o buff de pe nao pode fazer o desligar levar o que nao deu.
		UsarHabilidade(pl, "Focus");
		pl.Ficha.kicirculationskill = 500;   // o jogador treinou durante o buff
		UsarHabilidade(pl, "Focus");
		AfirmarCen("...e treinar DURANTE o buff nao faz o desligar tirar a mais (o bug classico)",
				   Math.Abs(pl.Ficha.Tkioff - kioff0) < 1e-9, $"{pl.Ficha.Tkioff:0.####} vs {kioff0:0.####}");
		pl.Ficha.kicirculationskill = 50;

		// --- EFFICIENCY: divide o dreno, tira poder ---
		UsarHabilidade(pl, "Efficiency");
		double divisor = 2 + pl.Ficha.kiefficiencyskill / 200;
		AfirmarCen("Efficiency: o dreno CAI pelo divisor do DM (2 + eficiencia/200)",
				   Math.Abs(pl.Ficha.DrainMod - dreno0 / divisor) < 1e-9,
				   $"{pl.Ficha.DrainMod:0.####} vs {dreno0 / divisor:0.####}");
		AfirmarCen("...e a ofensiva de Ki CAI (0,5 - buff/200 = 0,25 com buff 50)",
				   Math.Abs(pl.Ficha.Tkioff - (kioff0 - 0.25)) < 1e-9, $"{pl.Ficha.Tkioff:0.####}");
		UsarHabilidade(pl, "Efficiency");

		// --- ENERGY SHIELD: o canal MULTIPLICATIVO que o motor de buffs nao tinha ---
		double mod0 = pl.Ficha.superkiarmorMod, armadura0 = pl.Ficha.superkiarmor;
		UsarHabilidade(pl, "Energy_Shield");
		AfirmarCen("Energy Shield: `superkiarmorMod *= 1,2` (o canal de FATOR)",
				   Math.Abs(pl.Ficha.superkiarmorMod - mod0 * 1.2) < 1e-9,
				   $"{pl.Ficha.superkiarmorMod:0.####}");
		AfirmarCen("...e a armadura de energia sobe de verdade",
				   pl.Ficha.superkiarmor > armadura0, $"{pl.Ficha.superkiarmor:0.##}");

		UsarHabilidade(pl, "Energy_Shield");
		AfirmarCen("...e sair DIVIDE o fator de volta (subtrair deixaria o campo num numero terceiro)",
				   Math.Abs(pl.Ficha.superkiarmorMod - mod0) < 1e-9,
				   $"{pl.Ficha.superkiarmorMod:0.####} vs {mod0:0.####}");
		AfirmarCen("...e a armadura volta ao que era", Math.Abs(pl.Ficha.superkiarmor - armadura0) < 1e-6);

		// O ESCUDO CAI SOZINHO quando a armadura acaba -- o `Loop()` do DM.
		UsarHabilidade(pl, "Energy_Shield");
		pl.Ficha.superkiarmor = 0;
		pl.Ficha.kiarmor = 0;      // sem `kiarmor` o Statify nao a repoe
		TickDasTecnicasG6();
		AfirmarCen("...e sem armadura nenhuma o escudo se desfaz sozinho no tique",
				   !TemBuff(pl, "Energy_Shield"));

		// --- FULL POWER: o unico buff que fica MAIS BARATO com o uso ---
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Ficha.jirenskill = 1;
		UsarHabilidade(pl, "Full_Power");
		AfirmarCen("Full Power: soma 1,2 em forca, Ki e velocidade",
				   Math.Abs(pl.Ficha.Tphysoff - 1 - 1.2) < 1e-9 && Math.Abs(pl.Ficha.Tspeed - 1 - 1.2) < 1e-9,
				   $"{pl.Ficha.Tphysoff:0.##}/{pl.Ficha.Tspeed:0.##}");

		double kiA = pl.Ficha.Ki;
		TickDasTecnicasG6();
		double gastoNovato = kiA - pl.Ficha.Ki;
		pl.Ficha.jirenskill = 100;
		kiA = pl.Ficha.Ki;
		TickDasTecnicasG6();
		double gastoMestre = kiA - pl.Ficha.Ki;
		AfirmarCen("...e a pratica (jirenskill 1 -> 100) barateia o aluguel em ~100x",
				   gastoNovato > gastoMestre * 50, $"{gastoNovato:0.##} vs {gastoMestre:0.##}");

		pl.Ficha.Ki = pl.Ficha.MaxKi / 50;   // abaixo de MaxKi/20
		TickDasTecnicasG6();
		AfirmarCen("...e sem energia a aura cai sozinha", !TemBuff(pl, "Full_Power"));

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 7) O SOPRO
	// =====================================================================
	private void OSopro()
	{
		GD.Print("[censo] -- 7) O SOPRO: ARCO, RECARGA COMPARTILHADA, TIRO APAGADO E DEVOLVIDO");

		Vec2 chao = CorredorLivre(12);
		ServerPlayer pl = ForjarArmadoG6("Soprador", chao, bp: 500_000);
		pl.Facing = Facing.East;
		pl.Ficha.kiaiskill = 50;

		ServerPlayer frente = Forjar("NaFrente", chao + new Vec2(ZoneCollision.TileSize, 0), bp: 100);
		ServerPlayer atras = Forjar("Atras", chao - new Vec2(ZoneCollision.TileSize, 0), bp: 100);

		UsarHabilidade(pl, "Kiai");
		AfirmarCen("Kiai ARREMESSA quem esta na frente", frente.TiquesDeVoo > 0, $"{frente.TiquesDeVoo}");
		AfirmarCen("...e NAO toca em quem esta atras (o arco e a tecnica)", atras.TiquesDeVoo == 0);

		// A RECARGA E UMA SO PRAS QUATRO -- trocar de verb nao burla a espera.
		EscutaDeAvisos = [];
		UsarHabilidade(pl, "Shockwave");
		AfirmarCen("a recarga do sopro e COMPARTILHADA (Shockwave logo apos o Kiai e recusado)",
				   EscutaDeAvisos.Exists(a => a.Contains("reagrupou", StringComparison.OrdinalIgnoreCase)),
				   string.Join(" | ", EscutaDeAvisos));
		EscutaDeAvisos = null;

		// O KIAI SEM NINGUEM NA FRENTE VIRA TIRO (`if(!mobaff)`).
		_soproPronto.Remove(pl.Id);
		frente.Pos = chao + new Vec2(ZoneCollision.TileSize * 20, 0);
		atras.Pos = chao - new Vec2(ZoneCollision.TileSize * 20, 0);
		int antes = ProjeteisDaZona(pl.Zone.Hash).Count;
		UsarHabilidade(pl, "Kiai");
		AfirmarCen("...e sem ninguem no arco o sopro vira uma lamina de ar (um tiro de verdade)",
				   ProjeteisDaZona(pl.Zone.Hash).Count == antes + 1);
		LimparTudoDaBancada([pl, frente, atras]);

		// A ONDA DE CHOQUE APAGA TIRO -- e a razao de existir dela.
		_soproPronto.Remove(pl.Id);
		ServerPlayer inimigo = Forjar("Atirador", chao + new Vec2(ZoneCollision.TileSize, 0), bp: 10);
		inimigo.Facing = Facing.West;
		Projetil bola = Disparar(inimigo, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 10,
		});
		AfirmarCen("(o inimigo conseguiu atirar)", bola.Vivo);
		UsarHabilidade(pl, "Shockwave");
		AfirmarCen("a Onda de Choque APAGA o tiro do mais fraco que estava chegando", !bola.Vivo);
		AfirmarCen("...e arremessa o dono dele junto", inimigo.TiquesDeVoo > 0);

		// A DEFLEXAO TROCA O DONO -- sem isso, o tiro devolvido atravessa quem atirou (defeito do DM).
		_soproPronto.Remove(pl.Id);
		inimigo.TiquesDeVoo = 0;
		inimigo.Pos = chao + new Vec2(ZoneCollision.TileSize, 0);
		Projetil segunda = Disparar(inimigo, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 10,
		});
		UsarHabilidade(pl, "Deflection");
		AfirmarCen("a Deflexao devolve o tiro fraco E TROCA O DONO",
				   segunda.Vivo && segunda.Dono == pl.Id, $"dono {segunda.Dono} vs {pl.Id}");

		// E O TIRO FORTE DEMAIS NAO VOLTA: `strength > 1` e a porta.
		_soproPronto.Remove(pl.Id);
		Projetil pesado = Disparar(inimigo, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 10,
		});
		pesado.Bp = 1e18;
		pesado.ModsBase = 1e18;
		UsarHabilidade(pl, "Deflection");
		AfirmarCen("...e um tiro forte demais NAO e devolvido (a porta `strength > 1`)",
				   pesado.Dono == inimigo.Id, $"dono {pesado.Dono}");

		// O RUGIDO: duas fases, e o teto util avisa.
		LimparTudoDaBancada([pl]);
		_soproPronto.Remove(pl.Id);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		UsarHabilidade(pl, "Explosive_Roar");
		AfirmarCen("o Rugido comeca a carregar no primeiro aperto", _rugindo.ContainsKey(pl.Id));

		ServerPlayer perto = Forjar("Perto", pl.Pos + new Vec2(ZoneCollision.TileSize * 2, 0), bp: 100);
		for (int i = 0; i < 4; i++) { _rugindo[pl.Id].ProximoMs = 0; TickDasTecnicasG6(); }
		AfirmarCen("...e a carga anda no tique de 1 Hz", _rugindo[pl.Id].Carga >= 3,
				   $"{_rugindo[pl.Id].Carga}");

		UsarHabilidade(pl, "Explosive_Roar");
		AfirmarCen("o segundo aperto solta o rugido e ele arremessa quem esta no raio",
				   !_rugindo.ContainsKey(pl.Id) && perto.TiquesDeVoo > 0, $"{perto.TiquesDeVoo}");

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 8) OS DOIS PUNHOS
	// =====================================================================
	private void OsDoisPunhos()
	{
		GD.Print("[censo] -- 8) OS PUNHOS DO LOBO: TRES GOLPES E ARREMESSO, QUATRO E AVANCO");

		Vec2 chao = CorredorLivre(12);
		ServerPlayer pl = ForjarArmadoG6("Lobo", chao, bp: 200_000);
		pl.Facing = Facing.East;
		ServerPlayer alvo = Forjar("Saco", chao + new Vec2(ZoneCollision.TileSize / 2f, 0), bp: 200_000);
		pl.AlvoId = alvo.Id;

		double vida0 = alvo.Combate.Corpo.Vida();
		UsarHabilidade(pl, "Wolf_Fang_Fist");
		AfirmarCen("o Punho da Presa comeca a sequencia (o primeiro golpe sai na hora)",
				   _barragemG3.ContainsKey(pl.Id) && alvo.Combate.Corpo.Vida() < vida0,
				   $"{alvo.Combate.Corpo.Vida():0.###} vs {vida0:0.###}");

		AfirmarCen("...e ele cobra FOLEGO alem de Ki (8 de stamina)",
				   pl.Ficha.stamina <= 92.001, $"{pl.Ficha.stamina:0.#}");

		// A SEQUENCIA ANDA NO PULSO. Os dois golpes restantes + o arremesso do fim.
		for (int i = 0; i < 40 && _barragemG3.ContainsKey(pl.Id); i++)
		{
			foreach (int id in _barragemG3.Keys.ToList()) _barragemG3[id].ProximoMs = 0;
			PulsoBarragemG3(NowMs());
		}
		AfirmarCen("no fim da sequencia o alvo E ARREMESSADO (`kbdur = 4`)",
				   alvo.TiquesDeVoo > 0, $"{alvo.TiquesDeVoo}");

		// O FURACAO: quatro golpes, e NENHUM arremesso no meio.
		_prontoG3.Remove(pl.Id);
		alvo.TiquesDeVoo = 0;
		alvo.Pos = pl.Pos + new Vec2(ZoneCollision.TileSize / 2f, 0);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Knockback = true;

		UsarHabilidade(pl, "Wolf_Fang_Hurricane");
		AfirmarCen("o Furacao abre a sequencia de quatro", _barragemG3.ContainsKey(pl.Id)
				   && _barragemG3[pl.Id].Faltam == 3, $"faltam {_barragemG3.GetValueOrDefault(pl.Id)?.Faltam}");
		AfirmarCen("...e ela nasce com o empurrao DESLIGADO (`knockbackon = 0` do DM)",
				   _barragemG3[pl.Id].SemEmpurrao);

		Vec2 antesDoAvanco = pl.Pos;
		alvo.Pos = pl.Pos + new Vec2(ZoneCollision.TileSize * 1.2f, 0);
		foreach (int id in _barragemG3.Keys.ToList()) _barragemG3[id].ProximoMs = 0;
		PulsoBarragemG3(NowMs());
		AfirmarCen("...e quem bate AVANCA junto (o `step` entre os golpes)",
				   (pl.Pos - antesDoAvanco).Length > 1, $"{(pl.Pos - antesDoAvanco).Length:0.#}px");

		for (int i = 0; i < 40 && _barragemG3.ContainsKey(pl.Id); i++)
		{
			foreach (int id in _barragemG3.Keys.ToList()) _barragemG3[id].ProximoMs = 0;
			PulsoBarragemG3(NowMs());
		}
		AfirmarCen("...e o Furacao NAO arremessa no fim (o alvo fica na sua frente)",
				   alvo.TiquesDeVoo == 0, $"{alvo.TiquesDeVoo}");
		AfirmarCen("...e o empurrao do atacante VOLTA ao que era depois da sequencia", pl.Knockback);

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 9) SERVICO: CURAR E LER O OUTRO
	// =====================================================================
	private void OServico()
	{
		GD.Print("[censo] -- 9) SERVICO: A CURA QUE ONZE CARGOS PROMETIAM, E A LEITURA DE KI");

		Vec2 chao = CorredorLivre(12);
		ServerPlayer medico = ForjarArmadoG6("Medico", chao, bp: 50_000);
		ServerPlayer ferido = Forjar("Ferido", chao + new Vec2(ZoneCollision.TileSize / 2f, 0), bp: 50_000);
		medico.AlvoId = ferido.Id;

		// FERE de verdade, pelo funil do dano espalhado.
		EspalharDanoG3(ferido, ferido, 30, letal: false);
		double machucado = ferido.Combate.Corpo.Vida();
		AfirmarCen("(o corpo do ferido esta mesmo machucado)", machucado < 95, $"{machucado:0.###}");

		UsarHabilidade(medico, "Heal");
		AfirmarCen("a cura comeca e guarda quem esta curando quem", _curando.ContainsKey(medico.Id));

		TickDasTecnicasG6();
		AfirmarCen("...e um tique de 1 Hz REPARA o corpo de quem esta ao lado",
				   ferido.Combate.Corpo.Vida() > machucado,
				   $"{ferido.Combate.Corpo.Vida():0.####} vs {machucado:0.####}");

		double custou = medico.Ficha.MaxKi - medico.Ficha.Ki;
		AfirmarCen("...e cobra Ki de quem cura", custou > 0, $"{custou:0.##}");

		// O ALVO SAI DE PERTO: a cura para sozinha.
		ferido.Pos = chao + new Vec2(ZoneCollision.TileSize * 8, 0);
		TickDasTecnicasG6();
		AfirmarCen("o alvo se afasta e a cura para sozinha", !_curando.ContainsKey(medico.Id));

		// AVALIAR O KI: os tres degraus de leitura.
		ferido.Pos = chao + new Vec2(ZoneCollision.TileSize, 0);
		ferido.Ficha.kieffusionskill = 42;

		EscutaDeAvisos = [];
		medico.Ficha.kiawarenessskill = 10;
		UsarHabilidade(medico, "Assess_Ki_Skill");
		bool vago = EscutaDeAvisos.Exists(a => a.Contains("dominio de Ki", StringComparison.OrdinalIgnoreCase));
		bool numero1 = EscutaDeAvisos.Exists(a => a.Contains("42", StringComparison.Ordinal));
		AfirmarCen("percepcao baixa: so a impressao geral, sem numero nenhum", vago && !numero1,
				   string.Join(" | ", EscutaDeAvisos));

		EscutaDeAvisos.Clear();
		medico.Ficha.kiawarenessskill = 40;
		UsarHabilidade(medico, "Assess_Ki_Skill");
		AfirmarCen("percepcao media: comparacao pericia a pericia, ainda sem numero",
				   EscutaDeAvisos.Exists(a => a.Contains("efusao", StringComparison.OrdinalIgnoreCase))
				   && !EscutaDeAvisos.Exists(a => a.Contains("42", StringComparison.Ordinal)),
				   string.Join(" | ", EscutaDeAvisos));

		EscutaDeAvisos.Clear();
		medico.Ficha.kiawarenessskill = 80;
		UsarHabilidade(medico, "Assess_Ki_Skill");
		AfirmarCen("percepcao alta: os NUMEROS dele na tela",
				   EscutaDeAvisos.Exists(a => a.Contains("42", StringComparison.Ordinal)),
				   string.Join(" | ", EscutaDeAvisos));

		EscutaDeAvisos.Clear();
		ferido.Pos = chao + new Vec2(ZoneCollision.TileSize * 40, 0);
		UsarHabilidade(medico, "Assess_Ki_Skill");
		AfirmarCen("...e de longe demais (mais de 20 tiles) nao se le nada",
				   EscutaDeAvisos.Exists(a => a.Contains("longe", StringComparison.OrdinalIgnoreCase)));
		EscutaDeAvisos = null;

		LimparTudoDaBancada();
	}
}
