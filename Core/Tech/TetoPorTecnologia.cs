namespace Jandirus.Core.Tech;

/// <summary>
/// ============================ O TETO DAS MELHORIAS PELA TECNOLOGIA (dono, 2026-10-19) ============================
/// *"coloque limites do quanto vc pode dar de upgrade nas maquinas com base na tech level da pessoa, entao se alguem tem
/// tech level baixo, n vai conseguir dar mt upgrade na maquina de gravidade ... o mesmo pra maquina de regeneraçao e
/// drill, e considere tech 100 como oq acontece hj no jogo, sem um limite"*.
///
/// A INSPIRACAO E O DU (`DU-SOURCE-master`), onde o teto de uma melhoria sai da inteligencia de quem a compra: a maquina
/// de gravidade vai ate `Maximum Gravity * usr.Intelligence()**0.21` (`World Mechanics/Gravity.dm:125`), o scouter ate
/// `usr.Knowledge * 5 * usr.Intelligence()**0.35` (`Player Mechanics/Inventory.dm:1511`). Aqui a regua e o NIVEL DE
/// TECNOLOGIA (`Fighter.techskill`) de quem paga, em duas regras:
///
///   * A MELHORIA QUE O JOGO JA LIMITA (a forca do campo ate o teto de gravidade do mundo, o alcance dele ate 10, a Drill
///     ate o nivel 10, a nave ate 39x, a eficiencia e o nivel da estacao de recarga): o teto e o do jogo vezes
///     `tecnologia / 100`, e nunca abaixo do de fabrica -- com 50 de tecnologia, metade (<see cref="Proporcional"/>);
///   * A MELHORIA QUE O JOGO NAO LIMITA (as baterias, a velocidade de cura, as nanites, o poder e a energia do simulador,
///     as tres da clonagem): uma compra a cada <see cref="TechPorCompra"/> pontos de tecnologia, contadas desde a fabrica
///     (<see cref="Compras"/>).
///
/// Com <see cref="TechSemTeto"/> (100) ou mais nenhuma das duas vale: a primeira volta ao teto do proprio jogo, e a segunda
/// fica sem teto -- o jogo de antes, como o dono pediu. (O estudo deste port passa de 100; o teto para nele.)
///
/// O TETO E DE QUEM MELHORA, E NAO DA MAQUINA: a que ja passou dele (melhorada por alguem de mais tecnologia, ou antes
/// desta regra) continua como esta e funciona inteira -- so nao sobe mais nas maos de quem nao alcanca. E o DU outra vez:
/// o `Upgrade()` mede o `usr`, o `Click()` nao mede ninguem.
///
/// AS MELHORIAS DE UMA VEZ SO (estabilizacao, tratar ferimentos, paineis solares, lua negra, megafone, difusor, camera,
/// ressurreicao de androide) nao tem "quanto": ficam com a tecnologia que ja pediam. A blindagem do super computador ja
/// sai do teto de tecnologia do DM (`TetoDeTecnologia`), e fica como esta.
/// =================================================================================================================
/// </summary>
public static class TetoPorTecnologia
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--melhoriasteste`): nenhuma melhoria tem teto pela tecnologia -- o jogo de antes de
	/// 2026-10-19. Lido pela propria conta do teto (<see cref="Livre"/>). Falso em jogo, sempre.
	/// </summary>
	public static bool SemTetoDeTeste;

	/// <summary>Daqui pra cima a tecnologia nao limita nada: *"considere tech 100 como oq acontece hj no jogo, sem um limite"*.</summary>
	public const double TechSemTeto = 100;

	/// <summary>Nas melhorias que o jogo nao limita: uma compra a mais a cada tanto de tecnologia.</summary>
	public const double TechPorCompra = 5;

	/// <summary>O "sem teto" das contas inteiras.</summary>
	public const int SemTeto = int.MaxValue;

	/// <summary>A tecnologia que conta: os pontos inteiros, de 0 ate <see cref="TechSemTeto"/> (o que passa de 100 nao muda nada).</summary>
	public static double Nivel(double tech) => double.IsNaN(tech) ? 0 : Math.Clamp(Math.Floor(tech), 0, TechSemTeto);

	/// <summary>Com esta tecnologia a melhoria nao tem teto proprio?</summary>
	public static bool Livre(double tech) => SemTetoDeTeste || Nivel(tech) >= TechSemTeto;

	/// <summary>
	/// A REGRA DA MELHORIA QUE O JOGO JA LIMITA: o teto do jogo vezes `tecnologia / 100`, nunca abaixo do de fabrica.
	/// </summary>
	public static double Proporcional(double tech, double deFabrica, double doJogo) =>
		Livre(tech) ? doJogo : Math.Max(deFabrica, doJogo * Nivel(tech) / TechSemTeto);

	/// <summary>
	/// O teto proporcional em degraus inteiros, PRA CIMA: o primeiro ponto de tecnologia ja da o primeiro degrau (um campo de
	/// alcance zero nao pega ninguem, e a Drill de fabrica nao sobe sem ele). A folga e contra o `0,1 + 0,2` do ponto flutuante,
	/// que faria um 5 exato virar 6.
	/// </summary>
	private static int ProporcionalInteiro(double tech, int deFabrica, int doJogo) =>
		(int)Math.Ceiling(Proporcional(tech, deFabrica, doJogo) - 1e-9);

	/// <summary>
	/// A REGRA DA MELHORIA QUE O JOGO NAO LIMITA: quantas compras esta tecnologia permite, contadas desde a fabrica.
	/// <see cref="SemTeto"/> = sem teto.
	/// </summary>
	public static int Compras(double tech) => Livre(tech) ? SemTeto : (int)Math.Floor(Nivel(tech) / TechPorCompra);

	/// <summary>O valor de fabrica mais as compras -- sem estourar o "sem teto".</summary>
	private static int DeFabricaMais(int deFabrica, int compras) => compras == SemTeto ? SemTeto : deFabrica + compras;

	// =====================================================================
	// A MAQUINA DE GRAVIDADE (`GameServer.Gravidade.cs`)
	// =====================================================================
	/// <summary>O teto de fabrica da maquina (`Max = 10`).</summary>
	public const double GravidadeDeFabrica = 10;

	/// <summary>
	/// ATE ONDE A FORCA DO CAMPO SOBE nas maos desta tecnologia: o teto de gravidade do MUNDO vezes a fracao, arredondado, e
	/// nunca abaixo dos 10x de fabrica. Com 100 de tecnologia e o do mundo inteiro -- e nunca mais que ele.
	/// </summary>
	public static double ForcaDaGravidade(double tech, double tetoDoMundo) =>
		Math.Round(Proporcional(tech, Math.Min(GravidadeDeFabrica, tetoDoMundo), tetoDoMundo));

	/// <summary>Ate onde o alcance do campo sobe (o jogo para em <paramref name="doJogo"/>, 10).</summary>
	public static int AlcanceDaGravidade(double tech, int doJogo) => ProporcionalInteiro(tech, 0, doJogo);

	/// <summary>Quantas vezes a bateria pode DOBRAR desde a de fabrica (uma carga).</summary>
	public static int DobrasDaBateriaDaGravidade(double tech) => Compras(tech);

	/// <summary>Ate quantas nanites (a chance, em pontos percentuais, de recarregar parada).</summary>
	public static int NanitesDaGravidade(double tech) => Compras(tech);

	// =====================================================================
	// O REGENERADOR (`GameServer.Regenerador.cs`)
	// =====================================================================
	/// <summary>Ate quantas cargas de bateria (a de fabrica e uma).</summary>
	public static int BateriaDoRegenerador(double tech) => DeFabricaMais(1, Compras(tech));

	/// <summary>Ate que velocidade de cura (a eficiencia; a de fabrica e 1).</summary>
	public static int VelocidadeDoRegenerador(double tech) => DeFabricaMais(1, Compras(tech));

	/// <summary>Ate quantas nanites (decimos de ponto percentual de chance por pulso).</summary>
	public static int NanitesDoRegenerador(double tech) => Compras(tech);

	// =====================================================================
	// A DRILL (`GameServer.Drill.cs`)
	// =====================================================================
	/// <summary>Ate que nivel (o jogo para no <see cref="Drill.NivelMaximo"/>).</summary>
	public static int NivelDaDrill(double tech) => ProporcionalInteiro(tech, 1, Drill.NivelMaximo);

	// =====================================================================
	// A ESTACAO DE RECARGA (`GameServer.MaquinasDeChao.cs`)
	// =====================================================================
	/// <summary>Ate que eficiencia, no passo dela (0,5) e pra cima -- o jogo para em <see cref="EstacaoDeRecarga.EficienciaMaxima"/>.</summary>
	public static double EficienciaDaEstacao(double tech)
	{
		double passo = EstacaoDeRecarga.PassoDeEficiencia;
		double teto = Proporcional(tech, EstacaoDeRecarga.EficienciaDeFabrica, EstacaoDeRecarga.EficienciaMaxima);
		return Math.Min(Math.Ceiling(teto / passo - 1e-9) * passo, EstacaoDeRecarga.EficienciaMaxima);
	}

	/// <summary>Ate que nivel (o jogo para no <see cref="EstacaoDeRecarga.NivelMaximo"/>).</summary>
	public static int NivelDaEstacao(double tech) => ProporcionalInteiro(tech, 1, EstacaoDeRecarga.NivelMaximo);

	// =====================================================================
	// O SIMULADOR E A MAQUINA DE CLONAGEM (`GameServer.MaquinasGrandes.cs`)
	// =====================================================================
	/// <summary>
	/// Quantas vezes o teto de poder pode DOBRAR desde os 500 de fabrica. Conta em DOBRAS, e nao em poder, de proposito: o teto e
	/// um numero de poder, e esse so se le com medidor (`GameServer.PoderEscrito`).
	/// </summary>
	public static int DobrasDoSimulador(double tech) => Compras(tech);

	/// <summary>Ate quantos pontos de energia (o de fabrica e um).</summary>
	public static int EnergiaDoSimulador(double tech) => DeFabricaMais(1, Compras(tech));

	/// <summary>Ate que nivel de "sessao mais curta" (a de fabrica e 1: dez minutos).</summary>
	public static int VelocidadeDaClonagem(double tech) => DeFabricaMais(1, Compras(tech));

	/// <summary>Ate que nivel de "menos falha" (o de fabrica e 1: 50%).</summary>
	public static int FalhaDaClonagem(double tech) => DeFabricaMais(1, Compras(tech));

	/// <summary>Ate quantos pontos de bateria (sao dois de fabrica).</summary>
	public static int BateriaDaClonagem(double tech) => DeFabricaMais((int)MaquinaDeClonagem.EnergiaDeFabrica, Compras(tech));

	// =====================================================================
	// AS NAVES (`GameServer.Nave.cs`)
	// =====================================================================
	/// <summary>Ate que velocidade (o jogo para em <see cref="Naves.VelocidadeMaxima"/>).</summary>
	public static int VelocidadeDaNave(double tech) => ProporcionalInteiro(tech, Naves.VelocidadeInicial, Naves.VelocidadeMaxima);

	// =====================================================================
	// A FRASE
	// =====================================================================
	/// <summary>
	/// A RECUSA DE QUEM CHEGOU NO TETO DA PROPRIA TECNOLOGIA: o que, ate onde, e como o teto sobe -- uma frase so pra todas
	/// as maquinas, pra regra soar igual em todas.
	/// </summary>
	/// <param name="oQue">A melhoria, como comeca a frase ("a força do campo").</param>
	/// <param name="teto">O teto desta tecnologia, ja escrito ("250x", "nível 5").</param>
	/// <param name="proporcional">A regra da melhoria: a que o jogo ja limita (verdadeiro) ou a de uma compra a cada 5 (falso).</param>
	public static string Recusa(string oQue, double tech, string teto, bool proporcional) =>
		$"{oQue} já está no teto da sua tecnologia ({Nivel(tech):0}): {teto}. "
		+ (proporcional
			? "Esse teto sobe com a tecnologia, até o máximo do jogo aos 100."
			: $"Cada {TechPorCompra:0} pontos de tecnologia dão mais uma compra, e aos 100 não há teto.");
}
