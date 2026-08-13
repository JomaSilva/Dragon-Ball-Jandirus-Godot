using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A DIMENSAO MENTAL: meditar abre a porta pra lutar contra si mesmo.
///
/// E o primeiro NPC do port, e ele existe por uma razao de desenho: treinar sozinho socando o ar
/// funciona, mas nao ENSINA -- ninguem aprende a apara, a mira e a distancia sem alguem do outro
/// lado. O clone e o oponente que sempre esta disponivel, e como ele e uma copia exata, a luta
/// mede exatamente a sua habilidade e nao a diferenca de poder.
///
/// UM BOLSO POR PESSOA. A zona e <c>ZoneKey.Interior("Interdimension", id)</c>: o NOME resolve a
/// cena no catalogo (o mapa Interdimension ja existe), e o HASH -- que inclui o id -- e o corte
/// de interesse. Dois jogadores meditando ao mesmo tempo estao no mesmo cenario e em mundos
/// diferentes, sem nenhum codigo de instanciamento.
///
/// O CLONE JOGA PELAS MESMAS REGRAS. Ele nao teleporta, nao atravessa parede e nao bate mais
/// rapido que a cadencia: o cerebro (<see cref="Cerebro"/>) so decide o que APERTAR, e quem move
/// e quem resolve o golpe sao as mesmas funcoes do jogador. NPC que anda por fora das regras e a
/// origem do bug que ninguem reproduz.
/// </summary>
public partial class GameServer
{
	/// <summary>Onde o corpo nasce dentro da dimensao mental (centro do mapa Interdimension).</summary>
	private static readonly Vec2 PosMental = new(250 * 32 + 16, 250 * 32 + 16);

	/// <summary>A que distancia o clone aparece. Perto o bastante pra a luta comecar sozinha.</summary>
	private const float DistanciaDoClone = 96f;

	/// <summary>
	/// ENTRA NA MENTE. So meditando -- e a condicao do original, e ela faz sentido: e um lugar
	/// pra onde se vai por dentro, nao um mapa que se atravessa.
	/// </summary>
	private void EntrarNaMente(ServerPlayer pl)
	{
		if (pl.Peer == null) return;                       // clone nao medita

		// NAO HA GUARDA DE POSSESSAO AQUI, e e de proposito. Meditar ficou fora do `ComandoDeCorpo`
		// (e a saida da fera), mas a PORTA da mente entra pelo canal `C2S.Habilidade`, que esta na
		// lista -- entao o possuido ja e recusado no despacho e nunca chega nesta funcao. Um
		// `if (SemAsRedeas(pl))` aqui seria a mesma verdade escrita duas vezes, e a segunda copia e
		// sempre a que discorda um dia.

		if (pl.CloneId != 0) { Avisar(pl, "voce ja esta na sua mente."); return; }
		if (!pl.Ficha.med) { Avisar(pl, "e preciso estar MEDITANDO (tecla M) pra entrar na propria mente."); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "agora nao."); return; }

		pl.ZonaDeOrigem = pl.Zone;
		pl.PosDeOrigem = pl.Pos;

		ZoneKey mente = ZoneKey.Interior("Interdimension", (ulong)pl.Id);
		MoveToZone(pl.Id, mente, PosMental);

		ServerPlayer clone = CriarClone(pl, mente);
		pl.CloneId = clone.Id;

		Avisar(pl, "voce fecha os olhos. Diante de voce, VOCE -- e ele conhece cada golpe seu.");
		GD.Print($"[server] {pl.Name} entrou na mente (clone id {clone.Id}, BP {clone.Ficha.BP:N0})");
	}

	/// <summary>Sai da mente e desfaz o clone. Chamado pelo jogador, pela morte dele ou pelo logout.</summary>
	private void SairDaMente(ServerPlayer pl, string motivo)
	{
		if (pl.CloneId == 0) return;

		if (_players.TryGetValue(pl.CloneId, out ServerPlayer? clone)) RemoverClone(clone);
		pl.CloneId = 0;

		// volta pra onde estava. Se a origem se perdeu (save antigo, servidor reiniciado no
		// meio), cai na Terra em vez de ficar preso num bolso sem saida.
		ZoneKey destino = pl.ZonaDeOrigem.Name.Length > 0 ? pl.ZonaDeOrigem : SpawnZone;
		Vec2 pos = pl.ZonaDeOrigem.Name.Length > 0 ? pl.PosDeOrigem : SpawnPos;
		MoveToZone(pl.Id, destino, pos);
		Avisar(pl, motivo);
	}

	/// <summary>
	/// O CLONE. Copia a ficha inteira e o corpo -- ele E voce, com a mesma velocidade, a mesma
	/// cadencia e o mesmo poder.
	///
	/// A ficha e copiada por VALOR (uma instancia nova), nao compartilhada: se os dois
	/// apontassem pro mesmo <see cref="Fighter"/>, bater no clone tiraria a SUA vida.
	/// </summary>
	private ServerPlayer CriarClone(ServerPlayer dono, ZoneKey zona)
	{
		var clone = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,                       // e o que faz dele um NPC
			Name = dono.Name + " (mente)",
			Zone = zona,
			Pos = dono.Pos + new Vec2(DistanciaDoClone, 0),
			Race = dono.Race,
			Class = dono.Class,
			// `SpeedStat` NAO E COPIADO do dono desde que o nascimento virou funcao unica: quem o
			// calcula e o `PorNoMundo`, a partir do `Espeed` da ficha -- que e a mesma ficha, entao
			// o numero e o mesmo. Copiar um campo derivado e como ele envelhece.
			// COPIA, e nao a mesma instancia. Dono e clone dividiam o MESMO objeto (e a mesma lista
			// de roupa): tingir uma peca num aparecia no outro, e o `Sanear` de um reescrevia a
			// lista do outro. O `Copiar()` existia pra isto e nunca tinha sido chamado.
			Visual = dono.Visual.Copiar(),
			Genero = dono.Genero,
			Planeta = dono.Planeta,
			Idade = dono.Idade,
			LastInputMs = NowMs(),
			Cerebro = new Cerebro(),
			DonoDoClone = dono.Id,
			Ficha = ClonarFicha(dono.Ficha),
		};

		// A SEQUENCIA DE NASCER UM CORPO SEM DONO E UMA FUNCAO SO (`GameServer.Npc.cs`): statify,
		// velocidade, corpo, entrar no `_players` e na lista da zona, gravidade do chao. Era escrita
		// aqui e na bancada de convivio, e a fabrica de NPC seria a terceira copia.
		PorNoMundo(clone);
		clone.Livro = new Jandirus.Core.Skills.SkillBook();
		clone.Combate.Letal = false;   // a mente nao decepa membro nem mata: e treino

		// a APARENCIA precisa ir pro dono, senao ele ve um boneco sem roupa nem cabelo
		MandarLook(dono, clone);
		return clone;
	}

	/// <summary>
	/// Copia de ficha por serializacao dos campos que importam.
	///
	/// Nao ha `Clone()` no <see cref="Fighter"/> e escrever um a mao seria a mesma lista de
	/// campos que ja deu errado no save -- por isso o save serializa o objeto inteiro. Aqui o
	/// que interessa e o PODER e a energia; buff temporario e estado de luta o clone comeca do
	/// zero, que e o certo: ele e voce em condicoes de treino.
	/// </summary>
	private Fighter ClonarFicha(Fighter f)
	{
		var c = new Fighter
		{
			BP = f.BP,
			BPMod = f.BPMod,
			HP = 100,
			MaxKi = f.MaxKi,
			Ki = f.MaxKi,
			stamina = f.maxstamina,
			maxstamina = f.maxstamina,
			physoff = f.physoff, physdef = f.physdef, technique = f.technique,
			kioff = f.kioff, kidef = f.kidef, kiskill = f.kiskill,
			speed = f.speed, magiskill = f.magiskill,
			physoffMod = f.physoffMod, physdefMod = f.physdefMod, techniqueMod = f.techniqueMod,
			kioffMod = f.kioffMod, kidefMod = f.kidefMod, kiskillMod = f.kiskillMod,
			speedMod = f.speedMod, magiMod = f.magiMod,
		};
		c.Statify();
		return c;
	}

	private void RemoverClone(ServerPlayer clone)
	{
		ZoneList(clone.Zone.Hash).Remove(clone);
		_players.Remove(clone.Id);

		// avisa quem estava vendo -- sem isto o boneco fica congelado na tela do dono
		var w = Protocol.Begin(Protocol.S2C.PeerLeft);
		w.Put(clone.Id);
		if (_players.TryGetValue(clone.DonoDoClone, out ServerPlayer? dono))
			dono.Peer?.Send(w, Protocol.ChannelReliable, LiteNetLib.DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O TICK DE TODO CORPO QUE O SERVIDOR ESTA DIRIGINDO. Pensa e executa. Roda no tick CHEIO,
	/// junto com o combate, porque a decisao mexe em movimento e o movimento e por dt.
	///
	/// ============================ DOIS CASOS, UM SO EXECUTOR ============================
	/// Ate aqui "ter cerebro" e "ser clone" eram a mesma coisa, e o laco lia `DonoDoClone` como se
	/// fosse parte da definicao de NPC. O Oozaru sem controle e o segundo caso: um corpo com PEER
	/// -- um jogador de verdade, logado, olhando pra tela -- que o servidor dirige porque a fera
	/// nao obedece ninguem.
	///
	/// O que muda entre os dois e SO O ALVO e a faxina; movimento, guarda e golpe sao a mesma
	/// dezena de linhas la embaixo, e tem que continuar sendo: o clone ja existe justamente pra
	/// nao haver uma segunda fisica pra quem nao e jogador ("um NPC que se move por fora das
	/// regras vira o bug que ninguem reproduz"). Escrever uma IA nova pro macaco criaria essa
	/// segunda fisica pela porta dos fundos.
	/// ================================================================================
	/// </summary>
	/// <summary>
	/// O BUFFER DOS CORPOS DIRIGIDOS, reusado a cada tique.
	///
	/// ============================ ERA UM `Where().ToList()` A 30 Hz ============================
	/// A lista a parte E necessaria (o `Atacar` pode matar e mexer nas colecoes no meio da volta),
	/// mas ela era ALOCADA de novo 30 vezes por segundo, varrendo TODOS os jogadores -- inclusive
	/// num servidor sem nenhum corpo dirigido, onde o resultado e sempre vazio. Com 20 NPCs numa
	/// zona isso e lixo constante pro coletor, e o custo cresce com a quantidade de gente ONLINE e
	/// nao com a de NPCs.
	///
	/// O `Cerebro != null` continua sendo a unica verdade de posse: o filtro nao mudou, so parou de
	/// alocar. E `Clear()` num `List` reusado nao devolve a capacidade -- e exatamente o que se
	/// quer.
	/// ======================================================================================
	/// </summary>
	private readonly List<ServerPlayer> _dirigidos = [];

	private void TickDosCorposSemDono(double dt)
	{
		_dirigidos.Clear();
		foreach (ServerPlayer p in _players.Values)
			if (p.Cerebro != null) _dirigidos.Add(p);

		foreach (ServerPlayer npc in _dirigidos)
		{
			// ============================ MOB-ZUMBI: A CADA VOLTA, DE NOVO ============================
			// A volta anterior pode ter matado este corpo, tirado o cerebro dele (a posse venceu) ou
			// removido o clone. O DM pagou este defeito caro (`NPCAI.dm:751` -- laco segurando mob ja
			// deletado) e a receita de la e a mesma: **todo laco confere `loc`/vida a cada volta**.
			// Aqui `loc` e "ainda esta no `_players`", e "vida" e o cerebro continuar sendo dele.
			// =====================================================================================
			if (npc.Cerebro == null || !_players.ContainsKey(npc.Id)) continue;

			// ============================ UM NPC QUEBRADO NAO DERRUBA O SERVIDOR ============================
			// O `try` e POR CORPO e nunca em volta do tique. Em volta do tique ele viraria o
			// esconderijo de todo defeito da IA -- o servidor seguiria "funcionando" com a luta toda
			// errada e ninguem saberia. Por corpo, o estrago e um NPC que vira estatua, com o nome
			// dele e a excecao no console; o resto da zona nao percebe.
			// ==========================================================================================
			try { TicarUmCorpo(npc, dt); }
			catch (Exception ex)
			{
				GD.PushError($"[server] IA de '{npc.Name}' (id {npc.Id}) quebrou e o corpo foi solto: {ex}");
				npc.Cerebro = null;
				LargarOInput(npc);
			}
		}
	}

	/// <summary>UM corpo dirigido, um tique. Separado pra o `try` poder ser POR CORPO.</summary>
	private void TicarUmCorpo(ServerPlayer npc, double dt)
	{
		// --- 1. DE QUEM E ESTE CORPO, E ATRAS DE QUEM ELE VAI ---------------------
		ServerPlayer? presa;
		Vec2 destino;

		if (npc.DonoDoClone != 0)
		{
			// O CLONE DA MENTE. Ele so existe enquanto o dono existe e esta no mesmo bolso.
			if (!_players.TryGetValue(npc.DonoDoClone, out ServerPlayer? dono)
				|| dono.Zone.Hash != npc.Zone.Hash)
			{
				RemoverClone(npc);   // o dono sumiu: o clone nao tem por que existir
				return;
			}

			if (npc.Ficha.dead || npc.Ficha.KO)
			{
				// O CLONE CAIU: fim do treino. Ele nao renasce -- vencer a si mesmo e o objetivo,
				// e deixar o corpo no chao pra socar de novo esvaziaria a coisa.
				SairDaMente(dono, "o seu reflexo se desfaz. Voce abre os olhos.");
				return;
			}

			presa = dono;
			destino = dono.Pos;
		}
		else
		{
			// O CORPO POSSUIDO. Ele e de um jogador -- nao se remove, nao se mata: quando a
			// posse acaba, quem a armou tira o cerebro e o dono volta a dirigir.
			//
			// ============================ ESTE RAMO ERA "A FERA", E HOJE SAO DUAS ============================
			// O Oozaru sem controle (`TickDoOozaru`, passo 5) e a furia lendaria
			// (`TickDaFuriaLendaria`) caem os dois aqui, e de proposito: o comportamento que o DM
			// pede pros dois e o mesmo -- *"receita do rampage do Oozaru selvagem"*, palavra do
			// proprio `lssjbuff.dm:566`. O que difere entre elas mora no CEREBRO (temperos
			// diferentes) e no RELOGIO, nao aqui.
			// ==========================================================================================
			//
			// CAIDO, O CORPO PARA. Ele nao "morre" como o clone (o Oozaru sobrevive ao KO no DM
			// -- o que o KO tira e o ganho de maestria; e o `while` do berserk tem `!KO && !dead`);
			// ele so nao age. Sem esta guarda o `Atacar` seria chamado a 30 Hz contra o
			// `PodeAtacar()` de um corpo no chao.
			if (npc.Ficha.dead || npc.Ficha.KO) { npc.Moving = false; return; }

			// "ele sai batendo em qualquer coisa e atacando tudo" (a fera) / "ataca TUDO que ve
			// (player OU NPC)" (a furia): QUALQUER corpo da zona, sem dono, sem faccao, sem alvo
			// marcado. Inclusive outro possuido.
			presa = PresaDaFera(npc);
			destino = presa?.Pos ?? RumoDaFera(npc);
		}

		// --- 2. O QUE O JOGO RESPONDE QUE ELE PODE (1 Hz) -------------------------
		// A leitura varre o catalogo de formas; pagar isso 30 vezes por segundo por corpo seria o
		// custo da IA crescendo com o numero de bichos sem nada em troca. Nada aqui muda em 33 ms.
		Cerebro cerebro = npc.Cerebro!;
		if (cerebro.PrecisaLerCapacidades(dt)) cerebro.Poderes = LerCapacidades(npc);

		// --- 3. A DECISAO E A EXECUCAO, IDENTICAS PROS DOIS -----------------------
		// O RELATO E LIGADO AQUI E NAO NO NASCIMENTO DO CEREBRO, e a diferenca importa: sao tres
		// lugares que criam cerebro (clone, fera, furia) e um quarto viria com a proxima posse.
		// Escrever todo tique e uma atribuicao de bool e nao ha onde esquecer.
		cerebro.Explicando = _diagIa;

		// A LINHA DE VISAO SO E TRACADA PRA QUEM TEM O QUE ATIRAR -- hoje, ninguem (o arsenal de
		// longe sai vazio de todo corpo: ver `TecnicasDeLonge`). Passar a resposta do arsenal como
		// argumento e o que mantem a varredura de segmento fora do caminho de 30 Hz.
		Percepcao p = LerPercepcao(npc, presa, destino, cerebro.Poderes.DeLonge.TemAlguma);
		Plano antes = cerebro.Atual;
		Comando c = cerebro.Pensar(p, dt, _rng);
		AplicarComando(npc, c, dt);

		// ============================ A DECISAO VIRA DADO ============================
		// So na TROCA de plano: um despejo por tique seriam 30 linhas por segundo por corpo, e o
		// console viraria inutil exatamente quando fosse mais preciso.
		//
		// Isto existe pro risco que este desenho ja sabe que vai correr: a calibragem come mais
		// tempo que o codigo, e calibrar sem ver POR QUE ele decidiu e tentativa e erro no escuro.
		// ==========================================================================
		if (_diagIa && cerebro.Atual != antes)
			GD.Print($"[ia] {npc.Name}: {antes} -> {cerebro.Atual}  ({cerebro.Porque})"
				   + $" | vida {p.VidaFrac:0.00} ki {p.KiFrac:0.00} dist {p.Distancia:0} "
				   + $"andares {p.MeuAndar}/{p.AndarDoAlvo}");
	}

	/// <summary>
	/// QUEM A FERA VAI CACAR: o corpo em pe mais proximo da zona, seja quem for.
	///
	/// Sem lista de inimigos e sem alvo marcado de proposito -- marcar alvo e uma decisao, e a
	/// fera nao decide nada. Caidos saem porque o `Cerebro` ja para de bater em quem esta no chao
	/// e um alvo caido so faria o macaco parar de pe em cima de um corpo ate o prazo vencer.
	/// </summary>
	private ServerPlayer? PresaDaFera(ServerPlayer fera)
	{
		ServerPlayer? melhor = null;
		float perto = float.MaxValue;

		foreach (ServerPlayer o in ZoneList(fera.Zone.Hash))
		{
			if (o.Id == fera.Id || o.Ficha.dead || o.Ficha.KO) continue;
			float d2 = (o.Pos - fera.Pos).LengthSquared;
			if (d2 >= perto) continue;
			perto = d2; melhor = o;
		}
		return melhor;
	}

	/// <summary>
	/// PRA ONDE A FERA VAI QUANDO NAO HA NINGUEM. Um ponto 200 px a frente, numa direcao que troca
	/// a cada 4 s -- o macaco vagando e derrubando o que estiver no caminho.
	///
	/// FUNCAO PURA de (id, balde de tempo): nao ha campo de "destino atual" pra guardar, sanear e
	/// esquecer de limpar. Como o ponto e recalculado a partir da posicao ATUAL a cada tique, ele
	/// nunca e alcancado -- e o que faz a fera ANDAR na direcao em vez de parar num alvo.
	/// </summary>
	private Vec2 RumoDaFera(ServerPlayer fera)
	{
		// splitmix64 em cima de (id, balde): mistura barata e deterministica. `_rng` NAO serve
		// aqui -- ele daria uma direcao nova a cada tique e a fera tremeria no lugar.
		ulong h = (ulong)fera.Id * 0x9E3779B97F4A7C15UL ^ (ulong)(NowMs() / 4000) * 0xBF58476D1CE4E5B9UL;
		h ^= h >> 30; h *= 0xBF58476D1CE4E5B9UL;
		h ^= h >> 27; h *= 0x94D049BB133111EBUL;
		h ^= h >> 31;

		double ang = h % 3600 / 3600.0 * Math.Tau;
		return fera.Pos + new Vec2((float)Math.Cos(ang), (float)Math.Sin(ang)) * 200f;
	}

	/// <summary>
	/// ============================ ESTE CORPO AINDA E DE QUEM ESTA NA TELA? ============================
	/// NAO, se ele tem <see cref="ServerPlayer.Cerebro"/>: quem o dirige e o <see cref="TickDosCorposSemDono"/>.
	/// E a MESMA verdade que decide dirigir, e nao uma segunda conta que precisa concordar com ela --
	/// duas contas um dia discordam, e o resultado seria um jogador com o input bloqueado e ninguem
	/// dirigindo: uma estatua sem explicacao.
	///
	/// ISTO ERA `OozaruSemControle`, e o `Oozaru != Nao` que ele tinha a mais foi DELETADO junto com o
	/// nome. Ele existia so "pra ninguem confundir com o clone", e nao protegia nada: clone nao tem
	/// `Peer`, entao nao manda input nem recebe snapshot -- ele nunca chegava a estas perguntas. O que
	/// o nome antigo escondia e que a regra nao e do macaco: e de QUALQUER corpo que o servidor esteja
	/// dirigindo. O proximo (uma possessao, um controle mental, um NPC que toma um corpo) ja nasce
	/// obedecendo, em vez de repetir esta caca.
	/// ============================================================================================
	/// </summary>
	private static bool SemAsRedeas(ServerPlayer pl) => pl.Cerebro != null;

	/// <summary>
	/// ============================ O CORPO LARGA O QUE O DONO MANDOU ============================
	/// Chamado no instante em que o servidor assume um corpo, por QUALQUER motivo. Sem isto o ultimo
	/// pacote de input fica pendurado e o primeiro instante da posse e o boneco deslizando na direcao
	/// em que o jogador estava indo.
	///
	/// O QUE FICAVA PENDURADO, e nenhum destes morre sozinho:
	///   * `Correndo` -- o `EstadoDe` calcula `Correndo &amp;&amp; Moving`, e a IA repoe o `Moving` no
	///     tique seguinte; pior, o `dashing` continuava valendo na conta de dano (+2 pra quem bate,
	///     x1,25 pra quem apanha) pela posse inteira;
	///   * `QuerSubir`/`QuerDescer` -- sao PEDIDOS continuos que so morrem quando o proximo pacote de
	///     input diz o contrario, e o cliente para de mandar input agora (ver `LocalPlayer`). Quem for
	///     possuido com a tecla de subir apertada subiria pra sempre, drenando Ki, sem nada explicando
	///     por que;
	///   * a guarda -- um corpo dirigido pela IA que continuasse aparando por conta do dono seria a
	///     unica parte dele que ainda obedece;
	///   * **a CARGA DE KI** -- e esta faltava. `Carregando` PRENDE o corpo (o portao do `Input`
	///     recusa andar carregando, e agora o `PodeMexerOCorpo` diz isso pros dois lados), entao
	///     quem fosse possuido com o C apertado ficaria plantado no chao com aura acesa **pela posse
	///     inteira**, sem nada explicando por que a fera nao anda. E o `PararCarga` e o unico
	///     caminho que desliga as tres partes do estado juntas (a flag, a aura e o efeito no
	///     cliente) -- zerar `pl.Carregando` na mao deixaria o zumbido tocando.
	///
	/// ============================ E FOI POR ISTO QUE ESTA FUNCAO DEIXOU DE SER `static` ============================
	/// `PararCarga` e de instancia (ela manda pacote). O `static` daqui era o que fazia a falta
	/// passar despercebida: nao dava pra chamar, entao ninguem tentou.
	/// ========================================================================================================
	///
	/// ============================ ISTO ERA O MIOLO DO `TomarAsRedeas` DO OOZARU ============================
	/// Saiu de la e veio pra ca junto com o <see cref="SemAsRedeas"/> e o <see cref="DevolverAsRedeas"/>,
	/// pelo mesmo motivo que aquele comentario ja dava: **a regra nao e do macaco**. Com a furia
	/// lendaria virando a segunda possessao do jogo, deixa-la la significaria copiar seis linhas -- e
	/// a copia que esquecesse o `dashing` daria um bug de DANO que ninguem ligaria a possessao.
	/// ================================================================================================
	/// </summary>
	private void LargarOInput(ServerPlayer pl)
	{
		pl.Moving = false;
		pl.Correndo = false;
		pl.Ficha.dashing = false;
		pl.QuerSubir = false;
		pl.QuerDescer = false;
		pl.Combate.Guardar(false);
		PararCarga(pl);
	}

	/// <summary>
	/// DEVOLVE O CORPO AO DONO. Fim de prazo, rabo cortado, morte, meditacao (a fera) ou a furia
	/// passando (o Legendary) -- em todos, a posse acabando E o unico jeito de recuperar as redeas.
	///
	/// So mexe em corpo POSSUIDO POR POSSESSAO (`DonoDoClone == 0`): tirar o cerebro de um clone o
	/// deixaria parado pra sempre no bolso da mente, vivo e burro.
	///
	/// MORA AQUI E NAO NO ARQUIVO DO OOZARU desde que passou a ter dois chamadores -- ver
	/// <see cref="LargarOInput"/> pro mesmo argumento, e o rodape do `GameServer.Oozaru.cs`, que ja
	/// dizia onde estas tres funcoes deviam morar.
	/// </summary>
	private void DevolverAsRedeas(ServerPlayer pl)
	{
		if (pl.Cerebro == null || pl.DonoDoClone != 0) return;
		pl.Cerebro = null;
		pl.Moving = false;
		pl.LastInputMs = NowMs();   // senao o primeiro input do dono valeria pelo tempo todo da possessao
		// nao avisa: quem chama ja esta dizendo POR QUE a posse acabou -- e "o corpo volta a ser seu"
		// colado em "a fera se cansa" e a mesma frase duas vezes.
	}

	/// <summary>
	/// ============================ O QUE OS DEDOS DO DONO NAO PODEM MAIS FAZER ============================
	/// Os comandos que MEXEM NO CORPO. Enquanto o servidor dirige, eles nao chegam -- e o dono dizia:
	/// "eu ainda posso tentar mexer ai ele faz animaçao mas continua deslizando".
	///
	/// A recusa mora no SERVIDOR e nao so no cliente por um motivo velho deste arquivo: um cliente
	/// modificado continuaria socando com o corpo da fera. O cliente tambem se trava (ver
	/// `LocalPlayer`), mas isso e cortesia -- pra o dono nao ver a animacao de um golpe que nao vai
	/// acontecer.
	///
	/// O QUE NAO ESTA NA LISTA, E POR QUE:
	///   * `Activity` (T/M) -- MEDITAR E A SAIDA. E o `angertick` do DM: quem nao tem pericia so
	///     recobra a razao respirando fundo (ver `TickDoOozaru`, passo 2). Barrar isto transformaria
	///     a paralisia em punicao sem resposta, que e exatamente o que o desenho evita;
	///   * falar, menus, ficha, aba de cargo -- nada disso e o corpo. Perder as redeas nao e perder
	///     a boca nem a interface.
	/// ================================================================================================
	/// </summary>
	private static bool ComandoDeCorpo(Protocol.C2S id) => id is Protocol.C2S.Action
		or Protocol.C2S.Guard or Protocol.C2S.Carregar or Protocol.C2S.Habilidade
		or Protocol.C2S.Transformar or Protocol.C2S.Zanzoken;

	/// <summary>Manda a aparencia de um corpo pra um jogador (o clone precisa disto pra existir na tela).</summary>
	private static void MandarLook(ServerPlayer para, ServerPlayer quem)
	{
		var w = Protocol.Begin(Protocol.S2C.PeerLook);
		w.Put(quem.Id);
		w.Put(quem.Name);
		w.Put(quem.Race);
		w.Put(quem.Genero);
		w.PutAppearance(quem.Visual);
		para.Peer?.Send(w, Protocol.ChannelReliable, LiteNetLib.DeliveryMethod.ReliableOrdered);
	}
}
