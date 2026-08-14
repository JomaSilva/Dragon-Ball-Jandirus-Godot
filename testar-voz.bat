@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada da VOZ com clientes de verdade

REM ===========================================================================
REM  A VOZ LOCAL, COM QUATRO CLIENTES DE VERDADE  (--vozviva)
REM
REM      testar-voz.bat
REM
REM  POR QUE ELA EXISTE. As outras duas bancadas da voz dizem, cada uma no
REM  proprio cabecalho, que NAO atravessam o fio:
REM     * --diagvoz   mede o codec e o filtro sem rede e sem personagem;
REM     * --vozteste  mede o CORTE com corpos FORJADOS -- e corpo forjado nao
REM                   tem Peer, entao a linha da ENTREGA (`Peer.Send`), que e a
REM                   unica coisa que separa este sistema de um vazamento, nunca
REM                   tinha sido executada por bancada nenhuma.
REM
REM  Aqui o cliente `a` injeta uma ONDA CONHECIDA no lugar da captura (300 Hz +
REM  3 kHz) e o cliente `b`, noutro processo, mede o que saiu do decodificador
REM  DELE: quantos bytes chegaram, com que amplitude, com que espectro.
REM
REM  ONDE SAI CADA PLACAR:
REM     [vozviva]     ...  no processo ANFITRIAO  -> o que o SERVIDOR entregou
REM     [vozviva:a]   ...  no anfitriao tambem    -> o que SAIU do falante
REM     [vozviva:b]   ...  na janela do convidado -> O QUE CHEGOU NO OUVIDO
REM  As tres tabelas se leem juntas. So o `b` responde as perguntas do dono.
REM
REM  A LINHA QUE IMPORTA e a da fase `fora`: pacotes = 0 e B audio = 0. "Baixinho"
REM  seria outro sistema.
REM
REM  PORTA PROPRIA (7982): enquanto um --host estiver no ar nenhum outro sobe na
REM  mesma porta. Se aparecer "[server] FALHOU ao abrir a porta 7982" ha outra
REM  rodada viva -- feche-a (ver o bloco de limpeza no fim deste arquivo).
REM
REM  NAO PRECISA DE MICROFONE NEM DE PLACA DE SOM: roda --headless. O que o
REM  motor nao conseguir mixar sai DITO no placar, e nao sai como zero.
REM ===========================================================================

cd /d "%~dp0"

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
echo     testar-voz.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7982  (quatro processos: falante+servidor, ouvinte, e 2 falantes extras)

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ================================================================
echo   Os tres convidados entram 20 s depois do anfitriao. A cena sao
echo   oito fases de 3 s. O PLACAR DO OUVIDO sai na janela "vozviva-b".
echo  ================================================================
echo.

REM OS CONVIDADOS SOBEM ATRASADOS, cada um na janela dele: eles nao podem discar
REM antes de a porta existir. O ATRASO E `ping` E NAO `timeout` -- o `timeout` LE
REM do teclado e morre com "Input redirection is not supported" quando alguem
REM roda o .bat com a entrada redirecionada (qualquer automacao). Ver
REM testar-mente.bat, que pagou por isso.
REM
REM O `b` NAO E /min: e a janela dele que tem a resposta.
start "vozviva-b" cmd /c "ping -n 21 127.0.0.1 >nul & ""%GODOT%"" --headless --path . --rede 7982 --connect 127.0.0.1 --vozviva b --raca Saiyan --conta bancada_vozviva_b --nome VozVivaB & pause"
start "vozviva-c" /min cmd /c "ping -n 21 127.0.0.1 >nul & ""%GODOT%"" --headless --path . --rede 7982 --connect 127.0.0.1 --vozviva c --raca Saiyan --conta bancada_vozviva_c --nome VozVivaC"
start "vozviva-d" /min cmd /c "ping -n 21 127.0.0.1 >nul & ""%GODOT%"" --headless --path . --rede 7982 --connect 127.0.0.1 --vozviva d --raca Saiyan --conta bancada_vozviva_d --nome VozVivaD"

"%GODOT%" --headless --path . --host --rede 7982 --vozviva a --vozvivagente 4 ^
          --raca Saiyan --conta bancada_vozviva_a --nome VozVivaA

REM O ANFITRIAO SE FECHA SOZINHO no fim da cena; os convidados so veem a conexao
REM cair. Quem os tira e daqui.
REM
REM PELA LINHA DE COMANDO, e nao por titulo de janela: o Godot console.exe RELANCA
REM a si mesmo num segundo processo, e esse filho nasce fora da janela que o
REM `start` nomeou -- ficariam Godots vivos segurando a porta 7982, e a rodada
REM seguinte subiria com "FALHOU ao abrir a porta". (O `c` e o `d` morrem aqui; o
REM `b` fica com a janela aberta no `pause`, que e onde esta o placar.)
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.Name -like 'Godot*' -and ($_.CommandLine -like '*bancada_vozviva_c*' -or $_.CommandLine -like '*bancada_vozviva_d*') } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul

echo.
echo  Encerrado. O placar do OUVIDO esta na janela "vozviva-b".
pause
