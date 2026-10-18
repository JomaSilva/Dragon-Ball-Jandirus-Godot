@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- A ESCADA DO BIO-ANDROIDE, EM RETRATO

REM ===========================================================================
REM  UM RETRATO POR DEGRAU, LADO A LADO  (--biovivo + --diagbio)
REM
REM     ver-a-escada-do-bio.bat
REM
REM  A irma desta bancada e a `testar-bio.bat`, e as duas se precisam:
REM
REM     --bioteste   dirige e MEDE       (159 provas: a agulha, a gestacao, o
REM                  parto, a escada, o Zenkai, o Super Saiyajin, os numeros do
REM                  DM e 9 defeitos injetados. Nao olha a tela uma vez.)
REM     --biovivo    poe a escada em cena e FOTOGRAFA  (prova o PIXEL: cada
REM                  degrau desenha um bicho diferente? o DNA aparece?)
REM
REM  Nenhuma cobre o buraco da outra -- e a de cima passaria VERDE INTEIRA num
REM  servidor que subisse a escada sem que um pixel mudasse na tela. A arte dos
REM  quatro degraus passou meses importada neste projeto SEM UM CONSUMIDOR.
REM
REM  ---------------------------------------------------------------------------
REM  AS OITO POSES (o corpo nasce a 6 tiles de voce e sobe sozinho, ~80 s)
REM     0  HUMANO ............. o controle: ele ainda parece gente
REM     1  LARVA .............. Cell Larva
REM     2  IMPERFEITO ......... Bio Android 1
REM     3  SEMI-PERFEITO ...... Bio Android 2
REM     4  FORMA PERFEITA ..... Bio Android 3
REM     5  SUPER PERFEITA ..... Bio Android 4 por cima + RAIOS
REM     6  SUPER SAIYAJIN ..... **igual a pose 4**, e isso e a REGRA: o bio nao
REM                             ganha cabelo em forma nenhuma, o SSJ1 nao tem
REM                             raios, e a aura deste jogo so acende acima de
REM                             100%% de Ki. Um bio em SSJ parado nao se
REM                             distingue de um bio comum.
REM     7  SUPER SAIYAJIN CARREGANDO -- o contorno dourado acende. **Esta** e a
REM                             unica imagem em que o DNA Saiyajin aparece.
REM  ---------------------------------------------------------------------------
REM
REM  O QUE ELA MEDE, alem de salvar as imagens:
REM     * a FOLHA de cada pose, conferida contra `BioAndroids.Corpos` (medida
REM       ABSOLUTA -- "mudou alguma coisa" ficaria verde com os sprites trocados
REM       entre si);
REM     * a DIFERENCA DE PIXEL de cada par, ignorando o que se mexe sozinho
REM       (cada pose leva DOIS cliques; o que muda entre eles e transeunte e sai
REM       da conta);
REM     * que a TIRA final nao esta vazia -- ela ja saiu preta uma vez com as
REM       oito fotos boas no disco e o placar limpo.
REM
REM  PRECISA DE JANELA. No headless o `GetImage` volta vazio e nao ha veredito
REM  -- e aqui a foto E o teste. `--horateste 0.5` crava MEIO-DIA: a hora do
REM  mundo e sorteada, e uma escada fotografada a noite nao se le.
REM
REM  AS IMAGENS (em %APPDATA%\Godot\app_userdata\Dragon ball Jandirus):
REM     bio-escada.png ......... A TIRA, numerada. E esta que se olha.
REM     bio-retrato-0..7.png ... cada pose separada, ampliada 4x
REM
REM  PORTA PROPRIA (7956): se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a.
REM
REM  CONTA PROPRIA (bancada_bio_foto): nada toca conta de jogador. O corpo que
REM  vira bicho e um NPC forjado, nunca o seu.
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
echo     ver-a-escada-do-bio.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7956  (um processo so, COM JANELA)

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada fotografaria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ---- a escada do bio-androide, em retrato (COM JANELA, ~2 min) ----
REM SEM --headless: a foto e o juiz.
"%GODOT%" --path . --host --rede 7956 --biovivo --diagbio --horateste 0.5 ^
          --raca Human --conta bancada_bio_foto --nome Fotografo

echo.
echo  Leia o placar acima: "[bio-foto] ===== N ok, M falha(s) =====".
echo  A tira esta em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus\bio-escada.png".
pause
