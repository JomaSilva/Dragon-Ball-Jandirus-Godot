@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- os TRES PEDIDOS, medidos e fotografados

REM ===========================================================================
REM  OS TRES PEDIDOS DESTA RODADA
REM
REM     testar-raio.bat
REM
REM    1) "npcs estao conseguindo SE MOVER ENQUANTO TRANSFORMAM"
REM    2) "ataques de ki como BEAM deveriam criar um RASTRO NO CHAO igual o
REM        knock back por onde passam, e tb o EFEITO NA AGUA igual o do fly"
REM    3) "ao ACERTAREM alguem eles deveriam EMPURRAR A PESSOA JUNTO conforme
REM        o beam vai indo"
REM
REM  Ela roda QUATRO vezes, e as tres primeiras nao precisam de tela. A ordem
REM  nao e decorativa: quem mede em NUMERO vem antes de quem fotografa, porque
REM  uma foto custa um minuto de tela pra mostrar o que uma funcao pura ja
REM  tinha respondido em quatro segundos.
REM
REM    1) --projetilteste   os ataques de ki, sem janela. Dez familias: o rastro
REM                         no chao lido nos BYTES que sairiam, o arrasto medido
REM                         em px/s contra a velocidade da cabeca, e a familia 10,
REM                         que INJETA cinco defeitos no rastro medido e exige que
REM                         a regra com o nome de cada um fique VERMELHA.
REM
REM    2) --iateste         o corpo da IA. As familias 7b/7c/7d respondem o pedido 1
REM                         nos DOIS sentidos (nao anda na cena, ANDA depois dela),
REM                         com o defeito injetado no meio -- o relogio da cena
REM                         zerado, que e o estado de ANTES do conserto -- e com a
REM                         regressao que o dono ja cobrou uma vez: o corpo de
REM                         JOGADOR continua andando ferido, voando, com Ki baixo e
REM                         transformado.
REM
REM    3) --diagdecalque    o chao do lado do cliente: o RECORTE que a onda recebeu
REM                         (e nao a direcao que se pediu), os dois eixos DIFERENTES
REM                         um do outro, e o teto da fila com um raio longo.
REM
REM    4) --diagraio        AS FOTOS. E a unica que precisa de JANELA: no headless o
REM                         `GetImage` volta vazio e as fotos saem em branco.
REM
REM  AS FOTOS saem em
REM     %%APPDATA%%\Godot\app_userdata\Dragon ball Jandirus\raio-*.png
REM  e as duas que respondem sozinhas sao as TIRAS, que a propria bancada cola:
REM     raio-1-cena-a-tres.png   andando / preso na cinematica / andando de novo
REM     raio-3-levado-tres.png   o corpo levado pelo feixe, em tres quadros
REM     raio-2-terra-e-agua.png  o mesmo disparo riscando a terra e ondulando a agua
REM
REM  `--aguateste` poe o corpo na BEIRA de um lago, no seco: e o unico berco em que
REM  terra e agua cabem no mesmo disparo. `--horateste 0.5` crava meio-dia -- a hora
REM  do mundo e sorteada, e uma foto de lago as 3 da manha nao responde nada.
REM
REM  PORTA PROPRIA (7952): se aparecer "FALHOU ao abrir a porta", ha outra rodada
REM  viva -- feche-a.
REM
REM  ATENCAO -- OS DOIS PRIMEIROS PASSOS NAO SE FECHAM SOZINHOS. As duas bancadas
REM  rodam no BOOT do servidor, e o servidor continua no ar depois delas (ele nao
REM  sabe que subiu so pra medir). Quando o placar
REM       ================ N passaram, N falharam ================
REM  aparecer, feche com Ctrl+C pra o passo seguinte comecar. E feio e e honesto:
REM  poe-las pra derrubar o processo mudaria o codigo do servidor pra a
REM  conveniencia do teste.
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
echo     testar-raio.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7952

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- as bancadas mediriam a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ---- 1/4: os ataques de ki, sem janela (familias 1 a 10) ----
"%GODOT%" --headless --path . --server --port 7952 --projetilteste

echo.
echo  ---- 2/4: o corpo da IA, sem janela (7b/7c/7d: a cena, a injecao, a regressao) ----
"%GODOT%" --headless --path . --host --rede 7952 --iateste ^
          --raca Saiyan --conta bancada_ia_prova --nome IaProva

echo.
echo  ---- 3/4: o chao do lado do cliente (recorte da onda, eixos, teto da fila) ----
"%GODOT%" --path . --host --rede 7952 --quebrarteste 6 --diagdecalque ^
          --resolution 1280x720 --raca Human --conta bancada_decal --nome Marcador

echo.
echo  ---- 4/4: AS FOTOS (precisa de janela) ----
"%GODOT%" --path . --host --rede 7952 --aguateste --vooteste --bpteste 3000000 ^
          --horateste 0.5 --diagraio --resolution 1280x720 ^
          --raca Human --conta bancada_raio --nome Raio

echo.
echo  Encerrado. As fotos estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus".
pause
