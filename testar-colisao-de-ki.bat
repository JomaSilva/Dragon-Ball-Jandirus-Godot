@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- A COLISAO DE KI: OS FEIXES SE EMPURRANDO E O EMPATE

REM ===========================================================================
REM  O PEDIDO DO DONO, E ELE TEM DUAS METADES
REM
REM      testar-colisao-de-ki.bat
REM
REM  "a ideia e ter um QTE IGUAL DO ZANZOCLASH, mesma ideia, so q CADA ACERTO
REM   EMPURRA O BEAM DO INIMIGO PRA TRAS, ate o SEU BEAM ENCOSTAR NO INIMIGO,
REM   ai vc VENCE. o timer e bem maior, uns 15 SEGUNDOS. se NINGUEM VENCER em
REM   15 segundos, acontece uma EXPLOSAO, e AMBOS os jogadores sofrem um DANO e
REM   sao JOGADOS PRA TRAS pela ONDA DE CHOQUE e o duelo acaba ai em EMPATE"
REM
REM  Ela roda TRES vezes, e a ordem nao e decorativa: quem mede em NUMERO vem
REM  antes de quem fotografa, porque uma foto custa um minuto de tela pra
REM  mostrar o que uma funcao pura ja respondeu em quatro segundos.
REM
REM    1) --embatekiteste  sem janela. 87 afirmacoes: o gatilho, a fisica do
REM                        medidor, a escada de poder de 1x a 6x, o PIXEL que
REM                        cada acerto empurra, o encontro CHEGANDO ao corpo
REM                        (encostar = vencer), o preco do empate, o contra-
REM                        exemplo (quem vence antes NAO explode) e as tres
REM                        bordas (prazo, sair do jogo, ficar sem Ki).
REM
REM    2) --pressateste    sem janela. SER RAPIDO PAGA, nos dois embates -- e a
REM                        familia 6 e a que fecha a porta do abuso: um cliente
REM                        que responde em 1 ms nao passa do PISO de cadencia.
REM                        Ela injeta os dois defeitos opostos (metronomo e piso
REM                        zero) e exige que cada regra fique vermelha.
REM
REM    3) --diagembateki   AS FOTOS. E a unica que precisa de JANELA: no headless
REM                        o `GetImage` volta vazio. Ela responde a metade que o
REM                        numero nao pega -- as duas de cima leem o `Feixe.Pos`
REM                        do SERVIDOR e ficariam verdes com os dois feixes
REM                        DESENHADOS do mesmo tamanho.
REM
REM  AS FOTOS saem em
REM     %%APPDATA%%\Godot\app_userdata\Dragon ball Jandirus\embateki-*.png
REM  e as que respondem sozinhas sao as TIRAS, que a propria bancada cola:
REM     embateki-tira-do-empurrao.png   encontro / empurrando / vitoria
REM     embateki-tira-do-empate.png     a explosao, e os dois voando
REM
REM  `--horateste 0.45` adianta o relogio pra o comeco da tarde: a hora do mundo
REM  e sorteada, e uma foto de duelo as 3 da manha mostra dois vultos. O CLIMA
REM  continua sorteado (o `--climateste` so forca clima RUIM, nao o limpo), entao
REM  a foto pode sair com chuva -- os feixes aparecem do mesmo jeito.
REM
REM  A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`): o dono trabalha no
REM  principal. Se a sua tela 2 comeca noutro X, mude o numero.
REM
REM  PORTA PROPRIA (7910). Os passos 1 e 2 NAO SE FECHAM SOZINHOS quando ha
REM  outra rodada viva na mesma porta -- se aparecer "FALHOU ao abrir a porta",
REM  feche a outra. Quando o placar
REM       ================ N passaram, N falharam ================
REM  aparecer, feche com Ctrl+C pra o passo seguinte comecar.
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
echo     testar-colisao-de-ki.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7910

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
echo  ---- 1/3: a colisao de ki no NUMERO, sem janela ----
"%GODOT%" --headless --path . --server --port 7910 --embatekiteste

echo.
echo  ---- 2/3: ser rapido paga, e o piso segura o martelo ----
"%GODOT%" --headless --path . --server --port 7910 --pressateste

echo.
echo  ---- 3/3: AS FOTOS (precisa de janela, no MONITOR 2) ----
"%GODOT%" --path . --host --rede 7910 --horateste 0.45 ^
          --diagembateki --position 1920,0 --resolution 1600x900 ^
          --raca Human --conta bancada_embateki --nome Embate

echo.
echo  Encerrado. As fotos estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus".
echo  Comece pela embateki-tira-do-empurrao.png.
pause
