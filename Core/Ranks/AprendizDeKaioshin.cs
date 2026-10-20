namespace Jandirus.Core.Ranks;

/// <summary>
/// ============================ O APRENDIZ DE KAIOSHIN (2026-10-19, pedido do dono) ============================
/// *"os verbos do Aprendiz de Kaioshin não dizem nada -- conserte"*. A skill `Kaioshin Apprenticeship` vinha no kit do Kaioshin
/// (`DadivaDeCargo`), com os dois verbos dela no censo como MUDOS (`CensoDeSkills`, "patronato"): o botao nao existia, e o aprendiz
/// era inalcancavel ate pelo admin. E o `Modules/Ranks/KaioshinApprentice.dm` do DM:
///   * o Kaioshin NO TRONO toma discipulos de QUALQUER raca pelo "Nomear Aprendiz" -- jogador vivo, de pe, sem cargo proprio e sem
///     mestre, a ate 4 tiles (`KSAP_RANGE`), que aceita ou recusa; ate 3 por Kaioshin (`KSAP_MAX`). Nao e trono: e um vinculo;
///   * o aprendiz ganha a ARVORE "Aprendiz de Kaioshin" (`skills.json`) -- Cura, Telepatia, Observar e Ver Mortos (`ksap_kit()`),
///     compradas com Marcos. Fica de fora o que e prerrogativa do cargo (Mistico, Reviver, Reencarnar...);
///   * o vinculo MORRE com o mestre fora do trono (vago, ou outro no lugar) e com o aprendiz assumindo um cargo proprio (a
///     formatura) -- e entao o kit comprado na arvore sai e os Marcos VOLTAM (`ksap_strip`): ninguem perde investimento;
///   * o "Dispensar Aprendiz" solta um discipulo, com o mesmo reembolso; o admin da e tira pelo painel.
///
/// O QUE O PORT MUDOU, E POR QUE (declarado no log da versao):
///   * o Kaioshin tem CINCO vagas aqui (`RankDef.Vagas`): cada um dos cinco guia ate 3, e o vinculo e com a CONTA do mestre -- vale
///     enquanto ela ocupar uma vaga de Kaioshin;
///   * a resposta do convidado nao trava a tela (o `alert()` do DM): e o cartao de pedido, com 60 s de prazo, como todo convite do port;
///   * o reembolso so devolve o que foi comprado PELO aprendizado: quem ja tinha a Telepatia pela arvore da raca fica com ela.
///
/// AQUI MORA SO A REGRA: os numeros, o kit e as perguntas. Os vinculos (em disco), os verbos, a arvore aberta pela regra do port e a
/// devolucao sao do servidor (`GameServer.AprendizDeKaioshin.cs`).
/// =================================================================================================================
/// </summary>
public static class AprendizDeKaioshin
{
	/// <summary>`KSAP_MAX`: aprendizes ao mesmo tempo por Kaioshin.</summary>
	public const int MaxPorMestre = 3;

	/// <summary>`KSAP_RANGE`: a distancia (tiles) pra nomear um discipulo.</summary>
	public const int TilesParaNomear = 4;

	/// <summary>Quanto o convite espera a resposta (o `alert()` do DM nao tinha prazo porque travava a tela).</summary>
	public const long MsDaOferta = 60_000;

	/// <summary>O cargo do mestre (`Supreme_Kai`).</summary>
	public const string CargoDoMestre = "kaioshin";

	/// <summary>O titulo de quem e aprendiz -- o que a aba People mostra no lugar do cargo.</summary>
	public const string Titulo = "Aprendiz de Kaioshin";

	/// <summary>A arvore do aprendiz (`skills.json`): nenhuma raca a tem; a regra do port a abre pra quem tem o vinculo.</summary>
	public const string Arvore = "/datum/skill/tree/Rank/KaioshinApprentice";

	/// <summary>`ksap_kit()`: o recorte do saber divino. O espelho dos galhos da arvore.</summary>
	public static readonly string[] Kit =
		["/datum/skill/ki/Heal", "/datum/skill/Telepathy", "/datum/skill/general/observe", "/datum/skill/rank/Dead"];

	/// <summary>As duas do kit que nascem APAGADAS no catalogo (`ligada 0`): a regra do port as acende na arvore do aprendiz.</summary>
	public static readonly string[] ApagadasNoCatalogo = ["/datum/skill/ki/Heal", "/datum/skill/rank/Dead"];

	/// <summary>Os comandos do canal de verbos (o censo os chama `PorOutroCanal`).</summary>
	public const string VerboNomear = "aprendiz_nomear", VerboDispensar = "aprendiz_dispensar",
		VerboAceitar = "aprendiz_aceitar", VerboRecusar = "aprendiz_recusar";

	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada `--aprendizteste`) -- falsos em jogo, sempre
	// =====================================================================

	/// <summary>DEFEITO INJETADO: o vinculo nao cai quando o mestre deixa o trono.</summary>
	public static bool VinculoEternoDeTeste;

	/// <summary>DEFEITO INJETADO: o Kaioshin toma um quarto aprendiz.</summary>
	public static bool SemLimiteDeTeste;

	/// <summary>DEFEITO INJETADO: a reconciliacao do kit de cargo apaga a Cura e o Ver Mortos do aprendiz -- o jogo de antes.</summary>
	public static bool KitApagadoDeTeste;

	/// <summary>DEFEITO INJETADO: o vinculo cai e o que foi comprado como aprendiz fica, sem Marco de volta.</summary>
	public static bool SemDevolucaoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (robo `--diagaprendiz`, a r1): a aba Learning de TODO jogador anuncia a arvore do aprendiz entre as que faltam
	/// abrir ("abre com: ..."), porque a regra que a abre mora na Strength of Spirit, que todo mundo tem.
	/// </summary>
	public static bool ArvoreAnunciadaDeTeste;

	/// <summary>DEFEITO INJETADO (`--aprendizteste`, a r1): o aceite diz tambem "o que voce treinou abriu um caminho novo" -- a arvore nao veio de treino.</summary>
	public static bool TreinoAnunciaDeTeste;

	// =====================================================================
	// A REGRA
	// =====================================================================

	/// <summary>A chave que o admin escreve no painel pra dar ou tirar o aprendizado (`Rewards.dm:302-305`).</summary>
	public static bool EAChaveDoAdmin(string chave) =>
		chave.Trim().ToLowerInvariant() is "aprendiz" or "aprendiz de kaioshin" or "kaioshin apprentice";

	/// <summary>
	/// POR QUE ESTE KAIOSHIN NAO PODE TOMAR ESTE DISCIPULO AGORA (`Nomear_Aprendiz`, `KaioshinApprentice.dm:315-336`). Nulo = pode.
	/// </summary>
	public static string? PorQueNaoNomeia(bool mestreNoTrono, int quantosJaGuia, bool alvoEGente, bool alvoDePe, bool alvoTemCargo,
										  bool alvoJaTemMestre) =>
		!mestreNoTrono ? "só o Kaioshin no trono toma discípulos"
		: quantosJaGuia >= MaxPorMestre && !SemLimiteDeTeste ? $"você já guia {MaxPorMestre} aprendizes -- dispense um antes de tomar outro"
		: !alvoEGente ? "só um jogador pode ser aprendiz"
		: !alvoDePe ? "o discípulo precisa estar vivo e de pé"
		: alvoTemCargo ? "quem já carrega um cargo não é aprendiz -- é colega"
		: alvoJaTemMestre ? "essa alma já tem um mestre Kaioshin"
		: null;

	/// <summary>
	/// POR QUE O VINCULO CAIU (`ksap_bond_reason`, `:53-60`) -- nulo = de pe. A mesma triagem pro tique e pra quem volta ao jogo.
	/// </summary>
	public static string? MotivoDaQueda(bool mestreAindaNoTrono, bool algumKaioshinNoTrono, bool aprendizTemCargo) =>
		!mestreAindaNoTrono && !VinculoEternoDeTeste
			? (algumKaioshinNoTrono ? "o Kaioshin que te iniciou não ocupa mais o trono" : "o trono do Kaioshin está vago")
		: aprendizTemCargo ? "você assumiu um cargo próprio e está formado"
		: null;
}
