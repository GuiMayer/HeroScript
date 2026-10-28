using System.Collections.Immutable;
using System.Globalization;
using Core.CardZones;
using Core.Combat.Models;
using Core.Common;
using Core.Content;
using Core.Run;
using Core.Run.Content;
using Core.Run.DeckCompartments;

namespace Core.GameplayEvents.Responses;

/// <summary>Reads the same effective container and compartment contributions as card execution. Zone purpose is irrelevant.</summary>
public sealed class CardGameplayObserverProvider(IContentRuntimeResolver runtimes) : GameplayContainerObserverProvider
{
    public override string ProviderId => "core.card";

    protected override Result<ImmutableArray<GameplayResponseContainerSource>> Sources(GameplayObserverCollectionContext context)
    {
        if (context.Run.Deck.Topology.Instances.Count == 0) return Done([]);
        if (context.Run.Deck.Topology.Instances.Count > HierarchySelectorIndex.MaximumRegistrations)
            return Result<ImmutableArray<GameplayResponseContainerSource>>.Failure("Card observer scan exceeds its input budget");
        var runtime = runtimes.Resolve(context.Run.Determinism.ContentRevision, context.Run.ConfigName);
        if (runtime.IsFailure) return Result<ImmutableArray<GameplayResponseContainerSource>>.Failure(runtime.Error);
        var compiler = new CardContentCompiler(context.Catalog);
        var resolver = new EffectiveCardResolver(compiler);
        var definitions = new Dictionary<string, CompiledCardDefinition>(StringComparer.Ordinal);
        var sources = ImmutableArray.CreateBuilder<GameplayResponseContainerSource>();
        foreach (var instance in context.Run.Deck.Topology.Instances.Values.OrderBy(card => card.CreationOrdinal).ThenBy(card => card.CardInstanceId))
        {
            if (!definitions.TryGetValue(instance.DefinitionId, out var definition))
            {
                var compiled = compiler.Compile(instance.DefinitionId, runtime.Value);
                if (compiled.IsFailure) return Result<ImmutableArray<GameplayResponseContainerSource>>.Failure(compiled.Error);
                definitions.Add(instance.DefinitionId, definition = compiled.Value);
            }
            var effective = DeckCompartmentComposition.Resolve(context.Run, runtime.Value, definition, instance, resolver, compiler);
            if (effective.IsFailure) return Result<ImmutableArray<GameplayResponseContainerSource>>.Failure(effective.Error);
            foreach (var component in effective.Value.Components.OfType<CardResponseComponentDefinition>())
            {
                var zone = context.Run.Deck.Topology.FindZone(instance.CardInstanceId);
                var zoneDefinition = context.Run.ResolvedMode?.CardZoneSystem?.Zones.SingleOrDefault(item => item.ZoneId == zone?.Address.ZoneId);
                if (zone == null || zoneDefinition == null || component.Response == null)
                    return Result<ImmutableArray<GameplayResponseContainerSource>>.Failure("Card observer has no effective response or pinned zone");
                GameplayOwner? owner = zoneDefinition.OwnerScope switch
                {
                    CardZoneOwnerScope.Actor => new() { Kind = GameplayOwnerKind.Entity, Id = zone.Address.OwnerId },
                    CardZoneOwnerScope.RunOwner => new() { Kind = GameplayOwnerKind.Run, Id = context.Run.RunId.ToString("D") },
                    CardZoneOwnerScope.Global => null,
                    _ => null
                };
                var contributors = Contributors(instance, effective.Value, component.ComponentId);
                if (contributors.IsFailure) return Result<ImmutableArray<GameplayResponseContainerSource>>.Failure(contributors.Error);
                sources.Add(new() { Definitions = [component.Response], Owner = owner, CardInstanceId = instance.CardInstanceId,
                    Contributors = contributors.Value, Origin = new() { ContainerKind = "card", ContainerId = instance.CardInstanceId.ToString("D"),
                        ComponentId = component.ComponentId, ContentRevision = contributors.Value.LastOrDefault()?.ContentRevision ?? runtime.Value.Manifest.Revision } });
                if (sources.Count > HierarchySelectorIndex.MaximumRegistrations)
                    return Result<ImmutableArray<GameplayResponseContainerSource>>.Failure("Card observer components exceed budget");
            }
        }
        return Done(sources);
    }

    private static Result<ImmutableArray<GameplayEventOrigin>> Contributors(CardInstanceState instance, EffectiveCardDefinition effective, string componentId)
    {
        var result = ImmutableArray.CreateBuilder<GameplayEventOrigin>();
        foreach (var trace in effective.UpgradeTrace.Where(trace => trace.ComponentId == componentId))
        {
            var upgrade = effective.AppliedUpgrades.SingleOrDefault(upgrade => upgrade.TransformationId == trace.TransformationId);
            if (upgrade == null) return Result<ImmutableArray<GameplayEventOrigin>>.Failure("Card observer trace lost its captured upgrade");
            result.Add(new() { ContainerKind = "upgrade", ContainerId = trace.TransformationId.ToString(CultureInfo.InvariantCulture),
                ComponentId = trace.UpgradeId, ContentRevision = upgrade.ContentRevision });
        }
        foreach (var trace in effective.CompositionTrace.Where(trace => trace.ComponentIds.Contains(componentId, StringComparer.Ordinal)))
        {
            var upgrade = effective.AppliedUpgrades.SingleOrDefault(upgrade => upgrade.TransformationId == trace.TransformationId);
            if (upgrade == null) return Result<ImmutableArray<GameplayEventOrigin>>.Failure("Card observer composition lost its captured revision");
            result.Add(new() { ContainerKind = "composition", ContainerId = trace.TransformationId.ToString(CultureInfo.InvariantCulture),
                ComponentId = trace.RuleId, ContentRevision = upgrade.ContentRevision });
        }
        foreach (var trace in effective.ComponentContributions ?? [])
            if (trace.ComponentIds.Contains(componentId, StringComparer.Ordinal))
                result.Add(new() { ContainerKind = "contribution", ContainerId = trace.SourceId,
                    ComponentId = componentId, ContentRevision = trace.ContentRevision });
        return result.Count > 64 ? Result<ImmutableArray<GameplayEventOrigin>>.Failure("Card observer provenance exceeds budget")
            : Result<ImmutableArray<GameplayEventOrigin>>.Success(result.Distinct().ToImmutableArray());
    }
}
