[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$ApiPort = 5279,
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$server = $null
$serverJob = $null
$serverAttached = $false
try {
    $repo = $PSScriptRoot
    $editor = Join-Path $repo 'tools\ContentEditor'
    Get-Command node.exe -ErrorAction Stop | Out-Null
    $npm = (Get-Command npm.cmd -ErrorAction Stop).Source
    $dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source

    if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains $ApiPort) {
        throw "A porta $ApiPort esta em uso. Feche a outra instancia ou execute com -ApiPort."
    }

    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $repoHash = [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($repo.ToUpperInvariant()))).Replace('-', '').Substring(0, 16)
        $lock = [BitConverter]::ToString($hasher.ComputeHash([IO.File]::ReadAllBytes((Join-Path $editor 'package-lock.json'))))
    } finally {
        $hasher.Dispose()
    }
    $runtime = Join-Path $env:LOCALAPPDATA "HeroScript\editor\$repoHash"
    $build = Join-Path $runtime 'api'
    New-Item -ItemType Directory -Path $runtime -Force | Out-Null

    Write-Host '[1/3] Preparando a interface...'
    Push-Location -LiteralPath $editor
    try {
        $marker = 'node_modules\.editor-launcher-lock'
        $vite = 'node_modules\vite\bin\vite.js'
        if (!(Test-Path -LiteralPath $marker) -or !(Test-Path -LiteralPath $vite) -or
            (Get-Content -LiteralPath $marker -Raw).Trim() -ne $lock) {
            & $npm ci --no-audit --no-fund
            if ($LASTEXITCODE -ne 0) { throw 'Falha ao instalar as dependencias do editor.' }
            Set-Content -LiteralPath $marker -Value $lock -Encoding ascii
        }
        & $npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o editor.' }
    } finally {
        Pop-Location
    }

    Write-Host '[2/3] Preparando a engine...'
    Push-Location -LiteralPath $repo
    try {
        & $dotnet build (Join-Path $repo 'src\API\API.csproj') --output $build --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar a engine.' }
    } finally {
        Pop-Location
    }

    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $keyBytes = New-Object byte[] 24
        $random.GetBytes($keyBytes)
        $key = [Convert]::ToBase64String($keyBytes)
        $launchCode = $null
        if (!$NoBrowser) {
            $launchBytes = New-Object byte[] 32
            $random.GetBytes($launchBytes)
            $launchCode = [BitConverter]::ToString($launchBytes).Replace('-', '')
        }
    } finally { $random.Dispose() }
    $origin = "http://127.0.0.1:$ApiPort"
    $editorUrl = "$origin/editor/"
    $stdout = Join-Path $runtime "api-$ApiPort.log"
    $stderr = Join-Path $runtime "api-$ApiPort.error.log"

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $origin
    $env:Mods__PackageRoots__0 = Join-Path $repo 'data\configs'
    $env:Content__StartupSettings__0 = 'uno-clash'
    $env:Content__StartupSettings__1 = 'circular-table'
    $env:Persistence__ContentStorePath = Join-Path $runtime 'content'
    $env:Persistence__RunStatePath = Join-Path $runtime 'runs'
    $env:Persistence__OperationalTelemetryPath = Join-Path $runtime 'telemetry'
    $env:Admin__Enabled = 'true'
    $env:Admin__ApiKey = $key
    $env:ToolAccess__Profile = 'dev_modder'
    $env:Authoring__ApplyEnabled = 'true'
    $env:Authoring__EditorPath = Join-Path $editor 'dist'
    $env:Authoring__EditorLaunchCode = $launchCode
    [Environment]::SetEnvironmentVariable('Authoring__WritablePackages__uno-clash', 'heroscript.uno-clash')
    [Environment]::SetEnvironmentVariable('Authoring__WritablePackages__circular-table', 'heroscript.circular-table')

    Write-Host '[3/3] Iniciando a engine local...'
    if (-not ('EditorProcessJob' -as [type])) {
        Add-Type -Path (Join-Path $editor 'EditorProcessJob.cs')
    }
    $serverJob = [EditorProcessJob]::Create()
    $dll = Join-Path $build 'API.dll'
    $server = Start-Process -FilePath $dotnet -ArgumentList ('"' + $dll + '" --urls ' + $origin) `
        -WorkingDirectory $repo -NoNewWindow -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    [EditorProcessJob]::Attach($serverJob, $server)
    $serverAttached = $true
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while ($true) {
        if ($server.HasExited) { throw "A engine encerrou durante a inicializacao. Consulte $stdout e $stderr." }
        try {
            $catalog = Invoke-RestMethod -Uri "$origin/api/v1/admin/authoring/settings" `
                -Headers @{ 'X-Admin-Key' = $key } -TimeoutSec 2
            if (@($catalog.items | Where-Object writablePackageId).Count -ge 2) { break }
        } catch {
            if ([DateTime]::UtcNow -ge $deadline) { throw "A engine nao respondeu. Consulte $stdout e $stderr." }
        }
        if ([DateTime]::UtcNow -ge $deadline) { throw "Os settings editaveis nao foram encontrados. Consulte $stdout." }
        Start-Sleep -Milliseconds 300
    }

    Write-Host ''
    Write-Host "Editor: $editorUrl" -ForegroundColor Green
    Write-Host "Chave administrativa desta sessao: $key" -ForegroundColor Cyan
    Write-Host 'Use a chave se precisar reconectar. Feche esta janela ou pressione Ctrl+C para encerrar.'
    if (!$NoBrowser) { Start-Process ("${editorUrl}#launch=$launchCode") }
    Wait-Process -Id $server.Id
    $server.Refresh()
    if ($server.ExitCode -ne 0) { throw "A engine encerrou com erro. Consulte $stdout e $stderr." }
} catch {
    Write-Host "[ERRO] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    if ($null -ne $serverJob) { $serverJob.Dispose() }
    if (!$serverAttached -and $null -ne $server -and !$server.HasExited) {
        try { $server.Kill() } catch { if (!$server.HasExited) { throw } }
    }
}
