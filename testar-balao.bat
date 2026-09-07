@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do BALAO DE FALA (nitido em qualquer zoom)
REM ===========================================================================
REM  O BALAO DE FALA  (--diagbalao)
REM
REM     testar-balao.bat
REM
REM  O PEDIDO DO DONO (2026-09-06, etapa 2 de IA/baloes/torneios): "texto nitido
REM  (fonte vetorial, sem filtro nearest no texto), balao escala com o texto e
REM  quebra linha, pra jogadores e NPCs".
REM
REM  UMA RODADA, COM JANELA (a foto precisa renderizar) no SEGUNDO MONITOR:
REM    * o balao continua FILHO do corpo: anda com o dono, sobe com quem voa,
REM      acha o corpo certo pelo nome, substitui com piso de leitura;
REM    * em cada zoom (2, 3, 6) o no encolhe por 1/zoom e escreve com a fonte
REM      em 8 x zoom px -- um pixel do no e um pixel da tela; a caixa fica nos
REM      108 px de mundo e a frase quebra; um balao aberto acompanha a troca;
REM    * NA FOTO (zoom 4): as letras NAO sao blocos de 4x4 (60%% dos tracos tem
REM      largura que nao e multiplo do zoom); com o defeito injetado (pixels de
REM      mundo + nearest, o que havia) TODO traco e multiplo de 4 -- o serrilhado.
REM      As duas fotos ficam em %%APPDATA%%\Godot\app_userdata\...\balao-1-nitido.png
REM      e balao-2-serrilhado.png.
REM  Placar: "[balao] ===== TUDO OK =====" ou "===== N FALHA(S) =====".
REM
REM  A pasta de saves do Godot e DESVIADA pro %%TEMP%%.
REM  PORTA PROPRIA (7930): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     testar-balao.bat
echo.
pause
exit /b 1
:temgodot
echo  Godot : %GODOT%
echo  Porta : 7930
set "APPDATA=%TEMP%\jandirus-bancada-balao"
if exist "%APPDATA%\Godot" rmdir /s /q "%APPDATA%\Godot"
if not exist "%APPDATA%" mkdir "%APPDATA%"
echo  Saves : %APPDATA% (desviado -- a pasta de saves de quem joga fica intacta)
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
echo.
echo  ---- o balao de fala, com janela no segundo monitor (a foto precisa renderizar) ----
"%GODOT%" --path . --position 1920,0 --host --rede 7930 --diagbalao --semfoco ^
          --raca Saiyan --conta bancada_balao --nome MedidorDoBalao
echo.
echo  Leia o placar: "[balao] ===== TUDO OK =====". As fotos estao em:
echo    %APPDATA%\Godot\app_userdata\Dragon ball Jandirus\balao-1-nitido.png
echo    %APPDATA%\Godot\app_userdata\Dragon ball Jandirus\balao-2-serrilhado.png
pause
