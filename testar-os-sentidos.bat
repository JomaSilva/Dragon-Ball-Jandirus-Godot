@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada dos SENTIDOS (a aba Sense / Scan)

REM ===========================================================================
REM  A ABA SENSE/SCAN PELO FUNIL DO SERVIDOR
REM
REM     testar-os-sentidos.bat
REM
REM  UMA BANCADA, sem janela (`--sentidosteste`, ver Server/GameServer.SentidosTeste.cs):
REM
REM   1. os TRES ALCANCES do Sense (Sense2.0.dm:20-35) e quem fica de fora de
REM      cada um -- os 15 tiles, o escondido, o Android, o piso de 5 de BP, o
REM      NPC, o piso de 5 milhoes da galaxia; o nome so de quem se conhece; o
REM      poder RELATIVO; o rumo do get_dir;
REM   2. o SCOUTER (Scan): a area inteira com BP EXATO e coordenadas, NPC so
REM      se for chefe, o scouter vencendo a skill;
REM   3. o SIGILO NO FIO: o pacote aberto com o leitor do cliente -- no Sense
REM      todo BP e NaN, no Scan e o numero, nenhum byte sobrando;
REM   4. o REENVIO: a 1 Hz, so pra quem sente, e so quando a lista muda.
REM
REM  DIFERENTE DA `testar-a-mente.bat`, o servidor NAO fica de pe esperando
REM  Ctrl+C: este .bat vigia o log ate o placar aparecer, mata SO o PID que
REM  ele subiu, e imprime as linhas "[sentidos]" no fim. (A bancada roda no
REM  BOOT, e um `Quit()` de dentro do boot nao fecha o processo -- o boot segue,
REM  povoa o mundo e abre a porta -- entao quem desliga e este arquivo.)
REM
REM  RESIDUO ZERO: a pasta de usuario do Godot e DESVIADA por APPDATA, entao
REM  a pasta de saves do dono nao e tocada.
REM
REM  PORTA PROPRIA (7996): se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- a bancada roda mesmo assim (ela e do boot).
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
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%

REM `set SEMBUILD=1` roda o binario que JA esta compilado (so pra repetir uma
REM rodada); no uso normal a bancada compila sempre, senao mede a DLL de ontem.
if "%SEMBUILD%"=="1" (
    echo  SEMBUILD=1: sem recompilar -- medindo a DLL que ja esta ai.
    goto :compilado
)
where dotnet >nul 2>nul
if not %errorlevel%==0 (
    echo  Precisa do dotnet no PATH.
    pause
    exit /b 1
)
echo  Compilando o jogo...
REM `-t:Rebuild` e OBRIGATORIO: o build incremental ja deu "compilacao com
REM exito" sem trocar a DLL, e o Godot subiu com o binario de ontem.
dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
if errorlevel 1 (
    echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
    pause
    exit /b 1
)
:compilado

set "REDE=7996"
set "APPDATA=%TEMP%\jandirus-bancada-sentidos"
if exist "%APPDATA%\Godot" rmdir /s /q "%APPDATA%\Godot"
if not exist "%APPDATA%" mkdir "%APPDATA%"
set "LOG=%APPDATA%\sentidos.log"
set "LOGERR=%APPDATA%\sentidos.err.log"
set "PIDFILE=%APPDATA%\servidor.pid"
if exist "%PIDFILE%" del /q "%PIDFILE%"
echo  Saves desviados para: %APPDATA%

REM O servidor sobe pelo binario SEM console (o `_console.exe` e so um embrulho
REM que dispara o outro, e o PID que se guarda tem que ser o de quem roda).
REM As FALHAS saem por stderr (GD.PrintErr), entao os dois canos vao pra disco.
REM
REM O POWERSHELL VIGIA O LOG, um segundo por vez, ate a linha do placar
REM ("N passaram, M falharam") aparecer -- ai mata SO o PID que subiu. Se o
REM processo morrer antes (crash) ou o placar nao vier em 10 min, tambem mata
REM e sai com 2 (sem placar) ou 3 (tempo esgotado). O VEREDITO SAI DO PLACAR,
REM e nao do codigo de saida do processo: 0 = tudo verde, 1 = houve falha.
set "GODOTSRV=%GODOT:_console.exe=.exe%"
echo.
echo  ---- a aba Sense/Scan pelo funil do servidor (--sentidosteste, porta %REDE%) ----
echo.
powershell -NoProfile -Command "$p = Start-Process -FilePath '%GODOTSRV%' -ArgumentList '--headless','--path','.','--host','--rede','%REDE%','--sentidosteste' -WorkingDirectory '%CD%' -WindowStyle Minimized -RedirectStandardOutput '%LOG%' -RedirectStandardError '%LOGERR%' -PassThru; $null = $p.Handle; $p.Id | Set-Content -Path '%PIDFILE%'; $fim = (Get-Date).AddSeconds(600); $m = $null; while ((Get-Date) -lt $fim) { if (Test-Path '%LOG%') { $m = Select-String -Path '%LOG%' -Pattern 'passaram, (\d+) falharam' | Select-Object -Last 1 }; if ($m -or $p.HasExited) { break }; Start-Sleep -Seconds 1 }; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }; if (-not $m) { if ((Get-Date) -ge $fim) { exit 3 } else { exit 2 } }; if ($m.Matches[0].Groups[1].Value -eq '0') { exit 0 } else { exit 1 }"
set "CODIGO=%errorlevel%"

echo.
findstr /C:"[sentidos]" "%LOG%" 2>nul
findstr /C:"[sentidos]" "%LOGERR%" 2>nul
echo.
if "%CODIGO%"=="3" echo  A bancada NAO fechou sozinha em 10 min -- o PID foi morto. Leia %LOG% e %LOGERR%.
if "%CODIGO%"=="2" echo  O placar "[sentidos] ==== N passaram, M falharam ====" NAO apareceu no log -- a bancada nem rodou?
echo  Encerrado (codigo %CODIGO%: 0 = tudo verde, 1 = houve falha). Log em %LOG% (e as falhas em %LOGERR%).
exit /b %CODIGO%
