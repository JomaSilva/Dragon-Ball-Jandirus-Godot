using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// OS GANCHOS DO ROBO `--diagexecucao` (cliente, com o servidor neste processo): a ABSORCAO DE EXECUCAO do bio-androide na tela (o adendo do
/// dono, 2026-10-17). O Cell da fabrica (`NascerNpc("cell")`, sem cerebro -- parado, porque um passo dele desfaz a execucao) ao lado de um
/// cidadao nocauteado pela porta de producao (`Nocautear`); o Cell aperta o `absorver` pelo verb (`UsarHabilidade`), e quem conduz os 5 s e
/// o tique de producao. O robo so monta, espera e fotografa.
/// </summary>
public partial class GameServer
{
	/// <summary>Uma cena montada: o bio e a vitima dele.</summary>
	internal readonly record struct CenaDaExecucao(int Bio, int Vitima);

	/// <summary>
	/// MONTA A CENA a direita do corpo <paramref name="id"/>, a tres tiles e meio (na camera da bancada o canto de cima a esquerda e do painel
	/// de vida e o de baixo do chat): o Cell olhando pro leste e a vitima a um tile dele, ja nocauteada.
	/// </summary>
	internal CenaDaExecucao? MontarCenaDaExecucaoDeTeste(int id, ulong semente)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return null;
		const float T = ZoneCollision.TileSize;
		Vec2 onde = pl.Pos + new Vec2(3.5f * T, 0.3f * T);
		ServerPlayer? bio = NascerNpc("cell", pl.Zone, onde, semente);
		ServerPlayer? vitima = NascerNpc("cidadao", pl.Zone, onde + new Vec2(T, 0), semente + 1);
		if (bio == null || vitima == null)
		{
			if (bio != null) RemoverNpc(bio);
			if (vitima != null) RemoverNpc(vitima);
			return null;
		}
		// O CELL VESTIDO PELO DEGRAU DELE (o imperfeito), como a saga e o diretor do trailer o vestem (`EntrarNoDegrau`) -- nascido so pela
		// fabrica ele tem o corpo da larva.
		if (bio.Papel is { Molde.EhChefe: true } papel)
		{
			papel.BpsPinados = Jandirus.Core.Npc.Sagas.Pinar(papel.Molde, MediaDoServidor());
			papel.Estagio = 0;
			EntrarNoDegrau(bio, cura: 0, anunciar: false);
		}
		bio.Cerebro = null;
		vitima.Cerebro = null;
		bio.Facing = Facing.East;
		// A VITIMA OLHA PRO CELL E CAI DEITADA NA HORIZONTAL, de cabeca pro leste (`FacingDaQueda`).
		vitima.Facing = Facing.West;
		vitima.FacingDaQueda = Facing.East;   // o NPC que cai sem golpe deita de cabeca pro NORTE -- e de cima isso parece em pe
		Nocautear(vitima);
		bio.AlvoId = vitima.Id;
		return new CenaDaExecucao(bio.Id, vitima.Id);
	}

	/// <summary>O bio aperta o `absorver` pelo verb de producao. Devolve se a execucao abriu.</summary>
	internal bool ExecutarDeTeste(int bio)
	{
		if (!_players.TryGetValue(bio, out ServerPlayer? b)) return false;
		UsarHabilidade(b, "absorver");
		return _execucoesDoBio.ContainsKey(bio);
	}

	/// <summary>A execucao deste bio ainda corre?</summary>
	internal bool ExecutandoDeTeste(int bio) => _execucoesDoBio.ContainsKey(bio);

	/// <summary>Quantos segundos faltam pra execucao deste bio fechar, pelo relogio do SERVIDOR (-1 = nenhuma correndo).</summary>
	internal double FaltaDaExecucaoDeTeste(int bio) =>
		_execucoesDoBio.TryGetValue(bio, out ExecucaoDoBio? e) ? (e.FechaEm - NowMs()) / 1000.0 : -1;

	/// <summary>UM PASSO do bio (seis pixels pro leste): o tique de producao desfaz a execucao.</summary>
	internal void PassoDoBioDeTeste(int bio)
	{
		if (_players.TryGetValue(bio, out ServerPlayer? b)) b.Pos += new Vec2(6, 0);
	}

	/// <summary>Este corpo ainda esta no mundo?</summary>
	internal bool NoMundoDeTeste(int id) => _players.ContainsKey(id);

	/// <summary>O snapshot manda este corpo SECANDO (`Ocupacao.Secando`)?</summary>
	internal bool SecandoDeTeste(int id) => _players.TryGetValue(id, out ServerPlayer? p) && OcupacaoDe(p) == Ocupacao.Secando;

	/// <summary>Quantos cadaveres ha na zona deste corpo (-1 sem o corpo).</summary>
	internal int CadaveresNaZonaDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? p) ? ZoneList(p.Zone.Hash).Count(x => x.ECadaver) : -1;

	internal void DesmontarCenaDaExecucaoDeTeste(CenaDaExecucao c)
	{
		foreach (int quem in new[] { c.Bio, c.Vitima })
			if (_players.TryGetValue(quem, out ServerPlayer? corpo)) RemoverNpc(corpo);
	}
}
