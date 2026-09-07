@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada da IA NA AGUA (nadar / voar)
REM ===========================================================================
REM  A IA NA AGUA  (--nadoiateste)
REM
REM     testar-nado-ia.bat
REM
REM  O PEDIDO DO DONO (2026-09-07): "faca com que a IA saiba nadar quando
REM  precisar (quando e jogada na agua ou quando precisa atravessar a agua)
REM  mas se ela souber voar ela de preferencia pra voo".
REM
REM  A bancada acha um lago de verdade no mapa da Terra (8 a 14 celulas de
REM  largura, chao dos dois lados) e joga corpos nele:
REM    1) so corpo: liga o nado sozinho e nada ate a margem, sem teleporte;
REM    2) quem sabe voar decola em vez de nadar e nao pousa na agua;
REM    3) sem folego boia parado e nada quando o Ki volta;
REM    4) alvo na outra margem: entra nadando (ou atravessa voando); e o
REM       contra-exemplo do alvo no seco, que nao mexe no modo;
REM    5) o habitante (rotina) jogado no lago nada e volta a rotina; o que voa
REM       decola e pousa no seco;
REM    6) defeito injetado: com a regra desligada o corpo fica boiando.
REM  Placar: "[nadoia] ==== N OK, M FALHA(S) ====".
REM
REM  Roda no 1o login (o habitante de molde so acorda com jogador na zona).
REM  A pasta de saves do Godot e DESVIADA pro %%TEMP%%.
REM  PORTA PROPRIA (7922): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     testar-nado-ia.bat
echo.
pause
exit /b 1
:temgodot
echo  Godot : %GODOT%
echo  Porta : 7922
set "APPDATA=%TEMP%\jandirus-bancada-nado-ia"
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
echo  ---- a IA na agua (servidor, 1o login, sem janela) ----
"%GODOT%" --path . --headless --host --rede 7922 --nadoiateste ^
          --raca Human --conta bancada_nado_ia --nome MedidorDoNado
echo.
echo  Leia o placar: "[nadoia] ==== N OK, M FALHA(S) ====".
pause
