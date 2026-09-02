using Core.Combat.Models;
using Core.Damage;
using Core.Logging;

namespace Core.Effects.Handlers;

public sealed class CombatEffectHandler : IEffectHandler
{
    private static readonly IReadOnlySet<EffectType> Types = new HashSet<EffectType>
    {
        EffectType.DAMAGE,
        EffectType.HEAL,
        EffectType.MODIFY_RESOURCE
    };

    private readonly IDamageCalculator _damageCalculator;
    private readonly EffectFormulaEvaluator _values;
    private readonly ILogger _logger;

    public CombatEffectHandler(
        IDamageCalculator damageCalculator,
        EffectFormulaEvaluator values,
        ILogger logger)
    {
        _damageCalculator = damageCalculator;
        _values = values;
        _logger = logger;
    }

    public IReadOnlySet<EffectType> SupportedTypes => Types;

    public EffectResult Execute(EffectExecutionRequest request) =>
        request.Effect.Definition.Type switch
        {
            EffectType.DAMAGE => ExecuteDamage(request),
            EffectType.HEAL => ExecuteValue(request),
            EffectType.MODIFY_RESOURCE => ExecuteValue(request),
            _ => EffectResult.CreateFailure("Unsupported combat effect")
        };

    private EffectResult ExecuteDamage(EffectExecutionRequest request)
    {
        if (request.Context.CombatState == null)
            return EffectResult.CreateFailure("DAMAGE effect requires combat context");

        var effect = request.Effect;
        var value = _values.Calculate(effect, request.TargetId, request.Context);
        var resourceId = effect.Definition.TargetResource;
        if (string.IsNullOrWhiteSpace(resourceId))
            return EffectResult.CreateFailure("DAMAGE effect requires targetResource");
        var source = request.Context.CombatState.GetEntity(effect.SourceEntityId);
        var target = request.Context.CombatState.GetEntity(request.TargetId);

        if (source != null && target != null)
        {
            var action = new ActionDefinition
            {
                ActionId = effect.SourceActionId ?? effect.Definition.EffectId,
                Effects = new[] { effect.Definition with { FlatValue = value, FormulaValue = null } },
                Tags = effect.Definition.Tags
            };
            var damage = request.UseExplicitDamageRandom
                ? _damageCalculator.CalculateDamage(action, source, target, request.RandomProvider)
                : _damageCalculator.CalculateDamage(action, source, target);
            value = damage.FinalDamage;
        }

        _logger.LogDebug($"Executing DAMAGE effect: {value} to {resourceId} on {request.TargetId}");
        return EffectResult.CreateSuccess(value, resourceId) with
        {
            AffectedEntityIds = new List<string> { request.TargetId }
        };
    }

    private EffectResult ExecuteValue(EffectExecutionRequest request)
    {
        var value = _values.Calculate(request.Effect, request.TargetId, request.Context);
        var resourceId = request.Effect.Definition.TargetResource;
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return EffectResult.CreateFailure(
                $"{request.Effect.Definition.Type} effect requires targetResource");
        }
        _logger.LogDebug(
            $"Executing {request.Effect.Definition.Type} effect: {value} to {resourceId} on {request.TargetId}");
        return EffectResult.CreateSuccess(value, resourceId) with
        {
            AffectedEntityIds = new List<string> { request.TargetId }
        };
    }
}
