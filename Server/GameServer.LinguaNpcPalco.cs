using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diaglinguanpc` (2026-11-01, o pedido 221): habitantes de uma raca escolhida ao lado do jogador -- com o
/// papel do habitante de planeta (`TipoDeNpc.Cidadao`), parados (sem cerebro) -- que dizem uma frase pela porta de producao (`Falar`, a fala
/// do jogo, com a marca de automatica). O resto e producao: a lingua do habitante, o embaralhador, a entrega e o balao.
/// </summary>
public partial class GameServer
{
	private readonly List<int> _habitantesDoPalcoLN = [];

	/// <summary>Um habitante da raca dada a <paramref name="tilesX"/> tiles do jogador, de frente pra ele. Devolve o id (0 = sem molde ou sem jogador).</summary>
	internal int PalcoLN_Habitante(int idJogador, string raca, string nome, float tilesX)
	{
		if (!_players.TryGetValue(idJogador, out ServerPlayer? dono)) return 0;
		MoldeDeNpc? molde = _moldes?.Todos.FirstOrDefault(m => m.Tipo == TipoDeNpc.Cidadao && !m.EhChefe && !m.Alma);
		if (molde == null) return 0;
		var f = new Fighter
		{
			Name = nome, Race = raca, BP = 1_000,
			physoff = 1, physdef = 1, technique = 1, kioff = 1, kidef = 1,
			kiskill = 1, speed = 1, magiskill = 1, Idade = 25,
			maxstamina = 100, stamina = 100,
		};
		f.Statify();
		f.PowerLevel();
		f.Ki = f.MaxKi;
		f.Class = "Normal";
		int id = _nextId++;
		var c = new ServerPlayer
		{
			Id = id,
			Peer = null,
			Name = nome,
			Race = raca,
			Genero = "Male",
			Idade = 25,
			Zone = dono.Zone,
			Pos = dono.Pos + new Vec2(tilesX * ZoneCollision.TileSize, 0),
			LastInputMs = NowMs(),
			Ficha = f,
			Livro = new Jandirus.Core.Skills.SkillBook(),
			Cerebro = null,   // parado: so fala quando o palco manda
			Papel = new PapelDeNpc(molde, (ulong)id),
			Visual = AparenciaDeNpc(new MoldeDeNpc { Id = "palco_lingua_npc", Racas = [raca], Classe = "Normal" }, raca, "Male", (ulong)id),
		};
		c.Facing = tilesX > 0 ? Facing.West : Facing.East;
		PorNoMundo(c);
		TrocarAparencias(c);
		_habitantesDoPalcoLN.Add(id);
		return id;
	}

	/// <summary>O habitante diz a frase pela porta de producao: a fala do jogo (`digitada` falso), na lingua dele.</summary>
	internal void PalcoLN_Dizer(int id, string frase)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? c)) return;
		_ultimaFala.Remove(id);
		Falar(c, Protocol.Fala.Diz, frase);
	}

	/// <summary>Os habitantes do palco saem do mundo.</summary>
	internal void PalcoLN_Limpar()
	{
		foreach (int id in _habitantesDoPalcoLN)
			if (_players.TryGetValue(id, out ServerPlayer? c)) RemoverNpc(c);
		_habitantesDoPalcoLN.Clear();
	}
}
