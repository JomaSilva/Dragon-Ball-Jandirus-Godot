namespace Jandirus.Core.Stats;

/// <summary>
/// O QUE AS TECNICAS ATIVAS PRECISAM DA FICHA: o dreno de Ki e as pericias que o dimensionam.
/// </summary>
public partial class Fighter
{
	// =====================================================================
	// PERICIAS DE KI que dimensionam tecnica (as `*skill` do original)
	// =====================================================================
	/// <summary>Quao forte sao os DEBUFFS que voce aplica (cegueira, paralisia). Cresce usando.</summary>
	public double kidebuffskill;

	/// <summary>
	/// QUAO BEM VOCE APARA ENERGIA -- o `kidefenseskill` do DM, e a unica defesa contra projetil
	/// que nao e stat de ficha.
	///
	/// Entra em TRES lugares da cadeia de ki (ver <see cref="Combat.DanoDeKi"/>): divide o dano por
	/// `log_4(max(kidefenseskill,4))`, divide DE NOVO quando a guarda esta erguida, e multiplica a
	/// chance de defletir por `max(kidefenseskill/10, 1)`. Zerada nao muda nada (log_4(4) = 1) --
	/// entao um personagem novo nao ganha nada de graca, que e a intencao.
	///
	/// CRESCE APANHANDO: `M.kidefensecounter++` a cada blast que encosta, `+= 4` quando a deflexao
	/// barata sai (`objects.dm:353`). E o unico canal de treino do jogo que so avanca com voce do
	/// lado errado do raio.
	/// </summary>
	public double kidefenseskill;

	/// <summary>
	/// Quanto Ki voce DESPERDICA. Entra dividindo em <see cref="BaseDrain"/> -- e o unico canal
	/// que faz tecnica ficar mais BARATA com o tempo, em vez de so mais forte.
	/// </summary>
	public double kiefficiencyskill;

	/// <summary>
	/// AS DUAS PERICIAS DE RAIO, e elas so valem pro <see cref="Combat.TipoDeProjetil.Beam"/>:
	/// `beammods = Ekioff*Ekiskill*log_10(max(kieffusionskill,10))*log_10(max(beamskill,10))`
	/// (`beams.dm:152`). Zeradas os dois logs valem 1 e a formula do raio coincide com a da bola --
	/// que e o estado de quem nunca atirou, e e por isso que as duas nao dao pra fundir numa so.
	///
	/// `beamskill` tambem encurta a CARGA (`beams.dm:33`), entao ela paga duas vezes: o raio sai
	/// mais rapido e mais forte. Crescem disparando (`beamcounter += 3` a cada segmento que
	/// encosta, `objects.dm:311`).
	/// </summary>
	public double beamskill, kieffusionskill;

	/// <summary>
	/// A PERICIA DE BOLA -- irma das de cima, e a que o `blastcounter += 3` alimenta
	/// (`objects.dm:313`). Ela entra no `basedamage` do `Basic_Blast`
	/// (`1*Ekioff*log_10(max(blastskill,10))`, `blasts.dm:63`).
	///
	/// ============================ AS QUATRO ESTAVAM NO DISCO E CAINDO NO CHAO ============================
	/// `kidefenseskill`, `beamskill`, `kieffusionskill` e `blastskill` JA ERAM ESCRITAS pelo extrator:
	/// `Assets/Data/niveis.json` tem 33 degraus que somam nelas (a arvore inteira de Ki Effusion, o
	/// `Ki_Unlocked` nivel 35 -- que e o mesmo degrau que concede o verb `Basic_Blast` --, o
	/// `Advanced_Ki_Effusion`...). Como os campos nao existiam no <see cref="Fighter"/>, o
	/// `EfeitosDeSkill` os jogava em <see cref="Skills.EfeitosDeSkill.Desconhecidos"/> e seguia em
	/// frente: o jogador comprava a skill, lia a mensagem no chat e nao ganhava nada.
	///
	/// E a terceira vez que este projeto acha dado extraido sem consumidor (as 178 animacoes nos 35
	/// atlas, o `canPower`/`expbarrier`). O remedio e sempre o mesmo: existir o campo. Elas continuam
	/// crescendo TAMBEM pelo uso, como no DM -- ver `GameServer.Projeteis.cs`.
	/// =====================================================================================================
	/// </summary>
	public double blastskill;

	/// <summary>
	/// AS SEIS QUE FALTAVAM PRO ARSENAL NOMEADO -- e elas estavam no disco pela QUARTA vez.
	///
	/// ============================ MESMA HISTORIA DAS QUATRO DE CIMA ============================
	/// O bloco acima conta como `blastskill` e as tres irmas dela existiam em `niveis.json` sem ter
	/// campo onde cair. Estas seis estavam no MESMO arquivo e no mesmo estado, e so nao apareceram
	/// naquele conserto porque nenhuma tecnica portada as lia ainda:
	///
	///     volleyskill    10 degraus      targetedskill   9 degraus
	///     guidedskill     9 degraus      homingskill    10 degraus
	///     chargedskill    3 degraus      bonusShots     10 degraus
	///
	/// (contagem sobre `Assets/Data/skills.json` + `Assets/Data/niveis.json`; a arvore de Ki
	/// Effusion inteira soma nelas.) Ate agora todas caiam em
	/// <see cref="Skills.EfeitosDeSkill.Desconhecidos"/>: o jogador subia `Basic_Volley_Mastery`,
	/// lia a mensagem no chat e nao ganhava nada.
	///
	/// O ARSENAL NOMEADO E QUEM AS ACORDA (`GameServer.Tecnicas.G5.cs`):
	///   * `volleyskill` e `bonusShots` decidem QUANTAS bolas saem de uma barragem
	///     (`amount = 8 + bonusShots + round(log(blastskill)) + round(log(volleyskill))`,
	///     `blasts.dm:140`);
	///   * `targetedskill` faz o mesmo pro campo minado e pra Hellzone (`blasts.dm:406`);
	///   * `chargedskill` encurta a carga do raio (`beams.dm:33`) -- ver
	///     <see cref="Combat.Projetil.SegundosDeCarga"/>, que estava lendo a pericia ERRADA;
	///   * `guidedskill` e `homingskill` entram na pontaria do teleguiado
	///     (`blasts/DeathBall.dm:57`, `blasts.dm:65`).
	/// ==========================================================================================
	/// </summary>
	public double volleyskill, targetedskill, guidedskill, homingskill, chargedskill;

	/// <summary>
	/// QUANTAS BOLAS A MAIS por barragem. Nao e pericia: e um contador que degraus de skill somam
	/// direto (`bonusShots` aparece em 10 degraus). Entra somado em toda tecnica de volei.
	/// </summary>
	public double bonusShots;

	/// <summary>
	/// AS TRES QUE FALTAVAM PRO LOTE G6 -- e e a QUINTA vez que este projeto acha campo extraido
	/// sem consumidor.
	///
	/// ============================ MESMA HISTORIA DAS SEIS DE CIMA ============================
	/// O bloco anterior conta como `volleyskill` e as cinco irmas estavam em `niveis.json` sem ter
	/// campo onde cair. Estas tres estavam no MESMO arquivo, no mesmo estado, e nao apareceram
	/// naquele conserto pelo mesmo motivo de sempre: nenhuma tecnica portada as lia ainda.
	///
	///     kiaiskill        8 degraus     kibuffskill     10 degraus
	///     kiawarenessskill 8 degraus
	///
	/// (contagem sobre `Assets/Data/skills.json` + `Assets/Data/niveis.json` -- as arvores
	/// `Basic_Kiai_Mastery`, `Basic_Ki_Circulation` e `Basic_Ki_Awareness` somam nelas.) Ate agora
	/// as tres caiam em <see cref="Skills.EfeitosDeSkill.Desconhecidos"/>: o jogador subia a arvore
	/// de sopro inteira e o sopro nao ficava um grama mais forte.
	///
	/// QUEM AS ACORDA (`Server/GameServer.Tecnicas.G6.cs`):
	///   * `kiaiskill` entra na FORCA e na RECARGA das quatro tecnicas de sopro
	///     (`Ki2.0/Kiai.dm:11,15`) -- e a unica pericia que aparece nos dois lados da conta;
	///   * `kibuffskill` dimensiona o Focus e a Efficiency (`Ki2.0/KiBuffs.dm:24,52`);
	///   * `kiawarenessskill` decide QUANTO o Assess Ki Skill conta sobre o outro -- tres degraus
	///     de leitura em `KiStatsModule.dm:88,95,127`, de "ele parece mais forte" ate a planilha.
	/// ==========================================================================================
	/// </summary>
	public double kiaiskill, kibuffskill, kiawarenessskill;

	/// <summary>
	/// AS TRES CHAVES QUE OS `growbranches()` LEEM e que o port nao tinha onde guardar -- a SEXTA
	/// vez do mesmo achado: o extrator ja as escrevia (`effusionspecial=1` no degrau 25 da Basic Ki
	/// Effusion em `niveis.json`; `effspec=1..3` e `gravitate=1..2` nos `after_learn` de
	/// `Effusion Specialization.dm:63-105` e `Spirit.dm:101-134`) e as tres caiam em
	/// <see cref="Skills.EfeitosDeSkill.Desconhecidos"/>.
	///
	/// O que cada uma abre (as regras estao em `skills.json`, extraidas do DM):
	///   * `effusionspecial == 1` -> a arvore Effusive Specialty (`Effusion.dm:22-23`);
	///   * `effspec == 0`         -> as tres especialidades a escolher (Ki Shock, Interference,
	///                              Forceful Ki -- `Effusion Specialization.dm:18-23`); escolher uma
	///                              escreve 1..3 e as outras duas APAGAM. E a exclusividade do DM;
	///   * `gravitate`            -> a Greater Will (`Spirit.dm:26-27`), depois de Upper ou Lower.
	/// </summary>
	public double effusionspecial, effspec, gravitate;

	/// <summary>
	/// A PERICIA DO FULL POWER (`Buffs/racial/grays.dm:41-45`), e ela e do CINZA e de mais ninguem.
	///
	/// Nasce 1 no `after_learn()` do DM (`:63`) e sobe 0,5 por tique enquanto a forma esta de pe,
	/// ate 100. Ela so DIVIDE o custo (`round(100/jirenskill,1)*BaseDrain`), entao o valor de
	/// fabrica 1 e o pior caso -- quem nunca segurou a aura paga o preco cheio. Comeca em 1 e nao
	/// em 0 porque zero seria divisao por zero, que e literalmente o que o DM evita ao escrever o
	/// `= 1` no aprendizado.
	/// </summary>
	public double jirenskill = 1;

	/// <summary>
	/// OS DOIS DA ARVORE DO ESPIRITO (`Core Trees/Spirit.dm:294-341`) -- e e a SEXTA vez que este
	/// projeto acha campo extraido sem consumidor.
	///
	/// ============================ ELES JA ESTAVAM NO DISCO, COMO OS ONZE DE CIMA ============================
	/// `niveis.json` ja carrega os degraus do `/datum/skill/Spirit_Ball` escritos como MULTIPLICADOR
	/// (`"mults": ["SpiritBallCost=0.5", "SpiritBallDamage=2"]` no nivel 1;
	/// `"SpiritBallCost=0.5", "SpiritBallDamage=1.25"` no nivel 2). Sem campo onde cair, os dois iam
	/// direto pra <see cref="Skills.EfeitosDeSkill.Desconhecidos"/>: o jogador subia a arvore inteira
	/// do Espirito e o Spirit Gun continuava custando o mesmo e batendo o mesmo.
	///
	/// OS VALORES DE FABRICA SAO OS DO DM (`Spirit.dm:340-342`: `SpiritBallCost = 2`,
	/// `SpiritBallDamage = 1`), e nao 1 e 1 -- o custo NASCE dobrado e o primeiro degrau da skill o
	/// corta pela metade. Escrever 1 aqui faria o Spirit Gun de estreia custar metade do que o
	/// original cobra, e o degrau 1 deixaria de ser um degrau.
	///
	/// O `SpiritBallDamage` entra em DOIS lugares no verb (`:353` e `:361`): no `basedamage` da bola e
	/// no `BP` que ela carrega (`passbp = expressedBP * SpiritBallDamage`). Nao e engano de leitura --
	/// e o que faz a tecnica ficar mais dificil de defletir junto com o dano, ver
	/// <see cref="Combat.ReceitaDeProjetil.MultDeOnda"/>.
	/// ======================================================================================================
	/// </summary>
	public double SpiritBallCost = 2, SpiritBallDamage = 1;

	/// <summary>
	/// OS DOIS DO PUNHO ESPIRITUAL -- `mob/var/SpiritFistCost = 2`, `SpiritFistDamage = 1`
	/// (Spirit.dm:436-439). Mesma historia dos dois de cima: o `niveis.json` ja trazia
	/// `"mults": ["SpiritFistCost=0.5", ...]` nos degraus 1 e 2 da `Spirit_Fist` (Spirit.dm:418-429) e
	/// o verb usava duas constantes -- "sem niveis no port, fica 2". Agora o degrau cai aqui.
	/// </summary>
	public double SpiritFistCost = 2, SpiritFistDamage = 1;

	/// <summary>Modificadores de dreno: `P` e permanente, o outro e o de agora.</summary>
	public double PDrainMod = 1, DrainMod = 1;

	/// <summary>O botao do servidor pra deixar tudo mais caro ou mais barato de uma vez.</summary>
	public static double GlobalKiDrainMod = 1;

	/// <summary>
	/// O CUSTO-BASE DE QUALQUER TECNICA, 1:1 com o `BaseDrain()` do original:
	///
	///     sqrt(max(MaxKi,1)/140) * PDrainMod * globalKiDrainMod * DrainMod / log(9, max(kiefficiencyskill,5))
	///
	/// ELE ESCALA COM O SEU PROPRIO KI MAXIMO, e essa e a parte contra-intuitiva que precisa
	/// ficar registrada: uma tecnica de "100 de Ki" nao custa 100 -- custa 100 x BaseDrain, e
	/// BaseDrain cresce com a raiz do seu MaxKi. E de proposito (o comentario do original diz:
	/// "depois de certo Ki maximo, drenos pequenos nao fazem nada"), mas ja causou um bug de
	/// verdade no jogo antigo: o Planet Destroy pedia `1000*BaseDrain` e virou IMPAGAVEL pra
	/// quem tinha muito Ki. Custo fixo e custo escalado sao decisoes diferentes -- ao portar
	/// cada tecnica, olhe qual das duas o verb usava.
	/// </summary>
	public double BaseDrain()
	{
		double num = Math.Max(MaxKi, 1);
		double eff = Math.Log(Math.Max(kiefficiencyskill, 5)) / Math.Log(9);
		return Math.Sqrt(num / 140) * PDrainMod * GlobalKiDrainMod * DrainMod / eff;
	}
}
