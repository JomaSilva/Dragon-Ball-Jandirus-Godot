using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>Os botoes de balanceamento do combate, num lugar so.</summary>
public static class CombatKnobs
{
	/// <summary>Multiplicador global do dano de soco (`globalmeleeattackdamage` do jogo).</summary>
	public static double DanoGlobal = 0.75;

	/// <summary>
	/// Divisor da CADENCIA. No BYOND era 3, o que com Eactspeed 20 dava 0,67s por soco leve --
	/// lento demais pra uma luta de Dragon Ball. Com 6 o soco leve sai em ~0,33s e o chute em
	/// ~0,67s, e a formula continua escalando: carregar Ki derruba o Eactspeed e o personagem
	/// bate mais rapido, que e como o jogo sempre quis premiar quem carrega poder.
	/// </summary>
	public static double DivisorCadencia = 6;

	public static double VelocidadeGlobal = 1;

	/// <summary>Alcance de um soco, em pixels. Um tile e 32.</summary>
	public static float Alcance = 40f;

	/// <summary>Meio angulo do cone do golpe, em graus. Fora dele o soco passa longe.</summary>
	public static float MeioAnguloCone = 60f;

	/// <summary>Piso do multiplicador de gap de poder. Sem TETO: 10x mais forte bate 10x.</summary>
	public static double BpModMin = 0.01;

	/// <summary>Quanto o crit multiplica, antes da tecnica entrar.</summary>
	public static int CritMin = 15, CritMax = 30;

	/// <summary>Chance base de crit, em porcento. No BYOND isto NUNCA disparava (ver MeleeResolver).</summary>
	public static double ChanceCrit = 8;

	/// <summary>Duracao do atordoamento de um crit, em segundos.</summary>
	public static double DuracaoStun = 0.6;

	/// <summary>
	/// Chance base, em porcento, de a guarda CEDER mesmo com o corpo inteiro. Sem ela a
	/// guarda vira parede: o banco de prova mostrou 100% dos golpes aparados com o corpo
	/// inteiro, o que faz segurar o bloqueio ser sempre melhor do que qualquer outra coisa.
	/// </summary>
	public static double FalhaBaseGuarda = 10;

	/// <summary>Quanto cada ponto de vantagem de poder soma na chance de furar a guarda.</summary>
	public static double FalhaGuardaPorGap = 20;

	/// <summary>Fracao do Ki maximo que cada bloqueio custa. Sem Ki, nao ha guarda.</summary>
	public static double CustoKiDaGuarda = 0.01;

	/// <summary>Espera, em segundos, entre um contra-ataque e o proximo.</summary>
	public static double RecargaDoContra = 1.5;

	/// <summary>Segundos que a tag de combate dura depois do ultimo golpe (90s no jogo).</summary>
	public static double TagDeCombate = 90;
}

/// <summary>
/// As contas do combate. Todas puras -- entram numeros, saem numeros -- pra poderem ser
/// testadas sem subir o jogo, e pra o servidor e o cliente concordarem sobre o que aconteceu.
/// </summary>
public static class CombatMath
{
	/// <summary>
	/// O GAP DE PODER. Bater em alguem dez vezes mais fraco multiplica o dano por dez.
	///
	/// Linear e SEM TETO de proposito: e o que faz o BP importar de verdade em Dragon Ball.
	/// O piso existe pra que o mais fraco ainda arranhe (1% do dano), nao pra que ele compita.
	/// </summary>
	public static double BpModulus(double meu, double dele)
	{
		if (dele <= 0) return meu <= 0 ? 1 : 999;
		if (meu <= 0) return 0;
		return Math.Max(Math.Round(meu / dele / 0.05) * 0.05, CombatKnobs.BpModMin);
	}

	/// <summary>
	/// O dano BASE de um soco: quanto a ofensiva de um supera a defensiva do outro.
	/// Tecnica conta dos DOIS lados -- pra quem bate ela e mira, pra quem apanha e leitura.
	/// </summary>
	public static double DanoBase(Fighter atacante, Fighter alvo)
	{
		double meu = atacante.Ephysoff + atacante.Etechnique / 1.25;
		double dele = alvo.Ephysdef + alvo.Etechnique / 1.25;
		if (dele <= 0) dele = 1;
		return meu / dele * CombatKnobs.DanoGlobal;
	}

	/// <summary>
	/// Tipos de dano contra resistencias. No padrao (fisico 2, energia 1, resistencias 1) da
	/// exatamente 1,5x -- e o mesmo numero do jogo.
	///
	/// GUARDA QUE O ORIGINAL NAO TINHA: se todo componente cair abaixo de 1, o divisor do
	/// BYOND vira zero e o proc estoura. Aqui o piso e 1.
	/// </summary>
	public static double Resistencia(double dano, IReadOnlyDictionary<string, double> tipos,
									 IReadOnlyDictionary<string, double> resistencias)
	{
		double soma = 0;
		int contados = 0;
		foreach ((string tipo, double valor) in tipos)
		{
			if (valor <= 0) continue;
			double r = resistencias.TryGetValue(tipo, out double rr) && rr > 0 ? rr : 1;
			double parcela = valor / r;
			soma += parcela;
			if (Math.Round(parcela) >= 1) contados++;
		}
		if (contados == 0) return dano;   // o original dividia por zero aqui
		return dano * (soma / contados);
	}

	/// <summary>Armadura de ki: retorno decrescente, nunca zera o dano.</summary>
	public static double Armadura(double dano, double armadura)
	{
		double f = Math.Clamp(armadura, 0, 100) / 100;
		return dano * (1 / (1.7 * f + 1));
	}

	/// <summary>
	/// A CHANCE DE ACERTAR, em porcento. Tecnica de quem bate contra velocidade de quem
	/// apanha, pesada pelo gap de poder.
	/// </summary>
	public static double Pontaria(Fighter atacante, Fighter alvo, double deflexaoAlvo, double precisao)
	{
		double vel = Math.Max(alvo.Espeed, 0.1);
		return atacante.Etechnique / vel
			 * BpModulus(atacante.expressedBP, alvo.expressedBP) * 100
			 - deflexaoAlvo + precisao;
	}

	/// <summary>
	/// Quanto tempo ate poder golpear de novo, em segundos.
	///
	/// Escala pelo `Eactspeed`, que cai quando o personagem carrega Ki -- entao carregar
	/// poder deixa a luta mais rapida, nao so mais forte.
	/// </summary>
	public static double Cadencia(Fighter atacante, double tipo = 1)
	{
		double div = Math.Max(CombatKnobs.DivisorCadencia * CombatKnobs.VelocidadeGlobal, 0.01);
		return Math.Max(atacante.Eactspeed / div * Math.Max(tipo, 0.1) / 10, 0.05);
	}
}
