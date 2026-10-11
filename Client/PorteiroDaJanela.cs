using Godot;

namespace Jandirus.Client;

/// <summary>
/// O PORTEIRO DA JANELA DESTACADA -- um no que mora DENTRO da janela do sistema do menu P e faz as duas coisas
/// que so quem esta la dentro consegue:
///
///   1. ENTREGA AO MENU AS TECLAS. Elas chegam a tela da janela que tem o foco do sistema, e o `MenuJogo` (que
///      escuta P e ESC) mora na tela do JOGO. No `_Input` -- ANTES da interface da janela --, que e a ordem do
///      menu embutido: la tambem as telas do jogo leem a tecla antes dos botoes do menu.
///        - o ESC desfaz uma camada do menu (`MenuJogo.EscNaJanela`);
///        - qualquer outra e OFERECIDA AO JOGO (`MenuJogo.TeclaNaJanela`); so se ele nao a quis ela segue pra
///          interface da janela (a navegacao de foco, o botao com foco).
///      Na PRIMEIRA versao o repasse era so do que SOBRAVA da janela (`_UnhandledKeyInput`), e a bancada mostrou o
///      furo: o TAB (a lista de teclas do jogo) era engolido pela navegacao de foco da janela e nunca chegava.
///   2. AVISA QUANDO A JANELA MUDOU DE LUGAR. O sistema manda a notificacao de posicao pra janela e pros nos
///      dela; o `MenuJogo` esta fora, e nao a recebe.
/// </summary>
public partial class PorteiroDaJanela : Node
{
	public override void _Input(InputEvent evento)
	{
		if (evento is not InputEventKey k || MenuJogo.Instancia is not { } menu) return;
		if (k is { Pressed: true, Echo: false, Keycode: Key.Escape }) menu.EscNaJanela(GetViewport());
		else menu.TeclaNaJanela(k, GetViewport());
	}

	public override void _Notification(int o)
	{
		if (o == NotificationWMPositionChanged) MenuJogo.Instancia?.JanelaMexeu();
	}
}
