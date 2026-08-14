using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Core.Skills;

/// <summary>Como a tecnica se comporta no tempo -- e o que decide como o botao se pinta.</summary>
public enum Modo
{
	/// <summary>Aperta, acontece, acabou (Solar Flare, Regenerar).</summary>
	Instantanea,

	/// <summary>Liga e desliga, drenando enquanto durar (Invisibility, Ki Shield).</summary>
	Sustentada,

	/// <summary>Existe no catalogo do DM mas o efeito ainda nao foi portado.</summary>
	NaoPortada,
}

/// <summary>
/// AS TECNICAS ATIVAS -- a metade das skills que nao e um numero somado.
///
/// As 85 skills que so somam num campo saem prontas do extrator (<see cref="EfeitosDeSkill"/>).
/// As 48 que destravam um `assignverb` NAO saem: o corpo do verb e codigo de verdade, com custo,
/// recarga, alcance e efeito. Essas sao portadas a mao, uma a uma -- e esta tabela e a lista de
/// quais ja foram.
///
/// A TABELA INCLUI AS NAO-PORTADAS DE PROPOSITO. E tentador listar so o que funciona, mas ai o
/// jogo fica sem como responder "essa tecnica existe e ainda nao faz nada" -- e o jogador que
/// gastou marcos numa delas merece essa frase em vez de um botao mudo. Tambem e a unica medida
/// honesta de quanto falta: <see cref="Portadas"/> contra <see cref="Total"/>.
///
/// OS NUMEROS SAO OS DO DM, sem arredondar pra "numero bonito". `100*BaseDrain` de Ki e
/// `max(20*Eactspeed, 200)` decimos de segundo de recarga sao o que o Solar Flare custa la.
/// </summary>
public static partial class Tecnicas
{
	public sealed class Tecnica
	{
		public string Id = "";        // o nome do verb no DM: "Solar_Flare"
		public string Nome = "";      // como aparece pro jogador
		public Modo Modo = Modo.NaoPortada;
		public string Desc = "";
		public string Aba = "Skills";
	}

	private static readonly Dictionary<string, Tecnica> Tudo = new(StringComparer.OrdinalIgnoreCase);

	public static int Total => Tudo.Count;
	public static int Portadas => Tudo.Values.Count(t => t.Modo != Modo.NaoPortada);
	/// <summary>
	/// A TECNICA, PORTADA OU NAO. Um id desconhecido NAO devolve nulo -- devolve uma entrada
	/// sintetica marcada como nao-portada.
	///
	/// A primeira versao disto era uma lista de quarenta nomes escrita a mao, e ela ficou
	/// obsoleta no dia em que o extrator melhorou: as skills passaram a conceder 115 verbs em
	/// vez de 48, e os 75 novos nao tinham entrada -- devolviam nulo, e nulo virava botao
	/// nenhum. O jogador comprava a skill, lia "nova habilidade: X" no chat e nao achava X em
	/// lugar algum. Uma lista fechada num sistema que cresce e um bug esperando o proximo passo.
	/// </summary>
	public static Tecnica? Get(string id)
	{
		if (id.Length == 0) return null;
		if (Tudo.TryGetValue(id, out Tecnica? t)) return t;
		var nova = new Tecnica
		{
			Id = id,
			Nome = NomesLegiveis.Habilidade(id),
			Modo = Modo.NaoPortada,
			Desc = "Voce sabe a tecnica, mas o efeito dela ainda nao foi trazido do jogo antigo.",
		};
		Tudo[id] = nova;
		return nova;
	}
	public static IEnumerable<Tecnica> Todas => Tudo.Values;

	/// <summary>
	/// OS IDS QUE ESTA TECNICA CONHECE COM EFEITO -- o numerador do relatorio do catalogo.
	///
	/// Nao e o mesmo que <see cref="Portadas"/> (que e a contagem): esta lista existe pra ser
	/// COMPARADA com o espelho do Core, e a comparacao e a unica coisa que impede o console de
	/// subcontar o proprio progresso. Ver <see cref="DesencontroDoEspelho"/>.
	/// </summary>
	public static IEnumerable<string> Vivas =>
		Tudo.Values.Where(t => t.Modo != Modo.NaoPortada).Select(t => t.Id);

	// =====================================================================
	// O ESPELHO DO CORE -- e a conta que ele existe pra fechar
	// =====================================================================
	/// <summary>
	/// OS IDS QUE O ESPELHO DO CORE (<c>Tecnicas.Portadas.cs</c>) DECLARA.
	///
	/// ============================ POR QUE ISTO PRECISOU VIRAR DADO ============================
	/// O efeito de cada tecnica e codigo de servidor e mora nos `GameServer.Tecnicas.G*.cs`; o
	/// DESCRITOR (id, nome, modo, texto) e dado puro e mora aqui, porque o console do extrator
	/// (`dotnet run -- efeitos`) nao carrega Godot e mesmo assim precisa contar o que ja foi
	/// portado. Sao, portanto, DUAS BOCAS pro mesmo fato -- e o cabecalho de `Tecnicas.Portadas.cs`
	/// ja avisava, por escrito, o que acontece quando elas divergem: *"um relatorio que subconta o
	/// proprio progresso e tao ruim quanto um que superconta"*.
	///
	/// E foi exatamente o que aconteceu de novo: o lote G7 registrou dezesseis verbos no servidor e
	/// **nenhuma linha foi acrescentada ao espelho**. Durante uma sessao inteira o console respondeu
	/// "52 com efeito portado" enquanto o jogo tinha 68. Ninguem viu, porque um numero baixo demais
	/// nao quebra nada -- ele so faz a divida parecer maior do que e, que e o jeito mais silencioso
	/// de um relatorio mentir.
	///
	/// Enquanto o registro era so uma chamada, a divergencia era invisivel: as duas bocas escreviam
	/// no MESMO dicionario e o segundo registro cobria o primeiro. Guardando QUEM veio do espelho, a
	/// pergunta "as duas contam a mesma coisa?" passa a ter resposta em codigo -- e a bancada
	/// `--catalogoteste` a faz toda rodada.
	/// ======================================================================================
	/// </summary>
	public static IEnumerable<string> NoEspelho => Espelho;

	private static readonly HashSet<string> Espelho = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// AS DUAS BOCAS DIVERGIRAM? Funcao PURA -- ela recebe os dois conjuntos em vez de le-los, e e
	/// por isso que a bancada consegue INJETAR o defeito (chamar com um par sintetico) em vez de
	/// so afirmar sobre o par de verdade. Uma checagem que so sabe olhar pro estado real nunca
	/// prova que ela reprovaria.
	///
	/// As duas direcoes sao defeitos DIFERENTES, e por isso saem separadas:
	///   * <c>SoNoJogo</c>  -- o servidor porta e o espelho nao sabe: **o console subconta**;
	///   * <c>SoNoEspelho</c> -- o espelho promete um efeito que o servidor nao registra mais: o
	///     jogador ve o botao, aperta, e ouve *"ainda nao tem efeito"* sobre uma tecnica que o
	///     catalogo jura estar pronta.
	/// </summary>
	public static (List<string> SoNoJogo, List<string> SoNoEspelho) DesencontroDoEspelho(
		IEnumerable<string> vivas, IEnumerable<string> espelho)
	{
		var e = new HashSet<string>(espelho, StringComparer.OrdinalIgnoreCase);
		var v = new HashSet<string>(vivas, StringComparer.OrdinalIgnoreCase);
		return ([.. v.Where(x => !e.Contains(x)).Order(StringComparer.OrdinalIgnoreCase)],
				[.. e.Where(x => !v.Contains(x)).Order(StringComparer.OrdinalIgnoreCase)]);
	}

	/// <summary>
	/// O MARCO DE ONDE ESTE CATALOGO SAIU: **34 verbos com corpo**, medidos no censo que abriu esta
	/// frente (165 folhas vivas concediam 94 verbos e 34 tinham efeito; 60 botoes prometiam e nao
	/// entregavam).
	///
	/// E um numero HISTORICO e por isso pode ser uma constante: ele nao mede o hoje, ele marca o
	/// ontem. A bancada imprime `34 -> N` toda rodada e exige que N nunca desca -- que e a unica
	/// forma de uma contagem provar progresso em vez de so descreve-lo.
	/// </summary>
	public const int MarcoDeVerbosComCorpo = 34;

	static Tecnicas()
	{
		// AS DUAS PORTAS DESTA CASA SAO ESPELHO. O que for registrado depois -- os lotes do
		// servidor -- e "vivo mas fora do espelho" ate alguem trazer a linha pra ca.
		_montandoEspelho = true;

		// as portadas: tabela gerada, no Core, pra que a bancada tambem as enxergue
		RegistrarPortadas();

		Por("Solar_Flare", "Solar Flare", Modo.Instantanea,
			"Uma explosao de luz que CEGA quem estiver olhando pra voce. Quem estava de costas nao "
			+ "se importa -- e por isso que a tecnica se usa de frente, e por isso que fugir dela "
			+ "e virar o rosto. Custa Ki e demora a voltar.");

		Por("Invisibility", "Invisibility", Modo.Sustentada,
			"Some da vista. Drena Ki o tempo todo e cai sozinha quando o Ki acaba -- ficar invisivel "
			+ "e caro, e e caro justamente pra nao ser o estado padrao de ninguem.");

		Por("Ki_Shield", "Ki Shield", Modo.Sustentada,
			"Uma casca de Ki que soma defesa fisica E de Ki enquanto estiver de pe. Cai no nocaute "
			+ "e ao meditar.");

		Por("Regenerate", "Regenerar", Modo.Instantanea,
			"Faz um membro decepado voltar a crescer, ou cura por inteiro o mais ferido.");

		Por("Space_Flight", "Decolar", Modo.Instantanea,
			"Deixa a superficie e sobe ao espaco.", aba: "Outros");

		// AS NAO-PORTADAS NAO SAO LISTADAS AQUI. Antes havia uma lista de quarenta nomes; ela
		// virou divida no dia em que o extrator achou setenta e cinco verbs a mais. Quem nao
		// esta nesta tabela e sintetizado por `Get()` como nao-portado, com o mesmo texto.

		_montandoEspelho = false;
	}

	/// <summary>Verdadeiro enquanto o construtor estatico monta o espelho. Ver <see cref="NoEspelho"/>.</summary>
	private static bool _montandoEspelho;

	/// <summary>
	/// REGISTRA uma tecnica portada. Publico pra que cada lote de tecnicas viva no proprio
	/// arquivo e se anuncie sozinho -- e o que permite portar as proximas sem tocar nesta lista.
	/// </summary>
	public static void Registrar(string id, string nome, Modo modo, string desc, string aba = "Skills")
		=> Por(id, nome, modo, desc, aba);

	private static void Por(string id, string nome, Modo modo, string desc, string aba = "Skills")
	{
		Tudo[id] = new Tecnica { Id = id, Nome = nome, Modo = modo, Desc = desc, Aba = aba };
		if (_montandoEspelho && modo != Modo.NaoPortada) Espelho.Add(id);
	}

	// =====================================================================
	// AS FORMULAS -- copiadas do corpo dos verbs, nao inventadas
	// =====================================================================

	/// <summary>
	/// SOLAR FLARE. Custo `100*BaseDrain` (`misc.dm`), recarga `max(20*Eactspeed, 200)` decimos.
	/// </summary>
	public static double SolarCustoKi(Fighter f) => 100 * f.BaseDrain();

	public static long SolarRecargaMs(Fighter f) => (long)(Math.Max(20 * f.Eactspeed, 200) * 100);

	/// <summary>
	/// O ALCANCE em TILES: `round(kidebuffskill/10 + max(Ekiskill/2, 1), 1)`, minimo 2. Cresce com
	/// treino de Ki -- um novato cega quem esta colado, um veterano cega a sala.
	/// </summary>
	public static double SolarAlcanceTiles(Fighter f)
		=> Math.Max(DmMath.Round(f.kidebuffskill / 10 + Math.Max(f.Ekiskill / 2, 1), 1), 2);

	/// <summary>
	/// A DURACAO da cegueira: `max(round(0.4*kidebuffskill*max(Ekiskill/2,1), 1), 10)` ticks do
	/// BYOND (decimos de segundo). O piso de 10 e o que garante que a tecnica sempre serve pra
	/// alguma coisa, mesmo recem-comprada.
	/// </summary>
	public static long SolarCegueiraMs(Fighter f)
		=> (long)(Math.Max(DmMath.Round(0.4 * f.kidebuffskill * Math.Max(f.Ekiskill / 2, 1), 1), 10) * 100);

	/// <summary>
	/// A VITIMA ESTAVA DE FRENTE PRO CLARAO?
	///
	/// O original testa `A.dir == get_dir(A,usr)` mais as duas diagonais vizinhas -- num mundo de
	/// 8 direcoes isso e um arco de 135 graus. Aqui o movimento e livre, entao vira angulo.
	///
	/// ISTO E O CORACAO DA TECNICA e nao da pra afrouxar pra "todo mundo por perto": se cegar de
	/// costas tambem, some a unica defesa que existe contra ela e nao ha mais motivo pra ninguem
	/// desviar o olhar. O jogador aprende a virar o rosto porque virar o rosto FUNCIONA.
	///
	/// Mora no Core, e nao no servidor, pra ser TESTAVEL sem subir partida -- e geometria pura, o
	/// tipo de coisa que quebra calada quando alguem mexe no enum de direcao.
	/// </summary>
	public const double ArcoDoClarao = 135;

	public static bool EstaOlhandoPra(Vec2 dePos, Facing deOlhar, Vec2 alvoPos)
	{
		Vec2 d = alvoPos - dePos;
		if (d.X == 0 && d.Y == 0) return true;
		Vec2 olhar = deOlhar switch
		{
			Facing.North => new Vec2(0, -1),
			Facing.South => new Vec2(0, 1),
			Facing.West => new Vec2(-1, 0),
			_ => new Vec2(1, 0),
		};
		double n = Math.Sqrt(d.X * d.X + d.Y * d.Y);
		double cos = (d.X * olhar.X + d.Y * olhar.Y) / n;
		return cos >= Math.Cos(Math.PI * ArcoDoClarao / 360);   // metade do arco, em radianos
	}

	/// <summary>INVISIBILITY: `Ki -= 1*BaseDrain` por segundo, e cai quando o Ki fica abaixo de 10.</summary>
	public static double InvisDrenoPorSegundo(Fighter f) => 1 * f.BaseDrain();
	public const double InvisKiMinimo = 10;

	/// <summary>KI SHIELD: custo `MaxKi/10` e bonus `max(log_8.3(Ekioff*10), 0)` em Tphysdef E Tkidef.</summary>
	public static double EscudoCustoKi(Fighter f) => f.MaxKi / 10;

	public static double EscudoBonus(Fighter f)
		=> Math.Max(Math.Log(Math.Max(f.Ekioff * 10, 1)) / Math.Log(8.3), 0);
}
