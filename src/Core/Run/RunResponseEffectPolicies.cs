using Core.Common;
using Core.Effects;

namespace Core.Run;

/// <summary>Rejects state that cannot be projected into a persistent actor. No transient combat state is silently discarded.</summary>
internal static class RunResponseEffectPolicies
{
    internal static Result Validate(IReadOnlyList<EffectDefinition> definitions)
    {
        var invalid = false;
        var errors = EffectDefinitionValidator.Validate(definitions, (effect, _) => invalid |= effect.Chance != 1 ||
            effect.Target is not (EffectTarget.SELF or EffectTarget.TARGET) ||
            effect.Type is EffectType.APPLY_STATUS or EffectType.REMOVE_STATUS or EffectType.DISPEL_STATUS or EffectType.TURN_SEQUENCE ||
            effect.Type == EffectType.MODIFY_ATTRIBUTE && effect.AttributeMutation?.Lifetime != Core.Entity.AttributeLifetime.RunBase);
        return invalid || !errors.IsEmpty ? Result.Failure("Persistent responses require guaranteed effects with persistable targets and state") : Result.Success();
    }
}
