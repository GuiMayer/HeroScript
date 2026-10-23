param([switch]$Check)
$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path $PSScriptRoot -Parent) 'examples/godot-engine-showcase'
$assetRoot = Join-Path $project 'assets/programmer-art'
# Authoring-time vector recipes. No randomness or game rules run in the client.
# Coordinates inherit the 320 x 150 card artwork canvas already used by ivory.
$shapes = [ordered]@{
    strike = '<path fill="#20201c" d="M109 114L177 23H204L136 114Z"/><path fill="#b83e25" d="M146 114L194 50H211L163 114Z"/><path fill="none" stroke="#20201c" stroke-width="4" d="M108 126H195"/>'
    guard = '<path fill="#20201c" d="M111 27H209V80L160 126 111 80Z"/><path fill="#f2ebdd" d="M120 36H200V76L160 114 120 76Z"/><path fill="#b83e25" d="M160 45H191V72L160 102Z"/>'
    fire = '<path fill="#20201c" d="M160 16L198 54V103L177 124H139L118 103V65L140 43V83H153V36Z"/><path fill="#b83e25" d="M166 62L183 79V101L172 112H150L140 102V88H154V100H166Z"/>'
    recovery = '<path fill="#20201c" d="M145 25H175V59H209V89H175V123H145V89H111V59H145Z"/><path fill="#b83e25" d="M153 33H167V67H201V81H167V115H153V81H119V67H153Z"/>'
    energy = '<path fill="#20201c" d="M168 16H197L169 62H196L144 132H123L151 86H124Z"/><path fill="#b83e25" d="M170 33H180L153 74H173L143 116H140L164 74H145Z"/>'
    condense = '<circle cx="160" cy="75" r="26" fill="#20201c"/><path fill="#b83e25" d="M160 62L173 75 160 88 147 75Z"/><path fill="none" stroke="#20201c" stroke-width="4" d="M107 22L133 48M187 102L213 128M107 128L133 102M187 48L213 22"/><path fill="#20201c" d="M121 48H133V36ZM187 36V48H199ZM121 102H133V114ZM187 114V102H199Z"/>'
    poison = '<path fill="none" stroke="#20201c" stroke-width="4" d="M126 22H194M140 22V57L115 103V119H205V103L180 57V22"/><path fill="#20201c" d="M136 80H184L197 106V111H123V106Z"/><circle cx="151" cy="96" r="6" fill="#b83e25"/><circle cx="171" cy="92" r="4" fill="#f2ebdd"/>'
    multihit = '<path fill="#20201c" d="M107 112L151 28H170L126 112ZM142 112L186 28H205L161 112Z"/><path fill="#b83e25" d="M177 112L212 46H225L190 112Z"/>'
    precision = '<circle cx="160" cy="75" r="39" fill="none" stroke="#20201c" stroke-width="4"/><path fill="none" stroke="#20201c" stroke-width="4" d="M160 19V48M160 102V131M104 75H133M187 75H216"/><path fill="#b83e25" d="M160 62L173 75 160 88 147 75Z"/>'
    recall = '<path fill="none" stroke="#20201c" stroke-width="4" d="M130 42H190L207 59V95L190 112H130L113 95V59"/><path fill="#20201c" d="M112 32L137 57H112Z"/><path fill="#b83e25" d="M146 60H175V92H146Z"/><path fill="none" stroke="#f2ebdd" stroke-width="2" d="M151 68H170M151 74H166"/>'
    charge = '<path fill="none" stroke="#20201c" stroke-width="4" d="M116 35H204V115H116Z"/><path fill="#20201c" d="M128 92H144V103H128ZM152 71H168V103H152Z"/><path fill="#b83e25" d="M176 48H192V103H176Z"/>'
    release = '<circle cx="160" cy="75" r="13" fill="#b83e25"/><path fill="none" stroke="#20201c" stroke-width="4" d="M160 21V48M160 102V129M106 75H133M187 75H214M122 37L141 56M179 94L198 113M122 113L141 94M179 56L198 37"/><circle cx="160" cy="75" r="26" fill="none" stroke="#20201c" stroke-width="2"/>'
    empower = '<path fill="none" stroke="#20201c" stroke-width="4" d="M160 19L212 75 160 131 108 75Z"/><path fill="#20201c" d="M160 40L192 75 160 110 128 75Z"/><path fill="#b83e25" d="M160 59L175 75 160 91 145 75Z"/>'
    recover = '<path fill="none" stroke="#20201c" stroke-width="4" d="M125 40H193L210 57V99L193 116H127L110 99V57"/><path fill="#20201c" d="M111 30L137 56H111Z"/><path fill="#b83e25" d="M154 54H166V70H182V82H166V98H154V82H138V70H154Z"/>'
    unknown = '<path fill="none" stroke="#20201c" stroke-width="4" d="M123 30H197V120H123Z"/><path fill="#20201c" d="M160 48L183 75 160 102 137 75Z"/><circle cx="160" cy="75" r="6" fill="#b83e25"/>'
    relic = '<path fill="#20201c" d="M160 19L211 50V100L160 131 109 100V50Z"/><circle cx="160" cy="75" r="27" fill="#f2ebdd"/><circle cx="160" cy="75" r="12" fill="#b83e25"/>'
    inscription = '<path fill="none" stroke="#20201c" stroke-width="4" d="M123 25H183L198 40V125H123Z"/><path fill="none" stroke="#20201c" stroke-width="4" d="M137 47H181M137 102H181"/><path fill="#b83e25" d="M160 59L176 75 160 91 144 75Z"/>'
    modifier = '<path fill="none" stroke="#20201c" stroke-width="4" d="M120 32H184V111H120M136 111V124H200V45H184"/><path fill="#b83e25" d="M147 54H159V69H174V81H159V96H147V81H132V69H147Z"/>'
    shop = '<path fill="none" stroke="#20201c" stroke-width="4" d="M112 61H208V121H112ZM103 61L118 29H202L217 61M175 121V84H191V121"/><path fill="#b83e25" d="M123 76H157V101H123Z"/>'
    camp = '<path fill="none" stroke="#20201c" stroke-width="4" d="M106 119L160 25 214 119ZM160 25V119M132 119L160 70 188 119"/><path fill="#b83e25" d="M160 70V119H188Z"/>'
    forge = '<path fill="#20201c" d="M108 69H212V84L181 96V113H196V125H124V113H139V96L108 84Z"/><path fill="#b83e25" d="M145 21H178V39H145Z"/><path fill="none" stroke="#20201c" stroke-width="4" d="M160 39V61"/>'
    reward = '<path fill="none" stroke="#20201c" stroke-width="4" d="M111 55H209V123H111ZM104 39H216V55H104ZM160 39V123"/><path fill="#b83e25" d="M139 22H154V38H139ZM166 22H181V38H166Z"/>'
}
$generated = [ordered]@{}
foreach ($family in $shapes.Keys) {
    foreach ($variant in 0..2) {
        $angle = @(-8, 0, 8)[$variant]
        $trace = @(
            '<path d="M18 42H59L82 65H101M18 47H57L80 70H101M18 52H55L78 75H101"/>',
            '<path d="M18 103H59L82 80H101M18 108H61L84 85H101M18 113H63L86 90H101"/>',
            '<path d="M219 75H238L261 98H302M219 80H236L259 103H302M219 85H234L257 108H302"/>'
        )[$variant]
        $body = '<g fill="none" stroke="#5b574f" stroke-width="1">' + $trace + '</g><g transform="rotate(' + $angle + ' 160 75)">' + $shapes[$family] + '</g>'
        $generated["$family-$variant.svg"] = '<svg xmlns="http://www.w3.org/2000/svg" width="320" height="150" viewBox="0 0 320 150">' + $body + '</svg>' + "`n"
    }
    $generated["icon-$family.svg"] = '<svg xmlns="http://www.w3.org/2000/svg" width="128" height="128" viewBox="85 0 150 150">' + $shapes[$family] + '</svg>' + "`n"
}
$manifest = Get-Content -LiteralPath (Join-Path $project 'data/ivory_run_art_manifest.json') -Raw | ConvertFrom-Json -AsHashtable
$families = @{card_strike='strike';card_defend='guard';card_fire='fire';card_heal='recovery';card_condense='condense';card_poison='poison';card_multihit='multihit';card_precision='precision';card_recall='recall';card_spark='energy';card_unknown='unknown';core_strike='multihit';core_guard='guard';core_heal='recovery';core_charge_card='charge';core_release='release';core_recover='recover';core_empower_card='empower'}
foreach ($slot in $families.Keys) {
    $manifest.slots[$slot].path = 'res://assets/programmer-art/' + $families[$slot] + '-1.svg'
}
# Distinct compositions are stored explicitly in the generated manifest.
$index = 0
foreach ($id in @($manifest.categories.cards.Keys | Sort-Object)) {
    $old = $manifest.categories.cards[$id]
    if (-not $families.ContainsKey($old)) { continue }
    $slot = 'card_' + $id
    $manifest.slots[$slot] = @{path=('res://assets/programmer-art/' + $families[$old] + '-' + ($index % 3) + '.svg');fit='contain';placeholder=$true}
    $manifest.categories.cards[$id] = $slot
    $index++
}
$extra = @{lab_unknown_many_resources='energy';lab_unknown_long_title='unknown';lab_unknown_long_rules='recall';lab_unknown_free='recovery';lab_unknown_custom='empower';arcane_bolt='precision'}
foreach ($id in $extra.Keys) {
    $manifest.categories.cards[$id] = 'sample_' + $id
    $manifest.slots['sample_' + $id] = @{path=('res://assets/programmer-art/' + $extra[$id] + '-1.svg');fit='contain';placeholder=$true}
}
$objects = @{effect_unknown='unknown';behavior_unknown='unknown';core_charge_effect='charge';core_scorch_effect='fire';core_afterglow_effect='recovery';core_multihit_behavior='multihit';core_cascade_behavior='release';activity_unknown='unknown';activity_encounter='strike';activity_relic='relic';activity_reward='reward';activity_shop='shop';activity_camp='camp';activity_forge='forge';activity_victory='empower'}
foreach ($slot in $objects.Keys) { $manifest.slots[$slot].path = 'res://assets/programmer-art/icon-' + $objects[$slot] + '.svg' }
foreach ($category in @('relics','inscriptions','modifier_items')) {
    $family = @{relics='relic';inscriptions='inscription';modifier_items='modifier'}[$category]
    $manifest.defaults[$category] = $family
    if (-not $manifest.categories.ContainsKey($category)) { $manifest.categories[$category] = @{} }
    $manifest.slots[$family] = @{path=('res://assets/programmer-art/icon-' + $family + '.svg');fit='contain';placeholder=$true}
}
$manifest.slots['modifier_fire'] = @{path='res://assets/programmer-art/icon-fire.svg';placeholder=$true}
$manifest.categories.modifier_items.fire = 'modifier_fire'
$manifestPath = Join-Path $project 'data/labs/programmer_art_manifest.json'
$manifestText = ($manifest | ConvertTo-Json -Depth 30) + "`n"
if ($Check) {
    foreach ($name in $generated.Keys) {
        $path = Join-Path $assetRoot $name
        if (-not (Test-Path -LiteralPath $path) -or [IO.File]::ReadAllText($path).Replace("`r`n", "`n") -cne $generated[$name]) { throw "Generated asset differs: $name" }
    }
    # Dictionary property order is not part of the catalogue contract.
    $actual = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    if ($actual.categories.cards.Count -ne $manifest.categories.cards.Count) { throw 'Catalogue card count differs' }
    foreach ($slot in $manifest.slots.Keys) { if ($actual.slots[$slot].path -ne $manifest.slots[$slot].path) { throw "Catalogue path differs: $slot" } }
    Write-Host "Verified $($generated.Count) vector assets and $($manifest.categories.cards.Count) explicit card mappings."
    return
}
New-Item -ItemType Directory -Force -Path $assetRoot | Out-Null
foreach ($name in $generated.Keys) { [IO.File]::WriteAllText((Join-Path $assetRoot $name), $generated[$name]) }
[IO.File]::WriteAllText($manifestPath, $manifestText)
Write-Host "Generated $($generated.Count) SVG assets and isolated laboratory catalogue."
