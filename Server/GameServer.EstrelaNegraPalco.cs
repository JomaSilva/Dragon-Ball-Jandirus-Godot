using Jandirus.Core.Magic;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagestrelanegra` (2026-10-21): as sete Black Star trazidas pra volta do corpo, acesas, e o fim
/// que as devolve ao sorteio. O resto do roteiro passa pelos verbos de producao (a tecla E, o `db_invocar`, a janela do dragao).
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// AS SETE NAS CELULAS EM VOLTA DO CORPO, com o set aceso e sem pedido gasto. As oito vizinhas da celula do corpo cabem no alcance da
	/// invocacao (`Esferas.AlcanceDaInvocacao`, 1,5 tile pra cada lado): a numero 1 fica a direita, a um tile -- e a que a tecla E pega.
	/// So na superficie de um planeta (e onde a Black Star se invoca). Devolve quantas foram postas.
	/// </summary>
	internal int PalcoEN_SeteAoLado(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !Espaco.EhPlaneta(pl.Zone) || _negras.Count != Esferas.Total) return 0;
		int cx = (int)MathF.Floor(pl.Pos.X / ZoneCollision.TileSize), cy = (int)MathF.Floor(pl.Pos.Y / ZoneCollision.TileSize);
		(int Dx, int Dy)[] volta = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1)];
		foreach (EsferaNegra e in _negras.OrderBy(x => x.Numero))
		{
			(int dx, int dy) = volta[e.Numero - 1];
			e.Portador = 0;
			e.PorZona(pl.Zone);
			e.Cx = cx + dx;
			e.Cy = cy + dy;
			e.PrecisaAssentar = false;
		}
		_setNegro.AtivoEm = TempoDoMundo - 1;
		_setNegro.Pedidos = 0;
		_invocacoes.Remove(EsferasNegras.IdDoSet);
		SalvarNegras();
		MandarEsferas(pl.Zone);
		MandarMochila(pl);
		return Esferas.Total;
	}

	/// <summary>
	/// O set de palco da foto do radar (N1c): o ultimo id de set de jogador (`Esferas.SetDeJogador`, 1..999 -- a tela numera as esferas
	/// por `set*10 + n`), fora do disco e do sorteio.
	/// </summary>
	private const int IdDoSetDoPalcoEN = 999;

	/// <summary>
	/// (2026-10-21) O RADAR DA FOTO N1c: o Dragon Radar na mochila e UMA esfera comum acordada a SESSENTA tiles a direita (um set de palco),
	/// pra o disco mostrar as duas cores lado a lado -- as Black Star em vermelho, a comum em amarelo. A dez tiles (a r2 da `radarcor`) o
	/// ponto dela caia a 5 px do centro e as sete vermelhas, pintadas depois, o cobriam. Devolve se armou.
	/// </summary>
	internal bool PalcoEN_RadarComUmaComum(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !Espaco.EhPlaneta(pl.Zone)) return false;
		if (pl.Mochila.Quantos(Jandirus.Core.Items.CatalogoDeItens.Radar) == 0) pl.Mochila.Guardar(Jandirus.Core.Items.CatalogoDeItens.Radar);
		PalcoEN_TirarAComum(id);
		var s = new SetDeEsferas { Id = IdDoSetDoPalcoEN, Ex = pl.Pos.X, Ey = pl.Pos.Y, Poder = Esferas.PoderDoEterno, Desejos = 1 };
		s.PorZona(pl.Zone);
		s.AtivoEm = TempoDoMundo - 1;
		_sets.Add(s);
		var e = new Esfera { Set = IdDoSetDoPalcoEN, Numero = 1, X = pl.Pos.X + 60 * ZoneCollision.TileSize, Y = pl.Pos.Y };
		e.PorZona(pl.Zone);
		_esferas.Add(e);
		MandarEsferas(pl.Zone);
		MandarMochila(pl);
		return true;
	}

	/// <summary>(2026-10-21) Tira o set de palco do radar (N1c) do mundo -- antes da tecla E, que nao deve achar outra esfera por perto.</summary>
	internal void PalcoEN_TirarAComum(int id)
	{
		bool tinha = _sets.RemoveAll(s => s.Id == IdDoSetDoPalcoEN) > 0;
		_esferas.RemoveAll(e => e.Set == IdDoSetDoPalcoEN);
		if (tinha && _players.TryGetValue(id, out ServerPlayer? pl)) MandarEsferas(pl.Zone);
	}

	/// <summary>O fim do palco: o dragao vai embora (se ainda estiver de pe) e as sete voltam ao sorteio de producao.</summary>
	internal void PalcoEN_Encerrar()
	{
		if (_invocacoes.ContainsKey(EsferasNegras.IdDoSet))
			FecharAInvocacao(EsferasNegras.IdDoSet, $"{EsferasNegras.NomeDoDragao} some no céu.");
		_sets.RemoveAll(s => s.Id == IdDoSetDoPalcoEN);
		_esferas.RemoveAll(e => e.Set == IdDoSetDoPalcoEN);
		EspalharAsNegras(anunciar: false);
		// (2026-10-23) o planeta que o desejo do robo partiu volta a existir, e o ceu de fim do mundo sai (ver `PalcoEN_AbrirOPalcoDasMortes`)
		_palcoEN?.Dispose();
		_palcoEN = null;
	}

	/// <summary>
	/// (2026-10-23) O PALCO DAS MORTES DO ROBO: o desejo de verdade dele (a Danca, N7) parte o planeta onde foi feito -- a Terra do campo.
	/// Aberto antes do pedido e fechado no <see cref="PalcoEN_Encerrar"/>: a contagem fica so na memoria (o `planetas-mortos.json` nao e
	/// escrito) e o ceu volta limpo no fim. O robo acaba muito antes dos 310 s.
	/// </summary>
	private PalcoDeMortes? _palcoEN;

	internal void PalcoEN_AbrirOPalcoDasMortes() => _palcoEN ??= PalcoDeMortesDeBancada();

	/// <summary>(2026-10-23) O corpo sabe a Danca da Fusao? (N7, lido no livro do servidor.)</summary>
	internal bool PalcoEN_SabeDancar(int id) => _players.TryGetValue(id, out ServerPlayer? pl) && SabeDancar(pl);

	/// <summary>(2026-10-23) O planeta onde o corpo esta entrou nos 310 s da destruicao? (N7.)</summary>
	internal bool PalcoEN_PlanetaSeParte(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) && MorteDaZona(pl.Zone) is { Fase: FaseDaMorte.Explodindo };
}
