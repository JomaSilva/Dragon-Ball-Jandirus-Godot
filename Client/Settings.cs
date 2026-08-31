using System.Text.Json;
using Godot;

namespace Jandirus.Client;

/// <summary>
/// As preferencias DESTA MAQUINA: janela, zoom, volume e TECLAS.
///
/// Ficam em `user://config.json`, junto dos perfis -- nada disso e do servidor, e nada disso
/// afeta o jogo dos outros. Zoom e o unico que chega perto: ver mais longe e vantagem, entao
/// ele tem TETO (ver <see cref="ZoomMax"/>).
///
/// ============================ POR QUE A TECLA MORA AQUI E NAO NO PERFIL ============================
/// Porque o dedo e da PESSOA e nao do personagem. `Profiles.cs` guarda conta por servidor: uma
/// ligacao gravada la mudaria ao trocar de servidor, e sumiria ao apagar o personagem. Quem liga o
/// Kamehameha ao Q quer o Q ligado no Saiyajin, no Namekuseijin e no servidor do amigo.
///
/// A consequencia esta escrita: uma tecla ligada a uma acao que ESTE personagem nao tem continua
/// gravada, apagada na tela, e nao manda pacote nenhum -- ver `Atalhos.Disparar`. Apagar a ligacao
/// faria quem volta pro Namekuseijin perder o Regenerar pra sempre.
/// ==============================================================================================
/// </summary>
public sealed class Settings
{
    private const string Arquivo = "user://config.json";

    /// <summary>Uma acao do jogo que o jogador tirou da tecla de fabrica. Ver `Teclas`.</summary>
    public sealed class LigacaoDeTecla
    {
        public string Acao = "";
        /// <summary>O nome do `Godot.Key` (`"J"`). Vazio = a acao ficou SEM tecla.</summary>
        public string Tecla = "";
    }

    /// <summary>Uma tecla que o jogador ligou a um verbo ou a uma transformacao. Ver `Teclas.Atalho`.</summary>
    public sealed class AtalhoGravado
    {
        public string Tecla = "";
        public string Tipo = "";
        public string Chave = "";
        public string Rotulo = "";
    }

    // ============================ SAO LISTAS DE OBJETO, E NAO UM DICIONARIO ============================
    // `Dictionary<Key,string>` teria sido menos codigo e um erro: o `System.Text.Json` serializa
    // chave de enum como NUMERO, e o arquivo passaria a depender da ordem interna do enum do Godot
    // -- uma atualizacao do motor que insira uma tecla no meio remapearia as ligacoes de todo mundo,
    // caladamente. Este projeto ja perdeu dado gravado por causa da FORMA do JSON (as cores de
    // roupa); aqui o campo e o nome da tecla em texto, que so muda se o Godot renomear a tecla.
    // ==============================================================================================
    public List<LigacaoDeTecla> LigacoesDeTecla = [];
    public List<AtalhoGravado> AtalhosDeTecla = [];

    // --- video ---
    //
    // ============================ O QUE A RESOLUCAO SIGNIFICA (leia antes de mexer) ============================
    // Ela deixou de ser "o tamanho da janela" e passou a ser **a base de desenho**: o
    // `Aplicar` escreve estes dois numeros no `ContentScaleSize` da raiz, e o motor estica dali ate
    // a janela. O `project.godot` declara o par que faz isso funcionar:
    //
    //     window/stretch/mode   = "canvas_items"
    //     window/stretch/aspect = "expand"
    //
    // ANTES o `mode` era "disabled" (ausente = padrao), e com ele o `aspect="expand"` que ja estava
    // escrito no arquivo era LETRA MORTA: o viewport seguia a janela 1:1 e a escala era sempre 1,00.
    // Dai vinham os dois defeitos que o dono descreveu como um so:
    //   I.  em tela cheia a resolucao escolhida era IGNORADA (o `if (!TelaCheia)` pulava o unico
    //       lugar que usava o numero);
    //   II. escolher uma resolucao DESLIGAVA a tela cheia (`_cfg.TelaCheia = false` no `PauseMenu`).
    // Juntos, "tela cheia com resolucao menor" era um estado inalcancavel -- o que sobrava era uma
    // JANELA de 1280x720 no meio de um desktop de 1920x1080, ou seja *"o jogo n cobre a tela toda"*.
    //
    // ---- O CUSTO ACEITO: nem barra preta, nem distorcao -- e sim MAIS MUNDO NO EIXO LARGO ----
    // As alternativas de `aspect`, com o custo de cada uma:
    //   * `keep`  / `keep_width` / `keep_height` -- sem distorcao, mas com BARRA PRETA. E exatamente
    //     o defeito que o dono pediu pra tirar. Descartadas.
    //   * `ignore` -- preenche 100% DISTORCENDO. No monitor dele a distorcao seria de 0,19%
    //     (invisivel), mas num 16:10 ou 21:9 o boneco sai ovalado. Escolha fragil, descartada.
    //   * `expand` -- **a escolhida**. Escala pelo eixo mais apertado e ALARGA a base no outro ate
    //     encher. Sem barra e sem distorcao; o preco e que a proporcao da JANELA decide quanto de
    //     mundo entra no eixo largo.
    //
    // ---- E ISSO E REGRA DE JOGO, NAO SO DE JANELA ----
    // Num jogo de luta em rede **ver mais longe que o outro e vantagem de verdade** -- e a mesma
    // razao pela qual o `ZoomMin` existe. Entao, com todas as letras:
    //
    //   * o CAMPO DE VISAO passa a sair da resolucao escolhida, e nao do tamanho da janela. Base
    //     menor = escala maior = **ve MENOS mundo** (e tudo maior). Base maior = ve mais.
    //   * o TETO NAO SUBIU: a lista para na nativa do monitor (tela cheia) ou na area util
    //     (janela) -- ver `TetoDeResolucao`. Quem escolhe o maximo ve exatamente o que ja via antes
    //     desta mudanca; ninguem passa a ver mais do que via.
    //   * o `expand` acrescenta so o que a proporcao da janela pedir. No monitor do dono
    //     (1920x1082 de viewport contra uma base 1280x720) sao 1,3 linha de mundo -- desprezivel.
    //     Numa tela 21:9 seria bem mais, e continua sendo o preco de nao ter barra preta.
    //   * ARRASTAR A BORDA DA JANELA nao mostra mais mundo: ela so estica o mesmo desenho. Antes
    //     mostrava. Quem quer mais campo de visao usa a lista de resolucoes, que tem teto.
    // ==========================================================================================
    public int LarguraJanela = 1280;
    public int AlturaJanela = 720;
    public bool TelaCheia;

    /// <summary>
    /// Ampliacao da camera. INTEIRO de proposito: em arte de pixel um zoom quebrado mapeia
    /// texel em pixel de tela de forma irregular e a imagem cintila andando.
    /// </summary>
    public int Zoom = 3;

    /// <summary>
    /// QUALIDADE GRAFICA: 0 baixo, 1 medio, 2 alto. Hoje ela decide UMA coisa -- o filtro da
    /// sombra das luzes --, e por isso o nome e generico de proposito: e o lugar onde o proximo
    /// ajuste de custo visual entra sem virar mais uma opcao solta na tela.
    ///
    ///   baixo  sem filtro  -- a sombra sai com a borda dura, e e a mais barata
    ///   medio  PCF5        -- cinco amostras: suaviza sem pesar
    ///   alto   PCF13       -- treze amostras, a borda mais macia que o Godot 2D oferece
    ///
    /// PADRAO ALTO: o custo de PCF13 e por LUZ COM SOMBRA na tela, e o cenario tem poucas
    /// (fogueira, tocha, lava). Quem precisar baixar tem a opcao; comecar no feio pra economizar
    /// o que nao esta caro seria escolher pelo jogador.
    /// </summary>
    public int Grafico = 2;

    public const int GraficoBaixo = 0, GraficoMedio = 1, GraficoAlto = 2;

    /// <summary>O filtro que a qualidade atual pede. E o unico lugar que traduz o numero.</summary>
    public Godot.Light2D.ShadowFilterEnum FiltroDeSombra => Grafico switch
    {
        GraficoBaixo => Godot.Light2D.ShadowFilterEnum.None,
        GraficoMedio => Godot.Light2D.ShadowFilterEnum.Pcf5,
        _ => Godot.Light2D.ShadowFilterEnum.Pcf13,
    };

    /// <summary>
    /// QUANTOS ATAQUES DE KI PODEM LANCAR LUZ AO MESMO TEMPO. Ver <see cref="LuzDeKi"/>.
    ///
    /// ============================ POR QUE ISTO PRECISA DE TETO E A FOGUEIRA NAO ============================
    /// A luz do cenario e contada pelo MAPA: 125 fontes nos 40 mapas, 25 no pior deles (Vegeta), e
    /// esse numero so muda quando alguem edita um `.luz`. A luz de ki e contada pela BRIGA -- o teto
    /// de tiros de uma zona e 256 e o cliente desenha todos. Sem um teto aqui, uma troca de rajadas
    /// entre seis lutadores decidiria o quadro na maquina do jogador.
    ///
    /// Ele e ESTATICO porque quem pergunta e um node de tiro que pode nascer aos montes: ler
    /// `Boot.Config` seria a resposta certa pela porta cara. Quem escreve e o <see cref="Aplicar"/>,
    /// uma vez, como o resto da configuracao.
    ///
    /// ============================ E O TETO ALTO NAO SAIU DO MILISSEGUNDO ============================
    /// A bancada (`--diagluzdeki`, familia 4) mediu: 64 luzes de ki numa tela custam o MESMO que 8,
    /// dentro do ruido. Luz 2D sem sombra e barata, e concluir dali que o teto podia ser alto seria o
    /// erro -- porque a familia 6 fotografou o motivo do empate: **o renderizador de canvas do Godot
    /// compoe ate ~16 luzes por PEDACO de cenario e descarta o resto calado**. Das 64 acesas sobre um
    /// chao de um pedaco so, 15 chegaram ao chao e 49 sumiram sem erro, sem aviso, sem excecao.
    ///
    /// Ou seja: nao ha o que comprar acima disso. E o teto fica ABAIXO do muro de proposito, porque
    /// o orcamento nao e so do ki -- a fogueira do cenario e a aura de cada corpo aceso disputam as
    /// MESMAS vagas do mesmo pedaco. Um teto colado nos 16 faria a luz do tiro apagar a da fogueira
    /// ao lado dela, que e o defeito que ninguem ia saber explicar.
    /// ==========================================================================================
    /// </summary>
    public static int LuzesDeKi { get; private set; } = 12;

    /// <summary>O orcamento de luz de ki que esta qualidade pede. O unico lugar que traduz o numero.</summary>
    public int OrcamentoDeLuzDeKi => Grafico switch
    {
        GraficoBaixo => 4,
        GraficoMedio => 8,
        _ => 12,
    };

    /// <summary>Troca o orcamento. SO PRA BANCADA -- em jogo quem manda e a qualidade grafica.</summary>
    public static void LuzesDeKiDeTeste(int n) => LuzesDeKi = Math.Max(0, n);

    /// <summary>
    /// Teto do zoom-out. Diminuir o zoom mostra MAIS mundo, e enxergar mais longe que os
    /// outros e vantagem de verdade num jogo de luta -- por isso o minimo nao e livre.
    /// </summary>
    public const int ZoomMin = 2;
    public const int ZoomMax = 6;

    // --- audio (0 a 1) ---
    public float VolumeGeral = 0.8f;
    public float VolumeMusica = 0.6f;
    public float VolumeEfeitos = 0.9f;
    public float VolumeAmbiente = 0.5f;

    /// <summary>
    /// A voz das outras pessoas. Comeca ALTA (0,9): quem ligou a voz ligou pra ouvir, e uma voz mais
    /// baixa que o vento so faria a pessoa achar que o sistema nao funciona.
    /// </summary>
    public float VolumeVoz = 0.9f;

    // --- voz ---
    /// <summary>
    /// O MICROFONE ESTA LIGADO?
    ///
    /// ============================ **FALSO POR PADRAO**, E ISSO NAO E CONSERVADORISMO ============================
    /// Microfone e a unica coisa deste jogo que capta o quarto de uma pessoa. Um sistema de voz que
    /// nasce ligado transforma "instalei um jogo" em "abri o microfone", e ninguem tomou essa decisao.
    ///
    /// Enquanto isto for falso o `Microfone` **nao cria o `AudioStreamPlayer` do microfone**: nao ha
    /// captura, nao ha codificador, nao ha pacote. Nao e um `if` no envio -- e o aparelho nao existir.
    ///
    /// OUVIR NAO DEPENDE DISTO. Quem nao quer falar continua ouvindo os outros, que e o que a maioria
    /// vai querer; pra nao ouvir tambem existe o <see cref="VolumeVoz"/> em zero.
    /// ==========================================================================================================
    /// </summary>
    public bool VozLigada;

    /// <summary>
    /// APERTA-PRA-FALAR (verdadeiro) ou MICROFONE ABERTO (falso).
    ///
    /// **O padrao e aperta-pra-falar** -- foi o que o dono pediu ao dizer *"ao apertar V"*, e e o unico
    /// modo em que a pessoa sabe, sem olhar pra tela, se o quarto dela esta indo pra rede: o dedo esta
    /// na tecla ou nao esta.
    ///
    /// O modo aberto existe pra quem quer -- e ele nao e microfone LIVRE: ele ainda depende de
    /// <see cref="VozLigada"/>, e ainda so manda quadro acima do <see cref="LimiarDeVoz"/>.
    /// </summary>
    public bool VozApertarParaFalar = true;

    /// <summary>
    /// QUAO ALTO PRECISA SER PRA CONTAR COMO VOZ (amplitude de pico, 0 a 1).
    ///
    /// Vale nos DOIS modos, e no de apertar tambem: quadro de silencio codificado e mandado e banda
    /// gasta pra transmitir o ar de um quarto vazio. 0,02 e baixo o bastante pra passar uma fala
    /// sussurrada e alto o bastante pra segurar o chiado de fundo de um microfone barato.
    /// </summary>
    public float LimiarDeVoz = 0.02f;

    /// <summary>
    /// O microfone escolhido, pelo nome que o `AudioServer.GetInputDeviceList()` da. Vazio = o padrao
    /// do sistema. Nome e nao indice: a lista muda de ordem quando alguem pluga um fone.
    /// </summary>
    public string DispositivoDeVoz = "";

    private static readonly JsonSerializerOptions Opcoes = new() { IncludeFields = true, WriteIndented = true };

    public static Settings Carregar()
    {
        if (!Godot.FileAccess.FileExists(Arquivo)) return new Settings();
        try
        {
            return JsonSerializer.Deserialize<Settings>(
                Godot.FileAccess.GetFileAsString(Arquivo), Opcoes) ?? new Settings();
        }
        catch (Exception e)
        {
            GD.PushWarning($"[config] ilegivel, usando o padrao: {e.Message}");
            return new Settings();
        }
    }

    public void Gravar()
    {
        using Godot.FileAccess? f = Godot.FileAccess.Open(Arquivo, Godot.FileAccess.ModeFlags.Write);
        if (f == null) { GD.PushWarning("[config] nao consegui gravar"); return; }
        f.StoreString(JsonSerializer.Serialize(this, Opcoes));
    }

    /// <summary>Poe a janela e o audio no estado gravado. Chamado no boot e a cada mudanca.</summary>
    public void Aplicar()
    {
        Zoom = Math.Clamp(Zoom, ZoomMin, ZoomMax);

        // O ORCAMENTO DE LUZ DE KI. Aqui e nao na `AplicarQualidade` porque este e o caminho que o
        // boot E o menu ja percorrem -- ver `PauseMenu`, que chama `AplicarEGravar` ao trocar a
        // qualidade. Quem ja esta aceso mantem a vaga ate morrer, e e o certo: um tiro vive
        // segundos, e apagar luz na cara de quem acabou de mexer no controle seria pior que esperar.
        LuzesDeKi = OrcamentoDeLuzDeKi;

        // A MOLDURA SO SE MEDE COM A JANELA VESTIDA -- ver <see cref="Moldura"/>. Mede-se ANTES de
        // trocar de modo (aproveitando o estado em que a janela ja esta) e de novo DEPOIS, se o
        // destino for janela: ai a medida e a de verdade, e nao a lembrada.
        MedirMoldura();

        DisplayServer.WindowSetMode(TelaCheia
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);

        if (!TelaCheia) MedirMoldura();

        // ============================ O TETO VALE SEMPRE, E NAO SO NA HORA DE MONTAR A LISTA ============================
        // Esta e a REGRA que resolve o caso chato do L3: trocar de modo, trocar de monitor, ou abrir
        // um `config.json` que veio de outra maquina, com uma resolucao ja escolhida que ali nao
        // cabe. Uma lista filtrada so na abertura da tela nao resolveria isso -- ela e o CARDAPIO, e
        // cardapio nao trava nada; quem trava e este corte, que roda em TODA aplicacao.
        //
        // Ja aconteceu neste projeto de um calculo feito uma vez so (a caixa de apagar personagem,
        // que centrava na abertura e saia do centro quando a resolucao mudava depois). Aqui a conta
        // e refeita a cada `Aplicar`, que e o unico momento em que a janela muda de forma.
        // ==========================================================================================
        Vector2I teto = TetoDeResolucao(TelaCheia);
        LarguraJanela = Math.Clamp(LarguraJanela, 320, teto.X);
        AlturaJanela = Math.Clamp(AlturaJanela, 240, teto.Y);

        // ============================ A RESOLUCAO ESCOLHIDA E A BASE DE DESENHO ============================
        // Ver o bloco "O QUE A RESOLUCAO SIGNIFICA" la em cima. E ESTA linha que faz a resolucao
        // valer alguma coisa em TELA CHEIA -- antes o `if (!TelaCheia)` abaixo pulava o unico lugar
        // que usava o numero, e tela cheia com resolucao menor era um estado inalcancavel.
        //
        // Em modo JANELA a base e igual ao tamanho da janela, entao a escala da exatamente 1,00 e o
        // desenho sai idêntico ao de antes desta mudanca -- de proposito: o pedido do dono e sobre
        // tela cheia, e mexer no que ja estava certo seria trocar um defeito por outro.
        if (Engine.GetMainLoop() is SceneTree { Root: { } raiz })
            raiz.ContentScaleSize = new Vector2I(LarguraJanela, AlturaJanela);

        if (!TelaCheia)
        {
            var tam = new Vector2I(LarguraJanela, AlturaJanela);
            DisplayServer.WindowSetSize(tam);
            Recentrar(tam);
        }

        AudioDirector.Instance?.AplicarVolumes(this);
    }

    /// <summary>
    /// PoE A JANELA NO MEIO DA AREA UTIL **DO MONITOR EM QUE ELA ESTA**.
    ///
    /// ============================ O BUG QUE ESTAVA AQUI E ARRASTAVA A JANELA DE MONITOR ============================
    /// A conta era `(ScreenGetSize() - tamanho) / 2`. Duas coisas erradas na mesma linha:
    ///
    ///   1. `WindowSetPosition` recebe coordenada do DESKTOP INTEIRO (os dois monitores lado a lado),
    ///      e a conta nunca somava a origem do monitor. Numa janela de 1920x1080 num monitor de
    ///      1920x1080 o resultado dava (0,0) -- a quina do monitor PRIMARIO. Era por isso que toda
    ///      bancada lancada com `--position 1920,0` voltava sozinha pro monitor de trabalho do dono.
    ///   2. Nao descontava a MOLDURA. A barra de titulo mora ACIMA do cliente, entao centrar o
    ///      cliente deixa os 31 px de titulo fora da tela -- janela que nao da pra arrastar nem
    ///      fechar, so Alt+F4. Medido: cliente em y=0 e moldura em y=-31.
    ///
    /// Agora quem manda e o `ScreenGetUsableRect` do monitor ATUAL (ele ja desconta a barra de
    /// tarefas) e o que se centra e a janela COM a moldura.
    /// ==========================================================================================
    /// </summary>
    private static void Recentrar(Vector2I tam)
    {
        int tela = DisplayServer.WindowGetCurrentScreen();
        Rect2I util = DisplayServer.ScreenGetUsableRect(tela);
        if (util.Size.X <= 0 || util.Size.Y <= 0) return;   // headless: nao ha o que centrar

        // A MOLDURA NAO E SIMETRICA: no Windows sao 8 px de borda de cada lado e de baixo, e o resto
        // da altura e barra de titulo (medido: 16 de largura, 39 de altura -> 8 e 31). A conta
        // deriva isso da largura em vez de cravar 31, porque o tema do sistema muda esses numeros.
        Vector2I m = Moldura;
        var borda = new Vector2I(m.X / 2, m.Y - m.X / 2);

        Vector2I canto = util.Position + (util.Size - (tam + m)) / 2 + borda;
        DisplayServer.WindowSetPosition(new Vector2I(
            Math.Max(canto.X, util.Position.X + borda.X),
            Math.Max(canto.Y, util.Position.Y + borda.Y)));
    }

    // =====================================================================
    // O QUE CABE NESTA TELA, AGORA
    // =====================================================================
    private static Vector2I _moldura;

    /// <summary>
    /// QUANTO A MOLDURA DA JANELA COME, em pixels (largura, altura). LEMBRADA e nao recalculada na
    /// hora: em tela cheia nao ha moldura pra medir (o sistema devolve zero), e e justamente em tela
    /// cheia que a tela de opcoes precisa saber quanto vai sobrar se o jogador voltar pra janela.
    ///
    /// Zero enquanto nunca houve uma janela vestida (headless, servidor dedicado) -- e ai o teto de
    /// modo janela vira a area util inteira, que e o certo: sem moldura, nada e descontado.
    /// </summary>
    public static Vector2I Moldura => _moldura;

    private static void MedirMoldura()
    {
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) return;
        Vector2I d = DisplayServer.WindowGetSizeWithDecorations() - DisplayServer.WindowGetSize();
        if (d.X >= 0 && d.Y > 0) _moldura = d;
    }

    /// <summary>
    /// O MAIOR CLIENTE QUE CABE NESTE MODO, NESTA TELA, AGORA.
    ///
    ///   * TELA CHEIA -- o monitor inteiro. Nao ha moldura nem barra de tarefas na frente, entao a
    ///     nativa continua na lista (a restricao do dono e do modo JANELA, e so dele).
    ///   * JANELA -- a area util do monitor (o `ScreenGetUsableRect` ja tira a barra de tarefas)
    ///     MENOS a moldura. Na maquina do dono: 1920x1032 de util - 16x39 de moldura = 1904x993.
    ///
    /// **NADA AQUI E CRAVADO**, e o dono tem razao ao dizer que isso varia: os 48 px da barra de
    /// tarefas sao da posicao/tamanho/auto-esconder DELE, e os 39 px da moldura sao do tema do
    /// Windows. Dois monitores da mesma casa ja dao numeros diferentes.
    /// </summary>
    public static Vector2I TetoDeResolucao(bool telaCheia)
    {
        int tela = DisplayServer.WindowGetCurrentScreen();
        Vector2I monitor = DisplayServer.ScreenGetSize(tela);
        if (monitor.X <= 0 || monitor.Y <= 0) return new Vector2I(1920, 1080);   // headless

        if (telaCheia) return monitor;

        Vector2I util = DisplayServer.ScreenGetUsableRect(tela).Size;
        if (util.X <= 0 || util.Y <= 0) util = monitor;
        return new Vector2I(Math.Max(320, util.X - _moldura.X),
                            Math.Max(240, util.Y - _moldura.Y));
    }

    /// <summary>
    /// A ESCADA DE ONDE AS LISTAS SAEM. Ela NAO e o que a tela oferece -- ver
    /// <see cref="ResolucoesPara"/>, que corta pelo que cabe e acrescenta o maximo do momento.
    ///
    /// Continua 16:9 porque e a proporcao em que este jogo foi desenhado; a entrada do MAXIMO e a
    /// unica que foge disso, e ela pode: com `stretch/aspect = expand` quem decide a proporcao do
    /// que se ve e a JANELA, e nao a base (ver "O QUE A RESOLUCAO SIGNIFICA").
    /// </summary>
    /// <remarks>
    /// O PISO CONTINUA EM 1280x720, o mesmo da lista antiga, e isso e uma decisao e nao um esquecimento:
    /// a tela de opcoes tem ~690 px de altura de controles, entao uma base menor que 720 nao caberia
    /// nela propria. Oferecer 854x480 seria entregar uma tela que se corta sozinha.
    /// </remarks>
    private static readonly (int L, int A)[] Escada =
    [
        (1280, 720), (1366, 768), (1600, 900),
        (1920, 1080), (2560, 1440), (3840, 2160),
    ];

    /// <summary>
    /// AS RESOLUCOES QUE ESTE MODO PODE OFERECER AGORA. **Derivada em tempo de execucao**, nunca
    /// cravada -- era uma lista fixa de cinco itens, igual em janela e em tela cheia, e dois deles
    /// (1920x1080 e 2560x1440) nao cabiam em modo janela no monitor do dono. Escolher a nativa em
    /// janela punha a barra de titulo FORA da tela e enfiava 48 px do jogo por baixo da barra de
    /// tarefas, sem aviso nenhum.
    ///
    /// A ULTIMA ENTRADA E O TETO DO MOMENTO -- em janela e o "maximo da janela" (1904x993 no monitor
    /// do dono, que e o que ele chamou de *"a resolucao do fullscreen do modo janela"*), em tela
    /// cheia e a nativa do monitor.
    /// </summary>
    public static (int L, int A)[] ResolucoesPara(bool telaCheia)
    {
        Vector2I teto = TetoDeResolucao(telaCheia);
        List<(int L, int A)> saida = [.. Escada.Where(r => r.L <= teto.X && r.A <= teto.Y)];

        (int L, int A) maxima = (teto.X, teto.Y);
        if (!saida.Contains(maxima)) saida.Add(maxima);

        saida.Sort((a, b) => a.L != b.L ? a.L.CompareTo(b.L) : a.A.CompareTo(b.A));
        return [.. saida];
    }

    /// <summary>
    /// O QUE A LINHA DA LISTA DIZ. Em tela cheia ela diz **quanto a imagem vai ser esticada**, e
    /// avisa quando a esticada nao e inteira.
    ///
    /// ============================ O CUSTO QUE E DESTE JOGO E NAO DO MOTOR ============================
    /// A arte e de PIXEL, e o jogo poe cada coisa num ponto da grade de pixel de TELA (ver
    /// `LocalPlayer.NoPontoDaGrade`; o `snap_2d_transforms_to_pixel` do motor esta DESLIGADO
    /// justamente porque ele arredondava em espaco de mundo, que e a grade errada com zoom). Numa
    /// esticada de 1,5x um texel vira ora 1 ora 2 pixels de MONITOR, e o padrao troca conforme a
    /// camera anda: a imagem CINTILA. E o mesmo motivo pelo qual o <see cref="Zoom"/> e inteiro.
    ///
    /// E O PRECO ESTA MEDIDO, e nao so suposto: a bancada da rolagem (`--diagrolagem`, linha "NO
    /// VIDRO") mediu a hesitacao da rolagem em pixel de monitor a 1x e a 1,5x -- 0,45 contra 0,60.
    /// A resolucao menor nao acrescenta desordem nova a rolagem: ela AMPLIA a que ja existe.
    ///
    /// A saida NAO foi tirar essas resolucoes da lista: o pedido do dono e literalmente poder jogar
    /// em tela cheia com resolucao menor. Entao elas ficam, e a lista DIZ o preco -- quem quiser
    /// nitidez escolhe uma esticada inteira (960x540 num monitor 1080p e 2x exato).
    /// ==========================================================================================
    /// </summary>
    public static string RotuloDaResolucao((int L, int A) r, bool telaCheia)
    {
        Vector2I teto = TetoDeResolucao(telaCheia);
        string texto = $"{r.L} x {r.A}";

        if (r.L == teto.X && r.A == teto.Y)
            return texto + (telaCheia ? "  (nativa)" : "  (máximo da janela)");

        if (!telaCheia) return texto;

        float escala = Math.Min(teto.X / (float)r.L, teto.Y / (float)r.A);
        bool inteira = Math.Abs(escala - MathF.Round(escala)) < 0.02f;
        return $"{texto}  (estica {escala:0.##}x{(inteira ? ", nitida" : ", pode cintilar")})";
    }
}
