@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- a FOTO da chama (Aura, Big)

REM ===========================================================================
REM  A FOTO DA CHAMA  (--diagchama)
REM
REM      ver-a-chama.bat
REM
REM  O pedido do dono, marcado por ele como EXTREMA IMPORTANCIA:
REM     "mudar o sprite da CARGA/AURA DE CARREGAMENTO DE KI e de KI ACIMA DE
REM      100%% da FORMA BASE (e das formas q usam o mesmo sprite da base, como o
REM      MISTICO etc) para o sprite `Aura, Big.png`"
REM
REM  ============================ POR QUE ELA EXISTE ============================
REM  A `--diagforma` ja tem quatro mil checagens verdes sobre esta troca: a
REM  folha e a `Aura, Big`, ela carrega, todo quadro tem pixel, o RGB dela e
REM  chapado, o alfa dela e identico ao da `AuraSSjBig`. Tudo isso e verdade
REM  sobre o ARQUIVO e sobre o UNIFORM -- e nada disso e um pixel na tela.
REM
REM  E o buraco tem nome nesta casa ("a bancada mede INTENCAO") e um custo
REM  medido: quatro defeitos visuais depois de quatro mil provas verdes.
REM
REM  ============================ AS DUAS MEDIDAS ============================
REM     chama-jogo-*.png   O CORPO NO MUNDO, segurando C. Registro visual pro
REM                        dono -- e ele traz o NUMERO, nao o veredito: medido,
REM                        o piso de ruido em jogo e 27%% do recorte (os raios da
REM                        carga, a luz, o clima), da MESMA ordem do sinal.
REM
REM     chama-lab-*.png    A MESMA classe de producao (SpriteDeAura + o
REM                        Aura.gdshader de verdade) numa SubViewport de fundo
REM                        transparente. Sem mundo: o ruido cai a ZERO e a
REM                        diferenca medida e a da ARTE. QUEM DECIDE E ESTA.
REM
REM  ============================ A PROVA QUE VALE MAIS QUE "MUDOU" ============================
REM  `Aura, Big.png` e rgb(0,0,0) em 100%% dos pixels opacos. Sem o ramo
REM  `forma_no_alfa` do shader ela sai como SILHUETA PRETA em volta de 19 formas
REM  -- com a bancada de folha VERDE. Entao o laboratorio mede o BRILHO, e
REM  desenha a mesma folha com o uniform ERRADO de proposito pra provar que a
REM  medida ENXERGA o desastre:
REM
REM     chama-lab-3-base-como-o-jogo-pinta.png      brilho 0,460
REM     chama-lab-4-DEFEITO-sem-forma-no-alfa.png   brilho 0,000  <- preto
REM
REM  Essa injecao roda SOZINHA, dentro da rodada. Nao ha o que desfazer.
REM
REM  ============================ E A DE FONTE, QUE VOCE FAZ NA MAO ============================
REM  Pra provar que ela pega a TROCA DE FOLHA (e nao so o shader), em
REM  Client\SpriteDeAura.cs troque
REM       FolhaBase = "res://Assets/Sprites/Auras/Aura, Big.tres"
REM  por  FolhaBase = "res://Assets/Sprites/Auras/colorablebigaura.tres"
REM  e rode de novo.
REM  MEDIDO: 2 provas em vermelho --
REM       "a chama da base MUDOU de desenho: 0x o ruido, 0,0%% dos pixels"
REM       "e a MEDIDA ENXERGA o desastre ... (0,492)"
REM  DESFACA DEPOIS. Bancada que fica verde com o defeito dentro e pior que
REM  bancada nenhuma.
REM
REM  ============================ AS FLAGS NAO SAO ENFEITE ============================
REM     --kiteste     o Ki acima de 100%% exige controle de Ki liberado. Sem ele
REM                   o servidor RECUSA a carga e as fases 2, 3 e 4 nao teriam o
REM                   que medir (a bancada diz "SEM MEDIDA", nao "ok").
REM     --horateste   crava meio-dia. A hora do mundo e sorteada, e o registro
REM                   visual de uma chama as 3 da manha nao responde nada.
REM
REM  PRECISA DE JANELA -- no --headless o GetImage volta vazio e as linhas de
REM  pixel saem como SEM MEDIDA. A JANELA VAI PRO MONITOR 2 (--position
REM  1920,0): o dono trabalha no principal.
REM
REM  CONTA PROPRIA: bancada_chama. Nada aqui toca personagem de jogador.
REM  PROCURE no fim:   ===== FIM: 19 OK, 0 FALHA(S), 0 SEM MEDIDA =====
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

REM porta limpa: bancada que nao sai sozinha segura a porta da proxima
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*diagchama*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>nul

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
"%GODOT%" --path . --host --rede 7904 --kiteste --bpteste 3000000 --horateste 0.5 ^
          --diagchama --position 1920,0 --resolution 1280x720 ^
          --raca Human --conta bancada_chama --nome Chama

echo.
echo  ============================ AS FOTOS ============================
echo  Em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus":
echo.
echo    chama-lab-1-base-hoje-Aura-Big.png            a chama de HOJE
echo    chama-lab-2-base-ontem-colorablebigaura.png   a de ONTEM (78%% diferente)
echo    chama-lab-3-base-como-o-jogo-pinta.png        brilho 0,460
echo    chama-lab-4-DEFEITO-sem-forma-no-alfa.png     brilho 0,000 (preta)
echo    chama-lab-5-mistico-herda-a-base.png          quem HERDA
echo    chama-lab-6-ssj-tem-a-dele.png                o CONTRA-EXEMPLO
echo    chama-lab-7-deus-quente-arte-propria.png      arte outra, nao so cor
echo.
echo    chama-jogo-1..6-*.png                         o corpo no mundo
echo.
pause
