# Deterministic runs

HeroScript treats a run as a reproducible state machine. Given the same initial
state, content revision and ordered commands, the engine must produce the same
states and domain events on every supported platform.

## Transition contract

Domain modules follow this shape:

```text
(immutable state, command, deterministic context)
    -> (new immutable state, new deterministic context, immutable events)
```

A transition must not read wall-clock time, create a random identifier, use a
process-global random source, perform I/O, or mutate an input object. Application
facades validate and order commands; infrastructure persists the returned facts.

## Deterministic context

`DeterministicContext` is part of the run snapshot and owns every ambient input
that can affect simulation:

- the original seed and the serializable SplitMix64 state;
- the transition step and monotonic logical clock;
- the deterministic identifier sequence;
- the immutable content revision;
- the engine-rules version.

Random draws and identifier allocation are pure operations. Each returns both a
value and the replacement context, making accidental reuse visible in code review
and replay tests.

## Stable representations

Snapshots and event payloads are serialized canonically before hashing. Object
properties use ordinal ordering; array order is preserved because it is domain
data. State hashes are lowercase SHA-256 values.

The pseudo-random algorithm is owned and versioned by the engine instead of using
`System.Random`, whose implementation is not a replay contract. Domain UUIDs use
UUIDv8 derived from seed, allocation sequence and semantic scope. Simulation time
comes from the logical clock, never `DateTime.UtcNow`.

## Compatibility rule

A persisted run records its content and engine versions. A replay must refuse to
continue when either version is unavailable; silently applying newer rules would
produce a plausible but invalid run.

Legacy mutable modules are migrated vertically. Until a module uses this contract,
it must be treated as non-replayable and cannot claim deterministic guarantees.

## Authoritative aggregates

Only snapshots owned by `RunManager` and `CombatSystem` are authoritative game
state. Their public transition methods serialize access per aggregate and replace
the whole snapshot after a successful transition. Nested runtime collections use
immutable storage and copy caller-owned collections on assignment.

The main modules have narrow responsibilities:

| Module | Responsibility |
| --- | --- |
| Run transitions | Deck, economy, rewards, shop and preparation state changes |
| Combat transitions | Action validation, entity/resource changes and turn progress |
| Effect handlers | Adapt a typed effect to combat, status, run or metadata behavior |
| Deterministic context | Random cursor, logical time, IDs, engine/content version |
| Repository | Atomically make an accepted run transition durable |
| Event bus | Publish an immutable, ordered and reproducible observation stream |

Managers may coordinate these modules, but domain calculations must not retain a
hidden random cursor, clock, turn meter or partial state in a singleton.

## Command and checkpoint lifecycle

Each accepted run command follows this order:

```text
validate command
    -> calculate candidate snapshot
    -> advance deterministic context
    -> calculate canonical state hash
    -> atomically persist snapshot + journal entry
    -> publish snapshot in memory
    -> publish observation events
```

`RunCheckpoint` is the atomic persistence unit. Its journal entry records the
command type and payload, run sequence, deterministic step, previous-state hash,
new-state hash and logical timestamp. If persistence fails, the candidate is not
published and any external modifier applied during preparation is rolled back.

Restoring an older domain snapshot is itself a new command. It never rewinds the
sequence, random cursor, identifier sequence or logical clock.

## Replay and integrity

`RunReplayVerifier` loads retained checkpoints in sequence order and verifies:

- run/sequence identity between snapshot and journal entry;
- contiguous retained sequences;
- monotonic deterministic steps;
- the SHA-256 link to the preceding retained state;
- the canonical hash of every stored state.

The resulting final snapshot is accepted only when the complete retained chain is
valid. When retention removes old checkpoints, the first remaining checkpoint is
the new trust anchor; all links after it are still verified. This is checkpoint
reconstitution and integrity verification. A future command-only replayer may
re-execute the recorded command payloads, but it must use the same versioned
transition registry rather than bypass these invariants.

## Explicit compatibility boundaries

Some APIs still serve editors, standalone calculators and older callers. They are
not part of a replayable run unless the caller supplies deterministic inputs:

- `DefaultRandomProvider` uses process entropy; the run path passes
  `DeterministicRandomProvider` explicitly;
- compatibility overloads for status and script modifiers allocate ambient IDs;
  run/effect transitions call overloads with deterministic IDs and logical time;
- optional entity/session ID helpers are conveniences for external API callers;
- cache invalidation time and configuration authoring dates are operational
  metadata and never enter a run snapshot or its hash.

Every remaining ambient source in `src/Core` is marked
`nondeterministic-boundary:`. `DeterminismArchitectureTests` fails if a new use of
wall-clock time, global randomness or random GUID allocation is added without
being removed or explicitly classified.

## Rules for extending the engine

1. Put all data that can change a result in `DeterministicContext`, versioned
   content, or the command itself.
2. Make a transition return replacement state; never mutate the input or retain
   simulation state in a service instance.
3. Copy caller-owned collections into immutable storage at aggregate boundaries.
4. Allocate domain IDs through the transition's deterministic cursor.
5. Use logical timestamps for domain facts. Real time is allowed only for logs,
   monitoring, caches and authoring metadata.
6. Persist before publishing authoritative state.
7. Add a same-input/same-hash test and a mutation-isolation test for new state.
8. Bump the engine or content revision when a rules change would alter replay.

## Required validation

Before merging a deterministic-domain change, run the Core suite, the API unit
suite and the solution build. At minimum, tests must cover reproducibility from
the same seed and commands, defensive collection copies, persistence rollback,
checkpoint hash-chain tampering, and detection of unclassified ambient inputs.
