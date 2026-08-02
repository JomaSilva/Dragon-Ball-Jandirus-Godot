using System.Diagnostics;
using System.Text;

namespace Jandirus.Tools;

/// <summary>
/// CONVERTE AS CENAS DE TEXTO (.tscn) PRA BINARIO (.scn).
///
/// O proprio Godot avisa ao abrir: "a cena textual ocupa muito espaco em disco, provavelmente
/// porque incorpora dados binarios". Ele esta certo -- cada andar e um `PackedByteArray` com
/// 250 mil celulas escritas como TEXTO DECIMAL SEPARADO POR VIRGULA: ~3 MB por planeta, 40
/// planetas, e o editor tem que ler e reescrever tudo isso a cada salvamento.
///
/// O `.scn` guarda os mesmos bytes como bytes. Abre e edita no editor igual, so nao da pra
/// ler num diff -- e o que se perde nao importa aqui, porque estes arquivos sao GERADOS: quem
/// se compara e o `.dmm` de origem, nao a saida.
///
/// Quem faz a conversao e o PROPRIO GODOT, rodando sem janela. Escrever o formato binario de
/// cena na mao seria reimplementar um serializador versionado do motor pra economizar uma
/// chamada de processo.
/// </summary>
public static class SceneBinary
{
    public static void Converter(string mapsDir, string? godot)
    {
        godot ??= Achar();
        if (godot == null)
        {
            Console.WriteLine("ERRO: nao achei o executavel do Godot.");
            Console.WriteLine("      passe o caminho:  ... -- binario Assets/Maps \"C:/.../Godot.exe\"");
            return;
        }

        string[] cenas = Directory.GetFiles(mapsDir, "*.tscn");
        if (cenas.Length == 0) { Console.WriteLine("nenhum .tscn pra converter"); return; }

        string? raiz = AcharRaiz(mapsDir);
        if (raiz == null) { Console.WriteLine("ERRO: nao achei o project.godot"); return; }

        // o script roda DENTRO do projeto: e o unico jeito de os `res://` resolverem
        string script = Path.Combine(raiz, ".converter_cenas.gd");
        File.WriteAllText(script, Gd(mapsDir, raiz), new UTF8Encoding(false));

        try
        {
            var psi = new ProcessStartInfo(godot)
            {
                WorkingDirectory = raiz,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string a in new[] { "--headless", "--path", raiz, "--script", "res://.converter_cenas.gd" })
                psi.ArgumentList.Add(a);

            using Process? p = Process.Start(psi);
            if (p == null) { Console.WriteLine("ERRO: nao consegui rodar o Godot"); return; }

            // OS DOIS FLUXOS PRECISAM SER DRENADOS AO MESMO TEMPO.
            //
            // Ler `StandardOutput` ate o fim e SO DEPOIS esperar o processo TRAVA quando a
            // saida de erro enche o buffer do sistema: o Godot fica bloqueado tentando
            // escrever em stderr enquanto nos ficamos bloqueados lendo stdout, e nenhum dos
            // dois sai disso. Foi exatamente o que aconteceu na primeira tentativa -- dez
            // minutos parado sem uma linha. Os eventos assincronos leem os dois em paralelo.
            var saida = new StringBuilder();
            p.OutputDataReceived += (_, e) => { if (e.Data != null) saida.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, _) => { };   // drenado e descartado: e ruido do motor
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            if (!p.WaitForExit(10 * 60 * 1000))
            {
                p.Kill(entireProcessTree: true);
                Console.WriteLine("ERRO: o Godot passou de 10 minutos convertendo -- abortado");
                return;
            }

            foreach (string linha in saida.ToString().Split('\n'))
                if (linha.StartsWith("[scn]", StringComparison.Ordinal)) Console.WriteLine("  " + linha[5..].Trim());
        }
        finally
        {
            if (File.Exists(script)) File.Delete(script);
        }

        // o manifesto aponta pras cenas: se elas viraram .scn, ele tem que acompanhar
        string manifesto = Path.Combine(mapsDir, "manifest.json");
        if (File.Exists(manifesto))
        {
            string txt = File.ReadAllText(manifesto).Replace(".tscn\"", ".scn\"");
            File.WriteAllText(manifesto, txt, new UTF8Encoding(false));
        }

        long antes = 0, depois = 0;
        foreach (string t in cenas)
        {
            string scn = Path.ChangeExtension(t, ".scn");
            if (!File.Exists(scn)) continue;
            antes += new FileInfo(t).Length;
            depois += new FileInfo(scn).Length;
            File.Delete(t);
            string imp = t + ".import";       // sobra do importador, se houver
            if (File.Exists(imp)) File.Delete(imp);
        }

        Console.WriteLine($"cenas convertidas: {cenas.Length} | {antes / 1048576} MB -> {depois / 1048576} MB");
    }

    private static string Gd(string mapsDir, string raiz)
    {
        string rel = Path.GetRelativePath(raiz, mapsDir).Replace('\\', '/');
        return $$"""
            extends SceneTree
            func _init() -> void:
                var dir := DirAccess.open("res://{{rel}}")
                if dir == null:
                    print("[scn] nao abri res://{{rel}}")
                    quit(1); return
                var n := 0
                for arq in dir.get_files():
                    if not arq.ends_with(".tscn"): continue
                    var caminho := "res://{{rel}}/" + arq
                    var ps: PackedScene = load(caminho)
                    if ps == null:
                        print("[scn] FALHOU: " + arq)
                        continue
                    var destino := caminho.replace(".tscn", ".scn")
                    var erro := ResourceSaver.save(ps, destino)
                    if erro != OK:
                        print("[scn] erro %d ao gravar %s" % [erro, destino])
                    else:
                        n += 1
                print("[scn] %d cenas gravadas em binario" % n)
                quit(0)
            """;
    }

    private static string? AcharRaiz(string daqui)
    {
        var d = new DirectoryInfo(Path.GetFullPath(daqui));
        while (d != null)
        {
            if (File.Exists(Path.Combine(d.FullName, "project.godot"))) return d.FullName;
            d = d.Parent;
        }
        return null;
    }

    /// <summary>Procura o Godot nos lugares onde ele costuma estar neste projeto.</summary>
    private static string? Achar()
    {
        string? env = Environment.GetEnvironmentVariable("GODOT");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

        string? raiz = AcharRaiz(Directory.GetCurrentDirectory());
        foreach (string? b in new[] { raiz, raiz == null ? null : Directory.GetParent(raiz)?.FullName })
        {
            if (b == null || !Directory.Exists(b)) continue;
            foreach (string dir in Directory.GetDirectories(b, "Godot_*"))
            {
                string[] exe = Directory.GetFiles(dir, "*console.exe");
                if (exe.Length > 0) return exe[0];
                exe = Directory.GetFiles(dir, "Godot*.exe");
                if (exe.Length > 0) return exe[0];
            }
        }
        return null;
    }
}
