using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A LUZ DO MUNDO: o ciclo do dia, as fontes de cenario e o que o personagem consegue ver.
///
/// TRES CAMADAS, e vale saber por que sao tres:
///
///  1. AMBIENTE (<see cref="CanvasModulate"/>) -- a cor do dia. Multiplica a cena inteira, e
///     e o que faz meio-dia parecer meio-dia e meia-noite parecer meia-noite.
///  2. FONTES DE CENARIO -- fogueira, tocha, lampada, lava. Cada uma e um
///     <see cref="PointLight2D"/> plantado onde o mapa disse (`&lt;zona&gt;.luz`), e elas SOMAM
///     luz por cima do ambiente. E o que faz a fogueira acender a noite.
/// O QUE NAO ESTA AQUI: o campo de visao. Ele nao e luz -- e um veu escuro recortado por
/// raycast, e mora em <see cref="Visao"/>. Vale ler o porque la: uma luz de visao acende
/// tambem o sprite de quem a carrega, e era isso que fazia o personagem parecer aceso.
/// </summary>
public partial class Iluminacao : Node2D
{
    /// <summary>
    /// TROCA O FILTRO DE TODAS AS LUZES JA PLANTADAS.
    ///
    /// Sem isto, mudar a qualidade so valeria pras luzes do PROXIMO planeta -- o jogador mexeria
    /// no controle e nao veria nada mudar, que e a pior resposta que um controle pode dar.
    /// </summary>
    public static void AplicarQualidade(Node raiz)
    {
        foreach (Node n in raiz.GetChildren())
        {
            if (n is PointLight2D l) l.ShadowFilter = Boot.Config.FiltroDeSombra;
            AplicarQualidade(n);
        }
    }

    // =====================================================================
    // O CICLO DO DIA
    // =====================================================================
    /// <summary>
    /// Quanto dura o dia PADRAO (o da Terra), em segundos reais. Mora no
    /// <see cref="Jandirus.Core.World.Ceu"/> porque a escala do universo tambem depende dele --
    /// eram duas copias, com um comentario pedindo pra quem mexesse numa lembrar da outra.
    ///
    /// CADA PLANETA TEM A SUA. Este e so o padrao de quem nao diz o contrario; a duracao real
    /// vem da ficha do mundo (ver <see cref="Relogio"/>).
    /// </summary>
    public const double SegundosPorDia = Ceu.SegundosPorDia;

    /// <summary>
    /// A cor do ambiente ao longo do dia. Nao e um degrade de "claro pra escuro": o
    /// amanhecer e o poente passam pelo LARANJA, e a noite puxa pro AZUL. E o que faz a hora
    /// do dia ser reconhecivel de relance, sem relogio na tela.
    ///
    /// A fase vai de 0 (meia-noite) a 1 (meia-noite de novo).
    /// </summary>
    private static readonly (float Fase, Color Cor)[] Curva =
    [
        // A NOITE SUBIU UM TOM depois que a visao deixou de ser luz. Antes o ambiente entrava
        // rebaixado e a luz do personagem devolvia a diferenca -- de noite ela clareava o
        // circulo em volta e dava pra jogar. Agora o ambiente e o que se ve, e um azul
        // profundo demais transformaria a noite inteira em tela preta.
        (0.00f, new Color("1b2242")),   // meia-noite:  azul profundo
        (0.20f, new Color("212a4a")),   // madrugada
        (0.26f, new Color("54405e")),   // primeira luz: o roxo antes do sol
        (0.30f, new Color("d98452")),   // AMANHECER:   laranja rasgando
        (0.36f, new Color("f0d2ab")),   // manha cedo
        (0.45f, new Color("dcdcd2")),   // dia
        (0.55f, new Color("dcdcd2")),   // dia
        (0.68f, new Color("f0cfa0")),   // tarde dourada
        (0.74f, new Color("e08246")),   // POENTE:      laranja de novo
        (0.79f, new Color("6b4a6b")),   // crepusculo
        (0.85f, new Color("232b4c")),   // noite caindo
        (1.00f, new Color("1b2242")),   // fecha o ciclo
    ];

    /// <summary>
    /// O tom do MEIO-DIA. Vai INTEIRO no <see cref="CanvasModulate"/>: o mundo de dia e um
    /// mundo de dia.
    ///
    /// Houve uma versao em que o ambiente entrava rebaixado e uma luz presa no personagem
    /// devolvia a diferenca dentro do campo de visao. Funcionava como mecanica e falhava como
    /// imagem -- a luz tambem batia no sprite de quem a carregava, e o personagem ficava mais
    /// claro que o cenario. Quem esconde agora e o veu (<see cref="Visao"/>), que escurece o
    /// que esta atras da parede sem acender nada.
    /// </summary>
    public static readonly Color AmbienteDia = new("dcdcd2");

    /// <summary>
    /// A COR DE UMA NOITE DE LUA CHEIA. E pra onde o azul da meia-noite caminha conforme o luar
    /// aumenta -- mais claro e um tom mais frio, que e como o olho le "prateado".
    ///
    /// ISTO E MECANICA, NAO ENFEITE: a lua cheia e o gatilho do Oozaru, e um gatilho que nao
    /// muda a imagem da tela e um gatilho que o jogador so descobre lendo o chat. A diferenca
    /// entre a lua nova e a cheia tem que dar pra ver do lado de fora da janela.
    /// </summary>
    public static readonly Color AmbienteLuar = new("41507f");

    /// <summary>
    /// QUANTO O CLIMA PERDE DE FORCA A NOITE.
    ///
    /// Nuvem tira LUZ DO SOL. A meia-noite nao ha sol pra tirar, entao aplicar o mesmo fator de
    /// uma tempestade de meio-dia sobre um ambiente que ja esta em 13% de brilho deixa a tela
    /// preta e o jogo injogavel. Aqui o efeito do clima na luz encolhe conforme a base escurece
    /// -- que e o mesmo raciocinio, so que escrito.
    ///
    /// Nao vale pro que o clima faz DE COR (o ocre da areia, o vermelho da chuva de sangue): isso
    /// continua inteiro, porque cor do ar nao depende de haver sol.
    /// </summary>
    private const float ClimaPerdeANoite = 0.55f;

    private CanvasModulate _ambiente = null!;
    private Node2D _luzes = null!;
    private ClimaNaTela _clima = null!;

    /// <summary>
    /// A FICHA DE CEU DO PLANETA em que se esta: quanto dura o dia daqui, se ha noite, e que lua.
    ///
    /// Trocada a cada mudanca de zona (ver `World.CarregarZona`). Era isto que faltava pra "cada
    /// planeta com o proprio horario": a curva de luz sempre foi a mesma, o que muda e a
    /// VELOCIDADE e a DEFASAGEM com que se anda nela.
    /// </summary>
    public RelogioDoPlaneta Relogio { get; set; } = RelogioDoPlaneta.Padrao;

    /// <summary>Que climas podem cair neste planeta, e com que frequencia. Trocado por zona.</summary>
    public ClimaDoPlaneta ClimaDaqui { get; set; } = ClimaDoPlaneta.Nenhum;

    /// <summary>O sal do sorteio do clima -- e o que faz o ceu de Vegeta nao ser o da Terra.</summary>
    public ulong SalDoClima { get; set; }

    /// <summary>
    /// QUE HORAS SAO NO UNIVERSO, em segundos. Quem manda e o servidor (`S2C.Ceu`); isto so anda
    /// sozinho entre um pacote e outro, pra a luz nao dar saltos a cada correcao.
    /// </summary>
    public double Tempo { get; private set; }

    /// <summary>O ceu de agora, ja resolvido pro planeta atual. O HUD e a bancada leem daqui.</summary>
    public EstadoDoCeu Estado { get; private set; }

    /// <summary>
    /// O CLIMA de agora: natural, ou o que o servidor mandou (ver `S2C.Clima`).
    ///
    /// Chama-se assim e nao `Clima` porque `Clima` e o tipo do Core que esta classe usa a cada
    /// quadro -- uma propriedade com o nome do tipo o esconderia dentro da propria classe.
    /// </summary>
    public EstadoDoClima TempoQueFaz { get; private set; }

    /// <summary>A hora LOCAL deste planeta, de 0 (meia-noite) a 1. O menu mostra.</summary>
    public double Fase => Estado.Hora;

    public override void _Ready()
    {
        _ambiente = new CanvasModulate { Name = "Ambiente", Color = AmbienteDia };
        AddChild(_ambiente);

        _luzes = new Node2D { Name = "Fontes" };
        AddChild(_luzes);

        _clima = new ClimaNaTela { Name = "Clima" };
        AddChild(_clima);

        if (GameClient.Instance is { } cli)
        {
            if (cli.TempoChegou) { Tempo = cli.TempoDoMundo; _temHora = true; }
            cli.HoraDoMundo += AcertarRelogio;
        }
        Recalcular(0);
    }

    /// <summary>
    /// JA SEI QUE HORAS SAO?
    ///
    /// ============================ POR QUE ESPERAR IMPORTA ============================
    /// O `World` nasce DENTRO do callback de `Joined`, ou seja, no meio do tratamento do
    /// `JoinAccepted` -- e o `S2C.Ceu` e o pacote SEGUINTE. Por alguns quadros o cliente esta
    /// montado e nao sabe a hora, e antes desta guarda ele desenhava esses quadros com o relogio
    /// em ZERO: meia-noite de 1970, e o clima do bloco de tempo de 1970 junto.
    ///
    /// Nao dava pra ver de olho (sao dois ou tres quadros no meio da carga da zona), mas a
    /// bancada do clima pegou: a forca do ceu saltava 59 por segundo no instante em que o
    /// relogio pulava 56 anos de uma vez. Um salto que ninguem ve continua sendo um salto, e o
    /// conserto certo nao e afrouxar a medida -- e nao desenhar o ceu antes de saber qual e.
    /// ================================================================================
    /// </summary>
    private bool _temHora;

    public override void _ExitTree()
    {
        if (GameClient.Instance is { } cli) cli.HoraDoMundo -= AcertarRelogio;
    }

    /// <summary>
    /// O SERVIDOR CORRIGIU A HORA. Encaixa direto em vez de interpolar: os dois relogios andam na
    /// mesma escala (segundos reais), entao entre uma sincronia e outra a diferenca e de
    /// milissegundos e nao se ve. O salto que se ve e o PRIMEIRO -- e esse tem que ser um salto,
    /// porque antes dele o cliente nao sabia que horas eram.
    /// </summary>
    private void AcertarRelogio(double segundos)
    {
        Tempo = segundos;
        _temHora = true;
    }

    /// <summary>
    /// Anda o relogio local entre um pacote e outro do servidor. Nao e a verdade -- e
    /// interpolacao, pra a luz nao andar aos saltos a cada correcao de hora.
    /// </summary>
    public override void _Process(double delta)
    {
        Tempo += delta;
        Recalcular(delta);
    }

    /// <summary>
    /// QUAO ESCURA A CENA ESTA AGORA: 0 = meio-dia cheio, 1 = breu.
    ///
    /// ============================ QUEM PRECISA DISSO E POR QUE ============================
    /// A aura de transformacao (<see cref="Aura"/>) acende uma <c>PointLight2D</c>. De noite ela e
    /// o efeito; ao meio-dia ela e um borrao branco -- o ambiente ja esta claro, entao somar luz so
    /// estoura a cena e some com o proprio personagem dentro dela.
    ///
    /// E ESTATICO porque a luz e de CADA CORPO e a escuridao e do MUNDO: dez corpos transformados
    /// procurando o node de iluminacao na arvore, todo quadro, seria dez buscas pra ler um numero
    /// que e o mesmo pros dez.
    /// ==================================================================================
    /// </summary>
    public static float Escuridao { get; private set; }

    /// <summary>Força a escuridao. SO PRA BANCADA -- em jogo quem manda e o relogio do planeta.</summary>
    public static void EscuridaoDeTeste(float v) => Escuridao = Mathf.Clamp(v, 0, 1);

    /// <summary>Guarda a escuridao a partir da cor do ambiente. Chamado sempre que ela muda.</summary>
    private static void AnotarEscuridao(Color ambiente) =>
        Escuridao = 1 - Mathf.Clamp(ambiente.Luminance, 0, 1);

    private void Recalcular(double delta)
    {
        // ANTES DE SABER A HORA, o mundo fica em luz de dia e o ceu fica vazio -- ver `_temHora`.
        // Chutar um horario aqui seria pintar a cena errada pra corrigi-la na cara do jogador.
        if (!_temHora)
        {
            _ambiente.Color = AmbienteDia;
            AnotarEscuridao(AmbienteDia);
            _clima.Aplicar(default, delta, AmbienteDia);
            return;
        }

        Estado = Ceu.De(Relogio, Tempo);
        TempoQueFaz = Clima.De(ClimaDaqui, Tempo, SalDoClima, Forcado);

        Color cor = CorDoCeu(Estado, TempoQueFaz);
        _ambiente.Color = cor;
        AnotarEscuridao(cor);
        // A COR DO AMBIENTE VAI JUNTO: o veu do clima e de tela e escapa do `CanvasModulate`, entao
        // e ele que tem que se escurecer. Sem isto, neblina branca brilharia a meia-noite.
        //
        // A LUA NAO ESTA MAIS AQUI: ela virou mostrador do HUD (ver `LuaNoCeu`), porque fase da
        // lua e informacao e nao cenario -- e no ceu ela passava por cima do combate.
        _clima.Aplicar(TempoQueFaz, delta, cor);
    }

    /// <summary>
    /// CAIU UM RAIO NAQUELE PONTO DO MAPA -- o servidor contou pra zona inteira (ver `S2C.Raio`).
    /// Repassa pro desenho, que decide entre mostrar o risco e so acender o clarao.
    /// </summary>
    public void Raio(Vector2 onde, float semente) => _clima.Estourar(onde, semente);

    /// <summary>
    /// O CLIMA QUE O SERVIDOR MANDOU. Vazio = o ceu segue o ciclo natural.
    ///
    /// So o forcado viaja pelo fio; o natural as duas pontas calculam do mesmo tempo do mundo.
    /// Ver `S2C.Clima` e `GameServer.Clima.cs`.
    /// </summary>
    public ClimaForcado Forcado { get; set; }

    /// <summary>
    /// A COR DO AMBIENTE: a curva do dia, clareada pelo LUAR e lavada pelo CLIMA.
    ///
    /// O luar so tem efeito de noite (de dia a lua esta abaixo do horizonte e o valor e zero), e
    /// ele ja embute a altura -- lua cheia no horizonte quase nao clareia, lua cheia a pino
    /// clareia tudo. O teto de 0,7 existe pra que a noite de lua cheia continue sendo NOITE: uma
    /// noite tao clara quanto o dia tiraria da escuridao o unico peso que ela tem.
    ///
    /// A ORDEM IMPORTA, e o luar vem PRIMEIRO. Nuvem carregada tapa a lua: aplicar o luar depois
    /// do clima daria uma noite de tempestade CLAREADA por uma lua cheia que ninguem consegue
    /// ver. O `Encobre` do clima ja derruba o luar antes de ele chegar aqui (ver `Recalcular`),
    /// e a cor do ar entra por cima do que sobrou.
    ///
    /// ============================ O CLIMA MULTIPLICA, NAO MISTURA ============================
    /// A versao anterior misturava a cena com um cinza CLARO e preservava a luminancia -- e por
    /// isso, de dia, nao mudava quase nada: o tom do dia (`dcdcd2`) ja e quase cinza, entao
    /// "dessaturar mantendo o brilho" era operacao nula. Nublado, neblina e nevasca ficavam
    /// invisiveis em pleno 100%.
    ///
    /// Agora o clima e a COR DO AR (ver `Clima.CorDoAr`) e ela MULTIPLICA. Abaixo de 1 tira luz
    /// (tempestade), acima de 1 poe (nevasca, que e um whiteout). Multiplicar escala em vez de
    /// comprimir, entao o mundo escurece ou clareia de verdade sem que nada perca contraste.
    /// =========================================================================================
    /// </summary>
    public static Color CorDoCeu(EstadoDoCeu ceu, EstadoDoClima clima)
    {
        double luar = ceu.Luar * (1 - clima.Encobre);
        Color cor = CorDaFase((float)ceu.Hora).Lerp(AmbienteLuar, (float)(luar * 0.7));

        (double r, double g, double b) = clima.Ar;

        // A NOITE ABRANDA O QUE E PERDA DE LUZ (fator < 1) e deixa o resto passar inteiro.
        float noite = 1 - Mathf.Clamp(cor.Luminance, 0, 1);
        float alivio = noite * ClimaPerdeANoite;
        float Suave(double v) => v < 1 ? Mathf.Lerp((float)v, 1f, alivio) : (float)v;

        return new Color(
            Mathf.Clamp(cor.R * Suave(r), 0, 1),
            Mathf.Clamp(cor.G * Suave(g), 0, 1),
            Mathf.Clamp(cor.B * Suave(b), 0, 1));
    }

    /// <summary>Interpola a curva do dia. Publico porque o menu de pause mostra a hora.</summary>
    public static Color CorDaFase(float fase)
    {
        fase -= MathF.Floor(fase);
        for (int i = 1; i < Curva.Length; i++)
        {
            if (fase > Curva[i].Fase) continue;
            (float f0, Color c0) = Curva[i - 1];
            (float f1, Color c1) = Curva[i];
            float t = f1 > f0 ? (fase - f0) / (f1 - f0) : 0;
            return c0.Lerp(c1, t);
        }
        return Curva[^1].Cor;
    }

    /// <summary>Nome legivel da hora. Mora no Core -- servidor e cliente dizem a mesma coisa.</summary>
    public static string NomeDaFase(double fase) => Ceu.NomeDaHora(fase);

    // =====================================================================
    // FONTES DE CENARIO
    // =====================================================================
    /// <summary>
    /// Planta as luzes do mapa. Chamado a cada troca de zona -- as antigas somem junto com o
    /// planeta antigo.
    /// </summary>
    public void CarregarLuzes(string? caminhoJson)
    {
        foreach (Node n in _luzes.GetChildren()) n.QueueFree();
        if (string.IsNullOrEmpty(caminhoJson) || !Godot.FileAccess.FileExists(caminhoJson)) return;

        var json = Json.ParseString(Godot.FileAccess.GetFileAsString(caminhoJson));
        if (json.VariantType != Variant.Type.Array) return;

        int n2 = 0;
        foreach (Variant v in json.AsGodotArray())
        {
            var d = v.AsGodotDictionary();
            int cx = (int)d["x"], cy = (int)d["y"];
            int raio = d.ContainsKey("raio") ? (int)d["raio"] : 96;
            var cor = new Color(d.ContainsKey("cor") ? (string)d["cor"] : "ffffff");
            float forca = d.ContainsKey("forca") ? (float)d["forca"] : 1f;
            bool tremula = d.ContainsKey("tremula") && (bool)d["tremula"];

            var luz = new Fogo
            {
                Name = $"Luz{n2++}",
                Position = new Vector2(cx * 32 + 16, cy * 32 + 16),
                Cor = cor,
                Raio = raio,
                Forca = forca,
                Tremula = tremula,
            };
            _luzes.AddChild(luz);
        }
        GD.Print($"[luz] {n2} fontes de cenario na zona");
    }
}

/// <summary>
/// UMA FONTE DE LUZ do cenario. Fogueira, tocha, lampada, lava.
///
/// O TREMOR importa mais do que parece: uma chama com brilho FIXO le como lampada. Duas ondas
/// senoidais de periodo incomensuravel (2,7 e 5,3 Hz) dao um bruxuleio que nunca se repete
/// igual, e o olho aceita como fogo. Sem isso a fogueira vira um poste.
/// </summary>
public partial class Fogo : Node2D
{
    public Color Cor = Colors.White;
    public int Raio = 96;
    public float Forca = 1f;
    public bool Tremula;

    private PointLight2D _luz = null!;

    private double _t;

    public override void _Ready()
    {
        // fase inicial sorteada: sem isso, TODA fogueira do mapa pisca junto e o efeito vira
        // um estrobo de discoteca em vez de fogo
        _t = GD.Randf() * 10f;

        _luz = new PointLight2D
        {
            Texture = Radial(Raio),
            Color = Cor,
            Energy = Forca,
            // sombra LIGADA: a fogueira dentro da casa nao pode vazar luz pela parede
            ShadowEnabled = true,
            // O FILTRO VEM DA CONFIGURACAO (baixo/medio/alto). Era fixo em PCF5.
            ShadowFilter = Boot.Config.FiltroDeSombra,
            BlendMode = Light2D.BlendModeEnum.Add,
            ZIndex = -5,
        };
        AddChild(_luz);
        SetProcess(Tremula);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        float tremor = 1f
                     + MathF.Sin((float)_t * 2.7f) * 0.06f
                     + MathF.Sin((float)_t * 5.3f) * 0.035f;
        _luz.Energy = Forca * tremor;
    }

    /// <summary>Textura radial gerada em codigo: nao depende de arte importada.</summary>
    /// <summary>
    /// AS TEXTURAS SAO COMPARTILHADAS POR RAIO. Cada `Fogo` criava um Gradient e uma
    /// GradientTexture2D novos no `_Ready` -- e nos 40 mapas existem so TRES raios distintos (88,
    /// 104 e 112) entre 125 luzes. Ou seja, 122 das 125 texturas eram copias exatas, cada uma
    /// custando alocacao + upload de ~121 KB pra GPU no meio da troca de zona.
    ///
    /// Medido: `CarregarLuzes` custava 8,6 ms com as 4 luzes da Terra (2,15 ms por luz), e Vegeta
    /// tem 25 -- ~54 ms so de acender fogueira. Com o cache, a segunda zona em diante paga zero.
    /// </summary>
    private static readonly Dictionary<int, GradientTexture2D> _radiais = [];

    public static GradientTexture2D Radial(int raio)
    {
        if (_radiais.TryGetValue(raio, out GradientTexture2D? pronta)) return pronta;
        GradientTexture2D nova = MontarRadial(raio);
        _radiais[raio] = nova;
        return nova;
    }

    private static GradientTexture2D MontarRadial(int raio)
    {
        var g = new Gradient();
        g.SetColor(0, Colors.White);
        g.SetColor(1, new Color(1, 1, 1, 0));
        return new GradientTexture2D
        {
            Gradient = g,
            Width = raio * 2,
            Height = raio * 2,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.0f, 0.5f),
        };
    }
}
