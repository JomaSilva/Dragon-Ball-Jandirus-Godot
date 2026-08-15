@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- A PAREDE MUDA, fotografada (antes x depois)

REM ===========================================================================
REM  A PAREDE INVISIVEL, FOTOGRAFADA  (--diagmuda)
REM
REM     ver-a-parede-muda.bat
REM
REM  O RELATO QUE TROUXE ISTO:
REM     "em VARIOS MAPAS PRE-FEITOS tem VARIOS TILES INVISIVEIS COM COLISAO...
REM      ai quando eu soco ele QUEBRA e faz TODOS OS EFEITOS mas N TINHA NADA
REM      LA, so colisao"
REM
REM  O que se descobriu: NAO ha arte perdida. Todo bloqueador de todo mapa ou
REM  desenha, ou e /turf/Other/Blank -- que no BYOND tambem nao desenha nada e
REM  la e INDESTRUTIVEL (destroyable = 0). O port nao extraia esse campo, entao
REM  a costura invisivel CAIA no soco. Metade da queixa era heranca; a outra
REM  metade era regressao, e e essa que o conserto fecha.
REM
REM  ESTE .BAT RODA DUAS VEZES, no mesmo binario e no mesmo mapa:
REM     1a) com --semduro : o mundo de ANTES  (o .duro fica no disco, sem ser lido)
REM     2a) sem nada      : o mundo de DEPOIS
REM
REM  AS TRES CENAS DE CADA RODADA:
REM     muda-A-lookout-<antes|depois>.png    o Templo, no MIOLO do mapa
REM     muda-B-arconia-<antes|depois>.png    Arconia, outra costura
REM     muda-C-quebravel-<antes|depois>.png  o CONTRA-EXEMPLO: parede NORMAL,
REM                                          socada, caida e PISADA
REM
REM  A cena C nao e enfeite: "nada quebrou" e o desfecho de um conserto certo E
REM  de um conserto que matou o cenario destrutivel inteiro. Sem ela, marcar o
REM  mapa todo como duro passaria verde em tudo.
REM
REM  O QUE PROCURAR no console:
REM     [muda] ===== TUDO OK =====      ou     [muda] ===== N FALHA(S) =====
REM  ...e nas fotos: na tira de ANTES o corpo ATRAVESSA a linha invisivel e ha
REM  poeira e terra batida onde nao havia nada; na de DEPOIS ele para na mesma
REM  linha nos tres quadros.
REM
REM  JANELA OBRIGATORIA (no headless o GetImage volta vazio) e no SEGUNDO
REM  MONITOR (--position 1920,0), porque o dono trabalha no principal.
REM
REM  A IRMA DELA, sem janela e sem rede:
REM     dotnet run --project Tools/AssetPipeline -- censo Assets/Maps <BYOND>/Code <BYOND>/Maps
REM  ...e o CONTROLE NEGATIVO dela, que TEM que reprovar:
REM     ...mesmo comando... --semduro
REM
REM  PORTA PROPRIA (7924). Se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a. CONTA PROPRIA: nada toca conta de jogador.
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
echo     ver-a-parede-muda.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7924  (um processo de cada vez, COM janela, no monitor 2)

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada fotografaria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ================== 1/2: O MUNDO DE ANTES (--semduro) ==================
REM SEM `--` separando as flags: com ele elas vao parar em GetCmdlineUserArgs()
REM e a bancada sobe muda (ver servidor.bat).
"%GODOT%" --path . --host --rede 7924 --diagmuda --semduro ^
          --position 1920,0 --resolution 1600x900 ^
          --raca Human --conta bancada_muda_antes --nome Olheiro

echo.
echo  ================== 2/2: O MUNDO DE DEPOIS ==================
"%GODOT%" --path . --host --rede 7924 --diagmuda ^
          --position 1920,0 --resolution 1600x900 ^
          --raca Human --conta bancada_muda_depois --nome Olheiro

echo.
echo  As fotos ficam em:
echo     %%APPDATA%%\Godot\app_userdata\Dragon ball Jandirus\muda-*.png
echo.
pause
