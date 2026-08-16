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

	/// <summary>
	/// ============================ O TETO DA CURA PASSIVA -- e ele nao E mais a cura ============================
	/// **ISTO ERA A REGRA E VIROU UM LIMITE SUPERIOR.** Era a taxa de cura de todo mundo: 1,667 de
	/// vida por segundo em CADA membro, plana, sem raca, sem membro, sem fome, sem vigor. A regra
	/// mudou de casa pro <see cref="Regeneracao"/>, que e o `assign_regen()` + os quatro canais de
	/// `Injuries.dm`/`master.dm` do original -- ver o cabecalho de la.
	///
	/// **ELE CONTINUA AQUI PORQUE TEM UM SEGUNDO DONO**: o piso do dano da estrela e calibrado pra
	/// ficar ACIMA da cura (`Core.World.CalorDaEstrela.FatorMinimo`), senao o sol curaria em vez de
	/// matar (regra 0.7 da spec). Uma desigualdade dessas nao pode ser afirmada contra "a cura da
	/// raca que estiver la dentro" -- ela precisa do pior caso, e o pior caso e este numero.
	///
	/// **E ELE AINDA E UM TETO DE VERDADE**, com folga, e a conta cabe numa linha: o corpo que mais
	/// se cura no jogo e o Majin (`Regeneration = 100` -> `passiveRegen = 2,20`) num membro de
	/// `regenerationrate = 2`, com `Ephysdef` saturado. Somando os quatro canais da
	/// <see cref="Regeneracao"/> isso da **~1,07 de vida por segundo** -- abaixo de 1,667, e muito
	/// abaixo do piso de 3,0 do sol. Uma raca comum fica em ~0,26.
	/// ======================================================================================================
	/// </summary>
	public static double RegenPorSegundo = 100.0 / 60.0;

	/// <summary>
	/// O RELOGIO DA MECANICA (`IsInFight`), em segundos -- e ele NAO e a tag de 90 s.
	///
	/// ============================ O DM SEPARA OS DOIS DE PROPOSITO ============================
	/// `UpdateFightingStatus` desliga o `IsInFight` com `spawn(100)`, ou seja 10 s
	/// (`UpdateFightingList.dm:25-26`), enquanto o `combatTag` dura 900 decisegundos = 90 s. E o
	/// comentario do proprio original diz por que estao separados:
	///
	///   "Deliberately does NOT set IsInFight, so the long tag never drags combat-speed /
	///    Ki-regen / stun / skill-gain mechanics along with it."
	///
	/// O port tinha juntado os dois: `IsInFight = EmCombate > 0`, com 90 s. Consequencia -- a
	/// regeneracao de Ki (que so passou a existir agora) ficava pela METADE por um minuto e meio
	/// depois do ultimo golpe, e a velocidade de combate junto. Um minuto e meio de "ainda estou
	/// lutando" depois de a luta ter acabado.
	/// =========================================================================================
	/// </summary>
	public static double LutaDeVerdade = 10;
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
	/// ============================ O `Eactspeed` NAO SE MEXE, E ISSO FOI MEDIDO ============================
	/// Aqui estava escrito que ele "cai quando o personagem carrega Ki". NAO CAI -- nem com Ki, nem
	/// com velocidade, nem com nada. Medido com `speedMod` de 1,00 a 0,50 em stat cru 5, 20 e 60: o
	/// `Espeed` desce 31% e o `Eactspeed` fica em 20,000 nos nove casos, cadencia 0,333 s parada.
	///
	/// A razao esta em `Fighter.Statify`: `Eactspeed = clamp(actspeed / denom, 5, 22)` com
	/// `denom = max(log10(max(log10(dentro)*3, 1)) * 4, 1)`. Pra `denom` sair do piso 1 e preciso
	/// `dentro > 10^(10/3)`, ou seja ~2154 -- e com `Espeed` e `Ekiskill` saturados pelo `StatCap`
	/// (que satura perto de 10) o maximo que `dentro` alcanca e ~2,96. Alem disso `actspeed` e
	/// constante 20 e ninguem nunca escreve nele (`Fighter.cs`, `master.dm:68`).
	///
	/// ENTAO O LEVER POR PERSONAGEM E O DIVISOR, e ele e o do DM:
	/// `testactspeed /= 3 * globalmeleeattackspeed * hitspeedMod` (`attack cmn.dm:100/137`).
	/// `DivisorCadencia` e o `3`, `VelocidadeGlobal` e o global -- faltava o terceiro, que e por
	/// personagem. Divide: acima de 1 soca mais rapido, abaixo de 1 mais devagar.
	/// ==================================================================================================
	/// </summary>
	public static double Cadencia(Fighter atacante, double tipo = 1)
	{
		// `hitspeedMod` e do equipamento (ainda sem escritor no port); `formaCadencia` e da forma
		// ativa. Dois donos, dois campos, e compoem multiplicando -- ver `Fighter.cs`.
		double pessoal = Math.Max(atacante.hitspeedMod * atacante.formaCadencia, 0.01);
		double div = Math.Max(CombatKnobs.DivisorCadencia * CombatKnobs.VelocidadeGlobal * pessoal, 0.01);
		return Math.Max(atacante.Eactspeed / div * Math.Max(tipo, 0.1) / 10, 0.05);
	}
}
