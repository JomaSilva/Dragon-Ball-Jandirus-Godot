namespace Jandirus.Core.Ranks;

/// <summary>O que o servidor sabe de quem esta reivindicando. Tudo que os requisitos consultam.</summary>
public readonly struct FichaDeRank
{
	public string Raca { get; init; }
	public string Classe { get; init; }
	public double BpBase { get; init; }
	public int Karma { get; init; }

	/// <summary>A chave do cargo que esta alma ja carrega ("" = nenhum). A escada usa isto.</summary>
	public string CargoAtual { get; init; }
}

/// <summary>Um cargo. Um dono por vez, e a alma so carrega um.</summary>
public sealed class RankDef
{
	public string Chave = "";
	public string Nome = "";
	public string Desc = "";

	/// <summary>Cargos que este cargo exige como degrau anterior. Vazio = porta de entrada.</summary>
	public string[] Degraus = [];

	public string? Raca;
	public string? Classe;
	public int Karma;              // minimo; use KarmaMax pros cargos de vilao
	public int KarmaMax = int.MaxValue;
	public double Bp;
}

/// <summary>
/// OS CARGOS, portados de `Code/Modules/Ranks/RankQuests.dm` (`rq_requirements`).
///
/// TRES COISAS QUE DEFINEM O SISTEMA:
///
/// 1. **UM DONO POR CARGO, E UMA ALMA POR CARGO.** Nao ha "vice": reivindicar so vale com o
///    trono VAGO, e quem ja tem um cargo com deveres nao acumula outro.
/// 2. **KARMA E A PORTA MORAL.** A escola da Tartaruga pede karma alto; a do Grou pede karma
///    ZERO OU MENOS ("nao forma santos"); o Senhor do Inferno pede -50 ou pior. E o unico
///    sistema do jogo em que ser mau ABRE porta em vez de fechar.
/// 3. **HA UMA ESCADA.** Os quatro Kaios cardeais levam ao Grand Kai, que leva ao Kaioshin. Um
///    cargo com `Degraus` so abre pra quem esta num deles.
///
/// O QUE NAO ESTA AQUI AINDA: a PROVA contra o Espirito do Cargo (o original invoca um NPC e
/// exige vence-lo), e os requisitos que dependem de sistemas que o port ainda nao tem --
/// reputacao de planeta, contador de mortes, zenni de campanha, God Ki. Os cargos que dependem
/// SO deles ficam de fora do catalogo em vez de entrar com requisito falso: um cargo que se
/// reivindica sem cumprir nada e pior que um cargo que ainda nao existe.
/// </summary>
public static class Cargos
{
	public static readonly RankDef[] Todos =
	[
		new()
		{
			Chave = "turtle", Nome = "Mestre da Escola da Tartaruga",
			Desc = "A escola que forma protetores. Ensina o Kamehameha.",
			Karma = 25, Bp = 1_000_000,
		},
		new()
		{
			Chave = "crane", Nome = "Mestre da Escola do Grou",
			Desc = "A escola rival. Nao forma santos -- forma assassinos eficientes.",
			KarmaMax = 0, Bp = 1_000_000,
		},
		new()
		{
			Chave = "elder", Nome = "Ancião Namekuseijin",
			Desc = "A voz do povo de Namek.",
			Raca = "Namekian", Karma = 25,
		},
		new()
		{
			Chave = "guardian", Nome = "Guardião da Terra",
			Desc = "A tradicao de Kami. So um Namekuseijin do Cla do Dragao.",
			Raca = "Namekian", Classe = "Dragon clan", Karma = 50,
		},
		new()
		{
			Chave = "demonlord", Nome = "Senhor do Inferno",
			Desc = "O Inferno exige um coracao PODRE.",
			KarmaMax = -50, Bp = 5_000_000,
		},
		new()
		{
			Chave = "frostlord", Nome = "Lorde do Gelo",
			Desc = "O mais forte da estirpe Frost Demon.",
			Raca = "Icer", Bp = 10_000_000,
		},
		new()
		{
			Chave = "nkai", Nome = "Kaio do Norte",
			Desc = "Ensina o Kaio-ken e a Genkidama.", Karma = 50,
		},
		new() { Chave = "skai", Nome = "Kaio do Sul", Desc = "Ensina a Expansao Corporal.", Karma = 50 },
		new() { Chave = "ekai", Nome = "Kaio do Leste", Desc = "Ensina a Explosao de Ki.", Karma = 50 },
		new() { Chave = "wkai", Nome = "Kaio do Oeste", Desc = "Ensina a Autodestruicao.", Karma = 50 },
		new()
		{
			Chave = "grandkai", Nome = "Grande Kaio",
			Desc = "O chefe dos quatro. Nao se reivindica de fora: sobe-se.",
			Degraus = ["nkai", "skai", "ekai", "wkai"],
		},
		new()
		{
			Chave = "kaioshin", Nome = "Kaioshin",
			Desc = "O topo da escada divina.",
			Degraus = ["grandkai"], Karma = 75,
		},
	];

	public static RankDef? Get(string chave) =>
		Array.Find(Todos, r => string.Equals(r.Chave, chave, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// O QUE FALTA pra reivindicar. Nulo = apto.
	///
	/// Devolve TEXTO e nao um booleano de proposito: "karma 25+" e "sangue Namekuseijin" mandam
	/// o jogador fazer coisas opostas, e o original ja entregava a frase pronta. Um "nao pode"
	/// seco faria a pessoa tentar de novo sem saber o que mudar.
	/// </summary>
	public static string? OqueFalta(RankDef r, FichaDeRank f)
	{
		if (r.Degraus.Length > 0 && !r.Degraus.Contains(f.CargoAtual, StringComparer.OrdinalIgnoreCase))
		{
			string nomes = string.Join(", ", r.Degraus.Select(d => Get(d)?.Nome ?? d));
			return $"ja ocupar um destes cargos: {nomes}";
		}

		if (r.Raca != null && !string.Equals(r.Raca, f.Raca, StringComparison.OrdinalIgnoreCase))
			return $"sangue {r.Raca}";

		if (r.Classe != null && !string.Equals(r.Classe, f.Classe, StringComparison.OrdinalIgnoreCase))
			return $"ser da linhagem {r.Classe}";

		if (f.Karma < r.Karma) return $"karma {r.Karma}+ (voce tem {f.Karma})";
		if (f.Karma > r.KarmaMax) return $"karma {r.KarmaMax} ou pior (voce tem {f.Karma})";
		if (f.BpBase < r.Bp) return $"{r.Bp:N0} de BP base (voce tem {f.BpBase:N0})";
		return null;
	}
}
