# Encounter attempts and persistent campaigns

An attempt is an existing `RunEncounterState`, identified by `Combat.CombatId`
and associated with `NodeId`. `RunState.AttemptKey` identifies campaign creation;
it is not an encounter attempt counter. Attempts remain in `RunState.Encounters`
in creation order, including their outcome and captured deck composition. There
is no second mutable attempt ledger.

## Authored policies

Select a revisioned progression policy through the game mode. For example:

```json
{
  "progressionPolicyId": "campaign-retry",
  "encounterVictory": "Continue",
  "encounterDefeat": "Fail",
  "encounterDraw": "Fail",
  "encounterAbandoned": "Fail",
  "endOfMap": "Complete",
  "encounterRetry": "RestartActivity",
  "retryableEncounterOutcomes": ["DEFEAT"],
  "allowAbandon": true,
  "allowOutOfActivityCommands": false
}
```

`RestartActivity` overrides the ordinary outcome transition only for listed
outcomes. A defeated combat is resolved normally through `RESOLVE_COMBAT`; the
campaign remains active, its encounter node remains pending, and the server
publishes `START_ENCOUNTER` again. No node exit effects, node rewards, or
progression are applied on that retry path. An abandoned campaign and an
abandoned combat are separate operations; only the latter can be retryable.

The completion query considers the latest attempt and the pinned retry policy.
An earlier ended combat cannot complete a newly active attempt.

Select an actor resource lifecycle policy through the mode as well. Each resource
rule explicitly defines `entry`, `victory`, `defeat`, `draw`, `abandoned`, `retry`
and `missingResource`. `retry: ResetToMaximum` restores that resource to its
persistent maximum. `ResetToConfiguredValue` uses the authored `configuredValue`.
No resource ID has a built-in health/heat/defense meaning. Missing rules do not
implicitly reset resources.

## Persistence matrix

| Data | Retry behavior and authority |
| --- | --- |
| Campaign identity, content revision, journal | Retained; ordinary command commit and receipt |
| Completed/active attempts | Retained immutable snapshots; new combat gets a new identity |
| Persistent hero resources | Actor resource lifecycle's `retry` action, separately per resource |
| Campaign wallet/materials | Retained unless an authored boundary effect explicitly changes them |
| Combat statuses, cooldowns, temporary actor changes | Remain in historical combat; new combat is initialized from persistent actor and configured initial state |
| Card locations and temporary exile | Authored `encounter.ended`/`encounter.started` zone flows; initialization does not invent a cleanup rule |
| Temporary card instances | Authored lifetime boundary; destruction and zone transfers share the transaction |
| Card mutations/upgrades, inscriptions, compartments, selected module | Retained permanent state; next encounter captures the currently valid deck build |
| Narrative flags/conversation consequences | Retained; retry does not re-enter the map node or repeat its entry effects |
| Node exit effects/rewards | Skipped for retryable outcomes; applied once on ordinary node completion |
| Randomness | Campaign stream continues; new combat seed is drawn from it, never reset to the previous seed |

Manual authoring can deliberately choose other reset/retention rules. The MVP
content policy will reset its life/heat/temporary defense explicitly; these are
setting decisions, not engine defaults. A resource with a defeat threshold must
be restored appropriately before the retry commit, otherwise its ordinary
outside-combat consequence can still fail the campaign.

## Transaction and recovery

Encounter-end zone flows, responses, resource promotion, attempt result and node
progression form one candidate. A failure publishes none of it. An invalid new
encounter start likewise preserves the pending node, build, attempt history and
RNG stream. Duplicate command IDs return the original receipt, including after a
server restart. The next attempt uses the normal encounter creation path and
captures a fresh build, without rewriting a prior attempt.

Regression coverage: `EncounterAttemptJourneyTests`, `ActorResourceJourneyTests`,
`RunProgressionTests`, deck loadout capture tests and packaged zone boundary tests.
The durable journey repeats semantic replay ten times and compares hashes while
preserving the parent branch. No player save directory is used.
