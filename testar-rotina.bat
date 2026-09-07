@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada da VIDA DIARIA dos NPCs (espalhamento, rotina, arena)
REM ===========================================================================
REM  A VIDA DIARIA DOS NPCs -- ETAPA 1 DO PEDIDO DE IA/BALOES/TORNEIOS
REM    (--espalhamentoteste + --arenaiateste + --rotinateste)
REM
REM     testar-rotina.bat
REM
REM  O PEDIDO DO DONO (2026-09-06):
REM    * "spawn de NPCs distribuido pelo planeta inteiro (so em terra, navegavel,
REM       sem colisao), distribuicao aproximadamente uniforme (distancia minima/
REM       regioes)"
REM    * "maquina de estados de vida diaria: ocioso/andar, treinar sozinho,
REM       conversar com outro NPC proximo (baloes curtos e variados), convidar
REM       pra spar -> aceitar/recusar, spar nao letal com condicao de termino;
REM       duracoes/probabilidades configuraveis (Assets/Data/rotina.json);
REM       tick barato com taxa reduzida/dormencia pra NPCs longe dos jogadores"
REM    * "consciencia de borda de arena, reagir a knockback voando pra evitar
REM       ring-out se puder voar, perfis de NPC (uns voam, uns usam ki, outros
REM       so corpo a corpo)"
REM
REM  TRES RODADAS, todas sem janela:
REM    1) --espalhamentoteste (boot): os seis planetas do plano, sem agua, sem
REM       empilhar, terra navegavel, um habitante por regiao antes de repetir;
REM       a regra antiga (24 tiles em volta do berco) como defeito injetado;
REM       mapa sintetico com lago, ilha grande, patio murado e obra na vaga.
REM       Placar: "[espalhamento] ==== N OK, M FALHA(S) ====".
REM    2) --arenaiateste (boot): na margem recua pro centro; arremessado pra
REM       fora, o voador decola antes da linha e o 'so corpo' sai; perfis;
REM       pouso quando seguro; margem -1 como defeito injetado.
REM       Placar: "[arenaia] ==== N OK, M FALHA(S) ====".
REM    3) --rotinateste (1o login): conversa com baloes dos dois lados, convite,
REM       aceite, spar nao letal que acaba, recusa, poder desigual, provocacao,
REM       dormencia por zona, 1 tique a cada 10 pra quem esta longe, determinismo.
REM       Placar: "[rotina] ==== N OK, M FALHA(S) ====".
REM
REM  A pasta de saves do Godot e DESVIADA pro %%TEMP%%.
REM  PORTA PROPRIA (7921): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     testar-rotina.bat
echo.
pause
exit /b 1
:temgodot
echo  Godot : %GODOT%
echo  Porta : 7921
set "APPDATA=%TEMP%\jandirus-bancada-rotina"
if exist "%APPDATA%\Godot" rmdir /s /q "%APPDATA%\Godot"
if not exist "%APPDATA%" mkdir "%APPDATA%"
echo  Saves : %APPDATA% (desviado -- a pasta de saves de quem joga fica intacta)
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
echo  ---- 1) onde os habitantes nascem (boot) ----
"%GODOT%" --path . --headless --host --rede 7921 --espalhamentoteste
echo.
echo  ---- 2) a linha que a IA nao cruza (boot) ----
"%GODOT%" --path . --headless --host --rede 7921 --arenaiateste
echo.
echo  ---- 3) a vida diaria dos habitantes (1o login) ----
"%GODOT%" --path . --headless --host --rede 7921 --rotinateste ^
          --raca Human --conta bancada_rotina --nome MedidorDaRotina
echo.
echo  Leia os tres placares: "[espalhamento] ==== N OK ====", "[arenaia] ==== N OK ====" e "[rotina] ==== N OK ====".
pause
