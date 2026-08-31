@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- a agonia vista DE DENTRO (--diagchao)

REM ===========================================================================
REM  A AGONIA DE UM PLANETA, VISTA DE DENTRO  (--agoniaviva + --diagchao)
REM
REM      ver-o-chao-de-um-mundo-acabar.bat
REM
REM  O pedido do dono, literal:
REM     "ao usar planet destroy, o planeta vai comecar a tremer, e varios
REM      efeitos climaticos como raios etc irao comecar, explosoes e crateras
REM      aparecendo, pedras levitando pelo mapa todo de forma 'aleatoria' por 5
REM      minutos, quanto mais perto ta de explodir, mais intenso esses efeitos
REM      ficam."
REM
REM  ---- A IRMA DELA E A `ver-um-planeta-morrer.bat` ----
REM  Aquela ve o mesmo mundo morrer DO ESPACO (magma, rachadura, mega explosao)
REM  num laboratorio sem rede. Esta ve DE DENTRO -- e precisa de servidor de
REM  verdade, porque QUATRO dos cinco efeitos do chao chegam por pacote:
REM     ceu       -> ForcarClima/ApertarClima -> S2C.Ceu
REM     tremor    -> MandarEfeito             -> World.AoCairEfeito
REM     cratera   -> MandarDecalque           -> Decalques
REM     chao caindo -> MandarCelulaCaida      -> PintorDePedacos
REM  So a PEDRA e decisao do cliente. Um laboratorio provaria o desenho e
REM  deixaria o encanamento -- que e onde este projeto ja perdeu quatro efeitos
REM  calados -- sem uma medida.
REM
REM  ---- O QUE ELA MEDE NO PIXEL ----
REM     O AR     razao R/((G+B)/2) do quadro, MEDIANA da janela. Razao e nao
REM              diferenca porque brilho de cena move diferenca e nao move
REM              razao; mediana porque um relampago e um quadro, nao um estado.
REM     O CHAO   a fracao da tela que deixou de ser o chao calmo, depois de
REM              normalizar o veu do clima e de ALINHAR o tremor. O que sobra e
REM              cratera, fumaca, buraco, pedra e particula.
REM     O TREMOR o deslocamento em px que o alinhamento teve que desfazer.
REM
REM  ---- E O CUSTO ----
REM  Quadros por segundo com o jogador DENTRO do planeta, medidos DUAS vezes --
REM  no mundo calmo e no pico da agonia, mesmo lugar, mesma camera, mesma
REM  janela. Uma medida so nao diz nada.
REM
REM  ---- A PASTA DO DONO ----
REM  A morte acontece SO NA MEMORIA (`PalcoDeMortes`), e o palco imprime no fim
REM  quantos planetas morreram dentro dele. O `planetas-mortos.json` nao e
REM  tocado. Ainda assim: rode com o APPDATA desviado se for repetir muito.
REM
REM  A JANELA ABRE NO SEGUNDO MONITOR (--position 1920,0).
REM  A rodada leva ~4 minutos (34 s de mundo calmo pra a regua e o piso de
REM  ruido, 4 patamares de 26 s, 40 s no pico e o desfecho). Sai sozinha.
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
echo     ver-o-chao-de-um-mundo-acabar.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%

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

echo.
echo  ---- o chao tremendo, a cratera abrindo e o mundo acabando debaixo dos pes ----
echo.
echo   Procure o placar:  ===== FIM: N OK, 0 FALHA(S) =====
echo   E OLHE a tira:     agonia-tira-do-chao.png
echo.
"%GODOT%" --path . --host --rede 7962 --agoniaviva --diagchao ^
          --bpteste 2000000 --conta bancada_chao --nome QuemVeOMundoAcabar ^
          --position 1920,0 --resolution 1280x720

echo.
echo  As fotos estao em:
echo     %APPDATA%\Godot\app_userdata\Dragon ball Jandirus\chao-*.png
echo     %APPDATA%\Godot\app_userdata\Dragon ball Jandirus\agonia-tira-do-chao.png
echo.
pause
