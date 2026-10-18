@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada da SOMBRA (o veu de visao)
REM ===========================================================================
REM  A SOMBRA  (--diagvisao + --diagsombra)
REM
REM     testar-sombra.bat
REM     testar-sombra.bat Hell,54,122       (outro portao: Zona,cx,cy)
REM
REM  O PEDIDO DO DONO (2026-09-07), com dois prints:
REM     "fica uma sombra na tela quando vc vai assistir o torneio, e os npcs
REM      estao realmente invisiveis"
REM     "as sombras estao bem bugadas: tem sombra q fica sobre tile e outras
REM      nao, tem sombra q sai do meio da parede"
REM
REM  AS DUAS CAUSAS, em uma linha cada:
REM     * o raio ATRAVESSAVA o muro e parava em faces diferentes (a saida
REM       lateral no vao do portao, a face norte longe dali): a aresta do leque
REM       cortava os tiles em diagonal -- a cunha. Agora o raio para na face de
REM       ENTRADA e a parede clara e um FURO na sombra (shader), por celula;
REM     * assistindo, o olho ficava no corpo (area de espera) e a camera na
REM       arena: com o olho FORA da tela o leque nao fecha os 360 graus e
REM       DOBRA -- a escuridao diagonal. Agora o olho vai com a camera (o
REM       EYE_PERSPECTIVE do DM) e a tela sempre contem o olho.
REM
REM  DUAS RODADAS:
REM    1) --diagvisao (sem janela): a geometria em numero, em seis posicoes da
REM       Terra (campo aberto, colado, cercado, muro comprido, muro atras de
REM       muro, o PORTAO). Fan x raio direto celula a celula; paradas fora de
REM       face = 0; cunhas = 0; leque sem dobra. E TRES DEFEITOS INJETADOS que
REM       tem que ficar vermelhos (a regra antiga, a parede sem furo, a tela
REM       sem o olho).
REM       Placar: "[visao] ==== N OK, TUDO OK ====".
REM    2) --diagsombra (cliente + servidor no mesmo processo, JANELA no segundo
REM       monitor pra foto): o corpo vai ate o portao da Terra (248,122), foto e
REM       medida; depois abre um torneio, se inscreve, adianta ate a luta,
REM       liga o Assistir e confere o olho na camera, o leque sem dobra e os
REM       DOIS lutadores desenhados e claros -- mais o contra-exemplo (o olho
REM       de antes dobrava).
REM       Placar: "[diagsombra] ===== TUDO OK =====". Fotos em
REM       %%APPDATA%%\Godot\app_userdata\...\sombra-1-portao.png e sombra-2-assistindo.png
REM
REM  A pasta de saves do Godot e DESVIADA pro %%TEMP%%.
REM  PORTA PROPRIA (7935): se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a.
REM ===========================================================================
cd /d "%~dp0"
set "LUGAR=%1"
if "%LUGAR%"=="" set "LUGAR=Earth,248,122"
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
echo     testar-sombra.bat
echo.
pause
exit /b 1
:temgodot
echo  Godot : %GODOT%
echo  Porta : 7935
echo  Portao: %LUGAR%
set "APPDATA=%TEMP%\jandirus-bancada-sombra"
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
echo  ---- 1) a geometria em numero (sem janela) ----
"%GODOT%" --path . --headless --diagvisao
echo.
echo  ---- 2) a sombra na tela e o espectador (janela no segundo monitor, pra foto) ----
"%GODOT%" --path . --position 1920,0 --host --rede 7935 --diagsombra --sombralugar %LUGAR% --semfoco ^
          --raca Human --conta bancada_sombra --nome RoboDaSombra
echo.
echo  Leia os dois placares: "[visao] ==== N OK, TUDO OK ====" e "[diagsombra] ===== TUDO OK =====".
echo  Fotos em: %APPDATA%\Godot\app_userdata\Dragon ball Jandirus\sombra-*.png
echo.
pause
