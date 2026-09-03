# HeroScript Validation Checklist

Complete validation checklist for verifying HeroScript API functionality, integration tests, and configuration setup before deployment or release.

---

## Quick Reference

**Status Legend:**
- ✓ = Passing / Complete
- ✗ = Failing / Incomplete
- ⚠ = Warning / Needs Attention
- ⊘ = Blocked / Cannot Test

**Priority Levels:**
- 🔴 Critical - Must pass before any deployment
- 🟡 Important - Should pass before release
- 🟢 Optional - Nice to have, not blocking

---

## 1. API Endpoint Validation

### Core Run Management (🔴 Critical)

- [ ] POST `/api/run/start` returns valid runId
- [ ] GET `/api/run/{runId}/state` retrieves run state
- [ ] POST `/api/run/{runId}/draw` draws cards successfully
- [ ] POST `/api/run/{runId}/discard` discards cards from hand
- [ ] GET `/api/run/{runId}/hand` returns current hand
- [ ] GET `/api/run/{runId}/deck` returns complete deck state
- [ ] POST `/api/run/{runId}/shuffle` shuffles discard into draw pile
- [ ] GET `/api/run/{runId}/snapshots` lists snapshots
- [ ] POST `/api/run/{runId}/undo` reverts to previous snapshot

**Validation Script:**
```bash
# Test run lifecycle
dotnet test --filter "FullyQualifiedName~RunManagement" --logger "console;verbosity=normal"
```

**Manual Test:**
```bash
# Start run
curl -X POST http://localhost:5000/api/run/start \
  -H "Content-Type: application/json" \
  -d '{"configName":"default","runDefinitionId":"default_run","playerEntityId":"player"}'

# Expected: 200 OK with { "runId": "guid", ... }
```

---

### Core Combat Management (🔴 Critical)

- [ ] POST `/api/combat/start` creates combat session
- [ ] GET `/api/combat/{combatId}/state` retrieves combat state
- [ ] POST `/api/combat/{combatId}/action` executes actions
- [ ] POST `/api/combat/{combatId}/end-turn` ends turn properly
- [ ] POST `/api/combat/{combatId}/process-ai-turns` runs AI logic
- [ ] POST `/api/combat/{combatId}/auto-play` completes combat automatically
- [ ] POST `/api/combat/{combatId}/end` finalizes combat
- [ ] GET `/api/combat/{combatId}/history` returns action log
- [ ] GET `/api/v1/combats/{combatId}/cards/evaluations` lists the hand with legality
- [ ] GET `/api/v1/combats/{combatId}/cards/{cardInstanceId}/evaluation` explains costs and targets
- [ ] POST `/api/v1/combats/{combatId}/commands` executes `PLAY_CARD` with optimistic versions

**Validation Script:**
```bash
# Test combat lifecycle
dotnet test --filter "FullyQualifiedName~CombatManagement" --logger "console;verbosity=normal"
```

**Manual Test:**
```bash
# Start combat
curl -X POST http://localhost:5000/api/combat/start \
  -H "Content-Type: application/json" \
  -d '{"heroId":"hero","enemyIds":["enemy_1"],"initialEnergy":3}'

# Expected: 200 OK with combat state
```

---

### Card Selection & Rewards (🟡 Important)

- [ ] POST `/api/run/{runId}/card-selection/start` opens reward screen
- [ ] POST `/api/run/{runId}/card-selection/{id}/pick` adds cards to deck
- [ ] POST `/api/run/{runId}/card-selection/{id}/reroll` rerolls options
- [ ] POST `/api/run/{runId}/card-selection/{id}/decompose/{cardId}` decomposes for resources

**Validation Script:**
```bash
dotnet test --filter "FullyQualifiedName~CardSelection" --logger "console;verbosity=normal"
```

---

### Shop (🟡 Important)

- [ ] POST `/api/run/{runId}/shop/open` opens shop
- [ ] POST `/api/run/{runId}/shop/{id}/buy/{itemId}` purchases items
- [ ] POST `/api/run/{runId}/shop/{id}/reroll` rerolls shop inventory
- [ ] Shop correctly deducts gold
- [ ] Shop prevents purchases with insufficient gold

**Validation Script:**
```bash
dotnet test --filter "FullyQualifiedName~Shop" --logger "console;verbosity=normal"
```

---

### Preparation (🟡 Important)

- [ ] POST `/api/run/{runId}/preparation/start` starts preparation
- [ ] POST `/api/run/{runId}/preparation/{id}/apply/{optionId}` applies buffs
- [ ] Preparation effects persist into next combat

**Validation Script:**
```bash
dotnet test --filter "FullyQualifiedName~Preparation" --logger "console;verbosity=normal"
```

---

### Status Effects (🔴 Critical)

- [ ] POST `/api/status/apply` applies status effects
- [ ] GET `/api/status/{targetId}` retrieves active effects (short alias)
- [ ] GET `/api/status/{targetId}/active` retrieves active effects (explicit)
- [ ] POST `/api/status/add-stacks` adds stacks correctly
- [ ] POST `/api/status/remove-stacks` removes stacks correctly
- [ ] POST `/api/status/{targetId}/tick` decrements duration
- [ ] DELETE `/api/status/{targetId}/status/{statusId}` removes effects
- [ ] Status effects trigger at correct timing (OnTurnStart, OnTurnEnd, etc.)

**Validation Script:**
```bash
dotnet test --filter "FullyQualifiedName~StatusEffect" --logger "console;verbosity=normal"
```

---

### Events (🟡 Important)

- [ ] GET `/api/events` returns event list with filters
- [ ] GET `/api/events/stream` provides SSE stream
- [ ] GET `/api/combat/{combatId}/events` filters by combat
- [ ] DELETE `/api/events` clears history (dev mode)
- [ ] Events include correct metadata (sequence, timestamp, category)

**Validation Script:**
```bash
dotnet test --filter "FullyQualifiedName~Event" --logger "console;verbosity=normal"
```

---

### Formula & Entity (🟡 Important)

- [ ] POST `/api/formula/evaluate` calculates formulas correctly
- [ ] GET `/api/formula` lists available formulas
- [ ] GET `/api/formula/{name}` retrieves formula definition
- [ ] POST `/api/formula/reload` reloads formula cache
- [ ] POST `/api/entity/create` creates entities from definitions
- [ ] GET `/api/entity/definitions` lists entity definitions
- [ ] POST `/api/entity/definitions/validate` validates definitions

**Validation Script:**
```bash
dotnet test --filter "FullyQualifiedName~Formula|Entity" --logger "console;verbosity=normal"
```

---

## 2. Integration Test Suite

### Test Execution Status

**Current Status: ⊘ BLOCKED**

**Reason:** All integration tests timeout due to `RunManager.StartRun` blocking indefinitely. See `docs/DIAGNOSTIC_REPORT.md` for details.

**Blocked Tests:**
- [ ] ⊘ RoguelikeGameFlowTests (8 tests)
- [ ] ⊘ AutoBattlerGameFlowTests (6 tests)
- [ ] ⊘ TacticalRpgGameFlowTests (6 tests)
- [ ] ⊘ PuzzleRpgGameFlowTests (5 tests)
- [ ] ⊘ GameFlowEdgeCaseTests (8 tests)
- [ ] ⊘ GameFlowEventTrackingTests (5 tests)

**Total: 38 tests blocked**

**Resolution Required:**
1. Debug `RunManager.StartRun` blocking issue
2. Verify test configuration files exist in `configs/test/`
3. Check for async/sync deadlocks
4. Add timeout middleware to prevent infinite hangs
5. Re-run full test suite after fix

**Validation Command (when unblocked):**
```bash
# Run all integration tests
dotnet test tests/API.Tests/API.Tests.csproj \
  --filter "Category=Integration" \
  --logger "console;verbosity=normal" \
  --no-build

# Expected: 38/38 passing
```

---

## 3. Configuration Validation

### Required Configuration Files

**For Default Config:**
- [ ] `configs/default/Entities.json` exists and is valid JSON
- [ ] `configs/default/Actions.json` exists and is valid JSON
- [ ] `configs/default/StatusEffects.json` exists and is valid JSON
- [ ] `configs/default/MathFormulas.json` exists and is valid JSON
- [ ] `configs/default/GameResources.json` exists and is valid JSON

**For Test Config:**
- [ ] `configs/test/Entities.json` exists with test entities (hero, enemy_1, enemy_2)
- [ ] `configs/test/Actions.json` exists with test actions
- [ ] `configs/test/RunDefinitions.json` exists with test_run definition
- [ ] `configs/test/CardSelections.json` exists with test_reward definition
- [ ] `configs/test/Shops.json` exists with test shop definitions

**Validation Script:**
```bash
# Check configuration files exist
$configs = @(
    "configs/default/Entities.json",
    "configs/default/Actions.json",
    "configs/test/Entities.json",
    "configs/test/RunDefinitions.json"
)

foreach ($config in $configs) {
    if (Test-Path $config) {
        Write-Host "[✓] $config exists"
        # Validate JSON
        try {
            Get-Content $config | ConvertFrom-Json | Out-Null
            Write-Host "[✓] $config is valid JSON"
        } catch {
            Write-Host "[✗] $config has JSON syntax errors"
        }
    } else {
        Write-Host "[✗] $config is missing"
    }
}
```

---

### Entity Definition Validation

- [ ] All entity definitions have unique IDs
- [ ] All entities reference valid resource IDs in `baseResources`
- [ ] All entities reference valid action IDs in `powers`
- [ ] Entity definitions include required fields (id, name, baseResources)

**Validation Endpoint:**
```bash
POST /api/entity/definitions/validate
{
  "definitionId": "hero"
}
```

---

### Action Definition Validation

- [ ] All action definitions have unique IDs
- [ ] All action costs reference valid resources
- [ ] All action effects have valid formulas
- [ ] Action definitions include required fields (id, name, actionType)

**Validation Endpoint:**
```bash
POST /api/action/validate
{
  "actionId": "basic_attack"
}
```

---

### Formula Validation

- [ ] All formula expressions are syntactically valid
- [ ] All formula variables are defined
- [ ] Formula evaluation returns expected results
- [ ] No circular formula dependencies

**Validation Test:**
```bash
POST /api/formula/evaluate
{
  "formulaName": "match_damage",
  "context": {
    "base_damage": 10,
    "orbs_matched": 5,
    "combo_count": 2,
    "attack_stat": 100
  }
}

# Expected: { "result": <calculated value> }
```

---

## 4. Game Flow Validation

### Roguelike Flow (🟡 Important)

- [ ] Complete run: start → combat → rewards → shop → next combat
- [ ] Deck grows with card selections
- [ ] Gold increases from combat rewards
- [ ] Gold decreases from shop purchases
- [ ] Run ends on hero death
- [ ] Snapshots enable undo functionality

**Manual Test Path:**
1. Start run
2. Start combat
3. Defeat enemy
4. Pick reward card
5. Visit shop, buy item
6. Start next combat
7. Verify deck contains new card
8. Verify gold reflects purchases

---

### Auto-Battler Flow (🟡 Important)

- [ ] Auto-play completes combat without manual input
- [ ] Gambits execute correctly for all units
- [ ] Combat ends when one side is defeated
- [ ] Events track all AI decisions
- [ ] Combat statistics are accurate (totalTurns, totalActions)

**Manual Test:**
```bash
POST /api/combat/start
# (get combatId)

POST /api/combat/{combatId}/auto-play

# Expected: Combat completes in <100 turns with winner
```

---

### Tactical RPG Flow (🟢 Optional)

- [ ] Turn phases advance correctly
- [ ] Positioning affects attack range
- [ ] Status effects apply at correct timing
- [ ] Movement and action phases work
- [ ] Turn order based on initiative

---

### Puzzle RPG Flow (🟢 Optional)

- [ ] Formula evaluation calculates match damage
- [ ] Resource generation from puzzle matches
- [ ] Action costs consume correct resources
- [ ] Can-afford checks prevent invalid actions
- [ ] Combo multipliers apply correctly

---

## 5. Error Handling & Edge Cases

### Expected Error Responses

- [ ] 404 Not Found for invalid IDs
- [ ] 400 Bad Request for invalid parameters
- [ ] 400 Bad Request for insufficient resources
- [ ] 400 Bad Request for invalid action state
- [ ] 500 Internal Server Error handled gracefully

**Test Invalid Requests:**
```bash
# Invalid run ID
GET /api/run/00000000-0000-0000-0000-000000000000/state
# Expected: 404 Not Found

# Insufficient energy
POST /api/combat/{combatId}/action
{
  "actorId": "hero",
  "actionId": "expensive_spell",
  "targetId": "enemy_1"
}
# Expected: 400 Bad Request with "Not enough energy"
```

---

### Edge Cases

- [ ] Empty deck triggers shuffle
- [ ] Zero health marks entity as dead
- [ ] Combat ends when all enemies dead
- [ ] Combat ends when hero dead
- [ ] Reroll with zero rerolls returns error
- [ ] Buy with insufficient gold returns error
- [ ] Action on dead target returns error
- [ ] Status effect stacks respect max stacks
- [ ] Duration 0 status effects are removed

---

## 6. Performance & Scalability

### Response Time Benchmarks (🟢 Optional)

- [ ] Simple GET requests < 100ms
- [ ] Combat action execution < 500ms
- [ ] Auto-play combat < 30 seconds
- [ ] Event queries with 1000+ events < 1 second

**Performance Test:**
```bash
# Measure response time
Measure-Command {
    Invoke-RestMethod -Uri "http://localhost:5000/api/combat/{combatId}/state" -Method Get
}
```

---

### Concurrency (🟢 Optional)

- [ ] Multiple concurrent runs don't interfere
- [ ] Multiple concurrent combats don't interfere
- [ ] Event stream handles multiple clients
- [ ] Repository operations are thread-safe

---

## 7. Documentation Completeness

### API Documentation (✓ Complete)

- [x] `docs/API_ENDPOINTS.md` - Complete endpoint reference
- [x] `docs/GAME_FLOW.md` - Game flow workflows
- [x] `docs/CONFIGURATION_GUIDE.md` - Configuration setup
- [x] `docs/INTEGRATION_TESTING_GUIDE.md` - Testing guide
- [x] `docs/DIAGNOSTIC_REPORT.md` - Known issues

---

### Code Documentation (🟡 Important)

- [ ] Controller methods have XML summaries
- [ ] Request/response DTOs are documented
- [ ] Complex business logic has explanatory comments
- [ ] API error codes are documented
- [ ] Configuration schemas are documented

---

## 8. Deployment Readiness

### Pre-Deployment Checklist (🔴 Critical)

- [ ] All critical tests passing
- [ ] No known timeout or blocking issues
- [ ] Configuration files validated
- [ ] Error handling tested
- [ ] Logging properly configured
- [ ] Health check endpoint responds
- [ ] Environment variables documented
- [ ] Database migrations (if any) ready
- [ ] Security review completed

---

### Post-Deployment Validation (🔴 Critical)

- [ ] Health check returns 200 OK
- [ ] Sample run completes successfully
- [ ] Sample combat completes successfully
- [ ] Event streaming works
- [ ] Logs show no errors
- [ ] Performance within acceptable range

**Quick Smoke Test:**
```bash
# Health check
curl http://your-deployment-url/api/health

# Start run
curl -X POST http://your-deployment-url/api/run/start \
  -H "Content-Type: application/json" \
  -d '{"configName":"default","runDefinitionId":"default_run","playerEntityId":"player"}'

# If both succeed, basic functionality is working
```

---

## 9. Known Issues & Workarounds

### Critical Issues

**Issue: Integration Tests Timeout**
- **Status:** ⊘ Blocked
- **Severity:** 🔴 Critical
- **Details:** See `docs/DIAGNOSTIC_REPORT.md`
- **Workaround:** Manual endpoint testing, avoid `/api/run/start` in tests until fixed
- **Resolution ETA:** Requires dedicated debugging session

---

### Minor Issues

**Issue: XML Comments Warnings**
- **Status:** ⚠ Warning
- **Severity:** 🟢 Optional
- **Details:** Some properties lack XML documentation comments
- **Workaround:** Suppress warnings or add documentation
- **Resolution:** Cosmetic, not blocking

---

## 10. Validation Summary

### Current Status

| Category | Status | Critical Issues | Notes |
|---|---|---|---|
| API Endpoints | ⚠ Partial | RunManager blocks | Most endpoints functional |
| Integration Tests | ⊘ Blocked | All tests timeout | Requires fix before release |
| Configuration | ✓ Complete | None | Well documented |
| Documentation | ✓ Complete | None | Comprehensive guides |
| Error Handling | ✓ Good | None | Standard error codes |
| Performance | ⚠ Unknown | Cannot benchmark | Blocked by test issues |

---

### Sign-Off Criteria

**Before Release:**
- [ ] Integration test suite 100% passing
- [ ] All 🔴 Critical validations passing
- [ ] All 🟡 Important validations passing
- [ ] Known issues documented with workarounds
- [ ] Deployment smoke test successful

**Current Recommendation:**
- **Status:** ⚠ NOT READY FOR RELEASE
- **Blockers:** Integration tests timeout (38 tests blocked)
- **Next Steps:**
  1. Debug and fix `RunManager.StartRun` blocking issue
  2. Re-run integration test suite
  3. Complete performance benchmarks
  4. Execute pre-deployment checklist
  5. Final sign-off

---

## Appendix: Quick Validation Scripts

### Full Validation Suite

```bash
# 1. Check configuration files
Get-ChildItem -Path "configs" -Recurse -Filter "*.json" | ForEach-Object {
    try {
        Get-Content $_.FullName | ConvertFrom-Json | Out-Null
        Write-Host "[✓] $($_.FullName)"
    } catch {
        Write-Host "[✗] $($_.FullName) - Invalid JSON"
    }
}

# 2. Build project
dotnet build

# 3. Run unit tests
dotnet test --filter "Category=Unit"

# 4. Run integration tests (when unblocked)
dotnet test --filter "Category=Integration"

# 5. Start API server
dotnet run --project src/API/API.csproj

# 6. Run smoke tests
curl http://localhost:5000/api/health
```

---

### Continuous Validation

Set up automated validation in CI/CD:

```yaml
# .github/workflows/validation.yml
name: Validation

on: [push, pull_request]

jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
      - name: Build
        run: dotnet build
      - name: Run Tests
        run: dotnet test --filter "Category=Integration"
      - name: Validate Configs
        run: |
          find configs -name "*.json" -exec jq empty {} \;
```

---

**Document Version:** 1.0.0  
**Last Updated:** 2026-07-05  
**Status:** Current validation baseline with known blockers documented
