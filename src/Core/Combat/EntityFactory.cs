using Core.Resources;

namespace Core.Combat;

/// <summary>
/// Factory para criação de entidades de combate
/// </summary>
public class EntityFactory : IEntityFactory
{
    /// <summary>
    /// Cria uma entidade mock para testes/API
    /// </summary>
    /// <param name="entityId">ID da entidade</param>
    /// <param name="health">Vida inicial (padrão: 100)</param>
    /// <returns>Entidade criada</returns>
    public CombatEntity CreateMockEntity(string entityId, float health = 100f)
    {
        // Criar definição de recurso health mock
        var healthDef = new ResourceDefinition
        {
            ResourceId = "health",
            DisplayName = "Health",
            Category = ResourceCategory.VITAL,
            DefaultCurrent = health,
            DefaultMax = health,
            DefaultMin = 0,
            CanBeNegative = false,
            CanExceedMax = false,
            Tags = new List<string>()
        };
        
        // Criar pool de health
        var healthPool = new ResourcePool
        {
            ResourceId = "health",
            Current = health,
            Maximum = health,
            Minimum = 0,
            Definition = healthDef
        };
        
        // Criar EntityResourceState
        var resourceState = new EntityResourceState
        {
            EntityId = entityId,
            Resources = new Dictionary<string, ResourcePool> { ["health"] = healthPool }
        };
        
        // Criar entidade
        var entity = new CombatEntity
        {
            EntityId = entityId,
            Name = entityId,
            IsHero = false,
            ResourceState = resourceState
        };
        
        return entity;
    }
}
