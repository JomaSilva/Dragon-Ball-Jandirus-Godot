using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Core.Combat;

/// <summary>
/// **O RABO PRESO** -- agarrar um Saiyajin por tras e pega-lo pelo rabo.
///
/// ============================ NAO E PORTE: E PEDIDO DO DONO (2026-10-12) ============================
/// *"agarrar um personagem por tras (pra jogar ou carregar) se esse personagem for um saiyajin e tiver um rabo, vc
/// vai agarrar o rabo dele o deixando fraco (ele perde grande parte do poder efetivo, assim como era no anime, e
/// fica impossibilitado de se mover), mas os saiyajins vao ter uma maestria disso ao qual quanto mais tempo seguram
/// o rabo dele ou quanto mais vezes isso aconteceu, menos isso afeta ele, ate ele ficar imune"*.
///
/// No DM o rabo so entra no agarrao como alvo de golpe (`CombatMovement.dm:309-313`: o soco letal por tras o
/// arranca). Nao ha pegada pelo rabo la.
/// ====================================================================================================
///
/// AS TRES PECAS:
///   * **QUANDO PEGA** (<see cref="Pega"/>): o corpo tem rabo de pe, quem agarra esta ATRAS dele (os dois olham
///     pro mesmo lado -- o mesmo `M.dir==dir` do soco que arranca o rabo) e ele ainda nao e imune;
///   * **O QUE FAZ** (<see cref="FatorDePoder"/>): o poder expresso cai a <see cref="PoderQueSobra"/> e sobe em
///     linha reta com a maestria ate o inteiro. O corpo preso nao golpeia (o servidor o mantem atordoado) e,
///     como todo agarrado, nao anda -- mas SE DEBATE, com o poder que sobrou: ninguem fica preso pra sempre;
///   * **A MAESTRIA** (0 a 100, salva na ficha -- `Fighter.maestriaDoRabo`): cada pegada vale
///     <see cref="MaestriaPorPegada"/> e cada segundo com o rabo na mao de alguem vale
///     <see cref="MaestriaPorSegundo"/>. Aos 100 o corpo e IMUNE: agarrado por tras, e um agarrao comum.
/// </summary>
public static class RaboPreso
{
	/// <summary>O poder que sobra com o rabo preso e maestria zero: um decimo -- "grande parte do poder".</summary>
	public const double PoderQueSobra = 0.10;

	/// <summary>A maestria em que o rabo deixa de ser fraqueza.</summary>
	public const double MaestriaDaImunidade = 100;

	/// <summary>Quanto cada pegada ensina: 25 pegadas, sozinhas, levam a imunidade.</summary>
	public const double MaestriaPorPegada = 4;

	/// <summary>Quanto cada segundo de rabo preso ensina: 200 s, sozinhos, levam a imunidade.</summary>
	public const double MaestriaPorSegundo = 0.5;

	/// <summary>DEFEITO INJETADO (bancada `--v020teste`): agarrar por tras nunca pega o rabo -- o jogo de antes. Falso em jogo, sempre.</summary>
	public static bool DesligadoDeTeste;

	public static bool Imune(Fighter f) => f.maestriaDoRabo >= MaestriaDaImunidade;

	/// <summary>
	/// ESTA PEGADA E PELO RABO? <paramref name="temRabo"/> e o rabo DE PE (nao arrancado) de quem e agarrado;
	/// "por tras" e os dois corpos virados pro mesmo lado.
	/// </summary>
	public static bool Pega(bool temRabo, Facing deQuemAgarra, Facing deQuemEAgarrado, Fighter agarrado) =>
		!DesligadoDeTeste && temRabo && deQuemAgarra == deQuemEAgarrado && !Imune(agarrado);

	/// <summary>
	/// QUANTO DO PODER EXPRESSO SOBRA com o rabo preso: <see cref="PoderQueSobra"/> sem maestria, o inteiro na
	/// imunidade, e uma reta entre os dois. E o que o `Fighter.raboPresoMod` recebe.
	/// </summary>
	public static double FatorDePoder(double maestria)
	{
		double m = Math.Clamp(maestria / MaestriaDaImunidade, 0, 1);
		return PoderQueSobra + (1 - PoderQueSobra) * m;
	}

	/// <summary>A maestria depois de mais uma pegada.</summary>
	public static double ComMaisUmaPegada(double maestria) => Math.Min(maestria + MaestriaPorPegada, MaestriaDaImunidade);

	/// <summary>A maestria depois de <paramref name="segundos"/> com o rabo na mao de alguem.</summary>
	public static double ComMaisTempo(double maestria, double segundos) =>
		Math.Min(maestria + MaestriaPorSegundo * Math.Max(segundos, 0), MaestriaDaImunidade);
}
