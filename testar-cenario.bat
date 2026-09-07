@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do ESTRAGO QUE CHEGA DE UMA VEZ
REM ===========================================================================
REM  O ESTRAGO QUE CHEGA DE UMA VEZ  (--cenarioteste + --diagcenario)
REM
REM     testar-cenario.bat
REM
REM  A QUEIXA DO DONO (2026-09-06): "ao voltar/entrar em um local que teve o
REM  cenario destruido, o jogo carrega o cenario normal e depois destroi o
REM  que deve estar destruido pra sincronizar com o server... roda a animacao
REM  de destruicao pra todos os tiles destruidos ao mesmo tempo, e isso faz o
REM  loading aumentar consideravelmente".
REM
REM  O servidor mandava o estrago de quem chega como N pacotes de "caiu
REM  agora", e o cliente animava cada um -- e reaplicava a lista INTEIRA a
REM  cada pedaco do mapa que o pintor entregava, replantando a terra revirada
REM  toda vez. Agora: UM retrato por zona (com a zona dentro), aplicado sem
REM  poeira, so quando o chao daquela zona esta montado, so no pedaco que
REM  acabou de entrar, e a terra revirada nasce uma vez por celula.
REM
REM  UM PROCESSO SO, headless: o servidor sobe com a bancada do fio
REM  (--cenarioteste, placar "[cenario] ==== N OK"), o dono nasce com 40
REM  celulas caindo AO VIVO em volta (--quebrarteste 40), o servidor o leva a
REM  Namek e o traz de volta, e o robo (--diagcenario) mede a volta (placar
REM  "[cenario] ==== N OK, M FALHA(S) ====").
REM
REM  A pasta de saves do Godot e DESVIADA pro %%TEMP%% -- a bancada nunca
REM  escreve na pasta de saves de quem joga nesta maquina.
REM
REM  PORTA PROPRIA (7915): se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a.
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
echo     testar-cenario.bat
echo.
pause
exit /b 1
:temgodot
echo  Godot : %GODOT%
echo  Porta : 7915
set "APPDATA=%TEMP%\jandirus-bancada-cenario"
if exist "%APPDATA%\Godot" rmdir /s /q "%APPDATA%\Godot"
if not exist "%APPDATA%" mkdir "%APPDATA%"
echo  Saves : %APPDATA% (desviado -- a pasta de saves de quem joga fica intacta)
where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)
echo.
echo  ---- o estrago que chega de uma vez: o fio (servidor) e a volta (cliente) ----
"%GODOT%" --path . --headless --host --rede 7915 --cenarioteste --quebrarteste 40 --diagcenario ^
          --raca Human --conta bancada_cenario --nome MedidorDeCenario
echo.
echo  Leia os dois placares acima: "[cenario] ==== N OK, M FALHA(S) ====" (o fio) e
echo  "[cenario] ============ N OK, M FALHA(S) ============" (a volta).
pause
