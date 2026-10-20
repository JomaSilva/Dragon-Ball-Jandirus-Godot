using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagpatriarca` (2026-10-19): achar o Grande Patriarca que o POVOAMENTO pos em Namek e levar o jogador
/// ate ele. O Guru nao e forjado: o robo espera o de producao nascer, e a primeira foto e a prova de que ele nasceu sozinho, perto do berco.
/// </summary>
public partial class GameServer
{
	/// <summary>O Guru de pe que o povoamento pos em Namek, ou 0 enquanto ele nao nasceu.</summary>
	internal int PalcoPt_OGuruDeNamek()
	{
		foreach (ServerPlayer p in ZoneList(ZoneKey.Premade("Namek").Hash))
			if (EhOPatriarca(p) && !p.Ficha.dead && !p.Ficha.KO) return p.Id;
		return 0;
	}

	/// <summary>A quantos tiles (por eixo, o maior dos dois) o Guru esta do ponto de nascer de Namek; -1 sem ele.</summary>
	internal int PalcoPt_TilesDoBerco(int guru)
	{
		if (!_players.TryGetValue(guru, out ServerPlayer? g)) return -1;
		(int bx, int by) = CelulaPt(PontoDeNascimento(g.Zone));
		(int gx, int gy) = CelulaPt(g.Pos);
		return Math.Max(Math.Abs(gx - bx), Math.Abs(gy - by));
	}

	/// <summary>
	/// LEVA O JOGADOR ATE O GURU pela porta de producao (`MoveToZone`): a um tile dele -- ao sul, ou no primeiro vizinho que serve de chao
	/// --, e com a reputacao com o povo dele neutra (o APPDATA da bancada e limpo, mas o palco nao conta com isso). Devolve se deu.
	/// </summary>
	internal bool PalcoPt_AoLadoDoGuru(int eu, int guru)
	{
		if (!_players.TryGetValue(eu, out ServerPlayer? pl) || !_players.TryGetValue(guru, out ServerPlayer? g)) return false;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(g.Zone);
		const float t = ZoneCollision.TileSize;
		Vec2 onde = g.Pos + new Vec2(0, t);
		foreach (Vec2 d in new[] { new Vec2(0, t), new Vec2(t, 0), new Vec2(-t, 0), new Vec2(0, -t) })
		{
			(int cx, int cy) = CelulaPt(g.Pos + d);
			if (mapa != null && !mapa.ServeDeChao(cx, cy)) continue;
			onde = g.Pos + d;
			break;
		}
		string povo = g.Berco.Natal.Length > 0 ? g.Berco.Natal : g.Zone.Name;
		if (_reputacao.TryGetValue(povo, out Dictionary<string, double>? livro)) livro.Remove(pl.Conta);
		MoveToZone(pl.Id, g.Zone, onde);
		return true;
	}
}
