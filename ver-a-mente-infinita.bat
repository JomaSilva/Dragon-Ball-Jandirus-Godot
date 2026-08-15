@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- a dimensao mental, ANTES e DEPOIS

REM ===========================================================================
REM  A MENTE BRANCA E SEM BEIRADA, NA TELA  (--fotodamente)
REM
REM      ver-a-mente-infinita.bat
REM
REM  ELA RODA DUAS VEZES O MESMO BINARIO, e so a chave muda:
REM
REM      2/3  --menteantiga   -> o lugar como o dono o encontrou: o z24 REAL do
REM                             BYOND, mosaico azul-petroleo de um lado e
REM                             nebulosa roxa do outro, com o nascimento na
REM                             emenda dos dois. E o "antes".
REM      3/3  (sem a chave)   -> a planta branca do Core, sem uma celula densa e
REM                             sem beirada nenhuma. E o "depois".
REM
REM  Uma foto de um lugar branco NAO PROVA QUE ELE MUDOU. O par prova.
REM
REM  ============================ QUAL FOTO OLHAR ============================
REM  Sao quatro por rodada, e a que responde ao ultimo pedido do dono e a
REM  QUARTA:
REM
REM      mente-depois-0-no-planeta.png     antes de meditar (o controle)
REM      mente-depois-1-dentro.png         o nascimento
REM      mente-depois-2-andando.png        6 s a leste, ainda na chapa
REM   >> mente-depois-3-fora-da-chapa.png  60 tiles ALEM da beirada
REM
REM  A quarta e a unica que encosta no que esta fase construiu: fora dos
REM  100x100 nao ha dado nenhum, so um pincel que o pintor repete por pedaco.
REM  Toda medida de colisao fica VERDE com esse pincel errado -- o jogador
REM  atravessaria o vazio andando por cima de preto, e nada reclamaria.
REM
REM  O log escreve a CELULA em que o corpo parou. Branco fotografado no meio da
REM  chapa e branco fotografado a 60 tiles dela sao a mesma imagem; a celula e
REM  o que separa as duas.
REM
REM  ============================ A JANELA VAI PRO MONITOR 2 ============================
REM  `--position 1920,0`: o dono trabalha no monitor principal, e uma bancada
REM  que rouba a tela dele no meio do dia e uma bancada que ninguem roda. A
REM  primeira etapa e --headless e nem abre janela.
REM
REM  CONTA PROPRIA: `bancada_mente`. Nada aqui toca personagem de jogador.
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
echo     ver-a-mente-infinita.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7932

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
echo  ---- 1/3: AS REGRAS, sem janela (a familia 3 e a 3b do --presoteste) ----
REM Ela vem primeiro de proposito: se a planta estiver errada, as fotos abaixo
REM sao duas imagens bonitas da coisa errada. Procure:
REM     [preso] ======== N OK, M FALHA(S) ========
REM O servidor NAO se fecha sozinho -- a linha de baixo o tira quando a bancada
REM ja escreveu o placar.
start "preso-regras" /min cmd /c ""%GODOT%" --headless --path . --server --rede 7932 --presoteste"
ping -n 35 127.0.0.1 >nul
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*presoteste*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul

echo.
echo  ---- 2/3: O ANTES (--menteantiga: o z24 do BYOND, azul e roxo) ----
"%GODOT%" --path . --host --rede 7932 --fotodamente --menteantiga ^
          --position 1920,0 --resolution 1600x900 ^
          --raca Human --conta bancada_mente --nome Mentalista

echo.
echo  ---- 3/3: O DEPOIS (a planta branca, sem beirada) ----
"%GODOT%" --path . --host --rede 7932 --fotodamente ^
          --position 1920,0 --resolution 1600x900 ^
          --raca Human --conta bancada_mente --nome Mentalista

echo.
echo  Encerrado. As fotos estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus".
echo  Compare mente-antes-3-fora-da-chapa.png com mente-depois-3-fora-da-chapa.png.
pause
