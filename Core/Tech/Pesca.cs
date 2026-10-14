namespace Jandirus.Core.Tech;

/// <summary>
/// A PESCA -- `obj/items/FishingPole` e `mob/proc/GenerateFish` (`Code/Modules/Stamina/Fishing.dm`).
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// O verb `Fish()` pede agua a dois tiles (`for(turf/T in view(2)) if(T.Water)`), lanca a boia e roda um laco de
/// dois em dois segundos (`sleep(20)`): com 10% de chance o `nextFish` anda um, e o peixe morde quando
///
///     nextFish >= 10 * log(max(FishCaught,1)) / log(max(waterNearby,1))      (`:33`)
///     log(max(nextFish,1)) * FishingSkill > 1 + FishCaught                    (`:34`)
///
/// Mordeu, a boia fica cinco segundos de pe (`fishup`, `sleep(50)`): clicar nela nesse tempo PEGA o peixe
/// (`obj/bobber/Click`), e a pericia sobe um (`usr.FishingSkill += 1`, `:48`). Qual peixe e o `GenerateFish`:
/// a truta a partir de 15 de pericia e o salmao a partir de 35, por sorteio.
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * PUXAR E USAR A VARA DE NOVO, e nao clicar na boia: a boia deste port nao e um objeto clicavel.
///   * COM UM TILE DE AGUA SO, o original divide por `log(1)` = zero (a conta estoura e a pescaria trava). Aqui
///     o divisor nao desce de `log(2)`.
///   * O `FishCaught` e temporario la (`tmp`) e cai um a cada volta do laco, dez segundos atrasado
///     (`spawn(100)`): aqui ele cai um por volta, sem o atraso.
///   * A PERICIA COMECA EM 1. La ela vem da maestria de vida "Fishing" (`Lifeskill Masteries.dm:26-28`), que este
///     port nao tem; o que sobe a pericia aqui e so pescar (o `+= 1` por peixe, que tambem e de la).
///   * NAO HA COZINHA neste port: o peixe se come cru, com a nutricao do cru (10, 15 e 20).
/// ==================================================================================
/// </summary>
public static class Pesca
{
	/// <summary>`sleep(20)`: de quanto em quanto a linha e olhada.</summary>
	public const double SegundosPorVolta = 2;

	/// <summary>`prob(10)`: a chance de o `nextFish` andar a cada volta.</summary>
	public const double ChanceDeAndar = 0.10;

	/// <summary>`sleep(50)` com a boia de pe: o tempo pra puxar.</summary>
	public const double SegundosPraPuxar = 5;

	/// <summary>`view(2)`: a que distancia (em tiles) a agua conta.</summary>
	public const int RaioDeAgua = 2;

	public const int PericiaDaTruta = 15, PericiaDoSalmao = 35;

	/// <summary>
	/// O PEIXE MORDEU NESTA VOLTA? As duas linhas do laco (`Fishing.dm:33-34`), com o divisor protegido (ver o
	/// cabecalho). <paramref name="pegosHaPouco"/> e o `FishCaught`.
	/// </summary>
	public static bool Mordeu(int nextFish, int pegosHaPouco, int aguaPerto, double pericia)
	{
		double limiar = 10 * Math.Log(Math.Max(pegosHaPouco, 1)) / Math.Log(Math.Max(aguaPerto, 2));
		return nextFish >= limiar && Math.Log(Math.Max(nextFish, 1)) * pericia > 1 + pegosHaPouco;
	}

	/// <summary>
	/// QUE PEIXE SAIU -- o `GenerateFish()` (`Fishing.dm:109-127`): `fishchance = min(60, FishingSkill * rand(1,3))`;
	/// salmao com 35 de pericia e `prob(fishchance/3)`, senao truta com 15 e `prob(fishchance/2)`, senao o peixe comum.
	/// <paramref name="dado"/> devolve um inteiro em [1, 3]; <paramref name="sorte"/>, um numero em [0, 100).
	/// </summary>
	public static string Peixe(double pericia, Func<int> dado, Func<double> sorte)
	{
		double chance = Math.Min(60, pericia * dado());
		if (pericia >= PericiaDoSalmao && sorte() < chance / 3) return Jandirus.Core.Items.CatalogoDeItens.Salmao;
		if (pericia >= PericiaDaTruta && sorte() < chance / 2) return Jandirus.Core.Items.CatalogoDeItens.Truta;
		return Jandirus.Core.Items.CatalogoDeItens.Peixe;
	}

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia T): a linha nunca fisga -- a vara da v0.2.0, um item parado na mochila.</summary>
	public static bool NuncaFisgaDeTeste;
}
