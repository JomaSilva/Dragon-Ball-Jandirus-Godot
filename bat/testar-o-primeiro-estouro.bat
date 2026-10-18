@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- o PRIMEIRO ESTOURO de ki, cronometrado

REM ===========================================================================
REM  O PRIMEIRO ESTOURO DE KI DO PROCESSO  (--diagestouro)
REM
REM     testar-o-primeiro-estouro.bat
REM
REM  O DEFEITO: o primeiro tiro de ki que acertava travava a tela -- um terco de
REM  segundo com o mesmo quadro. Dali em diante os estouros saiam lisos: o custo
REM  e de UMA VEZ POR PROCESSO, e por isso caia sempre no primeiro.
REM
REM  ONDE O TEMPO IA (medido por esta bancada, cache de shader em disco vazio):
REM     o quadro do primeiro estouro ....... 346 a 390 ms
REM        a poeira do estouro ............. 321 ms  (3 shaders de particula
REM                                                   compilados na hora)
REM        o estouro e o anel de choque .... ~25 ms cada (shader + pipeline)
REM     a primeira bola 54 a 69 ms, o primeiro raio 66 a 72, o primeiro anel
REM     de deflexao 34, a primeira estrela do embate 33
REM
REM  CARREGAR O SHADER NO LOBBY NAO COMPILA NADA: ele e compilado quando nasce o
REM  primeiro material, e a pipeline quando ele e desenhado. O conserto e o
REM  ENSAIO do aquecimento (cada efeito de ki montado e desenhado uma vez, fora
REM  da tela, no lobby) e a poeira segurando os materiais de particula dela.
REM  DEPOIS: o primeiro estouro custa 6 ms (o quadro inteiro, 9).
REM
REM  ---------------------------------------------------------------------------
REM  SAO TRINTA E DUAS RODADAS, e cada uma e um processo: o que se mede so acontece
REM  uma vez por processo.
REM
REM    1) a bola de producao, login de robo. A primeira bola, o primeiro
REM       estouro, o segundo e o terceiro (o controle), o quarto depois de a
REM       poeira sumir e o coletor passar, e o DEFEITO INJETADO na poeira
REM       (`PoeiraDeEstrago.SemSegurarDeTeste`), que a mesma regua reprova.
REM       E TRES LUTAS pra trilha: o primeiro golpe de cada uma poe no ar uma
REM       faixa de combate nova, que ja tem que estar na memoria (ela e lida
REM       numa thread de carga antes da luta) -- e na terceira o DEFEITO INJETADO
REM       na musica (`AudioDirector.SemThreadDeCargaDeTeste`), reprovado
REM       pela mesma regua.
REM    2) a mesma, com quatro segundos de lobby antes de discar: o login de gente.
REM    3) a mesma, de noite: os efeitos passam a ser desenhados com luz em cima.
REM    4) os OUTROS primeiros usos, um por segundo: o anel da deflexao, o raio,
REM       a estrela do embate, o escudo do parry e a faisca do golpe -- e o
REM       PRIMEIRO BORRAO do processo (o rastro de uma investida, que nao e de
REM       ki), com um segundo borrao de controle: o shader dele ja veio do
REM       lobby, nenhuma pipeline nasce no quadro, e ele custa um quadro normal.
REM       E A PRIMEIRA MIRAGEM do processo -- o vulto que o Zanzoken deixa pra
REM       tras, de quem tem a Afterimage --, com uma segunda de controle e a
REM       mesma regua de tres pernas.
REM    5) O CONTRA-EXEMPLO: a rodada 1 com `--semensaio`, o jogo de antes
REM       (`Aquecimento.SemEnsaioDeTeste`). A regua TEM que reprovar a primeira
REM       bola e o primeiro estouro -- e a rodada termina em TUDO OK justamente
REM       porque reprovou: procure as linhas "(defeito injetado: ...) a mesma
REM       regua REPROVA".
REM    6) a rodada 4 com `--semensaio`: o raio, o anel e a estrela reprovados.
REM    7) OS TEMAS QUE ENTRAM FORA DA LUTA: o ESC tres vezes e a estreia de
REM       tres formas. O tema de menu ja tem que estar na memoria quando o
REM       ESC abre (ele e lido numa thread de carga antes da hora), e o de
REM       uma transformacao entra no ar quando a thread de carga o entrega,
REM       sem a tela esperar por ele. O ESC 3 e a terceira estreia saem com o
REM       DEFEITO INJETADO na musica, reprovado pela mesma regua.
REM       E A PRIMEIRA CINEMATICA DO PROCESSO, que roda antes das estreias: o
REM       quadro em que ela NASCE e o do beat que ASSUME a forma (a cratera, a
REM       fumaca, o penteado, o clarao) custam um quadro normal, e o que a cena
REM       usa depois de nascer ja esta na memoria, com dono.
REM       E OS TRES QUADROS DO ENSAIO -- o primeiro anel de choque, os feixes do
REM       chao da primeira cinematica e os primeiros raios de corpo (os dois
REM       ultimos montavam ali uma pipeline do shader do raio cada um): nenhuma
REM       pipeline nasce neles, porque o ensaio do lobby ja desenhou os tres, e
REM       o desenho custa um desenho normal.
REM    8) a rodada 7 com `--semthreaddecarga`: o jogo de antes a rodada
REM       inteira. A regua reprova os seis temas, e a rodada imprime o numero
REM       de antes, faixa a faixa.
REM    9) a rodada 7 com `--semensaiodacena`: o aquecimento sem as folhas de
REM       chama e sem o ensaio da cena (`Aquecimento.SemEnsaioDaCenaDeTeste`).
REM       A regua reprova o quadro em que a primeira cinematica nasce: 41 ms
REM       em vez de 6.
REM   10) a rodada 7 com `--semadiantaracena`: nada do que a cena usa depois de
REM       nascer chega antes da hora (`Aquecimento.SemAdiantarACenaDeTeste`).
REM       A regua reprova o beat que assume (mais de 20 ms em vez de 5), a
REM       espera da thread principal por folha e a conferencia de quem segura
REM       os 16 arquivos da cena.
REM   11) OS SONS DE EFEITO: cada som que os gestos de uma luta tocam (o rasgo
REM       da corrida e da investida, o Zanzoken, a decolagem e o pouso, o Kiai,
REM       o Kaio-ken, o membro arrancado), um som que nenhuma linha do jogo
REM       cita e o ambiente de outro planeta tocam DUAS vezes pela porta de
REM       producao, com uma coleta forcada do .NET no meio. O primeiro toque
REM       dos de gesto nao espera pelo arquivo (ele veio do lobby, numa
REM       thread), depois da coleta os dez continuam na memoria, e o segundo
REM       toque nao volta ao disco.
REM   12) a rodada 11 com `--semsegurarossons`: os sons de efeito sem dono
REM       (`SonsPresos.SemSegurarDeTeste`), o jogo de antes. As tres pernas da
REM       regua reprovam: cada som e lido do disco pela thread principal no
REM       primeiro toque, sai da memoria com a coleta e e lido de novo.
REM   13) OS RAIOS DA FORMA COM O DRIVER DE VIDEO FRIO: a rodada 7 com
REM       `--driverfrio`. Montar uma pipeline custa 1 ms quando o driver de
REM       video ja viu o shader e 16 a 36 quando nunca viu -- e o cache dele
REM       mora fora da pasta desviada (ver o bloco de baixo), entao nas outras
REM       rodadas ele esta quente. Esta salga o `RaioDaForma.gdshader` no lobby
REM       (uma constante que vale zero em todo pixel): o driver recebe um shader
REM       que nunca viu, como na maquina de quem acabou de instalar. A regua dos
REM       dois quadros dos raios tem que PASSAR: nenhuma pipeline nasce neles, e
REM       o desenho deles custa menos de 1 ms.
REM   14) a rodada 13 com `--semensaiodosraios`: o aquecimento sem os dois atos
REM       que desenham um raio de forma no lobby
REM       (`Aquecimento.SemEnsaioDosRaiosDeTeste`), o jogo de antes. A regua
REM       reprova os dois quadros: uma pipeline nasce em cada um, e o desenho
REM       custa 19 a 23 ms em vez de menos de 1.
REM   15) OS TEMAS DE NOITE: a rodada 7 com `--horateste 0.9 --estouronoite`. A
REM       luz de um efeito de ki so nasce com o mundo escuro, e com o login de
REM       robo o ensaio cai nos primeiros quadros do mundo: a estrela do embate
REM       do ensaio acendia o lado ESCURO do palco, e os atos seguintes deixavam
REM       de montar a pipeline do item sem luz. (O clima natural e funcao do
REM       relogio -- um dia do jogo sao 24 minutos -- e a rodada 7 reprovava
REM       sozinha nos dias em que o meio-dia era tempestade.) A regua dos tres
REM       quadros tem que PASSAR: o lado escuro do palco continuou escuro.
REM   16) a rodada 15 com `--estrelacomluz`: a estrela do ensaio com a luz
REM       propria dela (`Aquecimento.EstrelaComLuzDeTeste`), o jogo de antes. A
REM       regua reprova os tres quadros: uma pipeline nasce em cada um.
REM   17) O PRIMEIRO RELAMPAGO COM O DRIVER DE VIDEO FRIO: a rodada 7 com
REM       `--driverfrio Raio`. O risco do raio e um sprite com o `Raio.gdshader`
REM       que nasce invisivel com o mundo, e a pipeline dele so era montada
REM       quando o primeiro raio caia DENTRO da tela -- aqui, o da tempestade da
REM       estreia do `ssj1`, que a rodada espera (ele cai entre 1 e 2 s de cena,
REM       e o que cai fora da tela nao conta: ela espera o seguinte). A regua
REM       tem que PASSAR: nenhuma pipeline nasce no quadro, e o desenho custa
REM       meio milissegundo.
REM   18) a rodada 17 com `--semensaiodorelampago`: o aquecimento sem o ato que
REM       desenha o risco no lobby (`Aquecimento.SemEnsaioDoRelampagoDeTeste`),
REM       o jogo de antes. A regua reprova o quadro: uma pipeline nasce nele, e
REM       o desenho custa 25 a 41 ms.
REM   19) A PRIMEIRA LUZ EM CIMA DE UM CORPO, COM O DRIVER FRIO: a rodada 3 (a
REM       bola de noite) com `--driverfrio Personagem`. O motor desenha o item
REM       que tem luz em cima com OUTRA pipeline do mesmo shader, e a do boneco
REM       so era montada quando a primeira luz alcancava um corpo -- de noite,
REM       a luz de ki do primeiro tiro. A regua tem que PASSAR.
REM   20) a rodada 19 com `--semensaiodocorpo`: o aquecimento sem o boneco no
REM       palco (`Aquecimento.SemEnsaioDoCorpoDeTeste`), o jogo de antes. A
REM       regua reprova o quadro: o desenho custa mais de 100 ms.
REM   21) O CEU: uma tempestade que COMECA no meio do jogo (`--temas --ceu`),
REM       com o driver frio pra chuva e pro raio (`--driverfrio Queda+Raio`).
REM       As outras rodadas de dia cravam o ceu aberto, e nenhuma ve uma chuva
REM       comecar: o emissor dela nasce desligado, o motor nao desenha
REM       particula inativa, e a pipeline do `Queda.gdshader` so era montada no
REM       primeiro pingo. A rodada forca a tempestade no servidor e, dois
REM       segundos depois, faz um raio cair a 3 tiles do corpo. A regua dos
REM       dois quadros tem que PASSAR. (O `--climateste Nublado` segura o ceu
REM       SEM chuva ate a bancada assumir: o clima natural e funcao do relogio,
REM       e num bloco de chuva o mundo ja nasceria chovendo.)
REM   22) a rodada 21 com `--semensaiodachuva --semensaiodorelampago`: o jogo de
REM       antes. A regua reprova os dois quadros: 28 a 38 ms na chuva e 25 a 41
REM       no raio.
REM   23) OS SCRIPTS DE NODE: pro Godot, cada classe C# de node e um recurso de
REM       script, e ele so vive enquanto ha uma instancia da classe. Morta a
REM       ultima, o motor o solta; o `new` seguinte o carrega de novo, pela
REM       thread principal -- 0,5 a 0,9 ms cada (a cinematica, 1,2), e um duelo
REM       de 90 s fazia 16 a 26 dessas: o tiro, a carga do raio, a estrela do
REM       embate, a poeira, ate tres no mesmo quadro. O aquecimento segura o
REM       script de dez classes de efeito desde o lobby. A rodada nasce cada uma
REM       SOZINHA, pelada e pela porta de producao: o script tem dono, nenhuma o
REM       carrega de novo, o `new` custa o de um node, e do lobby ao fim o script
REM       de nenhuma morre. O CONTROLE e um node que nenhuma lista cita: o motor
REM       tem que continuar soltando o script dele -- e a prova de que o
REM       mecanismo ainda existe.
REM   24) a rodada 23 com `--semsegurarosscripts`: o aquecimento sem segurar os
REM       scripts (`Aquecimento.SemSegurarOsScriptsDeTeste`), o jogo de antes. As
REM       quatro pernas da regua reprovam, e a rodada imprime o custo de cada
REM       carga, classe a classe, pelada e pela porta de producao.
REM   25) O PRIMEIRO BORRAO DO PROCESSO, que nao e efeito de ki: a rodada 4 com
REM       `--semensaiodoborrao` -- o aquecimento sem o shader do borrao na fila
REM       de carga do lobby e sem o ato que o desenha la
REM       (`Aquecimento.SemEnsaioDoBorraoDeTeste`), o jogo de antes. A regua
REM       mora na rodada 4; aqui as tres pernas dela reprovam: o shader nao
REM       estava na memoria, uma pipeline nasce no quadro, e ele custa 31 a
REM       40 ms em vez de 6 -- 22 a 32 deles com a tela esperando o shader.
REM   26) O BORRAO COM O DRIVER DE VIDEO FRIO: a rodada 4 com `--driverfrio
REM       Borrao` (o sal das rodadas 13 e 14, agora no shader do borrao). A
REM       regua tem que PASSAR: o ensaio pagou no lobby o shader e as duas
REM       pipelines dele, e o primeiro borrao continua custando 6 ms.
REM   27) a rodada 26 com `--semensaiodoborrao`: reprova, e com a pipeline
REM       montada do zero no quadro da primeira investida -- 59 a 61 ms de
REM       relogio (25 esperando o shader, 24 a 27 montando a pipeline).
REM   28) A PRIMEIRA MIRAGEM DO PROCESSO, o vulto do Zanzoken: a rodada 4 com
REM       `--semensaiodamiragem` -- o aquecimento sem o shader da miragem na
REM       fila de carga do lobby e sem o ato que a desenha la
REM       (`Aquecimento.SemEnsaioDaMiragemDeTeste`), o jogo de antes. A regua
REM       mora na rodada 4; aqui as tres pernas dela reprovam: o shader nao
REM       estava na memoria, uma pipeline nasce no quadro, e ele custa 33 a
REM       35 ms em vez de 3 -- 24 a 27 deles com a tela esperando o shader.
REM   29) A MIRAGEM DEBAIXO DE UMA LUZ, COM O DRIVER DE VIDEO FRIO: a rodada 4
REM       com `--raioscomluz --driverfrio Zanzoken`. O motor desenha o sprite
REM       que tem luz em cima com OUTRA pipeline do mesmo shader, e pro driver
REM       essa e a cara: 68 a 75 ms na primeira vez, contra 24 a 25 da do
REM       sprite sem luz. E a miragem de noite, ao lado de uma aura ou de um
REM       tiro de ki. A regua tem que PASSAR: o ensaio desenhou no lobby os
REM       dois lados, e a primeira miragem continua custando 3 ms.
REM   30) a rodada 29 com `--semensaiodamiragem`: reprova, e com a pipeline
REM       iluminada montada do zero no quadro do primeiro Zanzoken -- 101 a
REM       108 ms de relogio (25 esperando o shader, 68 a 75 na pipeline).
REM   31) OS PRIMEIROS USOS AVULSOS, com o driver de video frio: os seis shaders
REM       que ensaio nenhum desenhava e que rodada nenhuma punha na tela --
REM       a GOTA do transe, o ESTOURO de planeta (a Final Explosion), a
REM       NEBULOSA do Ultra Instinto, a NEVOA de altitude, a tela do EMBATE
REM       (um ZanzoClash de verdade) e o PLANETA visto do espaco. Um gesto
REM       por vez, cada primeiro uso num quadro so dele; depois uma luz
REM       acende em cima do corpo e os do chao se repetem (o motor desenha o
REM       item iluminado com OUTRA pipeline, e essa custava mais que a
REM       primeira: 58 a 79 ms). A regua dos onze quadros tem que PASSAR:
REM       nenhuma pipeline nasce neles, o shader de cada um ja veio do lobby,
REM       e o quadro custa um quadro normal. Precisa de `--mudezteste` (o
REM       embate sob encomenda) e de `--vooteste` (a skill de voo).
REM   32) a rodada 31 com os cinco `--semensaio...` dos avulsos (a gota, o
REM       planeta, a nebulosa, a nevoa, o embate): o aquecimento sem os atos
REM       e sem os shaders na fila, o jogo de antes. A regua reprova os onze:
REM       uma pipeline nasce em cada um, e o quadro do primeiro uso custa de
REM       23 a 82 ms (a gota 52 a 59, o estouro 54 a 57, a nebulosa 74 a 81,
REM       a nevoa 50 a 55, o embate 23 a 31, e o planeta 30 a mais de desenho).
REM
REM  Cada rodada termina sozinha, com
REM       [estouro] ===== TUDO OK =====        ou        ===== N FALHA(S) =====
REM
REM  ---------------------------------------------------------------------------
REM  A PASTA DE USUARIO E DESVIADA, E APAGADA ANTES DE CADA RODADA
REM
REM  Desviada pra a bancada nao escrever na pasta de saves de verdade. E APAGADA
REM  porque o cache de shader do Godot mora nela (`shader_cache`): com ele vazio
REM  a rodada mede o pior caso, o de quem abre o jogo pela primeira vez -- e e o
REM  unico em que o contra-exemplo da rodada 6 reprova com folga. Com o cache
REM  cheio o primeiro estouro sem ensaio custa 40 ms em vez de 380.
REM
REM  E MEIO PIOR CASO, e vale saber: o DRIVER DE VIDEO tem um cache de shader so
REM  dele, fora desta pasta (o da NVIDIA: AppData\Local\NVIDIA\DXCache), que
REM  nenhuma rodada esvazia. Com ele quente, uma pipeline que o ensaio nao montou
REM  custa 1 ms no quadro em que nasce, e nenhum teto de tempo a ve; na maquina
REM  de quem acabou de instalar (ou trocou de driver) sao 16 a 41 ms -- e mais
REM  de 100 no shader do boneco. As rodadas 13 e 14 poem o driver frio de
REM  proposito pro shader dos raios da forma, e as de 17 a 22, a 26, a 27,
REM  a 29, a 30, a 31 e a 32 pro do que cada uma mede (o `--driverfrio` aceita
REM  uma lista: nomes de arquivo da pasta dos shaders, separados por `+`).
REM  E DOIS GODOTS COM JANELA AO MESMO TEMPO nao medem a mesma coisa: o driver da
REM  um arquivo de cache a cada processo simultaneo, e o segundo cai num arquivo
REM  que nao viu o que o primeiro viu.
REM
REM  PRECISA DE JANELA (no headless nao ha desenho, e a travada nao existe). Ela
REM  nasce NO SEGUNDO MONITOR (--position 1920,0), muda, e sem roubar o teclado.
REM
REM  PORTA PROPRIA (7958): se aparecer "FALHOU ao abrir a porta", ha outra rodada
REM  viva -- feche-a.
REM ===========================================================================

cd /d "%~dp0"

if not "%GODOT%"=="" goto :temgodot

set "GODOT=E:\Users\Joao\Desktop\Godot_v4.7-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
if exist "%GODOT%" goto :temgodot

for /d %%D in ("..\Godot_*" "..\..\Godot_*") do (
    for %%F in ("%%~fD\*console.exe") do (
        set "GODOT=%%~fF"
        goto :temgodot
    )
)

echo.
echo  Nao encontrei o Godot.
echo     set GODOT=C:\caminho\Godot_v4.7.1-stable_mono_win64_console.exe
echo     testar-o-primeiro-estouro.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7958

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    REM `-t:Rebuild` e OBRIGATORIO: o build incremental ja deu "compilacao com
    REM exito" sem trocar a DLL, e o Godot subiu com o binario de ontem.
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

REM ---- o desvio da pasta de usuario, e ele vem ANTES do Godot ----
REM A PASTA DE RASCUNHO TEM NOME PROPRIO (`RASCUNHO`) e e por ELE que ela e apagada
REM a cada rodada, nunca por %APPDATA%: se um dia a linha do desvio sumir daqui,
REM o `rmdir` la embaixo nao pode cair na pasta de usuario de verdade.
set "RASCUNHO=%TEMP%\jandirus-bancada-estouro"
set "APPDATA=%RASCUNHO%"
echo.
echo  Saves desviados para: %APPDATA%
echo  (a pasta real do dono nao e tocada; esta e apagada antes de cada rodada)

set COMUM=--path . --position 1920,0 --audio-driver Dummy --semfoco --host --rede 7958 --campoteste 8 --diagestouro --raca Human --conta bancada_estouro --nome Estouro

call :rodada "1/32: a bola de producao, login de robo" --horateste 0.5
call :rodada "2/32: a mesma, com 4 s de lobby (o login de gente)" --horateste 0.5 --lobbyteste 4
call :rodada "3/32: a mesma, de NOITE" --horateste 0.9 --estouronoite
call :rodada "4/32: os outros primeiros usos de ki, o primeiro borrao e a primeira miragem" --horateste 0.5 --pecas
call :rodada "5/32: O CONTRA-EXEMPLO da 1 (--semensaio: a regua tem que REPROVAR)" --horateste 0.5 --semensaio
call :rodada "6/32: o contra-exemplo da 4 (--semensaio)" --horateste 0.5 --pecas --semensaio
call :rodada "7/32: os temas fora da luta, a primeira cinematica e os raios da forma (o ESC e a estreia de uma forma)" --horateste 0.5 --temas
call :rodada "8/32: o contra-exemplo da 7 (--semthreaddecarga: a regua tem que REPROVAR os temas)" --horateste 0.5 --temas --semthreaddecarga
call :rodada "9/32: o contra-exemplo da 7 (--semensaiodacena: a regua tem que REPROVAR o nascimento da primeira cinematica)" --horateste 0.5 --temas --semensaiodacena
call :rodada "10/32: o contra-exemplo da 7 (--semadiantaracena: a regua tem que REPROVAR o beat que assume)" --horateste 0.5 --temas --semadiantaracena
call :rodada "11/32: os sons de efeito (cada som de gesto duas vezes, com uma coleta do .NET no meio)" --horateste 0.5 --sons
call :rodada "12/32: o contra-exemplo da 11 (--semsegurarossons: a regua tem que REPROVAR os sons sem dono)" --horateste 0.5 --sons --semsegurarossons
call :rodada "13/32: os raios da forma com o DRIVER DE VIDEO FRIO (--driverfrio: a regua dos tres quadros tem que PASSAR)" --horateste 0.5 --temas --driverfrio
call :rodada "14/32: o contra-exemplo da 13 (--semensaiodosraios: a regua tem que REPROVAR os dois quadros dos raios da forma)" --horateste 0.5 --temas --driverfrio --semensaiodosraios
call :rodada "15/32: os temas de NOITE (o login de robo num mundo escuro: a regua dos tres quadros tem que PASSAR)" --horateste 0.9 --estouronoite --temas
call :rodada "16/32: o contra-exemplo da 15 (--estrelacomluz: a regua tem que REPROVAR os tres quadros)" --horateste 0.9 --estouronoite --temas --estrelacomluz
call :rodada "17/32: o primeiro relampago com o DRIVER DE VIDEO FRIO (--driverfrio Raio: a regua do quadro dele tem que PASSAR)" --horateste 0.5 --temas --driverfrio Raio
call :rodada "18/32: o contra-exemplo da 17 (--semensaiodorelampago: a regua tem que REPROVAR o primeiro relampago)" --horateste 0.5 --temas --driverfrio Raio --semensaiodorelampago
call :rodada "19/32: a primeira luz em cima de um corpo com o DRIVER FRIO (a bola de NOITE com --driverfrio Personagem: tem que PASSAR)" --horateste 0.9 --estouronoite --driverfrio Personagem
call :rodada "20/32: o contra-exemplo da 19 (--semensaiodocorpo: a regua tem que REPROVAR o primeiro corpo iluminado)" --horateste 0.9 --estouronoite --driverfrio Personagem --semensaiodocorpo
call :rodada "21/32: o CEU, uma tempestade que comeca no meio do jogo (a primeira chuva e o primeiro raio, driver frio: tem que PASSAR)" --horateste 0.5 --temas --ceu --climateste Nublado --driverfrio Queda+Raio
call :rodada "22/32: o contra-exemplo da 21 (--semensaiodachuva --semensaiodorelampago: a regua tem que REPROVAR os dois)" --horateste 0.5 --temas --ceu --climateste Nublado --driverfrio Queda+Raio --semensaiodachuva --semensaiodorelampago
call :rodada "23/32: os scripts de node (cada classe de efeito nasce sozinha: o script dela tem dono, e ninguem o carrega de novo)" --horateste 0.5 --scripts
call :rodada "24/32: o contra-exemplo da 23 (--semsegurarosscripts: a regua tem que REPROVAR os scripts sem dono)" --horateste 0.5 --scripts --semsegurarosscripts
call :rodada "25/32: o contra-exemplo da 4 (--semensaiodoborrao: a regua tem que REPROVAR o primeiro borrao)" --horateste 0.5 --pecas --semensaiodoborrao
call :rodada "26/32: o primeiro borrao com o DRIVER DE VIDEO FRIO (--driverfrio Borrao: a regua tem que PASSAR)" --horateste 0.5 --pecas --driverfrio Borrao
call :rodada "27/32: o contra-exemplo da 26 (--semensaiodoborrao: a regua tem que REPROVAR, com a pipeline montada do zero)" --horateste 0.5 --pecas --driverfrio Borrao --semensaiodoborrao
call :rodada "28/32: o contra-exemplo da 4 (--semensaiodamiragem: a regua tem que REPROVAR a primeira miragem)" --horateste 0.5 --pecas --semensaiodamiragem
call :rodada "29/32: a primeira miragem DEBAIXO DE UMA LUZ, com o DRIVER DE VIDEO FRIO (--raioscomluz --driverfrio Zanzoken: a regua tem que PASSAR)" --horateste 0.5 --pecas --raioscomluz --driverfrio Zanzoken
call :rodada "30/32: o contra-exemplo da 29 (--semensaiodamiragem: a regua tem que REPROVAR, com a pipeline iluminada montada do zero)" --horateste 0.5 --pecas --raioscomluz --driverfrio Zanzoken --semensaiodamiragem
call :rodada "31/32: os primeiros usos AVULSOS com o DRIVER DE VIDEO FRIO (a gota, o estouro de planeta, a nebulosa, a nevoa, o embate e o planeta: a regua dos onze quadros tem que PASSAR)" --horateste 0.5 --avulsos --mudezteste --vooteste --driverfrio Gota+EstouroDePlaneta+NebulosaDaForma+Altitude+Embate+PlanetaMorrendo
call :rodada "32/32: o contra-exemplo da 31 (os cinco --semensaio... dos avulsos: a regua tem que REPROVAR os onze quadros)" --horateste 0.5 --avulsos --mudezteste --vooteste --driverfrio Gota+EstouroDePlaneta+NebulosaDaForma+Altitude+Embate+PlanetaMorrendo --semensaiodagota --semensaiodoplaneta --semensaiodanebulosa --semensaiodanevoa --semensaiodoembate

echo.
echo  Encerrado. Cada rodada tem que terminar em  [estouro] ===== TUDO OK =====
echo  -- inclusive a 5, a 6, a 8, a 9, a 10, a 12, a 14, a 16, a 18, a 20, a 22, a 24, a 25, a 27, a 28, a 30 e a 32, que passam PORQUE a regua reprovou o defeito.
pause
exit /b 0

:rodada
echo.
echo  ---- %~1 ----
if exist "%RASCUNHO%" rmdir /s /q "%RASCUNHO%"
mkdir "%RASCUNHO%"
shift
set EXTRA=
:junta
if "%~1"=="" goto :solta
set EXTRA=!EXTRA! %1
shift
goto :junta
:solta
"%GODOT%" %COMUM% !EXTRA!
exit /b 0
