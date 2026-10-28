using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PLANETA YARDRAT NO SERVIDOR (dono, 2026-10-27, o pedido 109) -- o povo e o mestre que ensina o Instant Transmission. Qual planeta e,
/// e por que ele nao aparece na carta: `Core/World/Yardrat.cs`. Aqui:
///   * o POVO nasce com o chao (`PovoarOYardrat`, chamado quando o mundo gerado fica pronto): o mestre perto da chegada e os habitantes
///     espalhados em volta, todos Yardrat. Um mundo com gente nao e podado, entao eles ficam ate a parada do servidor; o chao que nasce de
///     novo (outro boot) os poe de novo;
///   * o MESTRE e o terceiro corpo com menu pela tecla E (`S2C.DivinoPerto`, depois do Guru e antes do habitante comum): "Aprender o
///     Instant Transmission" ensina a skill de graca a quem ainda nao a sabe (`AprenderComOMestreYardrat`).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Ate quantos tiles da chegada o povo nasce.</summary>
	private const int RaioDoPovoYardrat = 14;

	/// <summary>Este corpo e o mestre do Yardrat?</summary>
	private static bool EhOMestreYardrat(ServerPlayer p) => p.Peer == null && p.Papel?.Molde.Id == Yardrat.MoldeDoMestre;

	/// <summary>
	/// O YARDRAT NASCE POVOADO: o mestre perto da chegada e os habitantes em volta. So na zona do Yardrat, e so se o mestre ainda nao esta
	/// la (o chao que ficou carregado nao ganha um segundo povo).
	/// </summary>
	private void PovoarOYardrat(ZoneKey zona)
	{
		if (!string.Equals(zona.Name, Yardrat.Nome, StringComparison.Ordinal)) return;
		if (ZoneList(zona.Hash).Any(EhOMestreYardrat)) return;

		Vec2 centro = ChegadaDaZonaGerada(zona) ?? PontoDeNascimento(zona);
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		Vec2 Perto(ulong lugar, int raio)
		{
			if (mapa == null || raio == 0) return mapa?.PontoLivrePerto(centro) ?? centro;
			var r = new Random((int)(Espaco.Misturar(zona.Hash, lugar, 0x59A7) & 0x7FFFFFFF));
			var desejado = new Vec2(centro.X + r.Next(-raio, raio + 1) * ZoneCollision.TileSize, centro.Y + r.Next(-raio, raio + 1) * ZoneCollision.TileSize);
			return mapa.PontoLivrePerto(desejado);
		}

		int nasceram = 0;
		if (NascerNpc(Yardrat.MoldeDoMestre, zona, Perto(0, 2), 0) != null) nasceram++;
		for (int i = 1; i <= Yardrat.Habitantes; i++)
			if (NascerNpc(Yardrat.MoldeDoPovo, zona, Perto((ulong)i, RaioDoPovoYardrat), (ulong)i) != null) nasceram++;
		GD.Print($"[server] o planeta {Yardrat.Nome} nasceu povoado: {nasceram} corpo(s) (o mestre e {Yardrat.Habitantes} habitante(s))");
	}

	/// <summary>O mestre do Yardrat de pe ao alcance da tecla E deste jogador, e o menu dele -- ou (0, "", "").</summary>
	private (int Corpo, string Tipo, string Nome) MestreYardratAoAlcance(ServerPlayer pl)
	{
		if (!string.Equals(pl.Zone.Name, Yardrat.Nome, StringComparison.Ordinal)) return (0, "", "");
		ServerPlayer? mestre = MestreYardratPerto(pl);
		return mestre != null ? (mestre.Id, Yardrat.Tipo, mestre.Name) : (0, "", "");
	}

	/// <summary>O mestre de pe, ao alcance da tecla E deste jogador, ou nulo.</summary>
	private ServerPlayer? MestreYardratPerto(ServerPlayer pl)
	{
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (EhOMestreYardrat(o) && !o.Ficha.dead && !o.Ficha.KO && AoAlcanceDaTeclaE(pl, o)) return o;
		return null;
	}

	/// <summary>
	/// "APRENDER O INSTANT TRANSMISSION" (a tecla E do mestre): de graca, a quem ainda nao o sabe. Longe, ou com o mestre caido, ele nao
	/// ensina.
	/// </summary>
	private void AprenderComOMestreYardrat(ServerPlayer pl)
	{
		ServerPlayer? mestre = MestreYardratPerto(pl);
		if (mestre == null)
		{
			ServerPlayer? caido = ZoneList(pl.Zone.Hash).FirstOrDefault(o => EhOMestreYardrat(o) && AoAlcanceDaTeclaE(pl, o));
			Avisar(pl, caido != null ? $"{caido.Name} precisa estar de pé pra ensinar." : "não há mestre Yardrat ao seu alcance.");
			return;
		}
		if (pl.Livro.Sabe(Yardrat.SkillDoMestre))
		{
			Avisar(pl, $"você já sabe o Instant Transmission -- {mestre.Name} não tem mais nada a te ensinar.");
			return;
		}
		pl.Livro.Dar(Yardrat.SkillDoMestre);
		MandarSkills(pl);
		Avisar(pl, $"{mestre.Name} te ensina o Instant Transmission: concentre-se no ki de alguém e vá até ele.");
		GD.Print($"[server] {pl.Name} aprendeu o Instant Transmission com {mestre.Name} (Yardrat)");
	}
}
