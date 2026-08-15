using Godot;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// AS DUAS CLASSES DE ANDROIDE -- `DNALabs.dm`, o Lab 1.
///
/// ============================ ESTE ARQUIVO E O CONSUMIDOR QUE FALTAVA ============================
/// `Fighter.AndroideAbsorcao` e `Fighter.AndroideInfinito` existiam, eram ESCRITOS pela conversao,
/// iam pro disco e voltavam do disco -- e **nenhuma linha do repo os lia**. Um jogador pagava dois
/// milhoes de zeni pela Energia Infinita e continuava com fome, com cansaco e com o Ki normal; o de
/// Absorcao ganhava o que a raca `"Android"` do DM ja dava a todo mundo (`+100` de Ki ao defletir,
/// `objects.dm:365`), que nao e a mecanica dele e nem sequer olhava a classe.
///
/// Era o sexto caso de "dado extraido sem consumidor" deste porte, e o mais caro em zeni.
/// ==============================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>`DNL_ABSORB_KI_PER_HIT` -- 6% do Ki maximo por ataque engolido.</summary>
	private const double AbsorcaoPorGolpeDeKi = 0.06;

	/// <summary>
	/// O TETO DO TANQUE DE QUEM ABSORVE: `min(MaxKi * 2, ...)` nas duas contas do original.
	///
	/// DOBRO E NAO CHEIO, e a diferenca e a mecanica: o androide de absorcao COMPRIME energia. Aparar
	/// o ganho em `MaxKi` faria dele um corpo que so repoe, e o que o DM desenhou e um corpo que
	/// LUCRA com quem atira nele.
	/// </summary>
	private const double TetoDoAndroideAbsorcao = 2;

	/// <summary>`Ki += MaxKi * 0.02` a cada `sleep(5)` -- 2% por meio segundo, ou 4% por segundo.</summary>
	private const double RegenDoNucleoInfinito = 0.04;

	// =====================================================================
	// A ENERGIA INFINITA
	// =====================================================================
	/// <summary>
	/// O NUCLEO PERPETUO. Porte de `dnl_start_infinite()` (`DNALabs.dm:201-210`).
	///
	/// ============================ POR QUE UM TIQUE DE SERVIDOR E NAO UM LACO POR JOGADOR ============================
	/// No DM isto e um `while` com `sleep(5)` dentro do mob, e o proprio original paga o preco disso:
	/// o laco morre com um runtime ou com a morte, e por isso existe um `dnl_login_check` so pra
	/// re-arma-lo (`:705`). Aqui ele e uma passada do tique de 1 Hz sobre quem tem a classe -- nao ha
	/// laco pra morrer, nao ha o que re-armar no login, e o dono da conta reconecta ja funcionando.
	///
	/// A TAXA E A MESMA: 2% do `MaxKi` por meio segundo sao 4% por segundo. Escrever 2% aqui teria
	/// cortado a regeneracao pela metade calada -- e o erro classico de portar um `sleep` sem
	/// converter a cadencia.
	/// ============================================================================================================
	///
	/// **NUNCA MAIS COME E NUNCA MAIS CANSA**, e as duas sao afirmacoes e nao somas: `stamina =
	/// maxstamina` e `currentNutrition = maxNutrition`. Um `+=` aqui deixaria o estomago
	/// (`Nutricao.Passo`, que roda no mesmo segundo) ganhar a corrida em algum momento.
	/// </summary>
	private void TickDoNucleoInfinito()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			Fighter f = pl.Ficha;
			if (!f.AndroideInfinito || f.dead) continue;

			f.stamina = f.maxstamina;
			f.CurrentNutrition = Nutricao.Tanque(f.Metabolism);
			if (f.Ki < f.MaxKi) f.Ki = Math.Min(f.MaxKi, f.Ki + f.MaxKi * RegenDoNucleoInfinito);
		}
	}

	// =====================================================================
	// A POSTURA DE ABSORCAO
	// =====================================================================
	/// <summary>
	/// ABRE E FECHA OS COLETORES. Porte de `mob/keyable/verb/Absorb_Ki()` (`DNALabs.dm:213-224`).
	///
	/// ELA E UM TRATO, E O PRECO E NAO ANDAR: enquanto os coletores estao abertos o corpo fica
	/// fincado (`canmove = 0`), e em troca **todo** ataque de ki que chegar vira energia em vez de
	/// dano. Nao ha sorteio: e imunidade a energia comprada com imobilidade.
	///
	/// A TRAVA ENTRA PELO `PodeMexerOCorpo`, que e o funil de vetor do jogo -- o mesmo do nocaute, da
	/// paralisia, da carga e do embate. Entrando ali ela vale pro jogador E pra IA sem uma linha a
	/// mais, e nao ha uma segunda resposta pra "por que meu personagem nao anda".
	///
	/// ============================ O QUE FICOU DE FORA, E POR QUE ============================
	/// O laco do original tem uma SEGUNDA metade: agarrando alguem, a postura rouba 3% do Ki maximo
	/// do alvo por tique (`DNL_ABSORB_GRAB_DRAIN`, `:237-243`). **Este port nao tem agarrao** -- nao
	/// existe `grabbee` nem equivalente, e o unico "segurar" que ha e o `Carregando` da tecla C, que
	/// e outra coisa. Inventar um alvo pra ela seria inventar mecanica, entao ela nao entrou, e a
	/// ausencia esta escrita aqui em vez de sumir no silencio. O dia em que houver agarrao, sao
	/// quatro linhas dentro do `TickDaPostura`.
	/// =====================================================================================
	/// </summary>
	private void AlternarPosturaDeKi(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		if (!f.AndroideAbsorcao)
		{ Avisar(pl, "seu corpo nao tem coletores de energia -- isso e do androide de ABSORCAO."); return; }

		if (f.ki_absorb_stance)
		{
			f.ki_absorb_stance = false;
			Avisar(pl, "voce desativa os coletores de energia e volta a se mover.");
			return;
		}
		if (f.KO || f.dead) { Avisar(pl, "nao da, caido."); return; }

		f.ki_absorb_stance = true;
		Avisar(pl, $"voce finca os pes e abre os coletores: qualquer ataque de ki que te acertar "
				   + $"vira energia (ate {TetoDoAndroideAbsorcao * 100:0}% do seu tanque). Voce nao anda "
				   + "enquanto estiver assim.");
		Falar(pl, Jandirus.Net.Protocol.Fala.Emote,
			  "finca os pes e abre os coletores de energia -- pronto para SUGAR qualquer ataque de ki!");
	}

	/// <summary>
	/// QUEM CAI, FECHA OS COLETORES. O `if(KO) { ki_absorb_stance = 0; break }` do laco do original.
	///
	/// Sem isto a postura viraria uma trava permanente: nocauteado nao aperta verb, e quem acordasse
	/// continuaria fincado no chao sem entender por que -- com a agravante de o `PodeMexerOCorpo`
	/// responder "nao" pra sempre. Roda no mesmo segundo do nucleo infinito.
	/// </summary>
	private void TickDaPostura()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			Fighter f = pl.Ficha;
			if (!f.ki_absorb_stance) continue;
			if (!f.AndroideAbsorcao || f.KO || f.dead)
			{
				f.ki_absorb_stance = false;
				if (f.AndroideAbsorcao) Avisar(pl, "os coletores se fecham sozinhos.");
			}
		}
	}

	/// <summary>
	/// O ATAQUE DE KI FOI ENGOLIDO? Porte de `dnl_absorb_ki_attack()` (`DNALabs.dm:248-253`).
	///
	/// Devolve **true** quando o tiro morreu aqui -- e ai ele nao causa dano, nao empurra e nao
	/// treina defesa de ki de ninguem: nao houve defesa, houve refeicao.
	///
	/// ============================ ELE VEM ANTES DO SORTEIO DE DEFLEXAO, E ISSO IMPORTA ============================
	/// A deflexao e uma CHANCE; esta postura e uma CERTEZA comprada com imobilidade. Se o sorteio
	/// rodasse primeiro, o androide de absorcao parado ainda tomaria a maioria dos tiros na cara --
	/// e o trato que ele pagou (nao andar) valeria menos que o `+100` de Ki que a raca `"Android"` ja
	/// da de graca a quem anda.
	/// ==========================================================================================================
	/// </summary>
	private bool EngoliuOAtaqueDeKi(ServerPlayer alvo, string nomeDoTiro, bool fisico)
	{
		Fighter f = alvo.Ficha;
		if (fisico || !f.AndroideAbsorcao || !f.ki_absorb_stance) return false;

		f.Ki = Math.Min(f.MaxKi * TetoDoAndroideAbsorcao, f.Ki + f.MaxKi * AbsorcaoPorGolpeDeKi);
		Avisar(alvo, $"seus coletores ABSORVEM {nomeDoTiro} por inteiro -- ele vira energia.");
		return true;
	}
}
