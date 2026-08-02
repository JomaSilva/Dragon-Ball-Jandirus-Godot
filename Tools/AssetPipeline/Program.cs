using Jandirus.Tools;

// ============================================================================
// PIPELINE DE ASSETS: BYOND -> Godot
//
//   dotnet run --project Tools/AssetPipeline -- dmi <pastaBYOND> <pastaDestino> [--limit N]
//
// Varre os .dmi, copia os bytes como .png (um .dmi JA e um PNG valido) e gera o
// SpriteFrames .tres ao lado, com uma animacao por estado x direcao.
// Idempotente: rodar de novo so reescreve o que mudou de tamanho/conteudo.
// ============================================================================

if (args.Length >= 2 && args[0] == "turfs")
{
    // turfs <pastaCode> : lista o que o scanner entendeu (conferencia manual)
    Dictionary<string, TurfDef> t = DmTurfScanner.Scan(Path.GetFullPath(args[1]));
    int comIcone = t.Values.Count(v => v.Icon != null);
    int densos = t.Values.Count(v => v.Density);
    Console.WriteLine($"turfs encontrados: {t.Count} | com icone: {comIcone} | densos: {densos}");
    foreach (TurfDef d in t.Values.OrderBy(v => v.Path).Take(args.Length > 2 && args[2] == "--all" ? int.MaxValue : 15))
        Console.WriteLine($"  {d.Path,-46} icon={d.Icon ?? "-",-24} state=\"{d.IconState ?? ""}\" dens={(d.Density ? 1 : 0)} opac={(d.Opacity ? 1 : 0)}");
    return 0;
}

if (args.Length >= 2 && args[0] == "chars")
{
    // chars <races.json> [amostras] : roda o motor de genetica de verdade e mostra o que nasce
    var cat = Jandirus.Core.Races.RaceCatalog.Parse(File.ReadAllText(args[1]));
    int n = args.Length > 2 && int.TryParse(args[2], out int q) ? q : 2000;
    var rng = new Random(12345); // semente fixa: a saida e comparavel entre execucoes

    Console.WriteLine($"catalogo: {cat.Count} protos | {n} amostras por raca\n");
    Console.WriteLine($"{"RACA",-14} {"CLASSE",-22} {"%",6} {"BP medio",12} {"physoff",8}");
    Console.WriteLine(new string('-', 68));

    foreach (string raca in new[] { "Saiyan", "Human", "Namekian", "Icer", "Kai", "Majin", "Heran" })
    {
        Jandirus.Core.Races.RaceProto? proto = cat.Get(raca);
        if (proto == null) { Console.WriteLine($"{raca,-14} (ausente do catalogo)"); continue; }

        var contagem = new Dictionary<string, int>(StringComparer.Ordinal);
        var somaBp = new Dictionary<string, double>(StringComparer.Ordinal);
        var physoff = new Dictionary<string, double>(StringComparer.Ordinal);

        for (int i = 0; i < n; i++)
        {
            Jandirus.Core.Races.Genome g = cat.NewPureCharacter(raca, rng);
            Jandirus.Core.Races.StatBlock sb = g.Build(cat.Protos);
            double bp = g.StartingBp(sb, rng);
            contagem[g.Class] = contagem.GetValueOrDefault(g.Class) + 1;
            somaBp[g.Class] = somaBp.GetValueOrDefault(g.Class) + bp;
            physoff[g.Class] = sb.Get("Physical Offense");
        }

        bool primeira = true;
        foreach ((string cls, int qtd) in contagem.OrderByDescending(kv => kv.Value))
        {
            Console.WriteLine($"{(primeira ? raca : ""),-14} {cls,-22} {100.0 * qtd / n,5:0.0}% " +
                              $"{somaBp[cls] / qtd,12:0.00} {physoff[cls],8:0.###}");
            primeira = false;
        }
        Console.WriteLine();
    }
    return 0;
}

if (args.Length >= 1 && args[0] == "bp")
{
    // bp [races.json] : banco de prova da cadeia de stats e do powerlevel
    Jandirus.Core.Races.RaceCatalog? cat = args.Length > 1 && File.Exists(args[1])
        ? Jandirus.Core.Races.RaceCatalog.Parse(File.ReadAllText(args[1]))
        : null;
    StatBench.Run(cat);
    return 0;
}

if (args.Length >= 3 && args[0] == "sons")
{
    // sons <pastaSounds> <pastaDestino> : copia o audio e converte o que o Godot nao le
    string? ff = SoundConverter.AcharFfmpeg();
    Console.WriteLine(ff != null ? $"ffmpeg: {ff}" : "ffmpeg NAO encontrado: WAV comprimido vai ficar de fora");

    SoundConverter.Resultado r = SoundConverter.Converter(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), ff);
    Console.WriteLine($"copiados    : {r.Copiados}");
    Console.WriteLine($"convertidos : {r.Convertidos} (WAV comprimido -> ogg)");
    Console.WriteLine($"de fora     : {r.Ignorados}");
    foreach (string f in r.Fora.Take(8)) Console.WriteLine($"   - {f}");
    if (r.Fora.Count > 8) Console.WriteLine($"   ... e mais {r.Fora.Count - 8}");
    return 0;
}

if (args.Length >= 4 && args[0] == "visual")
{
    // visual <HairChoose.dm> <pastaSprites> <saida.json> : catalogo de aparencia
    (int cab, int rou, List<string> faltando) =
        DmAppearanceScanner.Escrever(args[1], Path.GetFullPath(args[2]), args[3]);

    Console.WriteLine($"cabelos : {cab}");
    Console.WriteLine($"roupas  : {rou}");
    Console.WriteLine($"saida   : {args[3]}");
    if (faltando.Count > 0)
    {
        Console.WriteLine($"\nATENCAO: {faltando.Count} sprite(s) citados no DM que NAO existem no port:");
        foreach (string f in faltando) Console.WriteLine($"  - {f}");
        Console.WriteLine("  (rode o comando 'dmi' pra converter, ou o nome mudou de caixa)");
    }
    return 0;
}

if (args.Length >= 2 && args[0] == "mover")
{
    // mover <zona.col> : anda de verdade sobre o mapa de colisao e testa a parede
    MoveBench.Run(args[1]);
    return 0;
}

if (args.Length >= 2 && args[0] == "binario")
{
    // binario <pastaMaps> [caminho do Godot] : .tscn (texto) -> .scn (binario)
    //
    // O proprio editor pede isso: cada andar tem 250 mil celulas escritas como texto decimal,
    // ~3 MB por planeta. O .scn guarda os mesmos bytes como bytes.
    SceneBinary.Converter(Path.GetFullPath(args[1]), args.Length > 2 ? args[2] : null);
    return 0;
}

if (args.Length >= 2 && args[0] == "colisao")
{
    // colisao <pastaMaps> : regera os .col A PARTIR DAS CENAS
    //
    // Rode depois de editar um mapa no Godot. Sem isto, o que voce apagar no editor continua
    // sendo parede pro servidor -- o jogador ve chao e leva correcao de movimento.
    SceneCollision.Regerar(Path.GetFullPath(args[1]));
    return 0;
}

if (args.Length >= 1 && args[0] == "luta")
{
    // luta [races.json] : banco de prova do combate (cadencia, gap, duelos, corpo)
    Jandirus.Core.Races.RaceCatalog? cc = args.Length > 1 && File.Exists(args[1])
        ? Jandirus.Core.Races.RaceCatalog.Parse(File.ReadAllText(args[1]))
        : null;
    CombatBench.Run(cc);
    return 0;
}

if (args.Length >= 2 && args[0] == "treino")
{
    // treino <races.json> : ritmo de ganho de BP por atividade
    TrainBench.Run(Jandirus.Core.Races.RaceCatalog.Parse(File.ReadAllText(args[1])));
    return 0;
}

if (args.Length >= 3 && args[0] == "races")
{
    // races <pastaCode> <arquivoSaida.json>
    List<ProtoDef> protos = DmProtoScanner.Scan(Path.GetFullPath(args[1]));
    protos.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

    var sb = new System.Text.StringBuilder("[\n");
    for (int i = 0; i < protos.Count; i++)
    {
        ProtoDef p = protos[i];
        sb.Append("  {\n");
        sb.Append($"    \"nome\": \"{p.Name}\",\n");
        sb.Append("    \"stats\": {").Append(Plano(p.Stats)).Append("},\n");
        sb.Append("    \"misc\": {").Append(Plano(p.Misc)).Append("},\n");
        sb.Append("    \"classes\": {");
        int c = 0;
        foreach ((string cls, Dictionary<string, double> v) in p.ClassStats)
            sb.Append(c++ > 0 ? ", " : "").Append($"\"{cls}\": {{").Append(Plano(v)).Append('}');
        sb.Append("},\n");
        sb.Append("    \"spread\": [");
        for (int k = 0; k < p.ClassSpread.Count; k++)
            sb.Append(k > 0 ? ", " : "").Append($"[\"{p.ClassSpread[k].Classe}\", {p.ClassSpread[k].Chance}]");
        sb.Append("]\n  }").Append(i < protos.Count - 1 ? ",\n" : "\n");
    }
    sb.Append("]\n");

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
    File.WriteAllText(args[2], sb.ToString(), new System.Text.UTF8Encoding(false));

    int comClasse = protos.Count(p => p.ClassStats.Count > 0);
    Console.WriteLine($"protos extraidos : {protos.Count}");
    Console.WriteLine($"com class_stats  : {comClasse}");
    Console.WriteLine($"com Class_Spread : {protos.Count(p => p.ClassSpread.Count > 0)}");
    Console.WriteLine($"chaves de stat   : {protos.SelectMany(p => p.Stats.Keys).Distinct().Count()} + misc {protos.SelectMany(p => p.Misc.Keys).Distinct().Count()}");
    Console.WriteLine($"saida            : {args[2]}");
    foreach (ProtoDef p in protos.Take(6))
        Console.WriteLine($"  {p.Name,-14} stats={p.Stats.Count,2} misc={p.Misc.Count,2} classes={p.ClassStats.Count,2} spread={p.ClassSpread.Count}");
    return 0;

    static string Plano(Dictionary<string, double> d) =>
        string.Join(", ", d.Select(kv =>
            $"\"{kv.Key}\": {kv.Value.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)}"));
}

if (args.Length >= 5 && args[0] == "maps")
{
    // maps <pastaMaps> <pastaCode> <pastaSprites> <pastaSaida>
    Console.WriteLine("lendo a arvore de tipos DM...");
    Dictionary<string, TurfDef> turfDefs = DmTurfScanner.Scan(Path.GetFullPath(args[2]));
    Console.WriteLine($"turfs: {turfDefs.Count}");
    MapConverter.Convert(Path.GetFullPath(args[1]), Path.GetFullPath(args[3]), Path.GetFullPath(args[4]), turfDefs);
    return 0;
}

if (args.Length < 3 || args[0] != "dmi")
{
    Console.Error.WriteLine("uso: dmi <origem> <destino> [--limit N] [--report caminho.csv]");
    Console.Error.WriteLine("     turfs <pastaCode> [--all]");
    return 1;
}

string src = Path.GetFullPath(args[1]);
string dst = Path.GetFullPath(args[2]);
int limit = int.MaxValue;
string? report = null;
for (int i = 3; i < args.Length - 1; i++)
{
    if (args[i] == "--limit") int.TryParse(args[i + 1], out limit);
    if (args[i] == "--report") report = args[i + 1];
}

if (!Directory.Exists(src)) { Console.Error.WriteLine($"origem nao existe: {src}"); return 1; }

string[] files = Directory.GetFiles(src, "*.dmi", SearchOption.AllDirectories);
Console.WriteLine($"encontrados {files.Length} .dmi em {src}");

int ok = 0, semMeta = 0, falhou = 0, animsTotal = 0, framesTotal = 0;
var overflowed = new List<string>();
var linhas = new List<string> { "arquivo;estados;animacoes;quadros;icone;folha;overflow" };

foreach (string file in files.Take(limit))
{
    string rel = Path.GetRelativePath(src, file);
    string relNoExt = Path.ChangeExtension(rel, null);

    try
    {
        DmiFile.Result? dmi = DmiFile.Read(file);
        if (dmi is null) { falhou++; continue; }
        if (dmi.States.Count == 1 && dmi.States[0].Name == "" && dmi.States[0].Frames == 1) semMeta++;

        string pngPath = Path.Combine(dst, relNoExt + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
        File.Copy(file, pngPath, overwrite: true); // .dmi E um PNG: copia crua, sem recodificar

        // caminho res:// que o .tres usa pra achar o atlas
        string resRel = Path.GetRelativePath(Directory.GetCurrentDirectory(), pngPath).Replace('\\', '/');
        string pngRes = "res://" + resRel;

        string tresPath = Path.Combine(dst, relNoExt + ".tres");
        SpriteFramesWriter.Stats st = SpriteFramesWriter.Write(tresPath, pngRes, dmi);

        ok++;
        animsTotal += st.Animations;
        framesTotal += st.Frames;
        if (st.Overflow) overflowed.Add(rel);

        linhas.Add($"{rel};{dmi.States.Count};{st.Animations};{st.Frames};" +
                   $"{dmi.IconWidth}x{dmi.IconHeight};{dmi.SheetWidth}x{dmi.SheetHeight};{(st.Overflow ? "SIM" : "")}");
    }
    catch (Exception ex)
    {
        falhou++;
        Console.Error.WriteLine($"  !! {rel}: {ex.Message}");
    }
}

Console.WriteLine($"convertidos    : {ok}");
Console.WriteLine($"sem metadados  : {semMeta} (viraram 1 animacao estatica)");
Console.WriteLine($"falharam       : {falhou}");
Console.WriteLine($"animacoes      : {animsTotal}");
Console.WriteLine($"quadros        : {framesTotal}");
if (overflowed.Count > 0)
{
    Console.WriteLine($"ATENCAO: {overflowed.Count} folha(s) menores que os metadados declaram (quadros ignorados):");
    foreach (string f in overflowed.Take(10)) Console.WriteLine($"  - {f}");
}

if (report != null)
{
    File.WriteAllLines(report, linhas);
    Console.WriteLine($"relatorio: {report}");
}

return 0;
