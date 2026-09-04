namespace Jandirus.Core.Stats;

/// <summary>
/// A CADEIA DE STATS -- o `statify()` do BYOND (Modules/Stats/Level/master.dm).
///
/// Tres degraus, sempre nesta ordem:
///
///   cru      physoff, speed, kiskill...   o que o genoma deu e o treino subiu
///     |
///   R<stat>  (cru x Mod + Buff) x Estilo x penalidade de fadiga
///     |
///   E<stat>  StatCap(R) -- a curva de retorno decrescente
///
/// A regra de ouro do sistema (esta escrita em maiusculas no proprio DM): stat 10 ja e
/// ENORME. Por isso o E<stat> satura perto de 10 e e ele, nunca o cru, que entra em dano,
/// esquiva e regeneracao.
///
/// O QUE NAO ENTROU: o statify do BYOND tambem DISPARA coisas (curar, regenerar Ki, drenar
/// folego, checar aura, aplicar gravidade, atualizar o God Ki). Isso e efeito de tick, nao
/// conta de stat, e cada um vem com o seu sistema nas etapas seguintes. Aqui fica so o
/// calculo -- que e justamente o que precisa rodar igual nas duas pontas.
/// </summary>
public sealed partial class Fighter
{
    public void Statify()
    {
        if (BP == 0) return;

        staminapercent = Math.Max(stamina, 1) / maxstamina;

        // ESCALA TROCADA NO ORIGINAL: staminadeBuff e 0-100 (o CheckNutrition escreve ate 100),
        // mas esta linha o trata como 0-1. O resultado pratico e que statstamina fica preso em 1
        // -- so cairia com o personagem abaixo de 1% de folego. Portado como esta: mexer aqui
        // mudaria offense/defense de todo mundo de uma vez.
        statstamina = Math.Max(Math.Min(1, staminadeBuff), 0.8);

        // Estaminamod ainda e o do tick ANTERIOR nestas duas linhas (o DM so recalcula ele
        // mais abaixo). Mantido na mesma ordem de proposito.
        if (staminapercent <= 0.05 && !dead) maxstamina += Math.Abs(0.0001 * Estaminamod);
        maxstamina = Math.Min(200 * Math.Abs(Estaminamod), maxstamina);

        if (isconcealed) { concealeddeBuff = 0.5; concealedBuff = 1.5; }
        else { concealeddeBuff = 1; concealedBuff = 1; }

        Ewillpower = Math.Max(TwillpowerMod * willpowerMod, 0.1);
        Estaminamod = staminagainMod * staminadrainStyle * satiationMod * Ewillpower;

        // Quanto o folego mexe no GANHO de BP. No BYOND mora no CheckNutrition (StaminaDrain.dm),
        // mas e funcao pura de folego e o CapCheck depende dele -- fica junto do resto do calculo.
        // Meio tanque = 1x; cheio = 1,25x; vazio = 0,5x. Treinar exausto rende metade.
        StamBPGainMod = DmMath.Clamp(stamina / (maxstamina * 0.5), 0.5, 1.25);

        // --- velocidade -------------------------------------------------
        // `formaSpeed` entra AQUI e nao no `Tspeed` porque o `Tspeed` esta morto nesta linha: ele
        // multiplica `speedBuff`, que nasce em zero. Ver o cabecalho do canal em `Fighter.cs`.
        Rspeed = (Math.Max(speed, 0.1) * Math.Max(speedMod, 0.1) + Math.Max(speedBuff * Tspeed, 0.1))
                 * speedStyle * formaSpeed * DmMath.Clamp(staminapercent * Ewillpower, 0.75, 1);
        Espeed = StatCurves.StatCap(Rspeed);

        if (!IsInFight) speedDIFF = 1;
        dashingMod = dashing ? 1 / (Espeed + 1) : 1;

        // O CUSTO DE ESTAR ESPALHADO EM MAIS DE UM CORPO. Uma copia viva sem maestria vale 0,5 (=
        // metade do poder expresso); cada 0,2 de maestria -- uma subida do `Splitformskill` do
        // `Split Forms.dm` -- devolve um pedaco, e maestria 1 anula o custo de uma copia.
        //
        // QUEM GASTA ESTE NUMERO e o `deBuff` do `PowerLevel`, e SO ELE. Ate 2026-09-02 ele era
        // calculado aqui e jogado fora la, porque no DM entra dentro de um `max(...,1)` que um fator
        // <= 1 nunca atravessa -- ver a divergencia declarada em `Fighter.Power.cs`.
        splitformdeBuff = Math.Min((1 + splitformMastery) / (1 + splitformCount), 1);

        // --- os sete stats ----------------------------------------------
        // Forma canonica: (base x Mod + Buff) x Estilo x fadiga. Quem sofre com fadiga:
        // offense/defense fisico e de ki. Technique, Ki Skill e Esoteric NAO sofrem -- sao
        // conhecimento, nao musculo.
        double fadiga = DmMath.Clamp(statstamina * Ewillpower, 0.6, 1);

        // Os `forma*` sao o canal da FORMA ATIVA (ver `Fighter.cs`): entram como fator do R inteiro,
        // no mesmo posto do `*Style`, e ficam de fora do `powerlevel()` de proposito.
        Rphysoff = (Math.Max(physoff, 0.1) * Math.Max(physoffMod, 0.1) + Math.Max(physoffBuff + Tphysoff, 0.1))
                   * physoffStyle * formaPhysoff * fadiga * Math.Max(0.01, phys_remove);
        Rphysdef = (Math.Max(physdef, 0.1) * Math.Max(physdefMod, 0.1) + Math.Max(physdefBuff + Tphysdef, 0.1))
                   * physdefStyle * formaPhysdef * fadiga;
        Rtechnique = (Math.Max(technique, 0.1) * Math.Max(techniqueMod, 0.1) + Math.Max(techniqueBuff + Ttechnique, 0.1))
                   * techniqueStyle * formaTecnica;
        Rkioff = (Math.Max(kioff, 0.1) * Math.Max(kioffMod, 0.1) + Math.Max(kioffBuff + Tkioff, 0.1))
                   * kioffStyle * formaKioff * fadiga;
        Rkidef = (Math.Max(kidef, 0.1) * Math.Max(kidefMod, 0.1) + Math.Max(kidefBuff + Tkidef, 0.1))
                   * kidefStyle * formaKidef * fadiga;
        Rkiskill = (Math.Max(kiskill, 0.1) * Math.Max(kiskillMod, 0.1) + Math.Max(kiskillBuff + Tkiskill, 0.1))
                   * kiskillStyle;
        Rmagiskill = (Math.Max(magiskill, 0.1) * Math.Max(magiMod, 0.1) + Math.Max(magiBuff + Tmagi, 0.1))
                   * Tmagimon * magiStyle;

        if (kiarmor > 0 && superkiarmor <= kiarmor * GlobalKiArmorMod) superkiarmor++;
        Esuperkiarmor = DmMath.Clamp(superkiarmor * superkiarmorMod, 0, 100);

        Ephysoff = StatCurves.StatCap(Rphysoff);
        Ephysdef = StatCurves.StatCap(Rphysdef);
        Etechnique = StatCurves.StatCap(Rtechnique);
        Ekioff = StatCurves.StatCap(Rkioff);
        Ekidef = StatCurves.StatCap(Rkidef);
        Ekiskill = StatCurves.StatCap(Rkiskill);
        Emagiskill = StatCurves.StatCap(Rmagiskill);

        Epspeed = Math.Max(Espeed / (speedDIFF * dashingMod), 0);

        // Velocidade de ACAO (quanto tempo cada golpe custa). Duas camadas de log em cima da
        // velocidade e da tecnica, e o resultado preso entre 5 e 22 decimos de segundo.
        // hpratio/kiratio aqui sao os do tick anterior -- o PowerLevel e quem os escreve, e
        // ele roda DEPOIS. No primeiro tick valem 0 e o denominador cai pro piso 1, o que so
        // faz o personagem comecar com a velocidade de acao base.
        double a = DmMath.Pow(DmMath.Log(10.15, Espeed), 2) + 1;
        double b = DmMath.Pow(DmMath.Log(10, Math.Max(Ekiskill / 2, Etechnique / 2.125)), 2) + 1;
        double dentro = Math.Max(a * b * hpratio * kiratio, 0.1);
        double denom = Math.Max(DmMath.Log(10, Math.Max(DmMath.Log(10, dentro) * 3, 1)) * 4, 1);
        Eactspeed = DmMath.Clamp(actspeed / denom, 5, 22);

        UpdateMaxKi();

        // --- potencial escondido ----------------------------------------
        // Quem tem muito mais potencial do que poder (o "modo Uub") ganha teto de Ki e de
        // raiva -- nao ganha stat.
        //
        // ============================ POR QUE O TETO DE KI CAIA A CADA SOCO ============================
        // `hdnptlmod = log4(hiddenpotential / BP)`. No DM o numerador cresce todo ano de jogo
        // (`Aging.dm:125-139`, `AgeCheck`: `hiddenpotential += BP*UPMod*anos`, proporcional ao BP), e
        // a razao se sustenta. Este port NAO portou essa linha: as unicas escritas em `hiddenpotential`
        // sao o nascimento (1) e o presente de compra da One Hundred/One Punch/One Training
        // (`hiddenpotential += relBPmax*k`, `Bodybuilding.dm:87-96`). Numerador CONGELADO, denominador
        // subindo a cada soco, treino e meditacao -> `MaxKi`, `kicapacity`, `powerupcap`, `KIregen` e
        // `MaxAnger` DESCIAM sozinhos, e o soco basico nem gasta Ki do atacante -- o dono viu "o Ki
        // maximo cai em vez do atual" (2026-09-04). A One Hundred ainda acelerava a propria queda: com
        // `hiddenpotential >= BP` o `AttackGain` toma o ramo gordo (1/6 em vez de 1/12).
        //
        // O CONSERTO: a razao que a compra deu fica GARANTIDA (`potencialGarantido`, somada na compra,
        // devolvida ao esquecer). O numerador efetivo e o maior entre o potencial gravado e
        // `BP * potencialGarantido` -- a mesma razao no dia da compra e em qualquer dia depois, e o bonus
        // de Ki que a skill deu e o bonus que ela mantem. O campo `hiddenpotential` em si NAO muda, de
        // proposito: ele tambem governa o ritmo de ganho de BP (`AttackGain`/`MedGain`), e tornar ESSE
        // permanente seria mudar a progressao inteira, o que ninguem pediu.
        //
        // DIVERGENCIA DECLARADA: o DM tambem nao mantem a razao dentro do ano (ela decai ate o proximo
        // `AgeCheck`) e a corta em `TopBP` (`Stats.dm:171-173`). Nenhuma das duas pontas foi portada; o
        // que se garante aqui e o que a skill promete ao jogador -- mais Ki, e nao "mais Ki ate o
        // proximo treino".
        // ==================================================================================================
        hdnptltoBP = Math.Max(Math.Max(hiddenpotential, BP * potencialGarantido), 1) / Math.Max(BP, 1);
        double hdnptlmod = hdnptltoBP > 1 ? Math.Max(DmMath.Log(4, hdnptltoBP), 1) : 1;

        // piso 120 pra um baseAnger perdido nao deixar o personagem "Muito Irritado" pra sempre
        MaxAnger = Math.Max(120, hdnptlmod * Ewillpower * angerMod * baseAnger) + legendaryAngerBonus;
        MaxKi = baseKi * KiMod * kiAmp * trueKiMod * hdnptlmod * Math.Max(0.01, kicapacity_remove) * TMaxKi;

        // Capacidade de concentrar Ki no corpo antes de se machucar, e o teto do power-up.
        // O max(...,1) envolve o ARGUMENTO INTEIRO do log: so o fator de skill protegido nao
        // bastava -- um KiMod zerado fazia log(8, 0) derrubar o statify a cada tick.
        double skillKi = Math.Max(kicirculationskill / 10 * (kigatheringskill / 10), 1)
                         * KiMod * hdnptlmod * Math.Max(Ekiskill, 0.1);
        kicapacity = 1.3 * kicapacityMod
                     * Math.Max(MaxKi * Math.Max(DmMath.Log(8, Math.Max(skillKi, 1)), 1) * OozaruBuff, 1.3 * MaxKi);

        double skillPup = Math.Max(kicirculationskill / 10 * (kicontrolskill / 10), 1)
                          * KiMod * kicapacityMod * hdnptlmod * Math.Max(Ekiskill, 0.1);
        powerupcap = Math.Max(1.4 * Math.Max(DmMath.Log(7, Math.Max(skillPup, 1)), 1) * OozaruBuff, 1.4);

        // Regeneracao de Ki: sempre passa por Ki Skill e pela MAIOR entre tecnica e esoterico.
        KIregen = kiregenMod * basekiregen * kiregenStyle * trueKiMod * hdnptlmod * Tkiregen
                  * Ekiskill * Math.Max(Emagiskill, Etechnique)
                  * (MaxKi / 100) * (statstamina * 5) * concealeddeBuff
                  * DmMath.Log(10, Math.Max(kigatheringskill, 10)) / 900;

        MagicCap = 2000 * Emagiskill * mana_cap_mod;
    }

    /// <summary>Ajuste global de armadura de ki (era `globalkiarmormod`, um knob de admin).</summary>
    public static double GlobalKiArmorMod = 1;

    /// <summary>
    /// Teto do Ki BASE, que cresce com o BP e nao com treino direto. `KiUnlockPercent` (10%)
    /// e o quanto desse teto o personagem realmente destravou.
    /// </summary>
    public void UpdateMaxKi()
    {
        if (BP <= 0) return;
        baseKiMax = DmMath.Round(100 * Math.Max(DmMath.Pow(DmMath.Log(10, BP), 2.3), 1), 1) * KiUnlockPercent;
    }

    /// <summary>Quanto de um ganho de Ki base cabe antes de bater no teto.</summary>
    public double KiCapCheck(double gap)
    {
        double check = baseKiMax - gap;
        return check > 0 ? gap : baseKiMax - baseKi;
    }
}
