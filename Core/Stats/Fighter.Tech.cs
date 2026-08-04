namespace Jandirus.Core.Stats;

/// <summary>
/// TECNOLOGIA E DINHEIRO -- a via de progressao que NAO passa por bater em ninguem.
///
/// No original o `techskill` e uma MAESTRIA ("Technology", em Crafting Masteries.dm) que sobe um
/// ponto por nivel e se ganha estudando material numa Research Station. E o unico numero da ficha
/// que nao vem de treino fisico, e e ele que abre as construcoes -- inclusive o mainframe que
/// transforma um humano em androide (`neededtech = 50`) e os laboratorios de DNA (`DNL_INT_REQ`
/// 70). Um humano que nunca socou ninguem pode acabar mais perigoso que um Saiyajin.
/// </summary>
public partial class Fighter
{
	/// <summary>
	/// INTELIGENCIA APLICADA. E a `techskill` do original: 0 pra quem nunca estudou, e cada ponto
	/// destrava construcao. Nao entra em <see cref="PowerLevel"/> -- de proposito: o poder de
	/// quem estuda vem do que ele CONSTROI, nao de um multiplicador escondido na ficha.
	/// </summary>
	public double techskill;

	/// <summary>
	/// O MULTIPLICADOR DE APRENDIZADO de tecnologia (`techmod`). Quem nasceu esperto aprende mais
	/// rapido; nao aprende COISAS diferentes. Comeca em 1.
	/// </summary>
	public double techmod = 1;

	/// <summary>
	/// ANDROIDE DE ABSORCAO: come o Ki que recebe em vez de apanhar dele.
	/// ANDROIDE DE ENERGIA INFINITA: nao cansa, nao come, o Ki volta sozinho.
	///
	/// Sao EXCLUDENTES no original -- a maquina reconstroi o corpo de UM jeito. Duas flags em vez
	/// de um enum porque e assim que a ficha se serializa hoje, e um campo booleano a mais nao
	/// justifica migrar todos os saves.
	/// </summary>
	public bool AndroideAbsorcao, AndroideInfinito;

	/// <summary>Dinheiro NO BOLSO. O laboratorio de androide custa 1.000.000 disto.</summary>
	public double Zeni;

	/// <summary>
	/// DINHEIRO NO BANCO -- o `Bank()` do original (`Modules/Tech/Bank.dm:2-23`).
	///
	/// POR QUE UM BANCO EXISTE NUM JOGO DE PORRADA: porque morrer custa, e o que esta guardado nao
	/// se perde. Ele e a unica forma de poupanca do jogo, e e o que torna as construcoes de meio
	/// milhao alcancaveis por quem apanha no caminho.
	///
	/// Campo novo em `Fighter`, que e serializado INTEIRO no save: quem nunca depositou carrega
	/// zero e nada precisa migrar.
	/// </summary>
	public double ZeniBanco;

	/// <summary>
	/// XP acumulado rumo ao proximo ponto de tech. Guardado separado do nivel porque o nivel e
	/// que abre porta: uma barra que anda e um nivel que trava sao coisas diferentes na tela.
	/// </summary>
	public double techXp;

	/// <summary>
	/// QUANTO XP PRO PROXIMO PONTO. Cresce com o nivel -- sem isso, sair do 0 e sair do 69 (a
	/// porta do laboratorio de DNA) custariam o mesmo, e o gate de inteligencia nao seria gate.
	///
	/// A curva e nossa, nao do original: la o XP vem do sistema de maestrias, que e um subsistema
	/// inteiro (`AddExp`/`levelstat`) com tabela propria. Portar a tabela toda por causa de um
	/// numero seria arrastar o sistema errado; o que importa e que estudar demore mais a cada
	/// ponto, e essa forma faz isso e deixa a substituicao facil no dia em que as maestrias forem.
	/// </summary>
	public double TechXpDoProximo() => 100 * Math.Pow(1 + techskill, 1.35);

	/// <summary>
	/// ESTUDA. Devolve quantos pontos de tech subiram (normalmente 0 ou 1).
	///
	/// O GANHO PASSA PELO `techmod` como no original (`AddExp(..., 15 + 10*tier*(1+qualidade/100)
	/// * usr.techmod)`) -- ele multiplica o XP, nunca o nivel.
	/// </summary>
	public int Estudar(double xp)
	{
		techXp += xp * Math.Max(techmod, 0.1);
		int subiu = 0;
		// laco, e nao um `if`: um estudo muito grande pode valer mais de um nivel, e engolir o
		// excedente faria o jogador perder XP sem nada na tela dizendo por que.
		while (techXp >= TechXpDoProximo() && subiu < 100)
		{
			techXp -= TechXpDoProximo();
			techskill++;
			subiu++;
		}
		return subiu;
	}
}
