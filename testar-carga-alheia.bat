@echo off
setlocal
title Dragon Ball Jandirus -- bancada da CARGA VISTA PELO OUTRO (dois processos)
REM ======================================================================
REM  A CHAMA DA CARGA NO CORPO ALHEIO  (--dois a / --dois b)
REM
REM  O relato (2026-09-04): "quando o efeito de carga ta ativado e alguem
REM  entra no seu planeta, ele nao sincroniza quem ja tava com isso ativado".
REM  A causa: quem soltava o C acima dos 100% continuava em chamas na PROPRIA
REM  tela e apagava na dos outros -- e quem entrava depois nunca via nada.
REM
REM  Duas rodadas, cada uma com dois processos:
REM    A hospeda, PUXA o B pra sua zona a partir dos 10 s (`admin_trazer` repetido ate o B
REM    existir no servidor: o berco de um Saiyajin depende da classe sorteada, e dois planetas
REM    diferentes nunca se veem), segura C aos 26 s -- DEPOIS de o B ja estar olhando, pra
REM    bancada julgar os dois estados (Ki normal e acima dos 100%) -- e SOLTA o C aos 44 s,
REM    sem nunca transformar. O Ki acima dos 100% NAO cai sozinho depois de soltar: e por
REM    isso que o estado "normal" tem que vir antes.
REM    B entra ~12 s depois, com janela no SEGUNDO monitor, e julga o corpo de A:
REM      - ao entrar, ve A carregando (o bit do snapshot);
REM      - quando A solta o C ainda acima dos 100%, a chama TEM que continuar;
REM      - sem C e abaixo dos 100%, a chama tem que estar apagada.
REM  A rodada 2 leva --doisinjetar (a regra velha) e TEM que reprovar.
REM
REM  A PASTA DE SAVES E DESVIADA (APPDATA): nada aqui toca os saves do dono.
REM ======================================================================
cd /d "%~dp0"
set "APPDATA=%TEMP%\jandirus-bancada-carga-alheia"
if exist "%APPDATA%" rmdir /s /q "%APPDATA%"
mkdir "%APPDATA%"
set "REDE=7985"

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

call :rodada prova ""
call :rodada contraprova "--doisinjetar"

echo.
echo  Encerrado. Procure "===== [dois-b] N OK, M FALHA =====" nas duas rodadas:
echo  a prova tem que dar 0 FALHA e a contraprova tem que REPROVAR a chama depois de soltar o C.
if not "%SEMPAUSA%"=="1" pause
exit /b 0

:rodada
echo.
echo  ---- rodada %~1 %~2 ----
REM O A TAMBEM TEM JANELA (no segundo monitor, ao lado do B): o "retrato do dono" que o B usa de regua
REM e medido no material do corpo do A, e no headless o `GetImage` volta vazio. O log dele vai pro
REM %TEMP%, porque um processo aberto com `start` nao escreve no console deste .bat.
REM NUMA LINHA SO: dentro de um `cmd /c "..."` o `^` de continuacao nao vale, e a segunda linha virava
REM um comando proprio ("'--dois' nao e reconhecido").
REM `--semfoco` NO A: a janela do B, aberta depois, rouba o foco do teclado -- e sem a flag o A "soltava"
REM o C (ActionRelease) e o LocalPlayer dele nunca lia a soltura. O B via o A carregando pra sempre.
start "carga-alheia-A" /min cmd /c ""%GODOT%" --path . --host --rede %REDE% --kiteste --position 2560,0 --semfoco --dois a --doistrazer 10 --doiscarga 26 --doissoltar 44 --doisatraso 999 --doisfim 40 --conta carga_a_%~1 --nome DoisA --raca Saiyan > "%TEMP%\jandirus-carga-a-%~1.log" 2>&1"
REM `ping` e nao `timeout`: o timeout recusa entrada redirecionada (um runner que alimenta o stdin do
REM .bat o derruba na hora), e o ping espera do mesmo jeito em qualquer console.
ping -n 13 127.0.0.1 >nul
"%GODOT%" --path . --connect 127.0.0.1 --rede %REDE% --position 1920,0 ^
      --dois b --doisatraso 4 --doisrotulo %~1 --doisfim 14 %~2 ^
      --conta carga_b_%~1 --nome DoisB --raca Saiyan
REM O A E MORTO PELA LINHA DE COMANDO DELE (a conta desta rodada), e nao pelo titulo da janela: o
REM Godot troca o titulo do console ao subir, e o taskkill por titulo nao achava ninguem.
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*carga_a_%~1*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"
echo  ---- o log do A desta rodada: %TEMP%\jandirus-carga-a-%~1.log ----
findstr /c:"[dois-a]" /c:"segurando" /c:"SOLTEI" /c:"entrou (id" "%TEMP%\jandirus-carga-a-%~1.log"
exit /b 0
