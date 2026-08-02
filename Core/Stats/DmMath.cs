namespace Jandirus.Core.Stats;

/// <summary>
/// As primitivas de numero do DreamMaker. Existem porque C# e DM NAO concordam nelas, e
/// cada divergencia aqui vira BP errado tres camadas acima.
///
/// * `round(x)` no DM e CHAO (floor), nao "arredondar pro mais perto". O powerlevel usa a
///   forma de um argumento em tres lugares -- trocar por Math.Round mudaria o BP final.
/// * `round(x, passo)` e o mais-perto (e o idioma `round(v, 0.1)` = uma casa decimal).
///   Math.Round do C# usa arredondamento bancario por padrao (2,5 -> 2), o DM nao.
/// * `log(x)` com um argumento e logaritmo NATURAL; com dois, o primeiro e a BASE.
/// * `x ** y` no DM aceita base negativa so com expoente inteiro; o resto e "not computable"
///   e derruba o proc inteiro. Como todo call site do jogo ja vem com `max(...)` de guarda,
///   aqui a gente prende no minimo positivo em vez de devolver NaN -- NaN se propaga calado
///   por dez multiplicacoes e so aparece no fim como BP zerado.
/// </summary>
public static class DmMath
{
    /// <summary>O menor valor que ainda pode entrar num log sem virar -infinito.</summary>
    private const double Epsilon = 1e-12;

    /// <summary>`round(x)` do DM: chao, NAO o mais perto.</summary>
    public static double Round(double x) => Math.Floor(x);

    /// <summary>`round(x, passo)` do DM: multiplo de `passo` mais proximo, meio pra cima.</summary>
    public static double Round(double x, double passo)
    {
        if (passo == 0) return x;
        return Math.Round(x / passo, MidpointRounding.AwayFromZero) * passo;
    }

    /// <summary>`log(x)` do DM: logaritmo natural.</summary>
    public static double Ln(double x) => Math.Log(Math.Max(x, Epsilon));

    /// <summary>`log(base, x)` do DM.</summary>
    public static double Log(double b, double x) => Math.Log(Math.Max(x, Epsilon)) / Math.Log(b);

    /// <summary>`x ** y`, com a mesma guarda de base negativa descrita no cabecalho.</summary>
    public static double Pow(double x, double y)
    {
        if (x < 0 && y != Math.Floor(y)) x = 0;
        double r = Math.Pow(x, y);
        return double.IsNaN(r) || double.IsInfinity(r) ? 0 : r;
    }

    public static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

    /// <summary>
    /// `prob(n)` do DM: verdadeiro em n por cento das vezes. O gerador vem de fora de
    /// proposito -- ganho de BP e autoridade do servidor, e sorteio escondido dentro de um
    /// objeto e sorteio que ninguem consegue reproduzir num teste.
    /// </summary>
    public static bool Prob(Random rng, double pct) => pct > 0 && rng.NextDouble() * 100 < pct;
}
