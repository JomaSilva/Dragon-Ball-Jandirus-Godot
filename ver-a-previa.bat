@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- A PREVIA DO INSTALAR, fotografada

REM ===========================================================================
REM  A PREVIA NO MOUSE, MEDIDA NO PIXEL  (--fotoinstalar)
REM
REM     ver-a-previa.bat
REM
REM  O pedido do dono, na parte que so a FOTO responde:
REM     "...basicamente uma versao TRANSPARENTE dele vai ficar no mouse como um
REM      preview de como vai ficar quando instalar naquele local"
REM
REM  A `testar-instalar.bat` ja anda o ciclo inteiro sem janela, e ela e a
REM  bancada principal. Mas tudo o que ela pode dizer sobre a transparencia e que
REM  o campo `Modulate.A` vale 0,55 -- e ESCREVER 0,55 num campo nao e o mesmo que
REM  o jogador enxergar o chao por baixo do desenho. Este projeto ja deixou quatro
REM  bugs visuais passarem por milhares de checagens verdes exatamente assim.
REM
REM  Entao esta rodada PRECISA de janela: ela tira quatro fotos da tela de verdade
REM  e faz a conta do lado de fora do jogo.
REM
REM  O QUE ELA MEDE (procure o placar "[fotoinstalar] ===== N OK"):
REM     Z.4b  os dois lugares medidos ficam FORA da caixa de chat -- pixel de
REM           interface nao entra na conta (foi ele que ja deu "alfa 0,125" numa
REM           previa de 0,55, com um "Esc cancela" atravessado no recorte);
REM     Z.10  a previa MUDA os pixels de onde ela esta (senao "transparente"
REM           estaria satisfeito por nao desenhar nada);
REM     Z.12  e some do lugar antigo quando o cursor anda -- ela SEGUE o mouse;
REM     Z.14  os dois fundos sao mesmo diferentes (sem isso a razao seria 0/0 e
REM           as tres provas seguintes nao valeriam nada);
REM     Z.15  o fundo ATRAVESSA a previa: ela nao e opaca  <- o pedido do dono;
REM     Z.16  ...e nao e invisivel;
REM     Z.17  e o alfa MEDIDO NO PIXEL bate com o que o codigo pediu;
REM     Z.18  depois do Esc a tela volta a ser a de antes;
REM     Z.19  o roteiro andou ate a ultima linha. Esta e a guarda contra o pior
REM           desfecho possivel: uma excecao no meio esgota a corrotina, o
REM           `MoveNext()` seguinte devolve `false` igualzinho a um fim normal, e
REM           o placar fecha "0 FALHA(S)" com metade das provas NAO TENDO RODADO.
REM           Ja aconteceu aqui -- e o que faltava era justamente Z.10 a Z.18.
REM
REM  AS FOTOS (a ultima e a que se olha):
REM     instalar-A-sem-previa.png      a cena limpa
REM     instalar-B-previa-lugar-1.png  a previa no primeiro lugar
REM     instalar-C-previa-lugar-2.png  e no segundo
REM     instalar-D-depois-do-esc.png   depois do Esc
REM     instalar-E-tira.png            OS CINCO RECORTES LADO A LADO, 3x:
REM                                    fundo A | previa em A | fundo B |
REM                                    previa em B | depois do Esc
REM
REM  ============================================================================
REM  SEGUNDO MONITOR (--position 1920,0): o dono trabalha no principal, e esta
REM  bancada MEXE NO CURSOR de verdade. Ela devolve o ponteiro pra onde estava no
REM  fim (ver `RoboDeFotoDoInstalar.Fim`), mas enquanto roda o mouse e dela.
REM
REM  A PASTA DE SAVES DO DONO NAO E TOCADA: o APPDATA e desviado ANTES de o Godot
REM  subir, e o `user://` sai dele sem uma linha de codigo envolvida. E de la que
REM  as fotos tambem saem -- o caminho aparece no fim.
REM  ============================================================================
REM
REM  PORTA PROPRIA (7976): se aparecer "FALHOU ao abrir a porta", ha outra rodada
REM  viva -- feche-a.
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
echo     ver-a-previa.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7976

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    REM `-t:Rebuild` e OBRIGATORIO: o build incremental ja deu "compilacao com
    REM exito" sem trocar a DLL, e o Godot subiu com o binario de ontem.
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada fotografaria a versao de ontem.
        pause
        exit /b 1
    )
)

REM ---- o desvio da pasta de usuario, e ele vem ANTES do Godot ----
set "APPDATA=%TEMP%\jandirus-bancada-previa"
if not exist "%APPDATA%" mkdir "%APPDATA%"
echo.
echo  Saves desviados para: %APPDATA%
echo  (a pasta real do dono nao e tocada por esta rodada)

echo.
echo  ---- a previa, fotografada (procure "[fotoinstalar] ===== N OK") ----
REM `--horateste 0.5` trava o relogio do mundo: sem isso o veu de clima muda
REM entre uma foto e a outra e a diferenca medida deixa de ser so a previa.
"%GODOT%" --path . --host --rede 7976 --techteste --fotoinstalar --horateste 0.5 ^
          --position 1920,0 --resolution 1280x720 ^
          --raca Human --conta bancada_fotoinstalar --nome Fotografo

echo.
echo  As fotos ficam em:
echo     %APPDATA%\Godot\app_userdata\Dragon ball Jandirus\instalar-*.png
echo  A que se olha e a instalar-E-tira.png
echo.
pause
