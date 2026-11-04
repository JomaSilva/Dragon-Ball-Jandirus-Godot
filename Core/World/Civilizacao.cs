namespace Jandirus.Core.World;

/// <summary>
/// ============================ AS CIVILIZACOES DOS MUNDOS SORTEADOS (o pedido 227, 2026-11-02) ============================
/// O dono: *"15% dos planetas procedurais vao ter civilizaçao, com isso vc pode fazer cidades procedurais com casas e estradas ligando
/// essas casas etc, e os npcs desse planeta andando normalmente [...] essas cidades e npcs sao gerados no momento em q alguem entra no
/// planeta pela primeira vez e ele faz parte desses 15%"*.
///
/// QUAL MUNDO TEM, SO A SEED DIZ (<see cref="Tem"/>) -- como a Sala do Tempo de planeta: o cliente e o servidor sabem sem trocar um byte, e
/// o mundo que ninguem visitou e so um numero (*"antes disso o server gera a seed da cidade e populaçao"*). As tres partes nascem quando o
/// mundo nasce na memoria, que e a primeira entrada de alguem:
///   * a CIDADE e do chao -- `CidadeProcedural`, erguida pelo `GeradorDeTerreno` (casas e estradas iguais nas duas pontas);
///   * a RACA e da seed -- `Races.RacaProcedural` (o nome, o corpo, a pele, o cabelo, os numeros);
///   * o POVO e do servidor -- `GameServer.Civilizacao.cs`, quando o mundo gerado fica pronto, como o do Yardrat.
///
/// O YARDRAT FICA DE FORA: e um mundo sorteado que ja tem o povo dele (o pedido 109), e uma segunda raca la seria outro planeta.
/// =========================================================================================================================
/// </summary>
public static class Civilizacao
{
	/// <summary>A fracao dos mundos sorteados com civilizacao: 15%, o numero do dono.</summary>
	public const double Fracao = 0.15;

	/// <summary>Quantos habitantes por casa da cidade (mais <see cref="HabitantesNaPraca"/> na praca).</summary>
	public const int HabitantesPorCasa = 2;

	public const int HabitantesNaPraca = 2;

	/// <summary>DEFEITO INJETADO (`--civteste`): nenhum mundo tem civilizacao -- o jogo de antes do pedido 227. Falso em jogo, sempre.</summary>
	public static bool NenhumaDeTeste;

	/// <summary>A sorte do mundo, de 0 a 9.999 (so a seed). Abaixo de 1.500, civilizado.</summary>
	public static ulong Sorte(ulong seedDoMundo) => Espaco.Misturar(seedDoMundo, Espaco.Hash64("civilizacao"), 0) % 10_000;

	/// <summary>ESTE MUNDO TEM CIVILIZACAO? A seed, e o nome so pra deixar o Yardrat de fora.</summary>
	public static bool Tem(ulong seedDoMundo, string nome) =>
		!NenhumaDeTeste && Sorte(seedDoMundo) < (ulong)(Fracao * 10_000) && !string.Equals(nome, Yardrat.Nome, StringComparison.Ordinal);

	/// <summary>A mesma pergunta pela zona: so mundo SORTEADO (o espaco tambem e `KindProcedural`, e fica de fora).</summary>
	public static bool Tem(ZoneKey zona) =>
		zona.Kind == ZoneKey.KindProcedural && !Espaco.EhEspaco(zona) && Tem(zona.Seed, zona.Name);

	/// <summary>A mesma pergunta pelo corpo da carta estelar.</summary>
	public static bool Tem(PlanetaNoEspaco p) => !p.Premade && Tem(p.Seed, p.Nome);

	/// <summary>Quantos habitantes a cidade tem.</summary>
	public static int Habitantes(CidadeProcedural c) => c.Casas.Count * HabitantesPorCasa + HabitantesNaPraca;
}
