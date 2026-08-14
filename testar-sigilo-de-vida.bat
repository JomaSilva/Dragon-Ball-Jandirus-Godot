@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do SIGILO DA VIDA ALHEIA (dois corpos)

REM ===========================================================================
REM  A VIDA DO OUTRO NAO E UM NUMERO  (--vida a + --vida b)
REM
REM     testar-sigilo-de-vida.bat            (rodada de 90 s)
REM     testar-sigilo-de-vida.bat 120        (rodada mais longa)
REM
REM  O pedido do dono: "tire a barra de vida q aparece acima da cabeca dos
REM  personagens, n deveria dar pra ver o hp dos outros, so ter uma ideia com
REM  base nos FERIMENTOS".
REM
REM  Isto NAO se testa apagando um desenho. A pergunta e que INFORMACAO existe
REM  no jogo, e ela precisa de DUAS telas: uma que apanha e sabe a propria vida,
REM  outra que olha aquele corpo e nao pode saber. Num processo so os dois lados
REM  sao a mesma memoria.
REM
REM  O QUE CADA PAPEL FAZ
REM     --vida a   (host)  apanha, se cura no meio da rodada e mede a PROPRIA
REM                        vida. Escreve o que mediu em
REM                        %%APPDATA%%\Godot\app_userdata\...\sigilo-do-ferido.txt
REM     --vida b   (2o)    olha aquele corpo: ve a amputacao, ve ela sumir na
REM                        cura (sem ter dado um soco), depois bate pra ver o
REM                        grau subir de novo. E quem tem o placar grande.
REM
REM  AS SEIS FAMILIAS
REM     1. a vida alheia NAO chega como numero (formato + corpo remoto + medida)
REM     2. a MINHA vida chega, fina, e a barra da HUD segue ela  (contra-exemplo)
REM     3. o GRAU de ferida viaja, muda com o dano e CAI na cura
REM     4. membro arrancado some no corpo alheio -- e volta quando ele se cura
REM     5. nao sobrou desenho sobre cabeca nenhuma (nem a sua)
REM     6. o respingo de sangue, que lia a vida alheia, continua vivo lendo o GRAU
REM
REM  AS FLAGS DE SERVIDOR NAO SAO ENFEITE:
REM     --feridateste     todo corpo nasce com um braco e uma perna ARRANCADOS.
REM                       E o caso pronto da familia 4: decepar de verdade exige
REM                       golpe letal em membro ja zerado, e bancada que so as
REM                       vezes arranca um braco nao mede nada nas outras vezes.
REM     --bpteste 3000    o BP de nascimento e sorteado. Numa rodada o olhador
REM                       ficou tao mais fraco que acertou 2 de 136 golpes -- a
REM                       bancada estava medindo o sorteio, e nao o canal.
REM     --esquivateste .1 o HOST (que aqui e o FERIDO) entra com 1/10 do BP. Com
REM                       os dois iguais, a pancada nao passava do hematoma e a
REM                       familia 6 reprovava por falta de estrago.
REM
REM  PORTA PROPRIA (7992): enquanto outro --host estiver no ar nenhum sobe na
REM  mesma porta. Se aparecer "[server] FALHOU ao abrir a porta 7992", ha outra
REM  rodada viva -- feche-a.
REM
REM  CONTAS PROPRIAS (bancada_vida_a / bancada_vida_b). Nada toca conta de jogador.
REM
REM  PROCURE, no fim de cada janela:
REM     ===== [vida-a] N OK, M FALHA =====
REM     ===== [vida-b] N OK, M FALHA =====
REM  O placar do A entra no placar do B (a familia 2 e a regua da familia 1): se
REM  o processo do ferido nao subir, o do olhador REPROVA em vez de ficar verde.
REM ===========================================================================

cd /d "%~dp0"

set FIM=%1
if "%FIM%"=="" set FIM=90

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
echo     testar-sigilo-de-vida.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7992  (dois processos: ferido/host + olhador)
echo  Fim   : %FIM% s

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ================================================================
echo   O OLHADOR entra 12 s depois (ele precisa achar a porta aberta).
echo   O ferido se cura ~16 s depois de ver o olhador; dai a pancada.
echo   A bancada fecha sozinha.
echo  ================================================================
echo.

REM O ATRASO E `ping` E NAO `timeout`: o `timeout` LE do teclado e morre com
REM "Input redirection is not supported" em qualquer automacao (ver testar-mente.bat).
REM Sem o separador "--": com ele os argumentos vao parar em GetCmdlineUserArgs()
REM e as flags de bancada saem MUDAS (ver servidor.bat).
start "sigilo-olhador" /min cmd /c "ping -n 13 127.0.0.1 >nul & ""%GODOT%"" --headless --path . --rede 7992 --connect 127.0.0.1 --vida b --vidaalvo Ferido --vidafim %FIM% --raca Human --conta bancada_vida_b --nome Olhador"

"%GODOT%" --headless --path . --host --rede 7992 --feridateste --bpteste 3000 --esquivateste 0.1 ^
          --vida a --vidaalvo Olhador --vidafim %FIM% ^
          --raca Human --conta bancada_vida_a --nome Ferido

REM O olhador nao sabe que acabou (ele so ve a conexao cair). Pela LINHA DE
REM COMANDO e nao por titulo: o console.exe relanca a si mesmo num filho que
REM nasce fora da janela que o `start` nomeou, e o filho e quem segura a porta.
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*bancada_vida_b*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul
taskkill /F /FI "WINDOWTITLE eq sigilo-olhador*" >nul 2>nul

echo.
echo  Encerrado.
pause
