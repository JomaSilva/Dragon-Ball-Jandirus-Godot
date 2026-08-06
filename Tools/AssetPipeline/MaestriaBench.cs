using Jandirus.Core.Skills;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DA MAESTRIA DE KI (`maestria`): meditar rende exp?
///
/// ============================ POR QUE ISTO PRECISA DE BANCADA ============================
/// O `niveis.json` traz 146 blocos `exp` lidos do DM, cada um com `quanto` e `cond`. O leitor
/// (`RegrasDoDisco`) abria o bloco, olhava so pra `prob` e jogava o resto fora -- entao as regras
/// CONDICIONAIS nunca creditaram nada. A mais visivel e a raiz da arvore de Ki
/// (`/datum/skill/mind/Ki_Unlocked`, Mind.dm:81): 2 por tique meditando, 2 voando, 1 parado.
///
/// Nada na tela dizia isso. A skill existia, o nivel existia, a barra nao andava -- e "a barra nao
/// anda" e indistinguivel de "a barra anda devagar" numa skill cuja barreira e 5000. So um teste
/// que compara o exp ANTES e DEPOIS separa as duas coisas.
///
/// E o teste roda no CORE puro: sem rede, sem Godot, sem servidor. A regra que ele exercita e a
/// mesma que o `TickDosNiveis` chama.
/// ========================================================================================
///
///     dotnet run --project Tools/AssetPipeline -- maestria
/// </summary>
public static class MaestriaBench
{
    private const string KiUnlocked = "/datum/skill/mind/Ki_Unlocked";

    public static int Rodar(string raiz)
    {
        string caminho = Path.Combine(raiz, "Assets", "Data", "niveis.json");
        if (!File.Exists(caminho)) { Console.WriteLine("ERRO: faltou niveis.json"); return 1; }

        int regras = RegrasDoDisco.Carregar(File.ReadAllText(caminho));
        Console.WriteLine($"regras de nivel: {regras}");
        Console.WriteLine($"exp por estado ACEITAS : {RegrasDoDisco.GanhosPorEstado}");
        Console.WriteLine($"exp por contador       : {RegrasDoDisco.GanhosPorContador}  (evento, outro caminho)");
        Console.WriteLine($"condicao NAO entendida : {RegrasDoDisco.CondicoesNaoEntendidas}  (continuam sem creditar)\n");

        int falhas = 0;
        void Conferir(bool ok, string oque)
        {
            Console.WriteLine((ok ? "  ok   " : "  FALHA") + "  " + oque);
            if (!ok) falhas++;
        }

        RegraDeNivel? r = RegrasDeNivel.Get(KiUnlocked);
        Conferir(r != null, "a regra do Ki Unlocked foi carregada");
        if (r == null) return 1;

        // ---------- as tres regras do DM chegaram? ----------
        double Quanto(RegraDeNivel.Estado e)
        {
            double t = 0;
            foreach (RegraDeNivel.GanhoPorEstado g in r.PorEstado) if (g.Quando == e) t += g.Quanto;
            return t;
        }

        Conferir(Quanto(RegraDeNivel.Estado.Meditando) == 2,
            $"MEDITANDO rende 2 por tique, como no DM ({Quanto(RegraDeNivel.Estado.Meditando)})");
        Conferir(Quanto(RegraDeNivel.Estado.Voando) == 2,
            $"VOANDO rende 2 por tique ({Quanto(RegraDeNivel.Estado.Voando)})");
        Conferir(Quanto(RegraDeNivel.Estado.Ocioso) == 1,
            $"e parado rende 1 ({Quanto(RegraDeNivel.Estado.Ocioso)})");

        // ---------- e o efetor CREDITA de verdade? ----------
        // A pergunta que a regra sozinha nao responde: ela esta escrita, mas alguem a le no tique?
        const int tiques = 20;
        var cat = new SkillCatalog();
        var livro = new SkillBook();
        livro.Dar(KiUnlocked);

        double Rodar(NiveisDeSkill.EstadoDoCorpo corpo)
        {
            var n = new NiveisDeSkill();
            n.Por(KiUnlocked, 0);
            // SEMENTE FIXA: o ganho por tempo usa `prob(50)` e uma bancada que sorteia da numeros
            // diferentes a cada rodada. O Ki Unlocked nao tem ganho por tempo, mas fixar a semente
            // deixa o teste reprodutivel se um dia tiver.
            var rng = new Random(1234);
            for (int i = 0; i < tiques; i++) n.Efetor(rng, cat, livro, corpo);
            return n.Exp(KiUnlocked);
        }

        double meditando = Rodar(new NiveisDeSkill.EstadoDoCorpo(Meditando: true, Voando: false, Treinando: false));
        double voando = Rodar(new NiveisDeSkill.EstadoDoCorpo(Meditando: false, Voando: true, Treinando: false));
        double parado = Rodar(default);

        Conferir(meditando == 2 * tiques,
            $"o efetor CREDITOU meditando: {meditando} de exp em {tiques} tiques (esperado {2 * tiques})");
        Conferir(voando == 2 * tiques,
            $"...e voando: {voando} (esperado {2 * tiques})");
        Conferir(parado == 1 * tiques,
            $"...e parado rende MENOS: {parado} (esperado {1 * tiques})");
        Conferir(meditando > parado, "meditar rende MAIS que ficar parado -- que e a regra que o dono pediu");

        // ---------- quanto tempo ate destravar o voo? ----------
        // Numero informativo, e importa: se der "quarenta horas", a regra esta ligada e o RITMO e
        // que esta errado -- e ritmo errado nao aparece em teste booleano nenhum.
        double total = 0;
        for (int nivel = 0; nivel < 50; nivel++) total += r.BarreiraEm(nivel);
        double porSegundo = 2 * (1.0 / NiveisDeSkill.SegundosPorTique);
        Console.WriteLine($"\n  --     do zero ao nivel 50 (onde o voo abre): {total:N0} de exp"
            + $" = {total / porSegundo / 3600:0.0} h meditando sem parar");

        Console.WriteLine(falhas == 0 ? "\n===== TUDO OK =====" : $"\n===== {falhas} FALHA(S) =====");
        return falhas == 0 ? 0 : 1;
    }
}
