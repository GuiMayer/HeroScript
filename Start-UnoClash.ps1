[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$ApiPort = 5260,
    [switch]$NoBrowser,
    [ValidateRange(1, 600)][int]$StartupTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'

function Get-UnoPortOwners {
    param([int]$Port)
    # Read all listeners successfully; a failed inspection must never mean "free".
    @(Get-NetTCPConnection -State Listen -ErrorAction Stop |
        Where-Object LocalPort -eq $Port |
        Select-Object -ExpandProperty OwningProcess -Unique)
}

function Test-UnoWorkspaceApi {
    param($ProcessInfo, [string]$RepositoryRoot, [string]$BuildDirectory)
    if ($null -eq $ProcessInfo) { return $false }
    $executable = [string]$ProcessInfo.ExecutablePath
    $commandLine = [string]$ProcessInfo.CommandLine
    if (-not $executable) { return $false }
    $sourceBin = [regex]::Escape((Join-Path $RepositoryRoot 'src\API\bin\'))
    $managedExe = Join-Path $BuildDirectory 'API.exe'
    if ($executable -ieq $managedExe -or
        $executable -match ("^" + $sourceBin + '(Debug|Release)\\net[^\\]+\\API\.exe$')) {
        return $true
    }
    if ([IO.Path]::GetFileName($executable) -ine 'dotnet.exe') { return $false }
    $managedDll = [regex]::Escape((Join-Path $BuildDirectory 'API.dll'))
    $sourceDll = $sourceBin + '(Debug|Release)\\net[^\\]+\\API\.dll'
    # Match the dotnet entry assembly, not an arbitrary path appearing later in arguments.
    return $commandLine -match ('^(?:"[^"]*dotnet\.exe"|\S*dotnet\.exe)\s+(?:"(?:' +
        $managedDll + '|' + $sourceDll + ')"|(?:' + $managedDll + '|' + $sourceDll + ')(?=\s|$))')
}

function Get-UnoWorkspaceListeners {
    param([int]$Port, [string]$RepositoryRoot, [string]$BuildDirectory)
    $owners = @(Get-UnoPortOwners $Port)
    foreach ($ownerId in $owners) {
        $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId = $ownerId" -ErrorAction Stop
        if (-not (Test-UnoWorkspaceApi $processInfo $RepositoryRoot $BuildDirectory)) {
            throw "A porta $Port esta ocupada por outro programa ou outra copia do projeto (PID $ownerId). Nenhum processo foi encerrado. Feche esse programa ou use -ApiPort com outra porta."
        }
    }
    return $owners
}

function Stop-UnoWorkspaceListeners {
    param([int]$Port, [string]$RepositoryRoot, [string]$BuildDirectory)
    # Validate every owner before stopping any of them, then recheck identity against PID reuse.
    $owners = @(Get-UnoWorkspaceListeners $Port $RepositoryRoot $BuildDirectory)
    foreach ($ownerId in $owners) {
        $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId = $ownerId" -ErrorAction Stop
        if (-not (Test-UnoWorkspaceApi $processInfo $RepositoryRoot $BuildDirectory)) {
            throw "O processo na porta $Port mudou durante a inicializacao. Tente novamente."
        }
        Write-Host '[1/3] Reiniciando a API deste projeto para carregar as atualizacoes...' -ForegroundColor Yellow
        Stop-Process -Id $ownerId -Force -ErrorAction Stop
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (@(Get-UnoPortOwners $Port).Count -gt 0) {
        if ([DateTime]::UtcNow -ge $deadline) { throw "A porta $Port nao foi liberada. O jogo nao sera aberto." }
        Start-Sleep -Milliseconds 200
    }
}

function Get-UnoReadySetting {
    param([string]$ApiUrl)
    Invoke-RestMethod -Uri "$ApiUrl/api/v1/health/ready" -TimeoutSec 2 -ErrorAction Stop | Out-Null
    $catalog = Invoke-RestMethod -Uri "$ApiUrl/api/v1/content/settings" -TimeoutSec 2 -ErrorAction Stop
    $settings = @($catalog.items | Where-Object settingId -eq 'uno-clash')
    if ($settings.Count -ne 1 -or [string]::IsNullOrWhiteSpace($settings[0].currentRevision)) {
        throw "A API ainda nao publicou uma revisao valida de uno-clash."
    }
    $setting = $settings[0]
    $four = @($catalog.items | Where-Object settingId -eq 'circular-table')
    if ($four.Count -ne 1 -or [string]::IsNullOrWhiteSpace($four[0].currentRevision) -or
        $four[0].launch.runDefinitionId -ne 'table_run' -or
        $four[0].launch.playerEntityId -ne 'seat_0' -or $four[0].launch.modeId -ne 'table_mode') {
        throw 'O modo circular-table ainda nao foi publicado com uma revisao valida.'
    }
    if ($setting.launch.runDefinitionId -ne 'uno_clash_run' -or
        $setting.launch.playerEntityId -ne 'player' -or $setting.launch.modeId -ne 'uno_classic') {
        throw 'O pacote uno-clash publicado nao corresponde ao jogo.'
    }
    # Confirm the actual published rules are readable at the revision the browser will use.
    $revision = [Uri]::EscapeDataString($setting.currentRevision)
    foreach ($entry in @(@('combat-rules', 'uno_combat'), @('cards', 'uno_red_reverse'), @('actions', 'uno_draw_card'))) {
        $definition = Invoke-RestMethod -Uri "$ApiUrl/api/v1/content/$($entry[0])/$($entry[1])?configName=uno-clash&revision=$revision" -TimeoutSec 2 -ErrorAction Stop
        if ($null -eq $definition.definition) { throw "Conteudo publicado indisponivel: $($entry[1])." }
    }
    $fourRevision = [Uri]::EscapeDataString($four[0].currentRevision)
    foreach ($entry in @(@('combat-rules', 'table_combat'), @('cards', 't_red_reverse'), @('actions', 't_draw'))) {
        $definition = Invoke-RestMethod -Uri "$ApiUrl/api/v1/content/$($entry[0])/$($entry[1])?configName=circular-table&revision=$fourRevision" -TimeoutSec 2 -ErrorAction Stop
        if ($null -eq $definition.definition) { throw "Conteudo publicado indisponivel: $($entry[1])." }
    }
    return $setting
}

function Wait-UnoApi {
    param($ApiProcess, [int]$Port, [string]$ApiUrl, [int]$TimeoutSeconds)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastFailure = 'A API ainda nao respondeu.'
    do {
        $ApiProcess.Refresh()
        if ($ApiProcess.HasExited) { throw "A API encerrou durante a inicializacao (codigo $($ApiProcess.ExitCode))." }
        $owners = @(Get-UnoPortOwners $Port)
        if ($owners.Count -gt 0 -and ($owners.Count -ne 1 -or $owners[0] -ne $ApiProcess.Id)) {
            throw "Outro processo ocupou a porta $Port durante a inicializacao. O jogo nao sera aberto."
        }
        if ($owners.Count -eq 1) {
            try { return Get-UnoReadySetting $ApiUrl }
            catch { $lastFailure = $_.Exception.Message }
        }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Tempo esgotado aguardando o UNO Clash ficar pronto. $lastFailure"
}

function Invoke-UnoClashLauncher {
    param([int]$Port, [switch]$SkipBrowser, [int]$TimeoutSeconds)
    $repositoryRoot = [IO.Path]::GetFullPath($PSScriptRoot)
    $webClient = Join-Path $repositoryRoot 'examples\uno-clash-web\index.html'
    $apiProject = Join-Path $repositoryRoot 'src\API\API.csproj'
    $runtimeRoot = Join-Path $repositoryRoot ".runtime\uno-clash\$Port"
    $buildDirectory = Join-Path $runtimeRoot 'api'
    $apiUrl = "http://localhost:$Port"
    $mutex = New-Object Threading.Mutex($false, "Local\HeroScriptUnoClash-$Port")
    $hasLock = $false
    $apiProcess = $null
    $ready = $false
    $savedEnvironment = @{}
    try {
        try { $hasLock = $mutex.WaitOne(0) }
        catch [Threading.AbandonedMutexException] { $hasLock = $true }
        if (-not $hasLock) { throw "Outro inicializador esta usando a porta $Port. Aguarde a inicializacao terminar." }
        foreach ($required in @($webClient, $apiProject)) {
            if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Arquivo necessario nao encontrado: $required" }
        }
        $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
        Write-Host 'UNO CLASH -- preparando API e regras atuais' -ForegroundColor Cyan
        # Never trust a responding API alone. Always build and restart our own workspace instance.
        Stop-UnoWorkspaceListeners $Port $repositoryRoot $buildDirectory
        $buildLog = Join-Path $runtimeRoot 'build.log'
        Write-Host '[2/3] Compilando a API com os arquivos atuais do projeto...' -ForegroundColor Cyan
        & $dotnet build $apiProject --configuration Release --output $buildDirectory --no-incremental --nologo *> $buildLog
        if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao. Consulte $buildLog" }
        if (@(Get-UnoPortOwners $Port).Count -gt 0) { throw "A porta $Port foi ocupada durante a compilacao. O jogo nao sera aberto." }
        $apiDll = Join-Path $buildDirectory 'API.dll'
        if (-not (Test-Path -LiteralPath $apiDll -PathType Leaf)) { throw "A compilacao nao gerou $apiDll" }
        $environment = @{
            ASPNETCORE_ENVIRONMENT = 'Development'
            DOTNET_ENVIRONMENT = 'Development'
        }
        foreach ($name in $environment.Keys) {
            $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process')
        }
        $arguments = @(
            "`"$apiDll`"", '--urls', $apiUrl,
            "`"--Mods:PackageRoots:0=$(Join-Path $repositoryRoot 'data\configs')`"",
            '--Content:StartupSettings:0=uno-clash',
            '--Content:StartupSettings:1=circular-table',
            "`"--Persistence:RunStatePath=$(Join-Path $runtimeRoot 'runs')`"",
            "`"--Persistence:ContentStorePath=$(Join-Path $runtimeRoot 'content')`"",
            "`"--Persistence:OperationalTelemetryPath=$(Join-Path $runtimeRoot 'telemetry')`""
        )
        $apiProcess = Start-Process -FilePath $dotnet -ArgumentList $arguments -WorkingDirectory $buildDirectory `
            -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtimeRoot 'api.log') `
            -RedirectStandardError (Join-Path $runtimeRoot 'api-error.log')
        $setting = Wait-UnoApi $apiProcess $Port $apiUrl $TimeoutSeconds
        $ready = $true
        Write-Host "[OK] API atualizada em $apiUrl; UNO Clash publicado (rev:$($setting.currentRevision))." -ForegroundColor Green
        if (-not $SkipBrowser) {
            # Pass custom ports explicitly and give each launch a distinct page URL.
            $pageUri = ([Uri]$webClient).AbsoluteUri
            $gameUrl = "$($pageUri)?api=$([Uri]::EscapeDataString($apiUrl))&launch=$([Guid]::NewGuid().ToString('N'))"
            Write-Host '[3/3] Abrindo o jogo com a API verificada...' -ForegroundColor Green
            Start-Process $gameUrl
        }
    }
    catch {
        throw "$($_.Exception.Message) Logs: $runtimeRoot"
    }
    finally {
        foreach ($name in $savedEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
        }
        # A failed startup must not leave an unusable API behind. Do not touch other processes.
        if (-not $ready -and $null -ne $apiProcess -and -not $apiProcess.HasExited) {
            Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
        }
        if ($hasLock) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

# Dot-sourcing exposes the functions to the launcher regression tests without starting the API.
if ($MyInvocation.InvocationName -ne '.') {
    try {
        Invoke-UnoClashLauncher -Port $ApiPort -SkipBrowser:$NoBrowser -TimeoutSeconds $StartupTimeoutSeconds
        exit 0
    }
    catch {
        Write-Host "[ERRO] $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}
