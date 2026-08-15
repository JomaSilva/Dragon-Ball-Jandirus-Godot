@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do SORTEIO DO ARREMESSO

REM ===========================================================================
REM  O SORTEIO DO ARREMESSO (KNOCK BACK)  (--kbteste)
REM
REM     testar-knockback.bat
REM
REM  A QUEIXA DO DONO: "o SOCO DO DASH ta SEMPRE dando KNOCK BACK (acho q o soco
REM  forte tb). eles deveriam ter uma CHANCE UM POUCO MAIOR de dar knock back,
REM  pq fui lutar com uns npcs e era UM JOGANDO O OUTRO PRA LONGE e tava
REM  estranha a luta".
REM
REM  O "soco do dash" e o soco PESADO -- o arranque e o `Aproximar` de dentro do
REM  `Atacar`, e so o pesado busca a 160 px (480 com alvo marcado). Um defeito,
REM  nao dois.
REM
REM  A CAUSA ERA UMA LINHA: `attack cmn.dm:115` manda o pesado direto pro
REM  `Impact` num `else` sem `prob()` nenhum, e o port copiava isso ao pe da
REM  letra. Todo pesado que encostava arremessava, 100%%, jogando o corpo meio
REM  segundo pelo ar. Agora ele sorteia com a propria formula do DM aplicada ao
REM  peso do golpe: `prob(check * 10 * 3)`.
REM
REM  ELA MEDE O PAR "ANTES x DEPOIS" NO MESMO GOLPE. A formula de ontem continua
REM  no Core (Avaliar + ForcaDoPesado), entao cada soco e avaliado duas vezes e
REM  a diferenca que sobra e a mudanca -- e nao a populacao de fichas forjadas,
REM  que muda de bancada pra bancada e ja fez esta reprovar sem regressao.
REM
REM    1) O PESADO SORTEIA   antes 100%%, depois ~31%% em BP parelho.
REM    2) MAIOR QUE O LEVE   e as duas metades da frase do dono viram duas
REM                          afirmacoes: "maior que o leve" e "menor que 100%%".
REM    3) O CAMBALEIA        a faixa do meio tem que ficar IDENTICA, golpe a
REM                          golpe -- ela e o unico efeito que o pesado tem
REM                          contra quem e mais forte, e e um caminho MUDO.
REM    4) O MAIS FORTE       a 3,0x de BP continua arremessando quase sempre.
REM    5) FORCA E DISTANCIA  ninguem pediu voo mais curto: nenhum arremesso pode
REM                          mudar de DURACAO (contado por golpe, nao por media).
REM    6) O FORCADO          o golpe de saida do Zanzo Clash pula o sorteio.
REM    7) O FIO              o soco pelo `Atacar` de producao, de ponta a ponta.
REM
REM  ---- E AS DUAS QUE VEEM A BRIGA, E NAO O GOLPE (as queixas na unidade delas) ----
REM
REM    8) O PINGUE-PONGUE    quatro brigas de 45 s -- um NPC de cerebro de
REM                          producao contra um corpo dirigido como JOGADOR --,
REM                          com e sem o defeito injetado, na mesma praca.
REM                          Conta ARREMESSOS POR MINUTO e quanto tempo a briga
REM                          passa com um corpo no ar. Medido: 60/min -> 28/min,
REM                          e 90%% do tempo com alguem voando -> 34%%.
REM    9) OS SOCOS NO VAZIO  a outra queixa ("meus socos n acertam"): quantos %%
REM                          dos socos saem sem ninguem na frente. Medido, SEM
REM                          marcar o alvo: 83%% -> 27%%. COM o alvo marcado da
REM                          0%% nas duas versoes -- o arranque marcado (480 px)
REM                          cobre o arremesso mais longo (576 px), e por isso a
REM                          familia mede as duas maneiras de jogar.
REM
REM  RODA NO HEADLESS -- nao ha foto aqui, so afirmacao. Sao ~26 mil socos
REM  resolvidos pelo `MeleeResolver` de verdade (segundos) MAIS tres minutos de
REM  briga no relogio de parede -- as familias 8 e 9 contam RITMO, e ritmo so se
REM  mede no relogio (a recarga do arranque e carimbada com NowMs, nao com dt).
REM
REM  PORTA PROPRIA (7912): se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a.
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
echo     testar-knockback.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7912

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
echo  ---- o sorteio do arremesso: antes x depois, no mesmo golpe ----
"%GODOT%" --path . --headless --host --rede 7912 --kbteste ^
          --raca Human --conta bancada_kb --nome MedidorKB

echo.
echo  Leia o placar acima: "[kb] ==== N OK, M FALHA(S) ====".
pause
