using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Run.Loadouts;

namespace Core.GameplayEvents.Responses;

public sealed class InscriptionGameplayObserverProvider(IContentRuntimeResolver runtimes) : GameplayContainerObserverProvider
{
    public override string ProviderId => "core.inscription";

    protected override Result<ImmutableArray<GameplayResponseContainerSource>> Sources(GameplayObserverCollectionContext context)
    {
        if (context.Run.Loadouts is not { } loadouts || loadouts.Actors.Count == 0)
            return Done([]);

        var sources = ImmutableArray.CreateBuilder<GameplayResponseContainerSource>();
        foreach (var (actorId, actorLoadout) in loadouts.Actors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!loadouts.Inventories.TryGetValue(loadouts.InventoryId(context.Run.RunId, actorId), out var inventory))
                continue;

            foreach (var (_, instanceId) in actorLoadout.Assignments.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var owned = inventory.Items.FirstOrDefault(item => item.InstanceId == instanceId);
                if (owned == null) continue;

                var acquired = runtimes.Resolve(owned.EffectiveRevision, context.Run.ConfigName);
                if (acquired.IsFailure)
                    return Result<ImmutableArray<GameplayResponseContainerSource>>.Failure(acquired.Error);
                if (acquired.Value.Manifest.Revision != owned.EffectiveRevision ||
                    !string.Equals(acquired.Value.Manifest.ConfigName, context.Run.ConfigName, StringComparison.OrdinalIgnoreCase))
                    return Result<ImmutableArray<GameplayResponseContainerSource>>.Failure("Inscription response setting or revision mismatch");

                var definition = acquired.Value.GetDefinition<InscriptionDefinition>("inscriptions", owned.DefinitionId);
                if (definition.IsFailure)
                    return Result<ImmutableArray<GameplayResponseContainerSource>>.Failure(definition.Error);
                if (definition.Value.Responses is not { } responses || responses.IsDefaultOrEmpty)
                    continue;

                sources.Add(new()
                {
                    Definitions = responses,
                    Owner = new() { Kind = GameplayOwnerKind.Entity, Id = actorId },
                    SourceActorId = actorId,
                    Origin = new()
                    {
                        ContainerKind = "inscription",
                        ContainerId = owned.InstanceId.ToString("D"),
                        ComponentId = "responses",
                        ContentRevision = owned.EffectiveRevision
                    }
                });
            }
        }

        return Done(sources.ToImmutable());
    }
}
