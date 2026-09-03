@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- O VERB DE DEGRAU VIRA BOTAO (cliente que disca)

REM ===========================================================================
REM  O BOTAO DO VERB CONCEDIDO POR DEGRAU (E POR CASA), NUM CLIENTE QUE DISCA
REM
REM      testar-o-degrau-no-cliente.bat
REM
REM  O DEFEITO DE ORIGEM: um verb concedido por NIVEL (o Hokuto Hyakuretsu Ken
REM  no nivel 2 do Hokuto no Shinken) ou por CASA (o Taunt da Holy Trinity)
REM  tinha corpo no servidor, porta no niveis.json e NENHUM botao no cliente --
REM  o menu montava os botoes so do catalogo, e o nivel das skills nao viaja.
REM  Um verb sem botao e inalcancavel. Hoje o servidor manda os verbs ATIVOS
REM  no S2C.Skills e o botao nasce deles (Client/Habilidades.cs, DasSkills).
REM
REM  A bancada (`--diagdegrau`, ver Client/RoboDoDegrau.cs) e HEADLESS e mede
REM  o REGISTRO de verbs (de onde o menu P tira os botoes) e a RESPOSTA do
REM  servidor ao apertar. Tres familias:
REM     F1  o verb de degrau (Hokuto) vira botao aceso e responde; o
REM         Revenge_Demon (nao concedido) nao tem botao
REM     F2  a Trindade ponta a ponta: COMPRA pelo funil, ESCOLHE a casa, SO o
REM         verb da casa vira botao e responde; a Grace segue sem pedir casa
REM     F3  o efeito sem desenho (a divida nomeada): o id `timefreeze` chega,
REM         o cliente o recebe e nao estoura
REM
REM  DOIS PROCESSOS pelo motivo da `testar-a-aba-de-skills.bat`: so um cliente
REM  que DISCA mede o que chega pelo FIO. O servidor e morto no fim PELO PID
REM  DELE, e so ele. A pasta de usuario do Godot e DESVIADA (APPDATA) e apagada
REM  no comeco: a pasta de saves do dono nao e tocada.
REM
REM  CODIGO DE SAIDA = 0 com o placar limpo, 1 com falha, 2 se o mundo nao chegou.
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
echo  Nao encontrei o Godot. set GODOT=C:\caminho\Godot_v4.7.1-stable_mono_win64_console.exe
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%

if "%SEMBUILD%"=="1" (
    echo  SEMBUILD=1: sem recompilar -- medindo a DLL que ja esta ai.
    goto :compilado
)
where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    REM `-t:Rebuild` e OBRIGATORIO: o build incremental ja deu "compilacao com
    REM exito" sem trocar a DLL, e o Godot subiu com o binario de ontem.
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)
:compilado

set "APPDATA=%TEMP%\jandirus-bancada-degrau"
if exist "%APPDATA%\Godot" rmdir /s /q "%APPDATA%\Godot"
if not exist "%APPDATA%" mkdir "%APPDATA%"
echo  Saves desviados para: %APPDATA%

set "REDE=7981"
set "LOGSRV=%APPDATA%\servidor.log"

REM ---- 1) o servidor DEDICADO, noutro processo, com o PID guardado ----
REM `--skillteste` da o Freeze e o Hokuto no Shinken; `--nivelteste` poe no nivel 2 o Hokuto (que
REM ele ja tem) e a Trindade QUANDO ELA FOR COMPRADA -- nao concede nada, senao a F2 mediria uma
REM compra que nao houve; `--marcosteste 60` paga as compras do Corpo/Bodybuilding ate a Grace.
REM O binario SEM console (`..._win64.exe`): o `_console.exe` e um embrulho, e o PID guardado
REM tem que ser o de quem ABRE A PORTA.
set "GODOTSRV=%GODOT:_console.exe=.exe%"
set "SRVPID="
set "PIDFILE=%APPDATA%\servidor.pid"
if exist "%PIDFILE%" del /q "%PIDFILE%"
powershell -NoProfile -Command "(Start-Process -FilePath '%GODOTSRV%' -ArgumentList '--headless','--path','.','--server','--port','%REDE%','--marcosteste','60','--skillteste','/datum/skill/general/timefreeze,/datum/skill/Assassain/Hokuto_no_Shinken','--nivelteste','/datum/skill/Assassain/Hokuto_no_Shinken=2,/datum/skill/Bodybuilding/TheHolyTrinity=2' -WorkingDirectory '%CD%' -WindowStyle Minimized -RedirectStandardOutput '%LOGSRV%' -PassThru).Id | Set-Content -Path '%PIDFILE%'"
if exist "%PIDFILE%" set /p SRVPID=<"%PIDFILE%"
if "%SRVPID%"=="" (
    echo  O servidor dedicado nao subiu.
    exit /b 1
)
echo  Servidor dedicado: PID %SRVPID% na porta %REDE% (log em %LOGSRV%)

REM ---- 2) espera a porta UDP abrir NESTE pid (ate 90 s) ----
set /a ESPERA=0
:espera
netstat -ano -p UDP | findstr /C:":%REDE% " | findstr /R /C:" %SRVPID%$" >nul 2>nul
if %errorlevel%==0 goto :subiu
set /a ESPERA+=1
if %ESPERA% geq 90 (
    echo  O servidor nao abriu a porta %REDE% em 90 s -- matando o PID %SRVPID%.
    taskkill /PID %SRVPID% /F >nul 2>nul
    exit /b 1
)
timeout /t 1 /nobreak >nul
goto :espera
:subiu
echo  Porta %REDE% aberta pelo PID %SRVPID% depois de %ESPERA% s.

REM ---- 3) a bancada, discando, sem janela ----
"%GODOT%" --headless --path . --connect 127.0.0.1 --rede %REDE% --diagdegrau ^
          --raca Saiyan --conta bancada_degrau --nome Degrau
set "CODIGO=%errorlevel%"

REM ---- 4) mata SO o servidor desta rodada, pelo PID ----
taskkill /PID %SRVPID% /F >nul 2>nul
echo.
echo  Encerrado (codigo %CODIGO%). Procure o placar "[degrau]  PLACAR" acima.
echo  Servidor (PID %SRVPID%) encerrado; log dele em %LOGSRV%.
exit /b %CODIGO%
