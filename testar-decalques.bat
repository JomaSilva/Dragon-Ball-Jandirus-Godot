@echo off
setlocal
title Dragon Ball Jandirus -- bancada dos DECALQUES (+ a volta ao lobby)
REM ======================================================================
REM  DECALQUES  (--diagdecalque)  -- duas rodadas: a prova e a contraprova
REM
REM  A primeira rodada mede tudo: a terra revirada, as dez pecas, o rastro,
REM  a cratera... e, no fim, VOLTA AO LOBBY pelo botao e entra de novo --
REM  o caminho do dono que perdia a trilha do arremesso e a cratera
REM  (2026-09-04: o World morto continuava assinado no DecalqueCaiu).
REM  A segunda rodada roda com --decalinjetar, que devolve o defeito: ela
REM  TEM que reprovar na familia da volta ao lobby. Verde nas duas = a
REM  bancada nao esta olhando o que diz olhar.
REM
REM  A JANELA ABRE NO SEGUNDO MONITOR (--position 1920,0).
REM  A PASTA DE SAVES E DESVIADA (APPDATA): nada aqui toca os saves do dono.
REM ======================================================================
cd /d "%~dp0"
set "APPDATA=%TEMP%\jandirus-bancada-decalques"
if exist "%APPDATA%" rmdir /s /q "%APPDATA%"
mkdir "%APPDATA%"

if not "%GODOT%"=="" goto :temgodot
set "GODOT=E:\Users\Joao\Desktop\Godot_v4.7-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
if exist "%GODOT%" goto :temgodot
echo  Nao encontrei o Godot. Defina GODOT=caminho\do\Godot_console.exe
pause
exit /b 1

:temgodot
if "%SEMBUILD%"=="1" (
    echo  SEMBUILD=1: sem recompilar -- medindo a DLL que ja esta ai.
) else (
    where dotnet >nul 2>nul
    if %errorlevel%==0 (
        echo  Compilando...
        dotnet build "Dragon ball Jandirus.csproj" -v q -nologo
        if errorlevel 1 (
            echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
            pause
            exit /b 1
        )
    )
)

echo.
echo  ---- rodada 1: a prova (procure "[decal] ===== TUDO OK =====") ----
"%GODOT%" --path . --host --rede 7987 --quebrarteste 6 --diagdecalque ^
          --position 1920,0 --nome Marcador --conta decal --raca Human

echo.
echo  ---- rodada 2: a CONTRAPROVA com --decalinjetar (procure "FALHA(S)" na volta ao lobby) ----
"%GODOT%" --path . --host --rede 7987 --quebrarteste 6 --diagdecalque --decalinjetar ^
          --position 1920,0 --nome Marcador --conta decal --raca Human

echo.
echo  Encerrado.
if not "%SEMPAUSA%"=="1" pause
