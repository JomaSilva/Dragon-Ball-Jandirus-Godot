using System.Globalization;
using System.Text;

namespace Jandirus.Tools;

/// <summary>
/// Escreve um SpriteFrames (.tres) apontando pro atlas .png, uma animacao por
/// (estado x direcao).
///
/// COMO O .dmi GUARDA OS QUADROS: a folha e lida da esquerda pra direita, de cima
/// pra baixo, num indice GLOBAL que corre os estados na ordem em que aparecem nos
/// metadados. Dentro de um estado a ordem e:  para cada FRAME, para cada DIRECAO.
/// (a direcao e a que varia mais rapido). Ordem das direcoes no BYOND:
/// 4 dirs = SUL, NORTE, LESTE, OESTE | 8 dirs = ...+ SE, SO, NE, NO.
///
/// TEMPO: o "delay" do BYOND esta em DECIMOS DE SEGUNDO. No Godot a duracao de um
/// quadro e um MULTIPLICADOR de 1/speed, entao fixando speed = 10 fps o delay do
/// BYOND vira a duracao do Godot sem nenhuma conversao (delay 2 = 0,2 s).
/// </summary>
public static class SpriteFramesWriter
{
    private const double GodotFps = 10.0; // 1 quadro = 1 decimo de segundo, igual ao BYOND

    private static readonly string[] Dir4 = ["south", "north", "east", "west"];
    private static readonly string[] Dir8 = ["south", "north", "east", "west", "southeast", "southwest", "northeast", "northwest"];

    public sealed record Stats(int Animations, int Frames, bool Overflow);

    public static Stats Write(string tresPath, string pngResPath, DmiFile.Result dmi)
    {
        int cols = Math.Max(1, dmi.SheetWidth / Math.Max(1, dmi.IconWidth));
        int rows = Math.Max(1, dmi.SheetHeight / Math.Max(1, dmi.IconHeight));
        int capacity = cols * rows;

        var subs = new StringBuilder();
        var anims = new List<string>();
        int subId = 0, globalIndex = 0, totalFrames = 0;
        bool overflow = false;
        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (DmiState st in dmi.States)
        {
            int dirs = st.Dirs is 1 or 4 or 8 ? st.Dirs : 1;
            string[] dirNames = dirs == 8 ? Dir8 : Dir4;

            // indices deste estado, por direcao (a direcao varia mais rapido dentro do frame)
            var perDir = new List<int>[dirs];
            for (int d = 0; d < dirs; d++) perDir[d] = new List<int>(st.Frames);

            for (int f = 0; f < st.Frames; f++)
                for (int d = 0; d < dirs; d++)
                {
                    if (globalIndex >= capacity) overflow = true;
                    perDir[d].Add(globalIndex++);
                }

            for (int d = 0; d < dirs; d++)
            {
                string name = AnimName(st.Name, dirs == 1 ? null : dirNames[d], usedNames, st.Movement);
                var frameEntries = new List<string>(st.Frames);

                for (int f = 0; f < perDir[d].Count; f++)
                {
                    int idx = perDir[d][f];
                    if (idx >= capacity) continue; // folha menor que os metadados: nao inventa quadro
                    int x = (idx % cols) * dmi.IconWidth;
                    int y = (idx / cols) * dmi.IconHeight;

                    string sub = $"AtlasTexture_{subId++}";
                    subs.Append($"[sub_resource type=\"AtlasTexture\" id=\"{sub}\"]\n")
                        .Append($"atlas = ExtResource(\"1_atlas\")\n")
                        .Append($"region = Rect2({x}, {y}, {dmi.IconWidth}, {dmi.IconHeight})\n\n");

                    double dur = f < st.Delays.Length && st.Delays[f] > 0 ? st.Delays[f] : 1.0;
                    frameEntries.Add($"{{\n\"duration\": {Num(dur)},\n\"texture\": SubResource(\"{sub}\")\n}}");
                    totalFrames++;
                }

                if (frameEntries.Count == 0) continue;

                anims.Add($"{{\n\"frames\": [{string.Join(", ", frameEntries)}],\n" +
                          $"\"loop\": {(st.Loop ? "true" : "false")},\n" +
                          $"\"name\": &\"{name}\",\n" +
                          $"\"speed\": {Num(GodotFps)}\n}}");
            }
        }

        // load_steps = 1 (o proprio recurso) + externos + sub-recursos
        int loadSteps = 1 + 1 + subId;
        var sb = new StringBuilder();
        sb.Append($"[gd_resource type=\"SpriteFrames\" load_steps={loadSteps} format=3]\n\n");
        sb.Append($"[ext_resource type=\"Texture2D\" path=\"{pngResPath}\" id=\"1_atlas\"]\n\n");
        sb.Append(subs);
        sb.Append("[resource]\n");
        sb.Append($"animations = [{string.Join(", ", anims)}]\n");

        Directory.CreateDirectory(Path.GetDirectoryName(tresPath)!);
        File.WriteAllText(tresPath, sb.ToString(), new UTF8Encoding(false));

        return new Stats(anims.Count, totalFrames, overflow);
    }

    /// <summary>
    /// Nome de animacao estavel: o codigo do jogo vai pedir por ele.
    ///
    /// O `movement` FAZ PARTE DA IDENTIDADE do estado. Um .dmi tem DOIS estados de nome
    /// vazio: um com `movement = 1` (o ciclo de CAMINHADA, que o BYOND troca sozinho quando
    /// o mob anda) e um sem (a pose PARADA, que pode ter animacao propria -- o corpo tem uma
    /// de 4 quadros com o ultimo segurando 30 decimos, uma respiracao).
    ///
    /// Ignorar essa marca e desempatar por ORDEM foi um defeito serio: a ordem MUDA por
    /// arquivo. No corpo a caminhada vem primeiro e virava `default_south`; no
    /// `ClothesSaiyanSuit` e no `GokuDBSSuit` vem a parada primeiro, entao a caminhada deles
    /// caia em `default_south_2` -- e o jogo desenhava a roupa PARADA sobre um corpo andando.
    /// </summary>
    private static string AnimName(string state, string? dir, HashSet<string> used, bool movement)
    {
        string baseName = string.IsNullOrEmpty(state) ? "default" : Sanitize(state);
        if (movement) baseName = baseName == "default" ? "walk" : baseName + "_mov";
        string name = dir == null ? baseName : $"{baseName}_{dir}";
        if (used.Add(name)) return name;
        for (int n = 2; ; n++)
        {
            string alt = $"{name}_{n}";
            if (used.Add(alt)) return alt;
        }
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        string r = sb.ToString().Trim('_');
        while (r.Contains("__")) r = r.Replace("__", "_");
        return r.Length == 0 ? "state" : r;
    }

    private static string Num(double v) => v.ToString("0.0###", CultureInfo.InvariantCulture);
}
