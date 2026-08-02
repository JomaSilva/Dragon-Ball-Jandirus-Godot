namespace Jandirus.Core.Appearance;

/// <summary>Uma cor de 8 bits por canal. Viaja em 3 bytes na rede.</summary>
public readonly struct Rgb(byte r, byte g, byte b)
{
    public readonly byte R = r, G = g, B = b;

    public static readonly Rgb Preto = new(0, 0, 0);
    public static readonly Rgb Branco = new(255, 255, 255);

    public bool Neutra => R == 0 && G == 0 && B == 0;

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// A APARENCIA de um personagem: tudo que se ve, e nada que se calcula.
///
/// Sao poucos campos porque no BYOND tambem eram: o corpo e um ARQUIVO escolhido entre os
/// permitidos da raca, o cabelo e um NOME de estilo, o olho e um unico sprite, e a roupa e
/// uma pilha de ate quatro pecas. Tudo o mais (cabelo de Super Saiyajin, aura, cor de
/// transformacao) e DERIVADO em jogo -- nao e escolha de criacao e nao entra aqui.
///
/// COR E OPCIONAL, E ISSO IMPORTA. Os sprites JA VEM COLORIDOS: o cabelo preto tem os
/// realces desenhados, o olho tem o brilho. Quando ha cor escolhida, ela e SOMADA por cima
/// (o `ICON_ADD` do BYOND), que clareia sem apagar o desenho. Multiplicar -- o reflexo
/// natural em Godot -- destroi: preto vezes qualquer coisa e preto, e o cabelo vira uma
/// mancha sem detalhe. Por isso cada cor aqui e ANULAVEL: nulo = a cor natural do sprite.
///
/// COMO SE DESENHA: uma pilha de camadas, todas com a MESMA animacao, direcao e QUADRO. E o
/// que o BYOND fazia com `vis_contents` + `VIS_INHERIT_DIR|VIS_INHERIT_ICON_STATE`.
///
///     corpo (2)  ->  roupa (3)  ->  cabelo (4)  ->  chapeu (5)  ->  aura (7)
/// </summary>
public sealed class Appearance
{
    /// <summary>Indice do corpo dentro da lista permitida pra raca+genero.</summary>
    public int Corpo;

    /// <summary>
    /// Tom, quando a raca escolhe por TOM e nao por arquivo (Namekuseijin). Indice na lista
    /// de tons do catalogo.
    /// </summary>
    public int Tom;

    /// <summary>Cor livre do corpo (Majin, Kai). Nulo = o proprio sprite.</summary>
    public Rgb? CorPele;

    /// <summary>Nome do estilo, como no catalogo. "Bald" = careca.</summary>
    public string Cabelo = "Bald";

    /// <summary>Nulo = a cor natural do sprite. E o padrao, e o que o Saiyajin usa sempre.</summary>
    public Rgb? CorCabelo;

    /// <summary>Nulo = o olho como foi desenhado.</summary>
    public Rgb? CorOlho;

    /// <summary>Ate 4 pecas, como o guarda-roupa do jogo. Sao caminhos res:// do catalogo.</summary>
    public List<string> Roupa = [];

    public const int MaxRoupa = 4;

    public Appearance Copiar() => new()
    {
        Corpo = Corpo, Tom = Tom, CorPele = CorPele,
        Cabelo = Cabelo, CorCabelo = CorCabelo, CorOlho = CorOlho,
        Roupa = [.. Roupa],
    };
}
