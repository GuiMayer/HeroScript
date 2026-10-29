param([string]$Manifest = (Join-Path $PSScriptRoot 'heroscript.json'), [switch]$ServerOnly, [switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path ([IO.Path]::GetFullPath($Manifest)) -Parent
$config = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
if ($config.schemaVersion -ne 1 -or $config.projectId -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]*$' -or
    -not $config.runtimeVersion -or @($config.startupSettings).Count -eq 0) { throw 'Manifesto invalido.' }
$runtimeRoot = Join-Path $projectRoot 'heroscript'
$runtime = Get-Content -LiteralPath (Join-Path $runtimeRoot 'runtime-manifest.json') -Raw | ConvertFrom-Json
if ($runtime.version -cne $config.runtimeVersion) { throw 'A versao instalada difere de runtimeVersion.' }
$executable = Join-Path $runtimeRoot 'API.exe'
$content = [IO.Path]::GetFullPath((Join-Path $projectRoot $config.contentDirectory))
if (-not (Test-Path -LiteralPath $content -PathType Container)) { throw "Conteudo ausente: $content" }
$port = [int]$config.port
if ($port -lt 1024 -or $port -gt 65535) { throw 'Porta invalida.' }
$listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $port)
try { $listener.Start() } finally { $listener.Stop() }
$dataRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) $config.projectId
New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null
$lock = [IO.File]::Open((Join-Path $dataRoot 'runtime.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
$process = $null
try {
    $arguments = @('--urls', "http://127.0.0.1:$port", '--environment', 'Production',
        '--Admin:Enabled=false', '--AllowConfigReload=false', '--ToolAccess:Profile=normal',
        "--Mods:PackageRoots:0=$content", "--Persistence:RunStatePath=$dataRoot/runs",
        "--Persistence:ContentStorePath=$dataRoot/content", "--Persistence:OperationalTelemetryPath=$dataRoot/telemetry")
    for ($i=0; $i -lt $config.startupSettings.Count; $i++) { $arguments += "--Content:StartupSettings:$i=$($config.startupSettings[$i])" }
    for ($i=0; $i -lt $config.allowedOrigins.Count; $i++) { $arguments += "--AllowedOrigins:$i=$($config.allowedOrigins[$i])" }
    $quoted = $arguments | ForEach-Object { if ($_ -match '["\r\n]') { throw 'Argumento invalido.' }; '"' + $_ + '"' }
    $process = Start-Process -FilePath $executable -ArgumentList $quoted -WorkingDirectory $runtimeRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $dataRoot 'api.log') -RedirectStandardError (Join-Path $dataRoot 'api-error.log')
    $url = "http://127.0.0.1:$port"
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw "API encerrou. Consulte $dataRoot/api-error.log" }
        try { Invoke-RestMethod "$url/api/v1/health/ready" -TimeoutSec 2 | Out-Null; $ready = $true; break } catch { Start-Sleep -Milliseconds 300 }
    }
    if (-not $ready) { throw 'Timeout iniciando API.' }
    $owners = @(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction Stop | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($process.HasExited -or $owners.Count -ne 1 -or $owners[0] -ne $process.Id) { throw 'A porta nao pertence a instancia iniciada.' }
    $catalog = Invoke-RestMethod "$url/api/v1/content/settings"
    foreach ($setting in $config.startupSettings) { if (-not ($catalog.items | Where-Object { $_.settingId -eq $setting -and $_.currentRevision })) { throw "Setting nao publicado: $setting" } }
    Write-Host "HeroScript pronto: $url | Dados: $dataRoot"
    if ($CheckOnly) { return }
    if (-not $ServerOnly -and $config.clientEntry) {
        $entry = [IO.Path]::GetFullPath((Join-Path $projectRoot $config.clientEntry))
        if (-not (Test-Path -LiteralPath $entry -PathType Leaf)) { throw 'Interface ausente.' }
        Start-Process (([Uri]$entry).AbsoluteUri + '?api=' + [Uri]::EscapeDataString($url))
    }
    Write-Host 'Mantenha este inicializador aberto. Feche-o com Ctrl+C para encerrar a API.'
    while (-not $process.HasExited) { Start-Sleep -Milliseconds 500 }
    if ($process.ExitCode -ne 0) { throw "API terminou com codigo $($process.ExitCode)." }
} finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id }
    $lock.Dispose()
}
