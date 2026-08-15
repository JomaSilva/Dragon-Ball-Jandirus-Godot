@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- A LUZ DOS ATAQUES DE KI

REM ===========================================================================
REM  O PEDIDO DO DONO
REM
REM     testar-luz-de-ki.bat
REM
REM  "beams e ataque de ki deveriam ter LUZ PROPRIA"
REM
REM  E ele NAO pediu um sistema de iluminacao -- ja houve um neste port e ele
REM  mandou REVERTER. O que existe hoje e a luz que ficou de pe: o ambiente do
REM  ciclo do dia, as fogueiras do cenario (`<zona>.luz`) e a luz da aura de
REM  quem esta transformado. O tiro virou MAIS UMA fonte dessas, pelo mesmo
REM  mecanismo -- nao um segundo caminho de luz.
REM
REM  Ela roda DUAS vezes, e a ordem nao e decorativa: quem mede em NUMERO vem
REM  antes de quem fotografa.
REM
REM    1) --headless   familias 1 a 3. O mecanismo (a luz e filha do tiro, a cor
REM                    e a do ki do dono, de dia nao nasce node nenhum), a MORTE
REM                    (zero luz orfa: o tiro que morre e a zona que some levam a
REM                    luz junto) e o TETO com a zona cheia de 256 tiros.
REM
REM    2) COM JANELA   familias 4 a 6. No headless o `GetImage` volta vazio e o
REM                    quadro nao existe -- entao o custo e o pixel so se medem
REM                    aqui. E foi esta metade que achou o que nenhum `if` acha:
REM                    o Godot compoe ate ~16 luzes por PEDACO de cenario e
REM                    DESCARTA o resto calado. Com 64 acesas sobre um chao de um
REM                    pedaco so, 15 chegaram ao chao e 49 sumiram sem aviso.
REM                    E por isso que o teto ALTO e 12 e nao 64: acima do muro do
REM                    motor nao ha o que comprar, e as vagas ainda sao
REM                    DISPUTADAS com a fogueira e com a aura do mesmo pedaco.
REM
REM  AS FOTOS saem em
REM     %%APPDATA%%\Godot\app_userdata\Dragon ball Jandirus\luzdeki-*.png
REM
REM     luzdeki-1-um-pedaco-apagado.png / -2-um-pedaco-aceso.png
REM        as mesmas 64 luzes sobre um chao de UM pedaco. Conte os halos: sao 16.
REM     luzdeki-3-em-blocos-apagado.png / -4-em-blocos-aceso.png
REM        o mesmo cenario em BLOCOS, como o `TileMapLayer` do jogo. Agora as 64
REM        aparecem -- e e essa a prova de que o limite e por pedaco.
REM
REM  AS FOTOS DA NOITE (familia 7) sao as que respondem o pedido com os olhos, e
REM  cada uma carrega o DEFEITO INJETADO no mesmo quadro, ao lado do certo:
REM
REM     luzdeki-5-noite-sem-tiro.png    a noite vazia -- o basal de toda medida.
REM     luzdeki-6-raio-nasce.png        em cima a esquerda o raio; a direita, com
REM        a energia que a analogia com a aura teria dado (o chao mal muda).
REM     luzdeki-7-raio-andou.png        A FOTO QUE VALE: em cima o raio andou e o
REM        clarao andou junto -- a origem ficou preta. Embaixo, o mesmo tiro com
REM        a luz DESGRUDADA dele: o raio esta a direita e o clarao ficou pra tras.
REM     luzdeki-8-raio-sumiu.png        os tres tiros sumiram e o chao voltou ao
REM        breu. O unico clarao que sobrou e a luz ORFA -- o defeito injetado.
REM     luzdeki-9-duas-cores.png        em cima dois kis (vermelho e azul) e dois
REM        claroes; embaixo os mesmos dois com a luz branca -- dois borroes iguais.
REM     luzdeki-10-sem-sombra.png / -11-com-sombra.png   a sombra ligada nas 64.
REM        Ela PICOTA o clarao em quina reta e nao custa tempo medivel: e imagem,
REM        e nao milissegundo, o que `ShadowEnabled = false` esta comprando.
REM
REM  A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`): o dono trabalha no
REM  principal. Se a sua tela 2 comeca noutro X, mude o numero.
REM
REM  SEM REDE E SEM LOGIN nas duas: luz de tiro nao depende de zona nem de
REM  servidor. O que ela toca e um node de tiro, um contador e o quadro.
REM ===========================================================================

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
echo     testar-luz-de-ki.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%

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
echo  ---- 1/2: o mecanismo, a luz orfa e o teto, sem janela (familias 1 a 3) ----
"%GODOT%" --headless --path . --diagluzdeki

echo.
echo  ---- 2/2: O CUSTO E O PIXEL (precisa de janela, no MONITOR 2) ----
"%GODOT%" --path . --diagluzdeki --position 1920,0 --resolution 1280x720

echo.
echo  Encerrado. As fotos estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus".
echo  Compare a luzdeki-2-um-pedaco-aceso.png com a luzdeki-4-em-blocos-aceso.png.
pause
