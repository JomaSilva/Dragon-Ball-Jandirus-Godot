namespace Jandirus.Client;

/// <summary>
/// TEM CAMPO DE TEXTO COM FOCO? Uma pergunta so, num lugar so.
///
/// `Input.IsActionPressed` le a TECLA FISICA e nao sabe que ha um LineEdit com foco -- entao
/// todo lugar que le teclado de jogo (andar, socar, treinar, mirar, atalhos do HUD) precisa
/// perguntar antes. Eram duas fontes (o chat e a busca do menu) e vao ser mais; cada ponto de
/// leitura conhecer todas elas e o jeito garantido de esquecer uma quando a terceira chegar.
/// </summary>
public static class Foco
{
	// A TERCEIRA FONTE CHEGOU, e ela e a mesa de tecnicas: escrever "Kamehameha" no nome de uma
	// tecnica mandava o personagem meditar (M) e carregar Ki (C) no meio da palavra. Entrou AQUI e
	// nao num `if` dentro de cada leitor de teclado -- que e literalmente o que o cabecalho acima
	// previu que aconteceria quando houvesse uma terceira.
	// A QUARTA FONTE E DE UM TIPO NOVO, e por isso ela merece nota propria: a tela de teclas nao
	// esta so com um campo de texto aberto -- ela pode estar ESPERANDO UMA TECLA. Enquanto espera,
	// nenhum leitor de teclado do jogo pode agir, senao ligar uma forma ao "C" transforma o
	// personagem no proprio gesto de ligar, e ligar o menu ao "P" abre o menu por cima da captura.
	//
	// Ela entrou AQUI e nao num `if` dentro de cada leitor pelo mesmo motivo das outras tres -- e
	// desta vez o `Atalhos` (o disparo das teclas do jogador) tambem passa a perguntar por aqui, o
	// que fecha o circulo: a tela que liga a tecla e a tecla ligada usam o mesmo portao.
	public static bool Digitando =>
		Chat.Digitando || MenuJogo.Digitando || TelaDeTecnicas.Digitando || TelaDeTeclas.Digitando;

	/// <summary>
	/// POSSO DISPARAR UM ATALHO AGORA? A pergunta dos ATALHOS -- menu, aba, mochila, interacao, chat,
	/// verbo, forma, e as sondas de teclado do corpo (C, T, M, V, N, Q, X, mira).
	///
	/// ============================ POR QUE ELA NAO E O `Digitando` ============================
	/// O dono: *"durante os clashs apertar os botoes ta ABRINDO MENUS etc. faca q enquanto ta tendo
	/// QUALQUER CLASH (ki ou zanzoclash) as HOTKEYS SAO DESATIVADAS pra n ficar abrindo varios
	/// menus"*. E medido por que doi tanto: o servidor sorteia a letra do quick time event em
	/// "BCEFGHIJKLMNOPQRTUVXYZ", e **14 dessas 22 letras ja tem acao de jogo** (C, E, F, G, I, K, M,
	/// N, P, Q, R, T, V, X). Responder ao embate abria a mochila, ligava o voo, mandava meditar.
	///
	/// O EMBATE NAO PODE ENTRAR NO `Digitando`, e essa e a linha que nao se cruza: quem le o
	/// `Digitando` inclui o PROPRIO quick time event (`ClashQte._UnhandledInput`), entao pendura-lo
	/// ali silenciaria a unica tecla que precisa continuar viva. Por isso sao duas perguntas: o
	/// `Digitando` e "ha um campo de texto com foco", e esta e "a tecla e do jogador ou e do embate".
	///
	/// O MOVIMENTO NAO PASSA POR AQUI. Ele ja e travado no VETOR (`LocalPlayer`, o `input` que vira
	/// `Vector2.Zero`), que e a regra da casa desde que um portao global de entrada quebrou o andar --
	/// *"pedi pra tu consertar a aura PQ TU QUEBROU A MOVIMENTACAO"*. Nem a GUARDA: soltar o ALT
	/// ENCERRA o lado de quem segura um feixe com as maos (`LadoOk`), e ler a tecla como solta seria
	/// perder a disputa de ki por causa do portao.
	///
	/// E O SILENCIO E DERIVADO, nao guardado: `EmClash` mora num PRAZO que se vence sozinho (ver
	/// `GameClient.EmClash`), pra que um embate que acabe mal -- nocaute, morte, troca de zona,
	/// logout, o outro sumindo -- nao deixe o teclado do jogador mudo pra sempre.
	/// =========================================================================================
	/// </summary>
	public static bool AtalhosMudos => Digitando || GameClient.Instance?.EmClash == true;
}
