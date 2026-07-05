# HeroScript Integration Testing Guide

Complete guide to writing and running integration tests for HeroScript, including patterns, best practices, troubleshooting, and the GameEngineClientSimulator API reference.

---

## Table of Contents

1. [Overview](#overview)
2. [Test Infrastructure](#test-infrastructure)
3. [Writing Integration Tests](#writing-integration-tests)
4. [GameEngineClientSimulator API](#gameengineclientsimulator-api)
5. [Common Test Patterns](#common-test-patterns)
6. [Troubleshooting](#troubleshooting)
7. [CI/CD Integration](#cicd-integration)

---

## Overview

### What are Integration Tests?

Integration tests verify that multiple components of HeroScript work together correctly. Unlike unit tests that test individual methods in isolation, integration tests:

- Start the full API server
- Make real HTTP requests
- Execute complete game workflows
- Verify end-to-end behavior

### Test Categories

**1. Game Flow Tests** - Complete gameplay scenarios
- `RoguelikeGameFlowTests` - Full run from start to victory/defeat
- `AutoBattlerGameFlowTests` - Team setup and auto-combat
- `TacticalRpgGameFlowTests` - Turn-based tactical battles
- `PuzzleRpgGameFlowTests` - Puzzle mechanics with combat

**2. Edge Case Tests** - Boundary conditions and error handling
- `GameFlowEdgeCaseTests` - Invalid inputs, missing resources, etc.

**3. Event Tracking Tests** - Event system verification
- `GameFlowEventTrackingTests` - Event generation and streaming

---

## Test Infrastructure

### Project Structure

```
tests/API.Tests/
├── Integration/
│   ├── GameEngineIntegrationTestBase.cs      # Base class for all tests
│   ├── GameEngineClientSimulator.cs          # HTTP client wrapper
│   ├── RoguelikeGameFlowTests.cs
│   ├── AutoBattlerGameFlowTests.cs
│   ├── TacticalRpgGameFlowTests.cs
│   ├── PuzzleRpgGameFlowTests.cs
│   ├── GameFlowEdgeCaseTests.cs
│   └── GameFlowEventTrackingTests.cs
├── Helpers/
│   └── TestWebApplicationFactory.cs           # Test server setup
└── API.Tests.csproj
```

### TestWebApplicationFactory

Creates an in-memory test server for integration tests:

```csharp
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Use in-memory repositories
            services.AddSingleton<ICombatRepository, InMemoryCombatRepository>();
            services.AddSingleton<IRunRepository, InMemoryRunRepository>();
            
            // Override configuration
            services.Configure<GameConfiguration>(config =>
            {
                config.ConfigName = "test";
            });
        });
    }
}
```

### Base Test Class

All integration tests inherit from `GameEngineIntegrationTestBase`:

```csharp
[Trait("Category", "Integration")]
public abstract class GameEngineIntegrationTestBase : IClassFixture<TestWebApplicationFactory>
{
    protected readonly HttpClient RawClient;
    protected readonly GameEngineClientSimulator Client;

    protected GameEngineIntegrationTestBase(TestWebApplicationFactory factory)
    {
        RawClient = factory.CreateClient();
        Client = new GameEngineClientSimulator(RawClient);
    }
    
    // Helper methods for assertions, setup, etc.
}
```

---

## Writing Integration Tests

### Basic Test Structure

```csharp
public class MyGameFlowTests : GameEngineIntegrationTestBase
{
    public MyGameFlowTests(TestWebApplicationFactory factory) : base(factory) { }
    
    [Fact]
    public async Task TestName_Scenario_ExpectedResult()
    {
        // Arrange
        var (runId, runState) = await SetupRunAsync();
        
        // Act
        var result = await Client.SomeActionAsync(runId);
        
        // Assert
        Assert.NotNull(result);
        AssertJsonPropertyEquals(result, "status", "Success");
    }
}
```

### Prerequisites

Before writing tests, ensure configuration files exist:

**Required for Tests:**
- `configs/test/Entities.json` - Test entities (hero, enemies)
- `configs/test/Actions.json` - Test actions
- `configs/test/RunDefinitions.json` - Test run definitions
- `configs/test/CardSelections.json` - Test reward definitions
- `configs/test/Shops.json` - Test shop definitions

**Minimal Test Entity:**
```json
{
  "id": "hero",
  "name": "Test Hero",
  "baseResources": {
    "health": { "current": 100, "maximum": 100 },
    "energy": { "current": 3, "maximum": 3 }
  },
  "powers": ["basic_attack"]
}
```

### Example: Complete Roguelike Test

```csharp
[Fact]
public async Task CompleteRoguelikeRun_WithCombatAndRewards_Succeeds()
{
    // 1. Start run
    var runId = await Client.StartRunAsync("test", "test_run", "hero");
    Assert.NotEqual(Guid.Empty, runId);
    
    // 2. Start combat
    var combatId = await Client.StartCombatAsync("hero", new[] { "enemy_1" });
    var combatState = await Client.GetCombatStateAsync(combatId);
    AssertCombatStateValid(combatState);
    
    // 3. Execute combat actions
    while (GetJsonBool(combatState, "isActive"))
    {
        // Player attacks
        await Client.ExecuteActionAsync(combatId, "hero", "basic_attack", "enemy_1");
        
        // End turn
        await Client.EndTurnAsync(combatId);
        
        // AI turn
        await Client.ProcessAiTurnsAsync(combatId);
        
        // Check state
        combatState = await Client.GetCombatStateAsync(combatId);
    }
    
    // 4. Verify victory
    var status = GetJsonString(combatState, "status");
    Assert.Equal("Victory", status);
    
    // 5. Card selection
    var selectionId = await Client.StartCardSelectionAsync(runId, "test_reward");
    await Client.PickCardsAsync(runId, selectionId, new[] { "fireball" });
    
    // 6. Verify deck updated
    var deckState = await Client.GetDeckStateAsync(runId);
    var deck = deckState.GetProperty("drawPile");
    Assert.Contains(deck.EnumerateArray(), card => card.GetString() == "fireball");
}
```

---

## GameEngineClientSimulator API

The `GameEngineClientSimulator` wraps HTTP requests with strongly-typed methods:

### Run Management

```csharp
// Start new run
Task<Guid> StartRunAsync(
    string configName = "default",
    string runDefinitionId = "default_run",
    string playerEntityId = "player"
)

// Get run state
Task<JsonElement> GetRunStateAsync(Guid runId)

// Get deck state
Task<JsonElement> GetDeckStateAsync(Guid runId)

// Get hand
Task<JsonElement> GetHandAsync(Guid runId)

// Draw cards
Task<JsonElement> DrawCardsAsync(Guid runId, int count = 5)

// Discard cards
Task<JsonElement> DiscardCardsAsync(Guid runId, string[] cardIds)

// Shuffle deck
Task<JsonElement> ShuffleDeckAsync(Guid runId)

// Snapshots
Task<JsonElement> GetSnapshotsAsync(Guid runId)
Task<JsonElement> GetSnapshotAsync(Guid runId, int sequence)
Task<JsonElement> UndoToSnapshotAsync(Guid runId, int targetSequence)
```

### Combat Management

```csharp
// Start combat
Task<Guid> StartCombatAsync(
    string heroId,
    string[] enemyIds,
    int initialEnergy = 3
)

// Get combat state
Task<JsonElement> GetCombatStateAsync(Guid combatId)

// Execute action
Task<JsonElement> ExecuteActionAsync(
    Guid combatId,
    string actorId,
    string actionId,
    string targetId,
    Dictionary<string, int>? costChoices = null
)

// End turn
Task<JsonElement> EndTurnAsync(Guid combatId)

// Process AI turns
Task<JsonElement> ProcessAiTurnsAsync(Guid combatId)

// Auto-play combat
Task<JsonElement> AutoPlayCombatAsync(Guid combatId)

// End combat
Task<JsonElement> EndCombatAsync(Guid combatId)

// Get action history
Task<JsonElement> GetCombatHistoryAsync(Guid combatId)

// Get available actions
Task<JsonElement> GetAvailableActionsAsync(
    Guid combatId,
    string actorId,
    Guid? runId = null
)

// Check affordability
Task<JsonElement> CanAffordActionAsync(
    Guid combatId,
    string actionId,
    string actorId,
    Guid? runId = null
)

// Get cost options
Task<JsonElement> GetCostOptionsAsync(
    Guid combatId,
    string actionId
)
```

### Card Selection (Rewards)

```csharp
// Start card selection
Task<Guid> StartCardSelectionAsync(
    Guid runId,
    string selectionDefinitionId,
    string context = "combat_victory"
)

// Pick cards
Task<JsonElement> PickCardsAsync(
    Guid runId,
    Guid selectionInstanceId,
    string[] cardIds
)

// Reroll options
Task<JsonElement> RerollCardSelectionAsync(
    Guid runId,
    Guid selectionInstanceId
)

// Decompose card
Task<JsonElement> DecomposeCardAsync(
    Guid runId,
    Guid selectionInstanceId,
    string cardId
)
```

### Shop

```csharp
// Open shop
Task<Guid> OpenShopAsync(
    Guid runId,
    string shopDefinitionId
)

// Buy item
Task<JsonElement> BuyShopItemAsync(
    Guid runId,
    Guid shopInstanceId,
    string itemId
)

// Reroll shop
Task<JsonElement> RerollShopAsync(
    Guid runId,
    Guid shopInstanceId
)
```

### Preparation

```csharp
// Start preparation
Task<Guid> StartPreparationAsync(
    Guid runId,
    string preparationDefinitionId
)

// Apply preparation option
Task<JsonElement> ApplyPreparationAsync(
    Guid runId,
    Guid preparationInstanceId,
    string optionId
)
```

### Status Effects

```csharp
// Apply status effect
Task<JsonElement> ApplyStatusEffectAsync(
    Guid targetId,
    string statusEffectId,
    int stacks = 1,
    int duration = 3
)

// Get active status effects
Task<JsonElement> GetStatusEffectsAsync(Guid targetId)
```

### Events

```csharp
// Get events
Task<JsonElement> GetEventsAsync(
    string? category = null,
    string? severity = null,
    int limit = 100,
    int? afterSequence = null,
    Guid? combatId = null,
    Guid? runId = null
)
```

### Formula

```csharp
// Evaluate formula
Task<JsonElement> EvaluateFormulaAsync(
    string formulaName,
    Dictionary<string, object> context
)
```

### Entity

```csharp
// Create entity
Task<JsonElement> CreateEntityAsync(
    string definitionId,
    Dictionary<string, object>? customizations = null
)
```

---

## Common Test Patterns

### Pattern 1: Setup Helpers

Use built-in helper methods to reduce boilerplate:

```csharp
// Setup run only
var (runId, runState) = await SetupRunAsync();

// Setup combat only
var (combatId, combatState) = await SetupCombatAsync(
    heroId: "hero",
    enemies: new[] { "enemy_1" },
    initialEnergy: 3
);

// Setup both
var (runId, combatId, runState, combatState) = await SetupRunWithCombatAsync();
```

### Pattern 2: Assertion Helpers

Use built-in assertion methods:

```csharp
// Validate structure
AssertCombatStateValid(combatState);
AssertRunStateValid(runState);
AssertDeckStateValid(deckState);

// Check properties
AssertJsonPropertyExists(element, "propertyName");
AssertJsonPropertyEquals(element, "gold", 150);

// Check resources
AssertEntityHasResource(entity, "health");
```

### Pattern 3: Property Access Helpers

Extract JSON values safely:

```csharp
var gold = GetJsonInt(runState, "gold");
var heroName = GetJsonString(entity, "name");
var combatId = GetJsonGuid(combatState, "combatId");
var isActive = GetJsonBool(combatState, "isActive");
var damage = GetJsonFloat(result, "damage");
var enemyCount = GetArrayLength(combatState, "enemies");
```

### Pattern 4: Combat Loop

Standard pattern for combat tests:

```csharp
var combatId = await Client.StartCombatAsync("hero", new[] { "enemy_1" });
var state = await Client.GetCombatStateAsync(combatId);

while (GetJsonBool(state, "isActive"))
{
    // Player action
    await Client.ExecuteActionAsync(combatId, "hero", "basic_attack", "enemy_1");
    await Client.EndTurnAsync(combatId);
    
    // AI action
    await Client.ProcessAiTurnsAsync(combatId);
    
    // Update state
    state = await Client.GetCombatStateAsync(combatId);
}

// Verify outcome
Assert.Equal("Victory", GetJsonString(state, "status"));
```

### Pattern 5: Event Verification

Verify events were generated:

```csharp
var combatId = await Client.StartCombatAsync("hero", new[] { "enemy_1" });

// Execute actions...

// Get events
var events = await Client.GetEventsAsync(combatId: combatId);
var eventArray = events.GetProperty("events");

// Verify event types
Assert.Contains(eventArray.EnumerateArray(), 
    e => e.GetProperty("eventType").GetString() == "DamageDealt");
Assert.Contains(eventArray.EnumerateArray(), 
    e => e.GetProperty("eventType").GetString() == "ActionExecuted");
```

### Pattern 6: Auto-Play Tests

For auto-battler or long combats:

```csharp
var combatId = await Client.StartCombatAsync("hero", new[] { "enemy_1", "enemy_2" });

// Run entire combat automatically
var result = await Client.AutoPlayCombatAsync(combatId);

// Verify result
AssertJsonPropertyExists(result, "status");
AssertJsonPropertyExists(result, "totalTurns");
AssertJsonPropertyExists(result, "totalActions");

var status = GetJsonString(result, "status");
Assert.True(status == "Victory" || status == "Defeat");
```

---

## Troubleshooting

### Common Issues

#### 1. Timeout Errors

**Symptom:** Tests hang and timeout after 60+ seconds

**Possible Causes:**
- Missing configuration files (RunDefinitions, Entities, etc.)
- Async/sync deadlock in business logic
- Infinite loop in game logic

**Debugging Steps:**
```csharp
[Fact(Timeout = 30000)] // 30 second timeout
public async Task MyTest()
{
    // Add logging
    var runId = await Client.StartRunAsync();
    Console.WriteLine($"Run started: {runId}");
    
    // Test in isolation
}
```

**Fix:**
- Verify all required config files exist in `configs/test/`
- Check `docs/DIAGNOSTIC_REPORT.md` for known issues
- Add timeouts to individual operations

#### 2. 404 Not Found

**Symptom:** `HttpRequestException: Response status code does not indicate success: 404`

**Possible Causes:**
- Invalid ID (combat/run doesn't exist)
- Wrong definition ID in config
- Entity not created

**Fix:**
```csharp
// Verify IDs are valid
var runId = await Client.StartRunAsync();
Assert.NotEqual(Guid.Empty, runId); // ✓ Ensure ID is valid

// Check definition exists
try {
    var state = await Client.GetRunStateAsync(runId);
} catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
    Assert.Fail("Run was not created properly");
}
```

#### 3. 400 Bad Request

**Symptom:** `Bad Request` with error message like "Not enough energy"

**Possible Causes:**
- Invalid action parameters
- Insufficient resources
- Action not available in current state

**Fix:**
```csharp
// Check available actions first
var actions = await Client.GetAvailableActionsAsync(combatId, "hero");
var actionArray = actions.GetProperty("actions");
Assert.NotEmpty(actionArray.EnumerateArray());

// Check affordability
var canAfford = await Client.CanAffordActionAsync(combatId, "fireball", "hero", runId);
Assert.True(canAfford.GetProperty("canAfford").GetBoolean());

// Then execute
await Client.ExecuteActionAsync(combatId, "hero", "fireball", "enemy_1");
```

#### 4. JSON Property Not Found

**Symptom:** `KeyNotFoundException: The given key 'propertyName' was not present`

**Possible Causes:**
- API response structure changed
- Property name typo
- Conditional property (only present in certain states)

**Fix:**
```csharp
// Use TryGetProperty for optional properties
if (combatState.TryGetProperty("optionalField", out var field))
{
    var value = field.GetInt32();
}

// Or use assertion helper
AssertJsonPropertyExists(combatState, "requiredField");
```

#### 5. Test Isolation Issues

**Symptom:** Tests pass individually but fail when run together

**Possible Causes:**
- Shared state between tests
- Repository not resetting
- Static variables

**Fix:**
```csharp
// Each test should be completely independent
[Fact]
public async Task Test1()
{
    // Fresh setup
    var factory = new TestWebApplicationFactory();
    var client = factory.CreateClient();
    var simulator = new GameEngineClientSimulator(client);
    
    // Test logic
}

// Or use IClassFixture properly
public class MyTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly GameEngineClientSimulator _client;
    
    public MyTests(TestWebApplicationFactory factory)
    {
        _client = new GameEngineClientSimulator(factory.CreateClient());
    }
}
```

### Debugging Tips

**1. Enable Verbose Logging:**
```json
// appsettings.Test.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Information"
    }
  }
}
```

**2. Use Test Output Helper:**
```csharp
public class MyTests : GameEngineIntegrationTestBase
{
    private readonly ITestOutputHelper _output;
    
    public MyTests(TestWebApplicationFactory factory, ITestOutputHelper output) 
        : base(factory)
    {
        _output = output;
    }
    
    [Fact]
    public async Task MyTest()
    {
        _output.WriteLine("Starting test...");
        var result = await Client.SomeActionAsync();
        _output.WriteLine($"Result: {result}");
    }
}
```

**3. Inspect Raw HTTP Responses:**
```csharp
var response = await RawClient.PostAsync("/api/run/start", content);
var responseBody = await response.Content.ReadAsStringAsync();
_output.WriteLine($"Response: {responseBody}");
```

**4. Isolate Failures:**
```csharp
// Run single test
dotnet test --filter "FullyQualifiedName~MyTests.MyTest"

// Run specific category
dotnet test --filter "Category=Integration"
```

---

## CI/CD Integration

### Running Tests in CI

**GitHub Actions Example:**
```yaml
name: Integration Tests

on: [push, pull_request]

jobs:
  integration-tests:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '10.0.x'
    
    - name: Restore dependencies
      run: dotnet restore
    
    - name: Build
      run: dotnet build --no-restore
    
    - name: Run Integration Tests
      run: dotnet test --no-build --filter "Category=Integration" --logger "trx;LogFileName=test-results.trx"
      timeout-minutes: 10
    
    - name: Upload Test Results
      if: always()
      uses: actions/upload-artifact@v3
      with:
        name: test-results
        path: '**/test-results.trx'
```

### Test Configuration for CI

**appsettings.CI.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  },
  "GameConfiguration": {
    "ConfigName": "test",
    "EnableDiagnostics": false
  }
}
```

### Performance Considerations

**Parallel Execution:**
```bash
# Run tests in parallel (xUnit default)
dotnet test --no-build

# Disable parallel execution if tests conflict
dotnet test --no-build -- xUnit.ParallelizeAssembly=false
```

**Selective Test Execution:**
```bash
# Fast smoke tests
dotnet test --filter "Priority=High"

# Full suite
dotnet test --filter "Category=Integration"
```

---

## Best Practices

### 1. Test Independence

Each test should be completely independent:

```csharp
// ✗ BAD - Shared state
private Guid _sharedRunId;

[Fact]
public async Task Test1() 
{
    _sharedRunId = await Client.StartRunAsync();
}

[Fact]
public async Task Test2() 
{
    // Depends on Test1 running first
    var state = await Client.GetRunStateAsync(_sharedRunId);
}

// ✓ GOOD - Independent
[Fact]
public async Task Test1() 
{
    var runId = await Client.StartRunAsync();
    // Complete test
}

[Fact]
public async Task Test2() 
{
    var runId = await Client.StartRunAsync();
    // Complete test
}
```

### 2. Descriptive Test Names

Use clear, descriptive names:

```csharp
// ✗ BAD
[Fact]
public async Task Test1() { }

// ✓ GOOD
[Fact]
public async Task StartRun_WithValidParameters_ReturnsValidRunId() { }

[Fact]
public async Task ExecuteAction_WithInsufficientEnergy_ReturnsBadRequest() { }
```

### 3. Arrange-Act-Assert

Follow AAA pattern:

```csharp
[Fact]
public async Task BuyShopItem_WithSufficientGold_DeductsGoldAndAddsItem()
{
    // Arrange
    var (runId, _) = await SetupRunAsync();
    var shopId = await Client.OpenShopAsync(runId, "test_shop");
    var initialGold = 150;
    
    // Act
    var result = await Client.BuyShopItemAsync(runId, shopId, "health_potion");
    
    // Assert
    var goldSpent = GetJsonInt(result, "goldSpent");
    var remainingGold = GetJsonInt(result, "remainingGold");
    Assert.Equal(initialGold - goldSpent, remainingGold);
    Assert.True(GetJsonBool(result, "addedToInventory"));
}
```

### 4. Test One Thing

Each test should verify one behavior:

```csharp
// ✗ BAD - Tests multiple behaviors
[Fact]
public async Task CompleteGameplay() 
{
    // Tests run start, combat, rewards, shop, AND progression
    // Too much in one test
}

// ✓ GOOD - Focused tests
[Fact]
public async Task StartRun_ReturnsValidRunState() { }

[Fact]
public async Task Combat_HeroDiesWithZeroHealth_ResultsInDefeat() { }

[Fact]
public async Task CardSelection_PickCard_AddsCardToDeck() { }
```

### 5. Use Test Categories

Organize tests with traits:

```csharp
[Trait("Category", "Integration")]
[Trait("Genre", "Roguelike")]
[Trait("Priority", "High")]
public class RoguelikeGameFlowTests { }
```

Run specific categories:
```bash
dotnet test --filter "Genre=Roguelike"
dotnet test --filter "Priority=High"
```

---

## Summary

**Key Takeaways:**

1. **Infrastructure:** Use `GameEngineIntegrationTestBase` and `GameEngineClientSimulator`
2. **Prerequisites:** Ensure test configuration files exist
3. **Patterns:** Use helper methods, assertion helpers, and standard patterns
4. **Troubleshooting:** Check timeouts, 404s, and test isolation
5. **Best Practices:** Independent tests, descriptive names, AAA pattern

**Common Workflows:**

- **New Feature:** Write integration test first, implement feature, verify test passes
- **Bug Fix:** Write failing test that reproduces bug, fix bug, verify test passes
- **Refactoring:** Run integration tests before and after to ensure behavior unchanged

**Resources:**

- API Reference: `docs/API_ENDPOINTS.md`
- Game Flows: `docs/GAME_FLOW.md`
- Configuration: `docs/CONFIGURATION_GUIDE.md`
- Known Issues: `docs/DIAGNOSTIC_REPORT.md`
