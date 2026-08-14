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
}
