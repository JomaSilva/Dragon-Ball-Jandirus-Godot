@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada dos DEGRAUS DESCARTADOS

REM ===========================================================================
REM  OS DEGRAUS QUE O LEITOR JOGAVA FORA, E OS BUFFS QUE O EXTRATOR NAO PEGAVA
REM
REM     testar-niveis.bat
REM
REM  SAO 86 PROVAS EM 3 SEGUNDOS, SEM GODOT E SEM JANELA: Core puro sobre os
REM  .json NO DISCO (skills.json, skilltrees.json, niveis.json) -- os mesmos
REM  que o jogo carrega.
REM
REM  POR QUE ELA EXISTE. Um censo achou que o maior buraco do port nao era verb:
REM  era DADO ja extraido e descartado pelo leitor. Cada familia e um buraco, e
REM  cada prova tem as DUAS metades (o nivel que nao rende e o que rende; a
REM  skill trancada e a mesma acesa; a compra que soma e o esquecimento que
REM  devolve):
REM
REM     F5a  o degrau PERIODICO (`if(level % 5 == 0)`) -- descartado
REM     F5b  o `destrava` (enableskill no nivel 100) -- ignorado: 55 folhas
REM          saiam do censo como "sem acendedor"; agora 23
REM     F5c  o gene por degrau; F5d o `concede` (Sense no nivel 5);
REM     F5e  o multiplicativo (SpiritBallCost /= 2); F5f a barreira trocada
REM     F1   pitted, gene Regeneration, HPregenbuff, KaiokenMastery, o ganho
REM          na compra com expressao, a escolha unica na 2a forma (+ a Grace
REM          que segue a Trinity)
REM     R    o razao que registrava campo inexistente como aplicado
REM     C    o censo antes/depois
REM
REM  CODIGO DE SAIDA = numero de falhas. As irmas: `testar-o-extrator.bat`
REM  (familia 6: o extrator) e `testar-arvores.bat` (--arvoreteste, familia 4:
REM  o tique de verdade no servidor).
REM ===========================================================================

cd /d "%~dp0"

where dotnet >nul 2>nul
if not %errorlevel%==0 (
    echo.
    echo  Precisa do dotnet no PATH -- a bancada e um programa de console.
    echo.
    pause
    exit /b 1
)

echo  Compilando...
REM `-t:Rebuild` pelo mesmo motivo das outras: build incremental ja deu
REM "compilacao com exito" sem trocar a DLL, e a bancada mediu a versao de ontem.
dotnet build "Tools\AssetPipeline\AssetPipeline.csproj" -t:Rebuild -v q -nologo
if errorlevel 1 (
    echo.
    echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
    pause
    exit /b 1
)

echo.
echo  ---- os degraus descartados (86 provas) ----
echo.
dotnet "Tools\AssetPipeline\bin\Debug\net8.0\AssetPipeline.dll" niveis "Assets\Data"
set "FALHAS=%errorlevel%"

echo.
if "%FALHAS%"=="0" (
    echo  ==== 0 FALHA ====
) else (
    echo  ==== %FALHAS% FALHA^(S^) -- leia as linhas "FALHA" acima ====
)
pause
exit /b %FALHAS%
