using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DO ROBO `--diagsoltos` (2026-10-31, o pedido 198g; e o `--diagcargopg` do pedido 200) -- atalhos de montagem pras fotos: a
/// soltura do chefe pela porta de producao (o `SoltarOVilao`, que o `GanchoDeDestruicao` chama), com o planeta invadido trocado pelo do
/// host ou por um pedido (o sorteio poderia mandar o vilao pra longe da camera). O que a foto mostra continua sendo o jogo: o corpo que
/// nasce com gente no planeta, o exercito, a caca ao mais forte, a revanche, o jornal e a missao da Patrulha.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O chefe deste elo vence (o corpo nasce no planeta da saga, no degrau pedido) e fica solto, invadindo o planeta do host -- ou o
	/// pre-feito <paramref name="planeta"/> (o pedido 200: a missao da Patrulha num planeta que nao e o da camera). Devolve se ficou solto.
	/// </summary>
	internal bool PalcoSV_Vencer(int idHost, string eloId, int degrau, string? planeta = null)
	{
		if (!_players.TryGetValue(idHost, out ServerPlayer? host)) return false;
		if (Array.Find(_cadeia, e => e.Id == eloId) is not { } elo || elo.Chefes.Length == 0) return false;
		string molde = elo.Chefes[^1].Molde;
		ZoneKey z = ZoneKey.Premade(elo.Planeta);
		if (NascerNpc(molde, z, PontoDeNascimento(z), 0x5017_A000UL + (ulong)_soltos.Count) is not { Papel: { } papel } corpo) return false;
		papel.Estagio = Math.Clamp(degrau, 0, Math.Max(papel.Molde.Estagios.Length - 1, 0));
		EntrarNoDegrau(corpo, cura: 0, anunciar: false);
		// SO A SOLTURA, e nao o gancho inteiro: a consumacao da saga acenderia a destruicao do planeta e destravaria a saga seguinte -- a
		// primeira foto do Freeza saiu com os Androides chegando na Terra por cima. O gancho de producao e provado pela bancada (S1).
		SoltarOVilao(elo, new EstadoDoChefe { Molde = molde, Fase = FaseDoChefe.NoMundo, Corpo = corpo.Id }, corpo,
			planeta is { Length: > 0 } nome ? ZoneKey.Premade(nome) : host.Zone);
		RemoverNpc(corpo);
		return _soltos.Exists(v => v.Elo == eloId);
	}

	/// <summary>O corpo do vilao solto deste elo (0 = nenhum de pe), e quantos soldados dele estao no mundo.</summary>
	internal (int Corpo, int Soldados) PalcoSV_Estado(string eloId)
	{
		if (_soltos.Find(v => v.Elo == eloId) is not { } v) return (0, 0);
		return (v.Corpo, _tropa.Count(x => x.Value.Elo == v.MarcaDaTropa && _players.ContainsKey(x.Key)));
	}

	/// <summary>Poe o corpo do vilao a alguns tiles do host, na frente da camera.</summary>
	internal void PalcoSV_Perto(int idHost, string eloId, float tiles)
	{
		if (!_players.TryGetValue(idHost, out ServerPlayer? host) || _soltos.Find(v => v.Elo == eloId) is not { Corpo: > 0 } v
			|| !_players.TryGetValue(v.Corpo, out ServerPlayer? corpo)) return;
		corpo.Pos = host.Pos + new Vec2(tiles * ZoneCollision.TileSize, 0);
	}

	/// <summary>A REVANCHE pelo funil de toda derrota: o host nocauteia o corpo do vilao solto deste elo.</summary>
	internal void PalcoSV_Derrotar(int idHost, string eloId)
	{
		if (!_players.TryGetValue(idHost, out ServerPlayer? host) || _soltos.Find(v => v.Elo == eloId) is not { Corpo: > 0 } v
			|| !_players.TryGetValue(v.Corpo, out ServerPlayer? corpo)) return;
		corpo.Ficha.KO = true;
		AoPerderALuta(corpo, host, morreu: false);
	}

	/// <summary>
	/// O host vai pra este planeta pre-feito, longe do ponto de chegada (fora da zona segura). O robo fotografa em Arconia: a vitoria do Cell
	/// acende a destruicao da Terra, e o aviso dela encheria o chat das fotos.
	/// </summary>
	internal void PalcoSV_IrPara(int idHost, string planeta)
	{
		ZoneKey z = ZoneKey.Premade(planeta);
		Vec2 longe = PontoDeNascimento(z) + new Vec2(28 * ZoneCollision.TileSize, 16 * ZoneCollision.TileSize);
		MoveToZone(idHost, z, MapaDaZonaOuCatalogo(z)?.PontoLivrePerto(longe) ?? longe);
	}

	/// <summary>
	/// O host forte o bastante pra sobreviver as fotos: o chefe de saga solto luta pra matar, e a primeira rodada do robo perdeu o host pro
	/// Freeza no meio da cena (morto, ele nao era mais "o mais forte" nem "gente no planeta"). E com a gravidade dominada (o chao de Icer).
	/// </summary>
	internal void PalcoSV_Forte(int idHost)
	{
		if (!_players.TryGetValue(idHost, out ServerPlayer? pl)) return;
		pl.Ficha.BP = 1e15;
		// (2026-10-31) E A GRAVIDADE DOMINADA: a foto do pagamento da missao do vilao (o C8 do `--diagcargopg`, o pedido 200) e em Icer, e
		// o chao de la esmagava o host ate a morte -- "a gravidade vence o que restava do seu corpo" -- antes de ele derrotar o Freeza.
		pl.Ficha.GravMastered = Math.Max(pl.Ficha.GravMastered, 1000);
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
	}

	/// <summary>
	/// (o pedido 206) O PRAZO DESTE VILAO ACABA AGORA, pela porta de producao (o `TickDosSoltos`): sem luta perto, ele sai do planeta. A
	/// bancada mede os dois dias; o robo so precisa do desfecho.
	/// </summary>
	internal void PalcoSV_PrazoAcaba(string eloId)
	{
		if (_soltos.Find(v => v.Elo == eloId) is not { Sumido: false } v) return;
		v.SaiEm = NowMs() - 1;
		TickDosSoltos();
	}

	/// <summary>
	/// (o pedido 206) OS DIAS SUMIDO DESTE VILAO ACABAM AGORA, pela porta de producao: ele ataca o primeiro planeta do sorteio (o indice 0
	/// da moeda da bancada -- nunca o de onde saiu), e nao um qualquer que caisse na frente da camera.
	/// </summary>
	internal void PalcoSV_Volta(string eloId)
	{
		if (_soltos.Find(v => v.Elo == eloId) is not { Sumido: true } v) return;
		v.VoltaEm = NowMs() - 1;
		SorteioDoPlanetaDeTeste = 0;
		try { TickDosSoltos(); }
		finally { SorteioDoPlanetaDeTeste = null; }
	}

	/// <summary>(o robo) o vilao deste elo esta sumido? E o planeta em que esta (ou de onde saiu).</summary>
	internal (bool Sumido, string Planeta) PalcoSV_Onde(string eloId) =>
		_soltos.Find(v => v.Elo == eloId) is { } v ? (v.Sumido, v.ZonaNome) : (false, "");

	/// <summary>Fim de cena: todos os soltos saem do jogo.</summary>
	internal void PalcoSV_Encerrar()
	{
		ZerarOsSoltos();
		SalvarOsSoltos();
	}
}
