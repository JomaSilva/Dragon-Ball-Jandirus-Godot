@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do CORPO QUE VOLTA

REM ===========================================================================
REM  O CORPO QUE VOLTA  (--diagcorpo)
REM
REM     testar-corpo.bat
REM
REM  O efeito de esquiva deixou de ser sobreposicao e virou SUBSTITUICAO: o
REM  `flick('Zanzoken.dmi', M)` do DM (CombatMovement.dm:286) TROCA o icone do
REM  mob por tres quadros e devolve -- o corpo do defensor SOME e as listras
REM  aparecem no lugar dele.
REM
REM  Com isso entrou um defeito que antes nao existia, e ele e pior que qualquer
REM  efeito feio: A INVISIBILIDADE VAZAR. Um personagem que some e nao volta e
REM  uma partida perdida. Esta bancada existe pra isso.
REM
REM  UM PROCESSO SO, SEM JANELA, SEM ADVERSARIO. Ela nao assiste a uma briga:
REM  ela DIRIGE o efeito. Dez esquivas sobrepostas na cadencia que ela quer,
REM  nocaute no milesimo certo, a zona caindo no meio da troca, o node arrancado
REM  a forca -- coisas que uma briga de verdade nao sabe encomendar. Roda em
REM  poucos segundos e fecha sozinha.
REM
REM  A IRMA DELA E A `testar-desvio.bat`, e as duas se precisam:
REM     --diagdesvio  poe dois lutadores brigando e FOTOGRAFA  (prova o PIXEL:
REM                   as listras sao pretas? nascem onde o corpo esta?)
REM     --diagcorpo   dirige e mede                            (prova a MAQUINA:
REM                   esconde, devolve, sobrepoe, e sobrevive a interrupcao)
REM  Nenhuma cobre o buraco da outra.
REM
REM  O QUE PROCURAR no console:
REM     [corpo] ===== TUDO OK =====        ou    [corpo] ===== N FALHA(S) =====
REM     e a linha que importa e a F3.5 ("TODO MUNDO esta visivel").
REM
REM  DEPOIS DA RODADA REAL ELA SE COBRA: cada regra recebe o defeito que ela
REM  existe pra pegar e TEM que ficar vermelha (as linhas "[injecao]"). Uma
REM  regra que passa verde com o proprio defeito e falha DA BANCADA.
REM
REM  PORTA PROPRIA (7986): enquanto outro --host estiver no ar nenhum sobe na
REM  mesma porta. Se aparecer "[server] FALHOU ao abrir a porta 7986", ha outra
REM  rodada viva -- feche-a.
REM
REM  CONTA PROPRIA (bancada_corpo): nada toca conta de jogador.
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
echo     testar-corpo.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7986  (um processo so, headless)

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

REM SEM `--` separando as flags: com ele elas vao parar em GetCmdlineUserArgs()
REM e a bancada sobe muda (ver servidor.bat).
"%GODOT%" --headless --path . --host --rede 7986 --diagcorpo ^
          --raca Human --conta bancada_corpo --nome Corpo

echo.
echo  Encerrado.
pause
