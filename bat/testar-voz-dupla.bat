@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- a voz JULGADA, com dois corpos

REM ===========================================================================
REM  A VOZ COM DOIS CORPOS, E COM O DEFEITO NA FRENTE DE CADA REGRA
REM
REM      testar-voz-dupla.bat
REM
REM  POR QUE ELA EXISTE, JA HAVENDO TRES BANCADAS DE VOZ:
REM     --diagvoz   mede o codec e o filtro; nao ha rede, corpo nem corte.
REM     --vozteste  mede o CORTE com corpos forjados; corpo forjado nao tem
REM                 Peer, entao a linha da ENTREGA nunca roda.
REM     --vozviva   MEDE o fio com 4 clientes -- e imprime TABELAS. Nenhum
REM                 veredito: quem le decide se o numero esta bom.
REM     --vozdupla  JULGA. OK/FALHA por familia, e pra CADA familia um MUTANTE:
REM                 o mesmo cenario com o defeito posto no lugar da regra,
REM                 exigindo que a linha fique VERMELHA.
REM
REM  A LINHA CENTRAL e a da fase `fora`: pacotes = 0 e B audio = 0. Ela conta
REM  BYTES e nao volume -- uma bancada de volume ficaria verde com a sala
REM  inteira recebendo tudo, porque quem esta longe soa baixo dos dois jeitos.
REM  E a fase seguinte (`fora_vazando`) troca o corte por "manda pra zona
REM  inteira" e EXIGE que aquela mesma linha fique vermelha.
REM
REM  O ANFITRIAO E O OUVINTE (papel `b`), e isso e de proposito: quem cala tem
REM  que ser admin, e o admin e o host. Entao O PLACAR SAI NESTA JANELA.
REM
REM  PORTA PROPRIA (7982): enquanto um --host estiver no ar nenhum outro sobe na
REM  mesma porta. "[server] FALHOU ao abrir a porta 7982" = ha outra rodada
REM  viva; feche-a (ver o bloco de limpeza no fim).
REM
REM  NAO PRECISA DE MICROFONE NEM DE PLACA DE SOM: roda --headless, e a captura
REM  e substituida por uma onda conhecida (so a captura -- limiar, codificador,
REM  sequencia e envio continuam sendo os de producao). O que o motor nao
REM  conseguir mixar sai DITO no placar, e nao sai como zero.
REM
REM  ELA MEXE NO config.json DA MAQUINA (religar tecla e preferencia de
REM  maquina, e nao ha config de bancada). O arquivo e copiado pra
REM  user://config.json.bancada-vozdupla antes e devolvido no fim -- inclusive
REM  se a bancada for fechada no meio. Ver RoboDeVozDupla.GuardarOConfig.
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
echo     testar-voz-dupla.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7982  (dois processos: ouvinte+servidor aqui, falante na outra janela)

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
echo   O falante entra 20 s depois. A cena sao 17 fases de 2 s, e o
echo   PLACAR DAS DEZ FAMILIAS sai NESTA janela ([vozdupla:b]).
echo  ================================================================
echo.

REM O CONVIDADO SOBE ATRASADO: ele nao pode discar antes de a porta existir. O
REM atraso e `ping` e nao `timeout` -- o `timeout` LE do teclado e morre com
REM "Input redirection is not supported" quando o .bat roda com a entrada
REM redirecionada (qualquer automacao). Ver testar-mente.bat, que pagou por isso.
start "vozdupla-a" /min cmd /c "ping -n 21 127.0.0.1 >nul & ""%GODOT%"" --headless --path . --rede 7982 --connect 127.0.0.1 --vozdupla a --raca Saiyan --conta bancada_vozdupla_a --nome VozDuplaA"

"%GODOT%" --headless --path . --host --rede 7982 --vozdupla b ^
          --raca Saiyan --conta bancada_vozdupla_b --nome VozDuplaB

REM PELA LINHA DE COMANDO, e nao por titulo de janela: o Godot console.exe
REM RELANCA a si mesmo num segundo processo, e esse filho nasce fora da janela
REM que o `start` nomeou -- ficariam Godots vivos segurando a porta 7982.
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.Name -like 'Godot*' -and $_.CommandLine -like '*bancada_vozdupla_a*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul

echo.
echo  Encerrado. O placar das dez familias esta acima, com o prefixo [vozdupla:b].
pause
