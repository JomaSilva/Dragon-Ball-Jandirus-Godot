@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do SELO

REM ===========================================================================
REM  O SELO, O POTE, A DEAD ZONE E O SIGILO DE PODER  (--seloteste)
REM
REM     testar-selo.bat
REM
REM  Sao 108 provas em uns 10 segundos, SEM JANELA, e todas dentro de um
REM  servidor vivo -- o Mafuba que ela aperta e o mesmo que o jogador aperta.
REM
REM  DE ONDE VEIO ESTE LOTE (vale ler antes de mexer nele):
REM  Tres skills de cargo -- Mafuba, Open Dead Zone e Superior Seal -- saiam do
REM  `skills.json` com "verbos": [] porque o extrator PERDIA o corpo do
REM  `after_learn()` delas. Elas estao declaradas em `Modules/Magic/Sealing.dm`
REM  com o typepath na propria linha, e os typepaths moram 175 arquivos adiante
REM  (`Ranks/ordered/EarthRanks.dm`): quando o corpo era lido, a skill nao
REM  existia ainda e o corpo caia no chao CALADO.
REM
REM  O estrago em jogo era uma mentira com duas bocas: o painel do Eremita
REM  Tartaruga listava o Mafuba entre o que o cargo ENTREGA, e o recado de posse
REM  dizia "o cargo te entrega: ... Mafuba". Nao havia botao nenhum.
REM
REM  AS OITO FAMILIAS
REM     1) o NUCLEO    as tres regras do `TestEscape` como funcao pura: a razao
REM                    de fuga de 1,25, a corrosao `0.001/dur` e o piso de 0,25
REM                    do pote sumido -- inclusive a divisao por zero do
REM                    original, que aqui virou guarda.
REM     2) o MAFUBA    sem pote a vista o verb NAO sai (e assim no DM tambem);
REM                    com pote a fita nasce, viaja e sela; e os 90 de dano em
REM                    CADA membro sao cobrados NA HORA, nao na chegada.
REM     3) a FUGA      25% acima do selo e o corpo sai sozinho no tique, e volta
REM                    pra ZONA e pro PONTO de onde saiu.
REM     4) o POTE      quebrar o pote SOLTA quem esta dentro -- e a interacao
REM                    principal do Mafuba. Pote com gente dentro nao se carrega
REM                    (desvio DECLARADO, ver GameServer.Tech.PegarObra).
REM     5) a DEAD ZONE cobra 90,9% do Ki, nasce 5 tiles ao NORTE (nao a frente),
REM                    arrasta quem esta perto e sela SEM POTE -- so sai quem
REM                    passar 25% do BP de quem abriu.
REM     6) o DISCO     selo gravado, selo relido: deslogar nao e a chave mestra
REM                    da prisao. Save de antes do lote volta SOLTO.
REM     7) o SIGILO    Conceal_Power alterna e trava 5 s; Power_Control SO BAIXA
REM                    -- e com o powerMod baixado a tecla C entra no estagio
REM                    RETOMANDO, que ate hoje era INALCANCAVEL (nada no port
REM                    inteiro baixava o powerMod).
REM     8) o CENSO     as tres skills deixaram de ser "folha muda de nascenca":
REM                    duas respondem PRONTA e a terceira (Superior Seal) nomeia
REM                    o sistema que falta -- a magia, que este port nao tem.
REM     9) o PAINEL    a pergunta do dono ("o painel do Eremita Tartaruga ainda
REM                    anuncia o Mafuba?") respondida NOS BYTES que sairiam no
REM                    fio -- desmontados com os mesmos limites de string que o
REM                    cliente usa --, e nao na tabela que os gera. No meio dela
REM                    o efeito do Mafuba e ARRANCADO: o mesmo painel tem que
REM                    move-lo pro lado dos mudos, e devolve-lo quando o efeito
REM                    volta. E a metade que prova o "a MENOS QUE ele faca
REM                    alguma coisa".
REM    10) o EFEITO    os quatro verbos do lote disparados pelo CAMINHO DE
REM                    PRODUCAO -- coroacao (`Outorgar`) -> dadiva -> livro ->
REM                    botao --, e nao com a skill escrita a mao. Cada um com o
REM                    efeito dito por extenso e conferivel contra o DM:
REM                      Mafuba: 90 de vida em CADA membro solto na hora do
REM                        lancamento, e o alvo passa a viver dentro do pote;
REM                      Open Dead Zone: custa MaxKi/1.1, nasce 5 tiles ao
REM                        NORTE, e quem cai dentro e selado SEM POTE;
REM                      Conceal Power: o BP que o mundo LE vira 5;
REM                      Power Control: segurar em 40% faz o BP expresso valer
REM                        40% do que valia.
REM                    E cada um carrega a metade que o derruba: antes do cargo
REM                    o botao NAO existe, e perder o cargo o tira de novo. (As
REM                    duas do sigilo nao sao de cargo: vem do DEGRAU 5 do
REM                    Basic Ki Control, e a bancada prova o canal.)
REM
REM  O QUE ELA NAO MEDE: o extrator. Aquilo tem bancada propria --
REM  `testar-o-extrator.bat` (26 provas, 3 s, sem Godot), que monta uma arvore
REM  DM sintetica com o defeito dentro e LIGA O DEFEITO DE VOLTA pra provar que
REM  sabe ficar vermelha. O comando `skills` do pipeline tambem tem alarme
REM  proprio ("after_learn de caminho absoluto SEM DONO: N", saida nao-zero).
REM
REM  PORTA PROPRIA (7981): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     testar-selo.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7981

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    REM `-t:Rebuild` e OBRIGATORIO: o build incremental ja deu "compilacao com
    REM exito" sem trocar a DLL, e o Godot subiu com o binario de ontem. Uma
    REM bancada medindo a versao anterior e pior que bancada nenhuma.
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ---- o selo, o pote, a Dead Zone e o sigilo (108 provas) ----
echo.
echo   Ela roda no BOOT (nao precisa de login) e leva uns 10 s. O SERVIDOR
echo   CONTINUA DE PE depois dela -- e a convencao das bancadas de servidor.
echo   Leia o placar "[selo] ==== N OK, M FALHA ====" e feche com Ctrl+C.
echo.
"%GODOT%" --headless --path . --host --rede 7981 --seloteste

echo.
echo  Encerrado.
pause
