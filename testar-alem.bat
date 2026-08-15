@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do OUTRO MUNDO

REM ===========================================================================
REM  A MORTE, O OUTRO MUNDO E A AUREOLA  (--alemteste)
REM
REM     testar-alem.bat
REM
REM  O pedido do dono, literal:
REM     "falta fazer o personagem quando MORRER ir pro OUTRO MUNDO e a AUREOLA
REM      aparecer sobre a cabeca"
REM
REM  Sao 67 provas em ~15 segundos, SEM JANELA. Ela e de servidor: mede a
REM  VIAGEM, a TRIAGEM e o FIO. O desenho da aureola (a camada sobre a cabeca,
REM  o que some com o Oozaru) e do outro lado, e mora na bancada das FORMAS --
REM  `RoboDeForma.AAureolaSobreACabeca`, que roda com tela.
REM
REM  AS OITO FAMILIAS
REM     1) o LUGAR       as tres zonas do alem, e a mesa do Enma com a inversao
REM                      do Y do BYOND conferida na conta E contra a colisao do
REM                      z6 convertido (cair numa parede prenderia alguem).
REM     2) o FUNIL       `Morrer()` SOZINHO arma o relogio -- prova de que o
REM                      gancho `AoMorrer` esta instalado e de que ninguem
REM                      escreve o prazo a mao.
REM     3) a TRIAGEM     os corpos que NAO vao: NPC do mundo (sai do mundo),
REM                      reflexo da mente e boneco do corpo largado (nada).
REM     4) a VIAGEM      o host MORRE DE VERDADE, cai, sobe, chega na mesa do
REM                      Enma inteiro, de pe, curado e ainda morto.
REM     5) a AUREOLA     ela sai por DIFERENCA e nao por chamada: uma morte
REM                      escrita direto na ficha (mente, G4, gestacao) acende.
REM     6) MORTO PODE    anda no alem; nao ganha Zenkai, nao digere, estamina
REM                      travada em 50%, Ki drenando em dobro.
REM     7) NAO SAI       as passagens do alem so levam ao alem; do alem nao se
REM                      decola; e a recusa nova barra quem tentasse.
REM     8) a VOLTA       o `ReviveMe()` desfaz TUDO o que a morte fez -- e a
REM                      aureola some sozinha, sem linha propria.
REM
REM  POR QUE ELA MATA O HOST: a triagem so leva pro Outro Mundo quem tem dono
REM  na tela (`EhJogador`), e um corpo forjado nao tem `Peer`. Sem um jogador
REM  de verdade a familia central nao teria como acontecer. Zona, posicao,
REM  corpo, Ki e os dois relogios sao fotografados e devolvidos no `finally`.
REM
REM  ELA JA ACHOU UM DEFEITO: o `IrProAlem` nao rearmava o `RelogioDaMorte`, e
REM  o jogador via o Outro Mundo por UM QUADRO (33 ms) enquanto o log anunciava
REM  "60s ate voltar a vida". `Alem.MsNoAlem` estava escrita e o unico
REM  consumidor dela era essa mensagem.
REM
REM  CONTA E PORTA PROPRIAS (7976): se aparecer "FALHOU ao abrir a porta", ha
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
echo     testar-alem.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7976

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
echo  ---- a morte, o Outro Mundo e a aureola (67 provas) ----
echo.
echo   A bancada roda no 1o login e leva uns 15 s. O SERVIDOR CONTINUA DE PE
echo   depois dela -- e a convencao das bancadas de servidor (--salateste,
echo   --genteteste): elas medem DENTRO de um servidor vivo. Leia o placar
echo   "[alem] ==== N passaram, M falharam ====" e feche com Ctrl+C.
echo.
"%GODOT%" --headless --path . --host --rede 7976 --alemteste ^
          --raca Saiyan --conta bancada_alem --nome Falecido

echo.
echo  Encerrado.
pause
