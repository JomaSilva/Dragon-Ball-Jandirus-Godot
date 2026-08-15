@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- o BORRAO do dash, FOTOGRAFADO

REM ===========================================================================
REM  O BORRAO DO DASH, FOTOGRAFADO  (--diagborrao)
REM
REM     ver-o-borrao.bat
REM
REM  A IRMA DESTA BANCADA E A `testar-o-borrao.bat`, que mede os mesmos dois
REM  relatos em NUMERO -- 41 provas, sete defeitos injetados, tudo verde.
REM
REM  **E ela ficaria verde com o borrao nunca desenhado na tela.** Tudo o que
REM  aquela ve e estado de SERVIDOR: o `w.Length` do pacote `S2C.Zanzo`, o
REM  contador de anuncios, a posicao do corpo. Entre esse pacote e o pixel ha o
REM  fio, o `GameClient`, o `World.AoPiscar`, a escolha da origem no
REM  `LocalPlayer` (`OrigemDoSalto`) e o `RastroDeCorrida` segurando o pedido
REM  ate a posicao de CHEGADA existir. E o relato do dono e sobre o que ele VE:
REM  "npcs quando usam DASH n ficam com o EFEITO DE BLUR igual os jogadores".
REM
REM  AS CINCO CENAS
REM     A  CONTROLE        ninguem arranca: ZERO copias de rastro, ZERO tinta.
REM                        E o contra-exemplo que impede "tem tinta em tudo" de
REM                        passar por "o borrao funciona".
REM     B  O MESMO QUADRO  o NPC (faixa de cima) e o JOGADOR (faixa do meio)
REM                        arrancam JUNTOS, e a foto e UMA SO -- e o "igual os
REM                        jogadores" do relato, lado a lado no mesmo pixel.
REM     C  QUEM NAO SALTA  a terceira faixa tem um corpo PARADO, e ela tem que
REM                        ficar limpa. O contra-exemplo mora DENTRO da foto.
REM     D  A FERA          o corpo POSSUIDO (`AssumirOCorpo` -- Oozaru, furia
REM                        lendaria) arranca sozinho, e o rastro tem que comecar
REM                        na origem que veio do SERVIDOR (sem as redeas o
REM                        `_deOndeSai` do cliente nunca foi escrito).
REM     F  O DEFEITO       o portao ANTIGO volta (`if (zanzo)` no lugar de
REM                        `if (investiu)`) e o NPC salta de novo: **o mesmo
REM                        salto de 268 px, e ZERO pixel de rastro**. Duas
REM                        coisas de uma vez: e a foto do ANTES que o dono
REM                        relatou (o corpo aparece no destino e nada conta o
REM                        trajeto), e e a prova de que esta medicao de pixel
REM                        sabe ficar VAZIA -- sem ela, "achei 31 mil px de
REM                        tinta" nao se distingue de "acho tinta em qualquer
REM                        tela". Desfeito o defeito, o rastro volta.
REM     E  O OUTRO         um SEGUNDO PROCESSO arranca e o borrao dele chega aqui
REM                        pelo fio. Ver a nota dos dois processos abaixo.
REM
REM  COMO O PIXEL E MEDIDO. A arvore e PAUSADA e a tela e fotografada QUATRO
REM  vezes: com as copias do rastro, sem elas, com de novo e sem de novo. A
REM  diferenca entre "com" e "sem" e o rastro e MAIS NADA -- sem camera se
REM  mexendo, sem tremor de soco, sem quadro de animacao trocando. E o par
REM  "sem/sem2" e o CHAO DE RUIDO: se ele nao for zero, a conta toda e suspeita
REM  e a bancada diz isso em vez de somar barulho como se fosse tinta.
REM  (A mesma receita da `--diagboca`, inclusive os DOIS QUADROS DE FOLGA a cada
REM  troca de visibilidade: `GetImage` devolve o ULTIMO quadro RENDERIZADO.)
REM
REM  NADA E FORJADO: o salto do NPC sai do `Atacar` (o mesmo `case C2S.Action`),
REM  o do jogador sai da TECLA (`Input.ActionPress("run")` + `"attack"`, que e o
REM  que faz o `LerAcoes` escrever o `_deOndeSai`), a marca sai do `Mirar` (o
REM  duplo clique) e a posse sai do `AssumirOCorpo` (a porta unica das duas
REM  possessoes do jogo).
REM
REM  OS DOIS PROCESSOS. A cena E precisa de um segundo cliente, e nao por
REM  capricho: as cenas A-D ja provam os DOIS ramos do `World.AoPiscar` (o
REM  remoto, pelo NPC, e o local, pelo jogador e pela fera) -- nao ha um
REM  terceiro. Mas "o pacote atravessa o fio ate outra pessoa" e uma afirmacao
REM  sobre o FIO, e num processo so os dois lados sao a mesma memoria. Mesma
REM  razao pela qual a `testar-vista.bat` sobe duas janelas.
REM  O visitante entra com `--socar --socaralvo Quieto --socarpesado`: ele marca
REM  o corpo PARADO da terceira faixa (e nao o fotografo -- socar a camera a
REM  derrubaria no meio da cena) e vem investindo. O fotografo faz a isca ir e
REM  voltar a cada 2,5 s pra reabrir o vao, senao haveria UM salto so.
REM  Sem o segundo processo a cena E diz SEM COBERTURA e nao reprova nada: uma
REM  bancada que falha por falta de vizinho vira ruido.
REM
REM  ELA PRECISA DE JANELA: no headless o `GetImage` volta vazio e as fotos saem
REM  em branco. A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`) porque o
REM  dono trabalha no principal. O zoom vai pra 2x no comeco da cena: em 3x um
REM  salto de 268 px com a camera na chegada poria a origem do rastro a 2 px da
REM  borda esquerda.
REM
REM  AS FOTOS saem em
REM     %APPDATA%\Godot\app_userdata\Dragon ball Jandirus\
REM        borrao-A-controle.png        + -tinta.png + -tira.png
REM        borrao-B-mesmo-quadro.png    + -tinta.png + -tira.png   <-- A FOTO
REM        borrao-D-fera.png            + -tinta.png + -tira.png
REM        borrao-F-defeito.png         + -tinta.png + -tira.png   <-- O ANTES
REM        borrao-E-outro-jogador.png   + -tinta.png + -tira.png
REM     A `-tinta.png` e a MASCARA: fundo preto, e so o que o rastro pintou.
REM     A `-tira.png` e o trio: sem rastro / com rastro / so o rastro.
REM
REM  PORTA PROPRIA (7916). Se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a.
REM
REM  PROCURE, no fim:  [borrao-foto] ===== TUDO OK =====
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
echo     ver-o-borrao.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7916  (dois processos: o fotografo com janela, o visitante sem)

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada fotografaria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ================================================================
echo   O fotografo abre no SEGUNDO monitor. O visitante entra 25 s
echo   depois, sem janela, e vem investindo contra o corpo "Quieto".
echo   A janela fecha sozinha; o placar sai no console.
echo  ================================================================
echo.

REM O ATRASO E `ping` E NAO `timeout`: o `timeout` LE do teclado e morre com
REM "Input redirection is not supported" em qualquer automacao.
REM Sem o separador "--": com ele os argumentos vao parar em GetCmdlineUserArgs()
REM e as flags de bancada saem MUDAS (ver servidor.bat).
REM
REM O VISITANTE ENTRA TARDE (25 s) de proposito: as cenas A, B e D acontecem
REM antes, e um corpo estranho andando pelo palco entraria na conta de pixel
REM delas. Quando ele chega, a bancada ja esta na cena E.
start "borrao-visita" cmd /c "ping -n 26 127.0.0.1 >nul & ""%GODOT%"" --headless --path . --rede 7916 --connect 127.0.0.1 --socar --socaralvo Quieto --socarpesado --socarperto 40 --raca Human --conta bancada_borrao_visita --nome Visita"

"%GODOT%" --path . --host --rede 7916 --diagborrao ^
          --position 1920,0 --resolution 1600x900 ^
          --raca Human --conta bancada_foto_borrao --nome Olheiro

REM O visitante nao sabe que acabou se o host cair antes. Pela LINHA DE COMANDO
REM e nao por titulo: o console.exe relanca a si mesmo num filho que nasce fora
REM da janela que o `start` nomeou, e o filho e quem segura a porta.
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*bancada_borrao_visita*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul
taskkill /F /FI "WINDOWTITLE eq borrao-visita*" >nul 2>nul

echo.
echo  Fotos em: %APPDATA%\Godot\app_userdata\Dragon ball Jandirus\borrao-*.png
echo.
pause
