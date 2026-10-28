using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;
using Core.Effects;

namespace Core.GameplayEvents.Responses;

/// <summary>Read-only migration of captured historical definitions. Does not distribute effects or interpret resource names.</summary>
internal sealed class LegacyResourceReactionObserverProvider : IGameplayObserverProvider
{
    public string ProviderId => "core.migrated-resource-reactions";

    public Result<ImmutableArray<GameplayObserverRegistration>> Collect(GameplayObserverCollectionContext context)
    {
        var registrations = ImmutableArray.CreateBuilder<GameplayObserverRegistration>();
        var actors = GameplayResponseSourceCompiler.Actors(context);
        foreach (var relic in context.Run.Relics)
        {
            var valid = EffectReactionPolicies.Validate(relic.Reactions ?? []);
            if (valid.IsFailure) return Result<ImmutableArray<GameplayObserverRegistration>>.Failure(valid.Error);
            foreach (var reaction in relic.Reactions ?? [])
            {
                // Historical contracts allowed contradictory tags. They were valid but never eligible.
                if (reaction.RequiredTags.Intersect(reaction.ExcludedTags, StringComparer.Ordinal).Any()) continue;
                var definition = new GameplayResponseDefinition
                {
                    ResponseId = reaction.ReactionId,
                    Observe = new()
                    {
                        Kinds = new() { Include = ["resource.change.resolved"] },
                        Reasons = new() { Include = ["**"], Exclude = ["cost.payment", "cost.refund"] },
                        Source = new() { MatchesOwner = reaction.OwnerScope == EffectReactionOwnerScope.Source ? true : null },
                        Subject = new() { MatchesOwner = reaction.OwnerScope == EffectReactionOwnerScope.Target ? true : null },
                        SourceSubjectRelationship = reaction.TargetRelationship,
                        Resource = new() { ResourceIds = [reaction.ResourceId], Field = reaction.ResourceField,
                            Operation = reaction.Operation, Role = ResourceOccurrenceRole.PrimaryEffect,
                            CalculationChannels = reaction.Channels.Distinct(StringComparer.Ordinal).ToImmutableArray() },
                        RequiredTags = reaction.RequiredTags.Distinct(StringComparer.Ordinal).ToImmutableArray(),
                        ExcludedTags = reaction.ExcludedTags.Distinct(StringComparer.Ordinal).ToImmutableArray()
                    },
                    Bindings = new() { Target = new() { Kind = reaction.Selection == EffectReactionSelection.Source
                        ? ResponseActorBindingKind.EventSource : ResponseActorBindingKind.EventSubject } },
                    Availability = new() { Snapshot = ResponseSnapshot.AfterOccurrence, RequireLivingOwner = reaction.RequireLivingOwner },
                    Propagation = ResponsePropagation.RecordOnly, Effects = reaction.Effects
                };
                var compiled = GameplayResponseCompiler.Compile(definition, context.Catalog);
                if (compiled.IsFailure) return Result<ImmutableArray<GameplayObserverRegistration>>.Failure(compiled.Error);
                foreach (var owner in actors.Where(actor => relic.Owner.Includes(actor, context.Combat, context.Run)))
                {
                    if (registrations.Count == HierarchySelectorIndex.MaximumRegistrations)
                        return Result<ImmutableArray<GameplayObserverRegistration>>.Failure("Migrated reaction observer budget exceeded");
                    var origin = new GameplayEventOrigin { ContainerKind = "relic", ContainerId = relic.RelicInstanceId.ToString("D"),
                        ComponentId = reaction.ReactionId, ContentRevision = relic.ContentRevision };
                    var id = CanonicalJson.ComputeHash(new { ProviderId, origin, owner = owner.InstanceId, reaction });
                    registrations.Add(new()
                    {
                        ProviderId = ProviderId, RegistrationId = id, Response = compiled.Value, Origin = origin,
                        OwnerActorId = owner.InstanceId, Stacks = relic.Stacks,
                        Order = new() { Group = reaction.Priority, ContainerInstanceId = relic.RelicInstanceId.ToString("D") }
                    });
                }
            }
        }
        return Result<ImmutableArray<GameplayObserverRegistration>>.Success(registrations.ToImmutable());
    }
}
