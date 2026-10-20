namespace Jandirus.Core.Ranks;

/// <summary>
/// ============================ O TRONO DE QUEM SUMIU VAGA SOZINHO (dono, 2026-10-19) ============================
/// O pedido: *"fazer com q todos os cargos do jogo tenham requisitos e n precisem de adm (tirando o anjo)"*. Requisito resolve a
/// ENTRADA -- mas um cargo de quem largou o jogo nunca vagava: os deveres congelam com o dono offline (`TickDasMissoes`, o
/// `rq_loop` do DM faz igual), e onze cargos nem deveres tem. So o admin destravava. Perguntado, o dono escolheu: **7 dias sem
/// entrar e o cargo vaga**, pra todos menos o Anjo.
///
/// O PRECEDENTE DO DM e o nucleo da Geti Star: quem a larga sozinha perde o controle dela pro proximo que clicar ("USER
/// ABANDONMENT PROTOCOL ACTIVATED", `Geti Star.dm:95-115`). Cargo nenhum do DM tinha isso.
///
/// A AUSENCIA SO CONTA COM O SERVIDOR NO AR. Medir pelo "visto por ultimo" da conta tiraria o cargo de todo mundo depois de uma
/// semana de servidor desligado -- quando ninguem podia entrar. Entao o servidor soma a ausencia do dono a cada volta, e o passo
/// de uma volta tem teto (<see cref="TetoDoPasso"/>): processo suspenso (a maquina dormiu) tambem nao e o dono sumindo.
/// =============================================================================================================
/// </summary>
public static class AbandonoDeCargo
{
	/// <summary>Quantos dias (de servidor no ar) sem o dono entrar ate o cargo vagar. A escolha do dono.</summary>
	public const int DiasSemEntrar = 7;

	/// <summary><see cref="DiasSemEntrar"/> em milissegundos.</summary>
	public const long MsSemEntrar = DiasSemEntrar * 24L * 60 * 60 * 1000;

	/// <summary>De quanto em quanto tempo o servidor soma as ausencias (ms). A regra e de dias: dez minutos de folga nao pesam.</summary>
	public const long MsEntreVoltas = 10L * 60 * 1000;

	/// <summary>O maior passo que uma volta soma (ms): duas voltas. Alem disso o processo esteve parado, e isso nao conta.</summary>
	public const long TetoDoPasso = 2 * MsEntreVoltas;

	/// <summary>O cargo que nao vaga por ausencia: o Anjo -- o unico que so o admin da (o dono: *"tirando o anjo"*).</summary>
	public const string Isento = "angel";

	/// <summary>
	/// DEFEITO INJETADO (bancada `--cargolivreteste`): a ausencia nao tira cargo nenhum -- o jogo de antes, em que so o admin
	/// esvaziava o trono de quem sumiu. Lido pelo <see cref="Vence"/>. Falso em jogo.
	/// </summary>
	public static bool AbandonoIgnoradoDeTeste;

	/// <summary>Quanto a volta de agora soma: o tempo desde a anterior, com o teto. A primeira volta depois do boot soma zero.</summary>
	public static long Passo(long voltaAnteriorMs, long agoraMs) =>
		voltaAnteriorMs <= 0 ? 0 : Math.Clamp(agoraMs - voltaAnteriorMs, 0, TetoDoPasso);

	/// <summary>Esta ausencia ja tira o cargo?</summary>
	public static bool Vence(long ausenciaMs) => !AbandonoIgnoradoDeTeste && ausenciaMs >= MsSemEntrar;

	/// <summary>Este cargo vaga por ausencia? Todos menos o <see cref="Isento"/>.</summary>
	public static bool Vaga(string cargo) => !string.Equals(cargo, Isento, StringComparison.OrdinalIgnoreCase);
}
