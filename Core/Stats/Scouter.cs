namespace Jandirus.Core.Stats;

/// <summary>
/// ============================ O QUE O SCOUTER CONSEGUE LER (2026-10-18) ============================
/// O dono: *"coloque um bp maximo no scouter de 100m de bp, acima disso o scouter marca com ???, e pessoas q estao em formas com godki, ou
/// estao em constante uso de ki divino (deuses da destruiçao, anjos, supremo kaioshins npcs e golden apple) n podem ter o poder lido com
/// scouter ou ter o ki sentido. somente pessoas q tb tem ki divino podem dar sense em outras pessoas com ki divino, scouter nunca consegue pq
/// o usuario n influencia no q o scouter sente"* -- e *"lembrando q scouter NUNCA consegue detectar ou sentir ki divino"*.
///
/// AS DUAS REGRAS SAO DO APARELHO, e nao de quem o usa:
///   * O TETO: acima de <see cref="TetoDeLeitura"/> o vidro marca "???". Cada numero e julgado sozinho -- o expresso e o base da aba Stats,
///     o antes e o depois de uma frase de ganho: o que passa do teto vira "???", e a frase que o diria cai pra leitura sem numero;
///   * O KI DIVINO: o scouter nao o DETECTA. Quem esta numa forma que usa o ki divino, ou que o usa sempre
///     (`DespertarDivino.KiDivinoEmUso`), nao aparece no Scan e nao tem o poder lido -- nem o proprio, pelo proprio aparelho. Quem usa o
///     scouter pode ser um deus, e o vidro continua sem ver ki divino nenhum. Quem decide quem e divino e o servidor
///     (`GameServer.KiDivinoEscondido`), que conhece cargo e papel de NPC.
///
/// DIVERGENCIA DECLARADA (o pedido do dono): o scouter do DM le qualquer poder (`HtmlUI.dm:400-419`, `scouteron`), sem teto e sem excecao.
/// ====================================================================================================
/// </summary>
public static class Scouter
{
	/// <summary>Ate quanto o scouter le: cem milhoes. Acima disso, "???".</summary>
	public const double TetoDeLeitura = 100_000_000;

	/// <summary>O vidro mostra este numero? So dentro do teto -- e NaN (o que ja chegou sem leitura) nao e numero.</summary>
	public static bool Le(double bp) => SemTetoDeTeste ? !double.IsNaN(bp) : bp <= TetoDeLeitura;

	/// <summary>DEFEITO INJETADO (`--sentidosteste`): o scouter volta a ler qualquer poder, sem teto -- o jogo de antes. Falso em jogo, sempre.</summary>
	public static bool SemTetoDeTeste;
}
