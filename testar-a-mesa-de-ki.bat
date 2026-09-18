@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- A MESA DE ATAQUES DE KI

REM ===========================================================================
REM  O PEDIDO DO DONO (2026-09-15)
REM
REM      testar-a-mesa-de-ki.bat
REM
REM  "faca a tela de customizacao de ataques de ki (beam, blast etc)"
REM
REM  A mesa (Client/TelaDeTecnicas.cs) e o CreateAttackWindow do DM mais o
REM  Blast_Color do menu de Settings, numa tela so: a previa VIVA do tiro (um
REM  ProjetilDesenhado de producao, sem luz), a grade de artes em miniatura
REM  recortada pelo tipo (raio ve Beams + Techniques, bola so Blasts,
REM  teleguiado so Techniques), a cor do ki (Aplicar / Sortear) e, na direita,
REM  os numeros de sempre (tipo, nome, gritos, as compras de ponto).
REM
REM  DUAS RODADAS:
REM
REM    1) --tecnicateste   sem janela, no boot do servidor. A tabela de pontos, os
REM                        tetos, a copia da mesa, o disco, os disparos, o verbo, e
REM                        a familia 7 (nova): a COR DO KI -- `ca_cor` escreve,
REM                        recusa fora da faixa, respeita a porta do
REM                        `Aura_and_Blast_Color` (carregando/voando/escudado),
REM                        `sortear` e o disco -- com o defeito injetado da faixa
REM                        desligada.
REM
REM    2) --diagmesa       COM JANELA (as fotos), no segundo monitor. A grade do raio
REM                        e da bola contadas contra PermitidasPara(tipo), a previa
REM                        vestindo a arte que o servidor confirmou, a bola-num-raio
REM                        recusada pelo fio, a grade sem filtro INJETADA reprovando,
REM                        e a cor do ki indo ao servidor e VOLTANDO pelo PeerLook.
REM
REM  AS FOTOS saem em %%APPDATA%%\Godot\app_userdata\Dragon ball Jandirus\mesa-*.png
REM  -- e o APPDATA e DESVIADO: a pasta de saves do dono nao e tocada.
REM
REM  ATENCAO -- O PRIMEIRO PASSO NAO SE FECHA SOZINHO: a bancada roda no boot do
REM  servidor e ele continua no ar depois dela. Quando o placar
REM       ================ N passaram, N falharam ================
REM  aparecer, feche com Ctrl+C pra o segundo passo comecar.
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
echo  Nao encontrei o Godot. set GODOT=C:\caminho\Godot_v4.7.1-stable_mono_win64_console.exe
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7963

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo  A compilacao FALHOU -- as bancadas mediriam a versao de ontem.
        pause
        exit /b 1
    )
)

set "APPDATA=%TEMP%\jandirus-bancada-mesa"
if not exist "%APPDATA%" mkdir "%APPDATA%"
echo  Saves desviados para: %APPDATA%

echo.
echo  ---- 1/2: a criacao de tecnicas + a cor do ki, sem janela (feche com Ctrl+C no placar) ----
"%GODOT%" --headless --path . --server --port 7963 --tecnicateste

echo.
echo  ---- 2/2: A MESA NA TELA (precisa de janela; abre no segundo monitor) ----
"%GODOT%" --path . --host --rede 7963 --diagmesa --position 1920,0 --resolution 1280x720 ^
          --raca Human --conta bancada_mesa --nome Mesa

echo.
echo  Encerrado. As fotos estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus".
pause
