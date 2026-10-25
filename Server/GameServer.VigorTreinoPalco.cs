using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DO ROBO `--diagvigortreino` (`Client/RoboDoVigorDoTreino.cs`): so posa o estado do corpo -- o vigor e o estomago -- e diz o
/// que o servidor ve. Nenhuma regra mora aqui: o pedido de treinar passa pelo fio de producao (a tecla T), e o vigor no poder pelo
/// estomago de producao (`TickDoEstomago`, sem passada).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// POE O VIGOR deste corpo numa fracao do tanque e o estomago em <paramref name="nutricao"/> (negativo = nao mexe), roda a linha de
	/// cada tique do estomago (o vigor no poder) e manda a ficha. Devolve se achou o corpo.
	/// </summary>
	internal bool VigorDeTeste(int id, double fracao, double nutricao = -1)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		Fighter f = pl.Ficha;
		f.stamina = Math.Clamp(fracao, 0, 1) * f.maxstamina;
		if (nutricao >= 0) f.CurrentNutrition = nutricao;
		TickDoEstomago(pl, 0);
		f.Tick(agoraMs: NowMs());
		MandarFicha(pl);
		return true;
	}

	/// <summary>O que o servidor diz deste corpo agora: se treina, o vigor (fracao), a nutricao, o `staminadeBuff` e a % efetivo.</summary>
	internal (bool Treina, double Vigor, double Nutricao, double DeBuff, double Inteireza) VigorDoCorpoDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
			? (pl.Ficha.train, pl.Ficha.stamina / Math.Max(pl.Ficha.maxstamina, 1e-9), pl.Ficha.CurrentNutrition, pl.Ficha.staminadeBuff, pl.Ficha.Inteireza)
			: default;
}
