@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- a prova do berco (--bercoprova)

REM ===========================================================================
REM  A PROVA DO BERCO  (--bercoprova)
REM
REM      testar-o-berco.bat
REM
REM  O RELATO DO DONO, LITERAL:
REM
REM      "por algum motivo todas as racas q eram pra nascer na terra tao
REM       nascendo em namek (isso n e problema do export pq ta acontecendo ate
REM       mesmo com a build dentro do godot)"
REM
REM  Ele estava certo em tudo, inclusive em ter descartado o empacotamento. E
REM  havia DEZENAS de bancadas verdes enquanto isso acontecia.
REM
REM  Esta e a TERCEIRA irma do berco, e a divisao de trabalho e:
REM
REM      --diagberco    a REGRA      (funcao pura + catalogo, sem corpo nenhum)
REM      --bercovivo    a CORRENTE   (ficha no disco -> corpo -> pouso -> chao)
REM      --bercoprova   a BANCADA COMO REU: ela poe o mundo em cada estado que
REM                     ja quebrou o jogo e exige o placar certo em cada um.
REM
REM  SEIS FAMILIAS:
REM
REM    1. O MUNDO COMO ELE ESTA -- uma linha por raca (TODAS as 24, e nao uma
REM       amostra), dizendo a zona ESPERADA e a OBTIDA. E as DUAS metades: nao
REM       basta "ninguem nasceu no lugar errado" (verde num mundo sem ninguem);
REM       todo planeta que e berco de alguem tem que RECEBER a conta certa.
REM    2. O DEFEITO DE VOLTA -- a Terra morta (o mundo do relato) e depois NAMEK
REM       morta (a outra metade). A chamada nominal TEM que ficar vermelha, e
REM       so nas racas daquele planeta.
REM    3. A ORDEM DA CARTA -- o destino do defeito e uma POSICAO NUMA LISTA
REM       (`Espaco.PreFeitos`). Matando a frente da carta em ordem o destino
REM       ANDA (Namek -> Vegeta -> Icer), e com uma zona nova na frente ('Hera',
REM       que ja tem mapa no manifesto) ele vira 'Hera'. Uma bancada que
REM       cravasse "Namek" ficaria verde nesse dia.
REM    4. RENASCER -- o OUTRO caminho (`Renascer` -> `MandarProBerco`), raca por
REM       raca, tambem com o defeito injetado.
REM    5. O POVO, CONTADO NO MUNDO -- `TickDoPovoamento` de producao e censo de
REM       corpos: na Terra so humano, em Namek so namekuseijin, em Vegeta so
REM       saiyajin -- e >0 em cada um, que e o que impede "verde por ausencia".
REM    6. POR QUE NENHUMA BANCADA PEGOU -- as tres cegueiras, MEDIDAS: as tres
REM       ficam VERDES no mundo em que 10 racas nascem no planeta errado.
REM
REM  O MUNDO DO DONO NAO PAGA A CONTA: toda morte de planeta desta bancada
REM  acontece dentro do `PalcoDeMortesDeBancada`, que recusa a gravacao e devolve
REM  registro, tremores, cargas e ceu no fim. O `planetas-mortos.json` fica
REM  intocado, byte a byte -- foi exatamente essa falta que causou o relato.
REM
REM  NAO PRECISA DE JANELA nem de ninguem logado: os corpos nascem sem `Peer`.
REM  A bancada roda no BOOT; o servidor fica de pe depois dela -- feche esta
REM  janela quando o placar sair.
REM
REM  PORTA PROPRIA (7961): se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a. O placar sai mesmo assim, porque a bancada roda
REM  antes de a porta abrir.
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
echo     testar-o-berco.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7961

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
echo  ---- a prova do berco (6 familias, 5 mundos adversarios) ----
echo  A bancada sai no console com o prefixo [bercoprova]. As tres irmas rodam
echo  juntas aqui: --diagberco (a regra), --bercovivo (a corrente) e
echo  --bercoprova (a prova). Os tres placares saem em sequencia.
echo.
"%GODOT%" --headless --path . --server --port 7961 --diagberco --bercovivo --bercoprova

echo.
echo  Encerrado.
pause
