@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- a lua cheia pega um NPC Saiyajin (bancada + FOTO)

REM ===========================================================================
REM  O NPC SAIYAJIN QUE VIRA OOZARU  --  as duas metades, em duas rodadas
REM
REM     testar-macaco.bat            as duas (bancada primeiro, foto depois)
REM     testar-macaco.bat bancada    so a bancada de numeros (headless)
REM     testar-macaco.bat foto       so a foto (precisa de JANELA)
REM
REM  1) `--luaferateste`: 74 checagens, sem janela. Os quatro portoes do dono
REM     (saiyajin / lutando / ferido grave / lua cheia), o RABO como portao
REM     legitimo, a maestria sorteada por semente, quem toma as redeas, a VOLTA
REM     (o NPC nao pode virar estatua) e o custo por corpo por tique.
REM     Procure:  ===== FIM: N OK, M FALHA(S) =====
REM
REM  2) `--macacovivo --diagmacaco`: nasce UM Saiyajin ao lado de quem entrar,
REM     poe a Terra em lua cheia e abre o braco dele 20 s depois. Quem
REM     transforma e o `TickDoCeu` de verdade. O cliente fotografa antes,
REM     durante e depois -- porque numero nenhum responde "o sprite trocou".
REM     As fotos saem em:
REM        %%APPDATA%%\Godot\app_userdata\Dragon ball Jandirus\macaco-*.png
REM     Procure:  [macaco] ===== N OK, M FALHA(S) =====
REM
REM  PORTA PROPRIA (7954). Se aparecer "[server] FALHOU ao abrir a porta 7954"
REM  ha outra rodada viva -- feche-a.
REM ===========================================================================

cd /d "%~dp0"

set MODO=%1
if "%MODO%"=="" set MODO=tudo

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
echo     testar-macaco.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7954

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

if "%MODO%"=="foto" goto :foto

echo.
echo  ================================================================
echo   1/2  BANCADA (headless). Procure: ===== FIM: N OK, M FALHA(S) =
echo   Ela nao fecha sozinha: feche a janela quando o placar sair.
echo  ================================================================
echo.
"%GODOT%" --headless --path . --host --rede 7954 --luaferateste ^
          --conta bancada_luafera --nome QuemViraMacaco

if "%MODO%"=="bancada" goto :fim

:foto
echo.
echo  ================================================================
echo   2/2  FOTO (com janela). O Saiyajin nasce ao lado de voce, apanha
echo   e vira macaco sozinho aos ~20 s. O robo sai do jogo no fim.
echo  ================================================================
echo.
REM COM JANELA DE PROPOSITO: no headless o `GetImage` volta vazio e nao ha foto
REM nenhuma -- a bancada sairia verde por nao ter olhado (ver RoboDeMacaco).
"%GODOT%" --path . --host --rede 7954 --macacovivo --diagmacaco --bpteste 3000000 ^
          --raca Saiyan --conta bancada_macaco --nome OlhaOMacaco

echo.
echo  As fotos estao em:
echo     %APPDATA%\Godot\app_userdata\Dragon ball Jandirus\macaco-*.png

:fim
echo.
echo  Encerrado.
pause
