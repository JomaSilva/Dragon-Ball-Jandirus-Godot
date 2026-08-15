namespace Jandirus.Core.Races;

/// <summary>
/// O BIO-ANDROIDE -- os degraus do corpo, e o nome da raca nas DUAS grafias que circulam.
///
/// ============================ POR QUE ISTO E APARENCIA, COMO O FROST DEMON ============================
/// Pelo motivo literal que o <see cref="FormasDeFrost"/> ja tem escrito: o degrau do bio nao e um
/// cabelo nem uma aura, e um CORPO INTEIRO -- e o DM diz isso na propria linha, trocando `icon` **e**
/// `oicon` a cada evolucao (`DNALabs.dm:619-630`, `dnl_larva_mature():506-507`). `oicon` e o corpo de
/// repouso: quem o troca esta dizendo "esta pessoa agora e assim", nao "esta pessoa esta acesa".
///
/// A DIFERENCA PRO FROST: la o jogador ESCOLHE quais corpos serao os dele na criacao, e por isso a
/// lista mora na ficha (<see cref="Appearance.Appearance.FormasDeFrost"/>). Aqui a lista e FIXA -- ha
/// um sprite por degrau e o jogador nao escolhe nenhum --, entao ela mora em codigo. O indice, esse,
/// mora no mesmo campo que o corpo de qualquer raca usa (`Appearance.Corpo`), e e por isso que o
/// degrau viaja pra todo mundo pela rede sem um canal novo: quem ve o bio evoluir ve porque a
/// aparencia dele mudou, que e o mesmo caminho de quem troca de roupa.
/// =========================================================================================================
///
/// ============================ E A SUPER PERFEITA **NAO** ESTA AQUI ============================
/// Os quatro degraus abaixo sao estado permanente (ver <see cref="Stats.Fighter.bio_stage"/>). A
/// Super Perfeita e uma TRANSFORMACAO de verdade -- 8x temporario, com dreno, que cai quando o Ki
/// acaba (`CellFormBuff.dm:1-45`) --, entao ela e uma entrada do
/// <see cref="Forms.Catalogo"/> e o corpo dela sai de la (`CorpoDeForma.BioSuperPerfeito`).
/// =============================================================================================
/// </summary>
public static class BioAndroids
{
	/// <summary>
	/// O NOME DA RACA NO `races.json`: <b>"BioAndroid"</b>, sem hifen.
	///
	/// O DM a chama de `"Bio-Android"` e o extrator de protos gravou a folha do typepath. As duas
	/// grafias circulam pelo projeto inteiro -- `skilltrees.json` usa a do DM, `races.json` e
	/// `visual.json` usam a daqui -- e foi exatamente essa dobra que deixou o ramo do Zenkai
	/// (`Fighter.HasZenkai`) escrito e inalcancavel. Quem precisa perguntar usa <see cref="EhBio"/>.
	/// </summary>
	public const string Raca = "BioAndroid";

	/// <summary>A grafia do DM, que ainda vive nos dados extraidos dele.</summary>
	public const string RacaDoDm = "Bio-Android";

	/// <summary>Esta raca usa este sistema? Aceita as duas grafias, e e a UNICA pergunta do projeto.</summary>
	public static bool EhBio(string? raca) =>
		string.Equals(raca, Raca, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(raca, RacaDoDm, StringComparison.OrdinalIgnoreCase);

	// =====================================================================
	// OS DEGRAUS
	// =====================================================================
	/// <summary>A larva (`CellLarva.dmi`). Nasce assim, e expressa 1% do proprio poder.</summary>
	public const int Larva = 1;

	/// <summary>Imperfeito (`Bio Android 1.dmi`). A carapaca rompeu: 100% do poder liberado.</summary>
	public const int Imperfeito = 2;

	/// <summary>Semi-perfeito (`Bio Android 2.dmi`). BP base DOBRA.</summary>
	public const int SemiPerfeito = 3;

	/// <summary>Perfeito (`Bio Android 3.dmi`). BP base QUADRUPLICA, e a forma e permanente.</summary>
	public const int Perfeito = 4;

	/// <summary>
	/// OS CORPOS, um por degrau -- a lista que o <see cref="Appearance.VisualCatalog"/> serve.
	///
	/// ELA NAO SAI DO `visual.json` de proposito, e a razao e mecanica e nao de gosto: aquele arquivo
	/// e GERADO pelo `AssetPipeline visual`, que extrai o passo de escolha de corpo do DM -- e o
	/// Bio-Androide **nao tem passo nenhum** la (`DmAppearanceScanner.cs:75` diz isso por escrito,
	/// junto com o Frost Demon). Uma entrada escrita a mao naquele json seria apagada na proxima
	/// geracao, calada. Em codigo ela sobrevive, e e o mesmo lugar em que o Frost Demon guarda a
	/// dele.
	/// </summary>
	public static readonly string[] Corpos =
	[
		"res://Assets/Sprites/DU/Mobs/Cell Larva.tres",
		"res://Assets/Sprites/Character Icons/Bioandroids/Bio Android 1.tres",
		"res://Assets/Sprites/Character Icons/Bioandroids/Bio Android 2.tres",
		"res://Assets/Sprites/Character Icons/Bioandroids/Bio Android 3.tres",
	];

	/// <summary>
	/// O CORPO DA SUPER PERFEITA (`Bio Android 4.dmi`, `body_custom.dm:249`).
	///
	/// Separado da lista porque ele NAO e um degrau de <see cref="Stats.Fighter.bio_stage"/>: e a
	/// camada que a forma veste por cima do corpo perfeito, e quem a pede e o
	/// `Client.CorposDeForma`. Foi justamente ele que o `dnl_bio_hatch` do original **esquece de
	/// escrever** (`form4icon` fica nulo e so o fallback do `Stats.dm:210` salva a cara do bicho);
	/// aqui ele e uma constante e nao ha o que esquecer.
	/// </summary>
	public const string CorpoSuperPerfeito =
		"res://Assets/Sprites/Character Icons/Bioandroids/Bio Android 4.tres";

	/// <summary>O sprite do degrau, com o degrau vindo em 1..4. Fora da faixa cai na larva.</summary>
	public static string Caminho(int estagio) =>
		Corpos[Math.Clamp(estagio, Larva, Perfeito) - 1];

	/// <summary>O INDICE de `Appearance.Corpo` que representa este degrau.</summary>
	public static int IndiceDoCorpo(int estagio) => Math.Clamp(estagio, Larva, Perfeito) - 1;

	/// <summary>
	/// ESTE CORPO DESENHA UM ROSTO? -- a metade bio da pergunta que
	/// <see cref="Forms.Catalogo.FolhaTemRosto"/> faz das folhas de forma.
	///
	/// SO A LARVA NAO TEM. A `CellLarva.dmi` e uma barata -- nao ha olho, nem boca, nem cabeca onde
	/// pendurar coisa de gente --, e os tres degraus acima dela sao humanoides desenhados. Foi
	/// exatamente isso que o dono viu: *"os bio androides o OLHO FICA VOANDO na forma de BARATA"*, com
	/// as duas pupilas flutuando acima do casco.
	///
	/// **RECEBE O INDICE DE `Appearance.Corpo`, e nao o `bio_stage`.** Os dois andam juntos (o
	/// <see cref="IndiceDoCorpo"/> e a conversao), mas quem pergunta e o desenho -- e o desenho so
	/// conhece o indice do corpo vestido. Pedir o degrau obrigaria o cliente a saber que existe uma
	/// ficha de combate por tras de um sprite.
	/// </summary>
	public static bool CorpoTemRosto(int indiceDoCorpo) => indiceDoCorpo != IndiceDoCorpo(Larva);

	// =====================================================================
	// OS NUMEROS DA EVOLUCAO -- `DNALabs.dm:39-47`
	// =====================================================================
	/// <summary>`DNL_BIO_BP_SHARE`: o bio nasce com metade do BP do doador mais forte.</summary>
	public const double FracaoDoDoador = 0.5;

	/// <summary>`DNL_BIO_MAX_DNA`: quantas amostras cabem numa fornada.</summary>
	public const int MaxDna = 4;

	/// <summary>`DNL_BIO_EVO_PLAYERS`: dez JOGADORES absorvidos evoluem um degrau.</summary>
	public const double JogadoresPraEvoluir = 10;

	/// <summary>`DNL_BIO_EVO_NPC_WEIGHT`: um NPC vale METADE de um jogador (vinte NPCs).</summary>
	public const double PesoDoNpc = 0.5;

    /// <summary>`DNL_BIO_EVO_ANDROIDS`: **um** androide de verdade evolui sozinho -- o atalho do Cell.</summary>
	public const double AndroidesPraEvoluir = 1;

	/// <summary>`DNL_BIO_EVO2_MULT` e `DNL_BIO_EVO3_MULT`: o BP BASE dobra e depois quadruplica.</summary>
	public const double MultDoSemiPerfeito = 2, MultDoPerfeito = 4;

	/// <summary>
	/// QUANTO FALTA PRO PROXIMO DEGRAU, ou 0 quando ja da. Devolve JOGADORES (o NPC ja entrou com
	/// peso meio na contagem) -- e a mesma frase que o original manda no chat (`DNALabs.dm:545`).
	/// </summary>
	public static double FaltamJogadores(double jogadores) =>
		Math.Max(JogadoresPraEvoluir - jogadores, 0);

	/// <summary>A contagem ja fecha um degrau? `bio_note_absorb`, `DNALabs.dm:541`.</summary>
	public static bool EvoluiAgora(double jogadores, double androides) =>
		androides >= AndroidesPraEvoluir || jogadores >= JogadoresPraEvoluir;

	/// <summary>O que o BP BASE vira ao chegar neste degrau. 1 = nao mexe.</summary>
	public static double MultDoDegrau(int estagio) => estagio switch
	{
		SemiPerfeito => MultDoSemiPerfeito,
		Perfeito => MultDoPerfeito,
		_ => 1,
	};

	/// <summary>O marco de ganho de BP que este degrau concede (`LinearGain.dm:22`).</summary>
	public static string MarcoDoDegrau(int estagio) => estagio switch
	{
		Imperfeito => "bio1",
		SemiPerfeito => "bio2",
		Perfeito => "bio3",
		_ => "",
	};

	/// <summary>O nome que o jogo usa pra cada degrau, pra mensagem nao ser um numero.</summary>
	public static string NomeDoDegrau(int estagio) => estagio switch
	{
		Larva => "larva",
		Imperfeito => "forma imperfeita",
		SemiPerfeito => "forma semi-perfeita",
		Perfeito => "FORMA PERFEITA",
		_ => "forma desconhecida",
	};
}
