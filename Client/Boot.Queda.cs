using Godot;

namespace Jandirus.Client;

/// <summary>
/// A QUEDA DE CONEXAO -- o que o jogo faz quando o servidor some.
///
/// O dono (2026-10-10): *"faça tb q caso tenha desconexao com o servidor o jogo te joga de volta pro menu
/// principal com a mensagem de desconectado do server"*.
///
/// ============================ O QUE HAVIA ============================
/// So a queda DURANTE A TELA DE CARREGAMENTO era tratada: um assinante do `GameClient.Caiu` soltava a
/// cobertura e voltava ao login, e o comentario dele dizia o resto -- *"uma queda com o jogador ja DENTRO
/// do mundo e outro assunto (hoje o mundo congela no lugar)"*. O servidor caia, o corpo parava de
/// responder, os outros congelavam no meio do passo, e a unica saida era adivinhar que a conexao tinha
/// ido e apertar ESC > Desconectar. Quem errava a porta na tela de login ficava em "conectando..." pra
/// sempre, pelo mesmo motivo.
///
/// ============================ POR ESTADO, E NAO POR EVENTO ============================
/// O `Boot` pergunta ao `GameClient`, A CADA QUADRO, se ha uma queda que ninguem tratou
/// (<see cref="GameClient.QuedaPendente"/>). Um cliente fora do login e sem conexao esta SEMPRE errado,
/// e um evento perdido -- um assinante que estourou antes deste, uma tela que ainda nao tinha nascido --
/// deixaria o mundo congelado na tela pra sempre, que era exatamente o defeito. O estado fica de pe ate
/// alguem tratar; o evento continua existindo pra quem so quer saber da hora.
///
/// SAIR NAO E CAIR: o botao "Desconectar" do menu de pausa e o "Sair" da selecao passam pelo
/// `GameClient.Desconectar`, que nao acende a queda -- quem saiu por vontade nao le aviso nenhum.
/// ====================================================================================
/// </summary>
public partial class Boot
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagqueda`): a queda fora da tela de carregamento volta a ser ignorada
	/// -- o estado de antes de 2026-10-10, em que o mundo congelava no lugar. Lido pela propria regra, em
	/// <see cref="AoCairAConexao"/>.
	/// </summary>
	public static bool QuedaNoMundoCongelaDeTeste;

	/// <summary>O nome do node do aviso de queda. A bancada o procura por ele.</summary>
	private const string NomeDoAvisoDeQueda = "AvisoDeQueda";

	public override void _Process(double delta)
	{
		if (GameClient.Instance is { QuedaPendente: { } motivo } cli) AoCairAConexao(cli, motivo);
	}

	/// <summary>
	/// A CONEXAO CAIU, e ninguem tratou ainda. Tres lugares em que o jogador pode estar:
	///
	///   * NA TELA DE LOGIN (o "Entrar" que nao achou o servidor): nao ha pra onde voltar -- so a linha de
	///     estado, que antes ficava em "conectando..." pra sempre.
	///   * SOB A TELA DE CARREGAMENTO: a cobertura so cai quando um quadro com o mundo e desenhado, e com o
	///     servidor morto o mundo nunca vem. Ela e solta ANTES de o lobby ser remontado, e a ordem importa:
	///     o `Soltar` espera um quadro desenhado pra esconder, entao o lobby aparece POR BAIXO dela e nao
	///     ha um quadro de nada no meio.
	///   * NA SELECAO, NA CRIACAO OU DENTRO DO MUNDO: tudo e derrubado, o menu principal volta, e o aviso
	///     diz o que aconteceu.
	///
	/// E UM QUARTO MOTIVO, que entra pelos mesmos lugares: A VERSAO NAO BATEU COM A DO SERVIDOR (dono,
	/// 2026-10-11). O `GameClient` acende a mesma queda e guarda o aviso de versao; aqui ele troca o titulo e a
	/// frase do painel -- ver `Boot.Versao.cs`.
	/// </summary>
	private void AoCairAConexao(GameClient cli, string motivo)
	{
		bool cobrindo = _carga is { NoAr: true };
		if (QuedaNoMundoCongelaDeTeste && !cobrindo) return;

		bool alemDoLogin = cobrindo || _selecao != null || _criacao != null || GetNodeOrNull("World") != null;
		Jandirus.Core.Versao.Aviso? versao = cli.TomarAvisoDeVersao();   // nulo = queda comum
		cli.TratarQueda();

		if (cobrindo) _carga!.Soltar();
		if (!alemDoLogin)
		{
			// O DESENCONTRO DE VERSAO BARRA ANTES DO LOGIN, entao e AQUI que ele quase sempre chega -- e ele e
			// aviso que nao da pra nao ver: o painel, e nao so a linha de estado.
			if (versao is { } barrado) AvisarDaVersao(barrado);
			else if (IsInstanceValid(_status)) _status.Text = $"sem conexão: {motivo}.";
			return;
		}

		// O MENU DE PAUSA SOBREVIVE A VOLTA (ele e uma das telas de maquina), e aberto ele ficaria por cima
		// do aviso, ainda com os botoes de dentro do mundo.
		if (_pause is { Aberto: true }) _pause.Fechar("a conexao caiu");

		GD.Print($"[client] a conexao caiu ({motivo}): de volta ao menu principal");
		VoltarAoLogin();
		if (versao is { } desencontro) AvisarDaVersao(desencontro);
		else AvisarDaQueda(motivo);
	}

	/// <summary>
	/// O AVISO DE QUEDA: o painel com "DESCONECTADO DO SERVIDOR" e o motivo. A linha de estado do formulario
	/// repete a frase, pra ela continuar la depois do OK.
	/// </summary>
	private void AvisarDaQueda(string motivo)
	{
		_status.Text = $"desconectado do servidor: {motivo}.";
		string frase = motivo.Length > 0 ? char.ToUpperInvariant(motivo[0]) + motivo[1..] : "A conexão caiu";
		PainelDeAviso("DESCONECTADO DO SERVIDOR", $"{frase}. Você voltou ao menu principal.");
	}

	/// <summary>
	/// O AVISO QUE NAO DA PRA NAO VER: um veu sobre o menu principal com um titulo, uma frase e um OK. Um
	/// painel so pros dois avisos que mandam o jogador de volta ao menu -- a queda de conexao e a versao que nao
	/// bate (`Boot.Versao.cs`).
	///
	/// O VEU SEGURA O CLIQUE de proposito: quem estava no meio de uma luta chega aqui apertando botao, e o
	/// primeiro clique nao pode cair em "Hospedar partida".
	///
	/// UM AVISO POR VEZ: o que ja estava na tela sai NA HORA (e nao no fim do quadro), senao o novo nasceria
	/// com outro nome e ficariam dois veus empilhados -- o OK de cima revelaria o de baixo.
	/// </summary>
	private void PainelDeAviso(string tituloDoAviso, string fraseDoAviso)
	{
		if (!IsInstanceValid(_painel) || _painel.GetParent() is not { } camada) return;
		if (camada.GetNodeOrNull(NomeDoAvisoDeQueda) is { } anterior)
		{
			camada.RemoveChild(anterior);
			anterior.QueueFree();
		}

		var veu = new ColorRect
		{
			Name = NomeDoAvisoDeQueda,
			Color = new Color(0, 0, 0, 0.6f),
			AnchorRight = 1, AnchorBottom = 1,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		Tema.Aplicar(centro);
		veu.AddChild(centro);

		PanelContainer moldura = Tema.Painel1(20);
		centro.AddChild(moldura);
		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
		caixa.AddThemeConstantOverride("separation", 10);
		moldura.AddChild(caixa);

		var titulo = new Label { Text = tituloDoAviso, HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 20);
		titulo.AddThemeColorOverride("font_color", Tema.Perigo);
		caixa.AddChild(titulo);

		var texto = new Label
		{
			Text = fraseDoAviso,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		caixa.AddChild(texto);

		var ok = new Button { Text = "OK" };
		ok.Pressed += veu.QueueFree;
		caixa.AddChild(ok);

		camada.AddChild(veu);
		ok.CallDeferred(Control.MethodName.GrabFocus);
	}

	// =====================================================================
	// O QUE A BANCADA `--diagqueda` PERGUNTA
	// =====================================================================
	/// <summary>O aviso de queda que esta na tela agora, ou nulo.</summary>
	private Node? AvisoDeQueda() =>
		IsInstanceValid(_painel) && _painel.GetParent()?.GetNodeOrNull(NomeDoAvisoDeQueda) is { } aviso
		&& !aviso.IsQueuedForDeletion() ? aviso : null;

	/// <summary>O que o aviso de queda esta dizendo (titulo e frase), ou nulo se nao ha aviso na tela.</summary>
	internal string? AvisoDeQuedaNaTela() =>
		AvisoDeQueda() is { } aviso
			? string.Join(" | ", aviso.FindChildren("*", nameof(Label), true, false).OfType<Label>().Select(l => l.Text))
			: null;

	/// <summary>Aperta o OK do aviso de queda, como o dedo do jogador. Falso se nao ha aviso.</summary>
	internal bool ApertarOkDoAvisoDeQueda()
	{
		if (AvisoDeQueda()?.FindChildren("*", nameof(Button), true, false).OfType<Button>().FirstOrDefault() is not { } ok)
			return false;
		ok.EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}

	/// <summary>O formulario do menu principal esta de pe e a mostra?</summary>
	internal bool MenuPrincipalNaTela =>
		IsInstanceValid(_painel) && _painel.IsInsideTree() && !_painel.IsQueuedForDeletion() && _painel.Visible;

	/// <summary>A linha de estado do formulario do menu principal (vazia sem formulario).</summary>
	internal string EstadoDoMenuPrincipal => IsInstanceValid(_status) ? _status.Text : "";

	/// <summary>O mundo esta montado (o node `World` existe debaixo do `Boot`)?</summary>
	internal bool MundoMontado => GetNodeOrNull("World") is { } m && !m.IsQueuedForDeletion();

	/// <summary>
	/// PALCO DA `--diagqueda`: disca de novo no servidor com a conta da linha de comando -- a mesma chamada
	/// do fim do `AutoConectar`. Com o modo automatico ligado, a lista de slots que volta entra sozinha no
	/// mundo, como na primeira vez.
	/// </summary>
	internal void DiscarDeNovoDeTeste()
	{
		string[] args = OS.GetCmdlineArgs();
		GameClient.Instance?.Conectar("127.0.0.1", PortaDaRede(),
			Arg(args, "--conta") ?? _autoNome, Arg(args, "--senha") ?? "teste");
	}
}
