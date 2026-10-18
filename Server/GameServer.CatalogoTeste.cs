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
			ORelogNaoHerda();
			AAposentadaSaiu();
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
		// carrega Godot e mesmo assim conta o progresso.
		//
		// O DESCRITOR NAO E MAIS ESCRITO DUAS VEZES: os lotes registram so o CORPO (`Vivo`), e o
		// texto vive so em `Tecnicas.Portadas.cs`. Mas a conta continua podendo nao fechar nas duas
		// direcoes -- um lote pode ganhar um `Vivo` sem descritor, e um descritor pode sobreviver ao
		// corpo que ele descrevia. Por isso o lado "vivo" vem do REGISTRO DO SERVIDOR
		// (<see cref="TecnicasComCorpo"/>) e nao de `Tecnicas.Vivas`, que hoje E o espelho: comparar
		// o espelho com ele mesmo seria uma prova que nunca reprova.
		// =====================================================================
		(List<string> soNoJogo, List<string> soNoEspelho) =
			Tecnicas.DesencontroDoEspelho(TecnicasComCorpo(), Tecnicas.NoEspelho);

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
		// (2026-10-16) a porta dela e o ESTADO, e nao skill -- ver `PortaPeloEstado`.
		["Sycophantic_Rage"] =
			"so o Corrupted Majin que destravou a Forma Pura a sabe (`PortaPeloEstado`), e o corpo da varredura nao fez a saga -- "
			+ "faze-la trocaria o corpo, o BP e a forma da cena dos outros verbos. Quem prova o efeito e a `--sagamajinteste` [11]",

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

		// ---- lote G12: as duas absorcoes exigem um caido, como o Life Suck acima ----
		["Soul_Absorb"] =
			"o alvo precisa estar NOCAUTEADO e vivo (`demon.dm:41`); nocautear o boneco faria os outros verbos "
			+ "medirem um alvo caido. Quem o prova com um caido de verdade e a `--g12teste`",
		["Absorb_Android"] =
			"o alvo precisa estar NOCAUTEADO e vivo (`Absorption.dm:22`), pelo mesmo motivo do de cima; a "
			+ "`--g12teste` prova os dois ramos (o androide absorvido e o dreno sustentado)",

		// ---- lote G9: o unico que precisa de uma COISA no chao, e nao de uma condicao no corpo ----
		["Mafuba"] =
			"precisa de um POTE SELANTE assentado a vista, e a cena da varredura e so dois corpos. Nao "
			+ "e limitacao da bancada: no DM o verb abre um `input()` com a lista dos `SealingItem` em "
			+ "`view()` e um `isnull(choice)` cancela (`Sealing.dm:158-164`) -- **Mafuba sem pote nao "
			+ "existe no jogo original**. Assentar um pote so pra este verb mudaria a cena dos outros "
			+ "noventa (o pote e denso e entra na colisao). Quem o prova de ponta a ponta, com pote, "
			+ "fita e preso, e a `--seloteste`",

		// ---- lote G10: a Trindade tem porta, e a porta e uma CASA (escolha unica) ----
		// O degrau 2 da Holy Trinity concede UM dos tres conforme a casa escolhida (`verbosporcasa`,
		// `Bodybuilding.dm:180-185`). O corpo da varredura aprende tudo mas NAO escolhe casa nenhuma --
		// e escolher uma faria os outros dois recusar de qualquer jeito: sao exclusivos. A recusa aqui e
		// do gate ("voce nao sabe"), e quem prova os tres pelo caminho de producao (Dar + Escolher +
		// nivel 2) e a `--g10teste`.
		["Taunt"] =
			"e da CASA Van-sama da Trindade (escolha unica): o corpo da varredura nao escolheu casa. "
			+ "A recusa e do gate; a `--g10teste` prova o efeito com a casa escolhida",
		["Counter_Taunt"] =
			"e da CASA Aniki da Trindade (escolha unica): o corpo da varredura nao escolheu casa. "
			+ "A recusa e do gate; a `--g10teste` prova o dano mental com a casa escolhida",
		["Slap"] =
			"e da CASA Ricardo da Trindade (escolha unica): o corpo da varredura nao escolheu casa. "
			+ "A recusa e do gate; a `--g10teste` prova o atordoamento com a casa escolhida",

		// ---- lote G11: um que exige um AGARRADO e um cuja lista depende de pericia e de poder relativo ----
		["Self_Destruct"] =
			"exige alguem AGARRADO (`Ki/misc.dm:223-225`, \"You need to be grabbing someone!\"); agarrar o "
			+ "boneco mudaria a cena dos outros noventa (preso nao anda, nao ataca). Quem prova as duas "
			+ "fases, a morte do agarrado e o dano em si e a `--g11teste`",
		["Instant_Transmission"] =
			"a lista de assinaturas (`yardrat.dm:185`) so aceita farol com `BP <= eBP*fam/(2*max(dist/30,0.2)*50/teleskill)`: "
			+ "com a pericia de estreia (1) e o boneco colado, de poder igual, e preciso um farol 20x mais "
			+ "forte -- a cena da varredura nao tem um, e o DM tambem nao acharia. A `--g11teste` prova a "
			+ "lista, a concentracao parada, a chegada com carona e a pericia crescendo",
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

		// ---- lote G11: os dois saltos de planeta com carona (o desenho do `RiftTeleport`) e a projecao ----
		// `Observe:<nome>` escreve `observingnow` na ficha de quem projeta e responde com a leitura do alvo:
		// o primeiro aperto LISTA quem da pra seguir, o segundo age (e o campo e o que a varredura ve mudar).
		["Devil_Bringer"] = "Devil_Bringer:Namek",
		["Kai_Kai"] = "Kai_Kai:Namek",
		["Observe"] = "Observe:Boneco",

		// ---- lote G13: os tres do sistema de estudo, todos do mesmo desenho ----
		// Os tres do DM abrem `input()`: `Study_Other` lista quem esta a vinte tiles (`:57-61`),
		// `Focus_Skill` e `Write_Teachings` listam as SUAS skills da Mente (`:88`, `:151-154`). Aqui o
		// id nu responde a lista e o id com argumento age -- e cada um mexe num registro do servidor
		// (`_estudoG13`, `_focoG13`, `_escritaG13`), que e o que a impressao do mundo enxerga.
		//
		// O ALVO DO ESTUDO E O "Boneco" da cena (o segundo corpo); as duas skills escolhidas sao a
		// `Ki Unlocked`, que o corpo da varredura sabe no nivel 99.
		// ---- lote G15: o Materializar abre uma ESCOLHA (o ritual do DM perguntava "Make what?", `Rituals_Manipulation.dm:528`).
		// O aperto nu responde o que da pra materializar; o com argumento cria o item no chao e cobra o Ki -- e e o Ki
		// que a impressao do mundo ve mudar.
		["Materialize"] = "Materialize:pesos",

		["Study_Other"] = "Study_Other:Boneco",
		["Focus_Skill"] = "Focus_Skill:Ki Unlocked",
		["Write_Teachings"] = "Write_Teachings:Ki Unlocked",
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

		// A TRINDADE MORAVA AQUI ate 2026-09-02 ("a escolha do TrinityType, degrau em quarentena no
		// extrator"). O extrator passou a ler o `switch(TrinityType)` do degrau 2 como `verbosporcasa`
		// (`Bodybuilding.dm:180-185`), o `RegrasDeNivel.VerbosDeDegrau` passou a conta-los, e a linha
		// "nenhum dos declarados SEM PORTA ganhou uma sem que a lista soubesse" e a que exigiu a saida
		// deles daqui -- exatamente o que ela existe pra fazer. Ver `RecusamPorCondicao` (a casa).
	};

	/// <summary>
	/// (2026-10-16) OS VERBOS COM CORPO QUE O **ESTADO** CONCEDE -- nem skill nem degrau --, e cada um diz qual estado. Nao sao "sem
	/// porta" (ha porta, e ela funciona): a porta so nao mora no catalogo de skills, e sim no servidor (o `SabeTecnica`).
	///
	/// SO A SYCOPHANTIC RAGE HOJE: ela vem do sistema do amigo do dono, sem nada no DM, e chega com a Forma Pura destravada do
	/// Corrupted Majin. A lista e conferida nas duas direcoes, como a de cima -- o dia em que uma skill a conceder, ela sai daqui.
	/// </summary>
	private static readonly Dictionary<string, string> PortaPeloEstado = new(StringComparer.OrdinalIgnoreCase)
	{
		["Sycophantic_Rage"] = "a Forma Pura destravada do Corrupted Majin (`GameServer.SagaDoMajin.ComAFormaPura`, no `SabeTecnica`)",
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
			if (PortaPeloEstado.ContainsKey(id)) continue;   // (2026-10-16) ver `PortaPeloEstado`
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

		// (2026-10-16) E A LISTA DOS QUE TEM PORTA PELO ESTADO, PELA MESMA REGUA NAS DUAS DIRECOES: cada um ainda e verb com corpo, e
		// nenhum ganhou uma skill que o conceda sem que a lista soubesse (ai a porta passou pro catalogo, e a linha daqui sobra).
		var estadoVelho = PortaPeloEstado.Keys
			.Where(v => comPorta.Contains(v) || !vivos.Contains(v, StringComparer.OrdinalIgnoreCase)).ToList();
		AfirmarCat("...e os de porta PELO ESTADO continuam vivos e sem skill que os conceda",
				   estadoVelho.Count == 0, string.Join(" | ", estadoVelho));

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
		string estouro = "";
		List<string> falou = Ouvir(() =>
		{
			try { UsarHabilidade(a, id); }
			catch (Exception e) { estouro = e.GetType().Name + ": " + e.Message; }
		});

		string depois = ImpressaoDoMundo(a, d);

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

		// UM MEMBRO FERIDO EM CADA UM. Sem isto o Regenerar responde "seu corpo ja esta inteiro", que e
		// recusa correta -- e a varredura estaria medindo a cena, nao o verb.
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
		string[] tudo = [.. _skills!.Todas.Where(s => !s.Arvore).Select(s => s.Path)];
		ServerPlayer pl = ForjarComSkills(nome, onde, bp: 80_000_000, skills: tudo,
										  degraus: [.. tudo.Select(p => (p, 99))]);

		// SAIYAJIN, e nao Humano (o padrao do forjador). Uma raca so pode ser escolhida, e esta e a
		// que tem escada de transformacao: com um Humano o `DirectSSJ` responde "sua raca nao tem essa
		// escada" -- recusa CORRETA, e a varredura estaria medindo a cena em vez do efeito. As
		// tecnicas de outra raca continuam fora do alcance desta bancada, e isso esta dito aqui em vez
		// de escondido num numero verde. O tanque vem DEPOIS da troca: o `Statify` refaz o MaxKi.
		pl.Race = pl.Ficha.Race = "Saiyan";
		pl.Ficha.Statify();

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
	/// A IA USA O QUE O JOGO TEM (v0.2.1) -- a divida que esta familia media foi paga.
	///
	/// Ate a v0.2.0 morava aqui uma lista, `AtiramEAIaNaoUsa`: os trinta verbos que punham tiro no mundo e que a
	/// tabela da IA nao conhecia. Ela era divida DECLARADA -- e o pedido do dono de 2026-10-13 (*"deveriam usar todas
	/// as skills disponiveis do jogador"*) a zerou: todo verb que atira tem linha em `TecnicasDeLonge`, e o resto do
	/// que tem corpo esta em `TecnicasDeLuta` ou na lista do que nao e gesto de luta (`TecnicasDeLuta.ForaDaLuta`).
	///
	/// O que continua valendo e a ORIGEM da medida: quem diz que um verb atira **e a familia 2**, apertando cada um e
	/// vendo projetil nascer. Um tiro novo que entre no jogo aparece aqui sem ninguem escrever nada -- e a bancada
	/// reprova ate alguem decidir a janela dele.
	/// </summary>

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

		// A INVARIANTE: todo verb que ATIRA esta na tabela da IA. Nenhuma lista escrita a mao decide quem
		// atira -- quem decide e o jogo, apertando o botao. (A lista de divida que esta linha aceitava
		// ficou vazia na v0.2.1, e saiu.)
		var fora = _catAtiraram.FindAll(v => !naTabela.Contains(v));
		AfirmarCat("todo verb que POE TIRO no mundo esta na tabela de longe da IA",
				   fora.Count == 0, string.Join(" | ", fora));

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

		OQueAIaUsaCobreOJogo();
		OPrecoQueAIaAcredita();
	}

	/// <summary>
	/// A REGUA DO "TODAS" (v0.2.1): todo verb com corpo esta numa das duas tabelas da IA -- a de longe ou a de luta --
	/// ou na lista do que nao e gesto de luta, com o motivo escrito. E nas duas direcoes: linha de tabela (ou de
	/// lista) que aponta pra um verb que nao existe mais e tao defeito quanto verb sem linha.
	///
	/// AS FAMILIAS POR PREFIXO (`Kaioken_20`, `Final_Explosion_3`) respondem pelo nome da familia.
	/// </summary>
	private void OQueAIaUsaCobreOJogo()
	{
		GD.Print("[catalogo] -- 3b) TODO VERB VIVO ESTA NA MAO DA IA, OU FORA DELA COM MOTIVO");

		// (o verb oco e da PROPRIA bancada: a familia 2 o registra pra provar que a varredura enxerga um verb que nao
		// faz nada -- ele nao e do jogo)
		List<string> vivos = [.. Tecnicas.Vivas.Where(v => v != "Verbo_Oco_Da_Bancada")];
		static bool DaFamilia(string id, string familia) =>
			string.Equals(id, familia, StringComparison.OrdinalIgnoreCase)
			|| id.StartsWith(familia + "_", StringComparison.OrdinalIgnoreCase);
		static string SemArgumento(string id) => id.IndexOf(':') is var c and >= 0 ? id[..c] : id;

		bool NaIa(string id) =>
			TecnicasDeLonge.Get(id) != null || TecnicasDeLuta.Get(id) != null
			|| TecnicasDeLuta.ForaDaLuta.Keys.Any(k => DaFamilia(id, k));

		var semLinha = vivos.Where(v => !NaIa(v)).Order(StringComparer.OrdinalIgnoreCase).ToList();
		AfirmarCat($"**A IA USA TODAS**: cada um dos {vivos.Count} verbos com corpo esta na tabela de longe "
				 + $"({TecnicasDeLonge.Quantas} linhas), na de luta ({TecnicasDeLuta.Quantas}) ou na lista do que nao e de luta "
				 + $"({TecnicasDeLuta.ForaDaLuta.Count}, cada um com o motivo)",
				   semLinha.Count == 0, "sem linha: " + string.Join(" | ", semLinha));

		bool Existe(string familia) => vivos.Exists(v => DaFamilia(v, familia));
		var fantasmas = new List<string>();
		foreach (TecnicasDeLonge.Linha l in TecnicasDeLonge.Todas) if (!Existe(l.Id)) fantasmas.Add("longe:" + l.Id);
		foreach (TecnicasDeLuta.Linha l in TecnicasDeLuta.Todas) if (!Existe(l.Gate)) fantasmas.Add("luta:" + l.Gate);
		foreach (string k in TecnicasDeLuta.ForaDaLuta.Keys) if (!Existe(k)) fantasmas.Add("fora:" + k);
		AfirmarCat("...e nenhuma linha (nem da lista do que fica de fora) aponta pra um verb que nao existe",
				   fantasmas.Count == 0, string.Join(" | ", fantasmas));

		var nasDuas = TecnicasDeLuta.ForaDaLuta.Keys
			.Where(k => TecnicasDeLonge.Get(k) != null || TecnicasDeLuta.Get(k) != null).ToList();
		foreach (TecnicasDeLuta.Linha l in TecnicasDeLuta.Todas)
			if (TecnicasDeLonge.Get(SemArgumento(l.Id)) != null) nasDuas.Add(l.Id);
		AfirmarCat("...e nenhum verb esta em duas casas ao mesmo tempo (usado E declarado fora; de longe E de luta)",
				   nasDuas.Count == 0, string.Join(" | ", nasDuas));

		AfirmarCat("...e cada um dos que ficam de fora tem o motivo escrito",
				   TecnicasDeLuta.ForaDaLuta.Values.All(m => m.Trim().Length >= 8),
				   string.Join(" | ", TecnicasDeLuta.ForaDaLuta.Where(kv => kv.Value.Trim().Length < 8).Select(kv => kv.Key)));

		// O CORPO QUE SABE TUDO sai com o repertorio de luta inteiro -- menos o que ele nao tem como saber.
		ServerPlayer npc = ForjarQueSabeTudo("CorpoDaIa", ChaoDaBancada());
		Arsenal luta = ArsenalDeLuta(npc, out bool temInvestida, out bool sabeRebater);
		var noRepertorio = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < luta.Quantas; i++) noRepertorio.Add(luta[i].Id);
		var naoSabe = new List<string>();
		foreach (TecnicasDeLuta.Linha l in TecnicasDeLuta.Todas) if (!noRepertorio.Contains(l.Id)) naoSabe.Add(l.Id);
		var porque = naoSabe.Where(id => !NaoTemPorta.ContainsKey(SemArgumento(id))).ToList();
		AfirmarCat($"um corpo que sabe tudo sai com {luta.Quantas} das {TecnicasDeLuta.Quantas} tecnicas de luta -- e as que "
				 + "faltam sao as que ele nao tem como saber (sem porta neste port, ou de uma casa que so um corpo escolhe)",
				   luta.Quantas > 40 && porque.Count == 0 && temInvestida && sabeRebater,
				   $"de fora: {string.Join(", ", naoSabe)}; sem motivo: {string.Join(", ", porque)}");
		LimparTudoDoCatalogo();
	}

	/// <summary>As tecnicas de luta que o corpo da varredura NAO sabe, e por que -- as mesmas razoes de `RecusamPorCondicao` e `SemPortaNestePort`.</summary>
	private static readonly Dictionary<string, string> NaoTemPorta = new(StringComparer.OrdinalIgnoreCase)
	{
		["Bite"] = "nao tem porta neste port (vem de ser vampiro ou lobisomem) -- ver `SemPortaNestePort`",
		["Slap"] = "e da casa Ricardo da Trindade: o corpo da varredura nao escolheu casa",
		["Counter_Taunt"] = "e da casa Aniki da Trindade: o corpo da varredura nao escolheu casa",
	};

	/// <summary>
	/// A REGUA DO PRECO (v0.2.1): a tabela da IA nunca promete MAIS BARATO do que o verb cobra.
	///
	/// As linhas novas ESPELHAM a conta do verb em vez de chamar a mesma funcao (os verbs dos lotes G5 a G14
	/// escrevem o custo dentro do efeito). Espelho envelhece -- e o lado perigoso e o da tabela ficar pra baixo: a IA
	/// acharia que pode pagar, pediria, e ouviria "isso pede N de energia" a luta inteira. Entao cada linha e
	/// APERTADA numa cena nova (dois corpos colados, tanque cheio), e o que saiu do tanque -- nas duas moedas -- tem
	/// de caber no preco que a linha declara pra aquele corpo.
	/// </summary>
	private void OPrecoQueAIaAcredita()
	{
		GD.Print("[catalogo] -- 3c) O PRECO QUE A IA ACREDITA NUNCA E MENOR QUE O QUE O VERB COBRA");

		var linhas = new List<(string Id, Func<Fighter, double> Ki, Func<Fighter, double>? Folego)>();
		foreach (TecnicasDeLonge.Linha l in TecnicasDeLonge.Todas) linhas.Add((l.Id, l.CustoDeKi, l.CustoDeFolego));
		foreach (TecnicasDeLuta.Linha l in TecnicasDeLuta.Todas) linhas.Add((l.Id, l.CustoDeKi, l.CustoDeFolego));

		var barato = new List<string>();
		var absurdo = new List<string>();
		int cobraram = 0;
		foreach ((string id, Func<Fighter, double> precoKi, Func<Fighter, double>? precoFolego) in linhas)
		{
			(ServerPlayer a, _) = CenaDoCatalogo();
			Fighter f = a.Ficha;
			double tabelaKi = precoKi(f), tabelaFolego = precoFolego?.Invoke(f) ?? 0;
			// (o tanque de ANTES: os reforcos refazem os atributos ao ligar, e o `Statify` reescreve o `MaxKi` da cena)
			double ki0 = f.Ki, folego0 = f.stamina, tanque = f.MaxKi, pulmao = f.maxstamina;
			Ouvir(() => { try { UsarHabilidade(a, id); } catch (Exception) { /* a familia 2 ja cobra que nenhum estoura */ } });
			double cobrouKi = ki0 - f.Ki, cobrouFolego = folego0 - f.stamina;
			if (cobrouKi > 1e-9 || cobrouFolego > 1e-9) cobraram++;
			if (cobrouKi > tabelaKi * (1 + 1e-9) + 1e-6) barato.Add($"{id}: tabela {tabelaKi:0.##} de Ki, o verb cobrou {cobrouKi:0.##}");
			if (cobrouFolego > tabelaFolego * (1 + 1e-9) + 1e-6) barato.Add($"{id}: tabela {tabelaFolego:0.##} de folego, o verb cobrou {cobrouFolego:0.##}");
			// O OUTRO LADO: a tabela dizer "nao da pra pagar" de um verb que o jogo ACEITOU. (O Escudo de Energia e o
			// caso legitimo -- o preco dele cresce com o quadrado do tanque, e neste corpo de tanque gigante o proprio
			// verb recusa: a tabela e ele concordam.)
			if ((tabelaKi > tanque && cobrouKi > 1e-9) || (tabelaFolego > pulmao && cobrouFolego > 1e-9))
				absurdo.Add($"{id}: a tabela pede {tabelaKi:0} de Ki num tanque de {tanque:0}, e o verb cobrou {cobrouKi:0}");
			LimparTudoDoCatalogo();
		}

		AfirmarCat($"**A TABELA DA IA NUNCA PROMETE MAIS BARATO DO QUE O VERB COBRA** -- as {linhas.Count} linhas apertadas, uma a uma, "
				 + $"numa cena nova ({cobraram} cobraram alguma coisa)",
				   barato.Count == 0 && cobraram > 40, string.Join(" | ", barato));
		AfirmarCat("...e nenhuma pede mais que o tanque inteiro por um verb que o jogo aceita (um preco que ninguem paga e uma tecnica que a IA nunca usa)",
				   absurdo.Count == 0, string.Join(" | ", absurdo));
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
		//
		// (A SEGUNDA nao e da extracao: e a `Saiyan Power`, que o dono tirou do jogo em 2026-10-10. Ela
		// passa pela mesma tabela -- sumir do catalogo e perda ate alguem dizer de quem e -- e a familia 8
		// cobra o resto: o reembolso de quem a tinha.)
		//
		// (A TERCEIRA e de 2026-10-11: as duas skills e a arvore do sistema VELHO de ataques customizados, que
		// vinham de `.dm` que o `.dme` nao inclui -- o jogo original nao as compila. Sairam quando o extrator
		// passou a ler so o que o compilador le; a familia 8 cobra o livro de quem tivesse uma.)
		//
		// (A QUARTA e de 2026-10-13: as cinco maestrias de forma por skill, legado que o proprio DM trocou pela
		// maestria em % -- pendiam de arvore sem acender por caminho nenhum. Ver `SkillsAposentadas`.)
		//
		// (A QUINTA e de 2026-10-16: a Respiracao Hamon, que nao e de Dragon Ball -- o dono mandou tirar qualquer traco dela.)
		//
		// SAO DEZESSETE LINHAS DE CINCO DONOS: a arvore comentada so tinha regra de nivel (uma linha), a skill
		// aposentada sumiu do catalogo E perdeu as regras de nivel (duas), as tres do arquivo fora do `.dme` so sumiram
		// do catalogo (tres: nenhuma tinha regra de nivel), as cinco maestrias sumiram do catalogo (cinco) e quatro
		// delas perderam as regras de nivel (quatro: a `forms/mssj` nunca teve), e a Hamon sumiu do catalogo e das regras (duas).
		string[] maestrias = ["/forms/ssj ", "/forms/mssj ", "/forms/lssjm ", "/heran/MstedMXPWR ", "/heran/MstedTMXPWR "];
		AfirmarCat("as perdas contra o marco tem CINCO donos e nenhum outro: a arvore comentada, a skill que o dono aposentou, as tres do arquivo fora do .dme, as cinco maestrias legadas e a Respiracao Hamon",
				   d.Explicadas.Count == 17
				   && d.Explicadas.Count(e => e.Contains("/HamonBreathing", StringComparison.Ordinal)) == 2
				   && d.Explicadas.Count(e => e.Contains("Focused", StringComparison.Ordinal)) == 1
				   && d.Explicadas.Count(e => e.Contains("Saiyan_Power", StringComparison.Ordinal)) == 2
				   && d.Explicadas.Count(e => e.Contains("/CustomAttacks/ki/", StringComparison.Ordinal)) == 2
				   && d.Explicadas.Count(e => e.Contains("/tree/Custom_Attacks", StringComparison.Ordinal)) == 1
				   && d.Explicadas.Count(e => maestrias.Any(m => e.Contains(m, StringComparison.Ordinal))) == 9,
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

	// =====================================================================
	// 7) O RELOG NAO HERDA
	// =====================================================================
	private const string PathDoKiShield = "/datum/skill/Cultivation/Ki_Shield";

	/// <summary>
	/// O ESTADO POR ID NAO SOBREVIVE AO CORPO. Os lotes guardam recarga, carga e escudo em dicionarios
	/// por id de jogador, e id se reusa: um corpo ergue o Ki Shield e SAI pelo funil de producao
	/// (<see cref="DespedirCorpo"/>, a metade do logout que apaga o id); o proximo corpo com o MESMO id
	/// aperta Ki Shield e tem que GANHAR defesa. Herdar a entrada de `_escudoAtivo` faria o toggle
	/// SUBTRAIR um bonus que este corpo nunca somou -- defesa abaixo da que nasceu.
	///
	/// A INJECAO no fim esvazia o registro (<see cref="_aoEsquecer"/>) e exige ver a heranca acontecer:
	/// e o que prova que a familia mede o registro, e nao um acaso da ordem dos ids.
	/// </summary>
	private void ORelogNaoHerda()
	{
		GD.Print("[catalogo] -- 7) O RELOG NAO HERDA: o estado por id morre com o corpo");
		Vec2 chao = ChaoDaBancada();

		ServerPlayer a = ForjarComSkills("QuemSai", chao, 1_000_000, skills: [PathDoKiShield], kiMin: 1_000_000);
		int id = a.Id;
		double defDeNascenca = a.Ficha.Tphysdef;
		UsarHabilidade(a, "Ki_Shield");
		AfirmarCat("um corpo ergue o Ki Shield: o registro guarda o bonus e a defesa sobe",
				   _escudoAtivo.TryGetValue(id, out double bonus) && bonus > 0 && a.Ficha.Tphysdef > defDeNascenca,
				   $"bonus {bonus:0.##}, Tphysdef {defDeNascenca:0.##} -> {a.Ficha.Tphysdef:0.##}");

		// O RESTO DO ESTADO POR ID que o logout nunca limpava: entra a mao, tem que sair pelo funil.
		long daquiAUmMinuto = NowMs() + 60_000;
		_prontoG3[id] = daquiAUmMinuto;
		_prontoG1[id] = daquiAUmMinuto;
		_cegoAte[id] = daquiAUmMinuto;
		_solarPronto[id] = daquiAUmMinuto;
		_cargaG3[id] = new CargaG3 { Raio = 3, Ancora = a.Pos, ComecouMs = NowMs(), ProximoMs = daquiAUmMinuto };
		_pontoCronoG4[id] = new PontoCronoG4 { Zona = a.Zone, Pos = a.Pos };
		FrasesDeG4(id)[0] = "segredo de quem saiu";

		DespedirCorpo(a);   // o logout de verdade, da linha `_players.Remove` em diante
		string sobrou = string.Join(", ", new[]
		{
			_escudoAtivo.ContainsKey(id) ? "escudo" : "", _prontoG3.ContainsKey(id) ? "prontoG3" : "",
			_prontoG1.ContainsKey(id) ? "prontoG1" : "", _cegoAte.ContainsKey(id) ? "cegoAte" : "",
			_solarPronto.ContainsKey(id) ? "solarPronto" : "", _cargaG3.ContainsKey(id) ? "cargaG3" : "",
			_pontoCronoG4.ContainsKey(id) ? "pontoCrono" : "", _frasesG4.ContainsKey(id) ? "frasesG4" : "",
		}.Where(s => s.Length > 0));
		AfirmarCat("...e sair do mundo pelo funil do logout apaga TODO o estado por id (base, G1, G3, G4)",
				   sobrou.Length == 0, $"sobrou: {sobrou}");

		// O PROXIMO CORPO REUSA O ID -- e o `_nextId` reciclado do jogo, montado a mao.
		_pjCorpos--;
		ServerPlayer b = ForjarComSkills("QuemEntra", chao, 1_000_000, skills: [PathDoKiShield], kiMin: 1_000_000);
		AfirmarCat("o corpo seguinte nasce com o MESMO id", b.Id == id, $"{b.Id} vs {id}");
		double defAntes = b.Ficha.Tphysdef;
		UsarHabilidade(b, "Ki_Shield");
		AfirmarCat("...e ao erguer o escudo GANHA defesa: nao herdou a entrada do anterior",
				   b.Ficha.Tphysdef > defAntes && _escudoAtivo.ContainsKey(id),
				   $"Tphysdef {defAntes:0.##} -> {b.Ficha.Tphysdef:0.##}");
		UsarHabilidade(b, "Ki_Shield");
		AfirmarCat("...e desligar devolve exatamente o que somou",
				   Math.Abs(b.Ficha.Tphysdef - defAntes) < 1e-9 && !_escudoAtivo.ContainsKey(id));

		// A INJECAO: com o registro VAZIO o relog herda, e erguer o proprio escudo DERRUBA a defesa.
		List<Action<int>> inscritos = [.. _aoEsquecer];
		_aoEsquecer.Clear();
		try
		{
			UsarHabilidade(b, "Ki_Shield");
			DespedirCorpo(b);
			bool herdou = _escudoAtivo.ContainsKey(id);
			_pjCorpos--;
			ServerPlayer c = ForjarComSkills("Herdeiro", chao, 1_000_000, skills: [PathDoKiShield], kiMin: 1_000_000);
			double antes = c.Ficha.Tphysdef;
			UsarHabilidade(c, "Ki_Shield");
			AfirmarCat("[injecao] sem os inscritos o id reusado HERDA o escudo, e erguer o proprio DERRUBA a defesa -- a regra sabe ficar vermelha",
					   herdou && c.Ficha.Tphysdef < antes, $"herdou={herdou}, Tphysdef {antes:0.##} -> {c.Ficha.Tphysdef:0.##}");
		}
		finally
		{
			_aoEsquecer.AddRange(inscritos);
		}

		LimparTudoDoCatalogo();
	}

	// =====================================================================
	// 8) A SKILL QUE O DONO TIROU DO JOGO
	// =====================================================================
	/// <summary>
	/// *"remova essa skill, pq ela n tem mais funçao no jogo, pode deletar ela completamente"* (dono,
	/// 2026-10-10, sobre a `Saiyan Power`). O que "completamente" quer dizer, em quatro lugares:
	///
	///   1. o CATALOGO que o jogo carrega nao a tem, e nenhuma arvore a lista -- e isto e lido do
	///      `skills.json` no DISCO, entao uma re-extracao que a trouxesse de volta reprova aqui;
	///   2. ela nao tem regra de nivel (`niveis.json`);
	///   3. o SAVE de quem a tinha: ela sai do livro e o marco volta -- so pra quem a COMPROU;
	///   4. o efeito dela saiu do servidor (cobrado na `--g11teste`, familia 9).
	///
	/// O DEFEITO INJETADO (`SkillsAposentadas.NaoReembolsaDeTeste`) e o reembolso que nao acontece: sem
	/// ele, tirar uma skill do jogo tiraria um marco de cada personagem que a escolheu, calado.
	/// </summary>
	private void AAposentadaSaiu()
	{
		GD.Print("[catalogo] -- 8) A SKILL QUE O DONO TIROU DO JOGO (Saiyan Power)");
		if (_skills == null) { AfirmarCat("ha catalogo pra conferir", false); return; }

		const string path = "/datum/skill/saiyan/Saiyan_Power";
		const string dura = "/datum/skill/general/Hardened_Body", vontade = "/datum/skill/general/Willed";

		AfirmarCat("a Saiyan Power esta na lista das aposentadas, com o custo que tinha (1 marco)",
				   SkillsAposentadas.Saiu(path) && SkillsAposentadas.Todas.Any(a => a.Path == path && a.Custo == 1));
		var quemCita = _skills.Todas.Where(s => s.Galhos.Contains(path, StringComparer.OrdinalIgnoreCase)
												|| s.PreReqs.Contains(path, StringComparer.OrdinalIgnoreCase))
									.Select(s => s.Path).ToList();
		AfirmarCat("ela NAO esta no catalogo que o jogo carrega, e nenhuma arvore nem pre-requisito a cita",
				   _skills.Get(path) == null && quemCita.Count == 0, string.Join(" | ", quemCita));
		AfirmarCat("...e nao tem regra de nivel", RegrasDeNivel.Get(path) == null);
		AfirmarCat("as vizinhas dela na arvore dos Saiyajins continuam la (a arvore perdeu UM galho, e nao a lista)",
				   _skills.Get(dura) != null && _skills.Get(vontade) != null
				   && _skills.Get("/datum/skill/tree/saiyan/SaiyanRacial") is { } arv
				   && arv.Galhos.Contains(dura, StringComparer.OrdinalIgnoreCase) && arv.Galhos.Length == 4,
				   $"{_skills.Get("/datum/skill/tree/saiyan/SaiyanRacial")?.Galhos.Length} galho(s)");

		// O SAVE DE QUEM A COMPROU: tres marcos ganhos, nenhum livre, tres skills -- uma delas a aposentada.
		ServerPlayer pl = Forjar("cat: o Saiyajin de save antigo", CorredorLivre(2), bp: 1_000);
		CharacterSave Antigo() => new()
		{
			Skills = [dura, vontade, path],
			MarcosTotais = 3,
			MarcosLivres = 0,
			Niveis = new NivelSave { Skills = { [path] = [2, 5] } },
		};

		PrepararSkills(pl, Antigo());
		AfirmarCat("no login, quem a COMPROU a perde do livro e recebe o marco de volta (3 ganhos, 0 livres -> 1 livre)",
				   !pl.Livro.Sabe(path) && pl.Livro.Sabe(dura) && pl.Livro.Sabe(vontade)
				   && pl.Livro.MarcosLivres == 1 && pl.Livro.MarcosTotais == 3,
				   $"sabe {pl.Livro.Sabe(path)}, livres {pl.Livro.MarcosLivres}, totais {pl.Livro.MarcosTotais}");

		List<string> falas = Ouvir(() => AvisarDasAposentadas(pl));
		AfirmarCat("...e e AVISADO, com o nome da skill e o marco que voltou -- uma vez so",
				   Disse(falas, "Saiyan Power") && Disse(falas, "1 marco") && pl.SkillsQueSairam.Count == 0
				   && Ouvir(() => AvisarDasAposentadas(pl)).Count == 0, string.Join(" | ", falas));

		pl.Niveis.DoSave(Antigo().Niveis);
		AfirmarCat("o nivel dela nao volta do save (sem regra, o progresso nem carrega)",
				   pl.Niveis.Nivel(path) == 0 && pl.Niveis.Exp(path) == 0);

		// A COPIA ENSINADA nao custou marco: sai do livro, e nao devolve nada.
		CharacterSave ensinada = Antigo();
		ensinada.SkillsEnsinadas = [path];
		ensinada.MarcosLivres = 1;
		PrepararSkills(pl, ensinada);
		AfirmarCat("quem a tinha por ENSINO tambem a perde, e nao ganha marco por isso (1 livre continua 1)",
				   !pl.Livro.Sabe(path) && !pl.Livro.FoiEnsinada(path) && pl.Livro.MarcosLivres == 1,
				   $"livres {pl.Livro.MarcosLivres}");

		// QUEM NUNCA A TEVE nao e tocado.
		CharacterSave semEla = Antigo();
		semEla.Skills = [dura];
		semEla.MarcosLivres = 2;
		PrepararSkills(pl, semEla);
		AfirmarCat("quem nunca a teve nao e tocado (2 livres continuam 2, nada pra avisar)",
				   pl.Livro.MarcosLivres == 2 && pl.SkillsQueSairam.Count == 0);

		// AS TRES DO ARQUIVO FORA DO `.dme` (dono, 2026-10-11: *"pode tirar"*): duas skills e uma arvore do sistema
		// VELHO de ataques customizados, que o jogo original nao compila. O catalogo que o jogo carrega nao as tem;
		// as duas folhas estao na lista das aposentadas, e o save que tiver uma (so por admin: nenhuma arvore as
		// oferecia) perde o path orfao e recebe o marco.
		const string raio = "/datum/skill/CustomAttacks/ki/beam/Beam", bola = "/datum/skill/CustomAttacks/ki/blast/Blast",
					 arvoreVelha = "/datum/skill/tree/Custom_Attacks";
		AfirmarCat("as tres do sistema VELHO de ataques customizados (arquivo fora do .dme) NAO estao no catalogo, nada as cita, e as duas folhas estao entre as aposentadas",
				   _skills.Get(raio) == null && _skills.Get(bola) == null && _skills.Get(arvoreVelha) == null
				   && !_skills.Todas.Any(s => s.Galhos.Concat(s.PreReqs).Any(p => p is raio or bola or arvoreVelha))
				   && SkillsAposentadas.Saiu(raio) && SkillsAposentadas.Saiu(bola));
		CharacterSave comAsDuas = Antigo();
		comAsDuas.Skills = [dura, raio, bola];
		PrepararSkills(pl, comAsDuas);
		List<string> falasDasDuas = Ouvir(() => AvisarDasAposentadas(pl));
		AfirmarCat("quem as tivesse no livro as perde no login, com 1 marco de volta por cada (0 livres -> 2) e o recado das duas",
				   !pl.Livro.Sabe(raio) && !pl.Livro.Sabe(bola) && pl.Livro.Sabe(dura) && pl.Livro.MarcosLivres == 2
				   && Disse(falasDasDuas, "Beam") && Disse(falasDasDuas, "Blast"),
				   $"livres {pl.Livro.MarcosLivres}; {string.Join(" | ", falasDasDuas)}");

		// A RESPIRACAO HAMON (dono, 2026-10-16: *"coisas nao oficiais de dragon ball nem eram pra entrar nesse port, se colocou favor
		// remover qualquer traço disso"*): era COMUM, de 2 marcos. Saiu do catalogo, das regras de nivel e do servidor -- o verb dela nao e
		// mais tecnica de ninguem --, e quem a comprou recebe os 2 de volta. (A pergunta da tecnica e a lista das VIVAS: o `Tecnicas.Get`
		// nunca devolve nulo -- ele cria a entrada nao portada de quem pergunta.)
		const string hamon = "/datum/skill/HamonBreathing";
		AfirmarCat("a Respiracao Hamon (nao e de Dragon Ball) esta entre as aposentadas com o custo que tinha (2 marcos), NAO esta no catalogo, nada a cita, nao tem regra de nivel, e o verb dela nao e mais tecnica",
				   SkillsAposentadas.Todas.Any(a => a.Path == hamon && a.Custo == 2) && _skills.Get(hamon) == null
				   && !_skills.Todas.Any(s => s.Galhos.Concat(s.PreReqs).Any(p => string.Equals(p, hamon, StringComparison.OrdinalIgnoreCase)))
				   && RegrasDeNivel.Get(hamon) == null && !Tecnicas.Vivas.Contains("HamonBreathing", StringComparer.OrdinalIgnoreCase),
				   $"aposentada {SkillsAposentadas.Saiu(hamon)}; no catalogo {_skills.Get(hamon) != null}; regra de nivel {RegrasDeNivel.Get(hamon) != null}; tecnica viva {Tecnicas.Vivas.Contains("HamonBreathing", StringComparer.OrdinalIgnoreCase)}");
		CharacterSave comHamon = Antigo();
		comHamon.Skills = [dura, hamon];
		comHamon.MarcosLivres = 0;
		PrepararSkills(pl, comHamon);
		List<string> falasDoHamon = Ouvir(() => AvisarDasAposentadas(pl));
		AfirmarCat("quem a comprou a perde no login e recebe os 2 marcos de volta (0 livres -> 2), com o recado",
				   !pl.Livro.Sabe(hamon) && pl.Livro.Sabe(dura) && pl.Livro.MarcosLivres == 2
				   && Disse(falasDoHamon, "Hamon Breathing") && Disse(falasDoHamon, "2 marco"),
				   $"livres {pl.Livro.MarcosLivres}; {string.Join(" | ", falasDoHamon)}");

		try
		{
			SkillsAposentadas.NaoReembolsaDeTeste = true;
			PrepararSkills(pl, Antigo());
			AfirmarCat("(defeito injetado: a skill sai e o marco nao volta) a mesma regua REPROVA -- quem a comprou fica com 0 livres",
					   !pl.Livro.Sabe(path) && pl.Livro.MarcosLivres == 0, $"livres {pl.Livro.MarcosLivres}");
		}
		finally { SkillsAposentadas.NaoReembolsaDeTeste = false; }
	}
}
