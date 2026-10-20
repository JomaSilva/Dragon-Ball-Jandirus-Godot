using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// OS GANCHOS DO ROBO `--diagcorpofrost` (2026-10-19) -- ver `Client/RoboDoCorpoDoFrost.cs`. O robo e o servidor rodam no mesmo processo
/// (`--host`). A PREMISSA e escrita aqui; a subida e a de producao, o C pelo fio do cliente (`Transformar`).
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// O corpo do robo vira um Frost Demon NORMAL com estes tres corpos (base, 1a e 2a Evolucao), descansando na forma base pelo mesmo
	/// `Catalogo.IdDoPiso` do login. Sem roupa: a regua do robo olha o CORPO, e uma camisa por cima esconderia o que ela mede.
	///
	/// A FORMA BASE E AS EVOLUCOES JA ESTREADAS E A BARRA DA LINHA NO PONTO QUE DISPENSA A CENA (`Cinematicas.Degrau`): a cena cheia de
	/// estreia prende o corpo por ~28 s e pinta poeira e raio por cima dele, e a foto e do corpo, nao da cena. A forma base entra junto
	/// porque o corpo chega nela por `Atual =` (como no login), e o primeiro RECUO pra ela contaria como estreia -- a primeira rodada
	/// fotografou o surto dela, poeira por cima do Koola. A cena das formas e de outra regua.
	/// </summary>
	internal bool VirarFrostDeTeste(int id, IReadOnlyList<string> corpos, double bp)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		Fighter f = pl.Ficha;
		pl.Race = f.Race = FormasDeFrost.Raca;
		pl.Class = f.Class = FormasDeFrost.ClasseNormal;
		pl.Visual.Roupa.Clear();
		pl.Visual.FormasDeFrost = FormasDeFrost.Sanear(FormasDeFrost.ClasseNormal, corpos);
		_visual?.Sanear(pl.Visual, pl.Race, pl.Genero);
		f.BP = bp;

		pl.Forma.Atual = Catalogo.IdDoPiso(Perfil(pl));
		foreach (FormaDef d in Catalogo.Todas)
			if (Catalogo.DegrauDoFrost(d) >= FormasDeFrost.Base) pl.Forma.EstreiaVista.Add(d.IdRede);
		pl.Forma.Maestria.Por(Catalogo.IdDaBaseDoFrost, Cinematicas.MaestriaQueDispensaCena);

		f.Statify();
		AplicarForma(pl);
		f.Ki = f.MaxKi;
		pl.SigAtributos = "";
		TrocarAparencias(pl);
		return true;
	}

	/// <summary>A forma em que o corpo esta, pelo id do catalogo ("" se ele nao esta no mundo).</summary>
	internal string FormaDoCorpoDeTeste(int id) => _players.TryGetValue(id, out ServerPlayer? pl) ? pl.Forma.Atual : "";

	/// <summary>O corpo esta preso numa cena de forma (`EmCena`)? Fora do mundo conta como sem cena.</summary>
	internal bool EmCenaDeTeste(int id) => _players.TryGetValue(id, out ServerPlayer? pl) && EmCena(pl);
}
