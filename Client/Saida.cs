using Godot;

namespace Jandirus.Client;

/// <summary>
/// FECHAR O JOGO. Um caminho so, pras tres portas que existem.
///
/// ============================ POR QUE ISTO NAO E `GetTree().Quit()` SOLTO ============================
/// Era. O `Quit()` aparecia num unico lugar de producao (o botao "Fechar o jogo" do menu de pausa) e
/// fazia `srv.Stop()` antes -- **e o `Stop` nao gravava nada**. Quem hospeda e fecha o jogo levava
/// junto ate dois minutos de progresso de todos os conectados (ver `GameServer.SalvarEParar`), e
/// ninguem avisava o servidor de que este cliente estava indo embora.
///
/// Agora sao tres passos, nesta ordem, e a ordem importa:
///
///   1. **O SERVIDOR LOCAL GRAVA E DESLIGA.** Primeiro porque o meu proprio personagem esta na lista
///      dele: se o passo 2 viesse antes, o save do meu corpo dependeria da desconexao chegar antes
///      do processo morrer.
///   2. **O CLIENTE SE DESPEDE.** O servidor pode ser de OUTRA PESSOA -- ai a saida limpa e a
///      diferenca entre ele me tirar da zona agora e me deixar de pe ate o timeout, apanhando.
///   3. **O PROCESSO SAI.**
///
/// ============================ O X DA JANELA E A MESMA PORTA ============================
/// Varri o projeto e nao havia handler nenhum de fechar janela: o X e o Alt+F4 nao passavam por
/// `Stop`, nem por `Persistir`, nem por `Desconectar` -- so matavam o processo. Do ponto de vista de
/// quem joga e o MESMO gesto do botao "Sair do jogo", entao e o mesmo caminho: o `Boot` desliga o
/// `auto_accept_quit` e manda o fechamento pra ca. Ver `Boot._Notification`.
/// ==================================================================================
/// </summary>
public static class Saida
{
    /// <summary>
    /// Ja estamos saindo? O botao e o X podem chegar quase juntos (clicar em "Sair" e no X enquanto
    /// o save roda), e gravar duas vezes no meio de um `Stop` e como se fecha arquivo pela metade.
    /// </summary>
    private static bool _saindo;

    /// <summary>Só pra bancada: desfaz a trava do <see cref="_saindo"/> entre rodadas.</summary>
    public static void RearmarDeTeste() => _saindo = false;

    /// <summary>Já pediram pra sair? Lido pela bancada -- em teste ninguem deixa o processo morrer.</summary>
    public static bool SaindoDeTeste => _saindo;

    /// <param name="motivo">De qual porta veio, em uma frase. So vai pro log.</param>
    public static void Encerrar(SceneTree? arvore, string motivo)
    {
        if (_saindo) return;
        _saindo = true;
        GD.Print($"[saida] fechando o jogo ({motivo})");

        // ============================ O AQUECIMENTO PRIMEIRO -- o travamento do lobby ============================
        // O lobby pede 284 recursos ao `ResourceLoader.LoadThreadedRequest` nos primeiros ~2,3 s
        // (`Aquecimento`). Fechar com essas cargas ainda no ar derruba a arvore por cima de um
        // carregador que esta lendo em outra thread: o tileset sai com "Parse Error", 380 RIDs vazam
        // -- e na maquina do dono o processo simplesmente parou de responder (AppHangB1 no Event Log,
        // tres vezes em 2026-09-04, todas com o X apertado nos primeiros segundos do lobby, antes de
        // a linha `[aquece]` sair; *"fechar o jogo no menu/lobby o jogo crasha ao inves de fechar"*).
        //
        // `Concluir` ESPERA o que falta (`LoadThreadedGet` bloqueia ate cada item voltar): no pior
        // caso os ~2 s do aquecimento inteiro, e depois do aquecimento e um retorno imediato. Fora do
        // `try` de proposito: ele nao lanca, e um lobby que ainda esta carregando nao tem servidor
        // nem cliente pra desligar. Medido pela `--diagsaida --saidalobby <s>` (`RoboDeSaida`).
        // ==========================================================================================================
        Aquecimento.Concluir();

        try
        {
            if (Jandirus.Server.GameServer.Instance is { Running: true } srv)
            {
                // GRAVAR SO SE ALGUEM CHEGOU A JOGAR AQUI. Ver `Boot.SessaoDeJogador`: as bancadas
                // mexem no mundo em memoria de proposito, e o X apertado no meio de uma delas nao
                // pode gravar esse estado por cima do mundo do dono. Sem sessao de jogador nao ha
                // nada de jogador pra salvar -- e o servidor ainda assim desce limpo.
                if (Boot.SessaoDeJogador) srv.SalvarEParar();
                else srv.Stop();
            }
            GameClient.Instance?.Desconectar();
        }
        catch (Exception e)
        {
            // SAIR MESMO ASSIM, SEMPRE. Com o `auto_accept_quit` desligado no `Boot`, uma excecao
            // aqui deixaria a janela sem X que funcione -- o remedio seria pior que a doenca.
            GD.PushError($"[saida] a limpeza falhou, saindo assim mesmo: {e.Message}");
        }

        arvore?.Quit();
    }
}
