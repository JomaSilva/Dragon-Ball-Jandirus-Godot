namespace Jandirus.Server;

/// <summary>
/// O PALCO DA `--diagformasteclas` (`Client/RoboDasTeclasDeForma.cs`, 2026-10-23): o C duplo de quem tem ki divino e a tecla de forma que
/// vai direto, NA TELA. O robo e o jogador do host; o palco so poe o corpo na condicao de cada foto -- o mesmo corpo da bancada de servidor
/// (`FtMontar`, `GameServer.FormasTeclasTeste.cs`), o ki divino desperto ou nao (o botao aparece e some com ele) e a maestria de uma forma.
/// Quem transforma e o que o robo aperta: o botao da aba Other, o C duplo e a tecla de forma, pelo teclado de verdade.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O CORPO DAS FOTOS: o da bancada de servidor, com o Super Saiyajin a 60% -- acima dos 50% que dispensam a cena, entao o C duplo da foto
	/// e a transformacao instantanea (a cinematica curta prenderia o corpo e a foto sairia no meio dela). A ficha lenta sai de novo: e nela
	/// que o cliente aprende as maestrias (a tela de teclas e o portao do `Atalhos` perguntam por elas).
	/// </summary>
	internal bool PalcoFT_Montar(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		FtMontar(pl);
		pl.Forma.Maestria.Por("ssj1", 60);
		pl.SigAtributos = "";
		return true;
	}

	/// <summary>O KI DIVINO desperto ou adormecido -- o fato `GodKiDesperto` sai no tique, e o botao do C aparece e some com ele.</summary>
	internal void PalcoFT_GodKi(int id, bool desperto)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.godki ??= new Jandirus.Core.Stats.GodKiState();
		pl.Ficha.godki.awakened = desperto;
		pl.Ficha.godki.mastery = desperto ? 40 : 0;
		pl.SigAtributos = "";
	}

	/// <summary>A MAESTRIA de uma forma, e a ficha lenta de novo (o cliente a le pra saber se o corpo "sabe" a forma).</summary>
	internal void PalcoFT_Maestria(int id, string forma, double pct)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Forma.Maestria.Por(forma, pct);
		pl.SigAtributos = "";
	}

	/// <summary>De volta a base pelo "voltar ao normal" de producao (o anuncio vai pra zona), com o tanque cheio.</summary>
	internal void PalcoFT_ABase(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) FtABase(pl);
	}
}
