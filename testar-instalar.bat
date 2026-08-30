@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do INSTALAR

REM ===========================================================================
REM  FABRICAR -> MOCHILA -> INSTALAR  (--diaginstalar)
REM
REM     testar-instalar.bat
REM
REM  O pedido do dono, literal:
REM     "faca q todo item q vc produzir na research table, va parar no inventario
REM      do personagem, ao qual ele pode clicar em instalar (caso seje algo q
REM      coloque no chao e nao um item equipavel de uso pessoal como scouter,
REM      armaduras e pesos), e ao clicar em instalar nesse objeto, basicamente
REM      uma versao transparente dele vai ficar no mouse como um preview de como
REM      vai ficar quando instalar naquele local (isso claramente so aparece pro
REM      jogador local) ao clicar o objeto vai ser instalado nesse local (e nesse
REM      momento q o server vai sincronizar com o resto dos jogadores) e todos
REM      vao poder ver."
REM
REM  Duas rodadas. A primeira NAO abre o Godot:
REM
REM    0) instalar-prova   a bancada de CATALOGO, sem janela e em ~5 s. Ela le o
REM                        `construcoes.json` de verdade e cobra a regra 2 nos
REM                        dois sentidos, item por item: 44 instalaveis e 64 de
REM                        uso pessoal (108 no catalogo), com Armadura/Scouter/
REM                        Pesos/armas do lado de fora e Gravidade/Research
REM                        Station/Telepad do lado de dentro. Os numeros saem do
REM                        `construcoes.json`; se ele mudar, o placar muda junto e
REM                        estas linhas e que ficam velhas -- confie no placar.
REM                        Se ela reprovar, a rodada de jogo nem sobe:
REM                        um catalogo errado faz TODAS as familias de la
REM                        reprovarem em cascata, e a causa esta aqui.
REM
REM    1) --diaginstalar   o CICLO ANDADO, sem janela, ~25 s. Sete familias, cada
REM                        uma com as DUAS metades:
REM
REM       F0  a classificacao lida pela MESMA lista de acoes que o menu desenha;
REM       F1  fabricar entra na mochila **e** nao deixa nada no chao;
REM       F2  a previa e um Sprite2D translucido filho do World, que segue o
REM           mouse **e** nao muda nada do que os outros veem enquanto anda;
REM       F3  a regra 2 dos DOIS lados -- os botoes DESENHADOS da armadura nao
REM           tem "Instalar no chao", e o `posicionar Armor/...` mandado pelo fio
REM           (como faria um cliente mexido) e RECUSADO pelo servidor;
REM       F4  um clique de mouse de verdade instala, a obra aparece na lista que
REM           o servidor manda pra todo mundo, e o item sai da mochila;
REM       F5  a recusa nao custa nada: o item fica, o motivo e dito, e a previa
REM           VOLTA pra mao. Mais o Esc, que cancela sem gastar -- e a frase
REM           "voce guarda ... de volta" lida da CAIXA DE CHAT desenhada;
REM       F6  parede e agua pedidas PELO FIO (como faria um cliente mexido): o
REM           servidor recusa, nada e assentado, o item continua na mochila e a
REM           previa volta pra mao. Mais o botao direito, que cancela sem gastar.
REM
REM       E no fim a INJECAO: as mesmas regras com amostras estragadas, cada uma
REM       obrigada a ficar vermelha.
REM
REM  ============================================================================
REM  A PASTA DE SAVES DO DONO NAO E TOCADA -- E E ESTA LINHA QUE GARANTE ISSO.
REM
REM  O servidor grava `mundo.json`, `naves.json` e as contas dentro do
REM  `user://`, que no Windows e %APPDATA%\Godot\app_userdata\<projeto>. Uma
REM  bancada que assenta e recolhe uma construcao ESCREVE nesse arquivo -- e hoje
REM  uma bancada gravou a morte da Terra no mundo real do dono por causa disso.
REM
REM  Entao a variavel APPDATA e desviada pra uma pasta de rascunho ANTES de o
REM  Godot subir. Nao ha uma linha de codigo envolvida: o proprio Godot resolve o
REM  `user://` a partir dela. Ao terminar, a pasta desviada fica onde esta (nao e
REM  apagada) pra dar pra conferir o que a rodada escreveu.
REM  ============================================================================
REM
REM  `--techteste` da tecnologia 80 e 5 milhoes de zeni -- sem isso nao ha como
REM  comprar nada e o ciclo inteiro fica sem prova.
REM
REM  PORTA PROPRIA (7975): se aparecer "FALHOU ao abrir a porta", ha outra rodada
REM  viva -- feche-a.
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
echo     testar-instalar.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7975

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    REM `-t:Rebuild` e OBRIGATORIO: o build incremental ja deu "compilacao com
    REM exito" sem trocar a DLL, e o Godot subiu com o binario de ontem.
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ---- 0/1: o catalogo, sem janela (a regra 2, item por item) ----
REM SEM `--no-build`: o `dotnet build` la em cima compila o projeto do JOGO, e
REM esta bancada e outro projeto (Tools/AssetPipeline).
dotnet run --project Tools/AssetPipeline -- instalar-prova
if errorlevel 1 (
    echo.
    echo  O CATALOGO REPROVOU. A rodada de jogo so repetiria isso seis vezes --
    echo  leia o placar acima primeiro.
    pause
    exit /b 1
)

REM ---- o desvio da pasta de usuario, e ele vem ANTES do Godot ----
set "APPDATA=%TEMP%\jandirus-bancada-instalar"
if not exist "%APPDATA%" mkdir "%APPDATA%"
echo.
echo  Saves desviados para: %APPDATA%
echo  (a pasta real do dono nao e tocada por esta rodada)

echo.
echo  ---- 1/1: o ciclo andado (procure o placar "[instalar] ===== N OK") ----
"%GODOT%" --headless --path . --host --rede 7975 --techteste --diaginstalar ^
          --raca Human --conta bancada_instalar --nome Instalador

echo.
echo  Encerrado. O que a rodada escreveu esta em
echo     %APPDATA%\Godot\app_userdata\Dragon ball Jandirus
pause
