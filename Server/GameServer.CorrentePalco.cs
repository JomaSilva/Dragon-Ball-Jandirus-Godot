using Jandirus.Core.Appearance;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-19) AS PORTAS DE PALCO DO ROBO `--diagcorrente` -- ver `Client/RoboDaCorrenteDePoder.cs`. O robo e o servidor rodam no mesmo
/// processo (`--host`), e o robo e quem ASSISTE: os dois que doam e recebem sao corpos de palco (sem tela), ao lado dele, e a doacao e a de
/// producao -- o `Give_Power` pelo canal de habilidade, o efetor de 5 Hz que da as doses e o folego que sai a cada uma.
/// </summary>
public sealed partial class GameServer
{
	private const int IdBaseDaCorrenteDeTeste = 987_780;
	private const string SkillDeDarPoderDeTeste = "/datum/skill/GivePower";
	private readonly List<ServerPlayer> _palcoDaCorrente = [];

	/// <summary>
	/// Um corpo de palco a (`dx`, `dy`) tiles do robo, de Ki cheio, que SABE dar poder (a skill do DM, ensinada). Devolve o id, ou zero.
	/// </summary>
	internal int PalcoC_Corpo(int idDoRobo, float dx, float dy, string nome)
	{
		if (!_players.TryGetValue(idDoRobo, out ServerPlayer? pl)) return 0;
		const float T = ZoneCollision.TileSize;
		Vec2 desejado = pl.Pos + new Vec2(dx * T, dy * T);
		var c = new ServerPlayer
		{
			Id = IdBaseDaCorrenteDeTeste + _palcoDaCorrente.Count, Peer = null, Name = nome, Race = "Human", Genero = "Male", Idade = 20,
			Zone = pl.Zone, Pos = MapaDaZonaOuCatalogo(pl.Zone)?.PontoLivrePerto(desejado) ?? desejado,
			Conta = $"palco_corrente_{_palcoDaCorrente.Count}", Slot = 0,
			Ficha = new Fighter { Race = "Human", BP = 1000 },
			Livro = new SkillBook(),
		};
		c.Ficha.Class = "Normal";
		c.Visual = new Appearance { Cabelo = "Goku" };
		PorNoMundo(c);
		c.Ficha.Ki = c.Ficha.MaxKi;
		c.Ficha.Tick(agoraMs: NowMs());
		c.Livro.DarComoEnsinada(SkillDeDarPoderDeTeste);
		c.Facing = dx > 0 ? Facing.West : Facing.East;
		TrocarAparencias(c);
		_palcoDaCorrente.Add(c);
		return c.Id;
	}

	/// <summary>O doador marca o alvo e aperta o `Give_Power` de producao. Devolve se a doacao comecou.</summary>
	internal bool PalcoC_DarPoder(int doador, int alvo)
	{
		if (!_players.TryGetValue(doador, out ServerPlayer? d)) return false;
		d.AlvoId = alvo;
		UsarHabilidade(d, "Give_Power");
		return _doacaoG11.ContainsKey(doador);
	}

	/// <summary>O segundo aperto: a doacao para no proximo tique do efetor (e o doador desmaia, como no DM).</summary>
	internal void PalcoC_Parar(int doador)
	{
		if (_players.TryGetValue(doador, out ServerPlayer? d) && _doacaoG11.ContainsKey(doador)) UsarHabilidade(d, "Give_Power");
	}

	/// <summary>Esta doacao ainda corre?</summary>
	internal bool PalcoC_Doando(int doador) => _doacaoG11.ContainsKey(doador);

	/// <summary>Liga (ou desliga) o defeito do folego que vai so pra tela de quem da.</summary>
	internal void PalcoC_SoProDoador(bool ligado) => CorrenteSoProDoadorDeTeste = ligado;

	/// <summary>Tira os corpos de palco (e as doacoes deles) e o defeito.</summary>
	internal void PalcoC_Limpar()
	{
		CorrenteSoProDoadorDeTeste = false;
		foreach (ServerPlayer c in _palcoDaCorrente)
		{
			_doacaoG11.Remove(c.Id);
			if (_players.ContainsKey(c.Id)) RemoverNpc(c);
		}
		_palcoDaCorrente.Clear();
	}
}
