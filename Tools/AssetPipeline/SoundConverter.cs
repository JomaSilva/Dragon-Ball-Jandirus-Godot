using System.Diagnostics;

namespace Jandirus.Tools;

/// <summary>
/// Traz o audio do BYOND pro Godot.
///
/// DUAS COISAS QUE O GODOT NAO LE e que o jogo original usa:
///
///  * MIDI (.mid) e .Sfarr -- 35 arquivos. Nao ha conversao automatica que preste (MIDI e
///    partitura, nao audio: renderizar exige um banco de sons). Ficam de fora, listados.
///  * WAV em MS ADPCM (codigo de formato 2 no cabecalho RIFF) -- 68 arquivos, quase todos os
///    efeitos de soco e de ki. O Godot so aceita PCM. Estes SAO convertidos, pra .ogg.
///
/// Sem esta passada o jogo importa em silencio e o som simplesmente nao toca -- o erro so
/// aparece em tempo de execucao, como "Cannot open file ....sample".
/// </summary>
public static class SoundConverter
{
    private static readonly string[] Aceitos = [".ogg", ".mp3", ".wav"];

    public sealed record Resultado(int Copiados, int Convertidos, int Ignorados, List<string> Fora);

    public static Resultado Converter(string origem, string destino, string? ffmpeg)
    {
        int copiados = 0, convertidos = 0, ignorados = 0;
        var fora = new List<string>();

        foreach (string arq in Directory.GetFiles(origem, "*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(arq).ToLowerInvariant();
            string rel = Path.GetRelativePath(origem, arq);

            if (Array.IndexOf(Aceitos, ext) < 0)
            {
                ignorados++;
                fora.Add($"{rel} (formato que o Godot nao le)");
                continue;
            }

            string alvo = Path.Combine(destino, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(alvo)!);

            // WAV comprimido (ADPCM e afins) nao entra no Godot: vira .ogg
            if (ext == ".wav" && !EhPcm(arq))
            {
                if (ffmpeg == null)
                {
                    ignorados++;
                    fora.Add($"{rel} (WAV comprimido e sem ffmpeg pra converter)");
                    continue;
                }
                string ogg = Path.ChangeExtension(alvo, ".ogg");
                if (RodarFfmpeg(ffmpeg, arq, ogg)) convertidos++;
                else { ignorados++; fora.Add($"{rel} (ffmpeg falhou)"); }
                continue;
            }

            File.Copy(arq, alvo, overwrite: true);
            copiados++;
        }

        return new Resultado(copiados, convertidos, ignorados, fora);
    }

    /// <summary>
    /// O cabecalho RIFF diz o formato no campo `wFormatTag`: 1 = PCM (o Godot le), 3 = float,
    /// 2 = MS ADPCM e 17 = IMA ADPCM (nao le). Ler os 22 primeiros bytes basta.
    /// </summary>
    private static bool EhPcm(string caminho)
    {
        try
        {
            using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read);
            Span<byte> cab = stackalloc byte[22];
            if (fs.Read(cab) < 22) return false;
            if (cab[0] != 'R' || cab[1] != 'I' || cab[2] != 'F' || cab[3] != 'F') return false;
            int formato = cab[20] | (cab[21] << 8);
            return formato is 1 or 3;
        }
        catch { return false; }
    }

    private static bool RodarFfmpeg(string ffmpeg, string entrada, string saida)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(ffmpeg)
            {
                ArgumentList = { "-y", "-loglevel", "error", "-i", entrada, "-c:a", "libvorbis", "-q:a", "5", saida },
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (p == null) return false;
            p.WaitForExit();
            return p.ExitCode == 0 && File.Exists(saida);
        }
        catch { return false; }
    }

    /// <summary>Acha o ffmpeg no PATH. Nulo = nao ha, e os WAV comprimidos ficam de fora.</summary>
    public static string? AcharFfmpeg()
    {
        string? caminho = Environment.GetEnvironmentVariable("PATH");
        foreach (string dir in (caminho ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            foreach (string nome in new[] { "ffmpeg.exe", "ffmpeg" })
            {
                try
                {
                    string f = Path.Combine(dir, nome);
                    if (File.Exists(f)) return f;
                }
                catch { /* entrada invalida no PATH */ }
            }
        }
        return null;
    }
}
