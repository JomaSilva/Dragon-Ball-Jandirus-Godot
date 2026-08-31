@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- A TELA DE CARREGAMENTO DA ENTRADA NO MUNDO

REM ===========================================================================
REM  O PEDIDO DO DONO
REM
REM     testar-a-tela-de-carregamento.bat
REM
REM  "quando terminar de criar o personagem aparecer uma tela de loading ate o
REM   personagem spawnar etc, pq fica uns 1 a 2 segundos na tela azul do byond
REM   ate carregar pela primeira vez"
REM
REM  A "tela azul do byond" era o `ColorRect` de `Tema.Fundo` (#12141c) que o
REM  lobby deixa vivo por baixo de tudo: a criacao se esconde sozinha e nao
REM  havia nada por cima.
REM
REM  ENTREGA EM DUAS METADES, e a segunda e a que importa mais:
REM
REM    1. A TELA. A `TelaDeCarregamento` ja existia, mas so a troca de MAPA a
REM       usava -- e ela nascia 32 linhas DEPOIS do mundo que deveria cobrir.
REM       Agora ela nasce no lobby, sobe no clique e so sai quando um quadro com
REM       o mundo foi DESENHADO (`frame_post_draw`), nunca por relogio.
REM
REM    2. O CONSERTO. Cobrir 1,5 s com uma tela e maquiagem se o 1,5 s tinha
REM       cura. Tinha: o `tileset.tres` (163 atlas), os shaders do mundo, as
REM       chapas do HUD e as folhas de aparencia dos NPC eram trabalho de UMA
REM       VEZ POR PROCESSO pago na cara do jogador. Passaram a ser carregados
REM       numa thread de fundo enquanto ele esta no lobby. Ver `Aquecimento`.
REM
REM       O numero limpo desse conserto sai na FAMILIA 1, e nao no total (o
REM       total depende do humor do servidor e balanca centenas de ms):
REM           pegar o tileset com aquecimento .... 0,02 ms
REM           pegar o tileset sem aquecimento ... ~420 ms
REM
REM  O QUE A BANCADA MEDE (`--diagcarga`), e ela mede em PIXEL:
REM    * TODO quadro entre o clique e o corpo na tela -- um por quadro
REM      DESENHADO, com o numero do quadro junto, de modo que "nenhum quadro
REM      escapou" e uma conta e nao uma promessa;
REM    * "nenhum quadro chapado" -- o pedido do dono, literal;
REM    * "a cobertura ja estava no PRIMEIRO quadro depois do clique" -- a borda
REM      de ENTRADA;
REM    * "no primeiro quadro SEM cobertura o corpo ja esta desenhado" -- a borda
REM      de SAIDA, medida no PIXEL DO CORPO: a bancada esconde e mostra o corpo
REM      em dois quadros seguidos, e os pixels que mudam SAO o corpo;
REM    * as duas series de tempo de QUADRO (CPU e relogio de parede), porque um
REM      total que encolhe pode ser trabalho remanejado em vez de evitado;
REM    * e o mesmo pro LOGIN de personagem que ja existe.
REM
REM  AS RODADAS DE INJECAO SAO PARTE DO TESTE:
REM    `--semcobertura`     desliga a cobertura -> as MESMAS amostras tem que
REM                         achar tela vazia, e a rodada TEM que ficar vermelha.
REM    `--semaquecimento`   desliga o pre-carregamento -> e o ANTES, no mesmo
REM                         binario.
REM    `--quedanomeio`      entra com o servidor MORTO -> prova que a cobertura
REM                         NAO fica presa (jogador trancado numa tela de
REM                         carregamento e pior que 2 s de tela vazia).
REM
REM  A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`): sem janela o
REM  `GetImage` volta vazio e as familias de pixel dizem que nao mediram.
REM
REM  SERVIDOR DEDICADO NUMA PORTA PROPRIA (`--port`/`--rede 7801`), e de
REM  proposito duas vezes: hospedar poria o servidor dentro do processo
REM  cronometrado (e o "Desconectar" derrubaria a partida junto, sem pra onde
REM  relogar), e a porta propria deixa esta bancada rodar ao lado de outro
REM  servidor na mesma maquina sem que os dois se misturem em silencio.
REM ===========================================================================

cd /d "%~dp0"

set "REDE=7801"

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
echo     testar-a-tela-de-carregamento.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : %REDE%

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem, que e
        echo  exatamente como um conserto "que nao faz nada" ja passou verde aqui.
        pause
        exit /b 1
    )
)

echo.
echo  ---- subindo o servidor dedicado ----
start "Jandirus -- servidor da bancada de carga" /min "%GODOT%" --headless --path . --server --port %REDE%
echo  esperando o servidor povoar o mundo...
timeout /t 14 /nobreak >nul

echo.
echo  ---- a entrada no mundo, quadro a quadro (criacao E login) ----
"%GODOT%" --path . --diagcarga --rede %REDE% --position 1920,0 --resolution 1280x720

echo.
echo  ---- INJECAO 1: sem a cobertura (o defeito de origem de volta) ----
echo  As contas de pixel TEM que ficar VERMELHAS aqui, e a bancada diz isso
echo  na propria linha de fecho. Verde nesta rodada = bancada que nao mede nada.
"%GODOT%" --path . --diagcarga --semcobertura --rede %REDE% --position 1920,0 --resolution 1280x720

echo.
echo  ---- INJECAO 2: sem o aquecimento (o ANTES do cronometro) ----
echo  Compare a linha "pegar o tileset agora e de graca" desta rodada com a da
echo  primeira: e o mesmo binario, so a fila de pre-carregamento muda.
"%GODOT%" --path . --diagcarga --semaquecimento --rede %REDE% --position 1920,0 --resolution 1280x720

echo.
echo  ---- A QUEDA DO SERVIDOR NO MEIO DA ESPERA ----
echo  O cliente para na selecao esperando um arquivo. Quem mata o servidor e
echo  este .bat, e SO DEPOIS de matar ele cria o arquivo -- assim o clique cai
echo  com o servidor comprovadamente morto. Um relogio ("espere 12 s") deixaria
echo  o clique cair com ele vivo e a rodada ficaria verde sem testar nada.
set "SINAL=%TEMP%\jandirus-servidor-morreu.flag"
del "%SINAL%" >nul 2>nul
set "LOGQ=%TEMP%\jandirus-queda.log"
start "Jandirus -- queda" /min cmd /c ""%GODOT%" --path . --diagcarga --quedanomeio --marca queda --sinal "%SINAL%" --rede %REDE% --position 1920,0 --resolution 1280x720 > "%LOGQ%" 2>&1"

set /a ESPERA=0
:esperaselecao
timeout /t 1 /nobreak >nul
findstr /c:"Esperando o aviso" "%LOGQ%" >nul 2>nul && goto :matar
set /a ESPERA+=1
if %ESPERA% lss 120 goto :esperaselecao
echo  O cliente nunca chegou na selecao -- a queda nao foi medida.
goto :fim

:matar
echo  O cliente esta na selecao. Matando o servidor AGORA.
taskkill /fi "WINDOWTITLE eq Jandirus -- servidor da bancada de carga*" /t /f >nul 2>nul
timeout /t 2 /nobreak >nul
echo morreu > "%SINAL%"
echo  Esperando o veredito da queda (ate 40 s)...
set /a ESPERA=0
:esperaqueda
timeout /t 1 /nobreak >nul
findstr /c:"BANCADA DA ENTRADA NO MUNDO" "%LOGQ%" >nul 2>nul && goto :mostraqueda
set /a ESPERA+=1
if %ESPERA% lss 40 goto :esperaqueda
:mostraqueda
findstr /c:"[carga]" "%LOGQ%"

:fim
echo.
echo  Encerrado. Leia as linhas "=====" no fim de cada rodada.
echo  As fotos estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus":
echo     tira-criacao-q00..qNN-*.png   TODO quadro entre o clique e o corpo
echo     corpo-criacao-A/B/C-*.png     as tres fotos da prova do corpo
echo     tira-semcobertura-*.png       os mesmos quadros com o defeito na frente
echo     queda-queda-o-que-sobrou.png  a tela depois de o servidor morrer
echo.
echo  Se o servidor ainda estiver de pe numa janela minimizada, feche-a.
pause
