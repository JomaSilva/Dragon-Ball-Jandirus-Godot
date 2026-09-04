@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do MEMBRO QUE CAI DO CORPO

REM ===========================================================================
REM  O MEMBRO PERDIDO: O JATO, A PECA NO CHAO E O RASTRO DA AGUA
REM
REM     testar-peca.bat
REM
REM  Sao DUAS bancadas, e elas rodam uma depois da outra neste arquivo porque
REM  respondem METADES diferentes do mesmo pedido -- e cada uma so consegue
REM  responder a sua:
REM
REM   1) --pecateste   (servidor, ~45 s, 58 provas, SEM JANELA)
REM      DOIS CORPOS brigam pelo `Atacar` de producao e a bancada le o FIO:
REM      o `S2C.Hit` (de onde o jato de sangue nasce) e o `S2C.Decalque` (de
REM      onde a peca no chao nasce), com os mesmos leitores do cliente.
REM        * o bit do jato sai UMA vez, nos dois canais -- e os DOIS
REM          contra-exemplos: soco que so machuca, e golpe nao-letal;
REM        * braco cai como Braco+Mao, perna como Perna+Pe, e as duas NAO se
REM          encostam (a linha que pega uma tabela inteira trocada);
REM        * a peca cai dentro de um tile do corpo, espalhada nos dois eixos, e
REM          o CENTRO delas fica em cima de quem perdeu e nao de quem bateu;
REM        * peca vem de AMPUTACAO e nao de soco: 700 socos, e o chao para de
REM          ganhar peca quando os cinco membros arrancaveis ja sairam;
REM        * o pacote da peca tem 12 bytes e nada mais -- e o relato da PLATEIA
REM          diz que houve amputacao sem dizer qual membro nem quanto doeu;
REM        * A EXPLOSAO e o DANO DIRETO tambem arrancam (o `SpreadDamage` e o
REM          `damage_mob` do DM passam pelo mesmo `LopLimb`), com o DEFEITO
REM          INJETADO da cauda unica desligada ficando vermelho; o nao-letal nao
REM          arranca;
REM        * quem ENTRA na zona recebe o RETRATO das pecas (`S2C.Pecas`), com
REM          quanto falta de cada uma; o teto de 32 por zona empurra a MAIS
REM          VELHA, e a peca de 600 s + 1 ms some no tique (o prazo do DM).
REM
REM   2) --diagdecalque  (cliente, ~45 s, 54 provas, SEM JANELA)
REM      O DESENHO, que o fio nao alcanca:
REM        * o jato NASCE (node com a folha carregada), uma vez, seguindo o
REM          corpo pelo elo -- e NAO nasce num acerto comum;
REM        * o teto de 32 pecas vivas dispara, e a poeira nao varre as pecas;
REM        * o rastro da agua nos QUATRO sentidos, os pares por eixo, e o
REM          parado mantendo o ultimo sentido.
REM
REM  POR QUE DUAS E NAO UMA: a briga nao acontece no processo do cliente e o
REM  pixel nao existe no do servidor. Juntar as duas obrigaria uma delas a
REM  medir por atalho -- chamar o efeito na mao (e ai nao se mede o gatilho) ou
REM  ler a struct do servidor (e ai nao se mede o pacote).
REM
REM  PORTA PROPRIA (7974): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     testar-peca.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7974

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
echo  ---- 1/2: o FIO da amputacao, com dois corpos (58 provas) ----
echo.
echo   Leia o placar "[peca] ==== N passaram, M falharam ====" e feche com
echo   Ctrl+C -- o servidor continua de pe depois da bancada, que e a
echo   convencao das bancadas de servidor deste projeto.
echo.
"%GODOT%" --headless --path . --host --rede 7974 --pecateste ^
          --raca Human --conta bancada_peca --nome Medidor

echo.
echo  ---- 2/2: o DESENHO -- jato, teto das pecas e rastro (54 provas) ----
echo.
"%GODOT%" --headless --path . --host --rede 7974 --quebrarteste 6 --diagdecalque ^
          --raca Human --conta bancada_decal --nome Marcador

echo.
echo  Encerrado.
pause
