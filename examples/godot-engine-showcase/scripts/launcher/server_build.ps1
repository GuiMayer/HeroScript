# A build can be reused only when source inputs, SDK and every output byte match.
function Get-HeroScriptBuildFingerprint {
    param([string]$RepositoryRoot, [string]$BuildRoot, [switch]$Outputs)
    if (-not ('HeroScriptBuildFingerprint' -as [type])) {
        Add-Type -Path (Join-Path $PSScriptRoot 'BuildFingerprint.cs')
    }
    $sdk = ''
    $environment = @()
    if (-not $Outputs) {
        $sdk = ((& dotnet --version | Out-String).Trim())
        if ($LASTEXITCODE -ne 0) { return $null }
        $environment = @(Get-ChildItem Env: | Where-Object { $_.Name -match '^(DOTNET_|NUGET_|MSBUILD|Configuration$)' } |
            ForEach-Object { $_.Name + '=' + $_.Value })
    }
    return [HeroScriptBuildFingerprint]::Compute($RepositoryRoot, $BuildRoot, [bool]$Outputs, $sdk, [string[]]$environment)
}

function Get-HeroScriptServerBinary {
    param([string]$RepositoryRoot, [string]$ApiProject, [string]$BuildRoot, [string]$RuntimeRoot)
    $binary = Join-Path $BuildRoot 'bin/API/release/API.dll'
    $manifest = Join-Path $RuntimeRoot 'server-build-v1.json'
    try { $before = Get-HeroScriptBuildFingerprint $RepositoryRoot $BuildRoot }
    catch { $before = $null }
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
    try {
        $after = Get-HeroScriptBuildFingerprint $RepositoryRoot $BuildRoot
        $outputs = Get-HeroScriptBuildFingerprint $RepositoryRoot $BuildRoot -Outputs
    } catch { $after = $null; $outputs = $null }
    if ($before -and $after -eq $before -and $outputs) {
        $temporary = "$manifest.$PID.tmp"
        $entry = @{ version = 1; source = $after; outputs = $outputs } | ConvertTo-Json -Compress
        [IO.File]::WriteAllText($temporary, $entry, [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $manifest -Force
    }
    return $binary
}
