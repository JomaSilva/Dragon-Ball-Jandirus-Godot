using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// O CLONE E OS CORPOS QUE O SERVIDOR DIRIGE.
///
/// ============================ A PORTA DA MENTE MUDOU DE ARQUIVO ============================
/// `EntrarNaMente`/`SairDaMente` moraram aqui enquanto "a mente" e "o clone" eram a mesma coisa. Com
/// a Camada 2 elas deixaram de ser: ha gente na mente sem clone nenhum (o visitante), e ha corpo
/// erguido pela mente que nao e clone (o chefe convocado). As regras do LUGAR foram pra
/// `GameServer.Mente.cs`; o que ficou aqui e o CORPO -- o reflexo e quem o dirige.
/// ======================================================================================
///
/// O clone e o primeiro NPC do port, e ele existe por uma razao de desenho: treinar sozinho socando
/// o ar funciona, mas nao ENSINA -- ninguem aprende a apara, a mira e a distancia sem alguem do
/// outro lado. O clone e o oponente que sempre esta disponivel, e como ele e uma copia exata, a luta
/// mede exatamente a sua habilidade e nao a diferenca de poder.
///
/// O CLONE JOGA PELAS MESMAS REGRAS. Ele nao teleporta, nao atravessa parede e nao bate mais
/// rapido que a cadencia: o cerebro (<see cref="Cerebro"/>) so decide o que APERTAR, e quem move
/// e quem resolve o golpe sao as mesmas funcoes do jogador. NPC que anda por fora das regras e a
/// origem do bug que ninguem reproduz.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// O CLONE. Copia a ficha inteira e o corpo -- ele E voce, com a mesma velocidade, a mesma
	/// cadencia e o mesmo poder.
	///
	/// A ficha e copiada por VALOR (uma instancia nova), nao compartilhada: se os dois
	/// apontassem pro mesmo <see cref="Fighter"/>, bater no clone tiraria a SUA vida.
	///
	/// O QUE ELE COPIA MORA NUM LUGAR SO -- <see cref="EspelharODono"/> --, e o nascimento e apenas o
	/// PRIMEIRO chamador dele: o segundo e a volta do reflexo derrotado. Ver o cabecalho de la.
	/// </summary>
	private ServerPlayer CriarClone(ServerPlayer dono, ZoneKey zona)
	{
		var clone = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,                       // e o que faz dele um NPC
			Name = dono.Name + " (mente)",
			Zone = zona,
			Pos = dono.Pos + new Vec2(DistanciaDoOponente, 0),
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

			// ============================ O REFLEXO TEM TEMPERO PROPRIO NO DM, E O PORT USAVA O DE FABRICA ============================
			// **LITERAL**: `behavior_vals = list(90, 70, 0, 70)` (`MindMeditate.dm:319`), com o
			// comentario do autor -- *"destemido, agressivo, SEM misericordia (e a sua sombra...)"*.
			// Sao coragem 90, furia 70, bondade 0 e logica 70, e a conversao e a MESMA do
			// `Temperamento` (que nao da pra chamar aqui: ele parte de um molde do `npcs.json`, e o
			// reflexo nao tem molde -- ele e a ficha do dono).
			//
			// Ele nascia com `new Cerebro()`, ou seja o tempero medio de fabrica (o equivalente a
			// 50 em tudo). A diferenca que mais importa e a CORAGEM: com a fuga portada, um reflexo
			// de coragem 50 comecaria a correr do dono no fim de um treino apertado -- e a sombra de
			// alguem nao foge. O numero 90 do original ja dizia isso antes de a fuga existir.
			// ==================================================================================================================
			Cerebro = new Cerebro
			{
				VidaCautelosa = 0.9 * (1 - 0.90),   // coragem 90: praticamente nao recua, nunca foge
				ChanceDePesado = 0.10 + 0.5 * 0.70, // furia 70
				Inteligencia = 0.70,                // logica 70
				Disciplina = 0.70,
			},
			DonoDoClone = dono.Id,
			Ficha = new Fighter(),
		};

		// O ESPELHO E CHAMADO COM O CORPO NA MAO e nao dentro do inicializador, porque ele escreve nos
		// DOIS lados: a ficha e o pino (`BpDaMente`). Ver <see cref="EspelharODono"/>.
		EspelharODono(clone, dono.Ficha);

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
	/// ============================ O REFLEXO E O DONO **AGORA**, INTEIRO ============================
	/// A unica funcao que ANCORA a ficha do reflexo, e ela e chamada de dois lugares: o nascimento
	/// (<see cref="CriarClone"/>) e cada volta dele do chao (<see cref="ReerguerOOponente"/>). Ela
	/// tambem escreve o PINO (<see cref="ServerPlayer.BpDaMente"/>), que e o que segura o numero entre
	/// uma ancoragem e a seguinte -- ver o re-pino no <see cref="TicarUmCorpo"/>.
	///
	/// Nao ha `Clone()` no <see cref="Fighter"/> e escrever um a mao seria a mesma lista de campos que
	/// ja deu errado no save -- por isso o save serializa o objeto inteiro. Aqui o que interessa e o
	/// PODER e a energia; buff temporario e estado de luta o reflexo comeca do zero, que e o certo:
	/// ele e voce em condicoes de treino.
	///
	/// ============================ O BP E O **EXPRESSO** DO DONO, e nao o base ============================
	/// **LITERAL**: `C.mind_seed_bp = max(round(M.expressedBP), 1)` seguido de `C.BP = C.mind_seed_bp`
	/// (`MindMeditate.dm:254-255`), com o comentario do autor ao lado -- *"espelho dos stats de
	/// combate, com o MESMO poder expresso (forma atual inclusa)"*.
	///
	/// Aqui isso era `BP = f.BP`, o poder BASE, e a diferenca aparecia na hora em que o sistema mais
	/// importa: quem entrava na mente em SSJ3 encontrava um reflexo na forma base, com um vigesimo do
	/// seu poder, e o "desafio e VOCE MESMO" virava um saco de pancada. O reflexo nao tem forma
	/// nenhuma (nasce com <see cref="Jandirus.Core.Skills.SkillBook"/> vazio, entao `formBuff` e 1) --
	/// e por isso o expresso do dono pode entrar como BASE dele sem contar a forma duas vezes.
	/// ================================================================================================
	///
	/// ============================ A RACA E A IDADE ENTRAM, E ELAS FALTAVAM ============================
	/// `Fighter.Race` e `Fighter.Idade` nao eram copiados, e nenhum dos dois e enfeite:
	/// `PowerLevel` calcula `AgeDiv = DivisorDeIdade(Race, Idade)` (`Fighter.Power.cs:70`), entao o
	/// reflexo de um Namekiano de 300 anos vinha com o divisor de um generico de 18. Ninguem veria
	/// isso como erro; veria como "meu clone e mais fraco do que eu" -- que e o sintoma mais dificil
	/// de rastrear que este sistema pode ter.
	/// ============================================================================================
	///
	/// "INTEIRO" e o resto: vida cheia, Ki cheio, folego cheio, barriga cheia e raiva calma -- os
	/// `C.HP = 100`, `C.Ki = C.MaxKi`, `C.stamina = C.maxstamina`, `C.currentNutrition = 100` e
	/// `C.Anger = 100` do `spawn_clone` (`:267-275`), no mesmo lugar de proposito: o estado com que
	/// ele NASCE e o estado com que ele VOLTA, e uma lista escrita duas vezes divergiria na primeira
	/// linha nova.
	/// </summary>
	private static void EspelharODono(ServerPlayer corpo, Fighter dono)
	{
		Fighter reflexo = corpo.Ficha;
		reflexo.Race = dono.Race;
		reflexo.Class = dono.Class;
		reflexo.Idade = dono.Idade;

		reflexo.BP = Math.Max(dono.expressedBP, 1);
		reflexo.BPMod = dono.BPMod;

		reflexo.physoff = dono.physoff; reflexo.physdef = dono.physdef; reflexo.technique = dono.technique;
		reflexo.kioff = dono.kioff; reflexo.kidef = dono.kidef; reflexo.kiskill = dono.kiskill;
		reflexo.speed = dono.speed; reflexo.magiskill = dono.magiskill;
		reflexo.physoffMod = dono.physoffMod; reflexo.physdefMod = dono.physdefMod;
		reflexo.techniqueMod = dono.techniqueMod;
		reflexo.kioffMod = dono.kioffMod; reflexo.kidefMod = dono.kidefMod; reflexo.kiskillMod = dono.kiskillMod;
		reflexo.speedMod = dono.speedMod; reflexo.magiMod = dono.magiMod;

		reflexo.HP = 100;
		reflexo.KO = false;
		reflexo.dead = false;
		reflexo.maxstamina = dono.maxstamina;
		reflexo.stamina = dono.maxstamina;
		reflexo.staminadeBuff = 100;
		reflexo.Anger = 100;

		// `Statify` ANTES do Ki e da barriga: e ele quem escreve `MaxKi` e o metabolismo de que o
		// tanque depende. Encher primeiro seria encher o tanque do tique anterior.
		reflexo.Statify();
		reflexo.Ki = reflexo.MaxKi;
		reflexo.CurrentNutrition = Jandirus.Core.Stats.Nutricao.Tanque(reflexo.Metabolism);

		// O `powerlevel()` do DM na mesma sequencia (`:273`): sem ele o `expressedBP` do reflexo fica
		// um tique atrasado, e quem le o poder dele -- o Sense, o dano, a decisao do proprio cerebro --
		// leria o poder de antes da reancoragem.
		reflexo.PowerLevel();

		// O PINO -- o `mind_seed_bp` (ver <see cref="ServerPlayer.BpDaMente"/>). Escrito DEPOIS da
		// conta e a partir do que ficou na ficha, e nao do argumento: se um dia o espelho passar a
		// arredondar ou a limitar o BP, o pino acompanha sozinho em vez de guardar o numero que a ficha
		// nao aceitou -- e o reflexo seria puxado, tique a tique, pra um valor que ele nunca teve.
		corpo.BpDaMente = reflexo.BP;
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

	/// <summary>
	/// AS ZONAS EM QUE HA GENTE DE VERDADE OLHANDO -- o `planet_player_count` do original
	/// (PlanetPopulation.dm:38-48), que o DM atualiza a cada 2 s varrendo so a `player_list`.
	///
	/// Reusado a cada tique como o <see cref="_dirigidos"/>, e preenchido NO MESMO laco que ele:
	/// uma segunda varredura de `_players` a 30 Hz custaria o mesmo que a IA inteira dos corpos.
	/// </summary>
	private readonly HashSet<ulong> _zonasComGente = [];

	/// <summary>
	/// O RELOGIO DO MUNDO, em segundos SIMULADOS -- o `world.time` do BYOND.
	///
	/// ============================ NAO E `NowMs()`, E A DIFERENCA APARECEU NA BANCADA ============================
	/// O passeio do habitante nasceu lendo o relogio de PAREDE (como o `RumoDaFera` ja fazia), e a
	/// bancada mediu **0 px em 3600 tiques**: um laco de tique roda 3600 vezes em menos de um segundo
	/// de parede, entao o balde de 7 s nunca virava e o comportamento era INEXERCITAVEL. O codigo
	/// podia estar quebrado que a medida seria a mesma.
	///
	/// E nao e so a bancada: o resto do tique e todo por `dt` (forma, carga, voo, combate). Um
	/// comportamento por relogio de parede anda mais devagar num servidor com tique atrasado e mais
	/// rapido num acelerado -- que e o oposto do que "1 tile a cada 20 segundos DE JOGO" quer dizer.
	/// O `sleep()` do DM tambem conta tiques do mundo, nao segundos do sistema operacional.
	/// ======================================================================================================
	/// </summary>
	private double _relogioDoMundo;

	private void TickDosCorposSemDono(double dt)
	{
		_relogioDoMundo += dt;
		_dirigidos.Clear();
		_zonasComGente.Clear();
		foreach (ServerPlayer p in _players.Values)
		{
			if (p.Cerebro != null) _dirigidos.Add(p);

			// SO JOGADOR CONTA. Um planeta cheio de NPC continua sendo um planeta vazio pra esta
			// pergunta -- e o `if(!P || !P.client...)` do `planet_activity_loop`. Sem isso os cidadaos
			// manteriam uns aos outros acordados pra sempre, que e o oposto do anti-lag. E o boneco de
			// quem esta meditando tambem nao acorda a zona: o dono dele ja esta la, pelo proprio corpo.
			//
			// ============================ E MORTO CONTA, e antes nao contava ============================
			// A linha nasceu com um `&& !p.Ficha.dead`, e ele era um DEFEITO VISIVEL: morrer leva ~15 s
			// ate o corpo ir pra mesa do Enma, e durante esses 15 s a zona ficava "vazia" com o dono
			// dela olhando pra tela. O vilarejo inteiro virava estatua no exato instante em que o
			// jogador caia -- o oposto do pedido do dono (*"planetas q os jogadores estao os npcs podem
			// agir EM TEMPO REAL"*), e um caso em que o anti-lag DEGRADA quem esta vendo.
			//
			// A pergunta aqui e "ha uma tela mostrando esta zona?", e nao "ha alguem em condicoes de
			// lutar". Quem morreu continua com a tela ligada. Quem NAO conta continua nao contando --
			// o crivo e o `EhJogador` (`Core/Npc/Gente.cs`), que ja corta o boneco largado, o reflexo da
			// mente e todo NPC; morrer nao deixa de ser gente.
			// ======================================================================================
			if (EhJogador(p)) _zonasComGente.Add(p.Zone.Hash);
		}

		// QUEM ACABOU DE ESVAZIAR fica marcado aqui, e so aqui -- ver `GameServer.Embaralho.cs`.
		MarcarZonasQueEsvaziaram();

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

	/// <summary>
	/// ============================ ESTE CORPO PENSA NESTE TIQUE? ============================
	/// **LITERAL**: `if(!planet_has_players(pop_planet)) sleep(150); continue` -- o
	/// `idle_wander_loop` com o comentario do autor em maiusculas, *"ANTI-LAG: planeta sem players =
	/// congela (nem anda)"* (PlanetPopulation.dm:129). E o pedido do dono com outras palavras:
	/// *"os NPCS TEREM SUA MENTE (IA) FUNCIONANDO SOMENTE QUANDO TIVER UM JOGADOR NO PLANETA"*.
	///
	/// ============================ E "DESLIGADA", E NAO "DE VEZ EM QUANDO" ============================
	/// O dono deixou os dois em aberto (*"seria DESLIGADA ou teria seus calculos chamados A CADA UM
	/// GRANDE INTERVALO"*). Desligada, e a medida diz por que: um corpo congelado custa **0,32 us**
	/// por tique aqui e acorda-lo custa **0,11 us** -- ou seja, o pensamento raro nao economizaria
	/// nada mensuravel, e ele COMPRARIA um defeito. Um cidadao que decide uma vez por minuto anda um
	/// tile e para no meio do passo; um chefe re-planeja com uma percepcao de 60 s de idade e persegue
	/// uma sombra. Comportamento incoerente sem plateia nenhuma pra apreciar a incoerencia.
	///
	/// O buraco que o intervalo grande existiria pra tapar -- *"pra n parecer q os npcs quase n se
	/// moveram"* -- e tapado por outra coisa, e de graca: o embaralho de posicoes
	/// (`GameServer.Embaralho.cs`), que faz o trabalho de cinco minutos de passeio numa conta so.
	/// ==========================================================================================
	///
	/// ============================ POR QUE SUBIR ISTO NAO MUDA O RESULTADO ============================
	/// Antes a guarda era `presa == null && zona vazia`, DEPOIS de procurar presa. Aqui ela e feita
	/// ANTES, e as duas so seriam diferentes se `PresaDoNpc` pudesse devolver alguem numa zona sem
	/// jogador. Nao pode -- as tres portas de presa exigem exatamente o que `_zonasComGente` mede:
	///
	///   * o rancor do cidadao        -- `EhJogador(quem) && quem.Zone == npc.Zone && !dead`;
	///   * a busca alargada           -- `EhJogador(o)`, no laco;
	///   * o hostil (`PresaDoHostil`) -- `EhJogador` nos dois ramos, engajada e adocao.
	///
	/// Sobra UMA que nao exige jogador: o alvo IMPOSTO PELO ROTEIRO (o `bev_prey` do DM -- o Cell
	/// atras de um androide nocauteado). Por isso ela e perguntada aqui, e ela e um `TryGetValue`.
	/// Ou seja: o resultado e o mesmo e o preco caiu de uma varredura de zona pra duas consultas.
	///
	/// **E `PresaEngajada` deixar de ser reescrito com a zona vazia nao vaza**: o unico leitor dele
	/// e o proprio `PresaDoHostil`, e ele revalida o alvo inteiro (gente, vivo, de pe, mesma zona,
	/// dentro do leash) antes de devolve-lo. Campo velho ali e indistinguivel de campo zerado.
	/// ==========================================================================================
	///
	/// Nao vale pro corpo POSSUIDO (Oozaru selvagem, furia lendaria) nem pro que a mente ergueu: os
	/// dois so existem ao lado de um jogador, entao a zona deles tem gente por definicao -- e testar
	/// seria pagar pra confirmar o obvio no caso em que ha alguem vendo.
	/// </summary>
	private bool MenteDormindo(ServerPlayer npc)
	{
		if (npc.Papel == null || npc.DonoDoClone != 0) return false;
		if (_zonasComGente.Contains(npc.Zone.Hash)) return false;
		if (PresaImpostaPeloRoteiro(npc) != null) return false;

		npc.Moving = false;
		return true;
	}

	/// <summary>UM corpo dirigido, um tique. Separado pra o `try` poder ser POR CORPO.</summary>
	private void TicarUmCorpo(ServerPlayer npc, double dt)
	{
		// --- 0. SEM PLATEIA, SEM MENTE -------------------------------------------
		// O congelamento anti-lag saiu de dentro do ramo do habitante e subiu pra ca. Ver
		// <see cref="MenteDormindo"/>: la esta a prova de que subir NAO muda o resultado, e aqui
		// esta o que se ganha com isso -- um corpo congelado deixou de pagar tres coisas que ele
		// pagava caladamente todo tique, a 30 Hz, so pra descobrir que nao havia ninguem:
		//
		//   * o REFLEXO do contra-feixe (quatro consultas de dicionario por corpo, abaixo);
		//   * a busca de presa (`PresaDoHostil` VARRE A LISTA DA ZONA INTEIRA -- 40 corpos em Vegeta
		//     eram 40 iteracoes por chefe por tique, num planeta sem ninguem);
		//   * o passo e a decisao, que ja estavam cortados.
		//
		// Sem esta guarda o reflexo era um absurdo escrito: um feixe so nasce de quem age, e numa
		// zona congelada NINGUEM age -- procurar raio inimigo ali e procurar o que nao pode existir.
		if (MenteDormindo(npc)) return;

		// --- 0,5. O REFLEXO: TEM RAIO VINDO? -------------------------------------
		// Antes de qualquer decisao, e de proposito. Um feixe chegando nao e uma opcao tatica -- e
		// uma coisa que vai acertar em menos de um segundo, e o DM tratou isso FORA da arvore de
		// decisao, com recarga propria (`NPCAI.dm:374`). Ver `TickDoContraFeixe`.
		TickDoContraFeixe(npc, NowMs());

		// --- 1. DE QUEM E ESTE CORPO, E ATRAS DE QUEM ELE VAI ---------------------
		ServerPlayer? presa;
		Vec2 destino;

		if (npc.DonoDoClone != 0)
		{
			// ============================ O CORPO QUE A MENTE ERGUEU ============================
			// Sao DOIS hoje -- o reflexo e o chefe convocado (`ChamarChefe`, `GameServer.Mente.cs`) --
			// e eles compartilham este ramo de proposito: os dois vem atras do dono, os dois somem com
			// ele, os dois se REERGUEM quando caem e so a morte dos dois encerra o transe. O que difere
			// e a FICHA (o reflexo copia a sua; o chefe tem a ficha pronta dele, com degraus e roteiro),
			// e ficha nao e comportamento.
			// ================================================================================
			if (!_players.TryGetValue(npc.DonoDoClone, out ServerPlayer? dono)
				|| dono.Zone.Hash != npc.Zone.Hash)
			{
				RemoverNpc(npc);   // o dono sumiu: nao ha por que este corpo existir
				return;
			}

			if (npc.Ficha.dead)
			{
				// ============================ MORRER **ENCERRA** O TRANSE, e nocautear nao ============================
				// Sao as duas unicas maneiras de derrubar o corpo que a mente ergueu, e a resposta a
				// cada uma e o oposto da outra de proposito:
				//
				//   * NOCAUTE -- ele se reergue (ver <see cref="ReerguerOOponente"/>). E o
				//     `clone_watch()` do DM, e o que faz do reflexo um parceiro de treino INFINITO;
				//   * MORTE -- fim. E a unica vitoria que termina a sessao, e ela e OPCIONAL: so
				//     acontece com o golpe LETAL ligado (o padrao e desligado, e ai o piso por membro
				//     do `MeleeResolver` impede que qualquer corpo chegue no `DeveMorrer`). Quem quer
				//     encerrar por vitoria liga o letal e destroi a coisa; quem quer treinar, nao liga
				//     e ela levanta pra sempre.
				//
				// No DM esta metade nem chega a existir: o `mind_enter` guarda o `murderToggle` do
				// meditador e o zera na entrada (`mind_premurder`, `:375-376`), entao la o reflexo E
				// indestrutivel. **Aqui e uma escolha do jogador**, e ela e a saida por vitoria que o
				// port passou a precisar no instante em que o nocaute deixou de ser uma.
				//
				// A FRASE VEM DO QUE ELE E, e nao de um campo: "reflexo" pra quem tem ficha copiada,
				// o NOME pra quem tem ficha pronta de chefe. Guardar o tipo seria a segunda verdade
				// sobre uma coisa que o corpo ja diz.
				// ================================================================================================
				SairDaMente(dono, npc.Papel is { EhChefe: true }
					? $"{npc.Name} se desfaz em nada. Voce abre os olhos."
					: "o seu reflexo se desfaz. Voce abre os olhos.");
				return;
			}

			if (npc.Ficha.KO)
			{
				// CAIDO, NAO AGE -- a mesma guarda dos outros dois ramos, e pelo mesmo motivo: sem ela o
				// `Atacar` seria chamado a 30 Hz contra o `PodeAtacar()` de um corpo no chao.
				npc.Moving = false;
				DerrubadoNaMente(npc, dono);
				return;
			}

			// DE PE DEPOIS DE TER CAIDO: o relogio do nocaute venceu e o `CombatState.Tick` ja o
			// levantou neste MESMO tique (o `TickCombate` roda antes do `TickDosCorposSemDono`). Falta
			// a outra metade do `clone_watch`, que e a cura.
			if (npc.CaiuNaMente) ReerguerOOponente(npc, dono);

			// ============================ O BP DO REFLEXO NAO ANDA SOZINHO ============================
			// O `NPCTicker()` do `MindClone` (`MindMeditate.dm:322-327`) inteiro, e ele e a mesma forma
			// (e a mesma tolerancia) do pino de chefe do `TickDoRoteiro`: o corpo passa pelos mesmos
			// lacos de ficha de um jogador, entao **lutar rende BP pra ele tambem**
			// (`Fighter.FightGain`). Sem esta reescrita, um treino longo deixaria um reflexo mais forte
			// do que o dono que o gerou -- crescendo por combate, com o dono limitado a um quarto do
			// ganho la dentro. Ver <see cref="ServerPlayer.BpDaMente"/>.
			//
			// A LEMBRANCA DE CHEFE NAO PASSA POR AQUI (`BpDaMente` zero): o pino dela e o
			// `Papel.BpsPinados`, reancorado pelo `TickDoRoteiro`. Um segundo pino sobre o mesmo numero
			// seria duas contas discordando no meio da luta.
			// ======================================================================================
			if (npc.BpDaMente > 0 && Math.Abs(npc.Ficha.BP - npc.BpDaMente) > 0.5)
			{
				npc.Ficha.BP = npc.BpDaMente;
				npc.Ficha.Statify();
				npc.Ficha.PowerLevel();
				npc.SigAtributos = "";
			}

			presa = dono;
			destino = dono.Pos;
		}
		else if (npc.Papel != null)
		{
			// ============================ O HABITANTE. ELE NAO E A FERA. ============================
			// Este ramo nasceu com o povoamento e ele existe por uma razao so: `PresaDaFera` devolve
			// *o corpo em pe mais proximo, seja quem for*. Um cidadao com esse alvo sai socando o
			// primeiro jogador que pousar no planeta -- e o `mob/npc/Citizen` do DM e o oposto disso
			// (`AIAlwaysActive = 0`, *"peaceful until hit"*).
			// ==================================================================================

			// MORTO SAI DO MUNDO, e nao renasce (ver o `TickCombate`). Aqui so se para de dirigir:
			// quem o remove e o laco de combate, no proximo tique, com a lista ja materializada.
			if (npc.Ficha.dead) { npc.Moving = false; return; }

			// CAIDO, PARA. Mesma guarda do corpo possuido, e pelo mesmo motivo: sem ela o `Atacar`
			// seria chamado a 30 Hz contra o `PodeAtacar()` de um corpo no chao.
			if (npc.Ficha.KO) { npc.Moving = false; return; }

			// O CONGELAMENTO ANTI-LAG NAO MORA MAIS AQUI -- ele subiu pro passo 0 (`MenteDormindo`),
			// porque nesta altura o custo que ele existe pra evitar JA FOI PAGO: `PresaDoNpc` de um
			// chefe e uma varredura da zona inteira.
			presa = PresaDoNpc(npc);

			// ============================ HABITANTE NAO E FERA VAGANDO ============================
			// O ramo antigo mandava `RumoDaFera` pra todo mundo sem alvo, e a bancada mediu o
			// resultado: **2400 px em 20 s** -- 75 tiles. Em dez minutos o vilarejo inteiro teria se
			// espalhado pelo mapa e ninguem mais estaria na cidade.
			//
			// Ela e a receita do rampage do Oozaru, e nao a do `idle_wander_loop`
			// (PlanetPopulation.dm:122-131), que e o oposto: `sleep(rand(45,95))` -- 4,5 a 9,5 s --
			// e entao `if(prob(35)) step_rand(src)`, **UM tile**. Da ~1 tile a cada 20 s: o
			// habitante que muda de lugar de vez em quando, que e o que faz a cidade parecer viva
			// sem ninguem sair dela.
			// =================================================================================
			if (presa == null)
			{
				Vec2? passo = PasseioDeHabitante(npc);
				if (passo == null) { npc.Moving = false; return; }
				destino = passo.Value;
			}
			else destino = presa.Pos;
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
			// `soJogadores: false` -- e o unico lugar do port em que "seja quem for" e a regra, e ela e
			// literal: *"o corpo ataca TUDO que ve (player OU NPC)"* (`lssjbuff.dm:563`). O corpo aqui
			// e de um JOGADOR possuido, e nao um NPC: o crivo do `NPCAI.dm:439` nao vale pra ele.
			presa = PresaDaFera(npc, soJogadores: false);
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

		// A LINHA DE VISAO SO E TRACADA PRA QUEM TEM O QUE ATIRAR -- quem nao comprou nenhuma das tres
		// tecnicas que voam (ver `TecnicasDeLonge`) nao paga a varredura de segmento. Passar a
		// resposta do arsenal como argumento e o que mantem esse custo fora do caminho de 30 Hz de
		// todo mundo, em vez de perguntar la dentro.
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
				   + $"andares {p.MeuAndar}/{p.AndarDoAlvo}"
				   // AS EMOCOES VAO JUNTO porque sao o que explica duas decisoes DIFERENTES com os
				   // mesmos numeros de vida e distancia. Sem elas no despejo, calibrar o
				   // `behavior_check` seria adivinhar -- e o proprio cabecalho do `--diagia` diz que a
				   // calibragem come mais tempo que o codigo.
				   + $" | furia {cerebro.FuriaExpressa:0.00} cautela {cerebro.CautelaExpressa:0.00}");
	}

	// =====================================================================
	// O CORPO DA MENTE SE REERGUE -- o `clone_watch()` (`MindMeditate.dm:288-306`)
	// =====================================================================
	/// <summary>
	/// QUANTO O CORPO ERGUIDO PELA MENTE FICA NO CHAO. **LITERAL**: `#define MIND_CLONE_REVIVE 150`
	/// (`MindMeditate.dm:19`), e o comentario do autor ao lado ja da a conversao -- *"ticks ate o clone
	/// derrotado se reerguer (15s)"*. O `sleep(150)` do BYOND e 150/10 s.
	///
	/// ============================ ELE **SUBSTITUI** O NOCAUTE COMUM, e nao se soma a ele ============================
	/// `MeleeResolver.SegundosDeNocaute` e 12, e e ele que estaria valendo: o reflexo levantaria
	/// sozinho tres segundos antes -- com os nucleos no piso do `Levantar()` (1,5x o limiar de quebra),
	/// ou seja de pe e quase caindo de novo. O que este numero faz e REESCREVER aquele relogio no
	/// instante da queda, pra a contagem ser uma so.
	/// ==========================================================================================================
	/// </summary>
	private const double SegundosAteOReflexoSeReerguer = 15;

	/// <summary>Quanto ele recupera ao se reerguer. **LITERAL**: o `SpreadHeal(150, 1, 1)` do DM.</summary>
	private const double CuraDoReerguer = 150;

	/// <summary>
	/// O CORPO DA MENTE CAIU. Chamado a 30 Hz enquanto ele estiver no chao; o trabalho e todo na
	/// PRIMEIRA volta -- ver <see cref="ServerPlayer.CaiuNaMente"/>.
	///
	/// *"Seu reflexo cai... mas a mente nunca descansa."* -- a frase e a do original, e o anuncio vai
	/// pra TODO mundo do bolso (o `for(var/mob/P in members)` do `clone_watch`) e nao so pro dono: com
	/// um visitante na mente, contar so pra um seria a "linha nova em dois dos tres lugares" de sempre.
	/// </summary>
	private void DerrubadoNaMente(ServerPlayer npc, ServerPlayer dono)
	{
		if (npc.CaiuNaMente) return;   // ja anunciado: quem conta o prazo daqui pra frente e o nocaute
		npc.CaiuNaMente = true;

		// O RELOGIO E O DO NOCAUTE, reescrito -- ver `SegundosAteOReflexoSeReerguer`. Nao ha um segundo
		// cronometro neste sistema, e e por isso que nao ha como as duas contagens discordarem.
		npc.Combate.NocauteRestante = SegundosAteOReflexoSeReerguer;

		AvisarNaMente(npc.Zone, npc.Papel is { EhChefe: true }
			? $"{npc.Name} cai... mas a mente nunca descansa."
			: "o seu reflexo cai... mas a mente nunca descansa.");
		GD.Print($"[server] o corpo da mente de {dono.Name} caiu e se reergue em {SegundosAteOReflexoSeReerguer:0}s");
	}

	/// <summary>
	/// ELE SE LEVANTA DE NOVO -- a segunda metade do `clone_watch`: `Un_KO()`, `SpreadHeal(150,1,1)` e
	/// `Ki = MaxKi` (`MindMeditate.dm:300-306`).
	///
	/// ============================ O `Un_KO` JA ACONTECEU, E DE PROPOSITO ============================
	/// Quem tira o corpo do chao e o `CombatState.Tick`, quando o `NocauteRestante` que a queda
	/// reescreveu chega a zero -- o mesmo caminho de todo mundo. Esta funcao roda no tique em que ele
	/// JA ESTA de pe, e o que ela faz e a CURA, que o `Levantar()` nao da (ele so poe os nucleos um
	/// pouco acima do limiar, o suficiente pra nao cair no proximo tique).
	///
	/// Sem a cura, o reflexo voltaria por 15 s e cairia de novo no primeiro soco -- o parceiro de treino
	/// infinito que se degrada ate virar um saco de pancada. A cura E o desenho: *"a mente nunca
	/// descansa"* quer dizer que ele volta INTEIRO.
	/// =========================================================================================
	///
	/// ============================ NAO HA "REENGAJAR" A ESCREVER ============================
	/// O DM precisa do `spawn(5) CC.foundTarget(T2)` porque la o alvo e um campo que a morte limpou.
	/// Aqui o alvo do corpo erguido pela mente e DERIVADO todo tique (`presa = dono`, tres linhas
	/// abaixo da chamada desta funcao): de pe, ele ja esta vindo. O `members[1]` do original -- que
	/// reengaja o PRIMEIRO membro e nao necessariamente o dono -- some junto, e pra melhor: o reflexo
	/// e de quem sonha.
	/// ==================================================================================
	/// </summary>
	private void ReerguerOOponente(ServerPlayer npc, ServerPlayer dono)
	{
		npc.CaiuNaMente = false;

		// ============================ O REFLEXO SE REANCORA NO DONO DE **AGORA** ============================
		// *"se o dono ficou mais forte treinando, o clone acompanha"* -- o pedido do dono, e ele e uma
		// SAIDA DO ORIGINAL que precisa estar escrita: no DM o reflexo e congelado no `mind_seed_bp` da
		// entrada e o `NPCTicker()` dele reescreve aquele mesmo numero pra sempre (`:322-327`). La isso
		// nao incomoda porque a mente do DM e uma luta; aqui ela e um treino INFINITO, e um oponente
		// congelado vira saco de pancada exatamente na sessao longa que a Camada 2 existe pra permitir.
		//
		// **SO O REFLEXO.** A lembranca de chefe tem ficha PROPRIA -- degraus, gatilho por membro, BP
		// pinado pelo `TickDoRoteiro` a partir do `Papel.BpAtual` -- e reescreve-la com a sua seria
		// apagar o Freeza e por voce no lugar dele. A pergunta e a mesma que ja escolhe a frase duas
		// linhas abaixo: e um corpo com ficha de chefe ou nao.
		//
		// **DONO CAIDO NAO SERVE DE ANCORA**: nocauteado, `expressedBP` e 10% do base (o corpo exposto),
		// e reancorar ali daria um reflexo dez vezes mais fraco. Nao ha caso pratico -- o dono caido e
		// EXPULSO da mente no mesmo tique (`BordasDeQuemEstaFora`) -- mas a ordem entre os dois lacos
		// nao e uma coisa em que apostar o poder do oponente.
		// ================================================================================================
		if (npc.Papel is not { EhChefe: true } && !dono.Ficha.KO && !dono.Ficha.dead)
		{
			EspelharODono(npc, dono.Ficha);   // reescreve a ficha E o pino (`BpDaMente`)
			npc.SigAtributos = "";            // o pacote de atributos so sai de novo se a assinatura mudar
		}

		// `SpreadHeal(150, 1, 1)` -- o 150 e do DM, e ele e uma CURA CHEIA e nao um numero de ajuste:
		// o corpo deste jogo tem 100 por membro, e o `Body.Curar` (a porta unica da cura) ja tem teto
		// por membro. Membro DECEPADO nao volta -- nem la nem aqui --, e com o golpe letal desligado
		// nao ha como decepar nada dentro da mente.
		npc.Combate.Corpo.Curar(CuraDoReerguer);
		npc.Combate.SincronizarVida();
		npc.Ficha.Ki = npc.Ficha.MaxKi;

		AvisarNaMente(npc.Zone, npc.Papel is { EhChefe: true }
			? $"{npc.Name} se levanta de novo!"
			: "o seu reflexo se levanta de novo!");
		GD.Print($"[server] o corpo da mente de {dono.Name} se reergueu inteiro");
	}

	/// <summary>
	/// FALA COM TODO MUNDO QUE ESTA NAQUELE BOLSO -- o `for(var/mob/P in members)` do original, que
	/// aqui e a lista da zona (a mente NAO tem lista de membros: ver o cabecalho de
	/// `GameServer.Mente.cs`).
	/// </summary>
	private void AvisarNaMente(ZoneKey mente, string frase)
	{
		foreach (ServerPlayer o in ZoneList(mente.Hash))
			if (o.Peer != null) Avisar(o, frase);
	}

	/// <summary>
	/// ATRAS DE QUEM ESTE NPC VAI -- e a resposta depende do que ele E, nao de onde ele esta.
	///
	/// ============================ PACIFICO ATE APANHAR ============================
	/// O cidadao nao procura ninguem: ele so conhece **quem bateu nele**, e so enquanto o rancor
	/// durar. E o `provoke()` do original (PlanetPopulation.dm:106-120), disparado pelo
	/// `refresh_combat_tag()` -- que os procs de melee e de rajada chamam na vitima ANTES de
	/// resolver o golpe, *"so this fires no matter the outcome"*: um NPC muito mais forte que
	/// esquiva de tudo ainda assim se vira e luta.
	///
	/// O prazo e o `combat_tag_duration` (90 s, UpdateFightingList.dm:8). Ele e o que transforma
	/// "apanhei" -- que e um EVENTO -- num estado que dura: sem prazo, o cidadao revidaria no tique
	/// do golpe e esqueceria no seguinte.
	/// ==========================================================================
	///
	/// ============================ E ISTO APAGA O TERMO QUADRATICO DO CIDADAO ============================
	/// `PresaDaFera` varre a lista da zona inteira, por corpo, todo tique -- O(n^2) por zona (medido
	/// no mapeamento: 1000 corpos numa zona = 1,0 ms/tique so nisso). O cidadao **nao varre nada**:
	/// um `TryGetValue` por id. Nao foi otimizacao, foi consequencia de portar a regra certa -- e e
	/// por isso que o teto de lotacao desta camada e o snapshot e nao a IA.
	///
	/// A varredura larga so acontece quando o rancor esta vivo e o agressor sumiu (mudou de zona,
	/// morreu, deslogou): e o `for(var/mob/P in oview(12,src))` do `provoke()`, que existe pelo mesmo
	/// motivo la (*"ki kills / stale lastDamager -> widen the search"*).
	/// ==============================================================================================
	/// </summary>
	/// <summary>
	/// ============================ O ALVO IMPOSTO PELO ROTEIRO ============================
	/// E o `bev_prey` do original (BossEvents.dm:264), e o comentario de la e a regra inteira:
	/// *"absorcao e prioridade ABSOLUTA: larga a luta atual"*. O Cell atras de um androide
	/// NOCAUTEADO e o caso que existe hoje -- e ele **so** existe porque este ramo nao filtra por
	/// KO: a caca normal pula quem esta no chao (e tem que pular, senao a fera fica parada em cima
	/// de um corpo caido ate o prazo vencer), entao sem este canal o Cell nunca chegaria perto.
	///
	/// Morto ou fora da zona, o alvo imposto cai sozinho -- nao ha campo pra limpar aqui, e quem
	/// escreve o id (`AbsorverCompanheiroCaido`) reescreve zero na mesma volta em que a presa some.
	///
	/// **SEPARADO DO `PresaDoNpc` PORQUE ELE E A UNICA PRESA QUE NAO PRECISA DE JOGADOR**, e por
	/// isso e ele -- e so ele -- que o congelamento tem que perguntar antes de mandar o corpo dormir.
	/// Ver <see cref="MenteDormindo"/>. Continua sendo a PRIMEIRA pergunta do funil de presa: quem
	/// chama `PresaDoNpc` recebe a mesma resposta de antes, na mesma ordem.
	/// ================================================================================
	/// </summary>
	private ServerPlayer? PresaImpostaPeloRoteiro(ServerPlayer npc) =>
		npc.Papel is { PresaDoRoteiro: not 0 }
		&& _players.TryGetValue(npc.Papel.PresaDoRoteiro, out ServerPlayer? imposta)
		&& !imposta.Ficha.dead && imposta.Zone.Hash == npc.Zone.Hash
			? imposta : null;

	private ServerPlayer? PresaDoNpc(ServerPlayer npc)
	{
		// O ALVO IMPOSTO PELO ROTEIRO VEM ANTES DE TUDO -- ver `PresaImpostaPeloRoteiro`.
		if (PresaImpostaPeloRoteiro(npc) is { } imposta) return imposta;

		// HOSTIL (chefe de saga; e o inimigo comum no dia em que voltar): **so em jogador** -- o
		// `AI nao ataca AI` do `NPCAI.dm:439` -- e agora DENTRO DE UM RAIO. Ver `PresaDoHostil`.
		if (npc.Papel is not { Pacifico: true }) return PresaDoHostil(npc);

		// O RANCOR VENCEU: ele volta a ser um habitante. Nao ha campo pra limpar -- o prazo E o
		// estado, e um `AlvoDoNpc = 0` a mais seria uma segunda verdade sobre a mesma coisa.
		if (NowMs() >= npc.RancorAte) return null;

		// O AGRESSOR TEM QUE SER GENTE, nos DOIS ramos -- e literal: o `provoke()` do DM exige
		// `atk.client` tanto no `lastDamager` quanto na busca alargada (`PlanetPopulation.dm:113-118`).
		// Sem isto, um chefe de saga que encostasse num cidadao punha o vilarejo pra revidar NELE, e
		// esse revide marcava a saga como "engajada" (ver `MonitorarChefe`) -- o ultimato de destruicao
		// do planeta encurtando por uma briga entre dois robos.
		if (_players.TryGetValue(npc.UltimoAgressor, out ServerPlayer? quem)
			&& quem.Id != npc.Id && EhJogador(quem) && quem.Zone.Hash == npc.Zone.Hash
			&& !quem.Ficha.dead && !quem.Ficha.KO)
			return quem;

		// O AGRESSOR SUMIU e o rancor ainda vale: procura outro por perto. O raio e o `oview(12)` do
		// `provoke()` -- 12 tiles --, e o corte por raio e o que impede isto de virar a varredura da
		// fera com outro nome: quem esta do outro lado do mapa nao entra na conta.
		ServerPlayer? melhor = null;
		float perto = RaioDeProvocacao * RaioDeProvocacao;
		foreach (ServerPlayer o in ZoneList(npc.Zone.Hash))
		{
			// SO GENTE DE VERDADE. O `provoke()` do DM exige `P.client` em toda a busca alargada, e a
			// razao aparece no jogo: sem isso, dois cidadaos que se esbarrassem numa briga passariam
			// a se cacar e o vilarejo inteiro viraria uma rinha depois do primeiro soco de um jogador.
			if (!EhJogador(o) || o.Ficha.dead || o.Ficha.KO) continue;
			float d2 = (o.Pos - npc.Pos).LengthSquared;
			if (d2 >= perto) continue;
			perto = d2; melhor = o;
		}
		return melhor;
	}

	/// <summary>
	/// O raio da busca alargada do <see cref="PresaDoNpc"/>. **LITERAL**: o `oview(12,src)` do
	/// `provoke()` (PlanetPopulation.dm:114), que o proprio autor alargou de 6 pra 12.
	/// </summary>
	private const float RaioDeProvocacao = 12 * Jandirus.Core.World.ZoneCollision.TileSize;

	/// <summary>
	/// A DISTANCIA EM QUE UM HOSTIL NOTA ALGUEM. **LITERAL**: `#define MAX_AGGRO_RANGE 20`
	/// (`1A Defines.dm:67`), o raio do `viewers()` que o passo do JOGADOR varre pra acordar NPC
	/// (`NPCAI.dm:48`) -- no DM quem aciona a agressao e quem chega perto, e nao o monstro.
	/// </summary>
	private const float RaioDeAgressao = 20 * Jandirus.Core.World.ZoneCollision.TileSize;

	/// <summary>
	/// ATE ONDE ELE PERSEGUE DEPOIS DE ENGAJAR. **LITERAL**: o `aggro_dist*2` do leash
	/// (`NPCAI.dm:442`) e do reengajamento do `lostTarget` (`:161`), com `aggro_dist = 30` (`:52`).
	/// </summary>
	private const float RaioDePerseguicao = 60 * Jandirus.Core.World.ZoneCollision.TileSize;

	/// <summary>
	/// ATRAS DE QUEM UM NPC HOSTIL VAI -- **dentro de um raio**, que e o que faltava.
	///
	/// ============================ O CHEFE CACAVA A ZONA INTEIRA, E NENHUM NPC DO DM FAZ ISSO ============================
	/// Ate aqui, todo corpo hostil pedia o `PresaDaFera`: *o corpo em pe mais proximo da zona*, sem
	/// distancia nenhuma. Numa zona de 1000 tiles isso e um chefe que sabe onde voce esta desde o
	/// instante em que voce pousa no planeta, e que atravessa o mapa atras de voce pra sempre. No DM
	/// o NPC nao procura: e o PASSO DO JOGADOR que o acorda, num raio de 20 tiles
	/// (`mob/OnStep -> viewers(MAX_AGGRO_RANGE)`, `NPCAI.dm:48`), e o `lostTarget` so re-procura
	/// dentro de `aggro_dist`.
	///
	/// ============================ E O RAIO DE LARGAR E MAIOR QUE O DE ADOTAR ============================
	/// Os dois numeros sao do original e a distancia entre eles E o comportamento: `aggro_dist*2`
	/// (60 tiles) pra largar, 20 pra adotar. O comentario do autor no leash diz por que a folga tem
	/// que ser grande -- *"a ZanzoClash/throw that displaces the NPC but keeps the target adjacent
	/// must NOT trip this"* --, e a distancia entre os dois numeros e o que impede o jogador de se
	/// soltar do chefe dando tres passos pra tras.
	///
	/// Sem os dois raios nao ha histerese, e sem histerese a briga PISCA: com um raio so, o corpo no
	/// limite entra e sai de "tem alvo" a cada passo, e o cerebro troca de plano junto.
	/// ==============================================================================================================
	///
	/// O CUSTO E O MESMO de antes: uma varredura da lista da zona, e ela agora tem uma SAIDA CURTA
	/// -- quem ja tem presa engajada e valida responde num `TryGetValue`, sem varrer nada. Numa saga
	/// em andamento (o caso comum) isso troca a varredura por uma consulta.
	/// </summary>
	private ServerPlayer? PresaDoHostil(ServerPlayer npc)
	{
		// --- 1. A PRESA JA ENGAJADA, enquanto ela nao escapar do leash ------
		// As recusas sao as MESMAS da adocao (gente de verdade, viva, de pe, na mesma zona) -- e a
		// unica coisa que muda entre as duas pontas e o raio. Ver o `lostTarget` (`NPCAI.dm:161`).
		if (npc.PresaEngajada != 0
			&& _players.TryGetValue(npc.PresaEngajada, out ServerPlayer? engajada)
			&& engajada.Id != npc.Id && EhJogador(engajada)
			&& !engajada.Ficha.dead && !engajada.Ficha.KO
			&& engajada.Zone.Hash == npc.Zone.Hash
			&& (engajada.Pos - npc.Pos).LengthSquared <= RaioDePerseguicao * RaioDePerseguicao)
			return engajada;

		// --- 2. NOTAR ALGUEM: o corpo em pe mais proximo DENTRO do raio -----
		ServerPlayer? melhor = null;
		float perto = RaioDeAgressao * RaioDeAgressao;
		foreach (ServerPlayer o in ZoneList(npc.Zone.Hash))
		{
			if (o.Id == npc.Id || !EhJogador(o) || o.Ficha.dead || o.Ficha.KO) continue;
			float d2 = (o.Pos - npc.Pos).LengthSquared;
			if (d2 >= perto) continue;
			perto = d2; melhor = o;
		}

		// NAO HA CAMPO PRA LIMPAR NO CAMINHO DE VOLTA: escrever zero aqui e o proprio "esqueci".
		// Quem sumiu, morreu, caiu ou escapou do leash cai no ramo de cima e e reescrito na mesma
		// volta -- o campo nunca sobrevive a um alvo invalido por mais de um tique.
		npc.PresaEngajada = melhor?.Id ?? 0;
		return melhor;
	}

	/// <summary>
	/// QUEM A FERA VAI CACAR: o corpo em pe mais proximo da zona.
	///
	/// Sem lista de inimigos e sem alvo marcado de proposito -- marcar alvo e uma decisao, e a
	/// fera nao decide nada. Caidos saem porque o `Cerebro` ja para de bater em quem esta no chao
	/// e um alvo caido so faria o macaco parar de pe em cima de um corpo ate o prazo vencer.
	///
	/// ============================ "SEJA QUEM FOR" E VERDADE PRA UM DOS DOIS CHAMADORES ============================
	/// Esta funcao serve a duas coisas que o DM trata de maneira OPOSTA, e a diferenca nunca tinha
	/// sido escrita -- os dois usavam o mesmo laco sem crivo nenhum:
	///
	///   * **O CORPO POSSUIDO** (Oozaru selvagem, furia lendaria): qualquer corpo mesmo. E literal --
	///     *"o corpo ataca TUDO que ve (player OU NPC)"* (`lssjbuff.dm:563`). `soJogadores: false`.
	///   * **O NPC HOSTIL** (todo chefe de saga passa por aqui, via `PresaDoNpc`): **so jogador**. E
	///     literal tambem, e o comentario do autor e a regra inteira -- *"AI nao ataca AI -- EXCETO
	///     torneio e PRESA de evento (Cell vs androides)"* (`NPCAI.dm:439`); a busca de alvo do NPC
	///     corre a `player_list` com `M.client` (`:763-764`). `soJogadores: true`.
	///
	/// **A EXCECAO DO CELL NAO PRECISA SER ESCRITA AQUI**: o alvo imposto pelo roteiro (`bev_prey`) e
	/// respondido ANTES desta funcao, no topo do `PresaDoNpc` -- e por isso ele continua alcancando o
	/// androide nocauteado.
	///
	/// O QUE ISSO CUSTAVA, medido no `npcs.json`: os dois androides do elo do Cell nascem no MESMO
	/// tique (`diasMin/Max = 0`) na Earth, que tem 40 cidadaos planejados. Sem crivo eles cacavam um
	/// ao outro e a populacao -- 17 nocauteava 18, o Cell absorvia o caido (`AbsorverCompanheiroCaido`
	/// so exige `KO` + mesma zona), chegava a forma Perfeita, e o elo se VENCIA sozinho com zero input
	/// de jogador. E o congelamento anti-lag nunca engatava, porque com 40 cidadaos de pe esta funcao
	/// nunca devolvia nulo pra um chefe.
	/// ======================================================================================================
	/// </summary>
	/// <param name="soJogadores">
	/// Ver acima. `true` no NPC hostil (a regra do DM), `false` no corpo possuido (a fera e do dono e
	/// bate em tudo). Sem valor padrao de proposito: quem chamar tem que RESPONDER a pergunta.
	/// </param>
	private ServerPlayer? PresaDaFera(ServerPlayer fera, bool soJogadores)
	{
		ServerPlayer? melhor = null;
		float perto = float.MaxValue;

		foreach (ServerPlayer o in ZoneList(fera.Zone.Hash))
		{
			if (o.Id == fera.Id || o.Ficha.dead || o.Ficha.KO) continue;
			if (soJogadores && !EhJogador(o)) continue;
			float d2 = (o.Pos - fera.Pos).LengthSquared;
			if (d2 >= perto) continue;
			perto = d2; melhor = o;
		}
		return melhor;
	}

	/// <summary>
	/// O PASSEIO DO HABITANTE -- o `idle_wander_loop` (PlanetPopulation.dm:122-131). Nulo = parado.
	///
	/// ============================ OS TRES NUMEROS SAO DO ORIGINAL ============================
	///   `sleep(rand(45,95))`  -> uma decisao a cada 4,5-9,5 s (media 7,0)
	///   `if(prob(35))`        -> so 35% das decisoes viram passo
	///   `step_rand(src)`      -> **UM tile**, e nao "andar naquela direcao"
	///
	/// Da mais ou menos um tile a cada vinte segundos. E pouco de proposito: o `step_rand` do BYOND
	/// e um passo de grade, nao um rumo -- e foi confundir as duas coisas que fez a bancada medir 75
	/// tiles em 20 segundos na primeira versao.
	/// ====================================================================================
	///
	/// ============================ FUNCAO PURA, SEM CAMPO NENHUM ============================
	/// De (id, balde de tempo) saem as tres respostas: se este balde e de passo, pra que lado, e se
	/// o instante atual ainda esta dentro da janela de andar. Mesmo desenho do <see cref="RumoDaFera"/>
	/// e pelo mesmo motivo: nao ha "destino atual" pra guardar, sanear e esquecer de limpar -- e 150
	/// habitantes nao geram 150 relogios pra o servidor manter.
	/// ==================================================================================
	/// </summary>
	private Vec2? PasseioDeHabitante(ServerPlayer npc)
	{
		// A media do `rand(45,95)` do DM: 7,0 s. Um periodo fixo em vez de um sorteio por corpo porque
		// a fase ja e diferente por corpo (ela sai do id, logo abaixo) -- e a variacao que importa pra
		// os habitantes nao se moverem todos juntos e a de FASE, nao a de periodo.
		const double SegundosPorDecisao = 7.0;

		// Quanto tempo ele fica ANDANDO quando decide andar. E o `step_rand`, que e UM tile e acabou.
		const double SegundosAndando = 0.4;

		// O RELOGIO E O DO MUNDO (dt acumulado), e nao o de parede -- ver `_relogioDoMundo`.
		double agora = _relogioDoMundo;
		ulong balde = (ulong)(agora / SegundosPorDecisao);

		// splitmix64 de (id, balde). `_rng` nao serve: ele daria uma resposta nova a cada tique e o
		// habitante tremeria no lugar em vez de dar um passo.
		ulong h = (ulong)npc.Id * 0x9E3779B97F4A7C15UL ^ balde * 0xBF58476D1CE4E5B9UL;
		h ^= h >> 30; h *= 0xBF58476D1CE4E5B9UL;
		h ^= h >> 27; h *= 0x94D049BB133111EBUL;
		h ^= h >> 31;

		if (h % 100 >= 35) return null;   // `prob(35)`: este balde e de descanso

		// A FASE E POR CORPO: sem ela os 40 habitantes da Terra dariam o passo no MESMO instante, e o
		// que se veria seria a cidade inteira andando em bloco de sete em sete segundos.
		double fase = (h >> 40) % 1000 / 1000.0 * (SegundosPorDecisao - SegundosAndando);
		double dentro = agora - balde * SegundosPorDecisao;
		if (dentro < fase || dentro > fase + SegundosAndando) return null;

		double ang = (h >> 8) % 3600 / 3600.0 * Math.Tau;
		return npc.Pos + new Vec2((float)Math.Cos(ang), (float)Math.Sin(ang)) * ZoneCollision.TileSize * 2;
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
