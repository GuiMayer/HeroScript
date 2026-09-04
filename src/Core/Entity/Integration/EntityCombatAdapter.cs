using Core.Combat.Models;
using Core.Common;
using Core.Entity.Components;
using Core.Logging;
using Core.Resources;

namespace Core.Entity.Integration;

/// <summary>
/// Adapter que converte entre Entity (novo sistema) e CombatEntity (sistema de combate).
/// Permite que o CombatSystem continue usando sua estrutura imutável enquanto
/// integra com o novo sistema de componentes.
/// </summary>
public class EntityCombatAdapter
{
    private readonly IResourceManager _resourceManager;
    
    public EntityCombatAdapter(IResourceManager resourceManager)
    {
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
    }
    
    /// <summary>
    /// Converte Entity para CombatEntity.
    /// Cria snapshot imutável do estado atual da entidade para uso em combate.
    /// </summary>
    public CombatEntity ToCombatEntity(Entity entity)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));
        
        // Obter componente de recursos
        var resourceComponent = entity.GetComponent<ResourceComponent>();
        if (resourceComponent == null)
            throw new InvalidOperationException($"Entity {entity.EntityId} has no ResourceComponent");
        
        // Obter ResourceSet do ResourceComponent
        var resourceState = resourceComponent.ResourceState;
        
        // Criar CombatEntity
        return new CombatEntity
        {
            EntityId = entity.EntityId,
            Name = entity.DisplayName ?? entity.EntityId,
            IsHero = entity.Type == EntityType.PLAYER || entity.Type == EntityType.COMPANION,
            ResourceState = resourceState
        };
    }
    
    /// <summary>
    /// Atualiza Entity com o estado de CombatEntity.
    /// Sincroniza recursos após mudanças em combate.
    /// </summary>
    public void UpdateEntityFromCombat(Entity entity, CombatEntity combatEntity)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));
        if (combatEntity == null)
            throw new ArgumentNullException(nameof(combatEntity));
        if (entity.EntityId != combatEntity.EntityId)
            throw new InvalidOperationException("Entity IDs do not match");
        
        // Obter componente de recursos
        var resourceComponent = entity.GetComponent<ResourceComponent>();
        if (resourceComponent == null)
            throw new InvalidOperationException($"Entity {entity.EntityId} has no ResourceComponent");
        
        // Atualizar recursos - trocar o snapshot completo e já reduzido.
        var updatedComponent = resourceComponent.WithState(combatEntity.ResourceState);
        
        // Substituir componente na entidade
        entity.RemoveComponent<ResourceComponent>();
        entity.AddComponent(updatedComponent);
    }
    
    /// <summary>
    /// Cria CombatEntity a partir de uma definição de entidade.
    /// Útil para criar entidades de combate diretamente de JSON.
    /// </summary>
    public CombatEntity CreateCombatEntityFromDefinition(
        string entityId,
        Definitions.EntityDefinition definition)
        => CreateCombatEntityFromDefinition(entityId, definition, null, null);

    public CombatEntity CreateCombatEntityFromDefinition(
        string entityId,
        Definitions.EntityDefinition definition,
        string? contentRevision,
        string? configName = null)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));
        
        // Criar recursos a partir da definição
        var resources = new Dictionary<string, ResourcePool>();
        
        if (definition.Resources?.Resources != null)
        {
            foreach (var (resourceId, resourceDef) in definition.Resources.Resources)
            {
                if (float.IsNaN(resourceDef.Current) || float.IsInfinity(resourceDef.Current) ||
                    float.IsNaN(resourceDef.Max) || float.IsInfinity(resourceDef.Max))
                    throw new InvalidOperationException($"Entity resource values must be finite: {resourceId}");
                var pool = !string.IsNullOrWhiteSpace(contentRevision) &&
                           _resourceManager is IRevisionedResourceManager revisionedResources
                    ? revisionedResources.CreatePool(
                        resourceId,
                        resourceDef.Current,
                        contentRevision,
                        configName).Match(
                            value => value,
                            error => throw new InvalidOperationException(error))
                    : _resourceManager.CreatePool(resourceId, resourceDef.Current);
                // Ajustar o máximo se necessário
                if (resourceDef.Max != pool.Maximum)
                {
                    pool = pool with { Maximum = resourceDef.Max };
                }
                pool = pool.Set(resourceDef.Current);
                resources[resourceId] = pool;
            }
        }
        
        // Criar ResourceSet
        var resourceState = new ResourceSet
        {
            OwnerId = entityId,
            Resources = resources
        };
        
        // Criar CombatEntity
        return new CombatEntity
        {
            EntityId = entityId,
            Name = definition.DisplayName ?? entityId,
            IsHero = definition.Type == EntityType.PLAYER || definition.Type == EntityType.COMPANION,
            ResourceState = resourceState
        };
    }
    
    /// <summary>
    /// Converte lista de Entities para lista de CombatEntities.
    /// </summary>
    public List<CombatEntity> ToCombatEntities(IEnumerable<Entity> entities)
    {
        if (entities == null)
            throw new ArgumentNullException(nameof(entities));
        
        return entities.Select(ToCombatEntity).ToList();
    }
    
    /// <summary>
    /// Atualiza múltiplas Entities a partir de CombatEntities.
    /// </summary>
    public void UpdateEntitiesFromCombat(
        IEnumerable<Entity> entities,
        IEnumerable<CombatEntity> combatEntities)
    {
        if (entities == null)
            throw new ArgumentNullException(nameof(entities));
        if (combatEntities == null)
            throw new ArgumentNullException(nameof(combatEntities));
        
        var entityDict = entities.ToDictionary(e => e.EntityId);
        
        foreach (var combatEntity in combatEntities)
        {
            if (entityDict.TryGetValue(combatEntity.EntityId, out var entity))
            {
                UpdateEntityFromCombat(entity, combatEntity);
            }
        }
    }
}
