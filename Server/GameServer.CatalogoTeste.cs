using System.Reflection;
using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Forms;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--catalogoteste` -- O RELATORIO DO CATALOGO ANDANDO, E AS SEIS PERGUNTAS QUE O MANTEM
/// HONESTO.
///
/// ============================ POR QUE ELA EXISTE, SE JA HA SEIS BANCADAS DE TECNICA ============================
/// `--punhoteste`, `--arsenalteste`, `--censoteste`, `--projetilteste`, `--tecnicateste` e
/// `--embatekiteste` afirmam, todas, sobre O LOTE DELAS: os dezesseis do G7, as catorze do G5, os
/// dezenove do G6. Cada uma prova que o SEU trabalho ficou de pe.
///
/// Nenhuma delas afirma nada sobre o CONJUNTO -- e e no conjunto que esta divida mora. As tres
/// coisas que esta rodada encontrou saem todas do mesmo tipo de buraco, e nenhuma bancada de lote
/// tinha como ve-las:
///
///   1. o lote G7 registrou dezesseis verbos no servidor e **nao acrescentou uma linha ao espelho do
///      Core** -- durante uma sessao inteira o console do extrator respondeu "52 com efeito portado"
///      sobre um jogo que tinha 68. Numero baixo demais nao quebra nada: ele so faz a divida parecer
///      maior, que e o jeito mais silencioso de um relatorio mentir;
///   2. os mesmos dezesseis continuaram escritos na tabela `CensoDeSkills.Esperando`, dizendo que
///      esperavam um sistema que ja tinha chegado. A contradicao nao mudava numero nenhum do
///      relatorio (a classificacao pergunta ao `Tecnicas` primeiro), e por isso sobreviveu -- mas
///      estragava a unica coisa que aquela tabela serve pra dizer: DE QUE SISTEMA O PROXIMO LOTE
///      DEPENDE. Quem lesse "29 verbos esperam o motor de combos" iria portar catorze ja portados;
///   3. e o espelho da IA (`TecnicasDeLonge`) tem quatro linhas pra vinte e um verbos que atiram --
///      divida ja conhecida, mas **nao medida contra a realidade**: ninguem sabia dizer QUAIS verbos
///      atiram, porque isso estava em codigo e nao em lista.
///
/// As tres tem a mesma forma: **fechar um lote toca em tres lugares e o autor lembrou de um**. E
/// nenhuma delas e um bug -- e um desencontro entre tabelas, que e o defeito que nao aparece em
/// teste porque nada quebra.
/// ==========================================================================================================
///
/// ============================ AS SEIS FAMILIAS, E COMO CADA UMA REPROVA ============================
///  1. O RELATORIO ANDANDO -- imprime `34 (marco) -> N hoje` toda rodada e exige que as DUAS BOCAS
///     (o servidor e o espelho do Core que o console le) contem a MESMA coisa, nas duas direcoes.
///     REPROVA quando um lote e registrado so de um lado -- que e como ela nasceu vermelha.
///  2. NENHUM VERBO VIVO RESPONDE VAZIO -- varre os N verbos com corpo, aperta CADA UM pelo funil de
///     producao (`UsarHabilidade`) e exige que o mundo MUDE. A mudanca e medida por impressao
///     digital tirada por REFLEXAO -- a ficha inteira e todos os registros do servidor --, e nao por
///     uma lista de campos escolhidos a dedo, que e exatamente onde um efeito novo escaparia.
///     REPROVA um verb registrado sem efeito: ele cai no `default` do despacho, diz "ainda nao tem
///     efeito" e nao mexe em nada. E o botao mentiroso com outra cara.
///  3. A IA CRESCE COM O JOGO -- a familia 2 OBSERVOU quais verbos criam projetil ou abrem canal; esta
///     compara essa observacao com a tabela da IA. REPROVA quando um tiro novo entra no jogo e nao
///     entra na tabela nem na lista de divida declarada. Nao ha lista escrita a mao: quem diz o que
///     atira e o jogo, apertando o botao.
///  4. O CARGO DA E TIRA -- o ciclo inteiro dos 30 cargos: reivindicar, receber, aprender, largar. A
///     sobra e medida pelo livro INTEIRO, mais o estilo vestido e os dez multiplicadores dele.
///     REPROVA acumulacao de graca (largar o cargo e ficar com o estilo) e REPROVA roubo (perder uma
///     skill que um mestre ensinou).
///  5. A RE-EXTRACAO NAO QUEBROU NADA -- compara o catalogo de hoje com o marco congelado
///     (`Assets/Data/catalogo-marco.json`, tirado do git). REPROVA skill que emudeceu, skill que
///     sumiu e verbo que deixou de ser concedido -- inclusive os de DEGRAU, que sao dois dos verbos
///     de tiro do jogo.
///  6. COBERTURA E CONTRADICAO -- todo verb declarado esta VIVO ou CATALOGADO (a `--censoteste` ja
///     afirma isso, e aqui a checagem e a outra metade): nenhum verb pode estar em DUAS listas ao
///     mesmo tempo. REPROVA a tabela que envelheceu sem que nenhum numero mudasse.
///
/// TODAS as seis injetam o defeito. As quatro que dependem de tabela injetam pelo lado puro (as
/// funcoes de comparacao recebem os conjuntos em vez de le-los, e a bancada as chama com um par
/// sintetico); as duas que dependem do mundo injetam no mundo (um verb oco de verdade e a regra
/// velha de revogacao remontada aqui).
/// ================================================================================================
/// </summary>
public partial class GameServer
{
	private int _catOk, _catFalhou;

	private void AfirmarCat(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _catOk++; GD.Print($"[catalogo]   OK    {oque}"); return; }
		_catFalhou++;
		GD.PrintErr($"[catalogo]   FALHA {oque}   {detalhe}");
	}

	/// <summary>O que a familia 2 VIU cada verb fazer -- a materia-prima da familia 3.</summary>
	private readonly Dictionary<string, string> _catOQueOVerboFez = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Os verbos que, apertados, puseram um projetil no mundo ou abriram um canal de raio.</summary>
	private readonly List<string> _catAtiraram = [];

	public void RodarBancadaDoCatalogo()
	{
		_catOk = _catFalhou = 0;
		_catOQueOVerboFez.Clear();
		_catAtiraram.Clear();
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);

		GD.Print("[catalogo] ================ O CATALOGO ANDANDO ================");
		AfirmarCat("a zona da bancada tem colisao carregada", _pjMapa != null);

		// O PLACAR DA VIZINHA, FOTOGRAFADO. Esta bancada usa o `CorredorLivre` da `--projetilteste`, e
		// as recusas DELE contam no placar DELA e saem por `stderr` -- foi assim que cinquenta
		// reprovacoes por rodada ficaram invisiveis atras de um 47/0. Ver `ChaoDaBancada`.
		int falhasDaVizinha = _pjFalhou;

		try
		{
			ORelatorioAndando();
			NenhumVerboVivoRespondeVazio();
			AIaCresceComOJogo();
			OCargoDaETira();
			ARextracaoNaoQuebrouNada();
			CoberturaESemContradicao();
		}
		catch (Exception e)
		{
			AfirmarCat($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			LimparTudoDoCatalogo();
		}

		AfirmarCat("...e esta bancada nao reprovou nada no placar da vizinha (o chao deu pra todos)",
				   _pjFalhou == falhasDaVizinha, $"{_pjFalhou - falhasDaVizinha} recusas do `CorredorLivre`");

		GD.Print($"[catalogo] ================ {_catOk} passaram, {_catFalhou} falharam ================");
	}

	// =====================================================================
	// 1) O RELATORIO ANDANDO -- e as duas bocas contando a mesma coisa
	// =====================================================================
	private void ORelatorioAndando()
	{
		GD.Print("[catalogo] -- 1) O RELATORIO DO CATALOGO, E AS DUAS BOCAS QUE PRECISAM CONCORDAR");

		if (_skills == null) { AfirmarCat("o catalogo de skills esta carregado", false); return; }

		CensoDeSkills.Relatorio r = CensoDeSkills.Levantar(_skills, RegrasDeNivel.VerbosDeDegrau);
		foreach (string linha in CensoDeSkills.Texto(r)) GD.Print($"[catalogo] {linha}");

		// O NUMERO QUE ANDA. Ele so pode subir: um verb que perdeu o corpo e o defeito mais caro que
		// este catalogo consegue ter, porque o botao continua na tela.
		AfirmarCat($"as tecnicas com corpo passaram do marco de {Tecnicas.MarcoDeVerbosComCorpo}",
				   r.ComCorpo > Tecnicas.MarcoDeVerbosComCorpo,
				   $"{r.ComCorpo} hoje");

		// ============================ AS DUAS BOCAS ============================
		// O efeito mora no servidor; o descritor mora no Core, porque o console do extrator nao
		// carrega Godot e mesmo assim conta o progresso. Enquanto as duas escreviam no mesmo
		// dicionario, a divergencia era invisivel -- o segundo registro cobria o primeiro.
		// =====================================================================
		(List<string> soNoJogo, List<string> soNoEspelho) =
			Tecnicas.DesencontroDoEspelho(Tecnicas.Vivas, Tecnicas.NoEspelho);

		AfirmarCat("o ESPELHO DO CORE conhece todo verb que o servidor porta (senao o console subconta)",
				   soNoJogo.Count == 0,
				   $"{soNoJogo.Count} fora do espelho: {string.Join(" | ", soNoJogo)}");

		AfirmarCat("...e o espelho nao promete efeito que o servidor nao registra mais",
				   soNoEspelho.Count == 0,
				   $"{soNoEspelho.Count} orfas: {string.Join(" | ", soNoEspelho)}");

		// O DEFEITO INJETADO, nas DUAS direcoes. A funcao e pura de proposito: e o que permite
		// perguntar "voce reprovaria?" sem sujar o registro de tecnicas do processo.
		(List<string> injJogo, List<string> injEspelho) =
			Tecnicas.DesencontroDoEspelho(["Solar_Flare", "Verbo_Que_O_Lote_Esqueceu"],
										  ["Solar_Flare", "Verbo_Que_Perdeu_O_Corpo"]);
		AfirmarCat("...e o detector pega as duas direcoes (injecao)",
				   injJogo is ["Verbo_Que_O_Lote_Esqueceu"] && injEspelho is ["Verbo_Que_Perdeu_O_Corpo"],
				   $"[{string.Join(",", injJogo)}] / [{string.Join(",", injEspelho)}]");

		// A CONTA DO RELATORIO TEM QUE FECHAR -- sem isto uma situacao nova sumiria da soma e o
		// relatorio ficaria bonito e errado.
		int soma = r.VerbosPortados + r.VerbosPorCanal + r.VerbosEsperando + r.VerbosSemCobertura;
		AfirmarCat("as quatro situacoes somam o total de verbos", soma == r.VerbosTotal,
				   $"{soma} vs {r.VerbosTotal}");

		AfirmarCat("o relatorio conta os verbos dos DOIS caminhos (skill E degrau)",
				   r.Verbos.Exists(l => l.PorSkill > 0) && r.Verbos.Exists(l => l.PorDegrau > 0));
	}

	// =====================================================================
	// 2) NENHUM VERBO VIVO RESPONDE VAZIO
	// =====================================================================
	/// <summary>
	/// OS VERBOS QUE RECUSAM POR CONDICAO, e a condicao de cada um.
	///
	/// ============================ POR QUE ESTA TABELA NAO E UMA DESCULPA ============================
	/// A varredura arma UMA cena (dois corpos colados, tudo aprendido, Ki e folego cheios, alvo
	/// marcado, um membro ferido em cada um) e aperta os N verbos nela. Alguns nao tem como AGIR
	/// nessa cena -- o Reviver precisa de um morto, o Kaioken_100 precisa de maestria, o Time Touch
	/// precisa de alguem que aceite envelhecer. Esses respondem RECUSANDO.
	///
	/// Recusar em voz alta e comportamento correto (a regra 5 da casa: falhar alto em vez de calar) --
	/// mas so vale se a recusa for DECLARADA, senao "recusou" vira o esconderijo perfeito pra um verb
	/// que nao faz nada. Entao a bancada exige as duas coisas: que a recusa exista em texto E que o
	/// verb esteja aqui com o motivo. Um verb que passe a recusar sem estar nesta tabela REPROVA.
	/// ==========================================================================================
	/// </summary>
	private static readonly Dictionary<string, string> RecusamPorCondicao = new(StringComparer.OrdinalIgnoreCase)
	{
		// ---- o efeito E A FRASE: nada muda no mundo porque a tecnica so LE o mundo ----
		["Assess_Ki_Skill"] =
			"leitura: o efeito dela e a frase que ela devolve. Mudar algo seria o defeito",
		["Telepathy"] =
			"precisa de outra MENTE no mundo -- corpo de bancada nao tem cliente, e o `ListarMentesG4` "
			+ "so lista quem tem. Medir isso pede dois clientes, e ha bancada propria pra chat",

		// ---- a cena nao tem a condicao, e arma-la aqui estragaria a cena dos outros noventa ----
		["Life_Suck"] =
			"o alvo precisa estar NOCAUTEADO e vivo; nocautear o boneco faria os outros verbos medirem "
			+ "um alvo caido em vez do golpe",
		["Revive"] =
			"precisa de um MORTO ao lado -- e matar o boneco tem o mesmo problema do de cima",
		["Bite"] =
			"NAO TEM PORTA neste port -- ver `SemPortaNestePort`. A recusa e do gate, e nao do efeito",

		// O ATALHO DE FORMA E ATALHO: ele so abre depois de a escada ter sido subida uma vez, e um
		// corpo recem-forjado nunca despertou nada. Arma-lo aqui seria transformar (e a forma muda a
		// ficha inteira) antes de apertar os outros noventa verbos -- a varredura passaria a medir um
		// Super Saiyajin. Quem prova o atalho e a `--formasteste`.
		["DirectSSJ"] = "so abre depois de a forma ter sido despertada uma vez pela escada",

		// ---- lote G8: dois em que a FRASE e o efeito, e um que espera a resposta de outra pessoa ----
		["Dead"] =
			"leitura: lista quem esta morto no mundo, e na cena da bancada nao ha nenhum. Matar o "
			+ "boneco pra ter o que listar e o mesmo problema do `Revive` acima",
		["Detect_Shard"] =
			"o verb INTEIRO do DM e uma frase (`SpaceRanks.dm:110-112`): a Esmeralda Mestra nao existe "
			+ "mais e nao ha nada a detectar. Mexer no mundo seria o defeito, nao a prova",
		["Restore_Youth"] =
			"o primeiro aperto PERGUNTA a idade e o efeito depende de o ALVO aceitar, por verb proprio "
			+ "(`juventude_aceitar`) -- um corpo forjado nao aperta botao. Ver `OferecerJuventudeG8`",

		// ---- lote G9: o unico que precisa de uma COISA no chao, e nao de uma condicao no corpo ----
		["Mafuba"] =
			"precisa de um POTE SELANTE assentado a vista, e a cena da varredura e so dois corpos. Nao "
			+ "e limitacao da bancada: no DM o verb abre um `input()` com a lista dos `SealingItem` em "
			+ "`view()` e um `isnull(choice)` cancela (`Sealing.dm:158-164`) -- **Mafuba sem pote nao "
			+ "existe no jogo original**. Assentar um pote so pra este verb mudaria a cena dos outros "
			+ "noventa (o pote e denso e entra na colisao). Quem o prova de ponta a ponta, com pote, "
			+ "fita e preso, e a `--seloteste`",
	};

	/// <summary>
	/// OS VERBOS DE DUAS FASES -- o primeiro aperto PERGUNTA, o segundo AGE.
	///
	/// ============================ POR QUE A BANCADA APERTA OS DOIS ============================
	/// No DM estes quatro abrem um `input()`, que e uma caixa modal travando o jogo ate a resposta.
	/// Aqui nao ha modal no servidor: o id sem argumento devolve a lista e o id COM argumento executa
	/// (`GameServer.Tecnicas.G4.cs`, cabecalho). Entao o primeiro aperto legitimamente nao mexe em
	/// nada -- ele so responde.
	///
	/// Declarar isso como "recusa" seria dar a estes quatro um passe livre: um menu que listasse as
	/// opcoes e um segundo aperto que nao fizesse nada passariam despercebidos pra sempre. Entao a
	/// bancada exige as DUAS coisas: que o primeiro aperto RESPONDA em voz alta e que o segundo MEXA
	/// no mundo.
	/// ====================================================================================
	/// </summary>
	private static readonly Dictionary<string, string> SegundoAperto = new(StringComparer.OrdinalIgnoreCase)
	{
		["Final_Explosion"] = "Final_Explosion_3",          // o menor dos tres raios (3 tiles)
		["Rock_Paper_Scissors"] = "Rock_Paper_Scissors_1",  // pedra
		["RiftTeleport"] = "RiftTeleport:Namek",            // o rasgo leva pra outra zona pre-feita

		// ---- lote G8: os dois teleportes de cargo, pelo mesmo desenho do `RiftTeleport` ----
		// TELEPORTAR O CORPO DA VARREDURA E SEGURO, e vale registrar por que: `ApertarUmVerbo` FORJA
		// uma cena nova (`CenaDoCatalogo`) a cada aperto e limpa tudo no fim, entao nenhum verb herda
		// o lugar (nem o Ki) do anterior. Sem isso, o `Holy_Shortcut` -- que cobra METADE do Ki --
		// deixaria os verbos seguintes recusando por falta de energia, e a varredura acusaria o
		// vizinho pelo que este fez.
		["Go_To_Heaven_Or_Hell"] = "Go_To_Heaven_Or_Hell:ceu",
		["Holy_Shortcut"] = "Holy_Shortcut:arconia",

		// ---- lote G9: a caixa de porcentagem do Controle de Poder ----
		// `Power_Control` abre um `input(...) as num` no DM (`Power Control.dm:178`) e vale pela MESMA
		// regra dos de cima: sem argumento ele RELATA a porcentagem atual, com argumento ele baixa o
		// `powerMod`. Entra aqui e nao no `RecusamPorCondicao` de proposito -- declarar como recusa
		// daria a ele um passe livre, e o segundo aperto (que e o que faz o efeito) nunca seria medido.
		["Power_Control"] = "Power_Control:40",
	};

	/// <summary>
	/// OS VERBOS COM CORPO QUE NENHUMA SKILL E NENHUM DEGRAU CONCEDE -- tecnica pronta e sem porta.
	///
	/// ============================ E O DEFEITO ESPELHADO DO BOTAO MENTIROSO ============================
	/// A frente inteira nasceu do botao que promete e nao entrega. Este e o contrario, e custa o mesmo:
	/// o efeito existe, foi escrito, tem bancada -- e ninguem consegue chegar nele, porque o
	/// `SabeTecnica` pergunta ao catalogo e o catalogo nao concede o verb a ninguem. O jogador nao ve
	/// promessa quebrada; ele simplesmente nunca ve a tecnica.
	///
	/// SO ELE APARECE HOJE, e a causa esta no original: o `Bite` nao vem de skill nenhuma, vem de SER
	/// VAMPIRO OU LOBISOMEM (`Login.dm:305-306`, `Vampires.dm:50`, `Werewolves.dm:31`) e do nivel 1 da
	/// maestria escondida de Vampirismo (`Hidden Masteries.dm:54`, um `addverb` dentro do
	/// `levelstat()`). Maestria escondida nao esta no `skills.json` -- ela nao e uma `/datum/skill` --
	/// e o lote G3 portou o efeito sem que existisse a via que o entrega.
	///
	/// Fica CATALOGADO em vez de forcado: dar uma porta ao `Bite` seria inventar de onde ele vem, e a
	/// resposta certa e portar vampirismo/licantropia -- um sistema, nao uma linha.
	/// ============================================================================================
	/// </summary>
	private static readonly Dictionary<string, string> SemPortaNestePort = new(StringComparer.OrdinalIgnoreCase)
	{
		["Bite"] = "vampirismo/licantropia (maestria ESCONDIDA do DM, fora do `skills.json`)",
	};

	/// <summary>As tres frases com que o despacho responde "isto nao existe". Nenhuma conta como resposta.</summary>
	private static readonly string[] FrasesDeAusencia =
	[
		"ainda nao tem efeito", "ainda não tem efeito",
		"nao foi portado", "não foi portado",
		"habilidade desconhecida",
	];

	private void NenhumVerboVivoRespondeVazio()
	{
		GD.Print("[catalogo] -- 2) TODO VERBO COM CORPO FAZ ALGUMA COISA (varredura pelo funil de producao)");

		if (_skills == null) { AfirmarCat("ha catalogo pra varrer", false); return; }

		List<string> vivos = [.. Tecnicas.Vivas.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
		AfirmarCat($"ha o que varrer: {vivos.Count} verbos com corpo", vivos.Count > 60);

		var vazios = new List<string>();
		var estouraram = new List<string>();
		var recusaramSemDeclarar = new List<string>();
		var menuQueNaoAge = new List<string>();
		int agiram = 0, recusaram = 0, porMenu = 0;

		foreach (string id in vivos)
		{
			(string veredito, string detalhe) = ApertarUmVerbo(id);

			// OS DE DUAS FASES: o primeiro aperto so pode PERGUNTAR (em voz alta), e quem tem que
			// mexer no mundo e o segundo. As duas metades sao exigidas.
			if (SegundoAperto.TryGetValue(id, out string? comArgumento))
			{
				if (veredito == "vazio") { vazios.Add($"{id} (o menu nao respondeu nada: {detalhe})"); continue; }
				(string v2, string d2) = ApertarUmVerbo(comArgumento);
				_catOQueOVerboFez[id] = $"menu -> {comArgumento}: {v2} ({d2})";
				if (v2 == "agiu") { porMenu++; continue; }
				menuQueNaoAge.Add($"{id} -> {comArgumento}: {v2} ({d2})");
				continue;
			}

			_catOQueOVerboFez[id] = $"{veredito}: {detalhe}";

			switch (veredito)
			{
				case "agiu": agiram++; break;
				case "estourou": estouraram.Add($"{id} ({detalhe})"); break;
				case "recusou":
					recusaram++;
					if (!RecusamPorCondicao.ContainsKey(id)) recusaramSemDeclarar.Add($"{id} -> {detalhe}");
					break;
				default: vazios.Add($"{id} ({detalhe})"); break;
			}
		}

		GD.Print($"[catalogo]    varredura: {agiram} agiram | {porMenu} agiram no SEGUNDO aperto | "
			   + $"{recusaram} recusaram em voz alta | {vazios.Count} vazios | {estouraram.Count} estouraram");

		AfirmarCat("os verbos de MENU respondem no primeiro aperto e AGEM no segundo",
				   menuQueNaoAge.Count == 0 && porMenu == SegundoAperto.Count,
				   string.Join(" | ", menuQueNaoAge));

		// A AFIRMACAO CENTRAL DA BANCADA.
		AfirmarCat("nenhum verb com corpo responde VAZIO (sem mexer no mundo e sem dizer nada)",
				   vazios.Count == 0, string.Join(" | ", vazios));

		AfirmarCat("...e nenhum estoura no meio do efeito",
				   estouraram.Count == 0, string.Join(" | ", estouraram));

		AfirmarCat("...e todo verb que RECUSA tem a condicao declarada nesta bancada",
				   recusaramSemDeclarar.Count == 0, string.Join(" | ", recusaramSemDeclarar));

		// ============================ E A TABELA NAO PODE ENVELHECER, QUE E O DEFEITO DESTA SESSAO ============================
		// Toda a frente saiu de tabelas que continuaram certas depois de o mundo mudar: dezesseis
		// verbos declarando que esperavam um sistema que ja tinha chegado, um espelho declarando 77
		// tecnicas num jogo com 93. Uma tabela de excecoes desta bancada apodrece do mesmo jeito -- e
		// apodrece PRA PIOR, porque cada linha obsoleta e um verb que deixou de ser conferido.
		//
		// Entao a bancada confere as tres tabelas nas DUAS direcoes: quem recusa esta declarado (acima)
		// E quem esta declarado recusou mesmo (aqui). O dia em que o `Revive` ganhar como agir na
		// cena, esta linha fica vermelha ate alguem apagar a excecao dele.
		// ================================================================================================================
		var declaradoMasAgiu = RecusamPorCondicao.Keys
			.Where(v => !_catOQueOVerboFez.TryGetValue(v, out string? o) || !o.StartsWith("recusou"))
			.ToList();
		AfirmarCat("...e nenhuma condicao declarada esta OBSOLETA (todas recusaram mesmo)",
				   declaradoMasAgiu.Count == 0, string.Join(" | ", declaradoMasAgiu));

		// O MESMO PRA LISTA DE MENU: um verb que deixou de ser menu (passou a agir no primeiro
		// aperto) tem que sair de la, senao a bancada estaria apertando duas vezes o que age uma.
		var menuQueJaAge = SegundoAperto.Keys
			.Where(v => !_catOQueOVerboFez.TryGetValue(v, out string? o) || !o.StartsWith("menu ->"))
			.ToList();
		AfirmarCat("...e a lista de verbos de MENU tambem nao envelheceu",
				   menuQueJaAge.Count == 0, string.Join(" | ", menuQueJaAge));

		// O RELATORIO DA VARREDURA, agrupado. Sao 93 verbos: um por linha viraria ruido, e um numero
		// so nao deixa ninguem conferir NOMES. Agrupado, cabe em quatro linhas e ainda diz quem e quem.
		foreach (IGrouping<string, KeyValuePair<string, string>> g in _catOQueOVerboFez
					 .GroupBy(p => p.Value.Split(':')[0].Split(" ->")[0])
					 .OrderByDescending(g => g.Count()))
			GD.Print($"[catalogo]    {g.Count(),3} {g.Key,-9}: "
				   + string.Join(", ", g.Select(p => p.Key).Order(StringComparer.OrdinalIgnoreCase)));

		// ============================ TODO VERBO COM CORPO PRECISA DE UMA PORTA ============================
		// O defeito espelhado: a tecnica existe, funciona, e ninguem chega nela porque skill nenhuma a
		// concede. Ver `SemPortaNestePort` -- foi assim que o `Bite` apareceu.
		// ==============================================================================================
		var comPorta = new HashSet<string>(RegrasDeNivel.VerbosDeDegrau, StringComparer.OrdinalIgnoreCase);
		foreach (Skill s in _skills.Todas)
			foreach (string v in s.Verbos) comPorta.Add(v);

		var semPorta = new List<string>();
		foreach (string id in vivos)
		{
			if (comPorta.Contains(id)) continue;
			if (SemPortaNestePort.ContainsKey(id)) continue;
			// OS SETE MULTIPLOS DE KAIO-KEN entram pelo verb-MAE: o cliente manda `Kaioken_20` e o
			// lote G2 confere o `Kaioken`, que a skill concede. Porta ha; ela so tem outro nome.
			if (id.StartsWith("Kaioken_", StringComparison.OrdinalIgnoreCase) && comPorta.Contains("Kaioken")) continue;
			semPorta.Add(id);
		}
		AfirmarCat("todo verb com corpo tem porta (alguma skill ou degrau o concede)",
				   semPorta.Count == 0, string.Join(" | ", semPorta));

		// ...E A LISTA DOS SEM PORTA TAMBEM NAO PODE ENVELHECER: no dia em que o vampirismo for
		// portado, o `Bite` ganha porta e esta linha exige que a excecao dele saia daqui.
		var jaTemPorta = SemPortaNestePort.Keys.Where(comPorta.Contains).ToList();
		AfirmarCat("...e nenhum dos declarados SEM PORTA ganhou uma sem que a lista soubesse",
				   jaTemPorta.Count == 0, string.Join(" | ", jaTemPorta));

		GD.Print($"[catalogo]    sem porta neste port, e DECLARADOS: "
			   + $"{string.Join(" | ", SemPortaNestePort.Select(p => $"{p.Key} <- {p.Value}"))}");

		// ============================ O DEFEITO INJETADO, E ELE E DE VERDADE ============================
		// Um verb registrado como portado que nao tem `case` no despacho: ele cai no `default`, diz
		// "ainda nao tem efeito" e nao mexe em nada. E EXATAMENTE o botao mentiroso que esta frente
		// existe pra matar -- e a varredura tem que pega-lo pela mesma porta por onde passou os outros.
		//
		// A PORTA TAMBEM E INJETADA: sem uma skill que o conceda, o oco morreria no `SabeTecnica` e a
		// bancada estaria provando que o GATE funciona (coisa que as bancadas de lote ja provam) em
		// vez de provar que a VARREDURA acha um efeito faltando. Entao o verb e pendurado numa skill
		// de verdade e despendurado depois -- a injecao dura tres linhas e nao sobrevive a familia.
		// ==========================================================================================
		Skill? cabide = _skills.Get("/datum/skill/ki/Boom_Wave");
		if (cabide == null) { AfirmarCat("ha uma skill pra pendurar a injecao", false); return; }

		string[] verbosDeVerdade = cabide.Verbos;
		try
		{
			cabide.Verbos = [.. verbosDeVerdade, "Verbo_Oco_Da_Bancada"];
			Tecnicas.Registrar("Verbo_Oco_Da_Bancada", "Verbo Oco da Bancada", Modo.Instantanea,
				"So existe durante a bancada: registrado como portado, sem efeito nenhum do outro lado.");

			(string vOco, string dOco) = ApertarUmVerbo("Verbo_Oco_Da_Bancada");
			AfirmarCat("...e um verb registrado SEM efeito e pego pela varredura (injecao)",
					   vOco == "vazio", $"{vOco}: {dOco}");
		}
		finally
		{
			cabide.Verbos = verbosDeVerdade;
		}
	}

	/// <summary>
	/// APERTA UM VERBO NA CENA E DIZ O QUE ACONTECEU. Devolve `agiu` / `recusou` / `vazio` / `estourou`.
	///
	/// A CENA E NOVA A CADA VERBO, e nao e zelo: as recargas do jogo sao POR CORPO (`basicCD` e do
	/// mob, nao da tecnica -- ver `--punhoteste`), entao reaproveitar o mesmo socador faria o segundo
	/// verb da lista ser recusado por causa do primeiro e a varredura mediria a recarga, nao o efeito.
	/// </summary>
	private (string Veredito, string Detalhe) ApertarUmVerbo(string id)
	{
		(ServerPlayer a, ServerPlayer d) = CenaDoCatalogo();

		int tirosAntes = ProjeteisDaZona(ZonaDaBancadaDeProjetil.Hash).Count;
		bool canalAntes = _canais.ContainsKey(a.Id);

		string antes = ImpressaoDoMundo(a, d);
		EscutaDeAvisos = [];
		string estouro = "";

		try { UsarHabilidade(a, id); }
		catch (Exception e) { estouro = e.GetType().Name + ": " + e.Message; }

		string depois = ImpressaoDoMundo(a, d);
		List<string> falou = EscutaDeAvisos ?? [];
		EscutaDeAvisos = null;

		int tirosDepois = ProjeteisDaZona(ZonaDaBancadaDeProjetil.Hash).Count;
		bool canalDepois = _canais.ContainsKey(a.Id);
		if (tirosDepois > tirosAntes || (canalDepois && !canalAntes))
			if (!_catAtiraram.Contains(id, StringComparer.OrdinalIgnoreCase)) _catAtiraram.Add(id);

		bool mudou = antes != depois;
		bool ausencia = falou.Exists(
			f => Array.Exists(FrasesDeAusencia, p => f.Contains(p, StringComparison.OrdinalIgnoreCase)));

		LimparTudoDoCatalogo();

		if (estouro.Length > 0) return ("estourou", estouro);
		if (mudou) return ("agiu", $"{(tirosDepois > tirosAntes ? $"{tirosDepois - tirosAntes} tiros; " : "")}"
								 + $"{falou.Count} fala(s)");
		if (ausencia) return ("vazio", string.Join(" / ", falou));
		if (falou.Count > 0) return ("recusou", falou[0]);
		return ("vazio", "nao mexeu em nada e nao disse nada");
	}

	/// <summary>
	/// A CENA: dois corpos colados, um marcando o outro, tudo aprendido, Ki e folego cheios, um
	/// membro ferido em cada um (pra que curar e regenerar tenham o que fazer).
	/// </summary>
	private (ServerPlayer A, ServerPlayer D) CenaDoCatalogo()
	{
		Vec2 chao = ChaoDaBancada();
		ServerPlayer a = ForjarQueSabeTudo("Varredor", chao);
		ServerPlayer d = ForjarQueSabeTudo("Boneco", chao + new Vec2(ZoneCollision.TileSize * 0.9f, 0));

		a.Facing = Facing.East;
		d.Facing = Facing.West;
		a.AlvoId = d.Id;
		d.AlvoId = a.Id;
		d.Combate.Bloqueando = false;

		// UM MEMBRO FERIDO EM CADA UM. Sem isto o Regenerar e a Respiracao Hamon respondem "seu corpo
		// ja esta inteiro", que e recusa correta -- e a varredura estaria medindo a cena, nao o verb.
		foreach (ServerPlayer p in new[] { a, d })
			if (p.Combate.Corpo.Partes.Find(x => x.Papel == Jandirus.Core.Combat.Vitalidade.Membro) is { } m)
			{
				m.Vida = m.VidaMax * 0.4;
				p.Combate.SincronizarVida();
			}

		return (a, d);
	}

	/// <summary>
	/// UM CORPO QUE SABE TUDO: o catalogo inteiro no livro e todo degrau no maximo.
	///
	/// E o unico jeito de a varredura medir o EFEITO em vez do gate: o `SabeTecnica` recusa antes de
	/// qualquer coisa acontecer, e um corpo comum ouviria "voce nao sabe" nos N verbos e a bancada
	/// ficaria verde sem ter apertado nada. Que o gate funciona, quem prova sao as bancadas de lote.
	/// </summary>
	private ServerPlayer ForjarQueSabeTudo(string nome, Vec2 onde)
	{
		ServerPlayer pl = Forjar(nome, onde, bp: 80_000_000);

		// SAIYAJIN, e nao Humano (o padrao do forjador). Uma raca so pode ser escolhida, e esta e a
		// que tem escada de transformacao: com um Humano o `DirectSSJ` responde "sua raca nao tem essa
		// escada" -- recusa CORRETA, e a varredura estaria medindo a cena em vez do efeito. As
		// tecnicas de outra raca continuam fora do alcance desta bancada, e isso esta dito aqui em vez
		// de escondido num numero verde.
		pl.Race = pl.Ficha.Race = "Saiyan";
		pl.Ficha.Statify();

		var save = new NivelSave();
		foreach (Skill s in _skills!.Todas)
		{
			if (s.Arvore) continue;
			pl.Livro!.Dar(s.Path);
			save.Skills[s.Path] = [99, 0];
		}
		pl.Niveis.DoSave(save);

		pl.Ficha.Ki = pl.Ficha.MaxKi = Math.Max(pl.Ficha.MaxKi, 5_000_000_000);
		pl.Ficha.stamina = pl.Ficha.maxstamina = Math.Max(pl.Ficha.maxstamina, 10_000_000);

		// VILAO DE PROPOSITO: o `Planet_Destroy` e o unico verb do jogo com gate de indole, e sem isto
		// ele sairia da varredura por uma recusa que nao diz nada sobre o efeito dele. A carga que ele
		// abre e desfeita no `LimparTudoDoCatalogo` -- ver o comentario de la.
		pl.Ficha.isVillain = true;
		return pl;
	}

	/// <summary>
	/// A IMPRESSAO DIGITAL DO MUNDO -- tirada por REFLEXAO, e essa e a decisao que faz a familia 2
	/// valer alguma coisa.
	///
	/// ============================ POR QUE NAO UMA LISTA DE CAMPOS ============================
	/// A alternativa obvia era comparar Ki, vida e posicao. Isso pega o soco e o tiro, e deixa passar
	/// tudo que e ESTADO: um buff que so mexe no `techniqueBuff`, uma recarga que so escreve num
	/// dicionario do servidor, uma cegueira que so entra num `HashSet`. A lista escolhida a dedo e
	/// exatamente onde o proximo efeito escaparia -- e, pior, ela envelhece calada: o campo novo de
	/// amanha nao esta nela, e a bancada continua verde.
	///
	/// Entao a impressao pega, sem escolher: TODOS os campos da ficha dos dois corpos, TODOS os campos
	/// do estado de combate, a vida e o estado de cada membro, e -- do lado do servidor -- o tamanho
	/// de TODA colecao e o valor de TODO campo simples declarado no `GameServer`. Um efeito novo entra
	/// na conta no dia em que e escrito, sem ninguem lembrar desta funcao.
	///
	/// E ela e barata onde precisa ser: roda duas vezes por verb, fora de qualquer tique -- a bancada
	/// e sincrona dentro do `_Ready`, entao nada mais esta mexendo no mundo enquanto ela mede.
	/// ====================================================================================
	/// </summary>
	private string ImpressaoDoMundo(params ServerPlayer[] corpos)
	{
		var sb = new System.Text.StringBuilder(4096);

		foreach (ServerPlayer p in corpos)
		{
			sb.Append(p.Id).Append('|').Append(p.Pos.X).Append(',').Append(p.Pos.Y).Append('|')
			  .Append(p.Facing).Append('|').Append(p.Voando).Append('|').Append(p.Zone.Hash).Append('|')
			  .Append(p.AlvoId).Append('|');

			Campos(sb, p.Ficha);
			Campos(sb, p.Combate);
			foreach (Jandirus.Core.Combat.BodyPart m in p.Combate.Corpo.Partes)
				sb.Append(m.Nome).Append('=').Append(m.Vida.ToString("0.###")).Append(m.Decepado ? 'D' : '.').Append(';');
			sb.Append('\n');
		}

		// O LADO DO SERVIDOR: toda colecao pelo tamanho, todo campo simples pelo valor. `DeclaredOnly`
		// corta a heranca do Godot (`Node` tem centenas de campos que nao dizem nada sobre o jogo).
		foreach (FieldInfo f in typeof(GameServer).GetFields(
					 BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
		{
			object? v;
			try { v = f.GetValue(this); }
			catch { continue; }

			switch (v)
			{
				case null: continue;
				case System.Collections.ICollection c: sb.Append(f.Name).Append('#').Append(c.Count).Append(';'); break;
				case string s: sb.Append(f.Name).Append('=').Append(s).Append(';'); break;
				case double dd: sb.Append(f.Name).Append('=').Append(dd.ToString("0.###")).Append(';'); break;
				case float ff: sb.Append(f.Name).Append('=').Append(ff.ToString("0.###")).Append(';'); break;
				case bool or int or long or Enum: sb.Append(f.Name).Append('=').Append(v).Append(';'); break;
			}
		}
		return sb.ToString();
	}

	/// <summary>Todos os campos publicos de um objeto, em ordem estavel. Ver <see cref="ImpressaoDoMundo"/>.</summary>
	private static void Campos(System.Text.StringBuilder sb, object alvo)
	{
		foreach (FieldInfo f in alvo.GetType()
					 .GetFields(BindingFlags.Instance | BindingFlags.Public)
					 .OrderBy(f => f.Name, StringComparer.Ordinal))
		{
			object? v;
			try { v = f.GetValue(alvo); }
			catch { continue; }

			switch (v)
			{
				case null: continue;
				case double dd: sb.Append(f.Name).Append('=').Append(dd.ToString("0.###")).Append(';'); break;
				case float ff: sb.Append(f.Name).Append('=').Append(ff.ToString("0.###")).Append(';'); break;
				case string or bool or int or long or Enum: sb.Append(f.Name).Append('=').Append(v).Append(';'); break;
			}
		}
	}

	// =====================================================================
	// 3) A IA CRESCE COM O JOGO
	// =====================================================================
	/// <summary>
	/// OS TIROS QUE A IA AINDA NAO SABE USAR -- divida DECLARADA, e ela e o produto desta familia.
	///
	/// A tabela `TecnicasDeLonge` e escrita a mao de proposito: cada linha declara alcance minimo,
	/// maximo e precisao, e **nenhum dos tres esta no DM** (o verb so diz `maxdistance`). Deriva-los
	/// automaticamente produziria uma IA que rejeita quase todo tiro por risco -- ver o cabecalho de
	/// la. Entao o arsenal NAO cresce sozinho, e a unica saida honesta e a divida ficar medida.
	///
	/// O que esta lista tem de diferente de um comentario e a origem: quem diz que estes verbos atiram
	/// **e a familia 2**, apertando cada um e vendo projetil nascer. Um tiro novo que entre no jogo
	/// aparece aqui sem ninguem escrever nada -- e ai a bancada exige que alguem decida a janela dele
	/// ou o declare em falta.
	/// </summary>
	private static readonly string[] AtiramEAIaNaoUsa =
	[
		// ---- os catorze do lote G5 que atiram de verdade (os outros do lote resolvem por raio) ----
		"Masenko", "Makkankosappo", "Massive_Beam", "Final_Flash", "Charged_Shot", "KillDriver",
		"BusterShell", "Scattershot", "Energy_Barrage", "Ki_Bomb", "Hellzone_Grenade", "Kienzan",
		"Paralysis", "Stunlock",

		// ---- os sete do lote G6 (os seis raios nomeados de cargo, mais o Kikoho) ----
		"Kamehameha", "GalicGun", "Death_Beam", "Dodompa", "Enkumei", "Boom_Wave", "Kikoho",

		// ---- e o Spirit Gun, que nao cabe na tabela por um motivo ESTRUTURAL e nao por trabalho ----
		// O campo se chama `Linha.CustoDeKi` e a IA compara com o Ki dela; o Spirit Gun cobra FOLEGO
		// (`usr.stamina -= kireq`, `Spirit.dm:352`). Registra-lo com custo em Ki faria a IA achar que
		// pode atirar sem folego -- ou o contrario, timida com o tanque cheio. Liga-lo pede um segundo
		// campo de MOEDA na linha, que e decisao de dado e nao remendo.
		"Spirit_Gun",
	];

	private void AIaCresceComOJogo()
	{
		GD.Print("[catalogo] -- 3) O ARSENAL DA IA CONTRA O QUE O JOGO REALMENTE ATIRA");

		if (_skills == null) { AfirmarCat("ha catalogo pra ler o arsenal", false); return; }

		var naTabela = new HashSet<string>(
			TecnicasDeLonge.Todas.Select(l => l.Id), StringComparer.OrdinalIgnoreCase);

		GD.Print($"[catalogo]    a familia 2 viu {_catAtiraram.Count} verbos porem tiro no mundo; "
			   + $"a tabela da IA tem {TecnicasDeLonge.Quantas} linhas");
		GD.Print($"[catalogo]    atiram: {string.Join(", ", _catAtiraram.Order(StringComparer.OrdinalIgnoreCase))}");

		AfirmarCat("a varredura viu tiro nascer (senao esta familia nao esta medindo nada)",
				   _catAtiraram.Count > 0);

		// A INVARIANTE: todo verb que ATIRA esta na tabela da IA ou na divida declarada. Nenhuma lista
		// escrita a mao decide quem atira -- quem decide e o jogo, apertando o botao.
		var fora = _catAtiraram.FindAll(
			v => !naTabela.Contains(v) && !AtiramEAIaNaoUsa.Contains(v, StringComparer.OrdinalIgnoreCase));
		AfirmarCat("todo verb que POE TIRO no mundo esta na tabela da IA ou na divida declarada",
				   fora.Count == 0, string.Join(" | ", fora));

		// ...e a divida declarada nao pode ter fantasma: um verb que ja entrou na tabela da IA nao
		// pode continuar listado como divida (o mesmo desencontro da familia 1, na outra tabela).
		var fantasmas = AtiramEAIaNaoUsa.Where(v => naTabela.Contains(v)).ToList();
		AfirmarCat("...e a divida declarada nao lista quem a IA ja usa",
				   fantasmas.Count == 0, string.Join(" | ", fantasmas));

		// ============================ O ARSENAL SAI DA PRODUCAO, PRA UM CORPO DE VERDADE ============================
		// `ArsenalDeLonge` e o mesmo metodo que o `LerCapacidades` chama a 1 Hz por corpo dirigido.
		// =======================================================================================================
		ServerPlayer npc = ForjarQueSabeTudo("CorpoDaIa", ChaoDaBancada());
		Arsenal arsenal = ArsenalDeLonge(npc);

		AfirmarCat($"um corpo que sabe tudo sai com as {TecnicasDeLonge.Quantas} tecnicas de longe",
				   arsenal.Quantas == TecnicasDeLonge.Quantas, $"{arsenal.Quantas} no arsenal");

		var noArsenal = new List<string>();
		for (int i = 0; i < arsenal.Quantas; i++) noArsenal.Add(arsenal[i].Id);
		AfirmarCat("...e sao exatamente as da tabela (nenhuma inventada, nenhuma faltando)",
				   naTabela.SetEquals(noArsenal), string.Join(" | ", noArsenal));

		// O PRECO NAO E COPIA: cada linha aponta pra funcao que o proprio efeito cobra.
		bool precoReal = true;
		for (int i = 0; i < arsenal.Quantas; i++)
		{
			TecnicasDeLonge.Linha? l = TecnicasDeLonge.Get(arsenal[i].Id);
			if (l == null || Math.Abs(arsenal[i].CustoDeKi - l.Value.CustoDeKi(npc.Ficha)) > 0.001)
				precoReal = false;
		}
		AfirmarCat("...e o custo de cada uma sai da MESMA funcao que o efeito cobra", precoReal);

		// E O CORPO QUE NAO SABE NADA SAI VAZIO -- sem isto o arsenal poderia estar sendo montado da
		// tabela e nao do livro, e a IA atiraria o que nunca aprendeu.
		ServerPlayer nu = Forjar("CorpoNu", ChaoDaBancada(), bp: 5_000);
		AfirmarCat("...e um corpo que nao comprou nada sai com arsenal VAZIO",
				   !ArsenalDeLonge(nu).TemAlguma);

		// ...e ESQUECER a skill tira a tecnica do arsenal (injecao: o elo livro -> arsenal e real).
		foreach (Skill s in _skills.Todas)
			if (!s.Arvore && s.Verbos.Contains("Scattering_Bullet", StringComparer.OrdinalIgnoreCase))
				npc.Livro!.Esquecer(s.Path);
		var save = new NivelSave();
		npc.Niveis.DoSave(save);   // sem degrau nenhum: os verbos de nivel tambem caem
		Arsenal depois = ArsenalDeLonge(npc);
		AfirmarCat("...e desaprender as skills TIRA as tecnicas do arsenal (injecao)",
				   depois.Quantas < arsenal.Quantas, $"{depois.Quantas} contra {arsenal.Quantas}");

		LimparTudoDoCatalogo();
	}

	// =====================================================================
	// 4) O CARGO DA E TIRA -- os trinta, o ciclo inteiro
	// =====================================================================
	/// <summary>
	/// AS SEIS SKILLS DE KIT QUE **NAO** SAEM COM O CARGO -- e o motivo de cada uma.
	///
	/// ============================ POR QUE ESTA LISTA E ESCRITA, E NAO DERIVADA ============================
	/// Derivar seria facil e seria inutil: bastaria perguntar ao proprio `DadivaDeCargo` quem ele
	/// considera revogavel, e a bancada concordaria com a producao por construcao -- o modo de falha
	/// "a checagem le a constante que devia defender", que este projeto ja pagou tres vezes.
	///
	/// Entao as seis estao aqui NA MAO, com a razao de cada uma, e a razao e sempre a mesma pergunta:
	/// *alguem que nunca teve cargo poderia ter comprado esta skill?* Se sim, tirar seria roubo.
	/// Uma skill nova de kit que pendure so em arvore de cargo e NAO saia faz esta familia ficar
	/// vermelha -- e e exatamente o exploit "reivindicar, receber, largar, ficar com" que a camada
	/// anterior fechou.
	/// ==================================================================================================
	/// </summary>
	private static readonly Dictionary<string, string> PodeFicarDepoisDoCargo = new(StringComparer.OrdinalIgnoreCase)
	{
		["/datum/skill/Telepathy"] = "pende das arvores `demon` e `kai` -- da pra comprar sem cargo",
		["/datum/skill/expand"] = "pende da `Body` e de seis raciais",
		["/datum/skill/general/observe"] = "pende de `demigod` e `kanassajin`",
		["/datum/skill/general/splitform"] = "pende de `kanassajin` e `majin`",
		["/datum/skill/general/selfdestruct"] = "pende de `android` e `saibaman`",

		// A EXCECAO QUE E PORTE DE UM DESCUIDO: o `growbranches()` de Namek chama
		// `enableskill(/datum/skill/Enkumei)` sem ele estar na `constituentskills` (`NamekRanks.dm:7-8`
		// contra `:19`) -- entao nem o `treeshrink` do original o tira. Manter e fiel, e o descuido e a
		// favor do jogador.
		["/datum/skill/Enkumei"] = "nao pende de arvore NENHUMA; nem o `treeshrink` do DM o tira",
	};

	private void OCargoDaETira()
	{
		GD.Print("[catalogo] -- 4) OS TRINTA CARGOS: REIVINDICAR, RECEBER, LARGAR, NAO DEIXAR NADA");

		if (_skills == null) { AfirmarCat("ha catalogo pra reconciliar", false); return; }

		// A FOTO DO MUNDO DE VERDADE -- esta familia escreve no `_tronos`, que e disco.
		var tronosReais = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		_tronos.Clear();

		try
		{
			// A PROMESSA EM TEXTO CONTRA A ENTREGA EM TYPEPATH. O `Concede[]` foi 100% orfao por
			// meses; hoje ele tem consumidor, e esta linha e o que impede o proximo cargo de nascer
			// prometendo em texto e entregando o vazio.
			var promessaVazia = new List<string>();
			foreach (RankDef r in Cargos.Todos)
			{
				if (r.Concede.Length == 0) continue;
				// "(nenhum: ...)" e uma promessa que declara nao ser promessa -- o DM nao da kit a
				// este cargo, e o proprio texto diz isso.
				if (r.Concede[0].StartsWith("(nenhum", StringComparison.OrdinalIgnoreCase)) continue;
				// O DEUS DA DESTRUICAO E O ANJO entregam DISCIPLINA e nao skill, por outro caminho
				// (`Disciplinas.RankQueEnsina`). A excecao e conferida no dado, e nao decorada aqui.
				if (Array.Exists(Disciplinas.Todas,
						dd => string.Equals(dd.RankQueEnsina, r.Chave, StringComparison.OrdinalIgnoreCase)))
					continue;

				string[] kit = DadivaDeCargo.De(r.Chave);
				if (kit.Length <= 1) promessaVazia.Add($"{r.Chave} promete {r.Concede.Length} e entrega 0");
			}
			AfirmarCat("nenhum cargo promete em TEXTO e entrega o vazio em TYPEPATH",
					   promessaVazia.Count == 0, string.Join(" | ", promessaVazia));

			// ============================ O CICLO, NOS TRINTA ============================
			int comKit = 0, semNadaNoFim = 0;
			var sobrou = new List<string>();
			var naoChegou = new List<string>();

			foreach (RankDef r in Cargos.Todos)
			{
				ServerPlayer pl = ForjarComConta($"pretendente_{r.Chave}", ChaoDaBancada());
				string[] kit = DadivaDeCargo.De(r.Chave);
				string[] doCatalogo = [.. kit.Where(p => _skills.Get(p) != null)];

				// 1. REIVINDICAR (o trono e o mesmo mapa que a reivindicacao escreve).
				_tronos[r.Chave] = pl.Conta;
				ReconciliarDadiva(pl);

				var faltou = doCatalogo.Where(p => !pl.Livro!.Sabe(p)).ToList();
				if (faltou.Count > 0) naoChegou.Add($"{r.Chave}: {string.Join(",", faltou)}");
				if (doCatalogo.Length > 1) comKit++;

				// 2. VESTIR O ESTILO que o cargo deu, se ele deu um -- e e aqui que mora o vazamento
				//    que ninguem via: o estilo nao mora no livro, mora na ficha.
				string estiloDoKit = "";
				foreach (string p in doCatalogo)
					if (_skills.Get(p) is { Estilo.Length: > 0 } s) estiloDoKit = s.Estilo;
				if (estiloDoKit.Length > 0)
				{
					pl.Ficha.EstiloAtual = estiloDoKit;
					AplicarEstilo(pl);
				}

				// 3. LARGAR o cargo.
				_tronos.Remove(r.Chave);
				ReconciliarDadiva(pl);

				// 4. O QUE SOBROU. O livro INTEIRO, e nao so o kit: uma skill que o kit nunca deu mas
				//    que a reconciliacao deixou entrar tambem e sobra.
				var restou = pl.Livro!.Aprendidas
					.Where(p => !PodeFicarDepoisDoCargo.ContainsKey(p)).ToList();
				if (restou.Count > 0) sobrou.Add($"{r.Chave}: {string.Join(",", restou)}");
				else semNadaNoFim++;

				// 5. E OS DEZ MULTIPLICADORES do estilo tem que ter voltado a 1. Escrever o corte no
				//    livro nao e aplicar o corte na ficha.
				if (estiloDoKit.Length > 0)
				{
					bool despiu = pl.Ficha.EstiloAtual.Length == 0
							   && Math.Abs(pl.Ficha.physoffStyle - 1) < 0.0001
							   && Math.Abs(pl.Ficha.kioffStyle - 1) < 0.0001
							   && Math.Abs(pl.Ficha.speedStyle - 1) < 0.0001;
					if (!despiu)
						sobrou.Add($"{r.Chave}: o estilo '{estiloDoKit}' continuou VESTIDO "
								 + $"(physoffStyle={pl.Ficha.physoffStyle:0.###})");
				}

				LimparTudoDoCatalogo();
			}

			AfirmarCat($"os cargos com kit entregam tudo que o kit promete ({comKit} deles)",
					   naoChegou.Count == 0, string.Join(" | ", naoChegou));

			AfirmarCat($"...e largar o cargo nao deixa nada alem das SEIS declaradas, "
					 + $"nos {Cargos.Todos.Length} cargos",
					   sobrou.Count == 0, string.Join(" | ", sobrou));

			AfirmarCat("...e o ciclo foi rodado mesmo (24 cargos com kit, e o livro limpo nos trinta)",
					   semNadaNoFim == Cargos.Todos.Length && comKit > 20,
					   $"{semNadaNoFim} limpos, {comKit} com kit");

			// A LISTA DO QUE PODE FICAR NAO PODE VIRAR ESCONDERIJO. Se alguem pusesse um ESTILO nela,
			// a afirmacao de cima ficaria verde com o exploit de volta -- e o exploit e justamente
			// esse: reivindicar o Eremita, receber o KameStyle, largar o cargo e ficar com os dez
			// multiplicadores pra sempre.
			var estiloEscondido = PodeFicarDepoisDoCargo.Keys
				.Where(p => _skills.Get(p) is { Estilo.Length: > 0 }).ToList();
			AfirmarCat("...e nenhum ESTILO de luta entrou na lista do que pode ficar",
					   estiloEscondido.Count == 0, string.Join(" | ", estiloEscondido));

			// ============================ O QUE NAO PODE SAIR ============================
			// O Eremita Tartaruga ENSINA o Kamehameha (`teacher = TRUE`) a quem nao tem cargo nenhum.
			// O tique de 1 Hz comia a licao um segundo depois, calado, so porque o typepath comeca
			// com `rank/`. A escapatoria e o `wastaught` do DM.
			// =========================================================================
			ServerPlayer aluno = ForjarComConta("aluno_sem_cargo", ChaoDaBancada());
			aluno.Livro!.DarComoEnsinada("/datum/skill/rank/Kamehameha");
			ReconciliarDadiva(aluno);
			AfirmarCat("a licao de um mestre SOBREVIVE a reconciliacao (o `wastaught` do DM)",
					   aluno.Livro.Sabe("/datum/skill/rank/Kamehameha"));

			// E A INJECAO: a skill posta na mao SEM ter sido ensinada, sem cargo nenhum, tem que sair.
			ServerPlayer trapaceiro = ForjarComConta("sem_cargo_com_kit", ChaoDaBancada());
			trapaceiro.Livro!.Dar("/datum/skill/style/KameStyle");
			trapaceiro.Livro.Dar("/datum/skill/rank/Kamehameha");
			trapaceiro.Ficha.EstiloAtual = "KameStyle";
			AplicarEstilo(trapaceiro);

			// A POSTURA TEM QUE ESTAR VESTIDA ANTES -- senao a linha de baixo mediria um estilo que
			// nunca subiu, e "os multiplicadores voltaram a 1" seria verdade desde o comeco. E a
			// primeira das tres formas de bancada verde mentirosa da armadilha 7 ("precondicao ja
			// satisfeita"), e ela ja passou por aqui uma vez com o id do estilo errado.
			AfirmarCat("...e o estilo do kit estava mesmo VESTIDO antes (a precondicao existe)",
					   Math.Abs(trapaceiro.Ficha.physoffStyle - 1) > 0.0001,
					   $"physoffStyle {trapaceiro.Ficha.physoffStyle:0.###}");

			ReconciliarDadiva(trapaceiro);
			AfirmarCat("...e quem NAO tem cargo perde o kit inteiro na reconciliacao (injecao)",
					   !trapaceiro.Livro.Sabe("/datum/skill/style/KameStyle")
					   && !trapaceiro.Livro.Sabe("/datum/skill/rank/Kamehameha"),
					   string.Join(",", trapaceiro.Livro.Aprendidas));

			AfirmarCat("...e a postura dele cai junto (escrever o corte E aplicar o corte)",
					   trapaceiro.Ficha.EstiloAtual.Length == 0
					   && Math.Abs(trapaceiro.Ficha.physoffStyle - 1) < 0.0001,
					   $"estilo '{trapaceiro.Ficha.EstiloAtual}', physoffStyle {trapaceiro.Ficha.physoffStyle:0.###}");
		}
		finally
		{
			LimparTudoDoCatalogo();
			_tronos.Clear();
			foreach ((string k, string v) in tronosReais) _tronos[k] = v;
			SalvarCargos();
		}
	}

	/// <summary>
	/// UM PEDACO DE CHAO LIVRE -- e ele RECICLA o corredor, que e a diferenca desta bancada pras
	/// outras.
	///
	/// ============================ ESTE BUG ESTAVA ESCONDIDO NO `stderr` ============================
	/// `CorredorLivre` anda tres faixas por chamada e desiste na faixa 250 -- cerca de oitenta
	/// corredores, que sempre bastaram porque uma bancada de lote forja uma duzia de corpos. Esta aqui
	/// forja mais de CENTO E TRINTA (um par por verbo varrido, mais um por cargo), entao a partir da
	/// octogesima chamada ela ouvia "varredura falhou" e recebia o canto (64,64) -- possivelmente
	/// dentro de parede, e sempre o mesmo lugar.
	///
	/// E NAO APARECIA: a recusa sai por `GD.PrintErr`, que no processo headless vai pro `stderr`, e o
	/// `stderr` estava sendo lido em outro arquivo. Cinquenta reprovacoes por rodada, contadas no
	/// placar da bancada de PROJETIL (o dono do metodo), enquanto esta imprimia 47/0. E a mesma
	/// familia de defeito que esta sessao inteira persegue -- so que dentro da propria bancada, e por
	/// isso fica escrito aqui e nao num commit calado.
	///
	/// A RECICLAGEM E LEGITIMA PORQUE O CHAO ESTA VAZIO: cada verbo termina com
	/// <see cref="LimparTudoDoCatalogo"/>, entao nenhum corpo da chamada anterior continua no mundo pra
	/// virar alvo desta. As bancadas de lote nao podem reciclar (elas mantem corpos vivos entre
	/// familias); esta pode, e por isso a diferenca mora aqui e nao la.
	/// ==========================================================================================
	/// </summary>
	private Vec2 ChaoDaBancada()
	{
		_pjProximoCorredor = 8;
		return CorredorLivre(24);
	}

	/// <summary>Um corpo de bancada com CONTA propria -- os cargos moram por conta, e nao por corpo.</summary>
	private ServerPlayer ForjarComConta(string conta, Vec2 onde)
	{
		ServerPlayer pl = Forjar(conta, onde, bp: 1_000_000);
		pl.Conta = $"bancada_catalogo_{conta}";
		return pl;
	}

	// =====================================================================
	// 5) A RE-EXTRACAO NAO QUEBROU NADA
	// =====================================================================
	private void ARextracaoNaoQuebrouNada()
	{
		GD.Print("[catalogo] -- 5) O CATALOGO DE HOJE CONTRA O MARCO CONGELADO");

		if (_skills == null) { AfirmarCat("ha catalogo pra comparar", false); return; }

		string json = LerFonteDaBancada("Assets/Data/catalogo-marco.json");
		AfirmarCat("o marco do catalogo esta no disco e foi lido", json.Length > 1000, $"{json.Length} chars");
		if (json.Length < 1000) return;

		MarcoDoCatalogo.Marco marco = MarcoDoCatalogo.Ler(json);
		GD.Print($"[catalogo]    marco: {marco.Origem}");
		AfirmarCat($"o marco tem as skills de la ({marco.Skills.Count})", marco.Skills.Count > 300);

		// OS DEGRAUS DE HOJE SAEM DO JSON, e nao do que o servidor carregou -- ver
		// `MarcoDoCatalogo.LerNiveisDeHoje`. Comparar arquivo com memoria media o CARREGADOR junto, e
		// a bancada acusava 33 perdas que nao existiam.
		Dictionary<string, string[]> niveisDeHoje =
			MarcoDoCatalogo.LerNiveisDeHoje(LerFonteDaBancada("Assets/Data/niveis.json"));
		AfirmarCat($"o `niveis.json` de hoje foi lido ({niveisDeHoje.Count} skills com regra)",
				   niveisDeHoje.Count > 100);

		MarcoDoCatalogo.Diferenca d = MarcoDoCatalogo.Comparar(marco, _skills, niveisDeHoje);

		GD.Print($"[catalogo]    antes -> depois: {d.Nasceram.Count} nasceram, {d.Cresceram.Count} cresceram, "
			   + $"{d.Sumiram.Count} sumiram, {d.Emudeceram.Count} emudeceram, "
			   + $"{d.VerbosPerdidos.Count} verbos perdidos, {d.Explicadas.Count} perdas explicadas");
		foreach (string e in d.Explicadas) GD.Print($"[catalogo]      explicada: {e}");

		AfirmarCat("nenhuma skill que existia SUMIU do catalogo",
				   d.Sumiram.Count == 0, string.Join(" | ", d.Sumiram));
		AfirmarCat("nenhuma skill que FAZIA alguma coisa emudeceu",
				   d.Emudeceram.Count == 0, string.Join(" | ", d.Emudeceram));
		AfirmarCat("nenhum verbo deixou de ser concedido (por skill OU por degrau)",
				   d.VerbosPerdidos.Count == 0, string.Join(" | ", d.VerbosPerdidos));

		// ============================ O DEFEITO INJETADO ============================
		// Um marco sintetico com uma skill que fazia e uma que dava verbo: as duas TEM que ser
		// acusadas. Sem isto, um `Comparar` que devolvesse listas vazias sempre passaria com nota
		// cheia -- que e o modo de falha que este projeto ja teve tres vezes.
		// ==========================================================================
		var fajuto = new MarcoDoCatalogo.Marco
		{
			Skills =
			[
				new MarcoDoCatalogo.Linha { Path = "/datum/skill/que/nunca/existiu", Buffs = 2 },
				new MarcoDoCatalogo.Linha { Path = "/datum/skill/ki/Boom_Wave", Verbos = ["Verbo_Que_Ela_Nao_Da"] },
			],
			VerbosDeDegrau =
			{
				["/datum/skill/boxing"] = ["Verbo_De_Degrau_Que_Sumiu"],
				["/datum/skill/tree/Que_Nunca_Subiu"] = [],
			},
		};
		MarcoDoCatalogo.Diferenca inj = MarcoDoCatalogo.Comparar(fajuto, _skills, niveisDeHoje);
		AfirmarCat("...e o detector acusa as QUATRO perdas: skill sumida, regra de nivel sumida, "
				 + "verbo de skill e verbo de DEGRAU (injecao)",
				   inj.Sumiram.Count == 2 && inj.VerbosPerdidos.Count == 2,
				   $"sumiram {inj.Sumiram.Count}, verbos {inj.VerbosPerdidos.Count}");

		// ============================ E A TABELA DE PERDAS EXPLICADAS TEM QUE ESTAR VIVA ============================
		// Uma tabela de excecoes que nunca dispara e indistinguivel de tabela nenhuma -- e o corolario
		// da regra 0.7 da casa ("um teto que nunca e atingido e indistinguivel de teto nenhum"). A
		// re-extracao desta frente perdeu UMA coisa: a arvore `Focused`, inteira dentro de um `/* */`
		// no DM, que o extrator tratava como viva porque a guarda de comentario nao funcionava. E ela
		// TEM que aparecer aqui como perda explicada -- se parar de aparecer, ou o marco mudou ou a
		// perda deixou de ser detectada, e nos dois casos alguem precisa olhar.
		// ========================================================================================================
		AfirmarCat("a unica perda desta re-extracao e a arvore comentada, e ela esta EXPLICADA",
				   d.Explicadas.Count == 1
				   && d.Explicadas[0].Contains("Focused", StringComparison.Ordinal),
				   string.Join(" | ", d.Explicadas));

		// ...e a re-extracao TEM que ter trazido alguma coisa: um marco identico ao hoje nao prova
		// nada sobre um extrator que parou de funcionar.
		AfirmarCat("o catalogo de hoje cobre o marco inteiro (nada ficou pra tras sem dono)",
				   d.Intacto);
	}

	// =====================================================================
	// 6) COBERTURA E CONTRADICAO
	// =====================================================================
	private void CoberturaESemContradicao()
	{
		GD.Print("[catalogo] -- 6) TODO VERB ESTA EM UMA LISTA, E EM UMA SO");

		if (_skills == null) { AfirmarCat("ha catalogo pra classificar", false); return; }

		CensoDeSkills.Relatorio r = CensoDeSkills.Levantar(_skills, RegrasDeNivel.VerbosDeDegrau);

		AfirmarCat($"ZERO verbos sem cobertura (de {r.VerbosTotal})",
				   r.VerbosSemCobertura == 0, string.Join(" | ", r.SemCobertura));

		// ============================ A OUTRA METADE, QUE FALTAVA ============================
		// A `--censoteste` prova que nenhum verb fica FORA das tres listas. Ela nao tem como provar
		// que nenhum esta em DUAS -- e essa e a forma que o desencontro tomou desta vez: catorze
		// punhos portados continuaram declarando que esperavam o motor de combos.
		// ================================================================================
		List<string> contra = CensoDeSkills.Contradicoes(
			Tecnicas.Vivas, CensoDeSkills.PorOutroCanal, CensoDeSkills.Esperando);

		AfirmarCat("nenhum verb esta em DUAS listas ao mesmo tempo (portado E esperando, por exemplo)",
				   contra.Count == 0, string.Join(" | ", contra));

		// O DEFEITO INJETADO: as tres formas de contradicao, UMA DE CADA, pra que a conta diga qual
		// forma escapou se alguma escapar.
		List<string> inj = CensoDeSkills.Contradicoes(
			["Vivo_E_Na_Fila", "Vivo_E_No_Canal"],
			new Dictionary<string, string> { ["Vivo_E_No_Canal"] = "canal_x", ["Canal_E_Fila"] = "canal_y" },
			new Dictionary<string, string> { ["Vivo_E_Na_Fila"] = "sistema_z", ["Canal_E_Fila"] = "sistema_w" });
		AfirmarCat("...e o detector pega as tres formas dela (injecao)", inj.Count == 3,
				   string.Join(" | ", inj));

		// E O AGRUPAMENTO CONTINUA FECHANDO -- o que sobrou de mudo esta todo dentro de algum grupo.
		int soma = 0;
		foreach ((string _, int quantos) in r.PorSistema) soma += quantos;
		AfirmarCat("os mudos continuam agrupados por sistema, e a soma bate com o total",
				   soma == r.VerbosEsperando, $"{soma} vs {r.VerbosEsperando}");

		GD.Print("[catalogo]    o que falta, por sistema (os cinco maiores):");
		foreach ((string sistema, int quantos) in r.PorSistema.Take(5))
			GD.Print($"[catalogo]      {quantos,3}  {sistema}");
	}

	// =====================================================================
	// LIMPEZA
	// =====================================================================
	/// <summary>
	/// TIRA DO MUNDO TUDO QUE ESTA BANCADA POS NELE.
	///
	/// A CARGA DE DESTRUICAO E O UNICO ESTADO QUE PRECISA DE LINHA PROPRIA: a varredura aperta o
	/// `Planet_Destroy` com um corpo de vilao, e ele abre uma contagem de trinta segundos sobre a
	/// Terra. O tique dela desiste sozinho quando o dono some do `_players` (`TickDaCargaDeDestruicao`),
	/// mas depender disso seria deixar meia duzia de contagens vivas dentro de um servidor que vai
	/// abrir a porta em seguida -- e a regra da casa e limpar o que se sujou, no mesmo bloco.
	/// </summary>
	private void LimparTudoDoCatalogo()
	{
		foreach (int id in _cargaDoPlanetDestroy.Keys.ToList())
			if (id >= IdBaseDeProjetil) _cargaDoPlanetDestroy.Remove(id);

		LimparTudoDaBancada();
	}
}
