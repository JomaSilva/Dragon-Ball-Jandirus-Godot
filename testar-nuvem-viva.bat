@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- a NUVEM com um corpo em cima

REM ===========================================================================
REM  A NUVEM COM UM CORPO EM CIMA  (--nuvemviva)
REM
REM      testar-nuvem-viva.bat
REM
REM  O pedido do dono, literal:
REM     "as NUVENS q tem no CAMINHO DA SERPENTE no outro mundo, se um jogador ir
REM      nelas SEM ESTAR COM FLY ATIVADO, ele vai automaticamente ser JOGADO NO
REM      MAPA DO INFERNO. e no mapa do LOOKOUT se o jogador cair na nuvem do
REM      mapa sem fly, ele CAI DE VOLTA PRA TERRA"
REM
REM  ============================ POR QUE A `nuvem-prova` NAO BASTA ============================
REM  Aquela (dotnet run --project Tools/AssetPipeline -- nuvem-prova) tem 62
REM  provas verdes e NENHUMA delas e sobre um jogador: ela le os .nuvem do
REM  disco, conta celula, confere destino declarado e exercita a funcao pura.
REM  Isso e o DADO e a PLANTA.
REM
REM  O pedido do dono e um ACONTECIMENTO. Entre a planta e o acontecimento moram
REM  seis coisas que nenhuma prova offline alcanca -- o laco do tique, a guarda
REM  de KO, a carencia compartilhada com as passagens, o funil
REM  `ModoDeTravessiaDe`, o `MoveToZone` e o `PontoLivrePerto` da zona de
REM  CHEGADA. Qualquer uma quebrada deixa as 62 verdes e o jogador andando por
REM  cima do ceu.
REM
REM  Esta poe um corpo em cima de uma celula de nuvem DE VERDADE, roda o
REM  TickDasNuvens DE PRODUCAO e pergunta em que zona ele acordou.
REM
REM  ============================ AS QUATRO FAMILIAS ============================
REM     1  Caminho da Serpente -> Inferno   (cai a pe / NAO cai voando)
REM     2  Templo (Lookout)    -> Terra     (cai a pe / NAO cai voando)
REM     3  quem cai chega em CHAO LIVRE, mirando de proposito numa celula que o
REM        mapa RECUSA -- porque a coordenada de verdade ja e chao bom, e uma
REM        prova contra ela fica verde com o funil ligado E desligado
REM     4  o CONTRA-EXEMPLO: Ceu e Reino dos Deuses so BARRAM. Sem ele, "toda
REM        nuvem derruba" (o oposto do pedido) passaria com nota cheia.
REM
REM  AS DUAS METADES ANDAM SEMPRE JUNTAS. "Cai a pe" sozinho fica verde num jogo
REM  em que a nuvem derruba ate quem voa; "nao cai voando" sozinho fica verde num
REM  jogo em que ela nao derruba ninguem.
REM
REM  ============================ E ELA SABE FICAR VERMELHA ============================
REM  13 defeitos injetados pelas `SondasDaNuvem` (o mesmo desenho do
REM  `SondasDoVacuo`), rodados DENTRO da rodada -- nao ha o que desfazer:
REM     * a metade do voo caiu (a nuvem derruba todo mundo)
REM     * a nuvem parou de derrubar (o estado ANTERIOR a esta tarefa)
REM     * o funil do modo travou em VOANDO
REM     * o destino sumiu / o destino trocou de zona
REM     * o funil de pouso saiu do caminho (o corpo chega dentro de parede)
REM     * a guarda que protege o Ceu caiu
REM
REM  RODA NO HEADLESS: aqui nao ha pixel, ha zona.
REM  ELA RECUSA RODAR com o host em cima de nuvem que derruba -- um defeito
REM  injetado o jogaria de zona, e ele nao saberia por que.
REM
REM  PROCURE no fim:
REM     [nuvem]   provas             : 31   (31 verdes, 0 vermelhas)
REM     [nuvem]   defeitos injetados : 13   (13 pegos, 0 passaram batido)
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
echo  Nao encontrei o Godot.   set GODOT=C:\caminho\Godot_..._console.exe
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7904

powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*nuvemviva*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul

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
echo  ---- a nuvem com um corpo em cima (31 provas, 13 defeitos injetados) ----
echo.
"%GODOT%" --headless --path . --host --rede 7904 --nuvemviva ^
          --raca Human --conta bancada_nuvem_viva --nome Nuvenzinha

echo.
echo  Encerrado. (feche esta janela -- o servidor nao sai sozinho)
pause
