using Godot;
using Jandirus.Core.Tech;
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
///     morre --[ Alem.MsNoChao ]--> Outro Mundo --[ PAGA: esferas / tecnica / Enma ]--> vivo, no berco
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
/// A SEGUNDA NAO TEM PRAZO. Ela ja foi um andaime de 60 s (`MsNoAlem`, hoje so na historia de
/// `Alem.cs`), da epoca em que nenhuma volta do original existia; o dono pediu o oposto
/// (2026-09-04): o morto fica no Outro Mundo ate alguem pagar a volta -- as esferas, a tecnica de
/// reviver de um vivo, ou o Enma Daioh (secao 5, 1.000.000 de zeni). Ver `Alem.TipoDoEnma`.
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
		// Ou seja: quando o prazo do cadaver vencer, `pl.Ficha.dead` ja e falso ha 2 s e o
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
		// QUEM JA ESTA NO OUTRO MUNDO NAO TEM PRAZO: nao ha volta automatica (ver `Alem.TipoDoEnma`).
		// O relogio chega aqui zerado por um relog antigo, por bancada, ou por uma morte DENTRO do alem
		// (um vivo que visitou e caiu la); ele e trancado e pronto. E a etapa de cadaver acabou: a
		// viagem de quem ja esta no alem e a de quem ja chegou, entao `MorteJaViajou` acende aqui --
		// sem isto o morto do alem ficaria DE PE (a pose e por lugar, `Alem.MortoDePe`) e SEM auréola
		// (a auréola e por etapa, `Alem.TemAureola`), que e o par que nunca deve existir junto.
		if (Alem.EhOAlem(pl.Zone))
		{
			if (!pl.MorteJaViajou)
			{
				pl.MorteJaViajou = true;
				GD.Print($"[server] {pl.Name} morreu DENTRO do Outro Mundo: de pe, com auréola, sem prazo");
			}
			pl.RelogioDaMorte = long.MaxValue;
			return;
		}

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
		// `Alem.MsNoChao`, a triagem volta aqui a cada `MsNoChao` (2 s) e reavalia, sem laco proprio e sem campo
		// novo. O `prob(5)` por tique do original vira "vence o prazo com o Ki ja embaixo", que e a
		// mesma coisa vista de longe: uma demora curta e aleatoria depois de o gatilho armar.
		//
		// ---- A DIVERGENCIA, E ELA E DO MODELO DE MORTE DESTE PORT E NAO DESTE VERB ----
		// **NO DM O MORTO COM CORPO ANDA. AQUI ELE NAO ANDA.** Desde 2026-09-04 o morto do OUTRO
		// MUNDO anda (`PodeMexerOCorpo` le `MortoDePe`, a pedido do dono: *"o personagem morto fica
		// preso, nao conseguindo andar"*), mas `MortoDePe` e por LUGAR, e o morto do `Keep_Body`
		// esta no mundo dos vivos -- pra ele a recusa por `Ficha.dead` continua. Soltar ESSE passo
		// e outra pergunta (o morto de pe entre os vivos, com a auréola o denunciando), com bancada
		// propria, e nao de carona num verb de cargo.
		//
		// O QUE O VERB ENTREGA MESMO ASSIM, e nao e pouco: o corpo **fica onde caiu**, no mundo dos
		// vivos, em vez de sumir pro Outro Mundo em 2 s. E isso muda uma coisa concreta e ligada
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
		// e igual, e os dois passos estao a 2 s de distancia, como la (`MsNoChao`) -- por isso o corpo e
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

		// ============================ A CHEGADA CURA -- `Death.dm:86-88` + `:111` ============================
		// `RegrowLimb` em cada membro decepado e `SpreadHeal(100,1,1)` na linha do `loc =`: o morto acorda
		// INTEIRO no Outro Mundo. (Esta linha saiu por um dia, lida como reclamacao; o dono esclareceu em
		// 2026-09-04: *"ao morrer voce acorda no outro mundo 100% curado de tudo, porem caso fique ferido
		// no outro mundo por lutas la, voce regenera normal fora de combate"*.) O cadaver que ficou,
		// logo acima, e uma COPIA: ele guarda as feridas e o angulo da queda -- e por isso a cura vem
		// DEPOIS do `DeixarOCadaver`, e nao antes.
		// ==================================================================================================
		if (pl.Combate is { } cura) { cura.Corpo.Restaurar(); cura.SincronizarVida(); MandarFeridas(pl); }

		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		MoveToZone(pl.Id, alem, MesaDoEnma(alem));

		// ============================ SEM PRAZO: A ETAPA SEGUINTE NAO TEM VOLTA AUTOMATICA ============================
		// Aqui se armava `NowMs() + Alem.MsNoAlem` (60 s) e o `PassoDaMorte` chamava `Renascer` quando
		// vencia -- o andaime da epoca em que nenhuma das voltas do DM existia. O dono pediu o contrario
		// (2026-09-04): *"voce teria que ficar morto ate alguem te reviver com as esferas, ou juntar 1
		// milhao de zeni e pagar o Enma Daioh"*. O relogio vai pro TETO e o tique nunca mais o examina;
		// a saida e paga: as esferas, a tecnica de reviver de um vivo ao lado, ou o Enma (secao 5).
		//
		// (A licao que o bloco anterior guardava continua valendo: um relogio que fica com o vencimento
		// JA PASSADO faz a mesma pergunta dar verdadeiro no tique seguinte. E por isso que e `MaxValue`
		// e nao "deixa como esta".)
		// ==================================================================================================================
		pl.RelogioDaMorte = long.MaxValue;


		Avisar(pl, "o chão some sob você. Você abre os olhos no Outro Mundo, inteiro -- e morto."
				 + " Uma auréola se acende sobre a sua cabeça.");

		// ============================ E SE O SEU MUNDO ACABOU, A ESCOLHA FICA ABERTA ============================
		// **A pergunta do refugio vale pra ESTA morte a partir daqui.** O `Renascer` roda quando alguem
		// paga a volta (esferas, tecnica, Enma) e nao pode esperar por clique nenhum (a decisao de
		// arquitetura esta escrita em `GameServer.Conquista.cs`: nada bloqueia o tique); como nao ha
		// mais prazo no Outro Mundo, o jogador tem ate a volta pra dizer para onde quer voltar.
		//
		// Quem nao responde volta pelo padrao -- a vizinhanca de casa --, e continua podendo escolher
		// depois: a resposta e uma preferencia (`Dominio.EhOSpawn`), e nao um voto de uma vez so.
		// Ver `GameServer.Refugio.OferecerORefugio`; com o berco de pe, ela nao faz nada.
		// ============================================================================================================
		OferecerORefugio(pl, podeAbrir: true);
		GD.Print($"[server] {pl.Name} MORREU e foi pro Outro Mundo"
				 + " (fica la ate alguem pagar a volta)");
	}

	/// <summary>
	/// O FANTASMA CAIU DE VEZ NO OUTRO MUNDO -- o `else if(dead)` do `Death()` (`Death.dm:113-118`):
	/// `move=1`, `loc=locate(187,104,6)`, `SpreadHeal(100,1,1)`, `KO(20)`. Nao e uma morte: `Morrer`
	/// devolveu falso, `AoMorrer` nao disparou, nada e contado nem cobrado. Ele e curado, posto de volta
	/// na mesa do Enma e fica em coma por <see cref="Alem.SegundosDeComaDoMortoDeNovo"/>; o `Levantar`
	/// do tique o ergue sozinho. Chega aqui pelo gancho `CombatState.AoMorrerDeNovo`.
	/// </summary>
	private void MorrerDeNovoNoAlem(ServerPlayer pl)
	{
		if (!pl.MortoDePe || pl.Combate is not { } c) return;
		c.Corpo.Restaurar();
		c.SincronizarVida();
		MandarFeridas(pl);
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		MoveToZone(pl.Id, alem, MesaDoEnma(alem));
		c.Nocautear(Alem.SegundosDeComaDoMortoDeNovo);
		Avisar(pl, "você desaba -- e acorda de novo na mesa do Enma, inteiro. Um morto não morre duas vezes.");
		GD.Print($"[server] {pl.Name} caiu de vez no Outro Mundo: curado, de volta a mesa do Enma, em coma por {Alem.SegundosDeComaDoMortoDeNovo:0.#} s");
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

	// =====================================================================
	// 5. O ENMA DAIOH -- a saida paga
	// =====================================================================
	/// <summary>
	/// SEMEIA O ENMA na cadeira dele, no boot, como obra FIXA do mapa do Outro Mundo. No DM ele e um
	/// mob de conversa posto por `Build_Sky_NPCs()` (`SkyNPCs.dm:55-59`); aqui e uma `Obra` `DoMapa`
	/// com menu E (`Interacoes.De("Enma_Daioh")`), densa como o original (`density = 1`), e com a
	/// armadura no infinito -- um arremesso que passe por cima da mesa nao derruba o juiz dos mortos.
	/// A entrada do catalogo entra junto e nao vem do disco: o `tech.json` e extraido das construcoes
	/// do DM, e o Enma nao e uma. O sprite e o `Enma.dmi` do DM (96x96), convertido pelo pipeline.
	/// </summary>
	private void SemearOEnma()
	{
		if (_obras == null) return;
		if (_obras.Get(Alem.TipoDoEnma) == null)
			_obras.Acrescentar(new Construcao
			{
				Id = Alem.TipoDoEnma,
				Nome = "Enma Daioh",
				Desc = "O juiz dos mortos. Lê a sua ficha e cobra a volta.",
				Custo = -1,
				Arte = "res://Assets/Sprites/NPCs/Enma.tres",
				Estado = "default",
				Densa = true,
				// O icone tem 96 px de largura sobre um tile de 32: o DM centraliza com `pixel_x = -32`
				// (`SkyNPCs.dm:36`); o desenho daqui ancora a obra na celula e desloca pelo mesmo `Pixel`.
				PixelX = -32,
				PixelY = 0,
			});

		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		if (_noChao.Any(o => o.Tipo == Alem.TipoDoEnma && o.Zona.Equals(alem))) return;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(alem);
		Vec2 cadeira = Alem.CadeiraDoEnma(mapa?.Height ?? 500);
		var enma = new Obra
		{
			Id = _proximaObraId++,
			Tipo = Alem.TipoDoEnma,
			X = cadeira.X,
			Y = cadeira.Y - MoveRules.FeetOffsetY,
			DonoNome = "",
			Aparafusada = true,
			DoMapa = true,
			ErguidaEm = 0,
			Armadura = double.PositiveInfinity,
			ArmaduraMax = double.PositiveInfinity,
		};
		enma.PorZona(alem);
		_noChao.Add(enma);
		AplicarColisaoDasObras(alem);
		GD.Print($"[server] o Enma Daioh sentou na mesa dele em {Alem.ZonaDoOutroMundo} ({cadeira.X:0},{cadeira.Y:0})");
	}

	/// <summary>O `enma_interact()` do DM (`SkyNPCs.dm:150-173`), so a fala: o vivo e enxotado; o morto ouve o preco.</summary>
	private void EnmaOuvir(ServerPlayer pl)
	{
		if (ObraQueAceita(pl, "enma_ouvir") == null) { Avisar(pl, "o Enma não está ao alcance -- fale com ele na mesa dele."); return; }
		if (!pl.Ficha.dead)
		{
			Avisar(pl, "Enma Daioh troveja: \"Os vivos não têm negócio na minha mesa! Fora daqui até chegar a sua hora!\"");
			return;
		}
		if (pl.Ficha.aged_out)
		{
			Avisar(pl, "Enma Daioh: \"Sua ampulheta virou pela última vez -- morte de velhice não tem volta.\"");
			return;
		}
		Avisar(pl, $"Enma Daioh lê a sua ficha: \"Uma alma equilibrada. Pode descansar no Outro Mundo -- ou voltar, por "
				 + $"{Alem.PrecoDoReviveDoEnma:N0} zeni. Você tem {pl.Ficha.Zeni:N0}.\"");
	}

	/// <summary>
	/// O `enma_zeni_revive()` do DM (`SkyNPCs.dm:185-197`): 1.000.000 de zeni, o BP expresso em 25% por
	/// uma hora (`zeni_revive_debuff_until`, lido em `Fighter.Power`), e a volta pro berco INTEIRO --
	/// vida, membros, Ki e folego, que e o que `Revive()` faz la (`Death.dm:163-173`) e o `Renascer`
	/// faz aqui.
	///
	/// NAO PORTADO (ainda): o dobro do preco pra quem foi apagado por Hakai (`hakai_mark`, campo que
	/// nao existe aqui), a reencarnacao a 10% e o julgamento pro Inferno por karma negativo.
	/// </summary>
	private void EnmaReviverPorZeni(ServerPlayer pl)
	{
		if (ObraQueAceita(pl, "enma_reviver") == null) { Avisar(pl, "o Enma não está ao alcance -- fale com ele na mesa dele."); return; }
		if (!pl.Ficha.dead) { Avisar(pl, "Enma Daioh troveja: \"Os vivos não têm negócio na minha mesa!\""); return; }
		if (pl.Ficha.aged_out)
		{
			Avisar(pl, "Enma Daioh: \"Nem todo o zeni do universo compra mais um dia para quem já viveu todos os seus.\"");
			return;
		}
		double custo = EnmaNaoCobraDeTeste ? 0 : Alem.PrecoDoReviveDoEnma;
		if (pl.Ficha.Zeni < custo)
		{
			Avisar(pl, $"Enma Daioh: \"Você não tem como pagar a viagem de volta! Volte quando tiver {custo:N0} zeni.\"");
			return;
		}
		pl.Ficha.Zeni -= custo;
		pl.Ficha.zeni_revive_debuff_until = NowMs() + Alem.MsDoDebuffDoEnma;
		Avisar(pl, "Enma Daioh carimba a sua ficha: \"De volta à terra dos vivos!\"");
		Avisar(pl, "seu corpo volta frágil da viagem: o seu poder fica em 25% por uma hora.");
		Renascer(pl);
		MandarFicha(pl);
		Persistir(pl);
		GD.Print($"[server] {pl.Name} pagou o Enma ({custo:N0} zeni) e voltou a vida");
	}

	/// <summary>DEFEITO INJETADO (`--alemteste`): o Enma de graca -- a prova "sem o milhao ele recusa" tem que ficar vermelha.</summary>
	internal bool EnmaNaoCobraDeTeste;
}
