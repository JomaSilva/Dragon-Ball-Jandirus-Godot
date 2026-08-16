namespace Jandirus.Core.Ranks;

/// <summary>
/// O QUE UM CARGO REALMENTE ENTREGA -- as arvores de skill concedidas por rank.
///
/// ============================ O QUE ESTE ARQUIVO CONSERTA ============================
/// A `RankDef.Concede` e uma lista de TEXTO ("estilo Kame", "Kamehameha", "Makankosappo"), e o
/// relatorio do catalogo mediu o tamanho do buraco: **`grep` por `.Concede` fora do proprio
/// `Ranks.cs` nao devolvia nada**. Trinta cargos declaravam o que davam e nenhum deles dava. Era o
/// mesmo defeito que o sigilo de BP ja custou uma vez a este projeto -- API escrita e 100% orfa --,
/// so que com trinta entradas em vez de uma.
///
/// Aqui a promessa vira TYPEPATH. Cada linha desta tabela e uma linha `enableskill(...)` de um
/// `growbranches()` do DM, e a fonte esta anotada em cada bloco.
/// ====================================================================================
///
/// ============================ COMO O DM FAZ, E COMO ESTE PORT FAZ ============================
/// No original a skill de cargo nasce `enabled = 0` (155 delas). A arvore do cargo -- Earth,
/// Otherworld, Space, Namek -- roda `growbranches()` no login e no `Rank_Verb_Assign`, e ali um
/// `switch(savant.Rank)` LIGA as poucas que aquele cargo pode ter. O jogador ainda paga o marco
/// (skillcost 0-3) pra aprender.
///
/// O port **da a skill**, em vez de liberar a compra. Dois motivos:
///
///   1. no catalogo do port a skill de cargo esta `ligada = 0` e **nao pende de arvore nenhuma que
///      raca ou classe conceda** -- `PodeAprender` a recusaria duas vezes (`Desligada`,
///      `SemArvore`). Liberar a compra exigiria um terceiro conceito ("arvore emprestada pelo
///      cargo") pra chegar no mesmo lugar;
///   2. `SkillBook.PenduraEmArvoreDe` ja documenta a saida certa pra esse caso, e ela e esta:
///      *"skill SOLTA nao e comprada, e ENSINADA -- e `Dar()` nao passa por aqui"*. Cargo que
///      concede skill encaixa exatamente ali.
///
/// O custo em marcos se perde, e e o que se perde de proposito: no DM ele e 0 ou 1 pra quase tudo
/// desta tabela (`skillcost=0 //Rank skills should be cheap to the rank owner`, RankTree.dm:20).
/// ============================================================================================
///
/// ============================ E O QUE SE TIRA AO PERDER O CARGO ============================
/// **O que so um cargo poderia ter dado, e que ninguem ensinou.** A conta e feita do catalogo em
/// <see cref="LevantarRevogaveis"/>, e a excecao em <see cref="Revogavel"/>; as duas citam o
/// `treeshrink` do original, que o autor do DM teve que reescrever pela mesma razao e deixou o
/// motivo comentado (`OtherworldRanks.dm:23-27`) -- *"o Sr. Kaioh ensina Kaio-ken e Spirit Bomb de
/// graca e as duas ficam desabilitadas pra quem nao e Kaio do Norte; um Grand Kai perdia a tecnica
/// que o NPC deu"*.
///
/// A PRIMEIRA VERSAO DESTE ARQUIVO ERRAVA AQUI, e vale ficar escrito: ela tirava so o namespace
/// `/datum/skill/rank/` e argumentava que o resto *"existe em outras arvores e em outras bocas"*.
/// Vale pra `expand`, `observe`, `splitform`, `selfdestruct` e `Telepathy` -- e **nao** vale pros
/// sete ESTILOS, pra `Heal`, `kikoho`, `Mystic`, `Majin` e `kaioken`, que so pendem de arvore de
/// cargo. Reivindicar um cargo, receber o estilo e largar o cargo era acumulacao de graca.
/// ========================================================================================
///
/// ============================ AS 24 CONCESSOES MORTAS DO DM -- DIVERGENCIA DELIBERADA ============================
/// **LEIA ISTO ANTES DE "CONSERTAR" ALGUMA LINHA DESTA TABELA DE VOLTA.** Este port entrega ONZE
/// skills que o original tambem manda entregar e **nao entrega**, em 24 linhas `enableskill(...)`
/// que sao no-op silencioso no BYOND. A generosidade e de PROPOSITO e o motivo esta escrito abaixo,
/// para que quem ler o DM lado a lado nao ache que o port inventou kit.
///
/// O MECANISMO DA MORTE, no proprio codigo do original (`Skills/trees.dm:172-176`):
///
///     datum/skill/tree/proc/enableskill(var/skilltype)
///         for(var/datum/skill/S in constituentskills)   // <- SO a lista da PROPRIA arvore
///             if(S.type == skilltype) S.enabled = 1
///
/// `enableskill` casa contra a `constituentskills` **daquela arvore**. Skill que nao esta na lista
/// nao e ligada, nao e recusada e nao avisa ninguem: o laco varre, nao acha, e volta. Nove das onze
/// nao sao constituintes de arvore NENHUMA do jogo inteiro -- ou seja, **nenhum jogador do BYOND
/// jamais pode ter possuido essas nove por meio algum**.
///
///     skill                    linhas  cargos afetados                            situacao no DM
///     -----------------------  ------  -----------------------------------------  --------------------
///     rank/Revive                   9  Demon Lord, King_Of_Hell, Grand Kai,       arvore nenhuma
///                                      Supreme Kai, W/E/N/S Kai, King Yemma       (`OtherworldRanks.dm:61,71,81,90,102,110,118,125,129`)
///     rank/Ritual_of_Might          3  Demon Lord, Grand Kai, Supreme Kai         arvore nenhuma (`:64,83,93`)
///     rank/Makkankosappo            2  Guardiao da Terra, Anciao de Namek         arvore nenhuma (`EarthRanks.dm:25`, `NamekRanks.dm:19`)
///     Enkumei                       2  Anciao e Grande Anciao de Namek            arvore nenhuma (`NamekRanks.dm:20,25`)
///     rank/DeathBall                2  Capitao dos Piratas, Geti Star King        arvore nenhuma (`SpaceRanks.dm:18,21`)
///     rank/BusterShell              1  Kaio do Leste                              arvore nenhuma (`OtherworldRanks.dm:109`)
///     rank/Judge                    1  King Yemma                                 arvore nenhuma (`OtherworldRanks.dm:127`)
///     rank/Paralysis                1  Geti Star King                             arvore nenhuma (`SpaceRanks.dm:22`)
///     rank/FinalFlash               1  Rei de Vegeta                              arvore nenhuma (`SpaceRanks.dm:27`)
///     style/GodStyle                1  Guardiao da Terra                          consta na arvore do Outro Mundo, nao na da Terra (`EarthRanks.dm:21`)
///     general/observe               1  Guardiao da Terra                          idem (`EarthRanks.dm:28`)
///
/// As duas ultimas sao de uma familia diferente e por isso estao separadas: a skill EXISTE noutra
/// arvore, entao um Kaio consegue `GodStyle` e `observe` normalmente -- **so o Guardiao da Terra**
/// e prejudicado, porque a arvore da Terra nao os lista.
///
/// POR QUE O PORT DIVERGE:
///   1. **e esquecimento, e da pra provar.** Ninguem escreve nove vezes `enableskill(Revive)` --
///      em nove cargos diferentes, dois deles em arquivos diferentes -- querendo que nao aconteca
///      nada. O `Revive` de cargo tem ate `skillcost = 2`, o mais caro de todos os kits
///      (`OtherworldRankSkills.dm:130`): alguem precificou uma skill que o jogo nunca entregou;
///   2. **portar o silencio de um `switch` incompleto nao e fidelidade.** E o mesmo argumento que o
///      bloco do Makyo King logo abaixo ja usa, e ele foi aceito naquele caso;
///   3. **o port nao tem como reproduzir o defeito de graca.** Aqui a dadiva e uma tabela de
///      typepaths e nao um `enableskill` contra uma lista de constituintes -- reproduzir a morte
///      exigiria escrever, a mao, uma lista de excecoes cujo unico proposito seria nao funcionar.
///
/// SE UM DIA O DONO QUISER FIDELIDADE AO BUG, o lugar e aqui e a mudanca e tirar os typepaths acima
/// dos kits em que a tabela do DM os mata -- **e nao** consertar `LevantarRevogaveis` nem o censo.
/// Verificado nesta sessao contra `Code/Modules/Ranks/ordered/*.dm` linha a linha.
/// ==================================================================================================================
/// </summary>
public static class DadivaDeCargo
{
	/// <summary>
	/// A UNICA SKILL DE CARGO QUE TODO CARGO TEM. `enabled=1` no DM, e o comentario diz por que:
	/// *"Rankchat is available to ALL ranks so this is the SOLE exception"* (`RankTree.dm:21`).
	/// </summary>
	public const string CanalDosCargos = "/datum/skill/rank/RankChat";

	/// <summary>
	/// O KIT DOS QUATRO ANCIAOS CARDEAIS -- **declarado ANTES da tabela de proposito**: inicializador
	/// de campo estatico roda na ordem do texto, e escrito depois ele chegaria nulo nas quatro linhas
	/// que o usam.
	/// </summary>
	private static readonly string[] AnciaoCardeal =
	[
		"/datum/skill/style/NamekStyle", "/datum/skill/rank/Makkankosappo", "/datum/skill/Enkumei",
		"/datum/skill/rank/Unlock_Potential", "/datum/skill/Telepathy", "/datum/skill/ki/Heal",
	];

	/// <summary>
	/// chave do cargo -> os typepaths que ele concede.
	///
	/// SO ENTRA O QUE O DM HABILITA. Cargo que o original deixa de maos vazias sai daqui vazio
	/// tambem (o Lorde do Gelo, o Saibamen Rouge Leader, o Mutany Leader, o Rei de Arlia) -- e essa
	/// e uma informacao de verdade, nao um esquecimento: o relatorio do catalogo ja a registrou.
	/// </summary>
	private static readonly Dictionary<string, string[]> Kits = new(StringComparer.OrdinalIgnoreCase)
	{
		// ======================= TERRA -- `ordered/EarthRanks.dm:19-43` =======================
		["guardian"] =
		[
			"/datum/skill/style/GodStyle", "/datum/skill/rank/Permission", "/datum/skill/rank/Keep_Body",
			"/datum/skill/rank/Dead", "/datum/skill/rank/Makkankosappo", "/datum/skill/rank/SuperiorSeal",
			"/datum/skill/rank/DeadZone", "/datum/skill/general/observe", "/datum/skill/Telepathy",
			"/datum/skill/ki/Heal",
		],
		["korin"] =
		[
			"/datum/skill/rank/Grow_Senzu", "/datum/skill/rank/Permission",
			"/datum/skill/rank/SuperiorSeal", "/datum/skill/ki/Heal",
		],
		["turtle"] = ["/datum/skill/style/KameStyle", "/datum/skill/rank/Kamehameha", "/datum/skill/rank/Mafuba"],

		// O `/datum/skill/flying/CraneFly` do DM (`EarthRanks.dm:41`) NAO EXISTE no `skills.json`: o
		// extrator nao o pegou (a declaracao dele nao tem `name`). Fica de fora com o motivo escrito
		// em vez de virar um typepath que o catalogo nao conhece e que `Dar()` guardaria calado.
		["crane"] =
		[
			"/datum/skill/style/CraneStyle", "/datum/skill/general/splitform",
			"/datum/skill/general/kikoho", "/datum/skill/rank/Dodompa",
		],
		["president"] = ["/datum/skill/rank/Taxes"],

		// ======================= NAMEK -- `ordered/NamekRanks.dm:16-31` =======================
		["elder"] =
		[
			"/datum/skill/Enkumei", "/datum/skill/style/NamekStyle", "/datum/skill/rank/Appoint_Elder",
			"/datum/skill/rank/Unlock_Potential", "/datum/skill/rank/Keep_Body", "/datum/skill/rank/Dead",
			"/datum/skill/ki/Heal",
		],
		// OS QUATRO CARDEAIS DIVIDEM O MESMO KIT porque no DM eles dividem o mesmo `mob.Rank`
		// ("Namekian Elder", `RankAssign.dm:95-96`): a arvore de skills nao os distingue, e inventar
		// uma diferenca aqui seria inventar jogo.
		["northelder"] = AnciaoCardeal, ["southelder"] = AnciaoCardeal,
		["eastelder"] = AnciaoCardeal, ["westelder"] = AnciaoCardeal,

		// ==================== OUTRO MUNDO -- `ordered/OtherworldRanks.dm:53-129` ====================
		["nkai"] =
		[
			"/datum/skill/style/GodStyle", "/datum/skill/rank/Keep_Body", "/datum/skill/rank/Dead",
			"/datum/skill/general/observe", "/datum/skill/kaioken", "/datum/skill/rank/SpiritBomb",
			"/datum/skill/rank/Revive",
		],
		["skai"] =
		[
			"/datum/skill/style/GodStyle", "/datum/skill/rank/Keep_Body", "/datum/skill/rank/Dead",
			"/datum/skill/general/observe", "/datum/skill/expand", "/datum/skill/rank/Revive",
		],
		["ekai"] =
		[
			"/datum/skill/style/GodStyle", "/datum/skill/rank/Keep_Body", "/datum/skill/rank/Dead",
			"/datum/skill/general/observe", "/datum/skill/general/selfdestruct",
			"/datum/skill/rank/BusterShell", "/datum/skill/rank/Revive",
		],
		["wkai"] =
		[
			"/datum/skill/style/GodStyle", "/datum/skill/rank/Keep_Body", "/datum/skill/rank/Dead",
			"/datum/skill/general/observe", "/datum/skill/rank/BusterBarrage", "/datum/skill/rank/Revive",
		],
		["grandkai"] =
		[
			"/datum/skill/style/GodStyle", "/datum/skill/rank/Unlock_Potential", "/datum/skill/rank/Keep_Body",
			"/datum/skill/rank/Dead", "/datum/skill/general/observe", "/datum/skill/rank/Reincarnate",
			"/datum/skill/rank/Restore_Youth", "/datum/skill/rank/Revive", "/datum/skill/rank/KaiPermission",
			"/datum/skill/rank/Ritual_of_Might",
		],
		["kaioshin"] =
		[
			"/datum/skill/style/GodStyle", "/datum/skill/rank/Keep_Body", "/datum/skill/rank/Dead",
			"/datum/skill/kai/Mystic", "/datum/skill/rank/Reincarnate", "/datum/skill/rank/Revive",
			"/datum/skill/general/observe", "/datum/skill/rank/KaiPermission", "/datum/skill/rank/Ritual_of_Might",
			"/datum/skill/ki/Heal", "/datum/skill/rank/Kaioshin_Apprenticeship",
		],
		["yemma"] = ["/datum/skill/rank/Judge", "/datum/skill/general/observe", "/datum/skill/rank/Revive"],
		["demonlord"] =
		[
			"/datum/skill/style/DemonStyle", "/datum/skill/rank/Keep_Body", "/datum/skill/rank/Dead",
			"/datum/skill/general/observe", "/datum/skill/demon/Majin", "/datum/skill/rank/Reincarnate",
			"/datum/skill/rank/Revive", "/datum/skill/rank/Unlock_Potential", "/datum/skill/rank/Restore_Youth",
			"/datum/skill/rank/Ritual_of_Might",
		],

		// ============================ O MAKYO KING, E A DIVERGENCIA ============================
		// No DM este kit NUNCA E ENTREGUE: o `Rank_Verb_Assign` nao tem ramo pro `King_Of_Hell`,
		// entao `mob.Rank` jamais vira essa string e o `if("King_Of_Hell")` do
		// `OtherworldRanks.dm:66` nunca roda. O cargo se conquista (esta no `RQ_CLAIMABLE`) e nao
		// entrega nada -- defeito que o proprio `Concede` do port ja registra em texto.
		//
		// AQUI ELE E ENTREGUE. A alternativa era portar um cargo que o jogador conquista pra receber
		// o nada, e "fiel ao DM" nao pode significar portar o silencio de um `switch` que esqueceu um
		// ramo. O kit e exatamente o que o `switch` PRETENDIA dar, sem uma linha a mais.
		// ====================================================================================
		["makyo"] =
		[
			"/datum/skill/style/DemonStyle", "/datum/skill/rank/Keep_Body", "/datum/skill/rank/Dead",
			"/datum/skill/demon/Majin", "/datum/skill/rank/Reincarnate", "/datum/skill/rank/Revive",
			"/datum/skill/general/observe",
		],

		// ======================= ESPACO -- `ordered/SpaceRanks.dm:13-40` =======================
		["kov"] =
		[
			"/datum/skill/rank/Taxes", "/datum/skill/rank/FinalFlash",
			"/datum/skill/style/SaiyanStyle", "/datum/skill/rank/GalicGun",
		],
		["capt"] = ["/datum/skill/rank/Death_Beam", "/datum/skill/style/AlienStyle", "/datum/skill/rank/DeathBall"],
		["geti"] =
		[
			"/datum/skill/general/splitform", "/datum/skill/rank/DeathBall", "/datum/skill/rank/Paralysis",
			"/datum/skill/style/AlienStyle", "/datum/skill/rank/Fusion_Dance",
		],
		["kingofacronia"] = ["/datum/skill/rank/KillDriver", "/datum/skill/style/AlienStyle"],
		["arconianguardian"] =
		[
			"/datum/skill/rank/SuperiorSeal", "/datum/skill/ki/Heal", "/datum/skill/rank/Holy_Shortcut",
			"/datum/skill/style/AlienStyle", "/datum/skill/rank/Detect_Shard",
		],

		// OS QUE O DM DEIXA DE MAOS VAZIAS. Estao aqui, vazios, de proposito: uma chave ausente e
		// indistinguivel de um cargo que a tabela esqueceu.
		["frostlord"] = [],
		["saibamenrougeleader"] = [],
		["mutany"] = [],
		["arlian"] = [],

		// O DEUS DA DESTRUICAO E O ANJO NAO PASSAM POR AQUI: o que eles concedem sao DISCIPLINAS
		// (Ultra Ego / Ultra Instinto) e verbs do proprio titulo, e isso ja tem dono em
		// `Core/Forms/Disciplinas.cs` (`RankQueEnsina`) e no `GameServer.Disciplinas.cs`. Duplicar
		// aqui daria duas fontes pra mesma verdade.
		["godofdestruction"] = [],
		["angel"] = [],
	};

	/// <summary>
	/// O KIT DE UM CARGO, com o <see cref="CanalDosCargos"/> junto. Cargo vazio ("") = nada.
	///
	/// O RankChat entra AQUI e nao em cada linha da tabela porque a regra do DM e literalmente "todo
	/// cargo tem": repeti-lo trinta vezes seria trinta lugares pra alguem esquecer um.
	/// </summary>
	public static string[] De(string cargo)
	{
		if (cargo.Length == 0) return [];
		if (!Kits.TryGetValue(cargo, out string[]? kit)) return [CanalDosCargos];
		return [.. kit, CanalDosCargos];
	}

	/// <summary>Os cargos que esta tabela conhece -- a bancada confere contra <see cref="Cargos.Todos"/>.</summary>
	public static IEnumerable<string> Cobertos => Kits.Keys;

	// =====================================================================
	// AS INSIGNIAS -- o que um cargo entrega que NAO e skill
	// =====================================================================
	/// <summary>
	/// OS ITENS QUE UM CARGO ENTREGA. Tabela SEPARADA do <see cref="Kits"/>, e a separacao e a
	/// regra: o kit e uma lista de TYPEPATHS de skill, e a bancada `--portasteste` afirma que os 51
	/// existem no catalogo de skills (`GameServer.PortasTeste.cs:173-174`). Um id de item enfiado la
	/// deixaria essa afirmacao vermelha por um motivo que nao tem nada a ver com o que ela mede.
	///
	/// ============================ POR QUE A POTARA E A PRIMEIRA (E POR ORA A UNICA) ============================
	/// Porque ela e o unico caso em que o DM PROMETE um item de cargo e nao entrega: o proprio brinco
	/// diz *"Kaioshins start with two"* (`Fusion.dm:608`) e o unico codigo que instanciaria um esta
	/// **comentado** na criacao de personagem (`CharacterCreation.dm:81-87`) -- e mesmo descomentado
	/// era por RACA (`if(Race=="Kai")`), nao por cargo. Ou seja: a fusao Potara e inalcancavel no
	/// BYOND. O dono disse como quer (*"Kaioshins ganham ao virar do rank Kaioshin"*), e entregar por
	/// aqui faz a insignia herdar de graca as duas propriedades do kit de skills: ela e IDEMPOTENTE e
	/// ela VOLTA quando o cargo vai embora.
	/// ======================================================================================================
	/// </summary>
	private static readonly Dictionary<string, string[]> Insignias =
		new(StringComparer.OrdinalIgnoreCase)
		{
			["kaioshin"] = [Jandirus.Core.Items.CatalogoDeItens.BrincosPotara],
		};

	/// <summary>Os itens deste cargo. Cargo vazio ou sem insignia = nada.</summary>
	public static string[] ItensDe(string cargo) =>
		cargo.Length == 0 ? [] : Insignias.GetValueOrDefault(cargo, []);

	/// <summary>Tudo o que alguma insignia entrega -- o universo que a reconciliacao pode TIRAR.</summary>
	public static IReadOnlyCollection<string> TodasAsInsignias { get; } =
		Insignias.Values.SelectMany(v => v).ToHashSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// TUDO O QUE ALGUM CARGO CONCEDE, sem repetir. E o universo de onde o
	/// <see cref="Revogavel"/> escolhe o que tirar.
	/// </summary>
	public static IReadOnlyCollection<string> Todas { get; } =
		Kits.Values.SelectMany(v => v).Append(CanalDosCargos)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>O espaco de nomes das skills que so um cargo tem -- `RankTree.dm:5`.</summary>
	private const string EspacoDoCargo = "/datum/skill/rank/";

	/// <summary>
	/// O PREFIXO DAS ARVORES DE CARGO. Pega as cinco ordenadas (`Rank/Earth`, `Rank/Otherworld`,
	/// `Rank/Namek`, `Rank/Space`, `Rank/KaioshinApprentice`) e a geral (`RankTree`), e NENHUMA
	/// outra das 47 do catalogo comeca assim -- a bancada afirma isso, porque um prefixo que
	/// pegasse uma arvore racial por engano tiraria skill de quem nunca teve cargo.
	/// </summary>
	private const string ArvoreDoCargo = "/datum/skill/tree/Rank";

	/// <summary>
	/// O QUE SAI JUNTO COM O CARGO -- levantado UMA VEZ do catalogo, e nao adivinhado por prefixo.
	///
	/// ============================ POR QUE O PREFIXO SOZINHO ESTAVA ERRADO ============================
	/// A primeira versao desta regra perguntava so `path.StartsWith("/datum/skill/rank/")`, com o
	/// argumento de que o resto do kit *"existe em outras arvores e em outras bocas"*. Metade do
	/// argumento e verdade e a outra metade nao era: os SETE ESTILOS, a Cura, o Kikoho, o Mistico, o
	/// Majin e o Kaio-ken **nao pendem de arvore nenhuma que nao seja de cargo** -- `ligada = 0` ou
	/// sem galho, `PodeAprender` recusa as onze. Ninguem nunca comprou uma delas. Quem tem uma, um
	/// cargo deu.
	///
	/// O buraco que isso abria e o mais barato de explorar que existe: reivindicar o Eremita
	/// Tartaruga, receber o `KameStyle`, reivindicar o Grou no dia seguinte e ficar com os dois --
	/// e com o Deus, e com o Namek -- pra sempre. Sao multiplicadores de ficha, nao lembranca.
	///
	/// A PROVA DE QUE A CORRECAO ESTA CERTA E DO PROPRIO DM: `datum/skill/tree/proc/treeshrink`
	/// (`trees.dm:119-135`) refunda toda skill aprendida que case com um CONSTITUINTE desabilitado
	/// da arvore -- e `new/datum/skill/style/GodStyle` e constituinte declarado da arvore do
	/// Outro Mundo (`OtherworldRanks.dm:15`). Perder o cargo desliga o constituinte, e o estilo cai.
	/// Aqui a mesma pergunta e feita ao catalogo: *quem segura esta skill?* Se so arvore de cargo a
	/// segura, ela e do cargo.
	/// ============================================================================================
	///
	/// ============================ O QUE FICA, E POR QUE ============================
	/// `Telepathy` (arvores `demon` e `kai`), `expand` (Body e seis raciais), `observe`
	/// (`demigod`, `kanassajin`), `splitform` (`kanassajin`, `majin`) e `selfdestruct` (`android`,
	/// `saibaman`) tem dono FORA do cargo: quem os tem pode te-los comprado, e tirar seria roubar.
	///
	/// `Enkumei` fica por um motivo diferente e que merece estar escrito: ele nao pende de arvore
	/// NENHUMA. O `growbranches()` de Namek chama `enableskill(/datum/skill/Enkumei)` sem ele estar
	/// na `constituentskills` (`NamekRanks.dm:7-8` contra `:19`), entao nem o `treeshrink` do
	/// original o tira. Manter e o porte fiel do descuido de la -- e um descuido a favor do jogador.
	/// ==============================================================================
	/// </summary>
	public static HashSet<string> LevantarRevogaveis(Skills.SkillCatalog cat)
	{
		// QUEM SEGURA CADA SKILL, numa volta so pelas 47 arvores. A pergunta e feita ate 52 vezes
		// por pessoa por segundo (o `TickDosCargos`), e varrer o catalogo dentro dela seria
		// trabalho de tique -- a regra 0.4 da casa. O valor e "TODAS as arvores que a seguram sao
		// de cargo".
		var soArvoreDeCargo = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		foreach (Skills.Skill arv in cat.Arvores)
		{
			bool daqui = arv.Path.StartsWith(ArvoreDoCargo, StringComparison.OrdinalIgnoreCase);
			foreach (string galho in arv.Galhos)
				soArvoreDeCargo[galho] =
					!soArvoreDeCargo.TryGetValue(galho, out bool antes) ? daqui : antes && daqui;
		}

		var sai = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string p in Todas)
			if (p.StartsWith(EspacoDoCargo, StringComparison.OrdinalIgnoreCase)
				|| (soArvoreDeCargo.TryGetValue(p, out bool so) && so))
				sai.Add(p);
		return sai;
	}

	/// <summary>
	/// ESTA SKILL SAI JUNTO COM O CARGO, PARA ESTA PESSOA?
	///
	/// Duas perguntas, e a segunda e a que faltava: **o `wastaught` do DM**. O `treeshrink` do Outro
	/// Mundo foi REESCRITO no original exatamente por isto (`OtherworldRanks.dm:23-27`) -- o Sr.
	/// Kaioh ensina Kaio-ken e Genkidama de graca, as duas ficam desabilitadas pra quem nao e Kaio
	/// do Norte, e o motor base tirava do Grand Kai a tecnica que o NPC lhe deu. La a guarda e
	/// "nao saiu desta arvore" (`investedskills`); aqui e "ninguem me ensinou", que e a mesma
	/// informacao, ja gravada no save (<see cref="Skills.SkillBook.Ensinadas"/>).
	///
	/// E ela conserta um defeito NA OUTRA DIRECAO que a versao por prefixo tinha e ninguem via: o
	/// Eremita Tartaruga ensina o Kamehameha (`teacher = TRUE`) a um aluno sem cargo nenhum, e o
	/// tique de 1 Hz comia a licao um segundo depois, calado, porque o typepath era `rank/`.
	/// </summary>
	/// <param name="soDeCargo">
	/// O resultado de <see cref="LevantarRevogaveis"/>, guardado pelo servidor. E
	/// <c>IReadOnlySet</c> e nao <c>IReadOnlyCollection</c> de proposito: com a interface generica o
	/// `Contains` cairia no `Enumerable.Contains`, que e varredura linear -- 52 comparacoes por
	/// skill, por pessoa, por segundo, dentro do tique.
	/// </param>
	public static bool Revogavel(IReadOnlySet<string> soDeCargo, Skills.SkillBook livro, string path) =>
		soDeCargo.Contains(path) && !livro.FoiEnsinada(path);
}
