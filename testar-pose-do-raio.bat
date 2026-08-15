@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- A POSE DO CORPO COM O RAIO NA MAO

REM ===========================================================================
REM  O PEDIDO DO DONO
REM
REM     testar-pose-do-raio.bat
REM
REM  "vc n colocou a ANIMACAO NO SPRITE dos personagem ao SOLTAR O BEAM. no
REM   byond, ao CARREGAR o beam tinha o EFEITO DE CHARGE do beam, e ao SOLTAR o
REM   sprite ficava na ANIMACAO DE SOCO pra DIRECAO q o beam esta sendo jogado,
REM   e ele so voltaria a posicao de IDLE quando ele PARASSE DE USAR O BEAM (por
REM   vontade propria ou pq ALGUEM BATEU NELE e cancelou o beam)"
REM
REM  Ela roda DUAS vezes, e a ordem nao e decorativa: quem mede em NUMERO vem
REM  antes de quem fotografa, porque uma foto custa um minuto de tela pra
REM  mostrar o que uma funcao pura ja tinha respondido em quatro segundos.
REM
REM    1) --projetilteste   sem janela. A familia 4b mede a pose no ESTADO DO
REM                         FIO -- a entrada, as tres saidas, o soco que
REM                         atravessa e devolve, a trava de direcao, o NPC e o
REM                         byte opcional. E a 4c poe cada uma das TRES SAIDAS
REM                         de volta no estado de ANTES do conserto (o bit
REM                         guardado, a pose derivada do projetil, o `Arremessar`
REM                         sem o `DerrubarRaioPorGolpe`) e exige que a regra com
REM                         o nome de cada uma fique VERMELHA.
REM
REM    2) --diagpose        AS FOTOS. E a unica que precisa de JANELA: no
REM                         headless o `GetImage` volta vazio e as fotos saem em
REM                         branco. Ela responde a metade que o numero nao pega
REM                         -- a 4b ficaria inteirinha verde com a folha
REM                         desenhando o MESMO boneco nas duas poses.
REM
REM  AS FOTOS saem em
REM     %%APPDATA%%\Godot\app_userdata\Dragon ball Jandirus\pose-*.png
REM  e as que respondem sozinhas sao as TIRAS, que a propria bancada cola:
REM     pose-A-tres-quadros.png      parado / carregando / soltando / parou
REM     pose-B-o-canal-inteiro.png   comeco, fim do canal, e o Ki acabando
REM     pose-C-direcao.png           o mesmo corpo atirando pra dois lados
REM     pose-D-npc.png               um NPC parado e o mesmo NPC atirando
REM     pose-E-apanhou.png           atirando / de volta ao idle depois do golpe
REM
REM  A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`): o dono trabalha no
REM  principal. Se a sua tela 2 comeca noutro X, mude o numero.
REM
REM  PORTA PROPRIA (7940): se aparecer "FALHOU ao abrir a porta", ha outra rodada
REM  viva -- feche-a. E o passo 1 NAO SE FECHA SOZINHO: a bancada roda no BOOT do
REM  servidor e o servidor continua no ar depois dela (ele nao sabe que subiu so
REM  pra medir). Quando o placar
REM       ================ N passaram, N falharam ================
REM  aparecer, feche com Ctrl+C pra o passo 2 comecar. E feio e e honesto: po-la
REM  pra derrubar o processo mudaria o codigo do servidor pra a conveniencia do
REM  teste.
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
echo     testar-pose-do-raio.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7940

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
echo  ---- 1/2: a pose no ESTADO DO FIO, sem janela (familias 4b e 4c) ----
"%GODOT%" --headless --path . --server --port 7940 --projetilteste

echo.
echo  ---- 2/2: AS FOTOS (precisa de janela, no MONITOR 2) ----
"%GODOT%" --path . --host --rede 7940 --bpteste 300000000 --horateste 0.5 ^
          --diagpose --position 1920,0 --resolution 1600x900 ^
          --raca Human --conta bancada_pose --nome Poseiro

echo.
echo  Encerrado. As fotos estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus".
echo  Comece pela pose-A-tres-quadros.png.
pause
