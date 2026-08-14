@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do corpo largado (mente + leme)

REM ===========================================================================
REM  O CORPO QUE FICA, COM DOIS CORPOS DE VERDADE  (--menteviva)
REM
REM     testar-mente.bat
REM
REM  ESTA E A UNICA BANCADA DO PROJETO QUE SOBE DOIS PROCESSOS, e o motivo esta
REM  no codigo: `LargarOCorpo` recusa quem nao tem `Peer` -- e a recusa e
REM  deliberada, esta escrita la ("corpo sem dono nao tem atencao pra mandar
REM  embora") e nao foi afrouxada por causa de um teste. Corpo forjado nao tem
REM  `Peer`, entao a PORTA da mente e a do leme nunca tinham sido medidas.
REM
REM  E o VISITANTE nao existe de outro jeito: ele e a unica familia em que DUAS
REM  pessoas precisam atravessar a porta -- uma pra deixar o corpo no chao e
REM  outra pra reparar nele.
REM
REM  O PLACAR SAI NO PROCESSO ANFITRIAO (o servidor e a autoridade). Procure:
REM     [menteviva] ============ N OK, M FALHA(S) ============
REM
REM  PORTA PROPRIA (7997): enquanto um --host estiver no ar nenhum outro sobe na
REM  mesma porta. Se aparecer
REM     [server] FALHOU ao abrir a porta 7997
REM  ha outra rodada viva -- feche-a.
REM
REM  CONTAS PROPRIAS, APAGADAS NO FIM: a bancada empurra BP, quebra membro e
REM  larga corpo. Nada aqui toca conta de jogador.
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
echo     testar-mente.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7997  (dois processos: anfitriao + convidado)

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
echo   O CONVIDADO entra 12 s depois do anfitriao (ele precisa achar a
echo   porta aberta). A bancada roda no login DELE e fecha o processo.
echo   Procure:  [menteviva] ============ N OK, M FALHA(S) ============
echo  ================================================================
echo.

REM O CONVIDADO SOBE ATRASADO, numa janela propria e com titulo: ele nao pode
REM discar antes de a porta existir, e o titulo e como o taskkill o acha depois.
REM
REM Sem o separador "--": com ele as flags vao parar em GetCmdlineUserArgs() e o
REM cliente sobe MUDO (sem conta, sem raca). Ver servidor.bat.
REM O ATRASO E `ping` E NAO `timeout`: o `timeout` LE do teclado (ele aceita uma
REM tecla pra pular), e morre com "Input redirection is not supported" quando
REM alguem roda o .bat com a entrada redirecionada -- que e o caso de qualquer
REM automacao. O convidado nunca subiria, e a bancada ficaria esperando o
REM segundo corpo pra sempre.
start "menteviva-convidado" /min cmd /c "ping -n 13 127.0.0.1 >nul & ""%GODOT%"" --headless --path . --rede 7997 --connect 127.0.0.1 --raca Saiyan --conta bancada_menteviva_b --nome MenteVivaB"

"%GODOT%" --headless --path . --host --rede 7997 --menteviva --raca Saiyan ^
          --conta bancada_menteviva_a --nome MenteVivaA

REM O ANFITRIAO SE FECHA SOZINHO no fim da bancada; o convidado nao tem como
REM saber que acabou (ele so ve a conexao cair), entao quem o tira e daqui.
REM
REM PELA LINHA DE COMANDO, e nao por titulo de janela: o `taskkill /FI WINDOWTITLE`
REM nao pega o convidado. O Godot console.exe RELANCA a si mesmo num segundo
REM processo (o de verdade, sem console), e esse filho nasce fora da janela que o
REM `start` nomeou -- ficavam DOIS Godot vivos segurando a porta 7997, e a rodada
REM seguinte subiria com "FALHOU ao abrir a porta". A conta da bancada esta na
REM linha de comando dos dois, e ela e o unico jeito de acertar os dois.
REM (o cano vai SEM `^`: dentro de aspas o cmd nao o interpreta, e o `^|` chegaria
REM literal no PowerShell -- erro de sintaxe, e nenhum processo morto.)
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*bancada_menteviva_b*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul
taskkill /F /FI "WINDOWTITLE eq menteviva-convidado*" >nul 2>nul

echo.
echo  Encerrado.
pause
