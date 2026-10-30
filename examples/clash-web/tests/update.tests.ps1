$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('clash-update-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'Update-HeroScript.ps1') -Destination $fixture
function Assert-That($condition, $message) { if (-not $condition) { throw $message } }
function Write-Json($path, $value) { $value | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $path -Encoding UTF8 }
function Must-Reject($operation, $message) {
    $rejected = $false
    try { & $operation } catch { $rejected = $true }
    Assert-That $rejected $message
}
New-Item -ItemType Directory -Path (Join-Path $fixture 'heroscript'),(Join-Path $fixture 'content') | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture 'heroscript/obsolete.dll'), 'old')
[IO.File]::WriteAllText((Join-Path $fixture 'heroscript/API.exe'), 'old executable')
[IO.File]::WriteAllText((Join-Path $fixture 'content/rules.json'), '{"rule":"keep"}')
[IO.File]::WriteAllText((Join-Path $fixture 'index.html'), 'keep interface')
$id = 'clash-update-test-' + [Guid]::NewGuid().ToString('N')
$data = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) $id
New-Item -ItemType Directory -Path $data | Out-Null
$save = Join-Path $data 'save.txt'
[IO.File]::WriteAllText($save, 'keep save')
$manifestPath = Join-Path $fixture 'heroscript.json'
Write-Json $manifestPath @{ projectId=$id; schemaVersion=1; projectVersion='9.9.9'; runtimeVersion='0.1.0'; contentDirectory='content'; clientEntry='index.html'; custom=@{keep='yes'}; runtimePackage=@{repository='GuiMayer/HeroScript';sha256=('a'*64)} }
$package = Join-Path $fixture 'package'
New-Item -ItemType Directory -Path (Join-Path $package 'heroscript'),(Join-Path $package 'content') | Out-Null
Write-Json (Join-Path $package 'heroscript/runtime-manifest.json') @{version='0.2.0'; platform='win-x64'}
[IO.File]::WriteAllText((Join-Path $package 'heroscript/API.exe'), 'new executable')
[IO.File]::WriteAllText((Join-Path $package 'Start-HeroScript.ps1'), '# published launcher')
[IO.File]::WriteAllText((Join-Path $package 'content/rules.json'), 'must never replace rules')
[IO.File]::WriteAllText((Join-Path $package 'heroscript.json'), 'must never replace project config')
$archive = Join-Path $fixture 'heroscript-runtime-v0.2.0-win-x64.zip'
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $archive
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content ([IO.Path]::ChangeExtension($archive, '.sha256'))
$before = (Get-FileHash $manifestPath).Hash
Must-Reject { & "$fixture/Update-HeroScript.ps1" -Version 0.2.0 -Archive $archive -Sha256 ('b'*64) } 'Invalid checksum accepted'
Must-Reject { & "$fixture/Update-HeroScript.ps1" -Version 0.3.0 -Archive $archive -Sha256 $hash } 'Wrong version accepted'
$lock = [IO.File]::Open((Join-Path $data 'runtime.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try { Must-Reject { & "$fixture/Update-HeroScript.ps1" -Version 0.2.0 -Archive $archive } 'Running game accepted' } finally { $lock.Dispose() }
Assert-That ((Get-FileHash $manifestPath).Hash -eq $before) 'Failed update changed manifest'
Assert-That (Test-Path "$fixture/heroscript/obsolete.dll") 'Failed update changed runtime'
& "$fixture/Update-HeroScript.ps1" -Version 0.2.0 -Archive $archive
$after = Get-Content $manifestPath -Raw | ConvertFrom-Json
Assert-That ($after.runtimeVersion -eq '0.2.0' -and $after.runtimePackage.sha256 -eq $hash) 'Manifest not updated'
Assert-That ($after.projectVersion -eq '9.9.9' -and $after.custom.keep -eq 'yes') 'Project config overwritten'
Assert-That (-not (Test-Path "$fixture/heroscript/obsolete.dll")) 'Old files remained in replacement'
Assert-That ((Get-Content "$fixture/heroscript/API.exe" -Raw) -eq 'new executable') 'Wrong replacement'
Assert-That ((Get-Content "$fixture/content/rules.json" -Raw) -eq '{"rule":"keep"}') 'Content changed'
Assert-That ((Get-Content "$fixture/index.html" -Raw) -eq 'keep interface') 'Interface changed'
Assert-That ((Get-Content $save -Raw) -eq 'keep save') 'Save changed'

# Force failure during the manifest swap and require the previous runtime to return.
[IO.File]::WriteAllText((Join-Path $fixture 'heroscript/API.exe'), 'previous generation')
$attributes = [IO.File]::GetAttributes($manifestPath)
try {
    [IO.File]::SetAttributes($manifestPath, ($attributes -bor [IO.FileAttributes]::ReadOnly))
    Must-Reject { & "$fixture/Update-HeroScript.ps1" -Version 0.2.0 -Archive $archive -Sha256 $hash } 'Read-only manifest should fail'
} finally { [IO.File]::SetAttributes($manifestPath, $attributes) }
Assert-That ((Get-Content "$fixture/heroscript/API.exe" -Raw) -eq 'previous generation') 'Rollback lost prior runtime'
Assert-That ((Get-Content $manifestPath -Raw | ConvertFrom-Json).runtimeVersion -eq '0.2.0') 'Rollback changed manifest'
Write-Host 'PASS: replacement, manifest, content/UI/saves preservation, checksum/version, running-game refusal and rollback.'
Write-Host "Test fixtures: $fixture"
