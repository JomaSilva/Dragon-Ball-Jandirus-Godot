@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada da CURA (a ativa do Namek, e a passiva no servidor)

REM ===========================================================================
REM  A CURA E PRIVILEGIO  (--curaviva)
REM
REM     testar-cura.bat
REM
REM  O pedido do dono, literal:
REM     "o TEMPO DE KO ta mt curto e FERIMENTOS estao REGENERANDO MT RAPIDO em
REM      racas sem passiva de regeneracao. um MEMBRO QUEBRADO deveria DEMORAR
REM      BASTANTE pra regenerar sozinho, sem ajuda como uma MAQUINA DE
REM      REGENERACAO ou algo do tipo. um NAMEK com a skill ATIVA dele pode
REM      CURAR O MEMBRO MAIS FERIDO dele ou REGENERAR UM MEMBRO INTEIRO PERDIDO
REM      gastando ENORMES QUANTIDADES DE ENERGIA. um MAJIN e a UNICA raca q
REM      PASSIVAMENTE regenera rapido MESMO EM COMBATE... ate mesmo um NAMEK N
REM      TEM REGENERACAO PASSIVA EM COMBATE, somente a ATIVA"
REM
REM  SAO TRES BANCADAS, e a frase do dono so fecha com as tres:
REM
REM     1) dotnet Tools/AssetPipeline/bin/Debug/net8.0/AssetPipeline.dll ^
REM            cura Assets/Data/races.json          -> 26 provas, no CORE
REM        os PRAZOS: braco quebrado, membro perdido, nocaute, levantar.
REM
REM     2) este arquivo (--curaviva)                -> 43 provas, no SERVIDOR
REM        a ATIVA do Namekuseijin (cura, custo, recarga, membro perdido, quem
REM        tem o botao) e a passiva pelo funil `RegenerarPassivo`.
REM
REM     3) testar-cidade.bat (--cidadeteste)        -> 29 provas
REM        a familia 5 dela e a MAQUINA de regeneracao: a saida de quem nao tem
REM        raca pra isso (Humano sem braco = 5 minutos deitado no tanque).
REM
REM  OS PRAZOS QUE ELAS MEDEM (contra o DM, medido):
REM     braco quebrado (sair de "Quebrado" / ficar inteiro), fora de combate
REM        Humano 103 s / 549 s   Saiyajin 108 s / 575 s   Namek 87 s / 468 s
REM        Bio 46 s / 269 s       Majin 22 s / 125 s
REM     braco ARRANCADO voltando sozinho
REM        Majin 18,8 s   Bio 45,9 s   os outros NUNCA (tanque, ou a ativa)
REM     nocaute (nucleo quebrado): Humano 146 s, Majin 18,6 s, teto 225 s
REM     a ativa do Namek: 70% do Ki MAXIMO e 10 s de recarga
REM     o tanque de regeneracao: 300 s deitado por membro
REM
REM  PORTA PROPRIA (7906). Se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a. O SERVIDOR CONTINUA DE PE depois da bancada: leia
REM  o placar "[curaviva] ===== N OK, M FALHA(S) =====" e feche com Ctrl+C.
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
echo     testar-cura.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7906

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

    echo.
    echo  ---- 1 de 2: os PRAZOS, no Core (26 provas) ----
    dotnet build "Tools\AssetPipeline\AssetPipeline.csproj" -t:Rebuild -v q -nologo
    dotnet "Tools\AssetPipeline\bin\Debug\net8.0\AssetPipeline.dll" cura "Assets\Data\races.json"
)

echo.
echo  ---- 2 de 2: a ATIVA e o funil do servidor (43 provas) ----
echo.
"%GODOT%" --headless --path . --server --rede 7906 --curaviva

echo.
echo  Encerrado.
pause
