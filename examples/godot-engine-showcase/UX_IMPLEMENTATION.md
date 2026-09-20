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

## Stage 4 — input and accessibility

- Explicit initial focus, directional neighbors and Tab traversal; pause receives focus and restores the previous control on resume.
- Keyboard and controller bindings coexist, support remapping/reset, and reject duplicate or UI-reserved inputs. Controller A/B and D-pad retain navigation roles. Default next-animation keyboard shortcut is F (Space remains UI confirmation).
- Text scale (90–120%), visible focus borders and opaque high-contrast controls supplement reduced motion and manual animation playback.
- Connection settings validate addresses and reject changes while an operation is unresolved; an explicitly edited address is persisted even when launched with a temporary port override.
- Automated tests inject physical Tab and controller D-pad events, and validate remap/reset/conflict behavior. Physical controller hardware has not been tested.

## Stage 5 — historical combat and branch navigation

- Cursor and paginated command list select canonical post-command snapshots, normalized by the gateway into the same actor components as live combat.
- Historical frame playback is a separate cursor. It highlights recorded applications but does not reconstruct intermediate authoritative states.
- The actual nested branch tree identifies the active run. Creating a branch and activating an existing one are explicit operations; no head is overwritten.
- Mode permissions govern historical inspection and forking. Slider reads are debounced, guarded against stale responses and cached in a bounded 32-state local cache.
- Real-engine UI tests verify history loading, frame playback isolation, creating a playable branch and switching back to the unchanged origin. Offline tests cover sparse pagination and aggregate normalization.
- Final visual checks include English combat, high contrast/large text and Portuguese timeline. Target controls precede scrollable actor detail so they remain easy to reach.

## Stage 6 — stable snapshot rendering

- Combat and Journey retain their screen identity while their context remains active. Accepted commands refresh the current snapshot without clearing the navigation host.
- Screen refresh is synchronous from the renderer's point of view: typography and contrast are applied before drawing, and stale asynchronous card-inspection responses are rejected by render generation.
- Existing cards do not replay their entrance animation. Card enrichment applies text scale before replacing a visible subtree.
- Contextual combat controls reserve stable layout slots. The empty-hand state has a readable horizontal minimum instead of collapsing to one character per line.
- Journey refresh preserves route/action scroll positions and does not replay offer-card animations. Actual context transitions still replace the screen.
- Regressions assert persistent screen identity, stable footer geometry, preserved scroll, stable 120% card fonts and the absence of vertical text.

All six stages preserve the engine as the only authority for gameplay. API/engine code did not require changes for this interaction work; existing canonical projections supplied the necessary data.

## Final validation

- Godot offline layer tests: passed, including concurrency/recovery, defensive copies, timeline normalization, localization and remapping.
- Real-engine UI smoke: passed, including physical Tab, simulated controller navigation, persistent combat refresh, stable controls, legal card play, frame/pause isolation and branch round-trip.
- Full campaign polish run: passed progression, rewards, shop purchase, preparation, upgrades, combats, automatic travel, final replay, history and pause abandonment with persistent Journey refresh.
- Layout matrix: passed at 1280×720, 1440×900 and 2560×1080 in English/Portuguese with text scales 1.0 and 1.2. The offline resolution suite additionally covers 1920×1080, 2560×1440, 3440×1440 and 3840×2160.
- Smoke launcher now rejects Godot script errors even when Godot exits with code zero.
- Current latency measurements live in the performance document instead of being duplicated here.
- Manual hardware gamepad validation remains recommended before distributing the demo.
