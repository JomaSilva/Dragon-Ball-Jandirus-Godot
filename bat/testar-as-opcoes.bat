@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- AS OPCOES NO LOBBY, A TELA CHEIA E AS RESOLUCOES

REM ===========================================================================
REM  O PEDIDO DO DONO
REM
REM      testar-as-opcoes.bat
REM
REM  "adicione tb uma opcao no lobby do jogo de sair do jogo, e abrir a tela de
REM   opcoes, pq so da pra fazer dentro do jogo isso e as vezes quero mudar o
REM   volume no lobby e n da. e percebi q mesmo colocando fullscreen com uma
REM   resolucao menor a o jogo n cobre a tela toda, e quando ta no modo janela o
REM   jogo n deveria ter a opcao de colocar na resolucao da minha tela, e sim na
REM   resolucao do fullscreen do modo janela (q e um pouco menor q o 1920x1080
REM   da minha tela por exemplo, mas outras resolucoes isso varia)"
REM
REM  SAO TRES PEDIDOS, e a bancada cobra os tres:
REM
REM    F1/F2  AS OPCOES NO LOBBY -- as TRES telas do lobby (login, selecao e
REM           criacao) ganharam "Opcoes" e "Sair do jogo". Elas se escondem uma
REM           a outra, entao um botao numa so nao serviria. A tela de opcoes e a
REM           MESMA de dentro do jogo, ajustada ao contexto: sem mundo, o titulo
REM           vira OPCOES, "Voltar ao jogo" vira "Fechar" e "Desconectar" SOME
REM           (no lobby ele derrubaria a conexao que segura os slots).
REM           O VOLUME e medido no MISTURADOR (AudioServer.GetBusVolumeDb), e nao
REM           no campo do config -- campo escrito nao e som tocado.
REM
REM    F3     A TRILHA DO LOBBY -- o `Fechar` chamava `PararCamada(Menu)`, e a
REM           musica da tela de login E um pedido da camada Menu. Abrir e fechar
REM           as opcoes no lobby matava a trilha de vez, calado.
REM
REM    F4     TELA CHEIA PREENCHE -- eram DOIS defeitos escondidos um no outro:
REM           em tela cheia a resolucao era ignorada, e escolher resolucao
REM           desligava a tela cheia. "Tela cheia com resolucao menor" era um
REM           estado que o jogo nao alcancava. Agora a resolucao e a BASE DE
REM           DESENHO (stretch canvas_items + expand).
REM           A conta e em PIXEL, e sao TRES: o tamanho do que foi desenhado
REM           contra o tamanho da janela, a contagem de borda preta nos quatro
REM           lados, e a REGUA.
REM
REM           A REGUA existe por um achado da fase de prova: repondo o
REM           `project.godot` DE ORIGEM (sem o `window/stretch/mode`), as duas
REM           primeiras contas ficaram VERDES -- e com razao, porque com o `mode`
REM           morto o viewport acompanha a janela 1:1, cobre tudo e nao ha barra
REM           preta nenhuma. O defeito de origem nunca foi "sobra borda", foi
REM           "a resolucao menor nao faz nada", e contar borda e cego pra isso.
REM           A regua e um retangulo de 100x100 pixels DE CANVAS medido em pixels
REM           DE TELA na foto: sai 150x150 com o conserto, 100x100 sem ele.
REM
REM    F7     A JANELA QUE SAI DA LISTA -- a F5 cobra o cardapio, esta cobra o
REM           prato: escolhe a MAIOR opcao pelo proprio `OptionButton` e mede
REM           onde a janela VESTIDA parou (`WindowGetPositionWithDecorations`),
REM           contra a area util. Mais as duas metades do pedido (a nativa FORA
REM           da lista de janela, DENTRO da de tela cheia) e a ida-e-volta de
REM           modo, que e onde um corte feito so na lista passaria batido.
REM
REM    F5     AS RESOLUCOES -- a lista era CRAVADA (cinco itens, iguais nos dois
REM           modos) e dois deles nao cabiam em janela neste monitor. Agora ela e
REM           DERIVADA: area util (sem a barra de tarefas) menos a moldura da
REM           janela, medida na hora. No monitor do dono da 1904x993. Em TELA
REM           CHEIA a nativa continua oferecida -- a restricao e so do modo
REM           janela. E consertou de quebra o `WindowSetPosition`, que nao somava
REM           a origem do monitor e arrastava TODA bancada de volta pro monitor 1.
REM
REM    F6     SAIR LIMPO -- o "Fechar o jogo" fazia `Stop()` sem gravar nada:
REM           quem hospedava levava junto ate 2 minutos de progresso de todo
REM           mundo. E o X da janela nao passava por lugar nenhum.
REM
REM  AS SEIS LINHAS DE INJECAO SAO O DEFEITO DE ORIGEM reposto nos nodes
REM  de verdade. As mesmas contas TEM que ficar vermelhas ali -- senao as
REM  familias de cima sao decoracao.
REM
REM  A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`): o dono trabalha no
REM  principal. Se a sua tela 2 comeca noutro X, mude o numero.
REM
REM  PRECISA DE JANELA: sem ela nao ha foto, nao ha moldura pra medir e a troca
REM  de resolucao nao acontece de verdade -- que e o que a bancada existe pra ver.
REM
REM  RESIDUO ZERO: ela aperta controles de PRODUCAO, e eles GRAVAM. Por isso ela
REM  copia pra memoria o `config.json`, o `perfis.json` e a pasta `saves\` antes
REM  de comecar, devolve byte a byte no fim, APAGA os arquivos que ela mesma
REM  criou (o save do servidor cria seis) e imprime o SHA256 do config no fim.
REM  Confira: ele tem que ser o mesmo de antes de rodar.
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
echo     testar-as-opcoes.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem, que e
        echo  exatamente como um conserto "que nao faz nada" ja passou verde aqui.
        pause
        exit /b 1
    )
)

echo.
echo  ---- 1/2: opcoes no lobby, volume, trilha, tela cheia e resolucoes ----
"%GODOT%" --path . --diagopcoes --position 1920,0 --resolution 1280x720

REM ===========================================================================
REM  2/2 -- A SAIDA DE VERDADE, E POR QUE ELA E UMA RODADA SEPARADA
REM
REM  A prova de que "o Sair encerra o processo" e o processo TER ENCERRADO. Isso
REM  nao cabe na rodada de cima: uma bancada que morre no meio nao mede mais nada
REM  depois, e nao devolve os arquivos do dono.
REM
REM  Entao aqui a rodada e curta e o veredito e de FORA: o codigo de saida do
REM  processo (`errorlevel`), a lista de processos e a porta 7912. A bancada sobe
REM  um servidor nessa porta de proposito -- sem ele o "sair" so despediria o
REM  cliente, e as duas coisas que interessam (o mundo GRAVADO e a porta
REM  DEVOLVIDA) nao aconteceriam.
REM
REM  CODIGOS: 0 = saiu limpo. 3 = nao achou o botao ou o servidor. 4 = apertou o
REM  botao e 3 s depois o processo ainda estava vivo -- a saida nao encerrou nada.
REM
REM  QUEM GUARDA OS ARQUIVOS DO DONO AQUI E ESTE .BAT, E NAO A BANCADA. A rodada 1
REM  copia tudo pra memoria e devolve no fim; esta rodada NAO PODE fazer isso --
REM  ela morre de proposito, e um processo morto nao devolve nada. E ela grava:
REM  o `SalvarEParar` que estamos medindo escreve o mundo, os cargos, as esferas e
REM  o titulo por cima do que estiver la. Entao a guarda desce um andar, pro .bat:
REM  copia a pasta `saves` ANTES, e depois apaga o que nasceu e devolve o resto.
REM ===========================================================================
set "SAVES=%APPDATA%\Godot\app_userdata\Dragon ball Jandirus\saves"
set "GUARDA=%TEMP%\jandirus-guarda-da-saida"

if exist "%GUARDA%" rd /s /q "%GUARDA%"
md "%GUARDA%" 2>nul
if exist "%SAVES%" xcopy "%SAVES%\*" "%GUARDA%\" /q /y >nul 2>nul

echo.
echo  ---- 2/2: A SAIDA DE VERDADE (o botao do lobby, e o processo morre) ----
"%GODOT%" --path . --diagopcoes --saidareal --position 1920,0 --resolution 1280x720
set "SAIU=%errorlevel%"

REM ---- a pasta do dono volta como estava: o que nasceu sai, o resto e devolvido ----
set "SOBROU=0"
if exist "%SAVES%" (
    for %%F in ("%SAVES%\*") do (
        if not exist "%GUARDA%\%%~nxF" (
            del "%%F" >nul 2>nul
            if exist "%%F" set "SOBROU=1"
        )
    )
    xcopy "%GUARDA%\*" "%SAVES%\" /q /y >nul 2>nul
)
rd /s /q "%GUARDA%" 2>nul

echo.
if "%SOBROU%"=="0" (
    echo   [ OK    ] a pasta "saves" do dono voltou como estava
) else (
    echo   [ FALHA ] sobrou arquivo meu em "%SAVES%"
)

echo.
if "%SAIU%"=="0" (
    echo   [ OK    ] o processo encerrou sozinho, codigo de saida 0
) else (
    echo   [ FALHA ] o processo saiu com codigo %SAIU% -- leia a linha FALHA acima
)

REM SOBROU ALGUEM VIVO? Um servidor local orfao seguraria a porta e continuaria
REM gravando; e exatamente o que o "sair limpo" existe pra impedir.
tasklist /fi "imagename eq Godot_v4.7.1-stable_mono_win64.exe" 2>nul | find /i "Godot" >nul
if errorlevel 1 (
    echo   [ OK    ] nao sobrou processo do jogo vivo
) else (
    echo   [ AVISO ] ainda ha processo do Godot vivo -- pode ser OUTRA bancada sua
)

REM A PORTA VOLTOU PRO SISTEMA?
netstat -ano | findstr /r /c:":7912 .*LISTENING" >nul
if errorlevel 1 (
    echo   [ OK    ] a porta 7912 foi devolvida ao sistema
) else (
    echo   [ FALHA ] alguem ainda escuta na porta 7912
)

echo.
echo  Encerrado. Leia a linha "PLACAR:" e a linha "INJECAO:" da rodada 1.
echo  As fotos estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus":
echo     opcoes-1-login.png              (as duas portas novas no lobby)
echo     opcoes-2-abertas-no-lobby.png   (a tela de opcoes sem mundo nenhum)
echo     opcoes-3-telacheia-preenchida.png
echo     opcoes-4-INJETADO-barra-preta.png   (a barra preta do `aspect = keep`)
echo     opcoes-5-janela-cabe.png
echo     opcoes-8-regua.png                  (a regua esticada 1,5x: a prova do L2)
echo     opcoes-9-INJETADO-config-velha.png  (o `project.godot` DE ORIGEM: regua 1x,
echo                                          tela cheia, e NENHUMA barra preta --
echo                                          e por isso contar borda era cego)
echo     opcoes-10-janela-depois-da-volta.png
pause
