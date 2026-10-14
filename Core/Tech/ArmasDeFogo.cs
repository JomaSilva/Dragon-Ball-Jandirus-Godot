using Jandirus.Core.Items;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Tech;

/// <summary>
/// AS ARMAS DE FOGO -- `obj/items/Guns` (`Code/Modules/Tech/Guns.dm`), o Bolter (`Turfs/Building/buildable.dm:177-224`)
/// e a caixa de municao (`obj/items/Ammo`).
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// Toda arma cria um `obj/attack/blast` -- o MESMO objeto de uma bola de ki -- e escreve nele quatro coisas:
///
///     A.mods  = power           // o numerador da conta de dano, no lugar do `Ekioff*Ekiskill` de quem atira
///     A.BP    = powerlevel      // o poder do TIRO, fixo na arma -- e nao o de quem a segura
///     A.physdamage = 1          // (so as de polvora) a cadeia fisica: a defesa que conta e a fisica
///     if(prob(critical)) A.BP *= 10
///
/// e o solta com `walk(A, usr.dir)` (um tile por tique). E por isso que elas sao *"for those who are Ki deficient"*
/// (`Guns.dm:14`): o tiro vale o que a ARMA vale, seja quem for o dono. A melhoria "Force" leva o `powerlevel` ao
/// teto de tecnologia de quem a paga (`powerlevel = usr.intBPcap`, `:216`).
///
/// Tres fontes de tiro: POLVORA (um pente que so a caixa de municao enche -- `Reload()`, `:143-155`), ENERGIA (uma
/// reserva que se paga pra encher e pra dobrar -- o Blaster e o Bolter) e KI (o Gaster Blaster cobra
/// `10 * BaseDrain` de quem atira, `:396-398`).
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * O ESTADO VAI NO ID da unidade (`Handgun|28|30||1|`): carga, pente, critico e as duas melhorias de efeito.
///     Ver `TechDeBolso`. A FORCA REFORCADA NAO: ela e o teto de tecnologia de quem pagou -- um numero na escala
///     de poder, funcao do poder dele --, e a regra do dono e que ninguem le o proprio poder sem medidor, nem em
///     pacote. Ela mora no cofre do servidor, e o id leva so a gaveta (`Handgun|28|30|c12|1|`; ver
///     `TechDeBolso.Gaveta`). A de fabrica e constante da tabela, e nao viaja.
///   * A "FORCE" COBRA OS 10.000 ZENI DO ROTULO. No original a opcao so aparece pra quem tem 10.000
///     (`if(usr.zenni>=10000) Choices.Add("Force (10000z)")`) e depois cobra `10*power` -- dez a quarenta zeni.
///   * A RECARGA DE ENERGIA COBRA O PRECO DO ROTULO (`5*maxreserve`); la o rotulo diz cinco e a conta cobra dez.
///   * A "FALHA" NAO EXISTE: o `Info()` anuncia *"Failure Chance: 20%"* (`:104`) e nenhuma linha do tiro a sorteia.
///   * A COR DO TIRO DE ENERGIA e a do ki de quem atira. La o Blaster sorteia uma cor ao ser fabricado
///     (`BlasterR/G/B`); aqui todo tiro leva a cor do dono, e a regra e uma so (ver `ReceitaDeProjetil.Nome`).
///   * AS MELHORIAS ACEITAM RECURSOS, pelo dobro (dono, 2026-10-10 e 2026-10-13).
///   * O TIRO SAI NO ALVO MARCADO, como todo tiro deste port (dono, 2026-10-09); la sai pra onde o corpo olha.
/// ==================================================================================
/// </summary>
public static class ArmasDeFogo
{
	public const string Municao = "Ammo";

	public const string Pistola = "Handgun", Metralhadora = "SMG", Rifle = "Rifle", LancaFoguetes = "Rocket_Launcher",
						Escopeta = "Shotgun", Blaster = "Blaster", Bolter = "Bolter", Gaster = "G_Blaster";

	// ---- as acoes da mochila ----
	public const string AcaoAtirar = "atirar", AcaoRecarregar = "recarregar", AcaoInfo = "arma_info",
						AcaoForca = "arma_forca", AcaoCritico = "arma_critico", AcaoEmpurrao = "arma_empurrao",
						AcaoAtordoar = "arma_atordoar", AcaoReserva = "arma_reserva", AcaoEncher = "arma_encher",
						AcaoAparafusar = "arma_aparafusar";

	/// <summary>O verb de tecla: atira com a primeira arma da mochila que tem com o que atirar.</summary>
	public const string VerboAtirar = "arma_atirar";

	/// <summary>
	/// O PREFIXO COM QUE O TIRO DE UMA ARMA PERGUNTA PELA ARTE E PELO SOM DELE (`ArteDeProjetil`, `SonsDeKi`): as duas
	/// tabelas sao indexadas pelo verb de quem atira, e o id de uma arma nao pode coincidir com o de uma tecnica.
	/// </summary>
	public const string PrefixoDeVerbo = "arma:";

	/// <summary>De onde sai o tiro.</summary>
	public enum Fonte : byte { Polvora, Energia, Ki }

	/// <summary>
	/// Uma arma como o original a declara: o pente (ou a reserva) de fabrica, o `power`, o `powerlevel`, o tempo entre
	/// dois tiros e quantos projeteis saem por aperto.
	/// </summary>
	public sealed record Modelo(string Id, Fonte Fonte, int Pente, double Power, double Forca, double SegundosEntreTiros, int Balas);

	private static readonly Dictionary<string, Modelo> Modelos = new(StringComparer.Ordinal)
	{
		// `ammo=30 power=1 powerlevel=2 refire=10` (`Guns.dm:236-249`); `spawn(refire) blasting=0` e em decimos.
		[Pistola] = new(Pistola, Fonte.Polvora, 30, 1, 2, 1.0, 1),
		// `ammo=200 power=1 powerlevel=3 refire=5` (`:250-260`).
		[Metralhadora] = new(Metralhadora, Fonte.Polvora, 200, 1, 3, 0.5, 1),
		// `ammo=6 power=2 powerlevel=5 refire=25`, tres balas por aperto (`:261-325`).
		[Escopeta] = new(Escopeta, Fonte.Polvora, 6, 2, 5, 2.5, 3),
		// `ammo=20 power=1 powerlevel=5 refire=20` (`:326-335`).
		[Rifle] = new(Rifle, Fonte.Polvora, 20, 1, 5, 2.0, 1),
		// `ammo=5 power=4 powerlevel=50 refire=50` (`:337-347`).
		[LancaFoguetes] = new(LancaFoguetes, Fonte.Polvora, 5, 4, 50, 5.0, 1),
		// `reserve=10 power=2 powerlevel=100`, `spawn(100/refire)` com o `refire=5` da base (`:348-385`): dois segundos.
		[Blaster] = new(Blaster, Fonte.Energia, 10, 2, 100, 2.0, 1),
		// `power=1 powerlevel=100`, `spawn(150/refire)` (`buildable.dm:186-208`): tres segundos.
		[Bolter] = new(Bolter, Fonte.Energia, 10, 1, 100, 3.0, 1),
		// `power=2.5 powerlevel=10000`, `spawn(100/refire)` (`Guns.dm:386-416`): dois segundos. Nao tem carga: cobra Ki.
		[Gaster] = new(Gaster, Fonte.Ki, 0, 2.5, 10000, 2.0, 1),
	};

	/// <summary>O modelo desta arma, ou nulo quando o id (so a parte antes do estado) nao e de uma.</summary>
	public static Modelo? De(string @base) => Modelos.GetValueOrDefault(@base);

	/// <summary>O id (com ou sem estado) e de uma arma de fogo?</summary>
	public static bool EhArma(string id) => Modelos.ContainsKey(TechDeBolso.Base(id));

	public static IEnumerable<string> Ids => Modelos.Keys;

	// =====================================================================
	// O ESTADO DE UMA UNIDADE
	// =====================================================================
	/// <summary>
	/// O que muda de uma arma pra outra do mesmo modelo: quantos tiros ha, quantos cabem, o `critical` (em porcento),
	/// as duas melhorias de efeito (`knockback` e `stun`) e a gaveta do cofre em que mora o `powerlevel` REFORCADO
	/// (zero = o de fabrica, que e o da tabela). Quem resolve a gaveta e o servidor (`GameServer.ForcaDaArma`).
	/// </summary>
	public readonly record struct Estado(int Carga, int Pente, int Critico, bool Empurra, bool Atordoa, int Cofre = 0);

	/// <summary>`var/critical=1` (`Guns.dm:80`): toda arma nasce com um porcento.</summary>
	public const int CriticoDeFabrica = 1;

	/// <summary>O estado escrito no id; a arma recem-fabricada (o id cru) esta cheia e de fabrica.</summary>
	public static Estado Ler(string id)
	{
		Modelo? m = De(TechDeBolso.Base(id));
		if (m == null) return default;
		string[] c = TechDeBolso.Campos(id);
		if (c.Length < 5) return new Estado(m.Pente, m.Pente, CriticoDeFabrica, false, false);

		int pente = Math.Max(Inteiro(c[1], m.Pente), 0);
		return new Estado(
			Math.Clamp(Inteiro(c[0], pente), 0, pente), pente,
			Math.Max(Inteiro(c[3], CriticoDeFabrica), 0),
			c[4].Contains('k'), c[4].Contains('s'), TechDeBolso.Gaveta(c[2]));
	}

	/// <summary>O id desta arma com este estado.</summary>
	public static string Escrever(string @base, Estado e) => TechDeBolso.Com(@base,
		e.Carga.ToString(System.Globalization.CultureInfo.InvariantCulture),
		e.Pente.ToString(System.Globalization.CultureInfo.InvariantCulture),
		TechDeBolso.EscreverGaveta(e.Cofre),
		e.Critico.ToString(System.Globalization.CultureInfo.InvariantCulture),
		(e.Empurra ? "k" : "") + (e.Atordoa ? "s" : ""));

	private static int Inteiro(string s, int padrao) =>
		int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : padrao;

	// =====================================================================
	// OS PRECOS -- o `Upgrade()` (`Guns.dm:173-235`)
	// =====================================================================
	/// <summary>"Force (10000z)": o `powerlevel` sobe pro teto de tecnologia de quem paga.</summary>
	public const double CustoDaForca = 10_000;

	/// <summary>"Knockback effect (2000z)" e "Stun effect (3000z)".</summary>
	public const double CustoDoEmpurrao = 2_000, CustoDoAtordoar = 3_000;

	/// <summary>"Critical Chance ([500*critical]z)".</summary>
	public static double CustoDoCritico(int critico) => 500.0 * Math.Max(critico, 1);

	/// <summary>`critical &lt; 20*usr.techmod`: ate onde o critico de uma arma sobe na mao de quem a melhora.</summary>
	public static double TetoDoCritico(double techmod) => 20 * Math.Max(techmod, 0);

	/// <summary>"Energy Reserve ([10*maxreserve]z)": a reserva dobra e enche.</summary>
	public static double CustoDaReserva(int pente) => 10.0 * Math.Max(pente, 1);

	/// <summary>"Energy Refill ([5*maxreserve]z)": a reserva enche.</summary>
	public static double CustoDeEncher(int pente) => 5.0 * Math.Max(pente, 1);

	/// <summary>O teto da reserva de energia: o id tem de continuar cabendo no pacote, e 10 dobrado quinze vezes ja passa de 300 mil tiros.</summary>
	public const int TetoDaReserva = 327_680;

	/// <summary>`if(prob(critical)) A.BP*=10`.</summary>
	public const double MultDoCritico = 10;

	/// <summary>`usr.Ki -= 10 * usr.BaseDrain` -- o que o Gaster Blaster cobra por tiro, em multiplos do `BaseDrain`.</summary>
	public const double KiDoGasterPorTiro = 10;

	/// <summary>`maxdistance=30` (`objects.dm:17`): o alcance de fabrica de todo `obj/attack`, que nenhuma arma muda.</summary>
	public const double AlcanceEmTiles = 30;

	/// <summary>
	/// A VELOCIDADE: `walk(A, dir)` sem atraso e um tile por tique -- o mais rapido que um objeto anda no original.
	/// Aqui e o degrau mais alto da escala de velocidade das bolas (`ReceitaDeProjetil.Velocidade`, 1 a 5).
	/// </summary>
	public const double Velocidade = 5;

	/// <summary>
	/// O LEQUE DA ESCOPETA: a bala do meio sai reta e as outras duas dao um passo na diagonal antes de seguir o
	/// rumo (`step(B, turn(usr.dir, 45))` e `walk(B, usr.dir)`, `Guns.dm:304-320`) -- tres balas paralelas, um
	/// tile de cada lado. Devolve o deslocamento lateral de cada uma, em tiles.
	/// </summary>
	public static readonly double[] LequeDaEscopeta = [0, 1, -1];

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X2): a arma atira sem gastar o pente -- municao infinita.</summary>
	public static bool PenteInfinitoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X2): o tiro sai com o poder de quem atira, e nao com o da arma -- vira uma bola de ki comum.</summary>
	public static bool ForcaDeQuemAtiraDeTeste;
}

/// <summary>
/// O ATORDOADOR -- `obj/items/Stungun` (`Tier2.dm:298-357`): UM tiro, que paralisa e pode derrubar na hora.
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// O tiro e um blast com `paralysis=2` e `A.BP = strength` (100 de fabrica; 1% de chance de sair dez vezes mais
/// forte). No impacto (`objects.dm:347-354`) ele paralisa como a tecnica Paralysis e, se o alvo nao esta caido nem
/// de campo de forca ligado, DERRUBA quando
///
///     M.HP &lt;= 15 / (BP / (M.Ekidef * max(M.expressedBP,1) * max(M.Ekiskill,M.Etechnique) * 10))
///     || (M.expressedBP &lt; 100 &amp;&amp; M.Ekidef &lt; 3)
///
/// O `Click()` leva o `strength` ao teto de tecnologia de quem paga `100*log(intBPcap)`, e o `Reload()` cobra o
/// mesmo preco.
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * A FRACAO DA PRIMEIRA LINHA ESTA INVERTIDA NO ORIGINAL: do jeito que esta escrita, quanto MAIS forte o alvo,
///     mais facil ele cai (um alvo de 1.000 de poder cairia com a vida cheia). O comentario do proprio item diz o
///     contrario -- *"raw stats are the counter to this [...] over 100 BP the HP threshold is a whopping 15"*. O que
///     se porta e a intencao: o limiar de vida e 15 quando a forca do tiro alcanca a defesa do alvo, e desce na
///     proporcao quando nao alcanca (<see cref="LimiarDeVida"/>).
///   * O `Reload()` DO ORIGINAL COBRA E NAO RECARREGA (nunca zera o `used`): depois do primeiro tiro a arma morre.
///     Aqui recarregar devolve o tiro.
/// ==================================================================================
/// </summary>
public static class Atordoador
{
	public const string Id = "Stungun";

	/// <summary>`var/strength = 100`.</summary>
	public const double ForcaDeFabrica = 100;

	/// <summary>`spawn(100) usr.blasting=0`: dez segundos sem poder atirar de novo.</summary>
	public const double SegundosEntreTiros = 10;

	/// <summary>`if(prob(1)) A.BP*=10`.</summary>
	public const double ChanceDeCritico = 1;

	/// <summary>`15`: a vida (em porcento) abaixo da qual o tiro derruba quem ele alcanca.</summary>
	public const double VidaQueDerruba = 15;

	/// <summary>`M.expressedBP&lt;100 &amp;&amp; M.Ekidef&lt;3`: quem e fraco assim cai com a vida que tiver.</summary>
	public const double PoderDeQuemCaiSempre = 100, DefesaDeQuemCaiSempre = 3;

	/// <summary>`100*log(usr.intBPcap)` -- o preco de recarregar e o de reforcar (o `log` do BYOND e natural).</summary>
	public static double Custo(double tetoDeTecnologia) => 100 * Math.Log(Math.Max(tetoDeTecnologia, Math.E));

	/// <summary>O tiro esta no cano? (`used==0`.)</summary>
	public static bool Carregado(string id) => TechDeBolso.Campo(id, 0) != "0";

	/// <summary>
	/// A gaveta do cofre em que mora a forca REFORCADA desta unidade (zero = a de fabrica). O numero nao viaja no id:
	/// ver `TechDeBolso.Gaveta`. Quem a resolve e o servidor (`GameServer.ForcaDoAtordoador`).
	/// </summary>
	public static int Cofre(string id) => TechDeBolso.Gaveta(TechDeBolso.Campo(id, 1));

	public static string Escrever(bool carregado, int cofre) =>
		TechDeBolso.Com(Id, carregado ? "1" : "0", TechDeBolso.EscreverGaveta(cofre));

	/// <summary>
	/// Abaixo de quanta vida este alvo cai pra um tiro desta forca: <see cref="VidaQueDerruba"/> quando a forca
	/// alcanca a defesa dele (`Ekidef * poder * max(Ekiskill, Etechnique)`), e proporcionalmente menos quando nao.
	/// </summary>
	public static double LimiarDeVida(double forcaDoTiro, Fighter alvo)
	{
		double defesa = alvo.Ekidef * Math.Max(alvo.expressedBP, 1) * Math.Max(alvo.Ekiskill, alvo.Etechnique);
		return VidaQueDerruba * Math.Min(1, forcaDoTiro / Math.Max(defesa, 1e-9));
	}

	/// <summary>Este tiro derruba este alvo agora? (Quem chama ja conferiu que ele esta de pe e sem campo de forca.)</summary>
	public static bool Derruba(double forcaDoTiro, Fighter alvo) =>
		!NuncaDerrubaDeTeste
		&& (alvo.HP <= LimiarDeVida(forcaDoTiro, alvo)
			|| (alvo.expressedBP < PoderDeQuemCaiSempre && alvo.Ekidef < DefesaDeQuemCaiSempre));

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X2): o tiro so paralisa -- vira a tecnica Paralysis, e a arma perde a razao de existir.</summary>
	public static bool NuncaDerrubaDeTeste;
}
