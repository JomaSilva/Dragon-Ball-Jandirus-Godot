using Godot;

namespace Jandirus.Client;

/// <summary>
/// A BARRINHA SOBRE A CABECA. Vida de quem esta na frente -- e a unica coisa da ficha alheia
/// que se ve sem scouter, porque um corpo machucado se ve mesmo.
///
/// So aparece quando ha o que mostrar: com o corpo inteiro ela some, senao a tela vira uma
/// floresta de barras verdes em cima de gente que esta bem.
///
/// Desenhada em codigo (`_Draw`) e nao com nodes de UI: sao dois retangulos, e um Control
/// dentro do mundo 2D traria layout, tema e ancoragem pra resolver o que duas linhas resolvem.
/// </summary>
public partial class HealthBar : Node2D, ISobeComOCorpo
{
    private const float Largura = 26f, Altura = 3f;

    /// <summary>Acima da cabeca do sprite de 32px (que e `Centered`, entao o topo fica em -16).</summary>
    internal const float AlturaBase = -22f;

    /// <summary>
    /// O DESLOCAMENTO DE ALTITUDE, escrito pela varredura do voo (<see cref="SubirComOVoo.Aplicar"/>),
    /// que chega aqui porque esta classe declara <see cref="ISobeComOCorpo"/>.
    ///
    /// PROPRIEDADE, e nao `Position` na mao: os dois chamadores escreviam direto na posicao e
    /// APAGAVAM a <see cref="AlturaBase"/> junto -- a barra caia da cabeca pro umbigo assim que a
    /// pessoa saia do chao. Nunca apareceu porque no chao o deslocamento e zero e a conta dava no
    /// mesmo por acidente. O balao de fala nasceu da mesma lista e teria herdado o defeito.
    /// </summary>
    public Vector2 Deslocamento
    {
        set => Position = new Vector2(0, AlturaBase) + value;
    }

    /// <summary>Vida de 0 a 1. Acima do limiar de "cheia" a barra nao aparece.</summary>
    public float Vida
    {
        get => _vida;
        set
        {
            float v = Mathf.Clamp(value, 0f, 1f);
            if (Mathf.IsEqualApprox(v, _vida)) return;
            _vida = v;
            Visible = v < 0.995f;
            QueueRedraw();
        }
    }

    private float _vida = 1f;

    public override void _Ready()
    {
        Position = new Vector2(0, AlturaBase);
        ZIndex = 20;
        Visible = false;
    }

    public override void _Draw()
    {
        var fundo = new Rect2(-Largura / 2 - 1, -1, Largura + 2, Altura + 2);
        DrawRect(fundo, new Color(0, 0, 0, 0.6f));

        // verde -> amarelo -> vermelho conforme cai. A cor e o aviso: numero ninguem le no
        // meio de uma luta, cor mudando o jogador percebe pelo canto do olho.
        Color cor = _vida > 0.5f
            ? new Color(1f - (_vida - 0.5f) * 2f * 0.55f, 0.78f, 0.24f)
            : new Color(0.85f, 0.18f + _vida * 1.2f, 0.18f);

        DrawRect(new Rect2(-Largura / 2, 0, Largura * _vida, Altura), cor);
    }
}
