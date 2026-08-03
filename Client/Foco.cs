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
	public static bool Digitando => Chat.Digitando || MenuJogo.Digitando;
}
