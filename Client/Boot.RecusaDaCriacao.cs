using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A TELA CINZA DA CRIACAO (2026-10-23) ============================
/// O relato do dono: *"alguns jogadores caem em uma tela cinza na criaçao"*.
///
/// O CAMINHO: a lista de vagas esconde o painel do login (`AoReceberSlots`), o "Criar personagem" esconde a selecao
/// (`AbrirCriacao`), o "Entrar no mundo" esconde a criacao (`CreationScreen.Confirmar`) e a cobertura sobe (`CobrirEntrada`).
/// Quando o servidor RECUSAVA a ficha, o tratador do `Rejected` derrubava a cobertura e escrevia "recusado: ..." no rotulo do
/// LOGIN -- escondido, la atras. Sobrava o fundo chapado (`Tema.Fundo`) e o selo da versao: a tela cinza, sem botao e sem motivo.
/// As recusas que o jogador encontrava: o nome de alguem em jogo (os moradores do mundo contam -- e o dono quer que continuem
/// contando), a raca fora do mundo (o sorteio velho "Icer" da conta e a raca que a criacao do filho deixava pra tras, as duas
/// consertadas na origem), a ficha que o servidor nao aceita, e a excecao no servidor, que nem resposta dava.
///
/// A DECISAO DO DONO: *"So mostrar o motivo"*. Toda recusa que chega DEPOIS do "Entrar no mundo" reabre a criacao, com o rascunho
/// do jogador intacto e o motivo escrito na propria tela (`CreationScreen.ReabrirComOMotivo`). A recusa de antes da criacao (o
/// login, a senha, o banimento) continua no rotulo do login, que e onde o jogador esta. (2026-10-24, "Consertar") E a do "Jogar" de
/// quem ja tem personagem fica escrita na selecao (`CharacterSelect.MostrarARecusa`).
/// ============================================================================================
/// </summary>
public partial class Boot
{
	/// <summary>
	/// DEFEITO INJETADO (robo `--diagtelacinza`): a recusa da criacao so escreve o motivo no rotulo do login escondido -- o jogo de
	/// antes, a tela cinza. Lido pelo <see cref="ReabrirACriacaoRecusada"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool RecusaSoNoLoginDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (robo `--diagtelacinza`): a lista de vagas que chega junto da recusa volta a aparecer POR CIMA da criacao
	/// reaberta (a selecao e a camada 3, a criacao a 2) -- o motivo some atras dela. Lido pelo <see cref="CriacaoNaFrente"/>. Falso em
	/// jogo, sempre.
	/// </summary>
	internal static bool ListaCobreACriacaoDeTeste;

	/// <summary>
	/// A RECUSA QUE CHEGOU, se ela e da criacao: a tela que mandou a ficha volta com o motivo. A recusa de qualquer outra hora (o login,
	/// o apagar, o "Jogar" de quem ja existe) nao tem criacao esperando, e o `ReabrirComOMotivo` devolve falso.
	/// </summary>
	private void ReabrirACriacaoRecusada(string motivo)
	{
		if (RecusaSoNoLoginDeTeste || _criacao == null || !IsInstanceValid(_criacao) || _criacao.IsQueuedForDeletion()) return;
		if (!_criacao.ReabrirComOMotivo(motivo)) return;
		if (_selecao != null) _selecao.Visible = false;
		GD.Print($"[client] a criacao voltou com o motivo da recusa: {motivo}");
	}

	/// <summary>
	/// A CRIACAO ESTA NA TELA? A lista de vagas que chega com ela aberta (a que o servidor manda junto de certas recusas -- o bebe que
	/// ja nasceu de outro, o erro do servidor) atualiza a selecao POR BAIXO: quem desiste da criacao (o `Cancelado`) encontra a lista
	/// nova, e o motivo continua na frente.
	/// </summary>
	private bool CriacaoNaFrente() =>
		!ListaCobreACriacaoDeTeste && _criacao != null && IsInstanceValid(_criacao) && !_criacao.IsQueuedForDeletion() && _criacao.Visible;

	/// <summary>
	/// DEFEITO INJETADO (robo `--diagtelacinza --jogar`): a recusa do "Jogar" so escreve o motivo no rotulo do login escondido -- o jogo
	/// de antes, o jogador de volta na selecao sem saber por que. Lido pelo <see cref="MostrarARecusaDoJogar"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool RecusaDoJogarSoNoLoginDeTeste;

	/// <summary>
	/// (2026-10-24) A RECUSA DO "JOGAR" FICA ESCRITA NA SELECAO -- o dono ("Consertar"): o mesmo defeito da tela cinza, do lado de quem
	/// ja tem personagem. A selecao nao se esconde no "Jogar" (ela e o que sobra embaixo da cobertura), entao nao havia tela cinza: havia o
	/// jogador de volta na lista, sem o motivo. So o pedido que a selecao mandou volta pra ela (`CharacterSelect.MostrarARecusa`).
	/// </summary>
	private void MostrarARecusaDoJogar(string motivo)
	{
		if (RecusaDoJogarSoNoLoginDeTeste || _selecao == null || !IsInstanceValid(_selecao) || _selecao.IsQueuedForDeletion()) return;
		if (_selecao.MostrarARecusa(motivo)) GD.Print($"[client] a recusa do Jogar ficou escrita na selecao: {motivo}");
	}
}
