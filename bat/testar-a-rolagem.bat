@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- a ROLAGEM DO CENARIO, medida no pixel

REM ===========================================================================
REM  A ROLAGEM DO CENARIO  (--diagrolagem / --diagrolagemlab)
REM
REM     testar-a-rolagem.bat            as duas bancadas, uma depois da outra
REM     testar-a-rolagem.bat lab        so o laboratorio (nao precisa de rede)
REM     testar-a-rolagem.bat antigo     o laboratorio na grade ANTIGA (controle)
REM     testar-a-rolagem.bat tira       o PAR de imagens antes x depois (ver abaixo)
REM
REM  O RELATO DO DONO:
REM     "a movimentacao pros lados (esquerda e direita) esta estranha ... quando
REM      ando pros lados parece q o personagem fica borrado/tremendo, mas
REM      andando pra cima e pra baixo fica liso sem problemas"
REM
REM  A PROVA CENTRAL E A REGULARIDADE
REM     O corpo nao anda na tela -- a camera e filha dele --, entao quem rola e o
REM     mundo. A bancada pega a sequencia de posicoes do cenario em pixel de TELA,
REM     quadro a quadro, e mede o quanto os deslocamentos consecutivos discordam
REM     entre si, DESCONTADO o relogio do quadro (um quadro que demorou o dobro
REM     andou o dobro, e andou certo). Sobra so o que o arredondamento acrescenta:
REM
REM        DESVIO    tem que ficar abaixo de 0,60 px de tela. Medido: 0,47 com o
REM                  codigo de hoje, 0,94 com a grade antiga -- o dobro exato.
REM        QUANTUM   de quanto em quanto o cenario pode PARAR (divisor comum dos
REM                  deslocamentos). Tem que dar 1. Com a grade antiga da 2, e
REM                  "so parar de dois em dois" e a definicao do defeito.
REM
REM     O chao dessa conta NAO e zero, e isso esta dito no codigo: com passo de
REM     ~2,7 px numa tela de pixel inteiro o cenario anda 2,2,3,2,3 -- um pixel de
REM     hesitacao e aritmetica, nao defeito.
REM
REM  E OS DOIS EIXOS TEM QUE DAR O MESMO VEREDITO
REM     A queixa do dono NAO e "treme": e a DIFERENCA entre os eixos. Uma bancada
REM     que so olhasse pro horizontal ficaria verde num conserto que consertasse
REM     metade. Ela anda nos quatro rumos e compara horizontal com vertical, no
REM     veredito E no numero. Medido: diferenca de 0,001 px.
REM
REM  DUAS VEZES, EM DOIS INSTRUMENTOS
REM     A mesma conta e feita sobre a TRANSFORMACAO de canvas (140 quadros por
REM     rumo) e sobre a FOTO (60 quadros, correlacao cruzada de pixel de verdade),
REM     em leste E em norte. Mais: o cenario tem que andar o que a transformacao
REM     prometeu, o CORPO tem que andar ZERO, e os outros bonecos tem que parar na
REM     mesma grade que o seu.
REM
REM  O CONTROLE E METADE DA PROVA
REM     `testar-a-rolagem.bat antigo` roda o MESMO palco na grade de pixel de
REM     MUNDO (a de antes do conserto). Ela tem que ficar VERMELHA -- 18 falhas,
REM     "o cenario so para de 2 em 2 px de tela". Uma bancada que so sabe ficar
REM     verde nao prova nada.
REM
REM  A TIRA, PRA OLHAR EM VEZ DE LER
REM     `--rolagemtira CAMINHO.png` grava a imagem: kimografo (uma linha de pixel
REM     do cenario por quadro, empilhadas -- listras retas = rolagem parelha) por
REM     cima do tranco (o atraso do desenho contra uma rolagem perfeita, ampliado
REM     20x -- linha reta = liso, zigue-zague = tremor; a LARGURA do zigue-zague
REM     E o tamanho do defeito). Faixa verde/vermelha = veredito; risco deitado =
REM     passo lateral, risco em pe = passo pra cima.
REM     `--rolagemtiraantes CAMINHO.png` cola uma tira anterior AO LADO -- e assim
REM     que o par antes x depois existe, ja que sao duas rodadas do jogo e nenhum
REM     processo fotografa as duas.
REM
REM  JANELA, E NO SEGUNDO MONITOR (`--position 1920,0`): no headless a fase da
REM  foto se declara nao-medida em vez de passar de graca.
REM
REM  EM QUALQUER RESOLUCAO. As contas julgam em pixel de BASE, entao o teto de
REM  0,60 vale igual em janela, em tela cheia nativa e em tela cheia com resolucao
REM  menor. A foto mede TAMBEM em pixel de monitor (linha "NO VIDRO"), que e onde
REM  o preco de uma esticada quebrada aparece -- medido 0,45 px a 1x contra 0,60
REM  a 1,5x. Isso nao reprova: e a resolucao que o dono pediu pra poder usar, e a
REM  tela de opcoes ja rotula a linha com "estica 1,5x, pode cintilar".
REM ===========================================================================

set "GODOT=E:\Users\Joao\Desktop\Godot_v4.7-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
if not exist "%GODOT%" (
  for /d %%D in ("..\Godot_*" "..\..\Godot_*") do (
    for %%F in ("%%D\*_console.exe") do set "GODOT=%%F"
  )
)
if not exist "%GODOT%" (
  echo.
  echo  Nao encontrei o Godot. Aponte na mao:
  echo     set GODOT=C:\caminho\Godot_v4.7.1-stable_mono_win64_console.exe
  echo.
  exit /b 1
)

set "MODO=%~1"

if /i "%MODO%"=="tira" (
  REM  O PAR ANTES x DEPOIS, NUMA IMAGEM SO.
  REM
  REM  O "antes" e a grade de pixel de MUNDO (`--rolagemgrade 1`), que e a de antes
  REM  do conserto. Ela reproduz o defeito SEM editar codigo -- e reproduz o numero
  REM  tambem: a grade sozinha da desvio 0,94 e quantum 2, os mesmos que a injecao
  REM  das duas metades do defeito original (grade de mundo + o snap do motor) deu
  REM  quando foi medida.
  echo.
  echo  === TIRA, metade 1 de 2: o ANTES (grade de pixel de mundo) ===
  "%GODOT%" --path . --diagrolagemlab --rolagemgrade 1 --position 1920,0 ^
            --rolagemtira "%TEMP%\tira-rolagem-antes.png"
  echo.
  echo  === TIRA, metade 2 de 2: o DEPOIS, com o antes colado do lado ===
  "%GODOT%" --path . --diagrolagemlab --position 1920,0 ^
            --rolagemtira "%TEMP%\TIRA-rolagem-antes-e-depois.png" ^
            --rolagemtiraantes "%TEMP%\tira-rolagem-antes.png"
  echo.
  echo  A imagem: %TEMP%\TIRA-rolagem-antes-e-depois.png
  echo  Esquerda = antes ^(faixa VERMELHA^), direita = depois ^(faixa VERDE^).
  goto :fim
)

if /i "%MODO%"=="antigo" (
  echo.
  echo  === CONTROLE: o laboratorio na grade ANTIGA (tem que ficar VERMELHO) ===
  "%GODOT%" --path . --diagrolagemlab --rolagemgrade 1 --position 1920,0
  goto :fim
)

echo.
echo  === LABORATORIO: a grade e a camera, sem rede ===
"%GODOT%" --path . --diagrolagemlab --position 1920,0

if /i "%MODO%"=="lab" goto :fim

echo.
echo  === MUNDO: o corpo do jogador, o MoveRules e o servidor conferindo ===
start "rolagem-servidor" /min cmd /c ""%GODOT%" --headless --path . --server"
ping -n 16 127.0.0.1 >nul
"%GODOT%" --path . --connect 127.0.0.1 --rede 7777 --diagrolagem --position 1920,0 ^
          --raca Human --conta bancada_rolagem --nome Andarilho
taskkill /fi "WINDOWTITLE eq rolagem-servidor*" /f >nul 2>&1

:fim
echo.
pause
