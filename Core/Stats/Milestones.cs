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
/// </summary>
public static class Milestones
{
    public static readonly Dictionary<string, double> Tabela = new(StringComparer.Ordinal)
    {
        // Saiyajins e racas que viram Super Saiyajin
        ["ssj"] = 2, ["ussj"] = 2.5, ["ssj2"] = 3, ["ssj3"] = 4, ["ssj4"] = 5, ["ssj4fp"] = 6,
        // escada Legendary
        ["lssj1"] = 2, ["lssj2"] = 4, ["lssj3"] = 6, ["lssj4"] = 6,
        // Bio-Androide
        ["bio1"] = 1.5, ["bio2"] = 2, ["bio3"] = 3, ["bio_ssj2"] = 4,
        // Majin (saga)
        ["majin2"] = 2, ["majin3"] = 3,
        // Frost Demon
        ["frost_final"] = 2, ["frost_golden"] = 3,
        // racas SEM forma (Human/Gray/Kai/Demigod...): a Ascensao e o marco
        ["asc5"] = 2, ["asc10"] = 3, ["asc20"] = 4,
        // universais
        ["potential"] = 1.5, ["godki"] = 5, ["blue"] = 6,
    };

    public static double Valor(string tag) => Tabela.GetValueOrDefault(tag);
}
