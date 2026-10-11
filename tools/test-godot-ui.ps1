param(
    [string[]]$Tests = @('ui_contracts','layers','card_presentation','card_preview_separation'),
    [string]$ApiUrl = '',
    [int]$TimeoutSeconds = 180,
    [string]$Godot = 'C:\Program Files (x86)\Steam\steamapps\common\Godot Engine\godot.windows.opt.tools.64.exe'
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$source = Join-Path $workspace 'examples/godot-engine-showcase'
$taskId = [Guid]::NewGuid().ToString('N')
$project = Join-Path $workspace ".runtime/ui-refactor/$taskId"
New-Item -ItemType Directory -Path $project -Force | Out-Null
Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/]\.godot[\\/]|[\\/]\.runtime[\\/]|[\\/]tests[\\/]output[\\/]'
} | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($source, $_.FullName)
    $destination = Join-Path $project $relative
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $destination
}
$configPath = Join-Path $project 'project.godot'
$config = [IO.File]::ReadAllText($configPath).Replace('[application]', "[application]`nconfig/use_custom_user_dir=true`nconfig/custom_user_dir_name=`"HeroScript-ui-refactor-$taskId`"")
[IO.File]::WriteAllText($configPath, $config)
$results = [Collections.Generic.List[object]]::new()
foreach ($test in @('import') + $Tests) {
    $stdout = Join-Path $project "$test.stdout.log"
    $stderr = Join-Path $project "$test.stderr.log"
    $arguments = @('--headless','--path',"`"$project`"")
    if ($test -eq 'import') { $arguments += @('--editor','--import','--quit') }
    else { $arguments += @('--script',"res://tests/$test.gd",'--','--layout-smoke') }
    if ($ApiUrl -and $test -ne 'import') { $arguments += "--api-url=$ApiUrl" }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $Godot -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id
        throw "Timeout in $test; logs: $project"
    }
    $process.WaitForExit()
    $lines = @(Get-Content -LiteralPath $stdout) + @(Get-Content -LiteralPath $stderr)
    # Godot reports retained process-wide script resources as an ERROR at exit.
    # Keep that diagnostic in the report, separate from functional/script/RID
    # errors. Certification must inspect it, not infer clean teardown from exit 0.
    $errors = @($lines | Where-Object { $_ -match 'ERROR:|Parse Error:|\[FAIL\]' -and $_ -notmatch '^ERROR: \d+ resources still in use at exit' })
    $result = [ordered]@{test=$test;exit_code=$process.ExitCode;checks=@($lines | Where-Object { $_ -match '^\[PASS\]' }).Count;
        elapsed_ms=$watch.ElapsedMilliseconds;diagnostics=@($lines | Where-Object { $_ -match 'WARNING:|ERROR:|leaked|still in use|\[FAIL\]' })}
    $results.Add($result)
    Write-Host "$test : exit=$($result.exit_code), checks=$($result.checks), elapsed=$($result.elapsed_ms)ms"
    if ($process.ExitCode -ne 0 -or $errors.Count -gt 0) {
        $lines | Select-Object -Last 45 | ForEach-Object { Write-Host $_ }
        $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $project 'validation.json')
        throw "Validation failed: $test; logs: $project"
    }
}
$results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $project 'validation.json')
Write-Host "Validation: $project/validation.json"
