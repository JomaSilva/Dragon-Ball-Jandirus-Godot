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
        mult *= GainKnobs.GlobalTrainGain * zoneGainMult * racialGanhoDeTreino;   // (v0.4.0) o Persistence -- `Fighter.racialGanhoDeTreino`

        // (2026-10-16) O LUGAR MULTIPLICA A ATIVIDADE: a gravidade efetiva na parcela da atividade, que ja levava o peso e a zona. E
        // o numero do painel -- ver `FatorDaGravidade`. A parcela do potencial oculto NAO o leva (dono, 2026-10-20): `LugarDoPotencialOculto`.
        double lugar = FatorDaGravidade();

        if (BP < relBPmax)
        {
            // empurraozinho pra quem esta comecando do zero absoluto
            if (BP < 10 && (KiUnlockPercent == 1 || DmMath.Prob(rng, 25)) && DmMath.Prob(rng, 10)) BP += 1;

            double amount = BpGainBase() * GainKnobs.BPTick * TrainMod * Egains * weight * (1.0 / 9) * mult * lugar;

            if (missedtrain != 0) tmp_activ_gains++;
            else if (tmp_activ_gains > 0)
            {
                // DEFEITO VIVO PORTADO 1:1: aqui falta o `max(1, ...)` que o Attack_Gain e o
                // Blast_Gain tem. Com o acumulador pequeno (abaixo de 10) o "premio" vira
                // PENALIDADE -- tmp_activ_gains 5 multiplica o ganho por 0,5.
                amount *= Math.Min(25, tmp_activ_gains / 10);
                tmp_activ_gains = Math.Max(0, tmp_activ_gains - 25);
            }

            BP += CapCheck(amount);

            // potencial escondido vira poder sozinho, mais rapido se ja passou do BP atual -- e sem o lugar, o peso e a zona, como no
            // DM (`Train.dm:26-29`): ver `LugarDoPotencialOculto`
            BP += CapCheck(hiddenpotential * GainKnobs.BPTick * (hiddenpotential >= BP ? 1.0 / 32 : 1.0 / 64)
                           * LugarDoPotencialOculto(lugar * weight * zoneGainMult));
        }

        if (DmMath.Prob(rng, 10)) maxstamina += 0.01 * weight;
        if (baseKi <= baseKiMax)
            baseKi += KiCapCheck(0.001 * TrainMod * BPrestriction * KiMod * baseKiMax / Math.Max(baseKi, 1e-9));
    }

    /// <summary>Meditar. Rende menos por tick que treinar, mas nao custa folego.</summary>
    public void MedGain(Random rng, double multi = 1)
    {
        if (multi == 0) multi = 1;
        double lugar = FatorDaGravidade();   // (2026-10-16) a gravidade multiplica a meditacao tambem (a atividade) -- ver `FatorDaGravidade`

        if (BP < relBPmax)
        {
            if (BP < 10 && (KiUnlockPercent == 1 || DmMath.Prob(rng, 12)) && DmMath.Prob(rng, 11)) BP += 1;

            // 1/24 = 24 horas pra chegar num dado teto a 1x. (A meditacao NAO leva o
            // multiplicador de zona -- so o treino e o spar levam. E assim no DM.)
            // (2026-10-13) MEDITAR RENDE MENOS QUE TREINAR, nas duas parcelas -- ver `GainKnobs.MeditarRende`.
            double amount = CapCheck(BpGainBase() * GainKnobs.BPTick * GainKnobs.GlobalMedGain * multi
                                     * Egains * MedMod * (1.0 / 24) * GainKnobs.MeditarRende * lugar);

            BP += amount;

            BP += CapCheck(hiddenpotential * GainKnobs.BPTick * (hiddenpotential >= BP ? 1.0 / 30 : 1.0 / 50)
                           * GainKnobs.MeditarRende * LugarDoPotencialOculto(lugar));   // sem o lugar, como no DM (`Meditate.dm:12-15`)
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

            // (2026-10-16) a gravidade multiplica a luta -- ver `FatorDaGravidade` (o potencial oculto nao: `LugarDoPotencialOculto`,
            // `combatgains.dm:156-159`)
            double lugar = FatorDaGravidade();
            BP += CapCheck(GainKnobs.BPTick * BpGainBase() * Etechnique * SparMod * Egains * weight * mult * lugar);
            BP += CapCheck(hiddenpotential * GainKnobs.BPTick * (hiddenpotential >= BP ? 1.0 / 6 : 1.0 / 12) * LugarDoPotencialOculto(lugar));
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

        double amount = GainKnobs.BPTick * BpGainBase() * Ekiskill * Egains * mult * FatorDaGravidade();   // (2026-10-16) o lugar multiplica
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

        if (BP < relBPmax && amount != 0) BP += CapCheck(amount);
    }

    /// <summary>
    /// Voar treina o corpo devagar -- o ganho mais fraco do jogo (`Movement.dm:6-11`).
    ///
    /// PAGA TODO TIQUE EM QUE SE VOA, parado ou nao, e isso NAO e descuido do port: o `lastloc` do
    /// original e declarado LOCAL ao proc (`var/lastloc` dentro do `Flight_Gain()`), entao ele nasce
    /// nulo em toda chamada e o `lastloc != loc` e sempre verdadeiro. A guarda "so quando sai do
    /// lugar" que o nome promete nunca funcionou no BYOND. Quem chama e que da a cadencia de 5 Hz
    /// do `Stats()` -- ver `GameServer.Voo.TickDoVoo`.
    ///
    /// A LINHA DO KI FICA FORA DO GATE DE PROPOSITO (e 1:1 com o DM): o `baseKi` sobe mesmo com o BP
    /// no teto, entao voar continua rendendo pra quem ja esta capado.
    /// </summary>
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
    /// QUANTO A GRAVIDADE MULTIPLICA O TIQUE -- a gravidade de verdade do chao (o planeta mais o campo de maquina), nunca abaixo
    /// de 1: o espaco (gravidade zero) nao zera o treino, so nao ajuda. Multiplica a parcela da ATIVIDADE no treino, na meditacao,
    /// na luta e no ki -- a do potencial oculto nao (dono, 2026-10-20: ver <see cref="LugarDoPotencialOculto"/>). Parado nao se
    /// ganha nada em gravidade nenhuma: ela multiplica o que a atividade paga, e quem nao faz nada nao recebe nada pra multiplicar.
    ///
    /// ============================ ELA SO SOMAVA, E SOMAVA QUASE NADA (dono, 2026-10-16) ============================
    /// *"em 1x gravidade diz 4,8x e em 10x diz 15,63x oq daria uma grande diferença, pq eu lembro q no DM esses valores
    /// realmente multiplicavam o bp gain e aqui deveria ser assim tb"*. O `Grav_Gain` do DM e uma parcela A PARTE --
    /// `gravidade/1500` da base por chamada -- e la ela pesava: o treino pagava no maximo a cada 4 s (`trainproc`, o
    /// `spawn(40)`) e a gravidade umas 8 vezes por segundo (o `statify` a chama duas vezes por volta, `sleep(3)`), entao 10x
    /// rendia ~+50% e 100x ~6x o treino. Aqui o treino paga a 5 Hz e a parcela da gravidade ficou no mesmo tique: em 10x ela
    /// era +2% do treino, enquanto o painel dizia "10x". DIVERGENCIA DECLARADA: a gravidade agora MULTIPLICA, e o painel
    /// (`MultiplicadorDeGanho`) e este fator vezes a area, o peso e a zona -- o que o tique de treino paga.
    ///
    /// `GlobalGravGain` (o botao de admin) escala o EXCESSO acima de 1: 1 = a gravidade inteira, 0 = a gravidade nao conta.
    /// ===========================================================================================================
    ///
    /// ============================ A ACLIMATACAO NAO ENTRA AQUI (dono, 2026-10-16) ============================
    /// *"o aclmiation deveria so ter a parte q da um pequeno multiplicador de bp quando vc vai pra uma gravidade abaixo da sua
    /// masterizada, fora isso ele n deveria dar ganho de bp eterno, isso quebra um pouco as coisas"*. No DM a folga da maestria
    /// (`(GravMastered - gravidade) x 0,2`) somava na gravidade do `Grav_Gain`; com a gravidade multiplicando, ela virava um
    /// multiplicador ETERNO de treino (dominar 100x num chao de 1x valia 20,8x). Saiu: a aclimatacao e so o bonus de PODER do
    /// `gravFelt` (`Fighter.Power`, `StatCurves.GravFelt`), que a aba de Formas mostra. DIVERGENCIA DECLARADA.
    /// =======================================================================================================
    /// </summary>
    public double FatorDaGravidade()
    {
        if (GainKnobs.GravidadeNaoMultiplicaDeTeste) return 1;
        double gravidade = Planetgrav + gravmult;
        return 1 + Math.Max(gravidade - 1, 0) * Math.Max(GainKnobs.GlobalGravGain, 0);
    }

    /// <summary>
    /// O QUE O LUGAR FAZ COM O POTENCIAL OCULTO: nada -- 1, e a parcela pinga igual em qualquer chao, como no DM
    /// (`Train.dm:26-29`, `Meditate.dm:12-15`, `combatgains.dm:156-159`: `hiddenpotential*BPTick*(1/N)`, sem gravidade, peso
    /// ou zona). <paramref name="lugar"/> e o que a parcela da atividade da MESMA chamada leva, e e o que o defeito injetado devolve.
    ///
    /// ============================ OS 20 BILHOES (dono, 2026-10-20) ============================
    /// *"teve um jogador q de alguma forma treinou na gravidade de 500x com peso no maximo ... ganhou 20 BILHOES de bp"*. De
    /// 2026-10-16 a 2026-10-20 o lugar multiplicava o tique INTEIRO, potencial incluso (a escolha daquele dia, que o dono nao
    /// tinha confirmado). A atividade e linear e pequena (base fixa x marcos); o potencial nao: a One Hundred da `relBPmax*2`
    /// e a One Punch e a One Training `relBPmax*0,5` cada (`GanhoNaCompra`) -- de 14 a 20 vezes o BP do dia da compra -- e ele
    /// nunca se gasta. A 500x com o peso no teto (8x) a parcela pingava 4000x a do DM: medido fora do jogo, um Majin de 1B com
    /// as tres compradas com 1B ia a 14B em uma hora de treino e a 20B em tres (morto, o vigor nao acaba); no DM, a 1,03B. Sem
    /// as compras nada muda: uns 270 mil de BP por hora nesse mesmo lugar. O dono escolheu "como no DM": o lugar multiplica o
    /// esforco (treino, luta, meditacao, ki), e o potencial pinga cru.
    /// ======================================================================================
    /// </summary>
    private static double LugarDoPotencialOculto(double lugar) => GainKnobs.PotencialOcultoComOLugarDeTeste ? lugar : 1;

    /// <summary>
    /// A MAESTRIA DE GRAVIDADE: o teto de aclimatacao so sobe encarando gravidade ACIMA dele -- a cada passe de ficha de todo jogador
    /// (`GameServer.Treinar`), treinando ou nao, como no DM (`Gravity.dm:30-33`; ate 2026-10-18 so no passe do treino e da meditacao, e o
    /// peso, que cansa o treino pelo quadrado, a travava). O BP que este metodo pagava (a parcela `gravidade/1500`) saiu em 2026-10-16: a
    /// gravidade agora multiplica o tique, ver <see cref="FatorDaGravidade"/>.
    ///
    /// A maestria segue a gravidade ABSOLUTA, nao a razao. O sistema antigo de tiers zerava o ganho no instante em que a
    /// maestria alcancava a gravidade, o que empurrava todo mundo pra salas muito acima do que o corpo aguentava.
    /// </summary>
    public void GravGain()
    {
        double gravidade = gravmult + Planetgrav;

        // o teto de aclimatacao so sobe encarando gravidade ACIMA dele
        if (gravidade > GravMastered)
        {
            // a maestria de gravidade nao e BP: o ganho de BP ficou 20x menor (2026-10-16), ela segue no ritmo do DM
            GravMastered += 0.001 + GainKnobs.BPTickDoDm * 10 * gravidade * GravMod * GainKnobs.GlobalGravGain * 0.02;
            GravMastered = Math.Min(Math.Min(gravidade, GravMastered), GainKnobs.GravityCap);
        }
    }

    /// <summary>
    /// QUANTO O TREINO ESTA RENDENDO AGORA, em vezes -- o `bp_gain_mult()` do DM
    /// (`HtmlUI.dm:108-116`), que la existe **so** pra alimentar a linha "BP GAIN" do painel.
    ///
    /// ============================ SEM ESTE NUMERO O SISTEMA NAO EXISTE ============================
    /// Peso, gravidade e Sala do Tempo sao tres multiplicadores invisiveis: eles mudam quanto BP
    /// entra por tique, e BP por tique nao se ve. A queixa que abriu esta camada e literalmente essa
    /// -- "o sistema e invisivel e parece nao existir". Uma linha na tela e o que transforma tres
    /// numeros escondidos numa DECISAO (vale a pena carregar mais peso? vale ir pra Vegeta?).
    /// ==========================================================================================
    ///
    /// A conta e a razao entre o ganho de agora e o de uma sessao neutra (gravidade 1, sem peso,
    /// fora de zona especial) DO MESMO CORPO: `FatorDaGravidade * weight * zona`. Gravidade e LINEAR,
    /// entao 10x de gravidade le 10x -- e desde 2026-10-16 e isso que o tique de treino PAGA (antes a
    /// gravidade so somava uma parcela de `gravidade/1500`, e o painel mentia). E o numero do ESFORCO: o
    /// potencial oculto pinga o mesmo em qualquer lugar (2026-10-20, <see cref="LugarDoPotencialOculto"/>).
    ///
    /// O VIGOR ENTRA NO NUMERO (dono, 2026-10-21: *"coloque no bp gain no painel esse multiplicador de
    /// vigor, pros jogadores saberem"*). `StamBPGainMod` e 1,25 com o tanque em 62,5% ou mais e 0,5 no
    /// fundo. Em 2026-10-16 ele tinha saido daqui pra o 1x de gravidade nao aparecer como 1,25; o tique
    /// sempre pagou o vigor, e o painel agora mostra o que paga.
    ///
    /// ============================ UMA DIVERGENCIA DELIBERADA DO DM ============================
    /// O `bp_gain_mult()` original **nao inclui o `htc_gain_mult()`**: dentro da Sala do Tempo o
    /// painel do BYOND mostra ~10x (so a gravidade) enquanto o ganho de verdade e 2800x. E um furo do
    /// original, e copia-lo aqui seria construir a camada 2 inteira e continuar sem a unica coisa que
    /// o dono pediu -- ver o multiplicador. O <see cref="zoneGainMult"/> entra na conta.
    /// ======================================================================================
    /// </summary>
    public double MultiplicadorDeGanho()
    {
        double gmult = FatorDaGravidade();   // o MESMO fator que o tique paga (2026-10-16)

        // O PESO MULTIPLICA O TREINO, e so pra cima: `weight` nasce em 1 e o `WeightTick` ja o
        // prende entre 1 e 8.
        double wmult = weight > 1 ? weight : 1;

        return Math.Max(gmult, 0.01) * wmult * Math.Max(zoneGainMult, 0) * Math.Max(StamBPGainMod, 0);
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

    // =====================================================================
    // LUTAR COM ALGUEM MAIS FORTE
    // =====================================================================
    /// <summary>
    /// Encarar quem e mais forte multiplica o ganho pelo GAP de poder, com teto de 2x. Contra
    /// o proprio mestre o teto sobe pra 3x e a escala usa o BP BASE -- o poder expresso e
    /// distorcido por forma, raiva, supressao e Ki, e "mestre 3x mais forte = 3x de ganho" so
    /// e estavel comparando o poder de verdade.
    ///
    /// Nunca reduz: oponente igual ou mais fraco da 1x -- menos o MESTRE, que rende no minimo
    /// <see cref="GainKnobs.MasterGainMin"/> enquanto o aluno nao o SUPERAR em BP base (regras do dono, 2026-10-13 e
    /// 2026-10-16; no DM, abaixo de 1,2x de gap o bonus do mestre valia 1). Quem superou fica so com o gap geral.
    ///
    /// (2026-10-13) E CONTRA UM COLEGA -- outro aluno do mesmo mestre -- o ganho nunca e menor que
    /// <see cref="GainKnobs.ColegaDeMestreGain"/>: *"alunos treinando com alunos tb deveriam ganhar um bonus de ganho
    /// mas menor do q treinar com o mestre"*. Fixo, e nao pelo gap: ver a constante.
    /// </summary>
    public double FightGainMult(Fighter? outro, bool ehMeuMestre = false, bool ehColegaDeMestre = false)
    {
        if (outro == null || ReferenceEquals(outro, this)) return 1;

        // CONTRA BICHO NAO HA GAP A EXPLORAR (2026-10-10): socar um leao rende o que socar o ar rende.
        // Ver `Npc.Animais.SemBonusDeLuta`.
        if (Npc.Animais.SemBonusDeLuta(Race, outro.Race)) return 1;

        double meu = Math.Max(expressedBP, 1);
        double dele = Math.Max(outro.expressedBP, 1);
        double geral = Math.Min(Math.Max(dele / meu, 1), GainKnobs.FightGainCap);

        if (ehColegaDeMestre && !ehMeuMestre && !GainKnobs.ColegaSemBonusDeTeste)
            return Math.Max(geral, GainKnobs.ColegaDeMestreGain);
        if (!ehMeuMestre) return geral;

        // LINEAR PELA RAZAO DOS BPs BASE, entre o piso e o teto (`combatgains.dm:97-98`).
        double r = Math.Max(outro.BP, 1) / Math.Max(BP, 1);

        // (2026-10-16) QUEM SUPEROU O MESTRE NAO TEM MAIS O BONUS DELE -- regra do dono: *"se um aluno superar o mestre ele n
        // ganha mais o bonus de bp so ganhando a troca"*. Superar e passar dele em BP BASE, a mesma regua do bonus; fica o gap
        // geral, que contra um mestre mais fraco vale 1x (e a troca PvP por cima, no servidor). O lado do mestre nao muda: o
        // bonus nunca foi de quem ensina.
        if (r < 1 && !GainKnobs.AlunoQueSuperouComBonusDeTeste) return geral;
        double piso = GainKnobs.MestreSemPisoDeTeste ? 1 : GainKnobs.MasterGainMin;
        double bonus = Math.Clamp(r, piso, GainKnobs.MasterGainCap);
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
        // ============================ AS DUAS GRAFIAS, E ELA E O MOTIVO DE ESTE RAMO SER MUDO ============================
        // O DM chama a raca de `"Bio-Android"` (`combatgains.dm:14`) e o `races.json` deste port a
        // gravou como `"BioAndroid"`, sem hifen -- a mesma dobra que ja existe em Icer/Frost Demon,
        // Kanassa/Kanassa-Jin e SpiritDoll/Spirit Doll. Esta linha testava SO a grafia do DM, entao
        // ela nunca casou com ninguem: era um ramo escrito e inalcancavel.
        //
        // Hoje ele nao tem vitima (a raca nao existia em jogo). No dia em que o tanque abrir, o
        // Zenkai simplesmente NAO viria e ninguem saberia por que -- que e exatamente como os outros
        // seis dados extraidos-sem-consumidor deste port passaram meses sem serem notados.
        //
        // `BioAndroids.EhBio` E NAO DOIS LITERAIS: o predicado mora ao lado da lista de corpos, e
        // `GameServer.Sigilo.cs` e o `skilltrees.json` ja fazem a mesma pergunta em outros dois
        // lugares. Uma copia a mais e mais um lugar pra divergir.
        // =============================================================================================================
        // (v0.3.0) E O ZENKAI DO BIO VEM DO SANGUE, nao da raca: o dono (2026-10-15) -- *"zenkai se tiver saiyajin"*, e
        // *"bio androide sozinho N tem nada"*. Ver `Races.SangueDoBio`. O ramo acima continua sendo o porque de a grafia
        // importar: e ele que reconhece o bio.
        if (Races.BioAndroids.EhBio(Race) || Races.BioAndroids.EhBio(ParentRace))
            return Races.SangueDoBio.BioComOsPassivosDeAntesDeTeste || Races.SangueDoBio.TemSaiyajin(this);
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
