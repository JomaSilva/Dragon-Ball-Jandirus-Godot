namespace Jandirus.Core.Npc;

/// <summary>
/// OS VILOES AVULSOS -- as "quests de viloes (fora das sagas)" do item 12 do playtest de 2026-10-10:
///
///   *"de tempos em tempos aparecem no planeta NPCs viloes; derrotar ou matar da karma positivo. Versao
///   minima: intervalo aleatorio, spawn em local valido (terra firme; reaproveite o spawn de NPCs se existir),
///   aviso para os jogadores do planeta, despawn depois de um tempo, limite de eventos simultaneos, forca e
///   karma configuraveis. Padrao: o karma vai para quem derrotou ou matou. Mantenha separado do sistema de
///   sagas."*
///
/// AQUI FICAM OS NUMEROS, todos com nome, e a conta do relogio. Quem poe o corpo no mundo, avisa o planeta e
/// paga o karma e o servidor (`GameServer.Viloes.cs`).
///
/// ============================ O QUE NAO E DAQUI ============================
///   * A FORCA do vilao e do MOLDE (`npcs.json`, molde <see cref="Molde"/>): o `bpRelativo` o faz nascer em
///     torno da media de BP dos jogadores VIVOS DO PLANETA onde ele aparece (1.0 = a media, +-15%), com o
///     `bpPiso` por baixo. Mudar a forca e editar aquelas duas linhas -- e dado, nao codigo. Quem conta a media
///     e o servidor (`GameServer.MediaDoPlaneta`): ate 2026-10-23 era a do servidor inteiro, e o dono mudou --
///     *"deveriam escalar com o poder do planeta atual e nao do server, galera novata tava apanhando mt"*.
///   * NAO E SAGA. O vilao e um molde do tipo <see cref="TipoDeNpc.Vilao"/>, sem estagios e sem roteiro: nada
///     aqui le ou escreve a cadeia de sagas, e um chefe de saga de pe no planeta nao impede nem atrasa um
///     vilao.
///   * O ORIGINAL NAO TEM ISTO. O que ha no DM de parecido e o karma por matar NPC hostil
///     (`gain_enemy_npc_kill_karma`, +5) e por derrubar chefe de saga (+30); o valor daqui fica entre os dois.
/// ===========================================================================
/// </summary>
public static class VilaoAvulso
{
	/// <summary>A chave geral. Desligada, nenhum vilao novo nasce (os que estao de pe seguem ate o prazo).</summary>
	public static bool Ligado = true;

	/// <summary>O molde do `npcs.json` que nasce. Tem que ser do tipo <see cref="TipoDeNpc.Vilao"/>.</summary>
	public const string Molde = "vilao_avulso";

	/// <summary>
	/// O INTERVALO ALEATORIO, por planeta com jogador: entre um vilao (nascido, derrotado ou ido embora) e o
	/// proximo passam de <see cref="IntervaloMinS"/> a <see cref="IntervaloMaxS"/> segundos.
	/// </summary>
	public const double IntervaloMinS = 12 * 60, IntervaloMaxS = 30 * 60;

	/// <summary>Quanto tempo o vilao fica antes de ir embora sozinho, se ninguem o derrotar.</summary>
	public const double DuracaoS = 10 * 60;

	/// <summary>Quantos eventos cabem ao mesmo tempo no servidor inteiro, e em cada planeta.</summary>
	public const int MaxSimultaneos = 3, MaxPorPlaneta = 1;

	/// <summary>O karma de quem derrota (nocauteia) OU mata o vilao. Positivo: e fazer o bem.</summary>
	public const int KarmaPorDerrotar = 10;

	/// <summary>
	/// (2026-10-25) ...E A REPUTACAO COM O PLANETA em que ele apareceu -- o dono: *"faca vencer viloes q aparecem no planeta ao serem
	/// derrotados alem de karma ganha reputacao com o planeta"*. 15 e o "amigavel" do planeta numa derrota so
	/// (`Reputacao.LimiarDeAmigavel`), e o "heroi" (50) depois de quatro; o chefe de saga, que e outra escala de ameaca, vale 100
	/// (`Reputacao.HeroiPorChefe`).
	/// </summary>
	public const double ReputacaoPorDerrotar = 15;

	/// <summary>
	/// A que distancia de um jogador o vilao aparece, em tiles: longe o bastante pra nao nascer em cima de
	/// ninguem, perto o bastante pra ser achado (a IA dele persegue quem entra em 20 tiles).
	/// </summary>
	public const float DistanciaMinTiles = 10, DistanciaMaxTiles = 30;

	/// <summary>
	/// (2026-10-23) DE ONDE E O VILAO QUE APARECE NUM MUNDO GERADO. O molde nao crava `racas` -- a raca dele e a de quem nasce no
	/// planeta (`Bercos.RacasNascidasEm`) --, e num mundo gerado NINGUEM nasce: a tabela do berco so conhece mapa feito a mao. A pool
	/// vazia estourava o sorteio (o `SorteioDeNpc.Sortear` recusa pool vazia, de proposito) DENTRO do tique dos viloes, a cada 12 a 30
	/// minutos, em todo mundo gerado com gente: o vilao nunca nascia la, e o resto daquele tique ficava pra tras.
	///
	/// Ele vem DE FORA: e o ALIEN, a raca sem lar do DM (os spawnpoints dela sao lugares de passagem -- ver `Bercos.PlanetaNatal`) e a
	/// de Zarbon, Dodoria, Cui e Appule (ele se veste com a armadura da tropa do Freeza, `RoupaDeNpc`). ESCOLHA MINHA: o dono nao falou
	/// de mundo gerado. Pra mudar, e esta lista (raca que nao existe no `races.json` e pulada).
	/// </summary>
	public static readonly string[] RacasDoForasteiro = ["Alien"];

	/// <summary>
	/// DEFEITO INJETADO (bancada `--vilaoteste`): o vilao de mundo gerado volta a nascer sem raca -- a pool vazia e o sorteio que
	/// estoura, o jogo de antes de 2026-10-23. Lido pela propria linha que da a raca do forasteiro (`SorteioDeNpc.Sortear`). Falso em
	/// jogo, sempre.
	/// </summary>
	public static bool ForasteiroSemRacaDeTeste;

	/// <summary>O intervalo sorteado: <paramref name="sorteio"/> e um numero em [0,1).</summary>
	public static double ProximoIntervaloS(double sorteio) =>
		IntervaloMinS + (IntervaloMaxS - IntervaloMinS) * Math.Clamp(sorteio, 0, 1);

	/// <summary>Cabe mais um evento agora? As duas contas do limite, juntas.</summary>
	public static bool Cabe(int noServidor, int noPlaneta) => noServidor < MaxSimultaneos && noPlaneta < MaxPorPlaneta;
}
