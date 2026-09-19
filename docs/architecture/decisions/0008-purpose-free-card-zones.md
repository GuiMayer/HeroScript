# ADR 0008: purpose-free card zones

**Status:** accepted

**Date:** 2026-09-18

## Decision

The engine persists cards as immutable instances inside a generic zone topology.
A zone has an identity, owner, ordering, visibility, capacity and presentation
metadata, but the runtime assigns no built-in meaning to its name.

Every card movement is executed by a revisioned JSON flow. Run start, encounter
start/end, activation start/end, card resolution, effects, gameplay commands and
development tools all invoke the same zone-flow executor. A card disposition
component therefore references only a `cardZoneResolutionFlowId`.

`allowsCardPlay` is the sole domain permission that identifies a play source.
Clients use the generic `cardZones` projection and presentation slots; they do
not infer rules from names such as `hand`, `draw`, `discard` or `exhaust`.

## Consequences

- A mode can implement deckbuilding, cooldown slots, inventories, queues or
  other card organizations without changing engine code.
- The topology and deterministic context change atomically, including fallback
  flows, shuffles, creation and destruction.
- Combat policies do not duplicate card-zone lifecycle rules.
- There is no legacy pile mutation API, fixed destination enum, fixed deck
  transition engine or purpose-specific card movement event.
- Published content must provide the complete zone graph required by a run.

## Invariants

1. Every live card instance belongs to exactly one authored zone.
2. Every mutation passes through `CardZoneTransitions` and a compiled flow.
3. A failed flow preserves both the topology and deterministic cursor.
4. Timeline steps record affected, created and destroyed instance identities.
5. Replay uses the pinned graph and content revision; the client never supplies
   a destination zone for a gameplay rule.
