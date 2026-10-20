namespace Jandirus.Core.World;

/// <summary>
/// **O QUE AINDA E O PLANETA** -- as zonas de interior que se alcancam A PE a partir dele, pelas bocas de passagem (os
/// `.passagens` dos mapas), sem pisar noutro planeta no caminho. A Terra: a caverna (tres bocas) e o Lookout (a escada do
/// Templo desce na Terra). Vegeta: a caverna dela e o mesmo Lookout (o castelo esta no mesmo andar do DM). Namek e os mundos
/// gerados: nenhuma.
///
/// Nasceu pras Esferas do Dragao (dono, 2026-10-19): *"n da pra sair do planeta com elas, entao naves e espaco vc e bloqueado
/// de ir com elas, mas locais como cavernas, lookout etc vc pode ir pq ainda estao dentro do planeta"*.
///
/// ============================ PELO CAMINHO, E NAO POR UMA LISTA ============================
/// A <see cref="Cavernas"/> responde "de que planeta e esta caverna", e e lista escrita a mao porque o que ela decide (de que
/// reserva sai o minerio) e so das cavernas. A pergunta daqui e outra -- "indo ate la, eu saio do planeta?" -- e quem responde
/// e o caminho: se uma boca leva a pe, o lugar e do planeta. A Sala do Tempo NAO entra: a porta dela nao e boca de passagem
/// (cobra chave e lotacao), e no DM ela e outra dimensao (`area/Hyperbolic_Time_Chamber`, `Planet = "Hyperbolic Time
/// Dimension"`, enquanto `area/Lookout` tem `Planet = "Earth"`, `Areas.dm:268-285`). Nave, mente, interior de Majin e o Outro
/// Mundo tambem nao. Mapa novo com boca nova entra sozinho.
/// ============================================================================================
/// </summary>
public static class InteriorDoPlaneta
{
	/// <summary>
	/// OS INTERIORES DESTE PLANETA, por nome de zona do catalogo. A busca anda de boca em boca e PARA em todo planeta: chegar
	/// num e sair deste (a escada do Lookout que da no castelo de Vegeta nao torna Vegeta parte da Terra).
	/// </summary>
	/// <param name="bocasDe">Os destinos das passagens de uma zona (o `.passagens` dela), por nome.</param>
	/// <param name="ehPlaneta">Esta zona do catalogo e planeta? A pergunta de producao e `Espaco.EhPlaneta`.</param>
	public static HashSet<string> Do(string planeta, Func<string, IEnumerable<string>> bocasDe, Func<string, bool> ehPlaneta)
	{
		var achados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var fila = new Queue<string>();
		fila.Enqueue(planeta);
		while (fila.Count > 0)
		{
			foreach (string destino in bocasDe(fila.Dequeue()))
			{
				if (string.Equals(destino, planeta, StringComparison.OrdinalIgnoreCase) || ehPlaneta(destino)) continue;
				if (achados.Add(destino)) fila.Enqueue(destino);
			}
		}
		return achados;
	}
}
