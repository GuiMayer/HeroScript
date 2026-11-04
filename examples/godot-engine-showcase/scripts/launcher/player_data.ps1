# Player-owned data only. Never modifies commits, rules, bookmarks or their IDs.
function Get-HeroScriptPlayerRoot {
    $roaming = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
    if ([string]::IsNullOrWhiteSpace($roaming)) { throw 'ApplicationData is unavailable.' }
    return Join-Path $roaming 'HeroScript'
}

function Get-HeroScriptRuntimeRoot {
    param([string]$PlayerRoot, [int]$Port = 5271)
    if ($Port -eq 5271) { return Join-Path $PlayerRoot 'engine' }
    return Join-Path $PlayerRoot "qa/$Port/engine"
}

function Copy-HeroScriptVerifiedFile {
    param([string]$Source, [string]$Destination)
    $before = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    if (Test-Path -LiteralPath $Destination) {
        if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $before) {
            throw "Migration conflict; nothing overwritten: $Destination"
        }
        return
    }
    New-Item -ItemType Directory -Path (Split-Path $Destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -ErrorAction Stop
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $before -or
        (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash -ne $before) {
        throw "Source changed or verification failed; originals retained: $Source"
    }
}

function Assert-HeroScriptMigrationIdle {
    param([int]$Port)
    if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
        throw "Close the game/API on port $Port before migration. Originals are unchanged."
    }
}

function Initialize-HeroScriptPlayerData {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$DemoRoot,
          [Parameter(Mandatory)][string]$PlayerRoot,
          [int]$Port = 5271,
          [string]$LegacyPreferences = '')
    $playerPath = [IO.Path]::GetFullPath($PlayerRoot)
    $legacyPath = [IO.Path]::GetFullPath((Join-Path $DemoRoot '.runtime'))
    if ($Port -ne 5271) { $legacyPath = Join-Path $legacyPath "qa-$Port" }
    $target = [IO.Path]::GetFullPath((Get-HeroScriptRuntimeRoot $playerPath $Port))
    $targetPrefix = $playerPath.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $target.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        $target.StartsWith($legacyPath.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        $legacyPath.StartsWith($target.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Migration source and destination must be separate, with destination inside PlayerRoot.'
    }
    $manifestPath = Join-Path $target 'migration-v1.json'
    if (Test-Path -LiteralPath $manifestPath) {
        $previous = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -ErrorAction Stop
        if ($previous.version -ne 1 -or -not $previous.sourceRetained) { throw "Invalid migration manifest; refusing to overwrite: $manifestPath" }
    }
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        if ((Test-Path -LiteralPath $target) -and @(Get-ChildItem -LiteralPath $target -Force).Count -gt 0) {
            throw "Unrecognized existing player data; refusing to merge or overwrite: $target"
        }
        # A live legacy writer would invalidate a multi-file backup even if each
        # individual file happened to be copied atomically.
        if (Test-Path -LiteralPath (Join-Path $legacyPath 'runs')) { Assert-HeroScriptMigrationIdle $Port }
        $parent = Split-Path $target -Parent
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
        $stage = Join-Path $parent ('.migration-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $stage | Out-Null
        $inventory = [Collections.Generic.List[object]]::new()
        foreach ($section in @('runs', 'content', 'telemetry')) {
            $sourceSection = Join-Path $legacyPath $section
            New-Item -ItemType Directory -Path (Join-Path $stage $section) | Out-Null
            if (-not (Test-Path -LiteralPath $sourceSection)) { continue }
            $entries = @(Get-ChildItem -LiteralPath $sourceSection -Recurse -Force)
            if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {
                throw "Migration does not follow links: $sourceSection"
            }
            foreach ($entry in $entries | Where-Object { -not $_.PSIsContainer }) {
                # Entries are already constrained to this source tree; substring
                # also works on Windows PowerShell 5.1 (no Path.GetRelativePath).
                $relative = $entry.FullName.Substring($legacyPath.TrimEnd('\', '/').Length + 1)
                $destination = Join-Path $stage $relative
                Copy-HeroScriptVerifiedFile $entry.FullName $destination
                $inventory.Add([ordered]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash; bytes = $entry.Length })
            }
        }
        foreach ($log in @('engine.log', 'engine-error.log')) {
            $oldLog = Join-Path $legacyPath $log
            if (Test-Path -LiteralPath $oldLog) { Copy-HeroScriptVerifiedFile $oldLog (Join-Path $stage "legacy-logs/$log") }
        }
        $currentCount = 0
        foreach ($section in @('runs', 'content', 'telemetry')) {
            $sourceSection = Join-Path $legacyPath $section
            if (Test-Path -LiteralPath $sourceSection) { $currentCount += @(Get-ChildItem -LiteralPath $sourceSection -Recurse -File -Force).Count }
        }
        if ($currentCount -ne $inventory.Count) { throw "Source inventory changed; verified staging retained: $stage" }
        foreach ($file in $inventory) {
            if ((Get-FileHash -LiteralPath (Join-Path $legacyPath $file.path) -Algorithm SHA256).Hash -ne $file.sha256) {
                throw "Source changed during migration; original and staging retained: $stage"
            }
        }
        $manifest = [ordered]@{ version = 1; completedUtc = [DateTime]::UtcNow.ToString('o'); source = $legacyPath;
            files = @($inventory.ToArray()); sourceRetained = $true; progressPartition = 'playerId + settingId (unchanged)' }
        $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage 'migration-v1.json') -Encoding utf8
        # Same-parent rename publishes the verified directory as one installation.
        # The checked absolute target is under PlayerRoot; never move the source.
        if (Test-Path -LiteralPath $target) {
            # Even an empty pre-existing destination is not deleted implicitly.
            throw "Destination appeared during migration; verified staging retained: $stage"
        }
        Move-Item -LiteralPath $stage -Destination $target -ErrorAction Stop
    }
    if ($Port -eq 5271 -and -not [string]::IsNullOrWhiteSpace($LegacyPreferences)) {
        foreach ($fileName in @('heroscript_showcase.cfg', 'card_animation_profile.json')) {
            $oldFile = Join-Path (Split-Path $LegacyPreferences -Parent) $fileName
            $newFile = Join-Path $playerPath $fileName
            if ((Test-Path -LiteralPath $oldFile) -and -not (Test-Path -LiteralPath $newFile)) {
                Copy-HeroScriptVerifiedFile $oldFile $newFile
            }
        }
    }
    return $target
}

function Test-HeroScriptStorageBinding {
    param([string]$RuntimeRoot, [int]$Port)
    $registration = Join-Path $RuntimeRoot 'launcher-session.json'
    if (-not (Test-Path -LiteralPath $registration)) { return $false }
    try {
        $saved = Get-Content -LiteralPath $registration -Raw | ConvertFrom-Json
        $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop | Select-Object -First 1
        $process = Get-Process -Id $listener.OwningProcess -ErrorAction Stop
        return $saved.runtimeRoot -eq [IO.Path]::GetFullPath($RuntimeRoot) -and
            $saved.processId -eq $process.Id -and $saved.startedUtc -eq $process.StartTime.ToUniversalTime().ToString('o')
    } catch { return $false }
}

function Save-HeroScriptStorageBinding {
    param([string]$RuntimeRoot, [int]$Port)
    $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop | Select-Object -First 1
    $process = Get-Process -Id $listener.OwningProcess -ErrorAction Stop
    @{runtimeRoot = [IO.Path]::GetFullPath($RuntimeRoot); processId = $process.Id;
      startedUtc = $process.StartTime.ToUniversalTime().ToString('o')} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $RuntimeRoot 'launcher-session.json') -Encoding utf8
}
