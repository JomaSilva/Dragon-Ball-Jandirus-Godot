using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// DIAGNOSTICO DAS CAMADAS. Roda sem janela e imprime, quadro a quadro, o que cada camada
/// esta desenhando: qual animacao, qual quadro, visivel ou nao.
///
/// Existe porque bug de animacao e invisivel pra quem le codigo -- eu errei duas vezes
/// deduzindo a causa da roupa fora de passo. Aqui o problema vira NUMERO: se a camisa
/// aparece na animacao errada, ou num quadro que nao acompanha o corpo, sai no log.
///
///     &lt;godot&gt; --headless --path . --diagvisual
/// </summary>
public partial class VisualDiag : Node2D
{
    private CharacterVisual _v = null!;
    private VisualCatalog? _cat;
    private int _t;
    private double _seg;          // tempo real desde a ultima troca de pose
    private int _ultimoQuadro = -1;

    /// <summary>Um roteiro: em que quadro entra qual pose.</summary>
    private static readonly (int Quadro, string Fase)[] Roteiro =
    [
        (0, "andando sul"), (10, "andando leste"), (20, "socando"), (30, "treinando"),
        (40, "parado"), (400, "fim"),
    ];

    public override void _Ready()
    {
        const string dados = "res://Assets/Data/visual.json";
        if (!Godot.FileAccess.FileExists(dados)) { GD.Print("[diag] sem visual.json"); Quit(); return; }
        _cat = VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));

        _v = new CharacterVisual { Name = "Alvo" };
        AddChild(_v);

        // veste JUSTAMENTE as pecas que o dono reportou como quebradas
        var ap = new Appearance { Cabelo = "Goku" };
        foreach (string alvo in new[] { "ClothesSaiyanSuit", "GokuDBSSuit" })
        {
            string? achou = _cat.Roupas.FirstOrDefault(r => r.Contains(alvo, StringComparison.OrdinalIgnoreCase));
            if (achou != null) ap.Roupa.Add(new(achou));
            else GD.Print($"[diag] roupa '{alvo}' nao esta no catalogo");
        }

        _v.Vestir(_cat, ap, "Saiyan", "Male");
        _v.SetMotion(Facing.South, true);

        GD.Print($"[diag] camadas vestidas: corpo + {ap.Roupa.Count} roupa(s) + cabelo '{ap.Cabelo}'");
        GD.Print("[diag] quadro | camada = animacao : quadro (visivel, sincronizada)");
    }

    public override void _Process(double delta)
    {
        foreach ((int q, string fase) in Roteiro)
        {
            if (q != _t) continue;
            if (fase == "fim") { Quit(); return; }

            GD.Print($"\n--- quadro {_t}: {fase} ---");
            switch (fase)
            {
                case "andando sul": _v.SetMotion(Facing.South, true); break;
                case "andando leste": _v.SetMotion(Facing.East, true); break;
                case "socando": _v.RestartState("attack"); break;
                case "treinando": _v.SetState("train"); break;
                case "parado":
                    _v.SetState("default");
                    _v.SetMotion(Facing.South, false);
                    _seg = 0; _ultimoQuadro = -1;
                    GD.Print("  (imprime so quando o QUADRO muda -- da pra ler a duracao de cada um)");
                    break;
            }
        }

        _seg += delta;

        // na fase PARADA o que interessa e a DURACAO de cada quadro (a piscada), nao o
        // quadro a quadro: imprime so quando o corpo troca de quadro, com o instante
        if (_t >= 40)
        {
            var corpo = (AnimatedSprite2D)_v.GetChild(0);
            if (corpo.Frame != _ultimoQuadro)
            {
                GD.Print($"  t={_seg,6:0.000}s  corpo quadro {corpo.Frame} ({corpo.Animation})");
                _ultimoQuadro = corpo.Frame;
            }
        }
        else Dump();
        _t++;
    }

    private void Dump()
    {
        var linha = new System.Text.StringBuilder($"  {_t,3} |");
        foreach (Node n in _v.GetChildren())
        {
            if (n is not AnimatedSprite2D s) continue;
            string nome = s.GetMeta("src", "").AsString();
            nome = nome.Length > 0 ? nome[(nome.LastIndexOf('/') + 1)..].Replace(".tres", "") : "?";
            if (nome.Length > 14) nome = nome[..14];
            linha.Append($" {nome}={s.Animation}:{s.Frame}{(s.Visible ? "" : " OCULTA")}");
        }
        GD.Print(linha.ToString());
    }

    private void Quit()
    {
        GD.Print("\n[diag] fim");
        GetTree().Quit();
    }
}
