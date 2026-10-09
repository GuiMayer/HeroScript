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

function Test-CompatibleDemoEngine {
    try {
        Invoke-RestMethod -Uri "$apiUrl/api/v1/health/ready" -TimeoutSec 1 | Out-Null
        $capabilities = Invoke-RestMethod -Uri "$apiUrl/api/v1/capabilities" -TimeoutSec 2
        $catalog = Invoke-RestMethod -Uri "$apiUrl/api/v1/content/settings" -TimeoutSec 2
        $settingIds = @($catalog.items | ForEach-Object { [string]$_.settingId })
        return @($capabilities.capabilities) -contains 'multi-setting-runs' -and
            @($capabilities.capabilities) -contains 'setting-scoped-profiles' -and
            @($capabilities.capabilities) -contains 'persistent-actor-resources' -and
            @($capabilities.capabilities) -contains 'calculated-random-inputs' -and
            @($capabilities.capabilities) -contains 'multi-tier-random-previews' -and
            $settingIds -contains 'default' -and $settingIds -contains 'ascendant'
    }
    catch {
        return $false
    }
}

function Stop-StaleWorkspaceEngine {
    $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $listener) { return }

    $owner = Get-CimInstance Win32_Process -Filter "ProcessId = $($listener.OwningProcess)" `
        -ErrorAction SilentlyContinue
    $commandLine = if ($null -eq $owner.CommandLine) { '' } else { [string]$owner.CommandLine }
    $belongsToWorkspace = $commandLine.IndexOf(
        $repositoryRoot,
        [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -and
        $commandLine -match 'API\.(exe|dll|csproj)'
    if (-not $belongsToWorkspace) {
        throw "A porta $Port está ocupada por outro programa. Encerre-o ou use -Port com outra porta."
    }

    Write-Host 'Atualizando uma instância antiga da HeroScript...' -ForegroundColor Yellow
    Stop-Process -Id $listener.OwningProcess -Force
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 100
        $stillListening = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
        if ($null -eq $stillListening) { return }
    }
    throw "A instância antiga da HeroScript não liberou a porta $Port."
}

New-Item -ItemType Directory -Force -Path `
    (Join-Path $runtimeRoot 'runs'), `
    (Join-Path $runtimeRoot 'content'), `
    (Join-Path $runtimeRoot 'telemetry') | Out-Null

try {
    if (Test-CompatibleDemoEngine) {
        Write-Host "HeroScript online: $apiUrl" -ForegroundColor Green
    }
    else {
        Stop-StaleWorkspaceEngine
        $apiEnvironment = @{
            ASPNETCORE_URLS = $apiUrl
            ToolAccess__Profile = 'dev_modder'
            Admin__Enabled = 'true'
            Admin__ApiKey = 'dev-admin-key'
            AllowConfigReload = 'true'
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
            if (Test-CompatibleDemoEngine) {
                $ready = $true
                break
            }
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
