@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do sistema de ki, de ponta a ponta

REM ===========================================================================
REM  A BANCADA DO SISTEMA DE KI, DE PONTA A PONTA
REM
REM     testar-ki-ponta.bat          porta 7961 (a padrao desta bancada)
REM     testar-ki-ponta.bat 7999     outra porta
REM
REM  Sobe o servidor e um CLIENTE-ROBO no mesmo processo, sem janela, e roda as
REM  duas metades:
REM
REM     --kideponta  (servidor)  124 checagens: tabela de pontos linha a linha,
REM                              dano contra a conta do `objects.dm`, os tres
REM                              tipos, a colisao nos dois sentidos, a paridade
REM                              do canal de tiro da IA, os dois tetos, o save
REM                              velho e o palco da metade viva.
REM     --diagki     (cliente)   35 checagens: conta NOVA pelo fio, tecnica
REM                              montada por verbo de rede, o teto de pontos
REM                              cobrado pelo SERVIDOR, cinco MENTIRAS que nao
REM                              acertam ninguem, o PASSO HONESTO que o servidor
REM                              aceita, e o RELOGIN: a tecnica, o lugar e o Ki
REM                              voltam como sairam.
REM
REM  O PALCO E DA BANCADA: no primeiro login o servidor poe o robo num campo
REM  aberto e o boneco a nove tiles (ver `ArmarAMetadeViva`). NAO some a flag
REM  `--campoteste` a esta linha: ela repoe o corpo em TODA entrada, inclusive
REM  na volta do relogin, e a conferencia "o corpo acorda onde deslogou" reprova
REM  -- medido em 2026-10-07.
REM
REM  A conta e `bancadaki<porta>` e ela e limpa no inicio de cada rodada.
REM  Duas rodadas ao mesmo tempo na MESMA porta nao rodam -- troque a porta.
REM
REM  O processo SAI SOZINHO no fim, com codigo 0 se tudo passou.
REM ===========================================================================

cd /d "%~dp0"

set PORTA=%1
if "%PORTA%"=="" set PORTA=7961

REM --- achar o Godot (mesma busca do servidor.bat) --------------------------
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
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : %PORTA%

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada rodaria o binario de ontem e
        echo  diria "tudo ok" sobre um conserto que nunca chegou.
        pause
        exit /b 1
    )
)

echo.
"%GODOT%" --headless --path . --rede %PORTA% --kideponta --diagki

echo.
echo  Encerrado com codigo %errorlevel%.
pause
