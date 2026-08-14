namespace Jandirus.Core.Social;

/// <summary>
/// A REPUTACAO DE UM PLANETA COM UMA PESSOA -- a escala, os limiares e os nomes deles.
///
/// ============================ POR QUE ELA NASCE AGORA, E SO ESTE PEDACO ============================
/// Ela entra porque a recompensa da cadeia de sagas E ela: *"a recompensa e reputacao de heroi
/// (+100)"*, e o original paga isso com `planet_rep_add(planet, K, BEV_HERO_REP, "boss-slain")`
/// (BossEvents.dm:259). Sem o livro-caixa, "matar o Freeza da +100 de reputacao" seria um numero que
/// nao vai pra lugar nenhum.
///
/// E entra SO O LIVRO-CAIXA. O `PlanetReputation.dm` inteiro e o Sistema 3 do original e tem muito
/// mais do que isto: cidadaos que fofocam sobre o heroi, cidadaos que CACAM quem passou do limiar
/// hostil, decaimento por dia in-game, penalidade por matar habitante. Nada disso esta aqui, e nada
/// disso esta escondido -- ver <see cref="OqueFalta"/>, que e uma lista e nao um comentario, pelo
/// mesmo motivo do <c>RankDef.Pendencias</c>: um sistema pela metade que nao diz onde acaba vira um
/// sistema que ninguem termina.
/// ================================================================================================
///
/// ============================ E ELA JA ERA DEVIDA A OUTRO SISTEMA ============================
/// Quatro cargos do `Ranks.cs` tem a pendencia escrita em texto -- *"ser HEROI do povo da Terra
/// (reputacao 50+) -- RankQuests.dm:218; o port nao tem reputacao de planeta"*. O limiar de heroi
/// desta classe e o mesmo 50, e por isso ele mora aqui e nao num numero solto dentro da cadeia de
/// sagas: o dia em que os cargos forem cobrar, eles cobram DESTE limiar.
/// ========================================================================================
/// </summary>
public static class Reputacao
{
	/// <summary>Piso e teto da escala -- `REP_MIN` / `REP_MAX` (PlanetReputation.dm:25-26).</summary>
	public const double Piso = -200, Teto = 200;

	/// <summary>`REP_HERO`. O mesmo 50 que quatro cargos ja pedem em texto no `Ranks.cs`.</summary>
	public const double LimiarDeHeroi = 50;

	/// <summary>`REP_VILLAIN` (mora no `1A Defines.dm` do original, nao no arquivo da reputacao).</summary>
	public const double LimiarDeVilao = -30;

	/// <summary>`REP_HOSTILE`: daqui pra baixo o povo caca a pessoa. Ainda nao ha quem cace -- ver <see cref="OqueFalta"/>.</summary>
	public const double LimiarDeCacado = -60;

	/// <summary>
	/// O piso da faixa "Amigavel" do `planet_rep_label`. Ele era um 15 solto dentro da
	/// <see cref="Faixa"/>; virou constante porque TRES cargos passaram a cobra-lo (Korin, Presidente
	/// e Grande Anciao pedem "Amigavel com o povo de X"), e um numero repetido em quatro lugares e
	/// quatro lugares pra ele envelhecer diferente.
	/// </summary>
	public const double LimiarDeAmigavel = 15;

	/// <summary>`BEV_HERO_REP`: o que derrotar um chefe de saga vale (BossEvents.dm:92).</summary>
	public const double HeroiPorChefe = 100;

	/// <summary>Sempre dentro da escala. Uma unica casa -- somar e cortar sao a mesma operacao.</summary>
	public static double Somar(double atual, double delta) =>
		Math.Clamp(atual + delta, Piso, Teto);

	/// <summary>
	/// O NOME DA FAIXA -- `planet_rep_label` (PlanetReputation.dm:67), na ordem dele. A ordem importa:
	/// "CACADO" e "Inimigo" se sobrepoem, e quem testa primeiro decide qual das duas o jogador le.
	/// </summary>
	public static string Faixa(double pontos) =>
		pontos >= LimiarDeHeroi ? "HERÓI do povo"
		: pontos >= LimiarDeAmigavel ? "Amigável"
		: pontos <= LimiarDeCacado ? "CAÇADO pelo povo"
		: pontos <= LimiarDeVilao ? "Inimigo do povo"
		: pontos <= -10 ? "Malvisto"
		: "Neutro";

	/// <summary>
	/// A FRASE DA TRAVESSIA DE LIMIAR, ou vazio quando nenhum foi cruzado. Sao os tres avisos do
	/// `planet_rep_add` (:86-91), e eles existem porque reputacao e um numero que ninguem ve: sem o
	/// aviso, virar heroi (ou virar caca) e uma coisa que acontece com o jogador sem ele saber.
	/// </summary>
	public static string Travessia(double antes, double depois, string planeta) =>
		antes > LimiarDeCacado && depois <= LimiarDeCacado
			? $"O povo de {planeta} agora te CAÇA."
		: antes > LimiarDeVilao && depois <= LimiarDeVilao
			? $"Sua fama em {planeta} apodrece... o povo sussurra seu nome com medo e ódio."
		: antes < LimiarDeHeroi && depois >= LimiarDeHeroi
			? $"O povo de {planeta} te considera um HERÓI!"
		: "";

	/// <summary>
	/// O QUE DO `PlanetReputation.dm` **NAO** ESTA AQUI. Lista, e nao comentario, porque o painel de
	/// admin e a bancada leem isto -- e porque a divida some quando so um comentario a registra.
	/// </summary>
	///
	/// SAIU DAQUI: *"perder reputacao por matar habitante"* (`planet_rep_on_citizen_killed`, :115).
	/// Ele existe desde a conquista -- ver `GameServer.MorreuUmCorpoSemDono`, que e o gancho unico de
	/// morte de corpo sem dono e onde o defensor de invasao PULA o generico pra usar o delta dele.
	public static readonly string[] OqueFalta =
	[
		"ganhar reputação matando quem o povo odeia (planet_rep_on_player_kill, :146)",
		"decaimento de 1 ponto por dia in-game (planet_rep_decay_loop, :262)",
		"cidadão que ataca à vista quem está abaixo de CAÇADO (REP_HOSTILE, :24)",
		"fofoca dos habitantes sobre o herói e sobre o vilão do planeta (:34-35)",
	];
}
