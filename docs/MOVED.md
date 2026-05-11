# Documentation Reorganization - File Mapping

**Date:** 2026-05-11  
**Reason:** Reorganization to improve structure and discoverability

## Files Removed

### Duplicates Consolidated
- `API-ENDPOINTS.md` (deleted) → Content was older version, superseded by `API_ENDPOINTS.md`
- `IMPLEMENTATION_PLAN.md` (deleted) → Empty file (2 bytes)

## Files Moved

This section will be updated as files are moved during reorganization.

### Root → architecture/
- `arquitetura-engine.md` → `architecture/overview.md`
- `session.md` → `architecture/timeline-system.md`
- `core-service-patterns.md` → `architecture/service-patterns.md`

### Root → systems/combat/
- `COMBAT_SYSTEM.md` → `systems/combat/combat-system.md`
- `alternative-costs.md` → `systems/combat/alternative-costs.md`

### Root → systems/damage/
- `DAMAGE_PIPELINE.md` → `systems/damage/damage-pipeline.md`
- `DAMAGE_PIPELINE_EXAMPLES.md` → `systems/damage/damage-examples.md`

### Root → systems/events/
- `EVENTBUS_SYSTEM.md` → `systems/events/eventbus-system.md`
- `EVENTBUS_IMPLEMENTATION_PLAN.md` → `systems/events/eventbus-implementation.md`

### Root → systems/math/
- `CORE_MATH_SYSTEM.md` → `systems/math/math-system.md`
- `API_MATH_EXPRESSION_MODES.md` → `systems/math/expression-modes.md`

### Root → systems/config/
- `CONFIG_SYSTEM.md` → `systems/config/config-system.md`
- `DELTA_REFERENCE.md` → `systems/config/delta-reference.md`

### Root → systems/resources/
- `IMPLEMENTATION_PLAN_RESOURCES.md` → `systems/resources/resource-system.md`

### Root → systems/effects/
- `EFFECT_SYSTEM.md` → `systems/effects/effect-system.md`

### Root → api/
- `API_ENDPOINTS.md` → `api/endpoints.md`

### Root → roadmap/
- `ROADMAP_STRATEGIC.md` → `roadmap/strategic.md`

### roadmap/ → roadmap/phases/
- `roadmap/PHASE_0.md` → `roadmap/phases/phase-0.md`
- `roadmap/PHASE_1.md` → `roadmap/phases/phase-1.md`
- `roadmap/PHASE_2.md` → `roadmap/phases/phase-2.md`
- `roadmap/PHASE_3.md` → `roadmap/phases/phase-3.md`
- `roadmap/PHASE_4.md` → `roadmap/phases/phase-4.md`
- `roadmap/PHASE_5.md` → `roadmap/phases/phase-5.md`
- `roadmap/PHASE_6.md` → `roadmap/phases/phase-6.md`

### roadmap/ → roadmap/analysis/
- `roadmap/CORE_MODULES_ANALYSIS.md` → `roadmap/analysis/core-modules.md`
- `roadmap/RESOURCE_COST_ANALYSIS.md` → `roadmap/analysis/resource-costs.md`
- `roadmap/EVENT_INTEGRATION.md` → `roadmap/analysis/event-integration.md`
- `roadmap/API_CONVENTIONS.md` → `roadmap/analysis/api-conventions.md`

### examples/ → examples/
- `examples/alternative-costs-examples.json` → `examples/alternative-costs.json`

## New Files Created

- `docs/README.md` - Main documentation index
- `systems/combat/turn-phase-system.md` - TurnPhase system documentation
- `systems/entity/entity-system.md` - Entity system documentation
- `systems/validation/validation-system.md` - Validation system documentation

## Quick Reference for Old Paths

If you have bookmarks or references to old paths, use this table:

| Old Path | New Path |
|----------|----------|
| `docs/API-ENDPOINTS.md` | `docs/api/endpoints.md` |
| `docs/API_ENDPOINTS.md` | `docs/api/endpoints.md` |
| `docs/COMBAT_SYSTEM.md` | `docs/systems/combat/combat-system.md` |
| `docs/DAMAGE_PIPELINE.md` | `docs/systems/damage/damage-pipeline.md` |
| `docs/EVENTBUS_SYSTEM.md` | `docs/systems/events/eventbus-system.md` |
| `docs/CONFIG_SYSTEM.md` | `docs/systems/config/config-system.md` |
| `docs/CORE_MATH_SYSTEM.md` | `docs/systems/math/math-system.md` |
| `docs/arquitetura-engine.md` | `docs/architecture/overview.md` |
| `docs/ROADMAP_STRATEGIC.md` | `docs/roadmap/strategic.md` |
