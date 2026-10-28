using System.Collections.Immutable;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Effects;
using Core.Run;
using Core.StatusEffects;

namespace Core.GameplayEvents.Responses;

/// <summary>Migration adapters only declare captured subscriptions. Matching, ordering and effect execution remain shared.</summary>
public static class GameplayBoundarySources
{
    public static string Kind(string boundary) => boundary switch
    {
        "CombatStart" => "combat.started", "CombatEnd" => "combat.ended",
        "StartActivation" => "activation.started", "EndActivation" => "activation.ended",
        "StartRound" => "round.started", "EndRound" => "round.ended", _ => "boundary.reached"
    };

    public static ImmutableArray<GameplayBoundaryInvocation> Status(RunState run, CombatState combat,
        IReadOnlyList<StatusEffectInstance> captured)
    {
        var result = ImmutableArray.CreateBuilder<GameplayBoundaryInvocation>();
        foreach (var status in captured)
        foreach (var trigger in status.Definition.Triggers)
        {
            if (result.Count > HierarchySelectorIndex.MaximumRegistrations) return result.ToImmutable();
            var revision = status.ContentRevision ?? combat.Determinism.ContentRevision;
            result.Add(new()
            {
                Kind = Kind(trigger.Boundary), BoundaryId = trigger.Boundary, StatusInstanceId = status.InstanceId,
                Order = new() { ContainerPriority = -(long)status.Definition.Priority, ContainerInstanceId = status.InstanceId.ToString("D") },
                Origin = new() { ContainerKind = "status", ContainerId = status.InstanceId.ToString("D"), ComponentId = trigger.TriggerId, ContentRevision = revision },
                Template = new() { Run = run, Combat = combat, Trigger = trigger, OwnerEntityId = status.TargetId,
                    SourceEntityId = status.SourceId ?? status.TargetId, ContentRevision = revision,
                    Provenance = new() { Kind = EffectProvenanceKind.Status, SourceId = status.InstanceId.ToString("D") } }
            });
        }
        return result.ToImmutable();
    }

    public static ImmutableArray<GameplayBoundaryInvocation> Relics(RunState run, CombatState combat)
    {
        var result = ImmutableArray.CreateBuilder<GameplayBoundaryInvocation>();
        foreach (var relic in run.Relics)
        foreach (var trigger in relic.Triggers)
        foreach (var owner in combat.GetAllActors().Where(actor => relic.Owner.Includes(actor, combat, run)))
        {
            if (result.Count > HierarchySelectorIndex.MaximumRegistrations) return result.ToImmutable();
            var activeOnly = trigger.Boundary is "StartActivation" or "EndActivation";
            if (activeOnly && owner.InstanceId != combat.ActivationState?.ActiveActorId) continue;
            result.Add(new()
            {
                Kind = Kind(trigger.Boundary), BoundaryId = trigger.Boundary, RequiredOwner = relic.Owner, RequireActiveOwner = activeOnly,
                Order = new() { ContainerInstanceId = relic.RelicInstanceId.ToString("D") },
                Origin = new() { ContainerKind = "relic", ContainerId = relic.RelicInstanceId.ToString("D"), ComponentId = trigger.TriggerId, ContentRevision = relic.ContentRevision },
                Template = new() { Run = run, Combat = combat, Trigger = trigger, OwnerEntityId = owner.InstanceId, SourceEntityId = owner.InstanceId,
                    ContentRevision = relic.ContentRevision, Variables = new Dictionary<string, float>(StringComparer.Ordinal) { ["stacks"] = relic.Stacks },
                    Provenance = new() { Kind = EffectProvenanceKind.Relic, SourceId = relic.RelicInstanceId.ToString("D") } }
            });
        }
        return result.ToImmutable();
    }

    public static GameplayBoundaryInvocation Phase(EffectTriggerExecutionRequest request, string phaseId, bool entering) => new()
    {
        Template = request, Kind = entering ? "phase.entered" : "phase.exited", BoundaryId = phaseId,
        Origin = new() { ContainerKind = "rule", ContainerId = request.Provenance.SourceId,
            ComponentId = request.Trigger.TriggerId, ContentRevision = request.ContentRevision }
    };
}
