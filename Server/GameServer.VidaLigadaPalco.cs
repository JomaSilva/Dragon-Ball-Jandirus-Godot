using Jandirus.Core.Ranks;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// (2026-11-01, o pedido 216) O PALCO DAS FOTOS do robo `--diagvidaligada` (`Client/RoboDaVidaLigada.cs`) -- o dono: *"N esqueça de tirar foto
/// de tudo pra revisao"*. O jogador da bancada senta no trono do Deus da Destruicao, e dois Kaioshins de jogador -- corpos forjados, cada um
/// com conta e slot proprios, sentados nas duas primeiras vagas do cargo -- ficam um de cada lado dele. Os tronos dos dois cargos voltam ao que
/// eram no fim (e o arquivo deles e regravado com o que era).
/// </summary>
public partial class GameServer
{
	private Dictionary<string, string>? _vlTronosAntes;
	private Dictionary<string, (string Conta, int Slot, string Nome)>? _vlPersonagensAntes;
	private readonly List<int> _vlCorposDoPalco = [];

	/// <summary>Monta a cena: o Deus e os dois Kaioshins. Devolve os ids dos dois (0 = nao montou).</summary>
	internal (int A, int B) PalcoVL_Montar(int idDeus)
	{
		if (!_players.TryGetValue(idDeus, out ServerPlayer? deus)
			|| Cargos.Get(RitualDoKaioshin.Cargo) is not { } kai || Cargos.Get(KitDoDeus.Cargo) is not { } god)
			return (0, 0);
		_vlTronosAntes ??= new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		_vlPersonagensAntes ??= new Dictionary<string, (string Conta, int Slot, string Nome)>(_personagensDosTronos, StringComparer.OrdinalIgnoreCase);
		foreach (string v in VagasDe(kai).Concat(VagasDe(god)).ToList()) Desocupar(v);
		Ocupar(Vaga(god, 0), deus);
		int a = ForjarKaioshinDoPalco(idDeus, kai, 0, "Shin", new Vec2(2 * ZoneCollision.TileSize, 0));
		int b = ForjarKaioshinDoPalco(idDeus, kai, 1, "Kibito", new Vec2(-2 * ZoneCollision.TileSize, 0));
		MandarCargos(deus);
		return (a, b);
	}

	/// <summary>Um Kaioshin de jogador: o corpo da foto com conta e slot SO DELE (o corpo da foto nasce com a conta de todos), na vaga dada.</summary>
	private int ForjarKaioshinDoPalco(int idDono, RankDef kai, int vaga, string nome, Vec2 desloc)
	{
		int id = ForjarCorpoDeFoto(idDono, desloc, nome, 5_000_000, comEscada: false);
		if (!_players.TryGetValue(id, out ServerPlayer? k)) return 0;
		k.Conta = $"bancada_vl_kaioshin_{vaga}";
		k.Slot = 0;
		Ocupar(Vaga(kai, vaga), k);
		_vlCorposDoPalco.Add(id);
		return id;
	}

	/// <summary>Mata este corpo pela porta de producao de toda morte (`Morrer()`, que chama o funil `AMorteAconteceu`).</summary>
	internal void PalcoVL_Matar(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? p)) p.Combate?.Morrer(ignorarSeguro: true);
	}

	/// <summary>Tira os Kaioshins do mundo, devolve os tronos, esquece o que a ligacao anotou e traz o Deus de volta a vida.</summary>
	internal void PalcoVL_Desfazer(int idDeus)
	{
		foreach (int id in _vlCorposDoPalco)
			if (_players.TryGetValue(id, out ServerPlayer? c))
			{
				_players.Remove(id);
				ZoneList(c.Zone.Hash).Remove(c);
			}
		_vlCorposDoPalco.Clear();
		if (_vlTronosAntes != null && _vlPersonagensAntes != null)
		{
			_tronos.Clear();
			foreach ((string v, string conta) in _vlTronosAntes) _tronos[v] = conta;
			_personagensDosTronos.Clear();
			foreach ((string v, (string Conta, int Slot, string Nome) p) in _vlPersonagensAntes) _personagensDosTronos[v] = p;
			_vlTronosAntes = null;
			_vlPersonagensAntes = null;
			SalvarCargos();
		}
		_kaioshinsQueCairam.Clear();
		_deusDevendoAMorte = null;
		PalcoV30_Reviver(idDeus);
		if (_players.TryGetValue(idDeus, out ServerPlayer? deus)) MandarCargos(deus);
	}
}
