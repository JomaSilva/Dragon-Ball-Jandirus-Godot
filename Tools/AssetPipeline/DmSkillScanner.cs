using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>Uma skill, do jeito que o jogo precisa saber dela pra ensinar e cobrar.</summary>
public sealed class SkillDef
{
	public string Path = "";          // "/datum/skill/Assassain/Backstab"
	public string Nome = "";
	public string Desc = "";
	public string Tipo = "Physical";  // Physical | Ki | Magic -- o `skilltype` do DM
	public int Tier = 1;
	public int Custo = 1;             // skillcost
	public int MaxNivel = 3;
	public bool CustoFixo;            // fixedcost: NAO normaliza o custo pelo tier
	public bool Comum;                // common_sense: qualquer um aprende
	public bool Ligada = true;        // enabled
	public bool SoVilao;              // villainonly
	public bool Ensinavel;            // teacher

	/// <summary>
	/// `teachCost` -- QUANTOS MARCOS O ALUNO PRECISA TER pra ser ensinado nesta skill. 0 = nao
	/// declarado, e ai vale o `skillcost` (ver `teachable.dm:43-45`).
	///
	/// SO UM TIPO DECLARA ISTO no jogo inteiro: `/datum/skill/rank` (`RankTree.dm:12`, valor 2), e e
	/// por HERANCA que ele chega nas dezenas de skills de cargo penduradas nele. Extrair sem herdar
	/// devolveria zero em todas e as skills de cargo passariam a custar o proprio tier -- que e
	/// exatamente o numero que o `teachCost` existe pra NAO ser (o comentario de la diz em voz alta:
	/// *"skill costs are what the TEACHER pays to learn them, not the student"*).
	/// </summary>
	public int CustoDeEnsino;

	public bool Arvore;               // e um /datum/skill/tree/... (o galho, nao a folha)

	/// <summary>`can_forget` -- da pra esquecer (e ser reembolsada) quando a arvore encolhe.</summary>
	public bool Esquecivel = true;

	/// <summary>
	/// SO NAS ARVORES: `allowedtier` de nascenca e `maxtier` (`trees.dm:10-11`). A vitrine do DM so
	/// mostra skill com `tier &lt;= allowedtier` (`HtmlUI.dm:820`), e o `allowedtier` CRESCE pelo
	/// `growbranches()` -- que sai traduzido em <see cref="Regras"/>.
	/// </summary>
	public int TierInicial = 1;
	public int TierMax = 1;

	/// <summary>
	/// SO NAS ARVORES: o `growbranches()` como dado -- `tipo;alvo;condicao`, uma por efeito, na ordem
	/// do DM. Ver <see cref="DmGalhosScanner"/> pro formato e pro que fica de fora.
	/// </summary>
	public List<string> Regras = [];

	public List<string> Racas = [];
	public List<string> Classes = [];
	public List<string> PreReqs = [];
	public List<string> Constituintes = [];   // so nas arvores: as skills que penduram nela

	/// <summary>
	/// O QUE A SKILL FAZ AO SER APRENDIDA: campo -> quanto soma. Extraido do corpo de
	/// `after_learn()`, onde 333 skills fazem exatamente `savant.physoffBuff += 0.1`.
	/// </summary>
	public Dictionary<string, double> Buffs = new(StringComparer.Ordinal);

	/// <summary>
	/// AS HABILIDADES ATIVAS que a skill destrava, pelo nome do verb do DM
	/// (`assignverb(/mob/keyable/verb/Solar_Flare)` -> `Solar_Flare`).
	/// </summary>
	public List<string> Verbos = [];

	/// <summary>
	/// O EFEITO CONDICIONADO A RACA: `if(savant.Race=="Yardrat") savant.teleskill=70` (yardrat.dm:85-87).
	/// Uma entrada por raca, no MESMO molde da casa da escolha unica (rotulo = a raca), e quem a aplica
	/// e o Core, so pra quem E daquela raca. Ate este canal existir o `if` era invisivel pro extrator e
	/// a linha de dentro saia como flag INCONDICIONAL -- a Instant Transmission e ensinavel, e todo
	/// aprendiz nascia com a pericia de um Yardrat.
	/// </summary>
	public List<EscolhaDeSkill> PorRaca = [];

	/// <summary>
	/// BUFF MULTIPLICATIVO (`savant.MedMod *= 2`). Canal SEPARADO do aditivo de proposito: no
	/// DM os campos que levam `*=` sao modificadores que nascem em 1 (MedMod, kicapacityMod,
	/// PDrainMod), e somar 2 num campo que deveria DOBRAR e um erro que passa despercebido --
	/// da o numero certo quando o valor de base e 1 e o numero errado em todo o resto.
	/// </summary>
	public Dictionary<string, double> Mults = new(StringComparer.Ordinal);

	/// <summary>
	/// O SEGUNDO CANAL DE BUFF PERMANENTE: `savant.genome.add_to_stat("Energy Level", 0.1)`.
	/// Nao passa pelos campos `*Buff` -- mexe nos MODIFICADORES da genetica e forca o recalculo.
	/// 23 skills usam isto, e um extrator que so olhasse `savant.<campo> +=` perderia todas.
	/// </summary>
	public Dictionary<string, double> Genes = new(StringComparer.Ordinal);

	/// <summary>
	/// ATRIBUICAO DIRETA (`savant.can_sup_el = 1`): flags de capacidade e contadores de arvore
	/// como `bodyskill`/`bodyreadiness` -- que NAO sao mortos, sao o gate de quatro arvores
	/// (Body.dm:24-44). Guardado a parte do aditivo porque a semantica e SETAR, nao somar.
	/// </summary>
	public Dictionary<string, double> Flags = new(StringComparer.Ordinal);

	/// <summary>
	/// O ESTILO DE LUTA que a skill concede (`attachedstyle = new /datum/style/KameStyle`).
	/// Vazio na esmagadora maioria: so nove skills em todo o jogo penduram estilo.
	/// </summary>
	public string Estilo = "";
	public int PreReqFolga;                   // prereqthreshold: quantos prereqs da pra pular
	public string? Pai;

	/// <summary>
	/// `expbarrier`: quanto exp custa UM nivel. Preenchido pelo <see cref="DmEffectorScanner"/>,
	/// que ja varre os mesmos arquivos pra ler o `effector()`. 0 = a skill nao declarou, e o DM
	/// cai no padrao 100 (Skills Master/skill.dm:9).
	///
	/// SEM ISTO TODA SKILL SOBE NA BARREIRA 100: as arvores de Ki pedem 5.000 (Ki Trees/
	/// Effusion.dm:36) e crescem 3% por nivel dali em diante, entao o erro nao e de margem -- e
	/// de cinquenta vezes, e sempre a favor do jogador.
	/// </summary>
	public int ExpBarreira;

	/// <summary>
	/// A ESCOLHA UNICA: conjuntos MUTUAMENTE EXCLUSIVOS de efeito, dos quais o dono fica com um so.
	///
	/// Uma skill em todo o jogo faz isto (`Great_Robotic_Alliance`, `meta.dm:104-125`): o
	/// `after_learn()` chama um proc PROPRIO (`choose()`) que abre um `switch(input(...) in
	/// list(...))` de tres casas, e cada casa buffa campos diferentes.
	///
	/// POR QUE ISTO NAO PODE CAIR NOS <see cref="Buffs"/>: as tres casas somadas dariam ao
	/// Metamoriano os TRES conjuntos de uma vez -- +1,5 de velocidade, +1 de fisico e +1 de Ki
	/// juntos. Errado pra mais e errado calado; "muda" ao menos aparece no censo.
	/// </summary>
	public List<EscolhaDeSkill> Escolhas = [];

	/// <summary>
	/// A TERCEIRA FORMA DA ESCOLHA UNICA: `switch(savant.trinitytype)` (Grace, Bodybuilding.dm:243) --
	/// a skill nao pergunta, ela entra na casa que OUTRA skill escolheu. Aqui fica o nome da `mob/var`
	/// lida (`trinitytype`); <see cref="EscolhaSegue"/> e a skill lider, resolvida no fim da varredura
	/// pelos rotulos das casas (as duas tem "Van-sama"/"Ricardo"/"Aniki").
	/// </summary>
	public string EscolhaPorVar = "";
	public string EscolhaSegue = "";

	/// <summary>
	/// O GANHO NA COMPRA COM EXPRESSAO -- `campo+=expr` sem `savant.` e sem espacos:
	/// `BP+=max(1,BP*0.01)`, `hiddenpotential+=relBPmax*2` (One_Hundred, Bodybuilding.dm:89-92).
	///
	/// O extrator so lia CONSTANTE; estas linhas usam um local do datum (`storedBP`) preenchido
	/// duas linhas antes com uma expressao sobre a ficha. Aqui o local e substituido pela expressao
	/// dele e a linha vira dado -- que o Core avalia na compra com o mesmo avaliador das regras de
	/// arvore (`GanhoNaCompra`). O que o Core nao consegue ler vai pro diario
	/// <see cref="DmSkillScanner.ComprasNaoLidas"/>, e nao pro json.
	/// </summary>
	public List<string> Compra = [];
}

/// <summary>
/// UMA CASA da escolha unica: o rotulo que o DM mostra e os efeitos daquela casa.
/// Os canais sao os MESMOS do <see cref="SkillDef"/> pelo mesmo motivo -- somar num campo que
/// deveria multiplicar da o numero certo so quando a base e 1.
/// </summary>
public sealed class EscolhaDeSkill
{
	public string Rotulo = "";
	public Dictionary<string, double> Buffs = new(StringComparer.Ordinal);
	public Dictionary<string, double> Mults = new(StringComparer.Ordinal);
	public Dictionary<string, double> Genes = new(StringComparer.Ordinal);
	public Dictionary<string, double> Flags = new(StringComparer.Ordinal);
	public List<string> Verbos = [];
}

/// <summary>
/// Le as 709 skills direto da arvore de tipos do DM.
///
/// POR QUE EXTRAIR E NAO TRANSCREVER: sao setecentas. Escrever isso a mao garante erro de
/// digitacao em requisito de raca, em custo e em pre-requisito -- e erro de requisito nao
/// aparece em teste, aparece meses depois quando alguem nao consegue aprender a skill da
/// propria raca. O DM e a fonte de verdade; o que sai daqui e uma COPIA MECANICA dela.
///
/// A MESMA ARMADILHA DO SCANNER DE TURF, e vale repetir: a arvore do DM e por INDENTACAO, e
/// dentro de um bloco de skill ha CORPOS DE PROC (`after_learn()`, `effector()`) que tambem sao
/// blocos indentados e que atribuem coisas. `savant.physoffBuff += 0.1` dentro de `after_learn()`
/// nao e propriedade da skill. Toda subarvore de um identificador que termina em "(...)" e
/// ignorada.
///
/// O EFEITO PASSIVO SAI DAQUI TAMBEM. A maioria esmagadora das skills nao "faz" nada: ela SOMA.
/// 333 corpos de `after_learn()` sao literalmente `savant.physoffBuff += 0.1` e mais nada, e isso e
/// dado, nao logica -- vai pra `Buffs`. O que continua fora sao as skills ATIVAS (Kaio-ken, Sense,
/// Zanzoken): essas tem corpo de verdade e sao escritas a mao em <c>Server/GameServer.Skills.cs</c>.
/// </summary>
public static class DmSkillScanner
{
	/// <summary>`savant.physoffBuff += 0.1` -- a forma dos efeitos de aprendizado.</summary>
	private static readonly Regex RxBuff = new(
		@"savant\.(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op>\+=|-=)\s*(?<v>[0-9.]+)", RegexOptions.Compiled);

	/// <summary>`savant.MedMod *= 2` -- buff MULTIPLICATIVO, outro canal.</summary>
	private static readonly Regex RxMult = new(
		@"savant\.(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op>\*=|/=)\s*(?<v>[0-9.]+)", RegexOptions.Compiled);

	/// <summary>`savant.kioffBuff++` -- o mesmo que `+= 1`, escrito curto.</summary>
	private static readonly Regex RxIncr = new(
		@"savant\.(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op>\+\+|--)", RegexOptions.Compiled);

	/// <summary>`savant.genome.add_to_stat(""Energy Level"", 0.1)` -- buff pela genetica.</summary>
	private static readonly Regex RxGene = new(
		@"genome\.(?<op>add_to_stat|sub_to_stat)\(\s*""(?<s>[^""]+)""\s*,\s*(?<v>[0-9.]+)", RegexOptions.Compiled);

	/// <summary>`savant.contents += new/obj/SplitForm` -- o SEGUNDO canal de habilidade ativa.</summary>
	private static readonly Regex RxObj = new(
		@"savant\.contents\s*\+=\s*new\s*/obj/(?:[A-Za-z0-9_]+/)*(?<o>[A-Za-z0-9_]+)", RegexOptions.Compiled);

	/// <summary>`savant.can_sup_el = 1` -- atribuicao direta (flag ou contador de arvore).</summary>
	private static readonly Regex RxSet = new(
		@"^savant\.(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<v>-?[0-9.]+)\s*$", RegexOptions.Compiled);

	/// <summary>`assignverb(/mob/keyable/verb/Solar_Flare)` -- a forma das skills ATIVAS.</summary>
	/// <summary>
	/// `/datum/skill/general/Hardened_Body/after_learn()` -- a SEGUNDA forma de declarar o
	/// efeito, com o caminho inteiro na linha em vez de aninhado no bloco da skill.
	///
	/// O DM aceita as duas e o jogo usa as duas: 87 skills declaram aninhado e CENTO E
	/// DEZESSEIS declaram assim. Eu so enxergava a primeira forma, e por isso dava skills como
	/// "Hardened Body" (cuja descricao promete `P.Def+++`) como sem efeito nenhum -- ela tem
	/// `physdefBuff += 0.2`, so que quatro linhas abaixo do bloco, num typepath proprio.
	/// </summary>
	private static readonly Regex RxLearnSolto = new(
		@"^(?<p>/?datum/skill(?:/[A-Za-z0-9_]+)+)/after_learn\(\)\s*$", RegexOptions.Compiled);

	private static readonly Regex RxVerb = new(
		@"assignverb\(\s*/(?:[A-Za-z0-9_]+/)*verb/(?<v>[A-Za-z0-9_]+)", RegexOptions.Compiled);

	/// <summary>
	/// `/datum/skill/meta/Great_Robotic_Alliance/proc/choose()` -- a mesma dobra do `after_learn`,
	/// agora pra qualquer proc. O grupo `pp` so casa quando ha o `proc/`, e e ele que diz se a
	/// skill DECLAROU um proc novo ou SOBRESCREVEU um do motor.
	/// </summary>
	private static readonly Regex RxProcSolto = new(
		@"^(?<p>/?datum/skill(?:/[A-Za-z0-9_]+)+?)/(?<pp>proc/)?(?<n>[A-Za-z_][A-Za-z0-9_]*)\([^)]*\)\s*$",
		RegexOptions.Compiled);

	/// <summary>`choose()` sozinho numa linha: o `after_learn` delegando o efeito.</summary>
	private static readonly Regex RxChamadaSimples = new(
		@"^(?<n>[A-Za-z_][A-Za-z0-9_]*)\(\s*\)\s*$", RegexOptions.Compiled);

	/// <summary>`switch(input(...) in list("a","b","c"))` -- a pergunta de casa unica.</summary>
	private static readonly Regex RxEscolhaLista = new(
		@"switch\s*\(\s*input\s*\(.*\)\s*in\s*list\s*\((?<l>.*)\)\s*\)\s*$", RegexOptions.Compiled);

	/// <summary>`if("Great Sea Nanos")` -- a casa.</summary>
	private static readonly Regex RxCasa = new(
		@"^if\s*\(\s*""(?<s>[^""]*)""\s*\)\s*$", RegexOptions.Compiled);

	/// <summary>`if(savant.Race=="Yardrat")` (com ou sem o comando na mesma linha) -- o efeito por RACA.</summary>
	private static readonly Regex RxPorRaca = new(
		@"^if\s*\(\s*savant\.Race\s*==\s*""(?<r>[^""]+)""\s*\)\s*(?<resto>.*)$", RegexOptions.Compiled);

	private static readonly Regex RxProp = new(
		@"^(?<ind>\t*)(?<k>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<v>.+?)\s*$", RegexOptions.Compiled);

	private static readonly Regex RxTypePath = new(
		@"^(?<ind>\t*)(?<p>/?datum/skill(?:/[A-Za-z0-9_]+)*)\s*$", RegexOptions.Compiled);

	/// <summary>`list("Saiyan","Human")` e `list(/datum/skill/a, new/datum/skill/b)`.</summary>
	private static readonly Regex RxItem = new(
		"\"(?<s>[^\"]*)\"|(?<p>/?(?:new\\s*/)?datum/skill(?:/[A-Za-z0-9_]+)+)", RegexOptions.Compiled);

	// =====================================================================
	// O QUE MORA FORA DO after_learn -- e o diario de quem ficou de fora
	// =====================================================================
	/// <summary>Corpo, com indentacao, dos procs PROPRIOS de cada skill: (skill, proc) -> linhas.</summary>
	private static readonly Dictionary<(string, string), List<(int Ind, string Txt)>> Corpos = [];

	/// <summary>`after_learn()` chamou este proc proprio.</summary>
	private static readonly HashSet<(string, string)> Chamados = [];

	/// <summary>
	/// CORPO DO `after_learn()` QUE CHEGOU ANTES DO DONO: caminho da skill -> linhas cruas.
	///
	/// ============================ O BURACO QUE ISTO FECHA ============================
	/// So a forma ABSOLUTA (`/datum/skill/rank/Mafuba/after_learn()`, <see cref="RxLearnSolto"/>)
	/// tem como cair aqui. Na forma ANINHADA o dono vem de `pilha[ind-1]` -- ele acabou de ser
	/// registrado no mesmo arquivo, linhas acima, e por construcao nunca falta. Na absoluta o
	/// typepath pode estar declarado em OUTRO arquivo, e o `Directory.GetFiles` entrega os
	/// arquivos em ordem de CAMINHO, nao em ordem de dependencia.
	///
	/// Foi exatamente o que aconteceu com o selo: `Modules\Magic\Sealing.dm` e o arquivo 140 e o
	/// `Modules\Ranks\ordered\EarthRanks.dm` -- onde os tres typepaths moram -- e o 315, de 502.
	/// Cento e setenta e cinco arquivos de distancia: nao e uma corrida apertada, e uma certeza.
	/// Quando o corpo era lido a skill ainda nao existia, o `TryGetValue` falhava e o `continue`
	/// engolia o `after_learn` INTEIRO sem uma palavra. TRES skills (Mafuba, Open Dead Zone,
	/// Superior Seal) perdiam o verb, e o censo as dava como "folha muda de nascenca" -- que e o
	/// mesmo carimbo de quem o DM tambem nao faz nada. O jogador cumpria karma 25 e 1M de BP, o
	/// painel do Eremita Tartaruga anunciava o Mafuba como entregue, e nao havia botao nenhum.
	///
	/// A SAIDA E A QUE A CASA JA USA pros procs proprios (<see cref="Corpos"/> +
	/// <see cref="Chamados"/>): nao se le o disco duas vezes -- GUARDA-SE o que nao deu pra
	/// resolver e resolve-se no fim, quando o catalogo inteiro esta na mao.
	/// ================================================================================
	/// </summary>
	private static readonly Dictionary<string, List<(int Ind, string Txt)>> CorposDeAprendizado =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// OS DONOS QUE JA ESTAVAM LIDOS quando o corpo do `after_learn` apareceu. TODO corpo e guardado e
	/// lido no fim agora (o `switch(input(...))` e o local `storedBP` precisam do corpo INTEIRO, com
	/// indentacao, e nao de uma linha por vez); esta lista guarda a distincao que a bancada do
	/// extrator mede -- <see cref="ComoAntesDoConserto"/> joga fora so quem NAO esta aqui, que e
	/// exatamente o que o extrator de antes do conserto fazia.
	/// </summary>
	private static readonly HashSet<string> DonosLidosNaHora = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Onde (arquivo:linha) cada `after_learn` adiado foi visto -- so pro diario.</summary>
	private static readonly Dictionary<string, string> OndeAdiou = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// GANHOS NA COMPRA QUE O CORE NAO LE -- (skill, linha). `savant.X += algo` que nao e constante
	/// nem expressao legivel (`savant.contents += ...` cai no canal de verb antes; o que sobra aqui e
	/// mesmo logica). Diario, nao json: uma expressao que o Core nao avalia nao pode virar `+= 0`.
	/// </summary>
	public static readonly List<(string Skill, string Linha)> ComprasNaoLidas = [];

	/// <summary>
	/// O RAMO `else` DE UM `if(savant.Race=="X")` QUE TEM EFEITO -- (skill, linha). "Todo mundo menos X"
	/// nao tem canal no `skills.json` e inventar um seria adivinhar; a linha vai pro diario e o relatorio
	/// do extrator a imprime. No DM do dono o unico `else` desses e um `to_chat` (yardrat.dm:88).
	/// </summary>
	public static readonly List<(string Skill, string Linha)> CondicionaisNaoLidas = [];

	/// <summary>
	/// `switch(savant.<var>)` de casa unica cuja LIDER nao foi achada -- (skill, var). A lider e a
	/// skill com casas de mesmos rotulos; sem ela a seguidora sai com as casas e sem `escolhasegue`,
	/// e o Core nunca entra em casa nenhuma (que e o que o DM faz sem a var escrita).
	/// </summary>
	public static readonly List<(string Skill, string Var)> EscolhasSemLider = [];

	/// <summary>
	/// O CORPO DO `growbranches()` DE CADA ARVORE, com indentacao, na ordem em que foi lido.
	///
	/// Guardado cru e traduzido SO NO FIM da varredura (<see cref="DmGalhosScanner.Traduzir"/>), porque
	/// a traducao precisa do `maxtier` da arvore e o corpo pode vir num arquivo lido antes da
	/// declaracao dela -- a mesma armadilha de ordem do `after_learn` adiado, logo acima.
	///
	/// A lista ACUMULA se o DM declarar o proc duas vezes pra mesma arvore: e o caso de
	/// `/datum/skill/tree/effusionmas/growbranches()`, escrito em `Effusion.dm:17` E em
	/// `BuffMastery.dm:15` -- o segundo era pra ser da `kibuffmas` e acende skills que nao sao galho
	/// da `effusionmas`. Fiel ao DM, a regra sai anexada a arvore errada e nao pega em nada; o censo do
	/// Core e quem diz que a Ki Buff Mastery ficou sem acendedor.
	/// </summary>
	private static readonly Dictionary<string, List<(int Ind, string Txt)>> CorposDeGalhos =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>O `prunebranches()` de cada arvore -- traduzido DEPOIS do growbranches, na ordem do `testunlocks()`.</summary>
	private static readonly Dictionary<string, List<(int Ind, string Txt)>> CorposDePoda =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// OS `after_learn()` QUE NEM COM TUDO LIDO ACHARAM DONO -- (skill, arquivo:linha).
	///
	/// ESTE E O ALARME, e ele e o conserto que dura. O adiamento acima fecha o buraco de HOJE; o
	/// que impede a TERCEIRA vez e esta lista sair impressa toda rodada. A mesma dobra ja custou
	/// 116 skills uma vez (o `after_learn` com typepath na propria linha) e 3 agora, e nas duas o
	/// remedio foi ler mais uma forma -- ninguem pos o alarme, e por isso houve a segunda vez.
	///
	/// O sinal e EXATO, e essa e a graca: ele nao pergunta "a skill tem `after_learn` no DM?"
	/// (pergunta que da 56 acertos, 53 deles legitimos -- as maestrias de Ki, cujo efeito real
	/// mora no `effector()`), pergunta "o extrator VIU a linha e nao conseguiu atribuir a
	/// ninguem?". Zero falso positivo por construcao.
	/// </summary>
	public static readonly List<(string Skill, string Onde)> AprendizadosSemDono = [];

	/// <summary>
	/// TODO PROC QUE MEXE NO `savant` E NAO E O `after_learn`: (skill, proc) -> arquivo:linha.
	///
	/// ESTE DIARIO E O CONSERTO DE VERDADE. O buraco que motivou esta rodada nao foi "faltou ler
	/// o `choose()`" -- foi que ninguem SABIA que havia um `choose()`. O extrator lia uma forma,
	/// as outras sumiam sem barulho, e a skill aparecia como "sem efeito" no censo, que e
	/// indistinguivel de "o DM tambem nao faz nada". Agora sai numero, e numero se confere.
	///
	/// Ver <see cref="ProcsForaDoLote"/> pra leitura ja separada por quem le o que.
	/// </summary>
	public static readonly Dictionary<(string Skill, string Proc), string> ProcsComEfeito = [];

	/// <summary>
	/// Os procs com efeito que NINGUEM le -- nem este scanner, nem o <see cref="DmEffectorScanner"/>,
	/// e que tambem nao sao o espelho negativo do aprendizado.
	///
	/// O QUE ESTA DE FORA, E POR QUE (medido, nao suposto):
	///   `before_forget` -- o espelho de desaprender. Reconstruir a soma do zero e mais seguro.
	///   `effector`      -- lido pelo <see cref="DmEffectorScanner"/>, que vira `niveis.json`.
	///   `login`         -- REAPLICACAO. As duas unicas skills cujo `login` traz algo que nao esta
	///                      no `after_learn` sao a `kaioken` (verbs de atalho que dependem de um
	///                      ajuste que o proprio jogador salvou) e a `Legendary_Anger` (uma
	///                      MIGRACAO de save antigo, que este port nunca teve). Ler qualquer uma
	///                      das duas seria importar defeito de outro jogo.
	/// </summary>
	public static IEnumerable<(string Skill, string Proc, string Onde)> ProcsForaDoLote =>
		ProcsComEfeito.Where(kv => kv.Key.Proc is not ("before_forget" or "effector" or "login"))
					  .Select(kv => (kv.Key.Skill, kv.Key.Proc, kv.Value))
					  .OrderBy(x => x.Skill, StringComparer.Ordinal);

	/// <summary>
	/// ============================ O MUNDO DE ANTES DO CONSERTO, NUMA CHAVE ============================
	/// Ligado, o adiamento de <see cref="CorposDeAprendizado"/> NAO acontece: o corpo do `after_learn`
	/// cujo dono ainda nao foi lido e jogado fora **e o alarme fica mudo** -- que e literalmente o que
	/// o extrator fazia antes desta rodada (o `continue` que engolia o corpo inteiro sem uma palavra).
	///
	/// POR QUE UMA CHAVE E NAO UM RITUAL. A guarda so vale alguma coisa se alguem ja tiver VISTO ela
	/// reprovar, e a primeira conferencia disso foi feita reinjetando o defeito no fonte e desfazendo
	/// depois -- o que prova a mesma coisa e **nao fica**: no mes que vem ninguem sabe se a linha ainda
	/// pega, e "esta verde" fica indistinguivel de "nao esta olhando". E a mesma decisao, com o mesmo
	/// argumento, que o <see cref="CensoMudoBench"/> tomou com o `semDuro` e o servidor com o
	/// `--semduro`: controle negativo e um COMANDO, nao um ritual.
	///
	/// Ninguem em producao liga isto -- o comando `skills` nao toca nela e o padrao e `false`. Quem
	/// ligar leva um berro no stderr, pelo mesmo motivo que o `--semduro` berra no log.
	/// ============================================================================================
	/// </summary>
	public static bool ComoAntesDoConserto;

	public static Dictionary<string, SkillDef> Scan(string pastaCode)
	{
		var defs = new Dictionary<string, SkillDef>(StringComparer.OrdinalIgnoreCase);
		Corpos.Clear();
		Chamados.Clear();
		ProcsComEfeito.Clear();
		CorposDeAprendizado.Clear();
		DonosLidosNaHora.Clear();
		OndeAdiou.Clear();
		AprendizadosSemDono.Clear();
		ComprasNaoLidas.Clear();
		CondicionaisNaoLidas.Clear();
		EscolhasSemLider.Clear();
		CorposDeGalhos.Clear();
		CorposDePoda.Clear();
		DmGalhosScanner.NaoLidas.Clear();

		foreach (string arq in Directory.GetFiles(pastaCode, "*.dm", SearchOption.AllDirectories))
			Ler(arq, defs);

		// BANCADA: o mundo de ANTES do conserto -- o corpo de dono nao-lido vai pro lixo e o alarme
		// nao fala. Ver <see cref="ComoAntesDoConserto"/>. Uma linha guardada, e ela e o controle
		// negativo da `dotnet run -- extrator`.
		if (ComoAntesDoConserto)
		{
			Console.Error.WriteLine(
				"[extrator] `ComoAntesDoConserto` LIGADO: o after_learn de dono nao-lido sera JOGADO "
				+ "FORA e o alarme ficara MUDO. Este e o extrator de antes do conserto -- se voce nao "
				+ "esta rodando uma bancada, desligue isto.");
			foreach (string skill in CorposDeAprendizado.Keys.Where(k => !DonosLidosNaHora.Contains(k)).ToList())
				CorposDeAprendizado.Remove(skill);
			OndeAdiou.Clear();
		}

		// TODO `after_learn()` E LIDO AQUI, com o catalogo inteiro na mao -- o de dono ja lido e o de
		// dono que so apareceu noutro arquivo (ver <see cref="CorposDeAprendizado"/> pra historia).
		// A ORDEM importa: este laco vem ANTES do dos `Chamados` de proposito, porque um corpo tambem
		// pode DELEGAR (`choose()`), e a delegacao so e resolvida se ja estiver anotada quando o de
		// baixo rodar.
		foreach ((string skill, List<(int Ind, string Txt)> corpo) in CorposDeAprendizado)
		{
			if (!defs.TryGetValue(skill, out SkillDef? dona))
			{
				AprendizadosSemDono.Add((skill, OndeAdiou.GetValueOrDefault(skill, "?")));
				continue;
			}
			AbsorverAprendizado(dona, skill, corpo);
		}
		AprendizadosSemDono.Sort((a, b) => string.CompareOrdinal(a.Skill, b.Skill));

		// OS PROCS PROPRIOS QUE O `after_learn` CHAMOU viram efeito da skill agora que os dois
		// lados estao lidos -- o proc pode ter sido declarado antes do `after_learn` no arquivo.
		foreach ((string skill, string proc) in Chamados)
		{
			if (!Corpos.TryGetValue((skill, proc), out List<(int Ind, string Txt)>? corpo)) continue;
			if (!defs.TryGetValue(skill, out SkillDef? d)) continue;
			Absorver(d, corpo);
		}

		// A LIDER DA ESCOLHA HERDADA: a skill que abre `switch(savant.trinitytype)` segue a que tem
		// casas de MESMOS ROTULOS (Grace <- TheHolyTrinity). Pelos rotulos e nao pelo nome da var
		// porque a var e escrita no `login()` da lider (Bodybuilding.dm:167), que este scanner nao le
		// -- e os rotulos sao a mesma lista nas duas, por construcao do proprio DM.
		foreach (SkillDef seguidora in defs.Values)
		{
			if (seguidora.EscolhaPorVar.Length == 0 || seguidora.Escolhas.Count == 0) continue;
			var rotulos = new HashSet<string>(seguidora.Escolhas.Select(e => e.Rotulo), StringComparer.OrdinalIgnoreCase);
			SkillDef? lider = defs.Values.FirstOrDefault(o =>
				o != seguidora && o.EscolhaPorVar.Length == 0 && o.Escolhas.Count == rotulos.Count
				&& o.Escolhas.All(e => rotulos.Contains(e.Rotulo)));
			if (lider != null) seguidora.EscolhaSegue = lider.Path;
			else EscolhasSemLider.Add((seguidora.Path, seguidora.EscolhaPorVar));
		}

		// HERANCA: `/datum/skill/Assassain/Backstab` herda de `/datum/skill/Assassain` se ele
		// existir, senao de `/datum/skill`. Sem isto, uma skill que so declara `name` e `tier`
		// perde raca, classe e tipo do pai -- e vira inaprendivel pra quem deveria poder.
		foreach (SkillDef d in defs.Values)
		{
			int corte = d.Path.LastIndexOf('/');
			if (corte <= 0) continue;
			string pai = d.Path[..corte];
			if (defs.ContainsKey(pai)) d.Pai = pai;
		}
		foreach (SkillDef d in defs.Values) Herdar(d, defs, 0);

		// O `growbranches()` VIRA REGRA agora que toda arvore ja tem o proprio `maxtier` lido. Corpo
		// de arvore que nao existe no catalogo (um typepath comentado, um `/proc/` do motor) fica no
		// diario em vez de sumir.
		foreach (string arvore in CorposDeGalhos.Keys.Concat(CorposDePoda.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			if (!defs.TryGetValue(arvore, out SkillDef? a) || !a.Arvore)
			{
				DmGalhosScanner.NaoLidas.Add((arvore, "(growbranches de uma arvore que o catalogo nao tem)"));
				continue;
			}
			if (CorposDeGalhos.TryGetValue(arvore, out List<(int, string)>? cresce))
				a.Regras.AddRange(DmGalhosScanner.Traduzir(arvore, cresce, a.TierMax));
			if (CorposDePoda.TryGetValue(arvore, out List<(int, string)>? poda))
				a.Regras.AddRange(DmGalhosScanner.Traduzir(arvore, poda, a.TierMax));
		}

		return defs;
	}

	/// <summary>
	/// Le o corpo de um proc PROPRIO que o `after_learn()` chamou.
	///
	/// DUAS SAIDAS, e a diferenca entre elas e a diferenca entre certo e errado pra mais:
	///
	///   * se o corpo abre um `switch(input(...) in list("a","b","c"))`, cada `if("a")` vira uma
	///     CASA em <see cref="SkillDef.Escolhas"/> -- conjuntos exclusivos, o dono fica com um;
	///   * senao, o corpo e efeito incondicional e cai nos canais normais da skill.
	///
	/// O que estiver FORA de qualquer casa (o `chosen = 1` da vida) e ignorado de proposito: e
	/// contabilidade do datum do DM, nao efeito no corpo de ninguem.
	/// </summary>
	private static void Absorver(SkillDef d, List<(int Ind, string Txt)> corpo)
	{
		int indSwitch = -1;
		EscolhaDeSkill? casa = null;
		int indCasa = -1;

		foreach ((int ind, string txt) in corpo)
		{
			if (indCasa >= 0 && ind <= indCasa) { casa = null; indCasa = -1; }
			if (indSwitch >= 0 && ind <= indSwitch) indSwitch = -1;

			if (indSwitch < 0 && RxEscolhaLista.Match(txt) is { Success: true })
			{
				indSwitch = ind;
				continue;
			}
			if (indSwitch >= 0 && RxCasa.Match(txt) is { Success: true } mc)
			{
				casa = new EscolhaDeSkill { Rotulo = mc.Groups["s"].Value };
				d.Escolhas.Add(casa);
				indCasa = ind;
				continue;
			}
			if (casa != null)
			{
				Colher(txt, casa.Buffs, casa.Mults, casa.Genes, casa.Flags, casa.Verbos, _ => { });
				continue;
			}
			if (indSwitch < 0) Colher(txt, d.Buffs, d.Mults, d.Genes, d.Flags, d.Verbos, e => d.Estilo = e);
		}
	}

	/// <summary>
	/// UMA LINHA DE CORPO DE `after_learn()`: os canais de efeito mais a delegacao.
	///
	/// E funcao pelo mesmo motivo que o <see cref="Colher"/> virou uma: agora ha DOIS chamadores
	/// -- o direto (dono ja lido) e o ADIADO (dono so apareceu noutro arquivo, ver
	/// <see cref="CorposDeAprendizado"/>). Duas copias divergiriam, e divergir AQUI seria a skill
	/// valer coisas diferentes conforme a ordem alfabetica do disco -- que e exatamente o defeito
	/// que este conserto existe pra matar, e nao pra mudar de lugar.
	///
	/// Nao se usa o <see cref="Absorver"/> aqui de proposito: ele le `switch(input(...))` e o
	/// caminho direto do `after_learn` nao le. Reaproveita-lo faria o corpo adiado ser lido com
	/// regra diferente da do corpo normal -- a mesma divergencia, so que escondida.
	/// </summary>
	/// <summary>`switch(savant.trinitytype)` -- a casa unica decidida por uma var do mob (Bodybuilding.dm:243).</summary>
	private static readonly Regex RxEscolhaPorVar = new(
		@"^switch\s*\(\s*savant\.(?<v>[A-Za-z_][A-Za-z0-9_]*)\s*\)\s*$", RegexOptions.Compiled);

	/// <summary>`storedBP = max(1,savant.BP*0.01)` -- um local do datum preenchido com a ficha.</summary>
	private static readonly Regex RxLocal = new(
		@"^(?<v>[A-Za-z_][A-Za-z0-9_]*)\s*=(?!=)\s*(?<e>.+)$", RegexOptions.Compiled);

	/// <summary>`savant.BP += storedBP` / `savant.hiddenpotential += (savant.relBPmax*0.5)` -- o ganho com expressao.</summary>
	private static readonly Regex RxGanho = new(
		@"^savant\.(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op>\+=|-=)\s*(?<e>.+)$", RegexOptions.Compiled);

	/// <summary>
	/// O CORPO INTEIRO DE UM `after_learn()`, com indentacao -- as TRES formas de efeito que ele pode ter:
	///
	///   1. o efeito incondicional, linha a linha (<see cref="Colher"/>) mais a delegacao (`choose()`);
	///   2. a ESCOLHA UNICA direto no corpo: `switch(input(...) in list(...))` (TheHolyTrinity,
	///      Bodybuilding.dm:119) ou `switch(savant.<var>)` (Grace, :243) com `if("casa")` dentro.
	///      Antes so a forma DELEGADA (`choose()`, <see cref="Absorver"/>) era lida como escolha, e a
	///      direta era SOMADA: a Holy Trinity saia com as tres casas juntas (`physdefBuff=0.6`,
	///      `physoffBuff=0.6`) -- errado pra mais, e calado;
	///   3. o GANHO NA COMPRA COM EXPRESSAO: `storedBP = max(1,savant.BP*0.01)` + `savant.BP+=storedBP`
	///      (:89-92). O local e substituido pela expressao e a linha vira `BP+=max(1,BP*0.01)`, que o
	///      Core avalia na compra. O que o Core nao le vai pro diario, nao pro json.
	/// </summary>
	private static void AbsorverAprendizado(SkillDef alvo, string skill, List<(int Ind, string Txt)> corpo)
	{
		int indSwitch = -1;
		EscolhaDeSkill? casa = null;
		int indCasa = -1;
		var locais = new Dictionary<string, string>(StringComparer.Ordinal);
		EscolhaDeSkill? raca = null;   // dentro de um `if(savant.Race=="X")` em bloco
		int indRaca = -1, indElse = -1;

		foreach ((int ind, string linhaCrua) in corpo)
		{
			string txt = linhaCrua;
			if (indCasa >= 0 && ind <= indCasa) { casa = null; indCasa = -1; }
			if (indSwitch >= 0 && ind <= indSwitch) indSwitch = -1;

			// ============================ O EFEITO CONDICIONADO A RACA ============================
			// `if(savant.Race=="Yardrat")` + corpo indentado (yardrat.dm:85-87). O que mora dentro vai
			// pra `PorRaca` com o rotulo da raca. O `else` que o segue (`else to_chat(...)`, :88) e
			// ignorado -- e, se tiver efeito, vai pro diario `CondicionaisNaoLidas`: "todo mundo menos
			// X" nao tem canal, e inventar um seria adivinhar. Ate aqui o `if` era INVISIVEL pra este
			// laco e a linha de dentro saia como flag de todo mundo.
			// ====================================================================================
			if (indRaca >= 0 && ind <= indRaca)
			{
				raca = null; indRaca = -1;
				if (txt.StartsWith("else", StringComparison.Ordinal))
				{
					string restoElse = txt[4..].TrimStart();
					if (restoElse.Length == 0) { indElse = ind; continue; }        // `else` em bloco: pula o corpo
					if (restoElse.StartsWith("if", StringComparison.Ordinal)) txt = restoElse;   // `else if(...)`: le como `if`
					else { if (TemEfeito(restoElse)) CondicionaisNaoLidas.Add((skill, linhaCrua)); continue; }
				}
			}
			if (indElse >= 0)
			{
				if (ind > indElse) { if (TemEfeito(txt)) CondicionaisNaoLidas.Add((skill, linhaCrua)); continue; }
				indElse = -1;
			}
			if (raca != null)
			{
				Colher(txt, raca.Buffs, raca.Mults, raca.Genes, raca.Flags, raca.Verbos, _ => { });
				continue;
			}
			if (indSwitch < 0 && casa == null && RxPorRaca.Match(txt) is { Success: true } mr)
			{
				string nome = mr.Groups["r"].Value;
				EscolhaDeSkill entrada = alvo.PorRaca.Find(e => e.Rotulo == nome) ?? new EscolhaDeSkill { Rotulo = nome };
				if (!alvo.PorRaca.Contains(entrada)) alvo.PorRaca.Add(entrada);
				string inline = mr.Groups["resto"].Value.Trim();
				if (inline.Length > 0) Colher(inline, entrada.Buffs, entrada.Mults, entrada.Genes, entrada.Flags, entrada.Verbos, _ => { });
				else { raca = entrada; indRaca = ind; }
				continue;
			}

			if (indSwitch < 0 && RxEscolhaLista.IsMatch(txt)) { indSwitch = ind; continue; }
			if (indSwitch < 0 && RxEscolhaPorVar.Match(txt) is { Success: true } mv)
			{
				// `switch(level)` no after_learn (kaioken.dm:88) NAO e escolha: e o nivel de entrada, e
				// as casas dele se somam do jeito que sempre se somaram
				indSwitch = ind;
				alvo.EscolhaPorVar = mv.Groups["v"].Value;
				continue;
			}
			if (indSwitch >= 0 && RxCasa.Match(txt) is { Success: true } mc)
			{
				casa = new EscolhaDeSkill { Rotulo = mc.Groups["s"].Value };
				alvo.Escolhas.Add(casa);
				indCasa = ind;
				continue;
			}
			if (casa != null)
			{
				Colher(txt, casa.Buffs, casa.Mults, casa.Genes, casa.Flags, casa.Verbos, _ => { });
				continue;
			}
			if (indSwitch >= 0) continue;   // dentro do switch, fora de casa: contabilidade do datum

			// 3) O LOCAL PREENCHIDO COM A FICHA, e o ganho que o usa. Vem ANTES do `Colher` porque
			// `savant.BP += storedBP` nao casa com nenhum canal de constante -- e `savant.X += 0.5`
			// nao casa aqui, porque o numero puro e do `Colher`.
			// (sem `continue`: `attachedstyle = new /datum/style/KameStyle` tambem casa aqui e e o
			// canal de ESTILO do `Colher` -- anotar o local nao tira a linha de ninguem)
			if (RxLocal.Match(txt) is { Success: true } ml && !txt.StartsWith("savant.", StringComparison.Ordinal)
				&& ml.Groups["e"].Value.Contains("savant.", StringComparison.Ordinal))
				locais[ml.Groups["v"].Value] = ml.Groups["e"].Value.Trim();
			if (RxGanho.Match(txt) is { Success: true } mg && !EhNumero(mg.Groups["e"].Value))
			{
				string expr = mg.Groups["e"].Value.Trim();
				foreach ((string nome, string valor) in locais)
					expr = Regex.Replace(expr, @"(?<![A-Za-z0-9_.])" + Regex.Escape(nome) + @"(?![A-Za-z0-9_])", "(" + valor + ")");

				// SO E GANHO NA COMPRA O QUE LE A FICHA (`savant.` na expressao, direto ou pelo local).
				// `savant.availableStyles += attachedstyle` e o canal de ESTILO e `savant.known_words +=
				// "..."` e magia -- os dois caem no `Colher` de sempre, que sabe (ou nao) o que fazer.
				if (expr.Contains("savant.", StringComparison.Ordinal))
				{
					string ganho = mg.Groups["c"].Value + mg.Groups["op"].Value + DmGalhosScanner.Normalizar(expr);
					// VALIDADO PELO LEITOR DE VERDADE: se o Core nao parseia, nao vai pro json
					if (Jandirus.Core.Skills.GanhoNaCompra.Parse(ganho) != null) alvo.Compra.Add(ganho);
					else ComprasNaoLidas.Add((skill, txt));
					continue;
				}
			}

			Colher(txt, alvo.Buffs, alvo.Mults, alvo.Genes, alvo.Flags, alvo.Verbos, e => alvo.Estilo = e);

			// O `after_learn()` QUE SO DELEGA. `Great_Robotic_Alliance` (meta.dm:127) tem corpo de duas
			// linhas: `choose()` e um `to_chat`. Todo o efeito da skill mora no proc PROPRIO que ela
			// chama, e por isso ela saia do extrator como muda. Aqui so se anota a chamada; o corpo do
			// proc e resolvido no fim da varredura, porque ele pode ter sido declarado ANTES do
			// `after_learn` no arquivo.
			if (RxChamadaSimples.Match(txt) is { Success: true } mch)
				Chamados.Add((skill, mch.Groups["n"].Value));
		}
	}

	private static bool EhNumero(string s) =>
		double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

	/// <summary>
	/// OS CINCO CANAIS DE EFEITO numa linha de DM, lidos de uma vez.
	///
	/// Estava embutido no laco do `after_learn()` e virou funcao porque agora ha um SEGUNDO lugar
	/// que le corpo de proc (<see cref="Absorver"/>). Duas copias da mesma leitura divergiriam --
	/// e divergir aqui e a skill valer coisas diferentes conforme onde o DM a escreveu.
	/// </summary>
	private static void Colher(string corpo,
		Dictionary<string, double> buffs, Dictionary<string, double> mults,
		Dictionary<string, double> genes, Dictionary<string, double> flags,
		List<string> verbos, Action<string> estilo)
	{
		foreach (Match mb in RxBuff.Matches(corpo))
		{
			if (!double.TryParse(mb.Groups["v"].Value, NumberStyles.Float,
					CultureInfo.InvariantCulture, out double vb)) continue;
			double sinal = mb.Groups["op"].Value == "-=" ? -1 : 1;
			string campo = mb.Groups["c"].Value;
			buffs[campo] = buffs.GetValueOrDefault(campo) + vb * sinal;
		}
		foreach (Match mm in RxMult.Matches(corpo))
		{
			if (!double.TryParse(mm.Groups["v"].Value, NumberStyles.Float,
					CultureInfo.InvariantCulture, out double vm) || vm == 0) continue;
			string campo = mm.Groups["c"].Value;
			double fator = mm.Groups["op"].Value == "/=" ? 1 / vm : vm;
			// COMPOEM, nao substitui: duas skills que dobram o mesmo campo dao 4x.
			mults[campo] = mults.GetValueOrDefault(campo, 1) * fator;
		}
		foreach (Match mi in RxIncr.Matches(corpo))
		{
			string campo = mi.Groups["c"].Value;
			double dd = mi.Groups["op"].Value == "--" ? -1 : 1;
			buffs[campo] = buffs.GetValueOrDefault(campo) + dd;
		}
		foreach (Match mg in RxGene.Matches(corpo))
		{
			if (!double.TryParse(mg.Groups["v"].Value, NumberStyles.Float,
					CultureInfo.InvariantCulture, out double vg)) continue;
			double sinal = mg.Groups["op"].Value == "sub_to_stat" ? -1 : 1;
			string stat = mg.Groups["s"].Value;
			genes[stat] = genes.GetValueOrDefault(stat) + vg * sinal;
		}
		foreach (Match ms in RxSet.Matches(corpo))
		{
			if (double.TryParse(ms.Groups["v"].Value, NumberStyles.Float,
					CultureInfo.InvariantCulture, out double vs))
				flags[ms.Groups["c"].Value] = vs;
		}
		foreach (Match mv in RxVerb.Matches(corpo))
		{
			string v = mv.Groups["v"].Value;
			if (!verbos.Contains(v)) verbos.Add(v);
		}
		// O SEGUNDO CANAL DE VERB. Em BYOND, um obj dentro de `mob.contents` empresta os verbs dele
		// ao dono -- entao `contents += new/obj/SplitForm` concede uma habilidade sem passar por
		// `assignverb`. Doze skills fazem so isso, e um extrator que so olhasse `assignverb` as
		// daria como "sem efeito".
		if (DmStyleScanner.RxAtacha.Match(corpo) is { Success: true } me)
		{
			// `new` SEM tipo usa o tipo declarado no var da skill -- e o caso da Martial Arts, cujo
			// `attachedstyle` ja e um /datum/style/BasicStyle. Sem este fallback a unica skill de
			// estilo que TODO mundo pega sairia sem estilo.
			string idE = me.Groups["id"].Value;
			estilo(idE.Length > 0 ? idE : "BasicStyle");
		}
		foreach (Match mo in RxObj.Matches(corpo))
		{
			string v = mo.Groups["o"].Value;
			if (!verbos.Contains(v)) verbos.Add(v);
		}
	}

	/// <summary>
	/// TIRA O COMENTARIO DA LINHA -- os dois tipos, e a profundidade viaja entre chamadas.
	///
	/// ============================ AS DUAS ARMADILHAS QUE ISTO PAGA ============================
	/// 1) O `/*/`. O jeito que o DM usa pra comentar um typepath inteiro e colar o `/*` nele:
	///
	///        /*/datum/skill/tree/Focused      <- abre o bloco morto
	///            name = "Focused Ki"
	///        */
	///
	///    A versao anterior perguntava "a linha contem `*/`?" e achava o `*/` DA PROPRIA ABERTURA
	///    (os caracteres 1 e 2 de `/*/`): o bloco fechava na mesma linha em que abria. Sao DEZ
	///    arquivos assim -- as arvores de Ki antigas inteiras (Mind, Ki_Control, Focused, Diffuse,
	///    Rudeff, AdvEffusion), o `kienzan` velho, o `blast` velho.
	///
	/// 2) O ANINHAMENTO. O DM permite `/* */` dentro de `/* */`, e o jogo usa: em
	///    `Rudimentary Effusion.dm` a arvore morta guarda um `/*constituentskills = ...*/` dentro
	///    dela. Sem contar profundidade, o fechamento do comentario de DENTRO ressuscitava trinta
	///    linhas de codigo morto -- e era dali que saia o `Rudeff/mod()` no diario.
	///
	/// O `//` entra aqui pelo mesmo motivo: com ele solto la fora, uma linha `// nota /* x` abria
	/// um bloco que o DM nunca abriu. Ele so vale FORA de bloco, e mata o resto da linha --
	/// literal ao `/datum/skill/mind/Ki_Unlocked//nota` que ja tinha custado a raiz da arvore de Ki.
	/// ==========================================================================================
	/// </summary>
	public static string SemComentario(string linha, ref int prof)
	{
		var saida = new StringBuilder(linha.Length);
		int i = 0;
		while (i < linha.Length)
		{
			bool par = i + 1 < linha.Length;
			if (prof > 0)
			{
				if (par && linha[i] == '*' && linha[i + 1] == '/') { prof--; i += 2; continue; }
				if (par && linha[i] == '/' && linha[i + 1] == '*') { prof++; i += 2; continue; }
				i++;
				continue;
			}
			if (par && linha[i] == '/' && linha[i + 1] == '*') { prof++; i += 2; continue; }
			if (par && linha[i] == '/' && linha[i + 1] == '/') break;
			saida.Append(linha[i]);
			i++;
		}
		return saida.ToString();
	}

	/// <summary>A linha mexe no corpo de alguem? So pro diario -- nao aplica nada.</summary>
	private static bool TemEfeito(string corpo) =>
		RxBuff.IsMatch(corpo) || RxMult.IsMatch(corpo) || RxIncr.IsMatch(corpo)
		|| RxGene.IsMatch(corpo) || RxSet.IsMatch(corpo) || RxVerb.IsMatch(corpo)
		|| RxObj.IsMatch(corpo) || DmStyleScanner.RxAtacha.IsMatch(corpo);

	private static void Herdar(SkillDef d, Dictionary<string, SkillDef> defs, int prof)
	{
		if (prof > 12 || d.Pai == null || !defs.TryGetValue(d.Pai, out SkillDef? p)) return;
		Herdar(p, defs, prof + 1);
		if (d.Racas.Count == 0) d.Racas = [.. p.Racas];
		if (d.Classes.Count == 0) d.Classes = [.. p.Classes];
		if (d.Desc.Length == 0) d.Desc = p.Desc;
		// O `teachCost` E DECLARADO UMA VEZ SO no jogo inteiro (`/datum/skill/rank`, `RankTree.dm:12`)
		// e chega nas skills de cargo por heranca. Ver <see cref="SkillDef.CustoDeEnsino"/>.
		if (d.CustoDeEnsino == 0) d.CustoDeEnsino = p.CustoDeEnsino;
	}

	/// <summary>
	/// Le um arquivo .dm reconstruindo a arvore de tipos POR INDENTACAO.
	///
	/// O DM aceita as duas formas, e o jogo usa as duas:
	///
	///     /datum/skill/Assassain/Backstab      <- caminho absoluto, uma linha
	///     /datum/skill/tree                    <- ...e caminho RELATIVO, aninhado:
	///         meta                                  vira /datum/skill/tree/meta
	///             name = "Meta Racials"
	///
	/// A primeira versao so entendia a forma absoluta e perdia blocos inteiros em silencio --
	/// a arvore racial dos Metamorianos, por exemplo, some do catalogo e aquele povo fica sem
	/// progressao nenhuma sem que nada acuse. Uma pilha por nivel de indentacao resolve os dois
	/// casos com o mesmo codigo.
	/// </summary>
	private static void Ler(string arq, Dictionary<string, SkillDef> defs)
	{
		string[] linhas = File.ReadAllLines(arq);

		// pilha[i] = o typepath que abre no nivel de indentacao i
		var pilha = new string[64];
		int indProc = -1;
		string? donoProc = null;   // de qual skill e o proc aberto
		string? nomeProc = null;   // o nome dele
		bool procProprio = false;  // veio com o prefixo `proc/` (declaracao nova) ou sobrescreve o motor?

		// O `after_learn()` E A EXCECAO A REGRA DE PULAR CORPO DE PROC: e nele que mora o EFEITO da
		// skill, e em 333 delas o efeito e mecanico -- `savant.physoffBuff += 0.1`. E o unico corpo
		// de proc que da pra ler sem interpretar DM. (O `before_forget()` e o espelho negativo do
		// mesmo; ignorado de proposito -- desaprender e reconstruir a soma do zero.)
		int indLearn = -1;      // indentacao da linha `after_learn(...)` aberta
		string? donoLearn = null;

		// DENTRO DE UM /* */? O DM tem codigo morto de verdade guardado em bloco comentado -- dez
		// definicoes de `after_learn` e uma skill de estilo inteira. Sem isto o catalogo do jogo
		// ganha skills que NAO EXISTEM em runtime, e o pior tipo delas: a que parece funcionar.
		// Quem faz a conta e o <see cref="SemComentario"/>; aqui mora so a PROFUNDIDADE, porque no
		// DM um `/* */` pode estar dentro de outro -- e esta justamente dentro, em dois arquivos.
		int profCom = 0;

		for (int i = 0; i < linhas.Length; i++)
		{
			string linha = linhas[i].TrimEnd();
			if (linha.Length == 0) continue;

			int ind = 0;
			while (ind < linha.Length && linha[ind] == '\t') ind++;
			if (ind >= pilha.Length - 1) continue;
			string corpo = linha[ind..];

			corpo = SemComentario(corpo, ref profCom).TrimEnd();
			if (corpo.Length == 0) continue;

			if (indLearn >= 0 && ind <= indLearn) { indLearn = -1; donoLearn = null; }
			if (indProc >= 0 && ind <= indProc) { indProc = -1; donoProc = null; nomeProc = null; procProprio = false; }

			// DENTRO do after_learn: so colhe buff e SEGUE. Nao sai daqui por causa de um `if(...)`
			// -- o corpo tem condicionais, e tratar cada uma como "outro proc" fazia perder tudo
			// depois da primeira linha de logica. Quem fecha o bloco e a indentacao, so ela.
			if (indLearn >= 0)
			{
				if (donoLearn != null)
				{
					// O CORPO E GUARDADO INTEIRO, com indentacao, e lido no fim da varredura -- o
					// `switch(input(...))` de casa unica e o local `storedBP` do ganho na compra so se
					// leem com o bloco todo na mao. Dono ja lido ou nao, o caminho e o mesmo; a
					// diferenca fica anotada (<see cref="DonosLidosNaHora"/>) porque e ela que a
					// bancada do extrator mede. Antes, o corpo de dono nao-lido era jogado fora em
					// silencio -- ver <see cref="CorposDeAprendizado"/>.
					if (defs.ContainsKey(donoLearn)) DonosLidosNaHora.Add(donoLearn);
					if (!CorposDeAprendizado.TryGetValue(donoLearn, out List<(int, string)>? linhasLearn))
						CorposDeAprendizado[donoLearn] = linhasLearn = [];
					linhasLearn.Add((ind, corpo));
				}
				continue;
			}

			// CORPO DE PROC QUE NAO E O `after_learn`. Nao se pula mais em silencio: se a linha
			// mexe no `savant`, ela vai pro diario (<see cref="ProcsComEfeito"/>) e, se for de um
			// proc PROPRIO da skill, o texto e guardado pra eventual leitura la no fim.
			if (indProc >= 0)
			{
				if (donoProc != null && nomeProc != null)
				{
					// O `growbranches()` DE UMA ARVORE e o unico corpo de proc de arvore que se le: e
					// ele que sobe o tier, abre outras arvores e acende skills. Guardado cru com a
					// indentacao (o `if` aninhado E a condicao) e traduzido no fim -- ver
					// <see cref="CorposDeGalhos"/>.
					// (`!procProprio` deixa de fora a declaracao BASE do motor,
					// `/datum/skill/tree/proc/growbranches()` em `trees.dm:136`, que nao e arvore nenhuma)
					//
					// O `prunebranches()` ENTRA TAMBEM, atras do `growbranches()`: o `testunlocks()` do DM
					// chama os dois em sequencia (`SkillTreesWindow.dm:306-310`), e ha exclusividade que
					// so mora no prune -- as tres especialidades de Ki se APAGAM uma a outra em
					// `Effusion Specialization.dm:37-47`. O tier que o prune abaixa e redundante num
					// recalculo do zero, mas nao atrapalha: a mesma condicao da o mesmo numero.
					// O `treegrow()`/`treeshrink()` DAS ARVORES RACIAIS ENTRAM PELA MESMA PORTA: e neles
					// que Arlian, Tsujin, Yardrat e Spirit Doll apagam a irma da skill escolhida
					// (`if(savant.pitted==1) disableskill(Supa)`, arlian.dm:13-16). O motor os chama
					// depois da compra (`SkillTreesWindow.dm:345`) e no encolhimento (`trees.dm:96`);
					// num recalculo do zero o `treegrow` vale como grow e o `treeshrink` como prune.
					if (nomeProc is "growbranches" or "prunebranches" or "treegrow" or "treeshrink" && !procProprio
						&& donoProc.StartsWith("/datum/skill/tree/", StringComparison.OrdinalIgnoreCase))
					{
						Dictionary<string, List<(int Ind, string Txt)>> onde =
							nomeProc is "growbranches" or "treegrow" ? CorposDeGalhos : CorposDePoda;
						if (!onde.TryGetValue(donoProc, out List<(int, string)>? galhos))
							onde[donoProc] = galhos = [];
						galhos.Add((ind, corpo));
					}

					// PROC PROPRIO da skill: no DM ele nasce com o prefixo `proc/`, e e essa palavra
					// -- nao uma lista de nomes conhecidos -- que separa "declarei um proc novo" de
					// "sobrescrevi um do motor". Guarda-se o corpo INTEIRO, com a indentacao, porque
					// e ela que carrega os ramos do `switch` la no fim.
					if (procProprio)
					{
						if (!Corpos.TryGetValue((donoProc, nomeProc), out List<(int, string)>? linhasProc))
							Corpos[(donoProc, nomeProc)] = linhasProc = [];
						linhasProc.Add((ind, corpo));
					}
					if (TemEfeito(corpo))
					{
						var chave = (donoProc, nomeProc);
						if (!ProcsComEfeito.ContainsKey(chave))
							ProcsComEfeito[chave] = $"{Path.GetFileName(arq)}:{i + 1}";
					}
				}
				continue;
			}

			// linha de continuacao do DM (barra invertida no fim)
			while (corpo.EndsWith('\\') && i + 1 < linhas.Length)
				corpo = corpo[..^1] + SemComentario(linhas[++i].Trim(), ref profCom).TrimEnd();

			// ============================ A CONTINUACAO POR PARENTESE ABERTO ============================
			// O DM tambem continua uma lista SEM a barra: basta o `(` do `list(` ainda estar aberto.
			// Tres arvores declaram os galhos assim (`Cultivation.dm:8`, `spirit-doll.dm:10`,
			// `CustomAttacks/physical/basic.dm:8`), e so ler a barra PERDIA 13 galhos calado -- a
			// Cultivation saia com 4 dos 7 (sem Ki_Shield, Shield_Bash e Sharpened_Ki), a Spirit Doll
			// com 3 dos 6. Skill que nao pende de arvore nenhuma e skill que ninguem compra.
			//
			// O parentese e contado FORA de aspas: uma `desc` com `(` solto dentro do texto nao pode
			// engolir a linha seguinte.
			// ==========================================================================================
			while (Desbalanco(corpo) > 0 && i + 1 < linhas.Length)
				corpo += SemComentario(linhas[++i].Trim(), ref profCom).TrimEnd();

			// --- propriedade ---
			int igual = corpo.IndexOf('=');
			bool ehProp = igual > 0 && corpo[igual - 1] is not ('=' or '!' or '<' or '>' or '+' or '-' or '*' or '/');
			if (ehProp)
			{
				string dono = ind > 0 ? pilha[ind - 1] ?? "" : "";
				if (dono.Length > 0 && defs.TryGetValue(dono, out SkillDef? d))
					Aplicar(d, corpo[..igual].Trim(), corpo[(igual + 1)..].Trim());
				continue;
			}

			// --- corpo de proc: `after_learn()`, `login(mob/x)`, `growbranches()` ---
			if (corpo.EndsWith(')') && corpo.Contains('('))
			{
				// a forma de TOPO: o caminho da skill vem na propria linha
				if (RxLearnSolto.Match(corpo) is { Success: true } ml)
				{
					indLearn = ind;
					donoLearn = Normalizar(ml.Groups["p"].Value);

					// `/datum/skill/proc/after_learn()` (`Skills Master/skill.dm:97`, corpo `return`)
					// e a DECLARACAO BASE do proc no motor do DM, nao uma skill chamada "proc". A
					// `RxLearnSolto` casa com ela porque `proc` parece um segmento de typepath. O
					// MESMO filtro do registro de typepath vale aqui -- sem ele o diario dos orfaos
					// nasceria com um falso alarme dentro, e diario com falso positivo e diario que
					// ninguem le.
					if (donoLearn.Contains("/proc/", StringComparison.Ordinal)
						|| donoLearn.EndsWith("/proc", StringComparison.Ordinal)
						|| donoLearn.Contains("/verb/", StringComparison.Ordinal)
						|| donoLearn.EndsWith("/verb", StringComparison.Ordinal))
						donoLearn = null;
					else if (!defs.ContainsKey(donoLearn))
						OndeAdiou.TryAdd(donoLearn, $"{Path.GetFileName(arq)}:{i + 1}");
				}
				else if (corpo.StartsWith("after_learn(", StringComparison.Ordinal) && ind > 0)
				{
					indLearn = ind;
					donoLearn = pilha[ind - 1];
				}
				else
				{
					indProc = ind;
					// DE QUEM E ESTE PROC, E ELE E NOVO OU SOBRESCRITO?
					//
					// Duas formas, como em tudo neste arquivo: `proc/choose()` aninhado no bloco da
					// skill, e `/datum/skill/x/proc/choose()` com o caminho na linha. O prefixo
					// `proc/` e o que o DM usa pra DECLARAR um proc novo -- sem ele, a linha esta
					// sobrescrevendo um proc do motor (`login`, `effector`, `before_forget`).
					Match mp = RxProcSolto.Match(corpo);
					if (mp.Success)
					{
						donoProc = Normalizar(mp.Groups["p"].Value);
						nomeProc = mp.Groups["n"].Value;
						procProprio = mp.Groups["pp"].Success;
					}
					else
					{
						donoProc = ind > 0 ? pilha[ind - 1] : null;
						procProprio = corpo.StartsWith("proc/", StringComparison.Ordinal);
						string cru = procProprio ? corpo["proc/".Length..] : corpo;
						int par = cru.IndexOf('(');
						nomeProc = par > 0 ? cru[..par] : null;
						if (nomeProc != null && !nomeProc.All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
							nomeProc = null;
					}
					if (donoProc != null && !donoProc.StartsWith("/datum/skill", StringComparison.OrdinalIgnoreCase))
						donoProc = null;
				}
				continue;
			}

			// --- segmento de typepath ---
			// so aceita o que PARECE typepath: letras, digitos, `_` e `/`. Isso corta linhas de
			// codigo solto (`..()`, `return`, `if(...)`) que caem aqui sem `=` e sem `()`.
			if (!corpo.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '/')) continue;

			// ABSOLUTO SEM A BARRA. O DM aceita `datum/skill/tree/Body` no comeco da linha, sem
			// a barra inicial, e o jogo usa as duas formas -- as arvores centrais (Body, Mind,
			// Spirit) sao justamente assim. Tratar isso como segmento relativo fazia elas
			// sumirem do catalogo, e sao as arvores que TODO personagem recebe.
			string pai = ind > 0 ? pilha[ind - 1] ?? "" : "";
			bool absoluto = corpo.StartsWith('/') || corpo.StartsWith("datum/", StringComparison.Ordinal);
			string caminho = absoluto ? Normalizar(corpo)
						   : pai.Length > 0 ? pai + "/" + corpo.Trim('/')
						   : "";
			if (caminho.Length == 0) continue;

			pilha[ind] = caminho;
			for (int k = ind + 1; k < pilha.Length; k++) pilha[k] = null!;

			// `proc` e `verb` sao ramos do MOTOR, nao skills; e `/datum/skill/tree/proc/x` nao
			// e uma arvore chamada "proc"
			if (caminho.Contains("/proc/") || caminho.Contains("/verb/") || caminho.EndsWith("/proc") || caminho.EndsWith("/verb"))
				continue;
			if (!caminho.StartsWith("/datum/skill", StringComparison.OrdinalIgnoreCase)) continue;

			// ARVORE E O QUE MORA DEBAIXO DE `/datum/skill/tree/` -- com a barra. Sem ela,
			// `/datum/skill/Tree_Mastery` (uma FOLHA da Weapons Expert, `Weapons Expert.dm:83`) casava
			// com o prefixo e saia do extrator como arvore sem galho nenhum; o menu precisava de uma
			// gambiarra pra esconder o card vazio.
			if (!defs.ContainsKey(caminho))
				defs[caminho] = new SkillDef
				{
					Path = caminho,
					Arvore = caminho.Equals("/datum/skill/tree", StringComparison.OrdinalIgnoreCase)
						  || caminho.StartsWith("/datum/skill/tree/", StringComparison.OrdinalIgnoreCase),
				};
		}
	}

	/// <summary>Parenteses abertos menos fechados, ignorando o que esta entre aspas.</summary>
	private static int Desbalanco(string s)
	{
		if (!s.Contains('(')) return 0;
		int prof = 0;
		bool aspas = false;
		foreach (char ch in s)
		{
			if (ch == '"') aspas = !aspas;
			if (aspas) continue;
			if (ch == '(') prof++;
			else if (ch == ')') prof--;
		}
		return prof;
	}

	private static void Aplicar(SkillDef d, string chave, string valor)
	{
		// tira o `var/` de declaracoes com valor e comentarios de fim de linha
		valor = Regex.Replace(valor, @"\s//.*$", "").Trim();
		string cru = valor.Trim('"');

		switch (chave.ToLowerInvariant())
		{
			case "name": d.Nome = cru; break;
			case "desc": d.Desc = cru; break;
			case "skilltype": d.Tipo = cru; break;
			case "tier": if (int.TryParse(cru, out int t)) d.Tier = t; break;
			case "skillcost": if (int.TryParse(cru, out int c)) d.Custo = c; break;
			case "maxlevel": if (int.TryParse(cru, out int ml)) d.MaxNivel = ml; break;
			case "prereqthreshold": if (int.TryParse(cru, out int pt)) d.PreReqFolga = pt; break;
			case "fixedcost": d.CustoFixo = cru is "1" or "TRUE"; break;
			case "common_sense": d.Comum = cru is "1" or "TRUE"; break;
			case "enabled": d.Ligada = cru is not ("0" or "FALSE"); break;
			case "can_forget": d.Esquecivel = cru is not ("0" or "FALSE"); break;
			case "allowedtier": if (int.TryParse(cru, out int at)) d.TierInicial = at; break;
			case "maxtier": if (int.TryParse(cru, out int mt)) d.TierMax = mt; break;
			case "villainonly": d.SoVilao = cru is "1" or "TRUE"; break;
			case "teacher": d.Ensinavel = cru is "1" or "TRUE"; break;
			case "teachcost": if (int.TryParse(cru, out int tc)) d.CustoDeEnsino = tc; break;
			case "compatible_races": d.Racas = Textos(valor); break;
			case "compatible_classes": d.Classes = Textos(valor); break;
			case "prereqs": d.PreReqs = Caminhos(valor); break;
			case "constituentskills": d.Constituintes = Caminhos(valor); break;
		}
	}

	private static List<string> Textos(string v)
	{
		var l = new List<string>();
		foreach (Match m in RxItem.Matches(v))
			if (m.Groups["s"].Success && m.Groups["s"].Value.Length > 0) l.Add(m.Groups["s"].Value);
		return l;
	}

	private static List<string> Caminhos(string v)
	{
		var l = new List<string>();
		foreach (Match m in RxItem.Matches(v))
			if (m.Groups["p"].Success) l.Add(Normalizar(m.Groups["p"].Value));
		return l;
	}

	private static string Normalizar(string p)
	{
		p = p.Replace("new", "").Replace(" ", "").Trim();
		return p.StartsWith('/') ? p : "/" + p;
	}

	// =====================================================================
	// QUEM GANHA QUAL ARVORE
	// =====================================================================
	/// <summary>Arvores que TODO MUNDO recebe, e as que vem por raca ou por classe.</summary>
	public sealed class Concessoes
	{
		public List<string> Todos = [];
		public Dictionary<string, List<string>> PorRaca = new(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, List<string>> PorClasse = new(StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Le o `generatetrees()` (Skill Trees/mobhandlers.dm:43) e transcreve QUEM GANHA O QUE.
	///
	/// E um proc, nao uma tabela -- mas e um proc com uma forma so, repetida vinte e cinco
	/// vezes: `if(r_race=="Namekian") getTree(new /datum/skill/tree/namek)`. Ler isso e mais
	/// seguro que copiar na mao: um nome de raca digitado errado aqui nao quebra nada visivel,
	/// so faz um povo inteiro nunca receber a propria arvore racial.
	/// </summary>
	public static Concessoes LerConcessoes(string pastaCode)
	{
		var c = new Concessoes();
		string arq = Path.Combine(pastaCode, "Modules", "Skills", "Skill Trees", "mobhandlers.dm");
		if (!File.Exists(arq)) return c;

		var rxRaca = new Regex(@"r_race\s*==\s*""(?<r>[^""]+)""", RegexOptions.Compiled);
		var rxClasse = new Regex(@"Class\s*==\s*""(?<c>[^""]+)""", RegexOptions.Compiled);
		var rxArvore = new Regex(@"getTree\(\s*new\s*(?<p>/datum/skill/tree/[A-Za-z0-9_/]+)", RegexOptions.Compiled);

		string[] linhas = File.ReadAllLines(arq);
		int ini = Array.FindIndex(linhas, l => l.Contains("proc/generatetrees", StringComparison.Ordinal));
		if (ini < 0) return c;

		string? raca = null, classe = null;
		int indRaca = -1, indClasse = -1;
		bool racialLigado = false;

		for (int i = ini + 1; i < linhas.Length; i++)
		{
			string l = linhas[i].TrimEnd();
			if (l.Length == 0) continue;
			int ind = 0; while (ind < l.Length && l[ind] == '\t') ind++;
			if (ind == 0) break;   // saiu do proc
			// linha comentada nao concede arvore nenhuma -- o `//getTree(Custom_Attacks)` do
			// original e uma arvore DESLIGADA, e le-la daria a todo mundo uma arvore que nem
			// existe no catalogo
			if (l[ind..].StartsWith("//", StringComparison.Ordinal)) continue;

			// o `else` do `if(GenerateRacials == null || == 0)` separa as gerais das raciais
			if (l.Trim() == "else") { racialLigado = true; raca = classe = null; continue; }

			if (raca != null && ind <= indRaca) raca = null;
			if (classe != null && ind <= indClasse) classe = null;

			Match mr = rxRaca.Match(l);
			if (mr.Success) { raca = mr.Groups["r"].Value; indRaca = ind; classe = null; continue; }

			Match mc = rxClasse.Match(l);
			if (mc.Success) { classe = mc.Groups["c"].Value; indClasse = ind; continue; }

			Match ma = rxArvore.Match(l);
			if (!ma.Success) continue;
			string arvore = ma.Groups["p"].Value;

			if (classe != null) Somar(c.PorClasse, classe, arvore);
			else if (raca != null) Somar(c.PorRaca, raca, arvore);
			else if (!racialLigado && !c.Todos.Contains(arvore)) c.Todos.Add(arvore);
		}
		return c;
	}

	private static void Somar(Dictionary<string, List<string>> d, string chave, string valor)
	{
		if (!d.TryGetValue(chave, out List<string>? l)) d[chave] = l = [];
		if (!l.Contains(valor)) l.Add(valor);
	}

	public static void EscreverConcessoes(string caminho, Concessoes c)
	{
		var sb = new StringBuilder();
		sb.Append("{\n");
		sb.Append($"  \"todos\": [{string.Join(", ", c.Todos.Select(J))}],\n");
		sb.Append("  \"raca\": {\n");
		sb.Append(string.Join(",\n", c.PorRaca.Select(kv => $"    {J(kv.Key)}: [{string.Join(", ", kv.Value.Select(J))}]")));
		sb.Append("\n  },\n  \"classe\": {\n");
		sb.Append(string.Join(",\n", c.PorClasse.Select(kv => $"    {J(kv.Key)}: [{string.Join(", ", kv.Value.Select(J))}]")));
		sb.Append("\n  }\n}\n");
		Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
		File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(false));
	}

	// =====================================================================
	// SAIDA
	// =====================================================================
	public static void Escrever(string caminho, Dictionary<string, SkillDef> defs)
	{
		var sb = new StringBuilder();
		sb.Append("[\n");
		bool primeiro = true;

		foreach (SkillDef d in defs.Values.OrderBy(x => x.Path, StringComparer.Ordinal))
		{
			// ARVORE nao e skill: e o galho onde as skills penduram. Sai com a marca propria
			// porque a interface de aprendizado precisa das duas coisas.
			if (d.Nome.Length == 0) continue;   // bloco sem nome: e classe-base, nao skill

			if (!primeiro) sb.Append(",\n");
			primeiro = false;
			sb.Append("  { ");
			sb.Append($"\"path\": {J(d.Path)}, \"nome\": {J(d.Nome)}, \"desc\": {J(d.Desc)}, ");
			sb.Append($"\"tipo\": {J(d.Tipo)}, \"tier\": {d.Tier}, \"custo\": {d.Custo}, ");
			sb.Append($"\"maxnivel\": {d.MaxNivel}, \"folga\": {d.PreReqFolga}, ");
			sb.Append($"\"arvore\": {(d.Arvore ? 1 : 0)}, \"comum\": {(d.Comum ? 1 : 0)}, ");
			sb.Append($"\"ligada\": {(d.Ligada ? 1 : 0)}, \"vilao\": {(d.SoVilao ? 1 : 0)}, ");
			sb.Append($"\"ensinavel\": {(d.Ensinavel ? 1 : 0)}, \"custofixo\": {(d.CustoFixo ? 1 : 0)}, ");
			sb.Append($"\"custoensino\": {d.CustoDeEnsino}, \"esquecivel\": {(d.Esquecivel ? 1 : 0)}, ");
			// SO NAS ARVORES: o tier de nascenca, o teto, e o `growbranches()` traduzido. Folha nao
			// tem tier de vitrine -- tem o proprio `tier`, que e o outro lado da mesma comparacao.
			if (d.Arvore)
			{
				sb.Append($"\"tierinicial\": {d.TierInicial}, \"tiermax\": {d.TierMax}, ");
				sb.Append($"\"regras\": [{string.Join(", ", d.Regras.Select(J))}], ");
			}
			sb.Append($"\"racas\": [{string.Join(", ", d.Racas.Select(J))}], ");
			sb.Append($"\"classes\": [{string.Join(", ", d.Classes.Select(J))}], ");
			sb.Append($"\"prereqs\": [{string.Join(", ", d.PreReqs.Select(J))}], ");
			sb.Append($"\"galhos\": [{string.Join(", ", d.Constituintes.Select(J))}], ");
			// LISTA PLANA, NAO OBJETO ANINHADO ("physoffBuff=0.4"). O leitor do jogo e um parser
			// de uma pagina que fatia o arquivo por `{`..`}` de primeiro nivel; uma chave dentro do
			// bloco quebraria TODA skill dali pra frente, e em silencio. Formato serve leitor.
			sb.Append($"\"buffs\": [{string.Join(", ", d.Buffs.Select(kv =>
				J($"{kv.Key}={kv.Value.ToString("0.####", CultureInfo.InvariantCulture)}")))}], ");
			sb.Append($"\"verbos\": [{string.Join(", ", d.Verbos.Select(J))}], ");
			sb.Append($"\"mults\": [{Pares(d.Mults)}], ");
			sb.Append($"\"genes\": [{Pares(d.Genes)}], ");
			sb.Append($"\"flags\": [{Pares(d.Flags)}], ");
			sb.Append($"\"estilo\": {J(d.Estilo)}");
			// A ESCOLHA UNICA, no MESMO formato plano do resto e pelo mesmo motivo (o leitor do
			// jogo fatia por `{`..`}` de primeiro nivel). Cada casa e uma string
			// "rotulo|campo=valor|campo=valor", com o canal marcado por prefixo:
			// sem prefixo = aditivo, `*` = multiplicativo, `g:` = genetica, `!` = atribuicao,
			// `v:` = verbo. Vazio na esmagadora maioria -- uma skill no jogo inteiro usa isto.
			if (d.Escolhas.Count > 0)
				sb.Append($", \"escolhas\": [{string.Join(", ", d.Escolhas.Select(e => J(Casa(e))))}]");
			// O EFEITO POR RACA, no mesmo formato de casa ("Yardrat|!teleskill=70") -- ver `PorRaca`. So as
			// entradas COM efeito: um `if(savant.Race=="Demon")` que so muda a frase (demon.dm:159) nao e dado.
			List<EscolhaDeSkill> porRaca = d.PorRaca.Where(ComEfeito).ToList();
			if (porRaca.Count > 0)
				sb.Append($", \"porraca\": [{string.Join(", ", porRaca.Select(e => J(Casa(e))))}]");
			// A ESCOLHA HERDADA e o GANHO NA COMPRA, so em quem os tem -- ver os campos no `SkillDef`.
			if (d.EscolhaSegue.Length > 0) sb.Append($", \"escolhasegue\": {J(d.EscolhaSegue)}");
			if (d.Compra.Count > 0) sb.Append($", \"compra\": [{string.Join(", ", d.Compra.Select(J))}]");
			sb.Append(" }");
		}
		sb.Append("\n]\n");

		Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
		File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(false));
	}

	/// <summary>Um dicionario como lista plana de "chave=valor" -- ver o comentario de `buffs`.</summary>
	private static string Pares(Dictionary<string, double> d) => string.Join(", ", d.Select(kv =>
		J($"{kv.Key}={kv.Value.ToString("0.####", CultureInfo.InvariantCulture)}")));

	/// <summary>Uma entrada (casa ou raca) que faz alguma coisa no corpo -- as vazias nao viram dado.</summary>
	public static bool ComEfeito(EscolhaDeSkill e) =>
		e.Buffs.Count + e.Mults.Count + e.Genes.Count + e.Flags.Count + e.Verbos.Count > 0;

	/// <summary>Uma casa da escolha unica numa string so -- ver o comentario de `escolhas`.</summary>
	private static string Casa(EscolhaDeSkill e)
	{
		var p = new List<string> { e.Rotulo };
		string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
		p.AddRange(e.Buffs.Select(kv => $"{kv.Key}={N(kv.Value)}"));
		p.AddRange(e.Mults.Select(kv => $"*{kv.Key}={N(kv.Value)}"));
		p.AddRange(e.Genes.Select(kv => $"g:{kv.Key}={N(kv.Value)}"));
		p.AddRange(e.Flags.Select(kv => $"!{kv.Key}={N(kv.Value)}"));
		p.AddRange(e.Verbos.Select(v => $"v:{v}"));
		return string.Join("|", p);
	}

	private static string J(string s) =>
		"\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "") + "\"";
}
