using Core.Logging;

namespace Core.Effects.Handlers;

public sealed class MetadataEffectHandler : IEffectHandler
{
    private static readonly IReadOnlySet<EffectType> Types = new HashSet<EffectType>
    {
        EffectType.MODIFY_DAMAGE_DEALT,
        EffectType.MODIFY_DAMAGE_TAKEN,
        EffectType.MODIFY_CRIT_CHANCE,
        EffectType.MODIFY_CRIT_MULT,
        EffectType.MODIFY_COOLDOWNS,
        EffectType.PREVENT_ACTIONS,
        EffectType.FORCE_TARGET,
        EffectType.SKIP_TURN,
        EffectType.REFLECT_DAMAGE,
        EffectType.ABSORB_DAMAGE
    };

    private readonly ILogger _logger;

    public MetadataEffectHandler(ILogger logger) => _logger = logger;

    public IReadOnlySet<EffectType> SupportedTypes => Types;

    public EffectResult Execute(EffectExecutionRequest request) =>
        request.Effect.Definition.Type switch
        {
            EffectType.MODIFY_DAMAGE_DEALT => Modifier(request, "damage_dealt"),
            EffectType.MODIFY_DAMAGE_TAKEN => Modifier(request, "damage_taken"),
            EffectType.MODIFY_CRIT_CHANCE => Modifier(request, "crit_chance"),
            EffectType.MODIFY_CRIT_MULT => Modifier(request, "crit_mult"),
            EffectType.MODIFY_COOLDOWNS => Modifier(request, "cooldowns"),
            _ => Control(request)
        };

    private EffectResult Modifier(EffectExecutionRequest request, string key)
    {
        var value = request.Effect.Definition.FlatValue
            ?? request.Effect.Definition.ModifierValue
            ?? 0f;
        _logger.LogDebug($"Executing modifier effect: {key} = {value} for {request.TargetId}");
        return EffectResult.CreateSuccess() with
        {
            ValueApplied = value,
            ResourceAffected = key,
            AffectedEntityIds = new List<string> { request.TargetId },
            Metadata = new Dictionary<string, object>
            {
                ["modifierKey"] = key,
                ["modifierValue"] = value,
                ["stateApplied"] = false
            }
        };
    }

    private EffectResult Control(EffectExecutionRequest request)
    {
        if (request.Context.CombatState == null)
        {
            return EffectResult.CreateFailure(
                $"{request.Effect.Definition.Type} effect requires combat context");
        }

        var type = request.Effect.Definition.Type.ToString();
        _logger.LogDebug($"Executing control effect: {type} on {request.TargetId}");
        return EffectResult.CreateSuccess() with
        {
            AffectedEntityIds = new List<string> { request.TargetId },
            Metadata = new Dictionary<string, object>
            {
                ["controlEffect"] = type,
                ["stateApplied"] = false
            }
        };
    }
}
