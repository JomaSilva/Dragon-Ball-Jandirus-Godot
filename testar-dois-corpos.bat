@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada dos DOIS CORPOS

REM ===========================================================================
REM  DOIS CORPOS  (--doiscorposteste)
REM
REM     testar-dois-corpos.bat
REM
REM  Os tres pedidos do dono, medidos COM DOIS CORPOS -- porque nenhum dos tres
REM  e sobre um corpo so:
REM     "pode AGARRAR o inimigo e JOGAR ELE LONGE, ou apertar o botao de agarrar
REM      2 VEZES e poder CARREGAR alguem com vc"
REM     "faca com q personagens N CONSIGAM PASSAR DENTRO DO OUTRO andando ou por
REM      KNOCK BACK ou por ser JOGADO pelo grab. ao COLIDIR (...) a pessoa JOGADA
REM      sofre dano E a pessoa q COLIDIU com o corpo voando TB toma dano"
REM     "o corpo mesmo morto TEM TODAS AS INTERACOES DE UM CORPO VIVO"
REM
REM  Sao 253 provas em ~2 min, SEM JANELA.
REM
REM  AS OITO FAMILIAS, CADA UMA NOS DOIS SENTIDOS
REM     1) AGARRAR      agarra quem pode / NAO agarra quem ja esta preso por
REM                     outro, quem e intocavel, quem esta noutro andar, quem
REM                     esta longe. (E o BONECO do corpo largado CONTINUA
REM                     agarravel -- e decisao escrita, nao esquecimento.)
REM     2) ARREMESSAR   andar segurando JOGA / segurar parado NAO joga
REM     3) CARREGAR     o carregado sobe na MESMA altitude / e NAO paga Ki de voo
REM     4) AS SOLTURAS  nocaute, zona e logout SOLTAM / um aperto sadio NAO se
REM                     desfaz sozinho em trinta tiques
REM     5) COLISAO      a pe, arremessado e por knockback ESBARRA / em andares
REM                     de voo diferentes ATRAVESSA (o contra-exemplo, senao o
REM                     mundo trava e nenhuma linha fica vermelha)
REM     9) O OCUPADO    NO AR: os DEZ estados do `Ocupacao`, um por linha e com o
REM                     nome do estado na linha -- quem soca, guarda, agarra,
REM                     canaliza, carrega Ki, treina, medita, esta em cena, num
REM                     embate ou nocauteado PARA quem voa contra ele e NAO sai
REM                     do lugar / com o mesmo corpo LIVRE quem voa ATRAVESSA
REM                     (o `mob/Cross`), e o ARREMESSO continua empurrando cada
REM                     um desses dez -- o pedido do dono e sobre ANDAR
REM     6) O BAQUE      os DOIS se machucam, uma linha por lado / sem encontro,
REM                     NENHUM dos dois se machuca
REM     7) O CADAVER    fica, apanha, e agarrado e levado VOANDO por outra
REM                     pessoa, e o enterro pela tecla E o faz sumir
REM     8) A VIAGEM     o Outro Mundo NAO regrediu (a regressao mais provavel)
REM    10) A FOTO       o cadaver nasce virado pra onde o morto caiu, sem o membro
REM                     que ele perdeu e com a MESMA mascara de feridas (o
REM                     `A.overlays += overlays` do DM) -- com o DEFEITO INJETADO
REM                     do `Body.Novo()` limpo ficando vermelho; soco no cadaver
REM                     NAO o gira / o vivo continua girando; o cadaver e o
REM                     nocauteado arremessados pousam pra onde deslizaram, sem
REM                     o estalo; quem entra na zona recebe as feridas do cadaver
REM
REM  E NOVE DEFEITOS INJETADOS. Toda afirmacao central passa pelo `Mutacao` (o
REM  mesmo helper da `--provateste`): mede o criterio, ESTRAGA o mundo, exige que
REM  o MESMO criterio reprove, desfaz e exige que ele volte a passar. Uma
REM  checagem que so foi vista passando e indistinguivel de `Checa("...", true)`.
REM
REM  Sete dos oito defeitos sao defeitos que este trabalho JA CONSERTOU,
REM  remontados pelo lado do DADO: o preso vivendo numa lista que o tique nao le,
REM  a grade de colisao descrevendo o quadro passado, o corpo no colo deixando de
REM  ser "carregado", a varredura orfa nao alcancando quem ficou preso, o corpo
REM  destrocado, a cova recusada, e a viagem que ja aconteceu.
REM
REM  POR QUE ELA MATA O HOST: a familia 8 mede a TRIAGEM de verdade, e a triagem
REM  so leva pro Outro Mundo quem tem dono na tela. Zona, posicao, morte,
REM  relogio, aureola, altitude e agarrao sao fotografados e devolvidos no
REM  `finally`, os corpos forjados saem do mundo, os cadaveres que ela produziu
REM  se desfazem, E AS LAPIDES QUE ELA ERGUEU SAEM DO `mundo.json` (senao cada
REM  rodada deixaria tumulos de teste no disco do dono).
REM
REM  A IRMA DELA E A `ver-dois-corpos.bat`, que fotografa as mesmas tres cenas --
REM  esta mede o NUMERO, aquela mede o PIXEL.
REM
REM  CONTA E PORTA PROPRIAS (7942): se aparecer "FALHOU ao abrir a porta", ha
REM  outra rodada viva -- feche-a.
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
echo     testar-dois-corpos.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7942

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
echo  ---- dois corpos: agarrao, colisao e o cadaver (253 provas) ----
echo.
echo   A bancada roda no 1o login e leva uns 40 s. O SERVIDOR CONTINUA DE PE
echo   depois dela. Leia o placar "[dois] ==== N passaram, M falharam ===="
echo   e feche com Ctrl+C.
echo.
"%GODOT%" --headless --path . --host --rede 7942 --doiscorposteste ^
          --raca Saiyan --conta bancada_dois --nome Duplo

echo.
echo  Encerrado.
pause
