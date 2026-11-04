using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagchavesala` (2026-10-31, os pedidos 196 e 197): o vilao da Terra que nasce e cai com o sorteio da chave
/// na mao da bancada, a frente da porta da Sala da Terra (e a volta dela), e o set comum que adormece entre uma cena e outra. O resto e
/// producao: o funil da derrota, a janela de desejos e os verbos do jogador (`db_invocar`, `sdb_invocar`, `sala_entrar`).
/// </summary>
public partial class GameServer
{
	/// <summary>Onde o jogador estava antes de a cena da porta o levar ao Templo -- pra devolver.</summary>
	private (ZoneKey Zona, Vec2 Pos)? _casaDoPalcoCS;

	/// <summary>
	/// O VILAO DA TERRA NASCE pelo tique de producao (com a hora vencida) e o jogador o derrota pelo funil da derrota, com o sorteio da chave
	/// que CAI. Devolve o nome do vilao, ou vazio se ele nao nasceu.
	/// </summary>
	internal string PalcoCS_OVilaoDeixaAChave(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return "";
		bool ligado = VilaoAvulso.Ligado;
		VilaoAvulso.Ligado = true;
		try
		{
			_proximoVilao[pl.Zone.Hash] = NowMs() - 1;
			TickDosViloes();
		}
		finally { VilaoAvulso.Ligado = ligado; }
		EventoDeVilao? e = _viloes.Find(v => v.Zona.Equals(pl.Zone) && !v.Derrotado);
		if (e == null || !_players.TryGetValue(e.Npc, out ServerPlayer? vilao)) return "";
		SorteioDaChaveDeTeste = 0;
		try { AoPerderALuta(vilao, pl, morreu: false); }
		finally { SorteioDaChaveDeTeste = null; }
		return e.Nome;
	}

	/// <summary>Quantas Chaves da Sala do Tempo o jogador tem na mochila.</summary>
	internal int PalcoCS_Chaves(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? pl.Mochila.Quantos(CatalogoDeItens.ChaveDaSala) : -1;

	/// <summary>
	/// O JOGADOR NA FRENTE DA PORTA DA SALA DA TERRA (o Templo, na celula da porta), SEM a autorizacao do Guardiao e sem recarga: a chave e
	/// que vai abrir. A viagem e a de producao (`MoveToZone`).
	/// </summary>
	internal bool PalcoCS_NaPortaDaSala(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		_casaDoPalcoCS ??= (pl.Zone, pl.Pos);
		pl.SalaAutorizada = false;
		pl.SalaPreso = false;
		pl.SalaUltimaEntrada = 0;
		pl.SalaDiasDaSessao = 0;
		pl.SalaJanelaAte = 0;
		_naSala.Clear();
		MoveToZone(id, ZoneKey.Premade(ClasseDeNuvem.ZonaDoTemplo), new Vec2(124 * ZoneCollision.TileSize + 16, 81 * ZoneCollision.TileSize + 16));
		return true;
	}

	/// <summary>De volta pra onde o jogador estava antes da cena da porta.</summary>
	internal void PalcoCS_DeVolta(int id)
	{
		if (_casaDoPalcoCS is not { } casa || !_players.ContainsKey(id)) return;
		_casaDoPalcoCS = null;
		MoveToZone(id, casa.Zona, casa.Pos);
	}

	/// <summary>
	/// (2026-11-01, o pedido 222) O PISO DOS 20 MILHOES NA FOTO: num processo de bancada ele so vale quando a cena o liga (o molde da zona
	/// segura, `PisoDaSalaNaBancadaDeTeste`), e o poder BASE do jogador vai pra onde a cena pede (0 = nao mexe). Devolve o poder base de antes.
	/// </summary>
	internal double PalcoCS_PisoDaSala(int id, bool ligado, double bpBase)
	{
		PisoDaSalaNaBancadaDeTeste = ligado;
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		double antes = pl.Ficha.BP;
		// DE PE E INTEIRO: a r1 chegou na porta com o fotografo CAIDO (o chao da Sala do C5 puxa 10x, e um raio da chuva desceu), e a porta
		// recusou pelo "mal se sustenta de pe" antes de chegar no piso
		pl.Combate?.Curar();
		if (pl.Ficha.KO) pl.Combate?.Levantar();
		if (bpBase <= 0) return antes;
		pl.Ficha.BP = bpBase;
		pl.Ficha.PowerLevel();
		MandarFicha(pl);
		return antes;
	}

	/// <summary>O SET COMUM DESTE MUNDO ADORMECE (o dragao de pe vai embora): a invocacao seguinte e de outro dragao, e nao dele.</summary>
	internal void PalcoCS_AdormecerOSetComum(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _sets.Find(x => x.Zona.Hash == pl.Zone.Hash) is not { } s) return;
		FecharAInvocacao(s.Id, "");
		s.Inerte = true;
		MandarEsferas(pl.Zone);
	}
}
