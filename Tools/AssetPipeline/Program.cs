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

if (args.Length >= 2 && args[0] == "effector")
{
    // effector <pastaCode> [saida.json] : extrai os DEGRAUS de nivel das skills
    //
    // O `effector()` e o TIQUE da skill: ela acumula exp sozinha, passa do `expbarrier` e sobe de
    // nivel -- e cada nivel destrava alguma coisa. E o unico canal de efeito que o extrator ainda
    // nao lia, e ele responde por 75 verbos e 32 skills destravadas.
    string pasta = args[1];
    string saida = args.Length >= 3 ? args[2] : System.IO.Path.Combine("Assets", "data", "niveis.json");

    var effs = Jandirus.Tools.DmEffectorScanner.Scan(pasta, out var barreiras);
    Console.WriteLine(Jandirus.Tools.DmEffectorScanner.Resumo(effs));

    // A BARREIRA VOLTA PRO CATALOGO DE SKILLS. Sem ela toda skill sobe na barreira padrao 100
    // enquanto as arvores de Ki pedem 5.000 -- erro de cinquenta vezes, sempre a favor do jogador.
    var sk = Jandirus.Tools.DmSkillScanner.Scan(pasta);
    int casadas = Jandirus.Tools.DmEffectorScanner.Aplicar(sk, barreiras);
    Console.WriteLine($"\nbarreiras casadas com o catalogo: {casadas} de {barreiras.Count}");

    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(saida)!);
    Jandirus.Tools.DmEffectorScanner.Escrever(saida, effs);
    Console.WriteLine($"gravado: {saida}");
    return 0;
}

if (args.Length >= 2 && args[0] == "planetas")
{
    // planetas <pastaCode> [saida.json] : extrai a gravidade de cada planeta do DM
    string pasta = args[1];
    string saida = args.Length >= 3 ? args[2] : System.IO.Path.Combine("Assets", "data", "planetas.json");

    var defs = Jandirus.Tools.DmPlanetScanner.Scan(pasta);
    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(saida)!);
    System.IO.File.WriteAllText(saida, Jandirus.Tools.DmPlanetScanner.ParaJson(defs));

    Console.WriteLine($"planetas com gravidade declarada: {defs.Count}\n");
    Console.WriteLine($"{"PLANETA",-24} {"GRAV",6}  TIPO");
    Console.WriteLine(new string('-', 48));
    foreach (var d in defs.OrderByDescending(p2 => p2.Gravidade))
        Console.WriteLine($"{d.Nome,-24} {d.Gravidade,6:0.##}x  {d.Tipo}");

    Console.WriteLine($"\ngravado: {saida}");
    Console.WriteLine("\nA GRAVIDADE vem do DM (Gravity.dm). O TIPO nao existe la -- e leitura "
                      + "nossa, declarada em DmPlanetScanner.TipoPorNome.");
    return 0;
}

if (args.Length >= 2 && args[0] == "estilos")
{
    // estilos <pastaCode> [saida.json] : extrai os estilos de luta e MOSTRA o multiplicador REAL
    string pasta = args[1];
    string saida = args.Length >= 3 ? args[2] : System.IO.Path.Combine("Assets", "data", "estilos.json");

    var defs = Jandirus.Tools.DmStyleScanner.Scan(pasta);
    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(saida)!);
    System.IO.File.WriteAllText(saida, Jandirus.Tools.DmStyleScanner.ParaJson(defs));

    // CARREGA PELO MESMO CAMINHO QUE O JOGO: a tabela abaixo sai do catalogo do Core lendo o
    // JSON recem-gravado, nao dos objetos em memoria. Se o emissor e o leitor divergirem, e
    // aqui que aparece -- e nao seis meses depois, em jogo.
    Jandirus.Core.Combat.Estilos.Carregar(System.IO.File.ReadAllText(saida));
    Console.WriteLine($"estilos: {defs.Count} extraidos, {Jandirus.Core.Combat.Estilos.Total} relidos");
    // A TABELA MOSTRA OS DOIS NUMEROS DE PROPOSITO. O "pts" e o que esta escrito no DM; o "x" e o
    // que o jogador sente. Ver os dois lado a lado e o que impede alguem de reler o arquivo daqui
    // a um ano, ver "6" e achar que e multiplicador.
    Console.WriteLine($"\n{"ESTILO",-14} {"custo",5}   " + string.Join("  ",
        new[] { "physoff", "physdef", "tecnica", "kioff", "kidef", "kiskill", "speed", "kiregen", "stamina" }
            .Select(h => $"{h,-13}")));
    Console.WriteLine(new string('-', 140));
    foreach (var d in defs.OrderBy(e => e.LearnCost))
    {
        var e2 = Jandirus.Core.Combat.Estilos.Get(d.Id);
        if (e2 == null) { Console.WriteLine($"{d.Nome,-14} (nao carregou)"); continue; }
        var m = Jandirus.Core.Combat.Estilos.Multiplicadores(e2);
        string L(string dk, string mk) =>
            $"{d.Defaults.GetValueOrDefault(dk),1:0}pts {m.GetValueOrDefault(mk, 1),6:0.000}x";
        Console.WriteLine($"{d.Nome,-14} {d.LearnCost,5:0}   "
            + string.Join("  ", new[]
            {
                L("defaultphysoff", "physoffStyle"), L("defaultphysdef", "physdefStyle"),
                L("defaultechnique", "techniqueStyle"), L("defaultkioff", "kioffStyle"),
                L("defaultkidef", "kidefStyle"), L("defaultkiskill", "kiskillStyle"),
                L("defaultspeed", "speedStyle"), L("defaultkiregen", "kiregenStyle"),
                L("defaultsstaminamod", "staminadrainStyle"),
            }));
    }
    Console.WriteLine($"\ngravado: {saida}");
    Console.WriteLine("\nlembrete: `pts` e o que o DM escreve; `x` e o multiplicador real "
                      + "(1 + (pts-1) * mudanca; 0,02 fisico, 0,01 mental e abstrato).");
    return 0;
}

if (args.Length >= 2 && args[0] == "tech")
{
    // tech <pastaCode> [saida.json] : extrai as construcoes (obj/Creatables) da arvore do DM
    string pasta = args[1];
    string saida = args.Length >= 3 ? args[2] : System.IO.Path.Combine("Assets", "data", "construcoes.json");

    var defs = Jandirus.Tools.DmTechScanner.Scan(pasta);
    Console.WriteLine($"construcoes com preco ou requisito: {defs.Count}");
    Console.WriteLine($"  so pra certas racas : {defs.Count(d => d.Racas.Count > 0)}");
    Console.WriteLine($"  tech necessario 0   : {defs.Count(d => d.TechNecessario <= 0)}");

    Console.WriteLine($"\n{"CONSTRUCAO",-32} {"TECH",5} {"CUSTO",12}  RACAS");
    Console.WriteLine(new string('-', 72));
    foreach (var d in defs.OrderByDescending(d2 => d2.TechNecessario).Take(14))
        Console.WriteLine($"{d.Nome,-32} {d.TechNecessario,5:0} {d.Custo,12:N0}  {string.Join("/", d.Racas)}");

    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(saida)!);
    System.IO.File.WriteAllText(saida, Jandirus.Tools.DmTechScanner.ParaJson(defs));
    Console.WriteLine($"\ngravado: {saida}");
    return 0;
}

if (args.Length >= 1 && args[0] == "efeitos")
{
    // efeitos : o que as skills FAZEM, e quanto disso ja foi portado.
    //
    // ESTE RELATORIO EXISTE PRA NAO SE MENTIR. Com 366 entradas no catalogo e facil olhar a aba
    // de skills cheia e concluir que o sistema esta pronto. Aqui sai a conta: quantas somam num
    // campo (pronto), quantas destravam uma tecnica (depende de a tecnica estar portada) e
    // quantas nao fazem nada ainda -- alem dos campos que o DM buffa e que o port NAO TEM, que
    // e o jeito mais rapido de descobrir que uma skill esta prometendo o que nao entrega.
    string cs = System.IO.Path.Combine("Assets", "data", "skills.json");
    string ct = System.IO.Path.Combine("Assets", "data", "skilltrees.json");
    if (!System.IO.File.Exists(cs)) { Console.Error.WriteLine($"faltou {cs} -- rode o comando 'skills'"); return 1; }

    var cat = Jandirus.Core.Skills.SkillCatalog.Parse(System.IO.File.ReadAllText(cs), System.IO.File.ReadAllText(ct));
    var folhas = cat.Todas.Where(s2 => !s2.Arvore).ToList();
    var comBuff = folhas.Where(s2 => s2.Buffs.Count > 0).ToList();
    var comVerb = folhas.Where(s2 => s2.Verbos.Length > 0).ToList();
    var vazias = folhas.Where(s2 => s2.SemEfeito).ToList();

    Console.WriteLine("=== O QUE AS SKILLS FAZEM (quatro canais) ===");
    Console.WriteLine($"folhas (skills de verdade) : {folhas.Count}");
    Console.WriteLine($"  1. somam num campo       : {comBuff.Count}");
    Console.WriteLine($"  2. multiplicam um campo  : {folhas.Count(s2 => s2.Mults.Count > 0)}");
    Console.WriteLine($"  3. mexem na genetica     : {folhas.Count(s2 => s2.Genes.Count > 0)}");
    Console.WriteLine($"  4. escrevem flag/contador: {folhas.Count(s2 => s2.Flags.Count > 0)}");
    Console.WriteLine($"  destravam tecnica ativa  : {comVerb.Count}");
    Console.WriteLine($"  sem efeito portado ainda : {vazias.Count}");
    // CONTA A PARTIR DO CATALOGO, nao da tabela de tecnicas: a tabela so conhece o que ja foi
    // portado, e contar por ela dava "5 de 5 portadas" -- cobertura de 100% que era so a tabela
    // olhando pro proprio umbigo. O denominador tem que ser o que o JOGO concede.
    var todosVerbos = folhas.SelectMany(s2 => s2.Verbos).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    int portadas = todosVerbos.Count(v =>
        Jandirus.Core.Skills.Tecnicas.Get(v) is { Modo: not Jandirus.Core.Skills.Modo.NaoPortada });
    Console.WriteLine($"\ntecnicas ativas que as skills concedem: {todosVerbos.Count}"
                      + $"  |  ja portadas: {portadas}");
    foreach (var t in Jandirus.Core.Skills.Tecnicas.Todas.Where(t2 => t2.Modo != Jandirus.Core.Skills.Modo.NaoPortada))
        Console.WriteLine($"   [ok] {t.Nome,-14} {t.Modo}");

    // OS CAMPOS: quantas skills mexem em cada um, e se o port sequer TEM o campo.
    var porCampo = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var s2 in comBuff) foreach (var kv in s2.Buffs) porCampo[kv.Key] = porCampo.GetValueOrDefault(kv.Key) + 1;

    var cobaia = new Jandirus.Core.Stats.Fighter();
    Console.WriteLine($"\n{"CAMPO",-20} {"SKILLS",7}  EXISTE NO PORT?");
    Console.WriteLine(new string('-', 52));
    foreach (var kv in porCampo.OrderByDescending(k => k.Value))
    {
        bool tem = typeof(Jandirus.Core.Stats.Fighter)
            .GetField(kv.Key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) != null;
        Console.WriteLine($"{kv.Key,-20} {kv.Value,7}  {(tem ? "sim" : "NAO -- a skill nao faz nada")}");
    }

    // APLICACAO DE VERDADE numa ficha: aprende tudo que tem buff e mostra o antes/depois. E
    // tambem a prova de que reaplicar NAO empilha -- a segunda passada tem que dar o mesmo numero.
    double off0 = cobaia.physoffBuff, tec0 = cobaia.techniqueBuff;
    int mexidos = Jandirus.Core.Skills.EfeitosDeSkill.Aplicar(cobaia, cat, comBuff.Select(s2 => s2.Path).ToList());
    double off1 = cobaia.physoffBuff, tec1 = cobaia.techniqueBuff;
    Jandirus.Core.Skills.EfeitosDeSkill.Aplicar(cobaia, cat, comBuff.Select(s2 => s2.Path).ToList());
    bool idem = Math.Abs(cobaia.physoffBuff - off1) < 1e-9;

    Console.WriteLine($"\naprendendo as {comBuff.Count} com buff: {mexidos} campos mexidos");
    Console.WriteLine($"   physoffBuff   {off0:0.##} -> {off1:0.##}");
    Console.WriteLine($"   techniqueBuff {tec0:0.##} -> {tec1:0.##}");
    Console.WriteLine($"   reaplicar nao empilha: {(idem ? "OK" : "FALHOU -- buff dobrou")}");

        // OS CAMPOS QUE FALTAM, E POR QUE.
    //
    // ESTA TABELA JA ESTEVE ERRADA, e vale contar como: eu tinha dado `bodyskill` e
    // `bodyreadiness` como "mortos no proprio DM", porque a varredura que usei filtrava as linhas
    // com `savant.` pra separar escrita de leitura -- e assim apagou justamente as LEITURAS
    // (`if(savant.bodyskill>2)`). Os dois sao o gate de QUATRO arvores (Body.dm:24-32). Licao:
    // quando um campo parece morto, procure a leitura com o filtro DESLIGADO antes de concluir.
    var explicado = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["HPregenbuff"] = "real: multiplica a cura passiva -- o port nao tem regen passiva de HP",
        ["lssjmult"] = "real: multiplicador da forma Legendary -- LSSJ ainda nao portada",
        ["lssjenergymod"] = "real: Ki da forma Legendary -- LSSJ ainda nao portada",
        ["lssjdrain"] = "real: dreno da forma Legendary -- LSSJ ainda nao portada",
        ["gene:Regeneration"] = "real: regen por morte (DeathRegen) -- o port nao tem",
        ["gene:Lifespan"] = "real: expectativa de vida -- o port nao tem envelhecimento",
        ["gene:Skillpoint Mod"] = "real: quantos marcos por marco de BP -- o port fixa isso",
        // VERIFICADOS UM A UM com busca de LEITURA (sem filtro), depois do erro do bodyskill:
        ["canbigform"] = "MORTO: so a declaracao em Giants.dm:2, zero leituras. Quem da o verb e o assignverb ao lado",
        ["KiSpecialty"] = "MORTO: nem declaracao viva sobrou. A desc promete mastery >50 e nada implementa",
        ["didbodychange"] = "desnecessario aqui: e o gatilho de recalculo do DM (Body.dm:22,38); o port recalcula a cada mudanca do livro",
        ["can_sup_el"] = "real: lido em Magic.dm:90 -- gate de magia, subsistema nao portado",
        ["gravitate"] = "real: lido em Spirit.dm:25 -- rota exclusiva dentro da arvore Spirit",
        ["gravitatecheck"] = "par do gravitate: marca que a rota ja foi escolhida",
        ["pitted"] = "real: lido em TechResearch.dm:92,122 -- Tsujin com pitted pula o requisito de tech de DUAS receitas de bancada, que o port nao tem",
        ["effspec"] = "real: rota exclusiva de especializacao dentro da arvore",
        ["hasayyform"] = "real: marca a forma Arlian desbloqueada -- forma nao portada",
        ["kiforceful"] = "real: golpe atravessa guarda -- so tem sentido com projetil, que o port nao tem",
        ["word_power"] = "real: poder de palavra magica -- subsistema de magia nao portado",
        ["ritual_power"] = "real: poder de ritual -- subsistema de magia nao portado",
        ["magiBuff"] = "EXISTE no port: se aparecer aqui e porque veio por atribuicao direta, nao por soma",
    };
    if (Jandirus.Core.Skills.EfeitosDeSkill.Desconhecidos.Count > 0)
    {
        Console.WriteLine("\ncampos que o DM mexe e o port nao tem:");
        foreach (string campo in Jandirus.Core.Skills.EfeitosDeSkill.Desconhecidos)
            Console.WriteLine($"   {campo,-22} {explicado.GetValueOrDefault(campo, "sem explicacao -- INVESTIGAR")}");
    }

    // O ARCO DO SOLAR FLARE. A vitima esta no ponto e olhando pra alguma direcao; quem estoura a
    // luz esta na ORIGEM. Cegar tem que valer so pra quem olha pra ca -- e a unica defesa que a
    // tecnica tem, e quebra calada se alguem mexer no enum de direcao.
    Console.WriteLine("\n=== ARCO DO SOLAR FLARE (a luz estoura em (0,0)) ===");
    Console.WriteLine($"{"vitima em",-14} {"olhando",-8} {"cega?",6}  esperado");
    Console.WriteLine(new string('-', 50));
    (int x, int y, Jandirus.Core.World.Facing dir, bool esperado)[] casos =
    [
        (0, -100, Jandirus.Core.World.Facing.South, true),   // acima, olhando pra baixo = de frente
        (0, -100, Jandirus.Core.World.Facing.North, false),  // acima, olhando pra cima = de costas
        (0,  100, Jandirus.Core.World.Facing.North, true),
        (0,  100, Jandirus.Core.World.Facing.South, false),
        (100,  0, Jandirus.Core.World.Facing.West,  true),
        (100,  0, Jandirus.Core.World.Facing.East,  false),
        (100, -100, Jandirus.Core.World.Facing.West, true),  // diagonal: dentro dos 135 graus
        (100, -100, Jandirus.Core.World.Facing.East, false),
        (100, -100, Jandirus.Core.World.Facing.South, true),
    ];
    int erros = 0;
    foreach (var (x, y, dir, esperado) in casos)
    {
        bool deu = Jandirus.Core.Skills.Tecnicas.EstaOlhandoPra(
            new Jandirus.Core.World.Vec2(x, y), dir, new Jandirus.Core.World.Vec2(0, 0));
        if (deu != esperado) erros++;
        Console.WriteLine($"({x,5},{y,5})  {dir,-8} {(deu ? "sim" : "nao"),6}  "
                          + (deu == esperado ? "ok" : $"ERRO -- esperava {(esperado ? "sim" : "nao")}"));
    }
    Console.WriteLine(erros == 0 ? "arco: OK" : $"arco: {erros} DIVERGENCIAS");

    // CUSTO DAS TECNICAS na pratica -- o BaseDrain escala com o MaxKi e isso precisa ser visivel.
    Console.WriteLine($"\n{"MaxKi",10} {"BaseDrain",10} {"SolarFlare",11} {"recarga",9} {"alcance",8} {"cegueira",9}");
    Console.WriteLine(new string('-', 62));
    foreach (double maxki in new[] { 100d, 1_000d, 20_000d, 500_000d })
    {
        var f = new Jandirus.Core.Stats.Fighter { MaxKi = maxki, Ekiskill = 1 + maxki / 50_000 };
        Console.WriteLine($"{maxki,10:N0} {f.BaseDrain(),10:0.##} {Jandirus.Core.Skills.Tecnicas.SolarCustoKi(f),11:N0} "
                          + $"{Jandirus.Core.Skills.Tecnicas.SolarRecargaMs(f) / 1000.0,8:0.#}s "
                          + $"{Jandirus.Core.Skills.Tecnicas.SolarAlcanceTiles(f),8:0.#} "
                          + $"{Jandirus.Core.Skills.Tecnicas.SolarCegueiraMs(f) / 1000.0,8:0.#}s");
    }
    return 0;
}

if (args.Length >= 1 && args[0] == "espaco")
{
    // espaco : confere a escala do universo contra o anime e mede a densidade de planetas
    using var esp = new System.IO.StringWriter();
    Console.WriteLine("=== ESCALA (derivada, nao cravada) ===");
    Console.WriteLine($"dia in-game        : {Jandirus.Core.World.Espaco.SegundosPorDiaInGame / 60:0} min reais");
    Console.WriteLine($"velocidade de voo  : {Jandirus.Core.World.MoveRules.BaseSpeedPx:0} px/s");
    Console.WriteLine($"chunk              : {Jandirus.Core.World.Espaco.ChunkTiles} tiles = {Jandirus.Core.World.Espaco.ChunkPx} px");

    double d = Jandirus.Core.World.Espaco.DistanciaTerraNamek;
    Console.WriteLine($"\nTerra -> Namek     : {d:N0} px  ({d / 32:N0} tiles, {d / Jandirus.Core.World.Espaco.ChunkPx:N0} chunks)");
    Console.WriteLine($"                     {Jandirus.Core.World.Espaco.SegundosDeViagem(d) / 60:0.0} min reais"
                      + $" = {Jandirus.Core.World.Espaco.DiasInGame(d):0.00} dias in-game  (o anime: 7)");

    Console.WriteLine($"\n{"PLANETA",-14} {"CHUNK",-12} {"DIST (px)",14} {"DIAS",7} {"MIN REAIS",10}");
    Console.WriteLine(new string('-', 62));
    var terra = Jandirus.Core.World.Espaco.PreFeitos().First();
    foreach (Jandirus.Core.World.PlanetaNoEspaco p in Jandirus.Core.World.Espaco.PreFeitos())
    {
        double dist = (p.Pos - terra.Pos).Length;
        Console.WriteLine($"{p.Nome,-14} {p.Chunk.ToString(),-12} {dist,14:N0} "
                          + $"{Jandirus.Core.World.Espaco.DiasInGame(dist),7:0.0} "
                          + $"{Jandirus.Core.World.Espaco.SegundosDeViagem(dist) / 60,10:0}");
    }

    // DENSIDADE: o espaco tem que parecer vazio. Um planeta a cada ~40 chunks e o alvo.
    const ulong seed = 20260802;
    int achados = 0, varridas = 0;
    for (int y = -60; y <= 60; y++)
        for (int x = -60; x <= 60; x++)
        {
            varridas++;
            if (Jandirus.Core.World.Espaco.PlanetaDaChunk(seed, new Jandirus.Core.World.ChunkId(x, y)) != null) achados++;
        }
    Console.WriteLine($"\nvarredura de {varridas:N0} chunks: {achados} planetas procedurais "
                      + $"(1 a cada {(double)varridas / Math.Max(achados, 1):0.0} chunks)");

    // ESTABILIDADE: a mesma chunk tem que dar o mesmo planeta SEMPRE -- e o que permite cliente
    // e servidor gerarem o universo sem trocar um byte.
    var c = new Jandirus.Core.World.ChunkId(17, -33);
    var a1 = Jandirus.Core.World.Espaco.PlanetaDaChunk(seed, c);
    var a2 = Jandirus.Core.World.Espaco.PlanetaDaChunk(seed, c);
    Console.WriteLine($"estabilidade em {c}: {(a1?.Nome ?? "vazio")} == {(a2?.Nome ?? "vazio")} -> "
                      + ((a1?.Nome == a2?.Nome && a1?.Pos.X == a2?.Pos.X) ? "OK" : "DIVERGIU"));

    var perto = Jandirus.Core.World.Espaco.PorPerto(seed, new Jandirus.Core.World.ChunkId(0, 0));
    Console.WriteLine($"visiveis da chunk (0,0): {string.Join(", ", perto.Select(p => p.Nome))}");
    return 0;
}

if (args.Length >= 3 && args[0] == "skills")
{
    // skills <pastaCode> <saida.json> : extrai o catalogo de skills da arvore de tipos DM
    Dictionary<string, SkillDef> sk = DmSkillScanner.Scan(Path.GetFullPath(args[1]));
    var reais = sk.Values.Where(s => s.Nome.Length > 0).ToList();
    var arvores = reais.Where(s => s.Arvore).ToList();
    var folhas = reais.Where(s => !s.Arvore).ToList();

    Console.WriteLine($"blocos lidos     : {sk.Count}");
    Console.WriteLine($"com nome         : {reais.Count}  ({arvores.Count} arvores, {folhas.Count} skills)");
    Console.WriteLine($"ligadas          : {folhas.Count(s => s.Ligada)}");
    Console.WriteLine($"com raca         : {folhas.Count(s => s.Racas.Count > 0)}");
    Console.WriteLine($"com classe       : {folhas.Count(s => s.Classes.Count > 0)}");
    Console.WriteLine($"com pre-requisito: {folhas.Count(s => s.PreReqs.Count > 0)}");
    Console.WriteLine($"comuns (todos)   : {folhas.Count(s => s.Comum)}");
    Console.WriteLine($"so vilao         : {folhas.Count(s => s.SoVilao)}");

    // PRE-REQUISITO QUE NAO EXISTE e o erro que so aparece quando alguem tenta aprender:
    // a skill fica travada pra sempre porque espera um pai que ninguem consegue ter.
    var orfaos = folhas.SelectMany(s => s.PreReqs.Select(p => (s.Path, p)))
                       .Where(x => !sk.ContainsKey(x.p)).ToList();
    if (orfaos.Count > 0)
    {
        Console.WriteLine($"\nATENCAO: {orfaos.Count} pre-requisitos apontam pra skill inexistente");
        foreach ((string quem, string alvo) in orfaos.Take(8)) Console.WriteLine($"   {quem}  ->  {alvo}");
    }

    Console.WriteLine("\npor tipo:");
    foreach (var g in folhas.GroupBy(s => s.Tipo).OrderByDescending(g => g.Count()))
        Console.WriteLine($"   {g.Key,-12} {g.Count()}");

    Console.WriteLine("\nexemplos:");
    foreach (SkillDef d in folhas.Where(s => s.Racas.Count > 0 || s.PreReqs.Count > 0).Take(6))
        Console.WriteLine($"   {d.Nome,-26} t{d.Tier} custo {d.Custo} racas[{string.Join(",", d.Racas)}] " +
                          $"pre[{d.PreReqs.Count}] {d.Tipo}");

    DmSkillScanner.Escrever(Path.GetFullPath(args[2]), sk);
    Console.WriteLine($"\ngravado: {args[2]}");

    DmSkillScanner.Concessoes con = DmSkillScanner.LerConcessoes(Path.GetFullPath(args[1]));
    string outCon = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!, "skilltrees.json");
    DmSkillScanner.EscreverConcessoes(outCon, con);
    Console.WriteLine($"arvores de todos : {string.Join(", ", con.Todos)}");
    Console.WriteLine($"racas com arvore : {con.PorRaca.Count} | classes com arvore: {con.PorClasse.Count}");
    foreach ((string r, List<string> a) in con.PorRaca.Take(6)) Console.WriteLine($"   {r,-14} {string.Join(", ", a)}");

    // arvore concedida que nao existe no catalogo = povo sem progressao e ninguem avisa
    var somem = con.Todos.Concat(con.PorRaca.Values.SelectMany(x => x)).Concat(con.PorClasse.Values.SelectMany(x => x))
                   .Distinct().Where(p => !sk.ContainsKey(p)).ToList();
    if (somem.Count > 0) Console.WriteLine($"ATENCAO: {somem.Count} arvores concedidas sem definicao: {string.Join(", ", somem)}");
    Console.WriteLine($"gravado: {outCon}");

    // ---- CONFERENCIA: le de volta o que acabou de escrever e simula a progressao ----
    //
    // Escrever o catalogo e so metade. A pergunta que importa e "cada raca consegue de fato
    // aprender alguma coisa?" -- e uma raca com ZERO ofertas nao acusa em lugar nenhum: o
    // jogador so descobre que a aba Learning dele esta vazia pra sempre.
    var cat = Jandirus.Core.Skills.SkillCatalog.Parse(
        File.ReadAllText(Path.GetFullPath(args[2])), File.ReadAllText(outCon));
    Console.WriteLine($"\nrelido: {cat.Total} entradas | {cat.Arvores.Count()} arvores");

    Console.WriteLine($"\n{"RACA",-14} {"ARVORES",8} {"OFERTAS",8}  primeiras");
    Console.WriteLine(new string('-', 74));
    foreach (string raca in new[] { "Saiyan", "Human", "Namekian", "Icer", "Kai", "Majin", "Heran", "Android", "Alien", "Bio-Android" })
    {
        var livro = new Jandirus.Core.Skills.SkillBook();
        livro.Conceder(99);
        var ofertas = livro.Ofertas(cat, raca, "", false).ToList();
        int quantasArvores = cat.ArvoresDe(raca, "").Count;
        string amostra = string.Join(", ", ofertas
            .Where(o => o.Estado == Jandirus.Core.Skills.Recusa.Pode)
            .Take(3).Select(o => o.Skill.Nome));
        Console.WriteLine($"{raca,-14} {quantasArvores,8} {ofertas.Count,8}  {amostra}");
    }
    return 0;
}

if (args.Length >= 5 && args[0] == "maps")
{
    // maps <pastaMaps> <pastaCode> <pastaSprites> <pastaSaida>
    Console.WriteLine("lendo a arvore de tipos DM...");
    Dictionary<string, TurfDef> turfDefs = DmTurfScanner.Scan(Path.GetFullPath(args[2]));

    // AS FICHAS DOS PLANETAS ANTES DE CONVERTER: o conversor cola nome, gravidade e tipo na
    // raiz de cada cena, e a fonte disso e o mesmo extrator que gera o `planetas.json` que o
    // servidor le. Sem esta linha as cenas nasceriam todas com gravidade 1.
    var fichas = Jandirus.Tools.DmPlanetScanner.Scan(Path.GetFullPath(args[2]));
    MapConverter.UsarPlanetas(Jandirus.Core.World.CatalogoDePlanetas.Parse(
        Jandirus.Tools.DmPlanetScanner.ParaJson(fichas)));
    Console.WriteLine($"planetas com ficha: {fichas.Count}");
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
