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

		// O PLANO DE POVOAMENTO PASSA PELA MESMA PORTA. Ele cruza dado com dado (a linha aponta um
		// molde, e o tipo do molde tem que servir) -- por isso e conferido aqui, com o catalogo ja
		// montado, e nao dentro do `Parse`.
		foreach (string p in Povoamento.Problemas(_moldes.Plano, _moldes))
		{ GD.PushError($"[server] npcs.json: {p}"); ruins++; }

		// ============================ E O POVO DOS TRES PLANETAS, ANTES DE QUALQUER CORPO ============================
		// *"na TERRA deveria so spawnar HUMANO e em NAMEK so NAMEKUSEIJIN e no planeta VEGETA so
		// SAIYAJIN"* -- e o recorte e uma INTERSECAO com o berco (`Bercos.RacasNascidasEm`), entao ele
		// falha do jeito mais caro que existe: se as duas tabelas discordarem, o planeta nao nasce
		// misto, ele nao nasce. Um planeta VAZIO nao grita sozinho -- a manutencao so pergunta "quantos
		// faltam?" e o `Sortear` so grita quando o primeiro corpo tenta nascer, cinco segundos depois e
		// uma vez por habitante. Aqui a pergunta e feita uma vez, no boot, com o motivo por extenso.
		//
		// `_racas` ja esta carregado nesta linha: `CarregarRacas()` roda em `Start` imediatamente antes
		// do `CarregarVisual()`, que e quem chama esta funcao.
		if (_racas != null)
			foreach (string p in Jandirus.Core.Races.Bercos.ContradicoesDoPovo(_racas.Protos.Keys))
			{ GD.PushError($"[server] povoamento: {p}"); ruins++; }

		GD.Print($"[server] moldes de NPC: {_moldes.Total} "
			   + $"({_moldes.Todos.Count(m => m.EhChefe)} com ficha pronta de chefe)"
			   + (ruins > 0 ? $"  <<< {ruins} problema(s)" : ""));

		// O RECORTE DO DONO SAI NO BOOT, com o motivo. Uma regra temporaria que so existe no codigo
		// e uma regra que alguem vai reencontrar por acidente daqui a tres meses.
		GD.Print($"[server] povoamento: {_moldes.Plano.Sum(l => l.Quantos)} cidadaos planejados em "
			   + $"{_moldes.Plano.Length} planeta(s); chefes de saga LIGADOS; inimigo comum "
			   + (Povoamento.InimigoComumLigado ? "ligado" : $"DESLIGADO ({Povoamento.MotivoDoInimigoDesligado})"));

		// O RECORTE DO DONO SAI NO BOOT, com o povo de cada um. Os planetas que NAO estao nesta linha
		// sao os que continuam saindo do berco puro ("no RESTO PODE MANTER").
		GD.Print("[server] povoamento: planetas de povo unico -- "
			   + string.Join(", ", Jandirus.Core.Races.Bercos.Povos.Select(x => $"{x.Planeta}={x.Raca}")));
	}

	/// <summary>
	/// A MEDIA DE PODER DOS JOGADORES -- o `AverageBP` do original (NPClist.dm:72), que e o que faz
	/// o bicho de mundo acompanhar quem esta jogando.
	///
	/// So conta JOGADOR: incluir NPC na media daria realimentacao (um NPC forte puxa a media, que gera
	/// NPCs mais fortes) e o servidor escalaria sozinho ate o infinito com ninguem online. O crivo e o
	/// funil do `Core` (<see cref="Jandirus.Core.Npc.Gente.EhJogador"/>) e nao um `Peer != null` a
	/// mao: o clone da mente carrega o `expressedBP` do dono e o boneco do corpo largado COMPARTILHA a
	/// ficha dele -- os dois contariam a mesma pessoa duas vezes, e esta media e o que PINA o BP de
	/// todo chefe de saga no disparo do marco.
	/// </summary>
	private double MediaDoServidor()
	{
		double soma = 0;
		int n = 0;
		foreach (ServerPlayer p in _players.Values)
			if (EhJogador(p) && p.Ficha is { BP: > 0 }) { soma += p.Ficha.BP; n++; }
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

		// `nascendo: true` -- um corpo sem dono nao tem berco no save, e a zona em que ele e posto no
		// mundo E o berco dele. Sem isto um habitante de Icer Planet (15x) ou de Vegeta (10x) nasceria
		// esmagado e preso no chao, e morreria de gravidade sem nunca ter andado. Ver
		// `Birth.AclimatarAoBerco`.
		AplicarGravidade(corpo, nascendo: true);
		return corpo;
	}

	/// <summary>
	/// ============================ A MENTE DESTE CORPO, MONTADA DO MOLDE + SEMENTE ============================
	/// Era codigo INLINE do <see cref="NascerNpc"/>, e virou funcao quando ganhou um segundo chamador: a
	/// devolucao das redeas (`MentePropriaDe`), que precisa por de volta no corpo a MESMA mente que ele
	/// tinha antes de a fera assumir.
	///
	/// Ela nao guarda nada e nao le nada de fora: (molde, semente) -> cerebro. Duas chamadas com o mesmo
	/// papel produzem cerebros identicos -- e e por isso que remontar substitui guardar. Ver o cabecalho
	/// do `Temperamento.Montar` pro que cada numero significa, e o `Cerebro.DesfasarCapacidades` pro que
	/// a fase resolve.
	/// =====================================================================================================
	/// </summary>
	private static Jandirus.Core.Ai.Cerebro CerebroDeNascimento(PapelDeNpc papel) =>
		Temperamento.Montar(papel.Molde,
							SorteioDeNpc.Sorteador(papel.Semente, "fase").NextDouble(), papel.Semente);

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

		// ============================ O RECORTE DO DONO, NO FUNIL ============================
		// *"NPCs INIMIGOS que nao sao chefe POR ENQUANTO NAO SPAWNAM."* A guarda mora AQUI, na unica
		// funcao por onde todo nascimento passa (o povoamento, o admin, as duas bancadas), e nao no
		// laco do povoamento -- do outro jeito ela valeria pra um dos quatro chamadores e a bancada
		// que provasse o recorte estaria provando o caminho que ninguem mais usa.
		//
		// E a recusa e FALADA. Um `return null` calado aqui viraria "o NPC nao nasceu e ninguem sabe
		// por que", que e a armadilha 5 da PARTE 3 exatamente onde ela custa mais caro: numa regra
		// temporaria, que alguem vai querer religar.
		if (!Povoamento.PodeNascer(molde.Tipo))
		{
			GD.Print($"[server] NPC '{molde.Id}' NAO nasceu: {Povoamento.PorQueNaoNasce(molde.Tipo)}");
			return null;
		}

		// ============================ A ZONA FAZ PARTE DO "LUGAR" ============================
		// `SementeDe` recebe (universo, molde, lugar), e o povoamento numera o lugar por planeta --
		// entao o habitante numero 4 da Terra e o numero 4 de Vegeta caiam na MESMA semente. A raca
		// disfarcava (ela vem do berco do planeta, e por isso saia diferente), mas BP, genero, idade
		// e indice do nome eram identicos, planeta por planeta, linha por linha. Da pra ver no log de
		// nascimento: `BP 2.318` na Terra, `BP 2.318` em Vegeta, `BP 2.318` em Namek.
		//
		// O conserto e aqui e nao no contador porque o contador nao e o unico chamador: o admin e as
		// bancadas tambem passam um indice qualquer, e todos eles querem dizer "este lugar, naquele
		// mapa". Misturar a zona onde a semente e MONTADA cobre os tres de uma vez.
		// ==================================================================================
		ulong semente = SorteioDeNpc.SementeDe(
			SeedDoUniverso, molde.Id, Espaco.Misturar(zona.Hash, lugar, 0));
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
			Visual = AparenciaDeNpc(molde, s.Raca, s.Genero, semente),

			// ============================ SEM CEREBRO ELE E UMA ESTATUA ============================
			// `TickDosCorposSemDono` filtra por `Cerebro != null` -- essa e a definicao de "quem o
			// servidor esta dirigindo". A fabrica nao escrevia este campo, e as duas bancadas que a
			// usavam anexavam o cerebro a mao depois (`GameServer.IaTeste.cs:504`): ou seja, **todo
			// NPC nascido pelo caminho de producao era um boneco parado**, e nenhuma bancada podia
			// pegar isso, porque nenhuma usava o caminho de producao inteiro.
			//
			// O temperamento vem dos quatro numeros do molde (ver `Temperamento.Montar`), e a FASE da
			// leitura de capacidades vem da semente: corpos nascidos no mesmo tique nao podem ficar
			// em fase na camada cara de 1 Hz. Ver `Cerebro.DesfasarCapacidades`.
			//
			// A SEMENTE VAI JUNTO porque o molde diz o que a especie e e o sorteio diz quem ESTE
			// corpo e: e o `rand(8,13)/10` por traco do `bhv_set` (`NPCAI.dm:816`), sem o qual os
			// quarenta cidadaos de um planeta recuam, socam e mudam de ideia todos no mesmo instante.
			// (a receita saiu daqui pro `CerebroDeNascimento` quando a POSSE passou a precisar remontar
			// a mente deste corpo -- ver `MentePropriaDe`, `GameServer.Clone.cs`)
			Cerebro = CerebroDeNascimento(s.Papel),

			// ============================ O BERCO DELE E ONDE ELE NASCEU ============================
			// Um NPC nao tem save, entao ele nao tinha berco -- e `Berco.Planeta` vazio faz o
			// `DestinoDoBerco` cair na `SpawnZone`, a Terra. Com o renascimento automatico do
			// `TickCombate` varrendo TODOS os `_players` sem olhar `Peer`, o cidadao de Namek morto
			// reaparecia **na Terra**, vivo, 15 s depois -- e o Freeza voltaria sozinho.
			//
			// Hoje NPC morto e REMOVIDO do mundo (ver `TickCombate`), entao esta linha nao e o que
			// impede o defeito. Ela existe porque o berco e a resposta a "de onde este corpo e", e
			// todo caminho que pergunte isso -- um admin mandando o NPC pro comeco, uma saga que
			// devolva um chefe ao planeta dele -- tem que achar o planeta certo em vez da Terra.
			// (`Zona` e derivado de `Planeta` + `PreFeito`, e nao um campo -- por isso so os tres.)
			Berco = new Jandirus.Core.Races.Berco
			{
				Planeta = zona.Name,
				Natal = zona.Name,
				Seed = zona.Seed,
				PreFeito = zona.Kind != ZoneKey.KindProcedural,
			},
		};

		PorNoMundo(npc);

		// AS FORMAS SO DEPOIS DO CORPO EXISTIR, porque o gate delas le o `PerfilDeFormas`, que e
		// derivado do `ServerPlayer` -- e `Perfil()` e o funil unico dos gates
		// (GameServer.Formas.cs:37). Montar um perfil dentro do sorteio seria a segunda copia dele,
		// e "acrescentar a linha nova em dois dos tres lugares" e o erro que aquele comentario ja
		// registra como o mais repetido deste port.
		// A SEMENTE VAI JUNTO porque a maestria do OOZARU e sorteada aqui dentro -- ela nao e um degrau
		// da escada, entao o `molde.Maestria` nunca a alcancava (ver `SortearAMaestriaDaFera`). E este
		// e o caminho de PRODUCAO: as bancadas que passam semente 0 continuam medindo a receita crua.
		int formas = SorteioDeNpc.AbrirFormas(npc.Forma, molde, npc.Ficha.BP, Perfil(npc), semente);

		// E O CORPO COMECA NO **PISO** DELE, que nem sempre e a base -- o mesmo `Catalogo.IdDoPiso`
		// que o login de um jogador usa. Hoje isso muda uma raca: um NPC de Frost Demon nasce na
		// forma base dele (o corpo que o `SorteioDeNpc` escolheu) em vez de num `base` sem escada
		// nenhuma -- e sem isto o roteiro de chefe dele nao conseguiria subir pra 1a Evolucao, porque
		// o degrau anterior dela (a forma base) exige estar NELA.
		npc.Forma.Atual = Catalogo.IdDoPiso(Perfil(npc));

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

		// O TETO DE FALA E POR ID, E IDS DE NPC NAO VOLTAM. Enquanto so jogador falava, `_ultimaFala`
		// tinha o tamanho da lista de online; com a IA falando (ver `GameServer.Ia.cs`) ele passou a
		// receber uma entrada por corpo NASCIDO -- e o povoamento renasce o vilarejo a cada 5 min.
		// Numa sessao de horas isso e um dicionario que so cresce, calado: o vazamento classico de
		// "cache indexado por id de coisa que morre".
		_ultimaFala.Remove(npc.Id);

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
	/// E o `npc_pick_body` + `npc_apply_hair` + `npc_wear_simple`/`npc_wear_armor_icon` do original
	/// (PlanetPopulation.dm:301, :339-342, :240-252), com uma diferenca de desenho: la os pools de
	/// corpo e cabelo sao listas escritas a mao no proc; aqui e o catalogo do jogo. Uma lista escrita
	/// a mao envelhece calada quando um sprite e renomeado.
	/// </summary>
	private Appearance AparenciaDeNpc(MoldeDeNpc molde, string raca, string genero, ulong semente)
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
		// ============================ E A ROUPA, QUE ELE NUNCA TEVE ============================
		// *"todo npc ta nascendo SEM ROUPAS"* -- o dono estava certo, e o defeito era literalmente a
		// ausencia destas linhas: este metodo escrevia corpo, tom, cabelo, formas e aura, e **nunca
		// tocava em `ap.Roupa`**. A lista vazia do construtor viajava no fio como zero pecas, e
		// como este e o UNICO caminho de aparencia de NPC do jogo, nenhum NPC -- cidadao, defensor
		// ou chefe -- tinha roupa nenhuma.
		//
		// A TABELA E DO DM e mora no Core (<see cref="RoupaDeNpc"/>), com o `arquivo:linha` de cada
		// escolha. Aqui fica so o recorte de QUEM se veste por ela:
		//
		//   molde com `roupa` cravada  -- vence sempre (e como o chefe se veste: os Androides 17 e
		//                                 18 sao vestidos a mao no DM tambem, BossEvents.dm:499,508)
		//   chefe SEM `roupa` cravada  -- **nao entra no sorteio racial**. E o pedido literal: um
		//                                 chefe cujo sprite ja o veste (Freeza, Cell, Boo) nao pode
		//                                 ganhar um moletom por sorteio. O DM concorda por omissao:
		//                                 as fabricas dos tres nao chamam `npc_wear_*` uma vez.
		//   o resto                    -- a tabela da raca
		//
		// A COR NAO PERSISTE, E NAO PRECISA: um NPC nao tem save (ver o `Berco` la em cima). A roupa
		// dele nao e guardada, e REDEDUZIDA da mesma semente a cada nascimento -- entao o cidadao
		// reposto pela manutencao de 5 min volta com a MESMA roupa da mesma cor, e um servidor
		// reiniciado tambem. Persistir seria mais fraco: um save so pode discordar da semente.
		// ==================================================================================
		var faltando = new List<string>();
		if (molde.Roupa.Length > 0)
			ap.Roupa.AddRange(RoupaDeNpc.Vestir(_visual, raca, genero, semente, molde.Roupa, faltando));
		else if (!molde.EhChefe)
			ap.Roupa.AddRange(RoupaDeNpc.Vestir(_visual, raca, genero, semente, null, faltando));

		if (faltando.Count > 0)
			GD.PushWarning($"[server] molde '{molde.Id}' ({raca}): peca(s) sem arte no port -- "
						 + string.Join(", ", faltando));

		// DEPOIS do `Sanear`, e nao antes: aquele metodo confere o que veio do catalogo (corpo,
		// tom, cabelo, roupa) e nao tem o que dizer sobre uma cor sorteada aqui dentro.
		//
		// E A ROUPA VEM ANTES DELE DE PROPOSITO. `Sanear` e o guarda do catalogo -- ele recusa peca
		// que nao veio de la --, entao vestir DEPOIS seria a segunda forma de vestir, sem guarda
		// nenhum, exatamente o que o pedido proibe. O preco e que uma peca fora do catalogo some em
		// silencio, e por isso o motivo que ele devolve deixou de ser descartado aqui: era assim que
		// "vesti o NPC e ele continua pelado" viraria um misterio de duas horas.
		string queixa = _visual.Sanear(ap, raca, genero);
		if (queixa.Length > 0)
			GD.PushWarning($"[server] aparencia de NPC do molde '{molde.Id}' ({raca}): {queixa}");

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
