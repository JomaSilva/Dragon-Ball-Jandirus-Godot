@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- a VARIEDADE dos ataques de ki, fotografada

REM ===========================================================================
REM  O PEDIDO DO DONO
REM
REM     testar-variedade.bat
REM
REM   "vi q vc n ta utilizando os ICONES DE BEAM q era usado no byond pra dar
REM    mais VARIEDADE nos ataques de ki"
REM
REM  Ela roda DUAS vezes, e a ordem nao e decorativa: quem responde de graca vem
REM  antes de quem custa uma janela e seis minutos.
REM
REM    1) --diagartedeki    A TABELA, AS FOLHAS E O PIXEL, sem atirar. Confere que
REM                         todo verb que atira tem arte declarada, que as 41
REM                         folhas CARREGAM do disco (este repo ja escreveu 35
REM                         atlas e nunca os importou) e que dois desenhos
REM                         diferentes dao pixel diferente. Precisa de janela: a
REM                         familia 3 mede pixel.
REM
REM    2) --diagvariedade   AS FOTOS, com o tiro SAINDO DA MAO. Dispara 21
REM                         tecnicas pelo MESMO `UsarHabilidade` que o botao do
REM                         jogador aciona, mais as DUAS ESCOLHAS de arte da
REM                         tecnica inventada, mais o CONTROLE (a primeira
REM                         repetida). Fotografa cada uma no mesmo ponto, compara
REM                         todas contra todas e monta o MOSAICO.
REM
REM  AS FOTOS saem em
REM     %%APPDATA%%\Godot\app_userdata\Dragon ball Jandirus\variedade-*.png
REM  e a que responde sozinha e
REM     variedade-mosaico.png    as 24 lado a lado, com o nome debaixo de cada uma
REM
REM  O QUE A SEGUNDA AFIRMA, e que nenhuma outra bancada alcanca: que a arte
REM  escolhida no SERVIDOR e a mesma que o CLIENTE vestiu (o `ushort` do anuncio
REM  de nascimento atravessou o fio), e que duas tecnicas quaisquer desenham
REM  DIFERENTE -- com o limiar MEDIDO na propria rodada (o controle: a mesma
REM  tecnica duas vezes), e nao escrito no codigo.
REM
REM  PORTA PROPRIA (7953): se aparecer "FALHOU ao abrir a porta", ha outra rodada
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
echo     testar-variedade.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7953

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
echo  ---- 1/2: a tabela, as folhas e o pixel (sem atirar, sem rede) ----
"%GODOT%" --path . --diagartedeki --resolution 1280x720

echo.
echo  ---- 2/2: AS FOTOS -- 24 tiros disparados de verdade, e o mosaico ----
"%GODOT%" --path . --host --rede 7953 --bpteste 300000000 --horateste 0.5 ^
          --diagvariedade --resolution 1600x900 ^
          --raca Human --conta bancada_variedade --nome Variado

echo.
echo  Encerrado. As fotos estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus".
echo  Comece pela variedade-mosaico.png.
pause
