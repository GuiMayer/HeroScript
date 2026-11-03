# Tactical campaign MVP — validation record

Date: 2026-11-03. Setting: `prototype-test`; normal launch: `prototype_chapter`.
Plan: [implementation](../plans/TACTICAL_CAMPAIGN_MVP_IMPLEMENTATION_PLAN.md).
Rules: [published preset](../game-design/PROTOTYPE_TEST_RULES.md).

## Status

The chapter is implemented. **The MVP is not fully certified**: frequent END_TURN
POSTs still exceed the 100 ms p95 target, and an external comprehension/balance
playtest has not been performed. Automated completion is not proof of fun or balance.

## Deterministic chapter

`PrototypeChapterTests` runs the normal capability policy through actual HTTP
sockets and durable file commits, without cheats, manual state replacement or
development-only gameplay commands. Seed: **8814**; actor: `player`.

It traverses all 12 nodes, completes three victories, deliberately loses at the
boss, changes character equipment and retries. Both rewards are decompiled once:
3 initial Parts become 9, with no duplicate grants after boss defeats. Chapter
completion leaves three durable narrative flags. The initial immutable snapshot
remains unchanged.

The test restarts the host with its store and verifies semantic replay. A separate
host/store then recreates the same published run and executes the exact recorded
command identities/payloads. **All 161 command successor hashes match**, without
normalizing gameplay data or dropping history. The reference hashes before and
after the discovery optimization are identical:

```text
initial d1a11c9d97b59cb63d737d8ba193d4814cb059cd88ea543d403061c60d1fa4de
final   fee6da299c5a510c69a972975cef76f83aa884fb74e68f0714665b8dcfcf495d
```

These hashes are evidence for this content revision, not constants for future
content edits. Tests compare fresh independent executions, not hardcoded hashes.

## REST performance

Measured in Release, .NET 10.0.9, Windows build 26200, Ryzen 5 5600G (6 cores,
12 logical processors), local Kestrel sockets, real durable stores. POST responses
use `Prefer: return=minimal`, as the game client does. Every command is followed
by the real compact session GET and a check that the GET did not change its hash.
The chapter's socket measurements are not in-process reducer timings.

| Operation | Samples | First sample | p50 | p95 | p99 / maximum |
| --- | ---: | ---: | ---: | ---: | ---: |
| PLAY_CARD | 65 | 91.39 ms | 46.36 ms | 83.54 ms | 99.73 ms |
| END_TURN | 50 | 220.13 ms | 184.68 ms | **470.11 ms** | 538.82 ms |
| START_ENCOUNTER | 5 | 421.46 ms | 70.14 ms | 421.46 ms | 421.46 ms |
| Compact session GET | 161 | — | — | 80.00 ms | p99 133.42 / max 249.03 ms |

Excluding the first sample of each operation, START_ENCOUNTER has four samples
with p95/max 72.24 ms; PLAY_CARD p95 83.40 ms; END_TURN p95 remains 470.11 ms.
First-use/JIT work is reported separately and is not hidden by a warm average.
Visual feedback latency is a separate Godot concern, not inferred from these values.

The earlier mixed test run observed PLAY_CARD p95 94.09 ms, END_TURN 639.30 ms and
session 105.29 ms. That run also included other latency cases; the later chapter
run was isolated. Therefore these are observed before/after samples, **not a
controlled causal percentage claim or a guarantee for other machines**.

Diagnostics identified unnecessary passive/end-turn simulations in AI ability-only
discovery. The optimization restricts discovery to explicitly configured action
types, retaining priority-pass fallback, canonical evaluation and unchanged
fingerprints. Wildcard rules/custom resolvers retain complete discovery. Public
legal-action reads remain complete; scoped and complete cache namespaces are
separate. No hashing, validation, RNG, transaction, journal or persistence check
was removed to reach a metric.

Remaining performance gate: profile late END_TURN's automatic actions, status
boundaries, repeated canonical encodings and response discovery with the full
chapter. A small three-turn smoke test is insufficient to certify this budget.
Do not declare the 100 ms target achieved from the PLAY_CARD result alone.

## Presentation evidence

Godot with an isolated real API passed 21 chapter-entry checks: normal setting
launch, dialogue choice, progression, canonical combat mounting, retained hand,
support cadence and input release. Separate payment/support journeys verify the
shared command, preview and animation paths.

The offline matrix exercises shared scenes in EN/PT-BR, 1280×720, 1920×1080 and
2560×1080, text scaling, keyboard/controller focus, contextual drag, autosave and
uncertain receipt recovery. Motion tests cover pause, skip, cancellation and
reduced motion without artificial waits. See [UI checks](../guides/tactical-mvp-ui-validation.md).

The final clean Godot/API run initially exposed an ingress bug: setup ran before
the combat scene entered the tree, so its pending opening presentation was never
dispatched and input stayed locked. The scene now resumes that presentation only
when mounted, using deferred/ready entry. Only that necessary pending fix was
incorporated. Repeating against a new clean API store passed **55 checks**: chapter
entry 21, chosen payment 17, support 17. Both transport and presentation use the
same production components. Four Python/jsonschema tests additionally validate the
updated authoring schema against published definitions/payments and invalid cases.

## Evidence and reproduction

Local evidence lives under `.runtime/tactical-mvp/results/` and corresponding logs;
it is deliberately excluded from version control. Reproducible test sources are
versioned. Run with disposable client/server stores, never player saves:

```powershell
dotnet test tests/API.Tests/API.Tests.csproj -c Release `
  --artifacts-path .runtime/tactical-mvp/release `
  --filter 'FullyQualifiedName~PrototypeChapterTests'
```

Focused sources additionally cover card payment, support, terminal rewards,
workshop mutations, lifecycle/retry, content calculation and older settings.
The progress record lists counts and evidence per completed certification run.

## Clean tree and regression certification

An export made from the exact staged Git tree (without the workspace's pre-existing
uncommitted changes or untracked files) compiled the API in Release with zero
warnings/errors. Its chapter socket test also passed, including restart, semantic
replay and independent-host comparison. Godot imported that clean tree and passed
**114 checks** for the catalog, responsive layout, payment, support, composition
and retry. No existing `.godot` import cache was copied into this export.

The clean source has a different inherited content revision because unrelated
workspace content edits were excluded. Its two independent hosts agreed on all
**169** successor hashes (same seed 8814 within that revision): initial
`db14384ee5e791369d1c6ec7f711b02821217df81955bdae0bb52e0136033c10`, final
`67b89974af7fc0a19aa462ff621fc18e2c039d03f306a7dc5f9a72ef4f061f54`.
Different revisions are not compared as though they must produce the same run.

The deep export path caused a native Godot font-import crash; the same clean tree
imported successfully under a short disposable Temp path. The QA copier was also
fixed to exclude cache directories using paths relative to the project, so a parent
`.runtime` directory cannot accidentally exclude the whole project.

The expanded Core suite passed **1,232 tests**. It caught and fixed an old test's
fixed build-directory assumption and a nullable card-evaluation access in a
manually constructed disposition scenario. The focused discovery suite passed
67 tests, including exact candidate/fingerprint/cache-isolation checks.

The expanded API session completed **21 non-extended cases with no failures**,
covering the two payments/modules, real support, exclusive rewards, three card
patterns, restart/replay and short journeys in default, volatile-core, uno-clash,
circular-table and compartment-lab. That session was nevertheless **aborted at
its configured five-minute limit** while the extended 150-command circular-table
journey was still running. The extended case is not certified; the aborted suite
must not be reported as a completely passing test run.

## Unfulfilled release gates

- END_TURN p95 below 100 ms over a progressed chapter, with representative samples.
- External [playtest](TACTICAL_CAMPAIGN_MVP_PLAYTEST.md), not yet performed: understanding defeat → preparation → retry, payment, Heat,
  retained hand, support and store/decompile; record time, defeats and softlocks.
- The extended circular-table full-history regression timed out and needs its own
  extended test job; only its short deterministic journey is certified here.
- Clean source/import certification is complete; a packaged standalone Godot/server
  export and installer remain outside the evidence above.

No claim of production readiness or external playtest approval is made here.
