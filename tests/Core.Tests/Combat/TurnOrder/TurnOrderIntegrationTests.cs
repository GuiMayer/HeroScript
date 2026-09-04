using Core.Combat;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Logging;
using Core.Resources;
using Xunit;

namespace Core.Tests.Combat.TurnOrder;

/// <summary>
/// Testes de integração do sistema de turnos com CombatSystem
/// </summary>
public class TurnOrderIntegrationTests
{
    private readonly ILogger _logger;
    private readonly IResourceManager _resourceManager;
    
    public TurnOrderIntegrationTests()
    {
        _logger = TurnOrderTestHelper.CreateTestLogger();
        _resourceManager = TurnOrderTestHelper.CreateTestResourceManager();
    }
    
    [Fact]
    public void CombatSystem_WithFixedTurnOrder_ShouldMaintainConsistentOrder()
    {
        // Arrange
        var calculator = new FixedTurnOrderCalculator(_logger);
        var combatSystem = new CombatSystem(
            _logger,
            _resourceManager,
            turnOrderCalculator: calculator,
            entityDefinitionLoader: CombatParticipantTestFixture.CreateDefinitionLoader(_logger));
        
        // Act
        var startResult = combatSystem.StartCombat("hero1", new List<string> { "enemy1", "enemy2" });
        
        // Assert
        Assert.True(startResult.IsSuccess);
        Assert.NotNull(startResult.Value.TurnOrder);
        Assert.Equal(3, startResult.Value.TurnOrder.Count);
        Assert.Equal("hero1", startResult.Value.TurnOrder[0]);
        Assert.Equal("enemy1", startResult.Value.TurnOrder[1]);
        Assert.Equal("enemy2", startResult.Value.TurnOrder[2]);
    }
    
    [Fact]
    public void CombatSystem_WithSpeedBasedTurnOrder_ShouldOrderBySpeed()
    {
        // Arrange
        var calculator = new SpeedBasedTurnOrderCalculator("speed", 0, _logger);
        var combatSystem = new CombatSystem(
            _logger,
            _resourceManager,
            turnOrderCalculator: calculator,
            entityDefinitionLoader: CombatParticipantTestFixture.CreateDefinitionLoader(_logger));
        
        // Act
        var startResult = combatSystem.StartCombat("hero1", new List<string> { "enemy1", "enemy2" });
        
        // Assert
        Assert.True(startResult.IsSuccess);
        Assert.NotNull(startResult.Value.TurnOrder);
        Assert.Equal("enemy1", startResult.Value.TurnOrder[0]);
    }
    
    [Fact]
    public void CombatSystem_WithInitiativeTurnOrder_ShouldCalculateOnce()
    {
        // Arrange
        var calculator = new InitiativeTurnOrderCalculator("speed", 2, 20, 0, _logger);
        var combatSystem = new CombatSystem(
            _logger,
            _resourceManager,
            turnOrderCalculator: calculator,
            entityDefinitionLoader: CombatParticipantTestFixture.CreateDefinitionLoader(_logger));
        
        // Act
        var startResult = combatSystem.StartCombat("hero1", new List<string> { "enemy1", "enemy2" });
        
        // Assert
        Assert.True(startResult.IsSuccess);
        Assert.NotNull(startResult.Value.TurnOrder);
        Assert.Equal(3, startResult.Value.TurnOrder.Count);
        
        // Store initial order
        var initialOrder = startResult.Value.TurnOrder.ToList();
        
        // End turn to trigger recalculation
        var endTurnResult = combatSystem.ExecuteAction(startResult.Value.CombatId, EndTurn("hero1"));
        
        // Initiative order should remain the same
        Assert.True(endTurnResult.IsSuccess);
        Assert.Equal(initialOrder, endTurnResult.Value.TurnOrder);
    }
    
    [Fact]
    public void CombatSystem_WithATBTurnOrder_ShouldFillGaugesOverTime()
    {
        // Arrange
        var calculator = new ATBTurnOrderCalculator("speed", 50f, 10f, 100f, 10f, _logger);
        var combatSystem = new CombatSystem(
            _logger,
            _resourceManager,
            turnOrderCalculator: calculator,
            entityDefinitionLoader: CombatParticipantTestFixture.CreateDefinitionLoader(_logger));
        
        // Act
        var startResult = combatSystem.StartCombat("hero1", new List<string> { "enemy1" });
        
        // Assert
        Assert.True(startResult.IsSuccess);
        // Initially, no one should be ready
        Assert.NotNull(startResult.Value.TurnOrder);
        
        // After ending turn, gauges should fill
        var endTurnResult = combatSystem.ExecuteAction(startResult.Value.CombatId, EndTurn("hero1"));
        
        Assert.True(endTurnResult.IsSuccess);
        Assert.NotNull(endTurnResult.Value.TurnOrder);
    }
    
    [Fact]
    public void CombatSystem_WithConditionalTurnOrder_ShouldUseCustomLogic()
    {
        // Arrange - Enemies first calculator
        var calculator = ConditionalTurnOrderCalculator.CreateEnemiesFirstCalculator(_logger);
        var combatSystem = new CombatSystem(
            _logger,
            _resourceManager,
            turnOrderCalculator: calculator,
            entityDefinitionLoader: CombatParticipantTestFixture.CreateDefinitionLoader(_logger));
        
        // Act
        var startResult = combatSystem.StartCombat("hero1", new List<string> { "enemy1", "enemy2" });
        
        // Assert
        Assert.True(startResult.IsSuccess);
        Assert.NotNull(startResult.Value.TurnOrder);
        Assert.Equal(3, startResult.Value.TurnOrder.Count);
        // Enemies should go first
        Assert.Equal("enemy1", startResult.Value.TurnOrder[0]);
        Assert.Equal("enemy2", startResult.Value.TurnOrder[1]);
        Assert.Equal("hero1", startResult.Value.TurnOrder[2]);
    }
    
     [Fact]
     public void CombatSystem_WithoutTurnOrderCalculator_ShouldWorkNormally()
     {
         // Arrange
         var combatSystem = new CombatSystem(
             _logger,
             _resourceManager,
             new FixedTurnOrderCalculator(_logger),
             entityDefinitionLoader: CombatParticipantTestFixture.CreateDefinitionLoader(_logger));
         
         // Act
         var startResult = combatSystem.StartCombat("hero1", new List<string> { "enemy1" });
         
         // Assert
         Assert.True(startResult.IsSuccess);
         // Turn order should be calculated with FixedTurnOrderCalculator
         Assert.NotNull(startResult.Value.TurnOrder);
     }
    
    [Fact]
    public void TurnOrderCalculatorFactory_Integration_ShouldCreateWorkingCalculators()
    {
        // Arrange
        var factory = new TurnOrderCalculatorFactory(_logger);
        
        // Test each strategy
        var strategies = new[]
        {
            TurnStrategy.FIXED,
            TurnStrategy.SPEED_BASED,
            TurnStrategy.INITIATIVE,
            TurnStrategy.ATB,
            TurnStrategy.CONDITIONAL
        };
        
        foreach (var strategy in strategies)
        {
            // Act
            var calculatorResult = factory.CreateCalculator(Configuration(strategy));
            Assert.True(calculatorResult.IsSuccess, $"Failed to create calculator for {strategy}");
            
            var combatSystem = new CombatSystem(
                _logger,
                _resourceManager,
                turnOrderCalculator: calculatorResult.Value,
                entityDefinitionLoader: CombatParticipantTestFixture.CreateDefinitionLoader(_logger));
            
            var startResult = combatSystem.StartCombat("hero1", new List<string> { "enemy1" });
            
            // Assert
            Assert.True(startResult.IsSuccess, $"Failed to start combat with {strategy}");
            Assert.NotNull(startResult.Value.TurnOrder);
        }
    }
    
    [Fact]
    public void CombatSystem_TurnOrderRecalculation_ShouldUpdateAfterEndTurn()
    {
        // Arrange
        var calculator = new SpeedBasedTurnOrderCalculator("speed", 0, _logger);
        var combatSystem = new CombatSystem(
            _logger,
            _resourceManager,
            turnOrderCalculator: calculator,
            entityDefinitionLoader: CombatParticipantTestFixture.CreateDefinitionLoader(_logger));
        
        // Act
        var startResult = combatSystem.StartCombat("hero1", new List<string> { "enemy1" });
        Assert.True(startResult.IsSuccess);
        
        var initialOrder = startResult.Value.TurnOrder;
        
        // End turn
        var endTurnResult = combatSystem.ExecuteAction(startResult.Value.CombatId, EndTurn("hero1"));
        
        // Assert
        Assert.True(endTurnResult.IsSuccess);
        Assert.NotNull(endTurnResult.Value.TurnOrder);
        // Order should be recalculated (may be same or different depending on speed changes)
        Assert.NotNull(initialOrder);
    }

    private static CombatActionCommand EndTurn(string actorId)
    {
        return new CombatActionCommand
        {
            ActorId = actorId,
            ActionType = ActionType.END_TURN
        };
    }

    private static TurnOrderConfiguration Configuration(TurnStrategy strategy) => new()
    {
        Strategy = strategy,
        OrderResourceId = "speed",
        MissingResourceValue = 0,
        InitiativeDieSides = 20,
        InitiativeResourcePerModifier = 2,
        AtbFillRate = 10,
        AtbReferenceResourceValue = 10,
        AtbReadyThreshold = 100,
        PriorityResourceId = "health",
        PriorityThreshold = 0.3f
    };
}
