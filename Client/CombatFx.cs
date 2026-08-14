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
    /// <summary>
	/// O CODIGO DESTE EFEITO mora num `.gdshader` de verdade -- ver o comentario de
	/// <see cref="CharacterVisual"/>: efeito procedural nao se acerta lendo codigo, se acerta
	/// arrastando o valor e OLHANDO, e pra isso ele precisa abrir no editor do Godot.
	/// </summary>
	private const string CaminhoDoShader = "res://Assets/Shaders/Impacto.gdshader";

    private static Shader? _anel;
    private static Shader AnelShader => _anel ??= ResourceLoader.Load<Shader>(CaminhoDoShader);

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
    // O JATO DE SANGUE DO MEMBRO ARRANCADO
    // =====================================================================
    /// <summary>Tres quadros de 32 px. E o `Blood Spray.dmi` do original, sem variante direcional.</summary>
    private const string Jato = Arte + "Blood Spray.tres";

    /// <summary>
    /// Meio segundo, e o numero e do original: `EffectStart` carrega o icone e faz `sleep(5)` antes
    /// do `EffectEnd` (`EffectLayer.dm:110-116`), e `sleep(5)` no BYOND e 0,5 s. A folha confirma
    /// por outro caminho -- os tres quadros duram 1+2+2 = 5 tiques a 10 fps. A arte foi desenhada
    /// pra caber exatamente nesse prazo.
    /// </summary>
    private const double DuracaoDoJato = 0.5;

    /// <summary>
    /// O JATO DE SANGUE de quem acabou de perder um membro.
    ///
    /// ============================ ELE ACOMPANHA O CORPO, MAS NAO E FILHO DELE ============================
    /// No original o jato e um overlay do mob com `VIS_INHERIT_DIR|VIS_INHERIT_ID` (`Overlays.dm:19`):
    /// nasce NO corpo e anda com ele. Aqui ele nao pode ser filho do corpo -- e a regra do cabecalho
    /// desta classe, e ela existe por um caso que e exatamente este: perder um membro pode MATAR, e
    /// um efeito filho do corpo some junto quando o node do morto e liberado, cortando justamente a
    /// animacao que anunciava a morte.
    ///
    /// A saida e o <see cref="RemoteTransform2D"/>: um node sem desenho, filho do corpo, que EMPURRA
    /// a posicao pro jato que vive na camada de atores. O jato acompanha sem herdar escala, sem
    /// herdar o `Modulate` do flash vermelho de dano, e -- quando o corpo morre e some -- ele apenas
    /// para de seguir e termina onde estava, em vez de sumir no meio.
    ///
    /// `UpdateRotation`/`UpdateScale` DESLIGADOS de proposito: o que se quer copiar e onde o corpo
    /// esta, nao o que fizeram com ele.
    /// ====================================================================================================
    /// </summary>
    public static void JatoDeSangue(Node2D pai, Node2D corpo)
    {
        var f = ResourceLoader.Load<SpriteFrames>(Jato);
        if (f == null || !f.HasAnimation("default")) return;

        var s = new AnimatedSprite2D
        {
            SpriteFrames = f,
            Animation = "default",
            Frame = 0,
            // GLOBAL, e nao `corpo.Position`: o `pai` e a camada de atores e o corpo e filho dela,
            // mas ler a global e o que continua certo se um dia o corpo passar a viver aninhado em
            // outro node (a montaria, o veiculo, o interior de nave).
            Position = corpo.GlobalPosition,
            ZIndex = 28,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        pai.AddChild(s);
        s.Play();

        // O ELO QUE FAZ ELE SEGUIR. Ver o cabecalho.
        var elo = new RemoteTransform2D
        {
            UseGlobalCoordinates = true,
            UpdateRotation = false,
            UpdateScale = false,
            RemotePath = s.GetPath(),
        };
        corpo.AddChild(elo);

        // O ELO MORRE COM O JATO. Sem isto ele ficaria no corpo pra sempre empurrando posicao pra um
        // node liberado -- um vazamento por membro arrancado, do tipo que so aparece em sessao longa.
        Tween t = s.CreateTween();
        t.TweenInterval(DuracaoDoJato);
        t.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(elo)) elo.QueueFree();
            s.QueueFree();
        }));
    }

    // A ESQUIVA SAIU DAQUI. Ela nao e efeito de impacto: e uma TROCA de sprite (o `flick` do DM),
    // e por isso mora num node que e filho do proprio corpo -- ver `EsquivaZanzoken`. O que estava
    // aqui era o oposto do original (tres silhuetas TINGIDAS por cima de um corpo que continuava
    // visivel) e foi deletado inteiro, junto com o shader de tinta que so ele usava.
}
