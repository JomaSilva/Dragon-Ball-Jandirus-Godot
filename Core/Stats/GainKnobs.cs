namespace Jandirus.Core.Stats;

/// <summary>
/// TODOS os botoes de balanceamento do ganho de BP, num lugar so.
///
/// No BYOND eles estavam espalhados por `1A Defines.dm`, `BalanceSettings.dm`, `softcap.dm`
/// e `Gravity.dm`, e alguns so existiam como numero solto dentro da formula. Juntar aqui e
/// o que permite ajustar o ritmo do jogo sem cacar formula.
///
/// Sao estaticos porque no BYOND sao globais de verdade (um admin muda e vale pra todo mundo
/// no servidor). Quem manda neles e o SERVIDOR -- o cliente so recebe o resultado.
/// </summary>
public static class GainKnobs
{
    /// <summary>
    /// A TAXA GLOBAL. Multiplica praticamente todo ganho de BP do jogo: mexer aqui acelera
    /// ou freia o servidor inteiro de uma vez.
    /// </summary>
    public static double BPTick = 0.000056;

    /// <summary>Base FIXA de ganho. Substituiu o `relBPmax` nas formulas de treino -- com o
    /// relBPmax ali o treino compunha como juros e a Sala do Tempo virava bilhoes.</summary>
    public static double LinearGainBase = 1000;

    // --- multiplicadores globais por atividade (knobs de admin) ---------------
    public static double GlobalTrainGain = 1;
    public static double GlobalMedGain = 1;
    public static double GlobalSparGain = 1;
    public static double GlobalGravGain = 1;

    /// <summary>Quanto MAIS lento e o ganho por gravidade. Ganho por tick = gravidade/isto.</summary>
    public static double GravGainDiv = 1500;

    /// <summary>
    /// Fracao da folga (maestria - gravidade atual) que vira bonus quando se treina ABAIXO
    /// da propria maestria. 0,2 mantem o premio de estar aclimatado sem deixar farmar
    /// gravidade baixa no lugar de encarar gravidade alta.
    /// </summary>
    public static double GravAccustomWeight = 0.2;

    /// <summary>Teto absoluto de maestria de gravidade.</summary>
    public static double GravityCap = 500;

    /// <summary>Teto do multiplicador de ganho que o PESO da (razao 1 = 2x, 2 = 4x, 4 = 8x).</summary>
    public static double WeightGainMax = 8;

    // --- lutar com alguem mais forte ------------------------------------------
    /// <summary>Teto do bonus por encarar um oponente mais forte.</summary>
    public static double FightGainCap = 2;
    /// <summary>Teto do bonus treinando com o PROPRIO mestre (o geral e 2x).</summary>
    public static double MasterGainCap = 3;
    /// <summary>Diferenca minima de poder pro bonus do mestre valer.</summary>
    public static double MasterGainFloor = 1.2;

    // --- zonas especiais -------------------------------------------------------
    /// <summary>Sala do Tempo: 1 dia la = 1 ano de treino (28 dias x 10 meses).</summary>
    public const double TimeChamberMult = 28 * 10;
    /// <summary>Dimensao Mental: ganho a 1/4 do de fora.</summary>
    public const double MindDimensionMult = 0.25;

    /// <summary>
    /// O MAIOR BP base do servidor. Entra em duas escalas de ganho (alvo de Ki, sombra de
    /// treino, blast) e no teto do potencial escondido. O servidor mantem atualizado.
    /// </summary>
    public static double TopBP = 1;

    // --- Zenkai ----------------------------------------------------------------
    /// <summary>Fracao do BP do inimigo que vira Zenkai numa derrota comum.</summary>
    public static double ZenkaiPct = 0.1;
    /// <summary>A mesma, com o corpo em farrapos.</summary>
    public static double ZenkaiPctInjured = 0.15;
    /// <summary>Recarga do Zenkai, em milissegundos de relogio real (1 hora).</summary>
    public static long ZenkaiCooldownMs = 3_600_000;
    /// <summary>
    /// APOSENTADORIA do Zenkai: teto = `ssj3at` x isto, pra quem nunca rolou o requisito
    /// pessoal. 0,25 e o ponto medio da formula que sorteia esse requisito.
    /// </summary>
    public static double ZenkaiCapSSJ3Mid = 0.25;
}
