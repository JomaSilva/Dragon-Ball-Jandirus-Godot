using Godot;

namespace Jandirus.Client;

/// <summary>
/// O QUE UM LUGAR E. Vira uma opcao no menu de criar node do Godot, e um valor no inspetor.
///
/// Nao vem do DM: o original nao tem conceito de bioma -- ele so sabe a gravidade de cada nome de
/// planeta. Isto e leitura nossa, e existe pra que o gerador procedural tenha com o que se
/// parecer: um planeta sorteado ao lado de Namek pode nascer Jardim como Namek.
/// </summary>
public enum TipoDePlaneta
{
	Rochoso,
	Jardim,
	Deserto,
	Gelado,
	Vulcanico,
	Morto,
}

/// <summary>
/// UM PLANETA -- e a raiz da cena dele.
///
/// ============================ A ARQUITETURA, EM UMA FRASE ============================
/// A cena SABE o que ela e (nome, gravidade, tipo) e decide sozinha como nascer: se for pre-feita,
/// o chao ja veio desenhado no .tscn; se for procedural, ela pede o terreno ao Core passando a
/// SEED que o servidor mandou mais o que ela sabe de si.
/// =====================================================================================
///
/// POR QUE A INFORMACAO MORA NA CENA e nao numa tabela central: porque quem abre o `z03_Vegeta`
/// no editor precisa VER que ali a gravidade e 10. Uma tabela em outro arquivo obriga quem edita
/// o mapa a lembrar de um segundo lugar -- e o que ele esquecer nao aparece em teste nenhum, so
/// no dia em que alguem treinar em Vegeta e ganhar o mesmo que na Terra.
///
/// POR QUE `[GlobalClass]`: e o que faz o Godot registrar o tipo e criar o script sozinho no node
/// -- a cena convertida so precisa apontar pra ele, e um planeta novo feito a mao no editor nasce
/// com todos os campos ja no inspetor.
///
/// AS DUAS FILHAS: <see cref="PlanetaPreFeito"/> (chao desenhado) e
/// <see cref="PlanetaProcedural"/> (chao gerado). O `Procedural` continua sendo um bool na base
/// porque quem pergunta "isto se gera?" -- o servidor, o carregador de zona -- nao deveria
/// precisar conhecer as subclasses.
/// </summary>
[GlobalClass]
public partial class Planeta : Node2D
{
	/// <summary>O nome curto, o mesmo que a <c>ZoneKey</c> usa ("Earth", "Namek", "Vegeta").</summary>
	[Export] public string NomeDoPlaneta { get; set; } = "";

	/// <summary>
	/// A GRAVIDADE, no `Planetgrav` do original (`Modules/Stats/BP/Gravity.dm:88-115`). Terra 1,
	/// Vegeta 10, Planeta Icer 15, Big Gete Star 25, Reino dos Deuses 500.
	///
	/// Nao e enfeite: entra no ganho de treino e no poder efetivo. Um planeta com gravidade errada
	/// e um planeta que treina errado, e isso nao aparece em teste -- aparece como "por que esse
	/// cara subiu tao rapido".
	/// </summary>
	[Export] public double Gravidade { get; set; } = 1;

	[Export] public TipoDePlaneta Tipo { get; set; } = TipoDePlaneta.Rochoso;

	/// <summary>
	/// O CHAO SE GERA? Falso = ja veio desenhado no .tscn.
	///
	/// Bool na base, e nao "e da classe X", porque quem faz a pergunta nao quer conhecer a
	/// hierarquia: o carregador de zona so precisa saber se espera terreno ou se manda gerar.
	/// </summary>
	[Export] public bool Procedural { get; set; }

	/// <summary>Tamanho em tiles, quando gerado. Ignorado no pre-feito (o mapa manda).</summary>
	[Export] public int Largura { get; set; } = 256;
	[Export] public int Altura { get; set; } = 256;

	/// <summary>
	/// A SEED, e ela vem DO SERVIDOR -- nunca do cliente.
	///
	/// E o que faz dois jogadores no mesmo planeta verem o mesmo chao sem que um unico tile
	/// trafegue: os dois geram a mesma coisa a partir do mesmo numero. Deixar o cliente escolher
	/// a seed seria deixar cada um jogar num planeta diferente com o mesmo nome.
	/// </summary>
	public ulong Seed { get; private set; }

	/// <summary>Onde o corpo aparece ao chegar. O gerador garante que este ponto e livre.</summary>
	public Vector2 PontoDeChegada { get; protected set; }

	private bool _nasceu;

	public override void _Ready()
	{
		// NAO GERA NO _Ready. A seed so chega quando o servidor manda, e isso pode ser depois de a
		// cena entrar na arvore. Quem dispara e `Entrar()`; nascer aqui geraria um mundo com
		// seed 0 que teria de ser jogado fora um quadro depois.
		if (NomeDoPlaneta.Length == 0) NomeDoPlaneta = Name;
	}

	/// <summary>
	/// ENTRAR NO PLANETA. E a unica porta: o carregador de zona chama isto com a seed que veio do
	/// servidor, e a cena se vira.
	///
	/// Idempotente: chamar duas vezes com a mesma seed nao regera nada. Trocar de zona e voltar e
	/// comum, e regerar um mapa de 256x256 a cada ida e volta seria um engasgo por porta atravessada.
	/// </summary>
	public void Entrar(ulong seed)
	{
		if (_nasceu && Seed == seed) return;
		Seed = seed;
		_nasceu = true;
		Nascer();
	}

	/// <summary>
	/// O que cada tipo de planeta faz pra existir. A base nao faz nada -- e o comportamento certo
	/// pra um planeta cujo chao ja esta no arquivo.
	/// </summary>
	protected virtual void Nascer() { }

	/// <summary>
	/// A ficha do lugar, pro HUD e pro chat. Existe aqui porque a cena e quem sabe -- perguntar
	/// isso a uma tabela central seria abrir a porta pra ela discordar do mapa.
	/// </summary>
	public string Ficha() =>
		$"{NomeDoPlaneta} — {Tipo}, gravidade {Gravidade:0.##}x"
		+ (Procedural ? $" (gerado, seed {Seed})" : "");
}
