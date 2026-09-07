@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do OOZARU (a lua que some, o soco que toca uma vez, o KO)
REM ===========================================================================
REM  O OOZARU  (--luasometeste + --diagflick)
REM
REM     testar-oozaru.bat
REM
REM  AS QUEIXAS DO DONO (2026-09-06):
REM    * "a aba de formas nao ta mostrando a forma atual que eu to"
REM    * "quando ele soca, a animacao de socar dele fica loopando rapido por
REM       alguns segundos"
REM    * "na oozaru ao ser nocauteado o sprite nao e rotacionado"
REM    * "a forma do oozaru e desfeita no momento que a fonte que deu o poder
REM       pra virar (lua etc) sumir: quando amanhecer e a lua sair do ceu,
REM       todos que estao em oozaru voltam a forma base"
REM
REM  E O QUE SE ACHOU NO CAMINHO: o relogio da fera (TickDoOozaru) estava
REM  FORA do laco de producao desde o "Grande Update Parte 4" -- so as
REM  bancadas o chamavam. Em jogo o macaco nunca se cansava nem perdia o
REM  controle.
REM
REM  DUAS RODADAS, as duas sem janela:
REM    1) --luasometeste (servidor, no 1o login): o laco de producao roda o
REM       relogio da fera; amanheceu -> cai; a lua minguou -> cai; dois feras
REM       caem no mesmo tique; a ficha de atributos diz "oozaru" enquanto dura.
REM       Placar: "[luasome] ==== N OK, M FALHA(S) ====".
REM    2) --diagflick (so o CharacterVisual): o soco toca UMA vez e o corpo
REM       volta ao parado (com o defeito injetado: sem a regra, o laco volta);
REM       o KO do macaco usa o quadro deitado do oeste e a MESMA rotacao de
REM       todo corpo. Placar: "[flick] ==== N OK, M FALHA(S) ====".
REM
REM  A pasta de saves do Godot e DESVIADA pro %%TEMP%%.
REM  PORTA PROPRIA (7920): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     testar-oozaru.bat
echo.
pause
exit /b 1
:temgodot
echo  Godot : %GODOT%
echo  Porta : 7920
set "APPDATA=%TEMP%\jandirus-bancada-oozaru"
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
echo  ---- 1) a lua que some (servidor) ----
"%GODOT%" --path . --headless --host --rede 7920 --luasometeste ^
          --raca Saiyan --conta bancada_oozaru --nome MedidorDaFera
echo.
echo  ---- 2) o soco que toca uma vez e o KO do macaco (cliente, sem rede) ----
"%GODOT%" --path . --headless --diagflick
echo.
echo  Leia os dois placares: "[luasome] ==== N OK ====" e "[flick] ==== N OK ====".
pause
