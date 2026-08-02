using System.Globalization;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;

namespace Jandirus.Tools;

/// <summary>
/// BANCO DE PROVA da conta de poder.
///
/// A cadeia de stats e o powerlevel nao tem como "quebrar" visivelmente: se um fator entrar
/// na familia errada (multiplicar em vez de somar na base), tudo continua compilando e um
/// personagem simples continua com o numero certo. O erro so aparece muito depois, num
/// personagem com forma + Kaio-ken + God Ki ao mesmo tempo.
///
/// Por isso o banco monta casos CONCRETOS com numeros redondos, onde da pra dizer de cabeca
/// qual devia ser a resposta -- e o mesmo caso pode ser reproduzido no BYOND pra comparar.
/// </summary>
public static class StatBench
{
    public static void Run(RaceCatalog? cat)
    {
        var ci = CultureInfo.InvariantCulture;
        Console.WriteLine("== CURVAS ==\n");

        Console.Write("StatCap (stat cru -> efetivo):  ");
        foreach (double s in new double[] { 1, 2, 2.182, 3, 5, 10, 20, 40, 60 })
            Console.Write($"{s.ToString("0.##", ci)}->{StatCurves.StatCap(s).ToString("0.##", ci)}  ");
        Console.WriteLine("\n  (o proprio numero ate 2,182; 10 pontos valem ~6; satura perto de 10)\n");

        Console.Write("NetCap (pacote de estado):      ");
        foreach (double s in new double[] { 0.5, 1, 2, 3, 5, 9, 17, 100 })
            Console.Write($"{s.ToString("0.##", ci)}->{StatCurves.NetCap(s).ToString("0.##", ci)}  ");
        Console.WriteLine("\n  (linear ate 2x; dali log2 do excedente; teto 10x)\n");

        Console.Write("GravFelt (maestria 100 em):     ");
        foreach (double g in new double[] { 1, 10, 100, 500, 1000 })
            Console.Write($"{g.ToString("0", ci)}g->{StatCurves.GravFelt(100, g).ToString("0.###", ci)}  ");
        Console.WriteLine();
        Console.Write("GravFelt (maestria 1 em):       ");
        foreach (double g in new double[] { 1, 10, 100, 1000 })
            Console.Write($"{g.ToString("0", ci)}g->{StatCurves.GravFelt(1, g).ToString("0.###", ci)}  ");
        Console.WriteLine("\n  (acima da maestria vira penalidade; abaixo, bonus amortecido)\n");

        // -----------------------------------------------------------------
        // BP: cada caso parte de um lutador NEUTRO de BP 1000, onde o esperado
        // e o proprio 1000. Assim cada linha mostra o efeito ISOLADO de um fator.
        // -----------------------------------------------------------------
        Console.WriteLine("== BP EXPRESSO (base 1000, tudo neutro) ==\n");
        Console.WriteLine($"{"CASO",-38} {"BP EXPRESSO",14}  {"ESPERADO",12}");
        Console.WriteLine(new string('-', 70));

        Caso("neutro", "1000 (identidade)", _ => { });

        Console.WriteLine("\n  -- familia 1: MULTIPLICAM --");
        Caso("SSJ masterizado (ssjBuff 6)", "6000", f => f.ssjBuff = 6);
        Caso("Oozaru (transBuff 1.5)", "1500", f => f.transBuff = 1.5);
        Caso("Ascensao (BPBoost 2)", "2000", f => f.BPBoost = 2);
        Caso("raiva no teto (MaxAnger 120)", "1200", f => f.Anger = 9999);
        Caso("raiva de raca irascivel (MaxAnger 300)", "2000 (teto 2x)", f => { f.baseAnger = 300; f.Anger = 9999; });
        Caso("a mesma, com Legendary Anger", "3000 (teto 3x)", f =>
        {
            f.baseAnger = 300; f.legendaryAngerBonus = 100; f.Anger = 9999;
        });
        Caso("Ki a 200%", "2000", f => f.Ki = f.MaxKi * 2);
        Caso("Ki a 300%", "3000 (NetCap: 3->3)", f => f.Ki = f.MaxKi * 3);
        Caso("Ki a 900%", "5000 (NetCap: 9->5)", f => f.Ki = f.MaxKi * 9);
        Caso("Controle de Poder a 50%", "500", f => f.powerMod = 0.5);

        Console.WriteLine("\n  -- familia 2: SOMAM NA BASE --");
        Caso("Kaio-ken x3", "3000", f => f.KaioPcnt = 3);
        Caso("Mistico x2", "2000", f => f.MysticPcnt = 2);
        Caso("Kaio-ken x3 + Mistico x2", "4000 (soma: 1+2+1)", f => { f.KaioPcnt = 3; f.MysticPcnt = 2; });
        Caso("SSJ 6x + Kaio-ken x3", "18000 (soma ANTES do x6)", f => { f.ssjBuff = 6; f.KaioPcnt = 3; });
        Caso("gravidade dominada (100 em 1g)", "~1530", f => f.GravMastered = 100);

        Console.WriteLine("\n  -- GOD KI: substitui a escada SSJ --");
        Caso("God (sem SSJ)", "22000", f => { f.godki = Deus(0); });
        Caso("Blue (SSJ 6x por baixo)", "32000 (o 6x e cancelado)", f =>
        {
            f.ssjBuff = 6; f.ssj = 1;
            f.godki = Deus(GodKiState.BluePct);
        });
        Caso("Royale (USSJ 1.5 no God Ki)", "56000", f =>
        {
            f.ssjBuff = 8; f.ssj = 1.5;
            f.godki = Deus(GodKiState.RoyalePct);
        });
        Caso("Beast (Prodigial, 56x)", "56000", f =>
        {
            f.Class = "Prodigial"; f.beast_form = true;
            f.godki = Deus(GodKiState.RoyalePct);
        });

        Console.WriteLine("\n  -- familia 3 e debuffs: no FIM de tudo --");
        Caso("nocauteado", "100 (10% do base)", f => { f.KO = true; f.ssjBuff = 6; f.Anger = 999; });
        Caso("peso 2x", "500", f => f.weight = 2);
        Caso("larva de bio-androide", "10 (1% do base)", f => { f.bio_lab_born = true; f.bio_stage = 1; f.ssjBuff = 6; });
        Caso("Frost mutante vazando (0.3)", "300", f => f.fd_release = 0.3);
        Caso("revive por Zeni (1a hora)", "250", f => f.zeni_revive_debuff_until = 1);
        Caso("poder escondido (2 ticks)", "5", f => f.isconcealed = true, ticks: 2);
        Caso("meio morto (HP 10) + sem folego", "180", f => { f.HP = 10; f.staminadeBuff = 30; });

        // -----------------------------------------------------------------
        // A cadeia de stats num personagem de verdade
        // -----------------------------------------------------------------
        if (cat == null) return;
        Console.WriteLine("\n== CADEIA DE STATS (personagens do catalogo) ==\n");
        Console.WriteLine($"{"RACA/CLASSE",-30} {"BP",10} {"physoff",16} {"speed",16} {"MaxKi",8} {"acao",6}");
        Console.WriteLine(new string('-', 92));

        var rng = new Random(12345);
        foreach ((string raca, string lin) in new[]
                 { ("Human", ""), ("Saiyan", "Saiyan"), ("Saiyan", "Primal Saiyan"),
                   ("Namekian", "Warrior clan"), ("Namekian", "Dragon clan"),
                   ("Halfbreed", "Future Lineage"), ("Icer", ""), ("Kai", ""), ("Majin", "") })
        {
            if (cat.Get(raca == "Halfbreed" ? "Saiyan" : raca) == null) continue;
            Fighter f = Birth.Nascer(cat, raca, lin, rng, raca);

            Console.WriteLine($"{raca + "/" + f.Class,-30} {f.BP,10:0.0} " +
                              $"{f.physoff.ToString("0.##", ci) + " -> " + f.Ephysoff.ToString("0.##", ci),16} " +
                              $"{f.speed.ToString("0.##", ci) + " -> " + f.Espeed.ToString("0.##", ci),16} " +
                              $"{f.MaxKi,8:0} {f.Eactspeed,6:0.0}");
        }
        Console.WriteLine("\n  (cru -> efetivo: e o StatCap comendo o excedente. So o efetivo entra em combate.)");
    }

    /// <summary>God Ki ligado e desperto, na maestria pedida.</summary>
    private static GodKiState Deus(double maestria) =>
        new() { usage = true, awakened = true, mastery = maestria };

    private static void Caso(string nome, string esperado, Action<Fighter> montar, int ticks = 1)
    {
        var f = new Fighter { BP = 1000, Name = nome };
        f.Tick();              // primeiro tick assenta MaxKi e os stats efetivos
        montar(f);
        for (int i = 0; i < ticks; i++) f.Tick(agoraMs: 0);

        Console.WriteLine($"{nome,-38} {f.expressedBP,14:0.##}  {esperado,12}");
    }
}
