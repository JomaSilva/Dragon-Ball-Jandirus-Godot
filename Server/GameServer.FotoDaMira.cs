using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// OS GANCHOS DO ROBO DA MIRA DO KI (`--diagmira`, `Client/RoboDaMiraDeKi.cs`) -- so o que o cliente nao tem
/// como fazer sozinho: por um alvo no mundo, anda-lo, e ler o tiro e o olhar do lado de ca.
///
/// O QUE O ROBO FAZ PELO FIO, e por isso NAO esta aqui: marcar o alvo (`C2S.Alvo`), atirar (`C2S.Habilidade`) e
/// inventar a tecnica na mesa (`ca_*`). A regra que se fotografa e a de producao; estes ganchos so montam a cena.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// O ALVO DA FOTO, a `tiles` do dono. NAO e marcado aqui: quem marca e o cliente. O corpo de foto nao tem
	/// cerebro nem tela -- fica onde for posto.
	/// </summary>
	internal int AlvoDaFotoDaMira(int dono, Vec2 tiles)
		=> ForjarCorpoDeFoto(dono, tiles * ZoneCollision.TileSize, "Foto: o marcado", 200_000, comEscada: false);

	/// <summary>Poe o alvo da foto a `tiles` do dono (o corpo de foto nao anda sozinho).</summary>
	internal void PorOAlvoDaMira(int dono, int alvo, Vec2 tiles)
	{
		if (!_players.TryGetValue(dono, out ServerPlayer? pl) || !_players.TryGetValue(alvo, out ServerPlayer? a)) return;
		a.Pos = pl.Pos + tiles * ZoneCollision.TileSize;
	}

	/// <summary>Anda o alvo da foto estes pixels.</summary>
	internal void AndarOAlvoDaMira(int alvo, Vec2 passo)
	{
		if (_players.TryGetValue(alvo, out ServerPlayer? a)) a.Pos += passo;
	}

	/// <summary>O tiro mais novo deste dono, visto do servidor.</summary>
	internal (bool Achou, int Id, Vec2 Cabeca, Vec2 Cauda, Vec2 Rumo, Vec2 Saida, int Alvo, bool Encostado) TiroDaMira(int dono)
	{
		if (!_players.TryGetValue(dono, out ServerPlayer? pl)) return default;

		Projetil? novo = null;
		foreach (Projetil p in ProjeteisDaZona(pl.Zone.Hash))
			if (p.Dono == dono && p.Vivo && (novo == null || p.Id > novo.Id)) novo = p;
		return novo == null
			? default
			: (true, novo.Id, novo.Pos, novo.Cauda, novo.Rumo, novo.RumoDaSaida, novo.Alvo, novo.Encostado);
	}

	/// <summary>Pra onde o servidor diz que este corpo olha, e onde ele esta.</summary>
	internal (Facing Olhar, Vec2 Pos) CorpoDaMira(int id)
		=> _players.TryGetValue(id, out ServerPlayer? pl) ? (pl.Facing, pl.Pos) : (Facing.South, default);

	/// <summary>O verbo do raio teleguiado que este corpo inventou ("" = nenhum), e quantos pontos ele custou.</summary>
	internal (string Verbo, int Gasto) RaioTeleguiadoDaMira(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return ("", 0);
		TecnicaCustomizada? t = pl.Customizadas.Find(x => x.Criada && x.Tipo == TipoDeProjetil.Beam && x.Teleguiado);
		return t == null ? ("", 0) : (t.Verbo, t.Gasto);
	}
}
