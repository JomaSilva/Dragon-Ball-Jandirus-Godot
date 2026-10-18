@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada dos DESEJOS, da LINGUA e do PROCURADOR

REM ===========================================================================
REM  OS DESEJOS, A LINGUA DOS DEUSES E O PROCURADOR  (--desejoteste)
REM
REM     testar-desejos.bat
REM
REM  FASE 2 do pedido do dono. A --esferateste mede o CORPO do sistema (onde as
REM  esferas caem, quem as pega, quando acordam); esta mede o que ele FAZ.
REM
REM  A PERGUNTA DIFICIL DO PEDIDO, e a resposta que esta bancada MEDE:
REM  "quem recebe o desejo, quem pediu ou quem falou?"  -> QUEM PEDIU.
REM  Medido no bolso: o zeni entra no doador e NAO entra no porta-voz. E a
REM  outra metade do mesmo tiro -- o falante manda `sdb_supremo`, acontece a
REM  RIQUEZA que o pedinte registrou, e ninguem fica com a divida do supremo.
REM
REM  E CADA AFIRMACAO CARREGA A METADE QUE A DERRUBA, porque afirmacao de um
REM  lado so fica verde num sistema morto:
REM
REM     o set de 1 pedido OFERECE   x  o Porunga de 3 NAO oferece
REM     o nao-portado nao consome   x  o portado CONSOME
REM     o criador e recusado        x  o estranho RECEBE
REM     sem lingua recusa           x  com lingua ATENDE (e o ciclo anda)
REM     cargo divino ensina         x  cargo comum NAO ensina
REM     sangue Kai ensina           x  sangue comum NAO ensina
REM     a escada Rose abre          x  e continua fechada pra outra classe
REM
REM  TRES FAMILIAS COM DEFEITO INJETADO (mede, estraga, tem que REPROVAR,
REM  conserta, mede de novo). Uma checagem que so foi vista passando e
REM  indistinguivel de `Checa("...", true)`:
REM
REM     A. o portao da lingua  -> `godtongue` ligado na mao
REM     B. a procuracao        -> apagada (o estado em que o DM SEMPRE esteve)
REM     C. a velhice           -> `aged_out` desligado
REM
REM  E DUAS DECISOES QUE SO ESTA BANCADA PROVA, porque as duas sao sobre o que
REM  o desejo NAO faz:
REM
REM     o revive do desejo NAO mexe no `ResurrectedCount` e NAO mata quem pediu
REM        (reusar o `RessuscitarG4` "porque ja existe" faria as duas coisas);
REM     o "Mais Forte do Universo" NAO e um buff: o tique de producao COBRA a
REM        vida no vencimento, e a morte e `aged_out` -- a unica que nem as
REM        Super Esferas desfazem.
REM
REM  NAO PRECISA DE JANELA. Ela roda no PRIMEIRO LOGIN (o bloqueio do criador
REM  compara ASSINATURA, que so existe com conta e slot) e devolve o mundo
REM  inteiro no fim -- sets, esferas, claims, a procuracao, o ciclo das Super,
REM  o adianto do ceu, a raca/classe/idade/zeni do testador e os tronos.
REM
REM  PORTA PROPRIA (7978): se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a.
REM
REM  O placar sai no console; LEIA a linha
REM  "===== BANCADA DOS DESEJOS: N OK, M FALHA =====".
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
echo     testar-desejos.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7978

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
echo  ---- os desejos, a lingua e o procurador (3 defeitos injetados) ----
"%GODOT%" --headless --path . --host --rede 7978 --desejoteste ^
          --raca Namekian --conta bancada_desejo --senha teste --nome DesejoBanca

echo.
echo  Encerrado.
pause
