using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// (2026-11-01, os pedidos 212 e 213) O PALCO DAS FOTOS do robo `--diagipecustos` (`Client/RoboDoIpEDosCustos.cs`) -- o dono: *"N esqueça de
/// tirar foto de tudo pra revisao"*. Um irmao de conexao colado no jogador (a regra do mesmo IP ligada SO nesta bancada: o jogo de host liga
/// o cliente pelo loopback, e a regra fica desligada nas bancadas -- `MesmoIpNaBancadaDeTeste`) e as tentativas pelas portas de producao; e
/// os custos do Outro Mundo mudados pelo verb da aba Admin.
/// </summary>
public partial class GameServer
{
	/// <summary>O irmao de conexao forjado pra foto (0 = nenhum).</summary>
	private int _irmaoDaFoto;

	/// <summary>Um corpo colado no jogador, da MESMA conexao (o endereco dele), marcado como o alvo dele; a regra do mesmo IP ligada so aqui.</summary>
	internal int PalcoIP_IrmaoDeConexao(int idDono)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono) || IpDe(dono) is not { } ip) return 0;
		int id = ForjarCorpoDeFoto(idDono, new Vec2(ZoneCollision.TileSize, 0), "Irmao", 50_000, comEscada: false);
		if (id == 0) return 0;
		IpDeTeste[id] = ip;
		MesmoIpNaBancadaDeTeste = true;
		dono.AlvoId = id;
		_irmaoDaFoto = id;
		return id;
	}

	/// <summary>
	/// As tentativas pelas portas de producao: a cura, o convite da Potara e o revive (com ele morto ao lado). As recusas vao pro chat de
	/// quem usa -- a do revive sai pela tecnica que acha o alvo sozinha, que fala uma vez a cada dois segundos (o relogio dela e zerado aqui).
	/// </summary>
	internal void PalcoIP_TentarTudo(int idDono)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono) || !_players.TryGetValue(_irmaoDaFoto, out ServerPlayer? irmao)) return;
		CurarG6(dono);
		Convidar(dono, irmao, Jandirus.Core.Social.TipoDeFusao.Potara);
		irmao.Ficha.dead = true;
		_ultimaFraseDoUsoNoMesmoIp.Remove(dono.Id);
		RessuscitarG4(dono);
		irmao.Ficha.dead = false;
	}

	/// <summary>Tira o irmao do mundo e desliga a regra da bancada.</summary>
	internal void PalcoIP_Desfazer()
	{
		MesmoIpNaBancadaDeTeste = false;
		if (_irmaoDaFoto == 0) return;
		IpDeTeste.Remove(_irmaoDaFoto);
		_pedidosDeFusao.Remove(_irmaoDaFoto);
		if (_players.TryGetValue(_irmaoDaFoto, out ServerPlayer? c))
		{
			_players.Remove(c.Id);
			ZoneList(c.Zone.Hash).Remove(c);
		}
		_irmaoDaFoto = 0;
	}

	/// <summary>Os custos do Outro Mundo pelo verb da aba Admin (`admin_custos_alem chave valor`), um por um, em nome deste jogador.</summary>
	internal void PalcoIP_MudarCustos(int idAdmin, params string[] pares)
	{
		if (!_players.TryGetValue(idAdmin, out ServerPlayer? adm)) return;
		foreach (string par in pares) AdminCustosDoAlem(adm, par);
	}
}
