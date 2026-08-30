@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada da ABA NAV

REM ===========================================================================
REM  A ABA NAV E A CARTA ESTELAR  (--diagnav)
REM
REM     testar-nav.bat            sem janela (o normal)
REM     testar-nav.bat janela     com janela, NO SEGUNDO MONITOR
REM
REM  O pedido do dono, literal:
REM     "a aba de nav system no menu 'P' so deve aparecer caso o jogador tenha
REM      o item nav system sem seu inventario."
REM     (o "sem" e erro de digitacao: e "em seu inventario")
REM
REM  ---------------------------------------------------------------------------
REM  O PORTAO, E AS DUAS METADES QUE ELE EXIGE
REM
REM  A bancada NASCE SEM O ITEM -- mochila vazia, zero zeni, tech zero -- e a
REM  primeira coisa que ela cobra e que a aba NAO exista. So depois o item entra.
REM  Uma bancada que nascesse com o Nav System nunca testaria a ENTRADA no estado
REM  e ficaria verde sem portao nenhum (foi exatamente o que acontecia antes: a
REM  linha "a aba Nav existe fora do espaco" passava com a mochila vazia, ou
REM  seja, ela PROVAVA o bug em vez de pega-lo).
REM
REM  Cada metade e medida em TRES camadas que nao se cobrem:
REM     a mochila (o que o servidor guarda), o bit `Poder.Nav` (o que o servidor
REM     CONTA pelo fio) e a barra de abas (o que o jogador VE no menu P).
REM
REM  E tem o CONTRA-EXEMPLO, que e o que separa a garantia viva da decoracao: o
REM  servidor acende `Poder.Nav` por dentro (pelo caminho do admin/cargo) com o
REM  item FORA da mochila, e mesmo assim o bit nao pode sair pelo fio. Sem esse
REM  passo, trocar o portao inteiro por "return pl.Poderes" daria o mesmo placar.
REM
REM  E o relog: grava e le do disco pra afirmar que o item continua na mochila --
REM  a aba pende dele, e "a mochila e salva" era ate entao afirmacao minha.
REM  ---------------------------------------------------------------------------
REM
REM  O RESTO DA BANCADA e a carta estelar em si (ela ja existia): quantos mundos
REM  o enquadramento cobre, se o cliente enumera planetas sozinho sem um byte de
REM  rede, o custo da varredura, clicar, viajar, a tela do sistema e a roda do
REM  mouse nas duas telas.
REM
REM  ============================================================================
REM  A PASTA DE SAVES DO DONO NAO E TOCADA -- E E ESTA LINHA QUE GARANTE ISSO.
REM
REM  O servidor grava as contas e o `mundo.json` dentro do `user://`, que no
REM  Windows e %APPDATA%\Godot\app_userdata\<projeto>. Esta bancada CRIA
REM  personagem, poe e tira item, decola, morre no vacuo e grava no disco --
REM  rodar isso na pasta real do dono e estrago, e ja aconteceu neste projeto.
REM
REM  Entao APPDATA e desviada pra uma pasta de rascunho ANTES de o Godot subir.
REM  Nao ha uma linha de codigo envolvida: o proprio Godot resolve o `user://` a
REM  partir dela. A pasta desviada fica onde esta (nao e apagada) pra dar pra
REM  conferir o que a rodada escreveu.
REM  ============================================================================
REM
REM  A JANELA, QUANDO PRECISAR DELA, VAI PRO SEGUNDO MONITOR (--position 1920,0):
REM  o dono trabalha no principal. Com janela a bancada ainda tira as fotos da
REM  carta, que no headless saem vazias ("headless nao renderiza").
REM
REM  PORTA PROPRIA (7965): se aparecer "FALHOU ao abrir a porta", ha outra rodada
REM  viva -- feche-a.
REM ===========================================================================

cd /d "%~dp0"

set MODO=%1

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
echo     testar-nav.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7965

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

REM ---- o desvio da pasta de usuario, e ele vem ANTES do Godot ----
set "APPDATA=%TEMP%\jandirus-bancada-nav"
if not exist "%APPDATA%" mkdir "%APPDATA%"
echo.
echo  Saves desviados para: %APPDATA%
echo  (a pasta real do dono nao e tocada por esta rodada)

REM CONTA NOVA a cada rodada, pelo relogio: a bancada nasce SEM o item de
REM proposito, e reaproveitar um personagem que ja tem Nav System na mochila
REM faria a primeira metade do portao passar por engano.
set CONTA=nav%RANDOM%

echo.
echo  ================================================================
echo   Procure a ultima linha:  [nav] ===== TUDO OK =====
echo   (ou "===== N FALHA(S) =====", com as linhas vermelhas listadas)
echo   A rodada leva cerca de dois minutos.
echo  ================================================================
echo.

if /i "%MODO%"=="janela" goto :comjanela

"%GODOT%" --headless --path . --host --rede 7965 --diagnav ^
          --conta %CONTA% --nome Piloto
goto :fim

:comjanela
echo  Com janela, NO SEGUNDO MONITOR (--position 1920,0).
"%GODOT%" --path . --position 1920,0 --host --rede 7965 --diagnav ^
          --conta %CONTA% --nome Piloto

:fim
echo.
echo  Encerrado. O que a rodada escreveu esta em
echo     %APPDATA%\Godot\app_userdata\Dragon ball Jandirus
pause
