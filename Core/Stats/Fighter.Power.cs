namespace Jandirus.Core.Stats;

/// <summary>
/// A CONTA DE PODER -- o `powerlevel()` do BYOND (Modules/Stats/BP/base.dm).
///
/// Transforma o BP REAL (o que o treino sobe, guardado no save) no BP EXPRESSO (o que um
/// scouter le, o que o dano usa, o que decide quem e mais forte). Entre um e outro passam
/// tres FAMILIAS de fator, e a familia de cada um importa mais do que o valor:
///
///  1) MULTIPLICAM        formas (SSJ, Oozaru, Blue), Ascensao, raiva, Controle de Poder,
///                        e o `nnetBuff` -- que ja e o pacote fechado de estado + debuffs.
///
///  2) SOMAM NA BASE      fusao, Mistico, Majin, gravidade, auras, Kaio-ken, paranormal.
///                        Entram como `BP*m - BP`, entao NAO se multiplicam entre si. Um
///                        valor abaixo de 1 aqui e PENALIDADE, mesmo parecendo buff.
///
///  3) APLICAM NO FIM     debuff do revive por Zeni (25%), carapaca da larva bio, vazamento
///                        do Frost mutante, e o corte de nocaute.
///
/// Trocar um fator de familia nao quebra compilacao e nao aparece em teste raso -- aparece
/// meses depois como numero esquisito em forma + Kaio-ken + God Ki ao mesmo tempo.
/// </summary>
public sealed partial class Fighter
{
	/// <summary>Larva de bio-androide expressa no maximo 1/100 do BP base.</summary>
	private const double BioLarvaRestrict = 100;

	/// <summary>
	/// `additiveBoost` -- converte um multiplicador em SOMA sobre o BP base. E o que impede
	/// os buffs da familia 2 de se multiplicarem uns pelos outros (que era impossivel de
	/// balancear: tres buffs de 2x viravam 8x em vez de 4x).
	/// </summary>
	public double AdditiveBoost(double mult) => BP * mult - BP;

	public void PowerLevel(double gravBalance = 1, long agoraMs = 0)
	{
		if (BP == 0) return;

		// --- estado do corpo: pisos generosos pra ferimento nao virar espiral ----------
		kiratio = Math.Max(Ki / Math.Max(MaxKi, 1e-9), 0.6);
		hpratio = Math.Max(HP / 100, 0.6);
		staminaratio = Math.Max(staminadeBuff / 100, 0.3);

		buffBuff = expandBuff * giantFormbuff * ArtifactsBuff * eyeBuff;
		formBuff = ssjBuff * transBuff * formsBuff * gateBuff * HellstarBuff * Math.Max(ue_ego_mult, 1);
		deBuff = 1 / Math.Max(weight * BPrestriction * splitformdeBuff, 1);

		// Ki acima de 100% e LINEAR: 200% = 2x, 300% = 3x. A versao antiga inflava a faixa
		// 100-200% (150% virava 1,66x) antes de virar linear.
		statusBuff = kiratio * hpratio * staminaratio;

		fusionBuff = Math.Max(FuseDanceMod * FPotaraMod, 1);

		// raiva com TETO de 2x (3x pra Legendary com a skill). Sem teto ela empilhava com o
		// Ki carregado e virava ~4x -- era a raiva, nao o Ki, no "10x" que o jogador via.
		angerBuff = Math.Min(Anger / 100, 2 + legendaryAngerBonus / 100);

		expressedAdd = HVBPExpAdd + MagAdd + TMagAdd;

		gravFelt = StatCurves.GravFelt(GravMastered, Planetgrav + gravmult, gravBalance);
		gravBuff = gravFelt;

		// A IDADE ENTRA AQUI, e ate hoje nao entrava em lugar nenhum. Crianca e ancia batem com
		// ate 3/4 do que o BP diria; o adulto no auge nao sente nada. Ver `Envelhecimento`.
		//
		// O SSJ4 E OS IMORTAIS PASSAM DIREITO no original (`Stats.dm:230` testa `ssj<4`, vampiro e
		// `biologicallyimmortal`). Aqui a excecao que ja da pra fazer honestamente e a da RACA que
		// nao envelhece -- e a propria funcao trata dela. As outras ficam pra quando o port tiver
		// vampirismo e imortalidade de verdade, e nao fingidas com um `if`.
		AgeDiv = Races.Envelhecimento.DivisorDeIdade(Race, Idade);

		// O PACOTE FECHADO: estado, idade, buffs de corpo e os divisores. Vale como UM fator
		// -- abrir os membros dele numa UI e contar duas vezes.
		netBuff = deBuff * statusBuff * AgeDiv * buffBuff * Math.Max(bp_remove, 0.01);
		nnetBuff = StatCurves.NetCap(netBuff);

		// --- base: BP real mais as somas de zeni cru ----------------------------------
		double tempBP = BP + BPadd + FuseBuff + HVBPAdd + CooldownAmount + majin_absorb_bp;
		if (AbsorbDeterminesBP && AbsorbBP != 0) tempBP = (tempBP + AbsorbBP) / 2;
		else tempBP += AbsorbBP;

		if (isconcealed && expressedBP >= 5)
		{
			// poder escondido: trava em 5 e ATROPELA tudo acima. Uma UI que liste os fatores
			// sem avisar disso vai se contradizer com o total.
			expressedBP = 5;
		}
		else
		{
			// familia 2 -- somam na base
			tempBP += AdditiveBoost(fusionBuff);
			tempBP += AdditiveBoost(MysticPcnt);
			tempBP += AdditiveBoost(MajinPcnt);
			tempBP += AdditiveBoost(gravBuff);
			tempBP += AdditiveBoost(aurasBuff);
			tempBP += AdditiveBoost(KaioPcnt);
			tempBP += AdditiveBoost(ParanormalBPMult);

			// familia 1 -- multiplicam
			tempBP *= BPBoost * formBuff;
			tempBP *= GodKiFactor();

			expressedBP = (DmMath.Round(Math.Max(tempBP, 1) * nnetBuff * angerBuff) + expressedAdd) * powerMod;

			// familia 3 -- no fim de tudo
			if (zeni_revive_debuff_until != 0 && agoraMs < zeni_revive_debuff_until)
				expressedBP = DmMath.Round(expressedBP * 0.25);

			// larva de bio-androide: TETO DURO em 1% do base. Raiva, Ki alto, forma e buff
			// nao furam a carapaca -- o divisor sozinho nao bastava, os multiplicadores
			// empilhavam por cima dele. (O rompimento da carapaca no tempo certo e do sistema
			// bio, que vem depois; aqui so mora o teto.)
			if (bio_lab_born && bio_stage == 1 && expressedBP > BP / BioLarvaRestrict)
				expressedBP = DmMath.Round(Math.Max(BP / BioLarvaRestrict, 1));
		}

		// Frost mutante sem controle: o poder VAZA ate o piso enquanto a supressao nao firma.
		if (fd_release < 1) expressedBP = DmMath.Round(Math.Max(expressedBP * fd_release, 1));

		// Nocauteado = COMPLETAMENTE exposto: 10% do BP base, e nada acima disso conta.
		if (KO) expressedBP = DmMath.Round(Math.Max(BP * 0.1, 1));

		// --- teto pessoal de treino ---------------------------------------------------
		// E do PROPRIO personagem: nao existe mais media de servidor puxando ninguem.
		relBPmax = BP * (1 + UPMod) * relcaprate * BPMod;
		if (HVBPAddEnd) relBPmax = BP;

		// o que o personagem seria sem idade, peso e ferimento -- serve pra "poder de pico"
		peakexBP = Math.Max(expressedBP / Math.Max(AgeDiv * deBuff * statusBuff, 1e-9), expressedBP);

		Egains = HBTCMod * Trainmult * bgains * tgains * tailgain;
		if (isHV && BoostActive && BoostMult != 0) Egains *= BoostMult;
	}

	/// <summary>
	/// O fator de God Ki. Fica separado porque e o unico ponto da conta onde um
	/// multiplicador SUBSTITUI outro em vez de se somar a ele.
	///
	/// A forma divina define o multiplicador TOTAL de forma (22x God, 32x Blue/Rose, 56x
	/// Royale/Beast). Como o `ssjBuff` ja foi multiplicado la em cima dentro do formBuff, a
	/// divisao por ele aqui CANCELA a escada Super Saiyajin e o produto reconstroi o numero
	/// divino cru. Mostrar "Forma 6x" e "God Ki 3,66x" lado a lado nao e contar duas vezes.
	/// </summary>
	private double GodKiFactor()
	{
		if (godki_gt_mode)
		{
			double f = godki_boost * gt_boost;
			if (godki != null && godki.adjust_me) f *= godki.transform_adjust;
			return f;
		}

		double fator = 1;
		if (godki != null && godki.usage)
		{
			double gfm = GodFormMult();
			fator *= gfm != 0 ? gfm / Math.Max(ssjBuff, 1) : godki.godki_mult;
		}

		// o Primal nao recebe BP do God Ki: nele o ki divino so destrava o SSJ4 Limit Breaker
		if (godki_give_mult != 0 && SaiyanLineage != "Primal Saiyan") fator *= godki_give_mult;
		return fator;
	}

	/// <summary>
	/// `god_form_mult()` -- o multiplicador CHEIO da forma divina ativa, ou 0 fora de uma.
	/// A escada e destravada pela maestria, nao por tiers: 22x de cara, 32x aos 33%, 56x aos 50%.
	/// </summary>
	public double GodFormMult()
	{
		if (godki == null || !godki.usage || !godki.awakened) return 0;

		// Prodigial nao tem SSG/Blue: o ki divino flui pelo MISTICO e culmina no Beast
		if (Class == "Prodigial")
		{
			if (beast_form) return 56;
			if (MysticActive) return godki.mastery >= GodKiState.BluePct ? 32 : 22;
			return 0;   // God Ki ligado sem Mistico: cai no godki_mult cru
		}

		if (ssj == 0 && lssj == 0) return 22;                       // Super Saiyajin Deus
		if (Math.Max(ssj, lssj) >= GodKiSsjCap) return 56;          // Royale / Rose 2
		return 32;                                                  // Blue / Rose
	}

	/// <summary>Teto de SSJ dentro do God Ki (1.5 = USSJ, o Royale da elite). Knob de admin.</summary>
	public static double GodKiSsjCap = 1.5;

	/// <summary>Velocidade global de treino (era `trainmult`, knob de balanceamento).</summary>
	public static double Trainmult = 1;

	// =====================================================================
	// TETO DE TREINO
	// =====================================================================
	/// <summary>BP acima disto treina com retorno decrescente. 0 desliga.</summary>
	public static double BpGainSoftcap = 1_000_000_000;

	/// <summary>Quanto amortecer acima do softcap. 1 = o ganho para de escalar com o BP.</summary>
	public static double BpGainSoftStrength = 1;

	public static double GainsRate = 1;

	/// <summary>A base de ganho da vez: fixa x raca/classe x marcos. Nada de BP proprio aqui.</summary>
	public double BpGainBase() => GainKnobs.LinearGainBase * Math.Max(BPMod, 0.1) * bp_milestone_mult;

	/// <summary>
	/// `capcheck()` -- quanto de um ganho bruto vira BP de verdade, dado o teto pessoal.
	///
	/// UM DESVIO DELIBERADO: os dois ramos do DM somam o buffer INTEIRO ao gap, mas so um
	/// deles zera o buffer -- o outro faz `BPBuffer -= gap` DEPOIS da soma, deixando o buffer
	/// NEGATIVO, e na chamada seguinte esse negativo e somado ao gap (o acumulado e dado e
	/// retomado). Como o buffer inteiro ja foi gasto, aqui ele simplesmente zera. E defeito de
	/// contabilidade, nao formula de balanco.
	///
	/// Mexer nos contadores faz parte do contrato: qualquer ganho pago RESETA o `Gaintimer`
	/// (50 ticks) e zera o `Buffertimer`, que e o que faz o acumulador de ocio so voltar a
	/// encher depois de um tempo sem ganhar nada. Ver <see cref="BufferTick"/>.
	/// </summary>
	public double CapCheck(double gap)
	{
		if (relBPmax == 0) return 0;

		Gaintimer = 50;
		Buffertimer = 0;
		gap *= StamBPGainMod;

		// retorno decrescente no topo: sem isto o ganho por tick e proporcional ao BP, e nos
		// bilhoes um unico tick adicionava bilhoes
		if (BpGainSoftcap > 0 && BpGainSoftStrength > 0 && BP > BpGainSoftcap)
			gap *= DmMath.Pow(BpGainSoftcap / BP, BpGainSoftStrength);

		if (BPBuffer > 0)
		{
			gap += BPBuffer;
			BPBuffer = 0;
		}

		double check = relBPmax - (gap + BP);
		if (check > 0) return Math.Max(Math.Min(gap * GainsRate, (relBPmax - BP) * 1.25), 0);
		return Math.Max(0, relBPmax - BP);
	}
}
