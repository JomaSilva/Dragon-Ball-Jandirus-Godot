namespace Jandirus.Core.Races;

/// <summary>
/// AS FORMAS DO FROST DEMON -- o `IcerTransform.dm` do original (rework de 2026-07-10).
///
/// ============================ POR QUE ISTO E APARENCIA, E NAO TRANSFORMACAO ============================
/// A escada Saiyajin (`Core/Forms/Formas.cs`) e uma escada de PODER: o corpo e sempre o mesmo e o que
/// muda e o cabelo, a aura e o multiplicador. A do Frost Demon nao -- cada forma dele e um CORPO
/// DIFERENTE, e o jogador escolhe QUAIS corpos serao os dele na criacao do personagem. Foi o que o
/// dono resumiu: "a aparencia do frost demon e onde ele escolhe as formas dele ja q as formas sao a
/// aparencia dele".
///
/// Por isso este arquivo mora em `Races` e nao em `Forms`, e por isso os corpos escolhidos viajam
/// dentro do <see cref="Appearance.Appearance"/> em vez de virarem estado de combate.
/// =========================================================================================================
///
/// ============================ A NUMERACAO E UNICA, DE 1 A 7 ============================
/// E tentador numerar "forma 1, 2, 3" a partir da base. O DM nao faz assim, e a razao aparece no
/// Mutante: ele NASCE preso numa supressao e sobe ate a base. Uma numeracao a partir da base
/// precisaria de indices negativos.
///
///   1 a 4  SUPRESSAO -- so o Mutante tem. Ele nasce na 1, com 25% do proprio poder.
///   5      BASE -- onde o Frost Demon normal nasce.
///   6      PRIMEIRA EVOLUCAO -- dez vezes.
///   7      FORMA BLACK -- vinte vezes.
///
/// O normal escolhe TRES corpos (5, 6, 7); o Mutante escolhe SETE.
/// =======================================================================================
/// </summary>
public static class FormasDeFrost
{
	/// <summary>Quantos degraus a escada tem, contando as supressoes.</summary>
	public const int Total = 7;

	public const int PrimeiraSupressao = 1;
	public const int UltimaSupressao = 4;
	public const int Base = 5;
	public const int Evolucao = 6;
	public const int Black = 7;

	/// <summary>Os nomes das duas classes que o servidor sorteia (1% de chance da mutante).</summary>
	public const string ClasseNormal = "Frost Demon";
	public const string ClasseMutante = "Mutant Frost Demon";

	/// <summary>Esta raca usa este sistema? Aceita as duas grafias que circulam no projeto.</summary>
	public static bool EhFrost(string raca) =>
		raca is "Icer" or "Frost Demon";

	/// <summary>
	/// O MULTIPLICADOR DE PODER de cada degrau -- o `fd_form_mult` (IcerTransform.dm:38-46).
	///
	/// As supressoes sao FRACOES: quem esta preso na primeira luta com um quarto do que tem. A base
	/// e neutra, e dali sobe pra dez e vinte.
	/// </summary>
	public static double Multiplicador(int forma) => forma switch
	{
		1 => 0.25,
		2 => 0.50,
		3 => 0.75,
		4 => 0.90,
		6 => MultiplicadorDaEvolucao,
		7 => MultiplicadorDoBlack,
		_ => 1,
	};

	/// <summary>`FD_FORM6_MULT` e `FD_FORM7_MULT` (1A Defines.dm:32-33).</summary>
	public const double MultiplicadorDaEvolucao = 10;
	public const double MultiplicadorDoBlack = 20;

	/// <summary>
	/// O BP que destrava cada evolucao -- `FD_FORM6_AT` e `FD_FORM7_AT` (1A Defines.dm:35-36).
	/// Zero quer dizer "ja disponivel".
	/// </summary>
	public static double BpNecessario(int forma) => forma switch
	{
		6 => 250_000_000,
		7 => 15_000_000_000,
		_ => 0,
	};

	/// <summary>Como o degrau se chama pro jogador -- o `fd_form_name` (IcerTransform.dm:48-56).</summary>
	public static string Nome(int forma) => forma switch
	{
		1 => "1ª Forma (supressão 25%)",
		2 => "2ª Forma (supressão 50%)",
		3 => "3ª Forma (supressão 75%)",
		4 => "4ª Forma (supressão 90%)",
		6 => "1ª Evolução",
		7 => "Forma Black",
		_ => "Forma Base",
	};

	/// <summary>
	/// QUAIS DEGRAUS ESTE PERSONAGEM PRECISA ESCOLHER CORPO, na ordem em que a tela pergunta.
	///
	/// O normal so ve tres (base, evolucao, black) porque supressao e coisa de mutante -- oferecer
	/// as quatro a ele seria pedir quatro sprites que nunca apareceriam em jogo.
	/// </summary>
	public static int[] DegrausDe(string classe) =>
		classe == ClasseMutante ? [1, 2, 3, 4, 5, 6, 7] : [5, 6, 7];

	/// <summary>Em que degrau este personagem NASCE: o mutante preso na supressao, o normal na base.</summary>
	public static int FormaInicial(string classe) =>
		classe == ClasseMutante ? PrimeiraSupressao : Base;

	/// <summary>
	/// O CORPO PADRAO DE CADA DEGRAU, pra quem nao escolher. Sao os `form_pick_res` do
	/// `body_custom.dm` (linhas 227-240), e existem pelo motivo de sempre: um slot vazio nao pode
	/// virar um personagem invisivel.
	/// </summary>
	public static string PadraoDoDegrau(int forma) => forma switch
	{
		1 => "Changeling Frieza 2",
		2 => "Changling - Form 2",
		3 => "Frostdemon Form 3",
		4 => "Frostdemon Form 4",
		6 => "Changeling Full Power",
		7 => "Koola Final Form",
		_ => "Changeling 5 Kold",
	};

	/// <summary>
	/// O CORPO PADRAO DO FROST DEMON NORMAL, que NAO e o mesmo do mutante.
	///
	/// O DM tem duas listas de default (body_custom.dm:227-233 pro mutante, 238-240 pro normal) e
	/// elas discordam de proposito: o normal comeca na base com o corpo da PRIMEIRA forma do Frieza
	/// -- o pequeno, de chifres --, sobe pra segunda e termina no Koola final. O mutante ja usa o
	/// Kold na base, porque as quatro supressoes abaixo dele ocupam os corpos pequenos.
	/// </summary>
	public static string PadraoDoNormal(int forma) => forma switch
	{
		5 => "Changeling Frieza 2",
		6 => "Changling - Form 2",
		_ => "Koola Final Form",
	};

	/// <summary>O corpo padrao certo pra esta classe e este degrau.</summary>
	public static string Padrao(string classe, int forma) =>
		classe == ClasseMutante ? PadraoDoDegrau(forma) : PadraoDoNormal(forma);

	/// <summary>
	/// TODOS OS CORPOS QUE O JOGADOR PODE ESCOLHER, na ordem do original (body_custom.dm:132-188).
	///
	/// NAO HA ESCOLHA DE LINHAGEM. A tentacao aqui era oferecer "Frieza / Coola / Kold / Kuriza"
	/// como um passo e derivar os sprites disso -- seria mais arrumado. Mas o original deixa a
	/// lista CRUA e livre, e a diferenca importa: da pra montar um Frost Demon que comeca com corpo
	/// de Kuriza, evolui pra um Koola e termina num Kold. A linhagem e um resultado da escolha, nao
	/// uma gaiola em volta dela.
	///
	/// Os nomes sao os dos arquivos sem extensao, e batem com os `.tres` de
	/// `Assets/Sprites/Character Icons/Frost Demons/`.
	/// </summary>
	public static readonly string[] Catalogo =
	[
		"Changeling Frieza 2",
		"Changling - Form 2",
		"Frostdemon Form 3",
		"Frostdemon Form 4",
		"Changeling 5 Kold",
		"GoldIcer",
		"BaseCooler",
		"Changeling 1 Large",
		"Changeling 5 Frieza",
		"Changeling Form 3",
		"Changeling Form 4 Orange",
		"Changeling Form 4",
		"Changeling Frieza 100% 2",
		"Changeling Frieza 100% 3",
		"Changeling Frieza 100%",
		"Changeling Frieza Form 2, 2",
		"Changeling Frieza Form 2",
		"Changeling Frieza Form 3, 2",
		"Changeling Frieza Form 3",
		"Changeling Frieza Form 4, 2",
		"Changeling Frieza Form 4, 3",
		"Changeling Frieza Form 4",
		"Changeling Frieza",
		"Changeling Full Power",
		"Changeling Kold 2",
		"Mecha Frieza",
		"Changeling Kold Form 2",
		"Changeling Kold",
		"Changeling Koola 2",
		"Changeling Koola Expand 2",
		"Changeling Koola Expand",
		"Changeling Koola Form 2",
		"Changeling Koola Form 3, 2",
		"Changeling Koola Form 3",
		"Changeling Koola Form 4, 3",
		"Changeling Koola Form 4",
		"Changeling Koola",
		"Changeling Kuriza",
		"Changeling",
		"Changling - Form 1",
		"Changling - Form 2 Orange",
		"Changling - Form 3 Orange",
		"Changling - Form 3",
		"Changling - Form 6 - Orange",
		"Changling - Form 6",
		"ChanglingForm7",
		"ChanglingMetal",
		"Cooler Form 4",
		"Frostdemon Kold",
		"Frostdemon Koola",
		"Frostdemon",
		"King Kold Form 2 Orange",
		"King Kold Form 2",
		"Koola Final Form",
	];

	/// <summary>Onde moram os sprites deste catalogo.</summary>
	public const string Pasta = "res://Assets/Sprites/Character Icons/Frost Demons/";

	/// <summary>O caminho do `.tres` de um corpo do catalogo.</summary>
	public static string Caminho(string corpo) => $"{Pasta}{corpo}.tres";

	/// <summary>
	/// ARRUMA A LISTA DE CORPOS ESCOLHIDOS pra ela ter exatamente um por degrau desta classe.
	///
	/// Roda no SERVIDOR, e nao so na tela: a lista chega pela rede, e uma lista curta demais, longa
	/// demais ou com um nome que nao existe no catalogo criaria um personagem sem corpo. Slot vazio
	/// ou desconhecido vira o padrao do degrau.
	/// </summary>
	public static List<string> Sanear(string classe, IReadOnlyList<string>? escolhidos)
	{
		int[] degraus = DegrausDe(classe);
		var saida = new List<string>(degraus.Length);

		for (int i = 0; i < degraus.Length; i++)
		{
			string? veio = escolhidos != null && i < escolhidos.Count ? escolhidos[i] : null;
			bool serve = veio is { Length: > 0 } && Array.IndexOf(Catalogo, veio) >= 0;
			saida.Add(serve ? veio! : Padrao(classe, degraus[i]));
		}

		return saida;
	}
}
