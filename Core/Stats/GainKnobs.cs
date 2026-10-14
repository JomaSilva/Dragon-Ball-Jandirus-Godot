namespace Jandirus.Core.Stats;

/// <summary>
/// TODOS os botoes de balanceamento do ganho de BP, num lugar so.
///
/// No BYOND eles estavam espalhados por `1A Defines.dm`, `BalanceSettings.dm`, `softcap.dm`
/// e `Gravity.dm`, e alguns so existiam como numero solto dentro da formula. Juntar aqui e
/// o que permite ajustar o ritmo do jogo sem cacar formula.
///
/// Sao estaticos porque no BYOND sao globais de verdade (um admin muda e vale pra todo mundo
/// no servidor). Quem manda neles e o SERVIDOR -- o cliente so recebe o resultado.
/// </summary>
public static class GainKnobs
{
	/// <summary>
	/// A TAXA GLOBAL. Multiplica praticamente todo ganho de BP do jogo: mexer aqui acelera
	/// ou freia o servidor inteiro de uma vez.
	/// </summary>
	public static double BPTick = 0.000056;

	/// <summary>Base FIXA de ganho. Substituiu o `relBPmax` nas formulas de treino -- com o
	/// relBPmax ali o treino compunha como juros e a Sala do Tempo virava bilhoes.</summary>
	public static double LinearGainBase = 1000;

	// --- multiplicadores globais por atividade (knobs de admin) ---------------
	public static double GlobalTrainGain = 1;
	public static double GlobalMedGain = 1;
	public static double GlobalSparGain = 1;
	public static double GlobalGravGain = 1;

	/// <summary>
	/// **QUANTO DO GANHO DE BP A MEDITACAO REALIZA** -- dono, 2026-10-13: *"meditar e training (botao t) tao dano o
	/// mesmo bp/s oq n deveria ser assim, train por consumir vigor deveria dar mais bp/s q o meditate (o train pode
	/// manter os ganhos atuais q ja ta legal), o meditate q deveria ganhar menos bp pq ele n cansa e ainda ganha
	/// energia"*.
	///
	/// POR QUE OS DOIS RENDIAM IGUAL: o `MedGain` tem DUAS parcelas, como no DM -- a da atividade (um vinte e
	/// quatro avos da base) e a do potencial escondido virando poder (`hiddenpotential/30`). A segunda quase nao
	/// difere da do treino (`/32`), e em quem tem potencial alto ela e o ganho inteiro: sentado ou suando, o numero
	/// subia igual. O fator vale pras DUAS parcelas. DIVERGENCIA DECLARADA do DM; o treino nao muda.
	/// </summary>
	public static double MeditarRende = 0.4;

	/// <summary>
	/// A TAXA GLOBAL DO EXP DE SKILL DE KI -- `var/GlobalKiExpRate = 1` (`KiStatsModule.dm:3`).
	///
	/// Ela NAO e irma das quatro de cima: aquelas multiplicam ganho de BP, esta multiplica o exp das
	/// arvores de Ki, dentro do `KiSkillGains` (`Mind.dm:859-871`). Ficam no mesmo lugar porque as
	/// duas familias sao knobs de admin do mesmo painel (`BalanceSettings.dm:20-22`).
	/// </summary>
	public static double GlobalKiExpRate = 1;

	/// <summary>
	/// O PESO DA MEDITACAO NO EXP DAS SKILLS DE KI (a arvore da Mente). **1 = o original, sem tirar nem por.**
	///
	/// O item 4 do playtest de 2026-10-10: *"meditar deveria fazer Ki Unlocked e outras skills de ki subirem
	/// mais rapido"*. O bonus EXISTE e e aplicado -- e do DM e esta nos dados (`niveis.json`): Ki Unlocked rende
	/// 2 por tique meditando contra 1 parado (`Mind.dm:126-131`), as tres basicas rendem 2 nos primeiros niveis
	/// (`:189-192`, `:242-247`, `:301-305`), e toda skill da Mente junta exp adiantado enquanto o dono medita
	/// (`:41-48`). O que o jogador nao via era o PROGRESSO (o nivel e o exp nao aparecem em lugar nenhum) e o
	/// tamanho da curva do recem-nascido (`KiSkillGains`: com Ki maximo ate 300 o ganho e multiplicado por
	/// Ki maximo / 3000 -- trinta vezes mais lento com 100 de Ki, como no original).
	///
	/// Este numero multiplica o ganho de curva de uma skill da Mente ENQUANTO O DONO MEDITA, por cima do que o
	/// dado ja diz. Deixado em 1 nada muda; e o botao que o dono pediu pra poder afinar.
	/// </summary>
	public static double MeditacaoNasSkillsDeKi = 1;

	/// <summary>Quanto MAIS lento e o ganho por gravidade. Ganho por tick = gravidade/isto.</summary>
	public static double GravGainDiv = 1500;

	/// <summary>
	/// Fracao da folga (maestria - gravidade atual) que vira bonus quando se treina ABAIXO
	/// da propria maestria. 0,2 mantem o premio de estar aclimatado sem deixar farmar
	/// gravidade baixa no lugar de encarar gravidade alta.
	/// </summary>
	public static double GravAccustomWeight = 0.2;

	/// <summary>Teto absoluto de maestria de gravidade.</summary>
	public static double GravityCap = 500;

	/// <summary>Teto do multiplicador de ganho que o PESO da (razao 1 = 2x, 2 = 4x, 4 = 8x).</summary>
	public static double WeightGainMax = 8;

	/// <summary>
	/// `WEIGHT_ITEM_CAP_MULT = 2`: quanto peso o corpo aceita VESTIR, em multiplos do proprio limite
	/// (`weight_cap_hw`). E o teto que o Upgrade da roupa de peso destrava no DM (`Tier 1.dm:145`).
	///
	/// Ele existe pra o "100%" do verbo de peso significar alguma coisa: com 2, vestir o maximo na
	/// gravidade da Terra da razao 2 -- ganho de treino 4x e ja esmagando. Metade disso e a razao 1,
	/// que e o limite confortavel do corpo (ganho 2x, sem dano).
	///
	/// A ESCALA E O PONTO. `Weighted` esta na mesma unidade do `weight_cap_hw` (que vale
	/// `expressedBP x Ephysoff x 20`), e nao numa fracao de 0 a 1 -- guardar a fracao ali fazia a
	/// razao dar zero e o sistema de peso inteiro nao existir. Ver `GameServer.AjustarPeso`.
	/// </summary>
	public static double WeightItemCapMult = 2;

	// --- lutar com alguem mais forte ------------------------------------------
	/// <summary>Teto do bonus por encarar um oponente mais forte.</summary>
	public static double FightGainCap = 2;
	/// <summary>Teto do bonus treinando com o PROPRIO mestre (o geral e 2x).</summary>
	public static double MasterGainCap = 3;

	/// <summary>
	/// O PISO DO BONUS DO MESTRE (dono, 2026-10-13: *"treinar com o seu mestre (do sistema de mestre e aluno) deveria
	/// dar um bonus de ganhos de bp, alunos treinando com alunos tb deveriam ganhar um bonus de ganho mas menor do q
	/// treinar com o mestre"*): contra o proprio mestre o ganho nunca e menor que isto, seja qual for o poder dele.
	///
	/// DIVERGENCIA DECLARADA. No DM o bonus do mestre so valia com ele pelo menos 1,2x mais forte em BP base
	/// (`MST_GAIN_FLOOR`, `combatgains.dm:98`): abaixo disso treinar com o mestre rendia o mesmo que treinar com um
	/// estranho, e o aluno que alcancava o mestre perdia o bonus calado -- e passaria a render MAIS com um colega do
	/// que com o mestre, o contrario da frase. Com ela o bonus vale SEMPRE, e fica acima do de colega
	/// (<see cref="ColegaDeMestreGain"/>) em qualquer poder. Entre o piso e o teto a escala continua a do original:
	/// linear pela razao dos BPs base.
	/// </summary>
	public static double MasterGainMin = 2;

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--mestreteste` e `--v021teste`): o bonus do mestre volta a depender so do gap -- com
	/// o mestre do mesmo poder, treinar com ele nao rende nada a mais. Lido pelo `Fighter.FightGainMult`. Falso em
	/// jogo, sempre.
	/// </summary>
	public static bool MestreSemPisoDeTeste;

	/// <summary>
	/// O BONUS DE TREINAR COM UM COLEGA -- outro aluno do MESMO mestre (dono, 2026-10-13: *"alunos treinando com
	/// alunos tb deveriam ganhar um bonus de ganho mas menor do q treinar com o mestre"*). Fixo, e metade do teto do
	/// mestre: os dois alunos podem ter o mesmo poder, e um bonus que dependesse do gap (como o do mestre) nao
	/// renderia nada justamente pro par mais comum. NAO EXISTE NO DM -- la o `fight_gain_mult` so conhece o mestre.
	/// </summary>
	public static double ColegaDeMestreGain = 1.5;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): treinar com um colega de mestre volta a nao render nada a mais -- a
	/// v0.2.0. Lido pelo `Fighter.FightGainMult`. Falso em jogo, sempre.
	/// </summary>
	public static bool ColegaSemBonusDeTeste;

	// --- de onde o ganho da luta vem (dono, 2026-10-14) -------------------------
	/// <summary>
	/// SOCAR UM OBJETO (parede, arvore, pedra, construcao, nave, bloco) rende isto, em vezes do ganho de socar um NPC:
	/// *"objetos (e oq menos da)"*. O mesmo peso vale pro ki que acaba numa parede ou no chao de um planeta. Socar o AR
	/// e o ki que some no nada nao rendem -- as regras estao em `GameServer.Ganhos.cs`.
	/// </summary>
	public static double GanhoDeObjeto = 0.1;

	/// <summary>
	/// LUTAR COM OUTRO JOGADOR NUMA TROCA DE GOLPES rende isto, em vezes do ganho contra um NPC, POR CIMA do gap e do
	/// mestre (<see cref="Fighter.FightGainMult"/>): *"e o maior ganho do jogo"*. Sem troca -- o outro nao te atacou
	/// nos ultimos <see cref="SegundosDaTroca"/> -- bater nele (ou apanhar dele) nao rende nada.
	/// </summary>
	public static double GanhoDaTrocaPvp = 2;

	/// <summary>A JANELA DA TROCA: o outro jogador te atacou (soco ou ki, acertando, errando ou sendo bloqueado) ha no maximo isto.</summary>
	public static double SegundosDaTroca = 10;

	/// <summary>DEFEITO INJETADO (bancada `--ganhoteste`): socar o ar volta a render como um soco, a regra de antes. Falso em jogo, sempre.</summary>
	public static bool SocoNoArRendeDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--ganhoteste`): o ki volta a render no disparo, acerte ou nao. Falso em jogo, sempre.</summary>
	public static bool KiRendeNoDisparoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--ganhoteste`): jogador contra jogador volta a render como contra NPC, com troca ou
	/// sem, e sem o bonus dela. Falso em jogo, sempre.
	/// </summary>
	public static bool PvpSemTrocaDeTeste;

	// --- zonas especiais -------------------------------------------------------
	/// <summary>Sala do Tempo: 1 dia la = 1 ano de treino (28 dias x 10 meses).</summary>
	public const double TimeChamberMult = 28 * 10;
	/// <summary>
	/// Dimensao Mental: ganho a 1/4 do de fora -- o `MIND_GAIN_MULT` (`MindMeditate.dm:18`).
	///
	/// ESTA CONSTANTE FICOU ORFA DESDE QUE FOI ESCRITA, e o pedido do dono a nomeia em voz alta
	/// (*"o GANHO DE BP E REDUZIDO"*). Quem a le hoje e o <see cref="World.RitmoDaZona.Aplicar"/>,
	/// pelo MESMO campo (<see cref="Fighter.zoneGainMult"/>) e no MESMO funil que a Sala do Tempo
	/// usa pro 280 -- e nao por um caminho proprio, que seria a segunda maneira de a zona mexer no
	/// treino.
	/// </summary>
	public const double MindDimensionMult = 0.25;

	/// <summary>
	/// O MAIOR BP base do servidor. Entra em duas escalas de ganho (alvo de Ki, sombra de
	/// treino, blast) e no teto do potencial escondido. O servidor mantem atualizado.
	/// </summary>
	public static double TopBP = 1;

	// --- Zenkai ----------------------------------------------------------------
	/// <summary>Fracao do BP do inimigo que vira Zenkai numa derrota comum.</summary>
	public static double ZenkaiPct = 0.1;
	/// <summary>A mesma, com o corpo em farrapos.</summary>
	public static double ZenkaiPctInjured = 0.15;
	/// <summary>Recarga do Zenkai, em milissegundos de relogio real (1 hora).</summary>
	public static long ZenkaiCooldownMs = 3_600_000;
	/// <summary>
	/// APOSENTADORIA do Zenkai: teto = `ssj3at` x isto, pra quem nunca rolou o requisito
	/// pessoal. 0,25 e o ponto medio da formula que sorteia esse requisito.
	/// </summary>
	public static double ZenkaiCapSSJ3Mid = 0.25;
}
