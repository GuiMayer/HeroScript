using System;
using System.Collections.Generic;
using Core.Combat.Models;
using Core.Common;
using Core.Config;
using Core.Effects;
using Core.Entity;
using Core.Entity.Components;
using Core.Events;
using Core.Logging;
using Core.Resources;
using Core.Run;
using Core.StatusEffects;
using Core.Validation;
using Moq;

namespace Core.Tests;

/// <summary>
/// Test Data Builder Pattern - Enterprise-grade test object factories
/// Provides fluent, immutable builders for all core domain objects
/// </summary>
public static class TestDataBuilders
{
    // ==================== MOCK FACTORIES ====================
    
    public static Mock<ILogger> MockLogger()
    {
        var mock = new Mock<ILogger>();
        mock.Setup(l => l.LogDebug(It.IsAny<string>()));
        mock.Setup(l => l.LogInformation(It.IsAny<string>()));
        mock.Setup(l => l.LogWarning(It.IsAny<string>()));
        mock.Setup(l => l.LogError(It.IsAny<string>()));
        mock.Setup(l => l.LogError(It.IsAny<string>(), It.IsAny<Exception>()));
        return mock;
    }
    
    public static Mock<IEventBus> MockEventBus()
    {
        var mock = new Mock<IEventBus>();
        mock.Setup(e => e.Publish(It.IsAny<IEvent>()));
        mock.Setup(e => e.Subscribe<IEvent>(It.IsAny<Action<IEvent>>()))
            .Returns(new Mock<IDisposable>().Object);
        return mock;
    }
    
    public static Mock<IResourceManager> MockResourceManager()
    {
        var mock = new Mock<IResourceManager>();
        
        mock.Setup(rm => rm.CreatePool(It.IsAny<string>(), It.IsAny<float?>()))
            .Returns((string id, float? init) => new ResourcePool
            {
                ResourceId = id,
                Current = init ?? 100f,
                Maximum = init ?? 100f,
                Minimum = 0f,
                Definition = new ResourceDefinition
                {
                    ResourceId = id,
                    DisplayName = id,
                    Category = ResourceCategory.VITAL,
                    DefaultCurrent = init ?? 100f,
                    DefaultMax = init ?? 100f,
                    DefaultMin = 0f
                }
            });

        mock.As<IRevisionedResourceManager>()
            .Setup(rm => rm.CreatePool(
                It.IsAny<string>(),
                It.IsAny<float?>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .Returns((string id, float? initial, string _, string? _) =>
                Result<ResourcePool>.Success(new ResourcePool
                {
                    ResourceId = id,
                    Current = initial ?? 0f,
                    Maximum = 1_000_000f,
                    Minimum = 0f,
                    Definition = new ResourceDefinition
                    {
                        ResourceId = id,
                        DisplayName = id,
                        Category = ResourceCategory.SPECIAL,
                        DefaultCurrent = initial ?? 0f,
                        DefaultMax = 1_000_000f,
                        DefaultMin = 0f,
                        CanExceedMax = true
                    }
                }));
        
        mock.Setup(rm => rm.GetDefinition(It.IsAny<string>()))
            .Returns((string id) => Result<ResourceDefinition>.Success(new ResourceDefinition
            {
                ResourceId = id,
                DisplayName = id,
                Category = ResourceCategory.VITAL
            }));
        
        mock.Setup(rm => rm.ValidateCost(It.IsAny<ResourcePool>(), It.IsAny<float>()))
            .Returns(Result.Success());
        
        return mock;
    }

    public static ResourceSet RunResources(params ResourceAmount[] amounts)
    {
        var pools = amounts.ToDictionary(
            amount => amount.ResourceId,
            amount => new ResourcePool
            {
                ResourceId = amount.ResourceId,
                Current = amount.Amount,
                Minimum = 0f,
                Maximum = 1_000_000f,
                Definition = new ResourceDefinition
                {
                    ResourceId = amount.ResourceId,
                    DisplayName = amount.ResourceId,
                    DefaultMax = 1_000_000f,
                    CanExceedMax = true
                }
            },
            StringComparer.OrdinalIgnoreCase);
        return new ResourceSet { OwnerId = "test-owner", Resources = pools };
    }
    
    // ==================== ENTITY BUILDERS ====================
    
    public static CombatEntityBuilder CombatEntity() => new();
    
    public class CombatEntityBuilder
    {
        private string _id = "test_entity";
        private string _name = "Test Entity";
        private bool _isHero = true;
        private Dictionary<string, ResourcePool> _resources = new();
        
        public CombatEntityBuilder WithId(string id)
        {
            _id = id;
            return this;
        }
        
        public CombatEntityBuilder WithName(string name)
        {
            _name = name;
            return this;
        }
        
        public CombatEntityBuilder AsEnemy()
        {
            _isHero = false;
            return this;
        }
        
        public CombatEntityBuilder WithResource(string resourceId, float current, float max = 100f)
        {
            _resources[resourceId] = new ResourcePool
            {
                ResourceId = resourceId,
                Current = current,
                Maximum = max,
                Minimum = 0f,
                Definition = new ResourceDefinition
                {
                    ResourceId = resourceId,
                    DisplayName = resourceId,
                    Category = ResourceCategory.VITAL
                }
            };
            return this;
        }
        
        public CombatEntityBuilder WithHealth(float current = 100f, float max = 100f)
        {
            return WithResource("health", current, max);
        }
        
        public CombatEntity Build()
        {
            if (!_resources.ContainsKey("health"))
            {
                WithHealth();
            }
            
            return new CombatEntity
            {
                EntityId = _id,
                Name = _name,
                IsHero = _isHero,
                ResourceState = new ResourceSet
                {
                    OwnerId = _id,
                    Resources = _resources
                }
            };
        }
    }
    
    // ==================== ACTION BUILDERS ====================
    
    public static ActionDefinitionBuilder Action() => new();
    
    public class ActionDefinitionBuilder
    {
        private string _actionId = "test_action";
        private string _displayName = "Test Action";
        private ActionType _actionType = ActionType.BASIC_ATTACK;
        private List<EffectDefinition> _effects = new();
        private ActionCosts _costs = new();
        private List<string> _tags = new();
        
        public ActionDefinitionBuilder WithId(string id)
        {
            _actionId = id;
            return this;
        }
        
        public ActionDefinitionBuilder WithName(string name)
        {
            _displayName = name;
            return this;
        }
        
        public ActionDefinitionBuilder WithType(ActionType type)
        {
            _actionType = type;
            return this;
        }
        
        public ActionDefinitionBuilder WithEffect(EffectDefinition effect)
        {
            _effects.Add(effect);
            return this;
        }
        
        public ActionDefinitionBuilder WithDamageEffect(float damage, List<string>? tags = null)
        {
            _effects.Add(new EffectDefinition
            {
                Type = EffectType.DAMAGE,
                FlatValue = damage,
                Target = EffectTarget.TARGET,
                Tags = tags ?? new List<string>()
            });
            return this;
        }
        
        public ActionDefinitionBuilder WithCost(string resourceId, float amount)
        {
            var costsList = new List<ResourceCost>(_costs.Costs)
            {
                new ResourceCost { ResourceId = resourceId, Amount = amount }
            };
            _costs = new ActionCosts { Costs = costsList };
            return this;
        }
        
        public ActionDefinitionBuilder WithTag(string tag)
        {
            _tags.Add(tag);
            return this;
        }
        
        public ActionDefinition Build()
        {
            return new ActionDefinition
            {
                ActionId = _actionId,
                DisplayName = _displayName,
                ActionType = _actionType,
                Effects = _effects,
                Costs = _costs,
                Tags = _tags
            };
        }
    }
    
    // ==================== RESOURCE BUILDERS ====================
    
    public static ResourceDefinitionBuilder Resource() => new();
    
    public class ResourceDefinitionBuilder
    {
        private string _id = "test_resource";
        private string _name = "Test Resource";
        private ResourceCategory _category = ResourceCategory.VITAL;
        private float _min = 0f;
        private float _max = 100f;
        private float _current = 100f;
        
        public ResourceDefinitionBuilder WithId(string id)
        {
            _id = id;
            return this;
        }
        
        public ResourceDefinitionBuilder WithName(string name)
        {
            _name = name;
            return this;
        }
        
        public ResourceDefinitionBuilder WithCategory(ResourceCategory category)
        {
            _category = category;
            return this;
        }
        
        public ResourceDefinitionBuilder WithRange(float min, float max, float current)
        {
            _min = min;
            _max = max;
            _current = current;
            return this;
        }
        
        public ResourceDefinition Build()
        {
            return new ResourceDefinition
            {
                ResourceId = _id,
                DisplayName = _name,
                ShortName = _id,
                Category = _category,
                DefaultMin = _min,
                DefaultMax = _max,
                DefaultCurrent = _current
            };
        }
    }
    
    // ==================== EFFECT BUILDERS ====================
    
    public static EffectDefinitionBuilder Effect() => new();
    
    public class EffectDefinitionBuilder
    {
        private EffectType _type = EffectType.DAMAGE;
        private EffectTarget _target = EffectTarget.TARGET;
        private float? _flatValue;
        private string? _formulaValue;
        private List<string> _tags = new();
        
        public EffectDefinitionBuilder WithType(EffectType type)
        {
            _type = type;
            return this;
        }
        
        public EffectDefinitionBuilder WithTarget(EffectTarget target)
        {
            _target = target;
            return this;
        }
        
        public EffectDefinitionBuilder WithFlatValue(float value)
        {
            _flatValue = value;
            return this;
        }
        
        public EffectDefinitionBuilder WithFormula(string formula)
        {
            _formulaValue = formula;
            return this;
        }
        
        public EffectDefinitionBuilder WithTag(string tag)
        {
            _tags.Add(tag);
            return this;
        }
        
        public EffectDefinition Build()
        {
            return new EffectDefinition
            {
                Type = _type,
                Target = _target,
                FlatValue = _flatValue,
                FormulaValue = _formulaValue,
                Tags = _tags
            };
        }
    }
    
    // ==================== STATUS EFFECT BUILDERS ====================
    
    public static StatusEffectDefinitionBuilder StatusEffect() => new();
    
    public class StatusEffectDefinitionBuilder
    {
        private string _id = "test_status";
        private string _name = "Test Status";
        private StatusEffectType _type = StatusEffectType.STRENGTH;
        private int _duration = 3;
        private int _maxStacks = 1;
        
        public StatusEffectDefinitionBuilder WithId(string id)
        {
            _id = id;
            return this;
        }
        
        public StatusEffectDefinitionBuilder WithName(string name)
        {
            _name = name;
            return this;
        }
        
        public StatusEffectDefinitionBuilder WithType(StatusEffectType type)
        {
            _type = type;
            return this;
        }
        
        public StatusEffectDefinitionBuilder WithDuration(int duration)
        {
            _duration = duration;
            return this;
        }
        
        public StatusEffectDefinitionBuilder WithMaxStacks(int maxStacks)
        {
            _maxStacks = maxStacks;
            return this;
        }
        
        public StatusEffectDefinition Build()
        {
            return new StatusEffectDefinition
            {
                StatusId = _id,
                DisplayName = _name,
                Type = _type,
                DefaultDuration = _duration,
                MaxStacks = _maxStacks
            };
        }
    }
    
    // ==================== RUN STATE BUILDERS ====================
    
    public static RunStateBuilder RunState() => new();
    
    public class RunStateBuilder
    {
        private Guid _runId = Guid.NewGuid();
        private readonly List<ResourceAmount> _resources = [];
        private DeckState? _deckState;
        
        public RunStateBuilder WithRunId(Guid runId)
        {
            _runId = runId;
            return this;
        }
        
        public RunStateBuilder WithResource(string resourceId, float amount)
        {
            _resources.RemoveAll(resource => resource.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase));
            _resources.Add(new ResourceAmount { ResourceId = resourceId, Amount = amount });
            return this;
        }
        
        public RunStateBuilder WithDeckState(DeckState deckState)
        {
            _deckState = deckState;
            return this;
        }
        
        public RunState Build()
        {
            return new RunState
            {
                RunId = _runId,
                ResourceState = RunResources(_resources.ToArray()) with { OwnerId = $"run:{_runId}" },
                Deck = _deckState ?? new DeckState()
            };
        }
    }
    
    // ==================== VALIDATION BUILDERS ====================
    
    public static ValidationResultBuilder ValidationResult() => new();
    
    public class ValidationResultBuilder
    {
        private bool _isValid = true;
        private List<string> _errors = new();
        
        public ValidationResultBuilder WithError(string error)
        {
            _isValid = false;
            _errors.Add(error);
            return this;
        }
        
        public ValidationResultBuilder AsInvalid()
        {
            _isValid = false;
            return this;
        }
        
        public ValidationResult Build()
        {
            return new ValidationResult
            {
                IsValid = _isValid,
                Errors = _errors
            };
        }
    }
}
