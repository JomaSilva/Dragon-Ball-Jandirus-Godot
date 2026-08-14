namespace Jandirus.Core.Forms;

/// <summary>O que um instante da cinematica faz. Um beat pode acumular varios.</summary>
[Flags]
public enum Efeito
{
	Nada = 0,
	/// <summary>Tremor de camera. `Quake()`.</summary>
	Tremor = 1,
	/// <summary>
	/// [APOSENTADO] Onda de choque em shader (`createShockwavemisc`).
	///
	/// ============================ POR QUE SAIU ============================
	/// E o efeito que o dono viu cobrindo o personagem: um `ColorRect` de 320x320 com o shader de
	/// transformacao, desenhado por cima do corpo. O desenho dele e uma espiral escura que se abre
	/// -- e num corpo de 32 px ela nao le como "onda saindo do chao", le como o personagem sumindo
	/// atras de um borrao preto.
	///
	/// O tremor de camera e as crateras ja contam a mesma coisa sem tapar ninguem.
	///
	/// O valor fica reservado pra o bit nao ser reaproveitado por um efeito novo -- mas nenhuma
	/// cena o usa e o tocador nao o le mais.
	/// ==================================================================
	/// </summary>
	Onda = 2,
	/// <summary>
	/// CRATERA NO CHAO (`createCrater`) -- E ELA NAO SE ESCREVE, ELA SE DERIVA.
	///
	/// ============================ ESCREVER AQUI NAO ADIANTA ============================
	/// Quem poe a cratera na cena e a <see cref="Cinematica.Beats"/> (ver o funil la): ela cai no
	/// beat que <see cref="Assumir"/> e em nenhum outro, em toda cena, sem excecao e sem lista. Um
	/// `Efeito.Cratera` escrito a mao noutro instante e apagado; escrito no beat que assume e
	/// redundante. As duas coisas sao inofensivas de proposito -- o que nao pode existir e uma cena
	/// que abra o chao antes de a forma chegar.
	///
	/// ============================ POR QUE VIROU REGRA E NAO CONSERTO ============================
	/// O dono: *"tem transformacao q estao criando a cratera no meio da cinematica (deveria ser
	/// sempre no final, assim q se transformar cria a cratera)"*. Doze das trinta e duas cenas
	/// erravam -- oito segundos ANTES no molde do SSJ1, dois DEPOIS no do SSJ2, e sessenta e sete
	/// antes no SSJ3 --, e as doze saiam de QUATRO lugares. Consertar os quatro deixaria a cratera
	/// continuando a ser um campo livre do beat: a decima terceira cena erraria do mesmo jeito, e
	/// caladamente, porque nada no arquivo diria onde ela deveria cair.
	///
	/// O que estava escrito nos quatro lugares era porte literal do DM (`spawn(20) createCrater` cai
	/// 2,0 s depois do `move = 1`). So que la o `move = 1` e o fim da PROC e nao o instante em que
	/// `ssj = 1` e escrito -- o original tambem abre o chao quando a forma chega, e a nossa lista de
	/// beats e que tinha separado as duas coisas. Ver `SSJCinematic.dm:83` / `SSJ2Cinematic.dm:45`.
	///
	/// ============================ E A GRANDE CONTINUA SENDO DERIVADA TAMBEM ============================
	/// Qual das duas artes cai (`Decal.Cratera` ou `Decal.CrateraGrande`) NAO se decide aqui e nem no
	/// beat: quem responde e `Catalogo.NasceDaRaiva(forma)`, no tocador. Ver
	/// `Jandirus.Client.Transformacao.Disparar`.
	/// ==================================================================================
	/// </summary>
	Cratera = 4,

	/// <summary>
	/// POEIRA (`createDustmisc`) -- E ELA E A POEIRA DA CRATERA, so isso.
	///
	/// ============================ ANTES DA TROCA NAO HA POEIRA ============================
	/// O dono: *"a dust cloud vc colocou ela de mais durante as cinematicas, ela deveria apenas vir
	/// quando a animacao cria uma cratera"*. Eram 137 beats de poeira nas 32 cenas -- 441 nuvens --,
	/// e so 8% deles caiam junto de uma cratera; o `ssj4_full_power` chegava a 2,17 nuvens por
	/// segundo. O DM concorda com o dono, e isso e o que fecha a questao: as cinematicas do original
	/// nao tem nuvem NENHUMA (todo `createDustmisc` delas e `s=2`, que e o Rising Rocks, ou `s=3`,
	/// que e o tornado -- a nuvem grande e `s=4` e nao aparece em cinematica alguma).
	///
	/// Entao o funil da <see cref="Cinematica.Beats"/> apaga toda poeira ANTERIOR ao
	/// <see cref="Assumir"/> e poe uma no proprio beat que assume, junto da <see cref="Cratera"/>.
	///
	/// ============================ E DEPOIS DELA, A POEIRA ASSENTA ============================
	/// O que vem DEPOIS do beat que assume o funil deixa passar -- e e a unica coisa que ele deixa.
	/// Nao e frouxidao: toda cena tem exatamente um beat de cauda (a bancada reprova o segundo, ver
	/// `RoboDeForma.ConferirRoteiro`), e ele existe justamente pra poeira levantada pelo impacto
	/// baixar. Poeira ali nao e poeira solta, e a mesma cratera acabando.
	/// ==============================================================================
	/// </summary>
	Poeira = 8,
	/// <summary>Raios espalhados pelo cenario. `createLightningmisc`.</summary>
	Raios = 16,
	/// <summary>
	/// LIGA O PISCAR DE CABELO -- e ele e um ESTADO, e nao um pulso.
	///
	/// ============================ ERA UM PULSO, E O PULSO ACABAVA NO COMECO ============================
	/// Isto valia um beat = uma troca, e a cena do SSJ1 gastava quatro beats (0,7 / 1,3 / 2,0 / 2,9 s)
	/// pra dar quatro trocas. Funcionava enquanto a cena tinha 8,5 s; com os 25,0 s do DM de volta, o
	/// dono viu o defeito inteiro: *"a do ssj1 o cabelo base e do ssj ficam trocando (oq e legal) mas e
	/// so no inicio da cinematica, teria q durar a cinematica toda"*.
	///
	/// Encher o resto da cena de mais `PiscaCabelo` seria a saida barata e a errada: o numero de beats
	/// passaria a definir o RITMO do piscar, e ele teria que ser reescrito a mao toda vez que o relogio
	/// de uma cena mudasse -- que neste projeto ja aconteceu tres vezes. Pior na ENCURTADA, onde o
	/// `Encurtar` multiplica os instantes por `k`: a mesma lista de beats piscaria 2,5x mais rapido.
	///
	/// Entao o beat ARMA e o tocador conta o tempo. Um beat so por cena, e o piscar dura dela ate o
	/// <see cref="Assumir"/> -- nas DUAS versoes, sem conta nenhuma a mais, porque quem o desliga e um
	/// beat que o `Encurtar` tambem comprime.
	///
	/// ============================ E A CADENCIA CONTINUA SENDO A DO DM ============================
	/// `SSJCinematic.dm:11-26` troca o cabelo com `sleep(rand(3,10))` entre as trocas -- irregular de
	/// proposito, como uma lampada falhando. Isso virou
	/// <see cref="Cinematicas.PiscadaMinima"/>/<see cref="Cinematicas.PiscadaMaxima"/>, que sao aqueles
	/// mesmos 3 e 10 tiques divididos por 12. O que mudou nao foi o ritmo, foi ate onde ele vai.
	///
	/// ============================ ELE CEDE A ESCADA ============================
	/// Ver <see cref="VesteDegrau"/>: os dois escrevem cabelo, e onde os dois existissem o piscar
	/// apagaria o degrau duas vezes por segundo. Quem cede e o piscar.
	/// ======================================================================
	/// </summary>
	PiscaCabelo = 32,
	/// <summary>
	/// A CHAMA DA CENA ACENDE -- o jorro de energia que envolve o corpo, e ele fica ate o fim.
	///
	/// Sprite, e nao shader: ela tem forma desenhada, e o proprio dono autorizou ("alguns vc pode
	/// usar o sprite"). Ver <see cref="Jandirus.Client.Transformacao"/> sobre o alinhamento.
	///
	/// ============================ ELA E A AURA DA PROPRIA FORMA AGORA ============================
	/// Isto era o `Aurabigcombined.dmi` do DM -- uma arte SEPARADA, de cor propria, que so a
	/// cinematica usava. O dono trocou: *"vamos trocar das cinematicas o Aurabigcombined pela propria
	/// aura da transformaçao q vc ta virando, entao se eu for virar ssj pela primeira eu vou usar a o
	/// proprio icone da carga dele na cinematica"*.
	///
	/// Consequencia direta pro roteiro: numa cena que veste degraus (<see cref="VesteDegrau"/>) a
	/// chama ACOMPANHA o degrau vestido, porque quem a escreve e o vestidor. Ela nao e mais um efeito
	/// avulso do beat -- e o mesmo personagem que esta na tela, visto de longe.
	/// ========================================================================================
	/// </summary>
	AuraGrande = 64,
	/// <summary>Feixes de energia saindo pelo chao nas 8 direcoes (`Electricgroundbeam.dmi`).</summary>
	FeixesNoChao = 128,
	// ============================ 256 APOSENTADO: `PedrasSubindo` ============================
	// Era o beat que soltava uma leva de `Rising Rocks.dmi`. Morreu porque a pedra deixou de ser
	// ACONTECIMENTO e virou ESTADO -- ver <see cref="Cinematica.OChaoSeSolta"/>: o chao fica solto
	// pela cena inteira, e quem conta o tempo e o tocador, exatamente como o
	// <see cref="PiscaCabelo"/> ja fazia com o cabelo.
	//
	// O QUE O DONO PEDIU: *"deveria ter mais `rising rocks.png` q ficariam do INICIO AO FIM em todas
	// as transformacoes"*. Com o bit, "do inicio ao fim" seria escrever um beat de pedra a cada dois
	// segundos em trinta e duas cenas -- e a versao ENCURTADA multiplicaria os instantes por `k` e
	// mudaria a densidade sozinha. Sem o bit nao ha o que escrever nem o que esquecer.
	//
	// E O DM CONCORDA: as pedras de la nao vem de um instante do roteiro, vem de uma varredura do
	// `view()` com `spawn(rand(10,150))` (1,0 a 15,0 s de atraso) e `spawn(rand(100,400))` de vida
	// (10 a 40 s) -- `SSJCinematic.dm:31` e `dusts.dm:203-208`. Um fundo, e nao um pulso.
	//
	// O bit fica RESERVADO, como o 2: reusar um numero aposentado faz uma cena antiga escrita com o
	// valor velho acender um efeito que ela nunca pediu.
	// ==========================================================================================
	/// <summary>
	/// A VIRADA DA CENA -- e numa transformacao a virada e o cabelo assumindo a forma DE VEZ, com a
	/// aura acendendo.
	///
	/// ============================ POR QUE O NOME DIZ MENOS DO QUE ELE FAZ ============================
	/// Este bit sempre teve dois papeis, e so um deles estava escrito aqui. O outro e estrutural: e
	/// ele que o funil da <see cref="Cinematica.Beats"/> procura pra por a cratera e a poeira, que o
	/// <see cref="Cinematicas.Encurtar"/> usa pra achar o `k` da versao curta, e que a bancada conta
	/// pra cobrar "um por cena". Ou seja, ele ja era **o instante em que a cena entrega o que veio
	/// entregar** -- assumir a forma so e o que isso quer dizer numa cena que tem forma.
	///
	/// A <see cref="Cinematicas.Furia"/> e a primeira que nao tem: la a virada e a erupcao (a cratera
	/// e o `powerup.wav` de `Murder.dm:158-160`), e o tocador simplesmente nao veste ninguem. Ver o
	/// `Assumir()` do <see cref="Jandirus.Client.Transformacao"/>, onde a ausencia de forma e uma
	/// pergunta e nao um caso especial.
	///
	/// A ALTERNATIVA ERA UM SEGUNDO BIT (`Climax`) valendo a mesma coisa pra cenas sem forma -- e ele
	/// duplicaria as tres regras acima, que teriam que aprender a olhar pros dois. Um bit que o funil
	/// esquecesse deixaria uma cena sem cratera, calada; e o funil existe justamente porque essa
	/// familia de esquecimento ja custou doze cenas erradas uma vez.
	/// =========================================================================================
	/// </summary>
	Assumir = 512,

	/// <summary>
	/// VESTE O PROXIMO DEGRAU DA ESCADA -- e SO veste.
	///
	/// ============================ POR QUE ELE EXISTE ============================
	/// `SSJ3Cinematic.dm:12-30` nao e uma transformacao, sao TRES: `RemoveHair()` +
	/// `updateOverlay(hair)` na fala do "estado normal" (:12-16), `updateOverlay(ssj/ssj1)` com
	/// `ssj = 1` na fala "This is a Super Saiyan" (:18-20), e `updateOverlay(ssj/ssj2)` com
	/// `ssj = 2` na fala do "ascendeu alem de um Super Saiyajin" (:26-28). O texto da cena NARRA
	/// uma escada, e sem isto ele narrava uma escada que a tela nao mostrava -- o dono: *"ele faz
	/// os efeitos ele fala no chat mas o cabelo n muda nem nada"*.
	///
	/// ============================ E POR QUE E UM EFEITO DE CENA, E NAO UM CASO DO SSJ3 ============================
	/// Ele nao carrega QUAL degrau: quem responde isso e a <see cref="Cinematicas.EscadaDaCena"/>,
	/// derivada do catalogo. O n-esimo `VesteDegrau` de uma cena veste o n-esimo degrau abaixo da
	/// forma dela, comecando na base. Escrever o id da forma no beat faria a cena do SSJ3 saber os
	/// nomes `ssj1`/`ssj2` -- e um degrau novo inserido no meio da linha (que e o caso que a
	/// <see cref="FormaDef.Ordem"/> existe pra baratear) deixaria a cena mentindo, calada.
	///
	/// ============================ O QUE ELE NAO FAZ ============================
	/// NAO concede forma, NAO desperta degrau e NAO marca maestria. E cinematica, nao progressao --
	/// e nao ha risco de virar: quem toca a cena e o CLIENTE (<see cref="Jandirus.Client.Transformacao"/>),
	/// que nao tem por onde conceder nada. O `ssj = 1` do DM e desta natureza tambem: la a var e
	/// reescrita pelo `SSj()` no fim da cena.
	/// ==========================================================================================
	/// </summary>
	VesteDegrau = 2048,

	/// <summary>
	/// O ANEL DE CHOQUE QUE SE ABRE NO CHAO -- e ele e o `createShockwavemisc` DE VOLTA.
	///
	/// ============================ NAO E O <see cref="Onda"/> RESSUSCITADO ============================
	/// O bit 2 continua aposentado e continua reservado (ver la). O que morreu naquele dia foi o
	/// DESENHO, e nao o acontecimento: eu tinha portado a onda como um `ColorRect` de 320x320 com o
	/// `Transformacao.gdshader` por cima do corpo -- uma espiral escura que, num personagem de 32 px,
	/// le como o sujeito sumindo atras de um borrao.
	///
	/// O acontecimento e do DM e esta espalhado por ele: `GodRitual.dm:62-69` solta QUATRO
	/// `createShockwavemisc(loc,1)` seguidos, `Mystic.dm:142` solta um de raio 3 no BeastUp, e os
	/// quatro procs de surto (`SSj4FP` e irmaos) abrem com `createShockwavemisc(loc,3)` e fecham com
	/// um de raio 2. Tirar a onda da cena por causa da arte foi jogar fora o efeito junto com o
	/// sprite.
	///
	/// ============================ E O DESENHO CERTO JA EXISTIA NO JOGO ============================
	/// <see cref="Jandirus.Client.CombatFx.Onda"/>: um anel PROCEDURAL (`Impacto.gdshader`) que abre
	/// de 32 a 512 px sem serrilhar, e que o combate ja usa em todo impacto. Ele nao tapa ninguem --
	/// e uma linha fina que corre pelo chao --, e por isso ele resolve exatamente a queixa que
	/// aposentou o outro. Fazer um terceiro anel pra cinematica seria pagar duas vezes pelo mesmo
	/// desenho.
	/// ============================================================================================
	/// </summary>
	AnelDeChoque = 4096,

	// ============================ 8192 APOSENTADO: `Cascalho` ============================
	// Era o chao se quebrando em volta dos pes, desenhado pela `Jandirus.Client.PoeiraDeEstrago` --
	// o MESMO sistema que cospe escombro quando uma parede cai. Ele era invencao minha (nao ha
	// `createDustmisc` de pedra quebrada em cinematica nenhuma do DM) e o dono mandou tirar:
	//
	//   *"vc colocou uns efeitos de particula nas cinematicas q parecem q tem uns quadrados marrons
	//   caindo e criando uma fumaca parecendo q quebrou uma parede ou objeto, TIRE esse efeito"*.
	//
	// A DESCRICAO DELE E LITERAL, e por isso nao houve o que investigar: a `PoeiraDeEstrago` desenha
	// o pedaco com `Image.CreateEmpty(3,3)` preenchido de cor CHAPADA (um quadrado de 3 px, mesmo) e
	// `Gravity = (0, 420)` (ele cai), na cor `TerraPadrao` = 0,46/0,36/0,26 (marrom), com dois
	// sistemas de fumaca no mesmo node. Quadrado marrom caindo e criando fumaca de parede quebrada.
	//
	// ============================ O QUE **NAO** FOI MEXIDO ============================
	// A `PoeiraDeEstrago` continua inteira: ela tem dono proprio (o cenario sendo derrubado, em
	// combate) e o defeito nunca foi dela -- foi a cinematica ter chamado o sistema de ESTRAGO pra
	// fazer enfeite. O conserto e parar de chamar.
	//
	// E o buraco que ele preenchia (as cenas longas com onze segundos de tela parada) passou a ser
	// preenchido pelo que o DM usa pra isso: a pedra do chao, agora do inicio ao fim -- ver
	// <see cref="Cinematica.OChaoSeSolta"/>.
	//
	// Bit RESERVADO pelo mesmo motivo do 256 e do 2.
	// ================================================================================

	/// <summary>
	/// O CLARAO QUE LAVA A TELA INTEIRA.
	///
	/// ============================ NOVO, E COM UMA REGRA DE UM SO ============================
	/// Nao ha nada disto no DM -- o `Quake()` sacode e o `animate(color=...)` pinta o CORPO, nunca a
	/// tela. Ele existe pelo problema que so aparece numa cena de dois minutos: o instante em que a
	/// forma FICA precisa se distinguir dos vinte tremores que vieram antes, e por acumulo isso nao
	/// se consegue (o vigesimo primeiro tremor le como o vigesimo).
	///
	/// **E ele so pode aparecer no beat que <see cref="Assumir"/>.** A regra e minha e e por
	/// seguranca, nao por gosto: dois clarões numa cena viram um estrobo, e a versao ENCURTADA
	/// comprime a mesma cena em 10 s -- o que numa cheia de 116 s seria "de vez em quando" viraria
	/// piscar. Com um por cena, a encurtada tambem tem exatamente um. A bancada confere.
	///
	/// A FORCA CAI COM A DISTANCIA pela mesma conta do tremor (`Transformacao.PesoDoTremor`): quem
	/// esta do outro lado do planeta ve um lampejo, e nao o mundo ficando branco.
	/// ====================================================================================
	/// </summary>
	ClaraoDeTela = 16384,

	/// <summary>
	/// UM RAIO CAI DO CEU EM CIMA DE QUEM ESTA VIRANDO -- com o clarao no ceu e o trovao atrasado.
	///
	/// ============================ NOVO NA CENA, MAS A ENGINE E A DO CLIMA ============================
	/// O DM tem `createLightningmisc`, que e a faisca de CENARIO (aqui, <see cref="Raios"/>) --
	/// eletricidade rasteira, no chao, em volta. Descarga vindo de cima nao existe la.
	///
	/// Aqui ela existe inteira e ha meses: <see cref="Jandirus.Client.Iluminacao.Raio"/> ->
	/// `ClimaNaTela.Estourar`, que ja desenha o risco em zigue-zague com semente, acende o ceu com
	/// intensidade que cai com a distancia, e agenda o TROVAO pelo tempo que o som leva pra chegar.
	/// E o canal que o servidor usa na tempestade. Reusa-lo custa uma chamada.
	///
	/// SO EM PLANETA, e o tocador ja sabe disso (`_noPlaneta`): nao ha ceu pra rachar no espaco nem
	/// dentro da Sala do Tempo. E so nas cenas em que a narracao do proprio DM ja falava do mundo
	/// reagindo -- *"esta causando terremotos por toda parte"*, *"o proprio oceano se afasta"*.
	/// ============================================================================================
	/// </summary>
	DescargaNoCeu = 32768,

	/// <summary>
	/// O CORPO INTEIRO SE BANHA NA COR DA FORMA -- e este e o `animate(src, color=rgb(...))` do DM.
	///
	/// ============================ NAO E INVENCAO: SAO CINCO PROCS, LITERAIS ============================
	/// Era o unico gesto do original que este port nao tinha canal nenhum pra desenhar, e ele esta em
	/// toda parte:
	///
	///   * `LSSj3_Primal()` (`supersaiyanbuff.dm:549-550`): `animate(src, time=6, color=rgb(46,245,72))`
	///     e `spawn(12) color=null` -- 0,6 s pra encher de verde, 1,2 s pra voltar. **E a implementacao
	///     de referencia**, a que funciona.
	///   * `SSj4FP()` (`:809-815`): o mesmo, em `rgb(255,200,40)` -- dourado.
	///   * `SSj4FPLB()` (`:833-839`): o mesmo, em `rgb(255,40,40)` -- carmesim.
	///   * `do_first_godki_appearance()` (`buffs.dm:59-66`): `animate(color=rgb(226,243,253), time=6)`
	///     -- o clarao branco-azulado com que o ki divino chega.
	///   * `LSSj()` (`lssjbuff.dm:439-440`) e `Ritual_God/Buff()` (`GodRitual.dm:91-92`): os mesmos
	///     dois `animate`, seguidos de `color = null` **na linha de baixo, sem sleep no meio** -- o
	///     tween morre no mesmo tique e no BYOND nao aparece nada.
	///
	/// Os dois ultimos sao o caso que o dono ja julgou uma vez, e no mesmo sentido: *"o dono esta
	/// pedindo a INTENCAO, nao a reproducao"*. Um `animate` que a linha seguinte anula e codigo escrito
	/// pra fazer alguma coisa; portar o silencio seria portar o defeito.
	///
	/// ============================ E A COR E DERIVADA, NAO UM CAMPO NOVO ============================
	/// Ela sai do `Aura.CorDaChamaDe(d)` -- ou seja, do <see cref="FormaDef.Aura"/> que o catalogo ja
	/// declara --, pela mesma razao que a <see cref="AuraGrande"/> deixou de ter arte propria: no DM a
	/// cor com que o corpo se lava e a cor do poder que esta chegando, e isso ja esta escrito.
	///
	/// E a derivacao CONFERE com o arquivo, o que e o teste dela:
	///
	///   | forma                  | `animate` do DM | `FormaDef.Aura` |
	///   |------------------------|-----------------|-----------------|
	///   | `primal_legendary3`    | `2ef548`        | `2bff3a`        |
	///   | `legendary`            | `2ef548`        | `2bff3a`        |
	///   | `ssj4_full_power`      | `ffc828`        | `ffe14d`        |
	///   | `ssj4_limit_breaker`   | `ff2828`        | `ff2d2f`        |
	///
	/// Quatro pares que ninguem escreveu pra bater. Um campo `CorDoBanho` daria os hexas exatos e
	/// cobraria a manutencao de uma quinta cor por forma -- e a primeira forma nova nasceria com ele
	/// vazio e o banho invisivel, calada.
	///
	/// ============================ E O CLIENTE JA SABIA DESENHAR ============================
	/// O `Personagem.gdshader` MISTURA o corpo em direcao a `flash_cor` pelo uniform `flash`, em todas
	/// as camadas ao mesmo tempo -- e o canal por onde o soco lava o boneco (`CharacterVisual.Impacto`).
	/// O banho e ele com outro relogio e sem o achatamento/empurrao do golpe; ver
	/// <see cref="Jandirus.Client.CharacterVisual.Banhar"/>. Nao ha shader novo, nao ha node novo.
	///
	/// ============================ UM POR CENA ============================
	/// Mesma regra do <see cref="ClaraoDeTela"/> e pelo mesmo motivo, so que mais frouxa: o banho nao
	/// precisa cair no beat que ASSUME (nas quatro procs de surto ele cai na LARGADA, junto do grito, e
	/// a forma so vem 0,8 s depois), mas dois numa cena viram pisca-pisca -- e na encurtada, que
	/// comprime tudo pelo mesmo `k`, viram estrobo. A bancada conta.
	/// ==============================================================
	/// </summary>
	BanhoDeCor = 65536,

	/// <summary>
	/// A AURA BASE DO PROPRIO CORPO (`colorablebigaura`) ACENDE -- e fica acesa ate o
	/// <see cref="Assumir"/>, que a apaga.
	///
	/// ============================ CONTINUA NAO SENDO A `AuraGrande`, E AGORA PELO DONO ============================
	/// *"ativar a aura base dele (NAO E O AURA BIG COMBINED) por um tempo, enquanto tem camera
	/// shake, dps de alguns segundos ele vira o oozaru"*. Quando essa frase foi dita eram duas ARTES
	/// diferentes, e a distincao se explicava sozinha. Depois que a <see cref="AuraGrande"/> passou a
	/// usar a aura da propria forma, as duas desenham a MESMA folha -- e a diferenca virou de DONO:
	///
	///   * <see cref="AuraGrande"/>: a chama DA CENA. Nasce e morre com ela, tem pivo proprio (cresce)
	///     e nao ilumina nada.
	///   * esta: a chama DO CORPO, o node `Aura` EMPRESTADO. Tem a `PointLight2D`, sobrevive ao beat e
	///     tem que ser devolvida (o <see cref="Assumir"/> a apaga).
	///
	/// Ou seja: continuam sendo duas coisas, e continua sendo errado trocar uma pela outra.
	///
	/// ============================ E ELA PASSA POR CIMA DO PORTAO DA CARGA ============================
	/// A regra do jogo e "aura so acende com C ou com Ki acima de 100%" (ver `Aura.Preparar`), e o
	/// `canPower` do DM ainda peneira quem tem controle de Ki suficiente. O dono abriu excecao pra
	/// esta cena, tambem textual: *"a aura do personagem vai ativar (mesmo sem controle bom de ki)
	/// so pra cinematica"*. Faz sentido pelo que a cena CONTA: quem vira Oozaru nao esta reunindo
	/// energia de proposito, a lua esta arrancando ela dele.
	///
	/// QUEM ACENDE E O NODE `Aura`, e nao a `CargaVisual` -- ver o comentario no tocador
	/// (`Jandirus.Client.Transformacao`), que e onde a escolha se justifica.
	/// =============================================================================================
	/// </summary>
	AuraBase = 1024,
}

/// <summary>
/// UM INSTANTE DA CINEMATICA: em que segundo, o que acontece e o que se ouve/le.
/// </summary>
/// <param name="Em">Segundos desde o inicio.</param>
/// <param name="Faz">Efeitos disparados neste instante.</param>
/// <param name="Fala">O que o personagem diz. Vazio = nao fala.</param>
/// <param name="Narra">Descricao em terceira pessoa (o `*[src] ...*` do DM). Vazio = nada.</param>
/// <param name="Som">Efeito sonoro. Vazio = nenhum.</param>
public readonly record struct Beat(double Em, Efeito Faz, string Fala = "", string Narra = "", string Som = "");

/// <summary>
/// QUANTA CENA ESTA TRANSFORMACAO MERECE. Sao TRES degraus, e eles sao do dono:
///
/// *"no byond a primeira cinematica e a mais longa e tem as musicas, porem ate vc ter 50% de
/// maestria de uma forma, ela ainda vai ter um tempo pra se transformar, menor q a primeira
/// cinematica mas ainda assim vai ser lenda (o byond mostra bem isso), e apartir de 50% de
/// maestria a transformaçao vira instantanea (e isso serve pra TODAS as formas do jogo, menos as
/// oozaru e golden oozaru)"*.
///
/// ============================ POR QUE UM ENUM E NAO O `bool primeira` ============================
/// Ate aqui o pacote `S2C.Forma` carregava UM bit e o cliente lia dele duas coisas: "roda a cena
/// cheia" e "nao roda nada". O terceiro estado nao cabe num bit, e a saida barata -- mandar um
/// segundo bool ao lado -- criaria a combinacao invalida (`primeira && instantanea`) que alguem
/// teria que se lembrar de nunca escrever.
///
/// Com tres valores nomeados a combinacao invalida nao existe, e o `switch` do cliente reprova em
/// compilacao no dia em que nascer um quarto degrau.
/// =============================================================================================
/// </summary>
public enum DegrauDeCena : byte
{
	/// <summary>INSTANTANEA: nem cena, nem corpo preso. Maestria >= 50%.</summary>
	Nenhuma = 0,

	/// <summary>ENCURTADA: os mesmos efeitos, o relogio comprimido e SEM musica nem falas.</summary>
	Curta = 1,

	/// <summary>A CENA CHEIA, com a musica que toca uma vez na vida do personagem.</summary>
	Estreia = 2,
}

/// <summary>
/// O ROTEIRO DA PRIMEIRA VEZ DE UMA FORMA.
///
/// ============================ POR QUE ISTO E DADO, E NAO CODIGO ============================
/// No BYOND cada cinematica e um `proc` proprio (`SSJCinematic.dm`, `SSJ2Cinematic.dm`,
/// `SSJ3Cinematic.dm`, `UltraSSJCinematic.dm`...), com os `sleep()` intercalados no meio dos
/// efeitos. Funciona e tem um custo: **acrescentar uma forma significa escrever outro proc**, e
/// mexer no ritmo de uma cena significa recontar sleeps a mao.
///
/// Aqui a cena e uma lista de <see cref="Beat"/> com o segundo de cada um. O tocador e um so, os
/// tempos sao lidos, e a cena de uma forma nova e uma lista -- do mesmo jeito que a forma em si
/// virou uma entrada do <see cref="Catalogo"/>.
/// =====================================================================================
///
/// OS TEMPOS SAO OS DO DM, convertidos: `sleep()` conta DECISSEGUNDOS, entao `sleep(50)` sao
/// 50/10 = 5,0 s. A unidade esta explicada e provada num lugar so -- <see cref="TempoDoDm"/>.
/// </summary>
public sealed class Cinematica
{
	/// <summary>Id da forma a que esta cena pertence.</summary>
	public required string Forma;

	/// <summary>
	/// O TEMA QUE TOCA. Caminho relativo a `res://Assets/Sounds/Music/`.
	///
	/// **So na PRIMEIRA vez**, e isso e do original: `ssj1_music_played`, `ssj2_music_played`,
	/// `ssj4_music_played` sao vars que PERSISTEM no save. O tema de virar Super Saiyajin toca uma
	/// vez na vida do personagem, e e o que o torna um acontecimento em vez de um efeito sonoro.
	/// </summary>
	public string Musica = "";

	/// <summary>
	/// QUANTOS SEGUNDOS O CORPO FICA PRESO (`move = 0`).
	///
	/// Menor que a cena inteira de proposito -- no DM o `move = 1` volta no meio do
	/// `SSJCinematic()`, antes dos ultimos sleeps. Deixar preso ate o fim seria vinte segundos sem
	/// poder reagir num jogo com outras pessoas em volta.
	/// </summary>
	public double SegundosPreso;

	/// <summary>Duracao total da cena, em segundos.</summary>
	public double Segundos => _beats.Length == 0 ? 0 : _beats[^1].Em + 1.0;

	/// <summary>
	/// O CHAO FICA SOLTO DURANTE ESTA CENA? -- a pedra do `Rising Rocks.dmi`, do primeiro segundo ao
	/// ultimo.
	///
	/// ============================ O PEDIDO, E POR QUE ELE VIROU UM CAMPO DERIVADO ============================
	/// *"deveria ter mais `rising rocks.png` q ficariam do INICIO AO FIM em todas as transformacoes"*
	/// e *"aumente a area q o jogo pode spawnar esse efeito de rising rock, pq ta mt perto do
	/// personagem e dura mt pouco"*.
	///
	/// Medido antes de mexer: das 32 cenas, ONZE nao levantavam uma pedra sequer, e a melhor de todas
	/// (`wrathful`) tinha pedra em 46,5% do tempo -- a do SSJ1, em 13,1%. Nenhuma chegava perto de
	/// "do inicio ao fim", porque a pedra era um bit de beat (`Efeito.PedrasSubindo`, hoje
	/// aposentado) e beat e instante.
	///
	/// ============================ E POR QUE NAO E UMA LISTA DE CENAS ============================
	/// A UNICA excecao e do dono e ele foi explicito: *"oozaru n tem esse efeito de rocks nem de
	/// particulas, o resto da cinematica do oozaru pode deixar"*. Escrever `Forma != "oozaru"` aqui
	/// daria uma lista de isentos de um elemento -- e uma lista de isentos e a forma de defeito que o
	/// funil da <see cref="Beats"/> existe pra impedir (o `oozaru_dourado` divide esta MESMA cena
	/// hoje, mas uma cena propria pra ele nasceria fora da lista, calada).
	///
	/// Entao a pergunta e feita ao CATALOGO, e a resposta ja existia: <see cref="Catalogo.NaoSeSobePraEla"/>
	/// e a linha do Oozaru inteira -- *"nao se sobe pra ele, ele acontece POR OLHAR A LUA"*. E e essa
	/// a diferenca fisica tambem: nas outras trinta e uma o chao se solta porque o poder esta sendo
	/// EMPURRADO pra fora de um corpo parado; no macaco o que acontece e o corpo crescendo.
	///
	/// FORMA DESCONHECIDA levanta pedra (`Def` nulo -> `NaoSeSobePraEla` falso). E o lado certo do
	/// erro: cena nova sem entrada no catalogo nasce com o efeito, e nao muda.
	/// ======================================================================================================
	/// </summary>
	public bool OChaoSeSolta => !Catalogo.NaoSeSobePraEla(Catalogo.Def(Forma));

	/// <summary>
	/// O CEU DESCARREGA DURANTE ESTA CENA? -- raio caindo do alto, na regiao, do primeiro segundo ao
	/// ultimo. Verdadeiro numa unica cena do jogo: a ESTREIA do Super Saiyajin.
	///
	/// ============================ O PEDIDO ============================
	/// *"o ssj1 na cinematica da primeira vez, deveria fazer raios cairem durante TODA a cinematica na
	/// regiao q o personagem esta se transformando"*.
	///
	/// Sao tres recortes numa frase e os tres estao nesta linha: **ssj1**, **primeira vez** e **toda a
	/// cinematica**. O terceiro e o motivo de isto ser uma propriedade e nao um bit de beat -- a mesma
	/// licao que ja custou o piscar de cabelo (<see cref="Efeito.PiscaCabelo"/>) e a pedra
	/// (<see cref="OChaoSeSolta"/>): beat e INSTANTE, e "durante toda a cena" escrito em beats seria
	/// uma fileira de dezessete instantes que a <see cref="Cinematicas.Encurtar"/> multiplicaria por
	/// `k`, mudando a cadencia sozinha entre as duas versoes da MESMA cena.
	///
	/// ============================ E "PRIMEIRA VEZ" NAO PRECISOU DE CAMPO NOVO ============================
	/// <see cref="Musica"/> JA e o marcador da estreia, e por construcao: o tema so existe na cena
	/// cheia (*"toca uma vez na vida do personagem"*, ver o campo) e a <see cref="Cinematicas.Encurtar"/>
	/// o apaga na primeira coisa que faz. Uma bandeira `Estreia` ao lado dele seria a MESMA informacao
	/// escrita duas vezes, e o dia em que as duas discordassem daria a cena do dono com trilha e sem
	/// raio -- ou o contrario, que e pior porque ninguem repara.
	///
	/// ============================ POR QUE UM `==` DE ID AQUI E O <see cref="OChaoSeSolta"/> PERGUNTA AO CATALOGO ============================
	/// Sao pedidos de naturezas diferentes. La o dono falou de TODAS as formas e isentou UMA linha, e
	/// escrever a isencao daria uma lista de excecoes que uma cena nova driblaria calada. Aqui ele
	/// falou de UMA forma, nominalmente, e o efeito e sobre o acontecimento que aquela cena narra --
	/// nao ha regra a generalizar. O id vem do <see cref="Catalogo.IdSsj1"/>, que ja existe e ja e
	/// usado pelo resolvedor de cabelo, entao nao ha string solta pra digitar errado.
	///
	/// E ele NAO conflita com o corte anterior do dono (*"ssj n tem efeitos de raio"*, que tirou os
	/// tres beats de <see cref="Efeito.Raios"/> desta cena): aquilo e a FAISCA rasteira que corre pelo
	/// chao e pelo corpo -- `createLightningmisc`, um sprite por tile. Isto e a descarga vinda de cima,
	/// com clarao de ceu e trovao atrasado. Sao dois canais de desenho diferentes e o dono pediu um
	/// depois de cortar o outro. Ver <see cref="Efeito.DescargaNoCeu"/>, que e o mesmo desenho num pulso.
	/// ============================================================================================================
	/// </summary>
	public bool OCeuDescarrega => Forma == Catalogo.IdSsj1 && Musica.Length > 0;

	private Beat[] _beats = [];

	/// <summary>
	/// O ROTEIRO -- e ele passa pelo funil da cratera antes de virar cena. Ver
	/// <see cref="ACrateraECoisaDoInstanteDaTroca"/>.
	/// </summary>
	public Beat[] Beats { get => _beats; init => _beats = ACrateraECoisaDoInstanteDaTroca(value); }

	/// <summary>Os dois efeitos de chao que NENHUM roteiro decide. Ver o funil logo abaixo.</summary>
	private const Efeito Chao = Efeito.Cratera | Efeito.Poeira;

	/// <summary>
	/// A CRATERA CAI NO BEAT DA TROCA. SEMPRE, EM TODA CENA, POR CONSTRUCAO.
	///
	/// ============================ A REGRA, EM TRES LINHAS ============================
	///   * antes do beat que <see cref="Efeito.Assumir"/>: nem cratera, nem poeira;
	///   * NO beat que assume: cratera + poeira, escritas por este funil e nao pelo roteiro;
	///   * depois dele: a cratera sai de novo (ela e um instante, nao um estado) e a poeira fica --
	///     e a mesma poeira baixando, na cauda de assentamento que toda cena tem.
	///
	/// ============================ POR QUE UM FUNIL, E NAO DOZE CONSERTOS ============================
	/// O pedido do dono foi *"deveria ser sempre no final, assim q se transformar cria a cratera"*, e
	/// doze cenas o violavam. Mas as doze saiam de QUATRO linhas de codigo (dois ramos da
	/// <see cref="Cinematicas.EspinhaSaiyajin"/>, a <see cref="Cinematicas.Ssj1"/> escrita a mao e a
	/// <see cref="Cinematicas.Ssj3"/>) -- ou seja, o defeito nunca foi das cenas, foi de a cratera ser
	/// um campo LIVRE do beat. Consertar as quatro devolveria doze cenas certas e trinta e tres
	/// oportunidades de errar de novo: e este arquivo ja mostrou tres vezes o que acontece com uma
	/// regra que mora em comentario (o clarao, o piscar de cabelo, as pedras do Oozaru).
	///
	/// Com o funil a pergunta "em que beat vai a cratera?" deixa de existir. Nao ha resposta errada
	/// possivel porque nao ha mais pergunta -- e uma cena nova nasce certa sem que ninguem leia isto.
	///
	/// ============================ E ELE E O UNICO CAMINHO ============================
	/// Toda cena do jogo se constroi por `new Cinematica { ... Beats = [...] }` -- as escritas a mao,
	/// as seis fabricas, o <see cref="Cinematicas.Encurtar"/> e ate as cenas sinteticas da bancada.
	/// Nao ha como montar uma <see cref="Cinematica"/> sem passar por este `init`, e por isso a regra
	/// nao tem furo por onde uma cena escapar.
	///
	/// IDEMPOTENTE de proposito: apagar-e-repor da o mesmo resultado na segunda passada, que e o que
	/// permite o `Encurtar` reconstruir uma cena ja funilada sem acumular nada.
	///
	/// ============================ O QUE ELE NAO FAZ ============================
	/// NAO mexe em `Em`, NAO cria nem apaga beat, NAO toca em fala, narracao ou som. Os prazos desta
	/// classe sao os `sleep()` do DM recontados um a um e nao se mexe neles por causa de efeito.
	///
	/// SEM BEAT QUE ASSUME (cena defeituosa, que a bancada ja reprova por outro caminho) a cratera
	/// simplesmente nao entra e a poeira toda sai: uma cena sem instante de troca nao tem onde por o
	/// chao quebrando, e inventar um lugar esconderia o buraco.
	/// ==========================================================================================
	/// </summary>
	private static Beat[] ACrateraECoisaDoInstanteDaTroca(Beat[] roteiro)
	{
		var saida = new Beat[roteiro.Length];
		bool jaTrocou = false;
		for (int i = 0; i < roteiro.Length; i++)
		{
			Beat b = roteiro[i];
			Efeito faz = b.Faz;

			// O PRIMEIRO `Assumir`, e nao qualquer um: e a mesma escolha que o `Encurtar` faz pra
			// achar o `k`. Uma cena com dois deles (que a bancada reprova) poria duas crateras.
			if (!jaTrocou && faz.HasFlag(Efeito.Assumir)) { jaTrocou = true; faz |= Chao; }
			else if (!jaTrocou) faz &= ~Chao;             // antes da troca o chao esta inteiro
			else faz &= ~Efeito.Cratera;                  // depois so assenta o que ja caiu

			saida[i] = b with { Faz = faz };
		}
		return saida;
	}
}

/// <summary>
/// AS CINEMATICAS DE PRIMEIRA TRANSFORMACAO, portadas de `Code/Modules/cinematics/`.
///
/// ============================ O QUE FOI PRESERVADO E O QUE MUDOU ============================
/// **Preservado**: os tempos (convertidos de tique -- decissegundo -- pra segundo), as musicas (os mesmos
/// arquivos, e a regra de tocarem UMA vez na vida do personagem), as falas do SSJ3 (a cena do
/// "even further beyond", que e a mais escrita das tres) e a ordem dos efeitos.
///
/// **Mudou**: as falas foram pro portugues, como o resto do jogo. O original em ingles fica no
/// comentario de cada uma -- quem for conferir com o `SSJ3Cinematic.dm` acha a linha.
///
/// ============================ E O QUE FOI DESFEITO: A COMPRESSAO ============================
/// Este bloco dizia, aqui mesmo, que as cenas tinham sido encurtadas de proposito -- o SSJ1 de
/// ~21 s pra 12, o SSJ3 de "mais de 60" pra 26 --, com o argumento de que no BYOND a cinematica era
/// um evento de servidor que todo mundo assistia e neste port o mundo continua correndo.
///
/// O argumento era meu e o dono nunca o pediu; ele pediu o contrario (*"no dm ela durava minutos"*).
/// E a medida mostrou que ate a conta estava errada: o `SSJ3Cinematic.dm` tem VINTE `sleep` somando
/// 1400 tiques -- **140 s**, e nao "mais de 60". A cena do port tinha 35.
///
/// Agora **todo prazo desta classe e a soma dos `sleep` do proc correspondente** dividida por 10
/// (o tique do BYOND e um DECIMO de segundo), e cada beat cai no instante REAL do original em vez
/// de num esticamento proporcional: o ritmo do DM nao e uniforme (oito dos vinte `sleep` do SSJ3
/// passam de 100 tiques,
/// e sao os silencios entre os gritos), e esticar teria dado a duracao certa com a respiracao errada.
///
/// E O CORPO FICA PRESO A CENA INTEIRA, por decisao do dono: *"no dm e o tempo inteiro da
/// transformaçao parado"*. Onde o `move = 1` do DM cai antes do fim da proc (o SSJ1 solta aos 15,0 s
/// de 25,0) o prazo aqui e mesmo assim o beat que ASSUME -- soltar antes deixaria o jogador andando
/// enquanto a cena ainda conta que ele vai virar.
///
/// AS TRES CENAS SEM `sleep` DE ORIGEM estao marcadas uma a uma: a <see cref="Destroyer"/> (o DM
/// diz, textual, que ela nao tem cinematica), a <see cref="Oozaru"/> (desenho novo) e os
/// <see cref="SurtoCurto"/> (no DM sao um `sleep(8)` de 0,8 s, curto demais pra ler como cena).
/// =====================================================================================
/// </summary>
public static class Cinematicas
{
	// ==================================================================================
	// OS TEMAS QUE MAIS DE UMA FORMA COMPARTILHA
	// ==================================================================================
	// Sao CONSTANTES e nao literais repetidos porque no DM eles sao literalmente a MESMA var de
	// save: `ssj1_music_played` cobre o SSJ1 comum, o Future Lineage e o C-Type (Z) do Legendary
	// Primal (todos passam pelo mesmo `SSj()`, supersaiyanbuff.dm:313); `blue_music_played` cobre
	// Blue e Rose (`SSj()` com `godki.usage`, :309); `ssj4_music_played` cobre o SSJ4 e o
	// Legendary SSJ4 (`SSj4()`, :748). Um erro de digitacao num desses nomes de arquivo nao
	// derruba nada -- so deixa a cena muda, que e o defeito mais dificil de perceber que existe.
	private const string TemaSsj1 = "Dragon Ball Z Dokkan Battle TEQ LR SSJ Goku Revival OST (Extended).mp3";
	private const string TemaBlue = "Dragon Ball Z Dokkan Battle AGL LR Super Saiyan Blue Goku & Vegeta Intro OST (Extended).mp3";
	private const string TemaSsj4 = "Dragon Ball GT   Super Saiyan 4 Theme (Gladius & Akihito Tokunaga)   By Gladius.mp3";

	// ==================================================================================
	// SUPER SAIYAJIN -- `SSJCinematic.dm`
	// ==================================================================================
	/// <summary>
	/// A PRIMEIRA VEZ DO SSJ1.
	///
	/// No DM: `rockmoving.wav`, o cabelo PISCANDO entre o normal e o dourado quatro vezes
	/// (`sleep(rand(3,10))` entre cada troca), raios e poeira espalhados pelo cenario, `sleep(50)`,
	/// a aura grande, `sleep(100)`, dois `Quake()`, os oito feixes de chao, cratera, e o
	/// `chargeaura.wav` com a fala do cabelo se erguendo.
	///
	/// ============================ 25,0 s, E O RELOGIO E O DO DM ============================
	/// Esta cena prendia 8,5 s. O `SSJCinematic.dm` soma 250 tiques, e tique e DECISSEGUNDO -- **25,0 s** --, e a
	/// forma so fica quando a proc RETORNA: o `SSj()` chama `SSJCinematic()` (`supersaiyanbuff.dm:320`)
	/// e so depois escreve `ssj = 1`. Os 8,5 s eram compressao minha, que o dono nunca pediu.
	///
	/// O `move = 1` do DM cai antes (aos 15,0 s, `SSJCinematic.dm:84`) e mesmo assim o prazo aqui e a
	/// cena inteira: o dono decidiu *"no dm e o tempo inteiro da transformaçao parado"*, e prender ate
	/// os 15,0 deixaria o jogador andando por dez segundos enquanto a cena ainda conta que ele vai
	/// virar Super Saiyajin.
	/// =====================================================================================
	/// </summary>
	// ============================ A FAISCA NAO. O RAIO SIM -- E SAO DUAS COISAS ============================
	// O SSJ1 NAO SOLTA FAISCA. O dono: "ssj n tem efeitos de raio". O catalogo ja estava certo
	// (`Raios` do `ssj1` e 0); quem os acendia era esta CENA, em tres beats -- efeito escrito em
	// dois lugares, e o segundo contradizia o primeiro.
	//
	// A faisca comeca no SSJ2 e ACABA no SSJ3: numa segunda passada o dono cortou o resto da escada
	// e das linhas divinas, e sobrou uma unica forma fora dela (`primal_legendary2`). Ver
	// `FormaDef.Raios`.
	//
	// **E MESMO ASSIM O CEU DESCARREGA AQUI, e nao ha contradicao.** O dono, depois: *"o ssj1 na
	// cinematica da primeira vez, deveria fazer raios cairem durante TODA a cinematica na regiao q o
	// personagem esta se transformando"*. O que ele cortou foi a eletricidade RASTEIRA que corre pelo
	// corpo e pelo chao (`Efeito.Raios` / `FormaDef.Raios`); o que ele pediu foi a descarga vinda de
	// CIMA, com clarao de ceu e trovao. Sao dois desenhos diferentes e dois pedidos diferentes.
	//
	// E ela nao esta escrita em beat NENHUM desta lista, de proposito: e o estado
	// `Cinematica.OCeuDescarrega`, que so vale nesta cena e so na versao cheia. Ver la.
	// ====================================================================================================
	public static readonly Cinematica Ssj1 = new()
	{
		Forma = "ssj1",
		Musica = TemaSsj1,
		SegundosPreso = 25.0,
		Beats =
		[
			new(0.0, Efeito.Tremor, Som: "rockmoving",
				Narra: "o chao começa a tremer."),

			// ============================ O CABELO PISCANDO E A ASSINATURA DESTA CENA ============================
			// UM BEAT, e ele ARMA -- ver `Efeito.PiscaCabelo`. Aqui havia quatro (0,7 / 1,3 / 2,0 / 2,9 s),
			// um por troca, e era assim que o piscar acabava aos 2,9 s de uma cena de 25,0: o dono viu
			// exatamente isso (*"e so no inicio da cinematica"*). Os tres beats que sobravam foram
			// DELETADOS, e o `Poeira` do quarto desceu pra um beat proprio aos 2,9 s -- que morreu
			// depois, quando a poeira passou a ser da cratera (ver `Efeito.Poeira`): sem ela aquele
			// instante nao tinha mais nada dentro.
			//
			// Daqui ate o `Assumir` (25,0 s na estreia, 10,0 s na encurtada) o penteado alterna sozinho,
			// na cadencia do DM (`Cinematicas.PiscadaMinima`/`PiscadaMaxima`).
			// ================================================================================================
			new(0.7, Efeito.PiscaCabelo),

			// `sleep(50)` = 5,0 s -- a `Aurabigcombined.dmi` entra (`SSJCinematic.dm:57-60`).
			new(5.0, Efeito.AuraGrande | Efeito.Tremor, Som: "chargeaura",
				Narra: "uma onda de poder emana daqui."),

			// O PRIMEIRO ANEL, e ele e o `createShockwavemisc` que o DM espalha por toda cena de poder
			// (ver `Efeito.AnelDeChoque`). AQUI porque este era o unico trecho da cena em que a aura ja
			// estava acesa e NADA respondia a ela -- o chao tem que comecar a ceder antes de a cena
			// falar em cratera.
			new(7.4, Efeito.AnelDeChoque),

			// O VAZIO DO MEIO E DO DM E NAO DESCUIDO: entre a aura e o `sleep(100)` o original nao
			// agenda nada NOVO -- o que preenche esses oito segundos sao os `spawn(rand(10,150))` da
			// abertura (`:29-31`), que espalham raio e poeira ate os 15,0 s. Este beat e a cauda deles.
			//
			// SO A NARRACAO, hoje: a poeira que ele tinha foi pra cratera (ver `Efeito.Poeira`). O
			// instante fica porque ele e o que quebra os 7,5 s entre o anel e o proximo tremor -- e
			// porque narracao E conteudo de beat.
			//
			// A FALA MUDOU PORQUE ELA MENTIA: ela dizia *"faíscas correm pela terra em volta"*, e a
			// faisca rasteira o dono cortou desta cena (ver o bloco la em cima) -- o beat narrava um
			// efeito que nenhum desenho fazia mais. Agora ela conta o que a tela mostra de verdade do
			// primeiro segundo ao ultimo, que e a descarga caindo (`Cinematica.OCeuDescarrega`).
			new(10.0, Efeito.Nada,
				Narra: "o céu se parte sobre a região."),

			// SO CASCALHO E TREMOR: o acumulo continua e nao muda de patamar. Um segundo anel aqui
			// faria os tres tempos da cena (subir, estourar, ficar) virarem um so.
			new(12.5, Efeito.Tremor),

			// `sleep(100)` = 15,0 s: os dois `Quake()` e os oito feixes de chao (`:65-79`).
			new(15.0, Efeito.Tremor | Efeito.FeixesNoChao, Som: "powerup"),

			// `spawn(20) createCrater(loc,3)` (`:83`): AQUI o DM abre o chao, 2,0 s depois dos feixes e
			// oito segundos antes de a forma ficar. A CRATERA NAO ESTA MAIS NESTE BEAT -- ela e do
			// instante da troca por regra, e o beat guarda o `Quake()` que tambem cai aqui. O porque,
			// e por que nao foi conserto de cena, esta em `Efeito.Cratera`.
			//
			// `spawn(130)` (`:62`) -- a aura grande sai aos 18,0 s -- tinha um beat so pra poeira, e ele
			// morreu com ela. Daqui ate o fim o DM fica em SILENCIO, e o silencio e o que faz o ultimo
			// beat parecer o clímax em vez de mais um efeito.
			new(17.0, Efeito.Tremor),

			// ============================ OS 7,0 s MUDOS ANTES DO CLIMAX ============================
			// Este era o maior buraco da cena: entre a aura grande sair (18,0 s) e a forma ficar (25,0 s)
			// nao acontecia nada. O silencio do DM e o das FALAS e o do SOM -- os `spawn` dele continuam
			// cuspindo efeito ate o fim --, e sete segundos de tela parada nao leem como tensao,
			// leem como o jogo ter travado.
			//
			// Sao dois beats e eles SOBEM: primeiro o chao se solta (pedra, sem anel), depois o estouro
			// que anuncia. O `zumbido` e o laco de carga do proprio jogo (`aurapowered.wav`) -- ele enche
			// o intervalo com SOM em vez de mais um estalo.
			// ====================================================================================
			new(20.4, Efeito.Tremor),
			new(22.8, Efeito.AnelDeChoque | Efeito.Tremor, Som: "zumbido"),

			// O INSTANTE. No DM e quando a proc RETORNA (250 tiques) e o `SSj()` escreve `ssj = 1`.
			// O CLARAO SO AQUI, e e a regra de um-por-cena (ver `Efeito.ClaraoDeTela`): depois de vinte
			// tremores, mais um tremor nao distingue nada -- o instante da forma precisa de outra coisa.
			new(25.0, Efeito.Assumir | Efeito.Tremor | Efeito.AnelDeChoque | Efeito.ClaraoDeTela,
				Narra: "o cabelo se ergue e doura."),

			new(26.4, Efeito.Poeira),
		],
	};

	// ==================================================================================
	// SUPER SAIYAJIN 2 -- `SSJ2Cinematic.dm`
	// ==================================================================================
	/// <summary>
	/// A PRIMEIRA VEZ DO SSJ2. A mesma espinha do SSJ1, com TRES `Quake()` seguidos no lugar de um
	/// e os feixes de chao trocados (`Electricgroundbeam2.dmi`) -- e sem o piscar de cabelo, que e
	/// exclusivo da primeira ascensao.
	///
	/// ============================ 15,0 s, E NAO 8 ============================
	/// O `SSJ2Cinematic.dm` soma 150 tiques (`sleep(50)` + `sleep(100)`) e ACABA no proprio
	/// `move = 1` (`:46`) -- 15,0 s cravados. Diferente do SSJ1, que ainda tem um `sleep(100)` depois
	/// da soltura: e por isso que as duas cenas irmas nao tem o mesmo relogio, e nao por escolha de
	/// ritmo. Os 8 s anteriores eram compressao minha.
	/// ==================================================================
	/// </summary>
	public static readonly Cinematica Ssj2 = new()
	{
		Forma = "ssj2",
		Musica = "Dragon Ball Z   Day Of Fate Unmei No Hi (Hironobu Kageyama)   By Gladius.mp3",
		SegundosPreso = 15.0,
		Beats =
		[
			new(0.0, Efeito.Tremor | Efeito.Raios, Som: "rockmoving",
				Narra: "o ar fica pesado, e um estalo corre pelo chão."),

			// `spawn(rand(40,60))` (`SSJ2Cinematic.dm:14`): o anel de raios cai entre 3,3 e 5,0 s.
			new(1.8, Efeito.Raios),

			// `sleep(50)`.
			new(5.0, Efeito.AuraGrande | Efeito.Tremor, Som: "chargeaura",
				Narra: "a energia se junta em volta do corpo."),

			// O ANEL, no mesmo lugar da espinha do SSJ1: logo depois de a aura grande entrar. E o que da
			// ao vazio de 5 s uma consequencia -- a energia que acabou de se juntar EMPURRA alguma coisa.
			new(7.2, Efeito.AnelDeChoque),

			// A cauda dos `spawn(rand(10,150))` da abertura (`:11-13`), que vao ate os 15,0 s.
			new(10.0, Efeito.Raios),

			// A ULTIMA SUBIDA antes dos tres tremores colados. Pedra e cascalho e nao anel: o anel ja
			// gastou o susto la atras, e o que falta aqui e o chao ficando instavel.
			new(12.0, Efeito.Tremor),

			// OS TRES `Quake()` SEGUIDOS (`:26-28`). E o que separa a cena do SSJ2 da do SSJ1 no DM,
			// e no original os tres caem COLADOS no fim do `sleep(100)` -- o terceiro e o mesmo
			// instante em que a forma fica.
			new(14.3, Efeito.Tremor),
			new(14.6, Efeito.Tremor),

			new(15.0, Efeito.Assumir | Efeito.Tremor | Efeito.FeixesNoChao
					| Efeito.AnelDeChoque | Efeito.ClaraoDeTela, Som: "powerup",
				Narra: "faíscas percorrem a aura, e ela não se apaga mais."),

			// A CAUDA. No DM o `spawn(20) createCrater(loc,3)` (`:45`) cai aqui, 2,0 s DEPOIS da forma;
			// a cratera passou pro beat que assume (ver `Efeito.Cratera`) e o que sobra neste instante
			// e o assentamento: a faisca e a poeira que o impacto levantou baixando.
			new(17.0, Efeito.Raios | Efeito.Poeira),
		],
	};

	// ==================================================================================
	// SUPER SAIYAJIN 3 -- `SSJ3Cinematic.dm`, a cena mais escrita das tres
	// ==================================================================================
	/// <summary>
	/// A PRIMEIRA VEZ DO SSJ3 -- a cena do "even further beyond".
	///
	/// ============================ AS FALAS SAO AS DO DM ============================
	/// Traduzidas, com o original em ingles ao lado de cada uma. Elas nao sao enfeite: sao a UNICA
	/// cinematica do jogo em que o personagem EXPLICA o que esta fazendo, e e por isso que o SSJ3
	/// e a transformacao que as pessoas lembram.
	/// ===========================================================================
	/// </summary>
	public static readonly Cinematica Ssj3 = new()
	{
		Forma = "ssj3",
		Musica = "ssj3theme.ogg",
		// ============================ 140 s, E O `sleep` E LITERAL ============================
		// Isto ja foi 20, depois 32. Os dois numeros eram MEUS: o `SSJ3Cinematic.dm` tem VINTE
		// `sleep()` somando **1400 tiques**, e em DECISSEGUNDO isso da 140 s -- mais de
		// dois minutos. Eu tinha comprimido a cena em 70% por conta propria, e o dono nunca pediu
		// isso; ele pediu o contrario (*"no dm ela durava minutos"*).
		//
		// E ela prende O TEMPO TODO porque o DM tambem prende: a proc escreve `move = 0` (`:9`) e
		// NUNCA escreve `move = 1` -- quem solta e o `SSj3()` la em cima, depois de a proc retornar.
		// A forma tambem so fica ai (`supersaiyanbuff.dm:482` chama, `:519` escreve `ssj = 3`).
		//
		// OITO DOS VINTE `sleep` PASSAM DE 100 TIQUES, e e isso que da o ritmo desta cena: a primeira
		// metade e falada e apertada (0,8 s a 27,5 s), a segunda e sete gritos com oito a onze
		// segundos de silencio entre eles. Esticar proporcionalmente teria dado a mesma duracao com o
		// ritmo errado -- por isso cada beat abaixo cita o instante REAL do original.
		// =================================================================================
		SegundosPreso = 140.0,
		Beats =
		[
			// ============================ A ESCADA E MOSTRADA, E NAO SO CONTADA ============================
			// Os tres primeiros beats sao os tres `updateOverlay` do `SSJ3Cinematic.dm:12-30`: a cena
			// VESTE o degrau de que a fala esta falando. Sem isso o texto prometia base -> SSJ1 -> SSJ2
			// -> SSJ3 e a tela mostrava o mesmo cabelo o tempo todo ate o fim.
			//
			// Qual degrau cada um veste NAO esta escrito aqui -- ver `Efeito.VesteDegrau` e
			// `EscadaDaCena`. O primeiro veste a base, o segundo o SSJ1, o terceiro o SSJ2.
			// ==========================================================================================

			// "You're going to love this, trust me. What you're seeing now is my normal state."
			// `sleep(10)` -> `RemoveHair()` + `updateOverlay(/obj/overlay/hairs/hair)` -- :11-16.
			new(1.0, Efeito.VesteDegrau,
				Fala: "Você vai gostar disso, confie em mim. O que você está vendo agora é o meu estado normal."),

			// "This is a Super Saiyan."
			// `sleep(60)` -> `updateOverlay(/obj/overlay/hairs/ssj/ssj1)` + `ssj = 1` -- :17-24. E o
			// beat em que o `ssj3theme.ogg` entra no original (`emit_TransformMusic`, :24).
			new(7.0, Efeito.VesteDegrau | Efeito.Tremor, Som: "chargeaura",
				Fala: "Isto é um Super Saiyajin."),

			// "And this... this is what is known as a Super Saiyan that has ascended past a Super Saiyan."
			// `sleep(50)` -> `updateOverlay(/obj/overlay/hairs/ssj/ssj2)` + `ssj = 2` -- :25-32. E o
			// `Efeito.Raios` deste beat e o `overlayList += 'Elec.dmi'` da linha :29: a faisca nasce
			// junto com o SSJ2, aqui como na escada de verdade.
			new(12.0, Efeito.VesteDegrau | Efeito.Tremor | Efeito.Raios, Som: "chargeaura",
				Fala: "E isto... isto é o que se conhece como um Super Saiyajin que ascendeu além de um Super Saiyajin."),

			// "Or you can just call this a Super Saiyan 2." -- `sleep(20)`, :33-34.
			new(14.0, Efeito.Nada, Fala: "Ou você pode simplesmente chamar de Super Saiyajin 2."),

			// `sleep(50)` -> as falas de QUEM ASSISTE (:44-49): "Has [src] really done it!?", "[src]
			// must be bluffing". Aqui vira narracao porque a cena do port nao tem elenco de apoio --
			// o beat existe pelo INSTANTE, que e uma pausa de quatro segundos no meio do discurso.
			new(19.0, Efeito.Nada,
				Narra: "quem está por perto não consegue acreditar no que vê."),

			// "AND THIS..." + *[src] leans inward and pumps their fists next to their sides!!*
			// `sleep(40)`, :50-52.
			new(23.0, Efeito.Tremor,
				Fala: "E ISTO...",
				Narra: "se encolhe e fecha os punhos ao lado do corpo!"),

			// "IS TO GO..." -- `sleep(50)`, :56-57.
			new(28.0, Efeito.Tremor, Fala: "É PARA IR..."),

			// "EVEN FURTHER BEYOND" / *[src] leans forward!!* -- `sleep(20)`, :58-60.
			new(30.0, Efeito.Tremor,
				Fala: "AINDA MAIS ALÉM!",
				Narra: "se inclina para a frente!"),

			// ============================ DAQUI PRA BAIXO A CENA E SO O GRITO ============================
			// Sao SETE `AAAA!!!` no original, separados por `sleep` de 100 a 140 tiques -- oito a onze
			// segundos e meio de silencio entre cada um, com um `Quake()` no meio de quase todos. E a
			// metade da cena que eu tinha apagado inteira: dos 140 s do DM, 107 estao aqui.
			// ======================================================================================

			// `sleep(30)`: a aura sai, a onda de poder, o 1o grito, o tremor em todo mundo na tela e
			// a chuva de poeira/raio em 24 tiles (:61-72).
			new(33.0, Efeito.AuraGrande | Efeito.Tremor | Efeito.Raios,
				Som: "powerup",
				Fala: "AAAAAAAAAAAAAAAAA!!!",
				Narra: "grita, e uma quantidade inacreditável de energia se solta."),

			new(38.0, Efeito.Tremor),                   // `sleep(50)` -> `Quake()`
			new(43.0, Efeito.Tremor,                                     // `sleep(50)` -> 2o grito
				Fala: "AAAAAAAAAAAAAAAAA!!!"),

			// ============================ OS NOVE SILENCIOS, PREENCHIDOS UM A UM ============================
			// Daqui pra baixo a cena do DM sao sete gritos com oito a onze segundos e meio entre eles. Esse
			// espacamento e o ritmo da cena e nao pode sumir -- mas ele nunca foi tela PARADA: os
			// `spawn(rand(10,150))` do original ficam espalhando efeito por baixo dos `sleep` o tempo todo, e
			// a lista de beats nao tinha esse fundo. Um minuto e meio de nada e pior que cena curta.
			//
			// Entao cada silencio ganha UM beat no meio dele (o vao continua com ~4 s de folga dos dois
			// lados) e os nove SOBEM juntos, que e o unico jeito de noventa segundos nao virarem repeticao:
			//
			//   48,0  tremor                -- o chao so estremece
			//   58,0  anel                  -- o poder passa a empurrar o ar
			//   68,0  anel + tremor         -- os dois juntos
			//   79,4  anel + tremor         -- o vao mais longo da cena (10,9 s) e o que mais precisa
			//   91,0  tremor
			//  103,0  anel + zumbido        -- entra SOM continuo; daqui pro fim nao ha mais so estalo
			//  115,0  DESCARGA no ceu       -- a narracao ja dizia `terremotos por toda parte`
			//  125,0  tudo junto
			//  135,0  descarga + explosao   -- o ultimo degrau antes do clarao
			//
			// ============================ O DEGRAU DE BAIXO MUDOU DE DONO ============================
			// Esta escada comecava em CASCALHO ("o chao so range"), e o cascalho era a `PoeiraDeEstrago`
			// -- o efeito que o dono mandou tirar (ver o bit 8192 aposentado). O fundo que ele dava nao
			// se perdeu: ele virou a PEDRA, que agora nao e mais um beat e sim o estado do chao pela
			// cena inteira (ver `Cinematica.OChaoSeSolta`). E melhor assim -- o fundo continuo passou a
			// ser fundo de verdade, em vez de nove pontinhos espalhados por noventa segundos.
			//
			// Os dois instantes que ficariam VAZIOS com o cascalho fora (48,0 e 68,0) receberam o que o
			// DM tem neles: `Quake()`. `SSJ3Cinematic.dm:74-76` e `:87-93` -- os quakes de planeta
			// inteiro que separam o segundo grito do terceiro. Nao ha beat inventado aqui, e nenhum
			// prazo mudou.
			// ============================================================================================
			new(48.0, Efeito.Tremor),                                    // `sleep(50)` -> `Quake()`

			// `sleep(100)`: tremor no PLANETA inteiro e a troca pra `Aurabigcombined.dmi` (:78-89).
			new(53.0, Efeito.AuraGrande | Efeito.Tremor,
				Fala: "AAAAAAAAAAAAAAAAA!!!"),

			new(58.0, Efeito.AnelDeChoque),

			new(63.0, Efeito.Tremor),                                    // `sleep(100)` -> `Quake()`

			// O DEGRAU DE CIMA DO ANEL DE 58,0 -- ver a escada dos nove silencios la em cima. Era
			// cascalho; e o `Quake()` de `:87-93` mais o anel, que e o mesmo passo que o 79,4 repete.
			new(68.0, Efeito.AnelDeChoque | Efeito.Tremor),

			// `sleep(100)`: *[src]'s voice grows very hoarse!* + `SSj2GroundGrind()` (:94-99).
			//
			// AQUI HAVIA UMA CRATERA, sessenta e sete segundos antes de a forma ficar -- a mais errada
			// das doze que o dono pegou. O `SSj2GroundGrind()` do DM e o RANGER do chao (pedra subindo),
			// e nao o buraco; o buraco e do instante da troca. Ver `Efeito.Cratera`.
			new(73.0, Efeito.Tremor,
				Fala: "AAAAAAAAAAAAAAAAAAAA!!!!",
				Narra: "a voz fica muito rouca!"),

			// O VAO MAIS LONGO (10,9 s). Ele leva o beat mais cheio dos primeiros quatro justamente por
			// ser o maior -- e o unico em que um beat magro deixaria o buraco de pe.
			new(79.4, Efeito.AnelDeChoque | Efeito.Tremor),

			// `sleep(130)` -- o maior silencio da cena. Troca pra `ss3transformaurafinal.dmi` (:104-112).
			new(86.0, Efeito.AuraGrande | Efeito.Tremor,
				Fala: "AAAAAAAAAAAAAAAAAAHHHH!!!!",
				Narra: "está sacudindo o planeta inteiro!"),

			new(91.0, Efeito.Tremor),

			new(96.0, Efeito.Tremor),                                    // `sleep(100)`

			// O SOM ENTRA AQUI. Daqui pro fim a cena nao volta a ficar muda -- e o que separa a ultima
			// quarta parte das tres anteriores sem precisar de mais um efeito na tela.
			new(103.0, Efeito.AnelDeChoque | Efeito.Tremor, Som: "zumbido"),

			// `sleep(140)` -- o outro silencio longo (:120-125).
			new(110.0, Efeito.Tremor | Efeito.Raios,
				Fala: "AAAAAAAAAAAAAAAAAAAAHHHH!!!!",
				Narra: "está causando terremotos por toda parte!"),

			// A PRIMEIRA DESCARGA, e ela vem logo DEPOIS da fala que a autoriza: o beat de 91,7 s narra
			// *esta causando terremotos por toda parte*. O ceu reagindo e a unica escalada que sobrou --
			// tremor, pedra e anel ja foram usados tres vezes cada nesta cena.
			new(115.0, Efeito.DescargaNoCeu),

			new(120.0, Efeito.Tremor),                                   // `sleep(100)`

			new(125.0, Efeito.AnelDeChoque | Efeito.Tremor),

			// `sleep(100)`: *The ocean itself is curling away from [src]'s immense power!!!* (:131-135).
			new(130.0, Efeito.Tremor,
				Fala: "AAAAAAAAAAAAAAAAAAAAAAHHHH!!!!",
				Narra: "o próprio oceano se afasta deste poder!"),

			// O ULTIMO DEGRAU ANTES DO CLIMAX. Segunda descarga + `explosao`: e o unico beat da cena
			// que empilha ceu e estouro, e ele existe pra o clarao de 140 s nao chegar do nada.
			new(135.0, Efeito.DescargaNoCeu | Efeito.Tremor, Som: "explosao"),

			// `sleep(100)`: o ultimo tremor, o overlay sai, a proc RETORNA -- e e so entao que o
			// `SSj3()` escreve `ssj = 3` (`supersaiyanbuff.dm:519`).
			//
			// O CLARAO E A DESCARGA JUNTOS, e e a unica vez na cena inteira: dois minutos de acumulo
			// precisam terminar em alguma coisa que nao aconteceu antes. Um clarao por cena -- ver
			// `Efeito.ClaraoDeTela`.
			new(140.0, Efeito.Assumir | Efeito.Tremor | Efeito.FeixesNoChao
					 | Efeito.AnelDeChoque | Efeito.ClaraoDeTela | Efeito.DescargaNoCeu,
				Narra: "o cabelo cresce até a cintura e as sobrancelhas somem."),

			new(142.2, Efeito.Poeira | Efeito.Raios),
		],
	};

	// ==================================================================================
	// SUPER SAIYAJIN 4 -- `SSj4()` (nao tem arquivo proprio de cinematica)
	// ==================================================================================
	/// <summary>
	/// O SSJ4 nao ganhou um `*Cinematic.dm`: a cena mora dentro do proprio `SSj4()` --
	/// `powerup.wav`, uma onda de choque de raio 3, `move = 0` e `dir = SOUTH`, e depois 160 tiques
	/// de espera (16,0 s -- ver <see cref="BlocoSsj4"/>). O que ela tem de POBRE e a lista de
	/// efeitos, e nao o relogio. O que ele tem de grande e o TEMA (`ssj4_music_played`, o do GT).
	///
	/// ============================ ERA ESCRITA A MAO, VIROU CHAMADA DE FABRICA ============================
	/// Os beats sao os mesmos, com UMA diferenca: o beat de 1,6 s perdeu o `Efeito.Raios`. O `ssj4`
	/// tem `Raios = 0` no catalogo (ele nao solta faisca nenhuma), e a cena soltava -- o mesmo
	/// desencontro que o dono ja tinha apontado no SSJ1. Quem decide agora e o <see cref="Faisca"/>,
	/// que le o catalogo, e nao a mao de quem escreve a cena.
	///
	/// E ela virou chamada porque o `primal_legendary4` sai do MESMO `SSj4()` do DM: manter os dois
	/// roteiros a mao seria manter o mesmo ritmo em dois lugares, que e como um deles envelhece.
	/// ================================================================================================
	/// </summary>
	public static readonly Cinematica Ssj4 = BlocoSsj4(
		"ssj4", TemaSsj4,
		"a pelagem vermelha nasce, e o chão cede.",
		"os olhos ficam dourados e a fera fica sob controle.");

	// ==================================================================================
	// AS DIVINAS -- `SSj()` em God Ki, `UltraInstinct.dm`, `UltraEgo.dm`
	// ==================================================================================
	/// <summary>
	/// SUPER SAIYAJIN BLUE. No DM ela e a MESMA `SSj()` do SSJ1 com `godki.usage` ligado: outro
	/// tema, outra cor, e uma fala que o SSJ1 comum nao tem.
	///
	/// ELA E O MOLDE DA <see cref="EspinhaDivina"/>, e por isso virou chamada: os beats sao
	/// exatamente os que estavam escritos aqui, MENOS a faisca -- o `blue` tinha `Raios = 2` e foi a
	/// zero no corte do dono, e como quem decide e o <see cref="Faisca"/>, esta cena se acertou
	/// sozinha. Quem sai do mesmo `SSj()`+God Ki do DM -- o Rose e o Mistico Ascendido do Prodigial
	/// -- pega o mesmo desenho sem ninguem recopiar tempos.
	/// </summary>
	// "We Saiyans have no limits!" -- a fala que o `SSj()` solta so quando ha God Ki.
	public static readonly Cinematica Blue = EspinhaDivina(
		"blue", TemaBlue,
		"Nós, Saiyajins, não temos limites!",
		"o ki divino toma o corpo, e o cabelo fica azul.");

	/// <summary>
	/// ULTRA INSTINCT -Sign-. `UI_OMEN_MUSIC_DS` -- o tema do despertar, 4 min.
	///
	/// ============================ 22,0 s: O RELOGIO DO `ui_grand_cinematic()` ============================
	/// `UltraInstinct.dm:326`. Sao dois lacos de `sleep(10)`: **12 ciclos** de subida (poeira, raio e
	/// `Quake()` a cada 4) ate os 10,0 s, e **10 ciclos** de surto (a aura azul, o `UI Powerup.dmi` e
	/// os oito feixes de chao) ate os 22,0 s, onde o `move = 1` cai junto com o climax. 220 tiques.
	///
	/// E ela e a MESMA cena do <see cref="UiPerfected"/>: no DM o `ui_transform_to()` (`:459`) roda
	/// `ui_grand_cinematic()` no primeiro despertar, qualquer que seja o estagio -- o Perfected nao
	/// tem proc propria, so tema proprio.
	/// ==============================================================================================
	/// </summary>
	public static readonly Cinematica UiSign = new()
	{
		Forma = "ui_sign",
		Musica = "Ui forms/omen/Ultimate Battle Theme Official Guitar Version - Full Song Montage Montaje.mp3",
		SegundosPreso = 22.0,
		Beats =
		[
			new(0.0, Efeito.Nada, Som: "chargeaura",
				Narra: "o corpo relaxa por inteiro, e a mente se cala."),

			// UM ANEL SOZINHO, sem tremor e sem poeira: no Ultra Instinto nada explode, o ar e que sai
			// do lugar. E o beat mais magro do arquivo de proposito -- ver o comentario do climax.
			new(2.0, Efeito.AnelDeChoque),

			// A SUBIDA: os `Quake()` dos ciclos 4, 8 e 12 (`if(cyc % 4 == 0)`).
			new(4.0, Efeito.Tremor),
			new(8.0, Efeito.Tremor),

			// AQUI HAVIA UM BEAT DE CASCALHO AOS 10,0 s, e ele saiu inteiro: era a unica coisa nele, e
			// o cascalho foi cortado pelo dono (ver o bit 8192 aposentado). O vao que ele deixa vai de
			// 8,0 a 12,0 s -- quatro segundos, bem dentro do limite de 7,2 da bancada --, e ele nao e
			// tela parada: a pedra do chao corre por baixo da cena inteira agora.

			// SEM FAISCA, e a correcao e a mesma do `ssj4`: `ui_sign` tem `Raios = 0` no catalogo
			// -- o Ultra Instinto -Sign- nao estala, ele silencia. Ver `Faisca`, que passou a
			// decidir isso pelo catalogo nas cenas novas; esta e escrita a mao e foi acertada junto.
			new(12.0, Efeito.AuraGrande | Efeito.Tremor | Efeito.FeixesNoChao,
				Narra: "uma coluna de luz azul-prateada engole tudo; o ar dobra os joelhos de quem assiste."),

			// O SURTO: os `Quake()` dos ciclos 3, 6 e 9 (`if(cyc % 3 == 0)`).
			new(15.0, Efeito.Tremor),
			new(18.0, Efeito.Tremor),
			new(21.0, Efeito.Tremor),

			// O CLARAO E O ANEL, E NADA ALEM. O DM fecha esta cena com *... e entao, silencio* -- entao o
			// instante da forma nao pode ganhar descarga nem cascalho. Uma luz e um anel de ar sao
			// exatamente o que se pode acrescentar sem contradizer a cena.
			new(22.0, Efeito.Assumir | Efeito.Tremor
					| Efeito.AnelDeChoque | Efeito.ClaraoDeTela,
				Narra: "os olhos prateiam. o corpo passa a se mover sozinho."),

			// *... e entao, silencio. A poeira paira no ar.* -- o climax do DM E a ausencia de ruido.
			new(24.0, Efeito.Poeira),
		],
	};

	/// <summary>
	/// PERFECTED ULTRA INSTINCT. `UI_PERF_MUSIC_DS`. Mesmo relogio do <see cref="UiSign"/> porque e
	/// literalmente a mesma proc no DM (ver la) -- o que muda e o tema e o que o corpo vira no fim.
	/// </summary>
	public static readonly Cinematica UiPerfected = new()
	{
		Forma = "ui_perfected",
		Musica = "Ui forms/Perfect/Dragon Ball Super Moro Arc   Ultra Instinct Perfected! (Norihito Sumitomo)   By Gladius.mp3",
		SegundosPreso = 22.0,
		Beats =
		[
			new(0.0, Efeito.Tremor, Som: "chargeaura",
				Narra: "o ar esfria, e fagulhas prateadas começam a flutuar."),
			// SEM FAISCA: o `ui_perfected` foi a zero no corte do dono ("raiozinhos somente o lssj 2
			// do primal legendary"). Esta cena e escrita a mao e por isso nao segue o `Faisca`.
			new(2.0, Efeito.AnelDeChoque),
			// Havia um beat so de poeira aos 4,0 s; ele morreu com ela (ver `Efeito.Poeira`).
			new(8.0, Efeito.Tremor),
			// O SEGUNDO ANEL vem com cascalho: o Perfected ja e o instinto COMPLETO, e a contencao do
			// -Sign- (onde o anel sai sozinho) e o que ele deixou pra tras.
			new(10.0, Efeito.AnelDeChoque),

			new(12.0, Efeito.AuraGrande | Efeito.Tremor | Efeito.FeixesNoChao),
			new(15.0, Efeito.Tremor),
			new(18.0, Efeito.Tremor),
			new(21.0, Efeito.Tremor),
			new(22.0, Efeito.Assumir | Efeito.Tremor
					| Efeito.AnelDeChoque | Efeito.ClaraoDeTela,
				Narra: "o cabelo prateia. o instinto está completo."),
			new(24.0, Efeito.Poeira),
		],
	};

	/// <summary>
	/// DESTROYER FORM. `UE_DESTROYER_MUSIC_DS`.
	///
	/// ============================ A UNICA CENA CUJO RELOGIO NAO SAI DE UM `sleep` ============================
	/// O DM **nao tem cinematica pra Destroyer**, e esta escrito la com todas as letras:
	/// `UltraEgo.dm:530` -- *"o PRIMEIRO Ultra Ego: cinematica grande + tema (a Destroyer nao tem)"*.
	/// O `ue_transform_to()` com `stage == 1` so toca o tema e liga o buff.
	///
	/// Entao os 6 s aqui sao INVENCAO, e nao uma cena minha comprimida como as outras eram: nao ha
	/// original pra restaurar. Ela existe porque toda forma do catalogo precisa de cena (a bancada
	/// reprova a falta) e porque uma forma de 60x aparecendo num quadro le como sprite trocado.
	/// Apagar esta entrada devolve o silencio do DM -- e derruba a bancada, que e o preco.
	/// ====================================================================================================
	/// </summary>
	public static readonly Cinematica Destroyer = new()
	{
		Forma = "destroyer",
		Musica = "UE forms/Destroyer/Dragon Ball Z Dokkan Battle PHY God of Destruction Toppo Active Skill OST (High Quality).mp3",
		SegundosPreso = 6.0,
		Beats =
		[
			new(0.0, Efeito.Tremor, Som: "powerup",
				Narra: "a aura da Destruição se acende, roxa e pesada."),
			// DOIS BEATS numa cena de 6 s -- ela tinha TRES no total e vaos de 3 s entre eles, que numa
			// forma de 72x le como o efeito nao ter carregado. Esta cena e invencao (o DM diz que a
			// Destroyer nao tem cinematica), entao encher nao contradiz original nenhum.
			new(1.5, Efeito.AnelDeChoque),

			// SEM FAISCA, pelo mesmo corte do `ui_perfected` -- o `destroyer` tambem foi a zero.
			new(3.0, Efeito.AuraGrande | Efeito.Tremor),
			new(4.5, Efeito.Tremor),
			new(6.0, Efeito.Assumir | Efeito.Tremor
					| Efeito.AnelDeChoque | Efeito.ClaraoDeTela),

			new(7.5, Efeito.Poeira),
		],
	};

	/// <summary>
	/// ULTRA EGO. `UE_ULTRA_MUSIC_DS` -- e a unica das divinas com cinematica propria no DM.
	///
	/// 22,0 s, pelo `ue_grand_cinematic()` (`UltraEgo.dm:415`): e o gemeo exato do
	/// `ui_grand_cinematic` -- 12 ciclos de `sleep(10)` de subida, 10 de surto, 220 tiques. A unica
	/// diferenca de roteiro esta no climax, e o proprio DM a comenta: *"o EGO nao conhece silencio"*.
	/// Onde o Ultra Instinto cala, este GRITA.
	/// </summary>
	public static readonly Cinematica UltraEgo = new()
	{
		Forma = "ultra_ego",
		Musica = "UE forms/Ultra ego/Dragon Ball Super Granolah Arc   Ultra Ego, Unbound   By Gladius.mp3",
		SegundosPreso = 22.0,
		Beats =
		[
			new(0.0, Efeito.Tremor, Som: "chargeaura",
				Narra: "quanto mais dói, mais forte fica."),
			// SEM FAISCA, pelo mesmo corte -- o `ultra_ego` tambem foi a zero.
			// QUATRO BEATS, e o motivo esta escrito no proprio DM: *o EGO nao conhece silencio*. Onde o
			// Ultra Instinto ganhou dois beats magros, este ganha quatro que se empilham -- e a mesma
			// proc, com 220 tiques iguais, contando o oposto. Hoje sao TRES: o primeiro deles era
			// cascalho sozinho e saiu com o efeito (bit 8192, cortado pelo dono). O empilhamento
			// sobrevive porque ele nunca foi a CONTAGEM, foi a subida -- tremor, anel, tremor, tremor.
			new(4.0, Efeito.Tremor),
			new(6.0, Efeito.AnelDeChoque),
			new(8.0, Efeito.Tremor),
			new(10.0, Efeito.Tremor),

			new(12.0, Efeito.AuraGrande | Efeito.Tremor | Efeito.FeixesNoChao,
				Narra: "uma coluna de luz púrpura irrompe: a própria destruição tomando forma."),
			new(15.0, Efeito.Tremor),
			// O ZUMBIDO no meio do surto: e o que impede os `Quake()` dos ciclos 3, 6 e 9 de soarem
			// iguais entre si. Som continuo onde ha tres solavancos identicos.
			new(16.6, Efeito.AnelDeChoque | Efeito.Tremor, Som: "zumbido"),
			new(18.0, Efeito.Tremor),
			new(21.0, Efeito.Tremor),

			// A DESCARGA AQUI E O CONTRAPONTO DO UI: as duas cenas sao a MESMA proc no DM (220 tiques,
			// 12 + 10 ciclos), e a unica coisa que as separa e o que acontece no fim. La, silencio; aqui,
			// o ceu partindo junto.
			new(22.0, Efeito.Assumir | Efeito.Tremor
					| Efeito.AnelDeChoque | Efeito.ClaraoDeTela | Efeito.DescargaNoCeu, Som: "powerup",

				Fala: "HAAAAAAAAAH!!!",
				Narra: "o ego se solta das amarras."),
			new(24.0, Efeito.Poeira),
		],
	};

	// ==================================================================================
	// OOZARU -- a unica cena que o DM NAO tem
	// ==================================================================================
	/// <summary>
	/// VIRAR O MACACO GIGANTE. Serve as DUAS entradas da linha (o comum e o Dourado): ver
	/// <see cref="Para"/>.
	///
	/// ============================ ESTA CENA E DESENHO NOVO, E ESTA DITO ============================
	/// Nao ha `OozaruCinematic.dm`. O que o DM tem e meia duzia de linhas dentro do proprio
	/// `Oozaru/Buff()` (`Oozaru.dm:141-149`), num `spawn`:
	///
	///     animate(container, color = rgb(0,0,0), time = 4, alpha = 0)   // o corpo apaga
	///     container.transform = matrix().Scale(1/5, 1/5)                // encolhe a 20%
	///     animate(container, transform = null, time = 10, alpha = 255, icon = targicon)
	///
	/// Ou seja: o corpo some, encolhe, e volta CRESCENDO ja como macaco em 10 tiques (1,0 s -- o
	/// `time` do `animate` tambem conta decissegundo). O crescimento foi portado onde ele mora de
	/// verdade -- na troca do corpo (`CharacterVisual.CorpoDaForma`) --, e por isso ele acontece
	/// por este caminho e por qualquer
	/// outro que instale o macaco.
	///
	/// O QUE E NOVO e o que vem ANTES: a aura base acesa e o tremor. O dono pediu textual, e o
	/// motivo e de leitura -- sem nada antes, um lutador de 32 px vira um bicho de 96 px em um
	/// quadro, e isso nao le como transformacao, le como o sprite errado ter sido carregado.
	/// ==========================================================================================
	///
	/// ============================ E ELA TOCA TODA VEZ, NAO SO NA ESTREIA ============================
	/// Divergencia deliberada da regra do resto do arquivo ("cinematica so na primeira vez", que e o
	/// `if(!firsttime)` do DM). Aqui ela nao e comemoracao de estreia: ela E a transformacao -- a
	/// aura acesa e o que ARRANCA o macaco de dentro do corpo. Tirar a cena da segunda lua cheia em
	/// diante deixaria o jogador virando bicho instantaneamente, que e o defeito que ela existe pra
	/// evitar.
	///
	/// O que continua sendo so da estreia e o TEXTO no chat (`** Oozaru **`), pelo `primeira` do
	/// pacote `S2C.Oozaru`.
	/// ==========================================================================================
	///
	/// SEM MUSICA de proposito: a faixa de transformacao ABAFA a de batalha pelo tempo dela (ver
	/// `Cinematica.Musica`), e virar Oozaru nao acaba a briga -- comeca a pior parte dela.
	/// </summary>
	public static readonly Cinematica Oozaru = new()
	{
		Forma = "oozaru",
		SegundosPreso = 4.0,
		Beats =
		[
			// A AURA ACENDE E O CHAO TREMÉ. O tremor nao e so destes beats: o tocador sacode a camera
			// TODO QUADRO da cena inteira (o <see cref="RumorDaCena"/>, em `Transformacao._Process`),
			// porque "camera shake por alguns segundos" nao se faz com tres solavancos soltos. Estes
			// beats sao os PICOS por cima daquele fundo.
			new(0.0, Efeito.AuraBase | Efeito.Tremor, Som: "chargeaura",
				Narra: "a lua prende o seu olhar, e alguma coisa responde por dentro."),

			new(1.6, Efeito.Nada,
				Narra: "o corpo dói e não para de doer."),

			// ============================ NESTA CENA NAO SOBE PEDRA -- E O DONO FOI EXPLICITO ============================
			// *"oozaru n tem esse efeito de rocks nem de particulas, o resto da cinematica do oozaru
			// pode deixar"*. Este beat teve `Efeito.PedrasSubindo` por meses e ninguem tinha percebido:
			// pedra a mais nao da erro, nao trava ninguem e ainda LE COMO EFEITO -- quem visse acharia
			// que era de proposito. Quem achou foi a bancada (`--diagforma`, `AFeraForaDosDegraus`),
			// pelo caminho de sempre: a regra estava escrita em comentario e nao em checagem.
			//
			// HOJE A REGRA NAO MORA MAIS NESTE BEAT, e por isso este comentario e historico: a pedra
			// deixou de ser um bit de beat e virou o estado <see cref="Cinematica.OChaoSeSolta"/>,
			// derivado do catalogo (`Catalogo.NaoSeSobePraEla` -- a linha do Oozaru inteira). Escrever
			// ou nao escrever alguma coisa aqui nao levanta nem abaixa uma pedra sequer.
			//
			// O tremor e o `powerup` ficam.
			// ==========================================================================================================
			new(3.0, Efeito.Tremor, Som: "powerup"),

			// O INSTANTE. `Assumir` troca o corpo pelo do macaco E APAGA a aura base -- o dono:
			// "ele vai virar o oozaru e nesse momento a aura desativa".
			new(4.0, Efeito.Assumir | Efeito.Tremor, Som: "roar",
				Narra: "a pele estica, o corpo cresce, e você deixa de caber em si mesmo."),

			new(5.2, Efeito.Poeira),
		],
	};

	// ==================================================================================
	// AS FABRICAS DE RITMO -- e por que elas NAO sao um fallback
	// ==================================================================================
	// ============================ O QUE ESTAVA ERRADO ANTES ============================
	// O catalogo tem 35 formas e so nove tinham cena escrita. As outras vinte e seis caiam num
	// `switch (d.Ordem)` no `Para` que devolvia a cena DE OUTRA FORMA: o Rose estreava com a fala
	// do Blue, o Wrathful com o cabelo do SSJ1 piscando, e o Beast -- a forma mais cara do
	// Prodigial -- com o tema do Super Saiyajin 4 do GT tocando por cima. Ninguem ia reclamar,
	// porque **uma cena errada parece uma cena**: ela roda, prende o corpo, solta na hora e faz
	// barulho. So quem conhecesse as duas formas notaria.
	//
	// ============================ FABRICA NAO E FALLBACK ============================
	// A diferenca esta no que se compartilha. O fallback dava **o objeto inteiro**: mesma musica,
	// mesma fala, mesmo prazo. A fabrica da so o **desenho do ritmo** -- em que segundo treme, em
	// que segundo a aura jorra, em que segundo a forma fica -- e cada forma traz o seu tema, as
	// suas falas e o seu `SegundosPreso`.
	//
	// E o desenho do ritmo e compartilhado no DM TAMBEM, o que faz disto porte e nao invencao: um
	// unico `lssj_grand_cinematic()` serve as tres transformacoes Legendary (`lssjbuff.dm:206`,
	// `:311`, `:405`), um unico `UltraSSJCinematic()` serve os dois Grades (`Ultra_SSj`, chamado
	// com grade 2 e com grade 3), e `SSj4FP()` / `SSj4FPLB()` / `LSSj_Controlled()` /
	// `LSSj3_Primal()` sao quatro procs com exatamente a mesma sequencia de sete linhas.
	// ==============================================================================

	/// <summary>
	/// A FORMA SOLTA FAISCA NA CENA? Derivado do catalogo (<see cref="FormaDef.Raios"/>) e nao
	/// escolhido cena a cena.
	///
	/// ============================ A REGRA JA EXISTIA, SO NAO TINHA NOME ============================
	/// O DM espalha `createLightningmisc` em TODA cinematica, inclusive na do SSJ1. O dono cortou:
	/// *"ssj n tem efeitos de raio"* -- e o catalogo ja dizia o mesmo (`ssj1` tem `Raios = 0`). Ou
	/// seja, a regra de verdade nunca foi "o SSJ1 e excecao", foi **"forma que nao tem faisca nao
	/// solta faisca na estreia dela"**, e ate agora ela vivia como um `if` na cabeca de quem
	/// escrevia cada cena.
	///
	/// Escrever a mesma coisa em dois lugares e como ela envelhece errado, e envelheceu: com a
	/// regra no papel, `ssj4` (`Raios = 0`) e `ui_sign` (`Raios = 0`) tinham `Efeito.Raios` no
	/// roteiro -- duas cenas soltando faisca de uma forma que nao tem nenhuma. Corrigidas junto
	/// com este metodo.
	///
	/// E ELE SE PAGOU NO CORTE SEGUINTE: quando o dono zerou a faisca de 18 formas, TODA cena feita
	/// por fabrica se acertou sozinha. As tres que ainda precisaram de mao (`ui_perfected`,
	/// `destroyer`, `ultra_ego`) sao exatamente as que estao escritas beat a beat -- a medida do
	/// custo de nao passar por aqui.
	/// ==========================================================================================
	/// </summary>
	private static Efeito Faisca(string forma) =>
		(Catalogo.Def(forma)?.Raios ?? 0) > 0 ? Efeito.Raios : Efeito.Nada;

	/// <summary>COMO A CENA COMECA, na espinha Saiyajin. As tres largadas sao dos tres `*Cinematic.dm`.</summary>
	private enum Largada
	{
		/// <summary>`SSJCinematic.dm`: o cabelo troca QUATRO vezes antes de ficar.</summary>
		CabeloPiscando,
		/// <summary>`SSJ2Cinematic.dm`: faisca de largada e os TRES `Quake()` colados no fim.</summary>
		TresTremores,
		/// <summary>`UltraSSJCinematic.dm`: sem piscar e sem os tres tremores -- o Grade so incha.</summary>
		Direta,
	}

	/// <summary>
	/// A ESPINHA SAIYAJIN -- `SSJCinematic.dm` / `SSJ2Cinematic.dm` / `UltraSSJCinematic.dm`.
	///
	/// Os tres procs sao o MESMO roteiro com uma largada diferente cada: espalha raio e poeira,
	/// `sleep(50)` (5,0 s) ate a aura grande, `sleep(100)` (10,0 s) ate os `Quake()` e os oito
	/// feixes de chao, `spawn(20)` pra cratera, `move = 1`.
	///
	/// ============================ E O RELOGIO NAO E UM SO -- SAO DOIS ============================
	/// Isto aqui devolvia 8,5 s pras tres largadas. Errado duas vezes: o numero era compressao minha
	/// (o dono pediu os prazos do DM), e os tres procs nao tem o mesmo comprimento.
	///
	/// O `SSJ2Cinematic.dm` e o `UltraSSJCinematic.dm` ACABAM no proprio `move = 1`, 150 tiques =
	/// **15,0 s**. O `SSJCinematic.dm` tem um `sleep(100)` A MAIS depois dele (`:101`), e a forma so
	/// fica quando a proc retorna: 250 tiques = **25,0 s**. Nao e ritmo diferente de proposito, e a
	/// linha extra do arquivo mais antigo -- e ela e justamente a da estreia do Super Saiyajin.
	/// ========================================================================================
	/// </summary>
	/// <param name="abre">Narracao do primeiro instante (o `rockmoving.wav` do DM).</param>
	/// <param name="vira">Narracao do beat que ASSUME -- o unico que muda o corpo.</param>
	private static Cinematica EspinhaSaiyajin(
		string forma, string musica, Largada largada, string abre, string vira)
	{
		Efeito f = Faisca(forma);

		// Ver o bloco do sumario: o `sleep(100)` a mais do `SSJCinematic.dm` e o que separa os dois.
		bool doSsj1 = largada == Largada.CabeloPiscando;
		double assume = doSsj1 ? 25.0 : 15.0;

		// A faisca de LARGADA e so do SSJ2 (`SSJ2Cinematic.dm` abre com `createLightningmisc`); a
		// espinha do SSJ1 e a do Grade abrem com pedra rolando e poeira.
		var b = new List<Beat>
		{
			new(0.0, Efeito.Tremor | (largada == Largada.TresTremores ? f : Efeito.Nada),
				Som: "rockmoving", Narra: abre),
		};

		if (doSsj1)
		{
			// UM BEAT ARMA O PISCAR e ele dura ate o `Assumir` -- ver `Efeito.PiscaCabelo` e o mesmo
			// bloco na cena escrita a mao do <see cref="Ssj1"/>. Eram quatro beats, um por troca.
			b.Add(new(0.7, Efeito.PiscaCabelo));
		}
		// A FAISCA DE LARGADA e so do SSJ2 (`SSJ2Cinematic.dm:14`), e por isso este beat e CONDICIONAL
		// hoje: ele tambem carregava poeira, e a poeira virou coisa da cratera (ver `Efeito.Poeira`).
		// Sem os dois ele seria um instante em que a cena para pra nada acontecer -- que e o beat vazio
		// que a bancada reprova, e com razao.
		else if (f != Efeito.Nada)
		{
			b.Add(new(1.8, f));
		}

		b.Add(new(5.0, Efeito.AuraGrande | Efeito.Tremor, Som: "chargeaura"));

		// ============================ O ENCHIMENTO, E POR QUE ELE E DERIVADO DO RELOGIO ============================
		// O dono abriu a porta pra mais efeito (*"no dm a transformaçao n tinha mts efeitos, aqui vc pode
		// colocar mais"*), e a restauracao dos prazos do DM tinha deixado buracos de verdade: no molde do
		// SSJ1 sao 7,0 s entre a aura grande sair e a forma ficar, com NADA no meio.
		//
		// Os instantes saem das MESMAS marcas que ja estruturavam a cena (o meio de cada `sleep`), e nao de
		// numeros novos -- por isso os dois relogios recebem contas diferentes em vez da mesma lista
		// esticada. Escrever "7,4" e "12,5" pras duas versoes poria dois beats colados no fim da curta.
		// =====================================================================================================
		b.Add(new(doSsj1 ? 7.4 : 7.2, Efeito.AnelDeChoque));

		// O VAZIO DO MEIO E DO DM: entre o `sleep(50)` e o `sleep(100)` nada NOVO e agendado -- quem
		// preenche sao os `spawn(rand(10,150))` da abertura, que espalham raio e poeira ate os 15,0 s.
		//
		// CONDICIONAL pelo mesmo motivo do beat de 1,8 s: tirada a poeira, o que sobra aqui e a faisca
		// de quem a tem. Nas outras o instante nao tem mais conteudo, e beat vazio nao existe neste
		// arquivo. (A PEDRA saiu daqui junto com o bit 256: ela deixou de ser um instante da largada
		// Direta e passou a correr por baixo das duas, do primeiro segundo ao ultimo.)
		if (f != Efeito.Nada) b.Add(new(10.0, f));

		// A ULTIMA SUBIDA antes do estouro. Na versao curta ela cai a 12,0 s, colada nos tres `Quake()`
		// do SSJ2 (14,3 / 14,6) -- e e ai que ela serve: o chao ja esta instavel quando eles batem.
		b.Add(new(doSsj1 ? 12.5 : 12.0, Efeito.Tremor));

		if (largada == Largada.TresTremores)
		{
			// OS TRES `Quake()` SEGUIDOS -- e o que separa a cena do SSJ2 da do SSJ1 no DM. No
			// original os tres estao COLADOS no fim do `sleep(100)`, e nao espalhados.
			b.Add(new(14.3, Efeito.Tremor));
			b.Add(new(14.6, Efeito.Tremor));
		}

		if (doSsj1)
		{
			// Os `Quake()` e os oito feixes caem aos 15,0 s -- mas a cena continua por mais dez
			// segundos, e e nesse silencio que a forma finalmente fica.
			b.Add(new(15.0, Efeito.Tremor | Efeito.FeixesNoChao, Som: "powerup"));

			// O `spawn(20) createCrater` do DM caia AQUI, oito segundos antes de a forma ficar -- este
			// era um dos quatro lugares que produziam as doze cenas erradas que o dono pegou. A cratera
			// e do instante da troca por regra agora (ver `Efeito.Cratera`); o beat guarda o `Quake()`.
			// O de 18,0 s (`spawn(130)`, a aura grande saindo) era so poeira e morreu com ela.
			b.Add(new(17.0, Efeito.Tremor));

			// OS 7,0 s MUDOS. Este vao so existe no molde do SSJ1 (o `sleep(100)` a mais do
			// `SSJCinematic.dm:101`) e era o maior buraco da fabrica inteira: a aura ja saiu, a cratera ja
			// abriu, e a cena passa sete segundos sem nada ate a forma ficar. Dois beats que SOBEM --
			// o chao se solta, depois estoura -- e o `zumbido` (`aurapowered.wav`) pra o intervalo ter som.
			b.Add(new(20.4, Efeito.Tremor));
			b.Add(new(22.8, Efeito.AnelDeChoque | Efeito.Tremor, Som: "zumbido"));

			b.Add(new(assume, Efeito.Assumir | Efeito.Tremor
							| Efeito.AnelDeChoque | Efeito.ClaraoDeTela, Narra: vira));
			b.Add(new(26.4, Efeito.Poeira | f));
		}
		else
		{
			b.Add(new(assume, Efeito.Assumir | Efeito.Tremor | Efeito.FeixesNoChao
							| Efeito.AnelDeChoque | Efeito.ClaraoDeTela,
					  Som: "powerup", Narra: vira));

			// A CAUDA, e ela e o OUTRO dos quatro lugares errados: aqui a cratera caia DOIS SEGUNDOS
			// DEPOIS da forma (o `spawn(20) createCrater` do `SSJ2Cinematic.dm:45`, que no DM segue o
			// `move = 1`). Ficou o assentamento -- a poeira dela baixando, e a faisca de quem a tem.
			b.Add(new(17.0, Efeito.Poeira | f));
		}

		return new Cinematica { Forma = forma, Musica = musica, SegundosPreso = assume, Beats = [.. b] };
	}

	/// <summary>
	/// A CENA GRANDE DA LENDA -- `lssj_grand_cinematic()` (`lssjbuff.dm:495`).
	///
	/// E a mais longa do DM depois da do SSJ3, e a unica que o original escreveu **pra ser
	/// reaproveitada**: as tres transformacoes Legendary chamam esta mesma proc na estreia
	/// (`:206`, `:311`, `:405`), cada uma com o seu tema.
	///
	/// O relogio e literal: 16 ciclos de `sleep(10)` = 16,0 s de subida lenta (pedra levantando,
	/// tornado, raio, `Quake()` a cada 4 ciclos), a aura grande com os oito feixes de chao aos
	/// 16,0 s, mais 12 ciclos = 12 s (`Quake()` a cada 3), e o climax aos 28,0 s com o grito, a
	/// cratera e o `move = 1`.
	///
	/// ELA NUNCA FOI COMPRIMIDA POR MIM -- a restauracao dos prazos nao mexeu na CONTA dela, so no
	/// divisor (12 -> 10): os 28,0 s daqui sao os 280 tiques do original. Isso a deixa como a REFERENCIA da
	/// medicao -- se o relogio de outra cena divergir do proc dela, esta e a prova de que o metodo
	/// (somar `sleep` e dividir por 10) chega no numero certo.
	/// </summary>
	/// <param name="grita">O `RRRAAAAAAAGH!!!` -- a unica fala da cena, e ela vai no climax.</param>
	private static Cinematica LendaGrande(
		string forma, string musica, string abre, string grita, string vira)
	{
		Efeito f = Faisca(forma);
		return new Cinematica
		{
			Forma = forma,
			Musica = musica,
			SegundosPreso = 28.0,
			Beats =
			[
				new(0.0, Efeito.Tremor, Som: "rockmoving", Narra: abre),

				// A SUBIDA LENTA: pedra solta se rasgando do chao e subindo devagar. O DM narra
				// isso em `lssj_transform_buildup()` -- "loose rocks tear free and drift slowly
				// upward" -- e e a assinatura da linha.
				new(4.0, Efeito.Tremor),

				// ============================ CINCO BEATS, E A SUBIDA E O ARGUMENTO ============================
				// Esta e a segunda cena mais longa do jogo (26 s) e o DM a descreve como uma SUBIDA LENTA --
				// *"loose rocks tear free and drift slowly upward"*, 16 ciclos de `sleep(10)` antes de qualquer
				// estouro. Com nove beats, "lenta" virava "parada": vaos de 3,3 s em que so o que mudava era
				// qual dos tres efeitos de sempre repetia.
				//
				// Os novos formam a rampa que o texto promete: cascalho -> anel -> tremor -> o ceu.
				// ==========================================================================================
				new(6.0, Efeito.AnelDeChoque),
				// O beat de 8,0 s era poeira + faisca; sem a poeira (ver `Efeito.Poeira`) sobrava a
				// faisca, que so o `primal_legendary2` tem -- e sem ela um instante vazio. Ele desceu
				// pro proprio 10,0 s, que ja estava aqui.
				new(10.0, Efeito.Tremor | f),
				new(12.0, Efeito.Tremor | f),
				new(14.0, Efeito.AnelDeChoque | Efeito.Tremor),

				// O SURTO. `*The ground erupts as [src]'s power tears the air apart!*` -- esta
				// narracao do meio e da PROPRIA proc compartilhada, e por isso e a mesma nas
				// quatro cenas que saem daqui. A de abertura e a de fechamento sao de cada uma.
				new(16.0, Efeito.AuraGrande | Efeito.Tremor | Efeito.FeixesNoChao, Som: "chargeaura",
					Narra: "o chão se abre, e o poder rasga o ar."),

				new(19.0, Efeito.Tremor),

				// A DESCARGA NO MEIO DO SURTO. Esta e a unica fabrica em que o ceu entra ANTES do climax, e o
				// motivo e o que a cena narra: *o chao se abre, e o poder rasga o ar*. Rasgar o ar e o unico
				// verbo do arquivo que pede uma descarga, e ele ja estava escrito -- faltava a tela concordar.
				new(20.5, Efeito.DescargaNoCeu),

				new(22.0, Efeito.Tremor | f),
				new(25.0, Efeito.Tremor | f),

				// O ULTIMO DEGRAU: som continuo colado no grito, pra o climax nao ser o primeiro instante
				// alto da cena.
				new(26.4, Efeito.AnelDeChoque | Efeito.Tremor, Som: "zumbido"),

				// O BANHO DE COR ENTRA AQUI, e ele e do `LSSj()` (`lssjbuff.dm:439`):
				// `animate(src, time=7, color=rgb(46,245,72))` no MESMO instante em que `lssj = 3` e o
				// grito saem -- ou seja, exatamente neste beat. No DM ele nao chega a aparecer (a linha
				// seguinte faz `src.color = null` sem sleep no meio); o `LSSj3_Primal()` faz identico
				// com `spawn(12)` e funciona, e e a intencao que se porta. Ver `Efeito.BanhoDeCor`.
				//
				// VALE PRAS QUATRO CENAS QUE SAEM DAQUI, e nao so pro `legendary`: esta fabrica existe
				// porque o DM tem UMA `lssj_grand_cinematic()` servindo as tres estreias Legendary
				// (`lssjbuff.dm:206`, `:311`, `:405`). O verde do climax e da PROC compartilhada tanto
				// quanto a narracao *"o chao se abre, e o poder rasga o ar"* que ja esta no beat de
				// 16,0 s -- e a cor sai da forma (`Efeito.BanhoDeCor`), entao o Wrathful se lava no
				// verde dele e o C-Type no dele.
				new(28.0, Efeito.Assumir | Efeito.Tremor
						| Efeito.AnelDeChoque | Efeito.ClaraoDeTela | Efeito.DescargaNoCeu
						| Efeito.BanhoDeCor,
					Som: "powerup", Fala: grita, Narra: vira),

				new(30.0, Efeito.Poeira | f),
			],
		};
	}

	/// <summary>
	/// O RITUAL DIVINO -- `INITIALIZEGODPROTOCOL()` (`GodRitual.dm:41`).
	///
	/// ============================ MUDA DE PROPOSITO, E ESTA ESCRITO NO ORIGINAL ============================
	/// O autor do DM deixou o comentario dentro da propria proc: *"was gonna do music but decided
	/// not to, let effects do the talking?"*. Nao e faixa que faltou -- e escolha. E ela se
	/// sustenta pelo que a cena CONTA: as outras transformacoes sao alguem se esforcando, esta e
	/// alguem sendo escolhido. Poe-se musica aqui e ela vira comemoracao.
	/// ==================================================================================================
	///
	/// ============================ E O CHAO RACHA, SIM -- MAS SO NO FIM ============================
	/// Esta era a UNICA cena sem cratera do jogo, e era fiel: o `INITIALIZEGODPROTOCOL` so faz
	/// `createShockwavemisc` e `createDustmisc`, nenhum `createCrater`, e a cena narra o corpo
	/// SUBINDO (*"[src] rises into the air!!"*) -- ninguem esta pisando em nada.
	///
	/// A REGRA DO DONO PASSOU POR CIMA, e de proposito: *"assim q se transformar cria a cratera"* nao
	/// abre excecao, e uma lista de cenas isentas seria exatamente a coisa que este funil existe pra
	/// nao ter (ver <see cref="Cinematica.Beats"/>). Sao TRES cenas afetadas -- `ssg`, `rose_ssg` e
	/// `mistico` --, e o que elas ganham e um buraco no instante em que o corpo volta a descer, que e
	/// o que o `Assumir` desta cena e. Se o dono achar que contradiz o ritual, a correcao e dele e e
	/// de uma linha; o que nao volta e a cratera ser campo livre do beat.
	///
	/// O CASCALHO CONTINUA FORA -- ver o beat de 3,5 s. Aquela decisao e sobre o MEIO da cena (chao se
	/// quebrando enquanto o corpo flutua) e nao sobre o instante da troca, e ninguem a revogou.
	/// ==========================================================================================
	///
	/// Relogio do DM: poeira e raio em volta + `Quake()`, `sleep(20)` = 2,0 s pro brilho vermelho,
	/// `sleep(30)` = 5,0 s pros tons se juntarem, `sleep(40)` = 9,0 s pro corpo se erguer, quatro
	/// `createShockwavemisc` de 10 tiques, o `godhue` aos 13,0 s e o buff aos 14,5 s.
	/// </summary>
	private static Cinematica RitualDivino(string forma, string abre, string sobe, string vira)
	{
		Efeito f = Faisca(forma);
		return new Cinematica
		{
			Forma = forma,
			Musica = "",
			SegundosPreso = 14.5,
			Beats =
			[
				new(0.0, Efeito.Tremor | f, Narra: abre),
				// O beat de 2,0 s era so poeira, e morreu com ela (ver `Efeito.Poeira`).

				// ============================ AQUI SO ENTRA O ANEL -- E NADA DE CASCALHO ============================
				// O corpo SOBE nesta cena (*"[src] rises into the air!!"*), ninguem esta pisando em nada no meio
				// dela. Cascalho voando aqui contradiria a propria cena -- e seria o tipo de efeito a mais que
				// ninguem estranharia, porque efeito a mais parece efeito.
				//
				// O anel, ao contrario, e literal: `GodRitual.dm:62-69` solta QUATRO `createShockwavemisc(loc,1)`
				// separados por `sleep(10)`. Tres deles ja viraram os tremores de 9,0 / 10,0 / 11,0 s; estes dois
				// devolvem o DESENHO que eles tinham perdido quando o `Efeito.Onda` foi aposentado.
				// ==============================================================================================
				new(3.5, Efeito.AnelDeChoque),
				new(5.0, Efeito.AuraGrande, Narra: sobe),
				new(7.2, Efeito.AnelDeChoque),

				// AS QUATRO ONDAS de `createShockwavemisc(loc,1)` separadas por `sleep(10)`. O
				// `Efeito.Onda` esta aposentado (ver o enum), entao quem as conta e o tremor --
				// que e o que elas faziam de util num corpo de 32 px.
				new(9.0, Efeito.Tremor),
				new(10.0, Efeito.Tremor),
				new(11.0, Efeito.Tremor),

				// ============================ O `godhue` DOS 13,0 s, QUE FALTAVA INTEIRO ============================
				// `GodRitual.dm:71` -> `updateOverlay(/obj/overlay/godhue)` (`:130-146`): uma chama de corpo
				// inteiro (`godhue.dmi`, 5 quadros 90x140), alpha 125, `pixel_x = -29`, que vive 15 tiques =
				// 1,5 s e sai. E o penultimo ato do ritual, e o unico gesto dele que o port nao desenhava
				// de jeito nenhum -- a `AuraGrande` deste beat e a chama que SOBE, nao a que lava o corpo.
				//
				// Ele vira `BanhoDeCor` porque e o que ele e: uma cor por cima do boneco inteiro, por um
				// segundo, e depois nao. A cor sai da forma (`ff4d6a` no SSG) e nao do arquivo -- e ha uma
				// DIVERGENCIA DECLARADA aqui, do mesmo tipo da `DeusRosa` em `Catalogo.Folha`: o
				// `godhue.dmi` do repo e AZUL (media de pixel 101,150,254) numa cena que e vermelha do
				// comeco ao fim, e a `godhue2.dmi` -- a que fica de aura permanente -- e que e laranja. Se
                // e troca de arquivo do original ou nao, nao da pra saber daqui; o que da pra saber e que
				// um clarao azul no meio do ritual vermelho nao le como o mesmo acontecimento.
				//
				// E O `animate(color=rgb(255,81,0))` DO PROPRIO BUFF confirma o lado quente: ele esta em
				// `GodRitual.dm:91`, uma linha antes de `container.color = null` sem sleep no meio -- o
				// mesmo `animate` morto do `LSSj()` (ver `Efeito.BanhoDeCor`), e a mesma decisao: porta-se
				// a intencao. Ou seja, os dois gestos do original apontam pro laranja-vermelho da forma.
				// =================================================================================================
				new(13.0, Efeito.AuraGrande | Efeito.BanhoDeCor | f),

				// O `ssg.wav` E DO `startbuff(/obj/buff/Ritual_God)` (`GodRitual.dm:78`), que e o que
				// acontece neste beat. Ele tambem estava importado e sem um unico leitor em `.cs` -- ver
				// `Trilha.KiDivino`. O `powerup` que estava aqui era chute meu; o arquivo certo sempre
				// esteve na pasta.
				new(14.5, Efeito.Assumir | Efeito.Tremor
						| Efeito.AnelDeChoque | Efeito.ClaraoDeTela, Som: "ssg", Narra: vira),
				new(16.2, Efeito.Poeira),
			],
		};
	}

	/// <summary>
	/// A ESPINHA DAS DIVINAS -- `SSj()` com `godki.usage` ligado (`supersaiyanbuff.dm:300`).
	///
	/// No DM o Blue nao tem proc proprio: e o MESMO `SSj()` do Super Saiyajin, com outro tema,
	/// outra cor e uma fala que o SSJ1 comum nao tem. O Rose e ele de novo (`godki.dm:21` chama o
	/// `godki_mod` de "this is your Rose variable" -- Rose nao e outra forma, e a variante da
	/// classe), e por isso as cenas divinas de meio de escada saem todas daqui.
	///
	/// ============================ 25,0 s: E A MESMA PROC, LOGO E O MESMO RELOGIO ============================
	/// Isto prendia 6 s, e eu tinha escrito aqui que era "mais curta que a espinha Saiyajin de
	/// proposito, porque o ki divino ja esta aceso". Era racionalizacao: o Blue passa pelo MESMO
	/// `if(!firsttime) SSJCinematic()` (`supersaiyanbuff.dm:320`) que o SSJ1 comum -- os mesmos 250
	/// tiques, os mesmos 25,0 s. Nao ha nada no original que encurte o caminho divino.
	///
	/// O que muda de verdade e a COREOGRAFIA, e ela continua diferente: nao ha cabelo piscando (o
	/// `spawn` do piscar so roda quando ha cabelo comum a trocar) e ha uma FALA que o SSJ1 nao tem
	/// (*"We Saiyans have no limits!"*, solta so quando `godki.usage` esta ligado, `:317`).
	/// ====================================================================================================
	/// </summary>
	private static Cinematica EspinhaDivina(string forma, string musica, string fala, string vira)
	{
		Efeito f = Faisca(forma);
		return new Cinematica
		{
			Forma = forma,
			Musica = musica,
			SegundosPreso = 25.0,
			Beats =
			[
				new(0.0, Efeito.Tremor | f, Som: "chargeaura", Fala: fala),

				// ============================ CINCO BEATS, E UM A MAIS QUE A ESPINHA SAIYAJIN ============================
				// As duas fabricas saem do MESMO `SSj()` e tem o MESMO relogio (25,0 s), entao os instantes sao os
				// mesmos -- 7,4 / 12,5 / 20,4 / 22,8. O extra e o de 2,5 s, e ele existe por uma diferenca real:
				// la o comeco esta ocupado (o cabelo piscando quatro vezes, ou o beat de 1,8 s), aqui nao ha
				// piscar nenhum -- o `spawn` do piscar so roda quando ha cabelo comum a trocar (ver o sumario) --
				// e a cena divina abria com 5,0 s de absolutamente nada depois da fala.
				// ====================================================================================================
				new(2.5, Efeito.AnelDeChoque),
				new(5.0, Efeito.AuraGrande | Efeito.Tremor),                   // `sleep(50)`
				new(7.4, Efeito.AnelDeChoque),
				// A cauda dos `spawn(rand(...))` era poeira + faisca; sem a poeira sobra a faisca, que
				// as divinas nao tem. Ver `Efeito.Poeira` -- e o beat de 12,5 s ja segura este trecho.
				new(12.5, Efeito.Tremor | f),
				new(15.0, Efeito.Tremor | Efeito.FeixesNoChao, Som: "powerup"),// `sleep(100)`
				// O `spawn(20) createCrater` caia aos 17,0 s -- oito segundos antes da forma. A cratera
				// e do instante da troca agora (ver `Efeito.Cratera`); ficou o `Quake()`. O beat de
				// 18,0 s (`spawn(130)`, a aura grande saindo) era so poeira e morreu com ela.
				new(17.0, Efeito.Tremor),

				// OS 7,0 s MUDOS, os mesmos da espinha Saiyajin e pela mesma razao (o `sleep(100)` a mais do
				// `SSJCinematic.dm:101`, que o caminho divino atravessa igual).
				new(20.4, Efeito.AnelDeChoque | Efeito.Tremor),
				new(22.8, Efeito.Tremor, Som: "zumbido"),

				// ============================ O KI DIVINO CHEGA COM SOM E COM BANHO ============================
				// `do_first_godki_appearance()` (`buffs.dm:59-66`) roda no instante em que o
				// `/obj/buff/SuperSaiyan` NASCE com o God Ki ja aceso -- ou seja, exatamente neste beat, o
				// `startbuff` no fim do `SSj()`. Ele faz TRES coisas e o port nao fazia nenhuma:
				//
				//   * `emit_Sound('ssb.wav')` -- o arquivo esta convertido, importado e com `.import` em
				//     `Assets/Sounds/Effects/Ki Effects/ssb.wav`, e ate agora nenhum `.cs` o citava. Era
				//     arte morta pelo mesmo motivo que a `FieryGodBlue.tres` foi: ninguem tinha aberto a
				//     porta pra ela. Ver `Trilha.KiDivinoAzul`;
				//   * `/obj/overlay/goblue` (`bluego.dmi`, 0,58 s) -- a chama azul de corpo inteiro. E a
				//     `AuraGrande` desta cena, que ja esta acesa desde os 4,2 s;
				//   * `animate(color=rgb(226,243,253), time=6)` + `color=null` -- o clarao branco-azulado
				//     no CORPO. Este e o `Efeito.BanhoDeCor`, e ele nao e um dos `animate` mortos: aqui ha
				//     um `sleep(1)` entre os dois, ou seja no BYOND ele aparece de verdade.
				//
				// A cor sai da forma (`3ad2ff` no Blue, `ee3382` no Rose) e nao do `226,243,253` literal --
				// ver `Efeito.BanhoDeCor` sobre por que a derivacao e o hexa: o branco-azulado do DM e o
				// Blue visto de perto, e o Rose, que passa por esta MESMA proc, nao tem clarao proprio la.
				// ==========================================================================================
				new(25.0, Efeito.Assumir | Efeito.Tremor                       // a proc retorna
						| Efeito.AnelDeChoque | Efeito.ClaraoDeTela | Efeito.BanhoDeCor,
					Som: "ssb", Narra: vira),
				new(26.4, Efeito.Poeira | f),
			],
		};
	}

	/// <summary>
	/// A ENTRADA NO CORPO PROPRIO -- o bloco de estreia do `SSj4()` (`supersaiyanbuff.dm:736`).
	///
	/// O SSJ4 nao ganhou um `*Cinematic.dm`: a cena mora dentro do proprio `SSj4()`, no
	/// `if(firsttime<=3)` -- raio e poeira espalhados, `sleep(50)` pra aura grande, `sleep(100)` e
	/// TRINTA E DOIS feixes de chao (o quadruplo das outras cenas), `move = 1`.
	///
	/// E ela serve as duas formas que TROCAM O CORPO pelo do SSJ4 (`Corpo = CorpoDeForma.Ssj4` e
	/// `Ordem = 40` nas duas linhas): o SSJ4 comum e o Legendary Super Saiyajin 4. Os quatro
	/// degraus DENTRO desse corpo (Full Power e Limit Breaker das duas linhas) nao passam por aqui
	/// -- eles usam o <see cref="SurtoCurto"/>, que e o que os procs deles fazem no DM.
	///
	/// ============================ 16,0 s, E NAO 4 ============================
	/// Eu tinha escrito no sumario do `Ssj4` que "a cena e curta". Nao e: o bloco tem `sleep(50)` +
	/// `sleep(100)` + `sleep(10)` = 160 tiques = **16,0 s**, e o `ssj = 4` cai depois do ultimo deles
	/// (`supersaiyanbuff.dm:790-791`). Os 4 s eram compressao minha. O que esta cena tem de curto e a
	/// LISTA DE EFEITOS -- nao o relogio; ela e quase toda espera, com 32 feixes de chao no fim (o
	/// quadruplo das outras) e nada entre a aura e eles.
	/// ==================================================================
	/// </summary>
	private static Cinematica BlocoSsj4(string forma, string musica, string abre, string vira)
	{
		Efeito f = Faisca(forma);
		return new Cinematica
		{
			Forma = forma,
			Musica = musica,
			SegundosPreso = 16.0,
			Beats =
			[
				new(0.0, Efeito.Tremor, Som: "powerup", Narra: abre),

				// ============================ TRES BEATS NUMA CENA QUE ERA `QUASE TODA ESPERA` ============================
				// O sumario logo acima ja dizia o defeito com todas as letras: *"ela e quase toda espera, com 32
				// feixes de chao no fim e NADA entre a aura e eles"*. Sao 16,0 s com seis beats -- o `SSj4()` do DM
				// e pobre de EFEITO, e nao de relogio, e a lista de beats herdou a pobreza sem herdar os
				// `spawn(rand(...))` que a disfarcavam la.
				//
				// O 2,5 s abre o vao que ia de 0 a 5,0; os outros dois enchem os dois vaos de 5,0 s seguintes.
				// ====================================================================================================
				new(2.5, Efeito.AnelDeChoque),
				new(5.0, Efeito.AuraGrande | Efeito.Tremor),                      // `sleep(50)`
				new(7.4, Efeito.Tremor),
				new(10.0, Efeito.Tremor | f),               // cauda dos `spawn(rand(...))`
				new(12.5, Efeito.AnelDeChoque | Efeito.Tremor,
					Som: "zumbido"),
				new(15.0, Efeito.Tremor | Efeito.FeixesNoChao),                   // `sleep(100)`: os 32 feixes
				new(16.0, Efeito.Assumir | Efeito.Tremor
						| Efeito.AnelDeChoque | Efeito.ClaraoDeTela, Narra: vira),   // `sleep(10)`: `ssj = 4`
				new(17.8, Efeito.Poeira | f),
			],
		};
	}

	/// <summary>
	/// O SURTO CURTO -- o degrau que se sobe JA transformado.
	///
	/// ============================ QUATRO PROCS DO DM, UMA SEQUENCIA SO ============================
	/// `SSj4FP()` (`supersaiyanbuff.dm:802`), `SSj4FPLB()` (`:827`), `LSSj_Controlled()`
	/// (`lssjbuff.dm:437`) e `LSSj3_Primal()` (`supersaiyanbuff.dm:539`) sao literalmente as mesmas
	/// sete linhas com outra cor e outro grito: `powerup.wav`, `createShockwavemisc(loc,3)`,
	/// `Quake()`, `animate(color)`, `sleep(8)`, `createShockwavemisc(loc,2)` + `createCrater(loc,5)`,
	/// `chargeaura.wav`.
	///
	/// E faz sentido serem curtas: **ninguem esta virando nada** -- o corpo ja e o da forma, o que
	/// muda e a intensidade. Uma cena de vinte segundos aqui contaria a historia errada.
	/// ========================================================================================
	///
	/// ============================ 0,8 s VIRARAM 2,5 -- E POR QUE ============================
	/// O `sleep(8)` do DM sao 8/10 = 0,8 s. Aqui isso nao chega a existir: a bancada exige que o
	/// beat que ASSUME caia depois de 1,0 s (`RoboDeForma.ConferirRoteiro`), e o motivo dela e
	/// real -- abaixo disso o tremor de camera nem completa um ciclo, e a "cena" le como engasgo.
	/// Pior, a encurtada tem piso de 2,0 s (<see cref="MinimoDaCurta"/>): com a cheia em 0,8 s a
	/// versao curta seria MAIS LONGA que a estreia, e a regra dos tres degraus se inverteria em
	/// silencio.
	/// ===================================================================================
	/// </summary>
	/// <param name="assumeEm">Em que segundo a forma fica. Tem que ser MAIOR que 2,0 (ver acima).</param>
	private static Cinematica SurtoCurto(
		string forma, string musica, double assumeEm, string grita, string vira)
	{
		Efeito f = Faisca(forma);
		return new Cinematica
		{
			Forma = forma,
			Musica = musica,
			SegundosPreso = assumeEm,
			Beats =
			[
				// O ANEL DE ABERTURA E LITERAL: `createShockwavemisc(loc,3)` e a SEGUNDA linha dos quatro
				// procs (`SSj4FP`, `SSj4FPLB`, `LSSj_Controlled`, `LSSj3_Primal`). Ele estava perdido desde
				// que o `Efeito.Onda` foi aposentado pelo DESENHO -- ver `Efeito.AnelDeChoque`.
				//
				// E O SURTO NAO GANHA BEAT NOVO: ele e curto porque ninguem esta virando nada (o corpo ja e o
				// da forma), e encher uma cena de 2,5 s contaria a historia errada. O que ele ganha e o efeito
				// que o DM ja mandava e que este port tinha deixado cair.
				//
				// ============================ O BANHO DE COR E A QUARTA LINHA DOS QUATRO PROCS ============================
				// `animate(src, time=6, color=rgb(...))` + `spawn(12) color=null`: verde no `LSSj3_Primal`
				// (`supersaiyanbuff.dm:549-550`), dourado no `SSj4FP` (`:813-815`), carmesim no `SSj4FPLB`
				// (`:837-839`). Nos tres ele cai na LARGADA, colado no grito -- e nao no instante em que a
				// forma fica, que so vem `sleep(8)` depois. Por isso ele esta neste beat e nao no `Assumir`.
				//
				// A QUARTA PROC (`LSSj_Controlled`) e a unica das quatro sem o `animate`, e era a unica
				// que ganhava o banho mesmo assim -- as quatro sao a MESMA sequencia de sete linhas, e o
				// que esta fabrica produz e essa sequencia e nao quatro copias com um furo cada. Ela nao
				// tem mais cena porque o `legendary_full_power` deixou de ser forma (ver o bloco logo
				// abaixo do `Ssj4LimitBreaker`); a regra fica escrita porque e ela que autoriza o banho
				// nas outras tres. Ver `Efeito.BanhoDeCor`.
				// =====================================================================================================
				new(0.0, Efeito.Tremor | Efeito.AnelDeChoque | Efeito.BanhoDeCor,
					Som: "powerup", Fala: grita),
				new(Math.Round(assumeEm * 0.36, 2), Efeito.Tremor | f),
				new(Math.Round(assumeEm * 0.68, 2), Efeito.AuraGrande | Efeito.Tremor),
				// `createShockwavemisc(loc,2)` + `createCrater(loc,5)` -- a penultima linha dos quatro procs,
				// e ela e um anel MAIS o buraco. Estas quatro cenas ja acertavam o instante da cratera
				// sozinhas; hoje quem a poe e o funil (ver `Efeito.Cratera`) e o beat so traz o anel.
				new(assumeEm, Efeito.Assumir | Efeito.Tremor | Efeito.AnelDeChoque,
					Som: "chargeaura", Narra: vira),
				new(Math.Round(assumeEm * 1.44, 2), Efeito.Poeira | f),
			],
		};
	}

	// ==================================================================================
	// AS 24 CENAS QUE FALTAVAM
	// ==================================================================================
	// A MUSICA DE CADA UMA E A DO DM, e onde o DM e mudo estas tambem sao -- com tres excecoes,
	// cada uma marcada INVENCAO no comentario da propria cena. O gate de tema no BYOND e por VAR
	// DE SAVE (`ssj1_music_played`, `blue_music_played`, `wrathful_music_played`...), e varias
	// formas COMPARTILHAM a mesma var: quando isso acontece, a segunda forma a estrear ja pega a
	// var queimada e sobe muda. Nao e esquecimento do original -- e o mesmo "toca uma vez na vida
	// do personagem" aplicado a uma LINHA inteira em vez de a uma forma.

	// ---------------------------------------------------------------------------
	// OS GRADES DO SSJ1 -- `Ultra_SSj(grade)` -> `UltraSSJCinematic()`
	// ---------------------------------------------------------------------------
	// MUDAS, e e do DM: o `Ultra_SSj` nao chama `emit_TransformMusic` em lugar nenhum. Faz sentido
	// -- o Grade nao e uma forma nova, e o SSJ1 sendo forcado alem do que o corpo aguenta, e o
	// tema do Super Saiyajin ja tocou na vida deste personagem.
	/// <summary>SUPER SAIYAJIN GRADE 2. Falas do `Ultra_SSj` (`supersaiyanbuff.dm:405`, `:414`).</summary>
	public static readonly Cinematica Grade2 = EspinhaSaiyajin(
		"grade2", "", Largada.Direta,
		// "*[src] begins to power up beyond their Super Saiyan power*"
		"começa a forçar o poder para além do Super Saiyajin.",
		// "*[src]'s Super Saiyan power becomes a more spikey gold!* (Grade 2)"
		"os músculos incham e o dourado fica mais afiado.");

	/// <summary>SUPER SAIYAJIN GRADE 3. A mesma proc com `grade = 3` -- o corpo ja nao aguenta o que carrega.</summary>
	public static readonly Cinematica Grade3 = EspinhaSaiyajin(
		"grade3", "", Largada.Direta,
		"força o corpo além do que o Grade 2 aguentava.",
		"o volume dobra, e cada movimento passa a custar caro.");

	// ---------------------------------------------------------------------------
	// OS DEGRAUS DENTRO DO CORPO DO SSJ4 -- `SSj4FP()` / `SSj4FPLB()`
	// ---------------------------------------------------------------------------
	/// <summary>SUPER SAIYAJIN 4 FULL POWER. Fala e narracao literais do `SSj4FP` (`:809`, `:823`).</summary>
	public static readonly Cinematica Ssj4FullPower = SurtoCurto(
		"ssj4_full_power", "", 2.5,
		// "[src]: HAAAAAAAAAAAAAAAHHHHHHHHHH!!!!"
		"HAAAAAAAAAAAAAAAHHHHHHHHHH!!!!",
		// "*A surge of golden power explodes around [src] as they reach Full Power!*"
		"uma onda de poder dourado explode ao redor: é o Full Power.");

	/// <summary>
	/// SUPER SAIYAJIN 4 LIMIT BREAKER. Fala e narracao do `SSj4FPLB` (`:834`, `:848`).
	///
	/// ============================ A MUSICA E INVENCAO ============================
	/// O `SSj4FPLB()` e mudo no DM. O catalogo chama esta forma de "God Form" (`ssj4fplbmult = 56`)
	/// e ela e o fim da linha Saiyajin comum -- subir o degrau mais caro do jogo em silencio le
	/// como bug, nao como contencao. `Godhand.ogg` estava no disco do original e NENHUM `.dm` a
	/// referencia; o nome e o que ela e. **Apagar esta string devolve o silencio do DM sem mexer em
	/// mais nada.**
	/// =========================================================================
	/// </summary>
	public static readonly Cinematica Ssj4LimitBreaker = SurtoCurto(
		"ssj4_limit_breaker", "Godhand.ogg", 3.0,
		// "[src]: HAAAAAAAAAAAAAAAAAHHHHHHHHHHHHHHH!!!!"
		"HAAAAAAAAAAAAAAAAAHHHHHHHHHHHHHHH!!!!",
		// "*A blinding crimson aura erupts around [src] as they shatter their limit!*"
		"uma aura carmesim cegante irrompe: o limite se parte.");

	// ---------------------------------------------------------------------------
	// A LINHAGEM DO FUTURO -- `SSj()` com `FutureLineage`
	// ---------------------------------------------------------------------------
	/// <summary>
	/// FUTURE SUPER SAIYAJIN. Ele nao acessa SSJ2 nem SSJ3 (`supersaiyanbuff.dm:415`, `:487`): esta
	/// e a UNICA transformacao da linhagem, e ela sobe em dez estagios por maestria. Por isso ela
	/// leva a espinha do SSJ1 inteira, cabelo piscando incluso -- e a estreia dele, nao um meio.
	/// </summary>
	public static readonly Cinematica FutureSsj = EspinhaSaiyajin(
		"future_ssj", TemaSsj1, Largada.CabeloPiscando,
		"o chão começa a tremer, e alguma coisa herdada responde.",
		// "*[src]'s hair stands on end and turns yellow!*"
		"o cabelo se ergue e doura -- o mesmo poder, outro caminho.");

	// ---------------------------------------------------------------------------
	// A LINHA LEGENDARY -- as tres que chamam `lssj_grand_cinematic()`
	// ---------------------------------------------------------------------------
	/// <summary>WRATHFUL. `Restrained_SSj()` (`lssjbuff.dm:206`), tema "22. Broly Evolves".</summary>
	public static readonly Cinematica Wrathful = LendaGrande(
		"wrathful", "22. Broly Evolves   DBS Broly Original Soundtrack.mp3",
		// "*A monstrous green aura coils around [src] as a legendary power begins to surface...*"
		"uma aura verde monstruosa se enrola em volta; alguma coisa lendária está subindo.",
		// "[src]: RRRAAAAAAAGH!!!"
		"RRRAAAAAAAGH!!!",
		// "*[src]'s eyes go cold and empty as a monstrous Legendary fury takes hold...*"
		"os olhos ficam frios e vazios, e a fúria toma o lugar de quem estava aqui.");

	/// <summary>SUPER SAIYAJIN C-TYPE. `Unrestrained_SSj()` (`:311`), tema da transformacao do Broly.</summary>
	public static readonly Cinematica CType = LendaGrande(
		"c_type", "Dragon Ball Super - Broly's Transformation Theme (HQ Epic Cover).mp3",
		// "*The earth caves as [src]'s aura erupts into a vast, menacing green inferno!*"
		"a terra cede, e a aura vira um inferno verde.",
		"RRRAAAAAAAGH!!!",
		// "*Jagged green sparks crackle violently around [src]!*"
		"faíscas verdes rasgadas estalam em volta do corpo inchado.");

	/// <summary>LEGENDARY SUPER SAIYAJIN. `LSSj()` (`:405`), tema "Rage &amp; Sorrow".</summary>
	public static readonly Cinematica Legendary = LendaGrande(
		"legendary", "Dragon Ball Super Broly - Rage & Sorrow Movie Version.mp3",
		// "*[src]'s hair blazes a deeper, jagged green as the power keeps surging!*"
		"o cabelo arde num verde mais fundo, e o poder não para de subir.",
		// "[src]: HRRAAAAAAAAGH!!!"
		"HRRAAAAAAAAGH!!!",
		// "*[src]'s aura roars skyward as the legendary power reaches its peak!*"
		"a aura ruge para o céu: a lenda chegou ao topo.");

	// ============================ A CENA DO LEGENDARY FULL POWER FOI APAGADA ============================
	// Ela era um `SurtoCurto("legendary_full_power", "", 3.0)` -- o `LSSj_Controlled()` do
	// `lssjbuff.dm:437`. O `legendary_full_power` deixou de ser entrada do catalogo: ele e o proprio
	// `legendary` com a maestria em 100% (ver o bloco dele em `Formas.cs`).
	//
	// E CINEMATICA PRECISA DE INSTANTE, e uma rampa nao tem nenhum. A cena so existe porque ha um
	// quadro em que o jogador NAO era aquilo e o seguinte em que ele e; a maestria sobe de fracao em
	// fracao e cruza os 100% sem borda pra filmar. Mesmo argumento que aposentou a cena do Mistico
	// Ascendido quando ele virou ponto da curva do Mistico.
	// ====================================================================================================

	// ---------------------------------------------------------------------------
	// A LINHA LEGENDARY PRIMAL -- ladder proprio (`Class == "Legendary Primal Saiyan"`)
	// ---------------------------------------------------------------------------
	/// <summary>
	/// SUPER SAIYAJIN C-TYPE (Z). No DM e o `SSj()` comum com a classe Legendary Primal -- entao
	/// leva a espinha do SSJ1 inteira e o tema do SSJ1 (a var de save e a mesma, `ssj1_music_played`).
	/// </summary>
	public static readonly Cinematica PrimalCType = EspinhaSaiyajin(
		"primal_c_type", TemaSsj1, Largada.CabeloPiscando,
		"o chão treme, e o sangue primal responde antes da cabeça.",
		"o cabelo se ergue com um verde por baixo do dourado.");

	/// <summary>
	/// LEGENDARY SUPER SAIYAJIN (Primal). `SSj2()` com `Class == "Legendary Primal Saiyan"`
	/// (`supersaiyanbuff.dm:429`) -- dai a espinha do SSJ2 (os tres `Quake()` colados) e o tema
	/// PROPRIO da linhagem, `10's.mp3`, que o DM toca so pra ela.
	/// </summary>
	public static readonly Cinematica PrimalLegendary = EspinhaSaiyajin(
		"primal_legendary", "10's.mp3", Largada.TresTremores,
		"o ar fica pesado, e a lenda da linhagem começa a acordar.",
		// "*A great wave of power emanates from [src] as a yellow aura bursts around them!*"
		"uma onda de poder se abre, e a aura estoura em verde.");

	/// <summary>
	/// LEGENDARY SUPER SAIYAJIN 2 (Primal). Sobe pelo `SSj3()` no DM, e dai vem a MUSICA
	/// (`ssj3theme.ogg`, `SSJ3Cinematic.dm:24`).
	///
	/// ============================ MAS NAO O ROTEIRO, E ISSO E DELIBERADO ============================
	/// A cena do SSJ3 e o DISCURSO do Goku pro Babidi -- "isto e um Super Saiyajin", "e isto e para
	/// ir ainda mais alem". E texto de um personagem especifico explicando a propria escada, e um
	/// Legendary Primal nao tem escada nenhuma pra explicar: ele tem uma lenda que sai a forca.
	/// Emprestar aquelas falas seria o defeito exato que estas fabricas existem pra matar -- uma
	/// cena falando pela boca de outra forma.
	///
	/// Entao o ritmo e o `lssj_grand_cinematic()`, que e a cinematica que o proprio DM escreveu pra
	/// **Legendary**, e a musica continua a que o DM toca neste degrau.
	/// ==========================================================================================
	/// </summary>
	public static readonly Cinematica PrimalLegendary2 = LendaGrande(
		"primal_legendary2", "ssj3theme.ogg",
		"o chão em volta se desfaz sozinho; nada foi golpeado.",
		"HRRAAAAAAAAGH!!!",
		"a aura verde irrompe, e o segundo degrau da lenda fica.");

	/// <summary>LEGENDARY SUPER SAIYAJIN 3 (Primal). `LSSj3_Primal()` (`supersaiyanbuff.dm:539`).</summary>
	public static readonly Cinematica PrimalLegendary3 = SurtoCurto(
		"primal_legendary3", "", 2.5,
		// "[src]: HAAAAAAAAAAAHHHHHHH!!!!"
		"HAAAAAAAAAAAHHHHHHH!!!!",
		// "*[src]'s green aura erupts as they ascend to Legendary Super Saiyan 3!*"
		"a aura verde irrompe: Legendary Super Saiyajin 3.");

	/// <summary>
	/// LEGENDARY SUPER SAIYAJIN 4 (Primal). Sai do Oozaru Dourado pela MESMA porta do SSJ4 comum
	/// (ver o catalogo), entao leva o mesmo bloco de estreia e o mesmo tema do GT -- no DM os dois
	/// passam pelo mesmo `SSj4()` e pela mesma var `ssj4_music_played`.
	/// </summary>
	public static readonly Cinematica PrimalLegendary4 = BlocoSsj4(
		"primal_legendary4", TemaSsj4,
		"a pelagem nasce verde-escura, e o chão cede sob o peso.",
		"os olhos douram, e a fera lendária passa a obedecer.");

	/// <summary>LEGENDARY SSJ4 FULL POWER. `SSj4FP()` pela linhagem Legendary Primal.</summary>
	public static readonly Cinematica PrimalLegendary4FullPower = SurtoCurto(
		"primal_legendary4_full_power", "", 2.5,
		"HAAAAAAAAAAAAAAAHHHHHHHHHH!!!!",
		"o poder da lenda enche a pelagem inteira: Full Power.");

	/// <summary>
	/// LEGENDARY SSJ4 LIMIT BREAKER. `SSj4FPLB()` pela linhagem Legendary Primal.
	///
	/// ============================ A MUSICA E INVENCAO ============================
	/// Mudo no DM, pelo mesmo motivo do `ssj4_limit_breaker` -- e com a mesma saida:
	/// `fightinggold.ogg` estava no disco do original e nenhum `.dm` a referencia. Apagar a string
	/// devolve o silencio.
	/// =========================================================================
	/// </summary>
	public static readonly Cinematica PrimalLegendary4LimitBreaker = SurtoCurto(
		"primal_legendary4_limit_breaker", "fightinggold.ogg", 3.0,
		"HAAAAAAAAAAAAAAAAAHHHHHHHHHHHHHHH!!!!",
		"o verde vira luz, e o teto do corpo lendário se parte.");

	// ---------------------------------------------------------------------------
	// A LINHA DIVINA -- SSG / Blue Evolution
	// ---------------------------------------------------------------------------
	/// <summary>SUPER SAIYAJIN GOD. O ritual do `GodRitual.dm` -- mudo, e sem cratera.</summary>
	public static readonly Cinematica Ssg = RitualDivino(
		"ssg",
		// "[M] begins to glow with a red hue!!"
		"o ar em volta ganha um tom vermelho que não vem de lugar nenhum.",
		// "The red hues collesques onto [src]!!" / "[src] rises into the air!!"
		"os tons vermelhos se juntam em você, e os pés deixam o chão.",
		// "Your godly power shimmers around you..."
		"o poder divino se acomoda: o cabelo e os olhos ficam vermelhos.");

	/// <summary>
	/// SUPER SAIYAJIN BLUE EVOLUTION. No DM e o `Ultra_SSj` com `godki.usage` (o "Royale"), entao
	/// leva a espinha do Grade -- e e MUDA pelo motivo do original: o tema azul e liberado pela var
	/// `blue_music_played`, que ja queimou quando este personagem estreou o Blue. O Evolution so se
	/// alcanca a partir do Blue; nao existe quem chegue aqui com a var intacta.
	/// </summary>
	public static readonly Cinematica BlueEvolution = EspinhaSaiyajin(
		"blue_evolution", "", Largada.Direta,
		"o ki divino já está aceso, e ainda assim o chão treme.",
		"o azul escurece e passa a queimar de dentro para fora.");

	// ---------------------------------------------------------------------------
	// A LINHA ROSE -- a MESMA escada, a cor outra (`godki.dm:21`)
	// ---------------------------------------------------------------------------
	/// <summary>SUPER SAIYAJIN GOD (Rose). O mesmo ritual: o corpo roubado tambem alcanca o deus.</summary>
	public static readonly Cinematica RoseSsg = RitualDivino(
		"rose_ssg",
		"o ar em volta ganha um vermelho que não pertence a este corpo.",
		"os tons se juntam em você, e os pés deixam o chão.",
		"o poder divino se acomoda num corpo que não nasceu para ele.");

	/// <summary>
	/// SUPER SAIYAJIN ROSE. `SSj()` com God Ki na classe Rose -- a MESMA proc do Blue, e por isso a
	/// mesma espinha e o mesmo tema (a var de save tambem e a mesma, `blue_music_played`). A fala e
	/// a que o DM solta so quando ha ki divino.
	/// </summary>
	public static readonly Cinematica Rose = EspinhaDivina(
		"rose", TemaBlue,
		// "We Saiyans have no limits!"
		"Nós, Saiyajins, não temos limites!",
		"o ki divino corrompido toma o corpo, e o cabelo fica rosa.");

	/// <summary>SUPER SAIYAJIN ROSE 2. `Ultra_SSj` com God Ki na classe Rose -- mudo pelo mesmo motivo do Blue Evolution.</summary>
	public static readonly Cinematica Rose2 = EspinhaSaiyajin(
		"rose2", "", Largada.Direta,
		"o rosa já está aceso, e mesmo assim o chão cede.",
		"o rosa fica quase branco, e nada em volta reage como devia.");

	// ---------------------------------------------------------------------------
	// A LINHA DO MISTICO -- Mistico -> Beast (`Mystic.dm`, `godki.dm:349`)
	// ---------------------------------------------------------------------------
	/// <summary>
	/// MISTICO. O DM nao tem cinematica pro Mistico -- o `/obj/buff/Mystic` acende o overlay e
	/// escreve uma linha. Mas ele e DADO por um ritual (no DM, o `INITIALIZEGODPROTOCOL`; aqui, o
	/// ritual do Kaioshin), e por isso ele leva o RITUAL: o mesmo desenho, a mesma mudez, e o mesmo
	/// "isto nao e esforco, e alguem sendo escolhido".
	///
	/// ============================ ERAM DUAS CENAS PRA UMA FORMA SO ============================
	/// A `ProdigialMisticoAscendido` (o 32x) foi DELETADA junto com a entrada dela no catalogo. Ela
	/// era a "estreia" de um degrau que nunca teve corpo proprio: mesma aura, mesmo cabelo, mesmo
	/// tudo -- so o numero mudava, e hoje ele muda numa RAMPA, sem instante pra filmar. Manter a
	/// cena daria uma cinematica disparando no meio de uma subida continua, em cima de um sprite
	/// que nao mexe um pixel.
	/// ======================================================================================
	/// </summary>
	public static readonly Cinematica Mistico = RitualDivino(
		Jandirus.Core.Forms.Catalogo.IdMistico,
		"o potencial que estava dormindo se mexe, e o ar em volta esfria.",
		"nada explode: o corpo simplesmente para de ter limite.",
		// "You unleash your godly Mystic form."
		"o Místico se abre -- todo o potencial de uma vez, sem virar deus.");

	/// <summary>
	/// BEAST -- o despertar do Prodigial (`BeastUp()` + `/obj/buff/BeastForm.Buff()`, `Mystic.dm`).
	///
	/// SURTO e nao cena grande, porque e o que o DM faz: `BeastUp()` e `powerup.wav`, `Quake()`,
	/// onda e cratera, e acabou. Mas com 4,0 s em vez dos 2,5 dos outros surtos -- este e o unico
	/// que TROCA O CABELO (o SSJ2 do proprio jogador embranquecido, `beast_hair_icon`) e acende uma
	/// aura de cor propria, e trocar as duas coisas em 2,5 s le como o sprite ter piscado.
	///
	/// ============================ A MUSICA E INVENCAO ============================
	/// O DM e mudo aqui. `BF - Gohan's Hidden Power.mp3` estava no disco do original, nenhum `.dm`
	/// a referencia, e o Beast E a forma do Gohan (o cabecalho do proprio `Mystic.dm` diz "estilo
	/// Gohan em Super Hero"). Apagar a string devolve o silencio do DM.
	/// =========================================================================
	/// </summary>
	public static readonly Cinematica Beast = SurtoCurto(
		"beast", "BF - Gohan's Hidden Power.mp3", 4.0,
		"AAAAAAAAAAAAAAAAAHHHH!!!",
		// "*O cabelo de [container] se ergue num branco-gelo e uma aura entre o azul e o roxo
		//   EXPLODE ao seu redor -- o BEAST desperta!*" (o DM ja escreveu esta em portugues)
		"o cabelo se ergue num branco-gelo e uma aura entre o azul e o roxo explode: a FERA acordou.");

	// ---------------------------------------------------------------------------
	// O FROST DEMON -- `Frost_Demon_Forms()` / `fd_burst_fx()` / `fd_grand_cinematic()`
	// ---------------------------------------------------------------------------
	// ============================ O DM TEM DUAS CENAS, E ELAS NAO SAO POR FORMA ============================
	// La a cena depende do ESTADO e nao do degrau: `Frost_Demon_Forms` (`IcerTransform.dm:98-107`) so
	// chama a `fd_grand_cinematic()` quando quem transforma e um MUTANTE entrando numa forma que ele
	// **nao segura** -- e so na primeira vez naquela forma (`fd_cine_flags`). Nas repeticoes vem a
	// `fd_burst_fx()`, um surto de 2,5 s. Pra todo o resto -- o Frost Demon normal inteiro -- o DM
	// e MUDO: troca o icone, toca `1aura.wav` e escreve uma linha no chat.
	//
	// Aqui a cena e da FORMA, e nao do estado. Mas o desenho sai no mesmo lugar, porque o port ja tem
	// o eixo que o DM nao tinha: os TRES DEGRAUS DE CENA (`Cinematicas.Degrau`) -- estreia cheia,
	// encurtada na repeticao, e instantanea a partir de 50% de maestria. Ou seja **a mesma progressao
	// "grande da primeira vez, surto depois, nada quando ja e seu"**, so que medida por maestria em vez
	// de por um bitmask de formas ja vistas.
	//
	// O QUE DIVERGE, DE OLHO ABERTO: o Frost Demon NORMAL passa a ver a cena das evolucoes, e no DM ele
	// nao veria. E deliberado -- subir pra 10x e pra 20x calado le como bug, e a bancada deste projeto
	// ja exige que toda forma tenha cena PROPRIA (o fallback por `Ordem` foi deletado justamente pra
	// uma forma nova nao estrear com a cena de outra). As quatro supressoes e a base ficam curtas, que
	// e o que elas sao: recolher a casca nao e virar nada.
    //
	// A MUSICA E A DO ORIGINAL, e ela e literal: `emit_TransformMusic(file("Sounds/Music/battle ost/
	// DBZ- Battle Music 10.mp3"), 850)` -- a unica linha de tema do arquivo inteiro, e ela toca
	// exatamente no ramo da transformacao instavel. Por isso ela esta nas duas EVOLUCOES e nao nas
	// supressoes: e o tema de "o poder esta vindo a tona", nao o de "estou me guardando".
	// =====================================================================================================
	private const string TemaFrost = "battle ost/DBZ- Battle Music 10.mp3";

	/// <summary>
	/// QUANTO DURA O SURTO DO FROST DEMON: <b>2,5 s</b> -- o `spawn(25)` do `fd_burst_fx()`
	/// (`IcerTransform.dm:186`), que e o tempo que a aura vermelha grande fica vestida.
	///
	/// E o numero do original e nao um ritmo escolhido: as cinco cenas curtas desta linha (as quatro
	/// supressoes e a forma base) SAO aquele surto. Uma constante e nao cinco literais porque elas sao
	/// o MESMO efeito cinco vezes -- se o surto mudar, muda inteiro.
	/// </summary>
	private const double SurtoDoFrost = 25 / TempoDoDm.TiquesPorSegundo;

	/// <summary>1a FORMA (supressao 25%). O fundo do poco do Mutante -- ele acorda aqui.</summary>
	public static readonly Cinematica Frost1 = SurtoCurto(
		"frost1", "", SurtoDoFrost,
		"Ainda não. Ainda não.",
		"a casca se fecha até o osso: sobra um quarto do que ele é.");

	/// <summary>2a FORMA (supressao 50%).</summary>
	public static readonly Cinematica Frost2 = SurtoCurto(
		"frost2", "", SurtoDoFrost,
		"Um pouco. Só um pouco.",
		"a carapaça cede um palmo, e metade do poder respira.");

	/// <summary>3a FORMA (supressao 75%).</summary>
	public static readonly Cinematica Frost3 = SurtoCurto(
		"frost3", "", SurtoDoFrost,
		"Chega de me segurar!",
		"o corpo se alonga: três quartos do poder já estão do lado de fora.");

	/// <summary>4a FORMA (supressao 90%). O ultimo degrau antes de o corpo ficar inteiro.</summary>
	public static readonly Cinematica Frost4 = SurtoCurto(
		"frost4", "", SurtoDoFrost,
		"Quase. QUASE.",
		"só um fio de casca continua no lugar -- e ele está tremendo.");

	/// <summary>
	/// FORMA BASE. Ela e a UNICA cena do jogo que nao celebra um degrau novo: pro Frost Demon normal
	/// ela e onde ele ja vive, e pro Mutante ela e a primeira vez que o corpo dele fica inteiro.
	/// Curta por isso.
	/// </summary>
	public static readonly Cinematica Frost5 = SurtoCurto(
		"frost5", "", SurtoDoFrost,
		"Este é o meu corpo. TODO ele.",
		"a última casca se parte, e o poder para de ser podado.");

	/// <summary>
	/// 1a EVOLUCAO (10x). A `fd_grand_cinematic()` do original -- e o DM diz de onde ela veio na
	/// propria linha: *"receita da lssj_grand_cinematica, recolorida"*. Por isso ela usa a MESMA
	/// fabrica das tres cenas Legendary, e nao uma copia com outros numeros.
	/// </summary>
	public static readonly Cinematica Frost6 = LendaGrande(
		"frost6", TemaFrost,
		// "*Uma aura VERMELHA e violenta serpenteia em volta de [src] -- um poder incontrolavel
		//   comeca a vir a tona...*" (IcerTransform.dm:200)
		"uma aura violenta serpenteia em volta; alguma coisa embaixo da pele começa a vir à tona.",
		// "[src]: RRRAAAAAAAAGH!!!" (IcerTransform.dm:243)
		"RRRAAAAAAAAGH!!!",
		"a carapaça se refaz do zero -- dez vezes o que ele era.");

	/// <summary>
	/// 2a EVOLUCAO (20x) -- a "Forma Black" do original. Mesma receita, mais alto.
	///
	/// O NOME NA TELA e "2ª Evolução" e nao "Forma Black" (ver `Races.FormasDeFrost.Nome`): o corpo
	/// dela e o que o jogador escolheu, e "Black" e nome proprio de um sprite especifico.
	/// </summary>
	public static readonly Cinematica Frost7 = LendaGrande(
		"frost7", TemaFrost,
		"o ar em volta congela e racha ao mesmo tempo; o que vem agora não tem nome.",
		"RRRAAAAAAAAAAAGH!!!",
		"a evolução final se fecha em volta dele -- vinte vezes, e nenhuma casca sobrando.");

	// ---------------------------------------------------------------------------
	// AS TRES LINHAS RACIAIS -- Namekuseijin, Heran e Alien
	// ---------------------------------------------------------------------------
	// ============================ DUAS DELAS SAO **INSTANTANEAS** NO ORIGINAL ============================
	// `snamek()` (`Super_Namek.dm:8-15`) e `Alien_Trans()` (`Alien_Transformations.dm:10-22`) nao tem UM
	// `sleep`. Eles tocam o som, chamam `createDustshock`/`createShockwavemisc`, abrem a cratera e ligam
	// o buff -- a forma fica no mesmo tique. O `animate(src, time=7)` do Namekuseijin e o unico relogio
	// dos dois arquivos, e ele e um flash de 0,7 s, nao uma cena.
	//
	// **A ESCOLHA FOI DAR A ELAS O SURTO E NAO OS 0,7 s**, e a razao e o funil deste port: forma que
	// aparece sem cena nenhuma le como bug (a bancada ja exige cena PROPRIA por forma justamente pra
	// isso), e o beat de virada precisa de espaco antes dele pra existir. O numero NAO e invencao: e o
	// mesmo `spawn(25)` do `fd_burst_fx()` -- ver <see cref="SurtoDoFrost"/> --, a cena mais curta que o
	// DM escreveu. Ou seja: o piso vem do original, so que de outro arquivo dele.
	//
	// ============================ O HERAN E O CONTRARIO: ELE E LONGO E O RELOGIO ENCOLHE ============================
	// `Max_Power()` (`HeranBuff.dm:97-190`) e `True_Max_Power()` (`:191-266`) sao a espinha Saiyajin
	// inteira -- `sleep(50)` ate a aura grande, `sleep(100)` ate os `Quake()` e os feixes de chao --
	// mais uma cauda propria: `sleep(2000 * ssjdrain)`.
	//
	// **ESSA CAUDA E A MESMA IDEIA DOS TRES DEGRAUS DE CENA DESTE PORT**, e vale registrar porque e uma
	// coincidencia de desenho e nao de numero: o `ssjdrain` do Heran CAI com a maestria ate zero
	// (`HeranBuff.dm:39`), entao no original a espera pela forma encurta sozinha conforme ele a domina,
	// ate sumir. E exatamente o que `DegrauDeCena` faz aqui (estreia -> encurtada -> instantanea aos
	// 50%), so que medido por maestria em vez de pelo dreno que a maestria move.
	//
	// ============================ POR QUE A LARGADA NAO E O `CabeloPiscando` ============================
	// Ela DEVERIA ser: o `Max_Power()` e o unico proc fora do `SSJCinematic.dm` que pisca o cabelo
	// (`updateOverlay/removeOverlay(sh1)` quatro vezes, `sleep(rand(3,10))` entre elas). Nao e porque o
	// port nao tem o penteado: `/obj/overlay/hairs/superheran/sh1` veste `container.truehair` -- o
	// PROPRIO cabelo do jogador -- e nao ha folha `SSjHeran` nenhuma pra o `CabelosDeForma` achar. Com
	// `SufixoDoCabelo` vazio os dois lados da piscada seriam o MESMO arquivo, e a bancada do piscar
	// (`RoboDeForma`) reprova isso na cara -- com razao: seria uma piscada que nao pisca.
	//
	// `TresTremores` e o que sobra e e literal do mesmo proc: `Quake()`, `spawn Quake()` no Max Power e
	// tres `Quake()` seguidos no True Max Power (`:224-226`). Os 15,0 s sao os do `SSJ2Cinematic.dm`
	// contra os ~20 s do DM do Heran -- a diferenca e a cauda que encolhe, e ela ja esta modelada acima.
	// ==============================================================================================================
	/// <summary>
	/// O PISO DE CENA das formas que o DM troca sem cena nenhuma -- 2,5 s.
	///
	/// Mesmo numero do <see cref="SurtoDoFrost"/> e pela mesma origem (`fd_burst_fx`, `spawn(25)`), mas
	/// constante PROPRIA: aquela e "quanto dura o surto do Frost Demon" e esta e "o minimo que uma
	/// transformacao pode durar aqui". Fundi-las faria mexer no surto do Frost mover a cena do
	/// Namekuseijin, o que ninguem espera.
	/// </summary>
	private const double SurtoInstantaneo = 25 / TempoDoDm.TiquesPorSegundo;

	/// <summary>
	/// SUPER NAMEKUSEIJIN. `emit_Sound('chargeaura.wav')`, o flash branco do `animate`, o
	/// `createDustshock(loc,3)` e o `createCrater(loc,5)` -- tudo no mesmo instante no original.
	/// </summary>
	public static readonly Cinematica SuperNamek = SurtoCurto(
		"snamek", "", SurtoInstantaneo,
		// `to_chat`: "Power up past two million and let the sparks fly, baby!" (namekian.dm:39) e a
		// promessa da skill; a fala da transformacao em si o DM nao escreve.
		"Deixa as faíscas voarem!",
		"o corpo cresce e endurece, e o poder verde que estava guardado sai inteiro de uma vez.");

	/// <summary>MAX POWER (Heran). A espinha Saiyajin com a largada dos tremores -- ver o bloco acima.</summary>
	public static readonly Cinematica Heran1 = EspinhaSaiyajin(
		"heran1", "", Largada.TresTremores,
		// "*A great wave of power emanates from [src] as they unleash their full power!*" (:171)
		"uma onda de poder se espalha: ele está soltando tudo o que tem.",
		// "*[src]'s hair stands on end and grows!*" (:180)
		"o cabelo se ergue e cresce, e o corpo para de guardar qualquer coisa.");

	/// <summary>TRUE MAX POWER (Heran). Mesma espinha, faisca VERMELHA -- `Electric_Red.dmi`.</summary>
	public static readonly Cinematica Heran2 = EspinhaSaiyajin(
		"heran2", "", Largada.TresTremores,
		// "*A great wave of power emanates from [src] as they unleash their true power!!!*" (:257)
		"uma onda de poder ainda maior se abre: isto não era o máximo dele.",
		// "*Red sparks begin to burst around [src]!*" (:263)
		"faíscas vermelhas começam a estourar em volta dele.");

	/// <summary>FORMA ALIEN (2x). `createShockwavemisc(loc,1)` + `createCrater(loc,5)`.</summary>
	public static readonly Cinematica Alien1 = SurtoCurto(
		"alien1", "", SurtoInstantaneo,
		// A skill so diz "Transform into the peak of your species!!" (alien.dm:27) -- e o que ele grita.
		"O auge da minha espécie!",
		"a forma se refaz em volta do corpo: é isto que a espécie dele guarda pro fim.");

	/// <summary>FORMA ALIEN FINAL (4x). `createShockwavemisc(loc,2)` -- a onda e maior, e so.</summary>
	public static readonly Cinematica Alien2 = SurtoCurto(
		"alien2", "", SurtoInstantaneo,
		"E ainda tem mais.",
		"o segundo e último degrau se fecha -- dobro do que ele já era.");

	// ==================================================================================
	// A FURIA EXTREMA -- `mob/proc/AngerCinematic()`, `Code/Modules/CombatMechanics/Murder.dm:136-163`
	// ==================================================================================

	/// <summary>
	/// A COR DO CLARAO DE RAIVA -- `#ff2a2a`, literal de `Murder.dm:149`.
	///
	/// ============================ POR QUE ELA E ESCRITA E NAO DERIVADA ============================
	/// Toda outra chama deste arquivo sai do <see cref="FormaDef.Aura"/> (ver <see cref="Efeito.AuraGrande"/>:
	/// *"a aura da propria transformacao q vc ta virando"*). A furia **nao tem forma** -- ela e o
	/// acontecimento que as vezes leva a uma --, entao nao ha de onde derivar: o DM escreve o hexa na
	/// mao, uma vez, e este e o unico lugar do port que o repete.
	///
	/// E ela e o ultimo uso vivo da `Aurabigcombined.dmi` COMO ARTE DE COR PROPRIA. Nas cinematicas de
	/// transformacao o dono trocou aquela folha pela aura da forma; aqui nao havia troca possivel, e
	/// e por isso que a excecao existe sem virar excecao de ninguem mais.
	/// ========================================================================================
	/// </summary>
	public const string CorDaFuria = "ff2a2a";

	/// <summary>
	/// QUANTO TEMPO ATE A CENA DE FURIA PODER TOCAR DE NOVO -- o `rageCinematicCD` do DM
	/// (`Murder.dm:140`: `world.time + 600`, e tique e DECISSEGUNDO -> 60,0 s).
	///
	/// ============================ E ELE NAO E O PRAZO DA RAIVA ============================
	/// A raiva dura 120 s (`GameServer.SegundosDeRaiva`, o `rageExpire`); a CENA se recarrega em 60.
	/// Sao dois relogios de proposito e no DM tambem: a raiva pode ser prolongada varias vezes dentro
	/// da mesma janela (cada amigo que cai reinicia o prazo), e sem uma recarga propria a cena tocaria
	/// a cada morte de uma briga em grupo.
	///
	/// Mora no Core, e nao ao lado do `SegundosDeRaiva` no servidor, pelo mesmo motivo da
	/// <see cref="PiscadaMinima"/>: e prazo de CENA, e a bancada precisa mede-lo sem recopiar.
	/// ==============================================================================
	/// </summary>
	public const double SegundosEntreFurias = 60;

	/// <summary>
	/// A CINEMATICA DA FURIA EXTREMA -- 5,0 s, e ela e a UNICA cena deste arquivo que nao pertence a
	/// forma nenhuma.
	///
	/// ============================ POR QUE ELA CABE NO MOTOR QUE JA EXISTE ============================
	/// Foi a primeira pergunta, e a resposta se mede: dos gestos do `AngerCinematic()` --
	/// `createShockwavemisc`, `createDustmisc`, `Quake()`, `createCrater`, o overlay de aura, os dois
	/// `emit_Sound`, a musica e a linha de chat -- **nenhum e novo**. Todos ja tem canal no tocador
	/// (<see cref="Jandirus.Client.Transformacao"/>), e tres deles (a pedra do chao, o funil da cratera
	/// e o rumor de camera) so existem la. Escrever um segundo tocador significaria uma segunda
	/// implementacao do chao solto -- 250 linhas -- pra desenhar exatamente a mesma coisa.
	///
	/// O QUE ELA **NAO** USA e o que a distingue: nao ha forma, entao nao ha cabelo, nem degrau, nem
	/// escada, nem clarao de tela. E, sobretudo, ela **nao prende o corpo**.
	///
	/// ============================ O CORPO NAO PARA, E ISSO E DO DM ============================
	/// `AngerCinematic()` abre com `set waitfor = 0` e o comentario do original diz o porque com todas
	/// as letras: *"Non-blocking so it never freezes the player mid-fight"*. E a diferenca de natureza
	/// entre as duas cenas -- transformacao e uma coisa que o jogador ESCOLHE fazer (e o dono decidiu
	/// que ela para o corpo o tempo inteiro), furia e uma coisa que ACONTECE com ele, no meio de uma
	/// briga, por causa de um golpe que outra pessoa deu. Prender aqui seria punir o enlutado.
	///
	/// Entao <see cref="Cinematica.SegundosPreso"/> e ZERO, e o tocador nem chega a trancar (ver o
	/// `_Ready` la: a tranca e a pose travada so nascem com prazo > 0). Nao ha "prende e solta no
	/// quadro seguinte".
	///
	/// ============================ E ELA TEM UM BEAT QUE `Assumir`, SEM ASSUMIR NADA ============================
	/// Parece contradicao e nao e: <see cref="Efeito.Assumir"/> e **a virada da cena** -- o instante em
	/// que ela entrega o que veio entregar --, e "a forma fica" e o que isso significa QUANDO HA FORMA.
	/// Aqui a virada e a erupcao: `createCrater(loc,2)` + `Quake()` + `powerup.wav`, `Murder.dm:158-160`.
	///
	/// E ele nao esta ali por gosto: e o beat da virada que o funil <see cref="Cinematica.Beats"/> usa
	/// pra por a cratera e a poeira. Sem ele esta cena perderia a cratera que o DM abre -- e ganha-la
	/// por um caminho proprio seria reabrir exatamente o campo livre que aquele funil fechou.
	///
	/// ============================ A CRATERA E A PEQUENA, E ISSO TAMBEM SE MEDE ============================
	/// `createCrater(loc, 2)` = `BurntCrater` (`craters.dm:6`), contra o `createCrater(loc, 3)` =
	/// `BigCrater` da cena do SSJ1 (`SSJCinematic.dm:83`). No port a escolha e derivada de
	/// `Catalogo.NasceDaRaiva(forma)`, e sem forma ela devolve falso -- a cratera pequena, que e a
	/// certa. A derivacao acerta sozinha; nao houve o que escrever.
	///
	/// ============================ A POEIRA DO DM VIROU PEDRA, E ELA CONTINUA SENDO A DO DM ============================
	/// O original solta `createDustmisc(loc, rand(1,3))` em cada um dos seis ciclos -- e `s=2` e o
	/// `/obj/meff/Rising`, a pedra que sobe (`dusts.dm:19`). Aqui isso e o
	/// <see cref="Cinematica.OChaoSeSolta"/>, que corre por baixo da cena inteira: sem forma no
	/// catalogo, `NaoSeSobePraEla` e falso e o chao se solta -- que e o lado certo do erro descrito la.
	///
	/// A NUVEM (`Efeito.Poeira`) nao esta escrita em beat nenhum, de proposito: ela e da cratera, e
	/// quem a poe e o funil. Ver <see cref="Efeito.Poeira"/>.
	///
	/// ============================ FORA DA <see cref="Todas"/>, E POR CONSTRUCAO ============================
	/// Aquela lista responde "que cena cada FORMA tem" -- ela alimenta o <see cref="Curtas"/> (que
	/// precisa do beat que assume pra reescalar o relogio, e reescalar uma furia nao quer dizer nada) e
	/// a bancada que cobra "toda forma tem cena, e cena propria". Uma cena sem forma dentro dela seria
	/// uma entrada que nenhuma daquelas perguntas sabe responder.
	///
	/// A furia tambem nao tem os tres degraus (<see cref="DegrauDeCena"/>): nao ha maestria de ficar
	/// com raiva. Ela toca inteira ou nao toca -- ver `GameServer.AmigoAbatido`, que e quem decide.
	/// =========================================================================================================
	/// </summary>
	public static readonly Cinematica Furia = new()
	{
		// SEM FORMA, e a string vazia e o jeito de dizer isso sem inventar um id que o catalogo nao
		// tem: `Catalogo.Def("")` e nulo, e as duas propriedades derivadas desta classe
		// (`OChaoSeSolta`, `OCeuDescarrega`) ja tratam o nulo.
		Forma = "",

		// O TEMA DA RAIVA, e ele **nao** e o marcador de estreia que o campo `Musica` costuma ser (ver
		// la): a furia nao tem primeira vez guardada em save nenhum -- o que impede o tema de virar
		// toque de celular e a recarga de `SegundosEntreFurias`. O arquivo e o mesmo do DM
		// (`Murder.dm:144`), e ele ja estava no disco deste port.
		//
		// QUEM O ABAFA CONTINUA SENDO A HIERARQUIA: `emit_RageMusic` toca num canal proprio *"so a
		// dedicated rage channel so a later transformation theme silences it (transform wins)"*, e o
		// `duck_battle_music` da vez calava o combate. As duas coisas sao a `AudioDirector.Camada.Raiva`,
		// que nasceu entre `Combate` e `Transformacao` exatamente por isto -- nao ha canal novo.
		Musica = "Dragon Ball Z - Gohan's Anger Theme   Epic Rock Cover.mp3",

		// ZERO. Ver o bloco do corpo la em cima -- `set waitfor = 0`, `Murder.dm:137`.
		SegundosPreso = 0,

		Beats =
		[
			// `emit_Sound('chargeaura.wav')` (`:141`), a musica (`:144`), a linha vermelha do chat
			// (`:145`) e o overlay de aura vermelha (`:146-150`), todos no instante zero. O primeiro
			// dos seis `createShockwavemisc(loc, rand(2,4))` cai aqui tambem (`:152`).
			//
			// A NARRACAO E A DO DM, traduzida: *"[src]'s fury erupts, blasting shockwaves out in every
			// direction!"*. Ela e narracao e nao fala -- ninguem DISSE isso --, entao vai pro chat e
			// nao pro balao. Ver o `Disparar` do tocador.
			new(0.0, Efeito.AuraGrande | Efeito.AnelDeChoque, Som: "chargeaura",
				Narra: "a fúria irrompe, e ondas de choque saem em todas as direções!"),

			// OS SEIS CICLOS, `sleep(5)` = 0,5 s entre eles (`:151-157`). O `Quake()` sai nos ciclos
			// PARES (`if(cyc % 2 == 0)`), ou seja no 2o, 4o e 6o -- 0,5 / 1,5 / 2,5 s.
			//
			// UM ANEL POR CICLO e nao os varios que o DM espalha pelo `view()`: o `Efeito.AnelDeChoque`
			// do port e o `CombatFx.Onda`, um anel PROCEDURAL que abre de 32 a 512 px -- ele ja cobre
			// sozinho o raio que la precisava de uma varredura de turfs a 18%. Ver `Efeito.AnelDeChoque`.
			new(0.5, Efeito.AnelDeChoque | Efeito.Tremor),
			new(1.0, Efeito.AnelDeChoque),
			new(1.5, Efeito.AnelDeChoque | Efeito.Tremor),
			new(2.0, Efeito.AnelDeChoque),
			new(2.5, Efeito.AnelDeChoque | Efeito.Tremor),

			// A VIRADA (`:158-160`): `createCrater(loc,2)`, `Quake()`, `powerup.wav`. A cratera e a
			// poeira NAO estao escritas aqui -- o funil da `Cinematica.Beats` as poe, que e a regra.
			new(3.0, Efeito.Assumir | Efeito.Tremor, Som: "powerup"),

			// `sleep(20)` = 2,0 s ate o overlay sair (`:161-163`). Este e o UNICO beat de cauda, como
			// em toda cena deste arquivo, e ele carrega a poeira baixando -- o que o funil deixa
			// passar depois da virada. Com ele em 4,0 s a cena mede 5,0 (`Segundos` = ultimo + 1,0),
			// que e exatamente o instante em que o DM tira a aura vermelha.
			new(4.0, Efeito.Poeira),
		],
	};

	// ==================================================================================
	// AS CONSULTAS
	// ==================================================================================

	public static readonly Cinematica[] Todas =
	[
		// as nove escritas a mao, cada uma vinda de um `*Cinematic.dm` proprio, mais a do macaco
		Ssj1, Ssj2, Ssj3, Ssj4, Blue, UiSign, UiPerfected, Destroyer, UltraEgo, Oozaru,

		// as 23 que faltavam, saidas das fabricas acima
		Grade2, Grade3, Ssj4FullPower, Ssj4LimitBreaker, FutureSsj,
		Wrathful, CType, Legendary,
		PrimalCType, PrimalLegendary, PrimalLegendary2, PrimalLegendary3,
		PrimalLegendary4, PrimalLegendary4FullPower, PrimalLegendary4LimitBreaker,
		Ssg, BlueEvolution, RoseSsg, Rose, Rose2,
		Mistico, Beast,

		// a escada do Frost Demon -- as quatro supressoes, a base e as duas evolucoes
		Frost1, Frost2, Frost3, Frost4, Frost5, Frost6, Frost7,

		// as tres linhas raciais: uma entrada Namekuseijin, duas Heran, duas Alien
		SuperNamek, Heran1, Heran2, Alien1, Alien2,
	];

	/// <summary>
	/// A CENA MAIS LONGA DO JOGO, em segundos.
	///
	/// ============================ EXISTE PRA REDE DA TRANCA, E E DERIVADA ============================
	/// O `Transformacao.PrazoMaximoPreso` -- a rede que solta o corpo na marra quando nenhuma cena o
	/// soltou -- era um literal `45.0`, com o comentario "o prazo da cena mais longa do jogo com
	/// folga". Era verdade enquanto a cena mais longa tinha 35 s. Com a do SSJ3 restaurada aos 140 s
	/// do DM, aquele 45 passaria a CORTAR a cena mais longa do jogo pela metade -- e o sintoma seria o
	/// pior possivel de diagnosticar: o jogador ganharia o controle no meio do grito, com um erro no
	/// log acusando um vazamento que nao existe.
	///
	/// Derivar mata a classe inteira desse defeito: mexer no relogio de qualquer cena move a rede
	/// junto, e ninguem precisa lembrar de um segundo numero em outro arquivo.
	/// ==========================================================================================
	/// </summary>
	public static readonly double CenaMaisLonga = Todas.Max(c => c.Segundos);

	/// <summary>A cena desta forma, se ela tiver uma. Nulo = a forma nao tem cinematica propria.</summary>
	public static Cinematica? De(string? formaId) =>
		formaId == null ? null : Array.Find(Todas, c => c.Forma == formaId);

	/// <summary>
	/// A CENA DESTA FORMA. Nulo = a forma nao tem cena, e isso e um DEFEITO que a bancada acusa
	/// (`RoboDeForma`: "toda forma tem cena"), nao um estado normal.
	///
	/// ============================ O FALLBACK POR `Ordem` FOI DELETADO ============================
	/// Ele existia porque so nove das formas tinham cena, e devolvia a cena de OUTRA forma pelo
	/// degrau equivalente da escada comum. Hoje todas tem cena propria (as 23 que faltavam saem das
	/// fabricas la de cima), e um fallback so serviria pra ESCONDER a forma nova que alguem
	/// acrescentar sem escrever cena -- ela estrearia com o tema e as falas de outra, e nada
	/// acusaria.
	///
	/// Devolver nulo e melhor porque o nulo tem quem o pegue: a bancada reprova antes de o jogo
	/// rodar, e `NoDegrau` transforma o nulo em "transformacao instantanea" -- o pior caso vira uma
	/// forma sem cena, e nao uma forma com a cena errada.
	/// =========================================================================================
	/// </summary>
	public static Cinematica? Para(FormaDef d)
	{
		if (De(d.Id) is { } propria) return propria;

		// ============================ A LINHA DO MACACO INTEIRA USA UMA CENA SO ============================
		// E a UNICA sobrevivente do compartilhamento, e ela e do dono: *"oozaru n tem esse efeito de
		// rocks nem de particulas, o resto da cinematica do oozaru pode deixar"* -- a cena do macaco
		// nao foi mexida nesta leva, e o `oozaru_dourado` continua nela.
		//
		// Pela LINHA, e nao por uma lista de dois ids: um Oozaru novo (o `Oozaru LSSJ.dmi` do DU esta
		// convertido e sem entrada no catalogo) ja nasce com a cena certa. E a mesma escolha do
		// `Catalogo.Folha`, que tambem deriva da linha.
		// ============================================================================================
		if (d.Linha == LinhaDeForma.Oozaru) return Oozaru;

		return null;
	}

	// ==================================================================================
	// OS TRES DEGRAUS -- estreia / encurtada / instantanea
	// ==================================================================================

	/// <summary>
	/// A MAESTRIA QUE DISPENSA A CENA. Do dono, textual: *"apartir de 50% de maestria a
	/// transformaçao vira instantanea"*.
	///
	/// NAO e o <see cref="Catalogo.Grade2Pct"/>, que por acaso vale o mesmo 50: aquele decide se o
	/// Grade 2 abriu, este decide se ha espera. Amarrar os dois faria mexer no balanceamento dos
	/// grades mudar, em silencio, o ritmo de TODAS as transformacoes do jogo.
	/// </summary>
	public const double MaestriaQueDispensaCena = 50;

	/// <summary>
	/// QUANTO O CABELO SEGURA CADA LADO DO PISCAR -- o `sleep(rand(3,10))` de `SSJCinematic.dm:13`,
	/// `:17`, `:21` e `:25`, em segundos.
	///
	/// ============================ SAO OS TIQUES DO DM, EM DECISSEGUNDO ============================
	/// 3 e 10 tiques = **0,3 s e 1,0 s**. Aqui estava `3.0/12.0` e `10.0/12.0` (0,25 e 0,83), pela
	/// mesma leitura errada que encurtou este arquivo inteiro em 20% -- ver <see cref="TempoDoDm"/>,
	/// que e o unico lugar onde a unidade esta explicada e provada.
	///
	/// Ficam aqui e nao no tocador porque sao do ROTEIRO -- a cadencia com que a cena do Super
	/// Saiyajin pisca --, e o cliente so os obedece. Escritos la, a bancada teria que recopiá-los pra
	/// medir, que e como um numero de porte vira dois numeros que discordam.
	///
	/// ============================ E O SORTEIO E O EFEITO ============================
	/// Um intervalo FIXO daria um estroboscópio de metrônomo. O `rand` do original nao e descuido: um
	/// cabelo que troca em 0,3 s e depois segura 1,0 s le como o corpo BRIGANDO pra manter a forma,
	/// que e o que a cena conta. Media de 0,65 s -> a cena do SSJ1 (25,0 s ate o `Assumir`) pisca umas
	/// 38 vezes, e a encurtada dela (10,0 s) umas 15.
	/// ======================================================================
	/// </summary>
	public const double PiscadaMinima = 3 / TempoDoDm.TiquesPorSegundo,
						PiscadaMaxima = 10 / TempoDoDm.TiquesPorSegundo;

	/// <summary>
	/// QUANTO O CEU SEGURA ENTRE UM RAIO E O PROXIMO, na cena que <see cref="Cinematica.OCeuDescarrega"/>.
	///
	/// ============================ A CONTA VEM DO DM, E NAO DE UM RITMO ESCOLHIDO ============================
	/// `SSJCinematic.dm:51-52` varre o `view(src)` inteiro e sorteia DUAS vezes por tile:
	/// `if(prob(5)) ... createLightningmisc(T,4)` e `else if(prob(5)) ... (T,2)`. Com o
	/// <see cref="TilesDoTremorCheio"/> (que E o alcance daquele mesmo `view(src)`, ja medido e
	/// justificado la) sao 13x13 = 169 tiles, e a esperanca sai em 169 x 0,05 = 8,4 mais
	/// 169 x 0,95 x 0,05 = 8,0 -- **~16,5 descargas por cena**.
	///
	/// A cena da estreia tem 25,0 s ate a forma ficar, e 16,5 descargas nela dao um intervalo MEDIO de
	/// 1,5 s. E e exatamente a media de 1,0 e 2,0.
	///
	/// ============================ O PISO TAMBEM E DO DM, E ELE PROTEGE O DESENHO ============================
	/// 1,0 s e o chao do `spawn(rand(10,150))` daquelas mesmas linhas (10 tiques = 1,0 s): no original
	/// tambem nao cai raio nenhum antes do primeiro segundo.
	///
	/// E ele resolve, de graca, o unico limite duro do desenho: o `ClimaNaTela` tem **um** node de raio,
	/// e cada risco fica visivel 0,333 s (`_idadeDoRaio += delta * 3`). Duas descargas no mesmo instante
	/// mostrariam UMA. Com o piso em 1,0 s ha tres vezes a duracao do risco entre uma e a seguinte --
	/// nao ha sobreposicao possivel, e nao foi preciso inventar um segundo quad pra isso.
	///
	/// ============================ E POR QUE SORTEADO, COMO A PISCADA ============================
	/// Mesmo argumento da <see cref="PiscadaMinima"/>, e o proprio jogo ja o aplicava a faisca
	/// (`RaiosDaForma`: *"o mesmo estalo repetido a cada 1,3..2,7 s vira metronomo"*). Intervalo fixo
	/// em 1,5 s daria dezessete raios em compasso, que le como efeito de jogo; tempestade nao tem
	/// compasso.
	/// ====================================================================================================
	/// </summary>
	public const double DescargaMinima = 1.0, DescargaMaxima = 2.0;

	/// <summary>
	/// QUANTO O <see cref="Efeito.BanhoDeCor"/> LEVA PRA ESCOAR.
	///
	/// No DM sao dois numeros: `animate(src, time=6, ...)` enche em 6 tiques (0,6 s) e
	/// `spawn(12) color = null` devolve aos 12 (**1,2 s**) -- `supersaiyanbuff.dm:549-550` e as tres
	/// irmas. Aqui e UM, porque o `CharacterVisual.Banhar` decai LINEAR do pico: o banho ja nasce
	/// cheio (o `animate` do BYOND entra num corpo que acabou de gritar, e meio segundo de subida
	/// num efeito de um segundo e o efeito quase inteiro) e escoa pelo prazo do `color = null`.
	///
	/// ESTAVA 1,0 -- e o proprio arquivo ja se contradizia: o `Efeito.BanhoDeCor` escreve, la em
	/// cima, que `animate(time=6)` + `spawn(12)` sao "0,6 s / 1,2 s". A conta certa ja estava aqui
	/// dentro; o que faltava era ela chegar ate a constante. Ver <see cref="TempoDoDm"/>.
	///
	/// Fica no Core e nao no tocador pela mesma razao da <see cref="PiscadaMinima"/>: e um prazo do
	/// ROTEIRO, e a bancada precisa medi-lo sem recopiar.
	/// </summary>
	public const double SegundosDoBanho = 12 / TempoDoDm.TiquesPorSegundo;

	// ==================================================================================
	// O TREMOR DE CAMERA DA CINEMATICA
	// ==================================================================================

	/// <summary>
	/// COMO A CINEMATICA SACODE A CAMERA -- e por que os numeros NAO sao os do combate.
	///
	/// O dono: *"o camera shake das transformacoes ta mt rapido, deveria durar mais tempo e tremer um
	/// pouco mais devagar"*. Sao duas queixas separadas, entao sao dois numeros separados:
	///
	/// <b>Queda</b> (forca por segundo): 8, contra os 40 do impacto. O solavanco de um beat de forca
	/// 6 passou de 0,15 s pra 0,75 s. Um soco QUER ser seco, ele acompanha um golpe que ja terminou;
	/// um chao abrindo debaixo de quem esta virando Super Saiyajin nao acaba em um sexto de segundo.
	///
	/// <b>Cadencia</b> (segundos que a camera segura cada rumo): 0,08, contra o 1/60 do impacto. Sao
	/// ~12 trocas de direcao por segundo em vez de ~60, com a camera CAMINHANDO entre elas -- e essa
	/// e a diferenca entre um chao roncando e um zumbido.
	///
	/// ============================ POR QUE ELES MORAM NO CORE ============================
	/// Mesmo motivo do <see cref="Catalogo.ForcaDoContorno"/>: a bancada mede a duracao e a cadencia
	/// que o jogador ve, e nao pode fazer isso repetindo os literais do cliente -- um teste que
	/// reescreve o numero que ele testa aprova qualquer coisa. O tremor tambem e ROTEIRO de cena
	/// (nasce do <see cref="Efeito.Tremor"/>, que ja mora aqui), e nao um detalhe de renderizacao.
	/// ================================================================================
	/// </summary>
	public const float QuedaDoTremor = 8f, CadenciaDoTremor = 0.08f;

	/// <summary>
	/// A AMPLITUDE dos tremores da cena, em pixels de camera.
	///
	/// <see cref="ForcaDoTremor"/> e o solavanco de PERTO; <see cref="ForcaDoTremorDeLonge"/> e o que
	/// chega pelo chao ao resto do planeta -- metade. <see cref="RumorDaCena"/> e o chao roncando
	/// DO COMECO AO FIM da cena: baixo de proposito, porque ele e o fundo por cima do qual os outros
	/// dois batem.
	///
	/// ============================ O RUMOR DEIXOU DE SER "DA AURA BASE" ============================
	/// Ele se chamava `RumorDaAuraBase` e so existia enquanto a aura emprestada do Oozaru estivesse
	/// acesa -- ou seja, numa cena de 34 e em nenhuma outra. O dono: *"ta estranho ele tremendo e
	/// parando, tremendo e parando"*, e a conta explica a queixa: um beat de <see cref="Efeito.Tremor"/>
	/// dura `Forca/Queda` = 0,75 s, e a cena do SSJ3 tem 22 beats de tremor em 119,5 s. Sao 16,5 s de
	/// camera viva e 103 s de camera PARADA -- 86% da cena.
	///
	/// Agora o rumor e o piso de TODA cena, e os beats sao os PICOS por cima dele. Nada foi inventado:
	/// e o mesmo numero, no mesmo `Sacudir`, com a mesma queda e a mesma cadencia -- so deixou de ter
	/// uma condicao que o desligava em 33 das 34 cenas.
	///
	/// ============================ POR QUE 1,6 E NAO MAIS ============================
	/// O dono: *"116 s de camera sacudindo forte enjoa"*. 1,6 px sao 27% do pico de 6 px, entao o beat
	/// continua sendo 3,75x o fundo -- ele nao se perde no rumor. E o pico leva `(6-1,6)/8` = 0,55 s pra
	/// AFUNDAR no rumor em vez de morrer no zero, o que e a diferenca entre um solavanco que termina e um
	/// solavanco que assenta.
	/// ==========================================================================================
	///
	/// ============================ "DE LONGE" ERA "NAO SOU EU", E ISSO ESTAVA ERRADO ============================
	/// A distincao aqui era `souEu ? Forca : ForcaDeLonge`: quem estava do lado do corpo levava metade
	/// do solavanco de quem virou. O DM diz o contrario -- `Quake()` (`Ascension.dm:7-10`) varre
	/// `view(src)` e chama `client.Quake()` com o MESMO `rand(-8,8)` pra todo mundo. Nao ha meia
	/// tremida no original: dentro do campo de visao, a tela de todos sacode igual.
	///
	/// Entao a metade deixou de significar "voce nao e o dono da cena" e passou a significar
	/// DISTANCIA (ver <see cref="RaioDoTremorCheio"/>): perto e cheio pra todos, e o resto do planeta
	/// sente o eco. O dono: *"o camera shake tb afeta todos no planeta"* -- afetar mesmo, e nao afetar
	/// so quem esta olhando.
	/// ====================================================================================================
	///
	/// ============================ A AMPLITUDE NAO MUDOU NESTA PASSADA ============================
	/// A queixa do dono foi de velocidade e duracao, e mexer na forca junto teria confundido as tres
	/// coisas numa sensacao so. Ela veio pro Core porque a duracao de um solavanco e
	/// `Forca / QuedaDoTremor`: com a forca escondida no cliente, a bancada mediria os 0,75 s sem ter
	/// como dizer de onde eles saem -- e voltaria a ser um literal copiado.
	/// =========================================================================================
	/// </summary>
	public const float ForcaDoTremor = 6f, ForcaDoTremorDeLonge = 3f, RumorDaCena = 1.6f;

	/// <summary>
	/// ATE ONDE O TREMOR CHEGA CHEIO -- em pixels de mundo, medidos do corpo que esta virando.
	///
	/// ============================ E O `view(src)` DO DM, EM PIXEL ============================
	/// `Quake()` sacode `for(var/mob/M in view(src))` (`Ascension.dm:8`). `view()` sem numero usa o
	/// alcance do cliente, e o `world` do original nunca escreve `view` (`Globals/World.dm:2-13`),
	/// entao vale o padrao do BYOND: 6 tiles de raio. Com `icon_size = 32` isso da 192 px.
	///
	/// Escrever "192" aqui teria envelhecido calado no dia em que o tile mudasse de tamanho -- por
	/// isso sao os SEIS tiles que estao escritos, e o pixel e derivado deles.
	/// ==================================================================================
	/// </summary>
	public const int TilesDoTremorCheio = 6;

	/// <inheritdoc cref="TilesDoTremorCheio"/>
	public const float RaioDoTremorCheio = TilesDoTremorCheio * Jandirus.Core.World.ZoneCollision.TileSize;

	/// <summary>
	/// QUANTO DA AMPLITUDE SOBRA PRA QUEM ESTA LONGE -- a fracao, e nao um terceiro numero.
	///
	/// Existe porque sao DOIS tremores com amplitudes diferentes (o pico do beat e o rumor continuo da
	/// cena) e a queda com a distancia e a MESMA regra pros dois. Escrever um "rumor de longe" ao lado
	/// do <see cref="RumorDaCena"/> seria a mesma decisao anotada em dois lugares -- e no dia em que a
	/// metade virasse um terco, um deles ficaria pra tras.
	/// </summary>
	public const float PesoDoTremorDeLonge = ForcaDoTremorDeLonge / ForcaDoTremor;

	/// <summary>
	/// QUANTO A CENA ENCURTADA GUARDA DA CHEIA, e os limites disso.
	///
	/// O fator sozinho nao serve: a cena do SSJ3 assume aos 140 s (0,4 disso seriam 56 s, que nao
	/// e "encurtada", e a mesma espera de novo) e os surtos assumem aos 2,5 s (0,4 disso seria 1,0 s,
	/// que nao chega a ler como transformacao -- e cairia no piso do 1,0 s que a bancada exige do beat
	/// que assume). O teto e o piso sao o que faz as 34 cenas caberem na mesma faixa de leitura sem
	/// ninguem escrever 34 numeros a mao.
	///
	/// ============================ O TETO E 10 s, E E DECISAO DO DONO ============================
	/// Era 5. Ele subiu junto com a restauracao dos prazos do DM, e um nao vale sem o outro: com as
	/// cheias de volta ao original (a do SSJ1 em 25,0 s, a do SSJ3 em 140), um teto de 5 s faria a
	/// encurtada de QUASE TODA cena bater no limite -- e cenas de 12, 18 e 116 segundos sairiam todas
	/// com exatamente o mesmo comprimento no degrau do meio. O degrau que o dono chama de "ainda assim
	/// vai ser lenda" perderia o que o distingue de um efeito sonoro.
	///
	/// Com 10 s a encurtada volta a ter faixa: sao SETE comprimentos distintos (2,0 / 2,4 / 5,8 / 6,0 /
	/// 6,4 / 8,8 / 10,0), com quatro grupos encostando no teto (SSJ1, as divinas de meio de escada, o
	/// SSJ3 e as quatro Legendary) --
	/// ou seja, a encurtada volta a ter a PROPORCAO da cheia, que e o que o fator existe pra dar.
	/// =====================================================================================
	/// </summary>
	public const double FatorDaCurta = 0.4, MinimoDaCurta = 2.0, MaximoDaCurta = 10.0;

	/// <summary>
	/// QUAL DOS TRES DEGRAUS ESTA TRANSFORMACAO E.
	///
	/// ============================ QUEM DECIDE E O SERVIDOR, E POR ISSO ELE MORA NO CORE ============================
	/// O cliente TEM a maestria (ela viaja no `S2C.Atributos`), e mesmo assim ele nao pode decidir:
	/// aquela ficha e a LENTA, sai de tres em tres segundos, e transformar-se logo depois de cruzar os
	/// 50% leria a maestria de antes. Pior, o corpo preso e uma regra de jogo -- deixar o cliente
	/// escolher por quanto tempo ele proprio fica parado e entregar a tranca pra quem ela prende.
	///
	/// Entao quem decide e o <see cref="Jandirus.Server.GameServer"/>, que ja tem a maestria na mao no
	/// instante exato do `Entrar()`. O que viaja e o RESULTADO (um byte no `S2C.Forma`), e nao a
	/// maestria -- e as duas pontas leem o mesmo <see cref="NoDegrau"/> pra chegar no mesmo
	/// <see cref="Cinematica.SegundosPreso"/>. Nao ha como divergirem porque nao ha duas contas.
	/// ========================================================================================================
	/// </summary>
	/// <param name="d">A forma que se esta assumindo. Nulo ou base = nada acontece.</param>
	/// <param name="estreia">O `Entrar()` disse que esta e a PRIMEIRA vez desta forma?</param>
	/// <param name="maestriaDaForma">Maestria NA FORMA ALVO -- e nao na anterior. O dono fala da
	/// forma que se esta assumindo: "ate vc ter 50% de maestria DE UMA FORMA, ELA ainda vai ter um
	/// tempo pra se transformar".</param>
	public static DegrauDeCena Degrau(FormaDef? d, bool estreia, double maestriaDaForma)
	{
		if (d == null || d.Id == Catalogo.IdBase) return DegrauDeCena.Nenhuma;

		// ============================ A EXCECAO DO OOZARU, DERIVADA DA LINHA ============================
		// O dono: *"isso serve pra TODAS as formas do jogo, menos as oozaru e golden oozaru"*.
		//
		// E ela nao e capricho: a cena do macaco NAO e comemoracao de estreia, ela E a transformacao --
		// a aura acesa e o tremor sao o que ARRANCA o bicho de dentro do corpo (ver `Cinematicas.Oozaru`,
		// que ja divergia da regra "cena so na primeira vez" pelo mesmo motivo). Sem ela um lutador de
		// 32 px vira um bicho de 96 px em um quadro, e isso le como sprite errado, nao como transformacao.
		//
		// PELA LINHA e nao por dois ids, do mesmo jeito que o `Para` acima: o `Oozaru LSSJ.dmi` do DU
		// esta convertido e sem entrada no catalogo, e no dia em que ele virar a terceira entrada ja
		// nasce com a regra certa. Uma lista de ids seria o terceiro lugar a esquecer.
		// =======================================================================================
		if (d.Linha == LinhaDeForma.Oozaru) return DegrauDeCena.Estreia;

		if (estreia) return DegrauDeCena.Estreia;
		return maestriaDaForma >= MaestriaQueDispensaCena ? DegrauDeCena.Nenhuma : DegrauDeCena.Curta;
	}

	/// <summary>
	/// A CENA A TOCAR NESTE DEGRAU. Nulo = instantanea, sem cena e sem corpo preso.
	///
	/// E o funil unico: quem quiser saber "quanto tempo esta transformacao demora" pergunta aqui, e
	/// nao soma prazos por conta propria.
	///
	/// FORMA SEM CENA CAI EM NULO -- ou seja, em transformacao instantanea. E o pior caso certo: a
	/// bancada ja reprovou antes de o jogo subir (`Para` devolve nulo e "toda forma tem cena" acusa),
	/// e o jogador que mesmo assim chegar aqui perde a cena em vez de assistir a de outra forma.
	/// </summary>
	public static Cinematica? NoDegrau(FormaDef d, DegrauDeCena degrau) => degrau switch
	{
		DegrauDeCena.Nenhuma => null,
		DegrauDeCena.Estreia => Para(d),
		_ => Para(d) is { } cheia ? Encurtada(cheia) : null,
	};

	/// <summary>
	/// OS DEGRAUS QUE UMA CENA ATRAVESSA ATE ESTA FORMA -- da base pra cima, sem incluir a forma.
	/// E a tabela que o <see cref="Efeito.VesteDegrau"/> percorre, um degrau por beat.
	///
	/// ============================ COMECA NA BASE, SEMPRE ============================
	/// O primeiro ato do `SSJ3Cinematic.dm` e `RemoveHair()` (:12): a cena poe o personagem de volta
	/// no normal pra a fala do "estado normal" fazer sentido. Isso nao e enfeite do SSJ3 -- quando ha
	/// cinematica o `World.AoMudarForma` NAO veste nada (ele deixa o beat `Assumir` vestir no fim),
	/// entao quem estava em SSJ2 e sobe pra SSJ3 comeca a cena de cabelo dourado. Sem o degrau base,
	/// a fala "o que voce esta vendo agora e o meu estado normal" sairia por cima de um SSJ2.
	///
	/// E a base nao vem do passeio: ela e a entrada de <see cref="LinhaDeForma.Saiyajin"/> com
	/// <see cref="FormaDef.Ordem"/> 0, e por isso so aparece sozinha em quem sobe ESSA linha. As
	/// outras (GodKi, Legendary, UI...) parariam no primeiro degrau da propria linha e a cena delas
	/// nunca voltaria ao normal.
	///
	/// ============================ E O RESTO E O `Anterior` ============================
	/// Nao ha lista escrita aqui: e o mesmo <see cref="Catalogo.Anterior"/> que a escada de PODER
	/// usa, entao os grades (`ForaDoTronco`) ficam de fora -- a cena do SSJ3 passa por SSJ1 e SSJ2
	/// e nao por um Grade 3 que o jogador talvez nunca tenha destravado. Um degrau novo no meio da
	/// linha entra na cena sozinho, e a unica coisa que a cena precisa e ter beats o bastante.
	/// ==========================================================================================
	/// </summary>
	public static FormaDef[] EscadaDaCena(FormaDef d)
	{
		var abaixo = new List<FormaDef>();
		// TERMINA SEMPRE: `Anterior` so devolve entrada com `Ordem` ESTRITAMENTE menor, e o catalogo
		// e finito. Nao ha ciclo possivel, e por isso nao ha teto de voltas aqui.
		for (FormaDef? p = Catalogo.Anterior(d); p != null; p = Catalogo.Anterior(p)) abaixo.Add(p);
		abaixo.Reverse();

		FormaDef? baseDef = Catalogo.Def(Catalogo.IdBase);
		if (baseDef != null && (abaixo.Count == 0 || abaixo[0] != baseDef)) abaixo.Insert(0, baseDef);
		return [.. abaixo];
	}

	/// <summary>
	/// ============================ UMA FUNCAO QUE ENCURTA, E NAO 35 CENAS A MAIS ============================
	/// A alternativa era um segundo roteiro por forma. Ela se paga uma vez e cobra pra sempre: com as
	/// 34 cenas de hoje seriam 34 curtas escritas a mao, e o modo de falha e o de sempre neste port -- mexer no ritmo de uma cena e
	/// esquecer da irma. Pior, o casamento entre `SegundosPreso` e o beat que ASSUME teria que ser
	/// mantido a mao em dobro (e a bancada ja reprova quando os dois divergem, com razao).
	///
	/// AQUI A CURTA E DERIVADA DA CHEIA, e o que ela guarda e o que ela joga fora sao decisoes, nao
	/// sobras:
	///
	///   * **O RELOGIO COMPRIME.** Todos os beats sao multiplicados pelo mesmo `k`, escolhido pra o
	///     beat que ASSUME cair no alvo. Comprimir em vez de CORTAR beats preserva a coreografia
	///     inteira -- o piscar de cabelo do SSJ1, os tres `Quake()` seguidos do SSJ2, os feixes de
	///     chao --, que e justamente o que o dono chama de "ainda assim vai ser lenda".
	///   * **A MUSICA SAI.** Isto e do DM e nao invencao: `ssj1_music_played` / `ssj2_music_played` /
	///     `ssj4_music_played` sao vars que PERSISTEM no save, e o tema toca uma vez na vida do
	///     personagem (ver <see cref="Cinematica.Musica"/>). Toca-lo de novo a cada transformacao e o
	///     que transformaria o acontecimento em toque de celular.
	///   * **AS FALAS SAEM.** Ninguem explica pela terceira vez o que e um Super Saiyajin. A cena do
	///     SSJ3 e a UNICA em que o personagem conta o que esta fazendo -- e ela conta pra quem esta
	///     vendo pela primeira vez.
	///   * **O QUE FICOU VAZIO MORRE.** Sem fala e sem efeito, um beat e um instante em que nada
	///     acontece -- exatamente o "campo escrito e nunca lido" que este projeto nao deixa ficar. Sao
	///     dois beats na cena do SSJ3 (`Efeito.Nada` com so a fala).
	/// ==================================================================================================
	///
	/// PRE-CALCULADAS na carga da classe, e nao num cache que cresce: as unicas cenas que existem sao as
	/// dez de <see cref="Todas"/> (o `Para` sempre devolve uma delas), entao a tabela e fechada. Um
	/// `Dictionary` preenchido sob demanda seria escrita concorrente entre a linha do servidor e a do
	/// cliente no modo `--host`, que e onde este jogo passa a vida.
	///
	/// UMA DAS DEZ ENTRADAS NUNCA SAI EM JOGO: a do Oozaru, porque <see cref="Degrau"/> devolve
	/// `Estreia` pra aquela linha em qualquer maestria. Ela e calculada assim mesmo -- pular a excecao
	/// aqui poria a regra do macaco em DOIS lugares, e o segundo envelheceria calado (e a bancada
	/// perderia o unico jeito barato de conferir que a derivacao continua funcionando pra ela, caso o
	/// dono mude de ideia).
	/// </summary>
	private static readonly Dictionary<Cinematica, Cinematica> Curtas =
		Todas.ToDictionary(c => c, Encurtar);

	/// <summary>A versao encurtada desta cena. Ver <see cref="Curtas"/>.</summary>
	public static Cinematica Encurtada(Cinematica cheia) =>
		Curtas.TryGetValue(cheia, out Cinematica? pronta) ? pronta : Encurtar(cheia);

	private static Cinematica Encurtar(Cinematica cheia)
	{
		double assume = -1;
		foreach (Beat b in cheia.Beats)
			if (b.Faz.HasFlag(Efeito.Assumir)) { assume = b.Em; break; }

		// SEM O BEAT QUE ASSUME nao ha o que reescalar -- e a cena ja e defeito (a bancada reprova
		// isso desde antes, porque quem a rodasse faria a cena inteira e voltaria ao normal).
		// Devolver a cheia e o menos errado: encurtar por um numero inventado esconderia o buraco.
		if (assume <= 0) return cheia;

		// O ALVO, e o `Math.Min` no fim nao e paranoia: com uma cena que ja assumisse em menos de 2 s o
		// PISO a esticaria, e "a curta e mais curta que a cheia" -- que e a regra inteira -- deixaria de
		// valer justamente sem ninguem notar.
		double alvo = Math.Min(Math.Clamp(assume * FatorDaCurta, MinimoDaCurta, MaximoDaCurta), assume);
		double k = alvo / assume;

		var beats = new List<Beat>(cheia.Beats.Length);
		double preso = alvo;
		bool achouOAssumir = false;
		foreach (Beat b in cheia.Beats)
		{
			Beat nb = b with { Em = Math.Round(b.Em * k, 2), Fala = "", Narra = "" };
			if (nb.Faz == Efeito.Nada && nb.Som.Length == 0) continue;   // ficou vazio: morre
			// O PRAZO SAI DO BEAT JA ARREDONDADO, e nao do `alvo` cru. Sao 5 milesimos de diferenca e
			// eles importam por um motivo so: a regra e "solta o corpo NO instante em que a forma fica",
			// e uma regra com folga embutida e uma regra que ninguem consegue conferir com `==`.
			//
			// O PRIMEIRO `Assumir`, como o `assume` la de cima: uma cena com dois deles pegaria o
			// ULTIMO aqui e o PRIMEIRO no calculo do `k`, e as duas metades diriam coisas diferentes.
			if (!achouOAssumir && nb.Faz.HasFlag(Efeito.Assumir)) { achouOAssumir = true; preso = nb.Em; }
			beats.Add(nb);
		}

		return new Cinematica
		{
			Forma = cheia.Forma,
			Musica = "",
			SegundosPreso = preso,
			Beats = [.. beats],
		};
	}
}
