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
| Defensive status payload | residual, block_payload | Defensive release, explicit recipe permission |
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
7. Offensive geometries: pending.
8. Modifier compatibility: pending.
9. Distribution and presentation: pending.
10. Certification: pending.
