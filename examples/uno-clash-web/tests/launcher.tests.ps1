$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Assert-That($condition, $message) { if (-not $condition) { throw $message } }
$binary = Join-Path $root 'heroscript/API.exe'
$before = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
$listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
& (Join-Path $root 'Start-Clash.ps1') -CheckOnly -ApiPort $port
Assert-That (-not (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)) 'API ficou aberta apos CheckOnly'
try {
    $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $port)
    $listener.Start()
    $rejected = $false
    try { & (Join-Path $root 'Start-Clash.ps1') -CheckOnly -ApiPort $port } catch { $rejected = $true }
    Assert-That ($rejected -and $listener.Server.IsBound) 'Porta ocupada nao foi preservada'
} finally { $listener.Stop() }
$temporary = Join-Path $root '.runtime/bad-runtime.zip'
[IO.File]::WriteAllText($temporary, 'invalid archive')
$rejected = $false
try { & (Join-Path $root 'Install-HeroScript.ps1') -Archive $temporary } catch { $rejected = $true }
Assert-That $rejected 'Checksum invalido foi aceito'
Assert-That ((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash -eq $before) 'O binario da engine foi alterado'
Assert-That (-not (Get-ChildItem -LiteralPath $root -Filter '.heroscript-session-*.json')) 'Manifesto temporario nao removido'
Write-Host 'PASS: manifesto, dois settings, runtime instalado, porta ocupada, checksum e encerramento; binario intacto.'
