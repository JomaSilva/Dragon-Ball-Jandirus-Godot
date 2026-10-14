namespace Jandirus.Core.Stats;

/// <summary>
/// **O QUE CANSA** -- os custos de vigor e de Ki que o dono pediu em 2026-10-13, num lugar so:
///
///   * *"falta o treino (botao T) gastar vigor com o tempo, pq atualmente ta dando pra treinar pra sempre"*;
///   * *"socar deveria gastar um pouco de ki (nao muita coisa, mas pra pelomenos uma luta longa forçar o jogador
///     ter q carregar o ki e gastar vigor)"*;
///   * *"voar parece q n ta gastando energia, olha no DM como era e aplique a logica aqui tb"*.
///
/// (O meditar que rende menos mora no `GainKnobs.MeditarRende`; o Ki acima de 100% que escorre, no `CargaDeKi`.)
///
/// ============================ O QUE O DM TINHA, E O QUE MUDA ============================
/// TREINO: `stamina -= 0.01*weight*weight` a cada `trainproc()`, e o treino para sozinho quando o vigor nao paga
/// (`Train.dm:86-103`). O port nao tinha trazido a linha. O NUMERO de la e absoluto (um centesimo de ponto de
/// vigor por passada: mais de meia hora de treino por tanque, com o estomago repondo no meio -- "pra sempre", na
/// pratica); aqui o custo e uma FRACAO do tanque por segundo, e o quadrado do peso continua valendo.
///
/// SOCO: no DM o soco basico nao gasta Ki (`attack_proc.dm`). NOVO, pedido do dono.
///
/// VOO: o custo do DM ja estava portado na letra (`Voo.CustoPorSegundo`, `Stats.dm:415`) -- so que ele e Ki
/// ABSOLUTO (um ponto por tique, no minimo), e some conforme o tanque cresce; e o admin voava de graca (o port
/// tinha lido o `freeflight` de la, que e um modulo de ciborgue, como "admin"). Ficou um PISO em fracao do
/// tanque, e o admin paga como todo mundo.
/// ========================================================================================
/// </summary>
public static class Cansaco
{
	// =====================================================================
	// O TREINO
	// =====================================================================
	/// <summary>Quanto do vigor MAXIMO um segundo de treino gasta, sem peso: o tanque cheio da uns 3 minutos.</summary>
	public const double VigorDoTreinoPorSegundo = 0.005;

	/// <summary>Abaixo desta fracao do vigor maximo o corpo nao aguenta treinar: o treino para.</summary>
	public const double VigorQueParaOTreino = 0.05;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): treinar volta a nao gastar vigor nenhum -- a v0.2.0, em que dava
	/// pra treinar pra sempre. Lido pela porta do servidor (`GameServer.PagarOVigorDoTreino`). Falso em jogo, sempre.
	/// </summary>
	public static bool TreinoNaoCansaDeTeste;

	/// <summary>
	/// O vigor que <paramref name="segundos"/> de treino custam a este corpo: a fracao do tanque, vezes o QUADRADO
	/// do peso que ele carrega (`0.01*weight*weight`, `Train.dm:88` -- treinar pesado rende mais e cansa muito mais).
	/// </summary>
	public static double VigorDoTreino(Fighter f, double segundos)
	{
		double peso = Math.Max(f.weight, 1);
		return f.maxstamina * VigorDoTreinoPorSegundo * peso * peso * Math.Max(segundos, 0);
	}

	/// <summary>Este corpo ainda aguenta mais <paramref name="segundos"/> de treino?</summary>
	public static bool AguentaTreinar(Fighter f, double segundos) =>
		f.stamina - VigorDoTreino(f, segundos) >= f.maxstamina * VigorQueParaOTreino;

	// =====================================================================
	// O SOCO
	// =====================================================================
	/// <summary>
	/// Quanto do Ki MAXIMO um soco leve gasta. O pesado gasta o triplo (o peso do golpe, `Protocol.PesoDoGolpe`).
	/// A tres socos por segundo, um tanque cheio dura perto de um minuto e meio de pancada sem parar.
	/// </summary>
	public const double KiDoSocoLeve = 0.004;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o soco volta a sair de graca -- a v0.2.0. Lido pela porta do
	/// servidor (`GameServer.CobrarOKiDoSoco`). Falso em jogo, sempre.
	/// </summary>
	public static bool SocoDeGracaDeTeste;

	/// <summary>O Ki que este soco custa. Nunca recusa o golpe: sem Ki o soco sai (fraco -- o poder cai com o tanque).</summary>
	public static double KiDoSoco(Fighter f, double pesoDoGolpe) => f.MaxKi * KiDoSocoLeve * Math.Max(pesoDoGolpe, 1);

	// =====================================================================
	// O VOO
	// =====================================================================
	/// <summary>O PISO do custo de voar, em fracao do Ki maximo por segundo: o tanque cheio da uns cinco minutos e meio no ar.</summary>
	public const double PisoDoVooPorSegundo = 0.003;

	/// <summary>
	/// O piso do voo RAPIDO (o Superflight): quatro vezes o do voo comum. No DM a razao e treze (`450/35`,
	/// `Stats.dm:415`) -- la em Ki absoluto, que o veterano nem sente; em fracao do tanque, treze derrubaria
	/// qualquer um em vinte segundos.
	/// </summary>
	public const double PisoDoVooRapidoPorSegundo = 0.012;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o voo volta a custar so o Ki absoluto do DM, e o admin volta a
	/// voar de graca -- a v0.2.0. Lido pelas duas portas do servidor (`PagaPeloVoo`, o desconto do `TickDoVoo`).
	/// Falso em jogo, sempre.
	/// </summary>
	public static bool VooSemPisoDeTeste;

	/// <summary>
	/// O Ki que um segundo no ar custa: o do DM (<paramref name="custoDoDm"/>, `Voo.CustoPorSegundo`) ou o piso em
	/// fracao do tanque, o que for MAIOR. O novato continua pagando o preco alto de la; o veterano, pelo menos o piso.
	/// </summary>
	/// <param name="comPiso">So o corpo de jogador paga o piso: o NPC que voa nao sabe pousar pra descansar.</param>
	public static double KiDoVooPorSegundo(Fighter f, double custoDoDm, bool rapido, bool comPiso)
	{
		if (!comPiso || VooSemPisoDeTeste) return custoDoDm;
		return Math.Max(custoDoDm, f.MaxKi * (rapido ? PisoDoVooRapidoPorSegundo : PisoDoVooPorSegundo));
	}
}
