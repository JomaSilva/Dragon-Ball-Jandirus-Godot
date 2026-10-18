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

	/// <summary>
	/// (2026-10-17) ESTE CORPO E UMA LARVA? -- a recem-nascida (`bio_stage == Larva`, sem degrau maduro ainda) ou a vestida por vontade
	/// (`bio_larva`, o adendo do dono). Pras duas vale o mesmo: o poder selado -- SEMPRE RELAXADA, 5 de poder, fora do Sense e do scouter
	/// (`PoderEmUso.Relaxado`; o dono: *"fica sempre no modo 'relaxado' com 5 de bp e escondido do sense e rastreadores"*) --, sem absorver,
	/// sem forma nenhuma, voando sem a skill, e so sai dela enterrada.
	/// </summary>
	public static bool EmLarva(Stats.Fighter f) => f.bio_lab_born && (f.bio_stage == Larva || f.bio_larva);

	/// <summary>O degrau que o CORPO mostra: a larva enquanto ela durar, senao o degrau maduro.</summary>
	public static int EstagioDoCorpo(Stats.Fighter f) => EmLarva(f) ? Larva : f.bio_stage;

	/// <summary>
	/// (2026-10-17) QUANTO TEMPO ENTERRADA A LARVA AMADURECE -- o adendo: 5 minutos debaixo da terra, e ela amadurece e volta na forma de antes.
	/// Sair da terra antes ZERA a conta (ESCOLHA: "5 minutos enterrada" e um so trecho).
	/// </summary>
	public const double SegundosEnterradoPraAmadurecer = 5 * 60;

	// =====================================================================
	// OS NUMEROS DA EVOLUCAO -- `DNALabs.dm:39-47`
	// =====================================================================
	/// <summary>`DNL_BIO_BP_SHARE`: o bio nasce com metade do BP do doador mais forte.</summary>
	public const double FracaoDoDoador = 0.5;

	/// <summary>`DNL_BIO_MAX_DNA`: quantas amostras cabem numa fornada.</summary>
	public const int MaxDna = 4;

	// =====================================================================
	// (2026-10-17) OS MARCOS DO BIO -- o adendo do dono
	// =====================================================================
	/// <summary>
	/// ============================ A ESCADA DO BIO PASSOU A SER DE MARCOS ============================
	/// O dono (2026-10-17): *"conforme vc vai absorvendo mais seres organicos mais multiplicador de bp sua nova forma te da, [...] ao absorver
	/// materia organica (npcs ou players, com npcs dando bem menos progresso) vc começa a ganhar "xp" ate ir pro proximo milestone, se vc
	/// atingir o ultimo milestone da sua forma e continuar absorvendo ate chegar no "sexto" vc vai pro proximo estagio de evoluçao"*. A tabela
	/// dele, em multiplicador ABSOLUTO sobre o imperfeito:
	///
	///   Imperfeito      1x -- o comeco. O dono nao deu marcos a ele: os passos andam, o multiplicador nao.
	///   Semi-Perfeito   2x -> marcos 2,2 / 2,4 / 2,6 / 2,8 / 3
	///   Perfeito        4x -> marcos 6 / 8 / 10 / 12 / 14
	///
	/// No DM a escada era de CONTAGEM: dez jogadores (o NPC valendo meio) ou UM androide por degrau (`DNALabs.dm:39-47`), e o degrau
	/// multiplicava o BP base -- 2x e depois 4x por cima, o perfeito valendo 8x. Do DM ficou: o multiplicador MORA NO BP BASE (cada passo da
	/// escada multiplica pela razao; o porque esta em `Fighter.bio_stage`), e o androide como atalho -- agora so o de ENERGIA INFINITA, e ele
	/// pula pra forma seguinte (no perfeito, pro marco 5: *"se vc absorver um androide dps de virar perfeito vc vai pro perfect form milestone
	/// 5"*).
	///
	/// OS PASSOS: um jogador absorvido vale um passo, e um NPC um quinto (ESCOLHA -- o dono: "bem menos"). Cinco passos fazem os cinco marcos,
	/// e o sexto sobe a forma. No perfeito o marco 5 e o teto: dali pra cima e a Super Perfeita, que e forma e tem os requisitos dela.
	/// ========================================================================================================================================
	/// </summary>
	public const int MarcosPorForma = 5;

	/// <summary>Os passos de cada absorcao (ESCOLHA: o NPC vale um quinto do jogador).</summary>
	public const double PassoDoJogador = 1, PassoDoNpc = 0.2;

	/// <summary>O multiplicador da forma semi-perfeita e o da perfeita, no marco zero -- a porta da Super Perfeita le o segundo.</summary>
	public const double MultDoSemiPerfeito = 2, MultDoPerfeito = 4;

	private static readonly double[] DoSemiPerfeito = [MultDoSemiPerfeito, 2.2, 2.4, 2.6, 2.8, 3];
	private static readonly double[] DoPerfeito = [MultDoPerfeito, 6, 8, 10, 12, 14];

	/// <summary>
	/// O MULTIPLICADOR ABSOLUTO deste degrau neste marco (sobre o BP do imperfeito). A larva conta pelo degrau a que ela volta -- quem
	/// pergunta passa o MADURO.
	/// </summary>
	public static double MultAbsoluto(int estagio, int marco) => estagio switch
	{
		SemiPerfeito => DoSemiPerfeito[Math.Clamp(marco, 0, MarcosPorForma)],
		Perfeito => DoPerfeito[Math.Clamp(marco, 0, MarcosPorForma)],
		_ => 1,
	};

	/// <summary>
	/// ONDE A ESCADA FICA depois de um ganho de passos: (degrau, marco, passos que sobram rumo ao proximo). Pura -- quem aplica o BP pela
	/// razao, o corpo e a cena e o servidor. A folga de 1e-9 e o ponto flutuante: cinco NPCs a 0,2 tem que fechar um passo.
	/// </summary>
	public static (int Estagio, int Marco, double Passos) Andar(int estagio, int marco, double passos, double ganho)
	{
		estagio = Math.Clamp(estagio, Imperfeito, Perfeito);
		marco = Math.Clamp(marco, 0, MarcosPorForma);
		passos = Math.Max(passos, 0) + Math.Max(ganho, 0);
		while (passos >= 1 - 1e-9)
		{
			if (marco < MarcosPorForma) { marco++; passos -= 1; continue; }
			if (estagio >= Perfeito) break;                 // o teto: o marco 5 do perfeito
			estagio++;
			marco = 0;
			passos -= 1;
		}
		if (estagio >= Perfeito && marco >= MarcosPorForma) passos = 0;
		return (estagio, marco, Math.Max(passos, 0));
	}

	/// <summary>
	/// O ATALHO DO ANDROIDE DE ENERGIA INFINITA: a forma seguinte no marco zero, sem os marcos -- e no perfeito, o marco 5.
	/// </summary>
	public static (int Estagio, int Marco) PeloAndroide(int estagio, int marco) =>
		estagio >= Perfeito ? (Perfeito, MarcosPorForma) : (Math.Clamp(estagio + 1, SemiPerfeito, Perfeito), 0);

	/// <summary>
	/// A ABSORCAO DE EXECUCAO (o adendo do dono): o prazo -- 5 segundos -- e o que ela devolve a quem absorve, 20% da
	/// vida (em cada membro), do folego e do Ki maximos. AQUI NO CORE porque a TELA le o mesmo prazo: e nele que o corpo da vitima seca
	/// (`Client/CorpoSecando.cs`).
	/// </summary>
	public const double SegundosDaExecucao = 5, DevolveAExecucao = 0.20;

	/// <summary>
	/// (2026-10-17) A ORDEM DA CAUDA NA ABSORCAO DE COMBATE -- o adendo do dono: braco esquerdo, braco direito, perna esquerda, perna direita,
	/// cabeca, e por fim o tronco. Cada estocada fura o primeiro desta lista que ainda NAO esta
	/// ferido (quebrado ou decepado -- a mesma leitura de "ferido" do nucleo); com os cinco feridos, o tronco.
	/// </summary>
	public static readonly string[] OrdemDaEstocada = ["Braco esquerdo", "Braco direito", "Perna esquerda", "Perna direita", "Cabeca"];

	/// <summary>
	/// O QUE UMA ESTOCADA TIRA E DEVOLVE: a quem fura, 5% do tronco, do Ki e do folego (o adendo). ESCOLHAS (o dono deu so os 5%): ela tira 30%
	/// do membro furado (tres estocadas o quebram) e 5% do Ki e do folego de quem e furado; e e uma por segundo.
	/// </summary>
	public const double FracaoDaEstocada = 0.30, DrenoDaEstocada = 0.05, DevolveAEstocada = 0.05, SegundosEntreEstocadas = 1;

	/// <summary>O MEMBRO DA PROXIMA ESTOCADA: o primeiro inteiro da ordem, ou o tronco. Nulo sem tronco -- nao ha mais o que beber.</summary>
	/// <summary>
	/// (2026-10-17) ONDE O NUCLEO PODE MORAR (o adendo do dono), pelos nomes do corpo (`Combat.Body`): os bracos, as pernas e a cabeca.
	/// </summary>
	public static readonly string[] LugaresDoNucleo = ["Braco esquerdo", "Braco direito", "Perna esquerda", "Perna direita", "Cabeca"];

	/// <summary>
	/// As vezes que o nucleo reergue o corpo (a ultima ja detona), o quanto de tudo ele devolve (5%, o adendo) e quanto tempo o corpo reerguido
	/// fica no chao (ESCOLHA: o nocaute curto de quem se refaz -- o teto do nocaute de soco, `MeleeResolver.TetoDoNocaute`, e de minutos).
	/// </summary>
	public const int RevividasDoNucleo = 2;
	public const double RefilDoNucleo = 0.05, SegundosNoChaoPeloNucleo = 10;

	/// <summary>
	/// O NUCLEO ESTA INTEIRO? -- desligado se o membro dele estiver ferido (o adendo). "Ferido" e QUEBRADO (abaixo de 20%) ou decepado, a
	/// mesma leitura da cauda (<see cref="MembroDaEstocada"/>).
	/// </summary>
	public static bool NucleoInteiro(Combat.Body corpo, string lugar) => corpo.Achar(lugar) is { Decepado: false, Quebrado: false };

	public static Combat.BodyPart? MembroDaEstocada(Combat.Body corpo)
	{
		foreach (string nome in OrdemDaEstocada)
			if (corpo.Achar(nome) is { Decepado: false, Quebrado: false } membro) return membro;
		return corpo.Achar(Combat.Body.NomeDoTronco) is { Decepado: false } tronco ? tronco : null;
	}

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
