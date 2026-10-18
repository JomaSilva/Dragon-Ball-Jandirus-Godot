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
///     rampa do `legendary` (25x a 50x). Vale os mesmos 6 do `lssj3`, entao ninguem perde nada.
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
                        FrostFinal = "frost_final", Blue = "blue";

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
    /// A escada fica crescente e o topo dos graus chega no que o USSJ valia:
    ///     Super Saiyajin x2  ->  Grau 2 x2,25  ->  Grau 3 x2,5  ->  Super Saiyajin 2 x3.
    /// O marco de cada grau vem na primeira vez que o corpo ENTRA nele (o instante do original), e nao
    /// quando a maestria do SSJ1 o destrava.
    /// ================================================================================================
    /// </summary>
    public const double ValorDoGrau2 = 2.25, ValorDoGrau3 = 2.5;

    public static readonly Dictionary<string, double> Tabela = new(StringComparer.Ordinal)
    {
        // Saiyajins e racas que viram Super Saiyajin (o `ussj` do DM virou os dois graus -- ver acima)
        [Ssj] = 2, [SsjGrau2] = ValorDoGrau2, [SsjGrau3] = ValorDoGrau3,
        [Ssj2] = 3, [Ssj3] = 4, [Ssj4] = 5, [Ssj4FullPower] = 6,
        // escada Legendary
        [Lssj1] = 2, [Lssj2] = 4, [Lssj3] = 6, ["lssj4"] = 6,
        // Bio-Androide
        ["bio1"] = 1.5, ["bio2"] = 2, ["bio3"] = 3, ["bio_ssj2"] = 4,
        // Majin (saga)
        ["majin2"] = 2, ["majin3"] = 3,
        // Frost Demon
        [FrostFinal] = 2, ["frost_golden"] = 3,
        // racas SEM forma (Human/Gray/Kai/Demigod...): a Ascensao e o marco
        ["asc5"] = 2, ["asc10"] = 3, ["asc20"] = 4,
        // universais
        ["potential"] = 1.5, ["godki"] = 5, [Blue] = 6,
    };

    public static double Valor(string tag) => Tabela.GetValueOrDefault(tag);
}
