using Jandirus.Core.Stats;

namespace Jandirus.Core.Tech;

/// <summary>
/// ============================ O REGENERADOR, EM NUMEROS -- `obj/items/Regenerator` ============================
/// Dono, 2026-10-09: *"as maquinas de regeneraçao nao estao funcionando (veja no DM como elas
/// funcionavam)"*, e em seguida a regra: *"assim como no dm era so entrar na area dela q ela ligava sozinha e
/// curava todos dentro do tile dela, aqui e a mesma coisa porem pra ligar vc tem q apertar E e abrir o menu
/// de interaçoes e apertar pra ligar, curando todos q estiverem no mesmo tile da maquina"*.
///
/// O que o port tinha era um achatado: 3 de vida por segundo num raio de um tile, sem bateria, sem Ki, sem
/// melhoria nenhuma, e sempre ligado. Este arquivo sao os numeros do `Ticker()` e do `Upgrade()` de
/// `Tech/Tier 1.5.dm:55-218`; quem os aplica e `Server/GameServer.Regenerador.cs`.
///
/// ============================ O QUE MUDA EM RELACAO AO DM (DECLARADO) ============================
///   * **TEM INTERRUPTOR** (o pedido do dono): la a maquina aparafusada trabalha sozinha; aqui alguem a
///     liga pelo menu da tecla E. A bateria so gasta curando, como la.
///   * **A CAMPANULA APARECE COM A MAQUINA LIGADA**, e nao so com alguem dentro (`podlayer`, `:127-133`): e
///     o que mostra de longe qual tanque esta trabalhando.
///   * **A DURABILIDADE NAO FOI PORTADA.** A regra dela e `T.gravity > 10*Durability` (`:120`), e o
///     `turf.gravity` do DM nunca sai de zero (so ha duas escritas no jogo, as duas `= 0`): la a melhoria
///     custa zeni e so muda um numero do `Info`.
///   * **AS MUTACOES NAO EXISTEM NESTE PORT** (`:86-89`): a metade do `injuryheal` que as remove ficou de fora.
///   * **O KI NAO PASSA DO TETO.** A conta de la (`:81-83`) soma a eficiencia DUAS vezes quando a primeira
///     soma nao alcanca o teto, e por isso pode estourar ate um `efficiency` acima dele. Aqui Ki acima do
///     teto e carga comprimida e sangra (`CargaDeKi`), entao a soma e a mesma e o estouro e aparado.
///   * A CURA SAI NO PULSO, e nao dois segundos depois dele (`spawn(20)`, `:79`).
/// ================================================================================================
/// </summary>
public static class Regenerador
{
	/// <summary>
	/// DEFEITO INJETADO (bancada): o tanque cura mesmo DESLIGADO -- o estado de antes de 2026-10-09, em que
	/// nao havia interruptor. Lido pela propria linha do interruptor, em `GameServer.PulsoDosTanques`.
	/// </summary>
	public static bool DesligadoCuraDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): quem esta no tile VIZINHO tambem e curado -- a caixa de um tile de raio
	/// do port antigo. Lido pela propria linha do tile, em `GameServer.NoTileDoTanque`.
	/// </summary>
	public static bool VizinhoContaDeTeste;

	/// <summary>O tipo da obra no catalogo (`construcoes.json`).</summary>
	public const string Tipo = "Regenerator";

	/// <summary>A arte do tanque desligado, e a do ligado: a base, e a base com a campanula por cima de quem esta nele.</summary>
	public const string ArteDesligado = "base", ArteLigado = "base+tank";

	/// <summary>DE QUANTO EM QUANTO ELE PULSA -- o `spawn(20) Ticker()` (`:141`): dois segundos.</summary>
	public const double SegundosDoPulso = 20 / TempoDoDm.TiquesPorSegundo;

	/// <summary>
	/// QUANTO TEMPO DO PULSO O PACIENTE NAO GOLPEIA -- o `inregen`: ligado no pulso (`:78`), desligado um
	/// segundo depois pelo `spawn(10)` do pulso seguinte (`:73-74`), e lido pelo portao do soco
	/// (`attack cmn.dm:98`, `!inregen`).
	/// </summary>
	public const double SegundosSemGolpear = 10 / TempoDoDm.TiquesPorSegundo;

	/// <summary>
	/// A VIDA QUE CADA PULSO DA A CADA MEMBRO FERIDO -- `SpreadHeal(3*round(efficiency/2,1), 1)` (`:80`). O
	/// `round` de dois argumentos e o mais-perto (meio pra cima): eficiencia 1 e 2 dao 3, 3 e 4 dao 6, 10 da 15.
	/// </summary>
	public static double CuraPorPulso(int eficiencia) => 3 * DmMath.Round(eficiencia / 2.0, 1);

	/// <summary>
	/// O KI QUE CADA PULSO DA -- `:81-83`. Sao DUAS somas de `1*efficiency` (a que mora dentro do `if` e a
	/// do `else`), e o teto apara. Ver "o Ki nao passa do teto" no cabecalho.
	/// </summary>
	public static double KiPorPulso(int eficiencia) => 2.0 * eficiencia;

	/// <summary>O que um paciente gasta de bateria por pulso -- `Energy -= 0.002*efficiency` (`:84`).</summary>
	public static double BateriaPorPulso(int eficiencia) => 0.002 * eficiencia;

	/// <summary>Abaixo disto a maquina nao cura -- `Energy >= 0.002` (`:75`).</summary>
	public const double BateriaMinima = 0.002;

	/// <summary>
	/// A CHANCE, POR PULSO, DE TRATAR UM FERIMENTO (com a melhoria comprada) -- `prob(min(max(efficiency,1),
	/// 100))` (`:90`), em pontos percentuais. O sorteado e UM membro dos que estao abaixo de
	/// <see cref="FerimentoInteiro"/> ou decepados: o decepado volta, o ferido ganha `1*efficiency` de vida.
	/// </summary>
	public static double ChanceDeFerimento(int eficiencia) => Math.Clamp(eficiencia, 1, 100);

	/// <summary>`if(C.health >= 0.991 * C.maxhealth) continue` (`:94`): acima disto o membro nem entra no sorteio.</summary>
	public const double FerimentoInteiro = 0.991;

	/// <summary>A chance, por pulso, de os nanites recarregarem a bateria -- `prob(NanoCore*0.1)` (`:124`).</summary>
	public static double ChanceDeNanites(int nanites) => nanites * 0.1;

	/// <summary>...e so com a bateria abaixo deste tanto do teto (`Energy &lt; MaxEnergy*0.1`).</summary>
	public const double BateriaDosNanites = 0.1;

	// =====================================================================
	// AS MELHORIAS -- `verb/Upgrade` (`:159-218`)
	// =====================================================================
	/// <summary>
	/// RECARREGAR. O botao do DM escreve `500*MaxEnergy` e COBRA `250*MaxEnergy` (`:167` e `:181`); aqui o
	/// preco dito e o cobrado.
	/// </summary>
	public static double CustoDeRecarga(double bateriaMax) => 250 * bateriaMax;

	/// <summary>`Battery Life`: mais uma carga no teto, e enche (`:186-192`).</summary>
	public static double CustoDeBateria(double bateriaMax) => 500 * bateriaMax;

	/// <summary>`Recovery Speed`: `efficiency += 1` (`:193-198`).</summary>
	public static double CustoDeVelocidade(int eficiencia) => 1000 * eficiencia;

	/// <summary>`Heal Injuries`: uma vez so (`:175-179`).</summary>
	public const double CustoDeFerimentos = 5000;

	/// <summary>`Nano Regeneration` (`:208-213`).</summary>
	public static double CustoDeNanites(int nanites) => 2000 * (nanites + 1);

	/// <summary>...e ela pede este tanto de tecnologia (`usr.techskill>=6`, `:172`).</summary>
	public const double TechDosNanites = 6;

	// =====================================================================
	// O TANQUE DO LABORATORIO DE VEGETA -- `Globals/VegetaCity.dm:103-115`
	// =====================================================================
	/// <summary>
	/// O regenerador que o construtor da cidade poe no meio do primeiro laboratorio, *"turbinado ... (todos
	/// os upgrades)"*: `efficiency = 10` (15 de vida por pulso), `MaxEnergy = Energy = 10`, `NanoCore = 10`
	/// e `injuryheal = 1`. E o unico regenerador que nasce com o mapa.
	/// </summary>
	public const int EficienciaDaCidade = 10, NanitesDaCidade = 10;
	public const double BateriaDaCidade = 10;
}
