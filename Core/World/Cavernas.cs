namespace Jandirus.Core.World;

/// <summary>
/// AS CAVERNAS DE UM PLANETA -- os mapas de interior que sao FILHOS de um planeta, e de qual.
///
/// O dono (2026-10-11): *"permita q de pra pegar recursos dentro dos mapas "caves" do planeta, essa e a unica
/// excessao de mapa de interior q da pra tirar recurso de um planeta"*.
///
/// ============================ POR QUE ISTO E UMA CLASSE, E NAO UM `Contains("Cave")` ============================
/// A caverna NAO E PLANETA (<see cref="Espaco.EhPlaneta"/> so aceita mundo gerado e os pre-feitos em que se
/// pousa): ela nao aparece na carta estelar, nao morre com o planeta, nao tem bandeira nem estatua. Pra tudo
/// isso ela continua sendo um interior, como o Mirante e a Sala do Tempo. A UNICA coisa em que ela difere dos
/// outros interiores e esta: o que se tira do chao dela sai da reserva do PLANETA-MAE.
///
/// Entao a relacao mora num lugar so -- "esta zona e caverna de quem?" --, e quem precisa dela pergunta aqui:
/// a reserva (`GameServer.ReservaDe`, por <see cref="Economia.Recurso.PlanetaDaReserva"/>), o verb Minerar nas
/// duas pontas, a Drill e a faixa da mineracao. Um teste de nome espalhado erraria calado no dia em que um
/// mapa novo se chamasse "Cave_of_Trials" sem ser de planeta nenhum.
/// ===============================================================================================================
///
/// ============================ E SE UM PLANETA GERADO GANHAR CAVERNA ============================
/// Hoje nenhum tem: as duas daqui sao andares do mapa do original (`z22` e `z23`). A regra nao e do NOME, e do
/// TIPO de zona -- quem diz que uma zona e caverna e <see cref="PlanetaMae"/>, e so ela. Uma caverna de mundo
/// gerado seria um interior criado pelo servidor (`ZoneKey.Interior`, como o de uma nave), e entraria por UM
/// ramo novo naquela funcao, devolvendo a `ZoneKey.Procedural` do mundo dela. A reserva, a mineracao, a Drill e
/// a tela a seguiriam sem mudar uma linha.
/// ================================================================================================
/// </summary>
public static class Cavernas
{
	/// <summary>
	/// DEFEITO INJETADO (bancadas `--recursosteste` e `--diagminerar`): caverna e um interior como os outros --
	/// o jogo de antes de 2026-10-11, em que "nao e planeta" queria dizer "nao ha o que tirar". Lido pela propria
	/// pergunta, em <see cref="PlanetaMae"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool InteriorComumDeTeste;

	/// <summary>
	/// A LISTA: cada caverna do catalogo de mapas e o planeta de que ela e filha. As bocas que ligam as duas
	/// estao no `.passagens` de cada mapa (a da Terra tem tres; a de Vegeta, tres).
	/// </summary>
	private static readonly (string Caverna, string Planeta)[] DoCatalogo =
	[
		("Earth_Cave", "Earth"),     // z23
		("Vegeta_Cave", "Vegeta"),   // z22
	];

	/// <summary>
	/// O PLANETA DE QUE ESTA ZONA E CAVERNA -- nulo quando ela nao e caverna de ninguem (o planeta em si, o
	/// espaco, o Mirante, a Sala do Tempo, uma estacao, o interior de uma nave ou de um Majin).
	///
	/// SO MAPA DO CATALOGO (<see cref="ZoneKey.KindPremade"/>): um mundo gerado ou um interior que por acaso
	/// tivesse o mesmo nome nao e caverna -- o nome sozinho nao e endereco (ver <see cref="ZoneKey"/>).
	/// </summary>
	public static ZoneKey? PlanetaMae(ZoneKey zona)
	{
		if (InteriorComumDeTeste || zona.Kind != ZoneKey.KindPremade) return null;

		foreach ((string caverna, string planeta) in DoCatalogo)
			if (string.Equals(caverna, zona.Name, StringComparison.Ordinal)) return ZoneKey.Premade(planeta);
		return null;
	}

	/// <summary>Esta zona e a caverna de algum planeta?</summary>
	public static bool EhCaverna(ZoneKey zona) => PlanetaMae(zona) != null;

	/// <summary>Todas as cavernas do catalogo, cada uma com o planeta dela -- pra quem enumera (as bancadas).</summary>
	public static IEnumerable<(ZoneKey Caverna, ZoneKey Planeta)> Todas()
	{
		foreach ((string caverna, string planeta) in DoCatalogo)
			yield return (ZoneKey.Premade(caverna), ZoneKey.Premade(planeta));
	}
}
