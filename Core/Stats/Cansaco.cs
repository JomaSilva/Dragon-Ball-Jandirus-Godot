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
/// pratica); aqui o custo e uma FRACAO do tanque por segundo -- e, desde 2026-10-24, proporcional ao peso, e nao
/// ao quadrado dele (o dono; ver <see cref="VigorDoTreino(Fighter, double)"/>).
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
	/// O TEMPO DE UM PASSE DO TREINO: o `Treinar` do servidor cobra o vigor a cada tique de ficha (5 Hz -- `GameServer.TicksPorFicha`
	/// vezes `Protocol.TickSeconds`). Mora aqui pro HUD dizer "EXAUSTO" pela MESMA conta que recusa o T (`Hud.AvisosDoCorpo`); a
	/// bancada `--vigortreinoteste` cobra que o servidor passe o mesmo numero.
	/// </summary>
	public const double SegundosDoPasseDoTreino = 0.2;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): treinar volta a nao gastar vigor nenhum -- a v0.2.0, em que dava
	/// pra treinar pra sempre. Lido pela porta do servidor (`GameServer.PagarOVigorDoTreino`). Falso em jogo, sempre.
	/// </summary>
	public static bool TreinoNaoCansaDeTeste;

	/// <summary>
	/// O vigor que <paramref name="segundos"/> de treino custam a este corpo: a fracao do tanque, vezes o PESO que ele carrega.
	///
	/// ============================ PROPORCIONAL AO PESO (dono, 2026-10-24) ============================
	/// *"Cobrar proporcional ao peso"*. Aqui era o QUADRADO do peso, a letra do DM (`0.01*weight*weight`, `Train.dm:88`), e o ganho
	/// do treino so cresce LINEAR com ele (`Fighter.TrainGain`, `x weight`): mais peso rendia MENOS poder por tanque, e na conta do
	/// relogio menos por minuto (medido com a DLL da v0.4.4, descansando ate encher: sem peso 0,118 BP/min, com o peso no dobro do
	/// limite 0,071, no quadruplo 0,047, no teto de 8x 0,034) -- enquanto o painel prometia 2x, 4x, 8x. Linear, o tanque inteiro rende
	/// o MESMO poder com qualquer peso, e rende `weight` vezes mais depressa. DIVERGENCIA DECLARADA do DM.
	/// ================================================================================================
	/// </summary>
	public static double VigorDoTreino(Fighter f, double segundos) => VigorDoTreino(f.maxstamina, f.weight, segundos);

	/// <summary>O mesmo custo, pelos numeros que a ficha leva ao cliente (o HUD pergunta "exausto?" sem a ficha inteira).</summary>
	public static double VigorDoTreino(double vigorMaximo, double peso, double segundos) =>
		vigorMaximo * VigorDoTreinoPorSegundo * FatorDoPeso(peso) * Math.Max(segundos, 0);

	/// <summary>O peso na conta do vigor do treino: o proprio peso, nunca abaixo de 1 -- ou o quadrado dele, no defeito injetado.</summary>
	private static double FatorDoPeso(double peso)
	{
		double p = Math.Max(peso, 1);
		return VigorPeloQuadradoDoPesoDeTeste ? p * p : p;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--vigortreinoteste`): o vigor do treino volta a ser cobrado pelo QUADRADO do peso --
	/// o DM (`Train.dm:88`) e o jogo de antes de 2026-10-24. Lido por <see cref="FatorDoPeso"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool VigorPeloQuadradoDoPesoDeTeste;

	/// <summary>Este corpo ainda aguenta mais <paramref name="segundos"/> de treino?</summary>
	public static bool AguentaTreinar(Fighter f, double segundos) => AguentaTreinar(f.stamina, f.maxstamina, f.weight, segundos);

	/// <summary>A mesma pergunta pelos numeros da ficha do cliente (o "EXAUSTO" do HUD).</summary>
	public static bool AguentaTreinar(double vigor, double vigorMaximo, double peso, double segundos) =>
		vigor - VigorDoTreino(vigorMaximo, peso, segundos) >= vigorMaximo * VigorQueParaOTreino;

	// =====================================================================
	// O CORPO EXAUSTO, DITO AO JOGADOR (dono, 2026-10-24)
	// =====================================================================
	// *"Avisar e mostrar no HUD"*: o T exausto e recusado ja no pedido dizendo QUANTO FALTA e que a COMIDA recupera o vigor; a frase do
	// corte perdeu o "ou medite" (meditar nao enche o vigor mais depressa -- ele so volta pela digestao, `Nutricao.Digerir`); e o
	// estomago vazio e avisado a cada tentativa (o "seu estomago ronca" do servidor sai uma vez so, pela bandeira `Hungry`). As frases
	// moram aqui pro servidor e as bancadas lerem as mesmas; quem as diz e o `GameServer.VigorDoTreino.cs`.

	/// <summary>
	/// QUANTOS SEGUNDOS DE DESCANSO FALTAM PRA ESTE CORPO AGUENTAR UM PASSE DE TREINO, pela digestao de agora
	/// (<see cref="Nutricao.VigorQueVoltaPorSegundo"/>). Zero = ja aguenta; infinito = o vigor nao volta (o estomago vazio).
	/// </summary>
	public static double SegundosAteAguentarTreinar(Fighter f, double segundosDoPasse)
	{
		double falta = f.maxstamina * VigorQueParaOTreino + VigorDoTreino(f, segundosDoPasse) - f.stamina;
		if (falta <= 0) return 0;
		double volta = Nutricao.VigorQueVoltaPorSegundo(f);
		return volta > 0 ? falta / volta : double.PositiveInfinity;
	}

	/// <summary>A recusa do T exausto, com quanto falta (<see cref="SegundosAteAguentarTreinar"/>; infinito = o estomago vazio).</summary>
	public static string FraseDoTreinoRecusado(double segundosQueFaltam) =>
		double.IsFinite(segundosQueFaltam)
			? $"você está exausto: faltam uns {Math.Max(Math.Ceiling(segundosQueFaltam), 1):0} s de descanso pra poder treinar. É a comida que recupera o vigor: sem nada no estômago ele não volta."
			: FraseDoExaustoSemComida;

	/// <summary>A recusa do T exausto com o estomago vazio: ai nao ha tempo que chegue.</summary>
	public const string FraseDoExaustoSemComida =
		"você está exausto e com o estômago vazio: sem comida o vigor não volta. Coma alguma coisa pra poder treinar.";

	/// <summary>O corte do treino que esvaziou o vigor (`GameServer.PagarOVigorDoTreino`) -- sem o "ou medite" de antes.</summary>
	public static string FraseDoTreinoQueParou(Fighter f) =>
		f.CurrentNutrition <= 0
			? "você está exausto: o treino para. Com o estômago vazio o vigor não volta: coma alguma coisa."
			: "você está exausto: o treino para. Descanse até o vigor voltar -- é a comida que você digere que o recupera.";

	/// <summary>O aviso de cada tentativa de treinar com o estomago vazio.</summary>
	public const string AvisoDoEstomagoVazio = "seu estômago está vazio: sem comida o vigor não volta. Coma alguma coisa.";

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
