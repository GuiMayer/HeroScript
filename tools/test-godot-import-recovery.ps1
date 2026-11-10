param([string]$Workspace = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$qaRoot = Join-Path $Workspace ('.runtime/import-recovery-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $qaRoot | Out-Null
$sourcePath = Join-Path $qaRoot 'MockGodot.cs'
$fakePath = Join-Path $qaRoot 'MockGodot.exe'
$markerPath = Join-Path $qaRoot 'first-attempt.txt'
@"
using System;
using System.IO;
class MockGodot {
    static int Main() {
        string marker = Environment.GetEnvironmentVariable("HEROSCRIPT_IMPORT_TEST_MARKER");
        if (Environment.GetEnvironmentVariable("HEROSCRIPT_IMPORT_TEST_ALWAYS") == "1") {
            File.AppendAllText(marker, "x"); return unchecked((int)0xC0000005);
        }
        if (!File.Exists(marker)) { File.WriteAllText(marker, "crashed"); return unchecked((int)0xC0000005); }
        Console.WriteLine("Mock clean import completed.");
        return 0;
    }
}
"@ | Set-Content -LiteralPath $sourcePath -Encoding UTF8
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe "/out:$fakePath" $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Cannot build isolated import crash simulator.' }
$env:HEROSCRIPT_IMPORT_TEST_MARKER = $markerPath
try {
    $output = @(& (Join-Path $Workspace 'tools/test-godot-ui.ps1') -Tests @() -Godot $fakePath -TimeoutSeconds 30 6>&1)
    $reportLine = @($output | Where-Object { "$_" -like 'Validation: *' })[-1]
    if (-not $reportLine) { throw 'Retry did not publish a validation report.' }
    $reportPath = ("$reportLine").Substring('Validation: '.Length)
    $report = @(Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json)
    $crash = @($report | Where-Object { $_.test -eq 'import_crashed_attempt' })
    $success = @($report | Where-Object { $_.test -eq 'import' -and $_.exit_code -eq 0 })
    if ($crash.Count -ne 1 -or $success.Count -ne 1) { throw 'Validation report lost the failure or clean retry.' }
    $prior = ($crash[0].diagnostics[0] -split 'original logs: ',2)[1]
    if (-not (Test-Path -LiteralPath (Join-Path $prior 'import.stdout.log')) -or
        -not (Test-Path -LiteralPath (Join-Path $prior 'validation.json')) -or
        [IO.Path]::GetFullPath($prior) -eq [IO.Path]::GetFullPath((Split-Path $reportPath))) {
        throw 'The failed import and its logs were not kept in a separate project.'
    }
    Write-Host '[PASS] Native crash triggers exactly one clean retry in another project and keeps the original logs.'
    Remove-Item -LiteralPath $markerPath
    $env:HEROSCRIPT_IMPORT_TEST_ALWAYS = '1'
    $rejected = $false
    try { & (Join-Path $Workspace 'tools/test-godot-ui.ps1') -Tests @() -Godot $fakePath -TimeoutSeconds 30 6>&1 | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected -or (Get-Content -LiteralPath $markerPath -Raw) -ne 'xx') {
        throw 'Repeated native crash must fail after exactly two independent attempts.'
    }
    Write-Host '[PASS] Repeated native crash fails after the bounded retry.'
} finally {
    Remove-Item Env:HEROSCRIPT_IMPORT_TEST_MARKER -ErrorAction SilentlyContinue
    Remove-Item Env:HEROSCRIPT_IMPORT_TEST_ALWAYS -ErrorAction SilentlyContinue
}
