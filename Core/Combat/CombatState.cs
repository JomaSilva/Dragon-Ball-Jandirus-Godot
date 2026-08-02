using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>
/// O LUTADOR EM COMBATE: a ficha (<see cref="Fighter"/>) mais tudo que so existe enquanto
/// se troca golpe -- o corpo em partes, a guarda, o cronometro do proximo soco.
///
/// Fica separado da ficha de proposito. O <see cref="Fighter"/> tem 235 campos e vai inteiro
/// pro savefile; nada daqui deve ser gravado, porque nada daqui sobrevive a desconexao. Uma
/// guarda erguida ou um atordoamento nao sao patrimonio do personagem.
/// </summary>
public sealed class CombatState
{
    public Fighter F;
    public Body Corpo;

    // --- guarda ----------------------------------------------------------
    /// <summary>Guarda erguida (a tecla de bloqueio segurada).</summary>
    public bool Bloqueando;

    /// <summary>Ha quantos segundos a guarda subiu. Bloquear NA HORA vira contra-ataque.</summary>
    public double TempoDeGuarda;

    /// <summary>
    /// O contra-ataque desta guarda ainda esta disponivel.
    ///
    /// UM POR SUBIDA DE GUARDA, e com recarga. Sem as duas travas bastava martelar a tecla de
    /// bloqueio pra devolver TODO golpe que chegasse -- a janela reabria a cada toque, e
    /// defender virava a jogada dominante.
    /// </summary>
    public bool ContraPronto;

    /// <summary>Quanto falta pra poder contra-atacar de novo.</summary>
    public double RecargaContra;

    // --- esquiva e mira --------------------------------------------------
    /// <summary>Deflexao: derruba a pontaria de quem ataca. O Zanzoken soma +20 aqui.</summary>
    public double Deflexao;

    /// <summary>Precisao: soma na propria pontaria.</summary>
    public double Precisao;

    /// <summary>
    /// Chance de ESQUIVA AUTONOMA, em porcento. Zero pra todo mundo -- e o Ultra Instinct que
    /// enche este numero. No BYOND a esquiva existia mas nunca era consultada contra socos
    /// (ver o defeito 1 no <see cref="MeleeResolver"/>).
    /// </summary>
    public double ChanceEsquiva;

    /// <summary>Zona que este lutador esta mirando: "cabeca", "torso", "bracos", "pernas".</summary>
    public string? ZonaMirada;

    /// <summary>
    /// O `murderToggle`. DESLIGADO por padrao: um soco nao-letal nunca leva um membro abaixo
    /// do limiar de nocaute, entao da pra treinar e brigar sem matar sem querer.
    /// </summary>
    public bool Letal;

    // --- cronometros (em segundos) ---------------------------------------
    /// <summary>Quanto falta pra poder golpear de novo.</summary>
    public double Recarga;

    /// <summary>
    /// GOLPES SEGUIDOS que encostaram. Serve pro NIVEL do som de impacto -- o original
    /// escolhe entre baque pequeno, medio e grande com `min(3, tipo + min(1, combo_count))`,
    /// e e isso que faz uma sequencia de socos ESQUENTAR em vez de virar um metronomo.
    /// Zera ao errar, ao ser aparado e sozinho depois de <see cref="JanelaDeCombo"/>.
    /// </summary>
    public int Combo;
    public double ComboAte;

    /// <summary>Quanto tempo um golpe conta como "seguido" do anterior.</summary>
    public const double JanelaDeCombo = 1.2;

    /// <summary>Marca mais um golpe encaixado.</summary>
    public void SomarCombo()
    {
        Combo = ComboAte > 0 ? Combo + 1 : 1;
        ComboAte = JanelaDeCombo;
    }

    public void ZerarCombo() { Combo = 0; ComboAte = 0; }

    /// <summary>Quanto falta de atordoamento. Acima de zero, nao ataca nem anda direito.</summary>
    public double Stun;

    /// <summary>Quanto ainda dura a tag de "em combate" (90s no jogo original).</summary>
    public double EmCombate;

    /// <summary>Ate quando o corpo fica desligado depois de um nocaute, em segundos.</summary>
    public double NocauteRestante;

    /// <summary>
    /// CARENCIA DE RENASCIMENTO, em segundos. Quem acabou de voltar nao pode ser atingido.
    ///
    /// Nao e cortesia: sem isto, morrer ao lado de quem matou e renascer no mesmo ponto vira
    /// laco -- o teste de rede com dois robos mostrou exatamente isso, morte atras de morte
    /// no mesmo lugar. A carencia CAI no instante em que o renascido golpeia: escudo pra
    /// levantar e ir embora, nao pra voltar batendo.
    /// </summary>
    public double Carencia;

    /// <summary>Nao pode ser atingido agora.</summary>
    public bool Intocavel => Carencia > 0;

    /// <summary>
    /// Composicao do golpe por tipo, e o que o corpo resiste. Os defaults sao os do jogo:
    /// soco = 2 de fisico + 1 de energia contra resistencias 1, o que da o 1,5x conhecido.
    /// </summary>
    public Dictionary<string, double> TiposDeDano = new() { ["Physical"] = 2, ["Energy"] = 1 };
    public Dictionary<string, double> Resistencias = new() { ["Physical"] = 1, ["Energy"] = 1 };

    public CombatState(Fighter f, bool comRabo = false, bool regenera = false)
    {
        F = f;
        Corpo = Body.Novo(comRabo);
        Corpo.RegeneraDecepado = regenera;
        SincronizarVida();
    }

    /// <summary>
    /// A vida da ficha e SEMPRE derivada do corpo. Escrever `HP` em qualquer outro lugar
    /// criaria duas verdades, e a que o jogador ve (a barra) e sempre a do corpo.
    /// </summary>
    public void SincronizarVida() => F.HP = Corpo.Vida();

    /// <summary>Passagem de tempo: cronometros, guarda, saida do nocaute.</summary>
    public void Tick(double dt)
    {
        if (Recarga > 0) Recarga = Math.Max(0, Recarga - dt);
        if (RecargaContra > 0) RecargaContra = Math.Max(0, RecargaContra - dt);
        if (ComboAte > 0 && (ComboAte = Math.Max(0, ComboAte - dt)) == 0) Combo = 0;
        if (Carencia > 0) Carencia = Math.Max(0, Carencia - dt);
        if (Stun > 0) Stun = Math.Max(0, Stun - dt);
        if (EmCombate > 0) EmCombate = Math.Max(0, EmCombate - dt);

        TempoDeGuarda = Bloqueando ? TempoDeGuarda + dt : 0;

        F.IsInFight = EmCombate > 0;

        if (NocauteRestante > 0)
        {
            NocauteRestante = Math.Max(0, NocauteRestante - dt);
            if (NocauteRestante == 0) Levantar();
        }
    }

    /// <summary>Pode golpear agora? Nocauteado, morto ou atordoado, nao.</summary>
    public bool PodeAtacar() => !F.dead && !F.KO && Stun <= 0 && Recarga <= 0;

    /// <summary>
    /// Ergue ou baixa a guarda. Subir REARMA o cronometro (e ele que decide o contra-ataque)
    /// e so libera o contra se a recarga ja tiver passado.
    /// </summary>
    public void Guardar(bool erguida)
    {
        if (erguida && !Bloqueando)
        {
            TempoDeGuarda = 0;
            ContraPronto = RecargaContra <= 0;
        }
        Bloqueando = erguida && !F.KO && !F.dead;
        if (!Bloqueando) ContraPronto = false;
    }

    /// <summary>Marca o inicio (ou a renovacao) da luta pros dois lados.</summary>
    public void EntrarEmCombate() => EmCombate = CombatKnobs.TagDeCombate;

    /// <summary>
    /// Cai. A guarda cai junto -- quem esta desmaiado nao bloqueia -- e o corpo fica um tempo
    /// desligado antes de responder de novo.
    /// </summary>
    public void Nocautear(double segundos)
    {
        F.KO = true;
        Bloqueando = false;
        ContraPronto = false;
        TempoDeGuarda = 0;
        NocauteRestante = segundos;
    }

    /// <summary>
    /// Levanta do nocaute. Os nucleos voltam pra um pouco ACIMA do limiar -- senao o
    /// personagem se levanta ja abaixo dele e cai no proximo tick, pra sempre.
    /// </summary>
    public void Levantar()
    {
        F.KO = false;
        NocauteRestante = 0;
        double piso = Regras.LimiarQuebra * 1.5;
        foreach (BodyPart p in Corpo.Partes)
        {
            if (p.Decepado) continue;
            double min = p.VidaMax * piso;
            if (p.Vida < min) p.Vida = min;
        }
        SincronizarVida();
    }

    /// <summary>Morrer. O corpo volta inteiro: ninguem renasce sem braco.</summary>
    public void Morrer()
    {
        F.dead = true;
        F.KO = false;
        Bloqueando = false;
        NocauteRestante = 0;
        EmCombate = 0;
    }

    public void Reviver(double fracaoDeVida = 1, double carencia = 0)
    {
        F.dead = false;
        F.KO = false;
        Carencia = carencia;
        Corpo.Restaurar();
        if (fracaoDeVida < 1)
            foreach (BodyPart p in Corpo.Partes) p.Vida = p.VidaMax * fracaoDeVida;
        SincronizarVida();
    }
}
