namespace Jandirus.Core.Stats;

/// <summary>
/// O TREINO FOCADO E A MEDITACAO FOCADA -- as constantes e a conta, num lugar so.
///
/// O dono (2026-10-21): o T ganha letras WASD num anel que fecha, o M ganha um marcador que vai e volta,
/// e os dois sao OPCIONAIS. O treino parado de hoje (AFK) continua igual. Cada acerto sobe o bonus ate
/// 2x o ganho normal; o erro derruba o bonus e custa vigor.
///
/// O bonus multiplica a parcela da ATIVIDADE (o <c>mult</c> de <see cref="Fighter.TrainGain"/> e o
/// <c>multi</c> de <see cref="Fighter.MedGain"/>), que e onde o DM punha o ritmo
/// (<c>6/(1+log(missedtrain))</c>, <c>Train.dm:72-108</c>). NAO multiplica o potencial oculto: esse
/// pinga cru em todo lugar (dono, 2026-10-20). O cliente nao manda o ganho -- so a tecla crua.
/// </summary>
public static class TreinoFocado
{
	// =====================================================================
	// O BONUS
	// =====================================================================
	/// <summary>O ganho normal, e tambem o piso: errar nao paga menos que o AFK.</summary>
	public const double PisoDoBonus = 1;

	/// <summary>O teto. Decisao do dono: "ate 2x".</summary>
	public const double Teto = 2;

	/// <summary>
	/// Quanto um acerto soma. Dez acertos limpos vao de 1x a 2x. Decisao minha: o dono disse que sobe,
	/// nao de quanto em quanto.
	/// </summary>
	public const double DegrauPorAcerto = 0.1;

	/// <summary>
	/// Quanto um erro tira. Maior que o degrau de proposito: martelar tecla rende menos do que esperar
	/// a letra. Dois erros no teto devolvem o ganho normal. Decisao minha.
	/// </summary>
	public const double QuedaPorErro = 0.5;

	/// <summary>O bonus depois de um acerto. Nunca passa do <see cref="Teto"/>.</summary>
	public static double Subir(double bonus) =>
		BonusNaoSobeDeTeste ? bonus : Math.Min(Teto, bonus + DegrauPorAcerto);

	/// <summary>O bonus depois de um erro. Nunca desce do <see cref="PisoDoBonus"/>.</summary>
	public static double Cair(double bonus) => Math.Max(PisoDoBonus, bonus - QuedaPorErro);

	/// <summary>
	/// DEFEITO INJETADO (bancada <c>--treinofocadoteste</c>): o acerto nao sobe o bonus. Lido por
	/// <see cref="Subir"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool BonusNaoSobeDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada <c>--treinofocadoteste</c>): o passe do <c>Treinar</c> ignora o bonus
	/// (o AFK e o focado pagam o mesmo). Lido pela linha do ganho em <c>GameServer.Treinar</c>. Falso
	/// em jogo, sempre.
	/// </summary>
	public static bool BonusNaoMultiplicaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada <c>--treinofocadoteste</c>): quem treina PARADO leva o teto. O focado
	/// e que ganha o bonus; o AFK fica em <see cref="PisoDoBonus"/>. Lido por
	/// <c>GameServer.MultiplicadorDoTreinoFocado</c>. Falso em jogo, sempre.
	/// </summary>
	public static bool AfkLevaOBonusDeTeste;

	// =====================================================================
	// A LETRA (o anel do T)
	// =====================================================================
	/// <summary>
	/// O prazo da primeira letra, em ms. O mesmo do embate (<c>GameServer.ZanzoClash</c>, 900): da tempo
	/// de ler a letra e achar a tecla.
	/// </summary>
	public const long PrazoInicialMs = 900;

	/// <summary>O piso do prazo. O mesmo do embate (300 ms): abaixo disso a letra nao se le.</summary>
	public const long PisoDoPrazoMs = 300;

	/// <summary>
	/// Quanto cada acerto seguido encurta a proxima letra, em ms. Com 15 acertos o prazo encosta no piso.
	/// Anti-macro: a sequencia aperta, e a letra e outra a cada vez. Decisao minha (o dono pediu o aperto
	/// e o piso, nao o passo).
	/// </summary>
	public const long ApertoPorAcertoMs = 40;

	/// <summary>O prazo da proxima letra, dado quantos acertos vieram seguidos. Nunca abaixo do piso.</summary>
	public static long PrazoDaLetra(int acertosSeguidos) =>
		PrazoNaoApertaDeTeste ? PrazoInicialMs
		: Math.Max(PisoDoPrazoMs, PrazoInicialMs - Math.Max(0, acertosSeguidos) * ApertoPorAcertoMs);

	/// <summary>
	/// DEFEITO INJETADO (bancada <c>--treinofocadoteste</c>): o prazo nao aperta. Lido por
	/// <see cref="PrazoDaLetra"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool PrazoNaoApertaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada <c>--treinofocadoteste</c>): a letra e sempre W. Lido pelo sorteio do
	/// servidor. Falso em jogo, sempre.
	/// </summary>
	public static bool LetraFixaDeTeste;

	// =====================================================================
	// O VIGOR DO ERRO
	// =====================================================================
	/// <summary>
	/// Fracao do vigor MAXIMO que um erro tira. 4%: vinte e cinco erros esvaziam um tanque cheio, e o
	/// treino em si continua gastando o dele (<see cref="Cansaco.VigorDoTreino"/>). A meditacao parada
	/// nao cansa; a focada, no erro, cansa -- e o preco de poder render o dobro. Decisao minha.
	/// </summary>
	public const double FracaoDeVigorPorErro = 0.04;

	/// <summary>O vigor que este erro custa. Zero no defeito injetado.</summary>
	public static double VigorDoErro(double vigorMaximo) =>
		ErroNaoCustaDeTeste ? 0 : Math.Max(0, vigorMaximo) * FracaoDeVigorPorErro;

	/// <summary>
	/// DEFEITO INJETADO (bancada <c>--treinofocadoteste</c>): errar nao custa vigor. Lido por
	/// <see cref="VigorDoErro"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool ErroNaoCustaDeTeste;

	// =====================================================================
	// A RESPIRACAO (a barra do M)
	// =====================================================================
	/// <summary>
	/// Um vai-e-volta inteiro, em ms. O marcador sai de 0, chega em 1 na metade e volta em 0 no fim.
	/// 1,6 s: a faixa do meio fica sob o marcador cerca de 140 ms por passagem, mais a folga. Decisao minha.
	/// </summary>
	public const long PeriodoDaRespiracaoMs = 1600;

	/// <summary>O meio da faixa, de 0 a 1. O marcador "certo" e o centro da barra.</summary>
	public const double CentroDaFaixa = 0.5;

	/// <summary>Metade da largura da faixa. A faixa inteira e 18% da barra. Decisao minha.</summary>
	public const double MeiaFaixa = 0.09;

	/// <summary>
	/// Folga de latencia, em ms, pros dois lados da faixa. O servidor julga pela HORA EM QUE O PACOTE
	/// CHEGOU, e um aperto certo nao pode virar erro so porque a rede demorou isto. 80 ms cobre o
	/// localhost e uma rede curta; rede longa se ajusta aqui.
	/// </summary>
	public const long FolgaDeLatenciaMs = 80;

	/// <summary>
	/// Depois de julgar um aperto, os seguintes nesta janela sao o mesmo dedo (ou o eco do pacote) e
	/// nao contam de novo. Decisao minha.
	/// </summary>
	public const long TravaAposJulgarMs = 180;

	/// <summary>Onde o marcador esta, de 0 a 1, num vai-e-volta que comeca em <paramref name="inicio"/>.</summary>
	public static double Posicao(long agora, long inicio, long periodo)
	{
		if (periodo <= 0) return 0;
		long t = (agora - inicio) % periodo;
		if (t < 0) t += periodo;
		double u = t / (double)periodo;
		return u < 0.5 ? u * 2 : (1 - u) * 2;
	}

	/// <summary>O marcador esta na faixa (ou, com a folga, esteve nela ha pouco)?</summary>
	public static bool NaFaixa(long agora, long inicio, long periodo)
	{
		if (RespiracaoSempreDentroDeTeste) return true;
		if (Dentro(Posicao(agora, inicio, periodo))) return true;
		if (Dentro(Posicao(agora - FolgaDeLatenciaMs, inicio, periodo))) return true;
		return Dentro(Posicao(agora - FolgaDeLatenciaMs / 2, inicio, periodo));
	}

	private static bool Dentro(double posicao) =>
		posicao >= CentroDaFaixa - MeiaFaixa && posicao <= CentroDaFaixa + MeiaFaixa;

	/// <summary>
	/// DEFEITO INJETADO (bancada <c>--treinofocadoteste</c>): qualquer aperto acerta, esteja onde estiver
	/// o marcador. Lido por <see cref="NaFaixa"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool RespiracaoSempreDentroDeTeste;

	/// <summary>
	/// (2026-10-21, revisao) A RESPIRACAO TEM PRAZO, como a letra do anel: quem fica isto sem apertar erra uma vez (o
	/// bonus cai e custa vigor), e de novo a cada prazo. Sem ele, subir ate 2x e largar o teclado deixava a meditacao
	/// focada rendendo o dobro no AFK -- e o dono deixou o AFK de fora do bonus ("o treino parado de hoje continua
	/// igual"). Um vai-e-volta e meio: da pra respirar numa passagem pelo meio e deixar a seguinte. Decisao minha.
	/// </summary>
	public const long RespiracaoSemApertoMs = PeriodoDaRespiracaoMs * 3 / 2;

	/// <summary>
	/// DEFEITO INJETADO (bancada <c>--treinofocadoteste</c>): a respiracao sem aperto nunca erra. Lido pela linha do
	/// prazo, no tique do servidor (<c>GameServer.TicarTreinoFocado</c>). Falso em jogo, sempre.
	/// </summary>
	public static bool RespiracaoSemPrazoDeTeste;

	/// <summary>
	/// (2026-10-21, revisao) O TETO DO ATRASO DA REDE que o julgamento desconta (ver <see cref="InstanteVisto"/>). Um
	/// atraso maior que isto (rede muito ruim, ou forjado) nao alarga a faixa alem dele.
	/// </summary>
	public const long AtrasoMaximoDaRedeMs = 400;

	/// <summary>
	/// (2026-10-21, revisao) O INSTANTE EM QUE O JOGADOR VIU O MARCADOR, na hora do servidor. O marcador que ele ve esta
	/// atrasado meia volta da rede (o pacote da fase demorou a chegar), e o aperto leva mais meia volta pra voltar: o
	/// servidor desconta o tempo de ida e volta (<c>NetPeer.RoundTripTime</c>) da hora em que o aperto CHEGOU. Sem isto,
	/// na VPS, um aperto certo no meio da faixa (que fica ~140 ms sob o marcador) chegava fora dela -- a folga de
	/// <see cref="FolgaDeLatenciaMs"/> so cobre a rede curta. O cliente nao diz quando apertou: quem mede e o servidor.
	/// </summary>
	public static long InstanteVisto(long chegada, long idaEVoltaMs) =>
		chegada - (RespiracaoSemCompensarAtrasoDeTeste ? 0 : Math.Clamp(idaEVoltaMs, 0, AtrasoMaximoDaRedeMs));

	/// <summary>
	/// DEFEITO INJETADO (bancada <c>--treinofocadoteste</c>): o julgamento usa a hora de chegada crua, sem descontar a
	/// rede. Lido por <see cref="InstanteVisto"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool RespiracaoSemCompensarAtrasoDeTeste;

	// =====================================================================
	// O CORPO
	// =====================================================================
	/// <summary>No fio: ninguem, treino focado, meditacao focada.</summary>
	public const byte ModoNenhum = 0, ModoTreino = 1, ModoMeditacao = 2;

	/// <summary>
	/// O WASD do treino focado NAO anda. A seta continua andando, e andar derruba o treino como hoje.
	/// Sem isto a letra do anel seria um passo -- e um passo derruba o treino
	/// (<c>LocalPlayer</c>, "nao se treina correndo").
	/// </summary>
	public static bool WasdNaoAnda(bool treinoFocadoLigado) =>
		treinoFocadoLigado && !WasdAndaNoFocadoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada <c>--treinofocadoteste</c> e <c>--diagtreinofocado</c>): no treino focado
	/// o WASD volta a andar. Lido por <see cref="WasdNaoAnda"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool WasdAndaNoFocadoDeTeste;

	/// <summary>
	/// O espaco da respiracao NAO e soco. O soco le a tecla direto (<c>IsActionJustPressed</c>), que nao
	/// ve o <c>SetInputAsHandled</c> da barra.
	/// </summary>
	public static bool EspacoEhDaRespiracao(bool meditacaoFocadaLigada) =>
		meditacaoFocadaLigada && !EspacoSocaNaRespiracaoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada <c>--treinofocadoteste</c>): o espaco da respiracao volta a socar. Lido
	/// por <see cref="EspacoEhDaRespiracao"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool EspacoSocaNaRespiracaoDeTeste;
}
