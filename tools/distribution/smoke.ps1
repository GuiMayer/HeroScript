param([Parameter(Mandatory=$true)][string]$Package)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path $Package).Path
$temporary = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $temporary | Out-Null
$listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$process = $null
try {
    $arguments = @('--urls', "http://127.0.0.1:$port", '--environment=Production',
      "--Mods:PackageRoots:0=$root/content", '--Content:StartupSettings:0=uno-clash',
      "--Persistence:RunStatePath=$temporary/runs", "--Persistence:ContentStorePath=$temporary/content",
      "--Persistence:OperationalTelemetryPath=$temporary/telemetry") | ForEach-Object { '"' + $_ + '"' }
    $process = Start-Process (Join-Path $root 'heroscript/API.exe') -ArgumentList $arguments -WorkingDirectory $temporary -WindowStyle Hidden -PassThru -RedirectStandardOutput "$temporary/api.log" -RedirectStandardError "$temporary/error.log"
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw (Get-Content "$temporary/error.log" -Raw) }
        try { Invoke-RestMethod "http://127.0.0.1:$port/api/v1/health/ready" -TimeoutSec 2 | Out-Null; $ready=$true; break } catch { Start-Sleep -Milliseconds 300 }
    }
    if (-not $ready) { throw 'Readiness timeout' }
    $catalog = Invoke-RestMethod "http://127.0.0.1:$port/api/v1/content/settings"
    $setting = $catalog.items | Where-Object settingId -eq 'uno-clash'
    if (-not $setting.currentRevision) { throw 'External setting not loaded' }
    $body = @{ commandId=[Guid]::NewGuid().ToString(); settingId='uno-clash'; contentRevision=$setting.currentRevision; runDefinitionId='uno_clash_run'; playerEntityId='player'; modeId='uno_classic'; seed=3 } | ConvertTo-Json
    $run = Invoke-RestMethod "http://127.0.0.1:$port/api/v1/runs" -Method Post -ContentType 'application/json' -Body $body
    if (-not $run.runId) { throw 'Run not created' }
    Invoke-RestMethod "http://127.0.0.1:$port/api/v1/runs/$($run.runId)" | Out-Null
    Write-Host 'PASS: published executable, external content, readiness and persisted run.'
    Stop-Process -Id $process.Id
    $process.WaitForExit()
    $probe = Get-Content (Join-Path $root 'heroscript.json') -Raw | ConvertFrom-Json
    $probe.port = $port
    $probe.projectId = 'heroscript-qa-' + [Guid]::NewGuid().ToString('N')
    $probePath = Join-Path $root 'smoke-project.json'
    $probe | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $probePath -Encoding UTF8
    & (Join-Path $root 'Start-HeroScript.ps1') -Manifest $probePath -CheckOnly
    if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { throw 'Launcher left an orphan API' }
    Write-Host 'PASS: project manifest, runtime version, launcher readiness and owned-process cleanup.'
    $probe.runtimeVersion = '0.0.0-invalid'
    $probe | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $probePath -Encoding UTF8
    $rejected = $false
    try { & (Join-Path $root 'Start-HeroScript.ps1') -Manifest $probePath -CheckOnly } catch { $rejected = $true }
    if (-not $rejected) { throw 'Mismatched runtime version was accepted' }
    $probe.runtimeVersion = (Get-Content (Join-Path $root 'heroscript/runtime-manifest.json') -Raw | ConvertFrom-Json).version
    $probe | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $probePath -Encoding UTF8
    $foreign = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $port)
    try {
        $foreign.Start()
        $rejected = $false
        try { & (Join-Path $root 'Start-HeroScript.ps1') -Manifest $probePath -CheckOnly } catch { $rejected = $true }
        if (-not $rejected -or -not $foreign.Server.IsBound) { throw 'Occupied port handling failed' }
    } finally { $foreign.Stop() }
    Write-Host 'PASS: incompatible version and occupied port rejected without killing another listener.'
} finally { if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id }; Write-Host "Smoke logs: $temporary" }
