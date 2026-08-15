@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- o VELORIO (bancada de dois corpos)

REM ===========================================================================
REM  O VELORIO -- A MORTE, O OUTRO MUNDO E A AUREOLA, COM DOIS CORPOS
REM
REM     testar-velorio.bat        (--velorio)
REM
REM  O pedido do dono, literal:
REM     "falta fazer o personagem quando MORRER ir pro OUTRO MUNDO e a AUREOLA
REM      aparecer sobre a cabeca"
REM
REM  ============================ POR QUE DOIS CORPOS ============================
REM  Porque com UM corpo metade das perguntas passa verde estando errada:
REM
REM    * "morto tem aureola" sozinho fica VERDE com `TemAureola => true` -- o jogo
REM      inteiro andaria de aureola e a bancada aplaudiria. So a linha gemea (o
REM      VIVO ao lado, no mesmo quadro, sem nenhuma) fecha essa porta.
REM    * "a aureola aparece no Outro Mundo" fica VERDE com um crivo por LUGAR
REM      (`morto && EhOAlem`) -- que e a correcao ERRADA, a que apagaria a
REM      aureola do `KeepsBody` (o morto que anda entre os vivos) no dia em que
REM      ele for portado. Por isso o corpo de controle e LEVADO PRO ALEM
REM      tambem: vivo, no meio dos mortos, e sem aureola.
REM    * "o morto vai pro Outro Mundo" fica VERDE com "todo corpo caido viaja" --
REM      e ai o NOCAUTEADO viajaria junto. Nocaute e morte usam a mesma pose e o
REM      mesmo corpo no chao: e a aureola que os separa na tela.
REM
REM  ============================ AS DEZ FAMILIAS ============================
REM     1  O CONTROLE     dois corpos vivos, zero aureolas
REM     2  O NOCAUTE      KO nao arma relogio -- e nem com o relogio vencido
REM                       A FORCA o nocauteado viaja
REM     3  O CADAVER      morto no chao dos vivos, SEM aureola (o bug que o dono
REM                       fotografou), e o vivo ao lado tambem sem
REM     4  A VIAGEM       o TIQUE leva pro Outro Mundo, e e AI que ela acende --
REM                       no fio e no pixel
REM     5  AS DUAS CABECAS  morto COM e vivo SEM, lado a lado DENTRO do alem
REM     6  A VOLTA        reviver apaga a aureola sem uma linha propria, e
REM                       ninguem fica preso la
REM     7  QUEM NAO VAI   cidadao, reflexo e boneco largado -- um por linha, com
REM                       o corpo nomeado
REM     8  AS BORDAS      a mente (morrer la nao e morrer), a ponte (zona
REM                       dinamica) e a Sala do Tempo (a tranca abre na viagem)
REM     9  O BALAO        a fala continua chegando na cabeca do morto, e as duas
REM                       coisas cabem la em cima -- medido em PIXEL ACESO, e nao
REM                       na conta de cabeca
REM    10  AS INTERFACES  `INaoSomeComOCorpo` e `ISobeComOCorpo`: a aureola NAO
REM                       declara nenhuma das duas, e a bancada cobra as duas
REM                       ausencias (some com o corpo / sobe no colo do pai)
REM
REM  ============================ 70 PROVAS, E CADA FAMILIA COM O DEFEITO NA FRENTE ============================
REM  "Como reprova" escrito num comentario e uma promessa. As treze injecoes da
REM  tabela no cabecalho de `RoboDoVelorio` foram postas no codigo de producao,
REM  compiladas e rodadas uma a uma -- de `TemAureola => true` (12 linhas
REM  vermelhas) ao `LocalPlayer` voltando a deitar o morto (3). Duas nao foram
REM  pegas na primeira tentativa, e uma delas fez nascer o TERCEIRO corpo desta
REM  bancada (o que chega de longe ja morto).
REM
REM  ============================ UM PROCESSO SO, E ELA SE FECHA ============================
REM  `--host` e servidor E cliente no mesmo processo: o corpo de controle e
REM  forjado no servidor e chega na tela pelo SNAPSHOT, como qualquer vizinho.
REM  A bancada mata o host CINCO vezes, desmonta os corpos que criou e fecha a
REM  janela sozinha (codigo de saida 0 = tudo verde).
REM
REM  Ela e a que JULGA. As outras tres medem e nao se sobrepoem a esta:
REM     --alemteste   servidor, headless: chama a triagem NA MAO e nunca desenhou
REM                   um pixel
REM     --diagforma   chama `MostrarAureola(true)` num boneco local: prova a
REM                   FUNCAO, nao o percurso
REM     --morte a|b   duas telas e FOTOS: a prova de olho, sem julgar triagem,
REM                   borda, balao nem interface
REM
REM  PORTA PROPRIA (7976): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     testar-velorio.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7976

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    REM `-t:Rebuild` e OBRIGATORIO: o build incremental ja deu "compilacao com
    REM exito" sem trocar a DLL, e o Godot subiu com o binario de ontem. Uma
    REM bancada medindo a versao anterior e pior que bancada nenhuma.
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ---- o velorio: dois corpos, dez familias ----
echo.
"%GODOT%" --headless --path . --host --rede 7976 --velorio ^
          --raca Saiyan --conta bancada_velorio --nome Defunto

echo.
echo  Codigo de saida: %errorlevel%   (0 = todas as familias verdes)
pause
