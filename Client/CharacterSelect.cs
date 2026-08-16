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

    /// <summary>A confirmacao de apagar, enquanto estiver na tela. Nulo = nenhuma aberta.</summary>
    private Control? _pergunta;

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
        // A CONFIRMACAO E FILHA DESTA CAMADA, entao o laco acima ja a apagou -- soltar a referencia
        // aqui evita ficar apontando pra um node liberado (e o ESC tentar fechar o que nao existe).
        _pergunta = null;

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

            // NEM CLASSE NEM BP. Aqui nao existe personagem em jogo, logo nao existe scouter --
            // e a CLASSE nunca aparece em situacao nenhuma, com scouter ou sem. O servidor ja
            // manda os dois campos censurados (`SlotVisivel`); apagar os rotulos e o outro lado
            // da mesma regra, pra que ninguem os reponha achando que o dado esta ali.
            //
            // O que sobra e o que identifica o personagem sem entregar o jogo: nome, raca, idade.
            caixa.AddChild(new HSeparator());
            caixa.AddChild(Info($"{s.Raca}  ·  {s.Idade} anos"));

            caixa.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
            var jogar = new Button { Text = "Jogar" };
            int alvo = indice;
            jogar.Pressed += () => Jogar?.Invoke(alvo);
            caixa.AddChild(jogar);

            // APAGAR FICA LONGE DE JOGAR, e discreto de proposito: e o unico botao desta tela que
            // destroi alguma coisa, e ele divide espaco com o que se aperta toda vez que se entra
            // no jogo. Rotulo pequeno, cor de aviso, e a confirmacao pedindo o NOME.
            var apagar = new Button
            {
                Text = "Excluir personagem",
                TooltipText = "apaga este personagem para sempre",
            };
            apagar.AddThemeFontSizeOverride("font_size", 12);
            apagar.AddThemeColorOverride("font_color", new Color("c96a6a"));
            apagar.Pressed += () => PerguntarSeApaga(alvo, s.Nome);
            caixa.AddChild(apagar);
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

    /// <summary>
    /// A CONFIRMACAO DE APAGAR: pede o NOME digitado, e nao um "tem certeza?".
    ///
    /// ============================ POR QUE NAO BASTA SIM/NAO ============================
    /// Quem clicou em "Excluir" por engano clica em "Sim" por engano tambem -- os dois botoes ficam
    /// no mesmo lugar da tela e o dedo ja esta em movimento. Digitar o nome obriga a LER qual dos
    /// tres personagens vai morrer, que e exatamente a informacao que falta em um clique errado.
    ///
    /// E o botao so acende quando o texto BATE. Assim o erro nao chega nem a ser possivel de
    /// cometer: nao ha "confirmar" pra apertar enquanto o nome estiver errado.
    ///
    /// O servidor confere de novo (`GameServer.DeleteChar`) -- esta tela e conveniencia, nao trava.
    /// ==================================================================================
    ///
    /// ==================== POR QUE ELA NAO E MAIS UM `ConfirmationDialog` ====================
    /// O dono: *"a tela de DELETAR PERSONAGEM ta TODA TORTA, e se eu coloco em FULL SCREEN o jogo e
    /// dps em JANELA, ela MUDA DE POSICAO e fica todo torto"*. Eram DOIS defeitos, e os dois vinham
    /// do TIPO DO NODE, nao de uma conta de posicao errada:
    ///
    /// 1. SOBREPOSICAO. Um `AcceptDialog` da a TODO filho Control o retangulo INTEIRO da area de
    ///    conteudo -- o mesmo retangulo onde o Label do `DialogText` ja esta. Medido: aviso e campo
    ///    ocupavam o mesmo `[P: (8,8), S: (538,101)]`, 100% de sobreposicao. Por isso o nome digitado
    ///    saia por cima do texto de aviso na foto dele.
    /// 2. DESLOCAMENTO. `PopupCentered()` centra UMA VEZ, na hora de abrir, e a subjanela embutida
    ///    guarda a posicao em pixels absolutos: trocar de tela cheia pra janela nao re-centra nada.
    ///    Medido: abrir em 1920x1080 e voltar pra 1280x720 deixava o dialogo 320 x 180 px fora do
    ///    centro (a regra e `(viewport_da_abertura - viewport_atual) / 2`).
    ///
    /// O CONSERTO E USAR O MOLDE QUE O RESTO DO JOGO JA USA e que se centra sozinho de graça:
    /// `Control` ancorado (0..1) -> `CenterContainer` -> `Tema.Painel1` -> `VBoxContainer`. E o mesmo
    /// do `PauseMenu`, do `TelaDeInventario`, da criacao -- e o da propria fileira de slots, tres
    /// linhas acima. Ancora nao tem "hora de centrar": ela e recalculada a cada resize.
    /// =======================================================================================
    /// </summary>
    private void PerguntarSeApaga(int slot, string nome)
    {
        FecharPergunta();   // nunca duas na tela ao mesmo tempo

        // O FUNDO ESCURO NAO E ENFEITE: ele e o que faz a caixa ser MODAL sem uma subjanela --
        // um Control com `MouseFilter` de parar come o clique, entao nao da pra apertar "Jogar"
        // num slot atras da pergunta de apagar outro.
        var fundo = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.72f),
            AnchorRight = 1, AnchorBottom = 1,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        Tema.Aplicar(fundo);   // o dialogo do Godot nascia CINZA, fora da paleta do jogo
        AddChild(fundo);
        _pergunta = fundo;

        var centro = new CenterContainer
        {
            AnchorRight = 1, AnchorBottom = 1,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        fundo.AddChild(centro);

        PanelContainer painel = Tema.Painel1(20);
        centro.AddChild(painel);

        // UMA COLUNA SO, e tudo empilhado nela. A largura fixa e o que segura o aviso: sem ela o
        // VBox cresce ate o tamanho da linha mais longa e o texto vira uma tira.
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        col.AddThemeConstantOverride("separation", 12);
        painel.AddChild(col);

        var titulo = new Label { Text = "EXCLUIR PERSONAGEM", HorizontalAlignment = HorizontalAlignment.Center };
        titulo.AddThemeFontSizeOverride("font_size", 22);
        titulo.AddThemeColorOverride("font_color", Tema.Perigo);
        col.AddChild(titulo);
        col.AddChild(new HSeparator());

        // O NOME EM DESTAQUE E SOZINHO: e a informacao que falta em um clique errado -- QUAL dos
        // tres vai morrer. Ele estava afogado no meio do paragrafo de aviso.
        var alvo = new Label { Text = nome, HorizontalAlignment = HorizontalAlignment.Center };
        alvo.AddThemeFontSizeOverride("font_size", 26);
        alvo.AddThemeColorOverride("font_color", Tema.Destaque);
        col.AddChild(alvo);

        var aviso = new Label
        {
            Text = "vai ser apagado PARA SEMPRE — o BP, as skills, os itens e os cargos "
                 + "deste personagem nao voltam.",
            AutowrapMode = TextServer.AutowrapMode.Word,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        aviso.AddThemeColorOverride("font_color", Tema.TextoFraco);
        col.AddChild(aviso);

        col.AddChild(new HSeparator());

        // O CAMPO GANHOU ROTULO PROPRIO. Antes a instrucao era a ultima linha do paragrafo de aviso,
        // e o campo desenhava POR CIMA dela.
        col.AddChild(Tema.Rotulo("digite o nome do personagem para confirmar"));

        var campo = new LineEdit
        {
            PlaceholderText = nome,
            MaxLength = 32,
            CustomMinimumSize = new Vector2(0, 34),
        };
        col.AddChild(campo);

        // OS DOIS BOTOES LADO A LADO, do mesmo tamanho e no fim da coluna. O de apagar nasce
        // APAGADO e so acende quando o texto BATE -- a regra nao mudou, so mudou onde ela mora
        // (era `caixa.GetOkButton()`, do Godot; agora e um botao nosso).
        var linha = new HBoxContainer();
        linha.AddThemeConstantOverride("separation", 10);
        col.AddChild(linha);

        var cancelar = new Button
        {
            Text = "Cancelar",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        cancelar.Pressed += FecharPergunta;
        linha.AddChild(cancelar);

        var excluir = new Button
        {
            Text = "Excluir",
            Disabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        excluir.AddThemeColorOverride("font_color", Tema.Perigo);
        linha.AddChild(excluir);

        bool Bate(string t) => string.Equals(t.Trim(), nome, StringComparison.OrdinalIgnoreCase);

        void Apagar()
        {
            if (!Bate(campo.Text)) return;
            GameClient.Instance?.SendDeleteChar(slot, campo.Text.Trim());
            // A PERGUNTA MORRE COM A RESPOSTA -- sem isto cada clique deixaria um node na arvore.
            FecharPergunta();
        }

        campo.TextChanged += t => excluir.Disabled = !Bate(t);
        campo.TextSubmitted += _ => Apagar();   // Enter faz o mesmo que o botao, e so se bater
        excluir.Pressed += Apagar;

        campo.GrabFocus();
    }

    /// <summary>
    /// ESC DESISTE DE APAGAR -- e o que o dialogo do Godot fazia de graça e agora e nosso.
    ///
    /// ============================ POR QUE `_Input` E NAO `_UnhandledKeyInput` ============================
    /// **MEDIDO** (`--diagapagar`): com o `_UnhandledKeyInput`, o ESC fechava a caixa **sem o campo em
    /// foco e nao fechava com ele** -- `[com o campo em foco=False, sem foco=True]`. E a caixa abre
    /// SEMPRE com o campo em foco (`campo.GrabFocus()`, no fim do `PerguntarSeApaga`), entao a unica
    /// saida de teclado da tela mais destrutiva do jogo nunca funcionou no estado em que ela existe.
    ///
    /// A causa e a ordem do motor: um `LineEdit` COM FOCO recebe a tecla pelo `gui_input` e o evento
    /// nao chega ao `_unhandled_*` de ninguem. Por isso as outras telas com campo de texto deste
    /// projeto ja liam pelo `_Input` -- ver `Chat._Input:207`, com o comentario que diz a mesma coisa.
    ///
    /// CONSUMIR AQUI E OBRIGATORIO (`SetInputAsHandled`): o menu de pause escuta a MESMA tecla no
    /// `_UnhandledInput`, e desistir de apagar nao pode abrir o menu junto.
    /// ===================================================================================================
    /// </summary>
    public override void _Input(InputEvent evento)
    {
        if (_pergunta == null) return;
        if (evento is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        FecharPergunta();
        GetViewport().SetInputAsHandled();
    }

    private void FecharPergunta()
    {
        _pergunta?.QueueFree();
        _pergunta = null;
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
