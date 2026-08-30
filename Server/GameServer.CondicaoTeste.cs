using Jandirus.Core.Combat;
using Jandirus.Core.Items;

namespace Jandirus.Server;

/// <summary>
/// AS JANELAS DE CONDICAO PRA BANCADA `--diagbancada`.
///
/// ============================ POR QUE ELAS ESCREVEM, E POR QUE SAO POUCAS ============================
/// A definicao que o dono deu da "% de BP efetivo" e uma lista de causas: *"ela so mudaria caso a
/// stamina ou ki caissem, peso, gravidade etc"*. Uma bancada que so saiba baixar o Ki prova UM item
/// dessa lista e chama isso de cobertura -- e as outras pernas ficam sendo exercitadas por ninguem,
/// que e como uma delas (a estamina) chegou ao ponto de estar MORTA na conta do jogo sem que nada
/// acusasse.
///
/// O jogo tem caminho pra tudo isso, e ele e caro em tempo: apanhar de um NPC ate a vida cair a 75%,
/// ficar sem folego, passar fome. Nenhum cabe no orcamento de uma bancada que ja gasta 40 s numa
/// cinematica de transformacao -- e a `--diagbancada` mede o MOSTRADOR, nao o combate.
///
/// ENTAO A REGRA AQUI E ESTREITA, e vale a pena escreve-la porque a proxima janela vai querer ser mais
/// larga:
///
///   1. CADA JANELA MEXE NUMA CAUSA SO, e devolve o que aconteceu de verdade (o HP que sobrou, a razao
///      de estamina que ficou). A bancada cobra a % efetivo contra o valor DEVOLVIDO, e nao contra o
///      que ela pediu: se a janela pedir 75 e o corpo parar em 78, a conta continua sendo cobrada
///      exata. Uma janela que devolvesse `void` faria a bancada cobrar a propria intencao.
///
///   2. A FERIDA PASSA PELO `Corpo.Ferir` DE PRODUCAO -- o mesmo metodo que o `MeleeResolver` chama
///      quando um soco acerta --, e NAO por `Ficha.HP = 75`. E nao e purismo: `HP` e DERIVADO
///      (`CombatState.SincronizarVida` faz `F.HP = Corpo.Vida()`), entao escrever no campo daria um
///      numero que o proximo sincronismo apagaria -- a bancada mediria um valor que o jogo nao tem.
///
///   3. NAO-LETAL, E SO EM MEMBRO. Braco e perna quebrados atrapalham; nucleo abaixo do limiar
///      NOCAUTEIA, e nocaute troca o `expressedBP` por 10% do BP base (familia 3) -- a % efetivo
///      deixaria de medir "quao inteiro eu estou" e passaria a medir outra coisa no meio da rodada.
///
///   4. O `Tick` VEM DEPOIS DA ESCRITA, e e o `Tick` inteiro (`Statify` + `PowerLevel`): e o
///      `PowerLevel` que escreve `expressedBP` e `peakexBP`, e sao esses dois que a % efetivo divide.
///      Mandar a ficha sem recalcular entregaria o campo novo com a conta velha.
/// ==================================================================================================
///
/// A JANELA DO SCOUTER NAO ESCREVE O BIT, de proposito: ela so poe o item na mochila. Quem liga o
/// scouter e o verbo `item_equipar` vindo do fio, que e o caminho do jogador -- e e ele que a bancada
/// tem que exercitar, porque o corte de sigilo mora no `FichaVisivel` e depende do bit que AQUELE
/// caminho acende. Acender o bit daqui provaria o corte e nao provaria o portao.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O KI EM UMA RAZAO DO TANQUE. Devolve a razao que ficou de verdade, ou NaN se nao achou.
	///
	/// SO POSA O ESTADO -- nao prova caminho nenhum, e a bancada diz isso onde a usa. O Ki SUBINDO
	/// acima de 100% ela pega pela tecla C (`SendCarregar`) e o Ki CAINDO ela pega pelo dreno da
	/// forma; esta janela existe pra o terceiro estado, "Ki baixo", que nenhum caminho barato produz:
	/// gastar Ki de verdade pede uma tecnica, um alvo e um combate.
	/// </summary>
	internal double KiDeTeste(int id, double razao)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return double.NaN;

		pl.Ficha.Ki = Math.Max(razao, 0) * pl.Ficha.MaxKi;
		pl.Ficha.Tick(agoraMs: NowMs());
		MandarFicha(pl);
		return pl.Ficha.MaxKi > 0 ? pl.Ficha.Ki / pl.Ficha.MaxKi : double.NaN;
	}

	/// <summary>
	/// MACHUCA ATE A VIDA CHEGAR PERTO DO ALVO. Devolve o HP que ficou, ou NaN se nao achou.
	///
	/// EM PASSOS PEQUENOS e espalhado pelos membros, porque `Ferir` propaga pros aninhados (a mao
	/// dentro do braco) e um golpe unico grande passaria do alvo: a bancada precisa parar ACIMA do
	/// piso de 0,6 do `hpratio`, senao a % efetivo para de responder e a checagem viraria um teste do
	/// piso em vez de um teste da perna da vida.
	/// </summary>
	internal double MachucarDeTeste(int id, double alvoHp)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return double.NaN;

		List<BodyPart> membros = pl.Combate.Corpo.Partes
			.Where(p => p.Papel == Vitalidade.Membro && !p.Aninhado && !p.Decepado)
			.ToList();
		if (membros.Count == 0) return pl.Ficha.HP;

		// teto de voltas: `Ferir` nao-letal tem PISO, entao um alvo baixo demais nunca chega e o
		// laco giraria pra sempre. Ele para no piso e a bancada cobra o valor devolvido.
		for (int volta = 0; volta < 400 && pl.Combate.Corpo.Vida() > alvoHp; volta++)
			foreach (BodyPart m in membros)
				pl.Combate.Corpo.Ferir(m, 4, letal: false);

		pl.Combate.SincronizarVida();
		pl.Ficha.Tick(agoraMs: NowMs());
		MandarFicha(pl);
		return pl.Ficha.HP;
	}

	/// <summary>Devolve o corpo ao inteiro. Sem isto a ferida contamina todo passo seguinte.</summary>
	internal double CurarDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return double.NaN;

		pl.Combate.Corpo.Curar(10_000);
		pl.Combate.SincronizarVida();
		pl.Ficha.Tick(agoraMs: NowMs());
		MandarFicha(pl);
		return pl.Ficha.HP;
	}

	/// <summary>
	/// A ESTAMINA DA CONTA DE PODER (`staminadeBuff`, 0-100). Devolve a RAZAO que o `PowerLevel`
	/// passou a usar -- ja com o piso de 0,3 aplicado --, que e o numero contra o qual a % efetivo
	/// pode ser cobrada.
	///
	/// ============================ ESTA PERNA ESTA MORTA NO JOGO, E POR ISSO ELA E TESTADA ============================
	/// `staminadeBuff` nasce em 100 e nada no jogo a abaixa hoje (so a Tecnica G3 a SOBE). Ou seja: o
	/// dono listou a estamina como causa da % cair, a formula obedece, e o mundo nunca aciona.
	///
	/// Nao e desculpa pra nao cobrir -- e o motivo pra cobrir. No dia em que alguem acordar a perna, a
	/// % efetivo tem que responder na medida, e esta e a unica checagem que vai dizer se ela responde.
	/// A bancada relata a perna como MORTA no jogo junto do resultado, pra ninguem ler o verde como
	/// "a fome cansa o corpo" -- ela so prova a CONTA.
	/// ============================================================================================================
	/// </summary>
	internal double EstaminaDeTeste(int id, double valor)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return double.NaN;

		pl.Ficha.staminadeBuff = Math.Clamp(valor, 0, 100);
		pl.Ficha.Tick(agoraMs: NowMs());
		MandarFicha(pl);
		return pl.Ficha.staminaratio;
	}

	/// <summary>
	/// POE UM SCOUTER NA MOCHILA -- e so isso. Ligar e desligar e com o verbo `item_equipar`, que a
	/// bancada manda pelo fio. Ver o cabecalho desta classe.
	/// </summary>
	internal bool ScouterNaMochilaDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		if (pl.Mochila.Quantos(CatalogoDeItens.Scouter) > 0) return true;
		return Guardar(pl, CatalogoDeItens.Scouter);
	}

	/// <summary>
	/// POE OU TIRA O NAV SYSTEM DA MOCHILA. Devolve se ele ficou la de verdade.
	///
	/// ============================ POR QUE ELA POE **E** TIRA ============================
	/// A janela do scouter so sabe Por, porque o que a bancada dele exercita e o verbo `item_equipar`.
	/// Aqui o portao E a mochila (`GameServer.Sigilo.TemNavSystem`), entao as duas metades da regra sao
	/// duas mochilas diferentes -- e uma bancada que so soubesse por o item nasceria DENTRO do estado
	/// "tem nav" e nunca mediria a ENTRADA nele nem a SAIDA dele. As duas metades ou nao ha regra.
	///
	/// ELA NAO ESCREVE O BIT, de proposito, exatamente como a do scouter: quem acende `Poder.Nav` e o
	/// `PoderesVisiveis` do tique olhando a mochila. Acender o bit daqui provaria a barra de abas e nao
	/// provaria o portao -- que e o defeito que esta tarefa veio consertar.
	///
	/// O `MandarAtributos` NAO PRECISA SER CHAMADO AQUI: o tique manda atributos pra todo jogador com
	/// `Peer` (`GameServer.Combat.cs`), e a assinatura de deduplicacao inclui `a.Poderes` -- entao mexer
	/// na mochila ja forca o reenvio dentro de um tique (1/30 s). A bancada espera um passo e mede.
	/// ==================================================================================================
	/// </summary>
	internal bool NavSystemNaMochilaDeTeste(int id, bool ter)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;

		if (!ter)
		{
			pl.Mochila.Tirar(CatalogoDeItens.NavSystem, int.MaxValue);
			MandarMochila(pl);
			return false;
		}

		if (pl.Mochila.Quantos(CatalogoDeItens.NavSystem) <= 0) Guardar(pl, CatalogoDeItens.NavSystem);
		return pl.Mochila.Quantos(CatalogoDeItens.NavSystem) > 0;
	}

	/// <summary>
	/// CONCEDE `Poder.Nav` AO JOGADOR SEM ITEM NENHUM. Devolve se o bit ficou aceso em `pl.Poderes`.
	///
	/// ============================ ESTA JANELA EXISTE PRA UM CONTRA-EXEMPLO ============================
	/// Todas as outras janelas deste arquivo posam o estado que a bancada quer MEDIR. Esta posa o estado
	/// que a regra tem que RECUSAR -- e ela existe porque o portao do Nav tem uma linha que, sem ela,
	/// seria indistinguivel de codigo morto:
	///
	///     Protocol.Poder p = pl.Poderes &amp; ~Protocol.Poder.Nav;   // GameServer.Sigilo.cs
	///
	/// Com `pl.Poderes` nunca contendo o bit, esse `&amp; ~` nao apaga nada nunca, e trocar a linha inteira
	/// por `pl.Poderes` daria exatamente o mesmo placar. Uma bancada que so soubesse por e tirar o ITEM
	/// ficaria 100% verde com a garantia mais forte da funcao apagada -- que e a armadilha de sempre:
	/// afirmacao de um lado so fica verde num sistema morto.
	///
	/// PELO CAMINHO DE PRODUCAO, e nao por `pl.Poderes |= ...`: o campo `Poderes` e REFEITO do zero toda
	/// vez que a lista de skills muda (`AplicarPoderes`), entao escrever nele direto seria posar um
	/// estado que o jogo apaga sozinho na proxima skill. `PoderesConcedidos` e o campo que sobrevive ao
	/// recalculo, e e por ele que o admin, o cargo e o host acendem bit -- exatamente os caminhos por
	/// onde um dia alguem pode acender o Nav sem lembrar deste portao. Ver `ServerPlayer.PoderesConcedidos`.
	///
	/// O `AplicarPoderes` tambem zera `SigAtributos`, entao o pacote de atributos sai no proximo tique
	/// com o valor novo -- a bancada espera um passo e mede, como faz com a mochila.
	/// ==================================================================================================
	/// </summary>
	internal bool PoderNavConcedidoDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;

		pl.PoderesConcedidos |= Jandirus.Net.Protocol.Poder.Nav;
		AplicarPoderes(pl);
		return (pl.Poderes & Jandirus.Net.Protocol.Poder.Nav) != 0;
	}

	/// <summary>
	/// GRAVA O PERSONAGEM E LE DE VOLTA DO DISCO. Devolve se o Nav System sobreviveu a viagem.
	///
	/// ============================ ELA MEDE A UNICA RAZAO DE O ITEM NAO TER LIGA/DESLIGA ============================
	/// O DM tem um `Power_Switch()` que acende `usr.hasnav` (`PlanetTech.dm:9-19`), e a decisao aqui foi
	/// NAO porta-lo: o liga/desliga moraria em <c>ServerPlayer.PoderesConcedidos</c>, que nao vai pro
	/// disco, e quem deslogasse ligado acordaria com a aba sumida. Por isso quem responde e a MOCHILA.
	///
	/// So que "a mochila vai pro disco" era, ate esta linha existir, uma AFIRMACAO MINHA. E e o tipo de
	/// afirmacao que este projeto ja viu ficar errada em silencio: as cores de roupa estavam no objeto,
	/// pareciam salvas, e nunca persistiram -- `System.Text.Json` pulava o campo. Um item que sumisse da
	/// mochila no relog faria a aba sumir sozinha, e o jogador leria isso como "perdi meus 550.000".
	///
	/// PELO CAMINHO DE PRODUCAO INTEIRO, de proposito: `Persistir` e o mesmo metodo do logout (serializa
	/// com as opcoes do `AccountStore` e grava por arquivo temporario + rename), e a leitura e o mesmo
	/// `Carregar` do login. Um round-trip so na memoria nao provaria nada -- o defeito das cores estava
	/// exatamente entre o objeto e o JSON.
	/// ==========================================================================================================
	/// </summary>
	internal bool NavSobreviveAoRelogDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _store == null) return false;
		if (pl.Conta.Length == 0 || pl.Slot < 0) return false;

		Persistir(pl);
		CharacterSave? lida = _store.Carregar(pl.Conta)?.Slots[pl.Slot];
		return lida?.Mochila?.Quantos(CatalogoDeItens.NavSystem) > 0;
	}
}
