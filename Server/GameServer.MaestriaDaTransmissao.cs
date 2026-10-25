using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A MAESTRIA DO INSTANT TRANSMISSION (dono, 2026-10-23): *"faça o IT ganhar maestria com uso, e com isso ter menos consumo
/// de ki e vigor, podendo ate ser usado em combate, pois quanto mais maestria, mais rapido vc da IT em pessoas ou inimigos
/// proximos"*. A regra e as contas moram em <see cref="TransmissaoEmNiveis"/> (Core); aqui ficam as tres portas do servidor:
///   * o CUSTO e a ESPERA de cada viagem (<see cref="KiDaTransmissao"/>, <see cref="EsperaDaTransmissao"/>), que o verbo
///     (`GameServer.Transmissao.cs`) pede ao concentrar -- o cartao da aba Sense pede as MESMAS, pra escrever o que o verbo cobra;
///   * a SUBIDA, quando a viagem chega (<see cref="SubirAMaestriaDaTransmissao"/>), com o aviso no chat a cada 10%;
///   * o RELOGIO FINO (<see cref="TickDasTransmissoesProntas"/>): a concentracao vence no tique cheio, e nao so no de 5 Hz.
///
/// EM LUTA NAO HA TRAVA NOVA: o IT nunca teve uma, e o que o segura e a concentracao ancorada -- mexer mais de 4 px (o
/// empurrao de um golpe inclusive) ou cair cancela, como sempre (`TickDaTransmissaoG11`). O que a maestria muda e o tempo:
/// a 100%, 0,2 s em quem esta no campo de visao. VIGOR: o IT nunca cobrou, e continua sem cobrar.
/// </summary>
public partial class GameServer
{
	/// <summary>DEFEITO INJETADO (bancada --teleporteteste, familia M): a viagem que chega nao sobe a maestria. Falso em jogo, sempre.</summary>
	internal static bool MaestriaDoItNaoSobeDeTeste;

	/// <summary>DEFEITO INJETADO: a maestria nao baixa o Ki -- a viagem cobra o de sempre. Falso em jogo, sempre.</summary>
	internal static bool MaestriaDoItNaoBaixaOKiDeTeste;

	/// <summary>DEFEITO INJETADO: a maestria nao encurta a concentracao, de longe nem de perto. Falso em jogo, sempre.</summary>
	internal static bool MaestriaDoItNaoEncurtaDeTeste;

	/// <summary>DEFEITO INJETADO: o campo de visao perde a pressa propria (fica so o quinto de longe). Falso em jogo, sempre.</summary>
	internal static bool MaestriaDoItSemPressaDePertoDeTeste;

	/// <summary>DEFEITO INJETADO: a concentracao so vence no tique de ficha (5 Hz) -- os 0,2 s viram ate 0,4 s. Falso em jogo, sempre.</summary>
	internal static bool TransmissaoSoNoTiqueDeFichaDeTeste;

	/// <summary>DEFEITO INJETADO: o cartao do IT fica fora da assinatura do Sense -- a maestria sobe e a aba nao fica sabendo. Falso em jogo, sempre.</summary>
	internal static bool MaestriaForaDaAssinaturaDeTeste;

	private static bool EhYardrat(ServerPlayer pl) => string.Equals(pl.Race, "Yardrat", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// O CUSTO SEM MAESTRIA: o `kireq` de sempre (`yardrat.dm:101`), sem o BaseDrain por cima -- ate o `teleskill` 100 e o tanque
	/// inteiro, e dai cai ate um terco (300) ou um quinto (500, o Yardrat). E a maestria escondida do DM, e ela fica.
	/// </summary>
	private static double KireqSemMaestria(Fighter f) => Math.Min(f.MaxKi, f.MaxKi / (Math.Max(f.teleskill, 1e-9) / 100));

	/// <summary>O Ki de uma viagem deste corpo: o de sempre, e a maestria multiplica (<see cref="TransmissaoEmNiveis.KiComMaestria"/>).</summary>
	private static double KiDaTransmissao(ServerPlayer pl)
	{
		double semMaestria = KireqSemMaestria(pl.Ficha);
		return MaestriaDoItNaoBaixaOKiDeTeste ? semMaestria : TransmissaoEmNiveis.KiComMaestria(semMaestria, pl.Ficha.MaestriaDaTransmissao);
	}

	/// <summary>
	/// A CONCENTRACAO deste corpo no nivel dado: a de sempre (nivel e raca, <see cref="TransmissaoEmNiveis.MsDaConcentracao"/>),
	/// e a maestria encurta -- mais ainda quando o alvo esta no campo de visao (<see cref="TransmissaoEmNiveis.EsperaComMaestria"/>).
	/// </summary>
	private static long EsperaDaTransmissao(ServerPlayer pl, int nivel, bool noCampoDeVisao)
	{
		long semMaestria = TransmissaoEmNiveis.MsDaConcentracao(nivel, EhYardrat(pl), TransmissaoSemTetoDeEsperaDeTeste);
		if (MaestriaDoItNaoEncurtaDeTeste) return semMaestria;
		return TransmissaoEmNiveis.EsperaComMaestria(semMaestria, pl.Ficha.MaestriaDaTransmissao,
			noCampoDeVisao && !MaestriaDoItSemPressaDePertoDeTeste);
	}

	/// <summary>
	/// A VIAGEM CHEGOU: a maestria sobe (+2, o Yardrat +3, no teto 100) e o chat avisa a cada 10% -- dez linhas na vida inteira
	/// do personagem, nenhuma por viagem. Chamado no fim do <see cref="ChegarPelaTransmissaoG11"/>, depois de cobrar: a viagem
	/// e paga com a maestria de ANTES dela.
	/// </summary>
	private void SubirAMaestriaDaTransmissao(ServerPlayer pl)
	{
		if (MaestriaDoItNaoSobeDeTeste) return;
		Fighter f = pl.Ficha;
		double antes = f.MaestriaDaTransmissao;
		f.MaestriaDaTransmissao = TransmissaoEmNiveis.MaestriaDepoisDaViagem(antes, EhYardrat(pl));
		int degrau = TransmissaoEmNiveis.DegrauDoAviso(antes, f.MaestriaDaTransmissao);
		if (degrau >= TransmissaoEmNiveis.MaestriaMaxima)
			Avisar(pl, $"você domina o Instant Transmission (100%): o Ki cai a {TransmissaoEmNiveis.KiNaMaestriaCheia * 100:0}% "
					 + $"e a concentração a {TransmissaoEmNiveis.EsperaNaMaestriaCheia * 100:0}% -- e em quem está no campo de visão "
					 + $"ela leva {TransmissaoEmNiveis.EsperaPertoNaMaestriaCheiaMs / 1000.0:0.0#} s.");
		else if (degrau > 0)
			Avisar(pl, $"sua maestria do Instant Transmission chegou a {degrau}% -- menos Ki e uma concentração mais curta.");
	}

	/// <summary>
	/// O CARTAO DA ABA SENSE (`S2C.NivelDaTransmissao`, ver <see cref="FioDaTransmissao"/>): o nivel, a maestria e o que ela rende
	/// agora, pelas MESMAS portas que o verbo usa -- o cartao nunca promete um custo ou um tempo que a viagem nao cumpre.
	/// </summary>
	internal static TransmissaoState EstadoDaTransmissao(ServerPlayer pl, byte nivelIt)
	{
		int maestria = (int)Math.Floor(Math.Clamp(pl.Ficha.MaestriaDaTransmissao, 0, TransmissaoEmNiveis.MaestriaMaxima));
		if (nivelIt == 0) return new TransmissaoState(0, maestria, 0, 0, 0);
		int custo = (int)Math.Round(100 * KiDaTransmissao(pl) / Math.Max(pl.Ficha.MaxKi, 1e-9));
		return new TransmissaoState(nivelIt, maestria, custo,
			(int)EsperaDaTransmissao(pl, nivelIt, noCampoDeVisao: false), (int)EsperaDaTransmissao(pl, nivelIt, noCampoDeVisao: true));
	}

	/// <summary>O pedaco do IT na assinatura do Sense (`MandarSentidos`): a maestria que sobe remanda o cartao.</summary>
	private static string AssinaturaDaTransmissao(TransmissaoState it) => MaestriaForaDaAssinaturaDeTeste ? "" : it.ToString();

	/// <summary>
	/// O RELOGIO FINO DA CONCENTRACAO, no tique cheio (30 Hz). O tique do efetor (`TickDoEfetorG11`) anda a 5 Hz, e com 0,2 s de
	/// concentracao ele chegaria ate 0,4 s depois do aperto -- o dobro do que o dono escolheu. Aqui a concentracao vence no primeiro
	/// tique depois do prazo (ate 33 ms), pela MESMA funcao (`TickDaTransmissaoG11`: mexer ou cair cancela, o prazo leva). O
	/// efetor continua chamando a dele; quem chegar primeiro tira a concentracao da lista e o outro nao acha nada. (2026-10-24) A
	/// saida piscando e o clarao da chegada andam aqui tambem -- ver `GameServer.SaidaDaTransmissao.cs`.
	/// </summary>
	private void TickDasTransmissoesProntas()
	{
		TickDasSaidasDaTransmissao();   // (2026-10-24) a saida piscando e o clarao da chegada, no mesmo relogio -- ver GameServer.SaidaDaTransmissao.cs
		if (_transmissaoG11.Count == 0 || TransmissaoSoNoTiqueDeFichaDeTeste) return;
		long agora = NowMs();
		foreach (int id in _transmissaoG11.Keys.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl) || !_transmissaoG11.TryGetValue(id, out TransmissaoG11? t)) continue;
			// o mesmo crivo do efetor (`TickDoEfetorG11`): o corpo que ele nao tica, este relogio tambem nao
			if (EhNpcDoMundo(pl) || pl.DonoDoClone != 0 || pl.DonoDoCorpoLargado != 0 || pl.Livro == null) continue;
			TickDaTransmissaoG11(pl, t, agora);
		}
	}
}
