[CmdletBinding()]
param(
    [switch]$Headless,
    [switch]$KeepEngine,
    [switch]$VerboseGodot
)

$ErrorActionPreference = 'Stop'
$demoRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $demoRoot '..\..')).Path
$runtimeRoot = Join-Path $demoRoot '.runtime'
$apiProject = Join-Path $repositoryRoot 'src\API\API.csproj'
$apiUrl = 'http://127.0.0.1:5271'
$startedEngine = $null

New-Item -ItemType Directory -Force -Path `
    (Join-Path $runtimeRoot 'runs'), `
    (Join-Path $runtimeRoot 'content'), `
    (Join-Path $runtimeRoot 'telemetry') | Out-Null

try {
    try {
        Invoke-RestMethod -Uri "$apiUrl/api/v1/health/ready" -TimeoutSec 1 | Out-Null
        Write-Host 'HeroScript já está disponível em 127.0.0.1:5271.' -ForegroundColor Green
    }
    catch {
        $apiEnvironment = @{
            ASPNETCORE_URLS = $apiUrl
            Persistence__RunStatePath = (Join-Path $runtimeRoot 'runs')
            Persistence__ContentStorePath = (Join-Path $runtimeRoot 'content')
            Persistence__OperationalTelemetryPath = (Join-Path $runtimeRoot 'telemetry')
        }
        foreach ($entry in $apiEnvironment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
        }
        $startedEngine = Start-Process -FilePath 'dotnet' `
            -ArgumentList @('run', '--project', $apiProject, '--no-launch-profile') `
            -WorkingDirectory $repositoryRoot -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $runtimeRoot 'engine.log') `
            -RedirectStandardError (Join-Path $runtimeRoot 'engine-error.log')
        $ready = $false
        for ($attempt = 0; $attempt -lt 80; $attempt++) {
            Start-Sleep -Milliseconds 250
            try {
                Invoke-RestMethod -Uri "$apiUrl/api/v1/health/ready" -TimeoutSec 1 | Out-Null
                $ready = $true
                break
            }
            catch { }
        }
        if (-not $ready) {
            throw "A HeroScript não iniciou. Consulte $runtimeRoot\engine-error.log."
        }
        Write-Host 'HeroScript iniciada com dados isolados da demo.' -ForegroundColor Green
    }

    if ($Headless) {
        $godotArguments = @('--headless')
        if ($VerboseGodot) { $godotArguments += '--verbose' }
        $godotArguments += @('--path', $demoRoot, '--', '--smoke')
        & godot @godotArguments
        if ($LASTEXITCODE -ne 0) { throw 'O smoke test da demo falhou.' }
    }
    else {
        & godot --path $demoRoot
    }
}
finally {
    if ($null -ne $startedEngine -and -not $KeepEngine -and -not $startedEngine.HasExited) {
        Stop-Process -Id $startedEngine.Id
    }
}
