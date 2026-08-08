using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Core.Stats;

/// <summary>
/// TUDO que entra na conta de poder de um personagem. E o `mob` do BYOND reduzido ao que
/// as duas contas grandes leem: <see cref="Statify"/> (stat cru -> stat efetivo) e
/// <see cref="PowerLevel"/> (BP base -> BP expresso).
///
/// OS NOMES SAO OS DO DM DE PROPOSITO. `ssjBuff`, `nnetBuff`, `Ephysoff`, `bp_remove` --
/// nenhum foi "arrumado" pra convencao de C#. A razao e pratica: essas duas contas tem
/// dezenas de fatores e a ordem entre eles (multiplica / soma na BASE / aplica no FIM) e
/// justamente o que nao pode sair errado. Com os nomes iguais da pra por o `base.dm` do
/// lado e conferir linha a linha; renomeando, nao da.
///
/// Fica no Core (assembly puro, sem Godot): servidor e cliente rodam a MESMA conta, que e
/// o que "cliente calcula, servidor valida" exige.
/// </summary>
public sealed partial class Fighter
{
	// =====================================================================
	// IDENTIDADE
	// =====================================================================
	public string Name = "";
	public string Race = "";
	public string Class = "None";
	public string SaiyanLineage = "";

	// =====================================================================
	// BP e vida
	// =====================================================================
	public double BP = 1;             // o poder REAL, o que o treino aumenta
	public double BPMod = 1;          // multiplicador racial/de classe (vem do proto)
	public double BPadd = 0;          // soma que nao vira BP real
	public double HP = 100;
	public double Ki = 100;
	public double MaxKi = 100;
	public double stamina = 100;
	public double maxstamina = 100;
	public double staminadeBuff = 100;
	public bool dead, KO, IsInFight, dashing, isconcealed;

	/// <summary>
	/// `flightability`: a proficiencia de voo, 1 a 100. DIVIDE o custo de Ki de voar.
	///
	/// MORA NA FICHA e nao no <c>ServerPlayer</c> por um motivo so: a ficha inteira e serializada
	/// no save (ver <c>CharacterSave.Ficha</c>), entao um numero que o jogador CONQUISTA voando
	/// persiste de graca. Deixado no jogador vivo, ele zeraria no logout -- e a familia de defeito
	/// de "buff persistente que morre no relog" ja custou caro neste projeto.
	///
	/// Comeca em 1, que no original nao e "1% de pericia": e a sentinela de quem NUNCA aprendeu
	/// (ver <c>Voo.HabilidadeSemVoo</c>).
	/// </summary>
	public double flightability = 1;

	// O `Apeshitskill` (dominio sobre o Oozaru, 0 a 10) MORAVA AQUI e foi deletado: o Oozaru passou
	// a usar o livro de maestrias das outras formas, por id de catalogo. Ver o bloco "O
	// `Apeshitskill` FOI DELETADO" em `Core/Forms/Oozaru.cs`.

	// =====================================================================
	// STATS CRUS (vem do genoma) e seus tres canais de modificacao
	//   Mod  = MULTIPLICA (transformacoes, itens permanentes)
	//   Buff = SOMA (efeitos que ligam e desligam)
	//   T*   = SOMA temporaria, morre no logout
	// O DM separa os tres justamente pra um "x2 depois +5 depois /2" nao virar perda liquida.
	// =====================================================================
	public double physoff = 1, physdef = 1, technique = 1;
	public double kioff = 1, kidef = 1, kiskill = 1, speed = 1, magiskill = 1;

	public double physoffMod = 1, physdefMod = 1, techniqueMod = 1;
	public double kioffMod = 1, kidefMod = 1, kiskillMod = 1, speedMod = 1, magiMod = 1;

	public double physoffBuff = 0, physdefBuff = 0, techniqueBuff = 0;
	public double kioffBuff = 0, kidefBuff = 0, kiskillBuff = 0, speedBuff = 0, magiBuff = 0;

	public double Tphysoff = 1, Tphysdef = 1, Ttechnique = 1;
	public double Tkioff = 1, Tkidef = 1, Tkiskill = 1, Tspeed = 1, Tmagi = 1, Tmagimon = 1;

	/// <summary>Estilo de luta ativo: multiplica cada stat (Style.dm, tudo 1 sem estilo).</summary>
	public double physoffStyle = 1, physdefStyle = 1, techniqueStyle = 1;
	public double kioffStyle = 1, kidefStyle = 1, kiskillStyle = 1, speedStyle = 1, magiStyle = 1;
	public double kiregenStyle = 1, staminadrainStyle = 1;

	// =====================================================================
	// SAIDA de Statify -- R = cru composto, E = depois da curva de retorno decrescente
	// =====================================================================
	public double Rphysoff = 1, Rphysdef = 1, Rtechnique = 1;
	public double Rkioff = 1, Rkidef = 1, Rkiskill = 1, Rmagiskill = 1, Rspeed = 1;

	public double Ephysoff = 1, Ephysdef = 1, Etechnique = 1;
	public double Ekioff = 1, Ekidef = 1, Ekiskill = 1, Emagiskill = 1, Espeed = 1;
	public double Epspeed = 1, Eactspeed = 20, Esuperkiarmor;
	public double speedDIFF = 1, dashingMod = 1, staminapercent = 1, statstamina = 1;
	public double Ewillpower = 1, Estaminamod = 1, MagicCap = 2000, KIregen;
	public double MaxAnger = 120, kicapacity = 1.25, powerupcap = 1.6, baseKiMax = 100;

	// =====================================================================
	// KI e vontade
	// =====================================================================
	public double baseKi = 100, KiMod = 1, kiAmp = 1, trueKiMod = 1, TMaxKi = 1;

	/// <summary>
	/// ============================ O TETO DE KI QUE CADA FORMA DA ============================
	/// No BYOND a escada inteira levanta o teto de Ki enquanto esta ativa, via `trueKiMod`
	/// (`supersaiyanbuff.dm:69-73`, `lssjbuff.dm:151-153`). O port nao tinha nada disso: as 36
	/// entradas do catalogo valiam 1,0x.
	///
	/// SAO CAMPOS, E NAO CONSTANTES NO `FormaDef`, porque no original eles sao `mob/var` que SKILLS
	/// reescrevem -- `heran.dm:135-136` poe `ssjenergymod = 3` e `ssj2energymod = 4`;
	/// `saiyan legendary.dm:57` faz `lssjenergymod *= 1.5`, levando LSSJ3/4 a 6x. Congelar o numero
	/// no catalogo desligaria essas skills EM SILENCIO, que e o pior modo de falha deste projeto.
	///
	/// Quem escolhe qual deles vale pra cada forma e `Catalogo.TetoDeKi`, derivado de (Linha,
	/// Ordem) -- irmao de `Folha` e `NasceDaRaiva`.
	/// ================================================================================
	/// </summary>
	public double ssjenergymod = 2, ssj2energymod = 3, ssj3energymod = 3.5, ssj4energymod = 4.5;

	/// <summary>Os tres da linha Legendary: Wrathful, C-Type e Legendary pra cima.</summary>
	public double rssjenergymod = 1.3, ussjenergymod = 2, lssjenergymod = 4;
	public double KiUnlockPercent = 0.1, kicapacityMod = 1, kicapacity_remove = 1;
	public double kiregenMod = 1, basekiregen = 1, Tkiregen = 1;
	public double kicirculationskill, kigatheringskill, kicontrolskill;
	public double TwillpowerMod = 1, willpowerMod = 1;

	/// <summary>
	/// AS DUAS CHAVES DA TECLA C, e elas sao DIFERENTES -- e o dono chamou a atencao pra isso.
	///
	///   <c>MeditateGivesKiRegen</c>  Ki Unlocked (`Mind.dm:79`). Sem ela, segurar C nao faz NADA:
	///                                quem nunca sentiu o proprio ki nao tem o que reunir. E a
	///                                unica das duas que nasce ligada -- a criacao a DESLIGA
	///                                (`CharacterCreation.dm:87`), entao todo mundo comeca sem.
	///   <c>canPower</c>              Basic Ki Control NIVEL 5 (`Mind.dm:281`), o "controle de ki
	///                                bom". E ela que libera o power-up de verdade: carga mais
	///                                rapida, `powerMod` de volta a 1 e -- so ela -- passar dos
	///                                100% de Ki, que e de onde sai o buff de BP.
	///
	/// SAO `double` PORQUE O CANAL DE EFEITO E DOUBLE. Todo efeito de skill entra por reflexao
	/// (<c>EfeitosDeSkill</c> / <c>NiveisDeSkill</c>) e escreve `double`; um `bool` aqui seria um
	/// campo que o extrator ENXERGA no DM e nao consegue escrever -- e falharia calado, que e o
	/// pior jeito de falhar. `!= 0` e ligado.
	/// </summary>
	public double MeditateGivesKiRegen, canPower;

	/// <summary>
	/// Carga acumulada da sessao de power-up (`extracharge`, tetado em 50 no DM). Ela nao entra em
	/// formula nenhuma do original -- e um CONTADOR de quanto se carregou, que outras coisas leem.
	/// Guardado porque zerar a cada tick esconderia esforco que o jogo mede.
	/// </summary>
	public double extracharge;

	/// <summary>
	/// Ja passou do teto seguro e ESTA pagando por isso. No DM (`Power Control.dm:118-130`) esta
	/// var desarma o dano enquanto o Ki desce sozinho: sem ela, quem ultrapassa leva dano em todo
	/// tick ate voltar, em vez de vazar energia e se estabilizar.
	/// </summary>
	public bool overcharge;
	public double staminagainMod = 1, satiationMod = 1;

	/// <summary>
	/// O TANQUE DE COMIDA -- `currentNutrition` do DM. E daqui que o vigor volta.
	///
	/// Ver <see cref="Nutricao"/>: o port tinha o vigor caindo e subindo do nada, porque a metade
	/// do sistema que o ALIMENTA nunca foi portada.
	/// </summary>
	public double CurrentNutrition = Nutricao.TanqueInicial;

	/// <summary>
	/// QUAO RAPIDO ESTE CORPO DIGERE. Estica o tanque e acelera as passadas do estomago; Namekuseijin
	/// e Saiyajin Primal tem 2 no original.
	/// </summary>
	public double Metabolism = 1;

	/// <summary>O corpo ja avisou que precisa comer? So pra o aviso nao repetir a cada passada.</summary>
	public bool Hungry;

	/// <summary>
	/// OS DOIS CONTADORES QUE ABREM ARVORE. Cada skill de base do corpo soma 1 em um deles, e e a
	/// SOMA que destrava arvore nova (`Body.dm:24-32`): mais de 2 de `bodyreadiness` abre
	/// Bodybuilding, mais de 2 de `bodyskill` abre Martial Skill (e Weapons Expert com arma), e 2
	/// de cada abre Cultivation.
	///
	/// EU JA DEI ESTES DOIS COMO CODIGO MORTO -- e estava errado. A varredura que fiz filtrava as
	/// linhas com `savant.` pra separar escrita de leitura, e com isso apagou justamente as
	/// leituras (`if(savant.bodyskill>2)`). Sao o gate de QUATRO arvores: sem eles, metade da
	/// progressao fisica do jogo fica inalcancavel e nada na tela diz por que.
	/// </summary>
	public double bodyskill, bodyreadiness;

	/// <summary>O estilo de luta ATIVO, pelo id do catalogo. Vazio = nenhum.</summary>
	public string EstiloAtual = "";

	/// <summary>Maestria por estilo, e o teto pessoal de cada um. Persistem no save.</summary>
	public Dictionary<string, double> MaestriaDeEstilo = new(StringComparer.Ordinal);
	public Dictionary<string, double> TetoDeEstilo = new(StringComparer.Ordinal);

	/// <summary>Sabe usar arma -- com `bodyskill` acima de 2, e o que abre a arvore de armas.</summary>
	public double weaponeq;

	/// <summary>
	/// O QUE AS SKILLS APRENDIDAS ESTAO SOMANDO AGORA (campo -> quanto). Guardado pra que
	/// reaplicar seja idempotente: <see cref="Skills.EfeitosDeSkill.Aplicar"/> desconta isto
	/// antes de somar de novo, senao cada login empilharia os buffs mais uma vez.
	/// </summary>
	public Dictionary<string, double> BuffsDeSkill = new(StringComparer.Ordinal);

	/// <summary>Os fatores MULTIPLICATIVOS aplicados agora. Desfeitos por divisao, nao subtracao.</summary>
	public Dictionary<string, double> MultsDeSkill = new(StringComparer.Ordinal);

	/// <summary>As flags/contadores ESCRITOS agora. Voltam a zero quando a skill sai.</summary>
	public Dictionary<string, double> FlagsDeSkill = new(StringComparer.Ordinal);
	public double concealeddeBuff = 1, concealedBuff = 1;
	public double kiarmor, superkiarmor, superkiarmorMod = 1;
	public double actspeed = 20, mana_cap_mod = 1;
	public double phys_remove = 1, bp_remove = 1;

	// =====================================================================
	// RAIVA
	// =====================================================================
	public double Anger = 100, baseAnger = 100, angerMod = 1;
	/// <summary>Skill Legendary Anger: +100 aqui = +1x no teto de raiva (2x -> 3x).</summary>
	public double legendaryAngerBonus = 0;

	// =====================================================================
	// BUFFS QUE MULTIPLICAM (o "formBuff" e o "buffBuff" do powerlevel)
	// =====================================================================
	public double ssjBuff = 1;        // escada Super Saiyajin
	public double transBuff = 1;      // Oozaru, formas de Icer, etc
	public double formsBuff = 1;      // slot generico de forma
	public double gateBuff = 1;       // Portao do Inferno
	public double HellstarBuff = 1;   // Estrela Makyo
	/// <summary>
	/// O UNBOUND EGO (`ue_ego_mult`): multiplicador de BP pelos membros FERIDOS.
	///
	/// ============================ ELE ESTAVA DECLARADO E ORFAO ============================
	/// O campo nasceu no porte da ficha e NINGUEM o lia -- nem o `PowerLevel()`, nem o servidor.
	/// Um multiplicador que nunca entra na conta e indistinguivel de multiplicador nenhum: o
	/// Unbound Ego "existia" e valia exatamente 1x. Agora o `PowerLevel()` o soma na base, junto
	/// do Mistico, e quem o escreve e o `TickDoUnboundEgo`.
	/// ==================================================================================
	///
	/// Campo PROPRIO e nao um dos `*Pcnt` porque ele e reescrito por INTEIRO a cada tique (o corpo
	/// muda o tempo todo): somar num campo compartilhado o faria empilhar consigo mesmo, e
	/// colidiria com o Mistico, que e de outra linhagem e pode coexistir.
	///
	/// 1 = sem bonus. Ver `Disciplinas.UnboundEgo`.
	/// </summary>
	public double ue_ego_mult = 1;
	public double expandBuff = 1, giantFormbuff = 1, ArtifactsBuff = 1, eyeBuff = 1;
	public double BPBoost = 1;        // Ascensao
	public double powerMod = 1;       // Controle de Poder (pode ser < 1)
	public double OozaruBuff = 1;

	// =====================================================================
	// BUFFS QUE SOMAM NA BASE (additiveBoost) -- ver o comentario em PowerLevel
	// =====================================================================
	public double FuseDanceMod = 1, FPotaraMod = 1;
	public double MysticPcnt = 1, MajinPcnt = 1, aurasBuff = 1;

	public double KaioPcnt = 1;             // o Kaio-ken VIVO (a maestria e outro numero)
	public double ParanormalBPMult = 1;     // Vampiro / Lobisomem

	// =====================================================================
	// DEBUFFS e estado
	// =====================================================================
	public double weight = 1, BPrestriction = 1, splitformdeBuff = 1;
	public double splitformMastery = 0, splitformCount = 0;

	/// <summary>
	/// A IDADE EM ANOS. Mora na ficha porque e a ficha que calcula poder.
	///
	/// Ela ja existia no `ServerPlayer.Idade`, mas so pra ser mostrada e salva -- a conta de BP
	/// nao a alcancava, e por isso <see cref="AgeDiv"/> ficava em 1 pra sempre.
	/// </summary>
	public double Idade = 18;

	/// <summary>
	/// O MULTIPLICADOR DE PODER POR IDADE, recalculado a cada `PowerLevel`.
	///
	/// ============================ ELE ERA UM 1 DECORATIVO ============================
	/// O campo existia, entrava no `netBuff` e NUNCA era escrito: nascia 1 e morria 1. O efeito
	/// pratico e que a idade nao fazia nada -- um Saiyajin de 8 anos e um de 800 batiam igual, e a
	/// caixa de idade da criacao era enfeite. Ver `Envelhecimento.DivisorDeIdade`.
	/// =================================================================================
	/// </summary>
	public double AgeDiv = 1;

	// =====================================================================
	// GRAVIDADE
	// =====================================================================
	public double GravMastered = 1, Planetgrav = 1, gravmult = 0;
	public double gravFelt = 1, gravBuff = 1;

	// =====================================================================
	// SOMAS DIRETAS (zeni cru, nao razao -- nunca vire "Nx" numa UI)
	// =====================================================================
	public double HVBPExpAdd, MagAdd, TMagAdd;
	public double FuseBuff, HVBPAdd, CooldownAmount, majin_absorb_bp, AbsorbBP;
	public bool AbsorbDeterminesBP, HVBPAddEnd;

	// =====================================================================
	// GOD KI
	// =====================================================================
	public GodKiState? godki;
	public bool godki_gt_mode;
	public double godki_boost = 2.5, gt_boost = 3, godki_give_mult = 0;
	public double ssj = 0, lssj = 0;
	public bool beast_form, MysticActive;

	// =====================================================================
	// CASOS ESPECIAIS DE FIM DE CONTA
	// =====================================================================
	/// <summary>Revive por Zeni: BP em 25% ate este instante (em ms de relogio real).</summary>
	public long zeni_revive_debuff_until;
	public bool bio_lab_born;
	public int bio_stage;
	/// <summary>Vazamento do Frost mutante: abaixo de 1 o poder escapa ate o piso.</summary>
	public double fd_release = 1;

	// =====================================================================
	// GANHOS
	// =====================================================================
	public double UPMod = 1;          // Potencial da raca: define o teto pessoal de treino
	public double relcaprate = 1, HBTCMod = 1, bgains = 1, tgains = 1, tailgain = 1;
	public double hiddenpotential = 1, hdnptltoBP = 1;
	public double BoostMult = 0;
	public bool isHV, BoostActive;

	/// <summary>Multiplicadores de RITMO que vem do genoma (misc_stats).</summary>
	public double TrainMod = 1, MedMod = 1, SparMod = 1, GravMod = 1;

	/// <summary>Multiplicador de MARCO de poder: so sobe ao romper um patamar da propria raca.</summary>
	public double bp_milestone_mult = 1;
	public HashSet<string> bp_milestones_done = new(StringComparer.Ordinal);

	public double StamBPGainMod = 1;

	/// <summary>Ganho acumulado enquanto o personagem NAO treina, pra quem interpreta nao ficar pra tras.</summary>
	public double BPBuffer = 0;
	public int Gaintimer = 0, Buffertimer = 0;

	/// <summary>
	/// Ritmo da ZONA. Sala do Tempo multiplica por 280 (1 dia la = 1 ano), Dimensao Mental
	/// divide por 4. O servidor escreve isto na troca de zona.
	/// </summary>
	public double zoneGainMult = 1;

	// --- estado do laco de treino (o que o BYOND guardava em tmp) ---------
	public bool train, med, minuteshot, train_med_to_hp;
	public int missedtrain, minuteshot_ig_ki;
	public double tmp_activ_gains;
	public Facing lastdir = Facing.South, dir = Facing.South;

	// --- peso ------------------------------------------------------------
	public double Weighted = 0;        // quanto de peso o personagem esta vestindo
	public double weight_ratio = 0;
	public double weight_cap_hw = 0;   // RECORDE do corpo: nunca cai (senao vira espiral)

	// --- Zenkai ----------------------------------------------------------
	public long zenkaiReady;           // relogio real (ms) em que o proximo Zenkai libera
	public double ssj3at = 2e10;       // BP de referencia do SSJ3 -- teto de aposentadoria do Zenkai
	public double ssj3LearnReq = 0;    // requisito pessoal, quando ja sorteado
	public string ParentRace = "";
	public bool canSSJ;

	/// <summary>O genoma que gerou este lutador. Usado pra saber a fracao de sangue Saiyajin.</summary>
	public Races.Genome? Genoma;

	// =====================================================================
	// RESULTADO
	// =====================================================================
	public double expressedBP;        // o numero que o mundo LE (scouter, dano, ranking)
	public double relBPmax;           // teto pessoal de treino
	public double peakexBP;           // o que seria sem idade/peso/ferimento
	public double Egains = 1;         // multiplicador de ganho de treino
	public double expressedAdd;

	/// <summary>
	/// Razoes de estado do corpo. Comecam em 1 (corpo inteiro) -- no DM sao `tmp` sem valor
	/// inicial, ou seja ZERO ate o primeiro powerlevel rodar, e o Statify le elas um tick
	/// antes de existirem. Zero ali nao e valor de projeto, e variavel nao inicializada.
	/// </summary>
	public double kiratio = 1, hpratio = 1, staminaratio = 1;
	public double buffBuff, formBuff, deBuff, statusBuff, netBuff, nnetBuff = 1;
	public double fusionBuff = 1, angerBuff = 1;

	/// <summary>
	/// Um tick completo, na ORDEM do GlobalStats do BYOND: primeiro os stats, depois o poder.
	/// A ordem importa -- o Statify recalcula MaxKi, e o PowerLevel divide o Ki por ele.
	/// </summary>
	public void Tick(double gravBalance = 1, long agoraMs = 0)
	{
		Statify();
		ClampAnger();
		PowerLevel(gravBalance, agoraMs);
		WeightTick();   // depende do expressedBP que o PowerLevel acabou de escrever
	}

	/// <summary>
	/// Prende a raiva na faixa valida. No BYOND isto mora no laco Stats(), junto com o
	/// decaimento e os rotulos de humor (que sao do sistema de raiva, outra etapa) -- mas o
	/// CORTE precisa vir junto da conta de poder: sem ele, qualquer coisa que escreva um
	/// Anger alto vira multiplicador de BP direto, que foi exatamente a anomalia do 20x.
	/// Depende do MaxAnger, entao roda depois do Statify.
	/// </summary>
	public void ClampAnger() => Anger = DmMath.Clamp(Anger, 100, MaxAnger);

	// =====================================================================
	// SEMENTE
	// =====================================================================
	/// <summary>
	/// Monta um lutador a partir do genoma ja composto -- a ponte entre a Etapa 3 (racas) e
	/// esta. As chaves sao as do proto do DM; quem nao vier no bloco fica no default.
	/// </summary>
	public static Fighter FromGenome(Genome genoma, StatBlock bloco, double bp, string nome = "")
	{
		var f = new Fighter
		{
			Name = nome,
			Race = genoma.MajorityRace,
			Class = genoma.Class,
			BP = Math.Max(bp, 1),

			physoff = bloco.Stats.GetValueOrDefault("Physical Offense", 1),
			physdef = bloco.Stats.GetValueOrDefault("Physical Defense", 1),
			kioff = bloco.Stats.GetValueOrDefault("Ki Offense", 1),
			kidef = bloco.Stats.GetValueOrDefault("Ki Defense", 1),
			kiskill = bloco.Stats.GetValueOrDefault("Ki Skill", 1),
			technique = bloco.Stats.GetValueOrDefault("Technique", 1),
			speed = bloco.Stats.GetValueOrDefault("Speed", 1),
			magiskill = bloco.Stats.GetValueOrDefault("Esoteric Skill", 1),

			KiMod = bloco.Stats.GetValueOrDefault("Energy Level", 1),
			BPMod = bloco.Stats.GetValueOrDefault("Battle Power", 1),

			basekiregen = bloco.Misc.GetValueOrDefault("Ki Regeneration", 1),
			baseAnger = 100 * bloco.Misc.GetValueOrDefault("Anger", 1),

			// POTENCIAL -> teto pessoal de treino. Vale repetir porque e facil de errar: o
			// relBPmax e `BP * (1 + UPMod) * ...`, entao UPMod ZERO deixa o teto colado no BP
			// atual e o personagem NAO TREINA. O default do DM e 1, e a raca sobrescreve.
			UPMod = bloco.Misc.GetValueOrDefault("Potential", 1),

			TrainMod = bloco.Misc.GetValueOrDefault("Train Mod", 1),
			MedMod = bloco.Misc.GetValueOrDefault("Med Mod", 1),
			SparMod = bloco.Misc.GetValueOrDefault("Spar Mod", 1),
			GravMod = bloco.Misc.GetValueOrDefault("Gravity Mod", 1),

			Genoma = genoma,
		};

		// o DM protege o ascBPmod contra 0; o mesmo vale pros mods que dividem contas
		if (f.BPMod <= 0) f.BPMod = 1;
		if (f.KiMod <= 0) f.KiMod = 1;

		// Nasce DESCANSADO. O Ki maximo depende do "Energy Level" da raca, entao deixar o Ki
		// no 100 fixo faria um Saiyajin (KiMod 1,4) nascer com 71% de energia -- e um Ki
		// parcial derruba o BP expresso na hora (o kiratio entra no pacote de estado).
		f.Tick();
		f.Ki = f.MaxKi;
		f.Tick();
		return f;
	}
}

/// <summary>
/// O pedaco do datum de God Ki que a conta de poder LE. O motor (maestria, energia,
/// despertar) e outra etapa -- aqui so entra o que vira multiplicador.
/// </summary>
public sealed class GodKiState
{
	public bool usage;        // esta usando agora
	public bool awakened;     // ja despertou alguma vez
	public double mastery;    // 0-100%: destrava Blue (33), Royale/Beast (50), UI/Destruicao (80)
	public double godki_mult = 1;
	public double transform_adjust = 1;
	public bool adjust_me;
	public double efficiency = 1, energy = 100;

	public const double BluePct = 33;
	public const double RoyalePct = 50;
}
