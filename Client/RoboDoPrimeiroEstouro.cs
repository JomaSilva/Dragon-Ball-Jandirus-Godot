using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O PRIMEIRO ESTOURO DE KI DO PROCESSO, CRONOMETRADO QUADRO A QUADRO (`--diagestouro`).
///
/// ============================ O DEFEITO QUE ELA MEDE ============================
/// O primeiro estouro de bola de ki de cada processo travava a tela: ~0,33 s com o mesmo quadro, o
/// estouro nascia, e mais ~0,08 s travado (medido em 2026-10-08 na tomada `s_ki` do trailer, com o
/// `engasgos.py`: o disco do Kienzan "pousado" em cima do alvo). Dali em diante os estouros saiam lisos.
///
/// ONDE O TEMPO IA, medido por esta bancada antes do conserto. Os numeros sao o TRABALHO do quadro (ver a
/// regua abaixo) com o cache de shader em disco VAZIO -- o de quem abre o jogo pela primeira vez, e o de toda
/// bancada e toda tomada do trailer, que rodam com o APPDATA desviado pra uma pasta nova:
///
///     o quadro do primeiro estouro ............ 346 a 382 ms   (o terceiro: 5)
///       -- a `PoeiraDeEstrago` ................ 321 ms   os TRES shaders de particula, compilados dentro
///                                                        da chamada que monta os emissores
///       -- o quad do `EstouroDeKi` ............. ~23 ms   shader compilado ao nascer o 1o material + pipeline
///       -- o anel (`CombatFx.Onda`) ............ ~25 ms   idem, e o `Impacto.gdshader` nem estava na lista
///     o quadro em que a primeira bola nasce .... 54 a 69 ms
///     o primeiro raio 66, o primeiro anel de deflexao 34, a primeira estrela do embate 33
///
/// Com o cache em disco cheio (o jogo de quem ja jogou uma vez) o primeiro estouro custava 40 a 46 ms e a
/// primeira bola 24 -- e o estouro voltava a custar 22 a 30 ms toda vez que a ultima poeira ja tinha sumido
/// e o coletor do .NET passado (o Godot libera o shader gerado quando o ultimo material dele morre).
///
/// OU SEJA: carregar o `.gdshader` no lobby nao compila nada. O shader e compilado quando o primeiro
/// MATERIAL dele nasce, a pipeline quando ele e DESENHADO pela primeira vez, e o shader de particula
/// dentro da chamada que cria o `ParticleProcessMaterial`. E nao dependia do login: com quatro segundos
/// de lobby e os 291 recursos carregados (`--lobbyteste 4 --semensaio`) o primeiro estouro custava os
/// mesmos 374 ms. O conserto e o ENSAIO do `Aquecimento` (os efeitos montados e desenhados uma vez, fora
/// da tela) mais os materiais de particula segurados pela `PoeiraDeEstrago`.
///
/// DEPOIS: o primeiro estouro custa 6 ms (o quadro inteiro, 9), de dia e de noite, com o cache vazio ou
/// cheio; a primeira bola 9 a 14, o primeiro raio 10 a 15, o anel 4 a 6, a estrela 5 a 7. O que sobra na
/// primeira bola e no primeiro raio e codigo rodando pela primeira vez dos dois lados do fio, e nao shader.
///
/// O QUE ELA MEDE E NAO COBRA: as pausas do coletor do .NET. Eram uns 30 a 40 ms a cada terco de segundo neste
/// processo, que hospeda o servidor -- 70 e tantas por corrida, 10% do tempo com o processo parado. Desde a
/// tarde de 2026-10-08 (a causa e a medida estao no `RoboDoColetor`, `--diagcoletor`) sao 2 a 7 por corrida, de
/// 15 a 18 ms. Nao sao do estouro.
/// =================================================================================
///
/// ============================ E A MUSICA DE COMBATE, QUE CAIA NO PRIMEIRO GOLPE ============================
/// O relato do primeiro golpe de cada luta custava 12 a 18 ms contra 6 a 7 dos seguintes, e esta bancada media
/// sem cobrar: ele poe no ar uma faixa de combate SORTEADA, e o mp3 dela (0,5 a 3,3 MB) era lido do disco pela
/// thread principal ali mesmo. Medido em 2026-10-08, com um cronometro em volta do `AudioDirector.Cruzar` e
/// outro em volta do `World.AoGolpe`:
///
///     o `World.AoGolpe` do primeiro golpe de uma luta ... 5,3 a 6,1 ms    (o de um golpe qualquer: 1,0 a 1,3)
///       -- esperando o mp3 .............................. 3,1 a 5,3 ms    com o arquivo no cache do Windows;
///                                                         7,5 numa primeira leitura do dia (1,5 MB)
///       -- ligando o tocador ............................ 0,1 ms
///     o quadro dele ..................................... 10,4 a 12,9 ms  (o de um golpe qualquer: 5,3 a 7,6)
///
/// O mp3 NAO era o excesso inteiro do primeiro golpe DO PROCESSO: com o arquivo no cache do Windows era metade
/// dos ~6 ms a mais, e o resto e codigo rodando pela primeira vez (1,5 ms dentro do `AoGolpe`, o restante no
/// pacote da morte do tiro, que chega no mesmo quadro). Nas lutas seguintes era o excesso todo.
///
/// O CONSERTO e o `AudioDirector.Adiantar`: a faixa que o proximo sorteio vai devolver e lida numa
/// thread de carga -- na entrada no mundo, e de novo cada vez que o saco de sorteio anda.
///
/// DEPOIS: a thread principal espera 0,0 ms pela faixa; o `World.AoGolpe` do primeiro golpe de uma luta leva
/// 1,0 ms e o quadro dele 3,2 a 4,2 (os golpes comuns das mesmas corridas: 3,4 a 4,0). O primeiro golpe do
/// PROCESSO fica em 8 a 10 ms -- o que sobra e o codigo de primeira vez, e nao musica.
/// (As duas colunas nao se comparam em valor absoluto: entre uma medicao e a outra o quadro parado deste
/// processo caiu de 4,5 pra 2,2 a 3,0 ms por um conserto de OUTRA frente, o do coletor. O que se compara e o
/// primeiro golpe contra os golpes comuns da MESMA corrida -- e, nas corridas de depois, a luta 3, que e o jogo
/// de antes: 6,8 a 12,3 ms, com 3,3 a 8,7 deles esperando o mp3.)
///
/// A REGUA DELA TEM DUAS PERNAS (ver `AnotarAFaixa`): o quadro em que a faixa entra custa um quadro normal, E a
/// thread principal nao esperou pelo arquivo. A segunda existe porque a primeira sozinha nao enxerga o defeito:
/// ler 2 MB do cache do Windows sao 3 a 5 ms, e o teto de quadro tem 12 de folga.
/// ==========================================================================================================
///
/// ============================ E OS TEMAS QUE ENTRAM FORA DA LUTA: O ESC E A ESTREIA DE UMA FORMA ============================
/// A musica de combate nao era a unica lida na hora. O tema de MENU entrava no quadro em que o ESC abria (seis
/// faixas de 1,6 a 14,2 MB, uma por abertura), o de cada TRANSFORMACAO no primeiro quadro da cinematica de estreia
/// (os maiores tem 10 MB) e o de um LUGAR na chegada na zona -- todos lidos do disco pela thread principal. Medido
/// pela rodada `--temas` em 2026-10-08, antes do conserto: quanto a thread principal esperou pelo arquivo, em ms.
///
///                                                    no cache do Windows    primeira leitura do dia
///     o ESC, faixa `.mp3` de 2,5 a 4,4 MB ............... 3,6 a 5,7 ............ 14,0 a 18,8
///     o ESC, a `.ogg` de 2,5 MB ......................... 10,4 a 11,5 .......... 14,9
///     o ESC, a faixa de 14,2 MB ......................... 14,7 a 21,5 .......... 45,3
///     a estreia do `ssj1`, tema de 9,8 MB ............... 10,2 a 10,8 .......... 32,4
///     a estreia do `ssj2`, tema de 4,2 MB ...............  4,9 a 6,5 ........... 17,1
///     a estreia do `blue`, tema de 10,0 MB .............. 10,5 a 16,1 .......... 31,1
///     a chegada no Inferno, `Demon World`, 7,1 MB ....... (sem medida) ......... 22,3
///
/// O CONSERTO tem duas metades, e quem escolhe entre elas e a camada ter ou nao SACO DE SORTEIO
/// (`AudioDirector.SacoDe`):
///   * o MENU tem saco, como o combate: a faixa que ele vai entregar em seguida e pedida a thread de carga antes
///     da hora (`AudioDirector.Adiantar`) -- na tela de login, na entrada no mundo e a cada abertura do ESC;
///   * o tema de uma TRANSFORMACAO, o da raiva e o de um lugar so se conhecem no quadro em que sao pedidos. Ai
///     quem espera e o som, e nao a tela: o `AudioDirector.Cruzar` pede o arquivo a thread de carga e a faixa
///     ENTRA NO AR QUANDO CHEGA.
///
/// DEPOIS: a thread principal espera 0,0 ms por todas elas. O ESC abre com a faixa ja na memoria. O tema de uma
/// estreia entra no ar 1 a 3 quadros depois de pedido (8 a 26 ms num monitor de 120 Hz, com o arquivo no cache do
/// Windows; uma vez em dez, com a maquina ocupada, 13 quadros -- 108 ms), e o quadro em que a cena nasce custa
/// 4,1 a 5,6 ms de trabalho -- com o defeito injetado, nas mesmas corridas, 9,3 a 17,2, dos quais 4,9 a 10,8
/// esperando o mp3.
///
/// (O quadro em que a PRIMEIRA cinematica do processo nasce tinha um custo que nao era da trilha, e ganhou regua
/// propria: e o bloco seguinte.)
/// ====================================================================================================================
///
/// ============================ E O QUADRO EM QUE A PRIMEIRA CINEMATICA DO PROCESSO NASCE ============================
/// A rodada dos temas media, sem cobrar, um quadro de 42 ms: o da primeira cinematica de transformacao do processo,
/// contra 4 das seguintes -- a tela parada no instante em que a transformacao comeca, uma vez por processo. ONDE O
/// TEMPO IA, medido em 2026-10-08 pela rodada `--temas --cenas` (o arquivo `RoboDoPrimeiroEstouro.Cena.cs`), com o
/// cache de shader em disco vazio como em toda bancada:
///
///     o quadro em que a primeira cena nasce ......... 41 a 43 ms   (script 18, desenho 24; as seguintes: 4)
///       -- esperando o shader da chama ............... 21 a 24 ms   a thread principal parada no PREPARO do desenho
///                                                                  do quadro, e nao no desenho da janela: o Godot
///                                                                  comeca a compilar o `Aura.gdshader`, em segundo
///                                                                  plano, quando o primeiro material dele nasce, e
///                                                                  quem nasce e e desenhado no mesmo quadro espera
///                                                                  a compilacao inteira (com um quadro de
///                                                                  antecedencia o mesmo primeiro desenho custou 1)
///       -- codigo rodando pela primeira vez .......... 9 ms         o .NET compilando o caminho do pacote, do
///                                                                  `World.AoMudarForma` ao `Transformacao._Ready`
///       -- a folha da chama, lida do disco ........... 3 a 4 ms     (`AuraSSjBig.tres`)
///       -- o material e o sprite da chama ............ 2 ms
///
/// (Os "42 a 63 ms" que este cabecalho citava eram o mesmo quadro: as corridas de 62 tinham a maquina carregada -- a
/// base delas e 2,8 a 2,9 ms em vez de 2,0 a 2,2, e tudo nelas sai multiplicado por 1,45. E a bancada mandava a VOLTA
/// A BASE no mesmo quadro; hoje o gesto manda so o pacote da transformacao, como o servidor, e o script do quadro
/// caiu de 20 pra 18 ms. Ver `AquecerACena`.)
///
/// O CONSERTO e do `Aquecimento`: as folhas de chama das sete linhas de forma entram na fila de carga do lobby, e o
/// ensaio ganhou um ato que NASCE uma cena de verdade fora da tela (`Transformacao.Ensaiar`).
///
/// DEPOIS: o quadro em que a primeira cena nasce custa 6,2 a 6,7 ms de trabalho (script 5,7 a 6,2, desenho 0,4 a 0,5),
/// dentro de uma volta do monitor, com o login de robo e com o de gente. Os 2 a 3 ms que ele ainda tem a mais que o
/// de uma cena qualquer sao o codigo de primeira vez que mora no `World`, onde o ensaio nao chega. Com o defeito
/// injetado, no mesmo binario (`--temas --semensaiodacena`): 41,3 a 42,6 ms.
///
/// COM O CACHE DE SHADER EM DISCO CHEIO (o de quem ja abriu o jogo uma vez; medido guardando a pasta de dados entre
/// duas corridas) a espera pelo shader some sozinha, e o defeito era 18,2 ms, todos de script. O conserto da os
/// mesmos 6,3.
///
/// (O QUE A CENA CUSTAVA DEPOIS DE NASCER -- o beat que assume, o primeiro piscar de cabelo, cada beat com som --
/// ganhou conserto e regua propria: e o bloco seguinte.)
/// ====================================================================================================================
///
/// ============================ E O BEAT QUE ASSUME, O INSTANTE MAIS VISTO DA CENA ============================
/// Dez segundos depois de nascer, a cena chega no beat em que a forma FICA: o chao abre, a tela estoura em clarao. O
/// quadro dele custava 28 a 36 ms na primeira cinematica do processo -- tres ou quatro voltas do monitor paradas no
/// climax -- e nenhuma regua o cobrava: a rodada dos temas cortava a cena aos 1,5 s. ONDE O TEMPO IA, medido em
/// 2026-10-08 pela rodada `--temas --cenas ... --cenacoleta` (o arquivo `RoboDoPrimeiroEstouro.Cena.cs`), que parte o
/// `_Process` da cena em pedacos (`Transformacao.EspiaoDePedaco`) e, com `--verbose`, da nome a cada arquivo que a
/// thread principal le:
///
///     o beat que assume, primeira cena do processo ..... 28,0 a 36,3 ms de trabalho (tres corridas)
///       -- a FUMACA da cratera ......................... 14,3 a 22,6 ms   um PNG de 1280x720, lido e descomprimido
///                                                                        ali -- e SEM DONO: o `Decalques` fazia um
///                                                                        `GD.Load` por fumaca, e com o coletor do
///                                                                        .NET passando entre duas cenas ela voltou
///                                                                        do disco em seis cenas de seis
///       -- a cratera ................................... 6,4 a 9,5 ms     a folha dela, e 3 a 4 de codigo rodando
///                                                                        pela primeira vez
///       -- o `Vestir` da forma e o clarao .............. 2 a 3 ms
///     a primeira cena de OUTRA forma, com a coleta ..... 30,8 a 37,7 ms   a mesma fumaca, e a folha que a forma
///                                                                        veste: o penteado 6,5 a 9,4, o corpo do SSJ4
///                                                                        7,0, a colada do Blue 9,9
///     uma forma repetida, com a coleta ................. 18,4 a 20,5 ms   so a fumaca
///     cada beat com som ................................ 2,5 a 5,6 ms    o arquivo, lido a cada toque e sem dono
///     o primeiro piscar de cabelo ...................... 12,3 ms          9,2 deles o penteado da forma
///
/// (SEM a coleta forcada os mesmos beats custavam 20 a 29, 9 a 24 e menos de 6 -- os numeros que este cabecalho
/// citava: quanto do cache sobrevivia de uma cena pra outra dependia de o coletor do .NET ter passado ou nao.)
///
/// O CONSERTO tem dois donos, e quem escolhe entre eles e o arquivo depender ou nao de quem se transforma:
///   * o que NAO depende -- as duas crateras, a fumaca, o som de cada beat, os estalos da faisca, os trovoes -- entra
///     na fila de carga do lobby e fica (`Aquecimento.ArquivosDaCena`); a fumaca ganhou dono (`Decalques.TexturaSolta`)
///     e o chao que abre, um ato no ensaio (`Decalques.Ensaiar`);
///   * o que DEPENDE -- o penteado, o corpo proprio e as coladas da forma -- a propria cena pede a thread de carga
///     quando comeca, pela porta das folhas (`Transformacao.AdiantarAsFolhasDaForma`, `FolhasPresas.Adiantar`).
///
/// DEPOIS: o beat que assume custa 4,4 a 5,8 ms na primeira cinematica do processo (oito corridas; os pedacos da cena
/// somam 2,4 a 2,9) e 3,2 a 7,6 nas outras formas e na forma repetida, com a coleta entre as cenas. A thread
/// principal nao le mais arquivo nenhum durante a cena, fora o script dela mesma, no nascimento (e esse ganhou dono
/// depois: ver o arquivo `RoboDoPrimeiroEstouro.Scripts.cs`). O primeiro piscar de cabelo cai de 12,3 pra 4,3 a 5,4
/// ms. Com o defeito injetado, no mesmo binario (`--temas --semadiantaracena`): 22,7 a 26,2 ms no beat que assume.
///
/// A REGUA TEM TRES PERNAS (`AcompanharAPrimeiraCena`, `ConferirAEsperaDaCena`, `ConferirOsDonos`): o quadro do beat
/// que assume custa um quadro normal; do nascimento ao fim da cena a thread principal nao esperou por folha (o
/// cronometro e o da porta, `FolhasPresas.EsperaDeTeste`: 13,7 ms antes, 0,0 depois); e, passada uma coleta forcada
/// do .NET, o que a cena usa continua na memoria (`ResourceLoader.HasCached`: antes, 15 de 16 fora). As duas ultimas
/// existem porque o teto de quadro nao separa uma leitura de 3 a 10 ms do resto do quadro.
///
/// O QUE FICOU, e nao e arquivo. O primeiro `_Process` da primeira cena do processo custa 9 a 13 ms (antes, 13 a 16):
/// e codigo rodando pela primeira vez, e o ensaio nao o paga porque a cena dele nao roda. O Godot relia o
/// `Transformacao.cs` a cada cena que nascia sem outra viva (1,4 a 1,5 ms no nascimento, medido depois; FECHADO: o
/// aquecimento segura o script -- ver o arquivo `RoboDoPrimeiroEstouro.Scripts.cs`). E pedir as folhas da forma custa
/// 2 a 4 ms no primeiro quadro calmo da cena -- achar a variante do penteado varre a pasta, uma vez por penteado.
///
/// (O ENGASGO que aparecia em tres de quinze corridas desta rodada -- 19 a 36 ms de desenho em dois quadros, com a
/// regua da musica reprovando por tabela -- nao era desta regua, e tambem nao era a maquina: e o bloco seguinte.)
/// ====================================================================================================================
///
/// ============================ E OS RAIOS DA FORMA: DUAS PIPELINES QUE O ENSAIO NAO MONTAVA ============================
/// Em tres de quinze corridas da rodada dos temas, dois quadros custavam 19 a 36 ms de DESENHO (o quadro inteiro, ate 43
/// contra o teto de 27): o do beat dos FEIXES DO CHAO da primeira cinematica do processo, aos 6 s dela, e o dos primeiros
/// RAIOS DE CORPO, o beat zero da estreia do `ssj2` -- que e tambem o quadro em que o tema dela entra no ar, e por isso
/// quem reprovava era a regua da musica. Nas outras doze, 1 ms cada. Sao os dois quadros em que nasce uma pipeline do
/// `RaioDaForma.gdshader`: o shader estava na lista de carga do `Aquecimento`, e ato nenhum do ensaio o desenhava.
///
/// POR QUE DUAS: o motor monta uma pipeline por VARIANTE do shader, e o retangulo de um sprite (os oito feixes) e a malha
/// de uma particula (a faisca do corpo) sao variantes diferentes. Cada uma tem ainda a gemea do item com luz em cima:
/// quatro ao todo, das quais um mundo de dia usa duas. (Godot 4.7, `renderer_canvas_render_rd`: a chave da pipeline e
/// variante + formato do alvo + formato do vertice + primitiva + "tem luz", e quem desenha ESPERA a montagem.)
///
/// POR QUE SO AS VEZES -- e nao era a maquina carregada. Quem monta a pipeline e o DRIVER de video, e ele guarda em
/// disco o que ja compilou, num cache SO DELE (o da NVIDIA mora em `%LOCALAPPDATA%\NVIDIA\DXCache`), fora da pasta de
/// usuario que toda bancada desvia. MEDIDO em 2026-10-09, o desenho de cada um dos dois quadros, sem o conserto:
///
///     o driver ja viu o shader (qualquer corrida repetida na maquina) ....... 1,0 a 1,9 ms    (dez corridas)
///     o driver NUNCA viu o shader (`--driverfrio`, a maquina sozinha) ....... 19,3 a 20,0 ms nos feixes e 21,1 a 23,4
///                                                                            nos raios (seis corridas); o quadro
///                                                                            inteiro, 23 a 31 ms
///     as tres corridas em quinze ............................................ 19 a 36 ms
///
/// O cache do driver so esta vazio pra um shader na PRIMEIRA vez que o jogo roda na maquina, depois de uma troca de
/// driver, e quando alguem o apaga -- os arquivos dele foram recriados as 22:44 de 2026-10-08, e a corrida seguinte foi
/// uma das tres. E ha o caso que e so de bancada: o driver da um arquivo de cache a cada processo SIMULTANEO do mesmo
/// executavel (`...df.nvph`, `...e0.nvph`, `...e1.nvph`), e o Godot com janela que sobe ao lado de outro cai num
/// arquivo que nao viu o que o primeiro viu -- outra das tres tinha a bancada de outra sessao ao lado. Reproduzido de
/// proposito: o TERCEIRO Godot com janela ao mesmo tempo estreou um arquivo vazio e pagou 16,5 ms nos feixes e 23,7
/// nos raios; sozinho, ou como segundo (num arquivo que ja tinha visto o shader), 1,0 a 1,9.
///
/// OU SEJA: O ENGASGO E DO JOGO -- e o de quem abre o jogo pela primeira vez --, e a bancada so nao o via sempre porque
/// o APPDATA desviado esvazia o cache de shader do GODOT e nao o do driver. O que este cabecalho chama de "cache de
/// shader em disco VAZIO" e a metade do caso de quem acabou de instalar: todo "a pipeline custa 1 ms no primeiro
/// desenho" daqui foi medido com o driver quente.
///
/// O CONSERTO e do `Aquecimento` (`AtosDosRaios`): dois atos a mais no ensaio do lobby, que desenham no palco, dos dois
/// lados dele, uma faisca de corpo (`RaiosDaForma.Ensaiar`) e os oito feixes (`Transformacao.EnsaiarOsFeixes`).
///
/// DEPOIS: nenhuma pipeline nasce em nenhum dos dois quadros, e o desenho deles custa 0,3 a 0,6 ms -- com o driver
/// quente e com ele frio. O ensaio monta 25 pipelines em vez de 21 (22 em vez de 18 com o login de gente, em que ele
/// cabe inteiro no lobby). O PRECO fica no ensaio: com o login de robo, nada que se meca na montagem (401 a 408 ms
/// contra 393 a 409); com o de gente, 48 ms a mais num quadro do lobby (469 contra 421, uma corrida de cada: sem corpo
/// nenhum no mundo, os primeiros materiais da faisca nascem ali); e, com o driver frio, as quatro pipelines -- o ensaio
/// inteiro leva uns 0,1 s a mais.
///
/// A REGUA TEM DUAS PERNAS (o arquivo `RoboDoPrimeiroEstouro.Raios.cs`): NENHUMA PIPELINE 2D NASCE no quadro -- e a que
/// reprova sempre, porque e contagem do proprio motor e nao depende do cache de ninguem -- e o DESENHO dele custa um
/// desenho normal, que e a que o jogador sente e so morde com o driver frio. O `--driverfrio` faz o driver frio existir
/// de proposito: salga o shader com uma constante que vale zero em todo pixel, e o driver recebe um codigo que nunca
/// viu. Com o defeito injetado, no mesmo binario (`--temas --semensaiodosraios`): uma pipeline em cada quadro, 1,0 a
/// 1,2 ms de desenho com o driver quente; com o `--driverfrio` junto, 19,3 e 19,4 nos feixes e 21,1 e 21,7 nos raios.
///
/// E A REGUA ACHOU UM SEGUNDO DEFEITO, ANTIGO, NO PROPRIO ENSAIO: O LADO ESCURO DO PALCO NEM SEMPRE ERA ESCURO. Com o
/// conserto de cima no lugar, ela passou dez corridas seguidas e reprovou as cinco seguintes (de 01:56 a 02:17) -- e
/// nelas tambem o ANEL DE CHOQUE, um ato antigo, montava a pipeline dele em jogo. O clima natural do mundo e funcao
/// do relogio (`Clima.TipoDoBloco`; um dia do jogo sao 24 minutos de parede), e o bloco do meio-dia tinha virado
/// tempestade: o mundo comecava ESCURO, e a estrela do embate do ensaio pendurava a luz de ki dela, que so nasce com
/// o mundo escuro (`LuzDeKi`, 121 px de raio, viva ate o palco sair). O lado escuro do palco ficava iluminado, e os
/// atos seguintes -- o estouro, o anel, o escudo, os raios da forma -- so montavam a pipeline do item COM luz: a "de
/// dia" ficava pra hora do primeiro uso. MEDIDO pela contagem do motor, com o login de robo: o ensaio montava 25
/// pipelines com o mundo claro e 20 com ele escuro (na tempestade natural, e de noite com o defeito injetado).
/// O CONSERTO: a estrela do ensaio nasce sem luz propria, como o tiro ja nascia (`ChoqueDeKi.SemLuz`,
/// `Aquecimento.PorAEstrela`). DEPOIS: de noite o ensaio monta 26, e nenhuma pipeline nasce nos tres quadros da regua
/// -- o anel entrou nela por isso. Com o defeito injetado (a rodada de NOITE com `--estrelacomluz`), 20, e uma em
/// cada quadro. Quem joga quase nunca caia nisso -- o ensaio acaba no lobby em menos de dois segundos, e no lobby nao
/// ha mundo escuro --, mas toda bancada e toda tomada do trailer entram no mundo antes dele.
///
/// E O LADO ILUMINADO DO PALCO TAMBEM FOI CONFERIDO (`--raioscomluz`: uma luz acesa em cima do corpo no meio da base):
/// no quadro em que ela acende nascia a pipeline iluminada do que ja estava na tela (uma: a do boneco, que hoje o
/// ensaio monta -- ver o bloco seguinte), e nos tres quadros da regua nenhuma -- as gemeas iluminadas dos tres efeitos
/// vieram do ensaio. Com `--semensaiodosraios` junto, uma em cada quadro dos raios.
///
/// O QUE AINDA MONTA PIPELINE EM JOGO sai no fim de toda rodada desta bancada (`ListarAsPipelinesNascidasEmJogo`),
/// tirado da mesma contagem -- e foi dessa lista que saiu o bloco seguinte.
/// ====================================================================================================================
///
/// ============================ E OS PRIMEIROS DESENHOS QUE SOBRAVAM NO INVENTARIO ============================
/// Com os raios da forma fora dele, o inventario ainda trazia quadro com pipeline 2D montada em jogo: na rodada dos
/// temas, o primeiro relampago da tempestade da estreia do `ssj1` (uma) e o beat zero da estreia do `blue` (duas); na
/// bola de NOITE, a primeira bola (uma); na das pecas, a primeira estrela do embate (duas, com o ensaio e sem ele).
/// Tudo medido so com o driver de video QUENTE, em que uma pipeline custa 1 ms. O `--driverfrio` passou a aceitar uma
/// LISTA de shaders (o sal acha sozinho onde entrar em qualquer shader de item 2D: `Salgar`), e cada quadro foi medido
/// com o driver frio, a maquina sozinha, ANTES de qualquer conserto.
///
/// MEDIDO em 2026-10-09, o DESENHO do quadro em que a pipeline nasce -- e, depois do conserto, do mesmo quadro sem ela:
///
///                                                  driver quente   driver FRIO    depois (quente e frio)
///     o primeiro relampago (`Raio.gdshader`) ...... 1,0 ms          25 a 41 ms     0,3 a 0,8 ms, nenhuma pipeline
///     a primeira chuva (`Queda.gdshader`) ......... 1,1 a 1,9       28 a 38        0,4 a 0,7 ms, nenhuma pipeline
///     a primeira luz num corpo (`Personagem`) ..... 1,2             101 a 106      0,3 a 0,5 ms, nenhuma pipeline
///     o primeiro balao de fala (shader padrao) .... 0,5 a 0,8       0,7            nao foi mexido
///     o quad da primeira estrela (`ChoqueDeKi`) ... 0,5 a 0,6       0,5 a 0,6      nao foi mexido
///
/// OS TRES PRIMEIROS CUSTAVAM, e foram consertados no molde de sempre: o ensaio do lobby desenha cada um, dos dois lados
/// do palco (`Aquecimento.AtosDoRelampago`, `AtosDaChuva`, `AtosDoCorpo`).
///   * O RELAMPAGO e o risco do `ClimaNaTela`, um sprite que nasce invisivel com o mundo e so e desenhado quando um raio
///     cai DENTRO da tela. Na estreia do `ssj1` ele cai entre 1,0 e 2,0 s de cena, e a rodada dos temas cortava a cena
///     com 1,5: aparecia em 18 corridas de 50 (em jogo a cena dura 26 s, e ele cai sempre). Ela passou a ESPERAR por
///     ele (`EsperandoORelampago`), e so conta o risco que tem pedaco dentro da tela (`RiscoNaTela`): o raio que cai
///     acima da borda acende um risco que o motor nao desenha, e esse quadro passava pela regua sem provar nada.
///   * A CHUVA nao estava no inventario: as rodadas de dia cravam o ceu aberto, e nenhuma ve uma chuva COMECAR. Saiu da
///     leitura do motor -- o emissor dela nasce desligado com o mundo, e o motor nao desenha particula inativa -- e
///     ganhou rodada propria, a do CEU (`--temas --ceu`), que forca uma tempestade no meio do jogo e faz um raio cair
///     a 3 tiles do corpo: mede a primeira chuva e, sem depender do sorteio da cinematica, o primeiro relampago.
///   * O BONECO COM LUZ era o "+1 na primeira bola de noite": o motor desenha o item que tem luz em cima com outra
///     pipeline do mesmo shader, e a do `Personagem.gdshader` -- o shader mais caro do jogo pro driver -- so nascia
///     quando a primeira luz alcancava um corpo. De noite, a luz de ki do primeiro tiro: mais de 100 ms de tela parada
///     no meio da luta. O ato poe um boneco de verdade no palco, com uma folha de um quadro branco so.
///
/// OS DOIS ULTIMOS NAO CUSTAM, e ficaram como estavam -- a conta de cada um esta no `RoboDoPrimeiroEstouro.Nascidas.cs`:
///   * O BALAO DE FALA (a FALA do beat zero do `blue`) sao duas malhas do shader PADRAO do motor, o contorno e o
///     rabicho. O sal nao alcanca codigo que nao e do projeto: foi medido com um arquivo de cache do driver novo de
///     verdade (o quarto Godot com janela ao mesmo tempo estreia um), e as duas pipelines nasceram num quadro de 0,7 ms
///     de desenho -- 2 ms a mais no quadro inteiro.
///   * O QUAD DA ESTRELA e a segunda das duas pipelines da primeira estrela (a outra e a das faiscas, material gerado,
///     que volta). E a gemea SEM luz do `ChoqueDeKi.gdshader`: a estrela do lado escuro do palco do ensaio encosta, por
///     nove decimos de pixel, no retangulo da luz do lado iluminado, e o ensaio so monta a gemea COM luz. A sem luz,
///     montada em jogo depois da outra, sai de graca mesmo com o driver frio. Quem as separou foi a medicao `--pecas
///     --estrelapartida`; a causa esta escrita no `Aquecimento.TamanhoDoPalco`.
///
/// O PRECO FICA NO LOBBY: tres atos a mais no ensaio, que com o login de gente somam 150 ms de quadro com o driver
/// quente e 500 com ele frio (os numeros estao no `Aquecimento.AtosDoRelampago`).
///
/// O QUE CONTINUA NA LISTA E NAO E DEFEITO: os dois de cima; a mira e a sombra de voo, que sao as mesmas malhas do
/// balao (a mira foi conferida numa corrida, a sombra so pela leitura do motor); e o material gerado de cada efeito
/// ensaiado (`CanvasItemMaterial`), que o motor solta com o ultimo dono e monta de novo no uso seguinte.
/// E O QUE ESTA BANCADA NAO ALCANCAVA: o primeiro uso de um shader do projeto que ato nenhum do ensaio desenhava e que
/// nenhuma rodada dela punha na tela -- o `Zanzoken`, o `Embate` (a tela do `ClashQte`), a `NebulosaDaForma`, a
/// `Altitude`, a `Gota` e os dois do planeta que morre. FECHADO no mesmo dia, os sete medidos com o driver frio antes de
/// qualquer conserto: o do Zanzoken no bloco "E A PRIMEIRA MIRAGEM", os outros seis no "E OS SEIS QUE NENHUMA RODADA
/// PUNHA NA TELA". Hoje, dos 21 `.gdshader` de `Assets/Shaders`, dezoito tem ato no ensaio do lobby; o do veu do tempo
/// (`Clima`) nasce visivel com o mundo, numa camada de tela onde luz nenhuma chega; e dois (`Ki`, `Transformacao`)
/// nenhuma linha do jogo carrega.
/// ====================================================================================================================
///
/// ============================ E O PRIMEIRO BORRAO: O EFEITO DE LUTA QUE O AQUECIMENTO NAO CONHECIA ============================
/// Toda investida e toda corrida largam copias borradas do corpo (`RastroDeCorrida`, com o `Borrao.gdshader`). O
/// `BorraoDirecional` carregava o shader na hora do primeiro uso, e ele nao estava nem na fila de carga do lobby: o
/// primeiro borrao do processo -- a primeira investida de uma luta -- lia o arquivo do disco pela thread principal,
/// criava o primeiro material dele e o desenhava no mesmo quadro. Achado em 2026-10-09 pelo gravador sozinho ao lado
/// de um duelo de dois NPCs (`--trailer duelo --diagestouro --passivo 100`): o quadro da primeira investida custava 33
/// a 42 ms de trabalho em cinco corridas, e em tres delas passou de 50 ms de relogio.
///
/// ONDE O TEMPO IA, medido pela rodada `--pecas` (o arquivo `RoboDoPrimeiroEstouro.Borrao.cs`). Ela ganhou a peca -- o
/// rastro de um arranque de oito tiles, dez copias do corpo, pela porta de producao -- e, com `--borraopartido`, nasce
/// antes cada pedaco do primeiro uso sozinho num quadro. O quadro do primeiro borrao, sem o conserto:
///
///                                              o cache de shader      o cache do Godot      o DRIVER de video
///                                              do Godot VAZIO         CHEIO                 FRIO
///     o quadro inteiro, em relogio ........... 32 a 42 ms             10 a 12 ms            59 a 61 ms
///       -- o script .......................... 7 a 12                 8 a 10                8
///       -- esperando o shader (o PREPARO) .... 22 a 32                0,5                   25
///       -- montando a pipeline (a TELA) ...... 1,0 a 1,9              1,0                   24 a 28
///     um borrao qualquer (o controle): 8,3 ms de relogio -- uma volta do monitor --, com 2,5 a 4,3 de script
///
/// ("VAZIO" e o cache de toda bancada, e o de quem abre o jogo pela primeira vez; "CHEIO", o de quem ja jogou -- medido
/// guardando a pasta de dados entre duas corridas; o driver frio e o `--driverfrio Borrao`, o sal do bloco de cima, que
/// esvazia junto o cache do Godot pra esse shader. Oito corridas na primeira coluna, duas em cada uma das outras.)
///
/// O SCRIPT A MAIS, em pedacos (`--borraopartido`): o arquivo lido do disco, 0,7 ms (3,9 com `--verbose`: o motor
/// escreve no log a cada carga); o primeiro material, 1,7; e 2 a 3 de codigo rodando pela primeira vez.
/// A ESPERA E DO MATERIAL, E NAO DO DESENHO: o quadro em que o primeiro material do shader nasce parou 24 ms no preparo
/// com o sprite FORA da arvore, sem nada desenhado; o primeiro desenho, um segundo depois, custou 1 ms (28 com o driver
/// frio) e trouxe so a pipeline.
///
/// O CONSERTO e do `Aquecimento` (`AtosDoBorrao`): o shader entra na fila de carga do lobby, e o ensaio ganha um ato --
/// um sprite borrado pela mesma chamada que o rastro faz em cada camada de cada foto (`BorraoDirecional.Ensaiar`), dos
/// dois lados do palco.
///
/// DEPOIS: o quadro do primeiro borrao custa 5,5 a 8,4 ms de trabalho (onze corridas) e cabe numa volta do monitor (8,0
/// a 9,0 ms de relogio), com o cache do Godot vazio ou cheio e com o driver quente ou frio; nenhuma pipeline nasce nele,
/// e a thread principal nao le arquivo nenhum. NO DUELO, com o gravador ao lado: a primeira investida 39,7 ms de trabalho
/// (42,0 de relogio) com o defeito injetado e 5,5 (8,0) sem ele -- uma corrida de cada, no mesmo binario. Os 2 a 3 ms que
/// o primeiro borrao ainda tem a mais que um qualquer sao o codigo do rastro rodando pela primeira vez: o palco do
/// ensaio tem um sprite, e nao um corpo correndo.
///
/// E O LADO ILUMINADO DO PALCO FOI CONFERIDO EM JOGO (`--pecas --raioscomluz`, a luz do bloco de cima em cima do corpo):
/// as copias mais perto do corpo caem dentro da luz e as outras fora, e o mesmo quadro usa as DUAS pipelines do shader --
/// com o defeito injetado nascem as duas nele; com o conserto, nenhuma.
///
/// O PRECO, NO LOBBY (a tabela "O ENSAIO, ATO A ATO" desta rodada, com `--lobbyteste 4`): um arquivo de 700 bytes a
/// mais na fila, um ato, duas pipelines (a do sprite sem luz e a do iluminado) e UM quadro de 37 ms -- 27 deles a
/// espera do shader -- onde haveria um de 8; o ensaio inteiro foi de 871 pra 896 ms (uma corrida de cada). Com o cache
/// do Godot cheio o quadro do ato custa 7 ms; com o driver frio, 127 (as duas pipelines montadas do zero).
///
/// A REGUA TEM TRES PERNAS: o shader JA ESTAVA NA MEMORIA antes do primeiro gesto (um fato, perguntado ao Godot -- a
/// leitura custa menos de 1 ms, e nenhum teto de quadro a ve); NENHUMA PIPELINE 2D NASCE no quadro, nem no seguinte
/// (contagem do motor, que nao depende do cache de ninguem); e o quadro CUSTA UM QUADRO NORMAL -- a que o jogador sente,
/// e a unica que depende de cache: com o do Godot cheio e o driver quente o defeito inteiro sao 8,5 a 10,9 ms de
/// trabalho, abaixo do teto, e nessa rodada o quadro sai como numero. O defeito injetado e o
/// `Aquecimento.SemEnsaioDoBorraoDeTeste` (`--pecas --semensaiodoborrao`): 31 a 35 ms de trabalho no mesmo binario
/// (quatro corridas), e 59 a 61 de relogio com o `--driverfrio Borrao` junto.
///
/// E A RODADA GANHOU DOIS INSTRUMENTOS. O ENSAIO, ATO A ATO (`Aquecimento.EspiaoDeAto`): a montagem de cada ato e o
/// quadro dele partido em script, preparo e tela -- e como se le quanto um ato pesa onde ele e pago. E O DESENHO PARTIDO
/// COM O ATRASO CERTO: o preparo de um quadro esta na leitura do quadro seguinte, e a tela na de DOIS depois (so assim a
/// conta fecha com o relogio: 10,4 + 27,6 + 1,1 num quadro de 40,2 ms). No gravador sozinho, os quatro primeiros
/// borroes da cena e cada ato do ensaio ganham rotulo e a mesma conta.
/// ====================================================================================================================
///
/// ============================ E A PRIMEIRA MIRAGEM: O VULTO DO ZANZOKEN TINHA O DEFEITO DO BORRAO ============================
/// Quem tem a `Afterimage Technique` larga uma miragem a cada Zanzoken e a cada arranque (`Zanzoken.Deixar`: a foto da
/// pilha do corpo, com um material do `Zanzoken.gdshader` em cada camada). O `Zanzoken` carregava o shader na hora do
/// primeiro uso, e ele nao estava nem na fila de carga do lobby: o feitio do borrao, achado por LEITURA do codigo na
/// sessao que o consertou, e medido aqui antes de qualquer conserto. A primeira miragem do processo lia o arquivo do
/// disco pela thread principal, criava os primeiros materiais dele e os desenhava no mesmo quadro.
///
/// ONDE O TEMPO IA, medido em 2026-10-09 pela rodada `--pecas` (o arquivo `RoboDoPrimeiroEstouro.Miragem.cs`). Ela ganhou
/// a peca -- a miragem do corpo local, tres camadas, pela chamada que o `World.AoPiscar` faz -- e, com `--miragempartida`,
/// nasce antes cada pedaco do primeiro uso sozinho num quadro. O quadro da primeira miragem, sem o conserto:
///
///                                              o cache de shader      o cache do Godot      o DRIVER de video
///                                              do Godot VAZIO         CHEIO                 FRIO
///     o quadro inteiro, em relogio ........... 34 a 36 ms             8,8 ms                56 a 57 ms
///       -- o script .......................... 6,7 a 8,0              6,7                   6,1 a 6,5
///       -- esperando o shader (o PREPARO) .... 24 a 27                0,2                   25
///       -- montando a pipeline (a TELA) ...... 1,2 a 1,4              1,2                   24 a 25
///     uma miragem qualquer (o controle): 8,1 a 8,7 ms de relogio -- uma volta do monitor --, com 1,8 a 3,3 de script
///
/// (Seis corridas na primeira coluna -- tres antes do conserto e tres com o defeito injetado, no binario de depois --,
/// uma na segunda e tres na terceira. "VAZIO", "CHEIO" e o driver frio sao os do bloco de cima; o sal e o
/// `--driverfrio Zanzoken`.)
///
/// E COM UMA LUZ EM CIMA DO CORPO (`--raioscomluz`) a pipeline que falta e a gemea do sprite ILUMINADO, e pro driver de
/// video essa e a cara: 67,6 ms de tela, num quadro de 100,8, na primeira vez que esta maquina a desenhou -- sem sal
/// nenhum: o driver nunca tinha visto essa variante --, e 73,8 e 74,6 (quadros de 107,6 e 106,6) com o sal. E a miragem
/// de noite, ao lado de uma aura, de uma fogueira ou de um tiro de ki. (Depois daquela corrida o driver passou a
/// conhece-la, e sem o sal a mesma rodada da 1,3 ms.)
///
/// EM PECAS (`--miragempartida`): o arquivo lido do disco, 0,8 ms; a chamada inteira na primeira vez, 3,2 a 3,3 (a foto,
/// um material por camada, e o codigo dela rodando pela primeira vez). A ESPERA E DO MATERIAL, E NAO DO DESENHO, como a
/// do borrao: a miragem montada pela chamada de producao num palco FORA da arvore, sem nada desenhado, parou o quadro
/// 25,6 ms no preparo; o primeiro desenho, um segundo depois, custou 1,1 ms (25,3 com o driver frio) e trouxe so a
/// pipeline.
///
/// O CONSERTO e do `Aquecimento` (`AtosDaMiragem`): o shader entra na fila de carga do lobby, e o ensaio ganha um ato --
/// uma miragem de verdade, pela mesma `Zanzoken.Deixar`, de um boneco escondido (`Zanzoken.Ensaiar`), dos dois lados do
/// palco.
///
/// DEPOIS: o quadro da primeira miragem custa 3,2 a 4,4 ms de trabalho (catorze corridas) e cabe numa volta do monitor
/// (8,1 a 8,6 ms de relogio), com o cache do Godot vazio ou cheio, com o driver quente ou frio, e com a luz em cima do
/// corpo ou sem ela; nenhuma pipeline nasce nele, e a thread principal nao le arquivo nenhum. E NAO SOBRA CODIGO DE
/// PRIMEIRA VEZ, como sobra no borrao: o ato e a chamada do jogo, e nao um sprite solto, e a primeira miragem custa de
/// script o que custa uma qualquer (2,7 a 3,9 ms contra 1,8 a 3,3).
///
/// E PELO JOGO INTEIRO (`--pecas --zanzodeverdade`: o pedido do duplo clique vai ao servidor, e a miragem nasce quando o
/// anuncio dele volta, com o borrao do salto e o som do teleporte no mesmo quadro): 30,9 ms de trabalho (32,8 de relogio)
/// com o defeito injetado e 4,6 (8,3) sem ele; com o driver frio, 58,8 de relogio contra 8,2 -- uma corrida de cada, no
/// mesmo binario. NO DUELO de dois NPCs, com o gravador ao lado (`--trailer duelo --diagestouro --passivo 100`), a
/// miragem so aparece quando o sorteio da a tecnica a um deles: em duas corridas apareceu numa, e o quadro dela custou
/// 5,4 ms de trabalho (10,4 de relogio, com o golpe no mesmo quadro).
///
/// O PRECO, NO LOBBY (a tabela "O ENSAIO, ATO A ATO" desta rodada, com `--lobbyteste 4`): um arquivo de 4,7 KB a mais na
/// fila, um ato, duas pipelines (a do sprite sem luz e a do iluminado) e UM quadro de 39,5 ms onde haveria um de 8; o
/// ensaio inteiro foi de 896 a 902 ms pra 945 (duas corridas sem o ato, uma com ele). Com o cache do Godot cheio o
/// quadro do ato custa 13,7 ms; com o driver frio, 118,6 (as duas pipelines montadas do zero). NESSE QUADRO A ESPERA
/// NAO APARECE NO PREPARO, ao contrario do que acontece no ato do borrao e no jogo sem o conserto: a coluna do preparo
/// marca zero, e so o relogio do quadro inteiro mostra os ~27 ms. Em que ponto do quadro ela cai, ninguem isolou.
///
/// A REGUA E A DO BORRAO, COM AS MESMAS TRES PERNAS: o shader JA ESTAVA NA MEMORIA antes do primeiro gesto; NENHUMA
/// PIPELINE 2D NASCE no quadro, nem no seguinte; e o quadro CUSTA UM QUADRO NORMAL. O defeito injetado e o
/// `Aquecimento.SemEnsaioDaMiragemDeTeste` (`--pecas --semensaiodamiragem`): 32,8 a 34,8 ms de trabalho no mesmo binario,
/// 55,8 de relogio com o `--driverfrio Zanzoken` junto, e 107 com a luz tambem. NO `.bat`, O DRIVER FRIO VAI COM A
/// LUZ (`--pecas --raioscomluz --driverfrio Zanzoken`): e a gemea cara, e quem a monta e o lado iluminado do palco do
/// ensaio; o lado escuro e guardado pela rodada de dia, na perna da contagem.
///
/// E O CONTROLE NEGATIVO DA `--diagcarga` MUDOU DE ARQUIVO: ela usava o `Zanzoken.gdshader` como "o arquivo que o
/// aquecimento NAO carrega" (`RoboDeCarga.Familia1`). Passou pro `Ki.gdshader`, que nenhuma linha do jogo pede.
/// ====================================================================================================================
///
/// ============================ E OS SEIS QUE NENHUMA RODADA PUNHA NA TELA ============================
/// Fechados os raios da forma, o relampago, a chuva, o boneco, o borrao e a miragem, sobravam seis shaders do projeto
/// sem ato no ensaio e sem rodada: o da GOTA do transe, o da NEVOA de altitude, o da NEBULOSA do Ultra Instinto, o da
/// tela do EMBATE e os dois do planeta -- o disco visto do espaco e o ESTOURO. Ganharam rodada propria (`--avulsos`, o
/// arquivo `RoboDoPrimeiroEstouro.Avulsos.cs`): o primeiro uso de cada um sozinho num quadro, pela porta por onde o jogo
/// chega nele, e depois os do chao de novo com uma luz acesa em cima do corpo. MEDIDO em 2026-10-09 ANTES de qualquer
/// conserto, a maquina sozinha -- o quadro do primeiro uso, em relogio:
///
///                                         o DRIVER de video FRIO                o driver QUENTE      a gemea COM LUZ, montada depois
///                                         (script | preparo | tela)             (so o cache do       da sem luz, driver frio:
///                                                                               Godot vazio)         a TELA (e o quadro)
///     a gota do transe ................... 52 a 59 ms  (8-11 | 21-24 | 19-28)    34 a 35 ms           58 a 61 ms  (61 a 64)
///     o estouro de planeta, no mundo ..... 54 a 57     (7-8 | 24-26 | 21-23)     35 a 39              63 a 67     (68 a 75)
///     a nebulosa do Ultra Instinto ....... 74 a 81     (13-15 | 0 | 60-65)       15 a 17              0,5         (8: de graca)
///     a nevoa de altitude ................ 50 a 55     (6-7 | 24-26 | 19-21)     33                   60 a 62     (62 a 65)
///     a tela do embate ................... 23 a 31     (4 | 0 | 14-21)           8 a 10               (luz nenhuma a alcanca)
///     o planeta visto do espaco .......... 76 a 82     (a tela, 30 a 32)         54 a 59              73 a 79     (75 a 80)
///
/// (Oito corridas na primeira coluna: uma em que o driver nunca tinha visto os shaders DE VERDADE -- ninguem os tinha
/// desenhado nesta maquina -- e sete com o sal do `--driverfrio`, cinco delas com os defeitos injetados no binario de
/// depois; duas na segunda. A nevoa e o planeta sem luz o driver ja conhecia de outras bancadas, e so tem as do sal.
/// Numa das do sal a maquina estava ocupada, e a gota e a nebulosa sairam por 72 e 88 ms: ficaram fora das faixas.
/// "FRIO" e "QUENTE" sao os do bloco dos raios da forma. O quadro do planeta e o da CHEGADA na zona do espaco, que custa
/// 54 a 59 ms com tudo quente; em onze corridas de dezoito a cobertura de carregamento ainda estava no ar nele, e em
/// sete, nao. E COM O CACHE DE SHADER DO GODOT CHEIO TAMBEM -- o de quem ja jogou; uma corrida -- o defeito inteiro eram
/// 9 a 15 ms por quadro: a leitura do arquivo, e uma pipeline de 1 ms.)
///
/// O QUE SO A MEDIDA DISSE:
///   * QUATRO DELES NEM NA FILA DE CARGA ESTAVAM -- a gota, a nevoa e os dois do planeta: lidos do disco, compilados e
///     desenhados no quadro do primeiro uso, com 21 a 26 ms da thread principal esperando o shader. E o defeito do
///     borrao, e aparece com o driver quente tambem (a segunda coluna). Os outros dois estavam na lista pelo engano dos
///     do ki: o material da nebulosa nasce com cada corpo e o da tela do embate com o mundo, invisiveis, e so a pipeline
///     ficava pro primeiro desenho -- o instante em que a forma acende, o comeco do primeiro ZanzoClash;
///   * A GEMEA ILUMINADA CUSTA MAIS QUE O PRIMEIRO USO, 58 a 79 ms de tela: e a ordem cara do bloco de cima (a COM luz
///     montada depois da SEM luz). Quem usa o efeito de dia paga o primeiro uso, e paga de novo, mais caro, na primeira
///     vez com uma luz na tela. A da nebulosa sai de graca (0,5 ms, com o shader salgado). DEDUCAO, pelo que ha em
///     comum: o shader da nebulosa e `unshaded`, como o do quad da estrela do embate, que tambem saiu de graca -- sem
///     codigo de luz, as duas pipelines sao o mesmo codigo pro driver. Os quatro que custam nao sao `unshaded`;
///   * A GOTA E A NEVOA SAO ALCANCADAS POR QUALQUER LUZ DO MUNDO QUE ESTEJA NA TELA: sao retangulos de tela cheia numa
///     `CanvasLayer` de camada 0, que e a faixa de camadas das luzes (contagem do motor: nasce a pipeline iluminada com
///     uma luz acesa em cima do corpo). A tela do embate mora na camada 5, e la luz nenhuma chega: so tem a sem luz;
///   * NO ESPACO HA LUZ DE EFEITO: a zona nao tem ceu e fica no crepusculo parado (escuridao 0,62, forca da noite 0,66,
///     lidas com a zona assentada). A aura de uma forma e um tiro de ki acendem la, e alcancam o disco de um planeta
///     que esteja perto;
///   * O ESTOURO TEM DUAS CASAS E UMA PIPELINE: o quad do `World.EstouroNoMundo` (a Final Explosion, a Aura da
///     Destruicao, o tremor de um mundo que morre) e o do planeta que estoura no espaco sao o mesmo item com o mesmo
///     shader -- desenhado um, nenhuma pipeline nasce no outro (o controle da rodada, em dezessete corridas).
///
/// O CONSERTO e do `Aquecimento` (`AtosDaGota` e as quatro listas irmas): os quatro shaders lidos na hora entram na fila
/// de carga do lobby, e o ensaio ganha seis atos -- cada efeito pela receita de producao dele (`GotaNaTela.Ensaiar`,
/// `PlanetaDesenhado.Ensaiar` e `EnsaiarOEstouro`, `NebulosaDaForma.Ensaiar`, `NevoaDeAltitude.Ensaiar`,
/// `ClashQte.Ensaiar`), dos dois lados do palco; a tela do embate, so do lado escuro.
///
/// DEPOIS (sete corridas, seis delas com o driver frio): nenhuma pipeline nasce em nenhum dos onze quadros, com a luz
/// ou sem ela, com o login de robo ou com o de gente; os quatro shaders ja estao na memoria antes do primeiro gesto; e o
/// quadro do primeiro uso custa 9 a 10 ms na gota, 8 a 9 no estouro e na nevoa, 9 a 15 no embate e 8 a 10 nas gemeas
/// iluminadas. OS DOIS QUE CONTINUAM ACIMA DE UMA VOLTA DO MONITOR NAO SAO MAIS SHADER: o da nebulosa custa 14 a 18 ms,
/// dos quais 13 a 17 de script -- o campo de distancia da silhueta construido pela primeira vez
/// (`NebulosaDaForma.Modelar`) --, e o da chegada ao espaco 12 a 30, a zona montando.
///
/// O PRECO, NO LOBBY (a tabela "O ENSAIO, ATO A ATO" da rodada `--pecas`, com `--lobbyteste 4`): quatro arquivos a mais
/// na fila (26 KB ao todo), seis atos, onze pipelines, e seis quadros de 30 a 38 ms onde haveria seis de 8 -- 196 ms ao
/// todo, quase tudo a espera dos shaders (25 a 33 ms cada, 17 o da nebulosa). Com o driver frio os seis somam 615 ms (49
/// a 138 cada: as duas pipelines de cada um montadas do zero). O ensaio inteiro foi de 946 pra 1145 ms, e pra 1545 com o
/// driver frio (uma corrida de cada, a primeira com os cinco defeitos injetados no mesmo binario). E ali que quem
/// acabou de instalar passa a pagar, atras da tela de login.
///
/// A REGUA e a dos primeiros desenhos, nos onze quadros (NENHUMA PIPELINE 2D NASCE, e o desenho custa um desenho
/// normal), e mais duas pernas: o arquivo dos quatro shaders JA ESTAVA NA MEMORIA antes do primeiro gesto, e o quadro
/// CUSTA UM QUADRO NORMAL na gota, no estouro, na nevoa e no embate (os da nebulosa e do espaco saem como numero: ver
/// acima). O item de cada uso tem que estar NA TELA -- o disco do planeta entra nela ate dois quadros depois de nascer,
/// e a regua dele olha dois quadros a mais. Os defeitos injetados sao os cinco `Aquecimento.SemEnsaioDa...DeTeste`
/// (`--semensaiodagota`, `--semensaiodoplaneta`, `--semensaiodanebulosa`, `--semensaiodanevoa`, `--semensaiodoembate`):
/// com os cinco na linha, a mesma regua reprova os onze quadros e os quatro arquivos.
///
/// O QUE FICOU, e nao e shader. O quadro da PRIMEIRA LETRA do embate, tres quadros depois do comeco dele: 17 a 48 ms de
/// relogio em doze corridas de catorze, com 5 a 25 de script e uma pipeline que custa menos de 1 ms de desenho -- com o
/// conserto e sem ele; ninguem abriu. O primeiro Ultra Instinto e a chegada ao espaco, ditos acima. E o estouro de um
/// planeta no espaco, 10 a 15 ms, quase tudo script. O `World.EstouroNoMundo` continua montando o quad dele por conta
/// propria (quem segura o shader pra ele e a fila do aquecimento).
/// ====================================================================================================================
///
/// ============================ O QUE SE MEDE, E COM QUE REGUA ============================
/// O TEMPO DE CADA QUADRO, partido em dois por dois ganchos de prioridade extrema (<see cref="MarcaDeQuadro"/>):
///
///   * a fase de SCRIPT -- do primeiro `_Process` do quadro ao ultimo. E onde cai o que o C# faz na
///     hora: carregar um recurso, montar um material (e, com ele, compilar o shader);
///   * o RESTO -- do ultimo `_Process` deste quadro ao primeiro do seguinte. E onde cai o DESENHO, e
///     dentro dele a montagem da pipeline no primeiro uso. O Godot mede a parte de CPU do desenho
///     (`ViewportGetMeasuredRenderTimeCpu`), e e ela que entra na conta: o resto do resto e esperar o monitor.
///
/// A REGUA e o TRABALHO do quadro: script (sem as pausas do coletor do .NET, que sao medidas uma a uma
/// e nao sao do estouro) mais a CPU do desenho. Ele nao pode passar do trabalho de um quadro parado mais
/// uma folga -- <see cref="FolgaDoEfeito"/> pra um efeito, <see cref="FolgaDoNascimento"/> pro quadro em
/// que um tiro nasce. Ao lado, o quadro INTEIRO em relogio de parede nao pode perder mais de duas voltas
/// do monitor -- e o que pega um custo que nao esteja nem no script nem no desenho.
///
/// O QUE SE COBRA E O QUADRO EM QUE O EFEITO NASCE, e so ele: foi nele que todo custo de primeiro uso
/// medido caiu (o primeiro estouro: 359 ms no quadro do nascimento, 8 no seguinte). Os tres seguintes
/// saem impressos ao lado, sem entrar na conta -- com eles na regua ela pegava carona de quem nao tinha
/// nada com o efeito: o processo hospeda o servidor, e um quadro solto gasta 11 a 13 ms de script de vez
/// em quando (uma ferida que muda o sprite do alvo, um tique mais gordo). Todo quadro caro da corrida,
/// com rotulo ou sem, vai listado no fim.
///
/// E relogio de parede, e nao o `delta`: o Godot poda o passo que entrega ao `_Process` (oito tiques de
/// fisica por quadro, 133 ms a 60 Hz), entao um quadro de 380 ms chega como um `delta` de 134 -- os dois
/// saem impressos lado a lado.
/// ========================================================================================
///
/// ============================ AS RODADAS ============================
/// O custo e de UMA VEZ POR PROCESSO, entao cada pergunta e um processo:
///
///     --diagestouro                    a bola de producao, sete vezes: a primeira bola, o primeiro estouro, o
///                                      segundo e o terceiro (o controle), o quarto depois de a poeira sumir e o
///                                      coletor passar, e o defeito injetado na poeira (`SemSegurarDeTeste`). E
///                                      TRES LUTAS pra trilha: o primeiro golpe da primeira e o da segunda com a
///                                      faixa ja na memoria, e o da terceira com o defeito injetado na musica
///                                      (`AudioDirector.SemThreadDeCargaDeTeste`)
///     --diagestouro --lobbyteste 4     a mesma, com o login de GENTE: quatro segundos de lobby antes de discar.
///                                      O ensaio inteiro cabe no lobby; com o login de robo os atos dele caem
///                                      nos primeiros quadros do mundo, junto com a montagem
///     --diagestouro --pecas            os OUTROS primeiros usos, um por segundo: o anel da deflexao, o raio, a
///                                      estrela do embate, o escudo do parry e a faisca do golpe -- e o PRIMEIRO
///                                      BORRAO do processo (o rastro de um arranque, pela porta de producao) e a
///                                      PRIMEIRA MIRAGEM (o vulto do Zanzoken, pela chamada do `World.AoPiscar`), cada
///                                      um com um segundo de controle. No relatorio sai tambem o ensaio do lobby, ato
///                                      a ato
///     ... --pecas --semensaiodoborrao  O DEFEITO INJETADO NO BORRAO (`Aquecimento.SemEnsaioDoBorraoDeTeste`, ligado
///                                      pelo `Boot`): o aquecimento sem o shader do borrao na fila e sem o ato dele,
///                                      o jogo de antes. As tres pernas da regua do primeiro borrao tem que REPROVAR
///     ... --pecas --driverfrio Borrao  O DRIVER DE VIDEO NUNCA VIU O SHADER DO BORRAO: a regua tem que PASSAR (o
///                                      ensaio pagou no lobby); com `--semensaiodoborrao` junto, REPROVAR com a
///                                      pipeline montada do zero no quadro do primeiro borrao
///     ... --pecas --borraopartido      A MEDICAO EM PECAS, sem regua: o arquivo, o primeiro material e o primeiro
///                                      desenho do borrao, cada um sozinho num quadro, antes do primeiro borrao. Com
///                                      `--semensaiodoborrao` e a conta partida do jogo de antes
///     ... --pecas --semensaiodamiragem O DEFEITO INJETADO NA MIRAGEM (`Aquecimento.SemEnsaioDaMiragemDeTeste`, ligado
///                                      pelo `Boot`): o aquecimento sem o shader da miragem na fila e sem o ato dela,
///                                      o jogo de antes. As tres pernas da regua da primeira miragem tem que REPROVAR
///     ... --pecas --driverfrio Zanzoken   O DRIVER DE VIDEO NUNCA VIU O SHADER DA MIRAGEM: a regua tem que PASSAR; com
///                                      `--semensaiodamiragem` junto, REPROVAR com a pipeline montada do zero. Com
///                                      `--raioscomluz` na linha a pipeline e a gemea do sprite iluminado, a cara: e
///                                      assim que a rodada esta no `.bat`
///     ... --pecas --miragempartida     A MEDICAO EM PECAS da miragem, sem regua: o arquivo, os primeiros materiais (a
///                                      miragem inteira montada FORA da arvore) e o primeiro desenho, cada um sozinho
///                                      num quadro. Com `--semensaiodamiragem` e a conta partida do jogo de antes
///     ... --pecas --zanzodeverdade --skillteste /datum/skill/ki/Afterimage
///                                      A MIRAGEM PELO JOGO INTEIRO: a peca pede um Zanzoken ao servidor (o pacote do
///                                      duplo clique no chao), e a miragem nasce quando o anuncio volta, com o borrao
///                                      do salto e o som do teleporte no mesmo quadro. A mesma regua, as mesmas flags
///     ... --semensaio                  O DEFEITO INJETADO (`Aquecimento.SemEnsaioDeTeste`): o jogo de antes. A
///                                      mesma regua tem que REPROVAR a primeira bola e o primeiro estouro
///     ... --semaquecimento             o contra-exemplo natural: o processo frio inteiro (ver `RoboDeCarga`)
///     ... --horateste 0.9 --estouronoite   de noite: os efeitos passam a ser desenhados com luz em cima
///     --diagestouro --temas            OS TEMAS QUE ENTRAM FORA DA LUTA, um gesto a cada segundo e meio: o ESC
///                                      tres vezes e a estreia de tres formas. O ESC 1, o ESC 2 e as estreias do
///                                      `ssj1` e do `ssj2` sao a regua; o ESC 3 e a estreia do `blue` saem com o
///                                      defeito injetado (`AudioDirector.SemThreadDeCargaDeTeste`). Com
///                                      `--raca Demon` o berco e o Inferno, e o tema do lugar sai medido tambem.
///                                      Antes das estreias a PRIMEIRA CINEMATICA do processo roda inteira (a
///                                      encurtada do `ssj1`, 11,6 s): o quadro em que ela nasce e o do beat que
///                                      ASSUME sao regua, e o ultimo gesto confere quem segura o que ela usou
///     ... --temas --semthreaddecarga   O DEFEITO INJETADO A RODADA INTEIRA: o jogo de antes, no mesmo binario. A
///                                      mesma regua tem que REPROVAR os seis temas -- e e desta rodada que sai o
///                                      numero de antes, faixa a faixa
///     ... --temas --semensaiodacena    O DEFEITO INJETADO NA PRIMEIRA CINEMATICA (`Aquecimento.SemEnsaioDaCenaDeTeste`,
///                                      ligado pelo `Boot`): o aquecimento sem as folhas de chama e sem o ensaio da
///                                      cena, o jogo de antes. A mesma regua tem que REPROVAR o quadro em que a
///                                      primeira cinematica nasce -- e e desta rodada que sai o numero de antes dele
///     ... --temas --semadiantaracena   O DEFEITO INJETADO NO BEAT QUE ASSUME (`Aquecimento.SemAdiantarACenaDeTeste`,
///                                      ligado pelo `Boot`): nada do que a cena usa depois de nascer chega antes da
///                                      hora, o jogo de antes. As tres pernas da regua dele tem que REPROVAR -- o
///                                      quadro do beat que assume, a espera por folhas e a conferencia dos donos
///     ... --temas --semensaiodosraios  O DEFEITO INJETADO NOS RAIOS DA FORMA (`Aquecimento.SemEnsaioDosRaiosDeTeste`,
///                                      ligado pelo `Boot`): o aquecimento sem os dois atos que desenham um raio de
///                                      forma no lobby, o jogo de antes. A regua dos dois quadros tem que REPROVAR:
///                                      uma pipeline nasce em cada um
///     ... --temas --driverfrio         O DRIVER DE VIDEO NUNCA VIU O SHADER DOS RAIOS: o `RaioDaForma.gdshader` ganha
///                                      no lobby uma constante que vale zero em todo pixel, e a pipeline dele e
///                                      montada do zero, como na maquina de quem acabou de instalar. E com ele que a
///                                      perna do DESENHO da regua dos raios morde: sozinho, a regua tem que PASSAR (o
///                                      ensaio pagou no lobby); com `--semensaiodosraios`, REPROVAR com os 19 a 23 ms
///     ... --temas --raioscomluz        UMA LUZ EM CIMA DO CORPO, acesa no meio da base: os tres quadros da regua (e
///                                      a cena inteira) passam pelas pipelines do item iluminado -- as da noite ao
///                                      lado de uma fogueira, ou de debaixo de um teto. Confere o lado iluminado do
///                                      palco do ensaio, e cobra que a luz tenha pegado
///     ... --temas --horateste 0.9 --estouronoite
///                                      A RODADA DOS TEMAS DE NOITE: o login de robo cai num mundo ESCURO, e o ensaio
///                                      roda nos primeiros quadros dele. A regua dos tres quadros tem que PASSAR: o
///                                      lado escuro do palco continuou escuro
///     ... (de noite) --estrelacomluz   O DEFEITO INJETADO NO LADO ESCURO (`Aquecimento.EstrelaComLuzDeTeste`, ligado
///                                      pelo `Boot`): a estrela do embate do ensaio com a luz propria dela, o jogo de
///                                      antes. A regua tem que REPROVAR os tres quadros
///     ... --driverfrio Raio+Queda      O `--driverfrio` COM LISTA: nomes de arquivo da pasta dos shaders, sem a
///                                      extensao, separados por `+` ou por virgula -- o driver nunca viu NENHUM deles
///                                      nesta corrida. Vale em qualquer rodada: `Raio` na dos temas (o relampago),
///                                      `Personagem` na bola de noite (o boneco com luz), `ChoqueDeKi` na das pecas
///     ... --temas --semensaiodorelampago
///                                      O DEFEITO INJETADO NO RELAMPAGO (`Aquecimento.SemEnsaioDoRelampagoDeTeste`,
///                                      ligado pelo `Boot`): o aquecimento sem o risco do raio, o jogo de antes. A regua
///                                      tem que REPROVAR o quadro do primeiro relampago, o da tempestade da estreia do
///                                      `ssj1` -- com `--driverfrio Raio`, pelos 25 a 41 ms
///     ... --semensaiodocorpo           O DEFEITO INJETADO NO BONECO (`Aquecimento.SemEnsaioDoCorpoDeTeste`, ligado
///                                      pelo `Boot`): o aquecimento sem o boneco no palco, o jogo de antes. Na bola de
///                                      NOITE (e na `--temas --raioscomluz`) a regua tem que REPROVAR o quadro do
///                                      primeiro corpo iluminado -- com `--driverfrio Personagem`, pelos 100 ms
///     --diagestouro --temas --ceu --climateste Nublado
///                                      A RODADA DO CEU: a bancada forca uma tempestade no MEIO do jogo e, dois segundos
///                                      depois, faz um raio cair a 3 tiles do corpo. Os quadros da primeira chuva e do
///                                      primeiro relampago tem que PASSAR. Nao toca cinematica nem tema; o `--climateste
///                                      Nublado` segura o ceu sem chuva ate a base (o clima natural e do relogio)
///     ... --ceu --semensaiodachuva --semensaiodorelampago
///                                      O DEFEITO INJETADO NA CHUVA (`Aquecimento.SemEnsaioDaChuvaDeTeste`, ligado pelo
///                                      `Boot`) e o do relampago: o jogo de antes. A regua tem que REPROVAR os dois
///     --diagestouro --pecas --estrelapartida
///                                      A PRIMEIRA ESTRELA DO EMBATE EM PEDACOS, sem regua: as pedras, as faiscas e o
///                                      quad pintado entram na tela em tres quadros, e o relatorio conta as pipelines
///                                      de cada um. Ver o arquivo `RoboDoPrimeiroEstouro.Nascidas.cs`
///     --diagestouro --temas --cenas ssj1,ssj2,blue
///                                      A MEDICAO DAS CENAS, sem regua: cada cena da lista do nascimento ao fim, um
///                                      evento por beat, com o desenho partido em preparo e tela e o `_Process` da
///                                      cena partido em pedacos; `--cenapartida` nasce antes cada peca do primeiro
///                                      quadro sozinha, `--cenacoleta` forca o coletor do .NET entre uma cena e a
///                                      seguinte, e com `--verbose` cada arquivo lido pela thread principal ganha o
///                                      quadro dele. Ver o arquivo `RoboDoPrimeiroEstouro.Cena.cs`
///     --diagestouro --passivo 20       O GRAVADOR SOZINHO, ao lado de outro robo (`--trailer ki ...`): nao dispara
///                                      nem cobra nada, so lista todo quadro caro da cena dele com o que chegou -- e
///                                      conta os SONS DE EFEITO dela: os toques, e cada vez que a thread principal
///                                      leu o arquivo de um do disco. Ver o arquivo `RoboDoPrimeiroEstouro.Sons.cs`
///     --diagestouro --sons             OS SONS DE EFEITO: cada som de gesto toca duas vezes pela porta de producao,
///                                      com uma coleta forcada do .NET no meio. O primeiro toque nao espera pelo
///                                      arquivo, depois da coleta todos continuam na memoria, e o segundo toque nao
///                                      volta ao disco
///     ... --sons --semsegurarossons    O DEFEITO INJETADO NOS SONS (`SonsPresos.SemSegurarDeTeste`, ligado pelo
///                                      `Boot`): o jogo de antes. As tres pernas da regua tem que REPROVAR
///     --diagestouro --scripts          OS SCRIPTS DE NODE: cada classe de efeito que uma luta nasce e mata e medida
///                                      sozinha -- o `new` dela com o script sem dono e com dono, pelado e pela porta
///                                      de producao. O script de todas tem dono, nenhuma o carrega de novo ao nascer, e
///                                      o `new` custa o de um node. Com `--passivo` na linha e so a conta: cada script
///                                      que o motor carregou na cena do outro robo. Ver o arquivo
///                                      `RoboDoPrimeiroEstouro.Scripts.cs`
///     ... --scripts --semsegurarosscripts
///                                      O DEFEITO INJETADO NOS SCRIPTS (`Aquecimento.SemSegurarOsScriptsDeTeste`, ligado
///                                      pelo `Boot`): o jogo de antes. As quatro pernas da regua tem que REPROVAR
///     --diagestouro --avulsos --mudezteste --vooteste
///                                      OS PRIMEIROS USOS AVULSOS, um gesto por vez: a gota do transe, o estouro de
///                                      planeta, a nebulosa do Ultra Instinto, a nevoa de altitude (o corpo decola), a
///                                      tela do embate (um ZanzoClash de verdade: e pra ele o `--mudezteste`) e o
///                                      planeta visto do espaco. Os quatro do chao se repetem com uma luz acesa em cima
///                                      do corpo. Nenhuma pipeline nasce em nenhum dos onze quadros, e o shader de cada
///                                      um ja veio do lobby. Ver o arquivo `RoboDoPrimeiroEstouro.Avulsos.cs`
///     ... --avulsos --driverfrio Gota+EstouroDePlaneta+NebulosaDaForma+Altitude+Embate+PlanetaMorrendo
///                                      O DRIVER DE VIDEO NUNCA VIU NENHUM DOS SEIS: a regua tem que PASSAR (o ensaio
///                                      pagou no lobby as onze pipelines)
///     ... --avulsos --semensaiodagota --semensaiodoplaneta --semensaiodanebulosa --semensaiodanevoa --semensaiodoembate
///                                      OS CINCO DEFEITOS INJETADOS NOS AVULSOS (os `Aquecimento.SemEnsaioDa...DeTeste`
///                                      de mesmo nome, ligados pelo `Boot`): o aquecimento sem os atos e sem os shaders
///                                      na fila, o jogo de antes. A regua tem que REPROVAR os onze quadros -- com o
///                                      `--driverfrio` junto, pelos 23 a 82 ms de cada um
///
/// COMO RODAR -- um processo, com `--host` (quem tem projetil e o servidor) e com JANELA (no headless
/// nao ha desenho, e a travada nao existe):
///
///     python Trailer/bruto/bancada.py estouro "\[estouro\] ===== (TUDO OK|\d+ FALHA)" --janela -- --host \
///         --rede 7958 --semfoco --campoteste 8 --horateste 0.5 --diagestouro \
///         --raca Human --conta bancada_estouro --nome Estouro
///
/// (a rodada dos temas estreia formas da escada Saiyajin: `... --diagestouro --temas --raca Saiyan`)
/// </summary>
public partial class RoboDoPrimeiroEstouro : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	/// <summary>
	/// Quanto o trabalho do quadro em que um EFEITO nasce (estouro, anel, estrela, escudo, faisca) pode passar
	/// do de um quadro parado, em ms. Em regime ele passa de 1 a 6: sao os nodes entrando na arvore. O menor
	/// defeito que a bancada injeta (a poeira recompilando os tres shaders do cache em disco) passa de 19 a 27.
	/// </summary>
	private const double FolgaDoEfeito = 12.0;

	/// <summary>
	/// A mesma folga pro quadro em que um TIRO nasce, e ela e maior de proposito. Esse quadro nao e so do
	/// cliente: o processo hospeda o servidor (`--host`), e o primeiro disparo roda pela primeira vez todo o
	/// caminho de projetil dos dois lados; de noite ele ainda acende as primeiras luzes de ki no chao em
	/// volta. Com o ensaio feito a primeira bola passa de 6 a 11 ms da base e o primeiro raio de 6 a 12 de
	/// dia e 17 de noite; sem ele (shader compilado e pipeline montada na hora) passam de 50 a 70.
	/// </summary>
	private const double FolgaDoNascimento = 24.0;

	/// <summary>UM QUADRO, como os dois ganchos o viram.</summary>
	private struct Quadro
	{
		public ulong Comeco, Fim;                    // usec no primeiro e no ultimo `_Process` do quadro
		public double Delta;                         // o passo que o Godot entregou a ESTE quadro
		public ulong Canvas;                         // pipelines 2D montadas ate aqui
		public double DesenhoCpu;                    // ms de CPU do desenho do quadro ANTERIOR, medidos pelo Godot
		public double PausaNoComeco, PausaNoFim;     // ms que o coletor do .NET ja parou o processo, somados
		public string Marcas;
	}

	private readonly List<Quadro> _quadros = new(32768);
	private readonly List<string> _marcasDoQuadro = [];
	private ulong _comecoDoQuadro;
	private double _pausaNoComeco;
	private Rid _tela;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

	private bool _acabou;
	private double _t, _vida;
	private int _passo;

	private const double Paciencia = 150;

	private int _alvo, _tiro, _tiros, _estouros;
	private readonly HashSet<ulong> _estourosVistos = [];

	/// <summary>O que cada coisa medida e pra bancada: regua, defeito injetado (a regua tem que reprovar) ou so numero.</summary>
	private enum Papel { Regua, Injetado, Nota }

	private readonly List<(string Oque, int Quadro, Papel Papel, string Defeito, bool Nascimento)> _eventos = [];

	private int _baseDe = -1, _baseAte = -1, _entrouNoMundo = -1;

	private static bool Tem(string flag) => Array.IndexOf(OS.GetCmdlineArgs(), flag) >= 0;

	private readonly bool _pecas = Tem("--pecas");

	/// <summary>`--temas`: a rodada dos temas que entram FORA da luta -- o ESC e a estreia de uma forma. Ver <see cref="AndarNosTemas"/>.</summary>
	private readonly bool _temas = Tem("--temas");

	/// <summary>
	/// `--temas --semthreaddecarga`: A RODADA DE INJECAO dos temas -- o processo inteiro com o defeito
	/// `AudioDirector.SemThreadDeCargaDeTeste`, o jogo de antes: toda faixa lida do disco pela thread principal no
	/// quadro em que entra. A mesma regua tem que REPROVAR os seis temas, e a rodada da o numero de antes no
	/// mesmo binario, faixa a faixa.
	/// </summary>
	private readonly bool _semThreadDeCarga = Tem("--semthreaddecarga");

	/// <summary>
	/// `--temas --semensaiodacena`: A RODADA DE INJECAO da primeira cinematica -- o processo entra no mundo sem as
	/// folhas e sem o ensaio da cena (`Aquecimento.SemEnsaioDaCenaDeTeste`, ligado pelo `Boot` na linha em que o
	/// aquecimento nasce): o jogo de antes. A mesma regua tem que REPROVAR o quadro em que a primeira cinematica
	/// nasce, e e desta rodada que sai o numero de antes, no mesmo binario.
	/// </summary>
	private readonly bool _semEnsaioDaCena = Tem("--semensaiodacena");

	/// <summary>
	/// `--temas --semadiantaracena`: A RODADA DE INJECAO do beat que ASSUME -- o processo entra no mundo com o defeito
	/// `Aquecimento.SemAdiantarACenaDeTeste` (ligado pelo `Boot` na linha em que o aquecimento nasce): o jogo de
	/// antes, em que a cinematica le do disco, na hora, cada coisa que usa depois de nascer. A mesma regua tem que
	/// REPROVAR o quadro do beat que assume e a conferencia dos donos, e e desta rodada que sai o numero de antes.
	/// </summary>
	private readonly bool _semAdiantarACena = Tem("--semadiantaracena");

	/// <summary>
	/// `--passivo &lt;s&gt;`: O GRAVADOR SOZINHO. Nao dispara nada e nao cobra nada: grava os primeiros segundos do
	/// corpo no mundo e lista todo quadro caro com o que chegou nele. E como se mede a cena de OUTRO robo -- o
	/// diretor do trailer, por exemplo (`--trailer ki --diagestouro --passivo 20`). A travada que la so se achava
	/// comparando quadros do video (`engasgos.py`) sai aqui com o tamanho, a fase em que caiu e o rotulo.
	/// </summary>
	private readonly double _passivo = Passivo();

	private static double Passivo()
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, "--passivo");
		return i >= 0 && i + 1 < a.Length
			   && double.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s)
			? s : 0;
	}

	/// <summary>
	/// A RODADA DE INJECAO: o processo entra no mundo sem o ensaio dos efeitos de ki. `--semensaio` e o
	/// defeito de antes cravado (os recursos carregam, o ensaio nao roda); `--semaquecimento` e o processo
	/// frio inteiro, que ja existia.
	/// </summary>
	private readonly string? _semEnsaio =
		Tem("--semensaio") ? "o aquecimento sem o ensaio dos efeitos de ki"
		: Tem("--semaquecimento") ? "o processo sem aquecimento nenhum (`--semaquecimento`)" : null;

	private int _shadersEmDiscoNoComeco;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	private void Marcar(string oque) => _marcasDoQuadro.Add(oque);

	/// <summary>O quadro corrente e o de uma coisa medida: e nele que o custo dela aparece.</summary>
	private void Evento(string oque, Papel papel, string defeito = "", bool nascimento = false)
	{
		Marcar(oque);
		_eventos.Add((oque, _quadros.Count, papel, defeito, nascimento));
	}

	/// <summary>
	/// O papel de um PRIMEIRO uso: regua no jogo de verdade, defeito injetado na rodada sem ensaio.
	///
	/// COM O CACHE DE SHADER EM DISCO CHEIO so o primeiro ESTOURO e cobrado como defeito injetado. Sem o ensaio
	/// ele ainda custa 40 a 48 ms contra um teto de 15; a primeira bola cai pra 24 a 27, que e o proprio teto
	/// dela, e uma regua que reprova por um milissegundo nao prova nada -- ela sai como numero. Com o cache
	/// vazio (o de toda bancada rodada pelo `bancada.py`, que comeca de uma pasta nova) os dois passam de 50 e
	/// os dois sao cobrados.
	/// </summary>
	private Papel PrimeiroUso(bool oEstouro = false) =>
		_semEnsaio == null ? Papel.Regua
		: oEstouro || _shadersEmDiscoNoComeco == 0 ? Papel.Injetado : Papel.Nota;

	public override void _Ready()
	{
		// O DEFEITO DA RODADA `--semensaio` JA ENTROU: quem liga o `Aquecimento.SemEnsaioDeTeste` e o `Boot`, na
		// linha em que o aquecimento nasce -- o ensaio e do lobby, e este robo nao poderia chegar antes dele.
		// Sai aqui: no `finally` da conferencia e, se a janela for fechada na mao, no `_ExitTree`.

		AddChild(new MarcaDeQuadro { Name = "ComecoDoQuadro", ProcessPriority = -100_000, Agora = NoComecoDoQuadro });
		AddChild(new MarcaDeQuadro { Name = "FimDoQuadro", ProcessPriority = 100_000, Agora = NoFimDoQuadro });

		_tela = GetViewport().GetViewportRid();
		RenderingServer.ViewportSetMeasureRenderTime(_tela, true);
		_shadersEmDiscoNoComeco = ShadersDeParticulaEmDisco();

		AudioDirector.EspiaoDeMusica += AoTrocarAFaixa;
		// (o gravador sozinho e a rodada dos sons contam cada som de efeito que toca e cada ida ao disco: ver `OuvirOsSons`)
		OuvirOsSons();
		// (com `--scripts` na linha, uma sonda conta cada script de node que o motor carrega, quadro a quadro: ver `OuvirOsScripts`)
		OuvirOsScripts();
		// (a rodada dos temas parte em pedacos o `_Process` de cada cinematica que ela toca: ver `AoMedirPedaco`)
		if (_temas) Transformacao.EspiaoDePedaco += AoMedirPedaco;
		if (C is { } cli)
		{
			cli.TiroNasceu += AoNascerTiro;
			cli.TiroMorreu += AoMorrerTiro;
			cli.Golpe += AoGolpe;
			cli.FeridasMudaram += AoMudarFeridas;
		}

		// A FAIXA DA PRIMEIRA LUTA E ESCOLHIDA AQUI, NO LOBBY: antes de o mundo existir e pedi-la a thread de
		// carga. Ver `PorFaixaGrandeNaFila`. (O gravador sozinho so pesa a pasta: o saco e da cena que ele assiste.)
		// A rodada dos temas faz o mesmo com o saco do MENU, e nao encosta no de combate: ver `PrepararOsTemas`.
		if (_temas) PrepararOsTemas();
		else
		{
			// (a rodada dos avulsos, como a das pecas, nao mede a musica de combate: nao ha faixa pra pesar nem pra por na fila)
			if (!_pecas && !_avulsos) PesarAPasta(PastaDeCombate);
			if (_passivo <= 0 && !_pecas && !_avulsos) PorFaixaGrandeNaFila();
		}
	}

	// =====================================================================
	// A MUSICA DE COMBATE -- o primeiro golpe de cada luta
	// =====================================================================
	/// <summary>
	/// Quanto a thread principal pode ESPERAR pela faixa de combate no quadro em que ela entra no ar, em ms. E
	/// o cronometro do `AudioDirector.Cruzar`: com a faixa ja na memoria ele marca 0,0x; lida do disco na hora
	/// marcou de 1,9 a 12,8 ms (e uma vez 23,8) nas 38 faixas da pasta, e nunca menos de 3,1 nas de 2 MB pra
	/// cima -- as que esta bancada poe na fila (2026-10-08).
	/// </summary>
	private const double EsperaDaFaixa = 1.0;

	/// <summary>
	/// O tamanho a partir do qual uma faixa serve pra MEDIR a leitura, em bytes. A pasta `battle ost` vai de 0,5
	/// a 3,3 MB e o custo de ler o mp3 acompanha o tamanho: deixada pro sorteio, a mesma corrida mediria 2 ms
	/// numa vez e 10 na outra. Seis das 38 faixas passam daqui.
	/// </summary>
	private const long FaixaGrande = 2_000_000;

	private const string PastaDeCombate = "res://Assets/Sounds/Music/battle ost";

	/// <summary>O tamanho de cada faixa da pasta, medido no lobby: abrir arquivo no quadro de um golpe entraria na conta dele.</summary>
	private readonly Dictionary<string, long> _bytes = [];

	/// <summary>As faixas de combate que este processo ja pos no ar. Essas estao na memoria: nao servem pra medir leitura.</summary>
	private readonly HashSet<string> _faixasTocadas = [];

	/// <summary>A faixa de combate que entrou no ar NESTE quadro, ate o fim dele: e la que o cronometro do `Cruzar` ja fechou.</summary>
	private string? _faixaQueEntrou;

	/// <summary>
	/// Quantas LUTAS a corrida ja abriu -- pra trilha, quantas vezes a tag de combate deste cliente subiu -- e se
	/// a ultima ainda esta de pe.
	/// </summary>
	private int _lutas;
	private bool _lutaAberta, _pediAQueda;

	/// <summary>Quantos relatos de golpe no alvo chegaram. Tem que ser um por bola -- ver <see cref="DanoDaBola"/>.</summary>
	private int _golpes;

	/// <summary>O que o cronometro do `Cruzar` marcou em cada evento de musica, pelo indice do evento em <see cref="_eventos"/>.</summary>
	private readonly Dictionary<int, (double Espera, double Tocador)> _entradas = [];

	/// <summary>Pesa cada faixa da pasta. Devolve quantas ela tem.</summary>
	private int PesarAPasta(string pasta)
	{
		int quantas = 0;
		foreach (string bruto in DirAccess.GetFilesAt(pasta))
		{
			string nome = bruto;
			if (nome.EndsWith(".remap")) nome = nome[..^6];
			if (nome.EndsWith(".import")) nome = nome[..^7];
			string caminho = $"{pasta}/{nome}";
			if (_bytes.ContainsKey(caminho)) continue;
			if (PesarAFaixa(caminho)) quantas++;
		}
		return quantas;
	}

	private bool PesarAFaixa(string caminho)
	{
		using Godot.FileAccess? f = Godot.FileAccess.Open(caminho, Godot.FileAccess.ModeFlags.Read);
		if (f == null) return false;
		_bytes[caminho] = (long)f.GetLength();
		return true;
	}

	/// <summary>
	/// A PROXIMA FAIXA DE COMBATE DO SACO PASSA A SER UMA GRANDE QUE ESTE PROCESSO AINDA NAO TOCOU: as que vem
	/// antes dela saem do saco sem tocar.
	///
	/// SO SE CHAMA COM NADA PEDIDO A THREAD DE CARGA -- no lobby (o mundo ainda nao existe pra pedir faixa
	/// nenhuma) e com o defeito `AudioDirector.SemThreadDeCargaDeTeste` ligado. Fora disso o `AudioDirector`
	/// ja pediu a faixa que estava na frente, e descarta-la aqui faria a luta seguinte abrir com outra, lida na
	/// hora: o defeito, fabricado pela propria bancada.
	/// </summary>
	private void PorFaixaGrandeNaFila()
	{
		// duas voltas do saco alcancam qualquer faixa dele; a folga e pelo resto da rodada em curso
		for (int i = 0; i < _bytes.Count * 2 + 4; i++)
		{
			string proxima = Trilha.ProximaDeCombate();
			if (proxima.Length == 0) return;
			if (_bytes.GetValueOrDefault(proxima) >= FaixaGrande && !_faixasTocadas.Contains(proxima)) return;
			Trilha.Combate();
		}
	}

	private void AoTrocarAFaixa(double t, AudioDirector.Camada camada, string faixa, string motivo)
	{
		if (faixa.Length == 0) return;
		// NA RODADA DOS TEMAS so interessa a faixa que o gesto da propria bancada pediu -- ver `PorOTemaNoAr`.
		if (_temas) { if (_pedindoOTema) _temaPedido = faixa; return; }
		if (camada == AudioDirector.Camada.Combate) _faixaQueEntrou = faixa;
	}

	/// <summary>
	/// UMA FAIXA DE COMBATE ENTROU NO AR NESTE QUADRO, e o cronometro do `AudioDirector.Cruzar` diz quanto a
	/// thread principal esperou pelo arquivo dela.
	///
	/// A QUE ABRE UMA LUTA E O RELATO DO PRIMEIRO GOLPE: quem a poe no ar e a ultima linha do `World.AoGolpe`.
	/// Por isso o evento nasce AQUI, e nao na chegada do pacote do golpe -- o que se cobra e o quadro em que o
	/// relato e DESENHADO (a faisca, o clarao, os sons, a musica), seja ele qual for.
	///
	/// TRES LUTAS, e cada uma responde uma pergunta:
	///   1. a faixa foi pedida a thread de carga na ENTRADA NO MUNDO -- regua;
	///   2. a faixa foi pedida quando a da luta 1 entrou no ar (o saco de sorteio andou) -- regua;
	///   3. com o defeito ligado desde antes da luta 2, ninguem pediu a desta, e ela e lida na hora -- a mesma
	///      regua tem que REPROVAR.
	/// </summary>
	private void AnotarAFaixa(string faixa, double espera, double tocador)
	{
		_faixasTocadas.Add(faixa);
		string qual = $"`{faixa.GetFile()}`, {_bytes.GetValueOrDefault(faixa) / 1e6:0.0} MB";

		// NO GRAVADOR SOZINHO, e no MEIO de uma luta (a faixa acabou e puxou outra), so o rotulo.
		if (_passivo > 0 || _lutaAberta)
		{
			Marcar($"MUSICA de combate entra ({qual}): a thread principal esperou {espera:0.0} ms por ela");
			return;
		}

		_lutaAberta = true;
		_lutas++;
		string oque = $"o relato do PRIMEIRO GOLPE da luta {_lutas} (`World.AoGolpe`: faisca, clarao, sons, e a musica de combate entra -- {qual}";
		// (nas rodadas de injecao do ensaio o primeiro golpe paga outras contas -- a faisca sem ensaio, as amostras
		// que ninguem segura -- e sai como numero)
		Papel papel = _semEnsaio == null ? Papel.Regua : Papel.Nota;
		switch (_lutas)
		{
			case 1: Evento(oque + ", pedida a thread de carga na entrada no mundo)", papel); break;
			case 2: Evento(oque + ", pedida quando a da luta 1 entrou no ar)", papel); break;
			case 3: Evento(oque + ", que ninguem pediu)", Papel.Injetado, "a proxima faixa de combate sem pedir a thread de carga"); break;
			default: Evento(oque + ")", Papel.Nota); break;
		}
		_entradas[_eventos.Count - 1] = (espera, tocador);
	}

	/// <summary>
	/// A LUTA EM CURSO ACABOU? Na primeira chamada adianta a queda da tag de combate deste cliente
	/// (`World.AdiantarAQuedaDaTagDeTeste`: so o relogio anda, quem derruba e o `_Process` do `World`); devolve
	/// verdadeiro quando ela caiu. A tag dura 90 s, e a corrida precisa de tres lutas.
	/// </summary>
	private bool LutaEncerrada(World mundo)
	{
		if (mundo.NaLuta)
		{
			if (!_pediAQueda) { _pediAQueda = true; mundo.AdiantarAQuedaDaTagDeTeste(); }
			return false;
		}
		_pediAQueda = false;
		_lutaAberta = false;
		return true;
	}

	// =====================================================================
	// OS TEMAS QUE ENTRAM FORA DA LUTA (`--temas`) -- o ESC e a estreia de uma forma
	// =====================================================================
	private const string PastaDeMenu = "res://Assets/Sounds/Music/Menu ost";

	/// <summary>
	/// As tres formas que a rodada estreia, na ordem: as duas da regua e a do defeito injetado. Escolhidas pelo
	/// tamanho do tema, que e o que a leitura do mp3 acompanha: 9,8 e 4,2 MB as da regua, 10,0 a do defeito.
	/// </summary>
	private static readonly string[] FormasDosTemas = ["ssj1", "ssj2", "blue"];

	private int _tema, _faixasDeMenu;

	/// <summary>Quanto custou abrir e fechar o painel de pause pela primeira vez, no lobby, em ms. Ver <see cref="PrepararOsTemas"/>.</summary>
	private double _painelNoLobby = -1;

	/// <summary>As faixas de menu que este processo ja tem na memoria, ou ja pediu a thread de carga: nao servem pra medir leitura.</summary>
	private readonly HashSet<string> _menuNaMemoria = [];

	/// <summary>Verdadeiro so durante o gesto da bancada que poe um tema no ar: e quando a espia anota a faixa pedida.</summary>
	private bool _pedindoOTema;

	/// <summary>O tema que o gesto corrente pediu, ate ele ENTRAR no ar -- e o evento, o quadro e o instante do pedido.</summary>
	private string? _temaPedido;
	private int _eventoDoTema = -1, _quadroDoPedido;
	private ulong _comecoDoPedido;
	private Papel _papelDoTema;

	/// <summary>Os temas que a rodada pediu, na ordem: o evento do pedido de cada um, e o nome curto dele.</summary>
	private readonly List<(int Evento, string Rotulo)> _temasDaRodada = [];

	/// <summary>
	/// Quanto cada tema demorou a entrar no ar depois de pedido, pelo indice do evento do pedido: em quadros e em
	/// ms. So tem linha aqui o tema que ENTROU.
	/// </summary>
	private readonly Dictionary<int, (int Quadros, double Ms)> _atrasos = [];

	/// <summary>O caminho do tema que a ESTREIA desta forma toca, ou nulo se ela nao tem.</summary>
	private static string? TemaDe(string forma) =>
		Jandirus.Core.Forms.Catalogo.Def(forma) is { } def
		&& Jandirus.Core.Forms.Cinematicas.Para(def) is { Musica.Length: > 0 } cena
			? $"res://Assets/Sounds/Music/{cena.Musica}" : null;

	/// <summary>
	/// NO LOBBY: pesa as faixas que a rodada vai por no ar, arruma o saco do MENU -- a proxima passa a ser uma
	/// que este processo ainda nao leu (ver <see cref="PorFaixaDeMenuNaFila"/>) -- e abre o painel de pause uma
	/// vez, pra o primeiro uso dele nao cair no quadro do ESC 1.
	///
	/// DUAS FAIXAS DE MENU JA ESTAO FORA DA CONTA AQUI: a da tela de login, que esta tocando, e a que o saco
	/// entregaria em seguida, que a tela de login pode ter deixado pedida a thread de carga.
	/// </summary>
	private void PrepararOsTemas()
	{
		_faixasDeMenu = PesarAPasta(PastaDeMenu);
		foreach (string id in FormasDosTemas) if (TemaDe(id) is { } tema) PesarAFaixa(tema);

		if (AudioDirector.Instance is { FaixaDeTeste.Length: > 0 } audio) _menuNaMemoria.Add(audio.FaixaDeTeste);
		if (Trilha.ProximaDeMenu() is { Length: > 0 } seguinte) _menuNaMemoria.Add(seguinte);
		PorFaixaDeMenuNaFila();

		// O PRIMEIRO USO DO PAINEL DE PAUSE E PAGO AQUI, NO LOBBY, onde ele abre sem musica (ver `PauseMenu.Fechar`).
		// A primeira abertura do processo custa 5 a 18 ms a mais que as seguintes -- medido em 2026-10-08, com a
		// faixa ja na memoria nas duas: 7,8 a 21,6 ms de trabalho no ESC 1 contra 2,2 a 3,7 no ESC 2 -- e esse custo
		// e do painel, nao da trilha. Deixado no ESC 1 ele encostava a perna do quadro da regua no teto, e uma
		// corrida em quatro reprovava por ele. Pago aqui (4,6 a 6,7 ms pra abrir e fechar, que saem no relatorio),
		// o ESC 1 custa 4,0 a 9,0 ms de trabalho.
		if (PauseMenu.Instancia is { } pause)
		{
			ulong t0 = Time.GetTicksUsec();
			pause.Abrir();
			pause.Fechar("bancada: o primeiro uso do painel, no lobby");
			_painelNoLobby = (Time.GetTicksUsec() - t0) / 1000.0;
		}

		// A RODADA DE INJECAO: o defeito entra aqui, no lobby, antes de a entrada no mundo pedir faixa nenhuma.
		// Sai no `finally` da conferencia e, se a janela for fechada na mao, no `_ExitTree`.
		if (_semThreadDeCarga) AudioDirector.SemThreadDeCargaDeTeste = true;
	}

	/// <summary>
	/// A PROXIMA FAIXA DE MENU DO SACO PASSA A SER UMA QUE ESTE PROCESSO AINDA NAO LEU -- grande, se ainda houver
	/// (o custo de ler o mp3 acompanha o tamanho); as que vem antes dela saem do saco sem tocar. E o
	/// <see cref="PorFaixaGrandeNaFila"/> do saco de menu, e com o mesmo cuidado: so se chama quando quem pede a
	/// proxima ainda vem (no lobby: e a entrada no mundo) ou com o defeito ligado. Fora disso a faixa ja pedida
	/// seria descartada aqui, e o ESC seguinte abriria com outra, lida na hora -- o defeito, fabricado pela bancada.
	/// </summary>
	private void PorFaixaDeMenuNaFila()
	{
		// DUAS PASSADAS: uma grande que nao esteja na memoria; se nao houver mais, qualquer uma que nao esteja.
		// (duas voltas do saco alcancam qualquer faixa dele; a folga e pelo resto da rodada em curso)
		foreach (long piso in new[] { FaixaGrande, 0L })
			for (int i = 0; i < _faixasDeMenu * 2 + 4; i++)
			{
				string proxima = Trilha.ProximaDeMenu();
				if (proxima.Length == 0) return;
				if (_bytes.GetValueOrDefault(proxima) >= piso && !_menuNaMemoria.Contains(proxima)) return;
				Trilha.Menu();
			}
	}

	/// <summary>
	/// UM GESTO DA BANCADA POE UM TEMA NO AR -- o ESC abrindo, uma forma estreando -- e o quadro do gesto vira
	/// evento.
	///
	/// SAO DOIS INSTANTES, e podem cair em quadros diferentes: o do PEDIDO (o gesto: a maquina de musica decide a
	/// faixa, e a espia a entrega aqui) e o da ENTRADA (o tocador liga -- ver <see cref="AnotarAEntradaDoTema"/>).
	/// O cronometro do `AudioDirector.Cruzar` so fecha na entrada, e e ele que diz quanto a thread principal
	/// esperou pelo arquivo.
	/// </summary>
	private void PorOTemaNoAr(string rotulo, string oque, Papel papel, string defeito, Action gesto)
	{
		_temaPedido = null;
		_pedindoOTema = true;
		try { gesto(); }
		finally { _pedindoOTema = false; }

		if (_temaPedido is not { } faixa)
		{
			Conferir(false, $"{rotulo}: o gesto pediu uma faixa a maquina de musica");
			Fechar();
			return;
		}

		Evento($"{oque} -- `{faixa.GetFile()}`, {_bytes.GetValueOrDefault(faixa) / 1e6:0.0} MB)", papel, defeito);
		_eventoDoTema = _eventos.Count - 1;
		_quadroDoPedido = _quadros.Count;
		_comecoDoPedido = _comecoDoQuadro;
		_papelDoTema = papel;
		_temasDaRodada.Add((_eventoDoTema, $"{rotulo}, `{faixa.GetFile()}` ({_bytes.GetValueOrDefault(faixa) / 1e6:0.0} MB)"));
	}

	/// <summary>
	/// O TEMA PEDIDO ENTROU NO AR NESTE QUADRO, e o cronometro do `AudioDirector.Cruzar` diz quanto a thread
	/// principal esperou pelo arquivo dele -- a segunda perna da regua, que vai pro evento do PEDIDO.
	///
	/// SE ELE ENTROU DEPOIS DO PEDIDO, o quadro em que o tocador liga e outro quadro, e tambem e cobrado.
	/// </summary>
	private void AnotarAEntradaDoTema(string faixa, double espera, double tocador)
	{
		_temaPedido = null;
		_menuNaMemoria.Add(faixa);

		int quadros = _quadros.Count - _quadroDoPedido;
		_entradas[_eventoDoTema] = (espera, tocador);
		_atrasos[_eventoDoTema] = (quadros, (_comecoDoQuadro - _comecoDoPedido) / 1000.0);
		if (quadros == 0) return;

		// (na rodada de injecao dos raios da forma este quadro pode ser o dos primeiros raios de corpo, que la monta
		// uma pipeline: sai como numero)
		Evento($"o tema `{faixa.GetFile()}` ENTRA no ar, {quadros} quadro(s) depois de pedido (o tocador liga)",
			   _papelDoTema == Papel.Injetado || OTemaCaiNoQuadroDosRaios() ? Papel.Nota : Papel.Regua);
		_entradas[_eventos.Count - 1] = (espera, tocador);
	}

	private void AbrirOEsc(PauseMenu pause, int n, string comoVeio, Papel papel, string defeito = "") =>
		PorOTemaNoAr($"o ESC {n}", $"o ESC {n} ABRE (`PauseMenu.Abrir`: o painel, e o tema de menu entra, {comoVeio}",
					 papel, defeito, pause.Abrir);

	/// <summary>
	/// ESTREIA UMA FORMA pelo caminho por onde o pacote do servidor entra (`World.AoMudarForma` com
	/// `DegrauDeCena.Estreia`) -- a mesma escolha do `RoboDeCena` e do `RoboDeTrilha`: uma cena montada na mao
	/// provaria que o tocador toca, e nao que o JOGO a toca. Passa pela BASE antes, pra segunda cena nao chegar
	/// com `de == para`.
	/// </summary>
	private void Estrear(World mundo, GameClient cli, string id, Papel papel, string defeito = "")
	{
		if (Jandirus.Core.Forms.Catalogo.Def(id) is not { } def || TemaDe(id) == null)
		{
			Conferir(false, $"a forma `{id}` tem cena de estreia com musica");
			Fechar();
			return;
		}

		ushort b = Jandirus.Core.Forms.Catalogo.Rede(Jandirus.Core.Forms.Catalogo.IdBase);
		PorOTemaNoAr($"a estreia de `{id}`",
					 $"a ESTREIA de `{id}` NASCE (`World.AoMudarForma` -> `Transformacao._Ready`: a cena, e o tema dela",
					 papel, defeito, () =>
					 {
						 mundo.AoMudarForma(cli.LocalId, b, b, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
						 mundo.AoMudarForma(cli.LocalId, b, def.IdRede, Jandirus.Core.Forms.DegrauDeCena.Estreia);
					 });
		// (a primeira estreia que soltar raio de corpo e um dos dois quadros da regua dos raios da forma)
		SeguirOsRaiosDaEstreia(mundo, cli, id);
	}

	/// <summary>
	/// A PRIMEIRA CINEMATICA DO PROCESSO, SEM MUSICA: a cena ENCURTADA da primeira forma (`DegrauDeCena.Curta`),
	/// que monta os mesmos efeitos da estreia e nao tem tema. REGUA: o quadro em que ela nasce custa um quadro
	/// normal.
	///
	/// E ELA RODA ATE O FIM (11,6 s), com a rodada esperando: dez segundos adiante esta o beat que ASSUME, e o quadro
	/// dele tem regua propria -- ver <see cref="AcompanharAPrimeiraCena"/>.
	///
	/// ELA VEM ANTES DAS ESTREIAS e num quadro so dela porque o primeiro uso da cena tem um custo que nao e da
	/// trilha: deixado no quadro da primeira ESTREIA, ele reprovaria a perna do quadro da regua da musica por uma
	/// conta que nao e a dela.
	///
	/// SO O PACOTE DA TRANSFORMACAO, COMO O SERVIDOR O MANDA. Este gesto mandava dois no mesmo quadro -- a volta a
	/// base e a transformacao --, e a volta a base punha no quadro cobrado codigo que no jogo nao esta nele: a
	/// primeira transformacao de um personagem e um pacote so, e o corpo ja esta na base. Medido em 2026-10-08: sem
	/// ela o script deste quadro caiu de 20 pra 18 ms (sozinha e antes de tudo ela custa 5 a 7 ms na primeira vez,
	/// mas o grosso desse codigo e do pacote da transformacao tambem, e ficou no quadro, que e onde o jogo o paga).
	/// As estreias seguintes continuam passando pela base no mesmo gesto, pra nao chegarem com `de == para`; quem
	/// roda a volta a base pela primeira vez e o <see cref="CortarACena"/> desta cena.
	/// </summary>
	private void AquecerACena(World mundo, GameClient cli)
	{
		string id = FormasDosTemas[0];
		if (Jandirus.Core.Forms.Catalogo.Def(id) is not { } def)
		{
			Conferir(false, $"a forma `{id}` existe no catalogo");
			Fechar();
			return;
		}

		ushort b = Jandirus.Core.Forms.Catalogo.Rede(Jandirus.Core.Forms.Catalogo.IdBase);
		_esperaDeFolhasNoComeco = FolhasPresas.EsperaDeTeste;
		mundo.AoMudarForma(cli.LocalId, b, def.IdRede, Jandirus.Core.Forms.DegrauDeCena.Curta);

		// (nas rodadas de injecao do ensaio dos efeitos de ki -- `--semensaio`, `--semaquecimento` -- o processo
		// inteiro esta frio por outra conta, e este quadro sai como numero)
		Papel papel = _semEnsaioDaCena ? Papel.Injetado : _semEnsaio == null ? Papel.Regua : Papel.Nota;
		Evento($"a PRIMEIRA CINEMATICA do processo nasce (a encurtada de `{id}`, que nao tem musica: `World.AoMudarForma` -> `Transformacao._Ready`)",
			   papel, _semEnsaioDaCena ? "o aquecimento sem as folhas e sem o ensaio da cinematica" : "");

		// ELA RODA ATE O FIM, e a rodada espera por ela: o beat que ASSUME esta dez segundos adiante.
		_primeiraCena = CenaDoCorpoLocal(mundo, cli);
		_beatsDaPrimeiraCena = 0;
		if (_primeiraCena == null) { Conferir(false, "a primeira cinematica nasceu (`World.AoMudarForma` -> `Transformacao.Rodar`)"); Fechar(); }
	}

	/// <summary>A primeira cinematica da rodada dos temas, enquanto ela roda, e quantos beats dela ja foram vistos.</summary>
	private Transformacao? _primeiraCena;
	private int _beatsDaPrimeiraCena;

	/// <summary>O que a porta das folhas ja tinha esperado quando a primeira cinematica ia nascer. Ver <see cref="ConferirAEsperaDaCena"/>.</summary>
	private double _esperaDeFolhasNoComeco;

	/// <summary>
	/// Quanto a thread principal pode esperar por folhas ao longo da primeira cinematica INTEIRA, em ms. Com as folhas
	/// ja na memoria a porta gasta centesimos de ms a cada uma; lida do disco na hora, uma folha so custa de 3 a 10.
	/// </summary>
	private const double EsperaPorFolhas = 1.0;

	private const string DefeitoDeLerNaHora = "a cinematica lendo do disco, na hora, o que usa depois de nascer";

	/// <summary>
	/// O BEAT QUE ASSUME DA PRIMEIRA CINEMATICA DO PROCESSO -- o instante mais visto da cena: a forma fica, o chao
	/// abre, a tela estoura em clarao. REGUA: o quadro em que ele dispara custa um quadro normal. (Antes do conserto,
	/// 28,0 a 36,3 ms de trabalho contra um teto de 15; depois, 4,4 a 5,8. A conta inteira esta no cabecalho da
	/// classe, no bloco "E O BEAT QUE ASSUME".)
	///
	/// Chamado no fim de cada quadro enquanto a cena roda: ela roda DEPOIS deste robo, entao o beat que disparou
	/// neste quadro so se ve daqui. Os outros beats so dao rotulo ao quadro deles.
	/// </summary>
	private void AcompanharAPrimeiraCena(Transformacao cena)
	{
		if (!IsInstanceValid(cena) || cena.IsQueuedForDeletion() || !cena.Rodando)
		{
			Marcar("a primeira cinematica ACABOU");
			_primeiraCena = null;
			ConferirAEsperaDaCena();
			return;
		}

		int n = cena.BeatsDeTeste;
		if (n == _beatsDaPrimeiraCena) return;

		Jandirus.Core.Forms.Beat[] beats = cena.CenaDeTeste.Beats;
		for (int i = _beatsDaPrimeiraCena; i < n && i < beats.Length; i++)
		{
			string rotulo = $"beat {i} aos {beats[i].Em:0.00} s [{beats[i].Faz}{(beats[i].Som.Length > 0 ? ", som `" + beats[i].Som + "`" : "")}]";
			// (o primeiro beat de anel de choque e o primeiro de feixes do processo sao quadros da regua do ensaio: ver
			// o arquivo `RoboDoPrimeiroEstouro.Raios.cs`)
			AnotarOBeatDaRegua(beats[i].Faz, rotulo);
			if (!beats[i].Faz.HasFlag(Jandirus.Core.Forms.Efeito.Assumir)) { Marcar("a primeira cinematica: " + rotulo); continue; }

			// (nas rodadas de injecao do ensaio -- `--semensaio`, `--semaquecimento`, `--semensaiodacena` -- o processo
			// esta frio por outra conta, e este quadro sai como numero)
			Papel papel = _semAdiantarACena ? Papel.Injetado
				: _semEnsaio == null && !_semEnsaioDaCena ? Papel.Regua : Papel.Nota;
			Evento($"o beat que ASSUME da primeira cinematica do processo ({rotulo}: a forma fica, o chao abre, a tela estoura em clarao)",
				   papel, _semAdiantarACena ? DefeitoDeLerNaHora : "");
		}
		_beatsDaPrimeiraCena = n;
	}

	/// <summary>
	/// A SEGUNDA PERNA DA REGUA DA PRIMEIRA CINEMATICA: do nascimento ao fim dela, a thread principal NAO ESPEROU POR
	/// FOLHA nenhuma -- a da cratera que o beat que assume planta e a do penteado da forma, que o cabelo veste pela
	/// primeira vez quando pisca. O cronometro e o da propria porta das folhas (`FolhasPresas.EsperaDeTeste`).
	///
	/// EXISTE PORQUE O TETO DE QUADRO NAO SEPARA: ler uma folha do disco custa de 3 a 10 ms, e o quadro em que o cabelo
	/// pisca pela primeira vez, com a leitura dentro, fica em 8 a 12 -- abaixo do teto.
	/// </summary>
	private void ConferirAEsperaDaCena()
	{
		double espera = FolhasPresas.EsperaDeTeste - _esperaDeFolhasNoComeco;
		const string oque = "do nascimento ao fim da primeira cinematica, a thread principal nao esperou por folha nenhuma (a da cratera, a do penteado da forma)";
		string medida = $"esperou {espera:0.0} ms contra {EsperaPorFolhas:0.0}";
		if (_semAdiantarACena) Conferir(espera > EsperaPorFolhas, $"(defeito injetado: {DefeitoDeLerNaHora}) a mesma regua REPROVA: {oque} ({medida})");
		else if (_semEnsaio != null || _semEnsaioDaCena) Nota($"{oque}: {medida} (nesta rodada o aquecimento nao e o do jogo)");
		else Conferir(espera <= EsperaPorFolhas, $"{oque} ({medida})");
	}

	/// <summary>
	/// QUEM SEGURA O QUE A CENA USA? O ultimo gesto da rodada: com a primeira cinematica acabada ha mais de dez
	/// segundos -- a fumaca dela sumiu, os sons dela pararam --, uma coleta forcada do .NET, e a pergunta feita ao
	/// proprio Godot (`ResourceLoader.HasCached`): a fumaca, as duas crateras e os sons que os beats tocam ESTAO NA
	/// MEMORIA?
	///
	/// E A PERNA QUE O TETO DE QUADRO NAO DA. Recurso que ninguem segura sai do cache quando o ultimo node que o
	/// usava morre e o coletor leva o involucro C# dele -- e volta do disco, pela thread principal, na vez seguinte.
	/// Uma releitura dessas custa poucos milissegundos, abaixo da folga do teto; aqui ela e um fato, e nao um tempo.
	/// </summary>
	private void ConferirOsDonos()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		Marcar("coleta forcada do .NET (a conferencia dos donos)");

		var fora = new List<string>();
		int quantos = 0;
		foreach (string caminho in ArquivosQueACenaNaoPodeReler())
		{
			quantos++;
			if (!ResourceLoader.HasCached(caminho)) fora.Add(caminho.GetFile());
		}

		string oque = $"com a fumaca sumida, os sons parados e o coletor do .NET passado, os {quantos} arquivos que uma cena usa depois de nascer estao na memoria (a fumaca, as duas crateras, os estalos da faisca e o som de cada beat)";
		string achado = fora.Count == 0 ? $"os {quantos} la" : $"{fora.Count} de {quantos} fora: {string.Join(", ", fora)}";
		if (_semAdiantarACena) Conferir(fora.Count > 0, $"(defeito injetado: {DefeitoDeLerNaHora}) a mesma conferencia REPROVA: {oque} ({achado})");
		else if (_semEnsaio != null) Nota($"{oque}: {achado} (nesta rodada o aquecimento nao e o do jogo)");
		else Conferir(fora.Count == 0, $"{oque} ({achado})");
	}

	/// <summary>
	/// A CENA E CORTADA, o tema dela sai do ar e o corpo VOLTA A BASE: o que a rodada cobra e o quadro em que a
	/// cena NASCE e o quadro em que o tema ENTRA, e a do `ssj1` tem 26 s. Quem solta o corpo e a pose e o
	/// `_ExitTree` da propria cena; o pedido que ela deixou na camada de transformacao so sai com o `Silenciar`
	/// (cena cortada nao chega ao fim da faixa).
	///
	/// A VOLTA A BASE E DAQUI: a cena cortada nunca assumiu a forma, mas o `World` ja registrou o pacote dela, e
	/// este e o pacote que desfaz o registro. E E AQUI QUE ELA RODA PELA PRIMEIRA VEZ NO PROCESSO, num quadro que
	/// ninguem cobra -- ver <see cref="AquecerACena"/>.
	/// </summary>
	private void CortarACena(AudioDirector audio)
	{
		if (GetTree().Root.FindChild("LocalPlayer", true, false) is Node2D corpo)
			foreach (Node n in corpo.GetParent().GetChildren())
				if (n is Transformacao cena) cena.QueueFree();
		audio.Silenciar("bancada: a cena foi cortada");

		if (World.Instancia is { } mundo && C is { } cli)
		{
			ushort b = Jandirus.Core.Forms.Catalogo.Rede(Jandirus.Core.Forms.Catalogo.IdBase);
			mundo.AoMudarForma(cli.LocalId, b, b, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		}
	}

	/// <summary>
	/// SEIS TEMAS, UM POR VEZ, com um segundo e meio entre um gesto e o seguinte -- o custo de cada um cai num
	/// quadro so dele. Tres vezes o ESC, tres estreias:
	///
	///   * o ESC 1 abre com a faixa que estava na frente do saco de menu na ENTRADA NO MUNDO, pedida ali a
	///     thread de carga -- regua;
	///   * a estreia do `ssj1` e a do `ssj2`: o tema de uma cinematica so se conhece no quadro em que ela nasce,
	///     e entra no ar quando a thread de carga o entrega -- regua, no quadro do pedido E no da entrada;
	///   * o ESC 2 abre com a faixa que passou a ser a proxima quando o ESC 1 abriu -- regua;
	///   * com o defeito ligado desde antes do ESC 2 (`AudioDirector.SemThreadDeCargaDeTeste`), o ESC 3 abre com
	///     uma faixa que este processo nunca leu, e o `blue` estreia: a mesma regua tem que REPROVAR os dois.
	///
	/// ANTES DAS ESTREIAS VEM UMA CENA SEM MUSICA (<see cref="AquecerACena"/>): o primeiro uso da cinematica tinha
	/// um custo proprio, que nao e da trilha, e a regua dele e outra -- o quadro em que essa cena nasce custa um
	/// quadro normal. Ela roda inteira, ate o beat que assume (<see cref="AcompanharAPrimeiraCena"/>), e o ultimo
	/// gesto da rodada confere quem segura o que ela usou (<see cref="ConferirOsDonos"/>).
	///
	/// A REGUA E A DA MUSICA DE COMBATE, com as duas pernas: o quadro do gesto custa um quadro normal, E a thread
	/// principal nao esperou pelo arquivo (<see cref="EsperaDaFaixa"/>). A segunda e a que pega o defeito sempre: o
	/// ESC que le 2,5 a 4,2 MB do cache do Windows gasta 7 a 9 ms de trabalho, abaixo do teto do quadro.
	///
	/// NA RODADA `--semthreaddecarga` o defeito esta ligado desde o lobby, e os quatro temas da regua viram quatro
	/// contra-exemplos: o jogo de antes, no mesmo binario, faixa a faixa.
	/// </summary>
	private void AndarNosTemas(World mundo, GameClient cli)
	{
		// (a rodada de MEDICAO das cenas, `--cenas`, e outra maquina com o mesmo preparo desta: ver `AndarNasCenas`)
		if (RodadaDasCenas) { AndarNasCenas(mundo, cli); return; }
		// (e a rodada do CEU, `--ceu`: uma tempestade que comeca no meio do jogo -- ver `AndarNoCeu`)
		if (_rodadaDoCeu) { AndarNoCeu(mundo, cli); return; }

		// O TEMA DO GESTO ANTERIOR AINDA NAO ENTROU NO AR: a rodada espera por ele.
		if (_temaPedido != null)
		{
			if (_t > 5) { Conferir(false, $"o tema `{_temaPedido.GetFile()}` entrou no ar em 5 s"); Fechar(); }
			return;
		}
		// A PRIMEIRA CINEMATICA RODA INTEIRA: a regua do beat que ASSUME precisa chegar nele, e a encurtada do `ssj1`
		// tem 11,6 s. O gesto seguinte sai um segundo e meio depois de ela acabar.
		if (_primeiraCena != null) { _t = 0; return; }
		// A ESTREIA COM TEMPESTADE SEGURA A RODADA ATE O PRIMEIRO RELAMPAGO DELA (a do `ssj1`: ele cai entre 1,0 e 2,0 s de
		// cena, e cortada com um segundo e meio a cena o perdia em duas corridas de tres): ver `EsperandoORelampago`.
		if (EsperandoORelampago()) return;
		if (_t < 1.5) return;
		_t = 0;

		if (PauseMenu.Instancia is not { } pause || AudioDirector.Instance is not { } audio)
		{
			Conferir(false, "ha menu de pause e maquina de musica pra rodada dos temas");
			Fechar();
			return;
		}

		const string defeito = "a musica sem a thread de carga";
		// NA RODADA DE INJECAO (`--semthreaddecarga`) o defeito esta ligado desde o lobby, e os quatro temas da
		// regua viram quatro contra-exemplos.
		Papel regua = _semThreadDeCarga ? Papel.Injetado : Papel.Regua;
		string daRegua = _semThreadDeCarga ? defeito + ", a rodada inteira" : "";

		switch (_tema++)
		{
			case 0: AbrirOEsc(pause, 1, "a que estava na frente do saco na entrada no mundo", regua, daRegua); break;
			case 1: pause.Fechar("bancada: o ESC fecha"); break;
			case 2: AquecerACena(mundo, cli); break;
			case 3: CortarACena(audio); break;
			case 4: Estrear(mundo, cli, FormasDosTemas[0], regua, daRegua); break;
			case 5: CortarACena(audio); break;
			case 6: Estrear(mundo, cli, FormasDosTemas[1], regua, daRegua); break;
			case 7: CortarACena(audio); break;
			case 8:
				// O DEFEITO ENTRA AQUI, antes de o ESC 2 abrir. A faixa DELE ja esta na memoria (foi pedida quando o
				// ESC 1 abriu); o que o defeito tira e o pedido da SEGUINTE.
				AudioDirector.SemThreadDeCargaDeTeste = true;
				AbrirOEsc(pause, 2, "a que passou a ser a proxima quando o ESC 1 abriu", regua, daRegua);
				break;
			case 9:
				pause.Fechar("bancada: o ESC fecha");
				PorFaixaDeMenuNaFila();   // o ESC 3 vai abrir com uma faixa que este processo nunca leu
				break;
			case 10: AbrirOEsc(pause, 3, "uma que ninguem pediu", Papel.Injetado, defeito); break;
			case 11: pause.Fechar("bancada: o ESC fecha"); break;
			case 12: Estrear(mundo, cli, FormasDosTemas[2], Papel.Injetado, defeito); break;
			case 13:
				CortarACena(audio);
				AudioDirector.SemThreadDeCargaDeTeste = false;
				break;
			// O ULTIMO GESTO, e por ultimo de proposito: ele forca uma coleta do .NET, e o que se medisse depois dela
			// ja nao seria o jogo de antes dela.
			case 14: ConferirOsDonos(); break;
			default: Fechar(); break;
		}
	}

	/// <summary>
	/// OS DEFEITOS INJETADOS NAO SOBREVIVEM A BANCADA: sao campos ESTATICOS de producao, e se este node sair da
	/// arvore no meio da corrida (janela fechada na mao, excecao num passo) o jogo seguinte neste processo
	/// rodaria com eles.
	/// </summary>
	public override void _ExitTree()
	{
		SoltarOsDefeitosDosPrimeirosDesenhos();
		SoltarOsAvulsos();
		SoltarAMiragem();
		Aquecimento.SemEnsaioDeTeste = false;
		Aquecimento.SemEnsaioDaCenaDeTeste = false;
		Aquecimento.SemEnsaioDoBorraoDeTeste = false;
		SoltarOBorrao();
		Aquecimento.SemAdiantarACenaDeTeste = false;
		Aquecimento.SemEnsaioDosRaiosDeTeste = false;
		Aquecimento.EstrelaComLuzDeTeste = false;
		PoeiraDeEstrago.SemSegurarDeTeste = false;
		AudioDirector.SemThreadDeCargaDeTeste = false;
		SonsPresos.SemSegurarDeTeste = false;
		Aquecimento.SemSegurarOsScriptsDeTeste = false;
		SoltarOsSons();
		AudioDirector.EspiaoDeMusica -= AoTrocarAFaixa;
		Transformacao.EspiaoDePedaco -= AoMedirPedaco;
		SoltarAEscutaDeCarga();
		if (C is { } cli)
		{
			cli.TiroNasceu -= AoNascerTiro;
			cli.TiroMorreu -= AoMorrerTiro;
			cli.Golpe -= AoGolpe;
			cli.FeridasMudaram -= AoMudarFeridas;
		}
	}

	/// <summary>
	/// QUANTOS SHADERS DE PARTICULA O CACHE EM DISCO JA TEM (`user://shader_cache`). Zero e o processo que
	/// vai compilar tudo do zero -- o de toda bancada, que roda com o APPDATA desviado pra uma pasta nova.
	/// So pra o relatorio dizer em que caso a corrida caiu: os numeros de antes mudam 8x de um pro outro.
	/// </summary>
	private static int ShadersDeParticulaEmDisco()
	{
		const string pasta = "user://shader_cache/ParticlesShaderRD";
		int n = 0;
		if (DirAccess.Open(pasta) is not { } raiz) return 0;
		foreach (string sub in raiz.GetDirectories())
			if (DirAccess.Open(pasta + "/" + sub) is { } d) n += d.GetFiles().Length;
		return n;
	}

	private void AoNascerTiro(NascimentoDeProjetil n)
	{
		if (_passivo > 0) { Marcar($"tiro {n.Id} NASCEU ({(Jandirus.Core.Combat.TipoDeProjetil)n.Tipo}, {(Jandirus.Core.Combat.ArteDeKi)n.Arte})"); return; }
		if (n.Id != _tiro) return;
		if (_pecas) Evento("o primeiro RAIO nasce (`World.AoNascerTiro`, shader `FeixeDeKi`)", PrimeiroUso(), nascimento: true);
		else Evento(_tiros == 1 ? "a PRIMEIRA BOLA nasce (`World.AoNascerTiro`, shader `EsferaDeKi`)" : $"a bola {_tiros} nasce",
						_tiros == 1 ? PapelDaPrimeiraBola() : _tiros <= 3 ? Papel.Regua : Papel.Nota, nascimento: true);
	}

	private void AoMorrerTiro(int id, byte fim, Vec2 onde)
	{
		if (_passivo > 0) Marcar($"tiro {id} MORREU ({(Jandirus.Core.Combat.FimDeProjetil)fim})");
		else if (id == _tiro) Marcar($"pacote: o tiro MORREU ({(Jandirus.Core.Combat.FimDeProjetil)fim})");
	}

	/// <summary>
	/// O PACOTE DO RELATO DO GOLPE chega um punhado de quadros antes do estouro (a bola ainda voa o ultimo trecho
	/// na tela). SAI COMO NUMERO: e o custo de um golpe qualquer, sem nada novo.
	///
	/// O GOLPE QUE ABRE UMA LUTA NAO E COBRADO AQUI, e sim no quadro em que a musica de combate entra -- o quadro
	/// em que o `World` DESENHA o relato (ver <see cref="AnotarAFaixa"/>). Hoje e este mesmo; no dia em que o
	/// relato do golpe de bola esperar a bola chegar na tela, passa a ser o do estouro, e uma regua pendurada no
	/// pacote ficaria medindo um quadro vazio.
	/// </summary>
	private void AoGolpe(Protocol.HitEvent h)
	{
		if (_passivo > 0)
		{
			Marcar($"GOLPE em {h.Alvo} ({(Jandirus.Core.Combat.Desfecho)h.Desfecho}"
				   + (h.Decepou ? ", DECEPOU" : "") + (h.Nocauteou ? ", NOCAUTE" : "") + (h.Morreu ? ", MORREU" : "") + ")");
			return;
		}
		if (h.Alvo != _alvo) return;
		_golpes++;
		if (World.Instancia is { NaLuta: false }) Marcar($"pacote: o relato do golpe da bola {_tiros} (abre uma luta)");
		else Evento($"o relato do golpe da bola {_tiros} chega", Papel.Nota);
	}

	private void AoMudarFeridas(int id) { if (_passivo > 0 || id == _alvo) Marcar($"pacote: as FERIDAS de {id} mudaram"); }

	// =====================================================================
	// OS DOIS GANCHOS
	// =====================================================================
	private void NoComecoDoQuadro()
	{
		_comecoDoQuadro = Time.GetTicksUsec();
		_pausaNoComeco = GC.GetTotalPauseDuration().TotalMilliseconds;
	}

	private void NoFimDoQuadro()
	{
		if (_acabou) return;

		// O ESTOURO NASCEU NESTE QUADRO? Lido aqui, no fim, porque quem o solta e o `World`, que roda depois do robo.
		// SO COM BOLA NO AR: a pergunta varre os filhos da camada de atores, e feita em todo quadro o lixo dela
		// entraria na conta do coletor que a propria bancada imprime.
		if ((_passivo > 0 || (_estouros < _tiros && !_pecas)) && World.Instancia is { } mundo)
			foreach ((ulong no, Vector2 _) in mundo.EstourosDeKiDesenhados())
				if (_estourosVistos.Add(no))
				{
					if (_passivo > 0) Marcar($"ESTOURO {++_estouros} NASCEU");
					else AnotarOEstouro(++_estouros);
				}

		// UMA FAIXA DE COMBATE ENTROU NESTE QUADRO? Lido aqui, no fim: o espiao avisa ANTES de o `Cruzar` rodar, e e
		// o cronometro dele que diz quanto a thread principal esperou pelo arquivo.
		if (_faixaQueEntrou is { } faixa)
		{
			_faixaQueEntrou = null;
			if (AudioDirector.Instance is { } audio)
			{
				(string qual, double espera, double tocador) = audio.UltimaEntradaDeTeste;
				if (qual == faixa) AnotarAFaixa(faixa, espera, tocador);
			}
		}

		// O TEMA QUE A RODADA PEDIU ENTROU NO AR? Lido aqui, no fim de cada quadro, ate entrar: a faixa que ja esta
		// na memoria (e a que e lida na hora) entra no quadro do pedido; a que a maquina de musica espera da thread
		// de carga entra uns quadros depois.
		if (_temaPedido is { } tema && AudioDirector.Instance is { } diretor && diretor.UltimaEntradaDeTeste.Faixa == tema)
			AnotarAEntradaDoTema(tema, diretor.UltimaEntradaDeTeste.Espera, diretor.UltimaEntradaDeTeste.Tocador);

		// A PRIMEIRA CINEMATICA DA RODADA DOS TEMAS, beat a beat, e os pedacos do `_Process` de qualquer cena que a
		// espia juntou neste quadro. Lidos aqui, no fim: a cena roda depois deste robo.
		if (_primeiraCena is { } primeira) AcompanharAPrimeiraCena(primeira);
		// (os raios da forma: o sal do `--driverfrio`, e a estreia que solta os primeiros raios de corpo)
		NoFimDoQuadroDosRaios();
		// (o borrao do rastro: o desenho partido em preparo e tela, e o quadro em que as copias dele nascem)
		NoFimDoQuadroDoBorrao();
		// (os primeiros usos avulsos: o desenho partido, e os dois que so aparecem quadros depois do gesto)
		NoFimDoQuadroDosAvulsos();
		// (a miragem do Zanzoken, no gravador sozinho: o quadro em que cada uma nasce)
		NoFimDoQuadroDaMiragem();
		GuardarOsPedacosDoQuadro();

		if (_entrouNoMundo < 0 && World.Instancia?.PosicaoLocal != null)
		{
			_entrouNoMundo = _quadros.Count;
			Marcar("O CORPO ESTA NO MUNDO");
		}

		_quadros.Add(new Quadro
		{
			Comeco = _comecoDoQuadro,
			Fim = Time.GetTicksUsec(),
			Delta = GetProcessDeltaTime(),
			Canvas = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsCanvas),
			DesenhoCpu = RenderingServer.ViewportGetMeasuredRenderTimeCpu(_tela) + RenderingServer.GetFrameSetupTimeCpu(),
			PausaNoComeco = _pausaNoComeco,
			PausaNoFim = GC.GetTotalPauseDuration().TotalMilliseconds,
			Marcas = string.Join(" + ", _marcasDoQuadro),
		});
		_marcasDoQuadro.Clear();
	}

	/// <summary>O que o estouro de cada bola e pra bancada -- o roteiro esta no <see cref="EsperarOEstouro"/>.</summary>
	private void AnotarOEstouro(int n)
	{
		switch (n)
		{
			case 1: Evento("o PRIMEIRO ESTOURO do processo (`World.RecolherTiro`: estouro + anel + poeira)", PrimeiroUso(oEstouro: true)); break;
			case 2 or 3: Evento($"o estouro {n} (o controle: nada e novo)", Papel.Regua); break;
			case 4: Evento("o estouro 4, depois de a poeira sumir e de o coletor do .NET passar", Papel.Regua); break;
			// (so numero: e a bola da luta 3, a do defeito da musica -- com o relato do golpe esperando a bola, o
			// estouro dela divide o quadro com a faixa lida na hora)
			case 5: Evento("o estouro 5 (a bola que abre a luta 3)", Papel.Nota); break;
			case 6: Evento("o estouro 6 (o defeito acaba de entrar: a poeira dele ja nao segura os materiais)", Papel.Nota); break;
			default:
				Evento($"o estouro {n}, depois de a poeira sumir e de o coletor passar", Papel.Injetado,
					   "a poeira sem segurar os materiais de particula");
				break;
		}
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou) return;
		_vida += delta;
		// (o gravador sozinho nao tem paciencia: quem manda no tempo da cena e o outro robo)
		if (_passivo <= 0 && _vida > Paciencia) { Conferir(false, $"a corrida acabou dentro da paciencia ({Paciencia:0} s; parou no passo {_passo})"); Fechar(); return; }

		if (_passivo > 0)
		{
			if (_entrouNoMundo >= 0 && (_t += delta) >= _passivo) FecharOPassivo();
			return;
		}

		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv) { Conferir(false, "ha servidor no processo (`--diagestouro` precisa de `--host`)"); Fechar(); return; }

		_t += delta;
		switch (_passo)
		{
			case 0: Assentar(mundo, srv, cli); break;
			case 1: EsperarOAlvo(mundo); break;
			case 2: MedirABase(); break;
			case 3:
				if (_scripts) AndarNosScripts(mundo);
				else if (_sons) AndarNosSons(mundo);
				else if (_temas) AndarNosTemas(mundo, cli);
				else if (_pecas) AndarNasPecas(mundo, srv, cli);
				else if (_avulsos) AndarNosAvulsos(mundo, srv, cli);
				else Atirar(srv, cli);
				break;
			case 4: EsperarOEstouro(mundo); break;
			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; }

	private void Assentar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 3 || mundo.PosicaoLocal is null) return;

		// MEIO-DIA E CEU LIMPO, a nao ser que a rodada seja a da noite: a hora e o clima sao sorteados, e chuva
		// escurece o bastante pra acender as luzes de ki -- que mudam COMO o efeito e desenhado.
		if (!Tem("--estouronoite")) srv.CravarMeioDiaDaVariedade(cli.LocalId);

		// (a rodada dos temas nao atira em ninguem: nao ha alvo pra forjar -- nem a dos avulsos)
		if (_temas || _avulsos)
		{
			// O TEMA DO LUGAR, se este berco tem um (hoje so o Inferno, o de `--raca Demon`): ele entrou no ar na
			// chegada na zona, dentro da carga dela, antes de haver base pra cobrar o quadro -- sai como numero.
			if (AudioDirector.Instance is { CamadaDeTeste: AudioDirector.Camada.Lugar, FaixaDeTeste.Length: > 0 } audio
				&& audio.UltimaEntradaDeTeste.Faixa == audio.FaixaDeTeste)
			{
				PesarAFaixa(audio.FaixaDeTeste);
				Nota($"o tema do LUGAR entrou no ar na chegada na zona (`{audio.FaixaDeTeste.GetFile()}`, {_bytes.GetValueOrDefault(audio.FaixaDeTeste) / 1e6:0.0} MB):"
					 + $" a thread principal esperou {audio.UltimaEntradaDeTeste.Espera:0.0} ms pelo arquivo, e gastou {audio.UltimaEntradaDeTeste.Tocador:0.0} ligando o tocador");
			}
			Virar(1);
			return;
		}

		_alvo = srv.ForjarCorpoDeFoto(cli.LocalId, new Vec2(5 * ZoneCollision.TileSize, 0), "Estouro: o alvo", 200_000, comEscada: false);
		Conferir(_alvo != 0, "o alvo entrou no mundo, a 5 tiles da mao");
		if (_alvo == 0) { Fechar(); return; }
		Virar(1);
	}

	private void EsperarOAlvo(World mundo)
	{
		if (!_temas && !_avulsos && mundo.CorpoDeTeste(_alvo) == null)
		{
			if (_t > 8) { Conferir(false, "o alvo tem SPRITE na tela"); Fechar(); }
			return;
		}
		if (_t < 2) return;
		_baseDe = _quadros.Count;
		Virar(2);
	}

	private void MedirABase()
	{
		if (_t < 2) return;
		_baseAte = _quadros.Count;
		// (com `--verbose` na linha, daqui em diante cada arquivo que a thread principal le ganha o quadro dele)
		if (_temas) LigarAEscutaDeCarga();
		Virar(3);
	}

	/// <summary>
	/// Quanto cada bola tira do alvo, em vida de membro: pouco acima do corte dos fracos (`DanoDeKi.CorteDoFraco`,
	/// 10). Abaixo dele o tiro estoura e NAO e golpe -- o servidor nao manda relato, e sem relato nao ha clarao,
	/// faisca nem musica de combate, que e metade do que esta bancada mede. (Ela atirava com 0,002, de cocegas; no
	/// dia em que o corte entrou as sete bolas estouraram e nenhuma luta abriu.) Doze nao marca o sprite -- a ferida
	/// so aparece com 15% do membro --, e o alvo e sarado antes de cada bola pra duas no mesmo membro nao marcarem.
	/// </summary>
	private const double DanoDaBola = 12;

	private void Atirar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		_tiros++;
		srv.SararCorpoDeFoto(_alvo);
		_tiro = srv.BolaDeFotoQueFere(cli.LocalId, _alvo, new Vec2(1, 0), alcanceTiles: 12, danoFinal: DanoDaBola);
		if (_tiro == 0) { Conferir(false, $"a bola {_tiros} saiu pelo `Disparar` de producao"); Fechar(); return; }
		Marcar($"a bola {_tiros} DISPARADA no servidor");
		Virar(4);
	}

	private bool _coletou;

	/// <summary>
	/// SETE BOLAS, TRES LUTAS. A primeira bola e a pergunta; a segunda e a terceira sao o controle da regua (nada
	/// e novo). A quarta sai depois de a poeira da terceira sumir e de o coletor do .NET passar -- e onde o custo
	/// do primeiro uso VOLTAVA numa luta longa. A sexta liga o defeito na poeira e a setima, de novo depois da
	/// coleta, tem que reprovar.
	///
	/// AS LUTAS sao da trilha: o primeiro golpe de cada uma sorteia e poe no ar uma faixa nova (ver
	/// <see cref="AnotarAFaixa"/>). A primeira vai da bola 1 a 3, a segunda e a bola 4 e a terceira comeca na 5,
	/// com o defeito da musica ligado desde antes da 4. O DEFEITO DA MUSICA E O DA POEIRA NAO DIVIDEM BOLA: no dia
	/// em que o relato do golpe esperar a bola chegar na tela, golpe e estouro caem no MESMO quadro, e dois
	/// defeitos nele se esconderiam um atras do outro.
	/// </summary>
	private void EsperarOEstouro(World mundo)
	{
		if (_estouros < _tiros)
		{
			if (_t > 6) { Conferir(false, $"a bola {_tiros} estourou no alvo em 6 s"); Fechar(); }
			return;
		}
		if (_t < 2.5) return;

		switch (_tiros)
		{
			case 1 or 2: Virar(3); return;

			case 3:
				// A POEIRA MORRE, O COLETOR PASSA -- E A LUTA 1 ACABA: a tag cai (adiantada) no meio da espera.
				bool acabou = LutaEncerrada(mundo);
				if (!PoeiraMortaEColetorPassado() || !acabou) return;
				// O DEFEITO DA MUSICA ENTRA AQUI, antes do golpe que abre a luta 2. A faixa DELA ja esta na memoria
				// (foi pedida quando a da luta 1 entrou no ar); o que o defeito tira e o pedido da SEGUINTE.
				AudioDirector.SemThreadDeCargaDeTeste = true;
				Virar(3);
				return;

			case 4:
				// A LUTA 2 ACABA, e a 3 vai abrir com uma faixa grande que este processo nunca leu.
				if (!LutaEncerrada(mundo)) return;
				PorFaixaGrandeNaFila();
				Virar(3);
				return;

			case 5:
				// O DEFEITO DA MUSICA SAI (ja foi medido) E O DA POEIRA ENTRA: a poeira da sexta bola solta os
				// moldes, e a setima nasce sem shader pronto.
				AudioDirector.SemThreadDeCargaDeTeste = false;
				PoeiraDeEstrago.SemSegurarDeTeste = true;
				Virar(3);
				return;

			case 6:
				if (!PoeiraMortaEColetorPassado()) return;
				Virar(3);
				return;

			default: Fechar(); return;
		}
	}

	/// <summary>A POEIRA VIVE 2,76 s; depois dela, o coletor. Verdadeiro quando os dois ja passaram.</summary>
	private bool PoeiraMortaEColetorPassado()
	{
		if (_t < 5.0) return false;
		if (!_coletou)
		{
			_coletou = true;
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			Marcar("coleta forcada do .NET");
			return false;
		}
		if (_t < 6.5) return false;
		_coletou = false;
		return true;
	}

	// =====================================================================
	// OS OUTROS PRIMEIROS USOS DE KI, UM POR VEZ (`--pecas`)
	// =====================================================================
	private int _peca;
	private Node2D? _palco;

	/// <summary>
	/// CADA EFEITO NASCE SOZINHO, com um segundo entre um e outro, pela porta de PRODUCAO -- e o custo do
	/// primeiro uso de cada um cai num quadro so dele. O palco e um node da propria `World`: mesmo canvas,
	/// mesma tela, mesma luz de quem joga.
	/// </summary>
	private void AndarNasPecas(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 1.0) return;
		_t = 0;

		if (_palco == null)
		{
			_palco = new Node2D { Name = "PalcoDasPecas", ZIndex = 5 };
			mundo.AddChild(_palco);
		}
		Vector2 onde = (mundo.PosicaoLocal ?? Vector2.Zero) + new Vector2(64, -48);
		var azul = new Color(0.3f, 0.6f, 1f);

		switch (_peca++)
		{
			case 0:
				// a mesma chamada do `FimDeProjetil.Defletido` no `World.RecolherTiro`
				CombatFx.Onda(_palco, onde, 60, azul, 0.2);
				Evento("o primeiro ANEL DE DEFLEXAO (`CombatFx.Onda`, shader `Impacto`)", PrimeiroUso());
				break;
			case 1:
				_tiro = srv.RaioDeFoto(cli.LocalId, new Vec2(0, -1), alcanceTiles: 6, baseDano: 0.002);
				if (_tiro == 0) { Conferir(false, "o raio saiu pelo `Disparar` de producao"); Fechar(); }
				break;   // o evento e o do pacote de nascimento -- ver `AoNascerTiro`
			case 2:
				srv.LimparAFoto();
				// como o `World.TickDosChoquesDeKi` a monta: definida antes de entrar na arvore
				var estrela = new ChoqueDeKi { Name = "ChoqueDaBancada" };
				estrela.Definir(onde, Vector2.Right, 30f, 18f, azul, new Color(1f, 0.3f, 0.8f));
				_palco.AddChild(estrela);
				Evento("a primeira ESTRELA DO EMBATE (`ChoqueDeKi`, shader `ChoqueDeKi`)", PrimeiroUso());
				break;
			case 3:
				foreach (Node n in _palco.GetChildren()) if (n is ChoqueDeKi c) c.Soltar();
				CombatFx.Escudo(_palco, onde, azul);
				Evento("o primeiro ESCUDO do parry (`CombatFx.Escudo`)", _semEnsaio != null ? Papel.Nota : Papel.Regua);
				break;
			case 4:
				CombatFx.Impacto(_palco, onde, 1.3f, Colors.White);
				Evento("a primeira FAISCA do golpe (`CombatFx.Impacto`)", _semEnsaio != null ? Papel.Nota : Papel.Regua);
				break;
				case 5: break;   // os ultimos quadros da faisca
				// E O PRIMEIRO BORRAO DO PROCESSO, que nao e efeito de ki e tem roteiro proprio (o arquivo
				// `RoboDoPrimeiroEstouro.Borrao.cs`): um gesto por segundo, como as pecas de cima. DEPOIS DELE, A PRIMEIRA
				// MIRAGEM -- o vulto do Zanzoken, com o mesmo feitio (o arquivo `RoboDoPrimeiroEstouro.Miragem.cs`) --, e a
				// rodada fecha quando ela acaba.
				default: if (!AndarNoBorrao(mundo, cli) && !AndarNaMiragem(mundo, cli)) Fechar(); break;
		}
	}

	// =====================================================================
	// AS CONTAS
	// =====================================================================
	private double TotalMs(int i) => i + 1 < _quadros.Count ? (_quadros[i + 1].Comeco - _quadros[i].Comeco) / 1000.0 : 0;
	private double ScriptMs(int i) => (_quadros[i].Fim - _quadros[i].Comeco) / 1000.0;

	/// <summary>Quanto do quadro `i` foi o coletor do .NET com o processo parado -- na fase de script, e no quadro inteiro.</summary>
	private double ColetorNoScript(int i) => _quadros[i].PausaNoFim - _quadros[i].PausaNoComeco;
	private double ColetorNoQuadro(int i) => i + 1 < _quadros.Count ? _quadros[i + 1].PausaNoComeco - _quadros[i].PausaNoComeco : 0;

	/// <summary>A CPU do desenho do quadro `i`: o Godot entrega a do ultimo quadro desenhado, entao ela esta na leitura do seguinte.</summary>
	private double DesenhoMs(int i) => i + 1 < _quadros.Count ? _quadros[i + 1].DesenhoCpu : 0;

	/// <summary>O TRABALHO do quadro `i` -- a regua. Ver o cabecalho.</summary>
	private double Trabalho(int i) => ScriptMs(i) - ColetorNoScript(i) + DesenhoMs(i);

	/// <summary>O quadro inteiro em relogio de parede, sem as pausas do coletor.</summary>
	private double TotalLiquido(int i) => TotalMs(i) - ColetorNoQuadro(i);

	private static double Mediana(List<double> v) { v.Sort(); return v.Count > 0 ? v[v.Count / 2] : 0; }

	private void ImprimirJanela(int centro, int antes, int depois)
	{
		for (int i = Math.Max(1, centro - antes); i <= Math.Min(_quadros.Count - 2, centro + depois); i++)
		{
			Quadro q = _quadros[i];
			_linhas.Add($"           q{i - centro,+3}  trabalho {Trabalho(i),6:0.0} ms (script {ScriptMs(i) - ColetorNoScript(i),6:0.0} + desenho {DesenhoMs(i),5:0.0})"
						+ $"  quadro inteiro {TotalMs(i),6:0.0} ms  delta seguinte {_quadros[i + 1].Delta * 1000,6:0.0}"
						+ $"  pipelines 2D +{_quadros[i + 1].Canvas - q.Canvas}"
						+ (ColetorNoQuadro(i) > 0.05 ? $"  coletor {ColetorNoQuadro(i):0.0} ms" : "")
						+ (q.Marcas.Length > 0 ? "   <-- " + q.Marcas : ""));
		}
	}

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;
		S?.LimparAFoto();

		try { Relatar(); }
		finally
		{
			// OS DEFEITOS SAEM AQUI, com conferencia ou sem ela.
			SoltarOsDefeitosDosPrimeirosDesenhos();
			SoltarOsAvulsos();
			SoltarAMiragem();
			Aquecimento.SemEnsaioDeTeste = false;
			Aquecimento.SemEnsaioDaCenaDeTeste = false;
			Aquecimento.SemEnsaioDoBorraoDeTeste = false;
			SoltarOBorrao();
			Aquecimento.SemAdiantarACenaDeTeste = false;
			Aquecimento.SemEnsaioDosRaiosDeTeste = false;
			Aquecimento.EstrelaComLuzDeTeste = false;
			PoeiraDeEstrago.SemSegurarDeTeste = false;
			AudioDirector.SemThreadDeCargaDeTeste = false;
			SonsPresos.SemSegurarDeTeste = false;
			Aquecimento.SemSegurarOsScriptsDeTeste = false;
			SoltarOsSons();
			Transformacao.EspiaoDePedaco -= AoMedirPedaco;
			SoltarAEscutaDeCarga();
		}

		GD.Print("\n[estouro] ===== O PRIMEIRO ESTOURO DE KI DO PROCESSO =====");
		foreach (string l in _linhas) GD.Print("[estouro] " + l);
		GD.Print(_falhas.Count == 0
			? "[estouro] ===== TUDO OK ====="
			: $"[estouro] ===== {_falhas.Count} FALHA(S) =====\n[estouro]   " + string.Join("\n[estouro]   ", _falhas));
		GetTree().Quit();
	}

	/// <summary>
	/// O RELATORIO DO GRAVADOR SOZINHO: todo quadro caro desde o corpo no mundo, com o instante, a fase em que o
	/// tempo caiu e o que chegou nele. Nao fecha o jogo -- quem manda na cena e o outro robo.
	/// </summary>
	private void FecharOPassivo()
	{
		_acabou = true;
		const double TrabalhoCaro = 12, QuadroCaro = 25;

		GD.Print("");
		GD.Print($"[estouro] ===== O GRAVADOR SOZINHO: {_passivo:0} s desde o corpo no mundo, {_quadros.Count - _entrouNoMundo} quadros =====");
		GD.Print($"[estouro] cache de shader em disco no comeco: {(_shadersEmDiscoNoComeco == 0 ? "VAZIO" : $"{_shadersEmDiscoNoComeco} shader(s) de particula")}"
				 + $" | o ensaio rodou? {Aquecimento.Ensaiou}");
		GD.Print($"[estouro] todo quadro com trabalho acima de {TrabalhoCaro:0} ms ou inteiro acima de {QuadroCaro:0} ms (ja sem o coletor do .NET), e todo quadro com tiro, golpe ou estouro:");
		int coletas = 0;
		double pausas = 0;
		for (int i = Math.Max(1, _entrouNoMundo); i + 1 < _quadros.Count; i++)
		{
			if (ColetorNoQuadro(i) > 1) { coletas++; pausas += ColetorNoQuadro(i); }
			bool caro = Trabalho(i) > TrabalhoCaro || TotalLiquido(i) > QuadroCaro;
			if (!caro && _quadros[i].Marcas.Length == 0) continue;
			GD.Print($"[estouro]   {(caro ? "CARO" : "    ")} t={(_quadros[i].Comeco - _quadros[_entrouNoMundo].Comeco) / 1e6,7:0.000}s"
					 + $"  trabalho {Trabalho(i),6:0.0} ms (script {ScriptMs(i) - ColetorNoScript(i),6:0.0} + desenho {DesenhoMs(i),5:0.0})"
					 + $"  quadro inteiro {TotalMs(i),6:0.0} ms  pipelines 2D +{_quadros[i + 1].Canvas - _quadros[i].Canvas}"
					 + (ColetorNoQuadro(i) > 0.05 ? $"  coletor {ColetorNoQuadro(i):0.0} ms" : "")
					 + (_quadros[i].Marcas.Length > 0 ? "   <-- " + _quadros[i].Marcas : ""));
		}
		GD.Print($"[estouro] o coletor do .NET: {coletas} pausa(s), {(coletas > 0 ? pausas / coletas : 0):0.0} ms cada");
		ImprimirOsScriptsDoPassivo();
		ImprimirOsSonsDoPassivo();
		ImprimirOBorraoDoPassivo();
		ImprimirAMiragemDoPassivo();
		GD.Print("[estouro] ===== PASSIVO FIM =====");
	}

	private void Relatar()
	{
		Nota($"rodada: {(_scripts ? "os scripts de node (`--scripts`)" : _sons ? "os sons de efeito (`--sons`)" : _temas ? "os temas que entram fora da luta (`--temas`)" : _pecas ? "os outros primeiros usos (`--pecas`)" : _avulsos ? "os primeiros usos avulsos (`--avulsos`)" : "a bola de producao")}"
			 + (_sons && _semSegurarOsSons ? $" | DEFEITO INJETADO: {DefeitoDosSons} (`--semsegurarossons`)" : "")
			 + (_scripts && _semSegurarOsScripts ? $" | DEFEITO INJETADO: {DefeitoDosScripts} (`--semsegurarosscripts`)" : "")
			 + (_semEnsaio != null ? $" | DEFEITO INJETADO: {_semEnsaio}" : "")
			 + RotuloDoBorrao()
			 + RotuloDaMiragem()
			 + RotuloDosAvulsos()
			 + (_temas && _semThreadDeCarga ? " | DEFEITO INJETADO: a musica sem a thread de carga, a rodada inteira (`--semthreaddecarga`)" : "")
			 + (_semEnsaioDaCena ? " | DEFEITO INJETADO: o aquecimento sem as folhas e sem o ensaio da cinematica (`--semensaiodacena`)" : "")
			 + (_semAdiantarACena ? $" | DEFEITO INJETADO: {DefeitoDeLerNaHora} (`--semadiantaracena`)" : "")
			 + (_semEnsaioDosRaios ? $" | DEFEITO INJETADO: {DefeitoDosRaios} (`--semensaiodosraios`)" : "")
			 + (_driverFrio ? $" | o DRIVER de video nunca viu {string.Join(", ", _shadersFrios.Select(c => "`" + c.GetFile() + "`"))} (`--driverfrio`)" : "")
			 + NotaDosPrimeirosDesenhos()
			 + (_raiosComLuz ? " | com uma luz em cima do corpo (`--raioscomluz`)" : "")
			 + (_estrelaComLuz ? $" | DEFEITO INJETADO: {DefeitoDaEstrela} (`--estrelacomluz`)" : "")
			 + (Tem("--lobbyteste") ? " | login de gente (`--lobbyteste`)" : " | login de robo")
			 + (Tem("--estouronoite") ? " | de NOITE" : " | meio-dia")
			 + $" | cache de shader em disco no comeco: {(_shadersEmDiscoNoComeco == 0 ? "VAZIO" : $"{_shadersEmDiscoNoComeco} shader(s) de particula")}");

		// ---- O ENSAIO ----
		if (_semEnsaio == null)
			Conferir(Aquecimento.Ensaiou, "o ensaio dos efeitos de ki rodou inteiro antes do primeiro tiro");
		else
			Nota($"o ensaio rodou? {Aquecimento.Ensaiou} (nesta rodada nao e pra rodar)");

		// ---- O LOBBY: onde o custo foi parar ----
		if (_entrouNoMundo > 2)
		{
			int pior = 1;
			for (int i = 1; i < _entrouNoMundo && i + 1 < _quadros.Count; i++) if (TotalLiquido(i) > TotalLiquido(pior)) pior = i;
			Nota($"antes de o corpo aparecer: {_entrouNoMundo} quadros; o mais caro custou {TotalLiquido(pior):0} ms"
				 + $" (trabalho {Trabalho(pior):0} ms) -- e ai que o ensaio e a montagem do mundo sao pagos");
		}

		// ---- A BASE ----
		if (_baseDe < 0 || _baseAte < _baseDe + 30) { Conferir(false, "a base foi medida (2 s parado, antes do primeiro tiro)"); return; }
		var trabalhos = new List<double>();
		var totais = new List<double>();
		for (int i = _baseDe; i < _baseAte && i + 1 < _quadros.Count; i++) { trabalhos.Add(Trabalho(i)); totais.Add(TotalLiquido(i)); }
		double baseTrabalho = Mediana(trabalhos), baseTotal = Mediana(totais);
		double tetoDeTrabalho = baseTrabalho + FolgaDoEfeito, tetoDoNascimento = baseTrabalho + FolgaDoNascimento;
		// DUAS VOLTAS DO MONITOR PERDIDAS, no maximo: 3,2 quadros da base (27 ms num monitor de 120 Hz).
		double tetoDoQuadro = baseTotal * 3.2;
		int acima = trabalhos.FindAll(x => x > tetoDeTrabalho).Count;
		Nota($"BASE ({trabalhos.Count} quadros parado): trabalho mediano {baseTrabalho:0.0} ms (p95 {trabalhos[(int)(trabalhos.Count * 0.95)]:0.0},"
			 + $" pior {trabalhos[^1]:0.0}), quadro inteiro mediano {baseTotal:0.0} ms | TETOS: trabalho {tetoDeTrabalho:0.0} ms num efeito e"
			 + $" {tetoDoNascimento:0.0} no nascimento de um tiro, quadro inteiro {tetoDoQuadro:0.0} ms"
			 + $" | {acima} quadro(s) da base acima do teto de trabalho");

		// ---- CADA COISA MEDIDA ----
		for (int n = 0; n < _eventos.Count; n++)
		{
			(string oque, int q, Papel papel, string defeito, bool nascimento) = _eventos[n];
			// O CUSTO e o do quadro em que ela nasceu -- ver o cabecalho.
			if (q + 1 >= _quadros.Count) { Conferir(false, $"{oque}: a corrida gravou o quadro seguinte"); continue; }
			double trabalho = Trabalho(q), quadro = TotalLiquido(q);
			double teto = nascimento ? tetoDoNascimento : tetoDeTrabalho;
			// O QUADRO INTEIRO pode levar o trabalho que lhe cabe e mais uma volta do monitor esperando: sem isto,
			// num monitor de 144 Hz o teto do quadro (22 ms) ficava ABAIXO do teto de trabalho de um nascimento.
			double tetoDesteQuadro = Math.Max(tetoDoQuadro, teto + baseTotal);
			// A FAIXA DE COMBATE, quando o evento e a entrada de uma: a regua dela tem uma segunda perna, que e a
			// thread principal NAO ter esperado pelo arquivo. O teto de quadro sozinho nao enxerga uma leitura de
			// 3 ms -- e o que custa uma faixa de 2 MB com o cache do Windows quente.
			bool temFaixa = _entradas.TryGetValue(n, out (double Espera, double Tocador) faixa);
			// O TEMA DA RODADA `--temas`, quando o evento e o PEDIDO de um: quantos quadros depois ele entrou no ar.
			// Zero e a faixa que ja estava na memoria (ou que foi lida na hora); mais que zero e a que a maquina de
			// musica esperou da thread de carga.
			bool temAtraso = _atrasos.TryGetValue(n, out (int Quadros, double Ms) atraso);
			bool normal = trabalho <= teto && quadro <= tetoDesteQuadro && (!temFaixa || faixa.Espera <= EsperaDaFaixa);
			string medida = $"trabalho {trabalho:0.0} ms contra o teto de {teto:0.0}; quadro inteiro {quadro:0.0} ms contra {tetoDesteQuadro:0.0}"
							+ (temFaixa ? $"; a thread principal esperou {faixa.Espera:0.0} ms pela faixa contra {EsperaDaFaixa:0.0}, e gastou {faixa.Tocador:0.0} ligando o tocador" : "")
							+ (!temAtraso ? "" : atraso.Quadros == 0 ? "; ela entrou no ar neste quadro" : $"; ela entrou no ar {atraso.Quadros} quadro(s) depois, {atraso.Ms:0.0} ms");
			bool esperada = temAtraso && atraso.Quadros > 0;

			switch (papel)
			{
				case Papel.Regua:
					Conferir(normal, $"{oque}: {(nascimento ? "sem a travada do primeiro uso" : esperada ? "custa um quadro normal, sem a thread principal esperar pela faixa" : temFaixa ? "custa um quadro normal, com a faixa ja na memoria" : "custa um quadro normal")} ({medida})");
					break;
				case Papel.Injetado:
					Conferir(!normal, $"(defeito injetado: {(defeito.Length > 0 ? defeito : _semEnsaio)}) a mesma regua REPROVA {oque} ({medida})");
					break;
				default: Nota($"{oque}: {medida}"); break;
			}
			if (papel != Papel.Nota || !normal) ImprimirJanela(q, 1, 3);   // os vizinhos, pra leitura: so o `q 0` foi cobrado
			ImprimirOsPedacos(q);
		}

		// (o sal do `--driverfrio` vale pra toda rodada: ver o arquivo `RoboDoPrimeiroEstouro.Raios.cs`)
		RelatarOSal();
		// E OS QUADROS DO ENSAIO -- os raios da forma, o relampago, a chuva, o corpo iluminado, o quad da estrela: os que
		// esta rodada alcancou. Ver os arquivos `RoboDoPrimeiroEstouro.Raios.cs` e `RoboDoPrimeiroEstouro.Nascidas.cs`.
		// (A rodada de MEDICAO das cenas e a dos sons nao tem quadro deles.)
		if (!_sons && !RodadaDasCenas) ConferirOsPrimeirosDesenhos();

		if (_scripts) RelatarOsScripts();
		else if (_sons) RelatarOsSons();
		else if (_temas) RelatarOsTemas();
		else if (_pecas)
		{
			ConferirOPrimeiroBorrao();
			ConferirAPrimeiraMiragem();
		}
		else if (_avulsos) RelatarOsAvulsos();
		else
		{
			Conferir(_estouros >= 7, $"as sete bolas estouraram no alvo ({_estouros} de 7)");
			Conferir(_golpes == 7, $"e cada uma foi GOLPE: o relato dela chegou ({_golpes} de 7; tiro abaixo do corte dos fracos estoura sem relato)");
			Conferir(_lutas == 3, $"a corrida abriu tres lutas, cada uma com a sua faixa de combate ({_lutas} de 3)");
		}

		// ---- TODO QUADRO CARO QUE NAO E DO COLETOR, na ordem -- pra nada caro passar sem rotulo ----
		Nota($"todo quadro com trabalho acima do teto de efeito ({tetoDeTrabalho:0.0} ms) depois de a base comecar:");
		int coletas = 0;
		double pausas = 0;
		for (int i = _baseDe; i + 1 < _quadros.Count; i++)
		{
			if (ColetorNoQuadro(i) > 1) { coletas++; pausas += ColetorNoQuadro(i); }
			if (Trabalho(i) > tetoDeTrabalho)
				_linhas.Add($"           t={(_quadros[i].Comeco - _quadros[_baseDe].Comeco) / 1e6,7:0.000}s  trabalho {Trabalho(i):0.0} ms"
							+ $" (script {ScriptMs(i) - ColetorNoScript(i):0.0} + desenho {DesenhoMs(i):0.0}), quadro inteiro {TotalMs(i):0.0} ms"
							+ (_quadros[i].Marcas.Length > 0 ? "   <-- " + _quadros[i].Marcas : "   <-- (sem rotulo)"));
		}
		double segundos = (_quadros[^1].Comeco - _quadros[_baseDe].Comeco) / 1e6;
		Nota($"o coletor do .NET, fora da regua: {coletas} pausa(s) em {segundos:0.0} s, {(coletas > 0 ? pausas / coletas : 0):0.0} ms cada"
			 + $" ({pausas / (segundos * 10):0.0}% do tempo com o processo parado)");

		// ---- E TODA PIPELINE 2D QUE NASCEU EM JOGO: o que o ensaio do lobby nao desenhou (o arquivo `RoboDoPrimeiroEstouro.Raios.cs`) ----
		ListarAsPipelinesNascidasEmJogo();
	}

	/// <summary>
	/// O FECHO DA RODADA DOS TEMAS: os seis entraram no ar -- sem isto, um tema que nunca entrasse passaria pela
	/// regua so com a perna do quadro, que ele nem chega a ocupar -- e a ESPERA, faixa a faixa, num quadro so.
	/// </summary>
	private void RelatarOsTemas()
	{
		// (a rodada de MEDICAO das cenas nao poe tema nenhum no ar pelo ESC: o fecho dela e outro)
		if (RodadaDasCenas) { RelatarAsCenas(); return; }
		// (nem a do ceu: o fecho dela sao so os quadros do ensaio, que o `Relatar` ja conferiu)
		if (_rodadaDoCeu) return;

		Conferir(_atrasos.Count == 6 && _temasDaRodada.Count == 6,
				 $"os seis temas da rodada entraram no ar -- tres vezes o ESC, tres estreias ({_atrasos.Count} de 6, {_temasDaRodada.Count} pedido(s))");
		Nota(_painelNoLobby >= 0
			? $"o primeiro uso do painel de pause foi pago no lobby, sem musica: {_painelNoLobby:0.0} ms pra abrir e fechar"
			: "o painel de pause NAO foi aberto no lobby (nao havia menu la): o ESC 1 paga o primeiro uso dele");

		Nota("A ESPERA, FAIXA A FAIXA -- o que a thread principal esperou pelo arquivo, o que gastou ligando o tocador, e quando a faixa entrou no ar:");
		foreach ((int evento, string rotulo) in _temasDaRodada)
		{
			if (!_entradas.TryGetValue(evento, out (double Espera, double Tocador) e) || !_atrasos.TryGetValue(evento, out (int Quadros, double Ms) a))
			{
				_linhas.Add($"           {rotulo}: NAO ENTROU NO AR");
				continue;
			}
			_linhas.Add($"           {rotulo}: esperou {e.Espera:0.0} ms, tocador {e.Tocador:0.0} ms, "
						+ (a.Quadros == 0 ? "entrou no quadro do pedido" : $"entrou {a.Quadros} quadro(s) depois do pedido ({a.Ms:0.0} ms)")
						+ (_eventos[evento].Papel == Papel.Injetado ? "   <-- DEFEITO INJETADO" : ""));
		}
	}
}

/// <summary>
/// UM GANCHO NUMA PONTA DO QUADRO: um node que roda antes de todos os outros (`ProcessPriority` muito
/// baixo) ou depois de todos (muito alto) e avisa. Dois deles partem o quadro em "o que os scripts
/// fizeram" e "o resto" -- ver <see cref="RoboDoPrimeiroEstouro"/>.
/// </summary>
public partial class MarcaDeQuadro : Node
{
	public Action? Agora;

	public override void _Process(double delta) => Agora?.Invoke();
}
