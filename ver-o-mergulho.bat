@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- o mergulho inteiro (telinha, gota, mente sem borda)

REM ===========================================================================
REM  O MERGULHO INTEIRO, PELO GESTO DO JOGADOR  (--diagmergulho)
REM
REM      ver-o-mergulho.bat
REM
REM  Os quatro pedidos desta rodada sao UM CAMINHO SO -- apertar M, escolher,
REM  ver a tela ondular, acordar num lugar sem beirada e voltar -- e nenhuma das
REM  bancadas que existiam atravessa esse caminho:
REM
REM      --presoteste     mede a PLANTA (funcao pura). E verde num jogo em que
REM                       ninguem consegue meditar.
REM      --menteviva      mede a PORTA com dois clientes, mas ATRAVESSA a onda
REM                       de proposito -- por desenho ela nao pode dizer nada
REM                       sobre a espera.
REM      --diaggota       mede o SHADER numa cena de laboratorio, sem rede e sem
REM                       mente. Prova que o pano ondula, nunca que o JOGO o
REM                       acende na hora certa.
REM      --fotodamente    fotografa o branco, e para na foto.
REM
REM  ============================ AS OITO FAMILIAS ============================
REM      1  a tecla M abre a TELINHA; o verb sumiu do menu P
REM      2  "Meditar normal" medita e NAO viaja
REM      3  a ida ONDULA, e a viagem so acontece no FIM da onda
REM      4  a volta por VITORIA (matar o reflexo) tambem ondula
REM      5  o soco no corpo real NAO ondula, e a volta e SECA
REM      6  a mente nao tem borda: 500 tiles sem esbarrar e sem teleporte
REM      7  o reflexo nao some pra sempre (a coleira o traz de volta)
REM      8  o pedaco descarrega e a conta de pedacos vivos nao cresce
REM
REM  Cada uma delas tem um DEFEITO INJETADO que a poe vermelha, e seis dos oito
REM  sao automaticos (saem no log como "[defeito] ..."). Os DOIS ultimos sao de
REM  FONTE, porque o que eles medem sao um `const` e um `if` dentro do laco de
REM  producao -- injeta-los por chave exigiria um `if (modoDeTeste)` no caminho
REM  do jogo. Ver o rodape deste arquivo pra receita das duas.
REM
REM  ============================ POR QUE `--horateste 0.5` ============================
REM  Porque tres familias medem PIXEL, e o mundo nasce com o relogio andando: a
REM  primeira rodada caiu de madrugada, e grama a noite e um campo quase liso de
REM  verde escuro -- a onda deslocava a tela inteira e a bancada mediu 0,1%. Com
REM  o sol a pino ela mede 26%. O piso de RUIDO continua sendo medido na hora
REM  (dois quadros parados), entao o corte nunca e um numero digitado.
REM
REM  ============================ E POR QUE PRECISA DE JANELA E DE --host ============================
REM  JANELA porque no headless o `GetImage` volta vazio -- e ali a bancada diz
REM  "NAO MEDIU" em vez de passar de graca (o placar conta isso separado).
REM  `--host` porque as familias perguntam a AUTORIDADE (o servidor esta neste
REM  mesmo processo) e porque a onda e um PACOTE: num corpo sem `Peer` ela e um
REM  `?.` que nao faz nada.
REM
REM  A JANELA VAI PRO MONITOR 2 (`--position 1920,0`): o dono trabalha no
REM  monitor principal.
REM
REM  CONTA PROPRIA: `bancada_mergulho`. Nada aqui toca personagem de jogador.
REM ===========================================================================

cd /d "%~dp0"

REM --- achar o Godot (mesma busca do servidor.bat) --------------------------
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
echo     ver-o-mergulho.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7914

REM --- porta limpa: bancada que nao sai sozinha segura a porta da proxima ---
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*diagmergulho*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ---- A RODADA INTEIRA: as oito familias, com seis defeitos injetados ----
REM Procure no fim:
REM     ===== FIM: N OK, M FALHA(S), K SEM MEDIDA =====
REM "SEM MEDIDA" nao e "ok": e uma familia de pixel que nao achou quadro.
"%GODOT%" --path . --host --rede 7914 --horateste 0.5 --diagmergulho ^
          --position 1920,0 --resolution 1280x720 ^
          --raca Human --conta bancada_mergulho --nome Mergulho

echo.
echo  ============================ AS FOTOS ============================
echo  Em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus":
echo.
echo    mergulho-1-a-telinha.png                 a pergunta que a tecla M abre
echo    mergulho-2-meio-da-onda-da-ida.png       o anel no meio da ida
echo    mergulho-2b-defeito-sem-onda.png         o MESMO instante sem a gota
echo    mergulho-3-meio-da-onda-da-volta.png     o anel no branco da mente
echo    mergulho-4-branco-a-500-tiles.png        o chao 500 tiles fora da chapa
echo    mergulho-4b-defeito-a-parede-de-volta.png  parado na beirada (o defeito)
echo.
echo  O PAR QUE IMPORTA e o 2 contra o 2b: a mesma tela, no mesmo lugar, com e
echo  sem a onda. A foto sozinha nao decide nada -- o log traz a fracao de
echo  pixels que mudou nos dois casos (26%% contra 0%%).
echo.
echo  ============================ AS DUAS INJECOES DE FONTE ============================
echo  Familia 7 (a coleira) -- meio minuto:
echo     em Server\GameServer.Clone.cs, comente a linha
echo        if (DimensaoMental.FugiuDoDono(npc.Pos, dono.Pos)) ReaparecerNaFrente(npc, dono);
echo     e rode:  ...--diagmergulho --mergulhofamilia 7
echo     MEDIDO: 2,5 s depois do empurrao ele continua a 69 tiles; as 3 provas ficam vermelhas.
echo.
echo  Familia 8 (o descarte de pedaco) -- dois minutos (ela anda os 500 tiles):
echo     em Client\PintorDePedacos.cs, troque FolgaDeDescarte de 64 pra 4000
echo     e rode:  ...--diagmergulho --mergulhofamilia 8
echo     MEDIDO: 20 pedacos vivos no fim contra 6, e zero "[pedacos] soltei" no log.
echo.
echo  DESFACA AS DUAS DEPOIS. Uma bancada que fica verde com o defeito dentro e
echo  pior que bancada nenhuma.
pause
