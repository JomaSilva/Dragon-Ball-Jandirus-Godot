using Godot;

namespace Jandirus.Client;

/// <summary>
/// OS DOIS BOTOES QUE FALTAVAM NO LOBBY: **Opcoes** e **Sair do jogo**.
///
/// Pedido do dono, literal: *"adicione tb uma opcao no lobby do jogo de sair do jogo, e abrir a tela
/// de opcoes, pq so da pra fazer dentro do jogo isso e as vezes quero mudar o volume no lobby e n
/// da"*.
///
/// ============================ POR QUE E UMA PECA COMPARTILHADA, E NAO UM BOTAO NUMA TELA SO ============================
/// Porque **o lobby sao TRES telas que se escondem uma a outra**, e nao uma:
///
///     LOGIN (`Boot.MontarLogin`)  ->  SELECAO (`CharacterSelect`)  ->  CRIACAO (`CreationScreen`)
///
/// O login some (`_painel.Visible = false`) quando a selecao aparece, e a selecao some quando a
/// criacao abre. Um botao posto so no login desapareceria nas outras duas -- e "quero mudar o volume
/// no lobby" inclui a tela onde o jogador passa mais tempo, que e a criacao de personagem.
///
/// ============================ E POR QUE NAO RESOLVER SO COM UMA TECLA ============================
/// Porque o ESC **nao serve como porta unica aqui**, e isto ja e MEDIDO neste projeto: o cabecalho
/// do `CharacterSelect._Input` guarda o resultado da bancada `--diagapagar` -- *"um LineEdit COM
/// FOCO recebe a tecla pelo gui_input e o evento nao chega ao `_unhandled_*` de ninguem"*. A tela de
/// login nasce com tres LineEdit (servidor, conta, senha) e o jogador esta digitando neles. O ESC
/// continua funcionando de brinde; o BOTAO e o que funciona sempre.
/// ==========================================================================================
/// </summary>
public static class BotoesDoLobby
{
    /// <param name="dono">
    /// Quem esta montando a linha. Serve pra chegar na `SceneTree` na hora de sair -- um `static`
    /// nao tem arvore, e pegar a do `Engine.GetMainLoop()` seria a mesma coisa por um caminho que
    /// nao diz de quem e o clique.
    /// </param>
    public static HBoxContainer Montar(Node dono)
    {
        var linha = new HBoxContainer();
        linha.AddThemeConstantOverride("separation", 6);

        // NENHUM OVERRIDE DE ESTILO AQUI, de proposito: as tres telas do lobby aplicam o
        // `Tema.Aplicar` na raiz e os filhos herdam. A tela de apagar personagem ja foi refeita neste
        // projeto por ter sido a unica que fugiu do molde da casa (era um dialogo do Godot e saia
        // torta); um botao com estilo proprio seria o mesmo erro em tamanho menor.
        var opcoes = new Button
        {
            Text = "Opções",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "volume, resolução, tela cheia e teclas",
        };
        opcoes.Pressed += () => PauseMenu.Instancia?.Abrir();
        linha.AddChild(opcoes);

        // ============================ O AVISO DE HOST MORA NO PROPRIO BOTAO ============================
        // Dava pra estar parado na tela de SELECAO, sem mundo nenhum, hospedando uma partida com
        // gente dentro: o `Boot.Hospedar` sobe o servidor e SO DEPOIS chama o `Entrar`. Sair dali
        // derruba todo mundo, entao pede-se duas vezes -- a mesma disciplina que o botao
        // "Desconectar" do menu de pausa ja tinha.
        //
        // O estado e uma variavel capturada, e nao o texto do botao: ler estado de volta de um rotulo
        // e o tipo de coisa que quebra quando alguem traduz a frase.
        bool avisado = false;
        var sair = new Button { Text = "Sair do jogo", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        sair.Pressed += () =>
        {
            if (!avisado && Jandirus.Server.GameServer.Instance is { Running: true })
            {
                avisado = true;
                sair.Text = "Sair MESMO? (derruba o servidor)";
                return;
            }
            Saida.Encerrar(dono.GetTree(), "botão do lobby");
        };
        linha.AddChild(sair);

        return linha;
    }
}
