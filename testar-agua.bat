@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada da AGUA

REM ===========================================================================
REM  A AGUA, OLHADA  (--diagagua)
REM
REM     testar-agua.bat
REM
REM  Ela roda QUATRO vezes. A primeira nao abre o Godot:
REM
REM    0) agua-prova    a bancada de REGRAS, sem janela e em ~4 s: sete familias
REM                     (uma por negacao do pedido) e 22 defeitos INJETADOS, cada
REM                     um obrigado a deixar a sua familia vermelha. Se ela
REM                     reprovar, as tres rodadas de foto nem chegam a subir.
REM
REM  As outras tres medem coisas OPOSTAS, e e por isso que sao tres bercos e nao
REM  um: a de dentro nasce com o problema de ENTRADA ja resolvido, e ficou verde
REM  durante os dois defeitos que so as outras duas pegam.
REM
REM    1) --aguateste   nasce na BEIRA de um lago estreito, com um vizinho na
REM                     outra margem. Responde os itens 1, 4 e 5 do pedido:
REM                     a pe a agua BARRA, socar nao faz nada com ela, e quem
REM                     esta do outro lado APARECE (agua nao entra no `.vis`).
REM                     E o gesto do jogador: apertar N na beira e ENTRAR.
REM
REM    2) --aguadentro  nasce DENTRO do lago -- o estado que o proprio servidor
REM                     preve ("deslogar dentro do lago, ser jogado la por um
REM                     arremesso"). Responde os itens 2 e 3: nadando, a pose e
REM                     a do voo, SEM sombra e SEM subir; e o mesmo corpo, no
REM                     mesmo ponto, VOANDO, pra comparacao lado a lado.
REM
REM    3) --aguanoar    nasce NO AR, por cima do meio do lago. E o outro caminho
REM                     que o jogador tem pra comecar a nadar, e o unico que
REM                     passa pelo `DescerAte`: apertar N no ar, POUSAR em cima
REM                     da agua sem ser desviado pra margem, e sair nadando.
REM
REM  PRECISA DE JANELA. No headless o `GetImage` volta vazio e as fotos saem
REM  em branco -- e aqui a foto E o teste.
REM
REM  AS FOTOS saem em
REM     %%APPDATA%%\Godot\app_userdata\Dragon ball Jandirus\agua-*.png
REM  e a que responde o item 3 sozinha e a  agua-8-nadar-x-voar.png  (os dois
REM  retratos colados: a esquerda nadando, a direita voando).
REM
REM  `--horateste 0.5` crava MEIO-DIA: a hora do mundo e sorteada, e uma foto
REM  de lago as 3 da manha nao responde "da pra ver que e agua?" pra ninguem.
REM  `--vooteste` da a skill de voo (sem voo nao ha comparacao) e `--bpteste`
REM  da forca de derrubar cenario, que e o contra-exemplo do soco na agua.
REM
REM  PORTA PROPRIA (7980): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     testar-agua.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7980

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

REM ---------------------------------------------------------------------------
REM  A BANCADA SEM JANELA VEM PRIMEIRO, e ela e o portao.
REM
REM  Sao 48 provas e 22 defeitos injetados em ~4 segundos, sem Godot e sem foto
REM  (`Tools/AssetPipeline/AguaBench.cs`). As fotos abaixo custam um minuto cada
REM  e precisam de tela -- rodar elas com a regra quebrada e gastar um minuto pra
REM  descobrir o que uma funcao pura ja tinha respondido.
REM
REM  Ela sai com codigo 1 se alguma familia ficar vermelha OU se alguma ficar
REM  CEGA (verde com o defeito injetado dentro) -- as duas coisas sao falha.
REM ---------------------------------------------------------------------------
echo.
echo  ---- 0/3: as regras, sem janela (48 provas, 22 defeitos injetados) ----
REM SEM `--no-build`: o `dotnet build` la em cima compila o projeto do JOGO, e a
REM bancada e outro projeto (Tools/AssetPipeline). Com `--no-build` ela rodaria a
REM versao de ontem -- exatamente o erro que o compile-antes-de-medir evita.
dotnet run --project Tools/AssetPipeline -- agua-prova
if errorlevel 1 (
    echo.
    echo  A BANCADA DE REGRAS REPROVOU. As fotos nao vao consertar isso -- leia
    echo  o placar acima antes de gastar dois minutos de tela.
    pause
    exit /b 1
)

echo.
echo  ---- 1/3: na BEIRA do lago (itens 1, 4 e 5, e o gesto de ENTRAR) ----
"%GODOT%" --path . --host --rede 7980 --aguateste --vooteste --bpteste 100000 --horateste 0.5 ^
          --diagagua --resolution 1920x1080 --raca Human --conta bancada_agua --nome Nadador

echo.
echo  ---- 2/3: DENTRO do lago (itens 2 e 3) ----
"%GODOT%" --path . --host --rede 7980 --aguadentro --vooteste --bpteste 100000 --horateste 0.5 ^
          --diagagua --resolution 1920x1080 --raca Human --conta bancada_agua --nome Nadador

echo.
echo  ---- 3/3: NO AR sobre o lago (o pouso em cima da agua) ----
"%GODOT%" --path . --host --rede 7980 --aguanoar --vooteste --bpteste 100000 --horateste 0.5 ^
          --diagagua --resolution 1920x1080 --raca Human --conta bancada_agua --nome Nadador

echo.
echo  Encerrado. As fotos estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus".
pause
