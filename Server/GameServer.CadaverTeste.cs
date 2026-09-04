using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ A BANCADA DO CADAVER (`--cadaverteste`) ============================
/// *"ao morrer o corpo deve FICAR NO CHAO ate alguem ENTERRAR ele (...) o corpo mesmo morto TEM TODAS
/// AS INTERACOES DE UM CORPO VIVO"* -- o pedido, medido em execucao.
///
/// ============================ O QUE ELA MEDE, E O QUE ELA SE RECUSA A MEDIR ============================
/// **Ela nao mede tabela.** A licao que o arquivo `dbclimax-port-bancada-mede-intencao` deste projeto
/// registra e que uma bancada que confere a INTENCAO fecha verde por cima do defeito -- quatro bugs
/// visuais passaram por quatro mil checagens assim. Entao aqui:
///
///   * nao se pergunta se `Interacoes.DoCadaver()` tem uma linha (isso e a tabela);
///   * nao se chama `DeixarOCadaver` pra provar que ele funciona -- **mata-se o host de verdade** e
///     deixa-se o TIQUE do servidor fazer a triagem, a viagem e o cadaver, como em jogo;
///   * nao se afirma "o cadaver e agarravel" olhando o `CorpoNaFrente`. Prende-se, **roda-se o tique**,
///     e pergunta-se se ele AINDA esta preso -- porque o defeito que este trabalho consertou vivia
///     exatamente ai: o `Prender` funcionava e o tique seguinte desfazia tudo, calado.
///
/// ============================ AS OITO FAMILIAS ============================
///   1. **A VIAGEM NAO QUEBROU** -- o host morre, o prazo vence, ele CHEGA no Outro Mundo com auréola,
///      e o cadaver fica no lugar exato, **sem** auréola. As duas coisas no mesmo teste, porque a
///      pergunta da tarefa era se elas brigavam.
///   2. **NAO VAZA NADA** -- o cadaver nao carrega BP nem vida do morto, e nao esta no `_players`.
///   3. **AGARRAR** -- e o aperto SOBREVIVE ao tique. E a familia que reprova o defeito antigo.
///   4. **CARREGAR** -- altitude e nado herdados de quem carrega, sem campo novo.
///   5. **ARREMESSAR** -- o corpo jogado ANDA. Reprova se o tique do arremesso voltar ao `_players`.
///   6. **ENTERRAR** -- nasce lapide com epitafio, o corpo some, e a lapide fica no mundo.
///   7. **O TETO DA ZONA** -- ele dispara de verdade e leva o MAIS ANTIGO.
///   8. **O BONECO TAMBEM** -- o conserto do agarrao vale pro corpo de quem esta meditando, que era o
///      caso que ja estava quebrado antes do cadaver existir.
/// ==========================================================
///
///     Godot --headless --path . --host --rede 7977 --cadaverteste --raca Saiyan
///                      --conta bancada_cadaver --nome Defunto
///
/// TUDO O QUE ELA TOCA DO HOST E DEVOLVIDO no `finally` -- zona, posicao, morte, relogio, auréola,
/// altitude e agarrao --, e todo cadaver forjado sai do mundo. As lapides que ela ergueu tambem: uma
/// bancada que deixasse tumulos no `mundo.json` sujaria o disco do dono a cada rodada.
/// </summary>
public partial class GameServer
{
	private bool _cadaverDeTeste;
	private int _cadOk, _cadFalhou;

	private void AfirmarCad(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _cadOk++; GD.Print($"[cadaver]   OK    {oque}"); return; }
		_cadFalhou++;
		GD.PrintErr($"[cadaver]   FALHA {oque}   {detalhe}");
	}

	private void RodarBancadaDoCadaver(ServerPlayer pl)
	{
		_cadOk = _cadFalhou = 0;
		GD.Print("[cadaver] ================ O CORPO QUE FICA ================");

		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		bool mortoGuardado = pl.Ficha.dead, koGuardado = pl.Ficha.KO;
		long relogioGuardado = pl.RelogioDaMorte;
		bool viajouGuardado = pl.MorteJaViajou, aureolaGuardada = pl.EnvAureola;
		float alturaGuardada = pl.Altitude;
		bool voandoGuardado = pl.Voando, nadandoGuardado = pl.Nadando;
		int obrasAntes = _noChao.Count;

		var forjados = new List<ServerPlayer>();

		try
		{
			AfirmarCad("PRECONDICAO: o host tem dono na tela (sem `Peer` a triagem nunca chega a viagem)",
					   EhJogador(pl));

			ServerPlayer? cadaver = AViagemDeixaOCorpo(pl);
			if (cadaver == null)
			{
				AfirmarCad("sem cadaver nao ha o que medir -- o resto da bancada fica de fora", false);
				return;
			}
			forjados.Add(cadaver);

			OCadaverNaoCarregaNumeroDeNinguem(pl, cadaver);
			OCadaverEAgarravelEContinuaPreso(pl, cadaver);
			OCadaverVaiNoColoComAAltitude(pl, cadaver);
			OCadaverArremessadoAnda(cadaver);
			EnterrarErgueALapide(pl, cadaver, forjados);
			OTetoDaZonaLevaOMaisAntigo(pl, forjados);
			OBonecoTambemFoiConsertado(pl, forjados);

			AfirmarCad("a bancada chegou ao fim (sem esta linha, abortar no meio reportaria '0 falhas')",
					   true);
		}
		catch (Exception e)
		{
			AfirmarCad($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			// OS CORPOS FORJADOS SAEM -- inclusive os que ja tinham sido desfeitos (o `Remove` de lista
			// e de dicionario nao reclama de quem nao esta la).
			foreach (ServerPlayer f in forjados)
			{
				if (f.AgarradoPorId != 0) LimparPreso(f);
				_npcsPraTirar.Remove(f);
				_players.Remove(f.Id);
				ZoneList(f.Zone.Hash).Remove(f);
				f.Peer = null;
			}

			// AS LAPIDES QUE ELA ERGUEU TAMBEM. Elas vao pro `mundo.json` de verdade (e esse e o ponto
			// da familia 6) -- e uma bancada que as deixasse la encheria o disco do dono de tumulos de
			// teste, um por rodada.
			if (_noChao.Count > obrasAntes)
			{
				_noChao.RemoveRange(obrasAntes, _noChao.Count - obrasAntes);
				GravarMundo();
				MandarObras(zonaGuardada);
			}

			// O HOST VOLTA VIVO E NO LUGAR -- mesma disciplina do `--alemteste`.
			if (pl.AgarrandoId != 0) Soltar(pl, MotivoDaSoltura.Tecla);
			pl.Combate.Reviver();
			pl.Ficha.dead = mortoGuardado;
			pl.Ficha.KO = koGuardado;
			pl.RelogioDaMorte = relogioGuardado;
			pl.MorteJaViajou = viajouGuardado;
			pl.EnvAureola = aureolaGuardada;
			pl.Altitude = alturaGuardada;
			pl.Voando = voandoGuardado;
			pl.Nadando = nadandoGuardado;
			if (!pl.Zone.Equals(zonaGuardada)) MoveToZone(pl.Id, zonaGuardada, posGuardada);
			else pl.Pos = posGuardada;
			pl.Combate.SincronizarVida();
			MandarAureola(pl);
		}

		GD.Print($"[cadaver] ================ {_cadOk} passaram, {_cadFalhou} falharam ================");
	}

	// =====================================================================
	// 1) A VIAGEM NAO QUEBROU -- e o corpo ficou
	// =====================================================================
	/// <summary>
	/// ============================ A PERGUNTA DA TAREFA, MEDIDA ============================
	/// *"NAO quebre a viagem pro Outro Mundo (...) se as duas coisas brigarem, pare e explique"*. Elas
	/// nao brigam, e esta familia e a prova: o MESMO caminho de producao produz as duas coisas, porque
	/// e o que o `Death()` do DM faz numa funcao so -- `GenerateCorpse()` no passo 5, `loc = locate(...)`
	/// no passo 11.
	///
	/// **MATA O HOST PELO FUNIL DE PRODUCAO** (`CombatState.Morrer`, o mesmo do soco e do Kamehameha) e
	/// **vence o prazo empurrando o relogio pro passado**, deixando a triagem do servidor decidir. Uma
	/// bancada que chamasse `IrProAlem` na mao provaria que a funcao funciona e nao provaria que alguem
	/// a chama -- que e a regra que o `--velorio` ja escreveu.
	///
	/// **E A TRIAGEM E CHAMADA DE DENTRO DA VARREDURA DE `_players.Values`**, que e de onde o
	/// `TickCombate` a chama. Essa linha nao e enfeite: enquanto ela nao existia, esta bancada fechava
	/// com 54 verdes com o tique do servidor CAINDO a cada morte de jogador. Ver o comentario no lugar.
	/// ====================================================================================
	/// </summary>
	private ServerPlayer? AViagemDeixaOCorpo(ServerPlayer pl)
	{
		GD.Print("[cadaver] -- 1) a viagem levou a pessoa, e o corpo ficou --");

		ZoneKey ondeMorreu = pl.Zone;
		Vec2 ondeCaiu = pl.Pos;
		int corposAntes = ZoneList(ondeMorreu.Hash).Count;

		pl.Combate.Reviver();
		AfirmarCad("o host morre pelo funil de producao (`CombatState.Morrer`)",
				   pl.Combate.Morrer(ignorarSeguro: true) && pl.Ficha.dead);

		// AINDA NAO HA CADAVER: durante os 2 s do `Alem.MsNoChao` o corpo no chao E a pessoa. Sem esta
		// linha, "apareceu um cadaver" fecharia verde com DOIS corpos no lugar da morte.
		AfirmarCad($"...e nos {Alem.MsNoChao / 1000} s de `Alem.MsNoChao` NAO nasce um segundo corpo "
				 + "(o corpo no chao ainda e a PESSOA)",
				   ZoneList(ondeMorreu.Hash).Count == corposAntes,
				   $"{ZoneList(ondeMorreu.Hash).Count} x {corposAntes}");

		pl.RelogioDaMorte = NowMs() - 1;

		// ============================ E A TRIAGEM E CHAMADA **DE DENTRO DA VARREDURA** ============================
		// Aqui morava um `VenceuOPrazoDaMorte(pl);` seco, e ele deixou passar o pior defeito que este
		// caminho ja teve. A chamada na mao exercitava a triagem, a viagem e o cadaver -- tudo menos o
		// LUGAR de onde o servidor a chama. E o lugar era o defeito: o `TickCombate` roda essa mesma
		// linha de DENTRO de um `foreach (ServerPlayer pl in _players.Values)`, e o cadaver nascia com
		// `_players[id] = corpo`, o que invalida o enumerador (insercao de chave nova e a UNICA operacao
		// de `Dictionary` que invalida -- `Remove` nao). Toda morte de jogador matava o tique inteiro:
		//
		//     InvalidOperationException: Collection was modified; enumeration operation may not execute.
		//        at Dictionary`2.ValueCollection.Enumerator.MoveNext()
		//        at GameServer.TickCombate(Double dt) in Server\GameServer.Combat.cs:line 1203
		//
		// E esta bancada dava 54 verdes por cima disso. E o cego que o `dbclimax-port-bancada-nasce-no-
		// lugar-errado` ja registra: **chamar o passo direto nunca testa a ENTRADA nele.**
		//
		// Entao a chamada passou a sair de onde ela sai em jogo -- as duas linhas de dentro sao copia
		// literal do `TickCombate`. E a prova nao e "nao estourou": e a CONTAGEM. Um laco que morre no
		// meio some com os corpos que viriam depois do morto, e sem contar, um `catch` calado deixaria
		// verde de novo.
		// ======================================================================================================
		int varridos = 0, haviam = _players.Count;
		bool varreuInteiro = false;
		try
		{
			long agora = NowMs();
			foreach (ServerPlayer corpo in _players.Values)
			{
				varridos++;
				if (corpo.Ficha.dead && agora >= corpo.RelogioDaMorte) VenceuOPrazoDaMorte(corpo);
			}
			varreuInteiro = true;
		}
		catch (InvalidOperationException e)
		{
			AfirmarCad("a varredura de `_players.Values` nao pode ser invalidada pela morte", false,
					   e.Message);
		}

		AfirmarCad("A MORTE ACONTECEU DE DENTRO DO `foreach (... in _players.Values)`, e a varredura "
				 + "CHEGOU AO FIM (o cadaver nao entra no `_players`)",
				   varreuInteiro && varridos == haviam, $"{varridos} de {haviam} corpos varridos");

		AfirmarCad("A VIAGEM ACONTECEU: o host esta no Outro Mundo", Alem.EhOAlem(pl.Zone),
				   pl.Zone.Name);
		AfirmarCad("...ainda morto, e agora COM auréola (`TemAureola` = morto && ja viajou)",
				   pl.Ficha.dead && Alem.TemAureola(pl.Ficha.dead, pl.MorteJaViajou));
		AfirmarCad("...e o relogio da segunda etapa foi rearmado (o defeito que o `--alemteste` achou)",
				   pl.RelogioDaMorte > NowMs());

		ServerPlayer? c = null;
		foreach (ServerPlayer o in ZoneList(ondeMorreu.Hash)) if (o.ECadaver) c = o;

		AfirmarCad("E O CORPO FICOU: ha um cadaver na zona onde ele morreu", c != null);
		if (c == null) return null;

		AfirmarCad("...no ponto EXATO onde ele caiu",
				   Math.Abs(c.Pos.X - ondeCaiu.X) < 0.01f && Math.Abs(c.Pos.Y - ondeCaiu.Y) < 0.01f,
				   $"({c.Pos.X:0.0},{c.Pos.Y:0.0}) x ({ondeCaiu.X:0.0},{ondeCaiu.Y:0.0})");
		AfirmarCad("...com a APARENCIA dele (`A.icon = icon` + `A.overlays += overlays`, `Corpse.dm:79-82`)",
				   c.Visual.Corpo == pl.Visual.Corpo && c.Visual.Cabelo == pl.Visual.Cabelo
				   && c.Race == pl.Race);
		AfirmarCad("...e a lista de aparencia e uma COPIA, e nao a instancia do morto",
				   !ReferenceEquals(c.Visual, pl.Visual));

		// ============================ A AUREOLA E O BUG QUE O DONO FOTOGRAFOU ============================
		// *"o corpo q fica no MAPA DOS VIVOS deveria ser o EXATO CORPO DELE QUANDO MORRE, sem a aureola"*.
		// No DM isso sai por ORDEM (o cadaver e fotografado antes do `overlayList += 'Halo.dmi'`); aqui
		// sai por CONSTRUCAO -- a ficha do cadaver e nova, entao `MorteJaViajou` e falso nele.
		AfirmarCad("...e o cadaver NAO tem auréola, com a do host acesa ao mesmo tempo",
				   !Alem.TemAureola(c.Ficha.dead, c.MorteJaViajou)
				   && Alem.TemAureola(pl.Ficha.dead, pl.MorteJaViajou));

		AfirmarCad("...e ele esta DEITADO (a pose de cadaver sai dos mesmos campos de sempre)",
				   c.Deitado && c.Pose(NowMs(), false) == Protocol.Pose.Nocauteado);
		return c;
	}

	// =====================================================================
	// 2) NAO VAZA NADA -- o item 6 da tarefa
	// =====================================================================
	/// <summary>
	/// *"o cadaver nao pode carregar numero de vida ou BP -- o dono acabou de mandar tirar a vida alheia
	/// do fio"*.
	///
	/// A prova nao e um corte: e a CONSTRUCAO. A ficha do cadaver e um `Fighter` novo, e por isso ele
	/// nao sabe quem foi forte. Pra a linha significar alguma coisa, o host tem que estar com um BP
	/// diferente do padrao -- senao "igual ao do morto" e "zerado" dariam a mesma resposta.
	/// </summary>
	private void OCadaverNaoCarregaNumeroDeNinguem(ServerPlayer pl, ServerPlayer c)
	{
		GD.Print("[cadaver] -- 2) o corpo nao carrega numero de ninguem --");

		AfirmarCad("PRECONDICAO: o host tem BP acima do padrao (senao a linha abaixo nao separa nada)",
				   pl.Ficha.BP > 10, $"BP {pl.Ficha.BP:N0}");
		AfirmarCad("o cadaver NAO herdou o BP do morto",
				   c.Ficha.BP < pl.Ficha.BP, $"{c.Ficha.BP:N0} x {pl.Ficha.BP:N0}");
		AfirmarCad("...nem o BP expresso (o que o Sense leria)",
				   c.Ficha.expressedBP < pl.Ficha.expressedBP,
				   $"{c.Ficha.expressedBP:N0} x {pl.Ficha.expressedBP:N0}");
		AfirmarCad("...e a ficha dele nao e a mesma instancia", !ReferenceEquals(c.Ficha, pl.Ficha));
		AfirmarCad("...nem o corpo (bater no cadaver nao fere quem morreu)",
				   !ReferenceEquals(c.Combate, pl.Combate));

		// ELE E O TERCEIRO GRUPO DO `Gente` -- nem jogador, nem NPC do mundo. Sem isto ele contaria pra
		// lotacao do planeta, entraria na reposicao de habitante e a media do servidor o somaria.
		AfirmarCad("`Gente.EhJogador` diz NAO (nao ha dono na tela)", !EhJogador(c));
		AfirmarCad("`Gente.EhNpcDoMundo` diz NAO (nao saiu de molde nenhum)", !EhNpcDoMundo(c));

		AfirmarCad("...e ele NAO esta no `_players` (nao e um corpo simulado -- veja o `TickDoEstomago`)",
				   !_players.ContainsKey(c.Id));
		AfirmarCad("...mas ESTA na `ZoneList` (e onde mora o que existe num lugar)",
				   ZoneList(c.Zone.Hash).Contains(c));
	}

	// =====================================================================
	// 3) AGARRAR -- e o aperto sobrevive ao tique
	// =====================================================================
	/// <summary>
	/// ============================ A FAMILIA QUE REPROVA O DEFEITO ANTIGO ============================
	/// **A segunda linha desta funcao e a bancada inteira.** `Prender` sempre funcionou; o que estava
	/// quebrado era o TIQUE seguinte, que resolvia o preso por `_players.TryGetValue` -- e nem o cadaver
	/// nem o boneco do corpo largado estao no `_players`. O aperto era desfeito 33 ms depois, sem
	/// mensagem nenhuma: agarrar quem esta meditando "nao fazia nada".
	///
	/// Por isso aqui se roda o `TickDoAgarrao` de verdade e se pergunta DEPOIS. Uma bancada que
	/// conferisse so o `Prender` fecharia verde com o defeito de pe.
	/// ============================================================================================
	/// </summary>
	private void OCadaverEAgarravelEContinuaPreso(ServerPlayer pl, ServerPlayer c)
	{
		GD.Print("[cadaver] -- 3) o corpo e agarravel, e o aperto dura --");

		// O host precisa estar VIVO e ao lado do corpo pra agarrar -- ele acabou de viajar pro alem.
		pl.Combate.Reviver();
		pl.Ficha.dead = false;
		pl.MorteJaViajou = false;
		if (!pl.Zone.Equals(c.Zone)) MoveToZone(pl.Id, c.Zone, c.Pos);
		pl.Pos = c.Pos + new Vec2(8, 0);
		pl.Facing = Facing.West;
		pl.Altitude = 0;

		AfirmarCad("o `CorpoNaFrente` acha o CADAVER (ele nao pergunta `Ficha.dead`, e o soco pergunta)",
				   CorpoNaFrente(pl) == c);
		AfirmarCad("...e o `AlvoNaFrente` do SOCO nao acha (as duas regras sao diferentes de proposito)",
				   AlvoNaFrente(pl) != c);

		Prender(pl, c);
		AfirmarCad("PEGOU: os dois lados apontam um pro outro",
				   pl.AgarrandoId == c.Id && c.AgarradoPorId == pl.Id);

		// **A LINHA QUE IMPORTA.** Tres tiques cheios, que e mais do que o suficiente pra a passada 1 e
		// a varredura orfa terem rodado.
		for (int i = 0; i < 3; i++) TickDoAgarrao(Protocol.TickSeconds);

		AfirmarCad("...E CONTINUA PRESO DEPOIS DO TIQUE (o `_players.TryGetValue` soltava aqui, calado)",
				   pl.AgarrandoId == c.Id && c.AgarradoPorId == pl.Id,
				   $"agarrando={pl.AgarrandoId} agarradoPor={c.AgarradoPorId}");

		// E O `CorpoNaFrente` PASSA A RECUSA-LO -- a regra do DM (`!M.grabber`, `Grabbing.dm:170`).
		// Ela e a que transformaria um `AgarradoPorId` orfao num corpo impossivel de agarrar pra sempre,
		// que e o motivo de a varredura orfa ter passado a varrer todo corpo do mundo.
		AfirmarCad("...e agora ele esta fora da busca de quem quiser agarrar (`!M.grabber`)",
				   CorpoNaFrente(pl) != c);
	}

	// =====================================================================
	// 4) CARREGAR -- a altitude e o nado
	// =====================================================================
	/// <summary>
	/// *"pessoas podem agarrar o corpo e SAIR VOANDO com ele, e ele tb vai estar considerado como
	/// VOANDO"*.
	///
	/// **NAO HA UMA LINHA DE CODIGO SOBRE CADAVER NESTE CAMINHO** -- o `LevarNoColo` escreve `Altitude`
	/// e `Nadando` e nao pergunta nada sobre quem e o carregado. O que esta bancada mede e que o
	/// caminho ALCANCA o cadaver, e que o resto se deriva: o andar de voo e o bit que o cliente le pra
	/// desenhar a pose e a sombra.
	/// </summary>
	private void OCadaverVaiNoColoComAAltitude(ServerPlayer pl, ServerPlayer c)
	{
		GD.Print("[cadaver] -- 4) carregado, ele voa junto --");

		pl.ModoDoAgarrao = ModoDeAgarrao.Carregando;
		pl.Altitude = 5 * ZoneCollision.TileSize;
		pl.Nadando = false;
		pl.Moving = false;

		TickDoAgarrao(Protocol.TickSeconds);

		AfirmarCad("o corpo esta na MESMA altitude de quem o carrega",
				   Math.Abs(c.Altitude - pl.Altitude) < 0.01f, $"{c.Altitude} x {pl.Altitude}");
		AfirmarCad("...e no mesmo ponto", (c.Pos - pl.Pos).LengthSquared < 0.01f);
		AfirmarCad("...e por isso ele esta no MESMO ANDAR (o alcance de soco e a vista saem daqui)",
				   Voo.Andar(c.Altitude) == Voo.Andar(pl.Altitude));

		// O BIT QUE VAI NO FIO E DERIVADO DA ALTURA, e nao do campo `Voando` -- ver `EstadoDe`. E por
		// isso o corpo no colo aparece voando pra todo mundo sem um bit novo no protocolo.
		AfirmarCad("...e o `EntityState.Voando` dele e VERDADE (ele e derivado da ALTURA)",
				   EstadoDe(c, NowMs()).Voando);
		AfirmarCad("...sem que ele pague Ki de voo (o `Voando` dele continua falso -- quem voa e o outro)",
				   !c.Voando);

		// O NADO E O UNICO ESTADO COPIADO, porque nadar nao se deriva de altura.
		pl.Altitude = 0;
		pl.Nadando = true;
		TickDoAgarrao(Protocol.TickSeconds);
		AfirmarCad("...e o modo de travessia acompanha: com o carregador nadando, o corpo nada",
				   c.Nadando);

		// ============================ E LARGADO NO AR, ELE CAI ============================
		// O caso e o do pedido levado ao fim: *"pessoas podem agarrar o corpo e SAIR VOANDO com ele"* --
		// e depois soltar. Sem o relogio de voo alcancando o cadaver (ele nao esta no `_players`), o
		// corpo ficaria **pairando pra sempre** a vinte tiles do chao, que e o unico corpo do jogo a nao
		// obedecer a gravidade. Ver `TickDosRelogiosDoCorpo`.
		pl.Nadando = false;
		pl.Altitude = 5 * ZoneCollision.TileSize;
		TickDoAgarrao(Protocol.TickSeconds);

		float laEmCima = c.Altitude;
		AfirmarCad("PRECONDICAO: o corpo esta no ar, no colo", laEmCima > ZoneCollision.TileSize);

		Soltar(pl, MotivoDaSoltura.Tecla);
		pl.Altitude = 0;
		for (int i = 0; i < 5; i++) TickDosRelogiosDoCorpo(Protocol.TickSeconds);

		AfirmarCad("...e LARGADO no ar ele CAI (o relogio de voo alcanca corpo fora do `_players`)",
				   c.Altitude < laEmCima, $"{c.Altitude} x {laEmCima}");

		for (int i = 0; i < 200; i++) TickDosRelogiosDoCorpo(Protocol.TickSeconds);
		AfirmarCad("...ate o chao", c.Altitude <= 0f, $"{c.Altitude}");
	}

	// =====================================================================
	// 5) ARREMESSAR -- o corpo jogado ANDA
	// =====================================================================
	/// <summary>
	/// ============================ A OUTRA FAMILIA QUE REPROVA UM DEFEITO ANTIGO ============================
	/// O `TickDoEmpurrao` varria `_players.Values`. Um corpo fora daquele dicionario recebia
	/// `TiquesDeVoo` do `Arremessar` e ficava com eles **pra sempre**: nunca andava, e (por `Deitado`)
	/// ficava desenhado deitado. Isso ja acontecia com o boneco de quem esta meditando, contra o que o
	/// proprio `GameServer.CorpoLargado.cs` afirma por escrito.
	///
	/// A prova e a POSICAO, e nao o campo: um corpo com `TiquesDeVoo > 0` que nao anda e exatamente o
	/// defeito. Por isso a linha compara onde ele estava com onde ele foi parar.
	/// ====================================================================================================
	/// </summary>
	private void OCadaverArremessadoAnda(ServerPlayer c)
	{
		GD.Print("[cadaver] -- 5) arremessado, o corpo voa mesmo --");

		// SOLTA SE AINDA ESTIVER PRESO, e o `if` nao e enfeite: se a familia 3 reprovar (o aperto nao
		// sobreviveu ao tique), `AgarradoPorId` e ZERO -- e um `_players[0]` derrubaria a bancada no
		// meio, transformando UMA falha legivel em "a bancada estourou". Toda familia daqui pra frente
		// tem que continuar medindo mesmo com a anterior vermelha.
		if (c.AgarradoPorId != 0 && _players.TryGetValue(c.AgarradoPorId, out ServerPlayer? quemSegura))
			Soltar(quemSegura, MotivoDaSoltura.Tecla);

		Vec2 antes = c.Pos;
		Arremessar(c, new Vec2(1, 0), Empurrao.ResistenciaPadrao, 3);
		AfirmarCad("o arremesso armou o voo do cadaver", c.TiquesDeVoo > 0);

		for (int i = 0; i < 6; i++) TickDoEmpurrao();

		AfirmarCad("...E ELE ANDOU (o laco do arremesso alcanca corpo fora do `_players`)",
				   (c.Pos - antes).Length > ZoneCollision.TileSize,
				   $"andou {(c.Pos - antes).Length:0.0} px");

		for (int i = 0; i < 40; i++) TickDoEmpurrao();
		AfirmarCad("...e o voo TERMINA (ele nao fica com `TiquesDeVoo` preso, que era o sintoma antigo)",
				   c.TiquesDeVoo <= 0);
	}

	// =====================================================================
	// 6) ENTERRAR
	// =====================================================================
	/// <summary>
	/// `verb/Bury()` -- `Corpse.dm:16-21`. Os tres passos do original, e o quarto que e do port: a
	/// lapide vai pro `mundo.json`, porque um tumulo que some no reinicio nao e um tumulo.
	/// </summary>
	private void EnterrarErgueALapide(ServerPlayer pl, ServerPlayer c, List<ServerPlayer> forjados)
	{
		GD.Print("[cadaver] -- 6) enterrar ergue a lapide --");

		string quemJaz = c.NomeDeQuemMorreu;
		ZoneKey zona = c.Zone;
		Vec2 onde = c.Pos;

		// LONGE: a recusa por alcance tem que existir, senao "enterrar funciona" e uma frase sem
		// conteudo -- e o menu do cliente e uma tela, nao uma permissao.
		pl.Pos = onde + new Vec2(400, 0);
		int obras = _noChao.Count;
		Enterrar(pl, "");
		AfirmarCad("LONGE: nao enterra nada (o servidor recusa, e nao o menu)", _noChao.Count == obras);
		AfirmarCad("...e o corpo continua no mundo", ZoneList(zona.Hash).Contains(c));

		// O TEXTO DIGITADO NA CAIXA viaja como argumento do verbo (`Forma.Texto`, o `input() as text` do
		// DM) -- com as sujeiras que um campo de texto traz, pra provar que a limpeza e do servidor.
		const string digitado = "  Aqui descansa quem\n  mexeu com a pessoa errada  ";
		pl.Pos = onde + new Vec2(16, 0);
		Enterrar(pl, digitado);

		Obra? lapide = _noChao.LastOrDefault(o => o.Tipo.StartsWith("Grave_", StringComparison.Ordinal));
		AfirmarCad("PERTO: nasceu uma lapide", lapide != null);
		AfirmarCad("...com um dos CINCO sprites do `pick(1,2,3,4,5)` (`Corpse.dm:57`)",
				   lapide != null && _obras?.Get(lapide.Tipo) != null, lapide?.Tipo ?? "");
		AfirmarCad("...no ponto onde o corpo estava",
				   lapide != null && Math.Abs(lapide.X - onde.X) < 1 && Math.Abs(lapide.Y - onde.Y) < 1);
		AfirmarCad("...com o EPITAFIO que foi DIGITADO (`A.desc = \"[text]\"`, `Corpse.dm:53`), aparado e numa linha so",
				   lapide != null && lapide.Epitafio == "Aqui descansa quem mexeu com a pessoa errada",
				   lapide?.Epitafio ?? "");
		AfirmarCad("...e ela nasce APARAFUSADA: sem o quadrado dourado de obra solta em volta (o dono: 'so a lapide sem esse quadrado')",
				   lapide is { Aparafusada: true });
		AfirmarCad("VAZIO vira o padrao do DM (`input(..., \"Here lies [name]\")` confirmado sem mexer)",
				   Jandirus.Core.World.Cadaver.EpitafioLimpo("   ", quemJaz) == Jandirus.Core.World.Cadaver.EpitafioPadrao(quemJaz));
		AfirmarCad("...e o que passa do teto e cortado nele (o fio aceita 256; a lapide, `MaxEpitafio`)",
				   Jandirus.Core.World.Cadaver.EpitafioLimpo(new string('a', 500), quemJaz).Length == Jandirus.Core.World.Cadaver.MaxEpitafio);
		AfirmarCad("...e o nome de quem jaz sai do titulo do menu (`QuemJazEm` e o avesso de `NomeDo`)",
				   Jandirus.Core.World.Cadaver.QuemJazEm(Jandirus.Core.World.Cadaver.NomeDo(quemJaz)) == quemJaz
				   && Jandirus.Core.Tech.Interacoes.TextoInicial(Jandirus.Core.Tech.Interacoes.DoCadaver()[0], Jandirus.Core.World.Cadaver.NomeDo(quemJaz))
					  == Jandirus.Core.World.Cadaver.EpitafioPadrao(quemJaz));
		AfirmarCad("...e ela conhece o proprio verbo de leitura (a mesma tabela que o menu le)",
				   lapide != null && Jandirus.Core.Tech.Interacoes.Aceita(lapide.Tipo, "lapide_ler"));

		AfirmarCad("E O CORPO SUMIU do mundo", !ZoneList(zona.Hash).Contains(c));
		forjados.Remove(c);
	}

	// =====================================================================
	// 7) O TETO DA ZONA
	// =====================================================================
	/// <summary>
	/// ============================ UM TETO QUE NUNCA DISPARA E TETO NENHUM ============================
	/// Corolario 0.7, que o `Povoamento.MaxPorZona` ja cita. Entao aqui se enche a zona com um corpo a
	/// mais que o teto e se roda o TIQUE de producao -- e se cobra que quem saiu foi **o mais antigo**,
	/// que e a metade da regra que erraria calada.
	/// ============================================================================================
	/// </summary>
	private void OTetoDaZonaLevaOMaisAntigo(ServerPlayer pl, List<ServerPlayer> forjados)
	{
		GD.Print("[cadaver] -- 7) o teto da zona leva o mais antigo --");

		int teto = Jandirus.Core.World.Cadaver.TetoPorZona;
		var meus = new List<ServerPlayer>();
		for (int i = 0; i <= teto; i++)
		{
			pl.Pos = new Vec2(200 + i * 40, 200);
			ServerPlayer c = DeixarOCadaver(pl);
			c.CaiuEm = NowMs() - (teto - i) * 1000;   // o primeiro e o mais VELHO
			meus.Add(c);
			forjados.Add(c);
		}

		ServerPlayer maisVelho = meus[0];
		AfirmarCad($"PRECONDICAO: a zona passou do teto ({teto + 1} > {teto})",
				   ZoneList(pl.Zone.Hash).Count(o => o.ECadaver) > teto);

		TickDosCadaveres();

		AfirmarCad("o MAIS ANTIGO se desfez", !ZoneList(pl.Zone.Hash).Contains(maisVelho));
		AfirmarCad("...e o mais RECENTE ficou (quem sai e o velho, e nao o que alguem veio buscar)",
				   ZoneList(pl.Zone.Hash).Contains(meus[^1]));
		AfirmarCad("...e a zona voltou pro teto",
				   ZoneList(pl.Zone.Hash).Count(o => o.ECadaver) <= teto,
				   $"{ZoneList(pl.Zone.Hash).Count(o => o.ECadaver)}");

		// E ABAIXO DO TETO NADA SE DESFAZ -- **nao ha prazo**. Sem esta linha, um teto de zero passaria
		// nas tres de cima e apagaria o pedido do dono inteiro.
		int quantos = ZoneList(pl.Zone.Hash).Count(o => o.ECadaver);
		for (int i = 0; i < 5; i++) TickDosCadaveres();
		AfirmarCad("...e ABAIXO do teto nenhum corpo se desfaz sozinho (nao ha prazo, so lotacao)",
				   ZoneList(pl.Zone.Hash).Count(o => o.ECadaver) == quantos,
				   $"{ZoneList(pl.Zone.Hash).Count(o => o.ECadaver)} x {quantos}");
	}

	// =====================================================================
	// 8) O BONECO TAMBEM
	// =====================================================================
	/// <summary>
	/// ============================ O CONSERTO NAO E SO DO CADAVER ============================
	/// O `GameServer.Agarrao.cs` afirma, num bloco proprio, que o boneco do corpo largado **PODE** ser
	/// agarrado -- *"entrar em transe passa a ter risco"*. Nao podia: era o mesmo `_players.TryGetValue`.
	/// Esta familia existe pra que o dia em que alguem desfizer o conserto reprove **nos dois** lugares,
	/// e nao so no que estava sendo escrito hoje.
	///
	/// O boneco e forjado a mao (e nao pelo `LargarOCorpo`) de proposito: aquele caminho MOVE o host
	/// pra outra zona, e esta bancada ja tem uma familia inteira mexendo na zona dele. O que se mede
	/// aqui e o agarrao, e pra isso basta um corpo com `DonoDoCorpoLargado` que viva so na `ZoneList`
	/// -- que e exatamente o que o boneco e.
	/// =======================================================================================
	/// </summary>
	private void OBonecoTambemFoiConsertado(ServerPlayer pl, List<ServerPlayer> forjados)
	{
		GD.Print("[cadaver] -- 8) e o boneco do corpo largado tambem --");

		var boneco = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,
			DonoDoCorpoLargado = pl.Id,
			Name = pl.Name,
			Zone = pl.Zone,
			Pos = pl.Pos + new Vec2(300, 300),
			Race = pl.Race,
			Ficha = pl.Ficha,
			Combate = pl.Combate,
			Forma = pl.Forma,
			Livro = pl.Livro,
			Niveis = pl.Niveis,
			Visual = pl.Visual.Copiar(),
			LastInputMs = NowMs(),
		};
		ZoneList(boneco.Zone.Hash).Add(boneco);
		forjados.Add(boneco);

		pl.Pos = boneco.Pos + new Vec2(8, 0);
		pl.Facing = Facing.West;
		pl.Altitude = 0;
		if (pl.AgarrandoId != 0) Soltar(pl, MotivoDaSoltura.Tecla);

		AfirmarCad("o boneco esta na `ZoneList` e FORA do `_players` (e o que ele sempre foi)",
				   ZoneList(boneco.Zone.Hash).Contains(boneco) && !_players.ContainsKey(boneco.Id));

		Prender(pl, boneco);
		for (int i = 0; i < 3; i++) TickDoAgarrao(Protocol.TickSeconds);

		AfirmarCad("...e agarrar o corpo de quem esta em transe SOBREVIVE ao tique",
				   pl.AgarrandoId == boneco.Id && boneco.AgarradoPorId == pl.Id,
				   $"agarrando={pl.AgarrandoId}");

		Soltar(pl, MotivoDaSoltura.Tecla);
		ZoneList(boneco.Zone.Hash).Remove(boneco);
	}
}
