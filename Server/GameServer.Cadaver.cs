using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// ============================ O CADAVER QUE FICA -- o encanamento ============================
/// *"ao morrer o corpo deve FICAR NO CHAO ate alguem ENTERRAR ele (...). o corpo mesmo morto TEM
/// TODAS AS INTERACOES DE UM CORPO VIVO: pode sofrer dano, ser agarrado, jogado etc, e pessoas podem
/// agarrar o corpo e SAIR VOANDO com ele, e ele tb vai estar considerado como VOANDO"* -- o pedido.
///
/// As constantes e as frases moram no <see cref="Cadaver"/>, no Core. Aqui fica o que e do servidor:
/// **quem deixa o cadaver, quem o desfaz, e quem o enterra**.
/// ============================================================================================
///
/// ============================ E A VIAGEM PRO OUTRO MUNDO NAO FOI TOCADA ============================
/// Esta era a pergunta que podia fazer o trabalho parar, e a resposta e que **as duas coisas nao
/// brigam -- elas sao o desenho do DM**. `mob/proc/Death()` faz as duas na mesma funcao, nesta ordem:
///
///     GenerateCorpse()               // :66  -- o CADAVER, um /obj separado, FICA
///     ...
///     loc = locate(187,104,6)        // :110 -- o MOB VIAJA
///
/// Ou seja: dois objetos, e a viagem e do mob. Este port ja tinha a viagem inteira construida e
/// medida (`GameServer.Alem.cs`); o que faltava era o primeiro objeto. **A unica linha nova no
/// caminho da morte e uma chamada a <see cref="DeixarOCadaver"/> dentro do `IrProAlem`**, um instante
/// antes do `MoveToZone` -- e nada mais mudou: nem o prazo, nem a aureola, nem a triagem, nem o
/// renascimento.
///
/// ============================ POR QUE NO INSTANTE DA VIAGEM, E NAO NO DA MORTE ============================
/// O DM fotografa o cadaver no passo 5 e viaja no 11, com um `sleep(20)` (2,0 s) no meio -- ou seja
/// **la os dois corpos convivem por dois segundos**, o mob KO'd por cima do proprio cadaver. Aqui a
/// janela entre morrer e viajar e de <see cref="Alem.MsNoChao"/> = 15 s (o port a alargou de
/// proposito, justamente porque nao tinha cadaver -- ver o cabecalho daquela constante), e quinze
/// segundos de DOIS corpos empilhados no mesmo ponto seria: duas caixas de colisao no mesmo pixel,
/// dois alvos de soco, dois agarraveis, e o nome do morto aparecendo duas vezes na zona.
///
/// Deixando o cadaver **no instante em que o corpo sai**, o lugar tem sempre exatamente UM corpo: o
/// da pessoa enquanto ela olha a propria morte, e o cadaver dali em diante. A troca e invisivel
/// porque a aparencia e a mesma (`Visual.Copiar()`) e a pose tambem (`Deitado`/`Nocauteado` saem dos
/// mesmos campos). O que os 15 s querem dizer mudou de "quanto tempo o cadaver dura" pra **"quanto
/// tempo voce olha o proprio corpo antes de ser puxado"** -- e o cadaver, agora, nao acaba mais.
/// ========================================================================================================
///
/// ============================ NADA AQUI PERGUNTA "ESTA VIVO?" -- O ITEM 2 SAIU DE GRACA ============================
/// **Nao ha uma unica linha neste arquivo ensinando o cadaver a ser agarrado, arremessado ou levado
/// voando.** Ele ganha as quatro coisas por ser um CORPO na `ZoneList`:
///
///   * **colisao** -- `MontarAsGrades` varre a `ZoneList` (e nao o `_players`) de proposito, entao o
///     cadaver entra na grade sozinho. E o `EntraNaGrade` ja decidiu, por escrito, que morto no chao
///     BLOQUEIA;
///   * **agarrao** -- `CorpoNaFrente` varre a `ZoneList` e nao pergunta `Ficha.dead` (o `AlvoNaFrente`
///     do soco pergunta; o do agarrao **nao**, e o cabecalho de `GameServer.Agarrao.cs` explica que
///     essa ausencia e a decisao central de la);
///   * **arremesso** -- o agarrao desemboca no `Arremessar` do `GameServer.Empurrao.cs`, e o dano nos
///     dois na colisao ja usa `CorpoNaMinhaZona`;
///   * **carregado voando, na altitude de quem carrega** -- `LevarNoColo` escreve `Altitude` e
///     `Nadando` e o resto se deriva (`Voo.Andar`, `ClasseDeAgua.ModoDe`, `EntityState.Voando`).
///
/// **O QUE NAO SAIU DE GRACA FOI CONSERTADO LA, E NAO REMENDADO AQUI** (a instrucao da tarefa em
/// letra): o tique do agarrao e o do arremesso resolviam corpo por `_players.TryGetValue`, e o
/// cadaver -- como o boneco do corpo largado antes dele -- vive so na `ZoneList`. Ver
/// <see cref="TodosOsCorpos"/> e as notas em `GameServer.Agarrao.cs`.
/// ================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 1. DEIXAR O CADAVER
	// =====================================================================
	/// <summary>
	/// ============================ `GenerateCorpse()` -- `Corpse.dm:75-85` ============================
	/// Ergue o corpo que fica pra tras. Chamado de DOIS lugares, que sao os dois `GenerateCorpse()` do
	/// original:
	///
	///   * `IrProAlem` (`GameServer.Alem.cs`) -- o jogador, no instante em que o corpo dele viaja. E o
	///     `Death.dm:66`;
	///   * o laco de NPC morto do `TickCombate` -- o corpo sem dono, no instante em que ele sai do
	///     mundo. E o `mobDeath.dm:15`, que chama o MESMO proc.
	///
	/// ============================ O QUE ELE COPIA, E O QUE ELE NAO COPIA ============================
	/// **COPIA A FOTO E MAIS NADA** -- e essa e a resposta ao item 6 da tarefa (*"o cadaver nao
	/// pode carregar numero de vida ou BP"*). Nao ha corte escrito, ha construcao: a `Ficha` do cadaver
	/// e um <see cref="Jandirus.Core.Stats.Fighter"/> NOVO, que nasce zerado, e a `Forma` e um perfil
	/// novo na base. Ninguem precisa lembrar de apagar nada, porque nada foi escrito.
	///
	/// E e exatamente o que o DM copia: `A.icon = icon`, `A.icon_state = "KO"`, `A.overlays += overlays`.
	/// Tres linhas de DESENHO, zero linha de ficha -- e "desenho" aqui e a aparencia, o ANGULO em que o
	/// corpo estava deitado e os FERIMENTOS daquele instante (o `overlays` do DM leva o sangue e o
	/// membro que falta). Os membros viajam como estado do corpo (`Body.CopiarEstadoDe`) porque e
	/// dele que a mascara de feridas e derivada; a vida MEDIA que resulta disso nao sai no fio (o
	/// sigilo do BP ja tirou a vida alheia do snapshot) e nao e a do morto -- e a do corpo que ficou.
	///
	/// O `Race`/`Genero` VIAJAM JUNTO porque eles nao sao numero de poder -- sao o que faz o desenho
	/// existir (`VisualCatalog.CorpoSprite` escolhe a folha pela raca, e o Frost Demon nao tem corpo no
	/// catalogo: o corpo dele E a forma). Sem eles o cadaver de um Namekuseijin seria desenhado como um
	/// humano palido, que e o mesmo defeito que o `AparenciaDeNpc` ja registra.
	///
	/// ============================ E ELE NAO ENTRA NO `_players` ============================
	/// Mesma casa do boneco do corpo largado, e por uma razao PARECIDA mas nao igual. O boneco fica de
	/// fora porque COMPARTILHA a ficha do dono (estar la tiquearia BP, Ki e fome duas vezes no mesmo
	/// personagem). O cadaver tem ficha propria, entao esse perigo nao existe -- o dele e outro:
	/// `_players` quer dizer *"um corpo que o servidor SIMULA"*, e um cadaver nao e simulado. Estar la
	/// o poria no `TickDoEstomago`, que **nao pergunta `dead`**: o cadaver passaria fome, e a fome
	/// espalha dano (`Espalhar`) -- ou seja, ele se desfaria sozinho por um relogio invisivel, que e
	/// precisamente o prazo que o dono nao quer. (Os outros lacos ja se defendem: `Treinar`,
	/// `RegenerarPassivo` e o envio de ficha ja perguntam `dead` ou `Peer`.)
	///
	/// A `ZoneList` e "o que existe NESTE lugar", e e dela que saem o snapshot, o alcance do soco, a
	/// grade de colisao e a busca do agarrao -- que e tudo de que um corpo no chao precisa.
	/// ====================================================================================
	/// </summary>
	private ServerPlayer DeixarOCadaver(ServerPlayer morto)
	{
		var corpo = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,        // ninguem olha por esses olhos...
			Cerebro = null,     // ...e ninguem os dirige.
			Papel = null,       // ...e ele nao saiu de molde nenhum: nao e NPC do mundo (ver `Gente`)
			ECadaver = true,

			Name = Cadaver.NomeDo(morto.Name),
			NomeDeQuemMorreu = morto.Name,
			CaiuEm = NowMs(),

			// ONDE O CORPO CAIU, EXATAMENTE. Inclusive a ALTITUDE: quem morre voando deixa o corpo no
			// ar, e ele desce pelo mesmo `TickDoVoo` que derruba qualquer corpo com altura e sem voo --
			// nao ha queda escrita aqui.
			Zone = morto.Zone,
			Pos = morto.Pos,
			Altitude = morto.Altitude,
			Facing = morto.Facing,

			// ============================ E PRA ONDE ELE ESTAVA DEITADO -- a FOTO do angulo ============================
			// *"personagens que morreram (...) giram como se tivessem ficado de pe (...) deveriam
			// ficar (...) na mesma posicao de quando morreram"* -- o relato do dono. O corpo novo
			// nascia com `FacingDaQueda` no padrao (`South`) e `RumoDoGolpe` zerado, entao no
			// instante da troca (os 15 s de `Alem.MsNoChao`) todo cadaver ESTALAVA pro sul, fosse
			// qual fosse o lado pra que o morto tinha caido. Era isso o "levanta e gira".
			//
			// `DirecaoDeitado` e a resposta ja RESOLVIDA do morto (rumo do golpe, ou do voo, ou o olhar
			// da queda), lida ANTES de o corpo dele viajar. Escrita como queda com o rumo zerado, ela e
			// o que o `FacingFrom` devolve dali em diante -- e como o morto nao aceita rumo de golpe
			// (`ApontarRumoDoGolpe`), ela e definitiva ate um arremesso deslizar o corpo pra outro lado.
			// ==============================================================================================================
			FacingDaQueda = morto.DirecaoDeitado,

			Race = morto.Race,
			Genero = morto.Genero,
			Planeta = morto.Planeta,
			LastInputMs = NowMs(),

			// ============================ A FICHA NASCE ZERADA, E ISSO E O ITEM 6 ============================
			// `new Fighter()` -- BP 1, sem buff, sem forma, sem skill. O cadaver **nao sabe** quem foi
			// forte: nao ha `expressedBP` pra o Sense ler, nao ha vida pra vazar (e a vida alheia nem
			// viaja no snapshot desde o corte de sigilo), e nao ha nada pra somar na media do servidor
			// (`EhJogador` ja o corta pelo `Peer` nulo).
			// ============================================================================================
			Ficha = new Jandirus.Core.Stats.Fighter { Name = Cadaver.NomeDo(morto.Name), Race = morto.Race },

			// A APARENCIA E O UNICO CAMPO QUE VEM DO MORTO -- `A.icon = icon` + `A.overlays += overlays`.
			// CÓPIA e nao a instancia: o `Sanear` do catalogo reescreve a lista que recebe, e dois corpos
			// apontando pra mesma foi um bug ja registrado no clone e no boneco.
			Visual = morto.Visual.Copiar(),
		};

		// MORTO ANTES DE ENTRAR NO MUNDO. A ordem importa: `PorNoMundo` chama `PrepararCombate`, e ele
		// tem um ramo proprio pra corpo que ja chega morto (o de quem loga morto). Escrever `dead`
		// depois faria o corpo passar um quadro de pe.
		//
		// O RELOGIO DA MORTE QUE ELE ARMA NUNCA VENCE, e nao e a triagem quem o desarma: quem so vive
		// na `ZoneList` nao passa pelo laco do `TickCombate`, entao o `VenceuOPrazoDaMorte` nem e
		// perguntado sobre este corpo. Quem tira cadaver do mundo e o `TickDosCadaveres` -- ver
		// `DesfazerOCadaver`.
		corpo.Ficha.dead = true;

		// A SEQUENCIA UNICA DE NASCIMENTO DE CORPO SEM DONO (`Statify` -> `PrepararCombate` -> listas),
		// e nao uma montagem a mao: e ela que cria o `CombatState` com os MEMBROS, sem o qual o cadaver
		// derruba o servidor com nulo no primeiro `Espalhar` de um arremesso.
		//
		// ============================ `noDicionario: false` -- ELE NUNCA ENTRA NO `_players` ============================
		// Ver o cabecalho desta funcao (*"E ELE NAO ENTRA NO `_players`"*), que ja dizia isto, e o
		// `<param name="noDicionario">` do <see cref="PorNoMundo"/>, que conta o resto da historia.
		//
		// Aqui moravam DUAS linhas -- `PorNoMundo(corpo);` e `_players.Remove(corpo.Id);` -- e elas
		// pareciam se anular. Nao se anulavam: a INSERCAO de chave nova e o que invalida um `foreach`
		// sobre `_players.Values` (o `Remove` nao invalida nada desde o .NET Core 3.0), e o cadaver do
		// JOGADOR nasce de dentro do laco do `TickCombate` -- `IrProAlem` -> `DeixarOCadaver`. Resultado:
		// **toda morte de jogador matava o tique inteiro do servidor** com
		// `InvalidOperationException: Collection was modified` em `GameServer.Combat.cs:1203`, e os ~60
		// subsistemas que rodam depois do combate perdiam o quadro. O cadaver do NPC nunca deu problema
		// porque ele nasce no dreno do `_npcsPraTirar`, que e FORA do laco.
		//
		// O conserto e nao inserir, e nao "inserir com cuidado": nada entre a insercao e a remocao lia o
		// `_players` (o resto do `PorNoMundo` e `ZoneList().Add` e `AplicarGravidade`, que so olham
		// `_zones` e a ficha), entao o par era um no-op com uma mina embaixo.
		// ============================================================================================================
		//
		// ============================ E O INTERRUPTOR DO DEFEITO INJETADO, LETRA POR LETRA ============================
		// `_cadaverNoPlayersDeTeste` e FALSO em jogo, sempre -- so a bancada `--tiquedamorte` o liga, e
		// o que ele reproduz e EXATAMENTE o par que morava aqui: `PorNoMundo(corpo)` (que inseria) mais
		// o `_players.Remove(corpo.Id)` da linha seguinte.
		//
		// Ele mora AQUI e nao dentro do arquivo de teste pela mesma razao escrita no `_borraoSoComSkill`
		// (`GameServer.Combat.cs`) e no `_dcGradeCega` (`GameServer.Corpos.cs`): o criterio tem que ser
		// o MESMO objeto com e sem o defeito. Uma copia do caminho da morte escrita na bancada mediria
		// a copia concordando consigo mesma -- e este projeto ja catalogou esse cego ("a bancada mede
		// INTENCAO"). Custa uma comparacao de bool por cadaver, e cadaver e coisa rara.
		//
		// **SEM ELE, PROVAR QUE A BANCADA SABE FICAR VERMELHA EXIGE EDITAR DOIS ARQUIVOS A MAO** -- e uma
		// afirmacao que so foi vista passando nao se distingue de uma constante.
		// ==========================================================================================================
		PorNoMundo(corpo, noDicionario: _cadaverNoPlayersDeTeste);
		if (_cadaverNoPlayersDeTeste) _players.Remove(corpo.Id);

		// ============================ `A.overlays += overlays` -- OS FERIMENTOS SAO A FOTO ============================
		// `Corpse.dm:82`: o cadaver copia os overlays do mob NAQUELE instante -- o sangue, o roxo e
		// o membro que faltava ficam nele. Aqui as feridas nao sao overlay: sao DERIVADAS do corpo
		// (`Feridas.De`), e o `PrepararCombate` acima acabou de dar ao cadaver um `Body.Novo()` --
		// inteiro, limpo, com os dois bracos. Era a outra metade do relato do dono (*"deveriam ficar
		// com os ferimentos"*): aos 15 s o corpo destrocado virava um corpo sao deitado no chao.
		//
		// A COPIA VEM ANTES DO `Restaurar()` do `IrProAlem` (a ordem esta la): o morto viaja inteiro,
		// e o que fica e o que ele era quando caiu. E e copia, nao instancia -- bater no cadaver nao
		// pode ferir quem ja esta no Outro Mundo.
		//
		// `_cadaverSemFotoDeTeste` e o interruptor do DEFEITO INJETADO da `--doiscorposteste`: com ele
		// ligado o cadaver fica com o `Body.Novo()` que tinha antes -- letra por letra o corpo limpo
		// que o dono viu. Falso em jogo, sempre; mora aqui pela mesma regra do
		// `_cadaverNoPlayersDeTeste` logo acima.
		// ============================================================================================================
		if (!_cadaverSemFotoDeTeste)
		{
			corpo.Combate.Corpo.CopiarEstadoDe(morto.Combate.Corpo);
			corpo.Combate.SincronizarVida();
		}
		corpo.EnvFeridas = Feridas.De(corpo.Combate.Corpo);

		// QUEM ESTA NA ZONA PRECISA VER O CORPO -- mesmo funil do NPC e do boneco. Sai ANTES do
		// `MoveToZone` de quem viaja (ver a ordem no `IrProAlem`): assim a aparencia do cadaver chega
		// pelo canal confiavel antes do `PeerLeft` do corpo que partiu, e a troca nao pisca.
		TrocarAparencias(corpo);

		// E AS FERIDAS DELE, UMA VEZ, LOGO ATRAS DA APARENCIA. O `TickDasFeridas` so varre o `_players`
		// (e o cadaver nao esta la, de proposito), entao sem esta linha a mascara fotografada acima
		// nunca sairia do servidor: quem estava olhando veria o corpo limpo ate o proximo tique que
		// lesse este cadaver -- e nenhum le. Quem CHEGA depois a recebe pelo `TrocarFeridas`, que ja
		// varre a `ZoneList` e ja reapresenta o `EnvFeridas` de todo corpo dela, cadaver incluso.
		MandarFeridas(corpo);

		GD.Print($"[server] {morto.Name} deixou o corpo em {morto.Zone.Name} @ "
			   + $"({morto.Pos.X:0},{morto.Pos.Y:0}) -- cadaver #{corpo.Id}");
		return corpo;
	}

	/// <summary>
	/// TIRA UM CADAVER DO MUNDO -- a porta unica, e sao QUATRO caminhos ate ela: enterrar, destruir
	/// pelo dano, o teto da zona, e o admin.
	///
	/// Ela e o <see cref="RemoverNpc"/> com uma diferenca: o cadaver **nao esta no `_players`**, entao
	/// aquele metodo tiraria de um dicionario onde ele nunca esteve. O que importa e o resto -- sair da
	/// `ZoneList` e avisar a zona com `PeerLeft`, sem o qual o corpo fica congelado na tela de quem
	/// estava olhando (o mesmo defeito que o `RemoverClone` ja tratava).
	/// </summary>
	private void DesfazerOCadaver(ServerPlayer corpo)
	{
		if (!corpo.ECadaver) return;

		// ============================ SOLTA QUEM ESTIVER SEGURANDO ANTES DE SUMIR ============================
		// Enterrar um corpo que alguem esta carregando e o caso que MAIS acontece (o pedido do dono e
		// literalmente *"vc pode AGARRAR o corpo e levar pra outro lugar"* -- e enterrar la). Sem esta
		// linha o carregador ficaria com `AgarrandoId` apontando pra um fantasma: a varredura orfa do
		// `TickDoAgarrao` limparia o lado do PRESO (que nao existe mais) e o dele so seria limpo pela
		// primeira passada -- que ja e uma rede de seguranca e nao devia ter que ser usada de rotina.
		// ================================================================================================
		if (corpo.AgarradoPorId != 0
			&& _players.TryGetValue(corpo.AgarradoPorId, out ServerPlayer? quemSegura)
			&& quemSegura.AgarrandoId == corpo.Id)
			Soltar(quemSegura, MotivoDaSoltura.OutraZona);

		List<ServerPlayer> zona = ZoneList(corpo.Zone.Hash);
		zona.Remove(corpo);

		var w = Protocol.Begin(Protocol.S2C.PeerLeft);
		w.Put(corpo.Id);
		foreach (ServerPlayer o in zona)
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// 2. O TIQUE -- o teto da zona e o corpo destrocado
	// =====================================================================
	/// <summary>
	/// ============================ O TIQUE DOS CADAVERES, a 5 Hz ============================
	/// Ele faz TRES coisas, e nenhuma delas e um prazo:
	///
	///   1. **DESFAZ O CORPO DESTROCADO** -- e o `verb/Destroy()` do DM (`Corpse.dm:22-26`), so que
	///      pelo golpe em vez do menu. E o que da sentido a *"pode sofrer dano"*: bater num cadaver ate
	///      o fim o desintegra, e o arremesso contra uma parede tambem;
	///   2. **APLICA O TETO DA ZONA** -- ver <see cref="Cadaver.TetoPorZona"/>. Quando estoura, o MAIS
	///      ANTIGO se desfaz;
	///   3. **DIZ A QUEM ESTA POR PERTO QUE HA UM CORPO AO ALCANCE** -- o alvo virtual da tecla E.
	///
	/// **A 5 Hz e nao no tique cheio** porque nenhuma das tres e um evento de quadro: o corpo
	/// destrocado pode sumir 200 ms depois, o teto e uma contagem, e a dica de "[E] corpo de Fulano"
	/// aparece quando o jogador chega perto -- que leva mais de 200 ms.
	/// ======================================================================================
	/// </summary>
	private void TickDosCadaveres()
	{
		// A LISTA REUSADA, pelo mesmo argumento do `_npcsPraTirar`: ela sai vazia quase sempre, e
		// alocar por tique num laco periodico e lixo constante pro coletor.
		_cadaveresPraTirar.Clear();

		foreach ((ulong _, List<ServerPlayer> zona) in _zones)
		{
			int vivos = 0;
			ServerPlayer? maisAntigo = null;

			foreach (ServerPlayer c in zona)
			{
				if (!c.ECadaver) continue;

				// ---- 1. DESTROCADO: o `Destroy()` do DM, pelo golpe ----
				// A pergunta e a MESMA que decide o nocaute e a morte de todo mundo (`Corpo.Partes`), e
				// nao um numero proprio: um corpo cujos membros chegaram todos a zero acabou. Nao ha
				// "HP do cadaver" em lugar nenhum -- so os membros, que ja existiam.
				if (EstaDestrocado(c)) { _cadaveresPraTirar.Add(c); continue; }

				vivos++;
				if (maisAntigo == null || c.CaiuEm < maisAntigo.CaiuEm) maisAntigo = c;

				// ---- 1b. AS FERIDAS DELE ACOMPANHAM O DANO QUE ELE LEVA ----
				// O cadaver *"pode sofrer dano"* (o pedido), e dano muda a mascara. O `TickDasFeridas`
				// nao o alcanca (ele so varre o `_players`), e este e o unico laco que passa por todo
				// cadaver do mundo -- entao a mesma pergunta por diferenca mora aqui. Sem isto o corpo
				// que apanha ate se desfazer continuaria com a cara de quando caiu.
				MascaraDeFeridas feridasAgora = Feridas.De(c.Combate.Corpo);
				if (feridasAgora != c.EnvFeridas) { c.EnvFeridas = feridasAgora; MandarFeridas(c); }
			}

			// ---- 2. O TETO DA ZONA ----
			// UM POR PASSADA e nao um laco: a 5 Hz, uma zona que estourou o teto em dez corpos volta ao
			// limite em dois segundos, e nenhum tique paga dez remocoes (cada uma manda um `PeerLeft`
			// confiavel pra zona inteira). E a mesma disciplina do `NascimentosPorTique` do povoamento.
			if (vivos > Cadaver.TetoPorZona && maisAntigo != null) _cadaveresPraTirar.Add(maisAntigo);
		}

		foreach (ServerPlayer c in _cadaveresPraTirar)
		{
			GD.Print($"[server] o {c.Name} se desfez em {c.Zone.Name}");
			DesfazerOCadaver(c);
		}

		// ---- 3. O ALVO VIRTUAL DA TECLA E ----
		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.Peer == null) continue;
			int perto = CadaverPerto(pl)?.Id ?? 0;
			if (perto == pl.EnvCadaverPerto) continue;   // so quando MUDA -- ver `MandarCadaverPerto`
			pl.EnvCadaverPerto = perto;
			MandarCadaverPerto(pl);
		}
	}

	/// <summary>Os cadaveres que saem do mundo nesta passada. Instancia reusada -- ver o tique.</summary>
	private readonly List<ServerPlayer> _cadaveresPraTirar = [];

	/// <summary>
	/// ESTE CORPO ACABOU? Todos os membros que ainda estao no lugar chegaram a zero.
	///
	/// O `Decepado` NAO CONTA como "acabado": um cadaver sem os dois bracos continua sendo um cadaver
	/// (e o `Corpo.Restaurar` que devolve membro, e ele nao roda em cadaver nenhum). O que acaba com
	/// ele e o corpo inteiro estar em zero -- que e o mesmo criterio de `DeveNocautear`/morte que o
	/// resto do jogo usa, lido de outro angulo.
	/// </summary>
	private static bool EstaDestrocado(ServerPlayer c)
	{
		foreach (Jandirus.Core.Combat.BodyPart p in c.Combate.Corpo.Partes)
			if (!p.Decepado && p.Vida > 0) return false;
		return true;
	}

	// =====================================================================
	// 3. A TECLA E
	// =====================================================================
	/// <summary>
	/// O CADAVER AO ALCANCE DA MAO, ou nulo.
	///
	/// O CORTE E O MESMO DE TODA COISA COM QUE SE MEXE (<see cref="Jandirus.Core.Tech.Interacoes.Alcance"/>,
	/// 64 px, por EIXO e nao por raio) -- e o proprio cabecalho daquela constante conta o que acontece
	/// quando um alcance diverge do outro: uma banda morta em que o menu abre e o servidor recusa.
	///
	/// O MAIS PROXIMO GANHA, como em toda a familia: com dois corpos empilhados, enterrar tem que
	/// escolher um, e "o de baixo" nao e uma resposta que o jogador consiga prever.
	/// </summary>
	private ServerPlayer? CadaverPerto(ServerPlayer pl)
	{
		ServerPlayer? melhor = null;
		float melhorDist = float.MaxValue;

		foreach (ServerPlayer c in ZoneList(pl.Zone.Hash))
		{
			if (!c.ECadaver) continue;
			if (Math.Abs(c.Pos.X - pl.Pos.X) > Jandirus.Core.Tech.Interacoes.Alcance
				|| Math.Abs(c.Pos.Y - pl.Pos.Y) > Jandirus.Core.Tech.Interacoes.Alcance) continue;

			float d = (c.Pos - pl.Pos).LengthSquared;
			if (d >= melhorDist) continue;
			melhorDist = d;
			melhor = c;
		}
		return melhor;
	}

	/// <summary>
	/// ============================ DIZ AO CLIENTE QUE HA UM CORPO AO ALCANCE ============================
	/// O mesmo desenho do <see cref="Protocol.S2C.Veiculo"/>, e pelo mesmo motivo, escrito la: a tecla E
	/// procura alvo na LISTA DE CONSTRUCOES da zona, e um cadaver nao e uma construcao -- ele e um
	/// corpo. Sem este pacote o menu de interacao simplesmente nao teria como enxerga-lo, exatamente
	/// como o piloto nao enxergava o proprio veiculo.
	///
	/// **NAO CABIA NO SNAPSHOT**, e a razao e a mesma que aquele campo escreveu: o snapshot sai 30 vezes
	/// por segundo POR CORPO e pra zona inteira, e "ha um cadaver a dois tiles de mim" muda uma vez por
	/// aproximacao e interessa a UMA pessoa. Um bit por corpo por tique pra carregar isso seria o canal
	/// errado -- e nao ha bit sobrando (os dois bytes de flags fecharam).
	///
	/// **E NAO E UM `bool`, E O ID + O NOME.** O id porque e ele que volta no verbo (senao o servidor
	/// teria que reachar o alvo e as duas pontas poderiam escolher corpos diferentes com dois
	/// empilhados); o nome porque o cliente **nao sabe** de quem e o corpo -- o `Name` do cadaver viaja
	/// no pacote de aparencia, e o menu precisa escrever "corpo de Fulano" no titulo. E o mesmo
	/// argumento que fez o nome do veiculo viajar junto do tipo dele.
	///
	/// **ID ZERO QUER DIZER "NENHUM"**, e por isso este pacote tem o "apagar": e ele que faz a dica
	/// sumir quando o jogador se afasta.
	/// ================================================================================================
	/// </summary>
	private void MandarCadaverPerto(ServerPlayer pl)
	{
		if (pl.Peer == null) return;
		ServerPlayer? c = CadaverPerto(pl);
		var w = Protocol.Begin(Protocol.S2C.Cadaver);
		w.Put(c?.Id ?? 0);
		w.Put(c?.Name ?? "");
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// ============================ ENTERRAR -- `verb/Bury()` (`Corpse.dm:16-21`) ============================
	///     to_chat(view(), "[usr] buries [name]")
	///     GenerateCross(input(usr,"Grave text.","","Here lies [name]") as text)
	///     del(src)
	///
	/// Tres passos, e os tres estao aqui: a zona inteira fica sabendo, nasce a lapide, o corpo some. O
	/// unico que diverge e o do meio -- **o texto nao e digitado**, porque o menu deste port nao sabe
	/// pedir texto (ver <see cref="Cadaver.EpitafioPadrao"/>, que cita a decisao ja escrita no
	/// catalogo de interacoes). A lapide nasce com a resposta PADRAO que o DM oferece pre-preenchida.
	///
	/// ============================ QUEM PODE ENTERRAR: QUALQUER UM, E DE PROPOSITO ============================
	/// O `verb` do DM nao tem crivo nenhum -- `set src in view(1)` e acabou: nem amizade, nem karma, nem
	/// ser o dono do corpo. O inimigo que te matou pode te enterrar, e essa e uma cena que o jogo devia
	/// poder contar. As unicas recusas sao de ALCANCE (o corpo tem que estar ali) e de ESTADO
	/// (`PodeMexerOCorpo` -- quem esta caido, em cena ou carregando Ki nao cava nada), e a segunda e a
	/// MESMA que o agarrao e o passo usam, pelo argumento de sempre: uma recusa nova nasce valendo pros
	/// tres no mesmo dia.
	/// ====================================================================================================
	/// </summary>
	private void Enterrar(ServerPlayer pl)
	{
		if (CadaverPerto(pl) is not { } corpo)
		{ Avisar(pl, "não há corpo nenhum ao seu alcance."); return; }

		if (!PodeMexerOCorpo(pl)) { Avisar(pl, "não dá pra cavar nada assim."); return; }

		// A LAPIDE PRIMEIRO, E O CORPO DEPOIS. Se a cova nao puder ser aberta (ha coisa demais neste
		// ponto), o corpo NAO pode sumir -- enterrar sem lapide seria apagar o cadaver e nao dar nada em
		// troca. Mesma ordem que o `Colher` usa com a mochila e a maca.
		if (!ErguerALapide(pl, corpo)) return;

		// `to_chat(view(), "[usr] buries [name]")` -- a zona inteira, e nao so quem cavou.
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (o.Peer != null) Avisar(o, $"{pl.Name} enterra o {corpo.Name}.");

		DesfazerOCadaver(corpo);
	}

	/// <summary>
	/// ============================ `GenerateCross()` -- `Corpse.dm:50-73` ============================
	///     var/obj/A = new ; A.name = "Grave" ; A.desc = "[text]"
	///     A.loc = locate(src.x,src.y,src.z)
	///     gravecon = pick(1,2,3,4,5) ; A.icon = 'Graves.dmi' ; A.icon_state = "[gravecon]"
	///
	/// A lapide e uma <see cref="Obra"/>, e ela e a coisa certa: uma `Obra` e desenhada pelo cliente com
	/// sprite do catalogo, entra no Y-sort, aparece na lista da zona e -- o que mais importa aqui --
	/// **vai pro `mundo.json`**. Um tumulo que sumisse no reinicio nao seria um tumulo.
	///
	/// ============================ O `pick(1..5)` VIROU CINCO TIPOS, E NAO UM CAMPO NOVO ============================
	/// O sorteio do DM e de ICON_STATE, e no port o estado de uma obra vem do CATALOGO, por tipo. As
	/// duas saidas eram: um campo `Estado` novo na `Obra` (que iria pro disco em cada uma das 105
	/// construcoes pra ser usado por uma so), ou cinco entradas no catalogo. Cinco entradas e o `switch`
	/// do original escrito onde ele ja estaria -- e o `Interacoes.De` aceita as cinco pela mesma linha.
	///
	/// O NOME DELA E O EPITAFIO, e nao "Grave": o pacote de construcoes carrega um nome POR INSTANCIA
	/// (foi por isso que o console da ponte deixou de se chamar "Ship Control" em ingles), entao a
	/// lapide chega ao menu do jogador ja dizendo *"Aqui jaz Fulano"*. O `A.desc` do DM, no lugar onde
	/// este port mostra descricao.
	/// ============================================================================================================
	/// </summary>
	private bool ErguerALapide(ServerPlayer quemCava, ServerPlayer corpo)
	{
		// NAO EMPILHA -- a mesma guarda do `Assentar`, e ela e a que faz um cemiterio ser um cemiterio
		// em vez de cinco lapides no mesmo pixel. Meio tile de folga, como la.
		if (_noChao.Any(o => o.Zona.Equals(corpo.Zone)
							 && Math.Abs(o.X - corpo.Pos.X) < 24 && Math.Abs(o.Y - corpo.Pos.Y) < 24))
		{ Avisar(quemCava, "já tem coisa demais neste ponto pra abrir uma cova."); return false; }

		var lapide = new Obra
		{
			Id = _proximaObraId++,
			// `gravecon = pick(1,2,3,4,5)` -- `Corpse.dm:57`
			Tipo = $"Grave_{_rng.Next(1, 6)}",
			X = corpo.Pos.X,
			Y = corpo.Pos.Y,

			// O DONO E QUEM CAVOU, e nao quem morreu: `DonoConta`/`DonoNome` querem dizer "quem ergueu
			// isto" no port inteiro (e ninguem constroi a propria lapide). Quem jaz ali esta no NOME da
			// obra, que e o epitafio.
			DonoConta = quemCava.Conta,
			DonoNome = quemCava.Name,
			ErguidaEm = NowMs(),

			// ARMADURA PADRAO e nao a do BP de quem cavou. A regra do `Assentar` (*"a bancada de um
			// lutador forte e dificil de derrubar"*) e sobre uma construcao que a pessoa ERGUEU pra si;
			// uma lapide de granito indestrutivel porque o coveiro era forte seria o mesmo numero
			// dizendo outra coisa. Aqui ela cai como a mobilia do mapa cai.
			ArmaduraMax = Jandirus.Core.Combat.Armadura.Padrao,
			Armadura = Jandirus.Core.Combat.Armadura.Padrao,

			// O EPITAFIO -- `A.desc = "[text]"`, com a resposta padrao do `input()` do DM.
			Epitafio = Cadaver.EpitafioPadrao(corpo.NomeDeQuemMorreu),
		};
		lapide.PorZona(corpo.Zone);

		_noChao.Add(lapide);
		GravarMundo();
		MandarObras(corpo.Zone);

		GD.Print($"[server] {quemCava.Name} enterrou {corpo.NomeDeQuemMorreu} em {corpo.Zone.Name} "
			   + $"@ ({corpo.Pos.X:0},{corpo.Pos.Y:0}) -- lapide #{lapide.Id}");
		return true;
	}

	/// <summary>
	/// LER A LAPIDE -- o `desc` do BYOND, que se le por `Examine`. Aqui e um botao do mesmo menu.
	///
	/// Ela existe porque uma lapide que nao diz quem esta ali e uma pedra: o epitafio ja viaja no NOME
	/// da obra (e portanto ja esta no titulo do menu), e esta acao e o que da a ele um lugar no CHAT --
	/// onde ele fica, e onde quem passou depois pode rolar pra cima e ler.
	/// </summary>
	private void LerALapide(ServerPlayer pl)
	{
		Obra? l = ObraQueAceita(pl, "lapide_ler");
		if (l == null) { Avisar(pl, "não há lápide nenhuma por perto."); return; }
		Avisar(pl, l.Epitafio.Length > 0 ? $"a lápide diz: \"{l.Epitafio}\"" : "a lápide está em branco.");
	}
}
