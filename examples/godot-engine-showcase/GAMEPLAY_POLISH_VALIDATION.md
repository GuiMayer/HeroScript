# Gameplay polish — validation and handoff

Validated on Windows with Godot 4.7.2 and .NET 10. See the
[research and remaining product work](../../docs/research/SLAY_THE_SPIRE_GAMEPLAY_AND_UX.md)
and the [art replacement contract](assets/ART_SLOTS.md).

## Implemented

- Combat arena, separate sides, clickable portraits and visible intent.
- Card illustration slots, distinct cost/effect areas, selected/focused state and play animation.
- English fallback and Portuguese messages, keyboard/controller navigation and reduced motion.
- Map sidebar driven by actual edges and advertised travel commands.
- Visual card rewards, shop identity/price joins, upgrade choices and confirmation dialogs.
- Run abandonment is available only from pause and requires confirmation. A single remaining route advances automatically; branches remain explicit choices.
- The main menu exposes immutable journey history. Replay walks persisted commands and resulting states without activating or mutating the selected run; hash verification remains a separate action.
- Signed block cleanup in JSON and a more varied opening hand, without a Godot gameplay exception.
- Longer communication allowance for whole-run replay verification; interactive commands retain their normal timeout.
- Persistent Combat/Journey screens refresh authoritative snapshots without clearing the navigation host. Existing cards do not replay entry animations, Journey scroll is retained and contextual combat controls reserve stable geometry.
- Empty hands render a centered localized state with a readable minimum width. Async card inspection applies the configured text scale before the enriched subtree can be drawn.

## Evidence

| Check | Result |
|---|---|
| Core/API suites | Presentation-only changes do not alter their contracts; run `dotnet test HeroScript.slnx -c Release` for the current totals |
| Block lifetime integration regression | Defense absorbs the enemy hit, leftover capacity clears at the owner's next activation, replay verifies |
| Offline client layers | Passed, including the canonical structured upgrade-choice fixture |
| Real-engine UI smoke | Passed, including persistent combat identity, stable footer geometry, targeting, pause, controller focus, historical state and branch activation |
| Gameplay layout | 1280×720, 1440×900 and 2560×1080; English/Portuguese; text scales 1.0 and 1.2 |
| Resolution regression | Empty-hand layout and async card enrichment preserve readable horizontal text and 120% font scale |
| Real campaign | Completed progression, rewards, shop purchase, preparation, upgrades and combats; Journey identity/scroll preserved and semantic replay valid |
| Visual checks | Rendered combat, rewards, shop, camp and forge; inspected combat in regular and high-contrast/expanded-text layouts |

The renderer may fit a requested window to the available desktop. The headless layout checks apply the exact requested dimensions; visual captures retain the same aspect ratio.

## Reproduce

From the demo directory, with an API running at the chosen address:

```powershell
godot --headless --path . --script res://tests/layers.gd
godot --headless --path . --script res://tests/resolutions.gd -- --layout-smoke
godot --headless --path . -- --ui-smoke --api-url=http://127.0.0.1:5271
godot --headless --path . --script res://tests/gameplay_polish.gd -- --api-url=http://127.0.0.1:5271
```

Add `--layout-only` to the last command for a shorter layout/projection check.
Run without `--headless` and add `--capture-polish` to render captures under
`tests/output/`. Test runs use fresh seeds but still create real runs in the
chosen API: use a separate QA port/storage, not a published game's service.

The tests restore their preferences on normal completion. As with other
integration tests, do not forcibly terminate them while they are changing
the application's selected test session.

## Try the changes

Restart the API to load the revised content, reopen the Godot project and
choose **New journey**. Existing runs keep their original content revision:
they are not rewritten to receive the new opening hand or cleanup pipeline.
The gameplay screen changes also work with older runs.

## Boundaries and remaining work

- The campaign still has four card definitions, a short linear route and limited AI variety. It is a playable foundation, not finished commercial content.
- The live HUD shows the authoritative result and plays received effect frames; it does not reconstruct intermediate game states locally.
- Long card descriptions retain full details in inspection/tooltips; compact views intentionally limit text.
- Shops and rerolls show published prices. Rich before/after upgrade comparisons and complete contextual status/relic explanations remain product work.
- No Slay the Spire art or text was imported. Placeholders are original vectors.
- The isolated Release build reported an existing `NU1903` advisory for `Microsoft.OpenApi 3.5.3`, plus existing nullable warnings. Dependency/security remediation was not included in this gameplay-focused change and should be reviewed before distribution.
