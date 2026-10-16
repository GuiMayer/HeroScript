# Tag-synergy expansion — implementation record

Setting: Ascendant Matrix, world one. The original 33 card components are frozen
in a regression fixture; opening deck and existing numeric behavior are unchanged.
Seed for focused execution tests: 123. Expansion definitions live in separate
files so a content revision pins both original content and additions atomically.

## Producer / consumer contracts

| Producer | Tags | Consumer |
| --- | --- | --- |
| Direct attacks | damage, attack | Weapon scaling, critical tiers, Armor, Block |
| Residual producers (effective card) | residual | Zone search / recovery |
| Defensive producers (effective card) | defense | Zone search / preparation |
| Offensive status payload | residual, damage_payload | Offensive release, explicit recipe permission |
| Defensive status payload | block_payload (reserve or residual) | Defensive release, explicit recipe permission |
| Healing status payload | residual, healing_payload | Healing release, explicit recipe permission |
| Energy reserve | reserve, energy_payload | Energy release, explicit recipe permission |
| Utility effects | resource_gain / recovery | Dedicated utility channel, no offensive scaling |

Tags classify; rules, flow IDs, payload permissions and typed components authorize
behavior. Effective tags, not names, feed selectors. Modifier inventories,
inscriptions and relics retain their separate authorities.

## Progress

1. Baseline and taxonomy: complete. Six focused content tests pass.
2. Shared calculation / payload contracts: complete. Utility channel isolated;
   source bindings require both domain and scaling tags. Recipes select typed
   tags, retaining explicit consumption permissions. Existing status lifecycle
   executes triggers before expiry; resource costs and condition components
   support nonlethal life payment. Dependent execution gates are tested with
   their card batches before publication.
3. Energy: complete. Eight execution cases cover full/partial/empty Energy,
   nonlethal life payment (including fractional Health), immutable failure and
   one-shot reserve consumption. Both generators and the reserve producer exile;
   release cannot manufacture potency when empty.
4. Defense: complete. Eight execution cases cover Block 0/8/20, missing Health,
   full-value area coverage followed by one Block consumption, and utility healing.
   Defense uses a points-typed pipeline; old scalar definitions remain unchanged.
5. Residuals and recovery: complete. Eight execution cases verify owner-only
   boundaries, trigger-before-expiry, no immediate/double tick, novel offensive
   status selection, and healing condensation based on potency rather than ticks.
   Reapplication adds captured lots and preserves the existing duration window;
   no per-lot duration is implied. Energy, damage, Block and healing all use
   the same payload processor with typed selectors and opt-in permissions.
6. Circulation: complete. Five execution cases verify canonical first-match order,
   partial/empty selection, unchanged instance identities and exclusion of the
   resolving card (it remains in hand while sources are draw/discard).
   World-one zone graph preserves the inherited lifecycle and adds effect-only
   flows. No manual-selection UI or exiled-card recovery is introduced.
7. Offensive geometries: complete. Six execution cases verify per-hit budgets,
   guaranteed tiers, energy cost, defense removal ordering and excess-only relay.
   New direct effects leave stage selection to the global policy, enabling its
   critical stage instead of accidentally opting out with a partial stage list.
   Critical damage attributes use percentage points (100 = +100% per tier).
8. Modifier compatibility: complete. The 12 existing upgrades and local critical
   override accept structurally compatible new cards; every declared target is
   composition-tested. Split is deliberately not granted to full-per-hit bases:
   it currently promises a shared budget. Numeric upgrades avoid utility-main
   cards; duration upgrades exclude the explicitly one-shot delayed recovery.
   Published whitelists remain structural compatibility gates, not combo rules.
   Predicates support effective tags, but the package validator preflights
   unrestricted upgrades against fixture cards with incompatible slots.
   Tag patches reject adding an already-present tag, so affinity/ward upgrades
   retain their existing tag contract rather than silently changing patch semantics.
   Inscription bonuses already select effect domains/channels; no card IDs added
   to inscriptions and no inventories merged.
9. Distribution and presentation: complete. All 24 cards are in the curated
   reward pool (54 candidates before progression filtering), with a separate
   tag-based expansion pool and all-world-one sandbox pool. Five Rule Lab presets
   cover Energy, defense, residuals, circulation and mixed attacks. Select
   Ascendant Matrix, open Rule Lab and choose a preset; its mode uses world-one
   rules with sandbox tools. The initial campaign deck is unchanged.
   Existing art slots/shared CardView are reused; names/descriptions have EN/PT
   mappings. Godot: 87 catalog/UI checks plus 106 existing presentation/UI checks.
   Five sandbox launches and reexecuted initial replays passed through REST.
10. Certification: complete. See final evidence below.

## Final evidence — 2026-10-16

- Complete Core suite: **1,874 passing tests**, including 53 expansion cases.
- API expansion + existing sandbox integration: **23 passing tests**. One test
  independently starts three fresh hosts/stores and plays each of the 24 cards:
  **72 real REST actions**, identical per-card state hashes across all three
  executions, read-only evaluations and 72 valid reexecuted replays.
- Five mixed-deck presets: launch, real card action, branch creation, unchanged
  source snapshot, and valid reexecuted source/child replays.
- Existing world-one mechanics tests: old card effects and enemy abilities
  remain executable. The guard reserve regression caught and fixed an overly
  restrictive selector: reserves need block_payload, not necessarily residual.
- Published campaign, seed 154: nine victories, Completed, **65 Health**,
  211 commands, valid replay and restart state equality. The previous 70-Health
  probe belonged to the old reward catalog. Offers and subsequent commands
  legitimately change with expanded content and its pinned revision.
- Godot headless: **193 checks** across the actual preset selector, shared card
  presentation and UI contracts. Preset editing uses independent copies.
- Effective-tag transformation/removal, full hand, empty discard, stack permission
  filtering, typed payload separation and three emergent compositions are tested.

Reproduction:

```powershell
dotnet test tests/Core.Tests --no-restore
dotnet test tests/API.Tests --no-restore --filter "FullyQualifiedName~TagSynergy|FullyQualifiedName~CombatSandboxIntegrationTests"
dotnet test tests/API.Tests --no-restore --filter "FullyQualifiedName~AscendantWorldOneJourneyTests.PublishedAct_CanTraverseAllNineEncountersServicesAndReplay"
./tools/test-godot-ui.ps1 -Tests @('tag_synergy_catalog','card_presentation','ui_contracts')
```

No effect handlers or engine-version changes were necessary: this expansion uses
the existing immutable reducers, generic calculations, payloads and zone flows.
Prior pinned revisions remain replayable; no existing player save was rewritten.
The user's running server was not restarted or modified during certification.

## Economy and remaining playtest work

Direct Energy generators, the Energy reserve producer and new healing producers
exile. Searches only move existing instances from draw/discard; they never revive
exiled producers. Empty releases do not create resources. Cost reductions retain
curated compatibility; no new zero-cost draw/generate/recover-exile cycle is added.
This is a structural check, not a proof over every possible future mod.

The three certified emergent examples are Caustic Guard → Guarded Strike →
offensive condensation; Block → Shelter + Guarded Strike; and Charge Cell →
Release Charge → Heavy Impact. Rules consume tags/resources, not these names.

Human balance/playfeel validation remains necessary: 25–40 minute duration,
reward dilution, recovery abundance and per-hit upgrades are not certified by an
automated victory. No manual playtest is claimed. Structural modifier whitelists
and the non-idempotent tag-patch limitation are explicitly retained in stage 8.
