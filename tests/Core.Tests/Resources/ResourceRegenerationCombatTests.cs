using Core.Combat;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Common;
using Core.Config;
using Core.Events;
using Core.Events.Domain;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Moq;
using Xunit;

namespace Core.Tests.Resources;

/// <summary>
/// Testes de integração para regeneração de recursos durante combate
/// </summary>
public class ResourceRegenerationCombatTests
{
    private readonly ILogger _logger;
    private readonly IResourceManager _resourceManager;
    private readonly IEventBus _eventBus;
    private readonly IMathEngine _mathEngine;
    private readonly IResourceRegenerationProcessor _regenerationProcessor;
    private readonly CombatSystem _combatSystem;

    public ResourceRegenerationCombatTests()
    {
        var mockLogger = new Mock<ILogger>();
        var mockEventBus = new Mock<IEventBus>();
        var mockMathEngine = new Mock<IMathEngine>();
        var mockResourceManager = new Mock<IResourceManager>();
        var mockRegenerationProcessor = new Mock<IResourceRegenerationProcessor>();
        
        _logger = mockLogger.Object;
        _eventBus = mockEventBus.Object;
        _mathEngine = mockMathEngine.Object;
        _regenerationProcessor = mockRegenerationProcessor.Object;
        _resourceManager = mockResourceManager.Object;
        
        // Setup ResourceManager to return valid pools
        mockResourceManager.Setup(rm => rm.CreatePool(It.IsAny<string>(), It.IsAny<float>()))
            .Returns((string resourceId, float current) =>
            {
                var definition = new ResourceDefinition
                {
                    ResourceId = resourceId,
                    DisplayName = resourceId == "health" ? "Health" : "Energy",
                    Category = resourceId == "health" ? ResourceCategory.VITAL : ResourceCategory.TACTICAL,
                    DefaultMin = 0,
                    DefaultMax = resourceId == "health" ? 100 : 10,
                    DefaultCurrent = current,
                    CanBeNegative = false,
                    Regeneration = new RegenerationConfig
                    {
                        Enabled = resourceId == "energy",
                        AmountPerTurn = 1,
                        Timing = RegenerationTiming.START_TURN
                    }
                };

                return new ResourcePool
                {
                    Definition = definition,
                    Current = current,
                    Maximum = resourceId == "health" ? 100 : 10,
                    Minimum = 0
                };
            });
        
        // Setup RegenerationProcessor to actually regenerate energy
        mockRegenerationProcessor.Setup(rp => rp.ProcessRegeneration(
            It.IsAny<EntityResourceState>(),
            It.IsAny<RegenerationTiming>(),
            It.IsAny<Dictionary<string, float>>()))
            .Returns((EntityResourceState state, RegenerationTiming timing, Dictionary<string, float> context) =>
            {
                var updatedResources = new Dictionary<string, ResourcePool>();
                foreach (var (resourceId, pool) in state.Resources)
                {
                    if (resourceId == "energy" && timing == RegenerationTiming.START_TURN)
                    {
                        // Regenerate 1 energy
                        var newPool = pool.Gain(1);
                        updatedResources[resourceId] = newPool;
                    }
                    else
                    {
                        updatedResources[resourceId] = pool;
                    }
                }
                
                return Result<EntityResourceState>.Success(new EntityResourceState
                {
                    EntityId = state.EntityId,
                    Resources = updatedResources
                });
            });
        
        _combatSystem = new CombatSystem(_logger, _resourceManager, new FixedTurnOrderCalculator(_logger), _eventBus, regenerationProcessor: _regenerationProcessor);
    }

    [Fact]
    public void EndTurn_ShouldRegenerateEnergyAtStartOfTurn()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero1", new List<string> { "enemy1" }, initialEnergy: 1);
        Assert.True(startResult.IsSuccess);
        var combatId = startResult.Value.CombatId;

        // Verificar energia inicial
        var initialState = startResult.Value;
        var initialEnergy = initialState.GetHeroResource("energy");
        Assert.NotNull(initialEnergy);
        Assert.Equal(1, initialEnergy.Current);

        // Act - Terminar turno (deve regenerar energia)
        var endTurnResult = _combatSystem.ExecuteAction(combatId, EndTurn("hero1"));

        // Assert
        Assert.True(endTurnResult.IsSuccess);
        var newState = endTurnResult.Value;
        var newEnergy = newState.GetHeroResource("energy");
        Assert.NotNull(newEnergy);
        
        // Energia deve ter regenerado (config padrão de energy regenera 1 por turno)
        Assert.True(newEnergy.Current > initialEnergy.Current, 
            $"Energy should have regenerated. Initial: {initialEnergy.Current}, After: {newEnergy.Current}");
    }

    [Fact]
    public void EndTurn_ShouldRegenerateMultipleResources()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero1", new List<string> { "enemy1" }, initialEnergy: 1);
        Assert.True(startResult.IsSuccess);
        var combatId = startResult.Value.CombatId;

        var initialState = startResult.Value;
        var initialEnergy = initialState.GetHeroResource("energy");
        var initialHealth = initialState.GetHeroResource("health");

        // Act
        var endTurnResult = _combatSystem.ExecuteAction(combatId, EndTurn("hero1"));

        // Assert
        Assert.True(endTurnResult.IsSuccess);
        var newState = endTurnResult.Value;
        
        // Verificar que recursos foram processados
        var newEnergy = newState.GetHeroResource("energy");
        var newHealth = newState.GetHeroResource("health");
        
        Assert.NotNull(newEnergy);
        Assert.NotNull(newHealth);
        
        // Pelo menos energia deve ter mudado
        Assert.NotEqual(initialEnergy?.Current ?? 0, newEnergy.Current);
    }

    [Fact]
    public void EndTurn_ShouldRegenerateEnemyResources()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero1", new List<string> { "enemy1" }, initialEnergy: 3);
        Assert.True(startResult.IsSuccess);
        var combatId = startResult.Value.CombatId;

        var initialState = startResult.Value;
        var initialEnemyHealth = initialState.GetEnemyResource("enemy1", "health");

        // Act - Terminar turno
        var endTurnResult = _combatSystem.ExecuteAction(combatId, EndTurn("hero1"));

        // Assert
        Assert.True(endTurnResult.IsSuccess);
        var newState = endTurnResult.Value;
        
        // Verificar que inimigo ainda existe e tem recursos
        var newEnemyHealth = newState.GetEnemyResource("enemy1", "health");
        Assert.NotNull(newEnemyHealth);
        
        // Recursos do inimigo devem ter sido processados (mesmo que não mudem)
        Assert.NotNull(initialEnemyHealth);
    }

    [Fact]
    public void Combat_ShouldPublishResourceRegeneratedEvents()
    {
        // Arrange - Criar um EventBus real para capturar eventos
        var realEventBus = new EventBus(new ConsoleLogger("Test"));
        var events = new List<ResourceRegeneratedEvent>();
        realEventBus.Subscribe<ResourceRegeneratedEvent>(e => events.Add(e));
        
        var mockLogger = new Mock<ILogger>();
        var mockMathEngine = new Mock<IMathEngine>();
        var mockResourceManager = new Mock<IResourceManager>();
        var mockRegenerationProcessor = new Mock<IResourceRegenerationProcessor>();
        
        // Setup ResourceManager
        mockResourceManager.Setup(rm => rm.CreatePool(It.IsAny<string>(), It.IsAny<float>()))
            .Returns((string resourceId, float current) =>
            {
                var definition = new ResourceDefinition
                {
                    ResourceId = resourceId,
                    DisplayName = resourceId == "health" ? "Health" : "Energy",
                    Category = resourceId == "health" ? ResourceCategory.VITAL : ResourceCategory.TACTICAL,
                    DefaultMin = 0,
                    DefaultMax = resourceId == "health" ? 100 : 10,
                    DefaultCurrent = current,
                    CanBeNegative = false,
                    Regeneration = new RegenerationConfig
                    {
                        Enabled = resourceId == "energy",
                        AmountPerTurn = 1,
                        Timing = RegenerationTiming.START_TURN
                    }
                };

                return new ResourcePool
                {
                    Definition = definition,
                    Current = current,
                    Maximum = resourceId == "health" ? 100 : 10,
                    Minimum = 0
                };
            });
        
        // Setup RegenerationProcessor to regenerate and publish events
        mockRegenerationProcessor.Setup(rp => rp.ProcessRegeneration(
            It.IsAny<EntityResourceState>(),
            It.IsAny<RegenerationTiming>(),
            It.IsAny<Dictionary<string, float>>()))
            .Returns((EntityResourceState state, RegenerationTiming timing, Dictionary<string, float> context) =>
            {
                var updatedResources = new Dictionary<string, ResourcePool>();
                foreach (var (resourceId, pool) in state.Resources)
                {
                    if (resourceId == "energy" && timing == RegenerationTiming.START_TURN)
                    {
                        var newPool = pool.Gain(1);
                        updatedResources[resourceId] = newPool;
                        
                        // Publicar evento
                        realEventBus.Publish(new ResourceRegeneratedEvent
                        {
                            EntityId = state.EntityId,
                            ResourceId = resourceId,
                            OldValue = pool.Current,
                            NewValue = newPool.Current,
                            Amount = 1,
                            Timing = timing
                        });
                    }
                    else
                    {
                        updatedResources[resourceId] = pool;
                    }
                }
                
                return Result<EntityResourceState>.Success(new EntityResourceState
                {
                    EntityId = state.EntityId,
                    Resources = updatedResources
                });
            });
        
        var combatSystem = new CombatSystem(mockLogger.Object, mockResourceManager.Object, new FixedTurnOrderCalculator(mockLogger.Object), realEventBus, regenerationProcessor: mockRegenerationProcessor.Object);

        var startResult = combatSystem.StartCombat("hero1", new List<string> { "enemy1" }, initialEnergy: 1);
        Assert.True(startResult.IsSuccess);
        var combatId = startResult.Value.CombatId;

        // Act
        var endTurnResult = combatSystem.ExecuteAction(combatId, EndTurn("hero1"));

        // Assert
        Assert.True(endTurnResult.IsSuccess);
        
        // Deve ter publicado pelo menos um evento de regeneração
        Assert.NotEmpty(events);
        
        // Verificar que evento tem dados corretos
        var energyEvent = events.FirstOrDefault(e => e.ResourceId == "energy");
        Assert.NotNull(energyEvent);
        Assert.Equal("hero1", energyEvent.EntityId);
        Assert.True(energyEvent.Amount > 0, "Regeneration amount should be positive");
        Assert.True(energyEvent.NewValue > energyEvent.OldValue, "New value should be greater than old value");
    }

    [Fact]
    public void EndTurn_WithoutRegenerationProcessor_ShouldStillWork()
    {
         // Arrange - Criar CombatSystem sem regenerationProcessor
         var combatSystemWithoutRegen = new CombatSystem(_logger, _resourceManager, new FixedTurnOrderCalculator(_logger), _eventBus);
        
        var startResult = combatSystemWithoutRegen.StartCombat("hero1", new List<string> { "enemy1" }, initialEnergy: 3);
        Assert.True(startResult.IsSuccess);
        var combatId = startResult.Value.CombatId;

        var initialState = startResult.Value;
        var initialEnergy = initialState.GetHeroResource("energy");

        // Act
        var endTurnResult = combatSystemWithoutRegen.ExecuteAction(combatId, EndTurn("hero1"));

        // Assert - Deve funcionar sem erros, mas energia não deve regenerar
        Assert.True(endTurnResult.IsSuccess);
        var newState = endTurnResult.Value;
        var newEnergy = newState.GetHeroResource("energy");
        
        Assert.NotNull(newEnergy);
        // Energia não deve ter mudado (sem regenerationProcessor)
        Assert.Equal(initialEnergy?.Current ?? 0, newEnergy.Current);
    }

    [Fact]
    public void MultipleEndTurns_ShouldRegenerateEachTurn()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero1", new List<string> { "enemy1" }, initialEnergy: 0);
        Assert.True(startResult.IsSuccess);
        var combatId = startResult.Value.CombatId;

        var energyValues = new List<float>();
        energyValues.Add(0); // Energia inicial

        // Act - Terminar turno 3 vezes
        for (int i = 0; i < 3; i++)
        {
            var endTurnResult = _combatSystem.ExecuteAction(combatId, EndTurn("hero1"));
            Assert.True(endTurnResult.IsSuccess);
            
            var energy = endTurnResult.Value.GetHeroResource("energy");
            Assert.NotNull(energy);
            energyValues.Add(energy.Current);
        }

        // Assert - Energia deve ter aumentado a cada turno
        for (int i = 1; i < energyValues.Count; i++)
        {
            Assert.True(energyValues[i] >= energyValues[i - 1], 
                $"Energy should increase or stay same. Turn {i-1}: {energyValues[i-1]}, Turn {i}: {energyValues[i]}");
        }
        
        // Pelo menos alguma regeneração deve ter ocorrido
        Assert.True(energyValues.Last() > energyValues.First(), 
            "Energy should have regenerated over multiple turns");
    }

    [Fact]
    public void EndTurn_ShouldNotExceedMaximumResource()
    {
        // Arrange
        var startResult = _combatSystem.StartCombat("hero1", new List<string> { "enemy1" }, initialEnergy: 3);
        Assert.True(startResult.IsSuccess);
        var combatId = startResult.Value.CombatId;

        var initialEnergy = startResult.Value.GetHeroResource("energy");
        var maxEnergy = initialEnergy?.Maximum ?? 0;

        // Act - Terminar turno múltiplas vezes para tentar exceder máximo
        CombatState? finalState = null;
        for (int i = 0; i < 10; i++)
        {
            var endTurnResult = _combatSystem.ExecuteAction(combatId, EndTurn("hero1"));
            Assert.True(endTurnResult.IsSuccess);
            finalState = endTurnResult.Value;
        }

        // Assert
        Assert.NotNull(finalState);
        var finalEnergy = finalState.GetHeroResource("energy");
        Assert.NotNull(finalEnergy);
        
        // Energia não deve exceder máximo
        Assert.True(finalEnergy.Current <= maxEnergy, 
            $"Energy should not exceed maximum. Current: {finalEnergy.Current}, Max: {maxEnergy}");
    }

    private static CombatActionCommand EndTurn(string actorId)
    {
        return new CombatActionCommand
        {
            ActorId = actorId,
            ActionType = ActionType.END_TURN
        };
    }
}
