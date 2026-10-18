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
REM    1) --diagartedeki    A TABELA, OS ESTILOS E O PIXEL, sem atirar. Confere que
REM                         todo verb que atira tem arte declarada, que toda arte
REM                         tem ESTILO e se veste (os tiros sao desenhados por
REM                         shader desde 2026-10-07, nao ha mais folha), que o
REM                         raio e CONTINUO em 19 rumos, que a ponta e o tronco
REM                         desenhados batem com as tabelas do Core, e que o
REM                         fogo em volta (o leque de labaredas das duas pontas)
REM                         nao pinta atras da mao nem alem da ponta. Precisa de
REM                         janela: da familia 2b em diante ela mede pixel.
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
REM  A CONTA E `bancada_variedade_b` DESDE 2026-10-07. O personagem da conta antiga
REM  (`bancada_variedade`) ENGORDAVA a cada rodada -- a bancada re-somava os degraus
REM  de nivel em cima do save -- ate a Bala Dispersa abrir mais de 256 esferas e ser
REM  recusada pelo teto da zona ("o ar aqui ja esta saturado de energia"). O defeito
REM  foi consertado (`ArmarParaAVariedade`), mas o save antigo continua inchado: a
REM  conta nova comeca de um personagem limpo. So letras e sublinhado no nome.
"%GODOT%" --path . --host --rede 7953 --bpteste 300000000 --horateste 0.5 --campoteste 23 ^
          --diagvariedade --position 1920,0 --resolution 1600x900 ^
          --raca Human --conta bancada_variedade_b --nome Variado

echo.
echo  Encerrado. As fotos estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus".
echo  Comece pela variedade-mosaico.png.
pause
