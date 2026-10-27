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
	///
	/// (2026-10-16) VINTE VEZES MENOR QUE A DO DM -- o dono: *"o bp gain ta MUITO alto ... diminuir os ganhos em 20x do jogo em
	/// si"* (tres lutas de menos de 3 minutos na Terra, sem bonus nenhum, davam mais de 1k de BP; ele quer uns 50). DIVERGENCIA
	/// DECLARADA. Os multiplicadores (gap, mestre, troca, gravidade, peso, Sala do Tempo) continuam os mesmos, por cima dela.
	/// Passa por aqui todo ganho de BP -- treino, meditacao, luta, ki, gravidade, andar e voar, o buffer de ocio, o potencial
	/// escondido, as gotas das absorcoes -- e nada mais: desejos, itens e Zenkai nao a leem. A maestria de GRAVIDADE tambem a
	/// lia, e ela nao e BP: ficou no ritmo do DM (<see cref="BPTickDoDm"/>).
	///
	/// (2026-10-23) E AGORA SO A METADE DA DO DM -- dez vezes a de 2026-10-16. O dono, junto do peso e da gravidade que passaram a
	/// esmagar o morto: *"isso virou meta do jogo, porem pra compensar suba o bp gain em 10x (tinha dividido por 20 antes entao agr seria
	/// so dividido por 2)"*. O divisor e o dele, e por isso a `--ganhoteste` (familia 5) o confere pelo numero ESCRITO (o mesmo soco de
	/// producao na taxa do DM rende 2x o de agora), e nao lendo este campo; o contra-exemplo dela e o ritmo de 2026-10-16 (`/ 20`).
	/// </summary>
	public static double BPTick = BPTickDoDm / 2;

	/// <summary>A taxa global do DM (`BPTick = 0.000056`). So a maestria de gravidade anda nela -- ver <see cref="BPTick"/>.</summary>
	public const double BPTickDoDm = 0.000056;

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
	///
	/// (2026-10-17) CINCO VEZES A DO DM -- o dono: *"faça as skills de ki evolurem mais rapido, estamos quase nivel super saiyajin e
	/// nem liberamos o beam ainda"*. DIVERGENCIA DECLARADA. O beam basico e o nivel 25 da Basic Ki Effusion, que so ganha exp com
	/// tiro de Ki; antes dele vem o Ki Unlocked 35 (a bola, que abre a arvore Effusive Mastery). Na taxa do DM, com pericia de Ki
	/// perto de 3: o Ki Unlocked 35 em ~3 h parado (~1,5 h meditando) e a Efusao 25 em ~3.100 bolas; aqui, ~35 min (~17 meditando)
	/// e ~630 bolas. Vale pra TODA skill de Ki (as tres familias da Mente e as maestrias de tiro), e o banco de exp adiantado
	/// (`100 * GlobalKiExpRate`, `Mind.dm:43`) cresce junto. O BP nao le esta taxa (ver <see cref="BPTick"/>).
	/// </summary>
	public static double GlobalKiExpRate = GlobalKiExpRateDoDm * 5;

	/// <summary>A taxa do DM (`GlobalKiExpRate = 1`). A `--niveisteste` mede a de agora contra ela, pela bola de producao.</summary>
	public const double GlobalKiExpRateDoDm = 1;

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

	/// <summary>
	/// DEFEITO INJETADO (bancada `--ganhoteste`, familia 6): a gravidade nao multiplica o ganho -- o jogo de antes de 2026-10-16, em
	/// que treinar em 10x rendia o mesmo que em 1x (a parcela que ela somava era ~2% do treino) com o painel dizendo "10x". Lido por
	/// `Fighter.FatorDaGravidade`. Falso em jogo, sempre.
	/// </summary>
	public static bool GravidadeNaoMultiplicaDeTeste;

	// --- (2026-10-26) O BANCO DO POTENCIAL, COMO NO DU -------------------------
	/// <summary>
	/// O BANCO DO POTENCIAL (o pedido 95 do dono, 2026-10-25/26): *"o potencial crescia quando vc treinava e ele n pingava no seu bp ele
	/// acumulava"* -- o `unusedPotential` do DU (`Train New 2021.dm:89`: `unusedPotential += gain * Math.Rand(0.2, 0.4)`). O banco e o
	/// proprio `hiddenpotential` (o dono: *"o hidden potential meio q pode ser usado como essa pool, so pra manter a variavel"*). Cada ganho
	/// de ATIVIDADE (treino, meditacao, luta, ki, voo, nado) guarda nele de 20 a 40% de si (`Fighter.GanhoDeAtividade`), e o banco NAO
	/// pinga mais no BP: so os liberadores o soltam, cada um pelo multiplicador dele (`LiberaOGuru` e os outros).
	///
	/// O QUE SAIU, e era o que fazia a Agua e o Unlock darem bilhoes a quem ja tinha bilhoes: o pingo do potencial no treino, na
	/// meditacao e no soco (`hiddenpotential*BPTick*(1/N)`, do DM), o ano que somava `BP*Potencial` no banco, a heranca do BP dos pais, e
	/// as liberacoes que liam o proprio BP (`BP*0,25*Potencial` no Unlock, um quinto do teto pessoal na Agua).
	/// </summary>
	public const double PotencialDoGanhoMin = 0.2, PotencialDoGanhoMax = 0.4;

	/// <summary>A classe Prodigial ENCHE o banco 1,5x mais depressa -- o dono, 2026-10-26: *"faz ele encher o banco 1,5x ao inves de 1,8x"*
	/// (era o `awaken` 1,8 do Unlock do DM, que multiplicava a liberacao). Sem bonus de raca nenhum: o Potencial da raca nao mexe no banco.</summary>
	public const double BancoDoProdigial = 1.5;

	/// <summary>
	/// QUANTO DO BANCO CADA LIBERADOR SOLTA (o dono, 2026-10-25): o Grande Patriarca 1,25x, a Agua Ultra Divina 1x, o ritual do Kaioshin 2x
	/// (alem do Potential Unleashed), o desejo ao dragao 1,5x e o Unlock Potential de um jogador em outro 1x. O que sai vai pro BP base e
	/// fica. (2026-10-26) E o Android Upgrade, a porta do androide de absorcao (o pedido 95c): 1x, a escolha do dono.
	/// </summary>
	public const double LiberaOGuru = 1.25, LiberaAAgua = 1, LiberaORitual = 2, LiberaODesejo = 1.5, LiberaOJogador = 1, LiberaOUpgrade = 1;

	/// <summary>
	/// A RECARGA ENTRE DUAS LIBERACOES DO MESMO CORPO: uma hora de relogio real -- o `nextUnlockUse` do DU (`Skills.dm:2280`); o dono:
	/// *"repetiveis, 1h de recarga ... potential unlocked pode a cada 1 hora"*. Vale pro Guru, pra Agua e pro Unlock de jogador; o ritual do
	/// Kaioshin e uma vez por personagem, e o desejo e o dragao (nenhum dos dois a pede nem a marca).
	/// </summary>
	public const long RecargaDaLiberacaoMs = 3_600_000;

	/// <summary>O que a Agua POE no banco depois de solta-lo: 10% do BP base, minguando a cada gole -- `(base_bp * 0.1) / (sacredWaterUses + 1)`
	/// (`Skills.dm:2772`).</summary>
	public const double AguaPoeNoBanco = 0.1;

	/// <summary>
	/// O SORTEIO DOS 20 A 40%: um gerador SO DO BANCO, pra o guardar nao gastar numeros do gerador que a bancada semeia pro ganho (as
	/// medidas de BP continuam as mesmas, com ou sem o banco). A bancada o troca por um semeado quando precisa.
	/// </summary>
	public static Random SorteioDoBanco = new();


	/// <summary>Teto absoluto de maestria de gravidade.</summary>
	public static double GravityCap = 500;

	/// <summary>
	/// O BONUS MAXIMO DA ACLIMATACAO, em fracao do BP base: +25% (dono, 2026-10-23: *"o buff de bp do aclimado deveria ser no maximo 25%
	/// quando vc tem 500x gravidade masterizada e nao 100% , ta mt op"*). E o PODER de quem domina mais gravidade do que o chao puxa (o
	/// `gravFelt` do DM, `base.dm:89-99`, que soma na base). A curva continua a do DM -- `ln(maestria/gravidade)^2` --, com o divisor
	/// tirado DESTE numero (a razao no teto da maestria, <see cref="GravityCap"/>, rende exatamente ele) e a razao TRAVADA nesse teto:
	/// o planeta mais a maquina chegam a ~501x, e a maestria pela luta (`Fighter.AttackGain`) nao tem teto. Com 0,25: 10x +3,4%, 100x
	/// +13,7%, 500x +25% (no DM, divisor 40: +5,3%, +53%, +96,6%). DIVERGENCIA DECLARADA. Ver `StatCurves.GravFelt`.
	/// </summary>
	public static double AclimatacaoMaxima = 0.25;

	/// <summary>Teto do multiplicador de ganho que o PESO da (razao 1 = 2x, 2 = 4x, 4 = 8x).</summary>
	public static double WeightGainMax = 8;

	/// <summary>
	/// PESO DEMAIS PRA TREINAR: com a razao do peso (`Fighter.weight_ratio` -- o peso vestido vezes a gravidade, contra o limite do corpo)
	/// ACIMA disto, o treino nao rende nada. O dono (2026-10-23): *"usar pesos acima de 8x seu limite corporal, vc nem conseguiria treinar
	/// assim n valendo a pena a usar pesos mt mt mt altos"*. Ate aqui o ganho travava em <see cref="WeightGainMax"/> a partir da razao 4, e
	/// 8x, 20x ou 100x o limite rendiam o mesmo que 4x. NAO EXISTE NO DM. So o GANHO zera (o T, a luta, a meditacao e o ki, e o servidor
	/// recusa ou derruba o treino e a meditacao com o aviso); o resto do peso continua como era: o esmagamento, o vigor que o treino cobra e
	/// o poder dividido pelo `weight`. Ver `Fighter.PesoDemaisPraTreinar`.
	/// </summary>
	public static double RazaoQueImpedeOTreino = 8;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--gravmetateste`): o peso acima de 8x o limite do corpo volta a render -- o jogo de antes de 2026-10-23, em
	/// que a razao 20 treinava como a 4 (8x). Lido por `Fighter.PesoDemaisPraTreinar`. Falso em jogo, sempre.
	/// </summary>
	public static bool PesoDemaisTreinaDeTeste;

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
	/// treinar com o mestre"*): contra o proprio mestre o ganho nunca e menor que isto -- enquanto o aluno nao o SUPERAR em
	/// BP base (dono, 2026-10-16: *"se um aluno superar o mestre ele n ganha mais o bonus de bp so ganhando a troca"*; ver
	/// <see cref="AlunoQueSuperouComBonusDeTeste"/>).
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
	/// DEFEITO INJETADO (bancada `--mestreteste`): o aluno que superou o mestre em BP base volta a ganhar o bonus dele (o piso
	/// de <see cref="MasterGainMin"/>) -- a regra de 2026-10-13, de antes da de 2026-10-16. Lido pelo `Fighter.FightGainMult`.
	/// Falso em jogo, sempre.
	/// </summary>
	public static bool AlunoQueSuperouComBonusDeTeste;

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
	/// O MAIOR BP base do servidor. Entra nas escalas de ganho (alvo de Ki, sombra de
	/// treino, blast). O servidor mantem atualizado.
	/// </summary>
	public static double TopBP = 1;

	// --- Zenkai ----------------------------------------------------------------
	/// <summary>
	/// (2026-10-26) O BANCO DO ZENKAI, como no DU (`Train New 2021.dm:92`, o `unusedZenkai`): cada ganho de atividade de quem tem sangue
	/// Saiyajin guarda de 5 a 15% de si, sem bonus (o dono: *"ja q so saiyajin e quem tem sangue saiyajin tem zenkai, n precisa multiplicar
	/// por 1,5x, apenas 1x ta bom"*). Ver `Fighter.GanhoDeAtividade`.
	/// </summary>
	public const double ZenkaiDoGanhoMin = 0.05, ZenkaiDoGanhoMax = 0.15;

	/// <summary>
	/// (2026-10-26) QUANTO DO BANCO DO ZENKAI UM NOCAUTE SOLTA: de 20 a 80% (o `rand(0.2,0.8)` do `Zenkai()` do DU, `Train New 2021.dm:211`),
	/// em todo nocaute, sem recarga -- e o que sai fica PRA SEMPRE no BP base (o dono, que viu o `activeZenkai` do DU ser comido pelo treino
	/// seguinte: *"oq sair do banco fica pra sempre"*). Ver `Fighter.SoltarOZenkai`.
	/// </summary>
	public const double ZenkaiSoltaMin = 0.2, ZenkaiSoltaMax = 0.8;

	/// <summary>
	/// (2026-10-26) A MORTE SOLTA EM DOBRO (o `Zenkai(2)` do DU, `death.dm:604`) -- e a maioria do corpo em estado critico tambem, sem
	/// precisar morrer (o dono: *"se a maioria do corpo do saiyajin estiver em estado crtico, ele ganha o boost igual da morte (2x)"*).
	/// </summary>
	public const double ZenkaiDobrado = 2;
	/// <summary>
	/// (2026-10-24) A RECARGA DA FUSAO NAMEKUSEIJIN E PROPORCIONAL AO MULTIPLICADOR: 2,5 h de relogio real por "x" do BP base de depois
	/// sobre o de antes (o passo 6 do `GameServer.AbsorverNamekuseijin`; o dono: *"o mesmo pra fusao namek"*). Nasceu pro Zenkai, que
	/// desde 2026-10-26 e um banco sem recarga (o DU) -- ela ficou so com a fusao. Ver <see cref="RecargaProporcionalMs"/>.
	/// </summary>
	public static long RecargaMsPorMultiplicador = 9_000_000;

	/// <summary>
	/// A RECARGA DE QUEM FOI DE <paramref name="antes"/> PRA <paramref name="depois"/> DE BP: 2,5 h por "x" (`depois / antes`), e nunca
	/// menos que a do "1x". Ver <see cref="RecargaMsPorMultiplicador"/>.
	/// </summary>
	public static long RecargaProporcionalMs(double antes, double depois) =>
		(long)(RecargaMsPorMultiplicador * Math.Max(depois / Math.Max(antes, 1e-9), 1));

	/// <summary>A hora fixa de antes de 2026-10-24 -- so o <see cref="RecargaFixaDeTeste"/> a usa.</summary>
	public const long RecargaFixaMs = 3_600_000;

	/// <summary>
	/// DEFEITO INJETADO (`--fusaoduplateste`): a absorcao Namekuseijin volta a deixar a hora fixa de antes, de qualquer tamanho. Lido
	/// pelo `GameServer.AbsorverNamekuseijin`. Falso em jogo, sempre.
	/// </summary>
	public static bool RecargaFixaDeTeste;
	/// <summary>
	/// APOSENTADORIA do Zenkai: teto = `ssj3at` x isto, pra quem nunca rolou o requisito
	/// pessoal. 0,25 e o ponto medio da formula que sorteia esse requisito.
	/// </summary>
	public static double ZenkaiCapSSJ3Mid = 0.25;
}
