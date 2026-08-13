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

    /// <summary>
    /// OS CORPOS DAS FORMAS DO FROST DEMON, um por degrau, na ordem que
    /// <see cref="Races.FormasDeFrost.DegrausDe"/> devolve. Vazio pra todas as outras racas.
    ///
    /// ============================ POR QUE ISTO E APARENCIA ============================
    /// Um Frost Demon nao tem "um corpo": ele tem tres (ou sete, se for mutante), e trocar de forma
    /// TROCA O SPRITE inteiro. Nao ha um passo de "escolha seu corpo" pra ele no original -- o passo
    /// de aparencia DELE e escolher quais corpos serao as formas, e foi o que o dono confirmou:
    /// "a aparencia do frost demon e onde ele escolhe as formas dele".
    ///
    /// Guardar aqui, e nao no estado de combate, e o que faz a escolha SOBREVIVER: ela e feita uma
    /// vez na criacao, viaja no mesmo pacote do cabelo e da roupa, e e salva com o personagem. O
    /// que o combate guarda e so em QUAL dos degraus o corpo esta agora.
    /// ==================================================================================
    /// </summary>
    public List<string> FormasDeFrost = [];

    /// <summary>
    /// ============================ A COR DA CHAMA DESTE PERSONAGEM ============================
    /// A aura da BASE (e a do Mistico, ver `Catalogo.ChamaDoJogador`) e pessoal: no original ela e
    /// sorteada no nascimento -- `AuraR/G/B = rand(0,255)` (`CharacterCreation.dm:25-27`), somada
    /// (`ICON_ADD`) sobre a `colorablebigaura` em `:32-40`.
    ///
    /// NULO NAO E "sem cor", E "ainda nao escrita": quem le DERIVA de nome + instante de criacao
    /// (<see cref="CorDeAura.De"/>), que e o par que todo save ja tem. Por isso um personagem de
    /// antes desta funcionalidade nao precisa de ramo de migracao nenhum -- ele cai na MESMA regra
    /// do personagem novo, e a cor dele e estavel entre logins porque a conta e pura.
    ///
    /// E POR ISSO O CAMPO EXISTE MESMO SENDO DERIVAVEL: ele e o OVERRIDE. O DM deixa o jogador
    /// trocar a cor depois (o verb `Aura_Color()`, `CharacterCreation.dm:129-151`), e esse dia
    /// escreve aqui sem que ninguem mais precise saber. E a mesma semantica de
    /// <see cref="CorCabelo"/> / <see cref="CorOlho"/>: nulo = o padrao, preenchido = a escolha.
    ///
    /// ANULAVEL TAMBEM POR SEGURANCA DE SAVE, e isso foi MEDIDO nas duas direcoes (save velho no
    /// binario novo le nulo; save novo no binario velho ignora a propriedade desconhecida). O que
    /// derruba uma conta neste projeto nao e campo novo: e mudar o TIPO de algo que ja esta no
    /// disco -- ver o cabecalho do <see cref="PecaDeRoupaConverter"/>.
    /// =====================================================================================
    /// </summary>
    public Rgb? CorAura;

    public Appearance Copiar() => new()
    {
        Corpo = Corpo, Tom = Tom, CorPele = CorPele,
        Cabelo = Cabelo, CorCabelo = CorCabelo, CorOlho = CorOlho,
        Roupa = [.. Roupa],   // `PecaDeRoupa` e imutavel: copiar a lista basta
        FormasDeFrost = [.. FormasDeFrost],
        // O CLONE HERDA A CHAMA DO DONO, que e o `A.AuraR = AuraR` do `CopyMaker.dm:98`. Esquecer
        // esta linha nao quebra nada: faz o clone nascer com a cor do fallback, calado, e isso so
        // aparece jogando -- este metodo lista campo a campo e e o unico lugar onde isso pode
        // acontecer.
        CorAura = CorAura,
    };
}

/// <summary>
/// ============================ O SORTEIO DA COR DA AURA, COMO NO ORIGINAL ============================
/// O DM sorteia `rand(0,255)` nos tres canais (`CharacterCreation.dm:25-27`) e SOMA o resultado na
/// folha `colorablebigaura` (`ICON_ADD`, `:32-40`). A folha tem tom dominante `c8c8c8` -- 6.278 dos
/// 17.564 pixels --, entao a soma satura pro branco na maioria dos sorteios. **E por isso que a aura
/// mais comum no original e a branca**: nao ha peso nenhum favorecendo o branco, e a consequencia de
/// somar um sorteio medio numa folha que ja esta a 200.
///
/// ============================ MAS O NUMERO DO DM NAO SE PORTA, O RESULTADO SIM ============================
/// Portar `rand(0,255)` cru pra ca daria chamas quase PRETAS. O DM SOMA e este port MULTIPLICA:
/// `Assets/Shaders/Aura.gdshader:46` faz `cor * i`, com `i` normalizado por `PicoDaFolha = 0.784`
/// -- que e 200/255, o mesmo `c8` da folha. Ou seja no pixel dominante o shader devolve `cor` puro,
/// e `cor` tem que ser o RESULTADO da soma do DM, nao a parcela dela.
///
/// A conta e <c>min(255, 200 + rand(0,255))</c> por canal, e o `200` nao e chute: e a mesma
/// constante que o shader ja nomeia. Media ~247 por canal, ~79% de chance de um canal estourar em
/// 255 e ~49% de os tres estourarem -- metade branca pura, o resto com uma tintura leve. E a
/// distribuicao do original, medida e nao inventada.
/// ==================================================================================================
/// </summary>
public static class CorDeAura
{
    /// <summary>
    /// O TOM DOMINANTE DA FOLHA, em 8 bits. E o `c8` de `c8c8c8` -- e o mesmo numero que o
    /// `Aura.gdshader` guarda normalizado (`PicoDaFolha = 0.784 = 200/255`). Escrito nos dois
    /// lugares porque um e GLSL e o outro e C#; se um dia a folha for retrabalhada, os dois mudam
    /// juntos ou a cor sorteada deixa de bater com o pixel desenhado.
    /// </summary>
    public const int PicoDaFolha = 200;

    /// <summary>
    /// O sorteio, a partir de uma semente qualquer. Ver o cabecalho da classe pra a conta.
    ///
    /// `Random` semeado (e nao um corte de bits da semente) pelo mesmo motivo do
    /// `LimiaresPessoais.Sorteador`: e o gerador que este Core ja usa pra sortear ficha, ele e
    /// estavel entre maquinas e execucoes, e a uniformidade dele nao depende de o misturador ter
    /// avalanche boa nos bits que eu escolhesse cortar.
    /// </summary>
    public static Rgb Sortear(ulong semente)
    {
        var r = new Random(unchecked((int)(semente ^ (semente >> 32))));
        return new Rgb(Canal(r), Canal(r), Canal(r));
    }

    private static byte Canal(Random r) => (byte)Math.Min(255, PicoDaFolha + r.Next(0, 256));

    /// <summary>
    /// A COR A PARTIR DA SEMENTE DE UM CORPO -- a de um personagem salvo ou a de um NPC (que e o
    /// LUGAR onde ele nasceu, e nao um save).
    ///
    /// O `Hash64("aura")` no meio e o mesmo desenho de "um gerador por campo" do
    /// `LimiaresPessoais.Sorteador`: sem ele, dois sistemas que sorteassem da semente crua do mesmo
    /// corpo andariam juntos pra sempre (o NPC de cabelo tal teria sempre a aura tal).
    /// </summary>
    public static Rgb DeSemente(ulong semente) =>
        Sortear(Jandirus.Core.World.Espaco.Misturar(
            semente, Jandirus.Core.World.Espaco.Hash64("aura"), 0xA57E_1B2C_4D3F_9E01UL));

    /// <summary>
    /// A COR DE UM PERSONAGEM, DERIVADA DO QUE JA O IDENTIFICA NO SAVE.
    ///
    /// Sortear na CARGA seria uma cor nova a cada login. Guardar um campo obrigatorio deixaria o
    /// personagem antigo sem resposta. A saida e a que este repo ja usou uma vez, e esta escrita no
    /// cabecalho de <see cref="Jandirus.Core.Forms.LimiaresPessoais.SementeDe"/>: nome + instante de criacao e a
    /// dupla que TODO save tem, e por isso a semente e "reconstruivel se o campo dela se perder num
    /// save antigo".
    ///
    /// NAO SE USA `Limiares.Semente` pra isto, por dois motivos medidos: ela e `0` em save anterior
    /// aos limiares, e na criacao ela e montada com `nome.GetHashCode()` (`GameServer.cs:1629`) --
    /// que o proprio `LimiaresPessoais:296` documenta como randomizado por processo, ou seja NAO
    /// reproduzivel; aquele campo so sobrevive porque foi gravado.
    ///
    /// </summary>
    public static Rgb De(string nome, long criadoEm) =>
        DeSemente(Jandirus.Core.Forms.LimiaresPessoais.SementeDe(nome, criadoEm));
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
