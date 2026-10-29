using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Run;

namespace Core.GameplayEvents.Responses;

/// <summary>Immutable view of a trusted run-resource adapter. Generic responses use the actual persistent actor;
/// the original activity keeps its captured numeric operand and historical invocation path.</summary>
internal sealed record RunWalletResponseView(RunState Run, CombatState Combat, string ActorId, string ResourceComponentId)
{
    internal static RunWalletResponseView? Create(RunState run, CombatState combat, GameplayEffectObservation observation)
    {
        if (!observation.RequirePersistentResponses || run.PlayerEntity is not { } player ||
            !observation.ResourceOwnerBindings.TryGetValue(player.InstanceId, out var owner) || owner.Kind != "run" ||
            owner.Id != run.ResourceState.OwnerId || combat.GetActor(player.InstanceId) is not { } adapter ||
            adapter.Component<ResourceEntityComponentState>() is not { } resource)
            return null;
        // ReplaceActor retains the captured action identity; only the resource/components view changes.
        var actor = adapter with { Components = player.Components };
        return new(run with { ResourceState = adapter.ResourceState }, combat.ReplaceActor(actor), player.InstanceId, resource.ComponentId);
    }

    internal GameplayObserverCollectionContext Apply(GameplayObserverCollectionContext context) => context with { Run = Run, Combat = Combat };

    internal (RunState Run, CombatState Combat) Restore(RunState run, CombatState combat)
    {
        var actual = combat.GetActor(ActorId)!;
        var restoredRun = run with { PlayerEntity = run.PlayerEntity! with { Components = actual.Components } };
        var components = actual.Components.ToImmutableDictionary(StringComparer.Ordinal).SetItem(ResourceComponentId,
            new ResourceEntityComponentState { ComponentId = ResourceComponentId, State = restoredRun.ResourceState });
        return (restoredRun, combat.ReplaceActor(actual with { Components = components }));
    }
}
