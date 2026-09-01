@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada da AGONIA DE UM PLANETA

REM ===========================================================================
REM  A AGONIA DE UM PLANETA  (--diagagonia)
REM
REM      ver-um-planeta-morrer.bat                  (Terra morrendo, Namek de controle)
REM      ver-um-planeta-morrer.bat Namek Vegeta     (outra vitima, outro controle)
REM
REM  O pedido do dono, literal:
REM     "quem ta vendo do espaco o planeta deveria ficar com uns efeitos (pode
REM      ser via shaders) na mesma ideia dos ferimentos procedurais nos
REM      personagens, so q seria um efeito meio avermelhado a lembra magma, e
REM      rachaduras no planeta, q vai se intensificando durante esses 5 minutos,
REM      ate acontecer uma mega explosao (via shaders e bem bonita de se ver) e
REM      assim o planeta some"
REM
REM  E o pedido dos DESTROCOS, tambem literal:
REM     "quando um planeta explodir e ele tiver a explosao, vao haver um clarao
REM      logo onde ele estava, ele vai sumir do espaco pra todos os jogadores
REM      (server sync) e onde ficava o planeta vao ter uns asteroides/rochas q
REM      vao girar lentamente e se afastar de onde era o planeta pra representar
REM      os pedacos do planeta. o icone usado e Asteroid5112013.png. dps de um
REM      tempo eles despawnam pro servidor n ter q ficar gastando tempo de tick
REM      pra ver a posicao de asteroides"
REM
REM  O SERVIDOR NAO GUARDA POSICAO DE ASTEROIDE NENHUMA: a posicao de cada caco e
REM  funcao pura de (semente do planeta, indice, tempo desde o estouro), entao o
REM  custo por quadro la e ZERO e duas telas veem os mesmos cacos sem um byte no
REM  fio. O lado do servidor disso e provado na `--planetateste` (PROVA 10).
REM
REM  ---- POR QUE ELA PRECISA DE JANELA ----
REM  Porque TODAS as perguntas dela sao sobre PIXEL. Este projeto tem quatro
REM  defeitos visuais registrados que passaram por quatro mil checagens verdes
REM  porque a bancada media INTENCAO -- `SetShaderParameter` devolve void, nunca
REM  falha, e continua devolvendo void com o shader inteiro sem compilar.
REM  No headless ela diz que nao mediu, em vez de passar de graca.
REM
REM  E os dois defeitos que ESTA bancada pegou, os dois so pela FOTO:
REM     * no auge da agonia o planeta virava um disco de ruido AMARELO, sem uma
REM       rachadura reconhecivel -- repintado, e nao rachado. Todas as checagens
REM       numericas estavam verdes ("o disco mudou", "ele avermelhou");
REM     * a mega explosao desenhava ABAIXO do proprio planeta: o `ZIndex` de um
REM       filho e RELATIVO ao do pai, e o pai e -60. As tres checagens de codigo
REM       passaram (o node existe, o material existe, o tween anda).
REM
REM  ---- AS DEZENOVE FAMILIAS ----
REM     1) O CONTROLE          os DOIS discos VIVOS, limpos, fotografados, e a
REM                            regiao de amostragem MEDIDA no alfa deles. Ela NAO
REM                            nasce dentro do estado que testa
REM     2) O PIXEL MUDA        a agonia no auge contra o controle
REM     3) A RAMPA             treze degraus: sobe, CHEGA LONGE, nao pula, comeca
REM                            no piso do Core e avermelha por RAZAO entre canais
REM     4) O ESTOURO           antes do prazo NAO estourou; no prazo estourou; e
REM                            a explosao acende a tela sem virar tela cheia
REM    11) O CLARAO (D1)       o miolo de onde o planeta estava acende MUITO mais
REM                            do que o planeta era, em branco-QUENTE, e o outro
REM                            planeta da tela nao clareia
REM     5) O MUNDO SOME        o node se recolhe E o pixel confirma: onde a vitima
REM                            estava nao ha mais CORPO -- so fundo e cacos
REM     9) AS DUAS TIRAS       a da VITIMA e a do CONTROLE, em arquivos separados
REM    12) OS DESTROCOS (D4)   o campo nasce no estouro, a arte Asteroid5112013
REM                            resolve, o teto duro morde, o giro leva 5 a 11 s
REM                            por volta e cada caco comeca num quadro diferente
REM    13) O AFASTAMENTO (D3)  todos os cacos se afastam, DESACELERANDO, duas
REM                            telas com a mesma semente veem os MESMOS cacos, e
REM                            atravessar uma fronteira de chunk nao muda o campo
REM    14) O CACO NO PIXEL     onde o node diz que ha pedra, a TELA mudou
REM    15) A JANELA (D5)       o campo desbota antes de sumir e se recolhe sozinho
REM                            no fim do minuto; e sem o planeta no registro do
REM                            servidor nao ha rescaldo nenhum (D2)
REM     6) AS PEDRAS           a agonia levanta pedra do chao, a densidade segue
REM                            a rampa e o custo nao acompanha o mapa
REM     7) O DETERMINISMO      duas telas com a MESMA semente veem as MESMAS
REM                            pedras nas MESMAS celulas
REM     8) O CONTRA-EXEMPLO    o planeta de controle, VIVO no mesmo quadro, nao
REM                            avermelha um pixel
REM    10) A PEDRA NO PIXEL    onde o node diz que ha pedra, a TELA mudou
REM
REM  ---- AS QUATRO QUE OLHAM O RESCALDO NO PIXEL (16 a 19) ----
REM  As familias 12 a 14 medem NODES: quantos existem, onde o node diz que estao,
REM  se duas telas concordam. As quatro abaixo perguntam a mesma coisa a TELA, e
REM  cada pedido do dono sozinho -- porque eles reprovam por motivos diferentes:
REM
REM    16) EXISTEM            contagem de MANCHAS do tamanho de um caco, sem
REM                           perguntar ao node onde olhar. E a metade que
REM                           derruba: com o planeta VIVO, e com o campo montado
REM                           mas o relogio ainda fechado, sao ZERO manchas
REM    17) GIRAM              a MESMA pedra, no MESMO lugar (o relogio fica
REM                           cravado de proposito), com silhueta diferente
REM                           depois que a folha andou. E o controle: dois
REM                           quadros colados dao silhueta IDENTICA
REM    18) SE AFASTAM         a distancia media das manchas ao ponto, em TRES
REM                           instantes. Dois pontos provam deslocamento;
REM                           afastamento e tendencia, e tendencia pede tres
REM    19) A ARTE NAO TROCA   *a pergunta do dono*, medida no pixel: nos cinco
REM                           instantes da tira, o disco continua parecendo com
REM                           ELE MESMO e nao com o outro planeta. E a mesma
REM                           regua, apontada pro vizinho, aponta pro vizinho
REM
REM  ---- TROCAR A VITIMA E O CONTROLE ----
REM     ver-um-planeta-morrer.bat Namek Vegeta
REM
REM  Os dois eram `const string` no fonte. Quando o dono perguntou "o planeta
REM  troca o icone pra terra durante a explosao?", responder com NAMEK morrendo
REM  exigiu COPIAR o projeto inteiro (1,2 GB) pra um rascunho e trocar duas
REM  constantes na copia. Com as bandeiras a mesma resposta leva 40 segundos.
REM
REM  ---- AS DUAS TIRAS, E POR QUE ELAS SAO DUAS ----
REM  A tira antiga tinha SEIS quadros numerados numa fileira so: o 0 era o
REM  planeta de CONTROLE (outro planeta, vivo) e os 1 a 5 eram a vitima. A
REM  explicacao disso morava so no console. O dono abriu o arquivo, leu a fileira
REM  como uma sequencia -- que e como qualquer um le uma tira -- e perguntou se o
REM  planeta trocava de icone no meio da explosao.
REM
REM  Nao trocava. Mas a prova provou errado, o que e pior que prova nenhuma.
REM  Agora sao DUAS, e cada quadro carrega o NOME do planeta escrito:
REM     agonia-tira-do-espaco.png ... so a VITIMA, cinco instantes
REM     agonia-tira-controle.png .... so o CONTROLE, nos MESMOS cinco instantes
REM  e ha uma terceira pro rescaldo:
REM     agonia-tira-dos-destrocos.png  os cacos a +2s, +10s, +30s e +55s
REM
REM  ---- O DEFEITO INJETAVEL ----
REM     ver-um-planeta-morrer.bat   -> tem que fechar 74 OK, 0 FALHA
REM     (o mesmo) com --agoniachata -> tem que fechar VERMELHO (4 falhas)
REM  A bandeira faz a rampa nao andar: o planeta fica igual do comeco ao fim.
REM  Ela existe porque a checagem "a rampa nunca desce" fica VERDE numa rampa
REM  chata, e um crivo que nunca corta e indistinguivel de crivo nenhum.
REM
REM  As FOTOS e as TIRAS saem na pasta de dados do jogo, em agonia-*.png.
REM  O veredito e o numero; a foto e o que deixa alguem discordar dele.
REM
REM  ---- O QUE ELA **NAO** PODE PROVAR, E ONDE ISSO E PROVADO ----
REM  Ela tem UM processo. O "pra TODOS os jogadores (server sync)" do dono e uma
REM  afirmacao sobre PROCESSOS, e as "duas telas" daqui sao dois nodes na mesma
REM  memoria, com a mesma DLL e uma lista de mortos que ela mesma escreveu.
REM
REM  Isso foi MEDIDO e nao suposto: trocando a mistura da semente por um
REM  `GetHashCode()` (estavel dentro do processo, diferente entre processos),
REM  esta bancada fechou 74 OK e 0 FALHA -- cega -- enquanto a
REM  `testar-destrocos.bat`, com dois clientes de verdade, apontou a primeira
REM  pedra a 46 px de distancia entre um cliente e o outro.
REM
REM  SEM REDE E SEM SERVIDOR: o que ela toca sao dois `PlanetaDesenhado`, um
REM  campo de destrocos, um `GameClient` sem conexao (so pra a conversao
REM  "faltam -> intensidade" ser a de producao) e o quadro desenhado. Ela nao
REM  escreve nada no mundo do dono.
REM
REM  A JANELA ABRE NO SEGUNDO MONITOR (--position 1920,0).
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
echo     ver-um-planeta-morrer.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    REM `-t:Rebuild` e OBRIGATORIO: o build incremental ja deu "compilacao com
    REM exito" sem trocar a DLL, e o Godot subiu com o binario de ontem. Uma
    REM bancada medindo a versao anterior e pior que bancada nenhuma.
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

set VITIMA=%1
set CONTROLE=%2
set BANDEIRAS=
if not "%VITIMA%"=="" set BANDEIRAS=--agoniavitima %VITIMA%
if not "%CONTROLE%"=="" set BANDEIRAS=%BANDEIRAS% --agoniacontrole %CONTROLE%

echo.
echo  ---- o mundo rachando, o magma subindo, a mega explosao e os destrocos ----
echo.
echo   Procure o placar:  ===== FIM: N OK, 0 FALHA(S) =====
echo   E OLHE as tiras:
echo     agonia-tira-do-espaco.png     so a VITIMA, cinco instantes
echo     agonia-tira-controle.png      so o CONTROLE, nos mesmos instantes
echo     agonia-tira-dos-destrocos.png os cacos a +2s, +10s, +30s e +55s
echo.
"%GODOT%" --path . --diagagonia %BANDEIRAS% --position 1920,0 --resolution 1280x720

echo.
echo  Encerrado.
pause
