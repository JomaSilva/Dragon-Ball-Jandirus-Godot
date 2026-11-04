using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DO ROBO `--diagcargopg` (2026-10-31, o pedido 198i) -- atalhos de montagem pras fotos (o poder e o karma de quem entra, o Nav
/// System na mochila, a ida ao planeta da missao, a derrota do foragido pelo funil). O que a foto mostra continua sendo o jogo: os verbos, o
/// scouter, o botao da carta, o foragido que nasce e a missao cumprida.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O host com o poder e o karma do pedido (50 mil e karma limpo) e um Nav System na mochila.</summary>
	internal void PalcoCP_Candidato(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.BP = PatrulhaGalactica.BpParaEntrar + 5000;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
		pl.Karma = Math.Max(pl.Karma, 10);
		if (pl.Mochila.Quantos(CatalogoDeItens.NavSystem) == 0) pl.Mochila.Guardar(CatalogoDeItens.NavSystem);
		MandarMochila(pl);
		Persistir(pl);
	}

	/// <summary>O poder do host abaixo do requisito (a foto da recusa).</summary>
	internal void PalcoCP_Fraco(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.BP = 1000;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
	}

	/// <summary>A missao de agora (nula = nenhuma), e o foragido dela de pe.</summary>
	internal (string Planeta, byte Tipo, int Foragido) PalcoCP_Missao(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) && FichaOuNulo(pl)?.Missao is { } m ? (m.ZonaNome, m.Tipo, m.Npc) : ("", (byte)0, 0);

	/// <summary>O host vai ao planeta da missao, longe do ponto de chegada.</summary>
	internal void PalcoCP_IrAMissao(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || FichaOuNulo(pl)?.Missao is not { } m) return;
		Vec2 longe = PontoDeNascimento(m.Zona) + new Vec2(28 * ZoneCollision.TileSize, 16 * ZoneCollision.TileSize);
		MoveToZone(id, m.Zona, MapaDaZonaOuCatalogo(m.Zona)?.PontoLivrePerto(longe) ?? longe);
	}

	/// <summary>O foragido da missao a alguns tiles do host, na frente da camera.</summary>
	internal void PalcoCP_ForagidoPerto(int id, float tiles)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || FichaOuNulo(pl)?.Missao is not { Npc: > 0 } m
			|| !_players.TryGetValue(m.Npc, out ServerPlayer? f)) return;
		f.Pos = pl.Pos + new Vec2(tiles * ZoneCollision.TileSize, 0);
	}

	/// <summary>(a foto do C8) o host esta vivo -- a r1 do agente soltosmissao o fotografou de aureola, morto pelos soldados do imperio.</summary>
	internal bool PalcoCP_Vivo(int id) => _players.TryGetValue(id, out ServerPlayer? pl) && !pl.Ficha.dead;

	/// <summary>O host derrota o foragido pelo funil de toda derrota.</summary>
	internal void PalcoCP_DerrotarOForagido(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || FichaOuNulo(pl)?.Missao is not { Npc: > 0 } m
			|| !_players.TryGetValue(m.Npc, out ServerPlayer? f)) return;
		f.Ficha.KO = true;
		AoPerderALuta(f, pl, morreu: false);
	}
}
