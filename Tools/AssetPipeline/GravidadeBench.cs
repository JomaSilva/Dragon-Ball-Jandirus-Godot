using System.Text.RegularExpressions;
using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DA GRAVIDADE (`gravidade`): cruza TODA zona do manifesto com a ficha que ela acha.
///
/// ============================ POR QUE ISTO PRECISA DE BANCADA ============================
/// A gravidade nao aparece na tela. Ela multiplica o ganho de treino e pesa no poder efetivo --
/// entao uma zona que erra a ficha e cai no padrao (1) nao acusa nada: o jogador so treina devagar
/// para sempre, e o unico sinal seria alguem reclamar comparando com outro.
///
/// O extrator entrega os nomes do `switch(Planet)` do DM ("Icer Planet", "Big Gete Star") e o
/// consumidor pergunta pelo nome da ZONA ("Icer", "Big_Geti_Star"). Sao dois espacos de chaves, e a
/// familia de defeito e a mesma do `default = 6` dos estilos: extrator e consumidor falando linguas
/// diferentes, com um padrao silencioso no meio.
/// ========================================================================================
///
///     dotnet run --project Tools/AssetPipeline -- gravidade
/// </summary>
public static class GravidadeBench
{
    public static int Rodar(string raiz)
    {
        string planetas = Path.Combine(raiz, "Assets", "Data", "planetas.json");
        string manifesto = Path.Combine(raiz, "Assets", "Maps", "manifest.json");
        if (!File.Exists(planetas) || !File.Exists(manifesto))
        {
            Console.WriteLine("ERRO: faltou planetas.json ou manifest.json");
            return 1;
        }

        CatalogoDePlanetas cat = CatalogoDePlanetas.Parse(File.ReadAllText(planetas));
        string[] zonas = [.. Regex.Matches(File.ReadAllText(manifesto), @"""zona"":\s*""([^""]+)""")
            .Select(m => m.Groups[1].Value).Distinct().OrderBy(s => s, StringComparer.Ordinal)];

        Console.WriteLine($"fichas: {cat.Total} | zonas alcancaveis: {zonas.Length}\n");

        var achadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int semFicha = 0;
        foreach (string z in zonas)
        {
            FichaDePlaneta f = cat.De(z);
            // A FICHA PADRAO SE DENUNCIA PELO NOME: `De` devolve `new FichaDePlaneta { Nome = zona }`
            // quando nao acha, entao o nome dela e o da ZONA e nao o do DM.
            bool achou = !string.Equals(f.Nome, z, StringComparison.Ordinal) || cat.Todas.Any(x => x.Nome == z);
            if (achou) achadas.Add(f.Nome);
            else semFicha++;
            Console.WriteLine($"  {(achou ? "ok    " : "SEM   ")} {z,-28} -> {(achou ? f.Nome : "(padrao)"),-28} grav {f.Gravidade}");
        }

        string[] orfas = [.. cat.Todas.Select(f => f.Nome).Where(n => !achadas.Contains(n))
            .OrderBy(s => s, StringComparer.Ordinal)];

        Console.WriteLine($"\nzonas SEM ficha: {semFicha}");
        Console.WriteLine($"fichas que nenhuma zona alcanca: {orfas.Length}"
                          + (orfas.Length > 0 ? "  -> " + string.Join(", ", orfas) : ""));

        // ============================ OS VALORES, E NAO A CONTAGEM ============================
        // "Nenhuma zona sem ficha" seria um teste ruim: cinco zonas -- as duas cavernas, o Templo, a
        // area generica e a vazia -- NAO estao no `switch(Planet)` do DM, e cair no padrao 1 e o
        // comportamento CERTO (`else Planetgrav=1`). Um teste que exigisse ficha pra todas obrigaria
        // a inventar dado que o original nao tem.
        //
        // O que se confere sao os NUMEROS que mudam o jogo, um a um, contra a linha do DM.
        // ====================================================================================
        (string Zona, double Esperado, string Onde)[] alvos =
        [
            ("Hyperbolic_Time_Chamber", 10,  "Gravity.dm:95  if(13) Planetgrav=10"),
            ("Vegeta",                  10,  "Gravity.dm:104 New Vegeta"),
            ("Icer",                    15,  "Gravity.dm:105 Icer Planet"),
            ("Big_Geti_Star",           25,  "Gravity.dm:113 Geti Star"),
            ("God_Realm",               500, "Gravity.dm:101 God Realm"),
            ("Hell",                    10,  "Gravity.dm:110 Hell"),
            ("Arlia",                   2,   "Gravity.dm:114 Arlian Planet"),
            ("Earth",                   1,   "Gravity.dm:102 Earth"),
            ("Espaco",                  0,   "Gravity.dm:106 Space -- a zona VIVA, nao o mapa z26"),
        ];

        Console.WriteLine();
        int errados = 0;
        foreach ((string zona, double esperado, string onde) in alvos)
        {
            double achou = cat.De(zona).Gravidade;
            bool ok = Math.Abs(achou - esperado) < 0.001;
            if (!ok) errados++;
            Console.WriteLine($"  {(ok ? "ok    " : "ERRADO")} {zona,-26} grav {achou,-6} esperado {esperado,-6} ({onde})");
        }

        Console.WriteLine($"\nvalores errados: {errados} | zonas no padrao 1 por ausencia no DM: {semFicha}");
        return errados == 0 ? 0 : 1;
    }
}
