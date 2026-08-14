using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;

namespace Jandirus.Server;

/// <summary>
/// ============================ O MOTOR DO FROST DEMON MUTANTE ============================
/// O `effector()` da arvore racial do original (`Skills/Skill Trees/Race Trees/icer.dm:29-88`), que
/// la roda a cada ciclo de stats. Tudo o que o Frost Demon NORMAL precisa ja e dado -- multiplicador,
/// corpo, porta de BP, cinematica -- e sai do catalogo sozinho; este arquivo existe pro **Mutante**,
/// que e a unica raca do jogo cujo poder pode SAIR do controle.
///
/// ============================ A REGRA, INTEIRA, EM QUATRO FRASES ============================
///   1. O Mutante nasce lacrado na 1a supressao (25% do proprio poder) com o BP de fabrica QUATRO
///      vezes maior por causa disso. Abrir a casca e o jogo dele.
///   2. Ele SEGURA pra sempre ate a forma que a maestria da base destrancou
///      (<see cref="FormasDeFrost.DegrauEstavel"/>: 0/25/50/75/100 -> 1a/2a/3a/4a/base). Acima
///      disso ele PODE entrar -- e ai um fusivel comeca a queimar.
///   3. Queimado o fusivel, o ki TRAVA (a tecla C morre, `CargaDeKi.Passo`) e a liberacao de poder
///      DESPENCA 1,2% por segundo ate o piso de 10% -- e o `powerlevel()` ja consome isso
///      (`Fighter.Power.cs`, `fd_release`).
///   4. Recuando pra uma forma estavel ele recupera 4% por segundo; e com a base 100% masterizada
///      as supressoes viram BATERIA, regenerando Ki por grau de casca fechada.
///
/// ============================ POR QUE ISTO NAO CABIA NO CATALOGO ============================
/// Porque nada disso e propriedade de uma FORMA -- e um estado que atravessa as sete. O catalogo
/// responde "quanto vale a 4a Forma"; este arquivo responde "por quanto tempo ainda". Sao perguntas
/// de dono diferente, e a prova e que a mesma forma responde as duas de jeitos opostos conforme a
/// maestria de quem esta dentro dela.
///
/// **O QUE ELE **NAO** FAZ, e por que**: nao escreve multiplicador (quem escreve e o `AplicarForma`,
/// pelo `Mult` da entrada), nao troca sprite (quem troca e o cliente, pelo `CorpoDeForma`) e nao
/// decide quem pode virar o que (quem decide e o `EstadoDeForma.Avaliar`). Ele mexe em exatamente
/// tres campos: <see cref="Jandirus.Core.Stats.Fighter.fd_release"/>,
/// <see cref="Jandirus.Core.Stats.Fighter.fd_ki_locked"/> e o Ki.
/// ========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Quantos segundos o aviso de "o controle esta escapando" espera pra repetir.</summary>
	private const double FrostAvisoAntes = 10;

	/// <summary>E o de "o poder esta vazando", que e mais longo porque a situacao ja e clara.</summary>
	private const double FrostAvisoVazando = 15;

	/// <summary>
	/// EM QUE FRACAO DO FUSIVEL O AVISO SAI -- `world.time - fd_unstable_since >= limit * 0.6`
	/// (`icer.dm:79`). Nao e enfeite: e o unico sinal que da pro jogador escolher entre recuar e
	/// apostar, e sem ele a punicao chegaria do nada.
	/// </summary>
	private const double FrostAvisaEm = 0.6;

	/// <summary>
	/// QUANTO A MAESTRIA ALONGA O FUSIVEL: `(1 + fd_base_mastery / 50)` (`icer.dm:76`) -- 100% de
	/// maestria triplica o tempo antes de perder o controle. Escrito como divisor pra ficar igual ao
	/// original em vez de virar "x3 aos 100%", que e o resultado e nao a conta.
	/// </summary>
	private const double FrostMaestriaAlonga = 50;

	/// <summary>
	/// ESTE CORPO E UM FROST DEMON MUTANTE?
	///
	/// Pela CLASSE, que e onde o original poe (`fd_is_mutant()`: `Class == "Mutant Frost Demon"`) e
	/// onde o berco deste port ja sorteia. A raca entra junto porque "Mutant Frost Demon" e nome de
	/// classe de uma raca so -- e o dia em que nao for, a pergunta continua certa.
	/// </summary>
	private static bool EhMutanteDeFrost(ServerPlayer pl) =>
		FormasDeFrost.EhFrost(pl.Race)
		&& string.Equals(pl.Ficha.Class, FormasDeFrost.ClasseMutante, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// A MAESTRIA DA FORMA BASE -- o `fd_base_mastery`.
	///
	/// Sai do livro de maestrias como qualquer outra, e nao de um campo novo: a linha inteira do
	/// Frost Demon compartilha UMA barra (ver `Catalogo.ChaveDaMaestria`), que e exatamente o que o
	/// original tem. De quebra ela ja persiste, ja vai pro save e ja aparece na aba de formas sem
	/// ninguem ligar nada.
	/// </summary>
	private static double MaestriaDaBaseDeFrost(ServerPlayer pl) =>
		pl.Forma.Maestria.De(Catalogo.IdDaBaseDoFrost);

	/// <summary>
	/// O TIQUE. Roda junto do <see cref="TickDaForma"/> e no MESMO tique cheio, porque as duas coisas
	/// que ele mexe -- o Ki e o poder expresso -- sao as mesmas que a forma cobra.
	///
	/// ============================ EM CENA ELE PARA, COMO TODOS OS OUTROS RELOGIOS ============================
	/// A cinematica da 2a Evolucao prende o corpo por 28 s, e o fusivel dela e de 22,5 s (90 x 0,25).
	/// Sem esta guarda, o Mutante PERDERIA o controle no meio da propria estreia -- e sairia da cena
	/// com o ki travado e o poder vazando, sem nunca ter tido um instante pra agir. E a mesma regra
	/// que ja para o dreno da forma, a carga e o custo do voo (ver `GameServer.Formas.EmCena`).
	/// ====================================================================================================
	/// </summary>
	private void TickDoFrost(ServerPlayer pl, double dt)
	{
		if (dt <= 0 || !EhMutanteDeFrost(pl)) return;

		if (pl.FrostAvisoEmSegundos > 0) pl.FrostAvisoEmSegundos = Math.Max(0, pl.FrostAvisoEmSegundos - dt);
		if (EmCena(pl)) return;

		int forma = Catalogo.DegrauDoFrost(pl.Forma.Def);

		// FORA DA ESCADA DELE (base, ou um estado que nem devia acontecer): nada a governar. Nao
		// "conserta" o estado nem reverte ninguem -- quem cuida de onde o corpo repousa e o
		// `Catalogo.PisoDaEscada`, no login e no recuo.
		if (forma == 0) return;

		double maestria = MaestriaDaBaseDeFrost(pl);
		int estavel = FormasDeFrost.DegrauEstavel(maestria);

		if (forma <= estavel) EstavelNoFrost(pl, dt, forma, maestria);
		else InstavelNoFrost(pl, dt, forma, maestria);
	}

	/// <summary>
	/// FORMA QUE ELE SEGURA: o relogio zera, a liberacao volta e -- com a base dominada -- a casca
	/// fechada vira bateria.
	/// </summary>
	private void EstavelNoFrost(ServerPlayer pl, double dt, int forma, double maestria)
	{
		Jandirus.Core.Stats.Fighter f = pl.Ficha;
		pl.FrostInstavelSegundos = 0;

		if (f.fd_release < 1)
			f.fd_release = Math.Min(1, f.fd_release + FormasDeFrost.RecuperacaoPorSegundoPct / 100 * dt);

		// O DESTRAVAMENTO SO VEM COM A LIBERACAO CHEIA, e nao no instante em que ele recua: recuperar
		// custa segundos (4%/s, ou seja ate 22,5 s se ele foi ate o piso), e e esse tempo que faz
		// perder o controle DOER. Destravar a tecla C na hora transformaria a punicao num susto.
		if (f.fd_ki_locked && f.fd_release >= 1)
		{
			f.fd_ki_locked = false;
			Avisar(pl, "seu ki se acalma -- você recuperou o controle do seu poder.");
		}

		// ============================ A BATERIA -- `icer.dm:60-62` ============================
		// Com a base 100% masterizada, a supressao deixa de ser so prejuizo: cada GRAU de casca
		// fechada regenera 0,2% do Ki maximo por segundo, entao a 1a Forma (quatro graus abaixo da
		// base) enche 0,8%/s. E o que transforma o fundo do poco num LUGAR -- o Mutante que dominou
		// o proprio corpo se recolhe pra recarregar e volta a abrir.
		//
		// `MaxKi` E O TETO, e nao o teto de carga: isto e regeneracao passiva, nao power-up. Quem
		// passa dos 100% e a tecla C, e ela cobra folego.
		if (maestria >= 100 && forma < FormasDeFrost.Base && !f.dead && f.Ki < f.MaxKi)
			f.Ki = Math.Min(f.MaxKi,
				f.Ki + f.MaxKi * (FormasDeFrost.RegeneracaoPorGrauPct / 100)
					 * (FormasDeFrost.Base - forma) * dt);
	}

	/// <summary>
	/// FORMA ACIMA DO QUE ELE SEGURA: o fusivel queima, o ki trava, e o poder comeca a escapar.
	/// </summary>
	private void InstavelNoFrost(ServerPlayer pl, double dt, int forma, double maestria)
	{
		Jandirus.Core.Stats.Fighter f = pl.Ficha;

		// ============================ NOCAUTEADO O FUSIVEL NAO ANDA ============================
		// `icer.dm:46` so acumula maestria com `!S.KO && !S.dead`, e o corpo caido ja perde a forma
		// pelo `TickDaForma` (que reverte pro piso, e o piso e SEMPRE estavel). Esta guarda cobre a
		// janela de um tique entre cair e reverter -- sem ela, um nocaute de meio segundo poderia
		// travar o ki de quem ja esta no chao.
		if (f.KO || f.dead) return;

		pl.FrostInstavelSegundos += dt;

		double limite = FormasDeFrost.SegundosAtePerderOControle
					  * FormasDeFrost.FatorDoFusivel(forma)
					  * (1 + maestria / FrostMaestriaAlonga);

		if (!f.fd_ki_locked)
		{
			if (pl.FrostInstavelSegundos >= limite)
			{
				f.fd_ki_locked = true;
				pl.FrostAvisoEmSegundos = FrostAvisoVazando;
				Avisar(pl, "SEU KI SAIU DO CONTROLE! Você não consegue mais reunir energia -- e seu "
						 + "poder vai começar a VAZAR. Recue para uma forma de supressão estável!");
			}
			else if (pl.FrostInstavelSegundos >= limite * FrostAvisaEm && pl.FrostAvisoEmSegundos <= 0)
			{
				pl.FrostAvisoEmSegundos = FrostAvisoAntes;
				Avisar(pl, "seu ki treme -- o controle está escapando...");
			}
			return;
		}

		// ============================ O VAZAMENTO -- E ELE NAO E UM DESCONTO NO FIM ============================
		// `fd_release` e campo do `Fighter` e o `powerlevel()` o consome na FAMILIA 3 do funil
		// (`Fighter.Power.cs`, "aplicam no fim"), exatamente como o `base.dm:144` do original. Escrever
		// aqui e so mover o numero; quem o aplica e a conta de poder, uma vez, pra todo mundo que le
		// `expressedBP` -- o dano do soco, a guarda, o arremesso, a quebra de cenario e o scouter.
		//
		// Foi essa a licao que o Ultra Ego ja deixou anotada neste projeto ("reducao de dano tem que
		// ser campo do Core e nao desconto depois"): um `expressedBP *= 0,1` escrito num chamador
		// pegaria o soco e deixaria o arremesso passar.
		// ==================================================================================================
		if (f.fd_release <= FormasDeFrost.PisoDaLiberacao) return;

		f.fd_release = Math.Max(FormasDeFrost.PisoDaLiberacao,
			f.fd_release - FormasDeFrost.VazamentoPorSegundoPct / 100 * dt);

		if (pl.FrostAvisoEmSegundos <= 0)
		{
			pl.FrostAvisoEmSegundos = FrostAvisoVazando;
			Avisar(pl, $"seu poder está VAZANDO! (liberação: {f.fd_release * 100:0}%)");
		}
	}
}
