using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// (2026-11-01, o pedido 219) O PALCO DAS FOTOS da gosma (`--diaggosma`, `Client/RoboDaGosma.cs`) -- o dono: *"faça isso e prove com imagens"*.
/// SO MONTA A CENA E LE O ESTADO: quem larga e o verbo de producao (`HabilidadeNaFotoDoMajin`), quem anda e o `TickDasGosmas`, quem absorve e o
/// `SelarNoMajin`. O palco poe os corpos no lugar, adianta o relogio da olhada (e as nove conferidas, quando a foto nao e delas) e le.
/// </summary>
public partial class GameServer
{
	private GosmaPosta? GosmaDoPalco(int dono) =>
		_gosmaDoDono.TryGetValue(dono, out int corpo) && _gosmas.TryGetValue(corpo, out GosmaPosta? g) ? g : null;

	/// <summary>A gosma deste Majin agora: o passo, o corpo, a presa, o poder, se esta oculta, e o poder e o preco do dono.</summary>
	internal (string Passo, int Corpo, int Presa, double Poder, bool Oculta, double PoderDoDono, double PrecoDoDono) PalcoGosma_Estado(int dono)
	{
		_players.TryGetValue(dono, out ServerPlayer? d);
		GosmaPosta? g = GosmaDoPalco(dono);
		return (g?.Passo.ToString() ?? "-", g?.Corpo ?? 0, g?.Presa ?? 0, g?.Poder ?? 0, g != null && EstaOculto(g.Corpo),
				d?.Ficha.expressedBP ?? 0, d?.Ficha.gosmaNoMapaMod ?? 1);
	}

	/// <summary>A proxima olhada da gosma e agora (o relogio de 8 s nao e da foto).</summary>
	internal void PalcoGosma_OlharJa(int dono)
	{
		if (GosmaDoPalco(dono) is { } g) g.Relogio = 0;
	}

	/// <summary>Grudada: as oito primeiras conferidas ja foram -- a proxima decide (os nove segundos nao sao desta foto).</summary>
	internal void PalcoGosma_PularAsConferidas(int dono)
	{
		if (GosmaDoPalco(dono) is not { Passo: PassoDaGosma.Grudada } g) return;
		g.Conferidas = GosmaDoMajin.Conferidas - 1;
		g.Relogio = 0;
	}

	/// <summary>A gosma esquece quem levou por ultimo -- o mesmo jogador da foto pode ser pego de novo.</summary>
	internal void PalcoGosma_EsquecerOUltimo(int dono)
	{
		if (GosmaDoPalco(dono) is { } g) g.UltimoLevado = 0;
	}

	/// <summary>O jogador vai pra perto do lugar da gosma (a <paramref name="tiles"/> tiles dele, a direita).</summary>
	internal void PalcoGosma_PorPerto(int idJogador, int dono, float tiles)
	{
		if (!_players.TryGetValue(idJogador, out ServerPlayer? pl) || GosmaDoPalco(dono) is not { } g) return;
		Vec2 alvo = g.Lugar + new Vec2(tiles * ZoneCollision.TileSize, 0);
		Transportar(pl, pl.Zone, MapaDaZonaOuCatalogo(pl.Zone)?.PontoLivrePerto(alvo) ?? alvo);
	}

	/// <summary>O dono vai pra longe do lugar da gosma, na mesma zona -- o voo de volta tem caminho pra foto.</summary>
	internal void PalcoGosma_LevarODono(int dono, float tiles)
	{
		if (!_players.TryGetValue(dono, out ServerPlayer? d) || GosmaDoPalco(dono) is not { } g) return;
		Vec2 alvo = g.Lugar + new Vec2(tiles * ZoneCollision.TileSize, 0);
		Transportar(d, d.Zone, MapaDaZonaOuCatalogo(d.Zone)?.PontoLivrePerto(alvo) ?? alvo);
	}

	/// <summary>O dono troca de zona pela porta de toda viagem (`MoveToZone`) -- e a regra *"sair da zona desfaz as gosmas"* que se mede.</summary>
	internal void PalcoGosma_MudarODonoDeZona(int dono, string zona)
	{
		var z = ZoneKey.Premade(zona);
		MoveToZone(dono, z, PontoDeNascimento(z));
	}

	/// <summary>O dono volta pra zona do jogador, ao lado dele.</summary>
	internal void PalcoGosma_TrazerODono(int dono, int idJogador, float tiles)
	{
		if (!_players.TryGetValue(dono, out ServerPlayer? d) || !_players.TryGetValue(idJogador, out ServerPlayer? pl)) return;
		Vec2 alvo = pl.Pos + new Vec2(tiles * ZoneCollision.TileSize, 0);
		Transportar(d, pl.Zone, MapaDaZonaOuCatalogo(pl.Zone)?.PontoLivrePerto(alvo) ?? alvo);
	}

	/// <summary>O poder do jogador da foto (o BP base; o expresso sai do `PowerLevel`).</summary>
	internal void PalcoGosma_PoderDoJogador(int id, double bp)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.BP = bp;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
		MandarFicha(pl);
	}

	/// <summary>Um golpe na gosma a vista: a vida dela cai, e o tique a desfaz pela porta de producao.</summary>
	internal bool PalcoGosma_Golpear(int dono)
	{
		if (GosmaDoPalco(dono) is not { } g || g.Passo == PassoDaGosma.Escondida || !_players.TryGetValue(g.Corpo, out ServerPlayer? c)) return false;
		c.Ficha.HP -= 10;
		return true;
	}

	/// <summary>Quem esta dentro deste Majin sai (a porta de producao do cuspir).</summary>
	internal void PalcoGosma_Soltar(int dono) => CuspirTodosDoMajin(dono, "");

	/// <summary>Este corpo vai dentro de uma gosma agora?</summary>
	internal bool PalcoGosma_NaGosma(int id) => NaGosma(id);

	/// <summary>Este Majin tem gosma no mapa?</summary>
	internal bool PalcoGosma_TemGosma(int dono) => TemGosma(dono);

	/// <summary>O voo com a presa dentro, em tiles/s, nas fotos (0 = o do jogo). Ver `VooLevandoDeTeste`.</summary>
	internal void PalcoGosma_VooDaFoto(double tilesPorSegundo) => VooLevandoDeTeste = tilesPorSegundo;

	private readonly Dictionary<int, Jandirus.Core.Ai.Cerebro?> _cerebroGuardadoDaFoto = new();

	/// <summary>
	/// O CEREBRO DO CORPO DESLIGADO (ou devolvido): sem ele o corpo nao anda nem bate (`TickDosCorposSemDono`), e o resto do jogo continua
	/// -- o relogio da saga, a gosma. A porta que os palcos ja usam (`Cerebro = null`, `GameServer.BioPalco.cs`). Medido na r2: o Super Boo
	/// solto me derrubava, e caido ele me absorveria pela porta de sempre -- a foto deixaria de ser da gosma.
	/// </summary>
	internal void PalcoGosma_SemCerebro(int id, bool sem)
	{
		_players.TryGetValue(id, out ServerPlayer? pl);
		if (!sem)
		{
			// o corpo pode ja ter saido do mundo (o Boo desfeito antes da limpeza): a guarda sai do mesmo jeito
			if (_cerebroGuardadoDaFoto.Remove(id, out Jandirus.Core.Ai.Cerebro? c) && pl != null) pl.Cerebro = c;
			return;
		}
		if (pl == null) return;
		if (!_cerebroGuardadoDaFoto.ContainsKey(id)) _cerebroGuardadoDaFoto[id] = pl.Cerebro;
		pl.Cerebro = null;
		pl.Moving = false;
	}

	/// <summary>Tira as gosmas que sobraram e os Majins da foto, e devolve o jogador ao poder de antes.</summary>
	internal void PalcoGosma_Limpar(int idJogador, double bpDoJogador)
	{
		VooLevandoDeTeste = 0;
		foreach (int id in _cerebroGuardadoDaFoto.Keys.ToArray()) PalcoGosma_SemCerebro(id, false);
		foreach (GosmaPosta g in _gosmas.Values.ToArray()) DesfazerAGosma(g, _players.GetValueOrDefault(g.Dono), "");
		LimparAFotoDoMajin();
		PalcoGosma_PoderDoJogador(idJogador, bpDoJogador);
	}
}
