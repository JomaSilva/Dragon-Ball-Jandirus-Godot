namespace Jandirus.Core.Stats;

/// <summary>
/// COMO O PODER CRESCE. Cada atividade do jogo tem a sua porta aqui, e todas passam pelo
/// mesmo funil:
///
///     base fixa x raca/classe x MARCO      (BpGainBase -- nunca o proprio BP)
///       x taxa global x ritmo da atividade x Egains x peso x gravidade
///       -> CapCheck -> BP
///
/// Duas coisas explicam o desenho:
///
///  1) O ganho NAO escala com o BP proprio. Antes escalava, e ai quem era forte enriquecia
///     mais rapido: bola de neve. Hoje o crescimento e LINEAR dentro do patamar, e so um
///     MARCO (virar Super Saiyajin, forma perfeita, Golden) muda de patamar.
///  2) O CapCheck e a unica porta de entrada do BP. Nada aqui escreve `BP +=` sem passar por
///     ele -- excecao unica e declarada: o Zenkai, que e recompensa, nao treino.
///
/// PROBABILIDADE: todo `prob()` recebe o gerador de fora. Ganho de BP e autoridade do
/// servidor; sorteio escondido num objeto e sorteio que ninguem reproduz num teste.
/// </summary>
public sealed partial class Fighter
{
    // =====================================================================
    // MARCOS
    // =====================================================================
    /// <summary>
    /// Rompeu um patamar. Devolve o novo multiplicador quando ele SUBIU (pro chamador
    /// anunciar), ou 0 quando o marco ja era conhecido ou nao vale mais que o atual.
    /// </summary>
    public double ReachMilestone(string tag)
    {
        double m = Milestones.Valor(tag);
        if (m == 0 || !bp_milestones_done.Add(tag)) return 0;
        if (m <= bp_milestone_mult) return 0;
        bp_milestone_mult = m;
        return m;
    }

    /// <summary>Marcos de ASCENSAO, pras racas que nao tem forma: o degrau e o proprio BPBoost.</summary>
    public double CheckAscensionMilestone()
    {
        if (BPBoost >= 20) return ReachMilestone("asc20");
        if (BPBoost >= 10) return ReachMilestone("asc10");
        if (BPBoost >= 5) return ReachMilestone("asc5");
        return 0;
    }

    // =====================================================================
    // TREINO
    // =====================================================================
    /// <summary>
    /// Treinar (o verbo Train). O `mult` vem do laco: treinar bem, virando pra outra direcao
    /// a cada golpe, rende mais que socar o mesmo lado.
    /// </summary>
    public void TrainGain(Random rng, double mult = 1)
    {
        if (mult == 0) mult = 1;
        mult *= GainKnobs.GlobalTrainGain * zoneGainMult;

        if (BP < relBPmax)
        {
            // empurraozinho pra quem esta comecando do zero absoluto
            if (BP < 10 && (KiUnlockPercent == 1 || DmMath.Prob(rng, 25)) && DmMath.Prob(rng, 10)) BP += 1;

            double amount = BpGainBase() * GainKnobs.BPTick * TrainMod * Egains * weight * (1.0 / 9) * mult;

            if (missedtrain != 0) tmp_activ_gains++;
            else if (tmp_activ_gains > 0)
            {
                // DEFEITO VIVO PORTADO 1:1: aqui falta o `max(1, ...)` que o Attack_Gain e o
                // Blast_Gain tem. Com o acumulador pequeno (abaixo de 10) o "premio" vira
                // PENALIDADE -- tmp_activ_gains 5 multiplica o ganho por 0,5.
                amount *= Math.Min(25, tmp_activ_gains / 10);
                tmp_activ_gains = Math.Max(0, tmp_activ_gains - 25);
            }

            if (train_med_to_hp) { hiddenpotential += amount / 4; CapHiddenPotential(); }
            else BP += CapCheck(amount);

            // potencial escondido vira poder sozinho, mais rapido se ja passou do BP atual
            BP += CapCheck(hiddenpotential * GainKnobs.BPTick * (hiddenpotential >= BP ? 1.0 / 32 : 1.0 / 64));
        }

        if (DmMath.Prob(rng, 10)) maxstamina += 0.01 * weight;
        if (baseKi <= baseKiMax)
            baseKi += KiCapCheck(0.001 * TrainMod * BPrestriction * KiMod * baseKiMax / Math.Max(baseKi, 1e-9));
    }

    /// <summary>Meditar. Rende menos por tick que treinar, mas nao custa folego.</summary>
    public void MedGain(Random rng, double multi = 1)
    {
        if (multi == 0) multi = 1;

        if (BP < relBPmax)
        {
            if (BP < 10 && (KiUnlockPercent == 1 || DmMath.Prob(rng, 12)) && DmMath.Prob(rng, 11)) BP += 1;

            // 1/24 = 24 horas pra chegar num dado teto a 1x. (A meditacao NAO leva o
            // multiplicador de zona -- so o treino e o spar levam. E assim no DM.)
            double amount = CapCheck(BpGainBase() * GainKnobs.BPTick * GainKnobs.GlobalMedGain * multi
                                     * Egains * MedMod * (1.0 / 24));

            if (train_med_to_hp) hiddenpotential += amount / 4;
            else BP += amount;

            BP += CapCheck(hiddenpotential * GainKnobs.BPTick * (hiddenpotential >= BP ? 1.0 / 30 : 1.0 / 50));
        }

        if (baseKi <= baseKiMax)
            baseKi += KiCapCheck(0.005 * MedMod * BPrestriction * KiMod * baseKiMax / Math.Max(baseKi, 1e-9));
    }

    /// <summary>
    /// Trocar golpes. E o ganho mais rapido do jogo por design -- lutar de verdade ensina
    /// mais que socar o ar -- e o unico que passa pela TECNICA (Etechnique).
    /// </summary>
    public void AttackGain(Random rng, double mult = 1)
    {
        if (mult == 0) mult = 1;
        mult *= GainKnobs.GlobalSparGain * zoneGainMult;

        if (tmp_activ_gains > 0)
        {
            mult *= Math.Max(1, Math.Min(25, tmp_activ_gains / 10));
            tmp_activ_gains = Math.Max(0, tmp_activ_gains - 25);
        }

        // lutar em gravidade alta tambem acostuma o corpo, so que devagar
        double gravidade = Planetgrav + gravmult;
        if (gravidade > GravMastered)
            GravMastered += 0.00001 * gravidade * GravMod * GainKnobs.GlobalGravGain;

        if (BP < relBPmax)
        {
            if (BP < 10 && (KiUnlockPercent == 1 || DmMath.Prob(rng, 50))
                && DmMath.Prob(rng, 1) && DmMath.Prob(rng, 50)) BP += 1;

            BP += CapCheck(GainKnobs.BPTick * BpGainBase() * Etechnique * SparMod * Egains * weight * mult);
            BP += CapCheck(hiddenpotential * GainKnobs.BPTick * (hiddenpotential >= BP ? 1.0 / 6 : 1.0 / 12));
        }

        if (DmMath.Prob(rng, 20)) maxstamina += 0.01 * weight;
    }

    /// <summary>
    /// Soltar ki. Tem um freio proprio: o PRIMEIRO tiro de cada janela rende metade, e os
    /// seguintes dentro da mesma janela rendem cada vez menos -- e o que impede farmar BP
    /// segurando o botao de blast.
    /// </summary>
    public void BlastGain(Random rng, double mult = 1, bool ignoreMinuteshot = false)
    {
        if (mult == 0) mult = 1;
        mult *= zoneGainMult;

        double amount = GainKnobs.BPTick * BpGainBase() * Ekiskill * Egains * mult;
        double kamount = 0.055 * BPrestriction * KiMod * baseKiMax / Math.Max(baseKi, 1e-9);

        // DEFEITO VIVO PORTADO 1:1: gainscale e DIVISOR aqui, e MULTIPLICADOR nos alvos de
        // treino. Como ele cai a 0,5 pra quem esta no topo do servidor, dividir por ele
        // DOBRA o ganho de quem ja e o mais forte -- o oposto do que o nome sugere.
        double gainscale = Math.Max(1 - BP / Math.Max(GainKnobs.TopBP, 1), 0.5);
        if (DmMath.Prob(rng, 15)) gainscale = 1;

        // variar a direcao dos tiros conta como treino ativo
        if (lastdir != dir) { missedtrain = 0; lastdir = dir; }
        else missedtrain++;

        amount /= gainscale * (1 + DmMath.Ln(Math.Max(1, missedtrain)));

        if (missedtrain != 0) tmp_activ_gains++;
        else if (tmp_activ_gains > 0)
        {
            amount *= Math.Max(1, Math.Min(25, tmp_activ_gains / 10));
            tmp_activ_gains = Math.Max(0, tmp_activ_gains - 25);
        }

        if (!minuteshot || ignoreMinuteshot)
        {
            minuteshot = true;
            minuteshot_ig_ki = 1;
            amount *= 0.5;
            kamount *= 0.15;
            // DEFEITO VIVO PORTADO 1:1: o DM aplica o ganho de Ki base AQUI e de novo logo
            // abaixo -- o primeiro tiro de cada janela rende Ki base DOBRADO.
            if (baseKi <= baseKiMax && kamount != 0) baseKi += KiCapCheck(kamount);
        }
        else
        {
            minuteshot_ig_ki += 2;
            tmp_activ_gains++;
            double detractor = DmMath.Log(1.3, Math.Max(2, minuteshot_ig_ki));
            amount *= 0.18 / detractor;
            kamount *= 0.2 / detractor;
        }

        if (baseKi <= baseKiMax && kamount != 0) baseKi += KiCapCheck(kamount);

        if (train_med_to_hp) { hiddenpotential += amount / 15; CapHiddenPotential(); }
        else if (BP < relBPmax && amount != 0) BP += CapCheck(amount);
    }

    /// <summary>Voar treina o corpo devagar, e so quando se sai do lugar.</summary>
    public void FlightGain()
    {
        if (BP < relBPmax) BP += CapCheck(BpGainBase() * GainKnobs.BPTick * (1.0 / 24));
        if (baseKi <= baseKiMax)
            baseKi += KiCapCheck(0.001 * KiMod * baseKiMax / Math.Max(baseKi, 1e-9));
    }

    /// <summary>Nadar, idem -- so conta quando muda de direcao.</summary>
    public void SwimGain()
    {
        if (BP < relBPmax) BP += CapCheck(BpGainBase() * GainKnobs.BPTick * (1.0 / 30));
    }

    // =====================================================================
    // GRAVIDADE
    // =====================================================================
    /// <summary>
    /// A gravidade nao treina sozinha: ela MULTIPLICA o treino. Parado em 500x nao se ganha
    /// nada -- e preciso estar treinando, meditando ou lutando.
    ///
    /// O ganho segue a gravidade ABSOLUTA, nao a razao com a maestria. O sistema antigo de
    /// tiers zerava o ganho no instante em que a maestria alcancava a gravidade, o que
    /// empurrava todo mundo pra salas muito acima do que o corpo aguentava.
    ///
    /// ACLIMATACAO: quem ja domina mais gravidade do que sente ainda treina com a folga, mas
    /// so uma fracao dela -- estar de verdade na gravidade alta e sempre melhor.
    /// </summary>
    public void GravGain()
    {
        double gravidade = gravmult + Planetgrav;
        double effgrav = gravidade;
        if (GravMastered > gravidade) effgrav += (GravMastered - gravidade) * GainKnobs.GravAccustomWeight;

        bool treinando = train || med || IsInFight || minuteshot;
        if (effgrav > 0 && gravidade >= 1 && treinando)
            BP += CapCheck(BpGainBase() * GainKnobs.BPTick * TrainMod * Egains * GainKnobs.GlobalGravGain
                           * (effgrav / GainKnobs.GravGainDiv) * zoneGainMult);

        // o teto de aclimatacao so sobe encarando gravidade ACIMA dele
        if (gravidade > GravMastered)
        {
            GravMastered += 0.001 + GainKnobs.BPTick * 10 * gravidade * GravMod * GainKnobs.GlobalGravGain * 0.02;
            GravMastered = Math.Min(Math.Min(gravidade, GravMastered), GainKnobs.GravityCap);
        }
    }

    /// <summary>
    /// PESO x GRAVIDADE. O peso efetivo e o que se veste MULTIPLICADO pela gravidade local,
    /// comparado com o recorde do proprio corpo. Razao 1 = dobra os ganhos; acima disso
    /// esmaga (o dano e do sistema de combate).
    ///
    /// O teto `weight_cap_hw` e um RECORDE que nunca cai. Antes ele acompanhava o BP expresso,
    /// e como vestir peso DERRUBA o BP expresso, vestir peso derrubava o proprio teto: espiral
    /// de esmagamento. A semente desfaz o efeito do proprio peso pra nao se morder.
    /// </summary>
    public void WeightTick()
    {
        weight_cap_hw = Math.Max(weight_cap_hw, expressedBP * Math.Max(weight * BPrestriction, 1) * Ephysoff * 20);

        if (Weighted > 0)
        {
            weight_ratio = Weighted * Math.Max(Planetgrav + gravmult, 1) / Math.Max(weight_cap_hw, 1);
            weight = Math.Max(Math.Min(weight_ratio * 2, GainKnobs.WeightGainMax), 1);
            BPrestriction = weight;
        }
        else
        {
            weight = 1;
            weight_ratio = 0;
            BPrestriction = 1;
        }
    }

    // =====================================================================
    // O ACUMULADOR DE QUEM NAO ESTA TREINANDO
    // =====================================================================
    /// <summary>
    /// Enquanto o personagem NAO ganha BP (esta interpretando, conversando, parado), um
    /// acumulador enche devagar. No proximo treino ele entra de uma vez. E o que impede que
    /// jogar de roleplay signifique ficar permanentemente pra tras.
    ///
    /// O `Gaintimer` e o inverso: toda vez que o CapCheck paga alguma coisa ele volta pra 50,
    /// e so quando zera o acumulador volta a encher.
    /// </summary>
    public void BufferTick()
    {
        if (Gaintimer == 0 || minuteshot)
        {
            if (Buffertimer <= 3000)
            {
                Buffertimer++;
                BPBuffer += BpGainBase() * GainKnobs.BPTick * Egains * (1.0 / 35);
                if (BPBuffer > relBPmax) BPBuffer = relBPmax;
            }
        }
        else Gaintimer--;
    }

    /// <summary>Teto do potencial escondido, ancorado no maior BP do servidor.</summary>
    private void CapHiddenPotential()
    {
        hiddenpotential = Math.Min(GainKnobs.TopBP * 5, hiddenpotential);
        hiddenpotential = Math.Max(GainKnobs.TopBP / 25, hiddenpotential);
    }

    // =====================================================================
    // LUTAR COM ALGUEM MAIS FORTE
    // =====================================================================
    /// <summary>
    /// Encarar quem e mais forte multiplica o ganho pelo GAP de poder, com teto de 2x. Contra
    /// o proprio mestre o teto sobe pra 3x e a escala usa o BP BASE -- o poder expresso e
    /// distorcido por forma, raiva, supressao e Ki, e "mestre 3x mais forte = 3x de ganho" so
    /// e estavel comparando o poder de verdade.
    ///
    /// Nunca reduz: oponente igual ou mais fraco da 1x.
    /// </summary>
    public double FightGainMult(Fighter? outro, bool ehMeuMestre = false)
    {
        if (outro == null || ReferenceEquals(outro, this)) return 1;

        double meu = Math.Max(expressedBP, 1);
        double dele = Math.Max(outro.expressedBP, 1);
        double geral = Math.Min(Math.Max(dele / meu, 1), GainKnobs.FightGainCap);

        if (!ehMeuMestre) return geral;

        double r = Math.Max(outro.BP, 1) / Math.Max(BP, 1);
        double bonus = r < GainKnobs.MasterGainFloor ? 1 : Math.Min(r, GainKnobs.MasterGainCap);
        return Math.Max(geral, bonus);   // ter mestre nunca pode PIORAR o ganho
    }

    // =====================================================================
    // ZENKAI
    // =====================================================================
    /// <summary>
    /// Zenkai e exclusivo de DNA Saiyajin -- mais os Bio-Androides tipo Cell, que carregam
    /// celulas Saiyajin. Nenhuma outra raca tem nada disso.
    /// </summary>
    public bool HasZenkai()
    {
        if (Race == "Bio-Android" || ParentRace == "Bio-Android") return true;
        if (Race == "Saiyan" || ParentRace == "Saiyan") return true;
        if (Race == "Halfbreed" || Race == "Half-Saiyan" || ParentRace == "Half-Saiyan") return true;
        if (canSSJ) return true;
        if (SaiyanLineage.Length > 0) return true;
        return Genoma != null && Genoma.RacePercent("Saiyan") >= 25;
    }

    /// <summary>BP alem do qual o corpo nao arranca mais forca das derrotas.</summary>
    public double ZenkaiCeiling() => ssj3LearnReq > 0 ? ssj3LearnReq : ssj3at * GainKnobs.ZenkaiCapSSJ3Mid;

    /// <summary>Por que o Zenkai nao veio desta vez. Vazio = veio.</summary>
    public enum ZenkaiResult { Concedido, SemDnaSaiyajin, InimigoMaisFraco, Aposentado, EmRecarga, Morto }

    /// <summary>
    /// Ser DERROTADO por alguem mais forte arranca poder do corpo. Pago NA HORA, direto no BP
    /// base e SEM passar pelo CapCheck: e recompensa, nao treino. (O "banco" antigo comia o
    /// ganho -- gotejava via CapCheck e a diferenca evaporava.)
    ///
    /// <paramref name="poderInimigo"/> e o poder EFETIVO do inimigo, nao o BP base: um inimigo
    /// transformado conta como mais forte mesmo com base menor.
    /// O teto e sobre o BP FINAL: um Zenkai comum no maximo DOBRA a base; com o corpo em
    /// farrapos, no maximo TRIPLICA.
    /// </summary>
    public ZenkaiResult GainZenkai(double poderInimigo, long agoraMs, bool muitoFerido = false, bool classeKaio = false)
    {
        double meuPoder = Math.Max(expressedBP, BP);
        if (poderInimigo <= 0 || poderInimigo <= meuPoder) return ZenkaiResult.InimigoMaisFraco;
        if (dead) return ZenkaiResult.Morto;
        if (!HasZenkai()) return ZenkaiResult.SemDnaSaiyajin;

        // a classe Kaio (corpo do desejo divino) tem Zenkai sem limite
        if (BP >= ZenkaiCeiling() && !classeKaio) return ZenkaiResult.Aposentado;
        if (agoraMs < zenkaiReady) return ZenkaiResult.EmRecarga;

        double pct = muitoFerido ? GainKnobs.ZenkaiPctInjured : GainKnobs.ZenkaiPct;
        double capmult = muitoFerido ? 3 : 2;

        double bruto = pct * poderInimigo;
        double teto = BP * (capmult - 1);
        UltimoZenkai = Math.Min(bruto, teto);
        UltimoZenkaiNoTeto = bruto >= teto;

        BP += UltimoZenkai;
        zenkaiReady = agoraMs + GainKnobs.ZenkaiCooldownMs;
        return ZenkaiResult.Concedido;
    }

    /// <summary>Tamanho do ultimo Zenkai concedido (pro chamador narrar sem repetir a conta).</summary>
    public double UltimoZenkai;
    public bool UltimoZenkaiNoTeto;
}
