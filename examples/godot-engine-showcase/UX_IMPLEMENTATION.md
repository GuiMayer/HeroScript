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

Next: canonical feedback and inspection, input/accessibility, historical combat and branch navigation.
