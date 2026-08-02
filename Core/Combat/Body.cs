namespace Jandirus.Core.Combat;

/// <summary>Que papel o membro tem na sobrevivencia.</summary>
public enum Vitalidade
{
    /// <summary>Braco, perna, rabo: quebrar ou perder atrapalha, mas nao mata.</summary>
    Membro,
    /// <summary>Cabeca, torso, abdomen: abaixo do limiar NOCAUTEIA; perdido, MATA.</summary>
    Nucleo,
    /// <summary>Cerebro, orgaos: vitais, mas so se ferem por propagacao (nunca sao mirados).</summary>
    Interno,
}

/// <summary>Uma parte do corpo.</summary>
public sealed class BodyPart
{
    public string Nome = "";
    public Vitalidade Papel = Vitalidade.Membro;

    public double Vida = 100, VidaMax = 100;
    public bool Decepado;

    /// <summary>Peso no sorteio de qual membro o golpe pega.</summary>
    public double Chance = 100;

    /// <summary>
    /// Membro ANINHADO: nunca sai no sorteio de um soco. Cerebro, orgaos, mao e pe so se
    /// ferem por propagacao ou por serem levados junto com o membro que os contem.
    /// </summary>
    public bool Aninhado;

    /// <summary>De quem este membro faz parte. Perder o braco leva a mao junto.</summary>
    public string? Dono;

    /// <summary>A zona que o jogador mira pra acertar este membro de proposito.</summary>
    public string Zona = "torso";

    public double Fracao => VidaMax <= 0 ? 0 : Math.Clamp(Vida / VidaMax, 0, 1);

    /// <summary>Quebrado: abaixo de 20% da propria vida. E o limiar de "Broken" do jogo.</summary>
    public bool Quebrado => !Decepado && Fracao < Regras.LimiarQuebra;

    public BodyPart Copiar() => (BodyPart)MemberwiseClone();
}

/// <summary>Os numeros que decidem quando o corpo cede.</summary>
public static class Regras
{
    /// <summary>Abaixo disto o membro esta QUEBRADO; num nucleo, isso e nocaute.</summary>
    public const double LimiarQuebra = 0.20;

    /// <summary>Quanto de um golpe na cabeca/torso escorre pro cerebro/orgaos.</summary>
    public const double Propagacao = 0.20;

    /// <summary>Decepar custa isto do Ki maximo do dono.</summary>
    public const double CustoDeceparKi = 0.20;

    /// <summary>Um membro devolvido por regeneracao volta com esta fracao de vida.</summary>
    public const double VidaAoRegenerar = 0.70;
}

/// <summary>
/// O CORPO em partes -- o `/datum/Body` do BYOND.
///
/// A vida do personagem NAO e um numero proprio: e a MEDIA das partes. Isso muda como o
/// combate funciona -- ninguem morre por acumular dano espalhado; morre porque um lugar
/// especifico cedeu. Mirar importa.
///
/// TRES REGRAS DE LETALIDADE, e so elas:
///   * qualquer NUCLEO (cabeca, torso, abdomen) abaixo de 20% -> NOCAUTE
///   * qualquer NUCLEO perdido ou zerado -> MORTE
///   * braco, perna, rabo: nunca matam nem nocauteiam, por pior que fiquem
/// </summary>
public sealed class Body
{
    public List<BodyPart> Partes = [];

    /// <summary>Raca que regenera membro perdido (Namekuseijin, Majin, Bio): entra em coma em vez de morrer.</summary>
    public bool RegeneraDecepado;

    public static Body Novo(bool comRabo = false)
    {
        var b = new Body();
        void P(string nome, Vitalidade papel, string zona, double chance, bool aninhado = false, string? dono = null)
            => b.Partes.Add(new BodyPart
            {
                Nome = nome, Papel = papel, Zona = zona, Chance = chance, Aninhado = aninhado, Dono = dono,
            });

        P("Cabeca", Vitalidade.Nucleo, "cabeca", 40);
        P("Cerebro", Vitalidade.Interno, "cabeca", 0, aninhado: true, dono: "Cabeca");
        P("Torso", Vitalidade.Nucleo, "torso", 100);
        P("Abdomen", Vitalidade.Nucleo, "torso", 70);
        P("Orgaos", Vitalidade.Interno, "torso", 0, aninhado: true, dono: "Torso");
        P("Reprodutor", Vitalidade.Membro, "torso", 10, aninhado: true, dono: "Abdomen");

        P("Braco esquerdo", Vitalidade.Membro, "bracos", 60);
        P("Mao esquerda", Vitalidade.Membro, "bracos", 0, aninhado: true, dono: "Braco esquerdo");
        P("Braco direito", Vitalidade.Membro, "bracos", 60);
        P("Mao direita", Vitalidade.Membro, "bracos", 0, aninhado: true, dono: "Braco direito");

        P("Perna esquerda", Vitalidade.Membro, "pernas", 50);
        P("Pe esquerdo", Vitalidade.Membro, "pernas", 0, aninhado: true, dono: "Perna esquerda");
        P("Perna direita", Vitalidade.Membro, "pernas", 50);
        P("Pe direito", Vitalidade.Membro, "pernas", 0, aninhado: true, dono: "Perna direita");

        if (comRabo) P("Rabo", Vitalidade.Membro, "torso", 15);

        return b;
    }

    public BodyPart? Achar(string nome) => Partes.FirstOrDefault(p => p.Nome == nome);

    /// <summary>
    /// A vida do personagem: MEDIA SIMPLES das partes que ainda existem.
    ///
    /// Decepado sai do DENOMINADOR em vez de contar zero -- senao perder um braco derrubaria
    /// a vida de todo mundo em ~7% de graca, e um manco viveria eternamente com meia vida.
    /// </summary>
    public double Vida()
    {
        int n = 0;
        double soma = 0;
        foreach (BodyPart p in Partes)
        {
            if (p.Decepado) continue;
            soma += p.Fracao * 100;
            n++;
        }
        return n == 0 ? 0 : soma / n;
    }

    // =====================================================================
    // ESTADO
    // =====================================================================
    private IEnumerable<BodyPart> Nucleos => Partes.Where(p => p.Papel == Vitalidade.Nucleo);

    /// <summary>Algum nucleo abaixo do limiar: o corpo desliga.</summary>
    public bool DeveNocautear() => Nucleos.Any(p => !p.Decepado && p.Fracao < Regras.LimiarQuebra);

    /// <summary>Algum nucleo zerado ou perdido: fim. Raca que regenera entra em coma.</summary>
    public bool DeveMorrer() => Nucleos.Any(p => p.Decepado || p.Vida <= 0);

    public IEnumerable<BodyPart> Quebrados() => Partes.Where(p => p.Quebrado);
    public IEnumerable<BodyPart> Perdidos() => Partes.Where(p => p.Decepado);

    /// <summary>
    /// "Extremamente ferido" -- o gatilho do Zenkai maior. E a fracao do corpo que esta
    /// quebrada ou perdida.
    /// </summary>
    public bool MuitoFerido(double fracao = 0.45)
    {
        if (Partes.Count == 0) return false;
        int ruins = Partes.Count(p => p.Decepado || p.Quebrado);
        return (double)ruins / Partes.Count >= fracao;
    }

    // =====================================================================
    // DANO
    // =====================================================================
    /// <summary>
    /// Sorteia qual membro o golpe pega. A zona mirada nao GARANTE o membro -- so pesa a
    /// favor dele. Errar a mira e parte do jogo.
    /// </summary>
    public BodyPart? Sortear(string? zona, Random rng)
    {
        var elegiveis = Partes.Where(p => !p.Aninhado && !p.Decepado).ToList();
        if (elegiveis.Count == 0) return null;

        double total = 0;
        var pesos = new double[elegiveis.Count];
        for (int i = 0; i < elegiveis.Count; i++)
        {
            // fora da zona mirada o membro ainda pode ser pego, com um quarto do peso
            double w = elegiveis[i].Chance;
            if (zona != null && elegiveis[i].Zona != zona) w /= 4;
            pesos[i] = Math.Max(w, 0.01);
            total += pesos[i];
        }

        double sorte = rng.NextDouble() * total;
        for (int i = 0; i < elegiveis.Count; i++)
        {
            sorte -= pesos[i];
            if (sorte <= 0) return elegiveis[i];
        }
        return elegiveis[^1];
    }

    /// <summary>
    /// Machuca UM membro. Cabeca e torso escorrem parte do golpe pros aninhados -- e assim
    /// que o cerebro e os orgaos se ferem, ja que ninguem os acerta diretamente.
    /// </summary>
    public void Ferir(BodyPart alvo, double dano, bool letal)
    {
        if (alvo.Decepado || dano <= 0) return;

        alvo.Vida -= dano;

        // Golpe NAO-LETAL nunca leva o membro abaixo do limiar: e o que permite treinar e
        // brigar sem matar sem querer. O piso e o MESMO limiar do nocaute, entao um soco
        // nao-letal chega a nocautear -- so nao chega a matar.
        double piso = letal ? 0 : alvo.VidaMax * Regras.LimiarQuebra * 0.99;
        if (alvo.Vida < piso) alvo.Vida = piso;

        // a propagacao usa o dano CRU, nao o que sobrou depois do piso: um golpe absurdo na
        // cabeca ainda chacoalha o cerebro mesmo com a cabeca protegida pelo nao-letal
        foreach (BodyPart dentro in Partes)
            if (dentro.Dono == alvo.Nome && !dentro.Decepado)
                Ferir(dentro, dano * Regras.Propagacao, letal);
    }

    /// <summary>
    /// Arranca um membro. Leva junto o que estava DENTRO dele -- perder o braco leva a mao.
    ///
    /// No BYOND essa cascata existia no papel mas nunca rodava: `parentlimb` nunca era
    /// atribuido, porque a busca devolvia TYPEPATH em vez de instancia. Aqui roda.
    /// </summary>
    public List<BodyPart> Decepar(BodyPart alvo)
    {
        var caiu = new List<BodyPart>();
        if (alvo.Decepado) return caiu;

        alvo.Decepado = true;
        alvo.Vida = 0;
        caiu.Add(alvo);

        foreach (BodyPart dentro in Partes.Where(p => p.Dono == alvo.Nome).ToList())
            caiu.AddRange(Decepar(dentro));

        return caiu;
    }

    /// <summary>Cura espalhada por todos os membros que ainda existem.</summary>
    public void Curar(double quanto)
    {
        foreach (BodyPart p in Partes)
        {
            if (p.Decepado) continue;
            p.Vida = Math.Min(p.Vida + quanto, p.VidaMax);
        }
    }

    /// <summary>Devolve um membro perdido, com parte da vida -- e o que a regeneracao faz.</summary>
    public bool Regenerar(BodyPart alvo)
    {
        if (!alvo.Decepado) return false;
        alvo.Decepado = false;
        alvo.Vida = alvo.VidaMax * Regras.VidaAoRegenerar;
        return true;
    }

    /// <summary>Morrer devolve o corpo inteiro: ninguem renasce sem braco.</summary>
    public void Restaurar()
    {
        foreach (BodyPart p in Partes)
        {
            p.Decepado = false;
            p.Vida = p.VidaMax;
        }
    }

    public Body Copiar()
    {
        var b = new Body { RegeneraDecepado = RegeneraDecepado };
        foreach (BodyPart p in Partes) b.Partes.Add(p.Copiar());
        return b;
    }
}
