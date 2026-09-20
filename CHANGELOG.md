# Changelog

All notable changes to the HeroScript project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

#### Stable Godot snapshot rendering (2026-09-20)

- Combat and Journey retain their screen identity while refreshing the same
  authoritative context; actual navigation transitions still replace screens.
- Existing cards no longer replay entrance animations after every command.
- Journey refresh preserves route/action scroll positions, and contextual
  combat controls reserve stable layout geometry.
- Visual preferences are applied before a refreshed subtree is drawn; async
  card enrichment preserves text scale and rejects stale render generations.
- Client integration documentation now distinguishes snapshot reconciliation
  from rebuilding the complete visual tree.

### Fixed

- Empty combat hands render a centered localized message instead of collapsing
  into vertical one-character lines.
- Updated the Godot layer regression to use the canonical structured
  `UPGRADE_CARD.validPayload.options` contract instead of the removed parallel
  legacy lists.

#### Stability hardening (2026-08-16)

- Fixed culture-dependent parsing in the damage pipeline; JSON numeric literals now use invariant culture.
- Removed a snapshot-retention deadlock in `VersionedRunStateRepository` and added regression coverage.
- Removed the API configuration service circular dependency through lazy validator resolution.
- Restored development and build-time discovery of data-driven configuration files, including `.slnx` project roots.
- Made run persistence snapshots immutable at enqueue time, preventing concurrent writes from sharing a sequence number.
- Disabled Windows Event Log output in API test hosts so tests run without elevated Windows permissions.
- Added SDK selection through `global.json` and updated the documented validation baseline.

### Known limitations

- Reaction/stack content remains reserved and disabled.
- The dashboard is intentionally outside the current validation scope.

### Analysis

#### MVP Viability Analysis (2026-07-12)

**Comprehensive viability analysis for MVP roguelike completed:**
- Engine status: 95% complete (1,248 Core tests + 150 API tests passing, 0 build errors)
- Content status: 10% complete (7/30+ actions, 3/10+ enemies, 0 bosses)
- Timeline: 3-4 weeks for playable MVP with focused effort

**Critical Blockers Identified:**
- ❌ Map Navigation System (0% implemented) - absolute blocker for run progression
- ❌ Event System (0% implemented) - absolute blocker for narrative variety
- ⚠️ Content gap: 23+ actions, 7+ enemies, 1 boss needed

**Documentation Updated:**
- README.md: Added MVP Status section highlighting engine vs content state
- docs/roadmap/README.md: Added critical blockers alert and updated statistics
- docs/roadmap/phases/phase-3.md: Marked as BLOCKED with detailed blocker analysis
- docs/roadmap/phases/phase-4.md: Added content inventory table and gap analysis
- docs/roadmap/analysis/core-modules.md: Updated test counts (553 → 1,248) and MVP viability section
- docs/roadmap/strategic.md: Prioritized MVP roguelike, updated genre support percentages
- All affected docs: Updated dates from 2026-05-23 to 2026-07-12

**Key Findings:**
- Core systems (Combat, Deck, Shop, Status, Gambits) are production-ready
- Map System and Event System are absolute requirements with no workarounds
- Content production is straightforward but requires dedicated sprint (1-2 weeks)
- Estimated implementation: Sprint 1 (Map+Events, 1-2 weeks), Sprint 2 (Content, 1-2 weeks), Sprint 3 (Balance/QA, 3-5 days)

### Added

#### Hardening Phase (2026-06-04 to 2026-06-08)

**Trilha 1 — Event Publishing (commit c15d05f)**
- EventBus now publishes lifecycle events across all managers
- New events: `CombatStartedEvent`, `CombatEndedEvent`, `TurnStartedEvent`, `TurnEndedEvent`
- Status events: `StatusAppliedEvent`, `StatusTickedEvent`, `StatusRemovedEvent`
- Modifier events: `ModifierAppliedEvent`, `ModifierRemovedEvent`
- Gambit events: `GambitEvaluatedEvent`, `GambitActionDecidedEvent`
- Resource events: `ResourceChangedEvent`, `ResourcePoolCreatedEvent`
- Support for event replay and audit trails via EventBus history

**Trilha 2 — API Security (commit b22655b)**
- `AdminKeyMiddleware` protecting administrative endpoints
- Configuration via `Admin:ApiKey` in appsettings or `HERESCRIPT_ADMIN_KEY` environment variable
- Protected endpoints: `/api/action/reload`, `/api/game-resources/reload`, `/api/config/load`, `/api/modifiers/reload`, `/api/gambits/reload`, `/api/status/reload`
- Returns `401 Unauthorized` for missing or invalid admin keys

**Trilha 3 — Logging & Observability (commit c1ab29f)**
- Eliminated all `Console.WriteLine` calls (32 occurrences removed from 5 Core files)
  - `DeltaMerger.cs`, `ResourcePathResolver.cs`, `ConfigValidator.cs`, `FormulaLoader.cs`, `ResourceProviderFactory.cs`
- `CorrelationIdMiddleware` adds `X-Correlation-ID` to all requests/responses
- Structured logging with templates instead of string interpolation
  - Example: `_logger.LogInformation("Effect {EffectId} resolved", effectId)`
- All Core managers now use `ILogger` with proper severity levels (Debug, Information, Warning, Error)
- Removed `ConsoleLogger` fallback in `EntityDefinitionLoader` - DI injection now mandatory
- Updated `appsettings.Development.json` to enable Debug-level logging

**Trilha 4 — Event Persistence (commit 38ecae9)**
- `JsonFileEventStore` with append-only `.jsonl` format
- Configurable via `Persistence:EventStorePath` (default: `data/events/`)
- Event filtering by `eventType`, `runId`, and `afterSequence`
- Dual-write in `EventBus`: in-memory history + optional persistent store
- Failures in event store do not propagate to `Publish<T>()` - fire-and-forget persistence
- Thread-safe append with file locking

**Trilha 5 — Run State Persistence (commit 38ecae9)**
- `JsonFileRunStateRepository` with atomic writes (temp file + rename)
- Each run saved as `{runId}.json` in configurable directory
- Configurable via `Persistence:RunStatePath` (default: `data/runs/`)
- Auto-load on `RunManager.GetRun()` when run not in memory
- Restart-safe: process restart preserves run state and event history
- API methods: `SaveAsync`, `LoadAsync`, `ExistsAsync`, `DeleteAsync`

**Trilha 6 — Test Stability & CI (commit 9162814)**
- Fixed `TestWebApplicationFactory.FindProjectRoot()` with depth limit (12 levels)
- Environment variable support: `HERESCRIPT_REPO_ROOT` for deterministic root finding
- Test categorization: `[Trait("Category", "Unit")]` (125 tests) and `[Trait("Category", "Integration")]` (25 tests)
- Test timeouts configured via `tests/heroscript.runsettings`
  - 30 seconds per test
  - 5 minutes per session
- CI pipeline in `.github/workflows/ci.yml` with separate jobs:
  - Build solution
  - Core Tests (723 tests)
  - API Unit Tests (125 tests, fast)
  - API Integration Tests (25 tests, timeout 5min)
- Resolved timeout/freeze issues in API.Tests
- All tests now passing reliably

**Infrastructure**
- Created `Core.Abstractions` layer with:
  - `IEventStore`, `IRunStateRepository`, `ISnapshotStore` interfaces
  - Typed identifiers: `RunId`, `CombatId`, `EntityId`
  - `ICorrelatedEvent` interface for event correlation
- Created `Core.Infrastructure.Persistence` with JSON implementations
- Registered persistence services in DI container

**Phase 3 — First Slice (2026-06-04)**
- `RunState` and `DeckState` as core of Phase 3
- `RunManager` with automatic persistence
- `RunController` (start, state, deck, hand, draw, discard, shuffle)
- `CardSelectionController` (start, pick, reroll, decompose)
- `ShopController` (start, buy, reroll, sell)
- `PreparationController` (start, apply-modifier)
- `CombatRunCoordinator` (combat ↔ deck/hand integration, real card consumption)
- Transactional rollback for composite operations

**Documentation**
- Added `docs/security.md` - AdminKeyMiddleware configuration and usage
- Added `docs/persistence.md` - JsonFileEventStore and JsonFileRunStateRepository details
- Added `docs/observability.md` - CorrelationIdMiddleware and structured logging
- Updated `README.md` with current project state and hardening phase

### Changed

**Test Suite**
- Core.Tests count updated: 723 tests (was 553)
- API.Tests count updated: 150 tests total (125 unit + 25 integration)
- API.Tests now stable - timeout/freeze issues resolved
- Test execution time reduced via categorization

**Documentation**
- Phase 3 status updated: first slice implemented (was "not implemented")
- README.md synchronized with actual project state

### Technical Details

**Code Quality**
- Zero `Console.WriteLine` in production code
- All logging uses structured templates
- Mandatory `ILogger` injection (no fallbacks)
- Type-safe identifiers prevent ID confusion

**Reliability**
- Atomic writes prevent data corruption
- Dual-write ensures event persistence doesn't block publish
- Test stability improvements enable reliable CI/CD

**Security**
- Admin endpoints protected by middleware
- Environment variable support for secrets
- CORS configured appropriately per environment

---

#### Phase 1: ActionManager API (2026-05-09)
- **ActionController** - Complete REST API for action management
  - `GET /api/action` - List all actions
  - `GET /api/action/{actionId}` - Get action details
  - `GET /api/action/by-type/{type}` - Filter actions by type
  - `GET /api/action/by-tag/{tag}` - Filter actions by tag
  - `POST /api/action/validate` - Validate action definitions
  - `POST /api/action/reload` - Reload action configurations

- **CombatController Extensions** - Integration with ActionManager
  - `GET /api/combat/{combatId}/cost-options` - Get real cost options for actions
  - `GET /api/combat/{combatId}/available-actions` - Get available actions in combat
  - `POST /api/combat/{combatId}/actions/{actionId}/can-afford` - Check action affordability

#### Phase 2: ResourceManager API (2026-05-09)
- **GameResourceController** - Complete REST API for resource management
  - `GET /api/game-resources` - List all resources
  - `GET /api/game-resources/{resourceId}` - Get resource details
  - `GET /api/game-resources/by-category/{category}` - Filter by category
  - `GET /api/game-resources/by-tag/{tag}` - Filter by tag
  - `POST /api/game-resources/validate` - Validate resource definitions
  - `POST /api/game-resources/reload` - Reload resource configurations
  - `POST /api/game-resources/create-pool` - Create resource pool instances
  - `POST /api/game-resources/validate-cost` - Validate resource costs

#### Phase 3: Documentation (2026-05-09)
- XML documentation generation enabled for all API endpoints
- Swagger/OpenAPI documentation available at root URL
- Comprehensive API documentation with examples in multiple languages (C#, JavaScript, Python)
- Usage examples for common scenarios (combat turns, character sheets, modding)

### Changed
- Test infrastructure updated to support new controller dependencies
- ConfigController and ResourceController tests fixed with proper mocking

### Technical Details
- 14 new REST endpoints implemented
- Full integration with existing ActionManager and ResourceManager systems
- Support for runtime configuration reloading (development only)
- Comprehensive error handling and validation
- Multi-language code examples for developer and modder consumption

---

## [Previous Versions]

_Version history before this changelog was established._
