@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- o BORRAO e o ALCANCE do dash, medidos

REM ===========================================================================
REM  O DASH DO NPC, EM NUMERO  (--borraoteste)
REM
REM     testar-o-borrao.bat
REM
REM  Os dois relatos do dono, palavra por palavra:
REM     "npcs quando usam DASH n ficam com o EFEITO DE BLUR igual os jogadores"
REM     "o RANGE DO TELEPORTE DO DASH ta mt grande, parece EXTREMAMENTE MAIOR
REM      q os dos jogadores"
REM
REM  Sao 41 provas em ~90 segundos, SEM JANELA.
REM
REM  AS SEIS FAMILIAS
REM     A  O SALTO PIXEL A PIXEL, dos dois lados -- varredura de 40 a 520 px
REM        pelo MESMO `Aproximar`. Separa as tres colunas do jogador (leve,
REM        pesado sem marca, pesado COM marca) da unica que a IA tem.
REM     B  O BIT `Correndo` do cerebro -- o que liga o rastro de CORRER. Medido:
REM        0 em 1000 comandos de perseguicao. Era por isso que o NPC nao borrava.
REM     C  A AFTERIMAGE NO SORTEIO -- quantos NPCs nascem sabendo a skill que
REM        gera a MIRAGEM (que nao e o borrao, e essa e a distincao inteira).
REM     D  O ANUNCIO DO SALTO -- o que sai pelo fio, lido no CARIMBO do escritor
REM        de verdade (`w.Length` = 14 bytes), pelos tres corpos: jogador, NPC e
REM        corpo POSSUIDO (a fera do Oozaru, a furia lendaria).
REM     E  OS SETE DEFEITOS INJETADOS -- ver abaixo.
REM     F  O MESMO VAO, O MESMO PIXEL -- a IA e o jogador medidos no MESMO vao
REM        (80, 150, 220 e 300 px) e comparados contra a REGRA
REM        (`vao - DistanciaDeParada`), e nao so um contra o outro. E a resposta
REM        do relato 2: NAO HA DOIS ALCANCES, ha a MARCA (a IA marca todo tique,
REM        o jogador marca por duplo clique) e ha a TECLA (leve x pesado).
REM
REM  OS SETE DEFEITOS INJETADOS (familia E). Toda afirmacao central passa pelo
REM  `Mutacao` -- o mesmo helper da `--provateste`: mede o criterio, ESTRAGA o
REM  mundo, exige que o MESMO criterio reprove, desfaz e exige que ele volte a
REM  passar. Uma checagem que so foi vista passando e `Checa("...", true)`.
REM     1  o PORTAO ANTIGO do borrao de volta -- `if (zanzo)` no lugar de
REM        `if (investiu)`. Era o relato 1 do dono inteiro.
REM     2  o livro SEM a Afterimage: mata a MIRAGEM e **nao** o borrao (as duas
REM        camadas medidas por lados opostos, no mesmo mundo estragado)
REM     3  o tanque de Ki vazio
REM     4  a recarga de 500 ms de pe
REM     5  o corpo INVISIVEL (o borrao nao pode entregar quem sumiu)
REM     6  a MARCA desfeita -- e a linha do relato 2
REM     7  o temperamento da IA sem PESADO nem INVESTIDA: o alcance dela cai pro
REM        do leve, exatamente como o do jogador
REM
REM  A IRMA DELA E A `ver-o-borrao.bat`, e esta bancada NAO PODE fazer o que
REM  aquela faz: tudo aqui e estado de SERVIDOR, e ela fecharia verde com o
REM  borrao nunca desenhado na tela. Entre o `S2C.Zanzo` e o pixel ha o fio, o
REM  `World.AoPiscar`, a escolha da origem no `LocalPlayer` e o `RastroDeCorrida`
REM  esperando a posicao de CHEGADA.
REM
REM  PORTA 7916. Se aparecer "FALHOU ao abrir a porta", ha outra rodada viva --
REM  feche-a.
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
echo     testar-o-borrao.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7916

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
echo  ---- o dash: alcance e borrao, 41 provas com sete defeitos injetados ----
echo.
echo   A bancada roda no 1o login e leva uns 90 s (as familias A, E e F rodam
echo   perseguicoes em TEMPO REAL -- a recarga do arranque e carimbada com o
echo   relogio de parede, e num laco sincrono ela nunca venceria).
echo   O SERVIDOR CONTINUA DE PE depois dela. Leia o placar
echo   "[borrao] ===== FIM: N ok, M falha(s) =====" e feche com Ctrl+C.
echo.
"%GODOT%" --headless --path . --host --rede 7916 --borraoteste ^
          --raca Saiyan --conta bancada_borrao --nome Borrao

echo.
echo  Encerrado.
pause
