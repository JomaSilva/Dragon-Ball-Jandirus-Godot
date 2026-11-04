using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Magic;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ A CHAVE DA SALA DO TEMPO E AS SALAS QUE OS DRAGOES REVELAM (dono, 2026-10-31) ============================
/// O pedido 196: *"faça com q viloes na terra tenham uma chance (bem baixa) de dropar uma "htc key" uma chave q permite entrar na sala do
/// tempo sem precisar do guardiao autorizar (mas ainda segue as regras de so liberar sala do tempo 2 dias apos wipe ou quando alguém bater
/// 20m) , permita q esferas tb tenham desejo de dar essa chave. Elas vao direto pro inventario da pessoa ao dropar de vilao ou do pedido, e o
/// sprite delas é o sprite padrao de key. Essa chave so pode ser usada uma vez e ela quebra ao ser usada."*
///   * A CHAVE e um item (`CatalogoDeItens.ChaveDaSala`, com a arte da chave do catalogo de construcoes). Ela FAZ AS VEZES da autorizacao do
///     Guardiao na porta da Sala da Terra (`EntrarNaSala`), e so dela: a sala fechada no comeco do mundo, a prisao, a lotacao e a recarga
///     continuam recusando, e nenhuma dessas recusas gasta a chave -- ela so QUEBRA quando o corpo atravessa, como a autorizacao, que
///     tambem so se consome na entrada. Quem tem as duas usa a autorizacao e guarda a chave. So a Sala da Terra: as de planeta nunca
///     pediram o Guardiao.
///   * O VILAO DA TERRA (o vilao avulso que aparece no planeta, `VilaoAvulso`) a deixa, com <see cref="ChanceDaChaveNoVilao"/> de chance,
///     pra quem o derrota -- na mesma vez unica do karma e do zeni (`VilaoFoiDerrotado`).
///   * O DESEJO (`Desejos.IdDaChaveDaSala`) esta na tabela de todo dragao: uma chave pra quem recebe o pedido.
///   * MOCHILA CHEIA: a chave cai aos pes, pelo funil de tudo o que cai no chao (`PorNoChao`) -- "direto pro inventario" sem lugar nele
///     nao pode apagar a chave.
///
/// O pedido 197: *"Faça com q black star db tenha um pedido pra falar qual planeta mais proximo(sem ser a terra) q tem uma sala do tempo
/// procedural. E a super db vai ter um pedido pra ela revelar onde da qualquer sala do tempo procedural , assim podendo perguntar qual
/// planeta tem a sala de 5 anos ou de 3 meses etc, o jogador escolhe."*
///   * AS BLACK STAR (`Desejos.IdDaSalaMaisProxima`): o mundo sorteado com sala mais perto de quem pede, de onde ele esta na galaxia (o
///     disco do mundo em que ele pisa -- `OndeEstouNaGalaxia` --, ou o ponto dele no espaco). Toda sala procedural e de mundo sorteado: a
///     Terra nunca entra.
///   * O SUPER SHENLONG (`Desejos.IdDaSalaRevelada`): a mesma busca, so as salas da dilatacao escolhida na segunda etapa da janela (uma
///     resposta por linha de `SalaDePlaneta.Tabela`, de 3 meses a 5 anos).
///   * A RESPOSTA vai pra quem pediu: o nome do mundo, o setor [x:y] da carta estelar, quanto tempo passa la dentro e a distancia. O mundo
///     ouve que alguem perguntou, e nao a resposta. Mundo destruido nao conta; sem sala nenhuma no raio, o pedido nao se gasta.
/// ===============================================================================================================================
/// Bancadas: `--salateste` (a porta), `--viloesteste` (o vilao), `--desejoteste` (os tres desejos) e `--menudodragaoteste` (a janela).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// A CHANCE DE UM VILAO DA TERRA DEIXAR A CHAVE -- *"uma chance (bem baixa)"*: 3% por vilao derrotado. Com um vilao a cada 12 a 30
	/// minutos num planeta com gente (`VilaoAvulso.IntervaloMinS`), e uma chave a cada dez horas de Terra, mais ou menos.
	/// </summary>
	public const double ChanceDaChaveNoVilao = 0.03;

	/// <summary>Os raios (em celulas de sistema) da busca dos desejos, do mais perto pro mais longe: os do `admin_sala_do_tempo` e mais um -- a de 5 anos e um mundo em vinte mil.</summary>
	private static readonly int[] RaiosDosDesejosDaSala = [6, 12, 24, 48, 96];

	/// <summary>A MOEDA DA BANCADA: o sorteio da chave do vilao no lugar do `_rng`. Nulo em jogo.</summary>
	internal double? SorteioDaChaveDeTeste;

	/// <summary>DEFEITO INJETADO (`--salateste`): a porta ignora a chave -- o jogo de antes do pedido 196. Lido por <see cref="TemAChaveDaSala"/>. Falso em jogo, sempre.</summary>
	internal static bool PortaIgnoraAChaveDeTeste;

	/// <summary>DEFEITO INJETADO (`--viloesteste`): o vilao de QUALQUER planeta deixa a chave, e nao so o da Terra. Lido por <see cref="ChaveDoVilao"/>. Falso em jogo, sempre.</summary>
	internal static bool ChaveEmTodoPlanetaDeTeste;

	/// <summary>DEFEITO INJETADO (`--desejoteste`): a busca ignora a dilatacao escolhida e devolve a sala mais perto de qualquer uma. Lido por <see cref="SalaMaisPertoDe"/>. Falso em jogo, sempre.</summary>
	internal static bool SalaReveladaSemEscolhaDeTeste;

	// =====================================================================
	// A CHAVE
	// =====================================================================
	/// <summary>
	/// A CHAVE VAI DIRETO PRA MOCHILA (*"Elas vao direto pro inventario da pessoa"*); sem lugar nela, cai aos pes. As duas frases vem
	/// inteiras de quem chama -- o tradutor casa o molde com o nome no lugar do pedaco. Devolve se ficou na mochila.
	/// </summary>
	private bool DarAChaveDaSala(ServerPlayer pl, string naMochila, string noChao)
	{
		if (pl.Mochila.Guardar(CatalogoDeItens.ChaveDaSala) == 0)
		{
			MandarMochila(pl);
			Persistir(pl);
			Avisar(pl, naMochila);
			return true;
		}
		PorNoChao(pl.Zone, OndeCaiOQueSeLarga(pl), TipoDeLargado.Item, CatalogoDeItens.ChaveDaSala, 1);
		Avisar(pl, noChao);
		return false;
	}

	/// <summary>A chave na mochila faz as vezes da autorizacao do Guardiao na porta da Sala da Terra (ver o cabecalho).</summary>
	private static bool TemAChaveDaSala(ServerPlayer pl) =>
		!PortaIgnoraAChaveDeTeste && pl.Mochila.Quantos(CatalogoDeItens.ChaveDaSala) > 0;

	/// <summary>A CHAVE QUEBRA NA ENTRADA -- *"Essa chave so pode ser usada uma vez e ela quebra ao ser usada"*. Chamado do `EntrarNaSala`.</summary>
	private void QuebrarAChaveDaSala(ServerPlayer pl)
	{
		pl.Mochila.Tirar(CatalogoDeItens.ChaveDaSala);
		MandarMochila(pl);
		Avisar(pl, "a Chave da Sala do Tempo gira na fechadura e a barreira se abre -- e a chave se parte em pedaços na sua mão.");
		GD.Print($"[server] {pl.Name} abriu a Sala do Tempo com uma Chave da Sala (a chave quebrou)");
	}

	/// <summary>
	/// O VILAO DA TERRA PODE DEIXAR A CHAVE. Chamado do `VilaoFoiDerrotado`, na vez unica do premio, pro heroi (quem o derrotou, ou o dono
	/// do clone que o derrotou). A Terra e o lugar em que o vilao APARECEU.
	/// </summary>
	private void ChaveDoVilao(ServerPlayer heroi, ZoneKey ondeApareceu, string nomeDoVilao)
	{
		bool daTerra = ondeApareceu.Kind == ZoneKey.KindPremade
					   && string.Equals(ondeApareceu.Name, ClasseDeNuvem.ZonaDaTerra, StringComparison.OrdinalIgnoreCase);
		if (!daTerra && !ChaveEmTodoPlanetaDeTeste) return;
		if ((SorteioDaChaveDeTeste ?? _rng.NextDouble()) >= ChanceDaChaveNoVilao) return;
		DarAChaveDaSala(heroi,
			$"o vilão {nomeDoVilao} deixou cair uma Chave da Sala do Tempo -- e ela foi direto pra sua mochila.",
			$"o vilão {nomeDoVilao} deixou cair uma Chave da Sala do Tempo -- mas a sua mochila está cheia, e ela caiu aos seus pés.");
		GD.Print($"[viloes] {heroi.Name} ganhou uma Chave da Sala do Tempo do vilao {nomeDoVilao}");
	}

	/// <summary>O DESEJO DA CHAVE (o pedido 196): uma chave pra quem recebe o pedido -- de qualquer dragao, uma so.</summary>
	private bool DesejoDaChaveDaSala(ServerPlayer quem, SetDeEsferas s)
	{
		AnunciarDesejo($"{quem.Name} recebe de {s.NomeDoDragao} uma Chave da Sala do Tempo!");
		DarAChaveDaSala(quem, "o dragão te dá uma Chave da Sala do Tempo -- e ela foi direto pra sua mochila.",
						"o dragão te dá uma Chave da Sala do Tempo -- mas a sua mochila está cheia, e ela caiu aos seus pés.");
		return true;
	}

	// =====================================================================
	// AS SALAS QUE OS DRAGOES REVELAM (o pedido 197)
	// =====================================================================
	/// <summary>
	/// A SALA PROCEDURAL MAIS PERTO DE QUEM PEDE -- so as de <paramref name="dilatacao"/> anos por dia, ou qualquer uma (nulo). Pelos raios
	/// crescentes de <see cref="RaiosDosDesejosDaSala"/>; mundo destruido nao conta. A achada so vale no raio em que ela esta DENTRO DO
	/// CIRCULO dele: fora do quadrado varrido nao ha nada mais perto que o raio, mas um canto do quadrado fica mais longe que um mundo do
	/// anel seguinte. Nulo = nenhuma ate o ultimo raio.
	/// </summary>
	internal SalaNoUniverso? SalaMaisPertoDe(ServerPlayer pl, double? dilatacao)
	{
		Vec2 daqui = DeOndePedeASala(pl);
		SistemaId c = SistemaId.De(daqui);
		SalaNoUniverso? melhor = null;
		double menor = double.MaxValue;
		foreach (int raio in RaiosDosDesejosDaSala)
		{
			foreach (SalaNoUniverso s in SalaDePlaneta.NaRegiao(SeedDoUniverso, c.Sx, c.Sy, raio))
			{
				if (dilatacao is { } d && !SalaReveladaSemEscolhaDeTeste && Math.Abs(s.Dilatacao - d) > 1e-9) continue;
				if (ZonaMorta(Espaco.ZonaDe(s.Corpo))) continue;
				double dist = (s.Corpo.Pos - daqui).Length;
				if (dist >= menor) continue;
				menor = dist;
				melhor = s;
			}
			if (melhor != null && menor <= raio * (double)Sistemas.CelulaPx) return melhor;
		}
		return melhor;
	}

	/// <summary>De onde quem pede esta na galaxia: o ponto dele no espaco, ou o disco do mundo em que ele pisa (a Terra, num interior).</summary>
	private Vec2 DeOndePedeASala(ServerPlayer pl) => Espaco.EhEspaco(pl.Zone) ? pl.Pos : OndeEstouNaGalaxia(pl);

	/// <summary>Onde fica a sala, na boca do dragao: o mundo, o setor da carta estelar, o tempo la dentro e a distancia de quem pediu.</summary>
	private string OndeFicaASala(ServerPlayer pl, SalaNoUniverso s) =>
		$"{NomeDoMundo(s.Corpo)}, no setor [{s.Sx}:{s.Sy}] da carta estelar -- lá dentro passam {SalaDePlaneta.TextoDaDilatacao(s.Dilatacao)} "
		+ $"por dia daqui, a {(s.Corpo.Pos - DeOndePedeASala(pl)).Length / Sistemas.CelulaPx:0.0} célula(s) de você.";

	/// <summary>O DESEJO DAS BLACK STAR (o pedido 197): a sala procedural mais perto de quem pede, de qualquer dilatacao.</summary>
	private bool DesejoDaSalaMaisProxima(ServerPlayer pl, SetDeEsferas s)
	{
		if (SalaMaisPertoDe(pl, null) is not { } sala)
		{
			Avisar(pl, "o dragão não enxerga nenhuma Sala do Tempo nos mundos em volta. Nenhum pedido foi gasto.");
			return false;
		}
		AnunciarDesejo($"{pl.Name} pergunta a {s.NomeDoDragao} onde fica a Sala do Tempo mais próxima.");
		Avisar(pl, $"{s.NomeDoDragao}: a Sala do Tempo mais perto de você fica em {OndeFicaASala(pl, sala)}");
		GD.Print($"[server] {pl.Name} pediu a sala mais proxima: {sala.Corpo.Nome} [{sala.Sx}:{sala.Sy}], {sala.Dilatacao:0.##} ano(s)/dia");
		return true;
	}

	/// <summary>
	/// O DESEJO DO SUPER SHENLONG (o pedido 197): a sala da dilatacao escolhida mais perto de quem pede. A escolha vem da segunda etapa da
	/// janela (`EntradaDoDesejo`), em anos por dia ("0.25" ... "5"); fora da tabela, a recusa nao gasta nada.
	/// </summary>
	private bool DesejoDaSalaRevelada(ServerPlayer pl, ServerPlayer alvo, string escolha)
	{
		if (!double.TryParse(escolha.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
							 System.Globalization.CultureInfo.InvariantCulture, out double anos)
			|| !SalaDePlaneta.Tabela.Any(f => Math.Abs(f.AnosPorDia - anos) < 1e-9))
		{
			Avisar(pl, "escolha qual Sala do Tempo o Super Shenlong deve revelar: de 3 meses a 5 anos por dia. Nenhum pedido foi gasto.");
			return false;
		}
		if (SalaMaisPertoDe(alvo, anos) is not { } sala)
		{
			Avisar(pl, $"nem o Super Shenlong enxerga uma Sala do Tempo de {SalaDePlaneta.TextoDaDilatacao(anos)} por dia nos mundos em volta. "
					   + "Nenhum pedido foi gasto.");
			return false;
		}
		AnunciarInvocacaoDoSuper(pl);
		AnunciarSupers($"{alvo.Name} pede ao SUPER SHENLONG que revele uma Sala do Tempo!");
		Avisar(alvo, $"SUPER SHENLONG: a Sala do Tempo de {SalaDePlaneta.TextoDaDilatacao(anos)} mais perto de você fica em {OndeFicaASala(alvo, sala)}");
		if (alvo != pl) Avisar(pl, $"a resposta foi para {alvo.Name} -- você apenas pronunciou.");
		GD.Print($"[server] {alvo.Name} pediu ao Super a sala de {anos:0.##} ano(s)/dia: {sala.Corpo.Nome} [{sala.Sx}:{sala.Sy}]");
		return true;
	}
}
