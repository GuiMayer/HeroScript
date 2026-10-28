using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;

namespace Core.GameplayEvents.Responses;

public sealed class ModeGameplayObserverProvider : GameplayContainerObserverProvider
{
    public override string ProviderId => "core.mode";
    protected override Result<ImmutableArray<GameplayResponseContainerSource>> Sources(GameplayObserverCollectionContext context)
    {
        var mode = context.Run.ResolvedMode?.Definition;
        return Done(mode?.Responses is { } responses ? [new() { Definitions = responses,
            Origin = new() { ContainerKind = "mode", ContainerId = mode.ModeId, ComponentId = "responses", ContentRevision = context.Run.Determinism.ContentRevision } }] : []);
    }
}

public sealed class EncounterGameplayObserverProvider : GameplayContainerObserverProvider
{
    public override string ProviderId => "core.encounter";
    protected override Result<ImmutableArray<GameplayResponseContainerSource>> Sources(GameplayObserverCollectionContext context)
    {
        var node = context.Run.Map.Nodes.SingleOrDefault(node => node.NodeId == context.Combat?.RunNodeId);
        return Done(node?.Activity.Type == Core.Run.RunActivityType.Encounter && node.Responses is { } responses ? [new()
        { Definitions = responses, Origin = new() { ContainerKind = "encounter", ContainerId = context.Combat!.CombatId.ToString("D"),
            ComponentId = node.NodeId, ContentRevision = node.ResponseContentRevision ?? string.Empty } }] : []);
    }
}

public sealed class ActorGameplayObserverProvider : GameplayContainerObserverProvider
{
    public override string ProviderId => "core.actor";
    protected override Result<ImmutableArray<GameplayResponseContainerSource>> Sources(GameplayObserverCollectionContext context) => Done(
        GameplayResponseSourceCompiler.Actors(context).SelectMany(actor => actor.Components.Values.OfType<EntityResponseComponentState>()
            .Where(component => component.Responses != null).Select(component => new GameplayResponseContainerSource
            { Definitions = component.Responses!.Value, Owner = new() { Kind = GameplayOwnerKind.Entity, Id = actor.InstanceId },
                SourceActorId = actor.InstanceId, Origin = new() { ContainerKind = "entity", ContainerId = actor.InstanceId,
                    ComponentId = component.ComponentId, ContentRevision = actor.ContentRevision } })));
}

public sealed class RelicGameplayObserverProvider : GameplayContainerObserverProvider
{
    public override string ProviderId => "core.relic";
    protected override Result<ImmutableArray<GameplayResponseContainerSource>> Sources(GameplayObserverCollectionContext context) => Done(
        context.Run.Relics.Where(relic => relic.Responses != null).OrderBy(relic => relic.RelicInstanceId).Select(relic => new GameplayResponseContainerSource
        { Definitions = relic.Responses!.Value, Owner = relic.Owner, Stacks = relic.Stacks,
            Origin = new() { ContainerKind = "relic", ContainerId = relic.RelicInstanceId.ToString("D"), ComponentId = "responses", ContentRevision = relic.ContentRevision } }));
}

public sealed class StatusGameplayObserverProvider : GameplayContainerObserverProvider
{
    public override string ProviderId => "core.status";
    protected override Result<ImmutableArray<GameplayResponseContainerSource>> Sources(GameplayObserverCollectionContext context) => Done(
        context.Combat == null ? [] : context.Combat.StatusEffects.OrderBy(pair => pair.Key, StringComparer.Ordinal).SelectMany(pair => pair.Value
            .Where(status => status.IsActive && status.Definition.Responses != null).OrderBy(status => status.InstanceId)
            .Select(status => new GameplayResponseContainerSource { Definitions = status.Definition.Responses!.Value,
                Owner = new() { Kind = GameplayOwnerKind.Entity, Id = status.TargetId }, SourceActorId = status.SourceId,
                Stacks = status.Stacks, Duration = status.Duration, Origin = new() { ContainerKind = "status", ContainerId = status.InstanceId.ToString("D"),
                    ComponentId = "responses", ContentRevision = status.ContentRevision ?? string.Empty } })));
}
