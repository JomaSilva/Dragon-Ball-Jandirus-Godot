@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada da cidade e do banco

REM ===========================================================================
REM  A CIDADE DE VEGETA E AS PAREDES INVISIVEIS
REM
REM     testar-cidade.bat
REM
REM  Ela roda as DUAS metades, e as duas fazem perguntas que a outra nao alcanca:
REM
REM   1) A BANCADA DOS ARQUIVOS (AssetPipeline, comando `cidade`). Julga o que
REM      esta PUBLICADO em Assets/Maps -- e nao o que uma conversao de hoje
REM      produziria. Ela cobra:
REM        * toda celula que bloqueia tem DONO (a fonte, uma porta, uma maquina
REM          ou a planta da cidade) -- e nenhuma perdeu a arte que o .dmm tem;
REM        * a reciproca: maquina densa e porta bloqueiam de verdade;
REM        * cada peca que o construtor de Vegeta promete esta no mapa, com o
REM          seu nome e a sua linha (a lista vem do construtor, nao daqui);
REM        * da pra ANDAR do ponto de nascimento ate o banco e usar o verbo --
REM          em Vegeta e na Terra.
REM
REM   2) A BANCADA VIVA (--cidadeteste, porta 7984). Sobe o servidor de verdade
REM      e mede o que so existe depois do boot: o alarme das construcoes mudas,
REM      as maquinas do mapa viradas Obra, a camada de colisao de runtime, e um
REM      corpo que chega no banco, pede o extrato e MOVE zeni.
REM
REM  CADA FAMILIA REPROVA COM O DEFEITO INJETADO, e as injecoes rodam junto --
REM  elas entram no placar. Uma bancada que nunca ficou vermelha nao provou nada.
REM
REM  PORTA PROPRIA (7984): enquanto outra rodada estiver no ar, esta nao sobe.
REM ===========================================================================

cd /d "%~dp0"

set DM=E:\Users\Joao\Desktop\Desktop\Finale-master
if not exist "%DM%\Code" (
    echo.
    echo  Nao achei o projeto BYOND em %DM%
    echo  A bancada precisa dele: a arvore de tipos ^(Code^) e os mapas de origem ^(Maps^).
    echo.
    pause
    exit /b 1
)

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
echo     testar-cidade.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  DM    : %DM%
echo.

echo  Compilando...
dotnet build "Dragon ball Jandirus.csproj" -v q -nologo
if errorlevel 1 (
    echo.
    echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
    pause
    exit /b 1
)

echo.
echo  ================================================================
echo   1/2  OS ARQUIVOS  (Assets/Maps publicado)
echo   Procure a ultima linha:  [cidade] ===== N OK, M FALHA(S) =====
echo  ================================================================
echo.
dotnet run --project Tools/AssetPipeline -- cidade Assets/Maps "%DM%\Code" "%DM%\Maps"

echo.
echo  ================================================================
echo   2/2  O SERVIDOR VIVO  (porta 7984)
echo   Ele NAO sai sozinho: feche esta janela depois do placar.
echo  ================================================================
echo.
"%GODOT%" --headless --path . --server --rede 7984 --cidadeteste

echo.
echo  Encerrado.
pause
