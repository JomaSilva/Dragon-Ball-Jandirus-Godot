@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada das ESFERAS DO DRAGAO

REM ===========================================================================
REM  AS ESFERAS DO DRAGAO E AS SUPER ESFERAS  (--esferateste)
REM
REM     testar-esferas.bat
REM
REM  FASE 1 do pedido do dono: a esfera como coisa do mundo (nascer, espalhar,
REM  ser achada, ser pega, sumir e voltar), a invocacao, e as Super Esferas com
REM  o claim planetario. Os DESEJOS e a LINGUA DOS DEUSES sao a Fase 2 -- e o
REM  ponto de plugue delas ja existe e ja e exercitado aqui (`ContarUmDesejo` e
REM  `ConsumirAsSupers`), pra a Fase 2 nao chegar num plugue que nunca rodou.
REM
REM  SAO 51 CONFERENCIAS, e nenhuma pergunta "o campo foi escrito":
REM
REM     o espalhamento cai em chao ANDAVEL  -> perguntado ao mapa de COLISAO
REM     o nocaute derruba as sete           -> medido em `Portador == 0`
REM     o radar acha                        -> lido no que o jogador OUVE
REM     o claim de 10 s fecha               -> rodado no TIQUE de producao
REM
REM  E CADA AFIRMACAO CARREGA A METADE QUE A DERRUBA, porque afirmacao de um
REM  lado so fica verde num sistema morto:
REM
REM     a posicao e a mesma        x  e MUDA quando o ciclo anda
REM     com sete o dragao sobe     x  com seis a invocacao recusa
REM     o radar acha a acordada    x  e NAO acha a apagada
REM     o claim de 10 s fecha      x  e CAI se o disputante se afasta
REM     passada a espera, acorda   x  antes dela, a invocacao e recusada
REM
REM  QUATRO FAMILIAS COM DEFEITO INJETADO (o padrao da --provateste: mede o
REM  codigo de producao, estraga, mede de novo -- tem que REPROVAR --, conserta,
REM  mede de novo). Uma checagem que so foi vista passando e indistinguivel de
REM  `Checa("...", true)`:
REM
REM     A. a celula proibida  -> o sorteio SEM a rejeicao do `rand(-8,8)`
REM     B. a espera           -> o carimbo de reativacao puxado pro passado
REM     C. a policia          -> a zona do set torta (o `Ballplanet` nulo do DM)
REM     D. o save             -> o ciclo zerado (o campo que carrega ONDE elas estao)
REM
REM  E A PROVA DO REUSO: a bancada le a FILA DA CONQUISTA depois de abrir uma
REM  disputa de Super Esfera. Se o recado nao estiver la, foi criada uma segunda
REM  fila -- o "segundo eixo pra mesma ideia" que a tarefa proibiu, e que o
REM  proprio DM manda evitar (`sdb_contest_channel` chama `conq_notify_owner`).
REM
REM  NAO PRECISA DE JANELA: o que ela mede sao numeros, e nao pixels. Ela roda
REM  no PRIMEIRO LOGIN (o set e de uma ASSINATURA, que so existe com conta e
REM  slot) e devolve o mundo inteiro no fim -- sets, esferas, claims, o ciclo
REM  das Super, o adianto do ceu, a raca/classe do testador e o trono de
REM  Guardiao que ela toma emprestado.
REM
REM  PORTA PROPRIA (7977): se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a.
REM
REM  O placar sai no console; LEIA a linha
REM  "===== BANCADA DAS ESFERAS: N OK, M FALHA =====".
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
echo     testar-esferas.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7977

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
echo  ---- as esferas do dragao (51 conferencias, 4 defeitos injetados) ----
"%GODOT%" --headless --path . --host --rede 7977 --esferateste ^
          --raca Namekian --conta bancada_db --senha teste --nome DbBanca

echo.
echo  Encerrado.
pause
