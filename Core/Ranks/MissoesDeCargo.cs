using Jandirus.Core.World;

namespace Jandirus.Core.Ranks;

// =====================================================================================
// OS DEVERES DO CARGO -- as tarefas com prazo que fazem um trono significar alguma coisa.
//
// Fonte: `Modules/Ranks/RankQuests.dm:417-659` (o `rq_loop`, o `rq_assign`, o `rq_check_done`, o
// `rq_complete`, o `rq_fail` e o `rq_destitute`) e `:34-54` (as constantes).
//
// ============================ POR QUE ISTO EXISTE ============================
// Ate aqui o cargo era so um NOME com um kit de skills: ganhava-se por prova, nomeacao, sucessao ou
// duelo, e depois **nada nunca mais acontecia**. O `RankDef.TemDeveres` estava escrito nos 17 cargos
// certos desde o primeiro dia e nao tinha um unico leitor -- a mesma familia de defeito que a
// `RankDef.Concede` ja custou a este projeto (trinta cargos declarando o que davam e nenhum dando).
//
// Um cargo com dever tambem e a UNICA coisa que da sentido a destituicao, que ja existia inteira
// (`Destronar`) com duas entradas so do Deus da Destruicao. Com este arquivo ela passa a valer pros
// dezessete.
// ============================================================================
//
// ============================ AS DUAS VOCACOES, E ELAS JA ESTAVAM NA TABELA ============================
// O DM tem quatro listas soltas (`RQ_ALL`, `RQ_WISDOM`, `RQ_SKY`, `RQ_EVIL`) e a tabela de cargos do
// port ja carrega as quatro como CAMPOS (`TemDeveres`, `Sabedoria`, `DoOutroMundo`, `Maligno`).
// Entao aqui **nao ha lista nenhuma de cargo**: tudo se deriva da `RankDef`. Duplicar as listas seria
// criar o defeito que o proprio DM registrou em voz alta no `rq_promo_targets` (`:161`) --
// *"espelha o rq_requirements: mexer aqui sem mexer la cria degrau que aparece mas nunca deixa subir"*.
//
//   * SABEDORIA (`Sabedoria = true`, 11 cargos) -- a tarefa e de SERVICO, e nao de poder: acumular
//     pontos com visitantes qualificados por perto. Vivos TREINANDO pros mestres; almas MORTAS pros
//     cargos do Outro Mundo.
//   * PODER (os outros 6) -- libertar o proprio planeta, cacar vilao (protetores) ou HEROI (lordes
//     malignos), destruir um mundo (o Lorde do Gelo) ou financiar a Terra (o Presidente).
// ==================================================================================================
// =====================================================================================

/// <summary>Que tarefa o cargo recebeu. E o `st["task"]` do DM, que la e uma string solta.</summary>
public enum TipoDeTarefa : byte
{
	/// <summary>Sem tarefa: esperando a proxima.</summary>
	Nenhuma = 0,

	/// <summary>SABEDORIA: pontos de presenca com visitantes qualificados (`"servico"`).</summary>
	Servico = 1,

	/// <summary>PODER, protetores: eliminar um vilao (`"vilao"`).</summary>
	Vilao = 2,

	/// <summary>PODER, lordes malignos: esmagar um heroi (`"heroi"`).</summary>
	Heroi = 3,

	/// <summary>PODER: o proprio planeta esta dominado -- liberte-o (`"liberar"`).</summary>
	Libertar = 4,

	/// <summary>PODER, o Presidente: verba pro cofre da Terra (`"verba"`).</summary>
	Verba = 5,

	/// <summary>PODER, o Lorde do Gelo: destruir um mundo (`"planeta"`).</summary>
	Planeta = 6,
}

/// <summary>
/// A FICHA DE UM CARGO -- o `rq_state[key]` do DM, que la e uma lista associativa de nove chaves.
///
/// UMA POR CARGO E NAO POR PESSOA, como no original: o dever e do TRONO. Quem assume herda a
/// cadencia, nao as falhas -- e e o <see cref="Dono"/> que faz isso acontecer sozinho (ver la).
/// </summary>
public sealed class FichaDeMissao
{
	/// <summary>
	/// A CONTA DE QUEM CARREGAVA O CARGO QUANDO ESTA FICHA FOI ESCRITA.
	///
	/// ============================ ELE E O QUE ZERA A FICHA, E POR ISSO E DERIVADO ============================
	/// O DM apaga a ficha na mao em quatro lugares (`rq_grant` duas vezes, `rq_destitute`, e o
	/// `rq_loop` quando o trono vaga). O port troca de dono por SEIS portas hoje e vai trocar por mais
	/// -- pendurar "apague a ficha" em cada uma seria seis lugares pra alguem esquecer um, que e
	/// literalmente o argumento que a `ReconciliarDadiva` ja escreveu.
	///
	/// Entao a ficha guarda de quem ela e, e o tique compara com o trono de agora. Dono diferente =
	/// ficha nova. Idempotente, derivada, e cobre inclusive a troca que aconteceu com o servidor
	/// fora do ar.
	/// ====================================================================================================
	/// </summary>
	public string Dono = "";

	public TipoDeTarefa Tarefa;

	/// <summary>O planeta da tarefa (`st["planet"]`): o nome legivel, pro texto.</summary>
	public string Planeta = "";

	/// <summary>
	/// A IDENTIDADE do planeta alvo -- o `ChaveDePlaneta.Texto`. Existe separada do nome porque nome
	/// de mundo procedural **nao e unico** (ver `ChaveDePlaneta`): destruir o homonimo do outro lado
	/// da galaxia cumpriria a tarefa.
	/// </summary>
	public string PlanetaChave = "";

	/// <summary>`st["target_sig"]` -- aqui a CONTA, como todo o resto do sistema de cargos.</summary>
	public string AlvoConta = "";

	/// <summary>`st["tname"]`: o nome que o portador leu quando a tarefa chegou.</summary>
	public string AlvoNome = "";

	/// <summary>Instante do <c>TempoDoMundo</c> (segundos) em que o prazo vence. `st["deadline"]`.</summary>
	public double Prazo;

	/// <summary>Quando a proxima tarefa pode ser sorteada. `st["next"]`.</summary>
	public double Proxima;

	/// <summary>`st["fails"]`: <see cref="MissoesDeCargo.FalhasQueDestituem"/> e a destituicao.</summary>
	public int Falhas;

	/// <summary>`st["done"]` -- o RENOME. E ele que destranca a ascensao.</summary>
	public int Cumpridas;

	/// <summary>`st["prog"]` / `st["goal"]`: so o servico e a verba os usam.</summary>
	public int Progresso;
	public int Meta;

	/// <summary>`st["promo_warned"]`: o aviso de ascensao sai UMA vez por degrau que abriu.</summary>
	public bool AvisouAscensao;
}

/// <summary>
/// AS REGRAS PURAS DOS DEVERES DE CARGO. Prazos, vocacao, alvos elegiveis, recompensa e destituicao.
///
/// O que **nao** mora aqui: quem esta online, quem esta perto, quem domina o planeta, quem morreu.
/// Isso e do `GameServer.CargoMissoes.cs`, que e a autoridade.
/// </summary>
public static class MissoesDeCargo
{
	// =====================================================================
	// OS NUMEROS -- `RankQuests.dm:34-54`
	// =====================================================================

	/// <summary>
	/// `RQ_TASK_DAYS 3` (`:38`) -- dias **IN-GAME** de prazo, e o mesmo numero de intervalo entre
	/// tarefas.
	/// </summary>
	public const int DiasDeTarefa = 3;

	/// <summary>
	/// ============================ O PRAZO E DO RELOGIO DO JOGO, E NAO DO CALENDARIO ============================
	/// O DM e explicito no comentario do proprio define (`:35-37`): *"os prazos seguem o relogio DO
	/// JOGO, nao o calendario do jogador -- mexer no DAY_REAL_MINUTES arrasta as quests junto"*. La um
	/// dia in-game dura `DAY_REAL_MINUTES 20` minutos reais, entao 3 dias = ~60 min reais.
	///
	/// **AQUI O DIA E OUTRO E ISSO E DE PROPOSITO**: neste port um dia in-game dura
	/// <see cref="Espaco.SegundosPorDiaInGame"/> (24 min, o mesmo ciclo do dia/noite do ceu), entao 3
	/// dias sao ~72 min reais. Copiar os "60 minutos" do DM seria portar o NUMERO e perder a REGRA --
	/// o prazo deixaria de ser tres nascer-do-sol e viraria uma constante solta que o ceu contradiz.
	///
	/// E o relogio e o `TempoDoMundo` (UTC + o adianto do ceu) pelo mesmo motivo da lealdade da
	/// conquista: ele **anda com o servidor desligado** (por isso existe o perdao de boot, ver
	/// `GameServer.PerdoarPrazosNoBoot`) e e adiantavel pela manivela do ceu, que e o que torna um
	/// prazo de 72 minutos testavel em vez de uma promessa.
	/// ====================================================================================================
	/// </summary>
	public static double SegundosDePrazo => DiasDeTarefa * Espaco.SegundosPorDiaInGame;

	/// <summary>Mesmo numero: `RQ_TASK_INTERVAL` e `RQ_TASK_DEADLINE` sao a mesma conta (`:39-40`).</summary>
	public static double SegundosDeIntervalo => SegundosDePrazo;

	/// <summary>`RQ_TASK_RETRY 6000` ds = 10 min: sem alvo elegivel, tenta de novo (`:42`).</summary>
	public const double SegundosSemAlvo = 600;

	/// <summary>`RQ_FAILS_MAX 3` (`:43`). A terceira falha DESTITUI.</summary>
	public const int FalhasQueDestituem = 3;

	/// <summary>`RQ_REWARD_ZENNI 100000` (`:44`).</summary>
	public const double ZeniPorTarefa = 100_000;

	/// <summary>`RQ_REWARD_KARMA 5` (`:45`) -- so pros cargos que nao sao `Maligno`.</summary>
	public const int KarmaPorTarefa = 5;

	/// <summary>O teto de karma do DM (`min(H.karma + RQ_REWARD_KARMA, 100)`, `:594`).</summary>
	public const int KarmaMaximo = 100;

	/// <summary>`RQ_SERVICE_RANGE 6` (`:46`): o raio do servico, em tiles.</summary>
	public const int TilesDoServico = 6;

	/// <summary>`RQ_SERVICE_CAP 3` (`:47`): no maximo 3 visitantes contam por minuto.</summary>
	public const int TetoDoServico = 3;

	/// <summary>A cadencia do `rq_loop`: `sleep(600)` = 1 minuto (`:632`).</summary>
	public const double SegundosDoLaco = 60;

	/// <summary>`RQ_PROMO_QUESTS 3` (`:53`): o RENOME que destranca a ascensao.</summary>
	public const int TarefasParaAscender = 3;

	/// <summary>`RQ_PRESIDENT_FUND 150000` (`:52`): a verba por tarefa.</summary>
	public const double VerbaDoPresidente = 150_000;

	/// <summary>
	/// `GOD_TASK_VILLAIN_KARMA`/`:519` -- karma daqui pra baixo ja e ameaca, mesmo sem o selo de
	/// vilao. E o mesmo -50 que o Deus da Destruicao usa, e nao por acaso: e o mesmo conceito.
	/// </summary>
	public const int KarmaDeAmeaca = -50;

	/// <summary>`P.karma >= 50` (`:519`): o que um lorde maligno considera um HEROI incomodo.</summary>
	public const int KarmaDeHeroi = 50;

	// =====================================================================
	// VOCACAO -- quem faz o que
	// =====================================================================

	/// <summary>
	/// Os cargos com dever. E o `RQ_ALL` (`:63`) **derivado**: 17 cargos, e a tabela ja os marcava.
	/// </summary>
	public static IEnumerable<RankDef> ComDeveres => Cargos.Todos.Where(r => r.TemDeveres);

	/// <summary>
	/// O PLANETA DE UM CARGO -- `myplanet` (`:497`). Vazio = o cargo nao e de lugar nenhum.
	///
	/// Sao os tres unicos cargos do DM presos a um mundo: o trono de Vegeta a Vegeta, e o Guardiao e o
	/// Presidente a Terra. Os nomes sao os das ZONAS, como no resto do port -- escrever "Terra" aqui
	/// daria uma tarefa que nunca casa e nunca reclama.
	/// </summary>
	public static string PlanetaDoCargo(string chave) => chave.ToLowerInvariant() switch
	{
		"kov" => "Vegeta",
		"guardian" or "president" => "Earth",
		_ => "",
	};

	/// <summary>
	/// A META DO SERVICO -- `:495`. Dez pros seis da escada divina, quinze pro resto.
	///
	/// ESTA LISTA E LITERAL DE PROPOSITO, e e a unica do arquivo. Nao ha campo na `RankDef` que
	/// separe estes seis (nem `DoOutroMundo` serve: o King Yemma esta la e recebe 15), entao derivar
	/// exigiria inventar um criterio que o DM nao tem. Um literal com a linha do original ao lado e
	/// mais honesto que uma regra bonita que o autor nunca escreveu.
	/// </summary>
	public static int MetaDeServico(string chave) =>
		chave.ToLowerInvariant() is "nkai" or "skai" or "ekai" or "wkai" or "grandkai" or "kaioshin" ? 10 : 15;

	/// <summary>
	/// QUAL TAREFA ESTE CARGO RECEBE AGORA. E a escada literal do `rq_assign` (`:493-526`), na ordem.
	///
	/// A ORDEM E A REGRA: o planeta dominado vem ANTES de tudo o que um cargo de poder faria, porque
	/// um Rei cujo mundo esta sob bandeira alheia nao tem o que cacar. Trocar a ordem daria ao
	/// Presidente uma tarefa de verba enquanto a Terra e de outro.
	/// </summary>
	/// <param name="meuPlanetaDominado">
	/// O planeta deste cargo tem dono, e o dono NAO e ele. (Vazio/sem dono = falso.)
	/// </param>
	public static TipoDeTarefa Escolher(RankDef r, bool meuPlanetaDominado)
	{
		if (r.Sabedoria) return TipoDeTarefa.Servico;
		if (meuPlanetaDominado) return TipoDeTarefa.Libertar;
		if (string.Equals(r.Chave, "president", StringComparison.OrdinalIgnoreCase)) return TipoDeTarefa.Verba;
		if (string.Equals(r.Chave, "frostlord", StringComparison.OrdinalIgnoreCase)) return TipoDeTarefa.Planeta;
		return r.Maligno ? TipoDeTarefa.Heroi : TipoDeTarefa.Vilao;
	}

	/// <summary>
	/// ESTA ALMA SERVE DE ALVO DE CACA? `:519`, e as duas metades sao opostas de proposito.
	///
	/// O protetor caca quem o mundo marcou (`isVillain`) **ou** quem tem karma podre; o lorde maligno
	/// caca quem tem karma ALTO. Nenhum dos dois caca gente comum -- e por isso um servidor de
	/// neutros deixa os dois sem tarefa, que e o `else` do original (10 min e tenta de novo).
	/// </summary>
	public static bool ServeDeAlvo(bool cargoMaligno, bool selado, int karma) =>
		cargoMaligno ? karma >= KarmaDeHeroi : selado || karma <= KarmaDeAmeaca;

	/// <summary>
	/// ESTE VISITANTE CONTA PONTO DE SERVICO? `:568`.
	///
	/// Os cargos do Outro Mundo atendem ALMAS (mortos); os mestres contam quem esta TREINANDO. Um
	/// mestre so pontua com aluno treinando na frente dele -- que e a diferenca entre "servir" e
	/// "ficar parado onde tem gente".
	/// </summary>
	public static bool ContaComoServico(bool cargoDoOutroMundo, bool visitanteMorto, bool visitanteTreinando) =>
		cargoDoOutroMundo ? visitanteMorto : !visitanteMorto && visitanteTreinando;

	/// <summary>
	/// O PORTADOR CONSEGUE SERVIR AGORA? `:564` -- *"mestre morto nao da aula; Kaio/Yemma atende ate
	/// morto"*.
	/// </summary>
	public static bool PodeServir(bool cargoDoOutroMundo, bool portadorMorto) =>
		cargoDoOutroMundo || !portadorMorto;

	// =====================================================================
	// ASCENSAO -- o RENOME que destranca a escada
	// =====================================================================

	/// <summary>
	/// OS DEGRAUS ACIMA DESTE CARGO -- o `rq_promo_targets` (`:162`), **derivado da propria tabela**.
	///
	/// ============================ POR QUE DERIVAR ERA OBRIGATORIO AQUI ============================
	/// O DM escreve a escada DUAS vezes: uma no `rq_requirements` (o Grand Kai exige ser um dos 4
	/// Kaios) e outra no `rq_promo_targets` (os 4 Kaios apontam pro Grand Kai). E ele mesmo deixou o
	/// aviso escrito por cima da segunda: *"espelha o rq_requirements: mexer aqui sem mexer la cria
	/// degrau que aparece mas nunca deixa subir"*.
	///
	/// A `RankDef.Degraus` ja e a primeira metade. Esta funcao e a segunda, LIDA da primeira -- entao
	/// as duas nao tem como divergir, e quem ligar um cargo novo na escada ganha a promocao de graca.
	/// ==========================================================================================
	///
	/// So `Porta == Prova` entra, como no DM (`if(k in RQ_CLAIMABLE)`): o trono de Vegeta, o titulo do
	/// Deus e os cargos de admin tem caminho proprio e nao se sobe pra eles.
	/// </summary>
	public static IEnumerable<RankDef> DegrausAcima(string chave) =>
		chave.Length == 0
			? []
			: Cargos.Todos.Where(r => r.Porta == PortaDoCargo.Prova
								   && r.Degraus.Contains(chave, StringComparer.OrdinalIgnoreCase));

	/// <summary>`rq_promo_ready` (`:179`): renome bastante pra almejar o degrau de cima.</summary>
	public static bool PodeAscender(int renome) => renome >= TarefasParaAscender;

	/// <summary>Os nomes dos degraus acima, pro texto (`rq_promo_txt`, `:194`).</summary>
	public static string TextoDosDegraus(string chave) =>
		string.Join(" ou ", DegrausAcima(chave).Select(r => r.Nome));

	// =====================================================================
	// TEXTO
	// =====================================================================

	/// <summary>
	/// QUANTO FALTA, NO RELOGIO DO JOGO -- o `rq_tempo_txt` (`:150`), que imprime as horas IN-GAME
	/// com o equivalente real entre parenteses.
	///
	/// AS DUAS UNIDADES SAO A MENSAGEM: "72h in-game" e a regra falando (tres dias, como o cargo
	/// promete), e "~72 min reais" e o que a pessoa precisa pra decidir se da tempo antes de dormir.
	/// So a primeira seria uma promessa que ninguem sabe medir; so a segunda esconderia que o prazo
	/// anda com o ceu.
	/// </summary>
	public static string TextoDeTempo(double segundos)
	{
		if (segundos < 0) segundos = 0;
		return $"{Math.Round(segundos / Espaco.SegundosPorDiaInGame * 24)}h in-game "
			 + $"(~{Math.Round(segundos / 60)} min reais)";
	}

	/// <summary>A tarefa em palavras -- o `rq_task_desc` (`:476`).</summary>
	public static string Descricao(FichaDeMissao f) => f.Tarefa switch
	{
		TipoDeTarefa.Libertar => $"LIBERTAR {NomeVisivel(f.Planeta)} do domínio inimigo (conquista)",
		TipoDeTarefa.Vilao => $"CAÇAR o vilão {f.AlvoNome} (eliminá-lo enquanto online)",
		TipoDeTarefa.Heroi => $"ESMAGAR o herói {f.AlvoNome} (eliminá-lo enquanto online)",
		TipoDeTarefa.Planeta => $"DESTRUIR o planeta {f.Planeta}",
		TipoDeTarefa.Verba => $"DEPOSITAR {VerbaDoPresidente:N0} zeni no cofre da Terra (verb Rank Duty)",
		TipoDeTarefa.Servico =>
			$"SERVIÇO: {Math.Min(f.Progresso, f.Meta)}/{f.Meta} pontos (visitantes qualificados a "
			+ $"{TilesDoServico} tiles: vivos TREINANDO para os mestres, almas MORTAS para o Outro Mundo)",
		_ => "nenhuma",
	};

	/// <summary>"Earth" e nome de zona; "Terra" e o que se le. Mesmo par do `conq_pname`.</summary>
	private static string NomeVisivel(string zona) =>
		string.Equals(zona, "Earth", StringComparison.OrdinalIgnoreCase) ? "a Terra" : zona;

	/// <summary>
	/// AS JANELAS GRAVADAS NAO PODEM SER MAIORES QUE AS DE HOJE -- o `rq_clamp_window` (`:453`).
	///
	/// A ficha guarda INSTANTES ABSOLUTOS, entao encurtar o prazo no codigo nao encurta sozinho o que
	/// ja esta em disco: quem estava esperando a janela velha continuaria esperando por ela. Devolve
	/// `true` quando mexeu. Vale pra qualquer ajuste futuro do <see cref="DiasDeTarefa"/> -- e pro dia
	/// em que alguem mexer no comprimento do dia in-game.
	/// </summary>
	public static bool ApararJanela(FichaDeMissao f, double agora)
	{
		bool mexeu = false;
		if (f.Proxima > agora + SegundosDeIntervalo) { f.Proxima = agora + SegundosDeIntervalo; mexeu = true; }
		if (f.Tarefa != TipoDeTarefa.Nenhuma && f.Prazo > agora + SegundosDePrazo)
		{
			f.Prazo = agora + SegundosDePrazo;
			mexeu = true;
		}
		return mexeu;
	}
}
