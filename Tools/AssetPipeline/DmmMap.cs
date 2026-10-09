using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>Um andar (o antigo "z") de um .dmm.</summary>
public sealed class DmmLevel
{
    public int Z;
    public int Width;
    public int Height;
    public string[,] Cells = new string[0, 0]; // [x, y] -> chave do cabecalho; y=0 e a PRIMEIRA linha do arquivo
}

/// <summary>
/// Leitor de .dmm.
///
/// FORMATO: um cabecalho de `"chave" = (/turf/X,/obj/Y,/area/Z)` e depois blocos
/// `(x,y,z) = {"` com uma linha de chaves por linha do mapa. A chave tem largura fixa
/// dentro do arquivo (1, 2 ou 3 caracteres) -- deduzimos pelo cabecalho.
///
/// EIXO Y: o BYOND conta de baixo pra cima; o Godot, de cima pra baixo. Guardamos as
/// celulas na ORDEM DO ARQUIVO (linha 0 = y 0), que preserva a imagem do mapa como ela
/// aparece no texto. Converter uma coordenada BYOND (bx,by) pra ca e (bx-1, Height-by).
/// </summary>
public static class DmmMap
{
    private static readonly Regex RxKey = new(@"^""([^""]+)""\s*=\s*\((.*)\)\s*$", RegexOptions.Compiled);
    private static readonly Regex RxBlock = new(@"^\((\d+),(\d+),(\d+)\)\s*=\s*\{""\s*$", RegexOptions.Compiled);

    public sealed record Result(Dictionary<string, string[]> Keys, List<DmmLevel> Levels, int KeyWidth);

    public static Result Read(string path)
    {
        var keys = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var levels = new List<DmmLevel>();
        int keyWidth = 0;

        string[] linhas = File.ReadAllLines(path, System.Text.Encoding.Latin1);
        int i = 0;

        // ---- cabecalho ----
        for (; i < linhas.Length; i++)
        {
            string l = linhas[i];
            if (l.Length == 0) continue;
            if (l[0] == '(') break;

            Match m = RxKey.Match(l.Trim());
            if (!m.Success) continue;

            string chave = m.Groups[1].Value;
            keyWidth = Math.Max(keyWidth, chave.Length);
            keys[chave] = SplitTypes(m.Groups[2].Value);
        }

        // ---- blocos ----
        for (; i < linhas.Length; i++)
        {
            Match b = RxBlock.Match(linhas[i].Trim());
            if (!b.Success) continue;

            int x0 = int.Parse(b.Groups[1].Value);
            int y0 = int.Parse(b.Groups[2].Value);
            int z = int.Parse(b.Groups[3].Value);

            var corpo = new List<string>();
            for (i++; i < linhas.Length; i++)
            {
                string l = linhas[i];
                if (l.StartsWith("\"}", StringComparison.Ordinal)) break;
                if (l.Length == 0) continue;
                corpo.Add(l);
            }
            if (corpo.Count == 0) continue;

            int larg = corpo[0].Length / keyWidth;
            int alt = corpo.Count;

            DmmLevel? nivel = levels.Find(n => n.Z == z);
            if (nivel == null)
            {
                nivel = new DmmLevel { Z = z, Width = Math.Max(larg, x0 - 1 + larg), Height = Math.Max(alt, y0 - 1 + alt) };
                nivel.Cells = new string[nivel.Width, nivel.Height];
                levels.Add(nivel);
            }

            for (int ly = 0; ly < alt; ly++)
            {
                string linha = corpo[ly];
                for (int lx = 0; lx < larg; lx++)
                {
                    int off = lx * keyWidth;
                    if (off + keyWidth > linha.Length) break;
                    int gx = x0 - 1 + lx;
                    int gy = y0 - 1 + ly;
                    if (gx < 0 || gy < 0 || gx >= nivel.Width || gy >= nivel.Height) continue;
                    nivel.Cells[gx, gy] = linha.Substring(off, keyWidth);
                }
            }
        }

        levels.Sort((a, b2) => a.Z.CompareTo(b2.Z));
        return new Result(keys, levels, Math.Max(1, keyWidth));
    }

    /// <summary>
    /// Separa os typepaths de uma chave respeitando os blocos `{...}` de propriedades
    /// (um obj pode vir como `/obj/X{pixel_x = 5}` e a virgula de dentro NAO separa).
    /// </summary>
    private static string[] SplitTypes(string s)
    {
        var saida = new List<string>();
        int nivel = 0, ini = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '{') nivel++;
            else if (c == '}') nivel--;
            else if (c == ',' && nivel == 0)
            {
                saida.Add(s[ini..i].Trim());
                ini = i + 1;
            }
        }
        if (ini < s.Length) saida.Add(s[ini..].Trim());
        return [.. saida];
    }

    /// <summary>Typepath sem o bloco de propriedades.</summary>
    public static string BasePath(string tp)
    {
        int i = tp.IndexOf('{');
        return (i < 0 ? tp : tp[..i]).Trim();
    }

    private static readonly Dictionary<string, string> SemVariaveis = new(StringComparer.Ordinal);

    /// <summary>
    /// O QUE A INSTANCIA TROCA NO TIPO: o bloco `{gotox = 208; icon = 'Icons/Turfs/Turf 57.dmi'}` de um
    /// typepath do mapa, como nome -> valor CRU (as aspas vao junto). Vazio quando nao ha bloco.
    ///
    /// O `;` DE DENTRO DE UM TEXTO NAO SEPARA: ha placa no mapa com a frase inteira em `Message = "..."`.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Variaveis(string tp)
    {
        int abre = tp.IndexOf('{');
        int fecha = tp.LastIndexOf('}');
        if (abre < 0 || fecha <= abre) return SemVariaveis;

        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        char aspa = '\0';
        int ini = abre + 1;
        for (int i = abre + 1; i <= fecha; i++)
        {
            char c = tp[i];
            if (aspa != '\0')
            {
                if (c == '\\') i++;                 // o caractere escapado nao fecha o texto
                else if (c == aspa) aspa = '\0';
                continue;
            }
            if (c is '"' or '\'') { aspa = c; continue; }
            if (c != ';' && i != fecha) continue;

            string parte = tp[ini..i];
            ini = i + 1;
            int igual = parte.IndexOf('=');
            if (igual > 0) vars[parte[..igual].Trim()] = parte[(igual + 1)..].Trim();
        }
        return vars;
    }
}
