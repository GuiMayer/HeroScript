using System.Collections.Immutable;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Effects;
using Core.Common;
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

    public static Result<ImmutableArray<GameplayBoundaryInvocation>> Status(RunState run, CombatState combat,
        IReadOnlyList<StatusEffectInstance> captured)
    {
        var result = ImmutableArray.CreateBuilder<GameplayBoundaryInvocation>();
        foreach (var status in captured)
        {
            var declared = GameplayContainerBoundaryResponses.Triggers(status.Definition.Triggers, status.Definition.Responses);
            if (declared.IsFailure) return Result<ImmutableArray<GameplayBoundaryInvocation>>.Failure(declared.Error);
            foreach (var (trigger, response) in declared.Value)
            {
                if (result.Count > HierarchySelectorIndex.MaximumRegistrations) return Result<ImmutableArray<GameplayBoundaryInvocation>>.Failure("Container response registration budget exceeded");
                var revision = status.ContentRevision ?? combat.Determinism.ContentRevision;
                result.Add(new()
                {
                    Response = response, Stacks = status.Stacks, Duration = status.Duration,
                    Kind = Kind(trigger.Boundary), BoundaryId = trigger.Boundary, StatusInstanceId = status.InstanceId,
                    Order = new() { ContainerPriority = -(long)status.Definition.Priority, ContainerInstanceId = status.InstanceId.ToString("D") },
                    Origin = new() { ContainerKind = "status", ContainerId = status.InstanceId.ToString("D"), ComponentId = trigger.TriggerId, ContentRevision = revision },
                    Template = new() { Run = run, Combat = combat, Trigger = trigger, OwnerEntityId = status.TargetId,
                        SourceEntityId = status.SourceId ?? status.TargetId, ContentRevision = revision,
                        Provenance = new() { Kind = EffectProvenanceKind.Status, SourceId = status.InstanceId.ToString("D") } }
                });
            }
        }
        return Result<ImmutableArray<GameplayBoundaryInvocation>>.Success(result.ToImmutable());
    }

    public static Result<ImmutableArray<GameplayBoundaryInvocation>> Relics(RunState run, CombatState combat)
    {
        var result = ImmutableArray.CreateBuilder<GameplayBoundaryInvocation>();
        foreach (var relic in run.Relics)
        {
            var declared = GameplayContainerBoundaryResponses.Triggers(relic.Triggers, relic.Responses, "relic");
            if (declared.IsFailure) return Result<ImmutableArray<GameplayBoundaryInvocation>>.Failure(declared.Error);
            foreach (var (trigger, response) in declared.Value)
            foreach (var owner in combat.GetAllActors().Where(actor => relic.Owner.Includes(actor, combat, run)))
            {
                if (result.Count > HierarchySelectorIndex.MaximumRegistrations) return Result<ImmutableArray<GameplayBoundaryInvocation>>.Failure("Container response registration budget exceeded");
                var activeOnly = trigger.Boundary is "StartActivation" or "EndActivation";
                if (activeOnly && owner.InstanceId != combat.ActivationState?.ActiveActorId) continue;
                result.Add(new()
                {
                    Response = response, Stacks = relic.Stacks,
                    Kind = Kind(trigger.Boundary), BoundaryId = trigger.Boundary, RequiredOwner = relic.Owner, RequireActiveOwner = activeOnly,
                    Order = new() { ContainerInstanceId = relic.RelicInstanceId.ToString("D") },
                    Origin = new() { ContainerKind = "relic", ContainerId = relic.RelicInstanceId.ToString("D"), ComponentId = trigger.TriggerId, ContentRevision = relic.ContentRevision },
                    Template = new() { Run = run, Combat = combat, Trigger = trigger, OwnerEntityId = owner.InstanceId, SourceEntityId = owner.InstanceId,
                        ContentRevision = relic.ContentRevision, Variables = new Dictionary<string, float>(StringComparer.Ordinal) { ["stacks"] = relic.Stacks },
                        Provenance = new() { Kind = EffectProvenanceKind.Relic, SourceId = relic.RelicInstanceId.ToString("D") } }
                });
            }
        }
        return Result<ImmutableArray<GameplayBoundaryInvocation>>.Success(result.ToImmutable());
    }

    public static Result<ImmutableArray<GameplayBoundaryInvocation>> Inscriptions(RunState run, CombatState combat,
        Core.Content.IContentRuntimeResolver? runtimes)
    {
        if (runtimes == null || run.Loadouts is not { } loadouts || loadouts.Actors.Count == 0)
            return Result<ImmutableArray<GameplayBoundaryInvocation>>.Success([]);

        var result = ImmutableArray.CreateBuilder<GameplayBoundaryInvocation>();
        foreach (var (actorId, actorLoadout) in loadouts.Actors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (combat.GetActor(actorId) is not { } owner || !loadouts.Inventories.TryGetValue(loadouts.InventoryId(run.RunId, actorId), out var inventory))
                continue;

            foreach (var (_, instanceId) in actorLoadout.Assignments.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var owned = inventory.Items.FirstOrDefault(item => item.InstanceId == instanceId);
                if (owned == null) continue;

                var acquired = runtimes.Resolve(owned.EffectiveRevision, run.ConfigName);
                if (acquired.IsFailure) return Result<ImmutableArray<GameplayBoundaryInvocation>>.Failure(acquired.Error);
                var definition = acquired.Value.GetDefinition<Core.Run.Loadouts.InscriptionDefinition>("inscriptions", owned.DefinitionId);
                if (definition.IsFailure) return Result<ImmutableArray<GameplayBoundaryInvocation>>.Failure(definition.Error);
                if (definition.Value.Responses is not { } responses || responses.IsDefaultOrEmpty) continue;

                var declared = GameplayContainerBoundaryResponses.Triggers([], responses, "inscription");
                if (declared.IsFailure) return Result<ImmutableArray<GameplayBoundaryInvocation>>.Failure(declared.Error);

                var requiredOwner = new GameplayOwner { Kind = GameplayOwnerKind.Entity, Id = actorId };
                foreach (var (trigger, response) in declared.Value)
                {
                    if (result.Count > HierarchySelectorIndex.MaximumRegistrations)
                        return Result<ImmutableArray<GameplayBoundaryInvocation>>.Failure("Container response registration budget exceeded");
                    var activeOnly = trigger.Boundary is "StartActivation" or "EndActivation";
                    if (activeOnly && owner.InstanceId != combat.ActivationState?.ActiveActorId) continue;
                    result.Add(new()
                    {
                        Response = response,
                        Stacks = 1,
                        Kind = Kind(trigger.Boundary),
                        BoundaryId = trigger.Boundary,
                        RequiredOwner = requiredOwner,
                        RequireActiveOwner = activeOnly,
                        Order = new() { ContainerInstanceId = owned.InstanceId.ToString("D") },
                        Origin = new() { ContainerKind = "inscription", ContainerId = owned.InstanceId.ToString("D"), ComponentId = trigger.TriggerId, ContentRevision = owned.EffectiveRevision },
                        Template = new() { Run = run, Combat = combat, Trigger = trigger, OwnerEntityId = owner.InstanceId, SourceEntityId = owner.InstanceId,
                            ContentRevision = owned.EffectiveRevision, Variables = new Dictionary<string, float>(StringComparer.Ordinal) { ["stacks"] = 1f },
                            Provenance = new() { Kind = EffectProvenanceKind.Relic, SourceId = owned.InstanceId.ToString("D") } }
                    });
                }
            }
        }
        return Result<ImmutableArray<GameplayBoundaryInvocation>>.Success(result.ToImmutable());
    }

    public static GameplayBoundaryInvocation Phase(EffectTriggerExecutionRequest request, string phaseId, bool entering,
        GameplayResponseDefinition? response = null) => new()
    {
        Response = response, Template = request, Kind = entering ? "phase.entered" : "phase.exited", BoundaryId = phaseId,
        Origin = new() { ContainerKind = "rule", ContainerId = request.Provenance.SourceId,
            ComponentId = request.Trigger.TriggerId, ContentRevision = request.ContentRevision }
    };
}
