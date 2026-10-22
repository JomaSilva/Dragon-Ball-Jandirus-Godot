using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DA `--diagsalas` (o robo de janela das Salas do Tempo de planeta, `Client/RoboDasSalas.cs`): o que so o
/// servidor pode arrumar pra a foto -- achar um mundo com sala em que a cobaia aguente ficar de pe, leva-la ate a
/// soleira da porta, e gastar a sessao dela de uma vez. NADA daqui decide regra: a porta, a entrada, a expulsao e
/// a recarga que o robo fotografa sao as de producao (`GameServer.SalasDePlaneta.cs`), pelos verbos de sempre.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>A cobaia aguenta o chao de qualquer mundo e os 10x da sala (senao ela chega esmagada e a porta a recusa caida).</summary>
	internal bool PalcoDasSalas_Cobaia(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.Ficha.GravMastered = Math.Max(pl.Ficha.GravMastered, 200);
		pl.SalaUltimaEntrada = 0;
		AplicarGravidade(pl);
		MandarFicha(pl);
		return true;
	}

	/// <summary>
	/// O MUNDO DO PALCO: entre as salas das celulas em volta da Terra, a do MENOR mundo (gera e carrega mais
	/// depressa) -- de preferencia uma que dilate diferente da Terra, pra a frase da porta ter o que mostrar.
	/// </summary>
	internal SalaNoUniverso? PalcoDasSalas_Mundo() =>
		SalaDePlaneta.NaRegiao(SeedDoUniverso, 0, 0, 40)
			.Where(s => !ZonaMorta(Espaco.ZonaDe(s.Corpo)))
			.OrderBy(s => s.Dilatacao == SalaDePlaneta.DilatacaoDaTerra ? 1 : 0)
			.ThenBy(s => MundoProcedural.DaSeed(s.Corpo.Seed, s.Corpo.Nome).Lado)
			.Cast<SalaNoUniverso?>().FirstOrDefault();

	/// <summary>
	/// Leva o corpo ate a SOLEIRA da porta deste mundo, olhando pra ela. Devolve falso enquanto o mundo ainda esta
	/// nascendo no servidor (a geracao e fora do tique): o robo tenta de novo no quadro seguinte.
	/// </summary>
	internal bool PalcoDasSalas_IrParaAPorta(int id, PlanetaNoEspaco p)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		ZoneKey zona = Espaco.ZonaDe(p);
		if (MundoDaZona(zona, p) == null || !_portasDasSalas.TryGetValue(zona.Hash, out PortaNoTerreno porta)) return false;

		(int sx, int sy) = porta.Soleira;
		MoveToZone(id, zona, CorpoNaCelulaDaSala(sx, sy));
		pl.Facing = Facing.North;
		return true;
	}

	/// <summary>
	/// Leva o corpo ao chao livre mais perto desta CELULA do mundo -- a foto das portas de outro mapa olha um lugar
	/// escolhido (onde Vegeta tem as dela), e nao a porta da sala. Falso enquanto o mundo ainda esta nascendo.
	/// </summary>
	internal bool PalcoDasSalas_IrParaACelula(int id, PlanetaNoEspaco p, int cx, int cy)
	{
		if (!_players.ContainsKey(id)) return false;
		ZoneKey zona = Espaco.ZonaDe(p);
		if (MundoDaZona(zona, p) == null) return false;

		Vec2 alvo = CorpoNaCelulaDaSala(cx, cy);
		MoveToZone(id, zona, MapaDaZonaOuCatalogo(zona)?.PontoLivrePerto(alvo) ?? alvo);
		return true;
	}

	/// <summary>A celula da porta deste mundo no servidor, se ele esta vivo (pro robo conferir o que o cliente desenha).</summary>
	internal (int X, int Y)? PalcoDasSalas_Porta(ZoneKey mundo) =>
		_portasDasSalas.TryGetValue(mundo.Hash, out PortaNoTerreno porta) ? (porta.X, porta.Y) : null;

	/// <summary>Gasta a sessao da cobaia ate um segundo antes do fim: o tique de producao a fecha e EXPULSA.</summary>
	internal bool PalcoDasSalas_AcabarASessao(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !TemSessaoNaSalaDePlaneta(pl)) return false;
		pl.SalaDiasDaSessao = SalaDoTempo.SessaoEmDias - SalaDoTempo.DiasPorSegundo;
		return true;
	}
}
