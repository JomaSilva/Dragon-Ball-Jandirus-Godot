@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- INSTALAR com DOIS CORPOS

REM ===========================================================================
REM  DOIS PROCESSOS: QUEM INSTALA E QUEM OLHA  (--diaginstalar + --olhoinstalar)
REM
REM     testar-instalar-dois.bat
REM
REM  A `testar-instalar.bat` mede o ciclo inteiro com UM cliente. Duas frases do
REM  pedido do dono NAO CABEM num cliente so, porque as duas sao sobre a tela de
REM  OUTRA PESSOA:
REM
REM     "...uma versao transparente dele vai ficar no mouse
REM      (ISSO CLARAMENTE SO APARECE PRO JOGADOR LOCAL)
REM      ao clicar o objeto vai ser instalado nesse local
REM      (e nesse momento q o server vai sincronizar com o resto dos jogadores)
REM      e TODOS VAO PODER VER."
REM
REM  Um processo so pode dizer "a MINHA lista nao mudou" e "nenhum pacote saiu
REM  do MEU funil" -- e dai INFERIR que ninguem viu. Inferencia e exatamente o
REM  lugar onde este projeto ja escondeu bug.
REM
REM  Entao aqui sobem DOIS clientes de verdade, cada um com soquete, conta e
REM  corpo proprios:
REM
REM    A  --diaginstalar   hospeda e ANDA o ciclo (fabricar, segurar a previa,
REM                        clicar, recolher). Ele grita MARCOS pelo canal OOC.
REM    B  --olhoinstalar   nao fabrica, nao clica: OLHA. Nos marcos, ele afirma
REM                        o que ve -- e o que NAO ve.
REM
REM  O QUE O B AFIRMA (leia o placar "[olho] ===== N OK"):
REM     O.0-O.2  sanidade: ele esta conectado, esta no mundo, e VE o corpo do A.
REM              Sem isso tudo o que vem depois ficaria verde por ausencia.
REM     O.3-O.4  enquanto a previa estava no mouse do A, NADA mudou na lista nem
REM              no desenho do B  -> a previa e local (regra 3).
REM     O.5      antes do clique, o B NAO via aquela bancada naquela celula.
REM     O.6-O.7  depois do clique, ele VE -- na lista e como desenho na tela
REM              -> "todos vao poder ver" (regra 4).
REM     O.8      e quando o A recolheu, sumiu pra ele tambem.
REM     O.9-O.10 os marcos chegaram e o canal de construcoes entregou lista pelo
REM              menos uma vez  -> "nada mudou" nao e o silencio de um fio morto.
REM
REM  ============================================================================
REM  A PASTA DE SAVES DO DONO NAO E TOCADA. O APPDATA e desviado ANTES de os dois
REM  Godot subirem -- o `user://` sai dele, sem uma linha de codigo envolvida.
REM  ============================================================================
REM
REM  PORTA PROPRIA (7977): se aparecer "FALHOU ao abrir a porta", ha outra rodada
REM  viva -- feche-a.
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
echo     testar-instalar-dois.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7977

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    REM `-t:Rebuild` e OBRIGATORIO: o build incremental ja deu "compilacao com
    REM exito" sem trocar a DLL, e o Godot subiu com o binario de ontem.
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

set "APPDATA=%TEMP%\jandirus-bancada-instalar-dois"
if not exist "%APPDATA%" mkdir "%APPDATA%"
echo.
echo  Saves desviados para: %APPDATA%
echo  (a pasta real do dono nao e tocada por esta rodada)

echo.
echo  ---- A (hospeda e instala) ----
REM `--instalaratraso 18` da ao B tempo de conectar E de entrar na zona antes de
REM o roteiro comecar. Sem isso o B perderia os primeiros marcos, e "ele nao viu
REM nada" ficaria verde por ausencia -- o pior jeito de ficar verde.
start "INSTALAR-A" /min "%GODOT%" --headless --path . --host --rede 7977 --techteste ^
      --diaginstalar --instalaratraso 18 ^
      --raca Human --conta bancada_inst_a --nome InstaladorA

echo  ---- esperando o servidor abrir a porta ----
timeout /t 6 /nobreak >nul

echo  ---- B (so olha) ----
REM `--connect 127.0.0.1` e o que faz o B DISCAR: sem `--host` ele nao tem alvo
REM e para na tela de login -- foi assim que a primeira rodada terminou com o
REM placar do B vazio (e placar vazio se le como sucesso).
"%GODOT%" --headless --path . --connect 127.0.0.1 --rede 7977 --olhoinstalar ^
          --raca Human --conta bancada_inst_b --nome OlheiroB

echo.
echo  Encerrado. O placar do A esta na janela "INSTALAR-A" (minimizada);
echo  o do B esta acima. Feche a janela do A se ela tiver ficado de pe.
echo     %APPDATA%\Godot\app_userdata\Dragon ball Jandirus
pause
