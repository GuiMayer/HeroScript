param(
    [string[]]$Tests = @('ui_contracts','layers','card_presentation','card_modifier_items','card_preview_separation','card_refresh_stability','animation_contracts','animation_responsiveness','card_motion_polish','presentation_envelope','presentation_planner','presentation_director','animation_live','semantic_cues','card_zone_motion','turn_hand_cycle','motion_policy','historical_presentation','animation_limits','settings_contracts','settings_interaction','settings_recovery','audio_contracts','replay_transport','replay_progressive','connection_epoch','run_graphics_updates','run_visual_contexts','combat_collaborators','map_geometry','map_graph_view','run_graphics_architecture','activity_reconciliation','map_selection','dialogue_contracts','dialogue_coordination','dialogue_reading','activity_ingress','ivory_run_contracts','hand_selection','reference_hand_geometry','card_collections'),
    [string]$ApiUrl = '',
    [string]$VisualTest = '',
    [string]$VisualOutputDirectory = '',
    [string]$VisualPhase = '',
    [ValidatePattern('^$|^[0-9]+x[0-9]+$')][string]$VisualResolution = '',
    [string]$ReuseProject = '',
    [switch]$VerboseGodot,
    [int]$TimeoutSeconds = 180,
    [string]$Godot = 'C:\Program Files (x86)\Steam\steamapps\common\Godot Engine\godot.windows.opt.tools.64.exe'
)
$ErrorActionPreference = 'Stop'
if (-not $PSBoundParameters.ContainsKey('Tests')) { $Tests += @('http_transport_pool', 'compact_session', 'rest_transport_latency') }
$workspace = Split-Path $PSScriptRoot -Parent
$source = Join-Path $workspace 'examples/godot-engine-showcase'
$taskId = [Guid]::NewGuid().ToString('N')
$project = Join-Path $workspace ".runtime/ui-refactor/$taskId"
if ($ReuseProject) {
    $project = [IO.Path]::GetFullPath($ReuseProject)
    $isolatedRoot = [IO.Path]::GetFullPath((Join-Path $workspace '.runtime/ui-refactor')) + [IO.Path]::DirectorySeparatorChar
    if (-not $project.StartsWith($isolatedRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath (Join-Path $project '.godot/global_script_class_cache.cfg')) -or
        -not ([IO.File]::ReadAllText((Join-Path $project 'project.godot')).Contains('HeroScript-ui-refactor-'))) {
        throw 'ReuseProject must identify an already-imported isolated UI test copy.'
    }
    $taskId = Split-Path $project -Leaf
}
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
if ($VisualTest) {
    if (-not $VisualOutputDirectory) { throw 'VisualOutputDirectory is required for a rendered test.' }
    $VisualOutputDirectory = [IO.Path]::GetFullPath($VisualOutputDirectory)
    New-Item -ItemType Directory -Path $VisualOutputDirectory -Force | Out-Null
}
$testNames = @('import') + $Tests
if ($ReuseProject) { $testNames = $Tests }
if ($VisualTest) { $testNames += $VisualTest }
foreach ($test in $testNames) {
    $stdout = Join-Path $project "$test.stdout.log"
    $stderr = Join-Path $project "$test.stderr.log"
    $arguments = @('--headless','--path',"`"$project`"")
    if ($VerboseGodot) { $arguments += '--verbose' }
    if ($test -eq $VisualTest) {
        $arguments = @('--path',"`"$project`"",'--audio-driver','Dummy','--script',"res://tests/$test.gd",'--','--layout-smoke',"`"--lab-output=$VisualOutputDirectory`"")
        if ($VisualPhase) { $arguments += "--capture-phase=$VisualPhase" }
        if ($VisualResolution) { $arguments = @('--resolution',$VisualResolution) + $arguments + "--capture-resolution=$VisualResolution" }
    }
    elseif ($test -eq 'import') { $arguments += @('--editor','--import','--quit') }
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
