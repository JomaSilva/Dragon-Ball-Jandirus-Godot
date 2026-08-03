using Godot;

namespace Jandirus.Client;

/// <summary>
/// A LUZ QUE UM PERSONAGEM EMITE. Desligada por padrao, e essa e a regra:
///
///   personagem comum NAO BRILHA. Quem brilha e quem esta TRANSFORMADO.
///
/// Antes cada jogador carregava um <see cref="PointLight2D"/> fixo -- todo mundo andava com
/// uma lanterna colada no corpo, o que apagava a diferenca entre um lutador qualquer e um
/// Super Saiyajin aceso. O mundo agora e claro por conta propria (o ambiente e dia) e esta
/// luz volta a significar alguma coisa.
///
/// O node existe sempre e nasce DESLIGADO: acender e trocar de cor tem que ser instantaneo
/// no meio de uma transformacao, e criar o PointLight2D na hora custaria um engasgo bem no
/// quadro em que o jogador esta olhando.
/// </summary>
public partial class Aura : Node2D
{
    /// <summary>Raio da aura em pixels. Cinco tiles: da pra ver de longe que alguem acendeu.</summary>
    private const int Raio = 160;

    private PointLight2D _luz = null!;

    /// <summary>
    /// O DESENHO da aura -- o `colorablebigaura` do original, tingido com a cor da forma.
    ///
    /// A LUZ SOZINHA NAO E A AURA. Ela ilumina o cenario em volta, mas nao poe nada em volta do
    /// CORPO: o Super Saiyajin ficava com o chao dourado e sem os fachos de energia que sao a
    /// imagem da transformacao. O sprite e a aura; a luz e o que ela faz no mundo. Estar
    /// transformado acende as duas -- e so estar transformado (ver <see cref="SpriteDeAura"/>).
    /// </summary>
    private SpriteDeAura _desenho = null!;

    public override void _Ready()
    {
        _desenho = new SpriteDeAura { Name = "Desenho" };
        AddChild(_desenho);

        _luz = new PointLight2D
        {
            Name = "Luz",
            Texture = Radial(Raio),
            Enabled = false,
            ShadowEnabled = true,
            Energy = 0,
            // ATRAS do personagem: uma luz por cima lava o sprite e apaga o desenho da roupa
            ZIndex = -5,
        };
        AddChild(_luz);
    }

    /// <summary>
    /// Acende (ou apaga) a aura. <paramref name="forca"/> 0 apaga; 1 e uma forma comum;
    /// acima disso a luz cresce junto com o tier.
    ///
    /// E o gancho que as transformacoes vao usar -- SSJ dourado, Kaioken vermelho, Blue azul.
    /// Enquanto elas nao existem, ninguem chama isto e ninguem brilha, que e o certo.
    /// </summary>
    public void Acender(Color cor, float forca = 1f)
    {
        if (forca <= 0) { Apagar(); return; }

        // AS DUAS CAMADAS JUNTAS -- e so aqui elas andam juntas. Quem esta so reunindo energia
        // recebe o desenho pela CargaVisual e NENHUMA luz.
        _desenho.Definir(true, cor, Mathf.Clamp(0.8f + forca * 0.35f, 0.5f, 2f));

        _luz.Color = cor;
        _luz.Energy = Mathf.Clamp(forca, 0.1f, 4f);
        _luz.TextureScale = Mathf.Clamp(0.7f + forca * 0.3f, 0.5f, 2.5f);
        _luz.Enabled = true;
    }

    public void Apagar()
    {
        _desenho.Definir(false, Colors.White);
        _luz.Enabled = false;
        _luz.Energy = 0;
    }

    /// <summary>
    /// Textura de luz radial gerada em codigo: nao depende de arte importada.
    ///
    /// PUBLICA porque a <see cref="CargaVisual"/> quer a MESMA textura. Duas receitas de degrade
    /// dariam duas luzes com queda de borda diferente no mesmo corpo -- e a de carga e justamente
    /// a que acende ao lado da de forma.
    /// </summary>
    public static GradientTexture2D Radial(int raio)
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
