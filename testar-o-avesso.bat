@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do AVESSO (a corrente inteira + o procurador sob ataque)

REM ===========================================================================
REM  A CORRENTE INTEIRA E O PROCURADOR SOB ATAQUE  (--avessoteste)
REM
REM     testar-o-avesso.bat
REM
REM  FASE 3 do pedido do dono: provar PELO AVESSO. As duas bancadas anteriores
REM  medem o sistema; esta mede se as OUTRAS DUAS sabem ficar vermelhas.
REM
REM  POR QUE ELA EXISTE, SE JA HA DUAS. Porque as duas tem o MESMO cego:
REM  **nascem dentro do estado**. A da Fase 2 forja um jogador ja com as sete
REM  Super Esferas na mao (`DarSupers`) e teleporta as sete comuns pro colo de
REM  quem vai pedir (`PorODragaoDePe`) -- entao nenhuma delas jamais testou
REM  ACHAR, PEGAR nem REIVINDICAR. Esta atravessa a corrente inteira, do nada
REM  ate o efeito no corpo, so por verbo de producao:
REM
REM     erguer -> o radar achar -> viajar -> pegar (uma a uma) -> reunir ->
REM     invocar -> escolher -> o zeni entrar no bolso -> as sete SUMIREM
REM
REM     achar no espaco -> chegar perto -> reivindicar -> os 10 s correrem no
REM     tique -> disputar a de OUTRO por 5 min -> as sete -> a lingua RECUSAR ->
REM     emprestar a voz -> e o desejo cair em quem PEDIU
REM
REM  AS DUAS METADES, SEMPRE, E NOS MESMOS CORPOS:
REM
REM     de longe o verbo RECUSA        x  chegando perto ele ACEITA
REM     com seis o dragao nao sobe     x  com sete ele SOBE
REM     gastas, as sete nao invocam    x  passado o prazo, invocam de novo
REM     a de outro nao cai aos 10 s    x  cai aos 5 min
REM     quem nao fala e recusado       x  quem fala invoca -- AS MESMAS SETE
REM     o desejo cai no pedinte        x  e NAO cai no porta-voz
REM
REM  O INIMIGO TENTA CINCO ROTAS DE ROUBO, e as cinco estao fechadas: reescrever
REM  o pedido, repassar as sete a um comparsa, esperar o pedinte sair do mundo,
REM  sumir com as sete pra sempre, e lavar a procuracao numa disputa combinada.
REM
REM  E ELA E O ALVO DAS CINCO INJECOES DE CODIGO-FONTE DA FASE 3 (feitas a mao,
REM  no arquivo de PRODUCAO, uma por vez, e desfeitas com Edit reverso):
REM
REM     (a) o portao da lingua apagado ......... GameServer.SuperShenron.cs
REM     (b) o procurador ficando com o desejo .. GameServer.SuperShenron.cs
REM     (c) a disputa de 5 min terminando cedo . GameServer.SuperEsferas.cs
REM     (d) a esfera nao sumindo apos o desejo . GameServer.Esferas.cs
REM     (e) o determinismo quebrado ............ Core/Magic/Esferas.cs
REM
REM  Injetar defeito no ESTADO (um campo escrito na mao) prova que a checagem le
REM  o campo. Injetar no CODIGO prova que ela le a REGRA -- e um `if` que alguem
REM  apagar do arquivo continua deixando as bancadas de estado verdes.
REM
REM  NAO PRECISA DE JANELA. Ela roda no PRIMEIRO LOGIN (o set e de uma
REM  ASSINATURA, o claim e por assinatura, e o canal de disputa exige `Peer`) e
REM  devolve o mundo inteiro no fim.
REM
REM  PORTA PROPRIA (7979): se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a.
REM
REM  O placar sai no console; LEIA a linha
REM  "===== BANCADA DO AVESSO: N OK, M FALHA =====".
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
echo     testar-o-avesso.bat
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
echo  ---- a corrente inteira e o procurador sob ataque ----
"%GODOT%" --headless --path . --host --rede 7979 --avessoteste ^
          --raca Namekian --conta bancada_avesso --senha teste --nome AvessoBanca

echo.
echo  Encerrado.
pause
