namespace Jandirus.Core.Races;

/// <summary>
/// UM BEBE A CAMINHO (2026-10-17). E a gravidez do DM (`Mating.dm:189-197`: `mom.Pregnant`, `Husband`, `Husband_Race/Class/BP`,
/// `womb`), guardada pelo servidor (`bebes.json`) ate um personagem novo aceitar nascer dele.
///
/// E UMA FOTO DOS PAIS NA HORA DA CONCEPCAO, e nao uma referencia: o filho pode nascer com os dois deslogados. O DM tirava a foto do
/// pai ali mesmo (`Husband_BP = dad.BP / 4`) e lia a da mae no parto (`Parent_BP = M.BP`, `race.dm:21`) -- DIVERGENCIA DECLARADA: a
/// da mae tambem e tirada na concepcao, porque aqui ela nao precisa estar no jogo pro filho nascer.
/// </summary>
public sealed class BebeACaminho
{
	public int Id;
	public long ConcebidoEm;
	public string Pai = "", Mae = "", SigPai = "", SigMae = "";
	public string RacaDoPai = "", RacaDaMae = "";
	public string ClasseDoPai = "", ClasseDaMae = "";

	/// <summary>A linhagem de cada um na regua do filho (<see cref="Filhos.LinhagemDe"/>): vazia nas racas que nao tem.</summary>
	public string LinhagemDoPai = "", LinhagemDaMae = "";

	/// <summary>
	/// O poder que entra na heranca, ja na regua do DM: o pai com um quarto do BP, teto 10.000; a mae com o BP inteiro. No OVO, quem o
	/// pos fica no lugar da mae, com um quarto do BP e sem teto (<see cref="Filhos.Ovo"/>).
	/// </summary>
	public double BpDoPai, BpDaMae;

	/// <summary>
	/// (2026-10-17, a resposta do dono) UM OVO, e nao uma gravidez: quem o pos esta nos campos da MAE -- e ela que "da a luz", o filho
	/// nasce do lado dela -- e o pai fica vazio. Ver <see cref="Filhos.Ovo"/>.
	/// </summary>
	public bool DeOvo;

	/// <summary>
	/// (2026-10-17, o dono: *"tinha um sprite de ovo"*) O OVO NO CHAO: o id da obra <see cref="Filhos.TipoDoOvo"/> que este ovo e no
	/// mundo -- zero na gravidez, e no ovo posto antes de o ovo ter corpo. O filho nasce do lado dela, e quebra-la desfaz o ovo.
	/// </summary>
	public int Ovo;

	/// <summary>A raca do filho: a dos pais, ou o meio-Saiyajin (<see cref="Filhos.MeioSaiyajin"/>).</summary>
	public string Raca = "";

	/// <summary>As linhagens que o filho pode ter: uma (fixa), as duas dos pais (ele escolhe), ou as tres do meio-Saiyajin. Vazia = nenhuma.</summary>
	public string[] Linhagens = [];

	public OfertaDeBebe Oferta => new(Id, Pai, Mae, Raca, Linhagens, DeOvo);
}

/// <summary>O que a tela de criacao mostra de um bebe a caminho -- viaja no fim do `S2C.SlotList`. No ovo, `Pai` e vazio.</summary>
public sealed record OfertaDeBebe(int Id, string Pai, string Mae, string Raca, string[] Linhagens, bool DeOvo);

/// <summary>COMO UM CORPO GERA FILHO (2026-10-17): com um par (a gravidez), sozinho (o ovo), dos dois jeitos, ou nao gera.</summary>
[Flags]
public enum ModoDeGerar
{
	Nenhum = 0,

	/// <summary>Com um par de sexo oposto: "Ter um filho" (o `Have_Child`, `N_Breed`).</summary>
	Gravidez = 1,

	/// <summary>Sozinho: "Pôr um ovo" (o `Lay_Egg`, `E_Breed`).</summary>
	Ovo = 2,
}

/// <summary>
/// QUEM PODE TER UM FILHO, E O QUE O FILHO HERDA (2026-10-17). O dono: *"faça com q seja possivel ter filhos no jogo, entao quando um
/// player chega em nivel relaçao "amor" com outro jogador do sexo oposto, eles possam ter filho"* -- e o resto da frase esta em cada
/// regra abaixo.
///
/// ============================ QUEM PODE ============================
///   * COM UM PAR ("Ter um filho"): o AMOR CORRESPONDIDO -- os dois declararam `Relacao.Amor` um pelo outro (a aba People). No DM era
///     o casamento, que so saia entre amigos (`Mating.dm:56-101`); o dono pediu o nivel "amor". Os dois tem de concordar: um pede e
///     o outro aceita (`Mating.dm:183`). SEXOS OPOSTOS, vivos, acordados e lado a lado (`try_have_child` e `mate_pair_ok`,
///     `Mating.dm:30-37` e `:176-179`).
///   * SOZINHO ("Pôr um ovo"): as racas de ovo -- o dono, *"as raças q poe ovos podem ter filhos sozinhos"* (o `Lay_Egg`,
///     `Mating.dm:222`). Vivo e acordado, um ovo por vez (no DM nao havia limite nem conferencia nenhuma alem do ferimento).
///   * QUEM GERA E COMO (<see cref="ComoGera"/>): o "Breed Type" do proto (`Genetic_Datum.dm:268-282`) -- 0 ovo (Frost Demon,
///     Namekuseijin, Arlian, Kanassa, Meta, Saibaman, Yardrat), 1 gravidez, 2 os dois (Majin, Makyo), 3 nenhum --, com o dono por
///     cima do 3: o MEIO-SAIYAJIN tem filho (no DM, *"Half-Breeds sao estereis"*, `Mating.dm:170-173`), e o ANDROIDE DE ENERGIA
///     INFINITA tambem, como humano. O de absorcao continua sem filho.
///   * A IDADE (<see cref="IdadeParaGerar"/>): os 16 anos do `Mature_Age` (`Mating.dm:19`; o DM so dava os verbos de gerar a partir
///     dela, `Aging.dm:153-156`) -- ou o auge da raca, quando vem antes.
///   * AS RACAS CRUZAM (<see cref="RacaDoFilho"/>): a mesma raca, ou o sangue Humano com o Saiyajin -- o meio-Saiyajin; o androide
///     conta como Humano. Pedido do dono (*"misturar raças sem ser saiyajin e humano n é possivel"*); no DM o genoma misturava qualquer
///     par (`return_new_genome`, `Genetic_Sex.dm:9-70`).
/// ===================================================================
///
/// ============================ O QUE O FILHO HERDA ============================
///   * A CLASSE (mesma raca): a MAIS RARA entre as dos pais -- pedido do dono. No DM saia do genoma (`Class_Spread` do dominante, com
///     a classe dos pais mais pesada, `Genetic_Sex.dm:47-64`).
///   * A LINHAGEM: a dos pais; diferentes (Saiyan x Primal), o filho ESCOLHE entre as duas -- e a classe mais rara e traduzida pra
///     linhagem escolhida (o Lendario de uma tabela vira o Lendario da outra; o resto, a classe comum dela).
///   * O MEIO-SAIYAJIN nao herda classe nem linhagem: escolhe uma das tres dele (*"ele escolhe entre as linhagens de meio
///     saiyajin"*).
///   * O PODER (`CharacterCreation.dm:53-58`): o BP ganha de 1/24 a 1/6 do de cada um (o do pai ja em um quarto, `Genome.StartingBp`),
///     e o potencial escondido ganha os dois inteiros.
///   * O OVO: a raca, a classe e a linhagem (o cla do Namekuseijin) de quem o pos, e um quarto do BP dele, sem teto (<see cref="Ovo"/>).
///   * A IDADE: 0 anos -- o dono pediu; o DM cravava 1 (`startingage`, `CharacterCreation.dm:41-44`).
/// ==========================================================================
/// </summary>
public static class Filhos
{
	public const string MeioSaiyajin = "Halfbreed";

	/// <summary>Quanto tempo o pedido espera a resposta -- o mesmo do convite de mestre.</summary>
	public const double PrazoDoPedidoSegundos = 60;

	/// <summary>"Lado a lado": o pedido sai pra quem esta na frente (o alcance do soco), e o "sim" aceita ate esta distancia em tiles.</summary>
	public const double TilesDoPedido = 4;

	/// <summary>`if(mom.Husband_BP >= 10000) mom.Husband_BP = 10000` (`Mating.dm:196`).</summary>
	public const double TetoDoBpDoPai = 10_000;

	/// <summary>`Mature_Age = 16` (`Mating.dm:19`): o DM so dava os verbos de gerar a partir desta idade (`Aging.dm:153-156`).</summary>
	public const int IdadeMadura = 16;

	/// <summary>
	/// A IDADE PRA GERAR: os 16 anos do DM -- ou o AUGE da raca, quando ela chega nele antes (o Saibaman, aos 10). DIVERGENCIA DECLARADA:
	/// no DM o Saibaman passava dos 16 envelhecendo; aqui a idade so anda na Sala do Tempo, e com os 16 cravados a raca nunca poria ovo.
	/// Quem nao envelhece (o Majin) fica nos 16. E e por esta porta que o recem-nascido nao tem filho.
	/// </summary>
	public static int IdadeParaGerar(string raca) =>
		Envelhecimento.NaoEnvelhece(raca) ? IdadeMadura : (int)Math.Min(IdadeMadura, Envelhecimento.AugeDaRaca(raca));

	/// <summary>
	/// DEFEITO INJETADO (bancada `--filhosteste`): as regras de antes da resposta do dono -- o meio-Saiyajin esteril, o androide sem filho
	/// e ovo nenhum. Falso em jogo.
	/// </summary>
	public static bool FilhosComoAntesDeTeste;

	/// <summary>
	/// A OBRA DO OVO no catalogo de construcoes: o `mob/Egg` do DM (`icon = 'Egg.dmi'`, `NPClist.dm:15-25`), que o `Lay_Egg` poe no chao
	/// (`Mating.dm:230`) e de onde o filho nasce (`SpawnPoints.dm:136-147`).
	/// </summary>
	public const string TipoDoOvo = "Egg";

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--filhosteste` e `--diagovo`): o ovo nao ganha corpo no chao -- o port de antes da resposta do dono,
	/// em que o ovo era guardado como a gravidez. Falso em jogo.
	/// </summary>
	public static bool OvoSemCorpoDeTeste;

	/// <summary>
	/// COMO ESTE CORPO GERA. O "Breed Type" do proto (`assign_breed`, `Genetic_Datum.dm:268-282`): 0 ovo, 1 gravidez, 2 os dois, 3
	/// nenhum -- com as duas palavras do dono (2026-10-17) por cima do 3: o MEIO-SAIYAJIN tem filho (*"meio saiyajin pode ter filho
	/// sim"*), e o ANDROIDE DE ENERGIA INFINITA tambem (*"androides de energia infinita podem ter filho"*). O de absorcao continua sem.
	/// </summary>
	public static ModoDeGerar ComoGera(RaceCatalog cat, string raca, bool androideInfinito)
	{
		if (!FilhosComoAntesDeTeste)
		{
			if (raca == MeioSaiyajin) return ModoDeGerar.Gravidez;
			if (raca == "Android") return androideInfinito ? ModoDeGerar.Gravidez : ModoDeGerar.Nenhum;
		}
		ModoDeGerar modo = cat.Get(raca) is { } p
			? (int)Math.Ceiling(p.MiscStat("Breed Type")) switch
			{
				0 => ModoDeGerar.Ovo,
				1 => ModoDeGerar.Gravidez,
				2 => ModoDeGerar.Gravidez | ModoDeGerar.Ovo,
				_ => ModoDeGerar.Nenhum,
			}
			: ModoDeGerar.Nenhum;
		return FilhosComoAntesDeTeste ? modo & ~ModoDeGerar.Ovo : modo;
	}

	/// <summary>A frase de quem nao tem filho COM UM PAR: o androide de absorcao; quem so poe ovo (que tem o dele); o resto nao gera.</summary>
	public static string PorQueNaoTemFilhoComPar(RaceCatalog cat, string raca, bool androideInfinito)
	{
		if (raca == "Android" && !androideInfinito) return "o androide de absorção não gera filhos (só o de energia infinita).";
		string nome = NomeDaRaca.Bonito(raca);
		return ComoGera(cat, raca, androideInfinito).HasFlag(ModoDeGerar.Ovo)
			? $"{nome} não tem filho com um par -- a raça põe ovo (Pôr um ovo, na aba Other)."
			: $"{nome} não gera filhos.";
	}

	/// <summary>A frase de quem nao poe ovo -- e, se a raca tem filho com um par, por onde.</summary>
	public static string PorQueNaoPoeOvo(RaceCatalog cat, string raca, bool androideInfinito) =>
		ComoGera(cat, raca, androideInfinito).HasFlag(ModoDeGerar.Gravidez)
			? $"{NomeDaRaca.Bonito(raca)} não põe ovo -- a raça tem filho com um par (Ter um filho)."
			: $"{NomeDaRaca.Bonito(raca)} não põe ovo.";

	/// <summary>
	/// O SANGUE DO GENOMA: o androide conta como Humano -- o dono, *"androides de energia infinita podem ter filho e sao tratados como
	/// humanos pra genoma etc"*. (Quem gera e o de energia infinita; o de absorcao e barrado antes, em <see cref="ComoGera"/>.)
	/// </summary>
	public static string SangueDe(string raca) => raca == "Android" ? "Human" : raca;

	/// <summary>
	/// A RACA DO FILHO DE A COM B, ou vazio quando nao cruzam. A mesma raca da a mesma raca; o sangue Humano com o Saiyajin -- quaisquer
	/// dois DIFERENTES entre Humano, Saiyajin e meio-Saiyajin -- da o meio-Saiyajin: Humano com Saiyajin (o pedido), o meio-Saiyajin com
	/// Humano (*"ter filhos com humanos fazer nascer outro meio saiyajin"*) e, escolha minha, o meio-Saiyajin com Saiyajin -- o jogo nao
	/// tem raca de "tres quartos". O androide entra pelo sangue (<see cref="SangueDe"/>): *"saiyajin + androide de energia infinita = meio
	/// saiyajin"*; com Humano, ou com outro androide, da Humano. O ovo e de um so: `RacaDoFilho(r, r)`.
	/// </summary>
	public static string RacaDoFilho(string racaA, string racaB)
	{
		string a = SangueDe(racaA), b = SangueDe(racaB);
		if (a == b) return a;
		return DoSangueSaiyajinHumano(a) && DoSangueSaiyajinHumano(b) ? MeioSaiyajin : "";
	}

	private static bool DoSangueSaiyajinHumano(string raca) => raca is "Human" or "Saiyan" or MeioSaiyajin;

	public static bool SexosOpostos(string a, string b) => (a, b) is ("Male", "Female") or ("Female", "Male");

	/// <summary>
	/// A linhagem de um corpo na regua do filho: a do Saiyajin; nas racas que ESCOLHEM a linhagem na criacao (o meio-Saiyajin, o cla do
	/// Namekuseijin -- `CharacterDraft.EscolhasDeClasse`), a propria classe; as outras racas nao tem.
	/// </summary>
	public static string LinhagemDe(string raca, string classe, string linhagemSaiyajin) =>
		raca == "Saiyan" ? (linhagemSaiyajin.Length > 0 ? linhagemSaiyajin : "Saiyan")
		: Array.IndexOf(CharacterDraft.EscolhasDeClasse(raca), classe) >= 0 ? classe : "";

	/// <summary>
	/// As linhagens que o filho pode ter: as tres do meio-Saiyajin (*"ele escolhe entre as linhagens de meio saiyajin"*); nas outras
	/// racas que tem linhagem, a dos pais, ou as duas quando diferem (o ovo tem um so) -- e, sem nenhuma conhecida, todas (a criacao nao
	/// fecha sem linhagem); nenhuma nas racas sem.
	/// </summary>
	public static string[] LinhagensDoFilho(string racaDoFilho, string linhagemDoPai, string linhagemDaMae)
	{
		string[] todas = CharacterDraft.EscolhasDeClasse(racaDoFilho);
		if (racaDoFilho == MeioSaiyajin || todas.Length == 0) return todas;
		string[] dosPais = [.. new[] { linhagemDoPai, linhagemDaMae }.Where(l => Array.IndexOf(todas, l) >= 0).Distinct()];
		return dosPais.Length > 0 ? dosPais : todas;
	}

	/// <summary>
	/// O OVO -- o `Lay_Egg` (`Mating.dm:222-236`): a FOTO de quem o pos, nos campos da mae (e ela que "da a luz": o filho nasce do lado
	/// dela), com a heranca do DM -- um quarto do BP, SEM teto (`Z.Father_BP=usr.BP/4`, `:233`; o teto de 10.000 e so do pai na gravidez). A
	/// raca, a classe e a linhagem sao as de quem o pos.
	/// </summary>
	public static BebeACaminho Ovo(int id, long quando, string quem, string sig, string raca, string classe, string linhagemSaiyajin, double bp)
	{
		string linhagem = LinhagemDe(raca, classe, linhagemSaiyajin);
		string doFilho = RacaDoFilho(raca, raca);
		return new BebeACaminho
		{
			Id = id, ConcebidoEm = quando, DeOvo = true,
			Mae = quem, SigMae = sig, RacaDaMae = raca, ClasseDaMae = classe, LinhagemDaMae = linhagem,
			BpDaMae = Math.Max(bp, 0) / 4, Raca = doFilho, Linhagens = LinhagensDoFilho(doFilho, "", linhagem),
		};
	}

	/// <summary>
	/// A CLASSE DO FILHO: a mais rara das dos pais (a de menor chance em <see cref="Birth.ChanceDaClasse"/>), na linhagem que ele
	/// escolheu. Vazia no meio-Saiyajin (a linhagem escolhida E a classe dele) e quando nenhum dos pais tem classe que nasca na raca
	/// (o Alien nao tem classe nenhuma). Empate -- o Semideus tem tres de 33 -- e de um dos dois pelo <paramref name="rng"/>.
	/// </summary>
	public static string ClasseDoFilho(RaceCatalog cat, BebeACaminho b, string linhagem, Random rng)
	{
		if (b.Raca == MeioSaiyajin) return "";
		string doPai = NaLinhagemDoFilho(b.Raca, b.ClasseDoPai, b.LinhagemDoPai, linhagem);
		string daMae = NaLinhagemDoFilho(b.Raca, b.ClasseDaMae, b.LinhagemDaMae, linhagem);
		double cPai = Birth.ChanceDaClasse(cat, b.Raca, linhagem, doPai), cMae = Birth.ChanceDaClasse(cat, b.Raca, linhagem, daMae);
		if (cPai <= 0) return cMae > 0 ? daMae : "";
		if (cMae <= 0 || cPai < cMae) return doPai;
		if (cMae < cPai) return daMae;
		return rng.Next(2) == 0 ? doPai : daMae;
	}

	/// <summary>
	/// A CLASSE DE UM DOS PAIS NA LINHAGEM DO FILHO. So o Saiyajin tem duas tabelas (`Birth.RollClass`): o Lendario de uma vira o
	/// Lendario da outra, e o resto vira a classe comum da linhagem do filho -- o filho de um Elite com uma Primal comum que escolhe
	/// ser Primal nasce Primal comum, e o de um Lendario com quem quer que seja nasce Lendario em qualquer das duas.
	/// </summary>
	private static string NaLinhagemDoFilho(string raca, string classe, string linhagemDoPai, string linhagemDoFilho)
	{
		if (raca != "Saiyan" || linhagemDoPai == linhagemDoFilho) return classe;
		bool lendario = classe is "Legendary" or "Legendary Primal Saiyan";
		return linhagemDoFilho == "Primal Saiyan"
			? (lendario ? "Legendary Primal Saiyan" : "Normal Primal Saiyan")
			: (lendario ? "Legendary" : "Normal");
	}

	/// <summary>O BP do pai na regua da heranca: `Husband_BP = dad.BP / 4`, com teto de 10.000 (`Mating.dm:195-196`).</summary>
	public static double BpDoPai(double bp) => Math.Min(Math.Max(bp, 0) / 4, TetoDoBpDoPai);
}
