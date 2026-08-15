@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- a prova do mundo (semente + roupa de NPC)

REM ===========================================================================
REM  A PROVA DO MUNDO  (--mundoprova)
REM
REM     testar-mundo.bat
REM
REM  O PAR CENTRAL dos dois pedidos do dono, e as duas metades se seguram:
REM
REM     "percebi q os NPCS estao sempre nascendo IGUAIS a cada WIPE e o
REM      UNIVERSO tb, como se a SEED DELES N MUDASSE"
REM     "todo npc ta nascendo SEM ROUPAS, coloque roupas neles, mas claro
REM      ROUPA DE SAIYAJIN pra saiyajins e ROUPAS COMUNS pra humanos, e
REM      ROUPAS DE NAMEK pra nameks"
REM
REM  Oito familias, e cada uma com o DEFEITO INJETADO -- o padrao da
REM  `--provateste`: mede o codigo de producao, estraga, mede de novo (tem que
REM  REPROVAR), conserta, mede de novo. Uma checagem que so foi vista passando
REM  e indistinguivel de `Checa("...", true)`.
REM
REM     1. dois mundos NOVOS dao mundos DIFERENTES (universo, planetas, gente,
REM        nomes, classes E a roupa deles)
REM     2. o MESMO mundo, relido do save, da o MESMO mundo
REM     3. a semente sobrevive ao restart e SOME no wipe
REM     4. save antigo sem o campo mantem o mundo de hoje
REM     5. todo NPC nasce vestido, e a roupa combina com a RACA -- uma linha
REM        por raca, com a peca NOMEADA
REM     6. a mesma semente veste o mesmo NPC igual (caminho E cor)
REM     7. a roupa sobrevive a reposicao pela manutencao
REM     8. o chefe de saga continua com a aparencia dele
REM
REM  AS DUAS PRIMEIRAS SAO OPOSTAS DE PROPOSITO: a semente constante (o estado
REM  em que o dono achou o jogo) passa na 2 com nota cheia, e a semente sorteada
REM  a cada boot (a correcao apressada) passa na 1 com nota cheia. So as duas
REM  juntas dizem o que o dono pediu.
REM
REM  NAO PRECISA DE JANELA e nao toca na pasta de saves de verdade: as familias
REM  1 a 4 rodam dentro do temporario da bancada da limpeza (`NaCaixa`), com o
REM  mesmo `finally` que devolve o mundo do dono.
REM
REM  PORTA PROPRIA (7958): se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a. A bancada roda no BOOT, antes de a porta abrir,
REM  entao o placar sai mesmo se a porta estiver ocupada.
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
echo     testar-mundo.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7958

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
echo  ---- a prova do mundo (8 familias, 27 defeitos injetados) ----
echo  A bancada sai no console com o prefixo [mundo]. Ela roda no BOOT; o
echo  servidor fica de pe depois dela -- feche esta janela quando o placar sair.
echo.
"%GODOT%" --headless --path . --server --port 7958 --mundoprova

echo.
echo  Encerrado.
pause
