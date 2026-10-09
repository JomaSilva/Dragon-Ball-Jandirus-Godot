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

	// =====================================================================
	// O CORTE DOS FRACOS -- `objects.dm:355-357`
	// =====================================================================
	/// <summary>
	/// O LIMIAR ABAIXO DO QUAL UM IMPACTO DE KI NAO E GOLPE: 10, em vida de membro (o membro cheio tem 100).
	///
	///     if(M.ResistCheck(dmg,"[element]")*BPModulus(BP,M.expressedBP) &lt; 10)
	///         explodeme = 1
	///         stoopme = 1
	///     else if(prob(deflectchance/2)&amp;&amp;M.Ki>=5&amp;&amp;M.DRenabled) ...    // o raspao
	///     else if(prob(deflectchance)&amp;&amp;M.Ki>=5&amp;&amp;M.DRenabled) ...      // a deflexao
	///     else ...                                                        // DamageLimb, Knockback, MiniStun
	///
	/// (`objects.dm:355-357`, no `Bump` de `/obj/attack/blast`). O numero comparado e o mesmo que entraria no
	/// membro -- aqui, o que o <see cref="Final"/> devolve. Abaixo dele o tiro NAO FERE E NAO E SORTEADO: nem
	/// raspao, nem deflexao, nem absorcao de androide, nem empurrao, nem atordoamento. A intencao esta escrita no
	/// proprio DM, dentro do ramo do dano: *"if the target is sufficiently strong, they should be able to walk
	/// through beams"* (`objects.dm:451`).
	///
	/// ============================ MEDIDO NO BYOND 516, E NAO SO LIDO (2026-10-08) ============================
	/// Mundo minimo com o `Bump`, os tres andares do `Move()`, o `explode()` e o `DamageLimb` copiados a letra:
	///   * 600 bolas com dano final 0,12 / 5 / 9,96: nenhuma feriu; com 10,08, 161 de 200 feriram (o corte e
	///     `&lt; 10`, estrito);
	///   * com a deflexao "de 1000%" e dano 0,12: nenhum raspao em 200 (o controle, com dano 120, deu 200 em 200);
	///   * o que sobra e o que o `Bump` faz ANTES da linha 355: o credito e a tag de combate (`:302-305`), o treino
	///     dos dois lados (`:306-319`: `kidefensecounter +1` por batida) e a PARALISIA (`:347-354`: 200 de 200
	///     paralisados com dano 0,12);
	///   * a bola sai do mundo no proprio `Bump` (`if(explodeme) explode()`, `:565-566`); a cabeca do raio fica
	///     marcada (`stoopme`), some no `Move()` seguinte (`:149-154`) e o segmento de tras vira cabeca e bate de
	///     novo -- uma batida a cada 5 tiques, contra uma por tique quando o raio fere;
	///   * o forte ANDA raio adentro: quatro tiles em ~20 tiques, ileso.
	/// (O `explode()` de la nao desenha nada -- o `spawn spawnExplosion(loc,...)` so roda depois de `src.loc = null`
	/// --, e o `explosion()` devolve na primeira volta do laco: nenhuma explosao do DM fere ninguem.)
	/// ==========================================================================================================
	///
	/// ============================ ONDE A LINHA CAI NAO E ONDE CAI NO DM, E ISSO E DIVIDA ANTIGA ============================
	/// O NUMERO e o do DM; a ESCALA do que ele corta nao e, porque a conta de `mods`/`basedamage` deste port ainda
	/// funde as duas grandezas (ver o cabecalho de `GameServer.Tecnicas.G5.cs`). Ficha nova contra um espelho de
	/// mesmo BP, medido com `Birth.Nascer`: a bola basica faz ~22 aqui e 0,99 no DM; o Ki Wave faz ~5,5 por batida
	/// aqui e 2,74 la. Entao, com o mesmo 10: aqui a bola de um novato fere ate um alvo 2x mais forte (no DM, so
	/// quem tem um decimo do BP dele), e o Ki Wave de um novato NAO fere um igual em nenhum dos dois (aqui precisa de
	/// 1,85x o BP do alvo; la, de 3,7x). O dono decidiu portar o numero como esta (2026-10-08).
	///
	/// E A DEFLEXAO QUASE SOME, como no DM: `deflexao% x dano final = 12 x max(kdef/10,1) / (Ekidef x
	/// log_4(max(kdef,4)))`, sem depender de quem atira nem do BP (fora do piso 0,01 do `BpModulus`). Sorteando so
	/// com dano >= 10, o teto e ~0,6% pra uma ficha nova (`Ekidef` 2). Ver <see cref="ChanceDeDeflexao"/>.
	/// ========================================================================================================================
	///
	/// BOTAO, E NAO CONSTANTE, como o <see cref="DanoGlobalDeKi"/>: e balanceamento.
	/// </summary>
	public static double CorteDoFraco = 10;

	/// <summary>
	/// DEFEITO INJETADO (bancada): o tiro fraco fere e e sorteado como qualquer outro -- o `Acertar` sem o ramo de
	/// `objects.dm:355-357`. Era o jogo ate 2026-10-08: um tiro de 5 tirava 5 de um membro, e um forte "esquivava"
	/// de bolas que no DM nem chegam ao sorteio.
	/// </summary>
	public static bool TiroFracoFereDeTeste;

	/// <summary>
	/// ESTE IMPACTO E FRACO DEMAIS PRA SER GOLPE? `dano final &lt; 10`, estrito -- dez cravado fere
	/// (`objects.dm:355`). E a pergunta que o `Acertar` do servidor faz depois da paralisia e antes dos sorteios.
	/// </summary>
	public static bool FracoDemais(double danoFinal) => !TiroFracoFereDeTeste && danoFinal < CorteDoFraco;

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

		// ============================ A ARMADURA: DIVERGENCIA DECLARADA (dono, 2026-10-08) ============================
		// O DM chama `ArmorCalc(dmg, M.Esuperkiarmor, FALSE)` (`objects.dm:330`), e o ramo FALSE e um LIMIAR
		// (`calcs.dm:37-39`: `if(damage>armor) return damage else return 0`) sobre o dano BRUTO -- antes da pericia,
		// da guarda e do `BPModulus`. Na linha seguinte `M.damage_armor(dmg)` (`objects.dm:331`, `calcs.dm:186-187`)
		// GASTA a armadura: 1 ponto se o tiro foi barrado, `dmg * armadura / 100` se passou. Ela volta 1 ponto por
		// `statify()` (`master.dm:162-163`), que o `GlobalStats` chama a cada 0,3 s (`Stats.dm:36`, `:67`).
		//
		// MEDIDO no BYOND 516: armadura 50 contra bruto 48 = 0 de dano em 200 bolas, e bruto 60 = 60 inteiro. Um raio
		// de bruto 48 ou 24 segurado em cima de `kiarmor` 50 nao feriu nem gastou a armadura (a batida barrada cai no
		// ciclo do corte, 2,4 por segundo, e a recarga e de 3,3); a bola de bruto 60 passou inteira e levou a
		// armadura de 51 a 21, 10, 5 e 2.
		//
		// AQUI A ARMADURA E A REDUCAO PROPORCIONAL do ramo TRUE (`calcs.dm:28-36`, a do soco) E NAO GASTA -- nem
		// aqui nem no soco, que no DM tambem cobra o desgaste (`calcs.dm:169-170`). O limiar faria dela uma
		// imunidade que nao olha o BP. Pelo bruto deste port contra alguem de stats iguais (Ki Wave 5, Masenko 11,
		// Kamehameha 29 a 44, Final Flash 34 a 53, Dodon Ray e Massive Beam 78 a 88, Death Beam 122 a 137), 6 pontos
		// barrariam todo Ki Wave e 66 -- as tres skills passivas no maximo -- barrariam tudo ate o Final Flash,
		// enquanto a bola de quem ja treinou (bruto de 141 pra cima) passaria sem desconto nenhum.
		// ==================================================================================================================
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
	///
	/// E ELA SO E SORTEADA ACIMA DO CORTE DOS FRACOS (<see cref="CorteDoFraco"/>, `objects.dm:355-357`): o "raio
	/// fraco contra um defensor forte" do paragrafo acima, que na conta passa de 100%, nem chega ao sorteio -- ele
	/// estoura sem ferir. A conta amarra as duas coisas (`chance% x dano final` e uma constante do defensor), entao
	/// na pratica a deflexao so aparece pra quem tem MUITA pericia de defesa de ki, ou no piso do `BpModulus`
	/// (um tiro pesado de alguem mais de quarenta vezes mais fraco).
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
