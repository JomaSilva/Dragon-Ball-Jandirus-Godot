using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ROBO DE TESTE (`--socar`). Soca sem parar e narra o que o servidor devolve.
///
/// Existe pelo mesmo motivo do `--treinar` e do `--diagvisual`: sem janela ninguem aperta
/// tecla, e a cadeia do combate -- pacote de golpe, escolha de alvo, resolucao, transmissao,
/// relato -- so se prova de ponta a ponta com alguem batendo de verdade. Dois processos com
/// esta flag no mesmo servidor produzem uma briga completa em texto.
///
/// Nao entra em jogo normal: sem a flag, este no nunca e criado.
/// </summary>
public partial class RoboDeSoco : Node
{
    private double _proximo;
    private double _cadencia = Protocol.AttackPoseMs / 1000.0;
    private int _golpes, _acertos, _aparados, _contras, _erros, _esquivas;
    private double _relatorio = 5;

    public override void _Ready()
    {
        if (GameClient.Instance is not { } cli) return;
        cli.Golpe += AoGolpe;
        cli.SheetUpdated += f => { if (f.SocoMs > 0) _cadencia = f.SocoMs / 1000.0; };
        // luta pra valer: o teste precisa chegar a quebra de membro e a morte
        cli.SendLethal(true);
        GD.Print("[robo] socando -- alvo: quem estiver na frente");
    }

    public override void _ExitTree()
    {
        if (GameClient.Instance is { } cli) cli.Golpe -= AoGolpe;
    }

    public override void _Process(double delta)
    {
        if (GameClient.Instance is not { Connected: true } cli) return;

        _proximo -= delta;
        if (_proximo <= 0)
        {
            _proximo = _cadencia;
            _golpes++;
            cli.SendAction();
        }

        _relatorio -= delta;
        if (_relatorio > 0) return;
        _relatorio = 5;
        GD.Print($"[robo] {_golpes} golpes | acertos {_acertos} aparados {_aparados} "
                 + $"contras {_contras} erros {_erros} esquivas {_esquivas} "
                 + $"| minha vida {cli.Sheet.HP:0.0} | cadencia {_cadencia * 1000:0} ms"
                 + (cli.Sheet.Imobilizado ? "  <- NO CHAO" : ""));
    }

    private void AoGolpe(Protocol.HitEvent h)
    {
        if (GameClient.Instance is not { } cli) return;
        bool bati = h.Atacante == cli.LocalId;

        switch ((Jandirus.Core.Combat.Desfecho)h.Desfecho)
        {
            case Jandirus.Core.Combat.Desfecho.Acertou:
            case Jandirus.Core.Combat.Desfecho.Critico: if (bati) _acertos++; break;
            case Jandirus.Core.Combat.Desfecho.Aparou: if (bati) _aparados++; break;
            case Jandirus.Core.Combat.Desfecho.Contra: if (bati) _contras++; break;
            case Jandirus.Core.Combat.Desfecho.Errou: if (bati) _erros++; break;
            case Jandirus.Core.Combat.Desfecho.Esquivou: if (bati) _esquivas++; break;
        }

        // so os acontecimentos que importam viram linha: a cada 0,3 s um relato encheria o log
        if (!h.Quebrou && !h.Decepou && !h.Nocauteou && !h.Morreu) return;
        string quem = bati ? "ELE" : "EU";
        GD.Print($"[robo] {quem}: {h.Membro}"
                 + (h.Decepou ? " ARRANCADO" : h.Quebrou ? " quebrado" : "")
                 + (h.Morreu ? " -- MORTE" : h.Nocauteou ? " -- NOCAUTE" : ""));
    }
}
