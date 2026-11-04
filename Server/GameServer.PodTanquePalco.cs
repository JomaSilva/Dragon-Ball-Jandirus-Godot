namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagpodtanque` (2026-10-31, os pedidos 207-209): o bolso, o pod ao lado e o que o servidor sabe do shift e do
/// tanque. Os gestos sob teste passam pelas portas de producao (a tecla E e os botoes dela, o input do shift, o relog pelo fio); o palco so
/// arma o lugar. O resto (o pod no ar e no espaco, a Capital Ship ao leme, a limpeza) e o palco do `--diagcapital` (`GameServer.CapitalPalco.cs`).
/// </summary>
public partial class GameServer
{
	/// <summary>Tecnologia, zeni, recursos e o Ki cheio no corpo -- e a carteira sai pro cliente.</summary>
	internal void PalcoPT_Bolso(int id, double tech, double zeni, double recursos)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.techskill = tech;
		pl.Ficha.Zeni = zeni;
		pl.Ficha.Recursos = recursos;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		MandarCarteira(pl);
		MandarFicha(pl);
	}

	/// <summary>O Ki do corpo de volta ao cheio (antes de medir se o shift o gasta).</summary>
	internal void PalcoPT_KiCheio(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		MandarFicha(pl);
	}

	/// <summary>Um pod do corpo, parado a um tile a leste dele (some no `PalcoCS_Limpar`). Devolve o id.</summary>
	internal int PalcoPT_Pod(int id) => _players.TryGetValue(id, out ServerPlayer? pl) ? PalcoCS_NovaNave(pl, "Spacepod", 1f).Id : 0;

	/// <summary>Os degraus do tanque, o nivel e o tanque cheio desta nave (degraus -1 = nao ha nave com esse id).</summary>
	internal (int Degraus, double Nivel, double Capacidade) PalcoPT_Tanque(int nave) =>
		_naves.FirstOrDefault(n => n.Id == nave) is { } n ? (n.DegrausDoTanque, NivelDoTanque(n), CapacidadeDe(n)) : (-1, 0, 0);

	/// <summary>O que o servidor concedeu ao shift deste corpo no ultimo pacote -- a corrida e a marcha rapida do voo --, o Ki e o Ki maximo.</summary>
	internal (bool Correndo, bool VooRapido, double Ki, double MaxKi) PalcoPT_Shift(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? (pl.Correndo, pl.VooRapido, pl.Ficha.Ki, pl.Ficha.MaxKi) : default;
}
