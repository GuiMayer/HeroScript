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
        => CreateEntityCore(
            _definitionLoader.LoadDefinition(definitionId),
            definitionId,
            entityId ?? Guid.NewGuid().ToString(), // nondeterministic-boundary: editor convenience
            contentRevision: null,
            configName: null);

    public Result<Entity> CreateEntity(
        string definitionId,
        string entityId,
        string contentRevision,
        string settingId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
            return Result<Entity>.Failure("Entity id is required");
        if (string.IsNullOrWhiteSpace(contentRevision))
            return Result<Entity>.Failure("Content revision is required");
        if (string.IsNullOrWhiteSpace(settingId))
            return Result<Entity>.Failure("Setting id is required");

        return CreateEntityCore(
            _definitionLoader.LoadDefinition(definitionId, contentRevision, settingId),
            definitionId,
            entityId,
            contentRevision,
            settingId);
    }

    private Result<Entity> CreateEntityCore(
        Result<EntityDefinition> defResult,
        string definitionId,
        string entityId,
        string? contentRevision,
        string? configName)
    {
        try
        {
            if (!defResult.IsSuccess)
            {
                return Result<Entity>.Failure($"Failed to load definition: {defResult.Error}");
            }
            
            var definition = defResult.Value!;
            
            // Criar entidade base
            var entity = new Entity
            {
                EntityId = entityId,
                Type = definition.Type,
                DefinitionId = definitionId,
                DisplayName = definition.DisplayName
            };
            
            // Adicionar componentes
            entity = AddComponents(entity, definition, contentRevision, configName);
            
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
    private Entity AddComponents(
        Entity entity,
        EntityDefinition definition,
        string? contentRevision,
        string? configName)
    {
        // Adicionar ResourceComponent
        if (definition.Resources != null)
        {
            var resourceComp = CreateResourceComponent(
                entity.EntityId,
                definition.Resources,
                contentRevision,
                configName);
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
        
        return entity;
    }
    
    /// <summary>
    /// Cria ResourceComponent a partir da definição
    /// </summary>
    private ResourceComponent CreateResourceComponent(
        string entityId,
        ResourcesDefinition resourcesDef,
        string? contentRevision,
        string? configName)
    {
        var pools = new Dictionary<string, ResourcePool>();
        
        foreach (var (resourceId, poolDef) in resourcesDef.Resources)
        {
            // Obter definição do recurso do ResourceManager
            Result<ResourceDefinition> resourceDefResult;
            if (!string.IsNullOrWhiteSpace(contentRevision))
            {
                if (_resourceManager is not IRevisionedResourceManager revisioned)
                {
                    throw new InvalidOperationException(
                        "Revisioned resource manager is required for published entity creation");
                }
                resourceDefResult = revisioned.GetDefinition(resourceId, contentRevision, configName);
            }
            else
            {
                resourceDefResult = _resourceManager.GetDefinition(resourceId);
            }
            if (!resourceDefResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Resource definition not found for entity {entityId}: {resourceId}");
            }
            
            var resourceDef = resourceDefResult.Value!;
            pools[resourceId] = ResourcePool.Materialize(
                resourceDef,
                poolDef.Current,
                poolDef.Max);
        }
        
        var resourceState = new ResourceSet
        {
            OwnerId = entityId,
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
        var aiDef = definition.AI
            ?? throw new InvalidOperationException(
                $"Enemy {definition.DefinitionId} requires an AI definition");
        
        var behaviorType = aiDef.BehaviorTree.ToLowerInvariant() switch
        {
            "aggressive" => AIBehaviorType.AGGRESSIVE,
            "defensive" => AIBehaviorType.DEFENSIVE,
            "balanced" => AIBehaviorType.BALANCED,
            _ => throw new InvalidOperationException(
                $"Unsupported AI behavior tree for {definition.DefinitionId}: {aiDef.BehaviorTree}")
        };
        
        return new AIController(
            decisionResourceId: aiDef.DecisionResourceId,
            behaviorType: behaviorType,
            logger: _logger,
            controllerId: $"ai_{definition.DefinitionId}",
            lowResourceThreshold: aiDef.LowResourceThreshold,
            fleeResourceThreshold: aiDef.FleeResourceThreshold
        );
    }
}
