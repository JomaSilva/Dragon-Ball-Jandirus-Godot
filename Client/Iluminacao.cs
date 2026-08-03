using Godot;

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
    // =====================================================================
    // O CICLO DO DIA
    // =====================================================================
    /// <summary>
    /// Quanto dura um dia inteiro, em segundos reais. 24 minutos = 1 minuto por hora, que e o
    /// bastante pra alguem numa sessao normal ver amanhecer e anoitecer sem que a luz mude
    /// debaixo do pe enquanto se luta.
    /// </summary>
    public const double SegundosPorDia = 24 * 60;

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

    private CanvasModulate _ambiente = null!;
    private Node2D _luzes = null!;
    private double _fase = 0.42;   // comeca de manha: e o horario em que o mapa se le melhor

    /// <summary>A hora do dia, de 0 (meia-noite) a 1. Quem manda e o SERVIDOR.</summary>
    public double Fase
    {
        get => _fase;
        set { _fase = value - Math.Floor(value); AplicarAmbiente(); }
    }

    public override void _Ready()
    {
        _ambiente = new CanvasModulate { Name = "Ambiente", Color = AmbienteDia };
        AddChild(_ambiente);

        _luzes = new Node2D { Name = "Fontes" };
        AddChild(_luzes);

        AplicarAmbiente();
    }

    /// <summary>
    /// Anda o relogio local entre um pacote e outro do servidor. Nao e a verdade -- e
    /// interpolacao, pra a luz nao andar aos saltos a cada correcao de hora.
    /// </summary>
    public override void _Process(double delta)
    {
        _fase += delta / SegundosPorDia;
        if (_fase >= 1) _fase -= 1;
        AplicarAmbiente();
    }

    private void AplicarAmbiente() => _ambiente.Color = CorDaFase((float)_fase);

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

    /// <summary>Nome legivel da hora, pro HUD ("amanhecer", "tarde", "noite").</summary>
    public static string NomeDaFase(double fase) => (fase - Math.Floor(fase)) switch
    {
        < 0.26 => "madrugada",
        < 0.33 => "amanhecer",
        < 0.45 => "manha",
        < 0.62 => "meio-dia",
        < 0.72 => "tarde",
        < 0.80 => "poente",
        _ => "noite",
    };

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
            ShadowFilter = Light2D.ShadowFilterEnum.Pcf5,
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
