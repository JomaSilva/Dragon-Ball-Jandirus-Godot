@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancadas da FUSAO

REM ===========================================================================
REM  A FUSAO INTEIRA, EM QUATRO RODADAS  (--diagfusaolook / --diagcenafusao /
REM                                       --cenafusaoteste / --fusaoduplateste)
REM
REM     testar-fusao.bat
REM
REM  As tres respondem PERGUNTAS DIFERENTES e a ordem e a da divida: quem a
REM  fusao E, depois a cena que a mostra nascendo, depois a regra de que ela so
REM  nasce no FIM daquela cena.
REM
REM    1) --diagfusaolook   QUEM ELA E. A energia (os onze pontos da tabela do
REM                         dono), o nome (Metamoro e Potara dos MESMOS dois dao
REM                         nomes DIFERENTES), a roupa (o colete SUBSTITUI, o
REM                         brinco SOMA), o cabelo (Goku+Vegeta = Vegito) e o
REM                         vermelho do SSJ4 medido nos pixels da folha.
REM
REM    2) --diagcenafusao   A CENA. O roteiro (a virada no FIM DA ANIMACAO da
REM                         luz -- 0,7 s de folha, e nao um prazo escrito a mao
REM                         --, o branco so ali, o clarao so ali), a arte
REM                         (`FusionLight.tres` e `fusion.wav` IMPORTADOS, e nao
REM                         "estao na pasta", com a duracao da folha conferida
REM                         contra a constante do Core), o tocador com DOIS
REM                         corpos (UMA luz, no ponto MEDIO, no dobro do tamanho,
REM                         em UM estouro que COBRE os dois e se apaga na virada)
REM                         e o branco medido no material.
REM
REM    4) --fusaoduplateste A FUSAO ENTRE **DOIS JOGADORES**, de ponta a ponta.
REM                         As tres de cima medem PEDACO: funcoes puras, desenho,
REM                         e o instante da virada com o `ComecarACenaDaFusao`
REM                         chamado NA MAO. Esta atravessa a corrente inteira com
REM                         dois corpos no mundo -- o `Convidar` do verb, o
REM                         pendente na mesa do outro, o `ResponderAoConvite`, as
REM                         letras do quick time event pelo mesmo `TeclaDaDanca`
REM                         da tecla do jogador, a cena, a virada e o `Separar`.
REM                         Os portoes (raca, poder proximo, skill nos DOIS), as
REM                         DUAS metades da coreografia errada, a heranca, os
REM                         nomes, a energia drenada num corpo de verdade e as
REM                         bordas -- mais a fusao NAMEKUSEIJIN (secao J), o corpo
REM                         inteiro ao fundir (K), a recarga de 1 h que atravessa
REM                         o logout (L) e **o PUXAO da Potara** (M) -- os dois
REM                         andando um pro outro a 1280 px/s, o input desligado
REM                         nos dois, a cena comecando quando ENCOSTAM, e quem
REM                         nao se alcanca nao fundindo. TREZE defeitos injetados
REM                         pelo `Mutacao`.
REM
REM    3) --cenafusaoteste  A VIRADA, no SERVIDOR. Que ate ela **nada foi feito**
REM                         -- sem BP somado, sem skill emprestada, sem
REM                         passageiro selado -- e que os quatro cortes (nocaute,
REM                         morte, zona, logout) derrubam a cena sem deixar
REM                         meio-corpo nem corpo preso.
REM
REM  NENHUMA DELAS PRECISA DE JANELA. As duas primeiras rodam `--headless` e
REM  saem sozinhas; a terceira sobe um servidor, roda a bancada no boot e fica
REM  de pe (servidor nao sai sozinho) -- e por isso ela e a ULTIMA e este script
REM  a fecha por voce quando ela acaba de imprimir o placar.
REM
REM  PORTA PROPRIA (7966): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     testar-fusao.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7966

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
echo  ---- 1/3: QUEM A FUSAO E (nome, roupa, cabelo, o vermelho do SSJ4) ----
"%GODOT%" --headless --path . --diagfusaolook
if errorlevel 1 (
    echo.
    echo  A bancada de APARENCIA reprovou. As outras duas nao consertam isso.
    pause
    exit /b 1
)

echo.
echo  ---- 2/3: A CINEMATICA (a luz sobre os dois, as ondas, a pedra, o branco) ----
"%GODOT%" --headless --path . --diagcenafusao
if errorlevel 1 (
    echo.
    echo  A bancada da CENA reprovou -- leia o placar acima.
    pause
    exit /b 1
)

REM ---------------------------------------------------------------------------
REM  A TERCEIRA SOBE UM SERVIDOR e nao sai sozinha: a bancada roda no boot,
REM  imprime o placar e o servidor segue de pe (como toda `--*teste` deste
REM  projeto). O `timeout` da a ela folga de sobra pra imprimir, e depois ela e
REM  fechada -- sem isso o script ficaria pendurado pra sempre.
REM
REM  ============ E O FECHAMENTO E **POR LINHA DE COMANDO**, NAO POR NOME ============
REM  `taskkill /im Godot_...console.exe` mataria TODO Godot aberto na maquina --
REM  inclusive o editor e qualquer outra bancada rodando em paralelo. Aqui so
REM  morre o processo cuja linha de comando tem `--cenafusaoteste`, que e o que
REM  este script subiu.
REM ---------------------------------------------------------------------------
echo.
echo  ---- 3/3: A VIRADA NO SERVIDOR (a fusao so existe no fim da cena) ----
start "" /b "%GODOT%" --headless --path . --server --rede 7966 --cenafusaoteste
timeout /t 25 /nobreak >nul
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*--cenafusaoteste*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul

echo.
echo  ---- 4/4: A FUSAO ENTRE DOIS JOGADORES, DE PONTA A PONTA (157 provas) ----
start "" /b "%GODOT%" --headless --path . --server --rede 7967 --fusaoduplateste
timeout /t 25 /nobreak >nul
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*--fusaoduplateste*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul

echo.
echo  ---- fim. Os quatro placares estao acima. ----
echo.
echo   E a metade do PIXEL e a `ver-a-fusao.bat`: a `FusionLight` sobre os dois,
echo   o corpo branco no climax, a metamoro e a potara LADO A LADO e o SSJ4 de
echo   cabelo vermelho. Ela precisa de janela.
echo.
pause
