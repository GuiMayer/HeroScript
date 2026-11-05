$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $workspace ('.runtime/local-server-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$launcher = Join-Path $workspace 'examples/godot-engine-showcase/Start-Demo.ps1'
$shellExe = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$port = 61984
if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { throw 'Test port already occupied; no processes changed.' }
function Check([bool]$Ok, [string]$Label) { if (-not $Ok) { throw "[FAIL] $Label" }; Write-Host "[PASS] $Label" }
$client = $null
$hostProcess = $null
$ownedApiId = 0
try {
    $client = Start-Process -FilePath $shellExe -ArgumentList @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 180') -WindowStyle Hidden -PassThru
    $arguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', "`"$launcher`"", '-ServerOnly', '-AutoStart', '-ClientProcessId', $client.Id, '-Port', $port, '-PlayerDataRoot', "`"$(Join-Path $fixture 'player')`"")
    $hostProcess = Start-Process -FilePath $shellExe -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $fixture 'startup.log') -RedirectStandardError (Join-Path $fixture 'startup-error.log')
    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(130)
    while ([DateTime]::UtcNow -lt $deadline -and -not $hostProcess.HasExited) {
        try { $ready = (Invoke-RestMethod "http://127.0.0.1:$port/api/v1/health/ready" -TimeoutSec 1).status -eq 'ready' } catch {}
        $binding = Join-Path $fixture "player/qa/$port/engine/launcher-session.json"
        if ($ready -and (Test-Path -LiteralPath $binding)) { break }
        Start-Sleep -Milliseconds 300
    }
    Check ($ready -and (Test-Path -LiteralPath $binding)) 'real Kestrel starts from Windows PowerShell and reaches readiness'
    $registration = Get-Content -LiteralPath $binding -Raw | ConvertFrom-Json
    $ownedApiId = [int]$registration.processId
    Check ($registration.runtimeRoot -eq (Join-Path $fixture "player/qa/$port/engine")) 'autostart uses only the isolated AppData-equivalent store'
    $again = Start-Process -FilePath $shellExe -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $fixture 'reuse.log') -RedirectStandardError (Join-Path $fixture 'reuse-error.log')
    Check ($again.WaitForExit(15000) -and $again.ExitCode -eq 0) 'second server-only invocation reuses the existing server'
    Check ((Get-NetTCPConnection -LocalPort $port -State Listen | Select-Object -First 1).OwningProcess -eq $ownedApiId) 'reusing does not duplicate or stop the API'
    $foreign = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 61985)
    $foreign.Start()
    try {
        $refusedArguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', "`"$launcher`"", '-ServerOnly', '-AutoStart', '-Port', 61985, '-PlayerDataRoot', "`"$(Join-Path $fixture 'foreign')`"")
        $refused = Start-Process -FilePath $shellExe -ArgumentList $refusedArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $fixture 'foreign.log') -RedirectStandardError (Join-Path $fixture 'foreign-error.log')
        Check ($refused.WaitForExit(15000) -and $refused.ExitCode -ne 0 -and $foreign.Server.IsBound) 'occupied unknown port is refused without terminating its owner'
    } finally { $foreign.Stop() }
    Stop-Process -Id $client.Id
    Check ($hostProcess.WaitForExit(15000)) 'lifecycle supervisor exits after its client closes'
    Start-Sleep -Milliseconds 500
    Check (-not [bool](Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)) 'owned API child is stopped, not orphaned'
    Write-Host "Logs and fixtures retained: $fixture"
} finally {
    if ($null -ne $client -and -not $client.HasExited) { Stop-Process -Id $client.Id -ErrorAction SilentlyContinue }
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id -ErrorAction SilentlyContinue }
    if ($ownedApiId -gt 0) { Stop-Process -Id $ownedApiId -ErrorAction SilentlyContinue }
}
