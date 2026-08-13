namespace Jandirus.Core.Races;

/// <summary>
/// A ficha que a criacao de personagem produz e que viaja pro servidor.
///
/// Fica no Core (nao no cliente) porque o SERVIDOR precisa refazer a mesma conta: e ele
/// quem decide o BP e os stats finais. O cliente monta a ficha e mostra o resultado; o
/// servidor recebe a ficha, roda o MESMO <see cref="RaceCatalog"/> e crava o personagem.
/// Assim ninguem nasce Legendary por ter editado a tela.
/// </summary>
public sealed class CharacterDraft
{
	public string Name = "";
	public string Race = "";
	public string Planet = "";
	public string Gender = "Male";
	public int Age = 18;

	/// <summary>
	/// Preenchido SO nos tres casos em que o jogador realmente escolhe: linhagem Saiyajin,
	/// cla Namekuseijin e linhagem Half-Saiyan. Em todo o resto fica vazio e a classe e
	/// sorteada pelo servidor -- por isso o jogo tem a "dica de classe" no chat.
	/// </summary>
	public string ChosenClass = "";

	/// <summary>
	/// A HISTORIA DO PERSONAGEM, escrita pelo jogador.
	///
	/// ============================ TEXTO LIVRE, E NAO UM MENU ============================
	/// A tentacao era oferecer uma lista de origens prontas ("orfao", "nobre", "exilado"), cada uma
	/// dando um bonus. O original nao faz isso: o formulario "QUEM E VOCE?" (CreationUI.dm:148-157)
	/// pede NOME, IDADE e um `textarea` de historia, com minimo de 10 e maximo de 500 caracteres, e
	/// nao ha efeito mecanico nenhum -- e identidade, nao build.
	///
	/// Portar como menu com bonus teria mudado o jogo: origem que da stat vira mais uma escolha
	/// otima a decorar, e quem quisesse ROLEPLAY pagaria por isso. O campo continua sendo o que
	/// era, e continua legivel depois pelo verb `Backstory`.
	/// ====================================================================================
	/// </summary>
	public string Backstory = "";

	/// <summary>O minimo e o maximo do `textarea` do original (`CTFORM_BS_MAX` = 500).</summary>
	public const int BackstoryMin = 10;
	public const int BackstoryMax = 500;

	/// <summary>
	/// PORTE DO CORPO -- uma escolha PERMANENTE de atributos, e a unica da criacao que mexe em
	/// numero. E o passo "TIPO DE CORPO" do DM (CharacterCreation.dm:170-174), que faltava aqui.
	///
	/// Medium nao ajusta nada; Small troca forca e resistencia por velocidade e recuperacao; Large
	/// faz o contrario. Ver <see cref="PorteDoCorpo"/>.
	/// </summary>
	public string Porte = "Medium";

	/// <summary>
	/// "QUERO NASCER NUM PLANETA QUALQUER PERTO DE CASA" -- a segunda metade do pedido do dono.
	///
	/// ============================ E UM PEDIDO, E NAO UMA ESCOLHA DE PLANETA ============================
	/// O campo e um BOOLEANO e nao o nome (nem a seed) do mundo, e a diferenca e de seguranca: a
	/// gravidade do berco vira `GravMastered` de graca no nascimento (`race.dm:130-131`), entao
	/// deixar o cliente dizer PARA ONDE ir seria deixa-lo escolher um atributo permanente -- e a
	/// tela de criacao e justamente onde este projeto ja decidiu que "sorteio no cliente e sorteio
	/// ate sair o resultado desejado" (ver o cabecalho desta classe).
	///
	/// Quem sorteia qual das irmas sai e o servidor, de uma semente que so ele conhece
	/// (<see cref="Bercos.SementeDoBerco"/>). O cliente ENUMERA as candidatas pela mesma funcao pura
	/// (<see cref="Bercos.IrmasDoNatal"/>) e as mostra -- ele sabe o sorteio inteiro, so nao sabe o
	/// resultado.
	/// ==============================================================================================
	///
	/// Falso e o padrao, e o padrao e nascer em casa. O <see cref="Bercos.Onde"/> ignora este campo
	/// quando o corpo e o do Lendario: exilio nao e opcao de ninguem.
	/// </summary>
	public bool PertoDeCasa;

	public static readonly string[] Portes = ["Medium", "Small", "Large"];

	/// <summary>Raças que NAO passam pelo passo de genero (o BYOND forcava Male).</summary>
	public static readonly string[] SemGenero =
		["Namekian", "Kanassa", "Makyo", "Yardrat", "Saibaman"];

	/// <summary>Raças que nascem carecas: o passo de cabelo nem aparece.</summary>
	public static readonly string[] SemCabelo =
		["Namekian", "Majin", "Icer", "BioAndroid", "Saibaman", "Shapeshifter"];

	/// <summary>
	/// As tres racas em que o jogador escolhe a classe, e as opcoes de cada uma.
	/// Vazio = a classe e sorteada.
	/// </summary>
	public static string[] EscolhasDeClasse(string raca) => raca switch
	{
		"Saiyan" => ["Saiyan", "Primal Saiyan"],          // linhagem: define de qual pool sai a classe
		"Namekian" => ["Warrior clan", "Demon clan", "Dragon clan"],
		"Halfbreed" => ["New Generation", "Future Lineage", "Prodigial"],
		_ => [],
	};

	/// <summary>
	/// Racas selecionaveis por planeta natal. Espelha os gates `can*` do BYOND com os
	/// defaults de producao (Android, Spirit Doll e Bio-Android ficam de fora: no BYOND
	/// dependem de uma lista de criadores que hoje ninguem alimenta).
	/// </summary>
	public static string[] RacasDoPlaneta(string planeta) => planeta switch
	{
		"Earth" => ["Human", "Shapeshifter", "Demigod", "Majin", "Alien"],
		"Vegeta" => ["Saiyan", "Tsujin", "Saibaman", "Heran", "Meta", "Icer", "Alien"],
		"Namek" => ["Namekian", "Arlian", "Makyo", "Gray", "Alien", "Kanassa", "Yardrat"],
		"Heaven" => ["Demigod", "Kai"],
		"Hell" => ["Demon", "Demigod"],
		_ => [],
	};

	public static readonly string[] Planetas = ["Earth", "Vegeta", "Namek", "Heaven", "Hell"];

	public bool PrecisaEscolherClasse => EscolhasDeClasse(Race).Length > 0;
	public bool TemGenero => Array.IndexOf(SemGenero, Race) < 0;

	/// <summary>
	/// O teto de idade DESTA raca -- o auge dela. Ver <see cref="Envelhecimento"/>.
	///
	/// Era um 200 fixo pra todo mundo, o que deixava um Saibaman (que vive 15 anos) nascer com 200
	/// e um Kai (que vive 112 mil) parar em 200.
	/// </summary>
	public int IdadeMaxima => Envelhecimento.IdadeMaximaNaCriacao(Race);

	public string Validar()
	{
		if (Name.Trim().Length < 2) return "escolha um nome com pelo menos 2 letras";
		if (Race.Length == 0) return "escolha uma raca";

		// A FAIXA E DA RACA, e nao do jogo. O original valida contra o `DeclineAge` pelo mesmo
		// motivo: ninguem nasce ja em declinio.
		if (Age < 1 || Age > IdadeMaxima)
			return $"a idade tem que ficar entre 1 e {IdadeMaxima} para esta raça";

		if (PrecisaEscolherClasse && ChosenClass.Length == 0) return "escolha a linhagem";
		if (Array.IndexOf(Portes, Porte) < 0) return "escolha o porte do corpo";

		string historia = Backstory.Trim();
		if (historia.Length < BackstoryMin)
			return $"escreva a história do personagem (pelo menos {BackstoryMin} caracteres)";
		if (historia.Length > BackstoryMax)
			return $"a história passou de {BackstoryMax} caracteres";

		return "";
	}

	/// <summary>
	/// O QUE CADA PORTE FAZ nos stats. Tabela literal do DM (o passo "TIPO DE CORPO").
	///
	/// ============================ DOIS CAMPOS SOMAM, O RESTO MULTIPLICA ============================
	/// O original mistura as duas coisas no mesmo passo, e a mistura tem que sobreviver a traducao:
	/// o Small MULTIPLICA a defesa de Ki por 0,8, e o Large SUBTRAI 0,2 dela. Guardar os dois no
	/// mesmo campo faria quem ligasse isto multiplicar um stat por -0,2 e inverter a defesa do
	/// personagem grande -- um bug que so apareceria em jogo, contra quem usa Ki.
	///
	/// Por isso os somadores tem nome proprio (`KiDefSoma`, `KiRegenSoma`) e os multiplicadores
	/// ficam todos juntos. Medium devolve tudo neutro: ele e o "nenhum ajuste" do original, e
	/// existe como opcao explicita porque escolher nao mexer tambem e escolher.
	/// ===============================================================================================
	/// </summary>
	public readonly record struct AjusteDePorte(
		double Speed, double KiOff, double PhysOff, double PhysDef, double KiDef,
		double KiDefSoma, double KiRegenSoma);

	public static AjusteDePorte PorteDoCorpo(string porte) => porte switch
	{
		// agil: troca forca e resistencia por velocidade, potencia de Ki e folego
		"Small" => new AjusteDePorte(1.4, 1.3, 0.9, 0.7, 0.8, 0, +0.6),
		// gigante: aguenta e bate mais, mas e lento e mais facil de acertar
		"Large" => new AjusteDePorte(0.7, 1.1, 1.1, 1.3, 1, -0.2, 0),
		_ => new AjusteDePorte(1, 1, 1, 1, 1, 0, 0),
	};
}
