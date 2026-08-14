@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada da tecla E nas naves

REM ===========================================================================
REM  A TECLA E NAS NAVES  (--diagembarque + --embarqueteste)
REM
REM     testar-embarque.bat            conta nova sorteada, porta 7995
REM     testar-embarque.bat outraconta
REM
REM  O que ela faz, sozinha e sem janela: fabrica uma Capital Ship pela tela do
REM  jogador, ANDA ate ela, aperta E, embarca, atravessa a sala de 100x100 a pe,
REM  acha a ponte, pilota, aperta E de novo pra voltar pra dentro, desembarca --
REM  e no meio disso cobra as recusas (senha, dono, nave destruida), varre o menu
REM  P atras de botao de nave que tenha sobrado e confere que o que FICOU na aba
REM  Nav continua funcionando.
REM
REM  PORTA PROPRIA (7995): as bancadas nao saem sozinhas, e enquanto um --host
REM  estiver no ar nenhuma outra sobe na mesma porta. Se a linha
REM     [server] FALHOU ao abrir a porta 7995
REM  aparecer, ha outra rodada viva -- feche-a ou use outra porta.
REM
REM  CONTA NOVA a cada rodada, pelo relogio: a bancada FABRICA e DESTROI uma nave
REM  de dois milhoes, e rodar isso no personagem de alguem nao e bancada, e
REM  estrago. Nada aqui toca conta existente.
REM ===========================================================================

cd /d "%~dp0"

set CONTA=%1
if "%CONTA%"=="" set CONTA=emb%RANDOM%

REM --- achar o Godot (mesma busca do servidor.bat) --------------------------
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
echo     testar-embarque.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Conta : %CONTA%  (porta 7995)

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
echo  ================================================================
echo   Procure a ultima linha:  [embarque] ===== N OK, M FALHA(S) =====
echo   O percurso leva cerca de dois minutos (o corpo ANDA de verdade).
echo  ================================================================
echo.

REM Sem o separador "--": com ele as flags vao parar em GetCmdlineUserArgs()
REM e a bancada sobe MUDA. Ver servidor.bat.
"%GODOT%" --headless --path . --host --rede 7995 --embarqueteste --diagembarque ^
          --conta %CONTA% --nome %CONTA%

echo.
echo  Encerrado.
pause
