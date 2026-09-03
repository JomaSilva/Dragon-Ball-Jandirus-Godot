@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada das DEZESSETE SKILLS DA MENTE

REM ===========================================================================
REM  "STRENGTH OF MIND": AS DEZESSETE, UMA A UMA
REM
REM     testar-a-mente.bat
REM
REM  (NAO CONFUNDA com `testar-mente.bat`, sem o "a": aquela e a bancada do
REM   CORPO LARGADO e da Dimensao Mental, e sobe DOIS processos.)
REM
REM  DUAS BANCADAS EM SEQUENCIA, sem janela:
REM
REM   1) `dotnet ... -- maestria` (Core puro, ~3 s): as fontes de exp da raiz da
REM      arvore de Ki, lidas do `niveis.json` DO DISCO, e o banco de exp
REM      adiantado (`expbuffer`) rendendo por cima da taxa nua.
REM
REM   2) `--menteskills` (servidor de pe, ~20 s): as dezessete pelo funil de
REM      producao. Sete familias:
REM        1. a TABELA das dezessete (tier, custo, fontes de exp, degraus, verbos);
REM        2. as SETE condicoes de exp novas, cada uma nas duas metades;
REM        3. a corrente `if/else` (o `else` nao credita por cima do irmao);
REM        4. o ALCANCE: `Aprender` -> os cinco acendedores por contador -> a
REM           cadeia Basic 100 -> Advanced 100 -> Perfect, e a Targeted;
REM        5. o EFEITO NOMEADO de cada uma (corpo sem ela contra corpo com ela);
REM        6. o SISTEMA DE ESTUDO (Study_Other, Focus_Skill, Write_Teachings);
REM        7. o `kibuffon` e a cura do `buffregen`.
REM
REM  O QUE ELA GUARDA: quinze das dezessete tinham ZERO fonte de exp neste port
REM  (as condicoes de ganho delas caiam em "condicao que o port nao entende"), e
REM  como as Advanced/Perfect so acendem no nivel 100 da anterior, DEZ eram
REM  inalcancaveis. Comprar e subir sao duas coisas, e a bancada mede as duas.
REM
REM  PORTA PROPRIA (7985): se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a.
REM ===========================================================================

cd /d "%~dp0"

where dotnet >nul 2>nul
if not %errorlevel%==0 (
    echo.
    echo  Precisa do dotnet no PATH.
    pause
    exit /b 1
)

echo  Compilando o pipeline...
dotnet build "Tools\AssetPipeline\AssetPipeline.csproj" -t:Rebuild -v q -nologo
if errorlevel 1 (
    echo  A compilacao do pipeline FALHOU.
    pause
    exit /b 1
)

echo.
echo  ---- 1) as fontes de exp no Core (dotnet -- maestria) ----
echo.
dotnet "Tools\AssetPipeline\bin\Debug\net8.0\AssetPipeline.dll" maestria
set "FALHAS=%errorlevel%"

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
echo  Nao encontrei o Godot -- a metade do servidor nao rodou.
echo     set GODOT=C:\caminho\Godot_v4.7.1-stable_mono_win64_console.exe
pause
exit /b %FALHAS%

:temgodot
echo.
echo  Compilando o jogo...
REM `-t:Rebuild` e OBRIGATORIO: o build incremental ja deu "compilacao com
REM exito" sem trocar a DLL, e o Godot subiu com o binario de ontem.
dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
if errorlevel 1 (
    echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
    pause
    exit /b 1
)

echo.
echo  ---- 2) as dezessete pelo funil do servidor (--menteskills) ----
echo.
echo   Roda no BOOT e leva uns 20 s (o livro de ensinamentos custa 18.000
echo   tiques de meditacao, que sao simulados). O SERVIDOR CONTINUA DE PE --
echo   leia o placar "[mente] ==== N passaram, M falharam ====" e feche com Ctrl+C.
echo.
"%GODOT%" --headless --path . --host --rede 7985 --menteskills

echo.
echo  Encerrado. (a bancada de mesa deu %FALHAS% falha^(s^))
pause
exit /b %FALHAS%
