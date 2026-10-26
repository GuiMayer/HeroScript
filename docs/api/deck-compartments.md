# Deck compartments (optional)

Compartments are character build associations, not card circulation zones or inscription equipment. A mode opts in through `deckCompartmentPolicyId` and the character through a `deck_compartments` entity component with `profileId`. No policy/profile means no gameplay change. No existing setting is enabled implicitly.

`GET /api/v1/runs/{runId}/deck-compartments` reads the current character only. Response includes `enabled`, `canChange`, `unavailableReason`, `profile`, `allocations`, cards with composed `presentation`, and `runSequence`/`expectedStep`/`contentRevision`. GET never initializes state. Disabled characters return an empty disabled view.

`POST /api/v1/runs/{runId}/deck-compartments/preview` is read-only:

```json
{
  "expectedSequence": 7,
  "expectedStep": 12,
  "allocations": [
    { "cardInstanceId": "10000000-0000-4000-8000-000000000001", "compartmentId": "utility" }
  ]
}
```

The array is the **complete final distribution**. Omitted cards become neutral. An empty array clears allocations. Missing/null arrays are invalid. Preview returns `before` and `after`, including ordinary card presentation of effects, costs, conditions and targeting. Stale sequence/step returns 409; incompatible distributions fail without partial changes.

Commit through `POST /api/v1/runs/{runId}/commands`, using the existing command envelope:

```json
{
  "commandId": "20000000-0000-4000-8000-000000000001",
  "type": "APPLY_DECK_COMPARTMENTS",
  "expectedSequence": 7,
  "expectedStep": 12,
  "payload": {
    "actorId": "hero",
    "allocations": [
      { "cardInstanceId": "10000000-0000-4000-8000-000000000001", "compartmentId": "utility" }
    ]
  }
}
```

Use the actual actor/card IDs returned by the query. The canonical pipeline owns optimistic concurrency, idempotency, persistence and replay. There is no separate equip-write endpoint. State cannot be changed during combat or pending encounter resolution. Foreign actors/cards, capacity violations, singleton collisions and missing bindings fail atomically.

Content grants reference `card-component-bundles`, using canonical typed card components. They do not accept executable rules in equip payloads and do not mutate base cards/upgrades. Contribution provenance is separate from `permanentChanges`; consumers receive the same component types regardless of source. Removing an allocation removes only that source's contribution.
