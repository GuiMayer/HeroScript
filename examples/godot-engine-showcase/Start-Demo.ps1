[CmdletBinding()]
param(
    [switch]$Headless,
    [switch]$KeepEngine,
    [switch]$VerboseGodot,
    [switch]$UiSmoke,
    [ValidateRange(1024, 65535)][int]$Port = 5271
)

$ErrorActionPreference = 'Stop'
$demoRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $demoRoot '..\..')).Path
$runtimeRoot = Join-Path $demoRoot '.runtime'
if ($Port -ne 5271) { $runtimeRoot = Join-Path $runtimeRoot "qa-$Port" }
$apiProject = Join-Path $repositoryRoot 'src\API\API.csproj'
$apiUrl = "http://127.0.0.1:$Port"
$startedEngine = $null

New-Item -ItemType Directory -Force -Path `
    (Join-Path $runtimeRoot 'runs'), `
    (Join-Path $runtimeRoot 'content'), `
    (Join-Path $runtimeRoot 'telemetry') | Out-Null

try {
    try {
        Invoke-RestMethod -Uri "$apiUrl/api/v1/health/ready" -TimeoutSec 1 | Out-Null
        Write-Host "HeroScript online: $apiUrl" -ForegroundColor Green
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
            -ArgumentList @('run', '--configuration', 'Release', '--project', $apiProject, '--no-launch-profile') `
            -WorkingDirectory $repositoryRoot -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $runtimeRoot 'engine.log') `
            -RedirectStandardError (Join-Path $runtimeRoot 'engine-error.log')
        $ready = $false
        for ($attempt = 0; $attempt -lt 80; $attempt++) {
            Start-Sleep -Milliseconds 250
            if ($startedEngine.HasExited) {
                throw "HeroScript stopped during startup. See $runtimeRoot\engine.log."
            }
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
        if ($KeepEngine) { Write-Host "EngineProcessId=$($startedEngine.Id)" }
    }

    if ($Headless -or $UiSmoke) {
        $godotArguments = @('--headless')
        if ($VerboseGodot) { $godotArguments += '--verbose' }
        $testArgument = if ($UiSmoke) { '--ui-smoke' } else { '--smoke' }
        $godotArguments += @('--path', $demoRoot, '--', $testArgument, "--api-url=$apiUrl")
        & godot @godotArguments 2>&1 | Tee-Object -Variable godotTestOutput
        $godotTestText = $godotTestOutput -join "`n"
        if ($LASTEXITCODE -ne 0 -or $godotTestText -match '(?m)^(SCRIPT ERROR:|ERROR:)' -or
            $godotTestText -notmatch 'SHOWCASE_(UI_)?SMOKE failures=0') {
            throw 'O smoke test da demo falhou ou reportou erro de script.'
        }
    }
    else {
        & godot --path $demoRoot -- "--api-url=$apiUrl"
    }
}
finally {
    if ($null -ne $startedEngine -and -not $KeepEngine -and -not $startedEngine.HasExited) {
        Stop-Process -Id $startedEngine.Id
    }
}
