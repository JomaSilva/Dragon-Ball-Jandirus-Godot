@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- a VISTA POR ALTURA, com DUAS TELAS

REM ===========================================================================
REM  DE CIMA SE VE, DE BAIXO NAO  (--vista a + --vista b)
REM
REM     testar-vista.bat            (rodada de 90 s)
REM     testar-vista.bat 120        (rodada mais longa)
REM
REM  O pedido do dono, literal:
REM     "voar mt alto faz vc N CONSEGUIR VER jogadores e npcs mt abaixo de vc e
REM      isso NAO DEVERIA ACONTECER. somente o oposto: pessoas mt abaixo da sua
REM      altura N CONSEGUIRIAM TE VER, mas pessoas em ALTURAS MAIORES q vc
REM      CONSEGUEM TE VER"
REM
REM  Isto e uma regra ASSIMETRICA, e assimetria NAO SE PROVA COM UMA TELA. Numa
REM  o corpo esta, na outra nao -- no MESMO instante. Num processo so os dois
REM  lados sao a mesma memoria e dao a mesma resposta.
REM
REM  E A BANCADA NAO PERGUNTA AO `Voo.Enxerga`: a tabela de expectativa dela e
REM  escrita a mao, das palavras do dono. Comparar a tela com a funcao que se
REM  julga e o cego que a memoria "a bancada mede INTENCAO" ja catalogou --
REM  "as duas telas concordam" fica verde com as duas erradas igual.
REM
REM  AS QUATRO CONFIGURACOES (as duas telas medem e fotografam cada uma)
REM     1. CHAO      Alfa 0, Beta 0   -> os DOIS se veem     (o CONTRA-EXEMPLO)
REM     2. RASANTE   Alfa 1, Beta 0   -> os DOIS se veem     (o limiar nao corta)
REM     3. ALTO      Alfa 2+, Beta 0  -> Alfa ve Beta; Beta NAO ve Alfa
REM     4. INVERSAO  Alfa 0, Beta 2+  -> Beta ve Alfa; Alfa NAO ve Beta
REM
REM  A fase 4 e a linha inteira: "a diferenca e grande?" e "quem esta em cima?"
REM  explicam as fases 1-3 do mesmo jeito, e so a INVERSAO as separa.
REM
REM  O QUE MAIS ELA MEDE (os consumidores que NAO foram mudados, um por linha)
REM     * o CHAT LOCAL chega mesmo de quem nao se ve (ele corta por zona e
REM       distancia, nunca por altura)
REM     * o BALAO recebe o texto MAS so desenha se o corpo desenha (ele e filho
REM       do corpo, e nao tem regra propria)
REM     * a GRADE DE COLISAO do cliente ganhou corpos -- e nenhuma colisao nova,
REM       porque `ClasseDeCorpo.MesmoAndar` e igualdade estrita
REM     * `Voo.PodeAcertar` continua cabendo dentro de `Voo.Enxerga`: NINGUEM
REM       leva soco de quem nao enxerga
REM
REM  E DUAS INVARIANTES POR QUADRO, que quatro fotos nao pegariam: em nenhum
REM  quadro alguem no meu andar ou ABAIXO sumiu da minha tela, e em nenhum
REM  quadro alguem DOIS andares acima apareceu.
REM
REM  AS FLAGS DE SERVIDOR NAO SAO ENFEITE:
REM     --vooteste       da a skill de voo no nivel 2. Sem ela o servidor RECUSA
REM                      a decolagem e a bancada nao tem o que medir.
REM     --bpteste 100000 o tanque de Ki. Voar drena `max(35/75,1) x 5` = 5 Ki/s
REM                      (`Stats.dm:415`), e com o tanque de um personagem novo
REM                      o corpo CAI no meio da fase ALTO -- a bancada ficaria
REM                      vermelha por falta de combustivel, e nao por defeito.
REM
REM  ELA PRECISA DE JANELA (duas): no headless o `GetImage` volta vazio e as
REM  fotos -- que sao METADE da prova -- saem em branco. AS DUAS JANELAS ABREM
REM  NO SEGUNDO MONITOR, empilhadas, porque o dono trabalha no principal.
REM
REM  PORTA PROPRIA (7922). Se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a.
REM
REM  AS FOTOS E OS DOIS RELATORIOS saem em
REM     %APPDATA%\Godot\app_userdata\Dragon ball Jandirus\
REM        vista-a-1-Chao.png     vista-b-1-Chao.png
REM        vista-a-2-Rasante.png  vista-b-2-Rasante.png
REM        vista-a-3-Alto.png     vista-b-3-Alto.png      <-- O PAR DO PEDIDO
REM        vista-a-4-Inversao.png vista-b-4-Inversao.png  <-- O PAR DA INVERSAO
REM        vista-a.txt            vista-b.txt
REM  Cada foto sai carimbada com o relogio de parede no relatorio: e o que
REM  deixa "no mesmo instante" ser LIDO em vez de prometido.
REM
REM  PROCURE, no fim:
REM     ===== N OK, M FALHA =====     nas duas.
REM ===========================================================================

cd /d "%~dp0"

set FIM=%1
if "%FIM%"=="" set FIM=90

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
echo     testar-vista.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7922  (dois processos, DUAS JANELAS no segundo monitor)
echo  Fim   : %FIM% s

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
echo  ================================================================
echo   Alfa (host) abre em cima; Beta entra 12 s depois, embaixo.
echo   Beta anda 3 tiles pra sair de cima do Alfa; dai as 4 fases.
echo   As duas janelas fecham sozinhas.
echo  ================================================================
echo.

REM O ATRASO E `ping` E NAO `timeout`: o `timeout` LE do teclado e morre com
REM "Input redirection is not supported" em qualquer automacao.
REM Sem o separador "--": com ele os argumentos vao parar em GetCmdlineUserArgs()
REM e as flags de bancada saem MUDAS (ver servidor.bat).
start "vista-beta" cmd /c "ping -n 13 127.0.0.1 >nul & ""%GODOT%"" --path . --rede 7922 --connect 127.0.0.1 --vista b --vistaalvo Alfa --vistafim %FIM% --position 1928,600 --resolution 900x480 --raca Human --conta bancada_vista_b --nome Beta"

"%GODOT%" --path . --host --rede 7922 --vooteste --bpteste 100000 ^
          --vista a --vistaalvo Beta --vistafim %FIM% ^
          --position 1928,60 --resolution 900x480 ^
          --raca Human --conta bancada_vista_a --nome Alfa

REM O Beta nao sabe que acabou se o host cair antes. Pela LINHA DE COMANDO e nao
REM por titulo: o console.exe relanca a si mesmo num filho que nasce fora da
REM janela que o `start` nomeou, e o filho e quem segura a porta.
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*bancada_vista_b*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul
taskkill /F /FI "WINDOWTITLE eq vista-beta*" >nul 2>nul

echo.
echo  ---- relatorio do ALFA (o que sobe primeiro) ----
type "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus\vista-a.txt"
echo.
echo  ---- relatorio do BETA (o que sobe na inversao) ----
type "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus\vista-b.txt"
echo.
echo  Fotos em: %APPDATA%\Godot\app_userdata\Dragon ball Jandirus\vista-*.png
echo.
pause
