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

	// ---- (2026-10-26) o sintetizador de virus saiu do jogo (o pedido 98) -- ver `Jandirus.Core.Tech.Sintetizador` ----
	/// <summary>
	/// Ate quando durava o reforco do LSD (ms; zero = nao ha). Ficou SO PRA DESFAZER O DE QUEM SALVOU SOB EFEITO: o relogio que o tirava
	/// saiu com o sintetizador -- ver <see cref="TirarOLsdAntigo"/>.
	/// </summary>
	public long LsdAte;

	/// <summary>
	/// (2026-10-26) O LSD QUE FICOU NO SANGUE de quem salvou sob efeito antes de o sintetizador sair do jogo: o login desfaz o reforco (o
	/// `Tmagi` e o `Tkiregen`, mais 1 cada um) e zera a marca. Sem isto o reforco de cinquenta segundos ficava pra sempre. Devolve se havia
	/// o que desfazer.
	/// </summary>
	public bool TirarOLsdAntigo()
	{
		if (LsdAte == 0) return false;
		LsdAte = 0;
		Tmagi -= 1;
		Tkiregen -= 1;
		return true;
	}
}
