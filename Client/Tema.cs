using Godot;

namespace Jandirus.Client;

/// <summary>
/// O VISUAL DA INTERFACE, num lugar so.
///
/// Antes cada tela montava o proprio jeito: `AddThemeColorOverride` solto, `Label` cru, barra
/// de progresso com o cinza padrao do Godot. O resultado era o que o dono viu -- parece
/// protótipo, nao jogo. Um <see cref="Theme"/> resolve isso na raiz: cor, fonte e moldura
/// descem sozinhas pra toda a arvore de Controls, e tela nova ja nasce igual as outras.
///
/// FEITO EM CODIGO, nao num .tres. E a mesma razao dos barramentos de audio: um tema salvo e
/// um arquivo binario que ninguem consegue revisar num diff, e este aqui muda junto com o
/// codigo que o usa.
///
/// A PALETA sai do jogo, nao de um gerador: laranja de gi, azul de ki, vermelho de sangue,
/// e um cinza-azulado frio de fundo pra que as duas cores quentes tenham pra onde saltar.
/// </summary>
public static class Tema
{
    // =====================================================================
    // PALETA
    // =====================================================================
    public static readonly Color Fundo = new("12141c");
    public static readonly Color Painel = new("1b1f2ae6");     // com alfa: o mundo respira atras
    public static readonly Color PainelClaro = new("252a38");
    public static readonly Color Borda = new("39405a");
    public static readonly Color BordaViva = new("f0a041");

    public static readonly Color Texto = new("e6e9f2");
    public static readonly Color TextoFraco = new("8f9ab5");
    public static readonly Color Destaque = new("f0a041");     // laranja do gi
    public static readonly Color Vida = new("d8483f");
    public static readonly Color Ki = new("3fa9d8");

    /// <summary>VIGOR: verde-oliva. Longe do vermelho da vida e do azul do Ki -- tres barras
    /// empilhadas precisam se distinguir de relance, no meio de uma luta.</summary>
    public static readonly Color Vigor = new("7fb356");

    /// <summary>
    /// NUTRICAO: amarelo-trigo. A QUARTA barra empilhada, e a mesma regra das outras tres vale --
    /// e a razao de nao ser mais um verde: ela fica logo abaixo do VIGOR, e verde sobre verde
    /// numa pilha de quatro obriga a LER o rotulo, que e justamente o que a cor existe pra evitar.
    ///
    /// O laranja do gi (<see cref="Destaque"/>) esta perto disto e nao briga: ele so pinta texto e
    /// borda, nunca o preenchimento de uma barra.
    /// </summary>
    public static readonly Color Nutricao = new("d9b64a");

    /// <summary>
    /// O KI ACIMA DOS 100% -- o pedaco da barra que passou do tanque.
    ///
    /// Azul quase branco: e o MESMO Ki, mais quente. Uma cor de outra familia (vermelho, roxo)
    /// leria como "outro recurso" ou como alarme, e transbordar de Ki nao e problema nenhum --
    /// e o power-up funcionando. A aura do jogo acende por volta do mesmo ponto (`kiratio > 1,1`).
    /// </summary>
    public static readonly Color KiExcesso = new("bfe9ff");
    public static readonly Color Perigo = new("e05a4a");
    public static readonly Color Bom = new("5fc46a");

    public const int RaioCanto = 6;

    private static Theme? _tema;

    /// <summary>O tema do jogo. Criado uma vez e compartilhado por todas as telas.</summary>
    public static Theme Atual => _tema ??= Montar();

    /// <summary>Aplica o tema numa raiz de interface. Os filhos herdam sozinhos.</summary>
    public static void Aplicar(Control raiz) => raiz.Theme = Atual;

    // =====================================================================
    // MOLDURAS
    // =====================================================================
    /// <summary>Fundo de painel: cantos arredondados, borda discreta, um respiro por dentro.</summary>
    public static StyleBoxFlat Caixa(Color? fundo = null, Color? borda = null, int margem = 12)
    {
        var s = new StyleBoxFlat
        {
            BgColor = fundo ?? Painel,
            BorderColor = borda ?? Borda,
            ContentMarginLeft = margem, ContentMarginRight = margem,
            ContentMarginTop = margem, ContentMarginBottom = margem,
        };
        s.SetBorderWidthAll(1);
        s.SetCornerRadiusAll(RaioCanto);
        return s;
    }

    private static StyleBoxFlat Chapa(Color cor, int raio = RaioCanto)
    {
        var s = new StyleBoxFlat { BgColor = cor };
        s.SetCornerRadiusAll(raio);
        return s;
    }

    private static Theme Montar()
    {
        var t = new Theme { DefaultFontSize = 14 };

        // ---- texto ----
        t.SetColor("font_color", "Label", Texto);
        t.SetColor("font_outline_color", "Label", new Color(0, 0, 0, 0.75f));
        t.SetConstant("outline_size", "Label", 0);   // contorno so onde e preciso: ver Legenda()

        // ---- botao ----
        var bNormal = Caixa(PainelClaro, Borda, 10);
        var bHover = Caixa(new Color("2f3549"), BordaViva, 10);
        var bPress = Caixa(new Color("1a1e29"), BordaViva, 10);
        var bDesab = Caixa(new Color("1b1f2a"), new Color("2a2f3f"), 10);
        t.SetStylebox("normal", "Button", bNormal);
        t.SetStylebox("hover", "Button", bHover);
        t.SetStylebox("pressed", "Button", bPress);
        t.SetStylebox("disabled", "Button", bDesab);
        t.SetStylebox("focus", "Button", Caixa(new Color(0, 0, 0, 0), BordaViva, 10));
        t.SetColor("font_color", "Button", Texto);
        t.SetColor("font_hover_color", "Button", Destaque);
        t.SetColor("font_pressed_color", "Button", Destaque);
        t.SetColor("font_disabled_color", "Button", TextoFraco);

        // ---- campo de texto ----
        var eNormal = Caixa(new Color("0e1017"), Borda, 8);
        var eFoco = Caixa(new Color("0e1017"), BordaViva, 8);
        t.SetStylebox("normal", "LineEdit", eNormal);
        t.SetStylebox("focus", "LineEdit", eFoco);
        t.SetColor("font_color", "LineEdit", Texto);
        t.SetColor("font_placeholder_color", "LineEdit", new Color(TextoFraco, 0.7f));
        t.SetColor("caret_color", "LineEdit", Destaque);
        t.SetColor("selection_color", "LineEdit", new Color(Destaque, 0.35f));

        // ---- painel ----
        t.SetStylebox("panel", "Panel", Caixa());
        t.SetStylebox("panel", "PanelContainer", Caixa());

        // ---- barra ----
        // O FUNDO DA BARRA E ESCURO E CHEIO, nao o cinza vazado do Godot: barra pela metade
        // tem que se ler de relance como "metade", e pra isso o vazio precisa ter forma.
        t.SetStylebox("background", "ProgressBar", Chapa(new Color("0b0d13"), 3));
        t.SetStylebox("fill", "ProgressBar", Chapa(Vida, 3));
        t.SetColor("font_color", "ProgressBar", Texto);

        // ---- lista / seletor ----
        t.SetStylebox("panel", "ItemList", Caixa(new Color("0e1017"), Borda, 6));
        t.SetStylebox("normal", "OptionButton", bNormal);
        t.SetStylebox("hover", "OptionButton", bHover);
        t.SetStylebox("pressed", "OptionButton", bPress);
        t.SetColor("font_color", "OptionButton", Texto);

        t.SetStylebox("panel", "PopupMenu", Caixa(new Color("161a24"), Borda, 6));
        t.SetColor("font_color", "PopupMenu", Texto);
        t.SetColor("font_hover_color", "PopupMenu", Destaque);

        t.SetStylebox("scroll", "HSlider", Chapa(new Color("0b0d13"), 3));
        t.SetStylebox("grabber_area", "HSlider", Chapa(Destaque, 3));
        t.SetStylebox("grabber_area_highlight", "HSlider", Chapa(BordaViva, 3));

        return t;
    }

    // =====================================================================
    // PECAS PRONTAS
    // =====================================================================
    /// <summary>Titulo de secao: pequeno, espacado e apagado. Serve pra separar sem gritar.</summary>
    public static Label Rotulo(string texto)
    {
        var l = new Label { Text = texto.ToUpperInvariant() };
        l.AddThemeFontSizeOverride("font_size", 11);
        l.AddThemeColorOverride("font_color", TextoFraco);
        return l;
    }

    /// <summary>
    /// Texto que fica SOBRE O MUNDO (fora de painel). Aqui o contorno preto e obrigatorio --
    /// sem ele o texto some num chao claro, e some justamente no meio de uma luta.
    /// </summary>
    public static Label Legenda(string texto, Color? cor = null, int tamanho = 14)
    {
        var l = new Label { Text = texto };
        l.AddThemeFontSizeOverride("font_size", tamanho);
        l.AddThemeColorOverride("font_color", cor ?? Texto);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        l.AddThemeConstantOverride("outline_size", 5);
        return l;
    }

    /// <summary>Painel de fundo com o visual do jogo, pronto pra receber conteudo.</summary>
    public static PanelContainer Painel1(int margem = 12)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Caixa(margem: margem));
        return p;
    }
}
