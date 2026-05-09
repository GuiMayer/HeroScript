# Plan: Comprehensive Unit Tests for Damage Pipeline

## Context

After exploring the codebase, I found:

### Existing Test Infrastructure

* **Test framework** : xUnit with FluentAssertions (based on existing tests in `tests/Core.Tests/`)
* **Test patterns** : Existing tests for Combat, Resources, Math, and Config systems
* **Mock infrastructure** : Tests use in-memory implementations and mocks

### Current Damage Pipeline Components

Located in `src/Core/Damage/`:

* `DamageContext` - Immutable state container
* `BucketDefinition` - Configuration model
* `FilterCondition` - 4 filter types (has_tag, has_modifier, metadata_exists, metadata_compare)
* `BucketOperation` - 10 operation types (set_base,
  multiply, add, multiply_modifier, roll_crit, multiply_crit,
  apply_mitigation, eval_formula, etc.)
* `GenericBucketProcessor` - Core processor
* `PipelineManager` - Pipeline orchestrator
* `DamageCalculator` - Facade for combat integration
* Events: `DamageCalculatedEvent`, `BucketProcessedEvent`, `PipelineReloadedEvent`

### Configuration Files

* `config/DamagePipeline.json` - Default 7-bucket pipeline
* `config/MathFormulas.json` - Armor mitigation formula

---

## Plan

### 1. Create Test Infrastructure

 **File** : `tests/Core.Tests/Damage/DamageTestHelpers.cs`

Create helper class with:

* Factory methods for creating test contexts
* Mock implementations of dependencies (IMathEngine, IEventBus, ILogger)
* Assertion helpers for comparing DamageContext objects
* Builder pattern for fluent test setup

### 2. Unit Tests - DamageContext

 **File** : `tests/Core.Tests/Damage/DamageContextTests.cs`

Test immutability and state management:

* Constructor initializes all properties correctly
* Immutable updates create new instances
* Tags are properly copied (not shared references)
* Modifiers are properly copied
* Metadata is properly copied
* Edge cases: empty collections, null handling

 **Estimated** : ~10 tests

### 3. Unit Tests - FilterCondition Evaluation

 **File** : `tests/Core.Tests/Damage/FilterConditionTests.cs`

Test all 4 filter types:

* **has_tag** : present, absent, case sensitivity
* **has_modifier** : present, absent, zero value
* **metadata_exists** : present, absent, null value
* **metadata_compare** : all operators (equals, greater_than, less_than, greater_or_equal, less_or_equal)
* Edge cases: empty strings, negative numbers, type mismatches

 **Estimated** : ~20 tests

### 4. Unit Tests - BucketOperation Execution

 **File** : `tests/Core.Tests/Damage/BucketOperationTests.cs`

Test all 10 operation types in isolation:

* **set_base** : from base_damage, from constant
* **multiply** : constant, modifier, metadata, attacker_stat, target_stat
* **add** : constant, modifier, metadata
* **multiply_modifier** : present, absent, zero, negative
* **roll_crit** : guaranteed crit (100%), guaranteed fail (0%), 50% chance (mock random)
* **multiply_crit** : tier 0, tier 1, tier 2+, with tier_bonus
* **apply_mitigation** : various armor values, formula evaluation
* **eval_formula** : valid formula, missing variables, formula not found
* **set_metadata** : overwrite, new key
* **conditional_multiply** : condition true, condition false

 **Estimated** : ~40 tests

### 5. Unit Tests - GenericBucketProcessor

 **File** : `tests/Core.Tests/Damage/GenericBucketProcessorTests.cs`

Test processor behavior:

* Bucket skipped when filters fail
* Bucket executes when filters pass
* Operations execute sequentially
* Events emitted when `emit_events: true`
* Events not emitted when `emit_events: false`
* Multiple operations in sequence
* Context immutability preserved
* Error handling for invalid operations

 **Estimated** : ~15 tests

### 6. Unit Tests - PipelineConfigLoader

 **File** : `tests/Core.Tests/Damage/PipelineConfigLoaderTests.cs`

Test JSON loading:

* Load valid configuration
* Load with missing optional fields (use defaults)
* Load with invalid JSON (throw exception)
* Load non-existent file (throw exception)
* Validate bucket structure
* Validate operation structure
* Validate filter structure

 **Estimated** : ~10 tests

### 7. Unit Tests - PipelineManager

 **File** : `tests/Core.Tests/Damage/PipelineManagerTests.cs`

Test pipeline orchestration:

* Execute pipeline with multiple buckets
* Cache processors after first load
* Reload configuration clears cache
* Reload emits PipelineReloadedEvent (success)
* Reload emits PipelineReloadedEvent (failure)
* Thread-safety of cache access
* Empty pipeline (no buckets)

 **Estimated** : ~12 tests

### 8. Unit Tests - DamageCalculator

 **File** : `tests/Core.Tests/Damage/DamageCalculatorTests.cs`

Test facade integration:

* Calculate damage with valid inputs
* Emits DamageCalculatedEvent
* Extracts crit_tier from metadata
* Returns non-negative damage (floor at 0)
* Handles missing pipeline gracefully
* Null action throws exception
* Null attacker throws exception
* Null target throws exception

 **Estimated** : ~10 tests

### 9. Integration Tests - Game System Configurations

 **File** : `tests/Core.Tests/Damage/GameSystemIntegrationTests.cs`

Create 4 test JSON configurations and verify end-to-end calculations:

#### 9.1. Path of Exile Style

 **Config** : `tests/Core.Tests/Damage/Configs/PathOfExile.json`

Pipeline:

1. Base damage
2. Added flat physical damage
3. Increased physical damage (additive)
4. More physical damage (multiplicative)
5. Critical strike (with multi)
6. Armor mitigation (complex formula)
7. Resistances (75% cap)

 **Tests** :

* Physical attack with increased damage
* Spell with added damage and more multipliers
* Critical strike with 500% multi
* Armor mitigation at various levels
* Resistance capping at 75%

 **Estimated** : ~8 tests

#### 9.2. Genshin Impact Style

 **Config** : `tests/Core.Tests/Damage/Configs/GenshinImpact.json`

Pipeline:

1. Base damage (character ATK * talent multiplier)
2. Elemental damage bonus
3. Critical (rate + damage)
4. Enemy defense reduction
5. Elemental resistance
6. Reaction multiplier (vaporize 2x, melt 1.5x)

 **Tests** :

* Normal attack without reactions
* Pyro attack with elemental bonus
* Critical hit with 200% crit damage
* Vaporize reaction (2x multiplier)
* Melt reaction (1.5x multiplier)
* Defense and resistance interaction

 **Estimated** : ~8 tests

#### 9.3. Card Game Style (Hearthstone/Slay the Spire)

 **Config** : `tests/Core.Tests/Damage/Configs/CardGame.json`

Pipeline:

1. Base damage (card value)
2. Strength modifier (additive)
3. Vulnerable debuff (1.5x if present)
4. Weak debuff (0.75x if present)
5. Block reduction (subtract from damage)
6. Thorns reflection (metadata only)

 **Tests** :

* Basic attack with strength
* Attack against vulnerable enemy
* Attack while weakened
* Block absorption (partial and full)
* Multiple debuffs stacking
* Thorns damage calculation

 **Estimated** : ~8 tests

#### 9.4. Default HeroScript Style

 **Config** : Use existing `config/DamagePipeline.json`

 **Tests** :

* Basic physical attack
* Spell with multiple damage types
* Critical hit mechanics
* Armor mitigation
* Stacking multiple increased damage modifiers
* Attack without can_crit tag

 **Estimated** : ~6 tests

### 10. Edge Case Tests

 **File** : `tests/Core.Tests/Damage/EdgeCaseTests.cs`

Test boundary conditions:

* Zero base damage
* Negative damage (should floor at 0)
* Extremely large damage values (overflow protection)
* Empty tags list
* Empty modifiers dictionary
* Empty metadata dictionary
* Division by zero in formulas
* Missing required metadata keys
* Invalid value sources
* Circular dependencies in formulas

 **Estimated** : ~15 tests

### 11. Event Tests

 **File** : `tests/Core.Tests/Damage/EventTests.cs`

Test event emission:

* DamageCalculatedEvent contains correct data
* BucketProcessedEvent contains correct delta
* PipelineReloadedEvent on success
* PipelineReloadedEvent on failure
* Events not emitted when emit_events: false
* Multiple subscribers receive events
* Event timestamps are set

 **Estimated** : ~10 tests

### 12. Performance Tests (Optional)

 **File** : `tests/Core.Tests/Damage/PerformanceTests.cs`

Benchmark critical paths:

* Single damage calculation (< 1ms target)
* 1000 damage calculations (< 100ms target)
* Pipeline reload (< 50ms target)
* Large pipeline (20+ buckets)

 **Estimated** : ~5 tests

---

## Risks & Mitigations

### Risk 1: Random Number Testing (Crit Rolls)

 **Problem** : `roll_crit` uses `Random.Shared`, making tests non-deterministic.

 **Mitigation** :

* Create `IRandomProvider` interface
* Inject into `GenericBucketProcessor`
* Use mock with fixed seed for tests
* Test boundary cases (0%, 100%) instead of probabilistic middle ground

### Risk 2: MathEngine Formula Dependencies

 **Problem** : Tests depend on `MathEngine` and formula definitions.

 **Mitigation** :

* Mock `IMathEngine` for unit tests
* Use real `MathEngine` only for integration tests
* Create minimal test formulas in test configs
* Document formula dependencies clearly

### Risk 3: JSON Configuration Maintenance

 **Problem** : 4 game system configs = maintenance burden.

 **Mitigation** :

* Keep configs minimal (5-7 buckets each)
* Document each config's purpose clearly
* Use configs as both tests AND examples
* Consider moving to `docs/examples/` after tests pass

### Risk 4: Test Execution Time

 **Problem** : 150+ tests might be slow.

 **Mitigation** :

* Use `[Trait]` attributes to categorize tests
* Separate unit tests (fast) from integration tests (slower)
* Run unit tests in CI, integration tests on-demand
* Parallelize test execution (xUnit default)

### Risk 5: Floating Point Precision

 **Problem** : Damage calculations use `float`, precision issues in assertions.

 **Mitigation** :

* Use `BeApproximately()` from FluentAssertions
* Define acceptable epsilon (0.01 for damage)
* Document precision expectations
* Consider `decimal` for currency-like values (future)

### Risk 6: Event Bus Testing

 **Problem** : Event emission is fire-and-forget, hard to verify.

 **Mitigation** :

* Create `TestEventBus` that captures events
* Assert on captured event list
* Verify event order and content
* Test both sync and async subscribers

---

## Test Coverage Goals

* **Unit tests** : 90%+ coverage of core logic
* **Integration tests** : All 4 game systems working end-to-end
* **Edge cases** : All known failure modes covered
* **Events** : All 3 event types verified

 **Total estimated tests** : ~150-170 tests

---

## Questions for User

1. **Random testing strategy** : Should I create `IRandomProvider` interface for testability, or use fixed 0%/100% crit tests only?
2. **Config location** : Should game system configs live in `tests/Core.Tests/Damage/Configs/` or `docs/examples/configs/` (dual purpose)?
3. **Performance tests** : Are performance benchmarks important now, or defer to later optimization phase?
4. **Test data builders** : Do you want fluent builder
   pattern for test setup (more code, better readability) or simple factory
   methods (less code, adequate)?
5. **Coverage threshold** : Should I add coverage enforcement (e.g., fail build if < 80% coverage)?
