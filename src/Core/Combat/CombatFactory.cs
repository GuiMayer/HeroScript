using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Common;
using Core.Determinism;
using Core.Entity.Definitions;
using Core.Entity.Integration;
using Core.Resources;
using Core.StatusEffects;

namespace Core.Combat;

/// <summary>
/// Creates an immutable initial combat snapshot from explicit deterministic
/// inputs. The owning RunState is the only authoritative session store.
/// </summary>
public sealed class CombatFactory : ICombatFactory
{
    private readonly ITurnOrderCalculator _turnOrder;
    private readonly EntityDefinitionLoader? _entities;
    private readonly EntityCombatAdapter _adapter;

    public CombatFactory(
        IResourceManager resources,
        ITurnOrderCalculator turnOrder,
        EntityDefinitionLoader? entities = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _turnOrder = turnOrder ?? throw new ArgumentNullException(nameof(turnOrder));
        _entities = entities;
        _adapter = new EntityCombatAdapter(resources);
    }

    public Result<CombatState> Create(
        CombatParticipantReference hero,
        IReadOnlyList<CombatParticipantReference> enemies,
        CombatStartOptions options)
    {
        if (hero == null || string.IsNullOrWhiteSpace(hero.EntityId))
            return Result<CombatState>.Failure("Hero entity ID cannot be empty");
        if (string.IsNullOrWhiteSpace(hero.DefinitionId))
            return Result<CombatState>.Failure("Hero definition ID cannot be empty");
        if (enemies == null || enemies.Count == 0)
            return Result<CombatState>.Failure("At least one enemy is required");
        if (enemies.Any(enemy => enemy == null ||
                                 string.IsNullOrWhiteSpace(enemy.EntityId) ||
                                 string.IsNullOrWhiteSpace(enemy.DefinitionId)))
            return Result<CombatState>.Failure("Every enemy requires an entity ID and definition ID");

        var unique = ValidateUniqueParticipantIds(
            enemies.Select(enemy => enemy.EntityId).Append(hero.EntityId));
        if (unique.IsFailure)
            return Result<CombatState>.Failure(unique.Error);

        var validated = ValidateOptions(
            options,
            enemies.Select(enemy => enemy.EntityId).Append(hero.EntityId));
        if (validated.IsFailure)
            return Result<CombatState>.Failure(validated.Error);

        var materializedHero = CreateConfiguredEntity(
            hero.EntityId,
            hero.DefinitionId,
            expectHero: true,
            options.ContentRevision);
        if (materializedHero.IsFailure)
            return Result<CombatState>.Failure(materializedHero.Error);

        var materializedEnemies = new List<CombatEntity>(enemies.Count);
        foreach (var enemy in enemies)
        {
            var materialized = CreateConfiguredEntity(
                enemy.EntityId,
                enemy.DefinitionId,
                expectHero: false,
                options.ContentRevision);
            if (materialized.IsFailure)
                return Result<CombatState>.Failure(materialized.Error);
            materializedEnemies.Add(materialized.Value);
        }

        return Create(materializedHero.Value, materializedEnemies, options);
    }

    public Result<CombatState> Create(
        Entity.Entity hero,
        IReadOnlyList<Entity.Entity> enemies,
        CombatStartOptions options)
    {
        if (hero == null)
            return Result<CombatState>.Failure("Hero entity cannot be null");
        if (enemies == null || enemies.Count == 0)
            return Result<CombatState>.Failure("At least one enemy is required");
        if (enemies.Any(enemy => enemy == null))
            return Result<CombatState>.Failure("Enemy entity cannot be null");

        return Create(
            _adapter.ToCombatEntity(hero),
            _adapter.ToCombatEntities(enemies).ToArray(),
            options);
    }

    public Result<CombatState> Create(
        CombatEntity hero,
        IReadOnlyList<CombatEntity> enemies,
        CombatStartOptions options)
    {
        if (hero == null)
            return Result<CombatState>.Failure("Hero combat entity cannot be null");
        if (!hero.IsHero)
            return Result<CombatState>.Failure("Scenario hero must be a hero entity");
        if (enemies == null || enemies.Count == 0)
            return Result<CombatState>.Failure("At least one enemy is required");
        if (enemies.Any(enemy => enemy == null || enemy.IsHero))
            return Result<CombatState>.Failure("Scenario enemies must be non-hero entities");

        var unique = ValidateUniqueParticipantIds(
            enemies.Select(enemy => enemy.EntityId).Append(hero.EntityId));
        if (unique.IsFailure)
            return Result<CombatState>.Failure(unique.Error);

        var validated = ValidateOptions(
            options,
            enemies.Select(enemy => enemy.EntityId).Append(hero.EntityId));
        if (validated.IsFailure)
            return Result<CombatState>.Failure(validated.Error);

        var heroOverride = ApplyInitialResourceValues(hero, options.InitialResourceValues);
        if (heroOverride.IsFailure)
            return Result<CombatState>.Failure(heroOverride.Error);
        var overriddenEnemies = new List<CombatEntity>(enemies.Count);
        foreach (var enemy in enemies)
        {
            var overridden = ApplyInitialResourceValues(enemy, options.InitialResourceValues);
            if (overridden.IsFailure)
                return Result<CombatState>.Failure(overridden.Error);
            overriddenEnemies.Add(overridden.Value);
        }

        var context = DeterministicContext.Create(options.Seed!.Value, options.ContentRevision);
        var combat = CombatTransitions.Create(
            heroOverride.Value,
            overriddenEnemies,
            context,
            options.IdScope ?? "combat") with
        {
            RunId = options.RunId,
            RunNodeId = options.RunNodeId
        };
        combat = ApplyInitialStatusEffects(combat, options.InitialStatusEffects);
        return InitializeTurnOrder(combat);
    }

    private Result<CombatEntity> CreateConfiguredEntity(
        string entityId,
        string definitionId,
        bool expectHero,
        string contentRevision)
    {
        if (_entities == null)
            return Result<CombatEntity>.Failure("Entity definition catalog is unavailable");
        var definition = _entities.LoadDefinition(definitionId, contentRevision);
        if (definition.IsFailure)
            return Result<CombatEntity>.Failure(definition.Error);

        var entity = _adapter.CreateCombatEntityFromDefinition(
            entityId,
            definition.Value,
            contentRevision);
        return entity.IsHero == expectHero
            ? Result<CombatEntity>.Success(entity)
            : Result<CombatEntity>.Failure(
                $"Entity {entityId} is not a valid {(expectHero ? "hero" : "enemy")} participant");
    }

    private Result<CombatState> InitializeTurnOrder(CombatState combat)
    {
        var initialized = _turnOrder.InitializeState(combat);
        if (initialized.IsFailure)
        {
            return Result<CombatState>.Failure(
                $"Failed to initialize turn order calculator: {initialized.Error}");
        }

        var calculated = _turnOrder.Calculate(initialized.Value);
        return calculated.IsFailure
            ? Result<CombatState>.Failure($"Failed to calculate turn order: {calculated.Error}")
            : Result<CombatState>.Success(
                calculated.Value.State with { TurnOrder = calculated.Value.Order });
    }

    private static Result ValidateOptions(
        CombatStartOptions? options,
        IEnumerable<string> participantIds)
    {
        if (options == null)
            return Result.Failure("Combat start options are required");
        if (!options.Seed.HasValue)
            return Result.Failure("Combat seed is required");
        if (string.IsNullOrWhiteSpace(options.ContentRevision))
            return Result.Failure("Content revision cannot be empty");
        return ValidateInitialResourceValueOwners(options.InitialResourceValues, participantIds);
    }

    private static Result ValidateUniqueParticipantIds(IEnumerable<string> participantIds)
    {
        var duplicates = participantIds
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        return duplicates.Length == 0
            ? Result.Success()
            : Result.Failure(
                $"Combat participant entity IDs must be unique: {string.Join(", ", duplicates)}");
    }

    private static Result<CombatEntity> ApplyInitialResourceValues(
        CombatEntity entity,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, float>>? valuesByEntity)
    {
        if (valuesByEntity == null || !valuesByEntity.TryGetValue(entity.EntityId, out var values))
            return Result<CombatEntity>.Success(entity);

        var current = entity;
        foreach (var (resourceId, value) in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!float.IsFinite(value))
            {
                return Result<CombatEntity>.Failure(
                    $"Initial resource value must be finite: {entity.EntityId}/{resourceId}");
            }
            if (current.GetResource(resourceId) == null)
            {
                return Result<CombatEntity>.Failure(
                    $"Initial resource override references an unknown resource: {entity.EntityId}/{resourceId}");
            }

            var applied = current.ApplyResourceMutation(
                $"combat-start:{entity.EntityId}:{resourceId}",
                resourceId,
                ResourceMutationOperation.Set,
                value);
            if (applied.IsFailure)
                return Result<CombatEntity>.Failure(applied.Error);
            current = applied.Value;
        }

        return Result<CombatEntity>.Success(current);
    }

    private static Result ValidateInitialResourceValueOwners(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, float>>? valuesByEntity,
        IEnumerable<string> participantIds)
    {
        if (valuesByEntity == null)
            return Result.Success();

        var knownParticipants = participantIds.ToHashSet(StringComparer.Ordinal);
        foreach (var (entityId, resourceValues) in valuesByEntity)
        {
            if (string.IsNullOrWhiteSpace(entityId) || !knownParticipants.Contains(entityId))
            {
                return Result.Failure(
                    $"Initial resource override references an unknown combat participant: {entityId}");
            }
            if (resourceValues == null)
            {
                return Result.Failure(
                    $"Initial resource values are required for combat participant: {entityId}");
            }
        }

        return Result.Success();
    }

    private static CombatState ApplyInitialStatusEffects(
        CombatState combat,
        IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>? initialStatuses)
    {
        if (initialStatuses == null || initialStatuses.Count == 0)
            return combat;

        var statuses = initialStatuses
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToImmutableDictionary(
                item => item.Key,
                item => item.Value
                    .Where(status => status.IsActive)
                    .OrderBy(status => status.InstanceId)
                    .ToImmutableArray(),
                StringComparer.Ordinal);
        return combat with { StatusEffects = statuses };
    }
}
