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
            Masculino = ["Majin", "Majin1"],
            Feminino = ["Female Majin", "female majin base (small)"],
            CorLivre = true,
        },
        ["Kai"] = new BodySet { Masculino = ["Konatsu"], Feminino = ["Alien 10"], CorLivre = true },
        ["Demon"] = new BodySet
        {
            Masculino = ["Base_Skully", "Demon - Form 1", "Demon4", "DemonForm1", "Satan"],
            Feminino = ["Base_Skully", "Demon - Form 1", "Demon4", "DemonForm1", "Satan"],
        },
    };

    /// <summary>Os tres tons aprovados de Saiyajin/Humano, por sexo.</summary>
    private static BodySet Humanoide() => new()
    {
        Masculino = ["NewPaleMale", "NewTanMale", "NewBlackMale"],
        Feminino = ["NewPaleFemale", "NewTanFemale", "NewBlackFemale"],
        Tons = ["Pele clara", "Pele morena", "Pele negra"],
    };

    /// <summary>
    /// Roupa CURADA. A pasta de roupas do jogo tem 223 arquivos, mas ela e o deposito de tudo
    /// que vira overlay de corpo -- inclusive olho, halo, rabo, potara e a pasta de armadura.
    /// Varrer a pasta inteira poria "olhos" na lista de camisas. Aqui fica so o que veste.
    ///
    /// SO A RAIZ: a pasta `Armor` e outra lista -- ver <see cref="Armaduras"/>.
    /// </summary>
    public static List<string> Roupas(string pastaSprites) =>
        Varrer(Path.Combine(pastaSprites, "Clothes"));

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
    /// Juntar as duas listas poria armadura de batalha na grade da criacao de personagem -- uma
    /// mudanca que o dono nao pediu. Separadas, o Saiyajin do mundo veste a dele
    /// (<see cref="Jandirus.Core.Npc.RoupaDeNpc"/>) e a tela do jogador continua igual.
    ///
    /// E ELA PRECISA EXISTIR NO CATALOGO de qualquer jeito: `VisualCatalog.Sanear` recusa caminho
    /// que nao esta no catalogo, e o servidor chama `Sanear` na aparencia do NPC. Sem esta lista,
    /// vestir um Saiyajin de armadura seria DESCARTADO EM SILENCIO -- ele nasceria pelado do mesmo
    /// jeito, so que agora com codigo que jura o contrario.
    /// ==========================================================================================
    /// </summary>
    public static List<string> Armaduras(string pastaSprites) =>
        Varrer(Path.Combine(pastaSprites, "Clothes", "Armor"));

    /// <summary>O crivo comum das duas listas: so a raiz da pasta, e so o que sabe ficar de pe.</summary>
    private static List<string> Varrer(string dir)
    {
        if (!Directory.Exists(dir)) return [];

        var fora = new[] { "Eyes_", "Halo", "Tail", "Potara", "potara", "Aura", "Scouter" };
        var saida = new List<string>();

        foreach (string f in Directory.GetFiles(dir, "*.tres"))
        {
            string nome = Path.GetFileNameWithoutExtension(f);
            if (fora.Any(p => nome.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;

            // SE NAO SABE FICAR DE PE, NAO E ROUPA. A pasta guarda tambem coisas que so
            // existem numa pose (o "Mega Buster" so tem quadros de ataque): vestir isso
            // deixaria o personagem sem a peca 90% do tempo. Vale a pose PARADA ou a de
            // CAMINHADA -- muitas pecas so trazem uma das duas.
            string txt = File.ReadAllText(f);
            if (!txt.Contains("\"default_south\"", StringComparison.Ordinal)
                && !txt.Contains("\"walk_south\"", StringComparison.Ordinal)) continue;

            saida.Add(nome);
        }
        saida.Sort(StringComparer.OrdinalIgnoreCase);
        return saida;
    }

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
        List<string> roupas = Roupas(pastaSprites);
        var roupaPaths = roupas.Select(r => Resolver(idx, r)).Where(x => x != null).ToList();
        sb.Append("  \"roupas\": [\n");
        for (int i = 0; i < roupaPaths.Count; i++)
            sb.Append($"    {Txt(roupaPaths[i])}").Append(i < roupaPaths.Count - 1 ? ",\n" : "\n");
        sb.Append("  ],\n");

        // --- armaduras: a pasta `Clothes/Armor`, que e EQUIPAMENTO e nao guarda-roupa (ver `Armaduras`) ---
        List<string> armaduras = Armaduras(pastaSprites);
        var armaduraPaths = armaduras.Select(a => Resolver(idx, a)).Where(x => x != null).ToList();
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
