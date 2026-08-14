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

		// A GRAVIDADE SAI DAQUI COMO DIVISOR DE CONDICAO. Ver o bloco que a calcula la embaixo --
		// nasce em 1 porque o ramo do poder escondido nao passa pelas somas da familia 2.
		gravDiv = 1;

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
			// O UNBOUND EGO soma na BASE, como o Mistico -- `ue_ego_mult` e 1 quando nao ha bonus.
			// Ver a taxonomia de buffs: multiplicar aqui em vez de somar faria o bonus por membro
			// ferido compor com a forma divina de 66x e explodir a escala.
			tempBP += AdditiveBoost(ue_ego_mult);
			tempBP += AdditiveBoost(MajinPcnt);
			tempBP += AdditiveBoost(gravBuff);
			tempBP += AdditiveBoost(aurasBuff);
			tempBP += AdditiveBoost(KaioPcnt);
			tempBP += AdditiveBoost(ParanormalBPMult);

			// ============================ A GRAVIDADE VIRA DIVISOR DE CONDICAO ============================
			// O `peakexBP` la embaixo divide o poder pelos fatores de CONDICAO pra responder "quanto do
			// meu pico eu estou expressando". So que ele so sabia dividir por fator MULTIPLICATIVO, e a
			// gravidade e familia 2 -- soma na base. Resultado medido: com gravidade 100 o poder cai pra
			// 46% e a conta continuava dizendo "100% inteiro".
			//
			// O dono citou gravidade junto de peso e Ki ao definir a %, e ele esta certo. Aqui ela vira
			// uma RAZAO entre a base real e a base que este mesmo corpo teria sem o peso do planeta --
			// exata, porque tudo que vem depois (familia 1, `nnetBuff`, raiva, `powerMod`) e
			// multiplicativo e se cancela na divisao.
			//
			// SO QUANDO ELA PESA (`gravBuff < 1`). Gravidade dominada e bonus de verdade e entra dos DOIS
			// lados, deixando a razao onde estava: quem treinou pra aguentar 100g nao esta "machucado".
			double baseSemGrav = tempBP - AdditiveBoost(gravBuff) + AdditiveBoost(Math.Max(gravBuff, 1));
			gravDiv = baseSemGrav > 0 ? Math.Min(tempBP / baseSemGrav, 1) : 1;

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

		// O QUE O PERSONAGEM SERIA SEM IDADE, PESO, FERIMENTO E GRAVIDADE -- o "poder de pico".
		//
		// A DIVISAO E O QUE FAZ A FORMA SUMIR DA CONTA, e e essa a propriedade que interessa: SSJ,
		// God Ki e raiva multiplicam os DOIS lados, entao a razao `expressedBP / peakexBP` nao se
		// mexe ao transformar. So os fatores de condicao ficam de pe -- que e exatamente como o dono
		// definiu a "% de BP efetivo".
		//
		// ============================ O PISO FICA, MAS A % NAO SAI MAIS DAQUI ============================
		// O `Math.Max(..., expressedBP)` e HERANCA FIEL DO DM e tem consumidor la: `buudead` e
		// `peakexBP/expressedBP` (`misc.dm:194/204/212`) -- um multiplicador de recuperacao de Buu que
		// TEM que ser >= 1, e o zumbi nasce com este numero (`Death.dm:44/63`). Tirar o piso plantaria
		// um `buudead < 1` pra quando essas duas vias forem portadas.
		//
		// SO QUE ELE ESTRANGULAVA A % DE BP EFETIVO, e o dono esta certo na queixa: a partir de
		// `statusBuff > 1` (Ki acima de 100%) o `Math.Max` troca de ramo, o pico vira o proprio
		// expresso e `expressedBP / peakexBP` sai 1,0000 EXATO -- por construcao, nao por
		// arredondamento. Medido: 110%, 118%, 150% e 200% de Ki davam todos "100% efetivo".
		//
		// A % MUDOU DE CONTA em vez de o piso sair (ver `Inteireza`): o pico continua sendo o pico do
		// DM, e a % passou a ser o produto dos fatores de condicao, que e o que ela sempre VALEU.
		// ================================================================================================
		peakexBP = Math.Max(expressedBP / Math.Max(AgeDiv * deBuff * statusBuff * gravDiv, 1e-9), expressedBP);

		Egains = HBTCMod * Trainmult * bgains * tgains * tailgain;
		if (isHV && BoostActive && BoostMult != 0) Egains *= BoostMult;
	}

	/// <summary>
	/// QUAO INTEIRO ESTE CORPO ESTA. A "% de BP efetivo" que o dono pediu -- e ELA PASSA DE 100%.
	///
	/// ============================ POR QUE ELA DEIXOU DE SAIR DE `expressedBP / peakexBP` ============================
	/// Era essa a conta, e ela batia num TETO DURO: o `peakexBP` tem um `Math.Max(..., expressedBP)`
	/// que e do DM e que la tem consumidor (ver o comentario dele em `PowerLevel`). Com Ki acima de
	/// 100% o `statusBuff` passa de 1, o `Math.Max` troca de ramo e a razao devolve 1,0000 exato --
	/// medido em 110%, 118%, 150% e 200% de Ki, todos "100% efetivo".
	///
	/// O dono: *"o bp efetivo pode SUBIR caso eu tenha um ki acima de 100%"*. Entao a % passou a sair
	/// do PRODUTO DOS FATORES DE CONDICAO -- que e literalmente o que a razao antiga VALIA quando o
	/// piso nao mordia (o comentario antigo ja dizia isso: `min(..., 1)`). Tirou-se o `min`, e mais
	/// nada mudou de significado. O `peakexBP` ficou intacto pro consumidor do DM.
	///
	/// DE QUEBRA A CONTA FICOU EXATA: a razao passava pelo `DmMath.Round` do `expressedBP`, e por
	/// causa dele um corpo perfeitamente inteiro com Ki cheio media 98,0% em vez de 100%.
	/// =============================================================================================================
	///
	/// Ki, vida, estamina, idade, peso/restricao e gravidade -- e mais nada. TRANSFORMAR NAO A MEXE,
	/// porque nenhum fator de forma entra neste produto; e essa invariancia e a definicao que o dono
	/// deu, de graca.
	///
	/// DUAS COISAS QUE ELA NAO ENXERGA, e e honesto dize-las:
	///   - a perna da ESTAMINA esta morta na conta do jogo (`staminaratio` le `staminadeBuff`, que
	///     nasce em 100 e nada abaixa hoje) -- quando alguem a acordar, ela entra aqui sozinha;
	///   - o que multiplica o poder DEPOIS (nocaute, debuff do revive, vazamento do Frost) some na
	///     divisao de proposito: aquilo nao e "estar inteiro", e estar cortado. O nocaute ja tem
	///     linha propria na tela.
	///
	/// DIVULGAVEL SEM SCOUTER: e RAZAO, nao poder. Ver o cabecalho de `GameServer.Sigilo`.
	///
	/// O PISO EM ZERO FICA (nenhum fator e negativo, mas um `weight` absurdo levaria o `deBuff` a
	/// zero e a % nao deve virar numero negativo). O TETO SAIU -- era ele a queixa.
	/// </summary>
	/// <remarks>
	/// O `statusBuff > 0` E SENTINELA DE "JA RODOU", nao paranoia: ele e produto de tres razoes com
	/// piso positivo (0,6 x 0,6 x 0,3), entao NUNCA vale zero depois de um `PowerLevel`. Zero so
	/// existe num lutador recem-criado que nunca tiquou -- e pra esse a resposta honesta e 1
	/// (corpo inteiro), que era o que a razao antiga tambem devolvia com `peakexBP` zerado.
	/// </remarks>
	public double Inteireza => statusBuff > 0 ? Math.Max(AgeDiv * deBuff * statusBuff * gravDiv, 0) : 1;

	/// <summary>
	/// O MULTIPLICADOR TOTAL sobre o BP base -- o que o treino virou depois de TUDO.
	///
	/// SAI DO RESULTADO (`expressedBP / BP`) E NAO DA SOMA DAS PARTES, e a diferenca nao e de
	/// arredondamento: neste jogo os fatores tem tres FAMILIAS (ver o cabecalho desta classe), e
	/// so a primeira multiplica. Medido: Kaio-ken 2x com Mistico 2x da 3x de verdade (os dois
	/// SOMAM na base) e o produto ingenuo diria 4x -- 33% de erro; com forma, raiva e gravidade
	/// juntos o erro medido passa de 126%; e o corte de 25% do revive por Zeni nao tem campo
	/// nenhum onde caber num produto de fatores.
	///
	/// Uma quebra por fator na tela e ilustracao bem-vinda, mas o TOTAL sai daqui. Se as duas
	/// discordarem, e a quebra que esta incompleta.
	///
	/// DIVULGAVEL SEM SCOUTER pelo mesmo motivo da <see cref="Inteireza"/>: e razao.
	/// </summary>
	public double MultiplicadorTotal => BP > 0 ? expressedBP / BP : 1;

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
