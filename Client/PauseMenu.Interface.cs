using Godot;

namespace Jandirus.Client;

/// <summary>
/// A SECAO "INTERFACE" DAS OPCOES -- o tamanho de cada peca da tela num lugar so, e o botao que desfaz tudo.
/// Pedido do dono de 2026-10-10 (*"adicionar mais opcoes de customizacao da UI do jogo"*).
///
/// Cada peca tambem se mexe DIRETO na tela (a barra do titulo e as bordas do menu P, as bordas do chat, o canto
/// dos paineis do topo); esta secao e a outra porta, pra quem prefere numero -- e a unica que tem o
/// **"Restaurar a interface padrao"** e a trava das bordas.
///
/// ELA NAO GUARDA NADA: cada controle le e escreve o <see cref="ArranjoDaInterface"/> pelas mesmas contas do
/// arrasto (<see cref="Janelas"/>), entao o deslizador para exatamente onde o mouse pararia -- o chat nao sobe
/// alem do painel de status, o painel de status nao desce ate o chat. O que o controle mostra depois de solto e
/// o valor que FICOU, e nao o que se pediu.
///
/// VALE NO LOBBY TAMBEM (esta tela vive la): sem mundo nao ha peca pra ver mudar, mas a preferencia e de maquina
/// e fica gravada -- as pecas nascem nela ao entrar.
/// </summary>
public partial class PauseMenu
{
    private HSlider _iMenuL = null!, _iMenuA = null!, _iChatL = null!, _iChatA = null!, _iStatus = null!, _iCorpo = null!;
    private Label _tMenuL = null!, _tMenuA = null!, _tChatL = null!, _tChatA = null!, _tStatus = null!, _tCorpo = null!;
    private CheckBox _iDestacar = null!, _iTravar = null!;

    /// <summary>
    /// OS CONTROLES ESTAO SENDO ESCRITOS PELA TELA, e nao pelo jogador. Um deslizador avisa "mudei" tambem quando a
    /// FAIXA dele muda e empurra o valor pra dentro dela (o `SetValueNoSignal` so cala a escrita do valor): sem este
    /// trinco, ABRIR as opcoes -- que acerta a faixa de cada deslizador pela tela de agora -- contava como seis
    /// mexidas do jogador, e o menu, o chat e os dois paineis iam pro minimo. Medido na primeira rodada da bancada.
    /// </summary>
    private bool _mostrandoAInterface;

    /// <summary>
    /// DEFEITO INJETADO (bancada --diagjanelas): sem o trinco acima -- abrir as opcoes mexe no arranjo. Falso em jogo, sempre.
    /// </summary>
    public static bool AbrirAsOpcoesMexeDeTeste;

    /// <summary>A tela de AGORA, em px da base de desenho: e contra ela que os deslizadores de tamanho se medem.</summary>
    private Vector2 TelaDaInterface() => GetViewport().GetVisibleRect().Size;

    private void MontarAInterface(VBoxContainer caixa)
    {
        caixa.AddChild(Secao("Interface"));

        var dica = new Label
        {
            Text = "Também dá pra mexer direto na tela: arraste o título e as bordas do menu (P), as bordas do chat "
                 + "e o canto dos painéis do topo.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        dica.AddThemeFontSizeOverride("font_size", 12);
        dica.AddThemeColorOverride("font_color", Tema.TextoFraco);
        caixa.AddChild(dica);

        _iDestacar = new CheckBox { Text = "menu (P) numa janela separada" };
        _iDestacar.Toggled += on =>
        {
            Janelas.Arranjo.MenuDestacado = on;
            Janelas.Avisar();
            Janelas.Gravar();
        };
        caixa.AddChild(_iDestacar);

        (_iMenuL, _tMenuL) = LinhaDeInterface(caixa, "Menu: largura", 10, v => MexerNoMenu(largura: (float)v));
        (_iMenuA, _tMenuA) = LinhaDeInterface(caixa, "Menu: altura", 10, v => MexerNoMenu(altura: (float)v));
        (_iChatL, _tChatL) = LinhaDeInterface(caixa, "Chat: largura", 10, v => MexerNoChat(largura: (float)v));
        (_iChatA, _tChatA) = LinhaDeInterface(caixa, "Chat: altura", 10, v => MexerNoChat(altura: (float)v));
        (_iStatus, _tStatus) = LinhaDeInterface(caixa, "Painel de status", 5, v => Hud.PedirEscala(doStatus: true, (float)v / 100f));
        (_iCorpo, _tCorpo) = LinhaDeInterface(caixa, "Painel do corpo", 5, v => Hud.PedirEscala(doStatus: false, (float)v / 100f));

        _iTravar = new CheckBox
        {
            Text = "travar as bordas (não mexem com o mouse)",
            TooltipText = "com isto ligado, arrastar uma borda não muda nada -- pra não redimensionar sem querer no meio de uma luta",
        };
        _iTravar.Toggled += on =>
        {
            Janelas.Arranjo.Travado = on;
            Janelas.Avisar();
            Janelas.Gravar();
        };
        caixa.AddChild(_iTravar);

        var restaurar = new Button
        {
            Text = "Restaurar a interface padrão",
            TooltipText = "menu, chat e painéis de volta ao lugar e ao tamanho de fábrica",
        };
        restaurar.Pressed += () =>
        {
            Janelas.Restaurar();
            AtualizarAInterface();
        };
        caixa.AddChild(restaurar);
    }

    /// <summary>
    /// Uma linha "rotulo | deslizador | valor" da secao. O `aoMexer` escreve no arranjo; a linha avisa as pecas,
    /// grava e REESCREVE o proprio deslizador com o valor que ficou (a trava pode ter segurado o pedido).
    /// </summary>
    private (HSlider, Label) LinhaDeInterface(VBoxContainer caixa, string rotulo, double passo, Action<double> aoMexer)
    {
        var s = new HSlider { Step = passo };
        var txt = new Label { CustomMinimumSize = new Vector2(56, 0), HorizontalAlignment = HorizontalAlignment.Right };
        s.ValueChanged += v =>
        {
            if (_mostrandoAInterface && !AbrirAsOpcoesMexeDeTeste) return;
            aoMexer(v);
            Janelas.Avisar();
            Janelas.Gravar();
            AtualizarAInterface();
        };
        HBoxContainer h = Linha(rotulo, s);
        h.AddChild(txt);
        caixa.AddChild(h);
        return (s, txt);
    }

    /// <summary>O deslizador de largura ou de altura do menu P: o tamanho muda em volta do MEIO de onde ele esta.</summary>
    private void MexerNoMenu(float? largura = null, float? altura = null)
    {
        Vector2 tela = TelaDaInterface();
        Rect2 r = Janelas.MenuNaTela(Janelas.Arranjo, tela);
        var novo = new Vector2(largura ?? r.Size.X, altura ?? r.Size.Y);
        r = Janelas.PrenderNaTela(new Rect2(r.Position - (novo - r.Size) / 2f, novo), tela, Janelas.MenuMinimo);
        Janelas.GuardarMenu(Janelas.Arranjo, r, tela);
    }

    private void MexerNoChat(float? largura = null, float? altura = null)
    {
        Vector2 tela = TelaDaInterface();
        Vector2 t = Janelas.ChatNaTela(Janelas.Arranjo, tela, Hud.PeDoStatus);
        t = Janelas.PrenderOChat(new Vector2(largura ?? t.X, altura ?? t.Y), tela, Hud.PeDoStatus);
        Janelas.GuardarChat(Janelas.Arranjo, t, tela);
    }

    /// <summary>
    /// OS CONTROLES MOSTRAM O ARRANJO DE AGORA. Roda em toda abertura da tela (o jogador pode ter arrastado as
    /// pecas com ela fechada, ou trocado a resolucao) e depois de cada mexida. SEM SINAL: escrever o valor num
    /// deslizador nao pode contar como "o jogador mexeu" -- abrir as opcoes marcaria tudo como mexido.
    /// </summary>
    private void AtualizarAInterface()
    {
        if (_iMenuL is null || _mostrandoAInterface) return;
        _mostrandoAInterface = true;
        try { MostrarOArranjo(); }
        finally { _mostrandoAInterface = false; }
    }

    private void MostrarOArranjo()
    {
        ArranjoDaInterface a = Janelas.Arranjo;
        Vector2 tela = TelaDaInterface();

        _iDestacar.SetPressedNoSignal(a.MenuDestacado);
        _iDestacar.Disabled = !Janelas.PodeDestacar;
        _iDestacar.TooltipText = Janelas.PodeDestacar
            ? "o menu abre numa janela do sistema (pra pôr noutro monitor); o botão \"Encaixar\" dela traz de volta"
            : "não há como abrir outra janela neste modo";
        _iTravar.SetPressedNoSignal(a.Travado);

        Rect2 menu = Janelas.MenuNaTela(a, tela);
        Mostrar(_iMenuL, _tMenuL, Math.Min(Janelas.MenuMinimo.X, tela.X), tela.X, menu.Size.X, $"{menu.Size.X:0} px");
        Mostrar(_iMenuA, _tMenuA, Math.Min(Janelas.MenuMinimo.Y, tela.Y), tela.Y, menu.Size.Y, $"{menu.Size.Y:0} px");

        Vector2 chat = Janelas.ChatNaTela(a, tela, Hud.PeDoStatus), teto = Janelas.TetoDoChat(tela, Hud.PeDoStatus);
        Mostrar(_iChatL, _tChatL, Janelas.ChatMinimo.X, Math.Max(teto.X, chat.X), chat.X, $"{chat.X:0} px");
        Mostrar(_iChatA, _tChatA, Janelas.ChatMinimo.Y, Math.Max(teto.Y, chat.Y), chat.Y, $"{chat.Y:0} px");

        Mostrar(_iStatus, _tStatus, Janelas.EscalaMinima * 100, Janelas.EscalaMaxima * 100, a.EscalaDoStatus * 100, $"{a.EscalaDoStatus * 100:0}%");
        Mostrar(_iCorpo, _tCorpo, Janelas.EscalaMinima * 100, Janelas.EscalaMaxima * 100, a.EscalaDoCorpo * 100, $"{a.EscalaDoCorpo * 100:0}%");
    }

    private static void Mostrar(HSlider s, Label txt, double minimo, double maximo, double valor, string texto)
    {
        s.MinValue = minimo;
        s.MaxValue = Math.Max(maximo, minimo);
        s.SetValueNoSignal(valor);
        txt.Text = texto;
    }

    // =====================================================================
    // SUPERFICIE DE BANCADA (`--diagjanelas`)
    // =====================================================================
    /// <summary>Os seis deslizadores da secao, pelo que mexem: "menu-l", "menu-a", "chat-l", "chat-a", "status", "corpo".</summary>
    public HSlider? DeslizadorDeInterfaceDeTeste(string qual) => qual switch
    {
        "menu-l" => _iMenuL, "menu-a" => _iMenuA, "chat-l" => _iChatL, "chat-a" => _iChatA,
        "status" => _iStatus, "corpo" => _iCorpo, _ => null,
    };

    /// <summary>O texto ao lado de um deslizador (o valor que ficou).</summary>
    public string ValorDeInterfaceDeTeste(string qual) => qual switch
    {
        "menu-l" => _tMenuL.Text, "menu-a" => _tMenuA.Text, "chat-l" => _tChatL.Text, "chat-a" => _tChatA.Text,
        "status" => _tStatus.Text, "corpo" => _tCorpo.Text, _ => "",
    };

    /// <summary>A caixa "menu (P) numa janela separada".</summary>
    public CheckBox DestacarDeTeste => _iDestacar;
}
