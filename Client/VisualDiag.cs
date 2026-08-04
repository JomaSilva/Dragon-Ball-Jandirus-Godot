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

    /// <summary>
    /// O VULTO DO ZANZOKEN NAO PODE FICAR CARECA.
    ///
    /// ============================ O DEFEITO QUE ISTO GUARDA ============================
    /// A miragem dissolve a pilha inteira (corpo, roupa, cabelo, rabo). Enquanto a dissolucao
    /// corria na UV de ATLAS, cada camada media o proprio limiar num lugar diferente da FOLHA --
    /// e como a posicao do quadro na folha muda com a DIRECAO, virado pra direita e pra baixo o
    /// cabelo cruzava o limiar antes do corpo e o vulto aparecia careca por alguns quadros. Pra
    /// esquerda e pra cima os quadros calhavam de cair em linhas parecidas, e nada aparecia.
    ///
    /// O conserto poe todas as camadas no MESMO espaco: a caixa do corpo, passada a cada uma ja
    /// descontada da posicao dela. O que isto confere e a premissa desse conserto -- que as
    /// camadas estao de fato EMPILHADAS, ocupando a mesma celula. Se uma sair do lugar, cada uma
    /// volta a medir o limiar num ponto diferente e a careca volta junto.
    ///
    /// NAS QUATRO DIRECOES, porque foi a direcao que expos o defeito da primeira vez: o quadro de
    /// cada direcao mora num lugar diferente da folha, e era exatamente isso que a UV de atlas
    /// deixava vazar pro calculo.
    ///
    /// O QUE ISTO **NAO** ALCANCA e o shader em si (nao da pra ler uniforme de dentro do C#).
    /// A outra metade do conserto -- o topo dissolver por ultimo -- esta no `Zanzoken.gdshader`,
    /// no sinal do `1.0 - p.y`, e so se confere olhando.
    /// ==================================================================================
    /// </summary>
    private void ConferirVulto()
    {
        GD.Print("\n[diag] --- vulto do Zanzoken: as camadas estao empilhadas no mesmo lugar? ---");

        foreach (Facing dir in new[] { Facing.South, Facing.North, Facing.East, Facing.West })
        {
            _v.SetMotion(dir, false);
            Node2D foto = _v.Fotografar();
            Rect2 corpo = Zanzoken.CaixaDoCorpo(foto);

            int camadas = 0;
            float maiorDesvio = 0;
            foreach (Node n in foto.GetChildren())
            {
                if (n is not Sprite2D s) continue;
                camadas++;
                Rect2 r = s.GetRect();
                r.Position += s.Position;

                // O QUANTO ESTA CAMADA DESTOA DA CAIXA COMUM, em pixels. Zero = perfeitamente
                // empilhada, que e o caso quando todas sao celulas 32x32 da mesma grade.
                maiorDesvio = Mathf.Max(maiorDesvio, Mathf.Max(
                    (r.Position - corpo.Position).Abs().MaxAxisIndex() == Vector2.Axis.X
                        ? Mathf.Abs(r.Position.X - corpo.Position.X) : Mathf.Abs(r.Position.Y - corpo.Position.Y),
                    Mathf.Max(Mathf.Abs(r.Size.X - corpo.Size.X), Mathf.Abs(r.Size.Y - corpo.Size.Y))));
            }

            bool ok = camadas > 1 && maiorDesvio < 1f;
            GD.Print($"[diag]   {dir,-6} {(ok ? "ok   " : "FALHA")} {camadas} camadas na caixa "
                     + $"{corpo.Size.X:0}x{corpo.Size.Y:0}, maior desvio {maiorDesvio:0.##} px");
            foto.QueueFree();
        }
    }

    private void Quit()
    {
        ConferirVulto();
        GD.Print("\n[diag] fim");
        GetTree().Quit();
    }
}
