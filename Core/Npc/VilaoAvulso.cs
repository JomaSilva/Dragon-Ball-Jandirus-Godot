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
///     torno da media de BP dos jogadores online (1.0 = a media, +-15%), com o `bpPiso` por baixo. Mudar a
///     forca e editar aquelas duas linhas -- e dado, nao codigo.
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
	/// A que distancia de um jogador o vilao aparece, em tiles: longe o bastante pra nao nascer em cima de
	/// ninguem, perto o bastante pra ser achado (a IA dele persegue quem entra em 20 tiles).
	/// </summary>
	public const float DistanciaMinTiles = 10, DistanciaMaxTiles = 30;

	/// <summary>O intervalo sorteado: <paramref name="sorteio"/> e um numero em [0,1).</summary>
	public static double ProximoIntervaloS(double sorteio) =>
		IntervaloMinS + (IntervaloMaxS - IntervaloMinS) * Math.Clamp(sorteio, 0, 1);

	/// <summary>Cabe mais um evento agora? As duas contas do limite, juntas.</summary>
	public static bool Cabe(int noServidor, int noPlaneta) => noServidor < MaxSimultaneos && noPlaneta < MaxPorPlaneta;
}
