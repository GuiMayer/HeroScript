param(
    [Parameter(Mandatory = $true)][string]$UiZip,
    [Parameter(Mandatory = $true)][string]$MagicZip
)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content (Join-Path $project 'data/audio_manifest.json') -Raw | ConvertFrom-Json
$temp = New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) ('heroscript-sfx-' + [guid]::NewGuid()))
try {
    $sources = @{}
    foreach ($pack in @{ ui = $UiZip; magic = $MagicZip }.GetEnumerator()) {
        $folder = Join-Path $temp.FullName $pack.Key
        Expand-Archive -LiteralPath $pack.Value -DestinationPath $folder
        $sources[$pack.Key] = @(Get-ChildItem -LiteralPath $folder -Recurse -File -Filter '*.ogg')
    }
    $paths = @($manifest.events.PSObject.Properties.Value | ForEach-Object { $_.preferred_clips } | Where-Object { $_ } | Sort-Object -Unique)
    foreach ($clip in $paths) {
        if ($clip -notmatch '^res://assets/audio/willosuna-local/(ui|magic)/([a-z0-9_]+\.ogg)$') { throw "Invalid local clip path: $clip" }
        $pack = $Matches[1]; $name = $Matches[2]
        $files = @($sources[$pack] | Where-Object Name -eq $name)
        if ($files.Count -ne 1) { throw "Expected exactly one $name in $pack archive." }
        $destination = Join-Path $project ($clip.Substring(6))
        New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath $files[0].FullName -Destination $destination -Force
    }
    Write-Host "Installed $($paths.Count) selected one-shot audio clips. Import the project in Godot."
} finally {
    Remove-Item -LiteralPath $temp.FullName -Recurse -Force
}
