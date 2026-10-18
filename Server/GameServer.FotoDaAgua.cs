using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// OS GANCHOS DA FOTO DA AGUA ULTRA DIVINA (`--diagaguasagrada`, `Client/RoboDaAguaSagrada.cs`, 2026-10-16).
///
/// O dono: *"Agua sagrada do korin mostra o bp mesmo sem scouter, resolva"*. A regra do gole e da `--aguateste` (sem tela); aqui o
/// robo leva o corpo LOCAL, sem scouter, ate o jarro do Mirante (ao lado do Korin), bebe pelo verbo da tecla E e varre a tela inteira
/// atras do numero. Os ganchos so montam a cena e leem o estado: quem anda as fases e o tique de producao (`TickDaAguaSagrada`).
/// </summary>
public partial class GameServer
{
	/// <summary>LEVA O CORPO AO JARRO DO MIRANTE, um tile a leste dele (o lado da `--aguateste`), pela troca de zona de producao.</summary>
	internal bool LevarAoJarroNaFoto(int id)
	{
		ZoneKey mirante = ZoneKey.Premade("Lookout");
		if (!_players.ContainsKey(id) || _noChao.FirstOrDefault(o => o.Tipo == AguaSagrada.Tipo && o.Zona.Equals(mirante)) is not { } jarro)
			return false;
		MoveToZone(id, mirante, new Vec2(jarro.X + ZoneCollision.TileSize, jarro.Y));
		return true;
	}

	/// <summary>
	/// O CORPO PRONTO PRA BEBER: um BP que nao se confunde com nenhum outro numero da tela (pra varredura achar), ABAIXO do proprio teto
	/// (a agua so da BP a quem esta abaixo dele, `BP &lt; relBPmax`), e a moeda a favor. Devolve o BP e o teto.
	/// </summary>
	internal (double Bp, double Teto) PrepararOGoleNaFoto(int id, double bp)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return (0, 0);
		Jandirus.Core.Stats.Fighter f = pl.Ficha;
		f.relBPmax = Math.Max(f.relBPmax, bp * 3);
		f.BP = bp;
		f.Statify();
		RepercutirPoder(pl);
		MandarFicha(pl);
		SorteioDaAguaDeTeste = 0.1;
		return (f.BP, f.relBPmax);
	}

	/// <summary>O poder do corpo, na conta do servidor -- o que a varredura procura na tela -- e se o gole ainda anda.</summary>
	internal (double Bp, double Expresso, bool Gole, bool Bebeu, bool Scouter) AguaNaFoto(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
			? (pl.Ficha.BP, pl.Ficha.expressedBP, _golesDeAgua.Exists(g => g.Id == id), pl.Ficha.sacredwater >= 1, TemScouter(pl))
			: (0, 0, false, false, false);
}
