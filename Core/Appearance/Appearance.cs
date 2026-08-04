using System.Text.Json.Serialization;

namespace Jandirus.Core.Appearance;

/// <summary>
/// Uma cor de 8 bits por canal. Viaja em 3 bytes na rede.
///
/// ============================ O `JsonConstructor` NAO E ENFEITE ============================
/// Os tres campos sao `readonly`, e o `System.Text.Json` GRAVA campo readonly mas nao LE: sem o
/// atributo, a cor ia pro disco certinha e voltava PRETA. Como a tinta e somada, preto = soma
/// zero = o sprite cru -- entao o efeito era a cor escolhida sumir no primeiro relogin, calada.
///
/// Medido num round-trip real: gravou `{"R":200,"G":10,"B":30}` e leu `#000000`. Com o atributo,
/// leu `#C80A1E`. Valia pra cabelo, olho e corpo desde sempre; a cor de roupa so tornou o defeito
/// grande o bastante pra ser achado.
/// ===========================================================================================
/// </summary>
[method: JsonConstructor]
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

    /// <summary>
    /// Ate 4 pecas, como o guarda-roupa do jogo -- cada uma com uma cor OPCIONAL.
    ///
    /// A cor por peca e o pedido do dono ("cada roupa q vc selecionar pode ter a cor alterada").
    /// Nulo = a peca como o artista desenhou, que continua sendo o padrao.
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(PecaDeRoupaConverter))]
    public List<PecaDeRoupa> Roupa = [];

    public const int MaxRoupa = 4;

    public Appearance Copiar() => new()
    {
        Corpo = Corpo, Tom = Tom, CorPele = CorPele,
        Cabelo = Cabelo, CorCabelo = CorCabelo, CorOlho = CorOlho,
        Roupa = [.. Roupa],   // `PecaDeRoupa` e imutavel: copiar a lista basta
    };
}

/// <summary>
/// UMA PECA VESTIDA: o caminho do sprite e a cor que o jogador escolheu pra ela.
///
/// RECORD STRUCT, e nao classe, de proposito: ela e copiada por valor em `Appearance.Copiar()`,
/// entao tingir a roupa de um clone nao pode alcancar o dono. Com classe, o `[.. Roupa]` copiaria
/// as REFERENCIAS e as duas listas apontariam pras mesmas pecas.
/// </summary>
public readonly record struct PecaDeRoupa(string Caminho, Rgb? Cor)
{
    public PecaDeRoupa(string caminho) : this(caminho, null) { }

    // SEM CONVERSAO IMPLICITA PRA `string`, de proposito. Ela faria todo consumidor antigo
    // continuar compilando e DESCARTAR a cor em silencio -- que e o unico modo de falha que um
    // compilador nao pega. Quem quer o caminho escreve `.Caminho`.
}

/// <summary>
/// LE AS DUAS FORMAS DO SAVE: a antiga (so o caminho) e a nova (caminho + cor).
///
/// ============================ POR QUE ISTO E OBRIGATORIO ============================
/// Um save gravado antes desta funcionalidade traz `"Roupa": ["res://a.tres", ...]`. Sem este
/// conversor, ler aquilo numa lista de objetos estoura `JsonException` -- e a partir dai a cadeia
/// e brutal: o `CharacterStore.Carregar` captura a excecao e devolve NULO; o `Login` le nulo como
/// "conta nova", monta uma conta vazia e GRAVA POR CIMA do arquivo. O save nao fica ilegivel: fica
/// APAGADO, com os tres personagens dentro.
///
/// Por isso a forma antiga continua sendo aceita pra sempre. Escrever, escreve so a nova.
/// ====================================================================================
/// </summary>
public sealed class PecaDeRoupaConverter : System.Text.Json.Serialization.JsonConverter<List<PecaDeRoupa>>
{
    public override List<PecaDeRoupa> Read(ref System.Text.Json.Utf8JsonReader r, Type t,
                                           System.Text.Json.JsonSerializerOptions o)
    {
        var fora = new List<PecaDeRoupa>();
        if (r.TokenType != System.Text.Json.JsonTokenType.StartArray) return fora;

        while (r.Read() && r.TokenType != System.Text.Json.JsonTokenType.EndArray)
        {
            if (r.TokenType == System.Text.Json.JsonTokenType.String)
            {
                // FORMA ANTIGA: a peca era so o caminho, e nao havia cor pra guardar.
                fora.Add(new PecaDeRoupa(r.GetString() ?? "", null));
                continue;
            }
            if (r.TokenType != System.Text.Json.JsonTokenType.StartObject) { r.Skip(); continue; }

            string caminho = "";
            Rgb? cor = null;
            while (r.Read() && r.TokenType != System.Text.Json.JsonTokenType.EndObject)
            {
                if (r.TokenType != System.Text.Json.JsonTokenType.PropertyName) continue;
                string campo = r.GetString() ?? "";
                r.Read();
                if (string.Equals(campo, nameof(PecaDeRoupa.Caminho), StringComparison.OrdinalIgnoreCase))
                    caminho = r.GetString() ?? "";
                else if (string.Equals(campo, nameof(PecaDeRoupa.Cor), StringComparison.OrdinalIgnoreCase))
                    cor = r.TokenType == System.Text.Json.JsonTokenType.Null
                        ? null
                        : System.Text.Json.JsonSerializer.Deserialize<Rgb>(ref r, o);
                else r.Skip();
            }
            if (caminho.Length > 0) fora.Add(new PecaDeRoupa(caminho, cor));
        }
        return fora;
    }

    public override void Write(System.Text.Json.Utf8JsonWriter w, List<PecaDeRoupa> v,
                               System.Text.Json.JsonSerializerOptions o)
    {
        w.WriteStartArray();
        foreach (PecaDeRoupa p in v)
        {
            w.WriteStartObject();
            w.WriteString(nameof(PecaDeRoupa.Caminho), p.Caminho);
            w.WritePropertyName(nameof(PecaDeRoupa.Cor));
            if (p.Cor is { } c) System.Text.Json.JsonSerializer.Serialize(w, c, o);
            else w.WriteNullValue();
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }
}
