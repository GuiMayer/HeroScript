param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [ValidateRange(1, 100)][int]$Iterations = 12
)
$ErrorActionPreference = 'Stop'
# Use an isolated test server/store: this creates a real run and durable commits.
$samples = [Collections.Generic.List[object]]::new()
function Invoke-InscriptionProbe([string]$Operation, [string]$Path, $Body = $null) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    if ($null -eq $Body) { $value = Invoke-RestMethod -Uri ($BaseUrl.TrimEnd('/') + $Path) -TimeoutSec 120 }
    else { $value = Invoke-RestMethod -Method Post -Uri ($BaseUrl.TrimEnd('/') + $Path) -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 30 -Compress) -TimeoutSec 120 }
    $watch.Stop()
    $samples.Add([pscustomobject]@{operation=$Operation;milliseconds=$watch.Elapsed.TotalMilliseconds})
    return $value
}
$settings = Invoke-InscriptionProbe 'catalog' '/api/v1/content/settings'
$setting = $settings.items | Where-Object settingId -EQ 'ascendant' | Select-Object -First 1
if ($null -eq $setting) { throw 'Ascendant setting is unavailable' }
$actor = 'inscription-probe-' + [Guid]::NewGuid().ToString('N')
$run = Invoke-InscriptionProbe 'create' '/api/v1/runs' @{settingId='ascendant';modeId='ascendant_world_one';runDefinitionId='ascendant_world_one_run';playerEntityId=$actor;seed=20261015;contentRevision=$setting.currentRevision}
$path = '/api/v1/runs/' + $run.runId
$snapshot = Invoke-InscriptionProbe 'read' ($path + '/loadout?actorId=' + $actor)
$item = $snapshot.items | Where-Object { $_.definition.inscriptionId -eq 'w1_focus' } | Select-Object -First 1
for ($index = 0; $index -lt $Iterations; $index++) {
    $draft = @{}
    if ($index % 2 -eq 0) { $draft['trace_1'] = $item.instance.instanceId }
    $null = Invoke-InscriptionProbe 'preview' ($path + '/loadout/preview') @{actorId=$actor;assignments=$draft;expectedSequence=$snapshot.runSequence;expectedStep=$snapshot.expectedStep}
    $null = Invoke-InscriptionProbe 'apply' ($path + '/commands') @{commandId=[Guid]::NewGuid().ToString();type='APPLY_LOADOUT';expectedSequence=$snapshot.runSequence;expectedStep=$snapshot.expectedStep;payload=@{actorId=$actor;assignments=$draft}}
    $null = Invoke-InscriptionProbe 'session' ($path + '/session')
    $snapshot = Invoke-InscriptionProbe 'read' ($path + '/loadout?actorId=' + $actor)
}
$summary = foreach ($group in $samples | Group-Object operation) {
    $sorted = @($group.Group.milliseconds | Sort-Object)
    [pscustomobject]@{operation=$group.Name;count=$sorted.Count;p50ms=[Math]::Round($sorted[[int][Math]::Floor(($sorted.Count-1)*0.5)],2);p95ms=[Math]::Round($sorted[[int][Math]::Ceiling(($sorted.Count-1)*0.95)],2);maxMs=[Math]::Round($sorted[-1],2)}
}
[pscustomobject]@{baseUrl=$BaseUrl;runId=$run.runId;iterations=$Iterations;summary=@($summary);samples=@($samples)} | ConvertTo-Json -Depth 8
