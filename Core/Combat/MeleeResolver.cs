using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>O que aconteceu quando o soco chegou.</summary>
public enum Desfecho : byte
{
    Errou,        // a pontaria falhou
    Esquivou,     // o alvo saiu do caminho (custa Ki dele)
    Aparou,       // bloqueou com um membro
    Contra,       // aparou NA HORA certa e devolveu
    Acertou,
    Critico,
}

/// <summary>
/// O relato de um golpe. O servidor resolve UMA vez e conta a mesma historia pros dois lados
/// -- nenhum cliente recalcula dano.
/// </summary>
public struct GolpeResultado
{
    public Desfecho Desfecho;
    public double Dano;
    public string Membro;
    public bool Quebrou, Decepou, Nocauteou, Morreu;
    public double Stun;

    /// <summary>O rabo foi arrancado por este golpe (regra separada -- ver o passo 7).</summary>
    public bool RaboArrancado;

    public bool Encostou => Desfecho is Desfecho.Acertou or Desfecho.Critico or Desfecho.Aparou;
}

/// <summary>
/// A RESOLUCAO DE UM SOCO -- o `hitProc` do BYOND, reescrito.
///
/// A ordem importa e e esta: pontaria -> bloqueio -> esquiva -> critico -> dano -> corpo.
/// Cada passo pode encerrar o golpe. Roda so no SERVIDOR: o `prob()` da comparacao de
/// estilos torna o dano nao-deterministico, entao nao ha como as duas pontas concordarem
/// calculando cada uma por si.
///
/// OS QUATRO DEFEITOS DO ORIGINAL, CONSERTADOS AQUI (decisao do dono do projeto):
///
///  1. Os dois chamadores do `hitProc` passavam CINCO argumentos pra uma assinatura de SEIS.
///     O `Type` caia no slot do `forcehit` e o ultimo parametro ficava nulo. Efeito no jogo:
///     o soco NUNCA critava, NUNCA atordoava (`stunCount = 100 * null = 0`) e a esquiva
///     automatica do Ultra Instinct NUNCA disparava contra melee. Aqui os tres funcionam.
///
///  2. `parentlimb` nunca era atribuido, entao decepar o braco nao levava a mao. Corrigido
///     no <see cref="Body.Decepar"/>, que leva junto o que estava dentro.
///
///  3. O contra-ataque de bloqueio perfeito lia o tempo de guarda do ATACANTE, e por isso era
///     inalcancavel. Aqui le o do DEFENSOR, que e quem esta bloqueando.
///
///  4. `AttackMultiple` passava seis argumentos pra cinco parametros e embaralhava tudo (o
///     `iscrit` virava `vampdamage`). Aqui os golpes multiplos chamam esta mesma funcao, uma
///     vez por golpe, sem caminho paralelo.
/// </summary>
public static class MeleeResolver
{
    /// <summary>Janela, em segundos, pra um bloqueio virar contra-ataque.</summary>
    public const double JanelaContra = 0.25;

    /// <summary>Quanto tempo o corpo fica desligado depois de um nocaute.</summary>
    public const double SegundosDeNocaute = 12;

    /// <summary>
    /// Resolve um golpe de <paramref name="a"/> em <paramref name="d"/>.
    /// <paramref name="anguloGraus"/> e medido a partir da FRENTE do defensor ate a direcao
    /// de onde o golpe vem: 0 = de frente, 180 = pelas costas.
    /// </summary>
    public static GolpeResultado Resolver(CombatState a, CombatState d, double anguloGraus,
                                          Random rng, double tipo = 1, double addDano = 0)
    {
        var r = new GolpeResultado { Membro = "" };
        if (d.F.dead || d.Intocavel) return r;

        // golpear GASTA a carencia de quem acabou de renascer: o escudo e pra sair de perto,
        // nao pra voltar batendo de graca
        a.Carencia = 0;

        a.EntrarEmCombate();
        d.EntrarEmCombate();

        // Nocauteado nao esquiva nem bloqueia: quem esta no chao leva tudo.
        bool indefeso = d.F.KO;

        // === 1. PONTARIA ===============================================
        double bhit = CombatMath.Pontaria(a.F, d.F, indefeso ? 0 : d.Deflexao, a.Precisao);
        if (!indefeso && !Sorteou(rng, bhit)) return r;   // Errou

        // === 2. BLOQUEIO ==============================================
        if (!indefeso && d.Bloqueando)
        {
            BodyPart? guarda = EscolherGuarda(d.Corpo, rng);
            double custoKi = d.F.MaxKi * CombatKnobs.CustoKiDaGuarda;

            if (guarda == null) d.Guardar(false);        // sem braco nem perna nao ha o que erguer
            else if (d.F.Ki < custoKi) d.Guardar(false);  // sem energia a guarda cai sozinha
            else if (GuardaAguenta(d.Corpo, CombatMath.BpModulus(a.F.expressedBP, d.F.expressedBP), rng))
            {
                d.F.Ki -= custoKi;   // bloquear CUSTA: nao da pra segurar guarda a luta inteira

                // CONSERTO 3: a janela de contra le o tempo de guarda do DEFENSOR.
                if (d.ContraPronto && d.TempoDeGuarda <= JanelaContra)
                {
                    d.ContraPronto = false;
                    d.RecargaContra = CombatKnobs.RecargaDoContra;
                    r.Desfecho = Desfecho.Contra;
                    r.Stun = CombatKnobs.DuracaoStun;
                    a.Stun = Math.Max(a.Stun, r.Stun);
                    return r;
                }

                // O golpe entra TODO no membro que aparou, e ignora a zona mirada: e o preco
                // de bloquear, e o que faz braco de quem bloqueia muito acabar quebrado.
                double dmgB = Calcular(a, d, anguloGraus, tipo, addDano) * ReducaoDaGuarda(d.F);
                r.Desfecho = Desfecho.Aparou;
                r.Dano = dmgB;
                AplicarNoMembro(d, guarda, dmgB, a.Letal, ref r);
                return r;
            }
            // a guarda CEDEU: o golpe passa e entra inteiro
        }

        // === 3. ESQUIVA ================================================
        // CONSERTO 1: a esquiva autonoma agora e consultada de verdade contra socos.
        if (!indefeso && TentarEsquiva(d, rng))
        {
            r.Desfecho = Desfecho.Esquivou;
            return r;
        }

        // === 4. CRITICO ================================================
        // CONSERTO 1: no original o crit dependia de um parametro que chegava sempre nulo.
        bool crit = !indefeso && rng.NextDouble() * 100 < CombatKnobs.ChanceCrit;

        // === 5. DANO ===================================================
        double dano = Calcular(a, d, anguloGraus, tipo, addDano);
        if (crit)
        {
            dano *= (rng.Next(CombatKnobs.CritMin, CombatKnobs.CritMax + 1) + a.F.Etechnique) / 10.0;
            r.Stun = CombatKnobs.DuracaoStun;
            d.Stun = Math.Max(d.Stun, r.Stun);
        }

        // === 6. CORPO ==================================================
        BodyPart? membro = d.Corpo.Sortear(a.ZonaMirada, rng);
        if (membro == null) return r;   // corpo sem nada atingivel

        r.Desfecho = crit ? Desfecho.Critico : Desfecho.Acertou;
        r.Dano = dano;
        AplicarNoMembro(d, membro, dano, a.Letal, ref r);

        // === 7. O RABO ================================================
        ArrancarRabo(a, d, anguloGraus, dano, ref r);
        return r;
    }

    /// <summary>Dano minimo que arranca um rabo (o `dmg>5` do original).</summary>
    public const double DanoQueArrancaRabo = 5;

    /// <summary>Fracao de vida abaixo da qual o rabo pode ser arrancado (`hpratio<0.6`).</summary>
    public const double VidaParaPerderRabo = 0.6;

    /// <summary>
    /// ARRANCAR O RABO e uma regra a PARTE do sorteio de membro, e sempre foi.
    ///
    /// No original (`CombatMovement.dm:309-314`): o golpe precisa ter ENCOSTADO, o alvo tem
    /// que estar virado pro MESMO lado que o atacante (ou seja: pego de costas), o golpe tem
    /// que ser letal, o alvo tem que estar abaixo de 60% de vida e o dano acima de 5. Nao ha
    /// sorteio -- batendo nessas condicoes, o rabo VAI.
    ///
    /// Nao e detalhe cosmetico: sem rabo o Saiyajin perde o Oozaru.
    /// </summary>
    private static void ArrancarRabo(CombatState a, CombatState d, double anguloGraus,
                                     double dano, ref GolpeResultado r)
    {
        if (!a.Letal || dano <= DanoQueArrancaRabo) return;
        if (anguloGraus < 135) return;                       // so pelas costas
        if (d.Corpo.Vida() >= VidaParaPerderRabo * 100) return;

        BodyPart? rabo = d.Corpo.Achar("Rabo");
        if (rabo == null || rabo.Decepado) return;

        d.Corpo.Decepar(rabo);
        d.F.Ki = Math.Max(0, d.F.Ki - d.F.MaxKi * Regras.CustoDeceparKi);
        d.SincronizarVida();
        r.RaboArrancado = true;
    }

    /// <summary>
    /// A CADEIA DE DANO, na ordem do original. Cada termo esta explicado no
    /// <see cref="CombatMath"/>; o que muda aqui e so a sequencia, que e o que ninguem pode
    /// reordenar sem mudar o balanceamento inteiro.
    /// </summary>
    private static double Calcular(CombatState a, CombatState d, double anguloGraus,
                                   double tipo, double addDano)
    {
        double dmg = CombatMath.DanoBase(a.F, d.F);
        dmg = CombatMath.Resistencia(dmg, a.TiposDeDano, d.Resistencias);
        dmg += addDano;

        if (a.F.dashing) dmg += 2;          // entrar correndo soma impacto
        if (dmg < 1) dmg = 1;
        if (d.F.dashing) dmg *= 1.25;       // e ser pego correndo custa mais caro ainda

        // DE ONDE VEM O GOLPE, medido a partir da frente do DEFENSOR: 0 grau e de frente,
        // 180 e pelas costas. Pegar alguem de costas vale 1,5x -- e o que premia flanquear em
        // vez de trocar soco parado.
        dmg *= anguloGraus switch
        {
            >= 135 => 1.5,
            >= 90 => 1.4,
            >= 45 => 1.2,
            _ => 1.0,
        };

        dmg += tipo;                        // golpe pesado ja entra somando

        // A propria tecnica e defesa TIRAM dano do golpe. Parece invertido, mas e o freio que
        // segura a curva no fim do jogo: sem ele, dois veteranos se matam num soco. Ate 4,4x.
        double auto = (a.F.Etechnique * 2 + a.F.Ephysdef * 2) / 10;
        if (auto > 0.01) dmg /= auto;

        // e SO ENTAO o gap de poder entra, multiplicando tudo que sobrou
        dmg *= CombatMath.BpModulus(a.F.expressedBP, d.F.expressedBP);
        dmg = CombatMath.Armadura(dmg, d.F.Esuperkiarmor);

        return Math.Max(dmg, 0);
    }

    private static void AplicarNoMembro(CombatState d, BodyPart membro, double dano, bool letal,
                                        ref GolpeResultado r)
    {
        bool eraQuebrado = membro.Quebrado;
        d.Corpo.Ferir(membro, dano, letal);

        r.Membro = membro.Nome;
        r.Quebrou = !eraQuebrado && membro.Quebrado;

        // So golpe LETAL arranca membro, e so um membro que ja estava zerado. Nucleo nao se
        // decepa por soco -- cabeca arrancada e coisa de tecnica, nao de troca de golpes.
        if (letal && membro.Vida <= 0 && membro.Papel == Vitalidade.Membro && !membro.Aninhado)
        {
            d.Corpo.Decepar(membro);
            d.F.Ki = Math.Max(0, d.F.Ki - d.F.MaxKi * Regras.CustoDeceparKi);
            r.Decepou = true;
        }

        d.SincronizarVida();

        // MORTE antes de NOCAUTE: quem morreu nao precisa cair primeiro. Raca que regenera
        // membro perdido entra em coma no lugar de morrer.
        if (d.Corpo.DeveMorrer() && !d.Corpo.RegeneraDecepado)
        {
            r.Morreu = true;
            d.Morrer();
        }
        else if (!d.F.KO && d.Corpo.DeveNocautear())
        {
            r.Nocauteou = true;
            d.Nocautear(SegundosDeNocaute);
        }
    }

    /// <summary>
    /// O membro que apara: braco tem tres vezes mais chance que perna -- e o reflexo natural.
    /// Membro quebrado ou perdido nao entra no rodizio.
    /// </summary>
    public static BodyPart? EscolherGuarda(Body corpo, Random rng)
    {
        var candidatos = new List<(BodyPart P, double Peso)>();
        foreach (BodyPart p in corpo.Partes)
        {
            if (p.Decepado || p.Quebrado || p.Aninhado) continue;
            if (p.Zona == "bracos") candidatos.Add((p, 3));
            else if (p.Zona == "pernas") candidatos.Add((p, 1));
        }
        if (candidatos.Count == 0) return null;

        double total = 0;
        foreach ((BodyPart _, double peso) in candidatos) total += peso;

        double sorte = rng.NextDouble() * total;
        foreach ((BodyPart p, double peso) in candidatos)
        {
            sorte -= peso;
            if (sorte <= 0) return p;
        }
        return candidatos[^1].P;
    }

    /// <summary>
    /// A guarda aguenta? Tres coisas a furam, e as tres importam:
    ///
    ///   * uma chance BASE -- sem ela o banco de prova mostrou 100% de golpes aparados com o
    ///     corpo inteiro, e segurar o bloqueio virava a jogada dominante do jogo;
    ///   * o GAP DE PODER -- quem e muito mais forte passa por cima da guarda, que e o que
    ///     todo Dragon Ball mostra;
    ///   * cada braco ou perna quebrado/perdido, 25% -- com o corpo em frangalhos nao ha
    ///     guarda que segure.
    /// </summary>
    public static bool GuardaAguenta(Body corpo, double gapDoAtacante, Random rng)
    {
        int ruins = 0;
        foreach (BodyPart p in corpo.Partes)
            if (!p.Aninhado && (p.Zona == "bracos" || p.Zona == "pernas") && (p.Decepado || p.Quebrado))
                ruins++;

        double falha = CombatKnobs.FalhaBaseGuarda
                     + ruins * 25
                     + Math.Max(0, gapDoAtacante - 1) * CombatKnobs.FalhaGuardaPorGap;

        return rng.NextDouble() * 100 >= Math.Min(falha, 95);
    }

    /// <summary>
    /// Quanto do golpe a guarda deixa passar. Escala pela tecnica de QUEM BLOQUEIA -- no
    /// original escalava pela do ATACANTE, e por isso um oponente de tecnica baixa fazia o
    /// bloqueio AMPLIFICAR o dano em vez de reduzir.
    /// </summary>
    private static double ReducaoDaGuarda(Fighter defensor)
    {
        double t = Math.Clamp(defensor.Etechnique / 10.0, 0, 1);
        return Math.Clamp(0.6 - t * 0.35, 0.25, 0.6);   // deixa passar de 60% ate 25%
    }

    /// <summary>
    /// A esquiva ATIVA: custa Ki e so existe pra quem tem <see cref="CombatState.ChanceEsquiva"/>
    /// acima de zero -- hoje ninguem tem. E o buraco por onde o Ultra Instinct entra depois.
    /// </summary>
    private static bool TentarEsquiva(CombatState d, Random rng)
    {
        if (d.ChanceEsquiva <= 0) return false;
        double custo = 0.05 * d.F.MaxKi / Math.Max(d.F.Etechnique, 0.1);
        if (d.F.Ki < custo) return false;
        if (rng.NextDouble() * 100 >= d.ChanceEsquiva) return false;

        d.F.Ki -= custo;
        return true;
    }

    private static bool Sorteou(Random rng, double pct) => rng.NextDouble() * 100 < pct;
}
