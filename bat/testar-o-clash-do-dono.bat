@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- os DOIS pedidos do dono sobre os clashs

REM ===========================================================================
REM  OS DOIS PEDIDOS DO DONO, MEDIDOS
REM
REM     testar-o-clash-do-dono.bat
REM
REM  1) *"durante os clashs apertar os botoes ta ABRINDO MENUS etc. faca q
REM      enquanto ta tendo QUALQUER CLASH (ki ou zanzoclash) as HOTKEYS SAO
REM      DESATIVADAS pra n ficar abrindo varios menus"*
REM  2) *"faca com q caso vc ACERTE o quick time event, ele JA VAI PRO PROXIMO
REM      BOTAO pra apertar, entao realmente QUANTO MAIS RAPIDO VC FOR MELHOR VAI
REM      SER"*
REM
REM  SAO DUAS RODADAS, e elas medem coisas que moram em lados opostos do fio:
REM
REM    1/2  --pressateste   O SEGUNDO PEDIDO, no SERVIDOR, sem ninguem em jogo.
REM                         Cinco familias nos DOIS embates (o vao entre letras,
REM                         o desfecho seguindo a mao, e o erro que NAO adianta),
REM                         cada uma com o METRONOMO injetado -- o jogo de antes,
REM                         escrito como "piso = janela". Gasta ~1 minuto de
REM                         relogio de parede, e a razao esta no cabecalho do
REM                         arquivo: o embate de velocidade conta tudo em
REM                         `NowMs()`, entao um laco sincrono mediria o laco.
REM
REM    2/2  --diagmudez     O PRIMEIRO PEDIDO, no CLIENTE, hospedando. Aperta as
REM                         DEZESSETE teclas de jogador uma por uma -- uma linha
REM                         com o nome de cada -- FORA do embate (onde elas tem
REM                         que abrir) e DENTRO dos DOIS embates (onde elas tem
REM                         que ficar mudas), e confere que a letra do quick time
REM                         event e o movimento continuam vivos.
REM                         Vem com `--mudezteste`, que e quem poe os embates de
REM                         VERDADE de pe (gatilho de producao, sem sorteio).
REM
REM  HEADLESS as duas: nao ha foto a tirar aqui, e o que se le e o placar.
REM
REM  PORTA PROPRIA (7918): se aparecer "FALHOU ao abrir a porta" ou "Bind
REM  exception", ha outra rodada viva -- feche-a. **Isto nao e detalhe**: com a
REM  porta tomada, o cliente da rodada 2 conecta no servidor DA OUTRA e a bancada
REM  mede um processo em que as fixtures nao existem (foi o que aconteceu na
REM  primeira rodada desta bancada, e o placar saiu cheio de falha por isso).
REM
REM  CONTA PROPRIA (bancada_mudez): nada aqui toca personagem de jogador.
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
echo     testar-o-clash-do-dono.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7918

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    REM `-t:Rebuild` E OBRIGATORIO, e custou uma rodada inteira: com a compilacao
    REM incremental, uma bancada NOVA saiu de fora do binario e a rodada mediu a
    REM versao anterior dela -- placar coerente, checagem faltando, e nada no log
    REM dizendo isso.
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ---- 1/2: SER RAPIDO PAGA? (servidor, sem ninguem em jogo) ----
echo   Procure:  [pressa] ==== N passaram, N falharam ^| injecao: ...
echo.
"%GODOT%" --headless --path . --server --port 7918 --pressateste

echo.
echo  ---- 2/2: A MUDEZ DOS ATALHOS (cliente, hospedando) ----
echo   Procure:  [mudez] PLACAR: ...  e  [mudez] INJECAO: ...
echo.
"%GODOT%" --headless --path . --host --rede 7918 --mudezteste --kiteste --bpteste 200000 ^
          --conta bancada_mudez --senha teste --nome Mudo --raca Saiyan --diagmudez

echo.
echo  Encerrado.
pause
