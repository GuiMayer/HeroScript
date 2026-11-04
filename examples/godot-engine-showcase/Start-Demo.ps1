[CmdletBinding()]
param(
    [switch]$Headless,
    [switch]$KeepEngine,
    [switch]$VerboseGodot,
    [switch]$UiSmoke,
    [switch]$ServerOnly,
    [switch]$AutoStart,
    [int]$ClientProcessId = 0,
    [string]$PlayerDataRoot = '',
    [string]$ProgressPath = '',
    [string]$ProgressAttempt = '',
    [ValidateRange(1024, 65535)][int]$Port = 5271
)

$ErrorActionPreference = 'Stop'
$demoRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $demoRoot '..\..')).Path
. (Join-Path $demoRoot 'scripts/launcher/player_data.ps1')
. (Join-Path $demoRoot 'scripts/launcher/server_build.ps1')
$playerRoot = Get-HeroScriptPlayerRoot
if ($PlayerDataRoot) { $playerRoot = [IO.Path]::GetFullPath($PlayerDataRoot) }
$runtimeRoot = Get-HeroScriptRuntimeRoot $playerRoot $Port
$apiProject = Join-Path $repositoryRoot 'src\API\API.csproj'
$apiUrl = "http://127.0.0.1:$Port"
$startedEngine = $null
$ownedApi = $null
$startupMutex = [Threading.Mutex]::new($false, "Local\HeroScriptDemoStartup-$Port")
$startupLock = $false

function Write-StartupProgress([string]$Phase) {
    if (-not $ProgressPath -or -not $ProgressAttempt) { return }
    try {
        $temporary = "$ProgressPath.$PID.tmp"
        $record = @{ version = 1; attemptId = $ProgressAttempt; phase = $Phase; timestampUtc = [DateTimeOffset]::UtcNow.ToString('o') }
        [IO.File]::WriteAllText($temporary, ($record | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $ProgressPath -Force
    } catch { Write-Warning "Startup progress could not be written: $_" }
}

function Test-CompatibleDemoEngine {
    param([switch]$IgnoreStorageBinding)
    try {
        if (-not $IgnoreStorageBinding -and -not (Test-HeroScriptStorageBinding $runtimeRoot $Port)) { return $false }
        Invoke-RestMethod -Uri "$apiUrl/api/v1/health/ready" -TimeoutSec 1 | Out-Null
        $capabilities = Invoke-RestMethod -Uri "$apiUrl/api/v1/capabilities" -TimeoutSec 2
        $catalog = Invoke-RestMethod -Uri "$apiUrl/api/v1/content/settings" -TimeoutSec 2
        $settingIds = @($catalog.items | ForEach-Object { [string]$_.settingId })
        $ascendant = @($catalog.items | Where-Object settingId -eq 'ascendant') | Select-Object -First 1
        return @($capabilities.capabilities) -contains 'multi-setting-runs' -and
            @($capabilities.capabilities) -contains 'setting-scoped-profiles' -and
            @($capabilities.capabilities) -contains 'persistent-actor-resources' -and
            @($capabilities.capabilities) -contains 'calculated-random-inputs' -and
            @($capabilities.capabilities) -contains 'multi-tier-random-previews' -and
            @($capabilities.capabilities) -contains 'configurable-profile-progress' -and
            @($capabilities.capabilities) -contains 'frozen-run-eligibility' -and
            @($capabilities.capabilities) -contains 'foundation-read-models' -and
            @($capabilities.capabilities) -contains 'foundation-reload-safety' -and
            @($capabilities.capabilities) -contains 'canonical-effect-identities' -and
            @($capabilities.capabilities) -contains 'global-effect-policies' -and
            @($capabilities.capabilities) -contains 'actor-attribute-projections' -and
            @($capabilities.capabilities) -contains 'compact-interaction-sessions-v1' -and
            $settingIds -contains 'default' -and $settingIds -contains 'ascendant' -and
            $null -ne $ascendant -and $ascendant.launch.modeId -eq 'ascendant_world_one'
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

try {
    try { $startupLock = $startupMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $startupLock = $true }
    if (-not $startupLock) { throw "Outra inicialização já está em andamento na porta $Port. Aguarde o servidor ficar pronto." }
    $reuseEngine = Test-CompatibleDemoEngine
    if (-not $reuseEngine) {
        if ($AutoStart -and (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)) {
            throw "Porta $Port ocupada por um servidor não reconhecido. A inicialização automática não encerra servidores existentes."
        }
        Stop-StaleWorkspaceEngine
    }
    Write-StartupProgress 'preparing_data'
    $legacyPreferences = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)) 'Godot/app_userdata/HeroScript- Ember Archive/heroscript_showcase.cfg'
    $runtimeRoot = Initialize-HeroScriptPlayerData -DemoRoot $demoRoot -PlayerRoot $playerRoot -Port $Port -LegacyPreferences $legacyPreferences
    Write-Host "Dados do jogador: $playerRoot" -ForegroundColor Cyan
    if ($reuseEngine) {
        Write-Host "HeroScript online: $apiUrl" -ForegroundColor Green
    }
    else {
        Write-StartupProgress 'checking_build'
        $apiEnvironment = @{
            HEROSCRIPT_STARTUP_PROGRESS_PATH = $ProgressPath
            HEROSCRIPT_STARTUP_ATTEMPT = $ProgressAttempt
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
        $buildRoot = Join-Path $runtimeRoot 'build'
        $serverBinary = Get-HeroScriptServerBinary $repositoryRoot $apiProject $buildRoot $runtimeRoot
        Write-StartupProgress 'starting_server'
        [Environment]::SetEnvironmentVariable('ASPNETCORE_CONTENTROOT', (Split-Path $apiProject), 'Process')
        $startedEngine = Start-Process -FilePath 'dotnet' `
            -ArgumentList @("`"$serverBinary`"") `
            -WorkingDirectory $repositoryRoot -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $runtimeRoot 'engine.log') `
            -RedirectStandardError (Join-Path $runtimeRoot 'engine-error.log')
        $ready = $false
        for ($attempt = 0; $attempt -lt 240; $attempt++) {
            Start-Sleep -Milliseconds 250
            if ($startedEngine.HasExited) {
                throw "HeroScript stopped during startup. See $runtimeRoot\engine.log."
            }
            if (Test-CompatibleDemoEngine -IgnoreStorageBinding) {
                $ready = $true
                break
            }
        }
        if (-not $ready) {
            throw "A HeroScript não iniciou. Consulte $runtimeRoot\engine-error.log."
        }
        Save-HeroScriptStorageBinding $runtimeRoot $Port
        $boundListener = Get-NetTCPConnection -LocalPort $Port -State Listen | Select-Object -First 1
        $ownedApi = Get-Process -Id $boundListener.OwningProcess
        Write-Host 'HeroScript iniciada com dados isolados da demo.' -ForegroundColor Green
        if ($KeepEngine) { Write-Host "EngineProcessId=$($startedEngine.Id)" }
    }
    $startupMutex.ReleaseMutex()
    $startupLock = $false

    if ($ServerOnly) {
        if ($null -ne $startedEngine -and -not $KeepEngine) {
            $client = if ($ClientProcessId -gt 0) { Get-Process -Id $ClientProcessId -ErrorAction SilentlyContinue } else { $null }
            while (-not $startedEngine.HasExited -and ($ClientProcessId -eq 0 -or ($null -ne $client -and -not $client.HasExited))) {
                Start-Sleep -Milliseconds 500
            }
        }
        return
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
catch {
    Write-StartupProgress 'failed'
    throw
}
finally {
    if ($startupLock) { $startupMutex.ReleaseMutex() }
    $startupMutex.Dispose()
    if ($null -ne $startedEngine -and -not $KeepEngine -and $null -ne $ownedApi -and -not $ownedApi.HasExited) {
        # dotnet run is the parent; its API child must not survive the game.
        Stop-Process -Id $ownedApi.Id -ErrorAction SilentlyContinue
    }
    if ($null -ne $startedEngine -and -not $KeepEngine -and -not $startedEngine.HasExited) {
        Stop-Process -Id $startedEngine.Id
    }
}
