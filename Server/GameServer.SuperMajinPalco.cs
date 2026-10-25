using Jandirus.Core.Forms;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DA `--diagsupermajin` (`Client/RoboDoSuperMajin.cs`, 2026-10-23): o Super Majin na tela -- o corpo de sempre, a aura rosa, as
/// faiscas e a aba Forms com a barra parada no teto do poder. O robo e o jogador do host; o palco so poe o corpo na condicao de cada foto (a
/// classe, o BP base, a cor pessoal da chama, a maestria) e roda o TIQUE DE PRODUCAO da forma -- quem transforma e o C do proprio cliente.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O CORPO DO ROBO: um Majin de classe Majin (o `--raca Majin` da linha de comando sorteia a classe, e 5% sai Corrupted), no nivel do SSJ1
	/// (2 M de BP base), na base, sem maestria, a chama PESSOAL cravada em azul (a cor pessoal e sorteada no nascimento: um sorteio rosa
	/// tiraria o contraste da foto da chama) e o palco sem povo, ao meio-dia da zona. Devolve a ficha em texto, pro log.
	/// </summary>
	internal string PalcoSM_Preparar(int id, double bp)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return "corpo fora do mundo";
		pl.Class = pl.Ficha.Class = "Majin";
		pl.Ficha.BP = bp;
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Visual.CorAura = new Jandirus.Core.Appearance.Rgb(70, 130, 255);
		ReapresentarAparencia(pl);
		pl.Forma.Maestria.Por(SuperMajin.Id, 0);
		if (!pl.Forma.NaBase) PalcoL_Forma(id, Catalogo.IdBase);
		PalcoL_SemPovo(id);
		CravarMeioDiaDaVariedade(id);
		pl.SigAtributos = "";
		return PalcoSM_Ficha(id);
	}

	/// <summary>
	/// UM TIQUE DE PRODUCAO DA FORMA (`TickDaForma`, o que sobe a maestria pelo funil), com o BP base e a maestria de partida da foto, e o
	/// que o servidor disse ao jogador. A ficha lenta sai de novo (a aba le a maestria dela).
	/// </summary>
	internal (double Maestria, double Mult, string Falas) PalcoSM_Tique(int id, double maestria, double bp, double segundos)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return (-1, 0, "corpo fora do mundo");
		pl.Ficha.BP = bp;
		pl.Forma.Maestria.Por(SuperMajin.Id, maestria);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.CenaSegundos = 0;
		List<string> falas = Ouvir(() => TickDaForma(pl, segundos));
		pl.SigAtributos = "";
		return (pl.Forma.Maestria.De(SuperMajin.Id), pl.Ficha.ssjBuff, string.Join(" | ", falas));
	}

	/// <summary>
	/// O KI ACIMA DE 100% (a chama acende sozinha pelo tique da carga, `CargaDeKi.AuraAcesa`) ou de volta ao tanque cheio (ela apaga). E o
	/// caminho de producao da chama transformada: a forma escolhe a COR, quem acende e o Ki.
	/// </summary>
	internal void PalcoSM_Sobrecarga(int id, bool acesa)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Ficha.Ki = pl.Ficha.MaxKi * (acesa ? 1.3 : 1.0);
	}

	/// <summary>A forma, a maestria do Super Majin, o multiplicador, o BP base e a classe que o servidor ve -- a linha de cada cena no log.</summary>
	internal string PalcoSM_Ficha(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
			? $"raca {pl.Race}, classe {pl.Ficha.Class}, forma {pl.Forma.Atual}, maestria {pl.Forma.Maestria.De(SuperMajin.Id):0.####}%, "
			  + $"x{pl.Ficha.ssjBuff:0.##}, BP base {pl.Ficha.BP:N0}, Ki {pl.Ficha.Ki:0}/{pl.Ficha.MaxKi:0}"
			: "corpo fora do mundo";
}
