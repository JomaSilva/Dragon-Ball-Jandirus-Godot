using System.Globalization;
using System.Text;

namespace Jandirus.Tools;

/// <summary>Uma luz de cenario: onde fica, quanto alcanca e de que cor.</summary>
public sealed record LuzDeTile(int X, int Y, int Raio, string Cor, float Forca, bool Tremula);

/// <summary>
/// QUEM ILUMINA O CENARIO.
///
/// ISTO E ACRESCENTO, NAO PORTE. O BYOND nao tem um unico `luminosity` no codigo do jogo
/// inteiro (procurei): a "iluminacao" de la era um overlay preto por cima do chao, sem fonte
/// de luz nenhuma -- o motor nao dava mais que isso. Entao nao ha dado original pra copiar, e
/// a lista abaixo e uma decisao: fogueira, tocha, lampada e lava emitem luz; o resto nao.
///
/// A regra e por TYPEPATH e nao por sprite, porque o mesmo `.dmi` serve pra coisas diferentes
/// -- `Turf 57.dmi` tem a fogueira no estado 82 e a tocha no 83, e tambem dezenas de pedras.
/// </summary>
public static class LightCatalog
{
    /// <summary>Uma regra: prefixo de typepath -> como a luz dele se parece.</summary>
    private sealed record Regra(string Prefixo, int Raio, string Cor, float Forca, bool Tremula);

    /// <summary>
    /// As fontes de luz do jogo. A ORDEM IMPORTA: a primeira regra que casa vence, entao o
    /// que e mais especifico vem primeiro.
    ///
    /// Cor e alcance seguem o que a coisa e: fogo e laranja-quente e TREMULA; lampada e um
    /// branco morno e firme; lava e um vermelho baixo e largo, que lambe o chao inteiro.
    /// </summary>
    private static readonly Regra[] Regras =
    [
        new("/turf/decor/Fire",          112, "ff9a3c", 1.25f, true),
        new("/obj/buildables/Fire",      112, "ff9a3c", 1.25f, true),
        new("/turf/decor/Torch",          88, "ffb056", 1.10f, true),
        new("/turf/CastleWall/Torch",     88, "ffb056", 1.10f, true),
        new("/obj/buildables/Torch",      88, "ffb056", 1.10f, true),
        new("/obj/buildables/lamp",       96, "ffe8c0", 0.95f, false),
        new("/turf/decor/Light",         104, "cfe4ff", 0.90f, false),
        new("/turf/SpaceStation/light",  104, "cfe4ff", 0.90f, false),
        new("/turf/Other/Lava",          128, "ff5a1e", 0.85f, true),
        new("/turf/build/Lava",          128, "ff5a1e", 0.85f, true),
    ];

    /// <summary>Esta celula emite luz? Nulo = nao.</summary>
    public static (int Raio, string Cor, float Forca, bool Tremula)? Da(string typepath)
    {
        foreach (Regra r in Regras)
            if (typepath.StartsWith(r.Prefixo, StringComparison.OrdinalIgnoreCase))
                return (r.Raio, r.Cor, r.Forca, r.Tremula);
        return null;
    }

    /// <summary>
    /// Grava as luzes de um andar. JSON e nao binario de proposito: sao poucas por zona e o
    /// dono pode querer acrescentar uma tocha na mao depois de editar o mapa.
    /// </summary>
    public static void Escrever(string caminho, List<LuzDeTile> luzes)
    {
        var sb = new StringBuilder();
        sb.Append("[\n");
        for (int i = 0; i < luzes.Count; i++)
        {
            LuzDeTile l = luzes[i];
            sb.Append($"  {{ \"x\": {l.X}, \"y\": {l.Y}, \"raio\": {l.Raio}, \"cor\": \"{l.Cor}\", " +
                      $"\"forca\": {l.Forca.ToString("0.##", CultureInfo.InvariantCulture)}, " +
                      $"\"tremula\": {(l.Tremula ? "true" : "false")} }}");
            sb.Append(i + 1 < luzes.Count ? ",\n" : "\n");
        }
        sb.Append("]\n");
        File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(false));
    }
}
