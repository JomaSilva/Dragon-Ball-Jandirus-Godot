@echo off
setlocal enabledelayedexpansion
title Dragon Ball Jandirus -- bancada dos CINCO SISTEMAS LIGADOS

REM ===========================================================================
REM  OS CINCO SISTEMAS QUE ESTAVAM ESCRITOS E SEM CHAMADOR  (--ligadosteste)
REM
REM     testar-ligados.bat
REM
REM  Cinco regras do `Core/` estavam corretas e sem NINGUEM que as chamasse:
REM  exp por evento (`Creditar`), marco de ascensao, ganho de voo, morte de
REM  velhice e genoma do filho. Ligar cada uma foi metade do trabalho; ESTA
REM  bancada e a outra metade -- a que REPROVA no dia em que a chamada sumir.
REM
REM  SAO 39 CONFERENCIAS, e nenhuma pergunta "o metodo foi chamado": todas
REM  atravessam o funil de producao (`TickDoVoo`, `TickDoNado`, `Treinar`,
REM  `EnvelhecerNaSala`, `BasicBlast`, `SolarFlare`, `ResolverBusterG3`) e
REM  cobram o MUNDO MUDAR. Cada familia carrega o seu contra-exemplo, porque
REM  "subiu" fica verde com a regra errada dentro:
REM
REM     voar sobe o BP        x  quem esta no chao nao sobe
REM     nadar sobe o BP       x  nadar em linha reta nao sobe
REM     BPBoost 5 da patamar  x  BPBoost 4 nao da
REM     o corpo de 75 morre   x  o de 25 atravessa o mesmo funil e vive
REM     a bola sobe a arvore  x  as arvores de raio e de debuff ficam em ZERO
REM     o filho herda dos 2   x  o neto nao colapsa em dois nomes
REM
REM  NAO PRECISA DE JANELA: o que ela mede sao numeros, e nao pixels. Ela roda
REM  no PRIMEIRO LOGIN (precisa de um corpo com dono: os dois funis de exp
REM  recusam NPC de proposito) e limpa os corpos que forjou no fim.
REM
REM  PORTA PROPRIA (7962): se aparecer "FALHOU ao abrir a porta", ha outra
REM  rodada viva -- feche-a.
REM
REM  O placar sai no console; codigo de saida != 0 nao existe aqui, entao LEIA
REM  a linha "===== FIM: N ok, M falha(s) =====".
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
echo     testar-ligados.bat
echo.
pause
exit /b 1

:temgodot
echo  Godot : %GODOT%
echo  Porta : 7962

where dotnet >nul 2>nul
if %errorlevel%==0 (
    echo  Compilando...
    dotnet build "Dragon ball Jandirus.csproj" -t:Rebuild -v q -nologo
    if errorlevel 1 (
        echo.
        echo  A compilacao FALHOU -- a bancada mediria a versao de ontem, que e
        echo  exatamente como um conserto "que nao faz nada" ja passou verde aqui.
        pause
        exit /b 1
    )
)

echo.
echo  ---- os cinco sistemas ligados (39 conferencias) ----
"%GODOT%" --headless --path . --host --rede 7962 --ligadosteste ^
          --raca Human --conta bancada_ligados --nome Ligador

echo.
echo  Encerrado.
pause
