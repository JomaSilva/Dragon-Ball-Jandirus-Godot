using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O KIT DO DEUS DA DESTRUICAO NA MAO DE UM CORPO SEM DONO (dono, 2026-10-11: *"faca o bills usar o kit completo"*).
///
/// O `GameServer.KitDoDeus.cs` ja deixa um corpo do mundo carregar o titulo (`ConsagrarDeusSemDono`) e ja serve
/// todo verb a ele. O que faltava era o corpo USAR o kit sozinho, e isso tem duas metades:
///
///   * QUEM DECIDE e o `Cerebro` de todo NPC (`Core/Ai/Cerebro.Kit.cs`), pelo mesmo `Comando.Habilidade` que o
///     atuador entrega ao `UsarHabilidade` do jogador -- nao ha caminho novo de gesto aqui;
///   * O QUE ELE PRECISA SABER PRA DECIDIR mora neste arquivo, e sao so perguntas: o corpo carrega o kit? o Hakai
///     dele vale nesta luta? ha ki vindo que a anulacao apagaria? As respostas sao as das funcoes dos PROPRIOS
///     verbs (`OHakaiPega`, `AAnulacaoPega`), lidas pelo `LerCapacidades` e pelo `LerPercepcao`.
///
/// SO PRA CORPO SEM DONO CONSAGRADO. O jogador que e o Deus da Destruicao aperta os botoes dele; e o corpo de
/// jogador POSSUIDO (a fera, a furia lendaria) nao ganha o kit por tabela -- um Deus que perdeu a razao apagando
/// gente com o Hakai nao foi pedido por ninguem.
/// </summary>
public partial class GameServer
{
	/// <summary>Os corpos sem dono consagrados que NAO executam: o Hakai (o verb e o dom da esfera) desligado pra a luta em que estao.</summary>
	private readonly HashSet<int> _deusesSemHakai = [];

	/// <summary>
	/// CONSAGRA UM CORPO SEM DONO PRA UMA LUTA, dizendo se o Hakai vale nela. E a `ConsagrarDeusSemDono` do kit mais
	/// a chave que so quem dirige o corpo conhece (o Bills tem uma por luta: `BillsEWhis.HakaiNaTerra` e
	/// `BillsEWhis.HakaiNoDesafio`).
	/// </summary>
	internal void ConsagrarPraLuta(ServerPlayer npc, bool comHakai)
	{
		// (ids de corpo que ja sairam do mundo nao ficam na lista -- a mesma faxina da `ConsagrarDeusSemDono`)
		_deusesSemHakai.RemoveWhere(id => !_players.ContainsKey(id));
		if (npc.Peer != null) return;

		ConsagrarDeusSemDono(npc);
		if (comHakai) _deusesSemHakai.Remove(npc.Id);
		else _deusesSemHakai.Add(npc.Id);
	}

	/// <summary>A luta acabou: o corpo deixa de carregar o titulo (a Fury cai e o estado do kit e esquecido, pela regra do proprio kit).</summary>
	internal void DesconsagrarDaLuta(ServerPlayer npc)
	{
		_deusesSemHakai.Remove(npc.Id);
		ConsagrarDeusSemDono(npc, consagrado: false);
	}

	/// <summary>O Hakai deste corpo esta desligado pra a luta em que ele esta? Lido pelo `OHakaiPega` -- a porta dos dois caminhos do Hakai.</summary>
	private bool SemHakai(ServerPlayer deus) => _deusesSemHakai.Count > 0 && _deusesSemHakai.Contains(deus.Id);

	/// <summary>
	/// ESTE CORPO USA O KIT SOZINHO? -- o corpo sem dono consagrado, e so ele. E o `Capacidades.KitDoDeus` da IA.
	/// (A contagem vem na frente: num mundo sem Deus de mundo nenhum, a pergunta custa uma comparacao.)
	/// </summary>
	private bool KitNaMaoDaIa(ServerPlayer pl) => _deusesSemDono.Count > 0 && _deusesSemDono.Contains(pl.Id);

	/// <summary>
	/// QUANTOS TIROS DO KIT ENTRAM NO ARSENAL DESTE CORPO: nenhum (nao carrega o kit), um (so o Destruction God's
	/// Fury: o Hakai esta desligado, e a Destroyer Sphere E o Hakai a distancia -- sem ele ela nao e largada, em vez
	/// de virar uma bola que nao existe no kit) ou os dois.
	/// </summary>
	private int TirosDoKitNaIa(ServerPlayer pl) => !KitNaMaoDaIa(pl) ? 0 : SemHakai(pl) ? 1 : 2;

	/// <summary>
	/// O rumo de um tiro "vem pra ca" quando aponta pra este corpo dentro de 45 graus -- o mesmo corte do reflexo de
	/// contra-feixe (`VemFeixeContraMim`).
	/// </summary>
	private const float RumoQueVemPraCa = 0.7f;

	/// <summary>
	/// HA KI ALHEIO VINDO PRA CIMA DESTE CORPO QUE A ENERGY NULLIFICATION APAGARIA AGORA? E o `Percepcao.HaKiPraAnular`.
	///
	/// As perguntas sao as do verb (`AnularEnergiaDoDeus`): de pe, a recarga vencida, e pelo menos um ataque que o
	/// `AAnulacaoPega` apagaria. A que e so da IA e o RUMO: o verb apaga tudo o que esta ao alcance, mas o cerebro so
	/// gasta os cinco minutos de recarga com o que vem pra cima dele -- um tiro que passa de lado, entre dois outros
	/// corpos, nao e problema dele.
	///
	/// SEM CRIAR LISTA, pela mesma razao do `VemFeixeContraMim`: roda a 30 Hz por corpo que carrega o kit, e
	/// `ProjeteisDaZona` nasce uma lista vazia pra zona que nao tem tiro nenhum.
	/// </summary>
	private bool HaKiPraAnular(ServerPlayer pl)
	{
		if (Caido(pl)) return false;
		if (!_projeteis.TryGetValue(pl.Zone.Hash, out List<Projetil>? daZona) || daZona.Count == 0) return false;
		if (_kitDoDeus.TryGetValue(pl.Id, out EstadoDoKit? e) && NowMs() < e.AnulacaoProntaEmMs) return false;

		foreach (Projetil p in daZona)
		{
			if (!AAnulacaoPega(pl, p)) continue;

			Vec2 ateMim = pl.Pos - p.Pos;
			float dist = ateMim.Length, passo = p.Rumo.Length;
			if (dist < 1e-3f) return true;                      // ja esta em cima
			if (passo < 1e-3f) continue;                        // parado no ar (uma mina): nao vem pra lugar nenhum
			if ((p.Rumo.X * ateMim.X + p.Rumo.Y * ateMim.Y) / (dist * passo) >= RumoQueVemPraCa) return true;
		}
		return false;
	}
}
