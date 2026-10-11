using Jandirus.Core.Economia;

namespace Jandirus.Core.Tech;

/// <summary>
/// O ESTADO DE UMA DRILL -- o que vai pro `mundo.json` dentro da obra (`Obra.Drill`).
///
/// SAO TRES NUMEROS, E O TERCEIRO E O QUE IMPORTA: o carimbo. A Drill nao "produz por tique" -- ela guarda
/// ATE QUANDO a producao dela ja foi contada, e quem pergunta (o tique lento, o menu, a coleta) faz a conta
/// do tempo que passou desde entao. E por isso que ela trabalha com o dono fora do jogo, com o planeta
/// vazio e com o servidor desligado: nenhum desses casos precisa de alguem contando.
///
/// O ESTOQUE E O CARIMBO VAO JUNTOS PRO DISCO, e e o par que nao deixa o reinicio perder nem duplicar: um
/// par velho so quer dizer que a proxima conta comeca mais atras.
/// </summary>
public sealed class DrillDaObra
{
	/// <summary>De 1 a <see cref="Drill.NivelMaximo"/>. Cada nivel sobe a taxa e o teto do estoque.</summary>
	public int Nivel = 1;

	/// <summary>O que ja foi tirado do planeta e ainda nao foi coletado. Para no <see cref="Drill.Teto"/>.</summary>
	public double Estoque;

	/// <summary>Ate que instante (ms do relogio do servidor) a producao desta Drill ja esta contada.</summary>
	public long ContadoAteMs;
}

/// <summary>Como a Drill esta, pra quem olha o menu dela.</summary>
public enum SituacaoDaDrill : byte
{
	/// <summary>Tirando recurso da reserva do planeta.</summary>
	Extraindo = 0,

	/// <summary>O estoque bateu no teto: ela para ate alguem coletar.</summary>
	Cheia = 1,

	/// <summary>A reserva do planeta secou: ela so leva o que a reserva regenerar.</summary>
	PlanetaEsgotado = 2,

	/// <summary>O lugar nao tem reserva (Lookout, interior, espaco): instalada, e sem o que extrair.</summary>
	SemReserva = 3,
}

/// <summary>
/// O QUE O MENU DA TECLA E MOSTRA DE UMA DRILL -- o que viaja no `S2C.Drill`, pra quem perguntou.
///
/// A taxa, o teto e o preco da proxima melhoria NAO viajam: saem do <see cref="Nivel"/> pelas contas do
/// <see cref="Drill"/>, que as duas pontas leem. `Id` zero = nao ha Drill ao alcance.
/// </summary>
/// <param name="FracaoDaReserva">De 0 a 1: como esta a reserva do planeta. Sem sentido em <see cref="SituacaoDaDrill.SemReserva"/>.</param>
/// <param name="Minha">Quem pergunta pode coletar e melhorar.</param>
/// <param name="Zeni">O zeni de quem pergunta -- e ele que escolhe com o que paga a melhoria.</param>
/// <param name="Recursos">O saldo de recursos de quem pergunta.</param>
public readonly record struct EstadoDeDrill(
	int Id, int Nivel, double Estoque, SituacaoDaDrill Situacao, double FracaoDaReserva,
	bool Minha, string Dono, double Zeni, double Recursos)
{
	/// <summary>"Nao ha Drill ao seu alcance" -- o que o servidor responde a quem perguntou de longe.</summary>
	public static EstadoDeDrill Nenhuma => new(0, 1, 0, SituacaoDaDrill.SemReserva, 0, false, "", 0, 0);
}

/// <summary>
/// ============================ A DRILL, EM NUMEROS -- `/obj/Power_Drill` ============================
/// Dono, 2026-10-10: *"a maquina "Drill" de tech 30 fica pegando recursos de forma automatica no planeta q
/// foi instalada, vc pode investir mais recurso ou zenni no menu de interaçao pra deixar ela melhor e pegar
/// mais recursos por segundo"*. E, das rochas de minerio: *"se alguem usar uma drill e zerar o planeta,
/// essas rochas param de spawnar"*.
///
/// ============================ O QUE ELA ERA NO DM, E POR QUE NAO E ISTO ============================
/// `Tier2.dm:434-475`: a cada 30 s (`sleep(300)`) ela sorteava UM material de alquimia e o guardava numa
/// lista de ate 30, que o clique esvaziava item a item. Aqui a regra e a do dono: ela tira RECURSO da
/// reserva do planeta, por segundo, e o que se investe nela sobe essa taxa. Do original ficaram a forma --
/// trabalha sozinha, guarda num estoque com teto, e alguem vem buscar -- e o fato de nao pedir parafuso
/// (la ela nao tem `Bolt`: comeca a trabalhar ao nascer).
///
/// ============================ AS TRES REGRAS QUE SAO UMA LINHA CADA ============================
///   * A CONTA E PELO TEMPO DECORRIDO, e nao pelo tique (<see cref="Pedido"/>): quem nao foi contado
///     por uma hora -- planeta vazio, servidor desligado -- recebe a hora inteira na proxima conta;
///   * O QUE ENTRA NO ESTOQUE E O QUE SAIU DA RESERVA (<see cref="PorEmDia"/>), nem um recurso a mais: e
///     isso que faz a Drill secar o planeta, e o planeta seco parar a Drill;
///   * AS DRILLS DO MESMO PLANETA SAO CONTADAS JUNTAS, e a reserva que nao da pra todas e rateada pelo
///     que cada uma pediu -- senao a primeira da lista levava tudo.
/// ================================================================================================
/// </summary>
public static class Drill
{
	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada `--drillteste`) -- falsos em jogo, sempre
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada --drillteste): a conta so enxerga o intervalo de UM tique, e nao o tempo que
	/// passou de verdade -- a Drill que "produz por tique". Lido pela propria linha do tempo decorrido, em
	/// <see cref="Pedido"/>: com ele, o planeta vazio e o servidor desligado perdem a producao.
	/// </summary>
	public static bool ContaSoOTiqueDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --drillteste): o estoque nao tem teto. Lido pela propria linha do teto, em
	/// <see cref="Pedido"/>.
	/// </summary>
	public static bool EstoqueSemTetoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --drillteste): o teto corta tambem o que JA ESTAVA no estoque -- baixar a
	/// constante apagaria recurso de quem tinha a Drill cheia. Lido pela propria linha do teto, em
	/// <see cref="PorEmDia"/>.
	/// </summary>
	public static bool TetoCortaOQueJaEstavaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --drillteste): a Drill credita o que PEDIU, e nao o que saiu da reserva --
	/// recurso que vem do nada. Lido pela propria linha do rateio, em <see cref="PorEmDia"/>: com ele o
	/// planeta seco nao para ninguem.
	/// </summary>
	public static bool TiraDoNadaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --drillteste): cada Drill vai a reserva por conta propria, na ordem da
	/// lista -- a primeira leva tudo. Lido pela propria linha do rateio, em <see cref="PorEmDia"/>.
	/// </summary>
	public static bool PrimeiraLevaTudoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --drillteste): o carimbo nao anda depois da conta -- o mesmo tempo e pago
	/// de novo a cada consulta. Lido pela propria linha do carimbo, em <see cref="PorEmDia"/>.
	/// </summary>
	public static bool CarimboParadoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --drillteste): coletar entrega o estoque e nao o tira da maquina. Lido pela
	/// propria linha que esvazia, em <see cref="Coletar"/>.
	/// </summary>
	public static bool ColetaNaoEsvaziaDeTeste;

	// =====================================================================
	// AS CONSTANTES -- o que o dono ajusta
	// =====================================================================
	/// <summary>O tipo da obra no catalogo (`construcoes.json`): nome "Drill", 100.000 zeni, tecnologia 30.</summary>
	public const string Tipo = "Power_Drill";

	/// <summary>Ate onde da pra melhorar.</summary>
	public const int NivelMaximo = 10;

	/// <summary>
	/// RECURSOS POR SEGUNDO, POR NIVEL: a de fabrica tira 1 por segundo, a de nivel 10 tira 10. Pra medir: um
	/// planeta pre-feito (500x500) guarda 2,5 milhoes e regenera 28,9 por segundo (os numeros de 2026-10-11,
	/// com a reserva em lado^2 x 10; antes eram 500 mil e 5,8) -- so tres Drills do nivel 10 juntas tiram mais
	/// do que ele repoe. Num mundo gerado pequeno (lado 192: 369 mil, 4,3 por segundo) uma do nivel 5 ja tira.
	/// </summary>
	public const double TaxaPorNivel = 1.0;

	/// <summary>
	/// QUANTAS HORAS DE TRABALHO CABEM NO ESTOQUE. Cheia, ela para -- e e o que obriga alguem a passar por
	/// ela. O teto sobe junto com a taxa: nivel 1 guarda 21.600, nivel 10 guarda 216.000.
	/// </summary>
	public const double HorasDeEstoque = 6.0;

	/// <summary>
	/// O PRECO DA MELHORIA, EM ZENI: este tanto vezes o nivel ATUAL (25 mil do 1 pro 2, 225 mil do 9 pro 10;
	/// 1.125.000 pra levar uma do 1 ao 10). Em recursos e o dobro -- a conta e do contrato
	/// (<see cref="Recurso.CustoEmRecursos"/>), como em toda obra.
	/// </summary>
	public const double CustoBaseDaMelhoria = 25_000;

	/// <summary>De quanto em quanto o servidor poe TODAS as Drills em dia. A conta e pelo tempo decorrido: este numero so diz o quanto o estoque fica atrasado entre duas consultas.</summary>
	public const double SegundosEntreLeituras = 5.0;

	/// <summary>De quanto em quanto o estoque que mudou vai pro disco. E o maximo que uma queda do servidor faz o planeta "pagar de novo".</summary>
	public const double SegundosEntreGravacoes = 60.0;

	/// <summary>A folga das comparacoes de estoque e de reserva (contas em ponto flutuante).</summary>
	public const double Folga = 1e-6;

	// OS VERBOS DO MENU DA TECLA E. Escritos uma vez: a tabela do `Interacoes`, o despacho do servidor e o
	// painel do cliente leem os mesmos nomes.
	public const string VerboVer = "drill_ver";
	public const string VerboInfo = "drill_info";
	public const string VerboColetar = "drill_coletar";
	public const string VerboMelhorar = "drill_up";

	/// <summary>Os dois argumentos do <see cref="VerboMelhorar"/>: com o que se paga.</summary>
	public const string PagarComZeni = "zeni", PagarComRecursos = "recursos";

	/// <summary>A pagina "Melhorar..." do menu da tecla E, e a chave dela na tabela do `Interacoes` (`tipo/pagina`).</summary>
	public const string PaginaDeMelhorias = "melhorar", ChaveDasMelhorias = Tipo + "/" + PaginaDeMelhorias;

	// =====================================================================
	// A CURVA
	// =====================================================================
	/// <summary>O nivel dentro da faixa -- um `mundo.json` mexido a mao nao vira uma Drill de nivel 900.</summary>
	public static int NivelValido(int nivel) => Math.Clamp(nivel, 1, NivelMaximo);

	/// <summary>Quantos recursos por segundo este nivel tira do planeta.</summary>
	public static double Taxa(int nivel) => TaxaPorNivel * NivelValido(nivel);

	/// <summary>Quanto cabe no estoque deste nivel.</summary>
	public static double Teto(int nivel) => Taxa(nivel) * HorasDeEstoque * 3600.0;

	/// <summary>Quanto custa, EM ZENI, sair deste nivel pro seguinte. Em recursos: <see cref="Recurso.CustoEmRecursos"/> disto.</summary>
	public static double CustoDaMelhoria(int nivelAtual) => CustoBaseDaMelhoria * NivelValido(nivelAtual);

	public static bool NoMaximo(int nivel) => nivel >= NivelMaximo;

	// =====================================================================
	// A CONTA
	// =====================================================================
	/// <summary>
	/// QUANTO ESTA DRILL PEDE A RESERVA AGORA: a taxa do nivel pelo tempo que passou desde a ultima conta,
	/// ate o que ainda cabe no estoque. Relogio que andou pra tras pede zero.
	/// </summary>
	public static double Pedido(DrillDaObra d, long agoraMs)
	{
		double segundos = Math.Max(0, (agoraMs - d.ContadoAteMs) / 1000.0);
		if (ContaSoOTiqueDeTeste) segundos = Math.Min(segundos, SegundosEntreLeituras);

		double quer = Taxa(d.Nivel) * segundos;
		return EstoqueSemTetoDeTeste ? quer : Math.Min(quer, Math.Max(0, Teto(d.Nivel) - d.Estoque));
	}

	/// <summary>
	/// POE EM DIA AS DRILLS DE UM PLANETA, todas de uma vez: soma o que cada uma pede, tira da reserva pela
	/// porta de sempre (<see cref="ReservaDePlaneta.Tirar"/>) e reparte o que SAIU na proporcao do que cada
	/// uma pediu. Com a reserva folgada cada uma recebe o que pediu; com ela seca, ninguem recebe.
	/// </summary>
	/// <returns>O que as Drills pediram, e o que saiu da reserva de fato.</returns>
	public static (double Pedido, double Saiu) PorEmDia(IReadOnlyList<DrillDaObra> drills, ReservaDePlaneta reserva, long agoraMs)
	{
		int n = drills.Count;
		if (n == 0) return (0, 0);

		double[] pedidos = new double[n];
		double total = 0;
		for (int i = 0; i < n; i++) total += pedidos[i] = Pedido(drills[i], agoraMs);

		double saiu = PrimeiraLevaTudoDeTeste ? 0 : reserva.Tirar(total);
		double fator = total > 0 ? saiu / total : 0;

		for (int i = 0; i < n; i++)
		{
			DrillDaObra d = drills[i];
			double parte = PrimeiraLevaTudoDeTeste ? reserva.Tirar(pedidos[i])
						 : TiraDoNadaDeTeste ? pedidos[i]
						 : pedidos[i] * fator;
			if (PrimeiraLevaTudoDeTeste) saiu += parte;

			// O TETO APARA O QUE ENTRA, e nunca o que ja estava la: um estoque acima do teto de hoje (o dono
			// baixou `HorasDeEstoque`, alguem mexeu no nivel dentro do `mundo.json`) fica inteiro ate ser coletado.
			double novo = d.Estoque + parte;
			if (!EstoqueSemTetoDeTeste)
				novo = Math.Min(novo, TetoCortaOQueJaEstavaDeTeste ? Teto(d.Nivel) : Math.Max(d.Estoque, Teto(d.Nivel)));
			d.Estoque = novo;
			if (!CarimboParadoDeTeste) d.ContadoAteMs = agoraMs;
		}
		return (total, saiu);
	}

	/// <summary>Como esta Drill esta, DEPOIS de posta em dia. Reserva nula = o lugar nao tem o que extrair.</summary>
	public static SituacaoDaDrill Situacao(DrillDaObra d, ReservaDePlaneta? reserva) =>
		reserva == null ? SituacaoDaDrill.SemReserva
		: d.Estoque >= Teto(d.Nivel) - Folga ? SituacaoDaDrill.Cheia
		: reserva.Atual <= Folga ? SituacaoDaDrill.PlanetaEsgotado
		: SituacaoDaDrill.Extraindo;

	/// <summary>
	/// COLETA: tira do estoque as unidades INTEIRAS e as devolve. A fracao fica na maquina -- o saldo do
	/// jogador paga precos inteiros, e um saldo de "1999,6" que a tela arredonda pra 2000 e nao compra o que
	/// custa 2000 seria uma mentira.
	/// </summary>
	public static double Coletar(DrillDaObra d)
	{
		double inteiro = Math.Floor(d.Estoque + Folga);
		if (inteiro <= 0) return 0;
		if (!ColetaNaoEsvaziaDeTeste) d.Estoque = Math.Max(0, d.Estoque - inteiro);
		return inteiro;
	}

	// =====================================================================
	// O QUE O JOGADOR LE
	// =====================================================================
	/// <summary>
	/// O ESTADO ALGUNS SEGUNDOS DEPOIS, sem perguntar de novo: o painel do cliente mostra o estoque subindo
	/// entre duas respostas do servidor. So a Drill que esta EXTRAINDO anda -- e para no teto.
	/// </summary>
	public static EstadoDeDrill Adiante(in EstadoDeDrill e, double segundos)
	{
		if (e.Situacao != SituacaoDaDrill.Extraindo || !(segundos > 0)) return e;
		double teto = Teto(e.Nivel);
		double estoque = Math.Min(teto, e.Estoque + Taxa(e.Nivel) * segundos);
		return e with { Estoque = estoque, Situacao = estoque >= teto - Folga ? SituacaoDaDrill.Cheia : SituacaoDaDrill.Extraindo };
	}

	/// <summary>
	/// AS LINHAS DO ESTADO, na ordem em que se leem -- as MESMAS no chat ("Ver estado") e no painel do menu
	/// da tecla E. Uma casa so pras duas pontas: o que o servidor diz e o que a tela mostra nao discordam.
	/// </summary>
	public static List<string> Descrever(in EstadoDeDrill e)
	{
		List<string> l = [];
		if (!e.Minha && e.Dono.Length > 0) l.Add($"Drill de {e.Dono}: só o dono coleta e melhora.");

		double taxa = Taxa(e.Nivel), teto = Teto(e.Nivel);
		l.Add($"Nível {NivelValido(e.Nivel)} de {NivelMaximo}: {taxa:0.#} {(taxa == 1 ? "recurso" : "recursos")} por segundo");

		string estoque = $"Estoque: {Math.Floor(e.Estoque + Folga):N0} de {teto:N0}";
		if (e.Situacao == SituacaoDaDrill.Extraindo && taxa > 0)
			estoque += $" (cheio em {Duracao((teto - e.Estoque) / taxa)})";
		l.Add(estoque);

		switch (e.Situacao)
		{
			case SituacaoDaDrill.SemReserva:
				l.Add("AQUI NÃO HÁ O QUE EXTRAIR: a Drill só trabalha num planeta -- na superfície, ou numa caverna dele.");
				break;
			case SituacaoDaDrill.PlanetaEsgotado:
				l.Add("PLANETA ESGOTADO: ela só extrai o que a reserva regenerar.");
				break;
			case SituacaoDaDrill.Cheia:
				l.Add("ESTOQUE CHEIO: ela parou. Colete pra ela voltar a extrair.");
				l.Add($"Reserva do planeta: {Porcento(e.FracaoDaReserva)}");
				break;
			default:
				l.Add($"Reserva do planeta: {Porcento(e.FracaoDaReserva)}");
				break;
		}

		if (!e.Minha) return l;

		if (NoMaximo(e.Nivel)) l.Add("Nível máximo: não há mais o que melhorar.");
		else
		{
			double custo = CustoDaMelhoria(e.Nivel);
			l.Add($"Nível {e.Nivel + 1} ({Taxa(e.Nivel + 1):0.#}/s, estoque de {Teto(e.Nivel + 1):N0}): "
				  + $"{custo:N0} zeni ou {Recurso.CustoEmRecursos(custo):N0} recursos");
		}
		l.Add($"Você tem {Math.Floor(e.Zeni):N0} zeni e {Math.Floor(e.Recursos + Folga):N0} recursos.");
		return l;
	}

	private static string Porcento(double fracao) => $"{Math.Clamp(fracao, 0, 1) * 100:0}%";

	/// <summary>"2 h 13 min", "45 min", "30 s" -- o tempo que falta pro estoque encher.</summary>
	public static string Duracao(double segundos)
	{
		if (!(segundos > 0)) return "0 s";
		if (segundos < 60) return $"{Math.Ceiling(segundos):0} s";
		long minutos = (long)Math.Ceiling(segundos / 60.0);
		return minutos < 60 ? $"{minutos} min" : minutos % 60 == 0 ? $"{minutos / 60} h" : $"{minutos / 60} h {minutos % 60} min";
	}
}
