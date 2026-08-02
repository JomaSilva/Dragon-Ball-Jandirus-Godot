using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>
/// REGERA A COLISAO A PARTIR DA CENA, e nao do .dmm.
///
/// POR QUE ISTO EXISTE. O `.col` que o servidor le nasce da conversao do `.dmm`; a cena `.tscn`
/// tambem. Enquanto ninguem edita, os dois concordam. No instante em que o dono abre a Terra no
/// editor do Godot e apaga uma parede, os dois DIVERGEM: o desenho muda e a parede continua la
/// pro servidor -- o jogador ve chao e leva correcao de movimento em cima de uma parede
/// invisivel. E o mesmo defeito que acabamos de matar, so que reintroduzido pela porta da
/// frente toda vez que alguem editar o mapa.
///
/// Com este comando, a CENA passa a ser a verdade: o que esta desenhado la define o que
/// bloqueia. Rode depois de mexer no mapa:
///
///     dotnet run --project Tools/AssetPipeline -- colisao Assets/Maps
///
/// O criterio de "bloqueia" e o mesmo do editor: o tile tem poligono de fisica no tileset.
/// </summary>
public static class SceneCollision
{
    private const int Cell = 32;

    public static void Regerar(string mapsDir)
    {
        string tileset = Path.Combine(mapsDir, "tileset.tres");
        if (!File.Exists(tileset))
        {
            Console.WriteLine($"ERRO: nao achei {tileset}");
            return;
        }

        HashSet<(int Fonte, int X, int Y)> densos = LerDensos(tileset);
        Console.WriteLine($"tiles com fisica no tileset: {densos.Count}");

        int cenas = 0;
        foreach (string tscn in Directory.GetFiles(mapsDir, "*.tscn").OrderBy(s => s))
        {
            string nome = Path.GetFileNameWithoutExtension(tscn);
            string col = Path.Combine(mapsDir, nome + ".col");
            if (!File.Exists(col))
            {
                Console.WriteLine($"  {nome}: sem .col ao lado -- pulando");
                continue;
            }

            // largura e altura vem do .col antigo: e a unica fonte do TAMANHO do andar (a cena
            // so tem as celulas ocupadas, e um andar pode ter borda vazia)
            byte[] antigo = File.ReadAllBytes(col);
            if (antigo.Length < 8 || antigo[0] != 'J' || antigo[1] != 'C')
            {
                Console.WriteLine($"  {nome}: .col ilegivel -- pulando");
                continue;
            }
            int w = antigo[4] | (antigo[5] << 8);
            int h = antigo[6] | (antigo[7] << 8);

            var bits = new byte[(w * h + 7) / 8];
            int n = 0;
            foreach ((int fonte, int cx, int cy, int ax, int ay) in Celulas(tscn))
            {
                if (cx < 0 || cy < 0 || cx >= w || cy >= h) continue;
                if (!densos.Contains((fonte, ax, ay))) continue;
                int i = cy * w + cx;
                if ((bits[i >> 3] & (1 << (i & 7))) != 0) continue;
                bits[i >> 3] |= (byte)(1 << (i & 7));
                n++;
            }

            int antes = Contar(antigo);
            using (var fs = new FileStream(col, FileMode.Create, FileAccess.Write))
            {
                fs.Write("JCOL"u8);
                fs.WriteByte((byte)(w & 0xFF)); fs.WriteByte((byte)(w >> 8));
                fs.WriteByte((byte)(h & 0xFF)); fs.WriteByte((byte)(h >> 8));
                fs.Write(bits);
            }

            string delta = n == antes ? "igual" : (n > antes ? $"+{n - antes}" : (n - antes).ToString());
            Console.WriteLine($"  {nome}: {n} celulas bloqueadas ({delta})");
            cenas++;
        }

        Console.WriteLine($"cenas regeradas: {cenas}");
    }

    private static int Contar(byte[] col)
    {
        int n = 0;
        for (int i = 8; i < col.Length; i++)
        {
            int b = col[i];
            while (b != 0) { n += b & 1; b >>= 1; }
        }
        return n;
    }

    /// <summary>
    /// Quais tiles do tileset tem poligono de fisica. A chave e (fonte, x, y) -- a MESMA
    /// tripla que a cena guarda por celula.
    /// </summary>
    private static HashSet<(int, int, int)> LerDensos(string tileset)
    {
        var comFisica = new HashSet<(int, int, int)>();
        var porSub = new Dictionary<string, HashSet<(int, int)>>(StringComparer.Ordinal);

        string? sub = null;
        foreach (string linha in File.ReadAllLines(tileset))
        {
            Match m = Regex.Match(linha, @"^\[sub_resource type=""TileSetAtlasSource"" id=""([^""]+)""\]");
            if (m.Success) { sub = m.Groups[1].Value; porSub[sub] = []; continue; }
            if (sub == null) continue;

            m = Regex.Match(linha, @"^(\d+):(\d+)/0/physics_layer_0/");
            if (m.Success) porSub[sub].Add((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)));
        }

        // sources/<id> = SubResource("<sub>") -- e aqui que o id da FONTE (o que a cena grava)
        // se liga ao sub_resource
        foreach (Match m in Regex.Matches(File.ReadAllText(tileset),
                     @"sources/(\d+) = SubResource\(""([^""]+)""\)"))
        {
            int fonte = int.Parse(m.Groups[1].Value);
            if (!porSub.TryGetValue(m.Groups[2].Value, out HashSet<(int, int)>? cs)) continue;
            foreach ((int x, int y) in cs) comFisica.Add((fonte, x, y));
        }
        return comFisica;
    }

    /// <summary>
    /// Le as celulas de todos os TileMapLayer da cena.
    /// Formato do blob: [uint16 formato][por celula: int16 x, int16 y, uint16 fonte,
    /// uint16 atlasX, uint16 atlasY, uint16 alternativa].
    /// </summary>
    private static IEnumerable<(int Fonte, int X, int Y, int AX, int AY)> Celulas(string tscn)
    {
        foreach (Match m in Regex.Matches(File.ReadAllText(tscn),
                     @"tile_map_data = PackedByteArray\(([^)]*)\)"))
        {
            string[] partes = m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var b = new byte[partes.Length];
            for (int i = 0; i < partes.Length; i++) b[i] = byte.Parse(partes[i].Trim());

            for (int i = 2; i + 11 < b.Length; i += 12)
            {
                short x = (short)(b[i] | (b[i + 1] << 8));
                short y = (short)(b[i + 2] | (b[i + 3] << 8));
                int fonte = b[i + 4] | (b[i + 5] << 8);
                int ax = b[i + 6] | (b[i + 7] << 8);
                int ay = b[i + 8] | (b[i + 9] << 8);
                yield return (fonte, x, y, ax, ay);
            }
        }
    }
}
