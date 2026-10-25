using Jandirus.Core.Forms;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DA `--diagpreludio` (`Client/RoboDoPreludioDoSsj2.cs`, 2026-10-24): o robo no SSJ1 dominado, com o poder do SSJ2 pago e a ESTREIA
/// do 2 por ver -- quem transforma e o C do proprio cliente. Nada daqui e chamado em jogo.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// O CORPO DO ROBO: Saiyajin de classe Normal (o `--raca Saiyan` sorteia a classe, e o Legendary nao tem o SSJ2), no SSJ1 com a maestria
	/// cheia e sem cena (`PalcoL_Forma`), com o BP base entre a porta do SSJ2 e a do SSJ3 (`DarPoderPraFormaDeTeste`) e a estreia do 2 POR VER
	/// -- tirada da lista de novo, que e o que deixa o contra-exemplo rodar a estreia uma segunda vez. Sem povo, ao meio-dia. Devolve a forma
	/// em que o corpo ficou.
	/// </summary>
	internal string PalcoPS_NoSsj1(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return "";
		pl.Class = pl.Ficha.Class = "Normal";
		// o C do SSJ1 vai direto pro SSJ2: com os grades ligados (a preferencia de fabrica do jogador) ele passa pelo Grade 2 antes -- a r1
		// desta bancada caiu ai, com a estreia do Grade 2 no lugar da do SSJ2
		pl.Forma.GradesLigados = false;
		string forma = PalcoL_Forma(id, "ssj1");
		DarPoderPraFormaDeTeste(id, "ssj2", "ssj3");
		// O 2 LIBERADO E COM A ESTREIA DEVENDO (`Forma.Liberar`, o gesto de quem destrava uma forma por fora da escada): sem isto o C
		// recusa -- o tronco Saiyajin so desperta o degrau novo pela raiva --, e a r2 desta bancada ficou parada no SSJ1
		pl.Forma.Liberar("ssj2");
		pl.Forma.EstreiaVista.Remove(Catalogo.Rede("ssj2"));
		pl.Forma.Maestria.Por("ssj2", 0);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		PalcoL_SemPovo(id);
		CravarMeioDiaDaVariedade(id);
		pl.SigAtributos = "";
		return forma;
	}
}
