$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
. (Join-Path $workspace 'examples/godot-engine-showcase/scripts/launcher/player_data.ps1')
# Dependency seam: fixture migration must not depend on the player's live port.
$script:simulatedWriter = $false
function Assert-HeroScriptMigrationIdle { param([int]$Port); if ($script:simulatedWriter) { throw 'Simulated active writer' } }
$fixture = Join-Path $workspace ('.runtime/player-data-test-' + [Guid]::NewGuid().ToString('N'))
$demo = Join-Path $fixture 'demo'
$player = Join-Path $fixture 'AppData/HeroScript'
$legacy = Join-Path $demo '.runtime'
$legacyPrefs = Join-Path $fixture 'old/heroscript_showcase.cfg'
function Check([bool]$Condition, [string]$Label) {
    if (-not $Condition) { throw "[FAIL] $Label" }
    Write-Host "[PASS] $Label"
}
foreach ($relative in @('runs/default-run/commits/00000001.json', 'runs/ascendant-run/commits/00000001.json',
    'runs/_profile-progress/profile.lease', 'content/published/revision-a.json', 'content/published/revision-b.json', 'telemetry/history.jsonl')) {
    $file = Join-Path $legacy $relative
    New-Item -ItemType Directory -Path (Split-Path $file -Parent) -Force | Out-Null
    @{ fixture = $relative; immutable = $true } | ConvertTo-Json | Set-Content -LiteralPath $file -Encoding utf8
}
New-Item -ItemType Directory -Path (Split-Path $legacyPrefs -Parent) -Force | Out-Null
'[session]', 'selected_setting_id="ascendant"', 'resume_runs={"default":"run-a", "ascendant":"run-b"}' | Set-Content -LiteralPath $legacyPrefs -Encoding utf8
'{"draw":{"seconds":1}}' | Set-Content -LiteralPath (Join-Path (Split-Path $legacyPrefs -Parent) 'card_animation_profile.json')
$target = Initialize-HeroScriptPlayerData -DemoRoot $demo -PlayerRoot $player -LegacyPreferences $legacyPrefs
Check ($target -eq (Join-Path $player 'engine')) 'normal gameplay uses AppData/HeroScript/engine'
foreach ($original in Get-ChildItem -LiteralPath $legacy -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($legacy, $original.FullName)
    Check ((Get-FileHash -LiteralPath $original.FullName).Hash -eq (Get-FileHash -LiteralPath (Join-Path $target $relative)).Hash) "byte-identical migration: $relative"
}
Check ((Get-FileHash -LiteralPath $legacyPrefs).Hash -eq (Get-FileHash -LiteralPath (Join-Path $player 'heroscript_showcase.cfg')).Hash) 'preferences and per-setting bookmarks copied byte-for-byte'
Check ((Get-FileHash -LiteralPath (Join-Path (Split-Path $legacyPrefs -Parent) 'card_animation_profile.json')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $player 'card_animation_profile.json')).Hash) 'optional animation profile also migrates without conversion'
$manifest = Get-Content -LiteralPath (Join-Path $target 'migration-v1.json') -Raw | ConvertFrom-Json
Check ($manifest.files.Count -eq 6 -and $manifest.sourceRetained) 'migration manifest records all hashes and retained originals'
$newCommit = Join-Path $target 'runs/default-run/commits/00000002.json'
'new authoritative commit' | Set-Content -LiteralPath $newCommit
$again = Initialize-HeroScriptPlayerData -DemoRoot $demo -PlayerRoot $player -LegacyPreferences $legacyPrefs
Check ((Get-Content -LiteralPath $newCommit -Raw).Trim() -eq 'new authoritative commit' -and $again -eq $target) 'repeat migration never overwrites newer progress'
Check ((Get-ChildItem -LiteralPath $legacy -Recurse -File).Count -eq 6 -and (Test-Path -LiteralPath $legacyPrefs)) 'all original files remain available for rollback'
$conflictPlayer = Join-Path $fixture 'conflict'
New-Item -ItemType Directory -Path (Join-Path $conflictPlayer 'engine') -Force | Out-Null
'must survive' | Set-Content -LiteralPath (Join-Path $conflictPlayer 'engine/user-save.txt')
$rejected = $false
try { Initialize-HeroScriptPlayerData -DemoRoot $demo -PlayerRoot $conflictPlayer | Out-Null } catch { $rejected = $true }
Check ($rejected -and (Get-Content -LiteralPath (Join-Path $conflictPlayer 'engine/user-save.txt')).Trim() -eq 'must survive') 'unknown existing destination is rejected without overwriting'
$qa = Initialize-HeroScriptPlayerData -DemoRoot $demo -PlayerRoot $player -Port 61983
Check ($qa -eq (Join-Path $player 'qa/61983/engine') -and -not (Test-Path -LiteralPath (Join-Path $qa 'runs/default-run'))) 'QA data never imports or shares normal-game progress'
$differentPrefs = Join-Path $player 'heroscript_showcase.cfg'
'new preferences' | Set-Content -LiteralPath $differentPrefs
Initialize-HeroScriptPlayerData -DemoRoot $demo -PlayerRoot $player -LegacyPreferences $legacyPrefs | Out-Null
Check ((Get-Content -LiteralPath $differentPrefs).Trim() -eq 'new preferences') 'existing AppData preferences are never replaced by older settings'
$script:simulatedWriter = $true
$busyPlayer = Join-Path $fixture 'busy'
$rejected = $false
try { Initialize-HeroScriptPlayerData -DemoRoot $demo -PlayerRoot $busyPlayer | Out-Null } catch { $rejected = $true }
Check ($rejected -and -not (Test-Path -LiteralPath (Join-Path $busyPlayer 'engine'))) 'active writer blocks migration before publishing any destination'
Write-Host "Fixtures retained at $fixture"
