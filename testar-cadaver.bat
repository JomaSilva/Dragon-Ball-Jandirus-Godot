@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do CORPO QUE FICA

REM ===========================================================================
REM  O CADAVER  (--cadaverteste)
REM
REM     testar-cadaver.bat
REM
REM  O pedido do dono, literal:
REM     "ao morrer o corpo deve FICAR NO CHAO ate alguem ENTERRAR ele (basta
REM      apertar E perto do corpo pra enterrar, ou vc pode AGARRAR o corpo e
REM      levar pra outro lugar). o corpo mesmo morto TEM TODAS AS INTERACOES DE
REM      UM CORPO VIVO: pode sofrer dano, ser agarrado, jogado etc, e pessoas
REM      podem agarrar o corpo e SAIR VOANDO com ele, e ele tb vai estar
REM      considerado como VOANDO"
REM
REM  Sao 54 provas em ~15 segundos, SEM JANELA. Ela e a IRMA da `--alemteste`:
REM  as duas medem a MESMA funcao do DM (`mob/proc/Death()`), que produz DOIS
REM  objetos -- o mob que VIAJA (la) e o cadaver que FICA (aqui).
REM
REM  AS OITO FAMILIAS
REM     1) A VIAGEM NAO QUEBROU  o host morre, o prazo vence, ele CHEGA no
REM                              Outro Mundo COM aureola -- e o corpo fica no
REM                              ponto exato, com a aparencia dele, SEM aureola.
REM                              As duas coisas na mesma familia, porque a
REM                              pergunta era se elas brigavam. Nao brigam: e o
REM                              desenho do DM (`GenerateCorpse` no passo 5,
REM                              `loc = locate(...)` no passo 11).
REM     2) NAO VAZA NADA         o cadaver nao carrega BP nem vida do morto --
REM                              nao por corte, por CONSTRUCAO: a ficha dele e
REM                              nova. E ele nao esta no `_players`.
REM     3) AGARRAR               ...e o aperto SOBREVIVE ao tique. E a familia
REM                              que reprova o defeito consertado aqui.
REM     4) CARREGAR              altitude e nado herdados de quem carrega, sem
REM                              campo novo -- e largado no ar ele CAI.
REM     5) ARREMESSAR            o corpo jogado ANDA de verdade.
REM     6) ENTERRAR              nasce lapide (1 dos 5 sprites do `pick`) com o
REM                              epitafio, o corpo some, e longe demais recusa.
REM     7) O TETO DA ZONA        dispara mesmo, leva o MAIS ANTIGO -- e ABAIXO
REM                              do teto nada se desfaz: NAO HA PRAZO.
REM     8) O BONECO TAMBEM       o conserto vale pro corpo de quem esta em
REM                              transe, que ja estava quebrado antes disto.
REM
REM  DOIS DEFEITOS ANTIGOS que ela reprova, e nenhum dos dois era do cadaver --
REM  os dois eram contra o BONECO DO CORPO LARGADO, e a documentacao do repo
REM  afirmava o contrario nos dois casos:
REM     * `TickDoEmpurrao` varria `_players`, entao o boneco recebia
REM       `TiquesDeVoo` e ficava com eles PRA SEMPRE (nunca andava, e ficava
REM       desenhado deitado);
REM     * `TickDoAgarrao` resolvia o preso por `_players.TryGetValue`, entao
REM       agarrar quem esta meditando era desfeito 33 ms depois, calado.
REM
REM  POR QUE ELA MATA O HOST: igual a `--alemteste` -- a triagem so leva pro
REM  Outro Mundo quem tem dono na tela, e sem viagem nao ha cadaver. Zona,
REM  posicao, morte, relogio, aureola e altitude sao fotografados e devolvidos
REM  no `finally`, os corpos forjados saem do mundo, E AS LAPIDES QUE ELA
REM  ERGUEU SAEM DO `mundo.json` (senao cada rodada deixaria tumulos de teste
REM  no disco do dono).
REM
REM  CONTA E PORTA PROPRIAS (7977): se aparecer "FALHOU ao abrir a porta", ha
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
echo     testar-cadaver.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7977

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
echo  ---- o corpo que fica (54 provas) ----
echo.
echo   A bancada roda no 1o login e leva uns 15 s. O SERVIDOR CONTINUA DE PE
echo   depois dela. Leia o placar "[cadaver] ==== N passaram, M falharam ===="
echo   e feche com Ctrl+C.
echo.
"%GODOT%" --headless --path . --host --rede 7977 --cadaverteste ^
          --raca Saiyan --conta bancada_cadaver --nome Defunto

echo.
echo  Encerrado.
pause
