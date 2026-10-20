using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagaprendiz` (2026-10-19) -- ver `Client/RoboDoAprendiz.cs`. O robo e o servidor rodam no mesmo processo
/// (`--host`): o palco poe ao lado do robo um Kaioshin forjado (gente: conta e vaga, com o trono na conta) e faz o que so o outro lado
/// faria -- o convite, o trono que se perde, o discipulo do robo quando ele e o mestre. O resto e producao: o cartao de pedido e o botao
/// dele, a aba Learning, a compra, o tique dos cargos, o verbo "Dismiss Apprentice" e o cartao de escolhas da aba Other.
/// </summary>
public partial class GameServer
{
	private const int IdDoMestreDoPalcoApz = 99_850, IdDoDiscipuloDoPalcoApz = 99_851;
	private const string ContaDoMestreDoPalcoApz = "bancada_apz_mestre_palco", ContaDoDiscipuloDoPalcoApz = "bancada_apz_discipulo_palco";

	private ServerPlayer ForjarNoPalcoApz(int id, string nome, string conta, ServerPlayer perto, int tilesAoLeste)
	{
		var p = new ServerPlayer
		{
			Id = id, Peer = null, Name = nome, Race = "Kai", Genero = "Male", Idade = 300,
			Zone = perto.Zone, Pos = perto.Pos + new Vec2(tilesAoLeste * ZoneCollision.TileSize, 0), Conta = conta, Slot = 0,
			Ficha = new Fighter { Race = "Kai", BP = 1_000_000 }, Livro = new SkillBook(), Karma = 80,
		};
		p.Ficha.Class = "Normal";
		PorNoMundo(p);
		TrocarAparencias(p);
		return p;
	}

	/// <summary>Um Kaioshin forjado a 2 tiles do robo, com o trono na conta dele, convida o robo. Falso sem robo.</summary>
	internal bool PalcoApz_OKaioshinConvida(int idDoRobo)
	{
		if (!_players.TryGetValue(idDoRobo, out ServerPlayer? robo)) return false;
		ServerPlayer mestre = _players.TryGetValue(IdDoMestreDoPalcoApz, out ServerPlayer? ja) ? ja
			: ForjarNoPalcoApz(IdDoMestreDoPalcoApz, "Kaioshin", ContaDoMestreDoPalcoApz, robo, 2);
		_tronos[AprendizDeKaioshin.CargoDoMestre] = mestre.Conta;
		VerboNomearAprendiz(mestre, idDoRobo.ToString());
		return _ofertasDeAprendiz.ContainsKey(robo.Conta);
	}

	internal void PalcoApz_DarMarcos(int id, int quantos)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? p)) return;
		p.Livro.MarcosLivres += quantos;
		MandarSkills(p, forcar: true);
	}

	/// <summary>O trono do Kaioshin vaga (o mestre do robo sai dele).</summary>
	internal void PalcoApz_OTronoVaga()
	{
		_tronos.Remove(AprendizDeKaioshin.CargoDoMestre);
		SalvarCargos();
	}

	/// <summary>O robo vira o Kaioshin, com um discipulo forjado (o vinculo amarrado) ao lado. Falso sem robo.</summary>
	internal bool PalcoApz_ORoboEOMestre(int idDoRobo)
	{
		if (!_players.TryGetValue(idDoRobo, out ServerPlayer? robo)) return false;
		_tronos[AprendizDeKaioshin.CargoDoMestre] = robo.Conta;
		ReconciliarDadiva(robo);
		ServerPlayer d = _players.TryGetValue(IdDoDiscipuloDoPalcoApz, out ServerPlayer? ja) ? ja
			: ForjarNoPalcoApz(IdDoDiscipuloDoPalcoApz, "Discipulo", ContaDoDiscipuloDoPalcoApz, robo, 3);
		_aprendizes[d.Assinatura] = new VinculoDeAprendiz
		{
			ContaDoMestre = robo.Conta, NomeDoMestre = robo.Name, ContaDoAprendiz = d.Conta, NomeDoAprendiz = d.Name,
		};
		MandarCargos(robo);
		return true;
	}

	/// <summary>O robo: e aprendiz agora? sabe a Cura? quantos Marcos livres?</summary>
	internal (bool Aprendiz, bool SabeACura, int Marcos) PalcoApz_Estado(int idDoRobo) =>
		_players.TryGetValue(idDoRobo, out ServerPlayer? p)
			? (EAprendizDeKaioshin(p), p.Livro.Sabe("/datum/skill/ki/Heal"), p.Livro.MarcosLivres)
			: (false, false, 0);

	/// <summary>O vinculo do discipulo forjado do robo ainda esta de pe?</summary>
	internal bool PalcoApz_ODiscipuloAindaEDele(int idDoRobo) =>
		_players.TryGetValue(IdDoDiscipuloDoPalcoApz, out ServerPlayer? d) && _players.TryGetValue(idDoRobo, out ServerPlayer? robo)
		&& _aprendizes.TryGetValue(d.Assinatura, out VinculoDeAprendiz? v) && v.Caiu.Length == 0
		&& string.Equals(v.ContaDoMestre, robo.Conta, StringComparison.OrdinalIgnoreCase);

	/// <summary>Tira do mundo os forjados, os tronos, os vinculos e os convites do palco.</summary>
	internal void PalcoApz_Limpar(int idDoRobo)
	{
		foreach (int id in new[] { IdDoMestreDoPalcoApz, IdDoDiscipuloDoPalcoApz })
			if (_players.TryGetValue(id, out ServerPlayer? p))
			{
				_aprendizes.Remove(p.Assinatura);
				RemoverNpc(p);
			}
		_tronos.Remove(AprendizDeKaioshin.CargoDoMestre);
		if (_players.TryGetValue(idDoRobo, out ServerPlayer? robo))
		{
			_aprendizes.Remove(robo.Assinatura);
			_ofertasDeAprendiz.Remove(robo.Conta);
			ReconciliarDadiva(robo);
		}
		SalvarCargos();
		SalvarAprendizes();
	}
}
