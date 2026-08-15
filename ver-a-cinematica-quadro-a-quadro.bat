@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- A CINEMATICA QUADRO A QUADRO

REM ===========================================================================
REM  O CORPO SO TROCA NO FIM -- FILMADO, NAO FOTOGRAFADO  (--biofilme + --diagfilme)
REM
REM     ver-a-cinematica-quadro-a-quadro.bat
REM
REM  O dono, com foto: *"o bio androide ta MUDANDO O CORPO ANTES DA CINEMATICA
REM  ACABAR ai ta ficando BUGADO como pode ver"* -- na imagem dele o corpo aparece
REM  MEIO TROCADO, dois desenhos de tamanhos diferentes empilhados. E ele ja tinha
REM  cobrado o mesmo FORMATO antes: *"tem transformacao q estao criando a CRATERA
REM  NO MEIO da cinematica (deveria ser sempre no FINAL)"*.
REM
REM  ---------------------------------------------------------------------------
REM  POR QUE UMA FOTO NAO RESPONDE ISSO
REM  ---------------------------------------------------------------------------
REM  "Antes do fim" e uma afirmacao sobre ORDEM, e ordem nao cabe num quadro. As
REM  tres bancadas de bio que ja existiam tiram UMA foto por estado, e por isso
REM  nenhuma delas jamais poderia ter pego este defeito:
REM
REM     testar-bio.bat ............... 159 provas, nao olha a tela uma vez;
REM     ver-a-escada-do-bio.bat ...... um retrato por degrau, e o palco dela existe
REM                                    justamente pra NAO ter cena rodando;
REM     ver-o-que-o-dono-pediu-do-bio.bat .. UM instante no meio da metamorfose, e a
REM                                    pergunta dela e sobre uma CAMADA acender.
REM
REM  Aqui a unidade e o FILME: ~30 amostras por cena, uma por segundo, do segundo
REM  zero ao fim. O veredito nao le nenhuma isoladamente -- ele acha o INSTANTE DA
REM  TROCA na sequencia e cobra tres coisas dele: que caia NA VIRADA (a conta e
REM  contra o beat `Efeito.Assumir` lido do Core, nunca contra um numero digitado),
REM  que seja UMA troca so (duas sao o "meio trocado" na forma mais crua) e que o
REM  quadro do corpo novo NAO tenha mais a silhueta de luz por cima -- que e a forma
REM  exata da foto do dono, porque a silhueta e dimensionada pro corpo VELHO.
REM
REM  ---------------------------------------------------------------------------
REM  QUATRO CORPOS, E CADA UM RESPONDE UMA PERGUNTA
REM  ---------------------------------------------------------------------------
REM     F  o BIO que roda a metamorfose de 28,0 s inteira -- o filme principal;
REM     M  um SAIYAJIN que vira OOZARU. Ele prova que a regra e GENERICA e nao um
REM        `if` de bio: o Oozaru troca o corpo por OUTRO caminho de codigo
REM        (`FormaDef.Corpo`, que so o cliente le) e mesmo assim tem que trocar no
REM        mesmo beat;
REM     K  um BIO identico ao F que leva NOCAUTE no meio da propria cena -- a
REM        pergunta do dono: que corpo ficou?
REM     D  um BIO identico ao F que roda a cena com o DEFEITO INJETADO no cliente
REM        (`World.VestirNaHoraDeTeste`, letra por letra o codigo de antes do
REM        conserto). As linhas do veredito TEM que ficar vermelhas nele -- uma
REM        bancada que nao sabe reprovar nao aprova nada.
REM
REM  ---------------------------------------------------------------------------
REM  AS IMAGENS (em %APPDATA%\Godot\app_userdata\Dragon ball Jandirus)
REM  ---------------------------------------------------------------------------
REM     TIRA-filme-bio.png ....... **A TIRA DO PEDIDO.** A cena inteira, um quadro
REM                                por segundo, da esquerda pra direita. O quadro da
REM                                troca vem com uma faixa BRANCA embaixo.
REM     TIRA-troca-bio.png ....... so os tres quadros do instante: o anterior (corpo
REM                                VELHO), o da troca e o seguinte (corpo NOVO)
REM     TIRA-filme-oozaru.png .... o mesmo, na outra raca
REM     TIRA-troca-oozaru.png
REM     TIRA-filme-nocaute.png ... a cena que leva o golpe no meio
REM     nocaute-*.png ............ o corpo do K no instante do nocaute e depois
REM     TIRA-filme-defeito.png ... **a mesma cena com o defeito injetado.** E nesta
REM                                que o corpo troca no quadro 0 com a silhueta acesa
REM                                por cima -- a foto do dono, reproduzida
REM
REM  PRECISA DE JANELA. No headless o `GetImage` volta vazio e nao ha veredito
REM  visual -- e aqui a foto E o teste. A bancada DIZ isso em vez de fechar com
REM  "0 falhas", que e o modo de falha mais perigoso de uma bancada visual.
REM
REM  `--horateste 0.5` crava MEIO-DIA: a hora do mundo e sorteada, e um filme
REM  gravado a noite nao se le.
REM
REM  PORTA PROPRIA (7936): se aparecer "FALHOU ao abrir a porta", ha outra rodada
REM  viva -- feche-a.
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
echo     ver-a-cinematica-quadro-a-quadro.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7936  (um processo so, COM JANELA)

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    REM `-t:Rebuild` e OBRIGATORIO: o build incremental ja deu "compilacao com
    REM exito" sem trocar a DLL, e o Godot subiu com o binario de ontem.
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada filmaria a versao de ontem.
        pause
        exit /b 1
    )
)

echo.
echo  ---- a cinematica quadro a quadro (COM JANELA, ~4 min) ----
REM A JANELA VAI PRO SEGUNDO MONITOR (--position 1930,20): o dono trabalha no
REM principal. Ajuste a origem se o seu arranjo de monitores for outro.
REM A resolucao e grande de proposito: com zoom 3 (o padrao) o corpo mais distante
REM do elenco fica a ~480 px do centro, e numa janela pequena ele cai fora do
REM quadro -- que e um modo de falha que a bancada da escada ja registrou por
REM escrito ("o palco esta FORA DA TELA", quatro poses perdidas numa rodada).
"%GODOT%" --path . --resolution 1900x1000 --position 1930,20 ^
          --host --rede 7936 --biofilme --diagfilme --horateste 0.5 ^
          --raca Human --conta bancada_filme --nome Filmador

echo.
echo  Leia o placar acima: "[filme] ===== N ok, M falha(s) =====".
echo  As tiras estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus\TIRA-*.png".
pause
