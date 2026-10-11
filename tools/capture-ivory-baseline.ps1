param(
    [string]$Godot = 'C:\Program Files (x86)\Steam\steamapps\common\Godot Engine\godot.windows.opt.tools.64.exe',
    [string]$OutputDirectory = '',
    [switch]$Headless
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$source = Join-Path $workspace 'examples/godot-engine-showcase'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $workspace 'docs/design/interface-baseline/2026-10-11' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$taskId = [Guid]::NewGuid().ToString('N')
$project = Join-Path $workspace ".runtime/ivory-baseline/$taskId"
New-Item -ItemType Directory -Path $project, $output -Force | Out-Null

# Generated isolated checkout: no changes to the player's project or user:// files.
Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/]\.godot[\\/]|[\\/]\.runtime[\\/]|[\\/]tests[\\/]output[\\/]'
} | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($source, $_.FullName)
    $destination = Join-Path $project $relative
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $destination
}
$configPath = Join-Path $project 'project.godot'
$config = [IO.File]::ReadAllText($configPath).Replace('[application]', "[application]`nconfig/use_custom_user_dir=true`nconfig/custom_user_dir_name=`"HeroScript-ivory-baseline-$taskId`"")
[IO.File]::WriteAllText($configPath, $config)

function Invoke-BaselineProcess([string]$Name, [string[]]$Arguments) {
    $stdout = Join-Path $project "$Name.stdout.log"
    $stderr = Join-Path $project "$Name.stderr.log"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $Godot -ArgumentList $Arguments -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $watch.Stop()
    $lines = @(Get-Content -LiteralPath $stdout) + @(Get-Content -LiteralPath $stderr)
    $result = [ordered]@{name=$Name; exit_code=$process.ExitCode; elapsed_ms=$watch.ElapsedMilliseconds;
        pass_count=@($lines | Where-Object { $_ -match '^\[PASS\]' }).Count;
        diagnostics=@($lines | Where-Object { $_ -match 'WARNING:|ERROR:|SCRIPT ERROR:|\[FAIL\]|leaked|still in use' })}
    Write-Host "$Name`: exit=$($result.exit_code), checks=$($result.pass_count), elapsed=$($result.elapsed_ms)ms"
    if ($process.ExitCode -ne 0) { $lines | Select-Object -Last 18 | ForEach-Object { Write-Host $_ } }
    return $result
}

$results = [Collections.Generic.List[object]]::new()
$results.Add((Invoke-BaselineProcess 'import' @('--headless','--editor','--path',"`"$project`"",'--import','--quit')))
if ($results[0].exit_code -ne 0) { throw 'The isolated project could not be imported.' }
foreach ($test in @('layers','card_preview_separation','card_presentation','card_hand_lab','hand_ux','pause_layers','combat_stage_layout','card_play_selection','resolutions')) {
    $results.Add((Invoke-BaselineProcess $test @('--headless','--path',"`"$project`"",'--script',"res://tests/$test.gd",'--','--layout-smoke')))
}
$captureArguments = @('--path',"`"$project`"",'--audio-driver','Dummy','--script','res://tests/visual_baseline.gd')
if ($Headless) { $captureArguments += '--headless' }
$captureArguments += @('--','--layout-smoke',"`"--baseline-output=$output`"")
$results.Add((Invoke-BaselineProcess 'visual_baseline' $captureArguments))

$inventory = foreach ($file in Get-ChildItem -LiteralPath (Join-Path $source 'scripts/ui') -Filter '*.gd') {
    $text = [IO.File]::ReadAllText($file.FullName)
    [ordered]@{file=[IO.Path]::GetRelativePath($workspace,$file.FullName).Replace('\','/');
        color_constructor_count=[regex]::Matches($text,'Color\(').Count;
        embedded_color_count=[regex]::Matches($text,'\[color=|font_color=|#[0-9a-fA-F]{6,8}').Count;
        theme_override_count=[regex]::Matches($text,'add_theme_\w+_override\(').Count;
        app_theme_references=[regex]::Matches($text,'AppTheme\.').Count}
}
$report = [ordered]@{source_revision=(& git -C $workspace rev-parse HEAD); captured_at_utc=[DateTime]::UtcNow.ToString('o');
    isolated_project=$project; isolation='Custom user directory in generated project; no live API, player preferences or run commands';
    results=$results; ui_inventory=@($inventory); rest_validation='Not run: offline fixture baseline, not an engine integration test'}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'validation.json') -Encoding utf8
if (@($results | Where-Object { $_.exit_code -ne 0 }).Count) { throw "Baseline failed; see $project and validation.json." }
Write-Host "BASELINE_READY=$output"
