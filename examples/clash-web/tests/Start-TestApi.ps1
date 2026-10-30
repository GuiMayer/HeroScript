param(
    [ValidateSet('stack','stack-four','effects-four','effects-four-draw2','recycle','recycle-four','empty','bot-buy','bot-buy-clash','bot-extra','bot-draw4','player-extra')][string]$Mode = 'stack',
    [ValidateRange(1024,65535)][int]$Port = 15262
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) { throw "Porta de testes $Port ocupada." }
$api = Get-Item -LiteralPath (Join-Path $repo 'heroscript/API.exe')
if (-not $api) { throw 'Instale o runtime com Install-HeroScript.ps1.' }
$fixture = Join-Path $repo ('.runtime\clash-fixture-' + $Mode + '-' + [Guid]::NewGuid().ToString('N'))
$python = (Get-Command python -CommandType Application | Select-Object -First 1).Source
& $python (Join-Path $PSScriptRoot 'make-fixture.py') $Mode $fixture | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Falha ao preparar os JSONs de teste.' }
$startupSetting = if ($Mode -in @('recycle-four', 'stack-four', 'effects-four', 'effects-four-draw2')) { 'circular-table' } else { 'clash' }
$arguments = @('--urls',"http://localhost:$Port", "`"--Mods:PackageRoots:0=$fixture`"",
    "--Content:StartupSettings:0=$startupSetting", '--environment=Development',
    "`"--Persistence:RunStatePath=$fixture\runs`"", "`"--Persistence:ContentStorePath=$fixture\content`"",
    "`"--Persistence:OperationalTelemetryPath=$fixture\telemetry`""
)
$process = Start-Process -FilePath $api.FullName -ArgumentList $arguments -WorkingDirectory $api.DirectoryName -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $fixture 'api.log') -RedirectStandardError (Join-Path $fixture 'api-error.log')
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) { throw "API de teste encerrou. Consulte $fixture\api-error.log" }
        try { Invoke-RestMethod "http://localhost:$Port/api/v1/health/ready" -TimeoutSec 1 | Out-Null; $ready = $true; break }
        catch { Start-Sleep -Milliseconds 250 }
    }
    if (-not $ready) { throw 'Tempo esgotado iniciando API de testes.' }
    $receipt = @{ processId=$process.Id; api="http://localhost:$Port"; store=(Join-Path $fixture 'runs'); root=$fixture; mode=$Mode }
    $receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $repo ".runtime\clash-fixture-$Port.json") -Encoding UTF8
    $receipt | ConvertTo-Json -Compress
} catch {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    throw
}
