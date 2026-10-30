param(
    [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$')][string]$Version,
    [string]$Archive,
    [ValidatePattern('^[a-fA-F0-9]{64}$')][string]$Sha256,
    [ValidatePattern('^\d+$')][string]$RunId
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$manifestPath = Join-Path $root 'heroscript.json'
$originalHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
$config = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($config.projectId -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]*$') { throw 'projectId invalido.' }
$destination = Join-Path $root 'heroscript'
if (-not (Test-Path -LiteralPath $destination -PathType Container)) { throw 'Runtime ausente. Use Install-HeroScript.ps1 para a primeira instalacao.' }
$work = Join-Path $root '.runtime'
foreach ($path in @($destination,$work)) {
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Pastas de runtime com redirecionamento nao sao aceitas.' }
}
New-Item -ItemType Directory -Force -Path $work | Out-Null
$updateLock = [IO.File]::Open((Join-Path $work 'update.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
$gameLock = $null
$oldMoved = $false
$newMoved = $false
$committed = $false
$stage = Join-Path $work ('update-' + [Guid]::NewGuid().ToString('N'))
$backup = Join-Path $stage 'previous-runtime'
try {
    $dataRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) $config.projectId
    New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null
    try { $gameLock = [IO.File]::Open((Join-Path $dataRoot 'runtime.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw 'O jogo esta aberto. Feche o inicializador antes de atualizar.' }
    $active = @(Get-CimInstance Win32_Process -Filter "Name='API.exe'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase) })
    if ($active.Count) { throw 'O runtime esta em uso. Nenhum processo foi encerrado.' }
    New-Item -ItemType Directory -Path $stage | Out-Null
    $fileName = "heroscript-runtime-v$Version-win-x64.zip"
    $repository = $config.runtimePackage.repository
    $source = [ordered]@{ repository=$repository; fileName=$fileName }
    $samePinnedVersion = $Version -ceq $config.runtimeVersion -and -not $RunId
    if (-not $Archive) {
        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Use GitHub CLI autenticada ou forneca -Archive e seu checksum.' }
        if ($RunId -or ($samePinnedVersion -and $config.runtimePackage.runId)) {
            if (-not $RunId) { $RunId = $config.runtimePackage.runId }
            & gh run download $RunId --repo $repository --name runtime-win-x64 --dir $stage
            $source.runId = $RunId
            $source.artifact = 'runtime-win-x64'
        } else {
            & gh release download "v$Version" --repo $repository --pattern $fileName --pattern ($fileName -replace '\.zip$', '.sha256') --dir $stage
            $source.releaseTag = "v$Version"
        }
        if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel baixar a versao especificada. A instalacao atual foi preservada.' }
        $Archive = Join-Path $stage $fileName
    } else {
        # Retain a reproducible remote source for later installs; no local absolute path is stored.
        if ($RunId) { $source.runId=$RunId; $source.artifact='runtime-win-x64' }
        elseif ($samePinnedVersion -and $config.runtimePackage.runId) { $source.runId=$config.runtimePackage.runId; $source.artifact=$config.runtimePackage.artifact }
        else { $source.releaseTag="v$Version" }
    }
    $Archive = (Resolve-Path -LiteralPath $Archive).Path
    if (-not $Sha256 -and $samePinnedVersion) { $Sha256 = $config.runtimePackage.sha256 }
    if (-not $Sha256) {
        $checksumPath = [IO.Path]::ChangeExtension($Archive, '.sha256')
        $line = @(Get-Content -LiteralPath $checksumPath | Where-Object { $_ -match ('^([a-fA-F0-9]{64})\s+\*?' + [regex]::Escape($fileName) + '$') })
        if ($line.Count -ne 1) { throw 'Checksum ausente ou ambiguo para este pacote.' }
        $Sha256 = ($line[0] -split '\s+')[0]
    }
    if ($Sha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash -ine $Sha256) { throw 'Checksum incorreto. Nada foi substituido.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -match '(^[/\\]|:|(^|[/\\])\.\.([/\\]|$))' -or (($entry.ExternalAttributes -shr 16) -band 61440) -eq 40960) { throw 'Caminho inseguro dentro do ZIP.' }
        }
    } finally { $zip.Dispose() }
    $expanded = Join-Path $stage 'expanded'
    Expand-Archive -LiteralPath $Archive -DestinationPath $expanded
    $incoming = Join-Path $expanded 'heroscript'
    $metadata = Get-Content -LiteralPath (Join-Path $incoming 'runtime-manifest.json') -Raw | ConvertFrom-Json
    if ($metadata.version -cne $Version -or $metadata.platform -ne 'win-x64') { throw 'Versao ou plataforma do pacote incorreta.' }
    if (-not (Test-Path -LiteralPath (Join-Path $incoming 'API.exe') -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $expanded 'Start-HeroScript.ps1') -PathType Leaf)) { throw 'Pacote incompleto.' }
    Copy-Item -LiteralPath (Join-Path $expanded 'Start-HeroScript.ps1') -Destination (Join-Path $incoming 'Start-HeroScript.ps1')
    if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -cne $originalHash) { throw 'O manifesto mudou durante a atualizacao. Tente novamente.' }
    $source.sha256 = $Sha256.ToLowerInvariant()
    $config.runtimeVersion = $Version
    $config.runtimePackage = [pscustomobject]$source
    $pendingManifest = Join-Path $stage 'next-manifest.json'
    $config | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $pendingManifest -Encoding UTF8
    Move-Item -LiteralPath $destination -Destination $backup
    $oldMoved = $true
    Move-Item -LiteralPath $incoming -Destination $destination
    $newMoved = $true
    [IO.File]::Replace($pendingManifest, $manifestPath, (Join-Path $stage 'previous-manifest.json'))
    $committed = $true
    Write-Host "HeroScript substituido pela versao $Version. Conteudo, interface e saves preservados."
    Write-Host "Instalacao anterior: $backup"
} catch {
    if ($oldMoved -and -not $committed) {
        if ($newMoved) { Move-Item -LiteralPath $destination -Destination (Join-Path $stage 'failed-runtime') }
        Move-Item -LiteralPath $backup -Destination $destination
    }
    throw
} finally {
    if ($gameLock) { $gameLock.Dispose() }
    $updateLock.Dispose()
}
