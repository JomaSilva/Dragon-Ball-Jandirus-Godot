using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Forms;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A FABRICA DE NPC: de um molde e uma semente sai um corpo no mundo.
///
/// ============================ ESTA E A TERCEIRA VEZ QUE UM CORPO SEM DONO NASCE ============================
/// As duas primeiras sao o clone da mente (`CriarClone`, GameServer.Clone.cs) e os corpos da bancada de
/// convivio (`Forjar`, GameServer.ConvivioTeste.cs), e as duas faziam a MESMA sequencia: montar o
/// `ServerPlayer` com `Peer = null`, `Statify`, `PrepararCombate`, entrar no `_players`, entrar na
/// `ZoneList`. Escrever a terceira copia aqui era o caminho para a quarta.
///
/// Entao a sequencia virou uma funcao -- <see cref="PorNoMundo"/> -- e as duas antigas passaram a
/// chama-la. O que cada uma tem de proprio (o clone copia a ficha do dono e nao e letal; a bancada
/// crava conta e slot pra ter assinatura; o NPC sorteia tudo) continua sendo delas.
///
/// Sem isso, o defeito classico deste port se repetiria: um passo novo no nascimento de um corpo
/// (a gravidade do chao, por exemplo) entraria em duas das tres e o terceiro tipo de corpo ficaria
/// com uma regra a menos, sem nada acusando.
/// =======================================================================================================
///
/// ============================ O NPC NAO TEM FICHA DE NPC ============================
/// Ele tem <see cref="Fighter"/>, <see cref="Jandirus.Core.Skills.SkillBook"/>,
/// <see cref="Jandirus.Core.Skills.NiveisDeSkill"/> e <see cref="EstadoDeForma"/> -- os mesmos quatro
/// objetos de um jogador, montados pelas mesmas funcoes. `Peer == null` e a unica diferenca
/// estrutural, e ela ja era a definicao de NPC deste servidor desde o clone.
/// ================================================================================
/// </summary>
public partial class GameServer
{
	private CatalogoDeMoldes? _moldes;

	/// <summary>
	/// Faixa de ids dos NPCs? NAO -- eles usam o `_nextId` como todo mundo.
	///
	/// A bancada de convivio usa uma faixa reservada porque forja corpos NO MEIO de um servidor
	/// vivo e precisa nao colidir com ninguem. Um NPC de verdade e um habitante: ele disputa id com
	/// jogador, entra no snapshot, apanha e morre. Dar-lhe uma faixa separada seria a primeira regra
	/// que o distingue de um jogador, e a lista de regras assim so cresce.
	/// </summary>
	private void CarregarMoldes()
	{
		const string caminho = "res://Assets/Data/npcs.json";
		if (!Godot.FileAccess.FileExists(caminho))
		{
			GD.PushWarning("[server] sem npcs.json -- nenhum molde de NPC carregado");
			return;
		}

		_moldes = CatalogoDeMoldes.Parse(Godot.FileAccess.GetFileAsString(caminho));

		// O ARQUIVO E EDITADO A MAO PELO DONO, e molde incoerente falha CALADO: o bicho nasce, e so
		// quem estiver olhando o combate percebe que ele nunca transforma. Por isso a conferencia
		// grita no boot, com o id do molde e a frase do que esta errado.
		int ruins = 0;
		foreach (MoldeDeNpc m in _moldes.Todos)
			foreach (string p in m.Problemas()) { GD.PushError($"[server] npcs.json: {p}"); ruins++; }

		GD.Print($"[server] moldes de NPC: {_moldes.Total} "
			   + $"({_moldes.Todos.Count(m => m.EhChefe)} com ficha pronta de chefe)"
			   + (ruins > 0 ? $"  <<< {ruins} problema(s)" : ""));
	}

	/// <summary>
	/// A MEDIA DE PODER DOS JOGADORES -- o `AverageBP` do original (NPClist.dm:72), que e o que faz
	/// o bicho de mundo acompanhar quem esta jogando.
	///
	/// So conta quem tem `Peer`: incluir NPC na media daria realimentacao (um NPC forte puxa a
	/// media, que gera NPCs mais fortes) e o servidor escalaria sozinho ate o infinito com ninguem
	/// online.
	/// </summary>
	private double MediaDoServidor()
	{
		double soma = 0;
		int n = 0;
		foreach (ServerPlayer p in _players.Values)
			if (p.Peer != null && p.Ficha is { BP: > 0 }) { soma += p.Ficha.BP; n++; }
		return n == 0 ? 0 : soma / n;
	}

	/// <summary>
	/// POE UM CORPO NO MUNDO -- a sequencia unica, que os tres nascimentos de corpo sem dono usam.
	///
	/// A ORDEM E A DO LOGIN (`Entrar`), e cada passo tem um motivo que ja custou caro em algum
	/// lugar deste port:
	///   1. `Statify` antes de tudo -- e ele que produz `Espeed`, `MaxKi` e os stats efetivos;
	///   2. `SpeedStat` DEPOIS dele (era campo copiado do dono no clone, e copia envelhece);
	///   3. `PrepararCombate` cria o corpo (membros) -- rodar qualquer coisa que leia
	///      `pl.Combate` antes disto derruba o servidor com nulo, tropeco ja registrado duas vezes
	///      em `Entrar`;
	///   4. entrar no `_players` e na `ZoneList` -- fora da lista da zona o corpo nao e visto por
	///      ninguem, e um teste daria verde por ausencia;
	///   5. a gravidade do chao, que e por zona.
	/// </summary>
	private ServerPlayer PorNoMundo(ServerPlayer corpo)
	{
		corpo.Ficha.Statify();
		corpo.SpeedStat = MoveRules.SpeedStatFrom(corpo.Ficha.Espeed);
		PrepararCombate(corpo, null);
		_players[corpo.Id] = corpo;
		ZoneList(corpo.Zone.Hash).Add(corpo);
		AplicarGravidade(corpo);
		return corpo;
	}

	/// <summary>
	/// NASCE UM NPC. Devolve nulo quando o molde nao existe (e diz qual no console).
	///
	/// `lugar` e o que separa um NPC do outro dentro do mesmo molde -- ponto de spawn, indice da
	/// onda, o que for. Ver <see cref="SorteioDeNpc.SementeDe"/>: com (universo, molde, lugar) o
	/// mesmo NPC sai igual em qualquer maquina e em qualquer reinicio.
	/// </summary>
	private ServerPlayer? NascerNpc(string moldeId, ZoneKey zona, Vec2 pos, ulong lugar)
	{
		MoldeDeNpc? molde = _moldes?.Get(moldeId);
		if (molde == null)
		{
			GD.PushError($"[server] molde de NPC desconhecido: '{moldeId}'");
			return null;
		}
		if (_racas == null) { GD.PushError("[server] sem races.json: nao da pra nascer NPC"); return null; }

		ulong semente = SorteioDeNpc.SementeDe(SeedDoUniverso, molde.Id, lugar);
		FichaSorteada s = SorteioDeNpc.Sortear(molde, semente, _racas, _skills, zona.Name, MediaDoServidor());

		var npc = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,                    // e o que faz dele um NPC -- a mesma definicao do clone
			Name = s.Nome,
			Zone = zona,
			Pos = pos,
			Race = s.Raca,
			Class = s.Classe,
			Genero = s.Genero,
			Idade = (int)s.Ficha.Idade,
			Planeta = zona.Name,
			LastInputMs = NowMs(),
			Ficha = s.Ficha,
			Livro = s.Livro,
			Niveis = s.Niveis,
			Papel = s.Papel,
			Visual = AparenciaDeNpc(s.Raca, s.Genero, semente),
		};

		PorNoMundo(npc);

		// AS FORMAS SO DEPOIS DO CORPO EXISTIR, porque o gate delas le o `PerfilDeFormas`, que e
		// derivado do `ServerPlayer` -- e `Perfil()` e o funil unico dos gates
		// (GameServer.Formas.cs:37). Montar um perfil dentro do sorteio seria a segunda copia dele,
		// e "acrescentar a linha nova em dois dos tres lugares" e o erro que aquele comentario ja
		// registra como o mais repetido deste port.
		int formas = SorteioDeNpc.AbrirFormas(npc.Forma, molde, npc.Ficha.BP, Perfil(npc));

		// o multiplicador de forma volta pra 1 (ele comeca na base) e a ficha fecha
		AplicarForma(npc);
		SorteioDeNpc.Normalizar(npc.Ficha);

		// quem esta na zona precisa VER o corpo novo; pro NPC os envios de volta sao no-op (`Peer?.`)
		TrocarAparencias(npc);

		GD.Print($"[server] NPC '{npc.Name}' ({npc.Race}/{npc.Class}) nasceu em {zona.Name} "
			   + $"| molde {molde.Id} semente {semente} | BP {npc.Ficha.BP:N0} (expresso {npc.Ficha.expressedBP:N0}) "
			   + $"| {npc.Livro.Aprendidas.Count} skills, {formas} formas"
			   + (molde.EhChefe ? $" | ROTEIRO de {molde.Estagios.Length} degrau(s)" : ""));
		return npc;
	}

	/// <summary>
	/// TIRA UM NPC DO MUNDO. O avesso exato do <see cref="PorNoMundo"/>.
	///
	/// Avisa a zona com `PeerLeft`: sem isso o boneco fica congelado na tela de quem estava vendo --
	/// o mesmo defeito que o `RemoverClone` ja tratava, agora pra qualquer corpo sem dono.
	/// </summary>
	private void RemoverNpc(ServerPlayer npc)
	{
		if (npc.Peer != null) return;   // corpo de jogador nao se remove, se solta (ver DevolverAsRedeas)

		List<ServerPlayer> zona = ZoneList(npc.Zone.Hash);
		zona.Remove(npc);
		_players.Remove(npc.Id);

		var w = Protocol.Begin(Protocol.S2C.PeerLeft);
		w.Put(npc.Id);
		foreach (ServerPlayer o in zona)
			o.Peer?.Send(w, Protocol.ChannelReliable, LiteNetLib.DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// A APARENCIA de um NPC. Sorteada da MESMA lista que a tela de criacao oferece
	/// (<see cref="VisualCatalog"/>) e passada pelo MESMO <see cref="VisualCatalog.Sanear"/> que
	/// confere a ficha que chega do cliente.
	///
	/// E o `npc_pick_body` + `npc_apply_hair` do original (PlanetPopulation.dm:301, :339-342), com
	/// uma diferenca de desenho: la os pools de corpo e cabelo sao listas escritas a mao no proc; aqui
	/// e o catalogo do jogo. Uma lista escrita a mao envelhece calada quando um sprite e renomeado.
	/// </summary>
	private Appearance AparenciaDeNpc(string raca, string genero, ulong semente)
	{
		var ap = new Appearance();
		if (_visual == null) return ap;

		BodyOptions corpos = _visual.CorposDe(raca);
		List<string> lista = corpos.Para(genero);
		if (lista.Count > 0) ap.Corpo = SorteioDeNpc.Sorteador(semente, "corpo").Next(lista.Count);
		if (corpos.Tons.Count > 0) ap.Tom = SorteioDeNpc.Sorteador(semente, "tom").Next(corpos.Tons.Count);

		if (VisualCatalog.TemCabelo(raca) && _visual.Cabelos.Count > 0)
			ap.Cabelo = _visual.Cabelos[SorteioDeNpc.Sorteador(semente, "cabelo").Next(_visual.Cabelos.Count)].Nome;

		// O FROST DEMON NAO TEM CORPO NO CATALOGO: o corpo dele E a forma (ver `VisualCatalog.CorpoSprite`).
		// Sem esta linha o Freeza nasceria desenhado como um humano palido -- pra todo mundo na zona.
		if (Jandirus.Core.Races.FormasDeFrost.EhFrost(raca))
			ap.FormasDeFrost = Jandirus.Core.Races.FormasDeFrost.Sanear(
				Jandirus.Core.Races.FormasDeFrost.ClasseNormal, null);

		// A COR DA CHAMA DELE, do MESMO sorteio dos jogadores (`min(255, 200 + rand(0,255))` por
		// canal) e da mesma semente por campo que o corpo e o cabelo acabaram de usar. Um NPC nao
		// tem `CriadoEm` -- ele nao esta em save nenhum --, entao a derivacao por nome + nascimento
		// nao serve aqui; a semente dele E o lugar, e ela ja e estavel.
		//
		// DEPOIS do `Sanear`, e nao antes: aquele metodo confere o que veio do catalogo (corpo,
		// tom, cabelo, roupa) e nao tem o que dizer sobre uma cor sorteada aqui dentro.
		_visual.Sanear(ap, raca, genero);
		ap.CorAura = Jandirus.Core.Appearance.CorDeAura.DeSemente(semente);
		return ap;
	}

	/// <summary>
	/// ============================ ESTE CORPO PODE SUBIR A ESCADA POR CONTA PROPRIA? ============================
	/// Jogador: sempre. NPC comum: sempre. Chefe roteirizado: **nunca** -- ele TEM a forma (o
	/// `Despertou()` dele devolve verdadeiro, o multiplicador existe) e quem o move e o roteiro.
	///
	/// E a resposta ao pedido do dono -- *"o freeza do planeta vegeta nao transforma enquanto o
	/// freeza de namek transforma"* --, e ela e uma pergunta sobre a FICHA e nao sobre a IA: no
	/// original os dois Freezas tem o mesmo cerebro e o mesmo `ai_no_powerup = 1`
	/// (BossEvents.dm:181); o que difere e a lista de degraus.
	///
	/// A guarda mora no <see cref="Transformar"/>, que e o funil por onde a tecla C, o admin e (em
	/// breve) a decisao da IA passam. Escrever o corte e nao aplica-lo ja aconteceu neste projeto --
	/// a API de sigilo de BP ficou 100% orfa -- e o preco e uma regra que so existe no comentario.
	/// =====================================================================================================
	/// </summary>
	private static bool AscendePorDecisao(ServerPlayer pl) => pl.Papel?.AscendePorDecisao ?? true;
}
