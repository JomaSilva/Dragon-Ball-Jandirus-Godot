namespace Jandirus.Core.Stats;

/// <summary>
/// As tres curvas que domam o jogo. Sao as unicas coisas entre "somei mais um ponto" e
/// "meu numero explodiu", entao ficam separadas e testaveis, longe do resto.
/// </summary>
public static class StatCurves
{
    /// <summary>
    /// `statCap()` -- retornos decrescentes de STAT (Stat Balance.dm). Ate 2,182 o stat vale
    /// o proprio numero; dali pra cima 10 pontos viram ~6 e 20 viram ~8,4. Acima de 39,93
    /// troca pra um ramo quase plano em ~9,7.
    ///
    /// A troca de ramo em 39,93 NAO e continua (9,9999 -> 9,8187: o stat CAI um tico ao
    /// cruzar). Portado assim de proposito -- e o comportamento que o jogo tem hoje, e a
    /// queda e menor que o passo de um ponto de stat.
    /// </summary>
    public static double StatCap(double stat)
    {
        if (stat <= 2.182) return stat;
        if (stat > 39.93) return Math.Sqrt(stat - 35) / (stat - 25) + 9.67;
        return 11 - 10 * DmMath.Pow(1.78, -stat / 10);
    }

    /// <summary>
    /// `netCap()` -- teto suave do pacote de buffs de estado (base.dm). Linear ate 2x; dali
    /// pra cima cresce no log2 do excedente (3->3, 5->4, 9->5, 17->6) e trava em 10x.
    ///
    /// A versao antiga usava uma base persistida perto de 1, e QUALQUER coisa acima de 2
    /// pulava direto pro teto: era o bug do Ki acima de 100% virando 10x de BP.
    /// </summary>
    public static double NetCap(double v)
    {
        if (v > 2) return Math.Min(2 + DmMath.Log(2, Math.Max(v - 1, 1)), 10);
        return v;
    }

    /// <summary>
    /// `gravFelt` -- quanto a gravidade do lugar ajuda ou atrapalha, dada a maestria.
    /// Entra como a razao maestria/gravidade: acima de 1 (treinou mais do que pesa aqui) da
    /// bonus em ln(razao)^2 amortecido; abaixo de 1 da penalidade.
    ///
    /// ARMADILHA DE PRECEDENCIA: no DM o `-log(x)**1.6` do ramo de baixo le como
    /// `(-log(x))**1.6`, nao `-(log(x)**1.6)` -- a segunda leitura seria base negativa com
    /// expoente fracionario, que nem numero e. Confirmado pela conta: com expoente 1,2
    /// (o valor antigo, ainda citado no comentario do DM) a razao 1/1000 da exatamente a
    /// "metade do BP" que o comentario promete. Com o 1,6 de hoje da ~0,31.
    /// </summary>
    public static double GravFelt(double maestria, double gravidadeLocal, double gravBalance = 1)
    {
        double g = maestria / Math.Max(1, gravidadeLocal);

        if (g > 1)
        {
            double v = DmMath.Pow(DmMath.Ln(g), 2);
            if (v == 0) return 1;
            return v / (40 * gravBalance) + 1;
        }

        return 1 / (DmMath.Pow(-DmMath.Ln(g), 1.6) / 10 + 1);
    }
}
