using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagprovacargo` (2026-10-19) -- ver `Client/RoboDaProvaDoCargo.cs`. O robo e o servidor rodam no
/// mesmo processo (`--host`): o palco deixa o robo apto pro cargo (o BP e o karma que ele pede) e faz as duas quedas que o robo nao
/// tem como fazer sozinho a tempo de uma foto (o espirito derrubado PELAS MAOS dele, e ele mesmo nocauteado). O resto e producao: o
/// botao "Reivindicar" da aba Cargos, o pacote `C2S.Cargo`, o espirito, a luta e o juiz de 1 Hz.
/// </summary>
public partial class GameServer
{
	/// <summary>Solta os cargos do robo, encerra a prova dele, esquece a vergonha e o poe de pe.</summary>
	internal void PalcoPcg_Limpar(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		foreach (string vaga in _tronos.Where(kv => string.Equals(kv.Value, pl.Conta, StringComparison.OrdinalIgnoreCase))
									   .Select(kv => kv.Key).ToList())
			_tronos.Remove(vaga);
		foreach (ProvaEmCurso p in _provasDoCargo.Values.Where(p => p.Desafiante == id).ToList())
		{
			_provasDoCargo.Remove(p.Cargo);
			_espiritosDoCargo.Remove(p.Espirito);
			if (_players.TryGetValue(p.Espirito, out ServerPlayer? e)) RemoverNpc(e);
		}
		if (pl.Assinatura.Length > 0) _vergonhaDoCargo.Remove(pl.Assinatura);
		if (pl.Ficha.KO) pl.Combate.Levantar();
		SalvarCargos();
		ReconciliarDadiva(pl);
		MandarCargos(pl);
	}

	/// <summary>O robo fica apto pro Eremita Tartaruga (karma 25+) ou pro Grou (karma 0 ou menos): 1M de BP base e o karma pedido.</summary>
	internal void PalcoPcg_AptoPara(int id, int karma)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		PalcoPcg_Limpar(id);
		pl.Ficha.BP = 1_000_000;
		pl.Karma = karma;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
		MandarFicha(pl);
		MandarCargos(pl);
	}

	/// <summary>O espirito da prova do robo cai PELAS MAOS dele: a marca do agressor (a do golpe) e o nocaute. Falso sem prova.</summary>
	internal bool PalcoPcg_DerrubarOEspirito(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		if (_provasDoCargo.Values.FirstOrDefault(p => p.Desafiante == id) is not { } prova
			|| !_players.TryGetValue(prova.Espirito, out ServerPlayer? e)) return false;
		MarcarAgressao(e, pl);
		e.Combate.Nocautear(10);
		return true;
	}

	/// <summary>O robo cai (a derrota da foto).</summary>
	internal void PalcoPcg_Nocautear(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Combate.Nocautear(10);
	}

	/// <summary>O robo levanta.</summary>
	internal void PalcoPcg_Levantar(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.Ficha.KO) pl.Combate.Levantar();
	}

	/// <summary>A prova do robo: se ha uma, a quantos tiles o espirito esta e a vida do robo (0..100).</summary>
	internal (bool Prova, double Tiles, double Vida) PalcoPcg_Estado(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return (false, -1, 0);
		if (_provasDoCargo.Values.FirstOrDefault(p => p.Desafiante == id) is not { } prova
			|| !_players.TryGetValue(prova.Espirito, out ServerPlayer? e)) return (false, -1, pl.Ficha.HP);
		double tiles = e.Zone.Hash == pl.Zone.Hash ? (e.Pos - pl.Pos).Length / ZoneCollision.TileSize : -1;
		return (true, tiles, pl.Ficha.HP);
	}
}
