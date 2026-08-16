using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Damage;
using Core.Effects.Handlers;
using Core.Events;
using Core.Events.Domain;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.StatusEffects;

namespace Core.Effects;

/// <summary>
/// Orquestra o ciclo de um efeito. Regras puras e integrações por domínio vivem
/// em componentes separados; esta classe só coordena validação, chance, alvos,
/// repetição, encadeamento e publicação dos resultados.
/// </summary>
public sealed class EffectResolver : IEffectResolver
{
    private readonly IEventBus _eventBus;
    private readonly ILogger _logger;
    private readonly IRandomProvider _legacyRandomProvider;
    private readonly EffectFormulaEvaluator _formulas;
    private readonly EffectHandlerDispatcher _handlers;
    private readonly object _modifiersLock = new();
    private ImmutableDictionary<string, ImmutableList<EffectModifier>> _activeModifiers =
        ImmutableDictionary<string, ImmutableList<EffectModifier>>.Empty
            .WithComparers(StringComparer.Ordinal);

    public EffectResolver(
        IDamageCalculator damageCalculator,
        IResourceManager resourceManager,
        IEventBus eventBus,
        ILogger logger,
        IRuntimeFormulaEvaluator formulaEvaluator,
        IRandomProvider? randomProvider = null,
        IStatusEffectManager? statusEffectManager = null,
        IRunManager? runManager = null)
    {
        ArgumentNullException.ThrowIfNull(damageCalculator);
        ArgumentNullException.ThrowIfNull(resourceManager);
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _legacyRandomProvider = randomProvider ?? new DefaultRandomProvider();
        _formulas = new EffectFormulaEvaluator(formulaEvaluator, logger);
        _handlers = new EffectHandlerDispatcher(new IEffectHandler[]
        {
            new CombatEffectHandler(damageCalculator, _formulas, logger),
            new StatusEffectHandler(statusEffectManager, logger),
            new RunEffectHandler(runManager, _formulas, logger),
            new MetadataEffectHandler(logger)
        });
    }

    public Result<EffectResult> ResolveEffect(EffectInstance effect, CombatState state)
    {
        var result = ApplyEffect(effect, CombatEffectContext.FromEffect(effect, state));
        return result.IsSuccess
            ? Result<EffectResult>.Success(result.Value.EffectResult)
            : Result<EffectResult>.Failure(result.Error);
    }

    public Result<EffectApplicationResult> ApplyEffect(
        EffectInstance effect,
        IEffectContext context) =>
        ApplyEffectCore(effect, context, _legacyRandomProvider, useExplicitDamageRandom: false);

    public Result<EffectApplicationResult> ApplyEffect(
        EffectInstance effect,
        IEffectContext context,
        IRandomProvider randomProvider) =>
        ApplyEffectCore(effect, context, randomProvider, useExplicitDamageRandom: true);

    private Result<EffectApplicationResult> ApplyEffectCore(
        EffectInstance effect,
        IEffectContext context,
        IRandomProvider randomProvider,
        bool useExplicitDamageRandom)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(effect);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(randomProvider);

            effect = MaterializeEffect(effect, randomProvider, "effect");
            var canExecute = EffectRules.CanExecute(effect, context);
            if (canExecute.IsFailure)
                return Result<EffectApplicationResult>.Failure(canExecute.Error);

            effect = effect.MarkAsExecuting();
            if (effect.Definition.Condition != null
                && !_formulas.EvaluateCondition(effect.Definition.Condition, effect, context))
            {
                return ApplicationResult(
                    context,
                    EffectResult.CreateFailure("Condition not met"));
            }

            if (effect.Definition.Chance < 1f)
            {
                var roll = (float)randomProvider.NextDouble();
                if (roll > effect.Definition.Chance)
                {
                    return ApplicationResult(
                        context,
                        EffectResult.CreateFailure("Probability check failed"));
                }
            }

            var targets = EffectTargetResolver.Resolve(
                effect.Definition.Target,
                effect.SourceEntityId,
                effect.TargetEntityId,
                context,
                randomProvider);
            if (targets.Count == 0)
                return ApplicationResult(context, EffectResult.CreateFailure("No valid targets"));

            var results = new List<EffectResult>();
            for (var repeat = 0; repeat < System.Math.Max(1, effect.Definition.Repeat); repeat++)
            {
                foreach (var targetId in targets)
                {
                    results.Add(ExecuteOnTarget(
                        effect,
                        targetId,
                        context,
                        randomProvider,
                        useExplicitDamageRandom));
                }
            }

            var aggregate = EffectResultAggregator.Aggregate(results);
            if (effect.Definition.ChainedEffects is { Count: > 0 } && aggregate.Success)
            {
                var chained = new List<EffectInstance>();
                foreach (var definition in effect.Definition.ChainedEffects)
                {
                    var instance = MaterializeEffect(
                        CreateEffectInstance(
                            definition,
                            effect.SourceEntityId,
                            effect.TargetEntityId),
                        randomProvider,
                        "effect");
                    chained.Add(instance);
                    ApplyEffectCore(
                        instance,
                        context,
                        randomProvider,
                        useExplicitDamageRandom);
                }

                aggregate = aggregate with
                {
                    ChainedEffects = aggregate.ChainedEffects.Concat(chained).ToList()
                };
                _eventBus.Publish(new EffectChainedEvent
                {
                    ParentEffectId = effect.InstanceId,
                    ChainedEffectIds = chained.Select(item => item.InstanceId).ToList()
                });
            }

            _eventBus.Publish(new EffectExecutedEvent
            {
                EffectInstanceId = effect.InstanceId,
                EffectType = effect.Definition.Type,
                SourceEntityId = effect.SourceEntityId,
                TargetEntityId = effect.TargetEntityId,
                ValueApplied = aggregate.ValueApplied,
                Success = aggregate.Success,
                Metadata = aggregate.Metadata
            });
            return ApplicationResult(context, aggregate);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                $"Error resolving effect {effect?.InstanceId}: {exception.Message}",
                exception);
            return Result<EffectApplicationResult>.Failure(
                $"Error resolving effect: {exception.Message}");
        }
    }

    private EffectResult ExecuteOnTarget(
        EffectInstance effect,
        string targetId,
        IEffectContext context,
        IRandomProvider randomProvider,
        bool useExplicitDamageRandom)
    {
        try
        {
            return _handlers.Execute(new EffectExecutionRequest(
                effect,
                targetId,
                context,
                randomProvider,
                useExplicitDamageRandom));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                $"Error executing effect {effect.InstanceId} on target {targetId}: {exception.Message}",
                exception);
            return EffectResult.CreateFailure($"Execution error: {exception.Message}");
        }
    }

    private static Result<EffectApplicationResult> ApplicationResult(
        IEffectContext context,
        EffectResult result) =>
        Result<EffectApplicationResult>.Success(
            EffectApplicationResult.FromEffectResult(
                context.Scope,
                result,
                context.CombatState));

    public Result<List<EffectResult>> ResolveEffects(
        List<EffectInstance> effects,
        CombatState state)
    {
        var results = effects
            .Select(effect => ResolveEffect(effect, state))
            .Where(result => result.IsSuccess)
            .Select(result => result.Value)
            .ToList();
        return Result<List<EffectResult>>.Success(results);
    }

    public Result<List<EffectApplicationResult>> ApplyEffects(
        List<EffectInstance> effects,
        IEffectContext context)
    {
        var results = effects
            .Select(effect => ApplyEffect(effect, context))
            .Where(result => result.IsSuccess)
            .Select(result => result.Value)
            .ToList();
        return Result<List<EffectApplicationResult>>.Success(results);
    }

    public EffectDefinition ApplyModifiers(
        EffectDefinition definition,
        List<EffectModifier> modifiers) =>
        EffectModifierTransformer.Apply(definition, modifiers);

    public EffectInstance CreateEffectInstance(
        EffectDefinition definition,
        string sourceEntityId,
        string targetEntityId,
        List<EffectModifier>? modifiers = null)
    {
        var appliedModifiers = modifiers ?? [];
        return new EffectInstance
        {
            Definition = EffectModifierTransformer.Apply(definition, appliedModifiers),
            SourceEntityId = sourceEntityId,
            TargetEntityId = targetEntityId,
            AppliedModifiers = appliedModifiers
        };
    }

    public Result<bool> CanExecuteEffect(EffectInstance effect, CombatState state) =>
        EffectRules.CanExecute(effect, CombatEffectContext.FromEffect(effect, state));

    public Result<bool> CanExecuteEffect(EffectInstance effect, IEffectContext context) =>
        EffectRules.CanExecute(effect, context);

    public Result<bool> ValidateDefinition(EffectDefinition definition) =>
        EffectRules.Validate(definition);

    public List<EffectModifier> GetActiveModifiers(
        string entityId,
        EffectType? filterType = null)
    {
        lock (_modifiersLock)
        {
            return _activeModifiers.TryGetValue(entityId, out var modifiers)
                ? modifiers.ToList()
                : [];
        }
    }

    public void RegisterModifier(string entityId, EffectModifier modifier)
    {
        lock (_modifiersLock)
        {
            var modifiers = _activeModifiers.GetValueOrDefault(entityId, []);
            _activeModifiers = _activeModifiers.SetItem(entityId, modifiers.Add(modifier));
        }
    }

    public void UnregisterModifier(string entityId, string modifierId)
    {
        lock (_modifiersLock)
        {
            if (!_activeModifiers.TryGetValue(entityId, out var modifiers))
                return;
            _activeModifiers = _activeModifiers.SetItem(
                entityId,
                modifiers.RemoveAll(modifier => modifier.ModifierId == modifierId));
        }
    }

    private static EffectInstance MaterializeEffect(
        EffectInstance effect,
        IRandomProvider randomProvider,
        string scope)
    {
        if (randomProvider is not DeterministicRandomProvider deterministic)
            return effect;

        var instanceId = string.IsNullOrWhiteSpace(effect.InstanceId)
            ? deterministic.AllocateId(scope).ToString("D")
            : effect.InstanceId;
        var createdAt = effect.CreatedAt == DateTime.UnixEpoch
            ? deterministic.LogicalTimestamp
            : effect.CreatedAt;
        return effect with { InstanceId = instanceId, CreatedAt = createdAt };
    }
}
