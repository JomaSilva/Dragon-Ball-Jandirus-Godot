using Jandirus.Core.Combat;
using Jandirus.Core.Forms;

namespace Jandirus.Server;

/// <summary>
/// ============================ O QUE O MEMBRO PERDIDO COBRA, DO LADO DO SERVIDOR ============================
/// As regras e os numeros moram no Core (<see cref="DebuffsDeMembro"/>, uma funcao por pergunta, cada uma com a
/// linha do DM). Aqui mora o que so o servidor sabe: QUEM esta dirigindo o corpo, se ele esta NO AR, de que
/// RACA ele e -- e a frase que sai no chat no instante em que o membro cai.
///
/// ============================ QUEM PAGA O QUE -- e a divisao e a do original ============================
///   * A PONTARIA sem braco vale pra TODO corpo: a linha mora no `hitProc` (`CombatMovement.dm:189`), que
///     jogador e NPC dividem. No port ela e uma linha do `MeleeResolver.Resolver` e nao passa por este arquivo.
///   * A GUARDA SEM MEMBRO PRA APARAR tambem vale pra TODO corpo: o `holdblock()` e o verb que o ALT do jogador
///     e a IA chamam (`CombatMovement.dm:73-82`, `NPCAI.dm:191`), e aqui os dois passam pelo `ErguerAGuarda`.
///   * AS OUTRAS QUATRO (a recarga sem os dois bracos, a corrida sem perna, o chao sem as duas, o passo do
///     ferido) moram no `unitimer()` do DM (`movement handler.dm:34-294`), e o `unitimer` e o laco do JOGADOR:
///     `while(src &amp;&amp; key &amp;&amp; !LoggingOut)`. O NPC tem relogio proprio (`NPCAI.dm:855-937`), e la nao ha
///     `legsLost`, nem `armsLost` (a recarga dele e `attacking-1` cru, `:892`), nem `HP/100`.
///
/// Por isso as quatro perguntam <see cref="DirigidoPeloDono"/>. **E e isso que impede a IA de travar**: um NPC
/// sem as duas pernas viraria estatua (a IA nao sabe que precisa levantar voo pra sair do lugar), e um chefe de
/// saga ferido passaria a luta inteira se arrastando. No DM nenhum dos dois acontece, e aqui tambem nao.
/// ========================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// QUEM MANDA NESTE CORPO E O DONO DELE? `Cerebro` nulo e a definicao da casa (ver `ServerPlayer.Cerebro`:
	/// "QUEM DIRIGE ESTE CORPO AGORA. Nulo = o dono"). O NPC, o clone da mente e o corpo POSSUIDO (a fera do
	/// Oozaru, a furia lendaria) tem cerebro -- e saem das quatro regras do `unitimer` enquanto ele estiver la.
	/// </summary>
	private static bool DirigidoPeloDono(ServerPlayer pl) => pl.Cerebro == null;

	/// <summary>
	/// SEM UMA PERNA NAO HA CORRIDA -- `speedy.dm:36-38` e `:54`, `movement handler.dm:50`. Lido pelo `AplicarInput`
	/// logo antes do `PodeCorrer` (que e quem concede e COBRA a corrida no chao): recusada aqui, ela nem e cobrada.
	/// No ar o SHIFT e o superflight e nao passa por esta pergunta -- o DM so corta o dash (`:52-53` nao leem pernas).
	///
	/// AO LEME NAO VALE: a nave nao corre com as pernas do piloto (`EstaPilotando`, por ultimo -- so e perguntado a
	/// quem ja perdeu uma perna).
	/// </summary>
	private bool SemPernaPraCorrer(ServerPlayer pl) =>
		!DebuffsDeMembro.DesligadosDeTeste && DirigidoPeloDono(pl) && pl.Combate is { } c
		&& !DebuffsDeMembro.Corre(DebuffsDeMembro.De(c.Corpo)) && !EstaPilotando(pl.Id);

	/// <summary>
	/// SEM AS DUAS PERNAS, NO CHAO, O CORPO NAO SAI DO LUGAR -- `if(legsLost >= 2 &amp;&amp; !flight) mobTime = 0`
	/// (`movement handler.dm:133`), a linha imediatamente abaixo do `gravParalysis` que ja mora no
	/// `PodeMexerOCorpo`. Entra pelo MESMO funil de vetor, e por isso vale de uma vez pro passo, pro arranque do
	/// golpe e pra corrida das tecnicas: quem nao sai do lugar nao investe (o soco em si continua saindo).
	///
	/// "NO AR" E A ALTURA, e nao o bit `Voando`: o cliente so recebe a altura (`LocalPlayer._altitude`), e as
	/// duas pontas tem que decidir pelo MESMO numero -- senao, durante o pouso (voo desligado, corpo ainda
	/// descendo), uma andaria e a outra recusaria, que e o corpo tremendo. Quem liga o voo sobe sozinho ate a
	/// altura de pairar no tique seguinte, entao "levantar voo" continua sendo a saida, como no DM.
	///
	/// DIVERGENCIA DECLARADA: no DM o `mobTime = 0` tambem zera o `hasTime` e, por ele, o `canfight`
	/// (`movement handler.dm:157-163`) -- la, sem as duas pernas e no chao, o corpo tambem nao soca nem atira.
	/// Aqui a familia inteira do funil (prensado pela gravidade, paralisado, com raio na mao) foi portada como
	/// trava de PASSO: "continua socando, so nao sai do lugar". Esta linha segue a familia.
	///
	/// E QUEM ESTA PRESO NAO GIRA PELO TECLADO (o `AplicarInput` recusa o pacote inteiro, olhar junto com passo),
	/// outra regra da familia inteira -- no DM o corpo sem pernas ainda vira no lugar (`:198-200`). O golpe e o
	/// tiro viram sozinhos pro alvo MARCADO, que e por onde o preso mira.
	///
	/// AO LEME NAO VALE, pelo mesmo motivo da corrida.
	/// </summary>
	private bool SemPernasNoChao(ServerPlayer pl) =>
		!DebuffsDeMembro.DesligadosDeTeste && DirigidoPeloDono(pl) && pl.Altitude <= 0f && pl.Combate is { } c
		&& !DebuffsDeMembro.AndaNoChao(DebuffsDeMembro.De(c.Corpo)) && !EstaPilotando(pl.Id);

	/// <summary>
	/// O RITMO DA RECARGA DO SOCO -- `attacking - (armsLost>=2 ? 0.5 : 1)` (`movement handler.dm:167`). E a
	/// resposta do gancho `CombatState.RitmoDaRecarga` (instalado no `PrepararCombate`) e o divisor do `SocoMs`
	/// que vai na ficha: o cliente espera o mesmo tempo que o servidor cobra.
	/// </summary>
	private static double RitmoDaRecargaDe(ServerPlayer pl) =>
		DebuffsDeMembro.DesligadosDeTeste || !DirigidoPeloDono(pl) || pl.Combate is not { } c
			? 1
			: DebuffsDeMembro.RitmoDaRecarga(DebuffsDeMembro.De(c.Corpo));

	/// <summary>
	/// QUANTO DO PASSO SOBRA A ESTE CORPO FERIDO -- `if(HP>0&amp;&amp;!undelayed) mobTime*=HP/100` (`movement
	/// handler.dm:55`). Entra no `RecalcularVelocidade`, o unico lugar que escreve `SpeedStat` por tique, e dali
	/// segue pelos dois trilhos que precisam concordar (a ficha que o cliente obedece e o `ValidateStep`).
	/// </summary>
	private static double FatorDeVidaNoPasso(ServerPlayer pl) =>
		DebuffsDeMembro.DesligadosDeTeste || !DirigidoPeloDono(pl)
			? 1
			: DebuffsDeMembro.FatorDoPasso(pl.Ficha.HP, pl.Race);

	/// <summary>
	/// ERGUER (OU BAIXAR) A GUARDA -- o `holdblock()` do DM, com a recusa dele (`CombatMovement.dm:73-82`):
	/// `if(!block_limbs_ok()) { "Seus membros estao quebrados demais para erguer a guarda!"; return }`. Sem braco
	/// nem perna INTEIRO (os quatro quebrados ou arrancados) nao ha o que erguer.
	///
	/// ============================ UMA PORTA SO, PRO JOGADOR E PRA IA ============================
	/// No DM o `holdblock()` e o verb do ALT (`Hotkeys_Defaults.dm:17`) **e** e o que a IA chama pra aparar
	/// (`NPCAI.dm:191`, `:292`, `:527`): a recusa vale pros dois. Aqui tambem: o `case Protocol.C2S.Guard` do
	/// `Handle` e o passo 5 do `AplicarComando` (`GameServer.Ia.cs`) chamam ESTA funcao, e nenhum dos dois escreve
	/// a guarda no `CombatState` por conta propria. (Na primeira versao so o jogador passava por aqui, e o NPC sem
	/// os quatro membros erguia uma guarda que o jogador nao ergue -- a `--kideponta` reprovou a diferenca.)
	///
	/// A PERGUNTA E A QUE JA EXISTIA: `TemComQueAparar` (`GameServer.Ia.cs`), a mesma que o parry de ki faz e que
	/// o cerebro le pelas `Capacidades` pra nao insistir numa guarda que nao sobe.
	/// ==========================================================================================
	///
	/// O port ja derrubava essa guarda -- mas so no PRIMEIRO GOLPE (`MeleeResolver`: `EscolherGuarda` nulo, e a
	/// guarda cai), calado: ate la o HUD dizia "guarda" de um corpo que nao aparava nada. Recusada aqui, o bit nem
	/// sobe, e a aba Body ("Voce nao ergue a guarda") diz o que o servidor faz.
	///
	/// A FRASE E SO PRO CORPO QUE O DONO DIRIGE, uma por aperto e sem relogio: o cliente manda `C2S.Guard` na BORDA
	/// da tecla (`LocalPlayer`, so quando o estado muda). A IA pede a guarda a cada tique enquanto a quiser (ver o
	/// passo 5 do atuador), e uma frase por pedido encheria o chat do jogador possuido trinta vezes por segundo --
	/// no DM o aviso tambem tem janela propria (`block_warn_raise`, `BlockLimbs.dm:72-75`), e num NPC ele nao tem
	/// quem o leia.
	/// </summary>
	private void ErguerAGuarda(ServerPlayer pl, bool erguer)
	{
		if (erguer && !DebuffsDeMembro.DesligadosDeTeste && !TemComQueAparar(pl))
		{
			if (DirigidoPeloDono(pl)) Avisar(pl, DebuffsDeMembro.AvisoDeGuarda);
			return;
		}
		pl.Combate.Guardar(erguer);
	}

	/// <summary>
	/// SSJ4 SEM RABO SE DESFAZ -- `if(!container.Tail) { "[container]'s tail was lost, reverting them from SSJ4!";
	/// DeBuff() }`, repetido nos tres degraus do laco do buff (`supersaiyanbuff.dm:135-137` o SSJ4, `:148-150` o
	/// Full Power, `:160-162` o Limit Breaker). O Oozaru ja caia sem rabo (`TickDoOozaru`); o SSJ4, que so nasce
	/// dele, continuava de pe com o rabo no chao. Lido pelo `TickDaForma`.
	///
	/// SO O LACO FOI PORTADO. O DM tambem recusa a ENTRADA sem rabo (`if(!Tail) return`, `:807` no Full Power e
	/// `:832` no Limit Breaker); aqui quem tentar entra e cai no tique seguinte, lendo o porque.
	///
	/// "PERDEU O RABO" E TER O MEMBRO E ELE ESTAR ARRANCADO -- e nao "nao tem rabo": um corpo sem o membro `Rabo`
	/// (um chefe de outra raca que o roteiro poe em SSJ4) nunca teve o que perder, e nao pode ser derrubado da
	/// forma a cada tique por uma regra que nao e dele. So as tres formas da escada Saiyajin que vestem o corpo de
	/// SSJ4: as primais do Lendario tem laco proprio no DM (`lssjbuff.dm`), e la nao ha essa linha.
	/// </summary>
	private static bool Ssj4SemRabo(ServerPlayer pl) =>
		!DebuffsDeMembro.DesligadosDeTeste
		&& Catalogo.Def(pl.Forma.Atual) is { Linha: LinhaDeForma.Saiyajin, Corpo: CorpoDeForma.Ssj4 }
		&& pl.Combate?.Corpo.Achar("Rabo") is { Decepado: true };

	/// <summary>
	/// A FRASE DO CHAT NO INSTANTE DA PERDA -- o `to_chat(view(savant), "[savant]'s [src] was torn off!")` do
	/// `LopLimb` (`mobparts_logic.dm:107-109`). Chamado pelo gancho `CombatState.AoDecepar`, ao lado do
	/// `SoltarPecas`: roda pra todo membro que sai de todo corpo, por qualquer funil de dano.
	///
	/// SAO DUAS FRASES, e a diferenca e a regra do sigilo: quem PERDEU le o que passa a valer pra ele (com os
	/// numeros de depois da perda -- o segundo braco nao custa o mesmo que o primeiro); quem ASSISTE le so o
	/// fato, como no DM. So a RAIZ fala: a mao que foi de carona no braco nao ganha frase propria (o DM a
	/// anunciaria com "...is torn off along with it!", `:108`, se o `parentlimb` dele funcionasse).
	/// </summary>
	private void AnunciarMembroPerdido(ServerPlayer d, List<BodyPart> caiu)
	{
		if (caiu.Count == 0 || d.Combate == null) return;
		string raiz = caiu[0].Nome;

		Avisar(d, DebuffsDeMembro.FraseDaPerda(raiz, DebuffsDeMembro.De(d.Combate.Corpo)));
		AvisarPertoG3(d, RaioDaVista, DebuffsDeMembro.FraseDaPerdaAlheia(d.Name, raiz), excetoCentro: true);
	}
}
