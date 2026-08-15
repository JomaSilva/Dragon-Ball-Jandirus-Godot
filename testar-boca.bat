@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- DE ONDE O FEIXE SAI, E EM QUE CAMADA

REM ===========================================================================
REM  O PEDIDO DO DONO, E ELE VEIO EM FOTO
REM
REM     testar-boca.bat
REM
REM  "os beams tao saindo DE CIMA do personagem, deveriam sair DA FRENTE dele,
REM   NA FRENTE DO SPRITE deles"
REM
REM  Sao DUAS perguntas e ele fez as duas:
REM    (A) ONDE nasce -- a boca do cano devia sair da mao, a frente do corpo, no
REM        sentido em que ele olha.
REM    (B) EM QUE CAMADA -- "na frente do sprite" tambem se le como ordem de
REM        desenho: o feixe passando POR TRAS do corpo em vez de a frente dele.
REM
REM  Ela roda DUAS vezes, e a ordem nao e decorativa: quem mede em NUMERO vem
REM  antes de quem fotografa, porque uma foto custa um minuto de tela pra
REM  mostrar o que uma funcao pura ja tinha respondido em quatro segundos.
REM
REM    1) --projetilteste   sem janela. A familia 1-bis mede a boca nos quatro
REM                         sentidos (projecao no rumo, deriva transversal, o vao
REM                         entre os quadros, a altura que viaja) e a 1-ter poe o
REM                         nascimento de volta no UMBIGO e exige que cada uma
REM                         daquelas regras fique VERMELHA.
REM
REM    2) --diagboca        AS FOTOS. E a unica que precisa de JANELA: no headless
REM                         o `GetImage` volta vazio e as fotos saem em branco.
REM                         Ela responde a metade que o numero nao pega -- a 1-bis
REM                         le o `Pos` do SERVIDOR e ficaria verde com o feixe
REM                         desenhado atras do corpo, no chao, ou com a cauda
REM                         carimbada em cima do boneco.
REM
REM  AS FOTOS saem em
REM     %%APPDATA%%\Godot\app_userdata\Dragon ball Jandirus\boca-*.png
REM  e as que respondem sozinhas sao as TIRAS, que a propria bancada cola:
REM     boca-1-os-quatro-sentidos.png      o mesmo corpo atirando pros quatro lados
REM     boca-2-antes-e-depois.png          o defeito do dono injetado, e a producao
REM     boca-3-voando-antes-e-depois.png   o feixe no chao, e o feixe na altura do corpo
REM     boca-4-camada-antes-e-depois.png   o feixe atras do corpo, e o feixe a frente
REM     boca-5-colado.png                  o tiro dado em quem esta COLADO
REM
REM  E as `boca-*-mascara.png` sao a PROVA DA PROVA: o recorte com os pixels que a
REM  sonda contou como feixe pintados de magenta. Uma bancada que diz "0 px de
REM  feixe em cima do personagem" tem que poder mostrar QUAIS pixels ela contou --
REM  este projeto ja teve quatro defeitos visuais passando por quatro mil
REM  checagens verdes porque a bancada media INTENCAO.
REM
REM  `--horateste 0.5` crava MEIO-DIA e nao e enfeite: de dia a `LuzDeKi` nao
REM  acende, e uma luz radial de tres tiles em volta da cabeca do tiro entraria na
REM  mascara como se fosse tinta de feixe -- inclusive por cima do corpo, que e a
REM  regiao que esta bancada precisa medir vazia. `--vooteste` da a skill de voo,
REM  sem a qual a cena C nao decola.
REM
REM  A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`): o dono trabalha no
REM  principal. Se a sua tela 2 comeca noutro X, mude o numero.
REM
REM  PORTA PROPRIA (7932): se aparecer "FALHOU ao abrir a porta", ha outra rodada
REM  viva -- feche-a. E o passo 1 NAO SE FECHA SOZINHO: a bancada roda no BOOT do
REM  servidor e o servidor continua no ar depois dela (ele nao sabe que subiu so
REM  pra medir). Quando o placar
REM       ================ N passaram, N falharam ================
REM  aparecer, feche com Ctrl+C pra o passo 2 comecar.
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
echo     testar-boca.bat
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
echo  ---- 1/2: a boca no NUMERO, sem janela (familias 1-bis e 1-ter) ----
"%GODOT%" --headless --path . --server --port 7932 --projetilteste

echo.
echo  ---- 2/2: AS FOTOS (precisa de janela, no MONITOR 2) ----
"%GODOT%" --path . --host --rede 7932 --vooteste --bpteste 3000000 --horateste 0.5 ^
          --diagboca --position 1920,0 --resolution 1600x900 ^
          --raca Human --conta bancada_boca --nome Boqueiro

echo.
echo  Encerrado. As fotos estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus".
echo  Comece pela boca-1-os-quatro-sentidos.png.
pause
