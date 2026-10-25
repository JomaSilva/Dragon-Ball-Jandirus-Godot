using Jandirus.Core.Items;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-23) AS PORTAS DE PALCO DO ROBO `--diagpotara`: um Kaioshin forjado ao lado do jogador, que lhe DA o par pelo botao de producao;
/// o parceiro da fusao; o aceite dele; e a limpeza. Nada aqui decide regra -- as regras sao as de `GameServer.ParDePotara.cs`, e o par
/// chega e se da pelos mesmos funis do jogo (`ReconciliarDadiva`, `item_dar`, `fus_sim`).
/// </summary>
public partial class GameServer
{
	/// <summary>A conta do Kaioshin forjado (o trono e da conta): uma so dele, pra o parceiro forjado nao herdar o cargo.</summary>
	private const string ContaDoKaioDaFotoPtr = "bancada_dpotara_kaio";

	/// <summary>Os tronos de antes do palco, devolvidos na limpeza.</summary>
	private Dictionary<string, string>? _tronosAntesDaFotoPtr;

	/// <summary>
	/// O KAIOSHIN DA O PAR AO JOGADOR: forjado a 2 passos, com o trono na conta dele, o par pelo funil do cargo (`ReconciliarDadiva`) e o
	/// gesto pelo botao de producao (`item_dar` com o jogador marcado). Devolve o id do Kaioshin, ou 0 se o par nao mudou de mao.
	/// </summary>
	internal int PalcoPtr_KaioshinDaOPar(int idDoJogador)
	{
		if (!_players.TryGetValue(idDoJogador, out ServerPlayer? pl)) return 0;
		int id = ForjarCorpoDeFoto(idDoJogador, new Vec2(2 * ZoneCollision.TileSize, 0), "Kibitoshin", 1_000_000, comEscada: false);
		if (!_players.TryGetValue(id, out ServerPlayer? kaio)) return 0;
		kaio.Conta = ContaDoKaioDaFotoPtr;
		_tronosAntesDaFotoPtr ??= new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		_tronos["kaioshin"] = kaio.Conta;
		ReconciliarDadiva(kaio);
		pl.Combate.Carencia = 0;   // a carencia do login deixa o corpo intocavel, e o `Marcado` do Kaioshin nao o acharia
		kaio.AlvoId = pl.Id;
		Verbo(kaio, "item_dar", CatalogoDeItens.BrincosPotara);
		return pl.Mochila.Quantos(CatalogoDeItens.BrincosPotara) > 0 ? id : 0;
	}

	/// <summary>O par de volta na mochila do jogador -- depois do contra-exemplo que o tira (`ParVoltaAoCofreDeTeste`).</summary>
	internal void PalcoPtr_DevolverOPar(int idDoJogador)
	{
		if (!_players.TryGetValue(idDoJogador, out ServerPlayer? pl)) return;
		if (pl.Mochila.Quantos(CatalogoDeItens.BrincosPotara) == 0) pl.Mochila.Guardar(CatalogoDeItens.BrincosPotara);
		MandarMochila(pl);
	}

	/// <summary>O PARCEIRO DA FUSAO, a 1 passo e marcado pelo jogador (o duplo clique). Devolve o id dele, ou 0.</summary>
	internal int PalcoPtr_ParceiroMarcado(int idDoJogador)
	{
		if (!_players.TryGetValue(idDoJogador, out ServerPlayer? pl)) return 0;
		int id = ForjarCorpoDeFoto(idDoJogador, new Vec2(ZoneCollision.TileSize, 0), "Parceiro", Math.Max(pl.Ficha.BP, 1000), comEscada: false);
		if (!_players.TryGetValue(id, out ServerPlayer? par)) return 0;
		par.Combate.Carencia = 0;
		pl.Combate.Carencia = 0;
		pl.Ficha.fusion_cooldown_until = 0;
		pl.AlvoId = id;
		return id;
	}

	/// <summary>Ha um convite de fusao na mesa deste corpo?</summary>
	internal bool PalcoPtr_TemConvite(int id) => _pedidosDeFusao.ContainsKey(id);

	/// <summary>O PARCEIRO ACEITA -- o "sim" de producao (o que o `fus_sim` chama). Falso se nao havia convite na mesa dele.</summary>
	internal bool PalcoPtr_Aceitar(int idDoParceiro)
	{
		if (!_players.TryGetValue(idDoParceiro, out ServerPlayer? p) || !_pedidosDeFusao.ContainsKey(idDoParceiro)) return false;
		ResponderAoConvite(p, aceitou: true);
		return true;
	}

	/// <summary>Este corpo esta fundido agora?</summary>
	internal bool PalcoPtr_Fundido(int id) => EstaFundido(id);

	/// <summary>Desfaz a fusao, tira os corpos forjados e devolve os tronos.</summary>
	internal void PalcoPtr_Limpar()
	{
		foreach (FusaoAtiva f in _fusoes.ToList()) Separar(f, "fim do robo da Potara");
		foreach (CenaDeFusao c in _cenasDeFusao.ToList()) SoltarDaCenaDeFusao(c);
		LimparAFoto();
		if (_tronosAntesDaFotoPtr is not { } antes) return;
		_tronos.Clear();
		foreach ((string vaga, string conta) in antes) _tronos[vaga] = conta;
		_tronosAntesDaFotoPtr = null;
	}
}
