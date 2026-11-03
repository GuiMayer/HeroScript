# Deck composition (optional)

Composition selects existing owned instances for a build. It does not grant card components, move cards between circulation zones or equip inscriptions. Enable it with the mode's `deckCompositionPolicyId`; groups, quotas, eligibility, modules, initial selections and edit windows come from revisioned JSON.

`GET /api/v1/runs/{runId}/deck-loadout` projects the current campaign character. It returns setting/run/actor identity, `runSequence`, `expectedStep`, `contentRevision`, `enabled`, `canChange`, `unavailableReason`, policy, current selection and owned cards with canonical base presentations. Each card has `groupId` or belongs to the reserve. Modes without this feature return a disabled empty snapshot; reads never initialize a build.

`POST /api/v1/runs/{runId}/deck-loadout/preview` evaluates one complete gesture without changing the run, RNG, zones or journal:

```json
{
  "expectedSequence": 7,
  "expectedStep": 12,
  "edit": {
    "actorId": "hero",
    "groupId": "primary",
    "replacedCardId": "10000000-0000-4000-8000-000000000001",
    "replacementCardId": "10000000-0000-4000-8000-000000000004"
  }
}
```

The response contains `before` and `after`. Stale versions return HTTP 409. Invalid ownership, group eligibility, quotas, duplicate selections, setting/revision or edit window reject the whole gesture. No empty-slot intermediate state is committed.

For module selection, use an edit containing only `actorId`, `groupId` and `moduleId`. The server selects that module's authored `startingCardIds` from already-owned instances in creation order, excluding cards used by other groups. It never generates missing cards. Card replacement and module selection are mutually exclusive gestures. Card IDs remain unchanged, including upgrades and compartment associations.

Commit the same `edit` as the payload of `EDIT_DECK_LOADOUT` through the existing `/api/v1/runs/{runId}/commands` endpoint. Use the snapshot's expected sequence/step and a stable command ID; retry an uncertain response with the same envelope. Preview is advisory: the command repeats validation against its authoritative version. The command is advertised only when the configured edit window permits it.

In Godot, open the current character's loadout → Deck → Build. Select a group, select an active card, then its replacement in reserve; dragging reserve onto an active card uses the same operation. Selecting a module autosaves through preview → command → confirmed read. Switching tabs preserves in-flight operations. Session/setting changes and stale previews cannot submit an old selection; unresolved receipts use the existing pending-command recovery. The view uses editable scenes and canonical card components, independent of transport code.

The new campaign setting is supplied by later MVP stages; this API does not implicitly enable composition in existing settings.
