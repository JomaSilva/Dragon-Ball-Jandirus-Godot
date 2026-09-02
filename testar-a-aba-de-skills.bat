@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- A ABA DE SKILLS DO MENU P

REM ===========================================================================
REM  O PEDIDO DO DONO
REM
REM      testar-a-aba-de-skills.bat [pasta-das-fotos] [pasta-das-fotos-de-antes]
REM
REM  "na parte das skills onde vc escolhe a tree e compra a skills ta mt ruim,
REM   mt simples e pouco intuitivo, deveria ser mais bonito e entendivel"
REM
REM  A bancada (`--diagskills`, ver Client/RoboDeSkills.cs) entra no mundo como
REM  jogador, aperta P, e anda pela aba Learning pelos MESMOS controles que o dedo
REM  aperta -- achados pelo texto na arvore de cena. Ela cobra:
REM
REM    F1   as arvores em CARDS, com a descricao VISIVEL; as que o progresso
REM         vai abrir aparecem apagadas com a condicao escrita; a faixa de
REM         MARCOS e grande e fica fora da rolagem; os verbs de Learning
REM         (mestre/aluno, fusao, ensino, mente) tem lugar na aba.
REM    F2   a arvore em LINHAS POR TIER (baixo em cima), na ordem do catalogo;
REM         tier trancado e uma linha visivel com "invista N marcos".
REM    F3   a FICHA e um painel no tema (moda de pixel contra a paleta), e
REM         comprar por ela muda a faixa e o card na hora.
REM    F4   os quatro estados (sua / compravel / trancada por pre-requisito /
REM         trancada por tier) desenhados DIFERENTES -- borda lida no pixel.
REM    F4b  o MOUSE EM CIMA: o card apontado e o mais forte (borda e chapa de
REM         hover do tema + brilho por fora) e o pre-requisito dele ganha uma
REM         ETIQUETA "requisito" -- nunca a mesma borda noutra cor. Sem mouse,
REM         nenhum dos dois tem marca; apontar o pre-requisito nao acende o
REM         dependente.
REM    F4c  TRANCADA POR DEGRAU: o card (e a ficha) de uma skill que so um
REM         DEGRAU de outra acende (Advanced Ki Awareness <- Basic no nivel
REM         100; sao 32 assim) diz QUEM acende e EM QUE NIVEL -- e nao "sem
REM         acendedor". Contra-exemplos: a trancada por CONDICAO de arvore, a
REM         por PRE-REQUISITO e a por TIER continuam com os textos delas. Isto
REM         so se mede com o cliente NOUTRO processo (ver DOIS PROCESSOS).
REM    F5   esquecer em dois cliques devolve o marco.
REM    F6   comprar o tier 1 inteiro: a linha do tier CONTINUA (o defeito G).
REM    F7   a busca acha skill pelo nome e leva ate ela.
REM    F8   a aba Skills diz tier + categoria + efeito, e nao o `type` cru.
REM    F9   a assinatura de cache, PECA A PECA -- e a injecao que tira uma peca
REM         e reproduz o defeito de origem (a gaveta que nao abria).
REM    F10  a injecao do AcceptDialog de fabrica: a conta de paleta acusa.
REM    F11  determinismo: duas montagens, a mesma ordem, a do catalogo.
REM    F12  a tira ANTES x DEPOIS, se houver a pasta das fotos de antes.
REM
REM  DOIS PROCESSOS (desde 2026-09-02): o servidor sobe DEDICADO (`--server`,
REM  headless, noutro processo) e a bancada DISCA (`--connect 127.0.0.1`). Antes
REM  era `--host` -- servidor e cliente no mesmo processo -- e isso mascarava a
REM  F4c: o registro de niveis e ESTATICO, o servidor o enche no boot, e o
REM  cliente do mesmo processo acertava o texto do card por tabela do vizinho.
REM  So um cliente que disca mede o que o CLIENTE carrega. O servidor e morto no
REM  fim PELO PID DELE, e so ele.
REM
REM  A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`): o dono trabalha no
REM  principal. PRECISA DE JANELA: sem foto as linhas de pixel sao PULADAS.
REM
REM  RESIDUO ZERO: a pasta de usuario do Godot e DESVIADA por APPDATA (pros dois
REM  processos: a variavel e herdada), entao a pasta de saves do dono nao e
REM  tocada -- e a desviada e APAGADA no comeco, senao o save da rodada anterior
REM  deixa o personagem com skills e a F2 mede outro estado (RODADA LIMPA).
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

REM `set SEMBUILD=1` roda o binario que JA esta compilado -- so pra repetir uma
REM rodada (o "antes" de uma tira, por exemplo) sem recompilar; no uso normal a
REM bancada compila sempre, senao mede a DLL de ontem.
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

set "PASTA=%~1"
if "%PASTA%"=="" set "PASTA=%TEMP%\jandirus-bancada-skills\fotos"
set "ANTES=%~2"

set "APPDATA=%TEMP%\jandirus-bancada-skills"
REM RODADA LIMPA: o user:// desviado da rodada anterior morre aqui (so o da
REM bancada; a pasta real do dono nao esta neste caminho).
if exist "%APPDATA%\Godot" rmdir /s /q "%APPDATA%\Godot"
if not exist "%APPDATA%" mkdir "%APPDATA%"
if not exist "%PASTA%" mkdir "%PASTA%"
echo  Saves desviados para: %APPDATA%
echo  Fotos em: %PASTA%

set "ARGANTES="
if not "%ANTES%"=="" set "ARGANTES=--antes "%ANTES%""

set "REDE=7982"
set "LOGSRV=%APPDATA%\servidor.log"

REM ---- 1) o servidor DEDICADO, noutro processo, com o PID guardado ----
REM `--server` le `--port` (o `--rede` e da ponta que disca); `--marcosteste` e
REM `--horateste` sao flags do SERVIDOR, entao vao nele e nao no cliente.
REM O PID vai por ARQUIVO, e nao por `for /f` sobre a saida do PowerShell: o
REM Godot herda o cano de saida, e o `for /f` so devolve quando o cano fecha --
REM ou seja, quando o servidor morre. A primeira rodada travou exatamente ai,
REM com o servidor de pe e o cliente nunca disparado.
REM E o servidor sobe pelo binario SEM console (`..._win64.exe`): o `_console.exe`
REM e so um embrulho que dispara o outro, e o PID que se guarda tem que ser o
REM de quem ABRE A PORTA -- senao a espera abaixo nunca casa e o `taskkill` do
REM fim mata o embrulho e deixa o servidor orfao de pe.
set "GODOTSRV=%GODOT:_console.exe=.exe%"
set "SRVPID="
set "PIDFILE=%APPDATA%\servidor.pid"
if exist "%PIDFILE%" del /q "%PIDFILE%"
powershell -NoProfile -Command "(Start-Process -FilePath '%GODOTSRV%' -ArgumentList '--headless','--path','.','--server','--port','%REDE%','--marcosteste','40','--horateste','0.5' -WorkingDirectory '%CD%' -WindowStyle Minimized -RedirectStandardOutput '%LOGSRV%' -PassThru).Id | Set-Content -Path '%PIDFILE%'"
if exist "%PIDFILE%" set /p SRVPID=<"%PIDFILE%"
if "%SRVPID%"=="" (
    echo  O servidor dedicado nao subiu.
    exit /b 1
)
echo  Servidor dedicado: PID %SRVPID% na porta %REDE% (log em %LOGSRV%)

REM ---- 2) espera a porta UDP abrir NESTE pid (ate 90 s: o boot carrega 23 zonas) ----
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

REM ---- 3) a bancada, discando ----
"%GODOT%" --path . --connect 127.0.0.1 --rede %REDE% --diagskills --semfoco ^
          --pasta "%PASTA%" %ARGANTES% --position 1920,0 --resolution 1280x720 ^
          --raca Saiyan --conta bancada_skills --nome Bancada
set "CODIGO=%errorlevel%"

REM ---- 4) mata SO o servidor desta rodada, pelo PID ----
taskkill /PID %SRVPID% /F >nul 2>nul
echo.
echo  Encerrado (codigo %CODIGO%). Procure o placar "[skills]  PLACAR" acima e as fotos em %PASTA%.
echo  Servidor (PID %SRVPID%) encerrado; log dele em %LOGSRV%.
exit /b %CODIGO%
