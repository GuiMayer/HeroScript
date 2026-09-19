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

## Version rule

A persisted run records its content and engine versions. A replay must refuse to
continue when either version is unavailable; silently applying newer rules would
produce a plausible but invalid run. Every gameplay module must satisfy this
contract before it can participate in an authoritative run.

## Authoritative aggregates

Snapshots owned by the run runtime are authoritative game state. A run-owned
combat is embedded in that run; no combat service owns a parallel mutable copy.
`CombatRunCoordinator` is the transactional combat application service, while
`CombatCommandHandler`, `AutomaticFlowDriver` and `CombatBoundaryExecutor`
calculate candidate replacements. `CombatFlowPlanner` only resolves the pinned
content graph and composes boundaries and intents. Public transition methods
serialize access per aggregate and replace the whole snapshot after a successful
transition. Nested runtime collections use immutable storage and copy caller-owned
collections on assignment.

The main modules have narrow responsibilities:

| Module | Responsibility |
| --- | --- |
| Run transitions | Card-zone topology, economy, rewards, shop and preparation state changes |
| Combat command handler | Compose legal-action evaluation and pure state reduction for every controller |
| Automatic flow driver | Reuse the command handler until player or player-priority input is required |
| Combat boundary executor | Order configured phase, card-zone, resource, status and relic lifecycles and emit frames |
| Combat outcome resolver | Derive outcome from sides, controllers and configured resource thresholds |
| Combat flow planner | Resolve the pinned policy graph and compose boundary results with intents |
| Effect handlers | Adapt a typed effect to combat, status, run or metadata behavior |
| Deterministic context | Random cursor, logical time, IDs, engine/content version |
| Repository | Atomically make an accepted run transition durable |
| Durable event projection | Derive reconnectable run/combat events from the journal |
| Event bus | Deliver correlated observation events and append durable telemetry |

Managers may coordinate these modules, but domain calculations must not retain a
hidden random cursor, clock, turn meter or partial state in a singleton.

A manual root action and every automatically derived action form one candidate
batch. The run committer receives that batch only after the whole automatic
continuation succeeds. A reducer, AI decision, lifecycle or automatic-step-limit
failure therefore publishes no prefix of the batch. Manual execution and theory-
crafting simulation share this same command handler and commit path.

## Command and commit lifecycle

Each accepted run command follows this order:

```text
validate command
    -> calculate candidate snapshot
    -> advance deterministic context
    -> calculate canonical state hash
    -> atomically append authoritative run commit
    -> publish snapshot in memory
    -> publish observation events
```

`RunCommit` is the atomic persistence unit in the append-only store. It records
the root command identity and payload, expected version, run sequence,
deterministic step range, previous/new state hashes, logical timestamp, resulting
state, ordered frames and durable facts. Read snapshots are rebuilt from commits;
they are never a parallel authority. If append fails, the candidate is not
published and any external modifier applied during preparation is rolled back.

Restoring an older domain snapshot is itself a new command. It never rewinds the
sequence, random cursor, identifier sequence or logical clock.

## Replay and integrity

`RunReplayVerifier` remains the low-level stored-commit integrity checker. It
verifies:

- run/sequence identity between snapshot and journal entry;
- contiguous retained sequences;
- monotonic deterministic steps;
- the SHA-256 link to the preceding retained state;
- the canonical hash of every stored state.

`RunSemanticReplayService` is the authoritative verification path. It creates an
isolated runtime, executes `run.start`, reexecutes every subsequent journal
command through the same run/combat coordinators, and compares sequence, step and
canonical state hash after every transition. Stored snapshots are not used as
replay results. Snapshot retention therefore has no effect on replay coverage.

The public verification surfaces are `POST /api/v1/runs/{runId}/verify` and
`POST /api/v1/combats/{combatId}/verify`. Journal and commit metadata are
available under the corresponding versioned read endpoints.

## Durable event projections

Runtime clients read `GET /api/v1/runs/{runId}/events` or the combat-scoped
equivalent. These events are deterministic projections of committed journal
entries: their cursor is the run sequence, their ID is derived from run seed,
sequence and command type, and their timestamp is logical time. They therefore
survive restarts without a second transactional write.

The versioned SSE endpoints read the same projection and honor both
`afterSequence` and `Last-Event-ID`. The dependency-injected `EventBus` persists
ordered, correlated operational telemetry and resumes its event sequence after a
restart. It remains an observation channel: recovery, idempotency and replay use
the journal, never EventBus storage.

## Immutable content publication

Gameplay content is exposed as a versioned catalog rather than mutable CRUD.
Administrative authoring captures the effective configuration into a draft,
validates every canonical artifact hash, and publishes an immutable bundle named
by the manifest SHA-256 revision. Publishing the same canonical draft is
idempotent; a different payload cannot replace an existing revision.

Draft identifiers, optimistic authoring versions and wall-clock timestamps are
operational metadata outside the simulation boundary. A published bundle
contains only its manifest and canonical artifact payloads. Administrative
authoring, reload and activation surfaces are disabled by default; authoritative
gameplay mutations continue to enter through the run or combat command gateway.

## Run collectibles

Cards owned by engine-version 4 runs have a deterministic `cardInstanceId`
separate from their content `definitionId`. Deck zones keep ordered instance-id
lists and derived definition projections, so duplicate cards can move and upgrade
independently. An upgrade appends a versioned content delta to one immutable card
instance; it never edits the shared card definition.

Relics follow the same aggregate rule. A run stores deterministic relic instance
ids, stack counts and a copy of the gameplay properties pinned at acquisition.
`ACQUIRE_RELIC`, `REMOVE_RELIC` and `UPGRADE_CARD` are journaled run commands and
there are no public endpoints that mutate a global relic or card object.

## Branches, simulations and meta projections

An undo never rewrites history. `run.branch.start` creates a new aggregate from
an immutable parent commit, records the parent id/sequence/hash and derives
the branch id from the source deterministic context plus a stable branch key.
When the commit contains an active encounter, the branch deterministically
derives a new combat identity and preserves the parent combat. Semantic replay
reconstructs the same branch from its parent before executing later commands.

Theory-crafting simulations use internal branches whose keys are hashes of the
ordered command list. Commands commit only to the simulation branch; retrying
the same request resumes or returns that branch and the source run is untouched.
Profile statistics, unlocks and achievements are rebuildable projections over
authoritative run snapshots. There is intentionally no public direct-unlock
command.

Daily challenge definitions are versioned content. Starting an attempt writes
the challenge id, mode, fixed seed and effective content revision into the run;
submission reexecutes the journal before accepting its proof. TCG legality,
target evaluation, priority windows and pending actions are read models over
combat state. Reaction proposals and `PASS_PRIORITY` enter exclusively through
the combat command gateway and are replayed from the same immutable snapshots.

## Operational nondeterministic boundaries

Editors, standalone calculators and administrative tools may operate outside a
replayable run. These surfaces are intentionally separate from gameplay and must
receive deterministic inputs before their result can enter an authoritative run:

- `DefaultRandomProvider` uses process entropy; the run path passes
  `DeterministicRandomProvider` explicitly;
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
commit hash-chain tampering, and detection of unclassified ambient inputs.
Semantic replay tests must additionally include at least one run-owned combat
action and prove equality of the final replay hash.
