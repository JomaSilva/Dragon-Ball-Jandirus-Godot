@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do MOVEL SOBRE A CADEIRA
REM ===========================================================================
REM  O MOVEL SOBRE A CADEIRA  (--mobiliateste + --diagmobilia)
REM
REM     testar-mobilia.bat
REM
REM  A QUEIXA DO DONO (2026-09-06), no castelo de Vegeta: "icones em posicao
REM  errada e ficando torto, como a cadeira em cima do bank" e "o icone nao
REM  aparece, so a hitbox, em varios locais do mapa".
REM
REM  A cadeira e o banco moram na MESMA celula do .dmm, e no BYOND o banco
REM  (criado depois) cobre a cadeira. Aqui o banco e um node que ordenava
REM  pelo TOPO do sprite -- ficava atras. E a fileira de mesas logo abaixo
REM  era um turf denso POR BAIXO do piso: no BYOND so o ultimo turf existe,
REM  mas o conversor somava a fisica dos dois (a "hitbox sem icone").
REM
REM  ABRE UMA JANELA NO SEGUNDO MONITOR (--position 1920,0) porque a prova
REM  final e uma FOTO: o pixel do centro da celula do banco tem a cor do
REM  banco, nao a da cadeira. A foto fica em
REM     %%APPDATA%%\Godot\app_userdata\Dragon ball Jandirus\mobilia-1-banco-e-cadeira.png
REM  com o APPDATA desviado (abaixo) -- a pasta de saves de quem joga fica
REM  intacta. Meio-dia (--horateste 0.5) pra luz nao escurecer a foto.
REM
REM  PORTA PROPRIA (7916): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     testar-mobilia.bat
echo.
pause
exit /b 1
:temgodot
echo  Godot : %GODOT%
echo  Porta : 7916
set "APPDATA=%TEMP%\jandirus-bancada-mobilia"
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
echo  ---- o movel sobre a cadeira: o banco cobre a cadeira, as mesas por baixo do piso nao barram ----
"%GODOT%" --path . --position 1920,0 --host --rede 7916 --mobiliateste --horateste 0.5 --diagmobilia --semfoco ^
          --raca Human --conta bancada_mobilia --nome MedidorDeMobilia
echo.
echo  Leia o placar acima: "[mobilia] ==== N OK, M FALHA(S) ====", e abra a foto em
echo     %APPDATA%\Godot\app_userdata\Dragon ball Jandirus\mobilia-1-banco-e-cadeira.png
pause
