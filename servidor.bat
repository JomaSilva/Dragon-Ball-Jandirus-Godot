@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- servidor dedicado

REM ===========================================================================
REM  SERVIDOR DEDICADO
REM
REM    servidor.bat              porta padrao (7777)
REM    servidor.bat 8888         outra porta
REM
REM  Pra apontar pra outro Godot, defina a variavel antes de chamar:
REM    set GODOT=C:\caminho\Godot_v4.7.1-stable_mono_win64_console.exe
REM
REM  Este e o MESMO servidor que o botao "Hospedar partida" do cliente sobe --
REM  a diferenca e que aqui ele roda sem janela e sem ninguem jogando nele.
REM ===========================================================================

cd /d "%~dp0"

set PORTA=%1
if "%PORTA%"=="" set PORTA=7777

REM --- achar o Godot -------------------------------------------------------
if not "%GODOT%"=="" goto :temgodot

set "GODOT=E:\Users\Joao\Desktop\Godot_v4.7-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
if exist "%GODOT%" goto :temgodot

REM procura uma pasta Godot_* ao lado do projeto e um andar acima
for /d %%D in ("..\Godot_*" "..\..\Godot_*") do (
    for %%F in ("%%~fD\*console.exe") do (
        set "GODOT=%%~fF"
        goto :temgodot
    )
)

echo.
echo  Nao encontrei o Godot.
echo  Defina o caminho e rode de novo:
echo.
echo     set GODOT=C:\caminho\Godot_v4.7.1-stable_mono_win64_console.exe
echo     servidor.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : %PORTA%

REM --- compilar (se o dotnet estiver na maquina) ----------------------------
where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- o servidor subiria com o binario antigo.
        echo  Corrija os erros acima antes de continuar.
        pause
        exit /b 1
    )
) else (
    echo  dotnet nao encontrado: subindo com o binario ja compilado.
)

echo.
echo  ================================================================
echo   Servidor no ar. Feche esta janela ou Ctrl+C para derrubar.
echo   Os jogadores conectam no IP desta maquina, porta %PORTA%.
echo  ================================================================
echo.

REM Sem o separador "--": com ele o argumento vai parar em GetCmdlineUserArgs()
REM e o servidor sobe MUDO, sem abrir porta nenhuma.
"%GODOT%" --headless --path . --server --port %PORTA%

echo.
echo  Servidor encerrado.
pause
