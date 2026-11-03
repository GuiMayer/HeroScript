# Tactical campaign presentation checks

The normal `prototype-test` launch opens `prototype_chapter`. Gameplay and writes
remain in the REST engine; presentation scenes consume its published projections.

## Offline inspection

Open `examples/godot-engine-showcase/scenes/unified_loadout_lab.tscn` in Godot.
Deck uses the same `deck_composition.tscn` as a run, including active/reserve columns,
group selector, module selector and CardView. Companions uses the same implemented
`support_workbench.tscn`, with both routines and the authored activation interval.
The old planned companion panel is no longer mounted in this laboratory.

`data/labs/tactical_build_lab.json` is a read-only capture from an isolated API
campaign, with its setting/revision/seed recorded. It contains all 27 distinct card
instances, the selected 15+5 composition, reserve and owned support. Editing these
examples cannot apply or save a real build. Re-capture deliberately when the
published contract changes; never use this file as a fallback for a run.

Payment uses the existing editable `scenes/ui/overlays/card_payment.tscn` and shared
CardView. `tests/card_payment.gd` exercises its fixture without API writes; the real
payment journey is `tests/card_payment_online.gd`. The offline catalog is not an
alternate gameplay implementation or an authority for resource/card costs.

## Automated matrix

Run `tools/test-godot-ui.ps1` with an isolated project and these tests:

- `tactical_build_catalog`: exact shared scenes, captured identities, two modules,
  two routines, cadence, read-only boundary and preserved card controls, EN/PT-BR.
- `unified_loadout_layout`: 1280×720, 1920×1080, 2560×1080, EN/PT-BR, text scaling,
  no vertically squeezed text and floating attribute drawer.
- `unified_loadout_shell`: keyboard/controller focus and persistent mounted tabs.
- `deck_composition`, `support_loadout`, `card_payment`: complete gestures,
  contextual drag, autosave, stale preview/uncertain receipt and chosen payments.
- `combat_stage_layout`, `dialogue_reading`: current combat layout and dialogue
  accessibility, including text reveal/history behavior.
- `card_motion_polish`, `presentation_director`, `turn_hand_cycle`: shared motion
  executor, cancellation/input release and the real activation lifecycle.

The tests reuse the current layout and animation settings. No new gameplay wait,
per-frame REST query, local support timer or local damage calculation is added.
Animation remains driven by confirmed facts. Tests certify contracts and geometry;
they do not substitute for manual readability and gameplay playtesting.

## Real API

Use a dedicated API/store on port 61989 for `prototype_trial_online.gd` (despite its
historical name, it follows the current normal chapter launch), payment and support
online journeys. These tests require an isolated Godot user directory and do not
touch the player's campaign. Stop that dedicated API before rebuilding its DLLs.
