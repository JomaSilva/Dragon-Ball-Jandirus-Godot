using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// ============================ A MORTE LEVA PRO OUTRO MUNDO ============================
/// *"falta fazer o personagem quando MORRER ir pro OUTRO MUNDO e a AUREOLA aparecer sobre a
/// cabeca"* -- o pedido do dono, e este arquivo e a metade que conhece o servidor. A outra metade
/// (o LUGAR e os PRAZOS) esta em <see cref="Alem"/>, no Core.
///
/// Porte de `mob/proc/Death()` -- `Code/Modules/Death/Death.dm:6-111`. La sao doze passos; oito
/// deles ja estavam neste port espalhados por outros arquivos (a negacao da morte pela Aura of
/// Destruction, o Zenkai do algoz, a raiva de quem assistiu, a sucessao do trono, a saida da luta).
/// O que faltava eram os TRES ULTIMOS, e eles sao o pedido inteiro:
///
///     overlayList += 'Halo.dmi'      // :106-108 -- a aureola
///     loc = locate(187, 104, 6)      // :110    -- a viagem
///     SpreadHeal(100, 1, 1)          // :111    -- chega curado
/// ================================================================================
///
/// ============================ AS DUAS ETAPAS, E POR QUE SAO DUAS ============================
/// A morte deixou de ser um instante e passou a ser um percurso com dois prazos:
///
///     morre --[ Alem.MsNoChao ]--> Outro Mundo --[ Alem.MsNoAlem ]--> vivo, no berco
///       ^                             ^
///       |                             |
///   corpo CAIDO, na pose de       de PE, com AUREOLA, andando
///   nocaute e SEM aureola:        (`Un_KO()` do DM, `Death.dm:89`)
///   e o cadaver, e o cadaver
///   do DM (`GenerateCorpse`,
///   `:64-67`) e fotografado
///   ANTES do `+= 'Halo.dmi'`
///   de `:106-108`
///
/// A PRIMEIRA ETAPA E DO DM: `KO(-1)` + `sleep(20)` ANTES do `loc =` (`:71-110`). Ela existe pra
/// quem viu a morte ver a morte -- sem ela, matar alguem seria "o inimigo piscou e nao estava mais
/// la", que le PIOR do que o jogo lia antes desta mudanca.
///
/// A SEGUNDA E DO PORT, e e um andaime declarado -- ver <see cref="Alem.MsNoAlem"/>. Nenhum dos
/// cinco caminhos de volta do original existe aqui ainda; sem uma volta, a viagem prenderia todo
/// mundo, e a instrucao do dono e *"prefira sempre o que nao prende ninguem"*.
/// ========================================================================================
///
/// ============================ UM RELOGIO SO, E ELE NAO E MAIS ESCRITO A MAO ============================
/// `pl.RenasceEm = NowMs() + MsAteRenascer` estava em NOVE lugares. Agora e o gancho
/// <see cref="Jandirus.Core.Combat.CombatState.AoMorrer"/>, instalado uma vez no `PrepararCombate`
/// ao lado do `NegarMorte` -- pelo mesmo argumento que criou aquele.
/// ====================================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// 1. O GANCHO -- "este corpo acabou de morrer"
	// =====================================================================
	/// <summary>
	/// O UNICO LUGAR QUE MARCA UMA MORTE. Pendurado em `CombatState.AoMorrer` pelo
	/// `PrepararCombate`, entao toda morte de todo corpo passa por aqui: soco, Kamehameha,
	/// explosao do planeta, calor da estrela, Final Explosion, Kaio-ken que estoura, fome,
	/// gestacao de bio-androide e o verb de admin.
	///
	/// ============================ ELE SO ESCREVE UM NUMERO, DE PROPOSITO ============================
	/// `Morrer()` e chamado de dentro do `MeleeResolver` e dos lacos de dano em area, que estao
	/// percorrendo a lista de uma zona. Viajar daqui mexeria nas listas de DUAS zonas no meio dessa
	/// varredura -- o "Collection was modified" que o `_npcsPraTirar` e o `TickDeQuemVolta` ja
	/// existem pra evitar. Quem viaja e o <see cref="PassoDaMorte"/>, no fim do prazo.
	/// ============================================================================================
	/// </summary>
	private void AMorteAconteceu(ServerPlayer pl)
	{
		pl.RelogioDaMorte = NowMs() + Alem.MsNoChao;

		// ============================ E A VIAGEM COMECA DEVENDO -- ESTE CORPO E O CADAVER ============================
		// A aureola nao acende aqui: acende na viagem (`Death.dm:64-67` fotografa o cadaver ANTES do
		// `overlayList += 'Halo.dmi'` de `:106-108`). Ver `Alem.TemAureola`.
		//
		// ESTA LINHA NAO E "lembrar de apagar um bit": ela e o REARME da mesma etapa que a linha acima
		// arma, no unico funil por onde toda morte passa. Sem ela, a segunda morte de alguem herdaria
		// o `true` que a viagem da PRIMEIRA deixou, e o cadaver voltaria a nascer com aureola -- o bug
		// do dono, ressuscitado so pra quem ja morreu uma vez, que e a pior forma dele.
		// ========================================================================================================
		pl.MorteJaViajou = false;

		// E O MESMO REARME PRA TRAVA DO KARMA -- `pk_karma_taken`, zerado no `ReviveMe` do original
		// (`SkyNPCs.dm:104`). Ela entra aqui e nao no revive pela razao que a linha de cima ja
		// documenta: este e o unico funil por onde TODA morte passa, e o revive tem varios caminhos.
		// Sem ela, a segunda morte de alguem herdaria o `true` da primeira e ninguem pagaria karma
		// por assassina-lo nunca mais. Ver `GameServer.Karma.cs`.
		pl.KarmaDaMorteContado = false;

		// ============================ QUEM MORRE DENTRO DA MENTE NAO MORREU ============================
		// **NAO HA CORTE ESCRITO AQUI, E ISSO E UMA DECISAO.** O mecanismo que desfaz esta morte ja
		// existe e ja e chamado no MESMO tique: `BordasDeQuemEstaFora` (`GameServer.CorpoLargado.cs`)
		// enfileira o dono assim que ve `pl.Ficha.dead`, o `TickDeQuemVolta` chama `SairDaMente`, e o
		// `RestaurarDaMente` reescreve `dead = f.Morto` -- o estado com que a pessoa ENTROU, que e
		// sempre "viva" (`EntrarNaMente` recusa quem esta caido ou morto).
		//
		// Ou seja: quando o prazo dos 15 s vencer, `pl.Ficha.dead` ja e falso ha 15 s e o
		// `PassoDaMorte` nem e alcancado. E o `MindMeditate.dm:448` -- *"morte MENTAL nao e real"* --
		// funcionando pelo caminho que o port ja tinha, e nao por um `if (NaMente(pl))` novo aqui.
		//
		// Um segundo corte neste ponto seria a copia que um dia discorda: bastaria alguem mexer no
		// prazo de uma das duas pontas pra as duas regras contarem historias diferentes sobre a mesma
		// morte. Fica ESCRITO porque a ausencia precisa ser lida como escolha.
		// ==========================================================================================

		if (!EhJogador(pl)) return;   // corpo sem dono nao le mensagem nenhuma

		// A AUREOLA SAIU DESTA FRASE junto com o bit: ela nao se acende aqui. O que este instante tem
		// e o cadaver, e o cadaver e o corpo exato de quem morreu -- ver `Alem.TemAureola`.
		Avisar(pl, "você morre. O seu corpo esfria no chão, exatamente como você caiu.");
	}

	// =====================================================================
	// 2. A TRIAGEM -- de quem e este corpo que morreu?
	// =====================================================================
	/// <summary>
	/// ============================ TRES GRUPOS DE CORPO, TRES DESTINOS ============================
	/// Chamado uma vez por corpo, quando o relogio da morte vence. **Esta e a unica pergunta "quem
	/// e voce?" do caminho da morte** -- ver <see cref="Jandirus.Core.Npc.Gente"/>, que existe
	/// justamente porque regras de mundo confundiam corpo com pessoa.
	///
	///   * **NPC DO MUNDO** (cidadao, Rei, chefe de saga) -- SAI DO MUNDO, nao renasce e nao viaja.
	///     Este `if` ja varreu todos os `_players` sem olhar `Peer`, e `Renascer` manda pro
	///     `DestinoDoBerco`: um corpo sem dono nao tem berco (`Berco.Planeta` vazio) e o funil
	///     responde `SpawnZone` -- **a Terra**. Com o povoamento ligado isso queria dizer: mate o
	///     cidadao de Namek e ele reaparece na Terra 15 s depois, vivo, pra sempre; o Freeza de uma
	///     saga voltaria sozinho depois de derrotado. Quem repoe habitante e a MANUTENCAO
	///     (`TickDoPovoamento`), como no original -- o morto sai da conta e outro nasce noutro lugar.
	///     A remocao e ADIADA (`_npcsPraTirar`) porque o chamador esta percorrendo `_players.Values`.
	///
	///   * **JOGADOR** -- percorre a morte: <see cref="PassoDaMorte"/>. E o unico grupo que ve o
	///     Outro Mundo, porque e o unico que tem alguem lendo a tela.
	///
	///   * **O TERCEIRO GRUPO** (reflexo da mente, boneco do corpo largado, fera possuida, chefe
	///     convocado sem papel, corpo forjado de bancada) -- **NADA**, so o relogio desarmado.
	///     Aqui morava um `else` largo, "quem nao e NPC do mundo renasce", e ele e FALSO: sao TRES
	///     grupos e nao dois. Um reflexo renasceria **na Terra**, vivo, e ficaria la pra sempre como
	///     um corpo dirigido sem dono -- nao aparecia porque o `DirigirClone` costuma desfazer o
	///     transe no mesmo tique, ou seja, dependia de uma corrida. Com a viagem pro Outro Mundo
	///     ficaria pior: **o reflexo de alguem apareceria de pe na mesa do Enma**. Quem ergueu o
	///     corpo cuida dele -- o `SairDaMente`, o `TickDeQuemVolta`, o dono do clone --, e nao este
	///     funil.
	/// ==========================================================================================
	///
	/// ============================ POR QUE ELA SAIU DE DENTRO DO `TickCombate` ============================
	/// Eram quatro linhas no meio do laco, com o argumento acima escrito por cima delas -- e nenhuma
	/// bancada conseguia alcanca-las sem rodar o tique inteiro do servidor. **A triagem e a metade da
	/// morte que erra calado**: os tres destinos sao invisiveis em jogo (ninguem ve um reflexo NAO
	/// aparecer no alem). Uma casa so, dois chamadores -- o tique e o `--alemteste`.
	/// ================================================================================================
	/// </summary>
	private void VenceuOPrazoDaMorte(ServerPlayer pl)
	{
		// ============================ SEM DUPLICATA NA FILA, COMO NA `PorNaFilaDeVolta` ============================
		// Este ramo NAO rearma o `RelogioDaMorte` -- de proposito: o corpo sem dono sai do mundo no dreno
		// do fim deste mesmo tique, entao nao ha prazo pra remarcar. So que, se o dreno nao chegar a rodar
		// (o `_npcsPraTirar.Clear()` e a ULTIMA linha do `TickCombate`, e qualquer excecao no meio do
		// tique o pula), o mesmo corpo continua morto e vencido e cai aqui de novo no tique seguinte --
		// agora com DUAS entradas na fila. Quando um tique enfim completa, o dreno passa duas vezes pelo
		// mesmo NPC: `MorreuUmCorpoSemDono` cobrado em dobro (reputacao, rancor) e `DeixarOCadaver`
		// chamado duas vezes -- **dois cadaveres empilhados pra um NPC so**, e isso o jogador ve.
		//
		// Foi exatamente o que aconteceu enquanto o cadaver do jogador derrubava o tique (ver o
		// comentario grande no `TickCombate`). Aquele defeito esta fechado; esta guarda fecha a familia
		// dele, e e a mesma linha que a `PorNaFilaDeVolta` ja usa na fila vizinha.
		// ======================================================================================================
		if (EhNpcDoMundo(pl))
		{
			if (!_npcsPraTirar.Contains(pl)) _npcsPraTirar.Add(pl);
			return;
		}
		if (EhJogador(pl)) { PassoDaMorte(pl); return; }

		// clone/reflexo/boneco/fera/corpo forjado: nao e conta daqui, e o relogio nao volta a vencer
		pl.RelogioDaMorte = long.MaxValue;
	}

	// =====================================================================
	// 3. O PERCURSO -- chamado da triagem quando o prazo vence
	// =====================================================================
	/// <summary>
	/// VENCEU O PRAZO: ou sobe pro Outro Mundo, ou volta a vida. Qual dos dois se responde pelo
	/// LUGAR onde o corpo esta -- ver <see cref="Alem.MortoDePe"/> pro argumento de por que a etapa
	/// nao e um campo.
	/// </summary>
	private void PassoDaMorte(ServerPlayer pl)
	{
		if (Alem.EhOAlem(pl.Zone)) { Renascer(pl); return; }

		// ============================ QUEM FICA COM O CORPO NAO E ARRANCADO -- `Stats.dm:275-292` ============================
		// `Keep_Body` de cargo (`OtherworldRankSkills.dm:195-202`) liga o `KeepsBody` em alguem, e o
		// laco de estado do DM trata os dois casos LADO A LADO:
		//
		//     if(dead) if(Planet && Planet!="Afterlife" && ...)
		//         if(KeepsBody)
		//             if(Ki <= (MaxKi/6))                       // so ai comeca a puxada
		//                 if(!returning) ... "Your spirit is waving..."
		//                 if(prob(5)) ... loc=locate(187,104,6)
		//         else if(!KeepsBody)
		//             ... "[src] cannot exist outside of the Afterlife."   // arranca na hora
		//
		// Ou seja: o morto comum e puxado, o morto com corpo FICA -- e so e chamado de volta quando a
		// energia dele cai a um sexto do maximo. E o poder inteiro do verb; sem esta metade o
		// `Keep_Body` seria um bit que ninguem nunca sentiria.
		//
		// O RELOGIO E REARMADO E NAO DESLIGADO, e a diferenca importa: `RelogioDaMorte = long.MaxValue`
		// (o que a triagem faz com corpo sem dono) mataria a pergunta pra sempre, e a condicao do DM e
		// **continua** -- ela precisa ser refeita enquanto o Ki cai. Rearmando pelo mesmo
		// `Alem.MsNoChao`, a triagem volta aqui a cada 15 s e reavalia, sem laco proprio e sem campo
		// novo. O `prob(5)` por tique do original vira "vence o prazo com o Ki ja embaixo", que e a
		// mesma coisa vista de longe: uma demora curta e aleatoria depois de o gatilho armar.
		//
		// ---- A DIVERGENCIA, E ELA E DO MODELO DE MORTE DESTE PORT E NAO DESTE VERB ----
		// **NO DM O MORTO COM CORPO ANDA. AQUI ELE NAO ANDA** -- e nao anda porque NENHUM morto anda
		// neste port: `PodeMexerOCorpo` (`GameServer.Ia.cs:360`) recusa por `Ficha.dead`, o que vale
		// inclusive pro morto que esta no Outro Mundo. `Alem.MortoDePe` decide so a POSE.
		//
		// Nao mexi nisso aqui de proposito: soltar o passo do morto e uma mudanca no funil de vetor
		// (jogador e IA de uma vez, cinco recusas compartilhadas) e ela nao pertence a um verb de
		// cargo -- e o tipo de coisa que se faz sozinha, com bancada propria, e nao de carona.
		//
		// O QUE O VERB ENTREGA MESMO ASSIM, e nao e pouco: o corpo **fica onde caiu**, no mundo dos
		// vivos, em vez de sumir pro Outro Mundo em 15 s. E isso muda uma coisa concreta e ligada
		// nesta mesma sessao: o `Revive` (racial e de cargo) so alcanca um morto ADJACENTE, e um morto
		// que ja viajou esta fora do alcance de qualquer um. `Keep_Body` e o que da aos amigos a
		// janela pra trazer a pessoa de volta.
		//
		// `MorteJaViajou` NAO e a viagem, e por isso ele PODE ser escrito aqui: ele significa "esta
		// morte ja passou da etapa de cadaver", que e o que a aureola pergunta. E exatamente o caso
		// que o `Alem.TemAureola` previu por escrito ao escolher o crivo temporal em vez do por lugar.
		// =================================================================================================================
		if (pl.Ficha.KeepsBody && pl.Ficha.Ki > pl.Ficha.MaxKi / 6)
		{
			// A AUREOLA ACENDE AQUI, e este e o unico lugar do jogo em que ela acende sem viagem: o
			// morto que anda entre os vivos e denunciado por ela, e o proprio DM faz piada com isso na
			// descricao da skill vizinha (*"Or, you could, y'know, look at their goddamn Halo."*,
			// `OtherworldRankSkills.dm:45`). Sem esta linha o `Keep_Body` seria invisibilidade de morto.
			if (!pl.MorteJaViajou)
			{
				pl.MorteJaViajou = true;
				Avisar(pl, "o chão não te leva: o seu corpo continua seu, e fica onde caiu. Uma auréola "
						 + "se acende sobre a sua cabeça. Enquanto houver energia, o Outro Mundo espera "
						 + "-- e quem chegar até você ainda pode te trazer de volta.");
				GD.Print($"[server] {pl.Name} morreu e FICOU (KeepsBody)");
			}
			pl.RelogioDaMorte = NowMs() + Alem.MsNoChao;
			return;
		}

		// O Ki caiu abaixo de um sexto (ou nunca houve `KeepsBody`): a viagem acontece.
		if (pl.Ficha.KeepsBody)
			Avisar(pl, "seu espírito treme -- o seu tempo no Mundo Material chegou ao fim.");

		IrProAlem(pl);
	}

	/// <summary>
	/// ============================ A VIAGEM -- `loc = locate(187,104,6)` ============================
	/// `Death.dm:110-111`, os dois ultimos passos do original: o corpo aparece na mesa do Enma e
	/// **chega curado inteiro** (`SpreadHeal(100,1,1)`).
	///
	/// ============================ E ELE CHEGA DE PE, COM OS MEMBROS DE VOLTA ============================
	/// O DM levanta o morto ANTES de mover -- `RegrowLimb` em cada membro decepado e `spawn Un_KO()`
	/// (`:86-89`). E `Corpo.Restaurar()` faz exatamente as duas coisas numa chamada (o mesmo metodo
	/// que o `Reviver` usa), sem tocar em `dead`: a pessoa continua morta, so nao esta mais em
	/// pedacos. Sem isto o jogador chegaria ao Outro Mundo sem os bracos que perdeu na luta, e a
	/// primeira coisa que ele veria do alem seria um defeito.
	///
	/// A POSE se resolve sozinha: <see cref="ServerPlayer.Pose"/> pergunta ao `Alem.MortoDePe`, e a
	/// resposta muda no instante em que a zona muda. Nao ha nada a escrever.
	/// ================================================================================================
	///
	/// ============================ O QUE O `MoveToZone` JA FAZ, E NAO PRECISOU DE LINHA ============================
	/// Soltar embate de Ki, soltar o raio, limpar os projeteis do dono, avisar quem ficou
	/// (`PeerLeft` -- sem ele o corpo fantasma ficaria de pe no lugar da morte), zerar o orcamento de
	/// passo, derrubar o voo e a altitude, remandar cenario, obras, portas, aparencia e feridas.
	/// Tudo isso e o caminho de troca de planeta que ja existia; a morte so o usa.
	/// ========================================================================================================
	/// </summary>
	private void IrProAlem(ServerPlayer pl)
	{
		// ============================ A TRANCA DA SALA DO TEMPO ABRE AQUI, E NAO NO RENASCER ============================
		// **Decisao do dono**: depois de preso, *"so morrendo pra sair"*. Isto estava no `Renascer` --
		// certo enquanto morrer e renascer eram o mesmo instante. Nao sao mais: com a viagem no meio,
		// deixar a limpeza pro fim manteria o preso MARCADO COMO PRESO durante o minuto inteiro no
		// Outro Mundo, e a `SalaSessao` continuaria contando o tempo dele numa sala de outro planeta.
		//
		// O bit vai pro disco, entao ele tem que morrer no primeiro passo que tira o corpo da sala.
		// Ver `GameServer.SalaDoTempo.AMorteSaiDaSala`.
		// ==========================================================================================================
		AMorteSaiDaSala(pl);

		// ============================ AGORA SIM A AUREOLA -- `overlayList += 'Halo.dmi'` (`Death.dm:106-108`) ============================
		// **UMA LINHA ANTES DO `MoveToZone`, E A ORDEM E O CONSERTO INTEIRO.** Ela e o passo 10 do
		// `Death()`, imediatamente antes do `loc =` do passo 11 -- e o cadaver que ficou pra tras foi
		// fotografado la no passo 5 (`GenerateCorpse()`, `:64-67`), sem ela. Era esse o bug do dono:
		// *"o corpo q fica no MAPA DOS VIVOS deveria ser o EXATO CORPO DELE QUANDO MORRE, sem a
		// aureola"*.
		//
		// ANTES E NAO DEPOIS por causa do que o `MoveToZone` faz por dentro: e ele que chama o
		// `TrocarAureolas` (via `TrocarAparencias`) e apresenta este corpo a zona de destino. Escrever
		// o bit depois mandaria pro Outro Mundo a foto ERRADA (sem aureola) e so o tique de 5 Hz
		// corrigiria, 0 a 200 ms depois -- um piscar na chegada, e a mesma familia de "o pacote existe,
		// sai uma vez, e quem nao estava presente naquele instante nunca soube".
		// ========================================================================================================
		pl.MorteJaViajou = true;

		// ============================ E O CORPO FICA -- `GenerateCorpse()` (`Death.dm:66`) ============================
		// **A UNICA LINHA NOVA QUE O CADAVER CUSTOU AO CAMINHO DA MORTE.** O DM ergue o `/obj/mobCorpse`
		// no passo 5 e viaja no 11, dentro do mesmo `Death()`: dois objetos, e quem viaja e o MOB. Aqui
		// e igual, so que os dois passos estao a 15 s de distancia em vez de 2 -- por isso o corpo e
		// deixado AGORA, no instante da partida, e nao no da morte: quinze segundos de dois corpos
		// empilhados no mesmo ponto seriam duas caixas de colisao, dois alvos de soco e o nome do morto
		// aparecendo duas vezes na zona. Ver o cabecalho de `GameServer.Cadaver.cs`.
		//
		// ANTES DO `MoveToZone`, e a ordem tem a mesma razao da linha acima: `DeixarOCadaver` manda a
		// aparencia do corpo novo pelo canal confiavel, e `MoveToZone` manda o `PeerLeft` de quem
		// partiu. Nesta ordem a troca nao pisca -- o cadaver ja esta desenhado quando o corpo some.
		//
		// E ELE NASCE SEM AUREOLA sem precisar de uma linha pra isso: a ficha dele e NOVA (`dead` sim,
		// `MorteJaViajou` nao), entao `Alem.TemAureola` responde falso. E exatamente o que o DM faz por
		// ordem -- o cadaver e fotografado antes de o `overlayList += 'Halo.dmi'` existir --, e e o bug
		// que o dono relatou, agora fechado dos dois lados.
		// ==========================================================================================================
		DeixarOCadaver(pl);

		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		MoveToZone(pl.Id, alem, MesaDoEnma(alem));

		// ============================ E O RELOGIO REARMA AQUI -- A ETAPA SEGUINTE TEM PRAZO ============================
		// **ESTA LINHA FALTAVA, E A AUSENCIA APAGAVA O PEDIDO DO DONO INTEIRO.** O `AMorteAconteceu`
		// arma o relogio pros 15 s de cadaver; quando ele vence, a triagem chama esta viagem -- e o
		// campo continuava com o vencimento **ja passado**. No tique seguinte (33 ms depois) a mesma
		// pergunta dava verdadeiro de novo, o `PassoDaMorte` via `EhOAlem(pl.Zone)` e chamava
		// `Renascer`. Ou seja: o jogador via o Outro Mundo por **um quadro**.
		//
		// Nada reclamava. `Alem.MsNoAlem` estava escrita, documentada em vinte linhas e lida por um
		// unico consumidor -- a MENSAGEM logo abaixo, que anunciava "60 s ate voltar a vida" enquanto
		// a volta acontecia no quadro seguinte. **Escrever a constante nao e aplicar a constante**, e
		// e o mesmo defeito que este port ja registrou no corte de sigilo do BP.
		//
		// O CAMPO E UM SO PORQUE A ETAPA E DERIVADA DO LUGAR (ver `ServerPlayer.RelogioDaMorte`): quem
		// esta no alem esta na segunda etapa, e por isso o mesmo `RelogioDaMorte` marca as duas.
		// ========================================================================================================
		pl.RelogioDaMorte = NowMs() + Alem.MsNoAlem;

		// `SpreadHeal(100,1,1)` + `RegrowLimb` -- ver o cabecalho. NAO e `Reviver`: `dead` fica.
		pl.Combate.Corpo.Restaurar();
		pl.Combate.SincronizarVida();
		AjustarGanhoDoRabo(pl);   // o rabo voltou: o ritmo de treino do Saiyajin volta com ele

		Avisar(pl, "o chão some sob você. Você abre os olhos no Outro Mundo, inteiro -- e morto."
				 + " Uma auréola se acende sobre a sua cabeça.");
		GD.Print($"[server] {pl.Name} MORREU e foi pro Outro Mundo"
				 + $" ({Alem.MsNoAlem / 1000:0}s ate voltar a vida)");
	}

	/// <summary>
	/// A MESA DO ENMA, ja conferida contra a colisao da zona.
	///
	/// A coordenada vem do DM (<see cref="Alem.MesaDoEnma"/>); o `PontoLivrePerto` e a mesma defesa
	/// que o <see cref="PontoDeNascimento"/> usa -- um mapa convertido tem parede onde o `.dmm`
	/// tinha, e uma construcao levantada em cima do ponto o bloqueia em runtime. Cair dentro de uma
	/// parede no Outro Mundo seria a segunda maneira de prender alguem.
	///
	/// A ALTURA SAI DO MAPA e nao de um 500 cravado: e ela que inverte o eixo Y do BYOND, e um
	/// numero cravado aqui mentiria calado no dia em que o z6 fosse reconvertido com outro tamanho.
	/// </summary>
	private Vec2 MesaDoEnma(ZoneKey alem)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(alem);
		Vec2 mesa = Alem.MesaDoEnma(mapa?.Height ?? 500);
		return mapa?.PontoLivrePerto(mesa) ?? mesa;
	}

	// =====================================================================
	// 4. A AUREOLA NO FIO
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE A AUREOLA NAO CABE NO SNAPSHOT ============================
	/// Os dois bytes de flags do <see cref="Protocol.EntityState"/> estao CHEIOS -- o primeiro com
	/// direcao (2 bits), pose (3) e mais tres bits, o segundo com os oito de carga/deitado/voo/redeas/
	/// nave. Um terceiro byte custaria um byte por corpo por tique (com 151 habitantes, ~4,5 KB/s)
	/// pra carregar UM bit que muda duas vezes por vida.
	///
	/// Entao ela vai pelo canal do estado LENTO, que este port ja tem em duas cores: `S2C.Feridas` e
	/// `S2C.Forma`. Reliable, so quando MUDA, e reenviado a quem entra na zona. Custo por tique: zero.
	///
	/// ============================ E ELA E DETECTADA POR DIFERENCA, E NAO POR CHAMADA ============================
	/// A tentacao era mandar o pacote de dentro do <see cref="AMorteAconteceu"/> e do
	/// <see cref="Renascer"/>. Seriam dois lugares -- e `Ficha.dead` e escrito DIRETO em pelo menos
	/// quatro outros que nao passam por nenhum dos dois: o restauro da mente (`RestaurarDaMente`), a
	/// volta no tempo (`GameServer.Tecnicas.G4`), o reflexo que nasce vivo (`GameServer.Clone.cs:143`)
	/// e a gestacao do bio-androide (`GameServer.Tech.cs:868`).
	///
	/// Comparar contra o ultimo valor enviado cobre os seis de uma vez, e cobre o setimo que nascer
	/// amanha. E o argumento inteiro do `TickDasFeridas`, do lado do qual isto roda.
	/// ========================================================================================================
	/// </summary>
	private void TickDasAureolas()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			bool agora = Alem.TemAureola(pl.Ficha.dead, pl.MorteJaViajou);
			if (agora == pl.EnvAureola) continue;
			pl.EnvAureola = agora;
			MandarAureola(pl);
		}
	}

	/// <summary>Manda a aureola de um corpo pra todo mundo que esta na zona dele (o dono incluso).</summary>
	private void MandarAureola(ServerPlayer de)
	{
		NetDataWriter w = PacoteDeAureola(de);
		foreach (ServerPlayer o in ZoneList(de.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	private static NetDataWriter PacoteDeAureola(ServerPlayer de)
	{
		var w = Protocol.Begin(Protocol.S2C.Aureola);
		w.Put(de.Id);
		w.Put(Alem.TemAureola(de.Ficha.dead, de.MorteJaViajou));
		return w;
	}

	/// <summary>
	/// APRESENTA AS AUREOLAS a quem chega numa zona, e as de quem ja estava a ele.
	///
	/// Mesma familia de defeito das construcoes, das portas, das feridas e das formas: o pacote
	/// existe, sai UMA vez, e quem nao estava presente naquele instante nunca soube. Sem isto, quem
	/// entrasse no Outro Mundo veria os mortos de la sem aureola nenhuma -- e o Outro Mundo e
	/// justamente onde todo mundo tem uma.
	/// </summary>
	private void TrocarAureolas(ServerPlayer novo)
	{
		novo.EnvAureola = Alem.TemAureola(novo.Ficha.dead, novo.MorteJaViajou);

		List<ServerPlayer> zona = ZoneList(novo.Zone.Hash);
		NetDataWriter minha = PacoteDeAureola(novo);

		foreach (ServerPlayer outro in zona)
		{
			outro.Peer?.Send(minha, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			if (outro != novo)
				novo.Peer?.Send(PacoteDeAureola(outro), Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}
	}
}
