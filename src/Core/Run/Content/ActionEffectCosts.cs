using System.Collections.Immutable;
using Core.Effects;

namespace Core.Run.Content;

/// <summary>Already-evaluated costs become ordinary resource mutations before authored effects.</summary>
public static class ActionEffectCosts
{
    public static ImmutableArray<ResolvedEffectCommand> Compile(CardPlayEvaluation evaluation,
        string actorId, EffectProvenance origin) => evaluation.Costs.Select(cost => new ResolvedEffectCommand
    {
        EffectInstanceId = $"{origin.SourceId}:cost:{cost.ComponentId}:{cost.OptionId ?? "base"}:{cost.ResourceId}",
        Definition = new()
        {
            EffectId = $"cost:{cost.ComponentId}", Type = EffectType.MODIFY_RESOURCE,
            TargetResource = cost.ResourceId, Operation = ResourceEffectOperation.SUBTRACT
        },
        SourceEntityId = actorId, TargetEntityIds = [actorId], ResolvedValue = cost.Amount,
        Provenance = origin with { ComponentId = cost.ComponentId }
    }).ToImmutableArray();
}
