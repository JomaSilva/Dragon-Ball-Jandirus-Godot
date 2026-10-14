namespace Jandirus.Core.Stats;

/// <summary>O que a tech de bolso da v0.2.1 escreve na ficha. A ficha inteira vai pro save: campo novo persiste sozinho.</summary>
public partial class Fighter
{
	/// <summary>
	/// A PERICIA DE PESCA -- o `FishingSkill` do original (`Lifeskill Masteries.dm:4`). La ela nasce em zero e a
	/// maestria de vida "Fishing" a poe em 1 ao ser adquirida (`:26-28`); este port nao tem as maestrias de vida, e
	/// todo mundo nasce sabendo o basico. Sobe um a cada peixe pescado (`Fishing.dm:48`). Ver `Jandirus.Core.Tech.Pesca`.
	/// </summary>
	public double FishingSkill = 1;

	/// <summary>
	/// QUANTAS PECAS DE DRENO JA ESTAO SOMADAS NO `KiMod` desta ficha (zero, uma ou duas). E o que faz o bonus das
	/// luvas e das botas de dreno seguir o que esta vestido sem nunca dobrar: o servidor compara este numero com a
	/// mochila e soma ou tira a diferenca (`GameServer.AcertarODreno`). Ver `Jandirus.Core.Tech.DrenoDeEnergia`.
	/// </summary>
	public int drenoVestido;

	// ---- (v0.2.1, lote T5) o que o sintetizador de virus escreve -- ver `Jandirus.Core.Tech.Sintetizador` ----
	/// <summary>`mob/var/Mutations` (`Mutations.dm:1`): tira um fio de vida por segundo, passa pelo golpe, e zera na morte.</summary>
	public int Mutations;

	/// <summary>`usr.VirusesConsumed`: os virus que este corpo ja tomou (cada um so uma vez).</summary>
	public List<string> VirusesConsumed = [];

	/// <summary>Ate quando dura o reforco do LSD (ms do relogio do servidor; zero = nao ha). O `TempBuff(..., 500)` de la.</summary>
	public long LsdAte;

	/// <summary>A hora marcada pela agulha da Insta-Death (ms do relogio do servidor; zero = nao ha).</summary>
	public long MorteMarcadaEm;
}
