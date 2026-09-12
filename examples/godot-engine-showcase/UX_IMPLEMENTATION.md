# Showcase architecture and interaction improvements

## Stage 1 — operation and screen lifecycles

- One session operation at a time; atomic projections and generation checks reject stale reads.
- Uncertain command responses retain the exact envelope for explicit reconnect/recovery. Recovery is in-memory; restarting the application requires loading the persisted run.
- Navigation epochs and screen lifetime guards prevent completed requests from navigating away from a newer screen.
- Connection changes cannot redirect an in-flight or uncertain command to a different engine.
- Verified with offline injected-transport regressions (late response, concurrency, lost response after commit, exact-envelope retry) and the real-engine UI smoke test.

Next: passive combat projection/components, canonical feedback and inspection, input/accessibility, historical combat and branch navigation.
