@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada da AGONIA DE UM PLANETA

REM ===========================================================================
REM  A AGONIA DE UM PLANETA  (--diagagonia)
REM
REM      ver-um-planeta-morrer.bat
REM
REM  O pedido do dono, literal:
REM     "quem ta vendo do espaco o planeta deveria ficar com uns efeitos (pode
REM      ser via shaders) na mesma ideia dos ferimentos procedurais nos
REM      personagens, so q seria um efeito meio avermelhado a lembra magma, e
REM      rachaduras no planeta, q vai se intensificando durante esses 5 minutos,
REM      ate acontecer uma mega explosao (via shaders e bem bonita de se ver) e
REM      assim o planeta some"
REM
REM  ---- POR QUE ELA PRECISA DE JANELA ----
REM  Porque TODAS as perguntas dela sao sobre PIXEL. Este projeto tem quatro
REM  defeitos visuais registrados que passaram por quatro mil checagens verdes
REM  porque a bancada media INTENCAO -- `SetShaderParameter` devolve void, nunca
REM  falha, e continua devolvendo void com o shader inteiro sem compilar.
REM  No headless ela diz que nao mediu, em vez de passar de graca.
REM
REM  E os dois defeitos que ESTA bancada pegou, os dois so pela FOTO:
REM     * no auge da agonia o planeta virava um disco de ruido AMARELO, sem uma
REM       rachadura reconhecivel -- repintado, e nao rachado. Todas as checagens
REM       numericas estavam verdes ("o disco mudou", "ele avermelhou");
REM     * a mega explosao desenhava ABAIXO do proprio planeta: o `ZIndex` de um
REM       filho e RELATIVO ao do pai, e o pai e -60. As tres checagens de codigo
REM       passaram (o node existe, o material existe, o tween anda).
REM
REM  ---- AS DEZ FAMILIAS ----
REM     1) O CONTROLE          os DOIS discos VIVOS, limpos, fotografados, e a
REM                            regiao de amostragem MEDIDA no alfa deles. Ela NAO
REM                            nasce dentro do estado que testa
REM     2) O PIXEL MUDA        a agonia no auge contra o controle
REM     3) A RAMPA             treze degraus: sobe, CHEGA LONGE, nao pula, comeca
REM                            no piso do Core e avermelha por RAZAO entre canais
REM                            (magma, e nao "mudou de cor")
REM     4) O ESTOURO           antes do prazo NAO estourou; no prazo estourou; e
REM                            a explosao acende a tela sem virar tela cheia
REM     5) O MUNDO SOME        o node se recolhe E o pixel confirma: onde a Terra
REM                            estava sobrou fundo, e o controle continua la
REM     6) AS PEDRAS           a agonia levanta pedra do chao, a densidade segue
REM                            a rampa, o custo nao acompanha o mapa, e com o
REM                            planeta vivo o chao fica limpo
REM     7) O DETERMINISMO      duas telas com a MESMA semente veem as MESMAS
REM                            pedras nas MESMAS celulas -- e uma semente
REM                            diferente poe pedra em outro lugar
REM     8) O CONTRA-EXEMPLO    NAMEK VIVA no mesmo quadro nao avermelha um pixel
REM                            enquanto a Terra dobra de vermelhidao
REM     9) A TIRA              seis quadros lado a lado, num arquivo so
REM    10) A PEDRA NO PIXEL    onde o node diz que ha pedra, a TELA mudou
REM
REM  ---- O DEFEITO INJETAVEL ----
REM     ver-um-planeta-morrer.bat   -> tem que fechar 37 OK, 0 FALHA
REM     (o mesmo) com --agoniachata -> tem que fechar VERMELHO (4 falhas)
REM  A bandeira faz a rampa nao andar: o planeta fica igual do comeco ao fim.
REM  Ela existe porque a checagem "a rampa nunca desce" fica VERDE numa rampa
REM  chata, e um crivo que nunca corta e indistinguivel de crivo nenhum.
REM
REM  As SEIS FOTOS e a TIRA saem na pasta de dados do jogo, em agonia-*.png.
REM  O veredito e o numero; a foto e o que deixa alguem discordar dele.
REM
REM  SEM REDE E SEM SERVIDOR: o que ela toca sao um `PlanetaDesenhado`, um
REM  `GameClient` sem conexao (so pra a conversao "faltam -> intensidade" ser a
REM  de producao) e o quadro desenhado. Ela nao escreve nada no mundo do dono.
REM
REM  A JANELA ABRE NO SEGUNDO MONITOR (--position 1920,0).
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
echo     ver-um-planeta-morrer.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%

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
echo  ---- a Terra rachando, o magma subindo e a mega explosao ----
echo.
echo   Procure o placar:  ===== FIM: N OK, 0 FALHA(S) =====
echo   E OLHE a tira:  agonia-tira-do-espaco.png  ^(0 = Namek viva, 1 a 5 = a Terra^)
echo.
"%GODOT%" --path . --diagagonia --position 1920,0 --resolution 1280x720

echo.
echo  Encerrado.
pause
