namespace Jandirus.Core.Forms;

/// <summary>
/// Um degrau da escada de transformacao.
///
/// O ID e o MESMO numero do `mob/var/ssj` do BYOND (0 base, 1 SSJ1, 1.5 grade, 2 SSJ2, 3 SSJ3,
/// 4 SSJ4...), multiplicado por 10 pra caber num inteiro. Manter a numeracao deixa a conferencia
/// com `supersaiyanbuff.dm` direta: quem for checar o dreno do SSJ3 abre o `if(3)` de la e o
/// `Ssj3` daqui.
/// </summary>
public enum Forma
{
	Base = 0,
	Ssj1 = 10,
	Grade2 = 15,   // o antigo USSJ: nao se compra, abre com maestria no SSJ1
	Grade3 = 16,
	Ssj2 = 20,
	Ssj3 = 30,
	Ssj4 = 40,
}

/// <summary>Tudo que o jogo precisa saber de uma forma pra mostra-la e cobra-la.</summary>
public sealed class FormaDef
{
	public Forma Id;
	public string Nome = "";
	public string Desc = "";

	/// <summary>
	/// BP BASE necessario -- O VALOR DE FABRICA. Ver <see cref="EscadaSaiyajin"/> sobre por que
	/// e o base e nao o expresso.
	///
	/// NAO E O NUMERO QUE GATEIA UM PERSONAGEM. Cada personagem sorteia o proprio limiar ao
	/// nascer (`statsaiyan.dm:45-77`): este campo e o `initial()` de onde aquele sorteio parte, e
	/// o valor que vale pra quem ainda nao rolou. Quem tem o numero de verdade e
	/// <see cref="LimiaresPessoais.Porta"/>.
	/// </summary>
	public double PortaBp;

	/// <summary>De onde se sobe pra ca. Base = a porta de entrada da escada.</summary>
	public Forma Vem = Forma.Base;

	/// <summary>Cor da aura e do cabelo. E o que o cliente acende e pinta.</summary>
	public string Aura = "ffd24a";
	public string Cabelo = "ffd24a";

	/// <summary>Maestria do PASSO ANTERIOR necessaria (grades). 0 = nao pede.</summary>
	public double PedeMaestria;
}

/// <summary>
/// A ESCADA SAIYAJIN, portada de `Code/Modules/Skills/Buffs/racial/supersaiyanbuff.dm`.
///
/// TRES REGRAS QUE PARECEM DETALHE E SAO O SISTEMA INTEIRO:
///
/// 1. **A PORTA E O BP BASE, NUNCA O EXPRESSO.** Foi decisao explicita no original (2026-06-28):
///    gatear pelo BP expresso deixaria uma forma, uma raiva ou um zenkai "fingirem" o requisito
///    da forma seguinte -- bastaria virar SSJ1 pra o SSJ2 abrir sozinho. Por isso os limiares
///    daqui ja vem DIVIDIDOS pelo multiplicador cheio da forma anterior, exatamente como la.
///
/// 2. **MAESTRIA EM DEGRAUS, nao rampa.** `stepped_mastery_mult` (supersaiyanbuff.dm:591): com
///    n degraus, o degrau k liga em `k/n*100%` de maestria, e o ULTIMO so aos 100%. SSJ1 e
///    `[2, 6]`: 2x ate 99%, 6x no 100%. Nao ha meio-termo -- dominar a forma e um ACONTECIMENTO,
///    nao um deslizar de barra.
///
/// 3. **PISO DE (ANTERIOR + 2).** Uma forma nunca vale menos que a anterior mais 2x, ate superar
///    naturalmente. Sem isso, um SSJ3 de raca nerfada (mult 2) ficaria MAIS FRACO que o proprio
///    SSJ2 dele, e subir a escada puniria.
///
/// 4. **AS PORTAS DAQUI NAO SAO AS PORTAS DE NINGUEM.** Os `Porta*` sao o `initial()` do BYOND:
///    cada personagem sorteia o proprio limiar ao nascer (`statsaiyan.dm:45-77`) e guarda em
///    <see cref="LimiaresPessoais"/>. Comparar BP com a constante e comparar com a media --
///    dois Elite da mesma idade tem portas diferentes de proposito.
/// </summary>
public static class EscadaSaiyajin
{
	// =====================================================================
	// OS `initial()` DA CONTA -- e NAO o limiar de ninguem
	//
	// No BYOND estes numeros sao o valor de fabrica de `mob/var/ssjat`, `ssj2at`... e o
	// `special_info()` do proto racial os REESCREVE no nascimento, multiplicando cada um por um
	// `rand()` proprio (`statsaiyan.dm:45-77`). Eles sao o ponto de partida do sorteio, nao o
	// resultado dele: o limiar que vale para uma pessoa mora em <see cref="LimiaresPessoais"/>.
	//
	// Continuam aqui, e nao la, porque sao a DEFINICAO da escada -- mexer neles reequilibra o
	// jogo inteiro; mexer nos de la seria trapacear com um personagem so.
	// =====================================================================

	/// <summary>`ssjat` -- 1,5 milhao de BP base. supersaiyanbuff.dm:6.</summary>
	public const double SsjatInicial = 1_500_000;

	/// <summary>`ssj2at` -- 3,5 bilhoes de BP EXPRESSO. supersaiyanbuff.dm:37.</summary>
	public const double Ssj2atInicial = 3.5e9;

	/// <summary>`ssj3at` -- 20 bilhoes de BP EXPRESSO. supersaiyanbuff.dm:48.</summary>
	public const double Ssj3atInicial = 2e10;

	/// <summary>`rawssj4at` -- este ja e BASE no original (rework 2026-07-10). supersaiyanbuff.dm:55.</summary>
	public const double RawSsj4atInicial = 15e9;

	/// <summary>`ultrassjat` -- 750 milhoes, o USSJ. supersaiyanbuff.dm:18.</summary>
	public const double UltrassjatInicial = 750e6;

	/// <summary>
	/// O DIVISOR QUE TRAZ O LIMIAR DO SSJ2 PRO BP BASE: `BP >= ssj2at/6`
	/// (Transformation Controls.dm:19). E o `ssj1_gate_mult()` do original com o SSJ1 cheio --
	/// dividir aqui e o que impede a propria forma anterior de "pagar" o requisito da seguinte.
	/// </summary>
	public const double Ssj1GateMult = 6;

	/// <summary>O mesmo pro SSJ3: `BP >= ssj3at/10` (Transformation Controls.dm:45).</summary>
	public const double Ssj2GateMult = 10;

	// --- o valor de fabrica ja pronto pra comparar com BP BASE -------------
	// E o que um personagem SEM sorteio usa (save antigo, NPC, catalogo de racas).

	/// <summary>Fabrica do SSJ1. Ver <see cref="LimiaresPessoais.Porta"/> pro numero pessoal.</summary>
	public const double PortaSsj1 = SsjatInicial;

	/// <summary>Fabrica do SSJ2: o limiar expresso dividido pelo SSJ1 cheio.</summary>
	public const double PortaSsj2 = Ssj2atInicial / Ssj1GateMult;

	/// <summary>Fabrica do SSJ3: o limiar expresso dividido pelo SSJ2 cheio.</summary>
	public const double PortaSsj3 = Ssj3atInicial / Ssj2GateMult;

	/// <summary>Fabrica do SSJ4 -- ja e base no original.</summary>
	public const double PortaSsj4 = RawSsj4atInicial;

	/// <summary>Maestria do SSJ1 que abre cada grade. `SSJ_GRADE2_PCT` / `SSJ_GRADE3_PCT`.</summary>
	public const double Grade2Pct = 50, Grade3Pct = 70;

	/// <summary>Fatores dos grades sobre a base do SSJ1. `SSJ_GRADE2_FACTOR` / `_GRADE3_FACTOR`.</summary>
	public const double Grade2Fator = 1.5, Grade3Fator = 2;

	/// <summary>A base do SSJ1 por raca: 2 normal, 1,35 diluido. `ssj1base`.</summary>
	public const double Ssj1Base = 2, Ssj1BaseDiluido = 1.35;

	/// <summary>Bases do SSJ2 e do SSJ3. `ssj2base` / `ssj3base` (16 = multiplicador FIXO).</summary>
	public const double Ssj2Base = 4, Ssj3Base = 16;

	public static readonly FormaDef[] Degraus =
	[
		new() { Id = Forma.Ssj1,   Nome = "Super Saiyajin",             PortaBp = PortaSsj1, Vem = Forma.Base,
				Aura = "ffd24a", Cabelo = "ffe066", Desc = "O cabelo se ergue e doura. 2x ate dominar; 6x aos 100% de maestria." },
		new() { Id = Forma.Grade2, Nome = "Super Saiyajin Grade 2",     PortaBp = PortaSsj1, Vem = Forma.Ssj1, PedeMaestria = Grade2Pct,
				Aura = "ffcf3a", Cabelo = "ffe066", Desc = "Musculatura inchada. Nao se compra: abre com 50% de maestria no SSJ1." },
		new() { Id = Forma.Grade3, Nome = "Super Saiyajin Grade 3",     PortaBp = PortaSsj1, Vem = Forma.Ssj1, PedeMaestria = Grade3Pct,
				Aura = "ffc21f", Cabelo = "ffe066", Desc = "Poder bruto no limite do corpo. Abre com 70% de maestria no SSJ1." },
		new() { Id = Forma.Ssj2,   Nome = "Super Saiyajin 2",           PortaBp = PortaSsj2, Vem = Forma.Ssj1,
				Aura = "ffe36b", Cabelo = "ffe680", Desc = "Faiscas percorrem a aura." },
		new() { Id = Forma.Ssj3,   Nome = "Super Saiyajin 3",           PortaBp = PortaSsj3, Vem = Forma.Ssj2,
				Aura = "fff08a", Cabelo = "ffee99", Desc = "Cabelo ate a cintura, sobrancelhas somem. Dreno brutal." },
		new() { Id = Forma.Ssj4,   Nome = "Super Saiyajin 4",           PortaBp = PortaSsj4, Vem = Forma.Ssj3,
				Aura = "e8484a", Cabelo = "2b2b2b", Desc = "Pelagem vermelha. Exige o Oozaru Dourado." },
	];

	public static FormaDef? Def(Forma f) => Array.Find(Degraus, d => d.Id == f);

	/// <summary>
	/// O DEGRAU POR MAESTRIA (`stepped_mastery_mult`, supersaiyanbuff.dm:591).
	///
	/// Com n degraus, o degrau k (1 = base) liga em maestria >= k/n*100. O ultimo so aos 100%.
	/// A divisao e em ponto flutuante de proposito: com 3 degraus o segundo abre em 66,67%.
	/// </summary>
	public static double PorDegrau(double maestria, params double[] degraus)
	{
		if (degraus.Length < 1) return 1;
		int idx = 0;
		for (int k = 2; k <= degraus.Length; k++)
			if (maestria >= k * 100.0 / degraus.Length) idx = k - 1;
		return degraus[idx];
	}

	/// <summary>O maior grade que esta maestria de SSJ1 destravou. 0 = nenhum.</summary>
	public static int GradeAberto(double maestriaSsj1) =>
		maestriaSsj1 >= Grade3Pct ? 3 : maestriaSsj1 >= Grade2Pct ? 2 : 0;

	public static double GradeMult(int g, double ssj1base) =>
		g == 0 ? 0 : ssj1base * (g >= 3 ? Grade3Fator : Grade2Fator);

	/// <summary>
	/// O MULTIPLICADOR EFETIVO da forma, com a maestria de cada degrau e o piso de +2.
	///
	/// E o `ssj_effective_mult()` do original, com a mesma ordem: o grade NAO tem piso (o SSJ1
	/// dominado, 6x, supera os dois de proposito), o SSJ2 tem piso sobre o maior entre SSJ1 e o
	/// grade ABERTO, e o SSJ3 tem piso sobre o SSJ2 EFETIVO -- usar o SSJ2 cru deixaria o SSJ3
	/// de raca nerfada mais fraco que o SSJ2 dela.
	/// </summary>
	public static double Multiplicador(Forma f, Maestrias m, bool diluido = false)
	{
		double b1 = diluido ? Ssj1BaseDiluido : Ssj1Base;
		double m1 = PorDegrau(m.De(Forma.Ssj1), b1, 6);
		double m2Cru = PorDegrau(m.De(Forma.Ssj2), Ssj2Base, 6, 8, 10);
		double gr = GradeMult(GradeAberto(m.De(Forma.Ssj1)), b1);
		double m2 = Math.Max(m2Cru, Math.Max(m1, gr) + 2);

		return f switch
		{
			Forma.Base => 1,
			Forma.Ssj1 => m1,
			Forma.Grade2 => GradeMult(2, b1),
			Forma.Grade3 => GradeMult(3, b1),
			Forma.Ssj2 => m2,
			// SSJ3 e multiplicador FIXO: a maestria dele NAO sobe o poder, so alivia o dreno
			Forma.Ssj3 => Math.Max(Ssj3Base, m2 + 2),
			Forma.Ssj4 => Math.Max(20, Math.Max(Ssj3Base, m2 + 2) + 2),
			_ => 1,
		};
	}

	/// <summary>
	/// FRACAO DO KI MAXIMO DRENADA POR SEGUNDO.
	///
	/// O original cobra por ciclo do BuffLoop (~0,8x/s) e o comentario dele da a conta pronta:
	/// SSJ3 nao-masterizado, `ssj3drain = 0,075` -> 0,075*0,4 = 3% por ciclo -> ~2,4%/s, ou seja
	/// esvazia um Ki cheio em ~25 s. Aqui o numero ja vem POR SEGUNDO (0,075 * 0,32 = 2,4%).
	///
	/// O SSJ1 zera o dreno aos 100% de maestria -- dominar a forma e poder ANDAR nela.
	/// </summary>
	public static double DrenoPorSegundo(Forma f, Maestrias m)
	{
		const double porCiclo = 0.4, ciclosPorSeg = 0.8;
		double bruto = f switch
		{
			Forma.Ssj1 => PorDegrau(m.De(Forma.Ssj1), 0.025, 0.015, 0),
			Forma.Grade2 => 0.035,
			Forma.Grade3 => 0.05,
			Forma.Ssj2 => PorDegrau(m.De(Forma.Ssj2), 0.045, 0.03, 0.015),
			Forma.Ssj3 => PorDegrau(m.De(Forma.Ssj3), 0.075, 0.05, 0.03),
			Forma.Ssj4 => 0.06,
			_ => 0,
		};
		return bruto * porCiclo * ciclosPorSeg;
	}

	/// <summary>
	/// Quanto de maestria se ganha por segundo DENTRO da forma.
	///
	/// O original sobe ~0,0116 por ciclo do buff, o que da as ~3 horas em forma pra dominar. E
	/// muito de proposito: maestria e o unico eixo do jogo que nao se compra nem se treina de
	/// fora -- so se paga ficando na forma, gastando Ki.
	/// </summary>
	public const double MaestriaPorSegundo = 100.0 / (3 * 3600);
}

/// <summary>Quanto o personagem domina CADA forma, de 0 a 100.</summary>
public sealed class Maestrias
{
	private readonly Dictionary<Forma, double> _v = [];

	public double De(Forma f) => _v.GetValueOrDefault(f);

	public void Por(Forma f, double v) => _v[f] = Math.Clamp(v, 0, 100);

	/// <summary>Sobe a maestria e devolve TRUE quando cruzou um marco que muda o jogo.</summary>
	public bool Subir(Forma f, double quanto, out string marco)
	{
		marco = "";
		if (f == Forma.Base || quanto <= 0) return false;

		double antes = De(f);
		if (antes >= 100) return false;
		Por(f, antes + quanto);
		double agora = De(f);

		// Os marcos que o jogador PRECISA saber que cruzou: os grades e o dominio pleno.
		if (f == Forma.Ssj1)
		{
			if (antes < EscadaSaiyajin.Grade2Pct && agora >= EscadaSaiyajin.Grade2Pct) marco = "Grade 2 liberado";
			else if (antes < EscadaSaiyajin.Grade3Pct && agora >= EscadaSaiyajin.Grade3Pct) marco = "Grade 3 liberado";
		}
		if (antes < 100 && agora >= 100) marco = "forma DOMINADA";
		return marco.Length > 0;
	}

	public IEnumerable<(Forma F, double V)> Todas => _v.Select(kv => (kv.Key, kv.Value));

	public Dictionary<string, double> ParaSave() => _v.ToDictionary(kv => ((int)kv.Key).ToString(), kv => kv.Value);

	public void DoSave(Dictionary<string, double>? d)
	{
		_v.Clear();
		if (d == null) return;
		foreach ((string k, double v) in d)
			if (int.TryParse(k, out int i)) _v[(Forma)i] = v;
	}
}
