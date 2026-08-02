using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O SELETOR DE ZONA -- o `zone_sel` do BYOND, ao lado do boneco de dano.
///
/// Um bonequinho de 32x32 onde se CLICA a regiao que se quer acertar. A zona nao garante o
/// membro: ela pesa o sorteio a favor dele (ver <see cref="Jandirus.Core.Combat.Body.Sortear"/>),
/// e e o que permite ir atras das pernas de quem foge ou da cabeca de quem esta quase caindo.
///
/// A ARMADILHA DO EIXO Y. O mapa de cliques abaixo e o do original, transcrito numero por
/// numero -- e no BYOND o `icon-y` conta DE BAIXO PRA CIMA (1 = pe, 32 = topo da cabeca),
/// enquanto no Godot a posicao do mouse conta de cima pra baixo. Sem inverter, clicar na
/// cabeca miraria a perna. E o tipo de erro que passa despercebido porque "funciona", so
/// mira sempre no lugar errado.
/// </summary>
public partial class ZonePicker : Control
{
    private const string Arte = "res://Assets/Sprites/Misc/HUD/";

    /// <summary>Lado do icone em pixels de arte. As faixas do mapa de clique sao neste espaco.</summary>
    private const int Lado = 32;

    /// <summary>Ampliacao. O original dobrava; aqui o triplo alinha a altura com o boneco (96).</summary>
    public const int Escala = 3;

    private TextureRect _base = null!, _destaque = null!;
    private SpriteFrames? _quadros;

    /// <summary>A zona escolhida, no vocabulario do zone_sel.dmi (e o nome do quadro).</summary>
    private string _selecionado = "chest";

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(Lado * Escala, Lado * Escala);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;   // este widget PRECISA receber clique
        TooltipText = "onde mirar (clique, ou 0-5)";

        _base = Camada(Quadro("screen_midnight", "zone_sel"));
        AddChild(_base);
        _destaque = Camada(null);
        AddChild(_destaque);

        _quadros = ResourceLoader.Load<SpriteFrames>($"{Arte}zone_sel.tres");
        if (_quadros == null) GD.PushWarning("[hud] zone_sel.tres ausente");

        // COMECA SEM MIRA, igual ao servidor (`ZonaMirada = 0`). Destacar "peito" de saida
        // mentiria: o golpe estaria sorteando o corpo todo enquanto o desenho promete o peito.
        Limpar();

        // as teclas 0-5 e o clique escrevem no MESMO lugar: quem muda pelo teclado ve o
        // desenho acompanhar, e quem clica ve o HUD de texto acompanhar
        if (GameClient.Instance is { } cli) cli.MiraMudou += DoTeclado;
    }

    public override void _ExitTree()
    {
        if (GameClient.Instance is { } cli) cli.MiraMudou -= DoTeclado;
    }

    private static TextureRect Camada(Texture2D? tex) => new()
    {
        Texture = tex,
        StretchMode = TextureRect.StretchModeEnum.Keep,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        MouseFilter = MouseFilterEnum.Ignore,   // o pai e quem trata o clique
        Scale = new Vector2(Escala, Escala),
    };

    private static Texture2D? Quadro(string arquivo, string estado)
    {
        var f = ResourceLoader.Load<SpriteFrames>($"{Arte}{arquivo}.tres");
        if (f == null || !f.HasAnimation(estado) || f.GetFrameCount(estado) == 0)
        {
            GD.PushWarning($"[hud] quadro ausente: {arquivo}:{estado}");
            return null;
        }
        return f.GetFrameTexture(estado, 0);
    }

    // =====================================================================
    // CLIQUE
    // =====================================================================
    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } m) return;

        int x = (int)(m.Position.X / Escala) + 1;                 // 1..32, como no original
        int y = Lado - (int)(m.Position.Y / Escala);              // INVERTIDO: ver o cabecalho

        string? z = Regiao(x, y);
        if (z != null) Aplicar(z, avisarServidor: true);
        AcceptEvent();
    }

    /// <summary>
    /// O mapa de cliques do original, transcrito de `ZoneSelect.dm`. As faixas que sobram
    /// (fora do boneco) devolvem nulo: clicar no vazio nao muda a mira.
    /// </summary>
    private static string? Regiao(int x, int y) => y switch
    {
        >= 1 and <= 9 => x switch          // pernas
        {
            >= 10 and <= 15 => "r_leg",
            >= 17 and <= 22 => "l_leg",
            _ => null,
        },
        <= 13 => x switch                  // maos e virilha
        {
            >= 8 and <= 11 => "r_arm",
            >= 12 and <= 20 => "groin",
            >= 21 and <= 24 => "l_arm",
            _ => null,
        },
        <= 22 => x switch                  // peito e bracos ate os ombros
        {
            >= 8 and <= 11 => "r_arm",
            >= 12 and <= 20 => "chest",
            >= 21 and <= 24 => "l_arm",
            _ => null,
        },
        <= 30 => x is >= 12 and <= 20 ? "head" : null,
        _ => null,
    };

    /// <summary>
    /// Do vocabulario do desenho (que separa esquerda e direita) pro do jogo (que nao separa
    /// -- mirar "o braco" nao escolhe qual, e o sorteio decide).
    /// </summary>
    private static byte ZonaDeJogo(string sel) => sel switch
    {
        "head" => 1,
        "chest" => 2,
        "groin" => 3,
        "r_arm" or "l_arm" => 4,
        "r_leg" or "l_leg" => 5,
        _ => 2,
    };

    /// <summary>Do indice do protocolo de volta pro quadro que o desenho destaca.</summary>
    private static string DoJogo(byte zona) => zona switch
    {
        1 => "head",
        2 => "chest",
        3 => "groin",
        4 => "r_arm",
        5 => "r_leg",
        _ => "",
    };

    /// <summary>
    /// A mira mudou por FORA (teclado, ou o eco do meu proprio clique). O eco importa: o
    /// jogo nao distingue braco esquerdo de direito, entao um clique no braco ESQUERDO
    /// voltaria como "bracos" e viraria destaque no DIREITO. Se o que ja esta desenhado
    /// significa a mesma zona, nao se mexe em nada.
    /// </summary>
    private void DoTeclado(byte zona)
    {
        if (_selecionado.Length > 0 && ZonaDeJogo(_selecionado) == zona) return;

        string alvo = DoJogo(zona);
        if (alvo.Length == 0) { Limpar(); return; }
        Aplicar(alvo, avisarServidor: false);
    }

    private void Limpar()
    {
        _selecionado = "";
        _destaque.Texture = null;
    }

    private void Aplicar(string sel, bool avisarServidor)
    {
        _selecionado = sel;
        _destaque.Texture = _quadros != null && _quadros.HasAnimation(sel) && _quadros.GetFrameCount(sel) > 0
            ? _quadros.GetFrameTexture(sel, 0)
            : null;

        if (avisarServidor) GameClient.Instance?.SendAim(ZonaDeJogo(sel));
    }
}
