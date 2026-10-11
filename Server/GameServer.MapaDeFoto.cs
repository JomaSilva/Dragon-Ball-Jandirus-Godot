using Jandirus.Core.Items;
using Jandirus.Core.Magic;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DA `--diagmapa` (o robo do mapa da aba World e do disco do radar) -- o que so o servidor sabe
/// montar: pousar o corpo num planeta GERADO de seed escolhida, dar e tirar o Dragon Radar, e por um set de
/// esferas acordadas em volta do corpo. Tudo passa pelas portas de producao que existem (`PousarEmProcedural`,
/// a mochila, o `MandarEsferas`); so o set e posto direto na lista, porque erguer uma estatua de verdade
/// pede raca, cla e dominio que a bancada do cliente nao tem como dar.
///
/// NADA DAQUI RODA EM JOGO: quem chama e o `RoboDoMapa`, no modo host.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O id do set que a bancada pos no mundo, ou -1.</summary>
	private int _setDeFoto = -1;

	/// <summary>
	/// POUSA o corpo num planeta gerado desta seed. O mundo nasce numa thread (`Encomendar`): devolve falso
	/// enquanto ele se forma -- o robo chama de novo ate dar verdadeiro.
	/// </summary>
	internal bool PousarNoGeradoDeFoto(int idDono, ulong seed, string nome)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? pl)) return false;
		return PousarEmProcedural(pl, new PlanetaNoEspaco { Nome = nome, Seed = seed, Pos = new Vec2(4_000_000, 4_000_000), Raio = 199 });
	}

	/// <summary>Da (ou tira) o Dragon Radar da mochila do corpo, e manda a mochila -- o disco e do tique de producao.</summary>
	internal void RadarDeFoto(int idDono, bool tem)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? pl)) return;
		pl.Mochila.Tirar(CatalogoDeItens.Radar, 99);
		if (tem) pl.Mochila.Guardar(CatalogoDeItens.Radar);
		MandarMochila(pl, forcar: true);
	}

	/// <summary>
	/// POE UM SET DE BANCADA na zona do corpo, com as sete ACORDADAS nos deslocamentos dados (em pixels, a
	/// partir do corpo). Desfaz o anterior. Devolve onde cada esfera ficou.
	/// </summary>
	internal List<Vec2> EsferasDeFoto(int idDono, IReadOnlyList<(float Dx, float Dy)> onde)
	{
		LimparEsferasDeFoto();
		var postas = new List<Vec2>();
		if (!_players.TryGetValue(idDono, out ServerPlayer? pl) || ProximoIdDeSet() is not (>= 0 and var id)) return postas;

		var s = new SetDeEsferas { Id = id, Ex = pl.Pos.X, Ey = pl.Pos.Y - 2 * ZoneCollision.TileSize, CriadorNome = "Bancada", AtivoEm = 0 };
		s.PorZona(pl.Zone);
		_sets.Add(s);
		_setDeFoto = id;

		for (int n = 1; n <= Esferas.Total; n++)
		{
			(float dx, float dy) = onde[(n - 1) % onde.Count];
			var e = new Esfera { Set = id, Numero = n, X = pl.Pos.X + dx, Y = pl.Pos.Y + dy };
			e.PorZona(pl.Zone);
			_esferas.Add(e);
			postas.Add(new Vec2(e.X, e.Y));
		}
		MandarEsferas(pl.Zone);
		return postas;
	}

	/// <summary>Poe a esfera `numero` do set de bancada na mao do corpo (o `container` do DM).</summary>
	internal void CarregarEsferaDeFoto(int idDono, int numero)
	{
		if (_esferas.Find(e => e.Set == _setDeFoto && e.Numero == numero) is { } e) e.Portador = idDono;
	}

	/// <summary>Tira o set de bancada do mundo.</summary>
	internal void LimparEsferasDeFoto()
	{
		if (_setDeFoto < 0 || SetPorId(_setDeFoto) is not { } s) { _setDeFoto = -1; return; }
		ZoneKey zona = s.Zona;
		_esferas.RemoveAll(e => e.Set == s.Id);
		_sets.Remove(s);
		_setDeFoto = -1;
		MandarEsferas(zona);
	}
}
