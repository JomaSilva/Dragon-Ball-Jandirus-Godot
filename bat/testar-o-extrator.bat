@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do EXTRATOR DE SKILLS

REM ===========================================================================
REM  O `after_learn` QUE CAIA NO CHAO  (dotnet ... -- extrator)
REM
REM     testar-o-extrator.bat
REM
REM  SAO 26 PROVAS EM 3 SEGUNDOS, SEM GODOT E SEM JANELA: o que ela mede e o
REM  extrator, que e um programa de console.
REM
REM  POR QUE ELA EXISTE. O extrator ja perdeu efeito de skill DUAS vezes, e as
REM  duas do mesmo jeito: ele lia UMA forma de declarar `after_learn()`, a outra
REM  caia no chao CALADA, e a skill saia do `skills.json` como se o DM tambem
REM  nao fizesse nada.
REM
REM     1a vez: 116 skills (o `after_learn` com o typepath na propria linha).
REM     2a vez:   3 skills (Mafuba, Open Dead Zone, Superior Seal), cujo corpo
REM               mora em `Modules/Magic/Sealing.dm` -- 175 arquivos ANTES do
REM               arquivo onde os typepaths sao declarados
REM               (`Modules/Ranks/ordered/EarthRanks.dm`).
REM
REM  Nas duas vezes o remedio foi ler mais uma forma. Nas duas vezes ninguem pos
REM  uma bancada -- e e por isso que houve a segunda vez.
REM
REM  O QUE ELA NAO E: ela nao pergunta "o extrator rodou". Ela MONTA uma arvore
REM  DM sintetica com o defeito exato dentro (o efeito num arquivo que vem ANTES
REM  do arquivo do dono), exige que o verb, o buff e a delegacao cheguem na
REM  skill, e entao LIGA O DEFEITO DE VOLTA -- a chave
REM  `DmSkillScanner.ComoAntesDoConserto`, que e o extrator de antes do conserto
REM  -- e exige que as MESMAS linhas fiquem vermelhas.
REM
REM  AS QUATRO FAMILIAS
REM     1) SINTETICA, ORDEM RUIM  o efeito antes do dono. O verb chega, o buff
REM                               chega, e a DELEGACAO (`choose()`) chega -- e e
REM                               por ela que o laco dos adiados roda ANTES do
REM                               laco dos chamados.
REM     2) SINTETICA, ORDEM BOA   o dono antes do efeito: o caminho que sempre
REM                               funcionou, inclusive no extrator ANTIGO. E o
REM                               controle que prova que o eixo medido e a ORDEM
REM                               DE LEITURA, e nao "o extrator le after_learn".
REM     3) O ALARME               `after_learn` de typepath inexistente sai
REM                               NOMEADO, com arquivo e a linha certa; e a
REM                               declaracao base `/datum/skill/proc/after_learn()`
REM                               do motor do DM NAO vira falso positivo.
REM     4) A ARVORE DE VERDADE    as tres skills de selo no DM do dono, o alarme
REM                               calado, e o `skills.json` NO DISCO -- porque o
REM                               jogo nao roda o extrator, ele le o arquivo.
REM
REM  CODIGO DE SAIDA = numero de falhas. Com o conserto desfeito na mao, ela deu
REM  8 falhas e saida 8; com ele no lugar, 26 OK e saida 0.
REM
REM  A IRMA DELA e a `testar-selo.bat` (--seloteste): aquela mede o que o dado
REM  extraido VIRA no jogo; esta mede se o dado chega.
REM ===========================================================================

cd /d "%~dp0"

set "CODE=E:\Users\Joao\Desktop\Desktop\Finale-master\Code"
if not "%~1"=="" set "CODE=%~1"

if not exist "%CODE%" (
    echo.
    echo  Nao achei a arvore do DM em:
    echo     %CODE%
    echo.
    echo  Passe o caminho na linha de comando:
    echo     testar-o-extrator.bat C:\caminho\Finale-master\Code
    echo.
    pause
    exit /b 1
)

where dotnet >nul 2>nul
if not %errorlevel%==0 (
    echo.
    echo  Precisa do dotnet no PATH -- a bancada e um programa de console.
    echo.
    pause
    exit /b 1
)

echo  DM     : %CODE%
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
echo  ---- o extrator de skills (26 provas) ----
echo.
dotnet "Tools\AssetPipeline\bin\Debug\net8.0\AssetPipeline.dll" extrator "%CODE%" "Assets\Data\skills.json"
set "FALHAS=%errorlevel%"

echo.
if "%FALHAS%"=="0" (
    echo  ==== 0 FALHA ====
) else (
    echo  ==== %FALHAS% FALHA^(S^) -- leia as linhas "FALHA" acima ====
)
pause
exit /b %FALHAS%
