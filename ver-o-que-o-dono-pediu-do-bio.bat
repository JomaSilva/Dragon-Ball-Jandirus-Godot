@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- OS TRES PEDIDOS VISUAIS DO BIO-ANDROIDE

REM ===========================================================================
REM  OS OLHOS DA LARVA, O BRILHO DA CINEMATICA E A MORTE QUE VIRA SSJ2
REM  (--bioolhar + --diagolhar)
REM
REM     ver-o-que-o-dono-pediu-do-bio.bat
REM
REM  Tres dos quatro pedidos do dono nesta rodada sao de PIXEL, e nenhum deles se
REM  responde com um `bool`:
REM
REM    1) *"os bio androides o OLHO FICA VOANDO na forma de BARATA, faca com q os
REM       olhos SUMAM ao ficar nessa forma"* -- ele mandou a FOTO das duas pupilas
REM       flutuando ACIMA e FORA do casco da larva;
REM    4) *"vc n colocou a CINEMATICA DE TRANSFORMACAO dos bio androides... tinha
REM       um OVERLAY q fazia o CORPO BRILHAR"*;
REM    3) *"ao ter os requisitos... e MORRER, ele vai CANCELAR A MORTE e voltar a
REM       vida de forma INSTANTANEA so q agr no SSJ2"*.
REM
REM  ---------------------------------------------------------------------------
REM  POR QUE ELA NAO E A `ver-a-escada-do-bio.bat`
REM  ---------------------------------------------------------------------------
REM  Aquela tira UMA foto por degrau e compara os recortes com um piso de 3%% de
REM  pixels. Ela e boa no que faz e nao alcanca NENHUM dos tres:
REM
REM     * os olhos da larva sao ~16 px de tela. Num recorte de 76x96 eles nao
REM       chegam nem perto do piso de 3%%, e a pose da larva ja passa com folga
REM       porque o CORPO INTEIRO trocou de sprite. O defeito que o dono viu e
REM       invisivel pra aquela maquinaria -- e foi por isso que a auditoria
REM       anterior o mediu no NODE, que e "uniform escrito", nao "pixel desenhado";
REM     * a cinematica dura 28 s e aquele palco existe justamente pra NAO ter cena
REM       rodando (ele marca `EstreiaVista` no nascimento pra pular todas);
REM     * e a morte precisa de DOIS corpos no mesmo quadro -- a prova de que o
REM       requisito e um requisito esta em quem NAO volta.
REM
REM  ---------------------------------------------------------------------------
REM  COMO ELA MEDE: O MESMO QUADRO, COM E SEM A CAMADA
REM  ---------------------------------------------------------------------------
REM  Nenhuma das tres se responde comparando fotos de INSTANTES diferentes -- o
REM  mato se mexe, a luz do dia anda, passa um dos 148 cidadaos, e a coisa medida
REM  (16 px de olho) e menor que o ruido.
REM
REM  Entao ela INJETA O DEFEITO e fotografa de novo tres quadros depois. A
REM  diferenca entre as duas fotos e, por construcao, exatamente a camada que se
REM  ligou ou desligou -- e a conta e feita SO DENTRO DA CAIXA em que aquela
REM  camada desenha, entao um vizinho a quarenta pixels deixa de existir pra
REM  medida. Cada medida carrega o proprio controle de ruido.
REM
REM  E isso da os DOIS SENTIDOS de graca, que e o que separa "consertei" de
REM  "apaguei": na LARVA forcar os olhos tem que MUDAR pixels (o desenho chegaria
REM  ali) e a producao tem que ser IGUAL a versao apagada (zero pixel de olho na
REM  tela); num corpo HUMANOIDE e o contrario exato.
REM
REM  ---------------------------------------------------------------------------
REM  TRES CORPOS, E A DIFERENCA ENTRE DOIS DELES E **UM CAMPO**
REM  ---------------------------------------------------------------------------
REM     A  bio-androide COM DNA Saiyajin na fornada -- o protagonista dos tres;
REM     C  bio-androide SEM DNA, e IDENTICO ao A em tudo o mais (mesmo BP, mesmo
REM        degrau, mesma maestria de SSJ1, morto no mesmo tique pela mesma porta);
REM     B  um SAIYAJIN, que se transforma no MESMO instante -- o contra-exemplo da
REM        cinematica. Sem ele, "a cena do bio acende uma silhueta" nao diria que
REM        ela e DO BIO, diria so que o tocador acende uma em toda transformacao.
REM
REM  Nada e encurtado: os corpos nascem pelo `NascerNpc`, viram bio pelo
REM  `NascerBioAndroide` (com laboratorio e fornada de verdade), sobem degrau pelo
REM  `SubirDegrauDoBio` -- que e quem dispara a cena -- e morrem pelo
REM  `Combate.Morrer()` **sem** `ignorarSeguro`, ou seja passando pelo `NegarMorte`
REM  que e a porta unica da morte. O unico atalho e o RELOGIO.
REM
REM  ---------------------------------------------------------------------------
REM  AS IMAGENS (em %%APPDATA%%\Godot\app_userdata\Dragon ball Jandirus)
REM  ---------------------------------------------------------------------------
REM     TIRA-olhos-larva.png ......... **A FOTO DO PEDIDO 1.** Producao / defeito
REM                                    injetado / camada apagada. No do meio estao
REM                                    as duas pupilas do dono, flutuando na grama
REM                                    acima da barata.
REM     TIRA-olhos-humano.png ........ o contra-exemplo: no humano os olhos ESTAO la
REM     TIRA-olhos-imperfeito.png .... e voltam no degrau acima da larva
REM     TIRA-cena-bioandroide.png .... **A FOTO DO PEDIDO 4.** O meio da metamorfose
REM                                    com o `bioto2` acendendo o corpo inteiro, e o
REM                                    mesmo instante sem ele
REM     TIRA-cena-saiyajin.png ....... o contra-exemplo, no mesmo instante: nenhuma
REM                                    folha de luz sobre o corpo
REM     TIRA-morte-antes.png ......... **A FOTO DO PEDIDO 3**, os dois de pe
REM     TIRA-morte-depois.png ........ um de pe, o outro estirado no chao
REM     TIRA-morte-dez-segundos-depois.png .. e nada se desfez
REM     morte-*-QUADRO-DO-CORPO.png .. o sprite CRU de cada um, sem cenario e sem
REM                                    rotacao -- ele existe porque a foto de tela
REM                                    ja mentiu uma vez (ver o bloco do `case 6`
REM                                    em GameServer.BioOlhar.cs)
REM
REM  PRECISA DE JANELA. No headless o `GetImage` volta vazio e nao ha veredito --
REM  e aqui a foto E o teste. A bancada DIZ isso em vez de fechar com "0 falhas",
REM  que e o modo de falha mais perigoso de uma bancada visual.
REM
REM  `--horateste 0.5` crava MEIO-DIA: a hora do mundo e sorteada, e uma foto
REM  tirada a noite nao se le.
REM
REM  PORTA PROPRIA (7944): se aparecer "FALHOU ao abrir a porta", ha outra rodada
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
echo     ver-o-que-o-dono-pediu-do-bio.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7944  (um processo so, COM JANELA)

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
echo  ---- os tres pedidos visuais do bio-androide (COM JANELA, ~2 min) ----
REM A JANELA VAI PRO SEGUNDO MONITOR (--position 1930,20): o dono trabalha no
REM principal. Ajuste a origem se o seu arranjo de monitores for outro.
REM A resolucao e grande de proposito: com zoom 3 (o padrao, `Settings.Zoom`) o
REM terceiro corpo do elenco fica a ~576 px do centro da tela, e numa janela
REM pequena ele cai fora do quadro -- que e um modo de falha que a bancada da
REM escada ja registrou por escrito.
"%GODOT%" --path . --resolution 1900x1000 --position 1930,20 ^
          --host --rede 7944 --bioolhar --diagolhar --horateste 0.5 ^
          --raca Human --conta bancada_olhar --nome Olhador

echo.
echo  Leia o placar acima: "[olhar] ===== N ok, M falha(s) =====".
echo  As tiras estao em "%APPDATA%\Godot\app_userdata\Dragon ball Jandirus\TIRA-*.png".
pause
