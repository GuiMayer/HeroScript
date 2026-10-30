param([switch]$NoBrowser, [switch]$CheckOnly, [ValidateRange(1024,65535)][int]$ApiPort = 15260)
$ErrorActionPreference = 'Stop'
$manifest = Join-Path $PSScriptRoot 'heroscript.json'
$launcher = Join-Path $PSScriptRoot 'heroscript/Start-HeroScript.ps1'
if (-not (Test-Path -LiteralPath $launcher)) { throw 'Runtime ausente. Execute Install-HeroScript.ps1 uma vez antes de abrir o jogo.' }
$config = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
$temporary = $null
try {
    if ($PSBoundParameters.ContainsKey('ApiPort')) {
        $config.port = $ApiPort
        $temporary = Join-Path $PSScriptRoot ('.heroscript-session-' + [Guid]::NewGuid().ToString('N') + '.json')
        $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temporary -Encoding UTF8
        $manifest = $temporary
    }
    & $launcher -Manifest $manifest -ServerOnly:$NoBrowser -CheckOnly:$CheckOnly
} finally {
    if ($temporary -and (Test-Path -LiteralPath $temporary)) { Remove-Item -LiteralPath $temporary }
}
