using Jandirus.Core.Items;

namespace Jandirus.Core.Tech;

/// <summary>
/// O SINTETIZADOR DE VIRUS -- `obj/items/Virus_Synth` (`Code/Modules/Tech/Viruses.dm`) e o que ele fabrica: os virus,
/// os antivirus e as injecoes (`Injections.dm`).
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// A maquina, APARAFUSADA, sintetiza por zeni o que a tecnologia de quem a usa alcanca: injecoes (25+), virus (65+)
/// e antivirus (75+). O produto e um item que se toma.
///   * O VIRUS e permanente e so se toma UMA vez. Tomar um e um risco: 25% de ganhar uma MUTACAO (certo, com quatro);
///     e quem ganhou -- sendo o virus "benefico" ou tendo tres -- ganha outra, e ai tem 25% de MORRER (certo, com
///     cinco). Androide nao toma.
///   * O ANTIVIRUS desfaz um virus (Delta, Echo), tira mutacoes (Kilo, que falha uma vez em quatro) ou cura
///     (Mike: dez segundos parado e sem apanhar).
///   * A INJECAO e de efeito imediato, e da 10% de mutacao.
/// A MUTACAO e um contador: tira um fio de vida por tique, passa pra quem leva o soco de um mutante (10% por mutacao),
/// e zera na morte.
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * O PRODUTO CAI NA MOCHILA (regra do dono: tudo o que se fabrica vai pro inventario); la, no chao.
///   * ACEITA RECURSOS, pelo dobro, como toda tech paga.
///   * O QUE NAO SE SINTETIZA, porque o que o produto mexe nao existe neste port: Virus Foxtrot (soma no `Lifespan`,
///     que o proprio original aposentou -- `Genetic_Datum.dm:227`), India (regeneracao de morte temporaria),
///     Daybreaker e o antivirus dele (vampiros), November (andar no tempo parado), Antivirus Juliett e a injecao do
///     T-Virus (zumbis), a Shapeshifter's Cure, e o Antivirus Lima (comentado fora do menu no proprio original).
///   * O ANTIVIRUS MIKE CUSTA O QUE O ROTULO DIZ (50.000). La o texto diz 50.000 e a conta cobra 25.000.
///   * A INSTA-DEATH MATA, como o rotulo promete (*"Will kill you. This ensures death."*). La as duas mortes pedem
///     `usr.HP&lt;100`: com a vida cheia a agulha nao faz nada.
///   * A RAIVA DAS INJECOES E A TERCEIRA FONTE DO NUMERO DE RAIVA deste port (`GameServer.RaivaComoNumero`), e so do
///     numero: ela nao e um degrau de raiva, entao nao abre forma nenhuma -- o `anger_ssj = 0` de la (*"Can't give
///     SSJ"*). Como la, nao tem prazo: so o gotejamento a apaga (e a meditacao, que acalma toda raiva). A parte de
///     `Lifespan` dos esteroides nao entra.
///   * O CONTAGIO E POR PROXIMIDADE DO GOLPE: quem apanha de um mutante a ate um tile e meio pode se contaminar. La e
///     so o soco que acerta (`hit == 2 || hit == 3`), e so em jogador (`M.client`) -- o segundo crivo ficou.
/// ==================================================================================
/// </summary>
public static class Sintetizador
{
	public const string Tipo = "Virus_Synth";
	public const string VerboFazer = "sint_fazer";
	public const string AcaoTomar = "sint_tomar", AcaoInjetar = "sint_injetar";

	public enum Classe : byte { Virus, AntiVirus, Injecao }

	/// <summary>Um produto do sintetizador: o que ele e, quanto custa, que tecnologia pede e o que o menu diz dele.</summary>
	public sealed record Produto(string Id, string Nome, Classe Classe, double Preco, double Tecnologia, bool Benefico, string Descricao);

	public const string Alfa = "Virus_Alfa", Bravo = "Virus_Bravo", Charlie = "Virus_Charlie", Delta = "Virus_Delta",
						Echo = "Virus_Echo", Golf = "Virus_Golf", Hotel = "Virus_Hotel";
	public const string AntiDelta = "Anti_Virus_Delta", AntiEcho = "Anti_Virus_Echo", Kilo = "Anti_Virus_Kilo", Mike = "Anti_Virus_Mike";
	public const string Juventude = "Youthenasia", Lsd = "LSD", Raiva = "RAGE", Esteroide = "Steroids", Esteroide2 = "Steroids_v2",
						MorteCerta = "Insta_Death", Raiva2 = "RAGE_v2", Rabo = "Tail_Injection";

	/// <summary>Os precos, as tecnologias e os textos sao os dos menus do original (`Viruses.dm:36-256`, `Injections.dm:1-120`).</summary>
	public static readonly Produto[] Produtos =
	[
		new(Alfa, "Virus Alfa", Classe.Virus, 50_000, 65, true, "aumenta a sua inteligência"),
		new(Bravo, "Virus Bravo", Classe.Virus, 25_000, 70, true, "aumenta as defesas"),
		new(Charlie, "Virus Charlie", Classe.Virus, 50_000, 75, true, "enche o Ki e o vigor, estica o vigor e aumenta o seu Ki"),
		new(Delta, "Virus Delta", Classe.Virus, 25_000, 75, false, "aumenta a regeneração de Ki, e diminui o ataque e a defesa de Ki"),
		new(Echo, "Virus Echo", Classe.Virus, 25_000, 80, false, "aumenta a regeneração de vida, e diminui a defesa física"),
		new(Golf, "Virus Golf", Classe.Virus, 25_000, 85, true, "aumenta a força e a velocidade"),
		new(Hotel, "Virus Hotel", Classe.Virus, 25_000, 90, true, "aumenta a técnica e o Ki"),

		new(AntiDelta, "Anti-Virus Delta", Classe.AntiVirus, 25_000, 75, false, "desfaz o Virus Delta"),
		new(AntiEcho, "Anti-Virus Echo", Classe.AntiVirus, 25_000, 75, false, "desfaz o Virus Echo"),
		new(Mike, "Anti-Virus Mike", Classe.AntiVirus, 50_000, 90, false, "cura muito a vida, e o vigor e o Ki por inteiro: dez segundos parado e sem apanhar"),
		new(Kilo, "Anti-Virus Kilo", Classe.AntiVirus, 25_000, 95, false, "tira de uma a três mutações (falha uma vez em quatro)"),

		new(Juventude, "Youthenasia", Classe.Injecao, 75_000, 25, false, "rejuvenesce: você volta a ter dez anos"),
		new(Rabo, "Tail Injection", Classe.Injecao, 100_000, 30, false, "devolve o rabo a quem pode ter um -- e derruba você com meia vida"),
		new(Lsd, "LSD", Classe.Injecao, 25_000, 30, false, "um reforço passageiro de Ki e de magia (e uma chance em dez de matar)"),
		new(Raiva, "RAGE", Classe.Injecao, 50_000, 35, false, "uma fúria de droga, não muito forte"),
		new(Esteroide, "Steroids", Classe.Injecao, 50_000, 40, false, "um pouco de poder na hora"),
		new(Esteroide2, "Steroids v2", Classe.Injecao, 75_000, 50, false, "mais poder na hora que o esteroide comum"),
		new(MorteCerta, "Insta-Death", Classe.Injecao, 25_000, 65, false, "mata. Garante a morte -- a sua, ou a de quem você injetar"),
		new(Raiva2, "RAGE v2", Classe.Injecao, 75_000, 70, false, "uma fúria melhor, um pouco de poder e alguma cura"),
	];

	public static Produto? De(string id)
	{
		foreach (Produto p in Produtos)
			if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) return p;
		return null;
	}

	/// <summary>A tecnologia minima de cada prateleira (`if(usr.techskill&gt;=75) SynthList += "Anti-Virus"` ...).</summary>
	public static double TecnologiaDaClasse(Classe c) => c switch { Classe.Virus => 65, Classe.AntiVirus => 75, _ => 25 };

	private const string ArteDoVirus = "res://Assets/Sprites/Misc/Objects/Technology/Poison Injection.tres";   // 'Poison Injection.dmi'
	private const string ArteDoAntiVirus = "res://Assets/Sprites/Misc/Objects/Items/Antivirus.tres";           // 'Antivirus.dmi'
	private const string ArteDaInjecao = "res://Assets/Sprites/Misc/Objects/Items/Item, Needle.tres";          // 'Item, Needle.dmi'

	/// <summary>A ficha do item que o sintetizador fabrica, ou nulo quando o id nao e de um produto. Empilha (`stackable=1`).</summary>
	public static ItemDef? Ficha(string id)
	{
		if (De(id) is not { } p) return null;
		string arte = p.Classe switch { Classe.Virus => ArteDoVirus, Classe.AntiVirus => ArteDoAntiVirus, _ => ArteDaInjecao };
		string frase = p.Classe switch
		{
			Classe.Virus => $"Um vírus: {p.Descricao}. É permanente, só se toma uma vez -- e tomar qualquer vírus é um risco de mutação.",
			Classe.AntiVirus => $"Um antivírus: {p.Descricao}.",
			_ => $"Uma injeção: {p.Descricao}.",
		};
		return new ItemDef(p.Id, p.Nome, frase, arte, "", Empilhavel: true,
						   Acoes: p.Id == MorteCerta ? [AcaoTomar, AcaoInjetar] : [AcaoTomar]);
	}

	// ---- a mutacao ----
	/// <summary>`if(prob(25)||usr.Mutations&gt;=4)`: a chance de um virus dar uma mutacao, e com quantas ela e certa.</summary>
	public const double ChanceDeMutarNoVirus = 25;
	public const int MutacoesQueGarantemOutra = 4;

	/// <summary>`if(CompletelyBeneficial||usr.Mutations&gt;=3)`: com quantas a mutacao vem em dobro.</summary>
	public const int MutacoesQueDobram = 3;

	/// <summary>`if(prob(25)||usr.Mutations&gt;=5)`: a chance de a segunda mutacao matar, e com quantas a morte e certa.</summary>
	public const double ChanceDeMorrerNoVirus = 25;
	public const int MutacoesQueMatam = 5;

	/// <summary>`if(prob(10)) usr.Mutations+=1`: a mutacao de uma injecao.</summary>
	public const double ChanceDeMutarNaInjecao = 10;

	/// <summary>`SpreadDamage(0.005*Mutations)`: o que cada mutacao tira de vida por segundo.</summary>
	public const double DanoPorMutacao = 0.005;

	/// <summary>`if(prob(10 * src.Mutations)) M.Mutations+=1`: o contagio, por mutacao de quem bate.</summary>
	public const double ContagioPorMutacao = 10;

	/// <summary>
	/// O QUE TOMAR UM VIRUS FAZ COM AS MUTACOES (`Viruses.dm:275-283`). Devolve as mutacoes depois, e se a pessoa morre.
	/// Os dois sorteios vem de fora (0..100), pra a conta ser a mesma na bancada.
	/// </summary>
	public static (int Mutacoes, bool Morre) Risco(int mutacoes, bool benefico, double sorteioDeMutar, double sorteioDeMorrer)
	{
		if (!(sorteioDeMutar < ChanceDeMutarNoVirus || mutacoes >= MutacoesQueGarantemOutra)) return (mutacoes, false);
		mutacoes++;
		if (!(benefico || mutacoes >= MutacoesQueDobram)) return (mutacoes, false);
		mutacoes++;
		return (mutacoes, sorteioDeMorrer < ChanceDeMorrerNoVirus || mutacoes >= MutacoesQueMatam);
	}

	// ---- os numeros dos efeitos ----
	/// <summary>`usr.TempBuff(list("Tmagi"=1,"Tkiregen"=1),500)`: cinquenta segundos.</summary>
	public const double SegundosDoLsd = 50;
	public const double ChanceDeMorrerDeLsd = 10;

	/// <summary>`sleep(100)`: os dez segundos parado do Antivirus Mike e de quem injeta outro.</summary>
	public const double SegundosParado = 10;

	/// <summary>`sleep(50)` e `sleep(100)`: quanto demora a morte da Insta-Death (o coma, 90% das vezes, ou a demora).</summary>
	public const double SegundosDoComa = 5, SegundosDaDemora = 10;
	public const double ChanceDeComa = 90;

	/// <summary>`usr.relBPmax*(1/100)`, `(1/50)` e `(1/800)`: o poder que os esteroides e a RAGE v2 somam.</summary>
	public const double FracaoDoEsteroide = 1.0 / 100, FracaoDoEsteroide2 = 1.0 / 50, FracaoDaRaiva2 = 1.0 / 800;

	/// <summary>`(((usr.MaxAnger-100)/2.4)+100)` e `/1.24`: que FRACAO da folga de raiva (`MaxAnger - 100`) cada injecao acende.</summary>
	public static double FracaoDaRaiva(bool v2) => 1 / (v2 ? 1.24 : 2.4);

	/// <summary>A raiva, em numero, no instante da injecao.</summary>
	public static double RaivaDaInjecao(double maxAnger, bool v2) => (maxAnger - 100) * FracaoDaRaiva(v2) + 100;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X7): o sintetizador nao confere a tecnologia de quem pede.</summary>
	public static bool SemPedirTecnologiaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X7): o mesmo virus se toma quantas vezes quiser.</summary>
	public static bool VirusRepeteDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X7): a mutacao nao tira vida nem passa adiante.</summary>
	public static bool MutacaoInofensivaDeTeste;
}
