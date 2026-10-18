@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- o RESCALDO com DOIS CLIENTES de verdade

REM ===========================================================================
REM  O RESCALDO DE UM MUNDO MORTO, VISTO POR DOIS PROCESSOS  (--destrocosvivos)
REM
REM      testar-destrocos.bat
REM
REM  O pedido do dono, na parte que ele grifou:
REM     "ele vai sumir do espaco pra todos os jogadores (server sync) e onde
REM      ficava o planeta vao ter uns asteroides/rochas q vao girar lentamente
REM      e se afastar de onde era o planeta"
REM
REM  ---- POR QUE ESTA BANCADA PRECISA DE DOIS PROCESSOS ----
REM  "PRA TODOS" e uma afirmacao sobre PROCESSOS, e ela nao tem como ser
REM  verdadeira nem falsa num processo so. O rescaldo ja tinha cobertura dos
REM  dois lados, e nenhum dos dois alcanca essa palavra:
REM
REM    * a --planetateste (PROVA 10) e de servidor puro: prova que o relogio do
REM      rescaldo anda, para no fim da janela e volta fechado do disco. Ela nao
REM      desenha nada -- por construcao nao sabe o que apareceu na tela de
REM      ninguem;
REM    * a --diagagonia mede o pixel inteiro do efeito, mas num processo so. O
REM      que ela chama de "duas telas" sao dois nodes na MESMA memoria, com a
REM      mesma DLL e uma lista de mortos que ela mesma escreveu.
REM
REM  Aqui a lista de mortos VIAJA no fio e dois clientes obedecem a ela sozinhos
REM  -- que e onde mora o unico defeito que o dono nomeou: o planeta sumir pra um
REM  e continuar aparecendo pro outro.
REM
REM  ---- O QUE ELA MEDE ----
REM     * CONTROLE .... com o mundo vivo, os DOIS clientes desenham o disco;
REM     * D2 .......... depois do commit, NENHUM dos dois desenha;
REM     * o CAMPO ..... nasceu nos dois, com o mesmo numero de cacos e na mesma
REM                     raiz -- sem um byte de asteroide no fio;
REM     * DETERMINISMO. perguntando aos dois onde as pedras estariam no MESMO
REM                     instante, a lista bate caractere por caractere;
REM     * o RELOGIO ... os dois estao no mesmo ponto do minuto do rescaldo.
REM
REM  ---- ELA MATA UM PLANETA DE VERDADE ----
REM  A Terra morre nesta cena, e o cadaver fica no save da pasta de usuario. Por
REM  isso a bancada RESSUSCITA o planeta no comeco de cada rodada (pela porta do
REM  `admin_restaurar_planeta`) -- senao a segunda rodada na mesma pasta comecaria
REM  com o mundo ja destruido e o CONTROLE sairia vermelho por montagem.
REM
REM  O pavio de 310 s vira 4 s pela sonda `SegundosDeExplosao`, que existe pra
REM  isso e ja e usada assim pela --planetateste. O CAMINHO nao e encurtado:
REM  ComecarDestruicao -> TickDaDestruicao -> ConsumarDestruicao -> MandarMortos.
REM
REM  Procure:  [destrocosvivos] ============ N OK, 0 FALHA(S) ============
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
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    REM `-t:Rebuild` e OBRIGATORIO: o build incremental ja deu "compilacao com
    REM exito" sem trocar a DLL, e o Godot subiu com o binario de ontem.
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ---- o mundo morre e os DOIS clientes contam o que viram ----
echo.

REM O CONVIDADO ENTRA DEPOIS: o host precisa estar ouvindo, e a bancada so
REM comeca quando o SEGUNDO cliente de verdade entra (ela conta quem tem Peer).
start "destrocos-convidado" /min cmd /c "ping -n 13 127.0.0.1 >nul & ""%GODOT%"" --headless --path . --rede 7983 --connect 127.0.0.1 --raca Saiyan --conta bancada_destrocos_b --nome DestrocoB --destrocos b"

"%GODOT%" --headless --path . --host --rede 7983 --destrocosvivos --destrocos a ^
          --raca Saiyan --conta bancada_destrocos_a --nome DestrocoA

echo.
REM SO O MEU CONVIDADO: pelo `--conta`, que e unico desta bancada. Um `taskkill`
REM largo por nome de imagem mataria o servidor de outra sessao na mesma maquina.
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*bancada_destrocos_b*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul

echo  Encerrado.
pause
