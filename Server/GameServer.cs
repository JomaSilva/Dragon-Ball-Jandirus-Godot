using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>Um jogador conectado, do ponto de vista do servidor.</summary>
public sealed class ServerPlayer
{
	public int Id;
	/// <summary>
	/// A CONEXAO. NULA quando este 'jogador' e um NPC -- o clone da meditacao, e mais tarde
	/// os NPCs do mundo. Todo envio passa por `Peer?.Send`, entao um corpo sem dono existe no
	/// mundo, aparece no snapshot dos outros e apanha normalmente, so nao recebe nada.
	/// </summary>
	public NetPeer? Peer;
	public string Name = "";
	public ZoneKey Zone;

	/// <summary>
	/// ONDE ESTE CORPO ESTA. Virou PROPRIEDADE por uma razao so: toda escrita -- o arremesso, a IA, a
	/// fusao, o embate, a volta do planeta, a troca de zona, um teste que forja um corpo -- apaga
	/// <see cref="PosVemDoCliente"/> sozinha, sem que cada um desses lugares precise saber que existe
	/// um carimbo. Uma lista de "onde o servidor move" escrita a mao ficaria velha na primeira
	/// mecanica nova, e o sintoma seria um corpo desenhado com a hora de outro.
	/// </summary>
	public Vec2 Pos
	{
		get => _pos;
		set { _pos = value; PosVemDoCliente = false; }
	}
	private Vec2 _pos;

	/// <summary>
	/// A POSICAO FOI AFIRMADA PELO CLIENTE (e aceita, ou devolvida como estava) e valia em
	/// <see cref="PosMs"/>. Falso = o servidor escreveu por ultimo, e ai ela vale AGORA, sempre: a
	/// idade que vai no snapshot e zero. So o `Input` liga isto. Ver `EntityState.IdadeMs`.
	/// </summary>
	public bool PosVemDoCliente;

	/// <summary>
	/// O relogio de quadro DESTE servidor (`GameServer.RelogioDeQuadrosMs`) em que <see cref="Pos"/>
	/// valia -- a hora que veio no input, traduzida pelo <see cref="Relogio"/>. So significa algo com
	/// <see cref="PosVemDoCliente"/>.
	/// </summary>
	public long PosMs;

	/// <summary>
	/// O RELOGIO DESTE CLIENTE, alinhado ao meu: `meu = dele + Relogio.Valor`. E o MINIMO em janela de
	/// (hora de chegada - hora de envio), que e a amostra do pacote que passou mais rapido -- ver
	/// `DeslocamentoDeRelogio`. Dois segundos por janela: a deriva dos cristais nao chega a 1 ms nisso.
	/// </summary>
	public readonly Jandirus.Net.DeslocamentoDeRelogio Relogio = new(maximo: false, janelaMs: 2000);

	public Facing Facing;
	public bool Moving;
	public float SpeedStat = 1f;
	public string Race = "";
	public string Class = "";
	public long LastInputMs;   // pra medir o dt real entre inputs (o cliente nao dita o tempo)
	public int Corrections;    // quantas vezes ja foi puxado de volta (sinal de cheat/lag cronico)

	/// <summary>
	/// A FICHA AUTORITATIVA. Vive so aqui: o cliente recebe os numeros prontos, nunca os
	/// ingredientes. Stat e BP sao a unica coisa que "cliente calcula" NAO cobre -- movimento
	/// da pra recalcular e conferir, poder nao.
	/// </summary>
	public Fighter Ficha = null!;

	/// <summary>
	/// O CORPO e o estado de luta. Vive so na memoria do servidor e morre com a sessao --
	/// guarda erguida, atordoamento e recarga de golpe nao sao patrimonio de personagem. O
	/// que persiste (vida dos membros) e gravado a parte no <see cref="CharacterSave"/>.
	/// </summary>
	public CombatState Combate = null!;

	/// <summary>Assinatura da ultima ficha lenta enviada -- ver MandarAtributos.</summary>
	public string SigAtributos = "";

	/// <summary>O que este personagem aprendeu, e quantos marcos tem pra gastar.</summary>
	public Jandirus.Core.Skills.SkillBook Livro = null!;

	/// <summary>O NIVEL de cada skill. Sobe sozinho enquanto a skill tem dono.</summary>
	public Jandirus.Core.Skills.NiveisDeSkill Niveis = new();

	/// <summary>
	/// KARMA. Sobe protegendo, desce matando -- e a porta moral dos cargos (a escola do Grou
	/// pede karma zero ou MENOS; o Senhor do Inferno, -50 ou pior).
	/// </summary>
	public int Karma;

	/// <summary>
	/// O CEREBRO -- QUEM DIRIGE ESTE CORPO AGORA. Nulo = o dono (ou ninguem, num NPC parado).
	/// Preenchido no clone da meditacao, no Oozaru sem controle e na furia lendaria.
	///
	/// **NAO E "isto e um NPC"**: um jogador possuido tem cerebro e continua sendo jogador (ver
	/// `SemAsRedeas`, GameServer.Clone.cs:327). Quem responde "de onde este corpo veio" e o
	/// <see cref="Papel"/>, e quem responde "ele tem dono na tela" e o <see cref="Peer"/>.
	/// </summary>
	public Jandirus.Core.Ai.Cerebro? Cerebro;

	/// <summary>
	/// ============================ A POSSE: O CEREBRO QUE **NAO E DESTE CORPO** ============================
	/// Nao-nulo = a FERA (ou a furia lendaria) esta no comando, e o <see cref="Cerebro"/> acima e o
	/// dela. Nulo = quem dirige este corpo e quem sempre dirigiu -- o dono na tela, ou a mente que o
	/// NPC nasceu tendo.
	///
	/// **POR QUE ELE PRECISOU EXISTIR, E O QUE ESTAVA QUEBRADO SEM ELE.** Ate aqui a posse era lida
	/// como `Cerebro != null`, e isso era verdade porque so JOGADOR era possuido -- e jogador nasce
	/// sem cerebro. No instante em que um NPC pode virar Oozaru, a mesma gaveta passa a guardar duas
	/// coisas opostas, e as duas perguntas do sistema davam a resposta errada:
	///
	///   * *"a fera ja tomou as redeas?"* (`TickDoOozaru`, passo 5) respondia **sim** pra todo NPC,
	///     desde o primeiro quadro -- a rampa de maestria/controle era letra morta nele;
	///   * *"a posse acabou, devolve o corpo"* (`DevolverAsRedeas`) fazia `Cerebro = null` num corpo
	///     cujo cerebro era a mente DELE. Um NPC que saisse do Oozaru viraria **estatua permanente**.
	///
	/// Um `bool` ao lado responderia a primeira e nao a segunda. Guardando o marcador NO PROPRIO
	/// objeto emprestado, as duas perguntas leem a mesma verdade e nao ha o que esquecer de limpar --
	/// e a devolucao sabe distinguir "volta pro dono" de "volta pra mente propria".
	/// ==================================================================================================
	/// </summary>
	public Jandirus.Core.Ai.Cerebro? CerebroDaPosse;

	/// <summary>
	/// O PAPEL -- de que molde este corpo saiu, com que semente, e em que degrau do roteiro esta.
	/// Nulo em todo corpo de jogador.
	///
	/// A FICHA NAO MORA AQUI: um NPC tem `Ficha`, `Livro`, `Niveis` e `Forma` como qualquer
	/// jogador, montados pelas mesmas funcoes. O papel e so o que um jogador nao tem.
	/// Ver <see cref="Jandirus.Core.Npc.PapelDeNpc"/>.
	/// </summary>
	public Jandirus.Core.Npc.PapelDeNpc? Papel;

	/// <summary>Em que chunk do espaco estou. Trocar de chunk dispara o pacote de vizinhanca.</summary>
	public ChunkId ChunkAtual;

	/// <summary>De onde decolei.</summary>
	public string PlanetaDeOrigem = "";

	// ============================ O SOL QUEIMANDO ESTE CORPO ============================
	// Tres campos e nada mais: quem decide e o `GameServer.Sol.cs`, que e uma funcao pura de
	// (posicao, seed, ficha) mais estes marcadores de BORDA. Eles existem porque "entrou" e "saiu"
	// sao eventos, e um tique nao sabe o que o tique anterior viu.
	/// <summary>
	/// Onde eu estava em relacao a estrela no tique passado. E o que transforma o estado continuo
	/// ("estou a 900 px do centro") nos tres eventos que o jogador percebe: o aviso da coroa, o
	/// grito de entrada e o alivio da saida.
	/// </summary>
	public Jandirus.Core.World.ProximidadeDaEstrela CalorNoTique;

	/// <summary>Rate-limit do berro de "voce esta queimando" -- senao ele sai 30x por segundo.</summary>
	public long AvisoDeCalorAte;

	/// <summary>
	/// Quanto de vida a estrela ja tirou deste corpo NA QUEIMADA ATUAL. Zera ao sair.
	///
	/// Nao e mecanica: e o que a mensagem de saida usa pra dizer o preco ("voce sai com 34 de vida
	/// a menos"), e o que a bancada le pra provar que houve dano ANTES da morte -- sem ele, a
	/// unica evidencia de queimadura seria o cadaver.
	/// </summary>
	public double VidaQueimada;

	// ============================== OOZARU (ver GameServer.Oozaru.cs) ==============================

	/// <summary>Em que Oozaru este corpo esta. Estado VIVO -- ninguem continua macaco deslogado.</summary>
	public Jandirus.Core.Forms.FormaOozaru Oozaru;

	/// <summary>Quando a forma cai sozinha (relogio real, ms). O `spawn(3000)`/`spawn(1000)` do DM.</summary>
	public long OozaruAte;

	/// <summary>
	/// O `angertick`: quanto falta de meditacao pra a raiva passar. Reiniciado a cada transformacao.
	/// </summary>
	public double RaivaDoOozaru;

	// ======================= A FURIA LENDARIA (ver GameServer.FuriaLendaria.cs) =======================

	/// <summary>
	/// O PROXIMO VIRAR DO RELOGIO DA FURIA (relogio real, ms). Zero = desarmado (fora de forma
	/// lendaria, em cena, ou com a forma dominada).
	///
	/// ============================ UM CAMPO SO, PORQUE O SENTIDO E DERIVADO ============================
	/// Ele quer dizer duas coisas opostas, e quem escolhe qual e o <see cref="Cerebro"/>:
	///
	///   * com o cerebro NULO -- **quando eu perco as redeas**;
	///   * com o cerebro preenchido -- **quando eu as recupero**.
	///
	/// Dois campos (`ControleAte` + `PosseAte`) seriam a mesma verdade escrita duas vezes, e um deles
	/// estaria sempre velho: so um dos dois relogios corre por vez, e o outro so existe pra alguem
	/// esquecer de zerar. E o mesmo raciocinio que o `TickDoOozaru` ja usa pra NAO ter um
	/// `bool JaPerdeu` ao lado do cerebro (passo 5, `GameServer.Oozaru.cs`).
	/// ============================================================================================
	/// </summary>
	public long FuriaAte;

	/// <summary>
	/// ATE QUANDO ESTE CORPO ESTA EM FURIA EXTREMA (relogio real, ms). Zero = nunca esteve.
	///
	/// A janela do `rageExpire` do DM (`Murder.dm:110-114`, 1200 decimos = 2 minutos), e metade do
	/// estado por tras do <see cref="Jandirus.Core.Forms.PerfilDeFormas.Raiva"/> -- a outra metade e
	/// a <see cref="RaivaLendariaAte"/>. Quem as acende e um ponto so:
	/// <see cref="GameServer.AmigoAbatido"/>.
	///
	/// NAO PERSISTE, e isso e regra e nao economia: luto nao atravessa o logout. Se atravessasse,
	/// o jeito de despertar o Beast seria ver um amigo morrer e deslogar ate ter os 50% de ki
	/// divino -- a janela viraria um item guardado no bolso.
	///
	/// ============================ E O `Fighter.Anger` NASCE DAQUI ============================
	/// O texto anterior deste bloco dizia *"NAO E o `Fighter.Anger`, aquilo e outro sistema e
	/// continua nao portado"*. Deixou de ser verdade nos dois pedacos: o buff de BP da raiva
	/// (`angerBuff`, `Fighter.Power.cs:56`) esta ligado, e **estas duas janelas sao a fonte dele**.
	/// O numero e derivado (`GameServer.RaivaComoNumero`) e nao guardado -- e por isso este campo
	/// nao persistir tambem resolve a persistencia do `Anger`: sem janela, o corpo que volta do
	/// logout nasce em 100, que e "calmo". Uma unica regra, um unico lugar pra revoga-la.
	/// =======================================================================================
	/// </summary>
	public long FuriaExtremaAte;

	/// <summary>
	/// ATE QUANDO ESTE CORPO ESTA EM RAIVA LENDARIA (relogio real, ms) -- a de ver um amigo CAIR.
	///
	/// ============================ POR QUE SAO DOIS CAMPOS E NAO UM CAMPO + UM NIVEL ============================
	/// Com um so (`RaivaAte` + `RaivaNivel`) um nocaute chegando no meio de um luto teria que
	/// decidir se REBAIXA o nivel ou se ignora o evento, e as duas respostas sao erradas: rebaixar
	/// fecharia o SSJ1 no meio da janela, e ignorar perderia a renovacao do prazo. Com duas
	/// janelas independentes nao ha decisao nenhuma a tomar -- cada evento renova a sua, e o nivel
	/// efetivo e simplesmente a mais alta que ainda estiver aberta (ver `NivelDaRaivaDe`).
	///
	/// O DM tem um `rageExpire` so porque la o degrau de raiva sai do `Emotion`, que e um NUMERO
	/// (`Stats.dm:445-449`) -- e aquela regua de cinco degraus e MAGNITUDE, nao fonte: os dois
	/// graus do port escrevem o mesmo `Anger = MaxAnger` no DM (`Murder.dm:112`). E por isso que a
	/// seta da derivacao aponta daqui pro `Anger`, e nunca ao contrario: um numero nao sabe dizer
	/// se voce viu o amigo cair ou morrer. Ver `GameServer.RaivaComoNumero` pro argumento inteiro.
	/// ========================================================================================================
	///
	/// NAO PERSISTE, pelo mesmo motivo da de cima.
	/// </summary>
	public long RaivaLendariaAte;

	/// <summary>
	/// ATE QUANDO A CENA DE FURIA DESTE CORPO ESTA EM RECARGA (relogio real, ms). Zero = pode tocar.
	///
	/// O `rageCinematicCD` do DM (`Murder.dm:139-140`: `world.time + 600` -> 60,0 s, ver
	/// <see cref="Jandirus.Core.Forms.Cinematicas.SegundosEntreFurias"/>).
	///
	/// ============================ POR QUE ELE NAO E DERIVAVEL DAS OUTRAS DUAS JANELAS ============================
	/// A tentacao era usar a propria <see cref="FuriaExtremaAte"/>: "se ja ha luto aberto, nao toca".
	/// Isso ja acontece por outro caminho (a cena so sai na ERUPCAO, e nao no prolongamento) e nao
	/// cobre o caso que este campo existe pra cobrir: duas mortes separadas por, digamos, quatro
	/// minutos, a segunda com a janela ja fechada. Sao duas erupcoes legitimas, e sem uma recarga
	/// PROPRIA a cena tocaria nas duas... o que ate estaria certo. O caso que a quebra e o oposto e e
	/// o comum: uma briga em grupo, tres amigos caindo em sequencia. A primeira morte enfurece, a
	/// segunda chega com a janela ainda aberta -- prolongamento, sem cena --, e a terceira chega DEPOIS
	/// de a janela vencer: erupcao nova, cena de novo, 130 s depois da anterior. E o DM recusa: 60 s e
	/// 60 s.
	///
	/// NAO PERSISTE, pelo mesmo motivo das duas de cima -- e no DM ele e literalmente `var/tmp`.
	/// ========================================================================================================
	/// </summary>
	public long FuriaCenaAte;

	/// <summary>
	/// O DOMINIO SOBRE A FERA EM QUE ESTE CORPO ESTA AGORA, 0 a 100.
	///
	/// DERIVADO: e a maestria da forma `oozaru`/`oozaru_dourado` no livro que todas as outras
	/// formas ja usam. Nao ha campo -- ver o bloco "O `Apeshitskill` FOI DELETADO" em
	/// `Core/Forms/Oozaru.cs`. Fora da forma da 0, porque `Oozaru.Id(Nao)` e vazio e maestria de
	/// forma nenhuma e zero: e a resposta certa pra "quanto voce domina a fera que voce nao e".
	/// </summary>
	public double MaestriaDaFera =>
		Forma.Maestria.De(Jandirus.Core.Forms.Oozaru.Id(Oozaru));

	// =====================================================================
	// AS DUAS DISCIPLINAS DIVINAS -- ver GameServer.Disciplinas.cs
	//
	// Os dois estados existem SEMPRE, e so um deles chega a ser aprendido: as escolas se excluem.
	// Guardar os dois (em vez de um so, com um enum dizendo qual) e o que faz o save sobreviver a
	// uma mudanca de regra futura sem perder o que a pessoa ja tinha.
	// =====================================================================

	/// <summary>O Ultra Instinto: precisao, maestria e o toggle da esquiva autonoma.</summary>
	public Jandirus.Core.Forms.EstadoDeDisciplina UltraInstinct = new();

	/// <summary>O Poder da Destruicao: energia, maestria e o toggle da Aura of Destruction.</summary>
	public Jandirus.Core.Forms.EstadoDeDisciplina PoderDaDestruicao = new();

	/// <summary>Qual das duas este corpo trilhou. Nulo = nenhuma.</summary>
	public Jandirus.Core.Forms.TipoDeDisciplina? Disciplina;

	// --- o que e de combate e nao persiste ---
	/// <summary>Pilhas vivas do bonus pos-esquiva (ate 5), e quando a ultima cai.</summary>
	public int PilhasDeEsquiva;
	public long EsquivaAte;

	/// <summary>Dano do ultimo golpe recebido -- alimenta a Destruction Explosion.</summary>
	public double UltimoGolpeRecebido;

	/// <summary>O death-save da aura e a restauracao do Unbound Ego: UMA vez por luta cada.</summary>
	public bool AuraSalvouNestaLuta, EgoRestauracaoUsada;

	/// <summary>Godly Display: quem esta marcado, ate quando vale o 2o toque, e a recarga.</summary>
	public readonly List<int> GdMarcados = [];
	public long GdJanelaAte, GdRecargaAte;

	/// <summary>Hakai Infusion: ate quando os ataques de ki estao infundidos, e a recarga.</summary>
	public long HakaiAte, HakaiRecargaAte;

	/// <summary>Rate-limit do aviso de "planeta sem superficie".</summary>
	public long AvisoDePousoAte;

	/// <summary>De quem este clone e reflexo (0 = nao e clone).</summary>
	public int DonoDoClone;

	/// <summary>O clone que este jogador invocou (0 = nenhum), e de onde ele veio.</summary>
	public int CloneId;
	public ZoneKey ZonaDeOrigem;
	public Vec2 PosDeOrigem;

	// =====================================================================
	// ============================ OS CAMPOS RECONSTRUIDOS ============================
	// **NENHUM DESTES E CODIGO NOVO.** Eles existiam, foram apagados por uma reversao acidental
	// (`git checkout` num arquivo compartilhado de arvore suja) e levaram o comentario autoral
	// junto. Sem eles o servidor inteiro nao compila -- e nao compilava para NENHUMA das sessoes
	// que trabalhavam no repo, cada uma esperando que outra desatasse o no.
	//
	// O QUE FOI RECONSTRUIDO E SO A DECLARACAO: tipo e valor inicial. Os dois nao foram adivinhados
	// -- vieram do outro lado do funil de save (`CharacterSave`/`CharacterStore.DeJogador`, que grava
	// cada um destes campos com o tipo exato) ou de um uso que so aceita um tipo. Nenhum COMPORTAMENTO
	// foi reescrito: os metodos que leem e escrevem estes campos nunca sairam do lugar.
	//
	// **O QUE NAO DEU PRA RECONSTRUIR ASSIM esta relatado e nao chutado** -- ver o fim deste bloco.
	// =============================================================================
	// =====================================================================

	/// <summary>
	/// O BONECO QUE ESTE JOGADOR DEIXOU PRA TRAS (nulo = esta no proprio corpo).
	/// RECONSTRUIDO; tipo pinado por `GameServer.CorpoLargado.cs:182` (`ServerPlayer? boneco = pl.BonecoLargado`).
	/// </summary>
	public ServerPlayer? BonecoLargado;

	/// <summary>
	/// EU SOU O BONECO DE QUEM (0 = nao sou boneco de ninguem). RECONSTRUIDO; tipo pinado por
	/// `CorpoLargado.cs:87` (`DonoDoCorpoLargado = pl.Id`).
	///
	/// **E a terceira perna do <see cref="Jandirus.Core.Npc.Gente.EhJogador"/>** -- o unico marcador
	/// que corta o boneco largado, que passa pelas outras duas (ele carrega a Ficha do dono de
	/// proposito, pro Sense enxergar quem esta em transe). Ver `Core/Npc/Gente.cs`.
	/// </summary>
	public int DonoDoCorpoLargado;

	// =====================================================================
	// O CADAVER (ver GameServer.Cadaver.cs)
	// =====================================================================
	/// <summary>
	/// ESTE CORPO E UM CADAVER -- o `/obj/mobCorpse` do DM (`Corpse.dm:1`), que aqui e um CORPO e nao
	/// um objeto, porque o pedido do dono e *"TODAS AS INTERACOES DE UM CORPO VIVO"*.
	///
	/// ============================ POR QUE ELE PRECISA DE UM MARCADOR PROPRIO ============================
	/// A tentacao era deriva-lo: "cadaver e corpo morto sem `Peer` e sem `Papel`". Isso ja e verdade,
	/// **e ja e verdade de outra coisa tambem** -- o reflexo da mente morto, a fera possuida que caiu, o
	/// corpo forjado de bancada. Os tres cairiam na mesma peneira, e o teto de lotacao passaria a
	/// desfazer reflexo de gente meditando.
	///
	/// A pergunta certa nao e "como voce esta" e sim **"de onde voce veio"** -- e essa nunca e derivavel
	/// do estado. E a mesma razao pela qual <see cref="Papel"/> e um campo e nao um `if`, e pela qual o
	/// <see cref="Jandirus.Core.Npc.Gente"/> precisou de tres marcadores em vez de um.
	///
	/// **ELE NAO MUDA O `Gente`**: um cadaver nao tem dono na tela, entao `EhJogador` ja o corta pelo
	/// `Peer`; e nao tem papel, entao `EhNpcDoMundo` ja o corta tambem. Ele cai no TERCEIRO grupo (o do
	/// clone e do boneco), que e exatamente onde ele pertence -- nao conta pra lotacao de planeta, nao
	/// entra na reposicao de habitante e nao renasce.
	/// ================================================================================================
	/// </summary>
	public bool ECadaver;

	/// <summary>
	/// DE QUEM E ESTE CORPO. Guardado porque o <see cref="Name"/> ja e a frase inteira ("corpo de
	/// Fulano") e a lapide precisa do nome limpo -- `"Here lies [name]"` (`Corpse.dm:20`) usa o nome do
	/// morto e nao o do cadaver. Extrair de volta da frase seria parsear texto que eu mesmo montei.
	/// </summary>
	public string NomeDeQuemMorreu = "";

	/// <summary>
	/// QUANDO ESTE CORPO CAIU (`NowMs()`). **NAO E UM PRAZO** -- nao ha prazo, ver
	/// <see cref="Jandirus.Core.World.Cadaver.TetoPorZona"/>. Ele so responde "qual e o mais antigo"
	/// quando a zona estoura a lotacao, e o mais antigo e o que menos falta faz.
	/// </summary>
	public long CaiuEm;

	/// <summary>
	/// O ULTIMO ID DE CADAVER QUE FOI ANUNCIADO A ESTE JOGADOR (0 = nenhum). Mesma familia do
	/// <see cref="EnvAureola"/> e dos outros campos `Env*`: o pacote so sai quando MUDA, e o que ele
	/// compara e o que FOI ENVIADO -- nao o valor de tres linhas atras. Ver `MandarCadaverPerto`.
	/// </summary>
	public int EnvCadaverPerto;

	/// <summary>
	/// O CORPO COMO ELE ENTROU NA MENTE (nulo = nao ha foto). RECONSTRUIDO junto do tipo
	/// <see cref="Jandirus.Server.FotoDaMente"/>; ver `GameServer.Mente.cs:320` e `:339`.
	/// </summary>
	public FotoDaMente? FotoDaMente;

	/// <summary>
	/// O PINO DE BP DO CORPO QUE A MENTE ERGUEU (0 = nao e pinado por aqui). RECONSTRUIDO.
	///
	/// Escrito por `EspelharODono` e reancorado a cada tique pelo `TicarUmCorpo` -- e o
	/// `mind_seed_bp` do `MindClone.NPCTicker()` (MindMeditate.dm:322-327).
	///
	/// Existe porque o reflexo passa pelos MESMOS lacos de ficha de um jogador: sem o pino, lutar
	/// renderia BP pra ele (`Fighter.FightGain`) e um treino longo deixaria o reflexo mais forte do
	/// que o dono que o gerou. **A lembranca de chefe nao usa este campo** (fica em zero): o pino
	/// dela e o `Papel.BpsPinados`, reancorado pelo `TickDoRoteiro` -- dois pinos sobre o mesmo
	/// numero seriam duas contas discordando no meio da luta.
	/// </summary>
	public double BpDaMente;

	/// <summary>
	/// ESTE CORPO JA CAIU NESTA SESSAO DE TRANSE (e ainda nao se reergueu). RECONSTRUIDO.
	///
	/// E o latch do `clone_watch()` do DM: o anuncio da queda sai UMA vez (`DerrubadoNaMente`) e
	/// quem conta o prazo daqui pra frente e o nocaute; o bit cai quando o corpo e reerguido e curado
	/// (`ReerguerOOponente`), o que reabre o ciclo pra a proxima queda. E o que faz o reflexo ser um
	/// parceiro de treino INFINITO -- nocaute levanta, so a morte encerra.
	/// </summary>
	public bool CaiuNaMente;

	/// <summary>
	/// ATE QUANDO A COLEIRA DESTE CORPO FICA CALADA (`NowMs()`). RECONSTRUIDO.
	///
	/// A mente nao tem mais parede, e quem devolve o reflexo que ficou pra tras e o
	/// <see cref="GameServer.ReaparecerNaFrente"/>. O SALTO em si nao precisa de prazo nenhum -- ele
	/// so acontece a 40 tiles de distancia --, mas **a FRASE precisa**: um jogador voando em linha
	/// reta cruza 40 tiles a cada poucos segundos, e sem isto ele leria a mesma linha pra sempre.
	///
	/// O prazo mora no CORPO e nao no dono porque e o corpo que reaparece: dois oponentes na mesma
	/// mente (o reflexo e um chefe convocado) tem cada um a sua vez de falar.
	/// </summary>
	public long ColeiraCaladaAte;

	/// <summary>
	/// ATE QUANDO O `UltimoAgressor` AINDA VALE (`NowMs()`; 0 = rancor frio). RECONSTRUIDO.
	///
	/// E o `combat_tag_duration` do original (`UpdateFightingList.dm:8`, 900 decimos = 90 s, em
	/// <see cref="Jandirus.Core.Npc.Povoamento.SegundosDeRancor"/>), escrito por um funil so
	/// (`MarcarAgressao`) e lido por quatro sistemas: a presa do cidadao, o re-engajamento do
	/// defensor de invasao, o "quem me matou" da conquista e o "a saga esta engajada" do ultimato.
	///
	/// **O PRAZO E O ESTADO** -- nao ha um `EstouComRaiva` ao lado, de proposito: quem esquece e o
	/// relogio, e um segundo campo dizendo a mesma coisa seria a segunda verdade que discorda.
	/// </summary>
	public long RancorAte;

	/// <summary>
	/// QUEM JA VOAVA ANTES DE ENTRAR NA NAVE, pra devolver o voo ao sair. RECONSTRUIDO; tipo pinado
	/// por `GameServer.Nave.cs:454` (`pl.NaveDevolveVoo = pl.Voando`). E o `pod_had_flight` do DM.
	/// </summary>
	public bool NaveDevolveVoo;

	/// <summary>
	/// HA QUANTO TEMPO ESTE FROST DEMON ESTA NA FORMA INSTAVEL, em segundos. RECONSTRUIDO; tipo
	/// pinado por `GameServer.Frost.cs:164` (`+= dt`, e `dt` e `double`).
	/// </summary>
	public double FrostInstavelSegundos;

	/// <summary>
	/// QUANTO FALTA PRO PROXIMO AVISO DE INSTABILIDADE, em segundos (0 = pode avisar agora).
	/// RECONSTRUIDO; tipo pinado pelo mesmo `Math.Max(0, ... - dt)` de `GameServer.Frost.cs:98`.
	/// </summary>
	public double FrostAvisoEmSegundos;

	/// <summary>
	/// A OFERTA DE MESTRE QUE ESTA NA MESA DESTE ALUNO (nulo = ninguem ofereceu nada). RECONSTRUIDO;
	/// tipo pinado por `GameServer.Mestre.cs:295` (`new PedidoDeMestre(...)`).
	/// </summary>
	public PedidoDeMestre? PedidoDoMestre;

	/// <summary>
	/// A LICAO OFERECIDA A ESTE ALUNO (nulo = ninguem esta ensinando nada). RECONSTRUIDO; tipo
	/// pinado por `GameServer.Ensino.cs:120` (`new PedidoDeLicao(...)`).
	/// </summary>
	public PedidoDeLicao? PedidoDeLicao;

	/// <summary>
	/// QUANDO ESTE MESTRE PODE ENSINAR UMA SKILL DE NOVO (`NowMs()`). RECONSTRUIDO; tipo pinado por
	/// `GameServer.Ensino.cs:118` (`agora + (long)(...)`).
	/// </summary>
	public long RecargaDaLicao;

	/// <summary>
	/// QUANDO ESTE MESTRE PODE DESPERTAR UMA FORMA DE NOVO (`NowMs()`). RECONSTRUIDO; tipo pinado
	/// pelo save (`CharacterSave.MestreRecargaAte`, um `long`) e por `GameServer.Mestre.cs:460`.
	/// </summary>
	public long RecargaDeEnsino;

	/// <summary>
	/// AS FORMAS QUE ESTE PERSONAGEM VIU ALGUEM USAR (por `FormaDef.IdRede`). RECONSTRUIDO.
	///
	/// **E CONJUNTO E NAO LISTA**, e isso nao e gosto: `NotarFormaVista` (`Mestre.cs:229`) chama
	/// `Add` sem conferir duplicata, e roda toda vez que alguem se transforma perto. Numa lista o
	/// campo cresceria sem teto e iria inteiro pro disco. O save guarda `List&lt;int&gt;` porque JSON
	/// nao tem conjunto, e a copia `[.. pl.FormasVistas]` funciona nos dois sentidos.
	/// </summary>
	public HashSet<int> FormasVistas = [];

	/// <summary>
	/// OS CHEFES QUE ESTE PERSONAGEM VIU EM JOGO (por id de molde). RECONSTRUIDO.
	///
	/// **CONJUNTO por prova direta**: `GameServer.Mente.cs:400` usa o RETORNO do `Add`
	/// (`if (!o.ChefesVistos.Add(papel.Molde.Id)) continue;`), que so existe em `HashSet`.
	/// </summary>
	public HashSet<string> ChefesVistos = [];

	/// <summary>
	/// AS TECNICAS DE KI QUE ESTE PERSONAGEM CRIOU. RECONSTRUIDO; tipo pinado pelo save
	/// (`CharacterSave.Customizadas`, `List&lt;TecnicaCustomizada&gt;`) e por `pl.Customizadas.Count`.
	/// </summary>
	public List<Jandirus.Core.Skills.TecnicaCustomizada> Customizadas = [];

	/// <summary>
	/// A TECNICA ABERTA NA BANCADA AGORA (nula = nenhuma). RECONSTRUIDO; tipo pinado por
	/// `GameServer.Customizadas.cs:125` (`new TecnicaCustomizada { Id = id }`).
	/// </summary>
	public Jandirus.Core.Skills.TecnicaCustomizada? Mesa;

	/// <summary>
	/// QUANDO ESTE PERSONAGEM ENTROU NA SALA DO TEMPO PELA ULTIMA VEZ (`NowMs()`; 0 = nunca).
	/// RECONSTRUIDO; tipo pinado pelo save (`CharacterSave.SalaUltimaEntrada`, `long`).
	/// </summary>
	public long SalaUltimaEntrada;

	/// <summary>
	/// ELE TEM A CHAVE DA SALA DO TEMPO. RECONSTRUIDO; tipo pinado pelo save (`bool`).
	/// </summary>
	public bool SalaAutorizada;

	/// <summary>
	/// ELE ESTA TRANCADO LA DENTRO. RECONSTRUIDO; tipo pinado pelo save (`bool`).
	/// </summary>
	public bool SalaPreso;

	/// <summary>
	/// ELE ESTA SELADO -- as seis `mob/var` de `Sealing.dm:22-28` num objeto. RECONSTRUIDO do save.
	///
	/// NASCE PREENCHIDO (e nao `null`) porque a pergunta "esta selado?" e feita no tique, por corpo,
	/// e um `?.` por consulta multiplicaria a chance de alguem esquecer o `?`. O objeto vazio pesa
	/// seis campos e responde `Preso == false`, que e a resposta certa pra 99,9% dos corpos.
	/// Ver `Core/Combat/Selo.cs` e `GameServer.Selo.cs`.
	/// </summary>
	public Jandirus.Core.Combat.Selo Selo = new();

	/// <summary>
	/// QUANTOS DIAS DE SALA ESTA SESSAO JA CONSUMIU. RECONSTRUIDO; tipo pinado pelo save (`double`).
	/// </summary>
	public double SalaDiasDaSessao;

	/// <summary>
	/// QUANDO A JANELA DE SAIDA DA SALA FECHA (`NowMs()`; 0 = janela nao armada). RECONSTRUIDO;
	/// tipo pinado por `GameServer.SalaSessao.cs:190` (`pl.SalaJanelaAte - NowMs()`).
	///
	/// **NAO VAI PRO DISCO, e isso e desenho e nao esquecimento**: os quatro campos de Sala que o
	/// save guarda descrevem a SITUACAO (entrou quando, tem chave, esta preso, gastou quantos dias);
	/// este e um relogio de dois minutos que so faz sentido com o servidor no ar.
	/// </summary>
	public long SalaJanelaAte;

	/// <summary>Quando a regeneracao volta a ficar disponivel (relogio real, ms).</summary>
	public long RegenLivreEm;

	/// <summary>
	/// SEGUNDOS SEGUIDOS deitado no tanque de regeneracao com um membro faltando -- ver
	/// `SegundosDoRegeneradorPorMembro`. Zera ao sair de cima da maquina, de proposito: no DM o
	/// sorteio e uma volta do `Ticker()` e nao um deposito, entao entrar e sair nao acumula.
	///
	/// **NAO VAI PRO SAVE.** Cinco minutos deitado e uma sessao, nao patrimonio; e o membro que
	/// voltou (esse sim) ja e gravado pelo `save.Membros`.
	/// </summary>
	public double TanqueDeMembro;

	/// <summary>
	/// A FASE DA LUA QUE ESTE JOGADOR JA VIU ANUNCIADA neste ceu, e se ela estava no alto.
	///
	/// Sao dois campos e nao um porque o ceu tem dois eventos distintos: a lua NASCER (a fase
	/// entra no ceu) e a lua SE POR. Guardar so a fase faria a lua cheia ser anunciada de novo
	/// toda vez que o tique rodasse, e um aviso em vermelho por segundo nao e um acontecimento --
	/// e ruido. Zeram na troca de zona: a lua de Vegeta nao e a da Terra.
	/// </summary>
	public int LuaVista;
	public bool LuaEstavaNoCeu;

	/// <summary>Em que forma esta e quanto domina de cada uma.</summary>
	public Jandirus.Core.Forms.EstadoDeForma Forma = new();
	public string SigSkills = "";

	/// <summary>
	/// SEGURANDO C: reunindo energia agora. Estado VIVO (nao vai pro disco) -- ninguem continua
	/// carregando depois de deslogar, e o `is_drawing` do original tambem nao persistia.
	/// </summary>
	public bool Carregando;

	/// <summary>
	/// QUANTOS SEGUNDOS AINDA FALTAM DA CINEMATICA que prende este corpo. 0 = ninguem preso.
	///
	/// ============================ POR QUE O SERVIDOR PRECISA SABER DA CENA ============================
	/// A cena e do cliente e o prazo tambem era. So que o dono relatou o Ki caindo DURANTE a
	/// transformacao, e com as cenas de volta aos tempos do DM isso deixou de ser detalhe: a do SSJ3
	/// prende o corpo por 140 s, e o dreno da forma podia esvaziar o tanque e DERRUBAR o jogador da
	/// forma no meio da estreia dela. Quem cobra o Ki e o servidor, entao quem tem que saber e ele.
	///
	/// NAO NASCEU PACOTE NOVO. O servidor ja deriva o degrau da cena (`Cinematicas.Degrau`, dentro do
	/// `AnunciarForma`) e o Core ja sabe converter degrau em prazo (`NoDegrau` -> `SegundosPreso`) --
	/// e essa e a MESMA conta que o cliente faz pra decidir por quanto tempo prender o corpo. Perguntar
	/// ao cliente "voce ainda esta em cena?" seria pedir a quem a tranca prende que diga quanto tempo
	/// ela dura, alem de por uma segunda verdade no fio sobre um numero que ja existe nos dois lados.
	///
	/// CONTAGEM REGRESSIVA e nao "instante em que acaba": o tique da forma ja recebe o `dt`, e um prazo
	/// em relogio de parede obrigaria a escolher entre `Time.GetTicksMsec` (que a bancada nao controla)
	/// e um acumulador de tempo de servidor que nao existe.
	///
	/// VIVO, nao vai pro disco -- pelo mesmo motivo do `Carregando` logo acima: ninguem continua
	/// assistindo a propria transformacao depois de deslogar.
	/// ================================================================================================
	/// </summary>
	public double CenaSegundos;

	/// <summary>
	/// ATE QUANDO ESTE ATACANTE NAO PRECISA OUVIR DE NOVO que o golpe atravessou quem esta em
	/// cinematica. Instante de relogio de servidor, em ms.
	///
	/// EXISTE POR CAUSA DA CADENCIA: um soco leve sai tres vezes por segundo, e a cena mais longa do
	/// jogo (SSJ3) prende o corpo por 140 s -- sem freio seriam quatrocentas linhas iguais no chat de
	/// quem esta socando. Ver `GameServer.AvisarQueOAlvoEstaEmCena`.
	///
	/// INSTANTE E NAO CONTAGEM REGRESSIVA, e nao por gosto: um prazo que se apaga sozinho ao vencer
	/// nao precisa de ninguem pra zera-lo -- e este arquivo ja registra duas vezes o custo de um bit
	/// que alguem tinha que lembrar de limpar. Vivo, nao vai pro disco.
	/// </summary>
	public long AvisoDeCenaMs;

	/// <summary>
	/// ATE QUANDO ESTE LUTADOR NAO PRECISA OUVIR DE NOVO **por que o soco dele nao saiu** (corpo
	/// arremessado ou atordoado). Instante de relogio de servidor, em ms.
	///
	/// SEPARADO do <see cref="AvisoDeCenaMs"/> de proposito: aquele fala do ALVO (ele esta protegido) e
	/// este fala do MEU CORPO (eu nao consigo bater). Um relogio so faria uma explicacao engolir a
	/// outra justamente quando as duas sao verdade -- socar alguem em cinematica logo depois de levar
	/// um arremesso. Vivo, nao vai pro disco. Ver `GameServer.ExplicarPorQueNaoSai`.
	/// </summary>
	public long AvisoDoCorpoMs;

	/// <summary>
	/// SESSAO DE TREINO (`Training_Session`): o BP de quando ela comecou. Nao persiste -- no
	/// original tambem nao (`insession` e `startingbp` sao de runtime), e faz sentido: uma sessao
	/// e "esta jogatina", nao um recorde.
	/// </summary>
	public double BpDaSessao;
	public bool EmSessao;

	/// <summary>
	/// `knockbackon`: meus golpes arremessam? Ligado por padrao, como no original
	/// (`attack_bck.dm:3`). Desligar existe pra treinar com alguem sem joga-lo pra longe.
	/// </summary>
	public bool Knockback = true;

	/// <summary>O VOO em curso: quantos tiques faltam, pra onde, e com que forca. Ver GameServer.Empurrao.cs.</summary>
	/// <summary>A direcao pra onde ele estava olhando quando caiu. Congelada -- ver Sheet().</summary>
	public Facing FacingDaQueda;

	/// <summary>
	/// PRA ONDE O CORPO ESTA DEITADO -- e o unico angulo que o cliente desenha.
	///
	/// Voando, e a direcao do ARREMESSO; caido, e a direcao da QUEDA. Um campo so porque e uma
	/// pergunta so ("a cabeca aponta pra onde?"), e porque ter duas fontes pro mesmo angulo foi
	/// exatamente o defeito anterior: o corpo tremia entre o valor de um e o do outro.
	/// </summary>
	/// <summary>
	/// ESTE MORTO ESTA DE PE? A porta do <see cref="Jandirus.Core.World.Alem.MortoDePe"/> -- ver la
	/// o argumento de por que a resposta e DERIVADA (morto + lugar de morto) e nao um campo.
	///
	/// Falso pra quem esta vivo, entao ela serve sozinha como "estou no alem, morto".
	/// </summary>
	public bool MortoDePe => Jandirus.Core.World.Alem.MortoDePe(Ficha.dead, Zone.Name);

	/// <summary>
	/// O corpo esta deitado? Caido ou voando -- e a mesma pergunta pro desenho.
	///
	/// **MORTO NO OUTRO MUNDO NAO ESTA DEITADO.** O DM levanta o morto (`spawn Un_KO()`,
	/// `Death.dm:89`) antes de manda-lo pro alem, e la dentro ele anda, voa e treina -- a queda dos
	/// 2 s e o CADAVER, e o cadaver fica no mundo dos vivos. Ver <see cref="MortoDePe"/>.
	/// </summary>
	public bool Deitado => Ficha.KO || (Ficha.dead && !MortoDePe) || TiquesDeVoo > 0;

	/// <summary>
	/// DE ONDE VEIO O ULTIMO GOLPE QUE ENCOSTOU -- o vetor do soco, apontando pra longe de quem bateu.
	///
	/// E o MESMO vetor que o arremesso usa (`RumoDoVoo`), guardado tambem pros golpes que derrubam
	/// sem arremessar. Zero quando o corpo caiu sem levar pancada (fome, por exemplo).
	/// </summary>
	public Vec2 RumoDoGolpe;

	/// <summary>
	/// PRA ONDE A CABECA APONTA quando o corpo esta deitado.
	///
	/// ============================ ERAM DOIS ANGULOS PRA UM CORPO SO ============================
	/// Voando, isto devolvia a direcao do ARREMESSO; caido, a direcao pra onde o sujeito ESTAVA
	/// OLHANDO. Sao duas perguntas diferentes, e quem leva knockback e desmaia no ar responde as
	/// duas ao mesmo tempo -- entao o corpo voava deitado num angulo e, ao encostar no chao,
	/// estalava pra outro. O dono descreveu exatamente isso: "enquanto voava desmaiado ele ficava
	/// virado pra um lado nada a ver".
	///
	/// A SAIDA FOI DELE, e e a certa: quem decide o angulo de um corpo caido nao e pra onde ele
	/// olhava, e a direcao DE ONDE VEIO O GOLPE. Um soco que joga pra leste deita o corpo pra leste,
	/// esteja ele no ar ou no chao -- e como o arremesso ja usava esse mesmo vetor, os dois casos
	/// passam a ser um so.
	///
	/// O OLHAR SOBROU DE RESERVA, pra quem cai sem ter apanhado: `FacingFrom` devolve o olhar
	/// anterior quando o vetor e nulo, e ai a direcao antiga e a unica resposta que existe.
	/// ===========================================================================================
	/// </summary>
	public Facing DirecaoDeitado => MoveRules.FacingFrom(
		TiquesDeVoo > 0 ? RumoDoVoo : RumoDoGolpe, FacingDaQueda);

	/// <summary>
	/// ============================ DE ONDE VEIO O GOLPE -- escrito por quem bate, e O MORTO NAO GIRA ============================
	/// A porta unica de <see cref="RumoDoGolpe"/> (o soco em `GameServer.Combat.cs`, o feixe que
	/// carrega em `GameServer.Projeteis.cs`). Ela existe por causa do cadaver: *"personagens que
	/// morreram (...) giram como se tivessem ficado de pe"* -- o relato do dono.
	///
	/// No DM o cadaver e um `/obj/mobCorpse` (`Corpse.dm:75-85`): ele nao e mob, nao tem `dir`
	/// dirigido por ninguem, e o `M.dir = get_dir(M,src)` que o golpe escreve na vitima
	/// (`CombatMovement.dm:302`) nunca o alcanca. O corpo no chao e a FOTO de como caiu. Aqui o
	/// cadaver e um `ServerPlayer`, e tudo que escreve num corpo vivo escreveria nele tambem -- entao
	/// a recusa mora no corpo, e nao em cada escritor: uma terceira porta que aparecer gira o vivo e
	/// nao gira o morto sem saber que existe uma diferenca.
	///
	/// O VIVO CONTINUA GIRANDO, inclusive o nocauteado: e o `M.dir = get_dir(M,src)` do original, e e
	/// o que faz um corpo derrubado deitar pra onde o golpe veio.
	/// =============================================================================================================
	/// </summary>
	public void ApontarRumoDoGolpe(Vec2 rumo)
	{
		if (Ficha.dead) return;
		RumoDoGolpe = rumo;
	}

	/// <summary>
	/// CONGELA A DIRECAO DEITADA em <paramref name="dir"/>: e a foto do cadaver ao nascer e o fim do
	/// arremesso de quem pousa caido.
	///
	/// ============================ O ESTALO DO FIM DO ARREMESSO ============================
	/// `DirecaoDeitado` troca de fonte quando o voo acaba (`RumoDoVoo` -> `RumoDoGolpe`), e o
	/// `RumoDoGolpe` de um corpo jogado pelo AGARRAO e o do ultimo soco que ele levou -- que pode ter
	/// vindo de qualquer lado, minutos antes. O corpo deslizava deitado numa direcao e, ao parar,
	/// estalava pra outra. No cadaver era pior: o `RumoDoGolpe` dele e zero, e o estalo ia pro
	/// `FacingDaQueda`, que num corpo recem-erguido era o padrao (`South`).
	///
	/// Zerar o rumo e escrever a queda faz o `FacingFrom` devolver <paramref name="dir"/> ate que um
	/// golpe novo escreva outro rumo -- e no morto golpe nenhum escreve (ver
	/// <see cref="ApontarRumoDoGolpe"/>), entao no morto isto e definitivo.
	/// ======================================================================================
	/// </summary>
	public void CongelarDirecaoDeitada(Facing dir)
	{
		FacingDaQueda = dir;
		RumoDoGolpe = default;
	}

	/// <summary>De onde o corpo SAIU na ultima investida. E onde a miragem nasce -- ver Aproximar.</summary>
	public Vec2 SaiuDe;

	// ============================== VOO (ver GameServer.Voo.cs) ==============================

	/// <summary>O `flight` do original: pairando. Estado VIVO -- ninguem continua no ar deslogado.</summary>
	public bool Voando;

	/// <summary>O `flightspeed`: modo Superflight. Multiplica o custo por 450/flightability.</summary>
	public bool VooRapido;

	/// <summary>
	/// A que altura o corpo esta, em pixels. Zero = chao.
	///
	/// NAO SE CONFUNDE COM <see cref="Voando"/>: quem perde o voo no ar continua com altura por
	/// alguns quadros enquanto CAI. Separar os dois e o que permite a queda existir -- se altura
	/// zerasse junto com o voo, quem ficasse sem Ki a 20 tiles apareceria no chao no mesmo quadro.
	/// </summary>
	public float Altitude;

	/// <summary>O que o input diz agora: segurando espaco / segurando control.</summary>
	public bool QuerSubir, QuerDescer;

	/// <summary>Rate-limit do aviso de "nao da pra romper a atmosfera" -- senao ele sai 30x por segundo.</summary>
	public long AvisoDeTetoAte;

	public int TiquesDeVoo;
	public Vec2 RumoDoVoo;
	public double ForcaDoVoo;

	/// <summary>
	/// COM QUANTOS TIQUES O ARREMESSO COMECOU -- e so isso que diz a distancia TOTAL.
	///
	/// O rastro de terra do DU so aparece em arremesso de 8 tiles ou mais (`death.dm:218`), e essa
	/// e uma pergunta sobre o voo INTEIRO: no meio dele, `TiquesDeVoo` ja e o que FALTA, e um voo
	/// curto no comeco e indistinguivel de um voo longo no fim.
	/// </summary>
	public int TiquesIniciaisDoVoo;

	/// <summary>O corpo ja passou por aqui neste arremesso? Evita dois sulcos na mesma celula.</summary>
	public Vec2 UltimoSulco;

	/// <summary>
	/// QUANTO DO TIQUE DO ORIGINAL ja foi andado, de 0 a 1.
	///
	/// O voo do DM anda em passos de 0,1 s valendo dois tiles cada. Andar isso de uma vez fazia o
	/// corpo ficar 100 ms parado e depois pular 64 px -- ver `TickDoEmpurrao`. O passo agora e
	/// fatiado pelo tique do servidor, e esta fracao e o que sobra entre um tique do DM e o outro.
	/// </summary>
	public double VooNoTique;

	/// <summary>
	/// QUANTOS SEGUNDOS AINDA VALE "UM FEIXE ESTA ME CARREGANDO" -- e ele e um PRAZO, nao um bit.
	///
	/// ============================ POR QUE PRAZO, E NAO "QUEM ME ARRASTA" ============================
	/// A escolha obvia era guardar o id do feixe. So que quem apaga um bit desses e sempre alguem que
	/// tem que LEMBRAR de apagar -- e um feixe morre por seis caminhos (alcance, `Burnout`, parede,
	/// deflexao, o dono soltar, o dono sair do planeta), mais o proprio corpo trocando de zona ou
	/// deslogando. Um so desses esquecido deixa um jogador congelado pra sempre, que e a pior falha
	/// que este sistema pode ter.
	///
	/// Com prazo nao ha nada pra apagar: o feixe REGA o campo a cada tique em que de fato empurra
	/// (<c>ArrastarComOFeixe</c>), e o <c>TickDoEmpurrao</c> o escorre. Parou de regar por qualquer
	/// motivo -- inclusive nenhum -- e o corpo se solta sozinho em um decimo de segundo. E a mesma
	/// disciplina do `CenaSegundos` da cinematica e da imunidade derivada do `CombatState`.
	///
	/// O PRAZO E UM TIQUE DO DM (`Empurrao.SegundosPorTique`, 0,1 s = tres tiques do servidor) de
	/// proposito: curto o bastante pra a soltura ser imperceptivel, longo o bastante pra um tique
	/// perdido nao piscar o corpo de volta pro controle do dono no meio do arrasto.
	/// ==============================================================================================
	///
	/// **SO O `GameServer.Projeteis.cs` ESCREVE AQUI** (e so o `TickDoEmpurrao` desconta).
	/// </summary>
	public double ArrastoRestante;

	/// <summary>
	/// QUANTOS SEGUNDOS AINDA VALE "ESTOU SENDO PUXADO PRA UMA FUSAO" -- o `step_to` do
	/// `Potara_Fusion.dm:124-129`, e ele e um PRAZO pela mesma razao inteira do
	/// <see cref="ArrastoRestante"/> logo acima: quem apaga um bit e alguem que tem que LEMBRAR de
	/// apagar, e o puxao morre por seis caminhos (encostaram, empacaram, nocaute, morte, troca de zona,
	/// logout). Com prazo nao ha nada a apagar -- o `TickDoPuxaoDeFusao` REGA o campo a cada tique em
	/// que de fato empurra, e o `TickDoEmpurrao` o escorre.
	///
	/// **SO O `GameServer.Fusao.cs` ESCREVE AQUI** (e so o `TickDoEmpurrao` desconta).
	/// </summary>
	public double PuxaoDeFusaoRestante;

	/// <summary>
	/// O SERVIDOR ESTA DIRIGINDO ESTE CORPO? -- as TRES maneiras, num nome so.
	///
	/// Arremessado (<see cref="TiquesDeVoo"/>), levado por um feixe (<see cref="ArrastoRestante"/>) ou
	/// puxado pra uma fusao (<see cref="PuxaoDeFusaoRestante"/>). Sao tres calculos de deslocamento
	/// diferentes e EXCLUSIVOS, mas pra o resto do jogo -- o bit que manda o cliente parar de integrar
	/// tecla, o funil de vetor, a correcao de posicao -- eles sao a mesma frase, e ter tres perguntas
	/// pra ela seria a porta de entrada do "depende de qual tique acordou primeiro".
	/// </summary>
	public bool DirigidoPeloServidor =>
		TiquesDeVoo > 0 || ArrastoRestante > 0 || PuxaoDeFusaoRestante > 0;

	// ============================== AGARRAO (ver GameServer.Agarrao.cs) ==============================

	/// <summary>
	/// QUEM EU SEGURO (0 = ninguem) -- o `grabbee` do original (`Grabbing.dm`).
	///
	/// Estado VIVO: ninguem continua segurando alguem deslogado, e o par se desfaz sozinho quando um
	/// dos dois some do mundo (a varredura orfa do `TickDoAgarrao`).
	/// </summary>
	public int AgarrandoId;

	/// <summary>
	/// QUEM ME SEGURA (0 = ninguem) -- o `grabber` de la, e a fonte do `grabParalysis`.
	///
	/// **E um id e nao um bool de proposito**: e ele que permite a varredura orfa perguntar "o corpo
	/// que me segurava ainda existe?" -- a rede que impede alguem de ficar preso pra sempre por um
	/// campo que sobrou.
	/// </summary>
	public int AgarradoPorId;

	/// <summary>Em que pe esta o agarrao que EU dou. O `grabMode` -- ver <see cref="ModoDeAgarrao"/>.</summary>
	public ModoDeAgarrao ModoDoAgarrao;

	/// <summary>
	/// O `grabberSTR`: a forca de quem me segura, recalculada a cada tique do DM (`Grabbing.dm:188`).
	/// Guardada em MIM e nao nele porque e sobre ESTE aperto -- carregar multiplica por 1,5.
	/// </summary>
	public double ForcaDeQuemMeSegura;

	/// <summary>
	/// O `grabCounter`: o quanto eu ja juntei me debatendo. Ele tem dois papeis opostos e os dois sao
	/// do original -- e a barra que me solta (`>= 20/escapechance`) **e** uma soma no dano do
	/// estrangulamento (`movement handler.dm:194`). Quem se debate sai mais rapido e se machuca mais.
	/// </summary>
	public double ContadorDaLuta;

	/// <summary>O `is_choking` (`attack_bck.dm:43-57`): estou apertando o pescoco de quem seguro?</summary>
	public bool Estrangulando;

	/// <summary>
	/// O `choke_cooldown` (`attack_bck.dm:47-48`, `spawn(5)` = 0,5 s) -- quando a tecla de golpe pode
	/// voltar a alternar o afogamento.
	///
	/// O comentario do proprio autor diz por que existe: *"because sometimes this goes by very
	/// fast"*. Sem ele, segurar a tecla de socar liga e desliga o aperto tres vezes por segundo, e o
	/// estado vira ruido -- ninguem consegue saber se esta estrangulando ou nao.
	/// </summary>
	public long AfogamentoLivreEm;

	/// <summary>
	/// EU PEDI PRA ANDAR NESTE PACOTE DE INPUT, estando preso -- o `curdir` do bloco de escape.
	///
	/// Ele existe porque as duas metades da mesma frase moram em cadencias diferentes: a INTENCAO
	/// chega no pacote de input (dezenas por segundo, e recusada pelo `PodeMexerOCorpo` antes de
	/// virar passo), e a LUTA e resolvida no tique do DM (10 por segundo). Sem este bit o servidor
	/// nao teria como saber que o preso estava se debatendo -- a unica prova disso e um passo que
	/// nunca acontece. Ver `GameServer.Input` e `LutaPraEscapar`.
	/// </summary>
	public bool DebatendoSe;

	// ============================== NADO (ver GameServer.Nado.cs) ==============================

	/// <summary>
	/// O `swim` do original (`Swim.dm:5-23`). Estado VIVO -- ninguem continua nadando deslogado.
	///
	/// ELE MUDA **COMO ESTE CORPO ATRAVESSA O MUNDO** e nao so o desenho: e a entrada `nadando` do
	/// <see cref="Jandirus.Core.World.ClasseDeAgua.ModoDe"/>, e e por ela que a agua deixa de parar
	/// este corpo. Por isso viaja na FICHA (`SheetState.Estado2`, bit 0) e nao num canal de evento:
	/// o cliente preve o passo pela MESMA regra que o servidor confere, e um estado que se perde num
	/// pacote viraria o cliente prevendo por uma regra que o servidor ja nao aceita.
	///
	/// SO O `GameServer.Nado.cs` ESCREVE AQUI (mais o `AlternarVoo`, pela porta `PararDeNadar`).
	/// </summary>
	public bool Nadando;

	/// <summary>
	/// PRA ONDE ELE OLHAVA NO ULTIMO TIQUE DE NADO -- o gatilho do `Swim_Gain()` (`Movement.dm:1-4`),
	/// que no DM so paga quem MUDA de direcao nadando.
	/// </summary>
	public Facing UltimaDirDoNado;

	/// <summary>
	/// SEGUNDOS DE VOO AINDA NAO PAGOS pelo `Flight_Gain()` -- o acumulador que traz o tique cheio
	/// (30 Hz) de volta pra cadencia do original.
	///
	/// ============================ POR QUE ELE PRECISA EXISTIR ============================
	/// No DM o `Flight_Gain()` e chamado de dentro do `mob/proc/Stats()` (`Stats.dm:414`), que dorme
	/// `sleep_tiem = 2` -- 5 Hz. Aqui o voo mora no `TickDoVoo`, que roda no tique CHEIO (30 Hz)
	/// porque forma, carga, voo e nado mexem no MESMO Ki e precisam da mesma cadencia.
	///
	/// Chamar o `FlightGain()` direto de la pagaria SEIS vezes por tique do original -- 403 BP/h
	/// em vez dos 67 BP/h que o DM rende. Este campo junta o `dt` e libera um pagamento a cada
	/// 0,2 s, que e exatamente o `sleep_tiem` de la. Acumular o `dt` (em vez de contar tiques)
	/// mantem a taxa certa mesmo se o servidor engasgar e o quadro vier mais longo.
	/// ====================================================================================
	/// </summary>
	public float SegundosDeVooSemGanho;

	/// <summary>
	/// ESTE NADO JA MOLHOU O CORPO? -- e o que separa "ainda entrando" de "saiu no seco".
	///
	/// ============================ POR QUE ELE PRECISA EXISTIR ============================
	/// O verb aceita quem tem agua A FRENTE (`PodeComecarANadar`), e o tique desliga quem nao esta EM
	/// CIMA dela -- sozinhos, os dois se matam no mesmo decimo de segundo e ninguem consegue comecar
	/// a nadar da margem. No DM eles convivem porque o verb DA O PASSO pra dentro da agua
	/// (`step(usr, usr.dir)`, `Swim.dm:15`); aqui o corpo entra ANDANDO, pelo movimento do proprio
	/// jogador, e andar leva alguns decimos.
	///
	/// Entao o desligamento por chao seco passa a ter duas leituras, e este bit e quem as separa:
	///   * FALSO -- ele ainda nao encostou na agua: e a janela de ENTRADA, e ela vale pelo prazo do
	///     <see cref="Jandirus.Core.World.Nado.PrazoParaEntrar"/>, sem custo de Ki e sem maestria;
	///   * VERDADE -- ele ja nadou: vale a regra do original, sem carencia nenhuma. Chao seco DESLIGA
	///     no mesmo tique (`movement handler.dm:283-287`).
	///
	/// E POR ISSO QUE A CARENCIA NAO REARMA: ela morre no primeiro pixel de agua e nao volta. Sem
	/// isso, entrar e sair da agua de proposito renovaria o prazo pra sempre -- que e exatamente o
	/// defeito oposto ao que se estava consertando (nadar em terra firme).
	/// </summary>
	public bool NadoJaMolhou;

	/// <summary>Quantos segundos ele ja passou nadando SEM ter encostado na agua. Ver acima.</summary>
	public float NadoSecoS;

	/// <summary>A aura acesa por excesso de Ki. Guardada pra ter HISTERESE -- ver CargaDeKi.AuraAcesa.</summary>
	public bool AuraDeCarga;

	/// <summary>
	/// A aura do POWER-UP (desenho + zumbido). Separada de <see cref="Carregando"/> porque no
	/// original ela pede `canPower && stamina > 1` -- quem so tem Ki Unlocked carrega em silencio.
	/// </summary>
	public bool AuraDaCarga;

	/// <summary>
	/// A ULTIMA FICHA QUE FOI ENVIADA. E contra ELA que se decide reenviar.
	///
	/// ============================ POR QUE NAO DA PRA COMPARAR NO LUGAR ============================
	/// O `TickFichas` roda a 5 Hz e comparava o valor de ANTES com o de DEPOIS **dentro da propria
	/// chamada**. So que quase tudo que mexe no Ki roda FORA dela, a 30 Hz: a carga da tecla C, a
	/// regeneracao, o custo de correr. Quando o `TickFichas` acordava, o Ki ja tinha subido -- os
	/// dois lados da comparacao ja eram o valor NOVO, davam iguais, e o pacote nao saia.
	///
	/// O sintoma que o dono descreveu e exatamente isso: entrar no servidor com o tanque cheio e
	/// segurar C nao mexia a barra. So depois de GASTAR um pouco e que voltava a funcionar -- porque
	/// ai o `kiratio` passava a mudar o `expressedBP`, que E recalculado dentro do `Tick`, e esse
	/// sim aparecia na comparacao.
	/// ==========================================================================================
	/// </summary>
	public double EnvBP = double.NaN, EnvKi = double.NaN, EnvHp = double.NaN, EnvAct = double.NaN,
				  EnvVigor = double.NaN, EnvNutricao = double.NaN;

	/// <summary>
	/// O BP BASE E O TETO DE KI, que TAMBEM vao no pacote e nao estavam sendo comparados.
	///
	/// E o buraco que o comentario do `TickFichas` ja descreve com todas as letras -- "a lista de
	/// campos comparados era menor que a lista de campos ENVIADOS" -- e ele mordia dois campos que
	/// as leituras novas usam: o multiplicador total e `expressedBP / BP`, e o fim do trilho da
	/// barra de Ki e o `powerupcap`. Os dois mudam por caminhos que nao mexem no `expressedBP`
	/// (aprender uma skill de power-up sobe o teto sem mexer no poder), e sem estar aqui a leitura
	/// so chegaria de carona no proximo soco.
	///
	/// A INTEIREZA NAO PRECISA DE CAMPO PROPRIO: ela e `expressedBP / peakexBP`, e o pico deriva do
	/// proprio poder expresso -- nao existe jeito de ela mexer sem o `EnvBP` mexer junto.
	///
	/// (Restaurado em 2026-08-14: um `git checkout` numa arvore suja apagou estes dois campos, e a
	/// regra que os criou sobreviveu escrita no `EnvEstado2` logo abaixo enquanto os campos dela
	/// sumiam. Nao apague sem ler os dois comentarios juntos.)
	/// </summary>
	public double EnvBpBase = double.NaN, EnvTetoKi = double.NaN;

	/// <summary>
	/// OS TRES QUE FALTAVAM NA COMPARACAO: iam no pacote (`SheetState.Write`) e nao eram comparados, entao
	/// so chegavam de carona quando outro campo mudava. Foi o "as vezes" do relato do Ki maximo
	/// (2026-09-04): o `MaxKi` caia a cada soco, mas a aba so via a queda quando o Ki ou o BP mexiam.
	/// </summary>
	public double EnvMaxKi = double.NaN;
	public float EnvSpeed = float.NaN;
	public int EnvMembros = -1;

	/// <summary>O ultimo byte de estado enviado (KO, morto, guarda, letal, rabo).</summary>
	public int EnvEstado = -1;

	/// <summary>
	/// ...e o SEGUNDO byte (o nado). Separado, e nao espremido no de cima, pela regra que o proprio
	/// `TickFichas` escreve: **todo campo que vai no pacote precisa estar na comparacao**, senao ele
	/// so chega de carona quando outro muda. O nado ate se anuncia na hora (`AnunciarNado` chama
	/// `MandarFicha`), mas um estado que depende de o seu unico chamador lembrar de avisar e o
	/// mesmo buraco que ja deixou a barra de Ki e o golpe letal parados na tela.
	/// </summary>
	public int EnvEstado2 = -1;

	/// <summary>Ultimo estagio de carga anunciado. So se avisa o jogador quando o estagio MUDA.</summary>
	public Jandirus.Core.Combat.EstagioDaCarga EstagioDaCarga;

	/// <summary>
	/// EM QUEM EU BATI POR ULTIMO, e quando.
	///
	/// Existe pro ZANZO CLASH: o embate comeca quando os dois se acertam no MESMO instante, e sem
	/// este par nao ha como saber que o soco que esta chegando e RESPOSTA a um que acabou de sair.
	/// </summary>
	public int UltimoAlvo;
	public long UltimoSocoMs;

	/// <summary>Bits de <see cref="Protocol.Poder"/> que as skills aprendidas acenderam.</summary>
	public Protocol.Poder Poderes;

	/// <summary>
	/// A ultima mascara de FERIDAS enviada. Mesma disciplina dos `Env*`: o pacote so sai quando o
	/// corpo muda de cara, e nao a cada tique. Ver `GameServer.Feridas.cs`.
	/// </summary>
	public Jandirus.Core.Combat.MascaraDeFeridas EnvFeridas;

	/// <summary>
	/// OS BITS QUE NAO VEM DE SKILL -- hoje so o <see cref="Protocol.Poder.Admin"/>.
	///
	/// ============================ POR QUE ELE PRECISA EXISTIR ============================
	/// `Poderes` e RECALCULADO do zero toda vez que a lista de skills muda (`AplicarPoderes` faz
	/// `pl.Poderes = p`, varrendo as aprendidas). O login marcava o host com
	/// `pl.Poderes |= Admin` e, doze linhas abaixo, chamava `AplicarPoderes(pl)` -- que apagava a
	/// marca. O host entrava admin e deixava de ser admin no mesmo instante, e a aba nunca
	/// aparecia. Aprender uma skill qualquer faria a mesma coisa com quem fosse promovido.
	///
	/// Separar em dois campos e o conserto de raiz: o que e DERIVADO de skill se recalcula a
	/// vontade, e o que foi CONCEDIDO (pelo host, pela conta) sobrevive a qualquer recalculo.
	/// =====================================================================================
	/// </summary>
	public Protocol.Poder PoderesConcedidos;

	/// <summary>
	/// ============================ O PROXIMO PASSO DA MORTE (relogio real, ms) ============================
	/// Ele se chamava `RenasceEm` e queria dizer uma coisa so ("quando este corpo volta a vida").
	/// Agora a morte tem DUAS etapas -- o corpo caido no chao e a estadia no Outro Mundo (ver
	/// <see cref="Jandirus.Core.World.Alem"/>) --, e o mesmo campo marca o vencimento das duas. Qual
	/// delas esta correndo nao e um segundo campo: e o LUGAR onde o corpo esta (`Alem.EhOAlem`), pelo
	/// mesmo argumento que o `Alem.MortoDePe` escreve.
	///
	/// **SO E LIDO COM `Ficha.dead` LIGADO.** Um valor velho aqui num corpo vivo nao significa nada,
	/// e por isso nem todo caminho de revive precisa lembrar de zera-lo.
	///
	/// QUEM ESCREVE: o gancho `CombatState.AoMorrer` (ver `GameServer.Alem.AMorteAconteceu`) e mais
	/// ninguem. Antes eram nove atribuicoes a mao espalhadas por oito arquivos.
	/// ==================================================================================================
	/// </summary>
	public long RelogioDaMorte;

	/// <summary>
	/// ============================ ESTA MORTE JA PASSOU PELA VIAGEM? ============================
	/// A informacao que faltava pra <see cref="Jandirus.Core.World.Alem.TemAureola"/> -- ver la o
	/// argumento inteiro, com o `Death.dm:64-67` x `:106-108`. Em uma frase: o cadaver dos 2 s **e**
	/// o proprio corpo neste port, e ele nao pode ter aureola; o corpo que ja subiu tem, esteja onde
	/// estiver.
	///
	/// **SO E LIDO COM `Ficha.dead` LIGADO**, exatamente como o <see cref="RelogioDaMorte"/> logo
	/// acima -- e e essa disciplina que faz a aureola continuar sumindo sozinha em todo caminho de
	/// revive: ela e `dead && ESTE campo`, e nao um bit paralelo que alguem precise apagar.
	///
	/// QUEM ESCREVE, e sao os mesmos que ja escreviam o relogio (nao ha um quarto):
	///   * `AMorteAconteceu` -- FALSO. O funil unico da morte; e ele que garante que a morte seguinte
	///     nao herde o valor da anterior, sem que revive nenhum precise lembrar.
	///   * `IrProAlem` -- VERDADEIRO, uma linha antes do `MoveToZone` (a ordem importa: e o
	///     `TrocarAureolas` de dentro dele que apresenta a aureola a zona de destino).
	///   * `PrepararCombate`, pra quem LOGA morto -- pelo lugar onde acordou, no mesmo palpite que ja
	///     escolhe qual prazo rearmar. Um palpite so alimentando as duas coisas.
	///
	/// VIVO, nao vai pro disco: a etapa do percurso e do runtime, e quem loga morto e reposto pelo
	/// terceiro item acima.
	/// ==========================================================================================
	/// </summary>
	public bool MorteJaViajou;

	/// <summary>
	/// ============================ O KARMA DESTA MORTE JA FOI COBRADO? ============================
	/// O `tmp/pk_karma_taken` do original (`SkyNPCs.dm:104`), e o comentario de la diz o motivo em uma
	/// linha: *"killer_stuff pode rodar 2x na mesma morte -> nao conta karma 2x"*.
	///
	/// **A MESMA ARMADILHA EXISTE AQUI, POR OUTRO CAMINHO**: `AoPerderALuta(morreu: true)` tem dois
	/// chamadores -- o golpe (`GameServer.Combat.cs:500`) e a absorcao
	/// (`GameServer.Absorcao.cs:111`) --, e sem esta trava um Majin que absorve quem acabou de matar
	/// pagaria (ou receberia) 40 pontos por uma morte so.
	///
	/// **A BANDEIRA E DA VITIMA, E NAO DO ALGOZ**, como no DM (`victim.pk_karma_taken`). A diferenca
	/// importa: a pergunta e "esta MORTE ja foi contada?", e uma morte tem uma vitima e pode ter dois
	/// caminhos de algoz.
	///
	/// QUEM ESCREVE: `AMorteAconteceu` (FALSO -- o rearme, no funil unico por onde toda morte passa,
	/// exatamente como o <see cref="MorteJaViajou"/> logo acima) e `KarmaPorMatarJogador`
	/// (VERDADEIRO). Nao vai pro disco pelo mesmo motivo do vizinho: a morte que ele descreve nao
	/// atravessa reinicio.
	/// ========================================================================================
	/// </summary>
	public bool KarmaDaMorteContado;

	/// <summary>
	/// A ultima AUREOLA enviada pra zona. Mesma disciplina dos `Env*` e do `EnvFeridas`: o pacote so
	/// sai quando muda. Ver `GameServer.Alem.TickDasAureolas`.
	/// </summary>
	public bool EnvAureola;

	/// <summary>
	/// DEBRUCADO NUMA RESEARCH STATION. Nao entra no <see cref="Protocol.Activity"/> junto de
	/// treinar e meditar de proposito: aqueles dois sao POSES que os outros veem, e a pose vai no
	/// snapshot pra todo mundo. Estudar so interessa a quem estuda.
	/// </summary>
	public bool Estudando;

	/// <summary>Esta correndo AGORA (concedido pelo servidor, nao afirmado pelo cliente).</summary>
	public bool Correndo;

	/// <summary>Quando o proximo dash de aproximacao libera (relogio real, ms).</summary>
	public long DashLivreEm;

	/// <summary>Quando a proxima piscada do Zanzoken libera (relogio real, ms).</summary>
	public long ZanzoLivreEm;

	/// <summary>
	/// Ate quando uma correcao de movimento e ESPERADA (acabei de dar dash neste jogador).
	///
	/// O dash reposiciona o personagem no servidor, mas o cliente ja tinha pacotes em voo
	/// com a posicao antiga -- eles chegam e sao corrigidos, o que e certo. O que NAO pode e
	/// isso poluir o contador de correcoes: ele existe pra denunciar cheat e lag cronico, e
	/// um sinal que dispara sozinho toda vez que o jogo funciona nao denuncia nada.
	/// </summary>
	public long CorrecaoEsperadaAte;

	/// <summary>
	/// A ULTIMA SEQUENCIA DE INPUT recebida, e a que valia quando o servidor TELEPORTOU este
	/// corpo pela ultima vez.
	///
	/// ============================ O QUE ISTO CONSERTA ============================
	/// O dash move o personagem no servidor e manda uma correcao. Mas o cliente ja tinha pacotes
	/// EM VOO, montados antes de saber do teleporte, afirmando a posicao antiga. Sem carimbo, o
	/// servidor nao tinha como distinguir "pacote velho" de "cliente errado": ele validava o
	/// pacote velho, o clamp `from + delta.Normalized()*allowed` apontava PRA TRAS (do destino de
	/// volta pra origem) e virava um passo pra tras na velocidade maxima -- GRAVADO no estado
	/// autoritativo. Um por pacote em voo, ou seja um RTT inteiro de trancos.
	///
	/// Com o carimbo a regra fica trivial: input com sequencia <= a do teleporte foi montado ANTES
	/// dele e nao tem opiniao valida sobre onde o corpo esta. Descarta-se, sem clamp e sem gravar.
	/// ============================================================================
	/// </summary>
	public uint SeqInput, SeqDoTeleporte;

	/// <summary>
	/// Credito de deslocamento acumulado, em pixels. Ver <see cref="MoveRules.PassosDeFolga"/> --
	/// e o que absorve o jitter entre o dt que o cliente SIMULOU e o que o servidor MEDIU.
	/// </summary>
	public float OrcamentoPx;

	/// <summary>
	/// EM QUEM EU ESTOU MIRANDO (0 = ninguem). Marcado com duplo clique pelo jogador.
	///
	/// Um alvo marcado passa NA FRENTE do cone: e ele que leva o soco e e nele que a investida
	/// fecha, mesmo com outro mais perto e mesmo estando atras. Sem isso, brigar em grupo vira
	/// loteria -- o golpe vai em quem por acaso encostou primeiro.
	/// </summary>
	public int AlvoId;

	/// <summary>Quem me derrubou por ultimo -- e de quem o Zenkai cobra a conta.</summary>
	public int UltimoAgressor;

	/// <summary>
	/// A PRESA QUE ESTE NPC JA ENGAJOU (0 = nenhuma). **So o NPC hostil escreve e le isto** -- ver
	/// <see cref="GameServer.PresaDoHostil"/>.
	///
	/// Ela existe por uma razao so: o alvo do DM e um CAMPO (`target`), e um campo tem histerese --
	/// ele e adotado num raio (`MAX_AGGRO_RANGE`, 20 tiles) e largado noutro, bem maior
	/// (`aggro_dist*2`, 60). A escolha por varredura pura, que este port fazia, nao tem como ter dois
	/// raios: ela responde a mesma pergunta toda vez, entao o raio de adotar e o de largar seriam o
	/// mesmo numero -- e ai ou o chefe caca o mapa inteiro (o que ele fazia) ou o jogador se solta
	/// dele andando um passo alem do limite, que e pior ainda.
	/// </summary>
	public int PresaEngajada;

	/// <summary>Assinatura do ultimo corpo enviado -- so remanda quando algum membro muda.</summary>
	public string CorpoEnviado = "";

	/// <summary>Aparencia: so o servidor guarda a versao saneada.</summary>
	public Jandirus.Core.Appearance.Appearance Visual = new();
	public string Planeta = "Earth", Genero = "Male", Linhagem = "";
	public int Idade = 18;
	public long CriadoEm;

	/// <summary>
	/// O BERCO DESTE CORPO, calculado UMA VEZ no `Entrar` e lido por todo mundo que precisa mandar
	/// alguem "pro comeco" -- morte, verb `spawn`, admin, zona que sumiu.
	///
	/// Calculado no login e nao a cada uso porque ele nao muda enquanto o personagem existir: a
	/// semente e do disco e a raca/classe/linhagem tambem. Recalcular a cada morte custaria o exilio
	/// inteiro (ate 96 celulas) dentro do tique, por nada.
	///
	/// **Vazio (`Planeta` nulo) e o corpo SEM DONO** -- clone da mente, NPC, corpo de bancada. Eles
	/// nao tem save, entao nao tem berco, e quem os manda pro comeco cai no `SpawnZone` de sempre.
	/// </summary>
	public Jandirus.Core.Races.Berco Berco;

	/// <summary>A semente do berco. Do disco, ou derivada -- ver `AccountStore.ParaJogador`.</summary>
	public ulong SeedDoBerco;

	/// <summary>O pedido de "nascer perto de casa" que este personagem fez na criacao.</summary>
	public bool PertoDeCasa;

	/// <summary>
	/// A TELA DO REFUGIO JA FOI EMPURRADA NESTA SESSAO?
	///
	/// **De sessao e nao do disco, de proposito.** O que ela anuncia e uma catastrofe -- o planeta
	/// natal deixou de existir --, e nao uma morte: empurrar a tela a cada morte transformaria a
	/// informacao em barulho, e barulho e o jeito mais rapido de ela deixar de ser lida. Uma vez por
	/// login basta, e um campo novo no save de todo personagem seria caro demais pra isso.
	///
	/// Depois da primeira, a porta continua aberta pelo botao do menu (aba Nav) -- ver
	/// `GameServer.Refugio.OferecerORefugio`.
	/// </summary>
	public bool RefugioJaOferecido;

	/// <summary>O que este personagem carrega. Ver `Core.Items.Inventario`.</summary>
	public Jandirus.Core.Items.Inventario Mochila = new();

	/// <summary>Assinatura do ultimo inventario enviado -- mesma disciplina dos outros `Env*`.</summary>
	public string SigMochila = "";

	/// <summary>A historia que o jogador escreveu na criacao. Sem efeito mecanico -- ver o verb.</summary>
	public string Historia = "";

	/// <summary>O porte do corpo (Small/Medium/Large). Mexe em stat, e e permanente.</summary>
	public string Porte = "Medium";

	/// <summary>
	/// O NOME DA FUSAO QUE ESTE CORPO ESTA VESTINDO agora (vazio = nenhuma).
	///
	/// ============================ POR QUE ELE NAO ESCREVE NO `Name` ============================
	/// O DM faz `Keeper.name = FuseName` (`Fusion.dm:185`) e guarda o antigo pra devolver. **Aqui
	/// isso seria destrutivo.** O `Name` deste port nao e so um rotulo: o save grava `Nome = pl.Name`
	/// (`CharacterStore.cs:665`) e o salvamento periodico roda a cada 2 minutos -- entao uma fusao de
	/// 15 minutos gravaria "Goeta" como o nome do personagem. E pior: a COR DA AURA e a COR DO KI sao
	/// derivadas do nome no save (`CharacterStore.cs:806` e `:812`), e a semente do berco tambem
	/// (`:826`). Renomear o corpo trocaria a aura, o tiro e o lugar de nascimento do jogador -- pra
	/// sempre, e calado.
	///
	/// Entao o nome fundido e um campo A PARTE, VIVO, e quem o mostra e o <see cref="NomeVisivel"/>.
	/// O nome de verdade nunca e tocado, e a fusao nao tem como vazar pro disco.
	/// ======================================================================================
	/// </summary>
	public string NomeDeFusao = "";

	/// <summary>
	/// A APARENCIA QUE ESTE CORPO ESTA VESTINDO POR ESTAR FUNDIDO (nula = nenhuma).
	///
	/// ============================ IRMAO DO <see cref="NomeDeFusao"/>, E PELO MESMO MOTIVO EXATO ============================
	/// O bloco de cima ja fez este raciocinio inteiro pro nome, e ele vale letra por letra pra roupa e
	/// pro cabelo: o save grava `Visual = pl.Visual` (`CharacterStore.cs:674`) e o **salvamento periodico
	/// roda a cada 2 minutos** (`GameServer.cs`, `_tickCount % TicksPorSave`). Uma Metamoro dura 15
	/// minutos e uma Potara 30 -- ou seja qualquer fusao atravessa sete a quinze gravacoes.
	///
	/// Escrever a fusao em `pl.Visual` gravaria no disco, pra sempre: o guarda-roupa do jogador
	/// SUBSTITUIDO pelo colete metamoriano (a Danca nao herda roupa nenhuma -- ver
	/// `Fusao.RoupaDaFusao`) e o penteado dele trocado pelo do Vegito. Uma queda do servidor no meio de
	/// uma fusao apagaria a roupa que a pessoa escolheu na criacao, e nada no jogo diria por que.
	///
	/// **E HA UM SEGUNDO MOTIVO, QUE O NOME NAO TEM:** o `VisualCatalog.Sanear` recusa peca fora do
	/// catalogo, e o brinco `potara` esta fora dele DE PROPOSITO (ver `Fusao.PecaDosBrincosPotara`).
	/// Uma aparencia de fusao que passasse pelo saneamento -- e `pl.Visual` passa, no login e a cada
	/// troca de guarda-roupa -- perderia o brinco em silencio.
	///
	/// Entao a aparencia fundida e um campo A PARTE, VIVO, e quem a mostra e o
	/// <see cref="GameServer.VisualVisivel"/>. A aparencia de verdade nunca e tocada.
	/// ================================================================================================================
	/// </summary>
	public Jandirus.Core.Appearance.Appearance? LookDeFusao;

	/// <summary>
	/// O DISFARCE DA IMITACAO (lote G12) -- irmao do <see cref="LookDeFusao"/>, pelo mesmo motivo: o que
	/// o mundo ve sai do funil (`NomeVisivel`/`VisualVisivel`), e a ficha que vai pro disco nao e tocada.
	/// A fusao VENCE o disfarce quando os dois existem. Ver `DisfarceG12`.
	/// </summary>
	public DisfarceG12? Disfarce;

	/// <summary>A conta a que este personagem pertence, e em qual dos tres slots ele mora.</summary>
	public string Conta = "";
	public int Slot = -1;

	/// <summary>
	/// ============================ O PERSONAGEM DESTE CORPO FOI APAGADO PARA SEMPRE ============================
	/// **Um so escritor no jogo inteiro**: `GameServer.ApagarOPersonagemParaSempre`, o gesto da fusao
	/// Namekuseijin (a regra N3 do dono -- *"o outro namek se for jogador, perde o personagem pra
	/// sempre"*). E **um so leitor**: o <see cref="GameServer.Persistir"/>.
	///
	/// ============================ ELE EXISTE CONTRA O SALVAMENTO PERIODICO ============================
	/// O `Persistir` roda a cada 2 minutos e roda outra vez no `Drop`. Entre o instante em que o save do
	/// absorvido e apagado e o instante em que o `Disconnect()` de fato tira o corpo do mundo passa pelo
	/// menos um `PollEvents` -- e qualquer `Persistir` nessa janela **recriaria o personagem do nada**,
	/// montado do `ServerPlayer` que ainda esta de pe. O jogador veria o personagem "voltar", e o log
	/// diria que ele foi apagado.
	///
	/// Ou seja: nao e zelo, e a outra metade do apagamento. O arquivo morre e a porta que o reescreve
	/// fecha, **no mesmo gesto** -- ver a ordem escrita em `ApagarOPersonagemParaSempre`.
	///
	/// **NAO VAI PRO DISCO**, e nao faz sentido que va: ele descreve um corpo que esta caindo do mundo
	/// agora, e o save que ele protege ja nao existe.
	/// ======================================================================================================
	/// </summary>
	public bool PersonagemConsumido;

	/// <summary>
	/// A ASSINATURA -- a identidade PERMANENTE deste personagem. E o `mob/var/signature` do DM, e e
	/// a chave de tudo que e social (conhecidos, amizade, inimizade, rivais).
	///
	/// ============================ DERIVADA, E NAO UM CAMPO NOVO ============================
	/// Conta + slot ja E a identidade de um personagem neste projeto: e por esse par que o save o
	/// acha no disco, e ele nao muda enquanto o personagem existir. Um campo `Assinatura` gravado
	/// seria uma segunda verdade sobre a mesma coisa -- e a pergunta "e se os dois divergirem?" nao
	/// tem resposta boa. O `Id` de rede NAO serve: ele e da sessao, e a amizade de ontem apontaria
	/// pra quem entrou hoje naquele numero.
	///
	/// VAZIA PARA QUEM NAO TEM CONTA -- os corpos sem dono (clone da meditacao, NPCs, os corpos
	/// forjados das bancadas). E vazia e a resposta certa: eles nao entram em lista social nenhuma,
	/// e todo metodo daqui e do <see cref="Jandirus.Core.Social.Convivio"/> recusa a assinatura
	/// vazia na primeira linha. E o mesmo `if(!signature) return` que o DM escreve em
	/// `accrue_friendship` e `add_enmity`.
	/// =======================================================================================
	///
	/// ============================ E ELA E OPACA, PORQUE ELA APARECE NA TELA ============================
	/// A forma obvia seria `"conta#slot"`. Ela esta ERRADA por um motivo que so aparece na ponta: a
	/// assinatura VIAJA pro cliente (`S2C.Conhecidos`) e o jogo a MOSTRA -- o `HtmlUI.dm:374` do
	/// original escreve `??? ([signature])` pra quem voce nao conhece. Com `conta#slot`, cada
	/// desconhecido na sua aba entregaria o LOGIN de quem esta do outro lado.
	///
	/// No DM ela e um numero de 10 digitos sorteado uma vez na criacao (`CreationUI.dm:199-203`).
	/// Aqui e um HASH de conta+slot no mesmo formato -- opaco igual, e com uma vantagem sobre o
	/// sorteio: nao precisa de campo no save nem de conferencia de colisao na criacao, porque ele se
	/// recalcula sempre igual a partir de quem o personagem ja e.
	/// ====================================================================================================
	/// </summary>
	public string Assinatura => Conta.Length == 0 || Slot < 0 ? "" : AssinaturaDe(Conta, Slot);

	/// <summary>
	/// O hash da assinatura: FNV-1a de 64 bits sobre conta+slot, cortado nos 10 digitos do formato
	/// do DM.
	///
	/// MINUSCULAS de proposito: o nome da conta que o jogador digita no login pode vir com outra
	/// caixa, e a pasta de saves ja normaliza assim (`AccountStore.Arquivo`). Sem isto, entrar como
	/// "Joao" depois de ter entrado como "joao" trocaria a identidade do personagem e apagaria, na
	/// pratica, todas as amizades dele.
	/// </summary>
	internal static string AssinaturaDe(string conta, int slot)
	{
		ulong h = 14695981039346656037UL;
		foreach (char c in conta.ToLowerInvariant()) { h ^= c; h *= 1099511628211UL; }
		h ^= (ulong)(slot + 1);
		h *= 1099511628211UL;
		return (h % 10_000_000_000UL).ToString("D10");
	}

	/// <summary>
	/// QUEM EU CONHECO, DE QUEM EU GOSTO, DE QUEM EU NAO GOSTO. Ver `Core.Social.Convivio`.
	///
	/// PERSISTE INTEIRO (`CharacterSave.Social`): no DM as duas listas sao `mob/var` comuns -- nao
	/// `tmp` --, entao elas viajam no savefile do mob junto com o resto (`Friendship.dm:10` diz
	/// isso em voz alta: *"persists in the save"*). Amizade que morre no logout nao e amizade.
	/// </summary>
	public Jandirus.Core.Social.Convivio Social = new();

	/// <summary>
	/// O PEDIDO DE AMIZADE PENDENTE: a assinatura de quem me pediu, e o nome dele pra mensagem.
	///
	/// VIVO, nao vai pro disco -- o `tmp/pendingFriendReq` do DM (`Friendship.dm:14-15`) tambem e
	/// `tmp`. Um pedido e um gesto do momento; guardado no save ele viraria uma caixa de entrada
	/// que ninguem esvazia, e "aceitar" um pedido de tres semanas atras nao quer dizer nada.
	/// </summary>
	public string PedidoDeAmizade = "", NomeDeQuemPediu = "";

	/// <summary>
	/// Quando este corpo da o proximo passo de aproximacao (relogio real, ms).
	///
	/// POR JOGADOR e nao um contador do servidor: o `friend_tick` do DM tambem e do mob
	/// (`Friendship.dm:13`). Um contador global faria quem entrou agora esperar a vez do relogio
	/// de quem ja estava -- e, pior, todos os pares do servidor renderiam no mesmo instante.
	/// </summary>
	public long ProximaAproximacao;

	/// <summary>Ate quando a pose de soco fica no ar (relogio real, ms).</summary>
	public long AtaqueAte;

	/// <summary>
	/// A pose que os outros veem. Sai do ESTADO do servidor, nao de um pedido do cliente --
	/// senao daria pra aparecer meditando no meio de uma luta.
	/// </summary>
	/// <param name="canalDeKi">
	/// ESTE CORPO ESTA COM UM RAIO NA MAO? Vem de FORA porque a resposta mora no
	/// <see cref="GameServer.CanalDeKiDe"/> -- o dicionario `_canais`, que fica no `GameServer` e
	/// nao aqui pelo motivo escrito la (*"e estado de sessao de UMA tecnica, e um campo por tecnica
	/// na ficha de todo mundo faz a ficha crescer uma linha por skill portada"*).
	///
	/// E E PARAMETRO, E NAO UM CAMPO NESTA CLASSE, de proposito: um campo aqui seria um bit guardado
	/// -- alguem teria que lembrar de apaga-lo, e e exatamente assim que este projeto prendeu a
	/// aureola no cadaver. Passando, a unica fonte continua sendo o `_canais`, e quem esquecer de
	/// passar leva erro de compilacao em vez de uma pose presa.
	/// </param>
	public Protocol.Pose Pose(long agoraMs, bool canalDeKi)
	{
		// MORTO CAIDO E MORTO DE PE SAO DUAS COISAS. Enquanto o corpo esta no chao do mundo dos
		// vivos (os 2 s de <see cref="Jandirus.Core.World.Alem.MsNoChao"/>) ele e um cadaver e
		// desenha deitado; no Outro Mundo ele levanta e volta a ter poses -- e o `Un_KO()` que o DM
		// chama ANTES de mover (`Death.dm:89`). Ver <see cref="MortoDePe"/>.
		if (Ficha.KO || (Ficha.dead && !MortoDePe)) return Protocol.Pose.Nocauteado;
		if (agoraMs < AtaqueAte) return Protocol.Pose.Atacando;

		// ============================ O RAIO NA MAO, E ELE VEM DEPOIS DO SOCO DE PROPOSITO ============================
		// O DM faz exatamente esta ordem, e faz por acidente feliz do `Fight()`: socar salva o estado
		// atual (`var/prev_state = icon_state`), poe `"Attack"` por alguns tiques e **devolve o
		// anterior** (`CombatMovement.dm:134` e `:176`). Ou seja: quem soca com um raio de pe mostra o
		// soco e VOLTA pro raio -- e nao perde a pose, porque nada apagou o `beaming`.
		//
		// Aqui sai igual sem nenhuma linha de "guardar e devolver": o `AtaqueAte` e um PRAZO e este
		// e um ESTADO, entao o prazo ganha enquanto corre e o estado responde de novo assim que ele
		// vence. Guardar o estado anterior seria reimplementar em duas casas o que a ordem ja diz.
		//
		// ---- E ELE VEM ANTES DO VOO, o que o DM tambem diz ----
		// `usr.icon_state = "Blast"` (`beams.dm:280`) e escrito DEPOIS do `icon_state = "Flight"` de
		// quem ja estava voando (`flying.dm:114`), e nada reescreve o Flight por tique -- so o NADO
		// se reafirma assim (`Stats.dm:399`). Entao no original o raio ganha do voo, e ganha aqui: o
		// corpo continua no ar (a altura viaja em bit separado, ver `EntityState.Voando`) com a pose
		// do raio. Era isso que o dono pediu -- *"so voltaria a posicao de IDLE quando ele PARASSE DE
		// USAR O BEAM"* --, e um raio disparado do ar que mostrasse a pose de pairar seria a mesma
		// queixa noutra altura.
		// =========================================================================================================
		if (canalDeKi) return Protocol.Pose.Canalizando;

		// A POSE DE VOO ESTAVA DEFINIDA E MORTA: `Protocol.Pose.Voando` existia no enum, o
		// `CharacterVisual.SetPose` ja mapeava pra animacao "flight", e este metodo NUNCA a
		// devolvia -- entao a ponta do cliente esperava por um valor que ninguem escrevia.
		// Vem depois do ataque de proposito: socar no ar mostra o soco, nao o pairar.
		if (Voando) return Protocol.Pose.Voando;
		// A POSE DE NADO ESTAVA NO MESMO ESTADO EM QUE A DE VOO ESTEVE: `Protocol.Pose.Nadando`
		// definida, o `CharacterVisual` ja mapeando pra animacao "flight", e este metodo nunca a
		// devolvendo -- quem olhasse de fora veria o vizinho ANDANDO por cima do lago.
		//
		// DEPOIS DO VOO, e a ordem nao e escolha: os dois estados se excluem (`AlternarNado` desliga
		// o voo e `AlternarVoo` desliga o nado), entao qualquer ordem da o mesmo. Fica aqui porque e
		// aqui que se le "primeiro o que SOBE, depois o que nao sobe".
		if (Nadando) return Protocol.Pose.Nadando;
		if (Ficha.train) return Protocol.Pose.Treinando;
		if (Ficha.med) return Protocol.Pose.Meditando;
		return Protocol.Pose.Normal;
	}

	/// <summary>O rabo ainda esta no corpo? Falso pra quem nunca teve.</summary>
	public bool TemRaboAgora() => Combate?.Corpo.Achar("Rabo") is { Decepado: false };

	public SheetState Sheet() => new()
	{
		Class = Class,
		BP = Ficha.BP,
		ExpressedBP = Ficha.expressedBP,
		Ki = Ficha.Ki,
		MaxKi = Ficha.MaxKi,
		HP = Ficha.HP,
		Vigor = Ficha.stamina,
		VigorMax = Ficha.maxstamina,
		Nutricao = Ficha.CurrentNutrition,
		NutricaoMax = Jandirus.Core.Stats.Nutricao.Tanque(Ficha.Metabolism),

		// AS DUAS RAZOES SAEM PRONTAS DAQUI, e nao ha escolha: sem scouter o BP e o BP expresso
		// viram NaN no `FichaVisivel`, entao o cliente nao teria como dividir um pelo outro. Quem
		// calcula e o Core (`Fighter.Inteireza` / `Fighter.MultiplicadorTotal`); aqui elas so
		// entram no pacote. Razao nao e poder -- ver o cabecalho de `GameServer.Sigilo`.
		Inteireza = Ficha.Inteireza,
		MultTotal = Ficha.MultiplicadorTotal,

		// O FIM DO TRILHO DA BARRA DE KI. `powerupcap` e RAZAO sobre o MaxKi e mora no Statify,
		// que so roda no servidor -- o cliente nao tem como saber ate onde o proprio tanque sobe.
		TetoKi = Jandirus.Core.Combat.CargaDeKi.TetoEmRazao(Ficha),

		SpeedStat = SpeedStat,
		// a cadencia vai calculada: ela muda com o Ki carregado, e o cliente precisa dela
		// pro proprio cooldown e pra duracao da animacao de soco
		SocoMs = (int)Math.Round(CombatMath.Cadencia(Ficha) * 1000),
		MembrosRuins = (byte)Math.Min(255, Combate?.Corpo.Partes.Count(p => p.Decepado || p.Quebrado) ?? 0),
		Estado = (byte)((Ficha.KO ? 1 : 0) | (Ficha.dead ? 2 : 0)
						| (Combate?.Bloqueando == true ? 4 : 0) | (Combate?.Letal == true ? 8 : 0)
						| (TemRaboAgora() ? 16 : 0)
					// O BIT DE "NAO SOU EU QUE ESTOU ME MEXENDO" cobre AS DUAS maneiras de o servidor
					// dirigir um corpo -- o arremesso e o feixe que carrega (ver
					// `DirigidoPeloServidor`). Ele e o que faz o cliente parar de integrar tecla e so
					// deslizar ate a ultima correcao; sem ele no arrasto, as duas pontas empurrariam o
					// mesmo corpo em sentidos opostos, que e o TREMOR que este projeto ja pagou duas
					// vezes (ver `TickDoEmpurrao` e `LocalPlayer._empurrado`).
					| (DirigidoPeloServidor ? 32 : 0)
					// A DIRECAO DA QUEDA, em 2 bits (os dois ultimos que sobravam no byte).
					//
					// Ela precisa vir do SERVIDOR ate pro proprio dono. O corpo caido e desenhado pelo
					// cliente com a direcao que ELE tinha, e o servidor com a que CHEGOU pelo ultimo
					// pacote -- e as duas divergem justamente no instante do nocaute, que e quando o
					// angulo e escolhido. O dono fotografou as duas telas: o mesmo corpo caido pra
					// lados diferentes.
					//
					// Com isto ha UMA fonte: o servidor congela a direcao quando o nocaute acontece e
					// todo mundo (inclusive quem caiu) desenha por ela.
					| ((byte)DirecaoDeitado << 6)),

		// O SEGUNDO BYTE DE ESTADO -- hoje ele carrega DUAS coisas: o nado e o puxao da fusao.
		//
		// A primeira vem pela FICHA e nao pelo snapshot porque quem precisa dela e o DONO do corpo, pra
		// prever o passo pela mesma regra que o servidor vai conferir (ver `SheetState.Nadando` e
		// `LocalPlayer._nadando`). Quem olha de fora recebe a `Pose.Nadando`, que basta pra desenhar.
		//
		// A SEGUNDA e pelo mesmo motivo, e ela QUALIFICA o bit 32 do primeiro byte: durante o puxao o
		// corpo esta sendo dirigido pelo servidor (`Empurrado` ligado, e tem que estar), mas ele nao
		// esta sendo ARREMESSADO -- e o desenho do arremesso deita o corpo 90 graus. Ver
		// `SheetState.PuxadoNaFusao`.
		Estado2 = (byte)((Nadando ? 1 : 0) | (PuxaoDeFusaoRestante > 0 ? 2 : 0)),
	};
}

/// <summary>
/// SERVIDOR. Roda como autoload; so liga de verdade quando o processo sobe com --server
/// (o mesmo executavel serve de cliente e de servidor headless).
///
/// AUTORIDADE: o cliente calcula o proprio movimento e manda a posicao; o servidor CONFERE
/// com <see cref="MoveRules.ValidateStep"/> e devolve correcao quando o passo nao cabe no
/// tempo decorrido. O tempo usado na conta e o do SERVIDOR (relogio local entre pacotes),
/// nunca um dt vindo do cliente: dt e a variavel que todo cheat de velocidade infla.
///
/// INTERESSE: o snapshot de cada jogador so leva quem compartilha o mesmo
/// <see cref="ZoneKey.Hash"/>. E o que substitui o "mesmo z" do BYOND.
/// </summary>
public partial class GameServer : Node
{
	public static GameServer? Instance { get; private set; }
	public bool Running { get; private set; }

	private readonly NetManager _net;
	private readonly EventBasedNetListener _listener = new();
	private readonly Dictionary<int, ServerPlayer> _players = [];
	private readonly Dictionary<NetPeer, ServerPlayer> _byPeer = [];
	// quem ja entrou na CONTA mas ainda esta escolhendo personagem
	private readonly Dictionary<NetPeer, AccountSave> _logados = [];
	// a conta de quem ja esta em jogo -- e nela que o personagem e gravado
	private readonly Dictionary<NetPeer, AccountSave> _contas = [];
	private readonly Dictionary<ulong, List<ServerPlayer>> _zones = [];  // hash da zona -> quem esta la
	private int _nextId = 1;
	private double _accumulator;
	private int _tickCount;
	private bool _carregado;
	private ZoneCatalog? _catalogo;
	private RaceCatalog? _racas;
	private Jandirus.Core.Appearance.VisualCatalog? _visual;
	private AccountStore? _store;
	private readonly Random _rng = new();

	/// <summary>BP forcado por `--bpteste`. 0 = desligado (o normal).</summary>
	private double _bpDeTeste;

	/// <summary>`--kiteste`: nasce com Ki_Unlocked e Basic Ki Control nivel 5. Ver o `_Ready`.</summary>
	/// <summary>
	/// `--esquivateste N`: o HOST entra com N vezes o BP de quem chegar. 0 = desligado.
	///
	/// Existe pelo mesmo motivo -- e com a mesma forma -- do `BpDoHostNoTeste` do `--clashteste`:
	/// a ESQUIVA POR VELOCIDADE nao acontece entre iguais, e isso nao e defeito, e a regra. A
	/// pontaria e `Etechnique/Espeed * BpModulus * 100` (`CombatMath.Pontaria`), entao dois
	/// personagens recem-criados batem um no outro 100% das vezes -- e o proprio DM diz isso na
	/// linha ao lado (`CombatMovement.dm:190`, "two perfectly matched players will hit 100% of
	/// the time"). Sem desnivel de poder NAO HA o que fotografar.
	///
	/// O desnivel vai no HOST porque e ele quem tem janela nas bancadas de dois processos (o
	/// adversario sobe `--headless`), e a esquiva se desenha em cima de QUEM DESVIA.
	/// </summary>
	private double _bpDoHostNaEsquiva;

	private bool _kiDeTeste;

	/// <summary>Os dois caminhos do Ki liberado. Escritos uma vez pra nao divergirem.</summary>
	private const string SkillKiUnlocked = "/datum/skill/mind/Ki_Unlocked";
	private const string SkillKiControl = "/datum/skill/mind/Basic_Ki_Control";
	private double _techDeTeste, _zeniDeTeste;
	private int _marcosDeTeste;
	private List<string> _skillsDeTeste = [];
	/// <summary>`--nivelteste path=n,...`: (skill, nivel) pra quem entra e pra quem a compra. Ver `AplicarNiveisDeTeste`.</summary>
	private readonly List<(string Path, int Nivel)> _niveisDeTeste = [];
	private bool _nascerEmGerado;

	/// <summary>As fichas dos planetas (gravidade e tipo), lidas de `planetas.json`.</summary>
	private CatalogoDePlanetas? _planetas;

	/// <summary>
	/// AS SKILLS SUBINDO SOZINHAS. E o `effector()` do original: enquanto a skill tem dono, ela
	/// acumula exp, passa da barreira e sobe -- e cada degrau destrava alguma coisa.
	///
	/// A SUBIDA E ANUNCIADA COM O TEXTO DO DM quando ele existe. E o momento em que o jogo
	/// ensina: ninguem descobre sozinho que a Kicker no nivel 2 da um chute novo.
	/// </summary>
	private void TickDosNiveis()
	{
		if (_skills == null) return;
		foreach (ServerPlayer pl in _players.Values)
		{
			// ============================ SO JOGADOR SOBE DE NIVEL SOZINHO ============================
			// NPC TEM `Livro` e `Niveis` -- eles saem das mesmas funcoes que os do jogador
			// (`GameServer.Npc.cs:191-192`), e e assim que o molde do `npcs.json` da skill a um chefe.
			// Sem esta guarda, todo corpo sem dono acumulava exp de skill PRA SEMPRE e o
			// `Niveis.Aplicar` escrevia buff novo na ficha dele a cada degrau:
			//
			//   * no CHEFE isso furava o pino da saga. O `TickDoRoteiro` reancora o `Ficha.BP` de
			//     segundo em segundo, mas reancora o BP -- nao os buffs de nivel, que o `Statify`
			//     recalcula por cima e que empurram o `expressedBP` acima do numero anunciado;
			//   * no CIDADAO nada reancorava: um habitante de dois dias de uptime nao era mais o
			//     habitante do `npcs.json`.
			//
			// O nivel de NASCIMENTO nao se perde -- ele e aplicado no sorteio (`SorteioDeNpc:157-158`),
			// e o que morre aqui e so a subida sozinha, que e o `effector()` de quem esta JOGANDO.
			// ======================================================================================
			if (!EhJogador(pl)) continue;
			// o cadaver nao sobe de nivel; o fantasma de pe treina no Outro Mundo como um vivo
			if (EhCadaver(pl) || pl.Livro == null) continue;
			// o tique de UM corpo mora em `GameServer.Skills.cs` (`TicarNiveisDe`): e a mesma
			// funcao que a bancada `--arvoreteste` chama num corpo forjado, que nao passa por `EhJogador`
			TicarNiveisDe(pl);
		}

		// O LOTE G13 ANDA AQUI, e nao no tique cheio: os dois passos dele (a escrita do livro e o
		// laco do estudo) sao do relogio do EFETOR no original -- `writetime++` mora no `medproc()`,
		// que roda no mesmo `sleep(2)` das skills. Ver `GameServer.Tecnicas.G13.cs`.
		TickG13();
	}

	/// <summary>
	/// O EXP DAS PERICIAS DE KI, CREDITADO NO GOLPE -- a porta dos 30 contadores que o `niveis.json`
	/// entregava e o carregador jogava fora. Ver <see cref="Jandirus.Core.Skills.NiveisDeSkill.CreditarPorContador"/>.
	///
	/// <paramref name="vezes"/> e o DELTA DO CONTADOR DO DM naquele ponto, e nao "um golpe": o
	/// segmento de raio e `beamcounter += 3` (`objects.dm:317`), o tiro carregado e
	/// `blastcounter += 5` (`blasts.dm:100`), a barragem e `+= amount`. O numero sai SEMPRE da linha
	/// do original citada no ponto de chamada.
	///
	/// ============ POR QUE NAO DA PRA DERIVAR ISTO DOS CAMPOS `*skill` QUE JA EXISTIAM ============
	/// O port ja incrementa `beamskill`, `blastskill` e companhia nesses mesmos pontos, com o
	/// contador do DM escrito no comentario -- mas a ESCALA e inconsistente entre eles:
	/// `blastskill += 0.05` pra `blastcounter++` (÷20), `kidebuffskill += 0.4` pra `+= 4` (÷10),
	/// `beamskill += 0.03` pra `beamcounter += 3` (÷100). Multiplicar o campo de pericia por uma
	/// constante pra "recuperar" o contador erraria o exp em ate 10x, calado. Sao dois sistemas
	/// diferentes (pericia entra em dano; contador entra em exp) e agora sao duas linhas.
	/// ============================================================================================
	///
	/// SO JOGADOR, e pelo mesmo motivo do <see cref="TickDosNiveis"/>: corpo sem dono nao roda o
	/// `Efetor`, entao exp creditado nele nunca viraria nivel -- so um numero crescendo pra sempre
	/// na memoria de cada NPC do servidor.
	///
	/// CUSTO POR CHAMADA: uma busca em dicionario (o indice por contador) e no maximo tres
	/// `Creditar`, cada um outra busca. E O(1) e sem alocacao -- pode ficar no caminho do golpe.
	/// </summary>
	private void CreditarContador(ServerPlayer pl, string contador, double vezes)
	{
		if (vezes <= 0 || pl.Livro == null || !EhJogador(pl)) return;
		pl.Niveis.CreditarPorContador(contador, vezes, pl.Ficha);
	}

	/// <summary>
	/// O CORPO CHEGOU AO FIM? -- o `if(Body<0.1)` do `AgeCheck` (`Aging.dm:176-183`).
	///
	/// ============================ POR QUE ELE NAO MORA NUM TIQUE ============================
	/// No DM o `AgeCheck` e chamado de DOIS lugares: o login (`Login.dm:345`) e o virar do ano
	/// (`WorldClock.dm:92`, o `proc/Years()`, uma vez por ano do mundo) -- e ele ainda sai cedo se
	/// `LastYear == Year` (`Aging.dm:113`). Ou seja: quem manda e o RELOGIO DE ANO, nao o tique.
	///
	/// Este port nao tem relogio de ano (`WorldClock.dm` nao foi portado -- `CatalogoDePlanetas.cs:35`
	/// e `Ceu.cs:89` ja dizem isso por escrito), e a idade so anda por GESTO: a Sala do Tempo
	/// (`EnvelhecerNaSala`) e o Growth Spurt do Saibaman (`EnvelhecerG2`). Entao os pontos fieis sao
	/// esses dois mais o login -- que e exatamente onde o original pergunta. Poe-lo no `TickFichas`
	/// seria perguntar 5x por segundo uma coisa que so muda quando alguem envelhece.
	///
	/// CONSEQUENCIA HONESTA: enquanto nao houver calendario, so envelhece quem entra na Sala do
	/// Tempo. Um Saiyajin precisa de ~120 sessoes de Sala pra chegar aqui. Isto NAO e uma torneira
	/// que abre sozinha -- e a torneira certa ligada no cano que existe hoje.
	/// =======================================================================================
	///
	/// MEDIDO ANTES DE LIGAR: nos 67 saves de hoje, 56 personagens tem idade e raca, e ZERO cruzam
	/// a linha (todos em 18 anos, contra auge 50 do Humano e 80 do Saiyajin). Ninguem morre ao
	/// ligar isto.
	/// </summary>
	/// <returns>Verdadeiro se ele morreu agora.</returns>
	private bool ConferirMorteDeVelhice(ServerPlayer pl)
	{
		if (pl.Ficha.dead || pl.Ficha.aged_out) return false;
		if (!Jandirus.Core.Races.Envelhecimento.MorreuDeVelhice(pl.Ficha.Race, pl.Idade)) return false;

		// A MARCA VEM ANTES DA MORTE, e a ordem importa: o `AoMorrer` dispara de dentro do
		// `Morrer()` e e de la que saem o prazo e a viagem pro Outro Mundo. Marcar depois deixaria
		// esse gancho rodar sem saber que esta e a morte que nao tem volta.
		pl.Ficha.aged_out = true;

		// `Morrer()` E A PORTA UNICA DE QUALQUER MORTE (ver `AdminMatar`): ele zera KO, guarda,
		// nocaute restante e as marcas de combate. `ignorarSeguro: true` e o `buudead="force"` do
		// original (`Aging.dm:180`) -- a velhice passa por cima do seguro da Aura of Destruction,
		// que e o mesmo que o DM faz ao forcar a morte.
		pl.Combate?.Morrer(ignorarSeguro: true);
		MandarFicha(pl);

		// `to_chat(view(src), "[src] dies from old age.")` -- a zona inteira ve, e nao so ele.
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			Avisar(o, o == pl ? "seu corpo enfim cede ao tempo." : $"{pl.Name} morre de velhice.");

		GD.Print($"[server] {pl.Name} morreu de velhice aos {pl.Idade} anos ({pl.Ficha.Race}).");
		return true;
	}

	/// <summary>Um degrau pode ter concedido verb novo: o menu do cliente precisa saber.</summary>
	private void HabilidadesMudaram(ServerPlayer pl) => MandarSkills(pl, forcar: true);

	private void CarregarNiveis()
	{
		const string cj = "res://Assets/Data/niveis.json";
		if (!Godot.FileAccess.FileExists(cj))
		{
			GD.PushWarning("[server] sem niveis.json -- rode o AssetPipeline (comando 'effector')");
			return;
		}
		int n = Jandirus.Core.Skills.RegrasDoDisco.Carregar(Godot.FileAccess.GetFileAsString(cj));
		// O QUE ENTROU E O QUE FICOU DE FORA, em numero. Regra descartada em silencio ja custou
		// caro neste projeto -- e as condicoes de DM que este port nao sabe avaliar continuam
		// descartadas, so que agora contadas.
		GD.Print($"[server] exp por estado: {Jandirus.Core.Skills.RegrasDoDisco.GanhosPorEstado} regra(s)"
			+ $" | por contador (evento): {Jandirus.Core.Skills.RegrasDoDisco.GanhosPorContador}"
			+ $" | condicao nao entendida: {Jandirus.Core.Skills.RegrasDoDisco.CondicoesNaoEntendidas}");
		GD.Print($"[server] niveis de skill: {n} regras ({Jandirus.Core.Skills.RegrasDeNivel.Total} no total)");
	}

	private void CarregarPlanetas()
	{
		const string cj = "res://Assets/Data/planetas.json";
		if (!Godot.FileAccess.FileExists(cj))
		{
			GD.PushWarning("[server] sem planetas.json -- rode o AssetPipeline (comando 'planetas')");
			return;
		}
		_planetas = CatalogoDePlanetas.Parse(Godot.FileAccess.GetFileAsString(cj));
		GD.Print($"[server] planetas: {_planetas.Total} com gravidade propria");
	}

	/// <summary>
	/// POE O PESO DO CHAO na ficha. E o `Planetgrav` do original, e ele estava parado em 1 pra
	/// todo mundo desde o comeco do port -- treinar em Vegeta (10x) rendia igual a treinar na
	/// Terra, e nada na tela dizia isso. Um multiplicador que existe e nunca muda e pior que um
	/// que nao existe: da a impressao de estar valendo.
	///
	/// NO ESPACO A GRAVIDADE E ZERO (o `if("Space") Planetgrav=0` do DM) -- e por isso que
	/// ninguem treina viajando.
	/// </summary>
	/// <param name="nascendo">
	/// Este corpo esta sendo POSTO NO MUNDO agora (ver <see cref="PorNoMundo"/>)? Vale pra corpos que
	/// nao tem berco proprio -- NPC, habitante, clone --, cuja zona de nascimento **e** o berco deles.
	/// Ver a aclimatacao logo abaixo.
	/// </param>
	private void AplicarGravidade(ServerPlayer pl, bool nascendo = false)
	{
		// ============================ O RITMO DA ZONA VEM PRIMEIRO, E FORA DO ATALHO ============================
		// Os multiplicadores de TREINO que dependem do lugar sao escritos AQUI porque este metodo e o
		// funil por onde TODA troca de zona passa: login, passagem, porta, nave, admin, planeta
		// gerado, mente e leme. Um `Aplicar` chamado em cada um desses caminhos seria oito lugares
		// pra alguem esquecer de um -- e o esquecido seria justo a SAIDA.
		//
		// SAO DOIS LUGARES E UMA CHAMADA SO (ver `Core/World/RitmoDaZona.cs`): a Sala do Tempo
		// (280x de BP, 4x de maestria) e a DIMENSAO MENTAL (0,25x de BP, o `MindDimensionMult` do
		// pedido do dono). O `Aplicar` escreve o neutro antes de escrever a excecao, entao sair de
		// qualquer um dos dois limpa o outro de graca.
		//
		// ANTES do atalho de gravidade la embaixo, e isso e o ponto: entrar na Sala vindo de Vegeta
		// nao muda a gravidade (as duas sao 10x), entao aquele `return` engoliria o ritmo inteiro e a
		// Sala renderia 1x pra quem chegasse do planeta certo. Silencioso, e so em um caminho.
		//
		// O TERCEIRO ARGUMENTO E A SESSAO (13.5). Estar dentro nao basta desde que a sala PRENDE:
		// quem passou dos 2 dias in-game continua la e nao rende mais nada. Ver `SessaoDaSalaValendo`.
		// ====================================================================================================
		Jandirus.Core.World.RitmoDaZona.Aplicar(pl.Ficha, pl.Zone, SessaoDaSalaValendo(pl));

		if (_planetas != null)
		{
			FichaDePlaneta f = _planetas.De(pl.Zone.Name);

			// ============================ NINGUEM NASCE ESMAGADO ============================
			// O `race.dm:130-131` -- ver `Birth.AclimatarAoBerco`, onde a divida esta escrita por
			// inteiro. Em resumo: um Frost Demon nasce em Icer Planet, que puxa 15x, e a maestria de
			// berco dele valia 1. Com o esmagamento ligado ele nasceria PRESO no chao perdendo vida.
			//
			// **SO NO PROPRIO BERCO**, e essa e a diferenca entre a regra e uma trapaca: chegar de
			// nave num planeta pesado nao acostuma ninguem a nada -- o unico jeito de dominar
			// gravidade continua sendo treinar nela (`GravGain`). O DM diz "spawn planet", e o berco
			// deste port e exatamente isso.
			//
			// FORA DO ATALHO de gravidade logo abaixo porque ele desiste quando o numero nao mudou --
			// e no login o `Planetgrav` do save ja costuma ser o do berco, que e justamente o caso em
			// que a aclimatacao precisa acontecer.
			// ============================================================================
			if (nascendo || string.Equals(pl.Zone.Name, pl.Berco.Planeta, StringComparison.OrdinalIgnoreCase))
				Jandirus.Core.Races.Birth.AclimatarAoBerco(pl.Ficha, f.Gravidade);

			if (Math.Abs(pl.Ficha.Planetgrav - f.Gravidade) >= 1e-9)
			{
				pl.Ficha.Planetgrav = f.Gravidade;

				// O TIQUE INTEIRO, e nao so o `Statify` que estava aqui: o `weight_ratio` multiplica a
				// gravidade LOCAL (ver `Fighter.WeightTick`), entao quem pisa num chao 10x mais pesado
				// passa a ser esmagado *neste instante* -- e o passo, que sai daqui poucas linhas
				// abaixo, precisa ja saber disso. Com so o `Statify`, o corpo andaria 200 ms na
				// velocidade do planeta anterior.
				//
				// `Tick` E NAO `Statify` + `WeightTick`: o `PowerLevel` no meio e o que mantem o
				// recorde de peso (`weight_cap_hw`) estavel -- ver o comentario longo em `AjustarPeso`,
				// onde pular esse passo fazia vestir mais peso pesar menos.
				pl.Ficha.Tick(agoraMs: NowMs());

				pl.SigAtributos = "";
				if (f.Gravidade > 1)
					Avisar(pl, $"o chão de {f.Nome} puxa {f.Gravidade:0.##} vezes mais forte. Cada passo custa, "
							   + "e cada treino rende.");
			}
		}

		// A VELOCIDADE E A FICHA SAEM SEMPRE, e nao so quando a gravidade mudou.
		//
		// `SpeedStat` **nao esta** na lista de campos que o `TickFichas` compara antes de reenviar a
		// ficha (ver o comentario la), entao mudar de zona mexeria no passo sem avisar o cliente: ele
		// continuaria andando na velocidade do mapa anterior ate algum outro numero da ficha mexer
		// por acaso, e o servidor o corrigiria a cada pacote -- o corpo tremendo. E a MESMA familia de
		// defeito que o `RecalcularVelocidade` ja documenta pra nave, e a defesa e a mesma: recalcular
		// e mandar na hora do evento.
		RecalcularVelocidade(pl);
		MandarFicha(pl);
	}

	/// <summary>
	/// De quantos em quantos ticks a ficha e recalculada. O BYOND rodava o statify/powerlevel
	/// a cada ~0,3s; 6 ticks de 30 Hz da 5 Hz, mesma ordem de grandeza. Nao adianta rodar a
	/// 30 Hz: nada que entra na conta muda mais rapido que isso.
	/// </summary>
	private const int TicksPorFicha = 6;

	/// <summary>
	/// UM SEGUNDO EXATO, e nao "o mesmo tick da ficha". O dreno das tecnicas sustentadas e
	/// POR SEGUNDO no original (`sleep(10)` = 10 decimos); cobra-lo junto do tick de ficha, que
	/// e 5 Hz, faria a invisibilidade custar cinco vezes o preco e cair sozinha em segundos.
	/// Cadencia errada num dreno nao parece bug, parece balanceamento ruim.
	/// </summary>
	private const int TicksPorSegundo = 30;

	// zona inicial de todo mundo enquanto nao existe criacao de personagem
	private static readonly ZoneKey SpawnZone = ZoneKey.Premade("Earth");

	/// <summary>
	/// O MESMO ponto de nascimento do BYOND: `locate(rand(240,260), rand(240,260), 1)`, o
	/// campo aberto no meio da Terra. Em pixel isso e o centro do tile (249, 250) -- o canto
	/// (320, 320) que estava aqui era um lugar arbitrario de teste, longe de tudo.
	/// </summary>
	private static readonly Vec2 SpawnPos = new(249 * 32 + 16, 250 * 32 + 16);

	public GameServer()
	{
		_net = new NetManager(_listener)
		{
			AutoRecycle = true,
			// CINCO, e nao quinze. O `Send` do LiteNetLib so ENFILEIRA; quem poe o pacote no fio e a
			// thread de logica, a cada `UpdateTime` ms. Com 15 ms o snapshot de 33,3 ms saia numa
			// grade de 15 que nao divide 33,3 -- os intervalos de chegada alternavam 30/45 ms sem
			// nada de errado na rede. O tique ainda acorda a thread na hora (`TriggerUpdate` no fim
			// do snapshot); os 5 ms sao o teto pra tudo o que sai fora do tique (correcao, chat...).
			UpdateTime = 5,
			// OS MESMOS CANAIS DO CLIENTE, pela mesma constante -- ver `Protocol.ChannelVoz`: os dois
			// `NetManager` tem que subir com a mesma contagem, e "2" escrito aqui era o contrato de
			// `Protocol.TotalDeCanais` copiado a mao (e ja divergente: o cliente abre 3). A divergencia era
			// LATENTE, e por isso nenhuma bancada de voz a viu: a voz vai `Unreliable`, que nao leva numero
			// de canal no fio -- esta medido em `Protocol.ChannelVoz`, e la tambem esta o que ela viraria
			// (excecao no `Send`) no dia em que este canal levasse algo canalizado.
			ChannelsCount = Protocol.TotalDeCanais,
		};
	}

	public override void _Ready()
	{
		Instance = this;
		SetProcess(false);   // dormente ate alguem mandar subir

		string[] args = OS.GetCmdlineArgs();

		// A BANCADA E LIDA ANTES DA GUARDA. O servidor tambem sobe DENTRO do cliente (o botao
		// "Hospedar" e o `--host`), e nesse caminho este _Ready sai na linha seguinte -- ler a
		// flag depois dela a deixava sem efeito justamente no modo em que se testa.
		int bpIdx = Array.IndexOf(args, "--bpteste");
		if (bpIdx >= 0 && bpIdx + 1 < args.Length && double.TryParse(args[bpIdx + 1],
				System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double bpT))
		{
			_bpDeTeste = bpT;
			GD.Print($"[server] BANCADA: todo personagem entra com BP {bpT:N0}");
		}

		// `--techteste`: entra com tecnologia e zeni. Existe pelo mesmo motivo do `--bpteste`: o
		// laboratorio de DNA pede 70 de tecnologia, que sao horas de estudo -- sem atalho nao ha
		// como um teste automatico chegar la, e o sistema inteiro ficaria sem prova.
		int tIdx = Array.IndexOf(args, "--techteste");
		if (tIdx >= 0)
		{
			_techDeTeste = 80;
			_zeniDeTeste = 5_000_000;
			if (tIdx + 1 < args.Length && double.TryParse(args[tIdx + 1],
					System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out double tv)) _techDeTeste = tv;
			GD.Print($"[server] BANCADA: tecnologia {_techDeTeste:0} e {_zeniDeTeste:N0} zeni pra todo mundo");
		}

		// `--portateste`: nasce colado numa porta. As 82 portas do jogo estao dentro de casas, a
		// dezenas de tiles do ponto de nascimento -- andar ate uma leva meio minuto, e um teste
		// automatico que leva meio minuto pra COMECAR nao roda. Ver `NascerNaPorta`.
		// ============================ `--kiteste`: entra com o Ki LIBERADO ============================
		// Pra poder testar a tecla C a mao. Nao basta UMA coisa -- sao DUAS, e elas passam por
		// canais diferentes do motor de skills, o que e justamente o que torna isto facil de errar:
		//
		//   `Ki_Unlocked`            e uma skill COMPRADA  -> canal dos efeitos (MeditateGivesKiRegen)
		//   `Basic_Ki_Control` nv 5  e um DEGRAU de nivel   -> canal das flags de degrau (canPower)
		//
		// E o `canPower` (o "controle bom", `Mind.dm:281`) que deixa a carga passar de 100%. Dar so
		// a primeira faz o C carregar e parar no teto -- que e metade do que se quer testar.
		//
		// As duas sao concedidas ANTES do `PrepararSkills`/`Niveis.Aplicar`, e nao depois: assim o
		// caminho normal aplica os efeitos UMA vez. Conceder depois exigiria reaplicar, e efeito de
		// skill aplicado duas vezes soma duas vezes.
		// ================================================================================================
		_kiDeTeste = Array.IndexOf(args, "--kiteste") >= 0;
		if (_kiDeTeste) GD.Print("[server] BANCADA: todo mundo entra com o Ki liberado (C carrega e passa de 100%)");

		_portaDeTeste = Array.IndexOf(args, "--portateste") >= 0;
		if (_portaDeTeste) GD.Print("[server] BANCADA: todo mundo nasce colado numa porta");

		// `--feridateste`: nasce com uma ESCADA de estrago -- cada regiao do corpo num degrau
		// diferente. E o unico jeito de ver as duas fases do efeito (o roxo e o sangue) na MESMA
		// tela: numa luta de verdade elas aparecem separadas por minutos, e uma foto so pega uma.
		_feridaDeTeste = Array.IndexOf(args, "--feridateste") >= 0;
		if (_feridaDeTeste) GD.Print("[server] BANCADA: todo mundo nasce com uma escada de ferimentos");

		// `--sem-admin-local`: ninguem vira admin so por conectar da propria maquina.
		// E o que quem hospeda ATRAS DE TUNEL (playit.gg, ngrok, Docker) precisa ligar: por la
		// TODO jogador chega com endereco local, e sem isto o servidor inteiro entraria admin.
		// Ver `AdminPorEndereco` -- ha tambem um desarme automatico quando a ambiguidade aparece.
		if (Array.IndexOf(args, "--sem-admin-local") >= 0)
		{
			DesligarAdminPorEndereco();
			GD.Print("[server] admin por endereco DESLIGADO (--sem-admin-local): so a marca na conta vale");
		}

		// `--quebrarteste N`: derruba N celulas de cenario em volta do ponto de nascimento assim
		// que alguem entra. Existe pelo verb de admin que REFAZ o cenario: o estrago so acontece
		// quando um corpo e arremessado contra parede por outro jogador -- ou seja, um teste
		// automatico dele exigiria dois robos brigando de verdade, e mesmo assim so as vezes.
		// Sem isto o "Restore Scenery" e um botao que ninguem nunca viu funcionar.
		int qIdx = Array.IndexOf(args, "--quebrarteste");
		if (qIdx >= 0 && qIdx + 1 < args.Length && int.TryParse(args[qIdx + 1], out int qv))
		{
			_quebrarDeTeste = Math.Clamp(qv, 0, 200);
			GD.Print($"[server] BANCADA: {_quebrarDeTeste} celulas de cenario caem no nascimento");
		}

		// `--diaggolpe`: cada soco relata a propria conta no console -- os dois BP expressos, o
		// multiplicador que sai deles, os stats ofensivo e defensivo, e o motivo de um erro.
		// Existe porque as duas queixas do dono ("a hitbox ta meio estranha" e "o adm tava dando
		// +100 de dano") sao invisiveis pelo jogo: o cliente desenha erro de geometria e erro de
		// pontaria do mesmo jeito, e nenhuma tela mostra o BP EXPRESSO do outro, que e o termo
		// que multiplica o dano. Ver `ExplicarGolpe`.
		if (Array.IndexOf(args, "--diaggolpe") >= 0)
		{
			_diagGolpe = true;
			GD.Print("[server] BANCADA: cada soco vai relatar a propria conta ([golpe])");
		}

		// `--gestacaoteste N`: encurta a gestacao do bio-androide pra N segundos. Sem isto o teste
		// levaria DOZE HORAS -- e um sistema que so da pra testar em doze horas nao e testado.
		int gIdx = Array.IndexOf(args, "--gestacaoteste");
		if (gIdx >= 0 && gIdx + 1 < args.Length && double.TryParse(args[gIdx + 1],
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out double gv))
		{
			_gestacaoDeTeste = gv;
			GD.Print($"[server] BANCADA: gestacao de bio-androide em {gv:0}s");
		}

		// `--marcosteste N`: entra com N marcos. A progressao profunda (destravar arvore por
		// `bodyskill`, chegar na Martial Arts, ganhar um estilo) custa uma dezena de compras --
		// com os tres marcos iniciais nao ha como um teste automatico alcancar nada disso.
		int mIdx = Array.IndexOf(args, "--marcosteste");
		if (mIdx >= 0 && mIdx + 1 < args.Length && int.TryParse(args[mIdx + 1], out int mv))
		{
			_marcosDeTeste = mv;
			GD.Print($"[server] BANCADA: {mv} marcos pra todo mundo");
		}

		// `--skillteste a,b,c`: concede as skills listadas (typepath) a quem entrar.
		// A cadeia de pre-requisitos de uma tecnica tem meia duzia de degraus; um teste automatico
		// que precise andar a cadeia inteira testa a PROGRESSAO, nao a tecnica. Isto separa as duas
		// perguntas: a progressao ja tem teste proprio, e aqui eu quero ver a tecnica DISPARAR.
		int sIdx = Array.IndexOf(args, "--skillteste");
		if (sIdx >= 0 && sIdx + 1 < args.Length)
		{
			_skillsDeTeste = [.. args[sIdx + 1].Split(',', StringSplitOptions.RemoveEmptyEntries
													   | StringSplitOptions.TrimEntries)];
			GD.Print($"[server] BANCADA: concedendo {_skillsDeTeste.Count} skills: {string.Join(" | ", _skillsDeTeste)}");
		}

		// `--nivelteste path=n,path=n`: poe as skills listadas NO NIVEL n -- as que quem entra JA TEM (no
		// login, depois do `--skillteste`) e as que ele COMPRAR depois. Nao concede nada: a bancada do
		// cliente que disca (`--diagdegrau`) precisa de um verb concedido POR NIVEL (o Hokuto, dado pelo
		// `--skillteste` e posto no 2 aqui) e de uma Trindade no nivel 2 depois de COMPRADA pelo funil,
		// sem esperar o efetor subir cem tiques de luta -- a subida em si ja tem bancada propria.
		// Ver `AplicarNiveisDeTeste`.
		int nvIdx = Array.IndexOf(args, "--nivelteste");
		if (nvIdx >= 0 && nvIdx + 1 < args.Length)
		{
			foreach (string par in args[nvIdx + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				int ig = par.LastIndexOf('=');
				if (ig > 0 && int.TryParse(par[(ig + 1)..], out int nivel)) _niveisDeTeste.Add((par[..ig], nivel));
			}
			GD.Print($"[server] BANCADA: {_niveisDeTeste.Count} skill(s) no nivel de teste: "
					 + string.Join(" | ", _niveisDeTeste.Select(p => $"{p.Path}={p.Nivel}")));
		}

		// `--voltateste`: quem entrar nasce a SEIS tiles da beirada oeste.
		//
		// Sem isto a bancada da volta andaria 249 tiles ate a costura -- cem segundos de caminhada
		// antes da primeira conferencia, e um teste que demora cem segundos nao e rodado.
		_nascerNaBeirada = Array.IndexOf(args, "--voltateste") >= 0;
		if (_nascerNaBeirada) GD.Print("[server] BANCADA: nascendo a 6 tiles da beirada oeste");

		// `--solteste`: cozinha corpos dentro de estrelas e arremessa um deles la pra dentro.
		//
		// Ela NAO mexe em quem esta jogando -- os corpos sao forjados e recolhidos dentro do mesmo
		// bloco sincrono --, mas mata gente, entao so acontece com a flag.
		_solDeTeste = Array.IndexOf(args, "--solteste") >= 0;
		if (_solDeTeste) GD.Print("[server] BANCADA: o sol letal sera exercitado no 1o login");

		// `--vacuoteste`: o sufocamento no vacuo, com corpos forjados de cada raca, pod, nave-capital,
		// traje, cinematica e cargo -- e com os DEFEITOS injetados, um por familia.
		//
		// Ela nao mexe em quem esta jogando (os corpos entram e saem dentro do mesmo bloco sincrono e
		// as naves de papel saem da lista no `finally`), mas MATA corpos e mexe no `_tronos` durante a
		// medicao -- por isso so acontece com a flag. Ver `GameServer.VacuoTeste.cs`.
		_vacuoDeTeste = Array.IndexOf(args, "--vacuoteste") >= 0;
		if (_vacuoDeTeste) GD.Print("[server] BANCADA: o vacuo sera exercitado no 1o login");

		// `--nuvemviva`: a NUVEM com um corpo em cima dela -- as duas metades (cai a pe, NAO cai
		// voando) no Caminho da Serpente e no Templo, o pouso em chao livre e o contra-exemplo das
		// nuvens que so barram. Onze defeitos injetados pelas `SondasDaNuvem`.
		//
		// Ela forja corpos sem dono e roda o `TickDasNuvens` DE PRODUCAO, que varre `_players`
		// inteiro -- por isso e flag e por isso ela recusa rodar com o host em cima de nuvem que
		// derruba. Ver `GameServer.NuvemVivaTeste.cs`.
		_nuvemVivaDeTeste = Array.IndexOf(args, "--nuvemviva") >= 0;
		if (_nuvemVivaDeTeste) GD.Print("[server] BANCADA: a nuvem sera exercitada no 1o login");

		// `--naveteste`: fabrica, assenta, embarca, melhora ate o teto, lanca, viaja e pousa.
		//
		// Ela MEXE no personagem vivo (zeni, tecnologia, zona) e o devolve no fim -- por isso a
		// flag. Ver GameServer.NaveTeste.cs.
		_naveDeTeste = Array.IndexOf(args, "--naveteste") >= 0;
		if (_naveDeTeste) GD.Print("[server] BANCADA: a Spacepod sera exercitada no 1o login");

		// `--menteviva`: O CORPO QUE FICA, **COM DOIS CORPOS DE VERDADE**.
		//
		// NAO E UMA BANCADA DE BOOT, e nao pode ser: o mecanismo que ela mede (`LargarOCorpo`) recusa
		// quem nao tem `Peer`, e a recusa e deliberada -- e literalmente a razao pela qual a
		// `--menteteste` diz nao cobrir a porta. Ela roda no login do SEGUNDO cliente, o que exige
		// dois processos (ver `GameServer.MenteVivaTeste.cs` e o `testar-mente.bat`).
		//
		// Ela MEXE nos dois personagens (zona, BP, membros, forma) e APAGA as duas contas no fim --
		// por isso a flag, e por isso as contas sao proprias dela.
		_menteVivaLigada = Array.IndexOf(args, "--menteviva") >= 0;
		if (_menteVivaLigada) GD.Print("[server] BANCADA: o corpo largado sera medido quando o 2o cliente entrar");

		// `--embarqueteste`: a METADE DE SERVIDOR da bancada da tecla E nas naves. Ela nao pontua --
		// da tecnologia e zeni ao 1o que entrar e abre tres verbos de fixture (alienar, devolver,
		// estragar) que o robo `--diagembarque` usa pra chegar em recusas que nenhum verbo de
		// jogador alcanca. Ver GameServer.EmbarqueTeste.cs.
		_embarqueDeTeste = Array.IndexOf(args, "--embarqueteste") >= 0;
		if (_embarqueDeTeste) GD.Print("[server] BANCADA: fixtures do embarque ligadas (tech, zeni e 3 verbos)");

		// ============================ AS DUAS BANCADAS DE VOZ COM CLIENTES DE VERDADE ============================
		// MAIS NOVAS QUE A COPIA DE 23:07, entao o gancho delas foi deduzido do proprio codigo: o
		// cabecalho do `VozVivaNoLogin`/`VozDuplaNoLogin` diz, com todas as letras, "chamada no fim do
		// `Entrar`, como a `--menteviva` -- e pelo mesmo motivo", e o lado CLIENTE ja le as duas flags
		// (`Client/Boot.cs:742,753`). Sem estas linhas o servidor sobe, os quatro (ou dois) processos
		// conectam, e nenhuma fase roda: silencio indistinguivel de sucesso.
		//
		// O ARGUMENTO DA FLAG E DO CLIENTE (`--vozviva a|b|c|d` = o papel). O servidor so pergunta se
		// a flag ESTA la -- ele e um so, e nao tem papel.
		_vozVivaLigada = Array.IndexOf(args, "--vozviva") >= 0;
		if (_vozVivaLigada) GD.Print("[server] BANCADA: a voz sera medida quando os clientes de verdade entrarem");

		// `--vozvivagente N`: quantos clientes a cena espera antes de comecar (padrao 4, os papeis
		// a/b/c/d). E argumento e nao constante porque a fase 7 (o teto de quatro ouvintes) precisa de
		// quatro, mas quem esta depurando uma fase de distancia roda com dois e nao quer esperar.
		int vvgIdx = Array.IndexOf(args, "--vozvivagente");
		if (vvgIdx >= 0 && vvgIdx + 1 < args.Length && int.TryParse(args[vvgIdx + 1], out int vvgV) && vvgV > 0)
		{
			_vozVivaGente = vvgV;
			GD.Print($"[server] BANCADA: a voz viva espera {_vozVivaGente} cliente(s) de verdade");
		}

		// `--vozdupla`: a IRMA QUE JULGA da de cima -- dois corpos, e o DEFEITO na frente de cada regra.
		// E ela que destranca o `CorteQuebradoDeTeste` (ver `GameServer.Voz.cs:275`, onde o
		// `_vozDuplaLigada` vem PRIMEIRO no `&&` justamente pra sair de graca em jogo).
		_vozDuplaLigada = Array.IndexOf(args, "--vozdupla") >= 0;
		if (_vozDuplaLigada) GD.Print("[server] BANCADA: a voz JULGADA, com dois corpos, quando os dois entrarem");

		// `--clashteste`: o ZanzoClash sem dado e com desnivel de poder.
		//
		// Duas coisas atrapalham medir um embate: o `prob(50)` (uma bancada que so acontece metade
		// das vezes nao e bancada) e o fato de dois personagens recem-criados terem o MESMO BP, o
		// que poe a vantagem em 1,00 e deixa a regra do dono ("o mais forte ganha os pontos
		// multiplicados pela razao") sem nada pra provar. A flag tira o dado e da poder ao HOST.
		_clashSempre = Array.IndexOf(args, "--clashteste") >= 0;
		if (_clashSempre) GD.Print($"[server] BANCADA: embate sem sorteio, e o host com {BpDoHostNoTeste}x de BP");

		// `--mudezteste`: as FIXTURES da bancada da mudez (`--diagmudez` do lado do cliente). Ela nao
		// mede nada aqui -- ela poe embates de VERDADE de pe pro robo do cliente medir o teclado
		// dentro deles. Ver `GameServer.MudezTeste.cs`.
		//
		// O SORTEIO SAI JUNTO, e por isso ela liga o `_clashSempre`: um embate que so acontece metade
		// das vezes faria a bancada do cliente medir o dado. Ela NAO liga o `BpDoHostNoTeste` (isso e
		// da `--clashteste`): o que se mede aqui e o silencio das teclas, e desnivel de poder so
		// mudaria quem ganha.
		_mudezDeTeste = Array.IndexOf(args, "--mudezteste") >= 0;
		if (_mudezDeTeste)
		{
			_clashSempre = true;
			GD.Print("[server] BANCADA: fixtures da MUDEZ ligadas (embate sob encomenda, sem sorteio)");
		}

		// `--esquivateste [N]`: o host entra N vezes mais forte (padrao 10). Ver `_bpDoHostNaEsquiva`.
		//
		// (Restaurado em 2026-08-14: um `git checkout` numa arvore suja apagou este bloco, e o lado
		// CLIENTE da bancada continuou pedindo a flag -- `Client/Boot.cs` e `Client/RoboDeDesvio.cs`.
		// Sem o servidor, os dois corpos entram com o MESMO BP, a esquiva nunca acontece, e a bancada
		// do desvio mede o nada em silencio. E o modo de falhar mais caro que existe aqui.)
		int esqIdx = Array.IndexOf(args, "--esquivateste");
		if (esqIdx >= 0)
		{
			_bpDoHostNaEsquiva = 10;
			if (esqIdx + 1 < args.Length && double.TryParse(args[esqIdx + 1],
					System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out double esqV) && esqV > 0)
				_bpDoHostNaEsquiva = esqV;
			GD.Print($"[server] BANCADA: o host entra com {_bpDoHostNaEsquiva:0.##}x o BP -- e o desnivel que faz o outro ERRAR");
		}

		// `--vooteste`: quem entrar ja sabe voar (skill no nivel 2).
		//
		// Sem isso a bancada do voo seria impossivel de rodar: a skill sobe de nivel pelo `effector`,
		// que precisa de MUITO tempo de jogo -- um teste que exige treinar antes de comecar nao e
		// rodado. Repare que ela nao liga o voo, so da a skill: a porta continua sendo a de producao.
		_vooDeTeste = Array.IndexOf(args, "--vooteste") >= 0;
		if (_vooDeTeste) GD.Print("[server] BANCADA: quem entrar ja sabe voar (Flight nivel 2)");

		// --espeedteste <n>: quem entrar tem o stat BASE de velocidade (`Ficha.speed`) = n. E o STAT
		// e nao o `SpeedStat` porque este e reescrito a cada tique pelo `RecalcularVelocidade` a
		// partir do `Espeed`, que o `Statify` deriva do stat base -- uma escrita direta no
		// `SpeedStat` morria no primeiro tique (aconteceu). O `StatCap` do `Statify` limita o `Espeed`
		// a ~9,7, ou seja `SpeedStat` ~4,85 (~1700 px/s correndo): mande um numero grande
		// (1000000) e o corpo entra no teto do jogo. E da bancada da FLUIDEZ (`testar-fluidez.bat`):
		// o micro teleporte do corpo remoto cresce com a velocidade, e treinar ate o teto nao e coisa
		// que uma rodada de 40 s faz. Ver `_speedStatDeTeste`.
		int ie = Array.IndexOf(args, "--espeedteste");
		if (ie >= 0 && ie + 1 < args.Length
			&& float.TryParse(args[ie + 1], System.Globalization.NumberStyles.Float,
							  System.Globalization.CultureInfo.InvariantCulture, out float speedDeTeste)
			&& speedDeTeste > 0)
		{
			_speedStatDeTeste = speedDeTeste;
			GD.Print($"[server] BANCADA: quem entrar tem o stat base de velocidade {speedDeTeste} (--espeedteste)");
		}

		// ============================ A BANCADA DA AGUA: QUATRO BERCOS ============================
		// Os TRES primeiros poem o corpo no MESMO lago achado por `AcharTravessia` -- muda so ONDE, e
		// cada onde mede uma coisa que os outros nao conseguem medir:
		//
		//   * `--aguateste`  na MARGEM, no seco, virado pra agua. E o unico berco em que a pergunta
		//                    "a agua BARRA quem esta a pe?" tem sentido, e o unico de onde da pra
		//                    exercitar o gesto de verdade do jogador: apertar N na beira e ENTRAR.
		//   * `--aguadentro` DENTRO do lago, a pe. O estado que o proprio servidor preve (deslogar no
		//                    lago, ser jogado la por um arremesso). Mede o MEIO da travessia -- a
		//                    pose, a altura zero, a sombra que nao nasce.
		//   * `--aguanoar`   NO AR, por cima do meio do lago. Mede o POUSO em cima da agua, que e o
		//                    outro caminho que um jogador tem pra comecar a nadar (`DescerAte`).
		//
		// E O QUARTO NAO E O MESMO LAGO: `--aguaparede` procura OUTRA coisa -- uma celula de agua
		// COLADA NUM MURO -- e poe o corpo na agua um tile antes dela, olhando pro muro. Ele mede a
		// unica pergunta que o conserto do modo abriu de verdade: nadando, a parede ainda para?
		//
		// ELE PRECISA DE BERCO PROPRIO, e isso foi MEDIDO e nao suposto: a versao anterior tentava
		// alcancar o muro a partir do lago dos outros tres, e o muro mais perto dali esta a 15 tiles.
		// O corpo da bancada anda ~35 px/s, entao a viagem custa quinze segundos -- e o nado cobra Ki
		// por segundo. As duas rodadas terminaram iguais: "voce e levado pra margem" (a exaustao do
		// `TickDoNado`) antes de o muro aparecer na tela. Um teste que nao chega no assunto nao mede
		// o assunto.
		//
		// SAO QUATRO FLAGS E NAO UMA COM ARGUMENTO porque elas medem coisas OPOSTAS: uma quer o corpo
		// seco (a agua tem que barrar), outra o quer molhado (o nado tem que andar), a terceira o quer
		// no ar e a quarta o quer colado num muro. Uma flag so obrigaria a bancada a adivinhar qual
		// roteiro rodar.
		//
		// O CLIENTE NAO GANHA FLAG NENHUMA: ele descobre o roteiro perguntando ao mundo onde o corpo
		// nasceu (ver `RoboDeAgua`, passo 0). Duas flags -- uma de cada lado -- poderiam discordar.
		_aguaDentro = Array.IndexOf(args, "--aguadentro") >= 0;
		_aguaNoAr = Array.IndexOf(args, "--aguanoar") >= 0;
		_aguaParede = Array.IndexOf(args, "--aguaparede") >= 0;
		_aguaDeTeste = _aguaDentro || _aguaNoAr || _aguaParede || Array.IndexOf(args, "--aguateste") >= 0;
		if (_aguaDeTeste)
			GD.Print("[server] BANCADA DA AGUA: quem entrar nasce "
					 + (_aguaParede ? "na AGUA colado num MURO"
						: _aguaNoAr ? "NO AR por cima do lago"
						: _aguaDentro ? "DENTRO do lago" : "na BEIRA do lago"));

		// `--formasteste`: sobe a escada inteira no primeiro que entrar e confere o BP degrau a
		// degrau. Ver GameServer.FormasTeste.cs -- ela MEXE no personagem, entao so com a flag.
		_formasDeTeste = Array.IndexOf(args, "--formasteste") >= 0;
		if (_formasDeTeste) GD.Print("[server] BANCADA: escada de formas sera exercitada no 1o login");

		// `--frostteste`: a escada do FROST DEMON -- repouso, supressoes, evolucoes e o motor do
		// Mutante (fusivel, ki travado, vazamento, bateria). Ver GameServer.FrostTeste.cs.
		//
		// SEPARADA DA `--formasteste` de proposito: ela TROCA A RACA do personagem vivo (e a raca
		// muda a escada inteira), entao rodar as duas no mesmo corpo faria uma bancada medir o
		// estranho da outra -- que e o modo de falha que o cabecalho da `--formasteste` ja descreve.
		_frostDeTeste = Array.IndexOf(args, "--frostteste") >= 0;
		if (_frostDeTeste) GD.Print("[server] BANCADA: escada do Frost Demon no 1o login");

		// `--npcteste`: sorteia fichas de NPC, poe corpos no mundo e exercita o roteiro dos chefes.
		//
		// Ela SPAWNA e REMOVE corpos de verdade (e machuca os dois Freezas de propositoo), entao so
		// roda com a flag. O que ela prova nao cabe numa bancada de Core: que a ficha vira corpo no
		// `_players` e na lista da zona, que a guarda do "tem a forma e nao a usa" esta no FUNIL
		// (`Transformar`) e nao num comentario, e que o roteiro avanca um degrau por rajada.
		_npcDeTeste = Array.IndexOf(args, "--npcteste") >= 0;
		if (_npcDeTeste) GD.Print("[server] BANCADA: fichas de NPC e roteiro de chefe no 1o login");

		// `--povoteste`: o POVOAMENTO -- quem nasce, onde, quantos, e quem NAO nasce.
		//
		// Ela deixa o mundo nascer inteiro pelo `TickDoPovoamento` de producao e entao mede as
		// quatro coisas que so em jogo aparecem: que nenhum inimigo comum nasce (e que a recusa e o
		// TIPO), que o cidadao nao caca ninguem mas revida quem bate, que a raca vem do berco e nao
		// da tabela do menu, e que o teto de lotacao DISPARA. Mais o mob-zumbi por 5400 tiques.
		_povoDeTeste = Array.IndexOf(args, "--povoteste") >= 0;
		if (_povoDeTeste) GD.Print("[server] BANCADA: povoamento de cidadaos no 1o login");

		// `--iateste`: o CORPO da IA -- voar, aparar, carregar, transformar.
		//
		// Ela nao mede se o NPC "faz" cada gesto (isso um teste de mesa mediria); mede se ele
		// **PAGA** -- o Ki da decolagem, o dreno por segundo, o custo da guarda por golpe aparado, a
		// corrida a 2% do tanque por segundo -- comparando com a MESMA formula que cobra do jogador.
		// E ela mede o que faz parecer gente e da pra medir: variancia da reacao, piso de 100 ms, e
		// que o plano nao troca a cada quadro.
		_iaDeTeste = Array.IndexOf(args, "--iateste") >= 0;
		if (_iaDeTeste) GD.Print("[server] BANCADA: corpo da IA (voo, guarda, carga, forma) no 1o login");

		// `--ligadosteste`: OS CINCO SISTEMAS QUE ESTAVAM ESCRITOS E SEM CHAMADOR -- exp por evento,
		// marco de ascensao, ganho de voo, morte de velhice e genoma do filho.
		//
		// Ela nao confere se o `Core` calcula certo: confere se alguem AINDA CHAMA. Cada conferencia
		// atravessa o funil de producao (`TickDoVoo`, `Treinar`, `EnvelhecerNaSala`, `BasicBlast`),
		// de modo que apagar a linha que liga o sistema reprova a bancada. Ver o cabecalho do
		// `GameServer.LigadosTeste.cs`.
		_ligadosDeTeste = Array.IndexOf(args, "--ligadosteste") >= 0;
		if (_ligadosDeTeste) GD.Print("[server] BANCADA: os cinco sistemas ligados no 1o login");

		// `--kideponta`: o SISTEMA DE KI de ponta a ponta -- tabela de pontos por CONJUNTO, dano
		// contra a conta do `objects.dm`, os tres tipos, a colisao nos dois sentidos, o teto de
		// tiros e o save velho. Ela e a metade de SERVIDOR; a metade viva (conta nova pelo fio,
		// cliente mentindo, relogin) e o `--diagki` do cliente, no mesmo processo.
		//
		// No 1o login e nao no boot: ela precisa que o robo ja esteja dentro, e deixa o boneco de
		// pe na zona DELE. Ver `GameServer.KiDePontaTeste.cs`.
		_pontaLigada = _pontaDeTeste = Array.IndexOf(args, "--kideponta") >= 0;
		if (_pontaDeTeste) GD.Print("[server] BANCADA: o sistema de ki de ponta a ponta no 1o login");

		// `--sagateste`: a CADEIA de eventos de chefe -- ordem, BP pinado, dias in-game, recompensa,
		// save e o gancho (inerte) da destruicao de planeta.
		//
		// Ela dispara sagas de verdade, poe chefes no mundo e ESCREVE no `sagas.json` e no
		// `reputacao.json`; tudo o que ela mexe e devolvido no fim. So com a flag, por isso.
		_sagaDeTeste = Array.IndexOf(args, "--sagateste") >= 0;
		if (_sagaDeTeste) GD.Print("[server] BANCADA: cadeia de sagas no 1o login");

		// `--genteteste`: QUEM E GENTE PRA REGRA DE MUNDO. A acusacao do dono ("se algum NPC passa do
		// minimo ele ATIVA A SAGA") virada em bancada, com o defeito INJETADO em cada familia --
		// marco de BP, cadeia, jogador possuido, clone/boneco, ultimato, caca do chefe, DNA, niveis e
		// o topo do servidor. Ver GameServer.GenteTeste.cs.
		//
		// Ela dispara sagas, poe chefes e cidadaos no mundo, entra na mente, possui o corpo do jogador
		// e ergue um laboratorio; tudo o que ela mexe e devolvido no fim.
		_genteDeTeste = Array.IndexOf(args, "--genteteste") >= 0;
		if (_genteDeTeste) GD.Print("[server] BANCADA: quem e gente pra regra de mundo, no 1o login");

		// `--bioteste`: O ANDROIDE E O BIO-ANDROIDE, de ponta a ponta -- mainframe, laboratorio,
		// agulha, gestacao, NASCIMENTO, larva, escada por absorcao, Super Perfeita e o SSJ2 pela
		// morte. Ela dirige os VERBOS (`ComandoDeTech` e `UsarHabilidade`) e nao as funcoes, porque
		// a pergunta desta familia e "um jogador chega nisso jogando?". Ver `GameServer.BioTeste.cs`.
		_bioDeTeste = Array.IndexOf(args, "--bioteste") >= 0;
		if (_bioDeTeste) GD.Print("[server] BANCADA: androide e bio-androide, no 1o login");

		// `--biovivo`: O PALCO DA FOTO da escada do bio-androide. Um corpo nasce ao lado de quem
		// entrar e sobe os sete degraus de oito em oito segundos, pelas portas de producao, pra o
		// robo `--diagbio` fotografar cada um. Ver GameServer.BioPalco.cs.
		_bioVivo = Array.IndexOf(args, "--biovivo") >= 0;
		if (_bioVivo) GD.Print("[server] PALCO: a escada do bio-androide ao lado de quem entrar (pra foto)");

		// `--bioolhar`: O PALCO DOS TRES PEDIDOS VISUAIS -- os olhos da larva, a cinematica com o
		// corpo brilhando e a morte que vira Super Saiyajin 2. Tres corpos, e a diferenca entre dois
		// deles e UM campo. Ver GameServer.BioOlhar.cs.
		_bioOlhar = Array.IndexOf(args, "--bioolhar") >= 0;
		if (_bioOlhar) GD.Print("[server] PALCO: os tres pedidos visuais do bio-androide (pra foto)");

		// `--biofilme`: O PALCO DO **FILME** da cinematica. Quatro corpos: o bio que roda a metamorfose
		// inteira, um Saiyajin que vira Oozaru (a prova de que a regra da virada nao e um `if` de bio),
		// um bio que leva nocaute no meio da propria cena e um bio pra o cliente rodar com o defeito
		// injetado. Ver GameServer.BioFilme.cs.
		_bioFilme = Array.IndexOf(args, "--biofilme") >= 0;
		if (_bioFilme) GD.Print("[server] PALCO: a cinematica quadro a quadro (pra o `--diagfilme`)");

		// `--alemteste`: A MORTE, O OUTRO MUNDO E A AUREOLA -- o pedido do dono, medido em execucao.
		//
		// Ela **mata o host de verdade**, e e o unico jeito: um corpo forjado nao tem `Peer` e a
		// triagem da morte o recusa por desenho (que e uma das familias). Zona, posicao, corpo, Ki e
		// os dois relogios sao fotografados e devolvidos no `finally`. Ver GameServer.AlemTeste.cs.
		_alemDeTeste = Array.IndexOf(args, "--alemteste") >= 0;
		if (_alemDeTeste) GD.Print("[server] BANCADA: a morte e o Outro Mundo, no 1o login");

		// `--cadaverteste`: O CORPO QUE FICA -- o cadaver do `Corpse.dm`, medido em execucao.
		//
		// Ela e a IRMA da `--alemteste` e mede o outro objeto da mesma funcao do DM: la o mob que VIAJA,
		// aqui o cadaver que FICA. Mata o host pelo mesmo motivo (a triagem recusa corpo sem `Peer`) e
		// devolve tudo no `finally`, inclusive as lapides que ergueu. Ver GameServer.CadaverTeste.cs.
		_cadaverDeTeste = Array.IndexOf(args, "--cadaverteste") >= 0;
		if (_cadaverDeTeste) GD.Print("[server] BANCADA: o corpo que fica, no 1o login");

		// `--doiscorposteste`: OS TRES PEDIDOS DO DONO MEDIDOS COM **DOIS CORPOS** -- agarrao, colisao
		// e o cadaver, cada familia nos dois sentidos e as oito afirmacoes centrais com o defeito
		// INJETADO (o mesmo `Mutacao` da `--provateste`).
		//
		// Ela e a que faltava: a `--cadaverteste` prende e arremessa contra corpos que nunca andam, a
		// `--corpo` mede a grade em memoria sem servidor, e nenhuma das duas jamais teve dois corpos
		// vivos se encontrando. Ver GameServer.DoisCorposTeste.cs.
		_doisCorposDeTeste = Array.IndexOf(args, "--doiscorposteste") >= 0;
		if (_doisCorposDeTeste) GD.Print("[server] BANCADA: dois corpos (agarrao, colisao, cadaver), no 1o login");

		// `--tiquedamorte`: O QUADRO INTEIRO NO INSTANTE DA MORTE -- e a bancada que faltava embaixo
		// das outras tres.
		//
		// A `--alemteste`, a `--cadaverteste` e a `--velorio` medem O QUE a morte produz (a viagem, a
		// aureola, o corpo que fica) chamando **um subsistema por vez**, na mao. Nenhuma delas roda o
		// `Tick()` -- e por isso as tres ficaram verdes durante todo o tempo em que **cada morte de
		// jogador derrubava o tique inteiro do servidor** (`Collection was modified`, ver o comentario
		// grande do `TickCombate`). Esta aqui mata alguem a soco e pergunta o que aconteceu com o
		// QUADRO: o corpo no ar caiu, o tiro andou, a ferida sincronizou, e a ultima linha do tique
		// rodou. Ver GameServer.TiqueDaMorteTeste.cs.
		_tiqueDaMorteDeTeste = Array.IndexOf(args, "--tiquedamorte") >= 0;
		if (_tiqueDaMorteDeTeste) GD.Print("[server] BANCADA: o quadro inteiro no instante da morte, no 1o login");

		_planetaDeTeste = Array.IndexOf(args, "--planetateste") >= 0;
		if (_planetaDeTeste) GD.Print("[server] BANCADA: destruicao de planeta no 1o login");

		// `--destrocosvivos`: o RESCALDO com dois clientes de VERDADE. A `--planetateste` prova o
		// relogio do rescaldo sem desenhar nada e a `--diagagonia` prova o pixel num processo so; a
		// palavra que o dono grifou -- *"ele vai sumir do espaco pra TODOS os jogadores (server
		// sync)"* -- so tem sentido com dois processos. Ver `GameServer.DestrocosVivosTeste.cs`.
		//
		// O ARGUMENTO DA FLAG E DO CLIENTE (`--destrocos a|b` = o papel); o servidor e um so e nao
		// tem papel, entao aqui basta a presenca.
		_destrocosVivosLigados = Array.IndexOf(args, "--destrocosvivos") >= 0;
		if (_destrocosVivosLigados)
			GD.Print("[server] BANCADA: o rescaldo sera medido quando os dois clientes entrarem");

		// `--provateste`: as SETE perguntas do dono sobre NPC e saga, cada uma com o defeito INJETADO.
		//
		// Ela nao repete o que a `--povoteste` e a `--sagateste` afirmam: ela prova que aquelas
		// afirmacoes SABEM ficar vermelhas. Cada familia mede o codigo de producao com um criterio
		// nomeado, injeta um defeito de verdade (dado torto, regra desligada, um segundo chamador que
		// pula o portao, o save mutilado), exige que o MESMO criterio reprove, desfaz e exige que ele
		// volte a passar. Ver `GameServer.ProvaTeste.cs`.
		//
		// Ela ESTRAGA o mundo de proposito -- inclusive o `sagas.json` -- e devolve tudo no fim. So com
		// a flag, e de preferencia em conta e porta proprias.
		_provaDeTeste = Array.IndexOf(args, "--provateste") >= 0;
		if (_provaDeTeste) GD.Print("[server] BANCADA: as sete familias com defeito injetado no 1o login");

		// `--conquistateste`: a CONQUISTA DE PLANETAS -- chave, invasao por ondas, contestacao,
		// tributo, e o "manter custa" (que nao existe no DM e por isso precisa provar que dispara).
		//
		// Ela finca dominios de verdade, poe defensores no mundo, escreve no `conquista.json` e no
		// `reputacao.json`, e DESTROI um planeta gerado pra ver o dominio cair junto. Tudo o que ela
		// mexe e devolvido no fim. So com a flag, e de preferencia em conta e porta proprias.
		_conquistaDeTeste = Array.IndexOf(args, "--conquistateste") >= 0;
		if (_conquistaDeTeste) GD.Print("[server] BANCADA: conquista de planetas no 1o login");

		// `--esferateste`: AS ESFERAS DO DRAGAO -- o espalhamento deterministico, a espera de
		// nascimento, a policia de planeta, a invocacao, e as Super Esferas com o claim e a disputa.
		//
		// Ela ergue estatuas de verdade, nocauteia o proprio corpo do testador, adianta o relogio do
		// ceu e escreve no `esferas.json` e no `superesferas.json`. Tudo o que ela mexe e devolvido no
		// fim. So com a flag, e de preferencia em conta e porta proprias.
		_esferaDeTeste = Array.IndexOf(args, "--esferateste") >= 0;
		if (_esferaDeTeste) GD.Print("[server] BANCADA: esferas do dragao no 1o login");

		// `--desejoteste`: A TABELA DE DESEJOS, A LINGUA DOS DEUSES E O PROCURADOR -- a Fase 2.
		//
		// Separada da `--esferateste` de proposito: aquela mede o CORPO do sistema (onde as esferas
		// caem, quem as pega, quando acordam) e esta mede o que ele FAZ. Juntas seriam duzentas
		// checagens num console so, e a primeira coisa a acontecer com um relatorio assim e ninguem
		// ler o meio dele.
		//
		// Ela mata e ressuscita gente, forja corpos, troca a raca do testador, mexe nos tronos e
		// escreve nos dois arquivos. Tudo o que ela mexe e devolvido no fim.
		_desejoDeTeste = Array.IndexOf(args, "--desejoteste") >= 0;
		if (_desejoDeTeste) GD.Print("[server] BANCADA: desejos + lingua dos deuses + procurador no 1o login");

		// `--porungateste`: O SET DE ESFERAS QUE MORRE COM O PLANETA -- o pedido do dono ("porunga
		// morre em namek quando namek explode, so voltando quando o planeta e restaurado pelas esferas
		// de outro lugar").
		//
		// Ela e a TERCEIRA do sistema e nao cabe em nenhuma das duas anteriores: a `--esferateste` mede
		// o corpo do sistema e a `--desejoteste` mede o que ele faz -- esta mede o que a MORTE DE UM
		// MUNDO faz com ele, e por isso ela e a unica que destroi planetas de verdade pelo commit de
		// producao e recarrega o `esferas.json` do disco no meio da medicao.
		//
		// Ela destroi Namek e Arlia (dentro de um `PalcoDeMortes`), desvia o disco inteiro pra uma pasta
		// temporaria (`PalcoDeApagamentos`), finca um dominio, troca raca/classe/BP do testador e toma
		// o trono de Guardiao emprestado. Tudo volta no fim.
		_porungaDeTeste = Array.IndexOf(args, "--porungateste") >= 0;
		if (_porungaDeTeste) GD.Print("[server] BANCADA: o set de esferas que morre com o planeta, no 1o login");

		// `--avessoteste`: A CORRENTE INTEIRA E O PROCURADOR SOB ATAQUE -- a Fase 3.
		//
		// Ela existe porque as duas de cima tem o MESMO cego: **nascem dentro do estado**. A da Fase 2
		// forja um jogador ja com as sete Super Esferas na mao e teleporta as sete comuns pro colo de
		// quem vai pedir -- entao nenhuma das duas jamais testou ACHAR, PEGAR nem REIVINDICAR.
		//
		// Esta atravessa tudo so por verbo de producao (erguer -> radar -> viajar -> pegar -> invocar ->
		// pedir -> as sete sumirem; e depois achar -> reivindicar -> disputar -> a lingua recusar ->
		// emprestar a voz -> o desejo cair em quem PEDIU), e e o alvo das cinco injecoes de
		// CODIGO-FONTE da Fase 3. Ver `GameServer.AvessoTeste.cs`.
		_avessoDeTeste = Array.IndexOf(args, "--avessoteste") >= 0;
		if (_avessoDeTeste) GD.Print("[server] BANCADA: a corrente inteira + o procurador sob ataque no 1o login");

		// A DO EMBARALHO mede as DUAS metades do "sem plateia, sem mente": que a mente para num
		// planeta sem ninguem (com o controle ao lado, que e o mesmo laco com o host na zona) e que
		// os habitantes mudam de lugar quando o planeta esfria. Ver `GameServer.EmbaralhoTeste.cs`.
		_embaralhoDeTeste = Array.IndexOf(args, "--embaralhoteste") >= 0;
		if (_embaralhoDeTeste) GD.Print("[server] BANCADA: congelamento sem plateia + embaralho no 1o login");

		// A DA LUA E DA FERA mede o gatilho novo: NPC Saiyajin lutando, ferido grave, debaixo de lua
		// cheia, virando Oozaru pelo funil do jogador -- mais a maestria sorteada, o rabo como portao
		// legitimo e a VOLTA (o `DevolverAsRedeas` que apagava a mente do NPC).
		// Ver `GameServer.LuaFeraTeste.cs`.
		_luaFeraDeTeste = Array.IndexOf(args, "--luaferateste") >= 0;
		if (_luaFeraDeTeste) GD.Print("[server] BANCADA: a lua cheia pega os NPCs Saiyajins, no 1o login");

		// `--macacovivo`: o PALCO da foto. Nasce um Saiyajin ao lado de quem entrar, poe a Terra em lua
		// cheia e abre o corpo dele dez segundos depois -- e quem transforma e o `TickDoCeu` de verdade.
		// So faz sentido com janela e com o `--diagmacaco` do outro lado. Ver `GameServer.LuaFeraTeste.cs`.
		_macacoVivo = Array.IndexOf(args, "--macacovivo") >= 0;
		if (_macacoVivo) GD.Print("[server] PALCO: um Saiyajin vira Oozaru ao lado de quem entrar (pra foto)");

		// `--agoniaviva`: o PALCO DA MORTE DE UM PLANETA. Mata o mundo em que a pessoa entrou, pela
		// porta de producao, e segura a agonia em cinco patamares pra o `--diagchao` do outro lado
		// fotografar cinco instantes dos cinco minutos e medir o custo no pico. Morte so na memoria
		// (`PalcoDeMortes`). Ver `GameServer.AgoniaViva.cs`.
		_agoniaViva = Array.IndexOf(args, "--agoniaviva") >= 0;
		if (_agoniaViva) GD.Print("[server] PALCO: o planeta de quem entrar vai MORRER (pra foto); "
								+ "a morte acontece so na memoria");

		// `--diagia`: por que a IA decidiu o que decidiu. Uma linha por TROCA de plano.
		_diagIa = Array.IndexOf(args, "--diagia") >= 0;
		if (_diagIa) GD.Print("[server] DIAG: toda troca de plano da IA sai no console");

		// `--geradoteste`: quem entrar nasce num planeta GERADO em vez da Terra.
		//
		// Existe porque o planeta procedural mais proximo fica a ~39 chunks da Terra -- viajar ate
		// la leva horas in-game, e um caminho que so da pra testar depois de horas nao e testado.
		// Isto pula a VIAGEM, nao a geracao: o pouso, a colisao e a pintura sao os mesmos.
		if (Array.IndexOf(args, "--geradoteste") >= 0)
		{
			_nascerEmGerado = true;
			GD.Print("[server] BANCADA: todo mundo nasce num planeta gerado");
		}

		// `--socoteste`: nasce encostado no cenario mais proximo, virado pra ele. Ver `EncostarNaParede`.
		if (Array.IndexOf(args, "--socoteste") >= 0)
		{
			_nascerNaParede = true;
			GD.Print("[server] BANCADA: todo mundo nasce colado numa parede");
		}

		// `--horateste` e `--luateste`: adiantam o relogio do mundo. Ver GameServer.Ceu.cs -- a
		// lua cheia da Terra so volta a cada oito noites de 24 min, ou seja, mais de tres horas.
		LerBancadaDoCeu(args);

		// `--climateste <tipo>`: trava o ceu num clima. Ver GameServer.Clima.cs.
		LerBancadaDoClima(args);

		if (Array.IndexOf(args, "--server") < 0) return;   // processo de cliente

		int port = Protocol.DefaultPort;
		int idx = Array.IndexOf(args, "--port");
		if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out int p)) port = p;
		Start(port);
	}

	/// <summary>
	/// Sobe o servidor NESTE processo. Serve tanto pro servidor dedicado (`--server`) quanto
	/// pro jogador que quer hospedar a propria partida sem abrir outro programa -- e o mesmo
	/// codigo, so muda quem chama. Quem hospeda depois conecta em 127.0.0.1 como qualquer um.
	/// </summary>
	public bool Start(int port = Protocol.DefaultPort)
	{
		if (Running) return true;

		if (!_carregado)
		{
			CarregarZonas();
			CarregarRacas();
			CarregarVisual();
			Wire();
			InscreverEsquecimentos();
			_carregado = true;

			// `--diagberco`: a bancada do BERCO, e ela roda no BOOT e nao no primeiro login.
			//
			// Ela e a unica do projeto que nao precisa de ninguem em jogo, porque o que ela mede e
			// uma funcao pura mais o catalogo de zonas -- e as duas coisas ja estao prontas nesta
			// linha. Esperar um jogador so atrasaria a resposta e obrigaria a bancada a mexer num
			// corpo de verdade pra medir onde ele nasceria.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--diagberco") >= 0) RodarBancadaDeBerco();

			// `--bercovivo`: a IRMA da de cima, e ela poe CORPO no mundo e POUSA.
			//
			// Tambem no boot, e por um motivo que a de cima nao tinha: ela precisa de zonas, racas,
			// catalogo de planetas e do `AccountStore` -- e as quatro coisas acabaram de carregar
			// nestas linhas --, mas NAO precisa de ninguem logado. Os corpos dela nascem sem `Peer`,
			// entao nenhum pacote sai no fio e nenhum jogador de verdade e tocado. Poe-la no primeiro
			// login (como a do sol e a da IA) obrigaria um cliente a existir pra testar uma regra que
			// e do servidor sozinho -- e ninguem roda uma bancada que precisa de duas janelas.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--bercovivo") >= 0) RodarBancadaDoBercoVivo();

			// `--bercoprova`: a TERCEIRA irma -- a que poe o mundo em cada estado que ja quebrou o
			// jogo e exige o placar certo em cada um (a Terra morta, Namek morta, uma zona nova na
			// frente da carta), inclusive no povoamento e no renascimento.
			//
			// DEPOIS da `--bercovivo` de proposito: as duas compartilham o forjador de corpos
			// (`SaveDeBancada`/`CorpoDoSave`) e a `--bercoprova` mata planetas dentro do palco de
			// bancada. Rodar a que ESTRAGA o mundo antes da que so o observa deixaria a segunda
			// medindo um estado que a primeira montou, mesmo com o palco desfazendo tudo.
			//
			// Ela mata planetas -- SEMPRE dentro do `PalcoDeMortesDeBancada`, entao o
			// `planetas-mortos.json` do dono nao e tocado. Ver `GameServer.BercoProva.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--bercoprova") >= 0) RodarBancadaDaProvaDoBerco();

			// `--obrateste`: A CHAVE DE ZONA DAS CONSTRUCOES -- o disco, e nao a memoria.
			//
			// No boot, e num momento MUITO especifico: depois do `CarregarTech`, porque ela fotografa
			// o `_noChao` de verdade e o devolve no fim. Ela nao precisa de zona carregada nem de
			// ninguem logado -- o que ela prova acontece inteiro entre a lista e o arquivo, e o
			// arquivo dela e um temporario proprio (o `mundo.json` de verdade nunca e tocado).
			// Ver `GameServer.ObraTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--obrateste") >= 0) RodarBancadaDasObras();

			// `--projetilteste`: os ataques de ki. No boot, e pelo mesmo motivo da `--bercovivo`:
			// ela precisa das zonas com colisao (a familia do cenario mede contra a parede da
			// Terra) mas nao precisa de ninguem logado -- os corpos dela nascem sem `Peer`, entao
			// nenhum byte sai no fio. Ver `GameServer.ProjeteisTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--projetilteste") >= 0) RodarBancadaDeProjeteis();

			// `--pecateste`: O MEMBRO QUE CAI DO CORPO. Dois corpos brigam pelo `Atacar` de producao
			// e a bancada le os DOIS pacotes que saem da amputacao -- o `S2C.Hit` (de onde o jato de
			// sangue nasce no cliente) e o `S2C.Decalque` (de onde a peca no chao nasce). No boot
			// pelos mesmos motivos da `--projetilteste`, cuja infraestrutura ela empresta: precisa de
			// zona com colisao e de ninguem logado. Ver `GameServer.PecaTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--pecateste") >= 0) RodarBancadaDaPeca();

			// `--arsenalteste`: AS CATORZE FOLHAS DO LOTE G5 (o arsenal de ki nomeado). No boot pelos
			// mesmos motivos da de cima -- ela empresta a infraestrutura da `--projetilteste` (mesmo
			// forjador, mesma zona da Terra, mesmo corredor livre), entao precisa exatamente do que
			// aquela precisa e de nada mais. Ver `GameServer.ArsenalTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--arsenalteste") >= 0) RodarBancadaDoArsenal();

			// `--punhoteste`: OS DEZESSEIS DO LOTE G7 (os punhos nomeados + a Bala Dispersa e o
			// Spirit Gun). Mesma infraestrutura da `--arsenalteste`, pelos mesmos motivos. A familia
			// 1 dela e a mais importante do arquivo: ela afirma que as NOVE tecnicas que o lote
			// recusou continuam MUDAS -- e uma tecnica meio-portada nao aparece em lugar nenhum.
			// Ver `GameServer.PunhoTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--punhoteste") >= 0) RodarBancadaDoPunho();

			// `--g10teste`: OS VINTE DO LOTE G10 (os golpes do molde do G7 que o censo achou mudos + a
			// Trindade). Mesma infraestrutura da `--punhoteste`. Ver `GameServer.G10Teste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--g10teste") >= 0) RodarBancadaG10();

			// `--g12teste`: OS ONZE VERBOS E A PASSIVA DO LOTE G12. Mesma infraestrutura da
			// `--arsenalteste`, pelos mesmos motivos. Ver `GameServer.G12Teste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--g12teste") >= 0) RodarBancadaG12();

			// `--g11teste`: AS SKILLS QUE JA ESTAVAM NA ARVORE SEM EFEITO (lote G11). Mesma
			// infraestrutura da `--arsenalteste` (corpos forjados na Terra, ninguem logado); a familia
			// dos astros mexe no relogio do ceu e o devolve no fim. Ver `GameServer.G11Teste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--g11teste") >= 0) RodarBancadaG11();

			// `--tecnicateste`: a criacao de tecnicas de ki. No boot pelos mesmos dois motivos da de
			// cima -- ela precisa de zonas com colisao (a familia 5 dispara de verdade e o tiro tem
			// que ter chao) e do `AccountStore` (a familia 4 grava e le), mas nao precisa de ninguem
			// logado. Ver `GameServer.CustomizadasTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--tecnicateste") >= 0) RodarBancadaDeTecnicas();

			// `--embatekiteste`: a COLISAO DE KI. No boot pelos mesmos motivos das duas de cima -- ela
			// precisa das zonas com colisao (os raios sao disparados no chao da Terra e o encontro
			// racha esse chao) e nao precisa de ninguem logado. Ver `GameServer.EmbateDeKiTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--embatekiteste") >= 0) RodarBancadaDeEmbateDeKi();

			// `--pressateste`: SER RAPIDO PAGA? -- o segundo pedido do dono, nos DOIS embates, com o
			// metronomo de antes injetado em cada familia. No boot como as de cima (precisa de zona
			// com colisao pros socos e pros raios, nao precisa de ninguem logado), mas repare que ela
			// gasta relogio de PAREDE: o embate de velocidade conta tudo em `NowMs()`, entao ela leva
			// ~1 minuto. Ver `GameServer.PressaTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--pressateste") >= 0) RodarBancadaDaPressa();

			// `--escudoteste`: O ESCUDO DA CINEMATICA DE TRANSFORMACAO -- uma linha por FONTE de dano.
			//
			// No boot pelos mesmos motivos das de cima (zonas com colisao pro tiro que voa, moldes do
			// `npcs.json` pro corpo com `Papel`, catalogo de formas pra cena mais longa) e sem precisar
			// de ninguem logado: o escudo nao olha `Peer` em linha nenhuma.
			//
			// Ela DESTROI Arlia tres vezes, leva a TERRA numa Final Explosion de raio maximo e cozinha
			// corpos dentro de uma estrela -- tudo com corpos forjados que entram e saem dentro do mesmo
			// bloco sincrono, e **sem escrever uma linha no registro de planetas mortos**: quem garante
			// isso e o `PalcoDeMortes` (`GameServer.Destruicao.cs`), um escopo, e nao o cuidado de quem
			// escreveu a fonte. Foi essa promessa quebrada -- a Terra ficando morta no save do dono --
			// que fez 13 racas nascerem em Namek. Ver `GameServer.EscudoTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--escudoteste") >= 0) RodarBancadaDoEscudo();

			// `--tresteste`: OS TRES RELATOS DO DONO (soco no vazio depois do soco forte, NPC que so
			// anda, clone sem Zanzo Clash). No boot pelos mesmos motivos das de cima -- ela forja
			// corpos na Terra (precisa da colisao carregada, pro arranque de quinze tiles ter um
			// corredor livre de verdade) e nao olha `Peer` em linha nenhuma. Ver
			// `GameServer.TresRelatosTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--tresteste") >= 0) RodarBancadaDosTresRelatos();

			// `--kbteste`: O SORTEIO DO ARREMESSO -- a queixa "era UM JOGANDO O OUTRO PRA LONGE".
			// Mesma infraestrutura da `--tresteste` (corredor livre na Terra, corpos forjados) e
			// pelos mesmos motivos: ela soca milhares de vezes pelo funil de producao e le o
			// resultado no CORPO do outro, sem olhar `Peer` em linha nenhuma. Ver
			// `GameServer.KbTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--kbteste") >= 0) RodarBancadaDoArremesso();

			// `--borraoteste`: O DASH DO NPC -- alcance e borrao, SO MEDIDOS. Mesma infraestrutura da
			// `--tresteste` (corredor livre na Terra, corpos forjados, laco a 30 Hz no relogio de
			// parede) mais o CATALOGO DE MOLDES, que ela le pra amostrar o sorteio de producao -- e
			// por isso ela vem depois do `CarregarMoldes`, como a `--tiroiateste`. Ver
			// `GameServer.BorraoTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--borraoteste") >= 0) RodarBancadaDoBorrao();

			// `--tiroiateste`: O TIRO DA IA -- do molde do `npcs.json` ate o projetil na zona.
			//
			// No boot pelos mesmos motivos das tres de cima (zonas com colisao, ninguem logado), e
			// com um a mais que so ela tem: ela le o CATALOGO DE MOLDES, e ele so existe depois do
			// `CarregarMoldes` -- que roda nesta mesma sequencia, algumas linhas acima. Rodada no
			// primeiro login (como a `--iateste`), a resposta seria a mesma e exigiria um cliente.
			// Ver `GameServer.TiroDaIaTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--tiroiateste") >= 0) RodarBancadaDoTiroDaIa();

			// `--mestreteste`: MESTRE E ALUNO. No boot pelos mesmos motivos das de cima -- ela forja
			// corpos e precisa das zonas pre-feitas pra por os dois num planeta onde nao ha ninguem
			// conectado, mas nao precisa de cliente nenhum: o discipulado inteiro e do servidor.
			//
			// E ela e a UNICA que mexe num arquivo do mundo (`mestres.txt`) -- por isso fotografa e
			// devolve os vinculos de verdade no `finally`. Ver `GameServer.MestreTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--mestreteste") >= 0) RodarBancadaDoMestre();

			// `--cenafusaoteste`: A VIRADA DA FUSAO -- *"a fusao so EXISTE no fim da cena"*, e a cena
			// interrompida que nao pode deixar meio-corpo. No boot pelos mesmos motivos da de cima:
			// ela forja dois corpos e precisa das zonas pre-feitas pra por os dois num planeta onde
			// nao ha ninguem conectado (a cena manda pacote pra `ZoneList` inteira, e bancada nao
			// dispara cinematica na tela de quem esta jogando).
			//
			// A metade do DESENHO -- a luz sobre os dois, as ondas, a pedra, o branco que escoa -- e
			// do cliente e tem bancada propria: `--diagcenafusao`. Ver `GameServer.CenaDeFusaoTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--cenafusaoteste") >= 0) RodarBancadaDaCenaDeFusao();

			// `--fusaoduplateste`: A FUSAO ENTRE **DOIS JOGADORES**, de ponta a ponta -- o convite, o
			// aceite, as letras da danca, a cena, a virada, a heranca, os nomes, a energia e as
			// bordas. As outras tres bancadas da fusao medem pedaco: a `--diagfusaolook` mede funcoes
			// puras, a `--diagcenafusao` mede o desenho, e a `--cenafusaoteste` chama o
			// `ComecarACenaDaFusao` na mao (ou seja, pula justamente o convite e o quick time event).
			//
			// No boot pelos mesmos motivos da de cima: ela forja SEIS pares de corpos e precisa das
			// zonas pre-feitas pra por todos num planeta onde nao ha ninguem conectado -- a cena manda
			// pacote pra `ZoneList` inteira. Ver `GameServer.FusaoDuplaTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--fusaoduplateste") >= 0) RodarBancadaDaFusaoDupla();

			// `--ensinoteste`: O ENSINO DE SKILL -- o OUTRO sistema de mestre e aluno
			// (`teachable.dm`), que no DM nao toca no discipulado em linha nenhuma.
			//
			// No boot pelos mesmos motivos, e com um requisito proprio: ela le o CATALOGO DE SKILLS,
			// que carregou algumas linhas acima -- sem ele nao ha o que ensinar, e a bancada diz
			// isso em voz alta em vez de passar com zero checagens. Nao mexe em arquivo de mundo
			// nenhum: o que o ensino muda mora no livro de cada personagem.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--ensinoteste") >= 0) RodarBancadaDoEnsino();

			// `--menteteste`: A DIMENSAO MENTAL -- as tres regras do pedido do dono, cada uma no funil
			// dela, mais o visitante e o chefe convocado.
			//
			// No boot como as duas de cima, e com um requisito proprio: ela faz NASCER um chefe pelo
			// caminho de producao, entao precisa do `npcs.json` ja carregado (`CarregarMoldes`, la em
			// cima) -- sem ele a secao do chefe diz em voz alta que nao tinha o que medir.
			//
			// O que ela NAO cobre e a porta (`EntrarNaMente`), que exige `Peer`: quem exercita aquilo
			// e o robo, com `--socar --mente N`. Ver `GameServer.MenteTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--menteteste") >= 0) RodarBancadaDaMente();

				// `--presoteste`: OS DOIS RELATOS DO DONO -- a agua que PRENDE (em vez de teleportar) e
				// a meditacao que leva pra DIMENSAO BRANCA (em vez do z24 do BYOND).
				//
				// No boot pelos mesmos motivos das de cima, e com um requisito que so ela e a
				// `--menteteste` tem junto: ela precisa das zonas **com o plano de agua ja lido** (o
				// `.agua` entra no `CarregarZonas`, la em cima) e do CATALOGO inteiro -- a familia 3
				// injeta o defeito fazendo o catalogo resolver a mente pelo nome, e sem o z24 no
				// catalogo essa injecao nao provaria nada.
				//
				// O ESCAPE em si (zero pixel no lago, quem nasce na pedra saindo, a beira que nao
				// congela) NAO esta aqui: ele e `MoveRules` puro e mora na bancada sem janela
				// (`Tools/AssetPipeline -- agua-prova`, familias 8 a 10). Aqui ficam as tres coisas que
				// sao do SERVIDOR. Ver `GameServer.PresoTeste.cs`.
				if (Array.IndexOf(OS.GetCmdlineArgs(), "--presoteste") >= 0) RodarBancadaDoPreso();

			// `--mestrevivo`: MESTRE E ALUNO **COM PERSONAGENS DE VERDADE** -- a IRMA da
			// `--mestreteste`, na mesma divisao que a `--bercovivo` tem com a `--diagberco`.
			//
			// **A ULTIMA DAS BANCADAS DE BOOT**, e a ordem importa: ela cria contas no
			// `AccountStore`, poe SETE corpos numa zona pre-feita, grava o `mestres.txt` e o
			// RELE do disco. Rodando depois de todas as outras, nenhuma delas pega o mundo com
			// os corpos e as contas dela dentro -- e ela mesma fotografa e devolve o vinculo de
			// verdade, como a `--mestreteste` faz. Ver `GameServer.MestreVivoTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--mestrevivo") >= 0) RodarBancadaDoMestreVivo();

			// `--salateste`: A PORTA DA SALA DO TEMPO. No boot pelos mesmos motivos das de cima, e
			// com dois requisitos que so ela tem: ela le o CATALOGO DE CONSTRUCOES e as OBRAS DO
			// MAPA (a porta e mobilia do z12, e a checagem que a fase inteira existe pra fazer
			// passar e "ha uma porta de pe no Templo"), e os dois so existem depois do
			// `CarregarMundo`/`CarregarObjetosDoMapa`, que rodam nesta mesma sequencia acima.
			// Ver `GameServer.SalaTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--salateste") >= 0) RodarBancadaDaSalaDoTempo();

			// `--cargoportas`: AS PORTAS DE CARGO -- nomeacao, sucessao, duelo formal e a dadiva.
			//
			// No boot pelos mesmos motivos das de cima: ela forja corpos numa zona pre-feita e nao
			// precisa de cliente nenhum (as tres portas sao do servidor inteiras). Precisa do
			// CATALOGO DE SKILLS, que carregou nesta mesma sequencia -- sem ele nao ha dadiva pra
			// conferir, e a bancada diz isso em voz alta em vez de passar com zero checagens.
			//
			// E ela mexe em TRES arquivos do mundo (`cargos.txt`, `herdeiros.txt`, `titulo.txt`),
			// entao fotografa e devolve os tres no `finally` -- mesma precaucao da `--mestreteste`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--cargoportas") >= 0) RodarBancadaDasPortas();

			// `--cargomissoes`: OS DEVERES DE CARGO -- vocacao, prazo em dias in-game, servico,
			// renome e destituicao.
			//
			// IRMA DA `--cargoportas` e no mesmo lugar pelos mesmos motivos: corpos forjados, catalogo
			// de skills ja carregado (a destituicao tira o kit, e a bancada afirma isso), e ela mexe no
			// `cargos.txt` e no `missoes-de-cargo.json`, entao fotografa e devolve os dois no `finally`.
			//
			// O QUE SO ELA TEM: ela ADIANTA O RELOGIO DO MUNDO pela manivela do ceu (`_adiantoDoCeu`),
			// que e o unico jeito de exercer um prazo de 72 minutos e uma destituicao que jogando
			// levaria quase quatro horas. Ver `GameServer.MissoesTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--cargomissoes") >= 0) RodarBancadaDasMissoes();

			// `--censoteste`: O RELATORIO DO CATALOGO + as dezenove folhas do lote G6.
			//
			// ELA E A UNICA QUE IMPRIME UM NUMERO ANTES DE AFIRMAR QUALQUER COISA: o relatorio de
			// quantas skills tem efeito, quantas so dao verbo e quantas continuam mudas sai em toda
			// rodada, porque foi isso que a especificacao pediu como prova principal -- e porque
			// numero que so aparece quando alguem procura envelhece calado.
			//
			// DEPOIS DAS BANCADAS DE CARGO de proposito: ela le `DadivaDeCargo` pra dizer quantos
			// verbos os kits entregam vivos, e essa conta so vale com o catalogo de skills e o de
			// cargos ja carregados. Ver `GameServer.CensoTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--censoteste") >= 0) RodarBancadaDoCenso();

			// `--escolhateste`: A ESCOLHA UNICA -- a skill cujos buffs moram num `proc/choose()`.
			//
			// LOGO DEPOIS DO CENSO de proposito: e o censo que chamava a `Great Robotic Alliance` de
			// muda, e a familia 1 desta bancada afirma o contrario lendo o MESMO catalogo. As duas
			// vizinhas, e a divida que uma media a outra fecha. Ver `GameServer.EscolhaTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--escolhateste") >= 0) RodarBancadaDaEscolha();

			// `--menteskills`: AS DEZESSETE SKILLS DE "STRENGTH OF MIND", uma a uma -- alcance (quem
			// da pra comprar, e o que acende cada uma) e EFEITO (o que o corpo ganha), separados.
			//
			// VIZINHA DA `--arvoreteste` pelo mesmo motivo que a escolha unica e vizinha do censo:
			// aquela prova o MOTOR de arvores com a arvore do Corpo; esta atravessa a arvore da MENTE
			// inteira pelo mesmo funil, e ela e a unica que exercita a cadeia de tres degraus
			// (Basic 100 -> Advanced 100 -> Perfect). Ver `GameServer.MenteSkillsTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--menteskills") >= 0) RodarBancadaDasSkillsDaMente();
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--sentidosteste") >= 0) RodarBancadaDosSentidos();   // `--sentidosteste`: a aba Sense/Scan -- alcances, identidade, sigilo do BP no fio e reenvio so na mudanca; vizinha da mente porque os alcances acendem pelo contador dela. Ver `GameServer.SentidosTeste.cs`.

			// `--arvoreteste`: O TIER DE VITRINE E O `enabled` LIDO COMO O DM LE -- pelo FUNIL do
			// servidor (comprar -> efeitos -> contadores -> growbranches -> pacote), e o pacote
			// desmontado com o leitor do cliente. Vizinha da escolha unica pelo mesmo motivo que ela e
			// vizinha do censo: as tres medem o que o extrator entrega. Ver `GameServer.ArvoreTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--arvoreteste") >= 0) RodarBancadaDasArvores();

			// `--seloteste`: O SELO, O POTE, A DEAD ZONE E O SIGILO DE PODER (lote G9).
			//
			// VIZINHA DAS DUAS DE CIMA PELO MESMO MOTIVO QUE ELAS SAO VIZINHAS: as tres medem o que
			// o EXTRATOR entrega. O censo diz quantas skills continuam mudas, a escolha unica prova
			// que uma delas nao estava, e esta prova que outras TRES nao estavam -- e que duas ja
			// tem efeito. Ela le `DadivaDeCargo` na ultima familia (a frase que o painel do Eremita
			// Tartaruga mostra), entao precisa do catalogo de cargos carregado, como o censo.
			// Ver `GameServer.SeloTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--seloteste") >= 0) RodarBancadaDoSelo();

			// `--cidadeteste`: A CIDADE E O BANCO, com o servidor DE PE.
			//
			// MAIS NOVA QUE A COPIA DE 23:07, entao o lugar dela foi deduzido do proprio cabecalho da
			// bancada: ela mede o que "so existe depois do boot" -- a colisao das maquinas aplicada em
			// runtime por `AplicarColisaoDasObras` a partir do `_noChao`, o alarme do `CarregarTech` e o
			// alcance de uso sobre a posicao do CORPO. Todas as tres ja carregaram nesta sequencia, e
			// nenhuma delas precisa de cliente. AQUI e nao no login pelo mesmo argumento da
			// `--obrateste`, que e a irma de disco dela. Ver `GameServer.CidadeTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--cidadeteste") >= 0) RodarBancadaDaCidade();

			// `--curaviva`: A ATIVA DO NAMEKUSEIJIN e a passiva PELO FUNIL DO SERVIDOR.
			//
			// COLADA NA `--cidadeteste` porque as duas sao as duas metades da mesma frase do dono: a
			// familia 5 daquela mede a MAQUINA de regeneracao (a saida de quem nao tem raca pra isso)
			// e esta mede a HABILIDADE (a saida de quem tem). E aqui e nao no login pelo mesmo motivo
			// que a vizinha: ela forja corpos sem `Peer`, e o que ela precisa -- catalogo de skills
			// (pro gate de quem COMPROU `Regenerate`), `races.json` (pro eixo do genoma) e as zonas
			// pre-feitas (pro `PorNoMundo`) -- ja carregou tudo nesta sequencia.
			// Ver `GameServer.CuraVivaTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--curaviva") >= 0) RodarBancadaDaCuraViva();

			// `--vozteste`: O CORTE DA VOZ -- quem recebe e quem NAO recebe.
			//
			// MAIS NOVA QUE A COPIA, e no boot pelo motivo que o proprio cabecalho dela declara: ela
			// pergunta ao funil `QuemOuveAVoz` com corpos FORJADOS (sem `Peer`), mapas de verdade e
			// distancias de verdade -- entao precisa das zonas com `.vis` carregado (a secao 3 mede a
			// parede que CEGA) e de mais nada. As irmas que precisam de gente de verdade sao a
			// `--vozviva` e a `--vozdupla`, e essas duas entram no login. Ver `GameServer.VozTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--vozteste") >= 0) RodarBancadaDaVoz();

			// `--catalogoteste`: O CATALOGO INTEIRO, E NAO UM LOTE.
			//
			// **DEPOIS DE TODAS AS OUTRAS DE TECNICA, e a ordem e a afirmacao**: ela varre os verbos
			// COM CORPO -- todos, de todos os lotes -- e exige que cada um faca alguma coisa. Isso so
			// significa alguma coisa depois que os lotes ja se registraram (`RegistrarTecnicasG1..G7`,
			// mais acima nesta mesma sequencia) e depois que as bancadas de lote ja provaram cada um
			// deles. As outras respondem "o meu lote ficou de pe"; esta responde "o conjunto nao
			// desencontrou" -- que e o defeito que nenhuma bancada de lote consegue enxergar, porque
			// ele mora ENTRE elas.
			//
			// E ela precisa de tudo o que as vizinhas precisam junto: catalogo de skills (varredura e
			// marco), cargos carregados (o ciclo dos trinta escreve no `_tronos` e devolve no
			// `finally`) e a zona pre-feita da bancada de projetil (os corpos e os tiros).
			// Ver `GameServer.CatalogoTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--catalogoteste") >= 0) RodarBancadaDoCatalogo();

			// `--mundoprova`: O PAR CENTRAL -- a semente que nasce sorteada e o NPC que nasce vestido
			// --, com o DEFEITO INJETADO em cada familia. Ver `GameServer.MundoProvaTeste.cs`.
			//
			// COLADA NA `--sementeteste` e ANTES dela, pelo mesmo motivo que aquela e penultima: as
			// familias 1 a 4 daqui rodam dentro do mesmo `NaCaixa`, e a familia 3 chama o
			// `ExecutarLimpeza` DE PRODUCAO. Antes e nao depois porque esta bancada e o portao: as
			// afirmacoes da `--sementeteste` e da `--npcteste` so valem alguma coisa depois que
			// alguem provou que elas sabem ficar vermelhas.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--mundoprova") >= 0) RodarProvaDoMundo();

			// `--sementeteste`: A SEMENTE DESTE UNIVERSO -- de onde ela vem, onde ela mora, e o que o
			// wipe faz com ela. Ver `GameServer.SementeTeste.cs`.
			//
			// PENULTIMA, colada na `--wipeteste` e pelo mesmo motivo dela: a familia 4 chama o
			// `ExecutarLimpeza` DE PRODUCAO (limpeza total de verdade, contra um servidor de mentira
			// dentro de um temporario) e devolve o mundo do dono no `finally` pelo mesmo `NaCaixa`.
			// Rodando no meio da lista, ela zeraria a memoria debaixo das bancadas seguintes.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--sementeteste") >= 0) RodarBancadaDaSemente();

			// `--wipeteste`: A LIMPEZA TOTAL DO SERVIDOR -- e ela e a **ULTIMA DE TODAS**, por um
			// motivo que nenhuma outra tem: ela APAGA O MUNDO DE VERDADE e devolve depois.
			//
			// Depois de tudo porque a secao 2 dela varre a pasta de saves procurando arquivo sem
			// dono, e as bancadas acima escrevem contas e arquivos temporarios nela (a `--mestrevivo`
			// cria sete contas, a `--cargoportas` mexe em tres arquivos do mundo). Rodando antes, ela
			// mediria uma pasta que ainda vai mudar; rodando aqui, ela ve o que o servidor vai
			// realmente ter no ar.
			//
			// E ela precisa de TUDO carregado: sem `_store` nao ha pasta, sem os doze `Carregar*` nao
			// ha o que zerar, e sem o `_cadeia` das sagas a afirmacao de "volta adormecida" nao teria
			// com o que comparar. Ver `GameServer.LimpezaTeste.cs`.
			if (Array.IndexOf(OS.GetCmdlineArgs(), "--wipeteste") >= 0) RodarBancadaDaLimpeza();
		}

		Running = _net.Start(port);
		SetProcess(Running);
		GD.Print(Running
			? $"[server] escutando na porta {port} | tick {Protocol.TickHz} Hz"
			: $"[server] FALHOU ao abrir a porta {port} (ja tem alguem usando?)");
		return Running;
	}

	public void Stop()
	{
		if (!Running) return;
		_net.Stop();
		Running = false;
		SetProcess(false);
		_players.Clear();
		_byPeer.Clear();
		_zones.Clear();
		GD.Print("[server] parado");
	}

	/// <summary>
	/// FECHAR O JOGO COM O SERVIDOR NO AR: **grava tudo e so depois desliga**.
	///
	/// ============================ O `Stop` SOZINHO PERDIA ATE DOIS MINUTOS, DE TODO MUNDO ============================
	/// Ele faz `_players.Clear()` sem passar por `Persistir`. Os dois caminhos de save do servidor
	/// sao o periodico (`_tickCount % TicksPorSave`, cujo proprio comentario diz que "dois minutos e
	/// o maximo de treino que alguem pode perder") e o da DESCONEXAO -- e quem fecha o processo nao
	/// atravessa nenhum dos dois. Ou seja: quem HOSPEDA e fecha o jogo levava junto ate dois minutos
	/// de progresso de **todos os conectados**, calado.
	///
	/// Grava as mesmas tres coisas que o `AdminSalvarTudo` grava, e pela mesma razao: personagem sem
	/// o mundo deixa construcao que sumiu, e sem os cargos deixa rank que voltou pro anterior.
	/// ==========================================================================================
	/// </summary>
	public void SalvarEParar()
	{
		if (!Running) return;

		int n = 0;
		try
		{
			foreach (ServerPlayer p in Jogadores.ToList()) { Persistir(p); n++; }
			GravarMundo();
			SalvarCargos();
			GD.Print($"[server] fechando: {n} personagem(ns), o mundo e os cargos gravados");
		}
		catch (Exception e)
		{
			// SAIR MESMO ASSIM. Um erro de disco nao pode deixar o processo (e a porta) de pe.
			GD.PushError($"[server] falhei ao gravar no fechamento: {e.Message}");
		}

		Stop();
	}

	/// <summary>
	/// Le o manifesto das zonas e a colisao de cada uma. Sao ~31 KB por andar, entao carregar
	/// TUDO no boot custa pouco e evita hitch quando alguem troca de planeta.
	/// </summary>
	private void CarregarZonas()
	{
		// BANCADA: `--semduro` reproduz o mundo de ANTES do conserto -- ver a nota junto do
		// `CarregarDuro`, mais abaixo. Lido aqui e nao la dentro pra o aviso sair UMA vez.
		// `--menteantiga`: o LUGAR de antes do conserto, pra a foto do "antes". Lida aqui, junto do
		// `--semduro`, porque as duas sao a mesma ideia (mostrar o mundo SEM o conserto) e as duas
		// tem que gritar no log. Ver `GameServer.Mente.MenteAntiga`.
		MenteAntiga = Array.IndexOf(OS.GetCmdlineArgs(), "--menteantiga") >= 0;
		if (MenteAntiga)
			GD.PushWarning("[server] `--menteantiga`: a mente NAO tera planta propria. Este e o mundo "
						   + "de ANTES do conserto -- a meditacao volta a cair no z24 do BYOND, o "
						   + "\"lugar nada a ver\". Se voce nao esta tirando a foto do antes, tire esta chave.");

		_semDuro = Array.IndexOf(OS.GetCmdlineArgs(), "--semduro") >= 0;
		if (_semDuro)
			GD.PushWarning("[server] `--semduro`: o plano do indestrutivel NAO sera lido. Este e o "
						   + "mundo de ANTES do conserto -- o vazio invisivel volta a cair no soco. "
						   + "Se voce nao esta rodando uma bancada, tire esta chave.");

		const string manifesto = "res://Assets/Maps/manifest.json";
		if (!Godot.FileAccess.FileExists(manifesto))
		{
			GD.PushWarning("[server] sem manifest.json: rode o Tools/AssetPipeline -- movimento so sera validado por VELOCIDADE");
			return;
		}

		_catalogo = ZoneCatalog.Parse(Godot.FileAccess.GetFileAsString(manifesto));
		int ok = 0, comVista = 0, comAgua = 0, comDuro = 0, comNuvem = 0;
		foreach (ZoneEntry e in _catalogo.Todas)
		{
			if (!Godot.FileAccess.FileExists(e.Colisao)) continue;
			e.Mapa = ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(e.Colisao));
			if (e.Mapa == null) continue;
			ok++;

			// ZONA SEM BEIRADA (hoje so a Sala do Tempo): fora do bitset e chao livre, e nao o fim
			// do mundo. Ver `ZoneCollision.SemBorda` -- e o cliente pergunta ao MESMO `SalaDoTempo`
			// em `World.MapaCacheado`, porque duas respostas diferentes pra "aqui ha parede?" e o
			// corpo tremendo na costura.
			if (SalaDoTempo.SemBorda(e.Zona)) e.Mapa.SemBorda = true;

			// A AGUA E UM PLANO SEPARADO PENDURADO NO MESMO MAPA (arquivo `.agua` proprio, e nao a
			// cauda do `.col`, que ja tem dono). Sem esta linha a agua vira chao comum e se anda por
			// cima do oceano -- o servidor e a autoridade da colisao. O cliente faz o mesmo em
			// `World.MapaCacheado`, e as duas pontas TEM que ler o mesmo arquivo.
			if (Godot.FileAccess.FileExists(e.CaminhoDaAgua)
				&& e.Mapa.CarregarAgua(Godot.FileAccess.GetFileAsBytes(e.CaminhoDaAgua))) comAgua++;

			// A NUVEM E O QUARTO PLANO, e ela entra pelo mesmo caminho e com o mesmo cuidado da agua.
			// Sem esta linha as 499 mil celulas de nuvem continuam sendo chao comum -- que e
			// literalmente o bug que o dono relatou (andar por cima do ceu do Templo).
			//
			// O NOME DA ZONA VAI JUNTO, e nao um `bool`: e ele que decide se esta nuvem DERRUBA ou so
			// BARRA, e a derivacao mora num lugar so (`ClasseDeNuvem.Derruba`). Ver o cabecalho do
			// `CarregarNuvem` -- e a mesma razao pela qual o cliente passa o nome dele em
			// `World.MapaCacheado`, e nao uma segunda opiniao.
			if (Godot.FileAccess.FileExists(e.CaminhoDaNuvem)
				&& e.Mapa.CarregarNuvem(Godot.FileAccess.GetFileAsBytes(e.CaminhoDaNuvem), e.Zona)) comNuvem++;

			// O QUE NAO SE QUEBRA -- outro plano pendurado no mesmo mapa, pelo mesmo motivo que a
			// agua, e SO O SERVIDOR precisa dele: quem derruba cenario e ele (`DerrubarCelula`), e o
			// cliente so recebe o pacote de "esta celula caiu". Sem esta linha o `.duro` fica no
			// disco sem ninguem ler e o vazio do Templo volta a cair no soco, CALADO -- que e
			// exatamente como este defeito sobreviveu ate agora.
			//
			// ============================ `--semduro`: O MUNDO DE ANTES, DE PROPOSITO ============================
			// A unica prova honesta de que este conserto conserta e mostrar o mundo SEM ele -- e faze-lo
			// apagando o `.duro` do disco seria destruir o dado pra medir. Esta chave desliga a LEITURA e
			// nao o arquivo: com ela o servidor volta a ser exatamente o que o dono descreveu (o vazio do
			// Templo cai no soco), e sem ela volta ao certo, no mesmo binario e no mesmo mapa.
			//
			// Ela e de BANCADA e grita: uma partida de verdade rodando assim tem que aparecer no log.
			// ==================================================================================================
			if (_semDuro) { /* o mundo de ANTES do conserto -- ver a nota acima */ }
			else if (Godot.FileAccess.FileExists(e.CaminhoDoDuro)
					 && e.Mapa.CarregarDuro(Godot.FileAccess.GetFileAsBytes(e.CaminhoDoDuro))) comDuro++;

			// O `.vis` E OUTRO MAPA, e nao um campo deste. Ele diz o que CEGA, nao o que bloqueia --
			// porta cega e nao bloqueia, beirada bloqueia e nao cega. Quem le e a VOZ
			// (`MapaDaVista`), pra decidir "ha parede entre os dois?" com a MESMA resposta que a
			// vista do cliente da. Sem esta linha nenhuma parede abafa voz nenhuma, calado.
			if (Godot.FileAccess.FileExists(e.Visao))
			{
				e.Vista = ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(e.Visao));
				if (e.Vista != null) comVista++;
			}
		}
		GD.Print($"[server] zonas: {_catalogo.Todas.Count()} | com colisao: {ok} | com vista: {comVista}"
				 + $" | com agua: {comAgua} | com nuvem: {comNuvem}"
				 + $" | com celula indestrutivel: {comDuro}");

		// ============================ ZONA PRE-FEITA SEM `.duro` E SUSPEITA ============================
		// Todo `.dmm` feito a mao cerca o retangulo com `/turf/Other/Blank` -- e o jeito do BYOND de
		// dizer "o mapa acaba aqui" --, e o Blank e `destroyable = 0`. Ou seja: uma zona pre-feita com
		// paredes e SEM nenhuma celula dura quer dizer, quase sempre, que o `.duro` nao foi gerado --
		// e sem ele o vazio volta a cair no soco, exatamente como o dono relatou.
		//
		// E NOTA, e nao alarme, porque tres zonas de verdade nao tem nenhum typepath indestrutivel
		// (Makyo_Star, Inbetween_Realm e Void, medidas). Reprovar com elas na lista treinaria quem le
		// o log a ignorar o log -- a mesma regra do "FRACO DEMAIS NAO E FALHA" da bancada de soco.
		// Conserto, quando for o caso:
		//     dotnet run --project Tools/AssetPipeline -- duro <BYOND>/Maps <BYOND>/Code Assets/Maps
		// =============================================================================================
		string[] semDuro = [.. _catalogo.Todas
			.Where(e => e.Mapa is { TemDuro: false } m && m.Width > 0)
			.Select(e => e.Zona)];
		if (semDuro.Length > 0)
			GD.Print($"[server] zonas pre-feitas SEM plano de indestrutivel ({semDuro.Length}): "
					 + string.Join(", ", semDuro) + " -- se nao for de proposito, rode o pipeline 'duro'");

		// AS PORTAS VEM JUNTO e nao por acaso: elas ESCREVEM no `Mapa` que acabou de ser lido
		// (abrir uma porta e limpar a celula dela -- ver GameServer.Portas.cs). Carregar as duas
		// coisas no mesmo lugar deixa obvio que uma depende da outra.
		CarregarPortas();

		// AS PASSAGENS TAMBEM, e depois das zonas de proposito: elas apontam pra OUTRAS zonas, e o
		// carregador recusa as que apontam pra uma que nao existe -- pergunta que so tem resposta
		// com o catalogo inteiro na mao.
		CarregarPassagens();
	}

	/// <summary>
	/// Os prototipos raciais extraidos do DM. Sem eles o servidor ainda aceita gente, mas
	/// todo mundo nasce com stat 1 -- entao a falta e ruidosa de proposito.
	/// </summary>
	private void CarregarRacas()
	{
		const string dados = "res://Assets/Data/races.json";
		if (!Godot.FileAccess.FileExists(dados))
		{
			GD.PushWarning("[server] sem races.json: rode o AssetPipeline (comando 'races') -- todo mundo nascera generico");
			return;
		}
		_racas = RaceCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));
		GD.Print($"[server] racas: {_racas.Count} protos");
	}

	/// <summary>
	/// O catalogo de aparencia e a pasta de saves. O catalogo e o MESMO arquivo que o cliente
	/// usa na tela de criacao -- e o que torna "escolha so o que existe" uma regra de verdade
	/// e nao uma gentileza do cliente.
	/// </summary>
	private void CarregarVisual()
	{
		const string dados = "res://Assets/Data/visual.json";
		if (Godot.FileAccess.FileExists(dados))
		{
			_visual = Jandirus.Core.Appearance.VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));
			GD.Print($"[server] aparencia: {_visual.Cabelos.Count} cabelos, {_visual.Roupas.Count} roupas");
		}
		else GD.PushWarning("[server] sem visual.json: rode o AssetPipeline (comando 'visual')");

		// os saves ficam FORA do res:// (que e so leitura numa build exportada)
		string pasta = ProjectSettings.GlobalizePath("user://saves");
		_store = new AccountStore(pasta);

		// ============================ ANTES DE QUALQUER CARGA, E POR UM MOTIVO ============================
		// A inscricao dos sistemas do mundo (ver `GameServer.Limpeza.cs`) e quem RESERVA os nomes de
		// arquivo que nao sao conta -- `mundo.json`, `naves.json`, `cargos.txt` e companhia. Duas
		// coisas dependem disso e as duas acontecem abaixo: o `_store.Quantas()` do fim deste metodo
		// (que sem a reserva conta a frota e os dominios como se fossem contas) e o `Login`, que
		// recusa a conta chamada "naves" antes que ela grave por cima da frota do servidor.
		//
		// Ela nao carrega nada e nao le disco: sao lambdas guardadas numa lista. Por isso pode -- e
		// deve -- vir na frente de tudo.
		RegistrarSistemasDoMundo();

		// ============================ A SEMENTE, ANTES DE QUEM CONSULTA O UNIVERSO ============================
		// Ela e a raiz de TUDO o que e sorteado no mundo (galaxia, planeta gerado, clima, ceu, berco,
		// povoamento, invasao, embaralho), e dois carregadores logo abaixo ja perguntam pelo universo
		// com ela na mao: `CarregarConquista` valida cada dominio pelo endereco `(Sx, Sy, K)` do
		// sistema, e `CarregarPlanetasMortos` chaveia planeta gerado por semente de zona. Lida depois,
		// os dois julgariam este mundo com a semente errada -- e o log ja teria dito que estava tudo
		// certo. Ver `GameServer.Semente.cs`.
		//
		// DEPOIS do registro de propósito: e ele quem RESERVA o nome `universo.json` contra uma conta
		// homonima, pela mesma regra dos outros arquivos de mundo.
		CarregarSemente();

		CarregarSkills();
		CarregarTech();
		// DEPOIS do `CarregarTech`: a nave le o catalogo de construcoes pra saber nome, arte e
		// densidade dela. Carregada antes, toda nave do disco voltaria sem sprite e sem parede.
		CarregarNaves();
		CarregarEstilos();
		CarregarPlanetas();
		CarregarNiveis();
		// DEPOIS do `CarregarSkills` e do `CarregarNiveis`, e nao antes: um molde de NPC gasta
		// marcos no catalogo de skills e crava degraus no de niveis. Carregado antes, o primeiro
		// NPC nasceria sem livro e ninguem ligaria a causa a ordem desta lista.
		CarregarMoldes();

		// DEPOIS do `CarregarMoldes`: a cadeia de sagas vive no MESMO arquivo e cita os moldes pelo id
		// -- a conferencia dela (`Sagas.Problemas`) precisa do catalogo montado. E a reputacao vem
		// junto porque e a recompensa da cadeia; separadas, a primeira saga vencida num servidor novo
		// pagaria heroismo num livro-caixa que ainda nao tinha sido lido do disco.
		CarregarSagas();
		CarregarReputacao();

		// DEPOIS DAS SAGAS, e a ordem tem razao: e a cadeia que CONDENA um planeta, e o livro dos
		// mortos e quem o mata. Carregado antes, um servidor que caiu no meio dos cinco minutos de
		// explosao teria a saga voltando "consumada" e a destruicao ainda nao lida -- e o `Condenado`
		// do `sagas.json` viraria um segundo estado sobre a mesma coisa. Ver `GameServer.Destruicao.cs`.
		CarregarPlanetasMortos();

		// DEPOIS DOS MORTOS, e pela mesma razao de cadeia: o livro dos dominios pergunta "este planeta
		// ainda existe?" a `ZonaMorta` logo na validacao de boot (ver `CarregarConquista`). Carregado
		// antes, um dominio num planeta explodido sobreviveria ao reinicio -- e so cairia no primeiro
		// tique, depois de ja ter aparecido no log como se estivesse de pe.
		//
		// E DEPOIS DOS MOLDES tambem: `TemPovo` le o plano de povoamento, que e quem separa mundo
		// habitado de mundo vazio -- a divisao que substituiu o `conq_premade` do original.
		CarregarConquista();

		// ============================ AS ESFERAS VEM DEPOIS DA CONQUISTA, E A ORDEM E CADEIA ============================
		// `ErguerEstatua` pergunta ao livro dos DOMINIOS quem manda no planeta (e o `conq_owner_sig`
		// do original), e o set ETERNO precisa do `_catalogo` pra saber se Namek existe neste
		// manifesto de mapas. Carregar antes de um dos dois faria o set eterno nascer sem checar o
		// mapa -- que e a unica coisa que aquele bloco do DM existe pra garantir ("NUNCA no planeta
		// errado").
		//
		// AS SUPER SAO INDEPENDENTES do resto do mundo (a posicao delas so precisa da SEMENTE, que
		// nasce no `CarregarSemente`), mas ficam ao lado por serem o mesmo sistema pro jogador.
		// ==========================================================================================================
		CarregarEsferas();
		CarregarSupers();

		// OS QUATRO LOTES SE ANUNCIAM. Cada um vive no proprio arquivo e registra as tecnicas dele
		// -- portar o proximo lote nao mexe nesta lista, so acrescenta uma linha.
		RegistrarTecnicasBase();
		RegistrarTecnicasG1();
		RegistrarTecnicasG2();
		RegistrarTecnicasG3();
		RegistrarTecnicasG4();

		// O LOTE QUE ATIRA: Ki Wave (raio), Basic Blast (bola) e Guided Ball (teleguiada) -- um por
		// tipo de projetil. Ver `GameServer.Projeteis.cs`.
		RegistrarTecnicasDeProjetil();

		// O ARSENAL NOMEADO: as catorze folhas que atiram e que estavam mudas (Masenko, Final Flash,
		// Hellzone Grenade, Kienzan, Paralysis...). Depende do lote acima -- todas entram pelo
		// `Disparar`/`Canalizar` dele. Ver `GameServer.Tecnicas.G5.cs`.
		RegistrarTecnicasG5();

		// O LOTE DOS CARGOS: os seis raios nomeados (Kamehameha, Galick Ho, Death Beam, Dodon Ray,
		// Enkumei, Boom Wave), o Kikoho, os quatro buffs de Ki, o sopro inteiro, os dois punhos do
		// lobo, a cura e a leitura de Ki. Onze dos dezenove sao verbos que um KIT DE CARGO ja
		// entregava e que nao faziam nada. Ver `GameServer.Tecnicas.G6.cs`.
		RegistrarTecnicasG6();

		// O LOTE DOS PUNHOS NOMEADOS: os tres combos de boxe, os tres chutes, as quatro artes
		// marciais, o Lariat, os dois do assassino e as duas bolas que faltavam (Bala Dispersa e
		// Spirit Gun). Depende do lote G3 (o funil `GolpeG3` e a agenda de barragem) e do lote dos
		// projeteis. Ver `GameServer.Tecnicas.G7.cs`.
		RegistrarTecnicasG7();

		// O LOTE DOS VERBOS MUDOS DOS CARGOS: Ver os Mortos, o teleporte do juiz do Outro Mundo, o
		// Atalho Sagrado do Guardiao Arconiano, a piada da Esmeralda, o Manter o Corpo e a oferta de
		// juventude. Nenhum depende de sistema que este port nao tenha -- e tres deles estavam
		// etiquetados como se dependessem. Ver `GameServer.Tecnicas.G8.cs`.
		RegistrarTecnicasG8();

		// O LOTE DO SELO E DO SIGILO: Mafuba, Abrir a Dead Zone, Ocultar o Poder e Controle de
		// Poder. Os dois primeiros so passaram a ser exigiveis quando o extrator parou de perder o
		// `after_learn` deles; os dois ultimos fecham o lado de quem ESCONDE do sigilo de poder, que
		// o port so tinha do lado de quem le. Ver `GameServer.Tecnicas.G9.cs`.
		RegistrarTecnicasG9();

		// O LOTE DOS GOLPES MUDOS DO MOLDE DO G7: os cinco do assassino, os quatro do berserker, os
		// quatro da luta livre (todos no PRESO), a corrida de Ki, os dois teleportes e a Trindade.
		// Depende do G3 (GolpeG3), do G7 (a moldura de punho), do agarrao e do arremesso. Ver
		// `GameServer.Tecnicas.G10.cs`.
		RegistrarTecnicasG10();

		// O LOTE DOS PROJETEIS QUE FALTAVAM E DOS SISTEMAS PEQUENOS: Death Ball, Buster Barrage, as duas
		// rajadas, a Genkidama, as duas absorcoes, a Imitacao, a Divisao do Corpo, o Senzu e os Alvos de
		// Ki (mais a Precognicao, que e passiva). Ver `GameServer.Tecnicas.G12.cs`.
		RegistrarTecnicasG12();

		// O LOTE "UMA FUNCAO SOBRE PECA EXISTENTE": Sneak, Expand Body, Majin, Shackle, os tres
		// teleportes com carona, Flip, Self Destruct, Psycho Thread, Freeze, Observe, Unlock Potential
		// e Give Power -- as skills que ja estavam na arvore sem efeito. Ver `GameServer.Tecnicas.G11.cs`.
		RegistrarTecnicasG11();

		// O LOTE DO SISTEMA DE ESTUDO da arvore "Strength of Mind": Study_Other, Focus_Skill e
		// Write_Teachings -- os tres verbs que faltavam das dezessete skills da Mente. Ver
		// `GameServer.Tecnicas.G13.cs`.
		RegistrarTecnicasG13();

		// O `Planet_Destroy` -- a unica tecnica so-de-vilao do catalogo. Ver `GameServer.Destruicao.cs`.
		RegistrarTecnicasDaDestruicao();
		CarregarCargos();
		// O DISCIPULADO tem o MESMO ciclo de vida dos cargos: arquivo de texto ao lado dos saves,
		// lido UMA VEZ aqui e gravado na hora em cada mudanca. Ver `GameServer.Mestre.cs`.
		CarregarMestres();

		// AS PORTAS DOS CARGOS, mesmo ciclo de vida: a linha de sucessao do trono de Vegeta
		// (`herdeiros.txt`) e o relogio do titulo em disputa (`titulo.txt` -- carencia, adiamentos,
		// tarefa e falhas do Deus da Destruicao). Ver `GameServer.CargoPortas.cs`.
		CarregarHerdeiros();
		CarregarTitulo();

		// OS DEVERES DOS CARGOS. Mesmo ciclo de vida, arquivo proprio (`missoes-de-cargo.json`), e
		// com uma coisa que so ele precisa: o PERDAO DE BOOT. O prazo mora no relogio do MUNDO, que
		// anda com o servidor fora do ar -- sem o perdao, uma manutencao de duas horas venceria toda
		// tarefa em voo no primeiro tique e cobraria falha de quem nem estava jogando. Ver
		// `GameServer.CargoMissoes.cs`.
		CarregarMissoes();

		Directory.CreateDirectory(pasta);
		GD.Print($"[server] contas: {_store.Quantas()} em {pasta}");
	}

	private void Wire()
	{
		_listener.ConnectionRequestEvent += req => req.AcceptIfKey(Protocol.ConnectionKey);
		_listener.PeerConnectedEvent += peer => GD.Print($"[server] conexao de {peer.Address}");
		_listener.PeerDisconnectedEvent += (peer, info) => Drop(peer);
		_listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
		{
			try { Handle(peer, reader); }
			catch (Exception ex) { GD.PushWarning($"[server] pacote invalido de {peer.Address}: {ex.Message}"); }
		};
	}

	public override void _Process(double delta)
	{
		if (!Running) return;
		_net.PollEvents();

		_accumulator += delta;
		while (_accumulator >= Protocol.TickSeconds)
		{
			_accumulator -= Protocol.TickSeconds;
			Tick();
		}
	}

	public override void _ExitTree()
	{
		if (Running) _net.Stop();
		Instance = null;
	}

	// ---------------------------------------------------------------------
	// recepcao
	// ---------------------------------------------------------------------
	/// <summary>
	/// O ESPIAO DA ENTRADA: cada pacote que chega, cru, ANTES da primeira leitura. **RECONSTRUIDO**
	/// com os campos do `ServerPlayer` (ver o bloco de reconstruidos la em cima).
	///
	/// Existe pra a bancada de teclas (`Client/RoboDeTecla.cs`), e o cabecalho de la diz por que ela
	/// nao mede `Verbo.Acionar`: ler o codigo prova que HOJE o botao e a tecla chamam a mesma funcao,
	/// e nao prova que os dois puseram os MESMOS BYTES no fio -- que e a unica coisa que o servidor
	/// obedece. Uma segunda escrita do pacote, envelhecendo calada, e invisivel pra qualquer
	/// checagem que compare intencao.
	///
	/// **ESTATICO E NULO EM PRODUCAO**, e por isso ele custa uma comparacao com nulo por pacote: o
	/// gancho e da bancada, e a bancada e quem o desarma no `_ExitTree`. A copia e feita AQUI, antes
	/// do primeiro `GetByte()`, porque o `NetPacketReader` e um cursor -- depois do despacho o
	/// buffer ja andou, e "os bytes que chegaram" nao existem mais.
	/// </summary>
	public static Action<byte[]>? EspiaoDeEntrada;

	private void Handle(NetPeer peer, NetPacketReader reader)
	{
		EspiaoDeEntrada?.Invoke(reader.RawData is { } cru
			? cru[reader.UserDataOffset..(reader.UserDataOffset + reader.UserDataSize)]
			: []);

		var id = (Protocol.C2S)reader.GetByte();

		// ============================ QUEM PERDEU AS REDEAS NAO MEXE O CORPO ============================
		// A recusa ficava em UM lugar so -- o portao de MOVIMENTO, dentro do `Input`. Soco, guarda,
		// carga de Ki, habilidade, transformacao e Zanzoken passavam inteiros enquanto a fera dirigia
		// o corpo, que e metade da queixa do dono ("eu ainda posso tentar mexer").
		//
		// A porta e AQUI, no despacho, e nao dentro de cada handler, por dois motivos: um caso novo
		// (o proximo comando que mexa no corpo) so precisa entrar na lista de `ComandoDeCorpo`, e as
		// chamadas INTERNAS do servidor -- `Carregar(pl,false)` quando a fera assume, por exemplo --
		// continuam livres, porque quem esta barrado e o PACOTE do dono, nao a funcao.
		//
		// O movimento continua com o portao dele la embaixo: ele nao so recusa, ele tambem preserva o
		// `Moving` que a IA escreveu e responde a posicao. Ver `Input`.
		// ============================================================================================
		if (ComandoDeCorpo(id) && _byPeer.TryGetValue(peer, out ServerPlayer? tomado) && SemAsRedeas(tomado))
			return;

		switch (id)
		{
			case Protocol.C2S.Login: Login(peer, reader.GetString(24), reader.GetString(64)); break;
			case Protocol.C2S.PickSlot: PickSlot(peer, reader.GetByte()); break;
			case Protocol.C2S.DeleteChar: DeleteChar(peer, reader.GetByte(), reader.GetString(32)); break;
			case Protocol.C2S.CreateChar:
				CreateChar(peer, reader.GetByte(), reader.GetDraft(), reader.GetAppearance()); break;
			case Protocol.C2S.InputState: Input(peer, reader); break;
			case Protocol.C2S.Activity:
			{
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? a)) break;
				var q = (Protocol.Activity)reader.GetByte();
				a.Ficha.train = q == Protocol.Activity.Treinando;
				a.Ficha.med = q == Protocol.Activity.Meditando;
				GD.Print($"[server] {a.Name}: {q} (BP {a.Ficha.BP:0.0})");
				break;
			}
			case Protocol.C2S.Action:
			{
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? a)) break;
				Atacar(a, (Protocol.Golpe)reader.GetByte());
				break;
			}
			case Protocol.C2S.Guard:
			{
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? a)) break;
				a.Combate.Guardar(reader.GetBool());
				break;
			}
			case Protocol.C2S.Aim:
			{
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? a)) break;
				string z = Protocol.ZonaDe(reader.GetByte());
				a.Combate.ZonaMirada = z.Length > 0 ? z : null;
				break;
			}
			case Protocol.C2S.Lethal:
			{
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? a)) break;
				a.Combate.Letal = reader.GetBool();
				GD.Print($"[server] {a.Name}: golpe {(a.Combate.Letal ? "LETAL" : "nao-letal")}");
				break;
			}
			// TECNOLOGIA pelo mesmo padrao do cargo: comando + argumento num canal so. Ver
			// `GameServer.Tech.cs` -- sao nove comandos e nenhum merece um id de protocolo.
			case Protocol.C2S.Estilo:
			{
				string qual = reader.GetString(32);
				if (_byPeer.TryGetValue(peer, out ServerPlayer? quemLuta)) TrocarEstilo(quemLuta, qual);
				break;
			}

			case Protocol.C2S.Tech:
			{
				string cmd = reader.GetString(24);
				string arg = reader.GetString(48);
				if (_byPeer.TryGetValue(peer, out ServerPlayer? quemConstroi)) ComandoDeTech(quemConstroi, cmd, arg);
				break;
			}

			// O CANAL DOS VERBS. Mesmo formato do Tech: comando + argumento. Ver GameServer.Verbos.cs.
			// A TECLA DO ZANZO CLASH. Crua: quem julga se ela bate com a pedida e o servidor.
			case Protocol.C2S.ClashTecla:
			{
				char c = (char)reader.GetByte();
				// UM CANAL, DOIS EMBATES: quem sabe em qual deles este jogador esta e o servidor, que
				// e quem sorteou a letra. Ver `TeclaDeQualquerEmbate`.
				if (_byPeer.TryGetValue(peer, out ServerPlayer? quemTeclou)) TeclaDeQualquerEmbate(quemTeclou, c);
				break;
			}

			case Protocol.C2S.Verbo:
			{
				string cmd = reader.GetString(24);
				// O ARGUMENTO PRECISA CABER UM AVISO INTEIRO, e nao um nome curto.
				//
				// Era 48, herdado do canal de tecnologia (onde o argumento e um typepath). Mas o
				// painel de admin manda TEXTO por aqui -- o anuncio e a mensagem particular --, e
				// `GetString(max)` do LiteNetLib nao TRUNCA: acima do limite ele devolve string
				// VAZIA. O admin escrevia "o servidor vai reiniciar em dez minutos" (48+ letras), a
				// caixa se limpava, e o servidor respondia "escreva o aviso antes". O texto sumia e
				// a mensagem ainda acusava quem escreveu.
				string arg = reader.GetString(Protocol.MaxArgDeVerbo);
				if (_byPeer.TryGetValue(peer, out ServerPlayer? quemManda)) Verbo(quemManda, cmd, arg);
				break;
			}

			case Protocol.C2S.Cargo:
			{
				string chave = reader.GetString(32);
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? quemQuer)) break;
				if (chave.Length == 0) MandarCargos(quemQuer);
				else { ReivindicarCargo(quemQuer, chave); MandarCargos(quemQuer); }
				break;
			}
			case Protocol.C2S.Habilidade:
			{
				string hab = reader.GetString(48);
				if (_byPeer.TryGetValue(peer, out ServerPlayer? quemUsa)) UsarHabilidade(quemUsa, hab);
				break;
			}
			case Protocol.C2S.Transformar:
			{
				bool subir = reader.GetBool();
				if (_byPeer.TryGetValue(peer, out ServerPlayer? quemTransforma)) Transformar(quemTransforma, subir);
				break;
			}
			case Protocol.C2S.Zanzoken:
			{
				Vec2 alvo = reader.GetVec();
				if (_byPeer.TryGetValue(peer, out ServerPlayer? quemPisca)) Zanzoken(quemPisca, alvo);
				break;
			}
			case Protocol.C2S.Carregar:
			{
				bool ligado = reader.GetBool();
				if (_byPeer.TryGetValue(peer, out ServerPlayer? quemCarrega)) Carregar(quemCarrega, ligado);
				break;
			}
			case Protocol.C2S.Aprender:
			{
				string path = reader.GetString(96);
				if (_byPeer.TryGetValue(peer, out ServerPlayer? quemAprende)) Aprender(quemAprende, path);
				break;
			}
			case Protocol.C2S.Alvo:
			{
				int alvo = reader.GetInt();
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? quem)) break;
				// so vale mirar em quem existe e esta na MESMA zona -- o resto e cliente inventando.
				// A regra saiu daqui pra o `Mirar` porque a IA tambem mira: ver `GameServer.Combat.cs`.
				Mirar(quem, alvo);
				break;
			}
			// FALAR. E AQUI que o mute e conferido, e nao dentro do `Falar`.
			//
			// `Falar` nao e so o canal do chat: e por ele que o MOTOR solta os gritos das tecnicas
			// ("KAIOKEN TIMES 3!!", "Solar Flare!!") e os emotes de combate. A checagem morava la e
			// calava as tecnicas junto -- o jogador apertava Kaioken, a tecnica funcionava, o grito
			// nao saia (e ele era sinal de combate pros outros da zona) e no lugar dele vinha "voce
			// esta calado" a cada uso. Aqui e o unico ponto por onde a fala DE UM JOGADOR entra.
			case Protocol.C2S.Chat:
			{
				var canal = (Protocol.Fala)reader.GetByte();
				string texto = reader.GetString(Protocol.MaxFala);
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? a)) break;
				if (EstaCalado(a)) { Avisar(a, "voce esta calado."); break; }
				Falar(a, canal, texto);
				break;
			}

			// ============================ RECONSTRUIDO (mais novo que a copia de 23:07) ============================
			// A VOZ, COLADA NO CHAT. Esta linha nao foi copiada de lugar nenhum: ela foi DEDUZIDA de tres
			// coisas que sobreviveram intactas e que so casam de um jeito --
			//   1. `RecebiVoz` (`GameServer.Voz.cs:237`) e o funil, e o cabecalho do parametro `calado`
			//      manda ler *"o `case Protocol.C2S.Voz` no dispatch, colado no `case Protocol.C2S.Chat`"*
			//      -- e dai tanto a POSICAO desta linha quanto o fato de o mute vir de FORA, pelo mesmo
			//      `EstaCalado` que o texto usa logo acima. Sem `Avisar` aqui: o cabecalho de la explica
			//      que um aviso por quadro recusado seriam 50 linhas de chat por segundo, e que quem
			//      aperta a tecla ja e avisado uma vez pelo cliente;
			//   2. `GameClient.MandarVoz` (`Client/GameClient.cs:308`) e o outro lado do fio, e ele grava
			//      exatamente seq (ushort) + tam (byte) + `tam` bytes -- esta leitura e o inverso dele;
			//   3. `_quadroDeVoz` (`GameServer.Voz.cs:94`) existe, se declara *"buffer de leitura reusado"*
			//      e nao era lido por ninguem: e o destino deste `GetBytes`.
			//
			// A SEQUENCIA DO CLIENTE E LIDA E JOGADA FORA de proposito -- ela so precisa sair do reader
			// pros bytes seguintes casarem. Quem numera o quadro e o SERVIDOR (`_seqDeVoz`, cujo cabecalho
			// diz que o contador e dele e que um buraco na conta quer dizer perda de rede, e nao mentira
			// do cliente).
			//
			// O DESCARTE DE TAMANHO MENTIROSO E COPIA DO ESPELHO do cliente (`S2C.Voz`, com o motivo
			// escrito la): `GetBytes` estouraria e a excecao viraria uma linha de log por quadro.
			// ====================================================================================================
			case Protocol.C2S.Voz:
			{
				reader.GetUShort();
				byte tamVoz = reader.GetByte();
				if (tamVoz == 0 || tamVoz > Jandirus.Core.Social.VozLocal.MaxBytesDeQuadro
					|| reader.AvailableBytes < tamVoz) break;
				reader.GetBytes(_quadroDeVoz, tamVoz);
				if (!_byPeer.TryGetValue(peer, out ServerPlayer? quemFala)) break;
				RecebiVoz(quemFala, _quadroDeVoz, tamVoz, EstaCalado(quemFala));
				break;
			}
			case Protocol.C2S.Ping:
			{
				var w = Protocol.Begin(Protocol.S2C.Pong);
				w.Put(reader.GetLong());
				peer.Send(w, Protocol.ChannelState, DeliveryMethod.Unreliable);
				break;
			}
		}
	}

	// =====================================================================
	// PASSO 1: ENTRAR NA CONTA
	// =====================================================================
	/// <summary>
	/// Login por SERVIDOR: o par conta+senha e a identidade inteira aqui dentro. Conta que
	/// nao existe e CRIADA na hora -- e o comportamento de "primeiro acesso" que o jogador
	/// espera, e nao ha nada a proteger numa conta que ninguem usou ainda.
	/// </summary>
	private void Login(NetPeer peer, string conta, string senha)
	{
		if (_byPeer.ContainsKey(peer) || _logados.ContainsKey(peer)) return;

		// LIMPEZA EM CURSO: ninguem entra. A janela e de milissegundos, mas quem entrasse nela
		// gravaria uma conta nova DEPOIS de o disco ser varrido -- e o servidor "novo" nasceria com
		// um sobrevivente. Ver `GameServer.Limpeza.cs`.
		if (_limpezaEmCurso) { Recusar(peer, "o servidor esta sendo limpo -- tente de novo em instantes"); return; }

		conta = conta.Trim();
		if (conta.Length < 2) { Recusar(peer, "escolha um nome de conta com pelo menos 2 letras"); return; }

		// TETO NO NOME DA CONTA. Nao e capricho: o painel de admin manda a lista de contas pela rede
		// e o leitor do outro lado usa `GetString(48)` -- que devolve VAZIO pra string maior que o
		// limite. Sem este teto, uma conta de nome comprido apareceria como uma linha em branco no
		// painel, sem botao e sem explicacao. O limite fica FOLGADO em relacao ao do pacote.
		if (conta.Length > 32) { Recusar(peer, "nome de conta longo demais (maximo 32)"); return; }

		// NOME RESERVADO. O arquivo de uma conta e `<nome saneado>.json` NA MESMA PASTA em que o
		// `mundo.json` mora -- entao a conta chamada "mundo" aponta pro arquivo das construcoes. Sem
		// esta recusa, qualquer um mandava um login com esse nome: o parser estourava lendo um array
		// como conta, o `catch` devolvia null, o `Login` entendia "conta nova" e GRAVAVA a conta por
		// cima do mundo. Todas as construcoes do servidor apagadas, por um cliente sem senha.
		if (AccountStore.NomeReservado(conta))
		{
			Recusar(peer, "esse nome de conta e reservado pelo servidor");
			return;
		}
		if (senha.Length < 3) { Recusar(peer, "escolha uma senha de pelo menos 3 caracteres"); return; }
		if (_store == null) { Recusar(peer, "servidor sem armazenamento"); return; }

		// a mesma conta em duas telas brigaria pelo arquivo
		if (_logados.Values.Any(a => string.Equals(a.Conta, conta, StringComparison.OrdinalIgnoreCase))
			|| _players.Values.Any(p => string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase)))
		{
			Recusar(peer, "essa conta ja esta conectada");
			return;
		}

		AccountSave? acc = _store.Carregar(conta);
		if (acc == null)
		{
			(string sal, string hash) = AccountStore.Cadastrar(senha);
			acc = new AccountSave { Conta = conta, Sal = sal, Hash = hash, CriadaEm = NowMs() };
			_store.Gravar(acc);
			GD.Print($"[server] conta NOVA: {conta}");
		}
		else if (!AccountStore.Confere(acc, senha))
		{
			Recusar(peer, "senha incorreta");
			GD.Print($"[server] senha errada na conta '{conta}' de {peer.Address}");
			return;
		}

		// BANIMENTO E CONFERIDO DEPOIS DA SENHA, de proposito: dizer "banida" a quem nem sabe a
		// senha entregaria que a conta existe. E o mesmo cuidado do "senha incorreta" generico.
		if (acc.Banida)
		{
			Recusar(peer, "esta conta esta banida deste servidor");
			GD.Print($"[server] conta banida '{conta}' tentou entrar de {peer.Address}");
			return;
		}

		acc.VistoEm = NowMs();
		_logados[peer] = acc;
		MandarSlots(peer, acc);
		GD.Print($"[server] conta '{conta}' entrou | slots ocupados: {acc.Slots.Count(x => x != null)}/{AccountStore.Slots}");
	}

	/// <summary>
	/// ============================ O QUE A TELA DE SELECAO MOSTRA, COMO LISTA ============================
	/// Os tres slots ja censurados, na ordem em que o cliente os desenha. Ele existe SEPARADO do
	/// <see cref="MandarSlots"/> por uma razao de prova e nao de estilo: a pergunta *"o personagem
	/// apagado ainda aparece na selecao?"* so vale a pena se quem responde for **o mesmo codigo que
	/// responde ao jogador**. Uma bancada que relesse `acc.Slots` na mao estaria afirmando uma coisa
	/// sobre um array e nao sobre a tela -- e este projeto ja pagou por essa diferenca (a memoria do
	/// sigilo do BP: escrever o corte nao e aplicar o corte).
	///
	/// `MandarSlots` embrulha isto num pacote e mais nada. Quem quiser saber o que o jogador VE
	/// pergunta aqui.
	/// ================================================================================================
	/// </summary>
	internal static SlotInfo[] SlotsVisiveisDe(AccountSave acc)
	{
		var slots = new SlotInfo[AccountStore.Slots];
		for (int i = 0; i < AccountStore.Slots; i++)
		{
			CharacterSave? c = acc.Slots[i];
			// CENSURADO ANTES DE SAIR. Na tela de selecao nao existe personagem em jogo, logo nao
			// existe scouter -- e classe nunca aparece, em situacao nenhuma.
			slots[i] = SlotVisivel(new SlotInfo
			{
				Ocupado = c != null,
				Nome = c?.Nome ?? "", Raca = c?.Raca ?? "", Classe = c?.Ficha.Class ?? "",
				Genero = c?.Genero ?? "Male", Idade = c?.Idade ?? 0, BP = c?.Ficha.BP ?? 0,
				Visual = c?.Visual ?? new Jandirus.Core.Appearance.Appearance(),
			});
		}
		return slots;
	}

	/// <summary>A tela de selecao inteira num pacote: os tres slots com o que ela mostra.</summary>
	private static void MandarSlots(NetPeer peer, AccountSave acc)
	{
		var w = Protocol.Begin(Protocol.S2C.SlotList);
		w.Put((byte)AccountStore.Slots);
		foreach (SlotInfo s in SlotsVisiveisDe(acc)) s.Write(w);
		peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// PASSO 2: ESCOLHER OU CRIAR O PERSONAGEM
	// =====================================================================
	private void PickSlot(NetPeer peer, int slot)
	{
		if (!_logados.TryGetValue(peer, out AccountSave? acc)) { Recusar(peer, "faca login primeiro"); return; }
		if (slot < 0 || slot >= AccountStore.Slots) { Recusar(peer, "slot invalido"); return; }

		CharacterSave? c = acc.Slots[slot];
		if (c == null) { Recusar(peer, "esse slot esta vazio"); return; }

		Entrar(peer, acc, slot, c);
	}

	/// <summary>
	/// APAGA O PERSONAGEM DE UM SLOT. Nao ha volta, e por isso ha quatro guardas antes.
	///
	/// ============================ O QUE ISTO NAO PRECISA LIMPAR ============================
	/// CARGO NAO. O trono e da CONTA e nao do personagem (`_tronos` guarda conta -- ver
	/// `GameServer.Ranks.cs`), entao apagar um personagem nao deixa cargo ocupado por um fantasma.
	/// Isso foi conferido antes de escrever a funcao, e nao suposto: se fosse por personagem, este
	/// metodo teria que vagar o trono aqui, e esquecer disso travaria o cargo para sempre.
	/// ======================================================================================
	/// </summary>
	private void DeleteChar(NetPeer peer, int slot, string nomeDigitado)
	{
		if (!_logados.TryGetValue(peer, out AccountSave? acc)) { Recusar(peer, "faca login primeiro"); return; }
		if (slot < 0 || slot >= AccountStore.Slots) { Recusar(peer, "slot invalido"); return; }

		CharacterSave? c = acc.Slots[slot];
		if (c == null) { Recusar(peer, "esse slot ja esta vazio"); return; }

		// O NOME CONFERIDO AQUI, e nao na tela. O cliente mostra o campo; quem decide se bate e
		// quem tem o save na mao -- senao bastava mandar o pacote na mao pra pular a trava.
		if (!string.Equals(nomeDigitado.Trim(), c.Nome, StringComparison.OrdinalIgnoreCase))
		{
			Recusar(peer, $"o nome nao confere -- digite exatamente \"{c.Nome}\" pra confirmar");
			return;
		}

		// NAO SE APAGA QUEM ESTA EM JOGO. Outra conexao da MESMA conta pode estar jogando com este
		// personagem agora: apagar o save por baixo dela deixaria um corpo vivo sem ficha nenhuma,
		// e o proximo salvamento periodico o traria de volta do nada.
		if (_players.Values.Any(p => string.Equals(p.Conta, acc.Conta, StringComparison.OrdinalIgnoreCase)))
		{
			Recusar(peer, "esta conta esta em jogo agora -- saia do mundo antes de apagar");
			return;
		}

		acc.Slots[slot] = null;
		_store?.Gravar(acc);

		// O DISCIPULADO MORRE COM O PERSONAGEM. E o `mst_purge_sig` do DM (`MasterStudent.dm:123`),
		// e aqui ele e obrigatorio por um motivo que la nao existia: a assinatura e
		// `hash(conta, slot)`, entao o proximo personagem criado neste slot **nasce com a mesma
		// assinatura** -- e herdaria mestre e alunos de um morto. Ver `GameServer.Mestre.cs`.
		PurgarAssinatura(ServerPlayer.AssinaturaDe(acc.Conta, slot));

		// O LOG GUARDA O QUE FOI PERDIDO. Nao da pra desfazer, mas da pra saber o que havia --
		// e um "sumiu meu personagem" sem nenhuma linha no log e impossivel de responder.
		GD.Print($"[server] {acc.Conta} APAGOU o slot {slot + 1}: '{c.Nome}' "
			+ $"({c.Raca}, {c.Idade} anos, BP {c.Ficha.BP:0})");

		MandarSlots(peer, acc);
	}

	private void CreateChar(NetPeer peer, int slot, CharacterDraft ficha,
						   Jandirus.Core.Appearance.Appearance visual)
	{
		if (!_logados.TryGetValue(peer, out AccountSave? acc)) { Recusar(peer, "faca login primeiro"); return; }
		if (slot < 0 || slot >= AccountStore.Slots) { Recusar(peer, "slot invalido"); return; }
		if (acc.Slots[slot] != null) { Recusar(peer, "esse slot ja tem um personagem"); return; }

		string motivo = ValidarFicha(ficha);
		if (motivo.Length > 0) { Recusar(peer, motivo); GD.Print($"[server] ficha recusada: {motivo}"); return; }

		string nome = ficha.Name.Trim();
		if (_players.Values.Any(o => string.Equals(o.Name, nome, StringComparison.OrdinalIgnoreCase)))
		{
			Recusar(peer, "ja tem alguem em jogo com esse nome");
			return;
		}

		Fighter lutador = Nascer(ficha, nome);

		// APARENCIA: saneada, nunca recusada. Ela nao da vantagem nenhuma, entao um indice
		// fora da faixa vira o padrao em vez de derrubar a conexao. O que NAO se aceita e
		// caminho de sprite fora do catalogo -- isso e o cliente inventando arquivo.
		string ajuste = _visual?.Sanear(visual, ficha.Race, ficha.Gender) ?? "";
		if (ajuste.Length > 0) GD.Print($"[server] aparencia de {nome} ajustada: {ajuste}");

		// A COR DA AURA NAO SE ESCOLHE: ela e SORTEADA (`rand(0,255)` nos tres canais, como o
		// `CharacterCreation.dm:25-27`), e o sorteio e do servidor. O que chega do cliente aqui e
		// descartado -- nao porque a cor de uma chama de vantagem, mas porque "o cliente escolhe" e
		// "o mundo sorteia" sao duas coisas diferentes e o dono pediu a sorteada.
		//
		// ANULAR **E** SORTEAR: quem responde e o `ParaJogador`, logo abaixo no `Entrar`, derivando
		// de nome + `CriadoEm` (que a linha seguinte acaba de fixar). Sortear um numero aqui e
		// grava-lo seria uma SEGUNDA regra pra a mesma cor -- e a do personagem antigo, que nao
		// passa por este metodo, continuaria sendo a outra.
		visual.CorAura = null;

		// OS CORPOS DAS FORMAS SO PODEM SER SANEADOS AQUI, e nao no `Sanear` visual: quantos slots
		// existem depende da CLASSE, e a classe acabou de ser sorteada logo acima (`Nascer`). O
		// jogador escolheu tres corpos achando que era normal; se saiu Mutante, ele precisa de sete
		// -- e os quatro que faltam sao preenchidos com os padroes do original.
		if (Jandirus.Core.Races.FormasDeFrost.EhFrost(ficha.Race))
			visual.FormasDeFrost = Jandirus.Core.Races.FormasDeFrost.Sanear(lutador.Class, visual.FormasDeFrost);
		else
			visual.FormasDeFrost.Clear();

		long nasceuEm = NowMs();
		var novo = new CharacterSave
		{
			Nome = nome, Raca = ficha.Race, Planeta = ficha.Planet, Genero = ficha.Gender,
			Linhagem = ficha.ChosenClass, Idade = ficha.Age, Visual = visual, Ficha = lutador,
			Historia = ficha.Backstory.Trim(), Porte = ficha.Porte,
			CriadoEm = nasceuEm,
			// O PEDIDO DO JOGADOR VAI PRO DISCO, e nao so pro nascimento: o RENASCIMENTO sai da
			// mesma funcao, e sem este bit gravado quem escolheu o vizinho ressuscitaria no natal.
			PertoDeCasa = ficha.PertoDeCasa,
			SeedDoBerco = Jandirus.Core.Races.Bercos.SementeDoBerco(nome, nasceuEm),
		};

		// ============================ O BERCO SUBSTITUI A TERRA CRAVADA ============================
		// A zona e a posicao saiam daqui como `SpawnZone`/`SpawnPos` -- a Terra, sempre, pra todo
		// mundo. Agora saem do funil, que e o MESMO que a morte usa (`GameServer.Berco.cs`). A
		// ordem importa: o berco depende da CLASSE, e a classe acabou de ser sorteada no `Nascer`
		// logo acima; calcula-lo antes daria o berco de um Saiyajin normal pra um Lendario.
		// ======================================================================================
		Jandirus.Core.Races.Berco berco = BercoDe(novo);
		AplicarBercoNoSave(novo, berco);

		// OS LIMIARES SAO SORTEADOS AGORA, uma vez, e vao pro disco junto do personagem.
		// Cada Saiyajin tem o proprio BP de SSJ (o `rand(9,13)/10` do `statsaiyan.dm:50-56`).
		// Re-sortear no login faria o jogador virar SSJ e destransformar pra sempre.
		novo.Limiares = Jandirus.Core.Forms.LimiaresPessoais.Rolar(
			novo.Raca, lutador.Class, Jandirus.Core.World.Espaco.Misturar(
				(ulong)novo.CriadoEm, (ulong)slot, (ulong)nome.GetHashCode()));

		acc.Slots[slot] = novo;
		_store?.Gravar(acc);

		// E DE NOVO NA CRIACAO, e nao e redundancia: um save de antes desta limpeza (ou um slot
		// esvaziado por fora do jogo) deixaria vinculo pendurado numa assinatura que este
		// personagem novo acaba de reivindicar. O DM chama o `mst_purge_sig` exatamente aqui,
		// pela mesma razao (`RankAssign.dm:28`).
		PurgarAssinatura(ServerPlayer.AssinaturaDe(acc.Conta, slot));
		GD.Print($"[server] {acc.Conta} criou '{nome}' no slot {slot + 1} | {novo.Raca}/{lutador.Class} "
			+ $"| berco {berco.Planeta} ({berco.Motivo}{(berco.Despejado ? ", despejado" : "")})");

		Entrar(peer, acc, slot, novo);

		// A DICA DE CLASSE, e ela e a UNICA pista que o jogador jamais recebe sobre a propria
		// linhagem -- uma frase no chat, na criacao, que sugere sem dizer. Sem esta linha a
		// classe simplesmente nunca e insinuada, e a regra "classe nunca aparece" vira
		// "classe nao existe".
		if (_byPeer.TryGetValue(peer, out ServerPlayer? recem))
		{
			// A HISTORIA DO BERCO VEM ANTES DA DICA DE CLASSE, e a ordem e a da narrativa: primeiro
			// onde voce acordou, depois quem voce parece ser. Ver `HistoriaDoBerco` pro porque de
			// nenhuma das duas frases dizer a classe.
			Avisar(recem, HistoriaDoBerco(berco));
			DicaDeClasse(recem);
		}
	}

	// =====================================================================
	// PASSO 3: PISAR NO MUNDO
	// =====================================================================
	private void Entrar(NetPeer peer, AccountSave acc, int slot, CharacterSave c)
	{
		// O BAN E CONFERIDO AQUI TAMBEM, e nao so no `Login`.
		//
		// Entre digitar a senha e escolher o personagem existe uma TELA -- e nela a conta vive em
		// `_logados`, sem `ServerPlayer` nenhum. Banir alguem parado ali nao derrubava ninguem (o
		// laco do ban varre `_players`) e, quando a pessoa clicava no slot, este metodo a deixava
		// entrar. Pior: o `Persistir` seguinte regravava o arquivo a partir dessa copia velha e
		// APAGAVA o banimento do disco. O portao que leva ao mundo tem que conferir tambem.
		if (acc.Banida)
		{
			Recusar(peer, "esta conta esta banida deste servidor");
			GD.Print($"[server] conta banida '{acc.Conta}' barrada na entrada do mundo");
			peer.Disconnect();
			return;
		}

		var pl = new ServerPlayer { Id = _nextId++, Peer = peer, LastInputMs = NowMs() };
		AccountStore.ParaJogador(c, pl);
		pl.Conta = acc.Conta;
		pl.Slot = slot;
		// A ZONA VOLTA INTEIRA: tipo + nome + seed. Ver `CharacterSave.ZonaTipo`.
		//
		// SAVE ANTIGO (sem tipo) cai no pre-feito, que era o comportamento de antes -- e o certo
		// pra ele, porque quando o save foi escrito so existia esse caso no disco.
		pl.Zone = c == null ? SpawnZone : new ZoneKey(c.ZonaTipo, c.Zona, c.ZonaSeed);

		// ============================ O BERCO DESTE CORPO, UMA VEZ POR LOGIN ============================
		// Calculado AQUI e guardado, porque ele nao muda enquanto o personagem existir -- e porque
		// quem vai precisar dele (a morte, o verb `spawn`, o admin) so tem o `ServerPlayer` na mao.
		// Recalcular a cada morte custaria o exilio inteiro (ate 96 celulas do universo) dentro do
		// tique, pra chegar sempre no mesmo planeta.
		//
		// PERSONAGEM ANTIGO GANHA UM AQUI: a semente e derivada de nome + `CriadoEm` quando o save
		// nao a tem (ver `AccountStore.ParaJogador`), entao quem nasceu na Terra cravada passa a ter
		// o berco que teria se tivesse nascido hoje. Ele NAO e movido -- continua exatamente onde
		// deslogou --, mas a partir de agora e pra la que ele volta ao morrer.
		// ===========================================================================================
		pl.Berco = c == null ? default : BercoDe(c);

		OndeEsteCorpoPodeAcordar(pl);

		// ...E QUEM DESLOGOU DENTRO DE UMA NAVE QUE FOI DESTRUIDA no meio-tempo tambem. A guarda
		// acima so olha `KindPremade`, entao o interior de nave (que e `KindInterior`) passava
		// batido -- ver `ResgatarDeInteriorMorto`, que e onde o caso esta escrito por inteiro.
		ResgatarDeInteriorMorto(pl);

		// POSICAO ZERADA (save antigo, ou canto do mapa) cai no ponto de chegada da zona em que o
		// corpo esta -- e nao no berco: quem deslogou em Namek tem que acordar em Namek.
		if (pl.Pos.X == 0 && pl.Pos.Y == 0) pl.Pos = PontoDeNascimento(pl.Zone);

		// OS PODERES CONCEDIDOS (admin) SAO DECIDIDOS AQUI, e ANTES do `AplicarPoderes` la embaixo.
		// Eles vao pra `PoderesConcedidos` e nao pra `Poderes` de proposito: `AplicarPoderes` refaz
		// `Poderes` do zero a partir das skills, e escrever ali seria escrever pra ser apagado --
		// que e exatamente o defeito que fazia o host nunca receber a aba. Ver `GameServer.Admin.cs`.
		ConcederPoderes(pl, peer, acc);

		if (_portaDeTeste) NascerNaPorta(pl);
		pl.Facing = Facing.South;
		// --espeedteste, so bancada: o stat BASE, e o `Statify` na hora pra o `Espeed` (e a ficha que
		// sai logo abaixo) ja nascerem com ele -- senao o primeiro tique corrigiria o cliente.
		if (_speedStatDeTeste > 0) { pl.Ficha.speed = _speedStatDeTeste; pl.Ficha.Statify(); }
		pl.SpeedStat = MoveRules.SpeedStatFrom(pl.Ficha.Espeed);
		PrepararCombate(pl, c);
		PrepararSkills(pl, c);
		PrepararCustomizadas(pl, c);
		pl.Niveis = new Jandirus.Core.Skills.NiveisDeSkill();
		pl.Niveis.DoSave(c?.Niveis);

		// AS DUAS PECAS DO KI LIBERADO SAIRAM DAQUI e viraram `LiberarOKi` (GameServer.Skills.cs):
		// o `admin_liberar_ki` concede exatamente a mesma coisa em runtime, e duas copias da mesma
		// concessao e o jeito conhecido de uma delas envelhecer.
		//
		// A CHAMADA MORA ENTRE O `DoSave` E O `Aplicar`, e as duas bordas importam:
		//   * DEPOIS do `PrepararSkills`/`new NiveisDeSkill`, que sao quem CRIA `pl.Livro` e
		//     `pl.Niveis` -- a primeira versao disto rodava antes e derrubava o servidor com nulo,
		//     a mesma armadilha que o comentario do `--bpteste`, logo abaixo, ja registrava ter
		//     acontecido com o `pl.Combate`;
		//   * ANTES do `Aplicar`/`AplicarEfeitos`, pra o degrau e a skill entrarem numa aplicacao
		//     so, em vez de duas.
		if (_kiDeTeste) LiberarOKi(pl);

		pl.Niveis.Aplicar(pl.Ficha);   // o nivel veio do disco; o que ele soma, nao

		AplicarPoderes(pl);
		AplicarEfeitos(pl);

		// ============================ O CORPO AINDA AGUENTA? ============================
		// `AgeCheck()` no login (`Login.dm:345`) -- a SEGUNDA das duas portas do original, ao lado do
		// virar do ano. Aqui ela vale por um motivo diferente do de la: neste port a idade nao anda
		// sozinha (nao ha calendario), entao o login nunca vai encontrar alguem que envelheceu
		// offline. O que ele PEGA e o save que ja estava do outro lado da linha -- idade posta por
		// admin, personagem importado, ou a propria Sala do Tempo num servidor que caiu no meio.
		// DEPOIS do `PrepararCombate` e do `AplicarPoderes`, e nao antes: `Morrer()` precisa do
		// `pl.Combate`, e a primeira versao de checagens assim neste arquivo saiu calada por rodar
		// cedo demais (ver o comentario do `--feridateste` logo abaixo).
		// ============================ A DIVIDA DO DESEJO VEM ANTES DA VELHICE ============================
		// `Aging.dm:114-122` roda **antes** de qualquer guarda de nao-envelhecer, e a ordem e do DM: o
		// preco do "Mais Forte do Universo" passa por cima de imortal, vampiro e Deus da Destruicao.
		// Aqui ele vem antes do `ConferirMorteDeVelhice` pelo mesmo motivo -- e porque as duas mortes
		// marcam `aged_out`, e a que cobra a divida tem que ser a que anuncia o porque.
		// ============================================================================================
		ConferirDividaDoSupremo(pl);

		ConferirMorteDeVelhice(pl);

		// A LINGUA DOS DEUSES no login -- a cadeia de `sdb_login_check()` do DM
		// (`ProceduralSpace.dm:1464`, chamada de `Login.dm:327`). E uma das DUAS bocas obrigatorias:
		// sem ela, ninguem de sangue Kai ou Demigod aprenderia nunca (eles nao passam por cargo).
		// Ver `ConferirALingua`.
		ConferirALingua(pl);

		// Positiva, e nao so o alarme -- ver `ConferirKiLiberado`.
		if (_kiDeTeste) ConferirKiLiberado(pl, "--kiteste");

		// BANCADA: `--bpteste N` da BP a quem entrar. Existe porque a escada de transformacao
		// comeca em 1,5 MILHAO de BP base e um personagem novo nasce com 9 -- sem isto nao ha
		// como exercitar transformacao num teste automatico. So vale em servidor de teste.
		if (_bpDeTeste > 0) { pl.Ficha.BP = _bpDeTeste; pl.Ficha.Statify(); }
		if (_techDeTeste > 0) pl.Ficha.techskill = Math.Max(pl.Ficha.techskill, _techDeTeste);
		if (_zeniDeTeste > 0) pl.Ficha.Zeni = Math.Max(pl.Ficha.Zeni, _zeniDeTeste);
		if (_marcosDeTeste > 0 && pl.Livro.MarcosLivres < _marcosDeTeste) pl.Livro.Conceder(_marcosDeTeste);
		// DEPOIS do `PrepararCombate`, e nao antes: e ele que CRIA o corpo. A primeira versao
		// disto rodava la em cima e saia calada, porque `pl.Combate` ainda era nulo -- exatamente
		// o mesmo tropeco que o `--quebrarteste` deu com a zona.
		if (_feridaDeTeste) FerirDeTeste(pl);
		foreach (string sk in _skillsDeTeste) pl.Livro.Dar(sk);
		AplicarNiveisDeTeste(pl);   // `--nivelteste`: as skills que ele ja tem, no nivel pedido (depois do Dar de cima)

		// BANCADA DO EMBATE: o host fica mais forte, pra a vantagem de poder ter o que multiplicar.
		if (_clashSempre && EhHost(peer)) { pl.Ficha.BP *= BpDoHostNoTeste; pl.Ficha.Statify(); }

		// BANCADA DO DESVIO: o mesmo, com o multiplicador da linha de comando. Ver `_bpDoHostNaEsquiva`.
		if (_bpDoHostNaEsquiva > 0 && EhHost(peer)) { pl.Ficha.BP *= _bpDoHostNaEsquiva; pl.Ficha.Statify(); }

		// BANCADA DA VOLTA: nasce colado na beirada oeste, na altura do meio.
		if (_nascerNaBeirada && MapaDaZonaOuCatalogo(pl.Zone) is { } m)
			pl.Pos = new Vec2(6 * ZoneCollision.TileSize, m.Height / 2 * ZoneCollision.TileSize);

		// BANCADA DO ESTRAGO: nasce encostado no cenario mais proximo. Ver `EncostarNaParede`.
		if (_nascerNaParede) EncostarNaParede(pl);

		// BANCADA DA AGUA: nasce na margem / dentro / por cima de um lago estreito, e alguem nasce na
		// outra margem. Ver `PorNaBeiraDoLago` -- ela nao inventa colisao nenhuma, so escolhe ONDE a
		// pergunta vai ser feita.
		if (_aguaDeTeste) PorNaBeiraDoLago(pl);

		// BANCADA: nasce direto num mundo sorteado (ver `--geradoteste`).
		if (_nascerEmGerado)
		{
			// O PRIMEIRO PLANETA GERADO NA DIAGONAL. Antes isto varria chunk por chunk atras de um
			// hash que calhasse de dar planeta (1 em 40); agora toda celula tem um sistema, entao a
			// primeira que nao for anulada ja responde -- e o corpo escolhido e o da orbita mais
			// interna, que e o mais perto da estrela e portanto o caso mais apertado pra bancada.
			PlanetaNoEspaco? achado = null;
			for (int i = 1; i <= 400 && achado == null; i++)
				if (Sistemas.Do(SeedDoUniverso, i, i) is { Ancorado: false } sis) achado = sis.Planeta(0);
			if (achado is { } gerado)
			{
				// NASCE EM ORBITA, SOBRE O DISCO -- e nao com o pe no chao do planeta.
				//
				// Cravar a `ZoneKey` do planeta parecia mais direto e pulava justamente o que a
				// bancada promete exercitar: o pouso. Ninguem chamava `PousarEmProcedural`, entao o
				// mundo nunca era gerado NO SERVIDOR -- `MapaDaZona` devolvia nulo, o passo era
				// validado so por velocidade e dava pra atravessar montanha. A bancada dizia
				// "o pouso, a colisao e a pintura sao os mesmos" e nenhum dos tres acontecia.
				//
				// Em cima do disco, o `TickDoEspaco` faz o resto no proximo tique, pelo caminho de
				// verdade: encomenda o mundo, segura o corpo em orbita enquanto ele nasce, e pousa.
				pl.Zone = ZonaDoEspaco;
				pl.Pos = gerado.Pos;
				GD.Print($"[server] BANCADA: {pl.Name} nasce em orbita de {gerado.Nome} (seed {gerado.Seed})");
			}
			else GD.PushWarning("[server] BANCADA: nao achei planeta gerado em 400 chunks");
		}

		// A FORMA, O DISCIPULADO E A DISCIPLINA VOLTAM DO DISCO -- ver
		// <see cref="RestaurarFormaEDisciplina"/>. O bloco virou metodo porque a bancada
		// `--mestrevivo` precisa RELOGAR de verdade, e um segundo caminho de "save -> corpo" seria a
		// duplicata que a PARTE 3 da spec chama de "duas casas pra uma formula": a bancada mediria a
		// copia dela e o jogo continuaria com a sua.
		List<string> maestriasDescartadas = RestaurarFormaEDisciplina(pl, c);

		_logados.Remove(peer);
		_contas[peer] = acc;
		_players[pl.Id] = pl;

		// BANCADA: `--quebrarteste N` derruba cenario na zona de quem entrou. TEM que ser AQUI, e
		// nao la em cima: o `--geradoteste` reescreve `pl.Zone` no meio do caminho, e a versao
		// anterior quebrava a zona de spawn enquanto o jogador nascia noutro planeta -- o teste
		// procurava o estrago onde ele nao estava e dizia "nao houve estrago" com a flag ligada.
		// ============================ QUEBRA DEPOIS DE ENTRAR, e nao durante ============================
		// Rodando aqui, o estrago acontece ANTES de o jogador entrar na lista da zona: ele nao recebe
		// o `MandarCelulaCaida` de cada celula, recebe a LISTA do que ja estava caido -- e o cliente
		// aplica essa lista sem poeira, de proposito ("o estrago e velho, o efeito nao").
		//
		// Ou seja, a bancada da destruicao media o mapa mudando e NUNCA media o efeito. Meio segundo
		// de atraso poe o teste no caminho de producao: parede caindo com alguem olhando.
		if (_quebrarDeTeste > 0)
		{
			ServerPlayer alvo = pl;
			SceneTreeTimer t = GetTree().CreateTimer(0.5);
			t.Timeout += () =>
			{
				if (_players.ContainsKey(alvo.Id)) QuebrarCenarioDeTeste(alvo);
			};
		}

		// BANCADA DO VOO (`--vooteste`): da a skill no nivel 2, que e o que destrava `Fly` E
		// `Superflight` -- os dois verbs que a bancada precisa exercitar.
		//
		// A SKILL E CONCEDIDA, E NAO O VOO. A bancada tem que passar pela porta de verdade (o
		// `AlternarVoo`, o custo de decolagem, o dreno por tique): ligar `pl.Voando = true` aqui
		// mediria um atalho e nao o jogo.
		if (_vooDeTeste)
		{
			// A PORTA NOVA, e nao a antiga: metade da maestria de Ki. Dar a skill `flying` aqui
			// mediria o caminho do DM e deixaria o portao de verdade -- o que o jogador vai
			// atravessar -- sem teste nenhum.
			pl.Livro.Dar("/datum/skill/mind/Ki_Unlocked");
			pl.Niveis.Por("/datum/skill/mind/Ki_Unlocked", MaestriaQueDestravaVoo);
		}

		// A BANCADA DE FORMAS roda uma vez, no primeiro que entra.
		if (_formasDeTeste) { _formasDeTeste = false; RodarBancadaDeFormas(pl); }

		// A DO FROST tambem, e pelo mesmo motivo: metade do que ela mede e o LOGIN (a forma de
		// repouso escrita pelo `Catalogo.IdDoPiso`) e a outra metade e o tique (o motor do Mutante).
		// Nenhuma das duas existe sem um corpo dentro do mundo.
		if (_frostDeTeste) { _frostDeTeste = false; RodarBancadaDoFrost(pl); }

		// A DE NPC tambem, e pelo mesmo motivo de estar AQUI e nao no boot: ela poe corpos numa
		// zona e confere que eles aparecem na lista dela -- e sem ninguem no mundo, "aparecer" nao
		// tem como ser medido. Ver `RodarBancadaDeNpc`.
		if (_npcDeTeste) { _npcDeTeste = false; RodarBancadaDeNpc(pl); }

		// A DO POVOAMENTO tambem, e ela precisa MAIS ainda de estar aqui: metade do que ela mede e
		// sobre um alvo com `Peer` -- e o cidadao so descongela quando ha gente de verdade na zona
		// (o `planet_has_players`). Rodada no boot, ela mediria o anti-lag e chamaria isso de
		// pacifismo.
		if (_povoDeTeste) { _povoDeTeste = false; RodarBancadaDePovoamento(pl); }

		// A DA IA depois da de NPC, e a ordem importa: ela usa o molde `guardiao_saiyajin` pra
		// provar que o comando da IA respeita o "tem a forma e nao a usa", e os moldes precisam ter
		// carregado (o `CarregarMoldes` grita no boot se algum for contraditorio).
		if (_iaDeTeste) { _iaDeTeste = false; RodarBancadaDeIa(pl); }

		// Depois das outras: ela nasce e remove corpos proprios, e a de IA ja faz o mesmo -- rodar
		// as duas no mesmo login com a ordem invertida nao muda nada, mas manter a ultima e a mais
		// nova facilita ler o console quando as duas rodam juntas.
		if (_ligadosDeTeste) { _ligadosDeTeste = false; RodarBancadaDosLigados(pl); }

		// A DE PONTA A PONTA depois da de IA, e a ordem importa pelo mesmo motivo: ela usa os moldes
		// (a familia da IA forja duelistas de bancada) e precisa de alguem com `Peer` -- o boneco que
		// ela deixa de pe nasce na zona de quem acabou de entrar, e e o alvo da metade viva.
		if (_pontaDeTeste) { _pontaDeTeste = false; RodarBancadaDePontaAPonta(pl); }
		// E TODA ENTRADA DEPOIS DESSA re-arma o corpo -- a metade viva RELOGA de proposito, e quem
		// volta encontra o Ki gasto nas rajadas de antes. Ver `_pontaLigada`.
		else if (_pontaLigada) ArmarAMetadeViva(pl);

		// A DA CADEIA DE SAGAS tambem no primeiro login, e ela precisa MESMO de alguem com `Peer`:
		// metade do que ela mede depende disso -- o marco de BP so olha jogador de verdade
		// (`check_triggers` exige `M.client`), a media do servidor que o pino usa so conta quem tem
		// dono, o ultimato ESPERA se nao ha ninguem online, e a recompensa e paga a uma conta.
		if (_sagaDeTeste) { _sagaDeTeste = false; RodarBancadaDeSagas(pl); }

		// A DE "QUEM E GENTE" pelo mesmo motivo, e ela precisa MESMO de um `Peer` de verdade: metade
		// das familias e um CONTROLE (o jogador no marco, o jogador batendo no chefe, o jogador que
		// pousa e vira presa), e a outra metade EMPRESTA esse `Peer` a corpos sem dono pra ver o laco
		// de producao decidir com o defeito injetado.
		if (_genteDeTeste) { _genteDeTeste = false; RodarBancadaDeQuemEGente(pl); }
		if (_bioDeTeste) { _bioDeTeste = false; RodarBancadaDoBio(pl); }

		// A DO OUTRO MUNDO precisa de `Peer` pelo mesmo motivo, e por um a mais: a triagem da morte
		// **so leva pro alem quem tem dono na tela**, entao sem um corpo de verdade a familia central
		// da bancada nao teria como acontecer. Ela vem depois das outras porque mata o host -- e as
		// que rodam antes contam com ele vivo.
		if (_alemDeTeste) { _alemDeTeste = false; RodarBancadaDoAlem(pl); }

		// A DO CADAVER, logo depois e pelas mesmas duas razoes: ela tambem mata o host (a triagem so
		// leva pro alem quem tem dono na tela) e tambem o devolve inteiro. Ela vem DEPOIS da do alem
		// porque mede a mesma morte de outro angulo -- e se as duas cairem juntas, o defeito e da
		// viagem e nao do corpo.
		if (_cadaverDeTeste) { _cadaverDeTeste = false; RodarBancadaDoCadaver(pl); }

		// A DOS DOIS CORPOS logo depois da do cadaver, e pela terceira vez a mesma razao: ela tambem
		// mata o host (a familia da viagem mede a triagem de verdade, e a triagem so leva pro alem quem
		// tem dono na tela) e tambem o devolve inteiro. Vem DEPOIS porque ela poe corpos no mundo, e as
		// bancadas de cima contam corpos.
		if (_doisCorposDeTeste) { _doisCorposDeTeste = false; RodarBancadaDosDoisCorpos(pl); }

		// A DO QUADRO DA MORTE logo depois das tres da morte, e a ordem tem razao: ela e a unica que
		// roda o `Tick()` INTEIRO -- varias centenas de vezes --, entao o mundo em que ela mede ja
		// passou por tudo o que as outras poem e tiram. Ela nao mata o host (forja o proprio algoz e
		// as proprias vitimas, com `Peer` emprestado), mas precisa que ele exista: sem um `Peer` pra
		// emprestar, `Gente.EhJogador` responde NAO e a triagem nunca chega na viagem -- que e o
		// caminho que derrubava o tique.
		if (_tiqueDaMorteDeTeste) { _tiqueDaMorteDeTeste = false; RodarBancadaDoTiqueDaMorte(pl); }

		// A DA DESTRUICAO DE PLANETA depois da das sagas, e a ordem tem razao: a bancada das sagas
		// leva um elo ate o ultimato e AGORA isso destroi um planeta de verdade. Rodando antes, a
		// bancada da destruicao mexeria num registro que a das sagas ainda vai escrever -- e as
		// duas passariam ou cairiam por motivo alheio.
		//
		// Ela tambem precisa de alguem com `Peer`: metade do que mede depende disso -- o "servidor
		// vazio adia" so tem como ser medido tirando o `Peer` de alguem que o tinha, e a evacuacao
		// e sobre um corpo que recebe pacote.
		if (_planetaDeTeste) { _planetaDeTeste = false; RodarBancadaDePlaneta(pl); }

		// A DA CONQUISTA DEPOIS DA DESTRUICAO, e a ordem tem razao: ela pergunta "o planeta ainda
		// existe?" ao registro dos mortos e MATA um mundo gerado de proposito pra ver o dominio cair
		// junto. Rodando antes, ela mexeria num livro que a bancada da destruicao ainda vai reescrever.
		//
		// E ela precisa MESMO de alguem com `Peer`: o invasor tem que ter assinatura (dominio e do
		// PERSONAGEM), a invasao fracassa se o invasor "desapareceu" (`Peer == null`), o defensor so
		// engaja quem esta no mundo e o tributo cai num bolso que persiste.
		if (_conquistaDeTeste) { _conquistaDeTeste = false; RodarBancadaDaConquista(pl); }

		// A DAS ESFERAS DEPOIS DA CONQUISTA, e a ordem e cadeia: `ErguerEstatua` pergunta ao livro dos
		// DOMINIOS quem manda no planeta (e o `conq_owner_sig` do original), e a bancada da conquista
		// mexe nesse livro inteiro. Rodando antes, esta aqui mediria dominios que aquela ainda vai
		// escrever e apagar.
		//
		// E ela precisa de alguem com `Peer` por tres razoes proprias: o set e de uma ASSINATURA (que
		// so existe com conta e slot), o `MoveToZone` so mexe em quem esta no `_players`, e o claim da
		// Super Esfera cai quando o disputante "saiu do mundo" -- que e literalmente `Peer == null`.
		if (_esferaDeTeste) { _esferaDeTeste = false; RodarBancadaDasEsferas(pl); }

		// A DOS DESEJOS pelas MESMAS tres razoes da de cima, mais uma sua: o bloqueio do criador
		// compara ASSINATURA (`CreatorSig` contra a de quem pede) e a procuracao guarda a assinatura do
		// pedinte. Rodada sem conta e sem slot, ela compararia vazio com vazio e ficaria verde.
		if (_desejoDeTeste) { _desejoDeTeste = false; RodarBancadaDosDesejos(pl); }

		// A DO PORUNGA DEPOIS DAS DUAS, e a ordem e cadeia: ela ergue a propria estatua na TERRA e
		// precisa que nao haja outra la -- e as duas de cima erguem e derrubam as delas. Rodando antes,
		// ela cairia no "ja existe uma Estatua do Dragao aqui" da bancada vizinha.
		//
		// E ela precisa de alguem com `Peer` por duas razoes proprias: o set de jogador que faz o
		// desejo e de uma ASSINATURA (que so existe com conta e slot), e a checagem "quem carregava a
		// esfera foi AVISADO" le o que o jogador OUVE -- num corpo sem dono nao ha o que ouvir.
		if (_porungaDeTeste) { _porungaDeTeste = false; RodarBancadaDoPorunga(pl); }

		// A DO AVESSO DEPOIS DAS DUAS, e a ordem tambem e cadeia: ela ergue a propria estatua e por isso
		// precisa que nao haja outra na Terra -- e as duas de cima erguem e derrubam a delas. Rodando
		// antes, ela cairia no "ja existe uma Estatua do Dragao aqui" da bancada vizinha.
		//
		// E ela precisa de `Peer` mais do que qualquer uma: o canal de claim da Super Esfera cai na
		// PRIMEIRA condicao de aborto quando o disputante "saiu do mundo", e isso e literalmente
		// `Peer == null`. Um corpo forjado nunca fecha um claim -- entao quem atravessa a corrente aqui
		// tem que ser o testador de verdade.
		if (_avessoDeTeste) { _avessoDeTeste = false; RodarBancadaDoAvesso(pl); }

		// A DA PROVA POR ULTIMO ENTRE AS DE NPC, e pela soma dos motivos das outras: ela povoa o mundo
		// inteiro (precisa de alguem com `Peer` na zona, senao os cidadaos congelam pelo anti-lag),
		// dispara sagas (o marco de BP so olha jogador de verdade) e paga reputacao (que e de uma
		// conta). Rodada no boot, ela mediria um servidor vazio e chamaria isso de resultado.
		if (_provaDeTeste) { _provaDeTeste = false; RodarBancadaDaProva(pl); }

		// A DO SOL tambem entra aqui e nao no boot, e por uma razao propria: ela forja corpos NO
		// ESPACO e roda o `TickDoEmpurrao`, que varre `_players` -- sem ninguem no mundo o laco de
		// arremesso nao tem sobre quem correr, e "o corpo andou 576 px" mediria a lista vazia.
		// Ver `GameServer.SolTeste.cs`.
		if (_solDeTeste) { _solDeTeste = false; RodarBancadaDoSol(); }

		// A DO VACUO entra aqui pelo mesmo motivo da do sol, mais um proprio: ela precisa de uma ZONA
		// DE CHAO de verdade pro contra-exemplo mais importante dela ("no chao de um planeta ninguem
		// sufoca"), e a unica zona de chao garantidamente carregada e a do host que acabou de entrar.
		// Rodada no boot, ela mediria o vacuo contra um planeta que ninguem abriu.
		if (_vacuoDeTeste) { _vacuoDeTeste = false; RodarBancadaDoVacuo(pl); }

		// A DA NUVEM pelo mesmo motivo das duas de cima, e mais um: ela precisa do CATALOGO com os
		// mapas carregados (os planos `.nuvem` do Caminho da Serpente e do Templo), e no boot o
		// catalogo ainda pode nao ter passado. Ela tambem le a zona do host pra RECUSAR rodar se ele
		// estiver em cima de nuvem que derruba -- e no boot nao ha host pra perguntar.
		if (_nuvemVivaDeTeste) { _nuvemVivaDeTeste = false; RodarBancadaDaNuvemViva(pl); }

		// A DO EMBARALHO depois das outras: ela precisa dos moldes carregados (nasce vilarejo) e
		// mede TEMPO -- rodar no meio de outra bancada mediria o custo dos corpos que a outra forjou.
		if (_embaralhoDeTeste) { _embaralhoDeTeste = false; RodarBancadaDoEmbaralho(pl); }

		// A DA LUA E DA FERA vem LOGO DEPOIS da do embaralho, e pelos mesmos motivos dela mais um: ela
		// precisa dos moldes (nasce Saiyajin pelo caminho de producao) e do HOST com `Peer` (a familia 5
		// mede a porta de plateia com e sem ele na zona, e sem alguem de verdade so daria pra medir o
		// lado "sem"). Ela ADIANTA O RELOGIO DO MUNDO pra achar a lua cheia -- e devolve no `finally`,
		// entao rodar antes de outra bancada que leia o ceu nao a contamina.
		if (_luaFeraDeTeste) { _luaFeraDeTeste = false; RodarBancadaDaLuaDaFera(pl); }

		// O PALCO VIVO monta no primeiro login e nao mede nada: quem julga e a foto do `--diagmacaco`.
		// Ele precisa do host DENTRO da zona (a guarda 6 do gatilho e a plateia) e por isso mora aqui,
		// e nao no boot -- num servidor vazio o Saiyajin ficaria ferido a noite inteira sem virar nada.
		if (_macacoVivo && _macacoVivoId == 0) MontarOPalcoDoMacaco(pl);

		// O PALCO DA ESCADA DO BIO, pelo mesmo motivo e no mesmo lugar do de cima: ele precisa de
		// alguem na tela pra nascer ao lado (a camera segue o host, e o que nao cabe no quadro nao e
		// fotografado). Ver GameServer.BioPalco.cs.
		if (_bioVivo && _bioVivoId == 0) MontarOPalcoDoBio(pl);

		// E O PALCO DOS TRES PEDIDOS VISUAIS, pelo mesmo motivo: os tres corpos se plantam em volta de
		// quem esta olhando. Ver GameServer.BioOlhar.cs.
		if (_bioOlhar && _olharA == 0) MontarOOlharDoBio(pl);
		if (_bioFilme && _filmeF == 0) MontarOFilmeDoBio(pl);

		// A DA NAVE PRECISA DE UM CORPO DE VERDADE, e por dois motivos que nenhum corpo forjado
		// atende: ela passa pelo `ComandoDeTech` (que cobra zeni de uma FICHA e enche uma MOCHILA) e
		// pelo `MoveToZone` (que so mexe em quem esta no `_players`). Ver GameServer.NaveTeste.cs.
		if (_naveDeTeste) { _naveDeTeste = false; RodarBancadaDaNave(pl); }

		// A DO EMBARQUE nao roda nada aqui: ela so ENTREGA o que o robo do cliente precisa pra
		// comecar (tecnologia e zeni). O percurso e do outro lado -- ver GameServer.EmbarqueTeste.cs.
		if (_embarqueDeTeste) PrepararBancadaDeEmbarque(pl);

		// SO AGORA da pra mandar as construcoes: `MandarObras` varre `_players` procurando quem
		// esta na zona, e quem acabou de entrar so esta la depois desta linha.
		MandarObras(pl.Zone);
		// AS ESFERAS SEGUEM AS CONSTRUCOES, e pelo mesmo motivo escrito acima: `MandarEsferas` varre
		// `_players` procurando quem esta na zona. E as SUPER vao junto porque o placar e o radar
		// dourado sao coisa que o jogador precisa ter na tela desde o primeiro quadro -- ver
		// `GameServer.SuperEsferas.cs`.
		MandarEsferas(pl.Zone);
		MandarSupers(pl);
		MandarPortas(pl);
		MandarCenario(pl);
		// AS PECAS DE CORPO NO CHAO, pelo mesmo argumento do cenario derrubado: o `S2C.Decalque` que as
		// plantou saiu uma vez, pra quem estava la -- e quem loga no meio dos 600 s de uma peca precisa
		// ve-la. Ver `GameServer.Pecas.cs`.
		MandarPecas(pl);
		MandarCatalogoDeObras(pl);
		AplicarEstilo(pl);   // o estilo veio do save; os multiplicadores nao
		MandarEstilos(pl);
		// AS TECNICAS INVENTADAS entram junto dos estilos: as duas sao patrimonio que vira BOTAO, e
		// um botao que so aparece depois que o jogador abre a tela certa e um botao que ele nao sabe
		// que tem. Ver `GameServer.Customizadas.cs`.
		MandarCustomizadas(pl);
		// E OS CHEFES JA VISTOS, pelo mesmo argumento: cada um deles e um botao ("enfrentar na
		// mente"), e um botao que so aparece depois de o jogador reencontrar o chefe no mundo seria
		// um botao que ele nao sabe que tem. Ver `GameServer.Mente.cs`.
		MandarChefesDaMente(pl);
		AplicarGravidade(pl);
		_byPeer[peer] = pl;

		// O PLANETA ESFRIOU ENQUANTO ELE ESTAVA FORA? Uma das duas portas de entrada no mundo (a
		// outra e o `MoveToZone`), e ela tem que ser ANTES do `JoinAccepted`/do primeiro snapshot:
		// embaralhar depois seria o cliente ver a cidade inteira dar um pulo. Ver
		// `GameServer.Embaralho.cs`.
		EmbaralharSeEsfriou(pl.Zone);

		ZoneList(pl.Zone.Hash).Add(pl);
		Persistir(pl);

		var w = Protocol.Begin(Protocol.S2C.JoinAccepted);
		w.Put(pl.Id);
		w.PutZone(pl.Zone);
		w.PutVec(pl.Pos);
		w.Put(pl.Name);
		// A FICHA CENSURADA, e nao a crua. Sem isto o corte do sigilo esconde o ROTULO e o
		// servidor continua mandando o NUMERO -- quem abrir o pacote ve tudo, e esconder do
		// olho nao e esconder do jogo.
		FichaVisivel(pl).Write(w);
		w.PutAppearance(pl.Visual);

		// A SEED DO UNIVERSO, JA NO LOGIN.
		//
		// Ela e a chave que faz o cliente conseguir DESENHAR a galaxia sozinho (todo planeta e
		// funcao pura de seed + chunk). Antes ela so chegava no `S2C.Vizinhanca`, que so sai no
		// espaco -- entao a carta estelar em terra firme so tinha os sete pre-feitos, e o motivo
		// disso era invisivel pra quem estava olhando. Custa oito bytes uma vez por login.
		w.Put(SeedDoUniverso);
		peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		// A HORA DO MUNDO LOGO NO LOGIN, e nao no primeiro tique de sincronia: o cliente monta a
		// cena com a luz da hora que ele conhece, e sem isto ele desenharia o primeiro quadro no
		// horario errado e corrigiria na cara do jogador. Ver GameServer.Ceu.cs.
		MandarCeu(pl);
		ClimaDeTeste(pl);   // a bancada `--climateste`, se estiver ligada
		MandarClima(pl);    // e o temporal que ja estava em curso nesta zona

		// OS PLANETAS MORTOS NO LOGIN, e nao so quando algum morre: a carta estelar do cliente e
		// desenhada com o que chegou, e quem entra num servidor onde Vegeta ja explodiu ha semanas
		// nunca receberia o pacote de mudanca. Ver `S2C.Mortos`.
		MandarMortos(pl);

		// A MOCHILA VAI FORCADA no login: a assinatura nasce vazia e uma mochila TAMBEM vazia
		// casaria com ela, entao o pacote nao sairia -- e a tela de inventario ficaria mostrando o
		// do personagem anterior desta sessao de cliente.
		MandarMochila(pl, forcar: true);

		TrocarAparencias(pl);
		TrocarFeridas(pl);

		// QUEM ESTE PERSONAGEM CONHECE. Vai no login como a mochila e pelo mesmo motivo: a aba
		// People e desenhada com o que chegou, e sem isto ela abriria vazia ate a primeira mudanca
		// -- que num personagem antigo e cheio de gente pode nunca vir.
		MandarConhecidos(pl);

		// OS CARGOS TAMBEM VAO NO LOGIN. A lista so saia quando a aba Cargos pedia -- e desde que os
		// verbs de cargo da aba Other so APARECEM pra quem tem o cargo (`Verbo.Mostrar`, pedido do dono),
		// o cliente precisa saber no login que e o Guardiao, senao "Time Chamber: Authorize" so
		// nasceria depois de o jogador abrir a aba Cargos uma vez.
		MandarCargos(pl);

		// O QUE ACONTECEU COM SEUS DOMINIOS ENQUANTO VOCE ESTAVA FORA. E o `conq_login_check` do
		// original (PlanetConquest.dm:208), e ele existe porque quase tudo o que acontece com um
		// dominio -- invasao, perda, revolta do povo -- acontece com o dono offline.
		EntregarRecadosDeConquista(pl);

		// E SE O PLANETA NATAL DESTE PERSONAGEM DEIXOU DE EXISTIR, ele tem que descobrir isso ao
		// entrar -- e nao ao morrer. Quem estava offline quando o mundo acabou nao viu a explosao,
		// nao leu o anuncio e nao tem como saber que o proximo renascimento vai para outro lugar.
		// Com o berco de pe esta linha nao faz nada. Ver `GameServer.Refugio.cs`.
		OferecerORefugio(pl, podeAbrir: true);

		// QUEM VOLTA JA DENTRO DA SALA DO TEMPO REOCUPA A VAGA -- o `htc_login_check()` do DM.
		// Sem isto, relogar seria o jeito de liberar a vaga sem sair pela porta, e a lotacao de
		// duas pessoas viraria uma sugestao. Ver `GameServer.SalaDoTempo.cs`.
		SalaDoTempoNoLogin(pl);

		// E QUEM VOLTA SELADO CONTINUA SELADO -- o `if(isSealed) spawn TestEscape()` do
		// `Login.dm:258`, pelo mesmo motivo da linha de cima: sem esta chamada, deslogar seria a
		// chave mestra de toda prisao do jogo. Ver `GameServer.Selo.cs`.
		SeloNoLogin(pl);

		// ============================ O QUE O SAVE PERDEU, DITO EM VOZ ALTA ============================
		// As quatro formas divinas deixaram de ter maestria propria (ver `Maestrias.Por`). Quem ja
		// tinha registro no disco perde esse numero -- e perder calado e indistinguivel de um defeito:
		// o jogador abriria a aba, veria a barra em 0% e concluiria que o save corrompeu.
		if (maestriasDescartadas.Count > 0)
			Avisar(pl, $"a maestria de {string.Join(" e ", maestriasDescartadas)} nao existe mais: "
					 + "essas formas sao da SKILL, e usa-las agora sobe a proficiencia da disciplina.");

		// A COR DA AURA VAI NO LOG, e nao e enfeite: ela e DERIVADA de nome + `CriadoEm` quando o
		// save nao a tem (ver `AccountStore.ParaJogador`), e uma derivacao que mudasse entre logins
		// seria invisivel de qualquer outro jeito -- o jogador so notaria a chama trocando de cor.
		// Com ela impressa, duas entradas do mesmo personagem provam a estabilidade num `grep`.
		GD.Print($"[server] {pl.Name} entrou (id {pl.Id}) em {pl.Zone} | {pl.Race}/{pl.Class} " +
				 $"| BP {pl.Ficha.BP:0.0} (expresso {pl.Ficha.expressedBP:0}) " +
				 $"| aura {pl.Visual.CorAura}");

		// ============================ AS BANCADAS DE DOIS PROCESSOS ============================
		// AS TRES SAO AS ULTIMAS LINHAS DO `Entrar` de proposito, e o motivo e o mesmo pras tres: o
		// corpo que acabou de chegar so esta na `ZoneList` (e portanto so e visivel a quem ja estava)
		// depois de tudo o que veio acima. Cada uma conta quantos clientes de VERDADE ja entraram e
		// so comeca quando a conta fecha -- entao chamar em todo login e o que faz a ultima entrada
		// disparar a cena.
		//
		// A BANCADA DO CORPO LARGADO E A UNICA QUE ESPERA O **SEGUNDO** LOGIN.
		// Ver `GameServer.MenteVivaTeste.cs`.
		MenteVivaNoLogin();

		// AS DUAS DA VOZ sao mais novas que a copia de 23:07; o gancho delas foi deduzido do cabecalho
		// de cada uma, que manda chamar "no fim do `Entrar`, como a `--menteviva` -- e pelo mesmo
		// motivo". A viva espera `--vozvivagente` clientes (padrao 4); a dupla espera 2.
		VozVivaNoLogin();
		VozDuplaNoLogin();

		// E A DO RESCALDO, que espera DOIS: o "pra todos os jogadores" do dono nao cabe num processo
		// so. Ver `GameServer.DestrocosVivosTeste.cs`.
		DestrocosVivosNoLogin();
	}

	/// <summary>
	/// A ficha e DADO DO CLIENTE. Raca fora da lista do planeta, ou linhagem escolhida numa
	/// raca que nao escolhe, seria personagem forjado.
	/// </summary>
	private static string ValidarFicha(CharacterDraft ficha)
	{
		string motivo = ficha.Validar();
		if (motivo.Length == 0 && Array.IndexOf(CharacterDraft.RacasDoPlaneta(ficha.Planet), ficha.Race) < 0)
			motivo = "raca nao pertence a esse planeta";
		if (motivo.Length == 0 && ficha.ChosenClass.Length > 0
			&& Array.IndexOf(CharacterDraft.EscolhasDeClasse(ficha.Race), ficha.ChosenClass) < 0)
			motivo = "essa raca nao escolhe linhagem";
		return motivo;
	}

	/// <summary>
	/// Apresenta o recem-chegado a quem ja estava na zona, e vice-versa.
	///
	/// A aparencia vai UMA VEZ por pessoa, num pacote proprio -- nao no snapshot. Uma ficha
	/// de aparencia tem nome de estilo e ate quatro caminhos de roupa; mandar isso 30x por
	/// segundo por jogador seria gastar toda a banda pra repetir o que nao muda.
	/// </summary>
	private void TrocarAparencias(ServerPlayer novo)
	{
		List<ServerPlayer> zona = ZoneList(novo.Zone.Hash);

		NetDataWriter Ficha(ServerPlayer p)
		{
			var w = Protocol.Begin(Protocol.S2C.PeerLook);
			w.Put(p.Id);
			// O NOME QUE O MUNDO LE, e nao o do save. Ver `ServerPlayer.NomeDeFusao` -- este e o
			// unico pacote que carrega nome depois do login, entao ele e o unico lugar onde a fusao
			// precisa aparecer. (O `JoinAccepted` fica com o `Name` cru de proposito: ninguem entra
			// no mundo ja fundido -- a fusao e desfeita antes do save.)
			w.Put(NomeVisivel(p));
			// A RACA E O GENERO TAMBEM PASSAM PELO DISFARCE (lote G12): o corpo desenhado sai da raca.
			w.Put(p.Disfarce?.Raca ?? p.Race);
			w.Put(p.Disfarce?.Genero ?? p.Genero);
			// A APARENCIA QUE O MUNDO VE, e nao a do save -- gemea da linha do nome logo acima, e pelo
			// mesmo motivo. Ver `ServerPlayer.LookDeFusao`: a roupa e o cabelo da fusao NAO podem
			// encostar em `pl.Visual`, que vai pro disco a cada 2 minutos.
			w.PutAppearance(VisualVisivel(p));

			// ============================ E O TIPO DE FUSAO DESTE CORPO (0 = nenhuma) ============================
			// Um byte, ao lado da aparencia, porque ele e um fato do CORPO e nao da forma -- exatamente
			// como o `dominada` do `PacoteDeForma`. Ele existe por UMA regra do dono, hoje na segunda
			// versao dela: *"o ssj4 (e suas variantes) quando esta na fusao potara, o cabelo nao fica
			// vermelho e sim na cor normal de cabelo q seria se n fosse uma fusao, so a fusao
			// metamoro/danca q muda a cor do cabelo no ssj4"*.
			//
			// **ERA UM BOOL, E O BOOL NAO BASTA MAIS.** Enquanto a regra era "TODA fusao pinta", saber
			// que o corpo era fusao respondia tudo. Com Danca e Potara divergindo, o pixel precisa do
			// TIPO -- e ele so existia no servidor (`FusaoAtiva.Tipo`). Este byte e o dado atravessando.
			// Os valores sao os `FType` do DM (`Fusion.dm:268-271`), que e o que o `TipoDeFusao` ja usa:
			// 1 Danca, 2 Potara, 3 Namekuseijin. Zero nao e tipo nenhum -- e "este corpo nao e fusao".
			//
			// NAO DA PRA DEDUZIR DO CABELO, e essa e a razao de ele existir: a fusao que nao virou Vegito
			// veste o penteado de quem convidou (`Fusao.CabeloDaFusao`), entao o cliente olhando so a
			// aparencia veria um Goku comum. E nao da pra deduzir do NOME: nome e texto livre.
			//
			// **E NAO ENTROU EM `Appearance`**, que seria o lugar obvio: aquele objeto vai pro disco, e um
			// campo "sou uma fusao" gravado num save e um estado que sobrevive ao que o produziu.
			//
			// O `LookDeFusao != null` CONTINUA SENDO O PORTAO, e nao o `_fundidos`: os dois corpos da
			// fusao estao em `_fundidos` (dono e passageiro), e so um deles esta VESTINDO a fusao. Quem
			// desenha a fusao e quem tem a aparencia dela -- e e ele quem o `PassarOControle` troca.
			// ========================================================================================
			w.Put((byte)(p.LookDeFusao != null && FusaoDe(p.Id) is { } fus
				? (byte)fus.Tipo
				: 0));
			return w;
		}

		NetDataWriter meu = Ficha(novo);
		foreach (ServerPlayer outro in zona)
		{
			outro.Peer?.Send(meu, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			if (outro != novo)
				novo.Peer?.Send(Ficha(outro), Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}

		// ============================ A APARENCIA BASE NAO E A APARENCIA ============================
		// O `PeerLook` acima descreve o corpo de FICHA: cabelo escolhido na criacao, roupa, cor de pele.
		// Ele nao sabe nada de transformacao -- entao ate aqui quem chegava numa zona via um Super
		// Saiyajin 3 desenhado como lutador comum, e o dono do SSJ4 nao via a propria pelagem.
		//
		// PENDURADO DENTRO DESTE METODO, e nao ao lado das duas chamadas dele: "ligar a regra num
		// chamador e esquecer do outro" e, escrito por todo este port, o erro que mais se repetiu aqui.
		// Sao dois caminhos de entrada (login e troca de zona) e os dois ja passam por esta linha; um
		// terceiro que nasca amanha herda a regra de graca. Ver `SincronizarFormas`.
		SincronizarFormas(novo);

		// E A AUREOLA, PELO MESMO MOTIVO E NO MESMO LUGAR. Ela nao cabe no `PeerLook` acima (que
		// descreve a ficha de APARENCIA -- cabelo, roupa, pele -- e nao o estado do corpo) nem no
		// snapshot (os dois bytes de flags estao cheios). Sem esta linha, quem entra no Outro Mundo
		// veria os mortos de la sem aureola nenhuma -- e o Outro Mundo e onde todo mundo tem uma.
		// Ver `GameServer.Alem.cs`.
		TrocarAureolas(novo);
	}

	private static void Recusar(NetPeer peer, string motivo)
	{
		var neg = Protocol.Begin(Protocol.S2C.JoinRejected);
		neg.Put(motivo);
		peer.Send(neg, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Grava o personagem. Chamado ao entrar, de tempos em tempos e ao sair.</summary>
	/// <summary>
	/// GRAVA O PROGRESSO DESTE CORPO. **RECONSTRUIDO** -- os dois acrescimos abaixo se perderam com o
	/// resto do arquivo, e nenhum dos dois e invencao: cada um tem um consumidor que diz o contrato.
	///
	/// ============================ 1. A LIMPEZA FECHA A PORTA DE GRAVAR ============================
	/// `GameServer.Limpeza.cs:47` diz literalmente: *"enquanto ele esta ligado, `Persistir` nao grava
	/// linha nenhuma e `Login` recusa quem chega. Sem ele, o `Drop(peer)` -- que chama `Persistir`
	/// ANTES de soltar -- recria cada conta na saida, e a limpeza se desfaz sozinha no exato gesto de
	/// deslogar todo mundo."* A bancada `--wipeteste` mede as duas metades (grava com o portao
	/// aberto, NAO grava com ele fechado), e sem esta linha a segunda metade nao tem como passar.
	/// ======================================================================================
	///
	/// ============================ 2. A CONTA PODE VIR DE FORA ============================
	/// O caminho normal acha a conta pelo `Peer`. Quem esta gravando um corpo que ja foi solto do
	/// `_contas` -- e a sonda da propria bancada de limpeza -- nao tem esse caminho, e passa a conta
	/// na mao. O padrao nulo mantem todos os chamadores de producao como estavam.
	/// ================================================================================
	/// </summary>
	/// <remarks>
	/// ============================ 3. CORPO FUNDIDO NAO SE GRAVA ============================
	/// **Achado medindo esta etapa, e ele e da familia do `_limpezaEmCurso` logo acima: um estado que o
	/// save nao sabe descrever, entao a porta fecha enquanto ele dura.**
	///
	/// A fusao muda TRES coisas que este metodo grava, e nenhuma delas tem campo no save que diga "isto
	/// e emprestado":
	///
	///   1. **a ZONA do passageiro** (`CharacterStore.cs:734`). Ele esta num bolso `Interior("Selado")`
	///      de uma pessoa so, sem porta e sem ninguem. Gravado la dentro, ele **reloga preso num quarto
	///      branco pra sempre** -- a unica saida (a fusao) morreu com o processo. E exatamente o defeito
	///      que o `GameServer.Fusao.SoltarDaFusao` ja evita no LOGOUT, e o salvamento periodico o
	///      alcancava por baixo, a cada 2 minutos, sem ninguem deslogar;
	///   2. **os STATS do dono** (`Ficha = pl.Ficha`), que estao no "maior de cada" dos dois -- e o
	///      `Fighter` inteiro vai serializado, mods e tudo;
	///   3. **as SKILLS emprestadas** (`Skills = [.. pl.Livro.Aprendidas]`) e o `FuseBuff`, que carrega
	///      o `(A+B)*2`.
	///
	/// Ou seja: uma Metamoro de 15 min atravessa ~7 gravacoes e uma Potara de 30 min ~15. Uma queda do
	/// servidor no meio de qualquer uma delas deixava os dois jogadores **permanentemente** fundidos por
	/// dentro -- um com o BP e os stats do outro somados e um trancado no selo --, e nada no jogo diria
	/// por que. O rework portado ja tinha visto metade disto (ver `ServerPlayer.NomeDeFusao`, que existe
	/// so por causa deste mesmo salvamento periodico); faltava a outra metade.
	///
	/// **O CUSTO E CONHECIDO E E O LADO CERTO DA TROCA:** o que se perde numa queda e o treino feito
	/// DURANTE a fusao (no maximo 30 min, e o teto e a propria energia). O que se ganha e que nenhuma
	/// fusao pode virar permanente por acidente. E o caminho normal nao perde nada: sair do jogo chama
	/// `SoltarDaFusao` **antes** do `Persistir` (ver `Drop`), entao o corpo ja esta desfundido quando
	/// esta linha roda, e o que vai pro disco e a pessoa.
	/// ================================================================================
	/// </remarks>
	private void Persistir(ServerPlayer pl, AccountSave? conta = null)
	{
		if (_store == null || pl.Slot < 0) return;
		if (_limpezaEmCurso) return;
		if (EstaFundido(pl.Id)) return;   // ver o item 3 do <remarks>

		// ============================ 4. PERSONAGEM CONSUMIDO NAO SE GRAVA -- ELE NAO EXISTE MAIS ============================
		// Da familia das duas linhas acima, e a mais grave das tres: aqui o save **ja foi apagado do
		// disco** (a fusao Namekuseijin, regra N3 do dono). Gravar este corpo o RECRIARIA do nada, montado
		// de um `ServerPlayer` que ainda esta de pe esperando o `Disconnect` chegar -- e "o personagem
		// voltou depois de ser apagado" e um defeito que ninguem consegue explicar depois.
		// Ver `ServerPlayer.PersonagemConsumido` e `ApagarOPersonagemParaSempre`.
		// ================================================================================================================
		if (pl.PersonagemConsumido) return;

		AccountSave? acc = conta;
		if (acc == null && (pl.Peer == null || !_contas.TryGetValue(pl.Peer, out acc))) return;

		pl.Ficha.Class = pl.Class;
		acc.Slots[pl.Slot] = AccountStore.DeJogador(pl, NowMs());
		acc.VistoEm = NowMs();
		_store.Gravar(acc);
	}

	/// <summary>
	/// NASCIMENTO. O SERVIDOR sorteia a classe e monta a ficha -- a tela de criacao so
	/// escolheu identidade e, nas tres racas que escolhem, a linhagem. Sorteio no cliente
	/// seria sorteio ate sair o resultado desejado.
	/// </summary>
	private Fighter Nascer(CharacterDraft ficha, string nome)
	{
		Fighter f = _racas == null
			? new Fighter { Name = nome, Race = ficha.Race, BP = 1 }
			: Birth.Nascer(_racas, ficha.Race, ficha.ChosenClass, _rng, nome);

		// A IDADE E O PORTE SAO DA FICHA, e nao so do cadastro. A idade alimenta a curva de
		// `Envelhecimento` (que multiplica o BP) e o porte mexe direto nos mods -- as duas coisas
		// tem que estar valendo ANTES do primeiro `Statify`, senao o personagem nasce com os
		// numeros de outro e so se acerta no proximo recalculo.
		f.Idade = ficha.Age;
		AplicarPorte(f, ficha.Porte);
		f.Statify();
		return f;
	}

	/// <summary>
	/// O PORTE DO CORPO nos mods do lutador -- o passo "TIPO DE CORPO" do original, que faltava.
	///
	/// ============================ APLICA-SE UMA VEZ, NO NASCIMENTO ============================
	/// E a unica escolha da criacao que mexe em numero, e ela e PERMANENTE. "Permanente" aqui nao
	/// quer dizer "reaplicada todo login": o `CharacterSave` serializa o `Fighter` INTEIRO, mods
	/// inclusos, entao o efeito ja volta do disco pronto. Chamar isto de novo no login MULTIPLICARIA
	/// os mods outra vez, e o personagem ficaria mais rapido (ou mais lento) a cada vez que entrasse
	/// no jogo -- um bug que so apareceria depois de dias e pareceria progressao.
	///
	/// O campo `CharacterSave.Porte` existe pra MOSTRAR a escolha e pra um recalculo futuro saber
	/// de onde partir, e nao pra ser reaplicado.
	/// ==========================================================================================
	/// </summary>
	private static void AplicarPorte(Fighter f, string porte)
	{
		CharacterDraft.AjusteDePorte a = CharacterDraft.PorteDoCorpo(porte);
		f.speedMod *= a.Speed;
		f.kioffMod *= a.KiOff;
		f.physoffMod *= a.PhysOff;
		f.physdefMod *= a.PhysDef;
		f.kidefMod = f.kidefMod * a.KiDef + a.KiDefSoma;
		f.kiregenMod += a.KiRegenSoma;
	}

	private void Input(NetPeer peer, NetPacketReader reader)
	{
		if (!_byPeer.TryGetValue(peer, out ServerPlayer? pl)) return;

		uint seq = reader.GetUInt();
		uint tempoMs = reader.GetUInt();   // o relogio do CLIENTE quando `claimed` valia -- layout em `Protocol.InputCorrendo`
		Vec2 claimed = reader.GetVec();
		byte flags = reader.GetByte();

		// PACOTE OBSOLETO: foi montado antes do ultimo teleporte, entao a posicao que ele afirma
		// e de outra vida. Nao valida, nao grava, nao corrige -- so mantem o relogio do dt em dia
		// (senao o proximo pacote legitimo chega com um dt gigante e vira violacao sozinho).
		if (seq <= pl.SeqDoTeleporte)
		{
			pl.LastInputMs = NowMs();
			return;
		}
		pl.SeqInput = seq;

		// ============================ A HORA EM QUE A POSICAO VALIA ============================
		// O cliente disse "eu estava em `claimed` as `tempoMs` DO MEU relogio". O `Relogio` deste
		// jogador traduz isso pro meu (o minimo em janela de chegada - envio; ver
		// `DeslocamentoDeRelogio`), e o resultado vai no snapshot como idade -- e o que deixa o corpo
		// remoto andar no relogio de quem o move em vez de no do tique. Ver `EntityState.IdadeMs`.
		//
		// O `Math.Min(chegada, ...)` e a unica defesa que precisa: uma hora do futuro (relogio
		// adiantado, cliente mentindo) viraria idade negativa, e idade negativa nao existe -- o
		// carimbo mais novo possivel e "agora". Mentir pra tras so faz o proprio corpo parecer mais
		// velho na tela dos OUTROS; nenhuma regra de jogo le este numero.
		//
		// **O CARIMBO NAO E O DT.** O dt do passo continua sendo medido no MEU relogio, la embaixo:
		// se o cliente pudesse dizer quanto tempo passou, "passou 1 minuto" seria teleporte aprovado.
		//
		// E O CARIMBO VAI EM TODO CAMINHO QUE DEVOLVE A POSICAO COMO VERDADE -- o aceito, o corrigido e
		// o "nao pode andar" -- porque nos tres o corpo ESTA em `pl.Pos` na hora dita. O unico que
		// nao carimba e o arremesso (`TiquesDeVoo`), onde quem sabe a posicao e o `TickDoEmpurrao`;
		// e qualquer escrita do servidor em `pl.Pos` apaga o carimbo sozinha (ver a propriedade).
		// ====================================================================================
		long chegada = RelogioDeQuadrosMs();
		pl.Relogio.Amostrar(chegada - tempoMs, chegada);
		// O VALOR APLICADO E O SUAVIZADO (ver `DeslocamentoDeRelogio.Deslizar`): ele pode ficar uns
		// ms atras ou a frente do extremo, e por isso a hora traduzida pode cair um pouco DEPOIS da
		// chegada -- a idade no snapshot tem sinal justamente pra isso. O teto de 100 ms e so a
		// defesa contra o absurdo (um relogio de cliente que mente muito); em jogo honesto nunca vale.
		long valiaEm = Math.Min(chegada + 100, tempoMs + (long)Math.Round(pl.Relogio.Deslizar(chegada)));
		void Carimbar() { pl.PosMs = valiaEm; pl.PosVemDoCliente = true; }

		var facing = (Facing)(flags & 0x03);
		bool moving = (flags & Protocol.InputAndando) != 0;
		bool querCorrer = (flags & Protocol.InputCorrendo) != 0;
		// SUBIR/DESCER SAO PEDIDOS, como correr. Quem le e o `TickDoVoo`, que so obedece a quem
		// esta voando -- afirmar o bit no chao nao levanta ninguem.
		pl.QuerSubir = (flags & Protocol.InputSubir) != 0;
		pl.QuerDescer = (flags & Protocol.InputDescer) != 0;

		// ============================ SE DEBATER E TENTAR ANDAR PRESO -- E A LEITURA VEM AQUI ============================
		// No original o bloco de escape mora DENTRO do laco de movimento, no ramo `if(grabParalysis)`
		// (`movement handler.dm:238-255`): quem esta agarrado nao anda, mas o gesto de tentar e o que
		// faz o contador subir. Nao ha tecla de escapar em lugar nenhum -- **escapar e andar**.
		//
		// **ESTA LINHA TEM QUE FICAR ANTES DO `PodeMexerOCorpo` LOGO ABAIXO**, e essa e a razao de ela
		// existir separada: aquela porta devolve o pacote INTEIRO de quem esta preso, e depois dela
		// nao ha mais nada dizendo que a pessoa apertou uma direcao. Sem este bit, a unica prova de
		// que alguem se debateu seria um passo que nunca acontece -- e ninguem escaparia nunca.
		//
		// E um bit e nao um contador: quem o consome e o `LutaPraEscapar`, uma vez por tique do DM
		// (0,1 s), e ele o apaga ao ler. Somar aqui daria ao preso mais progresso quanto MAIOR fosse
		// a taxa de quadros do cliente dele.
		// ============================================================================================================
		if (pl.AgarradoPorId != 0 && moving) pl.DebatendoSe = true;

		// QUEM ESTA NO CHAO NAO ANDA -- e quem esta REUNINDO ENERGIA tambem nao.
		//
		// O cliente ja nem tenta (ver LocalPlayer), mas a regra tem que morar aqui: e o servidor
		// que decide onde os corpos estao, e um cliente modificado que ignorasse a trava andaria
		// carregando -- que e exatamente o "op demais" que o dono quis cortar.
		// (e quem esta num ZANZO CLASH tambem nao: la o corpo e do servidor, que o recoloca a cada
		// cruzamento. Sem esta linha o jogador andaria entre os teleportes e a briga de posicao
		// seria com o proprio efeito.)
		// ============================ O MACACO NAO OBEDECE ============================
		// O `ctrlParalysis` do DM (Oozaru.dm:163), agora com o PRAZO que o dono pediu no lugar do
		// interruptor: enquanto a maestria segura as redeas o input passa normalmente, e quando o
		// prazo vence o servidor assume o corpo (`TomarAsRedeas`) e a recusa comeca a valer. Ver
		// `Oozaru.SegundosDeControle`.
		//
		// A recusa entra na MESMA porta de quem esta caido ou carregando, e nao numa segunda: o
		// cliente ja e corrigido de volta por este caminho, e duplicar a regra seria criar duas
		// respostas pra "por que meu personagem nao anda".
		// =============================================================================
		// ============================ AS QUATRO CONDICOES VIRARAM UMA FUNCAO ============================
		// Elas eram escritas aqui e SO aqui, e por isso a IA nao as obedecia -- um NPC andava
		// carregando, que e exatamente o "op demais" que o dono cortou do jogador. Agora sao o
		// `PodeMexerOCorpo` (`GameServer.Ia.cs`), e o atuador da IA chama a MESMA funcao.
		//
		// A POSSE CONTINUA SEPARADA, e nao e desleixo: `SemAsRedeas` nao responde "este corpo pode
		// andar?", responde "quem manda nele?". Pro jogador possuido e recusa; pra a IA e a licenca.
		// Somar as duas numa funcao so faria a IA recusar a si mesma.
		// ==========================================================================================
		if (!PodeMexerOCorpo(pl) || SemAsRedeas(pl))
		{
			pl.LastInputMs = NowMs();

			// ============================ QUEM DIRIGE ESCREVE O `Moving` ============================
			// Nos outros casos desta porta o corpo esta MESMO parado, e zerar aqui e o certo. Com a
			// fera nao: o `TickDosCorposSemDono` acabou de dar um passo e escreveu `Moving = true`, e
			// o input do dono chega dezenas de vezes por segundo por fora do tique. Zerar cego
			// apagaria esse passo quase sempre, e nas outras telas o macaco andaria em soluços --
			// deslizando sem animacao de caminhada. A posicao continua correta; so a ANIMACAO
			// mentiria, que e o tipo de defeito que ninguem consegue descrever direito.
			// ====================================================================================
			if (pl.Cerebro == null) pl.Moving = false;

			// O CORPO ESTA ONDE `pl.Pos` DIZ, e esta desde a hora do input -- e se o servidor o mover
			// neste tique (a fera, o arremesso que comeca), a escrita apaga este carimbo antes de o
			// snapshot sair. Sem carimbar aqui, o nocauteado envelheceria ate saturar e a primeira
			// amostra depois de levantar chegaria com um vao de 255 ms na frente.
			Carimbar();

			var parado = Protocol.Begin(Protocol.S2C.Correction);
			parado.Put(pl.SeqInput);
			parado.PutVec(pl.Pos);
			peer.Send(parado, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			return;
		}

		// O DT E DO SERVIDOR. Se o cliente pudesse dizer quanto tempo passou, "passou 1
		// minuto" viraria teleporte com validacao aprovando.
		long now = NowMs();
		float dt = MathF.Max(0f, (now - pl.LastInputMs) / 1000f);
		pl.LastInputMs = now;

		// CORRER E CONCEDIDO, NAO DECLARADO. O cliente PEDE; quem decide e este metodo, e ele
		// COBRA por segundo de corrida. Sem isto, o bit de "correndo" seria 60% de velocidade
		// gratuita pra qualquer cliente modificado -- e o tipo de coisa que nao da pra
		// detectar depois, porque o movimento fica dentro do que a validacao aceita.
		// ============================ NO AR, O SHIFT E O SUPERFLIGHT ============================
		// A mesma tecla, dois custos que NAO se somam. Voando, quem cobra e o `TickDoVoo` pela
		// formula do DM (`450*flightspeed/flightability` por tique -- o dreno mais caro do jogo);
		// deixar o `PodeCorrer` cobrar por cima seria cobrar duas vezes pelo mesmo gesto, e o
		// jogador cairia do ceu na metade do tempo sem nada explicando por que.
		//
		// A VELOCIDADE continua vindo do mesmo lugar: `MoveRules` multiplica por `correndo`, entao
		// voar com shift e mais rapido exatamente como correr no chao e.
		// =======================================================================================
		MarchaDeVoo(pl, querCorrer);
		bool correndo = querCorrer && moving && (pl.Voando || PodeCorrer(pl, dt));

		// ============================ NO VOO, O SERVIDOR SOLTA ============================
		// Enquanto o corpo esta sendo ARREMESSADO, quem o move e o servidor -- e o cliente esta
		// deslizando na mesma direcao pra o voo nao ficar aos saltos (ver LocalPlayer). Passar esse
		// deslize pela validacao de passo e garantir briga: ele anda a 640 px/s, muito acima do que
		// o orcamento de caminhada permite, entao TODO pacote viraria correcao e o corpo voaria
		// travando. Foi o que o dono viu: "o knock back n ta fluido, o personagem voa meio travado
		// provavelmente por conta do server side verificando a posiçao do player".
		//
		// Nao ha buraco de trapaca aqui: a posicao do voo NAO vem do cliente. Ela e recalculada no
		// `TickDoEmpurrao` e reenviada por correcao a cada tique -- o que se ignora e a OPINIAO do
		// cliente sobre onde ele esta, que durante o arremesso nao vale nada mesmo.
		// =================================================================================
		// A DIRECAO DA QUEDA E A ULTIMA DE PE. So chega aqui quem esta em pe -- o guarda de
		// nocaute la em cima ja devolveu quem caiu --, entao guardar a cada passo deixa
		// congelado, no instante do nocaute, exatamente pra onde ele encarava.
		pl.FacingDaQueda = facing;

		if (pl.TiquesDeVoo > 0)
		{
			pl.Facing = facing;
			pl.Moving = false;
			pl.OrcamentoPx = 0;   // sai do voo sem credito acumulado
			return;
		}

		// ============================ VOANDO ALTO, NAO HA MAPA ============================
		// E o `isflying` do original, e a forma mais barata de dize-lo: `ValidateStep` com mapa nulo
		// so confere VELOCIDADE. Nao ha um segundo caminho de colisao pra manter em dia, e o cliente
		// toma a MESMA decisao pelo MESMO numero (ver `LocalPlayer`) -- que e a unica maneira de
		// isto nao virar briga de posicao entre as duas pontas.
		// =================================================================================
		ZoneCollision? mapa = AtravessandoCenario(pl) ? null : MapaDaZonaOuCatalogo(pl.Zone);

		// ============================ E O PASSO VAI COM O **MODO** ============================
		// E a `testWaters()` do original (`Swim.dm:26-38`) chegando no unico lugar onde ela decide o
		// passo do JOGADOR: quem esta nadando atravessa agua, quem esta a pe nao. Sem esta entrada a
		// validacao perguntava sempre A PE, e o resultado era as duas pontas contando historias
		// diferentes do mesmo gesto -- o cliente (`LocalPlayer.ModoDoCorpo`) previa o passo pra dentro
		// do lago e o servidor o recusava como "atravessou parede", devolvendo o corpo pra margem por
		// correcao. O `pl.Pos` do servidor nunca molhava, o `NadoJaMolhou` nunca virava verdade, e o
		// nado caia sozinho no fim do prazo: **ninguem conseguia entrar na agua andando**.
		//
		// QUEM DECIDE E O SERVIDOR, e nao o cliente -- `pl.Nadando` so o `GameServer.Nado.cs` escreve
		// (ver o comentario do proprio `ValidateStep`: se fosse a afirmacao do cliente, "estou
		// nadando" seria atravessar todo lago do mapa de graca).
		//
		// AS DUAS OUTRAS ENTRADAS DO MODO JA ESTAO RESOLVIDAS ACIMA, e por isso nao ha dois donos da
		// mesma pergunta:
		//   * ARREMESSADO -- o ramo `pl.TiquesDeVoo > 0` deu `return` antes daqui, entao esta entrada
		//     nunca chega valendo. E o mesmo `arremessado: false` que o cliente crava.
		//   * NO AR -- convive de proposito com o `AtravessandoCenario` da linha acima, por limiares
		//     DIFERENTES: acima de `Voo.AlturaQueAtravessa` nao ha mapa nenhum a consultar; ABAIXO
		//     dela (a decolagem e a queda, os primeiros 32 px) o mapa vale e e o modo `Voando` que
		//     impede a parede de agua na beira do lago. O cliente faz as duas leituras iguais, com a
		//     mesma funcao, no mesmo `Advance`.
		// ======================================================================================
		if (MoveRules.ValidateStep(pl.Pos, claimed, dt, pl.SpeedStat, mapa, ref pl.OrcamentoPx, out Vec2 ok, correndo,
								   ModoDeTravessiaDe(pl)))
		{
			pl.Pos = ok;

			// A VOLTA DO PLANETA. Depois do passo, e nao no lugar dele: quem chega na beirada sai
			// pela oposta. Se deu a volta, a correcao certa ja saiu de la e o resto nao se aplica.
			// Ver `GameServer.Volta.cs`.
			if (DarAVolta(pl))
			{
				pl.Facing = facing;
				pl.Moving = moving;
				pl.Correndo = correndo;
				pl.Ficha.dashing = correndo;
				return;
			}
		}
		else
		{
			pl.Pos = ok;
			bool esperada = now < pl.CorrecaoEsperadaAte;
			if (!esperada) pl.Corrections++;
			// Correcao em jogo HONESTO nao deveria existir: as duas pontas rodam a MESMA regra
			// de colisao e de velocidade. Se este aviso aparecer, o cliente esta sendo puxado de
			// volta -- e e exatamente isso que o jogador sente como o personagem tremendo.
			// (A do dash e a excecao: o servidor moveu o personagem de proposito.)
			if (!esperada && pl.Corrections % 30 == 1)
				GD.PushWarning($"[server] {pl.Name}: {pl.Corrections} correcoes de movimento (dt={dt:0.000}s)");

			var w = Protocol.Begin(Protocol.S2C.Correction);
			w.Put(pl.SeqInput);
			w.PutVec(pl.Pos);
			peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}

		// DEPOIS do `pl.Pos = ok` dos dois ramos (a escrita apaga o carimbo) e FORA do ramo da volta
		// do planeta, que devolveu antes: la o corpo foi teleportado e a idade certa e zero.
		Carimbar();

		pl.Facing = facing;
		pl.Moving = moving;
		pl.Correndo = correndo;
		pl.Ficha.dashing = correndo;   // entra na conta de dano: +2 pra quem bate, x1.25 pra quem apanha
	}

	/// <summary>
	/// Correr custa Ki por segundo. Sem energia, o corpo simplesmente nao corre -- e o mesmo
	/// desenho do original, onde cada arranque de dash descontava Ki.
	/// </summary>
	private static bool PodeCorrer(ServerPlayer pl, float dt)
	{
		// O fantasma corre no Outro Mundo (a Serpentina e comprida); o cadaver dos 2 s, nao.
		if ((pl.Ficha.dead && !pl.MortoDePe) || pl.Ficha.KO) return false;

		double custo = pl.Ficha.MaxKi * CustoCorridaPorSegundo * Math.Min(dt, 0.25f);
		if (pl.Ficha.Ki < custo) return false;

		pl.Ficha.Ki -= custo;
		return true;
	}

	/// <summary>Fracao do Ki maximo gasta por segundo de corrida.</summary>
	private const double CustoCorridaPorSegundo = 0.02;

	private void Drop(NetPeer peer)
	{
		_logados.Remove(peer);   // caiu na tela de selecao: nao ha nada a salvar
		if (!_byPeer.Remove(peer, out ServerPlayer? pl)) { _contas.Remove(peer); return; }

		// ============================ QUEM DESLOGA FORA DO CORPO VOLTA PRO CORPO PRIMEIRO ============================
		// ANTES do `Persistir`, e a ordem e a regra inteira. O save grava a ZONA em que o personagem
		// esta (`CharacterStore`), e quem cai enquanto medita esta numa zona `Interior(Interdimension,
		// id)` -- um bolso de uma pessoa so, sem clone, sem porta e sem ninguem. Ao relogar ele
		// acordaria dentro dele, e a unica saida (o verb "Sair da mente") nao existiria mais, porque o
		// `CloneId` morreu com a sessao. O mesmo vale pro leme: o save gravaria o VAZIO DO ESPACO.
		//
		// E o `Logout()` do DM, que restaura *"antes do save"* (`MindMeditate.dm:465-468`) -- so que
		// aqui uma linha so cobre a mente E a nave, porque quem responde "de onde eu volto" e o
		// mecanismo unico. Sem ela sobraria tambem o boneco: um corpo de pe pra sempre, com a ficha de
		// alguem que nao esta mais no servidor.
		// =====================================================================================================
		if (pl.BonecoLargado != null) VoltarDeOndeEstiver(pl, "");

		// A FUSAO SE DESFAZ ANTES DO SAVE, E PELA MESMA RAZAO DA LINHA ACIMA -- so que aqui o preso
		// nao e quem deslogou: e o OUTRO. O passageiro de uma fusao esta num bolso `Interior`
		// ("Selado") de uma pessoa so, sem porta e sem ninguem, e o save grava a zona. Persistido la
		// dentro ele relogaria trancado num quarto branco pra sempre, porque a unica saida (a fusao)
		// morreu com a sessao de quem caiu. Ver `SoltarDaFusao`.
		SoltarDaFusao(pl.Id);

		Persistir(pl);
		// SOLTA DO EMBATE ANTES de sumir da lista: o `Terminar` precisa do corpo pra devolver a
		// visibilidade e o controle ao OUTRO, que continua jogando.
		SoltarDoEmbate(pl.Id);

		// E DA COLISAO DE KI, pelo mesmo motivo -- so que aqui ha mais em jogo: quem sai no meio de
		// uma disputa ENTREGA o encontro (o outro vence por desistencia, como quem solta o raio), e
		// e o `Resolver` que solta a cabeca vencedora e fecha o canal do que saiu. Sem esta linha as
		// duas cabecas ficariam `EmEmbate` pra sempre -- congeladas no ar, sem quem as tiqueasse.
		SoltarDoEmbateDeKi(pl.Id);

		_contas.Remove(peer);   // ANTES de soltar: sair do jogo nao pode custar o progresso
		DespedirCorpo(pl);
	}

	/// <summary>
	/// ESTE CORPO SAIU DO MUNDO -- a metade do logout que apaga o ID. Separada do <see cref="Drop"/>
	/// porque o `Drop` precisa de um `NetPeer` e a bancada nao tem um: e por aqui que ela prova o
	/// relog (`--catalogoteste`, familia 7) pelo MESMO caminho que o jogador de verdade percorre.
	/// Nada e gravado aqui: o `Persistir` ja aconteceu la em cima, com a conta ainda na mao.
	/// </summary>
	private void DespedirCorpo(ServerPlayer pl)
	{
		_players.Remove(pl.Id);
		ZoneList(pl.Zone.Hash).Remove(pl);

		// OS RELOGIOS POR JOGADOR SOMEM JUNTO. Sao dicionarios indexados por id, e id se reusa: sem
		// isto, quem entrasse depois herdaria a carencia de passagem e o relogio de estomago do
		// anterior -- e o dicionario cresceria a sessao inteira.
		EsquecerPassagem(pl.Id);

		// A SESSAO DA SALA ESQUECE SO O RUIDO (janela, aviso, anti-spam da recusa) -- os dias
		// gastos e a prisao vao pro disco no `Persistir` la em cima e continuam valendo. E a
		// janela de saida ser esquecida aqui e o que devolve os 2 minutos inteiros pra quem cair no
		// meio dela: ver `GameServer.SalaSessao.cs`.
		EsquecerSessaoDaSala(pl.Id);

		_relogioDoEstomago.Remove(pl.Id);
		_fomeAcumulada.Remove(pl.Id);
		// ...e o codigo que ele tinha digitado pra destrancar uma nave. Pelo mesmo motivo escrito
		// acima, e com um agravante: id se reusa, e um codigo herdado abriria a nave de outra pessoa.
		_codigoNaMao.Remove(pl.Id);

		// O RAIO NA MAO MORRE COM QUEM SAIU. Sem isto o canal continuaria no dicionario cobrando Ki
		// de uma ficha que ninguem mais le, e -- pior -- `EnraizadoPorKi` responderia SIM pro id
		// reusado pelo proximo a entrar, que nasceria sem conseguir andar. Ver `FecharCanal`.
		SoltarDoRaio(pl.Id);
		EsquecerParalisia(pl.Id);
		EsquecerEsmagamento(pl.Id);   // o relogio do aviso, pelo mesmo motivo: id se reusa
		EsquecerVacuo(pl.Id);         // idem -- e sem isto o proximo a herdar o id nasceria "sufocando"

		// OS LOTES DE TECNICAS, POR UM NOME SO: cada um se inscreveu no boot (ver `_aoEsquecer`).
		// Antes eram cinco linhas a mao (G6, G7, G10, G11, G12) e CINCO lotes fora da cadeia -- o
		// proximo dono do id herdava o escudo, a carga da Final Explosion e ate as frases gravadas de
		// outra pessoa.
		EsquecerTecnicas(pl.Id);
		LimparProjeteisDeUmDono(pl.Id, pl.Zone.Hash);

		// ============================ RECONSTRUIDO (mais novo que a copia de 23:07) ============================
		// A VOZ nasceu depois do retrato que serviu de fonte pro resto deste metodo, entao esta linha
		// nao foi copiada: ela foi DEDUZIDA do proprio `EsquecerVoz` (`GameServer.Voz.cs:207`), cujo
		// cabecalho se descreve como *"Este corpo saiu: some com a torneira, a sequencia e o que sobrou
		// do cache dele"* -- "este corpo saiu" e este metodo -- e cujo comentario interno cita
		// nominalmente o **id REUSADO**, que e a mesma razao escrita nas seis linhas acima. Ela e a
		// unica chamadora possivel: nao ha outro ponto no servidor em que um id deixa de existir.
		// ====================================================================================================
		EsquecerVoz(pl.Id);

		_avisadosDeEspera.Remove(pl.Id);

		var w = Protocol.Begin(Protocol.S2C.PeerLeft);
		w.Put(pl.Id);
		foreach (ServerPlayer other in ZoneList(pl.Zone.Hash))
			other.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		GD.Print($"[server] {pl.Name} saiu");
	}

	/// <summary>
	/// QUEM PRECISA SABER QUE UM ID SAIU DO MUNDO. Cada lote de tecnicas guarda estado por id de
	/// jogador (recargas, cargas, ancoras, o escudo) fora do <see cref="ServerPlayer"/> -- ver o
	/// cabecalho de `GameServer.Tecnicas.cs` -- e id se reusa. O logout chama UM nome
	/// (<see cref="EsquecerTecnicas"/>); quem tem estado por id se inscreve no boot
	/// (<see cref="InscreverEsquecimentos"/>), e a bancada limpa os corpos dela pelo mesmo nome.
	///
	/// ============================ O BURACO QUE ISTO FECHA ============================
	/// A cadeia antiga era escrita a mao no `Drop`, um `EsquecerGx(pl.Id)` por lote -- e cinco lotes
	/// (a base, G1, G2, G3, G4) nunca entraram nela. Um id reusado herdava o `_escudoAtivo` do
	/// anterior: ao apertar Ki Shield, o corpo novo SUBTRAIA um bonus que nunca recebeu e ficava com
	/// a defesa abaixo da que nasceu. A lista e o que impede um sexto lote de ficar de fora: quem
	/// esquece de se inscrever esquece num lugar so, e a familia 7 da `--catalogoteste` prova o relog
	/// com o escudo de pe -- e prova que sabe ficar vermelha, esvaziando a lista.
	/// ==================================================================================
	/// </summary>
	private readonly List<Action<int>> _aoEsquecer = [];

	/// <summary>Este id saiu do mundo: todo estado por id das tecnicas some com ele.</summary>
	private void EsquecerTecnicas(int id)
	{
		foreach (Action<int> esquecer in _aoEsquecer) esquecer(id);
	}

	/// <summary>OS INSCRITOS, no boot: um por lote, cada um no arquivo do seu lote (`EsquecerGx`).</summary>
	private void InscreverEsquecimentos()
	{
		_aoEsquecer.Add(EsquecerBaseDasTecnicas);
		_aoEsquecer.Add(EsquecerG1);
		_aoEsquecer.Add(EsquecerG2);
		_aoEsquecer.Add(EsquecerG3);
		_aoEsquecer.Add(EsquecerG4);
		_aoEsquecer.Add(EsquecerG5);
		_aoEsquecer.Add(EsquecerG6);
		_aoEsquecer.Add(EsquecerG7);
		_aoEsquecer.Add(EsquecerG9);
		_aoEsquecer.Add(EsquecerProjeteis);
		_aoEsquecer.Add(EsquecerG10);
		_aoEsquecer.Add(EsquecerG11);
		_aoEsquecer.Add(EsquecerG12);
		_aoEsquecer.Add(EsquecerG13);
	}

	// ---------------------------------------------------------------------
	// tick: um snapshot por ZONA (nao por jogador) e o corte de interesse
	// ---------------------------------------------------------------------
	/// <summary>De quantos ticks em quantos o progresso vai pro disco (30 Hz x 3600 = 2 min).</summary>
	private const int TicksPorSave = 30 * 120;

	private void Tick()
	{
		// ONDE ESTAO OS CORPOS -- **a PRIMEIRA linha do tique, e a ordem e a regra**.
		//
		// A grade de colisao corpo-a-corpo e lida por TODO mundo que anda neste tique: o NPC do
		// `TickDosCorposSemDono`, o corpo arremessado do `TickDoEmpurrao` e (por consulta do cliente) o
		// jogador. Monta-la depois de qualquer um deles faria uns lerem o quadro de agora e outros o
		// quadro passado -- e o sintoma disso e o pior possivel numa colisao: "A parou em B, mas B
		// atravessou A", no mesmo instante. Ver `GameServer.Corpos.cs`.
		MontarAsGrades();

		// O combate anda no tick CHEIO (30 Hz): recarga de golpe e atordoamento sao contados
		// em fracao de segundo, e a ficha so roda a 5 Hz.
		TickCombate(Protocol.TickSeconds);

		// OS DEZ RELOGIOS DO CORPO -- forma, carga, voo, nado, Oozaru, Frost, furia e as tres
		// disciplinas divinas. Eram dez `foreach` soltos aqui; viraram um bloco com NOME em
		// `GameServer.RelogiosDoCorpo.cs`, onde tambem moram as razoes da ORDEM entre eles.
		//
		// A mudanca foi de endereco e nao de comportamento, e ela e o que torna esta parte do tique
		// MENSURAVEL: e daqui que sai a maior fatia do custo de um servidor com o mundo povoado e
		// ninguem online -- e ela e MUNDO, entao nao tem (e nao pode ter) porta de plateia.
		TickDosRelogiosDoCorpo(Protocol.TickSeconds);

		// O POVOAMENTO VEM ANTES DE OS CORPOS PENSAREM, e a ordem e a mesma razao do bloco acima: um
		// habitante que nasce neste tique ja e dirigido neste tique. Depois, ele passaria um quadro
		// como estatua -- invisivel, mas e o tipo de meio-estado de onde saem os bugs de "o NPC
		// deslizou no primeiro instante".
		//
		// O CUSTO E O DRENO, e ele e de 1 corpo por tique de proposito (ver `Povoamento.NascimentosPorTique`):
		// a manutencao decide de 5 em 5 min, este laco so paga a conta espalhada.
		TickDoPovoamento(Protocol.TickSeconds);

		// os corpos SEM DONO -- clones da mente, NPCs do povoamento e Oozarus fora de controle --
		// pensam e agem no tick cheio, pelas mesmas funcoes do jogador
		TickDosCorposSemDono(Protocol.TickSeconds);

		// A PORTA ABRE NO TICK CHEIO. Ela e uma parede que some, e uma parede que some com atraso
		// visivel e uma parede em que o jogador esbarra -- 5 Hz dariam ate 200 ms de porta fechada
		// depois de encostar nela.
		TickDasPortas();

		// AS PASSAGENS NO TIQUE CHEIO, junto das portas e pelo mesmo motivo: elas reagem a ENCOSTAR,
		// e uma reacao a 5 Hz deixaria o corpo atravessar a celula sem que ninguem percebesse.
		TickDasPassagens();

		// A QUEDA PELA NUVEM VEM LOGO DEPOIS DAS PASSAGENS, e a ordem importa: a volta do Ceu chega
		// no z6, que e todo nuvem. Vindo antes, a queda leria a posicao ANTIGA do corpo e o
		// despacharia do lugar errado; vindo depois, ela ve a chegada -- e a carencia compartilhada
		// (`_acabouDeAtravessar`) impede que os dois disparem no mesmo tique. Ver `GameServer.Nuvem.cs`.
		TickDasNuvens();

		// A LOTACAO DA SALA DO TEMPO, logo depois das passagens e nao por acaso: e a passagem
		// `fromhbtc` que tira gente de la, e quem acabou de sair tem que largar a vaga no MESMO
		// quadro -- senao a sala fica "cheia" com uma pessoa dentro por um tique inteiro, e um
		// terceiro seria recusado na porta sem motivo. Ver `GameServer.SalaDoTempo.cs`.
		TickDaSalaDoTempo();

		// A SESSAO CRONOMETRADA VEM LOGO DEPOIS DA LOTACAO, e a ordem e a mesma razao: quem acabou
		// de sair ja largou a vaga acima, entao este laco so olha quem esta MESMO dentro -- e a
		// comida, que e do lugar, some no mesmo quadro em que a sala esvazia. Ver
		// `GameServer.SalaSessao.cs`.
		TickDaSessaoDaSala(Protocol.TickSeconds);

		// O SELO -- os QUATRO relogios dele (fuga a 0,3 s, pote a 5 s, fita do Mafuba a 0,2 s,
		// portal da Dead Zone a 0,1 s), cada um com a cadencia que tem no DM. Anda no tique cheio
		// porque o mais rapido dos quatro e o portal, que arrasta corpo a 10 Hz -- num relogio de
		// 5 Hz ele puxaria de dois em dois tiles e o que se veria seria teleporte.
		// Ver `GameServer.Selo.cs`.
		TickDoSelo(Protocol.TickSeconds);

		// O ARREMESSO ANDA NO TICK CHEIO: o tique dele e 0,1 s e cada um vale dois tiles. A 5 Hz
		// o corpo daria saltos de quatro tiles, e o que se veria seria teleporte, nao voo.
		TickDoEmpurrao();

		// O AGARRAO LOGO DEPOIS DO ARREMESSO, e sao vizinhos por dois motivos concretos:
		//   * e no funil do arremesso que o agarrao DESEMBOCA (andar segurando alguem o joga), entao
		//     o corpo jogado neste quadro ja voa no proximo -- e nao um quadro depois;
		//   * o corpo que esta sendo CARREGADO tem a posicao escrita pelo servidor, exatamente como o
		//     que esta voando ou sendo levado por um feixe. Sao os tres unicos casos, e ficam juntos.
		// Ver `GameServer.Agarrao.cs`.
		TickDoAgarrao(Protocol.TickSeconds);

		// O ARRANQUE DA BANDEIRA NO TIQUE CHEIO, e aqui somos mais duros que o DM de proposito: la o
		// canal e conferido de segundo em segundo (`sleep(10)`), o que deixa uma janela pra dar um
		// passo e voltar dentro do mesmo segundo sem cancelar nada. Ver `TickDoArranque`.
		TickDoArranque(Protocol.TickSeconds);

		// OS ATAQUES DE KI. Dois tiques, e a ORDEM entre eles e a regra:
		//   * o CANAL primeiro, porque e ele quem PARE o raio (a carga fechando) e quem o mata por
		//     falta de Ki -- assim o tiro que nasce neste quadro ja anda neste quadro, em vez de
		//     ficar 33 ms parado na mao;
		//   * os PROJETEIS depois, e no tique cheio: um raio a 320 px/s anda 10 px por quadro a
		//     30 Hz, e a 5 Hz andaria 64 px de uma vez -- passaria por dentro de um corpo sem
		//     encostar nele, que e a pior falha possivel num sistema de acerto.
		TickDosCanaisDeKi(Protocol.TickSeconds);
		// O LOTE G12 ENTRE OS DOIS pelo mesmo motivo do canal: e ele quem PARE as esferas das rajadas e da
		// Death Ball, e a esfera que nasce neste quadro tem que andar neste quadro.
		TickG12(Protocol.TickSeconds);
		TickDosProjeteis(Protocol.TickSeconds);

		// A COLISAO DE KI, DEPOIS dos projeteis: e o tique deles que descobre o encontro (dois feixes
		// a um tile um do outro) e congela as duas cabecas. Rodando antes, uma disputa nascida neste
		// quadro so seria tiqueada no seguinte -- e as duas cabecas andariam mais um passo DEPOIS de
		// ja estarem em embate, atravessando uma a outra.
		TickDosEmbatesDeKi(Protocol.TickSeconds);

		// O PRAZO DO RAIO DE QUEM NAO TEM TECLADO (`BCL_NPC_BEAM_TIME`) -- ver o metodo: e ele que
		// faz o pulso unico do `Cerebro.Disparo` virar um canal com comeco e fim.
		TickDoPrazoDeRaioDaIa(Protocol.TickSeconds);

		// A LIMPEZA DO CACHE DE PAREDE DA VOZ. Ela se guarda sozinha (`< 64` sai na hora): varrer um
		// dicionario de tres entradas 30x por segundo seria pagar pela limpeza mais do que se limpa.
		TickDaVoz();

		// OS DOIS TIQUES DE BANCADA DA VOZ, guardados pela propria flag (`--vozdupla` / `--vozviva`).
		// Sem eles as duas bancadas sobem, conectam e NAO ANDAM NENHUMA FASE -- medem o nada em
		// silencio, que e o mesmo modo de falhar do `--esquivateste`.
		TickDaVozDupla();
		TickDaVozViva();

		// E O DA BANCADA DO RESCALDO (`--destrocosvivos`), guardado pela propria flag. Ele so vira
		// fase; o pavio do planeta desce pelo `TickDaDestruicao`, que e o de producao.
		TickDosDestrocosVivos();

		// OS MUNDOS QUE NASCERAM FORA DO TIQUE. Ver `GameServer.Procedural.cs`: gerar um planeta
		// custa de 27 ms (352 tiles) a ~220 ms (1000), e fazer isso aqui dentro parava o servidor
		// inteiro por causa de UM jogador encostando num planeta novo. Aqui so se colhe.
		TickDasGeracoes();

		TickDasArvores();     // as macas brotam de volta -- ver GameServer.Interacao.cs
		TickDaGravidade();    // a bateria das maquinas drena -- ver GameServer.Gravidade.cs

		// A CURA DAS MAQUINAS VARRE OS JOGADORES POR DENTRO, entao ela roda UMA vez por tique e nao
		// uma vez por jogador -- chamada de dentro do laco de combate, ela curaria todo mundo tantas
		// vezes quantos jogadores houvesse online, e o regenerador ficaria mais forte com a lotacao.
		TickDasMaquinasDeCura(Protocol.TickSeconds);

		// no espaco: troca de chunk e pouso por encostar. A copia que este laco precisa passou a ser
		// do tamanho de quem esta MESMO la em cima (quase sempre zero) e nao do servidor inteiro --
		// ver `_noEspaco` em `GameServer.RelogiosDoCorpo.cs`.
		TickDoEspacoDeTodos();

		// AS NAVES DEPOIS DO ESPACO, e a ordem tem razao: e o `TickDoEspaco` que POUSA o piloto num
		// planeta, e a nave copia a zona dele. Rodando antes, ela terminaria o tique descrita na
		// zona ANTERIOR -- um quadro em que o pod ainda esta em orbita e o dono ja esta no chao.
		// Ver `GameServer.Nave.cs`.
		TickDasNaves();

		// O TIQUE DAS SKILLS anda junto do da ficha, e nao por acaso: `NiveisDeSkill` foi
		// calibrado em 0,2 s por tique (5 Hz) e `TicksPorFicha` da exatamente isso a 30 Hz.
		// Cadencia errada aqui nao quebra nada -- so faz a skill subir mais rapido ou mais
		// devagar do que o original, calada.
		// A AUREOLA ENTRA AQUI, ao lado das feridas, e pelo mesmo argumento: e estado LENTO do corpo,
		// visto por toda a zona, detectado por DIFERENCA e nao por chamada -- ver `TickDasAureolas`.
		// O CADAVER ENTRA NESTE MESMO BLOCO DE 5 Hz, e pelo mesmo argumento das feridas e da aureola:
		// nada do que ele faz e evento de quadro. Ele desfaz o corpo destrocado (200 ms de atraso e
		// invisivel), aplica o teto da zona (uma contagem) e acende o "[E] corpo de Fulano" pra quem
		// chegou perto -- e chegar perto leva mais de 200 ms. Ver `GameServer.Cadaver.cs`.
		// E AS PECAS DE CORPO NO CHAO, ao lado do cadaver e pelo mesmo motivo: o prazo delas e de dez
		// minutos, e vencer 200 ms depois e invisivel. Ver `GameServer.Pecas.cs`.
		if (++_tickCount % TicksPorFicha == 0)
			{ TickFichas(); TickDosNiveis(); TickDasFeridas(); TickDasAureolas(); TickDosCadaveres(); TickDasPecas(); TickDoEfetorG11(); }
		// O EMBATE ANDA NO TIQUE CHEIO: os corpos se cruzam a cada 260 ms e as letras tem prazo de
		// 900 ms. A 5 Hz o prazo erraria por ate 200 ms, que num quick time event e a diferenca
		// entre acertar e nao.
		TickDosEmbates();

		// AS LETRAS DA DANCA DA FUSAO ANDAM AQUI, ao lado das do embate, e pela mesma razao escrita
		// duas linhas acima: o prazo de uma letra e de 900 ms e o piso de cadencia e de 300 ms. A 5 Hz
		// o adiantamento do acerto -- que e o "quanto mais rapido melhor" do dono -- deixaria de
		// existir. Ver `GameServer.Fusao.cs`.
		TickDasLetrasDaDanca();

		// O PUXAO DA POTARA ANDA AQUI PELO MOTIVO DO ARREMESSO, e nao pelo das letras: ele move CORPO,
		// a 1280 px/s cada um (os 32 px do `step_to` a cada `world.tick_lag` de um mundo a 40 fps -- ver
		// `Fusao.VelocidadeDoPuxao`). A 1 Hz cada passada andaria quarenta tiles de uma vez, e o que se
		// veria seria teleporte -- exatamente o que este bloco ja diz do arremesso e do selo.
		//
		// ANTES DA CENA, e a ordem importa: e este laco que decide que os dois ENCOSTARAM, e e o
		// encostar que faz a cinematica comecar (pedido do dono). Rodando depois, a cena que nascesse
		// neste tique so seria vista no proximo.
		TickDoPuxaoDeFusao();

		// A CINEMATICA DA FUSAO ANDA AQUI, ao lado das letras, e o argumento e o mesmo das duas linhas
		// acima levado ao extremo: o instante em que os dois viram um e um PONTO (o fim da animacao da
		// luz, `Cinematicas.SegundosDaLuzDaFusao` = 0,7 s de cena), e a 1 Hz ele erraria por ate um
		// segundo -- ou seja por MAIS que a cena inteira ate a virada. A fusao aconteceria antes ou
		// depois do clarao que existe pra anuncia-la, que e a queixa que o dono ja fez duas vezes sobre
		// efeito de fim caindo fora do fim. Custo fora de uma fusao: uma comparacao de inteiro (ver a
		// primeira linha de la).
		TickDaCenaDeFusao();

		// O CONVIVIO ENTRA NESTE MESMO BLOCO DE 1 Hz e cobra 3 segundos POR PESSOA la dentro (ver
		// `TickDoConvivio`): quem manda no passo e o prazo de cada jogador, e este laco so pergunta
		// se ja chegou a hora. Rodar mais rapido nao mudaria nada -- no relogio do DM a amizade
		// cresce 0,1 a cada 3 s.
		// O ROTEIRO DOS CHEFES ENTRA AQUI e nao no tique cheio: o gatilho dele e a fracao de vida do
		// membro mais ferido, que nao muda de forma interessante em 33 ms -- e avancar um degrau e um
		// EVENTO (cura, sprite novo, anuncio pra zona), nao algo pra conferir 30x por segundo.
		// O ESMAGAMENTO ENTRA NESTE BLOCO PORQUE AS CONSTANTES DELE SAO POR SEGUNDO: o dano
		// (`GRAVCRUSH_DMG_BASE`) e o dreno de folego do DM sao dose/segundo, e o proprio original
		// se protege com um throttle de 10 ticks (`gravcrush_dmg_next`) justamente porque o `Grav`
		// e chamado em taxas diferentes. Aqui a cadencia e do laco -- e no tique cheio o castigo
		// seria trinta vezes maior, que e a mesma armadilha do `TickDoEstomago`.
		// O VACUO ENTRA AO LADO DO ESMAGAMENTO E PELA MESMA RAZAO: a constante dele e um PRAZO de 20
		// segundos vindo de um contador de inteiros do DM (`spacetime`, `Stats.dm:120`), e um segundo
		// e a menor unidade que esse prazo distingue. Ele NAO entrou no `TickDoEspaco` (que roda a 30
		// Hz, ao lado do calor da estrela) de proposito: a estrela e um lugar por onde se ATRAVESSA e
		// 200 ms la tem que custar 200 ms de dano; ninguem "roça" o vacuo. Ver `GameServer.Vacuo.cs`.
		if (_tickCount % TicksPorSegundo == 0)
			{ TickDasTecnicas(); TickDasTecnicasG6(); TickDoEstudo(); TickDaGestacao(); TickDaLarva(); TickDoPalcoDoBio(); TickDoOlharDoBio(); TickDoFilmeDoBio(); TickDoNucleoInfinito(); TickDaPostura(); TickDosEstilos(); TickDosBuffs(); TickTecnicasG2(); TickDoCeu(); TickDoConvivio(); TickDoRoteiro(); TickDasSagas(); TickDoPalcoDaAgonia(1); TickDaDestruicao(1); TickDasInvasoes(); TickDaConquista(); TickDasEsferas(); TickDasSuperEsferas(); TickDosCargos(); TickDoEsmagamento(); TickDoVacuo(); TickDaFusao(); }
			if (_tickCount % TicksPorSegundo == 0) TickDosSentidos();   // OS SENTIDOS (a aba Sense/Scan) ANDAM A 1 Hz: a lista de quem eu sinto so muda quando alguem anda um tile, e so sai quando muda -- ver `GameServer.Sentidos.cs`

		// SALVAMENTO PERIODICO: sem isto, uma queda do servidor custa tudo desde o login.
		// Dois minutos e o maximo de treino que alguem pode perder.
		if (_tickCount % TicksPorSave == 0)
			foreach (ServerPlayer pl in _players.Values) Persistir(pl);

		long agora = NowMs();
		// O CARIMBO DO SNAPSHOT, lido UMA vez por tique e o mesmo pra todas as zonas: e o zero de
		// onde cada `EntityState.IdadeMs` conta pra tras. Vai nos 32 bits baixos; o cliente
		// desembrulha (`RelogioDoServidor`). E NAO E O `agora` de cima: aquele e relogio de parede,
		// e relogio de parede salta. E a hora NOMINAL deste tique (`_tickCount * TickMs`), porque e
		// nela que todo corpo movido pelo servidor foi integrado -- ver `RelogioDeQuadrosMs`.
		_relogioDoSnapshot = (long)Math.Round(_tickCount * Protocol.TickMs);
		uint carimbo = unchecked((uint)_relogioDoSnapshot);
		foreach (List<ServerPlayer> zona in _zones.Values)
		{
			if (zona.Count == 0) continue;

			// O ESPACO E UMA ZONA SO, e por isso o corte la nao pode ser a zona: todo mundo
			// esta nela. Quem decide o trafego e a CHUNK -- e como o recorte muda de jogador
			// pra jogador, o buffer nao da pra compartilhar e cada um recebe o seu.
			if (Espaco.EhEspaco(zona[0].Zone)) { SnapshotDoEspaco(zona, agora, carimbo); continue; }

			var w = Protocol.Begin(Protocol.S2C.Snapshot);
			w.Put(carimbo);
			w.Put((ushort)zona.Count);
			foreach (ServerPlayer pl in zona) EstadoDe(pl, agora).Write(w);

			// O SEGUNDO BLOCO: os ataques de ki no ar. Vem no MESMO buffer da zona porque e o mesmo
			// tipo de dado (posicao autoritativa, 30 Hz, sequenced) e porque a zona sem tiro nenhum
			// paga dois bytes por pacote -- um opcode proprio pagaria cabecalho e um envio a mais.
			EscreverProjeteis(w, zona[0].Zone.Hash);

			// mesmo buffer pra todos daquela zona: quem esta noutro planeta nao recebe nada
			foreach (ServerPlayer pl in zona)
				pl.Peer?.Send(w, Protocol.ChannelState, DeliveryMethod.Sequenced);
		}

		// ============================ O SNAPSHOT SAI AGORA, NAO NO PROXIMO DESPERTAR ============================
		// `Send` so enfileira: o pacote vai pro fio quando a thread de logica do LiteNetLib acorda
		// (`UpdateTime`). Sem esta linha o snapshot de todo mundo esperava ate 15 ms numa fila -- e
		// como 15 nao divide 33,3, a espera era DIFERENTE a cada tique (0, 12, 8, 3...). O carimbo
		// ja tira esse jitter do DESENHO (a hora viaja no pacote); esta linha tira da LATENCIA.
		// ====================================================================================================
		_net.TriggerUpdate();

		// ============================ A ULTIMA LINHA DO QUADRO -- E ELA E MENSURAVEL DE PROPOSITO ============================
		// **UM INCREMENTO, E ELE E A UNICA FORMA DE UMA BANCADA AFIRMAR "O TIQUE CHEGOU AO FIM".**
		//
		// Nao ha nada aqui embaixo pra observar: o ultimo passo do tique e o snapshot por zona, que
		// SO manda bytes (`EstadoDe` monta uma struct e o `Send` sai pelo `Peer`) -- num servidor sem
		// plateia ele nao deixa marca nenhuma no mundo. Ou seja, um quadro que morre no meio e um
		// quadro inteiro terminavam **iguais** pra quem olha de fora.
		//
		// E era exatamente esse o buraco: `TickCombate` e a PRIMEIRA chamada deste metodo, e enquanto
		// o cadaver do jogador nascia dentro do laco dele (`InvalidOperationException: Collection was
		// modified`, `GameServer.Combat.cs:1203`), os ~60 subsistemas daqui de cima **nao rodavam** a
		// cada morte -- e nenhuma das bancadas conseguia dizer isso em voz alta. Elas mediam pedaco:
		// chamavam `TickDoAgarrao`, `TickDosCadaveres`, `TickDosRelogiosDoCorpo` na mao, um por vez,
		// e todas ficavam verdes com o servidor caindo inteiro em jogo.
		//
		// **NAO E UM CONTADOR DE TEMPO E NAO SUBSTITUI O `_tickCount`**: aquele e incrementado NO MEIO
		// (ele decide as cadencias de 5 Hz e 1 Hz), entao ele nao sabe distinguir "morreu depois do
		// combate" de "chegou ao fim". Este so e escrito depois da ultima linha util, e por isso
		// "subiu 1 neste quadro" quer dizer o percurso inteiro.
		//
		// Custo: um `long++` a 30 Hz. Ver a bancada `--tiquedamorte`, que e quem o le.
		// ==============================================================================================================
		_quadrosInteiros++;
	}

	/// <summary>
	/// QUANTOS QUADROS CHEGARAM AO FIM desde que o servidor subiu -- escrito na ULTIMA linha do
	/// <see cref="Tick"/> e em nenhum outro lugar. Ver o bloco que o escreve pro argumento inteiro.
	///
	/// Um quadro que estoura no meio (foi o que toda morte de jogador fazia) nao incrementa isto,
	/// enquanto o `_tickCount` -- que e mexido no meio do tique -- fica igual nos dois casos.
	/// </summary>
	private long _quadrosInteiros;

	/// <summary>
	/// Recalcula a ficha de cada jogador e manda de volta o que MUDOU. O pacote so sai quando
	/// algum numero mexeu -- num servidor parado isso e zero trafego.
	/// </summary>
	private void TickFichas()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			Treinar(pl);

			// A RAIVA ANDA AQUI, e este e o tique CERTO e nao um qualquer: `TicksPorFicha` da 5 Hz,
			// que e exatamente o `sleep(sleep_tiem)` com `sleep_tiem = 2` do `mob/proc/Stats()`
			// (`Stats.dm:125`), o laco onde o decaimento de raiva do DM mora (`Stats.dm:438-443`).
			// E o mesmo tique que roda `Statify` (quem escreve o `MaxAnger`) e `PowerLevel` (quem le
			// o `angerBuff`) -- os tres no lugar em que o original os poe.
			//
			// ANTES do `Ficha.Tick` de proposito: o `ClampAnger` mora DENTRO dele, entre o `Statify`
			// e o `PowerLevel` (`Fighter.cs:392-397`). Projetar depois deixaria o poder deste tique
			// ser calculado com a raiva do anterior, e o corte pelo `MaxAnger` valeria um tique
			// atrasado. Ver `ProjetarRaiva` (`GameServer.Formas.cs`).
			ProjetarRaiva(pl);

			pl.Ficha.Tick(agoraMs: NowMs());

			// A VELOCIDADE PASSOU A TER DOIS DONOS (o corpo e a nave) e por isso virou UM metodo --
			// ver `RecalcularVelocidade` em `GameServer.Nave.cs`. Ele continua sendo o unico ponto
			// que escreve `SpeedStat` por tique.
			RecalcularVelocidade(pl);

			// ============================ O TOPO E DE QUEM JOGA -- a mesma regra do `MediaDoServidor` ============================
			// `TopBP` e "o maior BP **BASE** do servidor", ele so SOBE e ele entra na escala de ganho de
			// todo mundo e no teto do potencial escondido. Este laco varre o `_players` inteiro, e o
			// `_players` tem corpo sem dono desde o primeiro NPC -- entao um chefe de saga de 1e12 fixava
			// o topo do servidor pra sempre, e o `MediaDoServidor` ja recusa esse mesmo emprestimo com o
			// argumento inteiro: *"incluir NPC na media daria realimentacao"*.
			//
			// E O REFLEXO DA MENTE E O CASO QUE OBRIGA A ESCREVER ISTO: a ficha dele e ancorada no
			// **expresso** do dono (ver `EspelharODono`), entao o BP BASE dele nao e um BP base de
			// ninguem -- quem entrasse na mente em SSJ3 empurraria o topo do servidor pra 20x o proprio
			// poder, de graca, e o numero nunca mais desceria.
			// ==============================================================================================================
			if (EhJogador(pl)) GainKnobs.TopBP = Math.Max(GainKnobs.TopBP, pl.Ficha.BP);

			// CONTRA O QUE FOI ENVIADO, e nao contra o valor de tres linhas atras -- ver os campos
			// `Env*` no ServerPlayer. Comparar dentro da propria chamada perde tudo que mudou entre
			// duas chamadas, que e justamente onde a carga de Ki e a regeneracao vivem.
			// O `Estado` TAMBEM. Ele carrega KO, morto, guarda, LETAL e rabo -- e nenhum deles
			// mexe nos numeros acima. O dono viu o sintoma: apertou a tecla de golpe letal, o HUD
			// (que le o proprio toggle do cliente) mudou, e a aba Stats (que le a FICHA) continuou
			// dizendo "nao-letal" -- porque a ficha nunca era reenviada.
			//
			// Mesma familia do bug da barra de Ki: a lista de campos comparados era menor que a
			// lista de campos ENVIADOS. Todo campo que vai no pacote precisa estar aqui, senao ele
			// so chega de carona quando outro muda.
			SheetState ficha = pl.Sheet();
			if (pl.EnvBP == pl.Ficha.expressedBP && pl.EnvKi == pl.Ficha.Ki
				&& pl.EnvHp == pl.Ficha.HP && pl.EnvAct == pl.Ficha.Eactspeed
				&& pl.EnvVigor == pl.Ficha.stamina && pl.EnvNutricao == pl.Ficha.CurrentNutrition
				&& pl.EnvBpBase == pl.Ficha.BP
				&& pl.EnvTetoKi == Jandirus.Core.Combat.CargaDeKi.TetoEmRazao(pl.Ficha)
				&& pl.EnvMaxKi == ficha.MaxKi && pl.EnvSpeed == ficha.SpeedStat && pl.EnvMembros == ficha.MembrosRuins
				&& pl.EnvEstado == ficha.Estado && pl.EnvEstado2 == ficha.Estado2) continue;

			MandarFicha(pl);
		}
	}

	/// <summary>
	/// MANDA A FICHA e sincroniza o cache anti-repeticao. Uma casa so.
	///
	/// O `TickFichas` roda a 5 Hz, e isso basta pra vida e Ki -- mas NAO pra estado que o cliente
	/// usa pra decidir como se mover. O arremesso comeca e acaba no tique CHEIO (30 Hz), entao
	/// esperar o proximo tique de ficha deixava o corpo ate 200 ms sem saber que estava voando: em
	/// pe e teleportando. Quem precisa avisar na hora chama isto direto (ver GameServer.Empurrao).
	///
	/// O CACHE E ATUALIZADO AQUI, e nao no chamador: sem isso o `TickFichas` seguinte veria "mudou"
	/// e reenviaria a mesma ficha de graca, no canal confiavel.
	/// </summary>
	private void MandarFicha(ServerPlayer pl)
	{
		if (pl.Peer is not { } peer) return;

		pl.EnvBP = pl.Ficha.expressedBP;
		pl.EnvKi = pl.Ficha.Ki;
		pl.EnvHp = pl.Ficha.HP;
		pl.EnvAct = pl.Ficha.Eactspeed;
		pl.EnvVigor = pl.Ficha.stamina;
		pl.EnvNutricao = pl.Ficha.CurrentNutrition;
		pl.EnvBpBase = pl.Ficha.BP;
		pl.EnvTetoKi = Jandirus.Core.Combat.CargaDeKi.TetoEmRazao(pl.Ficha);
		SheetState enviada = pl.Sheet();
		pl.EnvEstado = enviada.Estado;
		pl.EnvEstado2 = enviada.Estado2;
		pl.EnvMaxKi = enviada.MaxKi;
		pl.EnvSpeed = enviada.SpeedStat;
		pl.EnvMembros = enviada.MembrosRuins;

		var w = Protocol.Begin(Protocol.S2C.Stats);
		FichaVisivel(pl).Write(w);
		peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O ganho de BP da vez. Roda no SERVIDOR e so aqui -- o cliente declara o que esta
	/// fazendo, mas quem soma poder e este metodo. A gravidade entra sempre: ela nao treina
	/// sozinha, ela MULTIPLICA quem ja esta treinando.
	/// </summary>
	private void Treinar(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;

		// ============================ OS MARCOS DE ASCENSAO ============================
		// `Stats.dm:257` chama `Auto_Gain()` do laco `Stats()` -- FORA do galho de treino/meditacao,
		// todo tique -- e o `Auto_Gain()` abre chamando `bp_milestone_check_ascension()`
		// (`ascensioncontrols.dm:83`). Entao a posicao fiel e esta: o topo do `Treinar`, que e o
		// `Stats()` deste port (mesma cadencia de 5 Hz, mesmo laco por jogador), ANTES do desvio que
		// separa quem treina de quem medita de quem esta parado.
		//
		// Nao foi pro `Ficha.Tick` porque la a pergunta seria feita sem o `ServerPlayer`, e quem sobe
		// de patamar precisa ser AVISADO -- o DM manda `to_chat` de dentro do `bp_milestone_reach`
		// (`LinearGain.dm:44`).
		//
		// ============ ISTO ESTA DORMENTE HOJE, E DE PROPOSITO -- LEIA ANTES DE APAGAR ============
		// `BPBoost` nasce 1 (`Fighter.cs:368`) e NENHUM caminho de producao escreve nele: a Ascensao
		// (`Auto_Gain`, `NPCAscension`) nao foi portada -- `FormasDeFrost.cs:305` e `MoldeDeNpc.cs:291`
		// ja registram isso por escrito. Com `BPBoost` cravado em 1 os tres `if` sao falsos e isto
		// devolve 0 sempre.
		//
		// A chamada fica AQUI mesmo assim porque o defeito recorrente deste projeto e o inverso --
		// regra escrita e nunca aplicada --, e porque no dia em que a Ascensao entrar ela vai entrar
		// escrevendo `BPBoost`, nao procurando quem chama o marco. A bancada `--ligadosteste` levanta
		// o `BPBoost` na mao e exige o marco: se esta linha sumir, ela reprova.
		// =========================================================================================
		if (f.CheckAscensionMilestone() is var marco && marco > 0)
			Avisar(pl, $"MARCO DE PODER! seu corpo rompeu um novo patamar: todo ganho agora e x{marco:0.##}.");

		// ============================ O SUPER NAMEKUSEIJIN DESPERTA AQUI (regra N5 do dono) ============================
		// Ao lado do marco, e pelo MESMO argumento escrito no bloco acima: e o ponto do laco por jogador
		// que roda todo tique, fora do galho de treino/meditacao, e quem cruza um patamar precisa ser
		// AVISADO na hora. Um Namekuseijin que so descobrisse a forma na proxima vez que apertasse alguma
		// coisa nao teria "ganhado a transformacao ao chegar no requisito" -- teria ganhado ao clicar.
		//
		// Ver `GameServer.ConferirODespertarDoSuperNamekuseijin`: ele sai nas tres primeiras linhas pra
		// quem nao e Namekuseijin, entao o custo pro resto do servidor e uma comparacao de string.
		// =========================================================================================================
		ConferirODespertarDoSuperNamekuseijin(pl);

		// O SACO DE PANCADA DOBRA O TREINO, e o bonus vem da PRESENCA e nao de um estado guardado:
		// se ha um saco aparafusado por perto, treinar rende mais. Um campo "estou no saco" ficaria
		// preso ligado quando o jogador andasse pra longe. Ver `BonusDeTreinoPerto`.
		double aparelho = f.train ? BonusDeTreinoPerto(pl) : 1;

		if (f.train) f.TrainGain(_rng, 6.0 / (1 + Math.Log(2)) * aparelho);
		else if (f.med) f.MedGain(_rng);
		else { f.BufferTick(); return; }   // parado: so acumula pro proximo treino

		f.GravGain();
	}

	/// <summary>Move um jogador de zona e avisa as duas pontas (usado pela troca de planeta).</summary>
	public void MoveToZone(int playerId, ZoneKey destino, Vec2 spawn)
	{
		if (!_players.TryGetValue(playerId, out ServerPlayer? pl)) return;

		ulong tEntrou = Time.GetTicksUsec();
		ZoneKey saiuDe = pl.Zone;

		// TROCAR DE PLANETA SOLTA O RAIO, e o tiro que ja saiu fica pra tras. Um beam cuja cauda
		// segue o dono atravessaria zonas -- a cauda apontaria pra uma posicao de outro mundo e o
		// rastro viraria uma linha de mil tiles. E ninguem "leva" um raio na mao pelo espaco.
		// E A DISPUTA ACABA JUNTO: um encontro de feixes com um dos dois noutro planeta nao existe.
		SoltarDoEmbateDeKi(pl.Id);
		SoltarDoRaio(pl.Id);
		LimparProjeteisDeUmDono(pl.Id, saiuDe.Hash);

		// O PLANETA DE DESTINO ESFRIOU ENQUANTO NINGUEM ESTEVE LA? A outra porta de entrada no mundo
		// (ver `GameServer.Embaralho.cs`), e ela vem ANTES do `ZoneChanged` e do primeiro snapshot da
		// zona nova pelo mesmo motivo do login: quem chega ve o resultado, nunca o pulo.
		//
		// SAIR NAO PRECISA DE LINHA NENHUMA AQUI: a marca de "esvaziou" e derivada do `_zonasComGente`
		// uma vez por tique, e por isso ela vale igual pra nave, logout, transe, morte e o que vier.
		EmbaralharSeEsfriou(destino);

		ZoneList(pl.Zone.Hash).Remove(pl);
		pl.Zone = destino;
		pl.Pos = spawn;
		ZoneList(destino.Hash).Add(pl);

		// ============================ QUEM FICA PRECISA SABER QUE VOCE FOI EMBORA ============================
		// O snapshot so descreve quem ESTA na zona -- ele nao diz "fulano saiu". Quem some da lista
		// simplesmente para de aparecer nos pacotes, e o cliente nao tem como distinguir isso de um
		// pacote perdido: ele mantem o boneco na tela, parado, pra sempre.
		//
		// Era o corpo fantasma que o dono viu: viajar pra outro planeta deixava uma copia sua de pe
		// no anterior, na ultima posicao conhecida. O caminho de DESCONECTAR ja mandava este pacote
		// desde sempre; mudar de zona e a mesma coisa pra quem ficou -- a pessoa sumiu de vista.
		// ======================================================================================================
		if (saiuDe.Hash != destino.Hash)
		{
			var fui = Protocol.Begin(Protocol.S2C.PeerLeft);
			fui.Put(pl.Id);
			foreach (ServerPlayer o in ZoneList(saiuDe.Hash))
				o.Peer?.Send(fui, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}

		// TROCAR DE ZONA E O MAIOR TELEPORTE DE TODOS -- do outro lado do mapa, ou de outro
		// planeta. Os pacotes que o cliente tinha em voo falam da zona ANTIGA, e sem este carimbo
		// o servidor tentaria "corrigir" a posicao nova em direcao a uma coordenada de outro
		// mundo. O orcamento tambem zera: credito acumulado andando la nao vale aqui.
		pl.SeqDoTeleporte = pl.SeqInput;
		pl.OrcamentoPx = 0;

		// O VOO NAO ATRAVESSA A PORTA. A altura e do lugar de onde se saiu: chegar num mapa novo
		// pairando a 20 tiles deixaria o corpo com a colisao desligada num terreno que ele nunca
		// viu -- e, no espaco, "altitude" nem quer dizer nada. Quem viaja chega no chao.
		pl.Voando = false;
		pl.Altitude = 0f;
		pl.QuerSubir = pl.QuerDescer = false;

		var w = Protocol.Begin(Protocol.S2C.ZoneChanged);
		w.PutZone(destino);
		w.PutVec(spawn);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		// AS CONSTRUCOES SAO POR ZONA: sem reenviar, quem muda de planeta continua vendo as
		// construcoes do planeta anterior desenhadas no chao do novo.
		MandarObras(destino);
		// AS ESFERAS SAO POR ZONA IGUAL. Sem esta linha, quem sai de Namek com o Porunga na tela
		// continuaria vendo as sete desenhadas no chao da Terra -- e o inverso: quem chega em Namek
		// nao veria nenhuma ate a proxima vez que alguem mexesse numa delas.
		MandarEsferas(destino);
		// ...e as PORTAS pelo mesmo motivo: quem chega tem que ver as que estao abertas agora, e o
		// `.col` do cliente tem que casar com o do servidor (ver MandarPortas).
		MandarPortas(pl);
		MandarCenario(pl);
		// ...e as PECAS DE CORPO no chao da zona nova, pelo mesmo motivo do cenario: sem esta linha,
		// quem volta do Outro Mundo pra onde perdeu o braco nao ve o braco. Ver `GameServer.Pecas.cs`.
		MandarPecas(pl);

		// A APARENCIA E AS FERIDAS TAMBEM SAO POR ZONA -- e isto faltava.
		//
		// `TrocarAparencias` so rodava no LOGIN. Quem viajava pra Namek entrava numa zona onde
		// ninguem jamais recebeu o `PeerLook` dele: os que ja estavam la o viam com a aparencia
		// padrao, e ele via os deles pelo mesmo motivo invertido. E a mesma familia de defeito das
		// construcoes e das portas -- o pacote existe, sai uma vez, e quem nao estava presente
		// naquele instante nunca soube. Mudar de planeta e exatamente "nao estar presente".
		TrocarAparencias(pl);
		TrocarFeridas(pl);

		pl.Estudando = false;   // ninguem estuda de outro planeta
		AplicarGravidade(pl);   // o chao mudou: o peso dele tambem

		// ============================ QUEM HOSPEDA PAGA AS DUAS CONTAS ============================
		// Servidor e cliente sao o MESMO processo quando se joga com `--host`, e no mesmo thread: um
		// segundo gasto aqui e um segundo com a janela congelada, indistinguivel de travamento do
		// cliente. Como o dono joga hospedando, a metade do servidor precisava de marco proprio --
		// sem ele, "o jogo trava ao trocar de mapa" nao tem como apontar de que lado esta.
		// ==========================================================================================
		double msServidor = (Time.GetTicksUsec() - tEntrou) / 1000.0;
		if (msServidor > 5) GD.Print($"[perf] servidor: trocar pra {destino.Name} levou {msServidor:0.0} ms");

		// O CEU MUDOU TAMBEM, e ele nao e o mesmo ceu: cada planeta corre o proprio dia e a
		// propria lua (ver GameServer.Ceu.cs). Zerar a memoria da lua e o que faz a cheia de
		// Vegeta ser anunciada pra quem acabou de chegar da Terra em plena madrugada -- sem isto
		// ele so seria avisado na PROXIMA vez que ela nascesse, oito noites depois.
		pl.LuaVista = 0;
		pl.LuaEstavaNoCeu = false;
		MandarCeu(pl);

		// O CLIMA FORCADO E DA ZONA, entao mudar de zona e trocar de ceu: quem sai de um temporal
		// forcado por um SSJ3 tem que deixar o temporal pra tras, e quem chega num tem que ve-lo.
		MandarClima(pl);
	}

	/// <summary>
	/// O ESTADO DE UM CORPO NO FIO -- fonte UNICA.
	///
	/// Existia em DUAS copias (a zona normal e o espaco), e a do espaco ficou pra tras: nasceu sem
	/// `Oculto` e depois sem `Deitado`/direcao do corpo. No espaco, entao, ninguem via o outro cair
	/// pro lado certo nem via quem estava sendo arremessado deitar -- o mesmo "nao sincroniza", vivo
	/// numa zona so.
	///
	/// Duplicar isto e uma armadilha armada: o proximo campo novo tambem so ia entrar num dos dois.
	/// Com uma fabrica, campo novo entra AQUI e chega nas duas.
	/// </summary>
	/// <summary>
	/// A hora nominal do snapshot que esta saindo -- o zero de onde a idade de cada corpo conta pra
	/// tras. Escrito uma vez por tique, antes das zonas (ver o laco do snapshot).
	/// </summary>
	private long _relogioDoSnapshot;

	/// <summary>
	/// O RELOGIO DE QUADRO DO SERVIDOR, em ms: os tiques ja dados mais o resto do acumulador. E a soma
	/// dos `delta` do motor desde que o servidor subiu, e e nele que TUDO o que o servidor move e
	/// integrado (o `TickDoEmpurrao`, a IA, o voo andam `TickSeconds` por tique). Carimbar com o QPC
	/// casaria posicoes integradas em tempo nominal com horas reais aos trancos do quadro -- ver "os
	/// relogios do fio sao relogios de quadro" em `Protocol`.
	///
	/// LIDO NO `Input`, que roda no `PollEvents` do comeco do quadro: todo input do mesmo quadro
	/// recebe a mesma hora, e isso e o certo -- quem carrega a hora fina e o `tempoMs` do cliente;
	/// esta so ANCORA o deslocamento (o minimo em janela, ver `DeslocamentoDeRelogio`).
	/// </summary>
	private long RelogioDeQuadrosMs() => (long)Math.Round(_tickCount * Protocol.TickMs + _accumulator * 1000.0);

	/// <summary>`--espeedteste`: o stat BASE de velocidade com que todo mundo entra. Zero = producao. Ver o parse.</summary>
	private float _speedStatDeTeste;

	private EntityState EstadoDe(ServerPlayer pl, long agora)
	{
		// O CANAL E PERGUNTADO UMA VEZ SO, e as duas leituras abaixo (a pose e o byte do canal) saem
		// da MESMA resposta. Perguntar duas vezes daria a chance -- pequena, e por isso pior -- de o
		// dicionario mudar entre as duas e sair um pacote dizendo "pose de canal, sem byte de canal",
		// que e um pacote que o leitor nao sabe ler (ele so busca o byte quando ve a pose).
		(bool canal, bool atirando, int cargaDoCanal) = CanalDeKiDe(pl.Id);

		var e = new EntityState
		{
			Id = pl.Id,
			Pos = pl.Pos,
			// HA QUANTO TEMPO `Pos` VALE. Corpo que o SERVIDOR escreveu por ultimo vale AGORA (zero):
			// e o dono dele. Corpo que o CLIENTE afirmou vale desde a hora do input, traduzida -- e
			// se essa hora ficar pra tras (o cliente parou de mandar), satura em 255 e o
			// `RemotePlayer` le a saturacao. Ver `EntityState.IdadeMs`.
			IdadeMs = pl.PosVemDoCliente
				? (sbyte)Math.Clamp(_relogioDoSnapshot - pl.PosMs, sbyte.MinValue, EntityState.IdadeSaturada)
				: (sbyte)0,
			// DE PE, e pra onde olha; DEITADO, e pra onde a cabeca aponta. Mesmo campo.
			Facing = (byte)(pl.Deitado ? pl.DirecaoDeitado : pl.Facing),
			Moving = pl.Moving,
			Pose = pl.Pose(agora, canal),
			// A VIDA NAO VAI MAIS NO SNAPSHOT, e a linha nao voltou junto com o resto do arquivo: o dono
			// mandou tirar o HP alheio do jogo, e o campo foi DELETADO do `EntityState` junto com a
			// barrinha por cima da cabeca. Ver `GameServer.Sigilo.cs` -- a vida que ainda sai no fio e a
			// SUA, pelo `MandarFicha`, e ela nao passa por aqui.
			Rabo = pl.TemRaboAgora(),
			Oculto = EstaOculto(pl.Id),
			Deitado = pl.Deitado,
			// CORRER E DADO, NAO DEDUCAO. O cliente media a velocidade entre snapshots e comparava com
			// a velocidade BASE do jogo -- entao quem tinha velocidade alta deixava rastro de corrida
			// ANDANDO. Ver `EntityState.Correndo`.
			Correndo = pl.Correndo && pl.Moving,
			Carregando = pl.AuraDaCarga,   // o VISUAL, nao o estado -- ver GameServer.Carga.cs
			Sobrecarregado = pl.AuraDeCarga,
			// O BIT E "TEM ALTURA PRA CONTAR", e nao "esta com o voo ligado": quem perdeu o voo no ar
			// ainda esta la em cima caindo, e desligar o bit no instante do nocaute faria o corpo
			// aparecer no chao pra todo mundo enquanto o servidor ainda o traz descendo.
			Voando = pl.Altitude > 0f,
			Altitude = pl.Altitude,
			// QUEM DIRIGE ESTE CORPO. Vai no snapshot porque e o corpo LOCAL que precisa saber: sem
			// isto ele continua escolhendo a propria animacao pelo teclado, que durante a possessao nao
			// da passo nenhum -- posicao andando e animacao parada e o "sai deslizando". Ver
			// `EntityState.SemRedeas`.
			SemRedeas = SemAsRedeas(pl),
			// A NAVE EM CIMA DELE. Um bit, e ele descreve um objeto inteiro: a nave pilotada sai da
			// lista de construcoes da zona e passa a ser desenhada pelo corpo que a carrega -- que e
			// literalmente o que o `verb/Use` do DM faz (`PlanetTech.dm:140-144`). Ver
			// `EntityState.Pilotando`.
			Pilotando = EstaPilotando(pl.Id),
			// ...E QUAL DELAS. Um segundo bit porque os dois sprites nao sao parecidos -- ver
			// `EntityState.NaveGrande`.
			NaveGrande = PilotaNaveGrande(pl.Id),
			// O QUE ESTE CORPO ESTA FAZENDO -- e o que faz quem esta batendo (ou guardando, ou
			// carregando...) parar tambem quem VOA contra ele. O calculo e o MESMO `OcupacaoDe` que
			// alimenta a grade de colisao do servidor; o cliente le esta resposta em vez de refazer a
			// lista do outro lado. Ver `EntityState.Ocupacao` e `Core/World/Ocupacao.cs`.
			//
			// O `canal` VAI JUNTO em vez de ser perguntado de novo, pelo mesmo motivo da pose logo
			// acima: duas leituras do `_canais` no mesmo pacote podem discordar entre si.
			Ocupacao = OcupacaoDe(pl, agora, canal),
		};

		// ============================ O BYTE DO CANAL SO E PREENCHIDO SE HOUVER CANAL ============================
		// Ele nem viaja fora daqui (o `Write` so o poe no fio com a pose `Canalizando`), entao escrever
		// nele sem canal seria escrever num campo que ninguem le. Fica dentro do `if` pra que o campo e
		// a condicao que o publica digam a MESMA coisa, num lugar so.
		//
		// A CARGA VEM RESOLVIDA DO CANAL, e nao e calculada aqui: ela e funcao pura da identidade do
		// corpo (`rand(1,9)` no `finalize_Race` do DM, `race.dm:60-61`) e o sorteio ALOCA. Este metodo
		// roda por corpo por tique -- ver `CanalDeKi.Carga`, que explica por que a conta fica no
		// `Canalizar`.
		// =====================================================================================================
		if (canal)
		{
			e.CanalAtirando = atirando;
			e.CargaDoCanal = cargaDoCanal;
		}

		return e;
	}

	/// <summary>
	/// A lista de corpos de uma zona.
	///
	/// ============================ ELE PARECE LEITURA, MAS **ESCREVE** ============================
	/// Zona que nunca foi vista NASCE aqui: a linha `_zones[hash] = l` e uma INSERCAO DE CHAVE NOVA,
	/// e insercao e a unica operacao que invalida um `foreach` de `Dictionary` em andamento (o
	/// `Remove`, desde o .NET Core 3.0, nao invalida). Ou seja: chamar `ZoneList(...)` de dentro de um
	/// `foreach (... in _zones.Values)` -- e o maior deles e o do SNAPSHOT, no `Tick()` -- derruba o
	/// tique inteiro com `InvalidOperationException: Collection was modified`, exatamente como o
	/// cadaver derrubava o laco do `TickCombate` (ver o comentario grande la).
	///
	/// Hoje nenhum dos dois lacos de `_zones.Values` alcanca esta funcao, e por isso nao ha copia
	/// defensiva paga por tique. Quem escrever o proximo: **consultar a lista de OUTRA zona de dentro
	/// de um deles precisa da chave ja existente, ou da chamada fora do laco.**
	/// ========================================================================================
	/// </summary>
	private List<ServerPlayer> ZoneList(ulong hash)
	{
		if (!_zones.TryGetValue(hash, out List<ServerPlayer>? l))
		{
			l = [];
			_zones[hash] = l;
		}
		return l;
	}

	private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	/// <summary>
	/// O NOME QUE O MUNDO LE: o da fusao enquanto ela dura, o do personagem no resto do tempo.
	/// Ver <see cref="ServerPlayer.NomeDeFusao"/> pra saber por que os dois nao sao o mesmo campo.
	/// </summary>
	private static string NomeVisivel(ServerPlayer p) =>
		p.NomeDeFusao.Length > 0 ? p.NomeDeFusao : p.Disfarce?.Nome ?? p.Name;

	/// <summary>
	/// A APARENCIA QUE O MUNDO VE: a da fusao enquanto ela dura, a do personagem no resto do tempo.
	/// Gemeo do <see cref="NomeVisivel"/> logo acima, e ver <see cref="ServerPlayer.LookDeFusao"/> pros
	/// DOIS motivos de os campos serem separados (o disco e o saneamento).
	///
	/// **UM FUNIL SO, e nao um `??` em cada chamador**: quem monta pacote de aparencia pergunta a este
	/// metodo, e no dia em que houver um terceiro caminho ele herda a regra de graca. O `JoinAccepted`
	/// e a UNICA excecao de propósito e ela esta comentada la -- ninguem entra no mundo ja fundido.
	/// </summary>
	private static Jandirus.Core.Appearance.Appearance VisualVisivel(ServerPlayer p) =>
		p.LookDeFusao ?? p.Disfarce?.Visual ?? p.Visual;
}
