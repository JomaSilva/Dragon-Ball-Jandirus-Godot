using Godot;

namespace Jandirus.Client;

/// <summary>
/// A ABERTURA DO JOGO -- QUANDO ela roda. (O que ela e esta na <see cref="AberturaDoJogo"/>; o titulo que fica no alto
/// do menu, no <see cref="TituloDoJogo"/>.)
///
/// ============================ SO NUMA PARTIDA ABERTA POR GENTE ============================
/// Ha mais de cem bancadas que abrem este jogo e medem tempo, e um diretor de trailer que filma: em nenhuma delas a
/// abertura pode rodar. Nao ha uma bandeira "sou bancada" no projeto, e adivinhar pelo nome do argumento erra no dia
/// em que alguem criar um nome novo. O que o `Boot` ja sabe de uma corrida automatica sao tres FATOS, e a abertura
/// pergunta por eles:
///
///   * a JANELA: sem janela (`--headless`) nao ha o que mostrar; e a janela de bancada nasce com `--semfoco` (ver o
///     `_Ready`: o lancador a poe em toda corrida de janela, pra ela nao roubar o teclado do dono);
///   * o LOGIN AUTOMATICO: `--host` e `--connect` entram sozinhos (ver <see cref="AutoConectar"/>) -- e o caminho de
///     todo robo que precisa de mundo, e o do trailer;
///   * UM ROBO NASCIDO NO LOBBY: os que medem a propria tela de login (`--diagopcoes`, `--diagcarga`,
///     `--diagmarcavel`, `--saidalobby`...) entram como filhos do `Boot` no `_Ready`. Um filho cuja classe se chama
///     `Robo...` e um robo -- o nome da classe e a convencao da casa, e nao o do argumento.
///
/// Fora disso ha uma pessoa na frente da tela. E ha duas chaves pra ela: `--semabertura` na linha de comando, e a
/// caixa "abertura animada ao abrir o jogo" das opcoes (<see cref="Settings.AberturaAnimada"/>).
///
/// `--abertura` FORCA (com janela): e como a bancada `--diagabertura` a ve rodar numa janela sem foco.
///
/// ============================ UMA VEZ POR EXECUCAO ============================
/// Quem a chama e o `_Ready`, que roda uma vez. Voltar ao menu -- cair do servidor, "Desconectar", sair da selecao --
/// remonta o login (<see cref="MontarLogin"/>) e nao passa por aqui: o titulo ja nasce no alto. E a propria
/// `AberturaDoJogo.Comecar` recusa uma segunda chamada.
/// </summary>
public partial class Boot
{
	/// <summary>O titulo do alto do menu principal DE AGORA (cada <see cref="MontarLogin"/> faz um novo).</summary>
	private TituloDoJogo? _tituloDoMenu;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagabertura`): a abertura roda tambem numa corrida automatica -- com a janela de
	/// bancada, com login automatico e com um robo no lobby. Lido pela propria regra, em <see cref="PorQueNaoAbre"/>.
	/// </summary>
	public static bool AberturaEmCorridaAutomaticaDeTeste;

	/// <summary>Por que a abertura NAO rodou neste processo ("" = rodou). Pro log e pra bancada.</summary>
	internal static string MotivoDeNaoAbrir { get; private set; } = "";

	/// <summary>
	/// A ULTIMA LINHA DO `_Ready`: decide se esta e uma partida aberta por gente e, se for, comeca a abertura por
	/// cima do menu que acabou de ser montado.
	/// </summary>
	private void AbrirComOTitulo()
	{
		string[] args = OS.GetCmdlineArgs();

		// `--aberturasemcalma`: o defeito injetado da rodada de mesmo nome da `--diagabertura` -- o ensaio do
		// `Aquecimento` corre por cima do titulo em movimento. Tem de valer antes de a abertura nascer.
		if (Array.IndexOf(args, "--aberturasemcalma") >= 0)
		{
			AberturaDoJogo.SemPedirCalmaDeTeste = true;
			GD.Print("[abertura] SEM PEDIR CALMA ao aquecimento, por --aberturasemcalma (o defeito injetado da --diagabertura)");
		}

		// `--diagabertura`: A BANCADA DA ABERTURA. Nasce aqui, depois do lobby, como a `--diagopcoes` e pelo mesmo
		// motivo: o menu que ela olha e o do jogador. Ver RoboDaAbertura.
		if (Array.IndexOf(args, "--diagabertura") >= 0) AddChild(new RoboDaAbertura { Name = "RoboDaAbertura" });

		TentarAbrir(args);
	}

	/// <summary>A DECISAO E A LARGADA. Devolve se uma abertura comecou.</summary>
	private bool TentarAbrir(string[] args)
	{
		string? porque = PorQueNaoAbre(args, DisplayServer.GetName() != "headless", RoboNoLobby(), Config.AberturaAnimada);
		// (a arte depois: e a unica pergunta que le arquivo)
		porque ??= TituloDoJogo.ArteImportada ? null : "a arte do titulo (Assets/UI) nao foi importada";
		// (e a trava de uma vez por execucao e da propria abertura: ver `AberturaDoJogo.Comecar`)
		if (porque == null && AberturaDoJogo.Comecar(this, TituloDoMenuNaTela) == null) porque = "ja rodou nesta execucao";

		if (AberturaDoJogo.Vezes == 0) MotivoDeNaoAbrir = porque ?? "";
		GD.Print(porque != null ? $"[abertura] nao roda: {porque}"
			   : Array.IndexOf(args, "--abertura") >= 0 ? "[abertura] comecou (forcada por --abertura)" : "[abertura] comecou");
		return porque == null;
	}

	/// <summary>O titulo do menu principal, se ele esta de pe -- e pra onde a abertura sobe.</summary>
	private TituloDoJogo? TituloDoMenuNaTela() =>
		_tituloDoMenu != null && IsInstanceValid(_tituloDoMenu) && _tituloDoMenu.IsInsideTree() && !_tituloDoMenu.IsQueuedForDeletion()
			? _tituloDoMenu : null;

	/// <summary>O nome da classe do primeiro robo que nasceu como filho do `Boot`, ou nulo.</summary>
	private string? RoboNoLobby()
	{
		foreach (Node filho in GetChildren())
			if (filho.GetType().Name.StartsWith("Robo", StringComparison.Ordinal)) return filho.GetType().Name;
		return null;
	}

	/// <summary>
	/// POR QUE A ABERTURA NAO RODA NESTA CORRIDA -- ou nulo, se roda. Funcao PURA dos fatos (a linha de comando, se
	/// ha janela, que robo nasceu no lobby, a caixa das opcoes): a bancada a chama com corridas que nao existem.
	/// A ordem das perguntas e a ordem de quem manda.
	/// </summary>
	internal static string? PorQueNaoAbre(string[] args, bool temJanela, string? roboNoLobby, bool ligadaNasOpcoes)
	{
		bool Tem(string bandeira) => Array.IndexOf(args, bandeira) >= 0;

		if (!temJanela) return "sem janela (--headless)";
		if (Tem("--abertura")) return null;   // FORCADA: passa por cima de tudo daqui pra baixo
		if (Tem("--semabertura")) return "desligada por --semabertura";
		if (!ligadaNasOpcoes) return "desligada nas opcoes";

		// (defeito injetado: nenhum dos tres fatos de corrida automatica e perguntado -- ver `AberturaEmCorridaAutomaticaDeTeste`)
		if (AberturaEmCorridaAutomaticaDeTeste) return null;

		if (Tem("--semfoco")) return "janela de bancada (--semfoco)";
		if (Tem("--host") || Tem("--connect")) return "login automatico (--host ou --connect)";
		if (roboNoLobby != null) return $"um robo nasceu no lobby ({roboNoLobby})";
		return null;
	}

	// =====================================================================
	// O QUE A BANCADA `--diagabertura` PERGUNTA E PEDE
	// =====================================================================
	/// <summary>O titulo do alto do menu principal de agora, ou nulo se o menu nao esta de pe.</summary>
	internal TituloDoJogo? TituloDoMenuDeTeste => TituloDoMenuNaTela();

	/// <summary>A tela de selecao de personagem que esta de pe, ou nula.</summary>
	internal CharacterSelect? SelecaoDeTeste => _selecao != null && IsInstanceValid(_selecao) && !_selecao.IsQueuedForDeletion() ? _selecao : null;

	/// <summary>
	/// PALCO DA `--diagabertura`: chama de novo a MESMA porta do `_Ready` -- o que uma volta ao menu faria se
	/// reprisasse a abertura. Devolve se uma abertura comecou.
	/// </summary>
	internal bool TentarAbrirDeNovoDeTeste() => TentarAbrir(OS.GetCmdlineArgs());

	/// <summary>PALCO DA `--diagabertura`: mais uma abertura por cima do menu, sem contar como a do jogo (ver `AberturaDoJogo.ComecarDeTeste`).</summary>
	internal AberturaDoJogo AberturaDeTeste(double? posadaEm = null) => AberturaDoJogo.ComecarDeTeste(this, TituloDoMenuNaTela, posadaEm);

	/// <summary>
	/// PALCO DA `--diagabertura`: com <paramref name="ligado"/>, daqui pra frente a lista de slots que chegar entra
	/// sozinha no mundo (o modo do `--host`), com um personagem de bancada -- pra chegar ao "Desconectar" sem atravessar
	/// as oito paginas da criacao. Desligado, a lista volta a parar na tela de selecao.
	/// </summary>
	internal void EntrarSozinhoDeTeste(string nome, string raca, bool ligado)
	{
		_autoNome = nome;
		_autoRaca = raca;
		_auto = ligado;
	}
}
