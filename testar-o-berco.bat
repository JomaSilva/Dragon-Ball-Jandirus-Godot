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
REM  A REGRA MUDOU, E AS FAMILIAS 2, 3 E 4 FORAM INVERTIDAS:
REM
REM      Pedido do dono: "quando uma raca fica sem planeta natal, o jogador pode
REM      ou spawnar em um planeta q ele conquistou ou em um planeta proximo do
REM      planeta natal dele".
REM
REM  O recuo por LISTA (`ZonaDeRecuoViva`, o primeiro pre-feito vivo da carta)
REM  foi DELETADO. No lugar dele esta o REFUGIO (`GameServer.Refugio.cs` +
REM  `Core/Races/Refugio.cs`): o dominio conquistado ou o mundo vivo mais perto
REM  de casa, com ESCOLHA quando existem os dois. As familias que afirmavam o
REM  comportamento de lista afirmam hoje o contrario dele.
REM
REM  DEZ FAMILIAS (7 aqui, e 8-10 em `GameServer.RefugioProva.cs`):
REM
REM    1. O MUNDO COMO ELE ESTA -- uma linha por raca (TODAS as 24, e nao uma
REM       amostra), dizendo a zona ESPERADA e a OBTIDA. E as DUAS metades: nao
REM       basta "ninguem nasceu no lugar errado" (verde num mundo sem ninguem);
REM       todo planeta que e berco de alguem tem que RECEBER a conta certa.
REM    2. O BERCO MORTO -- a Terra morta (o mundo do relato) e depois NAMEK
REM       morta (a outra metade). INVERTIDA: a chamada nominal tem que ficar
REM       VERDE, e verde pelas duas metades juntas -- ninguem fora do conjunto
REM       certo E todo mundo daquele planeta SAIU de casa. Mais a linha que
REM       enterra a regra velha: nenhum destino e um pre-feito da carta.
REM    3. A ORDEM DA CARTA NAO IMPORTA MAIS -- INVERTIDA. Matando a frente da
REM       carta em ordem (Earth, Namek, Vegeta) o destino de quem e da Terra
REM       NAO anda; matando a estrela da TERRA inteira, ele anda pro anel
REM       seguinte. E o corte de 3 mundos dispara de verdade (53 vivos ao
REM       alcance, 3 no sorteio).
REM    4. RENASCER -- o OUTRO caminho (`Renascer` -> `DestinoDe` ->
REM       `MandarProBerco`), raca por raca. INVERTIDA: com a Terra morta o
REM       renascimento tem que ACHAR o refugio, e as 10 racas da Terra tem que
REM       renascer FORA de casa (senao o verde e imobilidade).
REM    5. O POVO, CONTADO NO MUNDO -- `TickDoPovoamento` de producao e censo de
REM       corpos: na Terra so humano, em Namek so namekuseijin, em Vegeta so
REM       saiyajin -- e >0 em cada um, que e o que impede "verde por ausencia".
REM    6. POR QUE NENHUMA BANCADA PEGOU -- as tres cegueiras, MEDIDAS: as tres
REM       ficam VERDES no mundo em que 10 racas acordam longe de casa.
REM    7. A ESCOLHA -- os cinco estados do pedido do dono, cada um com as DUAS
REM       medidas (a escolha que existia e o destino de verdade): as duas
REM       saidas (escolha, padrao = vizinhanca), o jogador escolhendo o
REM       dominio, ele voltando atras, so o dominio (sem perguntar) e NENHUMA
REM       das duas -- que cai no ESPACO ABERTO, e nao na Terra morta.
REM    8. A ESCOLHA, RACA POR RACA -- as MESMAS perguntas da familia 7 com as 24
REM       racas, e nao com um personagem so (foi uma amostra que deixou passar o
REM       defeito do relato). Com o natal DE PE ninguem e perguntado e o verb do
REM       refugio RECUSA escrever; com ele MORTO ha escolha, o padrao e a
REM       vizinhanca, escolher o dominio move o corpo pro dominio e voltar atras
REM       traz o corpo pro MESMO mundo de antes -- sempre com PisouEmChao.
REM    9. AS BORDAS, UMA A UMA: a cascata da fase 0 (Terra+Namek+Vegeta mortos,
REM       ZERO desabrigados em Icer); TODOS OS PLANETAS MORTOS (a carta inteira,
REM       e as 24 tem que acordar em chao de verdade); o LACO DO CADAVER (o
REM       ultimo recurso poe o corpo sobre a Terra morta e ele NAO desce, nem
REM       pelo tique nem pelo login); o corpo SEM BERCO e o natal fora da carta;
REM       e a BANDEIRA QUE NAO SERVE.
REM   10. A LISTA MORREU -- a injecao antiga, apontada pro outro lado: um planeta
REM       ficticio ("Hera") na FRENTE da carta. A regra DELETADA mudaria de
REM       resposta (Namek -> Hera) e a regra de hoje NAO move ninguem, raca por
REM       raca. As duas metades sao exigidas juntas.
REM
REM  DUAS LINHAS DE WARNING NO STDERR SAO ESPERADAS, e sao a prova funcionando: a
REM  familia 8 tenta destruir `Heaven` e `Hell` de verdade pra mostrar que o
REM  `ComecarDestruicao` recusa (nenhum dos dois e corpo celeste, entao o Kai e o
REM  Demon nunca ficam sem casa). Stderr com essas duas e nenhuma outra e limpo.
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
echo  ---- a prova do berco (10 familias, mundos adversarios) ----
echo  A bancada sai no console com o prefixo [bercoprova]. As tres irmas rodam
echo  juntas aqui: --diagberco (a regra), --bercovivo (a corrente) e
echo  --bercoprova (a prova). Os tres placares saem em sequencia.
echo.
"%GODOT%" --headless --path . --server --port 7961 --diagberco --bercovivo --bercoprova

echo.
echo  Encerrado.
pause
