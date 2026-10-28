using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Jandirus.Core.Social;

/// <summary>
/// O QUE ESTA PESSOA E PRA MIM -- a DECLARACAO, o verb `Relation()` do DM (`Contacts.dm:115-151`).
///
/// ============================ POR QUE OS NUMEROS SAO EXPLICITOS ============================
/// Isto vai pro disco (o `Convivio` inteiro e serializado no save do personagem). Renumerar uma
/// entrada nao quebra compilacao nenhuma e transforma silenciosamente o "Amor" de todo mundo em
/// "Inimigo mortal" na proxima carga. Numeros cravados, e nunca reordenar.
/// ==========================================================================================
///
/// OS NOMES DO ENUM SAO OS DO DM ("Good", "Very Good", "Hate"...); o que o JOGADOR le sao os de
/// <see cref="Convivio.NomeDaRelacao"/> (amigo, melhor amigo, inimigo mortal...).
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

/// <summary>O que aconteceu com um pedido de declaracao. Ver <see cref="Convivio.Declarar"/>.</summary>
public enum Declaracao : byte
{
	Feita = 0,
	/// <summary>Nao ha ficha dessa pessoa na minha lista.</summary>
	NaoConhece = 1,
	/// <summary>`Relacao.Nenhuma` (ou um nome que nao casa com nada): a ausencia nao se declara.</summary>
	NaoSeDeclara = 2,
	/// <summary>O convivio ainda nao paga esta declaracao (<see cref="Convivio.FamiliaridadeExigida"/>).</summary>
	FaltaConvivio = 3,
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
/// O RETRATO (os `overlays` achatados num icone) mora no CLIENTE (`Client/VistosDeGente.cs`): e a
/// aparencia que os olhos DESTE jogador viram por ultimo. Aqui ficam os campos de TEXTO da ficha.
/// ==========================================================================================
///
/// ============================ A CLASSE NAO ENTRA NA FOTO -- DIVERGENCIA DECLARADA ============================
/// O Contact do DM guarda `c_class` e a aba People de la o ESCREVE (`Statistics.dm:153`, `HtmlUI.dm:557`:
/// *"[c_race] / [c_class]"*). Este port tem uma regra de jogo que diz o contrario -- *"a classe NUNCA aparece,
/// em lugar nenhum"* (`GameServer.Sigilo.cs`, regra 2): cada um so recebe uma PISTA da propria classe, na
/// criacao. Com a classe na lista de conhecidos a regra furava pelos dois lados: eu lia a classe de todo mundo
/// que cruzasse comigo ("Saiyan / Legendary"), e descobria a minha perguntando a qualquer conhecido. Por isso a
/// foto nao a guarda, o fio nao a manda e a aba nao a escreve. (Save antigo traz o campo; ele e ignorado.)
/// ==============================================================================================================
/// </summary>
public sealed class Conhecido
{
	public string Assinatura = "";

	/// <summary>`"[name] ([displaykey])"` no DM (`Contacts.dm:10`). Aqui: o nome do personagem.</summary>
	public string Nome = "";

	// o `c_race`/`c_gender`/`c_age` do Contact -- "?" e o valor de fabrica de la. A raca vai na linha da
	// lista; o genero e a idade so saem do servidor como DETALHES (ver `Convivio.ConvivioParaDetalhes`).
	public string Raca = "?";
	public string Genero = "?";
	public int Idade;

	/// <summary>
	/// O CONVIVIO -- quanto eu ja vivi com esta pessoa. E o `familiarity` do Contact, e e a UNICA medida
	/// que o JOGO escreve sobre nos dois: ele nao conclui nada dela, so conta. Sobe por ficar perto, por
	/// conversar e por lutar (ver as constantes do <see cref="Convivio"/>), nunca desce, e serve pra tres
	/// coisas: paga as declaracoes (<see cref="Convivio.FamiliaridadeExigida"/>), diz se eu sei o nome de
	/// alguem que sinto (`Sense2.0.dm:99`, `HtmlUI.dm:374`) e facilita o teleporte ate ele
	/// (`yardrat.dm:178`, *"easier to teleport to people you know better"*).
	///
	/// O NOME DO CAMPO FICOU O DO DM (e o do save): na tela ele se chama "convivio".
	/// </summary>
	public int Familiaridade;

	/// <summary>
	/// Os passos de proximidade guardados desde o ultimo ponto de convivio (0 a
	/// <see cref="Convivio.PassosPertoPorPonto"/> - 1). Vai pro save pra meio minuto ao lado de alguem
	/// nao se perder num relog. Ver <see cref="Convivio.ConviverPerto"/>.
	/// </summary>
	public int PassosPerto;

	/// <summary>O que EU disse que esta pessoa e pra mim. Ver <see cref="Relacao"/>.</summary>
	public Relacao Relacao = Relacao.Nenhuma;

	/// <summary>Quando a fotografia foi tirada (relogio real, ms) -- o `last_snap`.</summary>
	public long VistoEm;

	/// <summary>
	/// Desde quando eu a conheco (relogio real, ms): a hora da PRIMEIRA fotografia. Zero em ficha de antes
	/// deste campo (2026-10-10) -- "nao sei", e a tela nao escreve a linha.
	/// </summary>
	public long DesdeMs;

	/// <summary>
	/// (2026-10-27, o pedido 124c) O PODER E A CARA DE QUANDO EU O VI -- tirados com a foto (o mesmo prazo de um minuto). A lembranca dele
	/// na MINHA mente nasce com eles (`GameServer.CriarClone`). Zero/nulo em ficha de antes: a lembranca usa os meus.
	/// </summary>
	public double PoderVisto;
	public Jandirus.Core.Appearance.Appearance? AparenciaVista;

	// =====================================================================
	// OS DETALHES -- o que se aprende de alguem CONVIVENDO com ele. So sao escritos depois que o
	// convivio passa do `Convivio.ConvivioParaDetalhes`, e so quando eu estou com a pessoa (sao parte
	// da fotografia: o que eu sei e o que ela era da ultima vez que a vi).
	// =====================================================================

	/// <summary>
	/// A HISTORIA que ela escreveu na criacao (`CharacterDraft.Backstory`), como eu a ouvi por ultimo e
	/// ja limpa (<see cref="Convivio.HistoriaLegivel"/>). Vazia ate o convivio pagar.
	/// </summary>
	public string Historia = "";

	/// <summary>O nome do cargo que ela carregava quando a vi por ultimo ("" = nenhum).</summary>
	public string Titulo = "";

	/// <summary>Quando os detalhes foram ouvidos por ultimo (relogio real, ms). Zero = nunca.</summary>
	public long DetalhesEm;
}

/// <summary>
/// ============================ O SUBSTRATO SOCIAL: CONVIVIO E DECLARACAO ============================
/// Dois arquivos do DM moram aqui (`Contacts.dm` e `Friendship.dm`), e o que sobrou deles sao DUAS
/// coisas, cada uma com um dono so:
///
///   * o **CONVIVIO** (<see cref="Conhecido.Familiaridade"/>) -- quem escreve e o JOGO, e ele so CONTA:
///     tempo perto, conversa, luta. Nao tem rotulo, nao desce e nao conclui nada;
///   * a **DECLARACAO** (<see cref="Conhecido.Relacao"/>) -- quem escreve e o JOGADOR, e so ele: e o que
///     aquela pessoa e PRA ELE. O jogo nunca declara por ninguem e nunca desfaz uma declaracao.
///
/// E a regra que as liga: o convivio PAGA as declaracoes (<see cref="FamiliaridadeExigida"/>), e sao as
/// declaracoes -- e mais nada -- que respondem as perguntas da FURIA (<see cref="LutoPorMorte"/> e
/// irmas), que e a porta do Super Saiyajin.
/// ===================================================================================================
///
/// ============================ A AFINIDADE SAIU -- **PEDIDO DO DONO (2026-10-10)** ============================
/// Ate esta data havia um TERCEIRO eixo, automatico: os pontos de amizade do `Friendship.dm` (de -200 a
/// +200: subiam por proximidade, caiam quando um derrubava ou matava o outro), com o rotulo que o jogo
/// deduzia deles (Ligado, Amigo, Familiar, ..., Inimigo mortal), mais o odio (`enmity`), a lista de
/// rivais e o pedido de amizade. O dono, com as palavras dele: *"a galera n ta entendendo direito a
/// diferenca de vc selecionar oq e aquela pessoa pra vc e oq o jogo diz com base nas acoes, seria melhor
/// so deixar o player escolher oq aquela pessoa e pra ele e tirar isso de afinidade e so deixar convivio
/// e declaracao"*.
///
/// O QUE CADA LEITOR DA AFINIDADE VIROU:
///
///   * `is_friend()` (>= 50 pontos), a metade `|| M.is_friend(src)` das perguntas da furia -> le a
///     DECLARACAO: amigo e quem eu declarei `Bom`, `MuitoBom` ou `Amor` (<see cref="EhAmigo"/>). O minimo
///     que havia (50 pontos = 25 minutos lado a lado) continua valendo pelo CONVIVIO que essas declaracoes
///     pedem (50), no mesmo relogio -- ver <see cref="PassosPertoPorPonto"/>;
///   * a proximidade (`accrue_friendship`) -> rende CONVIVIO, na mesma taxa (<see cref="ConviverPerto"/>).
///     Era pedido antigo do dono que conviver bastasse: *"AQUI PODERIA SER SOMENTE alguem q ficou BASTANTE
///     TEMPO COM VC ate virar amigo"*. O tempo continua sendo o caminho; o que mudou e que no fim dele quem
///     diz "amigo" e o jogador;
///   * a queda dos pontos por nocaute/morte, o "inimigo automatico", a rivalidade declarada sozinha, o odio
///     (`enmity`, so desenhado), o verb `Declare_Rival` e o pedido de amizade (`Request_Friendship`, que so
///     escrevia pontos) -> SAIRAM. Rival e inimigo hoje sao declaracoes como as outras.
///
/// O SAVE ANTIGO traz as tres listas velhas. `Inimizade` e `Rivais` sao ignoradas na leitura; os pontos
/// positivos de `Amizade` viram convivio UMA vez (<see cref="MigrarAfinidade"/>), porque eram, na mesma
/// unidade, o tempo que os dois ja tinham passado juntos.
/// ==============================================================================================================
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
/// Aqui as duas moram DENTRO do personagem que as tem, entao elas sao naturalmente por
/// observador e as chaves nao tem como divergir. Portar a troca de chaves seria portar um typo:
/// os chamadores do DM (`check_relation(M, list("Good",...))`, a pergunta do verb *"What's your
/// relation?"*) dizem, sem ambiguidade, que a intencao e "o que EU sinto por ELE".
/// ==============================================================================================
/// </summary>
public sealed class Convivio
{
	// =====================================================================
	// DE ONDE VEM O CONVIVIO -- as constantes que o dono ajusta
	// =====================================================================

	/// <summary>`FRIEND_RANGE = 6` (`Friendship.dm:19`): a quantos TILES dois jogadores convivem por estar perto.</summary>
	public const int AlcanceDeConvivioTiles = 6;

	/// <summary>
	/// `FRIEND_THROTTLE = 10` voltas do `GlobalStats`, que dorme `sleep(3)` = 0,3 s
	/// (`Stats.dm:66`). Dez voltas de 0,3 s = **3 segundos** entre um passo de proximidade e o
	/// seguinte. Guardado em segundos e nao em "voltas" porque o tique deste port nao e o de la --
	/// copiar o numero 10 daria uma cadencia diferente calada.
	/// </summary>
	public const double SegundosEntreAproximacoes = 3;

	/// <summary>
	/// QUANTOS PASSOS DE PROXIMIDADE VALEM UM PONTO DE CONVIVIO -- o `FRIEND_RATE = 0.1` do DM
	/// (`Friendship.dm:20`: 0,1 por passo) escrito ao contrario, em inteiros: 10 passos de 3 s = **um ponto
	/// a cada 30 segundos** ao lado da mesma pessoa. Era a taxa da afinidade; hoje e a do convivio, e os
	/// minutos que o dono conhecia continuam os mesmos (ver <see cref="MinutosJuntosPara"/>).
	/// </summary>
	public const int PassosPertoPorPonto = 10;

	/// <summary>
	/// ATE ONDE SO FICAR PERTO LEVA -- o `min(cur + FRIEND_RATE, 200)` de `Friendship.dm:39`, e nao por
	/// acaso o preco da declaracao mais cara (<see cref="Relacao.Amor"/>). Conversa e luta nao tem teto,
	/// como no DM (`add_familiarity` so soma).
	/// </summary>
	public const int TetoDoConvivioPorTempo = 200;

	/// <summary>Quantos segundos lado a lado rendem um ponto. Derivado, nunca digitado.</summary>
	public static double SegundosPertoPorPonto => PassosPertoPorPonto * SegundosEntreAproximacoes;

	/// <summary>
	/// QUANTO TEMPO JUNTO PAGA ESTA DECLARACAO, so de ficar perto -- a resposta em minutos, derivada.
	/// `Bom` (amigo): 50 pontos x 30 s = **25 minutos**, o mesmo relogio que fazia "amigo" na afinidade.
	/// `MuitoBom`: 50 min; `Amor`: 100 min. Quem mexer na taxa ou no preco ve o minuto mudar junto.
	/// </summary>
	public static double MinutosJuntosPara(Relacao r) => FamiliaridadeExigida(r) * SegundosPertoPorPonto / 60.0;

	/// <summary>
	/// `prob(1)` (`Talking.dm:218`): 1% por fala OUVIDA, nos dois sentidos. E devagar de proposito --
	/// quem so conversa leva ~5000 falas pra chegar aos 50 do "amigo".
	/// </summary>
	public const double ChanceDeFamiliaridadePorFala = 0.01;

	/// <summary>
	/// ============================ CONVIVIO NA PORRADA -- PEDIDO DO DONO (2026-09-05), NAO PORTE ============================
	/// No DM a familiaridade so sobe por FALA ouvida (`add_familiarity`, 1% por linha). O dono: *"fiz um
	/// personagem apanhar muito ... mas ja que eu fiz isso em pouco tempo de jogo o convivio ainda era 0 e eu
	/// so conseguia declarar ele como neutro ... ficou esquisito e confuso"*. Apanhar de alguem E conhece-lo:
	/// cada golpe que encosta tem 10% de render 1 ponto (uma luta de 100 golpes ~ 10), cair vale 10 e morrer
	/// vale 20 -- nos DOIS lados. Tres mortes (60) ja pagam o "inimigo mortal" (50).
	/// ==================================================================================================================
	/// </summary>
	public const double ChanceDeFamiliaridadePorGolpe = 0.10;
	/// <inheritdoc cref="ChanceDeFamiliaridadePorGolpe"/>
	public const int FamiliaridadePorQueda = 10;
	/// <inheritdoc cref="ChanceDeFamiliaridadePorGolpe"/>
	public const int FamiliaridadePorMorte = 20;

	/// <summary>
	/// De quanto em quanto tempo a fotografia da ficha de alguem e refeita.
	///
	/// `Contacts.dm:26` -- `if(world.time &lt; A.last_snap + 600) return`, e 600 decimos = 1 minuto.
	/// Existe porque quem chama e a proximidade, que roda a cada 3 s: sem isto, ficar perto de
	/// alguem refotografaria a ficha dele vinte vezes por minuto.
	/// </summary>
	public const long IntervaloDoRetratoMs = 60_000;

	/// <summary>
	/// ============================ O CONVIVIO QUE ABRE OS DETALHES -- PEDIDO DO DONO (2026-10-10) ============================
	/// *"ao abrir na aba people e clicar em pessoas q vc conhece (tem q ter um certo nivel de convivio minimo) vc
	/// pode ver mais detalhes da pessoa, como idade, a backstory dela etc"*.
	///
	/// VINTE: dez minutos lado a lado, ou uma luta que terminou em morte, ou duas quedas. E o mesmo preco do
	/// "inimigo" (`MuitoRuim`) -- o ponto em que a regua ja admite que voces se conhecem de verdade --, e fica
	/// bem antes do "amigo" (50): ler a historia de alguem e o que te faz QUERER declarar alguma coisa.
	///
	/// Abaixo dele a pessoa esta na lista (nome, raca, retrato) e mais nada dela sai do servidor: a idade e o
	/// genero ficam na foto que o SERVIDOR guarda e nao sao mandados (`GameServer.VerboDetalhes`); a historia e
	/// o cargo nem chegam a ser copiados pra ficha (<see cref="AprenderDetalhes"/>).
	/// ======================================================================================================================
	/// </summary>
	public const int ConvivioParaDetalhes = 20;

	/// <summary>
	/// Quantas quebras de linha a historia de alguem conserva quando OUTRO a le (as que passam viram espaco).
	/// Quinhentos caracteres cabem em quinhentas linhas de uma letra: sem teto, uma historia "suja" esticaria
	/// o cartao de quem a abre por telas inteiras.
	/// </summary>
	public const int MaxQuebrasDaHistoria = 12;

	// =====================================================================
	// O ESTADO -- campos publicos e mutaveis porque isto vai INTEIRO pro save
	// (System.Text.Json com `IncludeFields`; ver `CharacterSave`). Nada de
	// `readonly` nem de propriedade so-leitura aqui: os dois voltam VAZIOS do
	// disco, em silencio, e o personagem perde quem ele conhecia.
	// =====================================================================

	/// <summary>`known_contact_list`: assinatura -> a ficha de quem eu conheco (com o convivio e a declaracao).</summary>
	public Dictionary<string, Conhecido> Conhecidos = [];

	/// <summary>
	/// **SO PRA LER SAVE ANTIGO.** Os pontos de AFINIDADE de antes de 2026-10-10 (`friendship`:
	/// assinatura -> pontos, de -200 a +200). Nulo em todo personagem novo e em todo personagem ja
	/// migrado -- e nulo nao e gravado, entao o campo some do arquivo na primeira gravacao depois do
	/// login. Quem o consome (uma vez) e o <see cref="MigrarAfinidade"/>; nenhuma REGRA le isto.
	/// </summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Dictionary<string, double>? Amizade;

	// =====================================================================
	// PERGUNTAS
	// =====================================================================
	public bool Conhece(string sig) => sig.Length > 0 && Conhecidos.ContainsKey(sig);

	public Conhecido? Ficha(string sig) =>
		sig.Length > 0 && Conhecidos.TryGetValue(sig, out Conhecido? c) ? c : null;

	/// <summary>`check_familiarity()` (`Contacts.dm:76-80`): o CONVIVIO. Zero pra quem eu nao conheco.</summary>
	public int Familiaridade(string sig) => Ficha(sig)?.Familiaridade ?? 0;

	/// <summary>
	/// O QUE EU DECLAREI que esta pessoa e pra mim. <see cref="Relacao.Nenhuma"/> pra quem eu nao
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

	/// <summary>
	/// DEFEITO INJETADO (bancadas `convivio` e `--conhecidosteste`, e o robo `--diagconhecidos`): os detalhes
	/// de alguem saem com QUALQUER convivio -- a historia e a idade de quem acabou de aparecer na lista. Lido
	/// pela propria linha do <see cref="PodeVerDetalhes"/>.
	/// </summary>
	public static bool DetalhesSemConvivioDeTeste;

	/// <summary>
	/// EU JA CONVIVI O BASTANTE COM ESTA PESSOA PRA SABER MAIS DELA? -- o portao dos detalhes
	/// (<see cref="ConvivioParaDetalhes"/>). E UMA pergunta, e as duas pontas a fazem: quem GUARDA os
	/// detalhes na ficha (<see cref="AprenderDetalhes"/>) e quem os MANDA (`GameServer.VerboDetalhes`).
	/// </summary>
	public bool PodeVerDetalhes(string sig) =>
		Ficha(sig) is { } c && (c.Familiaridade >= ConvivioParaDetalhes || DetalhesSemConvivioDeTeste);

	/// <summary>A ficha desta pessoa SE eu tenho direito de ler os detalhes dela; nulo caso contrario.</summary>
	public Conhecido? Detalhes(string sig) => PodeVerDetalhes(sig) ? Ficha(sig) : null;

	// =====================================================================
	// O QUE CADA DECLARACAO FAZ -- as listas do DM, por DECLARACAO.
	// Estaticas e sobre o enum de proposito: a aba People escreve "o que esta escolha faz"
	// perguntando AQUI, entao a tela nao tem como dizer uma regra que o servidor nao cumpre.
	// =====================================================================

	/// <summary>
	/// ESTA DECLARACAO FAZ DA PESSOA UM AMIGO? -- o que sobrou do `is_friend()` (`Friendship.dm:46-48`).
	/// La era "50 pontos de amizade"; aqui e "eu DISSE que gosto dela": amigo, melhor amigo ou amor.
	/// </summary>
	public static bool DeclaraAmigo(Relacao r) => r is Relacao.Bom or Relacao.MuitoBom or Relacao.Amor;

	/// <summary>
	/// `Death.dm:79` -- `M.check_relation(src, list("Good","Very Good","Love","Rival/Good")) ||
	/// M.is_friend(src)`. A lista da MORTE e a mais larga: ate o rival que voce respeitava conta.
	/// </summary>
	public static bool EnfurecePorMorte(Relacao r) =>
		DeclaraAmigo(r) || r is Relacao.Bom or Relacao.MuitoBom or Relacao.Amor or Relacao.RivalBom;

	/// <summary>
	/// `KO.dm:36` -- `M.check_relation(src, list("Very Good","Love")) || M.is_friend(src)`. A lista
	/// escrita so tem os dois graus mais altos; quem poe o `Bom` aqui dentro e a metade `is_friend`,
	/// que hoje e a declaracao de amigo (ver <see cref="DeclaraAmigo"/>) -- como era com os 50 pontos.
	///
	/// (Ficou de fora o `StoredAnger += 20` da linha seguinte, `KO.dm:39`, pra quem tinha
	/// `Good`/`Rival/Good`: ele alimenta o BUFF de raiva, que e outro sistema -- ver o cabecalho de
	/// `GameServer.AmigoAbatido`.)
	/// </summary>
	public static bool EnfurecePorQueda(Relacao r) => DeclaraAmigo(r) || r is Relacao.MuitoBom or Relacao.Amor;

	/// <summary>
	/// `MajinSaga.dm:172` -- `A.check_relation(M, list("Good","Very Good")) == TRUE || A.is_friend(M)`.
	/// A lista escrita nao tem `Love` nem `Rival/Good`; o `Amor` entra pela metade `is_friend`.
	/// </summary>
	public static bool EnfurecePorAbsorcao(Relacao r) => DeclaraAmigo(r) || r is Relacao.Bom or Relacao.MuitoBom;

	/// <summary>
	/// APANHAR DESTA PESSOA E TREINO? -- o avesso de `Death.dm:75` e `KO.dm:32`, a mesma linha nos dois:
	/// `!is_friend(foe) &amp;&amp; !check_relation(foe, list("Very Good","Love","Good","Rival/Good"))`.
	/// Perguntado a lista da VITIMA sobre o algoz: quem apanhou e quem sabe se aquilo era um duelo.
	/// </summary>
	public static bool LutaEhTreino(Relacao r) =>
		DeclaraAmigo(r) || r is Relacao.MuitoBom or Relacao.Amor or Relacao.Bom or Relacao.RivalBom;

	// =====================================================================
	// AS PERGUNTAS DA RAIVA -- os `if` de `Death.dm`, `KO.dm` e `MajinSaga.dm`, portados
	// =====================================================================

	/// <summary>
	/// DEFEITO INJETADO (bancadas `convivio` e `--conhecidosteste`): o jogo volta a CONCLUIR que alguem e
	/// amigo so pelo convivio (50, o limiar antigo da afinidade), sem declaracao nenhuma. Lido pela
	/// propria linha do <see cref="EhAmigo"/>.
	/// </summary>
	public static bool AmigoSoPeloConvivioDeTeste;

	/// <summary>
	/// ESTA PESSOA E MINHA AMIGA? -- `is_friend()`. **Quem responde e a minha declaracao, e so ela**: o
	/// jogo nao deduz amizade de nada que aconteceu entre nos (pedido do dono, ver o cabecalho da classe).
	/// </summary>
	public bool EhAmigo(string sig) =>
		DeclaraAmigo(RelacaoCom(sig))
		|| (AmigoSoPeloConvivioDeTeste && Familiaridade(sig) >= FamiliaridadeExigida(Relacao.Bom));

	/// <summary>ESTA PESSOA MORRENDO NA MINHA FRENTE ME ENFURECE? Ver <see cref="EnfurecePorMorte"/>.</summary>
	public bool LutoPorMorte(string sig) => EhAmigo(sig) || EnfurecePorMorte(RelacaoCom(sig));

	/// <summary>ESTA PESSOA CAINDO NA MINHA FRENTE ME ENFURECE? Ver <see cref="EnfurecePorQueda"/>.</summary>
	public bool LutoPorQueda(string sig) => EhAmigo(sig) || EnfurecePorQueda(RelacaoCom(sig));

	/// <summary>ESTA PESSOA SENDO ABSORVIDA NA MINHA FRENTE ME ENFURECE? Ver <see cref="EnfurecePorAbsorcao"/>.</summary>
	public bool LutoPorAbsorcao(string sig) => EhAmigo(sig) || EnfurecePorAbsorcao(RelacaoCom(sig));

	/// <summary>
	/// QUEM ME DERRUBOU ERA INIMIGO? -- perguntado a VITIMA sobre o algoz (`Death.dm:75`, `KO.dm:34`).
	///
	/// **E ela que impede o treino de virar fabrica de Super Saiyajin.** Sem esta pergunta, dois
	/// amigos sparrando enfureceriam a plateia a cada nocaute -- e o DM diz isso em voz alta no
	/// comentario de `Death.dm:75`: *"no rage for friendly duels or environmental death"*.
	///
	/// INIMIGO E O PADRAO: quem eu nao declarei amigo (nem rival amigo) e inimigo aos olhos da furia,
	/// conheca eu ou nao. Declarar alguem "inimigo" nao muda esta resposta -- ela ja era sim.
	///
	/// (2026-10-18) E A MESMA PERGUNTA E FEITA A QUEM ASSISTE -- o dono: *"se um amigo seu derruba seu outro amigo, n deveria triggar raiva,
	/// somente pessoas q vc n conhece/neutras ou rivais ruins pra baixo q contam pra vc ficar com raiva"*. Desconhecido, sem declaracao,
	/// neutro, rival ruim, ruim, muito ruim e odio respondem sim; amigo e rival bom, nao. Ver `GameServer.LutoNaVizinhanca`.
	/// </summary>
	public bool AlgozEhInimigo(string sig) => !EhAmigo(sig) && !LutaEhTreino(RelacaoCom(sig));

	// =====================================================================
	// AS DECLARACOES: PRECO, NOME E ORDEM
	// =====================================================================
	/// <summary>
	/// QUANTO CONVIVIO CADA DECLARACAO PEDE -- os numeros do `switch` do verb `Relation()`
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

	/// <summary>
	/// O NOME QUE O JOGADOR LE -- "essa pessoa e pra voce: ...". Substantivos, e nao os adjetivos do DM
	/// ("Good", "Very Bad"): a pergunta da aba e o que a pessoa E, e "ela e muito bom" nao e resposta.
	/// </summary>
	public static string NomeDaRelacao(Relacao r) => r switch
	{
		Relacao.Amor => "amor",
		Relacao.MuitoBom => "melhor amigo",
		Relacao.Bom => "amigo",
		Relacao.RivalBom => "rival amigo",
		Relacao.Neutro => "neutro",
		Relacao.RivalRuim => "rival inimigo",
		Relacao.Ruim => "desafeto",
		Relacao.MuitoRuim => "inimigo",
		Relacao.Odio => "inimigo mortal",
		_ => "",
	};

	/// <summary>
	/// De um nome pra declaracao. Casa o nome de hoje (<see cref="NomeDaRelacao"/>), o de antes de
	/// 2026-10-10 ("muito bom", "odio" -- macros e bancadas antigas) e o do DM em ingles. Sem diferenciar
	/// maiuscula.
	/// </summary>
	public static Relacao RelacaoPorNome(string nome) => nome.Trim().ToLowerInvariant() switch
	{
		"amor" or "love" => Relacao.Amor,
		"melhor amigo" or "muito bom" or "very good" => Relacao.MuitoBom,
		"amigo" or "bom" or "good" => Relacao.Bom,
		"rival amigo" or "rival bom" or "rival/good" or "rival good" => Relacao.RivalBom,
		"neutro" or "neutral" => Relacao.Neutro,
		"rival inimigo" or "rival ruim" or "rival/bad" or "rival bad" => Relacao.RivalRuim,
		"desafeto" or "ruim" or "bad" => Relacao.Ruim,
		"inimigo" or "muito ruim" or "very bad" => Relacao.MuitoRuim,
		"inimigo mortal" or "odio" or "ódio" or "hate" => Relacao.Odio,
		_ => Relacao.Nenhuma,
	};

	/// <summary>
	/// As nove declaracoes do `input()` do DM (`Contacts.dm:117`), na ordem em que a aba as desenha: do
	/// afeto ao desafeto, com o neutro no meio -- uma regua, e a posicao na regua ja diz o tom.
	/// </summary>
	public static readonly Relacao[] Declaraveis =
	[
		Relacao.Amor, Relacao.MuitoBom, Relacao.Bom, Relacao.RivalBom,
		Relacao.Neutro,
		Relacao.RivalRuim, Relacao.Ruim, Relacao.MuitoRuim, Relacao.Odio,
	];

	/// <summary>
	/// O DEGRAU DO CONVIVIO: quantos precos diferentes (0, 10, 15, 20, 50, 100, 200) ele ja paga -- de 1
	/// (so o neutro) a 7 (tudo). So pra ordenar a lista sem que ela dance: o numero muda a cada 30 s ao
	/// lado de alguem, o degrau so seis vezes na vida de um par.
	/// </summary>
	public static int Degrau(int familiaridade) =>
		Declaraveis.Select(FamiliaridadeExigida).Distinct().Count(preco => preco <= familiaridade);

	/// <summary>
	/// A ORDEM DA ABA: primeiro quem EU classifiquei (na ordem da regua, do amor ao inimigo mortal), depois
	/// quem ainda e neutro pra mim, do maior degrau de convivio pro menor; nome e assinatura desempatam.
	/// Nenhuma chave aqui muda sozinha a cada tique -- cartao que troca de lugar debaixo do mouse faz
	/// errar o clique.
	/// </summary>
	public List<Conhecido> EmOrdem() =>
	[
		.. Conhecidos.Values
			.OrderBy(c => c.Relacao is Relacao.Nenhuma or Relacao.Neutro ? int.MaxValue : Array.IndexOf(Declaraveis, c.Relacao))
			.ThenByDescending(c => Degrau(c.Familiaridade))
			.ThenBy(c => c.Nome, StringComparer.OrdinalIgnoreCase)
			.ThenBy(c => c.Assinatura, StringComparer.Ordinal),
	];

	// =====================================================================
	// MUDANCAS
	// =====================================================================

	/// <summary>
	/// FOTOGRAFA A FICHA DE ALGUEM E ME FAZ CONHECE-LO -- o `update_my_contact()` mais o
	/// `known_contact_list[sig] = ...` das duas pontas (`Friendship.dm:42-44`, `Contacts.dm:86`).
	///
	/// O convivio e a declaracao SOBREVIVEM a refotografada: eles sao meus, a foto e dele.
	/// Sobrescrever tudo aqui era o jeito obvio de zerar o convivio de alguem sempre que ele
	/// passasse na frente.
	///
	/// Devolve TRUE quando esta pessoa acabou de entrar na lista (primeiro encontro).
	/// </summary>
	public bool Fotografar(string sig, string nome, string raca, string genero, int idade, long agoraMs)
	{
		if (sig.Length == 0) return false;

		if (!Conhecidos.TryGetValue(sig, out Conhecido? c))
		{
			Conhecidos[sig] = new Conhecido
			{
				Assinatura = sig, Nome = nome, Raca = raca,
				Genero = genero, Idade = idade, VistoEm = agoraMs, DesdeMs = agoraMs,
			};
			return true;
		}

		// o throttle de 1x/min do original: quem chama e a proximidade, a cada 3 s
		if (agoraMs < c.VistoEm + IntervaloDoRetratoMs) return false;

		c.Nome = nome;
		c.Raca = raca;
		c.Genero = genero;
		c.Idade = idade;
		c.VistoEm = agoraMs;
		return false;
	}

	/// <summary>
	/// O QUE SE APRENDE DE ALGUEM CONVIVENDO -- a historia que ele escreveu e o cargo que carrega entram na
	/// MINHA ficha dele, e so se o convivio ja paga (<see cref="PodeVerDetalhes"/>). E parte da fotografia:
	/// quem chama e o mesmo ponto que fotografa (estar perto, conversar, lutar), com o mesmo throttle de um
	/// minuto -- o que eu sei de alguem e o que ele era da ultima vez que estive com ele, e nao o que ele e
	/// agora do outro lado do mundo.
	///
	/// A PRIMEIRA VEZ NAO ESPERA O MINUTO: no instante em que o convivio atravessa o preco, a proxima
	/// chamada ja guarda. Devolve TRUE quando guardou.
	/// </summary>
	public bool AprenderDetalhes(string sig, string historia, string titulo, long agoraMs)
	{
		if (!PodeVerDetalhes(sig)) return false;
		Conhecido c = Conhecidos[sig];
		if (c.DetalhesEm != 0 && agoraMs < c.DetalhesEm + IntervaloDoRetratoMs) return false;

		c.Historia = HistoriaLegivel(historia);
		c.Titulo = titulo;
		c.DetalhesEm = Math.Max(agoraMs, 1);   // zero quer dizer "nunca", ate numa bancada que roda no instante 0
		return true;
	}

	/// <summary>
	/// A HISTORIA DE ALGUEM, DO JEITO QUE OUTRO JOGADOR PODE LE-LA: sem caractere de controle nem de formato
	/// (o que apaga texto, inverte a direcao da linha ou nao desenha nada), com no maximo uma linha em branco
	/// entre paragrafos, no maximo <see cref="MaxQuebrasDaHistoria"/> quebras de linha, e cortada no tamanho
	/// que a criacao aceita (`CharacterDraft.BackstoryMax`).
	///
	/// A CRIACAO JA RECUSA o texto grande demais (`CharacterDraft.Validar`), mas so confere o tamanho -- e o
	/// que esta no disco pode ter sido escrito por uma versao antiga, por um admin ou por um pacote forjado.
	/// Aqui e a ULTIMA porta antes de o texto de um jogador aparecer na tela de outro, entao e aqui que ele e
	/// limpo, sempre.
	///
	/// POR PONTO DE CODIGO (`Rune`), e nao por `char`: um caractere de formato fora do plano basico sao DOIS
	/// `char`, e olhando um de cada vez ele passaria. E a metade solta de um par substituto -- que nao e
	/// caractere nenhum -- chega aqui como U+FFFD e sai junto. Esta ultima nao e so higiene: o escritor do fio
	/// (`NetDataWriter`, UTF-8 estrito) LANCA EXCECAO com ela, entao uma historia assim num save derrubaria o
	/// verb de quem pedisse os detalhes.
	///
	/// O PRECO: os caracteres de formato incluem o "juntador" dos emojis compostos (familia, casal) e as
	/// marcas de direcao do arabe e do hebraico -- o emoji composto vira as partes dele, e so.
	/// </summary>
	public static string HistoriaLegivel(string? bruta)
	{
		if (string.IsNullOrWhiteSpace(bruta)) return "";

		// linha a linha: filtra, apara, e junta as linhas vazias seguidas numa so
		var linhas = new List<string>();
		foreach (string crua in bruta.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
		{
			var sb = new StringBuilder(crua.Length);
			foreach (Rune r in crua.EnumerateRunes())
			{
				if (r.Value == '\t') { sb.Append(' '); continue; }
				if (r == Rune.ReplacementChar || Rune.IsControl(r)) continue;
				if (Rune.GetUnicodeCategory(r) is UnicodeCategory.Format or UnicodeCategory.PrivateUse
					or UnicodeCategory.OtherNotAssigned or UnicodeCategory.LineSeparator
					or UnicodeCategory.ParagraphSeparator) continue;
				sb.Append(r.ToString());
			}
			string limpa = sb.ToString().Trim();
			if (limpa.Length == 0 && (linhas.Count == 0 || linhas[^1].Length == 0)) continue;
			linhas.Add(limpa);
		}
		while (linhas.Count > 0 && linhas[^1].Length == 0) linhas.RemoveAt(linhas.Count - 1);

		// as primeiras quebras ficam; as que passam do teto viram espaco
		var texto = new StringBuilder();
		for (int i = 0; i < linhas.Count; i++)
		{
			if (i > 0) texto.Append(i <= MaxQuebrasDaHistoria ? '\n' : ' ');
			texto.Append(linhas[i]);
		}

		int teto = Races.CharacterDraft.BackstoryMax;
		if (texto.Length > teto)
		{
			// sem partir um par substituto (um emoji) no meio: metade dele e um caractere invalido
			if (char.IsHighSurrogate(texto[teto - 1])) teto--;
			texto.Length = teto;
		}
		return texto.ToString().Trim();
	}

	/// <summary>
	/// DEFEITO INJETADO (bancadas `convivio` e `--conhecidosteste`): ficar perto nao rende convivio -- o
	/// estado em que o sistema ficaria se a afinidade saisse sem que o tempo junto fosse pra lugar nenhum.
	/// Lido pela propria linha do <see cref="ConviverPerto"/>.
	/// </summary>
	public static bool TempoJuntoNaoContaDeTeste;

	/// <summary>
	/// UM PASSO DE PROXIMIDADE -- o corpo do `accrue_friendship()` (`Friendship.dm:37-41`), rendendo
	/// CONVIVIO onde rendia afinidade: a cada <see cref="PassosPertoPorPonto"/> passos, um ponto, ate o
	/// <see cref="TetoDoConvivioPorTempo"/>. Exige ja conhecer a pessoa (o servidor fotografa antes).
	///
	/// Devolve TRUE no passo em que um PONTO entrou (e so nele o servidor refaz a aba);
	/// <paramref name="abriuAPortaDeAmigo"/> sai TRUE no ponto exato que atravessou o preco do "amigo" --
	/// e o que deixa o servidor dizer, uma unica vez, que ja da pra declarar.
	/// </summary>
	public bool ConviverPerto(string sig, out bool abriuAPortaDeAmigo)
	{
		abriuAPortaDeAmigo = false;
		if (TempoJuntoNaoContaDeTeste) return false;
		if (!Conhecidos.TryGetValue(sig, out Conhecido? c) || c.Familiaridade >= TetoDoConvivioPorTempo) return false;

		if (++c.PassosPerto < PassosPertoPorPonto) return false;
		c.PassosPerto = 0;
		abriuAPortaDeAmigo = SomarFamiliaridade(sig);
		return true;
	}

	/// <summary>
	/// `add_familiarity()` (`Contacts.dm:82-89`): soma convivio. Chamado pela conversa (`Talking.dm:218`,
	/// 1% por fala ouvida, nos dois sentidos), pela luta e pelo passo de proximidade.
	///
	/// Exige ja conhecer a pessoa -- no DM tambem (`if(isnull(contact_list[...])) return FALSE`),
	/// e o servidor fotografa antes de somar.
	///
	/// Devolve TRUE **so na soma que atravessou o preco do "amigo"** (<see cref="Relacao.Bom"/>): e a
	/// unica porta que o servidor anuncia, porque e a que muda regra (a furia) quando o jogador a usa.
	/// </summary>
	public bool SomarFamiliaridade(string sig, int quanto = 1)
	{
		if (quanto <= 0 || !Conhecidos.TryGetValue(sig, out Conhecido? c)) return false;
		int porta = FamiliaridadeExigida(Relacao.Bom);
		bool antes = c.Familiaridade >= porta;
		c.Familiaridade += quanto;
		return !antes && c.Familiaridade >= porta;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancadas `convivio` e `--conhecidosteste`): qualquer declaracao passa com convivio
	/// zero -- "amor" a um clique de quem acabou de aparecer, e com ele a furia de graca. Lido pela propria
	/// linha do <see cref="Declarar"/>.
	/// </summary>
	public static bool DeclararSemConvivioDeTeste;

	/// <summary>
	/// EU DIGO O QUE ESTA PESSOA E PRA MIM -- o verb `Relation()` (`Contacts.dm:115-151`), com o portao
	/// de convivio dele. Mora aqui (e nao no servidor) pra bancada do Core poder cobrar o portao.
	///
	/// NAO HA OUTRO JEITO DE UMA DECLARACAO MUDAR: nada que aconteca entre os dois (apanhar, morrer,
	/// passar um ano sem se ver) a escreve ou a apaga. Quem declarou "amigo" e foi morto por ele continua
	/// tendo declarado amigo, ate dizer outra coisa.
	/// </summary>
	public Declaracao Declarar(string sig, Relacao r)
	{
		if (Ficha(sig) is not { } c) return Declaracao.NaoConhece;
		if (r == Relacao.Nenhuma) return Declaracao.NaoSeDeclara;
		if (c.Familiaridade < FamiliaridadeExigida(r) && !DeclararSemConvivioDeTeste) return Declaracao.FaltaConvivio;
		c.Relacao = r;
		return Declaracao.Feita;
	}

	/// <summary>
	/// ============================ O SAVE DE ANTES DE 2026-10-10 ============================
	/// A afinidade saiu do jogo, mas os pontos POSITIVOS dela eram tempo vivido: 0,1 a cada 3 s ao lado de
	/// alguem -- a mesma taxa que hoje rende convivio. Jogar o numero fora faria dois amigos de meses
	/// esperarem outros 25 minutos pra poder dizer "amigo". Entao, UMA vez por personagem, os pontos
	/// positivos somam no convivio de quem ainda tem ficha (as duas medidas tinham fontes diferentes -- uma
	/// o tempo, a outra a conversa e a luta --, por isso SOMA e nao maximo).
	///
	/// O NEGATIVO NAO VIRA NADA: era o jogo dizendo "inimigo", e isso agora e escolha do jogador. A
	/// DECLARACAO NAO E TOCADA pelo mesmo motivo -- quem era "Amigo" pelos pontos ganha o convivio que
	/// paga a declaracao, e declara se quiser.
	///
	/// Devolve TRUE quando havia afinidade antiga com alguem (o servidor avisa o jogador que a aba mudou).
	/// O campo fica nulo no fim, e nulo nao e gravado: nao ha segunda migracao.
	/// ========================================================================================
	/// </summary>
	public bool MigrarAfinidade()
	{
		if (Amizade is not { } velha) return false;
		Amizade = null;

		foreach ((string sig, double pontos) in velha)
			if (pontos >= 1 && Conhecidos.TryGetValue(sig, out Conhecido? c))
				c.Familiaridade += (int)Math.Min(pontos, TetoDoConvivioPorTempo);
		return velha.Count > 0;
	}
}
