@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do QUADRO DA MORTE

REM ===========================================================================
REM  O QUADRO DA MORTE  (--tiquedamorte)
REM
REM      testar-o-tique-da-morte.bat
REM
REM  O pedido do dono, literal:
REM     "Confirmar com uma bancada que exercite MORTE em combate, e nao so a
REM      leitura do log."
REM
REM  E a razao dele: um log limpo tambem sai quando nada aconteceu.
REM
REM  ---- O DEFEITO QUE ELA EXISTE PRA PEGAR ----
REM  O cadaver do jogador nascia com `_players[id] = corpo` de DENTRO do
REM  `foreach (... in _players.Values)` do `TickCombate`. Insercao de chave nova
REM  e a UNICA operacao de Dictionary que invalida um enumerador em andamento
REM  (o `Remove` nao invalida nada desde o .NET Core 3.0 -- por isso o par
REM  "insere e tira na linha seguinte" parecia um no-op). Resultado:
REM
REM     InvalidOperationException: Collection was modified
REM        at GameServer.TickCombate  (Server\GameServer.Combat.cs:1203)
REM        at GameServer.Tick         (Server\GameServer.cs:4742)
REM
REM  `TickCombate` e a PRIMEIRA chamada do `Tick()`: os ~60 subsistemas depois
REM  dele -- fichas, projeteis, feridas, buffs, cadaveres, vacuo, gravidade,
REM  esferas, sagas, conquista, ceu, curandeiros -- e o snapshot por zona
REM  perdiam o quadro. A cada morte de jogador.
REM
REM  ---- O QUE ELA MEDE (e o que ela SE RECUSA a medir) ----
REM  Ela NAO afirma "nao houve excecao": isso ficaria verde com um `catch` em
REM  qualquer ponto do caminho e o estrago inteiro de pe. O que ela mede sao
REM  CONSEQUENCIAS, todas DEPOIS do ponto onde o tique morria:
REM     1. o corpo no ar CAIU        (TickDosRelogiosDoCorpo, logo apos o combate)
REM     2. o projetil ANDOU          (TickDosProjeteis, no meio do quadro)
REM     3. a ferida SINCRONIZOU      (TickDasFeridas, bloco de 5 Hz, perto do fim)
REM     4. o quadro CHEGOU AO FIM    (_quadrosInteiros, a ULTIMA linha do Tick())
REM
REM  E as quatro sao medidas NOS QUADROS ANTES da morte tambem -- senao "chegou
REM  ao fim" ficaria verde num servidor que nunca roda o tique inteiro.
REM
REM  ---- AS OITO FAMILIAS ----
REM     1) A LINHA DE BASE          tres quadros com todo mundo vivo
REM     2) A MORTE EM COMBATE       um algoz soca a vitima pelo `Atacar` ate um
REM                                 membro VITAL zerar; entre um golpe e outro
REM                                 corre o `Tick()` inteiro (e ele que abate a
REM                                 recarga). Ninguem chama `Morrer()` na mao.
REM     3) O QUADRO ANTERIOR        o corpo ja caido, o prazo de 15 s correndo
REM     4) O QUADRO DA MORTE        o cadaver NASCE dentro do laco -- e as
REM                                 quatro consequencias valem
REM     5) NINGUEM PRESO NEM ZUMBI  o morto sai do lugar, o cadaver fica no
REM                                 ponto exato, as duas filas esvaziam, e os
REM                                 tres quadros seguintes tambem chegam ao fim
REM     6) O IRMAO DA FILA DE NPC   `_npcsPraTirar`: a triagem chamada duas
REM                                 vezes sem dreno enfileira UMA -- e nasce UM
REM                                 cadaver, e nao dois
REM     7) O IRMAO DA FILA DE VOLTA `_acordar`: um soco no corpo largado acorda
REM                                 o dono no MESMO tique e tira o boneco do
REM                                 mundo
REM     8) O DEFEITO INJETADO       o cadaver de volta no `_players`, letra por
REM                                 letra -- e as QUATRO consequencias tem que
REM                                 REPROVAR. Depois, desfeito, tudo volta a
REM                                 passar.
REM
REM  Ela roda no 1o login, sem janela, e devolve tudo o que poe no mundo
REM  (corpos, cadaveres, tiros no ar, a cadencia do tique e o interruptor do
REM  defeito). O servidor continua de pe depois -- leia o placar
REM  "[tique] ==== N passaram, N falharam ====" e feche com Ctrl+C.
REM
REM  CONTA E PORTA PROPRIAS (7981): se aparecer "FALHOU ao abrir a porta", ha
REM  outra rodada viva -- feche-a.
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
echo     testar-o-tique-da-morte.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7981

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    REM `-t:Rebuild` e OBRIGATORIO: o build incremental ja deu "compilacao com
    REM exito" sem trocar a DLL, e o Godot subiu com o binario de ontem. Uma
    REM bancada medindo a versao anterior e pior que bancada nenhuma.
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ---- o quadro inteiro no instante da morte ----
echo.
echo   Procure o placar:  [tique] ==== N passaram, 0 falharam ====
echo.
"%GODOT%" --headless --path . --host --rede 7981 --tiquedamorte ^
          --raca Saiyan --conta bancada_tique --nome Cronista

echo.
echo  Encerrado.
pause
