using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O BONECO DE DANO -- o `damage_indct` do BYOND, no canto superior direito.
///
/// Um corpo montado por CAMADAS, uma por regiao, e cada camada tingida pela vida DAQUELE
/// membro. E a unica leitura honesta de um corpo por partes: uma barra de vida unica esconde
/// justamente o que importa (o torso a 15% e o braco a 100% dao a mesma "media" que os dois
/// a 57%, e so um dos dois casos e um nocaute a um golpe de distancia).
///
/// A ARTE E SEMPRE A MESMA SILHUETA, TINGIDA POR CODIGO. Cada .dmi traz seis estados
/// desenhados ("healthy" ate "broken"), mas a paleta original escurece no extremo -- o
/// "Critically Injured" e um marrom sujo e o "Broken" e quase preto, entao um membro
/// destruido parecia mais CALMO que um levemente ferido. Usar um unico quadro e pintar por
/// porcentagem faz a cor acompanhar o dano de forma monotona, e todos os membros usam a
/// mesma escala. (O original chegou a essa mesma conclusao -- ver LimbHPIndicator.dm.)
/// </summary>
public partial class BodyDoll : Control
{
    private const string Arte = "res://Assets/Sprites/Misc/HUD/";

    /// <summary>O quadro que serve de silhueta. Presente e com a MESMA forma em toda peca.</summary>
    private const string Silhueta = "slightly_injured";

    /// <summary>Escala do boneco. A arte e 96x96; 1 ja e legivel numa tela moderna.</summary>
    public const int Escala = 1;

    /// <summary>
    /// Verde -> lima -> amarelo -> laranja -> vermelho -> vinho. As mesmas seis faixas e as
    /// mesmas seis cores do original, pra quem jogou o BYOND ler a mesma coisa.
    /// </summary>
    public static Color Cor(double pct) => pct switch
    {
        >= 100 => new Color("22ee22"),
        >= 80 => new Color("9bff00"),
        >= 60 => new Color("ffe400"),
        >= 40 => new Color("ff8a00"),
        >= 20 => new Color("ff2200"),
        _ => new Color("7a0000"),
    };

    /// <summary>Membro ARRANCADO le roxo -- nao "vermelho bem escuro", que se confunde com ferido.</summary>
    public static readonly Color CorDecepado = new("9b30ff");

    /// <summary>
    /// De que parte do corpo cada peca de arte fala. Braco e perna tem arte por LADO; cabeca,
    /// torso, abdomen e reprodutor tem uma so.
    ///
    /// Cerebro, orgaos, maos e pes NAO entram: nao ha arte pra eles e, no original, deixar
    /// que caissem no torso.dmi padrao carimbava uma segunda faixa de peito tingida pela vida
    /// de OUTRA coisa. O rabo tambem nao tem arte -- ele aparece na lista de texto ao lado.
    /// </summary>
    /// <summary>
    /// Cada peca de arte, na ORDEM DE DESENHO, e quais partes do corpo ela resume.
    ///
    /// A ORDEM IMPORTA NO PESCOCO: cabeca e torso se sobrepoem por uns 45 px, e o original
    /// desenha o TORSO por cima. Invertendo, o pescoco passa a mostrar a cor da cabeca, e a
    /// diferenca so aparece quando um dos dois esta ferido e o outro nao -- ou seja, aparece
    /// exatamente quando importa.
    ///
    /// BRACO E MAO CONTAM JUNTOS. Um braco tem dois membros no modelo (braco + mao) e uma
    /// regiao so no desenho: a cor sai da MEDIA dos dois, com o decepado entrando como zero.
    /// (Isto e diferente do <see cref="Jandirus.Core.Combat.Body.Vida"/>, que TIRA o decepado
    /// do denominador -- la a conta e "quanto deste corpo ainda funciona", aqui e "o quao
    /// destruida esta esta regiao".)
    ///
    /// Cerebro, orgaos e rabo NAO entram: nao ha arte pra eles. E nao ha peca "padrao" de
    /// proposito -- no original, deixar uma parte sem arte cair no torso.dmi carimbava uma
    /// segunda faixa de peito tingida pela vida de outra coisa.
    /// </summary>
    private static readonly (string Arquivo, string[] Partes)[] Pecas =
    [
        ("health_hud_head", ["Cabeca"]),
        ("health_hud_torso", ["Torso"]),
        ("health_hud_abdomen", ["Abdomen"]),
        ("health_hud_reproductive_organs", ["Reprodutor"]),
        // A ARTE JA VEM ESPELHADA: o boneco encara o jogador, entao `leftarm` desenha na
        // metade DIREITA da imagem. Mapear "esquerda = lado esquerdo da tela" troca os dois
        // bracos, e ninguem percebe ate alguem perder um.
        ("health_hud_leftarm", ["Braco esquerdo", "Mao esquerda"]),
        ("health_hud_rightarm", ["Braco direito", "Mao direita"]),
        ("health_hud_leftleg", ["Perna esquerda", "Pe esquerdo"]),
        ("health_hud_rightleg", ["Perna direita", "Pe direito"]),
    ];

    private readonly List<(TextureRect Camada, string[] Partes)> _camadas = [];
    private TextureRect _base = null!;
    private Label _extra = null!;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(96 * Escala, 96 * Escala);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Ignore;   // o boneco e leitura, nao botao

        // a MOLDURA do painel, sem tingir: e o fundo do widget no original
        AddChild(NovaCamada("health_hud", "health_hud"));

        _base = NovaCamada("health_hud_base");
        AddChild(_base);

        foreach ((string arquivo, string[] partes) in Pecas)
        {
            TextureRect t = NovaCamada(arquivo);
            AddChild(t);
            _camadas.Add((t, partes));
        }

        // o rabo nao tem arte de boneco: vira uma linha de texto embaixo
        _extra = new Label
        {
            Position = new Vector2(0, 96 * Escala - 14),
            Size = new Vector2(96 * Escala, 14),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _extra.AddThemeFontSizeOverride("font_size", 11);
        _extra.AddThemeColorOverride("font_outline_color", new Color("000000"));
        _extra.AddThemeConstantOverride("outline_size", 4);
        AddChild(_extra);

        if (GameClient.Instance is { } cli)
        {
            cli.CorpoAtualizado += Mostrar;
            if (cli.Corpo.Count > 0) Mostrar(cli.Corpo);
        }
    }

    public override void _ExitTree()
    {
        if (GameClient.Instance is { } cli) cli.CorpoAtualizado -= Mostrar;
    }

    private static TextureRect NovaCamada(string arquivo, string? estado = null)
    {
        var t = new TextureRect
        {
            Texture = Quadro(arquivo, estado),
            StretchMode = TextureRect.StretchModeEnum.Keep,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
            Scale = new Vector2(Escala, Escala),
        };
        return t;
    }

    /// <summary>
    /// Pega UM quadro do SpriteFrames convertido. O pipeline gerou os .dmi como folhas de
    /// animacao; aqui so interessa o primeiro quadro da silhueta, que ja vem como AtlasTexture
    /// e pode ir direto num TextureRect sem recortar nada na mao.
    /// </summary>
    private static Texture2D? Quadro(string arquivo, string? estado = null)
    {
        var f = ResourceLoader.Load<SpriteFrames>($"{Arte}{arquivo}.tres");
        if (f == null) { GD.PushWarning($"[hud] arte ausente: {arquivo}"); return null; }

        // ARMADILHA DE CAIXA: no .dmi o estado se chama "Slightly Injured", com espaco e
        // maiusculas; a conversao renomeou pra snake_case. Procurar pelo nome antigo devolve
        // nulo em silencio e o boneco simplesmente NAO APARECE, sem erro nenhum.
        string alvo = estado ?? Silhueta;
        if (!f.HasAnimation(alvo))
        {
            string[] nomes = f.GetAnimationNames();
            if (nomes.Length == 0) return null;
            alvo = nomes[0];
        }
        return f.GetFrameCount(alvo) > 0 ? f.GetFrameTexture(alvo, 0) : null;
    }

    private void Mostrar(List<Protocol.ParteState> corpo)
    {
        var mapa = new Dictionary<string, Protocol.ParteState>(corpo.Count);
        foreach (Protocol.ParteState p in corpo) mapa[p.Nome] = p;

        // A BASE E TINGIDA PELO TORSO, nao pela vida media.
        //
        // A arte de torso e so uma FAIXA no meio do corpo -- ombros e peito alto ficam por
        // conta da silhueta de base. Pintando a base pela media, um torso destruido aparecia
        // VERDE no peito enquanto a cabeca (que tem arte de cobertura inteira) ficava laranja.
        _base.Modulate = mapa.TryGetValue("Torso", out Protocol.ParteState torso)
            ? DaParte(torso)
            : new Color("22ee22");

        foreach ((TextureRect camada, string[] partes) in _camadas)
        {
            (bool tem, double media, bool perdido) = Regiao(mapa, partes);
            camada.Visible = tem;
            if (tem) camada.Modulate = perdido ? CorDecepado : Cor(media);
        }

        // o rabo: sem arte de boneco, mas com estado que importa demais pra esconder
        if (mapa.TryGetValue("Rabo", out Protocol.ParteState rabo))
        {
            _extra.Visible = true;
            _extra.Text = rabo.Decepado ? "rabo: ARRANCADO" : $"rabo {rabo.Vida}%";
            _extra.AddThemeColorOverride("font_color", DaParte(rabo));
        }
        else _extra.Visible = false;
    }

    /// <summary>
    /// A cor de uma REGIAO do desenho, a partir das partes que ela resume.
    ///
    /// ROXO SO PELO MEMBRO INTEIRO: perder so a MAO nao pinta o braco de roxo -- ela entra na
    /// media como zero, e o braco fica laranja. Roxo e reservado pra "esta regiao nao existe
    /// mais", que e o primeiro nome da lista (o membro-pai).
    /// </summary>
    private static (bool Tem, double Media, bool Perdido) Regiao(
        Dictionary<string, Protocol.ParteState> mapa, string[] partes)
    {
        double soma = 0;
        int n = 0;
        bool perdido = false;

        for (int i = 0; i < partes.Length; i++)
        {
            if (!mapa.TryGetValue(partes[i], out Protocol.ParteState p)) continue;
            soma += p.Decepado ? 0 : p.Vida;   // decepado CONTA como zero, nao sai da conta
            n++;
            if (i == 0 && p.Decepado) perdido = true;
        }

        return n == 0 ? (false, 0, false) : (true, soma / n, perdido);
    }

    private static Color DaParte(Protocol.ParteState p) => p.Decepado ? CorDecepado : Cor(p.Vida);
}
