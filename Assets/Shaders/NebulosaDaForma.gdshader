shader_type canvas_item;

// `unshaded`: a nuvem NAO e iluminada pelo mundo. A `PointLight2D` do node `Aura` fica no MESMO
// corpo e a 160 px de raio -- sem isto, acender a chama de carga multiplicaria a nebulosa pela cor
// da chama, e o indigo viraria dourado sujo em quem carrega Ki. Ela tem luz propria, como todo
// efeito de energia deste projeto.
//
// `blend_mix` (o padrao, escrito por extenso): aditivo apagaria justamente o indigo -- somar quase
// preto nao muda pixel nenhum -- e a rampa perderia a ponta escura que da profundidade.
render_mode blend_mix, unshaded;

// ============================================================================
// A NEBULOSA DO ULTRA INSTINTO -- a nuvem de galaxia que envolve o corpo.
//
// Esta e a metade da referencia que **nao existe no disco**. O DM veste duas folhas coladas no
// corpo (`UltraInstinct.dm:479-480`: `ui_aura` e `ui_dots`) e as duas medem 32x32 -- elas cobrem o
// azul-lavanda encostado na pele e as micro-particulas brancas subindo, e mais nada. O fundo
// indigo profundo e as volutas violeta-azuladas com filamento nao tem arte, e por isso sao
// procedurais aqui.
//
// ============================ O TAMANHO E A CAMADA VIERAM DO DONO, E ELE CORRIGIU OS DOIS =========
// A primeira versao leu a referencia como "a nuvem ocupa TRES corpos de largura" e desenhou um quad
// de 96 px ATRAS do boneco. A foto no jogo mostrou o que isso e de verdade: um halo redondo bem
// maior que o personagem, com ele inteiro dentro e o efeito passando por tras. A palavra do dono:
//
//   "a aura do ultra instinto, e do perfected ultra instinct estao mt grandes, era pra estar perto
//    do corpo, no MAXIMO 5 pixels de distancia do corpo, e o efeito deveria ficar sobre o corpo e
//    nao atras"
//
// Sao DUAS correcoes independentes, e as duas mudaram este arquivo:
//   * TAMANHO -- a elipse deixou de ser um numero de arte e passou a ser MEDIDA: o C# le a silhueta
//     do quadro que o boneco esta vestindo e soma a folga;
//   * CAMADA -- a nuvem desenha POR CIMA do sprite (ordem de irmao, no `World.AoEntrar`), e por isso
//     nasceu o `veu_no_corpo`: por cima sem o veu, o alfa de ~0,74 do miolo apagaria o rosto.
//
// E DEPOIS VEIO A TERCEIRA, que e a que matou a elipse:
//
//   "a aura em si ta estranha, ela deveria n ser um circulo e sim CONTORNAR O CORPO com esse efeito"
//
// Tamanho certo, forma errada. A resposta e o `campo_do_corpo` -- ver o bloco dele. As
// microparticulas NAO foram tocadas: o dono aprovou ("os pontinhos subindo estao legais"), e elas
// continuam nascendo dentro da `mascara`, que agora e o volume novo.
//
// ============================ POR QUE FBM COM *DOMAIN WARP* E NAO RUIDO PURO ============================
// Ruido puro -- ate FBM de quatro oitavas -- sai parecendo FUMACA: manchas moles, bordas redondas,
// nenhuma direcao. A referencia nao e fumaca; e nuvem de galaxia, com FILAMENTO: veios claros que
// se esticam e se dobram uns em volta dos outros.
//
// O filamento nao vem de mais oitavas; vem de deformar a COORDENADA antes de amostrar (o warp de
// dominio do iq). Amostra-se o ruido pra achar um deslocamento, desloca-se o ponto e amostra-se de
// novo -- duas vezes, aqui. O resultado e que regioes vizinhas do campo sao ESTICADAS em direcoes
// diferentes, e e esse estiramento que o olho le como veio.
//
// Custo: 3 chamadas de FBM em vez de 1 (o `q` e o `r` sao dois vetores, entao sao 5 amostras de
// FBM no total). Num quad de 42x42 por corpo transformado (o que a silhueta de 16x32 mais 5 px de
// folga produz) isso e barato -- e o `forca` desliga o desenho inteiro em quem nao esta na linha do
// Instinto, que e quase todo mundo. O quad encolheu 5x em area quando a nuvem veio pro corpo.
// ====================================================================================================
//
// ============================ TRES ARMADILHAS JA PAGAS NESTE REPO, E NENHUMA SE REPETE AQUI ==========
//   1. `i = max(r,g,b)` sem normalizar ESCURECE (ver `Aura.gdshader`). Nao ha textura aqui: a cor
//      e gerada, e o RGB nunca passa por um multiplicador menor que 1.
//   2. Passar BRANCO pra "nao tingir" APAGA a arte. Nao ha arte pra apagar -- as cores da rampa
//      SAO o desenho.
//   3. `forca` multiplicando RGB **e** alfa deixa escuro E transparente ao mesmo tempo -- foi o
//      "tudo cinza escuro" que o dono viu na chama de carga. Aqui vale a mesma correcao que ficou
//      no `Aura.gdshader`: **a `forca` so governa o ALFA**. O RGB sai sempre cheio.
// ====================================================================================================
//
// ============================ E O QUE IMPEDE O "AMARRONZADO POR CIMA DO VERDE DO CHAO" ==============
// O halo do `Personagem.gdshader` morreu por isso: cor escura somada/misturada sobre grama nao e
// nem uma cor nem a outra, e o dono viu na hora. A nebulosa carrega justamente um indigo QUASE
// PRETO, que e o candidato perfeito a repetir o defeito.
//
// A guarda esta em amarrar o ALFA a densidade com expoente > 1 (ver `curva_alfa`): onde a nuvem e
// escura ela e RALA (e a franja, onde a densidade cai a zero) e some antes de sujar o chao; onde
// ela e opaca ela ja subiu pro violeta e pro ciano-claro, que sao cores que CLAREIAM o fundo. O
// indigo existe pra dar profundidade a borda, nao pra pintar o cenario.
// ====================================================================================================
// ============================================================================

// ============================ A RAMPA, EM UNIFORM -- E AGORA ESCRITA DE FORA ============================
// Aqui estava escrito que estes valores eram "o unico jeito de afinar o efeito sem recompilar", e que
// o Core so decidia SE a nebulosa existe. Isso valia enquanto ela tinha UM dono.
//
// O dono pediu a mesma nuvem no Ultra Ego, em roxo -- *"a aura/carga do ultra ego e a mesma do
// instinto superior so q ROXA ao inves de branca/prateada, mas tem os mesmos efeitos"*. "Os mesmos
// efeitos" quer dizer que nao ha shader novo: ha uma PALETA a mais. E um shader nao sabe que forma
// esta vestindo, entao um valor padrao so pode servir a uma das duas.
//
// Quem escreve as quatro cores hoje e o `NebulosaDaForma.Definir`, com a resposta do
// `Catalogo.PaletaDaNebulosa` (Core). OS PADROES ABAIXO CONTINUAM SENDO A PALETA DO ULTRA INSTINTO,
// medida na referencia que o dono mandou -- mas hoje eles sao DOCUMENTACAO e previa do editor, nao a
// fonte. Mexer neles nao muda o jogo; mexa no Core.
// ======================================================================================
uniform vec4 cor_borda : source_color = vec4(0.141, 0.102, 0.239, 1.0); // #241a3d -- indigo quase preto
uniform vec4 cor_meio  : source_color = vec4(0.435, 0.373, 0.682, 1.0); // #6f5fae -- violeta-azulado
uniform vec4 cor_perto : source_color = vec4(0.847, 0.910, 1.000, 1.0); // #d8e8ff -- ciano-claro quase branco

/// A COR DA MICROPARTICULA que sobe por cima de tudo. Ela era `vec3(1.0)` CRAVADA no meio do
/// `fragment` -- a unica cor deste arquivo que nao era uniform, e a que o Ultra Ego precisava mudar.
/// No Ultra Instinto o padrao continua valendo (branco puro, que e a folha `UI_Dots` do DM); no Ultra
/// Ego ela sai lilas claro, um passo acima da ponta clara da rampa, que e o que a faz ler como faisca
/// e nao como parte da nuvem.
uniform vec4 cor_dos_pontos : source_color = vec4(1.0, 1.0, 1.0, 1.0);

/// ONDE A RAMPA VIRA. Abaixo disto vai de `cor_borda` a `cor_meio`; acima, de `cor_meio` a
/// `cor_perto`. Baixo demais e a nuvem fica lavada; alto demais e ela fica escura e o ciano some.
uniform float parada_meio : hint_range(0.05, 0.95) = 0.55;

/// ROLAGEM LENTA. E uma forma de CALMA -- o Ultra Instinto e o oposto de fufuria. Em voltas de
/// ruido por segundo: 0,05 quer dizer que a nuvem leva ~20 s pra andar uma volta inteira.
uniform float velocidade : hint_range(0.0, 0.5) = 0.05;

/// Quantas volutas cabem no quad. Alto demais vira granulado; baixo demais vira uma mancha so --
/// e "uma mancha so" foi o resultado da PRIMEIRA foto: com 3,4 o quad de 96 px cabia tres celulas
/// de ruido, e tres celulas suavizadas por quatro oitavas nao tem filamento nenhum pra mostrar.
///
/// E ELE E POR QUAD, ENTAO ELE ACOMPANHOU O ENCOLHIMENTO SOZINHO, e isso e o certo aqui: com o quad
/// em 42 px a mesma 4,4 da celulas de ~9,5 px em vez de ~22. Tinha que ficar mais fino mesmo -- o
/// que se ve da nuvem agora e um anel de 5 px e um veu por cima de um boneco de 32; veio de 22 px
/// nao cabe duas vezes ali dentro, e o que nao se repete nao le como filamento.
uniform float escala : hint_range(0.5, 12.0) = 4.4;

/// A FORCA DO WARP -- ver o cabecalho. Zero devolve FBM puro (fumaca); acima de ~1 o campo se
/// dobra tanto que o filamento vira no.
uniform float torcao : hint_range(0.0, 1.5) = 0.62;

// ============================ ONDE O CAMPO VIRA VEIO ============================
// A faixa de FBM que vira nuvem: abaixo de `x` e vazio, acima de `y` e nuvem cheia. Este par
// substituiu uma `densidade` que MULTIPLICAVA o campo, e a troca foi por causa da primeira foto: o
// FBM tem media ~0,47 e desvio pequeno, entao multiplicar so levantava tudo junto -- saia uma
// mancha CHAPADA e semitransparente, sem um unico veio.
//
// Esticar o contraste faz o contrario: as costas do ruido saturam em 1 e os vales caem a 0, e o
// que sobra no meio sao as PAREDES entre eles -- que sao exatamente os filamentos da referencia.
// E o vazio entre os veios importa tanto quanto os veios: e ele que faz a nuvem parecer nuvem em
// vez de neblina.
// ================================================================================
uniform vec2 corte_do_veio = vec2(0.30, 0.80);

// ============================ A CALIBRAGEM DE OPACIDADE FOI REFEITA CONTRA A FOTO NO JOGO ============================
// Os quatro numeros abaixo (`piso_da_nuvem`, `curva_alfa`, `opacidade`, `atenua_escuro`) foram
// afinados fora do jogo, num projeto descartavel, e os quatro estavam BAIXOS DEMAIS. A primeira foto
// tirada dentro do jogo, sobre a grama da Terra ao meio-dia, mediu o estrago: acender a nuvem mudava
// a cor media do anel em volta do corpo em **+1 no vermelho, 0 no verde, +2 no azul** (de 255). Ou
// seja, o efeito estava LIGADO, no lugar certo, com a cor certa -- e invisivel. Fazendo a conta no
// meio da elipse, o alfa saia em ~10%.
//
// A RAIZ FOI UM DIAGNOSTICO INVERTIDO. O ajuste anterior existia pra matar uma "auréola verde-suja"
// sobre a grama, e a conclusao tirada foi "o indigo suja, entao rebaixe o alfa". E o contrario: a
// mistura suja e a de MEIO TERMO -- 10% de indigo sobre verde nao le como indigo, le como grama
// encardida. Ou o alfa e baixo o bastante pra ninguem ver, ou alto o bastante pra a cor do efeito
// ganhar do fundo. O caminho de fugir do meio termo era pra CIMA, e a referencia concorda: nela a
// nebulosa ENGOLE o cenario em volta do lutador, nao o tinge.
//
// Os quatro andaram juntos porque eles se multiplicam no mesmo alfa (ver o fim do `fragment`); mexer
// num so exigiria um exagero que estragaria outra coisa. Com estes, o mesmo ponto do meio da elipse
// sai em ~47%, e a franja de fora continua caindo a zero pela mascara -- que e quem faz a nuvem nao
// ter borda.
// ================================================================================================================
/// QUANTA NUVEM EXISTE ONDE O RUIDO NAO POE VEIO NENHUM -- ver o bloco no `fragment`. Zero devolve
/// o defeito da segunda foto (a poca de um lado so); 1 apaga os filamentos e vira uma elipse lisa.
uniform float piso_da_nuvem : hint_range(0.0, 1.0) = 0.55;

/// O EXPOENTE DA FRANJA. Acima de 1 ele AFINA a nuvem inteira (era 1,5, e foi metade do sumico);
/// abaixo de 1 ela engorda. Perto de 1 o alfa acompanha a densidade sem torcer.
uniform float curva_alfa : hint_range(0.5, 4.0) = 0.85;

/// Opacidade maxima da nuvem, no miolo. Nao e "brilho": ver o item 3 das armadilhas.
uniform float opacidade : hint_range(0.0, 1.0) = 0.95;

/// QUANTO DA OPACIDADE SOBRA NA PONTA ESCURA DA RAMPA -- ver o bloco no `fragment`. Continua sendo o
/// botao de "quanto o indigo pode sujar um chao claro", so que agora calibrado pra ele APARECER: em
/// 0,45 a ponta escura era a metade invisivel do efeito.
uniform float atenua_escuro : hint_range(0.0, 1.0) = 0.80;

// ============================ A ELIPSE MORREU: AGORA E A SILHUETA, NUM CAMPO DE DISTANCIA ============================
// Aqui havia DUAS ELIPSES CONCENTRICAS (`centro`, `raio`, `raio_do_corpo`) e elas eram a queixa do
// dono, literal:
//
//   "a aura do ultra instinto n ta legal, os pontinhos subindo estao legais, mas a aura em si ta
//    estranha, ela deveria n ser um circulo e sim CONTORNAR O CORPO com esse efeito"
//
// Elipse ajustada a caixa do alfa acerta o TAMANHO e erra a FORMA: ela nao sabe que o cabelo e
// espetado, que o rabo sai pro lado e que entre as pernas ha vazio. A foto do modo chapado mostrava
// um ovo liso com os espetos do cabelo e o rabo pra fora e a barriga do ovo sobrando abaixo dos pes.
//
// O que entra no lugar e UM CANAL SO: `campo_do_corpo` guarda a DISTANCIA COM SINAL ate a silhueta de
// verdade -- a uniao do alfa das camadas VIVAS do boneco (corpo, corpo-da-forma, rabo, roupa,
// cabelo), no quadro que cada uma esta desenhando agora. Quem o constroi e o C#
// (`NebulosaDaForma.ConstruirCampo`), por transformada de distancia de chanfro, um texel por pixel
// de mundo do quad.
//
// A CONVENCAO, e ela e o shader inteiro:
//
//   campo_do_corpo = 0,0   ->  s = -1   o ponto MAIS FUNDO do boneco (o eixo medial da pose)
//   campo_do_corpo = 0,5   ->  s =  0   EM CIMA DA PELE
//   campo_do_corpo = 1,0   ->  s = +1   o fim da nuvem, `NebulosaDaForma.Folga` px depois da pele
//
// Os dois lados tem normalizacoes DIFERENTES de proposito, e e isso que salva o rosto: pra FORA a
// unidade e a folga (5 px, o pedido do dono, igual em toda direcao); pra DENTRO a unidade e a
// espessura do PROPRIO corpo naquela pose. Normalizar o lado de dentro tambem pela folga poria o
// braco inteiro (3 px de largura) e metade do peito dentro da faixa de alfa cheio -- ou seja, o
// personagem apagado, que e justamente o motivo pelo qual a primeira tentativa de por a nuvem na
// frente foi recusada. Com a espessura como unidade, o miolo do peito e do rosto continua valendo
// `s = -1` (veu ralo) e so o braco fino -- que na referencia E aura -- afunda na nuvem.
//
// O PADRAO E PRETO (`hint_default_black`, s = -1 em todo lugar) pra o shader abrir sozinho no editor
// do Godot mostrando o quad cheio. Em jogo ele e SEMPRE reescrito.
//
// `filter_linear`: o campo e um numero continuo (distancia), nao arte de pixel -- amostrado em
// `nearest` a mascara sairia em degraus de 1 px e a rampa de 5 px teria cinco degraus visiveis.
// `repeat_disable` porque a borda do quad ja e `s = +1`: repetir traria o outro lado do corpo.
// ================================================================================================================
uniform sampler2D campo_do_corpo : hint_default_black, filter_linear, repeat_disable;

// ============================ ONDE O CLARO ACABA, EM FOLGAS ============================
// A unidade MUDOU junto com a geometria, e nao da pra fingir que nao: ela era "raios de corpo"
// (1,45 = o claro morre a 45% do raio depois da pele) e raio de corpo nao existe mais -- a silhueta
// nao tem raio, tem contorno. Agora ela e a FOLGA: 0,85 quer dizer que o ciano-claro morre a 85% do
// caminho entre a pele e a ponta da nuvem, ou seja ~4,2 px do boneco.
//
// O numero saiu de refazer a conta velha, e nao de gosto: com elipse de 13x21 em volta de 8x16, a
// borda externa caia entre 1,31 (pra cima) e 1,63 (de lado) raios de corpo, e o claro morria em
// 1,45 -- ou seja em algum lugar entre 65% e 100% da folga, conforme a direcao. A media e ~85%, e
// agora ela vale igual em toda a volta, que e o mesmo ganho que a mascara teve.
//
// Em 1,0 o claro alcanca a ponta da nuvem e o indigo da borda some; perto de 0,1 sobra so o anel
// colado na pele.
// ====================================================================================================
uniform float fim_do_claro : hint_range(0.05, 1.0) = 0.85;

// ============================ E QUANTO DO CLARO CHEGA AO MEIO DO BONECO ============================
// A rampa tinha um PLATÔ: `perto = 1` do centro ate a silhueta. Isso estava certo enquanto a nuvem
// desenhava atras (o platô caia todo tapado pelo sprite e o que se via era so o anel). Por cima, a
// foto no jogo cobrou na hora: o miolo saiu no topo da rampa, ou seja em `cor_perto` (#d8e8ff, quase
// branco), e o personagem virou um borrao branco-acinzentado -- da pra ver que ha um boneco ali, mas
// nao QUEM.
//
// Agora a rampa e um ANEL DE LUZ: ela sobe do miolo ate a silhueta e cai depois dela. No meio do
// peito sai violeta (a `cor_meio`) em vez de branco, que e a leitura da referencia -- o corpo TINGIDO
// pela aura, com o brilho na borda. E o alfa disto tudo continua sendo o `veu_no_corpo`.
//
// Zero devolve indigo quase preto no meio do boneco (escurece demais); 1 devolve o platô branco de
// antes.
// ====================================================================================================
uniform float claro_no_miolo : hint_range(0.0, 1.0) = 0.45;

// ============================ QUANTO DA NUVEM SOBRA POR CIMA DO BONECO ============================
// A nuvem passou a desenhar POR CIMA do sprite (ordem de irmao, ver `NebulosaDaForma`), e no miolo
// dela o alfa e ~0,74: sem este botao o rosto sumiria atras de uma mancha, que e exatamente por que
// a segunda passada tinha sido RECUSADA na primeira versao.
//
// O conserto nao e voltar pra tras, e o ALFA -- palavra do dono ("o efeito deveria ficar sobre o
// corpo", e na referencia da pra ver o rosto do Goku atraves dele). Dentro da silhueta a nuvem vira
// VEU: sai com esta fracao do alfa e sobe pro cheio conforme se aproxima da borda do corpo. Em 0,22
// o rosto sai por baixo de ~0,16 de alfa -- visivel, tingido de violeta, e nao apagado. (Baixou de
// 0,28 depois da primeira foto por cima do boneco: junto com o platô branco da rampa, 0,28 lavava o
// personagem inteiro. As duas correcoes sao a mesma queixa -- ver `claro_no_miolo`.)
//
// Zero devolve a versao "atras do corpo" sem o custo de desenhar la; 1 apaga o personagem.
// ====================================================================================================
uniform float veu_no_corpo : hint_range(0.0, 1.0) = 0.22;

// ============================ LIGA/DESLIGA, E SO NO ALFA ============================
// Escrito pelo C# (`NebulosaDaForma.Definir`). Zero apaga; 1 e a forma acesa. Ver a armadilha 3 no
// cabecalho: ele NAO entra no RGB.
// ====================================================================================
uniform float forca : hint_range(0.0, 1.0) = 0.0;

// ============================ O SEGUNDO PAPEL DA NUVEM: ESTE CORPO ESTA CARREGANDO ============================
// Em Ultra Instinto a nuvem NAO e so o overlay da forma -- ela e tambem a aura de carga, no lugar da
// folha de chama. Ordem do dono: *"a aura/carga do ultra instinto deveria ser essa aura em shaders, e
// nao o icone de carga atual"*.
//
// Entao ela tem DOIS estados no mesmo desenho, e eles precisam se distinguir a olho, senao o jogador
// perde a leitura de "estou carregando" que a chama dava:
//
//   REPOUSO (`carga = 0`)   -- exatamente o desenho de hoje, pixel por pixel. E requisito: o efeito
//                              aprovado nao pode regredir por ganhar um segundo papel.
//   CARGA   (`carga` > 0)   -- a nuvem ADENSA. Escrito pela `CargaVisual`, e o numero que chega aqui e
//                              o MESMO `forca` que pintava a chama (0,70..0,95 carregando; 0,95..1,50
//                              na sobrecarga) -- ou seja ele ja PULSA, e pulsa mais rapido acima dos
//                              100%. A leitura de "esforco em andamento" vem de gracas, e vem igual a
//                              de todo mundo: a mesma onda que a chama de todo mundo usa.
//
// ============================ POR QUE NAO BASTA SUBIR O `forca` ============================
// Era o obvio e esta errado por dois motivos. Um: `forca` e o LIGA/DESLIGA -- zero significa "esta
// forma nao tem nuvem", e reusa-lo faria "acesa forte" e "carregando" virarem o mesmo eixo, sem como
// distinguir um Ultra Instinto parado de um carregando. Dois: ele so multiplica o alfa, e alfa sozinho
// e o eixo mais fraco que ha aqui -- sobre fundo claro ele quase nao le (foi a queixa do "amarronzado
// por cima do verde do chao", ver `atenua_escuro`).
//
// ============================ E POR QUE NENHUM DOS TRES GANHOS MEXE NO TEMPO ============================
// Acelerar a rolagem da nuvem ou a subida dos pontinhos seria a leitura mais forte, e e proibido:
// `t = TIME * velocidade` e `y = fract(... - t * v)` sao FASES. Multiplicar a velocidade num quadro
// qualquer salta a fase inteira -- a nuvem daria um pulo e cada particula TELEPORTARIA pra outra
// altura no instante em que o jogador apertasse o C. Amplitude nao tem esse problema: opacidade,
// densidade e veu podem ir de 0 a 1 no meio do desenho sem descontinuidade nenhuma.
//
// (O tempo continua sendo a leitura -- so que pela ONDA que a `CargaVisual` ja manda, que e amplitude
// modulada e nao fase reescrita.)
// ==========================================================================================================
uniform float carga : hint_range(0.0, 2.0) = 0.0;

// A NUVEM FICA MAIS OPACA. Quanto o alfa sobe no auge da carga -- ver `carga`.
uniform float ganho_da_carga : hint_range(0.0, 1.5) = 0.45;

// ============================ E MAIS POEIRA SOBE ============================
// As microparticulas sao o que o dono aprovou de olho ("os pontinhos subindo estao legais"), entao sao
// elas que carregam a leitura: carregando, MAIS COLUNAS acendem. Isto entra na densidade (quantas
// trilhas existem) e nao no tamanho nem na velocidade delas -- o desenho de cada particula continua
// sendo o mesmo, so ha mais delas.
//
// Uma coluna que acende comeca numa altura sorteada, o que le como particula nova entrando no campo --
// e nao como um salto, que e o que acelerar o relogio daria.
//
// ============================ E O NUMERO SAI DA CONTA, NAO DO GOSTO ============================
// Este ganho nasceu 0,60 e era GANHO ESCRITO E NUNCA DESENHADO -- o defeito que esta casa ja nomeou
// ("uniform escrito nao e pixel desenhado"). A conta: a densidade sai
// `min(pontos_densidade * (1 + carga*g), 1)`, e a `carga` NUNCA vale menos de 0,70 enquanto o C esta
// segurado. Com 0,60 isso da 0,75 x 1,42 = 1,065 -- **saturado desde o primeiro quadro da carga**.
// Consequencia: o pulso nao mexia nos pontinhos, e a sobrecarga desenhava exatamente a mesma
// quantidade deles que a carga comum. Dois estados escritos, um estado desenhado.
//
// Com 0,22 a faixa inteira cabe embaixo do teto e o pulso volta a valer:
//   repouso        0,750     carga 0,70  0,866     carga 0,95  0,907     sobrecarga 1,50  0,998
// ==============================================================================================
uniform float ganho_dos_pontos : hint_range(0.0, 1.5) = 0.22;

// ============================ E A NUVEM FECHA SOBRE O CORPO ============================
// O `veu_no_corpo` (0,22) e o quanto da nuvem sobra POR DENTRO da silhueta -- e o que deixa o rosto
// legivel atras do efeito. Carregando ele sobe pra ca: a energia se fecha em volta de quem a reune, o
// personagem some um pouco dentro do proprio efeito, e e exatamente isso que carregar significa.
//
// O TETO E BAIXO DE PROPOSITO. Este numero ja foi 0,28 e lavava o personagem inteiro (ver
// `veu_no_corpo`); 0,38 no AUGE de um pulso que passa a maior parte do tempo abaixo disso e o quanto
// da pra fechar sem voltar a apagar o rosto -- que e a queixa que este arquivo mais pagou.
// ====================================================================================
uniform float veu_na_carga : hint_range(0.0, 1.0) = 0.38;

// ============================ CADA CORPO TEM A PROPRIA NUVEM ============================
// `TIME` e global: dois personagens em Ultra Instinto lado a lado desenhariam a MESMA nuvem no
// mesmo instante, e o olho pega isso na hora (e o estroboscopio sincronizado que o
// `RaioDaForma.gdshader` descreve, do outro lado do mesmo problema). A semente desloca o campo e o
// relogio de cada corpo.
// ====================================================================================
uniform float semente = 0.0;

// ============================ AS MICROPARTICULAS BRANCAS SUBINDO ============================
// A camada 5 da referencia: pontinhos brancos puros, densidade BAIXA, uns de ~1 px e outros
// RISQUINHOS VERTICAIS curtos (rastro de quem sobe mais rapido). Sobem sempre.
//
// ============================ POR QUE AQUI DENTRO, E NAO NUM `GPUParticles2D` ============================
// O precedente da casa e o `RaiosDaForma`: particula de verdade, com `MODEL_MATRIX` de semente e
// `INSTANCE_CUSTOM.y` de fase. Ele existe porque o raio e GRANDE (um quad por raio, com forma
// propria) e porque ele NASCE E MORRE em rajadas -- coisas que so um ParticleProcessMaterial faz.
//
// Aqui nao e nada disso, e tres medidas dizem por que:
//
//   1. TAMANHO. A particula tem ~1 px. Um `GPUParticles2D` gastaria um quad, um draw call, um
//      material de processo e um passe de VRAM por corpo pra desenhar VINTE pixels. O quad da
//      nebulosa ja esta sendo rasterizado -- estes pixels sao os MESMOS que a nuvem ja pinta.
//   2. VOLUME. O enunciado e "nascem em volta do corpo, DENTRO do volume da nebulosa". Aqui isso e
//      construcao (multiplica-se pela mesma `mascara`, na mesma linha de codigo); com um node
//      separado seria uma caixa de emissao mantida em dia a mao contra `centro`/`raio`/`raio_do_corpo`
//      -- e os tres passaram a ser MEDIDOS do corpo, ou seja mudam de valor a cada quadro de
//      folha diferente. Ela DERIVARIA da mascara, ou
//      seja divergiria dela.
//   3. LIGA/DESLIGA. Um node novo repetiria `Definir`, `Visible`, o nascimento nos dois pontos do
//      `World` e o despacho nos dois caminhos do `Vestir`. Zero disso aqui: e o mesmo `forca`.
//
// E o dono pediu "feito em shaders". A receita do raio nao se perde -- ela e reusada com a peca
// que existe: sem instancias, a SEMENTE passa a ser o indice da coluna (`neb_hash(col)`) e a FASE
// passa a ser o `fract` do relogio vezes a velocidade sorteada daquela coluna. Mesmo desenho,
// mesma descorrelacao, sem o encanamento de particula.
//
// ============================ COLUNAS, E POR QUE O CUSTO E O(1) ============================
// O jeito ingenuo -- percorrer N particulas por pixel -- custa N. Aqui o quad e fatiado em COLUNAS
// verticais e cada pixel so olha a SUA: uma coluna tem no maximo uma particula viva, e a coluna sai
// da parte inteira de `UV.x`. TRES camadas de colunas (contagens que nao sao multiplas umas das
// outras) quebram a leitura de grade. Custo fixo: ~15 hashes por pixel, contra os ~80 que os cinco
// FBM da nuvem ja pagam no mesmo pixel -- ~18% a mais, e so dentro da elipse. (A maioria das
// colunas esta APAGADA e sai da funcao com UM hash: o corte da densidade e a primeira coisa que a
// funcao faz, antes de sortear velocidade, fase ou tamanho.)
//
// ============================ E POR QUE ELES NAO SAO ADITIVOS, APESAR DE BRANCOS ============================
// `blend_add` e do shader inteiro, e a nuvem PRECISA de `blend_mix` (somar quase-preto nao muda
// pixel nenhum, e o indigo sumiria -- ver o `render_mode`). Entao o ponto e composto no mix: RGB
// puxado pra branco e ALFA levado a 1 no miolo dele. Sobre fundo escuro o resultado e identico ao
// aditivo; sobre fundo claro ele e MELHOR -- branco somado a areia clara nao muda nada, branco
// opaco continua sendo um ponto branco.
// ====================================================================================================

/// ============================ A LARGURA DA TRILHA, EM PIXELS ============================
/// Cada trilha carrega NO MAXIMO uma particula viva, entao esta largura E o espacamento medio entre
/// os pontinhos. A camada de baixo divide o quad por ela; as outras duas usam 73% e 53% da contagem
/// que sair (razoes NAO inteiras: com metade e um terco as tres grades coincidiriam e o olho leria
/// colunas).
///
/// ============================ POR QUE PIXEL E NAO CONTAGEM ============================
/// Aqui havia `pontos_colunas = 26`, uma CONTAGEM -- 26 trilhas no quad, quantos pixels o quad
/// tivesse. Funcionava por acidente: 96/26 dava 3,7 px de espacamento. Quando o quad encolheu pra 42
/// pra colar no corpo, as mesmas 26 trilhas passariam a ficar a 1,6 px umas das outras e as tres
/// camadas somariam 58 trilhas num boneco de 32 px de largura -- que e a CHUVA que o enunciado manda
/// evitar, chegando pelo lado que ninguem olha: nao mais pontos, os mesmos pontos mais perto.
///
/// Dado em pixel ele nao depende do tamanho do quad: um corpo de 32 e um Oozaru de 96 tem pontinhos
/// do mesmo tamanho, com o mesmo espacamento, e o segundo simplesmente tem mais deles. E o teto do
/// `min` no corpo da funcao (0,30 da largura da coluna, 1,1 px aqui) deixa de apertar em qualquer
/// tamanho de quad -- ele existia so pra o caso de alguem exagerar na contagem.
/// ==========================================================================
uniform float pontos_largura_da_coluna : hint_range(1.5, 16.0) = 3.7;

/// ============================ O BOTAO DA DENSIDADE ============================
/// A fracao de trilhas ACESAS. E o unico numero a mexer pra ter mais ou menos pontinho, e ele e
/// uniform justamente pra isso.
///
/// A CONTA, e ela tem um fator que eu errei por 3x antes de fotografar:
///   trilhas   = (lado_do_quad / pontos_largura_da_coluna) * 2.26   (2,26 = 1 + 0,73 + 0,53)
///   acesas    = trilhas * pontos_densidade
///   VISIVEIS  = acesas * (area da elipse / area do quad)
///
/// Aquele ultimo fator e o preco da geometria e nao se ve no codigo: a particula nasce em qualquer
/// altura do quad, e so aparece dentro da mascara. Ele MUDOU DE VALOR quando a nuvem veio pro corpo,
/// e nas duas pontas: a elipse passou a ocupar 49% do quad em vez de 60% (era 0,25 na versao velha,
/// porque la o miolo tambem estava apagado -- desenhar atras do boneco tornava inutil todo ponto que
/// subisse pelo meio). Por cima do corpo o miolo VALE, e o ponto que passa na frente do peito e
/// justamente o que a referencia mostra.
///
/// Com os padroes (3,7 px de coluna num quad de 42 e 0,75): 25 trilhas, 19 acesas, ~9 visiveis --
/// que e a densidade medida da folha do DM (`UI_Dots`: 12 pixels acesos por quadro). Em 1,0 sao ~12;
/// e o caminho pra "chuva" (o defeito que o enunciado nomeia) e APERTAR A COLUNA, nao isto.
/// ==============================================================================
uniform float pontos_densidade : hint_range(0.0, 1.0) = 0.75;

/// Alturas de quad por segundo, ANTES do sorteio por trilha. Cada trilha varia entre 0,55x e 1,15x
/// disto, e as que viram risquinho ganham mais 2,2x -- e a variedade de velocidade que o enunciado
/// pede, e e ela que decide quem tem rastro.
uniform float pontos_velocidade : hint_range(0.0, 2.0) = 0.30;

/// A LARGURA do pontinho em PIXELS do quad -- 1,0 e o "~1 px" da referencia, e e tambem a altura da
/// bolinha (a que nao vira risquinho sai quadrada de 1x1). Tem TETO amarrado a largura da coluna
/// (ver o `min` no corpo): sem ele a particula transborda a propria coluna e sai CORTADA em linha
/// reta na fronteira. Pra pontos maiores, suba `pontos_largura_da_coluna` junto.
uniform float pontos_tamanho : hint_range(1.0, 4.0) = 1.0;

/// O COMPRIMENTO MAXIMO do risquinho, EM PIXELS (cada trilha sorteia entre 40% e 100% disto). E o
/// "rastro de quem sobe mais rapido"; igualar a `pontos_tamanho` devolve so bolinhas e some com a
/// segunda silhueta que a referencia pede.
///
/// Cinco pixels e o teto porque o corpo tem 32: um traco de 12 px (o que o padrao anterior, dado em
/// MULTIPLOS do raio, acabava desenhando) e mais de um terco do lutador -- vira risco de giz, e a
/// referencia diz "risquinho CURTO".
uniform float pontos_rastro : hint_range(1.0, 16.0) = 5.0;

/// Que fracao das trilhas acesas vira RISQUINHO em vez de bolinha. As duas silhuetas tem que
/// coexistir; 0 ou 1 devolve uma populacao so.
uniform float pontos_risco : hint_range(0.0, 1.0) = 0.45;

/// Teto de alfa dos pontos. Serve pra apagar a camada inteira sem mexer na nuvem (0 = so nebulosa).
uniform float pontos_brilho : hint_range(0.0, 1.0) = 1.0;

/// O LADO DO QUAD EM PIXELS, escrito pelo C# (`NebulosaDaForma.Modelar`). E ele que traduz o
/// `UV` em PIXEL do quad -- e em pixel que a particula e medida, travada na grade e espacada. Nao e
/// constante aqui pra nao existirem DOIS lados (um no C#, que escala o `Sprite2D`, e outro cravado no
/// shader) que alguem mude so de um lado: os pontos encolheriam sozinhos e ninguem saberia por que.
///
/// O padrao existe so pra o arquivo abrir sozinho no editor do Godot, e acompanha os outros: 42 e o
/// que a silhueta de 16x32 do fallback produz. Em jogo ele e SEMPRE reescrito, e hoje sai 36 num corpo
/// medido de verdade.
uniform float lado_do_quad = 42.0;


float neb_hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float neb_ruido(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(neb_hash(i),                  neb_hash(i + vec2(1.0, 0.0)), f.x),
			   mix(neb_hash(i + vec2(0.0, 1.0)), neb_hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

// QUATRO OITAVAS. A quinta custa uma amostra a mais em CINCO chamadas (o warp usa este FBM cinco
// vezes) e nao aparece num quad deste tamanho -- o detalhe dela cai abaixo de meio pixel.
float neb_fbm(vec2 p) {
	float s = 0.0, a = 0.5;
	for (int i = 0; i < 4; i++) {
		s += a * neb_ruido(p);
		p *= 2.03;   // NAO 2.0: potencia exata de dois alinha as oitavas na grade e cria xadrez
		a *= 0.5;
	}
	return s;
}

// ============================ UMA CAMADA DE MICROPARTICULAS ============================
// Devolve a intensidade (0..1) das particulas desta camada no pixel `uv`. Ver o cabecalho do bloco
// `pontos_*` pra por que isto e uma funcao de coluna e nao um laco por particula.
//
// `camada` e 0 ou 1: ela desloca a grade e entra em TODOS os hashes, entao as duas camadas sorteiam
// velocidades, fases e tamanhos independentes. `t` e o relogio ja com a semente do corpo somada.
// =====================================================================================
// `densidade` VEM DE FORA e nao do uniform: a carga adensa os pontinhos (ver `ganho_dos_pontos`), e
// quem sabe a conta da carga e o `fragment`. Ler o uniform aqui dentro obrigaria a repetir a conta em
// cada uma das tres camadas -- tres chances de a quarta camada nascer com a densidade de repouso.
float neb_pontos(vec2 uv, float camada, float t, float densidade) {
	// A CONTAGEM SAI DO TAMANHO DO QUAD, e nao o contrario -- ver `pontos_largura_da_coluna`. Num
	// quad de 42 px com colunas de 3,7 as tres camadas dao 11, 8 e 6 trilhas: razoes que nao sao
	// multiplas umas das outras, senao as trilhas de uma cairiam exatamente sobre as da outra e a
	// grade voltaria a aparecer.
	float lado = max(lado_do_quad, 1.0);
	float razao = camada < 0.5 ? 1.0 : (camada < 1.5 ? 0.73 : 0.53);
	float n = max(lado / max(pontos_largura_da_coluna, 0.5) * razao, 2.0);
	float cx = uv.x * n + camada * 0.37;
	float col = floor(cx);

	// A SEMENTE E O INDICE DA COLUNA -- e o `MODEL_MATRIX[3].xy` do `RaioDaForma` traduzido pro que
	// existe aqui. O `semente` do corpo entra junto: dois lutadores em Ultra Instinto lado a lado nao
	// podem ter o mesmo pontilhado (o estroboscopio sincronizado de sempre).
	vec2 k = vec2(col, camada * 31.0 + semente * 17.0);

	// O CORTE DA DENSIDADE VEM PRIMEIRO, e e ele que torna o efeito barato: a maioria das colunas
	// esta apagada e sai daqui com um hash so.
	float h1 = neb_hash(k);
	if (h1 > densidade) { return 0.0; }

	float h2 = neb_hash(k + vec2(7.3, 1.9));    // velocidade
	float h3 = neb_hash(k + vec2(2.1, 8.7));    // fase de subida e desvio dentro da coluna
	float h4 = neb_hash(k + vec2(5.9, 3.3));    // bolinha ou risquinho
	float h5 = neb_hash(k + vec2(11.7, 6.1));   // quao comprido e o risquinho

	// AS DUAS SILHUETAS DA REFERENCIA. Elas nao sao dois tamanhos do mesmo bicho: quem vira risco
	// tambem sobe MAIS RAPIDO, porque o rastro E a velocidade desenhada. Correlacionar as duas coisas
	// e o que faz o olho ler "aquele ali esta subindo depressa" em vez de "aquele ali e comprido".
	float risco = step(h4, pontos_risco);
	float v = pontos_velocidade * mix(0.55, 1.15, h2) * mix(1.0, 2.2, risco);

	// `UV.y` CRESCE PRA BAIXO: subtrair o tempo faz a particula subir. Somar faria chuva -- e o
	// enunciado diz tres vezes que elas sobem.
	float y = fract(h3 * 7.13 - t * v);

	// ============================ TRAVADA NA GRADE DE PIXEL, E ISSO E CORRECAO DE FOTO ============================
	// Duas versoes antes desta mediam a distancia continua ate o centro da particula e suavizavam a
	// borda. E o que se faz num efeito de alta resolucao, e aqui deu errado das duas vezes pelo mesmo
	// motivo: a particula tem UM pixel. Se o centro dela cai entre dois pixels, os dois saem pela
	// metade -- e "metade de branco" nao e branco, e cinza. Medi na foto: o pixel mais claro do quad
	// chegava a 148 de 255 na primeira versao e a ~172 na segunda, com UM ponto sozinho em 255.
	//
	// O alvo nao e um blob antialiasado, e um PIXEL: a folha `UI_Dots` do DM e feita de pixels de 1 px
	// acesos, e o jogo inteiro e pixel art de tile 32. Entao o centro da particula e TRAVADO no centro
	// de um pixel do quad e a borda e dura. Cada particula acende N pixels inteiros, brancos puros,
	// sem meio-tom -- que e exatamente o que a referencia mostra.
	//
	// (Borda dura nao cintila: a grade e a do QUAD, que anda junto com o corpo. Nao ha caso em que a
	// particula fique oscilando entre dois pixels parada no lugar.)
	// =========================================================================================================

	// A meia-largura, com o TETO da coluna: 30% da largura dela e a margem entre o desvio mais extremo
	// (0,30 ou 0,70 da coluna) e a fronteira. Sem o teto a particula sai cortada em linha reta ali.
	float meia_larg = min(pontos_tamanho * 0.5, 0.30 / n * lado);
	// O COMPRIMENTO: a bolinha e tao alta quanto larga; o risquinho estica ate `pontos_rastro`.
	float meia_alt = mix(meia_larg, max(meia_larg, pontos_rastro * mix(0.4, 1.0, h5) * 0.5), risco);

	// O centro, em pixel do quad e no centro do pixel (`+ 0.5`).
	float centro_x = floor((col + 0.30 + 0.40 * h3) / n * lado) + 0.5;
	float centro_y = floor(y * lado) + 0.5;

	float dx = uv.x * lado - centro_x;
	// A TRILHA E UM CILINDRO: o topo e a base do quad sao o mesmo lugar pra quem da a volta. Sem este
	// wrap a particula que sai por cima reapareceria embaixo com um SALTO -- e o risquinho que
	// cruzasse a borda sairia partido em dois pedacos, um em cada ponta do quad.
	float dy = uv.y * lado - centro_y;
	dy -= lado * round(dy / lado);

	float aceso = (1.0 - step(meia_larg, abs(dx))) * (1.0 - step(meia_alt, abs(dy)));

	// ============================ O RASTRO APAGA NA CAUDA ============================
	// Um retangulo branco de brilho uniforme le como TRACO. O que faz ler como rastro e a cauda ser
	// mais fraca que a cabeca. A particula SOBE, entao a cabeca e a ponta de CIMA (`dy` negativo) e a
	// cauda e o que ficou pra tras, embaixo -- dai a rampa de 1,0 a 0,45 de cima pra baixo. A bolinha
	// nao tem cauda: o `risco` zera o desconto inteiro.
	// ==============================================================================
	float ao_longo = clamp((dy + meia_alt) / max(2.0 * meia_alt, 1e-4), 0.0, 1.0);
	return aceso * mix(1.0, mix(1.0, 0.45, ao_longo), risco);
}

vec3 neb_rampa(float t) {
	float m = clamp(parada_meio, 0.02, 0.98);
	return t < m
		? mix(cor_borda.rgb, cor_meio.rgb, t / m)
		: mix(cor_meio.rgb, cor_perto.rgb, (t - m) / (1.0 - m));
}

void fragment() {
	// ============================ AS DUAS DISTANCIAS, E O SHADER INTEIRO E ELAS ============================
	// UMA LEITURA DE TEXTURA no lugar de duas divisoes de elipse -- ver o bloco do `campo_do_corpo`. `s` vai de
	// -1 (o ponto mais fundo do boneco) a +1 (o fim da nuvem), com ZERO exatamente em cima da pele.
	//
	// As duas leituras que o resto do arquivo pede sao fatias dele, e as duas continuam significando o
	// que sempre significaram -- por isso nenhuma linha abaixo mudou de forma:
	//
	//   `t_borda`    0 na pele, 1 na ponta da nuvem  ->  a queda MORA NA FOLGA, agora medida
	//                PERPENDICULAR a silhueta e nao ao longo de um raio de elipse. E o pedido literal
	//                do dono ("no MAXIMO 5 pixels de distancia do corpo") virando geometria: com
	//                elipse a folga era 5 px so nos quatro pontos onde a elipse encostava no boneco e
	//                sobrava nas diagonais; com o campo de distancia ela e 5 px em TODA a volta,
	//                inclusive na quina do ombro e entre as pernas.
	//
	//   `dist_corpo` 0 no miolo do boneco, 1,0 em cima da pele -- o MESMO numero de antes, e por isso
	//                o `veu_no_corpo` e o `claro_no_miolo` la embaixo continuam com os mesmos cortes
	//                (0,55 e 0,35) que foram calibrados na foto. O que mudou e de onde ele sai: era a
	//                razao entre dois raios de elipse, e agora e a profundidade real dentro da pose.
	// ====================================================================================================
	float s = texture(campo_do_corpo, UV).r * 2.0 - 1.0;
	float t_borda = max(s, 0.0);
	float dist_corpo = 1.0 + min(s, 0.0);
	float mascara = 1.0 - smoothstep(0.0, 1.0, t_borda);
	if (mascara <= 0.001 || forca <= 0.0) {
		COLOR = vec4(0.0);
	} else {
		// A NUVEM SOBE. `p.y + t` faz uma feicao do campo que estava em y=k aparecer cada vez mais
		// perto de UV.y = 0, que e o TOPO -- energia subindo, e nao escorrendo.
		float t = TIME * velocidade + semente;
		vec2 p = UV * escala + vec2(semente * 3.1, t);

		// ---- DOMAIN WARP EM DOIS PASSOS (ver o cabecalho) ----
		// Os deslocamentos das duas etapas andam em VELOCIDADES diferentes (1x e 0,7x/-0,5x) pelo
		// mesmo motivo que as tres dobras do `RaioDaForma` andam: iguais, o campo inteiro desliza
		// de lado em bloco em vez de se retorcer sobre si mesmo.
		vec2 q = vec2(neb_fbm(p + vec2(0.0, 0.0)),
					  neb_fbm(p + vec2(5.2, 1.3) - t));
		vec2 r = vec2(neb_fbm(p + torcao * q + vec2(1.7, 9.2) + t * 0.7),
					  neb_fbm(p + torcao * q + vec2(8.3, 2.8) - t * 0.5));
		float campo = neb_fbm(p + torcao * r);

		// O CONTRASTE ESTICADO E O QUE FAZ O VEIO -- ver `corte_do_veio`.
		float veio = smoothstep(corte_do_veio.x, corte_do_veio.y, campo);

		// ============================ O RUIDO MODULA A NUVEM, NAO DECIDE SE ELA EXISTE ============================
		// `dens = veio * mascara` (a versao da segunda foto) deixava o FBM ligar e DESLIGAR a nuvem, e
		// a foto mostrou o preco: a primeira oitava tem o comprimento do quad inteiro, entao METADE da
		// elipse ficava acima do corte e a outra metade abaixo. O resultado foi uma poca densa embaixo
		// e a esquerda do boneco e quase nada em cima -- que le como respingo no chao, e o oposto de
		// uma aura que envolve.
		//
		// O `piso_da_nuvem` conserta pela raiz: a elipse SEMPRE tem nuvem, e o que o ruido faz e
		// engrossa-la nos veios. A simetria passa a ser garantia da mascara (que e radial) e nao sorte
		// do ruido, e os filamentos continuam sendo do ruido -- cada um respondendo pelo que sabe.
		// =========================================================================================================
		float dens = mascara * mix(piso_da_nuvem, 1.0, veio);

		// ============================ A RAMPA E RADIAL **E** POR DENSIDADE ============================
		// Densidade sozinha nao serve: ela e ruido, entao o ciano-claro sairia em manchas aleatorias
		// espalhadas pela nuvem inteira -- inclusive na ponta de fora. A referencia e clara em que o
		// claro mora PERTO DO CORPO e o escuro nas pontas, o que e um gradiente RADIAL.
		//
		// Densidade tambem nao pode ficar de fora, senao a rampa vira um circulo liso: sao os veios
		// que sobem e descem dentro dela que fazem a nuvem parecer nuvem. Dai a mistura -- o radial
		// manda, o ruido tempera.
		//
		// ============================ O `perto` COMECA NA SILHUETA, E AGORA ISSO E LITERAL ============================
		// `1 - dist` (a primeira versao) punha o maximo da rampa no centro do quad; a segunda o punha em
		// `borda_do_corpo`, que era um TERCO DO RAIO chutado a olho pra cair perto da pele. As duas
		// mediam o mesmo lugar por aproximacao.
		//
		// Com a silhueta medida nao ha mais o que aproximar: `dist_corpo == 1.0` E a borda do boneco, e
		// e la que o claro fica -- o topo da rampa e um anel colado na silhueta, que e onde a referencia
		// poe o branco. Pra dentro ele cai ate `claro_no_miolo` (ver o cabecalho daquele uniform: o platô
		// branco que havia aqui apagava o rosto), pra fora ele morre em `fim_do_claro`.
		//
		// E AGORA O ANEL SEGUE O CONTORNO. Com elipse, "o topo da rampa colado na silhueta" era uma
		// aproximacao boa nos quatro pontos em que a elipse encostava no boneco e ruim no resto -- na
		// altura do pescoco o branco caia a 4 px da pele, no ar. Com o campo de distancia o anel de luz
		// desenha o CONTORNO: ele acompanha o espeto do cabelo, a ponta do rabo e o vao entre as pernas.
		//
		// A QUEDA MUDOU DE VARIAVEL, e nao e cosmetico: `dist_corpo` satura em 1,0 fora da pele (ver o
		// bloco la em cima), entao mante-lo aqui deixaria o claro CHEIO ate a borda do quad. Quem mede o
		// lado de fora e o `t_borda`, e ele ja esta na unidade em que o `fim_do_claro` passou a falar.
		// ====================================================================================================
		float perto = mix(claro_no_miolo, 1.0, smoothstep(0.35, 1.0, dist_corpo))
					* (1.0 - smoothstep(0.0, max(fim_do_claro, 0.05), t_borda));
		float rampa = clamp(perto * 0.80 + campo * 0.30 - 0.10, 0.0, 1.0);

		// RGB SEMPRE CHEIO. Ver a armadilha 3: quem some e o alfa.
		vec3 rgb = neb_rampa(rampa);

		// ============================ QUANTO MAIS ESCURA A COR, MAIS RALA ELA SAI ============================
		// A terceira foto mostrou o que faltava: sobre GRAMA o anel indigo virava uma auréola verde-suja
		// em volta do boneco -- o "amarronzado por cima do verde do chao" de novo, com outra cor. Sobre
		// fundo escuro a mesma faixa estava perfeita.
		//
		// A diferenca nao e a cor: e que escuro-sobre-escuro le com pouquissimo alfa e escuro-sobre-claro
		// SUJA com o mesmo alfa. Entao o alfa passa a acompanhar a RAMPA -- o indigo das pontas sai a
		// menos da metade da opacidade, o ciano encostado no corpo sai cheio. Nos dois fundos a leitura
		// e a mesma; no claro ele para de pintar o cenario.
		//
		// (E o mesmo raciocinio que matou o halo do `Personagem.gdshader`, so que resolvido em vez de
		// deletado: la nao havia como o halo ser claro, aqui a parte clara e justamente a que fica
		// colada no corpo.)
		// ====================================================================================================
		// O `1.0 + carga * ganho_da_carga` e o primeiro dos tres ganhos da carga (ver `carga`): a nuvem
		// fica mais opaca enquanto o corpo reune energia. Ele multiplica DEPOIS do `atenua_escuro` de
		// proposito -- a ponta indigo continua saindo rala sobre fundo claro, que e a correcao de foto
		// que aquele uniform existe pra guardar; a carga adensa a nuvem, nao suja o cenario.
		float a = pow(dens, curva_alfa) * opacidade * mix(atenua_escuro, 1.0, rampa) * forca
				* (1.0 + carga * ganho_da_carga);

		// ============================ E O BONECO PRECISA APARECER POR BAIXO ============================
		// Esta linha e a metade "camada" do pedido do dono. A nuvem agora desenha POR CIMA do sprite
		// (ordem de irmao, ver `NebulosaDaForma`), e o alfa dela no miolo do quad e ~0,74: sem veu, o
		// rosto sairia atras de uma mancha ciano. Foi por isso que a segunda passada tinha sido
		// recusada quando a nuvem ficava atras -- e o conserto nao e voltar pra tras, e o ALFA.
		//
		// A subida comeca na METADE do corpo e nao na pele: uma troca dura na silhueta desenharia o
		// contorno do boneco em alfa, ou seja um segundo contorno competindo com o do
		// `Personagem.gdshader` (que e quem tem esse trabalho). Assim o veu ADENSA em direcao a borda,
		// que e como a referencia se parece -- o peito da pra ver atraves, o ombro ja e aura.
		// ====================================================================================================
		// O TERCEIRO GANHO DA CARGA entra no PISO do veu e nao na subida: o que muda e quanto da nuvem
		// sobra por dentro da silhueta (0,22 em repouso, ate `veu_na_carga` no auge do pulso), e nao
		// ONDE ela adensa -- o `smoothstep(0.55, 1.0)` continua sendo o mesmo corte calibrado na foto,
		// senao a borda do veu andaria pra dentro do boneco toda vez que alguem apertasse o C.
		//
		// `min(carga, 1.0)`: a sobrecarga manda ate 1,50 e o veu nao pode passar do teto que foi medido
		// como "ainda da pra ver o rosto". O que a sobrecarga faz de diferente e pulsar mais rapido e
		// adensar mais os outros dois eixos, que nao tem teto de legibilidade.
		a *= mix(mix(veu_no_corpo, veu_na_carga, min(carga, 1.0)), 1.0,
				 smoothstep(0.55, 1.0, dist_corpo));

		// ============================ E POR CIMA DE TUDO, AS MICROPARTICULAS ============================
		// Elas tem RELOGIO PROPRIO: nada de reusar o `t` da nuvem, que ja vem multiplicado pela
		// `velocidade` de rolagem (0,05 -- ~20 s por volta). Amarrados, baixar a rolagem da nuvem
		// congelaria os pontinhos, e sao dois ajustes de arte que nao tem por que se prender um ao outro.
		//
		// AS TRES CAMADAS ENTRAM POR `max` E NAO POR SOMA: onde duas particulas se cruzarem, a soma
		// estouraria acima de 1 e o clamp devolveria um ponto DUAS VEZES maior -- o borrao que o
		// enunciado manda evitar. Com `max`, uma passa por tras da outra e as duas continuam de 1 px.
		// ==========================================================================================
		float tp = TIME + semente * 13.0;
		// A DENSIDADE E A MESMA PRAS TRES CAMADAS, e ela sobe com a carga (ver `ganho_dos_pontos`). O
		// teto de 1,0 e o do proprio sorteio: `h1` sai em [0,1), entao densidade 1 ja e "toda coluna
		// acesa" e qualquer numero acima disso seria ganho escrito e nao desenhado.
		float dens_pontos = min(pontos_densidade * (1.0 + carga * ganho_dos_pontos), 1.0);
		float pontos = max(max(neb_pontos(UV, 0.0, tp, dens_pontos),
							   neb_pontos(UV, 1.0, tp, dens_pontos)),
						   neb_pontos(UV, 2.0, tp, dens_pontos));

		// ============================ ONDE ELES PODEM APARECER ============================
		// A MASCARA e literalmente "dentro do volume da nebulosa": eles nascem e sobem no mesmo campo da
		// nuvem, sem borda propria e sem uma caixa de emissao pra manter em dia contra `centro`/`raio`.
		//
		// Mas ela entra DURA pro ponto e MOLE pra nuvem, e isso e correcao de foto:
		//
		//   `pontos *= mascara`, que era o obvio, derrubou o efeito inteiro. A mascara e uma QUEDA, entao
		//   toda particula da faixa de fora saia com metade do brilho: medi no painel escuro e o pixel
		//   mais claro do quad chegava a 148 de 255. Um efeito de "branco puro" desenhado em cinza medio.
		//   A causa e de conceito -- a queda existe pra a NUVEM nao ter borda visivel, que e coisa de
		//   campo continuo; um ponto de 1 px nao se esfuma, ele e ponto ou nao e.
		//
		//   E a chave vira em 0,15 e nao em ZERO. Com o corte no zero, o ponto existia onde a mascara so
		//   nao fosse nula -- e la o alfa da NUVEM ja esta em ~3%, entao saiam riscos brancos sozinhos
		//   sobre a grama nua, abaixo dos pes e acima da cabeca. Le como CHUVA, que e o defeito nomeado no
		//   enunciado, aparecendo pelo lado que eu nao esperava: nao pelo numero de pontos, pelo LUGAR
		//   deles.
		//
		// ============================ E O FADE DENTRO DA SILHUETA SAIU DAQUI ============================
		// Havia um segundo corte, `smoothstep(borda_do_corpo * 0.75, borda_do_corpo * 1.25, dist)`, que
		// APAGAVA todo ponto que subisse pelo meio do boneco. Ele era economia e nao arte: com a nuvem
		// desenhada ATRAS do sprite, o ponto do meio ficava tapado -- densidade paga e nunca vista.
		//
		// Por cima do corpo a premissa dele morreu, e ele passaria a apagar justamente o que a referencia
		// mostra (pontinho subindo NA FRENTE do peito do Goku). Pior: colada no corpo, a silhueta e ~50%
		// da elipse -- o corte velho comeria metade das particulas e o que sobrasse seria um cordao de
		// pontos no contorno, que le como purpurina e nao como energia.
		//
		// Eles nao apagam ninguem por estarem na frente: sao pixels de 1 px, brancos, esparsos -- e a
		// folha `UI_Dots` do DM (colada de 32x32, camada 12) ja desenha exatamente isso por cima do corpo
		// no original. O veu do `veu_no_corpo` e da NUVEM; o ponto sai cheio, que e o que o faz ponto.
		// ====================================================================================================
		//
		// O RUIDO DA NUVEM NAO ENTRA AQUI de proposito: modular o ponto pelo `veio` faria a particula
		// piscar conforme a voluta passasse por baixo dela, e o que se leria e uma nuvem cintilando -- nao
		// uma particula subindo.
		// ==================================================================================
		pontos *= smoothstep(0.15, 0.40, mascara) * pontos_brilho * forca;
		pontos = clamp(pontos, 0.0, 1.0);

		// COMPOSTO NO MIX (ver o cabecalho do bloco `pontos_*` pra por que nao aditivo). No miolo do quad
		// o ponto sai com `pontos = 1`: RGB cheio e alfa 1, ou seja o pixel da particula por cima do que
		// houver atras. Quem sai parcial e so quem esta na transicao acima -- o ponto que ja alcancou a
		// ponta da nuvem e esta saindo dela.
		//
		// ERA `vec3(1.0)` AQUI, e o branco cravado era o que sobrava de "esta nuvem so tem um dono". Ver
		// o cabecalho do `cor_dos_pontos`: no Ultra Instinto o valor e o mesmo de sempre.
		rgb = mix(rgb, cor_dos_pontos.rgb, pontos);
		a = max(a, pontos);

		COLOR = vec4(rgb, clamp(a, 0.0, 1.0));
	}
}
