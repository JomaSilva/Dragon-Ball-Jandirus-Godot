<#
  Gera o servidor dedicado pra VPS Linux (Debian), pronto pra subir:

    ServidorLinux\jandirus-servidor\                a pasta que vai pra VPS
    ServidorLinux\jandirus-servidor-linux.tar.gz    a mesma pasta num arquivo so

  O servidor e o PROPRIO JOGO rodando sem tela (o mesmo do servidor.bat): o binario Linux do Godot, o .pck com
  os dados (mapas, racas, skills) e a pasta data_* com as DLLs .NET do jogo e o runtime .NET inteiro -- a VPS nao
  precisa de .NET instalado. Exportar usa os modelos de exportacao do Godot 4.7.1 mono, os mesmos do .exe.

  Uso: dois cliques no exportar-servidor-linux.bat, ou
       powershell -File ServidorLinux\exportar-servidor-linux.ps1 [-Godot C:\caminho\Godot_..._console.exe]
#>
param([string]$Godot = $env:GODOT)

$ErrorActionPreference = 'Stop'
$pasta  = $PSScriptRoot
$raiz   = Split-Path $pasta -Parent
$pacote = Join-Path $pasta 'jandirus-servidor'
$tar    = Join-Path $pasta 'jandirus-servidor-linux.tar.gz'
$log    = Join-Path $pasta 'exportar.log'

function Falhar([string]$porque) { Write-Host ''; Write-Host "  $porque" -ForegroundColor Red; exit 1 }

# --- o Godot: o mesmo jeito de achar do servidor.bat ------------------------------------------------------
if (-not $Godot) { $Godot = 'E:\Users\Joao\Desktop\Godot_v4.7-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' }
if (-not (Test-Path $Godot)) {
    $Godot = Get-ChildItem (Join-Path $raiz '..\Godot_*'), (Join-Path $raiz '..\..\Godot_*') -Directory -ErrorAction SilentlyContinue |
             ForEach-Object { Get-ChildItem $_.FullName -Filter '*_console.exe' } | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Godot -or -not (Test-Path $Godot)) { Falhar 'Nao achei o Godot. Defina antes:  set GODOT=C:\caminho\Godot_v4.7.1-stable_mono_win64_console.exe' }

# --- os modelos de exportacao do Linux, da versao DESTE Godot ---------------------------------------------
if ((Split-Path $Godot -Leaf) -notmatch 'v(\d+\.\d+(?:\.\d+)?)-(\w+)_mono') { Falhar "Esse Godot nao e o mono (C#): $Godot" }
$modelos = Join-Path $env:APPDATA "Godot\export_templates\$($Matches[1]).$($Matches[2]).mono"
if (-not (Test-Path (Join-Path $modelos 'linux_release.x86_64'))) {
    Falhar "Faltam os modelos de exportacao do Linux em $modelos -- no editor: Editor > Gerenciar Modelos de Exportacao."
}

# --- nada de exportar com um Godot aberto DESTA pasta: o editor e a exportacao brigariam pela .godot ------
$vivos = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -like 'Godot*' -and $_.CommandLine -like "*$raiz*" })
if ($vivos.Count -gt 0) { Falhar "Ha um Godot aberto desta pasta (PID $($vivos.ProcessId -join ', ')). Feche-o antes de exportar." }

$versao = (Select-String -Path (Join-Path $raiz 'project.godot') -Pattern '^config/version="(.*)"').Matches.Groups[1].Value

Write-Host "  Godot  : $Godot"
Write-Host "  Versao : $versao"
Write-Host "  Pacote : $pacote"
Write-Host ''
Write-Host '  Exportando (compila o C# pra Linux e junta os dados -- uns minutos)...'

# O pacote anterior sai inteiro. (A pasta ServidorLinux tem um .gdignore: sem ele o Godot varreria o pacote
# anterior e o filtro "*.json" do preset enfiaria os .json do runtime .NET dele dentro do .pck novo.)
Remove-Item $pacote -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $pacote | Out-Null

$binario = Join-Path $pacote 'jandirus-servidor.x86_64'
# O LOG VAI PRA ARQUIVO, e espera-se SO o Godot (`WaitForExit`, e nao `-Wait`): o `-Wait` espera tambem os nos de
# build que o `dotnet publish` deixa vivos por minutos, e o `&` com stderr redirecionado faz do PowerShell 5 uma
# excecao a cada aviso do Godot.
$env:MSBUILDDISABLENODEREUSE = '1'
$godotVivo = Start-Process -FilePath $Godot -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.erros" `
    -ArgumentList "--headless --path `"$raiz`" --export-release `"Linux Servidor`" `"$binario`""
$null = $godotVivo.Handle   # (sem isto o PowerShell 5 perde o ExitCode de quem ja saiu)
$godotVivo.WaitForExit()
$codigo = $godotVivo.ExitCode
Get-Content "$log.erros" -ErrorAction SilentlyContinue | Add-Content $log
Remove-Item "$log.erros" -ErrorAction SilentlyContinue
$dados = Get-ChildItem $pacote -Directory -Filter 'data_*' | Select-Object -First 1
if ($codigo -ne 0 -or -not (Test-Path $binario) -or -not (Test-Path (Join-Path $pacote 'jandirus-servidor.pck')) -or -not $dados `
    -or -not (Test-Path (Join-Path $dados.FullName 'Dragon ball Jandirus.dll'))) {
    Select-String -Path $log -Pattern 'ERROR|error CS|Erro' | Select-Object -Last 15 | ForEach-Object { Write-Host "    $($_.Line)" }
    Falhar "A exportacao FALHOU (codigo $codigo). O log inteiro: $log"
}

# --- os scripts do Debian, com fim de linha LF: o bash nao aceita CRLF -------------------------------------
$utf8 = New-Object System.Text.UTF8Encoding($false)
foreach ($f in @(Get-ChildItem (Join-Path $pasta 'debian') -File) + @(Get-Item (Join-Path $pasta 'LEIA-ME.md'))) {
    $texto = [IO.File]::ReadAllText($f.FullName) -replace "`r`n", "`n"
    [IO.File]::WriteAllText((Join-Path $pacote $f.Name), $texto, $utf8)
}

# --- o lancador `server.dll`, pros paineis de hospedagem ".NET" (`dotnet server.dll`) -----------------------
# O fonte vai junto, em lancador/: painel que so sabe compilar acha o projeto ali.
$lancador = Join-Path $pasta 'lancador'
$ErrorActionPreference = 'Continue'
$saidaDoLancador = & dotnet publish (Join-Path $lancador 'Lancador.csproj') -c Release -o $pacote -nologo -v q 2>&1
$codigoDoLancador = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($codigoDoLancador -ne 0) {
    $saidaDoLancador | Select-Object -Last 10 | ForEach-Object { Write-Host "    $_" }
    Falhar 'O lancador (server.dll) nao compilou.'
}
Remove-Item (Join-Path $pacote 'server.pdb') -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $pacote 'lancador') | Out-Null
Copy-Item (Join-Path $lancador 'Lancador.csproj'), (Join-Path $lancador 'Program.cs') (Join-Path $pacote 'lancador')

# --- um arquivo so pra subir (o tar.exe vem no Windows desde o 10 1803) -----------------------------------
Remove-Item $tar -Force -ErrorAction SilentlyContinue
& tar.exe -czf $tar -C $pasta 'jandirus-servidor'
if ($LASTEXITCODE -ne 0) { Falhar "Nao consegui criar o $tar -- a pasta $pacote esta pronta, suba ela inteira." }

$mb = [Math]::Round((Get-Item $tar).Length / 1MB)
Write-Host ''
Write-Host "  PRONTO: servidor $versao pra Linux." -ForegroundColor Green
Write-Host "  $tar  ($mb MB)"
Write-Host ''
Write-Host '  Os jogadores precisam do jogo desta MESMA versao (exportado desta mesma pasta).'
Write-Host ''
Write-Host '  Pra subir e instalar (troque usuario@ip pelo da sua VPS):'
Write-Host "    scp `"$tar`" usuario@ip:~"
Write-Host '    ssh usuario@ip'
Write-Host '    tar xzf jandirus-servidor-linux.tar.gz && cd jandirus-servidor && sudo bash instalar.sh'
Write-Host ''
Write-Host '  Num painel de hospedagem ".NET": suba a pasta jandirus-servidor e use App Build Name = server.dll (ver LEIA-ME.md).'
