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
///
/// ============================ A MAESTRIA (dono, 2026-10-23) ============================
/// *"faça o IT ganhar maestria com uso, e com isso ter menos consumo de ki e vigor, podendo ate ser usado em combate, pois
/// quanto mais maestria, mais rapido vc da IT em pessoas ou inimigos proximos"*. A escolha dele: de 0 a 100%, e ela SO SOBE
/// USANDO -- +2% por Instant Transmission que CHEGA (o Yardrat +3%). A 100%: o Ki custa um quarto do de sem maestria, a
/// concentracao cai a um quinto, e em quem esta no CAMPO DE VISAO ela leva 0,2 s -- da pra usar no meio da luta. Mexer
/// (e o golpe que tira o corpo do lugar) continua cancelando, como antes. Vigor: o IT nunca cobrou, e continua sem cobrar.
/// Entre 0 e 100% as contas sao RETAS (decisao minha): ver <see cref="KiComMaestria"/> e <see cref="EsperaComMaestria"/>.
/// O `teleskill` do DM (a maestria escondida, `yardrat.dm:101`) continua por baixo, como era: ele mexe no custo de antes,
/// e a maestria multiplica o que sobra.
/// =======================================================================================
///
/// ============================ A SAIDA PISCANDO E O CLARAO DA CHEGADA (dono, 2026-10-24) ============================
/// "Fazer igual no IT": o mesmo par de atrasos do Kai Kai (<see cref="KaiKai"/>). O clarao e o efeito de desviar um golpe,
/// desenhado NO CORPO, e quem esta perto so o ve com o corpo na tela: vencida a concentracao o corpo PISCA e so
/// <see cref="AtrasoDaSaidaMs"/> depois some; e na chegada o clarao espera <see cref="AtrasoDoClaraoDaChegadaMs"/> o corpo entrar
/// no retrato da zona de quem ja esta la. Nesses 0,2 s o corpo esta se desfazendo, como no desvio: nenhum golpe pega
/// (<see cref="EsquivaDaSaidaMs"/>), mexer nao cancela mais (a concentracao acabou) e ele nao fica preso -- so cair o segura.
/// =====================================================================================================================
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

	// ---------------------------------------------------------------- a maestria (2026-10-23)

	/// <summary>O teto da maestria, em pontos (%).</summary>
	public const double MaestriaMaxima = 100;

	/// <summary>Quanto um Instant Transmission que CHEGA soma. Cancelar, ser recusado ou so pedir a lista nao soma nada.</summary>
	public const double MaestriaPorViagem = 2;

	/// <summary>O mesmo, pro Yardrat -- a raca que nasceu pra isso (o dono: "+3%").</summary>
	public const double MaestriaPorViagemDoYardrat = 3;

	/// <summary>O custo de Ki na maestria cheia, em fracao do custo sem maestria: um quarto.</summary>
	public const double KiNaMaestriaCheia = 0.25;

	/// <summary>A concentracao na maestria cheia, em fracao da concentracao sem maestria: um quinto.</summary>
	public const double EsperaNaMaestriaCheia = 0.2;

	/// <summary>A concentracao em quem esta no campo de visao (<see cref="CampoDeVisao"/>, 15 tiles), na maestria cheia.</summary>
	public const long EsperaPertoNaMaestriaCheiaMs = 200;

	/// <summary>De quantos em quantos pontos o chat avisa que a maestria subiu (10%, 20%... 100%): dez linhas na vida inteira.</summary>
	public const double AvisoDaMaestriaACada = 10;

	/// <summary>A maestria em fracao (0 a 1), presa no intervalo -- ficha velha ou mexida a mao nao estoura a conta.</summary>
	public static double Fracao(double maestria) => Math.Clamp(maestria / MaestriaMaxima, 0, 1);

	/// <summary>
	/// O CUSTO DE KI COM A MAESTRIA: reto, do custo inteiro (0%) ate <see cref="KiNaMaestriaCheia"/> dele (100%). O custo
	/// "inteiro" e o de sempre (o `kireq` do DM, com o `teleskill` dentro) -- a maestria nao mexe nele, multiplica o resultado.
	/// </summary>
	public static double KiComMaestria(double kiSemMaestria, double maestria) =>
		kiSemMaestria * (1 - (1 - KiNaMaestriaCheia) * Fracao(maestria));

	/// <summary>
	/// A CONCENTRACAO COM A MAESTRIA. Longe: reta, da espera inteira (0%) ate <see cref="EsperaNaMaestriaCheia"/> dela (100%).
	/// No campo de visao: reta da espera inteira (0%) ate <see cref="EsperaPertoNaMaestriaCheiaMs"/> (100%) -- e NUNCA mais lenta
	/// que a de longe (decisao minha): o Yardrat de nivel 5 ja concentra em 0,6 s, e um quinto disso (0,12 s) ganha dos 0,2 s.
	/// Com 0% as duas sao a espera de sempre, numero por numero.
	/// </summary>
	public static long EsperaComMaestria(long esperaSemMaestria, double maestria, bool noCampoDeVisao)
	{
		double m = Fracao(maestria);
		double ms = esperaSemMaestria * (1 - (1 - EsperaNaMaestriaCheia) * m);
		if (noCampoDeVisao)
			ms = Math.Min(ms, esperaSemMaestria + (EsperaPertoNaMaestriaCheiaMs - esperaSemMaestria) * m);
		return (long)Math.Round(ms);
	}

	/// <summary>A maestria depois de um Instant Transmission que chegou: +2 (Yardrat +3), nunca acima de 100.</summary>
	public static double MaestriaDepoisDaViagem(double maestria, bool yardrat) =>
		Math.Min(MaestriaMaxima, Math.Max(0, maestria) + (yardrat ? MaestriaPorViagemDoYardrat : MaestriaPorViagem));

	// ---------------------------------------------------------------- a saida e a chegada (2026-10-24)

	/// <summary>Quanto o corpo fica PISCANDO antes de sumir, em ms -- o mesmo do Kai Kai (o dono: "Fazer igual no IT").</summary>
	public const long AtrasoDaSaidaMs = KaiKai.AtrasoDaSaidaMs;

	/// <summary>Quanto o clarao da chegada espera o corpo entrar no retrato da zona de destino, em ms -- o mesmo do Kai Kai.</summary>
	public const long AtrasoDoClaraoDaChegadaMs = KaiKai.AtrasoDoClaraoDaChegadaMs;

	/// <summary>
	/// O DESVIO DA SAIDA: quanto o corpo fica intocavel ao comecar a piscar, em ms -- a saida inteira e mais um tique do servidor
	/// (a carencia cai no tique do combate, e o salto vem no primeiro tique depois dos 0,2 s). Sem isto os 0,2 s seriam uma janela
	/// NOVA pra apanhar com a viagem ja decidida.
	/// </summary>
	public const long EsquivaDaSaidaMs = AtrasoDaSaidaMs + 50;

	/// <summary>O degrau do aviso que a viagem cruzou (10, 20... 100), ou 0 se nao cruzou nenhum.</summary>
	public static int DegrauDoAviso(double antes, double depois)
	{
		int a = (int)Math.Floor(antes / AvisoDaMaestriaACada), d = (int)Math.Floor(depois / AvisoDaMaestriaACada);
		return d > a ? (int)(d * AvisoDaMaestriaACada) : 0;
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
