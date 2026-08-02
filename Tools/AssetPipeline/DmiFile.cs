using System.IO.Compression;
using System.Text;

namespace Jandirus.Tools;

/// <summary>Um estado de icone do .dmi (o "icon_state" que o codigo DM referencia por nome).</summary>
public sealed class DmiState
{
    public string Name = "";
    public int Dirs = 1;          // 1, 4 ou 8
    public int Frames = 1;
    public double[] Delays = [];  // em DECIMOS DE SEGUNDO (unidade do BYOND); vazio = estatico
    public bool Movement;         // estado usado so enquanto anda (o BYOND alterna sozinho)
    public bool Loop = true;
}

/// <summary>
/// Leitor de .dmi. Um .dmi E um PNG: a folha de sprites vem nos chunks de imagem e a
/// descricao dos estados vem num chunk de texto ("Description") em zTXt ou tEXt.
/// Nao decodificamos pixel nenhum: so precisamos do IHDR (pra saber quantas colunas a
/// folha tem) e do texto. Os bytes do arquivo sao copiados intactos como .png.
/// </summary>
public static class DmiFile
{
    public sealed record Result(int SheetWidth, int SheetHeight, int IconWidth, int IconHeight, List<DmiState> States);

    public static Result? Read(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 8 || data[0] != 0x89 || data[1] != 'P' || data[2] != 'N' || data[3] != 'G')
            return null;

        int sheetW = 0, sheetH = 0;
        string? desc = null;

        int i = 8;
        while (i + 8 <= data.Length)
        {
            int len = ReadBigEndianInt(data, i);
            if (len < 0 || i + 12 + len > data.Length) break;
            string type = Encoding.ASCII.GetString(data, i + 4, 4);
            int dataStart = i + 8;

            if (type == "IHDR")
            {
                sheetW = ReadBigEndianInt(data, dataStart);
                sheetH = ReadBigEndianInt(data, dataStart + 4);
            }
            else if (type == "zTXt" || type == "tEXt")
            {
                int zero = Array.IndexOf(data, (byte)0, dataStart, len);
                if (zero > 0)
                {
                    string key = Encoding.ASCII.GetString(data, dataStart, zero - dataStart);
                    if (key == "Description")
                    {
                        if (type == "tEXt")
                        {
                            desc = Encoding.Latin1.GetString(data, zero + 1, dataStart + len - (zero + 1));
                        }
                        else
                        {
                            // zTXt: 1 byte de metodo de compressao e o resto e zlib
                            int payload = zero + 2;
                            using var ms = new MemoryStream(data, payload, dataStart + len - payload);
                            using var z = new ZLibStream(ms, CompressionMode.Decompress);
                            using var outMs = new MemoryStream();
                            z.CopyTo(outMs);
                            desc = Encoding.Latin1.GetString(outMs.ToArray());
                        }
                    }
                }
            }
            else if (type == "IDAT" && desc != null)
            {
                break; // ja temos tudo que interessa
            }

            i += 12 + len; // len + tipo(4) + tamanho(4) + crc(4)
        }

        if (sheetW == 0 || sheetH == 0) return null;

        // sem metadados: trata a folha inteira como um unico icone estatico
        if (string.IsNullOrEmpty(desc))
            return new Result(sheetW, sheetH, sheetW, sheetH,
                [new DmiState { Name = "", Dirs = 1, Frames = 1 }]);

        var (iw, ih, states) = ParseDescription(desc!);
        if (iw <= 0 || ih <= 0) (iw, ih) = GuessIconSize(sheetW, sheetH, states);
        return new Result(sheetW, sheetH, iw, ih, states);
    }

    /// <summary>
    /// Muitos .dmi OMITEM width/height: nesse caso o BYOND usa o tamanho de icone do
    /// mundo (32 neste jogo). Em vez de cravar 32, deduzimos: o icone e quadrado, o lado
    /// tem que dividir as duas dimensoes da folha e a grade resultante precisa caber todos
    /// os quadros declarados. Pegamos o MAIOR lado que satisfaz isso, o que aterrissa em 32
    /// nas folhas deste projeto e continua certo se algum dia entrar arte de outro tamanho.
    /// </summary>
    private static (int, int) GuessIconSize(int sheetW, int sheetH, List<DmiState> states)
    {
        int needed = 0;
        foreach (DmiState s in states) needed += Math.Max(1, s.Dirs) * Math.Max(1, s.Frames);
        if (needed <= 0) needed = 1;

        for (int side = Math.Min(sheetW, sheetH); side >= 1; side--)
        {
            if (sheetW % side != 0 || sheetH % side != 0) continue;
            long capacity = (long)(sheetW / side) * (sheetH / side);
            if (capacity >= needed) return (side, side);
        }
        return (sheetW, sheetH); // folha degenerada: trata como icone unico
    }

    private static (int, int, List<DmiState>) ParseDescription(string desc)
    {
        int iconW = 0, iconH = 0;
        var states = new List<DmiState>();
        DmiState? cur = null;

        foreach (string raw in desc.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string key = line[..eq].Trim();
            string val = line[(eq + 1)..].Trim();

            switch (key)
            {
                case "width": int.TryParse(val, out iconW); break;
                case "height": int.TryParse(val, out iconH); break;
                case "state":
                    cur = new DmiState { Name = Unquote(val) };
                    states.Add(cur);
                    break;
                case "dirs": if (cur != null && int.TryParse(val, out int d)) cur.Dirs = d; break;
                case "frames": if (cur != null && int.TryParse(val, out int f)) cur.Frames = f; break;
                case "movement": if (cur != null) cur.Movement = val == "1"; break;
                case "loop":
                    // no BYOND, loop = N significa "repete N vezes"; 0/ausente = infinito
                    if (cur != null && int.TryParse(val, out int lp)) cur.Loop = lp == 0;
                    break;
                case "delay":
                    if (cur != null)
                    {
                        var parts = val.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        var arr = new double[parts.Length];
                        for (int k = 0; k < parts.Length; k++)
                            double.TryParse(parts[k].Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out arr[k]);
                        cur.Delays = arr;
                    }
                    break;
            }
        }

        return (iconW, iconH, states);
    }

    private static string Unquote(string s) =>
        s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;

    private static int ReadBigEndianInt(byte[] b, int off) =>
        (b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3];
}
