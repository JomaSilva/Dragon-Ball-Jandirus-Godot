using Godot;
using Jandirus.Core.Appearance;

namespace Jandirus.Client;

/// <summary>
/// ============================ BANCADA DA TINTA DO CABELO E DO RABO ============================
/// Ela mede o que CHEGA NA TELA, e nao o que o codigo pediu -- que e a unica coisa capaz de responder
/// a queixa do dono ("parece q tem algo pintando o cabelo, e o rabo, de pretos").
///
/// Um relato de COR nao se confere lendo codigo: o caminho tem tres operacoes empilhadas (a tinta do
/// uniform, o contorno da forma e o `COLOR` que o Godot entrega) e qualquer uma delas pode estar
/// achatando o desenho sem que nenhuma linha pareca errada. Aqui cada uma e ligada sozinha e o que
/// sai da tela e CONTADO: quantos tons distintos sobraram, e quao escuros ficaram.
///
/// A CONTA DE TONS E A MEDIDA CERTA porque o defeito relatado e "virou um borrao chapado". Relevo E
/// variedade de tom: a arte do `Hair_Goku` e quase toda preta e o que se ve como mecha sao os poucos
/// pixels mais claros. Se a contagem cai pra 1, o relevo morreu; se ela se mantem e o BRILHO MEDIO
/// desaba, alguem multiplicou o desenho.
///
///     &lt;godot&gt; --path . --diagtinta        (COM janela: no headless nao ha pixel pra ler)
/// ==============================================================================================
/// </summary>
public partial class RoboDeTinta : Node2D
{
    private readonly List<string> _linhas = [];
    private int _t;
    private CharacterVisual _v = null!;
    private AnimatedSprite2D _cab = null!, _rab = null!;
    private ColorRect _fundo = null!;
    private CanvasModulate? _ambiente;

    /// <summary>
    /// O FUNDO E MAGENTA PURO de proposito: ele nao existe em nenhum dos dois desenhos (cabelo preto,
    /// rabo cinza), entao "nao e o fundo" basta pra separar o sprite do resto da tela sem precisar
    /// saber onde ele caiu.
    /// </summary>
    private static readonly Color Fundo = new(1f, 0f, 1f);

    /// <summary>
    /// Onde cada sprite fica. A bancada separa os dois pelo X ao varrer a foto, e por isso eles tem
    /// que cair em METADES DIFERENTES dela -- a foto e do tamanho da JANELA (1920 aqui, nao os 1152
    /// do projeto), entao os dois precisam estar bem afastados pra a divisao valer em qualquer tela.
    /// </summary>
    private const int XCabelo = 300, XRabo = 1400, YSprite = 300;

    public override void _Ready()
    {
        const string dados = "res://Assets/Data/visual.json";
        if (!Godot.FileAccess.FileExists(dados)) { GD.Print("[tinta] sem visual.json"); GetTree().Quit(); return; }
        VisualCatalog cat = VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));

        _fundo = new ColorRect { Color = Fundo, Size = new Vector2(4000, 4000), Position = new Vector2(-500, -500), ZIndex = -100 };
        AddChild(_fundo);

        // ---- 1. O PERSONAGEM DE VERDADE, so pra LER o que o caminho de producao armou nos materiais.
        // Ele nao e fotografado: a foto e feita nas copias abaixo, que dao pra isolar camada por camada.
        _v = new CharacterVisual { Name = "Alvo", Position = new Vector2(-2000, -2000) };
        AddChild(_v);
        var ap = new Appearance { Cabelo = "Goku" };
        _v.Vestir(cat, ap, "Saiyan", "Male");
        _v.MostrarRabo(true);
        _v.SetMotion(Jandirus.Core.World.Facing.South, false);

        // ---- 2. AS COPIAS. Mesma folha, mesmo shader, uma camada por vez.
        _cab = Copia(cat.SpriteDoCabelo(ap.Cabelo)!, new Vector2(XCabelo, YSprite));
        _rab = Copia(CharacterVisual.SpriteDoRabo, new Vector2(XRabo, YSprite));
        _linhas.Add($"  folha do cabelo: {cat.SpriteDoCabelo(ap.Cabelo)} anim '{_cab.Animation}' "
                  + $"quadros {_cab.SpriteFrames.GetFrameCount(_cab.Animation)}");
        _linhas.Add($"  folha do rabo:   {CharacterVisual.SpriteDoRabo} anim '{_rab.Animation}' "
                  + $"quadros {_rab.SpriteFrames.GetFrameCount(_rab.Animation)}");
    }

    private AnimatedSprite2D Copia(string folha, Vector2 onde)
    {
        var s = new AnimatedSprite2D
        {
            Centered = false,
            Position = onde,
            TextureFilter = TextureFilterEnum.Nearest,
            Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Assets/Shaders/Personagem.gdshader") },
        };
        s.SpriteFrames = GD.Load<SpriteFrames>(folha);
        AddChild(s);
        // O RABO NAO TEM POSE PARADA na folha (`Tail.tres` so tem walk/flight/train/ko), e por isso a
        // escolha e por lista de preferencia e nao por um nome so: cair no `GetAnimationNames()[0]`
        // pegava `flight_mov_east`, cujo quadro 0 sai VAZIO na foto -- e a bancada media zero pixel.
        string anim = new[] { "default_south", "walk_south", "train_south" }
                          .FirstOrDefault(a => s.SpriteFrames.HasAnimation(a))
                      ?? s.SpriteFrames.GetAnimationNames()[0];
        s.Animation = anim;
        s.Pause();
        s.Frame = 0;
        return s;
    }

    public override void _Process(double _)
    {
        switch (_t++)
        {
            case 0: break;   // deixa o primeiro quadro desenhar

            case 1:
                _linhas.Add("--- o que o caminho de producao armou no personagem base (Saiyajin, cabelo Goku) ---");
                _linhas.Add($"  cabelo: tinta/modo = {Fmt(_v.TintaDoCabeloDeTeste)}");
                _linhas.Add($"  rabo:   tinta      = {(_v.TintaDoRaboDeTeste is { } tr ? tr.ToString() : "sem camada")}");
                Medir("A. so a arte (sem ambiente, sem contorno)");
                break;

            case 2:
                // ============================ O AMBIENTE SEMPRE TOCOU O BONECO ============================
                // Aqui esteve escrito o contrario -- que o ambiente "nunca tocou o boneco enquanto o shader
                // escrevia `COLOR = c`, e passou a tocar com o varying". Era a hipotese da vez pro relato
                // do dono, e ela e FALSA. A prova cabe em tres linhas e qualquer um pode refaze-la:
                //
                //     troque o fim do `fragment` por  `COLOR.rgb = vec3(1.0, 0.0, 0.0) * c.a;`
                //     (uma cor CONSTANTE, que nao le `COLOR` nenhum) e rode esta bancada.
                //
                // As linhas B e C continuam escurecendo: `#ff0000` sai `#dc0000` no meio-dia (`dc` e o R do
                // `dcdcd2`) e `#1b0000` na meia-noite. Ou seja o Godot aplica o `CanvasModulate` DEPOIS do
                // fragment -- nao ha o que escrever no shader que escape dele, e `COLOR = c` nunca escapou.
                //
                // ENTAO O QUE ESTAS DUAS LINHAS MEDEM e a luz do dia, e nao uma regressao: elas dizem
                // quanto o ceu come do RELEVO de uma arte quase preta (o cabelo perde 16% ao meio-dia e 87%
                // a meia-noite). E util e nao e defeito -- a linha D, sim, e a assinatura de defeito.
                // ==========================================================================================
                _ambiente = new CanvasModulate { Color = Iluminacao.AmbienteDia };
                AddChild(_ambiente);
                break;

            case 3: Medir($"B. + ambiente de MEIO-DIA ({Iluminacao.AmbienteDia.ToHtml(false)})"); break;

            case 4: _ambiente!.Color = new Color("1b2242"); break;

            case 5: Medir("C. + ambiente de MEIA-NOITE (1b2242)"); break;

            case 6:
                RemoveChild(_ambiente);
                _ambiente!.QueueFree();
                _ambiente = null;
                foreach (AnimatedSprite2D s in new[] { _cab, _rab })
                    ((ShaderMaterial)s.Material).SetShaderParameter("tinta_modo", 1);
                break;

            case 7: Medir("D. sem ambiente, modo MATIZ com tinta zero"); break;

            case 8:
                foreach (AnimatedSprite2D s in new[] { _cab, _rab })
                    ((ShaderMaterial)s.Material).SetShaderParameter("tinta_modo", 0);
                break;

            case 9: Medir("E. de volta ao modo SOMA com tinta zero (tem que bater com o A)"); break;

            default:
                GD.Print("\n[tinta] ===== BANCADA DA TINTA =====");
                foreach (string l in _linhas) GD.Print("[tinta] " + l);
                GD.Print("[tinta] ===== FIM =====");
                GetTree().Quit();
                break;
        }
    }

    /// <summary>
    /// Fotografa a tela e conta, pra cada metade, quantos tons OPACOS distintos sobraram e qual e o
    /// brilho medio deles. Dois numeros e o bastante: o primeiro diz se ha relevo, o segundo diz se
    /// alguem escureceu tudo.
    /// </summary>
    private void Medir(string rotulo)
    {
        Image? img = GetViewport()?.GetTexture()?.GetImage();
        if (img == null || img.IsEmpty()) { _linhas.Add($"{rotulo}: SEM FOTO (headless nao renderiza)"); return; }

        _linhas.Add($"{rotulo}   [foto {img.GetWidth()}x{img.GetHeight()}]");
        _linhas.Add("  " + Conta(img, "cabelo", 0, img.GetWidth() / 2));
        _linhas.Add("  " + Conta(img, "rabo  ", img.GetWidth() / 2, img.GetWidth()));
    }

    private static string Conta(Image img, string nome, int x0, int x1)
    {
        var tons = new Dictionary<int, int>();
        double soma = 0;
        int n = 0, claro = 0, escuro = int.MaxValue;
        for (int y = 0; y < img.GetHeight(); y++)
            for (int x = x0; x < x1; x++)
            {
                Color c = img.GetPixel(x, y);
                // "e o fundo?" com folga: o ambiente multiplica o ColorRect junto, entao a magenta
                // da foto nao e a magenta da constante -- o que nao muda e o VERDE zerado.
                if (c.G < 0.02f && c.R > 0.05f && c.B > 0.05f) continue;
                int r = (int)(c.R * 255 + 0.5f), g = (int)(c.G * 255 + 0.5f), b = (int)(c.B * 255 + 0.5f);
                int chave = (r << 16) | (g << 8) | b;
                tons[chave] = tons.GetValueOrDefault(chave) + 1;
                int luz = (r * 299 + g * 587 + b * 114) / 1000;
                soma += luz; n++;
                if (luz > claro) claro = luz;
                if (luz < escuro) escuro = luz;
            }

        if (n == 0) return $"{nome}: NENHUM PIXEL (o sprite nao caiu nesta metade da tela)";
        var top = tons.OrderByDescending(p => p.Value).Take(4)
            .Select(p => $"#{p.Key:x6}x{p.Value}");
        return $"{nome}: {tons.Count} tom(ns) em {n}px | brilho medio {soma / n:0.0} "
             + $"(de {escuro} a {claro}) | os mais comuns: {string.Join(" ", top)}";
    }

    private static string Fmt((Vector3 Tinta, int Modo)? v) =>
        v is { } x ? $"{x.Tinta} / {(x.Modo == 1 ? "MATIZ" : "SOMA")}" : "sem camada";
}
