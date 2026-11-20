param([ValidateRange(1024,65535)][int]$Port = 5280)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    throw "Port $Port already has a listener. Choose a free sandbox port; no existing process will be stopped."
}
$sandboxRoot = Join-Path $env:TEMP ('HeroScript-McpSandbox-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sandboxRoot | Out-Null
$hostPath = Join-Path $sandboxRoot 'host'
dotnet publish (Join-Path $repo 'src/API/API.csproj') --artifacts-path (Join-Path $sandboxRoot 'build') -o $hostPath -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Sandbox build failed' }
$origin = "http://127.0.0.1:$Port"
$stdout = Join-Path $sandboxRoot 'api.out.log'
$stderr = Join-Path $sandboxRoot 'api.err.log'
$arguments = @(('"' + (Join-Path $hostPath 'API.dll') + '"'),'--urls',$origin,'--environment','McpSandbox',
    '--ToolAccess:Profile','dev_modder','--McpSandbox:PersistenceRoot',('"' + $sandboxRoot + '"'),
    '--Persistence:RunStatePath',('"' + (Join-Path $sandboxRoot 'runs') + '"'),
    '--Persistence:ContentStorePath',('"' + (Join-Path $sandboxRoot 'content') + '"'),
    '--Persistence:OperationalTelemetryPath',('"' + (Join-Path $sandboxRoot 'telemetry') + '"'))
$process = Start-Process -FilePath (Get-Command dotnet.exe).Source -WorkingDirectory $hostPath -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
Write-Host "Sandbox PID $($process.Id), origin $origin, dedicated data and logs $sandboxRoot"
$deadline = [DateTime]::UtcNow.AddMinutes(5)
while ([DateTime]::UtcNow -lt $deadline) {
    if ($process.HasExited) { throw "Sandbox stopped before readiness. See $stderr" }
    try {
        $marker = Invoke-RestMethod "$origin/api/v1/diagnostics/mcp-sandbox" -TimeoutSec 2
        if ($marker.isolated) { Write-Host "MCP sandbox ready: $origin"; return }
    } catch { }
    Start-Sleep -Seconds 1
}
throw "Sandbox not ready within five minutes. Inspect $stdout and $stderr; PID $($process.Id)."
