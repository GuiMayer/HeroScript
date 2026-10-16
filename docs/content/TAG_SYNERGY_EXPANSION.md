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
4. Defense: pending.
5. Residuals and recovery: pending.
6. Circulation: pending.
7. Offensive geometries: pending.
8. Modifier compatibility: pending.
9. Distribution and presentation: pending.
10. Certification: pending.
