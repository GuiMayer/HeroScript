$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repository 'examples/godot-engine-showcase/scripts/launcher/server_build.ps1')
function Write-StartupProgress([string]$Phase) { }
$fixture = Join-Path $repository '.runtime/server-build-integration'
foreach ($path in @('src/API', 'data/configs', 'docs/schemas', 'runtime')) {
    New-Item -ItemType Directory -Path (Join-Path $fixture $path) -Force | Out-Null
}
$project = Join-Path $fixture 'src/API/API.csproj'
$source = Join-Path $fixture 'src/API/Program.cs'
[IO.File]::WriteAllText($project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
[IO.File]::WriteAllText($source, 'System.Console.WriteLine("fixture");')
$build = Join-Path $fixture 'runtime/build'
$runtime = Join-Path $fixture 'runtime'
$manifest = Join-Path $runtime 'server-build-v1.json'
if (Test-Path -LiteralPath $manifest) { Remove-Item -LiteralPath $manifest }
$binary = Get-HeroScriptServerBinary $fixture $project $build $runtime
$stamp = (Get-Item -LiteralPath $binary).LastWriteTimeUtc
$again = Get-HeroScriptServerBinary $fixture $project $build $runtime
if ($again -ne $binary -or (Get-Item -LiteralPath $binary).LastWriteTimeUtc -ne $stamp) { throw 'Unchanged inputs did not reuse the build.' }
[IO.File]::WriteAllText($source, 'this is not valid C#')
$failed = $false
try { Get-HeroScriptServerBinary $fixture $project $build $runtime | Out-Null } catch { $failed = $true }
if (-not $failed) { throw 'Changed invalid source unexpectedly reused a stale build.' }
Write-Output 'PASS first build, cached reuse and changed source failure'
