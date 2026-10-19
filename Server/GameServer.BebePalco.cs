using Jandirus.Core.Appearance;
using Jandirus.Core.Races;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagbebe` (2026-10-18) -- ver `Client/RoboDoBebe.cs`. O robo e o servidor rodam no mesmo processo
/// (`--host`): o fotografo veste a ficha que o filho escolheria na criacao (cabelo e roupa) e vira recem-nascido pelo mesmo campo que o
/// parto escreve (`ServerPlayer.BebeRestanteS`); uma menina de palco (corpo sem tela, apresentada a zona pelo `TrocarAparencias` de
/// producao, como os corpos da `--alunosteste`) nasce do lado dele; e o fim da hora chega sem esperar a hora -- quem faz o fotografo
/// crescer e o relogio de producao (`TickDosBebes`). O parto de verdade e da `--filhosteste` (o `CreateChar`) e da `--diagfilho` (a tela).
/// </summary>
public sealed partial class GameServer
{
	private const int IdBaseDoBebeDeTeste = 97_700;
	private int _meninasDoPalcoB;
	private ServerPlayer? _meninaDoPalcoB;

	/// <summary>A ficha que o filho escolheria na criacao: o cabelo e a roupa pedidos -- e o que ele mostra ao crescer. Devolve se vestiu.</summary>
	internal bool PalcoB_VestirAFicha(int id, string cabelo, string roupa)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _visual is not { } cat || !cat.Roupas.Contains(roupa)) return false;
		pl.Visual.Cabelo = cabelo;
		pl.Visual.Roupa.Clear();
		pl.Visual.Roupa.Add(new PecaDeRoupa(roupa));
		ReapresentarAparencia(pl);
		return true;
	}

	/// <summary>O fotografo vira recem-nascido: a hora cheia, no campo que o parto escreve, e a zona o ve de novo.</summary>
	internal bool PalcoB_Nascer(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.BebeRestanteS = Filhos.SegundosDeBebe;
		ReapresentarAparencia(pl);
		return true;
	}

	/// <summary>
	/// A MENINA DE PALCO: uma Humana recem-nascida a `dx` tiles do fotografo, com cabelo e roupa na ficha (que o bebe nao mostra). Devolve o
	/// id, ou zero.
	/// </summary>
	internal int PalcoB_Menina(int id, float dx, string cabelo, string roupa)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		var menina = new ServerPlayer
		{
			Id = IdBaseDoBebeDeTeste + _meninasDoPalcoB++, Peer = null, Name = "Bra", Race = "Human", Genero = "Female", Idade = 0,
			Zone = pl.Zone, Pos = PontoLivreDe(pl.Zone, pl.Pos + new Vec2(dx * ZoneCollision.TileSize, 0)),
			Conta = "palco_bebe_bra", Slot = 0,
			Ficha = new Fighter { Race = "Human", BP = 100 },
			Livro = new SkillBook(),
		};
		menina.Ficha.Class = "Normal";
		menina.Visual = new Appearance { Cabelo = cabelo };
		menina.Visual.Roupa.Add(new PecaDeRoupa(roupa));
		menina.BebeRestanteS = Filhos.SegundosDeBebe;
		PorNoMundo(menina);
		menina.Ficha.Ki = menina.Ficha.MaxKi;
		menina.Ficha.Tick(agoraMs: NowMs());
		TrocarAparencias(menina);
		_meninaDoPalcoB = menina;
		return menina.Id;
	}

	/// <summary>O fim da hora sem esperar a hora: sobram `segundos` ao corpo, e o relogio de producao faz o resto.</summary>
	internal void PalcoB_FaltaPouco(int id, double segundos)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.BebeRestanteS = segundos;
	}

	/// <summary>
	/// A MENINA CRESCE. Ela e corpo sem tela, e o relogio so conta jogador (`TickDosBebes`) -- entao o palco zera a hora e chama o mesmo
	/// `Crescer` de producao.
	/// </summary>
	internal void PalcoB_MeninaCresce()
	{
		if (_meninaDoPalcoB is not { } m || !_players.ContainsKey(m.Id)) return;
		m.BebeRestanteS = 0;
		Crescer(m);
	}

	/// <summary>Quanto falta pro corpo crescer, ou -1 se ele nao esta no mundo.</summary>
	internal double PalcoB_Restante(int id) => _players.TryGetValue(id, out ServerPlayer? pl) ? pl.BebeRestanteS : -1;

	/// <summary>A zona recebe de novo o pacote de aparencia do corpo -- o robo liga o defeito do boneco e pede o desenho outra vez.</summary>
	internal void PalcoB_Reapresentar(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) ReapresentarAparencia(pl);
	}

	/// <summary>A menina sai do mundo e o fotografo volta a ser adulto.</summary>
	internal void PalcoB_Limpar(int id)
	{
		if (_meninaDoPalcoB is { } m && _players.ContainsKey(m.Id)) RemoverNpc(m);
		_meninaDoPalcoB = null;
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.BebeRestanteS = 0;
	}
}
