@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada da FLUIDEZ (corpo remoto liso)

REM ===========================================================================
REM  A FLUIDEZ DO CORPO REMOTO  (--diagfluidez + --fluidez a|b)
REM
REM     testar-fluidez.bat
REM
REM  O relato do dono: "movimentacao de outros jogadores remotos como andando,
REM  correndo ou voando nao esta fluida, eles parecem ficar dando micro
REM  teleportes e quando o player e muito rapido fica mais perceptivel".
REM
REM  TRES RODADAS, uma atras da outra:
REM
REM   1) LABORATORIO (--diagfluidez, sem mundo, sem rede, headless). Um
REM      RemotePlayer de producao alimentado com snapshots SINTETICOS -- jitter
REM      de chegada, dois pacotes no mesmo quadro, quadro sem pacote, o degrau
REM      do tique (posicao repetida + deslocamento dobrado), corpo movido pelo
REM      servidor, voo -- a 352 e a 1760 px/s, com quadros irregulares. Mede o
REM      passo POR QUADRO da posicao DESENHADA. Contra-exemplo: um teleporte de
REM      3000 px crava no MESMO quadro. Defeito injetado: voltar a carimbar pela
REM      hora de chegada TEM que reprovar a mesma alimentacao.
REM
REM   2) DOIS PROCESSOS, velocidade normal (andar 160 / correr 352 px/s + voo).
REM      A e o host, headless: anda, corre e voa em pernas retas segurando as
REM      acoes reais. B conecta, headless, e grava a serie de passos por quadro
REM      do RemotePlayer de A -- os mesmos criterios da rodada 1.
REM
REM   3) DOIS PROCESSOS, velocidade no TETO (--espeedteste 1000000: o stat base
REM      de velocidade no infinito, que o StatCap trava em SpeedStat ~4,85 =
REM      ~1700 px/s correndo). E o caso "quando o player e muito rapido".
REM
REM  Procure no console:   [fluidez] ===== N OK, M FALHA(S) =====   (uma por rodada)
REM
REM  PORTA PROPRIA (7971): enquanto outro --host estiver no ar nenhum sobe na
REM  mesma porta. As bancadas vizinhas usam 7940-7969 e 7980-7999.
REM
REM  A PASTA DE SAVES DO DONO NAO E TOCADA: o APPDATA e desviado logo abaixo.
REM ===========================================================================

cd /d "%~dp0"

REM NUNCA na pasta de saves real (%APPDATA%\Godot\app_userdata\...). Tudo o que
REM esta bancada grava (contas, personagens, mundo.json, relatorio) vai pra ca.
set "APPDATA=%TEMP%\jandirus-bancada-fluidez"
if not exist "%APPDATA%" mkdir "%APPDATA%"

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
echo     testar-fluidez.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot   : %GODOT%
echo  Porta   : 7971
echo  APPDATA : %APPDATA%

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ---- rodada 1: laboratorio (--diagfluidez) ----
"%GODOT%" --headless --path . --diagfluidez

echo.
echo  ---- rodada 2: dois processos, velocidade normal ----
call :dupla normal ""

echo.
echo  ---- rodada 3: dois processos, velocidade no teto (--espeedteste 1000000) ----
call :dupla teto "--espeedteste 1000000"

echo.
echo  Encerrado.
pause
exit /b 0

REM ---------------------------------------------------------------------------
REM  :dupla <rotulo> <flags extras do servidor>
REM  O B entra 12 s depois (ele precisa achar a porta aberta). O A e o host e
REM  fica em primeiro plano ate o roteiro dele acabar (~45 s); o B fecha sozinho
REM  quando termina de medir, e o que sobrar dele e morto pela linha de comando.
REM  O ATRASO E `ping` E NAO `timeout`: o `timeout` le do teclado e morre com
REM  "Input redirection is not supported" em qualquer automacao.
REM ---------------------------------------------------------------------------
:dupla
set "ROTULO=%~1"
set "EXTRA=%~2"
start "fluidez-b-%ROTULO%" /min cmd /c "set "APPDATA=%APPDATA%" & ping -n 13 127.0.0.1 >nul & ""%GODOT%"" --headless --path . --rede 7971 --connect 127.0.0.1 --fluidez b --fluidezalvo FluidezA --fluidezrotulo %ROTULO% --raca Human --conta bancada_fluidez_b_%ROTULO% --nome FluidezB"
REM CONTA POR RODADA (`_%ROTULO%`): o stat de velocidade da rodada do teto fica no save do
REM personagem, e a rodada normal seguinte entraria com ele se a conta fosse a mesma.
REM `--fluidezalvo FluidezB` no A tambem: o berco e povoado, e sem o nome o A arrancaria ao ver o
REM primeiro NPC do snapshot -- antes de o B acabar de entrar (aconteceu na primeira rodada).
"%GODOT%" --headless --path . --host --rede 7971 --kiteste --bpteste 100000 --vooteste %EXTRA% --fluidez a --fluidezalvo FluidezB ^
          --raca Human --conta bancada_fluidez_a_%ROTULO% --nome FluidezA
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*bancada_fluidez_b*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul
exit /b 0
