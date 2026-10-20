namespace Jandirus.Core.Ranks;

/// <summary>
/// ============================ A PROVA DO ESPIRITO DO CARGO (2026-10-19, pedido do dono) ============================
/// *"conserte os 3 e faça a prova do espirito"*. O `Ranks.cs` carregava a divida escrita desde que a reivindicacao entrou: *"O QUE
/// AINDA NAO ESTA AQUI: a PROVA contra o Espirito do Cargo"*. Sem ela o "Reivindicar" dava o trono na hora -- e os 250.000 zeni da
/// campanha do Presidente, que o DM cobra AO VENCER a prova, nunca saiam do bolso de ninguem.
///
/// O DM (`Modules/Ranks/RankQuests.dm:256-416`):
///   * vago + requisitos -> o ESPIRITO DO CARGO e invocado ao lado de quem reivindica, a 1,15x do PODER EXPRESSO dele (`:48`, `:346`),
///     palido e de aureola (`:343-344`), e caca so ele (`foundTarget(M)`, `:353`);
///   * o espirito CAIU (nocauteado ou morto) = o cargo e seu (`:358`); quem reivindica caiu, morreu fora do Outro Mundo, trocou de
///     mapa, se afastou mais de 25 tiles, saiu do jogo ou deixou passar os 5 min = 30 min de "vergonha" antes de tentar de novo
///     (`:361`, `:371`);
///   * uma alma por vez enfrenta o espirito de cada cargo (`rq_trial_busy`, `:317`);
///   * ao vencer: o Presidente paga a campanha (`:405`) e o mundo ouve.
///
/// AQUI MORA SO A REGRA: os numeros, o juiz (o que encerra a prova e como) e as frases. O corpo do espirito, o tique e a porta de
/// producao sao do servidor (`GameServer.ProvaDoCargo.cs`).
///
/// O QUE O PORT MUDOU, E POR QUE (declarado no log da versao):
///   * o espirito ganha os ATRIBUTOS de quem o invocou (como a simulacao do Simulador) -- no DM ele nascia com os de um NPC comum, e
///     com eles os "15% acima" eram so o numero do BP: o jogador treinado o derrubava de um soco;
///   * ele nao tem tinta azul (o port nao tem tinta por corpo): e um morto DE PASSAGEM -- de pe, meio transparente, de aureola;
///   * o nome que o cliente desenha e "Espírito do Cargo" (o cliente le no maximo 24 letras de nome: "Espírito de Guardião da Terra"
///     chegaria vazio); o cargo vai no anuncio;
///   * quem DERRUBA o espirito tem de ser quem o invocou: derrubado por outro, a prova nao vale (sem vergonha). O DM dava o cargo a
///     quem estivesse sendo testado, fosse quem fosse que batesse -- com um amigo do lado a prova virava formalidade. O desafio ao
///     Bills (`GameServer.BillsTreino.cs`, o "intrometido") ja e assim;
///   * a vergonha e por ASSINATURA e sobrevive ao relog (no DM era `tmp`: sair e entrar zerava os 30 min).
/// =================================================================================================================
/// </summary>
public static class ProvaDoCargo
{
	/// <summary>`RQ_TRIAL_FACTOR`: o poder EXPRESSO do espirito e o de quem o invocou, NA HORA, vezes isto.</summary>
	public const double FatorDoEspirito = 1.15;

	/// <summary>`RQ_TRIAL_TIMEOUT 3000` (ticks de 0,1 s): 5 minutos pra derrubar o espirito.</summary>
	public const long MsDaProva = 5 * 60_000;

	/// <summary>`RQ_CLAIM_CD 18000`: 30 minutos de "vergonha" depois de perder.</summary>
	public const long MsDaVergonha = 30 * 60_000;

	/// <summary>`get_dist(M, S) > 25`: mais longe que isto do espirito e fugir da prova.</summary>
	public const double TilesDaFuga = 25;

	/// <summary>`rand(-2, 2)`: o espirito se ergue a ate 2 tiles de quem o invocou.</summary>
	public const int TilesDoNascimento = 2;

	/// <summary>O nome que o cliente desenha sobre o corpo (ate 24 letras -- ver o cabecalho).</summary>
	public const string NomeDoEspirito = "Espírito do Cargo";

	/// <summary>A frase do botao "Reivindicar" da aba Cargos: o `alert()` do DM (`:321`), escrito antes do clique.</summary>
	public const string FraseDoBotao =
		"a prova invoca o Espírito do Cargo, ~15% acima do seu poder expresso: derrube-o em 5 min e o cargo é seu. "
		+ "Caia, fuja ou deixe o tempo acabar e espere 30 min.";

	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada `--provacargoteste`) -- falsos em jogo, sempre
	// =====================================================================

	/// <summary>DEFEITO INJETADO: o "Reivindicar" da o trono na hora, sem espirito nenhum -- o jogo de antes.</summary>
	public static bool ProvaPuladaDeTeste;

	/// <summary>DEFEITO INJETADO: duas almas enfrentam o espirito do mesmo cargo ao mesmo tempo.</summary>
	public static bool SemTravaDeTeste;

	/// <summary>DEFEITO INJETADO: perder nao deixa vergonha -- da pra tentar de novo na hora.</summary>
	public static bool SemVergonhaDeTeste;

	/// <summary>DEFEITO INJETADO: o espirito derrubado por OUTRO vale como vitoria de quem o invocou.</summary>
	public static bool IntrusoValeDeTeste;

	/// <summary>DEFEITO INJETADO: o poder do espirito nao e recravado no tique -- o treino de toda ficha o faz subir.</summary>
	public static bool EspiritoSemCravoDeTeste;

	/// <summary>DEFEITO INJETADO: a campanha do Presidente nao e cobrada ao vencer -- o jogo de antes.</summary>
	public static bool CampanhaDeGracaDeTeste;

	// =====================================================================
	// A REGRA
	// =====================================================================

	/// <summary>O corpo do espirito: Namekuseijin pros cargos de Namek (`:342` -- o Kami era Namekuseijin), humano pro resto.</summary>
	public static string RacaDoEspirito(string chaveDoCargo) =>
		chaveDoCargo.EndsWith("elder", StringComparison.OrdinalIgnoreCase) || string.Equals(chaveDoCargo, "guardian", StringComparison.OrdinalIgnoreCase)
			? "Namekian" : "Human";

	/// <summary>O poder EXPRESSO do espirito pro de quem o invoca agora.</summary>
	public static double PoderDoEspirito(double expressoDeQuemInvoca) => Math.Max(1, Math.Round(expressoDeQuemInvoca * FatorDoEspirito));

	/// <summary>Minutos inteiros (pra cima) ate o instante -- nunca negativo.</summary>
	public static long MinutosAte(long ms, long agoraMs) => Math.Max(0, (ms - agoraMs + 59_999) / 60_000);

	/// <summary>O que o juiz olha a cada segundo de prova.</summary>
	public readonly record struct QuadroDaProva(
		bool EspiritoExiste, bool EspiritoCaido, int QuemDerrubou, int Desafiante, bool DesafianteNoJogo, bool DesafianteKo,
		bool DesafianteMorto, bool CargoDoAlem, bool MesmaZona, double DistanciaEmTiles, long AgoraMs, long AcabaEm);

	/// <summary>Como a prova acabou.</summary>
	public enum FimDaProva : byte
	{
		/// <summary>O espirito caiu pelas maos de quem o invocou: o cargo e dele.</summary>
		Venceu,

		/// <summary>O espirito caiu pelas maos de OUTRO: a prova nao vale (sem vergonha).</summary>
		Intruso,

		/// <summary>O corpo do espirito sumiu do mundo sem cair: a prova nao vale (sem vergonha).</summary>
		Sumiu,

		/// <summary>Quem reivindica foi nocauteado.</summary>
		Caiu,

		/// <summary>Quem reivindica morreu, e o cargo e do mundo dos vivos.</summary>
		Morreu,

		/// <summary>Quem reivindica trocou de mapa.</summary>
		SaiuDoLugar,

		/// <summary>Quem reivindica se afastou mais de 25 tiles do espirito.</summary>
		Fugiu,

		/// <summary>Os 5 minutos passaram.</summary>
		Tempo,

		/// <summary>Quem reivindica saiu do jogo.</summary>
		SaiuDoJogo,
	}

	/// <summary>
	/// O JUIZ, na ordem do DM (`:358`, `:361`): o espirito caido decide primeiro; depois quem reivindica -- no jogo, de pe, vivo (ou
	/// morto num cargo do Outro Mundo, `RQ_SKY`), no mesmo mapa, perto, e dentro do prazo. Nulo = a prova continua.
	/// </summary>
	public static FimDaProva? Julgar(QuadroDaProva q)
	{
		if (!q.EspiritoExiste) return FimDaProva.Sumiu;
		if (q.EspiritoCaido) return q.QuemDerrubou == q.Desafiante || IntrusoValeDeTeste ? FimDaProva.Venceu : FimDaProva.Intruso;
		if (!q.DesafianteNoJogo) return FimDaProva.SaiuDoJogo;
		if (q.DesafianteKo) return FimDaProva.Caiu;
		if (q.DesafianteMorto && !q.CargoDoAlem) return FimDaProva.Morreu;
		if (!q.MesmaZona) return FimDaProva.SaiuDoLugar;
		if (q.DistanciaEmTiles > TilesDaFuga) return FimDaProva.Fugiu;
		if (q.AgoraMs > q.AcabaEm) return FimDaProva.Tempo;
		return null;
	}

	/// <summary>A derrota que deixa vergonha (30 min). A prova que nao valeu (intruso, espirito sumido) nao deixa.</summary>
	public static bool Envergonha(FimDaProva fim) => fim is not (FimDaProva.Venceu or FimDaProva.Intruso or FimDaProva.Sumiu);

	/// <summary>O que quem reivindicou le quando a prova acaba sem o cargo.</summary>
	public static string FraseDoFim(FimDaProva fim, string nomeDoCargo) => fim switch
	{
		FimDaProva.Intruso => $"alguém derrubou o Espírito de {nomeDoCargo} por você -- a prova não vale. Pode tentar de novo quando quiser.",
		FimDaProva.Sumiu => $"o Espírito de {nomeDoCargo} se dissipou -- a prova não vale. Pode tentar de novo quando quiser.",
		_ => $"{Motivo(fim)}: o espírito se desfaz em risos... você NÃO estava à altura do cargo de {nomeDoCargo}. "
			 + $"(de novo em {MsDaVergonha / 60_000} min)",
	};

	private static string Motivo(FimDaProva fim) => fim switch
	{
		FimDaProva.Caiu => "você caiu",
		FimDaProva.Morreu => "você morreu",
		FimDaProva.SaiuDoLugar => "você saiu do lugar da prova",
		FimDaProva.Fugiu => "você fugiu da prova",
		FimDaProva.Tempo => "o tempo da prova acabou",
		FimDaProva.SaiuDoJogo => "você saiu do jogo",
		_ => "a prova acabou",
	};
}
