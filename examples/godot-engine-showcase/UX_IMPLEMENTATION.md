# Showcase architecture and interaction improvements

## Stage 1 — operation and screen lifecycles

- One session operation at a time; atomic projections and generation checks reject stale reads.
- Uncertain command responses retain the exact envelope for explicit reconnect/recovery. Recovery is in-memory; restarting the application requires loading the persisted run.
- Navigation epochs and screen lifetime guards prevent completed requests from navigating away from a newer screen.
- Connection changes cannot redirect an in-flight or uncertain command to a different engine.
- Verified with offline injected-transport regressions (late response, concurrency, lost response after commit, exact-envelope retry) and the real-engine UI smoke test.

## Stage 2 — passive projections and composition

- `CombatPresenter` captures one defensive snapshot and indexes cards, actors and legal choices. Formatting uses engine outcomes; no effect arithmetic or gameplay rules are reproduced.
- Explicit client input states distinguish selection, submission, animation, recovery and pause.
- `ActorPanel` and `ResourceView` are passive components with semantic selection signals, ready for reuse in historical inspection.
- Offline tests cover snapshot isolation, indexed choice isolation and presentation input states; UI smoke exercises the composed screen.

## Stage 3 — canonical feedback and inspection

- Cards display actual projected resource changes and uncertainty. The detailed inspector lists legal choices, upgrades and permitted context sources; raw diagnostics are optional.
- Bulk card evaluations are fetched separately from the critical input path and accepted only for the displayed sequence. Unavailable cards remain inspectable, showing engine reasons, without guessing affordability or targeting.
- Intent previews and receipt feedback use canonical applications. Every participant is visible; directed relationship metadata only affects styling, never targeting legality.
- Pile inspection lists canonical cards in alphabetical order, not hidden draw order. JSON presentation settings choose resource bars versus counters without changing resource rules.
- Live UI regression tests cover bulk evaluations, instance identities and actor coverage. Combat was also rendered and visually checked.

Next: input/accessibility, historical combat and branch navigation.
