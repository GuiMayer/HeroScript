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
