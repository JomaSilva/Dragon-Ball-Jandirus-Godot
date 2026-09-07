@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada dos TORNEIOS (Terra e Outro Mundo)
REM ===========================================================================
REM  OS TORNEIOS  (--torneioteste + --diagtorneio)
REM
REM     testar-torneio.bat
REM
REM  O PEDIDO DO DONO (2026-09-06, etapa 3 de IA/baloes/torneios): torneio de
REM  32 (16-avos ate a final, com disputa de 3o lugar), convite no canto
REM  inferior direito com Participar/Recusar e prazo, vagas preenchidas por
REM  NPCs gerados na hora (BP em torno da media dos inscritos), ring-out =
REM  derrota salvo voando, premios 1.000.000 / 500.000 / 250.000, chave que
REM  sobrevive a reconexao, W.O. por desconexao, Torneio da Terra mensal e o
REM  do Outro Mundo 15 dias depois (so mortos; NPCs nascem mortos com aureola).
REM
REM  DUAS RODADAS:
REM    1) --torneioteste (servidor, 1o login, sem janela): o host e o unico
REM       inscrito; 31 NPCs nascem; a chave roda inteira com o host lutando
REM       (mil vezes mais forte) ate ser campeao e receber o premio; ring-out
REM       com e sem voo; W.O.; reconexao; o Outro Mundo com todo mundo morto de
REM       pe; a agenda gravada em torneio.json; cancelar solta todo mundo.
REM       Placar: "[torneio] ==== N OK, M FALHA(S) ====".
REM    2) --diagtorneio (cliente + servidor no mesmo processo, JANELA no segundo
REM       monitor pra foto): o convite chega pela REDE, o painel aparece no
REM       canto, Participar/Recusar mandam os verbos, o prazo vence sozinho.
REM       Placar: "[diagtorneio] ===== TUDO OK =====". Foto em
REM       %%APPDATA%%\Godot\app_userdata\...\torneio-1-convite.png
REM
REM  A pasta de saves do Godot e DESVIADA pro %%TEMP%%.
REM  PORTA PROPRIA (7931/7932): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     testar-torneio.bat
echo.
pause
exit /b 1
:temgodot
echo  Godot : %GODOT%
echo  Portas: 7931 e 7932
set "APPDATA=%TEMP%\jandirus-bancada-torneio"
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
echo  ---- 1) os torneios de ponta a ponta (servidor) ----
"%GODOT%" --path . --headless --host --rede 7931 --torneioteste ^
          --raca Human --conta bancada_torneio --nome MedidorDoTorneio
echo.
echo  ---- 2) o convite na tela (janela no segundo monitor, pra foto) ----
"%GODOT%" --path . --position 1920,0 --host --rede 7932 --diagtorneio --semfoco ^
          --raca Human --conta bancada_diagtorneio --nome RoboDoTorneio
echo.
echo  Leia os dois placares: "[torneio] ==== N OK ====" e "[diagtorneio] ===== TUDO OK =====".
pause
