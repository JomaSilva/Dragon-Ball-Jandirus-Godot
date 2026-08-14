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

	/// <summary>
	/// O NOME DA RACA NO `races.json`: <b>"Icer"</b> -- e ele NAO e "Frost Demon".
	///
	/// As duas grafias existem e as duas sao do original: `Icer` e o nome do proto racial (e do
	/// planeta natal), e `Frost Demon` e o nome da CLASSE comum dentro dele (a outra e
	/// `Mutant Frost Demon`). Ler uma pela outra e um erro facil e mudo -- um `Race == "Frost Demon"`
	/// nunca casa com ninguem --, entao a constante existe pra quem precisa escrever o nome.
	/// </summary>
	public const string Raca = "Icer";

	/// <summary>Esta raca usa este sistema? Aceita as duas grafias que circulam no projeto.</summary>
	public static bool EhFrost(string raca) =>
		raca is Raca or ClasseNormal;

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
		// O DM CHAMA DE "Forma Black", e o dono pediu o nome que a escada explica: se a anterior e
		// a primeira evolucao, esta e a segunda. "Black" e nome proprio de uma forma especifica --
		// aqui o degrau e generico, e o corpo dele quem escolhe e o jogador.
		7 => "2ª Evolução",
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

	/// <summary>
	/// O caminho do `.tres` de um corpo do catalogo.
	///
	/// ============================ UM CORPO NAO MORA COM OS OUTROS ============================
	/// O `GoldIcer` esta em `Character Icons/Transformations/`, e nao em `Frost Demons/` -- ele e
	/// arte de TRANSFORMACAO, e a pasta foi organizada por isso. Montar o caminho por concatenacao
	/// cega punha o jogo procurando `Frost Demons/GoldIcer.tres`, que nao existe: a grade desenhava
	/// um botao vazio escrito "Gold" e o console enchia de "Cannot open file".
	///
	/// A excecao mora aqui, num lugar so, porque este metodo e a UNICA porta pro caminho -- a grade
	/// da criacao e o desenho do corpo em jogo passam os dois por ele.
	/// =========================================================================================
	/// </summary>
	public static string Caminho(string corpo) => corpo == "GoldIcer"
		? "res://Assets/Sprites/Character Icons/Transformations/GoldIcer.tres"
		: $"{Pasta}{corpo}.tres";

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

	/// <summary>
	/// ============================ QUAL SLOT DA LISTA DESENHA ESTE DEGRAU ============================
	/// A lista de corpos e SANEADA (<see cref="Sanear"/>) e por isso o tamanho dela ja diz de que
	/// classe ela e: sete corpos = Mutante (degraus 1..7), tres = normal (degraus 5..7). O primeiro
	/// degrau da lista e portanto `Total - quantos + 1`, e o slot e a distancia ate ele.
	///
	/// ============================ POR QUE A CONTA SAI DO TAMANHO, E NAO DA CLASSE ============================
	/// Quem desenha o corpo de OUTRO jogador e o cliente, e o cliente nao recebe a classe dos outros
	/// -- ele recebe a aparencia. Pedir a classe aqui obrigaria a por mais um byte no fio pra dizer
	/// uma coisa que a propria lista ja diz, e no dia em que os dois discordassem (um save antigo com
	/// a lista de tres num personagem que virou Mutante) o sprite sairia de outro degrau, calado.
	///
	/// **SLOT 0 E SEMPRE O REPOUSO.** E o que faz `VisualCatalog.CorpoSprite` (que serve
	/// `FormasDeFrost[0]` como "o corpo desta pessoa") concordar com a camada de forma sem ninguem
	/// sincronizar os dois -- ver `CorpoDeForma.FrostEscolhido`.
	///
	/// Devolve -1 quando o degrau nao cabe nesta lista (Frost Demon normal perguntando por uma
	/// supressao, ou lista vazia). Quem chama deixa o corpo como esta.
	/// ====================================================================================================
	/// </summary>
	public static int SlotDoDegrau(int degrau, int quantosCorpos)
	{
		if (quantosCorpos <= 0 || quantosCorpos > Total) return -1;
		int slot = degrau - (Total - quantosCorpos + 1);
		return slot >= 0 && slot < quantosCorpos ? slot : -1;
	}

	// =====================================================================
	// O MOTOR DO MUTANTE -- os knobs `FD_*` do `1A Defines.dm`
	//
	// ============================ POR QUE ELES MORAM AQUI, E NAO NO SERVIDOR ============================
	// No original os sete sao `#define` num arquivo so (`1A Defines.dm:37-43`) lido pelo
	// `IcerTransform.dm`, pelo `icer.dm` e pelo `base.dm`. Aqui eles ficam ao lado dos degraus que
	// governam pelo mesmo motivo: quem for reequilibrar o Mutante mexe num arquivo, e a bancada le
	// os MESMOS numeros que o motor -- uma bancada que redigite `90` mede a bancada.
	//
	// ============================ O OITAVO KNOB **NAO** ESTA AQUI, E ISSO E DIVIDA ============================
	// `FD_ASC_CAP 2.8` (`1A Defines.dm:34`, lido em `ascensioncontrols.dm:82,98`) e o teto da ASCENSAO
	// dos Frost Demons -- o que faz a 2a Evolucao chegar a 20 x 2,8 = 56x e empatar com o topo das
	// outras racas. Ele nao virou constante porque **o port nao tem Ascensao**: o `Fighter.BPBoost`
	// e multiplicado no `powerlevel()` e NUNCA e escrito por ninguem (vale 1 pra todo mundo, sempre).
	//
	// Escreve-lo aqui daria um numero que nada le -- e este projeto ja pagou caro por API orfa (a nota
	// do sigilo de BP). O numero fica NESTE comentario, que e onde quem for portar a Ascensao vai
	// procurar, e o teto do Frost Demon entra junto com o sistema que o usa.
	// =====================================================================================================
	// ================================================================================================
	// =====================================================================

	/// <summary>
	/// `FD_LOSS_SECS_F5` (90): segundos ate PERDER o controle do ki numa forma base instavel, com
	/// zero de maestria. As outras formas escalam por <see cref="FatorDoFusivel"/> e a maestria
	/// ALONGA o fusivel (100% = 3x).
	/// </summary>
	public const double SegundosAtePerderOControle = 90;

	/// <summary>`FD_RELEASE_DECAY_PCT` (1,2): % da liberacao de BP perdida por segundo com o ki solto.</summary>
	public const double VazamentoPorSegundoPct = 1.2;

	/// <summary>`FD_RELEASE_FLOOR` (0,10): o piso da liberacao -- ele fica com 10% do proprio poder.</summary>
	public const double PisoDaLiberacao = 0.10;

	/// <summary>`FD_RELEASE_RECOVER_PCT` (4): % da liberacao recuperada por segundo em forma ESTAVEL.</summary>
	public const double RecuperacaoPorSegundoPct = 4;

	/// <summary>
	/// `FD_REGEN_PCT` (0,2): com 100% de maestria da base, cada GRAU de supressao regenera esta
	/// porcentagem do Ki maximo por segundo. A forma 1 sao quatro graus -- 0,8%/s.
	/// </summary>
	public const double RegeneracaoPorGrauPct = 0.2;

	/// <summary>
	/// O `fd_form_losstime` (`IcerTransform.dm:60-68`): o FATOR de tempo do fusivel em cada forma.
	///
	/// Quanto mais perto da base, menos tempo sobra -- e as evolucoes queimam ainda mais rapido: a
	/// Forma Black dura um quarto do que a base duraria. Nao e progressao linear porque no original
	/// tambem nao e; e a "spec da imagem do design" citada na propria linha do DM.
	/// </summary>
	public static double FatorDoFusivel(int forma) => forma switch
	{
		2 => 8,
		3 => 4,
		4 => 2,
		6 => 0.5,
		7 => 0.25,
		_ => 1,       // forma 5 (base) -- e as supressoes, que nunca chegam aqui
	};

	/// <summary>
	/// ATE QUE DEGRAU O MUTANTE SEGURA O PODER **PRA SEMPRE** -- o `fd_stable_gate()`
	/// (`IcerTransform.dm:71-78`), pela maestria da forma base.
	///
	/// Zero de maestria e ele so aguenta a casca mais apertada; a cada 25 pontos ele destranca mais
	/// uma, e aos 100 a forma base e dele. **Acima disto ele PODE entrar** -- o gate nao e de entrada,
	/// e de permanencia: entrar numa forma que nao se segura e o drama inteiro do Mutante (a cena
	/// vermelha, o ki que trava, o poder que vaza). Fosse gate de entrada, ele nunca poderia entrar na
	/// forma 5 -- e e SO na forma 5 que a maestria cresce. Ficaria trancado em 0% pra sempre.
	/// </summary>
	public static int DegrauEstavel(double maestriaDaBase) => maestriaDaBase switch
	{
		>= 100 => Base,
		>= 75 => 4,
		>= 50 => 3,
		>= 25 => 2,
		_ => PrimeiraSupressao,
	};

	/// <summary>
	/// Os quatro degraus de maestria em que o Mutante ganha uma forma estavel nova. Sao os mesmos
	/// numeros do <see cref="DegrauEstavel"/> -- lidos daqui pelo aviso no chat e pela bancada, pra
	/// ninguem redigitar os 25/50/75/100.
	/// </summary>
	public static readonly double[] MarcosDaBase = [25, 50, 75, 100];
}
