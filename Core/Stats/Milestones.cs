namespace Jandirus.Core.Stats;

/// <summary>
/// MARCOS DE PODER: o multiplicador de ganho so sobe quando o personagem rompe um patamar
/// da PROPRIA raca -- virar Super Saiyajin, atingir a forma perfeita do bio, o Golden do
/// Frost, um degrau de Ascensao pra quem nao tem forma.
///
/// E o que substituiu o crescimento composto. Antes o ganho era proporcional ao proprio BP,
/// entao quem ja era forte ficava forte mais rapido -- bola de neve. Agora o ritmo e LINEAR
/// dentro de cada patamar e da um SALTO discreto quando o corpo muda de categoria: dobrar o
/// tempo de treino dobra o ganho, e ficar mais forte so acelera se voce conquistar algo.
///
/// NAO EMPILHA: vale sempre o MAIOR marco atingido, nunca o produto.
///
/// ============================ QUEM CONCEDE CADA UM (2026-10-11) ============================
/// A tabela e o `BP_MILESTONES` do DM (`Stats/BP/LinearGain.dm:19-26`). Ate esta data so as do bio,
/// as da Ascensao, o `potential` e o `godki` tinham quem as concedesse neste port: as das FORMAS
/// estavam escritas e ninguem as ligava.
///
///   * AS DAS FORMAS saem do catalogo: cada forma diz a propria etiqueta (`FormaDef.Marco`) e quem
///     confere e uma funcao so, por estado (`Core/Forms/MarcosDeForma.cs`).
///   * `bio1`/`bio2`/`bio3`/`bio_ssj2`: `GameServer.Tech.cs` e `GameServer.Absorcao.cs`.
///   * `asc5`/`asc10`/`asc20`: <see cref="Fighter.CheckAscensionMilestone"/>.
///   * `potential`: `GameServer.Tecnicas.G11.cs`. `godki`: `Core/Forms/DespertarDivino.cs`.
///   * `majin2`/`majin3`: `GameServer.SagaDoMajin.cs` -- o `majin_advance_form` (`Magic/MajinSaga.dm:416-417`), desde que a
///     saga do Corrupted Majin foi portada (2026-10-16).
///
/// DUAS CONTINUAM SEM NINGUEM, e nao por esquecimento -- nao ha o que as conceda neste port:
///   * `frost_golden` -- o `Buff()` da Golden Form (`IcerTransform.dm:276`). A Golden nao foi portada
///     (`CensoDeSkills`: "a camada Golden do Frost Demon").
///   * `lssj4` -- NEM O DM A CONCEDE: o `LSSj_Controlled()` (`lssjbuff.dm:447-461`) faz `lssj=4` e nao
///     chama `bp_milestone_reach`. E aqui a forma Controlled nem existe mais: ela virou o topo da
///     rampa do `legendary` (25x a 50x). Vale o mesmo que o `lssj3` (o nivel do Blue: 32 desde o pedido 188), entao ninguem perde nada.
/// ===========================================================================================
/// </summary>
public static class Milestones
{
    // ============================ AS ETIQUETAS QUE O CATALOGO DE FORMAS CITA ============================
    // Em constante porque sao escritas em DOIS lugares por natureza -- aqui (quanto vale) e na entrada
    // da forma (`FormaDef.Marco`, quem concede). Etiqueta digitada duas vezes e etiqueta que um dia
    // diverge calada: `ReachMilestone` devolve 0 pra uma que nao conhece, e ninguem reclama.
    public const string Ssj = "ssj", SsjGrau2 = "ssjg2", SsjGrau3 = "ssjg3", Ssj2 = "ssj2", Ssj3 = "ssj3",
                        Ssj4 = "ssj4", Ssj4FullPower = "ssj4fp",
                        Lssj1 = "lssj1", Lssj2 = "lssj2", Lssj3 = "lssj3",
                        FrostFinal = "frost_final", Blue = "blue",
                        // (2026-10-30, o pedido 184) os niveis divinos -- ver `Tabela`
                        BlueEvolution = "blue_evolution", Ssj4LimitBreaker = "ssj4lb", NivelDaEvolucao = "nivel_evolution",
                        GoldenPerfect = "golden_perfect", TensaoDivina = "tensao_divina", UltraInstintoEEgo = "ui_ue",
                        BlueKaioken = "blue_kaioken", AscensaoDeus = "asc_deus", AscensaoBlue = "asc_blue";

    /// <summary>
    /// (2026-10-30, o pedido 188) O PATAMAR DE NIVEL x VALE 2^x -- o dono: *"seria melhor ser 2^x onde x e o milestone, comecando em 1x pre
    /// ssj, e 1,5x no primeiro potential unlock como era antes, a mudanca ta no pos ssj, assim ficando ssj = 2x bp gain ssj2 = 4x ssj3 = 8x
    /// ssj4/godki = 16x blue/rose/ssj4fp = 32x evolution/rose2/ssj4lb = 64x UI/UE= 128x"*. O nivel 1 e o do SSJ, o 2 o do SSJ2, o 3 o do
    /// SSJ3, o 4 o do SSJ4 e do ki divino, o 5 o do Blue, o 6 o do Blue Evolution e o 7 o do Ultra Instinto e do Ultra Ego.
    ///
    /// AS OUTRAS RACAS E A ASCENSAO FICAM NO MESMO NIVEL DE ANTES, so com o valor novo: nos divinos, o nivel do pedido 184 (pelo multiplicador
    /// de entrada da forma); embaixo, o do DM, que e o valor dele MENOS UM (o `ssj` 2 e o nivel 1, o `bio3` 3 o nivel 2, o `asc20` 4 o nivel
    /// 3) -- a mesma conta que leva o `ssj4`/`godki` 5 do DM aos 16 e o `blue`/`ssj4fp` 6 aos 32 do dono. O `potential` e o `bio1` continuam
    /// 1,5x. Ate aqui: 2/3/4 embaixo (o DM) e 8/10/12/20 nos divinos (o pedido 184).
    /// </summary>
    public static double Patamar(int nivel) => Math.Pow(2, nivel);

    /// <summary>
    /// (2026-10-30, o pedido 184) OS NIVEIS DIVINOS DO DONO -- *"faça com q milestone de god ki/ssj4 e tudo acima disso dobre [...] e todas as
    /// formas e ascenscoes desses niveis tb"*, e as respostas dele: o Blue Kaio-ken vem *"ao atingir 50% de god ki mastery"*, as formas das
    /// outras racas entram pelo multiplicador (*"ta ok"*), e *"ultra ego e ultra instinto pode fazer como um molestone novo"*. Pelo
    /// multiplicador de entrada da forma: ~20-22x e o nivel do SSJ4 e do SSG (4), 32x o do Blue e do SSJ4 FP (5), 56x o do Blue Evolution e do
    /// LB (6). A Fury (72x) fica sem marco (o dono: quem chega nela ja passou pelo UI ou pelo UE). Em 2^x desde o pedido 188 (eram 8, 10, 12 e
    /// 20). Quem concede as do servidor: o `blue_kaioken` (`GameServer.MarcosDeForma`) e as da Ascensao (`Fighter.CheckAscensionMilestone`).
    /// </summary>
    public const double NivelDoDeus = 16, NivelDoBlue = 32, NivelDaEvolucaoDivina = 64, NivelDasDisciplinas = 128;

    /// <summary>
    /// OS DOIS GRAUS DO SUPER SAIYAJIN, no lugar do Ultra Super Saiyajin.
    ///
    /// ============================ DIVERGENCIA DECLARADA, DECIDIDA PELO DONO ============================
    /// No DM a etiqueta e UMA: `"ussj" = 2.5`, concedida pelo `Ultra_SSj(grade)` quando o corpo ENTRA
    /// num grau, qualquer dos dois (`supersaiyanbuff.dm:404` -- la o USSJ ja virou "os grades do SSJ1",
    /// mas o marco ficou um so). O dono, em 2026-10-11: *"sim pode ligar, mas lembre q USSJ n existe
    /// mais, oq tem e ssj grade 2 e ssj grade 3 no lugar, logo esse 2,5x teria q distribuir entre
    /// eles"* -- e, sobre a divisao proposta: *"pode ser essa distribuicao"*.
    ///
    /// A escada fica crescente, e (2026-10-30, o pedido 188) os graus dividem em tercos o vao entre o SSJ e o SSJ2 -- o dono: *"Os grades
    /// do ssj ficariam entre o 2x e 4x assim ficando grade 2 = 2,666...x e grade 3= 3,3333x"*:
    ///     Super Saiyajin x2  ->  Grau 2 x2,67 (8/3)  ->  Grau 3 x3,33 (10/3)  ->  Super Saiyajin 2 x4.
    /// Eram x2,25 e x2,5 (o 2,5 do USSJ distribuido entre os dois). O marco de cada grau vem na primeira vez que o corpo ENTRA nele (o
    /// instante do original), e nao quando a maestria do SSJ1 o destrava.
    /// ================================================================================================
    /// </summary>
    public const double ValorDoGrau2 = 8.0 / 3, ValorDoGrau3 = 10.0 / 3;

    public static readonly Dictionary<string, double> Tabela = new(StringComparer.Ordinal)
    {
        // (2026-10-30, o pedido 188) TUDO EM 2^x, cada etiqueta no nivel dela -- ver `Patamar`
        // Saiyajins e racas que viram Super Saiyajin (o `ussj` do DM virou os dois graus -- ver acima)
        [Ssj] = Patamar(1), [SsjGrau2] = ValorDoGrau2, [SsjGrau3] = ValorDoGrau3,
        // o SSJ4 e o FP nos niveis divinos do dono (o pedido 184)
        [Ssj2] = Patamar(2), [Ssj3] = Patamar(3), [Ssj4] = NivelDoDeus, [Ssj4FullPower] = NivelDoBlue,
        [Ssj4LimitBreaker] = NivelDaEvolucaoDivina,
        // escada Legendary: o LSSJ2 no nivel do SSJ3 (o 4 do DM); o LSSJ3 no do FP, que ele empatava (o pedido 184)
        [Lssj1] = Patamar(1), [Lssj2] = Patamar(3), [Lssj3] = NivelDoBlue, ["lssj4"] = NivelDoBlue,
        // Bio-Androide: o `bio1` fica 1,5x como o potencial; os outros nos niveis do DM; a Golden Perfect (22x) no do SSG (o pedido 184)
        ["bio1"] = 1.5, ["bio2"] = Patamar(1), ["bio3"] = Patamar(2), ["bio_ssj2"] = Patamar(3), [GoldenPerfect] = NivelDoDeus,
        // Majin (saga)
        ["majin2"] = Patamar(1), ["majin3"] = Patamar(2),
        // Frost Demon: a forma 7 (o Black de 20x) no nivel do SSJ4 (o pedido 184); a Golden, sem dono, no nivel do DM
        [FrostFinal] = NivelDoDeus, ["frost_golden"] = Patamar(2),
        // o Humano: a Tensao Divina (32x) no nivel do Blue (o pedido 184)
        [TensaoDivina] = NivelDoBlue,
        // racas SEM forma (Human/Gray/Kai/Demigod...): a Ascensao nas portas do SSJ, do SSJ2 e do SSJ3 -- e a divina no SSG e no Blue
        ["asc5"] = Patamar(1), ["asc10"] = Patamar(2), ["asc20"] = Patamar(3), [AscensaoDeus] = NivelDoDeus, [AscensaoBlue] = NivelDoBlue,
        // universais: o potencial continua 1,5x; o ki divino e o Blue nos niveis do dono
        ["potential"] = 1.5, ["godki"] = NivelDoDeus, [Blue] = NivelDoBlue,
        // (2026-10-30, o pedido 184) o nivel do Blue Evolution (56x): ele, o Rose 2, o Beast, o Orange, o MAX do bio e a Forma Pura; o Blue
        // Kaio-ken aos 50% de maestria divina; e o marco NOVO do Ultra Instinto e do Ultra Ego
        [BlueEvolution] = NivelDaEvolucaoDivina, [NivelDaEvolucao] = NivelDaEvolucaoDivina, [BlueKaioken] = NivelDaEvolucaoDivina,
        [UltraInstintoEEgo] = NivelDasDisciplinas,
    };

    public static double Valor(string tag) => Tabela.GetValueOrDefault(tag);
}
