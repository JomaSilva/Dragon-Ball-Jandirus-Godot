@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- o SILENCIO DO ESPACO, andado

REM ===========================================================================
REM  O SILENCIO DO ESPACO, ANDADO  (--diagsilencio)
REM
REM      testar-silencio.bat
REM
REM  O pedido do dono, literal:
REM     "no espaco o jogo n tem som, SOMENTE A MUSICA de combate. efeitos
REM      sonoros de ki, soco etc N EXISTEM, a nao ser q estejam DENTRO DE UMA
REM      NAVE como a capital ship. mas no espaco em si, usando roupa espacial ou
REM      sem ela, N TEM SOM, somente a OST"
REM
REM  ============================ POR QUE A `--diagtrilha` NAO BASTA ============================
REM  A `RoboDeTrilha.OSilencioDoEspaco` liga e desliga o vacuo NA MAO
REM  (AudioDirector.Vacuo(true)) e mede o barramento. Isso responde UMA pergunta:
REM  "o corte, quando pedido, funciona?".
REM
REM  A pergunta do dono e outra: "ao ENTRAR no espaco, ele e pedido?". Entre as
REM  duas moram o World.CarregarZona (que tem CINCO caminhos terminando em
REM  return, e o ramo do espaco e um deles), o MoveToZone e o pacote de troca de
REM  zona. Uma bancada que chama Vacuo(true) fica VERDE num jogo em que ninguem
REM  chama Vacuo coisa nenhuma.
REM
REM  Esta faz o SERVIDOR mudar o corpo de zona -- planeta, espaco, dentro da
REM  nave-capital, planeta de novo -- e pergunta ao AudioServer em cada parada.
REM
REM  ============================ O PAR QUE SE SEGURA ============================
REM  "o soco nao faz som" sozinho fica verde com o jogo INTEIRO mudo -- que e o
REM  conserto preguicoso (mutar o Master). Entao toda parada no vacuo mede as
REM  duas coisas juntas:
REM     * o efeito cala E o efeito continua sendo PEDIDO (o AudioDirector.Espiao
REM       dispara). Um soco que nunca tocou tambem "nao faz som", e nao e isso
REM       que o dono pediu;
REM     * a MUSICA de combate TOCA de verdade (TocandoDeTeste le os dois
REM       AudioStreamPlayer da trilha, e nao um campo de intencao).
REM
REM  E a ULTIMA parada e de volta num planeta -- a regressao mais provavel deste
REM  sistema e ele ficar LIGADO: o jogador pousa e continua sem ouvir soco
REM  nenhum, para sempre, e nada aponta pro espaco.
REM
REM  RODA NO HEADLESS: aqui nao ha pixel, ha barramento.
REM
REM  ============================ COMO ELA REPROVA -- as 4 injecoes, MEDIDAS ============================
REM  Sao de FONTE porque o que elas trocam sao tres linhas dentro do caminho de
REM  producao; injeta-las por chave exigiria um `if (modoDeTeste)` no audio do
REM  jogo. Cada uma leva meio minuto. DESFACA TODAS DEPOIS.
REM
REM  A) o corte no MASTER (o conserto preguicoso, que leva a OST junto)
REM     Client\AudioDirector.cs, em AplicarEfeitos:
REM        GetBusIndex(BusEfeitos)   ->   GetBusIndex("Master")
REM     MEDIDO: 6 provas em vermelho, entre elas
REM        "o corte NAO foi no `Master` (que levaria a OST junto)"
REM
REM  B) o VACUO NUNCA DESLIGA (o jogador pousa e fica mudo pra sempre)
REM     Client\AudioDirector.cs, primeira linha de Vacuo(bool):
REM        acrescente   if (!noVacuo) return;
REM     MEDIDO: 4 vermelhas -- as familias 3 (nave) e 4 (volta ao planeta)
REM
REM  C) a PERGUNTA DE ZONA ALARGA e o interior da nave vira espaco
REM     Core\World\Espaco.cs, em EhEspaco:
REM        acrescente   || z.Kind == ZoneKey.KindInterior
REM     MEDIDO: 3 vermelhas, a 1a
REM        "o interior da nave NAO e espaco (`Nave#1`)"
REM
REM  D) o CONTROLE DESLIZANTE escreve direto no barramento
REM     Client\AudioDirector.cs, em AplicarVolumes:
REM        troque   _volEfeitos = s.VolumeEfeitos; AplicarEfeitos();
REM        por      Volume(BusEfeitos, s.VolumeEfeitos);
REM     MEDIDO: 1 vermelha
REM        "mexer no controle de EFEITOS dentro do vacuo NAO devolve o som"
REM
REM  PROCURE no fim:   ===== FIM: 17 OK, 0 FALHA(S), 0 SEM MEDIDA =====
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

powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*diagsilencio*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul

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
echo  ---- planeta -^> espaco -^> dentro da nave -^> planeta (17 provas) ----
echo.
"%GODOT%" --headless --path . --host --rede 7904 --diagsilencio ^
          --raca Human --conta bancada_silencio --nome Silencio

echo.
echo  Encerrado. (feche esta janela -- o servidor nao sai sozinho)
pause
