using Jandirus.Core.Items;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Tech;

/// <summary>
/// OS COMPANHEIROS -- `obj/items/companion_obj` (a capsula) e `mob/npc/pet` (a criatura), de
/// `Code/Modules/NPCs/companions.dm` e `companion_list.dm`: a Pee-Kay Ball, a capsula de Saibaman, o nucleo de boneca,
/// o nucleo do interceptador e a racao.
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// A CAPSULA guarda uma criatura. O `Use` a solta ao lado de quem a carrega (*"[mob] appears!!"*) e a recolhe
/// (*"[mob] disappears!"*) -- no maximo DUAS soltas por dono. Solta, ela e um NPC que segue o dono e obedece: o
/// clique nela abre as ordens, e quantas ordens ela entende depende da INTELIGENCIA (`techmod`): seguir/ficar e
/// atacar (1), matar (2), atacar quando o dono ataca (3) e quando ele apanha (4, as duas so com a relacao acima de
/// 20), destruir (6), pegar/largar/falar/vestir (7), bordoes (8). Ela ganha experiencia lutando, sobe de nivel (ate
/// 10; cada nivel pede 10 x o nivel) e cada nivel da uma MELHORIA pra gastar: fisicos, ki, variados ou poder.
/// As tres de fabrica: o SAIBAMAN (poder 1.000, inteligencia 2; a capsula pede uma semente), a BONECA (poder 1,
/// inteligencia 8, o dobro de ki) e o INTERCEPTADOR (inteligencia 4 -> 6), que a cada segundo e meio derruba os
/// disparos de ki que nao sao do dono a ate nove tiles. A PEE-KAY BALL nasce vazia e CAPTURA um NPC nocauteado a
/// frente: guarda o nome, a cara e o poder dele. A RACAO da tres de inteligencia a criatura solta ao lado.
/// O "Update BP" (10.000 zeni e uma comida) recalcula o poder dela pela media do servidor.
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * AS ORDENS SAEM DA CAPSULA, na mochila: este port nao tem clique em NPC (as copias do Split Form tambem
///     obedecem por verbo). "Atacar" e "matar" valem pro ALVO MARCADO do dono, e nao pra um nome escolhido numa lista.
///   * O QUE NAO VEIO: trocar o icone (era envio de arquivo), "Destroy" (bater num tile ou objeto), pegar e largar
///     item, vestir roupa e equipamento, e os bordoes -- o NPC deste port nao carrega mochila nem roupa trocavel, e a
///     fala dele tem dono proprio. "Falar" veio: a criatura diz a frase.
///   * A CRIATURA NAO TROCA DE MAPA. La ela reaparece ao lado do dono quando ele muda de mapa; aqui ela VOLTA PRA
///     CAPSULA quando o dono sai da zona (ou do jogo), e e so solta-la de novo. Longe demais no mesmo mapa (60 tiles),
///     ela reaparece ao lado dele, como la.
///   * A RELACAO CRESCE COM O TEMPO SOLTA, um ponto a cada dez minutos. La e um sorteio por tique que depende do
///     temperamento (um em mil a cada segundo e meio, mais um em 450 por segundo de luta).
///   * A EXPERIENCIA E UM PONTO POR SEGUNDO DE LUTA (la, 50% a cada meio segundo -- a mesma media). A experiencia por
///     IDADE nao veio: este port nao tem o calendario (`Year`), e a conta de la (`5 * Year - linked_mob_age`, sem
///     nunca avancar a idade) da o nivel maximo de uma vez na primeira virada de ano.
///   * O MODIFICADOR DE PODER VALE. La o `linked_bp_mod` so e usado numa linha que nunca dispara
///     (`if(linked_mob_bp &lt; first_BP*0.9)`, depois de um `max(..., first_BP)`), e a melhoria de "BP" so refaz a
///     conta. Aqui ele MULTIPLICA o poder recalculado -- e o que o `Info` de la anuncia. O `BPSkew` (a assimetria da
///     distribuicao de poder do servidor, que este port nao mede) entra como 1.
///   * A RACAO FUNCIONA. O `Use()` de la compara o dono da criatura com a propria racao (`M.owner_ref == src`), que
///     nunca e verdade.
///   * NAO SE CAPTURA chefe de saga, lutador de torneio, nem o que uma tecnica ou uma maquina ergueu (copia,
///     reflexo, simulacao, guardiao). La o crivo e so "nao e mascote, nao e simulacao".
///   * O PODER DA CRIATURA MORA NO COFRE do servidor, e o `Info` so o diz em numero a quem tem medidor (regra do
///     dono: sem scouter ninguem sabe o poder de ninguem).
/// ==================================================================================
/// </summary>
public static class Companheiros
{
	public const string Bola = "Pkball", CapsulaDeSaibaman = "Saibaman_Capsule", NucleoDeBoneca = "Doll_Core",
						NucleoDoInterceptador = "Intercepter_Core", Racao = "Pet_Food";

	public const string AcaoSoltar = "comp_soltar", AcaoCapturar = "comp_capturar", AcaoInfo = "comp_info",
						AcaoSeguir = "comp_seguir", AcaoAtacar = "comp_atacar", AcaoMatar = "comp_matar", AcaoParar = "comp_parar",
						AcaoQuandoAtaco = "comp_quando_ataco", AcaoQuandoApanho = "comp_quando_apanho",
						AcaoFalar = "comp_falar", AcaoNome = "comp_nome", AcaoMelhorar = "comp_melhorar",
						AcaoPoder = "comp_poder", AcaoRacao = "comp_racao";

	/// <summary>O que uma capsula de fabrica guarda (`linked_mob_type` e os numeros dela, `companion_list.dm:86-108`).</summary>
	public sealed record Especie(string Nome, string Raca, double Bp, double BpMod, int Inteligencia, double Ki, double KiMod);

	public const string EspecieCapturada = "Capturado";

	/// <summary>A "raca" do interceptador: fora do `races.json`, e a chave da folha dele (um corpo de folha so, como o do bicho).</summary>
	public const string RacaDoInterceptador = "Interceptor";

	private const string FolhaDoInterceptador = "res://Assets/Sprites/NPCs/Interceptor.tres";

	/// <summary>A folha do corpo desta raca, quando ela e de um companheiro de folha so.</summary>
	// (lote T6) o droide do super computador e o segundo corpo de folha so da tech -- ver `SuperComputador`.
	public static string? FolhaDoCorpo(string? raca) =>
		raca == RacaDoInterceptador ? FolhaDoInterceptador
		: raca == SuperComputador.CorpoDoDroide ? SuperComputador.FolhaDoDroide
		: null;

	/// <summary>Este corpo e uma folha so (sem cabelo, sem rosto, sem roupa por cima)?</summary>
	public static bool EhDeFolha(string? raca) => raca == RacaDoInterceptador || raca == SuperComputador.CorpoDoDroide;

	// Os numeros sao os das tres capsulas e dos tres `mob/npc/pet` (`companion_list.dm:86-108` e `:222-289`):
	// `linked_mob_bp`, `linked_bp_mod`, `linked_mob_intelligence`, e o `MaxKi`/`KiMod` do tipo.
	public static readonly Especie DoSaibaman = new("Saibaman", "Saibaman", 1000, 0.8, 2, 5, 1);
	public static readonly Especie DaBoneca = new("Doll", "SpiritDoll", 1, 0.7, 8, 100, 2);
	public static readonly Especie DoInterceptador = new("Interceptor", RacaDoInterceptador, 1, 0.5, 4, 5, 1);

	/// <summary>A especie de uma capsula de fabrica, ou nulo (a Pee-Kay Ball nasce vazia).</summary>
	public static Especie? DaCapsula(string @base) => @base switch
	{
		CapsulaDeSaibaman => DoSaibaman,
		NucleoDeBoneca => DaBoneca,
		NucleoDoInterceptador => DoInterceptador,
		_ => null,
	};

	/// <summary>A especie de fabrica com este nome, ou nulo (a criatura capturada nao tem especie de fabrica).</summary>
	public static Especie? DaEspecie(string nome) =>
		nome == DoSaibaman.Nome ? DoSaibaman : nome == DaBoneca.Nome ? DaBoneca : nome == DoInterceptador.Nome ? DoInterceptador : null;

	/// <summary>Este item e uma capsula de companheiro (uma das quatro)?</summary>
	public static bool EhCapsula(string @base) => @base is Bola or CapsulaDeSaibaman or NucleoDeBoneca or NucleoDoInterceptador;

	// ---- a capsula: `<base>|c<gaveta>|<inteligencia>` ----
	/// <summary>A gaveta do cofre em que a criatura desta capsula mora; zero quando ela ainda nao tem (capsula nova).</summary>
	public static int Gaveta(string id) => TechDeBolso.Gaveta(TechDeBolso.Campo(id, 0));

	/// <summary>
	/// A inteligencia da criatura, como o id a carrega: e ela que diz que ordens a mochila mostra. (Nao e numero de
	/// poder: pode viajar no id. A da capsula nova e a da especie.)
	/// </summary>
	public static int Inteligencia(string id) =>
		int.TryParse(TechDeBolso.Campo(id, 1), out int n) && n > 0 ? n : DaCapsula(TechDeBolso.Base(id))?.Inteligencia ?? 1;

	public static string Escrever(string @base, int gaveta, int inteligencia) =>
		gaveta > 0 ? TechDeBolso.Com(@base, TechDeBolso.EscreverGaveta(gaveta), inteligencia.ToString(System.Globalization.CultureInfo.InvariantCulture)) : @base;

	/// <summary>O teto de letras do nome de uma criatura.</summary>
	public const int MaxDeNome = 20;

	// ---- os limites ----
	/// <summary>`usr.out_mobs.len &lt;= 1`: no maximo duas criaturas soltas por dono.</summary>
	public const int MaxSoltos = 2;

	/// <summary>`linked_mob_max_lvl = 10`, e `10 * linked_mob_lvl` de experiencia pro proximo nivel.</summary>
	public const int NivelMaximo = 10;
	public static double ExpDoNivel(int nivel) => 10.0 * nivel;

	/// <summary>`while(owner_ref &amp;&amp; d &gt; f_keep_dist + 1)`: ela so anda atras do dono a mais de tres tiles.</summary>
	public const int TilesDeFolga = 3;

	/// <summary>`if(... d &gt; 60) loc = owner_ref.loc`: longe assim, reaparece ao lado dele.</summary>
	public const int TilesDeSumico = 60;

	/// <summary>`sleep(15)` e `view(9)` do `InterceptorAI`.</summary>
	public const double SegundosDoInterceptador = 1.5;
	public const int RaioDoInterceptador = 9;

	/// <summary>"Update BP will require 10000 and one food item."</summary>
	public const double CustoDeAtualizar = 10000;

	/// <summary>`M.techmod+=3`.</summary>
	public const int InteligenciaDaRacao = 3;

	// ---- quanta inteligencia cada ordem pede (`companion_list.dm:117-134`) ----
	public const int IntDeObedecer = 1, IntDeMatar = 2, IntDeQuandoAtaco = 3, IntDeQuandoApanho = 4, IntDeFalar = 7;

	/// <summary>`relation["[usr.signature]"]&gt;20`.</summary>
	public const double RelacaoDeConfianca = 20;

	/// <summary>Quanto tempo solta vale um ponto de relacao -- ver o cabecalho.</summary>
	public const double SegundosPorRelacao = 600;

	/// <summary>`A.zenni = rand(100,4000)`: o que a criatura morta deixa no chao.</summary>
	public const int ZeniMinDaMorte = 100, ZeniMaxDaMorte = 4000;

	/// <summary>
	/// O PODER RECALCULADO (`upgrade_linked_mob_bp`): `(AverageBP / BPSkew) * min(max(0.10, AverageBP/owner.BP), 1) *
	/// max(1, log(2, lvl)) * (2/5)`, vezes o modificador (ver o cabecalho) -- e nunca abaixo do que ela ja tem.
	/// </summary>
	public static double Poder(double media, double bpDoDono, int nivel, double bpMod, double atual)
	{
		double parte = bpDoDono > 0 ? Math.Clamp(media / bpDoDono, 0.10, 1) : 1;
		double novo = media * parte * Math.Max(1, Math.Log2(Math.Max(nivel, 1))) * (2.0 / 5.0) * bpMod;
		return Math.Max(novo, atual);
	}

	/// <summary>
	/// OS ATRIBUTOS DA CRIATURA (`update_linked_mob`, `companions.dm:72-93`): o poder dela, a pericia de ki pela
	/// inteligencia, e as melhorias gastas -- um quarto de ponto por melhoria em cada atributo do grupo (um oitavo na
	/// pericia de ki, pelo grupo de ki).
	/// </summary>
	public static void Aplicar(Fighter f, Companheiro c)
	{
		Especie? especie = DaEspecie(c.Especie);
		f.BP = Math.Max(c.Bp, 1);
		f.BPMod = 1;
		f.physoff = f.physdef = 1 + c.Fisico / 4.0;
		f.kioff = f.kidef = 1 + c.Ki / 4.0;
		f.kiskill = 1 + c.Inteligencia + c.Ki / 8.0;
		f.technique = f.speed = f.magiskill = 1 + c.Misc / 4.0;
		f.KiMod = especie?.KiMod ?? 1;
		f.baseKi = especie?.Ki ?? 5;
	}

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X6): nao ha teto de criaturas soltas por dono.</summary>
	public static bool SemTetoDeSoltosDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X6): a criatura nao obedece a ordem de atacar -- fica onde esta.</summary>
	public static bool NaoObedeceDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X6): o interceptador nao derruba disparo nenhum.</summary>
	public static bool InterceptadorParadoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X6): a racao volta a comparar o dono com ela mesma, e nao faz nada.</summary>
	public static bool RacaoDoOriginalDeTeste;
}

/// <summary>
/// A CRIATURA DE UMA CAPSULA, como ela mora no cofre do servidor (`tech-itens.json`): o `linked_mob_*` do
/// `companion_obj`. O id da capsula leva so a gaveta.
/// </summary>
public sealed class Companheiro
{
	public string Especie = "";
	public string Nome = "";
	public string Raca = "";
	public string Genero = "Male";

	/// <summary>A cara da criatura capturada (`linked_mob_icon`). Nula nas de fabrica: a raca delas ja diz o corpo.</summary>
	public Appearance.Appearance? Visual;

	public double Bp = 1;
	public double BpMod = 1;
	public int Inteligencia = 1;
	public int Nivel = 1;
	public double Exp;

	/// <summary>`leveledup`: as melhorias ganhas e ainda nao gastas.</summary>
	public int Sobras;
	public int Fisico, Ki, Misc;

	public double Relacao;
	public bool QuandoAtaco, QuandoApanho;
}
