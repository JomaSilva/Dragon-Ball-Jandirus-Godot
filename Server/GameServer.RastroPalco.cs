using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagsenserastro` (2026-10-19) -- ver `Client/RoboDoRastro.cs`. O robo e o servidor rodam no mesmo processo
/// (`--host`): o palco poe o Sense no livro do robo, no nivel do PLANETA (a zona inteira, e so ela), e tres corpos em volta -- o Alvo e a
/// Angela (com acento, pra busca sem acento), que o robo conhece e a lista manda pelo NOME, e o Estranho, que a lista manda pela
/// ASSINATURA. O resto e producao: a lista do `SentidosDe`, o tique de 1 Hz e o snapshot da zona.
/// </summary>
public partial class GameServer
{
	/// <summary>Os corpos do palco, pelo nome com que o robo os pede.</summary>
	private readonly Dictionary<string, ServerPlayer> _corposDoRastro = new(StringComparer.Ordinal);

	/// <summary>Monta o palco em volta do robo: o Sense no nivel do planeta e os tres corpos. Falso sem jogador.</summary>
	internal bool PalcoRS_Montar(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu)) return false;
		PalcoSN_Sense(id, AlcancePlaneta);
		Vec2 c = eu.Pos;
		ServerPlayer Conhecido(ServerPlayer o)
		{
			eu.Social.Fotografar(o.Assinatura, o.Name, o.Race, "Male", 25, NowMs());
			return o;
		}
		_corposDoRastro["Alvo"] = Conhecido(StCorpo("Alvo", Em(c, 20, 0), 9_000));
		_corposDoRastro["Ângela"] = Conhecido(StCorpo("Ângela", Em(c, -10, 0), 9_000));
		_corposDoRastro["Estranho"] = StCorpo("Estranho", Em(c, 0, 12), 9_000);
		// QUEM CHEGA NA ZONA SE APRESENTA (o `PeerLook` com o nome): e o que poe o corpo no `_nomes` do cliente, de onde a seta tira a
		// posicao exata de quem a lista trouxe com nome (`World.PosicaoDoUnicoComONome`). E o mesmo pacote de quem entra andando.
		foreach (ServerPlayer o in _corposDoRastro.Values) TrocarAparencias(o);
		return true;
	}

	/// <summary>A assinatura de um corpo do palco ("" se nao ha) -- o robo procura o Estranho por ela.</summary>
	internal string PalcoRS_Assinatura(string nome) => _corposDoRastro.TryGetValue(nome, out ServerPlayer? o) ? o.Assinatura : "";

	/// <summary>Poe o corpo a (dx, dy) tiles do robo, na zona DELE -- trazendo-o de volta se estava em outra.</summary>
	internal void PalcoRS_Mover(int id, string nome, int dx, int dy)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu) || !_corposDoRastro.TryGetValue(nome, out ServerPlayer? o)) return;
		if (o.Zone.Hash != eu.Zone.Hash)
		{
			ZoneList(o.Zone.Hash).Remove(o);
			o.Zone = eu.Zone;
			ZoneList(o.Zone.Hash).Add(o);
			o.Pos = Em(eu.Pos, dx, dy);
			TrocarAparencias(o);
			return;
		}
		o.Pos = Em(eu.Pos, dx, dy);
	}

	/// <summary>Leva o corpo pra Namek: fora do alcance do Sense no nivel do planeta -- o rastro se perde.</summary>
	internal void PalcoRS_LevarPraLonge(string nome)
	{
		if (!_corposDoRastro.TryGetValue(nome, out ServerPlayer? o)) return;
		ZoneList(o.Zone.Hash).Remove(o);
		o.Zone = ZoneKey.Premade("Namek");
		o.Pos = new Vec2(ZoneCollision.TileSize * 10 + 16, ZoneCollision.TileSize * 10 + 16);
		ZoneList(o.Zone.Hash).Add(o);
	}

	/// <summary>Tira a skill Sense do robo -- o bit `Poder.Sense` apaga pelo `AplicarPoderes`, e a lista para de chegar.</summary>
	internal void PalcoRS_TirarOSense(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Livro?.Sabe(Jandirus.Core.Skills.SenseEmNiveis.Path) != true) return;
		pl.Livro.Esquecer(Jandirus.Core.Skills.SenseEmNiveis.Path);
		AplicarPoderes(pl);
		MandarSkills(pl, forcar: true);
		MandarFicha(pl);
	}

	/// <summary>Tira os corpos e a skill (o `PalcoSN_Limpar` do robo dos sentidos ja faz as duas coisas).</summary>
	internal void PalcoRS_Limpar(int id)
	{
		_corposDoRastro.Clear();
		PalcoSN_Limpar(id);
	}
}
