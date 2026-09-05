@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do ARRANQUE QUE FICAVA LONGE
REM ===========================================================================
REM  O ARRANQUE QUE FICAVA LONGE  (--arranqueteste)
REM
REM     testar-arranque.bat
REM
REM  A QUEIXA DO DONO (2026-09-05): "as vezes o dash de soco, tanto so apertando
REM  espaco quanto segurando o shift, acerta o soco no alvo mas o personagem
REM  ainda ta longe -- ele deveria entrar no range aonde o dash nao e mais
REM  ativado".
REM
REM  O ARRANQUE SEMPRE PAROU A UM TILE. O que ele via vinha DEPOIS: os pacotes
REM  de input que o cliente ja tinha despachado com a posicao de ANTES do
REM  arranque chegavam e o validador de movimento arrastava o corpo de volta
REM  pra ela, um orcamento de tique por pacote. O soco ja tinha acertado; o
REM  boneco e que "voltava". Agora, dentro da janela de correcao esperada que
REM  todo salto abre (500 ms), pedido longe e pacote velho: o corpo fica.
REM
REM    1) SHIFT + ESPACO   alvo marcado a 4 tiles: para a 32 px e o soco acerta.
REM    2) so ESPACO        alvo a 2 tiles no cone: o passo curto para a 32 px.
REM    3) PERTO DEMAIS     a 44 px o corpo ANDA o resto (sem investida).
REM    4) OS PACOTES EM VOO   quatro pacotes com a posicao velha nao movem o
REM                        corpo; um passo honesto continua aceito; e com a
REM                        janela FECHADA (a regra de ontem) os mesmos quatro
REM                        arrastam -- e o contra-exemplo, e e a anti-trapaca
REM                        de sempre fora da janela.
REM
REM  RODA NO HEADLESS. A pasta de saves do Godot e DESVIADA pro %%TEMP%% -- a
REM  bancada nunca escreve na pasta de saves de quem joga nesta maquina.
REM
REM  PORTA PROPRIA (7913): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     testar-arranque.bat
echo.
pause
exit /b 1
:temgodot
echo  Godot : %GODOT%
echo  Porta : 7913
set "APPDATA=%TEMP%\jandirus-bancada-arranque"
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
echo  ---- o arranque que ficava longe: o corpo para a um tile e os pacotes em voo nao o puxam de volta ----
"%GODOT%" --path . --headless --host --rede 7913 --arranqueteste ^
          --raca Human --conta bancada_arranque --nome MedidorArranque
echo.
echo  Leia o placar acima: "[arranque] ==== N OK, M FALHA(S) ====".
pause
