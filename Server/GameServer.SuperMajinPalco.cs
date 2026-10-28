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
		// (2026-10-28) UM TANQUE QUE SEGURA A FORMA A CENA INTEIRA: desde o dreno em PONTOS (o pedido 93, 2026-10-26) o tanque de fabrica deste
		// Majin de bancada (~110) acaba em ~4 s de Super Majin, e a forma caia antes das fotos -- o S1f..S6 estavam vermelhos desde entao (a r1
		// do `faiscamajin` mediu "Ki 3/110" no S1). Cem vezes o tanque: minutos de forma. So a ficha de bancada, e uma vez.
		if (pl.Ficha.MaxKi < 10_000)
		{
			pl.Ficha.baseKi *= 100;
			pl.Ficha.Statify();
		}
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
	/// <summary>
	/// (2026-10-26) UM TANQUE QUE AGUENTA <paramref name="segundos"/> DA FORMA DE AGORA -- o dreno virou PONTOS de Ki (o pedido 93), e um tique
	/// longo de bancada (um minuto de Super Majin) custa mais que o tanque de fabrica de uma ficha de teste: a forma cairia no meio dele.
	/// O Ki base sobe ate o tanque da forma caber o dreno do tique dez vezes; so cresce, e so em ficha de bancada.
	/// </summary>
	private void AguentarOTiqueDeTeste(ServerPlayer pl, double segundos)
	{
		double custo = pl.Forma.PontosDeDrenoPorSegundo() * Math.Max(segundos, 0) * Math.Max(pl.Racial.Efeito.Dreno, 1);
		if (custo <= 0 || pl.Ficha.MaxKi >= custo * 10) return;
		pl.Ficha.baseKi *= custo * 10 / Math.Max(pl.Ficha.MaxKi, 1e-9);
		pl.Ficha.Statify();
	}

	internal (double Maestria, double Mult, string Falas) PalcoSM_Tique(int id, double maestria, double bp, double segundos)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return (-1, 0, "corpo fora do mundo");
		pl.Ficha.BP = bp;
		pl.Forma.Maestria.Por(SuperMajin.Id, maestria);
		AguentarOTiqueDeTeste(pl, segundos);   // (2026-10-26) o dreno em pontos nao derruba a forma no meio do tique -- ver la
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
