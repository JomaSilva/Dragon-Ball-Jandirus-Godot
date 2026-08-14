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

    /// <summary>
    /// Cancela a assinatura do `Teclas.Mudou` da secao de voz. Nulo antes de <see cref="Montar"/>.
    ///
    /// ============================ `Teclas.Mudou` E ESTATICO ============================
    /// Uma assinatura nao cancelada nele nao vaza um objeto: ela vaza o painel de pausa INTEIRO, pra
    /// sempre, porque o evento e de uma classe estatica e vive enquanto o processo viver. E a lição
    /// que este port ja pagou (19 orfaos por ciclo de relog): metodo NOMEADO e `-=`, nunca lambda.
    /// ==============================================================================
    /// </summary>
    private Action? _soltarRotulo;

    public override void _ExitTree() => _soltarRotulo?.Invoke();

    /// <summary>Aberto = o jogador nao esta controlando o personagem.</summary>
    public bool Aberto { get; private set; }

    public override void _Ready()
    {
        Layer = 20;   // acima do HUD e de tudo
        _cfg = Boot.Config;
        Montar();
        // O MOTIVO E O QUE O DIARIO DA TRILHA VAI LER. Este `Fechar` do nascimento e o que de fato
        // tira o tema do menu do ar quando se entra no mundo -- ele roda ANTES do
        // `PararCamada(Menu, "entrei no mundo")` do `Boot` --, e com o motivo padrao o log da a
        // primeira troca do jogo como "ESC fechou" sem ninguem ter encostado no ESC. Um diario que
        // mente numa linha nao serve pra julgar as outras.
        Fechar("menu nasceu fechado (entrei no mundo)");
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
        AudioDirector.Instance?.Musica(Trilha.Menu(), AudioDirector.Camada.Menu, "ESC abriu");
    }

    /// <inheritdoc cref="AudioDirector.Musica" path="/param[@name='motivo']"/>
    public void Fechar(string motivo = "ESC fechou")
    {
        Aberto = false;
        _painel.Visible = false;
        AudioDirector.Instance?.PararCamada(AudioDirector.Camada.Menu, motivo);
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

        // GRAFICO: hoje ele decide o filtro da sombra das luzes (nenhum / PCF5 / PCF13).
        //
        // O nome e "Grafico" e nao "Filtro de sombra" de proposito: e o balde onde o proximo
        // ajuste de custo visual entra sem virar mais uma linha na tela. Tres niveis bastam --
        // o jogador escolhe entre "roda" e "bonito", nao entre treze parametros.
        var grafico = new OptionButton();
        grafico.AddItem("baixo (sem filtro)", Settings.GraficoBaixo);
        grafico.AddItem("medio (PCF5)", Settings.GraficoMedio);
        grafico.AddItem("alto (PCF13)", Settings.GraficoAlto);
        grafico.Selected = Math.Clamp(_cfg.Grafico, 0, 2);
        grafico.ItemSelected += i =>
        {
            _cfg.Grafico = (int)i;
            AplicarEGravar();
            // VALE AGORA, nao no proximo planeta: as luzes ja plantadas trocam de filtro na hora.
            if (World.Instancia is { } w) Iluminacao.AplicarQualidade(w);
        };
        caixa.AddChild(Linha("Grafico", grafico));

        // --- teclas ---
        //
        // A TELA E OUTRA, e o botao daqui e a unica porta dela. Ela nao cabe nesta caixa: sao 27
        // controles de jogo mais um por verb (um Saiyajin com muitas skills passa de cem) mais uma
        // linha por forma despertada, com busca e rolagem. Mas ela PERTENCE aqui -- tecla e
        // preferencia de maquina, igual a resolucao e ao volume que estao logo acima e logo abaixo.
        caixa.AddChild(Secao("Teclas"));
        var teclas = new Button { Text = "Configurar teclas e atalhos" };
        teclas.Pressed += () => TelaDeTeclas.Instancia?.Abrir();
        caixa.AddChild(teclas);

        // --- som ---
        caixa.AddChild(Secao("Som"));
        caixa.AddChild(Volume("Geral", () => _cfg.VolumeGeral, v => _cfg.VolumeGeral = v));
        caixa.AddChild(Volume("Musica", () => _cfg.VolumeMusica, v => _cfg.VolumeMusica = v));
        caixa.AddChild(Volume("Efeitos", () => _cfg.VolumeEfeitos, v => _cfg.VolumeEfeitos = v));
        caixa.AddChild(Volume("Ambiente", () => _cfg.VolumeAmbiente, v => _cfg.VolumeAmbiente = v));
        caixa.AddChild(Volume("Voz", () => _cfg.VolumeVoz, v => _cfg.VolumeVoz = v));

        // --- voz ---
        //
        // ============================ SECAO PROPRIA, E ELA COMECA DESLIGADA ============================
        // Voz nao e "mais um volume": e a unica coisa deste jogo que capta o quarto de quem joga. O
        // controle dela nao pode estar espremido entre "Efeitos" e "Ambiente" como se fosse mais uma
        // fatia do misturador -- quem procura "como desligo o microfone" tem que achar em um olhar.
        //
        // O interruptor DE FATO desliga: com ele em falso o `Microfone` nao cria o tocador do
        // dispositivo (ver `Settings.VozLigada`). Nao ha captura acontecendo "so que ignorada".
        // ============================================================================================
        caixa.AddChild(Secao("Voz local"));

        var vozLiga = new CheckBox { Text = "usar o microfone", ButtonPressed = _cfg.VozLigada };
        var vozModo = new CheckBox
        {
            Text = "apertar pra falar (senao: microfone aberto)",
            ButtonPressed = _cfg.VozApertarParaFalar,
            Disabled = !_cfg.VozLigada,
        };

        var vozDisp = new OptionButton { Disabled = !_cfg.VozLigada };
        vozDisp.AddItem("(padrao do sistema)", 0);
        string[] entradas = AudioServer.GetInputDeviceList();
        for (int i = 0; i < entradas.Length; i++) vozDisp.AddItem(entradas[i], i + 1);
        vozDisp.Selected = Math.Max(0, Array.IndexOf(entradas, _cfg.DispositivoDeVoz) + 1);
        vozDisp.ItemSelected += i =>
        {
            // POR NOME E NAO POR INDICE: a lista muda de ordem quando alguem pluga um fone, e um
            // indice gravado passaria a apontar pro microfone da webcam depois do proximo boot.
            _cfg.DispositivoDeVoz = i <= 0 ? "" : entradas[i - 1];
            AplicarEGravar();
        };

        var vozTecla = new Label();
        void Rotular() => vozTecla.Text =
            $"tecla: {Teclas.NomeDaAcao("falar_voz")}  (muda em \"Configurar teclas\")";
        Rotular();
        // A TELA DE TECLAS PODE MUDAR ISTO ENQUANTO ESTA CAIXA ESTA ABERTA (as duas convivem no
        // painel de pausa). Metodo NOMEADO e `-=` no `_ExitTree`: lambda nao se cancela, e `Mudou` e
        // um evento ESTATICO -- assinatura vazada aqui sobreviveria ao node inteiro.
        Teclas.Mudou += Rotular;
        _soltarRotulo = () => Teclas.Mudou -= Rotular;

        vozLiga.Toggled += on =>
        {
            _cfg.VozLigada = on;
            vozModo.Disabled = !on;
            vozDisp.Disabled = !on;
            AplicarEGravar();
        };
        vozModo.Toggled += on => { _cfg.VozApertarParaFalar = on; AplicarEGravar(); };

        caixa.AddChild(vozLiga);
        caixa.AddChild(vozModo);
        caixa.AddChild(Linha("Microfone", vozDisp));
        caixa.AddChild(vozTecla);

        // --- saida ---
        caixa.AddChild(new HSeparator());
        _aviso = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _aviso.AddThemeColorOverride("font_color", new Color("d8b98a"));
        caixa.AddChild(_aviso);

        var voltar = new Button { Text = "Voltar ao jogo" };
        // o botao e o ESC sao o mesmo gesto -- e o motivo padrao ja diz isso
        voltar.Pressed += () => Fechar();
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
