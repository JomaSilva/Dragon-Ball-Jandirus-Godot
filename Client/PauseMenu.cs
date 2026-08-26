using Godot;

namespace Jandirus.Client;

/// <summary>
/// O MENU DE PAUSE (ESC) **E A TELA DE OPCOES DO JOGO INTEIRO**. Video, som, teclas e a porta de saida.
///
/// NAO congela o mundo -- e um jogo em rede, e o servidor continua rodando de qualquer jeito.
/// Pausar a arvore local so ia mentir pro jogador enquanto ele apanha. O que ele faz e tirar
/// o foco do controle e trocar a MUSICA: abrir o menu poe o tema do menu no ar (como o BYOND
/// fazia na criacao de personagem) e fechar devolve o que estava tocando.
///
/// ============================ ELA VIVE NO LOBBY TAMBEM, E NAO SO DENTRO DO MUNDO ============================
/// Pedido do dono: *"as vezes quero mudar o volume no lobby e n da"*. Ele estava certo em duas
/// pontas -- esta e a UNICA tela de opcoes do projeto (o menu P nao tem uma linha de volume nem de
/// resolucao), e ela so nascia no `Boot.AoEntrarNoMundo`. No lobby ela nem estava na arvore.
///
/// **NAO FOI DESMEMBRADA EM DUAS.** O que e de MAQUINA (volume, resolucao, tela cheia, zoom,
/// grafico, teclas, microfone) e o mesmo dos dois lados -- o volume, que e o caso de uso literal do
/// dono, e 100% `AudioServer` e nao encosta em objeto de mundo nenhum. O que muda e o CONTEXTO, e
/// ele e resolvido em um lugar so: <see cref="AjustarAoContexto"/>. Duas telas parecidas divergiriam
/// no primeiro ajuste, que e a razao de o `Tema` existir e de o `Teclas` ter uma tabela so.
///
/// O QUE MUDA SEM MUNDO, item por item, e por que:
///   * o titulo vira "OPCOES" e o "Voltar ao jogo" vira "Fechar" -- nao ha jogo pra voltar;
///   * **"Desconectar" some**. Ele nao e so inutil no lobby, e ATIVAMENTE ruim: apertado na tela de
///     selecao ele derruba a conexao que esta segurando os slots na tela;
///   * "Configurar teclas" fica DESABILITADO COM MOTIVO se a tela de teclas nao estiver montada, em
///     vez de ficar mudo. (Hoje ela nasce no lobby junto com esta -- ver `Boot._Ready` --, entao o
///     caso so acontece em bancada que monta este menu sozinho.);
///   * o zoom ganha um aviso de que so vale ao entrar no mundo -- ele grava, mas nao tem o que
///     previsualizar;
///   * **a musica nao e tocada nem parada**. Ver <see cref="Fechar"/>: a camada `Menu` no lobby nao
///     e deste menu, e a trilha do lobby.
/// ==========================================================================================
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    /// <summary>
    /// A tela de opcoes, pra quem precisar abrir de fora -- os botoes das tres telas do lobby (ver
    /// <see cref="BotoesDoLobby"/>). Mesmo padrao da `TelaDeTeclas.Instancia`.
    /// </summary>
    public static PauseMenu? Instancia { get; private set; }

    public event Action? Desconectar;

    /// <summary>
    /// HA MUNDO NA TELA? E a unica pergunta que separa "pausa" de "opcoes do lobby". Derivada e nao
    /// guardada de proposito: um campo teria que ser atualizado nas duas pontas (entrar e sair do
    /// mundo), e o dia em que uma delas esquecesse, o menu mentiria sobre onde esta.
    /// </summary>
    private static bool NoMundo => World.Instancia != null;

    private Settings _cfg = null!;
    private Control _painel = null!;
    private Label _aviso = null!;
    private Label _titulo = null!;
    private Button _btVoltar = null!, _btDesconectar = null!, _btTeclas = null!;

    /// <summary>O seletor de resolucao e a lista que ele esta mostrando AGORA. Ver <see cref="RecarregarResolucoes"/>.</summary>
    private OptionButton _res = null!;
    private (int L, int A)[] _lista = [];

    /// <summary>A coluna de controles e a rolagem que a segura. Ver <see cref="AjustarAltura"/>.</summary>
    private ScrollContainer _rolagem = null!;
    private VBoxContainer _caixa = null!;

    private const int MargemDoPainel = 18;
    private const int LarguraDaColuna = 420;

    /// <summary>Quanto se deixa de folga entre o painel e a borda da tela, em cima e embaixo somados.</summary>
    private const int RespiroDeBorda = 32;

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

    public override void _ExitTree()
    {
        _soltarRotulo?.Invoke();
        if (GetViewport() is { } vp) vp.SizeChanged -= AjustarAltura;
        if (Instancia == this) Instancia = null;
    }

    /// <summary>Aberto = o jogador nao esta controlando o personagem.</summary>
    public bool Aberto { get; private set; }

    public override void _Ready()
    {
        Instancia = this;
        Layer = 20;   // acima do HUD e de tudo
        _cfg = Boot.Config;
        Montar();

        // METODO NOMEADO E `-=` NO `_ExitTree`, como manda a casa: lambda nao se cancela, e este
        // node atravessa a entrada no mundo e a volta ao login sem morrer.
        GetViewport().SizeChanged += AjustarAltura;
        AjustarAltura();

        // NASCE FECHADO, E AGORA ELE NASCE NO LOBBY (`Boot._Ready`) -- ou seja, sem mundo. O
        // `Fechar` daqui por isso NAO encosta na musica: a camada `Menu` que esta no ar neste
        // instante e a trilha da tela de login, e nao um tema que este menu tenha pedido.
        //
        // O MOTIVO E O QUE O DIARIO DA TRILHA VAI LER: com o motivo padrao, o log daria a primeira
        // linha do jogo como "ESC fechou" sem ninguem ter encostado no ESC, e um diario que mente
        // numa linha nao serve pra julgar as outras.
        Fechar("menu nasceu fechado");
    }

    /// <summary>
    /// O ESC. **ABRIR passa pelo mesmo portao das outras sete telas; FECHAR nao passa por portao
    /// nenhum.**
    ///
    /// ============================ POR QUE O ESC TAMBEM CALA NO EMBATE ============================
    /// O pedido do dono foi literal e amplo -- *"enquanto ta tendo QUALQUER CLASH (ki ou zanzoclash) as
    /// HOTKEYS SAO DESATIVADAS"* -- e este era o UNICO caminho de tecla do cliente que nao perguntava
    /// ao <see cref="Foco"/>. Ele nem passa pelo `Atalhos`: o ESC e registrado como `fixa_sair` com
    /// `Fixa: true` (`Teclas.cs`), entao `Teclas.AtalhoDe(ESC)` e nulo por construcao e o portao que
    /// existe em oito arquivos nao existia neste.
    ///
    /// **E MEDIDO, nao suposto -- sao tres coisas que a fase de medicao achou:**
    ///
    ///   1. **NAO HA PAUSA A PERDER.** Este menu nunca tocou em `GetTree().Paused` (o cabecalho acima
    ///      ja diz por que), entao "o ESC e a saida de emergencia" era falso: o embate corre por baixo
    ///      inteiro -- o anel do `ClashQte` continua fechando, as letras continuam saindo e o prazo
    ///      continua andando nas duas pontas. O que o menu fazia era **CEGAR**: um `ColorRect` preto de
    ///      alpha 0,72 na camada 20 por cima do quick time event, que mora na 5. A letra caia pra 28%
    ///      de brilho com o VBox de opcoes em cima dela.
    ///   2. **E ATRAS DELE HA UM EXPLOIT DE VERDADE**: o botao "Desconectar" daqui. `Drop` chama
    ///      `SoltarDoEmbate` -> `Terminar`, e o `Terminar` desiste em `if (!vivo) return;` ANTES do
    ///      `GolpeDeSaida` -- ou seja, deslogar no meio de um ZanzoClash CANCELA a pancada que fecha o
    ///      embate. Este menu era a unica porta de jogo pra chegar nesse botao.
    ///   3. **O CUSTO TEM NUMERO E E PEQUENO.** O silencio se vence sozinho (ver
    ///      `Foco.AtalhosMudos`): ZanzoClash dura 3,0 a 6,3 s e a colisao de ki 15 s, mais 2 s de folga
    ///      -- o PIOR caso de tela de opcoes adiada e 17 s, e so na colisao de ki. Alt+F4 e fechar a
    ///      janela sao do sistema operacional e nenhum portao nosso os alcanca.
    /// ==========================================================================================
    ///
    /// ============================ FECHAR NUNCA E BLOQUEADO ============================
    /// A metade que importa. O silencio olha o embate, e o embate pode COMECAR com este menu ja aberto
    /// -- e um portao simetrico prenderia o jogador atras da tela preta ate 17 s, sem enxergar a briga
    /// que ele esta perdendo. Ou seja: o remedio viraria a doenca da medicao 1 acima, so que pior.
    ///
    /// Fechar tambem nao e o que o dono pediu calar: o pedido e *"pra n ficar abrindo varios menus"*.
    /// =============================================================================
    ///
    /// E ELE CALA SO A SI MESMO. Nao ha `SetInputAsHandled` no caminho recusado, de proposito: recusar
    /// e nao agir, e nao engolir a tecla de quem vier depois. Isto e um portao de HOTKEY e nao um
    /// portao global de entrada -- a regra da casa desde que um portao global quebrou o andar do dono.
    /// </summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;

        if (Aberto) { Fechar(); GetViewport().SetInputAsHandled(); return; }

        if (Foco.AtalhosMudos) return;
        Abrir();
        GetViewport().SetInputAsHandled();
    }

    public void Abrir()
    {
        Aberto = true;
        _painel.Visible = true;

        // A TELA SE AJUSTA NA ABERTURA, e nao so na montagem. Sao duas coisas que mudam por baixo
        // dela sem ninguem avisar: o CONTEXTO (o mesmo node atende o lobby e o mundo) e o TETO DE
        // RESOLUCAO (o jogador pode ter arrastado a janela pro outro monitor). Um calculo feito so
        // no nascimento e o defeito que este projeto ja pagou na tela de apagar personagem.
        AjustarAoContexto();

        // MUSICA SO DENTRO DO MUNDO -- ver `Fechar`.
        if (NoMundo) AudioDirector.Instance?.Musica(Trilha.Menu(), AudioDirector.Camada.Menu, "ESC abriu");
    }

    /// <summary>
    /// ============================ NO LOBBY ESTE MENU NAO ENCOSTA NA MUSICA ============================
    /// A camada `Menu` do `AudioDirector` tem UM pedido so, e no lobby ele **nao e deste menu**: e a
    /// trilha da tela de login, posta pelo `Boot._Ready` e reposta pelo `Boot.VoltarAoLogin`. Um
    /// `PararCamada(Menu)` ao fechar as opcoes apagaria esse pedido (`_pedidos[camada] = ""`) e
    /// **ninguem o reporia** -- a trilha do lobby morreria de vez, sem nada na tela dizendo por que.
    ///
    /// Dentro do mundo continua exatamente como era: abrir poe o tema do menu, fechar devolve o que
    /// o lugar estava tocando.
    /// ==========================================================================================
    /// </summary>
    /// <inheritdoc cref="AudioDirector.Musica" path="/param[@name='motivo']"/>
    public void Fechar(string motivo = "ESC fechou")
    {
        Aberto = false;
        _painel.Visible = false;
        if (NoMundo) AudioDirector.Instance?.PararCamada(AudioDirector.Camada.Menu, motivo);
    }

    /// <summary>
    /// O QUE ESTE MENU E AGORA: pausa (ha mundo) ou opcoes do lobby (nao ha). Um lugar so decide,
    /// e ele roda em toda abertura -- ver <see cref="Abrir"/>.
    /// </summary>
    private void AjustarAoContexto()
    {
        bool mundo = NoMundo;

        _titulo.Text = mundo ? "PAUSA" : "OPÇÕES";
        _btVoltar.Text = mundo ? "Voltar ao jogo" : "Fechar";
        _aviso.Text = "";

        // ESCONDIDO E NAO DESABILITADO: fora do mundo ele nao tem um "ainda nao" pra explicar --
        // nao ha jogo do qual desconectar, e a conexao que existe (a que segura os slots na tela)
        // e justamente a que ele derrubaria.
        _btDesconectar.Visible = mundo;

        // DESABILITADO **COM MOTIVO NA FRENTE**, que e a diferenca entre "ainda nao da" e um botao
        // que nao faz nada quando se aperta. Era o unico controle desta tela que quebrava sem mundo.
        _btTeclas.Disabled = TelaDeTeclas.Instancia == null;
        _btTeclas.TooltipText = _btTeclas.Disabled
            ? "a tela de teclas não está montada nesta sessão"
            : "";

        RecarregarResolucoes();
        AjustarAltura();
    }

    /// <summary>
    /// A TELA DE OPCOES CABE NA TELA. Ela nunca passa do retangulo visivel: enquanto a coluna couber
    /// inteira, o painel tem a altura dela e nao ha rolagem nenhuma; quando nao couber, a rolagem
    /// aparece e os botoes de baixo continuam alcancaveis.
    ///
    /// **NAO E UM CALCULO UNICO**, e isso e a licao da caixa de apagar personagem (que centrava so
    /// na abertura e saia do lugar quando a resolucao mudava depois): ela roda em toda abertura E no
    /// `SizeChanged` do viewport -- que e justamente o que dispara quando o jogador troca a
    /// resolucao COM esta tela aberta, que e o unico jeito de trocar a resolucao.
    /// </summary>
    private void AjustarAltura()
    {
        if (_rolagem is null || _caixa is null) return;
        float visivel = GetViewport().GetVisibleRect().Size.Y;
        float cabe = visivel - 2 * MargemDoPainel - RespiroDeBorda;
        float precisa = _caixa.GetCombinedMinimumSize().Y;
        _rolagem.CustomMinimumSize = new Vector2(LarguraDaColuna, Math.Max(120f, Math.Min(precisa, cabe)));

        // A BARRA SO EXISTE QUANDO HA O QUE ROLAR. No modo automatico ela aparecia mesmo com tudo
        // cabendo (a altura pedida e a disponivel empatam, e um pixel de margem do proprio
        // `ScrollContainer` ja acende a barra) -- uma barra de rolagem que nao rola nada e ruido que
        // faz o jogador procurar conteudo que nao existe.
        _rolagem.VerticalScrollMode = precisa > cabe
            ? ScrollContainer.ScrollMode.Auto
            : ScrollContainer.ScrollMode.ShowNever;
    }

    /// <summary>
    /// A LISTA DE RESOLUCOES DO MODO ATUAL, refeita do zero. **A de janela e a de tela cheia nao sao
    /// a mesma lista** -- ver `Settings.ResolucoesPara`: em janela o teto e a area util menos a
    /// moldura (1904x993 no monitor do dono), em tela cheia e a nativa do monitor.
    ///
    /// Chamada em toda abertura da tela e a cada vez que o interruptor de tela cheia vira, porque
    /// as duas coisas que alimentam a conta mudam sozinhas por baixo: o modo e o monitor.
    /// </summary>
    private void RecarregarResolucoes()
    {
        _lista = Settings.ResolucoesPara(_cfg.TelaCheia);
        _res.Clear();

        int escolhida = -1;
        for (int i = 0; i < _lista.Length; i++)
        {
            _res.AddItem(Settings.RotuloDaResolucao(_lista[i], _cfg.TelaCheia));
            if (_lista[i].L == _cfg.LarguraJanela && _lista[i].A == _cfg.AlturaJanela) escolhida = i;
            // O QUE ESTA GRAVADO PODE NAO ESTAR NA ESCADA (um config vindo de outra maquina, um
            // 1024x600 de netbook). Ai a tela aponta a maior que ainda CABE, sem mexer no que esta
            // gravado: corrigir o numero de alguem so por ter aberto a tela seria escolher por ele.
            else if (escolhida < 0 && _lista[i].L <= _cfg.LarguraJanela && _lista[i].A <= _cfg.AlturaJanela)
                _res.Selected = i;
        }
        if (escolhida >= 0) _res.Selected = escolhida;
        if (_res.Selected < 0 && _lista.Length > 0) _res.Selected = 0;
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
        // ============================ O MOLDE DA CASA, QUE ESTA TELA NAO SEGUIA ============================
        // **A FOTO DA BANCADA MOSTROU, E O PLACAR VERDE NAO**: esta era a unica tela do jogo sem
        // `Tema.Aplicar` e sem painel de fundo -- so um veu preto e controles soltos. Dentro do
        // mundo, com cenario escuro atras, dava pra viver com isso; **no lobby ela virou texto por
        // cima de texto**, com o formulario de login legivel atraves das opcoes, e com os
        // deslizadores e o seletor no cinza de fabrica do Godot no meio de uma interface escura.
        //
        // E o mesmo defeito que a tela de excluir personagem teve (a unica que fugia do molde e por
        // isso saia torta), so que na tela que esta tarefa acabou de por na frente do jogador.
        // Duas linhas resolvem: o tema desce sozinho pra arvore inteira, e o painel da fundo.
        // ==========================================================================================
        Tema.Aplicar(centro);
        fundo.AddChild(centro);

        PanelContainer moldura = Tema.Painel1(MargemDoPainel);
        centro.AddChild(moldura);

        // ============================ ELA TEM QUE CABER NA MENOR RESOLUCAO QUE ELA PROPRIA OFERECE ============================
        // **MEDIDO pela bancada**: a coluna de controles tem 795 px de altura, e a menor resolucao
        // da lista tem 720. Ou seja, no tamanho PADRAO do jogo os dois ultimos botoes -- "Fechar" e
        // "Fechar o jogo" -- ficavam do lado de fora da tela. Uma tela de opcoes sem saida visivel e
        // pior que nao ter tela de opcoes.
        //
        // A ROLAGEM E A SAIDA CERTA, e nao encolher a fonte: a lista de controles so cresce (voz
        // entrou depois do som, teclas depois do video), e qualquer altura cravada aqui volta a
        // estourar no proximo controle. Ver `AjustarAltura` -- ela e REGRA e nao calculo unico:
        // roda em toda abertura E toda vez que o retangulo visivel muda, que e exatamente o que
        // acontece quando alguem troca a resolucao COM ESTA TELA ABERTA.
        // ==========================================================================================
        _rolagem = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        moldura.AddChild(_rolagem);

        // A LARGURA MORA NA ROLAGEM, E NAO NA COLUNA (ver `AjustarAltura`). Se a coluna tivesse
        // largura minima propria de 420 dentro de uma rolagem de 420, o dia em que a barra de
        // rolagem aparecesse ela comeria ~12 px da direita e a coluna seria CORTADA -- justamente
        // onde ficam os "90%" dos volumes. Com o minimo na rolagem, a coluna encolhe junto.
        _caixa = new VBoxContainer();
        _caixa.AddThemeConstantOverride("separation", 5);
        _rolagem.AddChild(_caixa);
        // apelido local: o resto desta funcao (as ~30 linhas de controles) ja escrevia `caixa`, e
        // trocar todas elas por `_caixa` seria churn sem leitor nenhum ganhando com isso
        VBoxContainer caixa = _caixa;

        _titulo = new Label { Text = "PAUSA", HorizontalAlignment = HorizontalAlignment.Center };
        _titulo.AddThemeFontSizeOverride("font_size", 26);
        caixa.AddChild(_titulo);
        caixa.AddChild(new HSeparator());

        // --- video ---
        caixa.AddChild(Secao("Video"));

        // A LISTA E DERIVADA E NAO CRAVADA -- `RecarregarResolucoes` a monta pelo modo e pelo
        // monitor atuais, e roda de novo a cada abertura da tela.
        _res = new OptionButton();
        _res.ItemSelected += i =>
        {
            // ESCOLHER RESOLUCAO **NAO DESLIGA MAIS A TELA CHEIA**. Havia um `_cfg.TelaCheia = false`
            // aqui, e era metade do defeito que o dono descreveu: com ele, "tela cheia com uma
            // resolucao menor" era um estado que o jogo nao conseguia alcancar -- ou voce estava em
            // tela cheia (e a resolucao era ignorada), ou tinha escolhido uma resolucao (e tinha
            // saido da tela cheia). Hoje a resolucao e a BASE DE DESENHO e vale nos dois modos; ver
            // o bloco "O QUE A RESOLUCAO SIGNIFICA" no `Settings`.
            (_cfg.LarguraJanela, _cfg.AlturaJanela) = _lista[(int)i];
            AplicarEGravar();
        };
        caixa.AddChild(Linha("Resolucao", _res));

        var cheia = new CheckBox { Text = "tela cheia", ButtonPressed = _cfg.TelaCheia };
        cheia.Toggled += on =>
        {
            _cfg.TelaCheia = on;
            // APLICA PRIMEIRO, RECARREGA DEPOIS: e o `Aplicar` que corta a resolucao gravada pelo
            // teto do modo novo (1920x1080 nao cabe numa JANELA neste monitor), e a lista tem que
            // mostrar o que ficou, e nao o que era.
            AplicarEGravar();
            RecarregarResolucoes();
        };
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
        // NO LOBBY ELE GRAVA MAS NAO TEM O QUE PREVISUALIZAR (`World.Instancia` e nulo, e a chamada
        // ja era guardada). O controle continua ali porque a preferencia e de MAQUINA como as
        // outras -- o que ele ganha e a frase que explica por que nada se mexe na tela.
        linhaZoom.TooltipText = "vale quando você entrar no mundo";
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
        //
        // E ELA FUNCIONA NO LOBBY: a `TelaDeTeclas` nao precisa de mundo (as duas fontes que ela le,
        // `FormasDespertas.Minhas()` e `Verbos.Da(cat)`, ja saem vazias sem cliente), entao ela
        // passou a nascer junto desta tela no `Boot._Ready` -- e o lobby ganhou configuracao de
        // teclas de graca, que e coerente com o paragrafo acima.
        caixa.AddChild(Secao("Teclas"));
        _btTeclas = new Button { Text = "Configurar teclas e atalhos" };
        _btTeclas.Pressed += () => TelaDeTeclas.Instancia?.Abrir();
        caixa.AddChild(_btTeclas);

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

        _btVoltar = new Button { Text = "Voltar ao jogo" };
        // o botao e o ESC sao o mesmo gesto -- e o motivo padrao ja diz isso
        _btVoltar.Pressed += () => Fechar();
        caixa.AddChild(_btVoltar);

        // SO APARECE DENTRO DO MUNDO -- ver `AjustarAoContexto`.
        _btDesconectar = new Button { Text = "Desconectar" };
        _btDesconectar.Pressed += () =>
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
                // GRAVA ANTES DE DESLIGAR. O `Stop` sozinho limpa a lista de jogadores sem passar
                // por `Persistir` -- ver `GameServer.SalvarEParar`.
                srv.SalvarEParar();
            }
            GameClient.Instance?.Desconectar();
            Fechar();
            Desconectar?.Invoke();
        };
        caixa.AddChild(_btDesconectar);

        // A MESMA PORTA DO BOTAO DO LOBBY E DO X DA JANELA. Ver `Saida.Encerrar`: era aqui que
        // morava o unico `GetTree().Quit()` de producao do projeto, e ele saia sem gravar nada.
        var fechar = new Button { Text = "Fechar o jogo" };
        fechar.Pressed += () => Saida.Encerrar(GetTree(), "botão Fechar o jogo");
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
