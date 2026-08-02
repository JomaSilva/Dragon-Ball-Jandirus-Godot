using Godot;

namespace Jandirus.Client;

/// <summary>
/// OS EFEITOS DE IMPACTO. Tudo que aparece na tela quando um golpe acontece e nao e o
/// personagem em si: a faisca, o anel de choque, os fantasmas da esquiva e o tremor da tela.
///
/// Fica separado do <see cref="World"/> de proposito. O mundo TRADUZ o relato do servidor em
/// intencao ("acertou forte no torso"); esta classe decide o que isso VIRA na tela. Misturar
/// os dois transforma qualquer ajuste de sensacao numa cirurgia no meio da rede.
///
/// TODO EFEITO E FILHO DA CAMADA DE ATORES, nunca do personagem. Preso ao boneco, ele herda a
/// escala, o `Modulate` do flash de dano e -- pior -- some junto quando o alvo morre e o node
/// e liberado, cortando a propria explosao que anunciava a morte.
/// </summary>
public static class CombatFx
{
    private const string Arte = "res://Assets/Sprites/Misc/";

    /// <summary>Seis variantes de faisca, um quadro cada. E o `attackspark` do original.</summary>
    private const string Faisca = Arte + "effects/attackspark.tres";

    /// <summary>O borrao de velocidade da esquiva.</summary>
    private const string Zanzo = Arte + "Zanzoken.tres";

    private static readonly RandomNumberGenerator Rng = new();

    // =====================================================================
    // FAISCA DE IMPACTO
    // =====================================================================
    /// <summary>
    /// A faisca do golpe, no PONTO ENTRE os dois corpos -- nao em cima do alvo.
    ///
    /// O meio do caminho e onde o punho encontra o corpo, e e ali que o olho procura. Estourar
    /// a faisca centrada no alvo faz o golpe parecer uma explosao interna dele em vez de um
    /// impacto entre dois.
    /// </summary>
    public static void Impacto(Node2D pai, Vector2 onde, float escala, Color cor)
    {
        var f = ResourceLoader.Load<SpriteFrames>(Faisca);
        if (f == null) return;

        string[] nomes = f.GetAnimationNames();
        if (nomes.Length == 0) return;

        var s = new AnimatedSprite2D
        {
            SpriteFrames = f,
            Animation = nomes[Rng.Randi() % (uint)nomes.Length],
            Frame = 0,
            Position = onde,
            Scale = Vector2.One * escala * 0.6f,
            Modulate = cor,
            ZIndex = 30,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            // ADITIVO: faisca e LUZ. No modo normal ela vira um adesivo opaco por cima do
            // personagem; somando, ela ACENDE o que esta atras e some sem deixar buraco.
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        pai.AddChild(s);

        Tween t = s.CreateTween();
        t.SetParallel();
        t.TweenProperty(s, "scale", Vector2.One * escala * 1.1f, 0.16);
        t.TweenProperty(s, "modulate:a", 0.0f, 0.16).SetDelay(0.04);
        t.Chain().TweenCallback(Callable.From(s.QueueFree));
    }

    // =====================================================================
    // ANEL DE CHOQUE
    // =====================================================================
    /// <summary>
    /// Anel procedural: nasce no ponto do impacto e se abre. Feito em shader e nao com o
    /// `Shockwavecustom` convertido porque ele escala de 32 a 512 px sem serrilhar -- a arte
    /// de 64 px ampliada quatro vezes vira um borrao de blocos.
    /// </summary>
    private const string ShaderAnel = """
        shader_type canvas_item;

        uniform float t : hint_range(0.0, 1.0) = 0.0;   // 0 = nasceu, 1 = morreu
        uniform vec4  cor : source_color = vec4(1.0, 0.95, 0.72, 1.0);
        uniform float espessura : hint_range(0.005, 0.4) = 0.09;

        void fragment() {
            vec2  d = UV - vec2(0.5);
            float r = length(d) * 2.0;                  // 0 no centro, 1 na borda

            float esp  = espessura * (1.0 - t * 0.7);   // o anel afina enquanto abre
            float anel = smoothstep(esp, 0.0, abs(r - t));
            float some = (1.0 - t) * (1.0 - t);

            COLOR = vec4(cor.rgb, anel * some * cor.a);
            COLOR.a *= step(r, 1.0);                    // nao vaza pra fora do circulo
        }
        """;

    private static Shader? _anel;
    private static Shader AnelShader => _anel ??= new Shader { Code = ShaderAnel };

    public static void Onda(Node2D pai, Vector2 onde, float raio, Color cor, double duracao = 0.25)
    {
        var mat = new ShaderMaterial { Shader = AnelShader };
        mat.SetShaderParameter("cor", cor);
        mat.SetShaderParameter("t", 0f);

        var r = new ColorRect
        {
            Size = new Vector2(raio * 2, raio * 2),
            Position = onde - new Vector2(raio, raio),
            Color = Colors.White,
            Material = mat,
            ZIndex = 29,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        pai.AddChild(r);

        Tween t = r.CreateTween();
        t.TweenMethod(Callable.From<float>(v => mat.SetShaderParameter("t", v)), 0f, 1f, duracao);
        t.TweenCallback(Callable.From(r.QueueFree));
    }

    // =====================================================================
    // FANTASMA DE ESQUIVA
    // =====================================================================
    private const string ShaderSilhueta = """
        shader_type canvas_item;
        uniform vec4  cor : source_color = vec4(0.55, 0.80, 1.00, 1.0);
        uniform float forca : hint_range(0.0, 1.0) = 1.0;
        void fragment() {
            float a = texture(TEXTURE, UV).a;
            COLOR = vec4(cor.rgb, a * cor.a * forca);   // silhueta chapada
        }
        """;

    private static Shader? _silhueta;
    private static Shader SilhuetaShader => _silhueta ??= new Shader { Code = ShaderSilhueta };

    /// <summary>
    /// O BORRAO DA ESQUIVA. Tres silhuetas azul-gelo que somem em cascata -- o vocabulario do
    /// `Zanzoken` do original, que hoje era o unico desfecho SEM nada na tela.
    /// </summary>
    public static void Esquiva(Node2D pai, Vector2 onde, Vector2 fuga)
    {
        var f = ResourceLoader.Load<SpriteFrames>(Zanzo);
        if (f == null) return;
        string[] nomes = f.GetAnimationNames();
        if (nomes.Length == 0 || f.GetFrameCount(nomes[0]) == 0) return;

        Texture2D tex = f.GetFrameTexture(nomes[0], 0);

        for (int i = 0; i < 3; i++)
        {
            var mat = new ShaderMaterial { Shader = SilhuetaShader };
            mat.SetShaderParameter("forca", 0.7f);

            var s = new Sprite2D
            {
                Texture = tex,
                Position = onde + fuga * (i * 6),
                Material = mat,
                ZIndex = 28,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            };
            pai.AddChild(s);

            Tween t = s.CreateTween();
            t.TweenInterval(i * 0.05);
            t.TweenMethod(Callable.From<float>(v => mat.SetShaderParameter("forca", v)), 0.7f, 0f, 0.18);
            t.TweenCallback(Callable.From(s.QueueFree));
        }
    }
}
