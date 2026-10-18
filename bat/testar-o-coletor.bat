@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- as pausas do coletor do .NET, medidas de dentro

REM ===========================================================================
REM  O COLETOR DO .NET, MEDIDO DE DENTRO DO PROCESSO  (--diagcoletor)
REM
REM     testar-o-coletor.bat
REM
REM  O DEFEITO: com o corpo parado num campo da Terra e nada acontecendo, o jogo
REM  parava 38 a 44 ms a cada 0,37 s -- uma coleta de geracao 1 do .NET quase
REM  tres vezes por segundo, mais de 10% do tempo com o processo parado e quatro
REM  a seis quadros perdidos de cada vez.
REM
REM  DE QUEM ERA (medido por esta bancada em 2026-10-08, 41 corpos na tela):
REM     o servidor sozinho .......... 1 pausa de 5 ms em 20 s: nao era dele
REM     o cliente ................... 42 MB/s de lixo, 97% do relogio de animacao
REM                                   do `CharacterVisual`
REM  POR QUE CADA COLETA CUSTAVA 40 ms: o relogio perguntava a animacao e os
REM  tempos dela ao Godot em todo quadro, por camada -- 332 mil `StringName` por
REM  segundo. Cada `StringName` criado em C# e tres objetos (ele, com finalizador,
REM  e a entrada dele na tabela de descartaveis do GodotSharp), e os tres
REM  SOBREVIVEM a primeira coleta: 13 dos 16 MB da geracao 0 subiam de geracao
REM  a cada vez.
REM
REM  DEPOIS: o processo aloca 0,33 MB/s (eram 43), a coleta passa de 2,7 por
REM  segundo pra uma por minuto (4 a 8 ms), e o script do quadro cai de ~5 ms
REM  pra 1,4. Os numeros de cada rodada saem no relatorio.
REM
REM  ---------------------------------------------------------------------------
REM  SAO CINCO RODADAS, e cada uma e um processo:
REM
REM    1) o HOST (cliente + servidor) parado, SEM ouvinte: a REGUA limpa -- bytes
REM       por corpo por quadro na cena, bytes por corpo por tique no servidor,
REM       %% do tempo parado e a pausa mediana.
REM    2) o mesmo, com o ouvinte verboso: os TIPOS que ainda sao alocados, por
REM       fase do quadro, e os objetos finalizados por segundo.
REM    3) o SERVIDOR DEDICADO sozinho, sem janela: a parte dele da conta.
REM    4) O CONTRA-EXEMPLO da cena: `--coletordefeito relogio`
REM       (`CharacterVisual.CompassoVencidoDeTeste`). A regua TEM que reprovar a
REM       cena -- e a rodada termina em TUDO OK justamente porque reprovou:
REM       procure "(defeito injetado: ...) a mesma regua REPROVA".
REM    5) O CONTRA-EXEMPLO do servidor: `--coletordefeito piloto`
REM       (`GameServer.PilotoPorFechoDeTeste`).
REM
REM  Cada rodada termina sozinha, com
REM       [coletor] ===== TUDO OK =====        ou        ===== N FALHA(S) =====
REM
REM  (A sexta pergunta -- o CLIENTE PURO ligado a um servidor dedicado -- sao dois
REM  processos, e as duas linhas de comando estao no cabecalho de
REM  Client\RoboDoColetor.cs. `--coletornocaute` no cliente desliga o `_Process`
REM  de uma classe de node por vez e diz quanto cada uma aloca.)
REM
REM  ---------------------------------------------------------------------------
REM  AS CHAVES DO COLETOR NAO ESTAO AQUI DE PROPOSITO. Rodando pelo editor do
REM  Godot o .NET sobe pelo `GodotPlugins.runtimeconfig.json` DO GODOT, e nao pelo
REM  do projeto -- o que se escreve no .csproj so vale no jogo EXPORTADO. Pra
REM  medir uma chave aqui ela entra por variavel de ambiente antes de rodar:
REM       set DOTNET_gcConcurrent=0
REM       set DOTNET_GCgen0MaxBudget=0x400000
REM  O relatorio imprime a configuracao do coletor que o processo pegou.
REM
REM  O QUE AS CHAVES DERAM (2026-10-08, host parado com o defeito `relogio`
REM  injetado -- 3 MB/s e 36 mil finalizaveis por segundo --, 30 s cada):
REM     padrao (workstation concorrente, geracao 0 de 16 MB)  0,20/s  25 ms  pior 34
REM     DOTNET_gcConcurrent=0 (geracao 0 ate 128 MB) .......  0,20/s  32 ms  pior 111
REM     DOTNET_gcServer=1 (32 heaps) ........................  0,13/s  29 ms  pior 51
REM     DOTNET_GCgen0size=0x4000000 (64 MB) .................  0,07/s  84 ms  pior 123
REM     DOTNET_GCgen0MaxBudget=0x400000 (4 MB) ..............  0,70/s   9 ms  pior 17
REM     DOTNET_GCConserveMemory=7 ...........................  0,17/s  33 ms  pior 53
REM     DOTNET_TieredPGO=0 ..................................  0,23/s  27 ms  pior 37
REM  A fracao do tempo parado e a mesma em todas (0,4 a 0,7%%): o que a chave
REM  muda e o TAMANHO da pausa, que acompanha o orcamento da geracao 0 -- quanto
REM  mais lixo com finalizador se junta entre duas coletas, mais longa cada uma.
REM  No jogo consertado (120 s): padrao, 2 pausas de 4,5 e 7,9 ms; com o teto de
REM  4 MB, 9 pausas de 1,6 ms (pior 3,9).
REM
REM  A PASTA DE USUARIO E DESVIADA, E APAGADA ANTES DE CADA RODADA: a bancada nao
REM  escreve na pasta de saves de verdade. As rodadas de janela nascem NO SEGUNDO
REM  MONITOR (--position 1920,0), mudas, e sem roubar o teclado.
REM
REM  PORTA PROPRIA (7964): se aparecer "FALHOU ao abrir a porta", ha outra rodada
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
echo     testar-o-coletor.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7964

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
REM A PASTA DE RASCUNHO TEM NOME PROPRIO (`RASCUNHO`) e e por ELE que ela e apagada
REM a cada rodada, nunca por %APPDATA%: se um dia a linha do desvio sumir daqui,
REM o `rmdir` la embaixo nao pode cair na pasta de usuario de verdade.
set "RASCUNHO=%TEMP%\jandirus-bancada-coletor"
set "APPDATA=%RASCUNHO%"
echo.
echo  Saves desviados para: %APPDATA%
echo  (a pasta real do dono nao e tocada; esta e apagada antes de cada rodada)

set CENA=--campoteste 8 --horateste 0.5 --diagcoletor 20
set HOST=--path . --position 1920,0 --audio-driver Dummy --semfoco --host --rede 7964 %CENA% --raca Human --conta bancada_coletor --nome Coletor
set SERVIDOR=--headless --path . --server --port 7964 %CENA%

call :rodada "1/5: o host parado, SEM ouvinte (a regua limpa)" %HOST% --coletornivel 0
call :rodada "2/5: o host parado, ouvinte verboso (os tipos e os finalizados)" %HOST%
call :rodada "3/5: o servidor dedicado sozinho, sem janela" %SERVIDOR%
call :rodada "4/5: O CONTRA-EXEMPLO da cena (--coletordefeito relogio: a regua tem que REPROVAR)" %HOST% --coletornivel 0 --coletordefeito relogio
call :rodada "5/5: O CONTRA-EXEMPLO do servidor (--coletordefeito piloto)" %HOST% --coletornivel 0 --coletordefeito piloto

echo.
echo  Encerrado. Cada rodada tem que terminar em  [coletor] ===== TUDO OK =====
echo  -- inclusive a 4 e a 5, que passam PORQUE a regua reprovou o defeito.
pause
exit /b 0

:rodada
echo.
echo  ---- %~1 ----
if exist "%RASCUNHO%" rmdir /s /q "%RASCUNHO%"
mkdir "%RASCUNHO%"
shift
set EXTRA=
:junta
if "%~1"=="" goto :solta
set EXTRA=!EXTRA! %1
shift
goto :junta
:solta
"%GODOT%" !EXTRA!
exit /b 0
