@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- teste da tecla C (Ki liberado)

REM ===========================================================================
REM  TESTE DA TECLA C
REM
REM    testar-ki.bat             BP padrao (3 milhoes)
REM    testar-ki.bat 50000000    outro BP
REM
REM  Sobe o jogo HOSPEDANDO (servidor dentro do proprio cliente, uma janela so)
REM  e com o Ki LIBERADO: o personagem ja entra com
REM
REM     Ki Unlocked            -> a carga funciona
REM     Basic Ki Control nv 5  -> o `canPower`, que deixa passar de 100%%
REM
REM  As duas coisas sao necessarias. So a primeira faz o C carregar e PARAR no
REM  teto, que e metade do que se quer ver.
REM
REM  Confira na janela a linha:
REM     [server] --kiteste em `Fulano`: canPower=1 ... OK
REM  Se ela disser NAO PEGOU, o elo skill -> flag quebrou e o teste nao vale.
REM
REM  Como testar: segure C. A aura acende e o Ki sobe; passando de 100%% ela
REM  troca de cor (a de sobrecarga). O host e sempre admin, entao a aba de
REM  admin do menu P tambem esta disponivel.
REM ===========================================================================

cd /d "%~dp0"

set BP=%1
if "%BP%"=="" set BP=3000000

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
echo  Defina o caminho e rode de novo:
echo.
echo     set GODOT=C:\caminho\Godot_v4.7.1-stable_mono_win64_console.exe
echo     testar-ki.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  BP    : %BP%

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- o jogo subiria com o binario antigo, e o
        echo  teste estaria medindo a versao de ontem.
        pause
        exit /b 1
    )
) else (
    echo  dotnet nao encontrado: subindo com o binario ja compilado.
)

echo.
echo  ================================================================
echo   Segure C pra carregar. Procure a linha "--kiteste ... OK".
echo  ================================================================
echo.

REM Sem o separador "--": com ele os argumentos vao parar em
REM GetCmdlineUserArgs() e as flags de bancada saem MUDAS. Ver servidor.bat.
"%GODOT%" --path . --host --kiteste --bpteste %BP%

echo.
echo  Encerrado.
pause
