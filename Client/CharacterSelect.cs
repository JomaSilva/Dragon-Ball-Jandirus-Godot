using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A TELA DOS TRES SLOTS, entre entrar na conta e pisar no mundo.
///
/// Cada slot mostra o personagem montado de verdade -- o retrato NAO e uma foto guardada, e
/// o mesmo boneco em camadas que vai andar no mundo. Slot vazio abre a criacao.
///
/// So existe DEPOIS do login porque so o servidor sabe o que a conta tem. E por isso que a
/// criacao de personagem mudou de lugar: antes ela vinha antes de conectar e nao tinha como
/// saber em qual slot ia parar.
/// </summary>
public partial class CharacterSelect : CanvasLayer
{
    public event Action<int>? Jogar;         // slot ocupado escolhido
    public event Action<int>? Criar;         // slot vazio escolhido
    public event Action? Sair;

    private VisualCatalog? _cat;
    private List<SlotInfo> _slots = [];

    public void Mostrar(List<SlotInfo> slots)
    {
        _slots = slots;
        if (IsNodeReady()) Remontar();
    }

    public override void _Ready()
    {
        Layer = 3;
        const string dados = "res://Assets/Data/visual.json";
        if (Godot.FileAccess.FileExists(dados))
            _cat = VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));
        Remontar();
    }

    private void Remontar()
    {
        foreach (Node n in GetChildren()) n.QueueFree();

        AddChild(new ColorRect { Color = Tema.Fundo, AnchorRight = 1, AnchorBottom = 1 });

        var centro = new CenterContainer
        {
            AnchorRight = 1, AnchorBottom = 1,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        Tema.Aplicar(centro);
        AddChild(centro);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 16);
        centro.AddChild(col);

        var titulo = new Label { Text = "SEUS PERSONAGENS", HorizontalAlignment = HorizontalAlignment.Center };
        titulo.AddThemeFontSizeOverride("font_size", 26);
        col.AddChild(titulo);

        var sub = new Label
        {
            Text = "tres vagas neste servidor",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        sub.AddThemeFontSizeOverride("font_size", 12);
        sub.AddThemeColorOverride("font_color", Tema.TextoFraco);
        col.AddChild(sub);

        var fileira = new HBoxContainer();
        fileira.AddThemeConstantOverride("separation", 16);
        col.AddChild(fileira);

        for (int i = 0; i < _slots.Count; i++) fileira.AddChild(MontarSlot(i, _slots[i]));

        var rodape = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddChild(rodape);
        var voltar = new Button { Text = "Trocar de servidor" };
        voltar.Pressed += () => Sair?.Invoke();
        rodape.AddChild(voltar);
    }

    private Control MontarSlot(int indice, SlotInfo s)
    {
        // SLOT VAZIO E SLOT CHEIO NAO SAO A MESMA COISA e nao devem parecer: o cheio tem
        // borda viva e fundo mais claro (e um personagem seu), o vazio fica apagado e
        // tracejado por dentro (e um convite, nao um card).
        var painel = new PanelContainer { CustomMinimumSize = new Vector2(216, 344) };
        painel.AddThemeStyleboxOverride("panel", s.Ocupado
            ? Tema.Caixa(Tema.PainelClaro, Tema.BordaViva, 12)
            : Tema.Caixa(new Color("161a24"), Tema.Borda, 12));

        var caixa = new VBoxContainer();
        caixa.AddThemeConstantOverride("separation", 3);
        painel.AddChild(caixa);

        // O retrato e um Node2D dentro de um Control: ele NAO respeita o layout do container
        // e desenha por cima do que vier depois. Duas coisas seguram isso -- a moldura precisa
        // caber o sprite inteiro (32 px x escala) e precisa RECORTAR o que passar. Sem isso o
        // boneco escorria por cima do nome logo abaixo.
        const int escala = 4;
        const int lado = 32 * escala;
        var moldura = new Control
        {
            CustomMinimumSize = new Vector2(200, lado + 16),
            ClipContents = true,
        };
        caixa.AddChild(moldura);

        if (s.Ocupado && _cat != null)
        {
            // o retrato E o personagem: mesmas camadas, mesma pilha do jogo
            var boneco = new CharacterVisual
            {
                Position = new Vector2(100, lado / 2 + 8),
                Scale = new Vector2(escala, escala),
            };
            moldura.AddChild(boneco);
            boneco.Vestir(_cat, s.Visual, s.Raca, s.Genero);
        }
        else
        {
            var vazio = new Label
            {
                Text = "+",
                AnchorRight = 1, AnchorBottom = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            vazio.AddThemeFontSizeOverride("font_size", 56);
            vazio.AddThemeColorOverride("font_color", new Color("39405a"));
            moldura.AddChild(vazio);
        }

        if (s.Ocupado)
        {
            var nome = new Label { Text = s.Nome, HorizontalAlignment = HorizontalAlignment.Center };
            nome.AddThemeFontSizeOverride("font_size", 19);
            caixa.AddChild(nome);

            var linhagem = new Label
            {
                Text = s.Classe.Length > 0 ? s.Classe : "sem linhagem",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            linhagem.AddThemeFontSizeOverride("font_size", 12);
            linhagem.AddThemeColorOverride("font_color", Tema.Destaque);
            caixa.AddChild(linhagem);

            caixa.AddChild(new HSeparator());
            caixa.AddChild(Info($"{s.Raca}  ·  {s.Idade} anos"));
            caixa.AddChild(Info($"BP {Numero(s.BP)}"));

            caixa.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
            var jogar = new Button { Text = "Jogar" };
            int alvo = indice;
            jogar.Pressed += () => Jogar?.Invoke(alvo);
            caixa.AddChild(jogar);
        }
        else
        {
            var livre = new Label { Text = "vaga livre", HorizontalAlignment = HorizontalAlignment.Center };
            livre.AddThemeColorOverride("font_color", Tema.TextoFraco);
            caixa.AddChild(livre);

            caixa.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
            var criar = new Button { Text = "Criar personagem" };
            int alvo = indice;
            criar.Pressed += () => Criar?.Invoke(alvo);
            caixa.AddChild(criar);
        }

        return painel;
    }

    private static Label Info(string t)
    {
        var l = new Label { Text = t, HorizontalAlignment = HorizontalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", 13);
        l.AddThemeColorOverride("font_color", Tema.TextoFraco);
        return l;
    }

    private static string Numero(double v) => v switch
    {
        >= 1e12 => $"{v / 1e12:0.##} T",
        >= 1e9 => $"{v / 1e9:0.##} B",
        >= 1e6 => $"{v / 1e6:0.##} M",
        >= 1e3 => $"{v / 1e3:0.##} k",
        _ => v.ToString("0.#"),
    };
}
