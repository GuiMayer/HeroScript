using System.Collections.Immutable;
using Core.Common;
using Core.Effects;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Run;
using Core.StatusEffects;

namespace Core.Content.Migrations;

/// <summary>Pure typed migration. Neither edits files nor publishes revisions or changes captured historical bundles.</summary>
public static class GameplayBoundaryResponseMigration
{
    public static Result<StatusEffectDefinition> Status(StatusEffectDefinition definition, GameplayEventCatalog catalog)
    {
        var converted = Convert(definition.Triggers, definition.Responses, catalog, fromSource: true);
        return converted.IsFailure ? Result<StatusEffectDefinition>.Failure(converted.Error)
            : Result<StatusEffectDefinition>.Success(definition.Triggers.Count == 0 ? definition : definition with { Triggers = [], Responses = converted.Value });
    }

    public static Result<RelicDefinition> Relic(RelicDefinition definition, GameplayEventCatalog catalog)
    {
        if (definition.Reactions is { IsEmpty: false })
            return Result<RelicDefinition>.Failure("Resource reactions require their own typed migration before the publication cutover");
        var converted = Convert(definition.Triggers, definition.Responses, catalog, fromSource: false);
        return converted.IsFailure ? Result<RelicDefinition>.Failure(converted.Error)
            : Result<RelicDefinition>.Success(definition.Triggers.Count == 0 ? definition : definition with { Triggers = [], Responses = converted.Value });
    }

    private static Result<ImmutableArray<GameplayResponseDefinition>?> Convert(IReadOnlyList<EffectTriggerDefinition> triggers,
        ImmutableArray<GameplayResponseDefinition>? existing, GameplayEventCatalog catalog, bool fromSource)
    {
        if (triggers.Count == 0) return Result<ImmutableArray<GameplayResponseDefinition>?>.Success(existing);
        if (existing is { IsEmpty: false } || existing is { IsDefault: true } || triggers.Count > 16 ||
            triggers.Select(trigger => trigger.TriggerId).Distinct(StringComparer.Ordinal).Count() != triggers.Count)
            return Result<ImmutableArray<GameplayResponseDefinition>?>.Failure("Ambiguous mixed authoring, duplicate IDs or migration budget exceeded");
        var result = ImmutableArray.CreateBuilder<GameplayResponseDefinition>();
        foreach (var trigger in triggers)
        {
            var response = new GameplayResponseDefinition
            {
                ResponseId = trigger.TriggerId, Timing = ResponseExecutionTiming.ContainerBoundary,
                Observe = new() { Kinds = new() { Include = [GameplayBoundarySources.Kind(trigger.Boundary)] },
                    Boundary = new() { BoundaryIds = [trigger.Boundary] } },
                Bindings = new() { Source = new() { Kind = fromSource ? ResponseActorBindingKind.ObserverSource : ResponseActorBindingKind.Owner },
                    Target = new() { Kind = ResponseActorBindingKind.Owner }, Missing = MissingResponseBinding.RejectAction },
                Availability = new() { RequireLivingOwner = false }, Propagation = ResponsePropagation.AllowResponses,
                Order = new() { Priority = trigger.Priority, Descending = true }, Effects = trigger.Effects.ToImmutableArray()
            };
            var timing = GameplayContainerBoundaryResponses.Validate(response);
            var compiled = GameplayResponseCompiler.Compile(response, catalog, new() { ContainerBoundary = true });
            if (timing.IsFailure || compiled.IsFailure)
                return Result<ImmutableArray<GameplayResponseDefinition>?>.Failure(timing.IsFailure ? timing.Error : compiled.Error);
            result.Add(response);
        }
        return Result<ImmutableArray<GameplayResponseDefinition>?>.Success(result.ToImmutable());
    }
}
