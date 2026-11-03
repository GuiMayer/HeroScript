# Support loadouts

Supports are a mode-enabled, revisioned build module independent from cards,
inscriptions and relics. The endpoint always addresses the current run character.
See [authoring and actor cadence](../guides/support-loadouts.md).

## Read

`GET /api/v1/runs/{runId}/support-loadout`

Returns `runId`, `settingId`, `runSequence`, `expectedStep`, `contentRevision`,
`actorId`, `enabled`, `canChange`, `unavailableReason`, `policy`, `selection`,
`owned` and optional `combat`. Each owned entry contains its support definition
and permitted routines, including authored names/descriptions. Unowned supports
and another actor's inventory are not projected.

A disabled setting returns `enabled: false`, an empty catalog and no equipment.
A combat in progress is readable with `canChange: false`; it does not become an
editable out-of-combat build because the client opened the loadout screen.

## Read-only preview

`POST /api/v1/runs/{runId}/support-loadout/preview`

```json
{
  "expectedSequence": 4,
  "expectedStep": 9,
  "equipment": {
    "actorId": "player",
    "supportId": "support_sentinel",
    "routineId": "support_defense"
  }
}
```

Returns `before` and `after` projections calculated against one immutable run
snapshot. No receipt, event, resource payment or RNG draw is committed. Stale
sequence/step returns 409. Invalid ownership, routine or editing window returns
400. Actual IDs and version tokens must come from the current response.

## Commit and recovery

Use the existing `POST /api/v1/runs/{runId}/commands` envelope with type
`EQUIP_SUPPORT` and the same equipment object as `payload`. Supply a unique
`commandId` and current `expectedSequence` / `expectedStep`. Both support and
routine are null to unequip. There is no separate mutable CRUD endpoint.

Every edit is one complete autosaved command. The UI displays confirmed
equipment while validating/saving, then reads the resulting version. On an
uncertain receipt it uses the shared pending-command recovery, preserving the
original command ID. It blocks other build edits and leaving until recovery;
it never submits a guessed replacement command.

## Combat presentation

The compact session's `run.supportCombat` is derived from the captured encounter
equipment and authoritative activation counters. It includes support/routine
IDs, participant ID, remaining completed owner activations, eligibility and owner
availability. It is null outside an active equipped encounter. Reading it does
not advance a timer or action. Existing intent/application records present the
actual ability and target; animations use the normal combat presentation queue.

`Support Workshop` (`support-lab`) is a separate playable proof setting with
offense/defense routines and a three-owner-activation interval. It does not change
existing campaigns or serve as the final tactical campaign content.

Regression evidence: `SupportLoadoutJourneyTests` (real API/gateway/effects),
`support_loadout.gd` (autosave/recovery/isolation), and
`support_loadout_online.gd` (Godot with a disposable real API store).
