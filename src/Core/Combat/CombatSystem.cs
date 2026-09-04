using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Common;
using Core.Damage;
using Core.Determinism;
using Core.Effects;
using Core.Entity.Definitions;
using Core.Entity.Integration;
using Core.Events;
using Core.Events.Domain;
using Core.Logging;
using Core.Resources;
using Core.StatusEffects;
using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Core.Combat;

/// <summary>
/// Sistema de combate com gerenciamento de estado imutável.
/// Thread-safe usando ConcurrentDictionary.
/// Usa sistema de recursos genérico configurável via JSON.
/// </summary>
public class CombatSystem : ICombatSystem
{
    private readonly ConcurrentDictionary<Guid, CombatState> _activeCombats = new();
    private readonly ConcurrentDictionary<Guid, object> _combatLocks = new();
    private readonly object _statusSnapshotLock = new();
    private readonly IEventBus? _eventBus;
    private readonly ILogger _logger;
    private readonly IResourceManager _resourceManager;
    private readonly IDamageCalculator? _damageCalculator;
    private readonly IStatusEffectManager? _statusEffectManager;
    private readonly IResourceRegenerationProcessor? _regenerationProcessor;
    private readonly ITurnOrderCalculator _turnOrderCalculator;
    private readonly IActionManager? _actionManager;
    private readonly IEffectResolver? _effectResolver;
    private readonly IActionCostEvaluator? _actionCostEvaluator;
    private readonly EntityDefinitionLoader? _entityDefinitionLoader;
    private readonly EntityCombatAdapter _entityAdapter;
    
    private const string BasicAttackActionId = "basic_attack";
    
    public CombatSystem(
        ILogger logger, 
        IResourceManager resourceManager,
        ITurnOrderCalculator turnOrderCalculator,
        IEventBus? eventBus = null, 
        IDamageCalculator? damageCalculator = null,
        IStatusEffectManager? statusEffectManager = null,
        IResourceRegenerationProcessor? regenerationProcessor = null,
        IActionManager? actionManager = null,
        EntityDefinitionLoader? entityDefinitionLoader = null,
        IEffectResolver? effectResolver = null,
        IActionCostEvaluator? actionCostEvaluator = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _turnOrderCalculator = turnOrderCalculator ?? throw new ArgumentNullException(nameof(turnOrderCalculator));
        _eventBus = eventBus;
        _damageCalculator = damageCalculator;
        _statusEffectManager = statusEffectManager;
        _regenerationProcessor = regenerationProcessor;
        _actionManager = actionManager;
        _effectResolver = effectResolver;
        _actionCostEvaluator = actionCostEvaluator;
        _entityDefinitionLoader = entityDefinitionLoader;
        _entityAdapter = new EntityCombatAdapter(_resourceManager);
    }
    
    public Result<CombatState> StartCombat(
        CombatParticipantReference hero,
        IReadOnlyList<CombatParticipantReference> enemies)
        => StartCombat(hero, enemies, new CombatStartOptions(Seed: CreateSeed()));

    public Result<CombatState> StartCombat(
        CombatParticipantReference hero,
        IReadOnlyList<CombatParticipantReference> enemies,
        CombatStartOptions options)
    {
        try
        {
            // Validações
            if (hero == null || string.IsNullOrWhiteSpace(hero.EntityId))
                return Result<CombatState>.Failure("Hero entity ID cannot be empty");
            if (string.IsNullOrWhiteSpace(hero.DefinitionId))
                return Result<CombatState>.Failure("Hero definition ID cannot be empty");
            
            if (enemies == null || enemies.Count == 0)
                return Result<CombatState>.Failure("At least one enemy is required");
            if (enemies.Any(enemy =>
                    enemy == null ||
                    string.IsNullOrWhiteSpace(enemy.EntityId) ||
                    string.IsNullOrWhiteSpace(enemy.DefinitionId)))
            {
                return Result<CombatState>.Failure(
                    "Every enemy requires an entity ID and definition ID");
            }
            var duplicateIds = enemies
                .Select(enemy => enemy.EntityId)
                .Append(hero.EntityId)
                .GroupBy(id => id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (duplicateIds.Length > 0)
            {
                return Result<CombatState>.Failure(
                    $"Combat participant entity IDs must be unique: {string.Join(", ", duplicateIds)}");
            }
            
            if (options == null)
                return Result<CombatState>.Failure("Combat start options are required");

            if (string.IsNullOrWhiteSpace(options.ContentRevision))
                return Result<CombatState>.Failure("Content revision cannot be empty");

            var overrideValidation = ValidateInitialResourceValueOwners(
                options.InitialResourceValues,
                enemies.Select(enemy => enemy.EntityId).Append(hero.EntityId));
            if (overrideValidation.IsFailure)
                return Result<CombatState>.Failure(overrideValidation.Error);
            
            var heroResult = CreateConfiguredCombatEntity(
                hero.EntityId,
                hero.DefinitionId,
                expectHero: true,
                options.ContentRevision);
            if (heroResult.IsFailure)
                return Result<CombatState>.Failure(heroResult.Error);
            var heroOverride = ApplyInitialResourceValues(heroResult.Value, options.InitialResourceValues);
            if (heroOverride.IsFailure)
                return Result<CombatState>.Failure(heroOverride.Error);

            var materializedEnemies = new List<CombatEntity>(enemies.Count);
            foreach (var enemy in enemies)
            {
                var enemyResult = CreateConfiguredCombatEntity(
                    enemy.EntityId,
                    enemy.DefinitionId,
                    expectHero: false,
                    options.ContentRevision);
                if (enemyResult.IsFailure)
                    return Result<CombatState>.Failure(enemyResult.Error);
                var enemyOverride = ApplyInitialResourceValues(
                    enemyResult.Value,
                    options.InitialResourceValues);
                if (enemyOverride.IsFailure)
                    return Result<CombatState>.Failure(enemyOverride.Error);
                materializedEnemies.Add(enemyOverride.Value);
            }
            var materializedHero = heroOverride.Value;
            
            var context = DeterministicContext.Create(
                options.Seed ?? CreateSeed(),
                options.ContentRevision);
            var combatState = CombatTransitions.Create(
                materializedHero,
                materializedEnemies,
                context,
                options.IdScope ?? "combat") with
            {
                RunId = options.RunId,
                RunNodeId = options.RunNodeId
            };
            combatState = ApplyInitialStatusEffects(combatState, options.InitialStatusEffects);
            var initialized = InitializeCombatState(combatState);
            if (initialized.IsFailure)
                return initialized;
            combatState = initialized.Value;
            
            // Adicionar ao dicionário
            if (!_activeCombats.TryAdd(combatState.CombatId, combatState))
                return Result<CombatState>.Failure("Failed to create combat (ID collision)");
            _combatLocks.TryAdd(combatState.CombatId, new object());
            
            // Publicar evento
            _eventBus?.Publish(new CombatStartedEvent
            {
                CombatId = combatState.CombatId,
                HeroId = materializedHero.EntityId,
                EnemyIds = materializedEnemies.Select(enemy => enemy.EntityId).ToArray(),
                Target = combatState.CombatId.ToString()
            });
            
            _logger.LogInformation($"Combat started: {combatState.CombatId}");
            return Result<CombatState>.Success(combatState);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error starting combat: {ex.Message}");
            return Result<CombatState>.Failure($"Failed to start combat: {ex.Message}");
        }
    }
    
    public Result<CombatState> StartCombatWithEntities(Entity.Entity hero, List<Entity.Entity> enemies)
        => StartCombatWithEntities(hero, enemies, new CombatStartOptions(Seed: CreateSeed()));

    public Result<CombatState> StartCombatWithEntities(
        Entity.Entity hero,
        List<Entity.Entity> enemies,
        CombatStartOptions options)
    {
        try
        {
            // Validações
            if (hero == null)
                return Result<CombatState>.Failure("Hero entity cannot be null");
            
            if (enemies == null || enemies.Count == 0)
                return Result<CombatState>.Failure("At least one enemy is required");
            if (enemies.Any(enemy => enemy == null))
                return Result<CombatState>.Failure("Enemy entity cannot be null");

            if (options == null)
                return Result<CombatState>.Failure("Combat start options are required");

            if (string.IsNullOrWhiteSpace(options.ContentRevision))
                return Result<CombatState>.Failure("Content revision cannot be empty");

            var overrideValidation = ValidateInitialResourceValueOwners(
                options.InitialResourceValues,
                enemies.Select(enemy => enemy.EntityId).Append(hero.EntityId));
            if (overrideValidation.IsFailure)
                return Result<CombatState>.Failure(overrideValidation.Error);
            
            // Converter entidades para CombatEntity usando o adapter
            var heroResult = ApplyInitialResourceValues(
                _entityAdapter.ToCombatEntity(hero),
                options.InitialResourceValues);
            if (heroResult.IsFailure)
                return Result<CombatState>.Failure(heroResult.Error);
            var enemiesCombat = new List<CombatEntity>(enemies.Count);
            foreach (var enemy in _entityAdapter.ToCombatEntities(enemies))
            {
                var overridden = ApplyInitialResourceValues(enemy, options.InitialResourceValues);
                if (overridden.IsFailure)
                    return Result<CombatState>.Failure(overridden.Error);
                enemiesCombat.Add(overridden.Value);
            }
            var heroCombat = heroResult.Value;
            
            var context = DeterministicContext.Create(
                options.Seed ?? CreateSeed(),
                options.ContentRevision);
            var combatState = CombatTransitions.Create(heroCombat, enemiesCombat, context, options.IdScope ?? "combat") with
            {
                RunId = options.RunId,
                RunNodeId = options.RunNodeId
            };
            combatState = ApplyInitialStatusEffects(combatState, options.InitialStatusEffects);
            var initialized = InitializeCombatState(combatState);
            if (initialized.IsFailure)
                return initialized;
            combatState = initialized.Value;
            
            // Adicionar ao dicionário
            if (!_activeCombats.TryAdd(combatState.CombatId, combatState))
                return Result<CombatState>.Failure("Failed to create combat (ID collision)");
            _combatLocks.TryAdd(combatState.CombatId, new object());
            
            // Publicar evento
            _eventBus?.Publish(new CombatStartedEvent
            {
                CombatId = combatState.CombatId,
                HeroId = hero.EntityId,
                EnemyIds = enemies.Select(e => e.EntityId).ToList(),
                Target = combatState.CombatId.ToString()
            });
            
            _logger.LogInformation($"Combat started with entities: {combatState.CombatId}");
            return Result<CombatState>.Success(combatState);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error starting combat with entities: {ex.Message}");
            return Result<CombatState>.Failure($"Failed to start combat: {ex.Message}");
        }
    }
    
    public Result<CombatState> ExecuteAction(Guid combatId, CombatActionCommand command)
    {
        if (command == null)
            return Result<CombatState>.Failure("Combat action command is required");

        var combatLock = _combatLocks.GetOrAdd(combatId, _ => new object());
        lock (combatLock)
        {
            if (_statusEffectManager == null)
                return ExecuteActionLocked(combatId, command);
            lock (_statusSnapshotLock)
                return ExecuteActionLocked(combatId, command);
        }
    }

    public Result<CombatState> StartCombatWithCombatEntities(
        CombatEntity hero,
        IReadOnlyList<CombatEntity> enemies,
        CombatStartOptions options)
    {
        try
        {
            if (hero == null)
                return Result<CombatState>.Failure("Hero combat entity cannot be null");
            if (!hero.IsHero)
                return Result<CombatState>.Failure("Scenario hero must be a hero entity");
            if (enemies == null || enemies.Count == 0)
                return Result<CombatState>.Failure("At least one enemy is required");
            if (enemies.Any(enemy => enemy == null || enemy.IsHero))
                return Result<CombatState>.Failure("Scenario enemies must be non-hero entities");
            if (options == null || string.IsNullOrWhiteSpace(options.ContentRevision))
                return Result<CombatState>.Failure("Content revision cannot be empty");

            var overrideValidation = ValidateInitialResourceValueOwners(
                options.InitialResourceValues,
                enemies.Select(enemy => enemy.EntityId).Append(hero.EntityId));
            if (overrideValidation.IsFailure)
                return Result<CombatState>.Failure(overrideValidation.Error);

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

            var context = DeterministicContext.Create(
                options.Seed ?? CreateSeed(),
                options.ContentRevision);
            var combatState = CombatTransitions.Create(
                heroOverride.Value,
                overriddenEnemies,
                context,
                options.IdScope ?? "combat") with
            {
                RunId = options.RunId,
                RunNodeId = options.RunNodeId
            };
            combatState = ApplyInitialStatusEffects(combatState, options.InitialStatusEffects);
            var initialized = InitializeCombatState(combatState);
            if (initialized.IsFailure)
                return initialized;

            if (!_activeCombats.TryAdd(initialized.Value.CombatId, initialized.Value))
                return Result<CombatState>.Failure("Failed to create combat (ID collision)");
            _combatLocks.TryAdd(initialized.Value.CombatId, new object());
            _eventBus?.Publish(new CombatStartedEvent
            {
                CombatId = initialized.Value.CombatId,
                HeroId = heroOverride.Value.EntityId,
                EnemyIds = overriddenEnemies.Select(enemy => enemy.EntityId).ToList(),
                Target = initialized.Value.CombatId.ToString()
            });
            return initialized;
        }
        catch (Exception exception)
        {
            _logger.LogError($"Error starting scenario combat: {exception.Message}");
            return Result<CombatState>.Failure($"Failed to start scenario combat: {exception.Message}");
        }
    }

    private Result<CombatState> InitializeCombatState(CombatState combatState)
    {
        var initResult = _turnOrderCalculator.InitializeState(combatState);
        if (initResult.IsFailure)
            return Result<CombatState>.Failure(
                $"Failed to initialize turn order calculator: {initResult.Error}");

        combatState = initResult.Value;
        var turnOrderResult = _turnOrderCalculator.Calculate(combatState);
        if (turnOrderResult.IsFailure)
            return Result<CombatState>.Failure(
                $"Failed to calculate turn order: {turnOrderResult.Error}");
        combatState = turnOrderResult.Value.State with { TurnOrder = turnOrderResult.Value.Order };
        _logger.LogDebug($"Initial turn order: {string.Join(", ", turnOrderResult.Value.Order)}");
        return Result<CombatState>.Success(combatState);
    }

    private Result<CombatState> ExecuteActionLocked(Guid combatId, CombatActionCommand command)
    {
        try
        {
            // Obter estado atual
            if (!_activeCombats.TryGetValue(combatId, out var currentState))
                return Result<CombatState>.Failure($"Combat {combatId} not found");

            var hydrated = HydrateStatusEffects(currentState);
            if (hydrated.IsFailure)
                return Result<CombatState>.Failure(hydrated.Error);
            
            if (!currentState.IsActive)
                return Result<CombatState>.Failure($"Combat {combatId} is not active (status: {currentState.Status})");

            if (command.ExpectedStep.HasValue && command.ExpectedStep.Value != currentState.Determinism.Step)
            {
                return Result<CombatState>.Failure(
                    $"Stale combat command: expected step {command.ExpectedStep.Value}, current step is {currentState.Determinism.Step}");
            }

            var actor = currentState.GetEntity(command.ActorId);
            if (actor == null)
                return Result<CombatState>.Failure($"Actor {command.ActorId} not found");

            if (!actor.IsAlive)
                return Result<CombatState>.Failure($"Actor {command.ActorId} is already dead");
            
            // Validar ação
            var validationResult = ValidateAction(currentState, actor, command);
            if (validationResult.IsFailure)
                return Result<CombatState>.Failure(validationResult.Error);
            
            // Executar ação e criar novo estado
            var newState = command.ActionType switch
            {
                ActionType.BASIC_ATTACK => ExecuteConfiguredAction(currentState, actor, ActionType.BASIC_ATTACK, BasicAttackActionId, command.TargetId!, command.CostOptionId, command.RunModifiers, command.IgnoreConfiguredCosts),
                ActionType.POWER => ExecuteConfiguredAction(
                    currentState,
                    actor,
                    ActionType.POWER,
                    command.PowerId!,
                    command.TargetId ?? actor.EntityId,
                    command.CostOptionId,
                    command.RunModifiers,
                    command.IgnoreConfiguredCosts),
                ActionType.PASS => ExecutePass(currentState, actor),
                ActionType.END_TURN => ExecuteEndTurn(currentState, actor, command.DeferTurnLifecycle),
                _ => throw new InvalidOperationException($"Unknown action type: {command.ActionType}")
            };

            var turnState = _turnOrderCalculator.UpdateStateAfterAction(newState, actor.EntityId);
            if (turnState.IsFailure)
                return Result<CombatState>.Failure(turnState.Error);
            newState = turnState.Value;

            if (command.ActionType == ActionType.END_TURN)
            {
                var turnOrder = _turnOrderCalculator.Calculate(newState);
                if (turnOrder.IsFailure)
                    return Result<CombatState>.Failure(
                        $"Failed to calculate turn order: {turnOrder.Error}");
                newState = turnOrder.Value.State with { TurnOrder = turnOrder.Value.Order };
                _logger.LogDebug($"Turn order for turn {newState.CurrentTurn}: {string.Join(", ", turnOrder.Value.Order)}");
            }
            
            // Verificar condições de vitória/derrota
            newState = CheckCombatEnd(newState);
            newState = CaptureStatusEffects(newState);
            
            // Atualizar estado
            _activeCombats[combatId] = newState;
            
            // Publicar evento da ação
            var lastAction = newState.ActionHistory.Last();
            _eventBus?.Publish(new ActionExecutedEvent
            {
                CombatId = combatId,
                ActionId = lastAction.ActionId,
                ActorId = lastAction.ActorId,
                ActionTypeName = lastAction.ActionType.ToString(),
                PowerId = lastAction.PowerId,
                TargetId = lastAction.TargetId,
                Applications = lastAction.Applications,
                Turn = newState.CurrentTurn,
                Subject = lastAction.ActorId,
                Target = lastAction.TargetId ?? "none"
            });
            PublishResourceChanges(newState, lastAction, $"Action: {lastAction.PowerId ?? lastAction.ActionType.ToString()}");
            
            return Result<CombatState>.Success(newState);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error executing action: {ex.Message}");
            return Result<CombatState>.Failure($"Failed to execute action: {ex.Message}");
        }
    }
    
    private Result<bool> ValidateAction(CombatState state, CombatEntity actor, CombatActionCommand command)
    {
        // Verificar se o ator está sob controle (STUN, FREEZE, etc.)
        if (command.ActionType != ActionType.PASS && command.ActionType != ActionType.END_TURN)
        {
            if (_statusEffectManager != null)
            {
                var activeStatusResult = _statusEffectManager.GetActiveStatus(actor.EntityId);
                if (activeStatusResult.IsSuccess)
                {
                    var hasControlEffect = activeStatusResult.Value.Any(s => 
                        s.Definition.Behavior == StatusEffectBehavior.CONTROL);
                    
                    if (hasControlEffect)
                    {
                        return Result<bool>.Failure($"Cannot perform action: actor {actor.EntityId} is under control effect (stunned, frozen, etc.)");
                    }
                }
            }
        }
        
        switch (command.ActionType)
        {
            case ActionType.BASIC_ATTACK:
                if (string.IsNullOrWhiteSpace(command.TargetId))
                    return Result<bool>.Failure("Target is required for basic attack");
                if (state.GetEntity(command.TargetId) == null)
                    return Result<bool>.Failure($"Target {command.TargetId} not found");
                if (!state.GetEntity(command.TargetId)!.IsAlive)
                    return Result<bool>.Failure($"Target {command.TargetId} is already dead");

                var basicActionDefinition = GetConfiguredAction(
                    BasicAttackActionId,
                    state.Determinism.ContentRevision);
                if (basicActionDefinition == null)
                    return Result<bool>.Failure($"Action definition not found: {BasicAttackActionId}");

                var basicAffordabilityError = command.IgnoreConfiguredCosts ? null : ValidateActionCosts(
                    actor,
                    basicActionDefinition.Costs,
                    command.CostOptionId,
                    state.Determinism.ContentRevision);
                if (basicAffordabilityError != null)
                    return Result<bool>.Failure(basicAffordabilityError);
                break;
                
            case ActionType.POWER:
                if (string.IsNullOrWhiteSpace(command.PowerId))
                    return Result<bool>.Failure("Power ID is required");

                var actionDefinition = GetConfiguredAction(
                    command.PowerId,
                    state.Determinism.ContentRevision);
                if (actionDefinition == null)
                    return Result<bool>.Failure($"Action definition not found: {command.PowerId}");

                if (actionDefinition.RequiresTarget && string.IsNullOrWhiteSpace(command.TargetId))
                    return Result<bool>.Failure("Target is required for power");

                var affordabilityError = command.IgnoreConfiguredCosts ? null : ValidateActionCosts(
                    actor,
                    actionDefinition.Costs,
                    command.CostOptionId,
                    state.Determinism.ContentRevision);
                if (affordabilityError != null)
                    return Result<bool>.Failure(affordabilityError);

                var powerTargetId = command.TargetId ?? actor.EntityId;
                if (state.GetEntity(powerTargetId) == null)
                    return Result<bool>.Failure($"Target {powerTargetId} not found");
                if (!state.GetEntity(powerTargetId)!.IsAlive)
                    return Result<bool>.Failure($"Target {powerTargetId} is already dead");
                break;
                
            case ActionType.PASS:
            case ActionType.END_TURN:
                // Sempre válido
                break;
                
            default:
                return Result<bool>.Failure($"Unknown action type: {command.ActionType}");
        }
        
        return Result<bool>.Success(true);
    }

    private Result<CombatEntity> CreateConfiguredCombatEntity(
        string entityId,
        string definitionId,
        bool expectHero,
        string contentRevision)
    {
        if (_entityDefinitionLoader == null)
        {
            return Result<CombatEntity>.Failure(
                "Entity definitions are required to start combat from participant IDs");
        }

        var definition = _entityDefinitionLoader.LoadDefinition(definitionId, contentRevision);
        if (definition.IsFailure)
            return Result<CombatEntity>.Failure(definition.Error);

        var entity = _entityAdapter.CreateCombatEntityFromDefinition(
            entityId,
            definition.Value,
            contentRevision);
        if (entity.IsHero != expectHero)
        {
            return Result<CombatEntity>.Failure(
                $"Entity {entityId} is not a valid {(expectHero ? "hero" : "enemy")} participant");
        }

        return Result<CombatEntity>.Success(entity);
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
    
    private CombatState ExecuteConfiguredAction(
        CombatState state,
        CombatEntity actor,
        ActionType actionType,
        string actionId,
        string targetId,
        string? costOptionId,
        IReadOnlyDictionary<string, float>? runModifiers,
        bool ignoreConfiguredCosts)
    {
        var randomProvider = new DeterministicRandomProvider(state.Determinism);
        var target = state.GetEntity(targetId)!;
        var actionDefinition = GetConfiguredAction(actionId, state.Determinism.ContentRevision)
            ?? throw new InvalidOperationException($"Action definition not found: {actionId}");

        var damageDealt = ApplyRunModifiers(
            CalculateActionDamage(actionDefinition, actor, target, state, randomProvider),
            actionDefinition,
            runModifiers);
        var damageResourceId = ResolveDamageResourceId(actionDefinition);

        // Processar status effects ON_DAMAGE_TAKEN por comportamento configurado.
        var (modifiedDamage, updatedActor) = ProcessOnDamageTakenEffects(target, actor, damageDealt, state.CurrentTurn);
        var newTarget = damageResourceId == null || modifiedDamage <= 0
            ? target
            : ApplyResourceReductionWithDefeatPrevention(target, damageResourceId, modifiedDamage);

        if (!ignoreConfiguredCosts)
        {
            updatedActor = ApplyCosts(
                updatedActor,
                actionDefinition.Costs,
                costOptionId,
                state.Determinism.ContentRevision);
        }
        updatedActor = ApplyActionResourceEffects(
            updatedActor,
            newTarget,
            actionDefinition.Effects,
            state,
            randomProvider);

        var updatedState = state with { Determinism = randomProvider.Context };
        if (updatedActor.EntityId == newTarget.EntityId)
        {
            updatedState = updatedState.ReplaceEntity(updatedActor);
        }
        else
        {
            updatedState = updatedState.ReplaceEntity(updatedActor).ReplaceEntity(newTarget);
        }
        updatedState = ApplyActionSideEffects(
            updatedState,
            actionDefinition.Effects,
            actionId,
            actor.EntityId,
            targetId,
            randomProvider);
        updatedState = updatedState with { Determinism = randomProvider.Context };

        var action = new CombatAction
        {
            Turn = state.CurrentTurn,
            ActorId = actor.EntityId,
            ActionType = actionType,
            PowerId = actionType == ActionType.POWER ? actionId : null,
            TargetId = targetId,
            Applications = DescribeResourceChanges(state, updatedState, actionId)
        };
        return CombatTransitions.AppendAction(updatedState, action).State;
    }

    private CombatState ApplyActionSideEffects(
        CombatState state,
        IEnumerable<EffectDefinition> effects,
        string actionId,
        string actorId,
        string targetId,
        IRandomProvider randomProvider)
    {
        if (_effectResolver == null)
            return state;

        var current = state;
        foreach (var effect in effects.Where(item => item.Type is
                     EffectType.HEAL or
                     EffectType.APPLY_STATUS or
                     EffectType.REMOVE_STATUS or
                     EffectType.DISPEL_STATUS))
        {
            var context = new CombatEffectContext
            {
                CombatState = current,
                SourceEntityId = actorId,
                TargetEntityId = targetId,
                SourceActionId = actionId
            };
            var applied = _effectResolver.ApplyEffect(
                CreateActionEffectInstance(effect, actionId, actorId, targetId),
                context,
                randomProvider);
            if (applied.IsFailure || !applied.Value.Success)
            {
                throw new InvalidOperationException(
                    applied.IsFailure ? applied.Error : applied.Value.ErrorMessage);
            }

            if (effect.Type != EffectType.HEAL ||
                applied.Value.EffectResult.ValueApplied is not { } increase)
                continue;
            var resourceId = applied.Value.EffectResult.ResourceAffected;
            if (string.IsNullOrWhiteSpace(resourceId))
                throw new InvalidOperationException($"HEAL effect {effect.EffectId} did not select a resource");
            foreach (var affectedId in applied.Value.EffectResult.AffectedEntityIds)
            {
                var affected = current.GetEntity(affectedId);
                var resource = affected?.GetResource(resourceId);
                if (affected == null || resource == null)
                {
                    throw new InvalidOperationException(
                        $"Resource {resourceId} not found on effect target {affectedId}");
                }
                current = current.ReplaceEntity(
                    ApplyResourceMutation(
                        affected,
                        $"action-heal:{actionId}:{resourceId}",
                        resourceId,
                        ResourceMutationOperation.Add,
                        increase));
            }
        }
        return current;
    }

    private static string? ResolveDamageResourceId(ActionDefinition definition)
    {
        var damageEffects = definition.Effects
            .Where(effect => effect.Type == EffectType.DAMAGE)
            .ToArray();
        if (damageEffects.Length == 0)
            return null;
        if (damageEffects.Any(effect => string.IsNullOrWhiteSpace(effect.TargetResource)))
        {
            throw new InvalidOperationException(
                $"Action {definition.ActionId} has a DAMAGE effect without targetResource");
        }

        var resourceIds = damageEffects
            .Select(effect => effect.TargetResource!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (resourceIds.Length != 1)
        {
            throw new InvalidOperationException(
                $"Action {definition.ActionId} must use one targetResource for its aggregated DAMAGE effects");
        }
        return resourceIds[0];
    }

    private float CalculateActionDamage(
        ActionDefinition actionDefinition,
        CombatEntity actor,
        CombatEntity target,
        CombatState state,
        IRandomProvider randomProvider)
    {
        var damageEffects = actionDefinition.Effects.Where(e => e.Type == EffectType.DAMAGE).ToList();
        if (damageEffects.Count == 0)
            return 0;

        if (_effectResolver != null)
        {
            return damageEffects.Sum(effect => ResolveActionEffectValue(
                effect,
                actionDefinition.ActionId,
                actor,
                target,
                state,
                randomProvider));
        }

        if (_damageCalculator != null)
        {
            var damageResult = _damageCalculator is IRevisionedDamageCalculator revisionedDamage
                ? revisionedDamage.CalculateDamage(
                    actionDefinition,
                    actor,
                    target,
                    randomProvider,
                    state.Determinism.ContentRevision)
                : _damageCalculator.CalculateDamage(
                    actionDefinition,
                    actor,
                    target,
                    randomProvider);
            _logger.LogDebug($"Action {actionDefinition.ActionId} damage: {damageResult.FinalDamage:F2} (crit tier: {damageResult.CritTier})");
            return damageResult.FinalDamage;
        }

        return damageEffects.Sum(e => e.FlatValue ?? 0);
    }

    private static float ApplyRunModifiers(float baseValue, ActionDefinition actionDefinition, IReadOnlyDictionary<string, float>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0 || baseValue == 0)
            return baseValue;

        var value = baseValue;
        if (modifiers.TryGetValue("added_damage", out var addedDamage))
            value += addedDamage;

        if (modifiers.TryGetValue("increased_damage_total", out var increasedDamage))
            value *= 1 + increasedDamage;

        return actionDefinition.Effects.Any(effect => effect.Type == EffectType.DAMAGE) ? value : baseValue;
    }

    private float ResolveActionEffectValue(
        EffectDefinition effect,
        string actionId,
        CombatEntity actor,
        CombatEntity target,
        CombatState state,
        IRandomProvider randomProvider)
    {
        var instance = CreateActionEffectInstance(effect, actionId, actor.EntityId, target.EntityId);
        var context = new CombatEffectContext
        {
            CombatState = state,
            SourceEntityId = actor.EntityId,
            TargetEntityId = target.EntityId,
            SourceActionId = actionId
        };

        var result = _effectResolver!.ApplyEffect(instance, context, randomProvider);
        if (result.IsSuccess && result.Value.Success && result.Value.EffectResult.ValueApplied.HasValue)
            return result.Value.EffectResult.ValueApplied.Value;

        _logger.LogWarning($"Effect resolver failed for action {actionId}: {(result.IsFailure ? result.Error : result.Value.ErrorMessage)}. Falling back to flat value.");
        return effect.FlatValue ?? 0;
    }

    private CombatEntity ApplyActionResourceEffects(
        CombatEntity actor,
        CombatEntity target,
        IEnumerable<EffectDefinition> effects,
        CombatState state,
        IRandomProvider randomProvider)
    {
        var updatedActor = actor;

        foreach (var effect in effects.Where(e => e.Type == EffectType.MODIFY_RESOURCE))
        {
            var value = ResolveResourceEffectValue(
                effect,
                updatedActor,
                target,
                state,
                randomProvider);
            var resourceId = effect.TargetResource;
            if (string.IsNullOrWhiteSpace(resourceId) || value == 0)
                continue;

            if (effect.Target == EffectTarget.SELF)
            {
                var pool = updatedActor.GetResource(resourceId);
                if (pool != null)
                    updatedActor = ApplyResourceMutation(
                        updatedActor,
                        $"action-resource:{effect.EffectId}:{resourceId}",
                        resourceId,
                        ToMutationOperation(effect.Operation),
                        value,
                        effect.ResourceField);
            }
            else if (effect.Target == EffectTarget.TARGET && target.EntityId == actor.EntityId)
            {
                var pool = updatedActor.GetResource(resourceId);
                if (pool != null)
                    updatedActor = ApplyResourceMutation(
                        updatedActor,
                        $"action-resource:{effect.EffectId}:{resourceId}",
                        resourceId,
                        ToMutationOperation(effect.Operation),
                        value,
                        effect.ResourceField);
            }
        }

        return updatedActor;
    }

    private float ResolveResourceEffectValue(
        EffectDefinition effect,
        CombatEntity actor,
        CombatEntity target,
        CombatState state,
        IRandomProvider randomProvider)
    {
        if (_effectResolver == null)
            return effect.FlatValue ?? 0;

        var instance = CreateActionEffectInstance(effect, string.Empty, actor.EntityId, target.EntityId);
        var context = new CombatEffectContext
        {
            CombatState = state,
            SourceEntityId = actor.EntityId,
            TargetEntityId = target.EntityId
        };

        var result = _effectResolver.ApplyEffect(instance, context, randomProvider);
        if (result.IsSuccess && result.Value.Success && result.Value.EffectResult.ValueApplied.HasValue)
            return result.Value.EffectResult.ValueApplied.Value;

        _logger.LogWarning($"Effect resolver failed for resource effect: {(result.IsFailure ? result.Error : result.Value.ErrorMessage)}. Falling back to flat value.");
        return effect.FlatValue ?? 0;
    }

    private static EffectInstance CreateActionEffectInstance(EffectDefinition effect, string sourceActionId, string actorId, string targetId)
    {
        return new EffectInstance
        {
            Definition = effect,
            SourceEntityId = actorId,
            TargetEntityId = targetId,
            SourceActionId = sourceActionId
        };
    }

    private void PublishResourceChanges(CombatState state, CombatAction action, string reason)
    {
        if (_eventBus == null)
            return;

        foreach (var application in action.Applications)
        {
            if (application.ResourceId is not { } resourceId ||
                application.ResourceField is not { } field ||
                application.PreviousValue is not { } previousValue ||
                application.CurrentValue is not { } currentValue)
                continue;
            _eventBus.Publish(new ResourceChangedEvent
            {
                CombatId = state.CombatId,
                ActionId = action.ActionId,
                OwnerId = application.TargetEntityId,
                ResourceId = resourceId,
                Field = field,
                PreviousValue = previousValue,
                CurrentValue = currentValue,
                Reason = reason,
                Turn = state.CurrentTurn,
                Subject = application.TargetEntityId,
                Target = resourceId
            });

            const string regenerationPrefix = "regeneration:";
            if (application.Provenance.SourceId.StartsWith(
                    regenerationPrefix,
                    StringComparison.Ordinal) &&
                Enum.TryParse<RegenerationTiming>(
                    application.Provenance.SourceId[regenerationPrefix.Length..],
                    out var timing))
            {
                _eventBus.Publish(new ResourceRegeneratedEvent
                {
                    OwnerId = application.TargetEntityId,
                    ResourceId = resourceId,
                    OldValue = previousValue,
                    NewValue = currentValue,
                    Amount = currentValue - previousValue,
                    Timing = timing,
                    Turn = state.CurrentTurn,
                    Subject = application.TargetEntityId,
                    Target = resourceId
                });
            }
        }
    }

    private static IReadOnlyList<EffectApplicationRecord> DescribeResourceChanges(
        CombatState before,
        CombatState after,
        string sourceId)
    {
        var applications = ImmutableArray.CreateBuilder<EffectApplicationRecord>();
        foreach (var beforeEntity in before.GetAllEntities()
                     .OrderBy(entity => entity.EntityId, StringComparer.Ordinal))
        {
            var afterEntity = after.GetEntity(beforeEntity.EntityId);
            if (afterEntity == null)
                continue;

            foreach (var (resourceId, beforePool) in beforeEntity.ResourceState.Resources
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var afterPool = afterEntity.GetResource(resourceId);
                if (afterPool == null)
                    continue;
                AddResourceFieldChange(
                    applications,
                    sourceId,
                    beforeEntity.EntityId,
                    resourceId,
                    ResourceValueField.Current,
                    beforePool.Current,
                    afterPool.Current);
                AddResourceFieldChange(
                    applications,
                    sourceId,
                    beforeEntity.EntityId,
                    resourceId,
                    ResourceValueField.Minimum,
                    beforePool.Minimum,
                    afterPool.Minimum);
                AddResourceFieldChange(
                    applications,
                    sourceId,
                    beforeEntity.EntityId,
                    resourceId,
                    ResourceValueField.Maximum,
                    beforePool.Maximum,
                    afterPool.Maximum);
            }
        }
        return applications.ToImmutable();
    }

    private static void AddResourceFieldChange(
        ImmutableArray<EffectApplicationRecord>.Builder applications,
        string sourceId,
        string ownerId,
        string resourceId,
        ResourceValueField field,
        float previousValue,
        float currentValue)
    {
        if (previousValue.Equals(currentValue))
            return;
        applications.Add(new EffectApplicationRecord
        {
            EffectInstanceId = $"action:{sourceId}:{ownerId}:{resourceId}:{field}",
            EffectType = EffectType.MODIFY_RESOURCE,
            TargetEntityId = ownerId,
            ResourceId = resourceId,
            ResourceField = field,
            PreviousValue = previousValue,
            CurrentValue = currentValue,
            Provenance = new EffectProvenance
            {
                Kind = EffectProvenanceKind.Ability,
                SourceId = sourceId
            }
        });
    }
    
    private CombatState ExecutePass(CombatState state, CombatEntity actor)
    {
        var action = new CombatAction
        {
            Turn = state.CurrentTurn,
            ActorId = actor.EntityId,
            ActionType = ActionType.PASS
        };
        
        return CombatTransitions.AppendAction(state, action).State;
    }
    
    private CombatState ExecuteEndTurn(CombatState state, CombatEntity actor, bool deferTurnLifecycle)
    {
        if (deferTurnLifecycle)
        {
            return CombatTransitions.AppendAction(state, new CombatAction
            {
                Turn = state.CurrentTurn,
                ActorId = actor.EntityId,
                ActionType = ActionType.END_TURN
            }).State;
        }

        var updatedState = state;
        var applications = ImmutableArray.CreateBuilder<EffectApplicationRecord>();
        
        if (_statusEffectManager != null)
        {
            // Fechar integralmente a fronteira atual antes de abrir a próxima.
            var beforeStatus = updatedState;
            updatedState = ProcessEndOfTurnStatusEffects(updatedState);
            applications.AddRange(DescribeResourceChanges(
                beforeStatus,
                updatedState,
                "status:end-turn"));
        }
        var endRegeneration = ProcessRegeneration(updatedState, RegenerationTiming.END_TURN);
        updatedState = endRegeneration.State;
        applications.AddRange(endRegeneration.Applications);

        updatedState = updatedState with { CurrentTurn = state.CurrentTurn + 1 };
        if (_statusEffectManager != null)
        {
            var beforeStatus = updatedState;
            updatedState = ProcessStartOfTurnStatusEffects(updatedState);
            applications.AddRange(DescribeResourceChanges(
                beforeStatus,
                updatedState,
                "status:start-turn"));
        }
        var startRegeneration = ProcessRegeneration(updatedState, RegenerationTiming.START_TURN);
        updatedState = startRegeneration.State;
        applications.AddRange(startRegeneration.Applications);

        return CombatTransitions.AppendAction(updatedState, new CombatAction
        {
            Turn = state.CurrentTurn,
            ActorId = actor.EntityId,
            ActionType = ActionType.END_TURN,
            Applications = applications.ToImmutable()
        }).State;
    }
    
    /// <summary>
    /// Processa status effects no início do turno (Regeneração, Energia, verificar Stun)
    /// </summary>
    private CombatState ProcessStartOfTurnStatusEffects(CombatState state)
    {
        _logger.LogDebug("Processing start-of-turn status effects");
        var statusEffectManager = _statusEffectManager!;
        var updatedHero = state.Hero;
        var updatedEnemies = state.Enemies.ToList();
        
        // Processar status effects do herói
        var heroProcessResult = statusEffectManager.ProcessStatusEffects(
            state.Hero.EntityId,
            StatusEffectTiming.START_OF_TURN,
            state.CurrentTurn);
        if (heroProcessResult.IsSuccess)
        {
            updatedHero = ApplyStatusEffectResults(updatedHero, heroProcessResult.Value.TickResults);
        }
        
        // Processar status effects dos inimigos
        for (int i = 0; i < updatedEnemies.Count; i++)
        {
            var enemy = updatedEnemies[i];
            var processResult = statusEffectManager.ProcessStatusEffects(
                enemy.EntityId,
                StatusEffectTiming.START_OF_TURN,
                state.CurrentTurn);
            if (processResult.IsSuccess)
            {
                updatedEnemies[i] = ApplyStatusEffectResults(enemy, processResult.Value.TickResults);
            }
        }
        
        return state with
        {
            Hero = updatedHero,
            Enemies = updatedEnemies
        };
    }
    
    /// <summary>
    /// Processa status effects no final do turno (DoT, HoT, decrementar durações)
    /// </summary>
    private CombatState ProcessEndOfTurnStatusEffects(CombatState state)
    {
        _logger.LogDebug("Processing end-of-turn status effects");
        var statusEffectManager = _statusEffectManager!;
        var updatedHero = state.Hero;
        var updatedEnemies = state.Enemies.ToList();
        
        // Processar status effects do herói
        var heroProcessResult = statusEffectManager.ProcessStatusEffects(
            state.Hero.EntityId,
            StatusEffectTiming.END_OF_TURN,
            state.CurrentTurn);
        if (heroProcessResult.IsSuccess)
        {
            updatedHero = ApplyStatusEffectResults(updatedHero, heroProcessResult.Value.TickResults);
        }

        // Decrementar durações
        statusEffectManager.TickDurations(state.Hero.EntityId);
        
        // Processar status effects dos inimigos
        for (int i = 0; i < updatedEnemies.Count; i++)
        {
            var enemy = updatedEnemies[i];
            var processResult = statusEffectManager.ProcessStatusEffects(
                enemy.EntityId,
                StatusEffectTiming.END_OF_TURN,
                state.CurrentTurn);
            if (processResult.IsSuccess)
            {
                updatedEnemies[i] = ApplyStatusEffectResults(enemy, processResult.Value.TickResults);
            }

            // Decrementar durações
            statusEffectManager.TickDurations(enemy.EntityId);
        }
        
        return state with
        {
            Hero = updatedHero,
            Enemies = updatedEnemies
        };
    }
    
    /// <summary>
    /// Aplica os resultados de status effects a uma entidade (dano, cura, etc.)
    /// </summary>
    private CombatEntity ApplyStatusEffectResults(
        CombatEntity entity,
        IReadOnlyList<StatusEffectTickResult> results)
    {
        var updatedEntity = entity;
        
        foreach (var result in results)
        {
            if (result.Behavior == StatusEffectBehavior.DAMAGE_OVER_TIME)
            {
                if (result.Value > 0)
                {
                    var resourceId = RequireStatusTargetResource(result);
                    _logger.LogDebug(
                        $"Status effect {result.StatusId} reduced {resourceId} by {result.Value} on {entity.EntityId}");
                    updatedEntity = ApplyResourceReductionWithDefeatPrevention(
                        updatedEntity,
                        resourceId,
                        result.Value);
                }
            }
            else if (result.Behavior == StatusEffectBehavior.HEAL_OVER_TIME)
            {
                if (result.Value > 0)
                {
                    var resourceId = RequireStatusTargetResource(result);
                    _logger.LogDebug(
                        $"Status effect {result.StatusId} increased {resourceId} by {result.Value} on {entity.EntityId}");
                    updatedEntity = ApplyResourceMutation(
                        updatedEntity,
                        $"status:{result.StatusId}:{resourceId}:heal",
                        resourceId,
                        ResourceMutationOperation.Add,
                        result.Value);
                }
            }
        }
        
        return updatedEntity;
    }
    
    /// <summary>
    /// Processa status effects quando uma entidade recebe dano.
    /// Retorna o dano modificado e a entidade atacante atualizada para efeitos reativos.
    /// </summary>
    private (float modifiedDamage, CombatEntity updatedAttacker) ProcessOnDamageTakenEffects(
        CombatEntity target, 
        CombatEntity attacker, 
        float incomingDamage,
        int currentTurn)
    {
        if (_statusEffectManager == null)
        {
            return (incomingDamage, attacker);
        }
        
        var processResult = _statusEffectManager.ProcessStatusEffects(target.EntityId, StatusEffectTiming.ON_DAMAGE_TAKEN, currentTurn);
        if (processResult.IsFailure)
        {
            return (incomingDamage, attacker);
        }
        
        var modifiedDamage = incomingDamage;
        var updatedAttacker = attacker;
        
        foreach (var result in processResult.Value.TickResults)
        {
            if (result.Behavior == StatusEffectBehavior.SHIELD && result.Value > 0)
            {
                var absorbed = System.Math.Min(modifiedDamage, result.Value);
                modifiedDamage -= absorbed;
                _logger.LogDebug($"SHIELD absorbed {absorbed} damage, remaining damage: {modifiedDamage}");
            }
            else if (result.Behavior == StatusEffectBehavior.REACTIVE && result.Value > 0)
            {
                var resourceId = RequireStatusTargetResource(result);
                _logger.LogDebug(
                    $"Reactive status {result.StatusId} reduced {resourceId} by {result.Value} on {attacker.EntityId}");
                updatedAttacker = ApplyResourceReductionWithDefeatPrevention(
                    updatedAttacker,
                    resourceId,
                    result.Value);
            }
            else if (result.Behavior == StatusEffectBehavior.DAMAGE_CAP && result.Value > 0)
            {
                if (modifiedDamage > result.Value)
                {
                    var capped = modifiedDamage - result.Value;
                    modifiedDamage = result.Value;
                    _logger.LogDebug($"Status effect {result.StatusId} capped {capped} damage, damage limited to: {modifiedDamage}");
                }
            }
        }
        
        return (modifiedDamage, updatedAttacker);
    }
    
    /// <summary>
    /// Reduz o recurso selecionado e consulta suas políticas para determinar se
    /// a alteração derrotaria o dono. Nome e categoria não têm semântica implícita.
    /// </summary>
    private CombatEntity ApplyResourceReductionWithDefeatPrevention(
        CombatEntity entity,
        string resourceId,
        float amount)
    {
        var pool = entity.GetResource(resourceId)
            ?? throw new InvalidOperationException(
                $"Resource {resourceId} not found on {entity.EntityId}");
        var reducedEntity = ApplyResourceMutation(
            entity,
            $"resource-reduction:{resourceId}",
            resourceId,
            ResourceMutationOperation.Subtract,
            amount);
        var reducedPool = reducedEntity.GetResource(resourceId)!;
        if (_statusEffectManager == null)
            return reducedEntity;

        var wouldDefeat = ResourceThresholdEvaluator.Evaluate(reducedPool)
            .Any(fact => fact.Consequence == ResourceThresholdConsequence.DefeatOwner);
        
        if (wouldDefeat)
        {
            var activeStatusResult = _statusEffectManager.GetActiveStatus(entity.EntityId);
            if (activeStatusResult.IsSuccess)
            {
                var bufferEffect = activeStatusResult.Value.FirstOrDefault(s => 
                    s.Definition.Behavior == StatusEffectBehavior.DEATH_PREVENTION);
                
                if (bufferEffect != null)
                {
                    var preservedValue = MathF.BitIncrement(pool.Minimum);
                    if (preservedValue <= pool.Maximum)
                    {
                        _logger.LogDebug(
                            $"Status effect {bufferEffect.StatusId} prevented defeat for {entity.EntityId} through resource {resourceId}");
                        _statusEffectManager.RemoveStatus(entity.EntityId, bufferEffect.InstanceId);
                        return ApplyResourceMutation(
                            entity,
                            $"defeat-prevention:{bufferEffect.InstanceId:N}:{resourceId}",
                            resourceId,
                            ResourceMutationOperation.Set,
                            preservedValue);
                    }
                }
            }
        }
        
        return reducedEntity;
    }

    private static CombatEntity ApplyResourceMutation(
        CombatEntity entity,
        string mutationId,
        string resourceId,
        ResourceMutationOperation operation,
        float value,
        ResourceValueField field = ResourceValueField.Current)
    {
        var applied = entity.ApplyResourceMutation(
            mutationId,
            resourceId,
            operation,
            value,
            field);
        return applied.IsSuccess
            ? applied.Value
            : throw new InvalidOperationException(applied.Error);
    }

    private static ResourceMutationOperation ToMutationOperation(ResourceEffectOperation operation) =>
        operation switch
        {
            ResourceEffectOperation.ADD => ResourceMutationOperation.Add,
            ResourceEffectOperation.SUBTRACT => ResourceMutationOperation.Subtract,
            ResourceEffectOperation.SET => ResourceMutationOperation.Set,
            _ => throw new InvalidOperationException($"Unsupported resource operation: {operation}")
        };

    private static string RequireStatusTargetResource(StatusEffectTickResult result) =>
        !string.IsNullOrWhiteSpace(result.TargetResource)
            ? result.TargetResource
            : throw new InvalidOperationException(
                $"Status {result.StatusId} requires targetResource for {result.Behavior}");
    
    /// <summary>
    /// Aplica custos de uma ação ao ator.
    /// Se costOptionId for fornecido, aplica custos da opção alternativa.
    /// Caso contrário, aplica custos normais.
    /// </summary>
    private CombatEntity ApplyCosts(
        CombatEntity actor, 
        ActionCosts costs, 
        string? costOptionId = null,
        string? contentRevision = null)
    {
        var selected = SelectCosts(costs, costOptionId);
        if (selected.IsFailure)
            throw new InvalidOperationException(selected.Error);
        var spent = SpendCosts(actor.ResourceState, selected.Value, contentRevision);
        if (spent.IsFailure)
            throw new InvalidOperationException(spent.Error);
        return actor with { ResourceState = spent.Value.State };
    }

    private ActionDefinition? GetConfiguredAction(string actionId, string contentRevision)
    {
        if (_actionManager == null)
            return null;

        var result = _actionManager is IRevisionedActionCatalog revisioned
            ? revisioned.GetDefinition(actionId, contentRevision)
            : _actionManager.GetDefinition(actionId);
        return result.IsSuccess ? result.Value : null;
    }

    private string? ValidateActionCosts(
        CombatEntity actor,
        ActionCosts costs,
        string? costOptionId,
        string? contentRevision)
    {
        var selected = SelectCosts(costs, costOptionId);
        if (selected.IsFailure)
            return selected.Error;
        var spent = SpendCosts(actor.ResourceState, selected.Value, contentRevision);
        return spent.IsFailure ? spent.Error : null;
    }

    private Result<ResourceSetMutationResult> SpendCosts(
        ResourceSet resources,
        IReadOnlyList<ResourceCost> costs,
        string? contentRevision)
    {
        var resolved = new List<ResolvedResourceCost>(costs.Count);
        foreach (var cost in costs)
        {
            var amount = _actionCostEvaluator == null
                ? IsValidResourceAmount(cost.Amount)
                    ? Result<float>.Success(cost.Amount)
                    : Result<float>.Failure("Resource cost must be finite and non-negative")
                : _actionCostEvaluator.CalculateCost(cost, resources.Resources, contentRevision);
            if (amount.IsFailure)
                return Result<ResourceSetMutationResult>.Failure(amount.Error);
            resolved.Add(new ResolvedResourceCost
            {
                ResourceId = cost.ResourceId,
                Amount = amount.Value,
                AllowOverdraft = cost.AllowOverdraft
            });
        }

        return ResourceCostTransitions.Spend(resources, resolved, "combat-action-cost");
    }

    private static Result<IReadOnlyList<ResourceCost>> SelectCosts(
        ActionCosts costs,
        string? costOptionId)
    {
        if (costs.AlternativeCosts.Count == 0)
            return Result<IReadOnlyList<ResourceCost>>.Success(costs.Costs);
        if (string.IsNullOrWhiteSpace(costOptionId))
            return Result<IReadOnlyList<ResourceCost>>.Failure(
                "Cost option must be specified for this action");
        var option = costs.GetOption(costOptionId);
        return option == null
            ? Result<IReadOnlyList<ResourceCost>>.Failure($"Cost option not found: {costOptionId}")
            : Result<IReadOnlyList<ResourceCost>>.Success(option.Costs);
    }

    private static bool IsValidResourceAmount(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;
    
    private CombatState CheckCombatEnd(CombatState state)
    {
        if (state.AllEnemiesDead)
            return state with { Status = CombatStatus.VICTORY };
        
        if (state.HeroIsDead)
            return state with { Status = CombatStatus.DEFEAT };
        
        return state;
    }

    private Result HydrateStatusEffects(CombatState state)
    {
        if (_statusEffectManager == null)
            return Result.Success();
        // Direct legacy combats may still use the status manager as their
        // session store. Run-owned combats always hydrate from the immutable
        // snapshot, including an explicitly empty status set.
        if (state.RunId == null && state.StatusEffects.Count == 0)
            return Result.Success();
        foreach (var entity in state.GetAllEntities())
        {
            var statuses = state.StatusEffects.TryGetValue(entity.EntityId, out var stored)
                ? stored
                : [];
            var replaced = _statusEffectManager.ReplaceActiveStatus(entity.EntityId, statuses);
            if (replaced.IsFailure)
                return replaced;
        }
        return Result.Success();
    }

    private CombatState CaptureStatusEffects(CombatState state)
    {
        if (_statusEffectManager == null)
            return state;

        var statuses = ImmutableDictionary.CreateBuilder<string, ImmutableArray<StatusEffectInstance>>(
            StringComparer.Ordinal);
        foreach (var entity in state.GetAllEntities())
        {
            var active = _statusEffectManager.GetActiveStatus(entity.EntityId);
            if (active.IsSuccess && active.Value.Count > 0)
            {
                statuses[entity.EntityId] = active.Value
                    .OrderBy(status => status.InstanceId)
                    .ToImmutableArray();
            }
        }
        return state with { StatusEffects = statuses.ToImmutable() };
    }

    private static CombatState ApplyInitialStatusEffects(
        CombatState state,
        IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>? initialStatuses)
    {
        if (initialStatuses == null || initialStatuses.Count == 0)
            return state;

        var statuses = initialStatuses
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToImmutableDictionary(
                item => item.Key,
                item => item.Value
                    .Where(status => status.IsActive)
                    .OrderBy(status => status.InstanceId)
                    .ToImmutableArray(),
                StringComparer.Ordinal);
        return state with { StatusEffects = statuses };
    }

    public Result<CombatState> GetCombatState(Guid combatId)
    {
        if (!_activeCombats.TryGetValue(combatId, out var state))
            return Result<CombatState>.Failure($"Combat {combatId} not found");
        
        return Result<CombatState>.Success(state);
    }

    public Result<CombatState> RestoreCombatState(CombatState state)
    {
        if (state == null)
            return Result<CombatState>.Failure("Combat state is required");
        if (state.CombatId == Guid.Empty)
            return Result<CombatState>.Failure("Combat id is required");
        if (state.Hero == null)
            return Result<CombatState>.Failure("Combat hero is required");

        var combatLock = _combatLocks.GetOrAdd(state.CombatId, _ => new object());
        lock (combatLock)
        {
            if (_statusEffectManager != null)
            {
                lock (_statusSnapshotLock)
                {
                    var hydrated = HydrateStatusEffects(state);
                    if (hydrated.IsFailure)
                        return Result<CombatState>.Failure(hydrated.Error);
                }
            }
            _activeCombats[state.CombatId] = state;
            return Result<CombatState>.Success(state);
        }
    }

    public Result RemoveCombatState(Guid combatId)
    {
        var combatLock = _combatLocks.GetOrAdd(combatId, _ => new object());
        lock (combatLock)
        {
            if (!_activeCombats.TryRemove(combatId, out _))
                return Result.Failure($"Combat {combatId} not found");

            _combatLocks.TryRemove(combatId, out _);
            return Result.Success();
        }
    }

    public Result<CombatState> UpdateCombatState(Guid combatId, Func<CombatState, CombatState> update)
    {
        if (update == null)
            return Result<CombatState>.Failure("Combat state update is required");

        try
        {
            var combatLock = _combatLocks.GetOrAdd(combatId, _ => new object());
            lock (combatLock)
            {
                if (!_activeCombats.TryGetValue(combatId, out var currentState))
                    return Result<CombatState>.Failure($"Combat {combatId} not found");

                var updatedState = update(currentState);
                if (updatedState == null)
                    return Result<CombatState>.Failure("Combat state update returned null");

                updatedState = updatedState with
                {
                    CombatId = currentState.CombatId,
                    StartedAt = currentState.StartedAt,
                    Determinism = currentState.Determinism.AdvanceStep()
                };
                _activeCombats[combatId] = updatedState;
                return Result<CombatState>.Success(updatedState);
            }
        }
        catch (Exception ex)
        {
            return Result<CombatState>.Failure($"Failed to update combat state: {ex.Message}");
        }
    }
    
    public Result<CombatResult> EndCombat(Guid combatId)
    {
        var combatLock = _combatLocks.GetOrAdd(combatId, _ => new object());
        lock (combatLock)
        {
            if (!_activeCombats.TryRemove(combatId, out var state))
                return Result<CombatResult>.Failure($"Combat {combatId} not found");

            var result = new CombatResult
            {
                CombatId = combatId,
                Status = state.Status,
                TotalTurns = state.CurrentTurn,
                TotalActions = state.ActionHistory.Count,
                Duration = state.Determinism.LogicalTimestamp.UtcDateTime - state.StartedAt
            };

            _eventBus?.Publish(new CombatEndedEvent
            {
                CombatId = combatId,
                StatusName = state.Status.ToString(),
                TotalTurns = result.TotalTurns,
                TotalActions = result.TotalActions,
                Duration = result.Duration,
                Target = state.Hero.EntityId
            });

            _combatLocks.TryRemove(combatId, out _);
            _logger.LogInformation($"Combat ended: {combatId} - {state.Status}");
            return Result<CombatResult>.Success(result);
        }
    }
    
    public Result<IReadOnlyList<CombatAction>> GetActionHistory(Guid combatId)
    {
        if (!_activeCombats.TryGetValue(combatId, out var state))
            return Result<IReadOnlyList<CombatAction>>.Failure($"Combat {combatId} not found");
        
        return Result<IReadOnlyList<CombatAction>>.Success(state.ActionHistory);
    }
    
    public bool CombatExists(Guid combatId)
    {
        return _activeCombats.ContainsKey(combatId);
    }
    
    public void ClearInactiveCombats()
    {
        var inactiveCombats = _activeCombats
            .Where(kvp => !kvp.Value.IsActive)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var combatId in inactiveCombats)
        {
            _activeCombats.TryRemove(combatId, out _);
            _combatLocks.TryRemove(combatId, out _);
        }
        
        _logger.LogInformation($"Cleared {inactiveCombats.Count} inactive combats");
    }

    private static ulong CreateSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }
    
    /// <summary>
    /// Processa regeneração de recursos no início do turno
    /// </summary>
    private CombatRegenerationTransition ProcessRegeneration(
        CombatState state,
        RegenerationTiming timing)
    {
        if (_regenerationProcessor == null)
            return new CombatRegenerationTransition(state, []);

        _logger.LogDebug($"Processing resource regeneration at {timing}");
        var context = BuildRegenerationContext(state);
        var current = state;
        var applications = ImmutableArray.CreateBuilder<EffectApplicationRecord>();
        foreach (var entity in state.GetAllEntities()
                     .OrderBy(entity => entity.EntityId, StringComparer.Ordinal))
        {
            var result = _regenerationProcessor.ProcessRegeneration(
                entity.ResourceState,
                timing,
                context);
            if (result.IsFailure)
                throw new InvalidOperationException(result.Error);
            current = current.ReplaceEntity(entity with { ResourceState = result.Value.State });
            foreach (var record in result.Value.Records)
            {
                if (record.PreviousValue.Equals(record.CurrentValue))
                    continue;
                applications.Add(new EffectApplicationRecord
                {
                    EffectInstanceId = $"{record.MutationId}:{entity.EntityId}",
                    EffectType = EffectType.MODIFY_RESOURCE,
                    TargetEntityId = entity.EntityId,
                    ResourceId = record.ResourceId,
                    ResourceField = record.Field,
                    ResourceOperation = record.Operation,
                    PreviousValue = record.PreviousValue,
                    CurrentValue = record.CurrentValue,
                    Provenance = new EffectProvenance
                    {
                        Kind = EffectProvenanceKind.Rule,
                        SourceId = $"regeneration:{timing}"
                    }
                });
            }
        }
        return new CombatRegenerationTransition(current, applications.ToImmutable());
    }

    private static ResourceRegenerationContext BuildRegenerationContext(CombatState state)
    {
        var variables = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["turn"] = state.CurrentTurn
        };
        foreach (var entity in state.GetAllEntities()
                     .OrderBy(entity => entity.EntityId, StringComparer.Ordinal))
        {
            ResourceFormulaVariables.AddOwner(
                variables,
                $"actors.{entity.EntityId}",
                entity.ResourceState);
        }
        return new ResourceRegenerationContext
        {
            ContentRevision = state.Determinism.ContentRevision,
            Variables = variables
        };
    }

    private sealed record CombatRegenerationTransition(
        CombatState State,
        IReadOnlyList<EffectApplicationRecord> Applications);
}
