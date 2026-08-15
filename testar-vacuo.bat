@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do VACUO

REM ===========================================================================
REM  O VACUO COBRA  (--vacuoteste)
REM
REM     testar-vacuo.bat
REM
REM  O pedido do dono, literal:
REM     "faca todas as racas MENOS a FROST DEMON, ALIEN e MAJIN sofrerem DANO
REM      POR SEGUNDO NO ESPACO pois elas n conseguem respirar la, precisando da
REM      ROUPA ESPACIAL ou estar dentro de uma POD ou NAVE CAPITAL SHIP"
REM
REM  Sao 63 provas e 30 defeitos INJETADOS em ~20 segundos, SEM JANELA. Ela roda
REM  no 1o login e o servidor CONTINUA DE PE depois -- leia o placar
REM  "[vacuo] ==== PLACAR ====" e feche com Ctrl+C.
REM
REM  AS DEZ FAMILIAS (uma por negacao do pedido, e cada uma com o PAR que a segura)
REM     1) A TAXA          5,00 de vida por nucleo por segundo (= vida cheia / 20 s,
REM                        o prazo do `spacetime = 100` do `Stats.dm:120`). Medida,
REM                        nao estimada. E o par: em TERRA FIRME o mesmo corpo nao
REM                        perde nada, e o corpo de 1e13 de BP perde o mesmo que o
REM                        de 25 -- o vacuo nao olha o poder.
REM     2) AS TRES RACAS   uma linha POR NOME: Frost Demon (nas duas grafias, "Icer"
REM                        e "Frost Demon"), Alien e Majin nao perdem; e filho de
REM                        Majin tambem nao. O par: Saiyajin, Humano e Namekuseijin
REM                        PERDEM ("alguem nao perde" ficaria verde com a regra
REM                        invertida).
REM     3) A POD           dentro dela nao perde -- e o VIZINHO sem nave, na mesma
REM                        zona, perde (a pod abriga o piloto, nao a zona). E
REM                        DESEMBARCAR volta a doer.
REM     4) A NAVE-CAPITAL  nao perde, inclusive quem esta PILOTANDO da ponte. Ela
REM                        NAO tem excecao escrita: o interior e outra zona. Esta
REM                        familia prova a PREMISSA disso.
REM     5) O TRAJE         Roupa Espacial ou Respirador na MOCHILA. Uma maca nao
REM                        protege, uma pilha VAZIA nao protege, e TIRAR a roupa
REM                        volta a doer.
REM     6) FORA DO VACUO   o contra-exemplo mais importante: chao de planeta
REM                        pre-feito, chao de planeta GERADO e interior nao custam
REM                        nada. Se a pergunta "estou no espaco?" ficar larga, todo
REM                        mundo sufoca em toda parte.
REM     7) A CINEMATICA    quem esta em transformacao nao sofre dano NEM recebe
REM                        aviso -- e ao fim da cena o castigo retoma SEM repetir a
REM                        abertura (o estado nao e limpo).
REM     8) O CARGO         o Deus da Destruicao respira enquanto porta o titulo
REM                        (`GodOfDestruction.dm:118-121`) e volta a sufocar ao
REM                        larga-lo (`:143`).
REM     9) A MORTE         morre em 20 s (o prazo do DM), nocaute aos 17, QUATRO
REM                        avisos no caminho -- e o primeiro diz AS TRES SAIDAS.
REM                        Mais o ALIVIO de quem vestiu a roupa a tempo.
REM    10) A CORRENTE      quem CHAMA o `TickDoVacuo`, e em que cadencia. Todas as
REM                        nove familias acima chamam o tique na mao -- ou seja,
REM                        continuariam verdes com o sistema ORFAO.
REM
REM  E CADA FAMILIA TEM QUE SABER REPROVAR: a bancada injeta 30 defeitos (trocando
REM  as sondas do vacuo por baixo das mesmas provas) e exige que a familia fique
REM  VERMELHA. Familia que continua verde com o defeito dentro sai como [CEGA].
REM
REM  ELA NAO ESTRAGA O MUNDO: o defeito "o mundo inteiro virou vacuo" e injetado
REM  por baixo do laco de PRODUCAO, que varre `_players` -- entao o host e os NPCs
REM  sufocam de verdade durante aqueles 3 segundos. A bancada fotografa quem NAO e
REM  dela antes de cada rodada e devolve tudo depois (vida, membro, nocaute, morte,
REM  tag de combate e o relogio do aviso). Quando ha respingo, ela diz quanto foi.
REM
REM  CONTA E PORTA PROPRIAS (7920): se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a.
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
echo     testar-vacuo.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7920

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
echo  ---- o vacuo cobra (63 provas, 30 defeitos injetados) ----
echo.
echo   A bancada roda no 1o login e leva uns 20 s. Procure o placar:
echo      [vacuo]   provas             : 63   (63 verdes, 0 vermelhas)
echo      [vacuo]   defeitos injetados : 30   (30 pegos, 0 passaram batido)
echo.
"%GODOT%" --headless --path . --host --rede 7920 --vacuoteste ^
          --raca Saiyan --conta bancada_vacuo --nome Asfixiado

echo.
echo  Encerrado.
pause
