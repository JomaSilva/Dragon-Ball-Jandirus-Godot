namespace Jandirus.Core.Races;

/// <summary>
/// ============================ A SAGA DO CORRUPTED MAJIN -- AS REGRAS E OS NUMEROS ============================
/// Porte da metade "saga" de `Code/Modules/Magic/MajinSaga.dm` (`:395-577`) e do sistema que o amigo do dono escreveu por
/// cima dela (dono, 2026-10-16: *"um amigo meu americano fez um sistema legal pro corrupted majin (classe majin mais rara),
/// quando o majin corrupted fizer toda a saga majin, e no final virar a pure form vai acontecer o seguinte"*).
///
/// AQUI MORA SO O QUE E REGRA E CONTA PURA. Quem move corpo, toca cena e fala no chat e o `Server/GameServer.SagaDoMajin.cs`.
///
/// ============================ A SAGA, NA ORDEM DO DM ============================
///   * 0 -> 1: absorver um KAI (jogador ou NPC, `:146-148` e `:228-230`) -- sem animacao de passagem;
///   * 1 -> 2: ver um amigo MORRER pelas maos de um inimigo (`Death/Death.dm:82`) -- e a OUTRA METADE se solta do corpo;
///   * 2 -> 3: absorver a propria outra metade, VENCIDA (`:124-130`);
///   * 3 -> 4: absorver TRES JOGADORES estando na forma 3 (`:231-234`);
///   * a FORMA PURA destrava na forma 4, sem ninguem dentro e com o BP base em 2 bilhoes (`:511-521`).
/// Cada degrau soma +1 no "Battle Power" do genoma (`:419`) e da os marcos `majin2`/`majin3` (`:416-417`).
///
/// ============================ O SISTEMA DO AMIGO, DEPOIS DA FORMA PURA ============================
/// A forma base do Corrupted Majin VIRA a Forma Pura (a sa, 56x) -- e so absorver alguem o tira dela. Ficar nela gasta a
/// SANIDADE; segurar alguem la dentro a devolve. Sanidade zero = a Forma Pura INSANA (56x subindo ate 76x), com a IA no
/// comando, gritos de bicho no chat, sem Sense e sem teletransporte, e o Ki caindo devagar sem regeneracao -- ate ele
/// acabar (ou alguem derruba-lo): dois minutos de sono e o corpo acorda com 10% de Ki e a cabeca inteira.
/// ======================================================================================================
/// </summary>
public static class SagaDoMajin
{
	// =====================================================================
	// QUEM
	// =====================================================================
	/// <summary>A classe rara do Majin (`races.json`: 5% do spread) -- a unica que anda na saga.</summary>
	public const string Classe = "Corrupted Majin";

	/// <summary>
	/// `is_corrupted_majin()` (`MajinSaga.dm:43-44`): `(Race == "Majin" || Parent_Race == "Majin") &amp;&amp; Class == "Corrupted Majin"`.
	/// </summary>
	public static bool EhCorrompido(string? raca, string? racaDoPai, string? classe) =>
		Combat.AbsorcaoMajin.EhMajin(raca, racaDoPai) && string.Equals(classe, Classe, StringComparison.Ordinal);

	/// <summary>`M.Race == "Kai" || M.Parent_Race == "Kai"` (`MajinSaga.dm:146` e `:228`): o primeiro passo da saga.</summary>
	public static bool EhKai(string? raca, string? racaDoPai) =>
		string.Equals(raca, "Kai", StringComparison.Ordinal) || string.Equals(racaDoPai, "Kai", StringComparison.Ordinal);

	// =====================================================================
	// OS DEGRAUS
	// =====================================================================
	/// <summary>A ultima forma da saga (`majin_saga_form == 4`). Depois dela so a Forma Pura.</summary>
	public const int UltimaForma = 4;

	/// <summary>Quantos JOGADORES a forma 3 precisa absorver pra chegar a 4 (`if(majin_form3_players >= 3)`, `:234`).</summary>
	public const int JogadoresPraUltimaForma = 3;

	/// <summary>
	/// `MAJIN_PURE_BP 2000000000` (`MajinSaga.dm:13`), com o comentario do autor: *"BP base minimo da Pure Form ("minimo de
	/// SSJ3": 20B expressos / ~10x de formas = 2B)"*. O BP BASE, e nao o expresso -- a regra de toda porta deste port.
	/// </summary>
	public const double BpDaFormaPura = 2e9;

	/// <summary>Quanto cada degrau soma no "Battle Power" do genoma (`genome.add_to_stat("Battle Power", 1)`, `:419`) -- o `BPMod` daqui.</summary>
	public const double PoderDeLutaPorDegrau = 1;

	/// <summary>O marco de BP de cada degrau (`:416-417`): o 2 da `majin2`; o 3 e o 4, a `majin3`. O 1 nao da nenhum.</summary>
	public static string MarcoDaForma(int forma) => forma switch
	{
		2 => "majin2",
		>= 3 => "majin3",
		_ => "",
	};

	/// <summary>
	/// A FORMA PURA DESTRAVA AGORA? -- `majin_check_pure_unlock()` (`MajinSaga.dm:511-521`): na forma 4, com NINGUEM dentro
	/// (*"precisa ter PERDIDO todos os absorvidos (expelir/KO/fuga)"*) e o BP base na porta.
	/// </summary>
	public static bool DestravaAFormaPura(int forma, bool jaDestravou, bool alguemDentro, double bpBase) =>
		!jaDestravou && forma == UltimaForma && !alguemDentro && bpBase >= BpDaFormaPura;

	// =====================================================================
	// OS CORPOS -- `majin_form_icon()` (`MajinSaga.dm:396-402`)
	// =====================================================================
	private const string Pasta = "res://Assets/Sprites/Character Icons/Majins/";

	/// <summary>
	/// As quatro folhas da saga, na ordem das formas: `Majin - Form 1.dmi` a `Majin - Form 4.dmi`. Tingidas pela cor que o
	/// jogador escolheu na criacao (`if(majin_color) icon += majin_color`, `:408`) -- a mesma soma do corpo de todo Majin.
	/// </summary>
	public static readonly string[] Corpos =
	[
		Pasta + "Majin - Form 1.tres",
		Pasta + "Majin - Form 2.tres",
		Pasta + "Majin - Form 3.tres",
		Pasta + "Majin - Form 4.tres",
	];

	/// <summary>
	/// ============================ O CORPO DA SAGA VIAJA NO `Appearance.Corpo`, NUMA FAIXA RESERVADA ============================
	/// 101 a 104 = as formas 1 a 4. E o caminho do degrau do bio-androide (`BioAndroids.Corpos`), pelo mesmo motivo escrito la:
	/// o corpo da saga e ESTADO PERMANENTE do corpo (o DM troca o `icon` e nunca o devolve), e e por este campo que a aparencia
	/// de todo mundo viaja -- o pacote da ficha, o guarda-roupa da ultima refeicao, a imagem do interior. Sem canal novo.
	///
	/// A FAIXA COMECA EM 101 E NAO EM 0 porque o Majin JA usa o indice: e o corpo que ele escolheu na criacao (o gordo, o
	/// magro, a mulher). Reaproveitar 0..3 apagaria a escolha dele por baixo da saga sem dizer.
	/// ==================================================================================================================
	/// </summary>
	public const int PrimeiroCorpo = 101;

	/// <summary>O indice de corpo da forma 1..4 da saga.</summary>
	public static int CorpoDaForma(int forma) => PrimeiroCorpo + Math.Clamp(forma, 1, Corpos.Length) - 1;

	/// <summary>Este indice de corpo e de uma forma da saga?</summary>
	public static bool EhCorpoDaSaga(int corpo) => corpo >= PrimeiroCorpo && corpo < PrimeiroCorpo + Corpos.Length;

	/// <summary>A folha de um corpo da saga, ou nula quando o indice nao e da saga.</summary>
	public static string? FolhaDoCorpo(int corpo) => EhCorpoDaSaga(corpo) ? Corpos[corpo - PrimeiroCorpo] : null;

	/// <summary>
	/// O CORPO DA FORMA PURA: `MajinForm5.dmi`, *"o corpo do BOSS Majin Boo (Kid Boo) -- era MajinForm11; a cor do proprio
	/// Majin tinge por cima"* (`MajinSaga.dm:536`). Corpo de FORMA (sai do catalogo, `CorpoDeForma.MajinPuro`), e nao da ficha.
	/// </summary>
	public const string FolhaDaFormaPura = Pasta + "MajinForm5.tres";

	// =====================================================================
	// A PASSAGEM -- a animacao de transformacao que mora em cada folha
	// =====================================================================
	/// <summary>
	/// ============================ A ANIMACAO DA PASSAGEM, DA FOLHA CERTA ============================
	/// O DM toca `flick(animstate, src)` (`MajinSaga.dm:455-460`) com o icone JA trocado pro de DESTINO, e com nomes que nao
	/// existem em folha nenhuma (`"thin transformation"`, `"super majin"`, `"super 2 transformation"`): nenhuma das tres tocava.
	/// As animacoes existem, e moram na folha de ORIGEM -- `thin transform` na Form 1 (o gordo emagrecendo), `super majin
	/// transform` na Form 2, `super 2 transform` na Form 3, `kid trans` na Form 4 (a da Forma Pura, `:567`). Aqui tocam, cada
	/// uma da folha dela. **DIVERGENCIA DECLARADA**: e a intencao do autor, que o codigo dele nao alcancava.
	///
	/// A forma 1 nao tem passagem: o `majin_advance_form(1, null)` do Kai (`:148`) passa `animstate` nulo.
	/// ==============================================================================================
	/// </summary>
	public static (string Folha, string Estado)? PassagemPara(int destino) => destino switch
	{
		2 => (Corpos[0], "thin_transform"),
		3 => (Corpos[1], "super_majin_transform"),
		4 => (Corpos[2], "super_2_transform"),
		_ => null,
	};

	/// <summary>A passagem da Forma Pura: o `kid trans` (`MajinSaga.dm:567`), da folha da forma 4.</summary>
	public static readonly (string Folha, string Estado) PassagemDaPura = (Corpos[3], "kid_trans");

	// =====================================================================
	// OS TEMAS
	// =====================================================================
	/// <summary>`emit_TransformMusic('Super Buu Theme (FULL).mp3', 1730)` (`MajinSaga.dm:437`): as formas 1 a 4.</summary>
	public const string TemaDaSaga = "Super Buu Theme (FULL).mp3";

	/// <summary>`emit_TransformMusic('Buu Is Fighting.mp3', 1060)` (`MajinSaga.dm:563`): a Forma Pura.</summary>
	public const string TemaDaPura = "Buu Is Fighting.mp3";

	// =====================================================================
	// A OUTRA METADE -- `mob/npc/MajinClone` e `majin_spawn_clone()` (`MajinSaga.dm:474-508`)
	// =====================================================================
	/// <summary>
	/// O PODER DA OUTRA METADE: `makeCopy(2, ...)` faz `z.BP = expressedBP/2` (`NPCs/CopyMaker.dm:35`), e o `NPCTicker` do clone
	/// o PINA ali (`clone_seed_bp`, `MajinSaga.dm:481-487`).
	/// </summary>
	public static double BpDaOutraMetade(double expressoDoMestre) => Math.Max(expressoDoMestre / 2, 1);

	/// <summary>
	/// A OUTRA METADE ESTA VENCIDA? `if(!(M.KO || M.dead || M.HP &lt;= 15))` (`MajinSaga.dm:125`) -- o mesmo criterio do guardiao
	/// do interior.
	/// </summary>
	public static bool OutraMetadeVencida(bool ko, bool morto, double hp) => ko || morto || hp <= Combat.AbsorcaoMajin.VidaDoVencido;

	/// <summary>
	/// QUANTO A OUTRA METADE LEVA PRA SE REFAZER quando some sem ter sido absorvida pelo dono (alguem a matou, outro Majin a
	/// comeu, o dono saiu do jogo). **DIVERGENCIA DECLARADA**: no DM ela simplesmente sumia, e o Majin ficava preso na forma 2
	/// pra sempre -- a saga inteira travada por um NPC morto.
	/// </summary>
	public const double SegundosPraOutraMetadeVoltar = 60;

	// =====================================================================
	// A FORMA PURA -- os numeros do amigo do dono
	// =====================================================================
	/// <summary>
	/// A FORMA PURA SA: *"Highest achieved non-Endgame Form 56x bp"* -- o 56x do topo das escadas que nao sao "de fim de jogo"
	/// (o `ssj4fplbmult = 56` do Limit Breaker, o Blue Evolution). No DM ela era `transBuff = 18` (`MajinSaga.dm:535`).
	/// </summary>
	public const double MultDaPura = 56;

	/// <summary>A FORMA PURA INSANA no topo da subida: *"an accelerating rising form bp 76x"*.</summary>
	public const double MultInsanoNoTopo = 76;

	/// <summary>
	/// QUANTO A SUBIDA DA INSANA LEVA, em segundos de loucura, pela maestria da Forma Pura (*"Form mastery affects the rate of
	/// the rising bp"*): 120 s sem maestria, 60 s dominada, em linha reta entre os dois. **ESCOLHA MINHA**, avisada ao dono.
	/// </summary>
	public static double SegundosAteOTopo(double maestria) => 120 - 60 * Math.Clamp(maestria, 0, 100) / 100;

	/// <summary>
	/// QUANTO DA SUBIDA DA INSANA JA ANDOU, de 0 (o 56x) a 1 (o 76x) -- ACELERANDO (quadratica: devagar no comeco, depressa no
	/// fim). Quem le e o `Catalogo.Multiplicador`, entre o primeiro e o ultimo valor do `Mult` da entrada.
	/// </summary>
	public static double SubidaDaLoucura(double loucuraSegundos, double maestria)
	{
		double t = Math.Clamp(loucuraSegundos / SegundosAteOTopo(maestria), 0, 1);
		return t * t;
	}

	/// <summary>
	/// O DRENO DA INSANA, em fracao do Ki maximo por segundo. *"a constantly increasing heavy drain that scales with its rising
	/// form bp"* -- e o dono por cima: *"faz o ki cair bem devagar"*. 0,25%/s a 56x, crescendo com o CUBO do multiplicador
	/// (~0,62%/s a 76x): um tanque cheio dura uns tres minutos e meio de loucura. **ESCOLHA MINHA**, avisada ao dono. E a
	/// regeneracao passiva fica DESLIGADA enquanto isso (o dono: *"desativa regen de ki passiva"*) -- quem desliga e o servidor.
	/// </summary>
	public static double DrenoInsano(double mult) => DrenoInsanoA56 * Math.Pow(Math.Max(mult, 1) / MultDaPura, 3);

	/// <summary>O dreno da insana a 56x: 0,25% do Ki maximo por segundo.</summary>
	public const double DrenoInsanoA56 = 0.0025;

	// =====================================================================
	// A SANIDADE
	// =====================================================================
	/// <summary>A sanidade cheia. Vai pro disco (`Fighter.majin_sanidade`).</summary>
	public const double SanidadeCheia = 100;

	/// <summary>
	/// QUANTO A FORMA PURA SA LEVA PRA ESVAZIAR A SANIDADE: 15 minutos. *"Sanity Meter lowered by using Pure Form"*. **ESCOLHA
	/// MINHA**, avisada ao dono.
	/// </summary>
	public const double SegundosAteEnlouquecer = 15 * 60;

	/// <summary>
	/// QUANTO SEGURAR ALGUEM DENTRO LEVA PRA ENCHER A SANIDADE: 5 minutos. *"Sanity regained by staying out of Pure Form by
	/// remaining in an absorption form"*. **ESCOLHA MINHA**, avisada ao dono.
	/// </summary>
	public const double SegundosAteSarar = 5 * 60;

	/// <summary>O que a Sycophantic Rage custa: *"at the cost of 50% of their sanity gauge"*.</summary>
	public const double CustoDaFuria = 50;

	/// <summary>
	/// O que perder o PRIMEIRO absorvido custa: *"having their first absorb freed"*. O numero e **ESCOLHA MINHA**. E o dono
	/// por cima: *"expulsar n conta como jogador fugir pra ele perder sanidade"*.
	/// </summary>
	public const double CustoDaFuga = 25;

	/// <summary>Sanidade zero = insano.</summary>
	public static bool Insano(double sanidade) => sanidade <= 0;

	/// <summary>Quanto a sanidade anda neste trecho: cai na Forma Pura sa, sobe com alguem dentro, parada no resto.</summary>
	public static double PassoDaSanidade(double sanidade, double dt, bool naPuraSa, bool alguemDentro)
	{
		if (naPuraSa) return Math.Max(0, sanidade - SanidadeCheia * dt / SegundosAteEnlouquecer);
		if (alguemDentro) return Math.Min(SanidadeCheia, sanidade + SanidadeCheia * dt / SegundosAteSarar);
		return sanidade;
	}

	/// <summary>*"fall asleep for two minutes"*.</summary>
	public const double SegundosDormindo = 120;

	/// <summary>*"wake up with 10% Ki and completely refilled Sanity Meter"*.</summary>
	public const double KiAoAcordar = 0.10;

	// =====================================================================
	// MINDLESS GAMBIT -- a passiva
	// =====================================================================
	/// <summary>
	/// QUANTO DO DANO NO TRONCO O CORPO IGNORA -- *"Reduces the damage to a Corrupted Majin's Torso when they are missing
	/// sanity up to a reduction of 20% when sane and up to a reduction of 40% when insane"*. Sa: 20% vezes o que falta da
	/// sanidade; insana: os 40% inteiros.
	/// </summary>
	public static double ReducaoNoTronco(double sanidade, bool insano) =>
		insano ? 0.40 : 0.20 * Math.Clamp(1 - sanidade / SanidadeCheia, 0, 1);

	// =====================================================================
	// SYCOPHANTIC RAGE -- a ativa
	// =====================================================================
	/// <summary>O verb da ativa. Concedido pelo estado (a Forma Pura destravada), e nao por skill da arvore.</summary>
	public const string VerboDaFuria = "Sycophantic_Rage";

	/// <summary>Por quanto o ataque de ki carregando sai: +100% sa, +200% insano (*"by 100%"*, *"by 200%"*).</summary>
	public static double FatorDaFuria(bool insano) => insano ? 3.0 : 2.0;

	/// <summary>O desconto de Ki do ataque que a furia insana pega: *"reduces their Ki cost by 25%"*. Sa: nenhum.</summary>
	public static double DescontoDaFuria(bool insano) => insano ? 0.25 : 0;

	/// <summary>*"Cooldown: 1 Minute"*.</summary>
	public const long RecargaDaFuriaMs = 60_000;

	// =====================================================================
	// ECHOED INSANITY
	// =====================================================================
	/// <summary>*"Corrupted Majins master everything 20% slower"* -- o fator de toda maestria, depois da Forma Pura.</summary>
	public const double FatorDeMaestria = 0.8;

	// =====================================================================
	// OS GRITOS DA LOUCURA
	// =====================================================================
	/// <summary>
	/// O QUE O INSANO GRITA NO CHAT -- o dono: *"enquanto louco ele deve ficar dando gritos no chat e falando parecendo um
	/// animal/macaco estilo kid boo"*. Texto ORIGINAL: so som de bicho, nenhuma fala de obra nenhuma.
	/// </summary>
	public static readonly string[] Gritos =
	[
		"GRAAAAAAAAH!!",
		"Hiiiii-hi-hi-hi-hi!",
		"Uuuuh... UUUUAAAAAH!!",
		"Kyaaaah! Kyah! Kyah!",
		"Rrrrrr... RRRRAAAAAH!!",
		"Hah! Hah! HAAAAAAH!",
		"Ooh-ooh-ooh-AAAAAH!!",
		"Gyahahahahaha!!",
		"Mmmmm... MMMRRRAAAH!",
		"Eeeeeeeeeeeh!!",
		"Grrr... GRAH! GRAH!",
		"Waaaaah-ha-ha-ha-ha!!",
	];

	/// <summary>De quanto em quanto ele grita, em segundos: sorteado entre os dois. **ESCOLHA MINHA**.</summary>
	public const double GritoMinimo = 5, GritoMaximo = 10;
}
