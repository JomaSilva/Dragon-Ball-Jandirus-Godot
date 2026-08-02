using Godot;

namespace Jandirus.Client;

/// <summary>
/// O MENU DE PAUSE (ESC). Video, som e a porta de saida.
///
/// NAO congela o mundo -- e um jogo em rede, e o servidor continua rodando de qualquer jeito.
/// Pausar a arvore local so ia mentir pro jogador enquanto ele apanha. O que ele faz e tirar
/// o foco do controle e trocar a MUSICA: abrir o menu poe o tema do menu no ar (como o BYOND
/// fazia na criacao de personagem) e fechar devolve o que estava tocando.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    public event Action? Desconectar;

    private Settings _cfg = null!;
    private Control _painel = null!;
    private Label _aviso = null!;

    /// <summary>Aberto = o jogador nao esta controlando o personagem.</summary>
    public bool Aberto { get; private set; }

    public override void _Ready()
    {
        Layer = 20;   // acima do HUD e de tudo
        _cfg = Boot.Config;
        Montar();
        Fechar();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        if (Aberto) Fechar(); else Abrir();
        GetViewport().SetInputAsHandled();
    }

    public void Abrir()
    {
        Aberto = true;
        _painel.Visible = true;
        AudioDirector.Instance?.Musica(Trilha.Menu(), AudioDirector.Camada.Menu);
    }

    public void Fechar()
    {
        Aberto = false;
        _painel.Visible = false;
        AudioDirector.Instance?.PararCamada(AudioDirector.Camada.Menu);
    }

    // =====================================================================
    private void Montar()
    {
        var fundo = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.72f),
            AnchorRight = 1, AnchorBottom = 1,
        };
        AddChild(fundo);
        _painel = fundo;

        var centro = new CenterContainer
        {
            AnchorRight = 1, AnchorBottom = 1,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        fundo.AddChild(centro);

        var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        centro.AddChild(caixa);

        var titulo = new Label { Text = "PAUSA", HorizontalAlignment = HorizontalAlignment.Center };
        titulo.AddThemeFontSizeOverride("font_size", 26);
        caixa.AddChild(titulo);
        caixa.AddChild(new HSeparator());

        // --- video ---
        caixa.AddChild(Secao("Video"));

        var res = new OptionButton();
        for (int i = 0; i < Settings.Resolucoes.Length; i++)
        {
            (int l, int a) = Settings.Resolucoes[i];
            res.AddItem($"{l} x {a}");
            if (l == _cfg.LarguraJanela && a == _cfg.AlturaJanela) res.Selected = i;
        }
        res.ItemSelected += i =>
        {
            (_cfg.LarguraJanela, _cfg.AlturaJanela) = Settings.Resolucoes[(int)i];
            _cfg.TelaCheia = false;
            AplicarEGravar();
        };
        caixa.AddChild(Linha("Resolucao", res));

        var cheia = new CheckBox { Text = "tela cheia", ButtonPressed = _cfg.TelaCheia };
        cheia.Toggled += on => { _cfg.TelaCheia = on; AplicarEGravar(); };
        caixa.AddChild(cheia);

        // ZOOM INTEIRO: em arte de pixel um zoom quebrado faz a imagem cintilar andando.
        var zoom = new HSlider { MinValue = Settings.ZoomMin, MaxValue = Settings.ZoomMax, Step = 1, Value = _cfg.Zoom };
        var zoomTxt = new Label { Text = $"{_cfg.Zoom}x", CustomMinimumSize = new Vector2(40, 0) };
        zoom.ValueChanged += v =>
        {
            _cfg.Zoom = (int)v;
            zoomTxt.Text = $"{_cfg.Zoom}x";
            AplicarEGravar();
            World.Instancia?.AplicarZoom(_cfg.Zoom);
        };
        var linhaZoom = Linha("Zoom", zoom);
        linhaZoom.AddChild(zoomTxt);
        caixa.AddChild(linhaZoom);

        // --- som ---
        caixa.AddChild(Secao("Som"));
        caixa.AddChild(Volume("Geral", () => _cfg.VolumeGeral, v => _cfg.VolumeGeral = v));
        caixa.AddChild(Volume("Musica", () => _cfg.VolumeMusica, v => _cfg.VolumeMusica = v));
        caixa.AddChild(Volume("Efeitos", () => _cfg.VolumeEfeitos, v => _cfg.VolumeEfeitos = v));
        caixa.AddChild(Volume("Ambiente", () => _cfg.VolumeAmbiente, v => _cfg.VolumeAmbiente = v));

        // --- saida ---
        caixa.AddChild(new HSeparator());
        _aviso = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _aviso.AddThemeColorOverride("font_color", new Color("d8b98a"));
        caixa.AddChild(_aviso);

        var voltar = new Button { Text = "Voltar ao jogo" };
        voltar.Pressed += Fechar;
        caixa.AddChild(voltar);

        var sair = new Button { Text = "Desconectar" };
        sair.Pressed += () =>
        {
            // QUEM HOSPEDA DERRUBA A PARTIDA AO SAIR. Avisa antes: o servidor mora neste
            // processo, entao sair do jogo tira todo mundo junto.
            if (Jandirus.Server.GameServer.Instance is { Running: true } srv)
            {
                if (_aviso.Text.Length == 0)
                {
                    _aviso.Text = "Voce esta HOSPEDANDO: sair derruba o servidor pra todos. Clique de novo.";
                    return;
                }
                srv.Stop();
            }
            GameClient.Instance?.Desconectar();
            Fechar();
            Desconectar?.Invoke();
        };
        caixa.AddChild(sair);

        var fechar = new Button { Text = "Fechar o jogo" };
        fechar.Pressed += () =>
        {
            (Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer)?.Stop();
            GetTree().Quit();
        };
        caixa.AddChild(fechar);
    }

    private void AplicarEGravar()
    {
        _cfg.Aplicar();
        _cfg.Gravar();
    }

    private static Label Secao(string t)
    {
        var l = new Label { Text = t };
        l.AddThemeColorOverride("font_color", new Color("9fb4d8"));
        return l;
    }

    private static HBoxContainer Linha(string rotulo, Control campo)
    {
        var h = new HBoxContainer();
        h.AddChild(new Label { Text = rotulo, CustomMinimumSize = new Vector2(120, 0) });
        campo.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        h.AddChild(campo);
        return h;
    }

    private HBoxContainer Volume(string rotulo, Func<float> ler, Action<float> escrever)
    {
        var s = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = ler() };
        var txt = new Label { Text = $"{ler() * 100:0}%", CustomMinimumSize = new Vector2(40, 0) };
        s.ValueChanged += v =>
        {
            escrever((float)v);
            txt.Text = $"{v * 100:0}%";
            AplicarEGravar();
        };
        HBoxContainer h = Linha(rotulo, s);
        h.AddChild(txt);
        return h;
    }
}
