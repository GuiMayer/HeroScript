# A build can be reused only when source inputs, SDK and every output byte match.
function Get-HeroScriptBuildFingerprint {
    param([string]$RepositoryRoot, [string]$BuildRoot, [switch]$Outputs)
    $paths = [Collections.Generic.List[string]]::new()
    if ($Outputs) {
        $outputRoot = Join-Path $BuildRoot 'bin'
        if (-not (Test-Path -LiteralPath $outputRoot)) { return $null }
        $roots = @($outputRoot, (Join-Path $BuildRoot 'obj'))
    } else {
        $roots = @((Join-Path $RepositoryRoot 'src'), (Join-Path $RepositoryRoot 'data/configs'), (Join-Path $RepositoryRoot 'docs/schemas'))
        foreach ($name in @('global.json', 'NuGet.Config', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')) {
            $file = Join-Path $RepositoryRoot $name
            if (Test-Path -LiteralPath $file -PathType Leaf) { $paths.Add($file) }
        }
    }
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { return $null }
        $all = Get-ChildItem -LiteralPath $root -Recurse -Force -ErrorAction Stop
        if (@($all | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { return $null }
        foreach ($item in $all) {
            if ($item.PSIsContainer) { continue }
            if (-not $Outputs -and $item.FullName -match '[\\/](bin|obj)[\\/]') { continue }
            if ($Outputs -and $root -like '*obj' -and $item.Name -ne 'project.assets.json') { continue }
            $paths.Add($item.FullName)
        }
    }
    $entries = [Collections.Generic.List[string]]::new()
    foreach ($file in ($paths | Sort-Object -CaseSensitive)) {
        $relative = if ($Outputs) { $file.Substring($BuildRoot.TrimEnd([char[]]'\/').Length + 1) } else { $file.Substring($RepositoryRoot.TrimEnd([char[]]'\/').Length + 1) }
        $algorithm = [Security.Cryptography.SHA256]::Create()
        $stream = [IO.File]::OpenRead($file)
        try { $fileHash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $algorithm.Dispose() }
        $entries.Add($relative.Replace('\', '/') + ':' + $fileHash)
    }
    if (-not $Outputs) {
        $entries.Add('sdk:' + ((& dotnet --version | Out-String).Trim()))
        if ($LASTEXITCODE -ne 0) { return $null }
        foreach ($item in (Get-ChildItem Env: | Where-Object { $_.Name -match '^(DOTNET_|NUGET_|MSBUILD|Configuration$)' } | Sort-Object Name)) {
            $entries.Add('env:' + $item.Name + '=' + $item.Value)
        }
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($entries -join "`n"))
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($bytes)).Replace('-', '') }
    finally { $algorithm.Dispose() }
}

function Get-HeroScriptServerBinary {
    param([string]$RepositoryRoot, [string]$ApiProject, [string]$BuildRoot, [string]$RuntimeRoot)
    $binary = Join-Path $BuildRoot 'bin/API/release/API.dll'
    $manifest = Join-Path $RuntimeRoot 'server-build-v1.json'
    $before = Get-HeroScriptBuildFingerprint $RepositoryRoot $BuildRoot
    if ($before -and (Test-Path -LiteralPath $binary) -and (Test-Path -LiteralPath $manifest)) {
        try {
            $saved = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
            $outputs = Get-HeroScriptBuildFingerprint $RepositoryRoot $BuildRoot -Outputs
            if ($saved.version -eq 1 -and $saved.source -eq $before -and $outputs -and $saved.outputs -eq $outputs) {
                Write-Host 'Reusing verified HeroScript server build.' -ForegroundColor Green
                return $binary
            }
        } catch { Write-Warning "Build cache ignored: $_" }
    }
    Write-StartupProgress 'building_server'
    & dotnet build $ApiProject --configuration Release --artifacts-path $BuildRoot | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $binary)) { throw 'HeroScript server build failed.' }
    $after = Get-HeroScriptBuildFingerprint $RepositoryRoot $BuildRoot
    $outputs = Get-HeroScriptBuildFingerprint $RepositoryRoot $BuildRoot -Outputs
    if ($before -and $after -eq $before -and $outputs) {
        $temporary = "$manifest.$PID.tmp"
        $entry = @{ version = 1; source = $after; outputs = $outputs } | ConvertTo-Json -Compress
        [IO.File]::WriteAllText($temporary, $entry, [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $manifest -Force
    }
    return $binary
}
