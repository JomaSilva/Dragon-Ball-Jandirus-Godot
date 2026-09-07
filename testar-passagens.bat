@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada da BOCA DA CAVERNA
REM ===========================================================================
REM  A BOCA DA CAVERNA  (--passagemteste)
REM
REM     testar-passagens.bat
REM
REM  A QUEIXA DO DONO (2026-09-06): "sair de cavernas etc nao ta colocando o
REM  personagem 1 tile na frente da entrada, as vezes o personagem volta
REM  dentro de paredes".
REM
REM  NO BYOND NINGUEM PISA NA BOCA: o Enter() teleporta e nega o passo. Aqui a
REM  celula era chao e o gatilho era "pe em cima", entao o corpo andava pra
REM  dentro do desenho da entrada (a fileira da parede) e so depois viajava;
REM  chegando do outro lado com a tecla apertada, andava pra dentro da boca de
REM  volta e, passada a carencia, voltava -- de dentro da parede.
REM
REM    1) A BOCA E LACRADA nas duas pontas, pela mesma lista .passagens; o
REM       .col no disco nao muda (contra-exemplo relendo o arquivo).
REM    2) A CHEGADA E UM TILE NA FRENTE da boca de volta, em todas as
REM       passagens de todos os mapas -- inclusive a unica que o DM cravou de
REM       lado (Turfs.dm:1452-1455, divergencia declarada); e um contra-exemplo
REM       puro com o ponto do DM dentro da parede.
REM    3) O GATILHO E A BORDA DO PASSO: parado ao lado nao viaja, passar na
REM       frente nao viaja, empurrar viaja; segurar a tecla depois de chegar
REM       nao devolve; soltar e empurrar de novo devolve.
REM
REM  RODA NO HEADLESS. A pasta de saves do Godot e DESVIADA pro %%TEMP%% -- a
REM  bancada nunca escreve na pasta de saves de quem joga nesta maquina.
REM
REM  PORTA PROPRIA (7914): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     testar-passagens.bat
echo.
pause
exit /b 1
:temgodot
echo  Godot : %GODOT%
echo  Porta : 7914
set "APPDATA=%TEMP%\jandirus-bancada-passagens"
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
echo  ---- a boca da caverna: lacrada, chegada na frente, gatilho na borda do passo ----
"%GODOT%" --path . --headless --host --rede 7914 --passagemteste ^
          --raca Human --conta bancada_passagens --nome MedidorDePassagens
echo.
echo  Leia o placar acima: "[passagem] ==== N OK, M FALHA(S) ====".
pause
