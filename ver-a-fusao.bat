@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- a FUSAO fotografada

REM ===========================================================================
REM  A FUSAO, FOTOGRAFADA  (--diagfotofusao)
REM
REM     ver-a-fusao.bat
REM
REM  A IRMA DESTA BANCADA E A `testar-fusao.bat`, que mede a fusao inteira em
REM  NUMERO -- 157 provas com treze defeitos injetados, tudo verde.
REM
REM  **E ela ficaria verde com a fusao desenhada careca e de calcao.** Entre o
REM  `LookDeFusao` do servidor e o pixel ha o `PeerLook`, o `_fusaoDaZona` do
REM  `World`, a pilha de camadas do `CharacterVisual`, o `CabelosDeForma` e o
REM  shader do corpo. Este projeto ja catalogou esse cego cinco vezes (a memoria
REM  "a bancada mede INTENCAO"), e as fotos que o dono pediu sao exatamente a
REM  metade que so o olho fecha.
REM
REM  O ROTEIRO (7 tomadas, no `user://` -- os caminhos saem no console)
REM     A0 fusao-a0-acendendo        a `FusionLight` acendendo sobre os dois
REM     A0b fusao-a0b-janela-limpa   a tela SEM disco assim que o estouro acaba --
REM                                  e a prova de que a luz nao e uma cortina
REM     A1 fusao-a1-cena             ...e o estouro cheio: UM disco, no meio deles
REM     A2 fusao-a2-branco           o corpo BRANCO no climax (mistura 0,91)
REM     A3 fusao-a3-branco-escoando  o branco na cauda, com a silhueta legivel
REM        fusao-A-cena.png          a tira: janela limpa / luz / branco / escoando
REM     B1 fusao-b1-metamoro         a METAMORO -- SO o colete metamoriano
REM     B2 fusao-b2-potara           a POTARA  -- brinco + a roupa do convidador
REM        fusao-B-lado-a-lado.png   **a tira que o dono pediu**, as duas juntas
REM     C1 fusao-c1-ssj4             a fusao em SSJ4, de cabelo VERMELHO
REM
REM     Cada tomada sai em DOIS arquivos: a tela cheia (prova o LUGAR) e um
REM     `-perto.png` recortado e ampliado 3x em Nearest -- num quadro de
REM     1600x900, a diferenca entre um colete e um brinco sao doze pixels.
REM
REM  NADA AQUI FUNDE NINGUEM: a cena sai do `ComecarACenaDaFusao` (o mesmo funil
REM  da danca resolvida e da Potara aceita), a fusao sai do
REM  `if (agora >= c.Funde)` do `TickDaCenaDeFusao`, a roupa e o cabelo saem do
REM  `Fundir`, o desfazer sai do `Separar` e o SSJ4 sai do `admin_forma` do
REM  menu P. O convite e o quick time event ficam de fora de proposito: eles nao
REM  tem pixel, e sao medidos de ponta a ponta pela `--fusaoduplateste`.
REM
REM  ============ E ELA CRAVA O SOL A PINO, TIQUE A TIQUE ============
REM  A primeira rodada desta bancada fechou "TUDO OK, cinco tomadas" com as
REM  cinco fotos PRETAS: o mundo estava de noite, e as checagens de campo leem o
REM  `LookDeFusao` e nao o pixel. Hoje ela acerta o meio-dia DA ZONA EM QUE O
REM  JOGADOR ESTA (a segunda tentativa usou a Terra como regua e a foto da
REM  Potara saiu preta noutro planeta), e alem disso MEDE O PIXEL MAIS CLARO de
REM  cada recorte -- porque "esta de dia" e uma teoria e "da pra olhar" e o fato.
REM  O ceu e devolvido ao que era na limpeza.
REM
REM  ELA PRECISA DE JANELA: no headless o `GetImage` volta vazio e as fotos saem
REM  em branco. A JANELA ABRE NO SEGUNDO MONITOR (`--position 1920,0`) porque o
REM  dono trabalha no principal.
REM
REM  E ELA LIMPA O QUE POS NO MUNDO -- o corpo forjado, a fusao que sobrou, a
REM  recarga de 1 h, o penteado emprestado do jogador e o adianto do ceu.
REM
REM  PORTA 7908: se aparecer "FALHOU ao abrir a porta", ha outra rodada viva --
REM  feche-a.
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
echo     ver-a-fusao.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7908

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

echo.
echo  ---- as sete tomadas, com janela no SEGUNDO monitor ----
echo.
echo   Leva uns 60 s (duas cinematicas de fusao de 7 s cada e a do SSJ4, que
echo   espera a cena de forma acabar antes de fotografar). A janela fecha
echo   sozinha e o placar sai no console:
echo   "[fotofusao] ===== TUDO OK (7 tomadas) =====".
echo.
"%GODOT%" --path . --host --rede 7908 --diagfotofusao ^
          --position 1920,0 --resolution 1600x900 ^
          --raca Saiyan --conta bancada_foto_fusao --nome Olheiro

echo.
echo  Encerrado.
pause
