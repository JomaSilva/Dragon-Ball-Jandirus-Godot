using Jandirus.Core.Appearance;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagtuffle` (2026-10-18) -- ver `Client/RoboDoTuffle.cs`. O robo e o servidor rodam no mesmo processo
/// (`--host`), e sao duas corridas: o robo TSUFURUJIN usa a maquina pelo menu E (o palco so poe o mainframe aos pes dele e da a tecnologia e
/// o zeni); e o robo SAIYAJIN e o HOSPEDE, o Vegeta que o Neo Tuffle possui. Ai o Tuffle e um corpo de palco (sem tela, como os da
/// `--alunosteste`), com a marca de Neo Tuffle escrita direto na ficha, e o resto e o jogo de producao -- o `Possuir` dele, o tique das posses
/// que expulsa quando o corpo cai, o pacote de aparencia e o cliente do robo assistindo.
/// </summary>
public sealed partial class GameServer
{
	private const int IdBaseDoTuffleDeTeste = 97_800;
	private int _tufflesDoPalco;
	private ServerPlayer? _tuffleDoPalco;
	private Obra? _mainframeDoPalco;

	/// <summary>Um mainframe de androides aparafusado aos pes do robo, e a zona fica sabendo. Devolve o id da obra.</summary>
	internal int PalcoN_Mainframe(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		var o = new Obra
		{
			Id = IdDoPalcoDoTuffle + 90, Tipo = "Android_Creation_Mainframe", Aparafusada = true,
			X = pl.Pos.X, Y = pl.Pos.Y - ZoneCollision.TileSize, DonoConta = pl.Conta, DonoNome = pl.Name,
		};
		o.PorZona(pl.Zone);
		_noChao.Add(o);
		_mainframeDoPalco = o;
		MandarObras(pl.Zone);
		return o.Id;
	}

	/// <summary>A raca do robo: o Tsufurujin usa a maquina, e o Saiyajin e o possuido (sao duas corridas).</summary>
	internal string PalcoN_Raca(int id) => _players.TryGetValue(id, out ServerPlayer? pl) ? pl.Race : "";

	/// <summary>O que a maquina pede ao Tsufurujin, com folga: a tecnologia do laboratorio e o zeni da conversao.</summary>
	internal void PalcoN_TechEZeni(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.techskill = Math.Max(pl.Ficha.techskill, TechDoLaboratorio + 10);
		pl.Ficha.Zeni = Math.Max(pl.Ficha.Zeni, CustoNeoTuffle * 1.5);
		pl.SigAtributos = "";
	}

	/// <summary>O laboratorio instalado no mainframe do palco (-1 = sem mainframe), e se o robo ja e Neo Tuffle.</summary>
	internal (int Lab, bool NeoTuffle) PalcoN_AMaquina(int id) =>
		(_mainframeDoPalco?.Lab ?? -1, _players.TryGetValue(id, out ServerPlayer? pl) && pl.Ficha.NeoTuffle);

	/// <summary>O mainframe sai do chao (o resto da cena e de posse, e ele so serviu pra foto do menu).</summary>
	internal void PalcoN_TirarOMainframe()
	{
		if (_mainframeDoPalco is not { } o) return;
		_noChao.Remove(o);
		MandarObras(o.Zona);
		_mainframeDoPalco = null;
	}

	/// <summary>
	/// O NEO TUFFLE DE PALCO: um Tsufurujin careca de fraque (a `TuffleTux` do DU), a `dx` tiles do robo, ja com a marca de Neo Tuffle. Devolve
	/// o id, ou zero.
	/// </summary>
	internal int PalcoN_Tuffle(int id, float dx)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		var baby = new ServerPlayer
		{
			Id = IdBaseDoTuffleDeTeste + _tufflesDoPalco++, Peer = null, Name = "Baby", Race = RacaDoTuffle, Genero = "Male", Idade = 20,
			Zone = pl.Zone, Pos = MapaDaZonaOuCatalogo(pl.Zone)?.PontoLivrePerto(pl.Pos + new Vec2(dx * ZoneCollision.TileSize, 0))
								  ?? pl.Pos + new Vec2(dx * ZoneCollision.TileSize, 0),
			Conta = "palco_tuffle_baby", Slot = 0,
			Ficha = new Fighter { Race = RacaDoTuffle, BP = 1000 },
			Livro = new SkillBook(),
		};
		baby.Ficha.Class = "Normal";
		baby.Visual = new Appearance { Cabelo = "Bald" };
		baby.Visual.Roupa.Add(new PecaDeRoupa("res://Assets/Sprites/Clothes/TuffleTux.tres"));
		PorNoMundo(baby);
		baby.Ficha.NeoTuffle = true;
		baby.Ficha.Ki = baby.Ficha.MaxKi;
		baby.Ficha.Tick(agoraMs: NowMs());
		baby.Facing = Facing.West;
		TrocarAparencias(baby);
		_tuffleDoPalco = baby;
		return baby.Id;
	}

	/// <summary>O robo cai nocauteado e o Tuffle de palco entra nele pelo `Possuir` de producao. Devolve se entrou.</summary>
	internal bool PalcoN_Possuir(int idDoRobo, int idDoTuffle)
	{
		if (!_players.TryGetValue(idDoRobo, out ServerPlayer? robo) || !_players.TryGetValue(idDoTuffle, out ServerPlayer? tuffle)) return false;
		robo.Combate.Nocautear(600);
		Possuir(tuffle);
		return PosseDoTuffleDe(tuffle.Id)?.Hospede == robo;
	}

	/// <summary>Liga (ou desliga) o defeito do pacote sem o cabelo do Tuffle, e a zona recebe o corpo possuido de novo.</summary>
	internal void PalcoN_SemCabeloBranco(int idDoTuffle, bool ligado)
	{
		TuffleSemCabeloBrancoDeTeste = ligado;
		if (_players.TryGetValue(idDoTuffle, out ServerPlayer? t)) TrocarAparencias(t);
	}

	/// <summary>O corpo possuido anda `dx` tiles -- pra ver o dono assistindo de onde ele esta.</summary>
	internal void PalcoN_Andar(int idDoTuffle, float dx)
	{
		if (_players.TryGetValue(idDoTuffle, out ServerPlayer? t))
			CravarPosicao(t, MapaDaZonaOuCatalogo(t.Zone)?.PontoLivrePerto(t.Pos + new Vec2(dx * ZoneCollision.TileSize, 0)) ?? t.Pos);
	}

	/// <summary>
	/// O corpo possuido cai nocauteado, como de um soco vindo do leste (o rumo do golpe e o que deita o corpo); quem expulsa o Tuffle e o
	/// tique de producao (`TickDasPosses`).
	/// </summary>
	internal void PalcoN_DerrubarOPossuido(int idDoTuffle)
	{
		if (!_players.TryGetValue(idDoTuffle, out ServerPlayer? t)) return;
		t.ApontarRumoDoGolpe(new Vec2(ZoneCollision.TileSize, 0));
		t.Combate.Nocautear(30);
	}

	/// <summary>Quem possui o robo agora (0 = ninguem), e se o robo esta nocauteado.</summary>
	internal (int PossuidoPor, bool Caido) PalcoN_Robo(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? (pl.PossuidoPor, pl.Ficha.KO) : (0, false);

	/// <summary>Tira o Tuffle de palco (saindo do corpo, se estiver dentro), o mainframe e o defeito; o robo levanta.</summary>
	internal void PalcoN_Limpar(int idDoRobo)
	{
		TuffleSemCabeloBrancoDeTeste = false;
		if (_tuffleDoPalco is { } t)
		{
			if (PosseDoTuffleDe(t.Id) is { } p) Expulsar(p, "");
			if (_players.ContainsKey(t.Id)) RemoverNpc(t);
		}
		_tuffleDoPalco = null;
		PalcoN_TirarOMainframe();
		if (_players.TryGetValue(idDoRobo, out ServerPlayer? robo) && robo.Ficha.KO) robo.Combate.Levantar();
	}
}
