$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repository 'examples/godot-engine-showcase/scripts/launcher/server_build.ps1')
$fixture = Join-Path $repository '.runtime/server-build-fingerprint-test'
foreach ($path in @('src/API', 'data/configs', 'docs/schemas', 'build/bin/API/release', 'build/obj/API')) {
    New-Item -ItemType Directory -Path (Join-Path $fixture $path) -Force | Out-Null
}
Set-Content -LiteralPath (Join-Path $fixture 'src/API/Program.cs') -Value 'source one'
Set-Content -LiteralPath (Join-Path $fixture 'data/configs/rules.json') -Value '{}'
Set-Content -LiteralPath (Join-Path $fixture 'docs/schemas/schema.json') -Value '{}'
Set-Content -LiteralPath (Join-Path $fixture 'build/bin/API/release/API.dll') -Value 'output one'
Set-Content -LiteralPath (Join-Path $fixture 'build/obj/API/project.assets.json') -Value '{}'
$build = Join-Path $fixture 'build'
$source = Get-HeroScriptBuildFingerprint $fixture $build
if ($source -ne (Get-HeroScriptBuildFingerprint $fixture $build)) { throw 'Source fingerprint is not stable.' }
Set-Content -LiteralPath (Join-Path $fixture 'data/configs/rules.json') -Value '{"changed":true}'
if ($source -eq (Get-HeroScriptBuildFingerprint $fixture $build)) { throw 'Content modification did not invalidate the build.' }
$changedContent = Get-HeroScriptBuildFingerprint $fixture $build
Set-Content -LiteralPath (Join-Path $fixture 'src/API/Program.cs') -Value 'source two'
if ($changedContent -eq (Get-HeroScriptBuildFingerprint $fixture $build)) { throw 'Source modification did not invalidate the build.' }
$project = Join-Path $fixture 'src/API/API.csproj'
Set-Content -LiteralPath $project -Value '<Project><Import Project="../../external.props" /></Project>'
if ($null -ne (Get-HeroScriptBuildFingerprint $fixture $build)) { throw 'External imports must disable build reuse.' }
Remove-Item -LiteralPath $project
$output = Get-HeroScriptBuildFingerprint $fixture $build -Outputs
Set-Content -LiteralPath (Join-Path $fixture 'build/bin/API/release/API.dll') -Value 'output two'
if ($output -eq (Get-HeroScriptBuildFingerprint $fixture $build -Outputs)) { throw 'Output tampering was not detected.' }
Write-Output 'PASS stable inputs, changed content and altered output'
