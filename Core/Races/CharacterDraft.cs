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

	public string Validar()
	{
		if (Name.Trim().Length < 2) return "escolha um nome com pelo menos 2 letras";
		if (Race.Length == 0) return "escolha uma raca";
		if (Age < 1 || Age > 200) return "idade fora da faixa";
		if (PrecisaEscolherClasse && ChosenClass.Length == 0) return "escolha a linhagem";
		return "";
	}
}
