using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>
/// SINCRONIZA O .col COM O QUE VOCE EDITOU NA CENA -- e SO com isso.
///
/// ============================ POR QUE ELE NAO RECALCULA O MAPA ============================
/// A primeira versao dizia "a CENA passa a ser a verdade" e reescrevia o `.col` inteiro a partir
/// dela. Medido nos 40 mapas, isso destruia colisao em massa:
///
///     z01_Earth            7914 -> 6354   (-1560)
///     z20_Negative_Earth   1718 ->  963   (-755)    <- mapa que ninguem tinha editado
///     z15..z18_Outside   250000 ->    0   cada
///
/// Sao duas causas independentes, e nenhuma se resolve com ajuste:
///
///   1. A CENA E UMA VISTA COM PERDA. Turf denso SEM ICONE vira colisao sem virar tile
///      (`MapConverter`: `if (td.Icon == null) { if (td.Density) muros.Add(...); return false; }`).
///      Os quatro `z15..z18` sao 250.000 celulas bloqueadas com ZERO tiles na cena.
///
///   2. AS DUAS TABELAS DE DENSIDADE DISCORDAM. O `.col` nasceu do `density` do DM; os poligonos
///      de fisica do tileset sao lado de renderizacao, e divergem em 1.599 celulas so na Terra.
///      (Tentei tambem APRENDER a tabela do proprio `.col` -- "tipo que aparece em celula livre nao
///      bloqueia". Ela reconstroi quase tudo, mas vira de lado com um unico contraexemplo: apagar
///      uma celula fez um tipo inteiro mudar de lado em todos os mapas.)
///
/// A saida nao e uma tabela melhor: e nao precisar de tabela pro mapa inteiro. Este comando compara
/// a cena com a LINHA DE BASE gravada ao lado do `.col` e mexe apenas nas celulas que MUDARAM. Uma
/// celula que voce nao tocou fica exatamente como estava, entao nenhuma das duas divergencias acima
/// tem por onde vazar.
/// ==========================================================================================
///
///     dotnet run --project Tools/AssetPipeline -- colisao Assets/Maps
///
/// Na primeira vez em cada mapa ele so GRAVA a linha de base e nao mexe em nada -- e o unico
/// comportamento honesto quando ainda nao ha com o que comparar.
/// </summary>
public static class SceneCollision
{
    /// <summary>Extensao da linha de base: as celulas da cena no ultimo estado sincronizado.</summary>
    private const string ExtBase = ".cel";

    public static void Regerar(string mapsDir)
    {
        string tileset = Path.Combine(mapsDir, "tileset.tres");
        if (!File.Exists(tileset)) { Console.WriteLine($"ERRO: nao achei {tileset}"); return; }

        HashSet<(int, int, int)> densos = LerDensos(tileset);
        Console.WriteLine($"tiles com fisica no tileset: {densos.Count}");

        int mexidos = 0, novosTotal = 0, tiradosTotal = 0, semBase = 0;
        foreach (string tscn in Directory.GetFiles(mapsDir, "*.tscn").OrderBy(s => s))
        {
            string nome = Path.GetFileNameWithoutExtension(tscn);
            string col = Path.Combine(mapsDir, nome + ".col");
            if (!File.Exists(col)) continue;

            byte[] bits = File.ReadAllBytes(col);
            if (bits.Length < 8 || bits[0] != 'J' || bits[1] != 'C')
            {
                Console.WriteLine($"  {nome}: .col ilegivel -- pulando");
                continue;
            }
            int w = bits[4] | (bits[5] << 8), h = bits[6] | (bits[7] << 8);

            List<Celula> agora = [.. Celulas(tscn)];
            string arqBase = Path.Combine(mapsDir, nome + ExtBase);
            if (!File.Exists(arqBase))
            {
                GravarBase(arqBase, agora);
                semBase++;
                continue;
            }

            // ---- o que mudou, POSICAO a POSICAO ----
            Dictionary<(int, int), List<Celula>> antes = PorCelula(LerBase(arqBase));
            Dictionary<(int, int), List<Celula>> depois = PorCelula(agora);

            var tocadas = new HashSet<(int, int)>();
            foreach (((int, int) p, List<Celula> a) in antes)
                if (!depois.TryGetValue(p, out List<Celula>? d) || !Igual(a, d)) tocadas.Add(p);
            foreach (((int, int) p, List<Celula> d) in depois)
                if (!antes.ContainsKey(p)) tocadas.Add(p);

            int novos = 0, tirados = 0;
            foreach ((int cx, int cy) in tocadas)
            {
                if (cx < 0 || cy < 0 || cx >= w || cy >= h) continue;

                // O ESTADO NOVO DA CELULA: bloqueia se ALGUMA camada tem tile com fisica ali.
                bool bloqueia = depois.TryGetValue((cx, cy), out List<Celula>? tiles)
                                && tiles.Any(t => densos.Contains((t.Fonte, t.AX, t.AY)));
                bool marcado = Bit(bits, w, cx, cy);
                if (bloqueia == marcado) continue;

                Definir(bits, w, cx, cy, bloqueia);
                if (bloqueia) novos++; else tirados++;
            }

            if (novos > 0 || tirados > 0)
            {
                File.WriteAllBytes(col, bits);
                Console.WriteLine($"  {nome}: {tocadas.Count} celula(s) editada(s) -> +{novos} muro(s), -{tirados}");
                mexidos++;
                novosTotal += novos;
                tiradosTotal += tirados;
            }
            else if (tocadas.Count > 0)
            {
                Console.WriteLine($"  {nome}: {tocadas.Count} celula(s) editada(s), nenhuma muda colisao");
            }

            GravarBase(arqBase, agora);
        }

        if (semBase > 0)
            Console.WriteLine($"linha de base gravada em {semBase} mapa(s) -- nada a sincronizar nesta primeira vez");
        Console.WriteLine($"mapas mexidos: {mexidos} | muros +{novosTotal} / -{tiradosTotal}");
    }

    private static bool Igual(List<Celula> a, List<Celula> b) =>
        a.Count == b.Count && a.OrderBy(Chave).SequenceEqual(b.OrderBy(Chave));

    private static long Chave(Celula c) => ((long)c.Fonte << 32) | ((long)c.AX << 16) | (uint)c.AY;

    private static Dictionary<(int, int), List<Celula>> PorCelula(IEnumerable<Celula> cels)
    {
        var fora = new Dictionary<(int, int), List<Celula>>();
        foreach (Celula c in cels)
        {
            if (!fora.TryGetValue((c.X, c.Y), out List<Celula>? l)) fora[(c.X, c.Y)] = l = [];
            l.Add(c);
        }
        return fora;
    }

    // =====================================================================
    // A LINHA DE BASE
    // =====================================================================
    private static void GravarBase(string caminho, List<Celula> cels)
    {
        using var fs = new FileStream(caminho, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        w.Write("JCEL"u8);
        w.Write(cels.Count);
        foreach (Celula c in cels)
        {
            w.Write((short)c.X); w.Write((short)c.Y);
            w.Write((ushort)c.Fonte); w.Write((ushort)c.AX); w.Write((ushort)c.AY);
        }
    }

    private static List<Celula> LerBase(string caminho)
    {
        var fora = new List<Celula>();
        using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read);
        using var r = new BinaryReader(fs);
        if (r.ReadUInt32() != 0x4C45434A) return fora;   // "JCEL" em little-endian
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
            fora.Add(new Celula(r.ReadInt16(), r.ReadInt16(), r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16()));
        return fora;
    }

    // =====================================================================
    // BITS
    // =====================================================================
    private static bool Bit(byte[] b, int w, int cx, int cy)
    {
        int i = cy * w + cx;
        return (b[8 + (i >> 3)] & (1 << (i & 7))) != 0;
    }

    private static void Definir(byte[] b, int w, int cx, int cy, bool ligado)
    {
        int i = cy * w + cx;
        if (ligado) b[8 + (i >> 3)] |= (byte)(1 << (i & 7));
        else b[8 + (i >> 3)] &= (byte)~(1 << (i & 7));
    }

    private readonly record struct Celula(int X, int Y, int Fonte, int AX, int AY);

    /// <summary>
    /// Quais tiles do tileset tem poligono de fisica. A chave e (fonte, x, y) -- a MESMA
    /// tripla que a cena guarda por celula.
    ///
    /// Esta tabela NAO reproduz o `.col` (ver o cabecalho): ela vale so pra decidir o que fazer
    /// com uma celula que voce ACABOU de editar, que e onde ela e a melhor resposta disponivel --
    /// e o que o editor te mostrou quando voce pintou o tile.
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
    /// Le as celulas do cenario -- do `.pedacos` ao lado da cena E do que ainda estiver DENTRO dela.
    ///
    /// ============================ O CHAO SAIU DA CENA ============================
    /// As celulas moravam num `tile_map_data` por camada dentro do `.tscn`. Hoje elas moram num
    /// `.pedacos` (ver `Core.World.PedacosDoMapa`), porque um TileMapLayer monta TODAS as celulas
    /// que tiver no primeiro quadro -- 708 ms na Terra, a travada de tres segundos ao trocar de mapa.
    ///
    /// Ler so a cena, aqui, seria pior que um erro: este comando compara o mapa com uma linha de
    /// base e MEXE no `.col` do que mudou. Uma cena vazia parece "o dono apagou o planeta inteiro",
    /// e o comando abriria as 250 mil celulas em silencio.
    ///
    /// LE OS DOIS de proposito. O `.pedacos` e o cenario convertido; o que sobrar na cena e o que
    /// alguem pintou a mao no editor. Somar os dois e o que mantem o comando util pros dois casos.
    /// =============================================================================
    ///
    /// ============================ DOIS JEITOS DE ESCREVER O MESMO BLOB ============================
    /// Quando ha blob na cena, ele pode vir de duas formas: `PackedByteArray(0, 0, 4, ...)` --
    /// decimais separados por virgula, o FORMATO 3 -- ou `PackedByteArray("AAAAAAAABAA...")`, em
    /// base64, que e pra onde o Godot sobe assim que alguem abre o mapa no editor e salva.
    ///
    /// Ler so o primeiro quebrava este comando (com `FormatException`) no dia em que alguem edita um
    /// mapa no editor -- que e exatamente o dia em que ele importa.
    /// ==============================================================================================
    /// </summary>
    private static IEnumerable<Celula> Celulas(string tscn)
    {
        string pedacos = Path.ChangeExtension(tscn, ".pedacos");
        if (File.Exists(pedacos)
            && Jandirus.Core.World.PedacosDoMapa.Ler(File.ReadAllBytes(pedacos)) is { } mapa)
        {
            for (int c = 0; c < mapa.Camadas.Length; c++)
                for (int cy = mapa.Cy0; cy < mapa.Cy1; cy++)
                    for (int cx = mapa.Cx0; cx < mapa.Cx1; cx++)
                    {
                        if (!mapa.Achar(cx, cy, c, out int inicio, out int quantas)) continue;
                        for (int i = 0; i < quantas; i++)
                        {
                            Jandirus.Core.World.CelulaDePedaco cel = mapa.Celula(inicio, i);
                            yield return new Celula(cel.X, cel.Y, cel.Fonte, cel.Ax, cel.Ay);
                        }
                    }
        }

        foreach (Match m in Regex.Matches(File.ReadAllText(tscn),
                     @"tile_map_data = PackedByteArray\(([^)]*)\)"))
        {
            string cru = m.Groups[1].Value.Trim();
            byte[] b;
            if (cru.StartsWith('"'))
            {
                b = Convert.FromBase64String(cru.Trim('"'));   // formato 4
            }
            else
            {
                string[] partes = cru.Split(',', StringSplitOptions.RemoveEmptyEntries);
                b = new byte[partes.Length];
                for (int i = 0; i < partes.Length; i++) b[i] = byte.Parse(partes[i].Trim());
            }

            for (int i = 2; i + 11 < b.Length; i += 12)
                yield return new Celula(
                    (short)(b[i] | (b[i + 1] << 8)),
                    (short)(b[i + 2] | (b[i + 3] << 8)),
                    b[i + 4] | (b[i + 5] << 8),
                    b[i + 6] | (b[i + 7] << 8),
                    b[i + 8] | (b[i + 9] << 8));
        }
    }
}
