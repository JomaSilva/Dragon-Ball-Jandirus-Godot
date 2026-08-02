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
        cli.Falou += Ouviu;
        GD.Print("[robo] socando -- alvo: quem estiver na frente");
    }

    public override void _ExitTree()
    {
        if (GameClient.Instance is not { } cli) return;
        cli.Golpe -= AoGolpe;
        cli.Falou -= Ouviu;
    }

    // =====================================================================
    // O ROBO TAMBEM FALA
    // =====================================================================
    /// <summary>
    /// Uma frase a cada tantos segundos, alternando os canais.
    ///
    /// Nao e enfeite: dois `--socar` no mesmo servidor sao o unico teste automatico que existe
    /// com DUAS pontas, e o chat so se prova com duas -- alcance por distancia, sussurro que
    /// vira teaser de longe, OOC que atravessa planeta. Sem isso, a unica forma de saber que a
    /// fala chega seria alguem digitar.
    ///
    /// Eles se afastam e se aproximam o tempo todo (ver <see cref="Andar"/>), entao as duas
    /// pontas do alcance sao percorridas sozinhas ao longo do teste.
    /// </summary>
    private void Tagarelar()
    {
        if (GameClient.Instance is not { } cli) return;
        Protocol.Fala canal = (_frase % 4) switch
        {
            0 => Protocol.Fala.Diz,
            1 => Protocol.Fala.Sussurro,
            2 => Protocol.Fala.Emote,
            _ => Protocol.Fala.Ooc,
        };
        cli.SendChat(canal, $"teste de {canal} numero {_frase}");
        _frase++;
    }

    private void Ouviu(Protocol.Fala canal, string autor, string texto) =>
        GD.Print($"[robo] ouvi ({canal}) {autor}: " + (texto.Length > 0 ? texto : "(sussurro sem conteudo)"));

    private int _frase;
    private double _proximaFrase = 3;

    public override void _Process(double delta)
    {
        if (GameClient.Instance is not { Connected: true } cli) return;

        Andar(delta);

        _proximo -= delta;
        if (_proximo <= 0)
        {
            _proximo = _cadencia;
            _golpes++;
            cli.SendAction();
        }

        _proximaFrase -= delta;
        if (_proximaFrase <= 0) { _proximaFrase = 4; Tagarelar(); }

        _relatorio -= delta;
        if (_relatorio > 0) return;
        _relatorio = 5;
        string corpo = "";
        foreach (Protocol.ParteState p in cli.Corpo)
            if (p.Decepado || p.Vida < 100) corpo += $" {p.Nome}={(p.Decepado ? "X" : p.Vida.ToString())}";

        GD.Print($"[robo] {_golpes} golpes | acertos {_acertos} aparados {_aparados} "
                 + $"contras {_contras} erros {_erros} esquivas {_esquivas} "
                 + $"| vida {cli.Sheet.HP:0.0} | rabo {(cli.Sheet.Rabo ? "sim" : "nao")}"
                 + $" | membros {cli.Corpo.Count}"
                 + (cli.Sheet.Imobilizado ? "  <- NO CHAO" : "")
                 + (corpo.Length > 0 ? $"\n         feridos:{corpo}" : ""));
    }

    /// <summary>
    /// VAI E VOLTA, CORRENDO. O robo aperta as teclas de verdade (`Input.ActionPress`) em vez
    /// de mexer na posicao: assim o teste passa pelo MESMO caminho do jogador -- movimento
    /// local, bit de corrida no pacote, concessao do servidor, validacao de passo.
    ///
    /// Afastar e voltar e o que faz o dash de aproximacao disparar: colados, os dois ja estao
    /// no alcance do soco e nao ha distancia pra fechar.
    /// </summary>
    private void Andar(double delta)
    {
        _troca -= delta;
        if (_troca > 0) return;
        _troca = 2.5;

        // Robos com id PAR comecam indo pra um lado e os impares pro outro: se todos andarem
        // juntos eles nunca se separam, ficam colados o teste inteiro, e o dash -- que so
        // existe pra FECHAR distancia -- nao dispara uma vez sequer.
        if (!_comecou)
        {
            _comecou = true;
            _indoPraDireita = (GameClient.Instance?.LocalId ?? 0) % 2 == 0;
        }
        else
        {
            Godot.Input.ActionRelease(_indoPraDireita ? "move_right" : "move_left");
            _indoPraDireita = !_indoPraDireita;
        }

        Godot.Input.ActionPress(_indoPraDireita ? "move_right" : "move_left");
        Godot.Input.ActionPress("run");
    }

    private double _troca;
    private bool _indoPraDireita, _comecou;

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
