@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- A TELA DE EXCLUIR PERSONAGEM

REM ===========================================================================
REM  O PEDIDO DO DONO
REM
REM     testar-a-tela-de-apagar.bat
REM
REM  "a tela de DELETAR PERSONAGEM ta TODA TORTA, e se eu coloco em FULL SCREEN
REM   o jogo e dps em JANELA, ela MUDA DE POSICAO e fica todo torto. coloque ele
REM   pra ficar SEMPRE FIXO NO CENTRO DA TELA e MELHORE O LAYOUT dele pq ta todo
REM   bagunçado"
REM
REM  Eram DOIS defeitos e os dois vinham do TIPO DO NODE (um `ConfirmationDialog`
REM  do Godot), nao de uma conta de posicao errada:
REM
REM    1. SOBREPOSICAO -- um `AcceptDialog` da a TODO filho Control o retangulo
REM       INTEIRO da area de conteudo, o mesmo onde o Label do `DialogText` ja
REM       esta. Por isso o campo de digitar nascia POR CIMA do texto de aviso.
REM    2. DESLOCAMENTO -- `PopupCentered()` centra UMA VEZ, na abertura, e guarda
REM       a posicao em pixels. Trocar de tela cheia pra janela nao re-centra.
REM
REM  A BANCADA MEDE OS DOIS EM PIXEL, e nao "parece certo":
REM
REM    * a DISTANCIA DO CENTRO da caixa ate o centro da tela, em quatro paradas
REM      -- janela 1280x720, TELA CHEIA com a caixa ABERTA, de volta pra janela
REM      SEM FECHAR (o caminho que o dono descreveu) e janela pequena 800x600;
REM    * a AREA DE SOBREPOSICAO entre todo par de elementos da coluna, em px².
REM
REM  E ela conferece a trava do nome, que e o que uma tela destrutiva precisa:
REM  nome errado nao apaga, nome errado com o BOTAO ACESO NA MARRA nao apaga,
REM  pacote mandado na mao com nome errado e recusado pelo servidor, e o nome
REM  certo apaga -- so aquele slot, e pra sempre (ela reloga pra provar).
REM
REM  AS DUAS ULTIMAS LINHAS SAO O DEFEITO DE ORIGEM REPOSTO nos nodes de verdade
REM  (desancorar a caixa, e por o campo em cima do aviso). As mesmas duas contas
REM  TEM que ficar vermelhas ali -- senao as familias de cima sao decoracao.
REM
REM  A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`): o dono trabalha no
REM  principal. Se a sua tela 2 comeca noutro X, mude o numero.
REM
REM  PRECISA DE JANELA: `GetViewport().GetTexture()` volta vazio no headless, e
REM  sem janela a troca de resolucao nao acontece de verdade -- que e justamente
REM  o que esta bancada existe pra medir.
REM
REM  PORTA PROPRIA (7902): ela SOBE UM SERVIDOR e cria dois personagens de
REM  mentira numa conta so dela (`diagapagar`). Se aparecer "nao consegui abrir
REM  a porta", ha outra rodada viva -- feche-a.
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
echo     testar-a-tela-de-apagar.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7902

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
echo  ---- a tela de excluir: centro, sobreposicao, trava do nome e 2 injecoes ----
"%GODOT%" --path . --diagapagar --position 1920,0 --resolution 1280x720 --rede 7902

echo.
echo  Encerrado. Leia a linha "PLACAR:" e a linha "INJECAO:".
echo  As fotos estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus":
echo     apagar-1-janela.png  apagar-2-tela-cheia.png  apagar-3-cheia-para-janela.png
echo     apagar-4-pequena.png
echo     apagar-5-INJETADO-fora-do-centro.png   apagar-6-INJETADO-sobreposto.png
echo  As duas ultimas sao o defeito de ORIGEM -- e o que a foto do dono mostrava.
pause
