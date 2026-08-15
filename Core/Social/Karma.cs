namespace Jandirus.Core.Social;

/// <summary>
/// ============================ O ALINHAMENTO MORAL -- a porta que estava trancada por dentro ============================
/// Porte de `Code/Modules/NPCs/SkyNPCs.dm:96-146`, o bloco "ALINHAMENTO / KARMA" do original.
///
/// **O QUE ESTE ARQUIVO CONSERTA, E POR QUE ELE E URGENTE.** O campo `ServerPlayer.Karma` existia,
/// era lido por TRES sistemas -- os requisitos de cargo (`Ranks.cs`, nove cargos), a escolha de alvo
/// das tarefas (`MissoesDeCargo.ServeDeAlvo`) e o duelo de titulo (`GameServer.CargoDuelo.cs`) -- e
/// tinha **um unico produtor**: `+5 por tarefa de cargo cumprida`
/// (`GameServer.CargoMissoes.cs:499`). Tarefa de cargo so existe pra quem JA TEM CARGO.
///
/// Isso fecha um circulo: pra tomar o cargo e preciso karma, e pra ganhar karma e preciso o cargo.
/// Com o campo comecando em zero e sem nenhuma outra fonte, **nove dos cargos reivindicaveis eram
/// inalcancaveis** -- Eremita Tartaruga (25), Korin (25), Guardiao da Terra (50), Grande Anciao de
/// Namek (25), os quatro Kaios cardeais (50), Rei Yemma (50) e Kaioshin (75) pelo lado bom; Lorde
/// Demonio (-50) e Rei Makyo (-50) pelo lado podre. O sistema de cargos compilava, tinha bancada
/// verde e recusava todo mundo pra sempre.
///
/// E havia a segunda metade do mesmo buraco: **o karma nunca foi pro disco**. Ele nao estava no
/// `CharacterSave`, entao o pouco que se ganhava morria no logout. O original e explicito na
/// declaracao -- `karma = 0 //alinhamento moral (PERSISTENTE: sem tmp)` (`SkyNPCs.dm:100`), e o
/// comentario existe justamente porque ao lado dele estao tres campos que SAO `tmp`.
/// ================================================================================================================
///
/// ============================ ELE MORA NO CORE PORQUE E UMA TABELA, NAO UM EVENTO ============================
/// O que muda o karma sao quatro fatos do mundo, e cada um ja tem funil proprio no servidor (a
/// derrota, a morte de habitante, o chefe de saga derrubado, a tarefa de cargo). O que NAO podia
/// ficar espalhado por esses quatro e a ARITMETICA: o teto, o piso, e a pergunta "a vitima era
/// inocente?". Quatro copias do `Math.Clamp(-100, 100)` sao quatro lugares pra alguem esquecer o
/// piso -- e o defeito apareceria como "matei dez inocentes e continuo podendo ser Guardiao".
/// ========================================================================================================
/// </summary>
public static class Karma
{
	/// <summary>
	/// O PISO E O TETO. Os dois estao escritos LITERALMENTE em cada uma das quatro contas do
	/// original (`min(karma + X, 100)` / `max(karma - X, -100)`, `SkyNPCs.dm:113-146`) -- o DM nao
	/// tem constante pra eles, e e por isso que a primeira coisa que este arquivo faz e dar uma.
	/// </summary>
	public const int Piso = -100, Teto = 100;

	/// <summary>
	/// O NEUTRO. Nao e so "zero": e o valor com que toda alma nasce e o valor pra onde o Enma
	/// devolve quem cumpriu pena no Inferno (`karma = 0 //alma limpa`, `SkyNPCs.dm:232`).
	///
	/// **E E O CORTE MORAL DO JOGO**: abaixo dele a alma e "coracao maligno" pro julgamento e vira
	/// presa legitima do protetor; nele ou acima, a escola do Grou ainda aceita (`karma <= 0`).
	/// </summary>
	public const int Neutro = 0;

	/// <summary>
	/// MATAR UM JOGADOR: 20 pontos, pros dois lados (`gain_kill_karma`, `SkyNPCs.dm:113-116`).
	///
	/// O numero e o mesmo nas duas direcoes de proposito no original, e a simetria e o desenho: cinco
	/// assassinatos de inocente levam do neutro ao fundo do poco, e cinco cacadas de vilao trazem de
	/// volta. Nenhuma reputacao do jogo se move tao rapido -- e nenhuma devia, porque esta e a unica
	/// que se paga com a vida de outra pessoa.
	/// </summary>
	public const int PorMatarJogador = 20;

	/// <summary>
	/// `KARMA_NPC_INNOCENT_LOSS = 5` (`SkyNPCs.dm:97`): o que custa cortar um transeunte.
	///
	/// QUATRO VEZES MENOS QUE UM JOGADOR, e o original explica a razao no comentario do ganho
	/// simetrico: habitante "e ilimitado" (o povoamento repoe), entao um valor alto viraria fazenda
	/// -- de santidade num sentido, de maldade no outro.
	/// </summary>
	public const int PorMatarInocente = 5;

	/// <summary>
	/// `KARMA_BOSS_GAIN = 30` (`SkyNPCs.dm:98`): derrubar um chefe de saga.
	///
	/// E o maior ganho unico do jogo, e o unico que nao exige matar gente: um servidor inteiro pode
	/// virar heroi sem que ninguem assassine ninguem. E o caminho que o Eremita Tartaruga (karma 25+)
	/// pede -- duas sagas e o cargo abre.
	/// </summary>
	public const int PorDerrotarChefe = 30;

	/// <summary>SEMPRE DENTRO DA ESCALA. Uma casa so -- somar e aparar sao a mesma operacao.</summary>
	public static int Somar(int atual, int delta) => Math.Clamp(atual + delta, Piso, Teto);

	/// <summary>
	/// ============================ MATEI ALGUEM: GANHO OU PERCO? ============================
	/// `gain_kill_karma` (`SkyNPCs.dm:108-116`), e a pergunta inteira e sobre A VITIMA:
	///
	///     if(victim.karma &lt; 0 || victim.isVillain)   // matou um VILAO   -> +20
	///     else                                        // matou um INOCENTE -> -20
	///
	/// **AS DUAS METADES DO `||` NAO SAO A MESMA COISA**, e por isso as duas estao aqui: `karma &lt; 0`
	/// e o que a alma FEZ (ela mesma matou inocentes); `isVillain` e o selo que o admin poe na mao,
	/// e ele existe pro vilao de roteiro que ainda nao sujou a ficha. Portar so a primeira faria o
	/// cacador de um vilao selado e de ficha limpa ser punido como assassino.
	///
	/// Repare no que ISTO NAO PERGUNTA: quem comecou, quem era mais forte, se era duelo combinado.
	/// O original tambem nao pergunta. Matar e matar -- a unica coisa que pesa e quem era o morto.
	/// ====================================================================================
	/// </summary>
	/// <param name="karmaDaVitima">O karma de quem morreu, no instante da morte.</param>
	/// <param name="vitimaSelada">O `isVillain` dela -- o selo do admin.</param>
	/// <returns>O delta a aplicar em quem matou: +20 ou -20.</returns>
	public static int PorMorteDeJogador(int karmaDaVitima, bool vitimaSelada) =>
		karmaDaVitima < Neutro || vitimaSelada ? +PorMatarJogador : -PorMatarJogador;

	/// <summary>
	/// O NOME DA FAIXA, pro jogador conseguir ler a propria ficha.
	///
	/// **ISTO NAO ESTA NO DM**, e a ausencia la e um defeito conhecido: o original so mostra o numero
	/// cru dentro das falas do Enma e do Sr. Kaioh, e o jogador que nunca morreu nao tem como saber
	/// que este eixo existe. Os limiares NAO sao inventados -- sao os que os cargos ja cobram
	/// (`Ranks.cs`) e os que as tarefas ja usam (`MissoesDeCargo.KarmaDeHeroi` = 50,
	/// `KarmaDeAmeaca` = -50); o que se acrescenta e o rotulo.
	/// </summary>
	public static string Faixa(int karma) => karma switch
	{
		>= 100 => "IMACULADO",
		>= 50 => "herói",
		>= 25 => "bondoso",
		> Neutro => "decente",
		Neutro => "neutro",
		> -25 => "duvidoso",
		> -50 => "cruel",
		> -100 => "MALIGNO",
		_ => "ABOMINÁVEL",
	};
}
