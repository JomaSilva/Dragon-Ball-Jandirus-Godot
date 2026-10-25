using System.Text;
using System.Text.RegularExpressions;

namespace Jandirus.Tools;

/// <summary>Um estilo de cabelo: o nome do card e o sprite (nulo = careca).</summary>
public sealed record HairStyle(string Nome, string? Sprite);

/// <summary>Os corpos disponiveis pra uma raca, por genero.</summary>
public sealed class BodySet
{
    public List<string> Masculino = [];
    public List<string> Feminino = [];
    /// <summary>Recolorizacao livre (Majin/Kai): o jogador escolhe a cor em vez de outro arquivo.</summary>
    public bool CorLivre;
    /// <summary>Tons fixos por NOME (Namekuseijin: o verde e um blend, nao um arquivo por tom).</summary>
    public List<string> Tons = [];
}

/// <summary>
/// Extrai do DM os CATALOGOS DE APARENCIA -- cabelos, corpos por raca e roupas.
///
/// Por que extrair em vez de digitar: sao 59 estilos de cabelo com o nome do arquivo .dmi
/// literal em cada um. Copiar isso a mao erra um caractere em algum lugar e o resultado e um
/// personagem careca que ninguem entende por que.
///
/// A ARMADILHA QUE OBRIGOU O PASSO DE RESOLUCAO: o proprio jogo escreve o mesmo arquivo de
/// jeitos diferentes -- o catalogo diz 'HairFemalePonytail.dmi' e o motor de formas diz
/// 'HairFemalePonyTail.dmi'. No Windows do BYOND tanto faz; num lookup exato, nao. Entao todo
/// literal e RESOLVIDO contra os arquivos que existem de verdade, e o que sai e o nome do
/// DISCO. O que nao resolver e denunciado em vez de virar sprite faltando em jogo.
/// </summary>
public static class DmAppearanceScanner
{
    private static readonly Regex RxTipo = new(@"\.hairtype\s*=\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex RxIcone = new(@"\.hairicon\s*=\s*(?:'([^']+)'|null)", RegexOptions.Compiled);

    /// <summary>
    /// O catalogo de cabelos, na ORDEM do codigo (que e a ordem dos cards na tela do jogo).
    /// Le so o corpo de `hair_style_catalog()`: e a unica fonte com os literais reais.
    /// </summary>
    public static List<HairStyle> Cabelos(string arquivoHairChoose)
    {
        string[] linhas = File.ReadAllLines(arquivoHairChoose);
        var saida = new List<HairStyle>();
        string? pendente = null;
        bool dentro = false;

        foreach (string linha in linhas)
        {
            if (linha.StartsWith("mob/proc/hair_style_catalog", StringComparison.Ordinal)) { dentro = true; continue; }
            // o proc seguinte encerra o catalogo
            if (dentro && linha.StartsWith("mob/proc/", StringComparison.Ordinal)) break;
            if (!dentro) continue;

            Match t = RxTipo.Match(linha);
            if (t.Success) { pendente = t.Groups[1].Value; continue; }

            Match i = RxIcone.Match(linha);
            if (i.Success && pendente != null)
            {
                saida.Add(new HairStyle(pendente, i.Groups[1].Success ? i.Groups[1].Value : null));
                pendente = null;
            }
        }
        return saida;
    }

    /// <summary>
    /// Os corpos por raca. TRANSCRITO do `Skin()` (body_custom.dm:3-108) em vez de parseado:
    /// aquilo e uma cadeia if/else por raca, com dois casos que nem sao lista de arquivo
    /// (Namekuseijin e blend de verde, Majin/Kai e seletor de cor livre). Um parser dali
    /// seria mais fragil que a tabela, e a tabela e pequena e foi conferida contra o codigo.
    ///
    /// Quem NAO aparece aqui cai no corpo padrao -- e Frost Demon e Bio-Android nem tem passo
    /// de corpo no jogo: eles escolhem FORMA, que e outro sistema.
    ///
    /// ============================ (2026-10-18) AS RACAS QUE CAIAM NO CORPO HUMANO ============================
    /// O dono: *"a raça saibamen n ta com o icone do saibamen, ta com de humano, (talvez outras raças tb, resolva isso)"*. Elas nao tinham
    /// linha aqui e caiam no corpo padrao. No DM o `Skin()` manda estas pro `genome.returnIcons()` (`Genetic_Icons.dm:5-97`), que monta a
    /// lista pelas FLAGS da raca (`alternate_icon_flags`, `RaceStats/stat*.dm`):
    ///   * Saibamen -> `Saibaman - Form 1`; Makyo -> `Makyojin`; Android -> `GochekAndroid`. O "Human" que o Makyo e o Androide tambem
    ///     tem nao soma nada: o `if(!v in list(...))` do DM e sempre falso (o `!` pega o `v`, e `0 in list` nunca e verdade);
    ///   * Alien, Arlian, Kanassa e Gray (a flag "Alien") -> a lista dos 23 alienigenas do DM.
    /// E QUATRO DESVIOS DECLARADOS, todos pro lado de a raca parecer a raca:
    ///   * HERAN, META, YARDRAT e SPIRIT DOLL tem o corpo proprio NO `returnIcons` (`spacepirate`, `MetaM`/`MetaF`, `Yardrat`,
    ///     `Spirit Doll`), mas a flag delas e so "Human" e o ramo nunca roda: no DM elas ganham os corpos humanos de sempre. Aqui o corpo
    ///     proprio vem NA FRENTE -- e o retrato do card da raca (`IconesDeRaca`) --, seguido dos humanos que o DM dava;
    ///   * ALIEN, ARLIAN, KANASSA e GRAY ganham o PROPRIO retrato na frente da lista dos alienigenas (`Konatsu`, `Arlian`, `Kanassan`,
    ///     `Jiren` -- o card de cada uma);
    ///   * os quatro BICHOS da lista dos alienigenas (`cat`, `dog`, `bat`, `bunny`) ficam de fora: sao folhas de animal sem golpe, sem
    ///     queda e sem nocaute -- o boneco de quem as escolhesse sumiria em toda luta;
    ///   * o ANDROIDE leva os tres corpos humanos DEPOIS do `GochekAndroid`: o humano que vira androide no laboratorio (`VirarAndroide`)
    ///     continua com a mesma cara -- o indice dele anda um (`GameServer.Tech.cs`), e o save de antes e acertado no login
    ///     (`CharacterSave.CorposVersao`).
    /// FICAM COMO ESTAVAM: Tsujin e Shapeshifter (humanos no DM tambem; o gato do card do Shapeshifter e um bicho) e o Demigod (o
    /// `Custom_Icon` do DM e uma silhueta preta -- o molde do icone customizado --, e nao um corpo).
    /// =========================================================================================================
    /// </summary>
    public static Dictionary<string, BodySet> Corpos() => new(StringComparer.Ordinal)
    {
        ["Saiyan"] = Humanoide(),
        ["Human"] = Humanoide(),
        ["Halfbreed"] = Humanoide(),
        ["Namekian"] = new BodySet
        {
            Masculino = ["Namek Young", "Albino Namek"],
            Feminino = ["Namek Young", "Albino Namek"],
            Tons = ["Verde claro", "Verde", "Verde escuro", "Albino"],
        },
        ["Majin"] = new BodySet
        {
            // O TERCEIRO E O KID BUU (`DU/Mobs/Kid Buu`): a forma pura, pedida pelo dono pro Boo do trailer
            // das sagas. Ele ja estava convertido e importado e nenhuma lista o citava -- e o chefe
            // `majin_boo` nascia como o Majin cinza sem tinta.
            Masculino = ["Majin", "Majin1", "Kid Buu"],
            Feminino = ["Female Majin", "female majin base (small)"],
            CorLivre = true,
        },
        ["Kai"] = new BodySet { Masculino = ["Konatsu"], Feminino = ["Alien 10"], CorLivre = true },
        ["Demon"] = new BodySet
        {
            Masculino = ["Base_Skully", "Demon - Form 1", "Demon4", "DemonForm1", "Satan"],
            Feminino = ["Base_Skully", "Demon - Form 1", "Demon4", "DemonForm1", "Satan"],
        },
        // (2026-10-18) as racas que caiam no corpo humano -- ver o cabecalho
        ["Saibaman"] = Unico("Saibaman - Form 1"),
        ["Makyo"] = Unico("Makyojin"),
        ["Android"] = new BodySet
        {
            Masculino = ["GochekAndroid", .. Humanoide().Masculino],
            Feminino = ["GochekAndroid", .. Humanoide().Feminino],
        },
        ["Alien"] = Alienigenas("Konatsu"),
        ["Arlian"] = Alienigenas("Arlian"),
        ["Kanassa"] = Alienigenas("Kanassan"),
        ["Gray"] = Alienigenas("Jiren"),
        ["Heran"] = ComOsHumanosDoDm("spacepirate", null),
        ["Meta"] = ComOsHumanosDoDm("MetaM", "MetaF"),
        ["Yardrat"] = ComOsHumanosDoDm("Yardrat", "Yardrat"),
        ["SpiritDoll"] = ComOsHumanosDoDm("Spirit Doll", "Spirit Doll"),
    };

    /// <summary>(2026-10-18) Um corpo so, pros dois sexos.</summary>
    private static BodySet Unico(string corpo) => new() { Masculino = [corpo], Feminino = [corpo] };

    /// <summary>(2026-10-18) OS 23 ALIENIGENAS DO DM (`Genetic_Icons.dm:13-35`), na ordem dele.</summary>
    private static readonly string[] OsAlienigenasDoDm =
    [
        "Metamori", "Konatsu", "Yukenojin", "Hatchiyack", "Bio Experiment", "Immecka", "BuffFrog", "Kratos", "Goron", "Zora", "Base_Skully",
        "cat", "dog", "bat", "bunny", "Demon1", "Demon2", "Demon3", "Majin1", "Jiren", "El Hermano", "Kanassan", "Arlian",
    ];

    /// <summary>(2026-10-18) Os quatro bichos da lista do DM: folhas de animal, sem golpe, queda nem nocaute -- ver o cabecalho de <see cref="Corpos"/>.</summary>
    private static readonly string[] BichosSemLuta = ["cat", "dog", "bat", "bunny"];

    /// <summary>(2026-10-18) A lista dos alienigenas do DM sem os bichos, com o retrato da raca na frente.</summary>
    private static BodySet Alienigenas(string retrato)
    {
        List<string> lista = [retrato, .. OsAlienigenasDoDm.Where(c => c != retrato && !BichosSemLuta.Contains(c))];
        return new BodySet { Masculino = lista, Feminino = [.. lista] };
    }

    /// <summary>
    /// (2026-10-18) OS HUMANOS DE SEMPRE DO DM -- o `if(!skin_list.len)` do `returnIcons()` (`Genetic_Icons.dm:83-96`), seis masculinos e
    /// quatro femininos -- com o corpo proprio da raca na frente (nulo = sem corpo proprio naquele sexo).
    /// </summary>
    private static BodySet ComOsHumanosDoDm(string? masculino, string? feminino)
    {
        List<string> m = ["White Male", "Tan Male", "Black Male", "BaseWhiteMale", "BaseTanMale", "BaseBlackMale"];
        List<string> f = ["Whitefemale", "BaseWhiteFemale", "BaseTanFemale", "BaseBlackFemale"];
        if (masculino != null) m.Insert(0, masculino);
        if (feminino != null) f.Insert(0, feminino);
        return new BodySet { Masculino = m, Feminino = f };
    }

    /// <summary>Os tres tons aprovados de Saiyajin/Humano, por sexo.</summary>
    private static BodySet Humanoide() => new()
    {
        Masculino = ["NewPaleMale", "NewTanMale", "NewBlackMale"],
        Feminino = ["NewPaleFemale", "NewTanFemale", "NewBlackFemale"],
        Tons = ["Pele clara", "Pele morena", "Pele negra"],
    };

    /// <summary>
    /// O GUARDA-ROUPA: TUDO o que esta na raiz de `Clothes`, menos o que o dono tirou.
    ///
    /// ============================ REGRA DO DONO (2026-10-12) ============================
    /// *"nem todas as roupas do ...\Assets\Sprites\Clothes estao aparecendo na criaçao de personagem, eu quero TODAS
    /// (menos a do beerus e o tail) dessa pasta"*.
    ///
    /// ANTES esta lista era CURADA: ficavam de fora o halo, os brincos potara e tudo o que "nao sabe ficar de pe" (o
    /// Mega Buster so tem quadro de ataque). Agora entra tudo. O que continua de fora, e por que:
    ///   * `Tail` e a do Beerus -- o dono tirou pelo nome;
    ///   * `Eyes_*` -- nao e roupa: e a camada de OLHOS que todo corpo ja tem (a secao `olhos` deste catalogo);
    ///   * (2026-10-23) os quatro BRINCOS POTARA (`potara`, `Potara-Equipped`, `potaraleft`, `potararight`) -- o dono: os jogadores faziam
    ///     o "esquerdo" e o "direito" com a Materialize e achavam que era a Potara (*"Doesn't seem to be fusing me and nova"*). Roupa nao
    ///     funde ninguem; a Potara de verdade e o item que o Kaioshin da (`Core/Items/ParDePotara.cs`). O `potara` continua sendo o
    ///     brinco que a FUSAO veste -- quem o acha fora do guarda-roupa e a segunda porta do `GameServer.CaminhoDaPecaDeFusao`.
    ///
    /// SO A RAIZ: a pasta `Armor` e outra lista -- ver <see cref="Armaduras"/>.
    /// ====================================================================================
    /// </summary>
    public static List<string> Roupas(string pastaSprites) =>
        [.. FolhasDoGuardaRoupa(pastaSprites).Select(f => Path.GetFileNameWithoutExtension(f))];

    /// <summary>O que nao entra no guarda-roupa, por pedaco do nome. Ver <see cref="Roupas"/>.</summary>
    private static readonly string[] ForaDoGuardaRoupa = ["Eyes_", "Tail", "Beerus", "potara"];

    private static List<string> FolhasDoGuardaRoupa(string pastaSprites) =>
        Varrer(Path.Combine(pastaSprites, "Clothes"), SearchOption.TopDirectoryOnly, ForaDoGuardaRoupa, soOQueFicaDePe: false);

    /// <summary>
    /// ============================ ARMADURA NAO E ROUPA DE GUARDA-ROUPA ============================
    /// E uma lista SEPARADA porque no proprio jogo ela e outra coisa, e da pra apontar onde:
    ///
    ///   * o guarda-roupa do jogador e o `ClothingChoice()` (ClothesChoose.dm:32+), uma grade de
    ///     `DummyClothes` com os `.dmi` escritos um a um -- e **nenhuma armadura esta la**;
    ///   * a armadura e EQUIPAMENTO, com estilo proprio (`Armor.dm:100-123`, "Plating Style" ->
    ///     'Armor 8.dmi', "Elite Armor" -> 'Armor_Elite.dmi'...);
    ///   * e o NPC a veste por um caminho que nem passa pelo catalogo: `npc_wear_armor_icon`
    ///     (PlanetPopulation.dm:246-252) cria um `obj/items/clothes` PELADO e escreve o `.icon` na mao.
    ///
    /// Juntar as duas listas poria armadura de batalha na grade da criacao de personagem. Separadas, o
    /// Saiyajin do mundo veste a dele (<see cref="Jandirus.Core.Npc.RoupaDeNpc"/>) e a grade da criacao so
    /// oferece roupa.
    ///
    /// E ELA PRECISA EXISTIR NO CATALOGO de qualquer jeito: `VisualCatalog.Sanear` recusa caminho
    /// que nao esta no catalogo, e o servidor chama `Sanear` na aparencia do NPC. Sem esta lista,
    /// vestir um Saiyajin de armadura seria DESCARTADO EM SILENCIO -- ele nasceria pelado do mesmo
    /// jeito, so que agora com codigo que jura o contrario.
    ///
    /// (2026-10-12) ELA E TAMBEM A LISTA DE ARTES DA ARMADURA DE TECH -- o dono: *"a sub pasta "Armor" sao sprites
    /// da armadura q vc pode fabricar com tech, q ao fabricar uma armadura da tech, vai abrir uma tela pra vc escolher
    /// qual sprite vc quer"*. Por isso ela agora desce nas SUBPASTAS (`Mandalorian`, `Phoenix Armour`, `Shields`):
    /// tudo o que esta debaixo de `Armor` e arte de armadura. Ficam de fora as folhas `unused*`, que o proprio nome
    /// diz o que sao. A do Beerus FICA na lista (e a roupa do NPC Bills) e sai na tela de escolha -- ver
    /// `Core/Items/ArmaduraDeTech.cs`.
    /// ==========================================================================================
    /// </summary>
    public static List<string> Armaduras(string pastaSprites) =>
        [.. FolhasDeArmadura(pastaSprites).Select(f => Path.GetFileNameWithoutExtension(f))];

    private static readonly string[] ForaDasArmaduras = ["unused"];

    private static List<string> FolhasDeArmadura(string pastaSprites) =>
        Varrer(Path.Combine(pastaSprites, "Clothes", "Armor"), SearchOption.AllDirectories, ForaDasArmaduras, soOQueFicaDePe: true);

    /// <summary>
    /// O crivo comum das duas listas. Devolve os ARQUIVOS, em ordem de nome -- quem quer o nome tira do caminho, e
    /// quem escreve o catalogo escreve o caminho do proprio arquivo (ver <see cref="Escrever"/>).
    /// </summary>
    private static List<string> Varrer(string dir, SearchOption fundura, string[] fora, bool soOQueFicaDePe)
    {
        if (!Directory.Exists(dir)) return [];

        var saida = new List<string>();
        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string f in Directory.GetFiles(dir, "*.tres", fundura).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            string nome = Path.GetFileNameWithoutExtension(f);
            if (fora.Any(p => nome.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;

            // SE NAO SABE FICAR DE PE, NAO E ARMADURA. Vale a pose PARADA ou a de CAMINHADA -- muitas pecas so
            // trazem uma das duas. (O guarda-roupa nao passa por aqui desde 2026-10-12: o dono quer a pasta inteira.)
            if (soOQueFicaDePe)
            {
                string txt = File.ReadAllText(f);
                if (!txt.Contains("\"default_south\"", StringComparison.Ordinal)
                    && !txt.Contains("\"walk_south\"", StringComparison.Ordinal)) continue;
            }

            // UM NOME, UMA PECA: a mochila guarda a peca pelo NOME do arquivo (`RoupaGuardada`), e duas folhas com
            // o mesmo nome em subpastas diferentes seriam a mesma peca pra ela. Vale a primeira, em ordem de caminho.
            if (!nomes.Add(nome)) continue;

            saida.Add(f);
        }
        saida.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(
            Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b)));
        return saida;
    }

    /// <summary>O caminho `res://` de um arquivo varrido, relativo a raiz do repo (a pasta de onde a ferramenta roda).</summary>
    private static string Res(string arquivo) =>
        "res://" + Path.GetRelativePath(Directory.GetCurrentDirectory(), arquivo).Replace('\\', '/');

    // =====================================================================
    // RESOLUCAO CONTRA O DISCO
    // =====================================================================
    /// <summary>
    /// Indexa todo .tres convertido por nome em MINUSCULA -> caminho res:// real.
    /// E o que desarma a armadilha de caixa alta/baixa dos nomes escritos no DM.
    /// </summary>
    public static Dictionary<string, string> IndiceDeSprites(string pastaSprites)
    {
        var idx = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string f in Directory.GetFiles(pastaSprites, "*.tres", SearchOption.AllDirectories))
        {
            string chave = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
            string rel = Path.GetRelativePath(Directory.GetCurrentDirectory(), f).Replace('\\', '/');
            idx.TryAdd(chave, "res://" + rel);
        }
        return idx;
    }

    /// <summary>Acha o caminho real de um .dmi citado no DM. Nulo = o arquivo nao existe no port.</summary>
    public static string? Resolver(Dictionary<string, string> idx, string? dmi)
    {
        if (string.IsNullOrEmpty(dmi)) return null;
        string bruto = Path.GetFileNameWithoutExtension(dmi).ToLowerInvariant();
        return idx.GetValueOrDefault(bruto);
    }

    /// <summary>Escreve o catalogo que o jogo carrega em runtime.</summary>
    public static (int cabelos, int roupas, int armaduras, List<string> faltando) Escrever(
        string arquivoHairChoose, string pastaSprites, string saidaJson)
    {
        Dictionary<string, string> idx = IndiceDeSprites(pastaSprites);
        var faltando = new List<string>();

        var sb = new StringBuilder("{\n");

        // --- cabelos ---
        List<HairStyle> cabelos = Cabelos(arquivoHairChoose);
        sb.Append("  \"cabelos\": [\n");
        for (int i = 0; i < cabelos.Count; i++)
        {
            HairStyle h = cabelos[i];
            string? caminho = Resolver(idx, h.Sprite);
            if (h.Sprite != null && caminho == null) faltando.Add($"cabelo {h.Nome}: {h.Sprite}");
            sb.Append($"    {{ \"nome\": {Txt(h.Nome)}, \"sprite\": {Txt(caminho)} }}");
            sb.Append(i < cabelos.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("  ],\n");

        // --- corpos ---
        sb.Append("  \"corpos\": {\n");
        Dictionary<string, BodySet> corpos = Corpos();
        int c = 0;
        foreach ((string raca, BodySet b) in corpos)
        {
            string[] m = b.Masculino.Select(x => Resolver(idx, x) ?? Falta(faltando, raca, x)).Where(x => x != null).ToArray()!;
            string[] f = b.Feminino.Select(x => Resolver(idx, x) ?? Falta(faltando, raca, x)).Where(x => x != null).ToArray()!;
            sb.Append($"    {Txt(raca)}: {{ \"masculino\": [{string.Join(", ", m.Select(Txt))}], " +
                      $"\"feminino\": [{string.Join(", ", f.Select(Txt))}], " +
                      $"\"tons\": [{string.Join(", ", b.Tons.Select(Txt))}], " +
                      $"\"corLivre\": {(b.CorLivre ? "true" : "false")} }}");
            sb.Append(++c < corpos.Count ? ",\n" : "\n");
        }
        sb.Append("  },\n");

        // --- roupas ---
        // O CAMINHO SAI DO PROPRIO ARQUIVO VARRIDO, e nao do indice por nome: ha folha com o MESMO nome na raiz de
        // `Clothes` e dentro de `Armor` (a GinsDynastyArmorRed), e o indice por nome entregava a da raiz pras duas
        // listas -- a "armadura" do catalogo era a roupa do guarda-roupa.
        List<string> roupaPaths = [.. FolhasDoGuardaRoupa(pastaSprites).Select(Res)];
        sb.Append("  \"roupas\": [\n");
        for (int i = 0; i < roupaPaths.Count; i++)
            sb.Append($"    {Txt(roupaPaths[i])}").Append(i < roupaPaths.Count - 1 ? ",\n" : "\n");
        sb.Append("  ],\n");

        // --- armaduras: a pasta `Clothes/Armor`, que e EQUIPAMENTO e nao guarda-roupa (ver `Armaduras`) ---
        List<string> armaduraPaths = [.. FolhasDeArmadura(pastaSprites).Select(Res)];
        sb.Append("  \"armaduras\": [\n");
        for (int i = 0; i < armaduraPaths.Count; i++)
            sb.Append($"    {Txt(armaduraPaths[i])}").Append(i < armaduraPaths.Count - 1 ? ",\n" : "\n");
        sb.Append("  ],\n");

        // --- olhos: UM arquivo so, tingido por cor (o jogo tem exatamente isto) ---
        sb.Append($"  \"olhos\": {Txt(Resolver(idx, "Eyes_Black"))}\n");
        sb.Append("}\n");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(saidaJson))!);
        File.WriteAllText(saidaJson, sb.ToString(), new UTF8Encoding(false));
        return (cabelos.Count, roupaPaths.Count, armaduraPaths.Count, faltando);
    }

    private static string? Falta(List<string> lista, string raca, string arq)
    {
        lista.Add($"corpo {raca}: {arq}");
        return null;
    }

    private static string Txt(string? s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
