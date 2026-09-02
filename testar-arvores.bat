@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada das ARVORES DE SKILL

REM ===========================================================================
REM  O TIER DE VITRINE E O `enabled = 0` LIDO COMO O DM LE
REM
REM     testar-arvores.bat
REM
REM  DUAS BANCADAS EM SEQUENCIA, sem janela:
REM
REM   1) `dotnet ... -- arvores`  (Core puro, ~3 s): o skills.json NO DISCO e o
REM      SkillBook/EfeitosDeSkill/RegraDeArvore de producao. Seis familias:
REM      o dado, o `enabled`, o tier (Afterimage trancado ao nascer, compravel
REM      depois de investir 4, e caindo com reembolso quando a arvore encolhe),
REM      as portas (uma por arvore: Wrestling, Assassain, Effusive Mastery,
REM      Effusive Specialty, Ki Buff Mastery, Magic, e as quatro do Body), o
REM      veredito como DADO, e o censo das trancadas.
REM
REM   2) `--arvoreteste` (servidor de pe, ~10 s): a compra pelo FUNIL do
REM      jogador, o pacote S2C.Skills desmontado com o leitor do cliente, e o
REM      verbo `skill_esquecer` com a cascata.
REM
REM  AS DUAS RAIZES (medidas por dois agentes, convergentes):
REM     R1  `enabled = 0` e "trancada ate o pre-requisito entrar" (skill.dm:26),
REM         e o port lia como tranca permanente.
REM     R2  nao existia tier de arvore (HtmlUI.dm:820, Body.dm:20-21): o
REM         Afterimage saia de graca ao nascer.
REM  Portar R2 sem R1 tranca o Afterimage PRA SEMPRE -- a familia 3 da bancada
REM  de mesa e a linha que fica vermelha se R1 for desfeita.
REM
REM  PORTA PROPRIA (7983): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo  ---- 1) as arvores no Core (dotnet -- arvores) ----
echo.
dotnet "Tools\AssetPipeline\bin\Debug\net8.0\AssetPipeline.dll" arvores "Assets\Data"
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
echo  ---- 2) o funil do servidor (--arvoreteste) ----
echo.
echo   Roda no BOOT e leva uns 10 s. O SERVIDOR CONTINUA DE PE depois dela --
echo   leia o placar "[arvores] ==== N passaram, M falharam ====" e feche com Ctrl+C.
echo.
"%GODOT%" --headless --path . --host --rede 7983 --arvoreteste

echo.
echo  Encerrado. (a bancada de mesa deu %FALHAS% falha^(s^))
pause
exit /b %FALHAS%
