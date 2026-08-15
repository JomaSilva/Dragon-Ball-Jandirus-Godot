using Jandirus.Core.Forms;
using Jandirus.Core.World;

namespace Jandirus.Core.Ai;

/// <summary>
/// O QUE O CORPO ESTA TENTANDO FAZER AGORA. Ele dura no minimo
/// <see cref="Cerebro.TempoMinimoNoPlano"/> -- ver o compromisso.
/// </summary>
public enum Plano
{
	/// <summary>Alvo no chao (ou corpo caido): nada a fazer. Continuar socando um corpo no chao nao
	/// muda nada no jogo e deixa a cena feia.</summary>
	Nada,

	/// <summary>
	/// ANDAR SEM ALVO -- o macaco vagando e derrubando o que estiver no caminho.
	///
	/// ============================ ELE SE PERDEU NA PRIMEIRA VERSAO DESTA CAMADA ============================
	/// O cerebro antigo nao tinha planos: ele andava na direcao de `posAlvo`, e quando nao havia
	/// presa o servidor passava um PONTO (`RumoDaFera`) no lugar do alvo. Com os planos, `!TemAlvo`
	/// caiu em `Nada` -- e a fera e a furia lendaria pararam de andar, o que a bancada de formas
	/// pegou na hora ("o corpo possuido se move sozinho" reprovou com a posicao identica).
	///
	/// A licao e sobre o TIPO: a percepcao carrega `DoAlvo` mesmo sem alvo, e um plano so pra esse
	/// caso e o que impede o ponto de virar um alvo (com soco e guarda) ou de sumir.
	/// ==================================================================================================
	/// </summary>
	Vagar,

	/// <summary>Fechar a distancia e bater. O estado normal.</summary>
	Pressionar,

	/// <summary>Afastar guardando. O "respirar" que o clone sempre teve.</summary>
	Recuar,

	/// <summary>Afastar ate <see cref="Cerebro.DistanciaSegura"/>, PARAR e carregar. O `rechargeState` (NPCAI.dm:666).</summary>
	Recuperar,

	/// <summary>
	/// FUGIR -- e ele NAO e o <see cref="Recuar"/>. O `runawayState` (NPCAI.dm:646), que este
	/// cerebro nunca teve.
	///
	/// ============================ A DIFERENCA ENTRE RECUAR E FUGIR E O QUE SE VE NA TELA ============================
	/// `Recuar` e um passo atras COM A GUARDA ERGUIDA e o corpo encarando quem bate -- e uma jogada
	/// dentro da luta, e no DM ele nem tem estado proprio (e o `step_away` do `attackState`, `:536`).
	/// `Fugir` e o oposto em todos os campos: ele **corre** (a mesma tecla shift do jogador, cobrada
	/// pelo `PodeCorrer`), **nao ataca**, **nao guarda** e **olha pra onde vai** -- que e a terceira
	/// excecao do olhar que o `Montar` ja tinha o lugar reservado.
	///
	/// O portao e o do original e ele e DUPLO (`NPCAI.dm:510`): `HP <= 25` **E** coragem baixa. Um
	/// bicho corajoso quase morto continua brigando -- e por isso a fera e a furia lendaria
	/// (`VidaCautelosa = 0`, coragem no talo) nunca fogem, sem uma linha de excecao escrita pra elas.
	/// ==========================================================================================================
	/// </summary>
	Fugir,

	/// <summary>Subir um degrau da escada de formas. O `npc_try_transform` (NPCAI.dm:361).</summary>
	Escalar,

	/// <summary>Decolar e subir ate o andar do alvo. Nao existe no DM -- ver o cabecalho.</summary>
	Alcancar,

	/// <summary>
	/// ABRIR DISTANCIA, PLANTAR O PE, CONJURAR E SOLTAR. Vale pra quem tem alguma das tres tecnicas
	/// que voam (ver `TecnicasDeLonge`); quem nao tem cai fora na primeira linha do `EscolherTiro`.
	///
	/// Ele foi escrito ANTES do projetil existir, e de proposito: o que e caro nao e o `case` do
	/// disparo -- e o LUGAR dele na ordem das perguntas, o compromisso, as interrupcoes e o preco.
	/// Escrever isso junto com um projetil novo seria escrever duas coisas de uma vez e nao saber
	/// qual das duas esta errada quando o NPC ficasse parado olhando pro horizonte. Quando o tiro
	/// chegou, este plano nao precisou de uma linha.
	/// </summary>
	Atirar,

	/// <summary>
	/// CIRCULAR -- o `strafeState` (`NPCAI.dm:551`), e ele e a resposta direta ao *"soca no mesmo
	/// ritmo pra sempre"*.
	///
	/// ============================ O QUE UM JOGADOR FAZ E ESTE CEREBRO NAO FAZIA ============================
	/// Todas as receitas anteriores mexem o corpo no MESMO EIXO: `Pressionar` avanca na direcao do
	/// alvo, `Recuar` volta por ela, `Recuperar` volta por ela, `Fugir` volta por ela. Um corpo que so
	/// anda pra frente e pra tras na linha que liga os dois e imediatamente reconhecivel como boneco --
	/// nenhuma pessoa joga assim. Gente **contorna**: fica na distancia que quer e anda de lado.
	///
	/// No DM ele e o estado dos BLASTERS (`if(isBlaster && prob(4)) strafeState()`, `:507`), e a
	/// razao la e tatica: quem tem tiro nao quer fechar a distancia, quer manter os tres tiles do
	/// `strafe_Dist` (`:53`) e continuar atirando. Aqui vale o mesmo gate -- quem nao tem arsenal
	/// (o `isBlaster` deste port) nao circula --, e o efeito colateral e o que interessa pro dono: o
	/// bicho com raio deixa de ser um pendulo.
	/// ==================================================================================================
	///
	/// A MAO (horario ou anti-horario) e sorteada UMA VEZ, ao entrar no plano, e nao por tique. Um
	/// sorteio por tique seria o corpo tremendo no lugar -- exatamente o defeito que o compromisso
	/// (<see cref="Cerebro.TempoMinimoNoPlano"/>) existe pra evitar, so que dentro de uma receita.
	/// </summary>
	Circular,
}

/// <summary>
/// O CEREBRO DE UM CORPO QUE O SERVIDOR ESTA DIRIGINDO.
///
/// E logica PURA (mora no Core, nao conhece Godot, nem rede, nem `ServerPlayer`): recebe o mundo
/// como caberia numa tela (<see cref="Percepcao"/>) e o que o jogo ja disse que este corpo pode
/// (<see cref="Capacidades"/>), e devolve **o que apertar** (<see cref="Comando"/>). Ele nao move,
/// nao soca, nao transforma e nao voa: quem faz isso e o servidor, pelas MESMAS funcoes do jogador.
///
/// ============================ O QUE MUDOU NESTA CAMADA, E O QUE NAO ============================
/// Ele NAO foi reescrito. As quatro manivelas que o Oozaru e a furia lendaria ja escreviam
/// continuam com o mesmo nome, o mesmo tipo e o mesmo default -- <see cref="IntervaloDeDecisao"/>,
/// <see cref="DistanciaIdeal"/>, <see cref="VidaCautelosa"/>, <see cref="ChanceDePesado"/> --, e o
/// comportamento delas e o mesmo: `VidaCautelosa = 0` continua querendo dizer "nunca recua, nunca
/// guarda por medo".
///
/// O que mudou:
///   * `Decisao` virou <see cref="Comando"/> (estado x pulso) -- ver o cabecalho daquele arquivo;
///   * `Pensar` passou a receber <see cref="Percepcao"/> em vez de sete argumentos soltos, porque
///     a altitude e o poder do alvo entrariam como o oitavo e o nono;
///   * nasceram quatro capacidades novas (voar, guardar por RITMO, carregar, transformar) e uma
///     manivela pra cada uma poder ser DESLIGADA por tempero (<see cref="Disciplina"/>,
///     <see cref="Inteligencia"/>). A fera continua nao guardando porque a manivela dela e zero,
///     e nao porque o codigo da guarda nao existe.
/// ==========================================================================================
///
/// ============================ DOIS RELOGIOS, E ISSO NAO E DETALHE ============================
/// **Decidir e devagar; reagir e rapido.** O plano e reconsiderado a cada
/// <see cref="IntervaloDeDecisao"/> (0,2 a 0,5 s conforme o tempero) e MANTIDO no meio-tempo -- e o
/// que da a hesitacao humana e o que impede o boneco de tremer entre duas opcoes empatadas.
///
/// Os REFLEXOS (a guarda, a queda de proposito) rodam a CADA tique, por fora da decisao. Uma IA que
/// decide a 4 Hz e bloqueia a 4 Hz leva soco parada em ate 250 ms de janela, e isso nao e questao
/// de calibragem: um ciclo so nao tem duas velocidades.
/// =========================================================================================
///
/// ============================ E O QUE DELE VEIO DO DM, LINHA POR LINHA ============================
/// Tres dos quatro gestos desta camada existem no `Code/Modules/NPCs/NPCAI.dm` e estao portados com
/// os numeros de la (cada constante cita a linha):
///
///   guarda      `npc_defensive_check` (:288-293) e o brace do `attack()` (:189-191)
///   carga       `rechargeState` (:666-698), o estado inteiro
///   transformar `npc_try_transform` (:361-368), chamado pelo `npc_power_up` (:234)
///
/// O QUARTO NAO EXISTE LA: **a IA do DM nunca voa.** Nao ha um `isflying`, um `Fly` nem um
/// `flight` no arquivo inteiro -- e nao e esquecimento, e que no BYOND nao ha altura (voar la e
/// sair da conta de colisao, no mesmo z). O voo e a altitude sao desenho novo deste port, e estao
/// marcados como tal onde aparecem.
/// ============================================================================================
/// </summary>
public sealed class Cerebro
{
	// =====================================================================
	// AS QUATRO MANIVELAS ANTIGAS -- o Oozaru e a furia escrevem estas
	// =====================================================================

	/// <summary>Quanto tempo entre duas DECISOES. Nao e o tempo de reacao: ver <see cref="TempoDeReacao"/>.</summary>
	public double IntervaloDeDecisao = 0.25;

	/// <summary>A que distancia ele tenta ficar. Um tile: dentro do alcance do soco.</summary>
	public float DistanciaIdeal = 34f;

	/// <summary>
	/// ============================ ATE ONDE ELE INVESTE -- O DASH DA IA ============================
	/// **O dono: *"os NPCs N USAM O DASH, eles so andam"*. Ele estava certo, e a causa nao era falta de
	/// caminho: era falta de RECEITA.**
	///
	/// O arranque do port nao e verb nem corrida -- ele mora DENTRO do `Atacar` (o `Aproximar`), e a IA
	/// ja passava pelo mesmo `Atacar` do jogador. O que faltava era o cerebro pedir um golpe estando
	/// LONGE: o <see cref="Pressao"/> so mandava `Leve`/`Pesado` dentro de `DistanciaIdeal * 1.6` =
	/// 54,4 px, e o `Aproximar` exige `dist - 32 >= 16`, ou seja **48 px**. A janela de investida era a
	/// fresta de 48 a 54,4 px -- e o proprio cerebro para de se aproximar em `DistanciaIdeal * 1.4` =
	/// 47,6 px, meio pixel ABAIXO dela. Assentado, ele nunca mais investia; fechando, quase nunca.
	///
	/// NO DM O NPC INVESTE, E DE PROPOSITO, em duas receitas:
	///   * `NPCAI.dm:188-195` -- `if(get_dist(src,target) &lt; 2) ... else if(haszanzo) Attack()`. Com o
	///     alvo a dois tiles ou mais, ele ataca **justamente pra fechar**;
	///   * `NPCAI.dm:449-450` -- dentro do `chaseState`: `if(d &lt;= 10 &amp;&amp; d &gt;= 3 &amp;&amp; prob(10))
	///     dashBreak = 1`, que quebra a perseguicao e cai no `attack()` (`:484-485`). Uma investida
	///     explicita de **tres a dez tiles**.
	///
	/// DEZ TILES E O NUMERO DAQUELA LINHA (320 px), e ele cabe: com o alvo marcado -- e a IA marca
	/// (`Montar` escreve `Comando.Marcar`) -- o arranque do port alcanca 480 px
	/// (`GameServer.Combat.AlcanceDoDashMarcado`). Sem marca ele alcancaria so 160, e a investida
	/// simplesmente nao sairia; nao ha aqui nenhuma checagem disso, pelo mesmo motivo de sempre: quem
	/// responde "nao deu" e o `Aproximar`, e um segundo juiz aqui divergiria dele no primeiro dia.
	/// ==============================================================================================
	/// </summary>
	public float AlcanceDaInvestida = 320f;

	/// <summary>
	/// A CHANCE DE UM GOLPE DE LONGE VIRAR INVESTIDA. O `prob(10)` do `NPCAI.dm:449`, reescalado.
	///
	/// La o dado e jogado a cada volta do `chaseState` (`sleep(chase_speed)`, e `chase_speed = 2` =
	/// 0,2 s), ou seja ~0,5 investida por segundo enquanto persegue. Aqui o dado e jogado no MESMO
	/// lugar em que o cerebro decide socar, e o freio de verdade e o <see cref="EmRajada"/> -- que ja
	/// espaca as rajadas em 0,45 a 1,2 s. Um quarto nesse portao mede **1,7 investida por segundo** na
	/// bancada (`--tresteste`) -- abaixo do teto de 2/s que a recarga do arranque permite, e sem virar
	/// metronomo: as vezes ele arranca, as vezes ele fecha andando, e as duas coisas se veem.
	///
	/// **MEIO A MEIO ERA DEMAIS, E A BANCADA MOSTROU**: 2,3 pedidos por segundo, contra os 2 que a
	/// recarga do arranque (500 ms) sequer consegue atender -- ou seja, um em cada seis viraria soco
	/// no ar por recarga, que e exatamente o defeito que este lote esta consertando. E cada arranque
	/// custa 5% do Ki maximo: nessa taxa o tanque de um NPC morre em dez segundos de perseguicao, e
	/// ele para de investir pelo `temFolego` -- corrigido por exaustao, que e a pior forma de corrigir.
	/// </summary>
	public double ChanceDeInvestir = 0.25;

	/// <summary>Fracao de vida abaixo da qual ele passa a guardar e a recuar. Zero = nunca (a fera).</summary>
	public double VidaCautelosa = 0.45;

	/// <summary>
	/// Chance de escolher o golpe pesado quando pode. O resto sai leve.
	///
	/// E a FURIA (`behavior_vals[2]`) em forma de peso de golpe -- e, desde as emocoes, ela e o
	/// **piso** e nao o valor final: ver <see cref="FuriaExpressa"/>.
	/// </summary>
	public double ChanceDePesado = 0.35;

	// =====================================================================
	// AS MANIVELAS NOVAS
	// =====================================================================

	/// <summary>
	/// DISCIPLINA (0..1): o quanto este corpo APARA. Zero = nunca ergue a guarda.
	///
	/// E o `e_behavior_vals[4]` (logica) do DM, que gateia o `npc_defensive_check`
	/// (`NPCAI.dm:291`: `e_behavior_vals[4] >= 35`). O default 0,35 e o `NPC_AI_INT_DEFAULT`.
	///
	/// **A fera e a furia lendaria a poem em ZERO**, e nao por economia: o `legendary_berserk_loop`
	/// nao tem um unico ramo de guarda, e o macaco sem controle *"sai batendo em qualquer coisa"*.
	/// Furia que se protege nao e furia -- e o mesmo argumento que ja justificava `VidaCautelosa = 0`
	/// nos dois.
	/// </summary>
	public double Disciplina = 0.35;

	/// <summary>
	/// INTELIGENCIA (0..1): o `ai_intelligence` (`NPC_AI_INT_DEFAULT 35`, `NPC_AI_INT_BOSS 85`).
	///
	/// Ela nao deixa o NPC "melhor em tudo": ela decide **quais planos existem pra ele**. Um bicho
	/// burro nunca recarrega e nunca paira rasante -- ele avanca e bate. Erro CARACTERISTICO e
	/// reconhecivel ("aquele bicho nunca recua"); erro aleatorio e indistinguivel de bug.
	/// </summary>
	public double Inteligencia = 0.35;

	/// <summary>
	/// AGRESSIVIDADE (0..1): o `ai_aggression` (`NPC_AI_AGGR_DEFAULT 50`, `NPC_AI_AGGR_BOSS 75`).
	///
	/// ============================ ELA GATEIA UMA COISA SO, E E A QUE O DM DIZ ============================
	/// *"agressividade (0-100): chance de entrar no modo finalizador"* (`NPCAI.dm:27`) -- e mais
	/// nada. Ela nao deixa o NPC "bater mais": ela decide se, com o alvo quase caido, ele PARA de
	/// medir a luta e vai fechar (ver <see cref="VidaDeFinalizar"/>).
	///
	/// O campo vem do MESMO `agressividade` do `npcs.json` que ja escolhia a cadencia da decisao --
	/// e o `Temperamento` prometia isso por escrito: *"o `ai_aggression` do DM gateia o MODO
	/// FINALIZADOR, que este cerebro ainda nao tem. No dia em que ele existir, e daqui que sai o
	/// gate -- nao de um campo novo"*. E daqui que ele saiu.
	/// ================================================================================================
	/// </summary>
	public double Agressividade = 0.50;

	/// <summary>
	/// TEMPO DE REACAO BASE, em segundos. Nao e o intervalo de decisao: e quanto ele demora pra
	/// RESPONDER a uma coisa que acabou de acontecer (um soco chegando).
	///
	/// 0,25 s e o tempo de reacao visual de um humano razoavelmente atento. O piso duro de
	/// <see cref="ReacaoMinima"/> existe pra que nenhum sorteio produza um reflexo sobre-humano --
	/// e a bancada afirma esse piso sobre mil amostras.
	/// </summary>
	public double TempoDeReacao = 0.25;

	// =====================================================================
	// OS NUMEROS. Portados quando ha original; marcados quando nao ha.
	// =====================================================================

	/// <summary>Piso absoluto do tempo de reacao, em segundos. Abaixo disto e clarividencia.</summary>
	public const double ReacaoMinima = 0.10;

	/// <summary>Quanto tempo um plano dura no minimo, em segundos. E o COMPROMISSO -- ver `Repensar`.</summary>
	public const double TempoMinimoNoPlano = 1.2;

	/// <summary>Guarda erguida dura de 0,8 a 1,8 s. LITERAL: `spawn(rand(8,18))` (`NPCAI.dm:293`).</summary>
	public const double GuardaMin = 0.8, GuardaMax = 1.8;

	/// <summary>Chance de aparar quando pressionado. `prob(35)` (`NPCAI.dm:291`).</summary>
	public const double ChanceDeApararSobPressao = 0.35;

	/// <summary>Chance de SOLTAR a guarda quando a pressao passa. `prob(40)` (`NPCAI.dm:289`).</summary>
	public const double ChanceDeSoltarAGuarda = 0.40;

	/// <summary>Abaixo desta vida ele se considera pressionado. `HP &lt;= 35` (`NPCAI.dm:291`).</summary>
	public const double VidaPressionada = 0.35;

	/// <summary>Acima desta vida a pressao passou. `HP &gt; 40` (`NPCAI.dm:289`).</summary>
	public const double VidaAliviada = 0.40;

	/// <summary>Chance base de ANTECIPAR um golpe pelo ritmo. `prob(15)` do brace (`NPCAI.dm:190`).</summary>
	public const double ChanceDeAntecipar = 0.15;

	/// <summary>Ki critico: `NPC_AI_KI_CRIT 0.10` (`NPCAI.dm:9`).</summary>
	public const double KiCritico = 0.10;

	/// <summary>Folego critico: `NPC_AI_STAM_CRIT 0.12` (`NPCAI.dm:12`).</summary>
	public const double FolegoCritico = 0.12;

	/// <summary>Ki BAIXO (nao critico): `NPC_AI_KI_LOW 0.30` (`NPCAI.dm:8`). Abaixo disto o esperto poupa.</summary>
	public const double KiBaixo = 0.30;

	/// <summary>Folego BAIXO: `NPC_AI_STAM_LOW 0.30` (`NPCAI.dm:10`). Abaixo disto o esperto joga defensivo.</summary>
	public const double FolegoBaixo = 0.30;

	/// <summary>
	/// Chance de erguer a guarda no modo defensivo por CANSACO. `NPC_AI_DEF_BLOCK_PROB 55`
	/// (`NPCAI.dm:25`, usado em `:526`). Convertida pra tique como a de soltar -- ver `Reflexos`.
	/// </summary>
	public const double ChanceDaGuardaPorCansaco = 0.55;

	/// <summary>
	/// VIDA DO ALVO que liga o MODO FINALIZADOR: `NPC_AI_FINISH_HP 25` (`NPCAI.dm:17`), lido em
	/// `:388` como `target.HP <= 25 && !target.KO && prob(ai_aggression)`.
	/// </summary>
	public const double VidaDeFinalizar = 0.25;

	/// <summary>Vida propria que faz FUGIR: `HP <= 25` (`NPCAI.dm:510`).</summary>
	public const double VidaQueFazFugir = 0.25;

	/// <summary>
	/// A CAUTELA acima da qual ele foge de verdade -- o `e_behavior_vals[1] &lt; 35` (`NPCAI.dm:510`)
	/// LIDO PELO AVESSO.
	///
	/// A coragem nao e um campo deste cerebro: ela ja mora aqui como <see cref="VidaCautelosa"/>,
	/// pela conversao do `Temperamento` (`0,9 * (1 - coragem)`). Entao `coragem &lt; 0,35` e o mesmo
	/// que `cautela &gt; 0,585`, e a conta e feita uma vez, aqui, com o nome do que ela significa.
	/// Guardar a coragem num segundo campo seria a mesma verdade escrita duas vezes -- e a segunda
	/// e a que envelhece quando alguem mexer na escala.
	/// </summary>
	public const double CautelaQueFazFugir = 0.585;

	/// <summary>
	/// A FURIA acima da qual ele solta RAJADA de socos, lida pelo avesso do peso do golpe:
	/// `ChanceDePesado = 0,10 + 0,5*furia` (`Temperamento`), entao `furia &gt;= 45` (`NPCAI.dm:412`)
	/// e o mesmo que `peso &gt;= 0,325`. Mesma disciplina da <see cref="CautelaQueFazFugir"/>.
	/// </summary>
	public const double PesoQueIndicaFuria = 0.325;

	/// <summary>Chance de a rajada de socos sair: `prob(45)` (`NPCAI.dm:412`).</summary>
	public const double ChanceDaBarragem = 0.45;

	/// <summary>Carrega ate esta fracao antes de voltar a lutar: `NPC_AI_RECHARGE_TO 0.75` (`NPCAI.dm:19`).</summary>
	public const double CarregarAte = 0.75;

	/// <summary>Distancia "segura" pra parar de recuar e comecar a carregar: `NPC_AI_RECHARGE_DIST 6` tiles (`NPCAI.dm:22`).</summary>
	public const float DistanciaSegura = 6 * ZoneCollision.TileSize;

	/// <summary>Teto do modo recarga: `NPC_AI_RECHARGE_MAX 200` tiques do DM = 20 s (`NPCAI.dm:23`).</summary>
	public const double PrazoDaRecarga = 20;

	/// <summary>Apanhou isto enquanto carregava: desiste. `NPC_AI_RECHARGE_ABORT_DMG 15` de HP (`NPCAI.dm:24`).</summary>
	public const double DanoQueAbortaACarga = 0.15;

	/// <summary>
	/// KI MINIMO PRA TENTAR TRANSFORMAR. **LITERAL**: `if(Ki &lt; MaxKi * 0.25) return`
	/// (`npc_try_transform`, `NPCAI.dm:365`).
	/// </summary>
	public const double KiParaTransformar = 0.25;

	/// <summary>Vida abaixo da qual vale a pena escalar: `HP &lt;= 45` (`NPCAI.dm:517`).</summary>
	public const double VidaQuePedeForma = 0.45;

	/// <summary>Alvo tantas vezes mais forte que eu -> escalar: `target.expressedBP &gt;= expressedBP * 1.5` (`NPCAI.dm:517`).</summary>
	public const double PoderQuePedeForma = 1.5;

	/// <summary>Carencia entre duas tentativas de escalar: `ai_powerup_cd = world.time + 150` = 15 s (`NPCAI.dm:240`).</summary>
	public const double CarenciaDeAscensao = 15;

	/// <summary>
	/// KI MINIMO PRA DECOLAR (fracao). **NAO HA ORIGINAL** -- a IA do DM nao voa. O numero e o mesmo
	/// da transformacao de proposito: os dois respondem a mesma pergunta ("da pra bancar o gesto ou
	/// vou cair dele?"), e um so numero e uma manivela a menos pra calibrar errado.
	/// </summary>
	public const double KiParaDecolar = 0.25;

	/// <summary>
	/// POUSAR DE PROPOSITO abaixo disto (fracao do tanque). **DESENHO NOVO.**
	///
	/// Ser derrubado do ceu e uma FALHA -- o corpo cai a 16 tiles por segundo e chega no chao sem
	/// guarda, sem folego e no meio do inimigo. Um lutador desce ANTES. O piso absoluto
	/// (<see cref="Voo.KiQueDerruba"/> x 4) anda junto porque aquele numero e absoluto: 5 de Ki e 5
	/// de Ki tanto pro saibaman quanto pro Freeza.
	/// </summary>
	public const double KiQueMandaPousar = 0.12;

	/// <summary>Inteligencia minima pra a receita de PAIRAR RASANTE existir. **DESENHO NOVO.**</summary>
	public const double InteligenciaQuePairaRasante = 0.6;

	/// <summary>
	/// Quantos segundos de voo a decolagem precisa poder pagar pra valer a pena. **DESENHO NOVO**
	/// (o DM nao voa) -- ver o uso, no ramo de alcancar.
	/// </summary>
	public const double SegundosDeVooQueValemADecolagem = 3;

	/// <summary>Chance de simplesmente ERRAR um reflexo. E o que faz o jogador poder vencer no ritmo.</summary>
	public const double ChanceDeErrar = 0.15;

	// =====================================================================
	// O TIRO DE LONGE. **NAO HA ORIGINAL NO DM** -- a IA de la nao usa tecnica ativa nenhuma
	// (`NPCAI.dm` nao chama `assignverb` nem nenhum dos verbs de skill). Sao numeros de desenho,
	// e cada um diz de onde saiu.
	// =====================================================================

	/// <summary>
	/// PRAZO DE UMA CONJURACAO, em segundos. Nao e o tempo de conjuracao da tecnica (esse e dela,
	/// e vem no <see cref="Tiro.TempoDeConjuracao"/>): e o teto do PLANO, pra um golpe que nunca
	/// completa -- porque o alvo fica entrando e saindo da janela -- nao virar um NPC plantado.
	///
	/// Mesmo papel do `PrazoDaRecarga` (o `RECHARGE_MAX 200` do DM), e por isso e a quarta parte
	/// dele: conjurar e um gesto curto; carregar Ki e um estado.
	/// </summary>
	public const double PrazoDaConjuracao = 5;

	/// <summary>
	/// DEPOIS DE ATIRAR, ESPERA ISTO antes de pensar em atirar de novo.
	///
	/// **NAO E A RECARGA DA TECNICA** -- essa mora no efeito, e e ele quem recusa (o `_solarPronto`
	/// do Solar Flare e o modelo). Isto aqui e a mesma coisa que a <see cref="CarenciaDeAscensao"/>
	/// resolve pro C: impedir que o cerebro martele uma porta fechada 4 vezes por segundo. Se a
	/// tecnica tiver recarga maior, quem manda e ela; se tiver menor, o NPC atira no ritmo daqui --
	/// e ser um pouco mais lento que o teorico e exatamente o que um jogador tambem e.
	/// </summary>
	public const double PausaDepoisDoTiro = 1.0;

	/// <summary>
	/// QUANTO RISCO DE ERRAR ELE ACEITA. Sai da <see cref="Inteligencia"/>: o bicho burro larga o
	/// golpe em alvo correndo no limite do alcance; o esperto espera o alvo parar.
	///
	/// E o mesmo desenho do `prob(ai_intelligence)` que gateia a recarga no DM (`NPCAI.dm:522`) --
	/// inteligencia decide QUE ERRO ele comete, e nao "o quanto ele acerta". Erro caracteristico e
	/// reconhecivel; erro aleatorio e indistinguivel de bug.
	/// </summary>
	private double ToleranciaDeErro => 0.75 - 0.5 * Math.Clamp(Inteligencia, 0, 1);

	/// <summary>Hesitacao, em segundos: quanto o corpo trava quando o mundo vira do avesso.</summary>
	public const double HesitacaoMin = 0.15, HesitacaoMax = 0.40;

	// =====================================================================
	// O SOPRO QUE ABRE ESPACO -- o `npc_kiai()` (NPCAI.dm:295), chamado em DOIS lugares
	// =====================================================================

	/// <summary>
	/// KI ABAIXO DO QUAL ele prefere empurrar a continuar trocando soco: `ki_ratio &lt;= 0.3`
	/// (`NPCAI.dm:399`). E o MESMO 0,30 do <see cref="KiBaixo"/> e nao um numero novo -- no original
	/// os dois sao o `NPC_AI_KI_LOW`, e a coincidencia e de sentido: e o ponto em que gastar Ki deixa
	/// de ser rotina e passa a ser decisao.
	/// </summary>
	public const double KiQuePedeSopro = KiBaixo;

	/// <summary>
	/// A que distancia o alvo conta como COLADO pro sopro: `d &lt;= 1` tile (`NPCAI.dm:399`).
	///
	/// E um tile, e nao a <see cref="DistanciaIdeal"/>: o sopro do port acerta o ARCO DA FRENTE a um
	/// tile e meio (`GameServer.Tecnicas.G6.cs`, os tres tiles do `Ki2.0/Kiai.dm:17-25`), entao pedir
	/// mais folga aqui seria gastar 50 de dreno num grito que nao encosta em ninguem.
	/// </summary>
	public const float DistanciaDoSopro = 1f * ZoneCollision.TileSize;

	/// <summary>
	/// Chance de soltar o sopro pra abrir espaco DURANTE a recarga: `prob(60)` (`NPCAI.dm:678`).
	///
	/// Ela e alta -- mais alta que qualquer coisa que ele faca em briga normal -- e o motivo esta na
	/// situacao: quem esta carregando **nao pode andar** (o portao do `PodeMexerOCorpo`), entao o
	/// unico jeito de tirar alguem de cima e empurrando.
	/// </summary>
	public const double ChanceDoSoproNaRecarga = 0.60;

	/// <summary>
	/// DEPOIS DE SOPRAR, ESPERA ISTO. **Nao e a recarga da tecnica** -- essa e o `kiaionCD` do DM
	/// (`NPCAI.dm:298`) e, aqui, o `_soproPronto` do proprio verb, que ja recusa por conta propria.
	///
	/// Isto e a mesma coisa que a <see cref="PausaDepoisDoTiro"/> e a <see cref="CarenciaDeAscensao"/>
	/// resolvem: impedir o cerebro de marretar uma porta fechada 30 vezes por segundo. Dois segundos
	/// porque o gesto e curto e a situacao que o pede (encurralado) nao passa sozinha.
	/// </summary>
	public const double PausaDepoisDoSopro = 2.0;

	// =====================================================================
	// O CIRCULO -- o `strafeState` (NPCAI.dm:551)
	// =====================================================================

	/// <summary>A que distancia ele orbita: `strafe_Dist = 3` tiles (`NPCAI.dm:53`).</summary>
	public const float DistanciaDeCircular = 3f * ZoneCollision.TileSize;

	/// <summary>
	/// Chance de ENTRAR no circulo, por decisao: `if(isBlaster && prob(4))` (`NPCAI.dm:507`).
	///
	/// A UNIDADE CONFERE, e neste projeto isso nunca e obvio: la o sorteio roda uma vez por volta do
	/// `attackState`, que dorme `chase_speed` -- 3 tiques do DM = 0,3 s. Aqui a decisao roda a cada
	/// <see cref="IntervaloDeDecisao"/> (0,20 a 0,30 s pelo tempero). Sao a mesma cadencia, entao o 4
	/// entra CRU -- ao contrario da guarda, que roda a 30 Hz e precisou do fator de conversao.
	/// </summary>
	public const double ChanceDeCircular = 0.04;

	/// <summary>Chance de LARGAR o circulo, por decisao: o `prob(10)` da saida (`NPCAI.dm:578`).</summary>
	public const double ChanceDeSairDoCirculo = 0.10;

	// =====================================================================
	// ESTADO VIVO
	// =====================================================================

	/// <summary>O que o jogo respondeu que este corpo pode. Escrito pelo servidor, a 1 Hz.</summary>
	public Capacidades Poderes;

	/// <summary>O plano em curso e ha quanto tempo ele esta de pe.</summary>
	public Plano Atual { get; private set; } = Plano.Nada;

	private double _relogioDeDecisao;
	private double _relogioDasCapacidades;
	private double _noPlano;

	// --- guarda ---
	private double _guardaAte;        // enquanto > 0, a guarda fica erguida
	private double _reacaoDaGuarda;   // atraso ate a guarda subir de fato

	// --- o RITMO do oponente (o que substitui a clarividencia) ---
	private const int MarcasDeGolpe = 4;
	private readonly double[] _quandoVi = new double[MarcasDeGolpe];
	private int _marcas;
	private double _relogio;          // tempo do cerebro, em segundos desde que ele nasceu

	// --- carga ---
	private double _vidaAoCarregar;
	private double _carregandoHa;

	// --- escalar ---
	private double _carenciaDeForma;

	// --- o tiro de longe (ver `Plano.Atirar`) ---
	private int _tiroEscolhido = -1;   // indice no `Poderes.DeLonge`; -1 = nenhum
	private double _conjurandoHa;
	private double _pausaDoTiro;
	private double _vidaAoConjurar;

	// --- o sopro que abre espaco (ver `PausaDepoisDoSopro`) ---
	private double _pausaDoSopro;

	// --- o circulo: pra que lado ele orbita NESTE plano (+1 ou -1) ---
	private float _maoDoCirculo = 1f;

	// --- humanidade ---
	private double _hesitaAte;
	private double _deriva;           // passeio lento do tempo de reacao (ver Reagir)
	private double _ultimoPoderDoAlvo;
	private double _voltaDoSoco;      // ritmo em RAJADA: ver EmRajada
	private int _golpesQueFaltam;     // quantos socos ainda saem NESTA rajada (ver EmRajada)
	private bool _finalizando;        // o modo FINALIZADOR desta decisao (ver Escolher)

	// --- as emocoes, e elas ANDAM durante a luta (o `behavior_check`, NPCAI.dm:769) ---
	private double _raiva;            // o `behavior_vals_t[2]`, 0..100
	private double _animo;            // a metade POSITIVA do `behavior_vals_t[1]`, 0..100
	private double _medo;             // a metade NEGATIVA dele -- ver `TicarEmocoes`
	private double _vidaNaUltimaLeitura = -1;   // o `keep_track_dmg`
	private double _relogioDasEmocoes;

	// =====================================================================
	// A LINGUA -- ver `Falatorio`
	// =====================================================================

	/// <summary>
	/// A BOCA DESTE CORPO. Nasce MUDA (`Ligado` falso) -- ver o campo la, e a razao e a mesma da
	/// `Percepcao.LinhaLivre`: **a direcao do erro foi escolhida**.
	///
	/// Quem liga e o <see cref="Npc.Temperamento"/>, ou seja: **so corpo que saiu de um molde do
	/// `npcs.json`**. Os tres cerebros montados a mao continuam calados de graca, e cada um por um
	/// motivo proprio que nao e economia:
	///
	///   * a FERA do Oozaru e a FURIA LENDARIA sao corpos de JOGADOR possuidos. Uma frase saindo dali
	///     apareceria no chat com o nome do jogador, dizendo o que ele nao digitou -- e o macaco sem
	///     controle nao fala, ele ruge (o `legendary_berserk_loop` nao tem uma linha de `say`);
	///   * o REFLEXO DA MENTE e a copia do proprio meditador: dois "Fulano" trocando farpas no chat
	///     seria a cena mais estranha do jogo.
	/// </summary>
	public readonly Falatorio Boca = new();

	/// <summary>
	/// A FRASE DESTE TIQUE, montada pelos pontos de fala e consumida uma vez no <see cref="Montar"/>.
	///
	/// `??=` em todo ponto de fala: **a primeira ocasiao do tique vence**, e isso e o original --
	/// cada ramo do `npc_combat_action` termina em `return` depois de falar (`NPCAI.dm:392, :402,
	/// :416`), entao nunca sai mais de uma frase por acao. O que nao coube neste tique nao fica na
	/// fila: ele simplesmente nao aconteceu, como no DM.
	/// </summary>
	private string? _fala;

	/// <summary>
	/// Quanto falta pra a proxima leitura dos AVISOS de recurso. O `npc_resource_warnings` e chamado
	/// pelo `checkState`, que dorme `sleep(5)` -- 0,5 s (`NPCAI.dm:813`). Nao e o relogio da fala
	/// (esse mora no <see cref="Falatorio"/>, e e de 30 s): e o de PERGUNTAR.
	/// </summary>
	private double _relogioDoAviso;

	/// <summary>Ver <see cref="Falatorio.PeriodoDaLeituraDeAvisos"/>.</summary>
	private const double PeriodoDoAviso = Falatorio.PeriodoDaLeituraDeAvisos;

	/// <summary>
	/// UMA FALA. Ponto unico: quem sabe da OCASIAO chama isto, e quem decide se sai som e a
	/// <see cref="Boca"/> (o relogio, a chance e o sorteio da frase moram la).
	/// </summary>
	private void Dizer(Momento m, Random rng) => _fala ??= Boca.DeCombate(m, rng);

	/// <summary>
	/// UMA FALA DE FORA DO CEREBRO -- e ha exatamente UMA, o contra-feixe.
	///
	/// ============================ POR QUE ESTA E PUBLICA E AS OUTRAS NAO ============================
	/// Porque o gesto que ela acompanha tambem mora fora: o contra-feixe e um REFLEXO do servidor
	/// (`GameServer.TickDoContraFeixe`), decidido antes da arvore de decisao e com recarga propria --
	/// exatamente como no DM, onde ele e a primeira coisa do `npc_combat_action`, antes de qualquer
	/// leitura de recurso (`NPCAI.dm:374-380`).
	///
	/// Ela nao abre uma porta nova: o que o servidor recebe e a MESMA frase, cobrada pelo MESMO
	/// relogio de combate. Se o contra-feixe disparar no mesmo segundo em que o corpo ia gritar
	/// socando, sai um dos dois -- que e o comportamento de la e o de uma pessoa.
	/// ==========================================================================================
	/// </summary>
	public string? FraseDoContraFeixe(Random rng) => Boca.DeCombate(Momento.ContraFeixe, rng);

	/// <summary>
	/// LIGA O RELATO. Desligado por padrao, e a razao e de ORCAMENTO e nao de gosto: montar a frase
	/// e uma string interpolada por decisao (4 Hz por corpo), e com 20 NPCs isso e lixo constante
	/// pro coletor -- que numa sessao de horas vira pausa, e ninguem liga a pausa a IA.
	///
	/// Quem quiser entender uma decisao liga isto naquele corpo. E o mesmo desenho do `_diagGolpe`
	/// do servidor: o diagnostico existe sempre, e custa so quando alguem esta olhando.
	/// </summary>
	public bool Explicando;

	/// <summary>
	/// A ULTIMA CONTA, pra quem quiser olhar. Vazia enquanto <see cref="Explicando"/> for falso.
	/// Nao alimenta decisao nenhuma -- e a metade de "a decisao vira dado" que cabe nesta camada.
	/// </summary>
	/// (E o relato e escrito com um `if (Explicando)` na frente e nao por um `Func&lt;string&gt;`
	/// preguicoso: a <see cref="Percepcao"/> viaja como `in`, e parametro `in` nao pode ser
	/// capturado por lambda. O `if` cru tambem e o unico jeito de a interpolacao nem existir.)
	public string Porque { get; private set; } = "";

	/// <summary>
	/// AS CAPACIDADES VENCERAM? O servidor pergunta, e so paga a leitura (que varre catalogo de
	/// formas) quando este metodo diz sim. E o rodizio da camada cara: 1 Hz por corpo.
	/// </summary>
	public bool PrecisaLerCapacidades(double dt)
	{
		_relogioDasCapacidades -= dt;
		if (_relogioDasCapacidades > 0) return false;
		_relogioDasCapacidades = 1.0;
		return true;
	}

	/// <summary>
	/// ESPALHA A LEITURA CARA NO TEMPO. Chamado UMA vez, no nascimento do corpo.
	///
	/// ============================ 1 Hz POR CORPO NAO E 1 Hz POR TIQUE ============================
	/// `_relogioDasCapacidades` nasce em zero, entao a primeira pergunta devolve verdadeiro e so
	/// dai em diante o rodizio de 1 s comeca a valer. Enquanto os corpos dirigidos nasciam um a um
	/// (o clone, a fera) isso nao tinha consequencia. Com POVOAMENTO tem: corpos nascidos no mesmo
	/// tique ficam EM FASE pra sempre, e a leitura -- que varre o catalogo de formas e custa ~2,3 us
	/// por corpo -- passa a acontecer toda inteira num tique so, uma vez por segundo.
	///
	/// Com 80 corpos numa zona sao 0,18 ms de pico contra 0,006 ms de media. A media esconde o pico,
	/// e o pico e o que trava. E o defeito que a armadilha 2 da PARTE 3 descreve com outro nome:
	/// **o custo estava fora da janela medida** -- so que aqui a janela e estatistica, nao temporal.
	///
	/// Uma fase inicial diferente por corpo transforma o pico numa rotacao plana. Nao ha estado novo:
	/// e o mesmo campo, comecando em outro lugar.
	/// ======================================================================================
	/// </summary>
	/// <param name="segundos">
	/// Onde na roda de 1 s este corpo comeca. Fora de [0,1] e apertado pra dentro -- um valor maior
	/// que o periodo atrasaria a PRIMEIRA leitura, e um corpo que age com as capacidades zeradas nao
	/// sabe voar, nem aparar, nem transformar.
	/// </param>
	public void DesfasarCapacidades(double segundos) =>
		_relogioDasCapacidades = Math.Clamp(segundos, 0, 1);

	/// <summary>
	/// EU VI UM GOLPE CHEGAR. Chamado pelo servidor quando este corpo e ALVO de um ataque --
	/// acertando, sendo aparado ou errando, tanto faz: o que se ve e o gesto.
	///
	/// ============================ E ISTO E TUDO O QUE ELE SABE DO OPONENTE ============================
	/// O servidor tem na mao o `alvo.Combate.Recarga` e o `alvo.AtaqueAte` -- daria pra saber o
	/// instante exato do proximo soco e apara-lo sempre. Seria uma IA impossivel de enganar e
	/// chata de enfrentar, porque o jogador nao teria o que descobrir.
	///
	/// Em vez disso ele guarda as ULTIMAS QUATRO vezes em que apanhou, estima a cadencia pela
	/// media dos intervalos e prepara a guarda pra o proximo. **Erra quando o jogador quebra o
	/// ritmo** -- que e exatamente a coisa que se quer que o jogador descubra sozinho.
	/// ============================================================================================
	/// </summary>
	public void ViuUmGolpe()
	{
		_quandoVi[_marcas % MarcasDeGolpe] = _relogio;
		_marcas++;
	}

	/// <summary>
	/// A CADENCIA ESTIMADA do oponente, em segundos. Zero = ainda nao da pra dizer.
	///
	/// Duas marcas ja dao um intervalo, mas um intervalo so e ruido; com tres ele ja tem media de
	/// dois. O PRIMEIRO golpe de uma luta portanto NUNCA e aparado por antecipacao, e isso e o
	/// certo: ninguem apara o que ainda nao viu acontecer.
	/// </summary>
	private double Cadencia()
	{
		int n = Math.Min(_marcas, MarcasDeGolpe);
		if (n < 3) return 0;

		// as marcas estao num anel; ordena por valor, que e o mesmo que ordenar por tempo
		Span<double> t = stackalloc double[MarcasDeGolpe];
		for (int i = 0; i < n; i++) t[i] = _quandoVi[i];
		t[..n].Sort();

		double soma = 0;
		for (int i = 1; i < n; i++) soma += t[i] - t[i - 1];
		double media = soma / (n - 1);

		// cadencia absurda (dois golpes no mesmo tique, ou um minuto entre eles) nao e ritmo
		return media is > 0.15 and < 4.0 ? media : 0;
	}

	// =====================================================================
	// AS EMOCOES -- o `behavior_check` (NPCAI.dm:769) e o `e_behavior_vals` (:818-824)
	// =====================================================================

	/// <summary>
	/// De quanto em quanto tempo as emocoes andam. O `checkState` do DM dorme `sleep(5)` -- 0,5 s --
	/// e chama o `behavior_check` com `prob(60)` (`NPCAI.dm:813`): uma leitura a cada ~0,83 s.
	/// </summary>
	public const double PeriodoDasEmocoes = 0.83;

	/// <summary>
	/// A FURIA DE AGORA. E o <see cref="ChanceDePesado"/> (a base, o `behavior_vals[2]`) MAIS o que
	/// a luta acumulou (<see cref="_raiva"/>, o `behavior_vals_t[2]`) -- a soma que o DM chama de
	/// `e_behavior_vals` (`NPCAI.dm:823`).
	///
	/// ============================ POR QUE A BASE CONTINUA SENDO UM CAMPO SEPARADO ============================
	/// Porque a fera e a furia lendaria ESCREVEM a base na mao, e a bancada afirma o numero delas.
	/// Se a emocao sobrescrevesse o campo, o primeiro soco levado apagaria o tempero do macaco e
	/// ninguem ligaria uma coisa na outra. Base + acumulador = expresso e a forma do proprio DM, e
	/// ela tem a propriedade que interessa aqui: **zero continua zero** (ver <see cref="CautelaExpressa"/>).
	/// ====================================================================================================
	/// </summary>
	public double FuriaExpressa => Math.Clamp(ChanceDePesado + 0.5 * (PorDegraus(_raiva) / 100.0), 0, 1);

	/// <summary>
	/// A CAUTELA DE AGORA. Vencer da CORAGEM e apanhar da MEDO -- as duas metades do
	/// `behavior_vals_t[1]`, e coragem e o avesso da cautela.
	///
	/// ============================ POR QUE MULTIPLICA EM VEZ DE SOMAR ============================
	/// O DM soma (`base*mod + t`), e somar aqui teria um efeito que ninguem quer: a fera
	/// (`VidaCautelosa = 0`) ganharia medo depois de apanhar um pouco, e o macaco sem controle
	/// passaria a recuar. Multiplicando, **zero continua zero em qualquer emocao** -- quem nunca teve
	/// medo nao aprende a ter, e quem nunca recuou nao comeca -- e quem TEM cautela a dobra quando a
	/// luta desanda e a corta quando esta ganhando. A propriedade que a soma dava (a emocao move o
	/// tempero) esta inteira; a que ela dava de brinde (a emocao INVENTA tempero) e que sai.
	/// ======================================================================================
	/// </summary>
	public double CautelaExpressa =>
		Math.Clamp(VidaCautelosa * (1 + (PorDegraus(_medo) - PorDegraus(_animo)) / 100.0), 0, 1);

	/// <summary>
	/// O DEGRAU DA EMOCAO -- *"logic rounds off the emotions: 100 logic will mean each emotion can
	/// be 0, 50, or 100"* (`NPCAI.dm:822`), com o degrau `max(1, logic/2)`.
	///
	/// E o que faz o bicho ESPERTO ter humor grosso (ou esta calmo ou esta furioso) e o burro ter
	/// humor fino. Vale so pro ACUMULADOR: a base vem do molde e nao pode ser arredondada, senao
	/// ligar as emocoes teria mexido no tempero de todo corpo que ja existia.
	/// </summary>
	private double Degrau => Math.Max(1, Inteligencia * 100 / 2);

	/// <summary>
	/// O acumulador arredondado pro degrau -- e APERTADO em [0,100] DEPOIS do arredondamento, que e
	/// a ordem que importa: arredondar 100 pra cima num degrau de 17,5 daria 105, e o teto de uma
	/// emocao nao pode depender do tamanho do degrau de quem a sente.
	/// </summary>
	private double PorDegraus(double v) => Math.Clamp(Math.Round(v / Degrau) * Degrau, 0, 100);

	/// <summary>
	/// COMO ESTA INDO A LUTA -- e as emocoes andam com a resposta. O `behavior_check` (`NPCAI.dm:769`).
	///
	/// ============================ ISTO E "REAGIR AO ESTADO" NO SENTIDO LITERAL ============================
	/// Ate aqui o tempero de um corpo era CONSTANTE: o mesmo peso de golpe no primeiro soco e no
	/// ultimo, o mesmo limiar de recuo com a vida cheia e no fio. O DM nao e assim -- ele mede o
	/// FLUXO da luta (quanto de vida foi embora desde a ultima leitura, e o quanto o outro e mais
	/// forte) e move a raiva e o animo com ele. Perder deixa o bicho furioso; ganhar deixa ele
	/// afoito. **E o mesmo corpo lutando diferente no minuto seis do que no minuto um.**
	/// ================================================================================================
	///
	/// ============================ UMA CORRECAO DE SINAL, E ELA ESTA DECLARADA ============================
	/// O original escreve `flow += keep_track_relation`, com `relation = alvo.expressedBP / meuBP`
	/// (`:782`) -- ou seja **quanto mais forte o inimigo, mais o fluxo vai pro lado de "estou
	/// ganhando"**. Isso contradiz o proprio comentario do autor no topo do arquivo (*"rage ... seeing
	/// it's kin get killed, etc will cause strength increases"*) e o uso que ele faz do numero: um
	/// NPC diante de alguem tres vezes mais forte ficaria CALMO.
	///
	/// Aqui o termo entra com o sinal que a frase pede (`- (razao - 1)`): parelho nao mexe em nada,
	/// inimigo mais forte empurra pro medo e pra raiva, inimigo mais fraco empurra pro animo. A
	/// forma da conta e a de la; o sinal e o que ela queria dizer.
	/// ================================================================================================
	/// </summary>
	private void TicarEmocoes(in Percepcao p, double dt, Random rng)
	{
		_relogioDasEmocoes -= dt;
		if (_relogioDasEmocoes > 0) return;
		_relogioDasEmocoes = PeriodoDasEmocoes;

		// SEM ALVO, AS EMOCOES ZERAM. E o `resetState`, que limpa `behavior_vals_t` e
		// `e_behavior_vals` inteiros ao desengajar (`PlanetPopulation.dm:174-176`). Sem isto um
		// cidadao carregaria a raiva de uma briga de ontem pra o passeio de hoje.
		if (!p.TemAlvo) { _raiva = 0; _animo = 0; _medo = 0; _vidaNaUltimaLeitura = -1; return; }

		// A PRIMEIRA LEITURA SO ANCORA. `if(!keep_track_dmg) keep_track_dmg = HP` (`:784`): sem
		// isto o primeiro fluxo seria a vida inteira de uma vez.
		if (_vidaNaUltimaLeitura < 0) { _vidaNaUltimaLeitura = p.VidaFrac; return; }

		double fluxo = (p.VidaFrac - _vidaNaUltimaLeitura) * 100;   // o DM conta HP de 0 a 100
		_vidaNaUltimaLeitura = p.VidaFrac;
		if (fluxo is > -1 and < 1) fluxo = 0;                       // `if(flow < 1 && flow > -1) flow = 0`
		fluxo -= p.RazaoDePoder - 1;                                // ver a correcao de sinal, acima

		if (fluxo < 0)
		{
			// `flow = min(flow,-1); behavior_vals_t[2] += 2*(-1/flow)` -- a raiva sobe RAPIDO quando
			// se esta perdendo pouco e devagar quando se esta perdendo feio (*"rage is limited by
			// the flow var"*, o comentario do autor em `:791`).
			//
			// ============================ E A OUTRA METADE, QUE NO DM E LETRA MORTA ============================
			// A mesma linha do original tem `behavior_vals_t[1] += flow` -- **o MEDO** --, e o autor
			// escreveu ao lado *"tick fear and rage"*. So que a parcela e negativa e o proprio
			// `checkState` aperta o acumulador em [0,100] na volta seguinte (`:819`): o medo some
			// antes de chegar a `e_behavior_vals`, e a coragem de um NPC do DM **so pode subir**.
			// Metade do sistema de emocoes de la nunca roda.
			//
			// Aqui ele e guardado num acumulador PROPRIO (`_medo`) e entra na conta com o sinal que
			// a frase do autor pede. E a mesma decisao (e a mesma justificativa) da correcao de
			// sinal la em cima: a forma da conta e a de la, o que muda e o que ela queria dizer.
			// Sem esta metade, "reagir ao estado" so vale pra o lado bom: o bicho ficaria mais afoito
			// ganhando e nunca mais cauteloso apanhando -- que e o oposto do que se ve numa luta.
			// =============================================================================================
			fluxo = Math.Min(fluxo, -1);
			_raiva += 2 * (-1 / fluxo);
			_medo += -fluxo;

			// ============================ E AQUI ELE RECLAMA -- `NPCAI.dm:793-797` ============================
			// `if(IsInFight && flow < -5 && prob(isBoss ? 50 : 25))`, e as DUAS frases: com a vida
			// acima de 30 sai um "Tch!", abaixo sai um "Eu nao caio aqui!".
			//
			// O LIMIAR DE -5 E O QUE FAZ ISSO NAO SER RUIDO. Um arranhao nao arranca frase nenhuma:
			// -5 e a vida caindo 5 pontos (de 100) desde a leitura anterior, ou seja **num intervalo
			// de 0,83 s**. E o golpe que doeu, e nao o desgaste.
			//
			// E ela sai DAQUI, e nao de um gancho no resolvedor de dano, porque e aqui que o dado ja
			// esta na mao: o fluxo ja foi calculado pra mover a raiva e o medo. Um ponto de fala no
			// caminho do golpe precisaria de um canal novo do servidor pro cerebro -- e a `Percepcao`
			// nao carrega "quanto doeu", ela carrega "quanta vida sobrou".
			// ==============================================================================================
			if (fluxo < -5)
				Dizer(p.VidaFrac <= 0.30 ? Momento.NoFio : Momento.Apanhar, rng);
		}
		else
		{
			// `flow = max(flow,1); t[1] += flow; t[2] -= 2*(1/flow)` -- ganhar da coragem e esfria.
			fluxo = Math.Max(fluxo, 1);
			_animo += fluxo;
			_raiva -= 2 * (1 / fluxo);
		}

		_raiva = Math.Clamp(_raiva, 0, 100);
		_animo = Math.Clamp(_animo, 0, 100);
		_medo = Math.Clamp(_medo, 0, 100);   // o mesmo teto do `clamp(...,0,100)` de `:819`
	}

	/// <summary>
	/// PENSA E DEVOLVE O QUE APERTAR. Chamado a CADA tique -- os reflexos moram aqui dentro, e o
	/// plano so e reconsiderado quando o relogio de decisao vira.
	/// </summary>
	public Comando Pensar(in Percepcao p, double dt, Random rng)
	{
		_relogio += dt;
		_noPlano += dt;
		if (_guardaAte > 0) _guardaAte -= dt;
		if (_reacaoDaGuarda > 0) _reacaoDaGuarda -= dt;
		if (_carenciaDeForma > 0) _carenciaDeForma -= dt;
		if (_hesitaAte > 0) _hesitaAte -= dt;
		if (_voltaDoSoco > 0) _voltaDoSoco -= dt;
		if (_pausaDoTiro > 0) _pausaDoTiro -= dt;
		if (_pausaDoSopro > 0) _pausaDoSopro -= dt;
		if (Atual == Plano.Recuperar) _carregandoHa += dt;

		// A FRASE E DESTE TIQUE E DE MAIS NENHUM. Zerada aqui, no topo, antes de qualquer ponto de
		// fala poder escrever: sem isto uma frase recusada pelo `Comando` (um corpo caido, por
		// exemplo) ficaria pendurada e sairia num tique qualquer, sem relacao com o que aconteceu.
		_fala = null;
		Boca.Tique(dt);

		// A CONJURACAO ANDA SOZINHA enquanto o plano estiver de pe. Quem a ZERA e a receita, quando
		// o corpo precisa se mexer (alvo colado demais) -- pelo mesmo motivo que a recarga de Ki nao
		// acontece andando: um gesto que exige o pe plantado nao sobrevive a um passo.
		if (Atual == Plano.Atirar && _pausaDoTiro <= 0) _conjurandoHa += dt;

		// CAIDO NAO FAZ NADA. Nem aperta tecla, nem guarda -- o `Guardar` do Core ja recusa em
		// corpo nocauteado, mas mandar o comando mesmo assim seria a IA apertando teclas que a
		// tela do jogador nem aceita. **E nem fala**: quem esta no chao nao provoca ninguem.
		if (p.Caido) { Atual = Plano.Nada; Porque = "caido"; return Comando.Nenhum; }

		// ---- OS AVISOS DE RECURSO, no relogio proprio deles (0,5 s pra OLHAR, 30 s pra FALAR) ----
		// Eles ficam ANTES da decisao de proposito: no DM quem os chama e o `checkState`, que roda em
		// paralelo ao `attackState` e nao dentro dele. Aqui "em paralelo" quer dizer "num relogio
		// proprio, sem depender de nenhum plano" -- e por isso um corpo que esta FUGINDO tambem
		// reclama de estar sem folego, que e o momento em que a frase mais faz sentido.
		_relogioDoAviso -= dt;
		if (_relogioDoAviso <= 0)
		{
			_relogioDoAviso = PeriodoDoAviso;

			// SEM ALVO, ESQUECE O QUE JA AVISOU -- o `resetState` (`NPCAI.dm:732-734`). E o mesmo
			// gesto (e a mesma linha) do reset das emocoes: desengajar limpa o que a briga acumulou.
			// O AVISO TEM PRECEDENCIA sobre a fala de combate deste tique (todos os pontos de fala
			// abaixo usam `??=`), e a razao e a raridade: um aviso sai a cada 30 s no MELHOR caso e
			// diz uma coisa que o jogador nao tinha como saber -- que o oponente esta no limite. Um
			// "Toma essa!" a mais nao diz nada e sai a cada 5 s.
			if (!p.TemAlvo) Boca.Esquecer();
			else _fala ??= Boca.Aviso(p, rng);
		}

		// --- 1. REFLEXOS (30 Hz, por fora da decisao) -------------------------
		bool susto = Sustos(p, rng);
		Reflexos(p, rng);

		// --- 1b. AS EMOCOES (o relogio mais lento dos tres, ~0,83 s) ----------
		// Elas nao decidem nada: elas mexem no TEMPERO com que as outras duas camadas decidem. E
		// por isso que este relogio pode ser o mais folgado -- humor nao muda de quadro em quadro.
		TicarEmocoes(p, dt, rng);

		// --- 2. DECISAO (na cadencia do tempero) ------------------------------
		if (_relogioDeDecisao <= 0 || susto)
		{
			_relogioDeDecisao = IntervaloDeDecisao;
			Repensar(p, rng);
		}
		else _relogioDeDecisao -= dt;

		// --- 3. O COMANDO, montado do plano + reflexos ------------------------
		return Montar(p, rng);
	}

	/// <summary>
	/// O QUE ASSUSTA: o alvo ficou muito mais forte de repente (transformou).
	///
	/// A hesitacao que isso produz e curta (0,15-0,40 s) e nao e cosmetica: durante ela o corpo
	/// recua guardando em vez de continuar avancando pra dentro de um golpe que agora vale o dobro.
	/// E tambem e o unico jeito de a transformacao do JOGADOR ter um efeito visivel no oponente --
	/// sem isto o NPC nao muda nada quando voce vira Super Saiyajin na cara dele.
	/// </summary>
	private bool Sustos(in Percepcao p, Random rng)
	{
		if (!p.TemAlvo) { _ultimoPoderDoAlvo = 0; return false; }

		double antes = _ultimoPoderDoAlvo;
		_ultimoPoderDoAlvo = p.PoderDoAlvo;
		if (antes <= 0 || p.PoderDoAlvo <= antes * 1.5) return false;

		_hesitaAte = HesitacaoMin + rng.NextDouble() * (HesitacaoMax - HesitacaoMin);
		_guardaAte = Math.Max(_guardaAte, _hesitaAte);
		Porque = "susto: o alvo ficou mais forte";
		return true;
	}

	/// <summary>
	/// OS REFLEXOS. Rodam a cada tique porque bloquear um soco nao pode esperar a proxima pensada.
	///
	/// Sao os tres do desenho, e cada um responde a uma coisa que ja aconteceu:
	///   1. RITMO       -- o proximo golpe esta pra chegar (ver <see cref="Cadencia"/>);
	///   2. PRESSAO     -- estou atordoado ou muito ferido, com ele colado (`npc_defensive_check`);
	///   3. SOLTAR      -- a pressao passou (`prob(40)` do mesmo proc).
	/// </summary>
	private void Reflexos(in Percepcao p, Random rng)
	{
		// A GUARDA DE VERDADE SOBE QUANDO O ATRASO DE REACAO VENCE. Sem isto o reflexo seria
		// instantaneo, que e a marca registrada de robo.
		if (_reacaoDaGuarda > 0) return;
		if (Disciplina <= 0) { _guardaAte = 0; return; }   // a fera nao apara: nao ha o que reagir
		if (!Poderes.TemComQueAparar) { _guardaAte = 0; return; }

		// ============================ GUARDA QUE O TANQUE NAO PAGA E GUARDA QUE CAI ============================
		// O `MeleeResolver` derruba a guarda de quem nao tem Ki pro golpe aparado, na mesma linha em
		// que derruba a de quem nao tem braco: `else if (d.F.Ki < custoKi) d.Guardar(false)`
		// (`MeleeResolver.cs:139`). Insistir nela nao protege nada -- so mantem o corpo com o gesto
		// erguido enquanto leva o soco inteiro.
		//
		// E o MESMO argumento (e o mesmo lugar) do `TemComQueAparar`, que ja estava escrito uma
		// linha acima: *"ela existe aqui pra a IA nao INSISTIR numa guarda que o resolvedor vai
		// derrubar"*. O numero vem das `Capacidades` -- `MaxKi * CombatKnobs.CustoKiDaGuarda`,
		// perguntado ao jogo a 1 Hz --, e era o terceiro campo extraido sem consumidor.
		// ==================================================================================================
		if (p.Ki < Poderes.CustoDaGuarda) { _guardaAte = 0; return; }

		if (!p.TemAlvo || p.AlvoCaido) return;

		bool colado = p.Distancia <= DistanciaIdeal * 2f && p.EleMeAlcanca;

		// --- 3. SOLTAR: a pressao passou -----------------------------------
		// ============================ A CHANCE DO DM E POR VOLTA DO LACO, NAO POR TIQUE ============================
		// `prob(40)` (`NPCAI.dm:289`) roda dentro do `chaseState`, que dorme `chase_speed = 3`
		// tiques do DM -- 0,3 s. Aqui o reflexo roda a 30 Hz, ou seja 9 vezes naquele mesmo tempo.
		// Copiar o 40 cru daria uma guarda que cai no primeiro quadro em que a pressao alivia.
		//
		// A conversao honesta e `1 - (1-p)^9 = 0,40` -> p ~= 0,055; o fator 0,1 da 0,040, que fica
		// do lado seguro (solta um pouco mais devagar do que o DM). O numero exato importa menos
		// que a UNIDADE estar certa -- e este projeto ja pagou caro por um `sleep` convertido pela
		// cadencia errada.
		// ====================================================================================================
		if (_guardaAte > 0)
		{
			if (!p.Atordoado && p.VidaFrac > VidaAliviada && rng.NextDouble() < ChanceDeSoltarAGuarda * 0.1)
				_guardaAte = 0;
			return;
		}

		if (!colado) return;

		// ERRO OCASIONAL. Ele nao e ruido: e o que faz o jogador poder VENCER no ritmo, e o que
		// impede a taxa de bloqueio de ser 100% -- que e o tipo de numero que denuncia um robo.
		if (rng.NextDouble() < ChanceDeErrar) return;

		// --- 2. PRESSAO (o `npc_defensive_check`, NPCAI.dm:291) -------------
		bool pressionado = p.Atordoado || p.VidaFrac <= VidaPressionada;
		bool porPressao = pressionado && Disciplina >= 0.35 && rng.NextDouble() < ChanceDeApararSobPressao;

		// --- 1b. CANSACO: a POSTURA DEFENSIVA (`NPCAI.dm:525-527`) ----------
		// `if(sr <= NPC_AI_STAM_LOW && prob(ai_intelligence))` e, dentro, `prob(NPC_AI_DEF_BLOCK_PROB)`
		// com o alvo colado -- **dois** sorteios, e por isso o produto: quem esta sem folego nao
		// consegue mais trocar soco, entao o esperto passa a APARAR em vez de continuar batendo.
		//
		// A conversao pra tique e a mesma da guarda que se solta (o `0,1` explicado logo acima): o
		// 55 do DM e por volta do laco de 0,3 s, e este reflexo roda 9 vezes naquele mesmo tempo.
		bool porCansaco = p.FolegoFrac <= FolegoBaixo
						  && rng.NextDouble() < Inteligencia * ChanceDaGuardaPorCansaco * 0.1;

		// --- 1. RITMO (o brace do `attack()`, NPCAI.dm:190) -----------------
		bool porRitmo = false;
		double cad = Cadencia();
		if (cad > 0)
		{
			double desdeOUltimo = _relogio - UltimaMarca();
			// a janela abre um tempo de reacao ANTES do golpe esperado e fecha logo depois dele:
			// antecipar cedo demais e guardar a luta inteira, e tarde demais e nao guardar.
			double falta = cad - desdeOUltimo;
			if (falta <= TempoDeReacao && falta > -cad * 0.4)
				porRitmo = rng.NextDouble() < ChanceDeAntecipar + 0.40 * Disciplina;
		}

		if (!porPressao && !porRitmo && !porCansaco) return;

		_reacaoDaGuarda = Reagir(rng);
		_guardaAte = _reacaoDaGuarda + GuardaMin + rng.NextDouble() * (GuardaMax - GuardaMin);
	}

	private double UltimaMarca()
	{
		double maior = 0;
		for (int i = 0; i < MarcasDeGolpe; i++) if (_quandoVi[i] > maior) maior = _quandoVi[i];
		return maior;
	}

	/// <summary>
	/// UM TEMPO DE REACAO, com variacao E com DERIVA.
	///
	/// ============================ DERIVA, E NAO RUIDO BRANCO ============================
	/// Sortear um numero independente a cada vez produz um oponente que reage em 120 ms, depois em
	/// 380, depois em 130 -- e isso NAO le como pessoa, le como tremor. Gente tem periodos: fica
	/// lenta por uns segundos, depois afia. Entao o sorteio de cada evento e um SINO estreito em
	/// volta de um centro que passeia devagar (<see cref="_deriva"/>), e e o passeio que da a
	/// impressao de atencao variando.
	/// ================================================================================
	/// </summary>
	private double Reagir(Random rng)
	{
		_deriva = Math.Clamp(_deriva + (rng.NextDouble() - 0.5) * 0.06, -0.35, 0.35);

		// sino barato: a soma de dois uniformes ja concentra no meio e some nas pontas
		double sino = (rng.NextDouble() + rng.NextDouble()) * 0.5;
		double t = TempoDeReacao * (1 + _deriva) * (0.6 + 0.8 * sino);
		return Math.Max(ReacaoMinima, t);
	}

	/// <summary>
	/// A DECISAO. Escolhe UM plano, e o compromisso o segura por
	/// <see cref="TempoMinimoNoPlano"/> a menos que uma INTERRUPCAO NOMEADA o quebre.
	///
	/// ============================ POR QUE O COMPROMISSO E O QUE FAZ PARECER GENTE ============================
	/// Sem ele, duas opcoes quase empatadas trocam de lugar a cada sorteio e o corpo fica indo e
	/// vindo no lugar -- o defeito que todo mundo reconhece como "IA" sem saber nomear. Com ele, o
	/// NPC **se compromete com um erro** por mais de um segundo, que e o que uma pessoa faz.
	///
	/// As interrupcoes sao nomeadas de proposito (e nao "se a pontuacao mudou muito"): cada uma e
	/// uma frase que da pra ler em jogo -- *"parei de carregar porque ele chegou perto"*.
	/// ====================================================================================================
	/// </summary>
	private void Repensar(in Percepcao p, Random rng)
	{
		// ============================ UM TIRO MORRE SEM A DECISAO MUDAR DE IDEIA ============================
		// Esta linha existe por causa de um buraco que a bancada achou, e o buraco e sobre a FORMA do
		// motor e nao sobre o tiro: as interrupcoes nomeadas so sao consultadas quando o plano NOVO e
		// diferente do atual (`if (novo == antes) return`). Um corpo apanhando no meio da conjuracao
		// continua achando que atirar e a melhor ideia -- o plano escolhido continua sendo `Atirar` --
		// e o `Interrompe` nunca chegava a ser perguntado. O golpe saia como se nada tivesse acontecido.
		//
		// As outras receitas nao tinham o problema porque as condicoes delas mudam a ESCOLHA (o tanque
		// enche e a recarga deixa de ser escolhida). Conjurar e o primeiro gesto deste cerebro cujo
		// fracasso nao muda de opiniao -- e por isso o aborto e explicito, aqui, antes de escolher.
		//
		// E ele COBRA a pausa: quem apanha no meio do gesto perde o gesto e mais um tempo, senao o
		// aborto vira um recomeco instantaneo e o jogador que acertou o soco nao ganhou nada com isso.
		// ==============================================================================================
		if (Atual == Plano.Atirar && TiroMorreu(p)) AbortarTiro();

		Plano antes = Atual;
		Plano novo = Escolher(p, rng);
		if (novo == antes) return;

		if (_noPlano < TempoMinimoNoPlano && !Interrompe(p, novo)) return;
		Atual = novo;
		_noPlano = 0;

		// o `startHP` do `rechargeState` (NPCAI.dm:671): a conta do "apanhei demais carregando" e
		// contra a vida de QUANDO COMECOU, e nao contra um limiar absoluto.
		//
		// E A FRASE DA RECARGA sai AQUI, na entrada do plano, porque e onde ela sai la: a PRIMEIRA
		// linha do `rechargeState` e `npc_warn_say(pick(npc_recharge_lines))` (`:670`). Ela paga o
		// relogio dos AVISOS (e nao o de combate), tambem como la -- e por isso ela nao rouba a vez
		// do "Toma essa!" da troca de socos.
		if (novo == Plano.Recuperar)
		{
			_vidaAoCarregar = p.VidaFrac;
			_carregandoHa = 0;
			_fala ??= Boca.DeRecarga(rng);
		}

		// A MAO DO CIRCULO, SORTEADA UMA VEZ. Ver `Plano.Circular`: por tique seria tremor, e o
		// compromisso -- que e o que impede o tremor entre PLANOS -- nao alcanca o que acontece
		// dentro de uma receita.
		if (novo == Plano.Circular) _maoDoCirculo = rng.Next(2) == 0 ? 1f : -1f;

		// MESMA CONTA PRA CONJURACAO, e pelo mesmo motivo: quem apanha no meio do gesto larga o
		// gesto. Sem zerar o relogio aqui, um plano de atirar retomado herdaria a conjuracao do
		// anterior e o golpe sairia no primeiro tique -- sem a parada que o jogador precisa ver.
		if (novo == Plano.Atirar) { _vidaAoConjurar = p.VidaFrac; _conjurandoHa = 0; }
	}

	/// <summary>
	/// AS INTERRUPCOES NOMEADAS -- o que quebra um compromisso antes da hora.
	///
	/// Cada linha e um acontecimento, e nao uma comparacao de pontos: e por isso que da pra
	/// explicar o comportamento pra quem esta jogando contra.
	/// </summary>
	private bool Interrompe(in Percepcao p, Plano novo) =>
		!p.TemAlvo || p.AlvoCaido                                        // o alvo saiu de cena
		|| novo == Plano.Nada
		|| novo == Plano.Fugir                                           // quase morto: nao se "termina o plano" primeiro
		|| (Atual == Plano.Fugir && p.VidaFrac > VidaQueFazFugir)        // `if(src.HP > 25) chaseState` (NPCAI.dm:650)
		|| (Atual == Plano.Recuperar && p.Distancia < DistanciaSegura * 0.6f)   // ele chegou perto
		|| (Atual == Plano.Recuperar && p.VidaFrac < _vidaAoCarregar - DanoQueAbortaACarga)
		|| (Atual == Plano.Recuperar && _carregandoHa > PrazoDaRecarga)
		|| (Atual == Plano.Recuperar && p.KiFrac >= CarregarAte)
		|| (Atual == Plano.Alcancar && p.AlcancoPelaAltura)                     // ja alcanco: chega de subir
		|| (Atual == Plano.Escalar && !Poderes.PodeSubirForma && !Poderes.FormaSoFaltaKi);
	// O TIRO NAO APARECE NESTA LISTA de proposito: ele e abortado ANTES da escolha (ver `Repensar`),
	// porque a morte dele nao muda o plano escolhido -- e uma linha aqui seria codigo inalcancavel.

	/// <summary>
	/// A ORDEM DAS PERGUNTAS. E a mesma do `chaseState` do DM (`NPCAI.dm:510-529`): fugir/escalar
	/// primeiro (sao decisoes sobre a luta inteira), recurso depois, e so entao a briga.
	/// </summary>
	private Plano Escolher(in Percepcao p, Random rng)
	{
		// SEM ALVO, ANDA -- e a fera vagando. O ponto pra onde ela vai chega em `DoAlvo` mesmo com
		// `TemAlvo` falso (ver `LerPercepcao`), e ele nunca e alcancado, porque e recalculado a
		// partir da posicao ATUAL: e o que a faz ANDAR na direcao em vez de parar num destino.
		if (!p.TemAlvo) { _finalizando = false; Porque = "vagar"; return Plano.Vagar; }

		// ---- O ALVO CAIU: a fala da vitoria, e ela sai UMA vez ----
		// `if(target && (target.KO || target.HP <= 0) && IsInFight)` no fim do `attackState`
		// (`NPCAI.dm:547-549`). O `IsInFight` de la e o `Atual` de briga daqui: sem essa condicao, um
		// corpo que passa perto de alguem ja caido comentaria a derrota de que nao participou.
		//
		// E ela sai uma vez so por consequencia, e nao por um bit de "ja falei": na decisao seguinte
		// o plano ja e `Nada`, e `Nada` nao esta nesta lista.
		if (p.AlvoCaido)
		{
			if (Atual is Plano.Pressionar or Plano.Alcancar or Plano.Atirar or Plano.Circular)
				Dizer(Momento.Vencer, rng);
			_finalizando = false;
			Porque = "alvo no chao";
			return Plano.Nada;
		}

		// ============================ O MODO FINALIZADOR -- e ele e o CONSUMIDOR DA VIDA DO ALVO ============================
		// `if(target.HP <= NPC_AI_FINISH_HP && !target.KO && prob(ai_aggression))` (`NPCAI.dm:388`),
		// a PRIMEIRA pergunta do seletor de acao do DM: com o outro quase caido, o bicho para de
		// medir a luta e vai fechar -- barragem maior, pausa curta entre as rajadas.
		//
		// `Percepcao.VidaDoAlvo` era o setimo caso deste port de DADO EXTRAIDO SEM CONSUMIDOR: o
		// servidor escrevia o campo todo tique (`GameServer.Ia.cs:398`) e o repo inteiro nao o lia
		// em lugar nenhum. Este e o consumidor, e ele e o do original.
		//
		// ELE E DECIDIDO AQUI, na cadencia da decisao, e nao no tique: `prob(ai_aggression)` no DM
		// roda uma vez por ACAO, e sorteado a 30 Hz viraria um liga-desliga de 30 vezes por segundo
		// -- o corpo tremeria entre duas cadencias de soco em vez de escolher uma.
		//
		// A MISERICORDIA (a outra leitura da vida do alvo, `:513`) **nao foi portada, e a falta e
		// deliberada**: ela pede `e_behavior_vals[3] >= 75`, e nao ha no DM inteiro um molde com
		// bondade acima de 25 (o Citizen tem 5, os chefes tem 0, o padrao e 50) -- nem existe algo
		// que a AUMENTE, so o `behavior_check` que a diminui. Porta-la seria escrever uma regra que
		// nao dispara e um campo de dado que ninguem preenche, que e o mesmo defeito que este bloco
		// esta consertando, so que de cabeca pra baixo.
		// ==============================================================================================================
		_finalizando = p.VidaDoAlvo <= VidaDeFinalizar && rng.NextDouble() < Agressividade;

		// --- FUGIR (o `runawayState`, NPCAI.dm:510) -----------------------
		// A ORDEM E A DO `attackState`: fugir vem ANTES de escalar, de recarregar e de tudo o mais.
		// Quem esta quase morto e nao tem coragem nao "considera as opcoes" -- ele sai correndo.
		if (p.VidaFrac <= VidaQueFazFugir && CautelaExpressa > CautelaQueFazFugir)
		{
			if (Explicando) Porque = $"fugir: vida {p.VidaFrac:0.00}, cautela {CautelaExpressa:0.00}";
			return Plano.Fugir;
		}

		// --- ESCALAR ------------------------------------------------------
		// `if(HP <= 45 || target.expressedBP >= expressedBP * 1.5) npc_power_up()` (NPCAI.dm:517),
		// que chama o `npc_try_transform` (:254). A carencia e o `ai_powerup_cd` (:240).
		bool valeEscalar = p.VidaFrac <= VidaQuePedeForma || p.RazaoDePoder >= PoderQuePedeForma;
		if (valeEscalar && _carenciaDeForma <= 0)
		{
			if (Poderes.PodeSubirForma && p.KiFrac >= KiParaTransformar)
			{
				if (Explicando) Porque = $"escalar: vida {p.VidaFrac:0.00}, razao de poder {p.RazaoDePoder:0.0}";
				return Plano.Escalar;
			}

			// A PRE-CONDICAO REPARAVEL. Falta folego pra a forma -- e folego se resolve carregando.
			// E a unica cadeia de duas etapas desta camada, e ela vem do proprio gate do DM
			// (`Ki < MaxKi * 0.25`): la o NPC simplesmente desiste; aqui ele vai buscar o Ki.
			if ((Poderes.FormaSoFaltaKi || (Poderes.PodeSubirForma && p.KiFrac < KiParaTransformar))
				&& Poderes.SabeReunirKi && Inteligencia >= 0.35)
			{
				// NAO HA UM `bool _voltarPraEscalar` GUARDADO, e a falta e de proposito: quando o
				// tanque chegar em `CarregarAte` (0,75) a interrupcao nomeada solta a recarga, esta
				// mesma funcao roda de novo e `p.KiFrac >= KiParaTransformar` (0,25) ja e verdade --
				// ela cai no ramo de cima sozinha. Um campo de intencao seria uma segunda verdade
				// sobre o que ele quer, e a que fica velha e sempre a segunda.
				Porque = "carregar PRA escalar (falta folego pra a forma)";
				return Plano.Recuperar;
			}
		}

		// --- RECUPERAR ----------------------------------------------------
		// `if(!ai_recharging && kr <= KI_CRIT && sr <= STAM_CRIT && prob(ai_intelligence))`
		// (NPCAI.dm:522). O gate probabilistico E a inteligencia: bicho burro morre batendo.
		if (Poderes.SabeReunirKi && p.KiFrac <= KiCritico && p.FolegoFrac <= FolegoCritico
			&& rng.NextDouble() < Inteligencia)
		{
			if (Explicando) Porque = $"recuperar: ki {p.KiFrac:0.00}, folego {p.FolegoFrac:0.00}";
			return Plano.Recuperar;
		}

		// --- ATIRAR: quem nao tem arsenal sai na primeira linha do `EscolherTiro` ------
		// ============================ POR QUE AQUI, E NAO DEPOIS DO VOO ============================
		// A ordem das perguntas E a maior parte do comportamento, e esta posicao diz duas coisas:
		//
		//   * DEPOIS de escalar e de recarregar, porque as duas sao decisoes sobre a luta INTEIRA e
		//     um golpe de longe nao resolve estar fraco nem estar sem folego;
		//   * ANTES de alcancar, e este e o ponto interessante: quando o alvo esta num andar que ele
		//     nao encosta, atirar RESOLVE o mesmo problema que decolar -- e sem pagar o dreno de voo,
		//     que e o mais caro do jogo. Um lutador com raio nao sobe atras de quem esta voando; ele
		//     mira. Deixar o voo primeiro produziria o NPC que gasta o tanque subindo pra socar quem
		//     ele podia ter acertado do chao, e ninguem entenderia por que.
		// ======================================================================================
		if (EscolherTiro(p, rng, out int qual))
		{
			_tiroEscolhido = qual;
			if (Explicando)
			{
				Tiro t = Poderes.DeLonge[qual];
				Porque = $"atirar {t.Id}: {p.Distancia:0} px na janela [{t.AlcanceMin:0}, {t.AlcanceMax:0}], "
					   + $"risco {Arsenal.RiscoDeErrar(t, p.Distancia, p.AlvoSeMovendo):0.00}";
			}
			return Plano.Atirar;
		}

		// --- CIRCULAR (o `strafeState`, NPCAI.dm:551) ---------------------
		// ============================ ELE VEM DEPOIS DO TIRO, E ISSO E O DESENHO ============================
		// No DM o `strafeState` E o estado que atira -- ele orbita e larga o `blast()` quando a recarga
		// deixa (`:573-575`). Aqui as duas coisas ja estao separadas: `Atirar` e um plano com fases
		// visiveis (abrir distancia, plantar o pe, conjurar, soltar) e ele e perguntado ACIMA. O que
		// sobra pro circulo e exatamente o resto -- o tempo entre um tiro e o proximo --, que era
		// justamente quando o corpo voltava a andar pra frente e pra tras na linha do inimigo.
		//
		// O GATE E O `isBlaster` DESTE PORT: ter arsenal. Quem nao tem raio nao orbita, aqui como la --
		// e isso e o que mantem a fera e a furia lendaria intocadas, sem uma linha de excecao escrita.
		// ==============================================================================================
		if (Poderes.DeLonge.TemAlguma && p.Distancia <= DistanciaDeCircular * 2f)
		{
			// SAIR TAMBEM E SORTEIO (`prob(10)`, `:578`) -- e por isso o "continua" e perguntado
			// separado do "comeca". Sem ele, o circulo duraria exatamente o compromisso e todo NPC
			// blaster do mundo orbitaria pelo mesmo tempo.
			bool continua = Atual == Plano.Circular && rng.NextDouble() >= ChanceDeSairDoCirculo;
			bool comeca = Atual != Plano.Circular && rng.NextDouble() < ChanceDeCircular;
			if (continua || comeca)
			{
				if (Explicando) Porque = $"circular: {p.Distancia:0} px, mao {_maoDoCirculo:+0;-0}";
				else Porque = "circular";
				return Plano.Circular;
			}
		}

		// --- ALCANCAR (DESENHO NOVO: o DM nao voa) -------------------------
		// Duas razoes pra subir, e as duas sao sobre ALCANCE e nao sobre gosto:
		//   * ele esta num andar de onde eu nao encosto -- a briga simplesmente nao acontece;
		//   * ele esta no chao e eu sou esperto o bastante pra explorar a assimetria do andar 1
		//     (`Voo.PodeAcertar`): eu bato nele, ele nao bate em mim.
		// ============================ E O TANQUE TEM QUE PAGAR A SUBIDA, EM KI DE VERDADE ============================
		// A fracao (`KiParaDecolar`) responde *"estou confortavel pra bancar o gesto?"*; ela nao
		// responde *"da pra pagar?"*, porque o preco do voo e ABSOLUTO e sai da proficiencia deste
		// corpo -- 25% de um tanque pequeno pode nao cobrir nem a decolagem.
		//
		// Os dois numeros ja estavam nas `Capacidades`, perguntados ao jogo a 1 Hz pelas MESMAS
		// funcoes que cobram do jogador (`Voo.CustoParaLigar` e `Voo.CustoPorSegundo`), e eram o
		// quarto e o quinto campo extraido sem consumidor. O prazo de tres segundos e a folga
		// minima pra a subida valer: decolar pra cair no tique seguinte e pior do que nao ter subido
		// -- e a mesma frase que ja justifica o `DeveDescerDoCeu`.
		// =========================================================================================================
		double precoDaSubida = Poderes.CustoDeDecolar + Poderes.CustoDoVooPorSegundo * SegundosDeVooQueValemADecolagem;
		if (Poderes.PodeVoar && p.KiFrac >= KiParaDecolar && (p.EstouVoando || p.Ki >= precoDaSubida))
		{
			bool naoAlcanco = !p.AlcancoPelaAltura;
			bool rasanteVale = Inteligencia >= InteligenciaQuePairaRasante
							   && p.AndarDoAlvo == 0 && !p.AlvoVoando && p.MeuAndar != 1;
			if (naoAlcanco || rasanteVale)
			{
				Porque = naoAlcanco ? "alcancar: ele esta noutro andar" : "pairar rasante: bato sem levar";
				return Plano.Alcancar;
			}
		}

		// --- RECUAR -------------------------------------------------------
		// O "respirar" que este cerebro sempre teve. `VidaCautelosa = 0` (a fera) o desliga -- e
		// continua desligando, porque a `CautelaExpressa` MULTIPLICA a base, e zero vezes qualquer
		// emocao continua zero.
		//
		// O LIMIAR AGORA E O DE AGORA, e nao o do molde: quem esta GANHANDO fica afoito e recua mais
		// tarde; quem esta apanhando de alguem mais forte fica com medo e recua mais cedo. E o mesmo
		// corpo se comportando diferente conforme a luta anda -- e e o `behavior_check` do DM, com a
		// metade do medo que o clamp de la apaga.
		double cautela = CautelaExpressa;
		if (cautela > 0 && p.VidaFrac < cautela && rng.NextDouble() < 0.35)
		{
			if (Explicando) Porque = $"recuar: vida {p.VidaFrac:0.00}, cautela {cautela:0.00}";
			return Plano.Recuar;
		}

		Porque = "pressionar";
		return Plano.Pressionar;
	}

	/// <summary>
	/// HA UM GOLPE DE LONGE QUE VALE A PENA AGORA? **Hoje devolve falso na primeira linha, sempre.**
	///
	/// ============================ ESTA FUNCAO E O GANCHO INTEIRO ============================
	/// E aqui que a decisao de "atacar de longe" nasce -- e ela faz, nesta ordem, as cinco perguntas
	/// que o dono nomeou, cada uma respondida por um dado que ja existe:
	///
	///   1. ALCANCE       -- `p.Distancia <= t.AlcanceMax`. Perto demais nao reprova aqui: a receita
	///                       sabe abrir distancia, e e dai que sai o "kitar" sem codigo de kite.
	///   2. LINHA DE VISAO-- `p.LinhaLivre`, tracada pelo servidor SO quando ha arsenal (e por isso
	///                       custa zero pra quem nao tem arsenal). Falso = nao sei = nao atira.
	///   3. CUSTO DE KI   -- o Ki tem que sobrar DEPOIS do golpe, e sobrar o bastante pra nao cair do
	///                       ceu: a mesma reserva do `DeveDescerDoCeu`, porque largar um raio e
	///                       despencar em seguida e pior do que nao ter atirado.
	///   4. RISCO DE ERRAR-- `Arsenal.RiscoDeErrar` contra a <see cref="ToleranciaDeErro"/>, que sai
	///                       da inteligencia. E medido na distancia em que ele VAI atirar, e nao na
	///                       de agora -- senao um corpo colado recusaria o golpe que ele so daria
	///                       depois de recuar.
	///   5. TEMPO DE CONJURACAO -- nao entra na escolha, entra na RECEITA: e o tempo de pe parado, e
	///                       e o que da ao jogador a janela pra interromper. Ver <see cref="Disparo"/>.
	///
	/// A ESCOLHA entre dois golgos viaveis e `(1 - risco) * CustoDeKi`. O custo entra como PROXY de
	/// tamanho: neste jogo o dreno E o tamanho da tecnica (o DM cobra `100*BaseDrain`), entao o mais
	/// caro e o mais forte. Se um dia isso deixar de valer, o conserto e um campo `Poder` na linha da
	/// tabela -- dado --, e nao uma regra nova aqui.
	/// ====================================================================================
	/// </summary>
	private bool EscolherTiro(in Percepcao p, Random rng, out int qual)
	{
		qual = -1;

		// A PODA, NA PRIMEIRA LINHA. Um `int` contra zero, 4 vezes por segundo por corpo: e este o
		// custo total do gancho enquanto nao houver ataque de longe no jogo.
		if (!Poderes.DeLonge.TemAlguma) return false;
		if (!p.TemAlvo || p.AlvoCaido) return false;
		if (_pausaDoTiro > 0) return false;

		// ============================ ECONOMIA DE KI: COM O TANQUE BAIXO, ELE SOCA ============================
		// **LITERAL**: `if(ki_ratio <= NPC_AI_KI_LOW && prob(ai_intelligence)) attack(); return`
		// (`NPCAI.dm:404`) -- e o comentario do autor e a regra inteira: *"com ki baixo o NPC esperto
		// troca pro corpo-a-corpo em vez de queimar ki"*.
		//
		// E ela NAO e redundante com o custo do golpe, logo abaixo: aquele pergunta *"da pra pagar
		// ESTE tiro?"* e este pergunta *"vale a pena gastar o que sobrou?"*. Sem esta linha, um NPC
		// com 12% de tanque larga o ultimo raio, fica sem Ki pra guarda, pra corrida e pra forma, e
		// perde a luta tendo feito a jogada "certa" -- que e exatamente o erro que o DM evita aqui.
		//
		// E o gate e a INTELIGENCIA, como todo o resto: o burro queima o tanque no primeiro tiro que
		// couber. Erro CARACTERISTICO.
		// =================================================================================================
		if (p.KiFrac <= KiBaixo && rng.NextDouble() < Inteligencia) return false;

		// SO QUEM E ESPERTO ABRE DISTANCIA PRA ATIRAR. O burro usa o golpe se ele ja estiver na
		// janela, e senao continua avancando pra socar -- o mesmo desenho do `prob(ai_intelligence)`
		// que decide quem recarrega (`NPCAI.dm:522`). Erro CARACTERISTICO: "aquele bicho atira de
		// longe se voce der espaco, mas se voce colar nele ele esquece que tem raio".
		bool sabeRecuarPraAtirar = Inteligencia >= 0.35;

		double melhorNota = 0;
		for (int i = 0; i < Poderes.DeLonge.Quantas; i++)
		{
			Tiro t = Poderes.DeLonge[i];

			if (p.Distancia > t.AlcanceMax) continue;
			if (p.Distancia < t.AlcanceMin && !sabeRecuarPraAtirar) continue;
			if (t.PrecisaDeLinhaLivre && !p.LinhaLivre) continue;
			if (p.Ki - t.CustoDeKi < ReservaDeKi(p)) continue;

			// o risco medido ONDE ele vai atirar: dentro da janela, e nao onde ele esta agora
			float daJanela = Math.Clamp(p.Distancia, t.AlcanceMin, t.AlcanceMax);
			double risco = Arsenal.RiscoDeErrar(t, daJanela, p.AlvoSeMovendo);
			if (risco > ToleranciaDeErro) continue;

			double nota = (1 - risco) * t.CustoDeKi;
			if (nota <= melhorNota) continue;
			melhorNota = nota;
			qual = i;
		}
		return qual >= 0;
	}

	/// <summary>
	/// O KI QUE ELE NAO GASTA. No chao, nenhum -- o tanque vazio so custa velocidade. No ar, a
	/// MESMA folga do <see cref="DeveDescerDoCeu"/>: um golpe que o derruba do ceu nao valeu a pena,
	/// porque quem cai chega no chao sem guarda e no meio do inimigo.
	/// </summary>
	private static double ReservaDeKi(in Percepcao p) => p.EstouVoando ? Voo.KiQueDerruba * 4 : 0;

	/// <summary>
	/// O TIRO EM CURSO MORREU? As interrupcoes NOMEADAS do plano de atirar -- cada uma e uma frase
	/// que da pra ler em jogo, e nao uma comparacao de pontos.
	/// </summary>
	private bool TiroMorreu(in Percepcao p)
	{
		// O ARSENAL ENCOLHEU DEBAIXO DELE. As capacidades sao relidas a 1 Hz: uma skill esquecida,
		// um debuff, uma forma que fechou a linha -- e o indice guardado passa a apontar pra fora.
		// Sem esta linha seria um `IndexOutOfRange` dentro do `try` por corpo, ou seja um NPC virando
		// estatua sem ninguem saber por que.
		if (_tiroEscolhido < 0 || _tiroEscolhido >= Poderes.DeLonge.Quantas) return true;

		Tiro t = Poderes.DeLonge[_tiroEscolhido];
		return p.Distancia > t.AlcanceMax                              // ele saiu do alcance
			|| (t.PrecisaDeLinhaLivre && !p.LinhaLivre)                // entrou parede no meio
			|| p.Ki - t.CustoDeKi < ReservaDeKi(p)                     // o folego acabou
			|| p.VidaFrac < _vidaAoConjurar - DanoQueAbortaACarga      // apanhou demais conjurando
			|| _conjurandoHa > PrazoDaConjuracao;                      // nunca completa: desiste
	}

	/// <summary>
	/// LARGA O GESTO. Zera a conjuracao, esquece a opcao escolhida, paga a pausa e devolve o corpo
	/// pra decisao livre -- o <see cref="_noPlano"/> e liberado porque um plano que acabou de morrer
	/// nao pode segurar o compromisso contra o proximo.
	/// </summary>
	private void AbortarTiro()
	{
		_pausaDoTiro = Math.Max(_pausaDoTiro, PausaDepoisDoTiro);
		_conjurandoHa = 0;
		_tiroEscolhido = -1;
		Atual = Plano.Nada;
		_noPlano = TempoMinimoNoPlano;
		Porque = "o tiro morreu no meio";
	}

	/// <summary>
	/// ABRIR DISTANCIA, PLANTAR O PE, CONJURAR, SOLTAR. Ver <see cref="Plano.Atirar"/>.
	///
	/// As tres fases sao visiveis de fora, e isso e o ponto: o jogador ve o NPC recuar, ve ele parar
	/// e ve o golpe sair. E na parada que mora a janela pra interromper -- um NPC que atira no mesmo
	/// tique em que decide nao da nada pro jogador fazer.
	/// </summary>
	private Comando Disparo(in Percepcao p, bool guarda, Random rng)
	{
		if (_tiroEscolhido < 0 || _tiroEscolhido >= Poderes.DeLonge.Quantas)
			return new Comando { Guardar = guarda };

		Tiro t = Poderes.DeLonge[_tiroEscolhido];

		// A MIRA SAIU DAQUI. Estas quatro linhas escreviam `Marcar = p.IdDoAlvo` uma a uma, e era a
		// forma exata do defeito que o `Montar` acabou de fechar: **so a receita de atirar marcava**,
		// entao as duas receitas de corpo-a-corpo socavam sem dizer em quem, e o `Atacar` -- que vira
		// o corpo pro marcado -- nao tinha pra quem virar. Agora a marca e escrita UMA vez, no
		// `Montar`, pra todo plano com alvo vivo; repetir aqui seria a mesma armadilha em quatro
		// copias esperando a quinta receita nascer em branco.
		//
		// acabou de atirar: espera a pausa de pe, mirado, sem recomecar a conjuracao
		if (_pausaDoTiro > 0)
			return new Comando { Guardar = guarda };

		// COLADO DEMAIS: recua, e a conjuracao VOLTA A ZERO. Nao e punicao -- e a mesma regra do
		// `rechargeState`: gesto que pede o pe plantado nao sobrevive a um passo.
		if (p.Distancia < t.AlcanceMin)
		{
			_conjurandoHa = 0;
			return new Comando { Rumo = p.Direcao * -1f, Guardar = guarda };
		}

		// NA JANELA: para e conjura. `Rumo` zero de proposito.
		if (_conjurandoHa < t.TempoDeConjuracao)
			return new Comando { Guardar = guarda };

		// SOLTA -- um PULSO so, pelo mesmo canal do jogador. Quem cobra recarga, Ki e "voce nao sabe
		// isso" e a tecnica, la no `UsarHabilidade`; aqui nao se confere nada.
		//
		// E ELE GRITA SOLTANDO, com as frases que o proprio DM usa pra isso (`NPCAI.dm:378`). O ponto
		// de fala mora aqui e nao no `Montar` pelo mesmo motivo do soco: **este e o unico lugar do
		// cerebro que solta um tiro**. Ver a justificativa do numero no `Falatorio.ChanceDe` -- ela
		// existe porque o compromisso deste port deixa um blaster preso no plano de atirar, e sem esta
		// linha um chefe cujo kit inteiro e raio atravessaria a luta mudo (medido: 20 s, zero frases).
		_pausaDoTiro = PausaDepoisDoTiro;
		_conjurandoHa = 0;
		Dizer(Momento.Disparar, rng);
		return new Comando { Habilidade = t.Id, Guardar = guarda };
	}

	/// <summary>
	/// DO PLANO PRA AS TECLAS. Todo tique -- as teclas de ESTADO precisam ser reafirmadas, e as de
	/// PULSO so saem no tique em que valem.
	/// </summary>
	private Comando Montar(in Percepcao p, Random rng)
	{
		bool guarda = _guardaAte > 0 && _reacaoDaGuarda <= 0;

		Comando gesto = _hesitaAte > 0
			// HESITANDO: recua guardando e nao ataca. Curto -- ver `Sustos`.
			? new Comando { Rumo = p.Direcao * -1f, Guardar = guarda, QuerDescer = false }
			: Atual switch
			{
				Plano.Escalar => Escalada(guarda),
				Plano.Atirar => Disparo(p, guarda, rng),
				Plano.Recuperar => Recuperacao(p, guarda, rng),
				Plano.Alcancar => Alcance(p, guarda, rng),
				Plano.Recuar => new Comando { Rumo = p.Direcao * -1f, Guardar = guarda, QuerDescer = QuerDescerPara(p), QuerSubir = QuerSubirPara(p) },
				Plano.Fugir => Fuga(p),
				Plano.Pressionar => Pressao(p, guarda, rng),
				Plano.Circular => Circulo(p, guarda),

				// VAGAR: so o rumo. Sem soco (o ponto nao e ninguem) e sem guarda (nao ha de quem se
				// defender) -- e exatamente o que o cerebro antigo fazia quando `posAlvo` era um ponto.
				Plano.Vagar => new Comando { Rumo = p.Direcao },

				_ => new Comando { Guardar = guarda },
			};

		// ============================ O OLHAR, E ELE E DECIDIDO **UMA VEZ SO** ============================
		// **Havendo com quem lutar, o corpo encara.** Uma linha, fora do `switch`, e de proposito: a
		// alternativa obvia -- cada receita escrever o proprio `Olhar` -- e a forma exata do defeito
		// que esta linha conserta. Bastaria uma receita esquecer (e as tres que dao um passo pra tras
		// esqueceriam, porque nelas o "pra onde eu ando" ja parece a resposta) pra o corpo voltar a
		// socar de costas, e o proximo plano nasceria com a mesma armadilha em branco.
		//
		// E o `dir = get_dir(src,target)` do `npc_combat_action` (`NPCAI.dm:386`): no DM ele tambem
		// e UM lugar so, a primeira linha do seletor de acao, ANTES de escolher entre kiai, agarrao,
		// raio, barragem ou soco -- e nao uma linha dentro de cada um dos cinco.
		//
		// ZERO QUANDO NAO HA COM QUEM LUTAR, e ai o servidor cai no rumo do passo:
		//   * `Vagar` (`!TemAlvo`) -- quem passeia olha pra onde vai, e nao pra um ponto imaginario;
		//   * alvo no chao (`AlvoCaido`) -- a briga acabou; o corpo nao fica encarando um caido;
		//   * `Fugir` -- **a terceira excecao, e ela chegou**. Quem esta correndo pra salvar a pele
		//     olha pra onde corre; encarar quem o persegue e o gesto de quem ainda esta lutando, e
		//     `Fugir` e justamente o plano que declara que ele nao esta mais. A linha nao precisou de
		//     um `if` no servidor: com `Olhar` zero, o atuador cai no rumo do passo sozinho -- que
		//     era exatamente a saida prevista quando o campo nasceu.
		//
		// `Recuar` e `Recuperar` NAO sao fuga -- sao passo atras com a guarda erguida, e no DM eles
		// tambem mantem o `dir` no alvo (`step_away` e logo em seguida `npc_combat_action`). Encarar
		// quem bate tambem e o que faz o `AnguloDeChegada` do resolvedor tratar o golpe como frontal,
		// e nao como pelas costas.
		// =============================================================================================
		//
		// ============================ E A MARCA, QUE E O QUE FECHA O SOCO DE COSTAS ============================
		// O `Olhar` acima conserta o corpo que ANDA. Ele nao conserta o corpo que **nao pode andar**:
		// o atuador aplica o olhar dentro do `PassoDaIa`, atras do `PodeMexerOCorpo`, e ha estados em
		// que o jogo recusa o passo mas NAO recusa o golpe -- paralisado, enraizado por Ki (o
		// `canmove = 0` dos raios), prensado pela gravidade. Nesses, `PodeAtacar()` continua
		// verdadeiro (`CombatState.cs`), o cerebro continua mandando `Leve`/`Pesado`, e o corpo
		// socava com a direcao do ultimo passo -- que, nas tres receitas que recuam, e a direcao
		// OPOSTA ao inimigo. Era o resto do soco de costas.
		//
		// A CORRECAO NAO PODE MORAR NO CAMINHO DO PASSO, e e por isso que ela mora aqui. No DM, a
		// unica virada que roda de verdade dentro do combate esta DENTRO do ataque
		// (`commonAttackProcs`, `attack cmn.dm:158`: `dir = get_dir(loc,M.loc)`) -- o caminho do
		// golpe, que nao tem recusa nenhuma. O port ja tem esse mesmo lugar: o `Atacar`
		// (`GameServer.Combat.cs`) vira o corpo pro **marcado** antes de arrancar. O que faltava era
		// a IA dizer em quem esta batendo: `Comando.Marcar` so era preenchido na receita de atirar
		// (`Disparo`), e nenhuma das duas receitas de corpo-a-corpo marcava ninguem.
		//
		// Com esta linha o `Atacar` passa a ser o UNICO lugar que decide a direcao do golpe -- o
		// mesmo pro jogador e pro NPC --, e ele roda depois do passo (ver a ordem do
		// `AplicarComando`), entao no instante do soco ele e quem manda. "Ninguem soca de costas"
		// deixa de depender de o corpo ter conseguido andar.
		//
		// E o `target` do DM, e nao um campo novo: la o NPC tem UMA variavel de alvo, usada pelo
		// seletor de acao e pelo ataque. Por isso a condicao aqui **nao** exclui `Fugir` como a de
		// cima: marcar e "quem e meu oponente", nao "pra onde estou voltado", e o `runawayState`
		// (`NPCAI.dm:646`) foge DO `target` -- ele nao o larga. Quem foge nao bate (o `Fuga` nao
		// manda `Leve` nem `Pesado`), entao a marca nao vira ninguem.
		//
		// ZERO E "NAO MEXE" (ver o campo no `Comando`), entao sem alvo a mira antiga fica -- e ela
		// se limpa sozinha quando o marcado morre, muda de zona ou fica intocavel (`Marcado`). Nao
		// ha soco com marca velha: as duas unicas receitas que socam (`Pressao` e `Alcance`) so sao
		// alcancadas com `TemAlvo && !AlvoCaido`, ou seja, no mesmo tique em que esta linha escreve.
		// =================================================================================================
		// ============================ E A FALA, QUE SEGUE A MESMA REGRA DAS DUAS DE CIMA ============================
		// Uma linha, fora do `switch`, pelo mesmo motivo: os pontos de fala espalhados pelo cerebro
		// dizem a OCASIAO e escrevem em `_fala` com `??=`; **este e o unico lugar que a poe no
		// comando**. Uma receita que montasse o proprio `Falar` teria o direito de esquecer o relogio,
		// e ai o corpo falaria a 30 Hz -- que e o defeito mais ruidoso que esta camada podia ter.
		//
		// Note que ela e escrita mesmo com o corpo hesitando ou fugindo, e isso e deliberado: no DM
		// as falas de apanhar (`:797`) e os avisos de recurso (`:322`) saem do `behavior_check` e do
		// `checkState`, que rodam EM PARALELO ao estado -- nao dentro dele. Quem esta correndo pra
		// salvar a pele ainda diz que esta sem folego.
		// ========================================================================================================
		return gesto with
		{
			Olhar = p.TemAlvo && !p.AlvoCaido && Atual != Plano.Fugir ? p.Direcao : Vec2.Zero,
			Marcar = p.TemAlvo && !p.AlvoCaido ? p.IdDoAlvo : 0,
			Falar = _fala,
		};
	}

	/// <summary>
	/// O SOPRO QUE ABRE ESPACO -- o `npc_kiai()` (`NPCAI.dm:295`), acionado pelo `npc_combat_action`
	/// (`:399`) e pelo `rechargeState` (`:678`).
	///
	/// ============================ ELE E UMA SAIDA, E NAO UM ATAQUE ============================
	/// A situacao que ele resolve e exata e o original a escreve numa linha: **colado, sem Ki e
	/// atordoado**. Nesse estado tudo o mais que o cerebro sabe fazer falha -- recuar nao adianta
	/// (quem persegue anda igual), aparar drena o Ki que ja acabou, e trocar soco atordoado e perder.
	/// O sopro nao machuca quase nada: ele ARREMESSA, e o que se ganha e o tempo.
	///
	/// **O CANAL E O DO JOGADOR**, como o do tiro: `Comando.Habilidade = "Kiai"`, e quem cobra os
	/// 50 de dreno, a recarga (`_soproPronto`) e o "voce nao sabe isso" e o proprio verb. Nao ha aqui
	/// nenhuma conferencia dessas tres -- so a PODA, que e o que impede o cerebro de marretar uma
	/// porta fechada trinta vezes por segundo.
	/// ====================================================================================
	/// </summary>
	/// <param name="colado">
	/// A distancia que conta como "em cima de mim". `DistanciaDoSopro` na briga (o `d &lt;= 1` do
	/// `:399`); na recarga o original usa o MESMO `d <= 1` (`:678`) -- e o mesmo numero nos dois.
	/// </param>
	private bool SoproAbreEspaco(in Percepcao p, float colado)
	{
		if (!Poderes.SabeSopro || _pausaDoSopro > 0) return false;
		if (!p.TemAlvo || p.AlvoCaido) return false;
		if (p.Distancia > colado) return false;

		// O PRECO SAI DA TECNICA, e nao de um numero escrito aqui: o `CustoDoSopro` das
		// `Capacidades` e a MESMA expressao que o verb cobra (`50 * BaseDrain`), perguntada a 1 Hz.
		// Uma tabela de precos paralela envelheceria no dia em que o dono mexesse no verb.
		return p.Ki >= Poderes.CustoDoSopro;
	}

	/// <summary>
	/// ...e o gesto. Um PULSO so, e a pausa cobrada na hora -- ver <see cref="PausaDepoisDoSopro"/>.
	/// </summary>
	private Comando Soprar(bool guarda, Random rng)
	{
		_pausaDoSopro = PausaDepoisDoSopro;
		Dizer(Momento.AbrirEspaco, rng);
		return new Comando { Habilidade = "Kiai", Guardar = guarda };
	}

	/// <summary>
	/// ORBITAR -- o `strafeState` (`NPCAI.dm:551-585`). Ver <see cref="Plano.Circular"/>.
	///
	/// ============================ A COMPONENTE LATERAL E A RECEITA INTEIRA ============================
	/// O radial e literalmente o de la: perto demais, `step_away` (`:569`); longe demais, volta pro
	/// `chaseState` (`:560`) -- que aqui e simplesmente andar na direcao do alvo, porque o plano ja
	/// vai ser largado na proxima decisao. O que o DM ganha DE GRACA e este port nao ganhava e o
	/// resto: no BYOND o `step_away` de um corpo que ja esta na distancia certa sai numa das oito
	/// direcoes e o movimento fica torto sozinho. Aqui a posicao e livre e o rumo e um vetor exato --
	/// entao "manter distancia" produzia um corpo PARADO, e parado a tres tiles e o retrato do boneco.
	///
	/// O vetor perpendicular (`-y, x`) resolve isso sem nenhuma maquina de estado: ele e sempre
	/// unitario, sempre tangente, e somado ao radial da uma espiral que fecha na distancia desejada.
	/// ============================================================================================
	/// </summary>
	private Comando Circulo(in Percepcao p, bool guarda)
	{
		Vec2 paraEle = p.Direcao;

		// ALVO EM CIMA DE MIM: sem direcao nao ha tangente, e normalizar zero e ruido. Cai no radial
		// puro do tique seguinte -- e "em cima de mim" ja e caso do sopro, nao do circulo.
		if (paraEle.LengthSquared <= 1e-6f) return new Comando { Guardar = guarda };

		float radial = p.Distancia < DistanciaDeCircular ? -1f
					 : p.Distancia > DistanciaDeCircular * 1.5f ? 1f
					 : 0f;

		var tangente = new Vec2(-paraEle.Y, paraEle.X) * _maoDoCirculo;
		Vec2 rumo = paraEle * radial + tangente;

		return new Comando
		{
			Rumo = rumo.Normalized(),
			Guardar = guarda,
			// A ALTURA CONTINUA ACOMPANHANDO. Orbitar no chao enquanto o alvo sobe seria o mesmo
			// buraco que o `Pressao` ja fecha: um plano que ignora o andar produz um corpo que se
			// mexe muito e nunca alcanca.
			QuerSubir = QuerSubirPara(p),
			QuerDescer = QuerDescerPara(p),
		};
	}

	/// <summary>
	/// SUBIR UM DEGRAU. Um PULSO so, e depois a carencia -- sem ela o corpo apertaria C 4 vezes por
	/// segundo contra uma porta fechada, que e o `if(!hasssj) return` do DM visto de fora
	/// (`NPCAI.dm:361`: *"never forced onto a random monster"*).
	///
	/// E ele PARA pra transformar. No anime ninguem vira Super Saiyajin correndo, e mecanicamente e
	/// o certo tambem: e o instante em que ele esta vulneravel, e o jogador tem que poder ver.
	/// </summary>
	private Comando Escalada(bool guarda)
	{
		if (_carenciaDeForma > 0) return new Comando { Guardar = guarda };
		_carenciaDeForma = CarenciaDeAscensao;

		// E ELE FICA UM INSTANTE SEM DIZER NADA. `ai_next_chat = world.time + 20` na ULTIMA linha do
		// `npc_power_up` (`NPCAI.dm:255`): quem acabou de explodir de poder nao emenda uma piadinha
		// no tique seguinte. E a unica pausa de fala do original que nao vem de ter falado.
		//
		// Cobrada AQUI e nao no atuador porque e uma decisao do cerebro; se o `Transformar` recusar
		// (o jogo tem a palavra final), o pior que acontece e um corpo dois segundos mais quieto --
		// que e o mesmo lado seguro da `CarenciaDeAscensao` logo acima.
		Boca.AcabouDeAscender();

		return new Comando { SubirForma = true, Guardar = guarda };
	}

	/// <summary>
	/// RECUAR ATE A DISTANCIA SEGURA, PARAR, E SO ENTAO CARREGAR -- o `rechargeState` inteiro
	/// (`NPCAI.dm:673-694`), inclusive a parte que se esquece facil: **enquanto recua, ele NAO
	/// carrega**. O comentario de la explica por que (*"is_drawing deixaria o passo mais lento"*), e
	/// aqui a razao e ainda mais dura: o servidor RECUSA andar carregando (ver o portao do `Input`),
	/// entao um comando com rumo E carga junto sairia como "parado, carregando" -- o NPC ficaria
	/// plantado a um passo do inimigo achando que estava recuando.
	/// </summary>
	private Comando Recuperacao(in Percepcao p, bool guarda, Random rng)
	{
		// ---- ELE ABRE ESPACO NA MARRA ANTES DE RECUAR (`NPCAI.dm:678-679`) ----
		// `if(d <= 1 && !kiaionCD && Ki >= 40*BaseDrain && prob(60)) npc_kiai()`, com o comentario do
		// autor: *"abre espaco na marra antes de recuar"*. Aqui ele importa MAIS do que la, e por uma
		// razao do port: carregar PRENDE o corpo (`PodeMexerOCorpo` recusa quem esta `Carregando`),
		// entao um NPC que recua com alguem colado nunca chega na distancia segura -- ele ficaria
		// recuando pra sempre, sem nunca carregar um ponto de Ki.
		//
		// Note que ele NAO cobra atordoamento nem Ki baixo, ao contrario do da briga: quem chegou no
		// `rechargeState` ja esta sem Ki por definicao (foi o gate que o trouxe), e o que falta aqui
		// e so espaco. E o sorteio de 60% que da a variedade -- as vezes ele grita, as vezes ele so
		// recua, e as duas coisas se veem.
		if (p.TemAlvo && p.Distancia < DistanciaSegura
			&& rng.NextDouble() < ChanceDoSoproNaRecarga
			&& SoproAbreEspaco(p, DistanciaDoSopro))
			return Soprar(guarda, rng);

		if (p.TemAlvo && p.Distancia < DistanciaSegura)
			return new Comando { Rumo = p.Direcao * -1f, Guardar = guarda };

		// longe o bastante: planta o pe e carrega. Rumo ZERO e obrigatorio -- andar SUSPENDE a carga
		// (`CargaDeKi.Passo` recebe o `Moving`), e um NPC que carrega andando nao carrega nada.
		return new Comando { Carregar = true, Guardar = guarda };
	}

	/// <summary>
	/// DECOLAR E SUBIR ATE O ANDAR DELE. **Desenho novo** -- ver o cabecalho.
	///
	/// O toggle do voo e um PULSO e a subida e um ESTADO, e sao as mesmas duas teclas do jogador:
	/// a de ligar o voo e a de segurar espaco.
	/// </summary>
	private Comando Alcance(in Percepcao p, bool guarda, Random rng)
	{
		if (!p.EstouVoando)
			return new Comando { AlternarVoo = true, Guardar = guarda };

		// ja no ar: sobe/desce ate o andar certo e continua indo pra cima dele
		return new Comando
		{
			Rumo = p.Distancia > DistanciaIdeal * 1.4f ? p.Direcao : Vec2.Zero,
			QuerSubir = QuerSubirPara(p),
			QuerDescer = QuerDescerPara(p),
			Guardar = guarda,
			Leve = p.AlcancoPelaAltura && p.Distancia <= DistanciaIdeal * 1.6f && EmRajada(p, rng),
		};
	}

	/// <summary>
	/// FUGIR DE VERDADE -- o `runawayState` (`NPCAI.dm:646-664`) inteiro, e ele cabe em uma linha
	/// porque tudo o que ele faz e o que ele NAO manda.
	///
	/// Sem `Guardar`, sem `Leve`, sem `Pesado`: o laco de la nao tem uma linha de ataque nem de
	/// guarda, so `step_away` ate a vida voltar. E COM `Correndo`, que e a unica coisa daqui que nao
	/// tem original -- no BYOND nao ha correr, e no port ha, cobrado pelo `PodeCorrer` (`MaxKi*0,02`
	/// por segundo) exatamente como o do jogador. Fugir andando seria a mesma velocidade de quem
	/// persegue, ou seja: nao seria fuga nenhuma.
	///
	/// A altura fica como esta de proposito -- nao ha `QuerSubir`/`QuerDescer`. Quem esta fugindo
	/// nao tem tempo de escolher andar, e um pedido de descida no meio da fuga poria o corpo no chao
	/// justamente na frente de quem vem atras.
	/// </summary>
	private static Comando Fuga(in Percepcao p) =>
		new() { Rumo = p.Direcao * -1f, Correndo = true };

	/// <summary>
	/// O ESTADO NORMAL: manter a distancia de soco e bater. Com a altitude acompanhando, porque o
	/// alvo pode subir no meio da troca.
	/// </summary>
	private Comando Pressao(in Percepcao p, bool guarda, Random rng)
	{
		// ---- ENCURRALADO: o sopro vem ANTES de tudo (`NPCAI.dm:399`) ----
		// A ORDEM E A DO ORIGINAL e ela nao e arbitraria: no `npc_combat_action` este `if` esta acima
		// da economia de Ki (`:404`) e acima do ramo de blaster (`:407`). Ou seja, quem esta colado,
		// atordoado e sem Ki NAO passa pela pergunta "vale a pena gastar o que sobrou?" -- ele empurra
		// e sai. E ele RETORNA sem socar, tambem como la.
		//
		// O gate e o triplo do DM: colado, Ki baixo e ATORDOADO. O atordoamento e o que impede isto de
		// virar rotina -- sem ele, todo corpo com pouco Ki soltaria um sopro a cada dois segundos.
		if (p.Atordoado && p.KiFrac <= KiQuePedeSopro && SoproAbreEspaco(p, DistanciaDoSopro))
			return Soprar(guarda, rng);

		Vec2 rumo = Vec2.Zero;
		if (p.Distancia > DistanciaIdeal * 1.4f) rumo = p.Direcao;
		else if (p.Distancia < DistanciaIdeal * 0.6f) rumo = p.Direcao * -1f;

		bool noAlcance = p.Distancia <= DistanciaIdeal * 1.6f && p.AlcancoPelaAltura;
		bool temFolego = p.KiFrac > 0.15;

		// ============================ A INVESTIDA: SOCAR DE LONGE **PRA FECHAR** ============================
		// O `dashBreak` do `chaseState` (`NPCAI.dm:449-450`) e o `else if(haszanzo) Attack()` do
		// `attack()` (`:195`), na unica forma que o port precisa: pedir o golpe estando fora do alcance.
		// Quem faz a investida acontecer e o `Aproximar`, dentro do `Atacar` -- o MESMO caminho do
		// jogador, com o mesmo custo de Ki (5% do maximo) e a mesma recarga de 500 ms.
		//
		// A FAIXA COMECA ONDE O SOCO PARA DE ALCANCAR (`noAlcance`) e vai ate a
		// <see cref="AlcanceDaInvestida"/>: exatamente o vao que o cerebro antes so sabia atravessar
		// andando. Nao ha piso escrito aqui porque `noAlcance` ja e o piso, e ele (54,4 px) esta acima
		// dos 48 px que o arranque exige de deslocamento -- os dois numeros nunca se cruzam.
		//
		// `temFolego` ENTRA porque a investida so vale PESADA (o arranque longo alcanca cinco tiles, o
		// curto dois e meio), e o pesado abaixo daqui exige folego. Sem esta condicao, um NPC sem Ki
		// pediria um golpe LEVE de 300 px de distancia -- que e um soco no ar com nome novo.
		// =====================================================================================================
		bool investida = !noAlcance && temFolego && p.AlcancoPelaAltura
					  && p.Distancia <= AlcanceDaInvestida
					  && rng.NextDouble() < ChanceDeInvestir;

		// `EmRajada` FICA POR ULTIMO -- ele tem efeito colateral (consome a rajada e arma a pausa), e
		// perguntar antes das condicoes de distancia gastaria golpe em tique que nao ia socar.
		bool bate = (noAlcance || investida) && !guarda && EmRajada(p, rng);

		// UM SORTEIO SO decide leve/pesado. Dois sorteios independentes (um pra cada campo) davam
		// um tique em que os dois saiam falsos -- a rajada engolia um golpe sem nada explicando.
		//
		// E O PESO E O DE AGORA (`FuriaExpressa`), e nao o do molde: o mesmo corpo bate mais pesado
		// depois de ter apanhado um tempo. E o `rage` do `behavior_check`, que existe pra isso.
		//
		// A INVESTIDA E SEMPRE PESADA, e nao por gosto: o arranque LONGO (cinco tiles de busca, e
		// quinze com o alvo marcado) so existe no golpe pesado -- `Aproximar(a, longo: golpe ==
		// Pesado)`. Um leve daqui buscaria alguem a 80 px e voltaria sem investir.
		bool pesado = bate && temFolego && (investida || rng.NextDouble() < FuriaExpressa);

		// DESCER DE PROPOSITO quando o folego acaba no ar. Ser derrubado e uma FALHA: o corpo cai a
		// 16 tiles por segundo e chega no chao sem guarda, sem Ki e no meio do inimigo. Descer usa a
		// MESMA tecla do jogador (segurar control), e nao um desligamento por dentro.
		bool pousar = p.EstouVoando && DeveDescerDoCeu(p);

		return new Comando
		{
			Rumo = rumo,
			Guardar = guarda,
			Leve = bate && !pesado,
			Pesado = pesado,
			QuerSubir = !pousar && QuerSubirPara(p),
			QuerDescer = pousar || QuerDescerPara(p),
		};
	}

	/// <summary>
	/// O RITMO EM RAJADA. Ninguem soca num metronomo: vem uma sequencia, vem uma pausa.
	///
	/// Isto e mecanicamente CERTO alem de parecer melhor -- o `Combo` do `CombatState` (`:85`) so
	/// conta enquanto os golpes ficam dentro da <see cref="Jandirus.Core.Combat.CombatState.JanelaDeCombo"/>,
	/// entao socar em rajada e o que faz o baque escalar de pequeno pra grande. E o NPC **nunca**
	/// ataca no instante exato em que a recarga zera: essa precisao e a assinatura de um robo.
	///
	/// ============================ A RAJADA PASSOU A SER CONTADA, E E O `BarrageAttack` ============================
	/// A versao anterior sorteava, a cada soco, se a pausa seria curta (72%) ou longa -- o que da um
	/// ritmo variado mas SEM FORMA: o numero de golpes seguidos e uma geometrica, e todo NPC do
	/// mundo tem a mesma. O DM nao faz assim: ele decide, ANTES, quantos golpes vao sair --
	/// `BarrageAttack(,,,,rand(2,4),2)` no ataque normal (`NPCAI.dm:413`) e `rand(3,5)` no
	/// finalizador (`:394`) --, e o gate de quem solta rajada e a FURIA com o FOLEGO
	/// (`rage >= 45 && (sr > NPC_AI_STAM_LOW || !prob(ai_intelligence)) && prob(45)`, `:412`).
	///
	/// A diferenca aparece na tela: um bicho calmo e cansado da golpes AVULSOS com pausa entre eles;
	/// um furioso com folego encaixa tres ou quatro seguidos e ai respira. Sao dois ritmos que se
	/// reconhecem -- e, como a furia ANDA durante a luta (ver <see cref="FuriaExpressa"/>), o mesmo
	/// corpo passa de um pro outro sem ninguem trocar o tempero dele.
	/// ==========================================================================================================
	/// </summary>
	private bool EmRajada(in Percepcao p, Random rng)
	{
		if (_voltaDoSoco > 0) return false;

		// ============================ O PONTO DE FALA DO GOLPE MORA AQUI, E SO AQUI ============================
		// Esta funcao e o UNICO lugar do cerebro que decide "sai um soco agora": as duas receitas que
		// batem (`Pressao` e `Alcance`) ambas terminam nela. Por isso a fala do golpe fica aqui e nao
		// numa das duas -- se ficasse nelas, a receita que voa esqueceria (era exatamente o defeito
		// que o `Comando.Olhar` custou a este port) e a proxima receita que batesse nasceria muda.
		//
		// E ela sai no COMECO DA RAJADA, e nao a cada soco: `npc_combat_chat` no DM e chamado uma vez
		// por ACAO, e uma acao e `BarrageAttack(,,,,rand(2,4),2)` inteiro (`NPCAI.dm:413-415`). Um
		// grito por soco daria quatro frases em meio segundo -- e o relogio do `Falatorio` ate as
		// engoliria, mas ai a fala sairia no golpe errado da sequencia.
		//
		// AS TRES OCASIOES SAO AS TRES DO ORIGINAL, na ordem em que ele as pergunta:
		//   finalizador (`:389`) > rajada (`:415`) > golpe avulso (`:418`).
		// ==================================================================================================
		if (_golpesQueFaltam <= 0)
		{
			_golpesQueFaltam = TamanhoDaRajada(p, rng);
			Dizer(_finalizando ? Momento.Finalizar
				 : _golpesQueFaltam > 1 ? Momento.Rajada
				 : Momento.Socar, rng);
		}
		_golpesQueFaltam--;

		_voltaDoSoco = _golpesQueFaltam > 0
			// DENTRO da rajada: o intervalo curto que mantem o combo de pe
			? 0.05 + rng.NextDouble() * 0.12
			// entre rajadas se RESPIRA -- menos quando esta fechando a luta (`:394`, sem a pausa)
			: _finalizando ? 0.20 + rng.NextDouble() * 0.25
						   : 0.45 + rng.NextDouble() * 0.75;
		return true;
	}

	/// <summary>
	/// QUANTOS GOLPES SAEM NESTA RAJADA. Um = golpe avulso (o `attack()` do DM); mais de um = o
	/// `BarrageAttack`. Ver <see cref="EmRajada"/> pras linhas do original.
	/// </summary>
	private int TamanhoDaRajada(in Percepcao p, Random rng)
	{
		bool folego = p.FolegoFrac > FolegoBaixo;

		// FINALIZADOR: `rand(3,5)`, e o gate de folego e o CRITICO e nao o baixo (`:395` cobra
		// `sr > NPC_AI_STAM_CRIT`) -- quem esta fechando a luta gasta a ultima reserva.
		if (_finalizando && p.FolegoFrac > FolegoCritico) return 3 + rng.Next(3);

		// `sr > NPC_AI_STAM_LOW || !prob(ai_intelligence)`: sem folego so o BURRO insiste na rajada.
		bool bancaOFolego = folego || rng.NextDouble() >= Inteligencia;

		if (FuriaExpressa >= PesoQueIndicaFuria && bancaOFolego && rng.NextDouble() < ChanceDaBarragem)
			return 2 + rng.Next(3);   // `rand(2,4)`

		return 1;
	}

	/// <summary>
	/// DESCER DO CEU DE PROPOSITO. Duas contas, e as duas precisam existir:
	///   * a FRACAO pega o lutador grande, cujo 12% ainda sao milhares de Ki;
	///   * o ABSOLUTO pega o pequeno, porque <see cref="Voo.KiQueDerruba"/> e 5 e nao 5%.
	/// O fator 4 e a folga: quatro vezes o Ki que derruba da tempo de descer os 20 tiles.
	/// </summary>
	private static bool DeveDescerDoCeu(in Percepcao p) =>
		p.KiFrac < KiQueMandaPousar || p.Ki < Voo.KiQueDerruba * 4;

	/// <summary>
	/// A QUE ALTURA ELE QUER FICAR, em pixels. **Este e o pedido literal do dono** -- *"acompanhar a
	/// ALTITUDE etc"*.
	///
	/// ============================ O ALVO E O MEIO DO ANDAR, E NAO A ALTURA DO OUTRO ============================
	/// Mirar na altura exata do alvo parece obvio e nao funciona: quem alcanca quem e decidido por
	/// ANDAR (`Voo.Andar`, faixas de 6,67 tiles), e a zona morta que impede o chacoalho tem largura
	/// -- entao parar "perto o bastante" da altura dele pode deixar os dois em andares diferentes,
	/// e ai o NPC fica pairando ao lado do alvo sem conseguir encostar nele, que e pior do que nao
	/// ter subido.
	///
	/// Mirando no MEIO do andar, a zona morta inteira cabe dentro daquele andar (conta:
	/// [(N-0,83)H, (N-0,17)H] esta contido em ((N-1)H, N·H]) -- ou seja, parar em qualquer ponto
	/// tolerado ainda garante o alcance.
	/// ======================================================================================================
	///
	/// CHAO E CHAO: com o alvo em pe e sem a receita de rasante, o desejo e ZERO -- ele desce ate
	/// pousar. Ficar pairando um tiquinho acima do chao daria a vantagem assimetrica do andar 1 de
	/// graca a um bicho burro, e essa vantagem tem que ser uma DECISAO de quem e esperto.
	/// </summary>
	private float AlturaDesejada(in Percepcao p)
	{
		bool rasante = Inteligencia >= InteligenciaQuePairaRasante && p.AndarDoAlvo == 0 && !p.AlvoVoando;
		int andar = rasante ? 1 : p.AndarDoAlvo;
		return andar <= 0 ? 0f : (andar - 0.5f) * (Voo.AlturaMaxima / Voo.Andares);
	}

	private bool QuerSubirPara(in Percepcao p) =>
		p.EstouVoando && p.TemAlvo && AlturaDesejada(p) - p.MinhaAltitude > ZonaMortaDeAltura;

	/// <summary>
	/// Descer tem uma folga a MENOS que subir: pra o CHAO nao ha meio termo. Com a zona morta
	/// valendo tambem pra o zero, o corpo pararia de descer a 71 px -- que e o andar 1 -- e o
	/// "pouso" nunca aconteceria.
	/// </summary>
	private bool QuerDescerPara(in Percepcao p)
	{
		if (!p.EstouVoando || !p.TemAlvo) return false;
		float d = AlturaDesejada(p);
		return p.MinhaAltitude - d > (d <= 0f ? 0f : ZonaMortaDeAltura);
	}

	/// <summary>A folga da zona morta de altura: um terco de andar.</summary>
	public const float ZonaMortaDeAltura = Voo.AlturaMaxima / Voo.Andares / 3f;
}
