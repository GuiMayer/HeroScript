using Core.Combat.Models;
using Core.Common;
using Core.Entity.Components;
using Core.Entity.Controllers;
using Core.Logging;
using Core.Resources;

namespace Core.Entity.Definitions;

/// <summary>
/// Factory para criar instâncias de Entity a partir de EntityDefinition.
/// Converte definições JSON em entidades funcionais com componentes e controllers.
/// </summary>
public class EntityFactory
{
    private readonly EntityDefinitionLoader _definitionLoader;
    private readonly IResourceManager _resourceManager;
    private readonly ILogger _logger;
    
    public EntityFactory(
        EntityDefinitionLoader definitionLoader,
        IResourceManager resourceManager,
        ILogger? logger = null)
    {
        _definitionLoader = definitionLoader ?? throw new ArgumentNullException(nameof(definitionLoader));
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _logger = logger ?? NullLogger.Instance;
    }
    
    /// <summary>
    /// Cria uma entidade a partir de uma definição
    /// </summary>
    public Result<Entity> CreateEntity(string definitionId, string? entityId = null)
    {
        try
        {
            // Carregar definição
            var defResult = _definitionLoader.LoadDefinition(definitionId);
            if (!defResult.IsSuccess)
            {
                return Result<Entity>.Failure($"Failed to load definition: {defResult.Error}");
            }
            
            var definition = defResult.Value!;
            
            // Criar entidade base
            var entity = new Entity
            {
                EntityId = entityId ?? Guid.NewGuid().ToString(), // nondeterministic-boundary: editor/API convenience
                Type = definition.Type,
                DefinitionId = definitionId,
                DisplayName = definition.DisplayName
            };
            
            // Adicionar componentes
            entity = AddComponents(entity, definition);
            
            // Adicionar controller
            var controllerResult = CreateController(definition);
            if (controllerResult.IsSuccess && controllerResult.Value != null)
            {
                entity = entity with { Controller = controllerResult.Value };
            }
            
            _logger.LogInformation($"Created entity {entity.EntityId} from definition {definitionId}");
            return Result<Entity>.Success(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creating entity from {definitionId}: {ex.Message}", ex);
            return Result<Entity>.Failure($"Error creating entity: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Adiciona componentes à entidade baseado na definição
    /// </summary>
    private Entity AddComponents(Entity entity, EntityDefinition definition)
    {
        // Adicionar ResourceComponent
        if (definition.Resources != null)
        {
            var resourceComp = CreateResourceComponent(entity.EntityId, definition.Resources);
            entity = entity.AddComponent(resourceComp);
        }
        
        // Adicionar StatsComponent
        if (definition.Stats != null)
        {
            var statsComp = CreateStatsComponent(definition.Stats);
            entity = entity.AddComponent(statsComp);
        }
        
        // Adicionar InventoryComponent
        if (definition.Inventory != null)
        {
            var inventoryComp = CreateInventoryComponent(definition.Inventory);
            entity = entity.AddComponent(inventoryComp);
        }
        
        // Adicionar StatusEffectComponent (sempre presente)
        var statusComp = new StatusEffectComponent();
        entity = entity.AddComponent(statusComp);
        
        return entity;
    }
    
    /// <summary>
    /// Cria ResourceComponent a partir da definição
    /// </summary>
    private ResourceComponent CreateResourceComponent(string entityId, ResourcesDefinition resourcesDef)
    {
        var pools = new Dictionary<string, ResourcePool>();
        
        foreach (var (resourceId, poolDef) in resourcesDef.Resources)
        {
            // Obter definição do recurso do ResourceManager
            var resourceDefResult = _resourceManager.GetDefinition(resourceId);
            if (!resourceDefResult.IsSuccess)
            {
                _logger.LogWarning($"Resource definition not found: {resourceId}");
                continue;
            }
            
            var resourceDef = resourceDefResult.Value!;
            
            var pool = new ResourcePool
            {
                ResourceId = resourceId,
                Current = poolDef.Current,
                Maximum = poolDef.Max,
                Definition = resourceDef
            };
            
            pools[resourceId] = pool;
        }
        
        var resourceState = new EntityResourceState
        {
            EntityId = entityId,
            Resources = pools
        };
        
        return new ResourceComponent(resourceState);
    }
    
    /// <summary>
    /// Cria StatsComponent a partir da definição
    /// </summary>
    private StatsComponent CreateStatsComponent(StatsDefinition statsDef)
    {
        return new StatsComponent(
            strength: statsDef.Strength,
            dexterity: statsDef.Dexterity,
            intelligence: statsDef.Intelligence,
            constitution: statsDef.Constitution,
            wisdom: statsDef.Wisdom,
            charisma: statsDef.Charisma,
            customStats: new Dictionary<string, float>(statsDef.CustomStats)
        );
    }
    
    /// <summary>
    /// Cria InventoryComponent a partir da definição
    /// </summary>
    private InventoryComponent CreateInventoryComponent(InventoryDefinition inventoryDef)
    {
        // Por enquanto, apenas cria vazio com capacidade
        // Itens iniciais serão adicionados na Fase 4 (Content System)
        return new InventoryComponent(maxCapacity: inventoryDef.MaxCapacity);
    }
    
    /// <summary>
    /// Cria controller apropriado baseado no tipo de entidade
    /// </summary>
    private Result<IEntityController?> CreateController(EntityDefinition definition)
    {
        try
        {
            IEntityController? controller = definition.Type switch
            {
                EntityType.PLAYER => new PlayerController($"player_{definition.DefinitionId}"),
                
                EntityType.COMPANION => new GambitController($"gambit_{definition.DefinitionId}"),
                
                EntityType.ENEMY => CreateAIController(definition),
                
                EntityType.NPC => null, // NPCs não têm controller de combate
                
                _ => null
            };
            
            return Result<IEntityController?>.Success(controller);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creating controller: {ex.Message}", ex);
            return Result<IEntityController?>.Failure($"Error creating controller: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Cria AIController a partir da definição de AI
    /// </summary>
    private AIController CreateAIController(EntityDefinition definition)
    {
        var aiDef = definition.AI ?? new AIDefinition();
        
        var behaviorType = aiDef.BehaviorTree.ToLowerInvariant() switch
        {
            "aggressive" => AIBehaviorType.AGGRESSIVE,
            "defensive" => AIBehaviorType.DEFENSIVE,
            "balanced" => AIBehaviorType.BALANCED,
            _ => AIBehaviorType.BALANCED
        };
        
        return new AIController(
            behaviorType: behaviorType,
            logger: _logger,
            controllerId: $"ai_{definition.DefinitionId}",
            lowHealthThreshold: aiDef.LowHealthThreshold,
            fleeHealthThreshold: aiDef.FleeHealthThreshold
        );
    }
}
