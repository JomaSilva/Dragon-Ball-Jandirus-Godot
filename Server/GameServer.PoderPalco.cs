using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DA `--diagpoder` (`Client/RoboDoPoder.cs`): o que o robo de tela precisa do servidor do mesmo
/// processo -- dar a porta a cobaia pelo caminho de producao, e ler a VERDADE do servidor pra comparar com o que
/// a tela escreveu. Nada daqui e chamado em jogo.
/// </summary>
public partial class GameServer
{
	/// <summary>A cobaia de pe, calma e de tanque cheio: o roteiro mede a tela, e nao um corpo caido.</summary>
	public bool PalcoDoPoder_Cobaia(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.Ficha.KO = false;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Ficha.stamina = pl.Ficha.maxstamina;
		return !pl.Ficha.dead;
	}

	/// <summary>
	/// DA O CONTROLE DE KI (Ki Unlocked + Basic Ki Control no nivel 5) pelas MESMAS quatro chamadas do verb de admin
	/// `admin_liberar_ki` (`AdminLiberarKi`, `GameServer.Admin.cs`): o `Power_Control` chega pelo degrau e a lista vai
	/// pro cliente pelo pacote de skills.
	/// </summary>
	public bool PalcoDoPoder_DarAPorta(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		LiberarOKi(pl);
		pl.Niveis.Aplicar(pl.Ficha);
		AplicarPoderes(pl);
		AplicarEfeitos(pl);
		MandarSkills(pl, forcar: true);
		MandarFicha(pl);
		return SabeTecnica(pl, PoderEmUso.VerboDoControle);
	}

	/// <summary>A verdade do servidor sobre o poder em uso deste corpo, ou nulo se ele nao existe.</summary>
	public (int Porcento, bool Escondido, bool Relaxado, bool SabeBaixar, double Expresso)? PalcoDoPoder_Verdade(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
			? (PoderEmUso.Porcento(pl.Ficha), PoderEmUso.Escondido(pl.Ficha), PoderEmUso.Relaxado(pl.Ficha),
			   SabeTecnica(pl, PoderEmUso.VerboDoControle), pl.Ficha.expressedBP)
			: null;
}
