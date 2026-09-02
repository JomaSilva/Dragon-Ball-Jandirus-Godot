using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// O ATUADOR: onde o <see cref="Comando"/> da IA vira o MESMO gesto que o pacote do jogador.
///
/// ============================ A REGRA DESTE ARQUIVO INTEIRO, E ELA E CURTA ============================
/// **O atuador nunca confere pre-condicao. Ele chama e deixa o jogo recusar.**
///
/// Nao ha um `if (PodeVoar(npc))` antes do <see cref="AlternarVoo"/>, nem um `if (temKi)` antes do
/// <see cref="Carregar"/>, nem um `if (Proxima() != null)` antes do <see cref="Transformar"/>.
/// Cada uma dessas funcoes JA sabe recusar -- ela recusa o jogador -- e escrever a conferencia aqui
/// criaria a segunda copia da regra: a copia que um dia discorda, e discorda EM FAVOR DA IA, que e
/// o pior jeito de errar.
///
/// O corolario e que este arquivo nao tem numero nenhum de jogo. Todo custo, todo prazo e toda
/// recusa moram na funcao chamada.
/// ================================================================================================
///
/// ============================ A LISTA DE PARIDADE ============================
/// Pra cada comando de corpo que o jogador manda (`ComandoDeCorpo`, GameServer.Clone.cs) existe um
/// campo no <see cref="Comando"/> e uma chamada aqui **pra a mesma funcao que o `switch` do
/// `Handle` chama**:
///
///   C2S.Action      -> Comando.Leve / .Pesado    -> Atacar(npc, golpe)
///   C2S.Guard       -> Comando.Guardar           -> npc.Combate.Guardar(bool)
///   C2S.Carregar    -> Comando.Carregar          -> Carregar(npc, bool)
///   C2S.Transformar -> Comando.SubirForma/.Descer-> Transformar(npc, subir)
///   C2S.InputState  -> Comando.Rumo/.Olhar/.Correndo/
///                      .QuerSubir/.QuerDescer    -> PassoDaIa (as MESMAS tres politicas do `Input`)
///   C2S.Habilidade  -> Comando.Habilidade        -> UsarHabilidade(npc, id)
///   C2S.Alvo        -> Comando.Marcar            -> Mirar(npc, id)
///   (o voo do jogador entra pelo verbo `Fly`)    -> AlternarVoo(npc)
///
/// As duas ultimas nasceram com o GANCHO DO ATAQUE DE LONGE e ficaram tres camadas sem uso, porque
/// nenhuma tecnica portada viajava entre tiques. Elas foram escritas mesmo assim -- pelo funil
/// certo e exercitadas na bancada -- pra que o dia do beam fosse "registrar uma linha de dado" e nao
/// "ensinar a IA a atirar". Foi o que aconteceu: hoje `TecnicasDeLonge` tem as tres que voam (raio,
/// bola, teleguiado) e estas duas linhas passaram a receber trafego sem mudar de forma.
///
/// Fica de fora, e de proposito: `C2S.Zanzoken` -- a IA nao pisca, e o respondedor de QTE do
/// ZanzoClash continua sendo uma decisao do dono (ou a IA ganha o lado dela, ou `TentarEmbate`
/// passa a exigir `Peer != null` nos dois lados).
/// ==========================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// `--diagia`: despeja no console TODA troca de plano de TODO corpo dirigido, com o motivo e os
	/// numeros que a produziram.
	///
	/// Desligado por padrao, e nao so por causa do ruido no console: com ele ligado o cerebro monta
	/// as frases de <see cref="Cerebro.Porque"/>, que sao strings interpoladas a 4 Hz por corpo.
	/// Mesmo desenho do `_diagGolpe`.
	/// </summary>
	private bool _diagIa;

	/// <summary>
	/// APLICA O COMANDO. A ordem importa e cada passo diz por que.
	/// </summary>
	private void AplicarComando(ServerPlayer npc, in Comando c, double dt)
	{
		// --- 1. PULSOS DE FORMA, ANTES DE TUDO --------------------------------
		// Transformar muda `MaxKi`, `Espeed` e o `SpeedStat`; feito depois do passo, o tique
		// inteiro andaria com a velocidade da forma velha. E o `Transformar` e quem cobra as
		// recusas todas -- inclusive o "tem a forma e nao a usa" do chefe (`AscendePorDecisao`).
		if (c.SubirForma) Transformar(npc, subir: true);
		else if (c.DescerForma) Transformar(npc, subir: false);

		// --- 2. O TOGGLE DO VOO ----------------------------------------------
		// `AlternarVoo` COBRA o Ki de decolagem (`Voo.CustoParaLigar`) e recusa quem nao sabe voar,
		// quem esta caido e quem nao tem folego. E a mesma funcao do verbo `Fly` do jogador.
		if (c.AlternarVoo) AlternarVoo(npc);

		// --- 3. AS TECLAS DE ALTURA ------------------------------------------
		// PEDIDOS continuos, escritos como o `Input` os escreve. Quem os obedece e o `TickDoVoo`, e
		// so pra quem esta voando -- afirmar o bit no chao nao levanta ninguem, aqui como la.
		npc.QuerSubir = c.QuerSubir;
		npc.QuerDescer = c.QuerDescer;

		// --- 4. A CARGA, SO NA TRANSICAO -------------------------------------
		// `Carregar(pl,true)` e idempotente por um `if (pl.Carregando) return`, mas `Carregar(pl,false)`
		// NAO e: ele chama `PararCarga`, que manda dois efeitos pro cliente. Chamado a 30 Hz seriam
		// 60 pacotes por segundo pra dizer que nada mudou.
		if (c.Carregar != npc.Carregando) Carregar(npc, c.Carregar);

		// --- 5. A GUARDA, SO NA TRANSICAO ------------------------------------
		// ============================ ISTO ERA UMA CHAMADA A 30 Hz ============================
		// `npc.Combate.Guardar(d.Guardar)` rodava todo tique e era inofensivo POR SORTE: o `if` de
		// `CombatState.Guardar` (`CombatState.cs:185`) so age na SUBIDA da guarda, entao o
		// `ContraPronto` nao era rearmado. Bastava alguem mexer naquele `if` -- pra consertar
		// qualquer outra coisa -- pra a IA passar a ter contra-ataque perfeito 30 vezes por segundo,
		// e ninguem ligaria o defeito a este arquivo.
		// ==================================================================================
		if (c.Guardar != npc.Combate.Bloqueando) npc.Combate.Guardar(c.Guardar);

		// --- 6. A MIRA, ANTES DE QUALQUER GOLPE ------------------------------
		// `Mirar` e a MESMA funcao do `C2S.Alvo` (ver `GameServer.Combat.cs`), com a mesma validacao
		// de zona e de existencia. Zero quer dizer "nao mexe" -- ver o campo no `Comando`.
		//
		// E ela vem antes do soco de proposito: o `Atacar` vira o corpo pro marcado antes de
		// arrancar, entao marcar depois de bater seria mirar pro golpe do tique QUE JA PASSOU.
		if (c.Marcar != 0 && c.Marcar != npc.AlvoId) Mirar(npc, c.Marcar);

		// --- 7. O PASSO ------------------------------------------------------
		PassoDaIa(npc, c, dt);

		// --- 8. A TECNICA ----------------------------------------------------
		// ============================ O CANAL E O MESMO DO JOGADOR ============================
		// O cerebro so preenche `Habilidade` na receita de atirar, e ela exige arsenal -- quem nao
		// comprou nenhuma das tres skills que voam nunca chega aqui. `UsarHabilidade` e literalmente
		// a funcao que o `case Protocol.C2S.Habilidade` do jogador chama.
		//
		// Como todo o resto deste arquivo, ela nao confere nada. Nao ha `if (SabeTecnica)` nem
		// `if (temKi)`: quem responde "voce nao sabe isso", "ainda se recompoe" e "isso pede pelo
		// menos N de energia" e a tecnica -- e essas tres frases sao as MESMAS que o jogador ouve.
		// ======================================================================================
		if (c.Habilidade is { Length: > 0 } tecnica) UsarHabilidade(npc, tecnica);

		// --- 9. O SOCO, POR ULTIMO -------------------------------------------
		// Depois do passo porque o `Atacar` faz o `Aproximar` a partir da posicao ATUAL: socar antes
		// de andar mediria a distancia do tique passado. E ele que cobra recarga, estamina e
		// derruba a guarda (`ca.Guardar(false)`) -- nada disso e repetido aqui.
		//
		// SOCO E TECNICA NAO SE EXCLUEM AQUI, e a falta do `else` e deliberada: escolher entre os
		// dois seria uma DECISAO, e decisao e do cerebro (que nunca manda os dois no mesmo tique --
		// a receita de atirar nao soca). Um `else` aqui esconderia um defeito de decisao pra sempre.
		if (c.Leve) Atacar(npc, Protocol.Golpe.Leve);
		else if (c.Pesado) Atacar(npc, Protocol.Golpe.Pesado);

		// --- 10. A FALA, DEPOIS DO GESTO -------------------------------------
		// ============================ O FUNIL E O `C2S.Chat` DO JOGADOR ============================
		// `Falar` e literalmente a funcao que o `case Protocol.C2S.Chat` chama, e por isso o NPC
		// herda de graca tudo o que uma pessoa herda: o raio de vista (22 tiles), o "!" que vira
		// grito e alarga o alcance pra 37, o corte de tamanho, o teto de uma fala a cada 400 ms e --
		// o que mais importa -- a familiaridade que CONVERSAR faz crescer (o `Ouviu`). Quem ouve um
		// NPC gritar passa a conhece-lo, exatamente como conhece uma pessoa.
		//
		// Um `Mandar(...)` direto daqui teria pulado os cinco. Ja aconteceu neste repo com outro
		// canal, e o sintoma foi "o NPC fala e ninguem nunca fica conhecido dele".
		//
		// DEPOIS DO GESTO, e nao antes: a frase COMENTA o que acabou de acontecer ("Toma essa!" sai
		// junto com o soco). Antes, ela sairia um tique adiantada -- e num jogo a 30 Hz isso e
		// invisivel, mas a ordem certa e a que nao precisa ser explicada depois.
		// ======================================================================================
		if (c.Falar is { Length: > 0 } frase) Falar(npc, Protocol.Fala.Diz, frase);
	}

	/// <summary>
	/// O PASSO DA IA -- as MESMAS TRES POLITICAS do <see cref="Input"/>, na mesma ordem.
	///
	/// ============================ POR QUE NAO DA PRA CHAMAR "A FUNCAO DE PASSO DO JOGADOR" ============================
	/// Porque ela nao existe, e juntar as duas produziria uma funcao pior. O jogador vai por
	/// `MoveRules.ValidateStep` -- que **confere uma posicao que o cliente afirmou** -- e a IA vai
	/// por `MoveRules.Advance` -- que **gera a posicao**. Sao perguntas opostas (`MoveRules.cs:91`
	/// vs `:159`), e uni-las daria uma funcao com dois modos e um `if` no meio.
	///
	/// O que E comum, e e o que estava faltando, sao as tres POLITICAS que decidem COMO o passo
	/// acontece. As tres estao aqui, com a mesma chamada e o mesmo custo:
	///
	///   1. `MarchaDeVoo(pl, shift)`  -- o Superflight;
	///   2. `PodeCorrer(pl, dt)`      -- **e ele que COBRA** `MaxKi * 0,02` por segundo de corrida;
	///   3. o mapa nulo em altura      -- `AtravessandoCenario`, o `isflying` do original.
	/// ==========================================================================================================
	///
	/// ============================ DOIS DEFEITOS QUE MORAVAM AQUI ============================
	///   * O MAPA ERA SEMPRE PASSADO (`_catalogo?.Get(npc.Zone)?.Mapa`, direto). Um NPC voando a 15
	///     tiles de altura batia em paredes que o jogador na mesma altura atravessa -- e nao havia
	///     nada na tela explicando por que o boneco parou no ar.
	///   * A CORRIDA NAO EXISTIA. `Advance` tem `correndo = false` por padrao (`MoveRules.cs:92`) e
	///     a IA nunca passava o argumento. Ligar so o campo `npc.Correndo` (a tentacao obvia) seria
	///     PIOR: daria 60% de velocidade **de graca** -- literalmente o buraco que o comentario do
	///     `Input` diz existir pra cliente modificado -- ou, ao contrario, um corpo que a rede diz
	///     que corre e que anda devagar. Os tres passos ou nenhum.
	/// ====================================================================================
	/// </summary>
	private void PassoDaIa(ServerPlayer npc, in Comando c, double dt)
	{
		// O MESMO PORTAO DO JOGADOR: caido, carregando ou em embate, ninguem anda. Ver
		// `PodeMexerOCorpo` -- a funcao e uma so, e o `Input` a chama tambem.
		if (!PodeMexerOCorpo(npc)) { npc.Moving = false; npc.Correndo = false; npc.Ficha.dashing = false; return; }

		MarchaDeVoo(npc, c.Correndo);

		bool andando = c.Rumo.LengthSquared > 1e-6f;
		bool correndo = c.Correndo && andando && (npc.Voando || PodeCorrer(npc, (float)dt));

		// ============================ O OLHAR VEM ANTES DO PASSO -- E ELE E A POSTURA, NAO A REGRA DO GOLPE ============================
		// **ATENCAO: esta linha NAO e mais o conserto do soco de costas.** Ela e a POSTURA do corpo
		// dirigido -- pra onde ele esta voltado enquanto anda, circula, guarda ou recua --, e ela
		// vale enquanto o corpo puder se mexer.
		//
		// QUEM GARANTE QUE NINGUEM SOCA DE COSTAS E O `Atacar` (`GameServer.Combat.cs`), que vira o
		// corpo pro alvo MARCADO antes de arrancar -- o caminho do GOLPE, que nao tem recusa nenhuma,
		// que e onde o DM poe a correcao (`commonAttackProcs`, `attack cmn.dm:158`) e que ja valia
		// pro jogador. Esta linha aqui esta atras do `PodeMexerOCorpo`, e havia estados que recusam o
		// passo e NAO recusam o soco (paralisado, enraizado por Ki, prensado pela gravidade): neles o
		// olhar nao era escrito e o punho saia na direcao do ultimo passo. Por isso a regra desceu
		// pro golpe e a IA passou a MARCAR quem enfrenta (`Cerebro.Montar`).
		//
		// As duas nao divergem: as duas leem o MESMO alvo do cerebro, e o `Atacar` roda depois do
		// passo (ver a ordem do `AplicarComando`), entao no instante do soco ele e quem manda.
		//
		// Antes ela morava depois do `if (!andando) return` la embaixo, ou seja: **so quem andava
		// virava**. Um corpo parado guardava a direcao do ULTIMO passo, e as receitas de combate dao
		// passos pra tras (colado demais na pressao, `Cerebro.Pressao`; abrindo espaco pro tiro,
		// `Cerebro.Disparo`; respirando, `Plano.Recuar`). O passo pra tras escrevia "olhando pro lado
		// oposto ao inimigo" e no MESMO tique saia o soco -- que so acha alvo dentro do cone da frente
		// (`AlvoNaFrente` -> `MeleeArea.NoAlcance`, o `compileRangeMobList` do DM). Resultado medido
		// pelo `--diaggolpe`: 38% dos golpes da IA sem alvo nenhum, 81% deles com o oponente a 180°,
		// no alcance e fora do cone. Nao era pontaria ruim -- era o inimigo estar atras.
		//
		// E o `dir = get_dir(src,target)` do DM (`NPCAI.dm:386`), a primeira linha do seletor de acao
		// da IA de la, logo depois do `step_away(src,target)` do `attackState` (`:536`). A estrutura
		// do port ja era a mesma; faltava a linha.
		//
		// ---- POR QUE AQUI DENTRO, E DEPOIS DO PORTAO DO PASSO ----
		// Porque e assim que o jogador funciona, e paridade nao e detalhe aqui. O `Input` recusa o
		// pacote INTEIRO -- olhar junto com passo -- quando `!PodeMexerOCorpo(pl)`: quem esta caido,
		// carregando, paralisado, prensado pela gravidade ou num embate nao gira, e nao gira porque
		// nesses estados a direcao do corpo e do SERVIDOR (o ZanzoClash crava as duas em
		// `GameServer.ZanzoClash.cs:657`, e o desfecho poe o vencedor nas costas do perdedor em
		// `:825`). Estando atras do mesmo portao, a IA herda as cinco recusas de graca e nao ha uma
		// segunda lista pra manter em dia.
		//
		// Os tres casos que o dono nomeou saem resolvidos por consequencia, e nao por excecao escrita:
		//   * CORPO NOCAUTEADO -- `PodeMexerOCorpo` ja barra (`KO`/`dead`), e antes dele o proprio
		//     cerebro devolve `Comando.Nenhum` (`Cerebro.Pensar`, "caido nao faz nada"). O sprite
		//     deitado desenha pela `DirecaoDeitado`, que nem le este campo.
		//   * CORPO LARGADO (o boneco do transe) -- ele nasce com `Cerebro == null` e `Peer == null`
		//     (`GameServer.CorpoLargado.cs:86`), entao nao entra no `TickDosCorposSemDono` e nunca
		//     chega aqui. Continua parado, olhando pra onde parou.
		//   * QUEM FOGE -- o `Plano.Fugir` (o `runawayState` do DM, `:646`) desliga o olhar do lado do
		//     CEREBRO mandando `Olhar` zero, e nao com um `if` novo aqui: com zero, a linha abaixo cai
		//     no rumo do passo e o corpo olha pra onde corre. E ele nao vira pelo golpe tambem, porque
		//     `Fuga` nao manda `Leve` nem `Pesado` -- quem foge nao bate, entao o `Atacar` nem roda.
		// ==================================================================================================================
		Vec2 olhar = c.Olhar.LengthSquared > 1e-6f ? c.Olhar : c.Rumo;
		if (olhar.LengthSquared > 1e-6f) npc.Facing = MoveRules.FacingFrom(olhar, npc.Facing);

		if (!andando)
		{
			npc.Moving = false;
			npc.Correndo = false;
			npc.Ficha.dashing = false;
			return;
		}

		ZoneCollision? mapa = AtravessandoCenario(npc) ? null : MapaDaZonaOuCatalogo(npc.Zone);

		Vec2 antes = npc.Pos;
		// ============================ A AGUA VALE PRA IA PELA MESMA FUNCAO ============================
		// O `modo` sai do MESMO `ModoDeTravessiaDe` que o `Input` do jogador usa -- e nao de um `if`
		// escrito aqui --, que e a regra deste arquivo desde o `PodeMexerOCorpo`: "as mesmas menos
		// uma" e o jeito classico de o NPC ficar op sem ninguem notar. Nenhum NPC nada hoje (nao ha
		// verb de IA pra isso), entao na pratica ele so responde `APe` ou `Voando` -- e e por isso
		// que o NPC que voa continua passando por cima do lago.
		//
		// A PE, A AGUA PARA O NPC IGUAL A UMA PAREDE, e nao ha caminhamento nenhum a consertar: a IA
		// nao contorna obstaculo (nao ha A* aqui), ela desliza no eixo livre e para. O lago se
		// comporta exatamente como o muro que ja existia.
		// =========================================================================================
		// ============================ E O CORPO ALHEIO PARA O NPC PELA MESMA FUNCAO ============================
		// O `Vizinhanca` sai do `VizinhancaDe`, o mesmo que o resto do servidor usa, e entra no MESMO
		// `Advance` -- nao ha um "desvio de NPC" escrito aqui. E a regra deste arquivo desde o
		// `PodeMexerOCorpo`: "as mesmas menos uma" e o jeito classico de o NPC ficar op sem ninguem
		// notar, e atravessar gente e op de um jeito bem visivel (o habitante que passa por dentro do
		// jogador, o chefe que ignora o corpo na frente dele).
		//
		// AQUI A PERGUNTA E GERADA E NAO CONFERIDA -- o servidor E quem move o NPC --, entao ela nao cai
		// na ressalva do `MoveRules.ValidateStep` (que abstem-se dos corpos de proposito; ver la).
		// ====================================================================================================
		npc.Pos = MoveRules.Advance(npc.Pos, c.Rumo, (float)dt, npc.SpeedStat, mapa, out _, correndo,
									ModoDeTravessiaDe(npc), VizinhancaDe(npc));
		npc.Moving = (npc.Pos - antes).LengthSquared > 0.01f;
		npc.Correndo = correndo;
		npc.Ficha.dashing = correndo;   // entra na conta de dano, igualzinho ao do jogador
	}

	/// <summary>
	/// ============================ ESTE CORPO PODE ANDAR AGORA? ============================
	/// As quatro condicoes que o portao do <see cref="Input"/> ja cobrava, extraidas pra que a IA
	/// obedeca as MESMAS -- e nao "as mesmas menos uma", que e o jeito classico de o NPC ficar op
	/// sem ninguem notar.
	///
	/// A que mais importa e o `Carregando`: reunir energia PRENDE o corpo, e sem esta linha o NPC
	/// andaria carregando -- exatamente o "op demais" que o dono cortou do jogador.
	///
	/// O que NAO esta aqui e a possessao (`SemAsRedeas`): ela nao e sobre o CORPO poder andar, e
	/// sim sobre QUEM manda nele. Pro jogador possuido ela e uma recusa; pra a IA ela e a licenca.
	/// Por isso o `Input` a pergunta separado.
	/// ==================================================================================
	/// </summary>
	/// <remarks>
	/// O `EnraizadoPorKi` e o `canmove = 0` dos verbs de raio (`beams.dm:294`) -- quem esta
	/// carregando ou segurando um beam fica PLANTADO. Ele entra aqui, e nao numa trava de input,
	/// porque e um gate de VETOR: o pacote continua chegando, o olhar continua girando, a guarda e a
	/// fala continuam valendo, e -- o que importa -- o proprio verb continua podendo ser apertado
	/// pra soltar o raio. Uma trava de input global deixaria o jogador preso dentro da tecnica ate
	/// o Ki acabar.
	///
	/// E entrando NESTA funcao ele vale tambem pra IA, que e o unico jeito de o NPC nao ganhar de
	/// graca um raio que anda -- o defeito classico de "o NPC obedece as mesmas regras menos uma".
	///
	/// O `_emEmbateDeKi` (a colisao de ki) entra pelo mesmo motivo, e a linha nao e redundante: quem
	/// esta ATIRANDO ja estava preso pelo `EnraizadoPorKi`, mas quem esta SEGURANDO o feixe com as
	/// maos nao tem canal nenhum -- e sao os pes dele cavando o chao que a disputa representa.
	/// </remarks>
	/// <remarks>
	/// A PARALISIA entra aqui pelo mesmo motivo do raio: e um gate de VETOR e nao de input. Quem
	/// levou uma Paralysis continua socando, bloqueando e defletindo -- so nao sai do lugar
	/// (`movement handler.dm:89`). E, entrando NESTA funcao, ela vale pra IA sem uma linha a mais:
	/// um NPC paralisado para de andar pela regra do jogador. Ver `_paralisadoAte`.
	/// </remarks>
	/// <remarks>
	/// O ESMAGAMENTO (`gravParalysis`) e a mesma familia: no DM ele vira `mobTime = 0`
	/// (`movement handler.dm:132`), lado a lado com a paralisia de tecnica. Quem esta a quatro vezes
	/// a propria maestria de gravidade -- ou carregando quatro vezes o que o corpo aguenta -- fica
	/// PRENSADO no chao; continua socando e defendendo, so nao sai do lugar.
	///
	/// A diferenca pras outras entradas desta lista e que ela nao consulta dicionario nenhum: e uma
	/// funcao pura da ficha (`Esmagamento.Prende`). Tirar o peso ou sair do planeta solta o corpo no
	/// tique seguinte, sem ninguem precisar lembrar de apagar um bit.
	/// </remarks>
	/// <remarks>
	/// A CINEMATICA DE TRANSFORMACAO e a entrada mais nova, e ela e da mesma familia de todas as
	/// outras: no DM a cena abre com `move=0` e fecha com `move=1` (`SSJ2Cinematic.dm:4` e `:44`, e
	/// igual nas outras), e quem le esse bit e o `movement handler.dm:131` -- `if(!move)mobTime=0`,
	/// **a linha imediatamente abaixo** do `canmove` que ja trouxe pra ca o raio, a paralisia e o
	/// androide. Ou seja: la tambem e gate de VETOR, e nao de input. Quem esta na cena continua
	/// virando o corpo pelo golpe, continua sendo alvo e continua com os pacotes chegando.
	///
	/// ---- POR QUE AQUI, E NAO NUM `if` DENTRO DO `PassoDaIa` ----
	/// O dono reclamou de NPC andando enquanto transforma. O portao do JOGADOR ja existia e mora no
	/// cliente (`LocalPlayer.cs:525`, o vetor zerado por `Transformacao.PrendendoOCorpo`) -- ele
	/// funciona, o dono conferiu, e ele NAO foi tocado. Mas ele e do dono da tela: o servidor nunca
	/// soube dele, e por isso a IA passava direto. Escrever a recusa dentro do `PassoDaIa` daria
	/// duas regras pra mesma frase ("quem esta em cinematica nao anda"), que e o defeito que este
	/// arquivo inteiro existe pra evitar. Entao a pergunta desceu pro funil por onde os DOIS passam
	/// -- o `Input` do jogador (`GameServer.cs`) e o atuador da IA --, e o jogador so ganha no
	/// servidor a regra que o cliente dele ja obedecia (o cinto de seguranca contra cliente
	/// modificado, exatamente como o "andar carregando" que ja mora nesta lista).
	///
	/// ---- E ELA SE SOLTA SOZINHA EM TODO JEITO DE A CENA ACABAR ----
	/// Nao ha bit novo pra apagar: `EmCena` deriva do `CenaSegundos`, escrito SO pelo `MarcarCena`
	/// (dentro do funil unico `AnunciarForma`) e abatido SO no topo do `TickDaForma` -- que roda pra
	/// TODO corpo de `_players`, todo tique cheio, antes de qualquer `return` (o `NaBase` inclusive).
	/// Prazo vencendo, nocaute, morte (os dois revertem a forma, e reverter remarca a cena da base em
	/// ZERO), troca de zona ou logout: em nenhum deles o relogio para de escorrer. E a mesma razao
	/// pela qual a imunidade da cinematica (`CombatState.EmCinematica`) pode ser derivada e nao
	/// guardada.
	/// </remarks>
	private bool PodeMexerOCorpo(ServerPlayer pl) =>
		!pl.Ficha.dead && !pl.Ficha.KO && !pl.Carregando && !_emEmbate.ContainsKey(pl.Id)
		&& !_emEmbateDeKi.ContainsKey(pl.Id) && !EnraizadoPorKi(pl.Id) && !Paralisado(pl.Id)
		&& !EmCena(pl)
		// OS COLETORES ABERTOS DO ANDROIDE DE ABSORCAO (`canmove = 0`, `DNALabs.dm:227`). E o preco
		// da imunidade a ki: fincado no chao ate desligar o verb. Entra AQUI e nao no `Input` porque
		// este e o funil de vetor -- e assim ela vale pra IA tambem, de graca.
		&& !pl.Ficha.ki_absorb_stance
		// LEVADO POR UM FEIXE (`ArrastoRestante`). Quem esta sendo empurrado por um Kamehameha nao
		// escolhe pra onde vai -- e a mesma frase que o arremesso ja diz, entao ela entra pelo MESMO
		// funil de vetor e vale pro jogador e pra IA de uma vez. Sem ela o NPC continuaria caminhando
		// contra o feixe enquanto o feixe o empurra, e o corpo ficaria decidido por quem escreveu
		// `Pos` por ultimo no tique -- exatamente o defeito que ninguem reproduz.
		//
		// (O ARREMESSO nao esta nesta lista, e isso e de proposito e vem de antes: ele tem porta
		// propria no `Input` -- `if (pl.TiquesDeVoo > 0)`, `GameServer.cs` -- porque durante o voo o
		// cliente PRECISA continuar mandando a direcao do olhar. O arrasto nao precisa: o DU vira a
		// vitima pra tras a forca (`P.dir=turn(dir,180)`, `Projectiles.dm:587`), e quem escolhe o
		// angulo aqui e o `DirecaoDeitado` pelo rumo do feixe.)
		&& pl.ArrastoRestante <= 0
		// PUXADO PRA UMA FUSAO (`PuxaoDeFusaoRestante`). O DU desliga o input dos DOIS enquanto o laco
		// de aproximacao roda -- `AlterInputDisabled(1)` antes e `(-1)` depois
		// (`Potara_Fusion.dm:122-123` e `:131-132`) --, e este e o funil por onde "input desligado" se
		// diz neste port. Sem ele os dois continuariam andando contra o puxao e o corpo ficaria decidido
		// por quem escreveu `Pos` por ultimo no tique, que e o defeito que este bloco inteiro documenta.
		&& pl.PuxaoDeFusaoRestante <= 0
		// AGARRADO POR ALGUEM (`grabParalysis`, `Grabbing.dm:183`). Entra por este funil e nao por uma
		// porta propria no `Input` pela razao que este bloco inteiro documenta: assim o NPC agarrado
		// para de andar pela MESMA regra que trava o jogador, e nao por uma segunda escrita a mao no
		// atuador da IA -- que e o defeito que fez esta funcao existir.
		//
		// O gesto de SE DEBATER nao morre com a recusa: ele e lido no `Input` **antes** desta porta e
		// guardado em `DebatendoSe`. Ver la, e o `LutaPraEscapar`.
		&& !Agarrado(pl)
		&& !PrensadoPelaGravidade(pl)
		// PLANTADO POR UMA TECNICA DO LOTE G12 (`usr.move = 0` da Death Ball e da Genkidama, `canmove = 0`
		// das rajadas). Mesmo funil, pela mesma razao de tudo acima.
		&& !PresoPeloG12(pl.Id);

	/// <summary>
	/// O QUE O JOGO RESPONDE QUE ESTE CORPO PODE FAZER. Lido a 1 Hz por corpo (ver
	/// <see cref="Cerebro.PrecisaLerCapacidades"/>) porque varre o catalogo de formas.
	///
	/// **Todo campo aqui e o retorno de uma funcao de recusa da producao.** Nao ha uma regra
	/// escrita nesta funcao -- ela so pergunta e anota. E o que faz uma recusa nova (uma skill, um
	/// cargo, um debuff) valer pra a IA no mesmo instante em que passa a valer pro jogador.
	/// </summary>
	private Capacidades LerCapacidades(ServerPlayer pl)
	{
		double hab = HabilidadeDeVoo(pl);
		double kiFrac = pl.Ficha.MaxKi > 0 ? pl.Ficha.Ki / pl.Ficha.MaxKi : 1;

		// A ESCADA, PELO MESMO SELETOR DA TECLA C. `Proxima` avalia com `kiFracao: 1` -- ela diz que
		// degrau este corpo ALCANCA. A recusa REAL (com o Ki de agora) sai da segunda chamada, e e
		// ela que transforma `SemKi` em pre-condicao reparavel em vez de fracasso. Ver `Capacidades`.
		//
		// UM `Perfil` SO, e nao tres. Ele e `readonly record struct` (nao aloca), mas o `TemEscada`
		// que eu chamava antes monta um `HashSet` por chamada (`Catalogo.LinhasAbertas`) -- e ele e
		// REDUNDANTE aqui: sem linha aberta, `Proxima` ja devolve nulo, porque `Avaliar` recusa todo
		// degrau com `LinhaFechada`. Uma pergunta a menos com a mesma resposta.
		PerfilDeFormas perfil = Perfil(pl);
		FormaDef? proxima = pl.Forma.Proxima(pl.Ficha.BP, perfil);
		RecusaForma recusa = proxima == null
			? RecusaForma.JaEsta
			: pl.Forma.Avaliar(proxima.Id, pl.Ficha.BP, kiFrac,
							   pl.Ficha.KO || pl.Ficha.dead, perfil);

		return new Capacidades
		{
			PodeVoar = PodeVoar(pl),
			CustoDeDecolar = Voo.CustoParaLigar(hab, pl.VooRapido),
			CustoDoVooPorSegundo = Voo.CustoPorSegundo(hab, pl.VooRapido),
			SabeReunirKi = CargaDeKi.SabeReunir(pl.Ficha),
			HaDegrauAcima = proxima != null,
			RecusaDaForma = recusa,
			// SEM AVISAR: isto e o retrato do corpo pro cerebro, e nao um gesto de ninguem. Ver o
			// parametro em `AscendePorDecisao`.
			AscendePorDecisao = AscendePorDecisao(pl, avisar: false),
			TemComQueAparar = TemComQueAparar(pl),
			CustoDaGuarda = pl.Ficha.MaxKi * CombatKnobs.CustoKiDaGuarda,
			DeLonge = ArsenalDeLonge(pl),

			// O SOPRO. A pergunta e a MESMA do jogador (`SabeTecnica`, quem responde "voce nao sabe
			// isso" pro verb) e o preco e a MESMA expressao que o verb cobra (`CustoDoSopro`,
			// `GameServer.Tecnicas.G6.cs`) -- nao ha aqui um `50` escrito, e e por isso que mexer no
			// preco da tecnica muda a decisao da IA no mesmo commit.
			SabeSopro = SabeTecnica(pl, "Kiai"),
			CustoDoSopro = CustoDoSopro(pl, TipoDeSopro.Kiai),
		};
	}

	/// <summary>
	/// O QUE ESTE CORPO PODE ATIRAR DE LONGE -- as tecnicas de `TecnicasDeLonge` que ele SABE.
	///
	/// ============================ A PODA VEM ANTES DA VARREDURA, E ELA E O PONTO ============================
	/// A pergunta "quais das minhas skills sao ataque a distancia?" custa varrer o livro inteiro e,
	/// pra cada skill, a lista de verbs dela -- e o `SabeTecnica` ja faz exatamente isso e ja e o
	/// caminho caro do sistema de tecnicas. Pago 1 vez por segundo por corpo, tudo bem -- e quem nao
	/// tem nenhuma das tres skills paga so a varredura, sem alocar nada.
	///
	/// Esta e a resposta direta ao risco que este desenho carrega desde a camada 2: *a percepcao vai
	/// engordar, e cada campo novo e uma varredura*. Aqui a varredura nasce ja atras de um portao, e
	/// atras do relogio de 1 Hz -- e nao no caminho de 30 Hz onde ela nao apareceria em medicao
	/// nenhuma ate alguem cronometrar o quadro certo.
	/// ==================================================================================================
	///
	/// E O ARRAY SO NASCE SE HOUVER ALGO NELE: `Arsenal.Vazio` nao aloca, entao um corpo sem tecnica
	/// de longe continua com o mesmo lixo por tique que a bancada ja afirma.
	/// </summary>
	private Arsenal ArsenalDeLonge(ServerPlayer pl)
	{
		if (!TecnicasDeLonge.Alguma || _skills == null) return Arsenal.Vazio;

		List<Tiro>? achados = null;
		foreach (TecnicasDeLonge.Linha linha in TecnicasDeLonge.Todas)
		{
			// A PERGUNTA E A DO JOGADOR, e e a mesma funcao: `SabeTecnica` e quem responde "voce nao
			// sabe isso" quando o verb chega pelo `C2S.Habilidade`. Entao o arsenal da IA nao pode
			// conter uma tecnica que o jogo recusaria -- se puder, e porque duas leituras divergiram,
			// e o defeito seria uma IA que atira sem saber.
			//
			// PERGUNTAR PELAS TRES, e nao listar os verbs todos: a versao anterior chamava `TecnicasDe`,
			// que monta a lista COMPLETA de habilidades do corpo pra depois filtrar tres. Num
			// personagem com muitas skills isso e uma lista de centenas de strings por segundo, e a
			// bancada de alocacao da IA reprovou na hora em que a tabela deixou de estar vazia.
			if (!SabeTecnica(pl, linha.Id)) continue;

			(achados ??= []).Add(new Tiro
			{
				Id = linha.Id,
				AlcanceMin = linha.AlcanceMinTiles * ZoneCollision.TileSize,
				AlcanceMax = linha.AlcanceMaxTiles * ZoneCollision.TileSize,
				TempoDeConjuracao = linha.TempoDeConjuracao,

				// O PRECO SAI DA FUNCAO DA PROPRIA TECNICA -- ver o cabecalho de `TecnicasDeLonge`.
				// Nenhum numero de preco e escrito aqui, e essa e a unica forma de a IA nao decidir
				// com uma tabela de custos paralela que envelhece.
				CustoDeKi = linha.CustoDeKi(pl.Ficha),

				PrecisaDeLinhaLivre = linha.PrecisaDeLinhaLivre,
				Precisao = linha.Precisao,
			});
		}
		return achados == null ? Arsenal.Vazio : new Arsenal([.. achados]);
	}

	/// <summary>
	/// SOBROU BRACO OU PERNA PRA APARAR? A MESMA pergunta que o `MeleeResolver.EscolherGuarda` faz
	/// no instante do golpe (`MeleeResolver.cs:95`: sem membro, a guarda cai sozinha).
	///
	/// Ela existe aqui pra a IA nao INSISTIR numa guarda que o resolvedor vai derrubar -- e nao pra
	/// substituir a checagem de la. Um NPC com os dois bracos quebrados que continuasse erguendo a
	/// guarda pagaria o Ki e nao apararia nada.
	/// </summary>
	private static bool TemComQueAparar(ServerPlayer pl)
	{
		foreach (BodyPart p in pl.Combate.Corpo.Partes)
			if (!p.Aninhado && p.Zona is "bracos" or "pernas" && !p.Decepado && !p.Quebrado)
				return true;
		return false;
	}

	/// <summary>
	/// O MUNDO COMO ELE VE. Nada aqui e varredura nova: todo campo ja existia por outro motivo, e
	/// o alvo ja vem escolhido de fora (o <see cref="PresaDaFera"/>, que era quem ja escolhia).
	///
	/// A UNICA EXCECAO E A LINHA DE VISAO, e ela e paga so por quem tem o que atirar -- ver
	/// <see cref="LinhaDeVisaoLivre"/>. Quem nao comprou nenhuma das tres skills que voam tem
	/// `quemAtira` falso e o metodo nem e chamado.
	/// </summary>
	private Percepcao LerPercepcao(ServerPlayer npc, ServerPlayer? alvo, Vec2 destino, bool quemAtira)
	{
		Fighter f = npc.Ficha;
		bool temAlvo = alvo != null;

		return new Percepcao
		{
			IdDoAlvo = alvo?.Id ?? 0,
			AlvoSeMovendo = alvo?.Moving ?? false,
			LinhaLivre = quemAtira && alvo != null && LinhaDeVisaoLivre(npc, alvo),

			Minha = npc.Pos,
			MinhaAltitude = npc.Altitude,
			EstouVoando = npc.Voando,
			VidaFrac = f.HP / 100.0,
			KiFrac = f.MaxKi > 0 ? f.Ki / f.MaxKi : 1,
			Ki = f.Ki,
			FolegoFrac = f.maxstamina > 0 ? f.stamina / f.maxstamina : 1,
			Caido = f.KO || f.dead,
			Atordoado = npc.Combate.Stun > 0,
			MeuPoder = Finito(f.expressedBP),

			// SEM ALVO, O DESTINO E UM PONTO (o vagar da fera). Ele entra como "alvo" no chao pra a
			// mesma conta de rumo servir aos dois casos -- foi sempre assim neste laco --, mas
			// `TemAlvo` continua falso, entao nenhuma decisao de luta se aplica a um ponto.
			TemAlvo = temAlvo,
			DoAlvo = alvo?.Pos ?? destino,
			AltitudeDoAlvo = alvo?.Altitude ?? 0f,
			AlvoVoando = alvo?.Voando ?? false,
			AlvoCaido = alvo is { } a && (a.Ficha.KO || a.Ficha.dead),
			VidaDoAlvo = (alvo?.Ficha.HP ?? 100) / 100.0,
			PoderDoAlvo = Finito(alvo?.Ficha.expressedBP ?? 0),
		};
	}

	/// <summary>
	/// TEM PAREDE ENTRE OS DOIS? A pergunta cara da percepcao, e por isso ela e feita SOB DEMANDA --
	/// so pra quem tem ataque de longe no arsenal, ou seja: hoje, ninguem.
	///
	/// ============================ ALTURA ABRE A LINHA, E ISSO NAO E ATALHO ============================
	/// Quem esta alto o bastante ja ATRAVESSA o cenario (`Voo.AtravessaCenario` -- e a mesma regra
	/// que o passo obedece, e que consertou o NPC batendo em paredes que o jogador voando cruza).
	/// Perguntar por colisao de chao pra um corpo que esta 15 tiles acima do muro daria "sem linha"
	/// pra um golpe que passaria por cima do muro tranquilamente -- seria a geometria do jogo
	/// respondendo uma coisa e a IA acreditando noutra.
	///
	/// Basta UM dos dois estar em altura de travessia: se eu estou em cima, atiro por cima; se ele
	/// esta em cima, o alvo esta exposto. O caso que sobra -- os dois no chao, com um muro no meio --
	/// e o unico em que a parede importa de verdade, e ai vale o `PathBlocked`, que e o mesmo
	/// amostrador que o movimento usa.
	/// ============================================================================================
	/// </summary>
	private bool LinhaDeVisaoLivre(ServerPlayer npc, ServerPlayer alvo)
	{
		if (AtravessandoCenario(npc) || AtravessandoCenario(alvo)) return true;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(npc.Zone);
		return mapa == null || !mapa.PathBlocked(npc.Pos, alvo.Pos);
	}

	/// <summary>
	/// ============================ O PODER PODE VIR NaN, E A CONTA INTEIRA APODRECE ============================
	/// `expressedBP` passa por caminhos que ja produziram `NaN` (o sigilo de BP mordeu a bancada de
	/// voo exatamente assim). Um `NaN` aqui contamina a `RazaoDePoder`, e comparacao com `NaN` e
	/// SEMPRE falsa -- entao o NPC simplesmente pararia de escalar, calado e pra sempre.
	///
	/// Zero e a resposta certa pra "nao sei": a razao de poder cai pra 1 (parelho) e a decisao passa
	/// a depender so da vida, que e um numero em que da pra confiar.
	/// ====================================================================================================
	/// </summary>
	private static double Finito(double x) => double.IsFinite(x) && x > 0 ? x : 0;
}
