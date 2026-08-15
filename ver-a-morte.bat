@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- a MORTE vista de fora (duas telas)

REM ===========================================================================
REM  A MORTE VISTA DE FORA  (--morte a / --morte b)
REM
REM     ver-a-morte.bat
REM
REM  O pedido do dono, literal:
REM     "falta fazer o personagem quando MORRER ir pro OUTRO MUNDO e a AUREOLA
REM      aparecer sobre a cabeca"
REM
REM  E a pergunta que ESTA bancada responde e a segunda metade dele: a auréola
REM  aparece **pros OUTROS**? Um corpo so nao prova -- e as duas bancadas que a
REM  auréola ja tinha nao olham pra ela:
REM
REM     --alemteste   e de SERVIDOR e headless. Mede a viagem, a triagem e o
REM                   byte do fio, e nunca desenhou um pixel.
REM     --diagforma   chama `MostrarAureola(true)` NA MAO num boneco do proprio
REM                   processo. Prova a FUNCAO, nao o percurso: aquele corpo
REM                   nunca morreu, nunca viajou e nunca veio pela rede.
REM
REM  AQUI SAO DOIS PROCESSOS, e a morte de um e olhada pelo outro.
REM
REM  O ROTEIRO (5 fotos, no `user://` -- ver o caminho impresso no console)
REM     1  morte-1-os-dois-vivos          a foto de CONTROLE, antes do 1o soco
REM     2  morte-2-cadaver-sem-aureola    o CADAVER no berco (sem auréola: ele e
REM                                       o corpo exato de quem caiu), eu vivo ao lado
REM     2b morte-2b-cadaver-assentado     o mesmo, com o corpo ja assentado
REM     3  morte-3-no-alem                o Outro Mundo, depois da viagem -- e e
REM                                       AQUI que a auréola acende (`Death.dm:106-108`)
REM     4  morte-4-morto-voando           a auréola acompanha o corpo no ar
REM     5  morte-5-revivido               revivido NO LUGAR: a auréola sumiu
REM
REM     Cada foto sai em DOIS arquivos: a tela cheia (prova o LUGAR) e um
REM     `-zoom.png` ampliado em Nearest (prova a CABECA -- no zoom do jogo o
REM     boneco tem 32 px e a auréola tem QUATRO linhas de pixel dentro dele).
REM
REM  ELA MATA NO COMBATE, e nao na bandeira: o olhador liga o LETAL, mira na
REM  cabeca e soca ate um vital quebrar. `admin_matar` seria mais curto e
REM  mediria menos -- entre a bandeira e o soco moram o NOCAUTE e a diferenca
REM  entre "caido" e "morto", que e exatamente o que a auréola tem que separar
REM  na tela. A bancada confere que ele passou por KO SEM auréola antes.
REM
REM  O `B` PRECISA DE JANELA: headless nao renderiza e o `GetImage` volta vazio.
REM  O `A` roda headless de proposito -- a tela dele nao e a pergunta.
REM
REM  A ORDEM IMPORTA: o B sobe primeiro e promove a propria conta no 1o segundo.
REM  O servidor desliga o admin-por-endereco assim que DUAS contas chegam de
REM  endereco local (`ConferirAmbiguidadeDeHost`), e sem a marca no disco o
REM  `admin_ir` do meio da rodada sairia sem ninguem pra responder.
REM
REM  PORTA PROPRIA (7976): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     ver-a-morte.bat
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
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ---- o OLHADOR (hospeda, mata e fotografa) -- COM JANELA ----
start "morte-b (olhador)" "%GODOT%" --path . --host --rede 7976 --vooteste ^
      --morte b --mortealvo Falecido --mortefim 210 --conta olheiro --nome Olheiro --raca Human

echo  ---- esperando o servidor abrir a porta e o admin ir pro disco ----
REM 8 s: o B precisa entrar no mundo E rodar o `admin_promover` ANTES de a
REM segunda conta local chegar. Cortar isso deixa o `admin_ir` sem efeito.
timeout /t 8 /nobreak >nul

echo  ---- o MORTO (apanha e conta pra que zona foi) -- headless ----
start "morte-a (o morto)" "%GODOT%" --headless --path . --connect 127.0.0.1 --rede 7976 ^
      --morte a --mortealvo Olheiro --mortefim 240 --conta falecido --nome Falecido --raca Saiyan

echo.
echo  As duas janelas fecham sozinhas. O placar do olhador sai no console dele
echo  e tambem em  user://morte-diario.txt  (a janela some junto com o console).
echo.
pause
