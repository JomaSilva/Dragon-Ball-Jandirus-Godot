using Jandirus.Core.Forms;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagritualkai` (2026-10-23) -- ver `Client/RoboDoRitualDoKaioshin.cs`. O robo e o servidor rodam no
/// mesmo processo (`--host`): o palco faz o que so o OUTRO lado faria -- o trono do robo, o Kaioshin forjado ao lado dele que aperta o
/// ritual, o relogio de parede que anda dez minutos, e a forma que sai entre a rodada em portugues e a em ingles. O resto e producao: o
/// botao do ritual na aba Skills, o cartao de pedido e o "Aceitar" dele, a trava, o chat do progresso, a tecla C e a aba Forms.
/// </summary>
public partial class GameServer
{
	private const int IdDoKaioshinDoPalcoRk = 99_870;
	private const string ContaDoKaioshinDoPalcoRk = "bancada_rk_kaioshin_palco";

	/// <summary>A vaga do trono que o Kaioshin forjado ocupa -- a segunda, pra o robo poder ter a primeira quando e ele o Kaioshin.</summary>
	private static string VagaDoPalcoRk => RitualDoKaioshin.Cargo + "#2";

	/// <summary>O robo vira (ou deixa de ser) o Kaioshin: o trono na conta dele, e o kit reconciliado na hora. Falso sem robo.</summary>
	internal bool PalcoRk_ORoboEKaioshin(int idDoRobo, bool sim)
	{
		if (!_players.TryGetValue(idDoRobo, out ServerPlayer? robo)) return false;
		if (sim) _tronos[RitualDoKaioshin.Cargo] = robo.Conta;
		else _tronos.Remove(RitualDoKaioshin.Cargo);
		ReconciliarDadiva(robo);
		MandarCargos(robo);
		return true;
	}

	/// <summary>
	/// O OUTRO KAIOSHIN: um corpo forjado no tile a leste do robo, olhando pra ele (o robo marcado), na SEGUNDA vaga do trono -- e o
	/// robo, quando sobe ao trono, e o segundo Kaioshin de cinco (o caso do dono: "o proprio Kaioshin precisa que OUTRO Kaioshin faca
	/// o ritual nele"; com dois donos o `Dono` do cargo chega com os dois nomes). Devolve o corpo (nulo sem robo).
	/// </summary>
	internal ServerPlayer? PalcoRk_OutroKaioshin(int idDoRobo)
	{
		if (!_players.TryGetValue(idDoRobo, out ServerPlayer? robo)) return null;
		if (!_players.TryGetValue(IdDoKaioshinDoPalcoRk, out ServerPlayer? k))
		{
			k = new ServerPlayer
			{
				Id = IdDoKaioshinDoPalcoRk, Peer = null, Name = "Kaioshin", Race = "Kai", Genero = "Male", Idade = 300,
				Zone = robo.Zone, Pos = robo.Pos + new Vec2(ZoneCollision.TileSize, 0), Conta = ContaDoKaioshinDoPalcoRk, Slot = 0,
				Ficha = new Fighter { Race = "Kai", BP = 1_000_000 }, Livro = new SkillBook(), Karma = 80,
			};
			k.Ficha.Class = "Normal";
			PorNoMundo(k);
			TrocarAparencias(k);
		}
		k.Facing = Facing.West;
		k.AlvoId = robo.Id;
		_tronos[VagaDoPalcoRk] = k.Conta;
		MandarCargos(robo);
		return k;
	}

	/// <summary>
	/// O outro Kaioshin aperta o ritual pelo canal de producao -- com o dia dele zerado (o palco repete o ritual na rodada em ingles).
	/// Devolve se o convite ficou na mesa do robo.
	/// </summary>
	internal bool PalcoRk_OKaioshinOferece(int idDoRobo)
	{
		if (!_players.TryGetValue(idDoRobo, out ServerPlayer? robo) || PalcoRk_OutroKaioshin(idDoRobo) is not { } k) return false;
		k.Ficha.RitualDoKaioshinEm = 0;
		UsarHabilidade(k, RitualDoKaioshin.Verbo);
		return _ofertasDeRitual.ContainsKey(robo.Conta);
	}

	/// <summary>O relogio de PAREDE anda (o dia e os dez minutos sao de parede), e o tique dos rituais roda na hora.</summary>
	internal void PalcoRk_AndarORelogio(long ms)
	{
		AdiantoDoRelogioDeTeste += ms;
		TickDosRituais();
	}

	/// <summary>O robo: esta num ritual? tem a forma? o potencial desperto?</summary>
	internal (bool EmRitual, bool TemAForma, bool Potencial) PalcoRk_Estado(int idDoRobo) =>
		_players.TryGetValue(idDoRobo, out ServerPlayer? p)
			? (RitualDe(p) != null, p.Forma.Despertou(Catalogo.IdMistico), p.Ficha.unlockPotential >= 1)
			: (false, false, false);

	/// <summary>Entre a rodada em portugues e a em ingles: o robo volta pra base e a forma sai (o potencial fica -- uma vez na vida).</summary>
	internal void PalcoRk_TirarAForma(int idDoRobo)
	{
		if (!_players.TryGetValue(idDoRobo, out ServerPlayer? p)) return;
		if (p.Forma.Atual != Catalogo.IdBase)
		{
			p.Forma.Entrar(Catalogo.IdBase);
			AplicarForma(p);
		}
		p.Forma.Liberadas.Remove(Catalogo.Rede(Catalogo.IdMistico));
		ReapresentarOCorpo(p);
		MandarFicha(p);
	}

	/// <summary>Tira do mundo o Kaioshin forjado, os tronos, os convites e os rituais do palco.</summary>
	internal void PalcoRk_Limpar(int idDoRobo)
	{
		if (_players.TryGetValue(idDoRobo, out ServerPlayer? robo))
		{
			_ofertasDeRitual.Remove(robo.Conta);
			_rituais.RemoveAll(r => r.Kaioshin == robo || r.Alvo == robo);
			MandarEfeito(robo, RitualDoKaioshin.EfeitoDaTrava, 0);
		}
		if (_players.TryGetValue(IdDoKaioshinDoPalcoRk, out ServerPlayer? k))
		{
			_rituais.RemoveAll(r => r.Kaioshin == k || r.Alvo == k);
			RemoverNpc(k);
		}
		_tronos.Remove(VagaDoPalcoRk);
		_tronos.Remove(RitualDoKaioshin.Cargo);
		SalvarCargos();
	}
}
