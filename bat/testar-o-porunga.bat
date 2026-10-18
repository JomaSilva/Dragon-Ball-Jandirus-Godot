@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do PORUNGA QUE MORRE COM NAMEK

REM ===========================================================================
REM  O SET DE ESFERAS QUE MORRE COM O PLANETA  (--porungateste)
REM
REM     testar-o-porunga.bat
REM
REM  O PEDIDO DO DONO, LITERAL:
REM     "sim porunga morre em namek quando namek explode, so voltando quando o
REM      planeta e restaurado pelas esferas de outro lugar"
REM
REM  ANTES DISTO, MEDIDO: o Porunga ficava ancorado em Namek depois da explosao
REM  e o zelador (`eternal_maintain`) o mantinha VIVO de segundo em segundo --
REM  num planeta que recusa pouso vindo de orbita, cujo arquivo de passagens tem
REM  2 bytes, e de onde quem deslogou acorda no berco. Preso, e preso VIVO.
REM
REM  A REGRA NOVA NAO GUARDA NADA NOVO: "o Porunga esta morto" NAO e um campo --
REM  e a ausencia dele nas listas mais o `planetas-mortos.json`, que ja existia.
REM  Um `bool` no set seria um segundo lugar pra guardar a mesma verdade.
REM
REM  A BANCADA NAO PERGUNTA SE UMA FUNCAO FOI CHAMADA. Ela mede resultado:
REM
REM     Namek explode           -> pelo COMMIT de producao, um segundo por volta
REM     o Porunga morreu?       -> lido em `_sets` e `_esferas`, as listas do jogo
REM     a volta?                -> `db_desejar curar_planeta Namek`, pelo funil
REM     quem carregava soube?   -> lido no que o JOGADOR OUVE
REM
REM  E CADA AFIRMACAO CARREGA A METADE QUE A DERRUBA, porque afirmacao de um
REM  lado so fica verde num sistema morto:
REM
REM     o Porunga morre com Namek   x  o set da TERRA atravessa a explosao inteiro
REM     a esfera de Namek some      x  a esfera da Terra continua na mesma mao
REM     morto ele NAO E INVOCADO    x  vivo, as mesmas sete na mesma mao o levantam
REM     o tique NAO o ressuscita    x  com Namek viva um tique so o refaz inteiro
REM     reiniciar nao o desenterra  x  com Namek viva a MESMA carga o ergue
REM     o desejo restaura Namek     x  pedir a cura de um mundo VIVO nao cobra
REM     e a volta ATRAVESSA O DISCO x  ...e o set da Terra atravessa junto
REM     o set de jogador morre      x  ...e a ZONA fica livre pra outra estatua
REM     nao se ergue em mundo que   x  ...e abortada a morte o mesmo gesto passa
REM       esta acabando
REM
REM  DUAS FAMILIAS COM DEFEITO INJETADO (mede o codigo de producao, estraga,
REM  mede de novo -- tem que REPROVAR --, conserta, mede de novo):
REM
REM     A. a ancora mentindo -> o set eterno reancorado num mundo VIVO
REM     B. o portao do nascimento -> Namek no livro dos mortos
REM
REM  O QUE ELA MEXE, E QUEM PROTEGE:
REM     o disco inteiro          -> PalcoDeApagamentos (pasta temporaria)
REM     o registro dos mortos    -> PalcoDeMortes
REM     sets, esferas, dominios, trono, raca/classe/BP, adianto do ceu
REM                              -> fotografados e devolvidos no fim
REM
REM  O QUE ELA COBRA: Namek e destruida pelo commit, entao os NPC dela morrem.
REM  Eles nao vao pro disco e voltam na proxima manutencao do povoamento.
REM
REM  NAO PRECISA DE JANELA. Ela roda no PRIMEIRO LOGIN (o set do desejo e de uma
REM  ASSINATURA, que so existe com conta e slot).
REM
REM  PORTA PROPRIA (7979): se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a.
REM
REM  O placar sai no console; LEIA a linha
REM  "===== BANCADA DO PORUNGA: N OK, M FALHA =====".
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
echo     testar-o-porunga.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7979

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem, que e
        echo  exatamente como um conserto "que nao faz nada" ja passou verde aqui.
        pause
        exit /b 1
    )
)

echo.
echo  ---- o set que morre com o planeta (2 defeitos injetados) ----
"%GODOT%" --headless --path . --host --rede 7979 --porungateste ^
          --raca Namekian --conta bancada_porunga --senha teste --nome PorungaBanca

echo.
echo  Encerrado.
pause
