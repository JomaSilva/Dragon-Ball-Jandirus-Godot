using System;

namespace Jandirus.Core.Combat;

/// <summary>
/// A ARMADURA DE UM OBJETO -- o outro sistema de destruicao do original, e o que faltava aqui.
///
/// ============================ SAO DOIS SISTEMAS, NAO UM ============================
/// O DM trata cenario de duas formas diferentes, e portar so uma deixa metade do mundo indestrutivel:
///
///   TURF (chao, parede, penhasco) tem `Resistance`, e a conta e um LIMIAR: se o `expressedBP` de
///   quem bate alcanca a resistencia, o tile cai inteiro; se nao alcanca, nao acontece nada, nem
///   um arranhao. Ver <see cref="Empurrao.ResistenciaPadrao"/>.
///
///   OBJ (arvore, bancada, banco, regenerador, maquina de gravidade) NAO usa `Resistance` -- o
///   campo existe na declaracao e nunca e lido. O que vale e `fragile` + `armor`/`maxarmor`, com
///   dano ACUMULADO: cada pancada tira um pedaco, e o objeto some quando a armadura zera.
///
/// Confundir os dois foi o que deixou "objeto nao quebra": a destruicao daqui so conhecia o
/// caminho do turf, e a arvore e o banco nao sao turf.
/// ====================================================================================
///
/// ============================ A SUPERARMADURA E O TRUQUE ============================
/// `barrier.dm:29-35`:
///
///     superarmor = 0.75*maxarmor
///     if(D>superarmor)
///         armor-=(D-superarmor)
///
/// Ou seja: dano abaixo de 75% da armadura MAXIMA e ignorado por inteiro, e o que passa disso e
/// descontado. Nao e uma reducao percentual -- e um PISO. As consequencias sao as que dao a
/// sensacao certa:
///
///   * quem e fraco demais nao arranha, por mais que insista. Bater mil vezes com metade da
///     armadura de dano nao derruba um poste;
///   * quem tem exatamente o dobro da armadura derruba de um golpe (D - 0,75M >= M quando
///     D >= 1,75M);
///   * entre os dois extremos ha uma faixa estreita em que a coisa cede em duas ou tres pancadas.
///
/// Uma armadura de objeto vale MUITO menos que a resistencia de um turf: o padrao do DM e 1, e por
/// isso arvore e mobilia caem no primeiro soco de qualquer um. Quem sobe esse numero e a
/// CONSTRUCAO DE JOGADOR, que nasce com a armadura igual ao teto de BP de quem a ergueu -- e ai so
/// alguem daquele nivel derruba.
/// ====================================================================================
/// </summary>
public static class Armadura
{
	/// <summary>
	/// `obj/var/maxarmor = 1` e `armor = 1` (`barrier.dm:8-9`).
	///
	/// Um contra um BP de tres digitos parece nada, e e proposital: no original a mobilia do mapa
	/// existe pra ser quebrada. O que a traz de volta nao e resistencia, e o mapa recarregar.
	/// </summary>
	public const double Padrao = 1;

	/// <summary>`superarmor = 0.75*maxarmor` -- o dano abaixo disto nao entra.</summary>
	public static double Piso(double maxima) => 0.75 * maxima;

	/// <summary>
	/// Aplica uma pancada e devolve a armadura que sobrou.
	///
	/// Nao mexe em nada: quem guarda o resultado e quem chama. Funcao pura pra a bancada poder
	/// medir a curva sem servidor.
	/// </summary>
	public static double Bater(double armadura, double maxima, double dano)
	{
		double piso = Piso(maxima);
		if (dano <= piso) return armadura;
		return Math.Max(0, armadura - (dano - piso));
	}

	/// <summary>Cedeu? `if(armor<=0&&!isdestroying)` do `testDestroy`.</summary>
	public static bool Cedeu(double armadura) => armadura <= 0;

	/// <summary>
	/// Quantas pancadas de <paramref name="dano"/> derrubam algo com esta armadura.
	/// Zero = nunca cede. So serve pra explicar no log e pra bancada.
	/// </summary>
	public static int Pancadas(double maxima, double dano)
	{
		double porGolpe = dano - Piso(maxima);
		if (porGolpe <= 0) return 0;
		return (int)Math.Ceiling(maxima / porGolpe);
	}
}
