@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- a FUSAO fotografada

REM ===========================================================================
REM  A FUSAO, FOTOGRAFADA  (--diagfotofusao)
REM
REM     ver-a-fusao.bat
REM
REM  A IRMA DESTA BANCADA E A `testar-fusao.bat`, que mede a fusao inteira em
REM  NUMERO -- 278 provas com os defeitos injetados, tudo verde.
REM
REM  **E ela ficaria verde com a fusao desenhada careca e de calcao.** Entre o
REM  `LookDeFusao` do servidor e o pixel ha o `PeerLook`, o `_fusaoDaZona` do
REM  `World`, a pilha de camadas do `CharacterVisual`, o `CabelosDeForma` e o
REM  shader do corpo. Este projeto ja catalogou esse cego cinco vezes (a memoria
REM  "a bancada mede INTENCAO"), e as fotos que o dono pediu sao exatamente a
REM  metade que so o olho fecha.
REM
REM  O ROTEIRO (16 tomadas, no `user://` -- os caminhos saem no console)
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
REM     C1 fusao-c1-ssj4-danca       a DANCA em SSJ4, de cabelo VERMELHO
REM     C2 fusao-c2-ssj4-potara      a POTARA em SSJ4, cabelo na COR NORMAL
REM        fusao-tira-ssj4-danca-e-potara.png  as duas coladas -- e a correcao
REM                                  que o dono pediu ("so a metamoro/danca muda
REM                                  a cor do cabelo no ssj4"); separadas, cada
REM                                  uma parece certa sozinha
REM
REM  ============ E O CABELO E MEDIDO **NO PIXEL DA FOTO** (C3) ============
REM  As provas C1/C2 leem o `TintaDoCabeloDeTeste` -- o uniform ESCRITO no
REM  shader. A memoria deste projeto tem um verbete inteiro pra esse cego, e ele
REM  ja custou caro: uma sessao assinou "o corpo branco" lendo um uniform e a
REM  foto mostrou 0,0% de branco. A familia C3 abre as duas imagens gravadas,
REM  ACHA sozinha a faixa do cabelo (o bloco mais denso de vermelho, no alto do
REM  boneco) e AMOSTRA a cor: a Danca sai vermelha e a Potara nao, com os dois
REM  hexadecimais impressos no log. O controle e a GRAMA ao lado da cabeca --
REM  ela tem que ser igual nas duas, senao o que mudou foi a luz e nao o cabelo.
REM
REM  ============ E O CEU FICA ABERTO, ALEM DE CRAVADO NO MEIO-DIA ============
REM  As primeiras fotos do cabelo sairam ilegiveis mesmo ao meio-dia: o que
REM  atravessava a tela era CHUVA DE SANGUE, o clima natural de Vegeta, com 1150
REM  riscos vermelhos por cima do assunto. Numa bancada que afirma "este cabelo
REM  esta VERMELHO e aquele nao", um clima vermelho e um confundidor. Hoje ela
REM  forca ceu aberto na zona e o DEVOLVE na limpeza. E ela espera a POEIRA da
REM  cratera baixar antes de fotografar (`PoeiraDeEstrago.VivosDeTeste == 0`) --
REM  com ela no ar, cabelo e cenario ficam a mesma cor.
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
REM  ============ E ELA **NAO** DESVIA A PASTA DE USUARIO, AO CONTRARIO DA IRMA ============
REM  A `testar-fusao.bat` troca o `APPDATA` antes de subir servidor, pra que as
REM  bancadas nao escrevam na pasta de saves do dono (leia o bloco de la -- uma
REM  rodada ja deixou tres contas de bancada e quatro arquivos de mundo dele).
REM  Aqui isso seria um tiro no pe: **as fotos SAO a entrega**, e elas saem no
REM  mesmo `user://` -- desviar a pasta esconderia o resultado do dono numa
REM  pasta temporaria. Esta bancada escreve na pasta de verdade de proposito, e
REM  os caminhos das fotos saem no console.
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
echo  ---- as 16 tomadas, com janela no SEGUNDO monitor ----
echo.
echo   Leva uns 60 s (duas cinematicas de fusao de 7 s cada e a do SSJ4, que
echo   espera a cena de forma acabar antes de fotografar). A janela fecha
echo   sozinha e o placar sai no console:
echo   "[fotofusao] ===== TUDO OK (16 tomadas) =====".
echo.
"%GODOT%" --path . --host --rede 7908 --diagfotofusao ^
          --position 1920,0 --resolution 1600x900 ^
          --raca Saiyan --conta bancada_foto_fusao --nome Olheiro

echo.
echo  Encerrado.
pause
