@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do DESVIO (som + efeito de esquiva)

REM ===========================================================================
REM  O DESVIO, COM DOIS CORPOS  (--diagdesvio + --esquivateste)
REM
REM     testar-desvio.bat
REM
REM  O pedido do dono: "falta o SOM do dodge e o EFEITO DE DESVIO q tinha no
REM  byond". Esta bancada NAO chama o efeito na mao -- isso provaria o efeito, e
REM  o que estava em duvida e se a ESQUIVA o aciona. Ela poe dois lutadores no
REM  mesmo servidor e fotografa o que a tela mostra quando o soco nao chega.
REM
REM  DOIS PROCESSOS, e o de cima TEM JANELA: `GetViewport().GetTexture()` volta
REM  vazio no headless, e sem foto nao ha veredito. O adversario e headless.
REM
REM  DESNIVEL DE PODER (--esquivateste 10): entre iguais o soco acerta 100% das
REM  vezes, e isso e a regra do DM ("two perfectly matched players will hit 100%
REM  of the time", CombatMovement.dm:190). Sem desnivel nao ha esquiva NENHUMA
REM  pra fotografar. O host entra 10x mais forte -- ele so apanha e fotografa.
REM
REM  O QUE SAI, na pasta user:// (%APPDATA%\Godot\app_userdata\...):
REM     desvio-chao-1-antes / -2-durante / -3-depois  (+ -zoom de cada uma)
REM     desvio-ar-1-antes   / -2-durante / -3-depois  (+ -zoom de cada uma)
REM     desvio-acerto.png   / -zoom.png    um acerto, mesma camera e mesmo atraso
REM     desvio-troca-longa.png             depois de dezenas de esquivas
REM  E no console:  [desvio] ===== N FALHA(S) =====
REM
REM  O TRIPTICO E O PEDIDO INTEIRO: o dono nao pediu um instante, pediu uma
REM  sequencia -- o corpo some, as listras pretas aparecem no lugar dele, e o
REM  corpo volta. Uma foto so no auge mostra o meio da frase e cala o fim, que e
REM  justamente a parte que nao pode dar errado (corpo que some e nao volta).
REM
REM  E ELE SAI DUAS VEZES, no chao e NO AR (--vooteste + --socarvoando 22): as
REM  listras nascem onde o corpo e DESENHADO, e voando o desenho sobe ate 160 px
REM  acima do no. Os DOIS sobem porque a regra de alcance por altura e
REM  assimetrica -- quem esta no chao nao acerta quem paira, e sem golpe nao ha
REM  esquiva nenhuma pra fotografar la em cima.
REM
REM  PORTA PROPRIA (7986): enquanto outro --host estiver no ar nenhum sobe na
REM  mesma porta. Se aparecer "[server] FALHOU ao abrir a porta 7986", ha outra
REM  rodada viva -- feche-a.
REM
REM  CONTAS PROPRIAS: a bancada empurra BP e apanha ate cansar. Nada toca conta
REM  de jogador.
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
echo     testar-desvio.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7986  (dois processos: alvo com janela + socador headless)

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
echo   O SOCADOR entra 12 s depois (ele precisa achar a porta aberta).
echo   Aos 22 s os DOIS sobem, e a briga continua no ar.
echo   A bancada roda 80 s e fecha sozinha.
echo   Procure:  [desvio] ===== ... =====
echo  ================================================================
echo.

REM O ATRASO E `ping` E NAO `timeout`: o `timeout` LE do teclado e morre com
REM "Input redirection is not supported" em qualquer automacao. Ver testar-mente.bat.
REM `--socaralvo DesvioA`: o berco tem NPC, e "o primeiro corpo do snapshot" e um
REM deles -- sem isto o socador marca o Krillin, vai bater NELE e passa a rodada
REM nocauteado. Foi o que aconteceu na primeira rodada desta bancada.
start "desvio-socador" /min cmd /c "ping -n 13 127.0.0.1 >nul & ""%GODOT%"" --headless --path . --rede 7986 --connect 127.0.0.1 --socar --socaralvo DesvioA --socarperto 56 --socarvoando 22 --raca Human --conta bancada_desvio_b --nome DesvioB"

REM SEM --headless: a foto e o juiz. Sem o separador "--", senao as flags vao
REM parar em GetCmdlineUserArgs() e a bancada sobe muda (ver servidor.bat).
REM
REM `--bpteste` ANTES do `--esquivateste`: o BP de nascimento varia muito (8,1
REM num e 1,7 no outro na primeira rodada), e um desnivel que o sorteio decide
REM nao e desnivel medido. Com os dois no mesmo numero a razao e EXATAMENTE 10 --
REM que e o que a pontaria le.
REM
REM E O NUMERO E GRANDE POR CAUSA DO VOO, nao da pancada: o teto do Ki sai de
REM `100 * log10(BP)^2,3 * KiUnlockPercent` e o voo cobra 6 de Ki por segundo. Com
REM `--bpteste 100` o tanque dava 8 s de ar e os DOIS caiam de exaustao antes de o
REM primeiro soco chegar la em cima -- a rodada anterior mediu exatamente isso
REM (0 esquiva no ar, os dois de volta ao chao). O `--kiteste` entra pelo mesmo
REM motivo: ele poe o `KiUnlockPercent` no maximo.
REM `--vooteste`: da a skill de voo pros DOIS (e flag de servidor, vale pra quem
REM entrar). Sem ela o `voar` e recusado, os dois ficam no chao e o triptico do
REM ar nao sai -- e a bancada reprova dizendo exatamente isso.
REM
REM `--horateste 0.5`: MEIO-DIA. Nao e enfeite -- aqui a FOTO e o juiz, e a hora
REM do mundo e sorteada a cada rodada: duas das quatro primeiras cairam de noite
REM e as listras pretas do Zanzoken sairam (3,3,5) em vez de (0,0,0), com o verde
REM do chao em (13,22,12). Nada estava errado no efeito -- era o ceu por cima de
REM tudo --, mas uma foto assim nao responde "as linhas sao PRETAS?" pra ninguem.
"%GODOT%" --path . --host --rede 7986 --kiteste --bpteste 100000 --esquivateste 10 --vooteste --horateste 0.5 --diagdesvio ^
          --raca Human --conta bancada_desvio_a --nome DesvioA

REM O socador nao sabe que acabou (ele so ve a conexao cair). Pela LINHA DE
REM COMANDO e nao por titulo: o console.exe relanca a si mesmo num filho que
REM nasce fora da janela que o `start` nomeou, e o filho e quem segura a porta.
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*bancada_desvio_b*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul
taskkill /F /FI "WINDOWTITLE eq desvio-socador*" >nul 2>nul

echo.
echo  Encerrado.
pause
