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
    /// <summary>
    /// O ANO QUE O <c>AgeCheck</c> CONTA (<c>Aging.dm:125-139</c>): <c>hiddenpotential += BP * UPMod * anosVividos</c>.
    ///
    /// <paramref name="anosVividos"/> e quantos anos de vida fecharam NESTA conta, contados da criacao do
    /// personagem. Nao e <see cref="Idade"/>. Quem nasce aos 18 e faz 19 passa 1. Um Majin criado com
    /// 45 milhoes na ficha, no primeiro aniversario, tambem passa 1: a idade da ficha nao entra na conta,
    /// senao a criacao vira poder. O BP e o de agora, numa soma so.
    /// Zero nao soma: o mes que nao fecha ano deixa o estoque quieto, e a razao cai ate o proximo ano, como no DM.
    /// </summary>
    public void SomarPotencialOcultoDaIdade(int anosVividos)
    {
        if (anosVividos <= 0) return;
        anosDesdeACriacao += anosVividos;
        hiddenpotential += BP * UPMod * anosVividos;
    }

    /// <summary>
    /// O MAIOR "Ki base x multiplicador do potencial" que este corpo ja teve com o multiplicador acima de 1 -- o piso do Ki maximo
    /// (ver <see cref="KiBasePeloPotencial"/>). Zero = o potencial nunca engordou o Ki dele. Vai pro save com a ficha.
    /// </summary>
    public double recordeDoKiPeloPotencial;

    /// <summary>
    /// O KI BASE QUE O POTENCIAL ENGORDA, E QUE NAO EMAGRECE (dono, 2026-10-22).
    ///
    /// O multiplicador do potencial e `log4(potencial / BP)` (o `hdnptlmod`, no piso de 1): ganha 1x a cada vez que a razao
    /// quadruplica e perde 1x a cada vez que o BP quadruplica. Com a reencarnacao passando o BP inteiro da vida anterior pro
    /// potencial, o Ki maximo de quem renasce comecava bem acima do normal e escorregava ate o normal conforme o BP alcancava. O
    /// dono: *"caso o novo teto esteja abaixo do teto anterior por conta do potencial, o teto n cai, pq n faz sentido o cara perder
    /// ki max pq ele atingiu o potencial"*.
    ///
    /// O RECORDE (<see cref="recordeDoKiPeloPotencial"/>) guarda o maior Ki base x multiplicador, e o Ki maximo nunca sai dele: o
    /// crescimento normal (o Ki base subindo com o treino e o BP) passa dele quando passar. SO O MULTIPLICADOR ACIMA DE 1 ESCREVE
    /// O RECORDE: quem nunca teve potencial acima de 4x o BP tem a conta de sempre. O que vem DEPOIS na conta (Energy Level,
    /// amplificadores, forma, o corte de selo) fica fora do recorde -- continua entrando e saindo na hora.
    ///
    /// DIVERGENCIA DECLARADA: no DM o Ki maximo cai junto com a razao (`master.dm:181`).
    /// </summary>
    private double KiBasePeloPotencial(double hdnptlmod)
    {
        double agora = baseKi * hdnptlmod;
        if (PotencialNoKiCaiDeTeste) return agora;
        if (hdnptlmod > 1) recordeDoKiPeloPotencial = Math.Max(recordeDoKiPeloPotencial, agora);
        return Math.Max(agora, recordeDoKiPeloPotencial);
    }

    /// <summary>
    /// DEFEITO INJETADO (bancada `--v030teste`, familia L): o Ki maximo volta a cair quando o BP alcanca o potencial -- o DM, e o
    /// jogo de antes de 2026-10-22. Lido por <see cref="KiBasePeloPotencial"/>. Falso em jogo, sempre.
    /// </summary>
    public static bool PotencialNoKiCaiDeTeste;

    /// <summary>
    /// DEFEITO INJETADO (bancada `--v030teste`, familia L): o potencial volta a multiplicar a regen de Ki e o teto seguro da carga
    /// -- o DM (`master.dm:182` e `:184`), e o jogo de antes de 2026-10-22. Lido pelo <see cref="Statify"/>. Falso em jogo, sempre.
    /// </summary>
    public static bool PotencialNaRegenENaCargaDeTeste;

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
                 * speedStyle * formaSpeed * racialSpeed * DmMath.Clamp(staminapercent * Ewillpower, 0.75, 1);
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
                   * physoffStyle * formaPhysoff * racialPhysoff * fadiga * Math.Max(0.01, phys_remove);
        // A ARMADURA DE TECH soma aqui, junto do `physdefBuff` -- que e onde o `Equip()` do DM a poe
        // (`usr.physdefBuff+=armorincrease`, `Armor.dm:29`). Ela nao e escrita NO `physdefBuff` porque o quanto ela
        // rende cai com o poder (regra do dono): ver `Items.ArmaduraDeTech.Bonus`.
        Rphysdef = (Math.Max(physdef, 0.1) * Math.Max(physdefMod, 0.1)
                    + Math.Max(physdefBuff + Tphysdef + Items.ArmaduraDeTech.Bonus(this), 0.1))
                   * physdefStyle * formaPhysdef * racialPhysdef * fadiga;
        Rtechnique = (Math.Max(technique, 0.1) * Math.Max(techniqueMod, 0.1) + Math.Max(techniqueBuff + Ttechnique, 0.1))
                   * techniqueStyle * formaTecnica * racialTecnica;
        Rkioff = (Math.Max(kioff, 0.1) * Math.Max(kioffMod, 0.1) + Math.Max(kioffBuff + Tkioff, 0.1))
                   * kioffStyle * formaKioff * racialKioff * fadiga;
        Rkidef = (Math.Max(kidef, 0.1) * Math.Max(kidefMod, 0.1) + Math.Max(kidefBuff + Tkidef, 0.1))
                   * kidefStyle * formaKidef * racialKidef * fadiga;
        Rkiskill = (Math.Max(kiskill, 0.1) * Math.Max(kiskillMod, 0.1) + Math.Max(kiskillBuff + Tkiskill, 0.1))
                   * kiskillStyle * racialKiskill;
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
        // raiva -- nao ganha stat. (2026-10-22, dono) Desde entao so o Ki maximo (que nao cai mais: `KiBasePeloPotencial`)
        // e a raiva maxima leem o multiplicador; a regen e o teto seguro da carga, nao.
        //
        // ============================ POR QUE O TETO DE KI CAIA A CADA SOCO ============================
        // `hdnptlmod = log4(hiddenpotential / BP)`. No DM o numerador cresce no ano que o `AgeCheck` conta
        // (`Aging.dm:125-139`: `hiddenpotential += BP*UPMod*anos`). Entre um ano e o outro o BP sobe no
        // treino e a razao cai. Este port ficou sem essa linha ate 2026-10-21: as unicas escritas eram o
        // nascimento (1), a heranca do filho e a compra da One Hundred/One Punch/One Training
        // (`hiddenpotential += relBPmax*k`, `Bodybuilding.dm:87-96`). Numerador parado, denominador
        // subindo -> `MaxKi` e `MaxAnger` desciam sozinhos (o dono, 2026-09-04).
        //
        // DUAS COISAS SEGURAM A RAZAO, E NAO SAO A MESMA:
        //   * O ANO (`SomarPotencialOcultoDaIdade`) escreve o estoque de verdade, como no DM. Isso tambem
        //     engorda o pinga de BP do treino, da meditacao e do soco, que leem `hiddenpotential` cru.
        //   * A COMPRA ANTIGA (`potencialGarantido`) ainda segura o piso de Ki de quem comprou One Hundred,
        //     One Punch ou One Training antes de 2026-10-21. Compra nova dessas skills nao escreve potencial
        //     (so BP na One Hundred e na One Training; a One Punch nao soma nada). O numerador efetivo e o
        //     maior entre o estoque gravado e `BP * potencialGarantido`.
        //
        // DIVERGENCIA DECLARADA: o DM corta a razao em `TopBP` (`Stats.dm:171-173`). Essa ponta nao foi
        // portada.
        // ==================================================================================================
        hdnptltoBP = Math.Max(Math.Max(hiddenpotential, BP * potencialGarantido), 1) / Math.Max(BP, 1);
        double hdnptlmod = hdnptltoBP > 1 ? Math.Max(DmMath.Log(4, hdnptltoBP), 1) : 1;

        // piso 120 pra um baseAnger perdido nao deixar o personagem "Muito Irritado" pra sempre
        // (o bonus da skill Legendary Anger entra AQUI -- `master.dm:180` --, e por isso ela sobe os DOIS degraus da
        // raiva por amigo: os dois saem do `MaxAnger`, ver `Combat.RaivaEmDegraus`. O `? 0` e o defeito injetado da
        // bancada; em jogo ele e sempre falso.)
        MaxAnger = Math.Max(120, hdnptlmod * Ewillpower * angerMod * baseAnger)
                   + (Jandirus.Core.Combat.RaivaEmDegraus.LegendaryAngerSoNoTetoDeTeste ? 0 : legendaryAngerBonus);
        // (2026-10-22) o potencial engorda o Ki maximo e ele nao emagrece quando o BP alcanca o potencial -- ver `KiBasePeloPotencial`
        MaxKi = KiBasePeloPotencial(hdnptlmod) * KiMod * kiAmp * trueKiMod * Math.Max(0.01, kicapacity_remove) * TMaxKi * racialMaxKi;

        // (2026-10-18) O TANQUE DO CHEFE DE SAGA TEM PISO -- o dono: *"o freeza n ta usando death beam"*. O Ki sem fim do chefe (o
        // cadeado `Nutricao.KiSempreCheio`) reenchia um tanque de NPC -- 259 no Freeza de Namek --, e o Death Beam cobra `kireq*25` por
        // ciclo (`DeathBeam.dm:46`): 975 desse tanque, quase quatro dele inteiro. A IA nunca o achava pagavel (nem a Death Ball, 279), e
        // o verb o recusaria. Com o piso, o tiro mais caro que um chefe sabe cabe num terco do tanque, e o cadeado o devolve no fim do
        // tique. Ver `PisoDoTanqueDeChefe`.
        if (KiDeChefe && !ChefeSemPisoDeTanqueDeTeste) MaxKi = Math.Max(MaxKi, PisoDoTanqueDeChefe);

        // Capacidade de concentrar Ki no corpo antes de se machucar, e o teto do power-up.
        // O max(...,1) envolve o ARGUMENTO INTEIRO do log: so o fator de skill protegido nao
        // bastava -- um KiMod zerado fazia log(8, 0) derrubar o statify a cada tick.
        // (2026-10-22) SEM O POTENCIAL: o teto seguro e das pericias, do Energy Level e do Ki Skill -- o dono: *"potencial n deveria
        // afetar regen, carga do ki"*. DIVERGENCIA DECLARADA do DM (`master.dm:182`, que le o `hdnptlmod` dentro do log8). O teto
        // absoluto continua proporcional ao Ki maximo, que o potencial engorda.
        double skillKi = Math.Max(kicirculationskill / 10 * (kigatheringskill / 10), 1)
                         * KiMod * (PotencialNaRegenENaCargaDeTeste ? hdnptlmod : 1) * Math.Max(Ekiskill, 0.1);
        kicapacity = 1.3 * kicapacityMod
                     * Math.Max(MaxKi * Math.Max(DmMath.Log(8, Math.Max(skillKi, 1)), 1) * OozaruBuff, 1.3 * MaxKi);

        double skillPup = Math.Max(kicirculationskill / 10 * (kicontrolskill / 10), 1)
                          * KiMod * kicapacityMod * hdnptlmod * Math.Max(Ekiskill, 0.1);
        // ============================ O TETO DO JOGO E 200% (dono, 2026-10-13) ============================
        // *"o teto limite do jogo q vc pode ter de ki seria 200%, entao escale as pericias e skills etc pro teto de
        // 200%, pq atualmente rapidinho vc consegue passar de 200% de ki oq quebra um pouco as coisas no jogo.
        // (entao 200% seria o cara q tem uma pericia absurda, no maximo, do controle de ki)"*.
        //
        // A conta do DM (`master.dm:183`) e `1.4 * log7(circulacao/10 * controle/10 * KiMod * kicapacityMod *
        // hdnptlmod * Ekiskill)`: com as duas pericias em 30 ja passava de 200%, e no talo chegava a 500%. A de
        // agora le SO as duas pericias (`CargaDeKi.TetoPelaPericia`): 140% de fabrica, 200% com as duas no maximo.
        // O botao devolve a de la, pra bancada.
        // ====================================================================================================
        powerupcap = Jandirus.Core.Combat.CargaDeKi.TetoPelaFormulaDoDmDeTeste
            ? Math.Max(1.4 * Math.Max(DmMath.Log(7, Math.Max(skillPup, 1)), 1) * OozaruBuff, 1.4)
            : Jandirus.Core.Combat.CargaDeKi.TetoPelaPericia(kicirculationskill, kicontrolskill);
        // (v0.4.0) O TETO RACIAL DA CARGA: o Immeasurable Potential do meio-Saiyajin leva a mesma escala a 225% -- ver `CargaDeKi.TetoDuro`.
        powerupcap *= Math.Max(racialTetoDaCarga, 0.01);

        // Regeneracao de Ki: sempre passa por Ki Skill e pela MAIOR entre tecnica e esoterico.
        // (2026-10-22) E SEM O POTENCIAL, pelo pedido do dono logo acima: ela continua proporcional ao Ki maximo (o tanque que o
        // potencial engorda rende mais Ki por segundo), mas enche no MESMO ritmo, em %, de quem nao tem potencial. No DM o
        // `hdnptlmod` entrava aqui direto e de novo pelo `MaxKi` (`master.dm:184`): ~135x mais Ki por segundo a 11,6x de
        // multiplicador. DIVERGENCIA DECLARADA.
        KIregen = kiregenMod * basekiregen * kiregenStyle * trueKiMod * (PotencialNaRegenENaCargaDeTeste ? hdnptlmod : 1) * Tkiregen
                  * Ekiskill * Math.Max(Emagiskill, Etechnique)
                  * (MaxKi / 100) * (statstamina * 5) * concealeddeBuff
                  * DmMath.Log(10, Math.Max(kigatheringskill, 10)) / 900;

        MagicCap = 2000 * Emagiskill * mana_cap_mod;
    }

    /// <summary>
    /// (2026-10-20) A CONTA DE VOLTA DO <see cref="Statify"/>: os oito atributos CRUS que fazem o `R` de cada um sair no ALVO pedido. E o chefe
    /// de saga (e o Bills) com o DOBRO do jogador mais forte -- o dono: *"faça os stats do boss(todos os de saga e contando o bills) ser o dobro
    /// do jogador mais forte"* (ver `GameServer.DarODobroAoChefe`). O `R` e o numero que a aba Stats mostra (vezes 10), e ele e LINEAR no cru:
    /// `R = (max(cru,0.1) x max(Mod,0.1) + soma) x fator`, com a soma (buff + T) e o fator (estilo, forma, racial, fadiga) que o corpo tem
    /// agora -- as oito linhas do `R` acima, lidas de tras pra frente. Roda o `Statify` antes (os canais de agora) e depois (o `R` novo).
    ///
    /// QUEM MEXER NAS LINHAS DO `R` ACIMA MEXE AQUI JUNTO: a bancada (`--torneiocellteste`, familia W) confere o `R` dos oito no alvo.
    /// </summary>
    public void FixarAtributos(double physoffR, double physdefR, double techniqueR, double kioffR, double kidefR, double kiskillR,
                               double speedR, double magiR)
    {
        Statify();
        double fadiga = DmMath.Clamp(statstamina * Ewillpower, 0.6, 1);
        physoff = CruPara(physoffR, physoffMod, Math.Max(physoffBuff + Tphysoff, 0.1),
                          physoffStyle * formaPhysoff * racialPhysoff * fadiga * Math.Max(0.01, phys_remove));
        physdef = CruPara(physdefR, physdefMod, Math.Max(physdefBuff + Tphysdef + Items.ArmaduraDeTech.Bonus(this), 0.1),
                          physdefStyle * formaPhysdef * racialPhysdef * fadiga);
        technique = CruPara(techniqueR, techniqueMod, Math.Max(techniqueBuff + Ttechnique, 0.1), techniqueStyle * formaTecnica * racialTecnica);
        kioff = CruPara(kioffR, kioffMod, Math.Max(kioffBuff + Tkioff, 0.1), kioffStyle * formaKioff * racialKioff * fadiga);
        kidef = CruPara(kidefR, kidefMod, Math.Max(kidefBuff + Tkidef, 0.1), kidefStyle * formaKidef * racialKidef * fadiga);
        kiskill = CruPara(kiskillR, kiskillMod, Math.Max(kiskillBuff + Tkiskill, 0.1), kiskillStyle * racialKiskill);
        speed = CruPara(speedR, speedMod, Math.Max(speedBuff * Tspeed, 0.1),
                        speedStyle * formaSpeed * racialSpeed * DmMath.Clamp(staminapercent * Ewillpower, 0.75, 1));
        magiskill = CruPara(magiR, magiMod, Math.Max(magiBuff + Tmagi, 0.1), Tmagimon * magiStyle);
        Statify();
    }

    /// <summary>O cru que da este `R`: `(R / fator - soma) / max(Mod, 0.1)`, no piso de 0,1 que o `Statify` poe no cru.</summary>
    private static double CruPara(double r, double mod, double soma, double fator) =>
        Math.Max((r / Math.Max(fator, 1e-9) - soma) / Math.Max(mod, 0.1), 0.1);

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
