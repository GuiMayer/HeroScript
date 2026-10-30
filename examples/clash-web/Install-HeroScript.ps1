param([string]$Archive)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$config = Get-Content -LiteralPath (Join-Path $root 'heroscript.json') -Raw | ConvertFrom-Json
$package = $config.runtimePackage
$stage = Join-Path $root ('.runtime/install-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
if (-not $Archive) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Instale a GitHub CLI para obter o artefato, ou use -Archive com o ZIP oficial. Nenhum SDK .NET e necessario.' }
    if ($package.releaseTag) {
        & gh release download $package.releaseTag --repo $package.repository --pattern $package.fileName --dir $stage
    } else {
        & gh run download $package.runId --repo $package.repository --name $package.artifact --dir $stage
    }
    if ($LASTEXITCODE -ne 0) { throw 'Falha baixando o runtime. Use gh auth login ou forneca -Archive.' }
    $Archive = Join-Path $stage $package.fileName
}
$Archive = (Resolve-Path -LiteralPath $Archive).Path
if ((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $package.sha256) { throw 'Checksum do pacote incorreto. Instalacao recusada.' }
$expanded = Join-Path $stage 'expanded'
Expand-Archive -LiteralPath $Archive -DestinationPath $expanded
$runtime = Join-Path $expanded 'heroscript'
$metadata = Get-Content -LiteralPath (Join-Path $runtime 'runtime-manifest.json') -Raw | ConvertFrom-Json
if ($metadata.version -cne $config.runtimeVersion -or $metadata.platform -ne 'win-x64') { throw 'Runtime incompativel com heroscript.json.' }
if (-not (Test-Path -LiteralPath (Join-Path $runtime 'API.exe'))) { throw 'Pacote incompleto.' }
$destination = Join-Path $root 'heroscript'
if (Test-Path -LiteralPath $destination) { throw 'Runtime ja instalado. Para trocar, feche a API e mova a pasta heroscript atual para um backup antes de instalar.' }
# The published launcher is copied intact; authored content and the project manifest are never overwritten.
Copy-Item -LiteralPath (Join-Path $expanded 'Start-HeroScript.ps1') -Destination (Join-Path $runtime 'Start-HeroScript.ps1')
Move-Item -LiteralPath $runtime -Destination $destination
Write-Host "HeroScript $($metadata.version) instalado. Conteudo do jogo preservado."
