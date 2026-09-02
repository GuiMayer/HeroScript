# Card Component Architecture and Implementation Plan

## Status

Accepted architecture for the canonical card runtime. This plan intentionally
contains breaking changes: HeroScript has not been published and the runtime
must not preserve the definition-only deck or client-selected card/action
compatibility paths.

## Invariants

1. A card is an immutable container of typed components.
2. A played card is identified only by `cardInstanceId`; the server derives all
   executable components from the instance and its pinned content revision.
3. Upgrades permanently transform the base components of one card instance.
4. Statuses, temporary buffs, relics, actor attributes, target attributes,
   game-mode rules and encounter rules contribute contextual influences to
   calculation pipelines. They do not rewrite the stored card.
5. Every state-changing effect is processed by the same effect processor. Its
   provenance is diagnostic data and must not select an execution path.
6. Damage has no privileged resource semantics. It is a resource reduction
   effect whose target resource is data, and may target health, mana, armor or
   any other configured resource.
7. Defeat, death and other resource-bound outcomes are declared by resource
   policy. The engine must not interpret a resource named `health` specially.
8. Costs, conditions, targeting, effects, triggers, pipeline influences and
   zone transitions are components.
9. Definitions, compiled containers, instances, calculations and state
   transitions are immutable and canonically hashable.
10. Validation, inspection, legal-play projection and command execution reuse
    the same resolvers. The visual client never reproduces a gameplay rule.

## Resolution order

```text
definition base
  -> deterministic component-bundle expansion
  -> permanent card-instance upgrade patches
  -> effective card base
  -> contextual influence collection
  -> configured calculation buckets
  -> immutable resolution plan
  -> universal effect reduction
  -> atomic run/combat commit
  -> journal, animation frames and timeline
```

The distinction between base mutation and contextual scaling is observable in
the card-evaluation API. For each calculated attribute the API reports the
definition base, upgrade patches, upgraded base, contextual contributors,
bucket trace and final value.

## Canonical content

Card behavior is represented by stable, typed component IDs. Reusable behavior
may be authored as a component bundle, but bundle references are expanded by
the content compiler. A compiled card never depends on a second runtime action
authority.

Upgrade patches address a component ID and a typed attribute. Patches are
validated when content is published and again when a content revision is
activated for a development run.

Status and relic definitions may contain trigger components that emit effects
and passive influence components that contribute to a named calculation
channel and bucket. Emitted effects use the same runtime representation as an
effect emitted by a card or game-mode rule.

## State topology

The run owns a permanent `CardCollectionState`. Each active encounter owns
`CombatCardZones`. Zones contain instance IDs only. Encounter policies define
initial order, shuffle, hand lifecycle, exhaust persistence, generated-card
persistence and encounter cleanup.

Existing snapshots using parallel definition and instance lists are rejected
after the engine/schema version change. They are not migrated at runtime.

## Generic resources and outcomes

Resource definitions own threshold policies such as `DEFEAT_OWNER`,
`VICTORY_FOR_OPPONENTS`, `REMOVE_ENTITY`, `TRIGGER_EFFECT` or `NONE`. Policies
declare the threshold (`AT_MINIMUM`, `AT_MAXIMUM`, or a formula), scope and
priority. Combat outcome evaluation consumes emitted resource-threshold facts;
it does not read a resource by a hard-coded name.

`DAMAGE`, `HEAL` and `MODIFY_RESOURCE` are authoring conveniences that compile
to resource delta effects with an explicit target resource and operation.
Calculation channels such as `damage`, `healing`, `block`, `cost.energy` and
`status.stacks` select independent, game-mode-configured bucket pipelines.

## API boundary

The canonical player command is `PLAY_CARD` with actor, card instance, targets
and an optional cost option. It never accepts an action ID.

Detailed inspection is exposed at:

```http
GET /api/v1/combats/{combatId}/cards/{cardInstanceId}/evaluation
```

The response includes version coordinates, base container, applied upgrades,
effective base, all considered contextual sources, bucket traces, legality,
costs, targets, disposition and a resolution fingerprint. A batch hand
projection provides the same information without an N+1 request pattern.

Inspection depth is controlled by the game mode so sandbox runs may expose a
full theorycraft trace while published modes may expose resolved values only.

## Delivery order

1. Component contracts and content compiler.
2. Instance-only collection and encounter zone topology.
3. Effective-card and typed-upgrade resolution.
4. Generic calculation context, pipelines and influence providers.
5. Generic resources and resource-policy outcome evaluation.
6. Conditions, costs and targeting.
7. Universal immutable effect processor.
8. Atomic `PLAY_CARD` resolution and removal of card/action dual authority.
9. Encounter deck lifecycle and deterministic weighted offers.
10. Status, buff, relic and ability migration.
11. Inspection, legal-play and Godot projections.
12. Replay, timeline, hot-reload validation, content cleanup and end-to-end
    determinism verification.

Every delivery step must leave the build and test suite green and receive its
own commit.
