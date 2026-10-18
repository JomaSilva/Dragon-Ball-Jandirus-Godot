using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// OS GANCHOS DO ROBO `--diagdnabio` (cliente, com o servidor neste processo): AS SKILLS DO DNA, A ESTOCADA e o NUCLEO do bio-androide na
/// tela (o adendo do dono, 2026-10-17). A PREMISSA e escrita aqui -- o sangue do Cell no corpo do robo, a vida baixa, a presa na frente, a
/// morte --; os GESTOS sao os de producao, pelo botao do menu e pelo fio do cliente (`dna:*`, `rc_ira`, `agarrar`, `absorver`, `nucleo:*`).
/// </summary>
public partial class GameServer
{
	/// <summary>O sangue do bio do robo (as doadoras, na ordem) -- e nenhuma skill escolhida ainda.</summary>
	internal void DarSangueDeTeste(int id, string[] racas)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.bio_racas_dna = [.. racas];
		pl.Ficha.bio_skills = 0;
		pl.SigAtributos = "";
	}

	/// <summary>O que o robo le do bio, num retrato: as skills que valem e as do sangue, a ficha e o nucleo.</summary>
	internal readonly record struct RetratoDoDna(SkillRacial Valem, SkillRacial Sangue, bool Caido, bool Morto, string Nucleo, int UsosDoNucleo,
		bool BracoEsquerdoDecepado, double Vida, bool Irado);

	internal RetratoDoDna? DnaDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
			? new RetratoDoDna(SkillsDoCorpo(pl), SkillsRaciais.DoSangue(pl.Ficha), pl.Ficha.KO, pl.Ficha.dead, pl.Ficha.bio_nucleo, pl.Ficha.bio_nucleo_usos,
													  pl.Combate?.Corpo.Achar("Braco esquerdo") is { Decepado: true }, pl.Ficha.HP, pl.Racial.IraDesde >= 0)
			: null;

	/// <summary>
	/// TODA PARTE numa fracao da vida, em combate -- a vida do corpo e a MEDIA delas (`Body.Vida`), os orgaos de dentro tambem. A Warrior Race
	/// pede menos de 20%.
	/// </summary>
	internal void FerirTodosDeTeste(int id, double fracao)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate is not { } c) return;
		foreach (BodyPart p in c.Corpo.Partes)
			if (!p.Decepado) p.Vida = p.VidaMax * fracao;
		c.SincronizarVida();
		c.EmCombate = 60;
	}

	/// <summary>O corpo inteiro de novo: de pe, cada membro cheio, Ki e folego cheios, fora de combate.</summary>
	internal void RestaurarDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate is not { } c) return;
		c.Reviver();
		c.Corpo.Restaurar();
		c.SincronizarVida();
		c.EmCombate = 0;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Ficha.stamina = pl.Ficha.maxstamina;
	}

	/// <summary>
	/// A PRESA: um cidadao parado (sem cerebro), a um tile NA FRENTE do robo (pra onde o servidor o ve olhando -- o agarrao pega quem esta na
	/// frente), virado pra ele. Devolve o id (0 = nao nasceu).
	/// </summary>
	internal int PresaNaFrenteDeTeste(int id, ulong semente)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		Vec2 frente = MeleeArea.Frente(pl.Facing);
		ServerPlayer? presa = NascerNpc("cidadao", pl.Zone, pl.Pos + frente * (ZoneCollision.TileSize * 0.9f), semente);
		if (presa == null) return 0;
		presa.Cerebro = null;
		presa.Facing = pl.Facing switch { Facing.East => Facing.West, Facing.West => Facing.East, Facing.North => Facing.South, _ => Facing.North };
		return presa.Id;
	}

	/// <summary>Os membros da presa, na ordem da cauda: a fracao de vida de cada um (0 = decepado).</summary>
	internal double[] MembrosDaPresaDeTeste(int presa) =>
		_players.TryGetValue(presa, out ServerPlayer? p) && p.Combate is { } c
			? [.. BioAndroids.OrdemDaEstocada.Select(n => c.Corpo.Achar(n) is { Decepado: false } m ? m.Fracao : 0)]
			: [];

	/// <summary>Quem o robo segura agora (0 = ninguem).</summary>
	internal int AgarrandoDeTeste(int id) => _players.TryGetValue(id, out ServerPlayer? pl) ? pl.AgarrandoId : 0;

	/// <summary>A MORTE chega ao robo pela porta de producao (`CombatState.Morrer`): a cadeia que a nega -- o nucleo -- e a de jogo.</summary>
	internal bool MatarDeTeste(int id) => _players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate is { } c && c.Morrer();

	internal void TirarDeTeste(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? p)) RemoverNpc(p);
	}
}
