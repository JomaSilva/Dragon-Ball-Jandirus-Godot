using Jandirus.Core.Stats;

namespace Jandirus.Core.Combat;

/// <summary>
/// A CADEIA DE DANO DE KI -- a segunda do jogo, e ela NAO e a do soco.
///
/// ============================ POR QUE UMA SEGUNDA CADEIA ============================
/// O port tinha uma so: <see cref="CombatMath.DanoBase"/> + <see cref="MeleeResolver"/>, que e
/// `(Ephysoff + Etechnique/1.25) / (Ephysdef + Etechnique/1.25)`. Todas as ~33 tecnicas portadas
/// passam por ela, porque todas sao instantaneas e resolvem pelo funil do soco (`GolpeG3`).
///
/// O DM cobra OUTRA conta quando o que acerta e um objeto de ki (`objects.dm:315-321`, o `Bump`
/// de `/obj/attack/blast`):
///
///     dmg = DamageCalc(mods*6*globalKiDamage, (Ekidef**2 * max(Etechnique,Ekiskill)), basedamage, maxdamage)
///     se dmg == 0: dmg += basedamage*0.02
///     dmg = ArmorCalc(dmg, Esuperkiarmor, FALSE)
///     dmg /= log_4(max(kidefenseskill,4))
///     se bloqueando: dmg /= 2*log_4(max(kidefenseskill,4))
///     final = ResistCheck(dmg, elemento) * BPModulus(BP_do_tiro, expressedBP_do_alvo)
///
/// e `DamageCalc(up,down,base) = (up/down)*base` (`calcs.dm:1-7`).
///
/// As duas contas divergem no que importa: a de soco poe a DEFESA FISICA no denominador, a de ki
/// poe `Ekidef` AO QUADRADO. Enfiar raio no funil de melee (que era o atalho barato) faria a
/// defesa de ki de todo mundo nao valer nada -- e todo o ramo de skill de defesa de ki viraria
/// pontos jogados fora, calado.
/// ===================================================================================
///
/// TUDO AQUI E FUNCAO PURA, e e de proposito: e a unica coisa do projetil que a bancada de mesa
/// consegue afirmar sem subir servidor. O que anda, colide e mata mora no servidor.
/// </summary>
public static class DanoDeKi
{
	/// <summary>
	/// `log_10(max(x, piso))` -- o padrao que aparece em quase todo `basedamage` do `blasts.dm`.
	///
	/// Existe como funcao pra que o piso NAO seja digitado vinte vezes: e ele que faz um personagem
	/// novo (pericia zero) receber exatamente 1,0 em vez de menos infinito, e um piso esquecido num
	/// dos vinte seria um dano negativo que ninguem consegue relatar. Cada lote tinha a sua copia
	/// (`Log10Min`, `Log10G12`); esta e a unica.
	/// </summary>
	public static double Log10Min(double x, double piso) => Math.Log(Math.Max(x, piso)) / Math.Log(10);

	/// <summary>
	/// `var/globalKiDamage = 2` (`objects.dm:56`) -- o irmao do `globalmeleeattackdamage`, que no
	/// port e <see cref="CombatKnobs.DanoGlobal"/>. Botao de balanceamento: mexe no dano de TODO
	/// ataque de ki de uma vez.
	/// </summary>
	public static double DanoGlobalDeKi = 2;

	/// <summary>
	/// O PISO DO PISO: `if(dmg==0) dmg += basedamage*0.02`. Existe porque a divisao por
	/// `Ekidef**2 * max(Etechnique,Ekiskill)` pode zerar em ponto flutuante contra um veterano --
	/// e um raio que faz exatamente zero nao e um raio, e um bug que ninguem consegue relatar.
	/// </summary>
	public const double FracaoDoPiso = 0.02;

	/// <summary>
	/// O DANO CRU, antes de armadura, pericia de defesa, guarda, resistencia e gap de poder.
	///
	/// `maxdamage` opcional (0 = sem teto), igual ao `DamageCalc` do DM: e o unico jeito de uma
	/// tecnica dizer "por mais forte que eu seja, este golpe nao passa daqui".
	/// </summary>
	public static double Bruto(double mods, double baseDano, double maxDano, Fighter alvo,
							   bool fisico = false)
	{
		// `Ekidef**2 * max(Etechnique, Ekiskill)` -- o quadrado e a espinha da defesa de ki.
		double baixo = fisico
			? alvo.Ephysdef * alvo.Ephysdef * Math.Max(alvo.Etechnique, alvo.Ekiskill)
			: alvo.Ekidef * alvo.Ekidef * Math.Max(alvo.Etechnique, alvo.Ekiskill);

		return BrutoContra(mods, baseDano, maxDano, baixo, fisico);
	}

	/// <summary>
	/// ============================ O MESMO DANO CRU, CONTRA UM DIVISOR QUALQUER ============================
	/// O <see cref="Bruto"/> acima e este metodo com o divisor tirado de um <see cref="Fighter"/>. Ele
	/// foi separado porque **existe um alvo de ki que nao e um corpo**: o PLANETA visto do espaco
	/// (ver `MortePlanetaria.DanoNoMundo`). Um mundo nao tem `Ekidef`, nao tem tecnica e nao tem
	/// pericia de ki -- ele nao se defende, ele apenas esta ali.
	///
	/// **E o divisor 1 nao e uma invencao pro planeta**: e exatamente o que a linha logo abaixo ja
	/// fazia quando o corpo nao tinha defesa nenhuma (`if(!downscalar) downscalar=1`, o remendo que o
	/// proprio `calcs.dm` traz com comentario mal-humorado). Passar `defesa: 0` daqui e dizer "nao ha
	/// divisor" pela MESMA porta, e nao escrever uma segunda formula com o numerador copiado -- que e
	/// a forma conhecida de as duas divergirem no primeiro ajuste do `DanoGlobalDeKi`.
	/// ==================================================================================================
	/// </summary>
	/// <param name="defesa">
	/// O denominador do DM (`Ekidef**2 * max(Etechnique,Ekiskill)`). Zero ou negativo = "nao ha
	/// defesa", e cai no piso 1.
	/// </param>
	public static double BrutoContra(double mods, double baseDano, double maxDano, double defesa,
									 bool fisico = false)
	{
		double cima = fisico
			? mods * 6 * CombatKnobs.DanoGlobal
			: mods * 6 * DanoGlobalDeKi;

		// `if(!downscalar) downscalar=1` -- o proprio DM tapou a divisao por zero aqui, com
		// direito a comentario mal-humorado no `calcs.dm`.
		if (defesa <= 0) defesa = 1;

		double dmg = cima / defesa * baseDano;
		if (maxDano > 0) dmg = Math.Min(dmg, maxDano);
		if (dmg == 0) dmg = baseDano * FracaoDoPiso;
		return dmg;
	}

	/// <summary>
	/// `log_4(max(kidefenseskill, 4))` -- o divisor que a PERICIA DE DEFESA DE KI aplica. Com a
	/// pericia zerada da 1,0 (nao muda nada); com 100 da ~3,3x menos dano.
	///
	/// Fica separado porque o DM o usa DUAS vezes na mesma linha de defesa (uma sozinho, outra
	/// dobrado por causa da guarda), e escrever a mesma formula duas vezes e como as duas contas
	/// divergem.
	/// </summary>
	public static double DivisorDaPericia(Fighter alvo)
		=> Math.Log(Math.Max(alvo.kidefenseskill, 4)) / Math.Log(4);

	/// <summary>
	/// A CADEIA INTEIRA, do dano cru ao numero que entra no membro.
	///
	/// A ORDEM E A DO DM e ninguem pode reordenar sem mudar o balanceamento: armadura ANTES da
	/// pericia, pericia ANTES da guarda, e o gap de poder por ULTIMO multiplicando tudo -- e o que
	/// faz uma diferenca de 10x de BP valer 10x no raio inteiro, e nao so no pedaco cru.
	/// </summary>
	/// <param name="bloqueando">Guarda erguida: `dmg /= 2 * log_4(...)`, ou seja, a pericia conta DE NOVO.</param>
	public static double Final(double mods, double baseDano, double maxDano, double bpDoTiro,
							   CombatState alvo, bool bloqueando, bool fisico = false)
	{
		double dmg = Bruto(mods, baseDano, maxDano, alvo.F, fisico);
		dmg = CombatMath.Armadura(dmg, alvo.F.Esuperkiarmor);

		double pericia = DivisorDaPericia(alvo.F);
		dmg /= pericia;
		if (bloqueando) dmg /= 2 * pericia;

		// A RESISTENCIA usa os tipos do PROJETIL, nao os do corpo que atirou: um raio e "Energy",
		// um chute e "Physical". Ver `CombatState.TiposDeDano`, que e o mesmo dicionario.
		var tipos = fisico ? TiposFisico : TiposEnergia;
		dmg = CombatMath.Resistencia(dmg, tipos, alvo.Resistencias);

		return Math.Max(dmg * CombatMath.BpModulus(bpDoTiro, alvo.F.expressedBP), 0);
	}

	/// <summary>`element = "Energy"` -- o padrao de `/obj/attack` (`objects.dm:16`).</summary>
	public static readonly Dictionary<string, double> TiposEnergia = new() { ["Energy"] = 1 };

	/// <summary>`physdamage=1` troca o elemento pra "Physical" (`objects.dm:319`).</summary>
	public static readonly Dictionary<string, double> TiposFisico = new() { ["Physical"] = 1 };

	/// <summary>
	/// A CHANCE DE DEFLETIR, em porcento -- e ela e a razao de existir da defesa de ki.
	///
	/// `(Ekidef * max(expressedBP,1) * max(Ekiskill,Etechnique) * max(kidefenseskill/10,1))
	///  / (BP * mods * basedamage)` (`objects.dm:333`).
	///
	/// Repare que ela COMPARA: o numerador e o alvo, o denominador e o tiro. Um raio fraco contra
	/// um defensor forte passa de 100% -- o defensor sempre desvia --, e um raio de alguem dez
	/// vezes mais forte cai perto de zero. E o que faz "levar um Kamehameha na cara" ser diferente
	/// de "levar uma bolinha de ki na cara" mesmo com a mesma vida.
	///
	/// METADE DELA e uma deflexao BARATA (`prob(deflectchance/2)`: o alvo so anda de lado); a
	/// outra metade e a cara (deflete/reflete/absorve). Quem sorteia e o servidor -- ver
	/// `GameServer.Projeteis.cs`.
	/// </summary>
	public static double ChanceDeDeflexao(Fighter alvo, double bpDoTiro, double mods,
										  double baseDano, bool bloqueando, bool fisico = false)
	{
		double baixo = bpDoTiro * mods * baseDano;
		if (baixo <= 0.01) baixo = 0.01;

		double def = fisico ? alvo.Ephysdef : alvo.Ekidef;
		double cima = def * Math.Max(alvo.expressedBP, 1) * Math.Max(alvo.Ekiskill, alvo.Etechnique);

		// A pericia de defesa so entra na conta do KI -- na versao fisica o DM a deixa de fora.
		if (!fisico) cima *= Math.Max(alvo.kidefenseskill / 10, 1);

		double chance = cima / baixo;
		return bloqueando ? chance * 2 : chance;   // `if(M.blocking) deflectchance *= 2`
	}
}
