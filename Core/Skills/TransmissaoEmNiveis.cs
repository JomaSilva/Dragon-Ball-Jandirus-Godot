namespace Jandirus.Core.Skills;

/// <summary>
/// O INSTANT TRANSMISSION EM CINCO NIVEIS (dono, 2026-10-21): "faça com que o teleporte seja igual o sense,
/// 5 niveis e com isso determina a distancia maxima".
///
/// A escada e a do <see cref="SenseEmNiveis"/>, numero por numero: o nivel N alcanca no maximo o que o Sense
/// nivel N alcanca. O 5o ("universo") inclui o Outro Mundo, porque o Sense so lista o Alem nesse nivel.
///
/// COMO A SKILL E O <c>teleskill</c> VIRAM NIVEL (decisao minha, no mesmo espirito do piso do Sense):
///   * a skill <c>/datum/skill/shunkanido</c> sobe como o Sense (barreira 1000, triplica, ganho por tempo,
///     o triplo meditando). O nivel 0, a skill comprada e ainda nao treinada, conta como o 1;
///   * o contador <c>teleskill</c> (Yardrat nasce com 70, os outros com 1, e ele cresce com a viagem) e o PISO,
///     como o <c>kiawarenessskill</c> e o piso do Sense: quem ja viajava nao perde alcance. 20 abre a regiao,
///     70 o planeta, 200 o sistema. O UNIVERSO nao sai do contador -- so do nivel 5 da skill. O teto do
///     contador (300 nos outros, 500 no Yardrat) continua sendo pericia de custo e de treino, nao de mapa.
/// O Yardrat concentra mais rapido (<see cref="PressaDoYardrat"/>). O alcance dele a mais, se houver, e o piso
/// do contador, nao um atalho de raca por fora da escada.
/// </summary>
public static class TransmissaoEmNiveis
{
	/// <summary>O typepath da skill -- o mesmo do DM (<c>yardrat.dm</c>).</summary>
	public const string Path = "/datum/skill/shunkanido";

	/// <summary>Os cinco alcances, os mesmos numeros do Sense, pra o cartao e o teleporte falarem a mesma lingua.</summary>
	public const int CampoDeVisao = SenseEmNiveis.CampoDeVisao, Regiao = SenseEmNiveis.Regiao,
					 Planeta = SenseEmNiveis.Planeta, Sistema = SenseEmNiveis.Sistema, Universo = SenseEmNiveis.Universo;

	/// <summary>O teto da skill.</summary>
	public const int NivelMaximo = Universo;

	/// <summary>Quem medita junto vai se estiver a no maximo isto, em tile de tabuleiro (o <c>get_dist</c> do DM, diagonal inclusive).</summary>
	public const int TilesDaCarona = 1;

	/// <summary>O dono: a concentracao nao passa de 5 segundos. O nivel 1 usa o teto inteiro.</summary>
	public const long TetoDaEsperaMs = 5000;

	/// <summary>DEFEITO: a espera velha, a de <c>max(600/teleskill, 15)</c> decimos com teleskill 1 (60 s).</summary>
	public const long EsperaSemTetoMs = 60_000;

	/// <summary>Quanto o Yardrat encurta a espera. 0,6: o nivel 1 deles leva 3 s, e nao 5.</summary>
	public const double PressaDoYardrat = 0.6;

	/// <summary>O piso do <c>teleskill</c> que abre a regiao -- o mesmo 20 que, no Sense, abre o planeta pela arvore.</summary>
	public const double TeleskillDaRegiao = 20;

	/// <summary>O piso que abre o planeta. 70 e o <c>teleskill</c> com que o Yardrat nasce (<c>yardrat.dm:85-87</c>).</summary>
	public const double TeleskillDoPlaneta = 70;

	/// <summary>O piso que abre o sistema. Abaixo do teto de quem nao e Yardrat (300), pra uma vida de viagens chegar la.</summary>
	public const double TeleskillDoSistema = 200;

	private static readonly long[] MsPorNivel = [5000, 4000, 3000, 2000, 1000];

	/// <summary>
	/// O nivel deste corpo: o maior entre o nivel da skill (0 conta como 1) e o piso do <c>teleskill</c>, nunca acima de 5.
	/// O universo so entra quando a skill chega no 5 -- o piso do contador para no sistema.
	/// </summary>
	public static int NivelDe(int nivelDaSkill, double teleskill)
	{
		int skill = nivelDaSkill <= 0 ? CampoDeVisao : nivelDaSkill;
		if (skill > NivelMaximo) skill = NivelMaximo;
		int piso = teleskill >= TeleskillDoSistema ? Sistema
			: teleskill >= TeleskillDoPlaneta ? Planeta
			: teleskill >= TeleskillDaRegiao ? Regiao
			: CampoDeVisao;
		int nivel = skill > piso ? skill : piso;
		return nivel > NivelMaximo ? NivelMaximo : nivel;
	}

	/// <summary>
	/// Quanto a concentracao dura. Nivel 1 = 5 s, e cada nivel seguinte tira 1 s. O Yardrat multiplica por
	/// <see cref="PressaDoYardrat"/>. <paramref name="semTeto"/> e o defeito da bancada: volta a espera de 60 s.
	/// </summary>
	public static long MsDaConcentracao(int nivel, bool yardrat, bool semTeto)
	{
		if (semTeto) return EsperaSemTetoMs;
		int n = nivel < 1 ? 1 : nivel > 5 ? 5 : nivel;
		long ms = MsPorNivel[n - 1];
		if (yardrat) ms = (long)(ms * PressaDoYardrat);
		return ms > TetoDaEsperaMs ? TetoDaEsperaMs : ms;
	}

	/// <summary>A regra de nivel -- registrada junto da do Sense (<c>RegrasDeNivel</c>).</summary>
	internal static RegraDeNivel Regra() => new()
	{
		Path = Path,
		Barreira = SenseEmNiveis.BarreiraInicial,
		Crescimento = SenseEmNiveis.CrescimentoPorNivel,
		GanhoPorTempo = true,
		PorEstado = [new RegraDeNivel.GanhoPorEstado { Quanto = SenseEmNiveis.GanhoMeditando, Quando = RegraDeNivel.Estado.Meditando }],
		Degraus =
		[
			new Degrau { Nivel = CampoDeVisao, Aviso = "você aprende a se transportar até uma presença próxima -- o Instant Transmission alcança o campo de visão." },
			new Degrau { Nivel = Regiao, Aviso = "o Instant Transmission alcança a região à sua volta." },
			new Degrau { Nivel = Planeta, Aviso = "o Instant Transmission cobre o planeta: qualquer lutador ou chefe que você sinta nele." },
			new Degrau { Nivel = Sistema, Aviso = "o Instant Transmission atravessa o sistema: os outros planetas desta estrela, e o espaço dela." },
			new Degrau { Nivel = Universo, Aviso = "o Instant Transmission alcança o universo -- inclusive o Outro Mundo, onde o seu Sense alcançar." },
		],
	};

	/// <summary>O teto no catalogo: cinco niveis, e nao o um do DM. A carga do catalogo chama (<c>SkillCatalog.Parse</c>).</summary>
	internal static void Aplicar(SkillCatalog cat)
	{
		if (cat.Get(Path) is { } s) s.MaxNivel = NivelMaximo;
	}
}
