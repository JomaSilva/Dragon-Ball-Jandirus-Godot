using System.Text.Json;
using Jandirus.Core.Appearance;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DA COR DE ROUPA (`cor`), na parte que nao precisa do jogo aberto: o DISCO.
///
/// ============================ O QUE SO O TESTE RESPONDE ============================
/// Escolher cor e um gesto de meio segundo; descobrir que ela nao voltou leva um relogin. E a
/// pergunta que mais importa nem e a da funcionalidade nova:
///   * as cores que JA existiam (cabelo, olho, pele) sobrevivem ao disco? (nao sobreviviam)
///   * um save gravado ANTES desta mudanca ainda carrega? (senao o `Login` o apaga)
///   * a cor por peca vai e volta, e a peca SEM cor continua sem?
///   * copiar uma aparencia copia mesmo, ou as duas apontam pra mesma roupa?
/// ==================================================================================
///
///     dotnet run --project Tools/AssetPipeline -- cor
/// </summary>
public static class RoupaBench
{
    public static int Rodar()
    {
        int falhas = 0;
        void Conferir(bool ok, string oque)
        {
            Console.WriteLine((ok ? "  ok     " : "  FALHA  ") + oque);
            if (!ok) falhas++;
        }

        // As MESMAS opcoes do CharacterStore -- testar com outras testaria outro programa.
        var opcoes = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };

        var a = new Appearance
        {
            Cabelo = "Spiky",
            CorCabelo = new Rgb(200, 10, 30),
            CorOlho = new Rgb(1, 2, 3),
            CorPele = new Rgb(9, 9, 9),
            Roupa = [new("res://a.tres", new Rgb(60, 110, 220)), new("res://b.tres", null)],
        };

        // ---- 1. as cores que ja existiam ----
        Appearance? v = JsonSerializer.Deserialize<Appearance>(JsonSerializer.Serialize(a, opcoes), opcoes);
        Conferir(v?.CorCabelo is { R: 200, G: 10, B: 30 },
            $"a cor do CABELO volta do disco inteira ({v?.CorCabelo})");
        Conferir(v?.CorOlho is { B: 3 } && v?.CorPele is { R: 9 },
            $"as cores de olho e pele tambem ({v?.CorOlho} / {v?.CorPele})");

        // ---- 2. a cor por peca ----
        Conferir(v?.Roupa.Count == 2, $"as duas pecas voltam ({v?.Roupa.Count})");
        Conferir(v?.Roupa[0].Caminho == "res://a.tres" && v.Roupa[0].Cor is { R: 60, G: 110, B: 220 },
            $"a peca tingida volta com a cor ({v?.Roupa[0].Cor})");
        Conferir(v?.Roupa[1].Cor == null, "e a peca sem cor volta SEM cor (nulo = o sprite cru)");

        // ---- 3. o save ANTIGO ----
        // Sem o conversor isto estoura `JsonException`, o `CharacterStore.Carregar` devolve nulo,
        // e o `Login` le a falha como "conta nova" e GRAVA POR CIMA -- apagando os tres slots.
        const string antigo = """
            {"Corpo":0,"Tom":0,"CorPele":null,"Cabelo":"Bald","CorCabelo":null,"CorOlho":null,
             "Roupa":["res://velha1.tres","res://velha2.tres"]}
            """;
        Appearance? velho = null;
        string erro = "";
        try { velho = JsonSerializer.Deserialize<Appearance>(antigo, opcoes); }
        catch (Exception e) { erro = e.Message; }
        Conferir(velho != null, "um save do formato ANTIGO carrega sem estourar"
                                + (erro.Length > 0 ? " -- " + erro : ""));
        Conferir(velho?.Roupa.Count == 2 && velho.Roupa[0].Caminho == "res://velha1.tres",
            $"e as pecas antigas chegam inteiras ({velho?.Roupa.Count})");
        Conferir(velho?.Roupa.TrueForAll(p => p.Cor == null) == true, "nenhuma peca antiga inventa cor");

        // ---- 4. copiar nao compartilha ----
        // O clone da Dimensao Mental dividia a MESMA instancia de Appearance com o dono.
        Appearance copia = a.Copiar();
        copia.Roupa[0] = new PecaDeRoupa("res://a.tres", new Rgb(1, 1, 1));
        Conferir(a.Roupa[0].Cor is { R: 60 }, "tingir a COPIA nao alcanca o original");

        Console.WriteLine(falhas == 0 ? "\n===== TUDO OK =====" : $"\n===== {falhas} FALHA(S) =====");
        return falhas == 0 ? 0 : 1;
    }
}
