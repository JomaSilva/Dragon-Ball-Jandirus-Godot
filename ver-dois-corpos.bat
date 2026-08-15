@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- os TRES PEDIDOS fotografados

REM ===========================================================================
REM  OS TRES PEDIDOS, FOTOGRAFADOS  (--diagcorpos)
REM
REM     ver-dois-corpos.bat
REM
REM  A IRMA DESTA BANCADA E A `testar-dois-corpos.bat`, que mede os mesmos tres
REM  pedidos em NUMERO -- 103 provas, oito defeitos injetados, tudo verde.
REM
REM  **E ela ficaria verde com o corpo desenhado atravessando o outro na tela.**
REM  Entre a caixa dos pes do servidor e o pixel ha o snapshot, o `World`, a
REM  interpolacao do corpo remoto e o Y-sort. Este projeto ja catalogou esse
REM  cego quatro vezes (a memoria "a bancada mede INTENCAO"), e as fotos que o
REM  dono pediu sao exatamente a metade que so o olho fecha.
REM
REM  O ROTEIRO (6 tomadas, no `user://` -- os caminhos saem no console)
REM     A1 corpos-a1-antes         os dois separados por tres tiles (o CONTROLE)
REM     A2 corpos-a2-colidindo     Alfa ANDANDO contra Beta, e parando nele
REM        corpos-A-colisao.png    a tira: antes / colidindo
REM     B1 corpos-b1-no-chao       no colo, ainda no chao (o controle da altura)
REM     B2 corpos-b2-no-ar         CARREGADO no ar, com a SOMBRA no chao
REM        corpos-B-no-colo.png    a tira: chao / ar
REM     C1 corpos-c1-cadaver       o CADAVER deitado no chao
REM     C2 corpos-c2-enterrado     depois do enterro: a LAPIDE no lugar dele
REM        corpos-C-cadaver.png    a tira: cadaver / lapide
REM
REM     Cada tomada sai em DOIS arquivos: a tela cheia (prova o LUGAR) e um
REM     `-perto.png` recortado e ampliado 3x em Nearest (prova a CENA -- num
REM     quadro de 1600x900, dois corpos encostados sao dois bonecos de 32 px).
REM
REM  NADA E FORJADO: o passo sai do `AplicarComando` (o atuador da IA, onde mora
REM  o `MoveRules.Advance` com a `Vizinhanca`), o agarrao sai do
REM  `AlternarAgarrao` (a tecla), o voo sai do `AlternarVoo` (o verbo `Fly`), a
REM  morte sai do `CombatState.Morrer` -- e O ENTERRO SAI DO CLIENTE, pelo
REM  `SendVerbo("enterrar")`, que e o mesmo pacote do botao do menu da tecla E.
REM
REM  ELA PRECISA DE JANELA: no headless o `GetImage` volta vazio e as fotos saem
REM  em branco. A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`) porque o
REM  dono trabalha no principal.
REM
REM  E ELA LIMPA O QUE POS NO MUNDO -- os corpos forjados, os cadaveres que eles
REM  deixaram e A LAPIDE (que vai pro `mundo.json` de verdade; sem isso cada
REM  rodada deixaria um tumulo de teste na partida do dono, pra sempre).
REM
REM  PORTA PROPRIA (7942): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     ver-dois-corpos.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7942

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
echo  ---- as tres cenas, com janela no SEGUNDO monitor ----
echo.
echo   Leva uns 45 s (o cadaver so nasce depois dos 15 s de `Alem.MsNoChao`,
echo   que e quando o corpo sai do mundo). A janela fecha sozinha e o placar
echo   sai no console: "[corpos] ===== TUDO OK (6 tomadas) =====".
echo.
"%GODOT%" --path . --host --rede 7942 --diagcorpos ^
          --position 1920,0 --resolution 1600x900 ^
          --raca Human --conta bancada_foto_corpos --nome Olheiro

echo.
echo  Encerrado.
pause
