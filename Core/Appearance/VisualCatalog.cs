using System.Text.Json;

namespace Jandirus.Core.Appearance;

/// <summary>Os corpos que uma raca pode usar.</summary>
public sealed class BodyOptions
{
    public List<string> Masculino = [];
    public List<string> Feminino = [];
    /// <summary>Nomes dos tons, quando a raca escolhe TOM em vez de arquivo.</summary>
    public List<string> Tons = [];
    /// <summary>A raca escolhe uma cor livre pro corpo (Majin, Kai).</summary>
    public bool CorLivre;

    public List<string> Para(string genero) =>
        genero == "Female" && Feminino.Count > 0 ? Feminino : Masculino;
}

/// <summary>
/// O catalogo de aparencia que o `Tools/AssetPipeline -- visual` extraiu do DM.
///
/// As DUAS pontas carregam este mesmo arquivo: o cliente pra montar a tela de criacao e
/// desenhar a previa, o servidor pra CONFERIR a ficha que chegou. Sem isso, "escolha so o
/// que existe" seria uma regra do cliente -- e regra que so o cliente conhece nao e regra.
/// </summary>
public sealed class VisualCatalog
{
    public List<(string Nome, string? Sprite)> Cabelos = [];
    public Dictionary<string, BodyOptions> Corpos = new(StringComparer.Ordinal);
    public List<string> Roupas = [];
    public string? Olhos;

    /// <summary>Corpo padrao pra raca que nao tem entrada propria (o fallback do `Skin()`).</summary>
    public const string CorpoPadraoM = "res://Assets/Sprites/Character Icons/NewPaleMale.tres";
    public const string CorpoPadraoF = "res://Assets/Sprites/Character Icons/NewPaleFemale.tres";

    /// <summary>
    /// Racas que NAO passam pelo passo de cabelo -- as mesmas seis do jogo. Nao e cosmetico:
    /// um Namekuseijin de cabelo espetado seria outra coisa.
    /// </summary>
    private static readonly string[] SemCabelo =
        ["Namekian", "Majin", "Shapeshifter", "Saibaman", "Icer", "BioAndroid"];

    public static bool TemCabelo(string raca) => Array.IndexOf(SemCabelo, raca) < 0;

    /// <summary>
    /// Saiyajin nao escolhe cor de cabelo: o cabelo dele JA e preto no sprite, com os realces
    /// desenhados. No BYOND isso era "forcar preto"; aqui e NAO TINGIR, que da o mesmo
    /// resultado sem apagar o desenho.
    /// </summary>
    public static bool CabeloNatural(string raca) => raca == "Saiyan";

    public BodyOptions CorposDe(string raca) => Corpos.GetValueOrDefault(raca) ?? new BodyOptions
    {
        Masculino = [CorpoPadraoM],
        Feminino = [CorpoPadraoF],
    };

    public bool TemCabeloChamado(string nome) => Cabelos.Any(c => c.Nome == nome);

    public string? SpriteDoCabelo(string nome) =>
        Cabelos.FirstOrDefault(c => c.Nome == nome).Sprite;

    public static VisualCatalog Parse(string json)
    {
        var cat = new VisualCatalog();
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement raiz = doc.RootElement;

        if (raiz.TryGetProperty("cabelos", out JsonElement cab))
            foreach (JsonElement c in cab.EnumerateArray())
            {
                string nome = c.GetProperty("nome").GetString() ?? "";
                JsonElement sp = c.GetProperty("sprite");
                cat.Cabelos.Add((nome, sp.ValueKind == JsonValueKind.Null ? null : sp.GetString()));
            }

        if (raiz.TryGetProperty("corpos", out JsonElement corp))
            foreach (JsonProperty p in corp.EnumerateObject())
            {
                var b = new BodyOptions { CorLivre = p.Value.GetProperty("corLivre").GetBoolean() };
                foreach (JsonElement s in p.Value.GetProperty("masculino").EnumerateArray())
                    b.Masculino.Add(s.GetString() ?? "");
                foreach (JsonElement s in p.Value.GetProperty("feminino").EnumerateArray())
                    b.Feminino.Add(s.GetString() ?? "");
                foreach (JsonElement s in p.Value.GetProperty("tons").EnumerateArray())
                    b.Tons.Add(s.GetString() ?? "");
                cat.Corpos[p.Name] = b;
            }

        if (raiz.TryGetProperty("roupas", out JsonElement rou))
            foreach (JsonElement s in rou.EnumerateArray())
                cat.Roupas.Add(s.GetString() ?? "");

        if (raiz.TryGetProperty("olhos", out JsonElement olh) && olh.ValueKind != JsonValueKind.Null)
            cat.Olhos = olh.GetString();

        return cat;
    }

    // =====================================================================
    // VALIDACAO -- a mesma dos dois lados
    // =====================================================================
    /// <summary>
    /// Confere a aparencia contra o catalogo e CORRIGE o que der, em vez de recusar tudo.
    /// Devolve o motivo quando algo era invalido (pro servidor registrar), ou vazio.
    ///
    /// A postura e deliberada: aparencia nao da vantagem nenhuma em jogo, entao um indice
    /// fora da faixa nao merece derrubar a conexao -- merece virar o valor padrao. O que NAO
    /// se aceita e sprite fora do catalogo, porque isso e o cliente inventando caminho.
    /// </summary>
    public string Sanear(Appearance ap, string raca, string genero)
    {
        var erros = new List<string>();

        BodyOptions corpos = CorposDe(raca);
        List<string> lista = corpos.Para(genero);
        if (ap.Corpo < 0 || ap.Corpo >= lista.Count)
        {
            if (ap.Corpo != 0) erros.Add("corpo fora da lista da raca");
            ap.Corpo = 0;
        }

        if (ap.Tom < 0 || ap.Tom >= Math.Max(corpos.Tons.Count, 1))
        {
            if (ap.Tom != 0) erros.Add("tom fora da lista");
            ap.Tom = 0;
        }

        if (!TemCabelo(raca))
        {
            ap.Cabelo = "Bald";
        }
        else if (!TemCabeloChamado(ap.Cabelo))
        {
            erros.Add($"estilo de cabelo desconhecido: {ap.Cabelo}");
            ap.Cabelo = "Bald";
        }

        if (CabeloNatural(raca)) ap.CorCabelo = null;
        if (!CorposDe(raca).CorLivre) ap.CorPele = null;   // so Majin/Kai escolhem cor de corpo

        // roupa: so o que esta no catalogo, e no maximo o teto do guarda-roupa
        //
        // A COR VIAJA JUNTO. O filtro e por CAMINHO -- duas pecas iguais com cores diferentes
        // continuam sendo a mesma peca, e vence a primeira --, mas a peca que passa leva a cor
        // que o jogador escolheu. Filtrar so o caminho e remontar sem a cor perderia a tintura
        // toda vez que alguem confirmasse a criacao.
        var limpo = new List<PecaDeRoupa>();
        foreach (PecaDeRoupa peca in ap.Roupa)
        {
            if (limpo.Count >= Appearance.MaxRoupa) { erros.Add("mais roupa que o guarda-roupa aguenta"); break; }
            if (!Roupas.Contains(peca.Caminho)) { erros.Add($"roupa fora do catalogo: {peca.Caminho}"); continue; }
            if (!limpo.Any(p => p.Caminho == peca.Caminho)) limpo.Add(peca);
        }
        // ESCREVE NO LUGAR, e nao troca a instancia: a tela de criacao guarda widgets ligados a
        // ESTA lista, e trocar o objeto por baixo deles deixava os botoes da grade afirmando que
        // uma peca esta vestida depois de o `Sanear` te-la tirado.
        ap.Roupa.Clear();
        ap.Roupa.AddRange(limpo);

        return string.Join("; ", erros);
    }

    /// <summary>
    /// O sprite do corpo, ja resolvido -- e o que a previa e o jogo desenham.
    ///
    /// Namekuseijin e caso a parte: o TOM manda no arquivo. Verde claro, verde e verde
    /// escuro sao o MESMO sprite com brilho diferente; albino e outro arquivo.
    /// </summary>
    public string CorpoSprite(Appearance ap, string raca, string genero)
    {
        // ============================ O FROST DEMON NAO TEM CORPO NO CATALOGO ============================
        // E de proposito, e nao um buraco: o corpo dele E a forma em que ele esta, e as formas sao
        // escolhidas na criacao (ver `Races.FormasDeFrost`). Sem esta saida ele caia no
        // `CorpoPadrao` la embaixo e o Freeza aparecia como um humano palido -- na previa da
        // criacao, no retrato do slot, e no mundo pra todo mundo.
        //
        // AQUI SEMPRE SAI A FORMA BASE. Trocar de forma em jogo e outro caminho (o combate decide
        // em que degrau o corpo esta e pede o sprite daquele indice); este metodo responde "como
        // este personagem se parece", e a resposta e o corpo em que ele anda.
        // =================================================================================================
        if (Races.FormasDeFrost.EhFrost(raca) && ap.FormasDeFrost.Count > 0)
            return Races.FormasDeFrost.Caminho(ap.FormasDeFrost[0]);

        List<string> lista = CorposDe(raca).Para(genero);
        if (lista.Count == 0) return genero == "Female" ? CorpoPadraoF : CorpoPadraoM;

        int i = raca == "Namekian"
            ? (ap.Tom >= 3 ? 1 : 0)                       // 3 = albino, o resto e o corpo verde
            : Math.Clamp(ap.Corpo, 0, lista.Count - 1);

        return lista[Math.Clamp(i, 0, lista.Count - 1)];
    }

    /// <summary>
    /// Como tingir o corpo. Duas coisas diferentes:
    ///
    ///   Soma   -- a cor livre de Majin/Kai, SOMADA como o `ICON_ADD` do jogo. Nulo = sem cor.
    ///   Brilho -- multiplicador acima/abaixo de 1, que e como o Namekuseijin faz os tons de
    ///             verde (o jogo soma/subtrai um cinza do MESMO sprite). Sem isso os quatro
    ///             tons de Namekuseijin sairiam todos iguais.
    /// </summary>
    public (Rgb? Soma, float Brilho) TintaDoCorpo(Appearance ap, string raca)
    {
        BodyOptions b = CorposDe(raca);
        if (b.CorLivre) return (ap.CorPele, 1f);
        if (raca != "Namekian") return (null, 1f);

        return ap.Tom switch
        {
            0 => (null, 1.18f),   // verde claro
            2 => (null, 0.82f),   // verde escuro
            _ => (null, 1f),      // verde normal e albino: o proprio arquivo
        };
    }
}
