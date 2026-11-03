# Support equipment and actor cadence

Supports are ordinary combat participants selected by a persistent build
association. Their entity contains resources/abilities; the routine selects
revisioned gambits. Those gambits rank the ordinary legal actions, and automatic
actions use the existing command/effect executor and journal. There is no
support damage calculator, client timer or parallel turn system.

## Content

The game mode optionally references `supportLoadoutPolicyId`. A setting without
this reference receives no inventory, participant or cadence counters.

Example `support-loadout-policies` definition:

```json
{
  "policyId": "campaign-support",
  "inventoryScope": "Actor",
  "editWindows": ["OutsideCombat"],
  "supportIds": ["sentinel"],
  "startingOwnedSupportIds": ["sentinel"],
  "startingSupportId": "sentinel"
}
```

`inventoryScope` accepts `Actor` or `Run`. Inventory ownership and each actor's
equipment remain separate. Current REST commands only edit the current player
character. `Preparation` restricts editing to an unresolved preparation activity;
combat editing is unsupported and rejected during publication. The examples are
authoring illustrations; their IDs must resolve to actual published definitions.

Example `supports` definition:

```json
{
  "supportId": "sentinel",
  "entityDefinitionId": "sentinel-entity",
  "ownerActivationInterval": 3,
  "countsForOutcome": false,
  "routineIds": ["offense", "defense"],
  "defaultRoutineId": "offense"
}
```

`countsForOutcome` must be authored explicitly. When false, the participant
does not keep a side alive or become a winner in the ordinary outcome evaluator.
It still exists, can act, and follows normal action targeting policies. Whether
an action can target it is controlled through the existing relationship and
action rules. There is no engine-wide immunity flag implied by being a support.

Example `support-routines` entry:

```json
{ "routineId": "offense", "gambitIds": ["sentinel-attack", "sentinel-pass"] }
```

Routines must reference nonempty, unique, published gambits. Entities authorize
their actual abilities; a routine does not grant an arbitrary action. Order and
tie-breaking use the existing gambit priorities and canonical IDs. Include a
legal fallback matching the mode's pass/end-activation policy.

## Equipment and encounters

`EQUIP_SUPPORT` takes `actorId`, `supportId`, `routineId`. To unequip, both latter
fields are null. Unknown/unowned equipment, unauthorized routines, a different
actor, stale commands or edits during a pending combat are rejected by the
ordinary versioned dispatcher. Writes are immutable candidates with regular
receipts, persistence and replay.

At encounter creation, the coordinator resolves the run's pinned revision and
adds the equipped participant on its owner's side with the selected gambits.
Its stable ID is `$support::<ownerId>`. Identity collisions and missing owners
are errors. Participant zones are provisioned by the existing participant zone
initializer. `RunEncounterState.SupportComposition` captures equipment identity,
routine, participant and revision; changing a later build never rewrites it.

## Generic cadence

The controller binding has optional `decisionIds` and `cadence` fields. They are
available to any participant, independently from support equipment:

```json
{
  "kind": "AI",
  "policyId": "gambit",
  "decisionIds": ["sentinel-attack"],
  "cadence": { "anchorActorId": "hero", "every": 3 }
}
```

An interval counts **completed activations of the anchor**. It does not count
actions, frames, animation durations or global rounds. After three anchor
activations, the participant becomes eligible in the canonical sequence. Its
next completed activation acknowledges the current anchor count. If a turn is
delayed, it does not create an immediate burst of banked actions. An unavailable
anchor disables its dependent participant. Self-links, missing anchors, chained
cadences and intervals outside 1–10000 are rejected.

Progress is an optional immutable part of `ActivationState`, carried through
ordinary activation frames, snapshots and replay. A new encounter starts a new
combat/cadence; an old attempt retains its own counters. There are no persistent
power gains across attempts. Actors without cadence acquire no counter state,
and optional new properties are omitted from their snapshots.

Content revisions are pinned. An equipped policy may explicitly select
`revisionMigration: RebindCompatible` to permit an outside-combat revision
change after owned equipment/routines validate against the target. Otherwise
the change is rejected. Captured active encounters cannot change equipment
revisions in flight; historical snapshots remain untouched.

## Validation

Core regressions cover the real phase/activation boundaries, exact 3/6 cadence,
three repeated runs, restored snapshots, owner defeat with a living excluded
support, invalid anchors, legacy snapshots, two inventory scopes, two routines,
actor/revision isolation and duplicate commands across restart plus ten semantic
replays. REST catalog/UI and final setting coverage are subsequent MVP stages.
