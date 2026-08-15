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

    /// <summary>
    /// ============================ A ARMADURA E OUTRA LISTA, E DE PROPOSITO ============================
    /// Ela NAO entra no guarda-roupa: a grade da criacao de personagem se monta da <see cref="Roupas"/>,
    /// e no jogo original a armadura tambem nao esta la (o `ClothingChoice()` de ClothesChoose.dm:32+
    /// nao lista nenhuma; armadura e EQUIPAMENTO, com seletor de estilo proprio em Armor.dm:100-123).
    ///
    /// Ela existe aqui por uma razao so, e e a que faz a roupa de NPC funcionar: o <see cref="Sanear"/>
    /// recusa todo caminho fora do catalogo, e e ele que confere a aparencia do NPC antes de ela ir pro
    /// fio. Sem esta lista, vestir um Saiyajin com 'Armor 8' seria descartado EM SILENCIO -- ele nasceria
    /// pelado com codigo jurando que o vestiu. Ver `DmAppearanceScanner.Armaduras`.
    /// ==============================================================================================
    /// </summary>
    public List<string> Armaduras = [];

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

    // =====================================================================
    // O NOME DO .dmi -> O CAMINHO DO .tres
    // =====================================================================
    private Dictionary<string, string>? _porNome;

    /// <summary>
    /// ACHA UMA PECA PELO NOME DO ARQUIVO -- 'Armor 8', 'Clothes_GiTop', 'ClothesNamekJacket'.
    /// Nulo = a arte nao existe neste port.
    ///
    /// ============================ POR QUE PELO NOME, E NAO PELO CAMINHO ============================
    /// Porque a fonte da tabela e o DM, e la a peca e citada por ARQUIVO ('Armor 8.dmi'). Escrever o
    /// `res://Assets/Sprites/Clothes/Armor/Armor 8.tres` na tabela seria transcrever um caminho de
    /// Godot dentro do Core -- que e o que a regra da casa proibe -- e envelheceria calado no dia em
    /// que a pasta mudasse: o caminho errado nao resolve, e o NPC volta a nascer pelado sem que
    /// ninguem saiba por que.
    ///
    /// Pelo nome, mover a pasta nao quebra nada, e o que quebra de verdade (a arte SUMIR) devolve
    /// nulo, que e o unico jeito de a falta ser DENUNCIADA em vez de silenciosa.
    ///
    /// CAIXA ALTA/BAIXA NAO IMPORTA, pela mesma armadilha que o extrator ja documenta: o proprio jogo
    /// escreve o mesmo arquivo de dois jeitos ('HairFemalePonytail' x 'HairFemalePonyTail').
    /// =========================================================================================
    /// </summary>
    public string? Peca(string nomeDoArquivo)
    {
        if (string.IsNullOrEmpty(nomeDoArquivo)) return null;
        _porNome ??= Indexar();
        return _porNome.GetValueOrDefault(Base(nomeDoArquivo));
    }

    private Dictionary<string, string> Indexar()
    {
        var idx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // ROUPA PRIMEIRO, ARMADURA DEPOIS: se um nome existir nas duas listas vence o guarda-roupa,
        // que e a lista que o jogador tambem ve. Hoje nao ha colisao; a ordem e pra quando houver.
        foreach (string c in Roupas) idx.TryAdd(Base(c), c);
        foreach (string c in Armaduras) idx.TryAdd(Base(c), c);
        return idx;
    }

    /// <summary>O nome do arquivo, sem pasta e sem extensao. Serve pro 'x.dmi' do DM e pro 'res://.../x.tres'.</summary>
    private static string Base(string caminho)
    {
        int barra = caminho.LastIndexOf('/');
        string nome = barra >= 0 ? caminho[(barra + 1)..] : caminho;
        int ponto = nome.LastIndexOf('.');
        return ponto > 0 ? nome[..ponto] : nome;
    }

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

        // AUSENTE E VAZIO, e nao erro: um `visual.json` gerado antes desta secao continua carregando.
        // O que ele produz e NPC sem armadura -- ver `RoupaDeNpc`, que denuncia a peca que nao resolve.
        if (raiz.TryGetProperty("armaduras", out JsonElement arm))
            foreach (JsonElement s in arm.EnumerateArray())
                cat.Armaduras.Add(s.GetString() ?? "");

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

        // ============================ O BIO-ANDROIDE TEM LISTA PROPRIA, E ELA E O DEGRAU ============================
        // `Corpos` dele nao esta no `visual.json` (o extrator nao tem passo pra essa raca -- ver
        // `Races.BioAndroids.Corpos`), entao o `CorposDe` devolveria a lista PADRAO de um item so e
        // este saneamento zeraria o indice: um bio na forma perfeita voltaria a larva TODA VEZ que
        // alguem chamasse `Sanear` -- que e o login, a criacao e cada troca de aparencia.
        //
        // Foi o mesmo modo de falha que o audit apontou no Androide ("um Humano com corpo 2 cai pra
        // 0" ao virar `Race = "Android"`); aqui ele seria pior, porque apagaria PROGRESSAO e nao
        // gosto.
        // =========================================================================================================
        int quantos = Races.BioAndroids.EhBio(raca) ? Races.BioAndroids.Corpos.Length : lista.Count;
        if (ap.Corpo < 0 || ap.Corpo >= quantos)
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
            // ARMADURA VALE TAMBEM. Nao e frouxidao: `Armaduras` e catalogo extraido do disco pelo
            // mesmo passo que a roupa, so que numa lista que a grade da criacao nao oferece. O que
            // esta linha continua recusando e o que sempre recusou -- caminho que nao veio de
            // catalogo nenhum, ou seja o cliente inventando arquivo.
            if (!Roupas.Contains(peca.Caminho) && !Armaduras.Contains(peca.Caminho))
            { erros.Add($"roupa fora do catalogo: {peca.Caminho}"); continue; }
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

        // ============================ E O BIO-ANDROIDE PELO MESMO ARGUMENTO, COM OUTRO INDICE ============================
        // O corpo dele E o degrau em que ele esta (`DNALabs.dm` troca `icon` **e** `oicon` a cada
        // evolucao), e a lista dos quatro nao esta no catalogo pelo motivo escrito em
        // `Races.BioAndroids.Corpos`. O indice sai de `ap.Corpo` -- o MESMO campo que toda raca usa
        // pra dizer qual corpo e o dela --, e e isso que faz a evolucao viajar pela rede sem canal
        // novo: o servidor escreve o indice, `TrocarAparencias` reapresenta o boneco, e todo mundo
        // na zona ve a criatura mudar.
        //
        // AQUI SAI O CORPO DE REPOUSO, como no ramo do Frost logo acima. A Super Perfeita e forma e
        // sai por `Client.CorposDeForma`.
        // =============================================================================================================
        if (Races.BioAndroids.EhBio(raca))
            return Races.BioAndroids.Corpos[
                Math.Clamp(ap.Corpo, 0, Races.BioAndroids.Corpos.Length - 1)];

        List<string> lista = CorposDe(raca).Para(genero);
        if (lista.Count == 0) return genero == "Female" ? CorpoPadraoF : CorpoPadraoM;

        int i = raca == "Namekian"
            ? (ap.Tom >= 3 ? 1 : 0)                       // 3 = albino, o resto e o corpo verde
            : Math.Clamp(ap.Corpo, 0, lista.Count - 1);

        return lista[Math.Clamp(i, 0, lista.Count - 1)];
    }

    /// <summary>
    /// O CORPO DE REPOUSO DESTA FICHA DESENHA UM ROSTO? -- a irma da
    /// <see cref="Forms.Catalogo.FolhaTemRosto"/>, do outro eixo.
    ///
    /// ============================ SAO DOIS EIXOS, E SO DOIS ============================
    /// Uma folha de corpo chega ao boneco por exatamente dois caminhos neste port: o corpo da RACA
    /// (<see cref="CorpoSprite"/>, indexado por `Appearance.Corpo`) e o corpo da FORMA
    /// (`CorpoDeForma`, resolvido por `Client.CorposDeForma`). Este metodo responde pelo primeiro, o
    /// `Catalogo.FolhaTemRosto` pelo segundo, e quem cruza os dois e o `CharacterVisual.Escondida` --
    /// que ja e o unico dono da visibilidade de camada.
    ///
    /// HOJE SO O BIO-ANDROIDE RESPONDE "NAO", e ele responde pelo mesmo lugar que ja e dono da lista
    /// de corpos dele (<see cref="Races.BioAndroids.Corpos"/>): a larva e uma barata. Uma raca nova
    /// com um corpo que nao seja gente entra aqui por um `else if` ao lado -- e nao por uma lista de
    /// caminhos `res://` escrita a mao, que e o que apodrece na primeira vez que a pasta de arte
    /// muda de nome.
    ///
    /// **NAO OLHA A FORMA VESTIDA**, de proposito: quem esta em cima manda. Um bio na Super Perfeita
    /// veste a `Bio Android 4`, que tem rosto, e e ela que decide -- mesmo que o corpo por baixo nao
    /// tivesse. (Nao acontece: larva nao alcanca forma nenhuma. Mas a regra nao depende disso.)
    /// ==================================================================================
    /// </summary>
    public static bool CorpoTemRosto(Appearance ap, string raca) =>
        !Races.BioAndroids.EhBio(raca) || Races.BioAndroids.CorpoTemRosto(ap.Corpo);

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
