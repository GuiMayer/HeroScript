using Core.Common;
using Core.Logging;
using Core.Resources;

namespace Core.Combat;

/// <summary>
/// Factory para criação de entidades de combate
/// Segue padrões estabelecidos em docs/core-service-patterns.md
/// </summary>
public class EntityFactory : IEntityFactory
{
    private readonly ILogger _logger;

    public EntityFactory(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Cria uma entidade mock para testes/API
    /// </summary>
    /// <param name="entityId">ID da entidade</param>
    /// <param name="health">Vida inicial (padrão: 100)</param>
    /// <returns>Resultado com a entidade criada</returns>
    public Result<CombatEntity> CreateMockEntity(string entityId, float health = 100f)
    {
        if (string.IsNullOrWhiteSpace(entityId))
            return Result<CombatEntity>.Failure("Entity ID cannot be empty");

        if (health <= 0)
            return Result<CombatEntity>.Failure("Health must be greater than zero");

        if (health > 10000)
            return Result<CombatEntity>.Failure("Health cannot exceed 10000");

        try
        {
            _logger.LogDebug($"Creating mock entity {entityId} with health {health}");

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

            _logger.LogDebug($"Mock entity {entityId} created successfully");

            return Result<CombatEntity>.Success(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to create mock entity {entityId}: {ex.Message}", ex);
            return Result<CombatEntity>.Failure($"Failed to create entity: {ex.Message}");
        }
    }
}
