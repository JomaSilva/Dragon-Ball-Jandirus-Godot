namespace Jandirus.Core.Social;

/// <summary>
/// QUE RELACAO EU DECLAREI TER COM ALGUEM -- o verb `Relation()` do DM (`Contacts.dm:115-151`).
///
/// ============================ POR QUE OS NUMEROS SAO EXPLICITOS ============================
/// Isto vai pro disco (o `Convivio` inteiro e serializado no save do personagem). Renumerar uma
/// entrada nao quebra compilacao nenhuma e transforma silenciosamente o "Amor" de todo mundo em
/// "Odio" na proxima carga. Numeros cravados, e nunca reordenar.
/// ==========================================================================================
///
/// A ORDEM NAO E ESCALA: `Ruim` nao e "menos que `Bom`". Quem pergunta pergunta por conjunto
/// (ver <see cref="Convivio.TemRelacao"/>), exatamente como o DM (`check_relation(M, list(...))`).
/// </summary>
public enum Relacao : byte
{
	/// <summary>Nunca declarei nada. E o padrao, e e o que RECUSA -- ver `Convivio.TemRelacao`.</summary>
	Nenhuma = 0,
	Neutro = 1,
	RivalRuim = 2,
	RivalBom = 3,
	Ruim = 4,
	Bom = 5,
	MuitoRuim = 6,
	MuitoBom = 7,
	Odio = 8,
	Amor = 9,
}

/// <summary>
/// UMA PESSOA QUE EU CONHECO -- o `/obj/Contact` do DM (`Contacts.dm:94-104`).
///
/// ============================ E UMA FOTOGRAFIA, NAO UM ESPELHO ============================
/// O `update_my_contact()` do DM copia raca, classe, genero, idade e ate os overlays do corpo pro
/// Contact e para por ai -- e a aba People escreve, literalmente, *"como visto da ultima vez"*
/// (`HtmlUI.dm:557`). Nao e economia: uma lista que se atualiza sozinha diria a raca ATUAL de quem
/// se transformou, o cargo ATUAL de quem foi promovido e o nome ATUAL de quem trocou de corpo --
/// ou seja, saber quem alguem e sem estar perto dele. A memoria e velha de proposito.
///
/// O RETRATO (os `overlays` achatados num icone) NAO FOI PORTADO. La ele existe porque o BYOND
/// sabe achatar icone + overlays num `/icon` e mandar por `browse_rsc`; aqui a aparencia e um
/// `Appearance` que o cliente monta em camadas, e "a foto de como ele estava" seria um segundo
/// caminho de vestir alguem -- o mesmo tipo de duplicata que este port ja pagou caro (ver o
/// `AoReceberAparencia` x `VestirCorpoInteiro`). Ficaram os campos de TEXTO da ficha, que sao o que a
/// aba realmente le.
/// ==========================================================================================
/// </summary>
public sealed class Conhecido
{
	public string Assinatura = "";

	/// <summary>`"[name] ([displaykey])"` no DM (`Contacts.dm:10`). Aqui: o nome do personagem.</summary>
	public string Nome = "";

	// o `c_race`/`c_class`/`c_gender`/`c_age` do Contact -- "?" e o valor de fabrica de la
	public string Raca = "?";
	public string Classe = "?";
	public string Genero = "?";
	public int Idade;

	/// <summary>
	/// QUANTO EU CONVIVI COM ESTA PESSOA -- o `familiarity` do Contact.
	///
	/// NAO E AMIZADE, e as duas nem crescem pela mesma coisa: amizade cresce por ESTAR PERTO
	/// (`accrue_friendship`, proximidade), familiaridade cresce por CONVERSAR (`TestListeners`,
	/// `Talking.dm:218-219`, com `prob(1)` por mensagem ouvida, nos dois sentidos). E a
	/// familiaridade que abre as declaracoes de relacao e que o resto do jogo consulta pra saber
	/// se voce sabe o nome de alguem (`Sense2.0.dm:99`, `HtmlUI.dm:374`) ou consegue se teleportar
	/// ate ele (`yardrat.dm:178`, *"easier to teleport to people you know better"*).
	/// </summary>
	public int Familiaridade;

	/// <summary>O que EU declarei sentir por esta pessoa. Ver <see cref="Relacao"/>.</summary>
	public Relacao Relacao = Relacao.Nenhuma;

	/// <summary>Quando a fotografia foi tirada (relogio real, ms) -- o `last_snap`.</summary>
	public long VistoEm;
}

/// <summary>
/// ============================ O SUBSTRATO SOCIAL, PORTADO DO DM ============================
/// Duas listas, dois arquivos la, e a separacao entre elas e DELIBERADA:
///
///   * `Contacts.dm`   -- `known_contact_list`: quem eu conheco, com a foto da ficha e a relacao
///     que eu declarei. Entra-se nela por proximidade, por conversa ou pelo verb `Set_Contact`.
///   * `Friendship.dm` -- `friendship`: quantos pontos de amizade eu tenho com cada um.
///
/// No DM **CONHECIDO NAO E AMIGO, E ISSO ESTA CRAVADO NUM NUMERO**: o teto da proximidade e o
/// `ACQUAINTANCE_CAP = 49` (`Friendship.dm:23`) e a exigencia de amigo e
/// <see cref="ExigenciaDeAmigo"/> = 50 (`:21`) -- **um ponto acima**. Conviver ao lado de alguem a
/// vida inteira te deixava `Familiar` e nunca `Friend`: la amizade exige um PEDIDO ACEITO
/// (`friend_request_resolve`, `:122-124`, que faz `max(..., FRIEND_REQ)` nas duas pontas).
/// ==========================================================================================
///
/// ============================ A DIVERGENCIA DECLARADA: AQUI E CONVIVIO, NAO CONVITE ============================
/// **A PEDIDO DO DONO**, e com as palavras dele: *"no byond tinha q CONVIDAR alguem pra ser seu
/// amigo. AQUI PODERIA SER SOMENTE alguem q ficou BASTANTE TEMPO COM VC ate virar amigo"*.
///
/// Entao o `ACQUAINTANCE_CAP` **nao foi portado**: a proximidade atravessa o 50 sozinha e vai ate o
/// teto de 200. Amizade aqui e um RELOGIO -- <see cref="MinutosParaVirarAmigo"/> minutos ao lado da
/// mesma pessoa. O pedido continua existindo, e ganhou um segundo emprego (ver
/// <see cref="AceitarAmizade"/>): ele agora e o ATALHO e a RECONCILIACAO, nao mais a unica porta.
///
///   * O QUE SE GANHA: o vinculo passa a existir sem que ninguem precise saber que ha um verb pra
///     isso -- e como todo o resto do jogo (Zenkai, maestria, gravidade) ja mede tempo e nao gesto,
///     a amizade deixa de ser o unico sistema que exige um clique. E, sobretudo, ela ganha PESO PRA
///     PERDER: so faz sentido cobrar de quem mata (ver <see cref="Afastar"/>) se o laco tiver custado
///     tempo. Um laco de um clique nao doi quando quebra.
///   * O QUE SE PERDE: a amizade deixa de ser uma ESCOLHA. Quem treina 25 minutos ao lado de alguem
///     que despreza vira amigo dele do mesmo jeito, e -- o que custa caro -- **passa a poder entrar
///     em furia por ele** (o `LutoPorMorte` le exatamente este numero, e ele e a porta do SSJ1). No
///     DM isso era impossivel de proposito. As duas defesas que sobraram sao: rival declarado nao
///     rende amizade nenhuma (<see cref="Aproximar"/>), e quem te mata desce pro NEGATIVO e vira
///     inimigo sozinho, sem volta por convivencia.
/// ================================================================================================================
///
/// ============================ POR QUE ISTO IMPORTA PRO SSJ1 ============================
/// A porta do tronco Saiyajin nao e amizade: e RAIVA. Mas quem decide se a raiva acende sao estas
/// duas listas, com um `||` entre elas -- `KO.dm:36` e `Death.dm:79`. Ver
/// <see cref="LutoPorMorte"/> e <see cref="LutoPorQueda"/>, que sao esses dois `if` portados.
/// ======================================================================================
///
/// ============================ DOIS DESVIOS CONSCIENTES DO ORIGINAL ============================
/// Os dois sao a MESMA raiz: no DM o `/obj/Contact` e um objeto GLOBAL, um por pessoa no mundo
/// inteiro (`var/list/contact_list`, `Contacts.dm:92`), e `known_contact_list` guarda uma
/// REFERENCIA pra ele. Ou seja `familiarity` e `relation` nao sao meus -- sao do objeto que todo
/// mundo compartilha. Disso saem duas coisas quebradas la:
///
///   1. **`familiarity` e um contador global.** `add_familiarity(M)` (`Contacts.dm:87`) escreve
///      `O.familiarity[M.signature]` no Contact de M -- o mesmo balde pra todo mundo. Duas pessoas
///      que nunca se viram compartilham a familiaridade que uma terceira acumulou.
///   2. **`relation` NUNCA e lida onde e escrita.** O verb escreve `relation[usr.signature]`
///      (`Contacts.dm:119`, chave = quem declarou) e o `check_relation` le
///      `relation[M.signature]` (`:74`, chave = de quem e o Contact). As chaves so coincidem se
///      voce declarar relacao consigo mesmo. Consequencia real: hoje, no DM, `check_relation`
///      responde FALSE sempre, e o gate de familiaridade do proprio verb (`familiarity[usr.sig]`,
///      `:121`) tambem nunca passa -- so "Neutral" da pra escolher.
///
/// Aqui as duas listas moram DENTRO do personagem que as tem, entao elas sao naturalmente por
/// observador e as chaves nao tem como divergir. Portar a troca de chaves seria portar um typo:
/// os chamadores do DM (`check_relation(M, list("Good",...))`, a pergunta do verb *"What's your
/// relation?"*) dizem, sem ambiguidade, que a intencao e "o que EU sinto por ELE".
/// ==============================================================================================
/// </summary>
public sealed class Convivio
{
	// =====================================================================
	// OS NUMEROS DO `Friendship.dm` (linhas 19-27) -- todos, com o nome de la ao lado
	// =====================================================================

	/// <summary>`FRIEND_RANGE = 6`: a quantos TILES dois jogadores estreitam lacos por estar perto.</summary>
	public const int AlcanceDeConvivioTiles = 6;

	/// <summary>`FRIEND_RATE = 0.1`: quanto de amizade cada passo de proximidade rende.</summary>
	public const double TaxaDeAmizade = 0.1;

	/// <summary>`FRIEND_REQ = 50`: a partir daqui a pessoa E amiga, e e isso que a raiva le.</summary>
	public const double ExigenciaDeAmigo = 50;

	/// <summary>O teto de tudo (`min(cur + FRIEND_RATE, 200)`, `:39`) -- e o degrau `Bonded`.</summary>
	public const double TetoDeAmizade = 200;

	/// <summary>
	/// ============================ O PISO, QUE NO DM NAO EXISTE ============================
	/// *"podendo ficar NO NEGATIVO e se tornando INIMIGOS"*. E o espelho exato do
	/// <see cref="TetoDeAmizade"/> e do <see cref="TetoDeInimizade"/>: a escala vai de -200 a +200 e
	/// o zero e o ponto em que o afeto vira o contrario dele.
	///
	/// **NO DM A AMIZADE NUNCA DIMINUI** -- a varredura por decremento de `friendship[...]` em
	/// `Code/` volta vazia. La, matar um amigo de alguem custa ODIO (`friend_harmed_by`), e so contra
	/// quem ja tinha sido declarado rival. Perda, negativo e inimizade automatica sao invencao do
	/// dono, nao porte.
	/// =====================================================================================
	/// </summary>
	public const double PisoDeAmizade = -TetoDeAmizade;

	/// <summary>
	/// QUANTO TEMPO JUNTO CUSTA VIRAR AMIGO -- a resposta em minutos, derivada e nunca digitada.
	///
	/// 50 pontos / 0,1 por passo = 500 passos de 3 s = 1500 s = **25 minutos** de convivio a menos de
	/// 6 tiles. E ela e uma CONTA e nao um numero escrito: quem mexer na taxa, no limiar ou na
	/// cadencia ve o minuto mudar junto, em vez de deixar um comentario mentindo no arquivo.
	/// (Ate o teto de 200 -- o degrau `Ligado` -- sao 100 minutos.)
	/// </summary>
	public static double MinutosParaVirarAmigo =>
		ExigenciaDeAmigo / TaxaDeAmizade * SegundosEntreAproximacoes / 60.0;

	/// <summary>
	/// `FRIEND_THROTTLE = 10` voltas do `GlobalStats`, que dorme `sleep(3)` = 0,3 s
	/// (`Stats.dm:66`). Dez voltas de 0,3 s = **3 segundos** entre um passo de aproximacao e o
	/// seguinte. Guardado em segundos e nao em "voltas" porque o tique deste port nao e o de la --
	/// copiar o numero 10 daria uma cadencia diferente calada.
	/// </summary>
	public const double SegundosEntreAproximacoes = 3;

	/// <summary>`ENMITY_HIT = 1`: cada golpe de um rival declarado.</summary>
	public const double InimizadePorGolpe = 1;

	/// <summary>`ENMITY_FRIEND_KO = 25`: um rival declarado nocauteou um amigo meu.</summary>
	public const double InimizadePorAmigoCaido = 25;

	/// <summary>`ENMITY_FRIEND_KILL = 60`: um rival declarado MATOU um amigo meu.</summary>
	public const double InimizadePorAmigoMorto = 60;

	/// <summary>`ENMITY_MAX = 200`: o teto, espelhando o `Bonded` da amizade.</summary>
	public const double TetoDeInimizade = 200;

	/// <summary>
	/// ============================ O PRECO DE MATAR -- **PEDIDO DO DONO, NAO PORTE** ============================
	/// *"pessoas q MATAM SEU AMIGO ou TE MATAM PERDEM PONTOS de amizade"*.
	///
	/// OS DOIS NUMEROS SAO OS MESMOS DO ODIO, e sao os mesmos DE PROPOSITO: matar o amigo de alguem ja
	/// vale `ENMITY_FRIEND_KILL = 60` de odio no DM, e derruba-lo vale `ENMITY_FRIEND_KO = 25`. E o
	/// MESMO evento, contado nos dois livros -- o quanto ele soma de odio e o quanto ele tira de
	/// afeto. Um par proprio aqui seria uma segunda tabela pra manter, que um dia discorda desta.
	///
	/// A CONTA QUE ELES DESENHAM: quem nunca te viu na vida (0 pontos) e te mata cai pra **-60** e
	/// vira inimigo no mesmo golpe; um amigo recem-feito (50, os 25 minutos) que te mata cai pra -10 e
	/// **tambem** vira inimigo; e quem construiu um vinculo `Ligado` (200, uma hora e quarenta) pode
	/// errar tres vezes antes de virar. E o que o numero quer dizer: tempo junto e credito de perdao.
	/// ==========================================================================================================
	/// </summary>
	public const double PerdaPorMorte = InimizadePorAmigoMorto;

	/// <inheritdoc cref="PerdaPorMorte"/>
	public const double PerdaPorNocaute = InimizadePorAmigoCaido;

	/// <summary>
	/// De quanto em quanto tempo a fotografia da ficha de alguem e refeita.
	///
	/// `Contacts.dm:26` -- `if(world.time &lt; A.last_snap + 600) return`, e 600 decimos = 1 minuto.
	/// Existe porque quem chama e a proximidade, que roda a cada 3 s: sem isto, ficar perto de
	/// alguem refotografaria a ficha dele vinte vezes por minuto.
	/// </summary>
	public const long IntervaloDoRetratoMs = 60_000;

	/// <summary>
	/// ============================ CONVIVIO NA PORRADA -- PEDIDO DO DONO (2026-09-05), NAO PORTE ============================
	/// No DM a familiaridade so sobe por FALA ouvida (`add_familiarity`, 1% por linha). O dono: *"fiz um
	/// personagem apanhar muito, fazendo ele ficar com o menor nivel possivel de amizade (inimigo mortal),
	/// mas ja que eu fiz isso em pouco tempo de jogo o convivio ainda era 0 e eu so conseguia declarar ele
	/// como neutro ... ficou esquisito e confuso"*. Apanhar de alguem E conhece-lo: cada golpe que encosta
	/// tem 10% de render 1 ponto (uma luta de 100 golpes ~ 10, o que cinco mil falas dariam), cair vale 10 e
	/// morrer vale 20 -- nos DOIS lados. Oito nocautes = 80: da pra declarar 'odio' (50) a quem o jogo ja
	/// chama de inimigo mortal. Os 200 do 'amor' continuam sendo tempo.
	/// ==================================================================================================================
	/// </summary>
	public const double ChanceDeFamiliaridadePorGolpe = 0.10;
	/// <inheritdoc cref="ChanceDeFamiliaridadePorGolpe"/>
	public const int FamiliaridadePorQueda = 10;
	/// <inheritdoc cref="ChanceDeFamiliaridadePorGolpe"/>
	public const int FamiliaridadePorMorte = 20;

	// =====================================================================
	// O ESTADO -- campos publicos e mutaveis porque isto vai INTEIRO pro save
	// (System.Text.Json com `IncludeFields`; ver `CharacterSave`). Nada de
	// `readonly` nem de propriedade so-leitura aqui: os dois voltam VAZIOS do
	// disco, em silencio, e o personagem perde quem ele conhecia.
	// =====================================================================

	/// <summary>`known_contact_list`: assinatura -> a foto de quem eu conheco.</summary>
	public Dictionary<string, Conhecido> Conhecidos = [];

	/// <summary>`friendship`: assinatura -> pontos acumulados.</summary>
	public Dictionary<string, double> Amizade = [];

	/// <summary>`enmity`: assinatura -> odio acumulado. So cresce contra RIVAL DECLARADO.</summary>
	public Dictionary<string, double> Inimizade = [];

	/// <summary>
	/// `rivals`: quem eu declarei inimigo. Lista e nao conjunto pra o JSON ir e voltar sem
	/// surpresa; as insercoes passam por <see cref="AlternarRival"/>, que nao duplica.
	/// </summary>
	public List<string> Rivais = [];

	// =====================================================================
	// PERGUNTAS
	// =====================================================================
	public double PontosDeAmizade(string sig) =>
		sig.Length > 0 && Amizade.TryGetValue(sig, out double p) ? p : 0;

	public double PontosDeInimizade(string sig) =>
		sig.Length > 0 && Inimizade.TryGetValue(sig, out double p) ? p : 0;

	/// <summary>`is_friend()` (`Friendship.dm:46-48`).</summary>
	public bool EhAmigo(string sig) => PontosDeAmizade(sig) >= ExigenciaDeAmigo;

	/// <summary>
	/// A OUTRA PONTA, QUE NO DM NAO EXISTE: amizade abaixo de zero. Nao ha limiar nenhum aqui de
	/// proposito -- **o zero E o limiar**, e e o mesmo zero do "nunca te vi": quem nunca te viu esta
	/// exatamente na fronteira, e um unico golpe mortal decide de que lado ele fica.
	///
	/// Ela e o gatilho de tudo que "ser inimigo" faz: quem cruza pra ca vira RIVAL DECLARADO sozinho
	/// (ver <see cref="Afastar"/>), e a partir dai o eixo que ja existia -- <see cref="Rivais"/> e
	/// <see cref="Inimizade"/> -- passa a valer sem uma linha nova.
	/// </summary>
	public bool EhInimigo(string sig) => PontosDeAmizade(sig) < 0;

	public bool EhRival(string sig) => sig.Length > 0 && Rivais.Contains(sig);

	public bool Conhece(string sig) => sig.Length > 0 && Conhecidos.ContainsKey(sig);

	public Conhecido? Ficha(string sig) =>
		sig.Length > 0 && Conhecidos.TryGetValue(sig, out Conhecido? c) ? c : null;

	/// <summary>`check_familiarity()` (`Contacts.dm:76-80`). Zero pra quem eu nao conheco.</summary>
	public int Familiaridade(string sig) => Ficha(sig)?.Familiaridade ?? 0;

	/// <summary>
	/// O QUE EU DECLAREI SENTIR por esta pessoa. <see cref="Relacao.Nenhuma"/> pra quem eu nao
	/// conheco -- e pra quem eu conheco e nunca declarei nada, que sao a mesma resposta.
	/// </summary>
	public Relacao RelacaoCom(string sig) => Ficha(sig)?.Relacao ?? Relacao.Nenhuma;

	/// <summary>
	/// `check_relation(M, list(...))` (`Contacts.dm:70-74`) -- a relacao que eu declarei esta
	/// nesta lista?
	///
	/// <see cref="Relacao.Nenhuma"/> nao casa com nada de proposito: ela e o "nao declarei", e o
	/// padrao do enum precisa ser o lado que RECUSA. Um `Nenhuma` que casasse com a lista da morte
	/// faria toda a zona entrar em luto por um desconhecido.
	/// </summary>
	public bool TemRelacao(string sig, params Relacao[] quais)
	{
		Relacao r = RelacaoCom(sig);
		return r != Relacao.Nenhuma && Array.IndexOf(quais, r) >= 0;
	}

	// =====================================================================
	// AS TRES PERGUNTAS DA RAIVA -- os `if` de `Death.dm` e `KO.dm`, portados
	// =====================================================================

	/// <summary>
	/// ESTA PESSOA MORRENDO NA MINHA FRENTE ME ENFURECE?
	///
	/// `Death.dm:79` -- `M.check_relation(src, list("Good","Very Good","Love","Rival/Good")) ||
	/// M.is_friend(src)`. A lista da MORTE e mais larga que a da queda: ver morrer alguem de quem
	/// voce gostava basta, e ate o rival que voce respeitava (`Rival/Good`) conta.
	/// </summary>
	public bool LutoPorMorte(string sig) =>
		EhAmigo(sig)
		|| TemRelacao(sig, Relacao.Bom, Relacao.MuitoBom, Relacao.Amor, Relacao.RivalBom);

	/// <summary>
	/// ESTA PESSOA CAINDO NA MINHA FRENTE ME ENFURECE?
	///
	/// `KO.dm:36` -- `M.check_relation(src, list("Very Good","Love")) || M.is_friend(src)`. So os
	/// dois graus MAIS ALTOS de afeto, e essa estreita e a regra: um nocaute e menos que uma morte,
	/// entao ele so move quem era muito proximo.
	///
	/// (Ficou de fora o `StoredAnger += 20` da linha seguinte, `KO.dm:39`, pra quem tinha
	/// `Good`/`Rival/Good`: ele alimenta o BUFF de raiva, que e outro sistema e continua nao
	/// portado -- ver o cabecalho de `GameServer.AmigoAbatido`.)
	/// </summary>
	public bool LutoPorQueda(string sig) =>
		EhAmigo(sig) || TemRelacao(sig, Relacao.MuitoBom, Relacao.Amor);

	/// <summary>
	/// QUEM ME DERRUBOU ERA INIMIGO? -- perguntado a VITIMA sobre o algoz.
	///
	/// `Death.dm:75` e `KO.dm:34`, a mesma linha nos dois: `!is_friend(foe) &amp;&amp;
	/// !check_relation(foe, list("Very Good","Love","Good","Rival/Good"))`.
	///
	/// **E ela que impede o treino de virar fabrica de Super Saiyajin.** Sem esta pergunta, dois
	/// amigos sparrando enfureceriam a plateia a cada nocaute -- e o DM diz isso em voz alta no
	/// comentario de `Death.dm:75`: *"no rage for friendly duels or environmental death"*.
	/// </summary>
	public bool AlgozEhInimigo(string sig) =>
		!EhAmigo(sig)
		&& !TemRelacao(sig, Relacao.MuitoBom, Relacao.Amor, Relacao.Bom, Relacao.RivalBom);

	// =====================================================================
	// ROTULOS
	// =====================================================================
	/// <summary>
	/// `acquaintance_label()` (`Friendship.dm:51-57`). E o que a aba People escreve.
	///
	/// OS TRES DEGRAUS DE BAIXO SAO NOVOS -- no DM nao ha faixa negativa porque nao ha como descer.
	/// Eles espelham os de cima na mesma escala (5 / 50 / 200), pra que o jogador leia a queda com a
	/// mesma regua com que leu a subida: `Desafeto` e o espelho de `Conhecido`, `Inimigo` e o espelho
	/// exato de `Amigo` (e o -50 e o mesmo modulo do +50), e `Inimigo mortal` o de `Ligado`.
	/// </summary>
	public static string RotuloDeProximidade(double pts) => pts switch
	{
		>= TetoDeAmizade => "Ligado",              // "Bonded"
		>= ExigenciaDeAmigo => "Amigo",            // "Friend"
		>= 20 => "Familiar",                       // "Familiar"
		>= 5 => "Conhecido",                       // "Acquaintance"
		>= 0 => "Mal conhecido",                   // "Barely Known"
		> -ExigenciaDeAmigo => "Desafeto",
		> PisoDeAmizade => "Inimigo",
		_ => "Inimigo mortal",
	};

	/// <summary>`enmity_label()` (`Friendship.dm:64-70`). Vazio quando nao ha odio nenhum.</summary>
	public static string RotuloDeInimizade(double pts) => pts switch
	{
		>= 150 => "Nemesis",
		>= 75 => "Rival odiado",
		>= 25 => "Rival",
		>= 5 => "Antipatizado",
		_ => "",
	};

	/// <summary>
	/// QUANTA FAMILIARIDADE CADA DECLARACAO PEDE -- os numeros do `switch` do verb `Relation()`
	/// (`Contacts.dm:117-151`), que a propria pergunta do DM lista pro jogador.
	///
	/// `Neutro` pede zero: e a saida, o "esquece o que eu disse".
	/// </summary>
	public static int FamiliaridadeExigida(Relacao r) => r switch
	{
		Relacao.Neutro => 0,
		Relacao.Ruim => 10,
		Relacao.RivalBom or Relacao.RivalRuim => 15,
		Relacao.MuitoRuim => 20,
		Relacao.Bom or Relacao.Odio => 50,
		Relacao.MuitoBom => 100,
		Relacao.Amor => 200,
		_ => int.MaxValue,   // `Nenhuma` nao se declara: ela e a ausencia de declaracao
	};

	/// <summary>O nome que o jogador digita/ve. Casa sem diferenciar maiuscula nem acento simples.</summary>
	public static Relacao RelacaoPorNome(string nome) => nome.Trim().ToLowerInvariant() switch
	{
		"neutro" or "neutral" => Relacao.Neutro,
		"bom" or "good" => Relacao.Bom,
		"muito bom" or "very good" => Relacao.MuitoBom,
		"amor" or "love" => Relacao.Amor,
		"rival bom" or "rival/good" or "rival good" => Relacao.RivalBom,
		"rival ruim" or "rival/bad" or "rival bad" => Relacao.RivalRuim,
		"ruim" or "bad" => Relacao.Ruim,
		"muito ruim" or "very bad" => Relacao.MuitoRuim,
		"odio" or "ódio" or "hate" => Relacao.Odio,
		_ => Relacao.Nenhuma,
	};

	public static string NomeDaRelacao(Relacao r) => r switch
	{
		Relacao.Neutro => "neutro",
		Relacao.Bom => "bom",
		Relacao.MuitoBom => "muito bom",
		Relacao.Amor => "amor",
		Relacao.RivalBom => "rival bom",
		Relacao.RivalRuim => "rival ruim",
		Relacao.Ruim => "ruim",
		Relacao.MuitoRuim => "muito ruim",
		Relacao.Odio => "ódio",
		_ => "",
	};

	/// <summary>As declaracoes possiveis, na ordem em que o DM as lista no `input()`.</summary>
	public static readonly Relacao[] Declaraveis =
	[
		Relacao.Amor, Relacao.MuitoBom, Relacao.Bom, Relacao.RivalBom,
		Relacao.RivalRuim, Relacao.Neutro, Relacao.Ruim, Relacao.MuitoRuim, Relacao.Odio,
	];

	// =====================================================================
	// MUDANCAS
	// =====================================================================

	/// <summary>
	/// FOTOGRAFA A FICHA DE ALGUEM E ME FAZ CONHECE-LO -- o `update_my_contact()` mais o
	/// `known_contact_list[sig] = ...` das duas pontas (`Friendship.dm:42-44`, `Contacts.dm:86`).
	///
	/// A familiaridade e a relacao SOBREVIVEM a refotografada: elas sao minhas, a foto e dele.
	/// Sobrescrever tudo aqui era o jeito obvio de zerar a familiaridade de alguem sempre que ele
	/// passasse na frente.
	///
	/// Devolve TRUE quando esta pessoa acabou de entrar na lista (primeiro encontro) -- e o que o
	/// servidor usa pra dizer "voce vai lembrar desta pessoa" uma unica vez.
	/// </summary>
	public bool Fotografar(string sig, string nome, string raca, string classe,
						   string genero, int idade, long agoraMs)
	{
		if (sig.Length == 0) return false;

		if (!Conhecidos.TryGetValue(sig, out Conhecido? c))
		{
			Conhecidos[sig] = new Conhecido
			{
				Assinatura = sig, Nome = nome, Raca = raca, Classe = classe,
				Genero = genero, Idade = idade, VistoEm = agoraMs,
			};
			return true;
		}

		// o throttle de 1x/min do original: quem chama e a proximidade, a cada 3 s
		if (agoraMs < c.VistoEm + IntervaloDoRetratoMs) return false;

		c.Nome = nome;
		c.Raca = raca;
		c.Classe = classe;
		c.Genero = genero;
		c.Idade = idade;
		c.VistoEm = agoraMs;
		return false;
	}

	/// <summary>
	/// UM PASSO DE PROXIMIDADE -- o corpo do `accrue_friendship()` (`Friendship.dm:37-41`), **sem o
	/// teto de 49**: aqui a convivencia sobe direto de 0 a 200 e cruza o 50 sozinha, que e o pedido do
	/// dono (ver o cabecalho da classe). Devolve TRUE **no passo exato em que a pessoa virou amiga**,
	/// pra que o servidor possa dizer isso uma unica vez.
	///
	/// ============================ DUAS PORTAS FECHADAS, E AS DUAS IMPORTAM ============================
	///   * RIVAL DECLARADO nao rende amizade nenhuma (`:36`, e agora e a defesa principal: sem o teto
	///     de 49, ela e o unico jeito de nao virar amigo de quem voce nao quer);
	///   * QUEM ESTA NO NEGATIVO tambem nao sobe -- e isso e consequencia da linha acima e nao uma
	///     segunda regra: cair abaixo de zero DECLARA a rivalidade (<see cref="Afastar"/>). Ficar perto
	///     de quem te matou nao te faz perdoa-lo; **desfazer a rivalidade e um gesto** (o verb `rival`,
	///     ou um pedido de amizade aceito), e so depois dele o tempo volta a contar.
	/// ==================================================================================================
	///
	/// ============================ POR QUE O `Math.Round` ============================
	/// 0,1 nao existe em binario: somar 0,1 quinhentas vezes da 49,99999999999995, e o limiar de 50
	/// cairia **um passo depois** do que este arquivo promete -- calado, e com a bancada reprovando por
	/// um erro que ninguem consegue ver lendo o codigo. Arredondar a cada passo prende o valor na
	/// grade de decimos em que ele foi escrito.
	/// ==============================================================================
	/// </summary>
	public bool Aproximar(string sig)
	{
		if (sig.Length == 0 || EhRival(sig)) return false;

		double antes = PontosDeAmizade(sig);
		if (antes >= TetoDeAmizade) return false;

		double depois = Math.Min(Math.Round(antes + TaxaDeAmizade, 3), TetoDeAmizade);
		Amizade[sig] = depois;
		return antes < ExigenciaDeAmigo && depois >= ExigenciaDeAmigo;
	}

	/// <summary>
	/// ============================ A VIOLENCIA COBRA O LACO -- **PEDIDO DO DONO** ============================
	/// *"pessoas q MATAM SEU AMIGO ou TE MATAM PERDEM PONTOS de amizade, podendo ficar NO NEGATIVO e se
	/// tornando INIMIGOS"*. Nao ha nada disto no DM (ver <see cref="PerdaPorMorte"/>).
	///
	/// **QUEM CRUZA O ZERO PRA BAIXO VIRA RIVAL DECLARADO NA HORA**, e essa unica linha e o que faz
	/// "ser inimigo" significar alguma coisa sem inventar sistema nenhum: a partir dela valem, de
	/// graca, o `Rivais` (que ja bloqueia a amizade por proximidade), o <see cref="Inimizade"/> (que so
	/// cresce contra rival declarado e agora tem uma fonte automatica), o `+1` por golpe dele, o
	/// `+25/+60` quando ele derruba ou mata um amigo seu, e o rotulo RIVAL da aba People.
	///
	/// O ODIO SOBE NA MESMA MEDIDA em que o afeto desce -- e o mesmo evento nos dois livros. Como o
	/// `SomarInimizade` exige rival declarado, a ordem aqui e obrigatoria: declara primeiro, soma
	/// depois. Invertida, o primeiro (e maior) golpe de odio seria descartado calado.
	///
	/// Devolve TRUE **so no golpe que atravessou o zero** -- o resto e afundar mais, e o servidor nao
	/// anuncia duas vezes que alguem virou seu inimigo.
	/// ==========================================================================================================
	/// </summary>
	public bool Afastar(string sig, double quanto)
	{
		if (sig.Length == 0 || quanto <= 0) return false;

		double antes = PontosDeAmizade(sig);
		double depois = Math.Max(Math.Round(antes - quanto, 3), PisoDeAmizade);
		Amizade[sig] = depois;

		if (depois >= 0) return false;

		TornarRival(sig);
		SomarInimizade(sig, quanto);

		// ============================ E A DECLARACAO DE AFETO CAI JUNTO ============================
		// **ESTA LINHA FOI ACHADA PELA BANCADA AO VIVO, e ela nao e cosmetica.** A relacao declarada
		// (`Relation()`) e o OUTRO eixo que responde as tres perguntas da raiva -- `LutoPorMorte` e
		// `AlgozEhInimigo` leem `is_friend() || check_relation(...)`. Sem derrubar a declaracao, quem
		// tivesse dito "muito bom" pro proprio assassino ficaria com a amizade em -60 e a relacao
		// intacta, e o resultado seria absurdo nos dois sentidos: **ele entraria em furia pela morte do
		// homem que o matou**, e -- pior -- o `AlgozEhInimigo` continuaria respondendo "nao e inimigo",
		// entao da PROXIMA vez que esse sujeito o matasse, ninguem na plateia se enfureceria.
		//
		// SO AS DECLARACOES DE AFETO CAEM. `Ruim`, `MuitoRuim`, `Odio` e `RivalRuim` concordam com o
		// estado novo -- apaga-las seria apagar o que a pessoa disse justamente quando ela se provou
		// certa. E a queda e pra `Nenhuma` (o "nao declarei") e nao pra `Neutro`: o jogo nao pode
		// declarar coisas na boca do jogador; o que ele perde e o afeto, nao o direito de falar.
		// ==========================================================================================
		if (Ficha(sig) is { } c
			&& TemRelacao(sig, Relacao.Bom, Relacao.MuitoBom, Relacao.Amor, Relacao.RivalBom))
			c.Relacao = Relacao.Nenhuma;

		return antes >= 0;
	}

	/// <summary>
	/// `add_familiarity()` (`Contacts.dm:82-89`), chamado de `Talking.dm:218` com `prob(1)`: e a
	/// CONVERSA que aproxima de verdade, e ela conta nos dois sentidos.
	///
	/// Exige ja conhecer a pessoa -- no DM tambem (`if(isnull(contact_list[...])) return FALSE`),
	/// e o servidor fotografa antes de somar.
	/// </summary>
	public void SomarFamiliaridade(string sig, int quanto = 1)
	{
		if (quanto > 0 && Conhecidos.TryGetValue(sig, out Conhecido? c)) c.Familiaridade += quanto;
	}

	/// <summary>
	/// O PEDIDO DE AMIZADE ACEITO -- `friend_request_resolve` (`Friendship.dm:122`).
	///
	/// `Math.Max` e nao `=`: quem ja era `Bonded` (200) nao pode ser rebaixado pra 50 por aceitar
	/// um pedido -- e o mesmo `max(...)` do original.
	///
	/// ============================ O SEGUNDO EMPREGO DELE: A RECONCILIACAO ============================
	/// Com a convivencia atravessando o 50 sozinha (ver o cabecalho da classe), o pedido deixou de ser
	/// a UNICA porta da amizade -- e passou a ser a unica porta de VOLTA. Quem esta no negativo e rival
	/// declarado, e rival declarado nao rende amizade por proximidade: **sem isto, um inimigo seria
	/// inimigo pra sempre**, e o dono nao pediu uma condenacao perpetua, pediu uma consequencia.
	///
	/// Por isso o `max` atravessa o negativo (de -60 pra 50 num gesto) e a rivalidade cai junto: ser
	/// amigo e rival ao mesmo tempo e um estado que nenhuma pergunta deste arquivo sabe responder --
	/// `Aproximar` recusaria crescer a amizade que acabou de nascer. O ODIO ACUMULADO FICA, como no
	/// `Declare_Rival` do DM: fazer as pazes nao apaga o que aconteceu.
	///
	/// E ELE E CARO DE PROPOSITO NO OUTRO SENTIDO: precisa dos DOIS lados (um pede, o outro aceita), e
	/// e por isso que um gesto pode desfazer 25 minutos de nada. Perdoar sozinho nao existe.
	/// ==================================================================================================
	/// </summary>
	public void AceitarAmizade(string sig)
	{
		if (sig.Length == 0) return;
		Amizade[sig] = Math.Max(PontosDeAmizade(sig), ExigenciaDeAmigo);
		Rivais.Remove(sig);
	}

	/// <summary>
	/// DECLARA (ou tira) UM RIVAL -- o verb `Declare_Rival` (`Friendship.dm:157-167`). Devolve se
	/// ele PASSOU a ser rival.
	/// </summary>
	public bool AlternarRival(string sig)
	{
		if (sig.Length == 0) return false;
		if (Rivais.Remove(sig)) return false;
		return TornarRival(sig);
	}

	/// <summary>
	/// DECLARA UM RIVAL SEM ALTERNAR -- e a metade do <see cref="AlternarRival"/> que o
	/// <see cref="Afastar"/> precisa.
	///
	/// **ELA EXISTE POR CAUSA DE UM BUG QUE O ALTERNAR TERIA CAUSADO**: cair pro negativo chamando o
	/// verb de alternancia TIRARIA a rivalidade de quem ja tinha declarado o sujeito rival antes --
	/// ou seja, matar o cara que voce ja odiava o promoveria de volta a nao-rival, e o odio pararia de
	/// crescer exatamente contra quem mais merece. Idempotente por isso.
	/// </summary>
	public bool TornarRival(string sig)
	{
		if (sig.Length == 0 || Rivais.Contains(sig)) return false;
		Rivais.Add(sig);
		return true;
	}

	/// <summary>
	/// `add_enmity()` (`Friendship.dm:72-75`). SO CRESCE CONTRA RIVAL DECLARADO -- e essa e a
	/// assimetria que o proprio DM explica (`:59-62`): amizade nasce de estar perto, odio so nasce
	/// de uma escolha.
	/// </summary>
	public void SomarInimizade(string sig, double quanto)
	{
		if (!EhRival(sig)) return;
		Inimizade[sig] = Math.Min(PontosDeInimizade(sig) + quanto, TetoDeInimizade);
	}
}
