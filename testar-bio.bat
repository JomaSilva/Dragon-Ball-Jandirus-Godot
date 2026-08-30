@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada do ANDROIDE e do BIO-ANDROIDE

REM ===========================================================================
REM  O ANDROIDE E O BIO-ANDROIDE, DE PONTA A PONTA  (--bioteste)
REM
REM     testar-bio.bat
REM
REM  Ela responde, em execucao e sem ninguem por perto, a pergunta que o dono
REM  fez: "a criacao de androide e bio-androide ja esta 100%?".
REM
REM  Sao 183 provas em ~45 segundos, em DEZESSEIS secoes, e cada uma dirige os
REM  VERBOS que o cliente usa (a tecla E da tecnologia e o canal de habilidade)
REM  -- nunca as funcoes por dentro. A diferenca nao e estilo: chamar a funcao
REM  prova que o metodo existe; apertar o verbo prova que um JOGADOR chega nele.
REM
REM  E METADE DELAS E CONTRA-EXEMPLO. Uma escada aberta pra todo mundo passaria
REM  por qualquer lista de "transformou, transformou, transformou": cada degrau
REM  e cobrado nos DOIS sentidos, e no fim a bancada injeta em si mesma os NOVE
REM  defeitos que ela existe pra pegar e exige ficar vermelha em todos.
REM
REM    1) A PORTA         mainframe -> laboratorio. Recusa por tecnologia (70) e
REM                       por raca (a maquina e humana), e aceita o humano.
REM    2) DNA E NASCIMENTO  a agulha so pega NOCAUTEADO; a amostra guarda raca,
REM                       nome, assinatura, BP e TECNICAS; a fornada le o BP de
REM                       HOJE do doador online; o tanque de dono OFFLINE espera;
REM                       e entao a CRIATURA NASCE -- com metade do BP do doador
REM                       mais forte, careca, sem genoma, com as tecnicas dos
REM                       doadores, com Zenkai e com o Super Saiyajin do DNA.
REM    3) A LARVA         1% do proprio poder (e nem a raiva fura a carapaca),
REM                       nao absorve, e no prazo vira IMPERFEITA.
REM    4) A ESCADA        absorver EVOLUI: 1 androide ou 10 jogadores (NPC vale
REM                       meio) -> SEMI-PERFEITO (BP x2) -> PERFEITO (BP x4).
REM    5) AS FORMAS       a SUPER PERFEITA (8x) abre com a forma perfeita, NAO
REM                       pede DNA Saiyajin, e nao convive com Super Saiyajin --
REM                       nos DOIS sentidos (no original o SSJ2 a atropela).
REM    6) O SSJ2          nem com furia extrema: no bio ele so vem MORRENDO.
REM    7) O ANDROIDE      energia infinita (folego, fome e Ki) e a postura de
REM                       coletores abertos, que engole ataque de ki inteiro.
REM    8) O LABORATORIO   destrui-lo CANCELA a gestacao.
REM    9) O CRIVO         de quem NAO se colhe: o CIDADAO do povoamento, o
REM                       BONECO que sobra no transe e o REFLEXO DA MENTE --
REM                       tres corpos que diferem de um jogador valido em UM
REM                       campo cada, mais o controle que colhe.
REM   10) O BIO SEM DNA   a cadeia inteira de novo, com um doador vazio, e a
REM                       escada por JOGADORES: nao sobe no NONO, sobe no DECIMO.
REM   11) A HERANCA       a tecnica que veio da PESSOA (Solar Flare) e a
REM                       habilidade que veio da RACA (Fusion- Namek Style) --
REM                       e o bio sem DNA que nao tem nenhuma das duas.
REM   12) O ZENKAI        o BP subindo de verdade pelo funil da derrota; o bio
REM                       sem DNA tambem tem (vem da RACA); o humano nao tem.
REM   13) O SUPER SAIYAJIN  com DNA sobe, sem DNA a linha nem abre. E o SSJ2 (o
REM                       que o dono chama de "super perfeito"): a MESMA morte,
REM                       mudando UM campo, mata ou desperta.
REM   14) O FOLEGO NO VACUO  TRES bios nascidos no mesmo minuto, pela mesma
REM                       porta, diferindo so no sangue do doador: um por raca
REM                       que o dono nomeou -- o de DNA meio-Majin (que chega
REM                       pelo PAI do doador) e o de DNA Frost Demon (que chega
REM                       pela RACA dele) nao sufocam no espaco; o de DNA humano
REM                       sufoca ao lado dos dois. As duas racas entram por
REM                       METADES DIFERENTES do `Race == X || Parent_Race == X`,
REM                       e uma familia com so uma delas ficaria verde com a
REM                       outra metade apagada. E o traje continua salvando o
REM                       terceiro -- o folego novo entrou num `||`, nao no
REM                       lugar dos outros abrigos.
REM   15) OS NUMEROS     um por um contra o DM (0,5 / 4 / 10 / 0,5 / 1 / 2x /
REM                       4x / 8x / 1,35x / 1,5-2-3-4x / 6%% / 2x / 4%% / 70 /
REM                       1M / 2M / 30 dias / 1%% da larva).
REM   16) A INJECAO      a bancada se cobra: nove defeitos postos a mao, e cada
REM                       afirmacao TEM que virar vermelha.
REM
REM  A IRMA DELA E A `ver-a-escada-do-bio.bat`, e as duas se precisam: esta MEDE
REM  e nao olha a tela uma vez; aquela FOTOGRAFA um retrato por degrau. Esta
REM  aqui passaria verde inteira num servidor que subisse a escada sem que um
REM  pixel mudasse na tela de quem esta olhando.
REM
REM  RODA NO HEADLESS -- nao ha foto aqui, so afirmacao. Ela usa CORPOS
REM  FORJADOS pro que transforma (virar bio-androide APAGA a persona: nome,
REM  raca, genoma, classe e livro de skills) e devolve tudo no fim; o unico
REM  emprestimo que o seu personagem faz e o SANGUE, e ele volta na mesma
REM  funcao.
REM
REM  PORTA PROPRIA (7956): se aparecer "FALHOU ao abrir a porta", ha outra
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
echo     testar-bio.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7956

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
echo  ---- o androide e o bio-androide, de ponta a ponta ----
"%GODOT%" --path . --headless --host --rede 7956 --bioteste ^
          --raca Human --conta bancada_bio --nome QuemViraBicho

echo.
echo  Leia o placar acima: "BIO: N ok, M falha(s)".
pause
