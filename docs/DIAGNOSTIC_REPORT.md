# Diagnostic Report - Integration Tests Timeout

**Date:** 2026-07-05  
**Status:** IDENTIFIED - NOT RESOLVED

## Problem Summary

Integration tests timeout after 60 seconds when calling the `/api/run/start` endpoint. The endpoint is found and the controller method begins execution but never completes.

## Evidence

```
info: Microsoft.AspNetCore.Routing.EndpointMiddleware[0]
      Executing endpoint 'API.Controllers.RunController.StartRun (API)'
info: Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker[102]
      Route matched with {action = "StartRun", controller = "Run"}. 
      Executing controller action with signature Microsoft.AspNetCore.Mvc.IActionResult StartRun(API.Controllers.StartRunRequest) 
      on controller API.Controllers.RunController (API).
      
[TEST HANGS HERE - Never returns]
```

## Root Cause Hypothesis

The `RunManager.StartRun` method is blocking indefinitely. Possible causes:

1. **Missing Configuration Files**: The method attempts to load configuration files that don't exist
   - Likely missing: `default_run` definition in `RunDefinitions.json`
   - May be waiting for file I/O that never completes

2. **Synchronous Blocking on Async Code**: Potential deadlock if:
   - Async method is called with `.Result` or `.Wait()`
   - No `ConfigureAwait(false)` on async calls in a sync context

3. **Infinite Loop in Business Logic**: The run initialization logic may have:
   - Retry loop with no timeout
   - Circular dependency resolution
   - Validation that never completes

4. **Repository/Database Blocking**: If using in-memory or file-based persistence:
   - Lock contention on shared resources
   - Transaction that never commits
   - Initialization that waits for external resources

## Recommended Fixes (NOT IMPLEMENTED)

### Fix 1: Add Timeout to RunManager Operations
```csharp
public Result<RunState> StartRun(string configName, string runDefinitionId, string playerEntityId)
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try 
    {
        // Existing logic with timeout
    }
    catch (OperationCanceledException)
    {
        return Result<RunState>.Failure("Run initialization timeout");
    }
}
```

### Fix 2: Add Logging to Identify Exact Block Point
```csharp
_logger.LogInformation("StartRun: Loading config {Config}", configName);
var config = LoadConfig(configName);
_logger.LogInformation("StartRun: Config loaded, loading run definition {RunDef}", runDefinitionId);
var runDef = LoadRunDefinition(runDefinitionId);
_logger.LogInformation("StartRun: Run definition loaded, creating entity {Entity}", playerEntityId);
// ... etc
```

### Fix 3: Ensure Test Configuration Exists
Create `configs/test/` directory with minimal valid definitions:
- `RunDefinitions.json`
- `Entities.json`
- `Actions.json`

### Fix 4: Review Async/Sync Boundaries
Search codebase for anti-patterns:
```powershell
rg "\.Result\b|\.Wait\(\)" --type cs src/Core/Run/
```

## Impact

**All integration tests are blocked** until this is resolved:
- ✗ RoguelikeGameFlowTests (8 tests)
- ✗ AutoBattlerGameFlowTests (6 tests)
- ✗ TacticalRpgGameFlowTests (6 tests)
- ✗ PuzzleRpgGameFlowTests (5 tests)
- ✗ GameFlowEdgeCaseTests (8 tests)
- ✗ GameFlowEventTrackingTests (5 tests)

**Total: 38 integration tests blocked**

## Workaround

For now, proceed with:
1. Implementing missing endpoints (auto-play)
2. Creating comprehensive API documentation
3. Setting up validation infrastructure

Integration tests can be fixed in a separate session focused on:
- Core business logic debugging
- Configuration setup
- Async/sync review

## Next Steps

1. Commit diagnostic findings
2. Proceed with Phases 1, 2, 4, 5 (documentation and missing endpoints)
3. Schedule separate debugging session for Phase 3 resolution
4. Consider adding timeout middleware to all endpoints as safety measure
